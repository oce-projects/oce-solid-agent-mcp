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
    /// <summary>An MCP client that can be configured from here.</summary>
    internal sealed class Client
    {
        public string Name;
        public string File;
        public bool Installed;
        /// <summary>What the process is called, to know whether it is open right now.</summary>
        public string ServerProcess;
        /// <summary>
        /// Claude Code: wired through `claude mcp add` when `claude` is on
        /// PATH (entry H19, B5; see ClaudeCli), through the file otherwise.
        /// </summary>
        public bool ClaudeCode;
    }

    /// <summary>
    /// CONNECTING CLAUDE TO SOLIDWORKS.
    ///
    /// This is the real job of the installer. Anyone can copy two files;
    /// the place where one gets lost is the last one: opening a hidden JSON
    /// file in %APPDATA%, understanding where the block goes, not breaking
    /// what was already there, and knowing that Claude must be restarted for it to read.
    ///
    /// THREE RULES, ALL THREE PAID FOR BY SOMEONE ELSE BEFORE US:
    ///
    /// 1. THE FILE IS NOT OVERWRITTEN. Whoever uses Claude often has other MCP
    ///    servers configured. An installer that writes its block and nothing else
    ///    deletes them all, and they will notice tomorrow without connecting the
    ///    matter to yesterday's installation. It reads, adds its own
    ///    entry, rewrites everything else as it was.
    ///
    /// 2. A COPY IS MADE FIRST. Always, even when the file looks simple.
    ///    It costs a millisecond and is worth the difference between an error and damage.
    ///
    /// 3. IT SAYS TO RESTART. Claude reads this file at startup. Without a
    ///    restart nothing happens, and «nothing happens» is the quickest way to
    ///    make people conclude that the installation failed.
    /// </summary>
    internal static class Configuration
    {
        public const string ServerName = "solidworks";

        private static JavaScriptSerializer Json()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 200;
            return s;
        }

        // ─────────────────────────────────────────────────────────────────
        // Which clients are on this machine
        // ─────────────────────────────────────────────────────────────────

        public static List<Client> FoundClients()
        {
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var all = new List<Client>
            {
                new Client
                {
                    Name = "Claude Desktop",
                    File = Path.Combine(appdata, @"Claude\claude_desktop_config.json"),
                    ServerProcess = "Claude",
                },
                new Client
                {
                    Name = "Claude Code",
                    File = ClaudeCodeFile(),
                    ServerProcess = null,
                    ClaudeCode = true,
                },
            };

            foreach (var c in all)
            {
                try
                {
                    // «Installed» means that the client's FOLDER is there:
                    // the configuration file may not exist yet, and that is
                    // the normal case of someone who has never added an MCP server.
                    //
                    // NOT FOR CLAUDE CODE (H19 review): its file sits in the
                    // user's profile, a folder that always exists, so Claude
                    // Code was "installed" on every machine. A silent install
                    // with no client at all then created a .claude.json nobody
                    // reads and exited 0, and the "no MCP client found" exit 3
                    // could never happen. See ClaudeCodePresent.
                    if (c.ClaudeCode) { c.Installed = ClaudeCodePresent(c.File); continue; }
                    var folder = Path.GetDirectoryName(c.File);
                    c.Installed = Directory.Exists(folder) || File.Exists(c.File);
                }
                catch { c.Installed = false; }
            }
            return all;
        }

        public static bool IsOpen(Client client)
        {
            if (client == null || string.IsNullOrWhiteSpace(client.ServerProcess)) return false;
            try { return Process.GetProcessesByName(client.ServerProcess).Length > 0; }
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
        // Writing
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Adds (or updates) the bridge entry in the configuration of the
        /// client, leaving everything else intact. Returns the path of the
        /// backup copy, or null if the file did not exist.
        /// </summary>
        public static string Connect(
            Client client, string executable, string redist, string weldmentProfiles, Action<string> tell)
        {
            if (client == null) throw new Exception("No client chosen.");
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                throw new Exception("I cannot find the bridge to connect: " + executable);

            var root = Read(client.File);
            var server = Below(root, "mcpServers");

            var entry = new Dictionary<string, object>
            {
                ["command"] = executable,
            };

            // `env` only if really needed: a variable written «just in case»
            // is one more thing that can become wrong when someone
            // moves SOLIDWORKS, and nobody will go back and reread it.
            object previous;
            server.TryGetValue(ServerName, out previous);
            var env = MergedEnv(EnvOf(previous), redist, weldmentProfiles, tell);
            if (env.Count > 0)
            {
                entry["env"] = env.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
                if (env.ContainsKey("SW_REDIST"))
                    tell("adding SW_REDIST: SOLIDWORKS is not in the usual place");
                if (env.ContainsKey("OCE_WELDMENT_PROFILES"))
                    tell("adding OCE_WELDMENT_PROFILES: " + env["OCE_WELDMENT_PROFILES"]);
            }

            bool existed = server.ContainsKey(ServerName);
            server[ServerName] = entry;

            var copy = Write(client.File, root, tell);
            tell((existed ? "updated" : "added") + " the entry \"" + ServerName +
                     "\" in " + client.Name);
            return copy;
        }

        /// <summary>
        /// The variables the bridge needs in the client's `env`, and only those
        /// (entry H19, A6): SW_REDIST when SOLIDWORKS is not where the bridge
        /// looks by itself, OCE_WELDMENT_PROFILES when a weldment profiles folder
        /// is known. One place, so the file, the block to paste and
        /// `claude mcp add` cannot say different things.
        /// </summary>
        public static Dictionary<string, string> EnvFor(string redist, string weldmentProfiles)
        {
            var env = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(redist) && !RedistStandard(redist)) env["SW_REDIST"] = redist;
            if (!string.IsNullOrWhiteSpace(weldmentProfiles)) env["OCE_WELDMENT_PROFILES"] = weldmentProfiles;
            return env;
        }

        /// <summary>
        /// The `env` an entry gets when it is written again (H19 review).
        ///
        /// Until this review a reinstall replaced the whole entry with the
        /// variables of <see cref="EnvFor"/>, and `claude mcp remove` + `add`
        /// did the same: whatever the user had added to the bridge's own entry
        /// was dropped without a word. OCE_WRITE_ROOT and
        /// OCE_READ_ONLY_FOLDERS (the folders the bridge may and may not
        /// write) are such variables, and so is a OCE_WELDMENT_PROFILES set by
        /// hand, as the README told people to do before the installer wrote
        /// it. Now the existing variables stay; of the two the installer owns,
        /// SW_REDIST is set or removed as the installer decided, and
        /// OCE_WELDMENT_PROFILES is set when the installer has a folder and kept
        /// as it was when it has none.
        /// </summary>
        public static Dictionary<string, string> MergedEnv(
            IDictionary<string, string> existing, string redist, string weldmentProfiles, Action<string> tell)
        {
            var env = new Dictionary<string, string>(StringComparer.Ordinal);
            if (existing != null)
                foreach (var kv in existing) env[kv.Key] = kv.Value;
            var fresh = EnvFor(redist, weldmentProfiles);
            if (fresh.ContainsKey("SW_REDIST")) env["SW_REDIST"] = fresh["SW_REDIST"];
            else env.Remove("SW_REDIST");
            if (fresh.ContainsKey("OCE_WELDMENT_PROFILES")) env["OCE_WELDMENT_PROFILES"] = fresh["OCE_WELDMENT_PROFILES"];

            var kept = env.Keys.Where(k => k != "SW_REDIST" && !(k == "OCE_WELDMENT_PROFILES" && fresh.ContainsKey(k)))
                          .OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (kept.Count > 0 && tell != null)
                tell("keeping from the existing entry's env: " + string.Join(", ", kept));
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
        /// A OCE_WELDMENT_PROFILES already in one of these clients' entries that
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
                if (env != null && env.TryGetValue("OCE_WELDMENT_PROFILES", out v) &&
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
            try { Read(client.File); return null; }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>
        /// Removes the bridge entry, leaving the other servers.
        ///
        /// ONLY OUR OWN ENTRY (entry H19, A5). With <paramref name="folder"/>
        /// given, the entry is removed only if its command is the bridge in
        /// that folder: a "solidworks" entry that points at another
        /// installation (or at another program that took the same name) is
        /// someone else's, and is left alone with a line that says so.
        /// Returns true when an entry was removed.
        /// </summary>
        public static bool Disconnect(Client client, string folder, Action<string> tell)
        {
            if (client == null || !File.Exists(client.File))
            {
                tell("nothing to disconnect");
                return false;
            }
            var root = Read(client.File);
            var server = Below(root, "mcpServers");
            object entry;
            if (!server.TryGetValue(ServerName, out entry))
            {
                tell("the entry was not there");
                return false;
            }
            if (!string.IsNullOrWhiteSpace(folder) && !PointsInto(entry, folder))
            {
                tell(client.Name + ": the \"" + ServerName + "\" entry points at another installation (" +
                         CommandOf(entry) + "): left alone");
                return false;
            }
            server.Remove(ServerName);
            Write(client.File, root, tell);
            tell("removed the entry from " + client.Name);
            return true;
        }

        /// <summary>True when the entry's command is the bridge inside <paramref name="folder"/>.</summary>
        public static bool PointsInto(object entry, string folder)
        {
            var c = CommandOf(entry);
            return !string.IsNullOrWhiteSpace(c) &&
                   Steps.EqualPaths(c, Path.Combine(folder, Steps.ExecutableName));
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
                var server = Below(Read(client.File), "mcpServers");
                object entry;
                if (!server.TryGetValue(ServerName, out entry)) return null;
                return EnvOf(entry);
            }
            catch { return null; }
        }

        /// <summary>What is written now for this server, if anything.</summary>
        public static string HowItIsConfigured(Client client)
        {
            try
            {
                if (client == null || !File.Exists(client.File)) return null;
                var server = Below(Read(client.File), "mcpServers");
                object entry;
                if (!server.TryGetValue(ServerName, out entry)) return null;
                var d = entry as Dictionary<string, object>;
                if (d == null) return null;
                object c;
                return d.TryGetValue("command", out c) ? Convert.ToString(c) : null;
            }
            catch { return null; }
        }

        // ─────────────────────────────────────────────────────────────────
        // Reading and writing the file
        // ─────────────────────────────────────────────────────────────────

        private static Dictionary<string, object> Read(string file)
        {
            try
            {
                if (!File.Exists(file)) return new Dictionary<string, object>();
                var text = File.ReadAllText(file, Encoding.UTF8).Trim();
                if (text.Length == 0) return new Dictionary<string, object>();
                var d = Json().DeserializeObject(text) as Dictionary<string, object>;
                return d ?? new Dictionary<string, object>();
            }
            catch (IOException ex)
            {
                // A file that CANNOT BE READ is not a broken file: it used to end
                // up in the branch below, and whoever had the client open was told that
                // their JSON was not valid.
                throw new Exception(
                    "I cannot read \"" + file + "\": it is locked by another " +
                    "program, and I did not touch it. Usually it is the client itself " +
                    "open: close it and try again. (" + ex.Message + ")");
            }
            catch (Exception ex)
            {
                // A broken file is NOT silently replaced with a new one:
                // inside there might be ten servers configured by hand.
                throw new Exception(
                    "The configuration file \"" + file + "\" is not valid JSON and I will " +
                    "not rewrite it: there may be other work inside. " +
                    "Open it and fix it, then try again. (" + ex.Message + ")");
            }
        }

        private static Dictionary<string, object> Below(
            Dictionary<string, object> root, string key)
        {
            object v;
            if (root.TryGetValue(key, out v))
            {
                var d = v as Dictionary<string, object>;
                if (d != null) return d;
            }
            var newItem = new Dictionary<string, object>();
            root[key] = newItem;
            return newItem;
        }

        private static string Write(
            string file, Dictionary<string, object> root, Action<string> tell)
        {
            var folder = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            // A FILE HELD BY ANOTHER PROGRAM IS DISCOVERED FIRST (entry H15).
            // It used to be discovered halfway: copy made, `.partial` written,
            // then `File.Delete` throws — and the `.partial` stayed next to
            // the real file forever. Now it is tried to open it for exclusive
            // writing: if it does not allow it, we stop without having touched
            // anything, and say so in words that make clear what to do.
            if (File.Exists(file))
            {
                try { using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
                catch (IOException ex)
                {
                    throw new Exception(
                        "The file \"" + file + "\" is locked by another program, and I did not " +
                        "touch it. Usually it is the client itself open: close it and " +
                        "try again. (" + ex.Message + ")");
                }
            }

            var copy = Backup(file, tell);

            var text = Indent(Json().Serialize(root));

            var temporary = file + ".partial";
            try
            {
                using (var w = new StreamWriter(temporary, false, new UTF8Encoding(false)))
                    w.Write(text);
                if (File.Exists(file)) File.Delete(file);
                File.Move(temporary, file);
            }
            catch
            {
                // Whatever went wrong, the `.partial` does not stay:
                // a half file next to the real one is the thing that makes
                // whoever opens the folder think that one can be used.
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                throw;
            }

            return copy;
        }

        /// <summary>
        /// The backup of a config file before it is changed, and the pruning
        /// of the old ones; null when the file does not exist. Public since the
        /// H19 review: `claude mcp remove` + `add` rewrite .claude.json too,
        /// and until then that route made no backup at all.
        /// </summary>
        public static string Backup(string file, Action<string> tell)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return null;
            // The name of the copy carries the date: so it is clear which is
            // which.
            var copy = file + CopyPrefix +
                        DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            File.Copy(file, copy, true);
            tell?.Invoke("backup copy: " + Path.GetFileName(copy));
            PruneCopies(file, tell ?? (s => { }));
            return copy;
        }

        private const string CopyPrefix = ".before-oce-solid-agent-";

        /// <summary>
        /// How many backup copies per file are kept. Three are enough to go back
        /// two wrong installations in a row; beyond that it is just stuff
        /// that piles up in %APPDATA% and that nobody looks at.
        /// </summary>
        public const int KeptCopies = 3;

        /// <summary>
        /// Removes the oldest backup copies, keeping the last
        /// <see cref="KeptCopies"/> (entry H15: before they were never cleaned up, and
        /// ten installations left ten `.bak`).
        ///
        /// It touches ONLY the files with our prefix next to that file: a
        /// copy made by hand by whoever uses the client is not ours.
        /// </summary>
        private static void PruneCopies(string file, Action<string> tell)
        {
            try
            {
                var folder = Path.GetDirectoryName(file);
                var itemName = Path.GetFileName(file) + CopyPrefix;
                var our = Directory.GetFiles(folder, itemName + "*.bak")
                    .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                    .Skip(KeptCopies)
                    .ToList();
                foreach (var old in our)
                {
                    try { File.Delete(old); }
                    catch { }
                }
                if (our.Count > 0)
                    tell("removed " + our.Count + " old backup copies (keeping " + KeptCopies + ")");
            }
            catch { /* failing to clean up is no reason not to install*/ }
        }

        /// <summary>
        /// Puts the line breaks back and indents the JSON.
        ///
        /// `JavaScriptSerializer` writes everything on a single line. Technically
        /// it is valid JSON; practically it means that whoever opens the file after
        /// us finds a line of two thousand characters where before there was something
        /// readable, and can no longer fix it by hand. An installer that
        /// makes a user's configuration file unreadable has done
        /// damage even if the content is right.
        ///
        /// It works on the text and not on the object because only spacing is needed:
        /// the only thing to be careful about is the strings, where brackets and
        /// commas are not structure.
        /// </summary>
        private static string Indent(string json)
        {
            var outside = new StringBuilder(json.Length * 2);
            int level = 0;
            bool inString = false, escaped = false;

            foreach (char c in json)
            {
                if (inString)
                {
                    outside.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        outside.Append(c);
                        break;
                    case '{':
                    case '[':
                        outside.Append(c);
                        level++;
                        outside.Append('\n').Append(new string(' ', level * 2));
                        break;
                    case '}':
                    case ']':
                        level--;
                        outside.Append('\n').Append(new string(' ', Math.Max(0, level) * 2));
                        outside.Append(c);
                        break;
                    case ',':
                        outside.Append(c).Append('\n').Append(new string(' ', level * 2));
                        break;
                    case ':':
                        outside.Append(": ");
                        break;
                    default:
                        outside.Append(c);
                        break;
                }
            }
            return outside.ToString().Replace("{\n" + new string(' ', 2) + "\n}", "{}") + "\n";
        }

        private static bool RedistStandard(string redist)
        {
            try
            {
                var standard = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"SOLIDWORKS Corp\SOLIDWORKS\api\redist");
                return Steps.EqualPaths(redist, standard);
            }
            catch { return false; }
        }

        /// <summary>The block to paste by hand, for whoever uses another client.</summary>
        public static string BlockToPaste(string executable, string redist, string weldmentProfiles)
        {
            var entry = new Dictionary<string, object> { ["command"] = executable };
            var env = EnvFor(redist, weldmentProfiles);
            if (env.Count > 0) entry["env"] = env.ToDictionary(kv => kv.Key, kv => (object)kv.Value);

            var root = new Dictionary<string, object>
            {
                ["mcpServers"] = new Dictionary<string, object> { [ServerName] = entry },
            };
            return Indent(Json().Serialize(root));
        }
    }
}
