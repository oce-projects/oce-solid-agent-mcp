using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE HOLE TABLE ON THE DRAWING (entry GT5).
    ///
    /// ── MEASURED BEFORE WRITING IT, 26/9/2026 (`probe0089 holetable`) ────
    ///
    /// `IView.InsertHoleTable2(UseAnchorPoint, X, Y, AnchorType, StartValue,
    /// TableTemplate)`, and `InsertHoleTable3` that replaces it since Z-C21.7,
    /// have no argument for the origin or the face: they are SELECTED first, in
    /// the view (`ISelectData.View`). Measured with InsertHoleTable2 on a
    /// 120 x 80 plate with four D8 holes 15 from the edges, top view:
    ///
    ///   corner vertex with mark 1, drilled face with mark 2
    ///       -> 5 rows: A1 (15,15), A2 (15,65), A3 (105,15), A4 (105,65),
    ///          all «Ø8 PASSANTE»: the positions from the corner, right
    ///   marks 2/1, or 0/0
    ///       -> the table is BORN EMPTY (the header only), without an error
    ///
    /// The Z-C21.3 probe probe_hole_table3 found the same with InsertHoleTable3
    /// (swapped marks, H3: the header only). Hence the operation wants
    /// rows = holes + 1, and removes and refuses a table born empty. And it
    /// reads every row back against the model: the position of each hole from
    /// the origin, measured on the view, must be in the table.
    ///
    /// On 17/9 no table was born on this installation (GT2): on 26/9 they
    /// are all born except the cut list. See entry GT2 of the TODO.
    /// </summary>
    public partial class SwOperations
    {
        private sealed class HoleInView
        {
            public double X, Y;          // centre on the sheet, metres
            public double DiameterMm;
        }

        private Outcome HoleTable(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed("The active document is not a drawing: the hole table goes on a view. First " +
                               "create the drawing with «create_drawing».");
            var names = ViewNames(drawing);
            if (names.Count == 0)
                return Failed("The drawing has no views: the hole table attaches to a view of the part. " +
                               "First put the views with «standard_views».");

            string origin = Str(a, "origin", "bottom_left");
            int sx, sy;
            switch (origin)
            {
                case "bottom_left": sx = -1; sy = -1; break;
                case "bottom_right": sx = 1; sy = -1; break;
                case "top_left": sx = -1; sy = 1; break;
                case "top_right": sx = 1; sy = 1; break;
                default:
                    return Failed("«origin» is one of bottom_left, bottom_right, top_left, " +
                                   "top_right, not «" + origin + "».");
            }

            string reason;
            IView view = ResolveView(drawing, names, Str(a, "view", null), out reason);
            if (reason != null) return Failed(reason);
            if (view == null) return Failed("I cannot get the view.");
            string viewName = "";
            try { viewName = view.GetName2() ?? ""; } catch { }

            IMathUtility mu = null;
            MathTransform tr = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; tr = view.ModelToViewTransform; } catch { }
            if (mu == null || tr == null)
                return Failed("I cannot read how the view carries the model onto the sheet.");
            double scale = 1;
            try { scale = view.ScaleDecimal; } catch { }
            if (scale <= 0) scale = 1;

            // ── THE DRILLED FACE THAT FACES WHOEVER READS ───────────────────
            //
            // Among the planar faces of the part behind the view, those whose normal,
            // brought onto the sheet, points towards the viewer (z > 0.99); of these, the one
            // with the most inner rings made of circles — the holes.
            var part = view.ReferencedDocument as IModelDoc2;
            var partDoc = part as IPartDoc;
            if (partDoc == null)
                return Failed("Behind the view «" + viewName + "» there is no part: the hole table is made on " +
                               "the holes of a part.");
            Func<double[], double[]> onSheet = p =>
                ((IMathPoint)((IMathPoint)mu.CreatePoint(p)).MultiplyTransform(tr)).ArrayData as double[];
            Func<double[], double[]> directionOnSheet = v =>
                ((IMathVector)((IMathVector)mu.CreateVector(v)).MultiplyTransform(tr)).ArrayData as double[];

            IFace2 face = null;
            var holes = new List<HoleInView>();
            try
            {
                var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                foreach (var oc in bodies ?? new object[0])
                {
                    foreach (var of in ((oc as IBody2)?.GetFaces() as object[]) ?? new object[0])
                    {
                        var f = of as IFace2;
                        var n = ApiHelpers.NormalIfPlanar(f);
                        if (n == null) continue;
                        var nv = directionOnSheet(n);
                        double len = Math.Sqrt(nv[0] * nv[0] + nv[1] * nv[1] + nv[2] * nv[2]);
                        if (len < 1e-12 || nv[2] / len < 0.99) continue;
                        var here = FaceHoles(f, onSheet);
                        if (here.Count > holes.Count) { holes = here; face = f; }
                    }
                }
            }
            catch (Exception ex) { return Failed("I cannot read the faces of the part: " + ex.Message); }
            if (face == null || holes.Count == 0)
                return Failed("In the view «" + viewName + "» no face with holes faces the reader: the hole " +
                               "table is made on the view in which the holes are seen as circles " +
                               "(usually the one from above, V2).");

            // ── THE ORIGIN: the vertex of the outer outline nearest the corner asked for ──
            IVertex vertex = null;
            double vx = 0, vy = 0, better = double.MinValue;
            try
            {
                foreach (var oe in (face.GetEdges() as object[]) ?? new object[0])
                {
                    var e = oe as IEdge;
                    foreach (var ov in new[] { e?.GetStartVertex(), e?.GetEndVertex() })
                    {
                        var v = ov as IVertex;
                        var p = v?.GetPoint() as double[];
                        if (p == null) continue;
                        var s = onSheet(p);
                        double score = s[0] * sx + s[1] * sy;
                        if (score > better + 1e-9) { better = score; vertex = v; vx = s[0]; vy = s[1]; }
                    }
                }
            }
            catch { }
            if (vertex == null) return Failed("I cannot find a corner vertex on the face with the holes.");

            // ── WHERE THE TABLE GOES: top left of the sheet, if not told otherwise ──
            double sheetWidth = 0.420, sheetHeight = 0.297;
            try
            {
                var sheet = drawing.GetCurrentSheet() as ISheet;
                var pr = sheet?.GetProperties2() as double[];
                if (pr != null && pr.Length >= 7 && pr[5] > 0 && pr[6] > 0) { sheetWidth = pr[5]; sheetHeight = pr[6]; }
            }
            catch { }
            double x = Units.M(Num(a, "x_mm", 15)), y = Units.M(Num(a, "y_mm", Units.Mm(sheetHeight) - 15));
            string firstTag = Str(a, "tag_start", "A");

            int calloutsBefore = HoleCalloutsInView(view);
            int tablesBefore = DrawingTables(drawing);
            model.ClearSelection2(true);
            var sel = model.SelectionManager as ISelectionMgr;
            var sdV = sel?.CreateSelectData() as SelectData;
            var sdF = sel?.CreateSelectData() as SelectData;
            if (sdV == null || sdF == null) return Failed("I cannot prepare the selection in the view.");
            sdV.Mark = SelectionMarks.HoleTableDatumOrigin; sdV.View = (View)view;
            sdF.Mark = SelectionMarks.HoleTableHoles; sdF.View = (View)view;
            bool okV = (vertex as IEntity).Select4(false, sdV);
            bool okF = (face as IEntity).Select4(true, sdF);
            if (!okV || !okF)
                return Failed("I cannot select in the view the origin and the face with the holes (vertex " + okV +
                               ", face " + okF + ").");

            string tableModel = FirstTableModel((int)swUserPreferenceStringValue_e.swFileLocationsHoleTableTemplates, ".sldholtbt");
            // Z-C21.7 (L15-1): InsertHoleTable3, not the obsolete InsertHoleTable2
            // (IView~InsertHoleTable2.html: «Obsolete. Superseded by
            // IView::InsertHoleTable3»). The two new arguments as case H1 of the
            // Z-C21.3 probe probe_hole_table3: TagOrder swHoleTableTagOrder_XY (1),
            // TagType swHoleTable_AlphaNumericTags (1), no manual tags
            // (IView~InsertHoleTable3.html: «ManualTags Array of custom tags;
            // valid only if TagType is swHoleTableTagStyle_e.swHoleTable_ManualTags»).
            // On the bench plate (120 x 80, four D8 and one D20) H1 gave the same
            // six rows as InsertHoleTable2 (H0): A1 (15,15), A2 (15,65),
            // A3 (105,15), A4 (105,65), B1 (60,40), every one on a hole. TagType
            // is honoured (H2, numeric: 1 to 5), so the start value this
            // operation takes, the LETTER of the tags, goes with the alphanumeric
            // style.
            object raw;
            try
            {
                raw = view.InsertHoleTable3(false, x, y,
                    (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft, firstTag, tableModel,
                    (int)swHoleTableTagOrder_e.swHoleTableTagOrder_XY,
                    (int)swHoleTableTagStyle_e.swHoleTable_AlphaNumericTags, null);
            }
            catch (Exception ex) { return Failed("The hole table was not created: " + ex.Message); }
            model.ClearSelection2(true);
            var table = raw as ITableAnnotation;
            if (table == null)
                return Failed("SOLIDWORKS did not create the hole table and did not say why.");

            int lines = -1;
            try { lines = table.RowCount; } catch { }
            // BORN EMPTY: it is what happens with the wrong marks, measured. A
            // table with only the header gets printed and makes it look as if the part has no holes.
            if (lines != holes.Count + 1)
            {
                string removed = RemoveTable(model, drawing, table, tablesBefore);
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "The hole table was created with {0} records and the holes of the face are {1}: {2} were needed " +
                    "(the header plus one hole per record).{3}", lines, holes.Count, holes.Count + 1, removed));
            }

            // ── THE REREAD: every row against the model ─────────────────────
            var entries = new List<object>();
            int match = 0;
            for (int r = 1; r < lines; r++)
            {
                string tag = CleanCell(TableCell(table, r, 0));
                string sx0 = CleanCell(TableCell(table, r, 1));
                string sy0 = CleanCell(TableCell(table, r, 2));
                string measure = CleanCell(TableCell(table, r, 3));
                double lx, ly;
                bool numbers = double.TryParse(sx0.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out lx) &
                              double.TryParse(sy0.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out ly);
                bool found = false;
                if (numbers)
                    foreach (var f in holes)
                    {
                        double dx = Math.Abs(f.X - vx) / scale * 1000.0, dy = Math.Abs(f.Y - vy) / scale * 1000.0;
                        if (Math.Abs(dx - Math.Abs(lx)) <= 0.05 && Math.Abs(dy - Math.Abs(ly)) <= 0.05) { found = true; break; }
                    }
                if (found) match++;
                entries.Add(new Dictionary<string, object>
                {
                    ["tag"] = tag,
                    ["x_mm"] = numbers ? (object)lx : sx0,
                    ["y_mm"] = numbers ? (object)ly : sy0,
                    ["measure"] = measure,
                    ["in_model"] = found,
                });
            }
            if (match != holes.Count)
            {
                string removed = RemoveTable(model, drawing, table, tablesBefore);
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "The hole table says {0} positions and only {1} correspond to a hole of the part measured from " +
                    "the origin on the view: it is not the table of these holes.{2}", holes.Count, match, removed));
            }

            var measurements = new Dictionary<string, object>
            {
                ["view"] = viewName,
                ["origin"] = origin,
                ["holes"] = holes.Count,
                ["rows"] = lines,
                ["entries"] = entries.ToArray(),
                ["positions_checked"] = true,
                ["tables_in_drawing"] = DrawingTables(drawing),
            };
            if (!string.IsNullOrEmpty(tableModel)) measurements["table_template"] = tableModel;
            // THE HOLES TWICE (26/9/2026, the first drawing looked at with the table):
            // if the view already had the hole callouts, the table repeats them.
            // «Each element is dimensioned only once»: it is stated. In the right
            // order — table first, then drawing_dimension — it does not happen, because
            // drawing_dimension leaves the holes to the table.
            if (calloutsBefore > 0)
                measurements["warning"] = "In the view there were already " + calloutsBefore + " hole callouts " +
                                       "(and usually the position dimensions of the holes): with the table the holes " +
                                       "are dimensioned twice. Put the table BEFORE «drawing_dimension», which then leaves the holes to the table.";
            return WithoutFeature(measurements);
        }

        /// <summary>
        /// The holes of a face: the inner rings made of a whole circle,
        /// with the centre brought onto the sheet.
        /// </summary>
        private static List<HoleInView> FaceHoles(IFace2 f, Func<double[], double[]> onSheet)
        {
            var outside = new List<HoleInView>();
            try
            {
                foreach (var ol in (f.GetLoops() as object[]) ?? new object[0])
                {
                    var ring = ol as ILoop2;
                    if (ring == null || ring.IsOuter()) continue;
                    var edges = ring.GetEdges() as object[];
                    if (edges == null || edges.Length == 0) continue;
                    var curva = (edges[0] as IEdge)?.GetCurve() as ICurve;
                    if (curva == null || !curva.IsCircle()) continue;
                    var cp = curva.CircleParams as double[];   // centre x,y,z, axis, radius
                    if (cp == null || cp.Length < 7) continue;
                    var c = onSheet(new[] { cp[0], cp[1], cp[2] });
                    outside.Add(new HoleInView { X = c[0], Y = c[1], DiameterMm = cp[6] * 2000.0 });
                }
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// How many holes the hole tables of the current sheet list (rows minus
        /// the header); 0 if there are none, -1 if it cannot be read.
        ///
        /// THE WHOLE SHEET IS LOOKED AT, NOT THE VIEW. Measured on 26/9/2026: the
        /// hole table put on the top view belongs to the SHEET (the
        /// «view» zero of `GetFirstView`), not to the view — looking for it in the
        /// view, `drawing_dimension` did not see it and dimensioned the holes a second
        /// time. A drawing is of one part: a hole table on the sheet covers the
        /// holes of its views.
        /// </summary>
        private static int HolesInHoleTable(IDrawingDoc drawing)
        {
            try
            {
                int n = 0;
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    foreach (var o in (v.GetTableAnnotations() as object[]) ?? new object[0])
                    {
                        var t = o as ITableAnnotation;
                        if (t != null && t.Type == (int)swTableAnnotationType_e.swTableAnnotation_HoleChart)
                            n += Math.Max(0, t.RowCount - 1);
                    }
                    v = v.GetNextView() as IView;
                }
                return n;
            }
            catch { return -1; }
        }

        /// <summary>How many hole callouts a view already has; -1 if it cannot be read.</summary>
        private static int HoleCalloutsInView(IView view)
        {
            try
            {
                int n = 0;
                var d = view.GetFirstDisplayDimension5() as IDisplayDimension;
                while (d != null)
                {
                    if (d.IsHoleCallout()) n++;
                    d = d.GetNext5() as IDisplayDimension;
                }
                return n;
            }
            catch { return -1; }
        }

        /// <summary>How many tables the drawing has, counted view by view; -1 if it cannot be read.</summary>
        private static int DrawingTables(IDrawingDoc drawing)
        {
            try
            {
                int n = 0;
                var v = drawing.GetFirstView() as IView;
                while (v != null) { n += v.GetTableAnnotationCount(); v = v.GetNextView() as IView; }
                return n;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Removes a table born wrong and says how it went: the tail of the
        /// refusal sentence. It is counted before and after, because «I deleted it» said
        /// without rereading is not a measurement.
        /// </summary>
        private static string RemoveTable(IModelDoc2 model, IDrawingDoc drawing, ITableAnnotation table, int before)
        {
            try
            {
                var ann = table.GetAnnotation() as IAnnotation;
                model.ClearSelection2(true);
                // DeleteSelection2 AND NOT EditDelete: EditDelete asks for confirmation with
                // a window, and the COM call does not return (see SwOperations,
                // WithoutSketch). It was the only one left, found by workflow W-S on
                // 27/9/2026; now the ban is in Tests/ForbiddenSignaturesTests.
                if (ann != null && ann.Select3(false, null))
                    model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
            }
            catch { }
            int after = DrawingTables(drawing);
            if (before >= 0 && after == before) return " I removed it: the drawing is as it was.";
            return " I could not remove it (tables " + before + " -> " + after + "): remove it by hand.";
        }

        private static string TableCell(ITableAnnotation t, int r, int c)
        {
            try { return t.get_Text(r, c) ?? ""; } catch { return ""; }
        }

        /// <summary>The text of a cell without the format codes: «&lt;MOD-DIAM&gt;» becomes «Ø».</summary>
        private static string CleanCell(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("<MOD-DIAM>", "Ø");
            s = Regex.Replace(s, "<[^>]*>", "");
            return s.Trim();
        }

        /// <summary>
        /// The first table template in the folders SOLIDWORKS declares for
        /// that type; empty if there is none. It is asked for, not written by hand (entry
        /// GT5: the right constant serves to ask the question, not to skip it).
        /// </summary>
        private string FirstTableModel(int preference, string ext)
        {
            try
            {
                string folders = _sw.GetUserPreferenceStringValue(preference) ?? "";
                foreach (var c in folders.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(c) || !System.IO.Directory.Exists(c)) continue;
                    var f = System.IO.Directory.GetFiles(c, "*" + ext, System.IO.SearchOption.AllDirectories);
                    if (f.Length > 0) { Array.Sort(f, StringComparer.OrdinalIgnoreCase); return f[0]; }
                }
            }
            catch { }
            return "";
        }
    }
}
