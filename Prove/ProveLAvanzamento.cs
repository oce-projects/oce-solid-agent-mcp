using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static Prove.Contratti.Programma;
using static Condiviso.ClienteMcp;

namespace Prove.Contratti
{
    /// <summary>
    /// L'AVANZAMENTO E L'ANNULLAMENTO, COL MECCANISMO DEL PROTOCOLLO (voce W2.2)
    ///
    /// ── PERCHE' SI PUO' MISURARE SENZA SOLIDWORKS ───────────────────────
    ///
    /// Perche' `notifications/progress` e' protocollo, non CAD. E perche' il
    /// ponte ha gia' la **prova a vuoto** (`prova: true`, voce Z-G6): controlla
    /// un piano passo per passo con SOLIDWORKS chiuso. Quel giro manda le sue
    /// notifiche come qualunque altro, quindi la catena intera — gettone che
    /// entra, notifiche che escono, gettone che torna identico — si guarda
    /// end-to-end su stdio col ponte vero.
    ///
    /// Cio' che NON si misura qui e' l'avanzamento durante l'esecuzione vera,
    /// che vuole un documento aperto. Quella meta' la manda `PianoInMemoria`,
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
    internal static class ProveLAvanzamento
    {
        internal static void Tutte()
        {
            Gruppo("l'avanzamento e l'annullamento (W2.2)");

            IlGettone();
            LaNotifica();
            IlFilo();
            IlTempoAccanto();
            IlPianoInMemoria();
            LAnnullamentoDiceDiNo();
            SulPonteVero();
        }

        // ─────────────────────────────────────────────────────────────────
        //  gli attrezzi comuni
        // ─────────────────────────────────────────────────────────────────

        private static Type _avanzamento;
        private static bool _cercato;

        private static Type Avanzamento()
        {
            if (_cercato) return _avanzamento;
            _cercato = true;
            try
            {
                var ponte = System.IO.Path.Combine(Radice(), "bin", "oce-solid-agent-mcp.exe");
                if (!System.IO.File.Exists(ponte)) return null;
                _avanzamento = Carica.Classe(Carica.Ponte(ponte), "Avanzamento");
            }
            catch { _avanzamento = null; }
            return _avanzamento;
        }

        private const BindingFlags Ovunque =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance;

        // ─────────────────────────────────────────────────────────────────
        //  1. IL GETTONE: dove sta, e che si rimanda com'e' arrivato
        // ─────────────────────────────────────────────────────────────────

        private static void IlGettone()
        {
            var tipo = Avanzamento();
            if (tipo == null)
            {
                // Senza ponte: «non qui» in CI, rosso dove SOLIDWORKS c'e' (e
                // allora manca build.ps1). Col ponte e senza la classe: rosso.
                if (PonteOppureNonQui("l'avanzamento: il gettone, la notifica, il filo") == null) return;
                Segna("la classe Avanzamento c'e'", false, "il ponte c'e' ma la classe non si carica");
                return;
            }

            var metodo = tipo.GetMethod("Gettone", Ovunque);
            if (metodo == null) { Segna("Gettone() c'e'", false, ""); return; }

            Func<Dictionary<string, object>, object> leggi =
                p => metodo.Invoke(null, new object[] { p });

            Segna("senza parametri non c'e' nessun gettone",
                  leggi(null) == null, "null non fa saltare niente");

            Segna("senza `_meta` non c'e' nessun gettone",
                  leggi(new Dictionary<string, object> { ["name"] = "disegna" }) == null,
                  "chi non lo chiede non viene aggiornato");

            Segna("con `_meta` ma senza progressToken, nemmeno",
                  leggi(new Dictionary<string, object>
                  {
                      ["_meta"] = new Dictionary<string, object> { ["altro"] = 1 },
                  }) == null,
                  "`_meta` porta anche altre cose");

            var conStringa = leggi(new Dictionary<string, object>
            {
                ["_meta"] = new Dictionary<string, object> { ["progressToken"] = "abc-1" },
            });
            Segna("un gettone di testo si legge",
                  Convert.ToString(conStringa) == "abc-1", Convert.ToString(conStringa));

            // IL CASO CHE CONTA. La specifica ammette anche un INTERO, e un
            // `42` che tornasse indietro come `"42"` non combacerebbe piu' con
            // niente dalla parte del client -- che non avrebbe modo di dire
            // perche'. Quindi non si guarda: si ricopia.
            var conNumero = leggi(new Dictionary<string, object>
            {
                ["_meta"] = new Dictionary<string, object> { ["progressToken"] = 42 },
            });
            Segna("un gettone numerico resta un numero, non diventa testo",
                  conNumero is int && (int)conNumero == 42,
                  conNumero == null ? "(niente)" : conNumero.GetType().Name + " " + conNumero);

            Segna("un gettone vuoto vale come niente",
                  leggi(new Dictionary<string, object>
                  {
                      ["_meta"] = new Dictionary<string, object> { ["progressToken"] = "" },
                  }) == null,
                  "e' quasi sempre un campo riempito male");
        }

