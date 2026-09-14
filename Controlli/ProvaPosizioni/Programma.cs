using System;
using System.Collections.Generic;
using System.Globalization;
using Riscontro.Disegno;

namespace Controlli.ProvaPosizioni
{
    /// <summary>
    /// IL GIUDIZIO SULLE POSIZIONI DEI FORI, CONTROLLATO SENZA SOLIDWORKS.
    ///
    /// PERCHE' ESISTE. Fino alla 0.25.1 il ponte scriveva
    /// `posizioni_verificate = true` senza condizione, subito dopo una funzione
    /// che tornava «tutto a posto» anche in QUATTRO strade in cui non aveva
    /// guardato niente — compresa la piu' frequente, «non ho trovato nessuna
    /// faccia cilindrica del diametro atteso». E il banco decideva il pass con
    /// `misure.ContainsKey("posizioni_verificate")`: una chiave sempre scritta.
    ///
    /// Il ramo che stampava «POSIZIONI NON VERIFICATE» non poteva scattare.
    /// Un controllo mai visto scattare non e' un controllo.
    ///
    /// PERCHE' QUI E NON NEL BANCO. Il caso che conta — zero facce cilindriche
    /// trovate — con SOLIDWORKS acceso non si sa come costringerlo a capitare:
    /// dipende da come la creazione guidata modella quel particolare foro.
    /// Separato il giudizio dal COM, quel caso si scrive in una riga.
    ///
    ///     Controlli\ProvaPosizioni\bin\ProvaPosizioni.exe
    ///
    /// Esce 0 se torna tutto, 1 se no.
    /// </summary>
    internal static class Programma
    {
        private static int guai;

        // Il piano XY, con la normale lungo Z: e' la disposizione di un foro
        // fatto sulla faccia superiore di una piastra.
        private static readonly double[] PianoXY = { 0, 1, 2 };

        private static int Main()
        {
            Console.WriteLine("Il giudizio sulle posizioni dei fori");
            Console.WriteLine();

            IlCasoCheNonSiPotevaProvare();
            QuandoSiPuoGuardare();
            LaSoglia();
            LaDistanzaDallAsse();

            Console.WriteLine();
            if (guai == 0) { Console.WriteLine("Tutto torna."); return 0; }
            Console.WriteLine(guai + " scostamenti.");
            return 1;
        }

        // ==================================================================
        // 1. Le quattro strade in cui prima si passava per buoni
        // ==================================================================

        private static void IlCasoCheNonSiPotevaProvare()
        {
            Console.WriteLine("=== Quando non si e' potuto guardare");

            // QUESTO E' IL DIFETTO. La lavorazione esiste, i punti sono stati
            // chiesti, ma sul pezzo non c'e' nessuna faccia cilindrica del
            // diametro atteso: i fori potrebbero non esserci affatto.
            // Prima questa strada usciva come «verificate».
            Esito("nessuna faccia cilindrica trovata",
                  EsitoPosizioni.NonVerificabile,
                  new List<double[]>(), null, DuePunti());

            Esito("la lista degli assi e' nulla",
                  EsitoPosizioni.NonVerificabile,
                  null, null, DuePunti());

            Esito("SOLIDWORKS non dichiara il diametro",
                  EsitoPosizioni.NonVerificabile,
                  null, "SOLIDWORKS non dichiara un diametro per questa lavorazione: " +
                        "non ho un termine di confronto",
                  DuePunti());

            Esito("la lavorazione non espone le facce",
                  EsitoPosizioni.NonVerificabile,
                  null, "la lavorazione non espone le sue facce", DuePunti());

            Esito("nessuna posizione chiesta",
                  EsitoPosizioni.NonVerificabile,
                  new List<double[]> { AsseZ(30, 20) }, null, new List<double[]>());

            // UN MOTIVO SEMPRE, E MAI VUOTO: chi legge deve sapere PERCHE' non
            // si e' potuto guardare, o «non verificabile» e' inutile quanto
            // «verificate» era falso.
            string motivo;
            GiudizioPosizioni.Giudica(new List<double[]>(), null, PianoXY, 0.01, DuePunti(), out motivo);
            Vero("il motivo c'e' ed e' una frase",
                 !string.IsNullOrWhiteSpace(motivo) && motivo.Length > 20, motivo);

            Console.WriteLine();
        }

