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
    /// THE REGISTER OF THE QUESTIONS WITHOUT AN ANSWER (entry Z-G7)
    ///
    /// The register exists to answer a question that nobody can answer
    /// from memory: **which operation to build next.** The five
    /// of the catalogue without bench cases we chose by intuition, and this
    /// file is the way to stop doing so.
    ///
    /// ── WHY IT CAN BE TESTED WITHOUT SOLIDWORKS ──────────────────────────
    ///
    /// Because it knows nothing about it: it writes a line of text when it is told
    /// that a name was not there. It is the part of the bridge that can be measured
    /// in full on the CI machine.
    ///
    /// ── THE TWO RULES THAT HERE ARE DEMANDED, NOT HOPED FOR ──────────────
    ///
    ///  1. **Never on stdout**, where the JSON-RPC passes. The SOURCE is read and
    ///     it is demanded that the word `Console` not be there. A rule that
    ///     concerns a channel is checked on the channel, not on intentions.
    ///  2. **Never an exception.** The register is pointed at an impossible
    ///     path and it is demanded that the call come back all the same — and that
    ///     the reason for the silence has stayed written, because an empty
    ///     `catch` and a `catch` that has never been executed look too much
    ///     alike.
    ///
    /// ── AND THE POINT THAT GETS FORGOTTEN ────────────────────────────────
    ///
    /// The places where the bridge knows it was asked for something that is not
    /// there are four. The foreseeable defect is not getting one wrong: it is
    /// adding the fifth and not registering it. So we do not count to four
    /// — it is demanded that **every** point that asks `_catalogo.Simili(` also
    /// write in the register, so that the fifth is born already watched.
    /// </summary>
    internal static class RegistryTests
    {
        internal static void All()
        {
            Group("the registry of the questions without an answer (Z-G7)");

            TheLineShape();
            TheSilenceIsWanted();
            EveryPlaceThatKnowsWritesIt();
            DoesNotTouchStdout();
        }

        // ─────────────────────────────────────────────────────────────────
        //  1. THE SHAPE OF THE LINE
        // ─────────────────────────────────────────────────────────────────

        private static void TheLineShape()
        {
            var bridge = BridgeOrNotHere("the line of the registry");
            if (bridge == null) return;

            MethodInfo line;
            Type typ;
            try
            {
                var asm = Load.Bridge(bridge);
                typ = Load.Class(asm, "QuestionRegistry");
                if (typ == null)
                { Check("the QuestionRegistry class is there", false, ""); return; }
                line = typ.GetMethod("Line",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (line == null) { Check("Line() is there", false, ""); return; }
            }
            catch (Exception ex)
            { Check("the bridge loads", false, ex.ToString()); return; }

            var timestamp = new DateTime(2026, 9, 23, 21, 30, 0);
            Func<string, string, string[], string> doIt = (t, n, s) =>
                (string)line.Invoke(null, new object[] { timestamp, t, n, s });

            // ── a normal line ────────────────────────────────────────────
            var normal = doIt("operation", "ripiega_lamiera",
                              new[] { "piega_lamiera" });
            Check("the line carries when, type and name",
                  normal.Contains("\"timestamp\":\"2026-09-23T21:30:00\"") &&
                  normal.Contains("\"kind\":\"operation\"") &&
                  normal.Contains("\"name\":\"ripiega_lamiera\""),
                  normal);
            Check("and the near names, which are the useful half of the measurement",
                  normal.Contains("piega_lamiera") && normal.Contains("similar"),
                  "it tells «got the spelling wrong» from «asks for something that is not there»");
            Check("it sits on ONE line only, which is what JSONL means",
                  !normal.Contains("\n") && !normal.Contains("\r"),
                  normal.Length + " characters, no line break");

            // ── the name that a model can really invent ──────────────────
            //
            // It is not a fanciful case: the name is written by a model, and
            // one quotation mark is enough to make the file unreadable FROM THEN ON
            // -- not the line, the file. That is why it is serialized with the
            // same `Protocol.InJson` as the rest of the bridge instead of
            // pasting quotation marks by hand.
            var bad = doIt("operation", "taglia\"a\"meta" + "\n" + "riga2", null);
            Check("a name with quotes does not break the line",
                  !bad.Contains("\n") && !bad.Contains("\r"),
                  bad);
            Check("and it reads back as JSON, not as broken text",
                  ReadsBack(bad, "taglia\"a\"meta" + "\n" + "riga2"),
                  "the name comes back identical, line break included");

            // ── `similar` is there only when there is something ─────────────
            var without = doIt("tool", "guarda_la_tavola", null);
            Check("without near names the field «similar» does not appear",
                  !without.Contains("similar"),
                  "an empty field in every line is noise that somebody then counts");
            var empty = doIt("tool", "guarda_la_tavola", new string[0]);
            Check("and not even with an empty list",
                  !empty.Contains("similar"),
                  "zero and absent are the same thing, and are written the same");
        }

        /// <summary>
        /// Is the line read back as JSON and does the name come back identical?
        ///
        /// A parser written here is not used: the one that reads JSON in the
        /// bridge is asked, because the real question is *«can the bridge read it back?»* and not
        /// *«does it look like JSON?»*.
        /// </summary>
        private static bool ReadsBack(string line, string expectedName)
        {
            try
            {
                var bridge = Path.Combine(Root(), "bin", "oce-solid-agent-mcp.exe");
                var asm = Load.Bridge(bridge);
                var prot = Load.Class(asm, "Protocol");
                if (prot == null) return false;
                var fromJson = prot.GetMethod("FromJson",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (fromJson == null) return false;
                var d = fromJson.Invoke(null, new object[] { line })
                        as Dictionary<string, object>;
                object n;
                return d != null && d.TryGetValue("name", out n) &&
                       Convert.ToString(n) == expectedName;
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────────────────
        //  2. THE SILENCE IS INTENDED, AND IT SHOWS
        // ─────────────────────────────────────────────────────────────────

        private static void TheSilenceIsWanted()
        {
            var bridge = BridgeOrNotHere("the registry that cannot write");
            if (bridge == null) return;

            Type typ;
            try
            {
                var asm = Load.Bridge(bridge);
                typ = Load.Class(asm, "QuestionRegistry");
                if (typ == null) return;   // already said above
            }
            catch { return; }

            var constructor = typ.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(string) }, null);
            if (constructor == null)
            { Check("the registry is built with a path", false, ""); return; }

            Func<object, string, string, object> annotate = (reg, method, itemName) =>
            {
                var m = ReflectionLog.Global.Method(typ, method,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return m.Invoke(reg, method == "OperationThatDoesNotExist"
                    ? new object[] { itemName, null }
                    : new object[] { itemName });
            };
            Func<object, string, object> read = (reg, prop) =>
                ReflectionLog.Global.Property(typ, prop,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(reg);

            // ── IT REALLY WRITES ──────────────────────────────────────────
            //
            // Before proving that it is silent when it must be, it is proved that it speaks
            // when it can. A register that is always silent would pass every check
            // on silence.
            var file = Path.Combine(Path.GetTempPath(),
                                    "registro-prova-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                var good = constructor.Invoke(new object[] { file });
                annotate(good, "OperationThatDoesNotExist", "fresa_a_candela");
                annotate(good, "ToolThatDoesNotExist", "apri_la_tavola");
                var lines = File.Exists(file) ? File.ReadAllLines(file) : new string[0];
                Check("it writes one line per question",
                      lines.Length == 2, lines.Length + " righe");
                Check("and it writes them appending, not overwriting",
                      lines.Length == 2 &&
                      lines[0].Contains("fresa_a_candela") &&
                      lines[1].Contains("apri_la_tavola"),
                      "the first stays when the second arrives");
                Check("and says it wrote them",
                      Convert.ToInt32(read(good, "Written")) == 2 &&
                      read(good, "WhyItWasSilent") == null,
                      "Written=2, no reason for silence");
            }
            finally { try { File.Delete(file); } catch { } }

            // ── THE EMPTY NAME IS NOT A QUESTION ──────────────────────────
            var file2 = Path.Combine(Path.GetTempPath(),
                                     "registro-prova-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                var reg = constructor.Invoke(new object[] { file2 });
                annotate(reg, "OperationThatDoesNotExist", "   ");
                Check("an empty name does not end up in the registry",
                      !File.Exists(file2) &&
                      Convert.ToInt32(read(reg, "Written")) == 0,
                      "it is a malformed plan, not something somebody wanted");
            }
            finally { try { File.Delete(file2); } catch { } }

            // ── IT CANNOT WRITE: IT STAYS SILENT, AND DOES NOT RAISE ──────
            //
            // The impossible path is a REAL file used as a folder:
            // it really happens, and it does not depend on the permissions of the machine that
            // runs the check -- which on a runner are different from here.
            var obstacle = Path.GetTempFileName();
            try
            {
                var impossible = Path.Combine(obstacle, "dentro-un-file.jsonl");
                var reg = constructor.Invoke(new object[] { impossible });
                bool hasRaised = false;
                try { annotate(reg, "OperationThatDoesNotExist", "fresa_a_candela"); }
                catch { hasRaised = true; }
                Check("if it cannot write it does NOT raise",
                      !hasRaised,
                      "a registry that makes the call it observes fail is " +
                      "worse than no registry");
                Check("and the reason for the silence stays written",
                      !hasRaised && read(reg, "WhyItWasSilent") != null &&
                      Convert.ToInt32(read(reg, "Written")) == 0,
                      Convert.ToString(read(reg, "WhyItWasSilent")));
            }
            finally { try { File.Delete(obstacle); } catch { } }

            // ── NO FILE TO WRITE ON ───────────────────────────────────────
            var withoutFile = constructor.Invoke(new object[] { null });
            bool raised2 = false;
            try { annotate(withoutFile, "ToolThatDoesNotExist", "x_qualunque"); }
            catch { raised2 = true; }
            Check("and with no file to write to, the same",
                  !raised2 && read(withoutFile, "WhyItWasSilent") != null,
                  "an empty OCE_QUESTION_REGISTRY means «do not register», " +
                  "and must not become «register next to the executable»");
        }

        // ─────────────────────────────────────────────────────────────────
        //  3. EVERY POINT THAT KNOWS, WRITES
        // ─────────────────────────────────────────────────────────────────

        private static void EveryPlaceThatKnowsWritesIt()
        {
            var source = Path.Combine(Root(), "Tools", "McpTools.cs");
            if (!File.Exists(source))
            { Check("McpTools.cs can be read", false, source); return; }

            var lines = File.ReadAllLines(source);
            var discovered = new List<int>();
            int monitored = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf("_catalog.Similar(", StringComparison.Ordinal) < 0)
                    continue;
                // The write is right next to it: three lines are enough, and not
                // so many as to catch the one of the next point.
                var window = string.Join("\n",
                    lines.Skip(i).Take(4));
                if (window.IndexOf("_registry.OperationThatDoesNotExist",
                                     StringComparison.Ordinal) >= 0) monitored++;
                else discovered.Add(i + 1);
            }

            Check("every place that looks for a near name also writes to the registry",
                  discovered.Count == 0,
                  discovered.Count == 0
                      ? monitored + " places, all guarded"
                      : "uncovered at lines " + string.Join(", ", discovered));
            Check("and the places are more than one, otherwise the rule guards nothing",
                  monitored >= 3,
                  monitored + " places that ask for `_catalog.Similar(`");

            var everything = string.Join("\n", lines);
            Check("and the tool that does not exist ends up in the registry",
                  everything.IndexOf("_registry.ToolThatDoesNotExist",
                                StringComparison.Ordinal) >= 0,
                  "it is the place that knew and kept quiet: the `default` of Invoke");

            // ── THE RULE IS MADE TO FAIL ON PURPOSE ───────────────────────
            //
            // On fake lines, because making the real one fail would mean
            // removing a call from the bridge.
            var fakeBroken = new[]
            {
                "                var similar = _catalog.Similar(itemName);",
                "                trouble.Add(location + \": does not exist\");",
            };
            var fakeHealthy = new[]
            {
                "                var similar = _catalog.Similar(itemName);",
                "                _registry.OperationThatDoesNotExist(itemName, similar);",
                "                trouble.Add(location + \": does not exist\");",
            };
            Check("the rule catches a place that does NOT write",
                  Discovered(fakeBroken) == 1,
                  "a line that looks for near names and registers nothing");
            Check("and leaves alone the one that writes",
                  Discovered(fakeHealthy) == 0,
                  "tried both ways, on fake lines");
        }

        private static int Discovered(string[] lines)
        {
            int howMany = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf("_catalog.Similar(", StringComparison.Ordinal) < 0)
                    continue;
                var window = string.Join("\n", lines.Skip(i).Take(4));
                if (window.IndexOf("_registry.OperationThatDoesNotExist",
                                     StringComparison.Ordinal) < 0) howMany++;
            }
            return howMany;
        }

        // ─────────────────────────────────────────────────────────────────
        //  4. MAI SU STDOUT
        // ─────────────────────────────────────────────────────────────────

        private static void DoesNotTouchStdout()
        {
            var source = Path.Combine(Root(), "Tools", "QuestionRegistry.cs");
            if (!File.Exists(source))
            { Check("QuestionRegistry.cs can be read", false, source); return; }

            // The CODE is looked at, not the comments: the explanation of the rule
            // names `Console` on purpose, and a check that punished its own
            // documentation would get its documentation deleted.
            var code = WithoutComments(File.ReadAllText(source));
            var foundItems = Regex.Matches(code, @"\bConsole\s*\.").Count;

            Check("the registry does not name Console anywhere",
                  foundItems == 0,
                  foundItems == 0
                      ? "the JSON-RPC passes there: one line too many is the protocol broken"
                      : foundItems + " occorrenze");
            Check("and the rule looks at the code, not at the comments that explain it",
                  File.ReadAllText(source).IndexOf("Console", StringComparison.Ordinal) >= 0,
                  "the header names Console to say not to use it: if this " +
                  "line turned red it would mean the explanation is gone");
        }

        /// <summary>
        /// Away with the line and block comments. It is not a C# parser and must not
        /// be: here it serves only not to read the prose that explains the
        /// rule as if it were a violation of the rule.
        /// </summary>
        private static string WithoutComments(string text)
        {
            text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
            text = Regex.Replace(text, @"^\s*///.*$", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"^\s*//.*$", "", RegexOptions.Multiline);
            return text;
        }
    }
}
