using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LE LETTERE DELLE SEZIONI E DEI DETTAGLI (voce Z-C13), e la vista per uid.
    ///
    /// Due sezioni «A-A» sulla stessa tavola sono due tagli diversi che sembrano
    /// lo stesso taglio; una sezione «B-B» e un dettaglio «B» uguale. SOLIDWORKS
    /// non lo impedisce. La lettera EFFETTIVA si legge — `IDrSection.GetLabel()`
    /// dalla vista di sezione (`IView.GetSection()`), `IDetailCircle.GetLabel()`
    /// dal dettaglio (`IView.GetDetail()`) — non si crede a quella chiesta.
    ///
    /// E «V1»: `section_view` e `detail_view` cercavano la vista
    /// solo per NOME, come `drawing_cut_list` fino alla 0.124.0 (che il
    /// banco ha preso rossa alla prima chiamata con «V1»).
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>«V2» come lo scrive lo stato diventa il nome vero; un nome vero resta.</summary>
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
        /// Le lettere gia' usate in tavola da sezioni e dettagli, lette dalle
        /// viste (la lettera effettiva). Chiave = lettera maiuscola, valore = cosa.
        /// </summary>
        private static Dictionary<string, string> SectionAndDetailLetters(IDrawingDoc drawing)
        {
            var outside = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // TUTTI I FOGLI, non solo quello attivo (revisione del 26/9/2026): una
            // «A-A» sul foglio 1 e un'altra sul foglio 2 sono due tagli con la stessa
            // lettera nello stesso documento. GetViews: per foglio, [foglio, viste...].
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
                        { letter = (v.GetSection() as IDrSection)?.GetLabel(); what = "la sezione"; }
                        else if (typ == (int)swDrawingViewTypes_e.swDrawingDetailView)
                        { letter = (v.GetDetail() as IDetailCircle)?.GetLabel(); what = "il dettaglio"; }
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

        /// <summary>La lettera effettiva della vista di sezione o di dettaglio, o null.</summary>
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
        /// Rifiuto se la lettera chiesta e' gia' di una sezione o di un dettaglio:
        /// il motivo, o null. Dice le lettere gia' usate.
        /// </summary>
        private static string LetterAlreadyUsed(IDrawingDoc drawing, string letter)
        {
            var already = SectionAndDetailLetters(drawing);
            string k = (letter ?? "").Trim().ToUpperInvariant();
            string chi;
            if (!already.TryGetValue(k, out chi)) return null;
            return "In tavola la lettera «" + k + "» e' gia' di " + chi + ": due tagli o due dettagli con la stessa " +
                   "lettera sembrano lo stesso (UNI EN ISO 128). Usa un'altra lettera — ci sono gia': " +
                   string.Join(", ", already.Keys) + ".";
        }
    }
}
