using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// DO THE DIMENSIONS OF THE DRAWING REACH THE PDF? (entry Z-C9, the «objects» half).
    ///
    /// The counters count IN THE DOCUMENT: a dimension on a layer that is not
    /// printed, or in a hidden view, counts and does not reach the workshop. The PDF is
    /// the only point where what the workshop really receives is measured.
    ///
    /// The measurement: the NUMBERS the dimensions print (the text of every dimension, from
    /// `IDisplayData`, split into its numbers: «10 H7 +0,015 0» gives 10, 0,015,
    /// 0) counted by value; in the text of the PDF (`PdfText`) each value must
    /// appear AT LEAST as many times as the dimensions print it. The PDF has also
    /// other numbers (the title block, the general tolerances): that is why «at least»
    /// and not «equal» — one number more says nothing, one less is a
    /// dimension that did not arrive.
    /// </summary>
    public partial class SwOperations
    {
        private static readonly Regex NumberInText = new Regex(@"\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        /// <summary>
        /// The numbers a dimension prints, as its texts give them («16,3» where the
        /// model holds 16.258), in order and joined by a blank; null when the texts
        /// hold no number (report 8cd843 N35, round 9). The same reading as
        /// <see cref="DimensionNumbers"/>, which looks for them in the PDF. Pure,
        /// so the contract suite reads it.
        /// </summary>
        internal static string TextShownOf(IList<string> texts)
        {
            if (texts == null) return null;
            var numbers = new List<string>();
            foreach (string text in texts)
                foreach (Match m in NumberInText.Matches(text ?? ""))
                    numbers.Add(m.Value);
            return numbers.Count == 0 ? null : string.Join(" ", numbers);
        }

        /// <summary>
        /// What the sheet prints for a dimension just made (report 8cd843 N35): the
        /// answer gave 16.258 and the sheet printed «16,3». Null when its display
        /// data cannot be read: then nothing is said, not a guess.
        /// </summary>
        private static string TextShown(IDisplayDimension d)
        {
            try
            {
                var data = d == null ? null : d.GetDisplayData() as IDisplayData;
                int n = data == null ? 0 : data.GetTextCount();
                var texts = new List<string>();
                for (int i = 0; i < n; i++) texts.Add(data.GetTextAtIndex(i));
                return TextShownOf(texts);
            }
            catch { return null; }
        }

        /// <summary>The <c>text_shown</c> of each dimension, in a row as <c>dimensions_mm</c>.</summary>
        private static object[] TextsShownInARow(List<Dictionary<string, object>> dimensions)
        {
            var row = new List<object>();
            foreach (var q in dimensions)
            {
                object v;
                row.Add(q.TryGetValue("text_shown", out v) ? v : null);
            }
            return row.ToArray();
        }

        /// <summary>
        /// The numbers printed by the dimensions of the drawing, by value -> how many times.
        /// `withoutText`: the dimensions whose text cannot be read — NOT checked,
        /// and it is stated (a check that skips silently looks like a check passed).
        /// </summary>
        private static Dictionary<string, int> DimensionNumbers(IDrawingDoc drawing, out int quote, out int withoutText)
        {
            quote = 0;
            withoutText = 0;
            var outside = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    var dd = v.GetFirstDisplayDimension5() as IDisplayDimension;
                    while (dd != null)
                    {
                        quote++;
                        try
                        {
                            var data = dd.GetDisplayData() as IDisplayData;
                            // ZERO-E-ZERO: without IDisplayData the dimension has no texts to
                            // look for in the PDF — zero texts is the limit of the loop, not a
                            // measurement. That dimension stays NOT checked, and is counted.
                            int n = data == null ? 0 : data.GetTextCount();
                            if (n <= 0) withoutText++;
                            for (int i = 0; i < n; i++)
                                foreach (Match m in NumberInText.Matches(data.GetTextAtIndex(i) ?? ""))
                                {
                                    string k = Normalize(m.Value);
                                    outside[k] = outside.TryGetValue(k, out int c) ? c + 1 : 1;
                                }
                        }
                        catch { withoutText++; }
                        dd = dd.GetNext5() as IDisplayDimension;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return outside;
        }

        /// <summary>A number written with a comma or a point, and without useless zeros.</summary>
        private static string Normalize(string s)
        {
            s = s.Replace(',', '.');
            double d;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)
                ? d.ToString("0.######", CultureInfo.InvariantCulture) : s;
        }

        /// <summary>
        /// The comparison: for every number of the dimensions, how many times in the PDF.
        /// Returns the numbers that appear fewer times in the PDF than on the drawing.
        /// </summary>
        private static List<string> DimensionsMissingInPdf(Dictionary<string, int> expectedValues, string pdfText)
        {
            var inPdf = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Match m in NumberInText.Matches(pdfText ?? ""))
            {
                string k = Normalize(m.Value);
                inPdf[k] = inPdf.TryGetValue(k, out int c) ? c + 1 : 1;
            }
            var areMissing = new List<string>();
            foreach (var kv in expectedValues)
            {
                int foundItems = inPdf.TryGetValue(kv.Key, out int t) ? t : 0;
                if (foundItems < kv.Value) areMissing.Add(kv.Key + " (on the drawing " + kv.Value + ", in the PDF " + foundItems + ")");
            }
            return areMissing;
        }
    }
}
