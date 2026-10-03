using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static Oce.Solid.Agent.ContractTests.Programma;
using static Shared.ClienteMcp;

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
    internal static class ProveProtocollo
    {
        // LA PRIMA CHIAMATA COSTA PIU' DELLE ALTRE: il ponte all'`initialize`
        // si aggancia a SOLIDWORKS, e se SOLIDWORKS c'e' ed e' occupato quella
        // riga puo' tardare parecchio. Venti secondi non bastavano, e il
        // sintomo era ingannevole: l'`initialize` risultava senza risposta e
        // poi la sua riga arrivava addosso alla prova dopo.
        // AttesaMs sta in Shared/McpClient.cs: lo usano in due.

        internal static void Tutte()
        {
            Gruppo("il protocollo MCP");

            var ponte = PonteOppureNonQui("il protocollo MCP, per intero");
            if (ponte == null) return;

            using (var p = Apri(ponte))
            {
                if (p == null) { Segna("il ponte parte", false, "non e' partito"); return; }
                Segna("il ponte parte anche senza SOLIDWORKS", true, "");

                // ── initialize RIMANDA LA VERSIONE RICEVUTA ───────────────
                var r = Chiedi(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"prove\",\"version\":\"1\"}}}");
                Segna("initialize risponde", r != null,
                      r != null ? Corto(r)
                                : "niente entro " + (AttesaMs / 1000) + "s. Righe viste finora: " +
                                  string.Join(" | ", p.Viste.Take(3)));
                var iniz = Ogg(r);
                var risultato = Dentro(iniz, "result") as Dictionary<string, object>;
                Segna("initialize rimanda la versione di protocollo ricevuta",
                      risultato != null &&
                      Convert.ToString(Dentro(risultato, "protocolVersion")) == "2024-11-05",
                      Convert.ToString(Dentro(risultato, "protocolVersion")));

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
                var atteso = VersioneDelRepository();
                var info = Dentro(risultato, "serverInfo") as Dictionary<string, object>;
                var detta = Convert.ToString(Dentro(info, "version"));
                Segna("e la versione che il ponte annuncia e' quella di VERSION.txt",
                      atteso != null && detta == atteso,
                      atteso == null
                          ? "VERSION.txt non si legge"
                          : "annunciata «" + detta + "», VERSION.txt dice «" + atteso + "»");

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
                Scrivi(p, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                Scrivi(p, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");

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
                string prima = null;
                Dictionary<string, object> oggPrima = null;
                var finoA = Environment.TickCount + AttesaMs;
                while (Environment.TickCount < finoA)
                {
                    prima = Riga(p, Math.Max(200, finoA - Environment.TickCount));
                    if (prima == null) break;
                    oggPrima = Ogg(prima);
                    var metodo = oggPrima == null ? null : Convert.ToString(Dentro(oggPrima, "method"));
                    if (metodo != "notifications/message") break;
                }
                Segna("una notifica NON riceve risposta",
                      oggPrima != null && oggPrima.ContainsKey("id") &&
                      Convert.ToString(oggPrima["id"]) == "2",
                      Corto(prima));

                // ── tools/list ────────────────────────────────────────────
                var lista = oggPrima != null && Convert.ToString(Dentro(oggPrima, "id")) == "2"
                    ? oggPrima
                    : Ogg(Chiedi(p, 2,
                        "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}"));
                var strumenti = Dentro(Dentro(lista, "result"), "tools") as object[];
                // SEI DALLA 0.57.0, non cinque: e' nato `measure` (voce W2.3).
                // SETTE DALLA 0.87.0: e' nato `document` (voci Z-B1/B2/B5/B6),
                // e questa riga e' stata cambiata apposta, non trovata rossa.
                // Il numero si assertisce ESATTO e non «almeno»: uno strumento
                // in piu' che appare senza che nessuno l'abbia voluto e' un
                // pezzo di superficie pubblica comparso per sbaglio, e questo e'
                // il solo posto che se ne accorge.
                // NINE SINCE 0.112.0: api_search and api_signature (entry
                // U1.1). Changed on purpose, like the seventh.
                Segna("tools/list gives exactly nine tools",
                      strumenti != null && strumenti.Length == 9,
                      strumenti == null ? "(niente)" : strumenti.Length + " strumenti: " +
                          string.Join(", ", strumenti.Select(s => Convert.ToString(Dentro(s, "name")))));

                // ── LE ANNOTAZIONI DICONO LA VERITA' ──────────────────────
                //
                // Sono l'unica cosa che il client legge PRIMA di chiamare, e
                // decidono se si ferma a chiedere conferma. Sbagliarle nel verso
                // comodo — un'operazione che toglie materiale dichiarata
                // innocua — e' il difetto che non da' errore, spostato dal pezzo
                // al protocollo.
                Func<string, string, string> ann = (nomeStrumento, campo) =>
                {
                    var s = strumenti?.FirstOrDefault(
                        x => Convert.ToString(Dentro(x, "name")) == nomeStrumento);
                    return s == null ? null
                        : Convert.ToString(Dentro(Dentro(s, "annotations"), campo));
                };
                Segna("«measure» si dichiara di sola lettura",
                      string.Equals(ann("measure", "readOnlyHint"), "True",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("measure", "readOnlyHint") ?? "(manca)");
                Segna("«measure» NON si dichiara distruttivo",
                      string.Equals(ann("measure", "destructiveHint"), "False",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("measure", "destructiveHint") ?? "(manca)");
                Segna("«build» resta distruttivo",
                      string.Equals(ann("build", "destructiveHint"), "True",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("build", "destructiveHint") ?? "(manca)");
                Segna("«build» NON si dichiara idempotente",
                      string.Equals(ann("build", "idempotentHint"), "False",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("build", "idempotentHint") ?? "(manca)");

                // «document» cambia il documento davanti: NON e' di sola
                // lettura, anche se non tocca la geometria. Un client che lo
                // credesse di sola lettura lo chiamerebbe senza chiedere, e il
                // piano successivo scriverebbe in un pezzo diverso da quello
                // che chi lavora ha davanti. E NON e' idempotente: «create» due
                // volte fa due documenti.
                Segna("«document» NON si dichiara di sola lettura",
                      string.Equals(ann("document", "readOnlyHint"), "False",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("document", "readOnlyHint") ?? "(manca)");
                Segna("«document» NON si dichiara idempotente",
                      string.Equals(ann("document", "idempotentHint"), "False",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("document", "idempotentHint") ?? "(manca)");
                Segna("«document» NON si dichiara distruttivo: non chiude e non butta niente",
                      string.Equals(ann("document", "destructiveHint"), "False",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("document", "destructiveHint") ?? "(manca)");

                bool schemiOk = strumenti != null && strumenti.All(s =>
                {
                    var schema = Dentro(s, "inputSchema") as Dictionary<string, object>;
                    return schema != null && Convert.ToString(Dentro(schema, "type")) == "object";
                });
                Segna("ogni strumento ha un inputSchema di tipo object", schemiOk);

                bool nomiOk = strumenti != null && strumenti.All(s =>
                    !string.IsNullOrWhiteSpace(Convert.ToString(Dentro(s, "name"))) &&
                    !string.IsNullOrWhiteSpace(Convert.ToString(Dentro(s, "description"))));
                Segna("ogni strumento ha nome e descrizione", nomiOk);

                // `title` DI PRIMO LIVELLO, non solo dentro `annotations`.
                // Nelle revisioni recenti dello spec sta sullo strumento; un
                // client nuovo che legge solo li' mostrerebbe il nome tecnico
                // `inspect_part` a chi sta lavorando.
                bool titoliOk = strumenti != null && strumenti.All(s =>
                    !string.IsNullOrWhiteSpace(Convert.ToString(Dentro(s, "title"))));
                Segna("ogni strumento ha un «title» di primo livello", titoliOk,
                      strumenti == null ? "(niente)" : string.Join(", ",
                          strumenti.Select(s => Convert.ToString(Dentro(s, "title")))));

                // ── UNO STRUMENTO CHE NON C'E' ────────────────────────────
                //
                // `isError: true` DENTRO IL CONTENUTO, non un errore JSON-RPC:
                // la distinzione e' scritta in `Mcp/Protocol.cs` e fino a
                // oggi non la verificava nessuno. Un errore di protocollo dice
                // «il cliente ha sbagliato a parlare»; questo dice «lo
                // strumento non esiste», ed e' una cosa che il modello deve
                // poter leggere e correggere da solo.
                var sconosciuto = Ogg(Chiedi(p, 3,
                    "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"strumento_che_non_esiste\",\"arguments\":{}}}"));
                Segna("uno strumento sconosciuto NON e' un errore JSON-RPC",
                      sconosciuto != null && !sconosciuto.ContainsKey("error"),
                      Corto(Convert.ToString(Dentro(sconosciuto, "error"))));
                var suo = Dentro(sconosciuto, "result") as Dictionary<string, object>;
                Segna("uno strumento sconosciuto da' isError: true nel contenuto",
                      suo != null && suo.ContainsKey("isError") &&
                      Convert.ToString(suo["isError"]).Equals("True", StringComparison.OrdinalIgnoreCase));

                // L'ELENCO NEL MESSAGGIO E' QUELLO VERO, non una copia
                // invecchiata. Il messaggio di «non lo conosco» nomina gli
                // strumenti scritti a mano in `NomiDegliStrumenti`; qui si
                // confronta con quelli che `tools/list` ha appena dichiarato.
                // Se domani nasce un settimo strumento e nessuno aggiorna
                // quella riga, il modello che ha sbagliato nome riceve un
                // elenco in cui il settimo non c'e' — e conclude che non
                // esiste. Un elenco incompleto e' peggio di nessun elenco.
                var testoSconosciuto = TestoDi(suo);
                var mancanti = strumenti == null ? new List<string>() : strumenti
                    .Select(s => Convert.ToString(Dentro(s, "name")))
                    .Where(n => !string.IsNullOrWhiteSpace(n) &&
                                testoSconosciuto.IndexOf(n, StringComparison.Ordinal) < 0)
                    .ToList();
                Segna("il messaggio elenca TUTTI gli strumenti che tools/list dichiara",
                      mancanti.Count == 0,
                      mancanti.Count == 0
                          ? "tutti e " + (strumenti == null ? 0 : strumenti.Length)
                          : "non nominati: " + string.Join(", ", mancanti));

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
                var tocca = Ogg(Chiedi(p, 4,
                    "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"measure\",\"arguments\":{\"steps\":[" +
                    "{\"operation\":\"extrude_rectangle\",\"arguments\":{}}]}}}"));
                var suoTocca = Dentro(tocca, "result") as Dictionary<string, object>;
                Segna("«measure» rifiuta «extrude_rectangle»",
                      suoTocca != null && suoTocca.ContainsKey("isError") &&
                      Convert.ToString(suoTocca["isError"]).Equals("True", StringComparison.OrdinalIgnoreCase),
                      Corto(TestoDi(suoTocca)));
                // E LO DICE COL CRITERIO, non con un verdetto: chi legge deve
                // sapere CON QUALE strumento si fa, altrimenti deve indovinare.
                Segna("il rifiuto dice con quale strumento si fa",
                      TestoDi(suoTocca).IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0,
                      Corto(TestoDi(suoTocca)));
                Segna("il rifiuto dice che non ha eseguito niente",
                      TestoDi(suoTocca).IndexOf("niente", StringComparison.OrdinalIgnoreCase) >= 0,
                      Corto(TestoDi(suoTocca)));

                // ── I DATI ESCONO ANCHE IN FORMA LEGGIBILE (voce W2.4) ───
                //
                // `list_operations` non tocca SOLIDWORKS, quindi e' l'unico
                // strumento con dati veri che si possa provare qui. Il testo
                // deve restare — i client che leggono solo `content` sono la
                // maggioranza — e `structuredContent` deve esserci ACCANTO.
                var elenco = Ogg(Chiedi(p, 5,
                    "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"list_operations\",\"arguments\":{}}}"));
                var suoElenco = Dentro(elenco, "result") as Dictionary<string, object>;
                Segna("il testo c'e' ancora", TestoDi(suoElenco).Length > 0,
                      Corto(TestoDi(suoElenco)));
                Segna("e accanto c'e' «structuredContent»",
                      suoElenco != null && suoElenco.ContainsKey("structuredContent") &&
                      suoElenco["structuredContent"] != null,
                      suoElenco != null && suoElenco.ContainsKey("structuredContent")
                          ? "c'e'" : "manca");
                // NIENTE `outputSchema`, ed e' una scelta: chi lo dichiara DEVE
                // rispettarlo, e la forma dello stato cambia col documento. Uno
                // schema parziale farebbe rifiutare risposte giuste. Se un
                // giorno comparisse senza che nessuno abbia deciso la forma,
                // questo controllo lo dice.
                bool conSchema = strumenti != null && strumenti.Any(
                    s => Dentro(s, "outputSchema") != null);
                Segna("nessuno strumento dichiara un «outputSchema» a meta'", !conSchema,
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
                var livelloOk = Ogg(Chiedi(p, 6,
                    "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"logging/setLevel\"," +
                    "\"params\":{\"level\":\"warning\"}}"));
                Segna("logging/setLevel accetta un livello vero",
                      livelloOk != null && livelloOk.ContainsKey("result") &&
                      !livelloOk.ContainsKey("error"),
                      Corto(Convert.ToString(Dentro(livelloOk, "error"))));

                var livelloNo = Ogg(Chiedi(p, 7,
                    "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"logging/setLevel\"," +
                    "\"params\":{\"level\":\"fortissimo\"}}"));
                var erroreLivello = Dentro(livelloNo, "error") as Dictionary<string, object>;
                Segna("logging/setLevel RIFIUTA un livello inventato",
                      erroreLivello != null,
                      erroreLivello == null ? "(l'ha accettato)" : "-32602");
                Segna("e dice quali sono i livelli veri",
                      erroreLivello != null &&
                      Convert.ToString(Dentro(erroreLivello, "message"))
                             .IndexOf("emergency", StringComparison.Ordinal) >= 0,
                      Corto(Convert.ToString(Dentro(erroreLivello, "message"))));

                // ── LE RISORSE (voce W2.12) ───────────────────────────────
                //
                // Il catalogo si puo' ATTACCARE invece che chiedere. Tre cose
                // da provare, e la terza e' quella che conta.
                var elencoRisorse = Ogg(Chiedi(p, 40,
                    "{\"jsonrpc\":\"2.0\",\"id\":40,\"method\":\"resources/list\",\"params\":{}}"));
                var risorse = Dentro(Dentro(elencoRisorse, "result"), "resources") as object[];
                Segna("resources/list da' il catalogo",
                      risorse != null && risorse.Length == 1 &&
                      Convert.ToString(Dentro(risorse[0], "uri")) == "catalog://operations",
                      risorse == null ? "(niente)" : risorse.Length + ": " +
                          string.Join(", ", risorse.Select(x => Convert.ToString(Dentro(x, "uri")))));

                var modelli = Ogg(Chiedi(p, 41,
                    "{\"jsonrpc\":\"2.0\",\"id\":41,\"method\":\"resources/templates/list\",\"params\":{}}"));
                var schemi = Dentro(Dentro(modelli, "result"), "resourceTemplates") as object[];
                Segna("resources/templates/list da' il modello per una operazione",
                      schemi != null && schemi.Length == 1 &&
                      Convert.ToString(Dentro(schemi[0], "uriTemplate"))
                          == "catalog://operation/{name}",
                      schemi == null ? "(niente)" : schemi.Length + ": " +
                          string.Join(", ", schemi.Select(x => Convert.ToString(Dentro(x, "uriTemplate")))));

                // ── E QUESTA E' LA PROVA VERA: **LE DUE PORTE, UNA COSA** ──
                //
                // La risorsa e lo strumento devono rispondere lo STESSO testo.
                // Se un giorno qualcuno scrive l'elenco «anche» dentro
                // `Risorse`, qui diventa rosso — ed e' l'unico posto che se ne
                // accorgerebbe, perche' le due strade non si incontrano mai da
                // sole. *Due porte sulla stessa cosa diventano due cose il
                // giorno in cui una delle due si scrive a parte.*
                var lettura = Ogg(Chiedi(p, 42,
                    "{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"resources/read\",\"params\":" +
                    "{\"uri\":\"catalog://operations\"}}"));
                var contenuti = Dentro(Dentro(lettura, "result"), "contents") as object[];
                var testoRisorsa = contenuti == null || contenuti.Length == 0
                    ? null : Convert.ToString(Dentro(contenuti[0], "text"));
                Segna("resources/read torna il testo del catalogo",
                      !string.IsNullOrWhiteSpace(testoRisorsa) &&
                      testoRisorsa.IndexOf("\"families\"", StringComparison.Ordinal) >= 0,
                      Corto(testoRisorsa));

                var daStrumento = Ogg(Chiedi(p, 43,
                    "{\"jsonrpc\":\"2.0\",\"id\":43,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"list_operations\",\"arguments\":{}}}"));
                var pezzi = Dentro(Dentro(daStrumento, "result"), "content") as object[];
                var testoStrumento = pezzi == null || pezzi.Length == 0
                    ? null : Convert.ToString(Dentro(pezzi[0], "text"));
                Segna("la risorsa e lo strumento dicono LA STESSA COSA",
                      !string.IsNullOrWhiteSpace(testoRisorsa) &&
                      !string.IsNullOrWhiteSpace(testoStrumento) &&
                      string.Equals(testoRisorsa, testoStrumento, StringComparison.Ordinal),
                      testoRisorsa == null || testoStrumento == null
                          ? "(una delle due e' vuota)"
                          : testoRisorsa.Length + " caratteri contro " + testoStrumento.Length);

                // ── UN URI CHE NON ESISTE DICE COSA ESISTE ────────────────
                //
                // Il caso rosso: senza, «non trovato» sarebbe un vicolo cieco.
                var mancante = Ogg(Chiedi(p, 44,
                    "{\"jsonrpc\":\"2.0\",\"id\":44,\"method\":\"resources/read\",\"params\":" +
                    "{\"uri\":\"catalog://operation/questa_non_esiste\"}}"));
                var erroreUri = Dentro(mancante, "error") as Dictionary<string, object>;
                var messaggioUri = erroreUri == null
                    ? null : Convert.ToString(Dentro(erroreUri, "message"));
                Segna("un URI sconosciuto e' un errore, non una risposta vuota",
                      erroreUri != null, erroreUri == null ? "(ha risposto ok)" : Corto(messaggioUri));
                Segna("e l'errore dice quali URI ci sono",
                      messaggioUri != null &&
                      messaggioUri.IndexOf("catalog://operations", StringComparison.Ordinal) >= 0,
                      Corto(messaggioUri));

                // ── JSON MALFORMATO: -32700, id nullo ─────────────────────
                Scrivi(p, "{questo non e' json");
                var storto = Ogg(Riga(p, AttesaMs));
                var errore = Dentro(storto, "error") as Dictionary<string, object>;
                Segna("JSON malformato da' -32700",
                      errore != null && Convert.ToString(Dentro(errore, "code")) == "-32700",
                      Convert.ToString(Dentro(errore, "code")));
                Segna("JSON malformato risponde con id nullo",
                      storto != null && storto.ContainsKey("id") && storto["id"] == null,
                      storto == null ? "(niente)" : Convert.ToString(storto["id"]));

                // ── SU STDOUT NON C'E' NIENTE CHE NON SIA JSON ───────────
                //
                // LA TRAPPOLA NUMERO UNO DEI SERVER MCP SU STDIO: una riga di
                // log finita su stdout rompe il cliente, che legge JSON riga
                // per riga. E' dichiarata due volte nei commenti del ponte e
                // non l'ha mai provata nessuno.
                Segna("su stdout ogni riga e' JSON",
                      p.RigheNonJson.Count == 0,
                      p.RigheNonJson.Count == 0
                          ? p.RigheLette + " righe, tutte JSON"
                          : "righe non JSON: " + string.Join(" | ", p.RigheNonJson.Take(3)));
                Segna("su stdout ci sono state righe da leggere", p.RigheLette > 0,
                      p.RigheLette + " righe");

                Chiudi(p);
            }

            SenzaCatalogoMisuraChiude(ponte);
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
        private static void SenzaCatalogoMisuraChiude(string ponte)
        {
            Gruppo("il ponte senza catalogo");
            using (var p = ApriConCatalogoRotto(ponte))
            {
                if (p == null) { Segna("il ponte parte anche senza catalogo", false, ""); return; }
                Segna("il ponte parte anche senza catalogo", true, "");

                Chiedi(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"prove\",\"version\":\"1\"}}}");
                Scrivi(p, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");

                var r = Ogg(Chiedi(p, 9,
                    "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"measure\",\"arguments\":{\"steps\":[" +
                    "{\"operation\":\"extrude_rectangle\",\"arguments\":{}}]}}}"));
                var suo = Dentro(r, "result") as Dictionary<string, object>;
                Segna("senza catalogo «measure» NON esegue",
                      suo != null && suo.ContainsKey("isError") &&
                      Convert.ToString(suo["isError"]).Equals("True", StringComparison.OrdinalIgnoreCase),
                      Corto(TestoDi(suo)));
                Segna("e dice che e' colpa del catalogo, non un errore qualunque",
                      TestoDi(suo).IndexOf("catalogo", StringComparison.OrdinalIgnoreCase) >= 0,
                      Corto(TestoDi(suo)));

                Chiudi(p);
            }
        }

    }
}
