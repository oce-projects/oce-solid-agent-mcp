using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  FlangiaSpigolo — la flangia a spigolo, prima di scriverla (voce GP18)
// =============================================================================
//
//  `InsertSheetMetalEdgeFlange2` vuole un elenco di spigoli E un elenco di
//  schizzi di profilo. La lunghezza c'e': e' il settimo parametro,
//  `FlangeOffsetDist` (per riflessione, 25/9/2026 notte), la stessa grandezza
//  di `IEdgeFlangeFeatureData.OffsetDistance` — la prima stesura di questa
//  sonda lo leggeva come uno spostamento e passava 0. Quello che non si sa:
//    1. se con gli schizzi a null la flangia nasce, e lunga quanto;
//    2. se cambiare OffsetDistance e poi ModifyDefinition la allunga davvero;
//    3. di quanto cresce lo sviluppo, e con quale convenzione di quota
//       (OffsetDimType: spigolo virtuale esterno, interno, tangente).
//
//  IL PEZZO: la L di `Controlli/Lamiera` (50 + 30, T 2, R 2, profonda 40), e
//  una flangia a 90 gradi sullo spigolo in fondo al lato da 50, portata a 25.
//  Lo sviluppo prima e dopo, letto togliendo la soppressione a «FlatPattern»
//  (SetBendState non sviluppa niente: misurato).
//
//  USO
//      .\Controlli\FlangiaSpigolo\bin\FlangiaSpigolo.exe            [--tieni]
// =============================================================================

internal static class Programma
{
    private static int _passo;
    private static void Dice(string cosa) { _passo++; Console.WriteLine("  " + _passo.ToString("00") + ". " + cosa); }

