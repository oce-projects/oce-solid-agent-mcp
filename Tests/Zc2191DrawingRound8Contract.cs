using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 8 OF REPORT 8CD843 (27/10/2026), WHERE THE BENCH
    /// CANNOT REACH.
    ///
    /// N29: a note of four lines came back with «\r\n» for «\n»: «text_matches»
    /// false, and the compact answer called it no warning. N30: the overall
    /// size of the clip assembly ended on couplers with no mate, and nothing
    /// said so. N31: «count» on a view of an assembly was set aside under a key
    /// the compact answer drops. N32: «delete_annotation» added the notes of the
    /// sheet and of the views, which «note» keeps apart.
    ///
    /// Two halves, as Zc2190. The decisions are small static methods, called
    /// here by reflection with the cases a bench cannot build (an assembly with
    /// a loose component, a text read back that differs). The wiring is read on
    /// the sources, and every rule first turns red on a copy where its line is
    /// gone or where the old line is back.
    ///
    /// On library 0.172.1 every check is red: the methods and the lines are
    /// missing.
    /// </summary>
    internal static class Zc2191DrawingRound8Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 8 of report 8cd843: note text, loose components in the overall size, count on an assembly, note counts (N29-N32)");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 8 fixes",
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
                sw = Assembly.Load(File.ReadAllBytes(library)).GetType("Oce.Solid.Agent.Core.SwOperations", false);
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (sw == null) { Check("SwOperations is in the library", false, "type missing"); return; }

            NoteText(sw);
            NoteCounts(sw);
            OverallComponentsRule(sw);
            LooseAndCount(sw);

            var bridge = BridgeOrNotHere("the rule of the compact answer on the new warning keys");
            if (bridge == null) return;
            Type tools;
            try { tools = Load.Class(Load.Bridge(bridge), "McpTools"); }
            catch (Exception ex) { Check("the bridge loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (tools == null) { Check("McpTools is in the bridge", false, "type missing"); return; }
            KeysThatStay(tools);
            UnknownArguments(tools);
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

        private static void NoteText(Type sw)
        {
            const string label = "N29: a note read back with «\\r\\n» for «\\n» matches, any other difference does not, and a difference is a warning with both texts";
            try
            {
                var wrong = new List<string>();
                Func<string, string, bool> same = (a, r) => (bool)Call(sw, "SameNoteText", a, r);
                if (!same("NOTE GENERALI\n1) MATERIALE", "NOTE GENERALI\r\n1) MATERIALE")) wrong.Add("«\\n» against «\\r\\n» called different");
                if (!same("A\nB", "A\rB")) wrong.Add("«\\n» against «\\r» called different");
                if (!same(" A ", "A")) wrong.Add("the outer blanks counted");
                if (same("A\nB", "A B")) wrong.Add("a line end against a blank called the same");
                if (same("S355", "S275")) wrong.Add("S355 against S275 called the same");
                if (same("A", null)) wrong.Add("a text not read back called the same");
                Func<string, string, string> warn = (a, r) => (string)Call(sw, "NoteTextWarning", a, r);
                if (warn("A\nB", "A\r\nB") != null) wrong.Add("a warning for the line ends alone");
                var differs = warn("MATERIAL S355", "MATERIAL S275");
                if (differs == null || !differs.Contains("«MATERIAL S275»") || !differs.Contains("«MATERIAL S355»") || !differs.Contains("delete_annotation"))
                    wrong.Add("the warning of a different text: " + (differs ?? "null"));
                var unread = warn("A", null);
                if (unread == null || !unread.Contains("could not read")) wrong.Add("the warning of a text not read back: " + (unread ?? "null"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "3 pairs the same, 3 different, 2 warnings with their texts" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void NoteCounts(Type sw)
        {
            const string label = "N32: the notes that went are the difference of the two counts, and none when a count is unread";
            try
            {
                var wrong = new List<string>();
                Func<int, int, int, int, int?> down = (sheetBefore, sheetAfter, viewsBefore, viewsAfter) =>
                    (int?)Call(sw, "NotesDownBy", sheetBefore, sheetAfter, viewsBefore, viewsAfter);
                if (down(67, 67, 8, 7) != 1) wrong.Add("a note of a view: " + down(67, 67, 8, 7));
                if (down(68, 67, 7, 7) != 1) wrong.Add("a note of the sheet: " + down(68, 67, 7, 7));
                if (down(68, 66, 7, 7) != 2) wrong.Add("two notes: " + down(68, 66, 7, 7));
                if (down(-1, 67, 7, 7) != null) wrong.Add("an unread count gave a number");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "3 differences, 1 unread" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void OverallComponentsRule(Type sw)
        {
            const string label = "N30: «components» read as a list or one name of the view, and refused when empty, not text, or not shown, with the names the view shows";
            try
            {
                var wrong = new List<string>();
                var shown = new List<string> { "26P000007-1", "26P000008-1", "26P000010-1" };
                Func<object, object[]> read = raw =>
                {
                    var args = new object[] { raw, shown, null };
                    var set = Call(sw, "OverallComponents", args) as HashSet<string>;
                    return new object[] { set, args[2] as string };
                };
                var none = read(null);
                if (none[0] != null || none[1] != null) wrong.Add("left out: " + none[1]);
                var two = read(new object[] { "26P000007-1", " 26P000008-1 " });
                var twoSet = two[0] as HashSet<string>;
                if (twoSet == null || twoSet.Count != 2 || !twoSet.Contains("26P000008-1")) wrong.Add("two names: " + two[1]);
                var one = read("26P000007-1");
                if (!(one[0] is HashSet<string>) || ((HashSet<string>)one[0]).Count != 1) wrong.Add("one name alone: " + one[1]);
                var bad = new[]
                {
                    Tuple.Create((object)new object[0], "is empty"),
                    Tuple.Create((object)new object[] { 5 }, "list of component names"),
                    Tuple.Create((object)new object[] { " " }, "empty name"),
                    Tuple.Create((object)new Dictionary<string, object> { { "a", 1 } }, "list of component names"),
                    Tuple.Create((object)new object[] { "26P000099-1" }, "«26P000010-1»"),
                };
                foreach (var b in bad)
                {
                    var r = read(b.Item1);
                    var problem = r[1] as string;
                    if (r[0] != null || problem == null || !problem.Contains(b.Item2)) wrong.Add("«" + b.Item2 + "»: " + (problem ?? "taken"));
                }
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "3 forms taken, " + bad.Length + " refused" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void LooseAndCount(Type sw)
        {
            const string label = "N30/N31: an overall on loose components names them with the remedy, and «count» on an assembly says it was not used";
            try
            {
                var wrong = new List<string>();
                var one = (string)Call(sw, "OverallFreeWarning", "overall height", 31.55, new List<string> { "26P000010-1" });
                if (one == null || !one.Contains("31.55") || !one.Contains("«26P000010-1»") || !one.Contains("no mate") ||
                    !one.Contains("«components»") || !one.Contains("delete_annotation") || !one.Contains("where it rests"))
                    wrong.Add("one loose: " + (one ?? "null"));
                var two = (string)Call(sw, "OverallFreeWarning", "overall length", 59.646, new List<string> { "26P000013-1", "26P000009-1" });
                if (two == null || !two.Contains("«26P000013-1», «26P000009-1»") || !two.Contains("where they rest"))
                    wrong.Add("two loose: " + (two ?? "null"));
                var count = (string)Call(sw, "CountNotUsedWarning", "both");
                if (count == null || !count.Contains("«count» was not used") || !count.Contains("«both»") || !count.Contains("«edges»"))
                    wrong.Add("count: " + (count ?? "null"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "singular and plural, and the count" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void KeysThatStay(Type tools)
        {
            const string label = "N29/N30/N31: the compact answer keeps the three new warning keys, and drops «count_ignored» as it did";
            try
            {
                var wrong = new List<string>();
                Func<string, bool> warns = k => (bool)Call(tools, "Warns", k);
                foreach (var key in new[] { "warning_text_differs", "warning_free_components", "warning_count_not_used" })
                    if (!warns(key)) wrong.Add("«" + key + "» left out");
                if (warns("count_ignored")) wrong.Add("«count_ignored» kept: the cause of N31 is not this rule");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "3 kept, the old key dropped" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void UnknownArguments(Type tools)
        {
            const string label = "the replay of round 8: an argument the operation does not declare is named, with the near name, and «_signatures» is the bridge's own";
            try
            {
                var wrong = new List<string>();
                var property = new Dictionary<string, object> { { "view", null }, { "count", null }, { "components", null } };
                var data = new Dictionary<string, object>
                {
                    { "view", "V1" }, { "component", new object[] { "26P000007-1" } }, { "_signatures", new object() },
                };
                var lines = Call(tools, "UnknownArgumentsOfStep", "step 1", "drawing_dimension", data, property) as List<string>;
                if (lines == null || lines.Count != 1) wrong.Add("lines: " + (lines == null ? "null" : lines.Count.ToString()));
                else if (!lines[0].Contains("«component»") || !lines[0].Contains("IGNORED") || !lines[0].Contains("«components»") ||
                         !lines[0].StartsWith("step 1 («drawing_dimension»)", StringComparison.Ordinal))
                    wrong.Add("the line: " + lines[0]);
                var none = Call(tools, "UnknownArgumentsOfStep", "step 2", "drawing_dimension",
                                new Dictionary<string, object> { { "count", 3 } }, property) as List<string>;
                if (none == null || none.Count != 0) wrong.Add("a declared argument named");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "1 named with its near name, «_signatures» and the declared ones left" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING, ON THE SOURCES
        // =================================================================

        private const string OnlyRead = "HashSet<string> only = OverallComponents(";
        private const string OnlyEdges = "if (only != null && (s.Component == null || !only.Contains(s.Component))) continue;";
        private const string OnlyCircles = "if (only != null && (c.Component == null || !only.Contains(c.Component))) continue;";
        private const string LooseLine = "string loose = FreeComponentOf(end.Entity, topLevel);";
        private const string LooseWarning = "measurements[\"warning_free_components\"] = string.Join(\" \", freeWarnings);";
        private const string CountWarning = "measurements[\"warning_count_not_used\"] = CountNotUsedWarning(mode);";
        private const string OldCountKey = "measurements[\"count_ignored\"]";
        private const string TextMatches = "[\"text_matches\"] = SameNoteText(text, reread),";
        private const string TextWarning = "if (differs != null) outside[\"warning_text_differs\"] = differs;";
        private const string DownLine = "int? down = NotesDownBy(sheetBefore, sheetAfter, viewsBefore, viewsAfter);";
        private const string ByTextLine = "bool byText = text != null && SameNoteText(text, noteText);";
        private const string WhereLine = "measurements[\"attached_to\"] = target.OnSheet ? \"sheet\" : \"view\";";
        private const string OldSum = "NotesOfTheSheet(";
        private const string RealRunCheck = "var unknownArguments = UnknownArgumentsOfPlan(steps);";
        private const string BeforeAsking = "string notAsked = null;";
        private const string DryRunCheck = "trouble.AddRange(UnknownArgumentsOfStep(location, itemName, data, property));";
        private const string CatalogLine ="\"description\", \"ON A VIEW OF AN ASSEMBLY: the names of the components the overall size \"";

        private static readonly string[][] Files =
        {
            new[] { "assembly", "Oce.Solid.Agent.Core", "DrawingAssemblyDimensions.cs" },
            new[] { "note", "Oce.Solid.Agent.Core", "DrawingNoteOperations.cs" },
            new[] { "delete", "Oce.Solid.Agent.Core", "DrawingDeleteAnnotation.cs" },
            new[] { "catalog", "Oce.Solid.Agent.Core", Path.Combine("Catalog", "Operations.cs") },
            new[] { "tools", "Tools", "McpTools.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "assembly", OnlyRead, "«components» is not read (N30)" },
            new[] { "assembly", OnlyEdges, "the overall size measures the edges of the components left out (N30)" },
            new[] { "assembly", OnlyCircles, "the overall size measures the circles of the components left out (N30)" },
            new[] { "assembly", LooseLine, "an overall dimension is not checked for loose components (N30)" },
            new[] { "assembly", LooseWarning, "the loose components of an overall dimension are not warned of (N30)" },
            new[] { "assembly", CountWarning, "«count» on an assembly is not said under a warning key (N31)" },
            new[] { "note", TextMatches, "the text of a note is compared with its line ends (N29)" },
            new[] { "note", TextWarning, "a note whose text differs is not warned of (N29)" },
            new[] { "delete", DownLine, "delete_annotation does not say how many notes went (N32)" },
            new[] { "delete", WhereLine, "delete_annotation does not say where the note was (N32)" },
            new[] { "delete", ByTextLine, "delete_annotation does not find a note of several lines by its text (N29)" },
            new[] { "catalog", CatalogLine, "the catalog does not declare «components» (N30)" },
            new[] { "tools", RealRunCheck, "a real build runs a plan with an argument its operation does not declare" },
            new[] { "tools", DryRunCheck, "the dry run does not name the unknown arguments with the same rule" },
        };

        /// <summary>The lines that must not come back, with what they bring back.</summary>
        private static readonly string[][] Gone =
        {
            new[] { "assembly", OldCountKey, "«count_ignored» is back, the key the compact answer drops (N31)" },
            new[] { "delete", OldSum, "delete_annotation adds the notes of the sheet and of the views again (N32)" },
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
            string tools;
            if (sources.TryGetValue("tools", out tools) && tools != null)
            {
                int check = tools.IndexOf(RealRunCheck, StringComparison.Ordinal);
                int asking = tools.IndexOf(BeforeAsking, StringComparison.Ordinal);
                if (check < 0 || asking < 0 || check > asking)
                    wrong.Add("the unknown arguments of a real build are not refused before the confirmation is asked");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: «components» on the edges and circles of the overall size, the loose components warned of, «count» under a warning key, the note text without line ends and its warning, the note counts of delete_annotation apart, «components» in the catalog";
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Files)
            {
                var path = Path.Combine(Root(), f[1], f[2]);
                sources[f[0]] = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            var real = WiringProblems(sources);

            // Teeth: each line taken away, each old line put back.
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
                mutate("without «" + anchor + "»", line[0], t => t.Replace(anchor, "/* gone */"));
            }
            foreach (var line in Gone)
            {
                var back = line[1];
                mutate("with «" + back + "» back", line[0], t => t + "\n// " + back);
            }
            mutate("the unknown arguments checked after the confirmation", "tools",
                   t => t.Replace(RealRunCheck, "").Replace(BeforeAsking, BeforeAsking + "\n" + RealRunCheck));

            bool ok = real.Count == 0 && blind.Count == 0;
            string detail = real.Count > 0 ? string.Join("; ", real)
                          : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                          : Lines.Length + " lines, " + Gone.Length + " old lines kept out; " + copies + " changed copies all red";
            Check(label, ok, detail);
        }
    }
}
