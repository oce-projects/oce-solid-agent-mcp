using System;
using System.Collections.Generic;
using System.Globalization;

namespace Oce.Solid.Agent.Core
{
    /// <summary>The three possible outcomes of a check on the positions.</summary>
    public enum PositionsOutcome
    {
        /// <summary>Looked at and right.</summary>
        Verified,

        /// <summary>It was not possible to look at them. It does NOT mean they are fine.</summary>
        NotVerifiable,

        /// <summary>Looked at, and at least one is out of tolerance.</summary>
        Wrong
    }

    /// <summary>
    /// The judgment on the hole positions, separated from SOLIDWORKS on purpose.
    ///
    /// WHY THIS CLASS EXISTS: THE SIXTH DEFECT THAT GAVE NO ERROR
    ///
    /// Up to 0.25.1 the check was a single function, `ForiMancanti`, which
    /// returned `null` to say «all fine». It was honest: it returned `null`
    /// also when it had not been able to look at anything, and one of its `return
    /// null` even had the comment
    ///
    ///     if (diameterMm &lt;= 0) return null;   // nothing to compare:
    ///                                          // a verdict is not invented
    ///
    /// The ways in which it abstained were four: unknown diameter, `GetFaces()`
    /// null, the exception, and — the most frequent — NO CYLINDRICAL FACE
    /// of the expected diameter, that is exactly the case in which the holes might
    /// not be there at all.
    ///
    /// Whoever called it then wrote, unconditionally:
    ///
    ///     misure["positions_checked"] = true;
    ///
    /// THE FUNCTION REFUSED TO INVENT THE VERDICT, AND THE CALLER INVENTED IT IN
    /// ITS PLACE.
    ///
    /// And the bench, which trusted that field to decide the pass, read it
    /// with `misure.ContainsKey("positions_checked")`: a key that is always
    /// written. The branch that printed «POSIZIONI NON VERIFICATE» was dead code
    /// — it could not fire. A check never seen to fire is not a
    /// check, and this one was so twice over: in the bridge and in the bench.
    ///
    /// THE REPAIR has two halves.
    ///
    ///  1. Three outcomes instead of two. «I could not look» is a different
    ///     answer from «I looked and it is fine», and it must be said. It is not a refusal:
    ///     the operation succeeded, it is the check that did not take place. It is
    ///     measured to EXPLAIN, not to refuse — and a wrong refusal
    ///     remains worse than a wrong explanation.
    ///  2. The judgment lives HERE, without SOLIDWORKS. Collecting the cylindrical
    ///     faces requires COM; deciding what to do with them does not. Separated, the
    ///     second piece can be tried at a desk — including the case
    ///     «zero axes found», which with SOLIDWORKS running nobody knows how to
    ///     force to happen. `Checks\PositionsTest` does it.
    ///
    /// The key in the outcome is DIFFERENT, not `false`: `positions_not_checked`
    /// with the reason inside. A `positions_checked = false` would have been read
    /// as «verified» by any caller that looks whether the key is there —
    /// which is precisely the error being repaired.
    /// </summary>
    public static class PositionsVerdict
    {
        /// <summary>
        /// How far a hole may be from where it was asked for, in millimetres.
        ///
        /// It is not a machining tolerance: it is the margin within which it is
        /// recognised that SOLIDWORKS put the hole where it was told to.
        /// A larger deviation than this is not imprecision, it is another
        /// position.
        /// </summary>
        public const double ToleranceMm = 0.1;

        /// <summary>
        /// Judges the hole positions by comparing the cylinder axes
        /// found on the feature with the points that were asked for.
        ///
        /// <param name="foundAxes">
        /// The `CylinderParams` of the cylindrical faces of the right diameter: seven
        /// doubles each, the first three the point on the axis, the next three the
        /// direction, the seventh the radius. May be null or empty.
        /// </param>
        /// <param name="cannot">
        /// The reason why the caller could not collect the axes, if it
        /// could not. If it is set, it wins over everything else: it exits
        /// with NotVerifiable and this reason.
        /// </param>
        /// <param name="axes">
        /// Which model coordinate corresponds to the first and to the second
        /// of the plane, and which is the normal: three indices among 0, 1 and 2.
        /// </param>
        /// <param name="offsetM">The offset along the normal, in metres.</param>
        /// <param name="positions">The points asked for, in millimetres, in the plane.</param>
        /// <param name="reason">
        /// With Wrong, the sentence to give to whoever reads. With NotVerifiable,
        /// why it was not possible to look. With Verified, null.
        /// </param>
        /// </summary>
        public static PositionsOutcome Judge(
            List<double[]> foundAxes,
            string cannot,
            double[] axes,
            double offsetM,
            List<double[]> positions,
            out string reason)
        {
            if (!string.IsNullOrEmpty(cannot))
            {
                reason = cannot;
                return PositionsOutcome.NotVerifiable;
            }

            if (positions == null || positions.Count == 0)
            {
                reason = "no position was requested, there is nothing to compare";
                return PositionsOutcome.NotVerifiable;
            }

            // THE CASE THAT USED TO PASS FOR GOOD.
            // No cylindrical face of the expected diameter on the feature:
            // it does not mean that the holes are in the right place, it means that it
            // is not even known whether they are there.
            if (foundAxes == null || foundAxes.Count == 0)
            {
                reason = "on the feature there is no cylindrical face of the " +
                         "expected diameter: I cannot tell where the holes ended up";
                return PositionsOutcome.NotVerifiable;
            }

            if (axes == null || axes.Length < 3)
            {
                reason = "I do not know which plane to look at";
                return PositionsOutcome.NotVerifiable;
            }

            double worse = 0;
            int far = -1;
            for (int i = 0; i < positions.Count; i++)
            {
                var expected = new double[3];
                expected[(int)axes[0]] = Units.M(positions[i][0]);
                expected[(int)axes[1]] = Units.M(positions[i][1]);
                expected[(int)axes[2]] = offsetM;

                double near = double.MaxValue;
                foreach (var par in foundAxes)
                    near = Math.Min(near, DistanceFromAxis(expected, par));
                if (near > worse) { worse = near; far = i; }
            }

            if (worse * 1000.0 > ToleranceMm)
            {
                reason = string.Format(
                    CultureInfo.InvariantCulture,
                    "Hole {0}, requested at ({1:0.##}, {2:0.##}), ended up {3:0.##} mm " +
                    "from there. I do not let it pass as good: withdraw it — " +
                    Sentences.HowToWithdrawIt + ".",
                    far + 1, positions[far][0], positions[far][1], worse * 1000.0);
                return PositionsOutcome.Wrong;
            }

            reason = null;
            return PositionsOutcome.Verified;
        }

        /// <summary>Distance between a point and the line of a cylinder, in metres.</summary>
        public static double DistanceFromAxis(double[] point, double[] cylinder)
        {
            if (point == null || cylinder == null || point.Length < 3 || cylinder.Length < 6)
                return double.MaxValue;
            double vx = point[0] - cylinder[0];
            double vy = point[1] - cylinder[1];
            double vz = point[2] - cylinder[2];
            double ax = cylinder[3], ay = cylinder[4], az = cylinder[5];
            double standard = Math.Sqrt(ax * ax + ay * ay + az * az);
            if (standard < 1e-12) return double.MaxValue;
            ax /= standard; ay /= standard; az /= standard;
            double along = vx * ax + vy * ay + vz * az;
            double dx = vx - along * ax, dy = vy - along * ay, dz = vz - along * az;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