    private static int Main(string[] argomenti)
    {
        bool tieni = Array.IndexOf(argomenti, "--tieni") >= 0;
        Console.WriteLine();
        Console.WriteLine("FLANGIA A SPIGOLO — prima di scriverla (voce GP18)");
        Console.WriteLine();
        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS non risponde: " + ex.Message); return 2; }

        IModelDoc2 doc = null;
        try
        {
            string modello = sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            doc = (IModelDoc2)sw.NewDocument(modello, 0, 0, 0);
            doc.ClearSelection2(true);
            int visti = 0;
            var f0 = doc.FirstFeature() as IFeature;
            while (f0 != null) { if (f0.GetTypeName2() == "RefPlane" && ++visti == 1) { f0.Select2(false, 0); break; } f0 = f0.GetNextFeature() as IFeature; }
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.AddToDB = true;
            doc.SketchManager.CreateLine(0, 0.030, 0, 0, 0, 0);
            doc.SketchManager.CreateLine(0, 0, 0, 0.050, 0, 0);
            doc.SketchManager.AddToDB = false;
            doc.SketchManager.InsertSketch(true);
            var base0 = doc.FeatureManager.InsertSheetMetalBaseFlange2(0.002, false, 0.002, 0.040, 0, false,
                (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind, 0, null, true,
                (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0, 0, 0.5, true, false, false, false);
            doc.EditRebuild3();
            double s0 = Sviluppo(doc);
            Dice("flangia base: " + (base0 != null) + ", sviluppo " + s0.ToString("0.###") + " mm (atteso 80,712)");

            // Lo spigolo in fondo al lato da 50: la L va da x -2 a 50, lo
            // spigolo esterno in fondo sta a x = 50, lungo Z, sulla faccia di
            // sotto (y = -2) o di sopra (y = 0). Si prende quello a y = 0.
            doc.ClearSelection2(true);
            IEdge scelto = null;
            var corpi = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            foreach (var o in (corpi[0] as IBody2).GetEdges() as object[])
            {
                var e = o as IEdge;
                var a = (e.GetStartVertex() as IVertex)?.GetPoint() as double[];
                var b = (e.GetEndVertex() as IVertex)?.GetPoint() as double[];
                if (a == null || b == null) continue;
                if (Math.Abs(a[0] - 0.050) < 1e-6 && Math.Abs(b[0] - 0.050) < 1e-6 &&
                    Math.Abs(a[1]) < 1e-6 && Math.Abs(b[1]) < 1e-6 && Math.Abs(Math.Abs(b[2] - a[2]) - 0.040) < 1e-6)
                { scelto = e; break; }
            }
            if (scelto == null) { Console.WriteLine("  non trovo lo spigolo in fondo al lato da 50"); return 4; }
            Dice("spigolo trovato: x 50, y 0, lungo Z per 40");

            // TRE TENTATIVI, perche' la prima corsa ha dato null con gli schizzi
            // a null e non dice perche'.
            int opzioni = (int)(swInsertEdgeFlangeOptions_e.swInsertEdgeFlangeUseDefaultRadius |
                                swInsertEdgeFlangeOptions_e.swInsertEdgeFlangeUseDefaultRelief);
            // TENTATIVI 5 E 6 (25/9/2026 notte). Per riflessione il settimo
            // parametro si chiama `FlangeOffsetDist`, ed e' la stessa grandezza
            // di `IEdgeFlangeFeatureData.OffsetDistance`: la LUNGHEZZA della
            // flangia. I primi quattro tentativi passavano 0 — una flangia lunga
            // zero, che non nasce senza dire perche'. Qui 20 mm.
            IFeature flangia = null;
            for (int t = 1; t <= 6 && flangia == null; t++)
            {
                doc.ClearSelection2(true);
                if (t >= 2) (scelto as IEntity).Select4(false, null);
                object schizzi = t == 3 || t == 6 ? (object)new object[] { null } : null;
                double lunghezza = t >= 5 ? 0.020 : 0;
                try
                {
                    flangia = t == 4
                        ? doc.FeatureManager.InsertSheetMetalEdgeFlange((Edge)scelto, null, opzioni,
                            Math.PI / 2, 0.002, (int)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside, 0,
                            (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0.5, 0, 0,
                            (int)swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp, null) as IFeature
                        : doc.FeatureManager.InsertSheetMetalEdgeFlange2(new object[] { scelto }, schizzi, opzioni,
                            Math.PI / 2, 0.002, (int)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside, lunghezza,
                            (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0.5, 0, 0,
                            (int)swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp, null) as IFeature;
                }
                catch (Exception ex) { Console.WriteLine("      tentativo " + t + " alza: " + ex.Message); }
                Console.WriteLine("      tentativo " + t + " (" + (t == 1 ? "niente selezionato, schizzi null"
                    : t == 2 ? "spigolo selezionato, schizzi null" : t == 3 ? "spigolo selezionato, schizzi [null]"
                    : t == 4 ? "InsertSheetMetalEdgeFlange, la vecchia"
                    : t == 5 ? "spigolo selezionato, schizzi null, LUNGA 20"
                    : "spigolo selezionato, schizzi [null], LUNGA 20") + "): " + (flangia == null ? "null" : flangia.Name));
            }
            doc.EditRebuild3();
            if (flangia == null) { Console.WriteLine("  nessuno dei quattro tentativi fa nascere la flangia"); return 5; }
            var dati = flangia.GetDefinition() as IEdgeFlangeFeatureData;
            double s1 = Sviluppo(doc);
            Dice("flangia a spigolo nata: «" + flangia.Name + "», lunghezza " + (dati.OffsetDistance * 1000).ToString("0.###") +
                 " mm, tipo quota " + dati.OffsetDimType + ", sviluppo " + s1.ToString("0.###"));

            dati.AccessSelections(doc, null);
            dati.OffsetDistance = 0.025;
            bool mod = flangia.ModifyDefinition(dati, doc, null);
            doc.EditRebuild3();
            double s2 = Sviluppo(doc);
            var dati2 = flangia.GetDefinition() as IEdgeFlangeFeatureData;
            Dice("OffsetDistance -> 25: ModifyDefinition " + mod + ", riletta " + (dati2.OffsetDistance * 1000).ToString("0.###") +
                 " mm, sviluppo " + s2.ToString("0.###") + " (cresciuto di " + (s2 - s0).ToString("0.###") + ")");

            // Il conto: la flangia quotata allo spigolo virtuale ESTERNO di 25
            // aggiunge (25 - R - T) di dritto + BA, e toglie al lato da 50 i
            // suoi R + T in fondo.
            double ba = Math.PI / 2 * (2 + 0.5 * 2);
            Console.WriteLine();
            Console.WriteLine("  se la flangia e' quotata allo spigolo virtuale esterno: +" + (25 - 2 - 2 - 2 - 2 + ba).ToString("0.###") + " mm");
            Console.WriteLine("  se allo spigolo virtuale interno:                        +" + (25 - 2 - 2 + ba).ToString("0.###") + " mm");
            Console.WriteLine("  misurato:                                                 +" + (s2 - s0).ToString("0.###") + " mm");
            return 0;
        }
        catch (Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            Console.WriteLine("ECCEZIONE: " + ex.GetType().Name + ": " + ex.Message);
            return 9;
        }
        finally
        {
            if (doc != null && !tieni) try { sw.CloseDoc(doc.GetTitle()); } catch { }
        }
    }

    private static double Sviluppo(IModelDoc2 doc)
    {
        IFeature fp = null;
        var f = doc.FirstFeature() as IFeature;
        while (f != null && fp == null) { if (f.GetTypeName2() == "FlatPattern") fp = f; f = f.GetNextFeature() as IFeature; }
        if (fp == null) return double.NaN;
        fp.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature, (int)swInConfigurationOpts_e.swThisConfiguration, null);
        doc.EditRebuild3();
        var corpi = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
        var b = (corpi[0] as IBody2).GetBodyBox() as double[];
        double l = Math.Max(Math.Abs(b[3] - b[0]), Math.Max(Math.Abs(b[4] - b[1]), Math.Abs(b[5] - b[2]))) * 1000;
        fp.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature, (int)swInConfigurationOpts_e.swThisConfiguration, null);
        doc.EditRebuild3();
        return l;
    }
}
