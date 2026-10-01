using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ProvaSaldatura
{
    /// <summary>
    /// LA QUOTA CHE RESTA SENZA SPIGOLO, VISTA DALLO STATO (voce Z-A2).
    ///
    /// `IAnnotation.IsDangling` e' morto per le quote in tavola fuori processo:
    /// misurato il 24/9/2026 con `Controlli/Orfana`, resta falso su una quota il
    /// cui spigolo e' stato soppresso. Dalla libreria 0.113.0 lo stato della
    /// tavola conta le quote attaccate solo a spigoli che la vista non mostra
    /// piu' (`dimensions_orphan`, `dimensions_orphan_total`).
    ///
    /// Qui i due versi, sullo stesso pezzo della sonda: una piastra con sopra un
    /// blocco, la quota sullo spigolo alto del blocco messa da
    /// `drawing_dimension`, e lo stato riletto — zero orfane. Poi il blocco si
    /// SOPPRIME nel pezzo, la tavola si ricostruisce, e lo stato deve dirne una,
    /// con l'avviso. Un caso dichiarato non puo' farlo: dopo `create_drawing` segue
    /// la tavola, e non sa tornare al pezzo per sopprimere.
    /// </summary>
    internal static partial class Program
    {
        private static void ProvaLeQuoteOrfane()
        {
            Console.WriteLine();
            Console.WriteLine("=== orfane: la quota che resta senza spigolo (Z-A2) ===");
            IModelDoc2 pezzo = null, tavola = null;
            string file = Path.Combine(Attese.Lavoro, "orfane_banco.sldprt");
            bool chiedeva = true, spenta = false;
            try
            {
                pezzo = NuovaParte();
                // Due estrusioni dal piano SUPERIORE, preso per posizione e non
                // per nome: il nome cambia con la lingua dell'installazione.
                if (Estrusione(pezzo, 0, 0, 0.100, 0.060, 0.010) == null)
                { ErroreDiBanco("orfane", "la piastra non e' venuta"); return; }
                var blocco = Estrusione(pezzo, 0.040, 0.020, 0.060, 0.040, 0.020);
                if (blocco == null) { ErroreDiBanco("orfane", "il blocco non e' venuto"); return; }

                try { if (File.Exists(file)) File.Delete(file); } catch { }
                int e = 0, w = 0;
                pezzo.Extension.SaveAs(file, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e, ref w);
                if (!File.Exists(file)) { ErroreDiBanco("orfane", "il pezzo non si salva (e=" + e + ")"); return; }

                string errore;
                Esegui(pezzo, "create_drawing", new Dictionary<string, object> { ["paper_size"] = "A3" },
                       out errore, out tavola);
                if (tavola == null) { ErroreDiBanco("orfane", "create_drawing: " + errore); return; }
                if (Esegui(tavola, "model_view", new Dictionary<string, object>
                    { ["model"] = file, ["view"] = "front", ["x_mm"] = 150, ["y_mm"] = 150 }, out errore) == null)
                { ErroreDiBanco("orfane", "model_view: " + errore); return; }

                // QUALE SPIGOLO: quello alto del blocco — Y 20 alle due estremita',
                // lungo X per 20. Lo si cerca nello stesso elenco che
                // `drawing_dimension` chiede alla vista, e se ne passa l'indice.
                var vista = ((IDrawingDoc)tavola).GetFirstView() as IView;
                vista = vista?.GetNextView() as IView;
                var spigoli = vista?.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
                int indice = -1;
                for (int i = 0; spigoli != null && i < spigoli.Length && indice < 0; i++)
                {
                    var ed = spigoli[i] as IEdge;
                    var a = (ed?.GetStartVertex() as IVertex)?.GetPoint() as double[];
                    var b = (ed?.GetEndVertex() as IVertex)?.GetPoint() as double[];
                    if (a == null || b == null) continue;
                    if (Math.Abs(a[1] - 0.020) < 1e-6 && Math.Abs(b[1] - 0.020) < 1e-6 &&
                        Math.Abs(Math.Abs(b[0] - a[0]) - 0.020) < 1e-6 && Math.Abs(b[2] - a[2]) < 1e-6)
                        indice = i;
                }
                if (indice < 0) { ErroreDiBanco("orfane", "fra gli spigoli visibili non trovo quello alto del blocco"); return; }

                var quota = Esegui(tavola, "drawing_dimension",
                    new Dictionary<string, object> { ["edges"] = new object[] { indice } }, out errore);
                if (quota == null) { ErroreDiBanco("orfane", "drawing_dimension: " + errore); return; }

                var prima = LeggiStato(tavola);
                Segna("orfane: la quota appena messa sullo spigolo del blocco NON e' orfana",
                      Convert.ToString(prima.ContainsKey("dimensions_orphan_total") ? prima["dimensions_orphan_total"] : "(manca)") == "0",
                      "dimensions_orphan_total " + Convert.ToString(prima.ContainsKey("dimensions_orphan_total") ? prima["dimensions_orphan_total"] : "(manca)"));

                // IL ROSSO: si sopprime il blocco, e la quota resta appesa.
                bool soppresso = blocco.SetSuppression2(
                    (int)swFeatureSuppressionAction_e.swSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
                pezzo.EditRebuild3();
                tavola.ForceRebuild3(false);
                var dopo = LeggiStato(tavola);
                object orf = dopo.ContainsKey("dimensions_orphan_total") ? dopo["dimensions_orphan_total"] : "(manca)";
                Segna("orfane: soppresso il blocco, lo stato dice UNA quota orfana, e lo avvisa",
                      soppresso && Convert.ToString(orf) == "1" && dopo.ContainsKey("warning_dimensions"),
                      "soppresso " + soppresso + ", dimensions_orphan_total " + Convert.ToString(orf) +
                      ", avviso " + (dopo.ContainsKey("warning_dimensions") ? "si'" : "no"));
            }
            catch (Exception ex) { ErroreDiBanco("orfane", Perche(ex)); }
            finally
            {
                try { if (tavola != null) _sw.CloseDoc(tavola.GetTitle()); } catch { }
                Chiudi(pezzo);
                try { if (File.Exists(file)) File.Delete(file); } catch { }
            }
        }

        /// <summary>Un rettangolo sul piano superiore, estruso di `alto` metri.</summary>
        private static IFeature Estrusione(IModelDoc2 doc, double x1, double y1, double x2, double y2, double alto)
        {
            doc.ClearSelection2(true);
            int visti = 0;
            var f = doc.FirstFeature() as IFeature;
            while (f != null)
            {
                if (f.GetTypeName2() == "RefPlane" && ++visti == 2) { f.Select2(false, 0); break; }
                f = f.GetNextFeature() as IFeature;
            }
            if (visti < 2) return null;
            doc.SketchManager.InsertSketch(true);
            bool prima = doc.SketchManager.AddToDB;
            doc.SketchManager.AddToDB = true;
            doc.SketchManager.CreateCornerRectangle(x1, y1, 0, x2, y2, 0);
            doc.SketchManager.AddToDB = prima;
            doc.SketchManager.InsertSketch(true);
            return doc.FeatureManager.FeatureExtrusion3(
                true, false, false, 0, 0, alto, 0.0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, 0, 0, false) as IFeature;
        }
    }
}
