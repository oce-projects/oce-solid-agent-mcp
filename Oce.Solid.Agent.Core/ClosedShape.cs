using System;
using System.Collections.Generic;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// Where a point is with respect to the border of a shape.
    ///
    /// The border is an answer of its own: a point lying on it is neither
    /// inside nor outside, and forcing it to be one of the two makes the
    /// verdict depend on the side of the polygon it happens to fall on. See
    /// <see cref="ClosedShape.WhereItIs"/>.
    /// </summary>
    public enum WhereItIsFound
    {
        /// <summary>Inside the material, border excluded.</summary>
        Inside,

        /// <summary>Outside the material, border excluded.</summary>
        Outside,

        /// <summary>On the border, within <see cref="ClosedShape.OnBorderMm"/>.</summary>
        OnBorder
    }

    /// <summary>
    /// THE GEOMETRY OF A PLANAR SHAPE, with no SOLIDWORKS in between.
    ///
    /// It serves to answer two questions that SOLIDWORKS does **not** ask:
    ///
    ///   * does this contour cross itself?
    ///   * does this void lie inside the outer border?
    ///
    /// Neither of the two gives an error. A contour that crosses itself is
    /// built just the same: SOLIDWORKS extrudes the regions it manages to
    /// close and produces a plausible and wrong part. A void that
    /// overruns **cuts** the part instead of drilling it, and the result is a
    /// perfectly valid solid that is not what was asked for.
    ///
    /// It lives in a file of its own, and not inside the executor, for a practical reason:
    /// so it can be tested without opening SOLIDWORKS. A geometric check that
    /// can be tested only with the CAD open gets tested little.
    ///
    /// CONVENTION ON THE POINTS. A ring is passed **without repeating the first
    /// point at the end**: the last side is always from `points[n-1]` to `points[0]`.
    /// Repeating it would add a side of length zero, which confuses
    /// both the count of the area and that of the crossings. Hence
    /// <see cref="Ring"/>, which removes it if it is there.
    /// </summary>
    public static class ClosedShape
    {
        /// <summary>Two points coincide, within the tolerance given in mm.</summary>
        public static bool Neighbors(double[] a, double[] b, double tolerance = 1e-6)
        {
            return Math.Abs(a[0] - b[0]) < tolerance
                && Math.Abs(a[1] - b[1]) < tolerance;
        }

        /// <summary>
        /// The clean ring: with the repeated points in a row removed and the first
        /// one repeated at the end removed.
        ///
        /// The duplicates are not a textbook case. Composite contours are
        /// written stroke by stroke and the end of a stroke **is** the start
        /// of the next one, so every joint carries a double point; and whoever
        /// writes a polyline by hand often repeats the first vertex at the end
        /// to «close it».
        /// </summary>
        public static List<double[]> Ring(IEnumerable<double[]> points, double tolerance = 1e-6)
        {
            var outside = new List<double[]>();
            foreach (var p in points)
            {
                if (outside.Count > 0 && Neighbors(outside[outside.Count - 1], p, tolerance)) continue;
                outside.Add(p);
            }
            while (outside.Count > 1 && Neighbors(outside[0], outside[outside.Count - 1], tolerance))
                outside.RemoveAt(outside.Count - 1);
            return outside;
        }

        /// <summary>
        /// The signed area according to Gauss, in absolute value. In square mm.
        /// </summary>
        public static double Area(IList<double[]> ring)
        {
            double sum = 0;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % n];
                sum += a[0] * b[1] - b[0] * a[1];
            }
            return Math.Abs(sum) / 2.0;
        }

        /// <summary>
        /// Where a point is with respect to a polygon: inside, outside, or on its
        /// border.
        ///
        /// WHY THREE ANSWERS AND NOT TWO.
        ///
        /// The parity of the crossings cannot answer on the border, and the
        /// answer it gives DEPENDS ON THE SIDE: with the comparison `x &lt; cut` a
        /// vertex on the right border comes out outside, and the same vertex on the
        /// left border comes out inside.
        ///
        /// It would be a detail if only one point were left. But the check of the
        /// contours classifies ALL the vertices and decides from those. Measured on
        /// 14/9/2026 on a 100x100 plate: a pocket flush with the RIGHT border —
        /// (30,-10)(50,-10)(50,10)(30,10) — has two vertices «outside» and two
        /// «inside», and was refused as «straddles the outer border». The
        /// same pocket mirrored to the LEFT has all four vertices
        /// «inside», and passed.
        ///
        /// Same geometry, opposite verdict depending on the side. One of the two was
        /// wrong — and SOLIDWORKS said which.
        ///
        /// THE FIRST REPAIR WENT THE WRONG WAY, and it is worth
        /// writing down. The reasoning was «a pocket flush with the border is
        /// an open slot, it is done every day, so both must be accepted».
        /// It is true of the PART and false of the SKETCH: two contours that
        /// share a stretch of border do not delimit a region, and
        /// the open slot is drawn as ONE contour only with eight points, not
        /// as two that touch.
        ///
        /// Measured, three geometries, one single result — «The extrusion was
        /// not created»:
        ///
        ///   * pocket flush with the right border;
        ///   * the same mirrored to the left;
        ///   * a rhombus that touches the border in ONE SINGLE VERTEX.
        ///
        /// So the right verdict for both sides is to REFUSE, and
        /// what was wrong was the acceptance on the left. The value of
        /// «on the border» is not to let more through: it is to give **the same
        /// answer** on both sides, and to give a better reason than the one SOLIDWORKS
        /// gives.
        ///
        /// > The direction of a repair is not derived from reasoning
        /// > about the workshop: the part is asked. «It is legitimate work» said
        /// > how it MUST go, not how it GOES.
        /// </summary>
        public static WhereItIsFound WhereItIs(double[] point, IList<double[]> polygon)
        {
            if (point == null || polygon == null || polygon.Count < 3)
                return WhereItIsFound.Outside;
            if (DistanceFromBorder(point, polygon) <= OnBorderMm)
                return WhereItIsFound.OnBorder;
            return Inside(point, polygon) ? WhereItIsFound.Inside : WhereItIsFound.Outside;
        }

        /// <summary>
        /// How far a point may be from the border and still count as «on the
        /// border», in millimetres.
        ///
        /// One micrometre. The gap was measured on both sides: the noise
        /// of the calculation on coordinates in millimetres is of the order of 1e-13,
        /// ten million times below; the smallest dimension that makes sense
        /// on a part is a hundredth of a millimetre, ten thousand times above.
        /// Neither of the two can be confused with the other.
        /// </summary>
        public const double OnBorderMm = 1e-6;

        /// <summary>
        /// The minimum distance between a point and the border of the polygon, in mm.
        /// </summary>
        public static double DistanceFromBorder(double[] point, IList<double[]> polygon)
        {
            double minValue = double.MaxValue;
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                double d = DistanceFromSegment(point, polygon[i], polygon[(i + 1) % n]);
                if (d < minValue) minValue = d;
            }
            return minValue;
        }

        /// <summary>Distance between a point and a segment, ends included.</summary>
        private static double DistanceFromSegment(double[] p, double[] a, double[] b)
        {
            double vx = b[0] - a[0], vy = b[1] - a[1];
            double wx = p[0] - a[0], wy = p[1] - a[1];
            double length2 = vx * vx + vy * vy;
            // Degenerate segment: it is a point, and the distance is from that point.
            if (length2 < 1e-24) return Math.Sqrt(wx * wx + wy * wy);
            double t = (wx * vx + wy * vy) / length2;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            double dx = wx - t * vx, dy = wy - t * vy;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Is the point inside the polygon? Parity of the crossings.
        ///
        /// IT DOES NOT ANSWER ON THE BORDER, and the answer it gives there depends on the side:
        /// use <see cref="WhereItIs"/> whenever a point may be on it.
        /// This one stays for the cases in which the border is excluded by construction.
        /// </summary>
        public static bool Inside(double[] point, IList<double[]> polygon)
        {
            double x = point[0], y = point[1];
            bool inside = false;
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % n];
                if ((a[1] > y) != (b[1] > y))
                {
                    double cut = a[0] + (y - a[1]) * (b[0] - a[0]) / (b[1] - a[1]);
                    if (x < cut) inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// Two segments REALLY cross, that is, they go through each other. Touching
        /// at an end does not count: it is what two consecutive sides do, and
        /// it is normal.
        /// </summary>
        public static bool Intersect(double[] p1, double[] p2, double[] p3, double[] p4)
        {
            double d1 = Side(p3, p4, p1), d2 = Side(p3, p4, p2);
            double d3 = Side(p1, p2, p3), d4 = Side(p1, p2, p4);
            return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0));
        }

        private static double Side(double[] a, double[] b, double[] c)
        {
            return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
        }

        /// <summary>
        /// Does the ring cross itself? If so, the index of the first side
        /// involved is returned; -1 if it is clean.
        ///
        /// The index is returned and not a boolean because the message must be able to
        /// say WHERE: «it crosses itself» on a shape of two hundred vertices helps
        /// nobody to repair it.
        ///
        /// COST. It is a comparison of all the sides with all: n²/2. On a wheel
        /// of 127 teeth that is about 3,300 points and 5.4 million comparisons, which
        /// in C# is under a second. Above <see cref="TooManyToCheck"/>
        /// points it gives up and says so, instead of making you wait in silence.
        /// </summary>
        public static int WhereItCrosses(IList<double[]> ring)
        {
            int n = ring.Count;
            if (n < 4 || n > TooManyToCheck) return -1;
            for (int i = 0; i < n; i++)
            {
                var a1 = ring[i];
                var a2 = ring[(i + 1) % n];
                for (int j = i + 2; j < n; j++)
                {
                    // The first and the last side share a vertex.
                    if (i == 0 && j == n - 1) continue;
                    if (Intersect(a1, a2, ring[j], ring[(j + 1) % n])) return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Beyond these points the check of the crossings is skipped.
        ///
        /// 6,000 points are 18 million comparisons, still under a second.
        /// The ceiling is higher than any shape that the bridge accepts
        /// (4,000 points for the contours generated by the recipes), so in
        /// practice it is never skipped: it is a net, not a shortcut.
        /// </summary>
        public const int TooManyToCheck = 6000;
    }
}
