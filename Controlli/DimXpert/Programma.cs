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
//      .\Controlli\DimXpert\bin\DimXpert.exe            [--tieni]
// =============================================================================

internal static class Programma
{
    private static int _passo;
    private static void Dice(string cosa)
    {
        _passo++;
        Console.WriteLine("  " + _passo.ToString("00") + ". " + cosa);
    }

    private static int Main(string[] argomenti)
    {
        bool tieni = Array.IndexOf(argomenti, "--tieni") >= 0;
        Console.WriteLine();
        Console.WriteLine("DIMXPERT — dal nucleo, senza add-in? (voce Z-H3)");
        Console.WriteLine("  piastra 100 x 60 x 10 con un foro passante da 20");
        Console.WriteLine();

        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS non risponde: " + ex.Message); return 2; }
        Dice("agganciato a SOLIDWORKS " + sw.RevisionNumber());

        IModelDoc2 doc = null;
        try
        {
            string modello = sw.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            doc = (IModelDoc2)sw.NewDocument(modello, 0, 0, 0);
            if (doc == null) { Console.WriteLine("  NewDocument -> null"); return 4; }

            doc.ClearSelection2(true);
            if (!doc.Extension.SelectByID2("Piano superiore", "PLANE", 0, 0, 0, false, 0, null, 0))
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
            double atteso = 100 * 60 * 10 - Math.PI * 10 * 10 * 10;
            Dice("piastra col foro: volume " + vol.ToString("0.0") + " mm^3 (atteso " + atteso.ToString("0.0") + ")");

            // ── IL GESTORE DIMXPERT, DAL NUCLEO ──────────────────────────
            string config = doc.ConfigurationManager.ActiveConfiguration.Name;
            var gestore = doc.Extension.get_DimXpertManager(config, true) as IDimXpertManager;
            if (gestore == null)
            {
                Console.WriteLine("  get_DimXpertManager -> null: dal nucleo NON si arriva.");
                return 5;
            }
            Dice("DimXpertManager dal nucleo: schema «" + gestore.SchemaName + "»");

            var parte = gestore.DimXpertPart as IDimXpertPart;
            if (parte == null) { Console.WriteLine("  DimXpertPart -> null"); return 5; }

            int annPrima = parte.GetAnnotationCount();
            int carPrima = parte.GetFeatureCount();
            Dice("prima dello schema: " + carPrima + " caratteristiche, " + annPrima + " annotazioni");

            var opzioni = parte.GetAutoDimSchemeOption();
            bool fatto = opzioni != null && parte.AutoDimensionScheme(opzioni);
            int annDopo = parte.GetAnnotationCount();
            int carDopo = parte.GetFeatureCount();
            Dice("AutoDimensionScheme -> " + fatto + ": " + carDopo + " caratteristiche, " + annDopo + " annotazioni");

            // Che cosa ha riconosciuto: i tipi delle caratteristiche.
            bool unForo = false;
            var car = parte.GetFeatures() as object[];
            if (car != null)
                foreach (var o in car)
                {
                    var c = o as IDimXpertFeature;
                    if (c == null) continue;
                    string tipo = c.Type.ToString();
                    Console.WriteLine("      caratteristica «" + c.Name + "»: " + tipo);
                    if (tipo.IndexOf("Hole", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        tipo.IndexOf("Cylinder", StringComparison.OrdinalIgnoreCase) >= 0) unForo = true;
                }
            var ann = parte.GetAnnotations() as object[];
            if (ann != null)
                foreach (var o in ann)
                {
                    var a = o as IDimXpertAnnotation;
                    if (a != null) Console.WriteLine("      annotazione «" + a.Name + "»: " + a.Type);
                }

            Console.WriteLine();
            if (!fatto || annDopo <= annPrima)
            {
                Console.WriteLine("LO SCHEMA NON HA AGGIUNTO NIENTE: " + annPrima + " -> " + annDopo +
                                  " annotazioni. DimXpert dal nucleo risponde ma non quota.");
                return 6;
            }
            if (!unForo)
            {
                Console.WriteLine("Lo schema ha aggiunto annotazioni ma fra le caratteristiche non c'e' il foro:");
                Console.WriteLine("si e' quotato qualcosa, non si sa cosa.");
                return 7;
            }
            Console.WriteLine("REGGE: dal nucleo, senza add-in, uno schema DimXpert riconosce il foro e aggiunge");
            Console.WriteLine(annDopo + " annotazioni dove prima ce n'erano " + annPrima + ". Z-H3 non aspetta nessuna spunta.");
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
            if (doc != null && !tieni)
            {
                try { sw.CloseDoc(doc.GetTitle()); Dice("pezzo chiuso senza salvare"); } catch { }
            }
        }
    }
}
