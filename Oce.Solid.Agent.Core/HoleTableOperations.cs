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
    ///          all «Ø8 THRU»: the positions from the corner, right
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
            // THE VERTICES OF EVERY PLANAR FACE THAT LOOKS AT THE READER, not only of the drilled one: the corner of
            // the part may belong to another face (the L bracket: the free end of the other flange). They are the
            // candidates for the origin of the table (0.131.0, item 4), as model vertices, the same kind of object
            // the origin has always been.
            var facingVertices = new List<IVertex>();
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
                        CollectVertices(f, facingVertices);
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

            // ── THE CORNER OF THE PART IN THIS VIEW, AND THE ORIGIN'S DISTANCE FROM IT (0.131.0, item 4) ──
            //
            // The proof session of 12/10/2026 (L bracket, sheet metal, chamfer 1 on the outline) got a table
            // saying (14, 29) and (74, 29) for holes that are 30 and 90 from the free edge and 30 from the sides,
            // and answered «positions_checked: true». The check compared the table with the holes MEASURED FROM
            // THE SAME VERTEX, so it could not fail: the vertex was the corner of the FLAT face (after the bend
            // radius 16 and the chamfer 1), not the corner of the part. Here the corner of the part is read from
            // the edges the view shows (IView~GetVisibleEntities2, IEdge~GetCurveParams3 and ICurve~Evaluate2,
            // sampled so that arcs count), the origin vertex is the visible vertex NEAREST to that corner
            // (any vertex of the view may be the datum: IView~InsertHoleTable3.html, Remarks «Datum origin
            // vertex, mark 1»), and the distance between the two is in the answer.
            double[] corner = PartCornerOnSheet(view, onSheet, sx, sy);
            double cornerGap = corner == null ? double.NaN : Math.Sqrt(Math.Pow(vx - corner[0], 2) + Math.Pow(vy - corner[1], 2));
            // The candidates for the origin, nearest to the corner of the part first; the vertex of the drilled
            // face found above stays at the end as the fallback, so a vertex the view cannot select (a hidden one)
            // never makes the table fail where it used to be born.
            var originCandidates = new List<Tuple<double, IVertex, double, double>>();
            if (corner != null)
            {
                foreach (var v in facingVertices)
                {
                    var p = v.GetPoint() as double[];
                    if (p == null) continue;
                    var s = onSheet(p);
                    if (s == null) continue;
                    originCandidates.Add(Tuple.Create(Math.Sqrt(Math.Pow(s[0] - corner[0], 2) + Math.Pow(s[1] - corner[1], 2)), v, s[0], s[1]));
                }
                originCandidates.Sort((p, q) => p.Item1.CompareTo(q.Item1));
            }
            var firstVertex = vertex; double firstVx = vx, firstVy = vy;

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
            bool okV = false;
            int tried = 0;
            var toTry = new List<Tuple<double, IVertex, double, double>>(originCandidates.GetRange(0, Math.Min(originCandidates.Count, 6)));
            toTry.Add(Tuple.Create(double.NaN, firstVertex, firstVx, firstVy));
            foreach (var cand in toTry)
            {
                tried++;
                model.ClearSelection2(true);
                if ((cand.Item2 as IEntity).Select4(false, sdV))
                {
                    okV = true; vertex = cand.Item2; vx = cand.Item3; vy = cand.Item4;
                    cornerGap = double.IsNaN(cand.Item1) ? (corner == null ? double.NaN : Math.Sqrt(Math.Pow(vx - corner[0], 2) + Math.Pow(vy - corner[1], 2))) : cand.Item1;
                    break;
                }
            }
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
            var fromCorner = new List<object>();   // the same rows, from the corner of the part (item 4)
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
                object[] fromThePartCorner = null;
                if (numbers)
                    foreach (var f in holes)
                    {
                        double dx = Math.Abs(f.X - vx) / scale * 1000.0, dy = Math.Abs(f.Y - vy) / scale * 1000.0;
                        if (Math.Abs(dx - Math.Abs(lx)) <= 0.05 && Math.Abs(dy - Math.Abs(ly)) <= 0.05)
                        {
                            found = true;
                            // THE SAME HOLE, FROM THE CORNER OF THE PART: in the order of the table, not of the list of holes.
                            if (corner != null)
                                fromThePartCorner = new object[] {
                                    Math.Round(Math.Abs(f.X - corner[0]) / scale * 1000.0, 2),
                                    Math.Round(Math.Abs(f.Y - corner[1]) / scale * 1000.0, 2) };
                            break;
                        }
                    }
                if (found) match++;
                if (fromThePartCorner != null) fromCorner.Add(fromThePartCorner);
                entries.Add(new Dictionary<string, object>
                {
                    ["tag"] = tag,
                    ["x_mm"] = numbers ? (object)lx : sx0,
                    ["y_mm"] = numbers ? (object)ly : sy0,
                    ["measure"] = measure,
                    ["in_model"] = found,
                    ["from_part_corner_mm"] = fromThePartCorner,
                });
            }
            if (match != holes.Count)
            {
                string removed = RemoveTable(model, drawing, table, tablesBefore);
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "The hole table says {0} positions and only {1} correspond to a hole of the part measured from " +
                    "the origin on the view: it is not the table of these holes.{2}", holes.Count, match, removed));
            }

            bool originOnCorner = !double.IsNaN(cornerGap) && cornerGap / scale * 1000.0 <= 0.05;
            var measurements = new Dictionary<string, object>
            {
                ["view"] = viewName,
                ["origin"] = origin,
                ["holes"] = holes.Count,
                ["rows"] = lines,
                ["entries"] = entries.ToArray(),
                // ONLY TRUE WHEN IT WAS CHECKED AGAINST THE PART (0.131.0, item 4). The check above compares the
                // table with the holes measured from the origin vertex: it says the table is faithful to THAT
                // vertex, and nothing about whether the vertex is where the workshop measures from. So
                // «positions_checked» is true when the origin vertex IS the corner of the part in the view, and
                // false (with the numbers the workshop would expect, and the gap) when it is not or when the corner
                // could not be read.
                ["positions_checked"] = originOnCorner,
                ["positions_checked_against_origin_vertex"] = true,
                ["origin_gap_to_part_corner_mm"] = double.IsNaN(cornerGap) ? null : (object)Math.Round(cornerGap / scale * 1000.0, 3),
                ["origin_on_part_corner"] = double.IsNaN(cornerGap) ? null : (object)originOnCorner,
                ["positions_from_part_corner_mm"] = fromCorner.ToArray(),
                ["tables_in_drawing"] = DrawingTables(drawing),
            };
            if (!originOnCorner)
                measurements["origin_warning"] = double.IsNaN(cornerGap)
                    ? "I could not read the corner of the part in this view, so I cannot say that the table is measured from it: the positions are checked against the origin vertex only."
                    : "The origin of the table is the vertex at " + Math.Round(cornerGap / scale * 1000.0, 2).ToString("0.##", CultureInfo.InvariantCulture) +
                      " mm from the corner of the part in this view (the part has no vertex exactly at the corner: a bend, a fillet or a chamfer rounds it off). " +
                      "The table is exact from THAT vertex; «positions_from_part_corner_mm» gives the positions from the corner of the part, which is what is usually dimensioned.";
            if (!string.IsNullOrEmpty(tableModel)) measurements["table_template"] = LocalCopies.Original(tableModel);
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

        private static void CollectVertices(IFace2 f, System.Collections.IList into)
        {
            try
            {
                foreach (var oe in (f.GetEdges() as object[]) ?? new object[0])
                {
                    var e = oe as IEdge;
                    foreach (var ov in new[] { e?.GetStartVertex(), e?.GetEndVertex() })
                    {
                        var v = ov as IVertex;
                        if (v != null) into.Add(v);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// The corner of the part in the view, on the sheet (metres): the extreme of the visible edges
        /// in the direction of the corner asked for (sx, sy = -1 left/bottom, +1 right/top). Arcs and
        /// circles are sampled along their parameter range, so a rounded corner counts. Null if the view
        /// shows no edge that can be read.
        /// </summary>
        private static double[] PartCornerOnSheet(IView view, Func<double[], double[]> onSheet, int sx, int sy)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            int points = 0;
            try
            {
                foreach (var oe in (view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]) ?? new object[0])
                {
                    var e = oe as IEdge;
                    if (e == null) continue;
                    ICurve curve;
                    var pd = ApiHelpers.EdgeParameters(e, out curve);
                    if (pd == null) continue;
                    var samples = new List<double[]>();
                    double u0, u1;
                    if (curve != null && ApiHelpers.EdgeRangeOnCurve(pd, curve, out u0, out u1))
                        for (int k = 0; k <= 8; k++)
                        {
                            var q = curve.Evaluate2(u0 + (u1 - u0) * k / 8.0, 0) as double[];
                            if (q != null && q.Length >= 3) samples.Add(q);
                        }
                    else
                    {
                        var a = pd.StartPoint as double[]; var b = pd.EndPoint as double[];
                        if (a != null) samples.Add(a);
                        if (b != null) samples.Add(b);
                    }
                    foreach (var q in samples)
                    {
                        var s = onSheet(new[] { q[0], q[1], q[2] });
                        if (s == null) continue;
                        points++;
                        minX = Math.Min(minX, s[0]); maxX = Math.Max(maxX, s[0]);
                        minY = Math.Min(minY, s[1]); maxY = Math.Max(maxY, s[1]);
                    }
                }
            }
            catch { return null; }
            if (points == 0) return null;
            return new[] { sx < 0 ? minX : maxX, sy < 0 ? minY : maxY };
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
                    var curve = (edges[0] as IEdge)?.GetCurve() as ICurve;
                    if (curve == null || !curve.IsCircle()) continue;
                    var cp = curve.CircleParams as double[];   // centre x,y,z, axis, radius
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
                foreach (var declared in folders.Split(';'))
                {
                    // Its local copy when OCE_LOCAL_COPIES asks for it (LocalCopies.cs).
                    string c = LocalCopies.Of(declared);
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
