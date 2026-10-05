using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  Lamiera — la misura che dimensiona la famiglia lamiera (voce GP18, passo 2)
// =============================================================================
//
//  Il 24/9/2026 si e' saputo che l'officina piega lamiera come lavoro
//  ricorrente. La famiglia produce il file che va alla macchina di taglio, ed
//  e' quindi quella in cui un difetto silenzioso costa lamiera vera: prima di
//  costruire le operazioni, UNA misura che si sa fare a mano.
//
//  IL PEZZO: una flangia base da uno schizzo aperto a L — un lato da 50 mm, uno
//  da 30 — spessa T, raggio interno R, profonda 40 mm. Sviluppata, e' un
//  rettangolo lungo
//
//      L_piatto = tratto_1 + tratto_2 + BA,    BA = pi/2 * (R + K*T)   (90 gradi)
//
//  dove i due tratti dipendono da COSA rappresenta lo schizzo: le facce interne
//  (tratti = lato - R) o quelle esterne (tratti = lato - R - T). Non lo si
//  indovina: si calcolano tutte e due, e la misura dice quale convenzione usa
//  SOLIDWORKS — come la sonda dello spessore ha detto in che unita' lavora.
//
//  T, R e K NON si prendono da qui: si RILEGGONO dalla lamiera creata. Se il
//  modello di documento ha un K diverso da quello che uno si aspetta, il conto
//  a mano si fa col K vero.
//
//  IL CONTROLLO NEI DUE VERSI: il volume sviluppato deve essere uguale a quello
//  piegato (la lamiera non si allunga ne' si accorcia nel conto: e' la
//  definizione di fibra neutra). Un rettangolo della lunghezza giusta con il
//  volume sbagliato vorrebbe dire che si e' misurata un'altra cosa.
//
//  USO
//      .\Checks\SheetMetal\bin\SheetMetal.exe            [--tieni]
// =============================================================================

internal static class Program
{
    private const double Side1Mm = 50.0;
    private const double Side2Mm = 30.0;
    private const double DepthMm = 40.0;
    private const double ThicknessMm = 2.0;
    private const double RadiusMm = 2.0;

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
        Console.WriteLine("LAMIERA — la misura che dimensiona la famiglia (voce GP18)");
        Console.WriteLine("  flangia base a L: " + Side1Mm + " + " + Side2Mm + " mm, profonda " + DepthMm +
                          ", spessore " + ThicknessMm + ", raggio " + RadiusMm);
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

            // ── lo schizzo aperto a L, sul piano frontale ────────────────
            doc.ClearSelection2(true);
            if (!SelectBasePlane(doc, 1))
            { Console.WriteLine("  non trovo «Piano frontale»"); return 4; }
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateLine(0, Side2Mm / 1000.0, 0, 0, 0, 0);
            doc.SketchManager.CreateLine(0, 0, 0, Side1Mm / 1000.0, 0, 0);
            doc.SketchManager.InsertSketch(true);