        // ─────────────────────────────────────────────────────────────────
        //  2. LA FORMA DELLA NOTIFICA
        // ─────────────────────────────────────────────────────────────────

        private static void LaNotifica()
        {
            var tipo = Avanzamento();
            if (tipo == null) return;
            var metodo = tipo.GetMethod("Notifica", Ovunque);
            if (metodo == null) { Segna("Notifica() c'e'", false, ""); return; }

            Func<object, double, double, string, Dictionary<string, object>> fai =
                (g, f, t, m) => (Dictionary<string, object>)
                    metodo.Invoke(null, new object[] { g, f, t, m });

            var n = fai("g1", 2, 5, "eseguo il passo 2 di 5");
            var p = n != null ? n["params"] as Dictionary<string, object> : null;

            Segna("la notifica e' `notifications/progress` e non ha id",
                  n != null && Convert.ToString(n["method"]) == "notifications/progress" &&
                  !n.ContainsKey("id"),
                  "una notifica con id sarebbe una richiesta, e il client aspetterebbe");
            Segna("porta gettone, progress e total",
                  p != null && Convert.ToString(p["progressToken"]) == "g1" &&
                  Convert.ToDouble(p["progress"]) == 2 &&
                  Convert.ToDouble(p["total"]) == 5,
                  "g1, 2 su 5");
            Segna("e il messaggio, che e' quello che una persona legge",
                  p != null && Convert.ToString(p["message"]).Contains("passo 2"),
                  Convert.ToString(p["message"]));

            var senzaTotale = fai("g1", 1, 0, "non so quanti saranno");
            var pt = senzaTotale["params"] as Dictionary<string, object>;
            Segna("senza totale, `total` non si scrive affatto",
                  !pt.ContainsKey("total"),
                  "un totale inventato darebbe a chi legge una barra che mente");

            var senzaMessaggio = fai("g1", 1, 3, "   ");
            var pm = senzaMessaggio["params"] as Dictionary<string, object>;
            Segna("e un messaggio vuoto non si scrive",
                  !pm.ContainsKey("message"), "un campo vuoto e' rumore");

            var numerico = fai(42, 1, 3, "x");
            var pn = numerico["params"] as Dictionary<string, object>;
            Segna("un gettone numerico torna indietro numerico",
                  pn["progressToken"] is int && (int)pn["progressToken"] == 42,
                  pn["progressToken"].GetType().Name);
        }

        // ─────────────────────────────────────────────────────────────────
        //  3. IL FILO: spento se nessuno l'ha chiesto, e `progress` cresce
        // ─────────────────────────────────────────────────────────────────

