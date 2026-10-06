using System;
using System.Collections.Generic;
using System.Globalization;
using Oce.Solid.Agent.Core;

namespace Checks.PositionsTest
{
    /// <summary>
    /// THE JUDGEMENT ON THE HOLE POSITIONS, CHECKED WITHOUT SOLIDWORKS.
    ///
    /// WHY IT EXISTS. Up to 0.25.1 the bridge wrote
    /// `positions_checked = true` unconditionally, right after a function
    /// that returned «all fine» even in FOUR roads in which it had looked at
    /// nothing — including the most frequent one, «I found no
    /// cylindrical face of the expected diameter». And the bench decided the pass with
    /// `measures.ContainsKey("positions_checked")`: an always-written key.
    ///
    /// The branch that printed «POSITIONS NOT VERIFIED» could not trigger.
    /// A check never seen to trigger is not a check.
    ///
    /// WHY HERE AND NOT IN THE BENCH. The case that counts — zero cylindrical faces
    /// found — with SOLIDWORKS running there is no known way to force it to happen:
    /// it depends on how the wizard models that particular hole.
    /// With the judgement separated from COM, that case is written in one line.
    ///
    ///     Checks\PositionsTest\bin\PositionsTest.exe
    ///
    /// Exits 0 if everything adds up, 1 if not.
    /// </summary>
    internal static class Program
    {
        private static int trouble;

        // The XY plane, with the normal along Z: it is the arrangement of a hole
        // made on the top face of a plate.
        private static readonly double[] PlaneXy = { 0, 1, 2 };

        private static int Main()
        {
            Console.WriteLine("The verdict on hole positions");
            Console.WriteLine();

            TheCaseThatCouldNotBeTested();
            WhenItCanBeChecked();
            TheThreshold();
            TheDistanceFromAxis();

            Console.WriteLine();
            if (trouble == 0) { Console.WriteLine("Everything adds up."); return 0; }
            Console.WriteLine(trouble + " deviations.");
            return 1;
        }

        // ==================================================================
        // 1. The four roads in which one used to pass for good
        // ==================================================================

        private static void TheCaseThatCouldNotBeTested()
        {
            Console.WriteLine("=== When it could not be looked at");

            // THIS IS THE DEFECT. The feature exists, the points were
            // requested, but on the part there is no cylindrical face of the
            // expected diameter: the holes might not be there at all.
            // Before, this road came out as «verified».
            Outcome("no cylindrical face found",
                  PositionsOutcome.NotVerifiable,
                  new List<double[]>(), null, TwoPoints());

            Outcome("the list of axes is null",
                  PositionsOutcome.NotVerifiable,
                  null, null, TwoPoints());

            Outcome("SOLIDWORKS does not declare the diameter",
                  PositionsOutcome.NotVerifiable,
                  null, "SOLIDWORKS does not declare a diameter for this feature: " +
                        "I have nothing to compare with",
                  TwoPoints());

            Outcome("the feature does not expose the faces",
                  PositionsOutcome.NotVerifiable,
                  null, "the feature does not expose its faces", TwoPoints());

            Outcome("no position asked",
                  PositionsOutcome.NotVerifiable,
                  new List<double[]> { ZAxis(30, 20) }, null, new List<double[]>());

            // A REASON ALWAYS, AND NEVER EMPTY: whoever reads must know WHY it
            // could not be looked at, or «not verifiable» is as useless as
            // «verified» was false.
            string reason;
            PositionsVerdict.Judge(new List<double[]>(), null, PlaneXy, 0.01, TwoPoints(), out reason);
            True("the reason is there and it is a sentence",
                 !string.IsNullOrWhiteSpace(reason) && reason.Length > 20, reason);

            Console.WriteLine();
        }

        // ==================================================================
        // 2. When one can really look
        // ==================================================================

