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
            if (string.IsNullOrEmpty(_dimensionPrefix) && string.IsNullOrEmpty(_dimensionSuffix) && !_dimensionParentheses) return;
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
            if (note != null) row["decoration_note"] = note;
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

        /// <summary>The widest text of a dimension on the sheet, metres; 0 when it cannot be read.</summary>
        private static double WidestText(IDisplayDimension d)
        {
            double widest = 0;
            try
            {
                var data = d.GetDisplayData() as IDisplayData;
                int n = data == null ? 0 : data.GetTextCount();
                for (int i = 0; i < n; i++) widest = Math.Max(widest, data.GetTextInBoxWidthAtIndex(i));
            }
            catch { }
            return widest;
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
        private Outcome DimensionBetween(
            IModelDoc2 model, IDrawingDoc drawing, IView view, string viewName, object[] edges,
            List<ProjectedEdge> all, List<Circle> circles, List<int[]> pairs, double[] contour,
            double dimensionOffset, double planStep, double scale, bool row, bool above, bool left)
        {
            var failed = new List<string>();
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
                if (k == 0) horizontalPairs.Add(new object[] { first, second, gapMm });
                else verticalPairs.Add(new object[] { first, second, gapMm });
            }
            if (horizontalPairs.Count + verticalPairs.Count == 0)
                return Failed("No pair of «between» can be dimensioned in the view «" + viewName + "»: " +
                              string.Join("; ", failed) + ". Read the edges with «inspect_part» and «view_edges» " +
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

            var born = new List<Dictionary<string, object>>();
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
                    Dictionary<string, object> placed = PlaceBetween(model, drawing, edges, first, second, true, (double)item[2], x, y, out failure);
                    if (placed == null) failed.Add(failure);
                    else if (failure == null) born.Add(placed);
                    else RecordBornWithoutObject(born, placed, failed, failure);
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
                    Dictionary<string, object> placed = PlaceBetween(model, drawing, edges, first, second, false, (double)item[2], x, y, out failure);
                    if (placed == null) failed.Add(failure);
                    else if (failure == null) born.Add(placed);
                    else RecordBornWithoutObject(born, placed, failed, failure);
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
                               "the horizontal ones " + (above ? "above" : "below") +
                               ", the vertical ones on the " + (left ? "left" : "right"),
                ["not_compared_with_other_views"] =
                    "a distance asked with «between» is placed even when another view dimensions the same extent: " +
                    "you named it",
                ["texts_read"] = texts == null ? null : (object)texts.Count,
                ["overlapping_texts"] = overlapping == null ? null : overlapping.ToArray(),
                ["how_many_texts_overlap"] = texts == null || overlapping == null || (texts.Count == 0 && after > 0)
                    ? null : (object)overlapping.Count,
                ["overlaps_checked_against"] = overlapsCheckedAgainst,
                ["contour_view_mm"] = InMillimeters(contour),
                ["contour_view_after_mm"] = InMillimeters(afterContour),
                ["offset_used_mm"] = Math.Round(dimensionOffset * 1000, 2),
                ["pitch_used_mm"] = Math.Round(planStep * 1000, 2),
            };
            _log("[agent] " + put + " dimensions «between» on the view «" + viewName + "»: " + ValuesInLine(born));
            return WithoutFeature(measurements);
        }

        /// <summary>
        /// One distance between two parallel edges: placed, read back against the distance on
        /// the sheet divided by the scale, decorated. A value that is not that distance is
        /// taken away and said. Null with <paramref name="failure"/> when nothing stays; the
        /// row with <paramref name="failure"/> when the dimension was born without its object.
        /// </summary>
        private Dictionary<string, object> PlaceBetween(
            IModelDoc2 model, IDrawingDoc drawing, object[] edges, ProjectedEdge first, ProjectedEdge second,
            bool horizontal, double expectedMm, double x, double y, out string failure)
        {
            failure = null;
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
            int k = horizontal ? 0 : 1;
            ArrowsByText(d, described, first.FromSheet[k], second.FromSheet[k], horizontal);
            if (d == null)
                failure = pairText + " (the dimension was born but SOLIDWORKS did not hand it back: its value was not read back, check it on the sheet)";
            return described;
        }
    }
}
