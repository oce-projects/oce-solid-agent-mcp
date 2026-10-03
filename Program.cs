using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Oce.Solid.Agent.Mcp;
using Oce.Solid.Agent.Bridge;
using Oce.Solid.Agent.Tools;

namespace Oce.Solid.Agent
{
    /// <summary>
    /// Il giro principale: legge una riga, risponde una riga.
    ///
    /// STDOUT E' SOLO DEL PROTOCOLLO. Qualunque altra cosa scritta li' rompe la
    /// conversazione senza dire perche'. Tutto il resto — l'aggancio, gli
    /// errori, quello che l'add-in racconta — va su stderr.
    ///
    /// SI PARTE ANCHE SE SOLIDWORKS NON C'E'. Un server MCP che muore
    /// all'avvio non fa in tempo a spiegarsi: chi lo lancia vede solo un
    /// processo finito. Qui si parte comunque e il motivo si legge chiamando
    /// uno strumento — che e' il posto dove chi usa il ponte sta guardando.
    /// </summary>
    internal static class Programma
    {
        /// <summary>
        /// LA VERSIONE SI LEGGE DA `VERSION.txt`, CHE VIAGGIA DENTRO
        /// L'ESEGUIBILE COME RISORSA.
        ///
        /// Qui c'era `const string Versione = "0.2.0"`, e c'e' rimasta
        /// **per settantotto versioni minori**: il ponte diceva 0.2.0
        /// nell'`initialize` mentre `VERSION.txt` era a 0.80.0. Chi
        /// registra la conversazione MCP si portava dietro un numero falso,
        /// e nessuno poteva accorgersene perche' non c'era niente con cui
        /// confrontarlo.
        ///
        /// E' la stessa marcescenza che il TODO si era gia' curata in
        /// testa il 16/9/2026 — quella riga diceva «Ponte 0.2.0» ed era
        /// vera il 14 — ma nel codice, dove si vede ancora meno.
        ///
        /// Se la risorsa non si legge si risponde **«sconosciuta»**, non
        /// un numero di ripiego: *un numero sbagliato e' peggio di un
        /// numero mancante*, perche' chi lo legge non ha modo di sapere
        /// che e' sbagliato.
        /// </summary>
        private static readonly string Versione = VersioneDallaRisorsa();

