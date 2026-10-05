using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swdimxpert;

// =============================================================================
//  DimXpert — si raggiunge dal nucleo, senza add-in? (voce Z-H3, e con lei GT8)
// =============================================================================
//
//  La voce diceva «serve la stessa spunta» di Utilities. La riflessione del
//  24/9/2026 sera dice un'altra cosa: `IModelDocExtension.get_DimXpertManager`
//  sta nell'interop del NUCLEO, e da li' si arriva a `IDimXpertPart` con
//  AutoDimensionScheme, InsertDatum, InsertGtol. Questa sonda lo prova su un
//  pezzo, invece di crederlo.
//
//  IL PEZZO: una piastra 100 x 60 x 10 con un foro passante da 20 al centro,
//  fatta con UNO schizzo (rettangolo e cerchio insieme): nessuna faccia da
//  scegliere. Il foro e' la cosa che uno schema di quotatura deve riconoscere.
//
//  IL CONTROLLO NEI DUE VERSI: prima dello schema le annotazioni DimXpert sono
//  ZERO; dopo sono piu' di zero, e fra le caratteristiche riconosciute c'e' il
//  foro. Uno schema che «riesce» senza aggiungere niente non e' uno schema.
//
//  USO
//      .\Checks\DimXpert\bin\DimXpert.exe            [--tieni]
// =============================================================================

internal static class Program
{
    private static int _step;
    private static void Says(string what)
    {
        _step++;
        Console.WriteLine("  " + _step.ToString("00") + ". " + what);
    }

