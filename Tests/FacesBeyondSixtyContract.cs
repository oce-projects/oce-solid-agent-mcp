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
    /// FACES BEYOND THE 60 LARGEST (entry W3.1-bis.12), the half that needs no
    /// SOLIDWORKS.
    ///
    /// The state listed the 60 largest faces and a face that was not among them
    /// had no identifier: on the tube stand of task 07 (177 faces) the walls of
    /// the third and the fourth foot tie at 4,800 mm2 behind 40 larger faces of
    /// the frame, the ceiling cut them in the order of the bodies, and neither a
    /// plan nor the model could name them. The fix has three parts, and the
    /// bench measures them live (cases in Bench/cases/faces_beyond_sixty/, the
    /// stand itself in Bench/tasks/prerequisites/07_tube_stand.json):
    ///
    ///   1. every face has an identifier, the rank of its area among ALL the
    ///      faces of the part (F1 the largest), whether it is listed or not;
    ///   2. inspect_part takes «faces», a filter (FaceFilter), and lists the
    ///      faces that match it with those identifiers;
    ///   3. the runner of the prerequisites reads every face before it resolves
    ///      a selector.
    ///
    /// What is checked here is what does not need the CAD: the filter's parsing
    /// and refusals, the choice of the faces (and that the expensive reads are
    /// asked for last), the reading of an identifier, and, in the sources, that
    /// the three parts are wired — that the listing numbers a face by its rank
    /// and not by a counter of what was listed, that the five places which find a
    /// face by its identifier ask the state for it by rank, that inspect_part
    /// offers the argument and does not refuse a rank the state did not list.
    /// FaceFilter is called on the BUILT library by reflection, as the other
    /// contracts do; on library 0.152.0 it is not there and the first check is red.
    /// </summary>
    internal static class FacesBeyondSixtyContract
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("faces beyond the 60 largest: an identifier for every face, a filter for the model, every face for the runner (W3.1-bis.12)");

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("faces beyond the 60 largest", library + " is missing: build the library first (build.ps1)");
                return;
            }
            Assembly asm;
            try
            {
                var redist = WhereTheInteropIs();
                if (redist != null)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += (s, a) =>
                    {
                        var name = new AssemblyName(a.Name).Name;
                        if (!name.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase)) return null;
                        var file = Path.Combine(redist, name + ".dll");
                        return File.Exists(file) ? Assembly.LoadFrom(file) : null;
                    };
                }
                asm = Assembly.Load(File.ReadAllBytes(library));
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }

            Type filter = asm.GetType("Oce.Solid.Agent.Core.FaceFilter", false);
            Check("the library has FaceFilter, the filter of «faces»", filter != null,
                  "Oce.Solid.Agent.Core.FaceFilter is not in the library");
            if (filter != null)
            {
                try
                {
                    Parsing(filter);
                    Refusals(filter);
                    Identifiers(filter);
                    Choosing(filter);
                    Bodies(filter);
                }
                catch (Exception ex) { Check("FaceFilter answers", false, ex.GetType().Name + ": " + ex.Message); }
            }
            Wiring();
        }

        // ── the filter ──────────────────────────────────────────────────────

        private static object Parse(Type filter, object raw, out string problem)
        {
            var m = filter.GetMethod("Parse", Any);
            var args = new object[] { raw, null };
            object result;
            try { result = m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
            problem = args[1] as string;
            return result;
        }

        private static T Field<T>(object o, string name)
        {
            var f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return (T)f.GetValue(o);
        }

        private static void Parsing(Type filter)
        {
            string problem;
            var none = Parse(filter, new Dictionary<string, object>(), out problem);
            Check("an empty «faces» is the ordinary reading: no condition, the 60 largest",
                  none != null && !(bool)filter.GetProperty("Narrows").GetValue(none) &&
                  Field<int>(none, "Limit") == 60,
                  problem ?? (none == null ? "null" : "Limit " + Field<int>(none, "Limit")));

            var full = Parse(filter, new Dictionary<string, object>
            {
                ["normal"] = "Front", ["area_min_mm2"] = 4000, ["area_max_mm2"] = 5000.5,
                ["near_mm"] = new List<object> { 1, 2.5, -3L }, ["within_mm"] = 2,
                ["body"] = "C3", ["limit"] = 200L,
            }, out problem);
            bool ok = full != null && problem == null;
            if (ok)
            {
                var near = Field<double[]>(full, "NearMm");
                ok = Field<string>(full, "Normal") == "front" && Field<double?>(full, "AreaMinMm2") == 4000 &&
                     Field<double?>(full, "AreaMaxMm2") == 5000.5 && near != null && near.Length == 3 &&
                     near[1] == 2.5 && near[2] == -3 && Field<double>(full, "WithinMm") == 2 &&
                     Field<string>(full, "Body") == "C3" && Field<int>(full, "Limit") == 200 &&
                     (bool)filter.GetProperty("Narrows").GetValue(full);
            }
            Check("every condition is read: the direction without case, the numbers whatever their JSON type, the point, the body, the limit",
                  ok, problem ?? "a field did not read back");

            var onlyLimit = Parse(filter, new Dictionary<string, object> { ["limit"] = 500 }, out problem);
            Check("a limit alone is a reading of up to 500 of the largest faces: it narrows nothing",
                  onlyLimit != null && !(bool)filter.GetProperty("Narrows").GetValue(onlyLimit) && Field<int>(onlyLimit, "Limit") == 500,
                  problem ?? "");

            var describe = full == null ? null : filter.GetMethod("Describe").Invoke(full, null) as Dictionary<string, object>;
            Check("the filter is echoed as it was asked, and only that",
                  describe != null && describe.ContainsKey("normal") && describe.ContainsKey("near_mm") && describe.ContainsKey("within_mm") &&
                  describe.ContainsKey("body") && describe.ContainsKey("limit") && describe.ContainsKey("area_min_mm2") &&
                  onlyLimit != null && (filter.GetMethod("Describe").Invoke(onlyLimit, null) as Dictionary<string, object>).Count == 1,
                  describe == null ? "null" : string.Join(", ", describe.Keys));
        }

        private static void Refusals(Type filter)
        {
            Func<string, object, string, string> says = (what, raw, expect) =>
            {
                string problem;
                var f = Parse(filter, raw, out problem);
                return f == null && problem != null && problem.IndexOf(expect, StringComparison.OrdinalIgnoreCase) >= 0 ? null
                     : what + ": " + (f != null ? "accepted" : "said «" + problem + "»");
            };
            var bad = new List<string>();
            Action<string> add = r => { if (r != null) bad.Add(r); };
            add(says("a key that does not exist", new Dictionary<string, object> { ["colour"] = "red" }, "has no «colour»"));
            add(says("a direction that is not a word of «facing»", new Dictionary<string, object> { ["normal"] = "north" }, "must be one of"));
            add(says("not an object", "up", "must be an object"));
            add(says("an area range that is empty", new Dictionary<string, object> { ["area_min_mm2"] = 10, ["area_max_mm2"] = 5 }, "no face can match"));
            add(says("a negative area", new Dictionary<string, object> { ["area_min_mm2"] = -1 }, "must be from"));
            add(says("an area that is text", new Dictionary<string, object> { ["area_max_mm2"] = "12" }, "must be a number"));
            add(says("a distance with no point", new Dictionary<string, object> { ["within_mm"] = 3 }, "give the point"));
            add(says("a point of two numbers", new Dictionary<string, object> { ["near_mm"] = new List<object> { 1, 2 } }, "three numbers"));
            add(says("a point that is not numbers", new Dictionary<string, object> { ["near_mm"] = new List<object> { 1, "a", 3 } }, "three numbers"));
            add(says("a distance of zero", new Dictionary<string, object> { ["near_mm"] = new List<object> { 0, 0, 0 }, ["within_mm"] = 0 }, "above 0"));
            add(says("a limit of zero", new Dictionary<string, object> { ["limit"] = 0 }, "must be from"));
            add(says("a limit above 500", new Dictionary<string, object> { ["limit"] = 501 }, "must be from"));
            add(says("a limit that is not whole", new Dictionary<string, object> { ["limit"] = 2.5 }, "whole number"));
            add(says("an empty body", new Dictionary<string, object> { ["body"] = "  " }, "must be a body"));
            Check("a filter that would silently ignore a condition is refused, each with what was expected (14 cases)",
                  bad.Count == 0, string.Join("; ", bad));
        }

        private static void Identifiers(Type filter)
        {
            var rankOf = filter.GetMethod("RankOf", Any);
            Func<string, int> rank = s => (int)rankOf.Invoke(null, new object[] { s });
            var wrong = new List<string>();
            foreach (var pair in new[]
            {
                Tuple.Create("F1", 1), Tuple.Create("F70", 70), Tuple.Create("f177", 177), Tuple.Create("F500", 500),
                Tuple.Create("F0", 0), Tuple.Create("F007", 0), Tuple.Create("F-3", 0), Tuple.Create("F+3", 0),
                Tuple.Create("E4", 0), Tuple.Create("C1", 0), Tuple.Create("Fillet1", 0), Tuple.Create("F", 0),
                Tuple.Create("", 0), Tuple.Create("F1 ", 0), Tuple.Create("F1.5", 0),
            })
                if (rank(pair.Item1) != pair.Item2) wrong.Add(pair.Item1 + " is " + rank(pair.Item1) + ", not " + pair.Item2);
            Check("an identifier names a rank only in its canonical spelling (F + a positive number: no sign, no leading zero, no other letters)",
                  wrong.Count == 0, string.Join("; ", wrong));
        }

        private static void Choosing(Type filter)
        {
            // The stand of task 07, as the state ranks it: 40 faces of the frame that are
            // larger, then the walls of the feet tied at 4,800 (four feet, eight walls each,
            // all different bodies), then the small ones.
            const int Total = 177;
            var area = new double?[Total];
            var facing = new string[Total];
            var body = new int[Total];
            for (int i = 0; i < Total; i++)
            {
                area[i] = i < 40 ? 20000 - i * 100 : i < 72 ? 4800 : 300 - (i - 72);
                facing[i] = i % 6 == 0 ? "up" : i % 6 == 1 ? "down" : i % 6 == 2 ? "front" : i % 6 == 3 ? "back" : i % 6 == 4 ? "left" : "right";
                body[i] = i < 40 ? i % 5 : 5 + (i % 4);
            }
            int areasAsked = 0, facingsAsked = 0, distancesAsked = 0;
            Func<int, int> bodyOf = i => body[i];
            Func<int, double?> areaOf = i => { areasAsked++; return area[i]; };
            Func<int, string> facingOf = i => { facingsAsked++; return facing[i]; };
            Func<int, double?> distanceOf = i => { distancesAsked++; return i == 100 ? 0.4 : 30; };

            Func<Dictionary<string, object>, int, List<int>> choose = (raw, wantedBody) =>
            {
                string problem;
                var f = Parse(filter, raw, out problem);
                areasAsked = facingsAsked = distancesAsked = 0;
                var args = new object[] { Total, bodyOf, areaOf, facingOf, distanceOf, wantedBody, 0 };
                var list = (List<int>)filter.GetMethod("Choose").Invoke(f, args);
                lastMatched = (int)args[6];
                return list;
            };

            var walls = choose(new Dictionary<string, object> { ["area_min_mm2"] = 4799, ["area_max_mm2"] = 4801 }, -2);
            Check("a range of areas lists the faces of the stand beyond the 60 largest (the 32 walls, ranks 41 to 72), and counts them",
                  walls.Count == 32 && walls[0] == 40 && walls[31] == 71 && lastMatched == 32,
                  walls.Count + " listed, " + lastMatched + " matched");

            var cut = choose(new Dictionary<string, object> { ["area_min_mm2"] = 4799, ["area_max_mm2"] = 4801, ["limit"] = 10 }, -2);
            Check("beyond the limit the first (largest) ones are listed and the count says how many matched",
                  cut.Count == 10 && cut[9] == 49 && lastMatched == 32, cut.Count + " listed, " + lastMatched + " matched");

            var front = choose(new Dictionary<string, object> { ["area_min_mm2"] = 4799, ["area_max_mm2"] = 4801, ["normal"] = "front" }, -2);
            Check("the normal is asked only of the faces whose area already matched: an expensive read is not paid for a face that is out",
                  front.Count > 0 && front.All(i => facing[i] == "front" && i >= 40 && i < 72) && facingsAsked == 32,
                  front.Count + " listed, the normal asked of " + facingsAsked + " faces (32 expected)");

            var near = choose(new Dictionary<string, object> { ["near_mm"] = new List<object> { 0, 0, 0 }, ["within_mm"] = 1, ["area_max_mm2"] = 5000 }, -2);
            Check("a point is judged by the distance to the face, and asked last: only of the faces that the area and the direction left",
                  near.Count == 1 && near[0] == 100 && distancesAsked == 137,
                  near.Count + " listed, the distance asked of " + distancesAsked + " faces (137 expected: the ones the area left)");

            var oneBody = choose(new Dictionary<string, object> { ["area_min_mm2"] = 4799, ["area_max_mm2"] = 4801 }, 6);
            Check("a body keeps only its faces, and is judged first (its area is not even read for the other bodies)",
                  oneBody.Count == 8 && oneBody.All(i => body[i] == 6) && areasAsked == CountOf(body, 6),
                  oneBody.Count + " listed, the area asked of " + areasAsked + " faces, " + CountOf(body, 6) + " belong to the body");

            var nothing = choose(new Dictionary<string, object> { ["normal"] = "up", ["area_min_mm2"] = 1e6 }, -2);
            Check("a filter that matches nothing lists nothing and says 0, it does not fall back on the largest faces",
                  nothing.Count == 0 && lastMatched == 0, nothing.Count + " listed, " + lastMatched + " matched");

            var unread = new double?[] { null, 4800, null };
            string p2;
            var f2 = Parse(filter, new Dictionary<string, object> { ["area_max_mm2"] = 6000 }, out p2);
            var args2 = new object[] { 3, (Func<int, int>)(i => 0), (Func<int, double?>)(i => unread[i]), (Func<int, string>)(i => null), (Func<int, double?>)(i => null), -2, 0 };
            var list2 = (List<int>)filter.GetMethod("Choose").Invoke(f2, args2);
            Check("a face whose area could not be read never matches a range (it is not taken for zero)",
                  list2.Count == 1 && list2[0] == 1, string.Join(",", list2));
        }

        private static int lastMatched;

        private static int CountOf(int[] values, int v)
        {
            int n = 0;
            foreach (var x in values) if (x == v) n++;
            return n;
        }

        private static void Bodies(Type filter)
        {
            string problem;
            Func<string, int> index = text =>
            {
                var f = Parse(filter, new Dictionary<string, object> { ["body"] = text }, out problem);
                return (int)filter.GetMethod("BodyIndex").Invoke(f, new object[] { new List<string> { "TUBE(1)", "TUBE(2)", "Cap", "Gusset" } });
            };
            var none = Parse(filter, new Dictionary<string, object>(), out problem);
            Check("a body is named by its identifier or by its exact name; a name that exists nowhere is -1, no condition is -2",
                  index("C3") == 2 && index("c1") == 0 && index("gusset") == 3 && index("TUBE(2)") == 1 && index("C9") == -1 &&
                  index("C0") == -1 && index("C03") == -1 && index("Nothing") == -1 &&
                  (int)filter.GetMethod("BodyIndex").Invoke(none, new object[] { new List<string> { "a" } }) == -2,
                  index("C3") + " " + index("c1") + " " + index("gusset") + " " + index("TUBE(2)") + " " + index("C9") + " " + index("C0"));
        }

        // ── the sources ─────────────────────────────────────────────────────

        private static string Source(params string[] parts)
        {
            var path = Path.Combine(new[] { Root() }.Concat(parts).ToArray());
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private static void Wiring()
        {
            var state = Source("Oce.Solid.Agent.Core", "DocumentState.cs");
            Check("the listing numbers a face by its RANK among all the faces, not by a counter of the ones it listed",
                  state != null && Regex.IsMatch(state, @"var uid = ""F"" \+ \(rankIndex \+ 1\);") &&
                  !Regex.IsMatch(state, @"faceIndex\+\+") && state.Contains("foreach (var rankIndex in listedRanks)"),
                  state == null ? "DocumentState.cs not found" : "the loop over the listed faces still counts");

            Check("the state keeps the whole ranking and finds a face it did not list by its rank (TryGetSignature)",
                  state != null && state.Contains("public bool TryGetSignature(string uid, out EntitySignature signature)") &&
                  state.Contains("FaceFilter.RankOf(uid)") && state.Contains("docState._ranked = sorted;"),
                  "TryGetSignature or the ranking is missing");

            // The five places that find a face (or a face or edge) from its identifier ask the state by rank.
            var ask = new[]
            {
                Tuple.Create("SwOperations.cs", "TryGetSignature(uid, out var signature) || signature.Type != \"FACE\""),
                Tuple.Create("SwOperations.cs", "!docState.TryGetSignature(u, out signature) ||"),
                Tuple.Create("WeldmentOperations.cs", "!docState.TryGetSignature(u, out signature) || signature.Type != \"FACE\""),
                Tuple.Create("MeasureOperations.cs", "!docState.TryGetSignature(uid, out signature)"),
            };
            var missing = new List<string>();
            foreach (var a in ask)
            {
                var text = Source("Oce.Solid.Agent.Core", a.Item1);
                if (text == null || !text.Contains(a.Item2)) missing.Add(a.Item1 + ": «" + a.Item2 + "»");
            }
            var weld = Source("Oce.Solid.Agent.Core", "WeldmentOperations.cs");
            if (weld == null || Regex.Matches(weld, Regex.Escape("!docState.TryGetSignature(u, out signature) || signature.Type != \"FACE\"")).Count != 2)
                missing.Add("WeldmentOperations.cs: the two places (gusset, weld bead) are not both on TryGetSignature");
            Check("the places that find a face by its identifier (a face, a shell, a gusset, a weld bead, a measurement) ask the state by rank, not the dictionary alone",
                  missing.Count == 0, string.Join("; ", missing));

            var tools = Source("Tools", "McpTools.cs");
            Check("inspect_part offers «faces» and reads it before touching the document, and a rank the state did not list is not «not in the part I looked at»",
                  tools != null && tools.Contains("[\"faces\"] = new Dictionary<string, object>") &&
                  tools.Contains("FaceFilter.Parse(facesRaw, out facesProblem)") && tools.Contains("IsFaceBeyondTheList(uid, docState)") &&
                  tools.Contains("_session.StateWithFaces(facesWanted, out error)"),
                  "inspect_part, its argument or the check of the cited identifiers is not wired");
            Check("a reading with a filter keeps the signatures of the reading before when it is the same part (it must not unprotect F1..F60)",
                  tools != null && tools.Contains("WhatChanged(previousLight, _signatureSeen) == null"),
                  "the merge of the seen signatures is missing");
            Check("a filter the reading did not apply (an assembly, a drawing, a part without solid bodies) is refused, not answered with the ordinary state",
                  tools != null && tools.Contains("facesWanted != null && !docState.ContainsKey(\"faces_filter\")") &&
                  tools.Contains("«faces» was not applied"),
                  "inspect_part answers an unapplied filter with the ordinary state");

            var selectors = Source("Bench", "Expectations.cs");
            Check("the bench refuses a selector that finds ONE face on a reading of every face that was cut all the same (over 500): its twin may be beyond the cut",
                  selectors != null && selectors.Contains("string cutNote = family == \"faces\" ? FacesCutByFilter(docState) : null;") &&
                  selectors.Contains("if (cutNote != null && foundOnes.Count <= 1)"),
                  "Bench/Expectations.cs takes the one face it found on a cut list");

            var runner = Source("Bench", "tasks", "prerequisite.js");
            Check("the runner reads every face before it resolves a selector on a face, and refuses a bridge that does not answer the filter",
                  runner != null && runner.Contains("faces: { limit: EVERY_FACE }") && runner.Contains("hasFaceSelector(step.arguments)") &&
                  runner.Contains("the bridge did not answer the filter"),
                  "prerequisite.js does not ask for the faces");

            var ps = Source("Bench", "tasks", "run-autonomous.ps1");
            Check("the prerequisites are built by an explicit -PrerequisiteBridge, the bridge AFTER by default, for both sides of D9",
                  ps != null && ps.Contains("[string]   $PrerequisiteBridge = ''") &&
                  ps.Contains("--build $PrerequisiteBridge") && ps.Contains("--bridge', $PrerequisiteBridge") &&
                  !ps.Contains("--build $Bridge") && ps.Contains("prerequisite_bridge = $PrerequisiteBridge"),
                  "run-autonomous.ps1 still builds the prerequisites with the bridge under test");

            var readme = Source("Bench", "tasks", "README.md");
            Check("the README of the tasks says the starting part is a fixture, not a measure",
                  readme != null && readme.IndexOf("fixture, not a measure", StringComparison.OrdinalIgnoreCase) >= 0,
                  "Bench/tasks/README.md does not say it");
        }
    }
}
