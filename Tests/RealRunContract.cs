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
    /// THE REAL RUN OF 02/11/2026 REPLAYED (note 100).
    ///
    /// The owner's four drawings of 26M000002 were made again on SOLIDWORKS with bridge 0.161.0
    /// and no hand move: 10 of 52 texts within 5 mm of his, where the layout checked on numbers
    /// alone (DimensionStyleContract) promised 15. The numbers had missed what SOLIDWORKS does:
    /// one call at a time (a second call on a view wrote over the first), the box of a text
    /// wider than its digits (the 11, 9,5 and 10,9 went outside), the label of a detail that
    /// looked only at its own call.
    ///
    /// Here the readings of that run (Tests/data/real-run-1102.tsv, made by stile_quote.py 1.5.0
    /// «reale» from the answers of drawing_dimension and view_edges of the same views) are
    /// replayed without SOLIDWORKS. First the rules of 0.161.0, copied below, must give back
    /// the places SOLIDWORKS read: that proves the replay is the run. Then the library lays the
    /// same calls out in the same order, each seeing what the earlier ones left in the view,
    /// and is held against the owner's drawings: more texts within 5 mm than the run, none
    /// over another, the choice inside or outside his more often. On library 0.190.0 every
    /// check but the faithful replay is red: LayOutDimensions takes no dimensions already there.
    /// </summary>
    internal static class RealRunContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        /// <summary>
        /// The floors of the replay with the built-in style, library 0.191.0 (02/11/2026): 12 of 51 within
        /// 5 mm (the run 10), 10 of his 41, the choice inside or outside his in 46 of 51 (the run 40), his
        /// side 28 times (the run 28). Raise them when the layout improves. Library 0.196.1: 14 of 51 and 12
        /// of his 41 (the overall horizontal one on a tie on the other side: the 193,6 below, and the 35
        /// and the 135 a row nearer above).
        /// </summary>
        private const int FloorWithin5 = 14, FloorHandWithin5 = 12, FloorInsideAsHe = 46, FloorHandSides = 28;

        private sealed class Dim
        {
            public string Drawing, View, Kind, Text, BridgeSide, BridgeText, OwnerSide, PlacedBy;
            public int Call;
            public bool Horizontal, HasOwner;
            public double Low, High, EndsLow, EndsHigh, Value, BridgeX, BridgeY, OwnerX, OwnerY;
            public double[] Drawn, Lines, Sheet;
        }

        internal static void RunAll()
        {
            Group("the real run of 02/11/2026 replayed: the places SOLIDWORKS read back, the calls in their order, the dimensions already in the view, the texts inside as the owner writes them, the label of a detail");

            Wiring();

            List<Dim> dims;
            var path = Path.Combine(Root(), "Tests", "data", "real-run-1102.tsv");
            try { dims = Read(path); }
            catch (Exception ex) { Check("the readings of the real run are read", false, path + ": " + Why(ex)); return; }
            Faithful(dims);

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the replay with the library", library + " is missing: build the library first (build.ps1)");
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

            Replay(sw, dims);
            DetailLabel(sw);
            DetailOnTheEdge(sw);
            TitleBlockOfTheFormat(sw);
            FormatLinesInOneCall(sw);
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

        private static List<Dim> Read(string path)
        {
            var lines = File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#').ToArray();
            var head = lines[0].Split('\t');
            Func<string, int> at = name => { int i = Array.IndexOf(head, name); if (i < 0) throw new InvalidDataException("no column " + name); return i; };
            Func<string, double> num = s => double.Parse(s, CultureInfo.InvariantCulture);
            var outside = new List<Dim>();
            foreach (var line in lines.Skip(1))
            {
                var r = line.Split('\t');
                var d = new Dim
                {
                    Drawing = r[at("drawing")], View = r[at("view")], Call = int.Parse(r[at("call")], CultureInfo.InvariantCulture),
                    Kind = r[at("kind")], Text = r[at("text")], Value = num(r[at("value_mm")]),
                    Drawn = new[] { num(r[at("drawn_x0_mm")]) / 1000, num(r[at("drawn_y0_mm")]) / 1000, num(r[at("drawn_x1_mm")]) / 1000, num(r[at("drawn_y1_mm")]) / 1000 },
                    Lines = new[] { num(r[at("lines_x0_mm")]) / 1000, num(r[at("lines_y0_mm")]) / 1000, num(r[at("lines_x1_mm")]) / 1000, num(r[at("lines_y1_mm")]) / 1000 },
                    Sheet = new[] { num(r[at("sheet_w_mm")]) / 1000, num(r[at("sheet_h_mm")]) / 1000 },
                    BridgeX = num(r[at("bridge_x_mm")]) / 1000, BridgeY = num(r[at("bridge_y_mm")]) / 1000,
                    BridgeSide = r[at("bridge_side")], BridgeText = r[at("bridge_text")],
                    OwnerSide = r[at("owner_side")], PlacedBy = r[at("placed_by")],
                };
                if (d.Kind == "between")
                {
                    d.Horizontal = r[at("horizontal")] == "1";
                    d.Low = num(r[at("low_mm")]) / 1000; d.High = num(r[at("high_mm")]) / 1000;
                    d.EndsLow = num(r[at("ends_low_mm")]) / 1000; d.EndsHigh = num(r[at("ends_high_mm")]) / 1000;
                }
                d.HasOwner = r[at("owner_x_mm")].Length > 0;
                if (d.HasOwner) { d.OwnerX = num(r[at("owner_x_mm")]) / 1000; d.OwnerY = num(r[at("owner_y_mm")]) / 1000; }
                outside.Add(d);
            }
            return outside;
        }

        /// <summary>
        /// The box SOLIDWORKS reads for a text 3.5 mm high (GetTextInBoxWidthAtIndex), metres: 2.58 mm
        /// a digit and 2.6 mm of margin (probe0089: «10» 7.76 mm, «120» 10.34 mm), 1.1 mm a comma
        /// (the choices of the run put it between 0.74 and 1.44 mm). The same as the frame width
        /// of stile_quote.py.
        /// </summary>
        private static double Box(string text)
        {
            return (text.Sum(c => c == ',' || c == '.' ? 1.1 : 2.58) + 2.6) / 1000.0;
        }

        private static double Mm(double metres) { return Math.Round(metres * 1000, 2); }

        // =================================================================
        // THE RULES OF 0.161.0 GIVE BACK WHAT SOLIDWORKS READ
        // =================================================================

        /// <summary>
        /// THE LAYOUT OF BRIDGE 0.161.0 (library 0.189.0, DrawingDimensionStyle.cs of that version)
        /// with its built-in style: one call at a time, nothing else of the view seen, the box of the
        /// text for the choice inside or outside. Rows as LayOutDimensions takes them; back {x, y,
        /// inside}. Kept here as the reference the run is checked against.
        /// </summary>
        private static double[][] Old161(double[][] dims, double[] drawn, double[] sheet)
        {
            const double first = 0.010, pitch = 0.0067, outside = 0.011, arrow = 0.002, touch = 0.00005;
            var result = new double[dims.Length][];
            var sides = new string[dims.Length];
            var inside = new bool[dims.Length];
            var along = new double[dims.Length];
            var taken = new double[dims.Length][];
            for (int i = 0; i < dims.Length; i++)
            {
                double[] d = dims[i];
                bool horizontal = d[0] > 0.5;
                double low = Math.Min(d[1], d[2]), high = Math.Max(d[1], d[2]), width = d[5];
                string tie = horizontal ? "above" : "right";
                double lowGap = d[3] - (horizontal ? drawn[1] : drawn[0]), highGap = (horizontal ? drawn[3] : drawn[2]) - d[4];
                string side = Math.Abs(lowGap - highGap) < 0.0005 ? tie
                            : lowGap < highGap ? (horizontal ? "below" : "left") : (horizontal ? "above" : "right");
                bool lowSide = side == "below" || side == "left";
                double at = horizontal ? (lowSide ? drawn[1] - first : drawn[3] + first) : (lowSide ? drawn[0] - first : drawn[2] + first);
                double limit = horizontal ? sheet[1] : sheet[0];
                if (at < 0.005 || at > limit - 0.005) side = horizontal ? (lowSide ? "above" : "below") : (lowSide ? "right" : "left");
                sides[i] = side;
                inside[i] = width > 0 && width + 2 * arrow <= high - low;
                double half = width > 0 ? width / 2 : 0;
                if (inside[i]) { along[i] = (low + high) / 2; taken[i] = new[] { low, high }; }
                else { along[i] = high + outside; taken[i] = new[] { low, high + outside + half }; }
            }
            var rowOf = new int[dims.Length];
            var placed = new bool[dims.Length];
            foreach (var group in Enumerable.Range(0, dims.Length).GroupBy(i => (dims[i][0] > 0.5 ? "h" : "v") + sides[i]))
            {
                foreach (int i in group.OrderBy(i => inside[i] ? 1 : 0).ThenBy(i => dims[i][6]))
                {
                    int r = 0;
                    while (group.Any(j => placed[j] && rowOf[j] == r && taken[i][0] < taken[j][1] - touch && taken[j][0] + touch < taken[i][1])) r++;
                    rowOf[i] = r;
                    placed[i] = true;
                    double distance = first + r * pitch;
                    bool horizontal = dims[i][0] > 0.5, lowSide = sides[i] == "below" || sides[i] == "left";
                    double across = horizontal ? (lowSide ? drawn[1] - distance : drawn[3] + distance)
                                               : (lowSide ? drawn[0] - distance : drawn[2] + distance);
                    result[i] = horizontal ? new[] { along[i], across, inside[i] ? 1.0 : 0.0, lowSide ? 0.0 : 1.0 }
                                           : new[] { across, along[i], inside[i] ? 1.0 : 0.0, lowSide ? 0.0 : 1.0 };
                }
            }
            return result;
        }

        private static double[] Row(Dim d, double choice)
        {
            return new[] { d.Horizontal ? 1.0 : 0.0, d.Low, d.High, d.EndsLow, d.EndsHigh, Box(d.Text), d.Value, 0.0, choice, 0.0035 };
        }

        private static IEnumerable<IGrouping<string, Dim>> Calls(List<Dim> dims)
        {
            return dims.GroupBy(d => d.Drawing + "|" + d.Call);
        }

        private static void Faithful(List<Dim> dims)
        {
            const string label = "the replay is the run: the rules of 0.161.0, one call at a time with the box SOLIDWORKS reads, give back every place SOLIDWORKS read for the 51 «between» of the four drawings, within 0.15 mm, and their choice inside or outside";
            try
            {
                var wrong = new List<string>();
                int same = 0, total = 0;
                foreach (var call in Calls(dims))
                {
                    var b = call.Where(d => d.Kind == "between").ToArray();
                    if (b.Length == 0) continue;
                    var p = Old161(b.Select(d => Row(d, 0)).ToArray(), b[0].Drawn, b[0].Sheet);
                    for (int i = 0; i < b.Length; i++)
                    {
                        total++;
                        double gap = Math.Sqrt(Math.Pow(p[i][0] - b[i].BridgeX, 2) + Math.Pow(p[i][1] - b[i].BridgeY, 2));
                        bool insideSame = (p[i][2] > 0.5) == (b[i].BridgeText == "inside");
                        if (gap <= 0.00015 && insideSame) same++;
                        else wrong.Add(b[i].Drawing + " " + b[i].View + " «" + b[i].Text + "» replayed at " + Mm(p[i][0]) + ", " + Mm(p[i][1]) +
                                       " mm, read at " + Mm(b[i].BridgeX) + ", " + Mm(b[i].BridgeY) + (insideSame ? "" : ", inside/outside differs"));
                    }
                }
                Check(label, total == 51 && wrong.Count == 0,
                      total != 51 ? total + " «between» in the readings instead of 51" : wrong.Count == 0 ? same + " of " + total + " give back the place read" : string.Join("; ", wrong.Take(8)));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE SAME CALLS LAID OUT BY THE LIBRARY
        // =================================================================

        private sealed class Outcome
        {
            public int Owned, Within5, Within2, Hand, HandWithin5, InsideAsHe, HandSides, HandSided, Overlaps;
            public List<string> Over = new List<string>();
            public List<string> Each = new List<string>();
        }

        private static object Style(Type sw, string json)
        {
            var m = ReflectionLog.Global.Method(sw, "StyleFrom", Any);
            if (m == null) throw new MissingMethodException("SwOperations.StyleFrom");
            var args = new object[] { json, json == null ? "built in" : "contract", null };
            var style = m.Invoke(null, args);
            if (style == null) throw new InvalidOperationException("the style was refused: " + args[2]);
            return style;
        }

        private static double[] TextRectangle(bool horizontal, double x, double y, double width)
        {
            double h = 0.0035;
            return horizontal ? new[] { x - width / 2, y - h / 2, x + width / 2, y + h / 2 }
                              : new[] { x - h / 2, y - width / 2, x + h / 2, y + width / 2 };
        }

        private static bool Overlap(double[] a, double[] b)
        {
            return a[0] < b[2] && b[0] < a[2] && a[1] < b[3] && b[1] < a[3];
        }

        /// <summary>
        /// Every call of the run in its order, laid out by the library: each sees, as the bridge now
        /// reads it from the view (DimensionsInTheView), what the earlier calls left in the same view;
        /// a dimension of another route enters with its text where SOLIDWORKS put it.
        /// </summary>
        private static Outcome LayOut(Type sw, List<Dim> dims, object style)
        {
            var o = new Outcome();
            var inView = new Dictionary<string, List<double[]>>();
            var texts = new Dictionary<string, List<KeyValuePair<string, double[]>>>();
            foreach (var call in Calls(dims).OrderBy(c => c.First().Drawing).ThenBy(c => c.First().Call))
            {
                foreach (var d in call.Where(x => x.Kind != "between"))
                {
                    string key = d.Drawing + "|" + d.View;
                    if (!inView.ContainsKey(key)) { inView[key] = new List<double[]>(); texts[key] = new List<KeyValuePair<string, double[]>>(); }
                    double w = Box(d.Text);
                    inView[key].Add((double[])Call(sw, "ExistingDimension", -1.0, 0.0, 0.0, d.BridgeX, d.BridgeY, w / 2, 0.00175));
                    texts[key].Add(new KeyValuePair<string, double[]>(d.Text, TextRectangle(true, d.BridgeX, d.BridgeY, w)));
                }
                var b = call.Where(x => x.Kind == "between").ToArray();
                if (b.Length == 0) continue;
                string view = b[0].Drawing + "|" + b[0].View;
                if (!inView.ContainsKey(view)) { inView[view] = new List<double[]>(); texts[view] = new List<KeyValuePair<string, double[]>>(); }
                var rows = b.Select(d => Row(d, (double)Call(sw, "TextWidthForTheChoice", d.Text, 0.0035, style))).ToArray();
                // From the lines of the view, or the extent of its edges when they could not be read (ExtentOf).
                var p = (double[][])Call(sw, "LayOutDimensions", rows, b[0].Lines, style, false, b[0].Sheet, inView[view].ToArray());
                for (int i = 0; i < b.Length; i++)
                {
                    var d = b[i];
                    double w = Box(d.Text);
                    var box = TextRectangle(d.Horizontal, p[i][0], p[i][1], w);
                    foreach (var t in texts[view])
                        if (Overlap(box, t.Value)) { o.Overlaps++; o.Over.Add(d.Drawing + " " + d.View + " «" + d.Text + "» over «" + t.Key + "»"); }
                    texts[view].Add(new KeyValuePair<string, double[]>(d.Text, box));
                    inView[view].Add((double[])Call(sw, "ExistingDimension", d.Horizontal ? 1.0 : 0.0, d.Low, d.High, p[i][0], p[i][1],
                                                     d.Horizontal ? w / 2 : 0.00175, d.Horizontal ? 0.00175 : w / 2));
                    if (!d.HasOwner) continue;
                    o.Owned++;
                    double gap = Math.Sqrt(Math.Pow(p[i][0] - d.OwnerX, 2) + Math.Pow(p[i][1] - d.OwnerY, 2));
                    o.Each.Add(string.Join("\t", d.Drawing, d.View, d.Call, d.Text, d.Horizontal ? "h" : "v", d.PlacedBy,
                                           Mm(p[i][0]), Mm(p[i][1]), Mm(d.OwnerX), Mm(d.OwnerY), Mm(gap),
                                           d.Horizontal ? (p[i][3] > 0.5 ? "above" : "below") : (p[i][3] > 0.5 ? "right" : "left"), d.OwnerSide,
                                           p[i][2] > 0.5 ? "inside" : (p[i][6] > 0.5 ? "low end" : "high end"), Mm(p[i][5]), Mm(d.High - d.Low)));
                    if (gap <= 0.005) o.Within5++;
                    if (gap <= 0.002) o.Within2++;
                    double ownerAlong = d.Horizontal ? d.OwnerX : d.OwnerY;
                    bool ownerInside = ownerAlong >= d.Low - 0.0001 && ownerAlong <= d.High + 0.0001;
                    if (ownerInside == (p[i][2] > 0.5)) o.InsideAsHe++;
                    if (d.PlacedBy != "hand") continue;
                    o.Hand++;
                    if (gap <= 0.005) o.HandWithin5++;
                    if (d.OwnerSide == "inside") continue;
                    o.HandSided++;
                    string side = d.Horizontal ? (p[i][3] > 0.5 ? "above" : "below") : (p[i][3] > 0.5 ? "right" : "left");
                    if (side == d.OwnerSide) o.HandSides++;
                }
            }
            return o;
        }

        private static void Replay(Type sw, List<Dim> dims)
        {
            // What the run itself gave, from the places SOLIDWORKS read.
            int runWithin5 = 0, runHand5 = 0, runInside = 0, runOwned = 0, runOverlaps = 0, runHandSides = 0;
            var runTexts = new Dictionary<string, List<double[]>>();
            foreach (var d in dims.Where(x => x.Kind == "between"))
            {
                string key = d.Drawing + "|" + d.View;
                if (!runTexts.ContainsKey(key)) runTexts[key] = new List<double[]>();
                var box = TextRectangle(d.Horizontal, d.BridgeX, d.BridgeY, Box(d.Text));
                runOverlaps += runTexts[key].Count(t => Overlap(box, t));
                runTexts[key].Add(box);
                if (!d.HasOwner) continue;
                runOwned++;
                double gap = Math.Sqrt(Math.Pow(d.BridgeX - d.OwnerX, 2) + Math.Pow(d.BridgeY - d.OwnerY, 2));
                if (gap <= 0.005) { runWithin5++; if (d.PlacedBy == "hand") runHand5++; }
                double ownerAlong = d.Horizontal ? d.OwnerX : d.OwnerY;
                if ((ownerAlong >= d.Low - 0.0001 && ownerAlong <= d.High + 0.0001) == (d.BridgeText == "inside")) runInside++;
                if (d.PlacedBy == "hand" && d.BridgeSide == d.OwnerSide) runHandSides++;
            }
            string run = "the run: " + runWithin5 + " of " + runOwned + " within 5 mm (" + runHand5 + " of his hand), inside or outside as he " +
                         runInside + ", his sides " + runHandSides + ", texts over texts " + runOverlaps;

            Outcome built;
            try { built = LayOut(sw, dims, Style(sw, null)); }
            catch (Exception ex) { Check("the calls of the run laid out by the library", false, Why(ex)); return; }
            // OCE_REPLAY_DUMP names a file that gets every dimension of the replay, for the record.
            var dump = Environment.GetEnvironmentVariable("OCE_REPLAY_DUMP");
            if (!string.IsNullOrWhiteSpace(dump))
                File.WriteAllLines(dump, new[] { "drawing\tview\tcall\ttext\tdir\tby\tx\ty\towner_x\towner_y\tgap\tside\towner_side\ttext_at\trow_mm\tgap_mm" }.Concat(built.Each));
            string replay = "replayed: " + built.Within5 + " of " + built.Owned + " within 5 mm (" + built.Within2 + " within 2), " +
                            built.HandWithin5 + " of his " + built.Hand + ", inside or outside as he " + built.InsideAsHe +
                            ", his sides " + built.HandSides + " of " + built.HandSided + ", texts over texts " + built.Overlaps;

            Check("the calls of the run laid out by the library, each seeing the view the earlier ones left: no text over another (the run had " + runOverlaps + ")",
                  built.Overlaps == 0, built.Overlaps == 0 ? replay : string.Join("; ", built.Over.Take(6)));
            Check("more texts within 5 mm of the owner's than the run, at least " + FloorWithin5 + " of 51 and " + FloorHandWithin5 + " of his hand",
                  built.Within5 > runWithin5 && built.Within5 >= FloorWithin5 && built.HandWithin5 >= FloorHandWithin5, replay + "; " + run);
            Check("the text between the extension lines or outside as the owner has it more often than the run, at least " + FloorInsideAsHe + " of 51",
                  built.InsideAsHe > runInside && built.InsideAsHe >= FloorInsideAsHe, replay + "; " + run);
            Check("the side of his own dimensions at least " + FloorHandSides + " times, not fewer than the run",
                  built.HandSides >= runHandSides && built.HandSides >= FloorHandSides, replay + "; " + run);

            // The styles the owner can choose, side by side: what each would have given on the run.
            var variants = new List<string>();
            foreach (var json in new[] { "{\"tie_balance\": true}", "{\"pitch_mm\": 7}", "{\"text_both_ends\": false}", "{\"tie_horizontal\": \"below\"}", "{\"char_mm\": 2.58, \"comma_mm\": 1.1}" })
            {
                try
                {
                    var v = LayOut(sw, dims, Style(sw, json));
                    variants.Add(json + ": " + v.Within5 + " within 5 mm, " + v.HandWithin5 + " of his hand, inside as he " + v.InsideAsHe + ", sides " + v.HandSides + ", over " + v.Overlaps);
                }
                catch (Exception ex) { variants.Add(json + ": " + Why(ex)); }
            }
            if (!string.IsNullOrWhiteSpace(dump)) File.AppendAllLines(dump, new[] { "# built in: " + replay }.Concat(variants.Select(v => "# " + v)));
            Check("the other styles laid out on the run too, for the record (OCE_REPLAY_DUMP gets them whole)", variants.Count == 5, string.Join(" | ", variants));
        }

        // =================================================================
        // THE LABEL OF A DETAIL DIMENSIONED IN TWO CALLS
        // =================================================================

        private static void DetailLabel(Type sw)
        {
            const string label = "the label of a detail dimensioned in two calls: the lowest text under the view counts the dimensions already in it, so a second call with a vertical one still finds the horizontal one of the first (26P000008 A: the 1,5 and the 2,5 of the channel)";
            try
            {
                var wrong = new List<string>();
                var drawn = new[] { 0.3204, 0.2221, 0.364, 0.2514 };
                var style = Style(sw, null);
                var first = (double[][])Call(sw, "LayOutDimensions", new[] { new[] { 1.0, 0.32038, 0.32788, 0.22413, 0.25087, 0.0072, 1.5, 1.0, 0.0028, 0.0035 } },
                                             drawn, style, false, null, null);
                var old = new[] { (double[])Call(sw, "ExistingDimension", 1.0, 0.32038, 0.32788, first[0][0], first[0][1], 0.0036, 0.00175) };
                var second = (double[][])Call(sw, "LayOutDimensions", new[] { new[] { 0.0, 0.23888, 0.25138, 0.32723, 0.36403, 0.0077, 2.5, 2.0, 0.0028, 0.0035 } },
                                              drawn, style, false, null, old);
                var both = new[] { old[0], (double[])Call(sw, "ExistingDimension", 0.0, 0.23888, 0.25138, second[0][0], second[0][1], 0.00175, 0.00385) };
                double lowest = (double)Call(sw, "LowestUnder", both, drawn);
                if (Math.Abs(lowest - first[0][1]) > 1e-9) wrong.Add("the lowest text " + Mm(lowest) + " mm instead of the 1,5 at " + Mm(first[0][1]));
                double alone = (double)Call(sw, "LowestUnder", new[] { both[1] }, drawn);
                if (!double.IsNaN(alone)) wrong.Add("the second call alone finds a text under the view at " + Mm(alone));
                var y = (double?)Call(sw, "DetailLabelY", 0.21396, lowest, 6.2);
                if (y == null || Math.Abs(y.Value - (lowest - 0.0062)) > 1e-9) wrong.Add("the label goes to " + (y == null ? "nowhere" : Mm(y.Value) + " mm"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "label under the 1,5 at " + Mm(lowest - 0.0062) + " mm" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // A DETAIL PUT ON THE EDGE OF THE CLEARANCE
        // =================================================================

        /// <summary>
        /// The replay of the real run on SOLIDWORKS with bridge 0.163.0 (02/11/2026, second drawing run of the day,
        /// step 035): detail B of 26M000002 (83 x 52.7 mm with its label) was sent «above the title
        /// block», to x 325.1..408 mm, y 69..121.7 mm, landed there to 0.1 mm, and was removed as
        /// falling «on the title block». The place is the top of the title block (65 mm) plus the
        /// 4 mm clearance exactly, and the outline read back after the move can come out a hair
        /// lower; the frame had a tolerance of a micrometre, the views and the title block none.
        /// In the run of 0.161.0 the same step had landed on the right side of the hair.
        /// </summary>
        private static void DetailOnTheEdge(Type sw)
        {
            const string label = "a detail sent to a free place on the edge of the clearance is still free when its outline reads back a nanometre lower, and not when it is half a millimetre into the clearance (26M000002 detail B, replay of 02/11/2026)";
            try
            {
                var wrong = new List<string>();
                var sheetType = ReflectionLog.Global.Nested(sw, "FreeSheet", BindingFlags.NonPublic | BindingFlags.Public);
                if (sheetType == null) { Check(label, false, "FreeSheet missing"); return; }
                var sheet = Activator.CreateInstance(sheetType, true);
                var titleBlock = new[] { 0.21, 0.0, 0.42, 0.065 };
                ReflectionLog.Global.Field(sheetType, "TitleBlock", BindingFlags.Public | BindingFlags.Instance).SetValue(sheet, titleBlock);
                var boxes = (List<double[]>)ReflectionLog.Global.Field(sheetType, "BoundingBoxes", BindingFlags.Public | BindingFlags.Instance).GetValue(sheet);
                boxes.Add(titleBlock);
                var touches = ReflectionLog.Global.Method(sheetType, "Touches", BindingFlags.Public | BindingFlags.Instance);
                Func<double, string> at = lower => (string)touches.Invoke(sheet, new object[] { 0.325, 0.069 - lower, 0.408, 0.1217 - lower });
                var free = ReflectionLog.Global.Method(sw, "FreePlace", Any);
                var args = new object[] { sheet, 0.083, 0.0527, null };
                var corner = free.Invoke(null, args) as double[];
                if (corner == null || (string)args[3] != "above the title block" || Math.Abs(corner[0] - 0.325) > 1e-9 || Math.Abs(corner[1] - 0.069) > 1e-9)
                    wrong.Add("the free place is " + (corner == null ? "none" : Mm(corner[0]) + "/" + Mm(corner[1]) + " mm") + " «" + args[3] + "», not above the title block at 325/69 mm");
                string hair = at(1e-9);
                if (hair != null) wrong.Add("a nanometre lower it " + hair);
                string half = at(0.0005);
                if (half != "falls on the title block") wrong.Add("half a millimetre into the clearance it says «" + (half ?? "free") + "»");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "above the title block at 325/69 mm; a nanometre lower free, 0.5 mm lower on the title block" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        /// <summary>
        /// The 69 straight lines of the sketch of the owner's A3 format (A3.slddrt), millimetres of
        /// sheet {x0, y0, x1, y1}, read with ISheet.GetTemplateSketch by a probe on a copy of
        /// 26P000007 on 02/11/2026 (71 segments, 2 of them circles). The format declares no title
        /// block (ITitleBlock null).
        /// </summary>
        private const string OwnerA3Lines =
            "210,7,210,0;391,53.5,391,38;217,53.5,413,53.5;217,38,413,38;7,7,413,7;413,22.5,217,22.5;217,7,413,7;366.1,14.7" +
            "5,366.1,7;341,53.5,341,38;382.6,7,382.6,14.75;356,38,356,22.5;356,30.25,276,30.25;399.1,14.75,366.1,14.75;366." +
            "1,14.75,339.1,14.75;366.1,14.75,366.1,22.5;326,38,326,22.5;341,45.75,391,45.75;366,45.75,366,38;339.1,22.5,339" +
            ".1,7;0,0,420,0;399.1,22.5,399.1,7;217,53.5,217,7;341,45.75,217,45.75;391,45.75,413,45.75;272.8,22.5,272.8,7;0," +
            "0,0,297;0,297,420,297;420,0,420,297;413,274,413,278;276,38,276,22.5;381,38,381,22.5;272.8,17.333,339.1,17.333;" +
            "272.8,12.167,305.95,12.167;250.6,7,413,7;413,282,413,286;281.087,7,281.087,12.167;305.95,12.167,305.95,7;7,290" +
            ",7,7;305.95,12.167,339.1,12.167;413,282,413,278;289.375,12.167,289.375,7;413,7,413,290;413,290,7,290;297.662,1" +
            "2.167,297.662,7;250.6,7,413,7;281.087,9.583,305.95,9.583;250.567,290,413,290;413,290,413,286;250.6,7,413,7;413" +
            ",7,413,53.5;281.087,9.583,272.8,9.583;276,30.25,254,30.25;254,30.25,217,30.25;250.6,22.5,250.6,7;254,38,254,22" +
            ".5;235.5,30.25,235.5,38;235.5,38,235.5,45.75;314.237,7,314.237,12.167;322.525,12.167,322.525,7;330.812,12.167," +
            "330.812,7;314.237,9.583,339.1,9.583;314.237,9.583,305.95,9.583;374.405,30.25,357.697,30.25;358.296,27.37,366.5" +
            "24,25.45;374.405,30.25,379.992,30.25;374.405,24.166,374.405,35.943;366.524,35.05,366.524,25.45;358.296,33.13,3" +
            "66.524,35.05;358.296,33.13,358.296,27.37";

        private static List<double[]> LinesOf(string text, Func<double[], bool> keep = null)
        {
            var lines = new List<double[]>();
            foreach (var row in text.Split(';'))
            {
                var l = row.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture) / 1000).ToArray();
                if (keep == null || keep(l)) lines.Add(l);
            }
            return lines;
        }

        /// <summary>
        /// LIBRARY 0.198.4. The lines of the format in one call: ISketch.GetLines2 gives 12 doubles a
        /// line, start at 6..8 and end at 9..11. The first record is the one SOLIDWORKS gave for the
        /// owner's A3 (a read-only probe, 02/11/2026): 255, 2, 0, 2, 1, 29, 0.21, 0.007, 0, 0.21,
        /// 0, 0, the line from 210; 7 to 210; 0 mm. An array that is not whole records gives null, so
        /// the library reads the segments one at a time instead.
        /// </summary>
        private static void FormatLinesInOneCall(Type sw)
        {
            const string label = "the lines of the format from ISketch.GetLines2: start at 6..8, end at 9..11 of each 12 doubles (measured on the owner's A3); an array that is not whole records gives null";
            try
            {
                var wrong = new List<string>();
                var two = new double[] { 255, 2, 0, 2, 1, 29, 0.21, 0.007, 0, 0.21, 0, 0,
                                         0, 1, 0, 1, 1, 0, 0.007, 0.29, 0, 0.413, 0.29, 0 };
                var lines = Call(sw, "LinesOfGetLines2", two) as List<double[]>;
                if (lines == null || lines.Count != 2) wrong.Add("two records give " + (lines == null ? "null" : lines.Count + " lines"));
                else
                {
                    var expected = new[] { new[] { 0.21, 0.007, 0.21, 0.0 }, new[] { 0.007, 0.29, 0.413, 0.29 } };
                    for (int i = 0; i < 2; i++)
                        for (int k = 0; k < 4; k++)
                            if (Math.Abs(lines[i][k] - expected[i][k]) > 1e-12)
                                wrong.Add("line " + (i + 1) + ", value " + (k + 1) + ": " + lines[i][k].ToString(CultureInfo.InvariantCulture) + " instead of " + expected[i][k].ToString(CultureInfo.InvariantCulture));
                }
                if (Call(sw, "LinesOfGetLines2", new double[13]) != null) wrong.Add("13 doubles give lines");
                if (Call(sw, "LinesOfGetLines2", new double[0]) != null) wrong.Add("no doubles give lines");
                if (Call(sw, "LinesOfGetLines2", new object[] { null }) != null) wrong.Add("null gives lines");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "2 records: 210 7 210 0 mm and 7 290 413 290 mm; 13, 0 and null: none" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        /// <summary>
        /// NOTE 98, DEFECT 8. The bill of materials asked over the title block stayed about 16 mm
        /// above it and to its left: the format declares no title block, and the bridge estimated it
        /// at half the sheet x 65 mm. The lines of the format draw it at 217..413 x 7..53.5 mm.
        /// </summary>
        private static void TitleBlockOfTheFormat(Type sw)
        {
            const string label = "the title block drawn by the lines of a format that declares none: the owner's A3 gives 217..413 x 7..53.5 mm (the estimate was 210..420 x 0..65 mm, note 98, defect 8); the frame and the paper alone, or lines only in another corner, give none";
            try
            {
                var wrong = new List<string>();
                Func<List<double[]>, double[]> block = lines => (double[])Call(sw, "TitleBlockFromLines", lines, 0.42, 0.297);
                Func<double[], string> said = b => b == null ? "none" : string.Join(" ", b.Select(v => Mm(v).ToString(CultureInfo.InvariantCulture))) + " mm";
                var owner = block(LinesOf(OwnerA3Lines));
                if (owner == null || Math.Abs(owner[0] - 0.217) > 1e-9 || Math.Abs(owner[1] - 0.007) > 1e-9 ||
                    Math.Abs(owner[2] - 0.413) > 1e-9 || Math.Abs(owner[3] - 0.0535) > 1e-9)
                    wrong.Add("the owner's A3 gives " + said(owner));
                // Only the edge of the paper, the frame and the marks on it: no title block.
                Func<double[], bool> onTheFrame = l =>
                    (Math.Abs(l[1] - l[3]) < 1e-7 && (Math.Abs(l[1]) < 1e-7 || Math.Abs(l[1] - 0.297) < 1e-7 || Math.Abs(l[1] - 0.007) < 1e-7 || Math.Abs(l[1] - 0.290) < 1e-7)) ||
                    (Math.Abs(l[0] - l[2]) < 1e-7 && (Math.Abs(l[0]) < 1e-7 || Math.Abs(l[0] - 0.42) < 1e-7 || Math.Abs(l[0] - 0.007) < 1e-7 || Math.Abs(l[0] - 0.413) < 1e-7));
                var frame = block(LinesOf(OwnerA3Lines, onTheFrame));
                if (frame != null) wrong.Add("the frame alone gives " + said(frame));
                var paper = block(LinesOf("0,0,420,0;420,0,420,297;420,297,0,297;0,297,0,0"));
                if (paper != null) wrong.Add("the paper alone gives " + said(paper));
                var otherCorner = block(LinesOf("7,7,413,7;413,7,413,290;413,290,7,290;7,290,7,7;7,50,180,50;180,50,180,7"));
                if (otherCorner != null) wrong.Add("a box in the bottom-left corner gives " + said(otherCorner));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "69 lines: 217 7 413 53.5 mm; the frame, the paper, the other corner: none" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING
        // =================================================================

        private const string ReadInView = "double[][] inTheView = DimensionsInTheView(view, mu, tr, out unreadInTheView);";
        private const string FirstBorn = "Dictionary<string, object> placed = PlaceBetween(";
        private static readonly string[][] Lines =
        {
            new[] { ReadInView, "the dimensions already in the view are read" },
            new[] { "double[][] places = LayOutDimensions(toLay, drawn, style, row, SheetDimensions(drawing), inTheView);", "the layout gets them" },
            new[] { "double choice = Math.Max(TextWidthForTheChoice(", "the choice inside or outside uses the widths of the rules" },
            new[] { "TextWidthForTheChoice(_dimensionBelow, height, style));", "and the text under the line when it is wider" },
            new[] { "box, p.ExpectedMm, sideAsked, choice, height,", "the layout gets that width and the height" },
            new[] { "var afterLayout = new List<double[]>(inTheView);", "the label counts the old dimensions" },
            new[] { "afterLayout.Add(ExistingDimension(", "and the new ones" },
            new[] { "lowestUnder = LowestUnder(afterLayout.ToArray(), drawn);", "the label goes under the lowest of all" },
        };

        private static List<string> WiringProblems(string between)
        {
            var problems = new List<string>();
            if (between == null) { problems.Add("DrawingBetweenDimensions.cs is missing"); return problems; }
            foreach (var line in Lines)
                if (!between.Contains(line[0])) problems.Add(line[1] + ": «" + line[0] + "» is gone");
            int read = between.IndexOf(ReadInView, StringComparison.Ordinal), born = between.IndexOf(FirstBorn, StringComparison.Ordinal);
            if (read >= 0 && born >= 0 && read > born) problems.Add("the view is read after the first dimension is born: the new ones would count as already there");
            return problems;
        }

        private static void Wiring()
        {
            const string label = "wired: «between» reads the dimensions already in the view before the new ones are born, hands them and the widths of the rules to the layout, and moves the label of a detail under the lowest of all";
            var file = Path.Combine(Root(), "Oce.Solid.Agent.Core", "DrawingBetweenDimensions.cs");
            string between = File.Exists(file) ? File.ReadAllText(file) : null;
            var real = WiringProblems(between);
            var blind = new List<string>();
            int copies = 0;
            Action<string, Func<string, string>> mutate = (what, change) =>
            {
                if (between == null) return;
                var changed = change(between);
                if (changed == between) { blind.Add(what + " (the copy did not change)"); return; }
                copies++;
                if (WiringProblems(changed).Count == 0) blind.Add(what);
            };
            foreach (var line in Lines)
            {
                var anchor = line[0];
                mutate("without «" + anchor + "»", t => t.Replace(anchor, "/* gone */"));
            }
            mutate("the view read after the first birth", t =>
            {
                int at = t.IndexOf(FirstBorn, StringComparison.Ordinal);
                if (at < 0) return t;
                var without = t.Replace(ReadInView, "/* moved */");
                int after = without.IndexOf('\n', without.IndexOf(FirstBorn, StringComparison.Ordinal)) + 1;
                return without.Insert(after, ReadInView + "\n");
            });
            bool ok = real.Count == 0 && blind.Count == 0;
            Check(label, ok, real.Count > 0 ? string.Join("; ", real)
                           : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                           : Lines.Length + " lines and their order; " + copies + " changed copies all red");
        }
    }
}
