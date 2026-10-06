using System;
using System.Collections.Generic;
using System.Globalization;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE PROFILE OF A SPUR GEAR WITH STRAIGHT TEETH.
    ///
    /// It is mathematics, and mathematics is verified without SOLIDWORKS: diameters,
    /// pitch and tooth thickness on the pitch circle are compared with the ISO
    /// formulas, and an error is seen before it reaches the workshop.
    ///
    /// THE GEOMETRY, IN PLAIN
    ///   pitch      d  = m·z            the diameter that «rolls»
    ///   base       db = d·cos(α)       from which the involute starts
    ///   outside    d_a = d + 2m
    ///   root       d_f = d − 2.5m
    ///   pitch      p  = π·m
    ///
    /// The flank of the tooth is the **involute of the base circle**. A point
    /// of the involute, with parameter t:
    ///
    ///     radius          = rb·√(1 + t²)
    ///     angular offset  = t − atan(t)      (it is inv(α) at that radius)
    ///
    /// and the angular half of the tooth on the base circle is
    /// ψ = π/(2z) + inv(α). From there the two flanks are symmetric with respect to
    /// the axis of the tooth: one at ψ − offset, the other at −ψ + offset.
    ///
    /// THE CONTOUR IS TRAVERSED COUNTERCLOCKWISE, tooth by tooth: root,
    /// rising flank, tip, falling flank, root, and the bottom up to the
    /// next tooth. This way the angle always grows, and a contour that never goes
    /// back cannot cross itself.
    ///
    /// THE SIMPLIFICATIONS, DECLARED
    /// The root fillet is NOT the true trochoid: below the base circle
    /// the flank goes down radially to the root. For a part to be milled or
    /// laser cut it is fine; **for a power gear verified for
    /// fatigue it is not**, and it must be said instead of discovered. Straight teeth only:
    /// no helix, no profile correction.
    ///
    /// WHY THIS FILE IS MORE DELICATE THAN THE OTHERS. A wrong transliteration
    /// of `Inv()` or of the sign of an offset gives no error:
    /// it gives **a gear that meshes badly**. It is the defect that gives no error in
    /// its pure form, and no internal check catches it — only the measure on the
    /// part, plus the comparison of the formulas with the ISO values.
    /// </summary>
    public static class SpurGear
    {
        /// <summary>Involute function: inv(α) = tan(α) − α, with α in radians.</summary>
        public static double Inv(double alpha)
        {
            return Math.Tan(alpha) - alpha;
        }

        /// <summary>
        /// Keyway slots in the HUB, normal series (ISO/R 773, DIN 6885-1):
        /// shaft up to → width b, depth t2 measured from the bore.
        ///
        /// WHY A TABLE AND NOT AN ARCHIVE OF STANDARDS TO CONSULT. For a
        /// normative text it would make sense; for a DIMENSION it would not. An archive
        /// returns rows of text that somebody has to interpret, and that is
        /// where errors are born. A keyway is a table — shaft 20, b=6,
        /// t2=2.8 — and a table in the code is exact, repeatable and
        /// verifiable.
        /// </summary>
        private static readonly double[][] Keyways =
        {
            new[] { 8.0, 2.0, 1.0 },     new[] { 10.0, 3.0, 1.4 },
            new[] { 12.0, 4.0, 1.8 },    new[] { 17.0, 5.0, 2.3 },
            new[] { 22.0, 6.0, 2.8 },    new[] { 30.0, 8.0, 3.3 },
            new[] { 38.0, 10.0, 3.3 },   new[] { 44.0, 12.0, 3.3 },
            new[] { 50.0, 14.0, 3.8 },   new[] { 58.0, 16.0, 4.3 },
            new[] { 65.0, 18.0, 4.4 },   new[] { 75.0, 20.0, 4.9 },
            new[] { 85.0, 22.0, 5.4 },   new[] { 95.0, 25.0, 5.4 },
            new[] { 110.0, 28.0, 6.4 },  new[] { 130.0, 32.0, 7.4 },
        };

        /// <summary>Width and depth of the keyway for that shaft diameter.</summary>
        public static bool KeywayForShaft(
            double diameterMm, out double b, out double t2, out string reason)
        {
            foreach (var line in Keyways)
            {
                if (diameterMm > line[0]) continue;
                b = line[1]; t2 = line[2]; reason = null;
                return true;
            }
            b = t2 = 0;
            reason = string.Format(CultureInfo.InvariantCulture,
                "I do not have the standard keyway for a shaft of {0:0.###} mm: the " +
                "table I know goes up to {1:0.###} mm. Tell me the width and the depth yourself.",
                diameterMm, Keyways[Keyways.Length - 1][0]);
            return false;
        }

        /// <summary>
        /// The contours of the gear, or null with the reason.
        /// </summary>
        public static List<object> Contours(
            int teeth, double module, double pressureAngleDeg,
            double holeMm, bool keyway, int pointsPerSide,
            out string reason)
        {
            reason = null;
            if (pointsPerSide < 2) pointsPerSide = 8;

            double alpha = pressureAngleDeg * Math.PI / 180.0;
            double r = module * teeth / 2.0;
            double rb = r * Math.Cos(alpha);
            double ra = r + module;
            double rf = r - 1.25 * module;

            if (rf <= 0)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "With z={0} and m={1:0.###} the root of the tooth would fall in the center " +
                    "of the wheel: the two numbers do not go together.", teeth, module);
                return null;
            }
            if (ra <= rb)
            {
                reason = "With these data the tooth does not leave the base circle: " +
                         "there is no profile to draw.";
                return null;
            }

            // BELOW THE UNDERCUT LIMIT the involute would enter the root, and
            // this generator does not do the profile correction that would be needed.
            // It is a simplification of OURS, not a law of mechanics: that is why
            // the message says which of the two things is happening,
            // instead of making people believe that that gear cannot be made.
            //
            // 2/sin²(α) is 17.097 for the standard 20 degrees, and practice
            // rounds DOWN: the gear with 17 teeth is made every day.
            // With rounding up we would have refused it, which is the
            // worst way of being wrong — saying no to a normal part.
            int minValue = (int)Math.Floor(2.0 / (Math.Sin(alpha) * Math.Sin(alpha)));
            if (teeth < minValue)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "With a pressure angle of {0:0.###} degrees, under {1} " +
                    "teeth the profile shift is needed to avoid undercutting the " +
                    "root, and this generator cannot do it. Ask for more teeth, " +
                    "or a bigger pressure angle.", pressureAngleDeg, minValue);
                return null;
            }

            double psi = Math.PI / (2 * teeth) + Inv(alpha);
            double tMax = Math.Sqrt((ra / rb) * (ra / rb) - 1.0);
            double deviationMax = tMax - Math.Atan(tMax);

            // WHERE THE FLANK STARTS: at the base circle, or at the bottom of the
            // tooth if the root lies higher.
            //
            // It is not a rare case. rb = r*cos(alpha) and rf = r*(1 - 2.5/z): the
            // root exceeds the base when cos(alpha) < 1 - 2.5/z, that is from 42
            // teeth up with the standard angle of 20 degrees. A gear with 127
            // teeth is a normal spare part, not a borderline case.
            //
            // Starting from rb anyway the flank went DOWN below the root — on
            // z=127 m=1 by 2.58 mm — and the contour cut itself. It
            // gave no error: it gave a wrong gear.
            //
            // The fillet stretch that used to be radial stays in its place and
            // becomes a bit of root: its two ends both lie
            // on rf, so the contour closes as before.
            double tMin = rf > rb
                ? Math.Sqrt((rf / rb) * (rf / rb) - 1.0)
                : 0.0;

            // THE HALF-ANGLE OF THE TOOTH AT THE ROOT, which is not psi.
            //
            // psi is the half-angle on the BASE CIRCLE: that is where the involute
            // begins, and from there up the tooth narrows by (t - atan t).
            // When the flank starts higher up, at the root the tooth is already
            // narrowed by deviationMin, and the root must be hooked on THERE.
            //
            // Using psi at the root too, with z=127 the root started at 0.02727
            // rad and ended at 0.02221: it went round the circle BACKWARDS. The
            // sketch cut itself and SOLIDWORKS refused the extrusion
            // with «The extrusion was not created» — a message that says
            // nothing about this.
            double deviationMin = tMin - Math.Atan(tMin);
            double psiRoot = psi - deviationMin;

            // AND WHAT IF THE TEETH TOUCHED AT THE ROOT.
            // The gap at the root measures (angularPitch - 2*psiRoot)*rf. If nothing
            // is left over, the teeth weld to each other and the part is no longer
            // a gear. It is not a theoretical case: it happens when inv(alpha) grows
            // with respect to the pitch, that is with many teeth and large pressure
            // angles.
            double cavityAtBottom = (2 * Math.PI / teeth - 2 * psiRoot) * rf;
            if (cavityAtBottom <= 0.01 * module)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "With z={0}, m={1:0.###} and a pressure angle of {2:0.###} degrees " +
                    "between one tooth and the next there would be {3:0.####} mm of root: " +
                    "the teeth would touch at the base. Fewer teeth or a smaller pressure " +
                    "angle are needed.", teeth, module, pressureAngleDeg, cavityAtBottom);
                return null;
            }
            if (deviationMax >= psi)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "With z={0} and m={1:0.###} the tooth would close to a point before the " +
                    "outside diameter. Fewer teeth or a different module are needed.",
                    teeth, module);
                return null;
            }
            double angularPitch = 2 * Math.PI / teeth;

            Func<double, double, object[]> point = (radius, angle) => new object[]
            {
                Math.Round(radius * Math.Cos(angle), 4),
                Math.Round(radius * Math.Sin(angle), 4),
            };

            // THE TOOTH IS MADE OF STRETCHES, NOT OF SEGMENTS.
            //
            // The flanks are SPLINES through the calculated points: the involute is a
            // curve, and approximating it with segments gave a faceted flank and
            // a sketch of 528 entities for a gear of 20 teeth. With one
            // spline per flank forty curves remain and the geometry is the
            // true one.
            //
            // It is not in contradiction with the rule «never splines with points
            // chosen by a model»: here the points are calculated by an exact formula.
            // The dangerous spline is the one with invented points, not the one
            // with the right points.
            //
            // The rest stays lines, and that is right: the radial stretch below the
            // base circle and the root between two teeth are straight lines, not curves to
            // interpolate.
            var strokes = new List<object>();
            for (int k = 0; k < teeth; k++)
            {
                double center = k * angularPitch;

                var sideUp = new List<object>();
                for (int i = 0; i <= pointsPerSide; i++)
                {
                    double t = tMin + (tMax - tMin) * i / pointsPerSide;
                    double deviation = t - Math.Atan(t);
                    sideUp.Add(point(rb * Math.Sqrt(1 + t * t), center - psi + deviation));
                }

                var sideDown = new List<object>();
                for (int i = pointsPerSide; i >= 0; i--)
                {
                    double t = tMin + (tMax - tMin) * i / pointsPerSide;
                    double deviation = t - Math.Atan(t);
                    sideDown.Add(point(rb * Math.Sqrt(1 + t * t), center + psi - deviation));
                }

                // 1. the foot: the radial line from the root down to the base
                //    circle. It exists ONLY when the base lies below the root: if
                //    it lies above, the flank already starts on the root and this
                //    line would be of zero length. On z=42 it was 0.9 micron long —
                //    not zero, so no error, but a useless entity
                //    in the sketch for each of the 42 teeth.
                if (tMin == 0)
                    strokes.Add(Lines(new List<object> { point(rf, center - psi), sideUp[0] }));
                // 2. the rising flank, involute
                strokes.Add(Spline(sideUp));
                // 3. the tip, on the outside diameter
                double startAngle = center - psi + deviationMax;
                double endAngle = center + psi - deviationMax;
                var ridge = new List<object> { sideUp[sideUp.Count - 1] };
                for (int i = 1; i < 3; i++)
                    ridge.Add(point(ra, startAngle + (endAngle - startAngle) * i / 3.0));
                ridge.Add(sideDown[0]);
                strokes.Add(Lines(ridge));
                // 4. the falling flank, symmetric
                strokes.Add(Spline(sideDown));
                // 5. the foot on the other side and the root up to the next tooth
                double bottomEnd = center + angularPitch - psiRoot;
                double fromBottom = center + psiRoot;
                var bottom = new List<object> { sideDown[sideDown.Count - 1] };
                // The radial descent from the base circle to the root, again
                // only when there is really something to descend: if the flank already ends
                // on the root, this point is the one from before.
                if (tMin == 0) bottom.Add(point(rf, fromBottom));
                for (int i = 1; i < 3; i++)
                    bottom.Add(point(rf, fromBottom + (bottomEnd - fromBottom) * i / 3.0));
                bottom.Add(point(rf, bottomEnd));
                strokes.Add(Lines(bottom));
            }

            var contours = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["kind"] = "composite",
                    ["strokes"] = strokes.ToArray(),
                },
            };

            if (holeMm > 0)
            {
                double holeRadius = holeMm / 2.0;
                if (holeRadius >= rf * 0.95)
                {
                    reason = string.Format(CultureInfo.InvariantCulture,
                        "A hole of {0:0.###} mm on a wheel with the tooth root at {1:0.#} mm " +
                        "of diameter leaves no material under the teeth.", holeMm, 2 * rf);
                    return null;
                }
                if (keyway)
                {
                    var withKeyway = HoleWithKeyway(holeMm, out reason);
                    if (withKeyway == null) return null;
                    contours.Add(withKeyway);
                }
                else
                {
                    // A TRUE CIRCLE, not a polyline with many sides. The
                    // first version approximated it with 48 INSCRIBED segments:
                    // on a 20 mm hole the material invades the circumference
                    // by 0.021 mm and an h7 shaft does not go in.
                    contours.Add(new Dictionary<string, object>
                    {
                        ["kind"] = "circle",
                        ["center"] = new object[] { 0.0, 0.0 },
                        ["diameter_mm"] = holeMm,
                    });
                }
            }

            return contours;
        }

        /// <summary>
        /// The hole with the keyway: a polyline, not a circle.
        ///
        /// A circle with a notch is no longer a circle, so here the
        /// circumference has to be approximated. Two precautions, both for the
        /// same reason — **a hole must NEVER come out narrower than the
        /// nominal**:
        ///
        ///   * 180 sides instead of 48: the deviation drops from 2.1 hundredths to
        ///     1.5 microns;
        ///   * the vertices lie on a CIRCUMSCRIBED circle (r / cos(π/n)), not
        ///     inscribed, so the polyline contains the nominal circle
        ///     instead of being contained by it.
        /// </summary>
        private static Dictionary<string, object> HoleWithKeyway(double holeMm, out string reason)
        {
            const int sides = 180;
            double r = holeMm / 2.0;
            double b, t2;
            if (!KeywayForShaft(holeMm, out b, out t2, out reason)) return null;

            double half = b / 2.0;
            if (half >= r)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "The keyway of {0:0.###} mm does not fit on a hole of {1:0.###} mm.", b, holeMm);
                return null;
            }

            double rPoly = r / Math.Cos(Math.PI / sides);
            double angle = Math.Asin(half / rPoly);   // where the keyway meets the hole
            double bottom = r + t2;                   // the bottom of the keyway, below the surface

            // We start from the left edge of the opening, go all the way
            // round counterclockwise, arrive at the right edge, and go
            // back up into the keyway.
            double start = Math.PI / 2 + angle;
            double benchRun = 2 * Math.PI - 2 * angle;
            var points = new List<object>();
            for (int i = 0; i <= sides; i++)
            {
                double a = start + benchRun * i / sides;
                points.Add(new object[]
                {
                    Math.Round(rPoly * Math.Cos(a), 4),
                    Math.Round(rPoly * Math.Sin(a), 4),
                });
            }
            // the keyway: up the right flank, across the bottom, down the
            // left. The last point returns by itself to the first.
            points.Add(new object[] { Math.Round(half, 4), Math.Round(bottom, 4) });
            points.Add(new object[] { Math.Round(-half, 4), Math.Round(bottom, 4) });

            reason = null;
            return new Dictionary<string, object>
            {
                ["kind"] = "polygonal",
                ["vertices"] = points.ToArray(),
            };
        }

        private static Dictionary<string, object> Lines(List<object> points)
        {
            return new Dictionary<string, object>
            {
                ["stroke"] = "lines",
                ["points"] = points.ToArray(),
            };
        }

        private static Dictionary<string, object> Spline(List<object> points)
        {
            return new Dictionary<string, object>
            {
                ["stroke"] = "spline",
                ["points"] = points.ToArray(),
            };
        }
    }
}
