using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 15 OF REPORT 8CD843 (28/10/2026), WHERE THE BENCH
    /// CANNOT REACH.
    ///
    /// N57: with the second sheet active, inspect_part gave V1 to the isometric view of
    /// that sheet, while on the first sheet V1 is the front view, and no view said its
    /// sheet; a plan that takes away «V1» after new_sheet takes away a view of another
    /// sheet. Observation 2: «hatches_solids» read 0 beside a PDF with the hatching.
    ///
    /// Two halves, as Zc2197: the decision by reflection, and the wiring on the
    /// sources, every rule first red on a changed copy. On library 0.174.5 every check
    /// is red.
    /// </summary>
    internal static class Zc2198DrawingRound15Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 15 of report 8cd843: the sheet of every view, no view taken away by number on a drawing of more sheets, no solid fills beside the hatching (N57)");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 15 fixes",
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

            NumberOnManySheets(sw);
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
        // THE PURE RULE
        // =================================================================

        private static void NumberOnManySheets(Type sw)
        {
            const string label = "N57: a view asked by number on a drawing of more sheets is not taken away, and the refusal names the view; one sheet, a real name or a name that is not a number go";
            try
            {
                var wrong = new List<string>();
                // The drawing of round 15: sheet 2 active, V1 is its isometric view.
                var refused = (string)Call(sw, "UidOnManySheets", "V1", false, "Drawing View11", "Sheet2", 2);
                foreach (var part in new[] { "«V1» numbers the views of the active sheet «Sheet2»", "2 sheets", "«Drawing View11»", "I touched nothing" })
                    if (refused == null || !refused.Contains(part)) wrong.Add("the refusal lacks «" + part + "»: " + (refused ?? "null"));
                if (Call(sw, "UidOnManySheets", " v3 ", false, "Drawing View3", "Sheet1", 3) == null) wrong.Add("« v3 » on three sheets went");
                if (Call(sw, "UidOnManySheets", "V2", false, "Drawing View2", "Sheet1", 1) != null) wrong.Add("V2 on one sheet was refused (the bench case view_is_deleted)");
                if (Call(sw, "UidOnManySheets", "V1", true, "V1", "Sheet2", 2) != null) wrong.Add("a view really called V1 was refused");
                if (Call(sw, "UidOnManySheets", "Detail View C (2 : 1)", false, "Detail View C (2 : 1)", "Sheet1", 2) != null) wrong.Add("a name was refused");
                if (Call(sw, "UidOnManySheets", "V1", false, "Drawing View1", "Sheet1", 0) != null) wrong.Add("a sheet count not read was refused");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "V1 on sheet 2 of round 15 refused with its name; one sheet, a real V1 and a name go" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING
        // =================================================================

        private const string SheetOfView = "[\"sheet\"] = active,";
        private const string NoteViews = "data[\"note_views\"] = \"V1, V2 ... number the views of the active sheet «\"";
        private const string OnlyToTakeAway = "if (verb == \"take away\")";
        private const string Refusal = "reason = UidOnManySheets(wanted, KnownViewName(names, wanted), view.GetName2(), ActiveSheet(drawing), sheets);";
        private const string SheetSaid = "[\"sheet\"] = ActiveSheet(drawing),";
        private const string OldSolids = "[\"hatches_solids\"] = solids,";
        private const string OldSolidRead = "sectionView.GetSolidHatchCount(out q)";

        private static readonly string[][] Files =
        {
            new[] { "state", "Oce.Solid.Agent.Core", "DrawingState.cs" },
            new[] { "view", "Oce.Solid.Agent.Core", "DrawingViewOperations.cs" },
            new[] { "section", "Oce.Solid.Agent.Core", "DrawingSectionOperations.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "state", SheetOfView, "a view of the state does not say its sheet, and V1 of two sheets look the same (N57)" },
            new[] { "state", NoteViews, "nothing says that V1, V2 ... count the views of the active sheet only (N57)" },
            new[] { "view", OnlyToTakeAway, "the number is refused for moves too, which can be seen and undone (N57)" },
            new[] { "view", Refusal, "a view is taken away by number on a drawing of more sheets (N57)" },
            new[] { "view", SheetSaid, "move_view and delete_view do not say the sheet they acted on (N57)" },
        };

        /// <summary>The lines that must not come back, with what they bring back.</summary>
        private static readonly string[][] Gone =
        {
            new[] { "section", OldSolids, "a 0 beside the hatching, read as «no hatching» (observation 2)" },
            new[] { "section", OldSolidRead, "the solid fills read again for nothing (observation 2)" },
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
            string view;
            if (sources.TryGetValue("view", out view) && view != null)
            {
                int only = view.IndexOf(OnlyToTakeAway, StringComparison.Ordinal);
                int refusal = view.IndexOf(Refusal, StringComparison.Ordinal);
                if (only < 0 || refusal < 0 || only > refusal)
                    wrong.Add("the refusal by number is not inside the branch of delete_view (N57)");
                int count = 0;
                for (int i = view.IndexOf(SheetSaid, StringComparison.Ordinal); i >= 0; i = view.IndexOf(SheetSaid, i + 1, StringComparison.Ordinal)) count++;
                if (count < 2) wrong.Add("the sheet is said by " + count + " of the two operations (N57)");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: the sheet of every view and the note on the numbers, the refusal by number only for delete_view, the sheet in the answers of move_view and delete_view, no solid fills in the section";
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Files)
            {
                var path = Path.Combine(Root(), f[1], f[2]);
                sources[f[0]] = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            var real = WiringProblems(sources);

            // Teeth: each line taken away, each old line put back, the order swapped, one answer without its sheet.
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
            mutate("the refusal by number outside the branch of delete_view", "view",
                   t => t.Replace(Refusal, "").Replace(OnlyToTakeAway, Refusal + "\n" + OnlyToTakeAway));
            mutate("one answer without its sheet", "view",
                   t => { int i = t.IndexOf(SheetSaid, StringComparison.Ordinal); return i < 0 ? t : t.Remove(i, SheetSaid.Length); });

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : Lines.Length + " lines, " + Gone.Length + " old lines kept out, one order, two answers; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
