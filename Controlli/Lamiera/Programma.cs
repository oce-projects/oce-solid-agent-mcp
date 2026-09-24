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
//      .\Controlli\Lamiera\bin\Lamiera.exe            [--tieni]
// =============================================================================

internal static class Programma
{
    private const double Lato1Mm = 50.0;
    private const double Lato2Mm = 30.0;
    private const double ProfonditaMm = 40.0;
    private const double SpessoreMm = 2.0;
    private const double RaggioMm = 2.0;

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
        Console.WriteLine("LAMIERA — la misura che dimensiona la famiglia (voce GP18)");
        Console.WriteLine("  flangia base a L: " + Lato1Mm + " + " + Lato2Mm + " mm, profonda " + ProfonditaMm +
                          ", spessore " + SpessoreMm + ", raggio " + RaggioMm);
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

            // ── lo schizzo aperto a L, sul piano frontale ────────────────
            doc.ClearSelection2(true);
            if (!doc.Extension.SelectByID2("Piano frontale", "PLANE", 0, 0, 0, false, 0, null, 0))
            { Console.WriteLine("  non trovo «Piano frontale»"); return 4; }
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateLine(0, Lato2Mm / 1000.0, 0, 0, 0, 0);
            doc.SketchManager.CreateLine(0, 0, 0, Lato1Mm / 1000.0, 0, 0);
            doc.SketchManager.InsertSketch(true);

