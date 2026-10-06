using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE LETTERS OF THE SECTIONS AND DETAILS (entry Z-C13), and the view by uid.
    ///
    /// Two «A-A» sections on the same drawing are two different cuts that look like
    /// the same cut; a «B-B» section and an equal «B» detail. SOLIDWORKS
    /// does not prevent it. The ACTUAL letter is read — `IDrSection.GetLabel()`
    /// from the section view (`IView.GetSection()`), `IDetailCircle.GetLabel()`
    /// from the detail (`IView.GetDetail()`) — what was asked for is not believed.
    ///
    /// And «V1»: `section_view` and `detail_view` looked for the view
    /// by NAME only, like `drawing_cut_list` until 0.124.0 (which the
    /// bench got red on the first call with «V1»).
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>«V2» as the state writes it becomes the real name; a real name stays.</summary>
        private string ViewRequestedByName(IDrawingDoc drawing, string requested)
        {
            if (string.IsNullOrWhiteSpace(requested)) return requested;
            var names = ViewNames(drawing);
            if (KnownViewName(names, requested)) return requested;
            var m = Regex.Match(requested.Trim(), @"^[Vv](\d+)$");
            if (!m.Success) return requested;
            int k = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return k >= 1 && k <= names.Count ? names[k - 1] : requested;
        }

        /// <summary>
        /// The letters already used on the drawing by sections and details, read from the
        /// views (the actual letter). Key = capital letter, value = what.
        /// </summary>
        private static Dictionary<string, string> SectionAndDetailLetters(IDrawingDoc drawing)
        {
            var outside = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // ALL THE SHEETS, not only the active one (review of 26/9/2026): an
            // «A-A» on sheet 1 and another on sheet 2 are two cuts with the same
            // letter in the same document. GetViews: per sheet, [sheet, views...].
            var all = new List<IView>();
            try
            {
                foreach (var perSheet in (drawing.GetViews() as object[]) ?? new object[0])
                    foreach (var ov in (perSheet as object[]) ?? new object[0])
                        if (ov is IView iv) all.Add(iv);
            }
            catch { }
            if (all.Count == 0)
            {
                try
                {
                    var w = drawing.GetFirstView() as IView;
                    while (w != null) { all.Add(w); w = w.GetNextView() as IView; }
                }
                catch { }
            }
            try
            {
                foreach (var v in all)
                {
                    int typ = -1;
                    try { typ = v.Type; } catch { }
                    string letter = null, what = null;
                    try
                    {
                        if (typ == (int)swDrawingViewTypes_e.swDrawingSectionView)
                        { letter = (v.GetSection() as IDrSection)?.GetLabel(); what = "the section"; }
                        else if (typ == (int)swDrawingViewTypes_e.swDrawingDetailView)
                        { letter = (v.GetDetail() as IDetailCircle)?.GetLabel(); what = "the detail"; }
                    }
                    catch { }
                    if (!string.IsNullOrWhiteSpace(letter))
                    {
                        string k = letter.Trim().ToUpperInvariant();
                        if (!outside.ContainsKey(k)) outside[k] = what + " «" + (v.GetName2() ?? "") + "»";
                    }
                }
            }
            catch { }
            return outside;
        }

        /// <summary>The actual letter of the section or detail view, or null.</summary>
        private static string ViewLetter(IView v)
        {
            try
            {
                if (v.Type == (int)swDrawingViewTypes_e.swDrawingSectionView) return (v.GetSection() as IDrSection)?.GetLabel();
                if (v.Type == (int)swDrawingViewTypes_e.swDrawingDetailView) return (v.GetDetail() as IDetailCircle)?.GetLabel();
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Refusal if the letter asked for already belongs to a section or a detail:
        /// the reason, or null. It says the letters already used.
        /// </summary>
        private static string LetterAlreadyUsed(IDrawingDoc drawing, string letter)
        {
            var already = SectionAndDetailLetters(drawing);
            string k = (letter ?? "").Trim().ToUpperInvariant();
            string chi;
            if (!already.TryGetValue(k, out chi)) return null;
            return "On the drawing the letter «" + k + "» already belongs to " + chi + ": two sections or two details with the same " +
                   "letter look like the same one (UNI EN ISO 128). Use a different letter — already used: " +
                   string.Join(", ", already.Keys) + ".";
        }
    }
}