    private static int Main(string[] arguments)
    {
        bool keep = Array.IndexOf(arguments, "--tieni") >= 0;
        Console.WriteLine();
        Console.WriteLine("DIMXPERT — dal nucleo, senza add-in? (voce Z-H3)");
        Console.WriteLine("  piastra 100 x 60 x 10 con un foro passante da 20");
        Console.WriteLine();

        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS non risponde: " + ex.Message); return 2; }
        Says("agganciato a SOLIDWORKS " + sw.RevisionNumber());

        IModelDoc2 doc = null;
        try
        {
            string model = sw.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            doc = (IModelDoc2)sw.NewDocument(model, 0, 0, 0);
            if (doc == null) { Console.WriteLine("  NewDocument -> null"); return 4; }

            doc.ClearSelection2(true);
            if (!SelectBasePlane(doc, 2))
            { Console.WriteLine("  non trovo «Piano superiore»"); return 4; }
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateCornerRectangle(0, 0, 0, 0.100, 0.060, 0);
            doc.SketchManager.CreateCircleByRadius(0.050, 0.030, 0, 0.010);
            doc.SketchManager.InsertSketch(true);
            var f = doc.FeatureManager.FeatureExtrusion3(
                true, false, false, 0, 0, 0.010, 0.0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, 0, 0, false);
            if (f == null) { Console.WriteLine("  l'estrusione non e' venuta"); return 4; }
            double vol = doc.Extension.CreateMassProperty().Volume * 1e9;
            double expected = 100 * 60 * 10 - Math.PI * 10 * 10 * 10;
            Says("piastra col foro: volume " + vol.ToString("0.0") + " mm^3 (atteso " + expected.ToString("0.0") + ")");

            // ── IL GESTORE DIMXPERT, DAL NUCLEO ──────────────────────────
            string config = doc.ConfigurationManager.ActiveConfiguration.Name;
            var handler = doc.Extension.get_DimXpertManager(config, true) as IDimXpertManager;
            if (handler == null)
            {
                Console.WriteLine("  get_DimXpertManager -> null: dal nucleo NON si arriva.");
                return 5;
            }
            Says("DimXpertManager dal nucleo: schema «" + handler.SchemaName + "»");

            var partDoc = handler.DimXpertPart as IDimXpertPart;
            if (partDoc == null) { Console.WriteLine("  DimXpertPart -> null"); return 5; }

            int annBefore = partDoc.GetAnnotationCount();
            int loadBefore = partDoc.GetFeatureCount();
            Says("prima dello schema: " + loadBefore + " caratteristiche, " + annBefore + " annotazioni");

            // MISURATO il 24/9 sera: con le opzioni di serie e niente
            // selezionato, `AutoDimensionScheme` risponde False e non fa niente.
            // Le opzioni non hanno campi per i riferimenti: i riferimenti si
            // SELEZIONANO prima, con un marchio per il primario, il secondario
            // e il terziario. Quali marchi, non si sa: si provano.
            //
            // La piastra sta in X 0..100, Y 0..10, Z -60..0 (lo schizzo sul
            // piano superiore ha la Y dello schizzo verso -Z del modello).
            var options = partDoc.GetAutoDimSchemeOption();
            bool done = false;
            int annAfter = annBefore, loadAfter = loadBefore;
            if (options != null)
            {
                options.PartType = (int)swDimXpertAutoDimSchemePartType_e.swDimXpertAutoDimSchemePartType_Prismatic;
                options.ToleranceType = (int)swDimXpertAutoDimSchemeToleranceType_e.swDimXpertAutoDimSchemeToleranceType_PlusMinus;
                options.ScopeAllFeature = true;
                options.FeatureFilters = 0xFFFF;
                foreach (var marks in new[] { new[] { 0, 0, 0 }, new[] { 1, 2, 4 }, new[] { 1, 2, 3 } })
                {
                    doc.ClearSelection2(true);
                    bool s1 = marks[0] == 0 || doc.Extension.SelectByID2("", "FACE", 0.020, 0.0, -0.010, true, marks[0], null, 0);
                    bool s2 = marks[1] == 0 || doc.Extension.SelectByID2("", "FACE", 0.0, 0.005, -0.010, true, marks[1], null, 0);
                    bool s3 = marks[2] == 0 || doc.Extension.SelectByID2("", "FACE", 0.020, 0.005, 0.0, true, marks[2], null, 0);
                    try { done = partDoc.AutoDimensionScheme(options); } catch (Exception ex) { Console.WriteLine("      alza: " + ex.Message); }
                    annAfter = partDoc.GetAnnotationCount();
                    loadAfter = partDoc.GetFeatureCount();
                    Says("marchi " + string.Join("/", marks) + " (selezioni " + s1 + "/" + s2 + "/" + s3 +
                         "): AutoDimensionScheme -> " + done + ", " + loadAfter + " caratteristiche, " + annAfter + " annotazioni");
                    // NON SI GUARDA `fatto`: misurato il 24/9 sera, risponde
                    // False anche quando ha messo sei quote giuste. Si guarda
                    // se le annotazioni ci sono.
                    if (annAfter > 3) break;
                }
                doc.ClearSelection2(true);
            }

            // Che cosa ha riconosciuto: i tipi delle caratteristiche.
            bool aHole = false;
            var car = partDoc.GetFeatures() as object[];
            if (car != null)
                foreach (var o in car)
                {
                    var c = o as IDimXpertFeature;
                    if (c == null) continue;
                    string typ = c.Type.ToString();
                    Console.WriteLine("      caratteristica «" + c.Name + "»: " + typ);
                    if (typ.IndexOf("Hole", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typ.IndexOf("Cylinder", StringComparison.OrdinalIgnoreCase) >= 0) aHole = true;
                }
            // I VALORI, non solo il conteggio: il nome porta il valore fra
            // parentesi («DistanzaTra2@Piano10(60)»). Sei quote con numeri a
            // caso sarebbero sei quote, e nessuna giusta.
            var expectedValues = new System.Collections.Generic.List<double> { 20, 10, 60, 100, 50, 30 };
            var ann = partDoc.GetAnnotations() as object[];
            if (ann != null)
                foreach (var o in ann)
                {
                    var a = o as IDimXpertAnnotation;
                    if (a == null) continue;
                    Console.WriteLine("      annotazione «" + a.Name + "»: " + a.Type);
                    int ap = a.Name.LastIndexOf('('), ch = a.Name.LastIndexOf(')');
                    double v;
                    if (ap > 0 && ch > ap && double.TryParse(a.Name.Substring(ap + 1, ch - ap - 1),
                            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                        expectedValues.RemoveAll(x => Math.Abs(x - v) < 1e-6);
                }
            Console.WriteLine("      valori attesi non trovati: " + (expectedValues.Count == 0 ? "nessuno" : string.Join(", ", expectedValues)));

            Console.WriteLine();
            if (expectedValues.Count > 0)
            {
                Console.WriteLine("LO SCHEMA HA QUOTATO, MA NON TUTTO: mancano " + string.Join(", ", expectedValues) + ".");
                return 8;
            }
            if (annAfter <= annBefore)
            {
                Console.WriteLine("LO SCHEMA NON HA AGGIUNTO NIENTE: " + annBefore + " -> " + annAfter +
                                  " annotazioni. DimXpert dal nucleo risponde ma non quota.");
                return 6;
            }
            if (!aHole)
            {
                Console.WriteLine("Lo schema ha aggiunto annotazioni ma fra le caratteristiche non c'e' il foro:");
                Console.WriteLine("si e' quotato qualcosa, non si sa cosa.");
                return 7;
            }
            Console.WriteLine("REGGE: dal nucleo, senza add-in, uno schema DimXpert riconosce il foro e aggiunge");
            Console.WriteLine(annAfter + " annotazioni dove prima ce n'erano " + annBefore + ", coi valori giusti — e");
            Console.WriteLine("AutoDimensionScheme ha risposto " + done + ". Z-H3 non aspetta nessuna spunta.");
            return 0;
        }
        catch (Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            Console.WriteLine();
            Console.WriteLine("ECCEZIONE: " + ex.GetType().Name + ": " + ex.Message);
            return 9;
        }
        finally
        {
            if (doc != null && !keep)
            {
                try { sw.CloseDoc(doc.GetTitle()); Says("pezzo chiuso senza salvare"); } catch { }
            }
        }
    }

    // W3.1-bis.2 point 10: base planes by position, not by their localized name
    // (a plane name is only valid in the SOLIDWORKS UI language of the machine
    // that created the part). The first three RefPlane features of the
    // tree, same rule as Checks/Rib, Checks/Fill and
    // Checks/ExtrusionType. NOTE for whoever touches this next: since
    // Z-C21.5 the bridge itself (Oce.Solid.Agent.Core/References.cs, BasePlane)
    // no longer trusts tree position either — it recognises a base plane by its
    // canonical normal through the world origin, because a template with one
    // extra plane ahead of the three defaults fools a position-based rule with
    // no error. This probe still uses position because it is a throwaway part
    // built by SOLIDWORKS' own default template and it cannot reference the
    // library (single-file compile, see Checks/build-checks.ps1);
    // if the template ever grows a leading plane, this rule silently picks the
    // wrong one.
    private static bool SelectBasePlane(IModelDoc2 doc, int which)
    {
        int seen = 0;
        var f = doc.FirstFeature() as IFeature;
        while (f != null)
        {
            if (f.GetTypeName2() == "RefPlane" && ++seen == which) { doc.ClearSelection2(true); return f.Select2(false, 0); }
            f = f.GetNextFeature() as IFeature;
        }
        return false;
    }
}
