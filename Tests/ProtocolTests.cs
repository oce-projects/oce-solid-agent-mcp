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
    /// THE MCP PROTOCOL, talking to the bridge as a customer would.
    ///
    /// `oce-solid-agent-mcp.exe` is launched as a subprocess and lines are given
    /// to it on stdin. Everything runs **without SOLIDWORKS**, because the bridge starts
    /// anyway and says why afterwards: it is a property of the bridge, and it is also
    /// the first check of this group.
    ///
    /// ── THE TIMEOUT IS NOT A CONVENIENCE ──────────────────────────────────
    ///
    /// Reading from a subprocess without blocking requires **a timeout on every
    /// line awaited**: without it, a check that fails HANGS the command instead
    /// of turning red, and in a CI that means a job that never ends
    /// instead of a red that can be read.
    ///
    /// And the silent defect to fear is the other one: **a protocol check
    /// that «passes» because it read zero lines waiting for zero lines.** For
    /// this reason every wait says how many lines it wants, and zero lines is never a
    /// green.
    /// </summary>
    internal static class ProtocolTests
    {
        // THE FIRST CALL COSTS MORE THAN THE OTHERS: on `initialize` the bridge
        // attaches itself to SOLIDWORKS, and if SOLIDWORKS is there and busy that
        // line may be very late. Twenty seconds were not enough, and the
        // symptom was misleading: `initialize` appeared to have no answer and
        // then its line landed on the next check.
        // TimeoutMs is in Shared/McpClient.cs: two use it.

        internal static void All()
        {
            Group("il protocollo MCP");

            var bridge = BridgeOrNotHere("il protocollo MCP, per intero");
            if (bridge == null) return;

            using (var p = Open(bridge))
            {
                if (p == null) { Check("il ponte parte", false, "non e' partito"); return; }
                Check("il ponte parte anche senza SOLIDWORKS", true, "");

                // ── initialize SENDS BACK THE VERSION RECEIVED ────────────────
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

                // ── AND THE VERSION IT ANNOUNCES IS THE REAL ONE ─────────────
                //
                // It was not: `Program.cs` held `const Versione =
                // "0.2.0"` while `VERSION.txt` said 0.80.0, and it stayed
                // that way for seventy-eight minor versions. There was nothing
                // wrong in the code — there were TWO places to write
                // the same number, and one of the two nobody reread.
                //
                // The remedy is a single source (`VERSION.txt` inside
                // the executable). This line is what stops them
                // from becoming two again.
                var expected = RepositoryVersion();
                var info = Inside(result, "serverInfo") as Dictionary<string, object>;
                var called = Convert.ToString(Inside(info, "version"));
                Check("e la versione che il ponte annuncia e' quella di VERSION.txt",
                      expected != null && called == expected,
                      expected == null
                          ? "VERSION.txt non si legge"
                          : "annunciata «" + called + "», VERSION.txt dice «" + expected + "»");

                // ── A NOTIFICATION RECEIVES NO ANSWER ──────────────────────
                //
                // A line without `id` is a notification, and answering a
                // notification is a violation that no customer reports.
                //
                // IT IS NOT MEASURED WITH AN EMPTY WAIT. «Nothing arrived
                // in a second and a half» is true also when the bridge is
                // simply slow, and it is the silent defect of this
                // group: a check that passes because it read zero lines waiting
                // for zero lines. The notification is sent, RIGHT AFTER a
                // real question, and we look that the FIRST line that comes back is
                // the answer to that one: if the notification had had an answer,
                // it would have arrived first.
                Write(p, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                Write(p, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");

                // THE BRIDGE'S NOTIFICATIONS ARE SKIPPED, not the answers.
                //
                // Since 0.58.0 the bridge sends `notifications/message` (entry
                // W2.8), and one of those may arrive between the two lines. It has
                // nothing to do with what this check measures: the
                // question is «is the first ANSWER the one to the request with
                // id, or is there an answer to the notification in front?».
                //
                // The check however is NOT widened to «ignore everything that has no
                // id»: only `notifications/message` is skipped, which is stuff
                // of ours and recognizable. If the bridge started sending
                // something else without id, this would notice — and that is the
                // point.
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
                // SIX SINCE 0.57.0, not five: `measure` was born (entry W2.3).
                // SEVEN SINCE 0.87.0: `document` was born (entries Z-B1/B2/B5/B6),
                // and this line was changed on purpose, not found red.
                // The number is asserted EXACT and not «at least»: one more tool
                // that appears without anybody wanting it is a
                // piece of public surface that appeared by mistake, and this is
                // the only place that notices.
                // NINE SINCE 0.112.0: api_search and api_signature (entry
                // U1.1). Changed on purpose, like the seventh.
                Check("tools/list gives exactly nine tools",
                      tools != null && tools.Length == 9,
                      tools == null ? "(niente)" : tools.Length + " strumenti: " +
                          string.Join(", ", tools.Select(s => Convert.ToString(Inside(s, "name")))));

                // ── THE ANNOTATIONS TELL THE TRUTH ───────────────────────
                //
                // They are the only thing the client reads BEFORE calling, and
                // they decide whether it stops to ask for confirmation. Getting them wrong in the
                // convenient direction — an operation that removes material declared
                // harmless — is the defect that gives no error, moved from the part
                // to the protocol.
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

                // «document» changes the document in front: it is NOT read-only,
                // even if it does not touch the geometry. A client that
                // believed it read-only would call it without asking, and the
                // next plan would write into a different part from the one
                // the person working has in front. And it is NOT idempotent: «create» twice
                // makes two documents.
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

                // `title` AT THE FIRST LEVEL, not only inside `annotations`.
                // In the recent revisions of the spec it is on the tool; a
                // new client that reads only there would show the technical name
                // `inspect_part` to whoever is working.
                bool titlesOk = tools != null && tools.All(s =>
                    !string.IsNullOrWhiteSpace(Convert.ToString(Inside(s, "title"))));
                Check("ogni strumento ha un «title» di primo livello", titlesOk,
                      tools == null ? "(niente)" : string.Join(", ",
                          tools.Select(s => Convert.ToString(Inside(s, "title")))));

                // ── A TOOL THAT IS NOT THERE ─────────────────────────────
                //
                // `isError: true` INSIDE THE CONTENT, not a JSON-RPC error:
                // the distinction is written in `Mcp/Protocol.cs` and until
                // today nobody verified it. A protocol error says
                // «the client got the talking wrong»; this one says «the
                // tool does not exist», and it is something the model must
                // be able to read and correct by itself.
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

                // THE LIST IN THE MESSAGE IS THE REAL ONE, not an aged
                // copy. The «I do not know it» message names the
                // tools written by hand in `ToolNames`; here it is
                // compared with the ones `tools/list` has just declared.
                // If tomorrow a seventh tool is born and nobody updates
                // that line, the model that got the name wrong receives a
                // list in which the seventh is not there — and concludes that it does not
                // exist. An incomplete list is worse than no list.
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

                // ── «measure» REFUSES WHAT TOUCHES THE PART ───────────────
                //
                // It is the criterion of entry W2.3, and it must be tested WITH THE RED CASE:
                // a tool that declares itself read-only and then accepts
                // an extrusion is not a convenience, it is a lie in the protocol —
                // and the client has already decided not to ask for confirmation on the
                // basis of that declaration.
                //
                // This refusal arrives WITHOUT SOLIDWORKS: the gate looks at
                // the families before touching the session. Therefore it can be tested
                // here, where SOLIDWORKS is not there.
                var touches = Obj(Ask(p, 4,
                    "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"measure\",\"arguments\":{\"steps\":[" +
                    "{\"operation\":\"extrude_rectangle\",\"arguments\":{}}]}}}"));
                var itsTouch = Inside(touches, "result") as Dictionary<string, object>;
                Check("«measure» rifiuta «extrude_rectangle»",
                      itsTouch != null && itsTouch.ContainsKey("isError") &&
                      Convert.ToString(itsTouch["isError"]).Equals("True", StringComparison.OrdinalIgnoreCase),
                      Short(TextOf(itsTouch)));
                // AND IT SAYS SO WITH THE CRITERION, not with a verdict: whoever reads must
                // know WITH WHICH tool it is done, otherwise they must guess.
                Check("il rifiuto dice con quale strumento si fa",
                      TextOf(itsTouch).IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0,
                      Short(TextOf(itsTouch)));
                Check("il rifiuto dice che non ha eseguito niente",
                      TextOf(itsTouch).IndexOf("niente", StringComparison.OrdinalIgnoreCase) >= 0,
                      Short(TextOf(itsTouch)));

                // ── THE DATA ALSO COME OUT IN A READABLE FORM (entry W2.4) ──
                //
                // `list_operations` does not touch SOLIDWORKS, so it is the only
                // tool with real data that can be tested here. The text
                // must stay — the clients that read only `content` are the
                // majority — and `structuredContent` must be there NEXT TO it.
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
                // NO `outputSchema`, and it is a choice: whoever declares it MUST
                // respect it, and the shape of the state changes with the document. A
                // partial schema would make correct answers be rejected. If one
                // day it appeared without anybody having decided the shape,
                // this check says so.
                bool conSchema = tools != null && tools.Any(
                    s => Inside(s, "outputSchema") != null);
                Check("nessuno strumento dichiara un «outputSchema» a meta'", !conSchema,
                      conSchema
                          ? "qualcuno lo dichiara: o la forma e' stabile e va scritto tutto, o va tolto"
                          : "nessuno, come deciso");

                // ── THE LEVEL OF NARRATION CAN BE ASKED FOR (entry W2.8) ────
                //
                // And the red case counts more than the green one: a level that does not
                // exist must be an ERROR. Accepting it and ignoring it
                // would leave the client convinced it had raised the threshold while
                // the messages keep arriving — or, worse, convinced it
                // hears everything while it no longer hears anything.
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

                // ── THE RESOURCES (entry W2.12) ──────────────────────────────
                //
                // The catalogue can be ATTACHED instead of asked for. Three things
                // to test, and the third is the one that counts.
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

                // ── AND THIS IS THE REAL CHECK: **THE TWO DOORS, ONE THING** ──
                //
                // The resource and the tool must answer the SAME text.
                // If one day somebody writes the list «also» inside
                // `Risorse`, here it turns red — and it is the only place that
                // would notice, because the two roads never meet by
                // themselves. *Two doors on the same thing become two things the
                // day one of the two is written separately.*
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

                // ── A URI THAT DOES NOT EXIST SAYS WHAT EXISTS ───────────────
                //
                // The red case: without it, «not found» would be a dead end.
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

                // ── ON STDOUT THERE IS NOTHING THAT IS NOT JSON ─────────────
                //
                // THE NUMBER ONE TRAP OF MCP SERVERS OVER STDIO: a log line
                // that ends up on stdout breaks the customer, who reads JSON line
                // by line. It is declared twice in the comments of the bridge and
                // nobody has ever tested it.
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
        /// THE RED CASE OF THE `measure` GATE.
        ///
        /// `measure` decides by looking at the FAMILY of the operation, and the family
        /// is known only to the catalogue. If the catalogue does not load, every family
        /// is `null`, **every step would pass the check**, and a tool
        /// that declared itself read-only — so the client has already
        /// decided not to ask for confirmation — would execute an extrusion.
        ///
        /// It is the worst form of the defect that gives no error: **the defense
        /// disappears exactly when something has already gone wrong**, and the
        /// symptom is that everything works.
        ///
        /// It is provoked for real, with `OPENPDM_CATALOGO` pointed at a file that
        /// is not there — the same variable that whoever installs the package
        /// elsewhere would use. *A check never seen triggering is not a check.*
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
