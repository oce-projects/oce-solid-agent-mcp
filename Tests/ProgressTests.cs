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
            Group("l'avanzamento e l'annullamento (W2.2)");

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
                if (BridgeOrNotHere("l'avanzamento: il gettone, la notifica, il filo") == null) return;
                Check("la classe Progress c'e'", false, "il ponte c'e' ma la classe non si carica");
                return;
            }

            var method = typ.GetMethod("Token", Everywhere);
            if (method == null) { Check("Token() c'e'", false, ""); return; }

            Func<Dictionary<string, object>, object> read =
                p => method.Invoke(null, new object[] { p });

            Check("senza parametri non c'e' nessun gettone",
                  read(null) == null, "null non fa saltare niente");

            Check("senza `_meta` non c'e' nessun gettone",
                  read(new Dictionary<string, object> { ["name"] = "build" }) == null,
                  "chi non lo chiede non viene aggiornato");

            Check("con `_meta` ma senza progressToken, nemmeno",
                  read(new Dictionary<string, object>
                  {
                      ["_meta"] = new Dictionary<string, object> { ["altro"] = 1 },
                  }) == null,
                  "`_meta` porta anche altre cose");

            var withString = read(new Dictionary<string, object>
            {
                ["_meta"] = new Dictionary<string, object> { ["progressToken"] = "abc-1" },
            });
            Check("un gettone di testo si legge",
                  Convert.ToString(withString) == "abc-1", Convert.ToString(withString));

            // THE CASE THAT COUNTS. The specification also admits an INTEGER, and a
            // `42` that came back as `"42"` would no longer match
            // anything on the client's side -- which would have no way of saying
            // why. So it is not looked at: it is copied back.
            var withNumber = read(new Dictionary<string, object>
            {
                ["_meta"] = new Dictionary<string, object> { ["progressToken"] = 42 },
            });
            Check("un gettone numerico resta un numero, non diventa testo",
                  withNumber is int && (int)withNumber == 42,
                  withNumber == null ? "(niente)" : withNumber.GetType().Name + " " + withNumber);

            Check("un gettone vuoto vale come niente",
                  read(new Dictionary<string, object>
                  {
                      ["_meta"] = new Dictionary<string, object> { ["progressToken"] = "" },
                  }) == null,
                  "e' quasi sempre un campo riempito male");
        }

        // ─────────────────────────────────────────────────────────────────
        //  2. THE SHAPE OF THE NOTIFICATION
        // ─────────────────────────────────────────────────────────────────

        private static void TheNotification()
        {
            var typ = Progress();
            if (typ == null) return;
            var method = typ.GetMethod("Notify", Everywhere);
            if (method == null) { Check("Notify() c'e'", false, ""); return; }

            Func<object, double, double, string, Dictionary<string, object>> doIt =
                (g, f, t, m) => (Dictionary<string, object>)
                    method.Invoke(null, new object[] { g, f, t, m });

            var n = doIt("g1", 2, 5, "eseguo il passo 2 di 5");
            var p = n != null ? n["params"] as Dictionary<string, object> : null;

            Check("la notifica e' `notifications/progress` e non ha id",
                  n != null && Convert.ToString(n["method"]) == "notifications/progress" &&
                  !n.ContainsKey("id"),
                  "una notifica con id sarebbe una richiesta, e il client aspetterebbe");
            Check("porta gettone, progress e total",
                  p != null && Convert.ToString(p["progressToken"]) == "g1" &&
                  Convert.ToDouble(p["progress"]) == 2 &&
                  Convert.ToDouble(p["total"]) == 5,
                  "g1, 2 su 5");
            Check("e il messaggio, che e' quello che una persona legge",
                  p != null && Convert.ToString(p["message"]).Contains("passo 2"),
                  Convert.ToString(p["message"]));

            var withoutTotal = doIt("g1", 1, 0, "non so quanti saranno");
            var pt = withoutTotal["params"] as Dictionary<string, object>;
            Check("senza totale, `total` non si scrive affatto",
                  !pt.ContainsKey("total"),
                  "un totale inventato darebbe a chi legge una barra che mente");

            var withoutMessage = doIt("g1", 1, 3, "   ");
            var pm = withoutMessage["params"] as Dictionary<string, object>;
            Check("e un messaggio vuoto non si scrive",
                  !pm.ContainsKey("message"), "un campo vuoto e' rumore");

            var numeric = doIt(42, 1, 3, "x");
            var pn = numeric["params"] as Dictionary<string, object>;
            Check("un gettone numerico torna indietro numerico",
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
            if (wireT == null) { Check("la classe Wire c'e'", false, ""); return; }

            Func<object, object> newItem = g => Activator.CreateInstance(wireT, new object[] { g });
            Action<object, double, string> planStep = (f, n, m) =>
                wireT.GetMethod("Step", Everywhere).Invoke(f, new object[] { n, m });
            Func<object, string, object> read = (f, itemName) =>
                ReflectionLog.Global.Property(wireT, itemName, Everywhere).GetValue(f);
            Action<object, double> total = (f, t) =>
                wireT.GetProperty("Total", Everywhere).SetValue(f, t);

            // ── OFF: no token, no notification ───────────────────────────
            var off = newItem(null);
            Check("senza gettone il filo e' spento", !(bool)read(off, "On"), "");
            total(off, 3);
            planStep(off, 1, "uno");
            planStep(off, 2, "due");
            Check("e spento non manda niente",
                  Convert.ToInt32(read(off, "Mandate")) == 0,
                  "chi non ha chiesto di essere aggiornato non viene disturbato");

            // ── ON ───────────────────────────────────────────────────────
            var isOn = newItem("g");
            total(isOn, 4);
            planStep(isOn, 1, "uno");
            planStep(isOn, 2, "due");
            Check("con un gettone manda un passo per volta",
                  Convert.ToInt32(read(isOn, "Mandate")) == 2,
                  "2 notifiche");
            Check("e si ricorda a che punto e' arrivato",
                  Convert.ToDouble(read(isOn, "Last")) == 2,
                  "serve a chi riprende il conto: l'esecuzione dopo il controllo");

            // ── THE RULE OF THE SPECIFICATION, MADE TO FAIL ON PURPOSE ────
            planStep(isOn, 2, "di nuovo due");
            planStep(isOn, 1, "indietro a uno");
            Check("un passo che NON cresce viene scartato",
                  Convert.ToInt32(read(isOn, "Mandate")) == 2 &&
                  Convert.ToInt32(read(isOn, "Discarded")) == 2,
                  "la specifica pretende che `progress` cresca, e qui non puo' non crescere");
            planStep(isOn, 3, "tre");
            Check("e dopo uno scartato il successivo passa lo stesso",
                  Convert.ToInt32(read(isOn, "Mandate")) == 3,
                  "scartare non blocca il filo, scarta quel passo");
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
            { Check("il filo accetta un orologio", false,
                    "senza, il tempo scritto non si puo' guardare"); return; }

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
            { Check("ColTempo() c'e'", false, ""); return; }
            Func<string, string> write =
                m => (string)colTempo.Invoke(wire, new object[] { m });

            Check("appena partito il tempo e' zero secondi",
                  write("eseguo il passo 1 di 3") == "eseguo il passo 1 di 3 \u2014 0s",
                  write("eseguo il passo 1 di 3"));

            now = now.AddSeconds(47);
            Check("sotto il minuto si scrivono i secondi",
                  write("passo 2") == "passo 2 \u2014 47s", write("passo 2"));

            now = now.AddSeconds(87);   // 2m14s in all
            Check("sopra il minuto si scrive minuti e secondi",
                  write("passo 3") == "passo 3 \u2014 2m14s", write("passo 3"));

            // THIS IS THE CASE OF S0: eleven minutes on the same step.
            // Without the time, «passo 3 di 5» is identical to two seconds ago, and
            // whoever looks does not know whether to wait or to kill the process.
            now = now.AddMinutes(9);
            Check("e a undici minuti si vede che e' tanto",
                  write("passo 3") == "passo 3 \u2014 11m14s", write("passo 3"));

            Check("senza messaggio resta solo il tempo",
                  write(null) == "11m14s", write(null));

            Check("e `Elapsed` dice la stessa cosa in numeri",
                  Math.Abs(((TimeSpan)wireT.GetProperty("Elapsed", Everywhere)
                      .GetValue(wire)).TotalSeconds - 674) < 0.001,
                  "674 secondi");

            // And the time REALLY ends up in the notification, not only in
            // `ColTempo`: the difference between the two things is a correct method
            // that nobody calls.
            wireT.GetProperty("Total", Everywhere).SetValue(wire, 3.0);
            planStep.Invoke(wire, new object[] { 1.0, "passo 1" });
            var seen = (Dictionary<string, object>)notify.Invoke(null,
                new object[] { "g", 1.0, 3.0, write("passo 1") });
            var par = seen["params"] as Dictionary<string, object>;
            Check("e il messaggio della notifica porta il tempo",
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
            { Check("PlanInMemory c'e'", false, ""); return; }

            var constructor = planeT.GetConstructors(Everywhere)
                .FirstOrDefault(c => c.GetParameters().Length == 2);
            if (constructor == null)
            { Check("PlanInMemory accetta un filo", false, "nessun costruttore a due"); return; }

            // `PlanStep` LIVES IN THE LIBRARY, NOT IN THE BRIDGE, and looking for it
            // among the types of the bridge could not work -- I tried, and
            // it went red at once. It is taken from the constructor that uses it:
            // `List<PlanStep>`. This way there is no assembly to
            // guess, and if one day the type changed the check would follow
            // instead of breaking.
            var stepT = constructor.GetParameters()[0]
                .ParameterType.GetGenericArguments().FirstOrDefault();
            if (stepT == null)
            { Check("il primo parametro e' una lista di passi", false,
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
            wireT.GetMethod("Step", Everywhere).Invoke(wire, new object[] { 3.0, "controllo finito" });

            var plane = constructor.Invoke(new[] { itemList, wire });
            var next = planeT.GetMethod("NextAsync", Everywhere);

            var seen = new List<double>();
            for (int i = 0; i < 4; i++)   // three steps and then the end
            {
                next.Invoke(plane, new object[] { null });
                seen.Add(Convert.ToDouble(wireT.GetProperty("Last", Everywhere).GetValue(wire)));
            }

            Check("l'esecuzione manda un avanzamento per passo",
                  Convert.ToInt32(wireT.GetProperty("Mandate", Everywhere).GetValue(wire)) == 4,
                  "1 dal controllo + 3 dall'esecuzione");
            Check("e riprende il conto da dove il controllo l'ha lasciato",
                  seen[0] == 4 && seen[1] == 5 && seen[2] == 6,
                  "4, 5, 6 su 6 — non riparte da 1");
            Check("finiti i passi non manda piu' niente",
                  seen[3] == 6,
                  "la quarta chiamata e' «fine», e non inventa un settimo passo");
        }

        // ─────────────────────────────────────────────────────────────────
        //  5. CANCELLATION: a no that is said, not a silence
        // ─────────────────────────────────────────────────────────────────

        private static void TheUndoSaysNo()
        {
            var bridge = BridgeOrNotHere("l'annullamento dice di no");
            if (bridge == null) return;
            Type prog;
            try { prog = Load.Class(Load.Bridge(bridge), "Program"); }
            catch { prog = null; }
            if (prog == null) { Check("la classe Program c'e'", false, ""); return; }

            var method = prog.GetMethod("UndoNotPossible", Everywhere);
            if (method == null)
            { Check("UndoNotPossible() c'e'", false, ""); return; }

            Func<Dictionary<string, object>, string> sentence =
                p => (string)method.Invoke(null, new object[] { p });

            var full = sentence(new Dictionary<string, object>
            {
                ["requestId"] = 7,
                ["reason"] = "l'utente ha cambiato idea",
            });
            Check("la risposta all'annullamento nomina la richiesta",
                  full.Contains("7"), Short(full));
            Check("e riporta il motivo che il client ha dato",
                  full.Contains("cambiato idea"),
                  "chi legge il log deve poter capire di cosa si parlava");
            Check("e dice CHIARAMENTE che non l'ha interrotta",
                  full.Contains("NON l'ho interrotta"),
                  "prima cadeva nel `default` e, essendo una notifica, " +
                  "l'errore veniva scartato: un no-op silenzioso");
            Check("e dice perche', che e' cio' che rende il no utile",
                  full.Contains("una richiesta alla volta"),
                  "un no senza motivo si legge come un difetto");

            var empty = sentence(null);
            Check("e senza parametri non salta, lo dice e basta",
                  !string.IsNullOrEmpty(empty) && empty.Contains("senza requestId"),
                  Short(empty));
        }

        // ─────────────────────────────────────────────────────────────────
        //  6. ON THE REAL BRIDGE, OVER STDIO, WITHOUT SOLIDWORKS
        // ─────────────────────────────────────────────────────────────────

        private static void OnTheRealBridge()
        {
            var executable = BridgeOrNotHere("l'avanzamento sul ponte vero");
            if (executable == null) return;

            var p = Open(executable);
            if (p == null) { Check("il ponte parte", false, "non si e' avviato"); return; }
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
                Check("con un progressToken arrivano notifiche di avanzamento",
                      withToken.Count >= 3,
                      withToken.Count + " `notifications/progress` per un piano di 3 passi");

                Check("e portano il gettone che il client ha mandato",
                      withToken.Count > 0 &&
                      withToken.All(n => Convert.ToString(Inside(n, "progressToken")) == "pt-77"),
                      "pt-77 su tutte");

                var numbers = withToken
                    .Select(n => Convert.ToDouble(Inside(n, "progress"))).ToList();
                Check("e `progress` cresce sempre, come la specifica pretende",
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
                Check("e chi NON manda un gettone non riceve niente",
                      without.Count == 0,
                      without.Count == 0
                          ? "zero notifiche, come deve essere"
                          : without.Count + " notifiche non richieste");

                // ── CANCELLATION IS NO LONGER A SILENCE ─────────────────────
                int beforeAnn = p.Views.Count;
                Write(p,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\"," +
                    "\"params\":{\"requestId\":30,\"reason\":\"basta cosi'\"}}");
                // We wait for the bridge to say something, and then we look at what.
                Line(p, 3000);
                var said = p.Views.Skip(beforeAnn)
                    .Where(r => r != null && r.Contains("annullamento chiesto")).ToList();
                Check("una `notifications/cancelled` NON cade nel silenzio",
                      said.Count > 0,
                      said.Count > 0 ? Short(said[0])
                                      : "niente: e' il no-op silenzioso che la voce descriveva");

                // And above all: the bridge is still alive. An unhandled
                // notification that made the run die would be worse than
                // silence.
                var after = Obj(Ask(p, 32,
                    "{\"jsonrpc\":\"2.0\",\"id\":32,\"method\":\"ping\",\"params\":{}}"));
                Check("e il ponte risponde ancora dopo averla ricevuta",
                      after != null,
                      "una notifica sconosciuta non deve fermare il giro");
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
            Check("il passo finto si puo' riempire: " + itemName, false,
                  typ.Name + " non ha ne' la proprieta' ne' il campo");
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
