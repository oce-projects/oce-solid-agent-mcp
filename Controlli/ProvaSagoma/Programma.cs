using System;
using System.Collections.Generic;
using System.Globalization;
using Riscontro.Disegno;

namespace Controlli.ProvaSagoma
{
    /// <summary>
    /// LA GEOMETRIA DELLE SAGOME, CONTROLLATA SENZA SOLIDWORKS.
    ///
    /// <see cref="FormaChiusa"/> risponde alle due domande che SOLIDWORKS non
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
    ///     Controlli\ProvaSagoma\bin\ProvaSagoma.exe
    ///
    /// Esce 0 se torna tutto, 1 se no.
    /// </summary>
    internal static class Programma
    {
        private static int guai;

        private static int Main()
        {
            Console.WriteLine("La geometria delle sagome");
            Console.WriteLine();

            Anelli();
            Aree();
            Incroci();
            Dentro();

            Console.WriteLine();
            if (guai == 0) { Console.WriteLine("Tutto torna."); return 0; }
            Console.WriteLine(guai + " scostamenti.");
            return 1;
        }

        // ------------------------------------------------------------------

        private static void Anelli()
        {
            Console.WriteLine("L'anello pulito");

            // Il primo ripetuto in coda si toglie: e' il modo in cui quasi
            // tutti scrivono una spezzata "chiusa", e lasciarlo darebbe un
            // lato di lunghezza zero.
            Conta("primo ripetuto in coda",
                FormaChiusa.Anello(P(new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 10 }, new[] { 0.0, 0 })), 3);

            // Le giunzioni dei tratti portano un punto doppio ciascuna.
            Conta("doppioni di fila",
                FormaChiusa.Anello(P(new[] { 0.0, 0 }, new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 0 }, new[] { 5.0, 8 })), 3);

