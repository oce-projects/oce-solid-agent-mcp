using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// WHAT A STEP MUST MEASURE (brief R16): the rules of «expect», read on the
    /// built library by reflection, as the other pure rules. A plan step can carry
    /// «expect» next to «arguments»; after the step each key is compared with the
    /// step's measurements, and one that does not hold, or that the step does not
    /// measure, makes the step fail. These rows are the promise of the tool's
    /// description: a plain number within 0.01, an explicit tolerance, lists
    /// element by element, «volume_mm3» as the volume after the step, and a form
    /// that cannot be checked refused before anything runs.
    /// </summary>
    internal static class R16StepExpectationsContract
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("what a step must measure: «expect» next to «arguments» (brief R16)");
            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the rules of «expect»", library + " is missing: build the library first (build.ps1)");
                return;
            }
            Type rules;
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
                rules = Assembly.Load(File.ReadAllBytes(library)).GetType("Oce.Solid.Agent.Core.StepExpectations", false);
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (rules == null) { Check("StepExpectations is in the library", false, "not found (0.155.0 or older)"); return; }

            Misses(rules);
            Forms(rules);
        }

        private static object Call(Type t, string method, params object[] args)
        {
            var m = t.GetMethod(method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method + " is not in this library");
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        private static Dictionary<string, object> D(params object[] pairs)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) d[(string)pairs[i]] = pairs[i + 1];
            return d;
        }

        private static void Misses(Type rules)
        {
            const string label = "the numbers of a step against «expect»: 0.01 by default, an explicit tolerance, lists, texts, volume_mm3, a key not measured";
            try
            {
                var wrong = new List<string>();
                var measured = D("removed_mm3", 35.996, "diameter_mm", 1.036, "walls_drilled", 1,
                                 "bounding_box_mm", new object[] { 120.0, 14.0, 80.0 }, "kind", "coincident",
                                 "volume_after_mm3", 6364.8, "suppressed", true);
                var rows = new[]
                {
                    Tuple.Create("removed 36, plain", D("removed_mm3", 36), 0),
                    Tuple.Create("diameter 0.992 against 1.036", D("diameter_mm", 0.992), 1),
                    Tuple.Create("diameter 1 within 0.05", D("diameter_mm", D("equal_to", 1.0, "tolerance", 0.05)), 0),
                    Tuple.Create("walls 1", D("walls_drilled", 1), 0),
                    Tuple.Create("box element by element", D("bounding_box_mm", new object[] { 120, 14, 80 }), 0),
                    Tuple.Create("box one element off", D("bounding_box_mm", new object[] { 120, 15, 80 }), 1),
                    Tuple.Create("text equal", D("kind", "coincident"), 0),
                    Tuple.Create("text different", D("kind", "distance"), 1),
                    Tuple.Create("true", D("suppressed", true), 0),
                    Tuple.Create("volume_mm3 is the volume after", D("volume_mm3", 6364.8), 0),
                    Tuple.Create("a key the step does not measure", D("added_mm3", 10), 1),
                    Tuple.Create("two keys, one off", D("removed_mm3", 36, "diameter_mm", 0.992), 1),
                };
                foreach (var row in rows)
                {
                    var misses = (List<string>)Call(rules, "Misses", row.Item2, measured);
                    if (misses.Count != row.Item3) wrong.Add(row.Item1 + ": " + misses.Count + " misses instead of " + row.Item3 +
                                                             (misses.Count > 0 ? " (" + string.Join("; ", misses) + ")" : ""));
                }
                var said = (List<string>)Call(rules, "Misses", D("diameter_mm", 0.992), measured);
                if (said.Count != 1 || !said[0].Contains("0.992") || !said[0].Contains("1.036"))
                    wrong.Add("a miss does not put the two numbers side by side: " + string.Join("; ", said));
                var unknown = (List<string>)Call(rules, "Misses", D("added_mm3", 10), measured);
                if (unknown.Count != 1 || !unknown[0].Contains("removed_mm3"))
                    wrong.Add("a key not measured does not say what the step measures: " + string.Join("; ", unknown));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? rows.Length + " rows, and the sentences say the numbers" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, ex.GetType().Name + ": " + ex.Message); }
        }

        private static void Forms(Type rules)
        {
            const string label = "a form of «expect» that cannot be checked is refused before anything runs";
            try
            {
                var wrong = new List<string>();
                var good = new object[] { null, D("removed_mm3", 36), D("diameter_mm", D("equal_to", 0.992, "tolerance", 0.001)),
                                          D("kind", "concentric"), D("box", new object[] { 1, 2, 3 }), D("ok", true) };
                foreach (var g in good)
                    if (Call(rules, "FormProblem", g) != null) wrong.Add("refused a good form: " + Call(rules, "FormProblem", g));
                var bad = new object[] { "36", new object[] { 1 }, D(), D("x", D("tolerance", 1)), D("x", D("equal_to", 1, "tolerance", -1)),
                                         D("x", D("equal_to", 1, "within", 1)), D("x", D("nested", 1)), D("x", new object[0]),
                                         D("x", new object[] { "a" }) };
                foreach (var b in bad)
                    if (Call(rules, "FormProblem", b) == null) wrong.Add("took a bad form (" + (b == null ? "null" : b.GetType().Name) + ")");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? good.Length + " good forms taken, " + bad.Length + " bad ones refused" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, ex.GetType().Name + ": " + ex.Message); }
        }
    }
}
