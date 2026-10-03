using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Condiviso
{
    /// <summary>
    /// PARLARE COL PONTE COME FAREBBE UN CLIENTE MCP: il trasporto, e basta.
    ///
    /// ── PERCHE' STA QUI E NON IN UNO DEI DUE PROGETTI ────────────────────
    ///
    /// Lo usano in due. `Prove.Contratti` da giorni, per provare il protocollo
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
    internal static class ClienteMcp
    {
        /// <summary>
        /// Quanto si aspetta una risposta. NON E' UNA COMODITA': senza, una
        /// prova che fallisce appende il comando invece di diventare rossa.
        /// Novanta secondi perche' il primo `tools/call` aggancia SOLIDWORKS,
        /// e se SOLIDWORKS c'e' ed e' occupato quella riga puo' tardare.
        /// </summary>
        internal const int AttesaMs = 90000;
        // ==================================================================
        // IL SOTTOPROCESSO
        // ==================================================================
        internal sealed class Ponte : IDisposable
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
        internal static Ponte ApriConCatalogoRotto(string eseguibile)
        {
            var finto = Path.Combine(
                Path.GetTempPath(), "catalogo_che_non_esiste_" + Guid.NewGuid().ToString("N") + ".json");
            return Apri(eseguibile, finto);
        }

        internal static Ponte Apri(string eseguibile)
        {
            return Apri(eseguibile, null);
        }

        internal static Ponte Apri(string eseguibile, string catalogoFinto)
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
        internal static void Scrivi(Ponte p, string riga)
        {
            p.Processo.StandardInput.Write(riga + "\n");
            p.Processo.StandardInput.Flush();
        }

        /// <summary>
        /// Una riga, o null dopo il timeout. **Il timeout e' obbligatorio**:
        /// senza, una prova che fallisce appende il comando invece di diventare
        /// rossa.
        /// </summary>
        internal static string Riga(Ponte p, int ms)
        {
            string riga;
            return p.Righe.TryTake(out riga, ms) ? riga : null;
        }

        internal static string Chiedi(Ponte p, int id, string richiesta)
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

        internal static void Chiudi(Ponte p)
        {
            try { p.Processo.StandardInput.Close(); } catch { }
            try { p.Processo.WaitForExit(3000); } catch { }
        }

        internal static Dictionary<string, object> Ogg(string riga)
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

        internal static object Dentro(object o, string chiave)
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
        internal static string TestoDi(Dictionary<string, object> risultato)
        {
            var contenuto = Dentro(risultato, "content") as object[];
            if (contenuto == null || contenuto.Length == 0) return "";
            return Convert.ToString(Dentro(contenuto[0], "text")) ?? "";
        }

        internal static string Corto(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(niente)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 90 ? s.Substring(0, 90) + "..." : s;
        }
    }
}
