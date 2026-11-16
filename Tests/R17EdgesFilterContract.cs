using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// WHICH EDGES THE STATE LISTS (brief R17). On 25/10/2026 the edges to fillet
    /// on 26P000007 were picked by hand among the thirty longest, and a shorter
    /// edge had no identifier at all. «inspect_part» takes «edges», the twin of
    /// «faces»: the direction, curved or straight, the length, a point near, the
    /// face they bound, the body. Each edge keeps its rank among ALL the edges,
    /// and a plan that cites E45 finds it. These checks read the rules on the
    /// built library and the wiring in the sources.
    /// </summary>
    internal static class R17EdgesFilterContract
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        internal static void RunAll()
        {
            Group("which edges the state lists: «edges» of inspect_part, by rank among all the edges (brief R17)");
            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library)) { NotHere("the rules of «edges»", library + " is missing: build the library first (build.ps1)"); return; }
            Type filter;
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
                filter = Assembly.Load(File.ReadAllBytes(library)).GetType("Oce.Solid.Agent.Core.EdgeFilter", false);
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (filter == null) { Check("EdgeFilter is in the library", false, "not found (0.160.0 or older)"); return; }
            Parsing(filter);
            Choosing(filter);
            Wiring();
        }

        private static Dictionary<string, object> D(params object[] pairs)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) d[(string)pairs[i]] = pairs[i + 1];
            return d;
        }

        private static object Parse(Type filter, object raw, out string problem)
        {
            var args = new object[] { raw, null };
            var f = filter.GetMethod("Parse", Any).Invoke(null, args);
            problem = (string)args[1];
            return f;
        }

        private static void Parsing(Type filter)
        {
            const string label = "«edges»: every condition read (the axis without case, the numbers, the point, the face, the body, the limit), and 13 forms that would ignore a condition refused";
            try
            {
                var wrong = new List<string>();
                string problem;
                var f = Parse(filter, D("along", "Z", "length_min_mm", 5, "length_max_mm", 40.5, "near_mm", new object[] { 1, 2, 3 },
                                        "within_mm", 0.5, "face", "F12", "body", "C2", "limit", 100), out problem);
                if (f == null) wrong.Add("a full filter refused: " + problem);
                else
                {
                    if ((string)filter.GetField("Along").GetValue(f) != "Z") wrong.Add("along " + filter.GetField("Along").GetValue(f));
                    if ((string)filter.GetField("Face").GetValue(f) != "F12") wrong.Add("face " + filter.GetField("Face").GetValue(f));
                    if ((int)filter.GetField("Limit").GetValue(f) != 100) wrong.Add("limit " + filter.GetField("Limit").GetValue(f));
                    if (!(bool)filter.GetProperty("Narrows").GetValue(f)) wrong.Add("a full filter does not narrow");
                }
                var lower = Parse(filter, D("along", "x"), out problem);
                if (lower == null || (string)filter.GetField("Along").GetValue(lower) != "X") wrong.Add("«x» is not read as X");
                var plain = Parse(filter, D(), out problem);
                if (plain == null || (bool)filter.GetProperty("Narrows").GetValue(plain) || (int)filter.GetField("Limit").GetValue(plain) != 30)
                    wrong.Add("an empty «edges» is not the ordinary reading of the 30 longest");
                var bad = new[]
                {
                    Tuple.Create((object)"x", "an object"),
                    Tuple.Create((object)D("colour", "red"), "has no «colour»"),
                    Tuple.Create((object)D("along", "diagonal"), "«x», «y» or «z»"),
                    Tuple.Create((object)D("curved", "yes"), "true"),
                    Tuple.Create((object)D("along", "x", "curved", true), "Pass one of the two"),
                    Tuple.Create((object)D("length_min_mm", "5"), "a number"),
                    Tuple.Create((object)D("length_min_mm", -1), "from 0"),
                    Tuple.Create((object)D("length_min_mm", 10, "length_max_mm", 5), "smaller one first"),
                    Tuple.Create((object)D("near_mm", new object[] { 1, 2 }), "three numbers"),
                    Tuple.Create((object)D("within_mm", 2), "give the point too"),
                    Tuple.Create((object)D("face", "E3"), "face identifier"),
                    Tuple.Create((object)D("body", " "), "body identifier"),
                    Tuple.Create((object)D("limit", 2.5), "whole number"),
                };
                foreach (var b in bad)
                {
                    var r = Parse(filter, b.Item1, out problem);
                    if (r != null || problem == null || !problem.Contains(b.Item2)) wrong.Add("«" + b.Item2 + "»: " + (r != null ? "taken" : problem));
                }
                Func<string, int> rank = u => (int)filter.GetMethod("RankOf", Any).Invoke(null, new object[] { u });
                if (rank("E45") != 45 || rank("e3") != 3 || rank("E045") != 0 || rank("E0") != 0 || rank("F3") != 0 || rank("E-1") != 0)
                    wrong.Add("RankOf: E45 " + rank("E45") + ", e3 " + rank("e3") + ", E045 " + rank("E045") + ", E0 " + rank("E0") + ", F3 " + rank("F3"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "1 full filter, x as X, empty = 30 longest, " + bad.Length + " refusals, E45 = 45" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message); }
        }

        private static void Choosing(Type filter)
        {
            const string label = "the choice: each condition holds, the cheap ones first (the curve, the face, the distance are not asked of an edge already out), the longest kept at the limit";
            try
            {
                var wrong = new List<string>();
                // Eight edges, longest first: body, length, curved, along, bounds F1, distance.
                var body = new[] { 0, 0, 0, 0, 1, 1, 0, 0 };
                var length = new double?[] { 100, 80, 60, 40, 40, 20, 10, null };
                var curved = new[] { false, false, true, false, false, true, false, false };
                var along = new[] { "X", "Z", null, "Z", "Z", null, null, "Y" };
                var face = new[] { true, true, false, true, false, false, true, true };
                var distance = new double?[] { 0.2, 5, 0.4, 0.9, 0.1, null, 3, 0.3 };
                Func<object, int, int, Tuple<List<int>, int, int, int, int>> run = (f, wantedBody, dummy) =>
                {
                    int curves = 0, faces = 0, distances = 0;
                    var choose = filter.GetMethod("Choose", Any);
                    var args = new object[]
                    {
                        8,
                        (Func<int, int>)(i => body[i]),
                        (Func<int, double?>)(i => length[i]),
                        (Func<int, bool>)(i => { curves++; return curved[i]; }),
                        (Func<int, string>)(i => along[i]),
                        (Func<int, bool>)(i => { faces++; return face[i]; }),
                        (Func<int, double?>)(i => { distances++; return distance[i]; }),
                        wantedBody, 0,
                    };
                    var list = (List<int>)choose.Invoke(f, args);
                    return Tuple.Create(list, (int)args[8], curves, faces, distances);
                };
                string problem;
                Func<Dictionary<string, object>, object> make = d => Parse(filter, d, out problem);

                var r1 = run(make(D("along", "z")), -2, 0);
                if (string.Join(",", r1.Item1) != "1,3,4" || r1.Item2 != 3) wrong.Add("along z: " + string.Join(",", r1.Item1) + " (" + r1.Item2 + ")");
                var r2 = run(make(D("curved", true)), -2, 0);
                if (string.Join(",", r2.Item1) != "2,5") wrong.Add("curved: " + string.Join(",", r2.Item1));
                var r3 = run(make(D("length_min_mm", 40, "length_max_mm", 80)), -2, 0);
                if (string.Join(",", r3.Item1) != "1,2,3,4") wrong.Add("length 40..80: " + string.Join(",", r3.Item1) + " (the edge whose length was not read must not match)");
                if (r3.Item3 != 0) wrong.Add("the curve was asked " + r3.Item3 + " times for a length-only filter");
                var r4 = run(make(D("face", "F1", "length_min_mm", 50)), -2, 0);
                if (string.Join(",", r4.Item1) != "0,1" || r4.Item4 != 3) wrong.Add("face F1 + length >= 50: " + string.Join(",", r4.Item1) + ", faces asked " + r4.Item4 + " (3 expected)");
                var r5 = run(make(D("near_mm", new object[] { 0, 0, 0 }, "within_mm", 0.5, "along", "z")), -2, 0);
                if (string.Join(",", r5.Item1) != "4" || r5.Item5 != 3) wrong.Add("near 0.5 + along z: " + string.Join(",", r5.Item1) + ", distances asked " + r5.Item5 + " (3 expected)");
                var r6 = run(make(D("along", "z", "limit", 2)), -2, 0);
                if (string.Join(",", r6.Item1) != "1,3" || r6.Item2 != 3) wrong.Add("limit 2: " + string.Join(",", r6.Item1) + " of " + r6.Item2);
                var r7 = run(make(D("body", "C2")), 1, 0);
                if (string.Join(",", r7.Item1) != "4,5") wrong.Add("body C2: " + string.Join(",", r7.Item1));
                var r8 = run(make(D("along", "y", "curved", false, "length_min_mm", 1)), -2, 0);
                if (r8.Item1.Count != 0 || r8.Item2 != 0) wrong.Add("nothing should match (the Y edge has no length): " + string.Join(",", r8.Item1));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "8 filters on 8 edges as expected; curve, face and distance asked only of the edges still in" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message); }
        }

        private static void Wiring()
        {
            const string label = "wired: the listing numbers an edge by its rank, the state keeps the ranking and finds E45 by TryGetSignature, the edge resolvers ask it, inspect_part offers «edges», the pre-check knows «edges_total»";
            var wrong = new List<string>();
            var state = Source("Oce.Solid.Agent.Core", "DocumentState.cs");
            if (state == null) wrong.Add("DocumentState.cs not found");
            else
            {
                if (!Regex.IsMatch(state, @"var uid = ""E"" \+ \(edgeRankIndex \+ 1\);") || Regex.IsMatch(state, @"edgeIndex\+\+"))
                    wrong.Add("the listing of the edges still counts instead of using the rank");
                if (!state.Contains("docState._rankedEdges = sortedItems;") || !state.Contains("EdgeFilter.RankOf(uid)"))
                    wrong.Add("the ranking of the edges is not kept, or TryGetSignature does not read it");
                if (!state.Contains("edge.GetClosestPointOn(Units.M(pointMm[0])"))
                    wrong.Add("the distance to an edge is not read from IEdge.GetClosestPointOn");
            }
            var operations = Source("Oce.Solid.Agent.Core", "SwOperations.cs");
            if (operations == null || Regex.Matches(operations, @"docState\.TryGetSignature\(uid, out signature\) &&\s*signature\.Type == ""EDGE""").Count != 2)
                wrong.Add("the two edge resolvers of SwOperations.cs do not both ask TryGetSignature");
            var tools = Source("Tools", "McpTools.cs");
            if (tools == null || !tools.Contains("[\"edges\"] = new Dictionary<string, object>") ||
                !tools.Contains("EdgeFilter.Parse(edgesRaw, out edgesProblem)") ||
                !tools.Contains("_session.StateWithFilters(facesWanted, edgesWanted, out error)") ||
                !tools.Contains("IsEdgeBeyondTheList(uid, docState)") ||
                !tools.Contains("edgesWanted != null && !docState.ContainsKey(\"edges_filter\")"))
                wrong.Add("inspect_part, its argument, the reading or the pre-check of a cited edge is not wired");
            Check(label, wrong.Count == 0, wrong.Count == 0 ? "5 places wired" : string.Join("; ", wrong));
        }

        private static string Source(string folder, string file)
        {
            var path = Path.Combine(Root(), folder, file);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }
}
