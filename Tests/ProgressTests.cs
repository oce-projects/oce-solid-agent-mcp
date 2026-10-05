using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;
using static Shared.McpClient;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// L'AVANZAMENTO E L'ANNULLAMENTO, COL MECCANISMO DEL PROTOCOLLO (voce W2.2)
    ///
    /// ── PERCHE' SI PUO' MISURARE SENZA SOLIDWORKS ───────────────────────
    ///
    /// Perche' `notifications/progress` e' protocollo, non CAD. E perche' il
    /// ponte ha gia' la **prova a vuoto** (`dry_run: true`, voce Z-G6): controlla
    /// un piano passo per passo con SOLIDWORKS chiuso. Quel giro manda le sue
    /// notifiche come qualunque altro, quindi la catena intera — gettone che
    /// entra, notifiche che escono, gettone che torna identico — si guarda
    /// end-to-end su stdio col ponte vero.
    ///
    /// Cio' che NON si misura qui e' l'avanzamento durante l'esecuzione vera,
    /// che vuole un documento aperto. Quella meta' la manda `PlanInMemory`,
    /// e siccome e' una classe del ponte e non della libreria **la si pilota
    /// direttamente**: le si chiede il passo successivo e si guarda cosa e'
    /// partito. Non e' il giro vero, ma non e' nemmeno una lettura del codice.
    ///
    /// ── LA REGOLA CHE SI FA FALLIRE APPOSTA ─────────────────────────────
    ///
    /// La specifica pretende che `progress` **cresca**. Una regola del genere,
    /// lasciata a chi chiama, si scopre rotta dentro un client — cioe' mai.
    /// Qui si passa al filo un valore che non cresce e si pretende che lo
    /// scarti.
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
        //  gli attrezzi comuni
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
        //  1. IL GETTONE: dove sta, e che si rimanda com'e' arrivato
        // ─────────────────────────────────────────────────────────────────

        private static void TheToken()
        {
            var typ = Progress();
            if (typ == null)
            {
                // Senza ponte: «non qui» in CI, rosso dove SOLIDWORKS c'e' (e
                // allora manca build.ps1). Col ponte e senza la classe: rosso.
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

            // IL CASO CHE CONTA. La specifica ammette anche un INTERO, e un
            // `42` che tornasse indietro come `"42"` non combacerebbe piu' con
            // niente dalla parte del client -- che non avrebbe modo di dire
            // perche'. Quindi non si guarda: si ricopia.
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
        //  2. LA FORMA DELLA NOTIFICA
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
        //  3. IL FILO: spento se nessuno l'ha chiesto, e `progress` cresce
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

            // ── SPENTO: nessun gettone, nessuna notifica ──────────────────
            var off = newItem(null);
            Check("senza gettone il filo e' spento", !(bool)read(off, "On"), "");
            total(off, 3);
            planStep(off, 1, "uno");
            planStep(off, 2, "due");
            Check("e spento non manda niente",
                  Convert.ToInt32(read(off, "Mandate")) == 0,
                  "chi non ha chiesto di essere aggiornato non viene disturbato");

            // ── ACCESO ────────────────────────────────────────────────────
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

            // ── LA REGOLA DELLA SPECIFICA, FATTA FALLIRE APPOSTA ──────────
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
        //  3-bis. IL TEMPO ACCANTO AL PASSO (voce S0)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// S0 non chiede «un avanzamento»: chiede che chi guarda da fuori
        /// distingua **«sta lavorando» da «e' piantato» senza guardare la
        /// CPU**. Il numero del passo da solo non lo dice — «passo 3 di 5»
        /// e' identico se quel passo e' cominciato due secondi fa o undici
        /// minuti fa.
        ///
        /// L'orologio si passa al filo, quindi qui lo si fissa e si guarda
        /// **la stringa esatta** invece di sperare che assomigli.
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

            // L'orologio finto: parte fermo, e lo si sposta a mano.
            var now = new DateTime(2026, 9, 24, 10, 0, 0);
            Func<DateTime> clock = () => now;

            var wire = withClock.Invoke(new object[] { "g", clock });
            wireT.GetProperty("Total", Everywhere).SetValue(wire, 3.0);
            var notify = typ.GetMethod("Notify", Everywhere);
            var planStep = wireT.GetMethod("Step", Everywhere);

            // Per leggere cosa SCRIVE senza un canale aperto si guarda
            // `Elapsed` e si ricostruisce la stringa? No: si guarderebbe
            // una copia della regola invece della regola. Si chiama
            // `ColTempo` direttamente.
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

            now = now.AddSeconds(87);   // 2m14s in tutto
            Check("sopra il minuto si scrive minuti e secondi",
                  write("passo 3") == "passo 3 \u2014 2m14s", write("passo 3"));

            // QUESTO E' IL CASO DI S0: undici minuti sullo stesso passo.
            // Senza il tempo, «passo 3 di 5» e' identico a due secondi fa, e
            // chi guarda non sa se aspettare o uccidere il processo.
            now = now.AddMinutes(9);
            Check("e a undici minuti si vede che e' tanto",
                  write("passo 3") == "passo 3 \u2014 11m14s", write("passo 3"));

            Check("senza messaggio resta solo il tempo",
                  write(null) == "11m14s", write(null));

            Check("e `Elapsed` dice la stessa cosa in numeri",
                  Math.Abs(((TimeSpan)wireT.GetProperty("Elapsed", Everywhere)
                      .GetValue(wire)).TotalSeconds - 674) < 0.001,
                  "674 secondi");

            // E il tempo finisce DAVVERO nella notifica, non solo in
            // `ColTempo`: la differenza fra le due cose e' un metodo giusto
            // che nessuno chiama.
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
        //  4. PlanInMemory: la meta' che conta, pilotata a mano
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

            // `PlanStep` STA NELLA LIBRERIA, NON NEL PONTE, e cercarlo
            // fra i tipi del ponte non poteva funzionare -- ci ho provato, ed
            // e' andato rosso subito. Lo si prende dal costruttore che lo usa:
            // `List<PlanStep>`. Cosi' non c'e' nessun assembly da
            // indovinare, e se un giorno il tipo cambiasse la prova seguirebbe
            // invece di rompersi.
            var stepT = constructor.GetParameters()[0]
                .ParameterType.GetGenericArguments().FirstOrDefault();
            if (stepT == null)
            { Check("il primo parametro e' una lista di passi", false,
                    constructor.GetParameters()[0].ParameterType.Name); return; }

            // Tre passi finti. Non si esegue niente: si chiede a questa classe
            // il passo successivo, che e' cio' che fa l'esecutore.
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
            // Il controllo del piano ha gia' raccontato tre passi: l'esecuzione
            // deve ripartire da quattro, non da uno.
            wireT.GetMethod("Step", Everywhere).Invoke(wire, new object[] { 3.0, "controllo finito" });

            var plane = constructor.Invoke(new[] { itemList, wire });
            var next = planeT.GetMethod("NextAsync", Everywhere);

            var seen = new List<double>();
            for (int i = 0; i < 4; i++)   // tre passi e poi la fine
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
        //  5. L'ANNULLAMENTO: un no detto, non un silenzio
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
        //  6. SUL PONTE VERO, SU STDIO, SENZA SOLIDWORKS
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

                // ── CON IL GETTONE ────────────────────────────────────────
                //
                // `dry_run: true` controlla il piano passo per passo e non tocca
                // niente: gira con SOLIDWORKS chiuso, ed e' l'unico giro del
                // ponte che si puo' guardare per intero su questa macchina.
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

                // ── SENZA IL GETTONE: SILENZIO ────────────────────────────
                //
                // E' la meta' che si dimentica. Mandare avanzamenti a chi non
                // li ha chiesti e' rumore sul canale del protocollo, e un
                // client che non li aspetta puo' benissimo non saperli
                // leggere.
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

                // ── L'ANNULLAMENTO NON E' PIU' UN SILENZIO ────────────────
                int beforeAnn = p.Views.Count;
                Write(p,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\"," +
                    "\"params\":{\"requestId\":30,\"reason\":\"basta cosi'\"}}");
                // Si aspetta che il ponte dica qualcosa, e poi si guarda cosa.
                Line(p, 3000);
                var said = p.Views.Skip(beforeAnn)
                    .Where(r => r != null && r.Contains("annullamento chiesto")).ToList();
                Check("una `notifications/cancelled` NON cade nel silenzio",
                      said.Count > 0,
                      said.Count > 0 ? Short(said[0])
                                      : "niente: e' il no-op silenzioso che la voce descriveva");

                // E soprattutto: il ponte e' ancora vivo. Una notifica non
                // gestita che facesse morire il giro sarebbe peggio del
                // silenzio.
                var after = Obj(Ask(p, 32,
                    "{\"jsonrpc\":\"2.0\",\"id\":32,\"method\":\"ping\",\"params\":{}}"));
                Check("e il ponte risponde ancora dopo averla ricevuta",
                      after != null,
                      "una notifica sconosciuta non deve fermare il giro");
            }
            finally { Close(p); p.Dispose(); }
        }

        /// <summary>
        /// Le `notifications/progress` arrivate dopo il segno `da`.
        ///
        /// Si filtra per metodo e non per contenuto: sul canale passano anche
        /// le `notifications/message` del ponte, e contarle sarebbe un verde
        /// per il motivo sbagliato.
        /// </summary>
        /// <summary>
        /// Mette un valore, che sia una proprieta' o un campo.
        ///
        /// Provare tutt'e due invece di indovinare: un `SetValue` su una
        /// proprieta' che non esiste non fa niente e non lo dice, e il passo
        /// resterebbe senza nome — con la prova verde per il motivo sbagliato.
        /// Qui se non si e' potuto scrivere, si segna rosso.
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
