using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;
using static Shared.McpClient;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// PROGRESS AND CANCELLATION, WITH THE MECHANISM OF THE PROTOCOL (entry W2.2)
    ///
    /// ── WHY IT CAN BE MEASURED WITHOUT SOLIDWORKS ─────────────────────────
    ///
    /// Because `notifications/progress` is protocol, not CAD. And because the
    /// bridge already has the **dry run** (`dry_run: true`, entry Z-G6): it checks
    /// a plan step by step with SOLIDWORKS closed. That run sends its
    /// notifications like any other, so the whole chain — token that
    /// comes in, notifications that go out, token that comes back identical — is looked at
    /// end-to-end over stdio with the real bridge.
    ///
    /// What is NOT measured here is the progress during the real execution,
    /// which needs an open document. That half is sent by `PlanInMemory`,
    /// and since it is a class of the bridge and not of the library **it is driven
    /// directly**: it is asked for the next step and we look at what
    /// went out. It is not the real run, but it is not a reading of the code either.
    ///
    /// ── THE RULE THAT IS MADE TO FAIL ON PURPOSE ──────────────────────────
    ///
    /// The specification demands that `progress` **grow**. A rule like that,
    /// left to the caller, is found broken inside a client — that is, never.
    /// Here a value that does not grow is passed to the thread and it is demanded that it
    /// reject it.
    /// </summary>
    internal static class ProgressTests
    {
        internal static void All()
        {
            Group("progress and cancellation (W2.2)");

            TheToken();
            TheNotification();
            TheWire();
            TheTimeAlongside();
            ThePlanInMemory();
            TheUndoSaysNo();
            OnTheRealBridge();
        }

        // ─────────────────────────────────────────────────────────────────
        //  common tools
        // ─────────────────────────────────────────────────────────────────

        private static Type _progress;
        private static bool _searched;

        private static Type Progress()
        {
            if (_searched) return _progress;
            _searched = true;
            try
            {
                var bridge = System.IO.Path.Combine(Root(), "bin", "oce-solid-agent-mcp.exe");
                if (!System.IO.File.Exists(bridge)) return null;
                _progress = Load.Class(Load.Bridge(bridge), "Progress");
            }
            catch { _progress = null; }
            return _progress;
        }

        private const BindingFlags Everywhere =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance;

        // ─────────────────────────────────────────────────────────────────
        //  1. THE TOKEN: where it is, and that it is sent back as it arrived
        // ─────────────────────────────────────────────────────────────────

        private static void TheToken()
        {
            var typ = Progress();
            if (typ == null)
            {
                // Without the bridge: «not here» in CI, red where SOLIDWORKS is there (and
                // then build.ps1 is missing). With the bridge and without the class: red.
                if (BridgeOrNotHere("progress: the token, the notification, the thread") == null) return;
                Check("the Progress class is there", false, "the bridge is there but the class does not load");
                return;
            }

            var method = typ.GetMethod("Token", Everywhere);
            if (method == null) { Check("Token() is there", false, ""); return; }

            Func<Dictionary<string, object>, object> read =
                p => method.Invoke(null, new object[] { p });

            Check("without parameters there is no token",
                  read(null) == null, "null does not blow anything up");

            Check("without `_meta` there is no token",
                  read(new Dictionary<string, object> { ["name"] = "build" }) == null,
                  "whoever does not ask for it is not updated");

            Check("with `_meta` but without progressToken, neither",
                  read(new Dictionary<string, object>
                  {
                      ["_meta"] = new Dictionary<string, object> { ["altro"] = 1 },
                  }) == null,
                  "`_meta` also carries other things");

            var withString = read(new Dictionary<string, object>
            {
                ["_meta"] = new Dictionary<string, object> { ["progressToken"] = "abc-1" },
            });
            Check("a text token can be read",
                  Convert.ToString(withString) == "abc-1", Convert.ToString(withString));

            // THE CASE THAT COUNTS. The specification also admits an INTEGER, and a
            // `42` that came back as `"42"` would no longer match
            // anything on the client's side -- which would have no way of saying
            // why. So it is not looked at: it is copied back.
            var withNumber = read(new Dictionary<string, object>
            {
                ["_meta"] = new Dictionary<string, object> { ["progressToken"] = 42 },
            });
            Check("a numeric token stays a number, it does not become text",
                  withNumber is int && (int)withNumber == 42,
                  withNumber == null ? "(nothing)" : withNumber.GetType().Name + " " + withNumber);

            Check("an empty token counts as nothing",
                  read(new Dictionary<string, object>
                  {
                      ["_meta"] = new Dictionary<string, object> { ["progressToken"] = "" },
                  }) == null,
                  "it is almost always a badly filled field");
        }

        // ─────────────────────────────────────────────────────────────────
        //  2. THE SHAPE OF THE NOTIFICATION
        // ─────────────────────────────────────────────────────────────────

        private static void TheNotification()
        {
            var typ = Progress();
            if (typ == null) return;
            var method = typ.GetMethod("Notify", Everywhere);
            if (method == null) { Check("Notify() is there", false, ""); return; }

            Func<object, double, double, string, Dictionary<string, object>> doIt =
                (g, f, t, m) => (Dictionary<string, object>)
                    method.Invoke(null, new object[] { g, f, t, m });

            var n = doIt("g1", 2, 5, "running step 2 of 5");
            var p = n != null ? n["params"] as Dictionary<string, object> : null;

            Check("the notification is `notifications/progress` and has no id",
                  n != null && Convert.ToString(n["method"]) == "notifications/progress" &&
                  !n.ContainsKey("id"),
                  "a notification with an id would be a request, and the client would wait");
            Check("it carries token, progress and total",
                  p != null && Convert.ToString(p["progressToken"]) == "g1" &&
                  Convert.ToDouble(p["progress"]) == 2 &&
                  Convert.ToDouble(p["total"]) == 5,
                  "g1, 2 su 5");
            Check("and the message, which is what a person reads",
                  p != null && Convert.ToString(p["message"]).Contains("step 2"),
                  Convert.ToString(p["message"]));

            var withoutTotal = doIt("g1", 1, 0, "I do not know how many there will be");
            var pt = withoutTotal["params"] as Dictionary<string, object>;
            Check("without a total, `total` is not written at all",
                  !pt.ContainsKey("total"),
                  "an invented total would give the reader a bar that lies");

            var withoutMessage = doIt("g1", 1, 3, "   ");
            var pm = withoutMessage["params"] as Dictionary<string, object>;
            Check("and an empty message is not written",
                  !pm.ContainsKey("message"), "an empty field is noise");

            var numeric = doIt(42, 1, 3, "x");
            var pn = numeric["params"] as Dictionary<string, object>;
            Check("a numeric token comes back numeric",
                  pn["progressToken"] is int && (int)pn["progressToken"] == 42,
                  pn["progressToken"].GetType().Name);
        }

        // ─────────────────────────────────────────────────────────────────
        //  3. THE THREAD: off if nobody asked for it, and `progress` grows
        // ─────────────────────────────────────────────────────────────────

        private static void TheWire()
        {
            var typ = Progress();
            if (typ == null) return;
            var wireT = typ.GetNestedType("Wire", BindingFlags.Public | BindingFlags.NonPublic);
            if (wireT == null) { Check("the Wire class is there", false, ""); return; }

            Func<object, object> newItem = g => Activator.CreateInstance(wireT, new object[] { g });
            Action<object, double, string> planStep = (f, n, m) =>
                wireT.GetMethod("Step", Everywhere).Invoke(f, new object[] { n, m });
            Func<object, string, object> read = (f, itemName) =>
                ReflectionLog.Global.Property(wireT, itemName, Everywhere).GetValue(f);
            Action<object, double> total = (f, t) =>
                wireT.GetProperty("Total", Everywhere).SetValue(f, t);

            // ── OFF: no token, no notification ───────────────────────────
            var off = newItem(null);
            Check("without a token the thread is off", !(bool)read(off, "On"), "");
            total(off, 3);
            planStep(off, 1, "uno");
            planStep(off, 2, "due");
            Check("and off it sends nothing",
                  Convert.ToInt32(read(off, "Mandate")) == 0,
                  "whoever did not ask to be updated is not disturbed");

            // ── ON ───────────────────────────────────────────────────────
            var isOn = newItem("g");
            total(isOn, 4);
            planStep(isOn, 1, "uno");
            planStep(isOn, 2, "due");
            Check("with a token it sends one step at a time",
                  Convert.ToInt32(read(isOn, "Mandate")) == 2,
                  "2 notifiche");
            Check("and it remembers how far it got",
                  Convert.ToDouble(read(isOn, "Last")) == 2,
                  "it is for whoever resumes the count: the execution after the check");

            // ── THE RULE OF THE SPECIFICATION, MADE TO FAIL ON PURPOSE ────
            planStep(isOn, 2, "two again");
            planStep(isOn, 1, "back to one");
            Check("a step that does NOT grow is discarded",
                  Convert.ToInt32(read(isOn, "Mandate")) == 2 &&
                  Convert.ToInt32(read(isOn, "Discarded")) == 2,
                  "the specification demands that `progress` grows, and here it has to grow");
            planStep(isOn, 3, "tre");
            Check("and after a discarded one the next one goes through anyway",
                  Convert.ToInt32(read(isOn, "Mandate")) == 3,
                  "discarding does not block the thread, it discards that step");
        }

        // ─────────────────────────────────────────────────────────────────
        //  3-bis. THE TIME NEXT TO THE STEP (entry S0)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// S0 does not ask for «a progress report»: it asks that whoever looks from outside
        /// tell **«it is working» from «it is stuck» without looking at the
        /// CPU**. The step number alone does not say it — «passo 3 di 5»
        /// is identical whether that step started two seconds ago or eleven
        /// minutes ago.
        ///
        /// The clock is passed to the thread, so here it is fixed and we look at
        /// **the exact string** instead of hoping it looks similar.
        /// </summary>
        private static void TheTimeAlongside()
        {
            var typ = Progress();
            if (typ == null) return;
            var wireT = typ.GetNestedType("Wire",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (wireT == null) return;

            var withClock = wireT.GetConstructors(Everywhere)
                .FirstOrDefault(c => c.GetParameters().Length == 2);
            if (withClock == null)
            { Check("the thread accepts a clock", false,
                    "without one, the written time cannot be looked at"); return; }

            // The fake clock: it starts stopped, and is moved by hand.
            var now = new DateTime(2026, 9, 24, 10, 0, 0);
            Func<DateTime> clock = () => now;

            var wire = withClock.Invoke(new object[] { "g", clock });
            wireT.GetProperty("Total", Everywhere).SetValue(wire, 3.0);
            var notify = typ.GetMethod("Notify", Everywhere);
            var planStep = wireT.GetMethod("Step", Everywhere);

            // To read what it WRITES without an open channel, do we look at
            // `Elapsed` and rebuild the string? No: we would be looking at
            // a copy of the rule instead of the rule. `ColTempo` is
            // called directly.
            var colTempo = wireT.GetMethod("ColTempo", Everywhere);
            if (colTempo == null)
            { Check("ColTempo() is there", false, ""); return; }
            Func<string, string> write =
                m => (string)colTempo.Invoke(wire, new object[] { m });

            Check("right after the start the time is zero seconds",
                  write("running step 1 of 3") == "running step 1 of 3 \u2014 0s",
                  write("running step 1 of 3"));

            now = now.AddSeconds(47);
            Check("under a minute the seconds are written",
                  write("step 2") == "step 2 \u2014 47s", write("step 2"));

            now = now.AddSeconds(87);   // 2m14s in all
            Check("over a minute minutes and seconds are written",
                  write("step 3") == "step 3 \u2014 2m14s", write("step 3"));

            // THIS IS THE CASE OF S0: eleven minutes on the same step.
            // Without the time, «passo 3 di 5» is identical to two seconds ago, and
            // whoever looks does not know whether to wait or to kill the process.
            now = now.AddMinutes(9);
            Check("and at eleven minutes you can see it is a lot",
                  write("step 3") == "step 3 \u2014 11m14s", write("step 3"));

            Check("without a message only the time is left",
                  write(null) == "11m14s", write(null));

            Check("and `Elapsed` says the same thing in numbers",
                  Math.Abs(((TimeSpan)wireT.GetProperty("Elapsed", Everywhere)
                      .GetValue(wire)).TotalSeconds - 674) < 0.001,
                  "674 seconds");

            // And the time REALLY ends up in the notification, not only in
            // `ColTempo`: the difference between the two things is a correct method
            // that nobody calls.
            wireT.GetProperty("Total", Everywhere).SetValue(wire, 3.0);
            planStep.Invoke(wire, new object[] { 1.0, "step 1" });
            var seen = (Dictionary<string, object>)notify.Invoke(null,
                new object[] { "g", 1.0, 3.0, write("step 1") });
            var par = seen["params"] as Dictionary<string, object>;
            Check("and the message of the notification carries the time",
                  Convert.ToString(par["message"]).EndsWith("11m14s"),
                  Convert.ToString(par["message"]));
        }

        // ─────────────────────────────────────────────────────────────────
        //  4. PlanInMemory: the half that counts, driven by hand
        // ─────────────────────────────────────────────────────────────────

        private static void ThePlanInMemory()
        {
            var typ = Progress();
            if (typ == null) return;
            var asm = typ.Assembly;
            var wireT = typ.GetNestedType("Wire", BindingFlags.Public | BindingFlags.NonPublic);
            var planeT = Load.Class(asm, "PlanInMemory");
            if (planeT == null || wireT == null)
            { Check("PlanInMemory is there", false, ""); return; }

            var constructor = planeT.GetConstructors(Everywhere)
                .FirstOrDefault(c => c.GetParameters().Length == 2);
            if (constructor == null)
            { Check("PlanInMemory accepts a thread", false, "no constructor with two"); return; }

            // `PlanStep` LIVES IN THE LIBRARY, NOT IN THE BRIDGE, and looking for it
            // among the types of the bridge could not work -- I tried, and
            // it went red at once. It is taken from the constructor that uses it:
            // `List<PlanStep>`. This way there is no assembly to
            // guess, and if one day the type changed the check would follow
            // instead of breaking.
            var stepT = constructor.GetParameters()[0]
                .ParameterType.GetGenericArguments().FirstOrDefault();
            if (stepT == null)
            { Check("the first parameter is a list of steps", false,
                    constructor.GetParameters()[0].ParameterType.Name); return; }

            // Three fake steps. Nothing is executed: this class is asked for
            // the next step, which is what the executor does.
            var itemList = (System.Collections.IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(stepT));
            for (int i = 1; i <= 3; i++)
            {
                var pd = Activator.CreateInstance(stepT);
                Put(stepT, pd, "Order", i);
                Put(stepT, pd, "Operation", "op" + i);
                itemList.Add(pd);
            }

            var wire = Activator.CreateInstance(wireT, new object[] { "g" });
            wireT.GetProperty("Total", Everywhere).SetValue(wire, 6.0);
            // The check of the plan has already told three steps: the execution
            // must restart from four, not from one.
            wireT.GetMethod("Step", Everywhere).Invoke(wire, new object[] { 3.0, "check finished" });

            var plane = constructor.Invoke(new[] { itemList, wire });
            var next = planeT.GetMethod("NextAsync", Everywhere);

            var seen = new List<double>();
            for (int i = 0; i < 4; i++)   // three steps and then the end
            {
                next.Invoke(plane, new object[] { null });
                seen.Add(Convert.ToDouble(wireT.GetProperty("Last", Everywhere).GetValue(wire)));
            }

            Check("the execution sends one progress per step",
                  Convert.ToInt32(wireT.GetProperty("Mandate", Everywhere).GetValue(wire)) == 4,
                  "1 from the check + 3 from the execution");
            Check("and it resumes the count from where the check left it",
                  seen[0] == 4 && seen[1] == 5 && seen[2] == 6,
                  "4, 5, 6 of 6 — it does not restart from 1");
            Check("once the steps are over it sends nothing more",
                  seen[3] == 6,
                  "the fourth call is «end», and it does not invent a seventh step");
        }

        // ─────────────────────────────────────────────────────────────────
        //  5. CANCELLATION: a no that is said, not a silence
        // ─────────────────────────────────────────────────────────────────

        private static void TheUndoSaysNo()
        {
            var bridge = BridgeOrNotHere("the cancellation says no");
            if (bridge == null) return;
            Type prog;
            try { prog = Load.Class(Load.Bridge(bridge), "Program"); }
            catch { prog = null; }
            if (prog == null) { Check("the Program class is there", false, ""); return; }

            var method = prog.GetMethod("UndoNotPossible", Everywhere);
            if (method == null)
            { Check("UndoNotPossible() is there", false, ""); return; }

            Func<Dictionary<string, object>, string> sentence =
                p => (string)method.Invoke(null, new object[] { p });

            var full = sentence(new Dictionary<string, object>
            {
                ["requestId"] = 7,
                ["reason"] = "the user changed their mind",
            });
            Check("the answer to the cancellation names the request",
                  full.Contains("7"), Short(full));
            Check("and reports the reason that the client gave",
                  full.Contains("changed their mind"),
                  "whoever reads the log must be able to understand what it was about");
            Check("and says CLEARLY that it did not interrupt it",
                  full.Contains("I did NOT interrupt it"),
                  "it used to fall into the `default` and, being a notification, " +
                  "the error was discarded: a silent no-op");
            Check("and says why, which is what makes the no useful",
                  full.Contains("one request at a time"),
                  "a no without a reason reads like a defect");

            var empty = sentence(null);
            Check("and without parameters it does not blow up, it just says so",
                  !string.IsNullOrEmpty(empty) && empty.Contains("no requestId"),
                  Short(empty));
        }

        // ─────────────────────────────────────────────────────────────────
        //  6. ON THE REAL BRIDGE, OVER STDIO, WITHOUT SOLIDWORKS
        // ─────────────────────────────────────────────────────────────────

        private static void OnTheRealBridge()
        {
            var executable = BridgeOrNotHere("progress on the real bridge");
            if (executable == null) return;

            var p = Open(executable);
            if (p == null) { Check("the bridge starts", false, "it did not start"); return; }
            try
            {
                Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"prove\",\"version\":\"1\"}}}");

                // ── WITH THE TOKEN ─────────────────────────────────────────
                //
                // `dry_run: true` checks the plan step by step and touches
                // nothing: it runs with SOLIDWORKS closed, and it is the only run of the
                // bridge that can be looked at in full on this machine.
                const string three =
                    "{\"operation\":\"extrude_rectangle\",\"arguments\":" +
                    "{\"length_mm\":100,\"width_mm\":50,\"thickness_mm\":5}}," +
                    "{\"operation\":\"extrude_circle\",\"arguments\":" +
                    "{\"diameter_mm\":40,\"height_mm\":10}}," +
                    "{\"operation\":\"shell\",\"arguments\":{\"thickness_mm\":3}}";

                int beforeWith = p.Views.Count;
                Ask(p, 30,
                    "{\"jsonrpc\":\"2.0\",\"id\":30,\"method\":\"tools/call\",\"params\":" +
                    "{\"_meta\":{\"progressToken\":\"pt-77\"}," +
                    "\"name\":\"build\",\"arguments\":{\"dry_run\":true,\"steps\":[" + three + "]}}}");

                var withToken = Progresses(p, beforeWith);
                Check("with a progressToken progress notifications arrive",
                      withToken.Count >= 3,
                      withToken.Count + " `notifications/progress` for a plan of 3 steps");

                Check("and they carry the token that the client sent",
                      withToken.Count > 0 &&
                      withToken.All(n => Convert.ToString(Inside(n, "progressToken")) == "pt-77"),
                      "pt-77 on all of them");

                var numbers = withToken
                    .Select(n => Convert.ToDouble(Inside(n, "progress"))).ToList();
                Check("and `progress` always grows, as the specification demands",
                      numbers.Count > 1 &&
                      Enumerable.Range(1, numbers.Count - 1).All(i => numbers[i] > numbers[i - 1]),
                      string.Join(", ", numbers));

                // ── WITHOUT THE TOKEN: SILENCE ─────────────────────────────
                //
                // It is the half that gets forgotten. Sending progress reports to whoever did not
                // ask for them is noise on the protocol channel, and a
                // client that does not expect them may very well not know how to
                // read them.
                int beforeWithout = p.Views.Count;
                Ask(p, 31,
                    "{\"jsonrpc\":\"2.0\",\"id\":31,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"build\",\"arguments\":{\"dry_run\":true,\"steps\":[" + three + "]}}}");

                var without = Progresses(p, beforeWithout);
                Check("and whoever does NOT send a token receives nothing",
                      without.Count == 0,
                      without.Count == 0
                          ? "zero notifications, as it must be"
                          : without.Count + " unrequested notifications");

                // ── CANCELLATION IS NO LONGER A SILENCE ─────────────────────
                int beforeAnn = p.Views.Count;
                Write(p,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\"," +
                    "\"params\":{\"requestId\":30,\"reason\":\"basta cosi'\"}}");
                // We wait for the bridge to say something, and then we look at what.
                Line(p, 3000);
                var said = p.Views.Skip(beforeAnn)
                    .Where(r => r != null && r.Contains("cancellation requested")).ToList();
                Check("a `notifications/cancelled` does NOT fall into silence",
                      said.Count > 0,
                      said.Count > 0 ? Short(said[0])
                                      : "nothing: it is the silent no-op that the entry described");

                // And above all: the bridge is still alive. An unhandled
                // notification that made the run die would be worse than
                // silence.
                var after = Obj(Ask(p, 32,
                    "{\"jsonrpc\":\"2.0\",\"id\":32,\"method\":\"ping\",\"params\":{}}"));
                Check("and the bridge still answers after receiving it",
                      after != null,
                      "an unknown notification must not stop the run");
            }
            finally { Close(p); p.Dispose(); }
        }

        /// <summary>
        /// The `notifications/progress` that arrived after the mark `da`.
        ///
        /// It is filtered by method and not by content: on the channel there also pass
        /// the `notifications/message` of the bridge, and counting them would be a green
        /// for the wrong reason.
        /// </summary>
        /// <summary>
        /// Sets a value, whether it is a property or a field.
        ///
        /// Trying both instead of guessing: a `SetValue` on a
        /// property that does not exist does nothing and does not say so, and the step
        /// would stay without a name — with the check green for the wrong reason.
        /// Here, if it could not be written, it is marked red.
        /// </summary>
        private static void Put(Type typ, object obj, string itemName, object val)
        {
            var prop = typ.GetProperty(itemName, Everywhere);
            if (prop != null && prop.CanWrite) { ReflectionLog.Global.Found(); prop.SetValue(obj, val); return; }
            var theField = typ.GetField(itemName, Everywhere);
            if (theField != null) { ReflectionLog.Global.Found(); theField.SetValue(obj, val); return; }
            ReflectionLog.Global.Missing("writable property or field", typ, itemName);
            Check("the fake step can be filled: " + itemName, false,
                  typ.Name + " has neither the property nor the field");
        }

        private static List<Dictionary<string, object>> Progresses(Bridge p, int da)
        {
            var outside = new List<Dictionary<string, object>>();
            foreach (var line in p.Views.Skip(da))
            {
                if (line == null || line.IndexOf("notifications/progress",
                                                 StringComparison.Ordinal) < 0) continue;
                var o = Obj(line);
                if (o == null) continue;
                if (Convert.ToString(Inside(o, "method")) != "notifications/progress") continue;
                var par = Inside(o, "params") as Dictionary<string, object>;
                if (par != null) outside.Add(par);
            }
            return outside;
        }
    }
}
