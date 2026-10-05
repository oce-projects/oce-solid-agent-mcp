using System;
using System.Collections.Generic;
using System.Globalization;
using Oce.Solid.Agent.Core;

namespace Checks.OutlineTest
{
    /// <summary>
    /// LA GEOMETRIA DELLE SAGOME, CONTROLLATA SENZA SOLIDWORKS.
    ///
    /// <see cref="ClosedShape"/> risponde alle due domande che SOLIDWORKS non
    /// fa — un contorno si incrocia da solo? un vuoto sta dentro il bordo? — e
    /// da quelle risposte dipende se un piano viene disegnato o rifiutato.
    /// Un errore qui non si vede: si vede un pezzo.
    ///
    /// PERCHE' ANCHE I CASI CHE DEVONO PASSARE.
    /// Un controllo che rifiuta troppo e' peggio di uno che rifiuta poco: il
    /// primo blocca lavoro buono e non si scopre finche' qualcuno non ci
    /// sbatte. Meta' dei casi qui sotto sono sagome legittime, e devono
    /// passare.
    ///
    ///     Checks\OutlineTest\bin\OutlineTest.exe
    ///
    /// Esce 0 se torna tutto, 1 se no.
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

            // Il primo ripetuto in coda si toglie: e' il modo in cui quasi
            // tutti scrivono una spezzata "chiusa", e lasciarlo darebbe un
            // lato di lunghezza zero.
            Count("primo ripetuto in coda",
                ClosedShape.Ring(P(new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 10 }, new[] { 0.0, 0 })), 3);

            // Le giunzioni dei tratti portano un punto doppio ciascuna.
            Count("doppioni di fila",
                ClosedShape.Ring(P(new[] { 0.0, 0 }, new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 0 }, new[] { 5.0, 8 })), 3);

            // Un anello gia' pulito non va toccato.
            Count("gia' pulito",
                ClosedShape.Ring(P(new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 5.0, 8 })), 3);

            // Un quadrato ha quattro vertici e il quarto NON e' un doppione:
            // togliere sempre l'ultimo sarebbe l'errore facile da fare qui.
            Count("square",
                ClosedShape.Ring(P(new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 10 }, new[] { 0.0, 10 })), 4);
            Console.WriteLine();
        }

        private static void Areas()
        {
            Console.WriteLine("L'area");
            Holds("quadrato 10x10", ClosedShape.Area(P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 10 }, new[] { 0.0, 10 })), 100, 1e-9);

            // Il verso non conta: l'area e' in valore assoluto, e i contorni
            // arrivano nei due versi a seconda di chi li ha scritti.
            Holds("quadrato al contrario", ClosedShape.Area(P(
                new[] { 0.0, 0 }, new[] { 0.0, 10 }, new[] { 10.0, 10 }, new[] { 10.0, 0 })), 100, 1e-9);

            Holds("triangolo", ClosedShape.Area(P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 0.0, 10 })), 50, 1e-9);

            // Tre punti in fila: area nulla. E' il caso che il controllo sulle
            // sagome deve rifiutare.
            Holds("punti allineati", ClosedShape.Area(P(
                new[] { 0.0, 0 }, new[] { 5.0, 0 }, new[] { 10.0, 0 })), 0, 1e-9);

            // Un poligono a L, che non e' convesso: la formula di Gauss non se
            // ne accorge, ed e' giusto cosi'.
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

            // L'elle e' concava. Un controllo scritto male confonde «concavo»
            // con «si incrocia», e rifiuterebbe meta' delle staffe che esistono.
            CleanCase("elle concava", P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 4 },
                new[] { 4.0, 4 }, new[] { 4.0, 10 }, new[] { 0.0, 10 }));

            // La stella a cinque punte e' molto concava e non si incrocia.
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

            // UN TRIANGOLO NON PUO' INCROCIARSI. Con tre lati i tre sono tutti
            // adiacenti fra loro, e se il controllo non salta le coppie
            // adiacenti accusa ogni triangolo che esista.
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

            // L'elle e' il caso che smaschera un controllo fatto con la scatola
            // d'ingombro invece che con il poligono: questo punto sta
            // NELL'INCAVO — dentro l'ingombro e fuori dal materiale.
            var ell = P(new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 4 },
                         new[] { 4.0, 4 }, new[] { 4.0, 10 }, new[] { 0.0, 10 });
            Sta("nell'incavo dell'elle", new[] { 7.0, 7 }, ell, false);
            Sta("nel pieno dell'elle", new[] { 2.0, 7 }, ell, true);
            Console.WriteLine();

            TheBorder();
        }

        // ==================================================================
        // Il bordo, che non e' ne' dentro ne' fuori
        // ==================================================================
        //
        // LA PARITA' DEGLI ATTRAVERSAMENTI NON SA RISPONDERE SUL BORDO, e la
        // risposta che da' DIPENDE DAL LATO. Con il confronto `x < taglio` un
        // vertice sul bordo destro risulta fuori e lo stesso vertice sul bordo
        // sinistro risulta dentro.
        //
        // Non sarebbe grave se fosse un dettaglio di un punto solo. Ma il
        // controllo dei contorni in `SensibleOutline` classifica TUTTI i vertici
        // di un contorno e rifiuta quando ne trova di dentro e di fuori
        // insieme («sta A CAVALLO del bordo esterno»). Una cava a filo del
        // bordo destro ha due vertici «fuori» e due «dentro»: rifiutata. La
        // stessa cava specchiata a sinistra ha tutti e quattro i vertici
        // «dentro»: accettata.
        //
        // Stessa geometria, verdetto opposto a seconda del lato, e quello
        // sbagliato e' un RIFIUTO DI LAVORO LEGITTIMO — una cava a filo del
        // bordo e' un'asola aperta, si fa tutti i giorni.
        //
        // La riparazione: tre risposte invece di due. `WhereItIs` dice Dentro,
        // Fuori o OnBorder, e chi classifica i contorni tratta «sul bordo»
        // come cio' che e': un contatto, non un attraversamento.

        private static void TheBorder()
        {
            Console.WriteLine("Il bordo");

            var plate = P(new[] { 0.0, 0 }, new[] { 10.0, 0 },
                            new[] { 10.0, 10 }, new[] { 0.0, 10 });

            // I quattro lati, uno per uno: la risposta deve essere la stessa.
            Where("un punto sul lato destro", new[] { 10.0, 5 }, plate, WhereItIsFound.OnBorder);
            Where("un punto sul lato sinistro", new[] { 0.0, 5 }, plate, WhereItIsFound.OnBorder);
            Where("un punto sul lato in alto", new[] { 5.0, 10 }, plate, WhereItIsFound.OnBorder);
            Where("un punto sul lato in basso", new[] { 5.0, 0 }, plate, WhereItIsFound.OnBorder);
            Where("un vertice", new[] { 10.0, 10 }, plate, WhereItIsFound.OnBorder);

            // E dentro e fuori continuano a funzionare.
            Where("il centro", new[] { 5.0, 5 }, plate, WhereItIsFound.Inside);
            Where("appena dentro il bordo destro", new[] { 9.99, 5 }, plate, WhereItIsFound.Inside);
            Where("appena fuori dal bordo destro", new[] { 10.01, 5 }, plate, WhereItIsFound.Outside);

            // LA SIMMETRIA, che e' il punto di tutto questo.
            var onRight = P(new[] { 8.0, 2 }, new[] { 10.0, 2 },
                            new[] { 10.0, 8 }, new[] { 8.0, 8 });
            var onLeft = P(new[] { 0.0, 2 }, new[] { 2.0, 2 },
                              new[] { 2.0, 8 }, new[] { 0.0, 8 });
            MirrorShape("una cava a filo del bordo", onRight, onLeft, plate);

            // PERCHE' `WhereItIs` ESISTE, fissato in una prova invece che in un
            // commento. `Inside` da solo — la parita' degli attraversamenti —
            // risponde in modo DIVERSO sulle due cave specchiate: e' il difetto
            // che ha portato a scrivere `WhereItIs`, e resta qui perche' se un
            // giorno qualcuno «ripara» `Inside` questa riga glielo dice.
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

            // E una che sta DAVVERO a cavallo deve restare a cavallo.
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
        /// Due geometrie specchiate devono dare la STESSA classificazione.
        ///
        /// Si controlla la simmetria e nient'altro. La prima versione di questa
        /// prova chiedeva anche `f1 == 0`, cioe' che tutte e due PASSASSERO: era
        /// la direzione sbagliata, e l'ha detto SOLIDWORKS. Una cava a filo del
        /// bordo non si estrude da nessuno dei due lati — due contorni che si
        /// toccano non delimitano una regione — quindi il verdetto giusto e'
        /// «no» da tutt'e due le parti, e cio' che era rotto era il «si» a
        /// sinistra.
        ///
        /// Quello che questa prova deve difendere e' che i due lati non
        /// divergano. COSA si risponde lo decide il pezzo, non lei.
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
