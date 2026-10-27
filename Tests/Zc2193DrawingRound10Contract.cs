using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 10 OF REPORT 8CD843 (27/10/2026), WHERE THE BENCH
    /// CANNOT REACH.
    ///
    /// N37: the note of the compact answer counted «warnings» 0 of
    /// «save_document» as a warning («the 1 warning(s) of the measures stay»
    /// over measures with none). N38: a view of an assembly whose overall sizes
    /// were all dimensioned in other views answered «No dimension was created»,
    /// as if something had gone wrong, lost the components measured and the ones
    /// the view hides, and wrote «16.26» beside 16.258.
    ///
    /// Two halves, as Zc2192. The decisions are small static methods, called here
    /// by reflection; the bench has no case on a view of an assembly. The wiring
    /// is read on the sources, and every rule first turns red on a copy where its
    /// line is gone, where the old line is back or where the order is swapped.
    ///
    /// On library 0.174.0 and bridge 0.152.0 every check is red: the methods and
    /// the lines are missing.
    /// </summary>
    internal static class Zc2193DrawingRound10Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 10 of report 8cd843: the warnings the note counts, a view of an assembly with nothing new to dimension (N37, N38)");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 10 fixes",
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

            NothingNew(sw);

            var bridge = BridgeOrNotHere("the warnings the note of the compact answer counts");
            if (bridge == null) return;
            Type tools;
            try { tools = Load.Class(Load.Bridge(bridge), "McpTools"); }
            catch (Exception ex) { Check("the bridge loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (tools == null) { Check("McpTools is in the bridge", false, "type missing"); return; }
            WarningValues(tools);
            WarningsCounted(tools);
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

        private static void NothingNew(Type sw)
        {
            const string label = "N38: a view of an assembly with every overall size dimensioned elsewhere is refused as nothing new, with the components measured, the ones not in the view and three decimals";
            try
            {
                var wrong = new List<string>();
                var elsewhere = new List<string> { "overall length 16.258 mm along Z (already dimensioned in another view of this assembly)" };
                var measured = new HashSet<string> { "26P000011-1", "26P000008-1" };
                var hidden = new List<string> { "26P000007-1" };
                var refusal = (string)Call(sw, "NothingNewOnTheView", "Drawing View3", elsewhere, measured, hidden);
                foreach (var part in new[]
                {
                    "I touched nothing: in the view «Drawing View3» of the assembly there is nothing left to dimension that is not already dimensioned",
                    "16.258 mm along Z",
                    "UNI ISO 129-1",
                    "leave this step out of the plan",
                    "Measured on «26P000008-1», «26P000011-1», the components asked that this view shows.",
                    "Not in this view: «26P000007-1».",
                })
                    if (refusal == null || !refusal.Contains(part)) wrong.Add("the refusal lacks «" + part + "»: " + (refusal ?? "null"));
                if (refusal != null && refusal.Contains("No dimension was created")) wrong.Add("it still reads as a failure: " + refusal);
                var leftOut = (string)Call(sw, "ComponentsSaid", null, new List<string>());
                if (leftOut != "") wrong.Add("«components» left out said something: «" + leftOut + "»");
                var onlyHidden = (string)Call(sw, "ComponentsSaid", null, hidden);
                if (onlyHidden != " Not in this view: «26P000007-1».") wrong.Add("only the hidden one: «" + onlyHidden + "»");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "the refusal says nothing new, the rule, the remedy and the components; nothing when «components» is left out" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void WarningValues(Type tools)
        {
            const string label = "N37: a «warning…» key is a warning only with a text or a list with something in it, not with a number";
            try
            {
                var wrong = new List<string>();
                Func<object, bool> says = v => (bool)Call(tools, "SaysAWarning", v);
                var yes = new object[] { "the empty columns", new[] { "a" }, new List<string> { "b" }, new List<object> { 1 } };
                var no = new object[] { 0, 2, false, true, null, "", "   ", new string[0], new List<object>() };
                foreach (var v in yes) if (!says(v)) wrong.Add("not a warning: " + Shown(v));
                foreach (var v in no) if (says(v)) wrong.Add("a warning: " + Shown(v));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? yes.Length + " warnings, " + no.Length + " not" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static string Shown(object v)
        {
            if (v == null) return "null";
            return v.GetType().Name + " «" + v + "»";
        }

        private static void WarningsCounted(Type tools)
        {
            const string label = "N37: the note counts the warnings of the measures, not «warnings» 0 of a save (the plans 006 and 014 of round 10)";
            try
            {
                var wrong = new List<string>();
                Func<List<object>, int> count = o => (int)Call(tools, "WarningsInOutcomes", o);
                var save = new Dictionary<string, object>
                {
                    ["measurements"] = new Dictionary<string, object>
                    {
                        ["warnings"] = 0,
                        ["save_warnings"] = "none (the Warnings mask of the save is 0)",
                    },
                };
                var bom = new Dictionary<string, object>
                {
                    ["measurements"] = new Dictionary<string, object> { ["warning_empty_columns"] = "column 2 is empty" },
                };
                var pdf = new Dictionary<string, object>
                {
                    ["measurements"] = new Dictionary<string, object> { ["warning"] = "a text was not found in the PDF", ["warning_none"] = null },
                };
                var failed = new Dictionary<string, object> { ["measurements"] = null };
                int round006 = count(new List<object> { save });
                int round014 = count(new List<object> { bom, save, pdf, failed });
                if (round006 != 0) wrong.Add("006 (a save alone): " + round006 + ", expected 0");
                if (round014 != 2) wrong.Add("014 (the table, a save, the PDF): " + round014 + ", expected 2");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "006: 0, 014: 2, as the report counted" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING, ON THE SOURCES
        // =================================================================

        private const string NothingNewBranch = "if (put <= 0 && failed.Count == 0 && elsewhere.Count > 0)";
        private const string NothingNewRefused = "return Failed(NothingNewOnTheView(viewName, elsewhere, only, notInView));";
        private const string GeneralRefusal = "\"No dimension was created on the view «\"";
        private const string GeneralSaysComponents = "\"and name the ones to dimension with «edges».\" + ComponentsSaid(only, notInView));";
        private const string AssemblyDecimals = "expectedMm.ToString(\"0.###\", Ci) + \" mm along \"";
        private const string OldAssemblyDecimals = "expectedMm.ToString(\"0.##\", Ci) + \" mm along \"";
        private const string PartDecimals = "Math.Round(s.Length * 1000.0 / scale, 3)";
        private const string OldPartDecimals = "Math.Round(s.Length * 1000.0 / scale, 2)";
        private const string WarningRule = "key.StartsWith(\"warning\", StringComparison.OrdinalIgnoreCase) && SaysAWarning(measurements[key])";
        private const string OldWarningRule = "measurements[key] != null && key.StartsWith(\"warning\"";

        private static readonly string[][] Files =
        {
            new[] { "dimension", "Oce.Solid.Agent.Core", "DrawingDimensionOperations.cs" },
            new[] { "assembly", "Oce.Solid.Agent.Core", "DrawingAssemblyDimensions.cs" },
            new[] { "tools", "Tools", "McpTools.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "assembly", NothingNewBranch, "a view of an assembly with nothing new is not told apart from a failure (N38)" },
            new[] { "assembly", NothingNewRefused, "the refusal of nothing new is not the one that names the rule and the components (N38)" },
            new[] { "assembly", GeneralSaysComponents, "the other refusals of a view of an assembly do not name the components (N38)" },
            new[] { "assembly", AssemblyDecimals, "the lengths already dimensioned on an assembly are not written with three decimals (N38)" },
            new[] { "dimension", PartDecimals, "the lengths already dimensioned on a part are not written with three decimals (N38)" },
            new[] { "tools", WarningRule, "the note counts a «warning…» key whatever it holds (N37)" },
        };

        /// <summary>The lines that must not come back, with what they bring back.</summary>
        private static readonly string[][] Gone =
        {
            new[] { "assembly", OldAssemblyDecimals, "«16.26» beside 16.258 is back on an assembly (N38)" },
            new[] { "dimension", OldPartDecimals, "two decimals are back on a part (N38)" },
            new[] { "tools", OldWarningRule, "«warnings» 0 counted as a warning is back (N37)" },
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
            string assembly;
            if (sources.TryGetValue("assembly", out assembly) && assembly != null)
            {
                int branch = assembly.IndexOf(NothingNewBranch, StringComparison.Ordinal);
                int general = assembly.IndexOf(GeneralRefusal, StringComparison.Ordinal);
                if (branch < 0 || general < 0 || branch > general)
                    wrong.Add("the refusal of nothing new comes after the general one, which takes it first (N38)");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: nothing new on a view of an assembly refused before the general refusal and naming the components, three decimals on both kinds of view, the note counting only real warnings";
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
            mutate("the refusal of nothing new after the general one", "assembly",
                   t => t.Replace(NothingNewBranch, "").Replace(GeneralRefusal, GeneralRefusal + "\n" + NothingNewBranch));

            bool ok = real.Count == 0 && blind.Count == 0;
            string detail = real.Count > 0 ? string.Join("; ", real)
                          : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                          : Lines.Length + " lines, " + Gone.Length + " old lines kept out, the order of the two refusals; " + copies + " changed copies all red";
            Check(label, ok, detail);
        }
    }
}
