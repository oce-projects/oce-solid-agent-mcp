using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.HoleOrFillet
{
    /// <summary>
    /// UNA FACCIA CILINDRICA E' UN FORO O UN RACCORDO? (misura per la voce E14)
    ///
    /// ── PERCHE' LA DOMANDA ────────────────────────────────────────────────
    ///
    /// `Riferimenti` risolve il ruolo `hole_face` cercando le facce
    /// cilindriche lasciate da un passo: se ne trova **una** la restituisce, se
    /// ne trova **piu' d'una** rifiuta.
    ///
    /// Tutte e due i rami hanno un problema, e il primo e' il piu' grave:
    ///
    ///   * **un raccordo lascia facce cilindriche.** Se un passo ne lascia
    ///     esattamente una, `hole_face` la restituisce **senza dire
    ///     niente** — e chi aveva chiesto la parete di un foro si ritrova la
    ///     superficie di un raccordo. Un difetto che non da' errore.
    ///   * quattro fori uguali fanno scattare il rifiuto, ed e' giusto: «la»
    ///     parete, fra quattro identiche, non esiste.
    ///
    /// La voce E14 proponeva di scegliere con un criterio invece di rifiutare.
    /// Per il secondo caso sarebbe indovinare. Per il PRIMO c'e' un criterio
    /// vero, ed e' geometrico: **la parete di un foro e' concava, la superficie
    /// di un raccordo esterno e' convessa.**
    ///
    /// ── COSA MISURA QUESTO ATTREZZO ───────────────────────────────────────
    ///
    /// Costruisce una piastra con un foro passante e un raccordo su uno
    /// spigolo, e stampa per ogni faccia cilindrica cosa rispondono
    /// `FaceInSurfaceSense()` e la normale letta in un punto.
    ///
    ///     Checks\HoleOrFillet\bin\HoleOrFillet.exe
    ///
    /// Se i due casi rispondono uguale, il criterio non esiste e la voce E14 va
    /// riscritta. Se rispondono diverso, il numero che li separa e' qui sotto e
    /// non si deve dedurre.
    ///
    /// Non tocca nessun documento aperto: se ne crea uno suo e lo chiude.
    /// </summary>
    internal static class Programma
    {
        private static int Main()
        {
            ISldWorks sw;
            try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SOLIDWORKS non agganciabile: " + ex.Message);
                return 2;
            }

            IModelDoc2 doc = null;
            try
            {
                string modello = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                doc = sw.NewDocument(modello, 0, 0, 0) as IModelDoc2;
                if (doc == null) { Console.Error.WriteLine("parte non creata"); return 2; }

                Piastra(doc, 0.100, 0.060, 0.020);
                Foro(doc);
                Raccordo(doc);

                Console.WriteLine();
                Console.WriteLine("=== una faccia cilindrica e' un foro o un raccordo? ===");
                Console.WriteLine();
                Console.WriteLine("  {0,-6} {1,-10} {2,-12} {3,-10} {4}",
                                  "uid", "diameter", "FaceInSurf", "area", "da");

                var parte = doc as IPartDoc;
                var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (corpi == null || corpi.Length == 0) { Console.Error.WriteLine("nessun corpo"); return 2; }

                int n = 0, concave = 0, convesse = 0;
                var corpo = corpi[0] as IBody2;
                var f = corpo.IGetFirstFace();
                while (f != null)
                {
                    double raggio;
                    if (Cilindrica(f, out raggio))
                    {
                        n++;
                        bool sense = false;
                        try { sense = f.FaceInSurfaceSense(); } catch { }
                        double area = 0;
                        try { area = f.GetArea() * 1e6; } catch { }
                        string da = "";
                        try { da = (f.GetFeature() as IFeature)?.Name ?? ""; } catch { }

                        if (sense) concave++; else convesse++;

                        Console.WriteLine("  {0,-6} {1,-10} {2,-12} {3,-10} {4}",
                                          "C" + n,
                                          (raggio * 2000).ToString("0.##"),
                                          sense ? "true" : "false",
                                          area.ToString("0.#"),
                                          da);
                    }
                    f = f.IGetNextFace();
                }

                Console.WriteLine();
                Console.WriteLine("  facce cilindriche: " + n +
                                  "   con FaceInSurfaceSense=true: " + concave +
                                  "   =false: " + convesse);
                Console.WriteLine();
                Console.WriteLine(n >= 2 && concave >= 1 && convesse >= 1
                    ? "I DUE CASI RISPONDONO DIVERSO: il criterio esiste."
                    : "ATTENZIONE: i due casi NON si separano con questo valore.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("la prova si e' rotta: " + ex.GetType().Name +
                                        ": " + ex.Message);
                return 2;
            }
            finally
            {
                if (doc != null) { try { sw.CloseDoc(doc.GetTitle()); } catch { } }
            }
        }

        private static bool Cilindrica(IFace2 faccia, out double raggio)
        {
            raggio = 0;
            try
            {
                var s = faccia.IGetSurface();
                if (s == null || !s.IsCylinder()) return false;
                var par = s.CylinderParams as double[];
                if (par == null || par.Length < 7) return false;
                raggio = par[6];
                return raggio > 0;
            }
            catch { return false; }
        }

        private static void Piastra(IModelDoc2 doc, double l, double w, double h)
        {
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateCornerRectangle(-l / 2, -w / 2, 0, l / 2, w / 2, 0);
            doc.SketchManager.InsertSketch(true);
            doc.FeatureManager.FeatureExtrusion3(
                true, false, false, 0, 0, h, 0, false, false, false, false,
                0, 0, false, false, false, false, true, true, true, 0, 0, false);
        }

        /// <summary>Un foro passante al centro della faccia in cima.</summary>
        private static void Foro(IModelDoc2 doc)
        {
            doc.ClearSelection2(true);
            doc.Extension.SelectByRay(0, 0.020, 0, 0, -1, 0, 0.001,
                                      (int)swSelectType_e.swSelFACES, false, 0,
                                      (int)swSelectOption_e.swSelectOptionDefault);
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateCircleByRadius(0, 0, 0, 0.006);
            doc.FeatureManager.FeatureCut4(
                true, false, false, (int)swEndConditions_e.swEndCondThroughAll, 0,
                0.01, 0.01, false, false, false, false, 0, 0,
                false, false, false, false, false, true, true, true, true, false,
                0, 0, false, false);
        }

        /// <summary>Un raccordo su uno spigolo verticale: superficie CONVESSA.</summary>
        private static void Raccordo(IModelDoc2 doc)
        {
            doc.ClearSelection2(true);
            // Lo spigolo verticale in un angolo della piastra 100x60.
            doc.Extension.SelectByRay(0.050, 0.010, 0.030, 0, 0, -1, 0.002,
                                      (int)swSelectType_e.swSelEDGES, false, 0,
                                      (int)swSelectOption_e.swSelectOptionDefault);
            try
            {
                doc.FeatureManager.FeatureFillet3(
                    195, 0.005, 0, 0, 0, 0, 0, null, null, null, null, null, null, null);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("(raccordo non riuscito: " + ex.Message + ")");
            }
        }
    }
}
