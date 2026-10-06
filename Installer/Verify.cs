using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Oce.Solid.Agent.Installer
{
    /// <summary>
    /// `Installer.exe --verify [--json]`: IS THIS INSTALLATION ONE A CLIENT CAN
    /// USE? (entry H19, B4)
    ///
    /// The installer says what it did; this says what is there, read back
    /// from the disk and from the bridge itself, the way the client will find
    /// it. Five checks, each ok, warn or fail:
    ///
    ///   files       every file the install puts down is there; the versions
    ///               of the bridge, the library and this installer
    ///   solidworks  where api\redist is, and which SOLIDWORKS it belongs to
    ///   clients     for each MCP client, whether its "solidworks" entry runs
    ///               the installed exe; and whether OCE_WELDMENT_PROFILES is in it
    ///   self-test   the exit code of the bridge's --self-test (0, 1, 3, 4, 5, 6)
    ///   handshake   initialize and tools/list over stdio with the installed
    ///               exe, as a client does, and the number of tools
    ///
    /// Exit 0 only when everything is ok; 1 when something failed; 2 when
    /// nothing failed but something is only a warning (SOLIDWORKS not running,
    /// a SOLIDWORKS version nobody measured, no weldment profiles). With
    /// --json, ONE line of JSON for a program to read; without, one readable
    /// line per check.
    /// </summary>
    internal static class Verify
    {
        public const int AllGreen = 0;
        public const int Failed = 1;
        public const int WarningsOnly = 2;

        private const string Ok = "ok", Warn = "warn", Fail = "fail";

        private sealed class Check
        {
            public string Name, Status, Detail;
        }

        public static int Run(string folder, string redist, string chosenConfig, bool json)
        {
            var checks = new List<Check>();
            var report = new Dictionary<string, object>();
            Action<string, string, string> add = (n, s, d) => checks.Add(new Check { Name = n, Status = s, Detail = d });

            var exe = Path.Combine(folder, Steps.ExecutableName);
            var installer = InstallerVersion();
            report["installer"] = installer;
            report["folder"] = folder;

            // ── files ────────────────────────────────────────────────────
            var files = new List<object>();
            var missing = new List<string>();
            foreach (var name in Steps.InstalledFiles)
            {
                var f = Path.Combine(folder, name);
                bool present = File.Exists(f);
                string version = null;
                if (present && (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    try { version = FileVersionInfo.GetVersionInfo(f).ProductVersion; } catch { }
                }
                files.Add(new Dictionary<string, object> { ["name"] = name, ["present"] = present, ["version"] = version });
                if (!present) missing.Add(name);
            }
            report["files"] = files;
            string bridgeVersion = File.Exists(exe) ? Steps.VersionOf(exe) : null;
            string libraryVersion = null;
            try
            {
                var lib = Path.Combine(folder, Steps.LibraryName);
                if (File.Exists(lib)) libraryVersion = FileVersionInfo.GetVersionInfo(lib).FileVersion;
            }
            catch { }
            report["bridgeVersion"] = bridgeVersion;
            report["libraryVersion"] = libraryVersion;
            if (missing.Count > 0)
                add("files", Fail, "missing in " + folder + ": " + string.Join(", ", missing));
            else if (bridgeVersion != null && installer != null && bridgeVersion != installer)
                add("files", Warn, "all present; the bridge installed is " + bridgeVersion +
                                   ", this installer carries " + installer + " (run it to update)");
            else
                add("files", Ok, "all " + Steps.InstalledFiles.Length + " present in " + folder +
                                 "; bridge " + (bridgeVersion ?? "?") + ", library " + (libraryVersion ?? "?"));

            // ── SOLIDWORKS ───────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(redist)) redist = Steps.FindRedist();
            string interop = null;
            int major = 0;
            if (Steps.FolderHasInterop(redist))
            {
                try
                {
                    interop = FileVersionInfo.GetVersionInfo(
                        Path.Combine(redist, "SolidWorks.Interop.sldworks.dll")).FileVersion;
                }
                catch { }
                major = Steps.MainVersion(interop);
            }
            report["solidworks"] = new Dictionary<string, object>
            {
                ["redist"] = Steps.FolderHasInterop(redist) ? redist : null,
                ["interop"] = interop,
                ["year"] = major > 0 ? (object)(major + 1992) : null,
            };
            if (!Steps.FolderHasInterop(redist))
                add("solidworks", Fail, "api\\redist not found: pass --redist <folder> or set SW_INSTALL_PATH to the SOLIDWORKS root");
            else if (major > 0 && major != Steps.TestedVersion)
                add("solidworks", Warn, redist + " (SOLIDWORKS " + (major + 1992) +
                                        "): the bridge is measured only on SOLIDWORKS 2021 SP5.1");
            else
                add("solidworks", Ok, redist + (interop == null ? "" : " (interop " + interop +
                                       (major > 0 ? ", SOLIDWORKS " + (major + 1992) : "") + ")"));

            // ── clients ──────────────────────────────────────────────────
            var clients = string.IsNullOrWhiteSpace(chosenConfig)
                ? Configuration.FoundClients().Where(c => c.Installed).ToList()
                : new List<Client> { new Client { Name = "chosen file", File = chosenConfig, Installed = true } };
            var clientRows = new List<object>();
            var wired = new List<string>();
            var elsewhere = new List<string>();
            var unreadable = new List<string>();
            var noEntry = new List<string>();
            var profiles = new List<string>();
            foreach (var c in clients)
            {
                var command = Configuration.HowItIsConfigured(c);
                bool points = command != null && Steps.EqualPaths(command, exe);
                var env = Configuration.ConfiguredEnv(c);
                // A file that cannot be read is not a file without an entry
                // (H19 review): the install that met it exited 3, and this
                // must not say all green behind it.
                var problem = Configuration.ReadProblem(c);
                string prof = null;
                if (env != null) env.TryGetValue("OCE_WELDMENT_PROFILES", out prof);
                clientRows.Add(new Dictionary<string, object>
                {
                    ["name"] = c.Name, ["file"] = c.File, ["command"] = command,
                    ["pointsToInstalled"] = points, ["weldmentProfiles"] = prof, ["readError"] = problem,
                });
                if (points) { wired.Add(c.Name); if (!string.IsNullOrWhiteSpace(prof)) profiles.Add(prof); }
                else if (problem != null) unreadable.Add(c.Name + " (" + c.File + ")");
                else if (command != null) elsewhere.Add(c.Name + " -> " + command);
                else noEntry.Add(c.Name);
            }
            report["clients"] = clientRows;
            var notWiredNote = noEntry.Count > 0 ? "; no entry in: " + string.Join(", ", noEntry) : "";
            if (wired.Count == 0)
                add("clients", Fail, clients.Count == 0
                    ? "no MCP client found: install Claude Desktop or Claude Code, or name a file with --config"
                    : "no client runs " + exe +
                      (elsewhere.Count > 0 ? " (" + string.Join("; ", elsewhere) + ")" : "") +
                      (unreadable.Count > 0 ? " (config that cannot be read: " + string.Join("; ", unreadable) + ")" : "") +
                      ": run the installer again, or paste the block it prints");
            else if (elsewhere.Count > 0 || unreadable.Count > 0)
                add("clients", Warn, "wired: " + string.Join(", ", wired) +
                                     (elsewhere.Count > 0 ? "; pointing elsewhere: " + string.Join("; ", elsewhere) : "") +
                                     (unreadable.Count > 0 ? "; config that cannot be read (malformed or locked): " +
                                                             string.Join("; ", unreadable) : "") + notWiredNote);
            else
                add("clients", Ok, "wired: " + string.Join(", ", wired) + notWiredNote + " (restart a client that was open)");

            if (wired.Count > 0)
            {
                var good = profiles.Where(Directory.Exists).ToList();
                if (good.Count > 0)
                    add("weldment profiles", Ok, "OCE_WELDMENT_PROFILES = " + good[0]);
                else if (profiles.Count > 0)
                    add("weldment profiles", Warn, "OCE_WELDMENT_PROFILES points at a folder that does not exist: " + profiles[0]);
                else
                    add("weldment profiles", Warn, "OCE_WELDMENT_PROFILES is not set: weldment_profiles will refuse " +
                                                  "(install again with --weldment-profiles <folder>)");
            }

            // ── self-test ────────────────────────────────────────────────
            int code = Steps.SelfTestNoAnswer;
            if (File.Exists(exe))
            {
                Steps.Test(exe, redist, out code);
                report["selfTest"] = new Dictionary<string, object> { ["code"] = code, ["meaning"] = Steps.SelfTestMeaning(code) };
                if (code == Steps.SelfTestAttached)
                    add("self-test", Ok, "exit 0: " + Steps.SelfTestMeaning(code));
                else if (code == Steps.SelfTestNotRunning || code == Steps.SelfTestSkipped)
                    add("self-test", Warn, "exit " + code + ": " + Steps.SelfTestMeaning(code) +
                                           (code == Steps.SelfTestNotRunning ? ": open SOLIDWORKS and verify again" : ""));
                else
                    add("self-test", Fail, "exit " + code + ": " + Steps.SelfTestMeaning(code));
            }
            else
            {
                report["selfTest"] = null;
                add("self-test", Fail, Steps.ExecutableName + " is not installed in " + folder);
            }

            // ── MCP handshake ────────────────────────────────────────────
            if (File.Exists(exe))
            {
                var hs = Handshake(exe, redist);
                report["handshake"] = hs;
                if ((bool)hs["ok"])
                    add("handshake", Ok, "initialize and tools/list answered: " + hs["tools"] +
                                         " tools, server version " + hs["serverVersion"]);
                else
                    add("handshake", Fail, Convert.ToString(hs["detail"]));
            }
            else
            {
                report["handshake"] = new Dictionary<string, object> { ["ok"] = false, ["tools"] = 0, ["detail"] = "not installed" };
                add("handshake", Fail, "not installed");
            }

            int exit = checks.Any(c => c.Status == Fail) ? Failed
                     : checks.Any(c => c.Status == Warn) ? WarningsOnly : AllGreen;
            report["checks"] = checks.Select(c => (object)new Dictionary<string, object>
                { ["name"] = c.Name, ["status"] = c.Status, ["detail"] = c.Detail }).ToList();
            report["ok"] = exit == AllGreen;
            report["exit"] = exit;

            if (json)
            {
                var s = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                Console.WriteLine(s.Serialize(report));
            }
            else
            {
                Console.WriteLine(Steps.ProductName + " - verify (installer " + installer + ")");
                foreach (var c in checks)
                    Console.WriteLine((c.Status == Ok ? "[ok] " : c.Status == Warn ? "[~]  " : "[NO] ") + c.Name + ": " + c.Detail);
                Console.WriteLine(exit == AllGreen ? "verify: all green (exit 0)"
                    : exit == WarningsOnly ? "verify: nothing failed, warnings above (exit 2)"
                    : "verify: something failed, see [NO] above (exit 1)");
            }
            return exit;
        }

        private static string InstallerVersion()
        {
            try
            {
                var a = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    Assembly.GetExecutingAssembly(), typeof(AssemblyInformationalVersionAttribute));
                return a?.InformationalVersion;
            }
            catch { return null; }
        }

        /// <summary>
        /// initialize, notifications/initialized, tools/list: what a client
        /// sends first. Every wait has a timeout (the first one is long: the
        /// bridge attaches to SOLIDWORKS while it starts), and a reply is
        /// recognised by its id, so the bridge's own notifications in between
        /// do not count as answers.
        /// </summary>
        private static Dictionary<string, object> Handshake(string exe, string redist)
        {
            var result = new Dictionary<string, object> { ["ok"] = false, ["tools"] = 0, ["serverVersion"] = null, ["detail"] = null };
            Process p = null;
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    StandardOutputEncoding = new UTF8Encoding(false),
                };
                if (!string.IsNullOrWhiteSpace(redist))
                {
                    psi.EnvironmentVariables.Remove("SW_INSTALL_PATH");
                    psi.EnvironmentVariables["SW_REDIST"] = redist;
                }
                p = Process.Start(psi);
                p.ErrorDataReceived += (s, e) => { };
                p.BeginErrorReadLine();

                var lines = new BlockingCollection<string>();
                var reader = new Thread(() =>
                {
                    try { string l; while ((l = p.StandardOutput.ReadLine()) != null) lines.Add(l); }
                    catch { }
                    finally { lines.CompleteAdding(); }
                }) { IsBackground = true };
                reader.Start();

                // Our own writer: no byte order mark in front of the first line.
                var input = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
                input.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\"," +
                                "\"capabilities\":{},\"clientInfo\":{\"name\":\"oce-installer-verify\",\"version\":\"1\"}}}");
                var init = WaitFor(lines, 1, 90000);
                if (init == null) { result["detail"] = "no answer to initialize within 90 s"; return result; }
                var info = Get(Get(init, "result"), "serverInfo");
                result["serverVersion"] = Convert.ToString(Get(info, "version"));

                input.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                input.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");
                var list = WaitFor(lines, 2, 60000);
                if (list == null) { result["detail"] = "no answer to tools/list within 60 s"; return result; }
                var tools = Get(Get(list, "result"), "tools") as System.Collections.IList;
                int n = tools == null ? 0 : tools.Count;
                result["tools"] = n;
                result["ok"] = n > 0;
                if (n == 0) result["detail"] = "tools/list answered with no tools";
                try { input.Close(); } catch { }
                return result;
            }
            catch (Exception ex)
            {
                result["detail"] = "the bridge could not be run: " + ex.Message;
                return result;
            }
            finally
            {
                if (p != null)
                {
                    try { if (!p.WaitForExit(10000)) p.Kill(); } catch { }
                    p.Dispose();
                }
            }
        }

        private static object Get(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        private static Dictionary<string, object> WaitFor(BlockingCollection<string> lines, int id, int ms)
        {
            var s = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var until = Environment.TickCount + ms;
            while (true)
            {
                int left = until - Environment.TickCount;
                if (left <= 0) return null;
                string line;
                try { if (!lines.TryTake(out line, left)) return null; }
                catch (InvalidOperationException) { return null; }
                if (string.IsNullOrWhiteSpace(line)) continue;
                Dictionary<string, object> m;
                try { m = s.DeserializeObject(line) as Dictionary<string, object>; }
                catch { continue; }
                if (m != null && m.ContainsKey("id") && Convert.ToString(m["id"]) == id.ToString()) return m;
            }
        }
    }
}
