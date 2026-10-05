using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// LA QUOTA CHE RESTA SENZA SPIGOLO, VISTA DALLO STATO (voce Z-A2).
    ///
    /// `IAnnotation.IsDangling` e' morto per le quote in tavola fuori processo:
    /// misurato il 24/9/2026 con `Checks/Orphan`, resta falso su una quota il
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
        private static void TestOrphanDimensions()
        {
            Console.WriteLine();
            Console.WriteLine("=== orfane: la quota che resta senza spigolo (Z-A2) ===");
            IModelDoc2 part = null, drawing = null;
            string file = Path.Combine(Expectations.Work, "orfane_banco.sldprt");
            bool asked = true, off = false;
            try
            {
                part = NewPart();
                // Due estrusioni dal piano SUPERIORE, preso per posizione e non
                // per nome: il nome cambia con la lingua dell'installazione.
                if (Extrusion(part, 0, 0, 0.100, 0.060, 0.010) == null)
                { BenchError("orfane", "la piastra non e' venuta"); return; }
                var block = Extrusion(part, 0.040, 0.020, 0.060, 0.040, 0.020);
                if (block == null) { BenchError("orfane", "il blocco non e' venuto"); return; }

                try { if (File.Exists(file)) File.Delete(file); } catch { }
                int e = 0, w = 0;
                part.Extension.SaveAs(file, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e, ref w);
                if (!File.Exists(file)) { BenchError("orfane", "il pezzo non si salva (e=" + e + ")"); return; }

                string error;
                Run(part, "create_drawing", new Dictionary<string, object> { ["paper_size"] = "A3" },
                       out error, out drawing);
                if (drawing == null) { BenchError("orfane", "create_drawing: " + error); return; }
                if (Run(drawing, "model_view", new Dictionary<string, object>
                    { ["model"] = file, ["view"] = "front", ["x_mm"] = 150, ["y_mm"] = 150 }, out error) == null)
                { BenchError("orfane", "model_view: " + error); return; }

                // QUALE SPIGOLO: quello alto del blocco — Y 20 alle due estremita',
                // lungo X per 20. Lo si cerca nello stesso elenco che
                // `drawing_dimension` chiede alla vista, e se ne passa l'indice.
                var view = ((IDrawingDoc)drawing).GetFirstView() as IView;
                view = view?.GetNextView() as IView;
                var edges = view?.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
                int index = -1;
                for (int i = 0; edges != null && i < edges.Length && index < 0; i++)
                {
                    var ed = edges[i] as IEdge;
                    var a = (ed?.GetStartVertex() as IVertex)?.GetPoint() as double[];
                    var b = (ed?.GetEndVertex() as IVertex)?.GetPoint() as double[];
                    if (a == null || b == null) continue;
                    if (Math.Abs(a[1] - 0.020) < 1e-6 && Math.Abs(b[1] - 0.020) < 1e-6 &&
                        Math.Abs(Math.Abs(b[0] - a[0]) - 0.020) < 1e-6 && Math.Abs(b[2] - a[2]) < 1e-6)
                        index = i;
                }
                if (index < 0) { BenchError("orfane", "fra gli spigoli visibili non trovo quello alto del blocco"); return; }

                var dimension = Run(drawing, "drawing_dimension",
                    new Dictionary<string, object> { ["edges"] = new object[] { index } }, out error);
                if (dimension == null) { BenchError("orfane", "drawing_dimension: " + error); return; }

                var before = ReadState(drawing);
                Check("orfane: la quota appena messa sullo spigolo del blocco NON e' orfana",
                      Convert.ToString(before.ContainsKey("dimensions_orphan_total") ? before["dimensions_orphan_total"] : "(manca)") == "0",
                      "dimensions_orphan_total " + Convert.ToString(before.ContainsKey("dimensions_orphan_total") ? before["dimensions_orphan_total"] : "(manca)"));

                // IL ROSSO: si sopprime il blocco, e la quota resta appesa.
                bool suppressed = block.SetSuppression2(
                    (int)swFeatureSuppressionAction_e.swSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
                part.EditRebuild3();
                drawing.ForceRebuild3(false);
                var after = ReadState(drawing);
                object orf = after.ContainsKey("dimensions_orphan_total") ? after["dimensions_orphan_total"] : "(manca)";
                Check("orfane: soppresso il blocco, lo stato dice UNA quota orfana, e lo avvisa",
                      suppressed && Convert.ToString(orf) == "1" && after.ContainsKey("warning_dimensions"),
                      "soppresso " + suppressed + ", dimensions_orphan_total " + Convert.ToString(orf) +
                      ", avviso " + (after.ContainsKey("warning_dimensions") ? "si'" : "no"));
            }
            catch (Exception ex) { BenchError("orfane", Why(ex)); }
            finally
            {
                try { if (drawing != null) _sw.CloseDoc(drawing.GetTitle()); } catch { }
                Close(part);
                try { if (File.Exists(file)) File.Delete(file); } catch { }
            }
        }

        /// <summary>Un rettangolo sul piano superiore, estruso di `alto` metri.</summary>
        private static IFeature Extrusion(IModelDoc2 doc, double x1, double y1, double x2, double y2, double up)
        {
            doc.ClearSelection2(true);
            int seen = 0;
            var f = doc.FirstFeature() as IFeature;
            while (f != null)
            {
                if (f.GetTypeName2() == "RefPlane" && ++seen == 2) { f.Select2(false, 0); break; }
                f = f.GetNextFeature() as IFeature;
            }
            if (seen < 2) return null;
            doc.SketchManager.InsertSketch(true);
            bool before = doc.SketchManager.AddToDB;
            doc.SketchManager.AddToDB = true;
            doc.SketchManager.CreateCornerRectangle(x1, y1, 0, x2, y2, 0);
            doc.SketchManager.AddToDB = before;
            doc.SketchManager.InsertSketch(true);
            return doc.FeatureManager.FeatureExtrusion3(
                true, false, false, 0, 0, up, 0.0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, 0, 0, false) as IFeature;
        }
    }
}
