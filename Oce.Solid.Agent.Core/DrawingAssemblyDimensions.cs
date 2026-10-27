using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// DIMENSIONS ON A VIEW OF AN ASSEMBLY (report 8cd843 N23, round 7, 27/10/2026).
    ///
    /// ── WHAT HAPPENED ────────────────────────────────────────────────────
    ///
    /// `drawing_dimension` with «count» 3 on the front view of the clip
    /// assembly (8 components, 178 edges) put 1.725 mm on the first edge of one
    /// clip and two chamfer dimensions of 0.4 and -0.4 mm (angles 15.23° and
    /// 159.25°), and answered «succeeded». Two causes, both measured:
    ///
    ///  1. the edges of an assembly view come in the coordinates of their
    ///     PART, and they were brought onto the sheet without the component's
    ///     placement (fixed in ToSheetOf, for every route);
    ///  2. the route of a part — the first «count» edges, the holes, the
    ///     chamfers — is the wrong question on an assembly: its first edges are
    ///     details of one component, and what the workshop needs from an
    ///     assembly drawing is its OVERALL SIZE, which could not be asked.
    ///
    /// ── WHAT THIS ROUTE DOES ─────────────────────────────────────────────
    ///
    /// The overall length and height of the view, each between the two
    /// outermost STRAIGHT edges the view shows across that direction, with
    /// three checks that the number is the overall one: the two edges are the
    /// extremes of everything the view shows (no curve or circle sticks out
    /// beyond them), the view is a front, top or side view (one axis of the
    /// assembly along each direction of the sheet), and the value read back
    /// equals the distance of the two edges on the sheet divided by the scale.
    /// A dimension that fails the last check is taken away. The same overall
    /// already dimensioned in another view of the same assembly is not repeated
    /// (UNI ISO 129-1: once per drawing).
    ///
    /// And the edges NAMED with «edges», each with its placement applied. No
    /// hole callouts, chamfer dimensions or diameters: they are details of a
    /// part, and they go on that part's drawing.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// Is the view a view of an assembly? Its referenced document says so;
        /// when it does not answer (a section view has none), an edge that
        /// belongs to a component does.
        /// </summary>
        private static bool ViewOfAnAssembly(IView view, List<ProjectedEdge> all, List<Circle> circles)
        {
            try
            {
                var shown = view.ReferencedDocument as IModelDoc2;
                if (shown != null) return shown.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY;
            }
            catch { }
            return all.Exists(s => s.Component != null) || circles.Exists(c => c.Component != null);
        }

        /// <summary>
        /// What «overall» asks for: «both», «horizontal», «vertical» or «none»,
        /// or null with the reason. Left out, «both» when no edge is named and
        /// «none» when edges are named. Pure, so the contract suite reads it.
        /// </summary>
        internal static string OverallMode(object raw, bool edgesNamed, out string problem)
        {
            problem = null;
            if (raw == null) return edgesNamed ? "none" : "both";
            if (raw is bool) return (bool)raw ? "both" : "none";
            var word = (raw as string ?? "").Trim().ToLowerInvariant();
            if (word == "both" || word == "horizontal" || word == "vertical" || word == "none") return word;
            problem = "«overall» is written as «both», «horizontal», «vertical» or «none», not «" +
                      Convert.ToString(raw, Ci) + "».";
            return null;
        }

        /// <summary>
        /// The key of an overall dimension between views: the axis of the
        /// assembly it runs along and its millimetres to the hundredth. Pure.
        /// </summary>
        internal static string OverallKey(int axis, double mm)
        {
            return (axis >= 0 && axis < 3 ? "XYZ"[axis].ToString() : "?") + "|" + Math.Round(mm, 2).ToString("0.00", Ci);
        }

        /// <summary>
        /// Why the outermost straight edges are not the overall size along one
        /// direction of the sheet, or null. <paramref name="lowEdge"/> and
        /// <paramref name="highEdge"/> are the positions of the outermost
        /// straight edges across that direction, <paramref name="lowAll"/> and
        /// <paramref name="highAll"/> the extremes of everything the view shows,
        /// all in sheet metres; <paramref name="scale"/> brings a beyond-amount
        /// back to the model. Pure, so the contract suite reads it.
        /// </summary>
        internal static string OverallEdgesProblem(bool horizontal, double lowEdge, double highEdge,
                                                   double lowAll, double highAll, double scale)
        {
            const double tolerance = 1e-5;   // 0.01 mm on the sheet
            string low = horizontal ? "left" : "bottom", high = horizontal ? "right" : "top";
            if (highEdge - lowEdge < tolerance)
                return "there are not two different straight edges across the " + (horizontal ? "length" : "height") +
                       " of the view";
            if (scale <= 0) scale = 1;
            if (lowEdge - lowAll > tolerance)
                return "on the " + low + " the view goes " + ((lowEdge - lowAll) / scale * 1000).ToString("0.##", Ci) +
                       " mm beyond the last straight edge (a curve or a circle sticks out)";
            if (highAll - highEdge > tolerance)
                return "on the " + high + " the view goes " + ((highAll - highEdge) / scale * 1000).ToString("0.##", Ci) +
                       " mm beyond the last straight edge (a curve or a circle sticks out)";
            return null;
        }

        private Outcome DimensionAssemblyView(
            IModelDoc2 model, IDrawingDoc drawing, IView view, string viewName, object[] edges,
            List<ProjectedEdge> all, List<Circle> circles, List<int> named, Dictionary<string, object> a,
            double[] contour, double dimensionOffset, double planStep, double scale, IMathUtility mu, MathTransform tr)
        {
            string modeProblem;
            string mode = OverallMode(a.ContainsKey("overall") ? a["overall"] : null, named != null, out modeProblem);
            if (mode == null)
                return Failed("I touched nothing: " + modeProblem + " On a view of an assembly use «overall» «both» " +
                              "for the overall length and height.");
            if (mode == "none" && named == null)
                return Failed(
                    "On a view of an assembly I put the overall dimensions or the dimensions of the edges you name, " +
                    "and «overall» «none» asks for neither: pass «overall» «both», or name the edges with «edges» " +
                    "(read them with «inspect_part» and «view_edges»). I touched nothing.");

            var components = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in all) if (s.Component != null) components.Add(s.Component);
            foreach (var c in circles) if (c.Component != null) components.Add(c.Component);

            var axes = SheetAxes(mu, tr);
            double[] sheetSize = SheetDimensions(drawing);

            int before = CountDimensions(drawing);
            if (before < 0)
                return Failed(
                    "I cannot count the dimensions of the drawing before starting: without that number I could not " +
                    "tell you which ones I put. I touched nothing: try again once the drawing answers.");

            var failed = new List<string>();
            var elsewhere = new List<string>();
            var born = new List<Dictionary<string, object>>();
            var overallRead = new Dictionary<string, object>();
            var alreadyOverall = new HashSet<string>(OverallKeysAlready(drawing, view, mu), StringComparer.Ordinal);

            bool askedForTheValue = true, off = false;
            try
            {
                try
                {
                    askedForTheValue = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                    _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
                    off = true;
                }
                catch { /* if it cannot be switched off, the first one that hangs will say so */ }

                int below = 0, sideways = 0, onLeft = 0;

                // ── THE EDGES NAMED, each with its component's placement ──────
                var seen = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var i in named ?? new List<int>())
                {
                    var circle = circles.Find(c => c.Index == i);
                    if (circle != null)
                    {
                        failed.Add(i + " (a circle Ø" + circle.DiameterMm.ToString("0.##", Ci) + " of «" + (circle.Component ?? "?") +
                                   "»: on a view of an assembly I put no diameters or hole callouts, they go on the drawing of the part)");
                        continue;
                    }
                    var s = all.Find(t => t.Index == i);
                    if (s == null) { failed.Add(i + " (I cannot measure it on the sheet)"); continue; }
                    if (seen.ContainsKey(s.Key)) { failed.Add(i + " (same measure as the edge " + seen[s.Key] + ")"); continue; }
                    seen[s.Key] = i;
                    double x, y;
                    if (s.Facing == "horizontal") { x = s.Half; y = contour[1] - dimensionOffset - below * planStep; below++; }
                    else if (s.Facing == "vertical")
                    {
                        x = contour[2] + dimensionOffset + sideways * planStep; y = s.Half; sideways++;
                        if (sheetSize != null && x > sheetSize[0] - 0.015)
                        {
                            sideways--;
                            x = contour[0] - dimensionOffset - onLeft * planStep;
                            onLeft++;
                        }
                    }
                    else { x = contour[0] - dimensionOffset - onLeft * planStep; y = (contour[1] + contour[3]) / 2; onLeft++; }

                    int dimensionsBefore = CountDimensions(drawing);
                    IDisplayDimension d = null;
                    try
                    {
                        model.ClearSelection2(true);
                        var ent = edges[i] as IEntity;
                        if (ent != null && ent.Select4(false, null))
                            d = model.Extension.AddDimension(x, y, 0, 0) as IDisplayDimension;
                        try { model.EditRebuild3(); } catch { }
                    }
                    catch (Exception ex) { failed.Add(i + " (" + Shorter(ex.Message) + ")"); continue; }
                    if (NotGrown(drawing, dimensionsBefore, d)) { failed.Add(i + " (the call went through, the dimension was not created)"); continue; }
                    var described = DescribeDimension(d, i);
                    object readBack;
                    if (d != null && described.TryGetValue("mm", out readBack) && readBack is double && (double)readBack <= 0)
                    {
                        bool gone = RemoveNewAnnotation(model, d.GetAnnotation() as IAnnotation, () => CountDimensions(drawing), dimensionsBefore);
                        failed.Add(i + " (the dimension read " + ((double)readBack).ToString("0.###", Ci) +
                                   " mm, and a length of zero or less measures nothing" +
                                   (gone ? ": I took it away)" : ": I could not take it away, remove it with «delete_annotation»)"));
                        continue;
                    }
                    described["orientation"] = s.Facing;
                    described["component"] = s.Component;
                    described["position_mm"] = WhereItEnded(d);
                    if (d != null) born.Add(described);
                    else RecordBornWithoutObject(born, described, failed,
                        i + " (the dimension was born but SOLIDWORKS did not hand it back: its value was not read back, check it on the sheet)");
                }

                // ── THE OVERALL SIZE ──────────────────────────────────────────
                foreach (bool horizontal in new[] { true, false })
                {
                    if (mode == "none" || (mode == "horizontal" && !horizontal) || (mode == "vertical" && horizontal)) continue;
                    string word = horizontal ? "overall length" : "overall height";
                    int axis = horizontal ? axes[0] : axes[1];
                    if (axis < 0)
                    {
                        failed.Add(word + " (no axis of the assembly runs along the " + (horizontal ? "horizontal" : "vertical") +
                                   " of this view: an isometric or turned view has no overall dimension, use a front, top or side view)");
                        continue;
                    }
                    int k = horizontal ? 0 : 1;
                    string across = horizontal ? "vertical" : "horizontal";
                    double lowAll = double.MaxValue, highAll = double.MinValue;
                    ProjectedEdge low = null, high = null;
                    foreach (var s in all)
                    {
                        lowAll = Math.Min(lowAll, Math.Min(s.FromSheet[k], s.ToSheet[k]));
                        highAll = Math.Max(highAll, Math.Max(s.FromSheet[k], s.ToSheet[k]));
                        if (!s.Straight || s.Facing != across) continue;
                        if (low == null || s.FromSheet[k] < low.FromSheet[k]) low = s;
                        if (high == null || s.FromSheet[k] > high.FromSheet[k]) high = s;
                    }
                    foreach (Circle c in circles)
                    {
                        // A circle seen face-on reaches its centre ± radius on the sheet; one
                        // seen edge-on lies along edges that are already counted.
                        if (c.Type == "edge_on") continue;
                        double r = c.DiameterMm / 2000.0 * scale;
                        lowAll = Math.Min(lowAll, c.SheetCenter[k] - r);
                        highAll = Math.Max(highAll, c.SheetCenter[k] + r);
                    }
                    if (low == null || high == null)
                    {
                        failed.Add(word + " (the view shows no straight " + across + " edge to rest it on: dimension it by hand)");
                        continue;
                    }
                    string problem = OverallEdgesProblem(horizontal, low.FromSheet[k], high.FromSheet[k], lowAll, highAll, scale);
                    if (problem != null)
                    {
                        failed.Add(word + " (" + problem + ": I do not put an overall dimension that is not the overall one, dimension it by hand)");
                        continue;
                    }
                    double expectedMm = (high.FromSheet[k] - low.FromSheet[k]) / scale * 1000.0;
                    string key = OverallKey(axis, expectedMm);
                    if (alreadyOverall.Contains(key))
                    {
                        elsewhere.Add(word + " " + expectedMm.ToString("0.##", Ci) + " mm along " + "XYZ"[axis] +
                                      " (already dimensioned in another view of this assembly)");
                        continue;
                    }

                    double x, y;
                    if (horizontal)
                    {
                        x = (low.FromSheet[0] + high.FromSheet[0]) / 2;
                        y = contour[1] - dimensionOffset - below * planStep;
                        below++;
                    }
                    else
                    {
                        y = (low.FromSheet[1] + high.FromSheet[1]) / 2;
                        x = contour[2] + dimensionOffset + sideways * planStep;
                        sideways++;
                        if (sheetSize != null && x > sheetSize[0] - 0.015)
                        {
                            sideways--;
                            x = contour[0] - dimensionOffset - onLeft * planStep;
                            onLeft++;
                        }
                    }

                    int dimensionsBefore = CountDimensions(drawing);
                    IDisplayDimension d = null;
                    string route = "AddDimension";
                    try
                    {
                        model.ClearSelection2(true);
                        if ((edges[low.Index] as IEntity).Select4(false, null) && (edges[high.Index] as IEntity).Select4(true, null))
                            d = model.Extension.AddDimension(x, y, 0, 0) as IDisplayDimension;
                        try { model.EditRebuild3(); } catch { }
                        if (NotGrown(drawing, dimensionsBefore, d))
                        {
                            // The second road, the one the sketch anchors use for an edge
                            // of the model (Horizontal/Vertical2 chosen by the direction).
                            route = horizontal ? "AddHorizontalDimension2" : "AddVerticalDimension2";
                            model.ClearSelection2(true);
                            if ((edges[low.Index] as IEntity).Select4(false, null) && (edges[high.Index] as IEntity).Select4(true, null))
                                d = (horizontal ? model.AddHorizontalDimension2(x, y, 0) : model.AddVerticalDimension2(x, y, 0)) as IDisplayDimension;
                            try { model.EditRebuild3(); } catch { }
                        }
                    }
                    catch (Exception ex) { failed.Add(word + " (" + Shorter(ex.Message) + ")"); continue; }
                    if (NotGrown(drawing, dimensionsBefore, d))
                    {
                        failed.Add(word + " (between the edges " + low.Index + " and " + high.Index + " the dimension was not created)");
                        continue;
                    }
                    var described = DescribeDimension(d, low.Index);
                    object mm;
                    described.TryGetValue("mm", out mm);
                    if (d != null && (!(mm is double) || Math.Abs((double)mm - expectedMm) > 0.05))
                    {
                        bool gone = RemoveNewAnnotation(model, d.GetAnnotation() as IAnnotation, () => CountDimensions(drawing), dimensionsBefore);
                        failed.Add(word + " (SOLIDWORKS wrote " + (mm is double ? ((double)mm).ToString("0.###", Ci) : "?") + " mm instead of " +
                                   expectedMm.ToString("0.###", Ci) + (gone ? ": I took it away)" : ": remove it with «delete_annotation»)"));
                        continue;
                    }
                    described["orientation"] = horizontal ? "overall, horizontal" : "overall, vertical";
                    described["model_axis"] = "XYZ"[axis].ToString();
                    described["edges"] = new object[] { low.Index, high.Index };
                    described["components"] = new object[] { low.Component, high.Component };
                    described["expected_mm"] = Math.Round(expectedMm, 3);
                    described["placed_with"] = route;
                    described["position_mm"] = WhereItEnded(d);
                    if (d != null)
                    {
                        born.Add(described);
                        overallRead[horizontal ? "horizontal" : "vertical"] = mm;
                        alreadyOverall.Add(key);
                    }
                    else RecordBornWithoutObject(born, described, failed,
                        word + " (the dimension was born but SOLIDWORKS did not hand it back: its value was not read back, check it on the sheet)");
                }
            }
            finally
            {
                if (off)
                    try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, askedForTheValue); }
                    catch { }
                try { model.ClearSelection2(true); } catch { }
            }

            int after = CountDimensions(drawing);
            if (after < 0)
                return Failed(
                    "I put the dimensions in the view «" + viewName + "», but then I cannot count them again: I cannot " +
                    "tell you how many were created. Look at the drawing, and read it with «inspect_part» before going on.");
            int put = after - before;
            if (put <= 0)
                return Failed(
                    "No dimension was created on the view «" + viewName + "» of the assembly (" + components.Count +
                    " components, " + all.Count + " edges measured): " +
                    string.Join("; ", failed.Concat(elsewhere)) + ". Read the edges with «inspect_part» and «view_edges», " +
                    "and name the ones to dimension with «edges».");

            var texts = DimensionTexts(drawing);
            var overlapping = TextsThatOverlap(drawing, view, texts);
            var measurements = new Dictionary<string, object>
            {
                ["view"] = viewName,
                ["view_of"] = "assembly",
                ["components_in_view"] = components.Count,
                ["edges_available"] = edges.Length,
                ["edges_tried"] = named == null ? 0 : named.Count,
                ["overall"] = mode,
                ["overall_mm"] = overallRead,
                ["sheet_axes"] = new Dictionary<string, object>
                {
                    ["horizontal"] = axes[0] >= 0 ? "XYZ"[axes[0]].ToString() : null,
                    ["vertical"] = axes[1] >= 0 ? "XYZ"[axes[1]].ToString() : null,
                },
                ["dimensions_before"] = before,
                ["dimensions_after"] = after,
                ["dimensions_placed"] = put,
                ["dimensions"] = born.ToArray(),
                ["dimensions_mm"] = MillimetersInARow(born),
                ["edges_not_dimensioned"] = failed.ToArray(),
                ["already_dimensioned_elsewhere"] = elsewhere.ToArray(),
                ["already_dimensioned_read_from"] =
                    "the overall dimensions of the other views of this assembly (two straight edges each); a dimension " +
                    "of a single edge is not compared on an assembly",
                ["contour_view_mm"] = InMillimeters(contour),
                ["texts_read"] = texts == null ? null : (object)texts.Count,
                ["overlapping_texts"] = overlapping == null ? null : overlapping.ToArray(),
                ["how_many_texts_overlap"] = texts == null || overlapping == null ? null : (object)overlapping.Count,
                ["offset_used_mm"] = Math.Round(dimensionOffset * 1000, 2),
                ["pitch_used_mm"] = Math.Round(planStep * 1000, 2),
                ["assembly_note"] =
                    "A VIEW OF AN ASSEMBLY: I put the overall length and height (between the outermost straight edges, " +
                    "read back and compared with their distance on the sheet) and the edges you name, each edge brought " +
                    "onto the sheet with its component's placement. No hole callouts, chamfer dimensions or diameters: " +
                    "they are details of a part, and they go on the drawing of that part. The third overall size is " +
                    "dimensioned on a view that shows it (the top or the side view).",
            };
            if (named == null && a.ContainsKey("count"))
                measurements["count_ignored"] =
                    "«count» was not used: on a view of an assembly the first edges the view lists are details of one " +
                    "component. Name the edges with «edges», or leave the overall dimensions.";
            _log("[agent] " + put + " dimensions on the assembly view «" + viewName + "»: " + ValuesInLine(born));
            return WithoutFeature(measurements);
        }

        /// <summary>The most edges one reading of a view lists.</summary>
        public const int MaxViewEdges = 500;

        /// <summary>
        /// THE EDGES OF A DRAWING VIEW, LISTED (report 8cd843 N23, round 7):
        /// «edges_available» 178, and the 178 were nowhere — «edges» of
        /// drawing_dimension was chosen blind. Each edge with the number
        /// drawing_dimension takes, its component on an assembly, what it is
        /// (line, circle, arc or other curve), its millimetres in the model (the
        /// length of a line, the diameter of a circle, the chord of an arc), how it
        /// sits on the sheet and where, the placement of the component applied
        /// (ToSheetOf). And the extent of the view, from what it shows. Reads
        /// only. Null with the reason when the document is not a drawing or the
        /// view is not there. Called by «inspect_part» with «view_edges».
        /// </summary>
        public static Dictionary<string, object> ViewEdgeList(
            IModelDoc2 model, ISldWorks sw, string wanted, string component, int limit, out string problem)
        {
            problem = null;
            var drawing = model as IDrawingDoc;
            if (drawing == null)
            {
                problem = "«view_edges» lists the edges of a drawing view, and the document in front is not a drawing: " +
                          "bring the drawing to the front, or ask again without «view_edges».";
                return null;
            }
            var names = new List<string>();
            try
            {
                var w = (drawing.GetFirstView() as IView)?.GetNextView() as IView;
                while (w != null) { try { names.Add(w.GetName2()); } catch { } w = w.GetNextView() as IView; }
            }
            catch { }
            if (names.Count == 0) { problem = "The drawing has no views: there is no edge to list."; return null; }
            string viewReason;
            var view = ResolveView(drawing, names, wanted, out viewReason);
            if (viewReason != null) { problem = viewReason; return null; }
            if (view == null) { problem = "I cannot get the view to read."; return null; }
            if (limit < 1 || limit > MaxViewEdges) limit = 200;

            object[] edges = null;
            try { edges = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; }
            catch (Exception ex) { problem = "I cannot ask the view for its edges: " + ex.Message; return null; }
            IMathUtility mu = null;
            MathTransform tr = null;
            try { mu = sw?.GetMathUtility() as IMathUtility; tr = view.ModelToViewTransform; } catch { }
            if (mu == null || tr == null) { problem = "I cannot read how the view carries the model onto the sheet."; return null; }
            double scale = 1;
            try { scale = view.ScaleDecimal; } catch { }
            if (scale <= 0) scale = 1;

            var listed = new List<Dictionary<string, object>>();
            var perComponent = new Dictionary<string, int>(StringComparer.Ordinal);
            double[] box = { double.MaxValue, double.MaxValue, double.MinValue, double.MinValue };
            int matched = 0, unreadable = 0;
            int total = edges == null ? 0 : edges.Length;
            for (int i = 0; i < total; i++)
            {
                var row = new Dictionary<string, object> { ["edge"] = i };
                string owner = null;
                var c = CircleOnSheet(edges[i] as IEdge, i, mu, tr);
                if (c != null)
                {
                    owner = c.Component;
                    row["kind"] = "circle";
                    row["mm"] = Math.Round(c.DiameterMm, 3);
                    row["center_mm"] = new object[] { Math.Round(c.SheetCenter[0] * 1000, 2), Math.Round(c.SheetCenter[1] * 1000, 2) };
                    row["seen"] = c.Type == "edge_on" ? "edge-on" : "face-on";
                    if (c.Type != "edge_on")
                    {
                        double r = c.DiameterMm / 2000.0 * scale;
                        Grow(box, c.SheetCenter[0] - r, c.SheetCenter[1] - r);
                        Grow(box, c.SheetCenter[0] + r, c.SheetCenter[1] + r);
                    }
                }
                else
                {
                    var s = ReadEdge(view, edges[i], i, mu, tr);
                    if (s == null) { unreadable++; row["kind"] = "unreadable"; }
                    else
                    {
                        owner = s.Component;
                        double dx = s.ToModel[0] - s.FromModel[0], dy = s.ToModel[1] - s.FromModel[1], dz = s.ToModel[2] - s.FromModel[2];
                        row["kind"] = s.Straight ? "line" : "curve";
                        row[s.Straight ? "mm" : "chord_mm"] = Math.Round(Math.Sqrt(dx * dx + dy * dy + dz * dz) * 1000, 3);
                        row["facing"] = s.Facing;
                        row["sheet_mm"] = new object[]
                        {
                            Math.Round(s.FromSheet[0] * 1000, 2), Math.Round(s.FromSheet[1] * 1000, 2),
                            Math.Round(s.ToSheet[0] * 1000, 2), Math.Round(s.ToSheet[1] * 1000, 2),
                        };
                        Grow(box, s.FromSheet[0], s.FromSheet[1]);
                        Grow(box, s.ToSheet[0], s.ToSheet[1]);
                    }
                }
                if (owner != null)
                {
                    row["component"] = owner;
                    int n;
                    perComponent.TryGetValue(owner, out n);
                    perComponent[owner] = n + 1;
                }
                if (component != null && !string.Equals(owner, component, StringComparison.Ordinal)) continue;
                matched++;
                if (listed.Count < limit) listed.Add(row);
            }

            var axes = SheetAxes(mu, tr);
            var outcome = new Dictionary<string, object>
            {
                ["view"] = view.GetName2() ?? "",
                ["scale"] = Math.Round(scale, 4),
                ["edges_total"] = total,
                ["edges_matching"] = matched,
                ["edges_listed"] = listed.Count,
                ["edges_unreadable"] = unreadable,
                ["edges"] = listed.ToArray(),
                ["sheet_axes"] = new Dictionary<string, object>
                {
                    ["horizontal"] = axes[0] >= 0 ? "XYZ"[axes[0]].ToString() : null,
                    ["vertical"] = axes[1] >= 0 ? "XYZ"[axes[1]].ToString() : null,
                },
                ["how_to_use"] =
                    "«edge» is the number «edges» of drawing_dimension takes, for this view and this reading only: " +
                    "a dimension or a view added changes nothing, a model changed can renumber them. «mm» is in the " +
                    "model (the length of a line, the diameter of a circle); «sheet_mm» are the two ends on the sheet.",
            };
            if (component != null) outcome["component_asked"] = component;
            if (perComponent.Count > 0) outcome["edges_per_component"] = perComponent;
            if (box[0] <= box[2])
                outcome["extent_mm"] = new Dictionary<string, object>
                {
                    // In the model: the extent on the sheet divided by the scale.
                    ["horizontal"] = Math.Round((box[2] - box[0]) / scale * 1000, 2),
                    ["vertical"] = Math.Round((box[3] - box[1]) / scale * 1000, 2),
                };
            return outcome;
        }

        private static void Grow(double[] box, double x, double y)
        {
            box[0] = Math.Min(box[0], x); box[1] = Math.Min(box[1], y);
            box[2] = Math.Max(box[2], x); box[3] = Math.Max(box[3], y);
        }

        private static string Shorter(string message)
        {
            var m = message ?? "";
            return m.Length > 90 ? m.Substring(0, 90) + "..." : m;
        }

        /// <summary>
        /// The overall dimensions already on the drawing, as keys
        /// (<see cref="OverallKey"/>): in the views of every sheet that show the
        /// same assembly, every dimension attached to exactly two straight edges
        /// that are parallel on the sheet, vertical (it measures the axis that
        /// runs along that view's horizontal) or horizontal (the axis along its
        /// vertical). Each edge with its component's placement, as ReadEdge does.
        /// </summary>
        private HashSet<string> OverallKeysAlready(IDrawingDoc drawing, IView view, IMathUtility mu)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            List<IView> views = null;
            try { views = ApiHelpers.ViewsOfEverySheet(drawing); } catch { }
            if (views == null)
            {
                var currentSheet = new List<IView>();
                try
                {
                    var w = (drawing.GetFirstView() as IView)?.GetNextView() as IView;   // [0] is the sheet
                    while (w != null) { currentSheet.Add(w); w = w.GetNextView() as IView; }
                }
                catch { }
                views = currentSheet;
            }
            string thisModel = null;
            try { thisModel = ApiHelpers.ReferencedModelKey(view); } catch { }
            foreach (var v in views)
            {
                try
                {
                    if (!ApiHelpers.MayShowTheSameModel(thisModel, ApiHelpers.ReferencedModelKey(v))) continue;
                    var vt = v.ModelToViewTransform;
                    if (vt == null) continue;
                    var viewAxes = SheetAxes(mu, vt);
                    for (IDisplayDimension d = v.GetFirstDisplayDimension5(); d != null; d = d.GetNext5())
                    {
                        try
                        {
                            if (ApiHelpers.IsHiddenByHand(d)) continue;
                            var ents = (d.GetAnnotation() as IAnnotation)?.GetAttachedEntities3() as object[];
                            if (ents == null || ents.Length != 2) continue;
                            var p0 = ReadEdge(v, ents[0], 0, mu, vt);
                            var p1 = ReadEdge(v, ents[1], 1, mu, vt);
                            if (p0 == null || p1 == null || !p0.Straight || !p1.Straight || p0.Facing != p1.Facing) continue;
                            int axis = p0.Facing == "vertical" ? viewAxes[0] : p0.Facing == "horizontal" ? viewAxes[1] : -1;
                            if (axis < 0) continue;
                            double mm = ApiHelpers.ReadDimensionInConfiguration(d.GetDimension2(0) as IDimension, "") * 1000.0;
                            keys.Add(OverallKey(axis, mm));
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return keys;
        }
    }
}
