using System;
using System.IO;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE CALL THAT DOES NOT RETURN (entries K18 and Z-C4).
    ///
    /// ── WHY IT CAN BE TESTED WITHOUT SOLIDWORKS ──────────────────────────
    ///
    /// Because the leash **calls nothing**: it receives the time from outside and
    /// answers a single question — «has too much time passed since it
    /// started?». A clock passed as an argument is a clock that can be
    /// made to lie, and *a guard that cannot be made to trigger cannot be
    /// believed*.
    ///
    /// ── AND THE HALF THAT THESE CHECKS DO NOT COVER ──────────────────────
    ///
    /// That the bench's guardian really prints and dies, with a real stuck
    /// COM call, these checks **do not say**: they read the source and
    /// verify that it is wired in. The real test is the day SOLIDWORKS
    /// gets stuck again — and at that point either the report is read, or nothing
    /// was done.
    /// </summary>
    internal static class LeashTests
    {
        internal static void All()
        {
            Group("la chiamata che non torna (K18)");

            var t0 = new DateTime(2026, 9, 23, 15, 36, 0);
            var g = new Shared.Leash(90000);

            // ── FIRST: IT STAYS SILENT WHEN THERE IS NOTHING TO SAY ──────
            //
            // The half that is usually not tested. A guardian that shouts between
            // one check and the next gets switched off the next day.
            Check("senza nessuna chiamata in corso, tace",
                  g.Hanging(t0.AddHours(3)) == null,
                  "tre ore di silenzio, nessun allarme");

            // ── A NORMAL CALL DOES NOT MAKE IT TRIGGER ───────────────────
            g.Begin("extrude_rectangle", t0);
            Check("una chiamata di 30 secondi non e' appesa",
                  g.Hanging(t0.AddSeconds(30)) == null,
                  "soglia 90 s");
            Check("e nemmeno una di 89",
                  g.Hanging(t0.AddSeconds(89)) == null, "");

            // ── AT NINETY IT TRIGGERS ────────────────────────────────────
            var report = g.Hanging(t0.AddSeconds(90));
            Check("a novanta secondi scatta", report != null,
                  report == null ? "(niente)"
                                  : report.Substring(0, Math.Min(90, report.Length)));

            // AND THE REPORT SAYS WHICH ONE, which is the whole point: on 23/9/2026 the
            // run stayed hung and nobody knew inside what.
            Check("e il referto NOMINA la chiamata",
                  report != null && report.Contains("extrude_rectangle"),
                  "senza il nome sarebbe un allarme senza indirizzo");

            Check("e dice da quanto, coi secondi",
                  report != null && report.Contains("90"),
                  "«e' partita 90 secondi fa»");

            // AND IT DOES NOT PROMISE WHAT IT CANNOT DO. A message that said
            // «I retry» or «I cancel» would be false: a stuck COM call
            // cannot be cancelled.
            // WITHOUT IGNORING CASE this line was born red: the report
            // says «**N**on si puo' annullare» because a sentence starts there.
            // It is the third time that a string comparison has fallen like this in
            // this repository — the COMPARISON is fixed, not the message.
            Check("e non promette di annullarla",
                  report != null &&
                  report.IndexOf("non si puo' annullare",
                                  StringComparison.OrdinalIgnoreCase) >= 0,
                  "dice quale era, e si ferma");

            // ── WHEN IT RETURNS, THE GUARDIAN MUST STAY SILENT ───────────
            //
            // Without this, the first pause between two checks would become an
            // alarm and the run would die by itself halfway.
            g.Finished();
            Check("finita la chiamata, torna a tacere",
                  g.Hanging(t0.AddHours(1)) == null,
                  "un'ora dopo, nessun allarme");

            // ── A RIDICULOUS THRESHOLD IS REFUSED ────────────────────────
            bool refused = false;
            try { new Shared.Leash(500); }
            catch (ArgumentOutOfRangeException) { refused = true; }
            Check("mezzo secondo non e' un guinzaglio, e viene rifiutato",
                  refused, "qualunque prova vera lo farebbe scattare");

            // ── AND THE BENCH REALLY USES IT ─────────────────────────────
            TheBenchUsesIt();
        }

        private static void TheBenchUsesIt()
        {
            var file = Path.Combine(Root(), "Bench", "Program.cs");
            if (!File.Exists(file))
            {
                Check("il sorgente del banco c'e'", false, "manca " + file);
                return;
            }
            var p = File.ReadAllText(file);

            Check("il banco costruisce il guinzaglio a 90 secondi",
                  p.Contains("new Shared.Leash(90000)"), "Bench/Program.cs");

            // A GUARDIAN DEFINED AND NEVER SWITCHED ON is the disease that this
            // repository chases, applied to itself.
            Check("e lo ACCENDE",
                  p.Contains("TurnOnGuardian();"),
                  "definirlo e non chiamarlo sarebbe un `Tested = true` che nessuno esercita");

            Check("ogni operazione passa dal guinzaglio",
                  p.Contains("Leash.Begin(operation,") &&
                  p.Contains("finally { Leash.Finished(); }"),
                  "con un `finally`: se la chiamata alza, il guinzaglio si slaccia comunque");

            // AND THE NEW PART, which is where it really got stuck.
            Check("e anche NewDocument, che e' dove si e' piantato il 23/9",
                  p.Contains("Leash.Begin(\"NewDocument"),
                  "il modello di documento letto da una condivisione di rete");

            Check("e l'uscita e' un codice suo, il 5",
                  p.Contains("System.Environment.Exit(5)"),
                  "non 1 e non 2: un giro appeso non e' un giro con prove rosse");

            TheLibraryUsesIt();
        }

        /// <summary>
        /// THE OTHER HALF: the leash inside the library (entry Z-C4).
        ///
        /// Same class, two callers, two different reactions — and the two
        /// reactions are the thing to look at. The bench is a run that either
        /// finishes or is of no use: it dies. The bridge is somebody
        /// else's service: it **says so** and goes on.
        /// </summary>
        private static void TheLibraryUsesIt()
        {
            var file = Path.Combine(Root(), "Oce.Solid.Agent.Core", "PlanExecutor.cs");
            if (!File.Exists(file))
            {
                Check("il sorgente dell'esecutore c'e'", false, "manca " + file);
                return;
            }
            var p = File.ReadAllText(file);

            Check("anche la libreria ha il guinzaglio",
                  p.Contains("new Shared.Leash("),
                  "la STESSA classe del banco: due scadenze che si comportano " +
                  "diversamente sono peggio di una sola");

            Check("e ogni passo del piano ci passa",
                  p.Contains("_leash.Begin(planStep.Operation") &&
                  p.Contains("finally { _leash.Finished(); }"),
                  "col `finally`: un passo che alza non deve lasciare il guinzaglio teso");

            // ── THE LINE THAT PROTECTS THE PROTOCOL ──────────────────────
            //
            // Over stdio, stdout IS the JSON-RPC channel. A report printed there
            // is not a nuisance: it breaks the conversation, and the client answers
            // -32700 to a request that has nothing to do with it.
            Check("il referto va su stderr, MAI su stdout",
                  p.Contains("Console.Error.WriteLine(\"[ponte] \"") &&
                  !p.Contains("Console.WriteLine(report)"),
                  "stdout e' il canale JSON-RPC");

            // ── AND IT DOES NOT KILL SOMEBODY ELSE'S PROCESS ─────────────
            int guardianIndex = p.IndexOf("TurnOnGuardian()", StringComparison.Ordinal);
            string body = guardianIndex < 0 ? "" : p.Substring(guardianIndex);
            Check("e non uccide il ponte, che e' un servizio di qualcun altro",
                  !body.Contains("Environment.Exit"),
                  "il banco muore, il ponte dice e va avanti");
        }
    }
}
