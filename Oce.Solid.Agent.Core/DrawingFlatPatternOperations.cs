using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE FLAT PATTERN VIEW ON THE DRAWING (entry GT13).
    ///
    /// ── MEASURED BEFORE WRITING IT, 26/9/2026 (`probe0089 flatview`) ────────
    ///
    /// `IDrawingDoc.CreateFlatPatternViewFromModelView3(path,
    /// configuration, X, Y, Z, HideBendLines, Flip)` on the L of the sheet
    /// metal cases (30 + 50, thickness 2, radius 2, depth 40): the view is born
    /// at the first attempt, `IsFlatPatternView` true, one bend line.
    ///
    /// **The extent is NOT read from `GetOutline`**: it said 51.88 x 92.592 mm for
    /// a flat pattern of 80.712 x 40 — the outline of the view includes the bend
    /// note and a margin. It is measured from the VISIBLE EDGES of the view
    /// (`GetVisibleEntities2`), brought onto the sheet with `ModelToViewTransform` and
    /// divided by the scale; and compared with the flat pattern measured on the part
    /// BEFORE putting the view (the «FlatPattern» feature unsuppressed,
    /// the body measured, put back).
    /// </summary>
    public partial class SwOperations
    {
        private Outcome FlatPatternView(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed("The active document is not a drawing: the flat pattern view goes in a drawing. " +
                               "Create it with «create_drawing».");

            string modelPath = Str(a, "model", null);
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                string why;
                modelPath = ModelToPutInView(out why);
                if (modelPath == null) return Failed(why);
            }
            var swPath = CanonicalPath(modelPath);
            if (swPath == null)
                return Failed("«" + modelPath + "» does not appear to be open: the view would be created empty.");
            // A part loaded hidden (no window of its own) answers here as well:
            // the C4-4 probe of 28/9/2026 read, on the object GetOpenDocumentByName
            // gave for a hidden part, the same IPartDoc, configuration, features,
            // volume and bodies as on the visible one (the guide's «opened in its
            // own window» did not matter).
            var part = _sw.GetOpenDocumentByName(swPath) as IModelDoc2;
            if (!(part is IPartDoc))
                return Failed("«" + swPath + "» is not a part: the flat pattern belongs to a sheet metal part.");
            if (SheetMetalData(part) == null)
                return Failed("«" + System.IO.Path.GetFileName(swPath) + "» is not sheet metal " +
                               "(it does not have the «SheetMetal» feature): there is no flat pattern to put in the drawing.");

            string configuration = Str(a, "configuration", null);
            if (string.IsNullOrWhiteSpace(configuration))
                try { configuration = (part.GetActiveConfiguration() as IConfiguration)?.Name ?? ""; } catch { configuration = ""; }
            double x = Units.M(Num(a, "x_mm", 300));
            double y = Units.M(Num(a, "y_mm", 90));
            bool hideBends = Bool(a, "hide_bends", false);

            // ── THE FLAT PATTERN OF THE PART, MEASURED FIRST ────────────────
            // Z-C21.8, finding M24: a flat pattern that did not unsuppress, or
            // did not suppress again, stops the step here, before the drawing is
            // touched; the part may be left flattened, and the refusal says so.
            double alongPart = FlattenedLength(part, out double _, out string flatPatternProblem);
            if (flatPatternProblem != null)
                return Failed("Before placing the view, I measured the flat length of the part and " +
                               flatPatternProblem + ". I did not place the view.");

            int before = CountViews(drawing);
            object raw = null;
            try
            {
                raw = drawing.CreateFlatPatternViewFromModelView3(
                    swPath, configuration ?? "", x, y, 0, hideBends, false);
            }
            catch (Exception ex) { return Failed("The flat pattern view was not created: " + ex.Message); }
            var view = raw as IView;
            if (view == null)
                return Failed("SOLIDWORKS did not create the flat pattern view and did not say why " +
                               "(configuration «" + configuration + "»).");
            string itemName = "";
            try { itemName = view.GetName2() ?? ""; } catch { }

            bool flat = false;
            try { flat = view.IsFlatPatternView(); } catch { }
            // THE ANSWER OF RemoveNewView IS READ at every refusal below (Z-C21.8,
            // finding M40): «I removed it» only when the view is no longer in the
            // drawing, which RemoveNewView checks by reading the views again. Until
            // library 0.144.0 four of these five refusals said removed whatever
            // the removal did.
            if (!flat)
            {
                bool removedNotFlat = RemoveNewView(model, itemName);
                return Failed("The view «" + itemName + "» was born but is not a flat pattern (IsFlatPatternView false)" +
                               ViewRemovedOrNot(removedNotFlat));
            }
            int bends = -1;
            try { bends = view.GetBendLineCount(); } catch { }

            // ── A FREE SPOT (seen in the PNG, 26/9/2026) ───────────────────
            //
            // Put at 300/90 mm at scale 2:1, the unfolded L went down into the
            // title block, with the bench green. The outline of the view is measured (with
            // the bend note: that is what occupies the sheet) and it is moved to
            // a free spot, if «x_mm»/«y_mm» did not fix it.
            string location, placeReason;
            if (!FlatPatternInItsPlace(drawing, view, a.ContainsKey("x_mm") || a.ContainsKey("y_mm"), out location, out placeReason))
            {
                bool removedNoPlace = RemoveNewView(model, itemName);
                return Failed(placeReason + (removedNoPlace
                    ? " I removed the view."
                    : " Remove the view — " + Sentences.HowToWithdrawIt + "."));
            }

            // ── THE EXTENT FROM THE VISIBLE EDGES ────────────────────────────
            double[] boundingBox = BoundingBoxFromEdges(view);
            var measurements = new Dictionary<string, object>
            {
                ["view"] = itemName,
                ["model"] = swPath,
                ["configuration"] = configuration,
                ["views_before"] = before,
                ["views_after"] = CountViews(drawing),
                ["flat"] = true,
                ["bend_lines"] = NumberOrNull(bends),
                ["bounding_box_mm"] = boundingBox == null ? null : new object[] { boundingBox[0], boundingBox[1] },
                ["flat_pattern_length_mm"] = double.IsNaN(alongPart) ? null : (object)Math.Round(alongPart, 3),
            };
            try { measurements["scale"] = view.ScaleDecimal; } catch { }
            measurements["view_location"] = location;
            try
            {
                var o = view.GetOutline() as double[];
                if (o != null) measurements["contour_view_mm"] = new object[] { Math.Round(o[0] * 1000, 1), Math.Round(o[1] * 1000, 1), Math.Round(o[2] * 1000, 1), Math.Round(o[3] * 1000, 1) };
            }
            catch { }
            measurements["inside_sheet"] = true;
            if (boundingBox == null)
            {
                bool removedUnmeasured = RemoveNewView(model, itemName);
                return Failed("The flat-pattern view was born but I cannot measure its edges: without a measure " +
                               "I cannot tell whether it is the right flat pattern" + ViewRemovedOrNot(removedUnmeasured));
            }
            if (!double.IsNaN(alongPart) && Math.Abs(boundingBox[0] - alongPart) > 0.1)
            {
                bool removedOther = RemoveNewView(model, itemName);
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "The flat-pattern view measures {0:0.###} mm on its long side and the flat pattern of the part " +
                    "{1:0.###} mm: it is not the flat pattern of this part (or the part is not flattened)",
                    boundingBox[0], alongPart) + ViewRemovedOrNot(removedOther));
            }
            if (double.IsNaN(alongPart))
            {
                // WITHOUT THE FLAT PATTERN OF THE PART THERE IS NO CROSS-CHECK (review of
                // 26/9/2026): the view could be of something else.
                bool removed = RemoveNewView(model, itemName);
                return Failed("I cannot measure the flat pattern of the part (the «FlatPattern» feature cannot be measured), " +
                               "and without it I cannot say whether the view shows the right flat pattern. " +
                               (removed ? "I removed the view." : "Remove the view: " + Sentences.HowToWithdrawIt + "."));
            }
            measurements["matches_with_part"] = true;
            // The first model view renames a drawing without a file (N27).
            TellTitle(model, measurements);
            return WithoutFeature(measurements);
        }

        /// <summary>
        /// The extent of the view in mm, from its true drawing: the visible
        /// edges, sampled, brought onto the sheet and divided by the scale.
        /// [long side, short side], or null if nothing can be read.
        /// </summary>
        private double[] BoundingBoxFromEdges(IView view)
        {
            IMathUtility mu = null;
            MathTransform tr = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; tr = view.ModelToViewTransform; } catch { }
            if (mu == null || tr == null) return null;
            double scale = 1;
            try { scale = view.ScaleDecimal; } catch { }
            if (scale <= 0) scale = 1;
            object[] edges = null;
            try { edges = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch { }
            if (edges == null || edges.Length == 0) return null;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            int points = 0;
            foreach (var o in edges)
            {
                var e = o as IEdge;
                if (e == null) continue;
                var samples = new List<double[]>();
                try
                {
                    var cp = ApiHelpers.EdgeParameters(e, out ICurve c);
                    if (cp != null && c != null)
                    {
                        var tess = c.GetTessPts(1e-5, 1e-3, cp.StartPoint, cp.EndPoint) as double[];
                        if (tess != null)
                            for (int i = 0; i + 2 < tess.Length; i += 3)
                                samples.Add(new[] { tess[i], tess[i + 1], tess[i + 2] });
                        var s = cp.StartPoint as double[];
                        var f = cp.EndPoint as double[];
                        if (s != null) samples.Add(s);
                        if (f != null) samples.Add(f);
                    }
                }
                catch { }
                foreach (var p in samples)
                {
                    double[] q;
                    try { q = ((IMathPoint)((IMathPoint)mu.CreatePoint(p)).MultiplyTransform(tr)).ArrayData as double[]; }
                    catch { continue; }
                    if (q == null) continue;
                    x0 = Math.Min(x0, q[0]); x1 = Math.Max(x1, q[0]);
                    y0 = Math.Min(y0, q[1]); y1 = Math.Max(y1, q[1]);
                    points++;
                }
            }
            if (points < 2) return null;
            double w = (x1 - x0) / scale * 1000.0, h = (y1 - y0) / scale * 1000.0;
            return new[] { Math.Round(Math.Max(w, h), 3), Math.Round(Math.Min(w, h), 3) };
        }

        /// <summary>
        /// Brings the view to a free spot of the sheet (SheetPlacementOperations),
        /// or checks that the one asked for is free. False with the reason.
        /// </summary>
        private static bool FlatPatternInItsPlace(IDrawingDoc drawing, IView view, bool requested, out string location, out string reason)
        {
            location = null; reason = null;
            var o = view.GetOutline() as double[];
            var p = view.Position as double[];
            if (o == null || o.Length < 4 || p == null || p.Length < 2)
            {
                reason = "The flat pattern view was created but I cannot read where it is on the sheet.";
                return false;
            }
            double w = o[2] - o[0], h = o[3] - o[1];
            var sheet = ReadSheet(drawing, view);
            if (requested)
            {
                string why = sheet.Touches(o[0], o[1], o[2], o[3]);
                if (why != null)
                {
                    reason = string.Format(CultureInfo.InvariantCulture,
                        "The flat pattern view ({0:0} x {1:0} mm on the sheet) where you asked for it {2}. Remove «x_mm» " +
                        "and «y_mm» and I put it in a free place.", w * 1000, h * 1000, why);
                    return false;
                }
                location = "where requested";
                return true;
            }
            var down = FreePlace(sheet, w, h, out location);
            if (down == null)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "On the sheet there is no free place for the flat pattern view ({0:0} x {1:0} mm) outside the other " +
                    "views and the title block: use a bigger sheet or another scale.", w * 1000, h * 1000);
                return false;
            }
            // The position of the view is not necessarily the centre of the outline:
            // it is moved by as much as the bottom left corner needs.
            try { view.Position = new[] { p[0] + down[0] - o[0], p[1] + down[1] - o[1] }; } catch { }
            // GetOutline IS READ RIGHT AFTER Position, with no rebuild, and that is
            // measured (Z-C21.3, finding L14-1, NOT REAL). IView~Position.html
            // asks for IModelDoc2::EditRebuild3 «to see these changes», which is
            // about the graphics. The Z-C21.3 probe of L14-1 (28/9/2026), a free
            // named view moved by (20, 10) mm: the outline read at once had moved
            // by exactly (20, 10) at both corners, and EditRebuild3 or
            // GraphicsRedraw2 in between changed nothing. The flat-pattern view is
            // a free view like that one. A projected view ALIGNED to its parent is
            // not: asked (20, 20), it moved (0, 20) (same probe). The check below
            // judges the outline where the view actually is, so even then it
            // would judge the place it landed in, not the one asked.
            var o2 = view.GetOutline() as double[];
            string anchor = o2 == null ? "cannot be read" : sheet.Touches(o2[0], o2[1], o2[2], o2[3]);
            if (anchor != null)
            {
                reason = "I moved the flat pattern view " + location + " but " + anchor + ".";
                return false;
            }
            return true;
        }

        /// <summary>
        /// The end of a refusal that removed the new view, or tried to (M40):
        /// «removed» only when RemoveNewView found it gone on a reread.
        /// </summary>
        private static string ViewRemovedOrNot(bool removed)
        {
            return removed ? ": I removed it." : ": I could not remove it, remove it — " + Sentences.HowToWithdrawIt + ".";
        }

        /// <summary>
        /// Removes a view just put, by name. `DeleteSelection2` and not
        /// `EditDelete`: the second may ask for confirmation with a window, and
        /// a COM call that waits for a window does not return (see
        /// SwOperations, «DeleteSelection2 AND NOT EditDelete»).
        /// </summary>
        private static bool RemoveNewView(IModelDoc2 drawing, string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return false;
            try
            {
                drawing.ClearSelection2(true);
                if (drawing.Extension.SelectByID2(itemName, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0))
                    drawing.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                drawing.ClearSelection2(true);
                // ONCE REMOVED IT IS READ BACK: the view with that name must no longer be there.
                var v = (drawing as IDrawingDoc)?.GetFirstView() as IView;
                while (v != null)
                {
                    if (string.Equals(v.GetName2(), itemName, StringComparison.Ordinal)) return false;
                    v = v.GetNextView() as IView;
                }
                return true;
            }
            catch { return false; }
        }
    }
}
