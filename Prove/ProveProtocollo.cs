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
                var prima = Riga(p, AttesaMs);
                var oggPrima = Ogg(prima);
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
                Segna("tools/list da' esattamente cinque strumenti",
                      strumenti != null && strumenti.Length == 5,
                      strumenti == null ? "(niente)" : strumenti.Length + " strumenti: " +
                          string.Join(", ", strumenti.Select(s => Convert.ToString(Dentro(s, "name")))));

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

        private static Ponte Apri(string eseguibile)
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

        private static string Corto(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(niente)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 90 ? s.Substring(0, 90) + "..." : s;
        }
    }
}
