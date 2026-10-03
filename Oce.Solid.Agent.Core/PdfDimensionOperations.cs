using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;

namespace Riscontro.Disegno
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
    /// 0) contati per valore; nel testo del PDF (`TestoDelPdf`) ogni valore deve
    /// comparire ALMENO tante volte quante lo stampano le quote. Il PDF ha anche
    /// altri numeri (il cartiglio, le tolleranze generali): per questo «almeno»
    /// e non «uguale» — un numero in piu' non dice niente, uno in meno e' una
    /// quota che non e' arrivata.
    /// </summary>
    public partial class OperazioniSw
    {
        private static readonly Regex NumeroInTesto = new Regex(@"\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        /// <summary>
        /// I numeri stampati dalle quote della tavola, per valore -> quante volte.
        /// `senzaTesto`: le quote di cui il testo non si legge — NON controllate,
        /// e si dice (un controllo che salta in silenzio sembra un controllo passato).
        /// </summary>
        private static Dictionary<string, int> NumeriDelleQuote(IDrawingDoc disegno, out int quote, out int senzaTesto)
        {
            quote = 0;
            senzaTesto = 0;
            var fuori = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                var v = disegno.GetFirstView() as IView;
                while (v != null)
                {
                    var dd = v.GetFirstDisplayDimension5() as IDisplayDimension;
                    while (dd != null)
                    {
                        quote++;
                        try
                        {
                            var dati = dd.GetDisplayData() as IDisplayData;
                            // ZERO-E-ZERO: senza IDisplayData la quota non ha testi da
                            // cercare nel PDF — zero testi e' il limite del ciclo, non una
                            // misura. Quella quota resta NON controllata, e si conta.
                            int n = dati == null ? 0 : dati.GetTextCount();
                            if (n <= 0) senzaTesto++;
                            for (int i = 0; i < n; i++)
                                foreach (Match m in NumeroInTesto.Matches(dati.GetTextAtIndex(i) ?? ""))
                                {
                                    string k = Normalizza(m.Value);
                                    fuori[k] = fuori.TryGetValue(k, out int c) ? c + 1 : 1;
                                }
                        }
                        catch { senzaTesto++; }
                        dd = dd.GetNext5() as IDisplayDimension;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return fuori;
        }

        /// <summary>Un numero scritto con la virgola o col punto, e senza zeri inutili.</summary>
        private static string Normalizza(string s)
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
        private static List<string> QuoteCheMancanoNelPdf(Dictionary<string, int> attesi, string testoPdf)
        {
            var nelPdf = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Match m in NumeroInTesto.Matches(testoPdf ?? ""))
            {
                string k = Normalizza(m.Value);
                nelPdf[k] = nelPdf.TryGetValue(k, out int c) ? c + 1 : 1;
            }
            var mancano = new List<string>();
            foreach (var kv in attesi)
            {
                int trovati = nelPdf.TryGetValue(kv.Key, out int t) ? t : 0;
                if (trovati < kv.Value) mancano.Add(kv.Key + " (sulla tavola " + kv.Value + ", nel PDF " + trovati + ")");
            }
            return mancano;
        }
    }
}
