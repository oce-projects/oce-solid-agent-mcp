using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 9 OF REPORT 8CD843 (27/10/2026), WHERE THE BENCH
    /// CANNOT REACH.
    ///
    /// N33: «components» refused a component of the assembly because one view
    /// did not show it, after 7 to 10 s of reading edges. N34: the drawing saved
    /// by «save_document» read «unsaved» at once, because the plan rebuilt it
    /// after the save. N35: «drawing_dimension» answered 16.258 where the sheet
    /// printed «16,3». N36: «none is a warning» read as no warning beside two.
    ///
    /// Two halves, as Zc2191. The decisions are small static methods, called
    /// here by reflection with the cases a bench cannot build (a component
    /// hidden in one view, an assembly of 25 components, a title). The wiring
    /// is read on the sources, and every rule first turns red on a copy where
    /// its line is gone, where the old line is back or where the order is
    /// swapped.
    ///
    /// On library 0.173.0 every check is red: the methods and the lines are
    /// missing.
    /// </summary>
    internal static class Zc2192DrawingRound9Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 9 of report 8cd843: components hidden in a view, the save flag after a save, the text a dimension prints, the note of the compact answer (N33-N36)");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 9 fixes",
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

            ComponentsInView(sw);
            ComponentsInAssembly(sw);
            RebuildRule(sw);
            TextShownRule(sw);

            var bridge = BridgeOrNotHere("the note of the compact answer");
            if (bridge == null) return;
            Type tools;
            try { tools = Load.Class(Load.Bridge(bridge), "McpTools"); }
            catch (Exception ex) { Check("the bridge loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (tools == null) { Check("McpTools is in the bridge", false, "type missing"); return; }
            NoteOfOutcomes(tools);
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

        private static void ComponentsInView(Type sw)
        {
            const string label = "N33: of the components asked, the ones the view shows are measured, the others are said, and the step is refused only when the view shows none";
            try
            {
                var wrong = new List<string>();
                var view = new List<string> { "26P000008-1", "26P000010-1" };
                Func<HashSet<string>, object[]> keep = asked =>
                {
                    var args = new object[] { asked, view, null, null };
                    var kept = Call(sw, "OverallComponentsInView", args) as HashSet<string>;
                    return new object[] { kept, args[2] as List<string>, args[3] as string };
                };
                var all = keep(null);
                if (all[0] != null || all[2] != null || (all[1] as List<string>)?.Count != 0) wrong.Add("left out: " + all[2]);
                var hidden = keep(new HashSet<string> { "26P000007-1", "26P000008-1" });
                var kept1 = hidden[0] as HashSet<string>;
                var out1 = hidden[1] as List<string>;
                if (kept1 == null || kept1.Count != 1 || !kept1.Contains("26P000008-1")) wrong.Add("the one shown: " + hidden[2]);
                if (out1 == null || out1.Count != 1 || out1[0] != "26P000007-1") wrong.Add("the one hidden is not said");
                if (hidden[2] != null) wrong.Add("a hidden component refused: " + hidden[2]);
                var none = keep(new HashSet<string> { "26P000007-1" });
                var problem = none[2] as string;
                if (none[0] != null || problem == null ||
                    !problem.Contains("this view shows none of «26P000007-1»: it shows «26P000008-1», «26P000010-1»."))
                    wrong.Add("none shown: " + (problem ?? "taken"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "all, one hidden said and not refused, none shown refused" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void ComponentsInAssembly(Type sw)
        {
            const string label = "N33: a name is checked on the assembly; unread components check the shape only, and a long assembly is listed up to 20 names";
            try
            {
                var wrong = new List<string>();
                var args = new object[] { new object[] { "26P000099-1" }, null, null };
                var taken = Call(sw, "OverallComponents", args) as HashSet<string>;
                if (taken == null || args[2] != null) wrong.Add("unread components refused a name: " + args[2]);
                var big = new List<string>();
                for (int i = 1; i <= 25; i++) big.Add("P" + i.ToString("00") + "-1");
                args = new object[] { new object[] { "26P000099-1" }, big, null };
                var refused = Call(sw, "OverallComponents", args) as HashSet<string>;
                var problem = args[2] as string;
                if (refused != null || problem == null || !problem.Contains("«P20-1» and 5 more.") || problem.Contains("«P21-1»"))
                    wrong.Add("25 components: " + (problem ?? "taken"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "unread taken, 25 listed as 20 and 5 more" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void RebuildRule(Type sw)
        {
            const string label = "N34: a drawing with nothing unsaved is not rebuilt after a step, every other document is, and a flag raised after a save is a warning with the title";
            try
            {
                var wrong = new List<string>();
                Func<int, bool, bool> rebuilds = (type, unsaved) => (bool)Call(sw, "RebuildsAfterStep", type, unsaved);
                const int part = 1, assembly = 2, drawing = 3;
                if (rebuilds(drawing, false)) wrong.Add("a clean drawing rebuilt");
                if (!rebuilds(drawing, true)) wrong.Add("a changed drawing not rebuilt");
                if (!rebuilds(part, false) || !rebuilds(part, true)) wrong.Add("a part not rebuilt");
                if (!rebuilds(assembly, false) || !rebuilds(assembly, true)) wrong.Add("an assembly not rebuilt");
                var warn = (string)Call(sw, "UnsavedAfterSaveWarning", "CLIP-26M000002 - Sheet1");
                if (warn == null || !warn.Contains("«CLIP-26M000002 - Sheet1»") || !warn.Contains("«document close»") ||
                    !warn.Contains("without saving"))
                    wrong.Add("the warning: " + (warn ?? "null"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "6 documents, 1 not rebuilt, and the warning" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void TextShownRule(Type sw)
        {
            const string label = "N35: the text a dimension prints is the numbers of its texts, as written, and nothing when there is none";
            try
            {
                var wrong = new List<string>();
                Func<List<string>, string> shown = texts => (string)Call(sw, "TextShownOf", texts);
                if (shown(new List<string> { "16,3" }) != "16,3") wrong.Add("16,3: " + shown(new List<string> { "16,3" }));
                if (shown(new List<string> { "Ø", "10" }) != "10") wrong.Add("a diameter: " + shown(new List<string> { "Ø", "10" }));
                if (shown(new List<string> { "2,5 x 45°" }) != "2,5 45") wrong.Add("a chamfer: " + shown(new List<string> { "2,5 x 45°" }));
                if (shown(new List<string> { "193.7" }) != "193.7") wrong.Add("a point kept: " + shown(new List<string> { "193.7" }));
                if (shown(new List<string>()) != null) wrong.Add("no text gave a number");
                if (shown(new List<string> { "abc", null }) != null) wrong.Add("a text with no number gave one");
                if (shown(null) != null) wrong.Add("unread texts gave a number");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "4 printed as written, 3 with nothing" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void NoteOfOutcomes(Type tools)
        {
            const string label = "N36: the note of the compact answer counts the warnings the measures keep, and is not there when nothing is left out";
            try
            {
                var wrong = new List<string>();
                Func<int, int, int, string> note = (explanations, echoes, warnings) =>
                    (string)Call(tools, "NoteOutcomes", explanations, echoes, warnings);
                var two = note(2, 0, 2);
                if (two == null || !two.Contains("none of them is a warning; the 2 warning(s) of the measures stay, above"))
                    wrong.Add("two warnings kept: " + (two ?? "null"));
                var zero = note(2, 0, 0);
                if (zero == null || zero.Contains("warning(s) of the measures")) wrong.Add("no warning: " + (zero ?? "null"));
                if (note(0, 0, 3) != null) wrong.Add("a note when nothing is left out");
                var echo = note(0, 1, 1);
                if (echo == null || !echo.Contains("The 1 warning(s) of the measures stay, above.") || !echo.Contains("1 measure(s) left out"))
                    wrong.Add("echoes only: " + (echo ?? "null"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "2 warnings counted, none, nothing left out, echoes only" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING, ON THE SOURCES
        // =================================================================

        private const string EarlyCheck = "componentsAsked = OverallComponents(a[\"components\"], AssemblyComponentNames(view), out componentsProblem);";
        // The call itself: since library 0.174.2 it is wrapped in the timing of its
        // phase (round 11), so the anchor is the call, not the assignment.
        private const string EdgesRead = "view.GetVisibleEntities2(";
        // Since round 21 (point 2) the route says whether the view is of a part: false here.
        private const string AskedPassed = "dimensionOffset, planStep, scale, mu, tr, componentsAsked, false);";
        private const string NotInViewKey = "measurements[\"components_not_in_view\"] = notInView.ToArray();";
        private const string OldLateCheck = "OverallComponents(a.ContainsKey(\"components\")";
        private const string RuleCalled = "if (RebuildsAfterStep(";
        private const string FlagRead = "measurements[\"unsaved_changes\"] = unsaved;";
        private const string FlagWarning = "measurements[\"warning_unsaved_after_save\"] = UnsavedAfterSaveWarning(Title(model));";
        private const string PlanCalls = "SwOperations.AfterStep(model, planStep.Operation, e.Measurements);";
        private const string OldPlanRebuild = "try { SwOperations.RebuildKeepingUndo(model); } catch { }";
        private const string BenchCalls = "afterStep.Invoke(null, new object[] { doc, operation, measured });";
        private const string ShownRow = "[\"dimensions_shown\"] = TextsShownInARow(born),";
        private const string NoteCall = "var outcomesNote = NoteOutcomes(explanationsLeftOut, echoesLeftOut, WarningsInOutcomes(outcomes));";
        private const string OldNote = "left out (none is a warning)";
        private const string Position = "described[\"position_mm\"] = WhereItEnded(";
        private const string Shown = "described[\"text_shown\"] = TextShown(";
        // Since round 21 (point 4) a dimension placed reads what it prints after its decimals
        // are made to print its value: the same N35, through ExactTextShown.
        private const string ExactShown = "described[\"text_shown\"] = ExactTextShown(";

        private static readonly string[][] Files =
        {
            new[] { "dimension", "Oce.Solid.Agent.Core", "DrawingDimensionOperations.cs" },
            new[] { "assembly", "Oce.Solid.Agent.Core", "DrawingAssemblyDimensions.cs" },
            new[] { "round", "Oce.Solid.Agent.Core", "DrawingRoundOperations.cs" },
            new[] { "operations", "Oce.Solid.Agent.Core", "SwOperations.cs" },
            new[] { "plan", "Oce.Solid.Agent.Core", "PlanExecutor.cs" },
            new[] { "bench", "Bench", "Program.cs" },
            new[] { "tools", "Tools", "McpTools.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "dimension", EarlyCheck, "the names of «components» are not checked on the assembly before the view is read (N33)" },
            new[] { "dimension", AskedPassed, "the names checked are not passed to the overall size (N33)" },
            new[] { "assembly", NotInViewKey, "a component asked and not in the view is not said (N33)" },
            new[] { "operations", RuleCalled, "the plan rebuilds a clean drawing after a step (N34)" },
            new[] { "operations", FlagRead, "a save does not say the flag read after the rebuild (N34)" },
            new[] { "operations", FlagWarning, "a flag raised after a save is not a warning (N34)" },
            new[] { "plan", PlanCalls, "the plan does not run the after-step the bench runs (N34)" },
            new[] { "bench", BenchCalls, "the bench rebuilds on its own, not as the plan does (N34)" },
            new[] { "dimension", ShownRow, "the dimensions of a part view do not say what the sheet prints (N35)" },
            new[] { "assembly", ShownRow, "the dimensions of an assembly view do not say what the sheet prints (N35)" },
            new[] { "tools", NoteCall, "the note of the compact answer does not count the warnings kept (N36)" },
        };

        /// <summary>The lines that must not come back, with what they bring back.</summary>
        private static readonly string[][] Gone =
        {
            new[] { "assembly", OldLateCheck, "«components» is checked on the view again, after its edges (N33)" },
            new[] { "plan", OldPlanRebuild, "the plan rebuilds on its own again, past the rule of the after-step (N34)" },
            new[] { "tools", OldNote, "«none is a warning» is back alone (N36)" },
        };

        private static int Count(string text, string what)
        {
            int n = 0;
            for (int i = text.IndexOf(what, StringComparison.Ordinal); i >= 0; i = text.IndexOf(what, i + what.Length, StringComparison.Ordinal)) n++;
            return n;
        }

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
            string dimension;
            if (sources.TryGetValue("dimension", out dimension) && dimension != null)
            {
                int check = dimension.IndexOf(EarlyCheck, StringComparison.Ordinal);
                int edges = dimension.IndexOf(EdgesRead, StringComparison.Ordinal);
                if (check < 0 || edges < 0 || check > edges)
                    wrong.Add("the names of «components» are checked after the edges of the view are read (N33)");
            }
            // N35: every dimension that says where it ended says what it prints.
            foreach (var key in new[] { "dimension", "assembly", "round" })
            {
                string text;
                if (!sources.TryGetValue(key, out text) || text == null) continue;
                int positions = Count(text, Position), shown = Count(text, Shown) + Count(text, ExactShown);
                if (positions == 0 || shown != positions)
                    wrong.Add(key + ": " + shown + " dimensions say what the sheet prints, of " + positions + " (N35)");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: «components» checked on the assembly before the edges and the hidden ones said, the after-step of plan and bench with the rule of the clean drawing and the flag, every dimension with what it prints, the note of the compact answer";
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Files)
            {
                var path = Path.Combine(Root(), f[1], f[2]);
                sources[f[0]] = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            var real = WiringProblems(sources);

            // Teeth: each line taken away, each old line put back, the order swapped.
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
            mutate("the names checked after the edges", "dimension",
                   t => t.Replace(EarlyCheck, "").Replace(EdgesRead, EdgesRead + "\n" + EarlyCheck));
            foreach (var key in new[] { "dimension", "assembly", "round" })
            {
                string text;
                if (!sources.TryGetValue(key, out text) || text == null) continue;
                string form = text.Contains(ExactShown) ? ExactShown : Shown;
                int first = text.IndexOf(form, StringComparison.Ordinal);
                if (first < 0) continue;
                mutate("one dimension of " + key + " without what it prints", key,
                       t => t.Substring(0, first) + "/* gone */" + t.Substring(first + form.Length));
            }

            bool ok = real.Count == 0 && blind.Count == 0;
            string detail = real.Count > 0 ? string.Join("; ", real)
                          : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                          : Lines.Length + " lines, " + Gone.Length + " old lines kept out, the order and the 3 files of dimensions; " + copies + " changed copies all red";
            Check(label, ok, detail);
        }
    }
}