            var flangia = doc.FeatureManager.InsertSheetMetalBaseFlange2(
                SpessoreMm / 1000.0, false, RaggioMm / 1000.0, ProfonditaMm / 1000.0, 0.0, false,
                (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind, 0,
                null, true, (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular,
                0.0, 0.0, 0.5, true, false, false, false);
            if (flangia == null) { Console.WriteLine("  InsertSheetMetalBaseFlange2 -> null"); return 5; }
            doc.EditRebuild3();
            Dice("flangia base creata: " + flangia.Name);

            // ── T, R, K RILETTI dalla lamiera ────────────────────────────
            ISheetMetalFeatureData dati = null;
            var f = doc.FirstFeature() as IFeature;
            while (f != null && dati == null)
            {
                if (f.GetTypeName2() == "SheetMetal") dati = f.GetDefinition() as ISheetMetalFeatureData;
                f = f.GetNextFeature() as IFeature;
            }
            if (dati == null) { Console.WriteLine("  nessuna feature «SheetMetal» nell'albero"); return 5; }
            double t = dati.Thickness * 1000.0, r = dati.BendRadius * 1000.0, k = dati.KFactor;
            int tipoBA = dati.BendAllowanceType;
            Dice("riletti: T " + t.ToString("0.###") + " mm, R " + r.ToString("0.###") + " mm, K " +
                 k.ToString("0.###") + ", calcolo della piega tipo " + tipoBA + " (2 = fattore K)");

            double volPiegato = Volume(doc);
            Dice("volume piegato " + volPiegato.ToString("0.0") + " mm^3");

            // ── SVILUPPATA ───────────────────────────────────────────────
            int esito = doc.SetBendState((int)swSMBendState_e.swSMBendStateFlattened);
            doc.EditRebuild3();
            double[] ingombro = Ingombro(doc);
            double volPiatto = Volume(doc);
            Dice("SetBendState(sviluppata) -> " + esito + ", volume sviluppato " + volPiatto.ToString("0.0") + " mm^3");
            if (ingombro == null) { Console.WriteLine("  nessun ingombro leggibile"); return 6; }
            Array.Sort(ingombro);
            double lunghezza = ingombro[2];
            Console.WriteLine("      ingombro sviluppato: " + ingombro[0].ToString("0.###") + " x " +
                              ingombro[1].ToString("0.###") + " x " + ingombro[2].ToString("0.###") + " mm");

            // ── il conto a mano, nelle due convenzioni ───────────────────
            double ba = Math.PI / 2.0 * (r + k * t);
            double interne = (Lato1Mm - r) + (Lato2Mm - r) + ba;
            double esterne = (Lato1Mm - r - t) + (Lato2Mm - r - t) + ba;
            Console.WriteLine();
            Console.WriteLine("  BA = pi/2 * (R + K*T) = " + ba.ToString("0.###") + " mm");
            Console.WriteLine("  schizzo = facce interne:  " + interne.ToString("0.###") + " mm   (scarto " +
                              (lunghezza - interne).ToString("+0.###;-0.###") + ")");
            Console.WriteLine("  schizzo = facce esterne:  " + esterne.ToString("0.###") + " mm   (scarto " +
                              (lunghezza - esterne).ToString("+0.###;-0.###") + ")");
            Console.WriteLine("  l'API, sviluppata:        " + lunghezza.ToString("0.###") + " mm");

            // ── il ritorno, e il DXF ─────────────────────────────────────
            doc.SetBendState((int)swSMBendState_e.swSMBendStateFolded);
            doc.EditRebuild3();

            string dxf = Path.Combine(Path.GetTempPath(), "lamiera-sonda.dxf");
            try { if (File.Exists(dxf)) File.Delete(dxf); } catch { }
            bool esportato = false;
            try
            {
                esportato = ((IPartDoc)doc).ExportFlatPatternView(dxf,
                    (int)swExportFlatPatternViewOptions_e.swExportFlatPatternOption_RemoveBends);
            }
            catch (Exception ex) { Console.WriteLine("      ExportFlatPatternView alza: " + ex.Message); }
            long peso = File.Exists(dxf) ? new FileInfo(dxf).Length : 0;
            Dice("ExportFlatPatternView -> " + esportato + ", DXF " + peso + " byte");

            Console.WriteLine();
            bool volumiUguali = volPiegato > 0 && Math.Abs(volPiatto - volPiegato) / volPiegato < 0.005;
            string convenzione = Math.Abs(lunghezza - interne) <= 0.5 ? "facce interne"
                               : Math.Abs(lunghezza - esterne) <= 0.5 ? "facce esterne" : null;
            if (!volumiUguali)
            {
                Console.WriteLine("IL VOLUME NON SI CONSERVA: piegato " + volPiegato.ToString("0.0") +
                                  ", sviluppato " + volPiatto.ToString("0.0") + ". La lunghezza sopra non e'");
                Console.WriteLine("la misura di uno sviluppo: la famiglia NON si costruisce finche' non si sa perche'.");
                return 7;
            }
            if (convenzione == null)
            {
                Console.WriteLine("LO SVILUPPO NON COMBACIA CON NESSUNA DELLE DUE CONVENZIONI entro 0,5 mm.");
                Console.WriteLine("La famiglia NON si costruisce finche' non si sa perche'.");
                return 6;
            }
            if (!esportato || peso == 0)
            {
                Console.WriteLine("Lo sviluppo torna (" + convenzione + "), ma il DXF non e' uscito.");
                return 8;
            }
            Console.WriteLine("REGGE: lo sviluppo e' " + lunghezza.ToString("0.###") + " mm come il conto a mano con lo");
            Console.WriteLine("schizzo sulle " + convenzione + ", il volume si conserva, e il DXF esce.");
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
            if (doc != null && !tieni)
            {
                try { sw.CloseDoc(doc.GetTitle()); Dice("pezzo chiuso senza salvare"); } catch { }
            }
        }
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

    private static double[] Ingombro(IModelDoc2 doc)
    {
        var parte = doc as IPartDoc;
        var corpi = parte == null ? null : parte.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
        if (corpi == null || corpi.Length == 0) return null;
        var box = (corpi[0] as IBody2).GetBodyBox() as double[];
        if (box == null || box.Length < 6) return null;
        return new[]
        {
            Math.Abs(box[3] - box[0]) * 1000.0,
            Math.Abs(box[4] - box[1]) * 1000.0,
            Math.Abs(box[5] - box[2]) * 1000.0,
        };
    }
}
