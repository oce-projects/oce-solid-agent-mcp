using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ProvaSaldatura
{
    /// <summary>
    /// IL NUMERO CHE SI STAMPA E' QUELLO DEL MODELLO? (voce Z-C2)
    ///
    /// «La quota viene dal modello, non puo' mentire» vale finche' nessuno
    /// scrive sopra il numero. Lo stato della tavola conta dalla libreria
    /// 0.118.0 le quote il cui numero stampato non e' quello del modello
    /// (`dimensions_with_number_not_from_model`), con le due letture misurate il
    /// 25/9/2026: `GetOverride` e `ShowDimensionValue`.
    ///
    /// Un caso dichiarato non puo' farlo: nessuna operazione del catalogo
    /// sovrascrive una quota, e non deve. Qui si falsificano tre quote a mano,
    /// una per strada — numero sovrascritto, testo al posto del valore, valore
    /// spento — e lo stato deve dirne tre. Prima, sulla tavola intatta, zero.
    /// </summary>
    internal static partial class Program
    {
        private static void ProvaINumeriDelleQuote()
        {
            Console.WriteLine();
            Console.WriteLine("=== numeri: il numero stampato e' quello del modello? (Z-C2) ===");
            IModelDoc2 pezzo = null, tavola = null;
            string file = Path.Combine(Attese.Lavoro, "numeri_banco.sldprt");
            try
            {
                pezzo = NuovaParte();
                if (Estrusione(pezzo, 0, 0, 0.120, 0.080, 0.010) == null)
                { ErroreDiBanco("numeri", "la piastra non e' venuta"); return; }
                try { if (File.Exists(file)) File.Delete(file); } catch { }
                int e = 0, w = 0;
                pezzo.Extension.SaveAs(file, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e, ref w);
                if (!File.Exists(file)) { ErroreDiBanco("numeri", "il pezzo non si salva (e=" + e + ")"); return; }

                string errore;
                Esegui(pezzo, "create_drawing", new Dictionary<string, object> { ["paper_size"] = "A3" },
                       out errore, out tavola);
                if (tavola == null) { ErroreDiBanco("numeri", "create_drawing: " + errore); return; }
                if (Esegui(tavola, "standard_views", new Dictionary<string, object> { ["model"] = file }, out errore) == null)
                { ErroreDiBanco("numeri", "standard_views: " + errore); return; }
                foreach (var vista in new[] { "V1", "V2" })
                    if (Esegui(tavola, "drawing_dimension", new Dictionary<string, object> { ["view"] = vista }, out errore) == null)
                    { ErroreDiBanco("numeri", "drawing_dimension " + vista + ": " + errore); return; }

                var prima = LeggiStato(tavola);
                object zero = prima.ContainsKey("dimensions_with_number_not_from_model_total")
                    ? prima["dimensions_with_number_not_from_model_total"] : "(manca)";
                Segna("numeri: sulla tavola appena quotata nessuna quota stampa un numero non del modello",
                      Convert.ToString(zero) == "0" && !prima.ContainsKey("warning_numbers"),
                      "dimensions_with_number_not_from_model_total " + Convert.ToString(zero));

                // TRE QUOTE, UNA PER STRADA. Si prendono le prime tre del foglio,
                // vista dopo vista: quali siano non conta, conta che siano tre.
                var quote = new List<IDisplayDimension>();
                var v = (((IDrawingDoc)tavola).GetFirstView() as IView)?.GetNextView() as IView;
                while (v != null && quote.Count < 3)
                {
                    var q = v.GetFirstDisplayDimension5() as IDisplayDimension;
                    while (q != null && quote.Count < 3) { quote.Add(q); q = q.GetNext5() as IDisplayDimension; }
                    v = v.GetNextView() as IView;
                }
                if (quote.Count < 3) { ErroreDiBanco("numeri", "sulla tavola ci sono " + quote.Count + " quote, ne servono tre"); return; }

                // IL NUMERO SOVRASCRITTO COMBACIA COL MODELLO, apposta: e' il caso
                // che la voce temeva di piu' — un confronto di valori non lo vede.
                double suo = quote[0].GetDimension2(0).GetSystemValue2("");
                quote[0].SetOverride(true, suo);
                quote[1].SetText((int)swDimensionTextParts_e.swDimensionTextAll, "99");
                quote[2].ShowDimensionValue = false;
                tavola.ForceRebuild3(false);

                var dopo = LeggiStato(tavola);
                object tre = dopo.ContainsKey("dimensions_with_number_not_from_model_total")
                    ? dopo["dimensions_with_number_not_from_model_total"] : "(manca)";
                var elenco = dopo.ContainsKey("dimensions_with_number_not_from_model")
                    ? dopo["dimensions_with_number_not_from_model"] as IEnumerable : null;
                int sovrascritte = 0, conTesto = 0, spente = 0;
                if (elenco != null)
                    foreach (var o in elenco)
                    {
                        var voce = o as Dictionary<string, object>;
                        string come = voce != null && voce.ContainsKey("how") ? Convert.ToString(voce["how"]) : "";
                        if (come.StartsWith("numero sovrascritto", StringComparison.Ordinal)) sovrascritte++;
                        else if (come.Contains("«99»")) conTesto++;
                        else if (come.StartsWith("valore spento", StringComparison.Ordinal)) spente++;
                    }
                Segna("numeri: falsificate tre quote (sovrascritta col suo stesso valore, testo, spenta), lo stato ne dice tre e avvisa",
                      Convert.ToString(tre) == "3" && sovrascritte == 1 && conTesto == 1 && spente == 1 &&
                      dopo.ContainsKey("warning_numbers"),
                      "totale " + Convert.ToString(tre) + ": sovrascritte " + sovrascritte + ", col testo " + conTesto +
                      ", spente " + spente + ", avviso " + (dopo.ContainsKey("warning_numbers") ? "si'" : "no"));
            }
            catch (Exception ex) { ErroreDiBanco("numeri", Perche(ex)); }
            finally
            {
                try { if (tavola != null) _sw.CloseDoc(tavola.GetTitle()); } catch { }
                Chiudi(pezzo);
                try { if (File.Exists(file)) File.Delete(file); } catch { }
            }
        }
    }
}
