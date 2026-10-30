using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 21 OF REPORT 8CD843 (28/10/2026), WHERE THE BENCH CANNOT
    /// REACH.
    ///
    /// Point 1, the scale of a view: «standard_views» put a part 175 mm long at 1:2 on an
    /// A3 with the sheet at 1:1, nothing could change it, and the title block printed
    /// «1:1». Now «scale» on «standard_views» and «model_view», the operation
    /// «set_view_scale», and the note when a view has another scale than the sheet.
    ///
    /// Point 4 (P1), the number on the sheet: 0.75 mm printed «0,8», 10.018 «10». Now a
    /// dimension placed gets the decimals its value needs, «decimals» fixes them, and
    /// one that still prints another number is listed.
    ///
    /// Two halves, as Zc2202: the decisions by reflection, and the wiring on the
    /// sources, every rule first red on a changed copy. On library 0.175.0 before
    /// round 21 every check is red.
    /// </summary>
    internal static class Zc2203DrawingRound21Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 21 of report 8cd843: the scale of a view, asked on standard_views and model_view, changed with set_view_scale, read back, and said when it is not the sheet's");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 21 fixes",
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

            Scale(sw);
            SheetNote(sw);
            Printed(sw);
            PartOverall(sw);
            Width(sw);
            Covers(sw);
            ChamferTrueSize(sw);
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

        private static void Scale(Type sw)
        {
            const string label = "point 1: «1:2», «2:1», «1:2.5» read as two numbers, every other writing refused with its reason, the ratio written back as the drawing writes it, a scale read compared by its value";
            try
            {
                var wrong = new List<string>();
                Func<string, double[]> parse = text =>
                {
                    var args = new object[] { text, null };
                    var m = ReflectionLog.Global.Method(sw, "ParseScale", Any);
                    if (m == null) throw new MissingMethodException("SwOperations.ParseScale");
                    return m.Invoke(null, args) as double[];
                };
                Func<string, string> reason = text =>
                {
                    var args = new object[] { text, null };
                    var m = ReflectionLog.Global.Method(sw, "ParseScale", Any);
                    m.Invoke(null, args);
                    return args[1] as string;
                };
                foreach (var good in new[] { new object[] { "1:2", 1.0, 2.0 }, new object[] { " 2 : 1 ", 2.0, 1.0 }, new object[] { "1:2.5", 1.0, 2.5 } })
                {
                    var r = parse((string)good[0]);
                    if (r == null || r[0] != (double)good[1] || r[1] != (double)good[2])
                        wrong.Add("«" + good[0] + "» read as " + (r == null ? "null" : r[0] + ":" + r[1]));
                }
                foreach (var bad in new[] { "half", "", "1/2", "1:2:3", "0:1", "1:-2", "1000:1", "1:5000", "1,5:2" })
                {
                    if (parse(bad) != null) wrong.Add("«" + bad + "» accepted");
                    else if (string.IsNullOrEmpty(reason(bad))) wrong.Add("«" + bad + "» refused with no reason");
                }
                var text12 = Call(sw, "ScaleText", new[] { 1.0, 2.0 }) as string;
                if (text12 != "1:2") wrong.Add("ScaleText(1, 2) = " + (text12 ?? "null"));
                var text125 = Call(sw, "ScaleText", new[] { 1.0, 2.5 }) as string;
                if (text125 != "1:2.5") wrong.Add("ScaleText(1, 2.5) = " + (text125 ?? "null"));
                if (Call(sw, "ScaleText", new object[] { null }) != null) wrong.Add("ScaleText of nothing");
                if (!(bool)Call(sw, "SameScale", 0.5, new[] { 2.0, 4.0 })) wrong.Add("0.5 not the same as 2:4");
                if ((bool)Call(sw, "SameScale", 0.5, new[] { 1.0, 1.0 })) wrong.Add("0.5 the same as 1:1");
                if ((bool)Call(sw, "SameScale", -1.0, new[] { 1.0, 1.0 })) wrong.Add("an unread scale (-1) the same as 1:1");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "3 writings read, 9 refused with a reason, 3 texts, 3 comparisons" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void SheetNote(Type sw)
        {
            const string label = "point 1: the views whose scale is not the sheet's are said, with the way to the sheet («set_sheet»), and nothing is said when there is none";
            try
            {
                var wrong = new List<string>();
                var one = Call(sw, "DifferentScaleText", "1:1", new List<string> { "«Drawing View1» 1:2" }) as string;
                foreach (var part in new[] { "The sheet is at 1:1 and this view is not: «Drawing View1» 1:2", "The title block prints the scale of the sheet", "«set_sheet»" })
                    if (one == null || !one.Contains(part)) wrong.Add("the text lacks «" + part + "»: " + (one ?? "null"));
                var two = Call(sw, "DifferentScaleText", "1:1", new List<string> { "«A» 1:2", "«B» 1:2" }) as string;
                if (two == null || !two.Contains("these views are not: «A» 1:2, «B» 1:2")) wrong.Add("two views: " + (two ?? "null"));
                if (Call(sw, "DifferentScaleText", "1:1", new List<string>()) != null) wrong.Add("a note with no view");
                if (Call(sw, "DifferentScaleText", null, new List<string> { "«A» 1:2" }) != null) wrong.Add("a note with no sheet scale");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "one view, two views, none, no sheet scale" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void PartOverall(Type sw)
        {
            const string label = "point 2: «overall» on a view of a part asks the overall size alone, «none» or left out keeps the usual way, with «edges» it is two calls";
            try
            {
                var wrong = new List<string>();
                Func<object, bool, string> part = (value, edges) =>
                    (string)Call(sw, "OverallOnAPart", value == null ? new Dictionary<string, object>() : new Dictionary<string, object> { ["overall"] = value }, edges);
                if (part("both", false) != "both") wrong.Add("both: " + part("both", false));
                if (part("horizontal", false) != "horizontal") wrong.Add("horizontal: " + part("horizontal", false));
                if (part(true, false) != "both") wrong.Add("true: " + part(true, false));
                if (part(null, false) != null) wrong.Add("left out: " + part(null, false));
                if (part("none", false) != null) wrong.Add("none: " + part("none", false));
                if (part("sideways", false) != null) wrong.Add("a word it does not take: " + part("sideways", false));
                if (part("both", true) != "with edges") wrong.Add("both with edges: " + part("both", true));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "7 requests" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void Printed(Type sw)
        {
            const string label = "point 4: the number a dimension prints read from its text («0,8», «Ø10», «2,5 x 45°»), compared with the value, the decimals the value needs, the dimensions still rounded listed, «decimals» from 0 to 8 only";
            try
            {
                var wrong = new List<string>();
                Func<string, double?> printed = text => (double?)Call(sw, "PrintedNumber", text);
                if (printed("0,8") != 0.8) wrong.Add("«0,8» read as " + printed("0,8"));
                if (printed("10") != 10) wrong.Add("«10» read as " + printed("10"));
                if (printed("2,5 45") != 2.5) wrong.Add("«2,5 45» read as " + printed("2,5 45"));
                if (printed(null) != null || printed("") != null) wrong.Add("a number from no text");
                Func<double, string, bool> differs = (mm, text) => (bool)Call(sw, "PrintedDiffers", mm, text);
                if (!differs(0.75, "0,8")) wrong.Add("0.75 printed «0,8» not found different");
                if (!differs(10.018, "10")) wrong.Add("10.018 printed «10» not found different");
                if (!differs(3.535, "3,5")) wrong.Add("3.535 printed «3,5» not found different");
                if (differs(0.75, "0,75")) wrong.Add("0.75 printed «0,75» found different");
                if (differs(120, "120")) wrong.Add("120 printed «120» found different");
                if (differs(5, null)) wrong.Add("a dimension with no text found different");
                Func<double, int> needed = mm => (int)Call(sw, "DecimalsNeeded", mm, 3);
                if (needed(0.75) != 2) wrong.Add("0.75 needs " + needed(0.75));
                if (needed(120) != 0) wrong.Add("120 needs " + needed(120));
                if (needed(10.018) != 3) wrong.Add("10.018 needs " + needed(10.018));
                if (needed(1.835) != 3) wrong.Add("1.835 needs " + needed(1.835));
                if (needed(0.12345) != 3) wrong.Add("0.12345 needs " + needed(0.12345) + ", not at most 3");
                var rows = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object> { ["mm"] = 0.75, ["text_shown"] = "0,8" },
                    new Dictionary<string, object> { ["mm"] = 120.0, ["text_shown"] = "120" },
                    new Dictionary<string, object> { ["mm"] = null, ["text_shown"] = "7" },
                };
                var rounded = Call(sw, "RoundedOnTheSheet", rows) as List<string>;
                if (rounded == null || rounded.Count != 1 || rounded[0] != "0.75 mm printed 0,8")
                    wrong.Add("rounded: " + (rounded == null ? "null" : string.Join(" | ", rounded)));
                Func<object, object> asked = value =>
                {
                    var args = new object[] { value == null ? new Dictionary<string, object>() : new Dictionary<string, object> { ["decimals"] = value }, null };
                    var m = ReflectionLog.Global.Method(sw, "DecimalsAsked", Any);
                    if (m == null) throw new MissingMethodException("SwOperations.DecimalsAsked");
                    return m.Invoke(null, args);
                };
                if (!Equals(asked(null), -1)) wrong.Add("«decimals» left out: " + asked(null));
                if (!Equals(asked(2), 2)) wrong.Add("«decimals» 2: " + asked(2));
                if (!Equals(asked(0), 0)) wrong.Add("«decimals» 0: " + asked(0));
                foreach (var bad in new object[] { 9, -1, 1.5, "two" })
                    if (asked(bad) != null) wrong.Add("«decimals» " + bad + " accepted");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "4 texts, 6 comparisons, 5 values, the rows, 7 «decimals»" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void Width(Type sw)
        {
            const string label = "point 7: «width» asks a width mate, which wants «face_a2» and «face_b2» and refuses the same face twice; any other mate refuses them";
            try
            {
                var wrong = new List<string>();
                Func<string, bool> asked = v => (bool)Call(sw, "WidthAsked", v);
                if (!asked("width") || !asked(" Width ")) wrong.Add("«width» not read as a width mate");
                if (asked("coincident") || asked(null)) wrong.Add("another mate read as width");
                Func<bool, bool, bool, string> faces = (w, a2, b2) => (string)Call(sw, "WidthFacesProblem", w, a2, b2);
                var none = faces(true, false, false);
                if (none == null || !none.Contains("Give «face_a2» and «face_b2»") || !none.Contains("I touched nothing"))
                    wrong.Add("width with no second face: " + (none ?? "null"));
                var noB = faces(true, true, false);
                if (noB == null || !noB.Contains("Give «face_b2».")) wrong.Add("width without face_b2: " + (noB ?? "null"));
                var noA = faces(true, false, true);
                if (noA == null || !noA.Contains("Give «face_a2».")) wrong.Add("width without face_a2: " + (noA ?? "null"));
                if (faces(true, true, true) != null) wrong.Add("width with its four faces refused");
                if (faces(false, false, false) != null) wrong.Add("a coincident mate refused");
                var extra = faces(false, true, false);
                if (extra == null || !extra.Contains("only for a «width» mate")) wrong.Add("face_a2 on a coincident mate: " + (extra ?? "null"));
                Func<string, string, string, string, string> same = (a1, a2, b1, b2) => (string)Call(sw, "WidthSameFaceProblem", a1, a2, b1, b2);
                var sameA = same("left", "LEFT", "left", "right");
                if (sameA == null || !sameA.Contains("«face_a» and «face_a2» are the same face")) wrong.Add("the same width face: " + (sameA ?? "null"));
                var sameB = same("left", "right", "up", "up");
                if (sameB == null || !sameB.Contains("«face_b» and «face_b2» are the same face")) wrong.Add("the same tab face: " + (sameB ?? "null"));
                if (same("left", "right", "left", "right") != null) wrong.Add("two different pairs refused");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "4 variants, 6 sets of faces, 3 pairs" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void Covers(Type sw)
        {
            const string label = "point 5: a dimension text over a note or over the title block is said, a box of the title block once as the title block, and what was compared is said";
            try
            {
                var wrong = new List<string>();
                var box = Call(sw, "ExtentBox", new[] { 0.05, 0.03, 0.0, 0.01, 0.02, 0.0 }) as double[];
                if (box == null || box[0] != 0.01 || box[1] != 0.02 || box[2] != 0.05 || box[3] != 0.03)
                    wrong.Add("the corners given the other way round: " + (box == null ? "null" : string.Join(", ", box)));
                if (Call(sw, "ExtentBox", new[] { 0.01, 0.02, 0.0, 0.01, 0.05, 0.0 }) != null) wrong.Add("an extent with no width taken as a box");
                if (Call(sw, "ExtentBox", new[] { 0.01, 0.02, 0.03 }) != null) wrong.Add("three numbers taken as a box");
                if (Call(sw, "ExtentBox", new object[] { null }) != null) wrong.Add("no extent taken as a box");
                if ((string)Call(sw, "NoteShown", "DETAIL A\r\nSCALE 2 : 1") != "DETAIL A") wrong.Add("the first line of a note");
                if (Call(sw, "NoteShown", "  ") != null || Call(sw, "NoteShown", new object[] { null }) != null) wrong.Add("an empty note named");
                var longText = (string)Call(sw, "NoteShown", new string('X', 130));
                if (longText == null || longText.Length != 43 || !longText.EndsWith("...")) wrong.Add("a long first line not cut at 40: " + (longText ?? "null"));
                // A text 10 x 3.5 mm at (100, 100) mm.
                Func<double, double, double[][]> text = (x, y) => new[]
                {
                    new[] { x, y }, new[] { x + 0.010, y }, new[] { x + 0.010, y + 0.0035 }, new[] { x, y + 0.0035 },
                };
                var names = new List<string> { "the note «DETAIL A» (Drawing View5)", "the note «MATERIAL» (Sheet1)" };
                var boxes = new List<double[]> { new[] { 0.104, 0.101, 0.130, 0.106 }, new[] { 0.300, 0.020, 0.330, 0.025 } };
                double[] block = { 0.250, 0.010, 0.410, 0.060 };
                Func<double[][], double[], List<string>> covers = (t, b) =>
                    (List<string>)Call(sw, "WhatTheTextCovers", t, names, boxes, b, 0.0003);
                var onLabel = covers(text(0.100, 0.100), block);
                if (onLabel.Count != 1 || onLabel[0] != names[0]) wrong.Add("a text over the label: " + string.Join(" | ", onLabel));
                var free = covers(text(0.100, 0.150), block);
                if (free.Count != 0) wrong.Add("a free text: " + string.Join(" | ", free));
                var onBlock = covers(text(0.305, 0.021), block);
                if (onBlock.Count != 1 || onBlock[0] != "the title block") wrong.Add("a text over a box of the declared title block: " + string.Join(" | ", onBlock));
                var noBlock = covers(text(0.305, 0.021), null);
                if (noBlock.Count != 1 || noBlock[0] != names[1]) wrong.Add("a text over a box, the title block not declared: " + string.Join(" | ", noBlock));
                var all = (string)Call(sw, "OverlapsCheckedAgainst", 68, true);
                if (all == null || !all.Contains("68 notes of the sheet and of its views") || !all.Contains("as the format declares it"))
                    wrong.Add("what was compared, all read: " + (all ?? "null"));
                var broken = (string)Call(sw, "OverlapsCheckedAgainst", -1, false);
                if (broken == null || !broken.Contains("the notes NOT read") || !broken.Contains("the title block NOT as a whole"))
                    wrong.Add("what was compared, notes broken off and no title block: " + (broken ?? "null"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "4 extents, 4 notes named, 4 texts placed, 2 texts of what was compared" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void ChamferTrueSize(Type sw)
        {
            const string label = "the chamfer of 26P000011: «0,4 x 15,23°» on a chamfer 0.4 x 45° does not measure it, the right one in either order does, a chamfer read by its type gives its pairs of legs";
            try
            {
                var wrong = new List<string>();
                Func<Dictionary<string, object>, List<double[]>> pairsOf = sizes =>
                {
                    var args = new object[] { sizes, null };
                    var m = ReflectionLog.Global.Method(sw, "ChamferLegPairs", Any);
                    if (m == null) throw new MissingMethodException("SwOperations.ChamferLegPairs");
                    var r = m.Invoke(null, args) as List<double[]>;
                    if (r != null && !(args[1] is string)) wrong.Add("pairs with no words");
                    return r;
                };
                Func<Dictionary<string, object>, string> saidOf = sizes =>
                {
                    var args = new object[] { sizes, null };
                    ReflectionLog.Global.Method(sw, "ChamferLegPairs", Any).Invoke(null, args);
                    return args[1] as string;
                };
                var at45 = new Dictionary<string, object> { ["leg1_mm"] = 0.4, ["angle_deg"] = 45.0 };
                var bevel = new Dictionary<string, object> { ["leg1_mm"] = 3.0, ["leg2_mm"] = 6.0 };
                var at30 = new Dictionary<string, object> { ["leg1_mm"] = 5.0, ["angle_deg"] = 30.0 };
                var corner = new Dictionary<string, object> { ["vertex_distances_mm"] = new[] { 2.0, 3.0, 4.0 } };
                var p45 = pairsOf(at45);
                if (p45 == null || saidOf(at45) != "0.4 x 45°") wrong.Add("0.4 x 45°: " + (saidOf(at45) ?? "null"));
                var p36 = pairsOf(bevel);
                if (p36 == null || saidOf(bevel) != "3 x 6") wrong.Add("3 x 6: " + (saidOf(bevel) ?? "null"));
                var p30 = pairsOf(at30);
                if (pairsOf(corner) != null) wrong.Add("a vertex chamfer given legs");
                if (pairsOf(new Dictionary<string, object>()) != null) wrong.Add("a chamfer with no leg given legs");
                Func<double?, double?, List<double[]>, string> judge = (deg, leg, pairs) =>
                    (string)Call(sw, "ChamferNotTrueSize", "Chamfer1", "0.4 x 45°", deg, leg, pairs);
                // 26P000011, round 21: angle 15.23°, face 0.415 mm, leg 0.415 x cos 15.23° = 0.4.
                var askew = judge(15.23, 0.4, p45);
                if (askew == null || !askew.Contains("not seen in its true size") || !askew.Contains("is 0.4 x 45°") || !askew.Contains("0.4 x 15.23°"))
                    wrong.Add("the 15.23° of 26P000011: " + (askew ?? "null"));
                if (judge(45.0, 0.4, p45) != null) wrong.Add("a right 0.4 x 45° taken away");
                if (judge(45.0, 0.404, p45) != null) wrong.Add("a 0.4 x 45° read 0.404 taken away");
                if (judge(63.435, 3.0, p36) != null) wrong.Add("the 3 x 6 read from the leg 3 taken away");
                if (judge(26.565, 6.0, p36) != null) wrong.Add("the 3 x 6 read from the leg 6 taken away");
                if (judge(45.0, 3.0, p36) == null) wrong.Add("a 3 x 6 read 3 x 45° kept");
                if (judge(30.0, 5.0, p30) != null || judge(60.0, 5.0, p30) != null) wrong.Add("a 5 x 30° read from either face taken away");
                if (judge(null, 0.4, p45) != null || judge(15.23, null, p45) != null || judge(15.23, 0.4, null) != null)
                    wrong.Add("a value or the feature unknown judged");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "5 definitions, 11 readings" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING
        // =================================================================

        private const string StandardParsed = "scaleRatio = ParseScale(scaleAsked, out scaleWhy);";
        private const string StandardApplied = "string scaleProblem = ScaleNewViews(model, drawing, viewsBefore, scaleRatio, out scaleSetOn);";
        private const string StandardCreated = "try { ok = drawing.Create1stAngleViews2(swPath); }";
        private const string StandardSaid = "[\"view_scales\"] = ViewScales(drawing),";
        private const string StandardNote = "string sheetNote = SheetScaleNote(drawing, (List<Dictionary<string, object>>)measurements[\"view_scales\"], madeNow, out sheetScale);";
        private const string ModelApplied = "string scaleProblem = ApplyViewScale(model, view, scaleRatio);";
        private const string ModelSaid = "[\"scale_ratio\"] = ScaleText(ratioRead),";
        private const string SetApplied = "string problem = ApplyViewScale(model, view, ratio);";
        private const string SetNote = "string sheetNote = SheetScaleNote(drawing, scales, new[] { name }, out sheetScale);";
        private const string ReadBack = "try { read = view.ScaleDecimal; } catch { }";
        private const string SwitchCase = "case \"set_view_scale\": return ViewScale(model, arguments);";
        private const string DecimalsRead = "int? decimals = DecimalsAsked(a, out decimalsWhy);";
        private const string DecimalsBody = "try { return DimensionTheView(model, a); }";
        private const string DecimalsReset = "finally { _dimensionDecimals = -1; }";
        private const string PrecisionSet = "try { result = d.SetPrecision3(decimals, keep, keep, keep); } catch { }";
        private const string PartRoute = "return DimensionAssemblyView(model, drawing, view, viewName, edges, all, circles, null, a, contour,";
        private const string PartAsked = "string partOverall = OverallOnAPart(a, requestedList.Count > 0);";
        private const string EdgesPointer = "[\"edges_listed_by\"] = \"«inspect_part» with «view_edges»";
        private const string RoundedSaid = "[\"rounded_on_the_sheet\"] = RoundedOnTheSheet(born).ToArray(),";
        private const string WidthKind = "case \"width\": return (int)swMateType_e.swMateWIDTH;";
        private const string WidthRefused = "string widthProblem = WidthFacesProblem(width, a.ContainsKey(\"face_a2\"), a.ContainsKey(\"face_b2\"));";
        private const string WidthFirstPick = "var faceA = PickFace(compA, facingA, pointA, out string why1);";
        private const string WidthMarkA = "? Select(widthHandler, faceA, SelectionMarks.WidthMateWidthFaces) &&";
        private const string WidthMarkB = "? Select(widthHandler, faceB, SelectionMarks.WidthMateTabFaces) &&";
        private const string WidthCentred = "false, false, (int)swMateWidthOptions_e.swMateWidth_Centered, out error);";
        private const string NotesCompared = "foreach (string what in WhatTheTextCovers(t.Angles, noteNames, noteBoxes, titleBlock, margin))";
        private const string NotesRead = "var noteBoxes = NoteBoxes(drawing, out noteNames, out notesRead);";
        private const string BlockRead = "double[] titleBlock = DeclaredTitleBlock(drawing);";
        private const string ComparedSaid = "[\"overlaps_checked_against\"] = overlapsCheckedAgainst,";
        private const string ChamferRead = "if (s.ChamferFeature != null) s.ChamferPairs = ChamferPairsOf(edge, out s.ChamferSaid);";
        private const string ChamferJudged = "string notTrue = ChamferNotTrueSize(s.ChamferFeature, s.ChamferSaid,";
        private const string ChamferRetried = "done.Remove(s.ChamferFeature);";
        private const string ChamferKept = "chamfers.Add(line);";

        private static readonly string[][] Files =
        {
            new[] { "drawing", "Oce.Solid.Agent.Core", "DrawingOperations.cs" },
            new[] { "scale", "Oce.Solid.Agent.Core", "ViewScaleOperations.cs" },
            new[] { "operations", "Oce.Solid.Agent.Core", "SwOperations.cs" },
            new[] { "dimension", "Oce.Solid.Agent.Core", "DrawingDimensionOperations.cs" },
            new[] { "precision", "Oce.Solid.Agent.Core", "DimensionPrecisionOperations.cs" },
            new[] { "mates", "Oce.Solid.Agent.Core", "MateOperations.cs" },
            new[] { "texts", "Oce.Solid.Agent.Core", "DimensionTextOperations.cs" },
            new[] { "assembly", "Oce.Solid.Agent.Core", "DrawingAssemblyDimensions.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "drawing", StandardParsed, "standard_views does not read «scale» before it makes the views" },
            new[] { "drawing", StandardApplied, "standard_views does not give its views the scale asked" },
            new[] { "drawing", StandardSaid, "standard_views does not say the scale of the views" },
            new[] { "drawing", StandardNote, "standard_views does not say a view at another scale than the sheet" },
            new[] { "drawing", ModelApplied, "model_view does not give its view the scale asked" },
            new[] { "drawing", ModelSaid, "model_view does not say the scale as a ratio" },
            new[] { "scale", SetApplied, "set_view_scale does not set the scale" },
            new[] { "scale", SetNote, "set_view_scale does not say a view at another scale than the sheet" },
            new[] { "scale", ReadBack, "the scale is not read back from the view" },
            new[] { "operations", SwitchCase, "a plan cannot run «set_view_scale»" },
            new[] { "dimension", DecimalsRead, "«decimals» is not read before the dimensions are placed (point 4)" },
            new[] { "dimension", DecimalsBody, "drawing_dimension does not run with the decimals of the call (point 4)" },
            new[] { "dimension", DecimalsReset, "the decimals of one call stay for the next (point 4)" },
            new[] { "dimension", RoundedSaid, "the dimensions still rounded are not said (point 4)" },
            new[] { "dimension", PartAsked, "«overall» is not read on a view of a part (point 2)" },
            new[] { "dimension", PartRoute, "the overall size of a part does not take the route of the overall size (point 2)" },
            new[] { "dimension", EdgesPointer, "the answer does not say where the edges are listed with their geometry (point 3)" },
            new[] { "precision", PrecisionSet, "the decimals of a dimension are not set (point 4)" },
            new[] { "mates", WidthKind, "«mate» cannot make a width mate (point 7)" },
            new[] { "mates", WidthRefused, "a width mate without its second faces is not refused (point 7)" },
            new[] { "mates", WidthMarkA, "the faces of the width are not selected with their mark (point 7)" },
            new[] { "mates", WidthMarkB, "the faces of the tab are not selected with their mark (point 7)" },
            new[] { "mates", WidthCentred, "the width mate is not asked centred (point 7)" },
            new[] { "texts", NotesRead, "the notes of the sheet and of its views are not read (point 5)" },
            new[] { "texts", BlockRead, "the title block is not read (point 5)" },
            new[] { "texts", NotesCompared, "the texts are not compared with the notes and the title block (point 5)" },
            new[] { "dimension", ComparedSaid, "the answer of a part view does not say what was compared (point 5)" },
            new[] { "assembly", ComparedSaid, "the answer of an assembly view does not say what was compared (point 5)" },
            new[] { "dimension", ChamferRead, "the edges of a chamfer do not carry the legs of its feature (26P000011)" },
            new[] { "dimension", ChamferJudged, "a chamfer dimension is not compared with its feature (26P000011)" },
            new[] { "dimension", ChamferRetried, "a chamfer seen askew is not tried on another edge (26P000011)" },
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
            string drawing;
            if (sources.TryGetValue("drawing", out drawing) && drawing != null)
            {
                int parsed = drawing.IndexOf(StandardParsed, StringComparison.Ordinal);
                int created = drawing.IndexOf(StandardCreated, StringComparison.Ordinal);
                if (parsed < 0 || created < parsed) wrong.Add("standard_views reads «scale» after it has made the views");
            }
            string dimension;
            if (sources.TryGetValue("dimension", out dimension) && dimension != null)
            {
                int read = dimension.IndexOf(DecimalsRead, StringComparison.Ordinal);
                int body = dimension.IndexOf(DecimalsBody, StringComparison.Ordinal);
                if (read < 0 || body < read) wrong.Add("«decimals» is read after the dimensions are placed (point 4)");
            }
            if (dimension != null)
            {
                int judged = dimension.IndexOf(ChamferJudged, StringComparison.Ordinal);
                int kept = dimension.IndexOf(ChamferKept, StringComparison.Ordinal);
                if (judged < 0 || kept < judged) wrong.Add("a chamfer dimension is kept before it is compared with its feature (26P000011)");
            }
            string mates;
            if (sources.TryGetValue("mates", out mates) && mates != null)
            {
                int refused = mates.IndexOf(WidthRefused, StringComparison.Ordinal);
                int picked = mates.IndexOf(WidthFirstPick, StringComparison.Ordinal);
                if (refused < 0 || picked < refused) wrong.Add("the second faces of a width mate are checked after a face is picked (point 7)");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: «scale» read before the views are made, set on the new views, read back from each view, said with the scale of the sheet; set_view_scale runnable; «decimals» read first and put back after the call, the decimals set, the rounded dimensions said; the width mate refused before anything is picked, its faces marked, centred; the texts compared with the notes and the title block, and what was compared said; a chamfer dimension compared with its feature and tried on another edge";
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
            mutate("«scale» read after the views are made", "drawing",
                   t => t.Replace(StandardParsed, "/* moved */").Replace(StandardCreated, StandardCreated + "\n" + StandardParsed));
            mutate("«decimals» read after the dimensions are placed", "dimension",
                   t => t.Replace(DecimalsRead, "/* moved */").Replace(DecimalsBody, DecimalsBody + "\n" + DecimalsRead));
            mutate("a chamfer dimension kept before it is compared", "dimension",
                   t => t.Replace(ChamferJudged, "string notTrue = null; string unused = ChamferNotTrueSize(s.ChamferFeature, s.ChamferSaid,"));
            mutate("the second faces of a width mate checked after a face is picked", "mates",
                   t => t.Replace(WidthRefused, "/* moved */").Replace(WidthFirstPick, WidthFirstPick + "\n" + WidthRefused));

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : Lines.Length + " lines, three orders; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
