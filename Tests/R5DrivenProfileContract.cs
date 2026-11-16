using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// A PROFILE DRIVEN BY ITS VERTICES (brief R5). On 24/10/2026 every
    /// «extrude_profile» came back locked with FIXED relations, and the only
    /// dimension a variable could drive was the depth. «dimensions» «driven»
    /// puts a dimension from the origin on every vertex instead. These are the
    /// rule that decides when it can be applied, read by reflection on the built
    /// library, the limit said in the catalogue, and the places of the source
    /// that use it. Whether SOLIDWORKS gives the dimensions is the bench case
    /// «a_profile_is_driven_by_its_vertices».
    /// </summary>
    internal static class R5DrivenProfileContract
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("a profile driven by its vertices: «dimensions» of extrude_profile (brief R5)");
            Rule();
            Wiring();
        }

        private static void Rule()
        {
            const string label = "«dimensions»: fixed and driven taken; another word, a curve and too many vertices refused with what to pass";
            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library)) { NotHere(label, library + " is missing: build the library first (build.ps1)"); return; }
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
                var operations = Assembly.Load(File.ReadAllBytes(library)).GetType("Oce.Solid.Agent.Core.SwOperations", false);
                var rule = operations == null ? null : operations.GetMethod("DimensionsModeProblem", Any);
                var limitField = operations == null ? null : operations.GetField("MaxDrivenVertices", Any);
                if (rule == null || limitField == null) { Check(label, false, "SwOperations.DimensionsModeProblem or MaxDrivenVertices is not in this library (0.161.0 or older)"); return; }
                int limit = (int)limitField.GetValue(null);
                Func<string, int, int, string> problem = (m, curved, vertices) => (string)rule.Invoke(null, new object[] { m, curved, vertices });
                var wrong = new List<string>();
                var taken = new[] { Tuple.Create("fixed", 0, 6), Tuple.Create("fixed", 3, 900), Tuple.Create("driven", 0, 6), Tuple.Create("driven", 0, limit) };
                foreach (var t in taken)
                    if (problem(t.Item1, t.Item2, t.Item3) != null) wrong.Add("refused " + t.Item1 + " with " + t.Item2 + " curves and " + t.Item3 + " vertices");
                var refused = new[] { Tuple.Create("free", 0, 6), Tuple.Create("Driven", 0, 6), Tuple.Create("", 0, 6), Tuple.Create("driven", 1, 6), Tuple.Create("driven", 0, limit + 1) };
                foreach (var t in refused)
                {
                    string said = problem(t.Item1, t.Item2, t.Item3);
                    if (said == null) wrong.Add("took «" + t.Item1 + "» with " + t.Item2 + " curves and " + t.Item3 + " vertices");
                    else if (said.IndexOf("«fixed»", StringComparison.Ordinal) < 0) wrong.Add("the refusal of «" + t.Item1 + "» does not name «fixed»: " + said);
                }
                if (problem(null, 0, 6) == null) wrong.Add("took a missing mode as a word");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? taken.Length + " taken, " + (refused.Length + 1) + " refused, the limit is " + limit : string.Join("; ", wrong));

                // THE NUMBER IN THE CATALOGUE IS THE NUMBER IN THE CODE.
                const string limitLabel = "the catalogue says the limit of the driven vertices that the code applies";
                var catalog = Path.Combine(Root(), "Oce.Solid.Agent.Core", "Catalog", "Operations.cs");
                var text = File.Exists(catalog) ? File.ReadAllText(catalog) : "";
                var said100 = Regex.Matches(text, @"at most (\d+) vertices");
                var off = new List<string>();
                foreach (Match m in said100) if (m.Groups[1].Value != limit.ToString()) off.Add(m.Value);
                Check(limitLabel, said100.Count >= 2 && off.Count == 0,
                      said100.Count < 2 ? "the catalogue says the limit " + said100.Count + " time(s), two expected (description and argument)"
                                        : off.Count == 0 ? said100.Count + " places say " + limit : "different: " + string.Join("; ", off));
            }
            catch (Exception ex) { Check(label, false, (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message); }
        }

        private static void Wiring()
        {
            const string label = "the dimensions go in before the sketch is closed; a driven sketch is not locked; the catalogue has the argument";
            var source = Path.Combine(Root(), "Oce.Solid.Agent.Core", "SwOperations.cs");
            var catalog = Path.Combine(Root(), "Oce.Solid.Agent.Core", "Catalog", "Operations.cs");
            if (!File.Exists(source) || !File.Exists(catalog)) { NotHere(label, "the sources are missing"); return; }
            var text = File.ReadAllText(source);
            var catalogText = File.ReadAllText(catalog);
            var missing = new List<string>();
            int drive = text.IndexOf("vertexDimensions = DriveProfile(model, sketches.ActiveSketch as ISketch, contours", StringComparison.Ordinal);
            int close = drive < 0 ? -1 : text.IndexOf("sketches.InsertSketch(false);", drive, StringComparison.Ordinal);
            if (drive < 0) missing.Add("ExtrudeProfile does not call DriveProfile on the open sketch");
            else if (close < 0 || close - drive > 400) missing.Add("the sketch is not closed right after the dimensions");
            // Since 0.141.0 (brief R11) the lock goes on in the open sketch, in the
            // «else» of the driven branch, and no longer after the extrusion.
            int lockOpen = drive < 0 ? -1 : text.IndexOf("locked = LockOpenSketch(", drive, StringComparison.Ordinal);
            if (lockOpen < 0 || text.IndexOf("else", drive, lockOpen - drive, StringComparison.Ordinal) < 0)
                missing.Add("a driven sketch is still locked");
            if (text.IndexOf("swInputDimValOnCreate, false", text.IndexOf("private List<Dictionary<string, object>> DriveProfile(", StringComparison.Ordinal) + 1, StringComparison.Ordinal) < 0)
                missing.Add("DriveProfile does not switch the value box off");
            if (!Regex.IsMatch(catalogText, "\"dimensions\", O\\(\\s*\"type\", \"string\",\\s*\"enum\", A\\(\\s*\"fixed\",\\s*\"driven\"\\)"))
                missing.Add("the catalogue has no «dimensions» with fixed and driven");
            Check(label, missing.Count == 0, missing.Count == 0 ? "5 places found" : string.Join("; ", missing));
        }
    }
}
