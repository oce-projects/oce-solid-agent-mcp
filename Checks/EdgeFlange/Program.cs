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
//  IL PEZZO: la L di `Checks/SheetMetal` (50 + 30, T 2, R 2, profonda 40), e
//  una flangia a 90 gradi sullo spigolo in fondo al lato da 50, portata a 25.
//  Lo sviluppo prima e dopo, letto togliendo la soppressione a «FlatPattern»
//  (SetBendState non sviluppa niente: misurato).
//
//  USO
//      .\Checks\EdgeFlange\bin\EdgeFlange.exe            [--tieni]
// =============================================================================

internal static class Programma
{
    private static int _passo;
    private static void Dice(string cosa) { _passo++; Console.WriteLine("  " + _passo.ToString("00") + ". " + cosa); }

    private static int Main(string[] argomenti)
    {
        bool tieni = Array.IndexOf(argomenti, "--tieni") >= 0;
        // --testa: salta i tentativi 1-9 (tutti misurati il 25-26/9) e prova
        // solo il 10, il profilo disegnato sulla faccia di testa.
        bool testa = Array.IndexOf(argomenti, "--testa") >= 0;
        // --alta=N: l'altezza del rettangolo di profilo in mm (20 se non detta).
        double alta = 0.020;
        foreach (var arg in argomenti)
            if (arg.StartsWith("--alta=")) alta = double.Parse(arg.Substring(7), System.Globalization.CultureInfo.InvariantCulture) / 1000.0;
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
            // TENTATIVI 7 E 8 (26/9/2026). La trappola gia' pagata con le
            // booleane (memoria «array tipizzati»): un elenco passato come
            // `object[]` fa rispondere null senza eccezione, e vuole il tipo
            // vero — `Edge[]` per gli spigoli, `Feature[]` per gli schizzi.
            IFeature flangia = null;
            for (int t = 1; t <= 8 && flangia == null && !testa; t++)
            {
                doc.ClearSelection2(true);
                if (t >= 2) (scelto as IEntity).Select4(false, null);
                object schizzi = t == 3 || t == 6 ? (object)new object[] { null }
                               : t == 8 ? (object)new Feature[] { null } : null;
                double lunghezza = t >= 5 ? 0.020 : 0;
                object spigoli = t >= 7 ? (object)new Edge[] { (Edge)scelto } : new object[] { scelto };
                try
                {
                    flangia = t == 4
                        ? doc.FeatureManager.InsertSheetMetalEdgeFlange((Edge)scelto, null, opzioni,
                            Math.PI / 2, 0.002, (int)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside, 0,
                            (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0.5, 0, 0,
                            (int)swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp, null) as IFeature
                        : doc.FeatureManager.InsertSheetMetalEdgeFlange2(spigoli, schizzi, opzioni,
                            Math.PI / 2, 0.002, (int)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside, lunghezza,
                            (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0.5, 0, 0,
                            (int)swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp, null) as IFeature;
                }
                catch (Exception ex) { Console.WriteLine("      tentativo " + t + " alza: " + ex.Message); }
                Console.WriteLine("      tentativo " + t + " (" + (t == 1 ? "niente selezionato, schizzi null"
                    : t == 2 ? "spigolo selezionato, schizzi null" : t == 3 ? "spigolo selezionato, schizzi [null]"
                    : t == 4 ? "InsertSheetMetalEdgeFlange, la vecchia"
                    : t == 5 ? "spigolo selezionato, schizzi null, LUNGA 20"
                    : t == 6 ? "spigolo selezionato, schizzi [null], LUNGA 20"
                    : t == 7 ? "Edge[], schizzi null, LUNGA 20"
                    : "Edge[], Feature[]{null}, LUNGA 20") + "): " + (flangia == null ? "null" : flangia.Name));
            }
            // TENTATIVO 9: lo SCHIZZO DI PROFILO, come dice il TODO. Un piano
            // perpendicolare allo spigolo nel suo vertice d'inizio (marchi 0 e
            // 1, vincoli perpendicolare + coincidente), e sul piano una linea di
            // 20 mm che parte dal vertice verso +Y: la direzione della flangia.
            if (flangia == null && !testa)
            {
                var v0 = (scelto.GetStartVertex() as IVertex);
                var p0 = v0?.GetPoint() as double[];
                var selMgr = (ISelectionMgr)doc.SelectionManager;
                doc.ClearSelection2(true);
                var sd0 = (SelectData)selMgr.CreateSelectData(); sd0.Mark = 0;
                var sd1 = (SelectData)selMgr.CreateSelectData(); sd1.Mark = 1;
                bool sel0 = (scelto as IEntity).Select4(false, sd0);
                bool sel1 = (v0 as IEntity).Select4(true, sd1);
                var piano = doc.FeatureManager.InsertRefPlane(
                    (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Perpendicular, 0,
                    (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Coincident, 0, 0, 0) as IFeature;
                Console.WriteLine("      tentativo 9: selezione " + sel0 + "/" + sel1 + ", piano " + (piano == null ? "NON NATO" : piano.Name) +
                                  ", vertice " + (p0 == null ? "?" : string.Join(" ", Array.ConvertAll(p0, x => (x * 1000).ToString("0.#")))));
                if (piano != null && p0 != null)
                {
                    doc.ClearSelection2(true);
                    piano.Select2(false, 0);
                    doc.SketchManager.InsertSketch(true);
                    var sk = doc.SketchManager.ActiveSketch as ISketch;
                    var mu = (IMathUtility)sw.GetMathUtility();
                    var tr = sk.ModelToSketchTransform;
                    Func<double, double, double, double[]> nelloSchizzo = (x, y, z) =>
                        ((IMathPoint)((IMathPoint)mu.CreatePoint(new[] { x, y, z })).MultiplyTransform(tr)).ArrayData as double[];
                    var a = nelloSchizzo(p0[0], p0[1], p0[2]);
                    var b = nelloSchizzo(p0[0], p0[1] + 0.020, p0[2]);
                    doc.SketchManager.AddToDB = true;
                    var linea = doc.SketchManager.CreateLine(a[0], a[1], 0, b[0], b[1], 0);
                    doc.SketchManager.AddToDB = false;
                    doc.SketchManager.InsertSketch(true);
                    IFeature schizzo = null;
                    var fs = doc.FirstFeature() as IFeature;
                    while (fs != null) { if (fs.GetTypeName2() == "ProfileFeature") schizzo = fs; fs = fs.GetNextFeature() as IFeature; }
                    Console.WriteLine("      tentativo 9: linea " + (linea != null) + ", schizzo " + (schizzo == null ? "?" : schizzo.Name));
                    doc.ClearSelection2(true);
                    (scelto as IEntity).Select4(false, null);
                    try
                    {
                        flangia = doc.FeatureManager.InsertSheetMetalEdgeFlange2(new Edge[] { (Edge)scelto },
                            new Feature[] { (Feature)schizzo }, opzioni,
                            Math.PI / 2, 0.002, (int)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside, 0.020,
                            (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0.5, 0, 0,
                            (int)swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp, null) as IFeature;
                    }
                    catch (Exception ex) { Console.WriteLine("      tentativo 9 alza: " + ex.Message); }
                    Console.WriteLine("      tentativo 9 (Edge[], Feature[]{schizzo di profilo}, LUNGA 20): " + (flangia == null ? "null" : flangia.Name));
                }
            }

            // TENTATIVO 10 (26/9/2026). Il 9 fa nascere una «Bordo-Flangia» che
            // non aggiunge materiale, e SOLIDWORKS sostituisce lo schizzo con uno
            // suo: la linea su un piano perpendicolare non e' un profilo. Nella
            // finestra il profilo della flangia a spigolo e' un RETTANGOLO sul
            // piano della flangia, col lato di base sullo spigolo. Qui: la faccia
            // di testa del lato da 50 (x = 50, y da -2 a 0) come piano di schizzo,
            // e il rettangolo da (y 0, z 0) a (y 20, z 40).
            if (flangia == null && testa)
            {
                doc.ClearSelection2(true);
                bool faccia = doc.Extension.SelectByID2("", "FACE", 0.050, -0.001, 0.020, false, 0, null, 0);
                doc.SketchManager.InsertSketch(true);
                var sk = doc.SketchManager.ActiveSketch as ISketch;
                Console.WriteLine("      tentativo 10: faccia di testa " + faccia + ", schizzo " + (sk != null));
                if (sk != null)
                {
                    var mu = (IMathUtility)sw.GetMathUtility();
                    var tr = sk.ModelToSketchTransform;
                    Func<double, double, double, double[]> nelloSchizzo = (x, y, z) =>
                        ((IMathPoint)((IMathPoint)mu.CreatePoint(new[] { x, y, z })).MultiplyTransform(tr)).ArrayData as double[];
                    var a = nelloSchizzo(0.050, 0, 0);
                    var b = nelloSchizzo(0.050, alta, 0.040);
                    doc.SketchManager.AddToDB = true;
                    var rett = doc.SketchManager.CreateCornerRectangle(a[0], a[1], 0, b[0], b[1], 0) as object[];
                    doc.SketchManager.AddToDB = false;
                    doc.SketchManager.InsertSketch(true);
                    IFeature schizzo = null;
                    var fs = doc.FirstFeature() as IFeature;
                    while (fs != null) { if (fs.GetTypeName2() == "ProfileFeature") schizzo = fs; fs = fs.GetNextFeature() as IFeature; }
                    Console.WriteLine("      tentativo 10: rettangolo " + (rett == null ? "null" : rett.Length + " linee") +
                                      ", schizzo " + (schizzo == null ? "?" : schizzo.Name));
                    doc.ClearSelection2(true);
                    try
                    {
                        flangia = doc.FeatureManager.InsertSheetMetalEdgeFlange2(new Edge[] { (Edge)scelto },
                            new Feature[] { (Feature)schizzo }, opzioni,
                            Math.PI / 2, 0.002, (int)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside, 0.020,
                            (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0.5, 0, 0,
                            (int)swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp, null) as IFeature;
                    }
                    catch (Exception ex) { Console.WriteLine("      tentativo 10 alza: " + ex.Message); }
                    Console.WriteLine("      tentativo 10 (rettangolo sulla testa): " + (flangia == null ? "null" : flangia.Name));
                }
            }

            doc.EditRebuild3();
            // L'ALBERO, dopo: i tentativi falliti hanno lasciato qualcosa? La
            // flangia nata e' in errore? E il volume, che dice se la flangia ha
            // aggiunto materiale davvero.
            Console.WriteLine("      albero:");
            var fa = doc.FirstFeature() as IFeature;
            while (fa != null)
            {
                bool avviso; int codice = 0;
                try { codice = fa.GetErrorCode2(out avviso); } catch { avviso = false; }
                string tipo = fa.GetTypeName2();
                if (tipo != "RefPlane" && tipo != "OriginProfileFeature" && !tipo.EndsWith("Folder") &&
                    tipo != "MaterialFolder" && tipo != "DetailCabinet" && tipo != "SensorFolder" &&
                    tipo != "HistoryFolder" && tipo != "SelectionSetFolder" && tipo != "EqnFolder")
                    Console.WriteLine("        " + fa.Name + " [" + tipo + "]" + (codice != 0 ? " ERRORE " + codice + (avviso ? " (avviso)" : "") : ""));
                fa = fa.GetNextFeature() as IFeature;
            }
            var corpiDopo = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            var massa = corpiDopo == null ? null : (corpiDopo[0] as IBody2).GetMassProperties(1) as double[];
            double volume = massa == null ? -1 : massa[3] * 1e9;
            Console.WriteLine("      volume adesso " + volume.ToString("0.##") + " mm3 (la L sola: 80,712 x 2 x 40 = 6457)");
            if (flangia == null) { Console.WriteLine("  nessuno dei tentativi fa nascere la flangia"); return 5; }
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

            // IL CONTO, misurato il 26/9/2026 (tentativo 10). La lunghezza che la
            // flangia dichiara (OffsetDistance, allo spigolo virtuale esterno) e'
            // l'altezza del profilo piu' lo spessore; con la posizione «materiale
            // dentro» il lato da 50 perde i suoi R + T in fondo. Lo sviluppo
            // cresce di (Lo - R - T) + BA - (R + T). ModifyDefinition su
            // OffsetDistance risponde vero e non cambia niente: la lunghezza la
            // comanda il profilo.
            double ba = Math.PI / 2 * (2 + 0.5 * 2);
            double lo = alta * 1000 + 2;
            Console.WriteLine();
            Console.WriteLine("  lunghezza attesa allo spigolo virtuale esterno: " + lo.ToString("0.###") + " mm (profilo + T)");
            Console.WriteLine("  sviluppo atteso: +" + (lo - 2 - 2 + ba - 2 - 2).ToString("0.###") + " mm");
            Console.WriteLine("  misurato:        +" + (s1 - s0).ToString("0.###") + " mm");
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