        private static void WhenItCanBeChecked()
        {
            Console.WriteLine("=== When it could be looked at");

            Outcome("two holes where they were asked",
                  PositionsOutcome.Verified,
                  new List<double[]> { ZAxis(30, 20), ZAxis(70, 20) }, null, TwoPoints());

            // One axis for two holes: the second hole is not there. The farthest
            // is 40 mm away, and it must be said.
            Outcome("one of the two holes is not there",
                  PositionsOutcome.Wrong,
                  new List<double[]> { ZAxis(30, 20) }, null, TwoPoints());

            Outcome("one hole ended up elsewhere",
                  PositionsOutcome.Wrong,
                  new List<double[]> { ZAxis(30, 20), ZAxis(72, 20) }, null, TwoPoints());

            // MORE AXES THAN HOLES IS NOT AN ERROR: a counterbored hole has two coaxial
            // cylindrical faces, and the counterbore of the right diameter ends
            // up in there. What counts is that every requested point has one close by.
            Outcome("more axes than holes (coaxial counterbore)",
                  PositionsOutcome.Verified,
                  new List<double[]> { ZAxis(30, 20), ZAxis(30, 20), ZAxis(70, 20) },
                  null, TwoPoints());

            // The sentence of the refusal must NAME the hole and the offset:
            // it is the criterion, not the verdict.
            string reason;
            var outcome = PositionsVerdict.Judge(
                new List<double[]> { ZAxis(30, 20), ZAxis(72, 20) },
                null, PlaneXy, 0.01, TwoPoints(), out reason);
            True("the refusal says which hole and by how much",
                 outcome == PositionsOutcome.Wrong &&
                 reason != null && reason.Contains("2") && reason.Contains("mm"),
                 reason);

            Console.WriteLine();
        }

        // ==================================================================
        // 3. The threshold, looked at from both sides
        // ==================================================================

        private static void TheThreshold()
        {
            Console.WriteLine("=== The threshold of " +
                PositionsVerdict.ToleranceMm.ToString("0.##", CultureInfo.InvariantCulture) + " mm");

            var requested = new List<double[]> { new double[] { 30, 20 } };

            // Inside the threshold: 0.09 mm.
            Outcome("deviation 0.09 mm", PositionsOutcome.Verified,
                  new List<double[]> { ZAxis(30.09, 20) }, null, requested);

            // Outside: 0.11 mm. Two values straddling it, not just one: a threshold
            // tested from one side only is not a threshold, it is an example.
            Outcome("deviation 0.11 mm", PositionsOutcome.Wrong,
                  new List<double[]> { ZAxis(30.11, 20) }, null, requested);

            // EXACTLY ON THE THRESHOLD IS NOT TESTED, and the why is worth more
            // than the case. Written here because it cost a red check.
            //
            // Asking for a hole at 30.1 mm and measuring one at 30 gives an
            // offset of 0.10000000000000142 mm, not 0.1: the round trip
            // millimetres -> metres -> millimetres does not come back exact in floating
            // point. The comparison is `>`, so that case falls among the
            // Wrong, and would have fallen among the Verified with a different
            // number of digits.
            //
            // A behaviour that depends on the last bit is not a
            // behaviour to pin down in a check: what is pinned is the SEPARATION
            // between the two sides, which is what really counts, and it is measured.
            // THE NOISE, AS MEASURED, is the offset that the computation introduces
            // between two points that in millimetres are exactly 0.1 apart.
            // On these values it is about 1.4e-15 mm.
            double noise = Math.Abs(
                PositionsVerdict.DistanceFromAxis(
                    new double[] { Units.M(30.1), Units.M(20), 0.01 },
                    ZAxis(30, 20)) * 1000.0 - 0.1);

            // Between the noise and the threshold there are fourteen orders of
            // magnitude: no real offset can be confused with the
            // noise, and no noise can trigger the refusal by itself.
            // It is the same shape of reasoning as on the gear, where
            // between the two quantities there were two.
            True("the noise is a million times below the threshold",
                 noise > 0 && noise * 1e6 < PositionsVerdict.ToleranceMm,
                 "noise " + noise.ToString("0.0e+00", CultureInfo.InvariantCulture) +
                 " mm against a threshold of " +
                 PositionsVerdict.ToleranceMm.ToString("0.##", CultureInfo.InvariantCulture));

            Console.WriteLine();
        }

