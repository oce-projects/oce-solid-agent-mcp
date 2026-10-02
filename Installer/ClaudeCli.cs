using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Oce.Solid.Agent.Installer
{
    /// <summary>
    /// CLAUDE CODE IS WIRED THROUGH ITS OWN COMMAND, WHEN THERE IS ONE (entry
    /// H19, B5).
    ///
    /// The installer used to wire Claude Code by reading the whole of
    /// %USERPROFILE%\.claude.json, adding its entry and writing the whole file
    /// back. That file is not a configuration file Claude Code reads once: a
    /// running Claude Code rewrites it on its own. Measured on 1 October 2026
    /// with Controlli/claude-json-watch.js (read only) while Claude Code was
    /// running on this machine: 5 rewrites in 180 seconds, the shortest gap
    /// about 20 seconds, no lock file beside it, and a leftover
    /// `.claude.json.tmp.&lt;pid&gt;.&lt;hex&gt;`, i.e. Claude Code writes a temporary
    /// file and renames it over the real one. Two consequences follow from
    /// that behaviour alone:
    ///
    /// 1. the installer's "is the file locked?" check can never see Claude
    ///    Code: a writer that renames holds no handle on the file;
    /// 2. if a running session writes back a copy it read before the
    ///    installer wrote, the "solidworks" entry is gone, with no error.
    ///    Whether Claude Code re-reads the file before each write (which
    ///    would make 2 harmless) is NOT measured: that needs a live session
    ///    under test, and stays for the owner (H19 L4, B8).
    ///
    /// `claude mcp add --scope user` is Claude Code's own supported way to do
    /// this, so its own writer does the merge. When `claude` is on PATH it is
    /// used; when it is not, or it fails, the installer writes the file as
    /// before and says so.
    /// </summary>
    internal static class ClaudeCli
    {
        /// <summary>How long one `claude mcp ...` may take.</summary>
        private const int TimeoutMs = 60000;

        /// <summary>The `claude` program on PATH, or null. claude.exe before claude.cmd.</summary>
        public static string Find()
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var raw in path.Split(';'))
            {
                var dir = raw.Trim().Trim('"');
                if (dir.Length == 0) continue;
                foreach (var name in new[] { "claude.exe", "claude.cmd", "claude.bat" })
                {
                    try
                    {
                        var f = Path.Combine(dir, name);
                        if (File.Exists(f)) return f;
                    }
                    catch { /* a malformed PATH entry is not a reason to stop looking */ }
                }
            }
            return null;
        }

        /// <summary>
        /// Registers the bridge in Claude Code's user scope: first
        /// `claude mcp remove --scope user solidworks` (an absent entry is not
        /// an error: `add` refuses a name that exists), then
        /// `claude mcp add --scope user solidworks -e "K=V" ... -- "exe"`.
        /// True when `add` exited 0.
        /// </summary>
        public static bool Register(string claude, string exe, IDictionary<string, string> env, Action<string> tell)
        {
            tell = tell ?? (s => { });
            string output;
            Run(claude, "mcp remove --scope user " + Configurazione.NomeServer, out output);

            var args = new StringBuilder("mcp add --scope user " + Configurazione.NomeServer);
            if (env != null)
                foreach (var kv in env.OrderBy(k => k.Key, StringComparer.Ordinal))
                    args.Append(" -e ").Append(Quote(kv.Key + "=" + kv.Value));
            args.Append(" -- ").Append(Quote(exe));

            int code = Run(claude, args.ToString(), out output);
            if (code == 0)
            {
                tell("Claude Code: registered with \"claude mcp add --scope user\"");
                return true;
            }
            tell("Claude Code: \"claude mcp add\" exited " + code +
                 (string.IsNullOrWhiteSpace(output) ? "" : ": " + Last(output)));
            return false;
        }

        /// <summary>`claude mcp remove --scope user solidworks`; true when it exited 0.</summary>
        public static bool Unregister(string claude, Action<string> tell)
        {
            tell = tell ?? (s => { });
            string output;
            int code = Run(claude, "mcp remove --scope user " + Configurazione.NomeServer, out output);
            if (code == 0) tell("Claude Code: removed with \"claude mcp remove --scope user\"");
            else tell("Claude Code: \"claude mcp remove\" exited " + code +
                      (string.IsNullOrWhiteSpace(output) ? "" : ": " + Last(output)));
            return code == 0;
        }

        /// <summary>
        /// One argument, quoted so that the Windows rules for splitting a
        /// command line (CommandLineToArgvW, which claude.exe and node follow)
        /// give it back unchanged (H19 review). The first version only
        /// escaped the quotes: a value ending in a backslash, such as
        /// `PROFILI_SALDATURA=D:\Profiles\`, came out as `...\"`, an escaped
        /// quote, and swallowed the arguments after it. Backslashes are
        /// literal except right before a quote, where they are doubled.
        /// </summary>
        internal static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char ch in s ?? "")
            {
                if (ch == '\\') { backslashes++; continue; }
                if (ch == '"') sb.Append('\\', backslashes * 2 + 1);
                else sb.Append('\\', backslashes);
                sb.Append(ch);
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2);
            return sb.Append('"').ToString();
        }

        private static string Last(string s)
        {
            var lines = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? "" : lines[lines.Length - 1].Trim();
        }

        /// <summary>
        /// Runs claude with these arguments and returns its exit code (-1 if it
        /// could not run or did not finish). A .cmd or .bat (the npm install)
        /// goes through cmd.exe, which is the only thing that can run it.
        /// </summary>
        private static int Run(string claude, string args, out string output)
        {
            output = "";
            try
            {
                bool script = claude.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                              claude.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
                // cmd.exe expands %NAME% even inside quotes, and there is no
                // escape for it on a command line: a path with a % in it
                // would reach claude changed. Better not to run it at all;
                // the caller then writes the file, and says so.
                if (script && args.IndexOf('%') >= 0)
                {
                    output = "an argument contains %, which cmd.exe would expand";
                    return -1;
                }
                var psi = script
                    ? new ProcessStartInfo("cmd.exe", "/d /s /c \"\"" + claude + "\" " + args + "\"")
                    : new ProcessStartInfo(claude, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    var outp = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(TimeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        output = "did not finish within " + (TimeoutMs / 1000) + " s";
                        return -1;
                    }
                    output = (outp.Result + err.Result).Trim();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }
    }
}
