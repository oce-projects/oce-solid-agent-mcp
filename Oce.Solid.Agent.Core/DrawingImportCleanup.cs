using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// WHAT `model_dimensions` BRINGS THAT SHOULD NOT STAY (0.131.0, items 3 and 5 of the drawing branch).
    ///
    /// ── WHAT THE SHEET OF 12/10/2026 SHOWED, AND WHAT WAS MEASURED ─────────────────────
    ///
    /// After `model_dimensions` with all_dimensions the L bracket sheet had (Checks/Draw131 «dims» and «lines», on the
    /// saved sheet, 12/10/2026):
    ///  - the diameter Ø11 twice: the hole callout «2 x Ø11 THRU» (RD1, from the hole wizard) and a diameter dimension of the
    ///    sketch of the hole («Thru Hole Dia.@Schizzo11», prefix «&lt;MOD-DIAM&gt;»), against «each element is dimensioned
    ///    once» (UNI ISO 129-1);
    ///  - the position dimensions 90 and 30 of the holes (D2 and D1@Schizzo12) with the SAME dimension line: both horizontal, both
    ///    at y 140.69 mm on the sheet, from x 64.5 and 124.5 to the free edge at 154.5. They overlap from 124.5 to 154.5:
    ///    SOLIDWORKS puts every dimension it imports at the same distance from the edge it measures from.
    ///
    /// The «dimensions_orphan» the state counts on those two views are NOT dimensions that hang from nothing: they are the
    /// dimensions attached to the sketch of a feature (types 10 and 11, sketch segments and points), which the visible edges
    /// of the view can never match. Removing «the dimensions that hang from nothing» would take away the R5.5, the 19 and the 35
    /// of the slot, and it is not done.
    ///
    /// ── WHAT IT DOES ──────────────────────────────────────────────────────────────────────────
    ///
    /// Only to the dimensions THIS call imported (the names that were not on the drawing before):
    ///  1. a diameter dimension of the model whose diameter a hole callout in the same view already says is taken away
    ///     (the callout carries the diameter, the count and the depth);
    ///  2. an imported linear dimension with the direction and the value of one that the drawing already had in the same view and
    ///     that was made in the drawing (its name ends in «.Drawing»: the model's own end in «.Part») is taken away: the model's depth
    ///     of 60 next to the overall 60 that drawing_dimension has put, the 30 and the 90 of the hole wizard next to the positions that
    ///     drawing_dimension has put from the outline of the part. An element is dimensioned once;
    ///  2b. an imported dimension of a CHAMFER FEATURE (its «Dn@feature@model» names a chamfer) is taken away when a chamfer
    ///     dimension that was on the drawing before the call, in ANY view of the same model, already says that chamfer (bridge
    ///     0.171.5): on the L bracket drawing_dimension put 1 x 45° in the front view and the import brought the 1 and the 45°
    ///     of the same chamfer into the side view. A chamfer is dimensioned once on the whole drawing, as drawing_dimension
    ///     already held it (the key «chamfer|feature» of KeysAlreadyDimensioned);
    ///  3. linear dimensions that lie on the same dimension line and overlap are moved apart: the shortest stays, the others go
    ///     outward one pitch each (IAnnotation~SetPosition2 on the text: the dimension line follows it), and the new place is
    ///     read back.
    /// Each removal is counted again (the dimension is no longer there), each move is read back, and the answer lists what was
    /// done: nothing is removed or moved silently, and nothing that was on the drawing before the call is touched.
    ///
    /// GUIDE PAGES: IDisplayDimension~IsHoleCallout, IDisplayDimension~GetText (the prefix), IDisplayDimension~GetDisplayData
    /// with IDisplayData~GetLineAtIndex3 (the lines SOLIDWORKS draws: [colour, type, style, weight, start xyz, end xyz]),
    /// IAnnotation~Select3 and IModelDocExtension~DeleteSelection2, IAnnotation~GetPosition and SetPosition2, IDisplayData~GetArrowHeadAtIndex2.
    /// </summary>
    public partial class SwOperations
    {
        private static readonly CultureInfo Ct = CultureInfo.InvariantCulture;

        /// <summary>The full names of every dimension of the drawing, all the views and the sheet.</summary>
        private static HashSet<string> DimensionNamesOfTheDrawing(IDrawingDoc drawing)
        {
            var outside = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    var d = v.GetFirstDisplayDimension5() as IDisplayDimension;
                    while (d != null)
                    {
                        try
                        {
                            var named = d.GetDimension2(0) as IDimension;
                            string full = named == null ? null : named.FullName;
                            if (!string.IsNullOrEmpty(full)) outside.Add(full);
                        }
                        catch { }
                        d = d.GetNext5() as IDisplayDimension;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return outside;
        }

        private sealed class ImportedDimension
        {
            public IView View;
            public string ViewName;
            public IDisplayDimension Display;
            public string Name;
            public bool Imported;
            public bool IsCallout;
            public bool IsDiameter;
            public double Mm = double.NaN;     // NaN: could not be read (not zero)
            // an angle: its value in degrees (Mm then holds the radians times 1000, and is not reported)
            public bool IsAngle;
            public double Deg = double.NaN;
            // the chamfer this dimension says (bridge 0.171.5): for a chamfer dimension the chamfer feature of the edges it rests
            // on, for a dimension of the model the chamfer feature it is a parameter of («Dn@feature@model»); null otherwise
            public string ChamferOf;
            public bool ChamferDimension;
            public string Model;               // ApiHelpers.ReferencedModelKey of its view, null when it cannot be read
            // the dimension line, when the dimension is linear and its line passes through its text
            public bool HasRow;
            public bool Horizontal;
            public double Row, Lo, Hi;         // metres on the sheet
        }

        private sealed class Victim
        {
            public ImportedDimension Row;
            public string Why;
        }

        /// <summary>
        /// Takes away what the import brought that must not stay, moves apart the dimensions that overlap, and reports it
        /// in <paramref name="measurements"/>. Never touches a dimension whose name was in <paramref name="before"/>.
        /// </summary>
        private void TidyImportedDimensions(
            IModelDoc2 model, IDrawingDoc drawing, HashSet<string> before, Dictionary<string, object> measurements)
        {
            var removed = new List<object>();
            var moved = new List<object>();
            var problems = new List<string>();
            try
            {
                var rows = new List<ImportedDimension>();
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    IView view = v;
                    string viewName = "";
                    try { viewName = view.GetName2() ?? ""; } catch { }
                    string viewModel = null;
                    try { viewModel = ApiHelpers.ReferencedModelKey(view); } catch { }
                    var d = view.GetFirstDisplayDimension5() as IDisplayDimension;
                    while (d != null)
                    {
                        IDisplayDimension current = d;
                        d = d.GetNext5() as IDisplayDimension;
                        try
                        {
                            var dim = current.GetDimension2(0) as IDimension;
                            string full = dim == null ? null : dim.FullName;
                            var row = new ImportedDimension { View = view, ViewName = viewName, Display = current, Name = full ?? "" };
                            row.Imported = !string.IsNullOrEmpty(full) && !before.Contains(full);
                            try { row.IsCallout = current.IsHoleCallout(); } catch { }
                            row.IsDiameter = !row.IsCallout && IsDiameterDimension(current);
                            if (dim != null)
                                try { row.Mm = Math.Round(ApiHelpers.ReadDimensionInConfiguration(dim, "") * 1000.0, 3); } catch { }
                            try { row.IsAngle = dim != null && dim.GetType() == (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular; } catch { }
                            if (row.IsAngle && !double.IsNaN(row.Mm)) row.Deg = Math.Round(row.Mm / 1000.0 * 180.0 / Math.PI, 2);
                            row.Model = viewModel;
                            try { row.ChamferDimension = current.Type2 == (int)swDimensionType_e.swChamferDimension; } catch { }
                            row.ChamferOf = row.ChamferDimension ? ChamferOfItsEdges(current) : ChamferFeatureOfModelDimension(view, full);
                            rows.Add(row);
                        }
                        catch { }
                    }
                    v = v.GetNextView() as IView;
                }

                // 1. the diameter a hole callout already carries
                var victims = new List<Victim>();
                foreach (ImportedDimension row in rows)
                {
                    if (!row.Imported || !row.IsDiameter || double.IsNaN(row.Mm)) continue;
                    foreach (ImportedDimension c in rows)
                        if (c.IsCallout && c.ViewName == row.ViewName && !double.IsNaN(c.Mm) && Math.Abs(c.Mm - row.Mm) < 0.01)
                        {
                            victims.Add(new Victim
                            {
                                Row = row,
                                Why = "the hole callout «" + c.Name + "» in the same view already says Ø" + row.Mm.ToString("0.##", Ct) +
                                      ": a hole is dimensioned once",
                            });
                            break;
                        }
                }
                // 1b. the dimension a dimension of the drawing already says: an imported linear dimension with the direction and the value
                // of one that was on the drawing before the call and was made in the drawing (its name ends in «.Drawing», the model's own
                // end in «.Part»), in the same view. The model's depth of 60 imported next to the overall 60 that drawing_dimension put.
                foreach (ImportedDimension r in rows) ReadDimensionRow(r);
                foreach (ImportedDimension row in rows)
                {
                    if (!row.Imported || !row.HasRow || double.IsNaN(row.Mm) || victims.Exists(q => ReferenceEquals(q.Row, row))) continue;
                    foreach (ImportedDimension p in rows)
                        if (!p.Imported && p.HasRow && p.ViewName == row.ViewName && p.Horizontal == row.Horizontal && !double.IsNaN(p.Mm) &&
                            Math.Abs(p.Mm - row.Mm) < 0.01 && p.Name.EndsWith(".Drawing", StringComparison.OrdinalIgnoreCase))
                        {
                            victims.Add(new Victim
                            {
                                Row = row,
                                Why = "the dimension «" + p.Name + "» of this view already says " + row.Mm.ToString("0.##", Ct) +
                                      (row.Horizontal ? " horizontally" : " vertically") + ": an element is dimensioned once",
                            });
                            break;
                        }
                }
                // 1c. A CHAMFER THE DRAWING ALREADY DIMENSIONS, IN ANY VIEW (bridge 0.171.5). The L bracket: drawing_dimension put the
                // chamfer dimension 1 x 45° in the front view, and the import brought the 1 and the 45° of the same chamfer feature
                // into the side view. A chamfer is dimensioned once on the whole drawing, not once per view (the owner's rule of
                // 02/11/2026, UNI ISO 129-1); drawing_dimension keeps the same rule with the key «chamfer|feature»
                // (KeysAlreadyDimensioned). Only a CHAMFER DIMENSION that was on the drawing before the call, shown, of the same model,
                // makes an imported dimension of that chamfer feature go: the import alone is never judged against itself.
                foreach (ImportedDimension row in rows)
                {
                    if (!row.Imported || row.ChamferOf == null || victims.Exists(q => ReferenceEquals(q.Row, row))) continue;
                    foreach (ImportedDimension c in rows)
                    {
                        if (c.Imported || !c.ChamferDimension || c.ChamferOf != row.ChamferOf || !ApiHelpers.MayShowTheSameModel(c.Model, row.Model)) continue;
                        bool hidden = false;
                        try { hidden = ApiHelpers.IsHiddenByHand(c.Display); } catch { }
                        if (hidden) continue;
                        victims.Add(new Victim
                        {
                            Row = row,
                            Why = "the chamfer dimension «" + c.Name + "» of the view «" + c.ViewName + "» already says the chamfer «" +
                                  row.ChamferOf + "»" + ChamferValuesText(c.Display) + ": a chamfer is dimensioned once on the whole drawing, not once per view",
                        });
                        break;
                    }
                }
                foreach (Victim p in victims)
                {
                    ImportedDimension gone = p.Row;
                    int beforeCount = CountDimensions(drawing);
                    bool deleted = false;
                    try
                    {
                        model.ClearSelection2(true);
                        IDisplayDimension victimDisplay = gone.Display;
                        IAnnotation ann = victimDisplay.GetAnnotation() as IAnnotation;
                        if (ann != null && ann.Select3(false, null))
                            deleted = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                        model.ClearSelection2(true);
                    }
                    catch { }
                    int afterCount = CountDimensions(drawing);
                    if (beforeCount >= 0 && afterCount >= 0 && afterCount < beforeCount)
                    {
                        gone.Display = null;
                        var removedRow = new Dictionary<string, object>
                        {
                            ["name"] = gone.Name,
                            ["view"] = gone.ViewName,
                            // an angle has no millimetres: its degrees go under «deg» (bridge 0.171.5)
                            ["mm"] = double.IsNaN(gone.Mm) || gone.IsAngle ? null : (object)gone.Mm,
                            ["why"] = p.Why,
                        };
                        if (gone.IsAngle && !double.IsNaN(gone.Deg)) removedRow["deg"] = gone.Deg;
                        removed.Add(removedRow);
                    }
                    else
                        problems.Add("«" + gone.Name + "» should have been taken away (" + p.Why + ") and is still there (dimensions " +
                                     beforeCount + " -> " + afterCount + ", the call said " + deleted + ")");
                }

                // 2. the dimensions on the same dimension line
                MoveApartTheOverlapping(model, rows, moved, problems);
            }
            catch (Exception ex) { problems.Add("the tidy-up stopped: " + ex.Message); }

            measurements["imported_dimensions_removed"] = removed.ToArray();
            measurements["dimensions_moved_apart"] = moved.ToArray();
            if (problems.Count > 0) measurements["tidy_up_problems"] = problems.ToArray();
        }

        /// <summary>
        /// The dimension line of a linear dimension, read from where SOLIDWORKS puts its two arrowheads (IDisplayData~GetArrowHeadAtIndex2:
        /// [tip xyz, direction xyz, width, height, style, normal xyz], metres on the sheet): the line between the two tips is the
        /// dimension line, horizontal when the tips are at the same height, vertical when they are at the same x. The position of the
        /// text is NOT the line (the text stands a little above it: 140.69 against 137.91 measured on the L bracket), and the longest
        /// line is not either (an extension line can be longer than a short dimension). False for what has not two arrowheads in
        /// line (a radius, a diameter, an angle, a callout).
        /// </summary>
        private static bool ReadDimensionRow(ImportedDimension row)
        {
            try
            {
                if (row.Display == null || row.IsCallout || row.IsDiameter) return false;
                IDisplayDimension itsDimension = row.Display;
                IDisplayData data = itsDimension.GetDisplayData() as IDisplayData;
                if (data == null || data.GetArrowHeadCount() != 2) return false;
                var t0 = data.GetArrowHeadAtIndex2(0) as double[];
                var t1 = data.GetArrowHeadAtIndex2(1) as double[];
                if (t0 == null || t1 == null || t0.Length < 2 || t1.Length < 2) return false;
                bool sameY = Math.Abs(t0[1] - t1[1]) < 1e-7, sameX = Math.Abs(t0[0] - t1[0]) < 1e-7;
                if (sameY == sameX) return false;
                row.HasRow = true;
                row.Horizontal = sameY;
                row.Row = sameY ? t0[1] : t0[0];
                row.Lo = sameY ? Math.Min(t0[0], t1[0]) : Math.Min(t0[1], t1[1]);
                row.Hi = sameY ? Math.Max(t0[0], t1[0]) : Math.Max(t0[1], t1[1]);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Linear dimensions of one view on the same line that overlap: the shortest stays where it is, each of the others goes
        /// outward (away from the middle of the view) to the first row beyond all the rows of the same direction that
        /// overlap it, one pitch apart. Only dimensions this call imported are moved.
        /// </summary>
        private void MoveApartTheOverlapping(IModelDoc2 model, List<ImportedDimension> rows, System.Collections.IList moved, System.Collections.IList problems)
        {
            const double pitch = 0.010;
            foreach (ImportedDimension r in rows) ReadDimensionRow(r);
            var byView = new Dictionary<string, List<ImportedDimension>>(StringComparer.Ordinal);
            foreach (ImportedDimension r in rows)
            {
                if (!r.HasRow) continue;
                if (!byView.ContainsKey(r.ViewName)) byView[r.ViewName] = new List<ImportedDimension>();
                System.Collections.IList itsList = byView[r.ViewName];
                itsList.Add(r);
            }
            foreach (string viewKey in byView.Keys)
            {
                List<ImportedDimension> inView = byView[viewKey];
                double[] outline = null;
                try { outline = inView[0].View.GetOutline() as double[]; } catch { }
                foreach (bool horizontal in new[] { true, false })
                {
                    var same = inView.FindAll(r => r.Horizontal == horizontal);
                    same.Sort((p, q) => (p.Hi - p.Lo).CompareTo(q.Hi - q.Lo));
                    var done = new List<ImportedDimension>();   // the ones that keep their row, shortest first
                    foreach (ImportedDimension r in same)
                    {
                        ImportedDimension clash = done.Find(o => Math.Abs(o.Row - r.Row) < 0.0005 &&
                                                                 Math.Min(o.Hi, r.Hi) - Math.Max(o.Lo, r.Lo) > 0.0005);
                        if (clash == null) { done.Add(r); continue; }
                        if (!r.Imported) { done.Add(r); continue; }   // never moves what was there before

                        double middle = outline == null || outline.Length < 4 ? r.Row
                                        : horizontal ? (outline[1] + outline[3]) / 2 : (outline[0] + outline[2]) / 2;
                        double direction = r.Row >= middle ? 1 : -1;
                        double target = r.Row;
                        foreach (ImportedDimension o in done)
                            if (Math.Min(o.Hi, r.Hi) - Math.Max(o.Lo, r.Lo) > 0.0005 && direction * (o.Row - target) >= -0.0005)
                                target = o.Row + direction * pitch;
                        double was = r.Row;
                        bool ok = false;
                        try
                        {
                            // The text goes by as much as the line has to go: the position of the text is not the line.
                            IDisplayDimension itsDimension = r.Display;
                            IAnnotation ann = itsDimension.GetAnnotation() as IAnnotation;
                            var pos = ann == null ? null : ann.GetPosition() as double[];
                            if (pos != null && pos.Length >= 3)
                            {
                                double nx = horizontal ? pos[0] : pos[0] + (target - was), ny = horizontal ? pos[1] + (target - was) : pos[1];
                                ok = ann.SetPosition2(nx, ny, pos[2]);
                            }
                            try { model.EditRebuild3(); } catch { }
                        }
                        catch { }
                        // READ BACK: where the dimension line really is now.
                        r.HasRow = false;
                        bool again = ReadDimensionRow(r);
                        double now = again ? r.Row : double.NaN;
                        if (again && Math.Abs(now - target) < 0.0005)
                        {
                            moved.Add(new Dictionary<string, object>
                            {
                                ["name"] = r.Name,
                                ["view"] = r.ViewName,
                                ["mm"] = double.IsNaN(r.Mm) ? null : (object)r.Mm,
                                ["direction"] = horizontal ? "horizontal dimension line" : "vertical dimension line",
                                ["from_mm"] = Math.Round(was * 1000.0, 2),
                                ["to_mm"] = Math.Round(now * 1000.0, 2),
                                ["apart_from"] = clash.Name,
                            });
                            done.Add(r);
                        }
                        else
                            problems.Add("«" + r.Name + "» overlaps «" + clash.Name + "» on the line at " + Math.Round(was * 1000.0, 2).ToString(Ct) +
                                         " mm and I could not move it to " + Math.Round(target * 1000.0, 2).ToString(Ct) + " mm (the call said " + ok +
                                         ", the line is now " + (again ? Math.Round(now * 1000.0, 2).ToString(Ct) + " mm" : "unreadable") + "): look at it on the sheet");
                    }
                }
            }
        }

        /// <summary>
        /// The chamfer feature a chamfer dimension says: the chamfer feature of the first edge it rests on that touches one
        /// (TouchesAChamfer, as KeysAlreadyDimensioned reads it); null when no edge can be read.
        /// </summary>
        private static string ChamferOfItsEdges(IDisplayDimension d)
        {
            try
            {
                var ann = d.GetAnnotation() as IAnnotation;
                var ents = ann == null ? null : ann.GetAttachedEntities3() as object[];
                if (ents == null) return null;
                foreach (object o in ents)
                {
                    var e = o as IEdge;
                    string feature = e == null ? null : TouchesAChamfer(e);
                    if (feature != null) return feature;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// The chamfer feature a dimension of the model is a parameter of: its full name is «Dn@feature@model.Part», and the
        /// feature of that name in the part of the view is a chamfer (IFeature~GetTypeName2 «Chamfer», the same in every
        /// language of SOLIDWORKS). Null for a dimension made in the drawing (its name ends in «.Drawing»), for a feature that is
        /// not a chamfer, and when the part cannot be read (an assembly view: its feature names carry the component).
        /// </summary>
        private static string ChamferFeatureOfModelDimension(IView view, string fullName)
        {
            if (string.IsNullOrEmpty(fullName) || fullName.EndsWith(".Drawing", StringComparison.OrdinalIgnoreCase)) return null;
            string[] parts = fullName.Split('@');
            if (parts.Length < 2 || string.IsNullOrEmpty(parts[1])) return null;
            try
            {
                IView itsView = view;
                IPartDoc part = null;
                for (int level = 0; itsView != null && part == null && level < 8; level++)
                {
                    part = itsView.ReferencedDocument as IPartDoc;
                    if (part == null) itsView = itsView.GetBaseView() as IView;
                }
                var feature = part == null ? null : part.FeatureByName(parts[1]) as IFeature;
                return feature != null && feature.GetTypeName2() == "Chamfer" ? feature.Name : null;
            }
            catch { return null; }
        }

        /// <summary>« (1 x 45°)»: the leg and the angle a chamfer dimension reads, as PlaceChamferDimension reads them; empty when unreadable.</summary>
        private static string ChamferValuesText(IDisplayDimension d)
        {
            double? angle = null, width = null;
            for (int k = 0; k < 2 && d != null; k++)
            {
                try
                {
                    var dk = d.GetDimension2(k) as IDimension;
                    if (dk == null) continue;
                    int typ = dk.GetType();
                    if (typ == (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular) angle = ApiHelpers.ReadDimensionInConfiguration(dk, "");
                    else if (typ == (int)swDimensionParamType_e.swDimensionParamTypeDoubleLinear) width = ApiHelpers.ReadDimensionInConfiguration(dk, "");
                }
                catch { }
            }
            if (!angle.HasValue || !width.HasValue) return "";
            double radians = angle.GetValueOrDefault(), metres = width.GetValueOrDefault();
            double leg = Math.Round(metres * Math.Cos(radians) * 1000.0, 3);
            return " (" + leg.ToString("0.###", Ct) + " x " + Math.Round(radians * 180.0 / Math.PI, 2).ToString("0.##", Ct) + "°)";
        }

        /// <summary>
        /// Is the display dimension a diameter dimension? The prefix SOLIDWORKS writes for it carries the diameter symbol
        /// (the tag «&lt;MOD-DIAM&gt;»), measured on «Thru Hole Dia.@Schizzo11» of the L bracket (IDisplayDimension~GetText).
        /// </summary>
        private static bool IsDiameterDimension(IDisplayDimension dd)
        {
            try
            {
                string prefix = dd.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? "";
                return prefix.IndexOf("<MOD-DIAM>", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }
    }
}
