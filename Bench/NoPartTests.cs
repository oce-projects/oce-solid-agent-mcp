// =============================================================================
//  Le prove che non aprono SOLIDWORKS  (voci E10 e F12)
// =============================================================================
//
//  PERCHE' STANNO IN UN FILE LORO. `NewOperationsTests.cs` e' il file delle prove sul
//  PEZZO: apre un documento, costruisce, misura, chiude. Queste no. Sono
//  funzioni pure su stringhe - `Sentences.WhereItWasAndWhereItIs` - e un setaccio che
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
// prova del null (F12) passa `null` a `OrSayIt<IModelDoc2>` per guardare
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
        /// del banco `NewPart()` veniva usata senza guardare se avesse
        /// prodotto qualcosa, e un `NullReferenceException` non dice quale dei
        /// diciotto. Adesso `NewPart()` non torna null: alza un'eccezione
        /// che nomina il passo.
        ///
        /// Questa prova guarda che il nome ci sia davvero, e che `Why()` —
        /// la funzione che il banco usa per raccontare un'eccezione — lo porti
        /// fino alla riga di esito. Senza, «il messaggio nomina il passo»
        /// resterebbe un'intenzione scritta in un commento.
        /// </summary>
        private static void TestThatNullHasAName()
        {
            Console.WriteLine();
            Console.WriteLine("=== quando il banco non riesce a preparare il pezzo (F12) ===");

            // ── IL ROSSO: il null alza, e il messaggio nomina il passo ────
            string said = null;
            string told = null;
            try
            {
                OrSayIt<IModelDoc2>(null, "la parte nuova su cui provare");
            }
            catch (Exception ex)
            {
                said = ex.Message;
                told = Why(ex);
            }
            Check("F12 ROSSO: un null alza, e dice QUALE passo",
                  said != null && said.Contains("la parte nuova su cui provare"),
                  Short(said));

            // `Why` tronca a 110 caratteri: si guarda l'inizio, che e' dove
            // sta il nome del passo, e il tipo dell'eccezione.
            // IL TIPO E' CAMBIATO, E IL CAMBIO E' IL PUNTO (voce K17):
            // `PreparationFailed` e' cio' che permette al giro dei casi
            // di non chiamare rossa una prova che non ha fatto. Resta un
            // `InvalidOperationException`, quindi chi lo cattura cosi'
            // continua a catturarlo.
            Check("F12: il racconto dell'eccezione porta tipo e passo",
                  told != null &&
                  told.StartsWith("PreparazioneFallita:", StringComparison.Ordinal) &&
                  told.Contains("la parte nuova"),
                  Short(told));

            // ── IL VERDE: quel che c'e' passa, e torna lo stesso oggetto ──
            var something = new object();
            Check("F12: quello che c'e' passa, e torna com'era",
                  ReferenceEquals(OrSayIt(something, "un oggetto qualunque"), something),
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
        private static void TestWhereItWas()
        {
            Console.WriteLine();
            Console.WriteLine("=== dove stava, in frazione dell'ingombro (E10) ===");

            var typ = _addin.GetType("Oce.Solid.Agent.Core.Sentences", false);
            if (typ == null) { Check("E10: la classe c'e'", false, "Frasi assente"); return; }
            var method = typ.GetMethod("WhereItWasAndWhereItIs");
            if (method == null)
            {
                Check("E10: il metodo c'e'", false, "DoveStavaEDoveSta assente");
                return;
            }

            Func<double[], double[], double[], double[], string> di =
                (before, now, sBefore, sNow) =>
                    Convert.ToString(method.Invoke(
                        null, new object[] { "F6", before, now, sBefore, sNow }));

            // ── IL CASO A DELLA VOCE E2, coi numeri misurati il 14/9 ──────
            // Blocco 100 x 60 x 20 centrato: la scatola va da -50 a +50 in X.
            // La quota passa da 100 a 160 e il pezzo cresce DA UNA PARTE SOLA:
            // -50 .. +110. La faccia destra va da 50 a 110 — sessanta
            // millimetri — e sta al 100% del lato tutte e due le volte.
            string testCase = di(
                new[] { 50.0, 0.0, 0.0 }, new[] { 110.0, 0.0, 0.0 },
                new[] { -50.0, -30.0, 0.0, 50.0, 30.0, 20.0 },
                new[] { -50.0, -30.0, 0.0, 110.0, 30.0, 20.0 });
            Check("E10: al 100% del lato X, e ci sta ancora",
                  testCase != null && testCase.Contains("100%") && testCase.Contains("ci sta ancora") &&
                  testCase.Contains("da 100 a 160"),
                  Short(testCase));

            // ── IL ROSSO: una faccia che si e' spostata RISPETTO al pezzo ──
            //
            // Senza questo, «la frase esce» passerebbe anche se dicesse «ci sta
            // ancora» sempre — cioe' anche se il confronto fra le due frazioni
            // non ci fosse. E' il caso che conta: un foro a 20 mm dal bordo su
            // una piastra che passa da 100 a 300 RESTA a 20 mm dal bordo, e la
            // frazione dice che e' andato a 60. Dirlo e' utile; deciderlo
            // sarebbe il difetto che la voce E10 esiste per non introdurre.
            string moved = di(
                new[] { 20.0, 0.0, 0.0 }, new[] { 20.0, 0.0, 0.0 },
                new[] { 0.0, 0.0, 0.0, 100.0, 60.0, 20.0 },
                new[] { 0.0, 0.0, 0.0, 300.0, 60.0, 20.0 });
            Check("E10 ROSSO: il foro fermo a 20 mm non si e' mosso, e si tace",
                  string.IsNullOrEmpty(moved),
                  moved == null ? "(niente, giusto)" : Short(moved));

            // Lo stesso pezzo che cresce, ma la faccia che si sposta a META'
            // invece che restare dov'era in proporzione: la frase deve dire
            // due percentuali diverse e NON deve dire «ci sta ancora».
            string different = di(
                new[] { 100.0, 0.0, 0.0 }, new[] { 150.0, 0.0, 0.0 },
                new[] { 0.0, 0.0, 0.0, 100.0, 60.0, 20.0 },
                new[] { 0.0, 0.0, 0.0, 300.0, 60.0, 20.0 });
            Check("E10 ROSSO: frazioni diverse, «ci sta ancora» non si dice",
                  different != null && different.Contains("100%") && different.Contains("50%") &&
                  !different.Contains("ci sta ancora"),
                  Short(different));

            // Senza la scatola non si inventa niente.
            Check("E10: senza la scatola, nessuna frase",
                  string.IsNullOrEmpty(di(
                      new[] { 50.0, 0.0, 0.0 }, new[] { 110.0, 0.0, 0.0 }, null, null)),
                  "");
        }

        /// <summary>
        /// IL CRITERIO VERO DELLA VOCE E10: una ricerca di «frazione» o
        /// «proporzional» dentro `SignatureMatches` o dentro il ciclo di
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
        private static void TestThatTheFractionDoesNotDecide()
        {
            Console.WriteLine();
            Console.WriteLine("=== la frazione non decide: il grep che deve tornare vuoto (E10) ===");

            string root;
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var bench = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                root = Directory.GetParent(bench.FullName).FullName;
            }
            catch (Exception ex)
            {
                Check("E10: trovo la radice del repo", false, Why(ex));
                return;
            }

            string operations = Path.Combine(root, "Oce.Solid.Agent.Core", "SwOperations.cs");
            string sentences = Path.Combine(root, "Oce.Solid.Agent.Core", "Sentences.cs");
            if (!File.Exists(operations) || !File.Exists(sentences))
            {
                Skipped("E10: il grep che deve tornare vuoto",
                        "i sorgenti non sono accanto all'eseguibile");
                return;
            }

            // IL SETACCIO FUNZIONA? In `Sentences.cs` quelle parole ci sono.
            string sentencesText = File.ReadAllText(sentences);
            Check("E10 ROSSO: il setaccio trova le parole dove ci sono",
                  Pick(sentencesText, 0, sentencesText.Length),
                  "Sentences.cs");

            string text = File.ReadAllText(operations);

            int a = text.IndexOf("private static bool SignatureMatches(", StringComparison.Ordinal);
            Check("E10: FirmaCombacia non parla di frazioni",
                  a >= 0 && !Pick(text, a, MethodEnd(text, a)),
                  a < 0 ? "FirmaCombacia non trovata" : "");

            int b = text.IndexOf("IFace2 foundAgain = null;", StringComparison.Ordinal);
            Check("E10: il ciclo di ritrovamento non parla di frazioni",
                  b >= 0 && !Pick(text, b, MethodEnd(text, b)),
                  b < 0 ? "il ciclo non trovato" : "");
        }

        /// <summary>Le parole vietate, in un pezzo di sorgente.</summary>
        private static bool Pick(string text, int da, int a)
        {
            if (da < 0 || a <= da || a > text.Length) return false;
            string part = text.Substring(da, a - da);
            return part.IndexOf("frazione", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   part.IndexOf("proporzional", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Dove finisce il metodo che comincia a `da`: la prima graffa aperta,
        /// e poi si conta fino a quando torna a zero. Rozzo — una graffa dentro
        /// una stringa lo confonderebbe — e sufficiente: qui serve a delimitare
        /// un pezzo di testo su cui cercare due parole, non a compilare.
        /// </summary>
        private static int MethodEnd(string text, int da)
        {
            int i = text.IndexOf('{', da);
            if (i < 0) return text.Length;
            int open = 0;
            for (; i < text.Length; i++)
            {
                if (text[i] == '{') open++;
                else if (text[i] == '}' && --open == 0) return i + 1;
            }
            return text.Length;
        }
    }
}
