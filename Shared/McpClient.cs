using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Shared
{
    /// <summary>
    /// PARLARE COL PONTE COME FAREBBE UN CLIENTE MCP: il trasporto, e basta.
    ///
    /// ── PERCHE' STA QUI E NON IN UNO DEI DUE PROGETTI ────────────────────
    ///
    /// Lo usano in due. `Oce.Solid.Agent.ContractTests` da giorni, per provare il protocollo
    /// **senza** SOLIDWORKS; il banco da adesso (voce F14), per far passare
    /// dei casi **dalla porta del cliente**, con un documento vero davanti.
    ///
    /// Copiarlo sarebbe stato piu' rapido e sbagliato. Dentro c'e' una cosa
    /// che e' costata un pomeriggio — il BOM UTF-8 che `AutoFlush = true`
    /// scrive davanti alla prima riga, tre byte che facevano rispondere al
    /// ponte «Primitiva JSON non valida» mentre il sintomo sembrava
    /// «initialize non risponde». *Un difetto pagato una volta si ripara in un
    /// posto solo, altrimenti si paga di nuovo nella copia.*
    ///
    /// Qui non c'e' nessuna asserzione: chi chiama decide cosa pretendere.
    /// </summary>
    internal static class McpClient
    {
        /// <summary>
        /// Quanto si aspetta una risposta. NON E' UNA COMODITA': senza, una
        /// prova che fallisce appende il comando invece di diventare rossa.
        /// Novanta secondi perche' il primo `tools/call` aggancia SOLIDWORKS,
        /// e se SOLIDWORKS c'e' ed e' occupato quella riga puo' tardare.
        /// </summary>
        internal const int TimeoutMs = 90000;
        // ==================================================================
        // IL SOTTOPROCESSO
        // ==================================================================
        internal sealed class Bridge : IDisposable
        {
            public Process ServerProcess;
            public readonly System.Collections.Concurrent.BlockingCollection<string> Lines =
                new System.Collections.Concurrent.BlockingCollection<string>();
            public readonly List<string> LinesNotJson = new List<string>();
            /// <summary>Tutto quello che e' arrivato, per poterlo DIRE quando
            /// una prova va rossa: «non e' arrivato niente» senza dire cos'e'
            /// arrivato invece costa un'ora a chi legge.</summary>
            public readonly List<string> Views = new List<string>();
            public int LinesRead;

            public void Dispose()
            {
                try { if (ServerProcess != null && !ServerProcess.HasExited) ServerProcess.Kill(); } catch { }
                try { ServerProcess?.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// Il ponte, ma con il catalogo rotto di proposito.
        ///
        /// METODO A PARTE E NON UN PARAMETRO IN PIU' SU `Open`. Aggiungere un
        /// argomento con valore predefinito e' gia' costato un banco intero
        /// (nota `80zf`: «Mancata corrispondenza nel numero dei parametri», da
        /// 88/0 a 70/58), e la riparazione che ha funzionato fu **un nome
        /// diverso**. Qui `Open` non e' chiamato per riflessione, ma la regola
        /// si tiene lo stesso: costa una riga e toglie una categoria di
        /// sorprese.
        /// </summary>
        internal static Bridge OpenWithBrokenCatalog(string executable)
        {
            var fake = Path.Combine(
                Path.GetTempPath(), "catalogo_che_non_esiste_" + Guid.NewGuid().ToString("N") + ".json");
            return Open(executable, fake);
        }

        internal static Bridge Open(string executable)
        {
            return Open(executable, null);
        }

        internal static Bridge Open(string executable, string fakeCatalog)
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
                var p = new Bridge
                {
                    ServerProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo(executable)
                        {
                            RedirectStandardInput = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            StandardOutputEncoding = new UTF8Encoding(false),
                            WorkingDirectory = Path.GetDirectoryName(executable),
                        },
                    },
                };
                // `OPENPDM_CATALOGO` e' la stessa variabile che userebbe chi
                // installa il pacchetto altrove: puntarla a un file che non
                // c'e' e' il modo onesto di provare cosa fa il ponte quando il
                // catalogo non si carica.
                if (fakeCatalog != null)
                    p.ServerProcess.StartInfo.EnvironmentVariables["OPENPDM_CATALOGO"] = fakeCatalog;
                p.ServerProcess.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    p.LinesRead++;
                    p.Views.Add(e.Data);
                    var t = e.Data.Trim();
                    // QUELLO CHE NON E' JSON SI TIENE DA PARTE, non si butta:
                    // e' il controllo, non un incidente.
                    if (t.Length > 0 && !(t.StartsWith("{") || t.StartsWith("[")))
                        p.LinesNotJson.Add(t);
                    p.Lines.Add(e.Data);
                };
                // stderr NON si guarda: e' li' che il ponte racconta, ed e'
                // giusto che ci racconti.
                p.ServerProcess.ErrorDataReceived += (s, e) => { };
                p.ServerProcess.Start();
                p.ServerProcess.BeginOutputReadLine();
                p.ServerProcess.BeginErrorReadLine();
                return p;
            }
            catch { return null; }
        }

        /// <summary>Una riga sullo stdin del ponte. Vedi `Open` per il BOM.</summary>
        internal static void Write(Bridge p, string line)
        {
            p.ServerProcess.StandardInput.Write(line + "\n");
            p.ServerProcess.StandardInput.Flush();
        }

        /// <summary>
        /// Una riga, o null dopo il timeout. **Il timeout e' obbligatorio**:
        /// senza, una prova che fallisce appende il comando invece di diventare
        /// rossa.
        /// </summary>
        internal static string Line(Bridge p, int ms)
        {
            string line;
            return p.Lines.TryTake(out line, ms) ? line : null;
        }

        internal static string Ask(Bridge p, int id, string request)
        {
            Write(p, request);
            // Si scarta quello che non e' la risposta attesa: il ponte puo'
            // scrivere altro, e cercare l'id e' l'unico modo che non dipende
            // dall'ordine.
            var expiry = Environment.TickCount + TimeoutMs;
            while (Environment.TickCount < expiry)
            {
                var line = Line(p, Math.Max(200, expiry - Environment.TickCount));
                if (line == null) return null;
                var o = Obj(line);
                if (o != null && o.ContainsKey("id") && Convert.ToString(o["id"]) == id.ToString())
                    return line;
            }
            return null;
        }

        internal static void Close(Bridge p)
        {
            try { p.ServerProcess.StandardInput.Close(); } catch { }
            try { p.ServerProcess.WaitForExit(3000); } catch { }
        }

        internal static Dictionary<string, object> Obj(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                s.MaxJsonLength = 40 * 1024 * 1024;
                return s.DeserializeObject(line) as Dictionary<string, object>;
            }
            catch { return null; }
        }

        internal static object Inside(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            if (d == null || !d.ContainsKey(key)) return null;
            return d[key];
        }

        /// <summary>
        /// Il testo dentro un `result` di `tools/call`: `content[0].text`.
        ///
        /// Torna la stringa vuota e non `null` quando non c'e': un'attesa che
        /// cerca una parola dentro un `null` esploderebbe, e l'esplosione si
        /// leggerebbe come un difetto del ponte invece che come «il contenuto
        /// era vuoto».
        /// </summary>
        internal static string TextOf(Dictionary<string, object> result)
        {
            var content = Inside(result, "content") as object[];
            if (content == null || content.Length == 0) return "";
            return Convert.ToString(Inside(content[0], "text")) ?? "";
        }

        internal static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(niente)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 90 ? s.Substring(0, 90) + "..." : s;
        }
    }
}
