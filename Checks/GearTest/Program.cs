using System;
using System.Collections.Generic;
using System.Globalization;
using Oce.Solid.Agent.Core;

namespace Checks.GearTest
{
    /// <summary>
    /// THE GEAR, CHECKED WITHOUT SOLIDWORKS.
    ///
    /// It is mathematics, and mathematics verifies itself: the points that come out
    /// of the generator are compared with the ISO formulas, and the error is seen
    /// before it reaches the CAD, not after.
    ///
    /// WHY IT IS NEEDED, GIVEN THAT THE MEASUREMENT ON THE PART ALREADY EXISTS.
    /// Because the measurement on the part takes the OUTER DIAMETER, and the outer
    /// diameter comes from ra = r + m: a formula that can be hit while
    /// having got everything else wrong. The involute lives in the FLANKS, and the flanks are not
    /// measured with a bounding box.
    ///
    /// Worse: it was measured that the bounding box coincides with the outer diameter
    /// ONLY when a tooth happens to be on the axis. With z=20 (teeth every 18 degrees)
    /// it happens; with z=17 (every 21.18 degrees) it does not, and the box reads 37.79 instead
    /// of 38. It is not an error of the gear, it is the limit of the measurement.
    ///
    /// WHAT THIS CHECKS, THAT THAT CANNOT.
    /// The TOOTH THICKNESS ON THE PITCH CIRCLE, which on a gear without
    /// correction is exactly pi*m/2, and the SPACE between one tooth and the next,
    /// which is just as much. They are two different measurements:
    ///
    ///   * the thickness is wrong if psi or inv(alfa) is wrong;
    ///   * the space is wrong if the angular pitch is wrong.
    ///
    /// A single right number is not enough to say the formula is right; two
    /// numbers that come from different pieces of the formula are.
    ///
    ///     Checks\GearTest\bin\GearTest.exe
    ///
    /// Exits 0 if everything adds up, 1 if not. It does not open SOLIDWORKS and does not need it.
    /// </summary>
    internal static class Program
    {
        private static int trouble;

        /// <summary>
        /// Dense on purpose: the points serve to measure, not to draw. With
        /// interpolation between two points that straddle the pitch circle the residual
        /// error drops below a micron, so a tight tolerance accuses
        /// the formula and not the discretisation.
        /// </summary>
        private const int PointsPerSide = 200;

        private static int Main()
        {
            Console.WriteLine("The gear, against the ISO formulas");
            Console.WriteLine();

            // Different gears: the classic case, the coarse module, the undercut
            // limit, the fine module. A wrong formula may come out right by
            // chance on one case only.
            Gear(20, 2.0, 20.0);
            Gear(24, 3.0, 20.0);
            Gear(17, 2.0, 20.0);
            Gear(40, 1.5, 20.0);
            Gear(127, 1.0, 20.0);

            // DIFFERENT PRESSURE ANGLES. It is needed because inv(20 degrees) is
            // 0.0149 and is small: a formula that forgets it altogether
            // gets the thickness wrong by 0.6 mm on m=2 — it shows. But one that
            // uses it with the wrong sign, at 20 degrees is off by little and at 25 by double.
            Gear(20, 2.0, 25.0);
            // At 14.5 degrees the undercut limit rises to 31 teeth: asking for 20
            // was MY mistake, not the generator's, and the refusal was right.
            Gear(32, 2.0, 14.5);

            // THE TWO CASES AROUND THE JUMP. The root exceeds the base at z=42 with
            // 20 degrees: 41 goes through the old part of the code, 42 through the new.
            Gear(41, 2.0, 20.0);
            Gear(42, 2.0, 20.0);

            // WHAT MUST BE REFUSED, and the reason why.
            //
            // The root closes when 2.5*tan(alfa) exceeds pi/2, that is beyond
            // 32.15 degrees of pressure angle: the tooth becomes so wide
            // at the base that no space is left between one tooth and the next. At 35 degrees
            // with 60 teeth 0.14 mm are missing.
            Refused(60, 1.0, 35.0, "they would touch at the base");
            // Below the undercut limit.
            Refused(16, 2.0, 20.0, "root notch");
            // THE POINTED TOOTH. At 20 degrees it is never reached: the undercut
            // limit (17 teeth) triggers first, and asking for z=5 m=4 one gets
            // refused by THAT — the case seemed to test the tip and did not
            // test it. At 45 degrees the undercut limit drops to 4 teeth and the
            // road opens.
            Refused(5, 4.0, 45.0, "it would close to a point");

            Console.WriteLine();
            if (trouble == 0) { Console.WriteLine("Everything adds up."); return 0; }
            Console.WriteLine(trouble + " scostamenti.");
            return 1;
        }