        // ==================================================================
        // 2. Quando si puo' guardare davvero
        // ==================================================================

        private static void QuandoSiPuoGuardare()
        {
            Console.WriteLine("=== Quando si e' potuto guardare");

            Esito("due fori dove erano stati chiesti",
                  EsitoPosizioni.Verificate,
                  new List<double[]> { AsseZ(30, 20), AsseZ(70, 20) }, null, DuePunti());

            // Un asse solo per due fori: il secondo foro non c'e'. Il piu'
            // lontano dista 40 mm, e si deve dire.
            Esito("un foro dei due non c'e'",
                  EsitoPosizioni.Sbagliate,
                  new List<double[]> { AsseZ(30, 20) }, null, DuePunti());

            Esito("un foro finito altrove",
                  EsitoPosizioni.Sbagliate,
                  new List<double[]> { AsseZ(30, 20), AsseZ(72, 20) }, null, DuePunti());

            // PIU' ASSI CHE FORI NON E' UN ERRORE: un foro lamato ha due facce
            // cilindriche coassiali, e la lamatura del diametro giusto ci
            // finisce dentro. Conta che ogni punto chiesto ne abbia uno vicino.
            Esito("piu' assi che fori (lamatura coassiale)",
                  EsitoPosizioni.Verificate,
                  new List<double[]> { AsseZ(30, 20), AsseZ(30, 20), AsseZ(70, 20) },
                  null, DuePunti());

            // La frase del rifiuto deve NOMINARE il foro e lo scostamento:
            // e' il criterio, non il verdetto.
            string motivo;
            var esito = GiudizioPosizioni.Giudica(
                new List<double[]> { AsseZ(30, 20), AsseZ(72, 20) },
                null, PianoXY, 0.01, DuePunti(), out motivo);
            Vero("il rifiuto dice quale foro e di quanto",
                 esito == EsitoPosizioni.Sbagliate &&
                 motivo != null && motivo.Contains("2") && motivo.Contains("mm"),
                 motivo);

            Console.WriteLine();
        }

        // ==================================================================
        // 3. La soglia, guardata da tutt'e due i lati
        // ==================================================================

        private static void LaSoglia()
        {
            Console.WriteLine("=== La soglia di " +
                GiudizioPosizioni.TolleranzaMm.ToString("0.##", CultureInfo.InvariantCulture) + " mm");

            var chiesto = new List<double[]> { new double[] { 30, 20 } };

            // Dentro la soglia: 0.09 mm.
            Esito("scostamento 0.09 mm", EsitoPosizioni.Verificate,
                  new List<double[]> { AsseZ(30.09, 20) }, null, chiesto);

            // Fuori: 0.11 mm. Due valori a cavallo, non uno solo: una soglia
            // provata da un lato solo non e' una soglia, e' un esempio.
            Esito("scostamento 0.11 mm", EsitoPosizioni.Sbagliate,
                  new List<double[]> { AsseZ(30.11, 20) }, null, chiesto);

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
            double rumore = Math.Abs(
                GiudizioPosizioni.DistanzaDallAsse(
                    new double[] { Unita.M(30.1), Unita.M(20), 0.01 },
                    AsseZ(30, 20)) * 1000.0 - 0.1);

            // Fra il rumore e la soglia ci sono quattordici ordini di
            // grandezza: nessuno scostamento vero puo' essere confuso col
            // rumore, e nessun rumore puo' far scattare il rifiuto da solo.
            // E' la stessa forma del ragionamento sulla ruota dentata, dove
            // fra le due grandezze ce n'erano due.
            Vero("il rumore sta un milione di volte sotto la soglia",
                 rumore > 0 && rumore * 1e6 < GiudizioPosizioni.TolleranzaMm,
                 "rumore " + rumore.ToString("0.0e+00", CultureInfo.InvariantCulture) +
                 " mm contro una soglia di " +
                 GiudizioPosizioni.TolleranzaMm.ToString("0.##", CultureInfo.InvariantCulture));

            Console.WriteLine();
        }

        // ==================================================================
        // 4. La geometria sotto, che non dipende da niente
        // ==================================================================

