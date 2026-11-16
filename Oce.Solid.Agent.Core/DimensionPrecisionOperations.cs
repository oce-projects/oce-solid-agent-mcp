using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE NUMBER THE SHEET PRINTS IS THE NUMBER OF THE MODEL (report 8cd843, round 21,
    /// point 4, P1, 28/10/2026). On the real drawings of 26P000009 and 26P000011 an edge of
    /// 0.75 mm was printed «0,8», 1.835 «1,8», 3.535 «3,5» and 10.018 «10»: the template of
    /// the drawing gives one decimal, and the answer said the right value in «mm» beside
    /// the wrong one in «text_shown» without a word. A wrong number on a drawing is a
    /// wrong part.
    ///
    /// Now every dimension «drawing_dimension» places is read back, and when the sheet
    /// prints a number other than the value, its decimals are raised to the ones the value
    /// needs (at most 3: the value is read to the thousandth). «decimals» fixes them for
    /// every dimension of the call instead; a dimension that still prints another number
    /// is said in «rounded_on_the_sheet».
    ///
    /// IDisplayDimension~SetPrecision3.html: «Number of digits displayed after the decimal
    /// point», 0 to 8, or swDimensionPrecisionSettings_e (swDoNotChangePrecisionSetting -1
    /// for the dual and tolerance values, left as they are); 0 is success, below 0 nothing
    /// was set, above 0 a value was invalid. The text is read again after the call (the
    /// guide redraws with GraphicsRedraw2 to see the change on screen: obsolete here, and
    /// the display data is what is read, not the screen).
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>The decimals asked by «drawing_dimension» for this call; -1 is «as each value needs».</summary>
        private int _dimensionDecimals = -1;

        /// <summary>The most decimals the bridge gives a value read to the thousandth.</summary>
        internal const int MostDecimalsNeeded = 3;

        /// <summary>
        /// The first number a dimension prints, «0,8» or «10.018», as a number; null when
        /// its text holds none. Pure, so the contract suite reads it.
        /// </summary>
        internal static double? PrintedNumber(string textShown)
        {
            if (string.IsNullOrWhiteSpace(textShown)) return null;
            // «(35)», a reference dimension (round 22, point 6), prints the number 35.
            string first = textShown.Trim().Split(' ')[0].TrimStart('(').TrimEnd(')').Replace(',', '.');
            double value;
            if (!double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return null;
            return value;
        }

        /// <summary>
        /// Whether the sheet prints a number other than the value: «0,8» for 0.75. False
        /// when the text holds no number (nothing to compare). Pure.
        /// </summary>
        internal static bool PrintedDiffers(double mm, string textShown)
        {
            double? printed = PrintedNumber(textShown);
            if (printed == null) return false;
            return Math.Abs((double)printed - mm) > 1e-6;
        }

        /// <summary>
        /// The fewest decimals that print the value as it is (0.75 needs 2, 10 needs 0),
        /// at most <paramref name="most"/>. Pure.
        /// </summary>
        internal static int DecimalsNeeded(double mm, int most)
        {
            for (int d = 0; d < most; d++)
                if (Math.Abs(Math.Round(mm, d, MidpointRounding.AwayFromZero) - mm) < 1e-6) return d;
            return most;
        }

        /// <summary>
        /// What a dimension just placed prints, after its decimals are made to print its
        /// value: raised to the ones it needs, or set to «decimals» when the call gives
        /// them. The row of the dimension says «printed_before» when the text changed,
        /// «decimals» when they were set, and «precision_note» when SOLIDWORKS refused.
        /// A row with no value in mm is read as it is.
        /// </summary>
        private string ExactTextShown(IDisplayDimension d, Dictionary<string, object> described)
        {
            string shown = TextShown(d);
            object value;
            if (d == null || described == null || !described.TryGetValue("mm", out value) || !(value is double))
                return shown;
            double mm = (double)value;
            bool fixedByCall = _dimensionDecimals >= 0;
            if (!fixedByCall && !PrintedDiffers(mm, shown)) return shown;
            int decimals = fixedByCall ? _dimensionDecimals : DecimalsNeeded(mm, MostDecimalsNeeded);
            int keep = (int)swDimensionPrecisionSettings_e.swDoNotChangePrecisionSetting;
            int result = -1;
            try { result = d.SetPrecision3(decimals, keep, keep, keep); } catch { }
            if (result != 0)
            {
                described["precision_note"] = "SOLIDWORKS did not set " + decimals + " decimals (SetPrecision3 answered " +
                                              result + "): the sheet prints «" + shown + "».";
                return shown;
            }
            string after = TextShown(d);
            described["decimals"] = decimals;
            if (after != shown) described["printed_before"] = shown;
            return after;
        }

        /// <summary>
        /// The dimensions whose sheet still prints another number than their value, as
        /// «0.75 mm printed 0,8». Pure.
        /// </summary>
        internal static List<string> RoundedOnTheSheet(IEnumerable<Dictionary<string, object>> dimensions)
        {
            var rounded = new List<string>();
            foreach (var row in dimensions)
            {
                object value, text;
                if (!row.TryGetValue("mm", out value) || !(value is double)) continue;
                if (!row.TryGetValue("text_shown", out text) || !(text is string)) continue;
                if (PrintedDiffers((double)value, (string)text))
                    rounded.Add(((double)value).ToString("0.###", CultureInfo.InvariantCulture) + " mm printed " + (string)text);
            }
            return rounded;
        }

        /// <summary>
        /// «decimals» of «drawing_dimension»: -1 when it is left out, the whole number from
        /// 0 to 8 otherwise; null with the reason when it cannot be used. Pure.
        /// </summary>
        internal static int? DecimalsAsked(Dictionary<string, object> a, out string why)
        {
            why = null;
            object raw;
            if (a == null || !a.TryGetValue("decimals", out raw) || raw == null) return -1;
            double value;
            try { value = Convert.ToDouble(raw, CultureInfo.InvariantCulture); }
            catch { why = "«" + raw + "» is not a number"; return null; }
            if (value != Math.Floor(value) || value < 0 || value > 8)
            {
                why = value.ToString(CultureInfo.InvariantCulture) + " is not a whole number from 0 to 8";
                return null;
            }
            return (int)value;
        }
    }
}
