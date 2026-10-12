using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE SELF-TEST SPEAKS IN CODES, AND BOTH SIDES AGREE ON THEM (entry H19, B3).
    ///
    /// Until 0.125.0 the installer decided whether an install had worked by
    /// looking for two sentences in the output of the bridge's --self-test. Now
    /// --self-test exits 0, 1, 3, 4, 5 or 6, and the installer reads the number. The
    /// numbers are written twice, in the bridge (Program.SelfTest*) and in
    /// the installer (Steps.SelfTest*), because the installer cannot reference
    /// the bridge's assembly: two lists of the same thing are two things that
    /// drift apart the day one is touched, so this reads both sources and
    /// compares them. And it checks, by reflection on the bridge, that each
    /// HRESULT of a failed attach becomes the code the table promises, without
    /// SOLIDWORKS.
    /// </summary>
    internal static class H19InstallerContract
    {
        internal static void RunAll()
        {
            SelfTestCodes();
            TheInstallerByReflection();
        }

        private static void SelfTestCodes()
        {
            Group("the self-test exit codes: HRESULT to code, and the same numbers in bridge and installer (H19 B3)");

            var bridge = Codes(Path.Combine(Root(), "Program.cs"));
            var installer = Codes(Path.Combine(Root(), "Installer", "Steps.cs"));
            Check("the bridge declares the self-test codes", bridge.Count >= 6,
                  string.Join(", ", bridge.Select(kv => kv.Key + "=" + kv.Value)));
            var differ = bridge.Where(kv => !installer.ContainsKey(kv.Key) || installer[kv.Key] != kv.Value)
                               .Select(kv => kv.Key + " bridge " + kv.Value + ", installer " +
                                             (installer.ContainsKey(kv.Key) ? installer[kv.Key].ToString() : "missing"))
                               .ToList();
            Check("the installer has the same numbers as the bridge", bridge.Count >= 6 && differ.Count == 0,
                  differ.Count == 0 ? bridge.Count + " codes agree" : string.Join("; ", differ));

            var bridgeExe = BridgeOrNotHere("the self-test codes, by reflection on the bridge");
            if (bridgeExe == null) return;

            MethodInfo failureOf, exitCode;
            try
            {
                var asm = Load.Bridge(bridgeExe);
                var session = Load.Class(asm, "Session");
                var program = Load.Class(asm, "Program");
                failureOf = session?.GetMethod("FailureOf", BindingFlags.NonPublic | BindingFlags.Static);
                exitCode = program?.GetMethod("SelfTestExitCode", BindingFlags.NonPublic | BindingFlags.Static);
                if (failureOf == null || exitCode == null)
                {
                    Check("Session.FailureOf and Program.SelfTestExitCode exist", false,
                          (failureOf == null ? "FailureOf missing " : "") + (exitCode == null ? "SelfTestExitCode missing" : ""));
                    return;
                }
            }
            catch (Exception ex) { Check("the bridge loads", false, ex.ToString()); return; }

            Func<uint, int> code = h =>
                (int)exitCode.Invoke(null, new[] { failureOf.Invoke(null, new object[] { unchecked((int)h) }) });

            var expected = new[]
            {
                new { H = 0x800401E3u, Code = 3, What = "MK_E_UNAVAILABLE (installed, not running)" },
                new { H = 0x800401F3u, Code = 4, What = "CO_E_CLASSSTRING (not installed)" },
                new { H = 0x80040154u, Code = 4, What = "REGDB_E_CLASSNOTREG (not registered)" },
                new { H = 0x80004005u, Code = 1, What = "E_FAIL (anything else)" },
            };
            foreach (var x in expected)
            {
                int got = code(x.H);
                Check("0x" + x.H.ToString("X8") + " " + x.What + ": --self-test exits " + x.Code, got == x.Code, "exits " + got);
            }

            // Every value of the enum has a code, and none of them is 2: that
            // number is the installer's own "self-test not confirmed".
            var enumType = failureOf.ReturnType;
            var codes = Enum.GetValues(enumType).Cast<object>()
                            .ToDictionary(v => v.ToString(), v => (int)exitCode.Invoke(null, new[] { v }));
            Check("every AttachFailure has a code, attached is 0 and skipped is 5, and none is 2",
                  codes.Count >= 5 && codes.ContainsKey("None") && codes["None"] == 0 &&
                  codes.ContainsKey("Skipped") && codes["Skipped"] == 5 && !codes.Values.Contains(2),
                  string.Join(", ", codes.Select(kv => kv.Key + "=" + kv.Value)));

            // THE ELEVATED CASE HAS ITS OWN CODE (H19 review). "Not running"
            // while a SLDWORKS process runs in this session exited 3, which the
            // installer accepts: an install with SOLIDWORKS started as
            // administrator said "Installed" and exit 0.
            var seen = Load.Class(Load.Bridge(bridgeExe), "Program")
                ?.GetMethod("SelfTestCodeSeen", BindingFlags.NonPublic | BindingFlags.Static);
            if (seen == null) { Check("Program.SelfTestCodeSeen exists (not running + a SLDWORKS process = 6)", false, "missing"); return; }
            Func<string, bool, int> codeSeen = (f, process) =>
                (int)seen.Invoke(null, new object[] { Enum.Parse(enumType, f), process });
            var rows = new[]
            {
                new { F = "NotRunning", P = true, Code = 6 }, new { F = "NotRunning", P = false, Code = 3 },
                new { F = "None", P = true, Code = 0 }, new { F = "NotInstalled", P = true, Code = 4 },
                new { F = "Skipped", P = true, Code = 5 },
            };
            var wrong = rows.Where(r => codeSeen(r.F, r.P) != r.Code)
                            .Select(r => r.F + (r.P ? "+process" : "") + " gives " + codeSeen(r.F, r.P) + ", not " + r.Code).ToList();
            Check("not running with a SLDWORKS process in this session exits 6; without one 3; the others unchanged",
                  wrong.Count == 0, wrong.Count == 0 ? rows.Length + " rows" : string.Join("; ", wrong));
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// THE INSTALLER'S OWN RULES, BY REFLECTION ON bin\Installer.exe (H19
        /// review), without SOLIDWORKS, without a client and without running
        /// it: how an argument is quoted for `claude mcp add`, which self-test
        /// codes mean "installed", what a reinstall keeps of the entry's env,
        /// and where Claude Code's file is and whether Claude Code is there.
        /// </summary>
        private static void TheInstallerByReflection()
        {
            Group("the installer by reflection: quoting, accepted codes, env kept, Claude Code found (H19 review)");
            var exe = Path.Combine(Root(), "bin", "Installer.exe");
            if (!ProductFromBuild(exe))
            {
                if (!TheBridgeCouldBeBuilt())
                {
                    NotHere("the installer by reflection",
                           "without SOLIDWORKS the bridge does not compile, so build.ps1 makes no installer to load.");
                    return;
                }
                Check("the installer is built", false, "no " + exe + ": run .\\build.ps1");
                return;
            }

            Assembly asm;
            try { asm = Assembly.Load(File.ReadAllBytes(exe)); }
            catch (Exception ex) { Check("the installer loads", false, ex.Message); return; }
            var all = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
            var cli = Load.Class(asm, "ClaudeCli");
            var steps = Load.Class(asm, "Steps");
            var conf = Load.Class(asm, "Configuration");
            if (cli == null || steps == null || conf == null) { Check("the three classes of the installer are there", false, ""); return; }

            // 1. Quoting, checked against the Windows splitter itself.
            var quote = cli.GetMethod("Quote", all);
            if (quote == null) Check("ClaudeCli.Quote exists", false, "");
            else
            {
                var values = new[]
                {
                    @"OCE_WELDMENT_PROFILES=D:\Weld Profiles\", @"C:\Some Folder\oce-solid-agent-mcp.exe",
                    @"SW_REDIST=\\server\share\api\redist\", "a\"b", @"ends\\", "", "plain",
                };
                var line = "claude " + string.Join(" ", values.Select(v => (string)quote.Invoke(null, new object[] { v }))) + " -- next";
                var back = Split(line);
                bool same = back.Length == values.Length + 3 &&
                            values.Select((v, i) => back[i + 1] == v).All(x => x) &&
                            back[back.Length - 2] == "--" && back[back.Length - 1] == "next";
                Check("an argument quoted for claude comes back unchanged through CommandLineToArgvW (trailing \\ too)",
                      same, same ? values.Length + " values" : line + " -> [" + string.Join("] [", back) + "]");
            }

            // 2. Which self-test codes mean "installed".
            var accepted = steps.GetMethod("SelfTestAccepted", all);
            if (accepted == null) Check("Steps.SelfTestAccepted exists", false, "");
            else
            {
                var yes = new[] { 0, 1, 3, 4, 5, 6, -1 }.Where(c => (bool)accepted.Invoke(null, new object[] { c })).ToList();
                Check("the installer accepts the self-test codes 0, 3 and 5 only (not 6, SOLIDWORKS running and not answering)",
                      yes.SequenceEqual(new[] { 0, 3, 5 }), "accepted: " + string.Join(", ", yes));
            }

            // 3. What a reinstall keeps of the entry's env.
            var merged = conf.GetMethod("MergedEnv", all);
            if (merged == null) Check("Configuration.MergedEnv exists (a reinstall keeps the entry's own variables)", false, "missing");
            else
            {
                var existing = new Dictionary<string, string>
                {
                    ["OCE_WRITE_ROOT"] = @"C:\Work", ["OCE_WELDMENT_PROFILES"] = @"C:\Mine", ["SW_REDIST"] = @"C:\Old\api\redist",
                };
                var kept = (Dictionary<string, string>)merged.Invoke(null, new object[] { existing, null, null, null });
                var replaced = (Dictionary<string, string>)merged.Invoke(null, new object[] { existing, null, @"C:\New", null });
                bool ok = kept.Count == 2 && kept["OCE_WRITE_ROOT"] == @"C:\Work" && kept["OCE_WELDMENT_PROFILES"] == @"C:\Mine" &&
                          replaced["OCE_WELDMENT_PROFILES"] == @"C:\New" && replaced["OCE_WRITE_ROOT"] == @"C:\Work" &&
                          !replaced.ContainsKey("SW_REDIST");
                Check("a reinstall keeps the entry's own variables, sets OCE_WELDMENT_PROFILES only when it has one, drops a stale SW_REDIST",
                      ok, string.Join(", ", kept.Select(kv => kv.Key + "=" + kv.Value)) + " | " +
                          string.Join(", ", replaced.Select(kv => kv.Key + "=" + kv.Value)));
            }

            // 4. Where Claude Code's file is, and whether Claude Code is there.
            var file = conf.GetMethod("ClaudeCodeFile", all);
            var present = conf.GetMethod("ClaudeCodePresent", all);
            if (file == null || present == null)
            {
                Check("Configuration.ClaudeCodeFile and ClaudeCodePresent exist (Claude Code is not 'installed' everywhere)", false,
                      (file == null ? "ClaudeCodeFile missing " : "") + (present == null ? "ClaudeCodePresent missing" : ""));
                return;
            }
            var keep = new[] { "CLAUDE_CONFIG_DIR", "USERPROFILE", "PATH" }
                .ToDictionary(k => k, Environment.GetEnvironmentVariable);
            var empty = Path.Combine(Path.GetTempPath(), "h19-no-claude-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(empty);
                Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);
                Environment.SetEnvironmentVariable("USERPROFILE", empty);
                Environment.SetEnvironmentVariable("PATH", empty);
                var f = (string)file.Invoke(null, null);
                bool none = !(bool)present.Invoke(null, new object[] { f });
                Directory.CreateDirectory(Path.Combine(empty, ".claude"));
                bool withFolder = (bool)present.Invoke(null, new object[] { f });
                Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", Path.Combine(empty, "cfg"));
                var fCfg = (string)file.Invoke(null, null);
                Check("Claude Code's file is .claude.json in CLAUDE_CONFIG_DIR, else in %USERPROFILE%",
                      f == Path.Combine(empty, ".claude.json") && fCfg == Path.Combine(empty, "cfg", ".claude.json"), f + " | " + fCfg);
                Check("Claude Code is absent in a profile with no .claude.json, no .claude folder and no claude on PATH; present with .claude",
                      none && withFolder, "absent: " + none + ", with .claude: " + withFolder);
            }
            finally
            {
                foreach (var kv in keep) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
                try { Directory.Delete(empty, true); } catch { }
            }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string line,
            out int count);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr mem);

        /// <summary>A command line split the way Windows programs split it.</summary>
        private static string[] Split(string line)
        {
            int n;
            var p = CommandLineToArgvW(line, out n);
            if (p == IntPtr.Zero) return new string[0];
            try
            {
                var result = new string[n];
                for (int i = 0; i < n; i++)
                    result[i] = System.Runtime.InteropServices.Marshal.PtrToStringUni(
                        System.Runtime.InteropServices.Marshal.ReadIntPtr(p, i * IntPtr.Size));
                return result;
            }
            finally { LocalFree(p); }
        }

        /// <summary>`const int SelfTestX = n;` in a source file, by name.</summary>
        private static Dictionary<string, int> Codes(string file)
        {
            var found = new Dictionary<string, int>(StringComparer.Ordinal);
            string text;
            try { text = File.ReadAllText(file); } catch { return found; }
            foreach (Match m in Regex.Matches(text, @"const\s+int\s+(SelfTest(?:Attached|Other|NotRunning|NotInstalled|Skipped|NotReachable))\s*=\s*(\d+)\s*;"))
                found[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
            return found;
        }
    }
}
