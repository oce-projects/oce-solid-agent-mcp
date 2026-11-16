using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE OVERALL SIZE, THE THICKNESS AND THE BEND RADIUS OF A VIEW WHOSE EDGES ARE ALL SHORTENED (0.131.0, item 1 of
    /// the drawing branch).
    ///
    /// ── WHAT WENT WRONG, 12/10/2026, AND WHAT WAS MEASURED ───────────────────────────
    ///
    /// The L bracket of the proof session (sheet metal 8 thick, bend radius 8, 120 x 70 x 60, chamfer 1 x 45 along the
    /// whole outline) could not be dimensioned by `drawing_dimension`: on every view, with every edge, the answer was
    /// «the chamfer dimension was not created». Checks/Draw131 «edges» (12/10/2026, SOLIDWORKS 29.5) read the 24 edges of
    /// the front view and found:
    ///
    ///  1. EVERY edge of the view touches the chamfer feature or is shortened by it: the edge-by-edge path («it goes with
    ///     the chamfer dimension») left them all out, and the only thing left to try was the chamfer dimension. The
    ///     first «oblique» edge it met was an ARC (the strip of the chamfer along the bend, R15, whose chord is oblique), and
    ///     AddChamferDim wants two straight edges: not created. The feature was then marked as done, so no other edge
    ///     was tried. The other oblique edges of the view are not a chamfer seen in profile either: both of their faces
    ///     are faces of the chamfer feature (the miter where two strips meet), and AddChamferDim on them gives a «135°»
    ///     chamfer dimension, not an error (the probe, «chamfer V1»). On the top view the bridge did place one: «-1 mm, 135°».
    ///  2. The 120 and the 70 of a bent sheet are not the length of any edge: the longest straight edge of the bottom is 103
    ///     (the virtual sharp is 16 beyond the tangent line of the bend, minus the chamfer). Dimensioning edge by edge cannot
    ///     give them, chamfer or no chamfer.
    ///
    /// So here the drawing is told what it needs and no edge can give: the OVERALL size between the two extreme edges of
    /// the outline (IModelDoc2~AddHorizontalDimension2 and AddVerticalDimension2: «a horizontal dimension for the
    /// currently selected entities», two edges selected, the way the hole positions already use them with a circle and
    /// an edge), the THICKNESS of the sheet between two parallel edges that are one thickness apart (the thickness is read
    /// from the «SheetMetal» feature of the part behind the view, ISheetMetalFeatureData~Thickness), and the INNER radius of
    /// the bend (IModelDocExtension~AddDimension on the arc: a radial dimension, measured in the probe on the arcs of this
    /// part; the outer arc is the inner plus the thickness and is not dimensioned a second time).
    ///
    /// It runs ONLY when the edge-by-edge path had nothing to dimension and left edges out because of a chamfer (see
    /// DrawingDimension): a part that has an edge to dimension is dimensioned as before.
    ///
    /// WHAT IS THE OUTLINE. The edges between a chamfer face and a face seen EDGE-ON (the side of the part) are the
    /// outline: the extreme ones are the edges of the 120 and the 70. The boundary of a chamfer strip on the face that
    /// looks at the reader is an inset line, the miter is a corner and an arc is not straight: none of them is an
    /// outline edge (ProjectedEdge.ChamferBoundaryFaceOn, ChamferMiter, IsArc, set by ClassifyChamferFaces from
    /// IEdge~GetTwoAdjacentFaces2 and IFace2~GetFeature).
    ///
    /// ONE DIMENSION PER ELEMENT. Each dimension has a key: the extent between the two edges along an axis of the model
    /// (ExtentKey, the same the dimensions of the drawing are read back with, see KeysAlreadyDimensioned), «gap|» plus the
    /// value for the thickness, «R|» plus the radius for a bend. A dimension of the model that arrived free (the 60 of the
    /// depth of the flange: the import attaches nothing to it) is recognised by its value: the same linear value in the same
    /// view is the same element and is not put again.
    /// </summary>
    public partial class SwOperations
    {
        private void DimensionTheOverallSize(
            IModelDoc2 model, IDrawingDoc drawing, IView view, object[] edges, List<ProjectedEdge> all,
            HashSet<string> already, double[] contour, double dimensionOffset, double planStep, double scale,
            ref int below, ref int sideways, ref int onLeft, double[] sheet,
            System.Collections.IList born, System.Collections.IList overall,
            System.Collections.IList elsewhere, System.Collections.IList failed)
        {
            // What the view already carries as a linear dimension: the model's own dimensions arrive free.
            List<CarriedDimension> carried = LinearDimensionsOfTheView(view, false);
            // A dimension of the model that arrived FREE (nothing attached: the depth of the base flange) in ANY view of the
            // drawing is the model's own: the same value is not put again in another view.
            carried.AddRange(FreeLinearDimensionsOfTheDrawing(drawing, view));

            OverallExtent(true, model, drawing, edges, all, already, carried, contour, dimensionOffset, planStep, scale,
                          ref below, ref sideways, ref onLeft, sheet, born, overall, elsewhere, failed);
            OverallExtent(false, model, drawing, edges, all, already, carried, contour, dimensionOffset, planStep, scale,
                          ref below, ref sideways, ref onLeft, sheet, born, overall, elsewhere, failed);

            // The sheet metal of the part behind the view: its thickness decides the thickness dimension and which
            // arc of a bend is the inner one.
            double thicknessMm = double.NaN;
            try
            {
                var part = view.ReferencedDocument as IModelDoc2;
                var data = part == null ? null : SheetMetalData(part);
                if (data != null) thicknessMm = Units.Mm(data.Thickness);
            }
            catch { }
            if (!double.IsNaN(thicknessMm) && thicknessMm > 0)
                ThicknessOfTheSheet(thicknessMm, model, drawing, edges, all, already, carried, contour, dimensionOffset, planStep, scale,
                                    ref below, ref sideways, ref onLeft, sheet, born, overall, elsewhere, failed);
            BendRadii(thicknessMm, model, drawing, edges, all, already, contour, dimensionOffset, scale,
                      born, overall, elsewhere, failed);
        }

        private static readonly CultureInfo Cg = CultureInfo.InvariantCulture;

        /// <summary>One linear dimension that is on the drawing: its value, whether it is horizontal or vertical on the sheet (when known), and whether nothing is attached to it.</summary>
        private sealed class CarriedDimension
        {
            public double Mm;
            public bool OrientationKnown, Horizontal, Free;
        }

        /// <summary>
        /// The linear dimensions of a view (IDisplayDimension~Type2, swDimensionType_e: linear, horizontal, vertical), whatever they hang
        /// from, with the value read in the configuration and the direction read from the two arrowheads (IDisplayData~GetArrowHeadAtIndex2:
        /// the tips at the same height make a horizontal dimension), the dimensions hidden by hand left out. With <paramref name="freeOnly"/>
        /// only the ones with no attached entity: the dimensions of the model import free, with no edge to read a key from.
        /// </summary>
        private static List<CarriedDimension> LinearDimensionsOfTheView(IView view, bool freeOnly)
        {
            var values = new List<CarriedDimension>();
            try
            {
                for (var d = view.GetFirstDisplayDimension5() as IDisplayDimension; d != null; d = d.GetNext5() as IDisplayDimension)
                {
                    try
                    {
                        bool hidden = false;
                        try { hidden = ApiHelpers.IsHiddenByHand(d); } catch { }
                        if (hidden) continue;
                        int type = d.Type2;
                        if (type != (int)swDimensionType_e.swLinearDimension && type != (int)swDimensionType_e.swHorLinearDimension &&
                            type != (int)swDimensionType_e.swVertLinearDimension) continue;
                        var dim = d.GetDimension2(0) as IDimension;
                        if (dim == null) continue;
                        var one = new CarriedDimension { Mm = Math.Round(ApiHelpers.ReadDimensionInConfiguration(dim, "") * 1000.0, 3) };
                        try
                        {
                            var ann = d.GetAnnotation() as IAnnotation;
                            var attached = ann == null ? null : ann.GetAttachedEntities3() as object[];
                            one.Free = attached == null || attached.Length == 0;
                        }
                        catch { }
                        try
                        {
                            var data = d.GetDisplayData() as IDisplayData;
                            if (data != null && data.GetArrowHeadCount() == 2)
                            {
                                var t0 = data.GetArrowHeadAtIndex2(0) as double[];
                                var t1 = data.GetArrowHeadAtIndex2(1) as double[];
                                if (t0 != null && t1 != null && t0.Length >= 2 && t1.Length >= 2)
                                {
                                    bool sameY = Math.Abs(t0[1] - t1[1]) < 1e-7, sameX = Math.Abs(t0[0] - t1[0]) < 1e-7;
                                    if (sameY != sameX) { one.OrientationKnown = true; one.Horizontal = sameY; }
                                }
                            }
                        }
                        catch { }
                        if (freeOnly && !one.Free) continue;
                        values.Add(one);
                    }
                    catch { }
                }
            }
            catch { }
            return values;
        }

        /// <summary>The free linear dimensions of every other view of the sheet (the model's own dimensions, put wherever the import chose).</summary>
        private static List<CarriedDimension> FreeLinearDimensionsOfTheDrawing(IDrawingDoc drawing, IView except)
        {
            var values = new List<CarriedDimension>();
            try
            {
                var v = (drawing.GetFirstView() as IView)?.GetNextView() as IView;   // [0] is the sheet
                while (v != null)
                {
                    IView other = v;
                    v = v.GetNextView() as IView;
                    string a = "", b = "";
                    try { a = other.GetName2() ?? ""; b = except.GetName2() ?? ""; } catch { }
                    if (a == b) continue;
                    // the direction on ANOTHER view says nothing about this one: the depth is vertical on the top view and horizontal on the side view
                    foreach (CarriedDimension one in LinearDimensionsOfTheView(other, true)) { one.OrientationKnown = false; values.Add(one); }
                }
            }
            catch { }
            return values;
        }

        /// <summary>The diameters (mm) of the hole callouts a view carries (IDisplayDimension~IsHoleCallout), the value read in the configuration.</summary>
        private static List<double> HoleCalloutDiametersOfTheView(IView view)
        {
            var values = new List<double>();
            try
            {
                for (var d = view.GetFirstDisplayDimension5() as IDisplayDimension; d != null; d = d.GetNext5() as IDisplayDimension)
                {
                    try
                    {
                        bool hidden = false;
                        try { hidden = ApiHelpers.IsHiddenByHand(d); } catch { }
                        if (hidden || !d.IsHoleCallout()) continue;
                        var dim = d.GetDimension2(0) as IDimension;
                        if (dim != null) values.Add(Math.Round(ApiHelpers.ReadDimensionInConfiguration(dim, "") * 1000.0, 3));
                    }
                    catch { }
                }
            }
            catch { }
            return values;
        }

        /// <summary>A line of the answer, put in a list that the obsolete-call contract can read the type of.</summary>
        private static void NoteIn(System.Collections.IList into, string text) { into.Add(text); }

        /// <summary>Does one of the dimensions have this value (within a hundredth of a millimetre) and this direction? orientation: 0 any, 1 horizontal, 2 vertical; a dimension whose direction is not known counts for any.</summary>
        private static bool CarriesTheValue(List<CarriedDimension> carried, double mm, int orientation)
        {
            foreach (CarriedDimension c in carried)
            {
                if (Math.Abs(c.Mm - mm) >= 0.01) continue;
                if (orientation == 0 || !c.OrientationKnown) return true;
                if (c.Horizontal == (orientation == 1)) return true;
            }
            return false;
        }

        /// <summary>
        /// The axis of the model that the separation between two parallel straight lines (the first with ends a0 and a1, the second with
        /// ends b0 and b1) runs on, and the two coordinates along it: the axis is the one whose separation IS <paramref name="valueMm"/>,
        /// the value the dimension reads. False when the lines are not parallel or no axis matches (an oblique view).
        /// The separation is the one PERPENDICULAR to the lines: the two edges of the 120 of the L bracket start at different heights (the
        /// first at y 16, the other at y 7), and the difference of their start points is not the 120. And it is not necessarily
        /// along ONE axis: the two edges of the 120 of the top view are 62 mm apart in the depth that the view does not show (the outer
        /// wall of the short flange at y 69 and the free end at y 7), so the dominant component is not the criterion either.
        /// </summary>
        private static bool AxisSeparation(double[] a0, double[] a1, double[] b0, double[] b1, double valueMm, out int axis, out double ca, out double cb)
        {
            axis = 0; ca = 0; cb = 0;
            if (a0 == null || a1 == null || b0 == null || b1 == null) return false;
            double[] u = { a1[0] - a0[0], a1[1] - a0[1], a1[2] - a0[2] };
            double[] w = { b1[0] - b0[0], b1[1] - b0[1], b1[2] - b0[2] };
            double lu = Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
            double lw = Math.Sqrt(w[0] * w[0] + w[1] * w[1] + w[2] * w[2]);
            if (lu < 1e-9 || lw < 1e-9) return false;
            double dot = (u[0] * w[0] + u[1] * w[1] + u[2] * w[2]) / (lu * lw);
            if (Math.Abs(dot) < 0.9999) return false;
            double[] d = { b0[0] - a0[0], b0[1] - a0[1], b0[2] - a0[2] };
            double along = (d[0] * u[0] + d[1] * u[1] + d[2] * u[2]) / lu;
            double[] perp = { d[0] - along * u[0] / lu, d[1] - along * u[1] / lu, d[2] - along * u[2] / lu };
            double lp = Math.Sqrt(perp[0] * perp[0] + perp[1] * perp[1] + perp[2] * perp[2]);
            if (lp < 1e-9) return false;
            double best = double.MaxValue;
            axis = -1;
            for (int i = 0; i < 3; i++)
            {
                double diff = Math.Abs(Math.Abs(perp[i]) * 1000.0 - valueMm);
                if (diff < best) { best = diff; axis = i; }
            }
            if (axis < 0 || best > 0.02) return false;
            ca = a0[axis]; cb = b0[axis];
            return true;
        }

        private static string ExtentKey(int axis, double a, double b)
        {
            double v0 = Math.Min(a, b) * 1000.0, v1 = Math.Max(a, b) * 1000.0;
            return "XYZ"[axis] + "|" + Math.Round(v0, 2).ToString("0.00", Cg) + "|" + Math.Round(v1, 2).ToString("0.00", Cg);
        }

        /// <summary>The keys of a dimension between two parallel edges, read back from the drawing (see KeysAlreadyDimensioned).</summary>
        private static void KeysOfTwoParallelEdges(double[] a0, double[] a1, double[] b0, double[] b1, double valueMm, HashSet<string> into)
        {
            int axis; double ca, cb;
            if (!AxisSeparation(a0, a1, b0, b1, valueMm, out axis, out ca, out cb)) return;
            into.UnionWith(new[] { ExtentKey(axis, ca, cb), GapKey(Math.Abs(cb - ca) * 1000.0) });
        }

        private static string GapKey(double mm) { return "gap|" + Math.Round(mm, 2).ToString("0.00", Cg); }

        /// <summary>The x of a dimension that goes on the side of the view, inside the frame of the sheet (the same rule as the others).</summary>
        private static double SideX(double[] contour, double offset, double pitch, ref int sideways, ref int onLeft, double[] sheet)
        {
            double x = contour[2] + offset + sideways * pitch;
            sideways++;
            if (sheet != null && x > sheet[0] - 0.015)
            {
                sideways--;
                x = contour[0] - offset - onLeft * pitch;
                onLeft++;
            }
            return x;
        }

        /// <summary>An edge of the outline of the part: straight, not the inset line of a chamfer strip, not a miter.</summary>
        private static bool IsOutlineLine(ProjectedEdge s)
        {
            return !s.IsArc && !s.ChamferMiter && !s.ChamferBoundaryFaceOn;
        }

        /// <summary>
        /// The overall width (between the leftmost and the rightmost vertical edge of the outline) or the overall height
        /// (between the lowest and the highest horizontal edge). Not made when an edge that no chamfer touches already
        /// measures the extent by itself, when the extremes are not parallel to an axis of the model (an isometric view),
        /// or when the same extent is already dimensioned in the drawing.
        /// </summary>
        private void OverallExtent(
            bool width, IModelDoc2 model, IDrawingDoc drawing, object[] edges, List<ProjectedEdge> all,
            HashSet<string> already, List<CarriedDimension> carried, double[] contour, double dimensionOffset, double planStep, double scale,
            ref int below, ref int sideways, ref int onLeft, double[] sheet,
            System.Collections.IList born, System.Collections.IList overall,
            System.Collections.IList elsewhere, System.Collections.IList failed)
        {
            string label = width ? "overall width" : "overall height";
            string across = width ? "vertical" : "horizontal";
            string along = width ? "horizontal" : "vertical";
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var s in all)
            {
                if (!IsOutlineLine(s) || s.Facing != across) continue;
                double c = width ? s.FromSheet[0] : s.FromSheet[1];
                if (c < lo) lo = c;
                if (c > hi) hi = c;
            }
            if (lo == double.MaxValue || hi - lo < 1e-6) return;
            double extentSheet = hi - lo;

            // A single edge that no chamfer touches already measures it: the edge-by-edge path is the one that dimensions it.
            foreach (var s in all)
                if (!s.IsArc && s.Facing == along && s.ChamferFeature == null && Math.Abs(s.Length - extentSheet) < 1e-6) return;

            ProjectedEdge low = null, high = null;
            foreach (var s in all)
            {
                if (!IsOutlineLine(s) || s.Facing != across) continue;
                double c = width ? s.FromSheet[0] : s.FromSheet[1];
                if (Math.Abs(c - lo) < 1e-6 && (low == null || s.Length > low.Length)) low = s;
                if (Math.Abs(c - hi) < 1e-6 && (high == null || s.Length > high.Length)) high = s;
            }
            if (low == null || high == null || ReferenceEquals(low, high)) return;

            double expectedMm = extentSheet / scale * 1000.0;
            int axis; double ca, cb;
            if (!AxisSeparation(low.FromModel, low.ToModel, high.FromModel, high.ToModel, expectedMm, out axis, out ca, out cb)) return;
            string key = ExtentKey(axis, ca, cb);
            string said = label + " " + Math.Round(expectedMm, 2).ToString("0.##", Cg) + " mm";
            if (already.Contains(key))
            {
                elsewhere.Add(said + " (already dimensioned in this drawing)");
                return;
            }
            if (CarriesTheValue(carried, expectedMm, width ? 1 : 2))
            {
                // The model's own dimension of this length is in the view (it arrives free): the same element.
                already.UnionWith(new[] { key });
                elsewhere.Add(said + " (the view already carries a dimension of the model with this value)");
                return;
            }

            double x, y;
            if (width)
            {
                x = (low.FromSheet[0] + high.FromSheet[0]) / 2;
                y = contour[1] - dimensionOffset - below * planStep;
                below++;
            }
            else
            {
                y = (low.FromSheet[1] + high.FromSheet[1]) / 2;
                x = SideX(contour, dimensionOffset, planStep, ref sideways, ref onLeft, sheet);
            }
            if (!DimensionBetweenEdges(label, width, low, high, x, y, expectedMm, new[] { key }, model, drawing, edges, already, born, overall, failed))
            {
                // Not born: the place is given back.
                if (width) below--;
                else if (x > contour[2]) sideways--; else onLeft--;
            }
        }

        /// <summary>
        /// The dimension between two selected edges, read back: the count of the dimensions of the drawing goes up, and the
        /// value is the one the geometry says (the distance seen on the sheet divided by the scale). True when it was born.
        /// </summary>
        private bool DimensionBetweenEdges(
            string label, bool horizontal, ProjectedEdge first, ProjectedEdge second, double x, double y, double expectedMm, string[] keys,
            IModelDoc2 model, IDrawingDoc drawing, object[] edges, HashSet<string> already,
            System.Collections.IList born, System.Collections.IList overall, System.Collections.IList failed)
        {
            int before = CountDimensions(drawing);
            IDisplayDimension dimension = null;
            try
            {
                model.ClearSelection2(true);
                if ((edges[first.Index] as IEntity).Select4(false, null) && (edges[second.Index] as IEntity).Select4(true, null))
                    dimension = (horizontal ? model.AddHorizontalDimension2(x, y, 0) : model.AddVerticalDimension2(x, y, 0)) as IDisplayDimension;
            }
            catch { }
            try { model.EditRebuild3(); } catch { }
            if (NotGrown(drawing, before, dimension))
            {
                failed.Add(label + " (the dimension between the edges " + first.Index + " and " + second.Index + " was not created)");
                return false;
            }
            var described = DescribeDimension(dimension, first.Index);
            described["orientation"] = label;
            described["position_mm"] = WhereItEnded(dimension);
            object mm;
            if (described.TryGetValue("mm", out mm) && mm != null && Math.Abs(Convert.ToDouble(mm, Cg) - expectedMm) > 0.05)
                described["warning"] = "is " + Convert.ToDouble(mm, Cg).ToString("0.##", Cg) + " instead of " + expectedMm.ToString("0.##", Cg) +
                                       ": the two edges are not the ones the geometry says are " + label.Replace("overall ", "") + " apart";
            if (dimension != null) born.Add(described);
            else RecordBornWithoutObject(born, described, failed,
                label + " (the dimension was born but SOLIDWORKS did not hand it back: its value was not read back, check it on the sheet)");
            already.UnionWith(keys);
            overall.Add(new Dictionary<string, object>
            {
                ["kind"] = label,
                ["mm"] = described.ContainsKey("mm") ? described["mm"] : null,
                ["edges"] = new object[] { first.Index, second.Index },
                ["model_key"] = keys[0],
            });
            return true;
        }

        /// <summary>
        /// THE THICKNESS of the sheet: between two parallel edges of the outline one thickness apart that overlap along their
        /// length. One per drawing: it is a single element.
        /// </summary>
        private void ThicknessOfTheSheet(
            double thicknessMm, IModelDoc2 model, IDrawingDoc drawing, object[] edges, List<ProjectedEdge> all,
            HashSet<string> already, List<CarriedDimension> carried, double[] contour, double dimensionOffset, double planStep, double scale,
            ref int below, ref int sideways, ref int onLeft, double[] sheet,
            System.Collections.IList born, System.Collections.IList overall,
            System.Collections.IList elsewhere, System.Collections.IList failed)
        {
            string said = "thickness " + thicknessMm.ToString("0.##", Cg) + " mm";
            if (already.Contains(GapKey(thicknessMm)))
            {
                elsewhere.Add(said + " (already dimensioned in this drawing)");
                return;
            }
            if (CarriesTheValue(carried, thicknessMm, 0))
            {
                elsewhere.Add(said + " (the view already carries a dimension of the model with this value)");
                return;
            }
            ProjectedEdge a = null, b = null;
            bool horizontalEdges = false;
            double best = 0;
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                {
                    var p = all[i]; var q = all[j];
                    if (!IsOutlineLine(p) || !IsOutlineLine(q) || p.Facing != q.Facing || (p.Facing != "horizontal" && p.Facing != "vertical")) continue;
                    bool horiz = p.Facing == "horizontal";
                    double gap = Math.Abs(horiz ? p.FromSheet[1] - q.FromSheet[1] : p.FromSheet[0] - q.FromSheet[0]) / scale * 1000.0;
                    if (Math.Abs(gap - thicknessMm) > 0.01) continue;
                    // how much of their length they share, on the sheet
                    double p0 = horiz ? Math.Min(p.FromSheet[0], p.ToSheet[0]) : Math.Min(p.FromSheet[1], p.ToSheet[1]);
                    double p1 = horiz ? Math.Max(p.FromSheet[0], p.ToSheet[0]) : Math.Max(p.FromSheet[1], p.ToSheet[1]);
                    double q0 = horiz ? Math.Min(q.FromSheet[0], q.ToSheet[0]) : Math.Min(q.FromSheet[1], q.ToSheet[1]);
                    double q1 = horiz ? Math.Max(q.FromSheet[0], q.ToSheet[0]) : Math.Max(q.FromSheet[1], q.ToSheet[1]);
                    double shared = Math.Min(p1, q1) - Math.Max(p0, q0);
                    if (shared > best + 1e-9) { best = shared; a = p; b = q; horizontalEdges = horiz; }
                }
            // A view that does not show the thickness says nothing: it is not a failure.
            if (a == null || best < 0.001) return;

            int axis; double ca, cb;
            if (!AxisSeparation(a.FromModel, a.ToModel, b.FromModel, b.ToModel, thicknessMm, out axis, out ca, out cb)) return;
            double cx = (a.FromSheet[0] + b.FromSheet[0]) / 2, cy = (a.FromSheet[1] + b.FromSheet[1]) / 2;
            double x, y;
            if (horizontalEdges)
            {
                // two horizontal edges: the distance is vertical, and the dimension goes at the side
                y = cy;
                x = SideX(contour, dimensionOffset, planStep, ref sideways, ref onLeft, sheet);
            }
            else
            {
                x = cx;
                y = contour[1] - dimensionOffset - below * planStep;
                below++;
            }
            if (!DimensionBetweenEdges("thickness", !horizontalEdges, a, b, x, y, thicknessMm, new[] { GapKey(thicknessMm), ExtentKey(axis, ca, cb) },
                                       model, drawing, edges, already, born, overall, failed))
            {
                if (horizontalEdges) { if (x > contour[2]) sideways--; else onLeft--; }
                else below--;
            }
        }

        /// <summary>
        /// THE BEND RADIUS: the arcs the view shows face-on (a bend seen from above is a straight line, and a radius cannot
        /// be dimensioned on it) that are not the boundary of a chamfer strip. Of two arcs with the same centre and one
        /// thickness of difference (the two faces of a bend) only the inner one is dimensioned; the outer is the inner
        /// plus the thickness. A radius is one element per value. The text goes on the side of the centre (the free side of
        /// an inner bend), on the bisector of the arc.
        /// </summary>
        private void BendRadii(
            double thicknessMm, IModelDoc2 model, IDrawingDoc drawing, object[] edges, List<ProjectedEdge> all,
            HashSet<string> already, double[] contour, double dimensionOffset, double scale,
            System.Collections.IList born, System.Collections.IList overall,
            System.Collections.IList elsewhere, System.Collections.IList failed)
        {
            var arcs = all.FindAll(s => s.IsArc && s.ArcFaceOn && !s.ChamferBoundaryFaceOn && !s.ChamferMiter && s.RadiusMm > 0 && s.CenterSheet != null);
            foreach (var arc in arcs)
            {
                // the other face of the bend: another arc with the same centre
                bool outer = false, hasOuter = false;
                foreach (var other in arcs)
                {
                    if (ReferenceEquals(other, arc)) continue;
                    if (Math.Abs(other.CenterSheet[0] - arc.CenterSheet[0]) > 1e-5 || Math.Abs(other.CenterSheet[1] - arc.CenterSheet[1]) > 1e-5) continue;
                    if (!double.IsNaN(thicknessMm) && Math.Abs(arc.RadiusMm - other.RadiusMm - thicknessMm) < 0.01) outer = true;
                    if (!double.IsNaN(thicknessMm) && Math.Abs(other.RadiusMm - arc.RadiusMm - thicknessMm) < 0.01) hasOuter = true;
                }
                // Only the INNER arc of a bend: the end of a slot, a fillet on a solid part are not a bend (they have no arc one
                // thickness away with the same centre), and the model dimensions of the slot arrive by themselves.
                if (outer || !hasOuter) continue;

                string key = "R|" + Math.Round(arc.RadiusMm, 2).ToString("0.00", Cg);
                string said = "bend R" + arc.RadiusMm.ToString("0.##", Cg);
                if (already.Contains(key))
                {
                    if (!elsewhere.Contains(said + " (already dimensioned in this drawing)")) elsewhere.Add(said + " (already dimensioned in this drawing)");
                    continue;
                }

                // WHERE THE TEXT GOES, on the bisector of the arc: on the side of the centre for an inner bend, outside otherwise.
                double sx = arc.FromSheet[0] - arc.CenterSheet[0], sy = arc.FromSheet[1] - arc.CenterSheet[1];
                double ex = arc.ToSheet[0] - arc.CenterSheet[0], ey = arc.ToSheet[1] - arc.CenterSheet[1];
                double bx = sx + ex, by = sy + ey, bl = Math.Sqrt(bx * bx + by * by);
                if (bl < 1e-9) { bx = sx; by = sy; bl = Math.Sqrt(bx * bx + by * by); }
                if (bl < 1e-9) continue;
                bx /= bl; by /= bl;
                double radiusSheet = arc.RadiusMm * scale / 1000.0;
                double reach = dimensionOffset;
                double x = hasOuter ? arc.CenterSheet[0] - bx * reach : arc.CenterSheet[0] + bx * (radiusSheet + reach);
                double y = hasOuter ? arc.CenterSheet[1] - by * reach : arc.CenterSheet[1] + by * (radiusSheet + reach);

                int before = CountDimensions(drawing);
                IDisplayDimension dimension = null;
                try
                {
                    model.ClearSelection2(true);
                    if ((edges[arc.Index] as IEntity).Select4(false, null))
                        dimension = model.Extension.AddDimension(x, y, 0, 0) as IDisplayDimension;
                }
                catch { }
                try { model.EditRebuild3(); } catch { }
                if (NotGrown(drawing, before, dimension))
                {
                    failed.Add(said + " (the dimension on the arc " + arc.Index + " was not created)");
                    continue;
                }
                already.UnionWith(new[] { key });
                var described = DescribeDimension(dimension, arc.Index);
                described["orientation"] = "bend radius";
                described["position_mm"] = WhereItEnded(dimension);
                object mm;
                if (described.TryGetValue("mm", out mm) && mm != null && Math.Abs(Convert.ToDouble(mm, Cg) - arc.RadiusMm) > 0.05)
                    described["warning"] = "is " + Convert.ToDouble(mm, Cg).ToString("0.##", Cg) + " instead of the radius " + arc.RadiusMm.ToString("0.##", Cg) +
                                           ": a diameter, or another arc";
                if (dimension != null) born.Add(described);
                else RecordBornWithoutObject(born, described, failed,
                    said + " (the dimension was born but SOLIDWORKS did not hand it back: its value was not read back, check it on the sheet)");
                overall.Add(new Dictionary<string, object>
                {
                    ["kind"] = "bend radius",
                    ["mm"] = described.ContainsKey("mm") ? described["mm"] : null,
                    ["edges"] = new object[] { arc.Index },
                    ["model_key"] = key,
                });
            }
        }
    }
}
