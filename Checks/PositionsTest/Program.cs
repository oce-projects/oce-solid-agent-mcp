using System;
using System.Collections.Generic;
using System.Globalization;
using Oce.Solid.Agent.Core;

namespace Checks.PositionsTest
{
    /// <summary>
    /// IL GIUDIZIO SULLE POSIZIONI DEI FORI, CONTROLLATO SENZA SOLIDWORKS.
    ///
    /// PERCHE' ESISTE. Fino alla 0.25.1 il ponte scriveva
    /// `positions_checked = true` senza condizione, subito dopo una funzione
    /// che tornava «tutto a posto» anche in QUATTRO strade in cui non aveva
    /// guardato niente — compresa la piu' frequente, «non ho trovato nessuna
    /// faccia cilindrica del diametro atteso». E il banco decideva il pass con
    /// `misure.ContainsKey("positions_checked")`: una chiave sempre scritta.
    ///
    /// Il ramo che stampava «POSIZIONI NON VERIFICATE» non poteva scattare.
    /// Un controllo mai visto scattare non e' un controllo.
    ///
    /// PERCHE' QUI E NON NEL BANCO. Il caso che conta — zero facce cilindriche
    /// trovate — con SOLIDWORKS acceso non si sa come costringerlo a capitare:
    /// dipende da come la creazione guidata modella quel particolare foro.
    /// Separato il giudizio dal COM, quel caso si scrive in una riga.
    ///
    ///     Checks\PositionsTest\bin\PositionsTest.exe
    ///
    /// Esce 0 se torna tutto, 1 se no.
    /// </summary>
    internal static class Program
    {
        private static int trouble;

        // Il piano XY, con la normale lungo Z: e' la disposizione di un foro
        // fatto sulla faccia superiore di una piastra.
        private static readonly double[] PlaneXy = { 0, 1, 2 };

        private static int Main()
        {
            Console.WriteLine("Il giudizio sulle posizioni dei fori");
            Console.WriteLine();

            TheCaseThatCouldNotBeTested();
            WhenItCanBeChecked();
            TheThreshold();
            TheDistanceFromAxis();

            Console.WriteLine();
            if (trouble == 0) { Console.WriteLine("Tutto torna."); return 0; }
            Console.WriteLine(trouble + " scostamenti.");
            return 1;
        }

        // ==================================================================
        // 1. Le quattro strade in cui prima si passava per buoni
        // ==================================================================

        private static void TheCaseThatCouldNotBeTested()
        {
            Console.WriteLine("=== Quando non si e' potuto guardare");

            // QUESTO E' IL DIFETTO. La lavorazione esiste, i punti sono stati
            // chiesti, ma sul pezzo non c'e' nessuna faccia cilindrica del
            // diametro atteso: i fori potrebbero non esserci affatto.
            // Prima questa strada usciva come «verificate».
            Outcome("nessuna faccia cilindrica trovata",
                  PositionsOutcome.NotVerifiable,
                  new List<double[]>(), null, TwoPoints());

            Outcome("la lista degli assi e' nulla",
                  PositionsOutcome.NotVerifiable,
                  null, null, TwoPoints());

            Outcome("SOLIDWORKS non dichiara il diametro",
                  PositionsOutcome.NotVerifiable,
                  null, "SOLIDWORKS non dichiara un diametro per questa lavorazione: " +
                        "non ho un termine di confronto",
                  TwoPoints());

            Outcome("la lavorazione non espone le facce",
                  PositionsOutcome.NotVerifiable,
                  null, "la lavorazione non espone le sue facce", TwoPoints());

            Outcome("nessuna posizione chiesta",
                  PositionsOutcome.NotVerifiable,
                  new List<double[]> { ZAxis(30, 20) }, null, new List<double[]>());

            // UN MOTIVO SEMPRE, E MAI VUOTO: chi legge deve sapere PERCHE' non
            // si e' potuto guardare, o «non verificabile» e' inutile quanto
            // «verificate» era falso.
            string reason;
            PositionsVerdict.Judge(new List<double[]>(), null, PlaneXy, 0.01, TwoPoints(), out reason);
            True("il motivo c'e' ed e' una frase",
                 !string.IsNullOrWhiteSpace(reason) && reason.Length > 20, reason);

            Console.WriteLine();
        }

