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

            IlBordo();
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
        // controllo dei contorni in `SagomaSensata` classifica TUTTI i vertici
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
        // La riparazione: tre risposte invece di due. `DoveSta` dice Dentro,
        // Fuori o SulBordo, e chi classifica i contorni tratta «sul bordo»
        // come cio' che e': un contatto, non un attraversamento.

        private static void IlBordo()
        {
            Console.WriteLine("Il bordo");

            var piastra = P(new[] { 0.0, 0 }, new[] { 10.0, 0 },
                            new[] { 10.0, 10 }, new[] { 0.0, 10 });

            // I quattro lati, uno per uno: la risposta deve essere la stessa.
            Dove("un punto sul lato destro", new[] { 10.0, 5 }, piastra, DoveSiTrova.SulBordo);
            Dove("un punto sul lato sinistro", new[] { 0.0, 5 }, piastra, DoveSiTrova.SulBordo);
            Dove("un punto sul lato in alto", new[] { 5.0, 10 }, piastra, DoveSiTrova.SulBordo);
            Dove("un punto sul lato in basso", new[] { 5.0, 0 }, piastra, DoveSiTrova.SulBordo);
            Dove("un vertice", new[] { 10.0, 10 }, piastra, DoveSiTrova.SulBordo);

            // E dentro e fuori continuano a funzionare.
            Dove("il centro", new[] { 5.0, 5 }, piastra, DoveSiTrova.Dentro);
            Dove("appena dentro il bordo destro", new[] { 9.99, 5 }, piastra, DoveSiTrova.Dentro);
            Dove("appena fuori dal bordo destro", new[] { 10.01, 5 }, piastra, DoveSiTrova.Fuori);

            // LA SIMMETRIA, che e' il punto di tutto questo.
            var aDestra = P(new[] { 8.0, 2 }, new[] { 10.0, 2 },
                            new[] { 10.0, 8 }, new[] { 8.0, 8 });
            var aSinistra = P(new[] { 0.0, 2 }, new[] { 2.0, 2 },
                              new[] { 2.0, 8 }, new[] { 0.0, 8 });
            Specchio("una cava a filo del bordo", aDestra, aSinistra, piastra);

            // PERCHE' `DoveSta` ESISTE, fissato in una prova invece che in un
            // commento. `Dentro` da solo — la parita' degli attraversamenti —
            // risponde in modo DIVERSO sulle due cave specchiate: e' il difetto
            // che ha portato a scrivere `DoveSta`, e resta qui perche' se un
            // giorno qualcuno «ripara» `Dentro` questa riga glielo dice.
            int destraDentro = 0, sinistraDentro = 0;
            foreach (var p in aDestra) if (FormaChiusa.Dentro(p, piastra)) destraDentro++;
            foreach (var p in aSinistra) if (FormaChiusa.Dentro(p, piastra)) sinistraDentro++;
            if (destraDentro != sinistraDentro)
                Console.WriteLine("  ok  `Dentro` da solo e' asimmetrico (a destra " +
                    destraDentro + " dentro su 4, a sinistra " + sinistraDentro +
                    "): e' il motivo per cui c'e' `DoveSta`");
            else
                Male("`Dentro` da solo non e' piu' asimmetrico (" + destraDentro +
                     " e " + sinistraDentro + "): rileggere se `DoveSta` serve ancora");

            // E una che sta DAVVERO a cavallo deve restare a cavallo.
            var aCavallo = P(new[] { 8.0, 2 }, new[] { 12.0, 2 },
                             new[] { 12.0, 8 }, new[] { 8.0, 8 });
            int d, f, b;
            Classifica(aCavallo, piastra, out d, out f, out b);
            if (d > 0 && f > 0)
                Console.WriteLine("  ok  una cava davvero a cavallo: " +
                    d + " dentro, " + f + " fuori, " + b + " sul bordo");
            else
                Male("una cava davvero a cavallo non viene piu' vista: " +
                     d + " dentro, " + f + " fuori, " + b + " sul bordo");

            Console.WriteLine();
        }

        private static void Classifica(
            List<double[]> contorno, List<double[]> esterno,
            out int dentro, out int fuori, out int sulBordo)
        {
            dentro = 0; fuori = 0; sulBordo = 0;
            foreach (var punto in contorno)
                switch (FormaChiusa.DoveSta(punto, esterno))
                {
                    case DoveSiTrova.Dentro: dentro++; break;
                    case DoveSiTrova.Fuori: fuori++; break;
                    default: sulBordo++; break;
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
        private static void Specchio(
            string cosa, List<double[]> uno, List<double[]> altro, List<double[]> esterno)
        {
            int d1, f1, b1, d2, f2, b2;
            Classifica(uno, esterno, out d1, out f1, out b1);
            Classifica(altro, esterno, out d2, out f2, out b2);

            if (d1 == d2 && f1 == f2 && b1 == b2)
            {
                Console.WriteLine("  ok  " + cosa +
                    ": stesso verdetto da tutt'e due i lati (" +
                    d1 + " dentro, " + b1 + " sul bordo, " + f1 + " fuori)");
                return;
            }
            Male(cosa + ": a destra " + d1 + "/" + f1 + "/" + b1 +
                 ", a sinistra " + d2 + "/" + f2 + "/" + b2 +
                 "  (dentro/fuori/bordo) - stessa geometria, verdetto diverso");
        }

        private static void Dove(
            string cosa, double[] punto, List<double[]> poligono, DoveSiTrova atteso)
        {
            var avuto = FormaChiusa.DoveSta(punto, poligono);
            if (avuto == atteso) Console.WriteLine("  ok  " + cosa + ": " + avuto);
            else Male(cosa + ": detto " + avuto + ", atteso " + atteso);
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
