using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Oce.Solid.Agent.Installer
{
    /// <summary>Un client MCP che si puo' configurare da qui.</summary>
    internal sealed class Client
    {
        public string Nome;
        public string File;
        public bool Installato;
        /// <summary>Come si chiama il processo, per sapere se e' aperto adesso.</summary>
        public string Processo;
        /// <summary>
        /// Claude Code: wired through `claude mcp add` when `claude` is on
        /// PATH (entry H19, B5; see ClaudeCli), through the file otherwise.
        /// </summary>
        public bool ClaudeCode;
    }

    /// <summary>
    /// COLLEGARE CLAUDE A SOLIDWORKS.
    ///
    /// E' questo il lavoro vero dell'installatore. Copiare due file lo sa fare
    /// chiunque; il punto dove ci si perde e' l'ultimo: aprire un file JSON
    /// nascosto in %APPDATA%, capire dove va incollato il blocco, non rompere
    /// quello che c'era gia', e sapere che Claude va riavviato perche' legga.
    ///
    /// TRE REGOLE, TUTTE E TRE PAGATE DA QUALCUN ALTRO PRIMA DI NOI:
    ///
    /// 1. NON SI SOVRASCRIVE IL FILE. Chi usa Claude ha spesso altri server
    ///    MCP configurati. Un installatore che scrive il suo blocco e basta
    ///    glieli cancella tutti, e se ne accorgera' domani senza collegare la
    ///    cosa all'installazione di ieri. Si legge, si aggiunge la propria
    ///    voce, si riscrive tutto il resto com'era.
    ///
    /// 2. SI FA UNA COPIA PRIMA. Sempre, anche quando il file sembra semplice.
    ///    Costa un millisecondo e vale la differenza fra un errore e un danno.
    ///
    /// 3. SI DICE DI RIAVVIARE. Claude legge questo file all'avvio. Senza
    ///    riavvio non succede niente, e «non succede niente» e' il modo piu'
    ///    rapido di far concludere che l'installazione e' fallita.
    /// </summary>
    internal static class Configurazione
    {
        public const string NomeServer = "solidworks";

        private static JavaScriptSerializer Json()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 200;
            return s;
        }

        // ─────────────────────────────────────────────────────────────────
        // Quali client ci sono su questa macchina
        // ─────────────────────────────────────────────────────────────────

        public static List<Client> ClientTrovati()
        {
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var tutti = new List<Client>
            {
                new Client
                {
                    Nome = "Claude Desktop",
                    File = Path.Combine(appdata, @"Claude\claude_desktop_config.json"),
                    Processo = "Claude",
                },
                new Client
                {
                    Nome = "Claude Code",
                    File = ClaudeCodeFile(),
                    Processo = null,
                    ClaudeCode = true,
                },
            };

            foreach (var c in tutti)
            {
                try
                {
                    // «Installato» vuol dire che c'e' la CARTELLA del client:
                    // il file di configurazione puo' non esistere ancora, ed e'
                    // il caso normale di chi non ha mai aggiunto un server MCP.
                    //
                    // NOT FOR CLAUDE CODE (H19 review): its file sits in the
                    // user's profile, a folder that always exists, so Claude
                    // Code was "installed" on every machine. A silent install
                    // with no client at all then created a .claude.json nobody
                    // reads and exited 0, and the "no MCP client found" exit 3
                    // could never happen. See ClaudeCodePresent.
                    if (c.ClaudeCode) { c.Installato = ClaudeCodePresent(c.File); continue; }
                    var cartella = Path.GetDirectoryName(c.File);
                    c.Installato = Directory.Exists(cartella) || File.Exists(c.File);
                }
                catch { c.Installato = false; }
            }
            return tutti;
        }

        public static bool EAperto(Client client)
        {
            if (client == null || string.IsNullOrWhiteSpace(client.Processo)) return false;
            try { return Process.GetProcessesByName(client.Processo).Length > 0; }
            catch { return false; }
        }

        /// <summary>
        /// The file where Claude Code keeps its user-scope MCP servers, where
        /// Claude Code itself looks for it (H19 review; read in its 2.1.281
        /// binary): <c>.claude.json</c> in CLAUDE_CONFIG_DIR when that is set,
        /// else in the home folder, which for Claude Code on Windows is
        /// %USERPROFILE%. The installer used the profile folder of the
        /// account only, so with CLAUDE_CONFIG_DIR set `claude mcp add` wrote
        /// one file and --verify read another.
        /// </summary>
        public static string ClaudeCodeFile()
        {
            var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (string.IsNullOrWhiteSpace(dir)) dir = Home();
            return Path.Combine(dir.Trim(), ".claude.json");
        }

        private static string Home()
        {
            var home = Environment.GetEnvironmentVariable("USERPROFILE");
            return string.IsNullOrWhiteSpace(home)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : home.Trim();
        }

        /// <summary>
        /// True when Claude Code is on this machine: its file exists, or its
        /// own folder does (CLAUDE_CONFIG_DIR, else %USERPROFILE%\.claude,
        /// which Claude Code creates the first time it runs), or `claude` is
        /// on PATH. The folder that holds the file is no evidence: it is the
        /// profile, and it always exists.
        /// </summary>
        public static bool ClaudeCodePresent(string file)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(file) && File.Exists(file)) return true;
                var own = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                if (string.IsNullOrWhiteSpace(own)) own = Path.Combine(Home(), ".claude");
                if (Directory.Exists(own.Trim())) return true;
            }
            catch { }
            return ClaudeCli.Find() != null;
        }

        // ─────────────────────────────────────────────────────────────────
        // Scrivere
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Aggiunge (o aggiorna) la voce del ponte nella configurazione del
        /// client, lasciando intatto tutto il resto. Ritorna il percorso della
        /// copia di sicurezza, o null se il file non esisteva.
        /// </summary>
        public static string Collega(
            Client client, string eseguibile, string redist, string weldmentProfiles, Action<string> racconta)
        {
            if (client == null) throw new Exception("Nessun client scelto.");
            if (string.IsNullOrWhiteSpace(eseguibile) || !File.Exists(eseguibile))
                throw new Exception("Non trovo il ponte da collegare: " + eseguibile);

            var radice = Leggi(client.File);
            var server = Sotto(radice, "mcpServers");

            var voce = new Dictionary<string, object>
            {
                ["command"] = eseguibile,
            };

            // `env` solo se serve davvero: una variabile scritta «tanto per»
            // e' una cosa in piu' che puo' diventare sbagliata quando qualcuno
            // sposta SOLIDWORKS, e nessuno andra' a rileggerla.
            object previous;
            server.TryGetValue(NomeServer, out previous);
            var env = MergedEnv(EnvOf(previous), redist, weldmentProfiles, racconta);
            if (env.Count > 0)
            {
                voce["env"] = env.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
                if (env.ContainsKey("SW_REDIST"))
                    racconta("aggiungo SW_REDIST: SOLIDWORKS non e' nel percorso solito");
                if (env.ContainsKey("PROFILI_SALDATURA"))
                    racconta("adding PROFILI_SALDATURA: " + env["PROFILI_SALDATURA"]);
            }

            bool cera = server.ContainsKey(NomeServer);
            server[NomeServer] = voce;

            var copia = Scrivi(client.File, radice, racconta);
            racconta((cera ? "aggiornata" : "aggiunta") + " la voce «" + NomeServer +
                     "» in " + client.Nome);
            return copia;
        }

        /// <summary>
        /// The variables the bridge needs in the client's `env`, and only those
        /// (entry H19, A6): SW_REDIST when SOLIDWORKS is not where the bridge
        /// looks by itself, PROFILI_SALDATURA when a weldment profiles folder
        /// is known. One place, so the file, the block to paste and
        /// `claude mcp add` cannot say different things.
        /// </summary>
        public static Dictionary<string, string> EnvFor(string redist, string weldmentProfiles)
        {
            var env = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(redist) && !RedistStandard(redist)) env["SW_REDIST"] = redist;
            if (!string.IsNullOrWhiteSpace(weldmentProfiles)) env["PROFILI_SALDATURA"] = weldmentProfiles;
            return env;
        }

        /// <summary>
        /// The `env` an entry gets when it is written again (H19 review).
        ///
        /// Until this review a reinstall replaced the whole entry with the
        /// variables of <see cref="EnvFor"/>, and `claude mcp remove` + `add`
        /// did the same: whatever the user had added to the bridge's own entry
        /// was dropped without a word. RISCONTRO_RADICE and
        /// RISCONTRO_SOLA_LETTURA (the folders the bridge may and may not
        /// write) are such variables, and so is a PROFILI_SALDATURA set by
        /// hand, as the README told people to do before the installer wrote
        /// it. Now the existing variables stay; of the two the installer owns,
        /// SW_REDIST is set or removed as the installer decided, and
        /// PROFILI_SALDATURA is set when the installer has a folder and kept
        /// as it was when it has none.
        /// </summary>
        public static Dictionary<string, string> MergedEnv(
            IDictionary<string, string> existing, string redist, string weldmentProfiles, Action<string> racconta)
        {
            var env = new Dictionary<string, string>(StringComparer.Ordinal);
            if (existing != null)
                foreach (var kv in existing) env[kv.Key] = kv.Value;
            var fresh = EnvFor(redist, weldmentProfiles);
            if (fresh.ContainsKey("SW_REDIST")) env["SW_REDIST"] = fresh["SW_REDIST"];
            else env.Remove("SW_REDIST");
            if (fresh.ContainsKey("PROFILI_SALDATURA")) env["PROFILI_SALDATURA"] = fresh["PROFILI_SALDATURA"];

            var kept = env.Keys.Where(k => k != "SW_REDIST" && !(k == "PROFILI_SALDATURA" && fresh.ContainsKey(k)))
                          .OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (kept.Count > 0 && racconta != null)
                racconta("keeping from the existing entry's env: " + string.Join(", ", kept));
            return env;
        }

        /// <summary>The `env` of an entry as read from a config file; empty when it has none.</summary>
        private static Dictionary<string, string> EnvOf(object entry)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var d = entry as Dictionary<string, object>;
            object env;
            if (d != null && d.TryGetValue("env", out env) && env is Dictionary<string, object>)
                foreach (var kv in (Dictionary<string, object>)env) result[kv.Key] = Convert.ToString(kv.Value);
            return result;
        }

        /// <summary>
        /// A PROFILI_SALDATURA already in one of these clients' entries that
        /// names a folder that exists, or null (H19 review). A folder chosen
        /// by hand is worth more than the one the installer would find: it is
        /// kept on a reinstall unless --weldment-profiles names another.
        /// </summary>
        public static string ExistingWeldmentProfiles(IEnumerable<Client> clients)
        {
            foreach (var c in clients ?? Enumerable.Empty<Client>())
            {
                var env = ConfiguredEnv(c);
                string v;
                if (env != null && env.TryGetValue("PROFILI_SALDATURA", out v) &&
                    !string.IsNullOrWhiteSpace(v) && Directory.Exists(v))
                    return v;
            }
            return null;
        }

        /// <summary>
        /// Why this client's config cannot be read (malformed, locked), or
        /// null when it reads or does not exist yet. For --verify, which used
        /// to treat an unreadable file like a file without an entry and could
        /// come out all green right after an install that exited 3 for it.
        /// </summary>
        public static string ReadProblem(Client client)
        {
            if (client == null || !File.Exists(client.File)) return null;
            try { Leggi(client.File); return null; }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>
        /// Toglie la voce del ponte, lasciando gli altri server.
        ///
        /// ONLY OUR OWN ENTRY (entry H19, A5). With <paramref name="folder"/>
        /// given, the entry is removed only if its command is the bridge in
        /// that folder: a "solidworks" entry that points at another
        /// installation (or at another program that took the same name) is
        /// someone else's, and is left alone with a line that says so.
        /// Returns true when an entry was removed.
        /// </summary>
        public static bool Scollega(Client client, string folder, Action<string> racconta)
        {
            if (client == null || !File.Exists(client.File))
            {
                racconta("niente da scollegare");
                return false;
            }
            var radice = Leggi(client.File);
            var server = Sotto(radice, "mcpServers");
            object entry;
            if (!server.TryGetValue(NomeServer, out entry))
            {
                racconta("la voce non c'era");
                return false;
            }
            if (!string.IsNullOrWhiteSpace(folder) && !PointsInto(entry, folder))
            {
                racconta(client.Nome + ": the \"" + NomeServer + "\" entry points at another installation (" +
                         CommandOf(entry) + "): left alone");
                return false;
            }
            server.Remove(NomeServer);
            Scrivi(client.File, radice, racconta);
            racconta("tolta la voce da " + client.Nome);
            return true;
        }

        /// <summary>True when the entry's command is the bridge inside <paramref name="folder"/>.</summary>
        public static bool PointsInto(object entry, string folder)
        {
            var c = CommandOf(entry);
            return !string.IsNullOrWhiteSpace(c) &&
                   Passi.PercorsiUguali(c, Path.Combine(folder, Passi.NomeEseguibile));
        }

        private static string CommandOf(object entry)
        {
            var d = entry as Dictionary<string, object>;
            object c;
            return d != null && d.TryGetValue("command", out c) ? Convert.ToString(c) : null;
        }

        /// <summary>The `env` of our entry, or null when there is no entry (for --verify).</summary>
        public static Dictionary<string, string> ConfiguredEnv(Client client)
        {
            try
            {
                if (client == null || !File.Exists(client.File)) return null;
                var server = Sotto(Leggi(client.File), "mcpServers");
                object entry;
                if (!server.TryGetValue(NomeServer, out entry)) return null;
                return EnvOf(entry);
            }
            catch { return null; }
        }

        /// <summary>Cosa c'e' scritto adesso per questo server, se c'e'.</summary>
        public static string ComeEConfigurato(Client client)
        {
            try
            {
                if (client == null || !File.Exists(client.File)) return null;
                var server = Sotto(Leggi(client.File), "mcpServers");
                object voce;
                if (!server.TryGetValue(NomeServer, out voce)) return null;
                var d = voce as Dictionary<string, object>;
                if (d == null) return null;
                object c;
                return d.TryGetValue("command", out c) ? Convert.ToString(c) : null;
            }
            catch { return null; }
        }

        // ─────────────────────────────────────────────────────────────────
        // Leggere e scrivere il file
        // ─────────────────────────────────────────────────────────────────

        private static Dictionary<string, object> Leggi(string file)
        {
            try
            {
                if (!File.Exists(file)) return new Dictionary<string, object>();
                var testo = File.ReadAllText(file, Encoding.UTF8).Trim();
                if (testo.Length == 0) return new Dictionary<string, object>();
                var d = Json().DeserializeObject(testo) as Dictionary<string, object>;
                return d ?? new Dictionary<string, object>();
            }
            catch (IOException ex)
            {
                // Un file che NON SI LEGGE non e' un file rotto: prima finiva
                // nel ramo sotto, e a chi aveva il client aperto si diceva che
                // il suo JSON non era valido.
                throw new Exception(
                    "Non riesco a leggere «" + file + "»: e' bloccato da un altro " +
                    "programma, e non l'ho toccato. Di solito e' il client stesso " +
                    "aperto: chiudilo e riprova. (" + ex.Message + ")");
            }
            catch (Exception ex)
            {
                // Un file rotto NON si sostituisce in silenzio con uno nuovo:
                // dentro potrebbero esserci dieci server configurati a mano.
                throw new Exception(
                    "Il file di configurazione «" + file + "» non e' JSON valido e non " +
                    "me la sento di riscriverlo: dentro potrebbe esserci altro lavoro. " +
                    "Aprilo e sistemalo, poi riprova. (" + ex.Message + ")");
            }
        }

        private static Dictionary<string, object> Sotto(
            Dictionary<string, object> radice, string chiave)
        {
            object v;
            if (radice.TryGetValue(chiave, out v))
            {
                var d = v as Dictionary<string, object>;
                if (d != null) return d;
            }
            var nuovo = new Dictionary<string, object>();
            radice[chiave] = nuovo;
            return nuovo;
        }

        private static string Scrivi(
            string file, Dictionary<string, object> radice, Action<string> racconta)
        {
            var cartella = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(cartella)) Directory.CreateDirectory(cartella);

            // UN FILE TENUTO DA UN ALTRO PROGRAMMA SI SCOPRE PRIMA (voce H15).
            // Prima lo si scopriva a meta': copia fatta, `.parziale` scritto,
            // poi `File.Delete` che solleva — e il `.parziale` restava accanto
            // al file vero per sempre. Adesso si prova ad aprirlo in scrittura
            // esclusiva: se non si lascia, ci si ferma senza aver toccato
            // niente, e lo si dice con parole che fanno sapere cosa fare.
            if (File.Exists(file))
            {
                try { using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
                catch (IOException ex)
                {
                    throw new Exception(
                        "Il file «" + file + "» e' bloccato da un altro programma, e non " +
                        "l'ho toccato. Di solito e' il client stesso aperto: chiudilo e " +
                        "riprova. (" + ex.Message + ")");
                }
            }

            var copia = Backup(file, racconta);

            var testo = Indenta(Json().Serialize(radice));

            var temporaneo = file + ".parziale";
            try
            {
                using (var w = new StreamWriter(temporaneo, false, new UTF8Encoding(false)))
                    w.Write(testo);
                if (File.Exists(file)) File.Delete(file);
                File.Move(temporaneo, file);
            }
            catch
            {
                // Qualunque cosa sia andata storta, il `.parziale` non resta:
                // un file a meta' accanto a quello vero e' la cosa che fa
                // pensare a chi apre la cartella che si puo' usare quello.
                try { if (File.Exists(temporaneo)) File.Delete(temporaneo); } catch { }
                throw;
            }

            return copia;
        }

        /// <summary>
        /// The backup of a config file before it is changed, and the pruning
        /// of the old ones; null when the file does not exist. Public since the
        /// H19 review: `claude mcp remove` + `add` rewrite .claude.json too,
        /// and until then that route made no backup at all.
        /// </summary>
        public static string Backup(string file, Action<string> racconta)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return null;
            // Il nome della copia porta la data: cosi' si capisce quale e'
            // quale.
            var copia = file + PrefissoCopia +
                        DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            File.Copy(file, copia, true);
            racconta?.Invoke("copia di sicurezza: " + Path.GetFileName(copia));
            PotaLeCopie(file, racconta ?? (s => { }));
            return copia;
        }

        private const string PrefissoCopia = ".prima-di-oce-solid-agent-";

        /// <summary>
        /// Quante copie di sicurezza per file si tengono. Tre bastano a tornare
        /// indietro di due installazioni sbagliate di fila; oltre e' solo roba
        /// che si accumula in %APPDATA% e che nessuno guarda.
        /// </summary>
        public const int CopieTenute = 3;

        /// <summary>
        /// Toglie le copie di sicurezza piu' vecchie, tenendo le ultime
        /// <see cref="CopieTenute"/> (voce H15: prima non si ripulivano mai, e
        /// dieci installazioni lasciavano dieci `.bak`).
        ///
        /// Tocca SOLO i file con il nostro prefisso accanto a quel file: una
        /// copia fatta a mano da chi usa il client non e' nostra.
        /// </summary>
        private static void PotaLeCopie(string file, Action<string> racconta)
        {
            try
            {
                var cartella = Path.GetDirectoryName(file);
                var nome = Path.GetFileName(file) + PrefissoCopia;
                var nostre = Directory.GetFiles(cartella, nome + "*.bak")
                    .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                    .Skip(CopieTenute)
                    .ToList();
                foreach (var vecchia in nostre)
                {
                    try { File.Delete(vecchia); }
                    catch { }
                }
                if (nostre.Count > 0)
                    racconta("tolte " + nostre.Count + " copie di sicurezza vecchie (ne tengo " + CopieTenute + ")");
            }
            catch { /* non poter pulire non e' un motivo per non installare */ }
        }

        /// <summary>
        /// Rimette a capo e indenta il JSON.
        ///
        /// `JavaScriptSerializer` scrive tutto su una riga sola. Tecnicamente
        /// e' JSON valido; praticamente vuol dire che chi apre il file dopo di
        /// noi trova una riga da duemila caratteri dove prima c'era qualcosa di
        /// leggibile, e non puo' piu' correggerlo a mano. Un installatore che
        /// rende illeggibile un file di configurazione dell'utente ha fatto un
        /// danno anche se il contenuto e' giusto.
        ///
        /// Si lavora sul testo e non sull'oggetto perche' serve solo spaziatura:
        /// l'unica cosa da cui stare attenti sono le stringhe, dove parentesi e
        /// virgole non sono struttura.
        /// </summary>
        private static string Indenta(string json)
        {
            var fuori = new StringBuilder(json.Length * 2);
            int livello = 0;
            bool inStringa = false, scappato = false;

            foreach (char c in json)
            {
                if (inStringa)
                {
                    fuori.Append(c);
                    if (scappato) scappato = false;
                    else if (c == '\\') scappato = true;
                    else if (c == '"') inStringa = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inStringa = true;
                        fuori.Append(c);
                        break;
                    case '{':
                    case '[':
                        fuori.Append(c);
                        livello++;
                        fuori.Append('\n').Append(new string(' ', livello * 2));
                        break;
                    case '}':
                    case ']':
                        livello--;
                        fuori.Append('\n').Append(new string(' ', Math.Max(0, livello) * 2));
                        fuori.Append(c);
                        break;
                    case ',':
                        fuori.Append(c).Append('\n').Append(new string(' ', livello * 2));
                        break;
                    case ':':
                        fuori.Append(": ");
                        break;
                    default:
                        fuori.Append(c);
                        break;
                }
            }
            return fuori.ToString().Replace("{\n" + new string(' ', 2) + "\n}", "{}") + "\n";
        }

        private static bool RedistStandard(string redist)
        {
            try
            {
                var standard = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"SOLIDWORKS Corp\SOLIDWORKS\api\redist");
                return Passi.PercorsiUguali(redist, standard);
            }
            catch { return false; }
        }

        /// <summary>Il blocco da incollare a mano, per chi usa un altro client.</summary>
        public static string BloccoDaIncollare(string eseguibile, string redist, string weldmentProfiles)
        {
            var voce = new Dictionary<string, object> { ["command"] = eseguibile };
            var env = EnvFor(redist, weldmentProfiles);
            if (env.Count > 0) voce["env"] = env.ToDictionary(kv => kv.Key, kv => (object)kv.Value);

            var radice = new Dictionary<string, object>
            {
                ["mcpServers"] = new Dictionary<string, object> { [NomeServer] = voce },
            };
            return Indenta(Json().Serialize(radice));
        }
    }
}