        // ==================================================================
        // 2. Quando si puo' guardare davvero
        // ==================================================================

        private static void WhenItCanBeChecked()
        {
            Console.WriteLine("=== Quando si e' potuto guardare");

            Outcome("due fori dove erano stati chiesti",
                  PositionsOutcome.Verified,
                  new List<double[]> { ZAxis(30, 20), ZAxis(70, 20) }, null, TwoPoints());

            // Un asse solo per due fori: il secondo foro non c'e'. Il piu'
            // lontano dista 40 mm, e si deve dire.
            Outcome("un foro dei due non c'e'",
                  PositionsOutcome.Wrong,
                  new List<double[]> { ZAxis(30, 20) }, null, TwoPoints());

            Outcome("un foro finito altrove",
                  PositionsOutcome.Wrong,
                  new List<double[]> { ZAxis(30, 20), ZAxis(72, 20) }, null, TwoPoints());

            // PIU' ASSI CHE FORI NON E' UN ERRORE: un foro lamato ha due facce
            // cilindriche coassiali, e la lamatura del diametro giusto ci
            // finisce dentro. Conta che ogni punto chiesto ne abbia uno vicino.
            Outcome("piu' assi che fori (lamatura coassiale)",
                  PositionsOutcome.Verified,
                  new List<double[]> { ZAxis(30, 20), ZAxis(30, 20), ZAxis(70, 20) },
                  null, TwoPoints());

            // La frase del rifiuto deve NOMINARE il foro e lo scostamento:
            // e' il criterio, non il verdetto.
            string reason;
            var outcome = PositionsVerdict.Judge(
                new List<double[]> { ZAxis(30, 20), ZAxis(72, 20) },
                null, PlaneXy, 0.01, TwoPoints(), out reason);
            True("il rifiuto dice quale foro e di quanto",
                 outcome == PositionsOutcome.Wrong &&
                 reason != null && reason.Contains("2") && reason.Contains("mm"),
                 reason);

            Console.WriteLine();
        }

        // ==================================================================
        // 3. La soglia, guardata da tutt'e due i lati
        // ==================================================================

        private static void TheThreshold()
        {
            Console.WriteLine("=== La soglia di " +
                PositionsVerdict.ToleranceMm.ToString("0.##", CultureInfo.InvariantCulture) + " mm");

            var requested = new List<double[]> { new double[] { 30, 20 } };

            // Dentro la soglia: 0.09 mm.
            Outcome("scostamento 0.09 mm", PositionsOutcome.Verified,
                  new List<double[]> { ZAxis(30.09, 20) }, null, requested);

            // Fuori: 0.11 mm. Due valori a cavallo, non uno solo: una soglia
            // provata da un lato solo non e' una soglia, e' un esempio.
            Outcome("scostamento 0.11 mm", PositionsOutcome.Wrong,
                  new List<double[]> { ZAxis(30.11, 20) }, null, requested);

            // ESATTAMENTE SULLA SOGLIA NON SI PROVA, e il perche' vale piu'
            // del caso. Scritto qui perche' e' costato una prova rossa.
            //
            // Chiedere un foro a 30.1 mm e misurarne uno a 30 da' uno
            // scostamento di 0.10000000000000142 mm, non 0.1: il giro
            // millimetri -> metri -> millimetri non torna esatto in virgola
            // mobile. Il confronto e' `>`, quindi quel caso cade fra gli
            // Sbagliate, e sarebbe caduto fra i Verificate con un numero
            // diverso di cifre.
            //
            // Un comportamento che dipende dall'ultimo bit non e' un
            // comportamento da fissare in una prova: si fissa la SEPARAZIONE
            // fra i due lati, che e' cio' che conta davvero, e la si misura.
            // IL RUMORE, MISURATO: e' lo scostamento che il calcolo introduce
            // fra due punti che in millimetri distano esattamente 0.1.
            // Su questi valori vale circa 1.4e-15 mm.
            double noise = Math.Abs(
                PositionsVerdict.DistanceFromAxis(
                    new double[] { Units.M(30.1), Units.M(20), 0.01 },
                    ZAxis(30, 20)) * 1000.0 - 0.1);

            // Fra il rumore e la soglia ci sono quattordici ordini di
            // grandezza: nessuno scostamento vero puo' essere confuso col
            // rumore, e nessun rumore puo' far scattare il rifiuto da solo.
            // E' la stessa forma del ragionamento sulla ruota dentata, dove
            // fra le due grandezze ce n'erano due.
            True("il rumore sta un milione di volte sotto la soglia",
                 noise > 0 && noise * 1e6 < PositionsVerdict.ToleranceMm,
                 "rumore " + noise.ToString("0.0e+00", CultureInfo.InvariantCulture) +
                 " mm contro una soglia di " +
                 PositionsVerdict.ToleranceMm.ToString("0.##", CultureInfo.InvariantCulture));

            Console.WriteLine();
        }