            // Un anello gia' pulito non va toccato.
            Conta("gia' pulito",
                FormaChiusa.Anello(P(new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 5.0, 8 })), 3);

            // Un quadrato ha quattro vertici e il quarto NON e' un doppione:
            // togliere sempre l'ultimo sarebbe l'errore facile da fare qui.
            Conta("quadrato",
                FormaChiusa.Anello(P(new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 10 }, new[] { 0.0, 10 })), 4);
            Console.WriteLine();
        }

        private static void Aree()
        {
            Console.WriteLine("L'area");
            Vale("quadrato 10x10", FormaChiusa.Area(P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 10 }, new[] { 0.0, 10 })), 100, 1e-9);

            // Il verso non conta: l'area e' in valore assoluto, e i contorni
            // arrivano nei due versi a seconda di chi li ha scritti.
            Vale("quadrato al contrario", FormaChiusa.Area(P(
                new[] { 0.0, 0 }, new[] { 0.0, 10 }, new[] { 10.0, 10 }, new[] { 10.0, 0 })), 100, 1e-9);

            Vale("triangolo", FormaChiusa.Area(P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 0.0, 10 })), 50, 1e-9);

            // Tre punti in fila: area nulla. E' il caso che il controllo sulle
            // sagome deve rifiutare.
            Vale("punti allineati", FormaChiusa.Area(P(
                new[] { 0.0, 0 }, new[] { 5.0, 0 }, new[] { 10.0, 0 })), 0, 1e-9);

            // Un poligono a L, che non e' convesso: la formula di Gauss non se
            // ne accorge, ed e' giusto cosi'.
            Vale("elle", FormaChiusa.Area(P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 4 },
                new[] { 4.0, 4 }, new[] { 4.0, 10 }, new[] { 0.0, 10 })), 64, 1e-9);
            Console.WriteLine();
        }

        private static void Incroci()
        {
            Console.WriteLine("Gli incroci");

            Pulito("quadrato", P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 10 }, new[] { 0.0, 10 }));

            // L'elle e' concava. Un controllo scritto male confonde «concavo»
            // con «si incrocia», e rifiuterebbe meta' delle staffe che esistono.
            Pulito("elle concava", P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 4 },
                new[] { 4.0, 4 }, new[] { 4.0, 10 }, new[] { 0.0, 10 }));

            // La stella a cinque punte e' molto concava e non si incrocia.
            var stella = new List<double[]>();
            for (int i = 0; i < 10; i++)
            {
                double raggio = (i % 2 == 0) ? 50 : 20;
                double ang = Math.PI / 2 + i * Math.PI / 5;
                stella.Add(new[] { raggio * Math.Cos(ang), raggio * Math.Sin(ang) });
            }
            Pulito("stella a cinque punte", stella);

            Sporco("papillon", P(
                new[] { 0.0, 0 }, new[] { 100.0, 0 }, new[] { 0.0, 50 }, new[] { 100.0, 50 }));

            Sporco("papillon storto", P(
                new[] { 0.0, 0 }, new[] { 100.0, 0 }, new[] { 10.0, 60 }, new[] { 90.0, 80 }));

            // UN TRIANGOLO NON PUO' INCROCIARSI. Con tre lati i tre sono tutti
            // adiacenti fra loro, e se il controllo non salta le coppie
            // adiacenti accusa ogni triangolo che esista.
            Pulito("triangolo", P(
                new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 5.0, 8 }));
            Console.WriteLine();
        }

        private static void Dentro()
        {
            Console.WriteLine("Dentro e fuori");
            var quadrato = P(new[] { -50.0, -50 }, new[] { 50.0, -50 },
                             new[] { 50.0, 50 }, new[] { -50.0, 50 });
            Sta("il centro", new[] { 0.0, 0 }, quadrato, true);
            Sta("appena dentro", new[] { 49.9, 0 }, quadrato, true);
            Sta("appena fuori", new[] { 50.1, 0 }, quadrato, false);
            Sta("lontano", new[] { 215.0, 0 }, quadrato, false);

            // L'elle e' il caso che smaschera un controllo fatto con la scatola
            // d'ingombro invece che con il poligono: questo punto sta
            // NELL'INCAVO — dentro l'ingombro e fuori dal materiale.
            var elle = P(new[] { 0.0, 0 }, new[] { 10.0, 0 }, new[] { 10.0, 4 },
                         new[] { 4.0, 4 }, new[] { 4.0, 10 }, new[] { 0.0, 10 });
            Sta("nell'incavo dell'elle", new[] { 7.0, 7 }, elle, false);
            Sta("nel pieno dell'elle", new[] { 2.0, 7 }, elle, true);
            Console.WriteLine();
        }

        // ------------------------------------------------------------------

        private static List<double[]> P(params double[][] punti)
        {
            return new List<double[]>(punti);
        }

        private static void Conta(string cosa, List<double[]> anello, int atteso)
        {
            if (anello.Count == atteso) Console.WriteLine("  ok  " + cosa + ": " + atteso + " punti");
            else Male(cosa + ": " + anello.Count + " punti, attesi " + atteso);
        }

        private static void Vale(string cosa, double avuto, double atteso, double entro)
        {
            if (Math.Abs(avuto - atteso) <= entro)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  ok  {0}: {1:0.####}", cosa, avuto));
            else
                Male(string.Format(CultureInfo.InvariantCulture,
                    "{0}: {1:0.######} invece di {2:0.######}", cosa, avuto, atteso));
        }

        private static void Pulito(string cosa, List<double[]> anello)
        {
            int dove = FormaChiusa.DoveSiIncrocia(anello);
            if (dove < 0) Console.WriteLine("  ok  " + cosa + ": non si incrocia");
            else Male(cosa + ": accusato di incrociarsi al lato " + dove + ", e non e' vero");
        }

        private static void Sporco(string cosa, List<double[]> anello)
        {
            int dove = FormaChiusa.DoveSiIncrocia(anello);
            if (dove >= 0) Console.WriteLine("  ok  " + cosa + ": si incrocia al lato " + dove);
            else Male(cosa + ": si incrocia e non se ne accorge");
        }

        private static void Sta(string cosa, double[] punto, List<double[]> poligono, bool atteso)
        {
            bool dentro = FormaChiusa.Dentro(punto, poligono);
            if (dentro == atteso)
                Console.WriteLine("  ok  " + cosa + ": " + (dentro ? "dentro" : "fuori"));
            else
                Male(cosa + ": detto " + (dentro ? "dentro" : "fuori") +
                     ", atteso " + (atteso ? "dentro" : "fuori"));
        }

        private static void Male(string riga)
        {
            guai++;
            Console.WriteLine("  XX  " + riga);
        }
    }
}
