using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE NETS OF W3.1-bis.2 THAT LIVE AS SCRIPTS, SEEN RED ON EVERY RUN.
    ///
    /// Five checks of the translation are scripts, not contracts: the token
    /// streams of C#, JavaScript and PowerShell (`token-stream.js`), the
    /// disassembled assemblies (`same-il.ps1`), the JSON readers of the checks
    /// that must exit 1 when an expected key is missing
    /// (`expected-keys-test.js`), the reading of the bridge's answers in
    /// `test-x2.js`, and the snapshot every later step of W3.1-bis is
    /// compared with (`snapshot.js`, item 11). Each one has a self-test that
    /// feeds it fake cases, one per way of failing, and expects red where it
    /// must and green where it must. The pre-push hook runs three of them, but
    /// a hook is a convenience of one clone: here they run with the suite, on
    /// every machine and on the CI runner.
    ///
    /// A self-test that cannot run on this machine (no Node, no Roslyn from
    /// MSBuild, no ildasm: exit 2) is «not runnable here», never green.
    /// </summary>
    internal static class SafetyNetSelfTests
    {
        private sealed class SelfTest
        {
            internal string Title;
            internal string Exe;
            internal string Script;
            internal string Arguments;
            internal Regex Summary;
            internal Func<Match, bool> Enough;
        }

        internal static void All()
        {
            Group("the script nets of W3.1-bis.2 turn red on their fakes");

            var root = Root();
            var tests = new[]
            {
                new SelfTest
                {
                    Title = "token-stream.js: every mode turns red on a changed token, green on the same tokens",
                    Exe = "node", Script = Path.Combine(root, "Checks", "language", "token-stream.js"),
                    Arguments = "--self-test",
                    Summary = new Regex(@"(\d+) of (\d+) cases as expected"),
                    Enough = m => m.Groups[1].Value == m.Groups[2].Value && int.Parse(m.Groups[2].Value) >= 31,
                },
                new SelfTest
                {
                    Title = "expected-keys-test.js: every JSON reader of the checks exits 1 on a missing key",
                    Exe = "node", Script = Path.Combine(root, "Checks", "expected-keys-test.js"),
                    Arguments = "",
                    Summary = new Regex(@"Every reader fails when an expected key is missing, and passes on what is there \((\d+) cases\)"),
                    Enough = m => int.Parse(m.Groups[1].Value) >= 25,
                },
                new SelfTest
                {
                    Title = "test-x2.js: an answer without its text is a failure, not a pass",
                    Exe = "node", Script = Path.Combine(root, "Checks", "test-x2.js"),
                    Arguments = "--self-test",
                    Summary = new Regex(@"The reading fails on every answer without the expected keys\."),
                    Enough = m => true,
                },
                new SelfTest
                {
                    Title = "snapshot.js: a changed literal, a vanished assertion target, a changed outcome and a count dropped to 0 are red, an unstable case is apart",
                    Exe = "node", Script = Path.Combine(root, "Checks", "language", "snapshot.js"),
                    Arguments = "--self-test",
                    Summary = new Regex(@"(\d+) of (\d+) checks as expected"),
                    Enough = m => m.Groups[1].Value == m.Groups[2].Value && int.Parse(m.Groups[2].Value) >= 28,
                },
                new SelfTest
                {
                    Title = "same-il.ps1: a changed literal, instruction, resource or unmapped rename is red",
                    Exe = "powershell.exe", Script = Path.Combine(root, "Checks", "language", "same-il.ps1"),
                    Arguments = "-SelfTest",
                    Summary = new Regex(@"(\d+) of (\d+) comparisons as expected"),
                    Enough = m => m.Groups[1].Value == m.Groups[2].Value && int.Parse(m.Groups[2].Value) >= 17,
                },
                // THE AUTONOMOUS RUN OF THE TASKS (D9, entry W3.1-bis.5): the three scripts
                // that decide what a session starts from. They need no SOLIDWORKS: the
                // rule that closes every document between two sessions (with the defect of
                // 6/10 reproduced and a document that never closes), the reference plans of
                // the tasks that start from another task (against the catalogue, the index
                // against the texts of the tasks, the whole build against a script that
                // plays the bridge), and the scorer that reads what the sessions kept.
                new SelfTest
                {
                    Title = "session-documents.js: every document the run opened is closed between two sessions, and none that was open before it; one that never closes is named",
                    Exe = "node", Script = Path.Combine(root, "Bench", "tasks", "session-documents.js"),
                    Arguments = "--self-test",
                    Summary = new Regex(@"self-test: all held"),
                    Enough = m => true,
                },
                new SelfTest
                {
                    Title = "prerequisite.js: the reference plans hold against the catalogue, every task that names another task has its plan, a plan that is not the part of its task is refused",
                    Exe = "node", Script = Path.Combine(root, "Bench", "tasks", "prerequisite.js"),
                    Arguments = "--self-test",
                    Summary = new Regex(@"self-test: all held"),
                    Enough = m => true,
                },
                new SelfTest
                {
                    Title = "score-autonomous.js: the tables, the outcomes of both bridges and a session that did not run for its prerequisite",
                    Exe = "node", Script = Path.Combine(root, "Bench", "tasks", "score-autonomous.js"),
                    Arguments = "--self-test",
                    Summary = new Regex(@"self-test: all held"),
                    Enough = m => true,
                },
            };

            foreach (var t in tests)
            {
                if (!File.Exists(t.Script)) { Check(t.Title, false, "missing: " + t.Script); continue; }

                string arguments = t.Exe == "node"
                    ? Q(t.Script) + (t.Arguments.Length > 0 ? " " + t.Arguments : "")
                    : "-NoProfile -ExecutionPolicy Bypass -File " + Q(t.Script) + " " + t.Arguments;

                string output;
                int code = Run(t.Exe, arguments, root, out output);
                if (code == -2)
                {
                    NotHere(t.Title, t.Exe + " is not on this machine (" + output + "). Not running it is not passing.");
                    continue;
                }
                if (code == 2)
                {
                    NotHere(t.Title, "the self-test says it cannot run here (exit 2): " + Tail(output));
                    continue;
                }

                var m = t.Summary.Match(output);
                bool ok = code == 0 && m.Success && t.Enough(m);
                Check(t.Title, ok, ok ? m.Value : "exit " + code + ": " + Tail(output));
            }
        }

        private static string Q(string s)
        {
            return "\"" + s + "\"";
        }

        private static string Tail(string s)
        {
            var lines = (s ?? "").Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToList();
            return string.Join(" | ", lines.Skip(Math.Max(0, lines.Count - 4)));
        }

        /// <summary>-2 when the program itself cannot be started.</summary>
        private static int Run(string exe, string arguments, string directory, out string output)
        {
            Process p;
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
                p = Process.Start(info);
            }
            catch (Exception ex)
            {
                output = ex.GetType().Name + ": " + ex.Message;
                return -2;
            }

            using (p)
            {
                var err = p.StandardError.ReadToEndAsync();
                output = p.StandardOutput.ReadToEnd() + err.Result;
                if (!p.WaitForExit(300000)) { try { p.Kill(); } catch { } output += "\n(timed out)"; return -1; }
                return p.ExitCode;
            }
        }
    }
}