            var flange = doc.FeatureManager.InsertSheetMetalBaseFlange2(
                ThicknessMm / 1000.0, false, RadiusMm / 1000.0, DepthMm / 1000.0, 0.0, false,
                (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind, 0,
                null, true, (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular,
                0.0, 0.0, 0.5, true, false, false, false);
            if (flange == null) { Console.WriteLine("  InsertSheetMetalBaseFlange2 -> null"); return 5; }
            doc.EditRebuild3();
            Says("flangia base creata: " + flange.Name);

            // ── T, R, K RILETTI dalla lamiera ────────────────────────────
            ISheetMetalFeatureData data = null;
            var f = doc.FirstFeature() as IFeature;
            while (f != null && data == null)
            {
                if (f.GetTypeName2() == "SheetMetal") data = f.GetDefinition() as ISheetMetalFeatureData;
                f = f.GetNextFeature() as IFeature;
            }
            if (data == null) { Console.WriteLine("  nessuna feature «SheetMetal» nell'albero"); return 5; }
            double t = data.Thickness * 1000.0, r = data.BendRadius * 1000.0, k = data.KFactor;
            int typeBa = data.BendAllowanceType;
            Says("riletti: T " + t.ToString("0.###") + " mm, R " + r.ToString("0.###") + " mm, K " +
                 k.ToString("0.###") + ", calcolo della piega tipo " + typeBa + " (2 = fattore K)");

            double bentVol = Volume(doc);
            Says("volume piegato " + bentVol.ToString("0.0") + " mm^3");

            // ── SVILUPPATA ───────────────────────────────────────────────
            //
            // MISURATO il 24/9 sera: `SetBendState(sviluppata)` risponde 0 e il
            // corpo resta PIEGATO (ingombro 32 x 40 x 52 prima e dopo). Nella
            // lamiera di oggi lo sviluppo e' una LAVORAZIONE in fondo all'albero,
            // soppressa: si sviluppa togliendole la soppressione. Si prova
            // SetBendState lo stesso e si dice cosa ha fatto, poi la lavorazione.
            int stateBefore = doc.GetBendState();
            int outcome = doc.SetBendState((int)swSMBendState_e.swSMBendStateFlattened);
            doc.EditRebuild3();
            var afterSet = BoundingBox(doc);
            Says("GetBendState " + stateBefore + ", SetBendState(sviluppata) -> " + outcome + ", GetBendState " +
                 doc.GetBendState() + ", ingombro " + Three(afterSet));
            doc.SetBendState((int)swSMBendState_e.swSMBendStateFolded);
            doc.EditRebuild3();

            IFeature flatPattern = null;
            var g = doc.FirstFeature() as IFeature;
            while (g != null && flatPattern == null)
            {
                if (g.GetTypeName2() == "FlatPattern") flatPattern = g;
                g = g.GetNextFeature() as IFeature;
            }
            if (flatPattern == null) { Console.WriteLine("  nessuna lavorazione «FlatPattern» nell'albero"); return 5; }
            bool removed = flatPattern.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                (int)swInConfigurationOpts_e.swThisConfiguration, null);
            doc.EditRebuild3();
            double[] boundingBox = BoundingBox(doc);
            double flatVol = Volume(doc);
            Says("«" + flatPattern.Name + "» senza soppressione: " + removed + ", volume sviluppato " +
                 flatVol.ToString("0.0") + " mm^3");
            flatPattern.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,
                (int)swInConfigurationOpts_e.swThisConfiguration, null);
            doc.EditRebuild3();
            if (boundingBox == null) { Console.WriteLine("  nessun ingombro leggibile"); return 6; }
            Array.Sort(boundingBox);
            double length = boundingBox[2];
            Console.WriteLine("      ingombro sviluppato: " + boundingBox[0].ToString("0.###") + " x " +
                              boundingBox[1].ToString("0.###") + " x " + boundingBox[2].ToString("0.###") + " mm");

            // ── il conto a mano, nelle due convenzioni ───────────────────
            double ba = Math.PI / 2.0 * (r + k * t);
            double inner = (Side1Mm - r) + (Side2Mm - r) + ba;
            double outer = (Side1Mm - r - t) + (Side2Mm - r - t) + ba;
            Console.WriteLine();
            Console.WriteLine("  BA = pi/2 * (R + K*T) = " + ba.ToString("0.###") + " mm");
            Console.WriteLine("  schizzo = facce interne:  " + inner.ToString("0.###") + " mm   (scarto " +
                              (length - inner).ToString("+0.###;-0.###") + ")");
            Console.WriteLine("  schizzo = facce esterne:  " + outer.ToString("0.###") + " mm   (scarto " +
                              (length - outer).ToString("+0.###;-0.###") + ")");
            Console.WriteLine("  l'API, sviluppata:        " + length.ToString("0.###") + " mm");

            // ── il DXF ───────────────────────────────────────────────────
            //
            // MISURATO il 24/9 sera: `ExportFlatPatternView` su un pezzo MAI
            // SALVATO apre «Salva con nome» e la chiamata COM non torna finche'
            // qualcuno non chiude la finestra. Si salva prima; e la chiamata si
            // fa lo stesso da un altro filo, con un tempo massimo, perche' un
            // secondo modale non ancora conosciuto non pianti la sonda.
            string savedPart = Path.Combine(Path.GetTempPath(), "lamiera-sonda.SLDPRT");
            try { if (File.Exists(savedPart)) File.Delete(savedPart); } catch { }
            int se = 0, knows = 0;
            bool saved = doc.Extension.SaveAs(savedPart, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref se, ref knows);
            Says("pezzo salvato prima del DXF: " + saved + " (errori " + se + ")");

