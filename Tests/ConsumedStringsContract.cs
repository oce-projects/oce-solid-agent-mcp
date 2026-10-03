using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// EVERY STRING ANOTHER PROGRAM LOOKS FOR IS STILL PRODUCED (entry
    /// W3.1-bis.2, item 6).
    ///
    /// The installer finds out that the bridge started by reading one of its
    /// sentences; `Banco/banco.ps1` builds the bench report from the lines of
    /// the bench; the release script quotes the summary line of this very
    /// suite. Translate one side and the other keeps searching for the old
    /// text, finds nothing, and mostly says nothing. The pairs are listed in
    /// `Controlli/lingua/consumed-strings.json` and checked by
    /// `Controlli/lingua/consumed-strings.js`, which shares its lexers'
    /// language with the rest of the language tooling (Node); this contract
    /// runs it, with its self-test first: a checker never seen red is not a
    /// checker.
    /// </summary>
    internal static class ConsumedStringsContract
    {
        /// <summary>
        /// The inventory may grow; it shrinks only on purpose (a consumer that
        /// was deleted), and then this number goes down in the same commit.
        /// 39 entries on 30/9/2026.
        /// </summary>
        private const int MinimumEntries = 39;

        internal static void RunAll()
        {
            Gruppo("every string another program looks for is still produced (W3.1-bis.2)");

            var root = Radice();
            var script = Path.Combine(root, "Controlli", "lingua", "consumed-strings.js");
            if (!File.Exists(script)) { Segna("the consumed-strings check exists", false, script); return; }

            string output;
            if (Run("node", "--version", root, out output) != 0)
            {
                NonQui("every consumed string is still produced",
                       "node is not on this machine: the check is a Node script, and the CI runner has Node. " +
                       "Not running it here is not the same as passing.");
                return;
            }

            // 1. the checker, on its fakes: a string taken out of its producer,
            //    kept only in a comment, a consumer translated alone...
            int code = Run("node", Q(script) + " --self-test", root, out output);
            var fakes = Regex.Match(output, @"self-test: (\d+) fakes, (\d+) ended otherwise");
            Segna("the consumed-strings check turns red on its fakes (a string taken out of its producer, and more)",
                  code == 0 && fakes.Success && fakes.Groups[2].Value == "0" && int.Parse(fakes.Groups[1].Value) >= 20,
                  code == 0 && fakes.Success ? fakes.Value : Tail(output));

            // 2. the inventory on this tree
            code = Run("node", Q(script) + " --root " + Q(root), root, out output);
            var summary = Regex.Match(output, @"consumed strings: (\d+) entries, (\d+) runs checked, (\d+) broken");
            Segna("every consumed string is still in a string literal of its producer",
                  code == 0 && summary.Success && summary.Groups[3].Value == "0",
                  code == 0 && summary.Success ? summary.Value : Tail(output));

            int entries = summary.Success ? int.Parse(summary.Groups[1].Value) : -1;
            Segna("the inventory of consumed strings has not shrunk",
                  entries >= MinimumEntries,
                  entries < 0 ? "the check did not say how many entries it read"
                              : entries + " entries, floor " + MinimumEntries);
        }

        private static string Q(string s)
        {
            return "\"" + s + "\"";
        }

        private static string Tail(string s)
        {
            var lines = (s ?? "").Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToList();
            return string.Join(" | ", lines.Skip(Math.Max(0, lines.Count - 6)));
        }

        private static int Run(string exe, string arguments, string directory, out string output)
        {
            try
            {
                var info = new ProcessStartInfo(exe, arguments)
                {
                    WorkingDirectory = directory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                using (var p = Process.Start(info))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    output = p.StandardOutput.ReadToEnd() + err.Result;
                    if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } output += "\n(timed out)"; return -1; }
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.GetType().Name + ": " + ex.Message;
                return -1;
            }
        }
    }
}
