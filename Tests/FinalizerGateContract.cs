using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Shared;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// NO SOLIDWORKS OBJECT RELEASED WHILE A CALL RUNS (bridge 0.160.2, Shared.FinalizerGate).
    ///
    /// Neither check needs SOLIDWORKS. The gate: with the gate open a dropped object is
    /// finalized (the control: without it the next check would prove nothing); with the gate
    /// closed the finalizer thread is held and nothing is finalized, and the object is finalized
    /// once the gate opens. The wiring: Program.cs closes the gate before Serve, opens it after
    /// Serve, and collects (ReleaseSolidWorksObjects, which waits for the finalizers) only
    /// outside the closed gate — inside it, the collection would wait for ever.
    /// </summary>
    internal static class FinalizerGateContract
    {
        internal static void RunAll()
        {
            Group("no SOLIDWORKS object released while a call runs (bridge 0.160.2)");
            Gate();
            Wiring();
        }

        // =================================================================
        // THE GATE
        // =================================================================

        private static readonly ManualResetEvent Finalized = new ManualResetEvent(false);

        private sealed class Witness
        {
            ~Witness() { Finalized.Set(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DropAWitness()
        {
            new Witness();
        }

        private static void Gate()
        {
            Finalized.Reset();
            DropAWitness();
            GC.Collect();
            bool free = Finalized.WaitOne(2000);
            Check("gate open: a dropped object is finalized within 2 s (the control)", free,
                  free ? "finalized" : "not finalized: the check of the closed gate would prove nothing");

            const string label = "gate closed: the finalizer thread held, nothing finalized in 1 s; the object finalized once the gate opens";
            bool held = false, early = true;
            try
            {
                held = FinalizerGate.Close(5000);
                Finalized.Reset();
                DropAWitness();
                GC.Collect();
                early = Finalized.WaitOne(1000);
            }
            finally { FinalizerGate.Open(); }
            bool late = Finalized.WaitOne(5000);
            var wrong = new List<string>();
            if (!held) wrong.Add("the finalizer thread was not held within 5 s");
            if (early) wrong.Add("an object was finalized while the gate was closed");
            if (!late) wrong.Add("the object was not finalized within 5 s after the gate opened");
            Check(label, wrong.Count == 0, wrong.Count == 0 ? "held; 0 finalized while closed; finalized after" : string.Join("; ", wrong));
        }

        // =================================================================
        // THE WIRING
        // =================================================================

        private const string Wanted = "bool gate = FinalizerGate.Wanted;";
        private const string Closed = "if (gate && !FinalizerGate.Close(GateWaitMs))";
        private const string Served = "var response = Serve(method, parameters, tools, session, catalog, id);";
        private const string Opened = "finally { if (gate) FinalizerGate.Open(); }";
        private const string Released = "ReleaseSolidWorksObjects();";
        private const string Waits = "WaitForPendingFinalizers";

        internal static List<string> WiringProblems(string program)
        {
            var wrong = new List<string>();
            if (program == null) { wrong.Add("Program.cs not found"); return wrong; }
            int wanted = program.IndexOf(Wanted, StringComparison.Ordinal);
            int closed = program.IndexOf(Closed, StringComparison.Ordinal);
            int served = program.IndexOf(Served, StringComparison.Ordinal);
            int opened = program.IndexOf(Opened, StringComparison.Ordinal);
            if (wanted < 0 || closed < 0)
            {
                wrong.Add("the gate is never closed: SOLIDWORKS objects are released while a call runs, and SOLIDWORKS falls painting a drawing");
                return wrong;
            }
            if (opened < 0)
            {
                wrong.Add("the gate is never opened: the finalizer thread stays held, and the collection after the call waits for ever");
                return wrong;
            }
            if (served < 0 || closed > served) wrong.Add("the gate is closed after Serve: the call runs with the gate open");
            if (served >= 0 && opened < served) wrong.Add("the gate opens before Serve: the call runs with the gate open");
            if (opened > closed)
            {
                var inside = program.Substring(closed, opened - closed);
                if (inside.Contains(Released) || inside.Contains(Waits))
                    wrong.Add("a collection waits for the finalizers while the gate is closed: it waits for ever");
            }
            if (program.IndexOf(Released, opened, StringComparison.Ordinal) < 0)
                wrong.Add("no collection after the gate opens: what died during the call is never released");
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: the gate closed before Serve, opened after it, no collection inside it, the collection after it";
            var path = Path.Combine(Root(), "Program.cs");
            var program = File.Exists(path) ? File.ReadAllText(path) : null;
            var real = WiringProblems(program);

            // Teeth: each change below must turn the check red.
            var blind = new List<string>();
            int copies = 0;
            Action<string, Func<string, string>> mutate = (what, change) =>
            {
                if (program == null) return;
                var changed = change(program);
                if (changed == program) { blind.Add(what + " (the copy did not change)"); return; }
                copies++;
                if (WiringProblems(changed).Count == 0) blind.Add(what);
            };
            mutate("the gate never closed", t => t.Replace(Closed, "if (false)"));
            mutate("the gate never opened", t => t.Replace(Opened, "finally { }"));
            mutate("the gate closed after Serve", t => t.Replace(Closed, "if (false)").Replace(Served, Served + "\n" + Closed + " { }"));
            mutate("the gate opened before Serve", t => t.Replace(Opened, "").Replace(Served, Opened + "\n" + Served));
            mutate("a collection inside the closed gate", t => t.Replace(Served, Served + "\n" + Released));
            mutate("no collection after the gate opens", t => t.Replace(Opened, "") + "\n" + Opened);

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : "closed before Serve, opened after, nothing collected inside; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
