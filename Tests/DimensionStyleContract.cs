using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE OWNER'S STYLE OF DIMENSIONING, CHECKED ON HIS OWN NUMBERS (31/10/2026).
    ///
    /// The layout of «drawing_dimension» with «between» (DrawingDimensionStyle.cs) was read
    /// from four of his drawings: 51 linear dimensions, 41 placed by his hand. Their numbers
    /// are in Tests/data/owner-dimension-layout.tsv (made by stile_quote.py of the internal
    /// notes, which laid them out again with the same rules: within 5 mm of his text 15 of
    /// 51 and 13 of the 41 of his hand; bridge 0.160.4 put 2 of the 41 there). «bridge» in
    /// the data is a dimension born from the bridge of 28/10 (its name and text in the
    /// bridge's answers) still where the bridge put it; until 02/11/2026 the distance alone
    /// (17.94 mm) decided, and five of his own on 26P000008, lined up on the bridge's row,
    /// counted as the bridge's. 7 of the 51 are one too many on their drawing: 5 dimension an
    /// extent already dimensioned, 2 close a chain (stile_quote.py 1.4.0, its mode for repeated
    /// dimensions). They are laid out as he left them, which says where such a text goes, not
    /// that the dimension belongs there. Here the
    /// library lays them out again and must do at least as well, so a change that loses his
    /// places is seen without SOLIDWORKS. Text widths are estimated as in the program, 1.8 mm
    /// a digit and 1 mm a comma: the bridge reads the true ones from SOLIDWORKS.
    ///
    /// Then the pieces: the side of the edges, the style file, the row and the stair, the
    /// label of a detail, and the wiring on the sources, every rule first red on a changed copy.
    /// On library 0.188.0 every check is red: LayOutDimensions does not exist.
    /// </summary>
    internal static class DimensionStyleContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the owner's style of dimensioning: his drawings laid out again, the side of the edges, the style file, the row and the stair, the label of a detail");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the owner's style", library + " is missing: build the library first (build.ps1)");
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

            OwnerDrawings(sw);
            Sides(sw);
            StyleFile(sw);
            RowAndStair(sw);
            SheetFrame(sw);
            OverallOnATie(sw);
            DetailLabel(sw);
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

        /// <summary>The built-in style, or the one of <paramref name="json"/>.</summary>
        private static object Style(Type sw, string json)
        {
            var args = new object[] { json, "contract", null };
            var m = ReflectionLog.Global.Method(sw, "StyleFrom", Any);
            if (m == null) throw new MissingMethodException("SwOperations.StyleFrom");
            var style = m.Invoke(null, args);
            if (style == null) throw new InvalidOperationException("the style was refused: " + args[2]);
            return style;
        }

        private static double TextWidth(string text)
        {
            return text.Sum(c => c == ',' || c == '.' ? 1.0 : 1.8) / 1000.0;
        }

        // =================================================================
        // HIS DRAWINGS LAID OUT AGAIN
        // =================================================================

        private static void OwnerDrawings(Type sw)
        {
            const string label = "the owner's 4 drawings laid out again: at least 17 of his 51 dimensions and 15 of the 41 of his hand within 5 mm of his text (17 and 15 since library 0.191.0: chains in one row, a text outside beyond the end that leaves its neighbours room; 15 and 13 with 0.190.0; bridge 0.160.4 put 2 of the 41 there), and the numbers of the bridge before stay under it";
            try
            {
                var path = Path.Combine(Root(), "Tests", "data", "owner-dimension-layout.tsv");
                var rows = File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').Skip(1)
                               .Select(l => l.Split('\t')).ToArray();
                if (rows.Length != 51) { Check(label, false, rows.Length + " rows in " + path + " instead of 51"); return; }
                Func<string[], int, double> mm = (r, i) => double.Parse(r[i], CultureInfo.InvariantCulture) / 1000.0;
                Func<object, int[]> count = style =>
                {
                    int within5 = 0, within2 = 0, hand = 0, hand5 = 0;
                    foreach (var view in rows.GroupBy(r => r[0] + "|" + r[1]))
                    {
                        var v = view.ToArray();
                        var dims = v.Select(r => new[]
                        {
                            double.Parse(r[2], CultureInfo.InvariantCulture), mm(r, 3), mm(r, 4), mm(r, 5), mm(r, 6),
                            TextWidth(r[12]), double.Parse(r[11], CultureInfo.InvariantCulture), 0.0,
                        }).ToArray();
                        var drawn = new[] { mm(v[0], 7), mm(v[0], 8), mm(v[0], 9), mm(v[0], 10) };
                        var places = (double[][])Call(sw, "LayOutDimensions", dims, drawn, style, false, null, null);
                        for (int i = 0; i < v.Length; i++)
                        {
                            double gap = Math.Sqrt(Math.Pow(places[i][0] - mm(v[i], 13), 2) + Math.Pow(places[i][1] - mm(v[i], 14), 2)) * 1000;
                            bool byHand = v[i][16] == "hand";
                            if (gap <= 5) within5++;
                            if (gap <= 2) within2++;
                            if (byHand) { hand++; if (gap <= 5) hand5++; }
                        }
                    }
                    return new[] { within5, within2, hand, hand5 };
                };
                var his = count(Style(sw, null));
                // THE CHECK MUST BE ABLE TO GO RED. The numbers of the bridge before 0.161.0
                // (12 mm from the outline = 17.94 mm from the lines, 10 mm of pitch, 3.5 mm for
                // an arrow, below and on the right, one row each) laid out by the same function
                // must stay under the threshold: if they reached it, the threshold would say nothing.
                var before = count(Style(sw, "{\"first_row_mm\": 17.94, \"pitch_mm\": 10, \"arrow_room_mm\": 3.5, " +
                                         "\"side_by_edges\": false, \"pack\": false, \"tie_horizontal\": \"below\", \"tie_vertical\": \"right\"}"));
                bool red = before[0] < 17 || before[3] < 15;
                Check(label, his[0] >= 17 && his[3] >= 15 && his[2] == 41 && red,
                      "within 5 mm " + his[0] + " of 51 (within 2 mm " + his[1] + "), of his hand " + his[3] + " of " + his[2] +
                      "; with the numbers before 0.161.0 " + before[0] + " of 51 and " + before[3] + " of " + before[2] +
                      (red ? ", red" : ", NOT red: the threshold does not tell the two layouts apart"));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE PIECES
        // =================================================================

        private static void Sides(Type sw)
        {
            const string label = "the side of the edges: the side of the lines the dimensioned edges come nearer to, the tie word within half a millimetre";
            try
            {
                var wrong = new List<string>();
                var drawn = new[] { 0.010, 0.020, 0.110, 0.060 };   // x 10..110, y 20..60 mm
                Func<bool, double, double, string> side = (h, lo, hi) => (string)Call(sw, "SideOfTheEdges", h, lo, hi, drawn, "TIE");
                if (side(true, 0.020, 0.040) != "below") wrong.Add("edges from the bottom to the middle: " + side(true, 0.020, 0.040));
                if (side(true, 0.045, 0.060) != "above") wrong.Add("edges from above the middle to the top: " + side(true, 0.045, 0.060));
                if (side(true, 0.020, 0.060) != "TIE") wrong.Add("edges over the whole height: " + side(true, 0.020, 0.060));
                if (side(true, 0.0203, 0.0599) != "TIE") wrong.Add("0.3 mm against 0.1 mm: " + side(true, 0.0203, 0.0599));
                if (side(false, 0.010, 0.030) != "left") wrong.Add("vertical, edges from the left: " + side(false, 0.010, 0.030));
                if (side(false, 0.080, 0.110) != "right") wrong.Add("vertical, edges to the right: " + side(false, 0.080, 0.110));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "6 cases" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void StyleFile(Type sw)
        {
            const string label = "the style file: its numbers over the built-in ones, a key it does not take, a number out of range, a word it does not take and a text that is not JSON refused with the reason";
            try
            {
                var wrong = new List<string>();
                var m = ReflectionLog.Global.Method(sw, "StyleFrom", Any);
                if (m == null) throw new MissingMethodException("SwOperations.StyleFrom");
                Func<string, string> refusal = json =>
                {
                    var args = new object[] { json, "contract", null };
                    return m.Invoke(null, args) == null ? (string)args[2] ?? "(no reason)" : null;
                };
                var builtIn = Style(sw, null);
                var describe = builtIn.GetType().GetMethod("Describe", BindingFlags.Instance | BindingFlags.NonPublic);
                var values = (Dictionary<string, object>)describe.Invoke(builtIn, null);
                var expected = new Dictionary<string, object>
                {
                    ["first_row_mm"] = 10.0, ["pitch_mm"] = 6.7, ["text_outside_mm"] = 11.0, ["arrow_room_mm"] = 2.0,
                    ["detail_label_gap_mm"] = 6.2, ["side_by_edges"] = true, ["pack"] = true,
                    ["tie_horizontal"] = "above", ["tie_vertical"] = "right",
                };
                foreach (var e in expected)
                    if (!values.ContainsKey(e.Key) || !Equals(values[e.Key], e.Value))
                        wrong.Add("built in, " + e.Key + " is " + (values.ContainsKey(e.Key) ? values[e.Key] : "missing") + " instead of " + e.Value);
                var changed = Style(sw, "{\"first_row_mm\": 8, \"tie_horizontal\": \"below\", \"pack\": false}");
                var read = (Dictionary<string, object>)describe.Invoke(changed, null);
                if (!Equals(read["first_row_mm"], 8.0) || (string)read["tie_horizontal"] != "below" || !Equals(read["pack"], false) || !Equals(read["pitch_mm"], 6.7))
                    wrong.Add("a file with three keys reads " + string.Join(", ", read.Select(p => p.Key + "=" + p.Value)));
                if (!(refusal("{\"first_row\": 8}") ?? "").Contains("is not a key of the style")) wrong.Add("an unknown key: " + refusal("{\"first_row\": 8}"));
                if (!(refusal("{\"pitch_mm\": 0}") ?? "").Contains("outside 1 to 40")) wrong.Add("a pitch of 0: " + refusal("{\"pitch_mm\": 0}"));
                if (!(refusal("{\"pitch_mm\": \"6\"}") ?? "").Contains("is a number of millimetres")) wrong.Add("a pitch in quotes: " + refusal("{\"pitch_mm\": \"6\"}"));
                if (!(refusal("{\"tie_vertical\": \"up\"}") ?? "").Contains("is «right» or «left»")) wrong.Add("a tie «up»: " + refusal("{\"tie_vertical\": \"up\"}"));
                if (!(refusal("first_row_mm = 8") ?? "").Contains("it is not JSON")) wrong.Add("not JSON: " + refusal("first_row_mm = 8"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "9 built-in values, a file of three keys, 5 refusals" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void RowAndStair(Type sw)
        {
            const string label = "the row and the stair: dimensions that only touch share a row (19 / 145 / 11), one that overlaps goes a pitch further, «row» keeps all in the first, without «pack» one row each; a text that does not fit goes 11 mm beyond the higher end";
            try
            {
                var wrong = new List<string>();
                var drawn = new[] { 0.0364, 0.22306, 0.2114, 0.22887 };   // 26P000008, view 1
                Func<double, double, double, double[]> below = (lo, hi, value) => new[] { 1.0, lo, hi, 0.22306, 0.2280, 0.004, value, 1.0 };
                var chain = new[] { below(0.0554, 0.2004, 145), below(0.0364, 0.0554, 19), below(0.2004, 0.2114, 11), below(0.0364, 0.2114, 175) };
                var builtIn = Style(sw, null);
                var p = (double[][])Call(sw, "LayOutDimensions", chain, drawn, builtIn, false, null, null);
                if (p[0][4] != 0 || p[1][4] != 0 || p[2][4] != 0) wrong.Add("the chain 19 / 145 / 11 in rows " + p[1][4] + ", " + p[0][4] + ", " + p[2][4]);
                if (p[3][4] != 1) wrong.Add("the overall 175 in row " + p[3][4] + " instead of 1");
                if (Math.Abs(p[0][1] - (0.22306 - 0.010)) > 1e-9) wrong.Add("the first row at y " + p[0][1] * 1000 + " mm instead of 10 mm under the lines");
                if (Math.Abs(p[3][1] - (0.22306 - 0.0167)) > 1e-9) wrong.Add("the second row at y " + p[3][1] * 1000 + " mm instead of 16.7 mm under the lines");
                var inRow = (double[][])Call(sw, "LayOutDimensions", chain, drawn, builtIn, true, null, null);
                if (inRow.Any(r => r[4] != 0)) wrong.Add("«row» puts some in rows " + string.Join(", ", inRow.Select(r => r[4])));
                var stair = (double[][])Call(sw, "LayOutDimensions", chain, drawn, Style(sw, "{\"pack\": false}"), false, null, null);
                if (stair.Select(r => r[4]).Distinct().Count() != 4) wrong.Add("without «pack» rows " + string.Join(", ", stair.Select(r => r[4])));
                var small = (double[][])Call(sw, "LayOutDimensions", new[] { new[] { 1.0, 0.0554, 0.0569, 0.22306, 0.2280, 0.005, 1.5, 1.0 } }, drawn, builtIn, false, null, null);
                if (small[0][2] != 0 || Math.Abs(small[0][0] - (0.0569 + 0.011)) > 1e-9) wrong.Add("a 1,5 of 1.5 mm reads text inside " + small[0][2] + " at x " + small[0][0] * 1000);
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "a chain, an overall, a row, a stair, a text outside" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void SheetFrame(Type sw)
        {
            const string label = "the frame of the sheet: a side not asked whose first row would put the text over the 12 mm of the frame turns to the other (26P000007 view 2, «offset_mm» 30: the 1,8 at x 6.5 mm with bridge 0.167.0, 02/11/2026); 20 mm out it stays";
            try
            {
                var wrong = new List<string>();
                var drawn = new[] { 0.0365, 0.10634, 0.2115, 0.11506 };   // 26P000007, view 2, from the rows read back
                var sheet = new[] { 0.42, 0.297 };
                // The 1,8: a vertical distance whose edges reach the left end of the view.
                var dims = new[] { new[] { 0.0, 0.10634, 0.10814, 0.0365, 0.0400, 0.00905, 1.8, 0.0, 0.0046, 0.0035 } };
                var far = (double[][])Call(sw, "LayOutDimensions", dims, drawn, Style(sw, "{\"first_row_mm\": 30}"), false, sheet, null);
                if (far[0][3] < 0.5 || Math.Abs(far[0][0] - 0.2415) > 1e-9)
                    wrong.Add("30 mm out the 1,8 reads " + (far[0][3] > 0.5 ? "right" : "left") + " at x " + (far[0][0] * 1000).ToString("0.##", CultureInfo.InvariantCulture) + " mm");
                var near = (double[][])Call(sw, "LayOutDimensions", dims, drawn, Style(sw, "{\"first_row_mm\": 20}"), false, sheet, null);
                if (near[0][3] > 0.5 || Math.Abs(near[0][0] - 0.0165) > 1e-9)
                    wrong.Add("20 mm out the 1,8 reads " + (near[0][3] > 0.5 ? "right" : "left") + " at x " + (near[0][0] * 1000).ToString("0.##", CultureInfo.InvariantCulture) + " mm");
                var unknown = (double[][])Call(sw, "LayOutDimensions", dims, drawn, Style(sw, "{\"first_row_mm\": 30}"), false, null, null);
                if (unknown[0][3] > 0.5) wrong.Add("without the sheet the 1,8 turns right");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "turned right at x 241.5 mm, left at x 16.5 mm, left without the sheet" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void OverallOnATie(Type sw)
        {
            const string label = "an overall horizontal dimension whose edges reach both sides as near goes on the side the tie does not take, the partial ones on the tie side: the owner's 193,6 below and 35, 135 above on 26M000002 view 1, his 15,6 below and 11,6, 2 above on 26P000009 view 2 (note 98: the bridge put the 193,6 above)";
            try
            {
                var wrong = new List<string>();
                var builtIn = Style(sw, null);
                Func<double[], double[][], double[][]> lay = (drawn, dims) => (double[][])Call(sw, "LayOutDimensions", dims, drawn, builtIn, false, null, null);
                Func<double[], string> side = r => r[3] > 0.5 ? "above" : "below";
                // 26M000002 view 1 (owner-dimension-layout.tsv): 193,6 overall, 35 and 135 from the left end.
                var m2 = lay(new[] { 0.0461, 0.22519, 0.1429, 0.23369 }, new[]
                {
                    new[] { 1.0, 0.0461, 0.1429, 0.22675, 0.2319, 0.0103, 193.6, 0.0 },
                    new[] { 1.0, 0.0461, 0.0636, 0.22558, 0.23338, 0.0052, 35, 0.0 },
                    new[] { 1.0, 0.0461, 0.1136, 0.22558, 0.23338, 0.0078, 135, 0.0 },
                });
                if (side(m2[0]) != "below") wrong.Add("26M000002: the 193,6 " + side(m2[0]));
                if (side(m2[1]) != "above" || side(m2[2]) != "above") wrong.Add("26M000002: the 35 " + side(m2[1]) + ", the 135 " + side(m2[2]));
                // 26P000009 view 2: 15,6 overall, 11,6 and 2 inside it.
                var p9 = lay(new[] { 0.0555, 0.07874, 0.1335, 0.14274 }, new[]
                {
                    new[] { 1.0, 0.0555, 0.1335, 0.07873, 0.14273, 0.0078, 15.6, 0.0 },
                    new[] { 1.0, 0.0655, 0.1235, 0.08873, 0.13273, 0.0078, 11.6, 0.0 },
                    new[] { 1.0, 0.1235, 0.1335, 0.07873, 0.14273, 0.0026, 2, 0.0 },
                });
                if (side(p9[0]) != "below") wrong.Add("26P000009: the 15,6 " + side(p9[0]));
                if (side(p9[1]) != "above" || side(p9[2]) != "above") wrong.Add("26P000009: the 11,6 " + side(p9[1]) + ", the 2 " + side(p9[2]));
                // A side asked wins: the 193,6 asked above stays above.
                var asked = lay(new[] { 0.0461, 0.22519, 0.1429, 0.23369 }, new[] { new[] { 1.0, 0.0461, 0.1429, 0.22675, 0.2319, 0.0103, 193.6, 2.0 } });
                if (side(asked[0]) != "above") wrong.Add("the 193,6 asked above reads " + side(asked[0]));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "193,6 and 15,6 below, 35, 135, 11,6 and 2 above, a side asked kept" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void DetailLabel(Type sw)
        {
            const string label = "the label of a detail: 6.2 mm under the text of its lowest dimension when that is lower than the label (26P000008 A: 213.96 mm automatic, 1,5 at 212.13 mm, the owner's label at 205.92 mm); left where it is otherwise";
            try
            {
                var wrong = new List<string>();
                var y = (double?)Call(sw, "DetailLabelY", 0.21396, 0.21213, 6.2);
                if (y == null || Math.Abs(y.Value - 0.20593) > 0.0001) wrong.Add("26P000008 A reads " + (y == null ? "no move" : (y.Value * 1000).ToString("0.##", CultureInfo.InvariantCulture) + " mm"));
                if (Call(sw, "DetailLabelY", 0.200, 0.21213, 6.2) != null) wrong.Add("a label already lower is moved");
                if (Call(sw, "DetailLabelY", 0.21396, double.NaN, 6.2) != null) wrong.Add("a detail with no dimension under it moves its label");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "3 cases" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING
        // =================================================================

        private const string StyleRead = "DimensionStyle style = StyleOfThisCall(out styleWhy);";
        private const string StyleRefused = "if (style == null)";
        private const string FirstBorn = "Dictionary<string, object> placed = PlaceBetween(model, drawing, edges, first, second, true,";
        private const string Drawn = "double[] drawn = DrawnBox(view);";
        private const string Laid = "double[][] places = LayOutDimensions(toLay, drawn, style, row, SheetDimensions(drawing), inTheView);";
        private const string Moved = "if (annotation != null) annotation.SetPosition2(at[0], at[1], 0);";
        private const string Label = "? MoveDetailLabel(view, lowestUnder, style.DetailLabelGapMm) : null;";
        private const string Said = "[\"style\"] = style.Describe(),";
        private const string OffsetNamed = "if (offsetNamed) style.FirstRowMm = dimensionOffset * 1000;";

        private static readonly string[][] Lines =
        {
            new[] { StyleRead, "the style is not read" },
            new[] { StyleRefused, "a style that cannot be used is not refused" },
            new[] { OffsetNamed, "«offset_mm» of the call does not win over the style" },
            new[] { Drawn, "the dimensions are measured from the outline, with its margin, not from the lines of the view" },
            new[] { Laid, "the owner's layout is not used" },
            new[] { Moved, "a dimension is not moved where the layout puts it" },
            new[] { Label, "the label of a detail is not moved under its dimensions" },
            new[] { Said, "the answer does not say the style it used" },
        };

        private static List<string> WiringProblems(string text)
        {
            var wrong = new List<string>();
            if (text == null) { wrong.Add("DrawingBetweenDimensions.cs not found"); return wrong; }
            foreach (var line in Lines)
                if (!text.Contains(line[0])) wrong.Add(line[1]);
            int a = text.IndexOf(StyleRead, StringComparison.Ordinal), b = text.IndexOf(FirstBorn, StringComparison.Ordinal);
            if (a < 0 || b < a) wrong.Add("the style is read after a dimension is placed: a bad file would leave a drawing half done");
            int c = text.IndexOf(Laid, StringComparison.Ordinal);
            if (c < 0 || c < b) wrong.Add("the layout comes before the dimensions are born, when the widths of their texts cannot be read");
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: the style read and checked before anything is placed, «offset_mm» winning over it, the dimensions measured from the lines of the view, laid out after they are born, moved there, the label of a detail moved, the style said in the answer";
            var path = Path.Combine(Root(), "Oce.Solid.Agent.Core", "DrawingBetweenDimensions.cs");
            string source = File.Exists(path) ? File.ReadAllText(path) : null;
            var real = WiringProblems(source);
            var blind = new List<string>();
            int copies = 0;
            Action<string, Func<string, string>> mutate = (what, change) =>
            {
                if (source == null) return;
                var changed = change(source);
                if (changed == source) { blind.Add(what + " (the copy did not change)"); return; }
                copies++;
                if (WiringProblems(changed).Count == 0) blind.Add(what);
            };
            foreach (var line in Lines)
            {
                var anchor = line[0];
                mutate("without «" + anchor + "»", t => t.Replace(anchor, "/* gone */"));
            }
            mutate("the style read after the first dimension", t => t.Replace(StyleRead, "/* moved */").Replace(FirstBorn, FirstBorn + "\n" + StyleRead));
            mutate("the layout before the dimensions are born", t => t.Replace(Laid, "/* moved */").Replace(FirstBorn, Laid + "\n" + FirstBorn));
            bool ok = real.Count == 0 && blind.Count == 0;
            Check(label, ok, real.Count > 0 ? string.Join("; ", real)
                           : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                           : Lines.Length + " lines, two orders; " + copies + " changed copies all red");
        }
    }
}