        // ==================================================================
        // 4. La geometria sotto, che non dipende da niente
        // ==================================================================

        private static void TheDistanceFromAxis()
        {
            Console.WriteLine("=== La distanza fra un punto e la retta di un cilindro");

            // Punto sull'asse.
            Number("punto sull'asse", 0,
                PositionsVerdict.DistanceFromAxis(
                    new double[] { 0.03, 0.02, 0.05 },
                    new double[] { 0.03, 0.02, 0, 0, 0, 1, 0.0034 }));

            // Punto a 5 mm di lato, con l'asse lungo Z: la quota lungo l'asse
            // non deve contare.
            Number("5 mm di lato, a qualunque quota", 0.005,
                PositionsVerdict.DistanceFromAxis(
                    new double[] { 0.035, 0.02, 0.9 },
                    new double[] { 0.03, 0.02, 0, 0, 0, 1, 0.0034 }));

            // Direzione non normalizzata: deve venire uguale.
            Number("direzione non normalizzata", 0.005,
                PositionsVerdict.DistanceFromAxis(
                    new double[] { 0.035, 0.02, 0.9 },
                    new double[] { 0.03, 0.02, 0, 0, 0, 7.3, 0.0034 }));

            // Direzione nulla: non si divide per zero, si dice «lontanissimo».
            Number("direzione nulla", double.MaxValue,
                PositionsVerdict.DistanceFromAxis(
                    new double[] { 0.035, 0.02, 0 },
                    new double[] { 0.03, 0.02, 0, 0, 0, 0, 0.0034 }));

            // Argomenti storti: non si esplode.
            Number("cilindro nullo", double.MaxValue,
                PositionsVerdict.DistanceFromAxis(new double[] { 0, 0, 0 }, null));
            Number("punto corto", double.MaxValue,
                PositionsVerdict.DistanceFromAxis(
                    new double[] { 0, 0 }, new double[] { 0, 0, 0, 0, 0, 1, 1 }));

            Console.WriteLine();
        }

        // ==================================================================

        /// <summary>Un asse lungo Z che passa per (x, y) in millimetri.</summary>
        private static double[] ZAxis(double xMm, double yMm)
        {
            return new double[] { Units.M(xMm), Units.M(yMm), 0, 0, 0, 1, 0.0034 };
        }

        /// <summary>Due fori a (30, 20) e (70, 20), in millimetri.</summary>
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
                "atteso " + expected + ", avuto " + had + "  " + Short(reason));
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
                    (expected == double.MaxValue ? "lontanissimo"
                     : had.ToString("0.######", CultureInfo.InvariantCulture) + " m"));
                return;
            }
            trouble++;
            Console.WriteLine("  XX  " + itemName.PadRight(52) +
                "atteso " + expected.ToString("0.######", CultureInfo.InvariantCulture) +
                ", avuto " + had.ToString("0.######", CultureInfo.InvariantCulture));
        }

        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= 76 ? s : s.Substring(0, 73) + "...";
        }
    }
}
