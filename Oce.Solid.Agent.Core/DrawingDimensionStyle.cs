using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// HOW THE OWNER LAYS OUT A DIMENSION, MEASURED ON HIS DRAWINGS (31/10/2026).
    ///
    /// On 30/10 the drawings of 26P000008, 26P000009, 26P000011 and 26M000002 were made again
    /// with bridge 0.160.3 and every dimension was then moved by hand to the place the owner
    /// gives it: 87 «move_annotation» on 5 drawings. The rules here put the dimension there
    /// in the first place. They were read from his drawings (read_drawing) against the edges
    /// of the same views (inspect_part view_edges), with the program stile_quote.py 1.2.1 of
    /// the internal notes, and checked by laying out again every dimension of the four
    /// drawings: 51 linear dimensions, 36 of them placed by his hand (the other 15 were left
    /// where bridge 0.152.10 had put them on 28/10, 12 mm from the outline). With the layout
    /// of bridge 0.160.4 1 of the 36 lands within 5 mm of his text; with these rules 13
    /// (8 within 2 mm), 15 of 36 when the side is the one he chose. The side is what misses
    /// most: the edges alone do not say which side he takes when both are as near.
    ///
    /// What was measured (sheet millimetres, from the lines the view draws, not from its
    /// outline: GetOutline keeps about 5.9 mm of margin around them):
    ///  * the first row of dimensions 10 mm from the lines (median of the first rows he
    ///    placed; four of them at exactly 10.0);
    ///  * 6.7 mm from one row to the next (median of ten steps, 4.0 to 12.0);
    ///  * a text that does not fit between the extension lines goes beyond the higher end
    ///    (13 of 18), its centre 11 mm from the extension line (median 10.7);
    ///  * the text stays between the extension lines with 9.5 mm of room: an arrow takes
    ///    2 mm, not 3.5 (with 2 mm the choice inside or outside is his in 46 of 51, with
    ///    3.5 in 42 of 51; the text widths of that count are estimated, 1.8 mm a digit);
    ///  * the side of the part the dimensioned edges reach (20 of 24); when both sides are
    ///    as near, the horizontal ones above (6 of 7) and the vertical ones on the right
    ///    (4 against 4: the old default kept); a horizontal one from end to end of the lines
    ///    of the view on a tie below, the other side (193,6 and 15,6, 2 of 2, 02/11/2026);
    ///  * dimensions that do not overlap share a row, the short ones with the text outside
    ///    nearest the part: a chain, 19 / 145 / 11 in one row on 26P000008;
    ///  * the label of a detail goes under its lowest dimension, 6.2 mm below its text
    ///    (one detail, 26P000008 A: a single case, a hypothesis until more are measured).
    ///
    /// Every number is a default: a JSON file named by OCE_DRAWING_STYLE changes them, and
    /// «offset_mm», «pitch_mm», «horizontal_side», «vertical_side» and «row» of a call win
    /// over both. Not measured on SOLIDWORKS: the rules are pure and checked by the contract
    /// suite on the owner's numbers; the drawings made again with them are the next measure.
    ///
    /// THE REAL RUN OF 02/11/2026 (note 100): the four drawings made again on SOLIDWORKS with
    /// bridge 0.161.0 and no hand move put 10 of 52 texts within 5 mm of his, not the 15 the
    /// numbers promised. Five causes, cured here and checked by RealRunContract on the readings
    /// of that run: the layout saw only its own call (a second call on the view wrote over the
    /// first: rows and texts already in the view are now seen, RowsOfASide, LayOutDimensions);
    /// the choice inside or outside used the box SOLIDWORKS reads, 2.58 mm a digit plus 2.6 mm
    /// of margin, so the 11, 9,5 and 10,9 went outside (TextWidthForTheChoice: the widths the
    /// rules were measured with, 1.8 mm a digit and 1 mm a comma); a text outside could only go
    /// beyond the higher end, a row further when that was taken (TextBothEnds); the label of a
    /// detail looked only at its own call (LowestUnder); the sides and distances measured with the
    /// 10 dimensions bridge 0.152.10 had placed counted as his (stile_quote.py 1.5.0, its mode of the rules,
    /// measures his 41 alone: first row 10.0 mm, pitch 6.96 mm, the side of the edges 18 of 23).
    /// </summary>
    internal sealed class DimensionStyle
    {
        /// <summary>From the lines the view draws to the first row of dimensions, sheet mm.</summary>
        internal double FirstRowMm = 10;
        /// <summary>From one row of dimensions to the next, sheet mm.</summary>
        internal double PitchMm = 6.7;
        /// <summary>From the extension line to the centre of a text set outside, sheet mm.</summary>
        internal double TextOutsideMm = 11;
        /// <summary>The room an arrow takes between the extension lines, sheet mm.</summary>
        internal double ArrowRoomMm = 2;
        /// <summary>From the text of the lowest dimension of a detail down to its label, sheet mm.</summary>
        internal double DetailLabelGapMm = 6.2;
        /// <summary>Each dimension on the side of the part its edges reach, when the call names no side.</summary>
        internal bool SideByEdges = true;
        /// <summary>Dimensions that do not overlap share a row; false gives one row per dimension.</summary>
        internal bool Pack = true;
        /// <summary>The side of a horizontal dimension when the edges reach both sides as near: above or below.</summary>
        internal string TieHorizontal = "above";
        /// <summary>The side of a vertical dimension when the edges reach both sides as near: right or left.</summary>
        internal string TieVertical = "right";
        /// <summary>When the edges reach both sides as near, the side with fewer dimensions of that direction; as many: the tie side.</summary>
        internal bool TieBalance = false;
        /// <summary>A text outside goes beyond the lower end when the higher end is taken, before going a row further.</summary>
        internal bool TextBothEnds = true;
        /// <summary>The width of a character for the choice inside or outside, sheet mm for a text 3.5 mm high.</summary>
        internal double CharMm = 1.8;
        /// <summary>The width of a comma or a point for that choice, sheet mm for a text 3.5 mm high.</summary>
        internal double CommaMm = 1.0;
        /// <summary>Where the numbers come from: «built in» or the file named by OCE_DRAWING_STYLE.</summary>
        internal string Source = "built in";

        internal DimensionStyle Copy() { return (DimensionStyle)MemberwiseClone(); }

        internal Dictionary<string, object> Describe()
        {
            return new Dictionary<string, object>
            {
                ["first_row_mm"] = FirstRowMm,
                ["pitch_mm"] = PitchMm,
                ["text_outside_mm"] = TextOutsideMm,
                ["arrow_room_mm"] = ArrowRoomMm,
                ["detail_label_gap_mm"] = DetailLabelGapMm,
                ["side_by_edges"] = SideByEdges,
                ["pack"] = Pack,
                ["tie_horizontal"] = TieHorizontal,
                ["tie_vertical"] = TieVertical,
                ["tie_balance"] = TieBalance,
                ["text_both_ends"] = TextBothEnds,
                ["char_mm"] = CharMm,
                ["comma_mm"] = CommaMm,
                ["source"] = Source,
            };
        }
    }

    public partial class SwOperations
    {
        /// <summary>The environment variable that names a JSON file with the dimension style.</summary>
        internal const string DrawingStyleVariable = "OCE_DRAWING_STYLE";

        /// <summary>The keys a style file takes; any other key is refused, so that a typing error is not ignored.</summary>
        internal static readonly string[] StyleKeys =
        {
            "first_row_mm", "pitch_mm", "text_outside_mm", "arrow_room_mm", "detail_label_gap_mm",
            "side_by_edges", "pack", "tie_horizontal", "tie_vertical", "tie_balance", "text_both_ends", "char_mm", "comma_mm",
        };

        /// <summary>
        /// The style written in <paramref name="json"/> over the built-in one; null with
        /// <paramref name="problem"/> when it cannot be used: not a JSON object, a key it does
        /// not take, a number out of its range, a word it does not take. Pure, so the
        /// contract suite reads it.
        /// </summary>
        internal static DimensionStyle StyleFrom(string json, string source, out string problem)
        {
            problem = null;
            var style = new DimensionStyle { Source = source ?? "built in" };
            if (string.IsNullOrWhiteSpace(json)) return style;
            Dictionary<string, object> values;
            try { values = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; }
            catch (Exception ex) { problem = "it is not JSON (" + ex.Message + ")"; return null; }
            if (values == null) { problem = "it is not a JSON object of keys and values"; return null; }
            foreach (string key in values.Keys)
            {
                object value = values[key];
                if (Array.IndexOf(StyleKeys, key) < 0)
                {
                    problem = "the key «" + key + "» is not a key of the style; the keys are " + string.Join(", ", StyleKeys);
                    return null;
                }
                string why = null;
                switch (key)
                {
                    case "first_row_mm": why = StyleNumber(key, value, 0, 60, v => style.FirstRowMm = v); break;
                    case "pitch_mm": why = StyleNumber(key, value, 1, 40, v => style.PitchMm = v); break;
                    case "text_outside_mm": why = StyleNumber(key, value, 0, 60, v => style.TextOutsideMm = v); break;
                    case "arrow_room_mm": why = StyleNumber(key, value, 0, 10, v => style.ArrowRoomMm = v); break;
                    case "detail_label_gap_mm": why = StyleNumber(key, value, 0, 60, v => style.DetailLabelGapMm = v); break;
                    case "side_by_edges":
                        if (value is bool) style.SideByEdges = (bool)value; else why = "«side_by_edges» is true or false";
                        break;
                    case "pack":
                        if (value is bool) style.Pack = (bool)value; else why = "«pack» is true or false";
                        break;
                    case "tie_horizontal":
                        if (value as string == "above" || value as string == "below") style.TieHorizontal = (string)value;
                        else why = "«tie_horizontal» is «above» or «below», not «" + value + "»";
                        break;
                    case "tie_vertical":
                        if (value as string == "right" || value as string == "left") style.TieVertical = (string)value;
                        else why = "«tie_vertical» is «right» or «left», not «" + value + "»";
                        break;
                    case "tie_balance":
                        if (value is bool) style.TieBalance = (bool)value; else why = "«tie_balance» is true or false";
                        break;
                    case "text_both_ends":
                        if (value is bool) style.TextBothEnds = (bool)value; else why = "«text_both_ends» is true or false";
                        break;
                    case "char_mm": why = StyleNumber(key, value, 0.5, 10, v => style.CharMm = v); break;
                    case "comma_mm": why = StyleNumber(key, value, 0, 10, v => style.CommaMm = v); break;
                }
                if (why != null) { problem = why; return null; }
            }
            return style;
        }

        private static string StyleNumber(string key, object value, double low, double high, Action<double> set)
        {
            double v;
            if (value is string || value is bool || value == null) return "«" + key + "» is a number of millimetres";
            try { v = Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { return "«" + key + "» is a number of millimetres, not «" + value + "»"; }
            if (double.IsNaN(v) || v < low || v > high)
                return "«" + key + "» is " + v.ToString(CultureInfo.InvariantCulture) + " mm, outside " +
                       low.ToString(CultureInfo.InvariantCulture) + " to " + high.ToString(CultureInfo.InvariantCulture);
            set(v);
            return null;
        }

        /// <summary>
        /// The style of this call: the built-in one, or the file named by OCE_DRAWING_STYLE read
        /// now (a file changed between two calls counts at the next). Null with the reason
        /// when the variable names a file that cannot be read or used: a style asked and not
        /// applied would lay out the drawing another way without a word.
        /// </summary>
        internal static DimensionStyle StyleOfThisCall(out string problem)
        {
            problem = null;
            string path = null;
            try { path = Environment.GetEnvironmentVariable(DrawingStyleVariable); } catch { }
            if (string.IsNullOrWhiteSpace(path)) return new DimensionStyle();
            string text;
            try { text = File.ReadAllText(path.Trim()); }
            catch (Exception ex)
            {
                problem = DrawingStyleVariable + " names «" + path + "», which cannot be read (" + ex.Message + ")";
                return null;
            }
            string why;
            var style = StyleFrom(text, path.Trim(), out why);
            if (style == null) problem = "the style file «" + path + "» named by " + DrawingStyleVariable + " cannot be used: " + why;
            return style;
        }

        /// <summary>
        /// The side of the part a dimension goes on, from where its edges reach (the four ends
        /// of the two edges on the axis across the dimension, metres of sheet, from
        /// <paramref name="endsLow"/> to <paramref name="endsHigh"/>): «below» or «above» for a
        /// horizontal distance, «left» or «right» for a vertical one, whichever side of
        /// <paramref name="drawn"/> (x0, y0, x1, y1) the edges come nearer to; <paramref name="tie"/>
        /// when the two sides are within half a millimetre. Pure.
        /// </summary>
        internal static string SideOfTheEdges(bool horizontal, double endsLow, double endsHigh, double[] drawn, string tie)
        {
            if (drawn == null || drawn.Length < 4) return tie;
            double lowGap = endsLow - (horizontal ? drawn[1] : drawn[0]);
            double highGap = (horizontal ? drawn[3] : drawn[2]) - endsHigh;
            if (Math.Abs(lowGap - highGap) < 0.0005) return tie;
            return lowGap < highGap ? (horizontal ? "below" : "left") : (horizontal ? "above" : "right");
        }

        /// <summary>The lower side of a direction: below for a horizontal dimension, left for a vertical one.</summary>
        private static bool IsLowSide(string side) { return side == "below" || side == "left"; }

        /// <summary>
        /// The width of a text for the choice inside or outside (ArrowsFor), metres of sheet: CharMm
        /// a character, CommaMm a comma or a point, for a text 3.5 mm high, scaled to
        /// <paramref name="height"/> (metres; 0 or less counts as 3.5 mm). NOT the box SOLIDWORKS
        /// reads (GetTextInBoxWidthAtIndex): that box is 2.58 mm a digit plus 2.6 mm of margin
        /// (probe0089: «10» 7.76 mm, «120» 10.34 mm), and with it bridge 0.161.0 put outside the
        /// 11, the 9,5 and the 10,9 the owner writes between the extension lines (note 100, defect
        /// 4). With the box the choice is his in 33 of his 41 dimensions, with these widths in 38
        /// (stile_quote.py 1.5.0, its mode of the rules). The box stays the room the text takes. Pure.
        /// </summary>
        internal static double TextWidthForTheChoice(string shown, double height, DimensionStyle style)
        {
            if (string.IsNullOrEmpty(shown) || style == null) return 0;
            double mm = 0;
            foreach (char c in shown.Trim()) mm += c == ',' || c == '.' ? style.CommaMm : style.CharMm;
            return mm / 1000.0 * (height > 0 ? height / 0.0035 : 1);
        }

        /// <summary>
        /// One dimension already in the view, the way LayOutDimensions takes it: {kind (1 a
        /// horizontal distance, 0 a vertical one, -1 any other dimension, of which only the text
        /// counts), along low, along high (what it takes along its distance: the extension lines and
        /// its text), x, y of its text, half width and half height of its text on the sheet}.
        /// Metres of sheet. Pure.
        /// </summary>
        internal static double[] ExistingDimension(double kind, double low, double high, double x, double y, double halfWidth, double halfHeight)
        {
            double lo = Math.Min(low, high), hi = Math.Max(low, high);
            if (kind > 0.5) { lo = Math.Min(lo, x - halfWidth); hi = Math.Max(hi, x + halfWidth); }
            else if (kind > -0.5) { lo = Math.Min(lo, y - halfHeight); hi = Math.Max(hi, y + halfHeight); }
            return new[] { kind, lo, hi, x, y, halfWidth, halfHeight };
        }

        /// <summary>
        /// The side of the view an existing dimension (ExistingDimension) is on: -1 the low side
        /// (below, left), 1 the high side (above, right), 0 over the view; its text's distance
        /// from the lines of the view in <paramref name="distance"/>. Pure.
        /// </summary>
        private static int ExistingSide(double[] e, double[] drawn, out double distance)
        {
            bool horizontal = e[0] > 0.5;
            double across = horizontal ? e[4] : e[3];
            double lo = horizontal ? drawn[1] : drawn[0], hi = horizontal ? drawn[3] : drawn[2];
            distance = 0;
            if (across < lo) { distance = lo - across; return -1; }
            if (across > hi) { distance = across - hi; return 1; }
            return 0;
        }

        /// <summary>
        /// The rows of one side, metres from the lines of the view, nearest first: the rows the
        /// dimensions already there use (<paramref name="taken"/>, from 0.6 of the first row out;
        /// two within 0.6 mm are one row), and at least <paramref name="wanted"/> rows of the
        /// style, first + r x pitch, each 0.6 of a pitch at least from a row already used. The
        /// owner lines his new dimensions up with a row that is there: the 19 and the 11 of
        /// 26P000008 view 1 on the row of the 145. Pure.
        /// </summary>
        internal static double[] RowsOfASide(double[] taken, double first, double pitch, int wanted)
        {
            var used = new List<double>();
            if (taken != null)
                foreach (double d in taken.OrderBy(x => x))
                    if (d >= 0.6 * first && (used.Count == 0 || d - used[used.Count - 1] > 0.0006)) used.Add(d);
            var rows = new List<double>(used);
            int fromStyle = 0;
            for (int r = 0; fromStyle < wanted && r < wanted + 1000; r++)
            {
                double g = first + r * pitch;
                bool clear = true;
                foreach (double d in used) if (Math.Abs(g - d) < 0.6 * pitch) { clear = false; break; }
                if (!clear) continue;
                rows.Add(g);
                fromStyle++;
            }
            rows.Sort();
            return rows.ToArray();
        }

        /// <summary>
        /// The text of the lowest horizontal dimension under the view, metres of sheet; NaN when
        /// none is under it. <paramref name="dimensions"/> are rows of ExistingDimension: those
        /// already in the view and those just placed, so that a detail dimensioned in two calls
        /// moves its label under all of them (note 100, defect 5: 26P000008 A, its 1,5 and its 2,5
        /// came from two calls). Pure.
        /// </summary>
        internal static double LowestUnder(double[][] dimensions, double[] drawn)
        {
            double lowest = double.NaN;
            if (dimensions == null || drawn == null || drawn.Length < 4) return lowest;
            foreach (double[] e in dimensions)
                if (e != null && e.Length >= 7 && e[0] > 0.5 && e[4] < drawn[1] && (double.IsNaN(lowest) || e[4] < lowest)) lowest = e[4];
            return lowest;
        }

        /// <summary>Whether two rectangles {x0, y0, x1, y1} come nearer than <paramref name="clearance"/>.</summary>
        private static bool TooNear(double[] a, double[] b, double clearance)
        {
            return a[0] < b[2] + clearance && b[0] < a[2] + clearance && a[1] < b[3] + clearance && b[1] < a[3] + clearance;
        }

        /// <summary>
        /// WHERE EACH DIMENSION OF A CALL GOES, in metres of sheet. One row per dimension of
        /// <paramref name="dims"/>: {horizontal 1 or 0 (a horizontal distance, between two
        /// vertical edges), low, high (the two extension lines along the axis of the distance),
        /// ends low, ends high (where the edges reach across it), text width (the box on the sheet,
        /// the room the text takes; 0 or less when unknown), value in mm (the order), side asked (0
        /// none, 1 the low side, 2 the high side), and optionally text width for the choice
        /// inside or outside (TextWidthForTheChoice; 0 or missing: the box) and text height (0 or
        /// missing: 3.5 mm)}. <paramref name="existing"/>: the dimensions already in the view
        /// (ExistingDimension), null for none. One row back for each: {x, y of the text, 1 when the
        /// text is between the extension lines, 1 on the high side (above, right), the row from 0,
        /// the distance of the row from the lines in metres, 1 when the text is beyond the lower end}.
        ///
        /// The side is the one asked, or the one of the edges (SideOfTheEdges) when the style says
        /// so; when the edges reach both sides as near, the tie side, or with TieBalance the side
        /// with fewer dimensions of that direction; a horizontal one from end to end of the lines
        /// of the view on a tie takes the other side. A side not asked whose first row would put its
        /// text over the frame of the sheet (<paramref name="sheet"/>, width and height, null when
        /// unknown; FrameOfTheSheet) turns to the other.
        /// The text is between the extension lines when it fits with an arrow on each side
        /// (ArrowsFor), otherwise beyond the higher end, or with TextBothEnds beyond the lower end
        /// when the higher one is taken or lies over another dimension of its side. On each side the
        /// dimensions with the text outside come first, then the chains (ChainsOf: those with the
        /// text inside that follow one another end to end go in one row together) from the smaller
        /// to the larger; with <paramref name="row"/> all in one row, the first that the dimensions
        /// already there leave free, in the order asked.
        ///
        /// THE ROWS SEE WHAT IS ALREADY IN THE VIEW (note 100, defect 1, 02/11/2026). Until 0.162.0
        /// the layout saw only the dimensions of its own call, and a second call on the same view
        /// (another «decimals», «suffix» or «parentheses» needs one) wrote its texts over the first
        /// ones: the 17 over the 11,9 of 26P000011, five pairs on 26P000008. Now the rows of a side
        /// are those the dimensions already there use and those of the style between them
        /// (RowsOfASide), and each dimension takes the first where its extent overlaps no other of
        /// that row (two that only touch share it) and its text comes no nearer than 0.5 mm to any
        /// text, of either direction, old or new. With Pack false one row each. Null when the box is
        /// missing. Pure, so the contract suite reads it.
        /// </summary>
        /// <summary>The frame of the sheet, metres from the edge of the paper: the one of the free places (FreeSheet.FrameBorder).</summary>
        internal const double FrameOfTheSheet = 0.012;

        internal static double[][] LayOutDimensions(double[][] dims, double[] drawn, DimensionStyle style, bool row, double[] sheet, double[][] existing)
        {
            if (dims == null || drawn == null || drawn.Length < 4 || style == null) return null;
            double first = style.FirstRowMm / 1000.0, pitch = style.PitchMm / 1000.0;
            double outside = style.TextOutsideMm / 1000.0, arrow = style.ArrowRoomMm / 1000.0;
            const double touch = 0.00005, clearance = 0.0005;
            double[][] there = existing ?? new double[0][];
            int n = dims.Length;
            var result = new double[n][];
            var sides = new string[n];
            var inside = new bool[n];
            var width = new double[n];
            var height = new double[n];

            // How many dimensions of each direction each side already has, for TieBalance.
            int lowH = 0, highH = 0, lowV = 0, highV = 0;
            foreach (double[] e in there)
            {
                if (e == null || e.Length < 7 || e[0] < -0.5) continue;
                double dist;
                int s = ExistingSide(e, drawn, out dist);
                if (e[0] > 0.5) { if (s < 0) lowH++; else if (s > 0) highH++; }
                else { if (s < 0) lowV++; else if (s > 0) highV++; }
            }
            for (int i = 0; i < n; i++)
            {
                double[] d = dims[i];
                bool horizontal = d[0] > 0.5;
                double low = Math.Min(d[1], d[2]), high = Math.Max(d[1], d[2]);
                width[i] = d[5] > 0 ? d[5] : 0;
                height[i] = d.Length > 9 && d[9] > 0 ? d[9] : 0.0035;
                double choice = d.Length > 8 && d[8] > 0 ? d[8] : d[5];
                inside[i] = ArrowsFor(high - low, choice, arrow) == "inside";
                string tie = horizontal ? style.TieHorizontal : style.TieVertical;
                string side;
                if (d[7] > 1.5) side = horizontal ? "above" : "right";
                else if (d[7] > 0.5) side = horizontal ? "below" : "left";
                else
                {
                    side = style.SideByEdges ? SideOfTheEdges(horizontal, d[3], d[4], drawn, null) : null;
                    if (side == null)
                    {
                        int onLow = horizontal ? lowH : lowV, onHigh = horizontal ? highH : highV;
                        side = !style.TieBalance || onLow == onHigh ? tie
                             : onLow < onHigh ? (horizontal ? "below" : "left") : (horizontal ? "above" : "right");
                        // THE OVERALL ONE ON THE OTHER SIDE (note 98: the 193,6 of 26M000002 view 1 above, the
                        // owner's below). Of his horizontal dimensions on a tie the two overall ones, from end
                        // to end of the lines of the view, are on the side the tie does not take (193,6 and the
                        // 15,6 of 26P000009 view 2, 2 of 2), and the partial ones of those views on the tie side
                        // (35, 135, 11,6, 2). Horizontal only: of the two overall vertical ones on a tie one is
                        // on each side. Not with TieBalance, which counts the sides itself.
                        if (style.SideByEdges && horizontal && !style.TieBalance &&
                            Math.Abs(low - drawn[0]) < 0.0006 && Math.Abs(high - drawn[2]) < 0.0006)
                            side = IsLowSide(tie) ? "above" : "below";
                    }
                    if (sheet != null && sheet.Length >= 2 && sheet[0] > 0 && sheet[1] > 0)
                    {
                        // The text of the first row against the frame (FrameOfTheSheet), not the edge of
                        // the paper: with 5 mm from the edge the 1,8 and the 3,5 of 26P000007 view 2
                        // stayed on the left at x 6.5 mm («offset_mm» 30, 02/11/2026). Across its row a
                        // text takes its height, a vertical one too, turned.
                        double at = horizontal ? (IsLowSide(side) ? drawn[1] - first : drawn[3] + first)
                                               : (IsLowSide(side) ? drawn[0] - first : drawn[2] + first);
                        double limit = horizontal ? sheet[1] : sheet[0], halfText = height[i] / 2;
                        if (at - halfText < FrameOfTheSheet || at + halfText > limit - FrameOfTheSheet)
                            side = horizontal ? (IsLowSide(side) ? "above" : "below") : (IsLowSide(side) ? "right" : "left");
                    }
                }
                sides[i] = side;
                if (horizontal) { if (IsLowSide(side)) lowH++; else highH++; }
                else { if (IsLowSide(side)) lowV++; else highV++; }
            }

            // The texts already in the view, as rectangles {x0, y0, x1, y1}: no new text comes near them.
            var oldTexts = new List<double[]>();
            foreach (double[] e in there)
                if (e != null && e.Length >= 7)
                    oldTexts.Add(new[] { e[3] - e[5], e[4] - e[6], e[3] + e[5], e[4] + e[6] });
            // The rows are kept as one number per dimension, not as lists: the obsolete-call
            // contract cannot type a generic list, and an Add on it counts as undecided.
            var rowOf = new int[n];
            var placed = new bool[n];
            var takenLo = new double[n];
            var takenHi = new double[n];
            var textBox = new double[n][];
            var groupOf = new int[n];
            int groupId = 0;
            foreach (var group in Enumerable.Range(0, n).GroupBy(i => (dims[i][0] > 0.5 ? "h" : "v") + sides[i]))
            {
                int[] members = group.ToArray();
                groupId++;
                bool horizontal = dims[members[0]][0] > 0.5, lowSide = IsLowSide(sides[members[0]]);
                // The dimensions of this direction already on this side: {distance, along low, along high}.
                var onThisSide = new List<double[]>();
                foreach (double[] e in there)
                {
                    if (e == null || e.Length < 7 || e[0] < -0.5 || (e[0] > 0.5) != horizontal) continue;
                    double dist;
                    if (ExistingSide(e, drawn, out dist) == (lowSide ? -1 : 1)) onThisSide.Add(new[] { dist, e[1], e[2] });
                }
                double[] distances = new double[onThisSide.Count];
                for (int k = 0; k < distances.Length; k++) distances[k] = onThisSide[k][0];
                double[] rows = RowsOfASide(distances, first, pitch, members.Length + onThisSide.Count + 4);
                // Puts dimension i in row r, its text between the lines or beyond one end.
                void Put(int i, int r, bool lowEnd)
                {
                    double low = Math.Min(dims[i][1], dims[i][2]), high = Math.Max(dims[i][1], dims[i][2]);
                    double half = width[i] / 2;
                    double along = inside[i] ? (low + high) / 2 : lowEnd ? low - outside : high + outside;
                    takenLo[i] = inside[i] ? low : lowEnd ? low - outside - half : low;
                    takenHi[i] = inside[i] ? high : lowEnd ? high : high + outside + half;
                    double distance = rows[r];
                    double across = horizontal ? (lowSide ? drawn[1] - distance : drawn[3] + distance)
                                               : (lowSide ? drawn[0] - distance : drawn[2] + distance);
                    textBox[i] = TextRectangle(horizontal, along, across, width[i], height[i]);
                    rowOf[i] = r;
                    groupOf[i] = groupId;
                    placed[i] = true;
                    result[i] = horizontal
                        ? new[] { along, across, inside[i] ? 1.0 : 0.0, lowSide ? 0.0 : 1.0, r, distance, lowEnd ? 1.0 : 0.0 }
                        : new[] { across, along, inside[i] ? 1.0 : 0.0, lowSide ? 0.0 : 1.0, r, distance, lowEnd ? 1.0 : 0.0 };
                }
                bool Free(int i, int r, bool lowEnd)
                {
                    return !Blocked(dims[i], rows[r], inside[i], lowEnd, width[i], height[i], horizontal, lowSide, drawn, outside,
                                    pitch, touch, clearance, onThisSide, oldTexts, rowOf, placed, takenLo, takenHi, textBox, groupOf, groupId, r);
                }

                if (row)
                {
                    // With «row» all go in the first row that the dimensions already there leave free, in the order asked.
                    int common = 0;
                    while (common < rows.Length - 1 && members.Any(i => Blocked(dims[i], rows[common], inside[i], false, width[i], height[i],
                                                                               horizontal, lowSide, drawn, outside, pitch, touch, clearance,
                                                                               onThisSide, oldTexts, null, null, null, null, null, null, 0, -1)))
                        common++;
                    foreach (int i in members) Put(i, common, false);
                    continue;
                }
                // CHAINS (note 100): the dimensions with the text inside that follow one another end to end
                // go in one row together, the first where all of them fit. The owner keeps 19 / 134 / 9,5 / 11
                // of 26P000008 view 2 on one line, one row out, while the 1,5 with its text outside takes
                // the row nearer the part; one at a time the 134 alone went out. The texts outside come
                // first, then the chains from the smaller to the larger (by their largest).
                int[] chainOf = style.Pack ? ChainsOf(members, dims, inside, touch) : Enumerable.Range(0, members.Length).ToArray();
                var units = new List<int[]>();
                foreach (int label in chainOf.Distinct())
                {
                    var unitMembers = new List<int>();
                    for (int k = 0; k < members.Length; k++) if (chainOf[k] == label) unitMembers.Add(members[k]);
                    units.Add(unitMembers.ToArray());
                }
                var largest = new double[units.Count];
                var outsideFirst = new int[units.Count];
                for (int k = 0; k < units.Count; k++)
                {
                    foreach (int i in units[k]) largest[k] = Math.Max(largest[k], dims[i][6]);
                    outsideFirst[k] = inside[units[k][0]] ? 1 : 0;
                }
                int next = 0;
                foreach (int u in Enumerable.Range(0, units.Count).OrderBy(k => outsideFirst[k]).ThenBy(k => largest[k]))
                {
                    int[] unit = units[u];
                    int r = style.Pack ? 0 : next;
                    bool lowEnd = false;
                    for (; r < rows.Length - 1; r++)
                    {
                        int i0 = unit[0];
                        if (unit.Length > 1 || inside[i0])
                        {
                            if (unit.All(i => Free(i, r, false))) break;
                            continue;
                        }
                        // A text outside: first the end that leaves the others of its side their place
                        // (the 5 of 26P000008 view 2 beyond its lower end, so the 11 next to it keeps the first row).
                        bool high = Free(i0, r, false), low = style.TextBothEnds && Free(i0, r, true);
                        bool highInTheWay = InTheWay(dims[i0], false, width[i0], outside, touch, members, i0, placed, dims);
                        bool lowInTheWay = InTheWay(dims[i0], true, width[i0], outside, touch, members, i0, placed, dims);
                        if (high && !highInTheWay) break;
                        if (low && !lowInTheWay) { lowEnd = true; break; }
                        if (high) break;
                        if (low) { lowEnd = true; break; }
                    }
                    foreach (int i in unit) Put(i, r, lowEnd);
                    next = r + 1;
                }
            }
            return result;
        }

        /// <summary>The rectangle {x0, y0, x1, y1} of a text centred at (along, across); a vertical dimension writes it turned.</summary>
        private static double[] TextRectangle(bool horizontal, double along, double across, double width, double height)
        {
            double halfAlong = width / 2, halfAcross = height / 2;
            return horizontal ? new[] { along - halfAlong, across - halfAcross, along + halfAlong, across + halfAcross }
                              : new[] { across - halfAcross, along - halfAlong, across + halfAcross, along + halfAlong };
        }

        /// <summary>
        /// The chains among <paramref name="members"/>, one number per member: the dimensions with the
        /// text inside that follow one another end to end (the high line of one is the low line of
        /// the next, within <paramref name="touch"/>) share it; every other has its own. Pure.
        /// </summary>
        private static int[] ChainsOf(int[] members, double[][] dims, bool[] inside, double touch)
        {
            var label = new int[members.Length];
            for (int k = 0; k < label.Length; k++) label[k] = k;
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int a = 0; a < members.Length; a++)
                    for (int b = 0; b < members.Length; b++)
                    {
                        if (a == b || label[a] == label[b] || !inside[members[a]] || !inside[members[b]]) continue;
                        double highA = Math.Max(dims[members[a]][1], dims[members[a]][2]), lowB = Math.Min(dims[members[b]][1], dims[members[b]][2]);
                        if (Math.Abs(highA - lowB) > touch) continue;
                        int keep = Math.Min(label[a], label[b]), drop = Math.Max(label[a], label[b]);
                        for (int k = 0; k < label.Length; k++) if (label[k] == drop) label[k] = keep;
                        changed = true;
                    }
            }
            return label;
        }

        /// <summary>
        /// Whether the text of a dimension set beyond one end (<paramref name="lowEnd"/>, or the
        /// higher one) would lie over the extent of another of its side not placed yet. Pure.
        /// </summary>
        private static bool InTheWay(double[] d, bool lowEnd, double width, double outside, double touch,
                                     int[] members, int self, bool[] placed, double[][] dims)
        {
            double low = Math.Min(d[1], d[2]), high = Math.Max(d[1], d[2]), half = width / 2;
            double lo = lowEnd ? low - outside - half : high, hi = lowEnd ? low : high + outside + half;
            foreach (int j in members)
            {
                if (j == self || placed[j]) continue;
                double a = Math.Min(dims[j][1], dims[j][2]), b = Math.Max(dims[j][1], dims[j][2]);
                if (lo < b - touch && a + touch < hi) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a dimension cannot go in the row at <paramref name="distance"/>: its extent
        /// overlaps one already in that row (old: <paramref name="onThisSide"/>; new: the placed
        /// members of its group <paramref name="groupId"/> in the row <paramref name="rowIndex"/>), or its
        /// text comes nearer than <paramref name="clearance"/> to a text, old or new. The text
        /// between the lines, beyond the higher end, or with <paramref name="lowEnd"/> beyond the
        /// lower end. Pure.
        /// </summary>
        private static bool Blocked(double[] d, double distance, bool inside, bool lowEnd, double width, double height,
                                    bool horizontal, bool lowSide, double[] drawn, double outside, double pitch, double touch,
                                    double clearance, List<double[]> onThisSide, List<double[]> oldTexts,
                                    int[] rowOf, bool[] placed, double[] takenLo, double[] takenHi, double[][] textBox,
                                    int[] groupOf, int groupId, int rowIndex)
        {
            double low = Math.Min(d[1], d[2]), high = Math.Max(d[1], d[2]), half = width / 2;
            double lo = inside ? low : lowEnd ? low - outside - half : low;
            double hi = inside ? high : lowEnd ? high : high + outside + half;
            double along = inside ? (low + high) / 2 : lowEnd ? low - outside : high + outside;
            foreach (double[] t in onThisSide)
                if (Math.Abs(t[0] - distance) < pitch / 2 && lo < t[2] - touch && t[1] + touch < hi) return true;
            double across = horizontal ? (lowSide ? drawn[1] - distance : drawn[3] + distance)
                                       : (lowSide ? drawn[0] - distance : drawn[2] + distance);
            double[] box = TextRectangle(horizontal, along, across, width, height);
            foreach (double[] t in oldTexts)
                if (TooNear(box, t, clearance)) return true;
            if (placed == null) return false;
            for (int j = 0; j < placed.Length; j++)
            {
                if (!placed[j]) continue;
                if (groupOf[j] == groupId && rowOf[j] == rowIndex && lo < takenHi[j] - touch && takenLo[j] + touch < hi) return true;
                if (textBox[j] != null && TooNear(box, textBox[j], clearance)) return true;
            }
            return false;
        }

        /// <summary>
        /// Where the label of a detail goes once its dimensions are placed: the text of the lowest
        /// dimension under the view less the gap of the style, when that is lower than the label
        /// is now; null when the label can stay (it is already lower, or no dimension is under
        /// the view: <paramref name="lowestText"/> NaN). Metres of sheet. Pure.
        /// </summary>
        internal static double? DetailLabelY(double labelY, double lowestText, double gapMm)
        {
            if (double.IsNaN(lowestText) || double.IsNaN(labelY)) return null;
            double wanted = lowestText - gapMm / 1000.0;
            return wanted < labelY - 0.00005 ? (double?)wanted : null;
        }
    }
}