        private static string VersioneDallaRisorsa()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (var nome in asm.GetManifestResourceNames())
                {
                    if (!nome.EndsWith("VERSION.txt",
                                       StringComparison.OrdinalIgnoreCase)) continue;
                    using (var flusso = asm.GetManifestResourceStream(nome))
                    using (var lettore = new StreamReader(flusso))
                        return lettore.ReadToEnd().Trim();
                }
            }
            catch { }
            return "sconosciuta";
        }

        private static int Main(string[] argomenti)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.InputEncoding = new UTF8Encoding(false);

            var sessione = new Sessione();
            var motivo = sessione.Aggancia();
            if (motivo != null) Protocollo.Nota("[ponte] " + motivo);
            else
                Protocollo.Nota(
                    "[ponte] SolidWorks " + sessione.VersioneSolidWorks() +
                    ", libreria di disegno " + sessione.VersioneLibreria());

            var catalogo = new Catalogo();
            if (catalogo.ErroreDiCaricamento != null)
                Protocollo.Nota("[ponte] " + catalogo.ErroreDiCaricamento);
            else
                Protocollo.Nota("[ponte] catalogo: " + catalogo.Quante + " operazioni provate");

            if (argomenti != null && argomenti.Length > 0 && argomenti[0] == "--prova")
                return Prova(sessione, catalogo);

            var attrezzi = new Attrezzi(sessione, catalogo);
            var uscita = Console.Out;

            // DA QUI IN POI IL PONTE RACCONTA ANCHE AL CLIENT (voce W2.8).
            // Non prima: il ciclo dei messaggi non e' ancora partito, e una riga
            // su stdout senza nessuno che legga righe JSON e' solo rumore che
            // rompe chi arrivera' dopo.
            Protocollo.AccendiIlCanale(uscita);

            // GLI ARRETRATI PRIMA DI STDIN (voce W2.5).
            //
            // Mentre il ponte aspetta la risposta a una conferma, il client
            // puo' mandare altro. Quelle righe vengono messe da parte
            // invece che buttate — buttarle lascerebbe il client ad
            // aspettare per sempre una risposta che nessuno gli dara' — e
            // il giro le serve qui, appena puo'.
            string riga;
            while ((riga = Elicitazione.Arretrato() ?? Console.In.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(riga)) continue;

                Dictionary<string, object> messaggio;
                try { messaggio = Protocollo.DaJson(riga); }
                catch (Exception ex)
                {
                    Protocollo.Manda(uscita, Protocollo.Errore(
                        null, Protocollo.ErroreParsing, "JSON non valido: " + ex.Message));
                    continue;
                }
                if (messaggio == null) continue;

                object id = messaggio.TryGetValue("id", out var i) ? i : null;
                var metodo = messaggio.TryGetValue("method", out var m)
                    ? Convert.ToString(m) : null;
                var parametri = messaggio.TryGetValue("params", out var p)
                    ? p as Dictionary<string, object> : null;

                // Le NOTIFICHE non hanno id e non vogliono risposta. Rispondere
                // a una notifica e' il modo piu' rapido di confondere chi
                // ascolta: si ritroverebbe una risposta che non aspettava.
                bool eNotifica = !messaggio.ContainsKey("id");

                try
                {
                    var risposta = Serve(metodo, parametri, attrezzi, sessione, catalogo, id);
                    if (risposta != null && !eNotifica) Protocollo.Manda(uscita, risposta);
                }
                catch (Exception ex)
                {
                    Protocollo.Nota("[ponte] " + ex);
                    if (!eNotifica)
                        Protocollo.Manda(uscita, Protocollo.Errore(
                            id, Protocollo.ErroreInterno, ex.Message));
                }
            }

            Protocollo.Nota("[ponte] chiuso");
            return 0;
        }

        private static Dictionary<string, object> Serve(
            string metodo,
            Dictionary<string, object> parametri,
            Attrezzi attrezzi,
            Sessione sessione,
            Catalogo catalogo,
            object id)
        {
            switch (metodo)
            {
                case "initialize":
                    // COSA SA FARE IL CLIENT si legge QUI e una volta sola
                    // (voce W2.5): `capabilities.elicitation` dice se sa
                    // chiedere una conferma all'utente. Presumerlo vorrebbe
                    // dire appendere una chiamata aspettando una risposta
                    // che non arrivera' mai.
                    Elicitazione.LeggiLeCapacita(parametri);
                    return Protocollo.Risposta(id, new Dictionary<string, object>
                    {
                        // La versione del protocollo si rimanda com'e' arrivata
                        // quando la si riconosce: dichiararne una sola vuol
                        // dire rompersi al primo aggiornamento del client.
                        ["protocolVersion"] = VersioneProtocollo(parametri),
                        ["capabilities"] = new Dictionary<string, object>
                        {
                            ["tools"] = new Dictionary<string, object>(),
                            // Il catalogo si puo' anche ATTACCARE invece che
                            // chiedere (W2.12). Senza questa riga un client non
                            // prova nemmeno `resources/list`, e la porta c'e'
                            // ma nessuno la vede. `subscribe` e `listChanged`
                            // restano fuori apposta: **il catalogo non cambia
                            // mentre il ponte gira** — e' un file letto
                            // all'avvio — e dichiarare una capacita' che non si
                            // esercita e' una promessa che qualcuno aspettera'.
                            ["resources"] = new Dictionary<string, object>(),
                            // Il ponte racconta cosa fa, e lo dichiara: senza
                            // questa riga un client non sa che puo' chiedere
                            // `logging/setLevel`, e le notifiche gli arrivano
                            // senza che le abbia mai potute regolare (W2.8).
                            ["logging"] = new Dictionary<string, object>(),
                        },
                        ["serverInfo"] = new Dictionary<string, object>
                        {
                            ["name"] = "oce-solid-agent",
                            ["version"] = Versione,
                        },
                        ["instructions"] =
                            "Un ponte verso SOLIDWORKS. Prima di proporre " +
                            "qualunque cosa chiama «inspect_part»: gli " +
                            "identificativi delle facce e degli spigoli nascono " +
                            "li' e valgono per quella lettura. Dopo ogni " +
                            "modifica il pezzo cambia e vanno riletti.\n\n" +
                            "Se la forma e' in dubbio, «inspect_part» con " +
                            "«views» ti fa VEDERE il pezzo: l'elenco delle facce " +
                            "dice quante sono e quanto misurano, non com'e' " +
                            "fatto.\n\n" +
                            "Se quello che hai disegnato non va, «undo» ritira " +
                            "l'ultimo piano tutto intero: non serve rifare il " +
                            "pezzo da capo.",
                    });

                case "notifications/initialized":
                    return null;

                case "ping":
                    return Protocollo.Risposta(id, new Dictionary<string, object>());

                // Il client decide quanto vuole sentire (voce W2.8).
                //
                // UN LIVELLO CHE NON SI RICONOSCE E' UN ERRORE, non un silenzio:
                // accettarlo e ignorarlo lascerebbe il client convinto di aver
                // alzato la soglia mentre i messaggi continuano ad arrivare —
                // oppure, peggio, convinto di sentire tutto mentre non sente
                // niente.
                case "logging/setLevel":
                {
                    var voluto = parametri != null && parametri.TryGetValue("level", out var lv)
                        ? Convert.ToString(lv) : null;
                    if (!Protocollo.ImpostaIlLivello(voluto))
                        return Protocollo.Errore(
                            id, Protocollo.ParametriNonValidi,
                            "Livello «" + (voluto ?? "") + "» sconosciuto. Ci sono: " +
                            "debug, info, notice, warning, error, critical, alert, emergency.");
                    return Protocollo.Risposta(id, new Dictionary<string, object>());
                }

                case "tools/list":
                    return Protocollo.Risposta(id, new Dictionary<string, object>
                    {
                        ["tools"] = attrezzi.Elenco(),
                    });

                // ── LE RISORSE (voce W2.12) ─────────────────────────────
                //
                // Stesso catalogo degli strumenti, presa da un'altra porta:
                // `Risorse` riceve l'oggetto `Catalogo` che e' gia' in mano e
                // non ne costruisce un altro. Costruirne un altro vorrebbe dire
                // rileggere il file, e due letture dello stesso file in momenti
                // diversi sono due cataloghi che possono differire.
                case "resources/list":
                    return Protocollo.Risposta(id, new Dictionary<string, object>
                    {
                        ["resources"] = new Risorse(catalogo).Elenco(),
                    });

                case "resources/templates/list":
                    return Protocollo.Risposta(id, new Dictionary<string, object>
                    {
                        ["resourceTemplates"] = new Risorse(catalogo).Modelli(),
                    });

                case "resources/read":
                {
                    var uri = parametri != null && parametri.TryGetValue("uri", out var u)
                        ? Convert.ToString(u) : null;
                    if (string.IsNullOrWhiteSpace(uri))
                        return Protocollo.Errore(
                            id, Protocollo.ParametriNonValidi, "manca «uri»");

                    // THE URIs OF BEFORE W3.1-bis.5 (Oce.Solid.Agent.Core/Alias.cs, decision
                    // D4): read as the English ones until the first release, and the
                    // answer says so in `deprecated`. Removed in W3.1-bis.11.
                    var deprecated = new List<string>();
                    uri = Oce.Solid.Agent.Core.Alias.ResourceUri(uri, deprecated);
                    var risorse = new Risorse(catalogo);
                    var contenuto = risorse.Leggi(uri);
                    if (contenuto != null)
                    {
                        var answerWithNotes = contenuto as Dictionary<string, object>;
                        if (deprecated.Count > 0 && answerWithNotes != null) answerWithNotes["deprecated"] = deprecated.ToArray();
                        return Protocollo.Risposta(id, contenuto);
                    }

                    // UN URI CHE NON ESISTE DICE COSA ESISTE. Un «non trovato»
                    // nudo lascia a chi chiede l'unico rimedio che non
                    // funziona: riprovare uguale.
                    var simili = risorse.Simili(uri);
                    return Protocollo.Errore(
                        id, Protocollo.ParametriNonValidi,
                        "Non conosco la risorsa «" + uri + "». C'e' «" +
                        Risorse.UriElenco + "» (tutte le operazioni in breve) e " +
                        "«" + Risorse.PrefissoOperazione + "<name>» per una sola." +
                        (simili.Count == 0
                            ? ""
                            : " Forse cercavi: " + string.Join(", ", simili) + "."));
                }

                case "tools/call":
                {
                    var nome = parametri != null && parametri.TryGetValue("name", out var n)
                        ? Convert.ToString(n) : null;
                    var suoi = parametri != null && parametri.TryGetValue("arguments", out var a)
                        ? a as Dictionary<string, object> : null;
                    if (string.IsNullOrWhiteSpace(nome))
                        return Protocollo.Errore(
                            id, Protocollo.ParametriNonValidi, "manca «name»");
                    // IL GETTONE DELL'AVANZAMENTO (voce W2.2). Sta su
                    // `params._meta`, cioe' accanto a `name` e non dentro
                    // `arguments`: e' del PROTOCOLLO, non dello strumento,
                    // e un client generico lo mette li' senza sapere niente
                    // di cosa fa questo ponte.
                    return Protocollo.Risposta(
                        id, attrezzi.Chiama(nome, suoi, Avanzamento.Gettone(parametri)));
                }

                // ── L'ANNULLAMENTO, E UN NO DETTO (voce W2.2) ─────────────
                //
                // Prima cadeva qui sotto, nel `default`. Ed essendo una
                // NOTIFICA, il giro principale scartava la risposta d'errore:
                // **un no-op silenzioso**. Il client restava convinto di aver
                // annullato qualcosa.
                //
                // Questo ponte NON puo' interrompere una chiamata in corso, e
                // non e' una mancanza da riparare con piu' codice: serve una
                // richiesta alla volta sullo stesso thread perche' SOLIDWORKS
                // vuole STA. Mentre una chiamata gira, stdin non viene letto —
                // quindi quando questa riga arriva, la richiesta che nomina e'
                // gia' finita.
                //
                // La specifica dice che una `cancelled` si puo' ignorare. Qui
                // non si ignora: si DICE. *Un no dichiarato si puo' leggere;
                // un silenzio no.*
                case "notifications/cancelled":
                    Protocollo.Nota(AnnullamentoNonPossibile(parametri));
                    return null;

                default:
                    return Protocollo.Errore(
                        id, Protocollo.MetodoSconosciuto,
                        "Metodo non gestito: " + metodo);
            }
        }

        /// <summary>
        /// Cosa si risponde a `notifications/cancelled` (voce W2.2).
        ///
        /// Sta da sola per poterla leggere in una prova senza far partire un
        /// ponte: la cosa che conta di questa risposta e' **che dica il
        /// numero della richiesta e il perche'**, e quello si guarda qui.
        /// </summary>
        internal static string AnnullamentoNonPossibile(
            Dictionary<string, object> parametri)
        {
            object quale = null;
            string motivo = null;
            if (parametri != null)
            {
                parametri.TryGetValue("requestId", out quale);
                object m;
                if (parametri.TryGetValue("reason", out m)) motivo = Convert.ToString(m);
            }

            return "[ponte] annullamento chiesto per la richiesta " +
                   (quale == null ? "(senza requestId)" : "«" + Convert.ToString(quale) + "»") +
                   (string.IsNullOrWhiteSpace(motivo) ? "" : ", motivo: " + motivo) +
                   ". NON l'ho interrotta: questo ponte serve una richiesta " +
                   "alla volta sullo stesso thread, perche' SOLIDWORKS lo " +
                   "pretende, e mentre lavora non legge stdin. Quando questa " +
                   "riga e' arrivata, quella richiesta era gia' finita. Se la " +
                   "risposta non ti serve piu', buttala: il documento e' nello " +
                   "stato in cui quel piano l'ha lasciato, e «inspect_part» " +
                   "te lo dice.";
        }

        private static string VersioneProtocollo(Dictionary<string, object> parametri)
        {
            try
            {
                if (parametri != null &&
                    parametri.TryGetValue("protocolVersion", out var v) &&
                    v != null)
                {
                    var chiesta = Convert.ToString(v);
                    if (!string.IsNullOrWhiteSpace(chiesta)) return chiesta;
                }
            }
            catch { }
            return "2024-11-05";
        }

        // ── THE EXIT CODES OF --prova (entry H19, B3) ────────────────────
        //
        // The installer reads these, and no longer the sentences: a sentence
        // is for a person and gets translated, a code does not. The same
        // numbers are written in Installer/Steps.cs (SelfTest*), which cannot
        // reference this assembly; a contract in Prove reads both.
        //
        //   0  attached (with or without a document)
        //   1  anything else: the library did not load, an unknown HRESULT,
        //      the open document could not be read
        //   3  SOLIDWORKS installed and not running (MK_E_UNAVAILABLE)
        //   4  SOLIDWORKS not installed or not registered (CO_E_CLASSSTRING,
        //      REGDB_E_CLASSNOTREG)
        //   5  not tried: OCE_NO_ATTACH is set
        //   6  MK_E_UNAVAILABLE, but a SLDWORKS process runs in this Windows
        //      session: it runs as administrator while this process does not
        //      (or the other way round: the running object table is not
        //      shared across integrity levels), or it is still starting
        //
        // 2 is left out on purpose: it is the installer's own "not confirmed".
        //
        // 6 IS NOT 3 (H19 review). Until then the elevated case exited 3, the
        // installer accepted 3 as "SOLIDWORKS is simply closed" and said
        // "Installato" with exit 0, and the hint about administrator rights
        // was a line among the others: whoever read only the code (an AI, as
        // INSTALL-FOR-AI asks) concluded the bridge would attach, and it
        // never does until the two are started the same way.
        internal const int SelfTestAttached = 0;
        internal const int SelfTestOther = 1;
        internal const int SelfTestNotRunning = 3;
        internal const int SelfTestNotInstalled = 4;
        internal const int SelfTestSkipped = 5;
        internal const int SelfTestNotReachable = 6;

        internal static int SelfTestExitCode(AttachFailure failure)
        {
            switch (failure)
            {
                case AttachFailure.None: return SelfTestAttached;
                case AttachFailure.NotRunning: return SelfTestNotRunning;
                case AttachFailure.NotInstalled: return SelfTestNotInstalled;
                case AttachFailure.Skipped: return SelfTestSkipped;
                default: return SelfTestOther;
            }
        }

        /// <summary>
        /// The code of --prova, with what this process can see of SOLIDWORKS:
        /// "not running" while a SLDWORKS process runs in this session is 6.
        /// Pure, so the contracts can try both sides without SOLIDWORKS.
        /// </summary>
        internal static int SelfTestCodeSeen(AttachFailure failure, bool solidWorksProcessInThisSession)
        {
            int code = SelfTestExitCode(failure);
            return code == SelfTestNotRunning && solidWorksProcessInThisSession ? SelfTestNotReachable : code;
        }

        internal static string SelfTestMeaning(int code)
        {
            switch (code)
            {
                case SelfTestAttached: return "attached to SOLIDWORKS";
                case SelfTestNotRunning: return "SOLIDWORKS is installed but not running: open it";
                case SelfTestNotInstalled: return "SOLIDWORKS is not installed, or not registered: install or repair it";
                case SelfTestSkipped: return "attach not tried, OCE_NO_ATTACH is set";
                case SelfTestNotReachable:
                    return "a SOLIDWORKS process is running but did not answer: it may run as administrator " +
                           "while this process does not (or the other way round), or it is still starting. " +
                           "Start SOLIDWORKS and the MCP client the same way, then try again";
                default: return "not attached, for another reason: read the lines above";
            }
        }

        /// <summary>
        /// A SLDWORKS process in THIS Windows session. Another user's
        /// SOLIDWORKS on the same machine (fast user switching, a terminal
        /// server) is not one this process could ever attach to, and must not
        /// turn "not running" into "not reachable". A process whose session
        /// cannot be read counts: on a workstation it is almost always ours.
        /// </summary>
        private static bool SolidWorksProcessInThisSession()
        {
            try
            {
                int mine = System.Diagnostics.Process.GetCurrentProcess().SessionId;
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("SLDWORKS"))
                {
                    try { if (p.SessionId == mine) return true; }
                    catch { return true; }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// `--prova`: un giro secco senza client, per sapere se il ponte sta in
        /// piedi su questa macchina. Stampa su stderr e non tocca il pezzo.
        /// </summary>
        private static int Prova(Sessione sessione, Catalogo catalogo)
        {
            Protocollo.Nota("[prova] agganciata: " + sessione.Agganciata);
            Protocollo.Nota("[prova] libreria di disegno: " + sessione.PercorsoLibreria);
            Protocollo.Nota("[prova] catalogo: " + catalogo.Percorso);

            if (catalogo.Quante > 0)
            {
                var qualcuna = string.Join(", ",
                    new List<string>(catalogo.Nomi).GetRange(0, Math.Min(5, catalogo.Quante)));
                Protocollo.Nota("[prova] prime operazioni: " + qualcuna);
            }

            if (!sessione.Agganciata)
            {
                // SOLIDWORKS "not running" while a SLDWORKS process exists in
                // this session is the elevated case: started as administrator,
                // its session is not in the running object table this process
                // can see. It gets its own code (6), not a line beside a 3.
                bool processHere = sessione.Failure == AttachFailure.NotRunning && SolidWorksProcessInThisSession();
                int code = SelfTestCodeSeen(sessione.Failure, processHere);
                Protocollo.Nota("[self-test] exit code " + code + ": " + SelfTestMeaning(code));
                return code;
            }

            // AGGANCIATO E SENZA DOCUMENTI E' UN PONTE IN PIEDI (voce H6-bis).
            //
            // Qui prima si chiedeva lo stato e, senza un documento aperto, lo
            // stato torna null: il giro secco usciva 1, e l'installatore diceva
            // «Installato, ma il giro secco non ha confermato» — con SOLIDWORKS
            // acceso e vuoto, che e' lo stato normale di chi ha appena acceso il
            // computer. Trovato il 14/9, rimasto senza casella fino al 24/9, e
            // visto di nuovo quella sera: il banco aveva chiuso tutto e il caso 2
            // di `Checks/test-installer.js` e' diventato rosso.
            //
            // Tre esiti e non due: agganciato con un documento (lo misura),
            // agganciato senza documenti (lo dice, ed e' in piedi), non agganciato
            // (esce 1, sopra). Che abbia parlato con SOLIDWORKS lo dice gia' la
            // riga col numero di versione, stampata all'aggancio.
            if (sessione.DocumentoAttivo() == null)
            {
                Protocollo.Nota("[prova] nessun documento aperto: agganciato, niente da misurare.");
                Protocollo.Nota("[prova] il ponte sta in piedi.");
                return 0;
            }

            var stato = sessione.Stato(out var errore);
            if (stato == null) { Protocollo.Nota("[prova] " + errore); return SelfTestOther; }

            foreach (var chiave in new[] { "document_title", "kind", "volume_mm3", "bodies_total" })
                if (stato.TryGetValue(chiave, out var v))
                    Protocollo.Nota("[prova] " + chiave + " = " + v);

            Protocollo.Nota("[prova] il ponte sta in piedi.");
            return 0;
        }
    }
}
