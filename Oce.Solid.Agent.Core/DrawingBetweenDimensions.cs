using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE DISTANCE BETWEEN TWO EDGES, AND HOW A DIMENSION IS LAID OUT (report 8cd843,
    /// round 22, 28/10/2026). On the drawing of 26P000007 made with bridge 0.152.10 the
    /// position of the rib (21 mm from the ends), the width of the channel (3.4) and the
    /// overall heights (7.05, 5.85, 3.35, 5) could not be dimensioned: «drawing_dimension»
    /// dimensioned the length of ONE edge, and the vertical edges are cut by the 0.4
    /// chamfer, so their lengths (5.45, 2.95) would have misled the shop.
    ///
    /// «between» names pairs of edges of the view; each pair must be two straight edges
    /// parallel on the sheet, both vertical (a horizontal distance) or both horizontal (a
    /// vertical distance). The dimension is placed with the same road as the overall size
    /// (DrawingAssemblyDimensions.cs: the two edges selected, `AddDimension`, then
    /// `AddHorizontalDimension2` / `AddVerticalDimension2` when nothing is born) and read
    /// back: its value must be the distance of the two edges on the sheet divided by the
    /// scale, or it is taken away. Not measured on SOLIDWORKS yet for a part: the overall
    /// size of an assembly is (bench case `assembly_drawing_overall`).
    ///
    /// The same round asked how a dimension is laid out, and this file holds it:
    ///  * «row»: every dimension of the call at the same distance from the view, a chain
    ///    instead of a staircase (round 22, point 2: one step per dimension with the same
    ///    «offset_mm» was the only way, steps 030 and 031);
    ///  * «horizontal_side» above or below, «vertical_side» right or left (point 7);
    ///  * «prefix» / «suffix» written on the dimension, «2,5 CHANNEL» (point 5),
    ///    IDisplayDimension~SetText.html with swDimensionTextParts_e;
    ///  * «parentheses», a reference dimension «(35)» (point 6),
    ///    IDisplayDimension~ShowParenthesis.html.
    /// Each written thing is read back (GetText, ShowParenthesis) and a row says what did
    /// not stick.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>The text «drawing_dimension» writes before the value of each dimension it places; null for none.</summary>
        private string _dimensionPrefix;
        /// <summary>The text written after the value; null for none.</summary>
        private string _dimensionSuffix;
        /// <summary>The text written under the dimension line, under the value («CHANNEL» under «3,4»); null for none.</summary>
        private string _dimensionBelow;
        /// <summary>Whether each dimension placed is shown in parentheses, as a reference dimension.</summary>
        private bool _dimensionParentheses;

        /// <summary>The most pairs one call takes: a drawing is dimensioned a view at a time.</summary>
        internal const int MostPairsBetween = 40;

        /// <summary>The longest text written before or after a value.</summary>
        internal const int LongestDimensionText = 60;

        /// <summary>
        /// «between» of «drawing_dimension»: pairs of edge numbers, [[3, 7], [7, 12]]. Null
        /// with the reason when it cannot be used; the range of each number is checked
        /// against <paramref name="available"/> (the edges the view shows), unless it is
        /// below 0. Pure, so the contract suite reads it.
        /// </summary>
        internal static List<int[]> BetweenPairsAsked(object raw, int available, out string why)
        {
            why = null;
            var pairs = new List<int[]>();
            var asList = raw as IEnumerable;
            if (raw == null || raw is string || asList == null)
            {
                why = "it is not a list of pairs of edge numbers, as [[3, 7], [7, 12]]";
                return null;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int position = 0;
            foreach (object item in asList)
            {
                position++;
                var inner = item as IEnumerable;
                if (item == null || item is string || inner == null)
                {
                    why = "the pair " + position + " is not two edge numbers in brackets, as [3, 7]";
                    return null;
                }
                var numbers = new List<int>();
                foreach (object n in inner)
                {
                    double value;
                    try { value = Convert.ToDouble(n, CultureInfo.InvariantCulture); }
                    catch { why = "the pair " + position + " holds «" + n + "», which is not a number"; return null; }
                    if (value != Math.Floor(value) || value < 0)
                    {
                        why = "the pair " + position + " holds " + value.ToString(CultureInfo.InvariantCulture) +
                              ", which is not an edge number (whole, from 0)";
                        return null;
                    }
                    numbers.Add((int)value);
                }
                if (numbers.Count != 2)
                {
                    why = "the pair " + position + " has " + numbers.Count + " numbers instead of two";
                    return null;
                }
                if (numbers[0] == numbers[1])
                {
                    why = "the pair " + position + " names the edge " + numbers[0] + " twice: a distance wants two edges";
                    return null;
                }
                if (available >= 0)
                    foreach (int n in numbers)
                        if (n >= available)
                        {
                            why = "the edge " + n + " does not exist: the view has " + available + " edges (from 0 to " + (available - 1) + ")";
                            return null;
                        }
                string key = Math.Min(numbers[0], numbers[1]) + "|" + Math.Max(numbers[0], numbers[1]);
                if (!seen.Add(key))
                {
                    why = "the pair " + numbers[0] + ", " + numbers[1] + " is asked twice";
                    return null;
                }
                pairs.Add(new[] { numbers[0], numbers[1] });
            }
            if (pairs.Count == 0) { why = "it is empty"; return null; }
            if (pairs.Count > MostPairsBetween)
            {
                why = pairs.Count + " pairs are more than " + MostPairsBetween + " in one call: dimension the view in more calls";
                return null;
            }
            return pairs;
        }

        /// <summary>
        /// Why two edges have no distance a dimension can give, or null when they do: both
        /// straight, both vertical or both horizontal on the sheet, and not on the same line.
        /// <paramref name="gapMm"/> is their distance on the sheet divided by the scale. Pure.
        /// </summary>
        internal static string BetweenProblem(string facingA, bool straightA, string facingB, bool straightB, double gapMm)
        {
            if (!straightA || !straightB)
                return "an edge that is not straight (an arc or a curve) has no distance from a line";
            if (facingA == "oblique" || facingB == "oblique")
                return "an oblique edge: only two vertical or two horizontal edges of the sheet are measured";
            if (facingA != facingB)
                return "one edge is vertical and the other horizontal: they are not parallel";
            if (gapMm < 0.001)
                return "the two edges lie on the same line: their distance is zero";
            return null;
        }

        /// <summary>
        /// The side asked for the dimensions of one direction: «horizontal_side» takes below
        /// or above, «vertical_side» right or left. The first of the two is the default, given
        /// back when the key is left out. Null with the reason for any other word. Pure.
        /// </summary>
        internal static string SideAsked(Dictionary<string, object> a, string key, string first, string second, out string why)
        {
            why = null;
            object raw;
            if (a == null || !a.TryGetValue(key, out raw) || raw == null) return first;
            string word = Convert.ToString(raw, CultureInfo.InvariantCulture).Trim().ToLowerInvariant();
            if (word == first || word == second) return word;
            why = "«" + key + "» takes «" + first + "» or «" + second + "», not «" + raw + "»";
            return null;
        }

        /// <summary>
        /// Why «prefix», «suffix» or «parentheses» cannot be used, or null. A text is one
        /// line of at most <see cref="LongestDimensionText"/> characters. Pure.
        /// </summary>
        internal static string DecorationProblem(object prefix, object suffix, object parentheses)
        {
            foreach (var named in new[] { new object[] { "prefix", prefix }, new object[] { "suffix", suffix } })
            {
                object value = named[1];
                if (value == null) continue;
                string text = value as string;
                if (text == null) return "«" + named[0] + "» is a text, not «" + value + "»";
                if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0) return "«" + named[0] + "» is one line";
                if (text.Length > LongestDimensionText)
                    return "«" + named[0] + "» has " + text.Length + " characters, more than " + LongestDimensionText;
            }
            if (parentheses != null && !(parentheses is bool)) return "«parentheses» is true or false, not «" + parentheses + "»";
            return null;
        }

        /// <summary>
        /// Why «below» cannot be used, null when it can or was not asked: a text of one line, at
        /// most LongestDimensionText characters (note 98, defect 2: the owner writes «CHANNEL» under
        /// the 3,4, not after it). Pure.
        /// </summary>
        internal static string BelowProblem(object below)
        {
            if (below == null) return null;
            string text = below as string;
            if (text == null) return "«below» is a text, not «" + below + "»";
            if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0) return "«below» is one line";
            if (text.Trim().Length == 0) return "«below» is empty";
            if (text.Length > LongestDimensionText) return "«below» has " + text.Length + " characters, more than " + LongestDimensionText;
            return null;
        }

        /// <summary>What of «below» did not stick, read back after writing; null when it did. Pure.</summary>
        internal static string BelowNote(string asked, string read)
        {
            if (string.IsNullOrEmpty(asked) || (read ?? "").Trim() == asked.Trim()) return null;
            return "the text under the line reads «" + (read ?? "nothing") + "» instead of «" + asked + "»: write it by hand on the sheet";
        }

        /// <summary>
        /// What of «prefix», «suffix» and «parentheses» did not stick on a dimension, read
        /// back after writing; null when all of it did. A text is compared without the
        /// blanks at its ends. Pure.
        /// </summary>
        internal static string DecorationNote(string prefixAsked, string prefixRead, string suffixAsked, string suffixRead,
                                              bool parenthesesAsked, bool? parenthesesRead)
        {
            var lost = new List<string>();
            if (!string.IsNullOrEmpty(prefixAsked) && (prefixRead ?? "").Trim() != prefixAsked.Trim())
                lost.Add("the prefix reads «" + (prefixRead ?? "nothing") + "» instead of «" + prefixAsked + "»");
            if (!string.IsNullOrEmpty(suffixAsked) && (suffixRead ?? "").Trim() != suffixAsked.Trim())
                lost.Add("the suffix reads «" + (suffixRead ?? "nothing") + "» instead of «" + suffixAsked + "»");
            if (parenthesesAsked && parenthesesRead != true)
                lost.Add(parenthesesRead == null ? "the parentheses cannot be read back" : "the parentheses did not stick");
            return lost.Count == 0 ? null : string.Join("; ", lost) + ": write it by hand on the sheet";
        }

        /// <summary>
        /// The «decoration_note» of every row, in one list, with the value of its dimension:
        /// what was asked and is not on the sheet. Pure.
        /// </summary>
        internal static List<string> DecorationsNotKept(IEnumerable<Dictionary<string, object>> rows)
        {
            var lost = new List<string>();
            foreach (Dictionary<string, object> row in rows)
            {
                object note, mm;
                if (!row.TryGetValue("decoration_note", out note) || !(note is string)) continue;
                row.TryGetValue("mm", out mm);
                lost.Add((mm is double ? ((double)mm).ToString("0.###", CultureInfo.InvariantCulture) + " mm: " : "") + (string)note);
            }
            return lost;
        }

        /// <summary>
        /// Writes «prefix», «suffix» and «parentheses» of the call on a dimension just placed,
        /// after its decimals (the number «text_shown» says is the value alone), and reads
        /// them back into its row. Nothing is written when the call asked none of them.
        /// </summary>
        private void DecorateDimension(IDisplayDimension d, Dictionary<string, object> row)
        {
            if (d == null || row == null) return;
            if (string.IsNullOrEmpty(_dimensionPrefix) && string.IsNullOrEmpty(_dimensionSuffix) && !_dimensionParentheses &&
                string.IsNullOrEmpty(_dimensionBelow)) return;
            if (!string.IsNullOrEmpty(_dimensionBelow))
            {
                string belowRead = null;
                try { d.SetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelow, _dimensionBelow); } catch { }
                try { belowRead = d.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelow); } catch { }
                row["below_read"] = belowRead;
                string belowNote = BelowNote(_dimensionBelow, belowRead);
                if (belowNote != null) row["decoration_note"] = belowNote;
                if (string.IsNullOrEmpty(_dimensionPrefix) && string.IsNullOrEmpty(_dimensionSuffix) && !_dimensionParentheses) return;
            }
            if (!string.IsNullOrEmpty(_dimensionPrefix))
                try { d.SetText((int)swDimensionTextParts_e.swDimensionTextPrefix, _dimensionPrefix); } catch { }
            if (!string.IsNullOrEmpty(_dimensionSuffix))
                try { d.SetText((int)swDimensionTextParts_e.swDimensionTextSuffix, _dimensionSuffix); } catch { }
            if (_dimensionParentheses)
                try { d.ShowParenthesis = true; } catch { }
            string prefixRead = null, suffixRead = null;
            bool? parenthesesRead = null;
            try { prefixRead = d.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix); } catch { }
            try { suffixRead = d.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix); } catch { }
            try { parenthesesRead = d.ShowParenthesis; } catch { }
            if (!string.IsNullOrEmpty(_dimensionPrefix)) row["prefix_read"] = prefixRead;
            if (!string.IsNullOrEmpty(_dimensionSuffix)) row["suffix_read"] = suffixRead;
            if (_dimensionParentheses) row["parentheses_read"] = parenthesesRead;
            string note = DecorationNote(_dimensionPrefix, prefixRead, _dimensionSuffix, suffixRead, _dimensionParentheses, parenthesesRead);
            object before;
            if (note != null) row["decoration_note"] = row.TryGetValue("decoration_note", out before) && before is string ? (string)before + "; " + note : note;
        }

        /// <summary>
        /// The room an arrow takes inside the extension lines, in metres of sheet: an arrow of
        /// 3 mm (UNI EN ISO 129-1 draws them 2.5 to 3.5 mm on A3/A4) and half a millimetre to
        /// the text. Not read from the template: a hypothesis until the bench.
        /// </summary>
        internal const double ArrowRoom = 0.0035;

        /// <summary>
        /// Where the arrows of a dimension go (report 8cd843, round 22, point 12, the owner's fixed
        /// rule): the text between the extension lines with the arrows inside when it fits there
        /// with an arrow on each side; otherwise the text outside with the arrows outside. Never
        /// the text inside with the arrows outside, which SOLIDWORKS did on every short
        /// dimension of 26P000007 (10,9, 19, 11, 5; 2, 1,8, 3,5 on the detail). Pure.
        /// </summary>
        internal static string ArrowsFor(double gap, double textWidth, double arrowRoom)
        {
            return textWidth > 0 && textWidth + 2 * arrowRoom <= gap ? "inside" : "outside";
        }

        /// <summary>
        /// The box {x0, y0, x1, y1} of the edges of a view on the sheet, metres, kept inside
        /// <paramref name="outline"/> when there is one: a detail lists the edges outside its circle too
        /// (26M000002 detail B: down to x -243 mm). Null without edges.
        /// </summary>
        private static double[] ExtentOf(List<ProjectedEdge> edges, double[] outline)
        {
            double[] box = null;
            if (edges == null) return null;
            foreach (ProjectedEdge e in edges)
                foreach (double[] p in new[] { e.FromSheet, e.ToSheet })
                {
                    if (p == null || p.Length < 2) continue;
                    if (box == null) box = new[] { p[0], p[1], p[0], p[1] };
                    box[0] = Math.Min(box[0], p[0]); box[1] = Math.Min(box[1], p[1]);
                    box[2] = Math.Max(box[2], p[0]); box[3] = Math.Max(box[3], p[1]);
                }
            if (box != null && outline != null && outline.Length >= 4)
            {
                box = new[] { Math.Max(box[0], outline[0]), Math.Max(box[1], outline[1]), Math.Min(box[2], outline[2]), Math.Min(box[3], outline[3]) };
                if (box[0] >= box[2] || box[1] >= box[3]) return null;
            }
            return box;
        }

        /// <summary>The height of the first text of a dimension on the sheet, metres; -1 when it cannot be read.</summary>
        private static double TextHeight(IDisplayDimension d)
        {
            try
            {
                var data = d.GetDisplayData() as IDisplayData;
                return data == null || data.GetTextCount() < 1 ? -1 : data.GetTextHeightAtIndex(0);
            }
            catch { return -1; }
        }

        /// <summary>
        /// THE DIMENSIONS ALREADY IN THE VIEW, for the layout (02/11/2026, note 100, defect 1): one row
        /// of ExistingDimension each. A distance between two straight parallel edges gives its kind and
        /// its two extension lines (the edges brought onto the sheet as «view_edges» brings them,
        /// ReadEdge); any other dimension gives its text only. Its text is the rectangle of its texts
        /// (TextBounds) centred on its position, as the layout places a new one. Hidden by hand: not on
        /// the paper, left out. <paramref name="unread"/> counts those that could not be read.
        /// </summary>
        private static double[][] DimensionsInTheView(IView view, IMathUtility mu, MathTransform tr, out int unread)
        {
            var rows = new List<double[]>();
            unread = 0;
            IDisplayDimension d = null;
            try { d = view.GetFirstDisplayDimension5() as IDisplayDimension; } catch { }
            for (int guard = 0; d != null && guard < 1000; guard++)
            {
                try
                {
                    bool hidden = false;
                    try { hidden = ApiHelpers.IsHiddenByHand(d); } catch { }
                    if (!hidden)
                    {
                        double[] box = TextBounds(d);
                        var annotation = d.GetAnnotation() as IAnnotation;
                        var at = annotation == null ? null : annotation.GetPosition() as double[];
                        if (box == null || at == null || at.Length < 2) unread++;
                        else
                        {
                            double kind = -1, low = 0, high = 0;
                            var attached = annotation.GetAttachedEntities3() as object[];
                            if (attached != null && attached.Length == 2)
                            {
                                ProjectedEdge a = ReadEdge(view, attached[0], -1, mu, tr), b = ReadEdge(view, attached[1], -1, mu, tr);
                                if (a != null && b != null && a.Facing == b.Facing && a.Facing != "oblique")
                                {
                                    // Two vertical edges make a horizontal distance, along X.
                                    kind = a.Facing == "vertical" ? 1 : 0;
                                    int k = kind > 0.5 ? 0 : 1;
                                    low = Math.Min(a.FromSheet[k], b.FromSheet[k]);
                                    high = Math.Max(a.FromSheet[k], b.FromSheet[k]);
                                }
                            }
                            rows.Add(ExistingDimension(kind, low, high, at[0], at[1], (box[2] - box[0]) / 2, (box[3] - box[1]) / 2));
                        }
                    }
                }
                catch { unread++; }
                try { d = d.GetNext5() as IDisplayDimension; } catch { d = null; }
            }
            return rows.ToArray();
        }

        /// <summary>The widest text of a dimension on the sheet, metres; -1 when it cannot be read.</summary>
        private static double WidestText(IDisplayDimension d)
        {
            try
            {
                var data = d.GetDisplayData() as IDisplayData;
                if (data == null) return -1;
                double widest = -1;
                int n = data.GetTextCount();
                for (int i = 0; i < n; i++) widest = Math.Max(widest, data.GetTextInBoxWidthAtIndex(i));
                return widest;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Puts the arrows of a linear dimension where its text is (ArrowsFor): inside, or the
        /// text moved out beyond the higher end with the arrows outside. <paramref name="low"/>
        /// and <paramref name="high"/> are the two extension lines along X when
        /// <paramref name="alongX"/>, along Y otherwise, in metres of sheet. The row says
        /// «arrows» as read back, and «arrows_note» when SOLIDWORKS did not keep them.
        /// </summary>
        private static void ArrowsByText(IDisplayDimension d, Dictionary<string, object> row, double low, double high, bool alongX)
        {
            if (d == null || row == null) return;
            double gap = Math.Abs(high - low), width = WidestText(d);
            string side = ArrowsFor(gap, width, ArrowRoom);
            if (side == "outside" && width > 0)
            {
                // The text out, beyond the higher end, at the same distance from the part.
                try
                {
                    var annotation = d.GetAnnotation() as IAnnotation;
                    var at = annotation == null ? null : annotation.GetPosition() as double[];
                    if (at != null && at.Length >= 2)
                    {
                        double along = Math.Max(low, high) + ArrowRoom + width / 2;
                        if (alongX) annotation.SetPosition2(along, at[1], 0);
                        else annotation.SetPosition2(at[0], along, 0);
                        row["text_moved_out"] = true;
                        row["position_mm"] = WhereItEnded(d);
                    }
                }
                catch { }
            }
            int wanted = side == "inside" ? (int)swDimensionArrowsSide_e.swDimArrowsInside : (int)swDimensionArrowsSide_e.swDimArrowsOutside;
            int read = -1;
            try { d.ArrowSide = wanted; } catch { }
            try { read = d.ArrowSide; } catch { }
            row["arrows"] = read == (int)swDimensionArrowsSide_e.swDimArrowsInside ? "inside"
                          : read == (int)swDimensionArrowsSide_e.swDimArrowsOutside ? "outside" : null;
            if (read != wanted)
                row["arrows_note"] = "I asked the arrows " + side + " and SOLIDWORKS reads " +
                                     (read < 0 ? "nothing" : ((swDimensionArrowsSide_e)read).ToString()) + ": set them by hand";
        }

        /// <summary>
        /// The swDetViewStyle_e of «circle_style» (round 22, point 13), null for a word it does
        /// not take. Pure.
        /// </summary>
        internal static int? DetailCircleStyle(string asked)
        {
            switch ((asked ?? "").Trim().ToLowerInvariant())
            {
                case "with_leader": return (int)swDetViewStyle_e.swDetViewLEADER;
                case "standard": return (int)swDetViewStyle_e.swDetViewSTANDARD;
                case "no_leader": return (int)swDetViewStyle_e.swDetViewNOLEADER;
                case "connected": return (int)swDetViewStyle_e.swDetViewCONNECTED;
                case "broken": return (int)swDetViewStyle_e.swDetViewBROKEN;
                default: return null;
            }
        }

        /// <summary>The word of «circle_style» for a style read back; null when it was not read. Pure.</summary>
        internal static string DetailCircleStyleName(int style)
        {
            foreach (string word in new[] { "with_leader", "standard", "no_leader", "connected", "broken" })
                if (DetailCircleStyle(word) == style) return word;
            return null;
        }

        /// <summary>Whether a view is a detail view, whose dimensions go above it (round 22, point 14).</summary>
        private static bool IsDetailView(IView view)
        {
            try { return view != null && view.Type == (int)swDrawingViewTypes_e.swDrawingDetailView; }
            catch { return false; }
        }

        /// <summary>
        /// THE ROUTE OF «between»: one dimension per pair of edges, the distance of two
        /// parallel edges, read back against their distance on the sheet. Works on a view of
        /// a part and of an assembly alike: the edges come from the list the view exposes.
        /// </summary>
        /// <summary>A distance placed by «between», kept for the layout that follows (DrawingDimensionStyle.cs).</summary>
        private sealed class BetweenPlaced
        {
            internal Dictionary<string, object> Row;
            internal IDisplayDimension Dimension;
            internal ProjectedEdge First, Second;
            internal bool Horizontal;
            internal double ExpectedMm;
        }

        private Outcome DimensionBetween(
            IModelDoc2 model, IDrawingDoc drawing, IView view, string viewName, object[] edges,
            List<ProjectedEdge> all, List<Circle> circles, List<int[]> pairs, double[] contour,
            double dimensionOffset, double planStep, double scale, bool row, bool above, bool left,
            bool horizontalNamed, bool verticalNamed, bool offsetNamed, bool pitchNamed,
            IMathUtility mu, MathTransform tr, bool repeat)
        {
            // THE STYLE, read before anything is placed: a file asked and not readable is a refusal,
            // not a drawing laid out another way without a word.
            string styleWhy;
            DimensionStyle style = StyleOfThisCall(out styleWhy);
            if (style == null)
                return Failed("The dimension style cannot be used: " + styleWhy + ". Correct the file, or remove " +
                              DrawingStyleVariable + " to use the built-in one. I touched nothing.");
            style = style.Copy();
            if (offsetNamed) style.FirstRowMm = dimensionOffset * 1000;
            if (pitchNamed) style.PitchMm = planStep * 1000;

            var failed = new List<string>();
            // ONE DIMENSION PER EXTENT ON THE WHOLE DRAWING (02/11/2026). The owner's four drawings
            // held 7 dimensions too many in 51 (the 19 and the 11 of 26P000008 in two views, the 175
            // next to 19 + 145 + 11), and «between» put them all back: it did not look at the other
            // views. Now a pair whose extent the drawing already gives, by one dimension or through
            // a chain, is refused, and the answer says which dimensions give it. «repeat» true, or
            // «parentheses» true (a reference dimension, the way a closing dimension is written),
            // places it anyway. Compared on a view of a part, square to the model's axes: the tracts
            // are model coordinates, and on a view of an assembly those of two components do not meet.
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            ExistingDimensionsReading existingRead;
            var already = new HashSet<string>(KeysAlreadyDimensioned(drawing, view, mu, out existingRead, owners), StringComparer.Ordinal);
            int[] axes = SheetAxes(mu, tr);
            bool ofAnAssembly = all.Exists(t => t.Component != null);
            string notCompared = ofAnAssembly ? "a view of an assembly: the tracts of two components are not compared"
                               : axes[0] < 0 || axes[1] < 0 ? "a view turned off the axes of the model"
                               : null;
            bool repeatAllowed = repeat || _dimensionParentheses;
            var refused = new List<string>();
            // Each pair checked on the sheet before anything is placed: horizontal distances
            // (two vertical edges) and vertical ones (two horizontal edges).
            var horizontalPairs = new List<object[]>();
            var verticalPairs = new List<object[]>();
            foreach (int[] pair in pairs)
            {
                ProjectedEdge first = all.Find(t => t.Index == pair[0]);
                ProjectedEdge second = all.Find(t => t.Index == pair[1]);
                string missing = null;
                foreach (int n in pair)
                    if (all.Find(t => t.Index == n) == null)
                        missing = (missing == null ? "" : missing + " and ") + n +
                                  (circles.Exists(c => c.Index == n) ? " (a circle)" : " (it cannot be measured on the sheet)");
                if (missing != null)
                {
                    failed.Add(pair[0] + "-" + pair[1] + " (the edge " + missing + ": a distance is between two straight edges)");
                    continue;
                }
                int k = first.Facing == "vertical" ? 0 : 1;
                double gapMm = Math.Abs(second.FromSheet[k] - first.FromSheet[k]) / scale * 1000.0;
                string problem = BetweenProblem(first.Facing, first.Straight, second.Facing, second.Straight, gapMm);
                if (problem != null)
                {
                    failed.Add(pair[0] + "-" + pair[1] + " (" + problem + ")");
                    continue;
                }
                string tract = notCompared != null ? null
                             : TractBetween(first.FromModel, first.ToModel, second.FromModel, second.ToModel, axes);
                if (tract != null)
                {
                    string[] chain = repeatAllowed ? null : ChainThrough(already, tract);
                    if (chain != null)
                    {
                        refused.Add(pair[0] + "-" + pair[1] + " (" + Math.Round(gapMm, 3).ToString(CultureInfo.InvariantCulture) +
                                    " mm: " + RepeatedWhy(tract, chain, owners) + ")");
                        continue;
                    }
                    already.Add(tract);
                    if (!owners.ContainsKey(tract)) owners[tract] = "the pair " + pair[0] + "-" + pair[1] + " of this call";
                }
                if (k == 0) horizontalPairs.Add(new object[] { first, second, gapMm });
                else verticalPairs.Add(new object[] { first, second, gapMm });
            }
            if (horizontalPairs.Count + verticalPairs.Count == 0 && failed.Count == 0)
                return Failed("Every pair of «between» in the view «" + viewName + "» dimensions an extent the drawing " +
                              "already gives: " + string.Join("; ", refused) + ". Each extent is dimensioned once on the " +
                              "whole drawing (UNI ISO 129-1); pass «repeat»: true to place them anyway, or «parentheses»: " +
                              "true to write them as reference dimensions. I touched nothing.");
            if (horizontalPairs.Count + verticalPairs.Count == 0)
                return Failed("No pair of «between» can be dimensioned in the view «" + viewName + "»: " +
                              string.Join("; ", failed) + (refused.Count == 0 ? "" : "; already given by the drawing: " + string.Join("; ", refused)) +
                              ". Read the edges with «inspect_part» and «view_edges» " +
                              "(each edge with its number, how it faces on the sheet and where it sits). I touched nothing.");

            // FROM THE SMALL TO THE LARGE moving away from the part, as the edges are; in a
            // row they keep the order asked, side by side.
            if (!row)
            {
                horizontalPairs.Sort((x, y) => ((double)x[2]).CompareTo((double)y[2]));
                verticalPairs.Sort((x, y) => ((double)x[2]).CompareTo((double)y[2]));
            }

            int before = CountDimensions(drawing);
            if (before < 0)
                return Failed("I cannot count the dimensions of the drawing before starting: without that number I " +
                              "could not tell you which ones I put. I touched nothing.");
            // What the view already holds, read before the new ones are born: the layout lines up with
            // its rows and keeps off its texts (note 100, defect 1).
            int unreadInTheView;
            double[][] inTheView = DimensionsInTheView(view, mu, tr, out unreadInTheView);

            var born = new List<Dictionary<string, object>>();
            // An array, not a list: the obsolete-call contract cannot type a generic list.
            var placedItems = new BetweenPlaced[horizontalPairs.Count + verticalPairs.Count];
            int placedCount = 0;
            bool askedForTheValue = true, off = false;
            try
            {
                try
                {
                    askedForTheValue = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                    _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
                    off = true;
                }
                catch { /* if it cannot be switched off, the first one that hangs will say so */ }

                // FIRST EACH DIMENSION IS BORN, out of the way as before; where it goes is
                // decided after, when the width of every text can be read (LayOutDimensions).
                int step = 0;
                foreach (object[] item in horizontalPairs)
                {
                    var first = (ProjectedEdge)item[0];
                    var second = (ProjectedEdge)item[1];
                    double x = (first.FromSheet[0] + second.FromSheet[0]) / 2;
                    double y = above ? contour[3] + dimensionOffset + step * planStep
                                     : contour[1] - dimensionOffset - step * planStep;
                    if (!row) step++;
                    string failure;
                    IDisplayDimension d;
                    Dictionary<string, object> placed = PlaceBetween(model, drawing, edges, first, second, true, (double)item[2], x, y, out failure, out d);
                    if (placed == null) failed.Add(failure);
                    else if (failure == null) born.Add(placed);
                    else RecordBornWithoutObject(born, placed, failed, failure);
                    if (placed != null)
                        placedItems[placedCount++] = new BetweenPlaced { Row = placed, Dimension = d, First = first, Second = second, Horizontal = true, ExpectedMm = (double)item[2] };
                }
                step = 0;
                foreach (object[] item in verticalPairs)
                {
                    var first = (ProjectedEdge)item[0];
                    var second = (ProjectedEdge)item[1];
                    double y = (first.FromSheet[1] + second.FromSheet[1]) / 2;
                    double x = left ? contour[0] - dimensionOffset - step * planStep
                                    : contour[2] + dimensionOffset + step * planStep;
                    if (!row) step++;
                    string failure;
                    IDisplayDimension d;
                    Dictionary<string, object> placed = PlaceBetween(model, drawing, edges, first, second, false, (double)item[2], x, y, out failure, out d);
                    if (placed == null) failed.Add(failure);
                    else if (failure == null) born.Add(placed);
                    else RecordBornWithoutObject(born, placed, failed, failure);
                    if (placed != null)
                        placedItems[placedCount++] = new BetweenPlaced { Row = placed, Dimension = d, First = first, Second = second, Horizontal = false, ExpectedMm = (double)item[2] };
                }
            }
            finally
            {
                if (off)
                    try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, askedForTheValue); }
                    catch { }
                try { model.ClearSelection2(true); } catch { }
            }

            int after = CountDimensions(drawing);
            if (after < 0)
                return Failed("I put the dimensions in the view «" + viewName + "», but then I cannot count them again: " +
                              "I cannot tell you how many were created. Look at the drawing before going on.");
            int put = after - before;
            if (put <= 0)
                return Failed("No dimension «between» was created on the view «" + viewName + "» (there were " + before +
                              ", there are " + after + "): " + string.Join("; ", failed) + ".");

            // ── WHERE THEY GO: the owner's style (DrawingDimensionStyle.cs) ──
            // Measured from the lines the view draws: the outline keeps about 5.9 mm of margin.
            double[] drawn = DrawnBox(view);
            string measuredFrom = drawn != null ? "the lines the view draws" : null;
            // Without its lines, the extent of its edges: the outline keeps 5.9 mm more, and on 26M000002
            // detail B the 12 went that much further out than the owner's (note 100).
            if (drawn == null && (drawn = ExtentOf(all, contour)) != null) measuredFrom = "the edges of the view (its lines could not be read)";
            if (drawn == null) { drawn = contour; measuredFrom = "the outline of the view (neither its lines nor its edges could be read)"; }
            var toLay = new double[placedCount][];
            for (int i = 0; i < placedCount; i++)
            {
                BetweenPlaced p = placedItems[i];
                IDisplayDimension d = p.Dimension;
                int k = p.Horizontal ? 0 : 1, across = 1 - k;
                double[] ends = { p.First.FromSheet[across], p.First.ToSheet[across], p.Second.FromSheet[across], p.Second.ToSheet[across] };
                double sideAsked = p.Horizontal ? (horizontalNamed ? (above ? 2 : 1) : 0) : (verticalNamed ? (left ? 1 : 2) : 0);
                double box = d == null ? -1 : WidestText(d), height = d == null ? -1 : TextHeight(d);
                // The widest line: the value, or the text under the line («below»).
                double choice = Math.Max(TextWidthForTheChoice(p.Row.TryGetValue("text_shown", out var shown) ? shown as string : null, height, style),
                                         TextWidthForTheChoice(_dimensionBelow, height, style));
                p.Row["text_box_mm"] = box > 0 ? (object)Math.Round(box * 1000, 2) : null;
                p.Row["text_width_for_the_choice_mm"] = choice > 0 ? (object)Math.Round(choice * 1000, 2) : null;
                toLay[i] = new[]
                {
                    p.Horizontal ? 1.0 : 0.0,
                    Math.Min(p.First.FromSheet[k], p.Second.FromSheet[k]), Math.Max(p.First.FromSheet[k], p.Second.FromSheet[k]),
                    Math.Min(Math.Min(ends[0], ends[1]), Math.Min(ends[2], ends[3])), Math.Max(Math.Max(ends[0], ends[1]), Math.Max(ends[2], ends[3])),
                    box, p.ExpectedMm, sideAsked, choice, height,
                };
            }
            double[][] places = LayOutDimensions(toLay, drawn, style, row, SheetDimensions(drawing), inTheView);
            // The dimensions of the view once these are placed: the old ones and the new ones, for the label of a detail.
            var afterLayout = new List<double[]>(inTheView);
            double lowestUnder = double.NaN;
            for (int i = 0; i < placedCount && places != null; i++)
            {
                BetweenPlaced p = placedItems[i];
                IDisplayDimension d = p.Dimension;
                double[] at = places[i];
                if (d == null || at == null) continue;
                ApplyPlace(d, p.Row, at, p.Horizontal);
                double halfBox = toLay[i][5] > 0 ? toLay[i][5] / 2 : 0, halfHeight = (toLay[i][9] > 0 ? toLay[i][9] : 0.0035) / 2;
                afterLayout.Add(ExistingDimension(p.Horizontal ? 1 : 0, toLay[i][1], toLay[i][2], at[0], at[1],
                                                  p.Horizontal ? halfBox : halfHeight, p.Horizontal ? halfHeight : halfBox));
            }
            lowestUnder = LowestUnder(afterLayout.ToArray(), drawn);
            // THE LABEL OF A DETAIL UNDER ITS DIMENSIONS (the owner moves it there: 26P000008, detail A).
            Dictionary<string, object> detailLabel = IsDetailView(view) && !double.IsNaN(lowestUnder)
                ? MoveDetailLabel(view, lowestUnder, style.DetailLabelGapMm) : null;

            double[] afterContour = ContourOf(view);
            var texts = DimensionTexts(drawing);
            string overlapsCheckedAgainst = null;
            var overlapping = TextsThatOverlap(drawing, view, texts, out overlapsCheckedAgainst);
            var measurements = new Dictionary<string, object>
            {
                ["view"] = viewName,
                ["edges_available"] = edges.Length,
                ["edges_listed_by"] = "«inspect_part» with «view_edges» {\"view\": \"" + viewName +
                                      "\"}: each edge with the number «between» takes, its kind, its mm, how it faces " +
                                      "and where it sits on the sheet",
                ["pairs_asked"] = pairs.Count,
                ["dimensions_before"] = before,
                ["dimensions_after"] = after,
                ["dimensions_placed"] = put,
                ["dimensions"] = born.ToArray(),
                ["dimensions_mm"] = MillimetersInARow(born),
                ["dimensions_shown"] = TextsShownInARow(born),
                ["decimals_asked"] = _dimensionDecimals >= 0 ? (object)_dimensionDecimals : null,
                ["rounded_on_the_sheet"] = RoundedOnTheSheet(born).ToArray(),
                ["decorations_not_kept"] = DecorationsNotKept(born).ToArray(),
                ["edges_not_dimensioned"] = failed.ToArray(),
                ["laid_out"] = (row ? "in a row, " : "from the smaller to the larger, ") +
                               "the horizontal ones " + (horizontalNamed ? (above ? "above" : "below") : SideWords(style, true)) +
                               ", the vertical ones " + (verticalNamed ? "on the " + (left ? "left" : "right") : SideWords(style, false)),
                ["style"] = style.Describe(),
                ["measured_from"] = measuredFrom,
                ["detail_label"] = detailLabel,
                // WHAT THE DRAWING ALREADY GAVE (02/11/2026): the pairs refused, each with the
                // dimensions that give its extent; why a view was not compared; what was read.
                ["already_given_refused"] = refused.ToArray(),
                ["repeat"] = repeatAllowed,
                ["not_compared"] = notCompared,
                ["already_dimensioned_read_from"] =
                    (existingRead.EverySheet ? "every sheet" : "current sheet only") +
                    (existingRead.Complete ? "" : ", interrupted: some views were not read"),
                // WHAT THE VIEW HELD (02/11/2026): the dimensions already there the layout kept off.
                ["dimensions_already_in_the_view"] = inTheView.Length,
                ["dimensions_in_the_view_not_read"] = unreadInTheView,
                ["texts_read"] = texts == null ? null : (object)texts.Count,
                ["overlapping_texts"] = overlapping == null ? null : overlapping.ToArray(),
                ["how_many_texts_overlap"] = texts == null || overlapping == null || (texts.Count == 0 && after > 0)
                    ? null : (object)overlapping.Count,
                ["overlaps_checked_against"] = overlapsCheckedAgainst,
                ["contour_view_mm"] = InMillimeters(contour),
                ["contour_view_after_mm"] = InMillimeters(afterContour),
                ["offset_used_mm"] = Math.Round(style.FirstRowMm, 2),
                ["pitch_used_mm"] = Math.Round(style.PitchMm, 2),
            };
            _log("[agent] " + put + " dimensions «between» on the view «" + viewName + "»: " + ValuesInLine(born));
            return WithoutFeature(measurements);
        }

        /// <summary>
        /// Puts a dimension where the layout says (one row of LayOutDimensions): its text there, the
        /// arrows inside or outside as the text is, and in its row of the answer the side, the row,
        /// where the text is and where it was read back. Shared by «between» and the automatic route
        /// (note 98, defect 4).
        /// </summary>
        private static void ApplyPlace(IDisplayDimension d, Dictionary<string, object> row, double[] at, bool horizontal)
        {
            bool textInside = at[2] > 0.5;
            row["side"] = horizontal ? (at[3] > 0.5 ? "above" : "below") : (at[3] > 0.5 ? "right" : "left");
            row["row"] = (int)at[4] + 1;
            row["row_mm"] = Math.Round(at[5] * 1000, 2);
            row["text"] = textInside ? "between the extension lines"
                        : at[6] > 0.5 ? "outside, beyond the lower end" : "outside, beyond the higher end";
            row["text_moved_out"] = !textInside;
            try
            {
                var annotation = d.GetAnnotation() as IAnnotation;
                if (annotation != null) annotation.SetPosition2(at[0], at[1], 0);
            }
            catch { }
            row["position_mm"] = WhereItEnded(d);
            var ended = row["position_mm"] as object[];
            if (ended == null || Math.Abs(Convert.ToDouble(ended[0], CultureInfo.InvariantCulture) - at[0] * 1000) > 0.1 ||
                Math.Abs(Convert.ToDouble(ended[1], CultureInfo.InvariantCulture) - at[1] * 1000) > 0.1)
                row["layout_note"] = "laid out at x " + Math.Round(at[0] * 1000, 2).ToString(CultureInfo.InvariantCulture) + " y " +
                                     Math.Round(at[1] * 1000, 2).ToString(CultureInfo.InvariantCulture) + " mm, and it reads " +
                                     (ended == null ? "no position" : "x " + ended[0] + " y " + ended[1] + " mm") + ": move it with «move_annotation»";
            int wanted = textInside ? (int)swDimensionArrowsSide_e.swDimArrowsInside : (int)swDimensionArrowsSide_e.swDimArrowsOutside;
            int read = -1;
            try { d.ArrowSide = wanted; } catch { }
            try { read = d.ArrowSide; } catch { }
            row["arrows"] = read == (int)swDimensionArrowsSide_e.swDimArrowsInside ? "inside"
                          : read == (int)swDimensionArrowsSide_e.swDimArrowsOutside ? "outside" : null;
            if (read != wanted)
                row["arrows_note"] = "I asked the arrows " + (textInside ? "inside" : "outside") + " and SOLIDWORKS reads " +
                                     (read < 0 ? "nothing" : ((swDimensionArrowsSide_e)read).ToString()) + ": set them by hand";
        }

        /// <summary>The side of a direction in «laid_out» when the call names none: by the edges, or the tie side alone.</summary>
        private static string SideWords(DimensionStyle style, bool horizontal)
        {
            string tie = horizontal ? style.TieHorizontal : "on the " + style.TieVertical;
            if (!style.SideByEdges) return tie;
            return "on the side their edges reach (" + tie + " when both are as near)";
        }

        /// <summary>
        /// Moves the label of a detail under its lowest dimension (DetailLabelY): the label is the
        /// one note of the view (ViewWithItsNotes in DrawingDetailOperations.cs), read back
        /// after the move. A view with no note or more than one is left as it is, and the row
        /// says why. Not measured on SOLIDWORKS yet.
        /// </summary>
        private static Dictionary<string, object> MoveDetailLabel(IView view, double lowestText, double gapMm)
        {
            var row = new Dictionary<string, object> { ["moved"] = false };
            INote label = null;
            int notes = 0;
            try
            {
                for (var note = view.GetFirstNote() as INote; note != null && notes < 50; note = note.GetNext() as INote)
                {
                    notes++;
                    label = note;
                }
            }
            catch { }
            if (notes != 1)
            {
                row["why"] = notes == 0 ? "the detail has no note to move as its label"
                                        : "the detail has " + notes + " notes, and I do not know which one is its label";
                return row;
            }
            IAnnotation annotation = null;
            double[] before = null;
            try
            {
                annotation = label.GetAnnotation() as IAnnotation;
                before = annotation == null ? null : annotation.GetPosition() as double[];
            }
            catch { }
            if (before == null || before.Length < 2)
            {
                row["why"] = "I cannot read where the label of the detail is";
                return row;
            }
            row["before_mm"] = new object[] { Math.Round(before[0] * 1000, 2), Math.Round(before[1] * 1000, 2) };
            double? labelAt = DetailLabelY(before[1], lowestText, gapMm);
            if (labelAt == null)
            {
                row["why"] = "the label is already under the dimensions";
                return row;
            }
            double y = (double)labelAt;
            double[] after = null;
            try
            {
                annotation.SetPosition2(before[0], y, 0);
                after = annotation.GetPosition() as double[];
            }
            catch { }
            if (after != null && after.Length >= 2)
                row["after_mm"] = new object[] { Math.Round(after[0] * 1000, 2), Math.Round(after[1] * 1000, 2) };
            row["moved"] = after != null && after.Length >= 2 && Math.Abs(after[1] - y) < 0.0001;
            if (!(bool)row["moved"])
                row["why"] = "I asked the label at y " + Math.Round(y * 1000, 2).ToString(CultureInfo.InvariantCulture) +
                             " mm and it does not read there: move it with «move_annotation»";
            return row;
        }

        /// <summary>
        /// One distance between two parallel edges: placed, read back against the distance on
        /// the sheet divided by the scale, decorated. A value that is not that distance is
        /// taken away and said. Null with <paramref name="failure"/> when nothing stays; the
        /// row with <paramref name="failure"/> when the dimension was born without its object.
        /// Where it goes and its arrows are decided after (DimensionBetween, LayOutDimensions):
        /// <paramref name="placedDimension"/> hands it over, null when SOLIDWORKS did not.
        /// </summary>
        private Dictionary<string, object> PlaceBetween(
            IModelDoc2 model, IDrawingDoc drawing, object[] edges, ProjectedEdge first, ProjectedEdge second,
            bool horizontal, double expectedMm, double x, double y, out string failure, out IDisplayDimension placedDimension)
        {
            failure = null;
            placedDimension = null;
            string pairText = first.Index + "-" + second.Index;
            int dimensionsBefore = CountDimensions(drawing);
            IDisplayDimension d = null;
            string route = "AddDimension";
            try
            {
                model.ClearSelection2(true);
                if ((edges[first.Index] as IEntity).Select4(false, null) && (edges[second.Index] as IEntity).Select4(true, null))
                    d = model.Extension.AddDimension(x, y, 0, 0) as IDisplayDimension;
                try { model.EditRebuild3(); } catch { }
                if (NotGrown(drawing, dimensionsBefore, d))
                {
                    // The second road of the overall size: Horizontal/Vertical2 chosen by the direction.
                    route = horizontal ? "AddHorizontalDimension2" : "AddVerticalDimension2";
                    model.ClearSelection2(true);
                    if ((edges[first.Index] as IEntity).Select4(false, null) && (edges[second.Index] as IEntity).Select4(true, null))
                        d = (horizontal ? model.AddHorizontalDimension2(x, y, 0) : model.AddVerticalDimension2(x, y, 0)) as IDisplayDimension;
                    try { model.EditRebuild3(); } catch { }
                }
            }
            catch (Exception ex) { failure = pairText + " (" + Shorter(ex.Message) + ")"; return null; }
            if (NotGrown(drawing, dimensionsBefore, d))
            {
                failure = pairText + " (the dimension between the two edges was not created)";
                return null;
            }
            var described = DescribeDimension(d, first.Index);
            object mm;
            described.TryGetValue("mm", out mm);
            if (d != null && (!(mm is double) || Math.Abs((double)mm - expectedMm) > 0.05))
            {
                bool gone = RemoveNewAnnotation(model, d.GetAnnotation() as IAnnotation, () => CountDimensions(drawing), dimensionsBefore);
                failure = pairText + " (SOLIDWORKS wrote " + (mm is double ? ((double)mm).ToString("0.###", Ci) : "?") +
                          " mm instead of " + expectedMm.ToString("0.###", Ci) + ", the distance of the two edges on the sheet" +
                          (gone ? ": I took it away)" : ": remove it with «delete_annotation»)");
                return null;
            }
            described["orientation"] = horizontal ? "between, horizontal" : "between, vertical";
            described["edges"] = new object[] { first.Index, second.Index };
            if (first.Component != null || second.Component != null)
                described["components"] = new object[] { first.Component, second.Component };
            described["expected_mm"] = Math.Round(expectedMm, 3);
            described["placed_with"] = route;
            described["position_mm"] = WhereItEnded(d);
            described["text_shown"] = ExactTextShown(d, described);
            DecorateDimension(d, described);
            placedDimension = d;
            if (d == null)
                failure = pairText + " (the dimension was born but SOLIDWORKS did not hand it back: its value was not read back, check it on the sheet)";
            return described;
        }
    }
}
