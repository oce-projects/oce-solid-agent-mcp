using System;
using System.Collections.Generic;
using System.Globalization;
using Oce.Solid.Agent.Core;

namespace Checks.OutlineTest
{
    /// <summary>
    /// THE GEOMETRY OF THE OUTLINES, CHECKED WITHOUT SOLIDWORKS.
    ///
    /// <see cref="ClosedShape"/> answers the two questions that SOLIDWORKS does not
    /// ask — does a contour cross itself? does a void sit inside the border? — and
    /// on those answers depends whether a plan is drawn or refused.
    /// An error here is not seen: a part is seen.
    ///
    /// WHY ALSO THE CASES THAT MUST PASS.
    /// A check that refuses too much is worse than one that refuses too little: the
    /// first blocks good work and is not discovered until somebody
    /// bumps into it. Half of the cases below are legitimate outlines, and must
    /// pass.
    ///
    ///     Checks\OutlineTest\bin\OutlineTest.exe
    ///
    /// Exits 0 if everything adds up, 1 if not.
    /// </summary>
    internal static class Program
    {
        private static int trouble;

        private static int Main()
        {
            Console.WriteLine("La geometria delle sagome");
            Console.WriteLine();

            Rings();
            Areas();
            Crossings();
            Inside();

            Console.WriteLine();
            if (trouble == 0) { Console.WriteLine("Tutto torna."); return 0; }
            Console.WriteLine(trouble + " scostamenti.");
            return 1;
        }

        // ------------------------------------------------------------------

        private static void Rings()
        {
            Console.WriteLine("L'anello pulito");

            // The first one repeated at the tail is removed: it is the way almost
            // everybody writes a "closed" polyline, and leaving it would give a
            // side of length zero.
            Count("primo ripetuto in coda",
                ClosedShape.Ring(P(new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 10 }, new[] { 0.0, 0 })), 3);

            // The joins of the strokes carry a double point each.
            Count("doppioni di fila",
                ClosedShape.Ring(P(new[] { 0.0, 0 }, new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 0 }, new[] { 5.0, 8 })), 3);

