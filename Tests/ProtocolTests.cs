using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static Oce.Solid.Agent.ContractTests.Program;
using static Shared.McpClient;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// IL PROTOCOLLO MCP, parlando col ponte come farebbe un cliente.
    ///
    /// Si lancia `oce-solid-agent-mcp.exe` come sottoprocesso e gli si danno
    /// righe su stdin. Tutto gira **senza SOLIDWORKS**, perche' il ponte parte
    /// comunque e dice il motivo dopo: e' una proprieta' del ponte, ed e' anche
    /// il primo controllo di questo gruppo.
    ///
    /// ── IL TIMEOUT NON E' UNA COMODITA' ──────────────────────────────────
    ///
    /// Leggere da un sottoprocesso senza bloccarsi vuole **un timeout su ogni
    /// riga attesa**: senza, una prova che fallisce APPENDE il comando invece
    /// di diventare rossa, e in una CI vuol dire un lavoro che non finisce mai
    /// invece di un rosso che si legge.
    ///
    /// E il difetto silenzioso da temere e' l'altro: **una prova di protocollo
    /// che «passa» perche' ha letto zero righe in attesa di zero righe.** Per
    /// questo ogni attesa dice quante righe vuole, e zero righe non e' mai un
    /// verde.
    /// </summary>
    internal static class ProtocolTests
    {
        // LA PRIMA CHIAMATA COSTA PIU' DELLE ALTRE: il ponte all'`initialize`
        // si aggancia a SOLIDWORKS, e se SOLIDWORKS c'e' ed e' occupato quella
        // riga puo' tardare parecchio. Venti secondi non bastavano, e il
        // sintomo era ingannevole: l'`initialize` risultava senza risposta e
        // poi la sua riga arrivava addosso alla prova dopo.
        // TimeoutMs sta in Shared/McpClient.cs: lo usano in due.

        internal static void All()
        {
            Group("il protocollo MCP");

            var bridge = BridgeOrNotHere("il protocollo MCP, per intero");
            if (bridge == null) return;

            using (var p = Open(bridge))
            {
                if (p == null) { Check("il ponte parte", false, "non e' partito"); return; }
                Check("il ponte parte anche senza SOLIDWORKS", true, "");

                // ── initialize RIMANDA LA VERSIONE RICEVUTA ───────────────
                var r = Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"prove\",\"version\":\"1\"}}}");
                Check("initialize risponde", r != null,
                      r != null ? Short(r)
                                : "niente entro " + (TimeoutMs / 1000) + "s. Righe viste finora: " +
                                  string.Join(" | ", p.Views.Take(3)));
                var iniz = Obj(r);
                var result = Inside(iniz, "result") as Dictionary<string, object>;
                Check("initialize rimanda la versione di protocollo ricevuta",
                      result != null &&
                      Convert.ToString(Inside(result, "protocolVersion")) == "2024-11-05",
                      Convert.ToString(Inside(result, "protocolVersion")));

                // ── E LA VERSIONE CHE ANNUNCIA E' QUELLA VERA ─────────────
                //
                // Non lo era: `Program.cs` teneva `const Versione =
                // "0.2.0"` mentre `VERSION.txt` diceva 0.80.0, e ci e'
                // rimasto per settantotto versioni minori. Non c'era niente
                // di sbagliato nel codice — c'erano DUE posti dove scrivere
                // lo stesso numero, e uno dei due nessuno lo rileggeva.
                //
                // Il rimedio e' una fonte sola (`VERSION.txt` dentro
                // l'eseguibile). Questa riga e' cio' che impedisce che
                // tornino a essere due.
                var expected = RepositoryVersion();
                var info = Inside(result, "serverInfo") as Dictionary<string, object>;
                var called = Convert.ToString(Inside(info, "version"));
                Check("e la versione che il ponte annuncia e' quella di VERSION.txt",
                      expected != null && called == expected,
                      expected == null
                          ? "VERSION.txt non si legge"
                          : "annunciata «" + called + "», VERSION.txt dice «" + expected + "»");

                // ── UNA NOTIFICA NON RICEVE RISPOSTA ──────────────────────
                //
                // Una riga senza `id` e' una notifica, e rispondere a una
                // notifica e' una violazione che nessun cliente segnala.
                //
                // NON SI MISURA CON UN'ATTESA A VUOTO. «Non e' arrivato niente
                // in un secondo e mezzo» e' vero anche quando il ponte e'
                // semplicemente lento, ed e' il difetto silenzioso di questo
                // gruppo: una prova che passa perche' ha letto zero righe in
                // attesa di zero righe. Si manda la notifica, SUBITO DOPO una
                // domanda vera, e si guarda che la PRIMA riga che torna sia la
                // risposta a quella: se la notifica avesse avuto una risposta,
                // sarebbe arrivata prima.
                Write(p, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                Write(p, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");

                // SI SALTANO LE NOTIFICHE DEL PONTE, non le risposte.
                //
                // Dalla 0.58.0 il ponte manda `notifications/message` (voce
                // W2.8), e una di quelle puo' arrivare fra le due righe. Non
                // c'entra niente con quello che questo controllo misura: la
                // domanda e' «la prima RISPOSTA e' quella alla richiesta con
                // id, o c'e' una risposta alla notifica davanti?».
                //
                // Il controllo pero' NON si allarga a «ignora tutto cio' che non
                // ha id»: si salta solo `notifications/message`, che e' roba
                // nostra e riconoscibile. Se il ponte cominciasse a mandare
                // qualcos'altro senza id, questo se ne accorgerebbe — ed e' il
                // punto.
                string before = null;
                Dictionary<string, object> objBefore = null;
                var upToA = Environment.TickCount + TimeoutMs;
                while (Environment.TickCount < upToA)
                {
                    before = Line(p, Math.Max(200, upToA - Environment.TickCount));
                    if (before == null) break;
                    objBefore = Obj(before);
                    var method = objBefore == null ? null : Convert.ToString(Inside(objBefore, "method"));
                    if (method != "notifications/message") break;
                }
                Check("una notifica NON riceve risposta",
                      objBefore != null && objBefore.ContainsKey("id") &&
                      Convert.ToString(objBefore["id"]) == "2",
                      Short(before));

                // ── tools/list ────────────────────────────────────────────
                var itemList = objBefore != null && Convert.ToString(Inside(objBefore, "id")) == "2"
                    ? objBefore
                    : Obj(Ask(p, 2,
                        "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}"));
                var tools = Inside(Inside(itemList, "result"), "tools") as object[];
                // SEI DALLA 0.57.0, non cinque: e' nato `measure` (voce W2.3).
                // SETTE DALLA 0.87.0: e' nato `document` (voci Z-B1/B2/B5/B6),
                // e questa riga e' stata cambiata apposta, non trovata rossa.
                // Il numero si assertisce ESATTO e non «almeno»: uno strumento
                // in piu' che appare senza che nessuno l'abbia voluto e' un
                // pezzo di superficie pubblica comparso per sbaglio, e questo e'
                // il solo posto che se ne accorge.
                // NINE SINCE 0.112.0: api_search and api_signature (entry
                // U1.1). Changed on purpose, like the seventh.
                Check("tools/list gives exactly nine tools",
                      tools != null && tools.Length == 9,
                      tools == null ? "(niente)" : tools.Length + " strumenti: " +
                          string.Join(", ", tools.Select(s => Convert.ToString(Inside(s, "name")))));

                // ── LE ANNOTAZIONI DICONO LA VERITA' ──────────────────────
                //
                // Sono l'unica cosa che il client legge PRIMA di chiamare, e
                // decidono se si ferma a chiedere conferma. Sbagliarle nel verso
                // comodo — un'operazione che toglie materiale dichiarata
                // innocua — e' il difetto che non da' errore, spostato dal pezzo
                // al protocollo.
                Func<string, string, string> ann = (toolName, theField) =>
                {
                    var s = tools?.FirstOrDefault(
                        x => Convert.ToString(Inside(x, "name")) == toolName);
                    return s == null ? null
                        : Convert.ToString(Inside(Inside(s, "annotations"), theField));
                };
                Check("«measure» si dichiara di sola lettura",
                      string.Equals(ann("measure", "readOnlyHint"), "True",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("measure", "readOnlyHint") ?? "(manca)");
                Check("«measure» NON si dichiara distruttivo",
                      string.Equals(ann("measure", "destructiveHint"), "False",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("measure", "destructiveHint") ?? "(manca)");
                Check("«build» resta distruttivo",
                      string.Equals(ann("build", "destructiveHint"), "True",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("build", "destructiveHint") ?? "(manca)");
                Check("«build» NON si dichiara idempotente",
                      string.Equals(ann("build", "idempotentHint"), "False",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("build", "idempotentHint") ?? "(manca)");

                // «document» cambia il documento davanti: NON e' di sola
                // lettura, anche se non tocca la geometria. Un client che lo
                // credesse di sola lettura lo chiamerebbe senza chiedere, e il
                // piano successivo scriverebbe in un pezzo diverso da quello
                // che chi lavora ha davanti. E NON e' idempotente: «create» due
                // volte fa due documenti.
                Check("«document» NON si dichiara di sola lettura",
                      string.Equals(ann("document", "readOnlyHint"), "False",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("document", "readOnlyHint") ?? "(manca)");
                Check("«document» NON si dichiara idempotente",
                      string.Equals(ann("document", "idempotentHint"), "False",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("document", "idempotentHint") ?? "(manca)");
                Check("«document» NON si dichiara distruttivo: non chiude e non butta niente",
                      string.Equals(ann("document", "destructiveHint"), "False",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("document", "destructiveHint") ?? "(manca)");

                bool schemasOk = tools != null && tools.All(s =>
                {
                    var schema = Inside(s, "inputSchema") as Dictionary<string, object>;
                    return schema != null && Convert.ToString(Inside(schema, "type")) == "object";
                });
                Check("ogni strumento ha un inputSchema di tipo object", schemasOk);

                bool namesOk = tools != null && tools.All(s =>
                    !string.IsNullOrWhiteSpace(Convert.ToString(Inside(s, "name"))) &&
                    !string.IsNullOrWhiteSpace(Convert.ToString(Inside(s, "description"))));
                Check("ogni strumento ha nome e descrizione", namesOk);

                // `title` DI PRIMO LIVELLO, non solo dentro `annotations`.
                // Nelle revisioni recenti dello spec sta sullo strumento; un
                // client nuovo che legge solo li' mostrerebbe il nome tecnico
                // `inspect_part` a chi sta lavorando.
                bool titlesOk = tools != null && tools.All(s =>
                    !string.IsNullOrWhiteSpace(Convert.ToString(Inside(s, "title"))));
                Check("ogni strumento ha un «title» di primo livello", titlesOk,
                      tools == null ? "(niente)" : string.Join(", ",
                          tools.Select(s => Convert.ToString(Inside(s, "title")))));

                // ── UNO STRUMENTO CHE NON C'E' ────────────────────────────
                //
                // `isError: true` DENTRO IL CONTENUTO, non un errore JSON-RPC:
                // la distinzione e' scritta in `Mcp/Protocol.cs` e fino a
                // oggi non la verificava nessuno. Un errore di protocollo dice
                // «il cliente ha sbagliato a parlare»; questo dice «lo
                // strumento non esiste», ed e' una cosa che il modello deve
                // poter leggere e correggere da solo.
                var unknown = Obj(Ask(p, 3,
                    "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"strumento_che_non_esiste\",\"arguments\":{}}}"));
                Check("uno strumento sconosciuto NON e' un errore JSON-RPC",
                      unknown != null && !unknown.ContainsKey("error"),
                      Short(Convert.ToString(Inside(unknown, "error"))));
                var its = Inside(unknown, "result") as Dictionary<string, object>;
                Check("uno strumento sconosciuto da' isError: true nel contenuto",
                      its != null && its.ContainsKey("isError") &&
                      Convert.ToString(its["isError"]).Equals("True", StringComparison.OrdinalIgnoreCase));

                // L'ELENCO NEL MESSAGGIO E' QUELLO VERO, non una copia
                // invecchiata. Il messaggio di «non lo conosco» nomina gli
                // strumenti scritti a mano in `ToolNames`; qui si
                // confronta con quelli che `tools/list` ha appena dichiarato.
                // Se domani nasce un settimo strumento e nessuno aggiorna
                // quella riga, il modello che ha sbagliato nome riceve un
                // elenco in cui il settimo non c'e' — e conclude che non
                // esiste. Un elenco incompleto e' peggio di nessun elenco.
                var unknownText = TextOf(its);
                var missingItems = tools == null ? new List<string>() : tools
                    .Select(s => Convert.ToString(Inside(s, "name")))
                    .Where(n => !string.IsNullOrWhiteSpace(n) &&
                                unknownText.IndexOf(n, StringComparison.Ordinal) < 0)
                    .ToList();
                Check("il messaggio elenca TUTTI gli strumenti che tools/list dichiara",
                      missingItems.Count == 0,
                      missingItems.Count == 0
                          ? "tutti e " + (tools == null ? 0 : tools.Length)
                          : "non nominati: " + string.Join(", ", missingItems));

                // ── «measure» RIFIUTA CIO' CHE TOCCA IL PEZZO ─────────────
                //
                // E' il criterio della voce W2.3, e va provato COL CASO ROSSO:
                // uno strumento che si dichiara di sola lettura e poi accetta
                // un'estrusione non e' un comodo, e' una bugia nel protocollo —
                // e il client ha gia' deciso di non chiedere conferma sulla
                // base di quella dichiarazione.
                //
                // Questo rifiuto arriva SENZA SOLIDWORKS: il cancello guarda
                // le famiglie prima di toccare la sessione. Percio' e' provabile
                // qui, dove SOLIDWORKS non c'e'.
                var touches = Obj(Ask(p, 4,
                    "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"measure\",\"arguments\":{\"steps\":[" +
                    "{\"operation\":\"extrude_rectangle\",\"arguments\":{}}]}}}"));
                var itsTouch = Inside(touches, "result") as Dictionary<string, object>;
                Check("«measure» rifiuta «extrude_rectangle»",
                      itsTouch != null && itsTouch.ContainsKey("isError") &&
                      Convert.ToString(itsTouch["isError"]).Equals("True", StringComparison.OrdinalIgnoreCase),
                      Short(TextOf(itsTouch)));
                // E LO DICE COL CRITERIO, non con un verdetto: chi legge deve
                // sapere CON QUALE strumento si fa, altrimenti deve indovinare.
                Check("il rifiuto dice con quale strumento si fa",
                      TextOf(itsTouch).IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0,
                      Short(TextOf(itsTouch)));
                Check("il rifiuto dice che non ha eseguito niente",
                      TextOf(itsTouch).IndexOf("niente", StringComparison.OrdinalIgnoreCase) >= 0,
                      Short(TextOf(itsTouch)));

                // ── I DATI ESCONO ANCHE IN FORMA LEGGIBILE (voce W2.4) ───
                //
                // `list_operations` non tocca SOLIDWORKS, quindi e' l'unico
                // strumento con dati veri che si possa provare qui. Il testo
                // deve restare — i client che leggono solo `content` sono la
                // maggioranza — e `structuredContent` deve esserci ACCANTO.
                var list = Obj(Ask(p, 5,
                    "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"list_operations\",\"arguments\":{}}}"));
                var itsList = Inside(list, "result") as Dictionary<string, object>;
                Check("il testo c'e' ancora", TextOf(itsList).Length > 0,
                      Short(TextOf(itsList)));
                Check("e accanto c'e' «structuredContent»",
                      itsList != null && itsList.ContainsKey("structuredContent") &&
                      itsList["structuredContent"] != null,
                      itsList != null && itsList.ContainsKey("structuredContent")
                          ? "c'e'" : "manca");
                // NIENTE `outputSchema`, ed e' una scelta: chi lo dichiara DEVE
                // rispettarlo, e la forma dello stato cambia col documento. Uno
                // schema parziale farebbe rifiutare risposte giuste. Se un
                // giorno comparisse senza che nessuno abbia deciso la forma,
                // questo controllo lo dice.
                bool conSchema = tools != null && tools.Any(
                    s => Inside(s, "outputSchema") != null);
                Check("nessuno strumento dichiara un «outputSchema» a meta'", !conSchema,
                      conSchema
                          ? "qualcuno lo dichiara: o la forma e' stabile e va scritto tutto, o va tolto"
                          : "nessuno, come deciso");

                // ── IL LIVELLO DI RACCONTO SI PUO' CHIEDERE (voce W2.8) ──
                //
                // E il caso rosso conta piu' di quello verde: un livello che non
                // esiste dev'essere un ERRORE. Accettarlo e ignorarlo
                // lascerebbe il client convinto di aver alzato la soglia mentre
                // i messaggi continuano ad arrivare — o, peggio, convinto di
                // sentire tutto mentre non sente piu' niente.
                var levelOk = Obj(Ask(p, 6,
                    "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"logging/setLevel\"," +
                    "\"params\":{\"level\":\"warning\"}}"));
                Check("logging/setLevel accetta un livello vero",
                      levelOk != null && levelOk.ContainsKey("result") &&
                      !levelOk.ContainsKey("error"),
                      Short(Convert.ToString(Inside(levelOk, "error"))));

                var levelNo = Obj(Ask(p, 7,
                    "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"logging/setLevel\"," +
                    "\"params\":{\"level\":\"fortissimo\"}}"));
                var levelError = Inside(levelNo, "error") as Dictionary<string, object>;
                Check("logging/setLevel RIFIUTA un livello inventato",
                      levelError != null,
                      levelError == null ? "(l'ha accettato)" : "-32602");
                Check("e dice quali sono i livelli veri",
                      levelError != null &&
                      Convert.ToString(Inside(levelError, "message"))
                             .IndexOf("emergency", StringComparison.Ordinal) >= 0,
                      Short(Convert.ToString(Inside(levelError, "message"))));

                // ── LE RISORSE (voce W2.12) ───────────────────────────────
                //
                // Il catalogo si puo' ATTACCARE invece che chiedere. Tre cose
                // da provare, e la terza e' quella che conta.
                var resourceList = Obj(Ask(p, 40,
                    "{\"jsonrpc\":\"2.0\",\"id\":40,\"method\":\"resources/list\",\"params\":{}}"));
                var risorse = Inside(Inside(resourceList, "result"), "resources") as object[];
                Check("resources/list da' il catalogo",
                      risorse != null && risorse.Length == 1 &&
                      Convert.ToString(Inside(risorse[0], "uri")) == "catalog://operations",
                      risorse == null ? "(niente)" : risorse.Length + ": " +
                          string.Join(", ", risorse.Select(x => Convert.ToString(Inside(x, "uri")))));

                var models = Obj(Ask(p, 41,
                    "{\"jsonrpc\":\"2.0\",\"id\":41,\"method\":\"resources/templates/list\",\"params\":{}}"));
                var schemas = Inside(Inside(models, "result"), "resourceTemplates") as object[];
                Check("resources/templates/list da' il modello per una operazione",
                      schemas != null && schemas.Length == 1 &&
                      Convert.ToString(Inside(schemas[0], "uriTemplate"))
                          == "catalog://operation/{name}",
                      schemas == null ? "(niente)" : schemas.Length + ": " +
                          string.Join(", ", schemas.Select(x => Convert.ToString(Inside(x, "uriTemplate")))));

                // ── E QUESTA E' LA PROVA VERA: **LE DUE PORTE, UNA COSA** ──
                //
                // La risorsa e lo strumento devono rispondere lo STESSO testo.
                // Se un giorno qualcuno scrive l'elenco «anche» dentro
                // `Risorse`, qui diventa rosso — ed e' l'unico posto che se ne
                // accorgerebbe, perche' le due strade non si incontrano mai da
                // sole. *Due porte sulla stessa cosa diventano due cose il
                // giorno in cui una delle due si scrive a parte.*
                var reading = Obj(Ask(p, 42,
                    "{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"resources/read\",\"params\":" +
                    "{\"uri\":\"catalog://operations\"}}"));
                var contents = Inside(Inside(reading, "result"), "contents") as object[];
                var resourceText = contents == null || contents.Length == 0
                    ? null : Convert.ToString(Inside(contents[0], "text"));
                Check("resources/read torna il testo del catalogo",
                      !string.IsNullOrWhiteSpace(resourceText) &&
                      resourceText.IndexOf("\"families\"", StringComparison.Ordinal) >= 0,
                      Short(resourceText));

                var fromTool = Obj(Ask(p, 43,
                    "{\"jsonrpc\":\"2.0\",\"id\":43,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"list_operations\",\"arguments\":{}}}"));
                var parts = Inside(Inside(fromTool, "result"), "content") as object[];
                var toolText = parts == null || parts.Length == 0
                    ? null : Convert.ToString(Inside(parts[0], "text"));
                Check("la risorsa e lo strumento dicono LA STESSA COSA",
                      !string.IsNullOrWhiteSpace(resourceText) &&
                      !string.IsNullOrWhiteSpace(toolText) &&
                      string.Equals(resourceText, toolText, StringComparison.Ordinal),
                      resourceText == null || toolText == null
                          ? "(una delle due e' vuota)"
                          : resourceText.Length + " caratteri contro " + toolText.Length);

                // ── UN URI CHE NON ESISTE DICE COSA ESISTE ────────────────
                //
                // Il caso rosso: senza, «non trovato» sarebbe un vicolo cieco.
                var missing = Obj(Ask(p, 44,
                    "{\"jsonrpc\":\"2.0\",\"id\":44,\"method\":\"resources/read\",\"params\":" +
                    "{\"uri\":\"catalog://operation/questa_non_esiste\"}}"));
                var uriError = Inside(missing, "error") as Dictionary<string, object>;
                var messageUri = uriError == null
                    ? null : Convert.ToString(Inside(uriError, "message"));
                Check("un URI sconosciuto e' un errore, non una risposta vuota",
                      uriError != null, uriError == null ? "(ha risposto ok)" : Short(messageUri));
                Check("e l'errore dice quali URI ci sono",
                      messageUri != null &&
                      messageUri.IndexOf("catalog://operations", StringComparison.Ordinal) >= 0,
                      Short(messageUri));

                // ── JSON MALFORMATO: -32700, id nullo ─────────────────────
                Write(p, "{questo non e' json");
                var crooked = Obj(Line(p, TimeoutMs));
                var error = Inside(crooked, "error") as Dictionary<string, object>;
                Check("JSON malformato da' -32700",
                      error != null && Convert.ToString(Inside(error, "code")) == "-32700",
                      Convert.ToString(Inside(error, "code")));
                Check("JSON malformato risponde con id nullo",
                      crooked != null && crooked.ContainsKey("id") && crooked["id"] == null,
                      crooked == null ? "(niente)" : Convert.ToString(crooked["id"]));

                // ── SU STDOUT NON C'E' NIENTE CHE NON SIA JSON ───────────
                //
                // LA TRAPPOLA NUMERO UNO DEI SERVER MCP SU STDIO: una riga di
                // log finita su stdout rompe il cliente, che legge JSON riga
                // per riga. E' dichiarata due volte nei commenti del ponte e
                // non l'ha mai provata nessuno.
                Check("su stdout ogni riga e' JSON",
                      p.LinesNotJson.Count == 0,
                      p.LinesNotJson.Count == 0
                          ? p.LinesRead + " righe, tutte JSON"
                          : "righe non JSON: " + string.Join(" | ", p.LinesNotJson.Take(3)));
                Check("su stdout ci sono state righe da leggere", p.LinesRead > 0,
                      p.LinesRead + " righe");

                Close(p);
            }

            WithoutCatalogMeasureCloses(bridge);
        }

        /// <summary>
        /// IL CASO ROSSO DEL CANCELLO DI `measure`.
        ///
        /// `measure` decide guardando la FAMIGLIA dell'operazione, e la famiglia
        /// la sa solo il catalogo. Se il catalogo non si carica, ogni famiglia
        /// e' `null`, **ogni passo passerebbe il controllo**, e uno strumento
        /// che si e' dichiarato di sola lettura — per cui il client ha gia'
        /// deciso di non chiedere conferma — eseguirebbe un'estrusione.
        ///
        /// E' la forma peggiore del difetto che non da' errore: **la difesa
        /// sparisce proprio quando qualcosa e' gia' andato storto**, e il
        /// sintomo e' che tutto funziona.
        ///
        /// Si provoca sul serio, con `OPENPDM_CATALOGO` puntata a un file che
        /// non c'e' — la stessa variabile che userebbe chi installa il pacchetto
        /// altrove. *Un controllo mai visto scattare non e' un controllo.*
        /// </summary>
        private static void WithoutCatalogMeasureCloses(string bridge)
        {
            Group("il ponte senza catalogo");
            using (var p = OpenWithBrokenCatalog(bridge))
            {
                if (p == null) { Check("il ponte parte anche senza catalogo", false, ""); return; }
                Check("il ponte parte anche senza catalogo", true, "");

                Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"prove\",\"version\":\"1\"}}}");
                Write(p, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");

                var r = Obj(Ask(p, 9,
                    "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"measure\",\"arguments\":{\"steps\":[" +
                    "{\"operation\":\"extrude_rectangle\",\"arguments\":{}}]}}}"));
                var its = Inside(r, "result") as Dictionary<string, object>;
                Check("senza catalogo «measure» NON esegue",
                      its != null && its.ContainsKey("isError") &&
                      Convert.ToString(its["isError"]).Equals("True", StringComparison.OrdinalIgnoreCase),
                      Short(TextOf(its)));
                Check("e dice che e' colpa del catalogo, non un errore qualunque",
                      TextOf(its).IndexOf("catalogo", StringComparison.OrdinalIgnoreCase) >= 0,
                      Short(TextOf(its)));

                Close(p);
            }
        }

    }
}