        private static void LaDistanzaDallAsse()
        {
            Console.WriteLine("=== La distanza fra un punto e la retta di un cilindro");

            // Punto sull'asse.
            Numero("punto sull'asse", 0,
                GiudizioPosizioni.DistanzaDallAsse(
                    new double[] { 0.03, 0.02, 0.05 },
                    new double[] { 0.03, 0.02, 0, 0, 0, 1, 0.0034 }));

            // Punto a 5 mm di lato, con l'asse lungo Z: la quota lungo l'asse
            // non deve contare.
            Numero("5 mm di lato, a qualunque quota", 0.005,
                GiudizioPosizioni.DistanzaDallAsse(
                    new double[] { 0.035, 0.02, 0.9 },
                    new double[] { 0.03, 0.02, 0, 0, 0, 1, 0.0034 }));

            // Direzione non normalizzata: deve venire uguale.
            Numero("direzione non normalizzata", 0.005,
                GiudizioPosizioni.DistanzaDallAsse(
                    new double[] { 0.035, 0.02, 0.9 },
                    new double[] { 0.03, 0.02, 0, 0, 0, 7.3, 0.0034 }));

            // Direzione nulla: non si divide per zero, si dice «lontanissimo».
            Numero("direzione nulla", double.MaxValue,
                GiudizioPosizioni.DistanzaDallAsse(
                    new double[] { 0.035, 0.02, 0 },
                    new double[] { 0.03, 0.02, 0, 0, 0, 0, 0.0034 }));

            // Argomenti storti: non si esplode.
            Numero("cilindro nullo", double.MaxValue,
                GiudizioPosizioni.DistanzaDallAsse(new double[] { 0, 0, 0 }, null));
            Numero("punto corto", double.MaxValue,
                GiudizioPosizioni.DistanzaDallAsse(
                    new double[] { 0, 0 }, new double[] { 0, 0, 0, 0, 0, 1, 1 }));

            Console.WriteLine();
        }

        // ==================================================================

        /// <summary>Un asse lungo Z che passa per (x, y) in millimetri.</summary>
        private static double[] AsseZ(double xMm, double yMm)
        {
            return new double[] { Unita.M(xMm), Unita.M(yMm), 0, 0, 0, 1, 0.0034 };
        }

        /// <summary>Due fori a (30, 20) e (70, 20), in millimetri.</summary>
        private static List<double[]> DuePunti()
        {
            return new List<double[]>
            {
                new double[] { 30, 20 },
                new double[] { 70, 20 }
            };
        }

        private static void Esito(
            string nome, EsitoPosizioni atteso,
            List<double[]> assiTrovati, string nonPosso, List<double[]> posizioni)
        {
            string motivo;
            var avuto = GiudizioPosizioni.Giudica(
                assiTrovati, nonPosso, PianoXY, 0.01, posizioni, out motivo);
            if (avuto == atteso)
            {
                Console.WriteLine("  ok  " + nome.PadRight(52) + Corto(motivo));
                return;
            }
            guai++;
            Console.WriteLine("  XX  " + nome.PadRight(52) +
                "atteso " + atteso + ", avuto " + avuto + "  " + Corto(motivo));
        }

        private static void Vero(string nome, bool condizione, string dettaglio)
        {
            if (condizione) { Console.WriteLine("  ok  " + nome.PadRight(52) + Corto(dettaglio)); return; }
            guai++;
            Console.WriteLine("  XX  " + nome.PadRight(52) + Corto(dettaglio));
        }

        private static void Numero(string nome, double atteso, double avuto)
        {
            bool ok = atteso == double.MaxValue
                ? avuto == double.MaxValue
                : Math.Abs(avuto - atteso) < 1e-9;
            if (ok)
            {
                Console.WriteLine("  ok  " + nome.PadRight(52) +
                    (atteso == double.MaxValue ? "lontanissimo"
                     : avuto.ToString("0.######", CultureInfo.InvariantCulture) + " m"));
                return;
            }
            guai++;
            Console.WriteLine("  XX  " + nome.PadRight(52) +
                "atteso " + atteso.ToString("0.######", CultureInfo.InvariantCulture) +
                ", avuto " + avuto.ToString("0.######", CultureInfo.InvariantCulture));
        }

        private static string Corto(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= 76 ? s : s.Substring(0, 73) + "...";
        }
    }
}
