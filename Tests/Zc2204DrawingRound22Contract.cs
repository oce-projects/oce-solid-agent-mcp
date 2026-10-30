using System;
using System.Collections.Generic;
using System.IO;
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
            const string label = "point 9: a balloon put again goes out of the outline of its view, on the side of its arrow, 10 mm beyond the edge it crosses; one on the centre goes up";
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
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "4 arrows" : string.Join("; ", wrong));
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
        private const string DecorationReset = "finally { _dimensionPrefix = null; _dimensionSuffix = null; _dimensionParentheses = false; }";
        private const string BetweenShown = "described[\"text_shown\"] = ExactTextShown(d, described);";
        private const string BetweenDecorated = "DecorateDimension(d, described);";
        private const string BetweenArrows = "ArrowsByText(d, described, first.FromSheet[k], second.FromSheet[k], horizontal);";
        private const string BetweenReadBack = "if (d != null && (!(mm is double) || Math.Abs((double)mm - expectedMm) > 0.05))";
        private const string SectionParsed = "sectionRatio = ParseScale(Str(a, \"scale\", null), out scaleWhy);";
        private const string SectionScaled = "string sectionScaleProblem = ApplyViewScale(model, sectionView, sectionRatio);";
        private const string SectionPlaced = "if (!SectionInAFreePlace(drawing, sectionView, facing, out sectionWhere, out sectionPlaceProblem))";
        private const string DetailStyled = "(int)circleStyle, scale1, 1.0, label,";
        private const string DetailLocked = "if (lockPosition) try { detailCircle.PinPosition = true; } catch { }";
        private const string BalloonsRoute = "return BalloonsAgain(model, drawing, view);";
        private const string PickedChecked = "if (BomKeyOf(picked) != key)";
        private const string BalloonBorn = "born = model.Extension.InsertBOMBalloon2(options) as INote;";
        private const string BlockWidth = "table.SetColumnWidth(c, table.GetColumnWidth(c) * factor,";
        private const string BlockSaid = "measurements[\"table_location\"] = \"over the title block\";";
        private const string MoveCase = "case \"move_annotation\": return MoveAnnotation(model, arguments);";
        private const string MoveChecked = "string moveWhy = MoveProblem(";
        private const string MoveDone = "try { annotation.SetPosition2(x, y, 0); }";
        private const string MoveReadBack = "if (after == null || after.Length < 2 || Math.Abs(after[0] - x) > 0.0001";
        private const string ReadCase = "case \"read_drawing\": return ReadDrawing(model, arguments);";
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

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : Lines.Length + " lines, five orders; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
