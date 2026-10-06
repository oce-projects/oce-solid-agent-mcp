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
            Group("the call that does not return (K18)");

            var t0 = new DateTime(2026, 9, 23, 15, 36, 0);
            var g = new Shared.Leash(90000);

            // ── FIRST: IT STAYS SILENT WHEN THERE IS NOTHING TO SAY ──────
            //
            // The half that is usually not tested. A guardian that shouts between
            // one check and the next gets switched off the next day.
            Check("with no call in progress, it stays silent",
                  g.Hanging(t0.AddHours(3)) == null,
                  "three hours of silence, no alarm");

            // ── A NORMAL CALL DOES NOT MAKE IT TRIGGER ───────────────────
            g.Begin("extrude_rectangle", t0);
            Check("a call of 30 seconds is not hung",
                  g.Hanging(t0.AddSeconds(30)) == null,
                  "threshold 90 s");
            Check("and neither is one of 89",
                  g.Hanging(t0.AddSeconds(89)) == null, "");

            // ── AT NINETY IT TRIGGERS ────────────────────────────────────
            var report = g.Hanging(t0.AddSeconds(90));
            Check("at ninety seconds it fires", report != null,
                  report == null ? "(nothing)"
                                  : report.Substring(0, Math.Min(90, report.Length)));

            // AND THE REPORT SAYS WHICH ONE, which is the whole point: on 23/9/2026 the
            // run stayed hung and nobody knew inside what.
            Check("and the report NAMES the call",
                  report != null && report.Contains("extrude_rectangle"),
                  "without the name it would be an alarm with no address");

            Check("and says for how long, with the seconds",
                  report != null && report.Contains("90"),
                  "«it started 90 seconds ago»");

            // AND IT DOES NOT PROMISE WHAT IT CANNOT DO. A message that said
            // «I retry» or «I cancel» would be false: a stuck COM call
            // cannot be cancelled.
            // WITHOUT IGNORING CASE this line was born red: the report
            // says «**N**on si puo' annullare» because a sentence starts there.
            // It is the third time that a string comparison has fallen like this in
            // this repository — the COMPARISON is fixed, not the message.
            Check("and does not promise to cancel it",
                  report != null &&
                  report.IndexOf("cannot be cancelled",
                                  StringComparison.OrdinalIgnoreCase) >= 0,
                  "says which one it was, and stops");

            // ── WHEN IT RETURNS, THE GUARDIAN MUST STAY SILENT ───────────
            //
            // Without this, the first pause between two checks would become an
            // alarm and the run would die by itself halfway.
            g.Finished();
            Check("once the call is over, it goes silent again",
                  g.Hanging(t0.AddHours(1)) == null,
                  "an hour later, no alarm");

            // ── A RIDICULOUS THRESHOLD IS REFUSED ────────────────────────
            bool refused = false;
            try { new Shared.Leash(500); }
            catch (ArgumentOutOfRangeException) { refused = true; }
            Check("half a second is not a leash, and it is refused",
                  refused, "any real check would make it fire");

            // ── AND THE BENCH REALLY USES IT ─────────────────────────────
            TheBenchUsesIt();
        }

        private static void TheBenchUsesIt()
        {
            var file = Path.Combine(Root(), "Bench", "Program.cs");
            if (!File.Exists(file))
            {
                Check("the bench source is there", false, "missing " + file);
                return;
            }
            var p = File.ReadAllText(file);

            Check("the bench builds the leash at 90 seconds",
                  p.Contains("new Shared.Leash(90000)"), "Bench/Program.cs");

            // A GUARDIAN DEFINED AND NEVER SWITCHED ON is the disease that this
            // repository chases, applied to itself.
            Check("and TURNS IT ON",
                  p.Contains("TurnOnGuardian();"),
                  "defining it and not calling it would be a `Tested = true` that nobody exercises");

            Check("every operation goes through the leash",
                  p.Contains("Leash.Begin(operation,") &&
                  p.Contains("finally { Leash.Finished(); }"),
                  "with a `finally`: if the call raises, the leash is loosened anyway");

            // AND THE NEW PART, which is where it really got stuck.
            Check("and NewDocument too, which is where it got stuck on 23/9",
                  p.Contains("Leash.Begin(\"NewDocument"),
                  "the document template read from a network share");

            Check("and the exit is its own code, 5",
                  p.Contains("System.Environment.Exit(5)"),
                  "not 1 and not 2: a hung run is not a run with red checks");

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
                Check("the executor source is there", false, "missing " + file);
                return;
            }
            var p = File.ReadAllText(file);

            Check("the library has the leash too",
                  p.Contains("new Shared.Leash("),
                  "the SAME class as the bench: two timeouts that behave " +
                  "differently are worse than one");

            Check("and every step of the plan goes through it",
                  p.Contains("_leash.Begin(planStep.Operation") &&
                  p.Contains("finally { _leash.Finished(); }"),
                  "with the `finally`: a step that raises must not leave the leash taut");

            // ── THE LINE THAT PROTECTS THE PROTOCOL ──────────────────────
            //
            // Over stdio, stdout IS the JSON-RPC channel. A report printed there
            // is not a nuisance: it breaks the conversation, and the client answers
            // -32700 to a request that has nothing to do with it.
            Check("the report goes to stderr, NEVER to stdout",
                  p.Contains("Console.Error.WriteLine(\"[bridge] \"") &&
                  !p.Contains("Console.WriteLine(report)"),
                  "stdout is the JSON-RPC channel");

            // ── AND IT DOES NOT KILL SOMEBODY ELSE'S PROCESS ─────────────
            int guardianIndex = p.IndexOf("TurnOnGuardian()", StringComparison.Ordinal);
            string body = guardianIndex < 0 ? "" : p.Substring(guardianIndex);
            Check("and it does not kill the bridge, which is somebody else's service",
                  !body.Contains("Environment.Exit"),
                  "the bench dies, the bridge says so and goes on");
        }
    }
}
