using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
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
        private const int AttesaMs = 90000;

        internal static void Tutte()
        {
            Gruppo("il protocollo MCP");

            var ponte = Path.Combine(Radice(), "bin", "oce-solid-agent-mcp.exe");
            if (!File.Exists(ponte)) { Segna("il ponte c'e'", false, "non trovo " + ponte); return; }

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
                // SEI DALLA 0.57.0, non cinque: e' nato `misura` (voce W2.3).
                // Il numero si assertisce ESATTO e non «almeno»: uno strumento
                // in piu' che appare senza che nessuno l'abbia voluto e' un
                // pezzo di superficie pubblica comparso per sbaglio, e questo e'
                // il solo posto che se ne accorge.
                Segna("tools/list da' esattamente sei strumenti",
                      strumenti != null && strumenti.Length == 6,
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
                Segna("«misura» si dichiara di sola lettura",
                      string.Equals(ann("misura", "readOnlyHint"), "True",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("misura", "readOnlyHint") ?? "(manca)");
                Segna("«misura» NON si dichiara distruttivo",
                      string.Equals(ann("misura", "destructiveHint"), "False",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("misura", "destructiveHint") ?? "(manca)");
                Segna("«disegna» resta distruttivo",
                      string.Equals(ann("disegna", "destructiveHint"), "True",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("disegna", "destructiveHint") ?? "(manca)");
                Segna("«disegna» NON si dichiara idempotente",
                      string.Equals(ann("disegna", "idempotentHint"), "False",
                                    StringComparison.OrdinalIgnoreCase),
                      ann("disegna", "idempotentHint") ?? "(manca)");

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
                // `guarda_il_pezzo` a chi sta lavorando.
                bool titoliOk = strumenti != null && strumenti.All(s =>
                    !string.IsNullOrWhiteSpace(Convert.ToString(Dentro(s, "title"))));
                Segna("ogni strumento ha un «title» di primo livello", titoliOk,
                      strumenti == null ? "(niente)" : string.Join(", ",
                          strumenti.Select(s => Convert.ToString(Dentro(s, "title")))));

                // ── UNO STRUMENTO CHE NON C'E' ────────────────────────────
                //
                // `isError: true` DENTRO IL CONTENUTO, non un errore JSON-RPC:
                // la distinzione e' scritta in `Mcp/Protocollo.cs` e fino a
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

                // ── «misura» RIFIUTA CIO' CHE TOCCA IL PEZZO ─────────────
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
                    "{\"name\":\"misura\",\"arguments\":{\"passi\":[" +
                    "{\"operazione\":\"estrudi_rettangolo\",\"argomenti\":{}}]}}}"));
                var suoTocca = Dentro(tocca, "result") as Dictionary<string, object>;
                Segna("«misura» rifiuta «estrudi_rettangolo»",
                      suoTocca != null && suoTocca.ContainsKey("isError") &&
                      Convert.ToString(suoTocca["isError"]).Equals("True", StringComparison.OrdinalIgnoreCase),
                      Corto(TestoDi(suoTocca)));
                // E LO DICE COL CRITERIO, non con un verdetto: chi legge deve
                // sapere CON QUALE strumento si fa, altrimenti deve indovinare.
                Segna("il rifiuto dice con quale strumento si fa",
                      TestoDi(suoTocca).IndexOf("disegna", StringComparison.OrdinalIgnoreCase) >= 0,
                      Corto(TestoDi(suoTocca)));
                Segna("il rifiuto dice che non ha eseguito niente",
                      TestoDi(suoTocca).IndexOf("niente", StringComparison.OrdinalIgnoreCase) >= 0,
                      Corto(TestoDi(suoTocca)));

                // ── I DATI ESCONO ANCHE IN FORMA LEGGIBILE (voce W2.4) ───
                //
                // `elenco_operazioni` non tocca SOLIDWORKS, quindi e' l'unico
                // strumento con dati veri che si possa provare qui. Il testo
                // deve restare — i client che leggono solo `content` sono la
                // maggioranza — e `structuredContent` deve esserci ACCANTO.
                var elenco = Ogg(Chiedi(p, 5,
                    "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"elenco_operazioni\",\"arguments\":{}}}"));
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
        /// IL CASO ROSSO DEL CANCELLO DI `misura`.
        ///
        /// `misura` decide guardando la FAMIGLIA dell'operazione, e la famiglia
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
                    "{\"name\":\"misura\",\"arguments\":{\"passi\":[" +
                    "{\"operazione\":\"estrudi_rettangolo\",\"argomenti\":{}}]}}}"));
                var suo = Dentro(r, "result") as Dictionary<string, object>;
                Segna("senza catalogo «misura» NON esegue",
                      suo != null && suo.ContainsKey("isError") &&
                      Convert.ToString(suo["isError"]).Equals("True", StringComparison.OrdinalIgnoreCase),
                      Corto(TestoDi(suo)));
                Segna("e dice che e' colpa del catalogo, non un errore qualunque",
                      TestoDi(suo).IndexOf("catalogo", StringComparison.OrdinalIgnoreCase) >= 0,
                      Corto(TestoDi(suo)));

                Chiudi(p);
            }
        }

        // ==================================================================
        // IL SOTTOPROCESSO
        // ==================================================================
        private sealed class Ponte : IDisposable
        {
            public Process Processo;
            public readonly System.Collections.Concurrent.BlockingCollection<string> Righe =
                new System.Collections.Concurrent.BlockingCollection<string>();
            public readonly List<string> RigheNonJson = new List<string>();
            /// <summary>Tutto quello che e' arrivato, per poterlo DIRE quando
            /// una prova va rossa: «non e' arrivato niente» senza dire cos'e'
            /// arrivato invece costa un'ora a chi legge.</summary>
            public readonly List<string> Viste = new List<string>();
            public int RigheLette;

            public void Dispose()
            {
                try { if (Processo != null && !Processo.HasExited) Processo.Kill(); } catch { }
                try { Processo?.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// Il ponte, ma con il catalogo rotto di proposito.
        ///
        /// METODO A PARTE E NON UN PARAMETRO IN PIU' SU `Apri`. Aggiungere un
        /// argomento con valore predefinito e' gia' costato un banco intero
        /// (nota `80zf`: «Mancata corrispondenza nel numero dei parametri», da
        /// 88/0 a 70/58), e la riparazione che ha funzionato fu **un nome
        /// diverso**. Qui `Apri` non e' chiamato per riflessione, ma la regola
        /// si tiene lo stesso: costa una riga e toglie una categoria di
        /// sorprese.
        /// </summary>
        private static Ponte ApriConCatalogoRotto(string eseguibile)
        {
            var finto = Path.Combine(
                Path.GetTempPath(), "catalogo_che_non_esiste_" + Guid.NewGuid().ToString("N") + ".json");
            return Apri(eseguibile, finto);
        }

        private static Ponte Apri(string eseguibile)
        {
            return Apri(eseguibile, null);
        }

        private static Ponte Apri(string eseguibile, string catalogoFinto)
        {
            // ── IL BOM CHE HA ROTTO LA PRIMA RIGA ────────────────────────
            //
            // `Process.StandardInput` su .NET Framework nasce con la codifica
            // di `Console.InputEncoding`, e appena creato gli si mette
            // `AutoFlush = true` — che **fa un Flush**, e un Flush di uno
            // `StreamWriter` UTF-8 scrive il preambolo: tre byte, `EF BB BF`,
            // davanti alla prima riga.
            //
            // Il ponte ha risposto `-32700: Primitiva JSON non valida`, ed era
            // vero — quello che gli arrivava non era JSON, erano tre byte e poi
            // del JSON. Il sintomo pero' sembrava tutt'altro, «initialize non
            // risponde», perche' la riga d'errore non porta l'id della
            // richiesta: si e' visto solo stampando le righe arrivate.
            //
            // **E' la trappola numero uno dei clienti MCP su stdio**, la gemella
            // di quella che questo gruppo prova sul ponte. Si ripara prima di
            // far partire il processo, con una UTF-8 che il preambolo non ce
            // l'ha.
            try { Console.InputEncoding = new UTF8Encoding(false); } catch { }

            try
            {
                var p = new Ponte
                {
                    Processo = new Process
                    {
                        StartInfo = new ProcessStartInfo(eseguibile)
                        {
                            RedirectStandardInput = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            StandardOutputEncoding = new UTF8Encoding(false),
                            WorkingDirectory = Path.GetDirectoryName(eseguibile),
                        },
                    },
                };
                // `OPENPDM_CATALOGO` e' la stessa variabile che userebbe chi
                // installa il pacchetto altrove: puntarla a un file che non
                // c'e' e' il modo onesto di provare cosa fa il ponte quando il
                // catalogo non si carica.
                if (catalogoFinto != null)
                    p.Processo.StartInfo.EnvironmentVariables["OPENPDM_CATALOGO"] = catalogoFinto;
                p.Processo.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    p.RigheLette++;
                    p.Viste.Add(e.Data);
                    var t = e.Data.Trim();
                    // QUELLO CHE NON E' JSON SI TIENE DA PARTE, non si butta:
                    // e' il controllo, non un incidente.
                    if (t.Length > 0 && !(t.StartsWith("{") || t.StartsWith("[")))
                        p.RigheNonJson.Add(t);
                    p.Righe.Add(e.Data);
                };
                // stderr NON si guarda: e' li' che il ponte racconta, ed e'
                // giusto che ci racconti.
                p.Processo.ErrorDataReceived += (s, e) => { };
                p.Processo.Start();
                p.Processo.BeginOutputReadLine();
                p.Processo.BeginErrorReadLine();
                return p;
            }
            catch { return null; }
        }

        /// <summary>Una riga sullo stdin del ponte. Vedi `Apri` per il BOM.</summary>
        private static void Scrivi(Ponte p, string riga)
        {
            p.Processo.StandardInput.Write(riga + "\n");
            p.Processo.StandardInput.Flush();
        }

        /// <summary>
        /// Una riga, o null dopo il timeout. **Il timeout e' obbligatorio**:
        /// senza, una prova che fallisce appende il comando invece di diventare
        /// rossa.
        /// </summary>
        private static string Riga(Ponte p, int ms)
        {
            string riga;
            return p.Righe.TryTake(out riga, ms) ? riga : null;
        }

        private static string Chiedi(Ponte p, int id, string richiesta)
        {
            Scrivi(p, richiesta);
            // Si scarta quello che non e' la risposta attesa: il ponte puo'
            // scrivere altro, e cercare l'id e' l'unico modo che non dipende
            // dall'ordine.
            var scadenza = Environment.TickCount + AttesaMs;
            while (Environment.TickCount < scadenza)
            {
                var riga = Riga(p, Math.Max(200, scadenza - Environment.TickCount));
                if (riga == null) return null;
                var o = Ogg(riga);
                if (o != null && o.ContainsKey("id") && Convert.ToString(o["id"]) == id.ToString())
                    return riga;
            }
            return null;
        }

        private static void Chiudi(Ponte p)
        {
            try { p.Processo.StandardInput.Close(); } catch { }
            try { p.Processo.WaitForExit(3000); } catch { }
        }

        private static Dictionary<string, object> Ogg(string riga)
        {
            if (string.IsNullOrWhiteSpace(riga)) return null;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                s.MaxJsonLength = 40 * 1024 * 1024;
                return s.DeserializeObject(riga) as Dictionary<string, object>;
            }
            catch { return null; }
        }

        private static object Dentro(object o, string chiave)
        {
            var d = o as Dictionary<string, object>;
            if (d == null || !d.ContainsKey(chiave)) return null;
            return d[chiave];
        }

        /// <summary>
        /// Il testo dentro un `result` di `tools/call`: `content[0].text`.
        ///
        /// Torna la stringa vuota e non `null` quando non c'e': un'attesa che
        /// cerca una parola dentro un `null` esploderebbe, e l'esplosione si
        /// leggerebbe come un difetto del ponte invece che come «il contenuto
        /// era vuoto».
        /// </summary>
        private static string TestoDi(Dictionary<string, object> risultato)
        {
            var contenuto = Dentro(risultato, "content") as object[];
            if (contenuto == null || contenuto.Length == 0) return "";
            return Convert.ToString(Dentro(contenuto[0], "text")) ?? "";
        }

        private static string Corto(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(niente)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 90 ? s.Substring(0, 90) + "..." : s;
        }
    }
}
