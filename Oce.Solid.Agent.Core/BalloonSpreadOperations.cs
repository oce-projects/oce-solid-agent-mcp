using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// BALLOONS WHOSE ARROWS END IN THE SAME PLACE (report 8cd843 N56, round 14, 27/10/2026).
    ///
    /// On sheet 2 of round 14 the arrows of balloons 4, 5 and 6 (the coupler and the two
    /// half channels) all ended in the mouth of the coupler: read on a copy, the three tips
    /// 0.7 to 2.3 mm apart, every other pair 39.9 mm or more. A reader could not tell which
    /// part was 5 and which 6. AutoBalloon5 chooses the points; nothing in its options chooses
    /// them apart.
    ///
    /// ── WHAT WAS MEASURED (a probe on a copy of the drawing of round 14) ─────────
    ///
    ///   - IAnnotation.GetLeaderPointsAtIndex(0): two points, the LAST is the arrow tip, in
    ///     sheet coordinates (balloon 4 at x 266 mm: points 261.4 and 253.6 mm);
    ///   - IView.GetPolylines7(1, out lines) gives the lines the view shows, hidden ones left
    ///     out (IView~GetPolylines7.html: «data for all visible solid model edges»), with points
    ///     in the space of the view: on the sheet they are the centre of IView.GetXform plus its
    ///     scale times the point. So mapped, all 7 arrow tips lay on a visible line (0 mm); through
    ///     ModelToViewTransform they lay 14 to 28 mm away;
    ///   - the edges GetPolylines7 hands back have no component (IEntity.GetComponent null), and
    ///     IsSame of 424 lines against the edges of every component ran past 8 minutes. So a line
    ///     is a component's when both its ends lie on that component's visible edges
    ///     (GetVisibleEntities2), tessellated and brought on the sheet with the component's
    ///     placement and ModelToViewTransform, as drawing_dimension does (ToSheetOf): 7.3 s for
    ///     the three components of round 14;
    ///   - IAnnotation.SetLeaderAttachmentPointAtIndex read back a moved arrow, and the PDF showed
    ///     the arrow where it was (one balloon moved instead): not used;
    ///   - SelectByID2("", "EDGE", x, y) at a point of a visible line, then
    ///     IModelDocExtension.InsertBOMBalloon2 (the recipe of «Insert and Show BOM Table and BOM
    ///     Balloon», C#) with swBalloonItemNumbers_DoNotChangeItemNumbers: a balloon with the same
    ///     number, its arrow on that line on the PDF (6 on its half channel near the first clip, 4 on
    ///     the edge of the coupler), put back where the old balloon was.
    ///
    /// So: a balloon whose arrow ends within 5 mm of another gets a new one, inserted on the
    /// point of a visible line of its own component farthest from the other arrows; the old one
    /// is taken away only when the new one carries the same number. Nothing else is touched.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>Two arrow tips closer than this (metres of sheet) are crowded (N56).</summary>
        internal const double CrowdedArrows = 0.005;

        private sealed class BalloonRead
        {
            public IAnnotation Annotation;
            public string Number;
            public double[] Tip;          // x, y of the arrow tip on the sheet, metres
            public double[] Position;     // of the balloon
            public IComponent2 Component;
        }

        /// <summary>
        /// Spreads the balloons of <paramref name="view"/> whose arrows end within
        /// <see cref="CrowdedArrows"/> of another (N56). The moves made, with the reason in
        /// <paramref name="note"/> when one could not be made or nothing was crowded.
        /// </summary>
        private List<Dictionary<string, object>> SpreadCrowdedBalloons(IModelDoc2 model, IView view, out string note)
        {
            note = null;
            var moves = new List<Dictionary<string, object>>();
            var balloons = BalloonsOfView(view);
            var tips = new List<double[]>();
            foreach (var b in balloons) tips.Add(b.Tip);
            var crowded = CrowdedIndices(tips, CrowdedArrows);
            if (crowded.Count == 0) return moves;

            IMathUtility mu = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; } catch { }
            List<double[]> pieces = null;
            try
            {
                object lines;
                var edges = view.GetPolylines7(1, out lines) as object[];
                pieces = PolylinePieces(lines as double[], edges == null ? 0 : edges.Length, view.GetXform() as double[]);
            }
            catch { }
            if (pieces == null || pieces.Count == 0 || mu == null)
            {
                note = "The arrows of the balloons " + NumbersOf(balloons, crowded) + " end within " + CrowdedArrows * 1000 +
                       " mm of each other, and I cannot read the lines the view shows (GetPolylines7) to move them: look at the sheet.";
                return moves;
            }
            var segments = new Dictionary<string, List<double[]>>();
            foreach (int i in crowded)
            {
                var c = balloons[i].Component;
                string n = SafeName(c);
                if (c == null || n == null || segments.ContainsKey(n)) continue;
                segments[n] = ComponentSegmentsOnSheet(view, c, mu);
            }
            var owners = PieceOwners(pieces, segments, 0.00005);

            var left = new List<string>();
            foreach (int i in crowded)
            {
                var b = balloons[i];
                var others = new List<double[]>();
                for (int k = 0; k < tips.Count; k++) if (k != i && tips[k] != null) others.Add(tips[k]);
                double nearBefore = Nearest(b.Tip, others);
                if (nearBefore >= CrowdedArrows) continue;          // an earlier move already freed it
                string mine = SafeName(b.Component);
                var best = mine == null ? null : FarthestOwnPoint(pieces, owners, mine, others, 0.001, 0.0005);
                if (best == null || best[2] < CrowdedArrows) { left.Add(b.Number); continue; }
                var moved = ReinsertBalloon(model, b, best);
                if (moved == null) { left.Add(b.Number); continue; }
                tips[i] = moved;
                moves.Add(new Dictionary<string, object>
                {
                    ["number"] = b.Number,
                    ["component"] = mine,
                    ["arrow_from_mm"] = new object[] { Math.Round(b.Tip[0] * 1000, 2), Math.Round(b.Tip[1] * 1000, 2) },
                    ["arrow_to_mm"] = new object[] { Math.Round(moved[0] * 1000, 2), Math.Round(moved[1] * 1000, 2) },
                    ["nearest_arrow_before_mm"] = Math.Round(nearBefore * 1000, 2),
                    ["nearest_arrow_after_mm"] = Math.Round(Nearest(moved, others) * 1000, 2),
                });
            }
            if (left.Count > 0)
                note = "The arrows of the balloons " + string.Join(", ", left) + " still end within " + CrowdedArrows * 1000 +
                       " mm of another arrow: their part shows no free visible line far enough from the others. Look at the sheet.";
            return moves;
        }

        /// <summary>The BOM balloons of a view with their arrow tip and component.</summary>
        private static List<BalloonRead> BalloonsOfView(IView view)
        {
            var l = new List<BalloonRead>();
            try
            {
                for (var n = view.GetFirstNote() as INote; n != null; n = n.GetNext() as INote)
                {
                    if (!n.IsBomBalloon()) continue;
                    var b = new BalloonRead { Number = (n.GetText() ?? "").Trim(), Annotation = n.GetAnnotation() as IAnnotation };
                    if (b.Annotation == null) continue;
                    try
                    {
                        var pts = b.Annotation.GetLeaderPointsAtIndex(0) as double[];
                        if (pts != null && pts.Length >= 6) b.Tip = new[] { pts[pts.Length - 3], pts[pts.Length - 2] };
                    }
                    catch { }
                    try { b.Position = b.Annotation.GetPosition() as double[]; } catch { }
                    try
                    {
                        foreach (var e in (b.Annotation.GetAttachedEntities3() as object[]) ?? new object[0])
                        {
                            var c = (e as IEntity)?.GetComponent() as IComponent2;
                            if (c != null) { b.Component = c; break; }
                        }
                    }
                    catch { }
                    l.Add(b);
                }
            }
            catch { }
            return l;
        }

        /// <summary>
        /// The visible edges of a component in a view as segments on the sheet (x0, y0, x1, y1 in
        /// metres): tessellated, through the component's placement and the view's transform.
        /// </summary>
        private static List<double[]> ComponentSegmentsOnSheet(IView view, IComponent2 component, IMathUtility mu)
        {
            var segs = new List<double[]>();
            MathTransform toSheet = null;
            try
            {
                var tv = view.ModelToViewTransform as MathTransform;
                var place = component.Transform2 as MathTransform;
                toSheet = place == null ? tv : place.Multiply(tv) as MathTransform;
            }
            catch { }
            if (toSheet == null) return segs;
            // THE TRANSFORM APPLIED HERE, NOT ONE COM CALL PER POINT: 45.8 s for the seven
            // components of round 14 through IMathPoint, 7.3 s for three with the numbers of
            // ArrayData. The order of ArrayData is checked on the first point against
            // IMathPoint (measured: the row order), and with neither order matching every
            // point goes through IMathPoint as before.
            double[] numbers = null;
            try { numbers = toSheet.ArrayData as double[]; } catch { }
            int order = 0;   // 0 not decided, 1 rows, 2 columns, 3 IMathPoint
            object[] edges = null;
            try { edges = view.GetVisibleEntities2((Component2)component, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch { }
            foreach (var o in edges ?? new object[0])
            {
                try
                {
                    var cp = ApiHelpers.EdgeParameters(o as IEdge, out ICurve c);
                    if (c == null || cp == null) continue;
                    var tess = c.GetTessPts(1e-4, 1e-3, cp.StartPoint, cp.EndPoint) as double[];
                    if (tess == null || tess.Length < 6) continue;
                    if (order == 0)
                    {
                        var first = new[] { tess[0], tess[1], tess[2] };
                        order = TransformOrder(numbers, first, OnSheet(first, mu, toSheet), 1e-9);
                    }
                    double[] prev = null;
                    for (int i = 0; i + 2 < tess.Length; i += 3)
                    {
                        var point = new[] { tess[i], tess[i + 1], tess[i + 2] };
                        var q = order == 1 || order == 2 ? Transformed(numbers, point, order == 1) : OnSheet(point, mu, toSheet);
                        if (q == null) continue;
                        if (prev != null) segs.Add(new[] { prev[0], prev[1], q[0], q[1] });
                        prev = q;
                    }
                }
                catch { }
            }
            return segs;
        }

        /// <summary>
        /// A new balloon on <paramref name="point"/> (x, y on the sheet) for the balloon
        /// <paramref name="b"/>, put where <paramref name="b"/> was, then <paramref name="b"/> taken
        /// away; the new arrow tip, or null when nothing changed (the new balloon would carry
        /// another number, or could not be made: then it is taken away and the old one stays).
        /// </summary>
        private static double[] ReinsertBalloon(IModelDoc2 model, BalloonRead b, double[] point)
        {
            INote born = null;
            try
            {
                model.ClearSelection2(true);
                if (!model.Extension.SelectByID2("", "EDGE", point[0], point[1], 0, false, 0, null, 0)) { model.ClearSelection2(true); return null; }
                var options = model.Extension.CreateBalloonOptions() as BalloonOptions;
                options.Style = (int)swBalloonStyle_e.swBS_Circular;
                options.Size = (int)swBalloonFit_e.swBF_2Chars;
                options.UpperTextContent = (int)swBalloonTextContent_e.swBalloonTextItemNumber;
                options.ItemOrder = (int)swBalloonItemNumbersOrder_e.swBalloonItemNumbers_DoNotChangeItemNumbers;
                born = model.Extension.InsertBOMBalloon2(options) as INote;
            }
            catch { born = null; }
            model.ClearSelection2(true);
            if (born == null) return null;
            var annotation = born.GetAnnotation() as IAnnotation;
            string number = "";
            try { number = (born.GetText() ?? "").Trim(); } catch { }
            if (annotation == null || number != b.Number)
            {
                DeleteAnnotationSelected(model, annotation);
                return null;
            }
            if (!DeleteAnnotationSelected(model, b.Annotation))
            {
                DeleteAnnotationSelected(model, annotation);
                return null;
            }
            try { if (b.Position != null && b.Position.Length >= 2) annotation.SetPosition2(b.Position[0], b.Position[1], 0); } catch { }
            try
            {
                var pts = annotation.GetLeaderPointsAtIndex(0) as double[];
                if (pts != null && pts.Length >= 6) return new[] { pts[pts.Length - 3], pts[pts.Length - 2] };
            }
            catch { }
            return new[] { point[0], point[1] };
        }

        /// <summary>Selects an annotation and takes it away (DeleteSelection2, no window); true when it went.</summary>
        private static bool DeleteAnnotationSelected(IModelDoc2 model, IAnnotation annotation)
        {
            if (annotation == null) return false;
            try
            {
                model.ClearSelection2(true);
                bool selected = annotation.Select3(false, null);
                bool gone = selected && model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
                return gone;
            }
            catch { return false; }
        }

        private static string SafeName(IComponent2 c)
        {
            try { return c?.Name2; } catch { return null; }
        }

        private static string NumbersOf(List<BalloonRead> balloons, List<int> which)
        {
            var l = new List<string>();
            foreach (int i in which) l.Add(balloons[i].Number);
            return string.Join(", ", l);
        }

        /// <summary>
        /// The indices of the tips that lie closer than <paramref name="limit"/> to another tip
        /// (null tips are skipped). Pure, so the contract suite reads it.
        /// </summary>
        internal static List<int> CrowdedIndices(List<double[]> tips, double limit)
        {
            var l = new List<int>();
            for (int i = 0; i < tips.Count; i++)
            {
                if (tips[i] == null) continue;
                for (int k = 0; k < tips.Count; k++)
                    if (k != i && tips[k] != null && Distance2(tips[i], tips[k]) < limit) { l.Add(i); break; }
            }
            return l;
        }

        /// <summary>
        /// The straight pieces of the lines of IView.GetPolylines7 on the sheet (x0, y0, x1, y1,
        /// metres): every record is [type, n, n doubles, 6 doubles of line style and layer, number
        /// of points, points x, y, z]; a point of the view space goes on the sheet as the centre of
        /// GetXform plus its scale times the point (measured: the arrow tips of round 14 at 0 mm).
        /// Null when the array does not hold <paramref name="records"/> records. Pure, so the
        /// contract suite reads it.
        /// </summary>
        internal static List<double[]> PolylinePieces(double[] data, int records, double[] xform)
        {
            if (data == null || xform == null || xform.Length < 3) return null;
            var pieces = new List<double[]>();
            int i = 0;
            for (int r = 0; r < records; r++)
            {
                if (i + 1 >= data.Length) return null;
                int geometry = (int)data[i + 1];
                int j = i + 2 + geometry + 6;
                if (j >= data.Length) return null;
                int points = (int)data[j];
                j++;
                if (points < 0 || j + 3 * points > data.Length) return null;
                for (int q = 1; q < points; q++)
                    pieces.Add(new[]
                    {
                        xform[0] + xform[2] * data[j + 3 * (q - 1)], xform[1] + xform[2] * data[j + 3 * (q - 1) + 1],
                        xform[0] + xform[2] * data[j + 3 * q], xform[1] + xform[2] * data[j + 3 * q + 1],
                    });
                i = j + 3 * points;
            }
            return i == data.Length ? pieces : null;
        }

        /// <summary>
        /// Whose each piece is: the first component whose segments pass within
        /// <paramref name="tolerance"/> of both its ends, null when none. Pure, so the contract
        /// suite reads it.
        /// </summary>
        internal static string[] PieceOwners(List<double[]> pieces, Dictionary<string, List<double[]>> segments, double tolerance)
        {
            var owners = new string[pieces.Count];
            for (int q = 0; q < pieces.Count; q++)
                foreach (KeyValuePair<string, List<double[]>> kv in segments)
                {
                    var p = pieces[q];
                    double d0 = double.MaxValue, d1 = double.MaxValue;
                    foreach (var s in kv.Value) { d0 = Math.Min(d0, PointToSegment(s, p[0], p[1])); d1 = Math.Min(d1, PointToSegment(s, p[2], p[3])); }
                    if (d0 < tolerance && d1 < tolerance) { owners[q] = kv.Key; break; }
                }
            return owners;
        }

        /// <summary>
        /// The point of the pieces owned by <paramref name="mine"/>, sampled every
        /// <paramref name="step"/>, farthest from every tip in <paramref name="others"/>, among the
        /// points farther than <paramref name="clearance"/> from any piece that is not
        /// <paramref name="mine"/> (a line shared with another part would point at both): {x, y,
        /// distance to the nearest other tip}, null when there is none. Pure, so the contract suite
        /// reads it.
        /// </summary>
        internal static double[] FarthestOwnPoint(List<double[]> pieces, string[] owners, string mine, List<double[]> others,
                                                  double step, double clearance)
        {
            double[] best = null;
            for (int q = 0; q < pieces.Count; q++)
            {
                if (owners[q] != mine) continue;
                var s = pieces[q];
                double len = Math.Sqrt((s[2] - s[0]) * (s[2] - s[0]) + (s[3] - s[1]) * (s[3] - s[1]));
                int steps = Math.Max(1, (int)(len / step));
                for (int k = 0; k <= steps; k++)
                {
                    double u = (double)k / steps, x = s[0] + u * (s[2] - s[0]), y = s[1] + u * (s[3] - s[1]);
                    bool shared = false;
                    for (int r = 0; r < pieces.Count && !shared; r++)
                        if (owners[r] != mine && PointToSegment(pieces[r], x, y) < clearance) shared = true;
                    if (shared) continue;
                    double far = Nearest(new[] { x, y }, others);
                    if (best == null || far > best[2]) best = new[] { x, y, far };
                }
            }
            return best;
        }

        /// <summary>
        /// A point through the 13 numbers of IMathTransform.ArrayData (rotation 0..8, translation
        /// 9..11, scale 12), x and y only, with the rotation read by rows or by columns. Null when
        /// the numbers are missing. Pure, so the contract suite reads it.
        /// </summary>
        internal static double[] Transformed(double[] a, double[] p, bool rows)
        {
            if (a == null || a.Length < 13 || p == null || p.Length < 3) return null;
            double s = a[12];
            if (rows)
                return new[] { s * (p[0] * a[0] + p[1] * a[3] + p[2] * a[6]) + a[9], s * (p[0] * a[1] + p[1] * a[4] + p[2] * a[7]) + a[10] };
            return new[] { s * (a[0] * p[0] + a[1] * p[1] + a[2] * p[2]) + a[9], s * (a[3] * p[0] + a[4] * p[1] + a[5] * p[2]) + a[10] };
        }

        /// <summary>
        /// Which order of ArrayData gives the point IMathPoint gave (<paramref name="reference"/>):
        /// 1 rows, 2 columns, 3 neither (or nothing to compare). Pure, so the contract suite reads it.
        /// </summary>
        internal static int TransformOrder(double[] a, double[] p, double[] reference, double tolerance)
        {
            if (reference == null || reference.Length < 2) return 3;
            var r = Transformed(a, p, true);
            if (r != null && Math.Abs(r[0] - reference[0]) <= tolerance && Math.Abs(r[1] - reference[1]) <= tolerance) return 1;
            var c = Transformed(a, p, false);
            if (c != null && Math.Abs(c[0] - reference[0]) <= tolerance && Math.Abs(c[1] - reference[1]) <= tolerance) return 2;
            return 3;
        }

        private static double Nearest(double[] p, List<double[]> others)
        {
            double d = double.MaxValue;
            if (p == null) return d;
            foreach (var o in others) if (o != null) d = Math.Min(d, Distance2(p, o));
            return d;
        }

        private static double Distance2(double[] a, double[] b)
        {
            return Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]));
        }

        private static double PointToSegment(double[] s, double x, double y)
        {
            double dx = s[2] - s[0], dy = s[3] - s[1], l = dx * dx + dy * dy;
            double u = l <= 0 ? 0 : Math.Max(0, Math.Min(1, ((x - s[0]) * dx + (y - s[1]) * dy) / l));
            double px = s[0] + u * dx - x, py = s[1] + u * dy - y;
            return Math.Sqrt(px * px + py * py);
        }
    }
}
