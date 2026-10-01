using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// THE SELF-TEST SPEAKS IN CODES, AND BOTH SIDES AGREE ON THEM (entry H19, B3).
    ///
    /// Until 0.125.0 the installer decided whether an install had worked by
    /// looking for two sentences in the output of the bridge's --prova. Now
    /// --prova exits 0, 1, 3, 4 or 5, and the installer reads the number. The
    /// numbers are written twice, in the bridge (Programma.SelfTest*) and in
    /// the installer (Passi.SelfTest*), because the installer cannot reference
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
            Gruppo("the self-test exit codes: HRESULT to code, and the same numbers in bridge and installer (H19 B3)");

            var bridge = Codes(Path.Combine(Radice(), "Programma.cs"));
            var installer = Codes(Path.Combine(Radice(), "Installer", "Passi.cs"));
            Segna("the bridge declares the self-test codes", bridge.Count >= 5,
                  string.Join(", ", bridge.Select(kv => kv.Key + "=" + kv.Value)));
            var differ = bridge.Where(kv => !installer.ContainsKey(kv.Key) || installer[kv.Key] != kv.Value)
                               .Select(kv => kv.Key + " bridge " + kv.Value + ", installer " +
                                             (installer.ContainsKey(kv.Key) ? installer[kv.Key].ToString() : "missing"))
                               .ToList();
            Segna("the installer has the same numbers as the bridge", bridge.Count >= 5 && differ.Count == 0,
                  differ.Count == 0 ? bridge.Count + " codes agree" : string.Join("; ", differ));

            var ponte = PonteOppureNonQui("the self-test codes, by reflection on the bridge");
            if (ponte == null) return;

            MethodInfo failureOf, exitCode;
            try
            {
                var asm = Carica.Ponte(ponte);
                var sessione = Carica.Classe(asm, "Sessione");
                var programma = Carica.Classe(asm, "Programma");
                failureOf = sessione?.GetMethod("FailureOf", BindingFlags.NonPublic | BindingFlags.Static);
                exitCode = programma?.GetMethod("SelfTestExitCode", BindingFlags.NonPublic | BindingFlags.Static);
                if (failureOf == null || exitCode == null)
                {
                    Segna("Sessione.FailureOf and Programma.SelfTestExitCode exist", false,
                          (failureOf == null ? "FailureOf missing " : "") + (exitCode == null ? "SelfTestExitCode missing" : ""));
                    return;
                }
            }
            catch (Exception ex) { Segna("the bridge loads", false, ex.ToString()); return; }

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
                Segna("0x" + x.H.ToString("X8") + " " + x.What + ": --prova exits " + x.Code, got == x.Code, "exits " + got);
            }

            // Every value of the enum has a code, and none of them is 2: that
            // number is the installer's own "self-test not confirmed".
            var enumType = failureOf.ReturnType;
            var codes = Enum.GetValues(enumType).Cast<object>()
                            .ToDictionary(v => v.ToString(), v => (int)exitCode.Invoke(null, new[] { v }));
            Segna("every AttachFailure has a code, attached is 0 and skipped is 5, and none is 2",
                  codes.Count >= 5 && codes.ContainsKey("None") && codes["None"] == 0 &&
                  codes.ContainsKey("Skipped") && codes["Skipped"] == 5 && !codes.Values.Contains(2),
                  string.Join(", ", codes.Select(kv => kv.Key + "=" + kv.Value)));
        }

        /// <summary>`const int SelfTestX = n;` in a source file, by name.</summary>
        private static Dictionary<string, int> Codes(string file)
        {
            var found = new Dictionary<string, int>(StringComparer.Ordinal);
            string text;
            try { text = File.ReadAllText(file); } catch { return found; }
            foreach (Match m in Regex.Matches(text, @"const\s+int\s+(SelfTest(?:Attached|Other|NotRunning|NotInstalled|Skipped))\s*=\s*(\d+)\s*;"))
                found[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
            return found;
        }
    }
}
