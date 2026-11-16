using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 19 OF REPORT 8CD843 (28/10/2026), WHERE THE BENCH CANNOT
    /// REACH.
    ///
    /// Observation: «export_pdf» with «search» said «not_found» for a value of the
    /// title block that the PDF printed, wrapped over two lines in its box. Measured
    /// on that PDF: PdfText gives each printed line as a line of its own, the first
    /// ending in a space.
    ///
    /// N61: after «set_property» on the model in view, «title_block» read the old value
    /// of a box until the drawing was opened again, while the PDF printed the new one.
    /// The bench cannot reach it: no operation of a plan brings the part forward
    /// once the drawing is made.
    ///
    /// Two halves, as Zc2200: the decision by reflection, and the wiring on the
    /// sources, every rule first red on a changed copy. On library 0.174.8 every
    /// check is red.
    /// </summary>
    internal static class Zc2201DrawingRound19Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 19 of report 8cd843: a text of the PDF wrapped over two lines is found and said on several lines; a box of the title block that shows a value the model no longer has is said (N61)");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 19 fix",
                       library + " is missing: build the library first (build.ps1)");
                return;
            }
            Type sw;
            try
            {
                var redist = WhereTheInteropIs();
                if (redist != null)
                    AppDomain.CurrentDomain.AssemblyResolve += (s, a) =>
                    {
                        var name = new AssemblyName(a.Name).Name;
                        if (!name.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase)) return null;
                        var file = Path.Combine(redist, name + ".dll");
                        return File.Exists(file) ? Assembly.LoadFrom(file) : null;
                    };
                // From the bytes: the repository may sit on a network drive,
                // where LoadFrom is refused.
                var core = Assembly.Load(File.ReadAllBytes(library));
                sw = core.GetType("Oce.Solid.Agent.Core.SwOperations", false);
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (sw == null) { Check("SwOperations is in the library", false, "type missing"); return; }

            WrappedSearch(sw);
            OldValue(sw);
        }

        private static object Call(Type t, string method, params object[] args)
        {
            var m = ReflectionLog.Global.Method(t, method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method);
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        private static string Why(Exception ex)
        {
            var inner = ex.InnerException ?? ex;
            return inner.GetType().Name + ": " + inner.Message;
        }

        // =================================================================
        // THE PURE RULES
        // =================================================================

        private static void WrappedSearch(Type sw)
        {
            const string label = "observation of round 19: a text on one line is found, across a line break it is on several lines (also a word cut in two), with any other difference it is not found; the note says what to look at, and is not said with nothing wrapped";
            try
            {
                var wrong = new List<string>();
                // As PdfText gives the box measured in round 19: the label, the value in
                // two lines, the first ending in a space, then the next label.
                const string box = "State:\nCOPY FOR A TEST \nROUND 19\nRevision description:\n26M0000\n02\n";
                var cases = new[]
                {
                    new[] { "State:", "found" },
                    new[] { "COPY FOR A TEST", "found" },
                    new[] { "COPY FOR A TEST ROUND 19", "on_several_lines" },
                    new[] { "26M000002", "on_several_lines" },
                    new[] { "COPY FOR A TEST ROUND 20", "not_found" },
                    new[] { "COPY  FOR A TEST", "not_found" },
                    new[] { "TEST-ROUND 19", "not_found" },
                    new[] { "ABSENT987", "not_found" },
                };
                foreach (var c in cases)
                {
                    var got = Call(sw, "PdfSearchVerdict", box, c[0]) as string;
                    if (got != c[1]) wrong.Add("«" + c[0] + "» gave " + (got ?? "null") + ", not " + c[1]);
                }
                if (Call(sw, "PdfSearchVerdict", "", "TEST") as string != "not_found") wrong.Add("an empty text found something");
                if (Call(sw, "PdfSearchVerdict", box, "") as string != "not_found") wrong.Add("an empty search was found");
                if (Call(sw, "PdfSearchVerdict", null, "TEST") as string != "not_found") wrong.Add("a text that could not be read found something");

                var one = Call(sw, "OnSeveralLinesNote", new List<string> { "COPY FOR A TEST ROUND 19" }) as string;
                foreach (var part in new[] { "«COPY FOR A TEST ROUND 19» is", "only across a line break", "look at the box" })
                    if (one == null || !one.Contains(part)) wrong.Add("the note lacks «" + part + "»: " + (one ?? "null"));
                var two = Call(sw, "OnSeveralLinesNote", new List<string> { "A B", "C D" }) as string;
                if (two == null || !two.Contains("«A B», «C D» are")) wrong.Add("two texts: " + (two ?? "null"));
                if (Call(sw, "OnSeveralLinesNote", new List<string>()) != null) wrong.Add("a note with nothing wrapped");
                if (Call(sw, "OnSeveralLinesNote", new object[] { null }) != null) wrong.Add("a note with no list");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? cases.Length + " searches in a box of two lines, three empty inputs, the note with 1, 2 and 0 texts" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void OldValue(Type sw)
        {
            const string label = "N61: a box that is one $PRPSHEET link (format codes aside) and shows a value none of the drawers of the model has is an old value; with fixed text, another kind of link, two links, nothing shown or nothing on the model it is not judged; the note counts the fields and says to open the drawing again";
            try
            {
                var wrong = new List<string>();
                const string state = "$PRPSHEET:\"State\"";
                var cases = new object[][]
                {
                    new object[] { state, "COPY FOR A TEST", new List<string> { "COPY FOR A TEST ROUND 19" }, true },
                    new object[] { "  " + state + " ", "COPY FOR A TEST", new List<string> { "COPY FOR A TEST ROUND 19" }, true },
                    new object[] { state, "copy for a test", new List<string> { "COPY FOR A TEST" }, true },
                    new object[] { state, "COPY FOR A TEST", new List<string> { "COPY FOR A TEST" }, false },
                    new object[] { state, "COPY FOR A TEST", new List<string> { "COPY FOR A TEST " }, false },
                    new object[] { state, "COPY FOR A TEST", new List<string> { "ANOTHER", "COPY FOR A TEST" }, false },
                    new object[] { state, "COPY FOR A TEST", new List<string>(), false },
                    new object[] { state, "COPY FOR A TEST", null, false },
                    new object[] { state, "", new List<string> { "COPY FOR A TEST" }, false },
                    new object[] { "$PRP:\"State\"", "COPY FOR A TEST", new List<string> { "ROUND 19" }, false },
                    new object[] { "$PRPSHEET:\"Weight\" Kg", "12 Kg", new List<string> { "13" }, false },
                    new object[] { state + " " + state, "A A", new List<string> { "B" }, false },
                    // The box of the workshop, measured in scenario t61: a format code before the link.
                    new object[] { "<FONT size=8PTS>" + state, "OLD VALUE", new List<string> { "NEW VALUE ROUND 19" }, true },
                    new object[] { "<FONT size=8PTS>" + state, "OLD VALUE", new List<string> { "OLD VALUE" }, false },
                    new object[] { "<FONT size=8PTS>$PRPSHEET:\"Weight\" Kg", "12 Kg", new List<string> { "13" }, false },
                };
                foreach (var c in cases)
                {
                    var got = Call(sw, "ShowsAnOldValue", c[0], c[1], c[2]) as bool?;
                    if (got != (bool)c[3])
                        wrong.Add("«" + c[0] + "» showing «" + c[1] + "» against " +
                                  (c[2] == null ? "null" : "[" + string.Join(", ", (List<string>)c[2]) + "]") +
                                  " gave " + (got == null ? "null" : got.ToString()) + ", not " + c[3]);
                }
                var one = Call(sw, "OldValueNote", 1) as string;
                foreach (var part in new[] { "1 field of the title block shows", "«requested_shown_with_an_old_value»", "«model_has»", "old value or the new one", "Close the drawing and open it again" })
                    if (one == null || !one.Contains(part)) wrong.Add("the note lacks «" + part + "»: " + (one ?? "null"));
                var two = Call(sw, "OldValueNote", 2) as string;
                if (two == null || !two.StartsWith("2 fields of the title block show ")) wrong.Add("two fields: " + (two ?? "null"));
                if (Call(sw, "OldValueNote", 0) != null) wrong.Add("a note with no old value");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? cases.Length + " boxes judged, the note with 1, 2 and 0 fields" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING
        // =================================================================

        private const string TextRead = "try { text = PdfText.Read(location); } catch { }";
        private const string VerdictAsked = "string where = PdfSearchVerdict(text, c);";
        private const string WrappedKept = "else if (where == \"on_several_lines\") wrappedItems.Add(c);";
        private const string WrappedSaid = "measurements[\"found_on_several_lines\"] = wrappedItems.ToArray();";
        private const string NoteSaid = "if (wrappedNote != null) measurements[\"on_several_lines_note\"] = wrappedNote;";
        private const string OldSearch = "(text.IndexOf(c, StringComparison.Ordinal) >= 0 ? foundItems : missingItems).Add(c);";
        private const string CatalogSays = "is in «found_on_several_lines», not in ";
        private const string SingleKept = "singleShows.Add(show);";
        private const string ModelAsked = "List<string> current = CurrentValues(viewModel, viewConfiguration, singleNames[i]);";
        private const string OldJudged = "if (ShowsAnOldValue(singleAsks[i], singleShows[i], current))";
        private const string OldSaid = "measurements[\"requested_shown_with_an_old_value\"] = oldValues.ToArray();";
        private const string NoteMade = "string oldValueNote = OldValueNote(oldValues.Count);";
        private const string VerdictTail = "measurements[\"verdict\"] = measurements[\"verdict\"] + \" AND \" + oldValueNote;";
        private const string CatalogSaysOld = "the ones that differ are in «requested_shown_with_an_old_value» with ";

        private static readonly string[][] Files =
        {
            new[] { "drawing", "Oce.Solid.Agent.Core", "DrawingOperations.cs" },
            new[] { "catalog", Path.Combine("Oce.Solid.Agent.Core", "Catalog"), "Operations.cs" },
            new[] { "title", "Oce.Solid.Agent.Core", "TitleBlockOperations.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "drawing", VerdictAsked, "the search does not know a text wrapped over two lines (round 19)" },
            new[] { "drawing", WrappedKept, "a wrapped text goes to «not_found» again (round 19)" },
            new[] { "drawing", WrappedSaid, "the answer does not say which texts are on several lines (round 19)" },
            new[] { "drawing", NoteSaid, "the answer does not say to look at the box of a wrapped text (round 19)" },
            new[] { "catalog", CatalogSays, "the description of export_pdf does not say «found_on_several_lines» (round 19)" },
            new[] { "title", SingleKept, "the boxes that are one link are not kept for the comparison (N61)" },
            new[] { "title", ModelAsked, "the values the model has now are not read (N61)" },
            new[] { "title", OldJudged, "a box is not compared with the model (N61)" },
            new[] { "title", OldSaid, "the answer does not say the boxes with an old value (N61)" },
            new[] { "title", VerdictTail, "the verdict says «no empty box» of boxes with an old value (N61)" },
            new[] { "catalog", CatalogSaysOld, "the description of title_block does not say «requested_shown_with_an_old_value» (N61)" },
        };

        /// <summary>The lines that must not come back, with what they bring back.</summary>
        private static readonly string[][] Gone =
        {
            new[] { "drawing", OldSearch, "the plain search, that says «not_found» for a printed text wrapped in its box (round 19)" },
        };

        private static List<string> WiringProblems(Dictionary<string, string> sources)
        {
            var wrong = new List<string>();
            foreach (var line in Lines)
            {
                string text;
                if (!sources.TryGetValue(line[0], out text) || text == null) { wrong.Add(line[0] + " not found"); continue; }
                if (!text.Contains(line[1])) wrong.Add(line[2]);
            }
            foreach (var line in Gone)
            {
                string text;
                if (sources.TryGetValue(line[0], out text) && text != null && text.Contains(line[1])) wrong.Add(line[2]);
            }
            string drawing;
            if (sources.TryGetValue("drawing", out drawing) && drawing != null)
            {
                int read = drawing.IndexOf(TextRead, StringComparison.Ordinal);
                int asked = drawing.IndexOf(VerdictAsked, StringComparison.Ordinal);
                if (read < 0 || asked < read)
                    wrong.Add("the texts are searched before the PDF is read (round 19)");
            }
            string title;
            if (sources.TryGetValue("title", out title) && title != null)
            {
                int kept = title.IndexOf(SingleKept, StringComparison.Ordinal);
                int judged = title.IndexOf(OldJudged, StringComparison.Ordinal);
                if (kept < 0 || judged < kept)
                    wrong.Add("the boxes are compared before they are collected (N61)");
                int made = title.IndexOf(NoteMade, StringComparison.Ordinal);
                int tail = title.IndexOf(VerdictTail, StringComparison.Ordinal);
                if (made < 0 || tail < made)
                    wrong.Add("the verdict is told before the old values are known (N61)");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: every searched text goes through PdfSearchVerdict after the PDF is read, the wrapped ones are said with their note and not as not found; every box that is one link is compared with the model after it is collected, the old values said in the answer and in the verdict; the catalog says both";
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Files)
            {
                var path = Path.Combine(Root(), f[1], f[2]);
                sources[f[0]] = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            var real = WiringProblems(sources);

            // Teeth: each line taken away, the old line put back, the order swapped.
            var blind = new List<string>();
            int copies = 0;
            Action<string, string, Func<string, string>> mutate = (what, key, change) =>
            {
                string text;
                if (!sources.TryGetValue(key, out text) || text == null) return;
                var changed = change(text);
                if (changed == text) { blind.Add(what + " (the copy did not change)"); return; }
                var copy = new Dictionary<string, string>(sources, StringComparer.Ordinal) { [key] = changed };
                copies++;
                if (WiringProblems(copy).Count == 0) blind.Add(what);
            };
            foreach (var line in Lines)
            {
                var anchor = line[1];
                mutate("without «" + anchor + "» in " + line[0], line[0], t => t.Replace(anchor, "/* gone */"));
            }
            foreach (var line in Gone)
            {
                var back = line[1];
                mutate("with «" + back + "» back", line[0], t => t + "\n// " + back);
            }
            mutate("the texts searched before the PDF is read", "drawing",
                   t => t.Replace(VerdictAsked, "/* moved */").Replace(TextRead, VerdictAsked + "\n" + TextRead));
            mutate("the boxes compared before they are collected", "title",
                   t => t.Replace(OldJudged, "/* moved */").Replace(SingleKept, OldJudged + "\n" + SingleKept));
            mutate("the verdict told before the old values are known", "title",
                   t => t.Replace(NoteMade, "/* moved */").Replace(VerdictTail, VerdictTail + "\n" + NoteMade));

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : Lines.Length + " lines, " + Gone.Length + " old line kept out, three orders; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
