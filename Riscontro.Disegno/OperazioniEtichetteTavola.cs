using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
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
    /// E «V1»: `sezione_in_tavola` e `dettaglio_in_tavola` cercavano la vista
    /// solo per NOME, come `distinta_taglio_in_tavola` fino alla 0.124.0 (che il
    /// banco ha preso rossa alla prima chiamata con «V1»).
    /// </summary>
    public partial class OperazioniSw
    {
        /// <summary>«V2» come lo scrive lo stato diventa il nome vero; un nome vero resta.</summary>
        private string VistaChiestaPerNome(IDrawingDoc disegno, string chiesta)
        {
            if (string.IsNullOrWhiteSpace(chiesta)) return chiesta;
            var nomi = NomiDelleViste(disegno);
            if (NomeDiVistaConosciuto(nomi, chiesta)) return chiesta;
            var m = Regex.Match(chiesta.Trim(), @"^[Vv](\d+)$");
            if (!m.Success) return chiesta;
            int k = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return k >= 1 && k <= nomi.Count ? nomi[k - 1] : chiesta;
        }

        /// <summary>
        /// Le lettere gia' usate in tavola da sezioni e dettagli, lette dalle
        /// viste (la lettera effettiva). Chiave = lettera maiuscola, valore = cosa.
        /// </summary>
        private static Dictionary<string, string> LettereDiSezioniEDettagli(IDrawingDoc disegno)
        {
            var fuori = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // TUTTI I FOGLI, non solo quello attivo (revisione del 26/9/2026): una
            // «A-A» sul foglio 1 e un'altra sul foglio 2 sono due tagli con la stessa
            // lettera nello stesso documento. GetViews: per foglio, [foglio, viste...].
            var tutte = new List<IView>();
            try
            {
                foreach (var perFoglio in (disegno.GetViews() as object[]) ?? new object[0])
                    foreach (var ov in (perFoglio as object[]) ?? new object[0])
                        if (ov is IView iv) tutte.Add(iv);
            }
            catch { }
            if (tutte.Count == 0)
            {
                try
                {
                    var w = disegno.GetFirstView() as IView;
                    while (w != null) { tutte.Add(w); w = w.GetNextView() as IView; }
                }
                catch { }
            }
            try
            {
                foreach (var v in tutte)
                {
                    int tipo = -1;
                    try { tipo = v.Type; } catch { }
                    string lettera = null, cosa = null;
                    try
                    {
                        if (tipo == (int)swDrawingViewTypes_e.swDrawingSectionView)
                        { lettera = (v.GetSection() as IDrSection)?.GetLabel(); cosa = "la sezione"; }
                        else if (tipo == (int)swDrawingViewTypes_e.swDrawingDetailView)
                        { lettera = (v.GetDetail() as IDetailCircle)?.GetLabel(); cosa = "il dettaglio"; }
                    }
                    catch { }
                    if (!string.IsNullOrWhiteSpace(lettera))
                    {
                        string k = lettera.Trim().ToUpperInvariant();
                        if (!fuori.ContainsKey(k)) fuori[k] = cosa + " «" + (v.GetName2() ?? "") + "»";
                    }
                }
            }
            catch { }
            return fuori;
        }

        /// <summary>La lettera effettiva della vista di sezione o di dettaglio, o null.</summary>
        private static string LetteraDellaVista(IView v)
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
        private static string LetteraGiaUsata(IDrawingDoc disegno, string lettera)
        {
            var gia = LettereDiSezioniEDettagli(disegno);
            string k = (lettera ?? "").Trim().ToUpperInvariant();
            string chi;
            if (!gia.TryGetValue(k, out chi)) return null;
            return "In tavola la lettera «" + k + "» e' gia' di " + chi + ": due tagli o due dettagli con la stessa " +
                   "lettera sembrano lo stesso (UNI EN ISO 128). Usa un'altra lettera — ci sono gia': " +
                   string.Join(", ", gia.Keys) + ".";
        }
    }
}