        // ==================================================================
        // 4. The geometry underneath, which depends on nothing
        // ==================================================================

        private static void TheDistanceFromAxis()
        {
            Console.WriteLine("=== The distance between a point and the line of a cylinder");

            // Point on the axis.
            Number("point on the axis", 0,
                PositionsVerdict.DistanceFromAxis(
                    new double[] { 0.03, 0.02, 0.05 },
                    new double[] { 0.03, 0.02, 0, 0, 0, 1, 0.0034 }));

            // Point 5 mm to the side, with the axis along Z: the coordinate along the axis
            // must not count.
            Number("5 mm to the side, at any height", 0.005,
                PositionsVerdict.DistanceFromAxis(
                    new double[] { 0.035, 0.02, 0.9 },
                    new double[] { 0.03, 0.02, 0, 0, 0, 1, 0.0034 }));

            // Non-normalised direction: it must come out the same.
            Number("direction not normalized", 0.005,
                PositionsVerdict.DistanceFromAxis(
                    new double[] { 0.035, 0.02, 0.9 },
                    new double[] { 0.03, 0.02, 0, 0, 0, 7.3, 0.0034 }));

            // Null direction: we do not divide by zero, we say «very far».
            Number("null direction", double.MaxValue,
                PositionsVerdict.DistanceFromAxis(
                    new double[] { 0.035, 0.02, 0 },
                    new double[] { 0.03, 0.02, 0, 0, 0, 0, 0.0034 }));

            // Crooked arguments: we do not blow up.
            Number("null cylinder", double.MaxValue,
                PositionsVerdict.DistanceFromAxis(new double[] { 0, 0, 0 }, null));
            Number("short point", double.MaxValue,
                PositionsVerdict.DistanceFromAxis(
                    new double[] { 0, 0 }, new double[] { 0, 0, 0, 0, 0, 1, 1 }));

            Console.WriteLine();
        }

        // ==================================================================

        /// <summary>An axis along Z passing through (x, y) in millimetres.</summary>
        private static double[] ZAxis(double xMm, double yMm)
        {
            return new double[] { Units.M(xMm), Units.M(yMm), 0, 0, 0, 1, 0.0034 };
        }

        /// <summary>Two holes at (30, 20) and (70, 20), in millimetres.</summary>
        private static List<double[]> TwoPoints()
        {
            return new List<double[]>
            {
                new double[] { 30, 20 },
                new double[] { 70, 20 }
            };
        }

        private static void Outcome(
            string itemName, PositionsOutcome expected,
            List<double[]> foundAxes, string cannot, List<double[]> positions)
        {
            string reason;
            var had = PositionsVerdict.Judge(
                foundAxes, cannot, PlaneXy, 0.01, positions, out reason);
            if (had == expected)
            {
                Console.WriteLine("  ok  " + itemName.PadRight(52) + Short(reason));
                return;
            }
            trouble++;
            Console.WriteLine("  XX  " + itemName.PadRight(52) +
                "expected " + expected + ", got " + had + "  " + Short(reason));
        }

        private static void True(string itemName, bool condition, string detail)
        {
            if (condition) { Console.WriteLine("  ok  " + itemName.PadRight(52) + Short(detail)); return; }
            trouble++;
            Console.WriteLine("  XX  " + itemName.PadRight(52) + Short(detail));
        }

        private static void Number(string itemName, double expected, double had)
        {
            bool ok = expected == double.MaxValue
                ? had == double.MaxValue
                : Math.Abs(had - expected) < 1e-9;
            if (ok)
            {
                Console.WriteLine("  ok  " + itemName.PadRight(52) +
                    (expected == double.MaxValue ? "very far"
                     : had.ToString("0.######", CultureInfo.InvariantCulture) + " m"));
                return;
            }
            trouble++;
            Console.WriteLine("  XX  " + itemName.PadRight(52) +
                "expected " + expected.ToString("0.######", CultureInfo.InvariantCulture) +
                ", got " + had.ToString("0.######", CultureInfo.InvariantCulture));
        }

        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= 76 ? s : s.Substring(0, 73) + "...";
        }
    }
}