            string dxf = Path.Combine(Path.GetTempPath(), "lamiera-sonda.dxf");
            try { if (File.Exists(dxf)) File.Delete(dxf); } catch { }
            bool exported = false;
            string exception = null;
            var wire = new System.Threading.Thread(() =>
            {
                try
                {
                    exported = ((IPartDoc)doc).ExportFlatPatternView(dxf,
                        (int)swExportFlatPatternViewOptions_e.swExportFlatPatternOption_RemoveBends);
                }
                catch (Exception ex) { exception = ex.Message; }
            });
            wire.IsBackground = true;
            wire.Start();
            if (!wire.Join(60000))
            {
                Console.WriteLine("  ExportFlatPatternView NON E' TORNATA in 60 s: quasi certamente un modale aperto.");
                Console.WriteLine("  Chiudilo a mano. Mi fermo senza chiudere il pezzo.");
                return 10;
            }
            if (exception != null) Console.WriteLine("      ExportFlatPatternView alza: " + exception);
            long weight = File.Exists(dxf) ? new FileInfo(dxf).Length : 0;
            Says("ExportFlatPatternView -> " + exported + ", DXF " + weight + " byte");

            Console.WriteLine();
            bool equalVolumes = bentVol > 0 && Math.Abs(flatVol - bentVol) / bentVol < 0.005;
            string convention = Math.Abs(length - inner) <= 0.5 ? "facce interne"
                               : Math.Abs(length - outer) <= 0.5 ? "facce esterne" : null;
            if (!equalVolumes)
            {
                Console.WriteLine("IL VOLUME NON SI CONSERVA: piegato " + bentVol.ToString("0.0") +
                                  ", sviluppato " + flatVol.ToString("0.0") + ". La lunghezza sopra non e'");
                Console.WriteLine("la misura di uno sviluppo: la famiglia NON si costruisce finche' non si sa perche'.");
                return 7;
            }
            if (convention == null)
            {
                Console.WriteLine("LO SVILUPPO NON COMBACIA CON NESSUNA DELLE DUE CONVENZIONI entro 0,5 mm.");
                Console.WriteLine("La famiglia NON si costruisce finche' non si sa perche'.");
                return 6;
            }
            if (!exported || weight == 0)
            {
                Console.WriteLine("Lo sviluppo torna (" + convention + "), ma il DXF non e' uscito.");
                return 8;
            }
            Console.WriteLine("REGGE: lo sviluppo e' " + length.ToString("0.###") + " mm come il conto a mano con lo");
            Console.WriteLine("schizzo sulle " + convention + ", il volume si conserva, e il DXF esce.");
            Console.WriteLine("La famiglia lamiera si puo' costruire.");
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
                try
                {
                    var f = Path.Combine(Path.GetTempPath(), "lamiera-sonda.SLDPRT");
                    if (File.Exists(f)) File.Delete(f);
                }
                catch { }
            }
        }
    }

    private static string Three(double[] b)
    {
        return b == null ? "-" : b[0].ToString("0.###") + " x " + b[1].ToString("0.###") + " x " + b[2].ToString("0.###");
    }

    private static double Volume(IModelDoc2 doc)
    {
        try
        {
            var m = doc.Extension.CreateMassProperty();
            return m == null ? double.NaN : m.Volume * 1e9;
        }
        catch { return double.NaN; }
    }

    private static double[] BoundingBox(IModelDoc2 doc)
    {
        var partDoc = doc as IPartDoc;
        var bodies = partDoc == null ? null : partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
        if (bodies == null || bodies.Length == 0) return null;
        var box = (bodies[0] as IBody2).GetBodyBox() as double[];
        if (box == null || box.Length < 6) return null;
        return new[]
        {
            Math.Abs(box[3] - box[0]) * 1000.0,
            Math.Abs(box[4] - box[1]) * 1000.0,
            Math.Abs(box[5] - box[2]) * 1000.0,
        };
    }

    // W3.1-bis.2 point 10: base planes by position, not by their localized name.
    // The first three RefPlane features of the tree, same rule as
    // Checks/Rib, Checks/Fill and Checks/ExtrusionType.
    // Since Z-C21.5 the library itself (Oce.Solid.Agent.Core/References.cs,
    // BasePlane) recognises a base plane by its canonical normal through the
    // world origin instead, because a template with an extra plane ahead of the
    // three defaults fools a position-based rule with no error; this probe
    // stays positional because it is a throwaway part on SOLIDWORKS' own
    // default template and cannot reference the library (single-file compile,
    // Checks/build-checks.ps1).
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
