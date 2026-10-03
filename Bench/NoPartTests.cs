// =============================================================================
//  Le prove che non aprono SOLIDWORKS  (voci E10 e F12)
// =============================================================================
//
//  PERCHE' STANNO IN UN FILE LORO. `NewOperationsTests.cs` e' il file delle prove sul
//  PEZZO: apre un documento, costruisce, misura, chiude. Queste no. Sono
//  funzioni pure su stringhe - `Frasi.DoveStavaEDoveSta` - e un setaccio che
//  legge i sorgenti del repo. Durano un millesimo di secondo e non hanno
//  bisogno di una licenza.
//
//  Tenerle in mezzo alle altre le faceva sembrare prove di disegno, e faceva
//  sembrare `NewOperationsTests.cs` piu' pieno di numeri attesi di quanto sia: la
//  voce F11 conta proprio quello.
//
//  NON SONO IN `Oce.Solid.Agent.ContractTests` per un motivo solo: chiedono alla LIBRERIA
//  VERA, caricata per riflessione, se quel metodo c'e' e cosa risponde.
//  `Oce.Solid.Agent.ContractTests` la libreria non la carica - e' il progetto che gira dove
//  SOLIDWORKS non c'e', e la libreria l'interop ce l'ha dentro.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
// UN SOLO `IModelDoc2` IN TUTTO IL FILE, e non ne viene creato nessuno: la
// prova del null (F12) passa `null` a `OppureDillo<IModelDoc2>` per guardare
// che cosa dice il banco quando la parte non c'e'. Serve il tipo, non il CAD.
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Bench
{
    internal static partial class Program
    {
        // ==================================================================
        // F12. quando il banco non riesce a preparare il pezzo, lo DICE
        // ==================================================================
        /// <summary>
        /// Il null ha un nome.
        ///
        /// Il 15/9/2026 una prova e' uscita rossa con «Riferimento a un oggetto
        /// non impostato su un'istanza di oggetto.» e basta: su diciotto punti
        /// del banco `NuovaParte()` veniva usata senza guardare se avesse
        /// prodotto qualcosa, e un `NullReferenceException` non dice quale dei
        /// diciotto. Adesso `NuovaParte()` non torna null: alza un'eccezione
        /// che nomina il passo.
        ///
        /// Questa prova guarda che il nome ci sia davvero, e che `Perche()` —
        /// la funzione che il banco usa per raccontare un'eccezione — lo porti
        /// fino alla riga di esito. Senza, «il messaggio nomina il passo»
        /// resterebbe un'intenzione scritta in un commento.
        /// </summary>
        private static void ProvaCheIlNullHaUnNome()
        {
            Console.WriteLine();
            Console.WriteLine("=== quando il banco non riesce a preparare il pezzo (F12) ===");

            // ── IL ROSSO: il null alza, e il messaggio nomina il passo ────
            string detto = null;
            string raccontato = null;
            try
            {
                OppureDillo<IModelDoc2>(null, "la parte nuova su cui provare");
            }
            catch (Exception ex)
            {
                detto = ex.Message;
                raccontato = Perche(ex);
            }
            Segna("F12 ROSSO: un null alza, e dice QUALE passo",
                  detto != null && detto.Contains("la parte nuova su cui provare"),
                  Corto(detto));

            // `Perche` tronca a 110 caratteri: si guarda l'inizio, che e' dove
            // sta il nome del passo, e il tipo dell'eccezione.
            // IL TIPO E' CAMBIATO, E IL CAMBIO E' IL PUNTO (voce K17):
            // `PreparazioneFallita` e' cio' che permette al giro dei casi
            // di non chiamare rossa una prova che non ha fatto. Resta un
            // `InvalidOperationException`, quindi chi lo cattura cosi'
            // continua a catturarlo.
            Segna("F12: il racconto dell'eccezione porta tipo e passo",
                  raccontato != null &&
                  raccontato.StartsWith("PreparazioneFallita:", StringComparison.Ordinal) &&
                  raccontato.Contains("la parte nuova"),
                  Corto(raccontato));

            // ── IL VERDE: quel che c'e' passa, e torna lo stesso oggetto ──
            var qualcosa = new object();
            Segna("F12: quello che c'e' passa, e torna com'era",
                  ReferenceEquals(OppureDillo(qualcosa, "un oggetto qualunque"), qualcosa),
                  "");
        }

        // ==================================================================
        // E10. la frazione dell'ingombro: come SPIEGAZIONE, mai come identita'
        // ==================================================================
        /// <summary>
        /// La frase che dice dove stava una faccia rispetto al pezzo.
        ///
        /// Funzione pura su quattro terne di numeri: non serve SOLIDWORKS, non
        /// serve un documento, e gira in un millesimo di secondo. Sta nel banco
        /// lo stesso perche' il banco e' il posto dove si guarda se qualcosa si
        /// e' rotto, e una funzione che non ha bisogno del CAD e' proprio
        /// quella che nessuno si ricordera' di riprovare a mano.
        /// </summary>
        private static void ProvaDoveStava()
        {
            Console.WriteLine();
            Console.WriteLine("=== dove stava, in frazione dell'ingombro (E10) ===");

            var tipo = _addin.GetType("Oce.Solid.Agent.Core.Frasi", false);
            if (tipo == null) { Segna("E10: la classe c'e'", false, "Frasi assente"); return; }
            var metodo = tipo.GetMethod("DoveStavaEDoveSta");
            if (metodo == null)
            {
                Segna("E10: il metodo c'e'", false, "DoveStavaEDoveSta assente");
                return;
            }

            Func<double[], double[], double[], double[], string> di =
                (prima, adesso, sPrima, sAdesso) =>
                    Convert.ToString(metodo.Invoke(
                        null, new object[] { "F6", prima, adesso, sPrima, sAdesso }));

            // ── IL CASO A DELLA VOCE E2, coi numeri misurati il 14/9 ──────
            // Blocco 100 x 60 x 20 centrato: la scatola va da -50 a +50 in X.
            // La quota passa da 100 a 160 e il pezzo cresce DA UNA PARTE SOLA:
            // -50 .. +110. La faccia destra va da 50 a 110 — sessanta
            // millimetri — e sta al 100% del lato tutte e due le volte.
            string caso = di(
                new[] { 50.0, 0.0, 0.0 }, new[] { 110.0, 0.0, 0.0 },
                new[] { -50.0, -30.0, 0.0, 50.0, 30.0, 20.0 },
                new[] { -50.0, -30.0, 0.0, 110.0, 30.0, 20.0 });
            Segna("E10: al 100% del lato X, e ci sta ancora",
                  caso != null && caso.Contains("100%") && caso.Contains("ci sta ancora") &&
                  caso.Contains("da 100 a 160"),
                  Corto(caso));

            // ── IL ROSSO: una faccia che si e' spostata RISPETTO al pezzo ──
            //
            // Senza questo, «la frase esce» passerebbe anche se dicesse «ci sta
            // ancora» sempre — cioe' anche se il confronto fra le due frazioni
            // non ci fosse. E' il caso che conta: un foro a 20 mm dal bordo su
            // una piastra che passa da 100 a 300 RESTA a 20 mm dal bordo, e la
            // frazione dice che e' andato a 60. Dirlo e' utile; deciderlo
            // sarebbe il difetto che la voce E10 esiste per non introdurre.
            string spostata = di(
                new[] { 20.0, 0.0, 0.0 }, new[] { 20.0, 0.0, 0.0 },
                new[] { 0.0, 0.0, 0.0, 100.0, 60.0, 20.0 },
                new[] { 0.0, 0.0, 0.0, 300.0, 60.0, 20.0 });
            Segna("E10 ROSSO: il foro fermo a 20 mm non si e' mosso, e si tace",
                  string.IsNullOrEmpty(spostata),
                  spostata == null ? "(niente, giusto)" : Corto(spostata));

            // Lo stesso pezzo che cresce, ma la faccia che si sposta a META'
            // invece che restare dov'era in proporzione: la frase deve dire
            // due percentuali diverse e NON deve dire «ci sta ancora».
            string diverse = di(
                new[] { 100.0, 0.0, 0.0 }, new[] { 150.0, 0.0, 0.0 },
                new[] { 0.0, 0.0, 0.0, 100.0, 60.0, 20.0 },
                new[] { 0.0, 0.0, 0.0, 300.0, 60.0, 20.0 });
            Segna("E10 ROSSO: frazioni diverse, «ci sta ancora» non si dice",
                  diverse != null && diverse.Contains("100%") && diverse.Contains("50%") &&
                  !diverse.Contains("ci sta ancora"),
                  Corto(diverse));

            // Senza la scatola non si inventa niente.
            Segna("E10: senza la scatola, nessuna frase",
                  string.IsNullOrEmpty(di(
                      new[] { 50.0, 0.0, 0.0 }, new[] { 110.0, 0.0, 0.0 }, null, null)),
                  "");
        }

        /// <summary>
        /// IL CRITERIO VERO DELLA VOCE E10: una ricerca di «frazione» o
        /// «proporzional» dentro `FirmaCombacia` o dentro il ciclo di
        /// ritrovamento deve tornare **vuota**.
        ///
        /// ── PERCHE' UNA PROVA CHE LEGGE IL SORGENTE ──────────────────────
        ///
        /// Perche' quello che va difeso non e' un comportamento: e' l'ASSENZA
        /// di un comportamento. La frazione dell'ingombro e' semplice, il
        /// numero adesso c'e' gia', e promuoverla da spiegazione a criterio
        /// costa dieci minuti a chiunque passi di qui fra due mesi — chi l'ha
        /// scritta compreso. Nessuna prova su un pezzo puo' accorgersene finche' i due
        /// criteri danno la stessa risposta, e quando daranno risposte diverse
        /// sara' perche' un pezzo e' gia' venuto sbagliato.
        ///
        /// ── E IL SUO ROSSO ───────────────────────────────────────────────
        ///
        /// Un controllo che cerca una parola e non la trova mai e' identico a
        /// un controllo rotto che non cerca niente. Lo stesso setaccio si passa
        /// su `Sentences.cs`, dove quelle parole ci sono per davvero: se li' non
        /// trova niente, il setaccio e' rotto e lo dice.
        /// </summary>
        private static void ProvaCheLaFrazioneNonDecide()
        {
            Console.WriteLine();
            Console.WriteLine("=== la frazione non decide: il grep che deve tornare vuoto (E10) ===");

            string radice;
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var banco = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                radice = Directory.GetParent(banco.FullName).FullName;
            }
            catch (Exception ex)
            {
                Segna("E10: trovo la radice del repo", false, Perche(ex));
                return;
            }

            string operazioni = Path.Combine(radice, "Oce.Solid.Agent.Core", "SwOperations.cs");
            string frasi = Path.Combine(radice, "Oce.Solid.Agent.Core", "Sentences.cs");
            if (!File.Exists(operazioni) || !File.Exists(frasi))
            {
                Saltata("E10: il grep che deve tornare vuoto",
                        "i sorgenti non sono accanto all'eseguibile");
                return;
            }

            // IL SETACCIO FUNZIONA? In `Sentences.cs` quelle parole ci sono.
            string testoFrasi = File.ReadAllText(frasi);
            Segna("E10 ROSSO: il setaccio trova le parole dove ci sono",
                  Pesca(testoFrasi, 0, testoFrasi.Length),
                  "Sentences.cs");

            string testo = File.ReadAllText(operazioni);

            int a = testo.IndexOf("private static bool FirmaCombacia(", StringComparison.Ordinal);
            Segna("E10: FirmaCombacia non parla di frazioni",
                  a >= 0 && !Pesca(testo, a, FineDelMetodo(testo, a)),
                  a < 0 ? "FirmaCombacia non trovata" : "");

            int b = testo.IndexOf("IFace2 ritrovata = null;", StringComparison.Ordinal);
            Segna("E10: il ciclo di ritrovamento non parla di frazioni",
                  b >= 0 && !Pesca(testo, b, FineDelMetodo(testo, b)),
                  b < 0 ? "il ciclo non trovato" : "");
        }

        /// <summary>Le parole vietate, in un pezzo di sorgente.</summary>
        private static bool Pesca(string testo, int da, int a)
        {
            if (da < 0 || a <= da || a > testo.Length) return false;
            string pezzo = testo.Substring(da, a - da);
            return pezzo.IndexOf("frazione", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   pezzo.IndexOf("proporzional", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Dove finisce il metodo che comincia a `da`: la prima graffa aperta,
        /// e poi si conta fino a quando torna a zero. Rozzo — una graffa dentro
        /// una stringa lo confonderebbe — e sufficiente: qui serve a delimitare
        /// un pezzo di testo su cui cercare due parole, non a compilare.
        /// </summary>
        private static int FineDelMetodo(string testo, int da)
        {
            int i = testo.IndexOf('{', da);
            if (i < 0) return testo.Length;
            int aperte = 0;
            for (; i < testo.Length; i++)
            {
                if (testo[i] == '{') aperte++;
                else if (testo[i] == '}' && --aperte == 0) return i + 1;
            }
            return testo.Length;
        }
    }
}