            // A ring already clean must not be touched.
            Count("gia' pulito",
                ClosedShape.Ring(P(new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 5.0, 8 })), 3);

            // A square has four vertices and the fourth is NOT a duplicate:
            // always removing the last one would be the easy mistake to make here.
            Count("square",
                ClosedShape.Ring(P(new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 10 }, new[] { 0.0, 10 })), 4);
            Console.WriteLine();
        }

        private static void Areas()
        {
            Console.WriteLine("L'area");
            Holds("quadrato 10x10", ClosedShape.Area(P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 10 }, new[] { 0.0, 10 })), 100, 1e-9);

            // The direction does not matter: the area is an absolute value, and the contours
            // arrive in both directions depending on who wrote them.
            Holds("quadrato al contrario", ClosedShape.Area(P(
                new[] { 0.0, 0 }, new[] { 0.0, 10 }, new[] { 10.0, 10 }, new[] { 10.0, 0 })), 100, 1e-9);

            Holds("triangolo", ClosedShape.Area(P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 0.0, 10 })), 50, 1e-9);

            // Three points in a row: null area. It is the case that the check on the
            // outlines must refuse.
            Holds("punti allineati", ClosedShape.Area(P(
                new[] { 0.0, 0 }, new[] { 5.0, 0 }, new[] { 10.0, 0 })), 0, 1e-9);

            // An L-shaped polygon, which is not convex: the Gauss formula does not
            // notice, and it is right so.
            Holds("elle", ClosedShape.Area(P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 4 },
                new[] { 4.0, 4 }, new[] { 4.0, 10 }, new[] { 0.0, 10 })), 64, 1e-9);
            Console.WriteLine();
        }

        private static void Crossings()
        {
            Console.WriteLine("Gli incroci");

            CleanCase("square", P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 10 }, new[] { 0.0, 10 }));

            // The L is concave. A badly written check confuses «concave»
            // with «self-intersects», and would refuse half of the brackets that exist.
            CleanCase("elle concava", P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 4 },
                new[] { 4.0, 4 }, new[] { 4.0, 10 }, new[] { 0.0, 10 }));

            // The five-pointed star is very concave and does not self-intersect.
            var star = new List<double[]>();
            for (int i = 0; i < 10; i++)
            {
                double radius = (i % 2 == 0) ? 50 : 20;
                double angle = Math.PI / 2 + i * Math.PI / 5;
                star.Add(new[] { radius * Math.Cos(angle), radius * Math.Sin(angle) });
            }
            CleanCase("stella a cinque punte", star);

            Dirty("papillon", P(
                new[] { 0.0, 0 }, new[] { 100.0, 0 }, new[] { 0.0, 50 }, new[] { 100.0, 50 }));

            Dirty("papillon storto", P(
                new[] { 0.0, 0 }, new[] { 100.0, 0 }, new[] { 10.0, 60 }, new[] { 90.0, 80 }));

            // A TRIANGLE CANNOT SELF-INTERSECT. With three sides all three are
            // adjacent to each other, and if the check does not skip the adjacent
            // pairs it accuses every triangle that exists.
            CleanCase("triangolo", P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 5.0, 8 }));
            Console.WriteLine();
        }

        private static void Inside()
        {
            Console.WriteLine("Dentro e fuori");
            var square = P(new[] { -50.0, -50 }, new[] { 50.0, -50 },
                             new[] { 50.0, 50 }, new[] { -50.0, 50 });
            Sta("il centro", new[] { 0.0, 0 }, square, true);
            Sta("appena dentro", new[] { 49.9, 0 }, square, true);
            Sta("appena fuori", new[] { 50.1, 0 }, square, false);
            Sta("lontano", new[] { 215.0, 0 }, square, false);

            // The L is the case that unmasks a check made with the bounding box
            // instead of with the polygon: this point sits
            // IN THE NOTCH — inside the bounding box and outside the material.
            var ell = P(new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 4 },
                         new[] { 4.0, 4 }, new[] { 4.0, 10 }, new[] { 0.0, 10 });
            Sta("nell'incavo dell'elle", new[] { 7.0, 7 }, ell, false);
            Sta("nel pieno dell'elle", new[] { 2.0, 7 }, ell, true);
            Console.WriteLine();

            TheBorder();
        }

        // ==================================================================
        // The border, which is neither inside nor outside
        // ==================================================================
        //
        // THE PARITY OF THE CROSSINGS CANNOT ANSWER ON THE BORDER, and the
        // answer it gives DEPENDS ON THE SIDE. With the comparison `x < taglio` a
        // vertex on the right border turns out outside and the same vertex on the left
        // border turns out inside.
        //
        // It would not be serious if it were a detail of a single point. But the
        // check of the contours in `SensibleOutline` classifies ALL the vertices
        // of a contour and refuses when it finds some inside and some outside
        // together («sta A CAVALLO del bordo esterno»). A cut-out flush with the
        // right border has two vertices «outside» and two «inside»: refused. The
        // same cut-out mirrored to the left has all four vertices
        // «inside»: accepted.
        //
        // Same geometry, opposite verdict depending on the side, and the wrong one
        // is a REFUSAL OF LEGITIMATE WORK — a cut-out flush with the
        // border is an open slot, it is done every day.
        //
        // The repair: three answers instead of two. `WhereItIs` says Inside,
        // Outside or OnBorder, and whoever classifies the contours treats «on the border»
        // as what it is: a contact, not a crossing.

        private static void TheBorder()
        {
            Console.WriteLine("Il bordo");

            var plate = P(new[] { 0.0, 0 }, new[] { 10.0, 0 },
                            new[] { 10.0, 10 }, new[] { 0.0, 10 });

            // The four sides, one by one: the answer must be the same.
            Where("un punto sul lato destro", new[] { 10.0, 5 }, plate, WhereItIsFound.OnBorder);
            Where("un punto sul lato sinistro", new[] { 0.0, 5 }, plate, WhereItIsFound.OnBorder);
            Where("un punto sul lato in alto", new[] { 5.0, 10 }, plate, WhereItIsFound.OnBorder);
            Where("un punto sul lato in basso", new[] { 5.0, 0 }, plate, WhereItIsFound.OnBorder);
            Where("un vertice", new[] { 10.0, 10 }, plate, WhereItIsFound.OnBorder);

            // And inside and outside keep working.
            Where("il centro", new[] { 5.0, 5 }, plate, WhereItIsFound.Inside);
            Where("appena dentro il bordo destro", new[] { 9.99, 5 }, plate, WhereItIsFound.Inside);
            Where("appena fuori dal bordo destro", new[] { 10.01, 5 }, plate, WhereItIsFound.Outside);

            // THE SYMMETRY, which is the point of all this.
            var onRight = P(new[] { 8.0, 2 }, new[] { 10.0, 2 },
                            new[] { 10.0, 8 }, new[] { 8.0, 8 });
            var onLeft = P(new[] { 0.0, 2 }, new[] { 2.0, 2 },
                              new[] { 2.0, 8 }, new[] { 0.0, 8 });
            MirrorShape("una cava a filo del bordo", onRight, onLeft, plate);

            // WHY `WhereItIs` EXISTS, fixed in a check instead of in a
            // comment. `Inside` alone — the parity of the crossings —
            // answers DIFFERENTLY on the two mirrored cut-outs: it is the defect
            // that led to writing `WhereItIs`, and it stays here because if one
            // day somebody «fixes» `Inside` this line tells them so.
            int rightInside = 0, leftInside = 0;
            foreach (var p in onRight) if (ClosedShape.Inside(p, plate)) rightInside++;
            foreach (var p in onLeft) if (ClosedShape.Inside(p, plate)) leftInside++;
            if (rightInside != leftInside)
                Console.WriteLine("  ok  `Inside` da solo e' asimmetrico (a destra " +
                    rightInside + " dentro su 4, a sinistra " + leftInside +
                    "): e' il motivo per cui c'e' `WhereItIs`");
            else
                Male("`Inside` da solo non e' piu' asimmetrico (" + rightInside +
                     " e " + leftInside + "): rileggere se `WhereItIs` serve ancora");

            // And one that REALLY straddles must stay straddling.
            var straddling = P(new[] { 8.0, 2 }, new[] { 12.0, 2 },
                             new[] { 12.0, 8 }, new[] { 8.0, 8 });
            int d, f, b;
            Classify(straddling, plate, out d, out f, out b);
            if (d > 0 && f > 0)
                Console.WriteLine("  ok  una cava davvero a cavallo: " +
                    d + " dentro, " + f + " fuori, " + b + " sul bordo");
            else
                Male("una cava davvero a cavallo non viene piu' vista: " +
                     d + " dentro, " + f + " fuori, " + b + " sul bordo");

            Console.WriteLine();
        }

        private static void Classify(
            List<double[]> contour, List<double[]> outer,
            out int inside, out int outside, out int onBorder)
        {
            inside = 0; outside = 0; onBorder = 0;
            foreach (var point in contour)
                switch (ClosedShape.WhereItIs(point, outer))
                {
                    case WhereItIsFound.Inside: inside++; break;
                    case WhereItIsFound.Outside: outside++; break;
                    default: onBorder++; break;
                }
        }

        /// <summary>
        /// Two mirrored geometries must give the SAME classification.
        ///
        /// Only the symmetry is checked and nothing else. The first version of this
        /// check also asked for `f1 == 0`, that is that both of them PASSED: it was
        /// the wrong direction, and SOLIDWORKS said so. A cut-out flush with the
        /// border cannot be extruded from either side — two contours that
        /// touch do not delimit a region — so the right verdict is
        /// «no» from both sides, and what was broken was the «yes» on the
        /// left.
        ///
        /// What this check must defend is that the two sides do not
        /// diverge. WHAT is answered is decided by the part, not by it.
        /// </summary>
        private static void MirrorShape(
            string what, List<double[]> one, List<double[]> other, List<double[]> outer)
        {
            int d1, f1, b1, d2, f2, b2;
            Classify(one, outer, out d1, out f1, out b1);
            Classify(other, outer, out d2, out f2, out b2);

            if (d1 == d2 && f1 == f2 && b1 == b2)
            {
                Console.WriteLine("  ok  " + what +
                    ": stesso verdetto da tutt'e due i lati (" +
                    d1 + " dentro, " + b1 + " sul bordo, " + f1 + " fuori)");
                return;
            }
            Male(what + ": a destra " + d1 + "/" + f1 + "/" + b1 +
                 ", a sinistra " + d2 + "/" + f2 + "/" + b2 +
                 "  (dentro/fuori/bordo) - stessa geometria, verdetto diverso");
        }

        private static void Where(
            string what, double[] point, List<double[]> polygon, WhereItIsFound expected)
        {
            var had = ClosedShape.WhereItIs(point, polygon);
            if (had == expected) Console.WriteLine("  ok  " + what + ": " + had);
            else Male(what + ": detto " + had + ", atteso " + expected);
        }

        // ------------------------------------------------------------------

        private static List<double[]> P(params double[][] points)
        {
            return new List<double[]>(points);
        }

        private static void Count(string what, List<double[]> ring, int expected)
        {
            if (ring.Count == expected) Console.WriteLine("  ok  " + what + ": " + expected + " punti");
            else Male(what + ": " + ring.Count + " punti, attesi " + expected);
        }

        private static void Holds(string what, double had, double expected, double within)
        {
            if (Math.Abs(had - expected) <= within)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  ok  {0}: {1:0.####}", what, had));
            else
                Male(string.Format(CultureInfo.InvariantCulture,
                    "{0}: {1:0.######} invece di {2:0.######}", what, had, expected));
        }

        private static void CleanCase(string what, List<double[]> ring)
        {
            int location = ClosedShape.WhereItCrosses(ring);
            if (location < 0) Console.WriteLine("  ok  " + what + ": non si incrocia");
            else Male(what + ": accusato di incrociarsi al lato " + location + ", e non e' vero");
        }

        private static void Dirty(string what, List<double[]> ring)
        {
            int location = ClosedShape.WhereItCrosses(ring);
            if (location >= 0) Console.WriteLine("  ok  " + what + ": si incrocia al lato " + location);
            else Male(what + ": si incrocia e non se ne accorge");
        }

        private static void Sta(string what, double[] point, List<double[]> polygon, bool expected)
        {
            bool inside = ClosedShape.Inside(point, polygon);
            if (inside == expected)
                Console.WriteLine("  ok  " + what + ": " + (inside ? "dentro" : "fuori"));
            else
                Male(what + ": detto " + (inside ? "dentro" : "fuori") +
                     ", atteso " + (expected ? "dentro" : "fuori"));
        }

        private static void Male(string line)
        {
            trouble++;
            Console.WriteLine("  XX  " + line);
        }
    }
}
