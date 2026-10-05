using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using static Oce.Solid.Agent.Bench.Expectations;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// LA REGRESSIONE SUI RIFERIMENTI (voce F8).
    ///
    /// ── IL RISULTATO E' UNA DI TRE COSE, NON DUE ──────────────────────────
    ///
    ///   * **REGGE** — la ricostruzione va, nessuna lavorazione in errore, e la
    ///     geometria a valle sta dove l'intento dichiara;
    ///   * **SI STACCA E LO DICE** — la ricostruzione fallisce, oppure una
    ///     lavorazione porta un codice d'errore. **Il progettista lo vede**, e
    ///     un difetto che si vede e' un problema, non una trappola;
    ///   * **SI STACCA IN SILENZIO** — ricostruzione a posto, nessun errore,
    ///     e la geometria a valle **e' finita da un'altra parte**.
    ///
    /// La terza riga e' l'unico numero che conta, ed e' quello che nessuno
    /// misura. Il progetto piu' avanzato del campo confronta due documenti
    /// interi a fine lavoro con tolleranza **1%**: su un pezzo da 200 mm sono
    /// 2 mm, cioe' non vede un foro che si e' spostato di uno spessore di
    /// parete. Qui il criterio e' **per feature e per posizione**.
    ///
    /// ── L'INTENTO, perche' senza non esiste risposta giusta ───────────────
    ///
    /// Un foro a 20 mm dal bordo, su una piastra che si allarga, deve restare a
    /// 20 mm dal bordo oppure restare dov'era? **Dipende da cosa voleva chi ha
    /// disegnato**, e finche' non lo si dichiara non c'e' niente da misurare.
    ///
    ///   * `ancorato_a_assoluto` — «il foro resta a x = 80 nel modello»;
    ///   * `ancorato_a_bordo` — «il foro resta a 20 mm dal bordo destro».
    ///
    /// **Fino alla libreria 0.115.0 il sistema sapeva esprimere solo il
    /// primo:** `x_mm: 80` e' assoluto per costruzione, e i casi
    /// `ancorato_a_bordo` fallivano tutti — la misura di partenza di E6/E7.
    /// Dalla 0.116.0 le operazioni su faccia prendono `anchor` (voce E18:
    /// `cylindrical_cut` e `rectangular_cut`; dalla 0.118.0 `slot`;
    /// dalla 0.119.0 `extrude_on_face` e `hole_wizard`): i casi il cui
    /// piano la usa possono reggere. Dichiararli attesi-verdi sarebbe barare;
    /// dichiararli attesi-rossi sarebbe rendere permanente un limite. Si
    /// contano e basta.
    ///
    /// ── E LA RIGA DEI SILENZIOSI SI SCRIVE ANCHE SE VALE ZERO ─────────────
    ///
    /// Un conteggio che sparisce quando e' zero e' un conteggio che nessuno
    /// controlla.
    /// </summary>
    internal static partial class Program
    {
        internal enum Outcome { Holds, DetachesAndSaysSo, DetachesInSilence }

        internal sealed class CaseOutcome
        {
            public string Name;
            public string Intent;
            public Outcome Come;
            public string Why;
        }

        internal static void RegressionRun()
        {
            Console.WriteLine();
            Console.WriteLine("=== la regressione sui riferimenti (F8) ===");

            var folder = RegressionFolder();
            if (folder == null) return;
            var file = Directory.GetFiles(folder, "*.json")
                                .OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal).ToList();
            if (file.Count == 0)
            {
                BenchError("regressione: c'e' almeno un caso", "cartella vuota: " + folder);
                return;
            }

            if (!LoadCaseKeys()) return;

            // One regression case, one entry of the test ledger, with its
            // outcome in English words (W3.1-bis.2, item 7).
            var outcomes = new List<CaseOutcome>();
            foreach (var f in file)
                using (var scope = Ledger.OpenCase(Path.GetFileNameWithoutExtension(f),
                                                   Path.GetFileName(Path.GetDirectoryName(f)), "regression"))
                {
                    int aliasUsesAtStart = AliasUsesNow();
                    var e = ACase(f);
                    CountAliasUses("regression/" + Path.GetFileNameWithoutExtension(f), f, aliasUsesAtStart);
                    scope.Outcome = scope.HasBenchErrors ? "bench_error"
                                  : e.Come == Outcome.Holds ? "holds"
                                  : e.Come == Outcome.DetachesAndSaysSo ? "breaks_and_says_so"
                                  : "breaks_silently";
                    outcomes.Add(e);
                }

            int holds = outcomes.Count(e => e.Come == Outcome.Holds);
            int noisy = outcomes.Count(e => e.Come == Outcome.DetachesAndSaysSo);
            int silent = outcomes.Count(e => e.Come == Outcome.DetachesInSilence);

            Console.WriteLine();
            foreach (var e in outcomes)
                Console.WriteLine("  " + Symbol(e.Come) + "  " + e.Name.PadRight(34) +
                                  e.Intent.PadRight(22) + Short(e.Why));

            WriteRegression(outcomes, holds, noisy, silent);

            // I TRE NUMERI, e sommano al numero dei casi. Si scrivono come
            // esiti del banco perche' il resoconto li prenda da li'.
            Check("regressione: reggono", true, holds + " su " + outcomes.Count);
            Check("regressione: si staccano e lo dicono", true, noisy + " su " + outcomes.Count);
            Check("regressione: SI STACCANO IN SILENZIO", true, silent + " su " + outcomes.Count);
            Check("regressione: i tre numeri sommano ai casi",
                  holds + noisy + silent == outcomes.Count,
                  holds + " + " + noisy + " + " + silent + " = " + outcomes.Count);
        }

        private static string Symbol(Outcome e)
        {
            switch (e)
            {
                case Outcome.Holds: return "REGGE   ";
                case Outcome.DetachesAndSaysSo: return "LO DICE ";
                default: return "SILENZIO";
            }
        }

        private static string RegressionFolder()
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var bench = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                var c = Path.Combine(bench.FullName, "cases", "regression");
                if (!Directory.Exists(c)) { BenchError("regressione: la cartella c'e'", c); return null; }
                return c;
            }
            catch (Exception ex) { BenchError("regressione: trovo la cartella", Why(ex)); return null; }
        }

        // ==================================================================
        // UN CASO: costruisci, modifica, guarda
        // ==================================================================
        private static CaseOutcome ACase(string filePath)
        {
            string itemName = Path.GetFileNameWithoutExtension(filePath);
            var outside = new CaseOutcome { Name = itemName, Intent = "?", Come = Outcome.DetachesInSilence };

            Dictionary<string, object> testCase;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                testCase = s.DeserializeObject(File.ReadAllText(filePath)) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                BenchError(itemName, "JSON illeggibile: " + Why(ex));
                outside.Why = "JSON illeggibile"; return outside;
            }

            // An unknown key is a case written wrong (W3.1-bis.2, item 4).
            var unknown = testCase == null ? null : _caseKeys.Unknown(testCase, _caseKeys.RegressionRoot);
            if (unknown != null && unknown.Count > 0)
            {
                BenchError(itemName, CaseKeys.Message(unknown));
                outside.Why = "unknown keys"; return outside;
            }

            outside.Intent = Text(testCase, "intent") ?? "?";
            if (outside.Intent != "ancorato_a_assoluto" && outside.Intent != "ancorato_a_bordo")
            {
                // SENZA L'INTENTO NON ESISTE RISPOSTA GIUSTA, e un caso che non
                // lo dichiara non e' un caso: e' un errore di banco.
                BenchError(itemName, "«intent» dev'essere «ancorato_a_assoluto» oppure " +
                                    "«ancorato_a_bordo»: senza, non esiste risposta giusta");
                outside.Why = "intento non dichiarato"; return outside;
            }
            if (Text(testCase, "intent_why") == null)
            {
                BenchError(itemName, "manca «intent_why»: l'intento senza il motivo " +
                                    "si copia dal caso accanto");
                outside.Why = "perche_intento assente"; return outside;
            }

            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();

                // ── COSTRUISCI ───────────────────────────────────────────
                var docState = ReadState(doc);
                foreach (var p in Items(testCase, "build") ?? new List<object>())
                {
                    var planStep = p as Dictionary<string, object>;
                    if (planStep == null) { BenchError(itemName, "un passo non e' un oggetto"); outside.Why = "passo storto"; return outside; }
                    var arguments = Resolve(
                        planStep.ContainsKey("arguments") ? planStep["arguments"] : null, docState)
                        as Dictionary<string, object> ?? new Dictionary<string, object>();
                    string error;
                    var measurements = Run(doc, Text(planStep, "operation"), arguments, out error, docState);
                    docState = ReadState(doc);
                    if (measurements == null)
                    {
                        // IL PEZZO DI PARTENZA DEVE VENIRE. Se non viene, non
                        // si sta misurando la modifica: si sta misurando un
                        // caso scritto male.
                        BenchError(itemName, "la costruzione e' fallita: " + Short(error));
                        outside.Why = "costruzione fallita"; return outside;
                    }
                }

                // ── QUELLO CHE NON DEVE CAMBIARE SI MISURA PRIMA ─────────
                //
                // Molti invarianti di questa suite sono «com'era»: il passo di
                // una ripetizione, la simmetria di uno specchio, il raggio di
                // quattro raccordi. Scriverli come numeri vorrebbe dire
                // calcolare a mano cio' che SOLIDWORKS ha gia' deciso — e
                // sbagliare quel conto trasformerebbe un caso in un falso
                // silenzio. Un'attesa con `same_as_before` si confronta con la
                // misura di PRIMA della modifica.
                var before = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var a in Items(testCase, "after") ?? new List<object>())
                {
                    var expectation = a as Dictionary<string, object>;
                    if (expectation == null || !True(expectation, "same_as_before")) continue;
                    string which = Text(expectation, "what");
                    if (which == null) continue;
                    var here0 = doc;
                    try { before[which] = Measure(which, c => FromDocument(c, here0, docState), docState, null); }
                    catch (CouldNotMeasure ex)
                    {
                        BenchError(itemName, "«" + which + "» non si misura prima della modifica: " + ex.Message);
                        outside.Why = ex.Message; return outside;
                    }
                }

                // ── MODIFICA: UNA SOLA QUOTA, A MONTE ────────────────────
                var change = (testCase.ContainsKey("modify") ? testCase["modify"] : null)
                               as Dictionary<string, object>;
                if (change == null)
                {
                    BenchError(itemName, "manca «modify»"); outside.Why = "modifica assente"; return outside;
                }
                var itsItems = Resolve(change, docState) as Dictionary<string, object>;
                string modError;
                var done = Run(doc, "set_dimension", itsItems, out modError, docState);

                // ── LE TRE STRADE ────────────────────────────────────────
                //
                // Prima si guarda se il pezzo lo DICE: una modifica rifiutata,
                // una ricostruzione che non va, una lavorazione in errore.
                bool rebuilt = false;
                try { rebuilt = doc.EditRebuild3(); } catch { }
                docState = ReadState(doc);
                int inError = StateFeaturesInError(docState);

                if (done == null)
                {
                    outside.Come = Outcome.DetachesAndSaysSo;
                    outside.Why = "la modifica e' stata rifiutata: " + Short(modError);
                    return outside;
                }
                if (!rebuilt || inError > 0)
                {
                    outside.Come = Outcome.DetachesAndSaysSo;
                    outside.Why = !rebuilt
                        ? "la ricostruzione non e' andata"
                        : inError + " lavorazioni in errore";
                    return outside;
                }

                // Ricostruzione pulita: adesso la geometria dice la verita'.
                var broken = new List<string>();
                foreach (var a in Items(testCase, "after") ?? new List<object>())
                {
                    var expectation = a as Dictionary<string, object>;
                    if (expectation == null) continue;
                    _expectations++;
                    Ledger.ExpectationAttempted();
                    string detail;
                    bool ok;
                    var here = doc;

                    // `same_as_before`: l'atteso e' la misura di prima, e si mette
                    // nell'attesa adesso. Cosi' il confronto lo fa lo stesso
                    // codice di sempre, e il messaggio esce nello stesso
                    // formato: «centers_x_mm [...] (atteso [...])».
                    if (True(expectation, "same_as_before"))
                    {
                        object era;
                        if (!before.TryGetValue(Text(expectation, "what") ?? "", out era) || era == null)
                        {
                            BenchError(itemName, "«same_as_before» ma la misura di prima non c'e'");
                            outside.Why = "come_prima senza la misura di prima"; return outside;
                        }
                        expectation["equal_to"] = era;
                    }

                    try { ok = Matches(expectation, c => FromDocument(c, here, docState), docState, done, out detail); }
                    catch (CouldNotMeasure ex)
                    {
                        BenchError(itemName, ex.Message);
                        outside.Why = ex.Message; return outside;
                    }
                    _verifiedExpectations++;
                    Ledger.ExpectationChecked();
                    if (!ok) broken.Add(detail);
                }

                if (broken.Count == 0)
                {
                    outside.Come = Outcome.Holds;
                    outside.Why = "ricostruita pulita, la geometria e' dove l'intento dice";
                }
                else
                {
                    // RICOSTRUZIONE A POSTO, NESSUN ERRORE, E LA GEOMETRIA E'
                    // ALTROVE. E' il numero che conta.
                    outside.Come = Outcome.DetachesInSilence;
                    outside.Why = string.Join(" · ", broken.Take(2));
                }
                return outside;
            }
            catch (Exception ex)
            {
                BenchError(itemName, Why(ex));
                outside.Why = Why(ex);
                return outside;
            }
            finally { Close(doc); }
        }

        private static int StateFeaturesInError(Dictionary<string, object> docState)
        {
            object v;
            if (docState == null || !docState.TryGetValue("features_in_error", out v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        // ==================================================================
        // IL RESOCONTO
        // ==================================================================
        private static void WriteRegression(
            List<CaseOutcome> outcomes, int holds, int noisy, int silent)
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var bench = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                var folder = Path.Combine(bench.FullName, "report");
                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, "regressione.md");

                var t = new List<string>
                {
                    "# La regressione sui riferimenti (F8)",
                    "",
                    "Ogni caso costruisce un pezzo, cambia **una sola quota a monte**, e",
                    "guarda dove e' finita la geometria a valle. Il risultato e' una di TRE",
                    "cose, non due.",
                    "",
                    "| | casi su " + outcomes.Count + " | |",
                    "|---|---|---|",
                    "| **reggono** | " + holds + " | ricostruzione pulita, geometria dove l'intento dice |",
                    "| **si staccano e lo dicono** | " + noisy + " | il progettista lo vede |",
                    "| **SI STACCANO IN SILENZIO** | " + silent + " | ricostruzione a posto, nessun errore, e la geometria e' altrove |",
                    "",
                    "> La terza riga si scrive **anche quando vale zero**: un conteggio che",
                    "> sparisce quando e' zero e' un conteggio che nessuno controlla.",
                    "",
                    "Senza `anchor` `x_mm: 80` e' assoluto per costruzione. Con `anchor`",
                    "(voce E18: fori, tasche, asole, rialzi e fori guidati) il profilo segue",
                    "lo spigolo. Un caso `ancorato_a_bordo` che resta in silenzio e' un intento",
                    "che il sistema non sa ancora esprimere: la misura di cio' che manca.",
                    "",
                    "| caso | intento | esito | |",
                    "|---|---|---|---|",
                };
                foreach (var e in outcomes)
                    t.Add("| `" + e.Name + "` | " + e.Intent + " | " +
                          (e.Come == Outcome.Holds ? "regge"
                           : e.Come == Outcome.DetachesAndSaysSo ? "lo dice" : "**silenzio**") +
                          " | " + (e.Why ?? "").Replace("|", "/") + " |");

                t.Add("");
                t.Add("Per intento:");
                t.Add("");
                t.Add("| intento | casi | reggono |");
                t.Add("|---|---|---|");
                foreach (var g in outcomes.GroupBy(e => e.Intent))
                    t.Add("| " + g.Key + " | " + g.Count() + " | " +
                          g.Count(e => e.Come == Outcome.Holds) + " |");

                File.WriteAllText(file, string.Join("\r\n", t) + "\r\n",
                                  new System.Text.UTF8Encoding(false));
                Console.WriteLine("regressione: " + file);
            }
            catch (Exception ex)
            {
                Console.WriteLine("non ho potuto scrivere regressione.md: " + ex.Message);
            }
        }
    }
}
