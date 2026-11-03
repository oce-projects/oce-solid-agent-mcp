using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 22 OF REPORT 8CD843 (28/10/2026), WHERE THE BENCH CANNOT
    /// REACH.
    ///
    /// The drawing of 26P000007 made with bridge 0.152.10, set against the one the owner
    /// corrected: the distance between two edges («between»), the row, the side, a text
    /// on the dimension, the parentheses, the arrows where the text is, the dimensions of a
    /// detail above it, the holes and chamfers left to another view, the scale of a
    /// section, the style of the detail circle, the balloons again in another view, the
    /// bill of materials over the title block, and a dimension moved.
    ///
    /// Two halves, as Zc2203: the decisions by reflection, and the wiring on the
    /// sources, every rule first red on a changed copy. On library 0.178.1 before
    /// round 22 every check is red.
    /// </summary>
    internal static class Zc2204DrawingRound22Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 22 of report 8cd843: the distance between two edges, the row and the side, the text and the parentheses, the arrows where the text is, the scale of a section, the detail circle, the balloons again, the bill of materials over the title block, a dimension moved, a finished drawing read");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 22 fixes",
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

            Pairs(sw);
            Parallel(sw);
            Layout(sw);
            Decoration(sw);
            BelowAndDirection(sw);
            Unalign(sw);
            BalloonPick(sw);
            ChamferRules(sw);
            Arrows(sw);
            DetailCircle(sw);
            Balloons(sw);
            TitleBlock(sw);
            Move(sw);
            Read(sw);
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

        private static void Pairs(Type sw)
        {
            const string label = "point 1: «between» read as pairs of edge numbers, every other writing refused with its reason (one number, three, the same edge twice, a pair asked twice, an edge the view has not, a fraction, a text, more than 40 pairs)";
            try
            {
                var wrong = new List<string>();
                Func<object, int, List<int[]>> parse = (raw, available) =>
                {
                    var args = new object[] { raw, available, null };
                    var m = ReflectionLog.Global.Method(sw, "BetweenPairsAsked", Any);
                    if (m == null) throw new MissingMethodException("SwOperations.BetweenPairsAsked");
                    var r = m.Invoke(null, args) as List<int[]>;
                    if (r == null && string.IsNullOrEmpty(args[2] as string)) wrong.Add("a refusal with no reason");
                    return r;
                };
                var two = parse(new object[] { new object[] { 3, 7 }, new object[] { 7, 12 } }, 20);
                if (two == null || two.Count != 2 || two[0][0] != 3 || two[0][1] != 7 || two[1][1] != 12)
                    wrong.Add("[[3, 7], [7, 12]] read as " + (two == null ? "null" : two.Count + " pairs"));
                var asDouble = parse(new object[] { new object[] { 0.0, 4.0 } }, -1);
                if (asDouble == null || asDouble[0][1] != 4) wrong.Add("[[0.0, 4.0]] with the range unknown not read");
                var many = new List<object>();
                for (int i = 0; i < 41; i++) many.Add(new object[] { i, i + 1 });
                var bad = new Dictionary<string, object>
                {
                    ["one number"] = new object[] { new object[] { 3 } },
                    ["three numbers"] = new object[] { new object[] { 1, 2, 3 } },
                    ["the same edge twice"] = new object[] { new object[] { 2, 2 } },
                    ["a pair asked twice, turned"] = new object[] { new object[] { 1, 2 }, new object[] { 2, 1 } },
                    ["an edge beyond the view"] = new object[] { new object[] { 1, 20 } },
                    ["a fraction"] = new object[] { new object[] { 1, 2.5 } },
                    ["a negative number"] = new object[] { new object[] { -1, 2 } },
                    ["a text"] = "1,2",
                    ["no pair"] = new object[0],
                    ["a pair that is a text"] = new object[] { "1-2" },
                    ["41 pairs"] = many.ToArray(),
                };
                foreach (KeyValuePair<string, object> b in bad)
                    if (parse(b.Value, 60) != null && b.Key != "an edge beyond the view") wrong.Add(b.Key + " accepted");
                if (parse(new object[] { new object[] { 1, 20 } }, 20) != null) wrong.Add("the edge 20 of a view of 20 accepted");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "2 writings read, 11 refused with a reason" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void Parallel(Type sw)
        {
            const string label = "point 1: two vertical or two horizontal straight edges have a distance; a curve, an oblique edge, a vertical with a horizontal, two edges on one line have none, each with its reason";
            try
            {
                var wrong = new List<string>();
                Func<string, bool, string, bool, double, string> judge = (facingA, straightA, facingB, straightB, gap) =>
                    (string)Call(sw, "BetweenProblem", facingA, straightA, facingB, straightB, gap);
                if (judge("vertical", true, "vertical", true, 80) != null) wrong.Add("two verticals 80 apart refused");
                if (judge("horizontal", true, "horizontal", true, 10) != null) wrong.Add("two horizontals 10 apart refused");
                foreach (var no in new[]
                {
                    new object[] { "vertical", false, "vertical", true, 5.0, "not straight" },
                    new object[] { "oblique", true, "oblique", true, 5.0, "oblique" },
                    new object[] { "vertical", true, "horizontal", true, 5.0, "not parallel" },
                    new object[] { "horizontal", true, "horizontal", true, 0.0, "same line" },
                })
                {
                    var r = judge((string)no[0], (bool)no[1], (string)no[2], (bool)no[3], (double)no[4]);
                    if (r == null || !r.Contains((string)no[5])) wrong.Add(no[5] + ": " + (r ?? "null"));
                }
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "2 distances, 4 refusals" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void Layout(Type sw)
        {
            const string label = "points 2, 7: «horizontal_side» below or above, «vertical_side» right or left, the default when left out, every other word refused with its reason";
            try
            {
                var wrong = new List<string>();
                Func<object, string> side = value =>
                {
                    var a = value == null ? new Dictionary<string, object>() : new Dictionary<string, object> { ["horizontal_side"] = value };
                    var args = new object[] { a, "horizontal_side", "below", "above", null };
                    var r = ReflectionLog.Global.Method(sw, "SideAsked", Any).Invoke(null, args) as string;
                    if (r == null && string.IsNullOrEmpty(args[4] as string)) wrong.Add("refused with no reason");
                    return r;
                };
                if (side(null) != "below") wrong.Add("left out: " + side(null));
                if (side("above") != "above") wrong.Add("above: " + side("above"));
                if (side(" Above ") != "above") wrong.Add("« Above »: " + side(" Above "));
                if (side("left") != null) wrong.Add("left taken for a horizontal side");
                if (side("over") != null) wrong.Add("a word it does not take");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "5 requests" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void Decoration(Type sw)
        {
            const string label = "points 5, 6: «prefix» and «suffix» one line of at most 60 characters, «parentheses» true or false; what did not stick read back and said, with the value; «(35)» prints the number 35";
            try
            {
                var wrong = new List<string>();
                if (Call(sw, "DecorationProblem", " CHANNEL", null, true) != null) wrong.Add("a good suffix refused");
                if (Call(sw, "DecorationProblem", null, null, null) != null) wrong.Add("nothing asked refused");
                if (Call(sw, "DecorationProblem", "A\nB", null, null) == null) wrong.Add("two lines accepted");
                if (Call(sw, "DecorationProblem", null, new string('X', 61), null) == null) wrong.Add("61 characters accepted");
                if (Call(sw, "DecorationProblem", 5, null, null) == null) wrong.Add("a number for a text accepted");
                if (Call(sw, "DecorationProblem", null, null, "yes") == null) wrong.Add("«yes» for parentheses accepted");
                if (Call(sw, "DecorationNote", "4x", "4x", " CHANNEL", "CHANNEL", true, (bool?)true) != null)
                    wrong.Add("everything kept said lost");
                var lost = Call(sw, "DecorationNote", null, null, " CHANNEL", "", true, (bool?)false) as string;
                if (lost == null || !lost.Contains("the suffix reads") || !lost.Contains("the parentheses did not stick"))
                    wrong.Add("the suffix and the parentheses lost: " + (lost ?? "null"));
                var unread = Call(sw, "DecorationNote", null, null, null, null, true, null) as string;
                if (unread == null || !unread.Contains("cannot be read back")) wrong.Add("the parentheses unread: " + (unread ?? "null"));
                var rows = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object> { ["mm"] = 2.5, ["decoration_note"] = "the suffix reads «» instead of « CHANNEL»: write it by hand on the sheet" },
                    new Dictionary<string, object> { ["mm"] = 80.0 },
                };
                var notKept = Call(sw, "DecorationsNotKept", rows) as List<string>;
                if (notKept == null || notKept.Count != 1 || !notKept[0].StartsWith("2.5 mm: ")) wrong.Add("the rows not kept: " + (notKept == null ? "null" : string.Join("|", notKept)));
                var printed = Call(sw, "PrintedNumber", "(35)") as double?;
                if (printed != 35) wrong.Add("«(35)» prints " + (printed == null ? "null" : printed.ToString()));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "6 requests, 3 readings back, the rows, the parentheses printed" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        /// <summary>
        /// Note 98, defects 2 and 3 (02/11/2026): the owner writes «CHANNEL» under the 3,4, not after
        /// it, and read_drawing could not say which way a «linear» distance between two edges
        /// measures. On library 0.191.0 both functions are missing.
        /// </summary>
        private static void BelowAndDirection(Type sw)
        {
            const string label = "note 98: «below» one line of at most 60 characters, what did not stick said; a distance between two edges reads its direction from them";
            try
            {
                var wrong = new List<string>();
                if (Call(sw, "BelowProblem", "CHANNEL") != null) wrong.Add("a good text refused");
                if (Call(sw, "BelowProblem", new object[] { null }) != null) wrong.Add("nothing asked refused");
                if (Call(sw, "BelowProblem", "A\nB") == null) wrong.Add("two lines accepted");
                if (Call(sw, "BelowProblem", "   ") == null) wrong.Add("an empty text accepted");
                if (Call(sw, "BelowProblem", new string('X', 61)) == null) wrong.Add("61 characters accepted");
                if (Call(sw, "BelowProblem", 5) == null) wrong.Add("a number for a text accepted");
                if (Call(sw, "BelowNote", "CHANNEL", "CHANNEL ") != null) wrong.Add("a text kept said lost");
                if (Call(sw, "BelowNote", null, null) != null) wrong.Add("nothing asked said lost");
                var lost = Call(sw, "BelowNote", "CHANNEL", null) as string;
                if (lost == null || !lost.Contains("«nothing» instead of «CHANNEL»")) wrong.Add("a text lost: " + (lost ?? "null"));
                Func<string, string, string, string> direction = (kind, a, b) => (string)Call(sw, "DimensionDirection", kind, a, b);
                if (direction("linear", "vertical", "vertical") != "horizontal") wrong.Add("two vertical edges not horizontal");
                if (direction("linear", "horizontal", "horizontal") != "vertical") wrong.Add("two horizontal edges not vertical");
                if (direction("linear", "vertical", "horizontal") != null) wrong.Add("mixed edges given a direction");
                if (direction("linear", "oblique", "oblique") != null) wrong.Add("oblique edges given a direction");
                if (direction("linear", null, null) != null) wrong.Add("edges not read given a direction");
                if (direction("vertical", null, null) != "vertical") wrong.Add("a vertical dimension not vertical");
                if (direction("diameter", "vertical", "vertical") != null) wrong.Add("a diameter given a direction");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "6 texts, 3 readings back, 7 directions" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void Unalign(Type sw)
        {
            const string label = "note 98, defect 5: «unalign» takes off its line only a view aligned to its parent (swViewAligned 2, swViewAlignBoth 3), not a view that only has views aligned to it (1) or none (0)";
            try
            {
                var wrong = new List<string>();
                var expected = new[] { false, false, false, true, true };
                for (int i = -1; i <= 3; i++)
                    if ((bool)Call(sw, "AlignedToItsParent", i) != expected[i + 1]) wrong.Add(i + " read wrong");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "5 values" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void ChamferRules(Type sw)
        {
            const string label = "note 98, defect 1: «chamfer» takes an oblique edge alone or with its leg and refuses what is not that; a chamfer drawn by the profile has one key whichever way its edge is read, and those of the same length one; the text of a chamfer dimension goes on the side the corner was taken from, by either leg; a view whose line is not the profile of the chamfer (26P000011 view 1: 0.415 mm for 0.566) does not show it, whatever edge lies on the profile (26P000009 view 3: a mitre of 0.693 mm); a leg that runs the same way as the chamfer edge reads 180° minus its angle (8 legs probed on SOLIDWORKS); the text tried at 15 places on the side of the corner";
            try
            {
                var wrong = new List<string>();
                Func<object, int, List<int[]>> asked = (raw, available) => (List<int[]>)Call(sw, "ChamferEdgesAsked", raw, available, null);
                var good = asked(new object[] { 5L, new object[] { 7L, 12L } }, 20);
                if (good == null || good.Count != 2 || good[0].Length != 1 || good[0][0] != 5 || good[1].Length != 2 || good[1][1] != 12)
                    wrong.Add("[5, [7, 12]] read as " + (good == null ? "nothing" : string.Join(" ", good.Select(g => "[" + string.Join(",", g) + "]"))));
                if (asked("5", 20) != null) wrong.Add("a text taken");
                if (asked(new object[] { new object[] { 7L, 7L } }, 20) != null) wrong.Add("an edge named twice taken");
                if (asked(new object[] { 25L }, 20) != null) wrong.Add("an edge the view does not have taken");
                if (asked(new object[] { new object[] { 1L, 2L, 3L } }, 20) != null) wrong.Add("three numbers taken");
                if (asked(new object[0], 20) != null) wrong.Add("an empty list taken");
                if (asked(new object[] { 2.5 }, 20) != null) wrong.Add("2.5 taken as an edge");
                Func<double[], double[], string> key = (p, q) => (string)Call(sw, "ChamferEndsKey", p, q);
                var a = new[] { 0.001, 0.002, 0.003 };
                var b = new[] { 0.0015, 0.0025, 0.003 };
                if (key(a, b) == null || key(a, b) != key(b, a)) wrong.Add("the key changes with the way the edge is read");
                if (key(a, b) == key(a, new[] { 0.00152, 0.0025, 0.003 })) wrong.Add("an end 0.02 mm away gives the same key");
                // A corner at the origin cut by a chamfer from (2, 0) to (0, 2) mm: the part lies up and right.
                Func<double[], double[]> place = leg => (double[])Call(sw, "ChamferTextPlace", new[] { 0.002, 0.0, 0.0, 0.002 }, leg, 0.01);
                var byBottom = place(new[] { 0.002, 0.0, 0.05, 0.0 });
                var byLeft = place(new[] { 0.0, 0.03, 0.0, 0.002 });
                double expected = 0.001 - 0.01 / Math.Sqrt(2);
                if (byBottom == null || Math.Abs(byBottom[0] - expected) > 1e-9 || Math.Abs(byBottom[1] - expected) > 1e-9)
                    wrong.Add("by the bottom leg the text at " + (byBottom == null ? "nothing" : byBottom[0] * 1000 + ", " + byBottom[1] * 1000 + " mm"));
                if (byLeft == null || Math.Abs(byLeft[0] - expected) > 1e-9 || Math.Abs(byLeft[1] - expected) > 1e-9)
                    wrong.Add("by the left leg the text at " + (byLeft == null ? "nothing" : byLeft[0] * 1000 + ", " + byLeft[1] * 1000 + " mm"));
                if (place(new[] { 0.01, 0.01, 0.05, 0.01 }) != null) wrong.Add("a leg that does not meet the chamfer gives a place");
                // 26P000008 detail A: 0.693 mm in the model, 0.567 on the sheet; 26P000007 detail A in true size.
                Func<double, double, string> slant = (model, sheet) => (string)Call(sw, "ChamferSlant", model, sheet, 1);
                if (slant(0.000693, 0.000567) == null) wrong.Add("the edge of 26P000008 detail A taken as parallel to the sheet");
                if (slant(0.000415, 0.000576) == null) wrong.Add("an edge longer on the sheet than in the model taken as parallel");
                if (slant(0.000566, 0.000566) != null) wrong.Add("an edge in true size refused");
                if (slant(0.000566, 0.000569) != null) wrong.Add("0.003 mm off refused");
                // 26P000009 view 3: a mitre edge of 0.693 mm on the 0.566 mm profile of a 0.4 x 45°, a true
                // dimension with 0.169.0; 26P000011 view 1: 0.415 mm on the sheet.
                var square = new List<double[]> { new[] { 0.4, 0.4 } };
                Func<List<double[]>, double, string> seen = (pairs, sheet) => (string)Call(sw, "ChamferProfileNotSeen", "Chamfer1", "0.4 x 45°", pairs, sheet, 4);
                if (seen(square, 0.566) != null) wrong.Add("the 0.566 mm profile of 26P000009 view 3 refused");
                if (seen(square, 0.567) != null) wrong.Add("the 0.567 mm profile of 26P000008 detail A refused");
                if (seen(square, 0.415) == null) wrong.Add("the 0.415 mm of 26P000011 view 1 taken as the profile of a 0.4 x 45°");
                if (seen(new List<double[]> { new[] { 0.5, 0.5 * Math.Tan(Math.PI / 3) }, new[] { 0.5, 0.5 / Math.Tan(Math.PI / 3) } }, 0.577) != null)
                    wrong.Add("a 0.5 x 60° seen with the angle on the other face refused");
                // The legs probed on 02/11/2026 (sheet mm): against the chamfer 0,4 x 45°, with it 135°.
                Func<double[], double[], bool> runsWith = (ch, leg) => (bool)Call(sw, "ChamferLegRunsWith", ch, leg);
                var detailA = new[] { 354.08, 224.13, 352.08, 222.12 };
                var view1 = new[] { 118, 172.2, 117.2, 171.4 };
                var probed = new[]
                {
                    new object[] { "26P000008 detail A leg 0", detailA, new[] { 327.23, 224.13, 354.08, 224.13 }, false },
                    new object[] { "26P000008 detail A leg 16", detailA, new[] { 354.08, 224.13, 364.03, 224.13 }, false },
                    new object[] { "26P000008 detail A leg 2", detailA, new[] { 352.08, 222.12, 329.23, 222.12 }, true },
                    new object[] { "26P000008 detail A leg 19", detailA, new[] { 362.03, 222.12, 352.08, 222.12 }, true },
                    new object[] { "26P000008 detail A leg 6", detailA, new[] { 354.08, 251.38, 354.08, 224.13 }, true },
                    new object[] { "26P000011 view 1 leg 23", view1, new[] { 118, 217.4, 118, 172.2 }, true },
                    new object[] { "26P000011 view 1 leg 24", view1, new[] { 116.3, 172.2, 118, 172.2 }, false },
                    new object[] { "26P000011 view 1 leg 26", view1, new[] { 117.2, 171.4, 116.08, 171.4 }, true },
                };
                // The places of the text (library 0.198.6): 49, the nearest first along «away», none farther
                // than 90° from it (past the side of the corner it is over the part), the 90° ones there, 6 to
                // 24 mm, the distance never shrinking, each with the unit toward it.
                var candidates = (System.Collections.IList)Call(sw, "ChamferTextCandidates", new[] { 0.1, 0.1 }, new[] { -1.0, 0.0 });
                var firstPlace = candidates == null || candidates.Count == 0 ? null : (double[])candidates[0];
                if (candidates == null || candidates.Count != 49) wrong.Add("the text of a chamfer dimension has " + (candidates == null ? "no" : candidates.Count.ToString()) + " places, not 49");
                else if (Math.Abs(firstPlace[0] - 0.094) > 1e-9 || Math.Abs(firstPlace[1] - 0.1) > 1e-9) wrong.Add("the first place of the text is not 6 mm along «away»");
                else
                {
                    double before = 0; bool across = false;
                    foreach (double[] candidate in candidates)
                    {
                        double ox = candidate[0] - 0.1, oy = candidate[1] - 0.1, ol = Math.Sqrt(ox * ox + oy * oy);
                        if (candidate.Length < 5 || Math.Abs(ol - candidate[2]) > 1e-9 || -ox / ol < -1e-9 || candidate[2] < 0.006 - 1e-9 || candidate[2] > 0.024 + 1e-9 ||
                            candidate[2] < before - 1e-12 || Math.Abs(candidate[3] - ox / ol) > 1e-9 || Math.Abs(candidate[4] - oy / ol) > 1e-9)
                            { wrong.Add("a place of the text outside 6 to 24 mm and 90° of «away», nearer after farther, or without its direction"); break; }
                        before = candidate[2];
                        if (Math.Abs(ox) < 1e-9) across = true;
                    }
                    if (!across) wrong.Add("no place of the text at 90° of «away», along the side of the corner");
                }
                // The leader of the text (library 0.198.6): crossings counted, its first 0,6 mm left out (it starts on
                // its own edge), touching at an end not counted; it meets the text at the nearest point of the box.
                var leaderLines = new List<double[]> { new[] { 0.0, 0.005, 0.02, 0.005 }, new[] { 0.0, 0.0003, 0.02, 0.0003 }, new[] { 0.015, 0.0, 0.015, 0.02 }, new[] { 0.0, 0.01, 0.02, 0.01 } };
                int crossings = (int)Call(sw, "LeaderCrossings", new[] { 0.0, 0.0 }, new[] { 0.01, 0.01 }, leaderLines, 0.0006);
                if (crossings != 1) wrong.Add("a leader from 0;0 to 10;10 mm crosses " + crossings + " lines, not 1 (the line at y 5; the one at 0,3 mm is in the part left out; the one at x 15 is beyond; the one at y 10 only touches its end)");
                var meet = (double[])Call(sw, "NearestOnBox", new[] { 0.0, 0.0 }, 0.004, -0.002, 0.03, 0.003);
                if (meet == null || Math.Abs(meet[0] - 0.004) > 1e-12 || Math.Abs(meet[1]) > 1e-12) wrong.Add("the leader does not meet the box at its nearest point");
                foreach (var p in probed)
                    if (runsWith((double[])p[1], (double[])p[2]) != (bool)p[3])
                        wrong.Add(p[0] + ((bool)p[3] ? " (135° on SOLIDWORKS) taken as running against the chamfer" : " (0,4 x 45° on SOLIDWORKS) taken as running with the chamfer"));
                Func<double, string> profile = l => (string)Call(sw, "ChamferProfileKey", l);
                if (profile(0.000986) != profile(0.0009864) || profile(0.000986) == profile(0.000988)) wrong.Add("the profile key does not tell 0.986 from 0.988 mm, or tells 0.986 from 0.9864");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "7 entries, 2 keys, 3 places, 4 slants, 4 profiles, 1 profile key, 8 legs probed, 49 places of the text, 1 leader crossing, 1 meeting point" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void BalloonPick(Type sw)
        {
            const string label = "note 98, defect 7: a balloon again takes the part of a section (no configuration) as the part of the model, not another file; a line is cut to the view, a line outside it gives nothing; the number of a part from the other views; three points a line, the middles first";
            try
            {
                var wrong = new List<string>();
                Func<string, string, bool> same = (a, b) => (bool)Call(sw, "SameBomPart", a, b);
                if (!same("c:\\p\\26p000009.sldprt|", "c:\\p\\26p000009.sldprt|Default")) wrong.Add("the section part and the model part told apart");
                if (!same("c:\\p\\26p000009.sldprt|A", "c:\\p\\26p000009.sldprt|A")) wrong.Add("the same key told apart");
                if (same("c:\\p\\26p000009.sldprt|A", "c:\\p\\26p000009.sldprt|B")) wrong.Add("two configurations taken as one");
                if (same("c:\\p\\26p000009.sldprt|", "c:\\p\\26p000007.sldprt|")) wrong.Add("two files taken as one");
                if (same(null, "c:\\p\\26p000009.sldprt|")) wrong.Add("nothing picked taken as the part");
                if (same("|", "|")) wrong.Add("two components without a file taken as one");
                Func<double[], double[]> clip = s => (double[])Call(sw, "ClippedToOutline", s, new[] { 0.0, 0.0, 10.0, 10.0 });
                var cut = clip(new[] { -5.0, 5.0, 15.0, 5.0 });
                if (cut == null || Math.Abs(cut[0]) > 1e-9 || Math.Abs(cut[2] - 10) > 1e-9) wrong.Add("a line across the view not cut at its sides");
                var whole = clip(new[] { 2.0, 2.0, 8.0, 3.0 });
                if (whole == null || Math.Abs(whole[0] - 2) > 1e-9 || Math.Abs(whole[3] - 3) > 1e-9) wrong.Add("a line inside the view changed");
                if (clip(new[] { -5.0, 12.0, 15.0, 12.0 }) != null) wrong.Add("a line outside the view kept");
                if (clip(new[] { 12.0, 0.0, 12.0, 10.0 }) != null) wrong.Add("a vertical line outside the view kept");
                var numbers = new List<string[]>
                {
                    new[] { "c:\\p\\26p000007.sldprt|Default", "1" },
                    new[] { "c:\\p\\26p000009.sldprt|Default", "3" },
                };
                if ((string)Call(sw, "ExpectedNumber", numbers, "c:\\p\\26p000009.sldprt|") != "3") wrong.Add("the number of the section part not found");
                if (Call(sw, "ExpectedNumber", numbers, "c:\\p\\26p000011.sldprt|") != null) wrong.Add("a part without a balloon given a number");
                var tries = (List<double[]>)Call(sw, "PointsToTry", new List<double[]> { new[] { 0.0, 0.0, 4.0, 0.0 }, new[] { 0.0, 0.0, 0.0, 8.0 } }, 8);
                if (tries.Count != 6 || Math.Abs(tries[0][0] - 2) > 1e-9 || Math.Abs(tries[1][1] - 4) > 1e-9 || Math.Abs(tries[2][0] - 1) > 1e-9 || Math.Abs(tries[5][1] - 6) > 1e-9)
                    wrong.Add("the points to try: " + tries.Count + " in the wrong order");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "6 pairs of keys, 4 lines, 2 numbers, 6 points" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void Arrows(Type sw)
        {
            const string label = "point 12, the owner's rule: the text inside with the arrows inside when it fits with an arrow on each side, otherwise the text outside with the arrows outside; a text whose width cannot be read is not called inside";
            try
            {
                var wrong = new List<string>();
                Func<double, double, string> arrows = (gap, width) => (string)Call(sw, "ArrowsFor", gap, width, 0.0035);
                if (arrows(0.040, 0.006) != "inside") wrong.Add("a text of 6 mm in 40 mm: " + arrows(0.040, 0.006));
                if (arrows(0.0075, 0.006) != "outside") wrong.Add("1,5 at 5:1, 7.5 mm for 6 mm of text: " + arrows(0.0075, 0.006));
                if (arrows(0.0131, 0.006) != "inside") wrong.Add("13.1 mm, the text and two arrows: " + arrows(0.0131, 0.006));
                if (arrows(0.040, 0.0) != "outside") wrong.Add("a text with no width: " + arrows(0.040, 0.0));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "4 dimensions" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void DetailCircle(Type sw)
        {
            const string label = "point 13: «circle_style» with_leader, standard, no_leader, connected, broken as swDetViewStyle_e, any other word refused, a style read back named again";
            try
            {
                var wrong = new List<string>();
                var expected = new Dictionary<string, int> { ["with_leader"] = 2, ["standard"] = 0, ["no_leader"] = 3, ["connected"] = 4, ["broken"] = 1 };
                foreach (KeyValuePair<string, int> e in expected)
                {
                    var r = Call(sw, "DetailCircleStyle", e.Key) as int?;
                    if (r != e.Value) wrong.Add(e.Key + " = " + (r == null ? "null" : r.ToString()));
                    if ((string)Call(sw, "DetailCircleStyleName", e.Value) != e.Key) wrong.Add(e.Value + " named " + Call(sw, "DetailCircleStyleName", e.Value));
                }
                if (Call(sw, "DetailCircleStyle", "leader") != null) wrong.Add("«leader» accepted");
                if (Call(sw, "DetailCircleStyleName", -1) != null) wrong.Add("an unread style named");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "5 styles both ways, 2 refusals" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void Balloons(Type sw)
        {
            const string label = "point 9: a balloon put again goes out of the outline of its view, on the side of its arrow, 10 mm beyond the edge it crosses; one on the centre goes up; a balloon on another, on a note or across the frame is blocked and tried at 39 places outside the outline (note 98, defect 7)";
            try
            {
                var wrong = new List<string>();
                var outline = new[] { 0.100, 0.100, 0.200, 0.150 };
                Func<double, double, double[]> outside = (x, y) => (double[])Call(sw, "BalloonOutside", x, y, outline, 0.010);
                var right = outside(0.180, 0.125);
                if (Math.Abs(right[0] - 0.210) > 1e-9 || Math.Abs(right[1] - 0.125) > 1e-9) wrong.Add("to the right: " + right[0] + ", " + right[1]);
                var top = outside(0.150, 0.140);
                if (Math.Abs(top[0] - 0.150) > 1e-9 || Math.Abs(top[1] - 0.160) > 1e-9) wrong.Add("upward: " + top[0] + ", " + top[1]);
                var centre = outside(0.150, 0.125);
                if (Math.Abs(centre[1] - 0.160) > 1e-9) wrong.Add("from the centre: " + centre[0] + ", " + centre[1]);
                var corner = outside(0.200, 0.150);
                if (!(corner[0] > 0.200 && corner[1] > 0.150)) wrong.Add("towards the corner, still inside: " + corner[0] + ", " + corner[1]);
                // The places tried when that one is taken (note 98, defect 7): 39, the first BalloonOutside,
                // all outside the outline.
                var places = (System.Collections.IList)Call(sw, "BalloonCandidates", 0.180, 0.125, outline);
                if (places == null || places.Count != 39) wrong.Add("a balloon has " + (places == null ? "no" : places.Count.ToString()) + " places, not 39");
                else
                {
                    var first = (double[])places[0];
                    if (Math.Abs(first[0] - right[0]) > 1e-9 || Math.Abs(first[1] - right[1]) > 1e-9) wrong.Add("the first place is not the one of BalloonOutside");
                    foreach (double[] p in places)
                        if (p[0] > 0.100 - 1e-9 && p[0] < 0.200 + 1e-9 && p[1] > 0.100 - 1e-9 && p[1] < 0.150 + 1e-9) { wrong.Add("a place inside the outline: " + p[0] + ", " + p[1]); break; }
                }
                // What blocks a balloon: section A-A of 26M000002 with 0.168.0 (sheet mm), the 2 and the 4
                // one on the other, the 1 on the label, the 2 of the isometric across the frame of an A3.
                Func<double[], double[], string> blocked = (box, other) => (string)Call(sw, "BalloonBlocked", box, null,
                    new[] { 0.420, 0.297 }, new List<double[]> { other }, new List<string> { "the other" }, new List<bool> { false },
                    new List<double[]>(), new List<string>(), null, 0.0005);
                Func<double, double, double[]> circle = (x, y) => new[] { x - 0.00485, y - 0.00485, x + 0.00485, y + 0.00485 };
                if (blocked(circle(0.20278, 0.26243), circle(0.20398, 0.26242)) == null) wrong.Add("the 2 and the 4 of section A-A taken as free");
                if (blocked(circle(0.20278, 0.19655), new[] { 0.180, 0.190, 0.228, 0.200 }) == null) wrong.Add("the 1 of section A-A on its label taken as free");
                if (blocked(circle(0.2495, 0.2894), new[] { 0.0, 0.0, 0.001, 0.001 }) == null) wrong.Add("the 2 of the isometric across the frame taken as free");
                if (blocked(circle(0.20278, 0.26243), circle(0.230, 0.26243)) != null) wrong.Add("two balloons 27 mm apart taken as touching");
                // The places near a balloon first: 36, the nearest ring 1.25 sizes away, to the right first.
                var ring = (System.Collections.IList)Call(sw, "BalloonRing", 0.2, 0.2, 0.0097);
                if (ring == null || ring.Count != 36) wrong.Add("the rings around a balloon have " + (ring == null ? "no" : ring.Count.ToString()) + " places, not 36");
                else
                {
                    var near = (double[])ring[0];
                    var far = (double[])ring[35];
                    if (Math.Abs(near[0] - (0.2 + 1.25 * 0.0097)) > 1e-9 || Math.Abs(near[1] - 0.2) > 1e-9) wrong.Add("the first place near a balloon is not 1.25 sizes to its right");
                    if (Math.Abs(Math.Sqrt((far[0] - 0.2) * (far[0] - 0.2) + (far[1] - 0.2) * (far[1] - 0.2)) - 3.75 * 0.0097) > 1e-9) wrong.Add("the last ring is not 3.75 sizes away");
                }
                // The circle from the leader: the 4 of the isometric of 26M000002 (GetBalloonInfo 402.78, 187.41 mm);
                // a leader across the label of the section (the 1, second run of 0.170.0) blocks, one into a view does not.
                var circleOf4 = (double[])Call(sw, "BalloonCircleBox", new[] { 0.39799, 0.1881 }, new[] { 0.39576, 0.18826 }, 0.00498);
                if (circleOf4 == null || Math.Abs((circleOf4[0] + circleOf4[2]) / 2 - 0.40278) > 0.0005 || Math.Abs((circleOf4[1] + circleOf4[3]) / 2 - 0.18741) > 0.0005)
                    wrong.Add("the circle of the 4 of the isometric is not within 0.5 mm of 402.78, 187.41 mm");
                var label1 = new[] { 0.17745, 0.19207, 0.2277, 0.20868 };
                Func<bool, string> across = may => (string)Call(sw, "BalloonBlocked", circle(0.18556, 0.16672), new[] { 0.18556, 0.16672, 0.20257, 0.21779 },
                    new[] { 0.420, 0.297 }, new List<double[]> { label1 }, new List<string> { "the label" }, new List<bool> { may },
                    new List<double[]>(), new List<string>(), null, 0.0005);
                if (across(false) == null) wrong.Add("the leader of the 1 of the section across its label taken as free");
                if (across(true) != null) wrong.Add("a leader into an outline taken as blocked");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "4 arrows, 39 places, 4 balloons judged, 36 places near, 1 circle, 2 leaders" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void TitleBlock(Type sw)
        {
            const string label = "point 10: the columns of the bill of materials brought to the width of the title block by one factor, no factor without two widths, the rows on two lines or more counted";
            try
            {
                var wrong = new List<string>();
                if (Math.Abs((double)Call(sw, "ColumnFactor", 0.100, 0.180) - 1.8) > 1e-9) wrong.Add("100 to 180 mm");
                if ((double)Call(sw, "ColumnFactor", 0.0, 0.180) != 1.0) wrong.Add("a table of no width");
                if ((double)Call(sw, "ColumnFactor", 0.100, 0.0) != 1.0) wrong.Add("a title block of no width");
                if ((int)Call(sw, "TallerRows", new List<double> { 0.006, 0.006, 0.012, 0.006, 0.0095 }) != 2) wrong.Add("two rows on two lines");
                if ((int)Call(sw, "TallerRows", new List<double>()) != 0) wrong.Add("no rows");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "3 factors, 2 tables" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void Move(Type sw)
        {
            const string label = "point 8: a dimension moved to a place with both «x_mm» and «y_mm», or by a shift; both, neither or half a place refused; a place outside the sheet refused";
            try
            {
                var wrong = new List<string>();
                Func<bool, bool, bool, bool, string> move = (x, y, dx, dy) => (string)Call(sw, "MoveProblem", x, y, dx, dy);
                if (move(true, true, false, false) != null) wrong.Add("a place refused");
                if (move(false, false, true, false) != null) wrong.Add("a shift to the right refused");
                if (move(false, false, false, false) == null) wrong.Add("nowhere accepted");
                if (move(true, false, false, false) == null) wrong.Add("x alone accepted");
                if (move(true, true, true, false) == null) wrong.Add("a place and a shift accepted");
                var a3 = new[] { 0.420, 0.297 };
                if (!(bool)Call(sw, "OnTheSheet", 0.100, 0.100, a3)) wrong.Add("a point of the sheet outside");
                if ((bool)Call(sw, "OnTheSheet", 0.500, 0.100, a3)) wrong.Add("x 500 mm on an A3");
                if ((bool)Call(sw, "OnTheSheet", 0.100, -0.001, a3)) wrong.Add("below the sheet");
                if (!(bool)Call(sw, "OnTheSheet", 0.500, 0.100, null)) wrong.Add("a sheet of unknown size refuses");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "5 requests, 4 places" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void Read(Type sw)
        {
            const string label = "point 20: every kind of dimension of a finished drawing has its own word, the rest «unknown»; a note is in the title block only inside its box";
            try
            {
                var wrong = new List<string>();
                var named = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int type = -1; type <= 20; type++)
                {
                    var word = (string)Call(sw, "DimensionKindName", type);
                    if (string.IsNullOrEmpty(word)) { wrong.Add("kind " + type + " without a word"); continue; }
                    if (word == "unknown") continue;
                    if (named.ContainsKey(word)) wrong.Add("«" + word + "» for kinds " + named[word] + " and " + type);
                    else named[word] = type;
                }
                foreach (var must in new[] { "linear", "angular", "diameter", "radius", "chamfer", "horizontal", "vertical" })
                    if (!named.ContainsKey(must)) wrong.Add("no kind reads «" + must + "»");
                if ((string)Call(sw, "DimensionKindName", -1) != "unknown") wrong.Add("kind -1 not «unknown»");
                if ((string)Call(sw, "DimensionKindName", 99) != "unknown") wrong.Add("kind 99 not «unknown»");
                var block = new[] { 0.240, 0.0, 0.420, 0.056 };
                if (!(bool)Call(sw, "InsideBox", 0.300, 0.020, block)) wrong.Add("a note in the title block outside it");
                if ((bool)Call(sw, "InsideBox", 0.100, 0.020, block)) wrong.Add("a note left of the title block inside it");
                if ((bool)Call(sw, "InsideBox", 0.300, 0.080, block)) wrong.Add("a note above the title block inside it");
                if ((bool)Call(sw, "InsideBox", 0.300, 0.020, null)) wrong.Add("no title block, and the note is inside it");
                if ((bool)Call(sw, "InsideBox", 0.300, 0.020, new[] { 0.240, 0.0 })) wrong.Add("half a box, and the note is inside it");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? named.Count + " kinds with their word, 5 places" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING
        // =================================================================

        private const string BetweenParsed = "betweenPairs = BetweenPairsAsked(a[\"between\"], available, out betweenWhy);";
        private const string EdgesMeasured = "PhaseClock.Time(\"drawing_dimension.edges_on_the_sheet\"";
        private const string BetweenRoute = "return DimensionBetween(model, drawing, view, viewName, edges, all, circles, betweenPairs, contour,";
        private const string BetweenAlone = "if (requestedList.Count > 0 || a.ContainsKey(\"overall\"))";
        private const string DetailAbove = ": IsDetailView(view);";
        private const string HolesLeft = "if (trueHoles.Count > 0 && holesInTable <= 0 && holesWanted)";
        private const string ChamfersLeft = "if (chamfersWanted)";
        private const string EdgeDecorated = "DecorateDimension(nature, described);";
        private const string EdgeArrows = "ArrowsByText(nature, described, Math.Min(s.FromSheet[0], s.ToSheet[0]), Math.Max(s.FromSheet[0], s.ToSheet[0]), true);";
        private const string EdgeRow = "if (!row) below++;";
        private const string DecorationReset = "finally { _dimensionPrefix = null; _dimensionSuffix = null; _dimensionParentheses = false; _dimensionBelow = null; }";
        private const string BetweenShown = "described[\"text_shown\"] = ExactTextShown(d, described);";
        private const string BetweenDecorated = "DecorateDimension(d, described);";
        // Library 0.189.0: the arrows of a distance are set where the owner's layout puts its text
        // (LayOutDimensions, DrawingDimensionStyle.cs), after every distance of the call is born.
        private const string BetweenArrows = "int wanted = textInside ? (int)swDimensionArrowsSide_e.swDimArrowsInside : (int)swDimensionArrowsSide_e.swDimArrowsOutside;";
        private const string BetweenReadBack = "if (d != null && (!(mm is double) || Math.Abs((double)mm - expectedMm) > 0.05))";
        private const string SectionParsed = "sectionRatio = ParseScale(Str(a, \"scale\", null), out scaleWhy);";
        private const string SectionScaled = "string sectionScaleProblem = ApplyViewScale(model, sectionView, sectionRatio);";
        private const string SectionPlaced = "if (!SectionInAFreePlace(drawing, sectionView, facing, out sectionWhere, out sectionPlaceProblem))";
        private const string DetailStyled = "(int)circleStyle, scale1, 1.0, label,";
        private const string DetailLocked = "if (lockPosition) try { detailCircle.PinPosition = true; } catch { }";
        private const string BalloonsRoute = "return BalloonsAgain(model, drawing, view);";
        private const string PickedChecked = "if (picked != null && !SameBomPart(BomKeyOf(picked), key))";
        private const string BalloonBorn = "born = model.Extension.InsertBOMBalloon2(options) as INote;";
        private const string BlockWidth = "table.SetColumnWidth(c, table.GetColumnWidth(c) * factor,";
        private const string BlockSaid = "measurements[\"table_location\"] = \"over the title block\";";
        private const string MoveCase = "case \"move_annotation\": return MoveAnnotation(model, arguments);";
        private const string MoveChecked = "string moveWhy = MoveProblem(";
        private const string MoveDone = "try { annotation.SetPosition2(x, y, 0); }";
        private const string MoveReadBack = "if (after == null || after.Length < 2 || Math.Abs(after[0] - x) > 0.0001";
        private const string ReadCase = "case \"read_drawing\": return ReadDrawing(model, arguments);";
        // Note 98, defect 6 (library 0.192.0): move_view reads every view with its labels before the move.
        private const string ViewNotesBefore = "var withNotesBefore = WithNotesOfTheSheet(drawing);";
        private const string ViewMoved = "try { view.Position = new[] { p[0] + want[0], p[1] + want[1] }; }";
        private const string AutoInView = "inTheViewAuto = DimensionsInTheView(view, mu, tr, out unreadAuto);";
        private const string AutoBorn = "var nature = model.Extension.AddDimension(x, y, 0, 0)";
        private const string ViewUnaligned = "try { view.RemoveAlignment(); } catch { }";
        private const string ReadBlockNotes = "if (at != null && InsideBox(at[0], at[1], block)) { blockNotes++; continue; }";

        private static readonly string[][] Files =
        {
            new[] { "dimension", "Oce.Solid.Agent.Core", "DrawingDimensionOperations.cs" },
            new[] { "between", "Oce.Solid.Agent.Core", "DrawingBetweenDimensions.cs" },
            new[] { "assembly", "Oce.Solid.Agent.Core", "DrawingAssemblyDimensions.cs" },
            new[] { "section", "Oce.Solid.Agent.Core", "DrawingSectionOperations.cs" },
            new[] { "detail", "Oce.Solid.Agent.Core", "DrawingDetailOperations.cs" },
            new[] { "bom", "Oce.Solid.Agent.Core", "BomOperations.cs" },
            new[] { "repeat", "Oce.Solid.Agent.Core", "BalloonRepeatOperations.cs" },
            new[] { "operations", "Oce.Solid.Agent.Core", "SwOperations.cs" },
            new[] { "move", "Oce.Solid.Agent.Core", "DrawingMoveAnnotation.cs" },
            new[] { "read", "Oce.Solid.Agent.Core", "ReadDrawingOperations.cs" },
            new[] { "view", "Oce.Solid.Agent.Core", "DrawingViewOperations.cs" },
            new[] { "spread", "Oce.Solid.Agent.Core", "BalloonSpreadOperations.cs" },
            new[] { "sheet", "Oce.Solid.Agent.Core", "SheetPlacementOperations.cs" },
            new[] { "state", "Oce.Solid.Agent.Core", "DocumentState.cs" },
            new[] { "guard", "Oce.Solid.Agent.Core", "StuckDragGuard.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "dimension", BetweenParsed, "«between» is not read (point 1)" },
            new[] { "dimension", BetweenRoute, "«between» does not take its route (point 1)" },
            new[] { "dimension", BetweenAlone, "«between» is mixed with «edges» or «overall» (point 1)" },
            new[] { "dimension", DetailAbove, "the dimensions of a detail do not go above it by default (point 14)" },
            new[] { "dimension", HolesLeft, "the holes cannot be left to another view" },
            new[] { "dimension", ChamfersLeft, "the chamfers cannot be left to another view (point 11)" },
            new[] { "dimension", EdgeDecorated, "a named edge does not get the text of the call (points 5, 6)" },
            new[] { "dimension", EdgeArrows, "a named edge does not get its arrows where its text is (point 12)" },
            new[] { "dimension", EdgeRow, "a row still goes down a step for each dimension (point 2)" },
            new[] { "dimension", DecorationReset, "the text of one call stays for the next (points 5, 6)" },
            new[] { "between", BetweenDecorated, "a distance does not get the text of the call (points 5, 6)" },
            new[] { "between", BetweenArrows, "a distance does not get its arrows where its text is (point 12)" },
            new[] { "between", BetweenReadBack, "a distance is not compared with the edges on the sheet (point 1)" },
            new[] { "assembly", EdgeDecorated.Replace("nature", "d"), "a dimension of an assembly does not get the text of the call (point 6)" },
            new[] { "section", SectionParsed, "«scale» of a section is not read (point 4)" },
            new[] { "section", SectionScaled, "the section is not given the scale asked (point 4)" },
            new[] { "detail", DetailStyled, "the detail circle is not made in the style asked (point 13)" },
            new[] { "detail", DetailLocked, "the position of the detail is not locked (point 13)" },
            new[] { "bom", BalloonsRoute, "«again» does not take its route (point 9)" },
            new[] { "repeat", PickedChecked, "a balloon put again is not checked to be on its part (point 9)" },
            new[] { "bom", BlockWidth, "the bill of materials is not brought to the width of the title block (point 10)" },
            new[] { "bom", BlockSaid, "the answer does not say the table is over the title block (point 10)" },
            new[] { "operations", MoveCase, "a plan cannot run «move_annotation» (point 8)" },
            new[] { "move", MoveReadBack, "a moved annotation is not read back (point 8)" },
            new[] { "operations", ReadCase, "a plan cannot run «read_drawing» (point 20)" },
            new[] { "read", ReadBlockNotes, "the notes of the title block are listed with the others (point 20)" },
            new[] { "dimension", "string decorationWhy = DecorationProblem(prefixAsked, suffixAsked, parenthesesAsked) ?? BelowProblem(belowAsked);", "«below» is not checked (note 98, defect 2)" },
            new[] { "dimension", "_dimensionBelow = belowAsked as string;", "«below» is not handed to the dimensions (note 98, defect 2)" },
            new[] { "between", "try { d.SetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelow, _dimensionBelow); } catch { }", "«below» is not written (note 98, defect 2)" },
            new[] { "read", "try { below = d.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelow); } catch { }", "read_drawing does not read the text under the line (note 98, defect 2)" },
            new[] { "read", "row[\"direction\"] = DimensionDirection(kind, firstFacing, secondFacing);", "read_drawing does not say which way a distance measures (note 98, defect 3)" },
            new[] { "view", ViewNotesBefore, "move_view reads the labels of a view after the move, where they still say the old place (note 98, defect 6)" },
            new[] { "view", "? LandedWithTheOutline(r0, o0, o1)", "move_view does not move the labels as far as the outline moved (note 98, defect 6)" },
            new[] { "view", "if (Bool(a, \"unalign\", false) && AlignedToItsParent(alignment))", "«unalign» is not read (note 98, defect 5)" },
            new[] { "view", "try { view.RemoveAlignment(); } catch { }", "a view is not taken off its line (note 98, defect 5)" },
            new[] { "view", "try { view.UseDefaultAlignment(); } catch { }", "a refused move does not give back the alignment it took away (note 98, defect 5)" },
            new[] { "dimension", "if (chamferDimension)", "a distance on an edge of the chamfer face makes the chamfer «already dimensioned» (note 98, defect 1)" },
            new[] { "dimension", "try { chamferDimension = d.Type2 == (int)swDimensionType_e.swChamferDimension; } catch { }", "a chamfer dimension is not told from the others (note 98, defect 1)" },
            new[] { "dimension", "if (!notTrueSize.ContainsKey(s.ChamferFeature)) notTrueSize[s.ChamferFeature] = slanted;", "a chamfer is tried on an edge that does not show it in its true size (note 98, defect 1: 135° and 164.77°)" },
            new[] { "dimension", "if (slanted != null) { refused.Add(entry[0] + \" (\" + slanted + \": dimension it in a view that looks along the chamfered edge)\"); continue; }", "«chamfer» takes an edge that does not show the chamfer in its true size (note 98, defect 1)" },
            new[] { "dimension", "if (s.Length / scale > 0.010 || NotSeenInItsTrueSize(s, scale) != null) continue;", "an edge not parallel to the sheet is taken as a chamfer the profile draws (note 98, defect 1)" },
            new[] { "dimension", "s.ChamferKey = ChamferProfileKey(s.Length / scale);", "the chamfers the profile draws with the same length are dimensioned one by one (note 98, defect 1: two 0,5 x 59,53° in 26P000011 view 2)" },
            new[] { "dimension", "if (chamferWidth.HasValue) outside.Add(ChamferProfileKey(chamferWidth.GetValueOrDefault()));", "a chamfer dimension already in the drawing does not keep the chamfers the profile draws with its width (note 98, defect 1)" },
            new[] { "dimension", "int byWay = along(p).CompareTo(along(q));", "the legs that run the same way as the chamfer edge are tried first: SOLIDWORKS reads 135° on them (note 98, defect 1)" },
            new[] { "dimension", "if (!ReferenceEquals(t, s) && t.Facing != \"oblique\" && t.Straight && MeetAtAnEnd(s, t))", "an edge on a chamfer face is not a leg: in 26P000008 detail A only such edges run against the chamfer (note 98, defect 1)" },
            new[] { "repeat", "measurements[\"balloons_moved_to_a_free_place\"] = FreeTheBalloons(drawing, view, balloonsBefore, true, out freeNote).ToArray();", "the balloons put again stay one on the other, on a label or across the frame (note 98, defect 7: 26M000002)" },
            new[] { "bom", "measurements[\"balloons_moved_to_a_free_place\"] = FreeTheBalloons(drawing, view, balloonsBefore, false, out freeNote).Cast<object>().ToArray();", "the balloons of AutoBalloon5 stay across the frame (note 98, defect 7: the 2 of the isometric of 26M000002)" },
            new[] { "dimension", "return WhatTheTextCovers(m, noteNames, noteBoxes, titleBlock, margin).Count > 0;", "the text of a chamfer dimension is put over a note or the title block (note 98, defect 1)" },
            new[] { "dimension", "var dimensionLines = DimensionLines(drawing, own);", "the text of a chamfer dimension is put across the lines of the other dimensions (note 98, defect 1: 26P000011 view 2)" },
            new[] { "dimension", "string textPlace = MoveChamferText(drawing, dimension, new[] { mx, my }, at, all, out endedAt);", "the text of a chamfer dimension stays where AddChamferDim puts it, over the part (note 98, defect 1: 26P000011 view 2)" },
            new[] { "dimension", "var legs = ChamferLegs(s, all);", "the automatic chamfer dimension tries one leg only (note 98, defect 1)" },
            new[] { "dimension", "foreach (var leg in legs)", "a chamfer dimension taken away is not tried on the next leg (note 98, defect 1)" },
            new[] { "dimension", "var profileChamfers = MarkProfileChamfers(all, scale);", "a chamfer the profile draws is dimensioned as an oblique line (note 98, defect 1)" },
            new[] { "dimension", "chamferAsked = ChamferEdgesAsked(a[\"chamfer\"], available, out chamferWhy);", "«chamfer» is not read (note 98, defect 1)" },
            new[] { "dimension", "return DimensionChamfersAsked(model, drawing, view, viewName, edges, all, chamferAsked, contour,", "«chamfer» does not take its route (note 98, defect 1)" },
            new[] { "sheet", "double[] drawn = TitleBlockFromLines(PhaseClock.Time(\"sheet.format_lines\", () => FormatLines(sheet)), w, h);", "the lines of a format that declares no title block are not read (note 98, defect 8)" },
            new[] { "repeat", "keys.Add(\"table|\" + vn + \"|\" + tableName); boxes.Add(tableBox); names.Add(\"the table «\" + (tableName ?? \"\") + \"» (\" + vn + \")\"); crossable.Add(false);", "a balloon is moved onto the bill of materials: the tables of the sheet are not in its way (library 0.198.6)" },
            new[] { "operations", "using (StuckDragGuard.During(out mark))", "an operation whose simulated click starts a drag of the view never comes back (library 0.198.7)" },
            new[] { "state", "using (StuckDragGuard.During(out mark))", "a reading of the state whose simulated click starts a drag of the view never comes back (library 0.198.7)" },
            new[] { "guard", "if (capture == IntPtr.Zero || button) { _held = IntPtr.Zero; _heldMs = 0; _escaped = false; return; }", "Escape is posted while the owner holds a mouse button (library 0.198.7)" },
            new[] { "sheet", "try { inOneCall = LinesOfGetLines2(sketch.GetLines2((short)swCrossHatchFilter_e.swCrossHatchExclude) as double[]); }", "the lines of the format are read one segment at a time again: about 0,7 s more in every drawing call that looks for a free place (library 0.198.4)" },
            new[] { "sheet", "f.TitleBlock = TitleBlockOfTheSheet(sheet, out blockFrom);", "the free places estimate a title block the format draws (note 98, defect 8)" },
            new[] { "bom", "double[] block = TitleBlockOfTheDrawing(drawing, out blockFrom);", "the bill of materials does not rest on a title block the format only draws (note 98, defect 8)" },
            new[] { "bom", "string covers = placedSheet.TouchesBeside(", "a bill of materials resting on the title block is said to fall on it (note 98, defect 8)" },
            new[] { "read", "double[] block = TitleBlockOfTheSheet(sheet, out blockFrom);", "read_drawing does not find a title block the format only draws (note 98, defect 8)" },
            new[] { "dimension", "DimensionStyle autoStyle = StyleOfThisCall(out autoStyleWhy);", "the automatic dimensions do not read the owner's style (note 98, defect 4)" },
            new[] { "dimension", AutoInView, "the automatic dimensions do not see the dimensions already in the view (note 98, defect 4)" },
            new[] { "dimension", "double[][] placesAuto = LayOutDimensions(toLayAuto, drawnAuto, autoStyle, row, sheetDimensions, inTheViewAuto);", "the automatic dimensions go one row each, out of the sheet (note 98, defect 4)" },
            new[] { "dimension", "ApplyPlace(dimA, rowA, atA, horizontalA);", "the automatic dimensions are not moved where the layout puts them (note 98, defect 4)" },
            new[] { "between", "ApplyPlace(d, p.Row, at, p.Horizontal);", "a distance is not moved where the layout puts it" },
            new[] { "view", "if (labelsOut.Count > 0) measurements[\"labels_out_of_place\"] = labelsOut.ToArray();", "move_view does not say where the labels of a view went when only they are in the way (replay of 02/11/2026, step 038)" },
            new[] { "repeat", "var picked = PickedComponent(manager);", "a balloon again reads the pick only as a component, not as a drawing component (note 98, defect 7)" },
            new[] { "repeat", "if (c == null) try { c = (raw as IDrawingComponent)?.Component as IComponent2; } catch { }", "a drawing component picked is not brought to its component (note 98, defect 7)" },
            new[] { "repeat", "double[] inside = ClippedToOutline(whole, outline);", "a balloon again tries lines outside the view, as a detail lists them (note 98, defect 7)" },
            new[] { "repeat", "if (got != expected)", "a balloon on a line that says no component is kept without its number checked (note 98, defect 7)" },
            new[] { "spread", "var byOwner = ToSheetOf(edges[0], view.ModelToViewTransform as MathTransform, out owner, out ownerPlacement);", "the lines of a component in a section are placed with the component the view lists, not with the owner of its edges (note 98, defect 7)" },
        };

        private static void Order(Dictionary<string, string> sources, string key, string first, string then, string lost, List<string> wrong)
        {
            string text;
            if (!sources.TryGetValue(key, out text) || text == null) return;
            int a = text.IndexOf(first, StringComparison.Ordinal);
            int b = text.IndexOf(then, StringComparison.Ordinal);
            if (a < 0 || b < a) wrong.Add(lost);
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
            Order(sources, "dimension", BetweenParsed, EdgesMeasured, "«between» is read after the edges are measured (point 1)", wrong);
            Order(sources, "between", BetweenShown, BetweenDecorated, "a distance is decorated before its decimals are read (points 5, 6)", wrong);
            Order(sources, "section", SectionScaled, SectionPlaced, "the section is placed before its scale is set (point 4)", wrong);
            Order(sources, "repeat", PickedChecked, BalloonBorn, "a balloon is made before its part is checked (point 9)", wrong);
            Order(sources, "move", MoveChecked, MoveDone, "an annotation is moved before the place is checked (point 8)", wrong);
            Order(sources, "view", ViewNotesBefore, ViewMoved, "move_view reads the labels after the view moved (note 98, defect 6)", wrong);
            Order(sources, "dimension", AutoInView, AutoBorn, "the automatic dimensions read the view after they are born: they would keep off themselves (note 98, defect 4)", wrong);
            Order(sources, "view", ViewUnaligned, ViewMoved, "move_view moves a view before taking it off its line (note 98, defect 5)", wrong);
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: «between» read before the edges are measured, on its own route, read back; the row, the side, a detail above; holes and chamfers left to another view; the text and the parentheses on every dimension placed and reset after the call; the arrows where the text is; the scale of a section set before it is placed; the style and the lock of the detail; the balloons again checked on their part; the bill of materials at the width of the title block; move_annotation runnable, checked first and read back; read_drawing runnable, the title block counted apart";
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Files)
            {
                var path = Path.Combine(Root(), f[1], f[2]);
                sources[f[0]] = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            var real = WiringProblems(sources);

            // Teeth: each line taken away, the order swapped.
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
            mutate("«between» read after the edges are measured", "dimension",
                   t => t.Replace(BetweenParsed, "/* moved */").Replace(EdgesMeasured, EdgesMeasured + "\n" + BetweenParsed));
            mutate("a distance decorated before its decimals", "between",
                   t => t.Replace(BetweenShown, "/* moved */").Replace(BetweenDecorated, BetweenDecorated + "\n" + BetweenShown));
            mutate("the section placed before its scale", "section",
                   t => t.Replace(SectionScaled, "/* moved */").Replace(SectionPlaced, SectionPlaced + "\n" + SectionScaled));
            mutate("a balloon made before its part is checked", "repeat",
                   t => t.Replace(PickedChecked, "/* moved */").Replace(BalloonBorn, BalloonBorn + "\n" + PickedChecked));
            mutate("an annotation moved before the place is checked", "move",
                   t => t.Replace(MoveChecked, "/* moved */").Replace(MoveDone, MoveDone + "\n" + MoveChecked));
            mutate("the labels of a view read after it moved", "view",
                   t => t.Replace(ViewNotesBefore, "/* moved */").Replace(ViewMoved, ViewMoved + "\n" + ViewNotesBefore));
            mutate("the view read after the automatic dimensions are born", "dimension",
                   t => t.Replace(AutoInView, "/* moved */").Replace(AutoBorn, AutoBorn + "; " + AutoInView));
            mutate("a view taken off its line after it moved", "view",
                   t => t.Replace(ViewUnaligned, "/* moved */").Replace(ViewMoved, ViewMoved + "\n" + ViewUnaligned));

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : Lines.Length + " lines, eight orders; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