        private static void IlFilo()
        {
            var tipo = Avanzamento();
            if (tipo == null) return;
            var filoT = tipo.GetNestedType("Filo", BindingFlags.Public | BindingFlags.NonPublic);
            if (filoT == null) { Segna("la classe Filo c'e'", false, ""); return; }

            Func<object, object> nuovo = g => Activator.CreateInstance(filoT, new object[] { g });
            Action<object, double, string> passo = (f, n, m) =>
                filoT.GetMethod("Passo", Ovunque).Invoke(f, new object[] { n, m });
            Func<object, string, object> leggi = (f, nome) =>
                ReflectionLog.Global.Property(filoT, nome, Ovunque).GetValue(f);
            Action<object, double> totale = (f, t) =>
                filoT.GetProperty("Totale", Ovunque).SetValue(f, t);

            // ── SPENTO: nessun gettone, nessuna notifica ──────────────────
            var spento = nuovo(null);
            Segna("senza gettone il filo e' spento", !(bool)leggi(spento, "Acceso"), "");
            totale(spento, 3);
            passo(spento, 1, "uno");
            passo(spento, 2, "due");
            Segna("e spento non manda niente",
                  Convert.ToInt32(leggi(spento, "Mandate")) == 0,
                  "chi non ha chiesto di essere aggiornato non viene disturbato");

            // ── ACCESO ────────────────────────────────────────────────────
            var acceso = nuovo("g");
            totale(acceso, 4);
            passo(acceso, 1, "uno");
            passo(acceso, 2, "due");
            Segna("con un gettone manda un passo per volta",
                  Convert.ToInt32(leggi(acceso, "Mandate")) == 2,
                  "2 notifiche");
            Segna("e si ricorda a che punto e' arrivato",
                  Convert.ToDouble(leggi(acceso, "Ultimo")) == 2,
                  "serve a chi riprende il conto: l'esecuzione dopo il controllo");

            // ── LA REGOLA DELLA SPECIFICA, FATTA FALLIRE APPOSTA ──────────
            passo(acceso, 2, "di nuovo due");
            passo(acceso, 1, "indietro a uno");
            Segna("un passo che NON cresce viene scartato",
                  Convert.ToInt32(leggi(acceso, "Mandate")) == 2 &&
                  Convert.ToInt32(leggi(acceso, "Scartati")) == 2,
                  "la specifica pretende che `progress` cresca, e qui non puo' non crescere");
            passo(acceso, 3, "tre");
            Segna("e dopo uno scartato il successivo passa lo stesso",
                  Convert.ToInt32(leggi(acceso, "Mandate")) == 3,
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
        private static void IlTempoAccanto()
        {
            var tipo = Avanzamento();
            if (tipo == null) return;
            var filoT = tipo.GetNestedType("Filo",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (filoT == null) return;

            var conOrologio = filoT.GetConstructors(Ovunque)
                .FirstOrDefault(c => c.GetParameters().Length == 2);
            if (conOrologio == null)
            { Segna("il filo accetta un orologio", false,
                    "senza, il tempo scritto non si puo' guardare"); return; }

            // L'orologio finto: parte fermo, e lo si sposta a mano.
            var adesso = new DateTime(2026, 9, 24, 10, 0, 0);
            Func<DateTime> orologio = () => adesso;

            var filo = conOrologio.Invoke(new object[] { "g", orologio });
            filoT.GetProperty("Totale", Ovunque).SetValue(filo, 3.0);
            var notifica = tipo.GetMethod("Notifica", Ovunque);
            var passo = filoT.GetMethod("Passo", Ovunque);

            // Per leggere cosa SCRIVE senza un canale aperto si guarda
            // `Trascorso` e si ricostruisce la stringa? No: si guarderebbe
            // una copia della regola invece della regola. Si chiama
            // `ColTempo` direttamente.
            var colTempo = filoT.GetMethod("ColTempo", Ovunque);
            if (colTempo == null)
            { Segna("ColTempo() c'e'", false, ""); return; }
            Func<string, string> scrivi =
                m => (string)colTempo.Invoke(filo, new object[] { m });

            Segna("appena partito il tempo e' zero secondi",
                  scrivi("eseguo il passo 1 di 3") == "eseguo il passo 1 di 3 \u2014 0s",
                  scrivi("eseguo il passo 1 di 3"));

            adesso = adesso.AddSeconds(47);
            Segna("sotto il minuto si scrivono i secondi",
                  scrivi("passo 2") == "passo 2 \u2014 47s", scrivi("passo 2"));

            adesso = adesso.AddSeconds(87);   // 2m14s in tutto
            Segna("sopra il minuto si scrive minuti e secondi",
                  scrivi("passo 3") == "passo 3 \u2014 2m14s", scrivi("passo 3"));

            // QUESTO E' IL CASO DI S0: undici minuti sullo stesso passo.
            // Senza il tempo, «passo 3 di 5» e' identico a due secondi fa, e
            // chi guarda non sa se aspettare o uccidere il processo.
            adesso = adesso.AddMinutes(9);
            Segna("e a undici minuti si vede che e' tanto",
                  scrivi("passo 3") == "passo 3 \u2014 11m14s", scrivi("passo 3"));

            Segna("senza messaggio resta solo il tempo",
                  scrivi(null) == "11m14s", scrivi(null));

            Segna("e `Trascorso` dice la stessa cosa in numeri",
                  Math.Abs(((TimeSpan)filoT.GetProperty("Trascorso", Ovunque)
                      .GetValue(filo)).TotalSeconds - 674) < 0.001,
                  "674 secondi");

            // E il tempo finisce DAVVERO nella notifica, non solo in
            // `ColTempo`: la differenza fra le due cose e' un metodo giusto
            // che nessuno chiama.
            filoT.GetProperty("Totale", Ovunque).SetValue(filo, 3.0);
            passo.Invoke(filo, new object[] { 1.0, "passo 1" });
            var visto = (Dictionary<string, object>)notifica.Invoke(null,
                new object[] { "g", 1.0, 3.0, scrivi("passo 1") });
            var par = visto["params"] as Dictionary<string, object>;
            Segna("e il messaggio della notifica porta il tempo",
                  Convert.ToString(par["message"]).EndsWith("11m14s"),
                  Convert.ToString(par["message"]));
        }

        // ─────────────────────────────────────────────────────────────────
        //  4. PianoInMemoria: la meta' che conta, pilotata a mano
        // ─────────────────────────────────────────────────────────────────

        private static void IlPianoInMemoria()
        {
            var tipo = Avanzamento();
            if (tipo == null) return;
            var asm = tipo.Assembly;
            var filoT = tipo.GetNestedType("Filo", BindingFlags.Public | BindingFlags.NonPublic);
            var pianoT = Carica.Classe(asm, "PianoInMemoria");
            if (pianoT == null || filoT == null)
            { Segna("PianoInMemoria c'e'", false, ""); return; }

            var costruttore = pianoT.GetConstructors(Ovunque)
                .FirstOrDefault(c => c.GetParameters().Length == 2);
            if (costruttore == null)
            { Segna("PianoInMemoria accetta un filo", false, "nessun costruttore a due"); return; }

            // `PassoDelPiano` STA NELLA LIBRERIA, NON NEL PONTE, e cercarlo
            // fra i tipi del ponte non poteva funzionare -- ci ho provato, ed
            // e' andato rosso subito. Lo si prende dal costruttore che lo usa:
            // `List<PassoDelPiano>`. Cosi' non c'e' nessun assembly da
            // indovinare, e se un giorno il tipo cambiasse la prova seguirebbe
            // invece di rompersi.
            var passoT = costruttore.GetParameters()[0]
                .ParameterType.GetGenericArguments().FirstOrDefault();
            if (passoT == null)
            { Segna("il primo parametro e' una lista di passi", false,
                    costruttore.GetParameters()[0].ParameterType.Name); return; }

            // Tre passi finti. Non si esegue niente: si chiede a questa classe
            // il passo successivo, che e' cio' che fa l'esecutore.
            var lista = (System.Collections.IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(passoT));
            for (int i = 1; i <= 3; i++)
            {
                var pd = Activator.CreateInstance(passoT);
                Metti(passoT, pd, "Ordine", i);
                Metti(passoT, pd, "Operazione", "op" + i);
                lista.Add(pd);
            }

            var filo = Activator.CreateInstance(filoT, new object[] { "g" });
            filoT.GetProperty("Totale", Ovunque).SetValue(filo, 6.0);
            // Il controllo del piano ha gia' raccontato tre passi: l'esecuzione
            // deve ripartire da quattro, non da uno.
            filoT.GetMethod("Passo", Ovunque).Invoke(filo, new object[] { 3.0, "controllo finito" });

            var piano = costruttore.Invoke(new[] { lista, filo });
            var prossimo = pianoT.GetMethod("ProssimoAsync", Ovunque);

            var visti = new List<double>();
            for (int i = 0; i < 4; i++)   // tre passi e poi la fine
            {
                prossimo.Invoke(piano, new object[] { null });
                visti.Add(Convert.ToDouble(filoT.GetProperty("Ultimo", Ovunque).GetValue(filo)));
            }

            Segna("l'esecuzione manda un avanzamento per passo",
                  Convert.ToInt32(filoT.GetProperty("Mandate", Ovunque).GetValue(filo)) == 4,
                  "1 dal controllo + 3 dall'esecuzione");
            Segna("e riprende il conto da dove il controllo l'ha lasciato",
                  visti[0] == 4 && visti[1] == 5 && visti[2] == 6,
                  "4, 5, 6 su 6 — non riparte da 1");
            Segna("finiti i passi non manda piu' niente",
                  visti[3] == 6,
                  "la quarta chiamata e' «fine», e non inventa un settimo passo");
        }

        // ─────────────────────────────────────────────────────────────────
        //  5. L'ANNULLAMENTO: un no detto, non un silenzio
        // ─────────────────────────────────────────────────────────────────

        private static void LAnnullamentoDiceDiNo()
        {
            var ponte = PonteOppureNonQui("l'annullamento dice di no");
            if (ponte == null) return;
            Type prog;
            try { prog = Carica.Classe(Carica.Ponte(ponte), "Programma"); }
            catch { prog = null; }
            if (prog == null) { Segna("la classe Programma c'e'", false, ""); return; }

            var metodo = prog.GetMethod("AnnullamentoNonPossibile", Ovunque);
            if (metodo == null)
            { Segna("AnnullamentoNonPossibile() c'e'", false, ""); return; }

            Func<Dictionary<string, object>, string> frase =
                p => (string)metodo.Invoke(null, new object[] { p });

            var pieno = frase(new Dictionary<string, object>
            {
                ["requestId"] = 7,
                ["reason"] = "l'utente ha cambiato idea",
            });
            Segna("la risposta all'annullamento nomina la richiesta",
                  pieno.Contains("7"), Corto(pieno));
            Segna("e riporta il motivo che il client ha dato",
                  pieno.Contains("cambiato idea"),
                  "chi legge il log deve poter capire di cosa si parlava");
            Segna("e dice CHIARAMENTE che non l'ha interrotta",
                  pieno.Contains("NON l'ho interrotta"),
                  "prima cadeva nel `default` e, essendo una notifica, " +
                  "l'errore veniva scartato: un no-op silenzioso");
            Segna("e dice perche', che e' cio' che rende il no utile",
                  pieno.Contains("una richiesta alla volta"),
                  "un no senza motivo si legge come un difetto");

            var vuoto = frase(null);
            Segna("e senza parametri non salta, lo dice e basta",
                  !string.IsNullOrEmpty(vuoto) && vuoto.Contains("senza requestId"),
                  Corto(vuoto));
        }

        // ─────────────────────────────────────────────────────────────────
        //  6. SUL PONTE VERO, SU STDIO, SENZA SOLIDWORKS
        // ─────────────────────────────────────────────────────────────────

        private static void SulPonteVero()
        {
            var eseguibile = PonteOppureNonQui("l'avanzamento sul ponte vero");
            if (eseguibile == null) return;

            var p = Apri(eseguibile);
            if (p == null) { Segna("il ponte parte", false, "non si e' avviato"); return; }
            try
            {
                Chiedi(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"prove\",\"version\":\"1\"}}}");

                // ── CON IL GETTONE ────────────────────────────────────────
                //
                // `prova: true` controlla il piano passo per passo e non tocca
                // niente: gira con SOLIDWORKS chiuso, ed e' l'unico giro del
                // ponte che si puo' guardare per intero su questa macchina.
                const string tre =
                    "{\"operazione\":\"estrudi_rettangolo\",\"argomenti\":" +
                    "{\"lunghezza_mm\":100,\"larghezza_mm\":50,\"spessore_mm\":5}}," +
                    "{\"operazione\":\"estrudi_cerchio\",\"argomenti\":" +
                    "{\"diametro_mm\":40,\"altezza_mm\":10}}," +
                    "{\"operazione\":\"guscio\",\"argomenti\":{\"spessore_mm\":3}}";

                int primaCon = p.Viste.Count;
                Chiedi(p, 30,
                    "{\"jsonrpc\":\"2.0\",\"id\":30,\"method\":\"tools/call\",\"params\":" +
                    "{\"_meta\":{\"progressToken\":\"pt-77\"}," +
                    "\"name\":\"disegna\",\"arguments\":{\"prova\":true,\"passi\":[" + tre + "]}}}");

                var conGettone = Avanzamenti(p, primaCon);
                Segna("con un progressToken arrivano notifiche di avanzamento",
                      conGettone.Count >= 3,
                      conGettone.Count + " `notifications/progress` per un piano di 3 passi");

                Segna("e portano il gettone che il client ha mandato",
                      conGettone.Count > 0 &&
                      conGettone.All(n => Convert.ToString(Dentro(n, "progressToken")) == "pt-77"),
                      "pt-77 su tutte");

                var numeri = conGettone
                    .Select(n => Convert.ToDouble(Dentro(n, "progress"))).ToList();
                Segna("e `progress` cresce sempre, come la specifica pretende",
                      numeri.Count > 1 &&
                      Enumerable.Range(1, numeri.Count - 1).All(i => numeri[i] > numeri[i - 1]),
                      string.Join(", ", numeri));

                // ── SENZA IL GETTONE: SILENZIO ────────────────────────────
                //
                // E' la meta' che si dimentica. Mandare avanzamenti a chi non
                // li ha chiesti e' rumore sul canale del protocollo, e un
                // client che non li aspetta puo' benissimo non saperli
                // leggere.
                int primaSenza = p.Viste.Count;
                Chiedi(p, 31,
                    "{\"jsonrpc\":\"2.0\",\"id\":31,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"disegna\",\"arguments\":{\"prova\":true,\"passi\":[" + tre + "]}}}");

                var senza = Avanzamenti(p, primaSenza);
                Segna("e chi NON manda un gettone non riceve niente",
                      senza.Count == 0,
                      senza.Count == 0
                          ? "zero notifiche, come deve essere"
                          : senza.Count + " notifiche non richieste");

                // ── L'ANNULLAMENTO NON E' PIU' UN SILENZIO ────────────────
                int primaAnn = p.Viste.Count;
                Scrivi(p,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\"," +
                    "\"params\":{\"requestId\":30,\"reason\":\"basta cosi'\"}}");
                // Si aspetta che il ponte dica qualcosa, e poi si guarda cosa.
                Riga(p, 3000);
                var detto = p.Viste.Skip(primaAnn)
                    .Where(r => r != null && r.Contains("annullamento chiesto")).ToList();
                Segna("una `notifications/cancelled` NON cade nel silenzio",
                      detto.Count > 0,
                      detto.Count > 0 ? Corto(detto[0])
                                      : "niente: e' il no-op silenzioso che la voce descriveva");

                // E soprattutto: il ponte e' ancora vivo. Una notifica non
                // gestita che facesse morire il giro sarebbe peggio del
                // silenzio.
                var dopo = Ogg(Chiedi(p, 32,
                    "{\"jsonrpc\":\"2.0\",\"id\":32,\"method\":\"ping\",\"params\":{}}"));
                Segna("e il ponte risponde ancora dopo averla ricevuta",
                      dopo != null,
                      "una notifica sconosciuta non deve fermare il giro");
            }
            finally { Chiudi(p); p.Dispose(); }
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
        private static void Metti(Type tipo, object oggetto, string nome, object valore)
        {
            var prop = tipo.GetProperty(nome, Ovunque);
            if (prop != null && prop.CanWrite) { ReflectionLog.Global.Found(); prop.SetValue(oggetto, valore); return; }
            var campo = tipo.GetField(nome, Ovunque);
            if (campo != null) { ReflectionLog.Global.Found(); campo.SetValue(oggetto, valore); return; }
            ReflectionLog.Global.Missing("writable property or field", tipo, nome);
            Segna("il passo finto si puo' riempire: " + nome, false,
                  tipo.Name + " non ha ne' la proprieta' ne' il campo");
        }

        private static List<Dictionary<string, object>> Avanzamenti(Ponte p, int da)
        {
            var fuori = new List<Dictionary<string, object>>();
            foreach (var riga in p.Viste.Skip(da))
            {
                if (riga == null || riga.IndexOf("notifications/progress",
                                                 StringComparison.Ordinal) < 0) continue;
                var o = Ogg(riga);
                if (o == null) continue;
                if (Convert.ToString(Dentro(o, "method")) != "notifications/progress") continue;
                var par = Dentro(o, "params") as Dictionary<string, object>;
                if (par != null) fuori.Add(par);
            }
            return fuori;
        }
    }
}
