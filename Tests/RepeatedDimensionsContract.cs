using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// ONE DIMENSION PER EXTENT ON THE WHOLE DRAWING (02/11/2026).
    ///
    /// The owner asked that no dimension be repeated in another view: it confuses the shop.
    /// His four drawings of 26M000002 held 7 dimensions too many in 51 (stile_quote.py 1.4.0 of
    /// the internal notes, its mode for repeated dimensions): on 26P000008 the 19 and the 11 in
    /// views 1 and 2, the 11 twice in view 2, the 1,5 of view 2 again in detail A, the 175 next
    /// to 19 + 145 + 11 and the 145 next to 1,5 + 134 + 9,5; on 26P000011 the 2,1 in views 2
    /// and 3. «between» put them all back, since it did not look at the other views, and the
    /// automatic route did not see a distance between two edges (note 98, defect 4).
    ///
    /// Here, without SOLIDWORKS: the tract of a distance between two edges, the chain that
    /// already gives an extent, the 13 dimensions along X of his 26P000008 replayed (5 refused,
    /// as the program counts), and the wiring on the sources, every rule first red on a
    /// changed copy. On library 0.189.0 every check but the wiring is red: ChainThrough does
    /// not exist.
    /// </summary>
    internal static class RepeatedDimensionsContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("one dimension per extent on the whole drawing: the tract of a distance, the chain that already gives it, the owner's 26P000008 replayed, the wiring");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of one dimension per extent", library + " is missing: build the library first (build.ps1)");
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
                // From the bytes: the repository may sit on a network drive, where LoadFrom is refused.
                var core = Assembly.Load(File.ReadAllBytes(library));
                sw = core.GetType("Oce.Solid.Agent.Core.SwOperations", false);
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (sw == null) { Check("SwOperations is in the library", false, "type missing"); return; }

            Tracts(sw);
            Chains(sw);
            OwnerReplay(sw);
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

        private static string Chain(Type sw, IEnumerable<string> keys, string key)
        {
            var found = (string[])Call(sw, "ChainThrough", keys, key);
            return found == null ? "null" : string.Join(" ", found);
        }

        // =================================================================
        // THE TRACT OF A DISTANCE BETWEEN TWO EDGES
        // =================================================================

        private static void Tracts(Type sw)
        {
            const string label = "the tract of a distance between two parallel edges: the axis the sheet measures across them and their two planes, the key of an edge of that tract; null for edges seen end-on, not parallel, or a turned view";
            try
            {
                var wrong = new List<string>();
                Func<int[], double[], double[], double[], double[], string> between =
                    (axes, a0, a1, b0, b1) => (string)Call(sw, "TractBetween", a0, a1, b0, b1, axes);
                var front = new[] { 0, 1 };   // X along the horizontal of the sheet, Y along the vertical
                var side = new[] { 2, 1 };
                Action<string, string, string> expect = (what, got, want) => { if (got != want) wrong.Add(what + ": " + (got ?? "null") + " instead of " + (want ?? "null")); };

                expect("the key of a tract", (string)Call(sw, "TractKey", 0, 0.0, 0.019), "X|0.00|19.00");
                expect("the lower first", (string)Call(sw, "TractKey", 2, 0.00873, 0.00723), "Z|7.23|8.73");
                // Two edges along Y, vertical on the front view, at X 0 and 19 (the second deeper):
                // a horizontal distance, the tract of X between them.
                expect("two vertical edges of the front view",
                       between(front, new[] { 0.0, 0, 0 }, new[] { 0.0, 0.00585, 0 }, new[] { 0.019, 0, 0.002 }, new[] { 0.019, 0.00585, 0.002 }),
                       "X|0.00|19.00");
                expect("two horizontal edges of the front view",
                       between(front, new[] { 0.0, 0, 0 }, new[] { 0.175, 0, 0 }, new[] { 0.0, 0.00585, 0 }, new[] { 0.175, 0.00585, 0 }),
                       "Y|0.00|5.85");
                expect("two vertical edges of the side view",
                       between(side, new[] { 0.0, 0, 0.00723 }, new[] { 0.0, 0.00585, 0.00723 }, new[] { 0.0, 0, 0.00873 }, new[] { 0.0, 0.00585, 0.00873 }),
                       "Z|7.23|8.73");
                expect("edges along the depth of the view",
                       between(front, new[] { 0.0, 0, 0 }, new[] { 0.0, 0, 0.01 }, new[] { 0.019, 0, 0 }, new[] { 0.019, 0, 0.01 }), null);
                expect("edges not parallel",
                       between(front, new[] { 0.0, 0, 0 }, new[] { 0.0, 0.00585, 0 }, new[] { 0.0, 0, 0 }, new[] { 0.175, 0, 0 }), null);
                expect("a turned view",
                       between(new[] { -1, -1 }, new[] { 0.0, 0, 0 }, new[] { 0.0, 0.00585, 0 }, new[] { 0.019, 0, 0 }, new[] { 0.019, 0.00585, 0 }), null);
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "2 keys, 3 tracts, 3 nulls" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE CHAIN THAT ALREADY GIVES AN EXTENT
        // =================================================================

        private static void Chains(Type sw)
        {
            const string label = "what already gives an extent: the same tract, a chain that joins its two planes (175 by 19 + 145 + 11, 164 by 175 - 11), nothing on another axis or for a hole, and the answer names the dimensions";
            try
            {
                var wrong = new List<string>();
                var keys = new[] { "X|0.00|19.00", "X|19.00|164.00", "X|164.00|175.00", "Y|0.00|175.00", "hole|8.00", "chamfer|Chamfer1" };
                Action<string, string, string> expect = (what, got, want) => { if (got != want) wrong.Add(what + ": " + got + " instead of " + want); };
                expect("the chain of 175", Chain(sw, keys, "X|0.00|175.00"), "X|0.00|19.00 X|19.00|164.00 X|164.00|175.00");
                expect("the same 19", Chain(sw, keys, "X|0.00|19.00"), "X|0.00|19.00");
                expect("a step not given", Chain(sw, keys, "X|10.90|15.90"), "null");
                expect("another axis", Chain(sw, keys, "Y|0.00|19.00"), "null");
                expect("a hole", Chain(sw, keys, "hole|8.00"), "null");
                expect("a difference", Chain(sw, new[] { "X|0.00|175.00", "X|164.00|175.00" }, "X|0.00|164.00"), "X|0.00|175.00 X|164.00|175.00");
                var owners = new Dictionary<string, string> { { "X|0.00|19.00", "«19» (RD2) in the view «V1»" } };
                var same = (string)Call(sw, "RepeatedWhy", "X|0.00|19.00", new[] { "X|0.00|19.00" }, owners);
                if (same == null || !same.Contains("already dimensioned") || !same.Contains("«19» (RD2) in the view «V1»"))
                    wrong.Add("the same extent is said as «" + same + "»");
                var closed = (string)Call(sw, "RepeatedWhy", "X|0.00|175.00", new[] { "X|0.00|19.00", "X|19.00|164.00" }, owners);
                if (closed == null || !closed.Contains("closes a chain") || !closed.Contains("«19» (RD2)") || !closed.Contains("X|19.00|164.00"))
                    wrong.Add("a chain is said as «" + closed + "»");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "6 questions, 2 answers in words" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // HIS 26P000008 REPLAYED
        // =================================================================

        /// <summary>
        /// The 13 dimensions along X of the owner's 26P000008 (tract from the left end, mm, from
        /// the edges each dimension rests on), view 1 then view 2 in the order of their names RDn.
        /// </summary>
        private static readonly string[][] OwnerAlongX =
        {
            new[] { "145 view 1", "X|19.00|164.00" }, new[] { "19 view 1", "X|0.00|19.00" }, new[] { "11 view 1", "X|164.00|175.00" },
            new[] { "134 view 2", "X|20.50|154.50" }, new[] { "9,5 view 2", "X|154.50|164.00" }, new[] { "11 view 2 below", "X|164.00|175.00" },
            new[] { "175 view 2", "X|0.00|175.00" }, new[] { "1,5 view 2", "X|19.00|20.50" }, new[] { "19 view 2", "X|0.00|19.00" },
            new[] { "10,9 view 2", "X|0.00|10.90" }, new[] { "5 view 2", "X|10.90|15.90" }, new[] { "11 view 2 above", "X|164.00|175.00" },
            new[] { "5 view 2 right", "X|159.00|164.00" },
        };

        private static void OwnerReplay(Type sw)
        {
            const string label = "the owner's 26P000008 replayed along X: of his 13 dimensions 5 are refused (the program of the internal notes counts 5 too many: 13 dimensions, 9 planes, 1 group), the 8 kept join all 9 planes";
            try
            {
                var placed = new List<string>();
                var refused = new List<string>();
                foreach (var d in OwnerAlongX)
                {
                    var chain = (string[])Call(sw, "ChainThrough", placed, d[1]);
                    if (chain != null) refused.Add(d[0]); else placed.Add(d[1]);
                }
                var planes = new HashSet<string>();
                foreach (var k in placed) { var p = k.Split('|'); planes.Add(p[1]); planes.Add(p[2]); }
                bool ok = refused.Count == 5 && placed.Count == 8 && planes.Count == 9;
                Check(label, ok, "refused " + refused.Count + " (" + string.Join(", ", refused) + "), kept " + placed.Count + " on " + planes.Count + " planes");
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING, ON THE SOURCES
        // =================================================================

        private const string ReadExisting = "var already = new HashSet<string>(KeysAlreadyDimensioned(drawing, view, mu, out existingRead, owners), StringComparer.Ordinal);";
        private const string Allowed = "bool repeatAllowed = repeat || _dimensionParentheses;";
        private const string Tract = "TractBetween(first.FromModel, first.ToModel, second.FromModel, second.ToModel, axes);";
        private const string BetweenChain = "string[] chain = repeatAllowed ? null : ChainThrough(already, tract);";
        private const string Kept = "already.Add(tract);";
        private const string BetweenSaid = "[\"already_given_refused\"] = refused.ToArray(),";
        private const string FirstBorn = "Dictionary<string, object> placed = PlaceBetween(model, drawing, edges, first, second, true,";
        private const string RepeatArgument = "bool repeat = Bool(a, \"repeat\", false);";
        private const string TwoEdges = "if (straight.Count == 2 && ents.Length == 2 && attachedCircles.Count == 0 && !onGeneratrix)";
        private const string TwoEdgesKey = "var k = TractBetween(p0.StartPoint as double[], p0.EndPoint as double[],";
        private const string AutoChain = "string[] chain = repeat || s.InModel == null ? null : ChainThrough(already, s.InModel);";
        private const string AutoSeen = "seen[s.Key] = i;";
        private const string AutoSaid = "[\"closing_a_chain\"] = closing.ToArray(),";

        private static readonly string[][] BetweenLines =
        {
            new[] { ReadExisting, "«between» does not read the dimensions the drawing already has" },
            new[] { Allowed, "«repeat» or «parentheses» do not let a repeated extent through" },
            new[] { Tract, "the tract of a pair is not measured" },
            new[] { BetweenChain, "a pair is not compared with what the drawing already gives" },
            new[] { Kept, "a pair of the call is not counted for the next ones" },
            new[] { BetweenSaid, "the answer does not say the pairs refused" },
        };

        private static readonly string[][] DimensionLines =
        {
            new[] { RepeatArgument, "«repeat» is not read" },
            new[] { TwoEdges, "a distance between two edges is not recorded as already dimensioned" },
            new[] { TwoEdgesKey, "a distance between two edges is not turned into its tract" },
            new[] { AutoChain, "the automatic route does not leave out an edge that closes a chain" },
            new[] { AutoSaid, "the answer does not say the edges that close a chain" },
        };

        private static List<string> WiringProblems(string between, string dimension)
        {
            var wrong = new List<string>();
            if (between == null) wrong.Add("DrawingBetweenDimensions.cs not found");
            else
            {
                foreach (var line in BetweenLines)
                    if (!between.Contains(line[0])) wrong.Add(line[1]);
                int c = between.IndexOf(BetweenChain, StringComparison.Ordinal), b = between.IndexOf(FirstBorn, StringComparison.Ordinal);
                if (c < 0 || b < c) wrong.Add("a pair is compared after a dimension is placed: a refusal would leave the drawing half done");
            }
            if (dimension == null) wrong.Add("DrawingDimensionOperations.cs not found");
            else
            {
                foreach (var line in DimensionLines)
                    if (!dimension.Contains(line[0])) wrong.Add(line[1]);
                int c = dimension.IndexOf(AutoChain, StringComparison.Ordinal), s = dimension.IndexOf(AutoSeen, StringComparison.Ordinal);
                if (c < 0 || s < c) wrong.Add("the automatic route keeps an edge before asking whether it closes a chain");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: «between» reads what the drawing has, measures each tract and compares it before placing anything, counts its own pairs, lets «repeat» and «parentheses» through, says what it refused; the drawing records a distance between two edges; the automatic route leaves out an edge that closes a chain and says it";
            var core = Path.Combine(Root(), "Oce.Solid.Agent.Core");
            string between = File.Exists(Path.Combine(core, "DrawingBetweenDimensions.cs")) ? File.ReadAllText(Path.Combine(core, "DrawingBetweenDimensions.cs")) : null;
            string dimension = File.Exists(Path.Combine(core, "DrawingDimensionOperations.cs")) ? File.ReadAllText(Path.Combine(core, "DrawingDimensionOperations.cs")) : null;
            var real = WiringProblems(between, dimension);
            var blind = new List<string>();
            int copies = 0;
            Action<string, bool, Func<string, string>> mutate = (what, inBetween, change) =>
            {
                string source = inBetween ? between : dimension;
                if (source == null) return;
                var changed = change(source);
                if (changed == source) { blind.Add(what + " (the copy did not change)"); return; }
                copies++;
                if (WiringProblems(inBetween ? changed : between, inBetween ? dimension : changed).Count == 0) blind.Add(what);
            };
            foreach (var line in BetweenLines)
            {
                var anchor = line[0];
                mutate("without «" + anchor + "»", true, t => t.Replace(anchor, "/* gone */"));
            }
            foreach (var line in DimensionLines)
            {
                var anchor = line[0];
                mutate("without «" + anchor + "»", false, t => t.Replace(anchor, "/* gone */"));
            }
            mutate("a pair compared after the first dimension", true, t => t.Replace(BetweenChain, "/* moved */").Replace(FirstBorn, FirstBorn + "\n" + BetweenChain));
            mutate("an edge kept before the chain is asked", false, t => t.Replace(AutoChain, "/* moved */").Replace(AutoSeen, AutoSeen + "\n" + AutoChain));
            bool ok = real.Count == 0 && blind.Count == 0;
            Check(label, ok, real.Count > 0 ? string.Join("; ", real)
                           : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                           : (BetweenLines.Length + DimensionLines.Length) + " lines, two orders; " + copies + " changed copies all red");
        }
    }
}