        private static void Gear(int z, double m, double alphaDeg)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "z={0}  m={1}  alpha={2} degrees", z, m, alphaDeg));

            string reason;
            var contours = SpurGear.Contours(
                z, m, alphaDeg, 0, false, PointsPerSide, out reason);
            if (contours == null) { Male("rifiutata: " + reason); return; }

            var points = FirstContourPoints(contours);
            if (points.Count < z * 4) { Male("only " + points.Count + " points: the outline is not what I think"); return; }

            double r = m * z / 2.0;
            double planStep = Math.PI * m;

            // 1. THE EXTREME RADII. They do not test the involute, but if these are wrong
            //    it is not worth looking at the rest.
            double rMin = double.MaxValue, rMax = 0;
            foreach (var p in points)
            {
                double radius = Math.Sqrt(p[0] * p[0] + p[1] * p[1]);
                if (radius < rMin) rMin = radius;
                if (radius > rMax) rMax = radius;
            }
            Compare("tip radius", rMax, r + m, 0.001);
            Compare("root radius", rMin, r - 1.25 * m, 0.001);

            // 1b. THE CONTOUR NEVER GOES BACK.
            //
            // This is the check that would have caught by itself the defect of
            // z=127: the root hooked to the wrong angle made a piece of the
            // turn be travelled backwards, the sketch cut itself and
            // SOLIDWORKS only said «L'estrusione non e' stata creata».
            //
            // A closed contour that turns in one direction has the unrolled angle
            // always increasing. A negative step is a self-intersection, and it is
            // seen without opening anything.
            // The threshold is not zero, and it is computed instead of chosen. The points
            // come out rounded to the ten-thousandth of a millimetre; on the smallest
            // radius that rounding is worth 1e-4/rf radians of tangential
            // noise, and three times as much covers the worst case. On z=20
            // it makes 1,7e-5 against the 6e-6 measured; on z=127 it makes 4,8e-6 against the
            // 1,7e-3 of the defect. Between noise and defect there are two orders of
            // magnitude, so the threshold is not a compromise.
            double noise = 3e-4 / (r - 1.25 * m);
            double back = BiggerStepBack(points);
            if (back > noise)
                Male(string.Format(CultureInfo.InvariantCulture,
                    "the outline goes back by {0:0.000000} rad: it cuts itself",
                    back));
            else
                Console.WriteLine("  ok  the outline always turns the same way");

            // 2. THE CROSSINGS OF THE PITCH CIRCLE. The contour turns, and every
            //    tooth crosses it twice: once going up, once going down.
            //    If they are not 2z, the tooth count is not the declared one.
            var upCrossings = new List<double>();
            var downCrossings = new List<double>();
            Crossings(points, r, upCrossings, downCrossings);
            if (upCrossings.Count != z || downCrossings.Count != z)
            {
                Male(string.Format(CultureInfo.InvariantCulture,
                    "crossings of the pitch circle: {0} up and {1} down, " +
                    "expected {2} and {2}", upCrossings.Count, downCrossings.Count, z));
                return;
            }

            // 3. THE THICKNESS AND THE SPACE, tooth by tooth. We look at the
            //    narrowest and the widest: a gear with a crooked tooth passes,
            //    if only the mean is looked at.
            double sMin = double.MaxValue, sMax = 0, vMin = double.MaxValue, vMax = 0;
            for (int k = 0; k < z; k++)
            {
                double thickness = (downCrossings[k] - upCrossings[k]) * r;
                double cavity = (upCrossings[(k + 1) % z] + (k + 1 == z ? 2 * Math.PI : 0)
                               - downCrossings[k]) * r;
                if (thickness < sMin) sMin = thickness;
                if (thickness > sMax) sMax = thickness;
                if (cavity < vMin) vMin = cavity;
                if (cavity > vMax) vMax = cavity;
            }
            Compare("minimum thickness on the pitch circle", sMin, planStep / 2, 0.002);
            Compare("maximum thickness on the pitch circle", sMax, planStep / 2, 0.002);
            Compare("minimum space on the pitch circle", vMin, planStep / 2, 0.002);
            Compare("maximum space on the pitch circle", vMax, planStep / 2, 0.002);
            Console.WriteLine();
        }

        /// <summary>
        /// The largest step backwards, in radians, unrolling the
        /// angles along the contour. Zero if the turn is always in the same
        /// direction.
        ///
        /// The root and the crest are chords: between two points the angle grows
        /// very little, but it grows. The rounding to 4 decimals can give
        /// a few nanoradians of noise, and that is why the threshold is not
        /// exactly zero.
        /// </summary>
        private static double BiggerStepBack(List<double[]> points)
        {
            double worse = 0, previous = 0;
            bool first = true;
            foreach (var p in points)
            {
                double angle = Math.Atan2(p[1], p[0]);
                if (first) { first = false; previous = angle; continue; }
                while (angle - previous > Math.PI) angle -= 2 * Math.PI;
                while (previous - angle > Math.PI) angle += 2 * Math.PI;
                double planStep = angle - previous;
                if (-planStep > worse) worse = -planStep;
                previous = angle;
            }
            return worse;
        }

        /// <summary>
        /// A gear that MUST be refused, and for the right reason.
        ///
        /// The reason counts as much as the refusal: a gear refused by the wrong
        /// check is a check that has never been tested. With
        /// <paramref name="fragment"/> empty the refusal is enough.
        /// </summary>
        private static void Refused(int z, double m, double alphaDeg, string fragment)
        {
            string reason;
            var contours = SpurGear.Contours(
                z, m, alphaDeg, 0, false, PointsPerSide, out reason);
            string head = string.Format(CultureInfo.InvariantCulture,
                "z={0}  m={1}  alpha={2} degrees  (must refuse)", z, m, alphaDeg);
            Console.WriteLine(head);
            if (contours != null) { Male("accepted instead"); Console.WriteLine(); return; }
            if (fragment.Length > 0 && reason.IndexOf(fragment, StringComparison.Ordinal) < 0)
            {
                Male("refused for another reason: " + reason);
                Console.WriteLine();
                return;
            }
            Console.WriteLine("  ok  " + reason);
            Console.WriteLine();
        }

        /// <summary>
        /// Where the contour cuts the pitch circle, and in which direction.
        ///
        /// The contour is walked IN THE ORDER IN WHICH IT IS WRITTEN, not by angle:
        /// the order is that of the turn, and two consecutive points one of which is
        /// inside and one outside the pitch circle are a crossing. Between the two
        /// we interpolate, because the exact point is almost never one of those
        /// computed.
        ///
        /// The angles are unrolled as we go (no jump from +pi to -pi), so
        /// the space between the last tooth and the first is measured like all the others.
        /// </summary>
        private static void Crossings(
            List<double[]> points, double r, List<double> upCrossings, List<double> downCrossings)
        {
            double previous = 0;
            bool first = true;
            var angles = new List<double>();
            var radii = new List<double>();
            foreach (var p in points)
            {
                double angle = Math.Atan2(p[1], p[0]);
                if (first) { first = false; }
                else
                {
                    while (angle - previous > Math.PI) angle -= 2 * Math.PI;
                    while (previous - angle > Math.PI) angle += 2 * Math.PI;
                }
                previous = angle;
                angles.Add(angle);
                radii.Add(Math.Sqrt(p[0] * p[0] + p[1] * p[1]));
            }

            for (int i = 1; i < radii.Count; i++)
            {
                double a = radii[i - 1] - r, b = radii[i] - r;
                if (a == 0 || b == 0 || (a < 0) == (b < 0)) continue;
                double f = a / (a - b);                       // between 0 and 1
                double angle = angles[i - 1] + f * (angles[i] - angles[i - 1]);
                if (a < 0) upCrossings.Add(angle); else downCrossings.Add(angle);
            }
        }

        private static List<double[]> FirstContourPoints(List<object> contours)
        {
            var outside = new List<double[]>();
            var d = contours[0] as Dictionary<string, object>;
            if (d == null) return outside;
            object raw;
            if (!d.TryGetValue("strokes", out raw)) return outside;
            foreach (var t in (object[])raw)
            {
                var stroke = t as Dictionary<string, object>;
                if (stroke == null) continue;
                object pp;
                if (!stroke.TryGetValue("points", out pp)) continue;
                foreach (var p in (object[])pp)
                {
                    var c = p as object[];
                    if (c == null || c.Length < 2) continue;
                    double x = Convert.ToDouble(c[0], CultureInfo.InvariantCulture);
                    double y = Convert.ToDouble(c[1], CultureInfo.InvariantCulture);
                    // The segments touch at the ends: the repeated point adds
                    // nothing and would falsify the count if the points were counted
                    // instead of the crossings.
                    if (outside.Count > 0)
                    {
                        var u = outside[outside.Count - 1];
                        if (u[0] == x && u[1] == y) continue;
                    }
                    outside.Add(new[] { x, y });
                }
            }
            return outside;
        }

        private static void Compare(string what, double had, double expected, double within)
        {
            double deviation = Math.Abs(had - expected);
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0,-34} {1,10:0.#####}  expected {2,10:0.#####}  deviation {3:0.000000}",
                what, had, expected, deviation);
            if (double.IsNaN(had) || deviation > within) Male(line);
            else Console.WriteLine("  ok  " + line);
        }

        private static void Male(string line)
        {
            trouble++;
            Console.WriteLine("  XX  " + line);
        }
    }
}
