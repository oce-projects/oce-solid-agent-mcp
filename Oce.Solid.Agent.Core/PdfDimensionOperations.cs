using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LE QUOTE DELLA TAVOLA ARRIVANO NEL PDF? (voce Z-C9, la meta' «oggetti»).
    ///
    /// I contatori contano NEL DOCUMENTO: una quota su un livello che non si
    /// stampa, o in una vista nascosta, conta e in officina non arriva. Il PDF e'
    /// l'unico punto in cui si misura cio' che l'officina riceve davvero.
    ///
    /// La misura: i NUMERI che le quote stampano (il testo di ogni quota, da
    /// `IDisplayData`, spezzato nei suoi numeri: «10 H7 +0,015 0» da' 10, 0,015,
    /// 0) contati per valore; nel testo del PDF (`PdfText`) ogni valore deve
    /// comparire ALMENO tante volte quante lo stampano le quote. Il PDF ha anche
    /// altri numeri (il cartiglio, le tolleranze generali): per questo «almeno»
    /// e non «uguale» — un numero in piu' non dice niente, uno in meno e' una
    /// quota che non e' arrivata.
    /// </summary>
    public partial class SwOperations
    {
        private static readonly Regex NumberInText = new Regex(@"\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        /// <summary>
        /// I numeri stampati dalle quote della tavola, per valore -> quante volte.
        /// `withoutText`: le quote di cui il testo non si legge — NON controllate,
        /// e si dice (un controllo che salta in silenzio sembra un controllo passato).
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
                            // ZERO-E-ZERO: senza IDisplayData la quota non ha testi da
                            // cercare nel PDF — zero testi e' il limite del ciclo, non una
                            // misura. Quella quota resta NON controllata, e si conta.
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

        /// <summary>Un numero scritto con la virgola o col punto, e senza zeri inutili.</summary>
        private static string Normalize(string s)
        {
            s = s.Replace(',', '.');
            double d;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)
                ? d.ToString("0.######", CultureInfo.InvariantCulture) : s;
        }

        /// <summary>
        /// Il confronto: per ogni numero delle quote, quante volte nel PDF.
        /// Torna i numeri che nel PDF compaiono meno volte che sulla tavola.
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
                if (foundItems < kv.Value) areMissing.Add(kv.Key + " (sulla tavola " + kv.Value + ", nel PDF " + foundItems + ")");
            }
            return areMissing;
        }
    }
}
