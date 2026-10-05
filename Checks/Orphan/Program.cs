using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  Orfana — `IAnnotation.IsDangling` diventa vero, o e' un campo morto? (Z-A2)
// =============================================================================
//
//  Il 18/9 `IsDangling` rispondeva `false` su 67 annotazioni su 67 — ma erano
//  tutte note libere, che non possono diventare orfane: un campo che e' falso
//  mentre non c'e' niente da guardare non e' ancora una misura. Da allora le
//  quote in tavola si CREANO (`drawing_dimension`, K10), e il rosso si puo'
//  costruire.
//
//  IL PEZZO: una piastra 100 x 60 x 10 e, sopra, un blocco 20 x 20 alto 20,
//  come DUE estrusioni. In tavola, vista frontale, si quota lo spigolo alto del
//  blocco (lungo 20). Poi nel pezzo si SOPPRIME il blocco: lo spigolo non c'e'
//  piu', la quota resta appesa al niente.
//
//  I DUE VERSI, nella stessa corsa:
//    verde  — prima di sopprimere, la quota NON e' orfana;
//    rosso  — dopo, lo e'.
//  Se dopo continua a dire `false`, il campo e' morto e lo si scrive; e allora
//  si guarda se `GetAttachedEntities3` dice qualcosa che `IsDangling` tace.
//
//  La quota si crea come la crea `drawing_dimension`: si seleziona lo spigolo che
//  la vista mostra e si chiama `AddDimension` in coordinate del FOGLIO, con
//  `swInputDimValOnCreate` SPENTO (acceso, apre un modale e la chiamata COM non
//  torna piu').
//
//  USO
//      .\Checks\Orphan\bin\Orphan.exe            [--tieni]
// =============================================================================

internal static class Program
{
    private static int _step;
    private static bool _orphanPerEdge;
    private static void Says(string what)
    {
        _step++;
        Console.WriteLine("  " + _step.ToString("00") + ". " + what);
    }

    private static int Main(string[] arguments)
    {
        bool keep = Array.IndexOf(arguments, "--tieni") >= 0;
        Console.WriteLine();
        Console.WriteLine("ORFANA — IsDangling diventa vero? (voce Z-A2)");
        Console.WriteLine("  piastra 100 x 60 x 10, blocco 20 x 20 x 20 sopra; si quota il blocco e lo si sopprime");
        Console.WriteLine();

        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS non risponde: " + ex.Message); return 2; }
        Says("agganciato a SOLIDWORKS " + sw.RevisionNumber());

        string file = Path.Combine(Path.GetTempPath(), "orfana-sonda.SLDPRT");
        IModelDoc2 part = null, drawingDoc = null;
        bool askedForTheValue = true, off = false;
        try
        {
            // ── IL PEZZO, salvato: una vista di tavola vuole un file ─────
            string model = sw.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            part = (IModelDoc2)sw.NewDocument(model, 0, 0, 0);
            if (part == null) { Console.WriteLine("  NewDocument(parte) -> null"); return 4; }

            if (Extrude(part, 0, 0, 0.100, 0.060, 0.010) == null)
            { Console.WriteLine("  la piastra non e' venuta"); return 4; }
            var block = Extrude(part, 0.040, 0.020, 0.060, 0.040, 0.020);
            if (block == null) { Console.WriteLine("  il blocco non e' venuto"); return 4; }
            double vol = Volume(part);
            // il blocco parte dallo stesso piano della piastra: sporge di 10
            Says("pezzo: volume " + vol.ToString("0") + " mm^3 (atteso " + (100 * 60 * 10 + 20 * 20 * 10) +
                 "), blocco «" + block.Name + "»");

            try { if (File.Exists(file)) File.Delete(file); } catch { }
            int err = 0, avv = 0;
            if (!part.Extension.SaveAs(file, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref err, ref avv))
            { Console.WriteLine("  SaveAs -> false, errori " + err); return 4; }
            Says("salvato in " + file);

            // ── LA TAVOLA, con la vista frontale ────────────────────────
            string drawingMod = sw.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
            drawingDoc = (IModelDoc2)sw.NewDocument(drawingMod, (int)swDwgPaperSizes_e.swDwgPaperA3size, 0.42, 0.297);
            if (drawingDoc == null) { Console.WriteLine("  NewDocument(tavola) -> null"); return 4; }
            var drawing = (IDrawingDoc)drawingDoc;

            IView view = null;
            string taken = null;
            foreach (var itemName in new[] { "*Frontale", "*Anteriore", "*Front" })
            {
                try { view = drawing.CreateDrawViewFromModelView3(file, itemName, 0.15, 0.15, 0); } catch { view = null; }
                if (view != null) { taken = itemName; break; }
            }
            if (view == null) { Console.WriteLine("  nessuna vista frontale creata"); return 4; }
            drawingDoc.EditRebuild3();
            Says("vista «" + taken + "» -> «" + view.GetName2() + "»");

            // ── LO SPIGOLO ALTO DEL BLOCCO ──────────────────────────────
            var edges = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
            int n = edges == null ? 0 : edges.Length;
            IEntity chosen = null;
            string descr = null;
            for (int i = 0; i < n && chosen == null; i++)
            {
                var e = edges[i] as IEdge;
                if (e == null) continue;
                var a = (e.GetStartVertex() as IVertex)?.GetPoint() as double[];
                var b = (e.GetEndVertex() as IVertex)?.GetPoint() as double[];
                if (a == null || b == null) continue;
                // il blocco sta da Y 0 a 0,020 (estruso dal piano superiore):
                // lo spigolo alto ha le due Y a 0,020 e corre lungo X per 20
                // mm. Quelli lungo Z, nella frontale, sono punti.
                if (Math.Abs(a[1] - 0.020) < 1e-6 && Math.Abs(b[1] - 0.020) < 1e-6 &&
                    Math.Abs(Math.Abs(b[0] - a[0]) - 0.020) < 1e-6 && Math.Abs(b[2] - a[2]) < 1e-6)
                {
                    chosen = edges[i] as IEntity;
                    descr = "spigolo " + i + " di " + n + ": lungo X, 20 mm, a Y 20 e Z " + (a[2] * 1000).ToString("0.###");
                }
            }
            if (chosen == null)
            {
                Console.WriteLine("  fra i " + n + " spigoli visibili non c'e' quello alto del blocco");
                return 4;
            }
            Says(descr);

            // ── LA QUOTA, come la crea drawing_dimension ──────────────────
            askedForTheValue = sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
            sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            off = true;

            var contour = view.GetOutline() as double[];
            int dimensionsBefore = Quote(drawing);
            drawingDoc.ClearSelection2(true);
            if (!chosen.Select4(false, null)) { Console.WriteLine("  lo spigolo non si seleziona"); return 4; }
            var born = drawingDoc.Extension.AddDimension(
                (contour[0] + contour[2]) / 2, contour[3] + 0.012, 0, 0) as IDisplayDimension;
            drawingDoc.ClearSelection2(true);
            drawingDoc.EditRebuild3();
            int dimensionsAfter = Quote(drawing);
            if (dimensionsAfter <= dimensionsBefore) { Console.WriteLine("  AddDimension non ha fatto nascere la quota"); return 4; }
            double val = double.NaN;
            try { val = born.GetDimension2(0).SystemValue * 1000; } catch { }
            Says("quota nata: " + dimensionsBefore + " -> " + dimensionsAfter + " quote in tavola, valore " + val.ToString("0.###") + " mm");

            // ── VERDE: prima di sopprimere ──────────────────────────────
            var stateBefore = Read(drawing);
            Says("PRIMA  — " + stateBefore.Summary);
            // Il verso verde del rivelatore indipendente: prima di sopprimere,
            // lo spigolo attaccato DEVE essere fra i visibili.
            int seenBefore = 0;
            try
            {
                var ents0 = (born.GetAnnotation() as IAnnotation)?.GetAttachedEntities3() as object[];
                var vis0 = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
                foreach (var e in ents0 ?? new object[0])
                    foreach (var v in vis0 ?? new object[0])
                        if (sw.IsSame(e, v) == (int)swObjectEquality.swObjectSame) { seenBefore++; break; }
            }
            catch (Exception ex) { Console.WriteLine("      confronto degli attaccati, prima: " + ex.Message); }
            Says("rivelatore indipendente, PRIMA: attaccati ancora fra i visibili " + seenBefore + " (atteso 1)");
            if (seenBefore != 1) { Console.WriteLine("  il rivelatore non vede nemmeno lo spigolo giusto: non misura niente"); return 7; }

            // ── IL ROSSO: si sopprime il blocco, e si ricostruisce ──────
            bool suppressed = block.SetSuppression2(
                (int)swFeatureSuppressionAction_e.swSuppressFeature,
                (int)swInConfigurationOpts_e.swThisConfiguration, null);
            part.EditRebuild3();
            double volAfter = Volume(part);
            Says("blocco soppresso: " + suppressed + ", volume del pezzo " + volAfter.ToString("0") + " mm^3 (atteso 60000)");
            drawingDoc.ForceRebuild3(false);
            drawingDoc.EditRebuild3();

            // LA VISTA SI E' AGGIORNATA? Senza questo, «IsDangling falso»
            // potrebbe voler dire solo che la tavola mostra ancora il blocco.
            // Misurato il primo giro (24/9 sera): falso, e non si sapeva perche'.
            int edgesBefore = n;
            var sp1 = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
            var afterState = Read(drawing);
            Says("DOPO   — spigoli visibili " + edgesBefore + " -> " + (sp1 == null ? 0 : sp1.Length) + "; " + afterState.Summary);

            // Secondo tentativo: la tavola DAVANTI e ricostruita tutta.
            int errAtt = 0;
            sw.ActivateDoc3(drawingDoc.GetTitle(), false, (int)swRebuildOnActivation_e.swRebuildActiveDoc, ref errAtt);
            drawingDoc.ForceRebuild3(false);
            var sp2 = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
            var after2 = Read(drawing);
            Says("DOPO, tavola davanti e ricostruita — spigoli visibili " + (sp2 == null ? 0 : sp2.Length) + "; " + after2.Summary);
            if (after2.Orfane > afterState.Orfane) afterState = after2;

            // UN RIVELATORE CHE NON CHIEDE A SOLIDWORKS SE E' ORFANA: lo
            // spigolo a cui la quota e' attaccata e' ancora fra quelli che la
            // vista mostra? Se IsDangling e' morto, questo e' quello che resta.
            int seenAttached = 0, attached = 0;
            string valueAfter = "-";
            try { valueAfter = (born.GetDimension2(0).SystemValue * 1000).ToString("0.###") + " mm"; } catch { }
            try
            {
                var ents = (born.GetAnnotation() as IAnnotation)?.GetAttachedEntities3() as object[];
                foreach (var e in ents ?? new object[0])
                {
                    attached++;
                    foreach (var v in sp2 ?? new object[0])
                        if (sw.IsSame(e, v) == (int)swObjectEquality.swObjectSame) { seenAttached++; break; }
                }
            }
            catch (Exception ex) { Console.WriteLine("      confronto degli attaccati: " + ex.Message); }
            // E il verso verde dello stesso rivelatore, sul PRIMA: lo spigolo
            // scelto era fra i visibili per costruzione.
            Says("rivelatore indipendente: attaccati " + attached + ", di cui ancora fra i visibili " + seenAttached +
                 "; valore della quota adesso " + valueAfter);
            _orphanPerEdge = attached > 0 && seenAttached == 0;

            Console.WriteLine();
            if (Math.Abs(volAfter - 60000) > 1)
            {
                Console.WriteLine("Il blocco non risulta soppresso (volume " + volAfter.ToString("0") + "): la prova non ha costruito il rosso.");
                return 5;
            }
            if (stateBefore.Orfane != 0)
            {
                Console.WriteLine("GIA' ORFANA PRIMA di sopprimere: " + stateBefore.Orfane + ". Il verde non regge, il campo non distingue.");
                return 7;
            }
            if (afterState.Orfane > 0)
            {
                Console.WriteLine("REGGE: IsDangling 0 -> " + afterState.Orfane + " quando lo spigolo sparisce. Z-A2 ha la sua misura.");
                return 0;
            }
            Console.WriteLine("IsDangling resta FALSE anche su una quota senza spigolo: il campo e' morto per le quote in tavola.");
            Console.WriteLine("Entita' agganciate: " + stateBefore.Attached + " prima, " + afterState.Attached + " dopo.");
            if (_orphanPerEdge)
            {
                Console.WriteLine("MA IL RIVELATORE INDIPENDENTE LA VEDE: lo spigolo a cui e' attaccata non e' piu' fra quelli");
                Console.WriteLine("che la vista mostra. Z-A2 si costruisce su questo, non su IsDangling.");
                return 3;
            }
            return 6;
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
            if (off)
                try { sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, askedForTheValue); } catch { }
            if (!keep)
            {
                if (drawingDoc != null) try { sw.CloseDoc(drawingDoc.GetTitle()); } catch { }
                if (part != null) try { sw.CloseDoc(part.GetTitle()); } catch { }
                try { if (File.Exists(file)) File.Delete(file); } catch { }
                Says("tavola e pezzo chiusi senza salvare, file tolto");
            }
        }
    }

    private sealed class State
    {
        public int Annotations, Orfane, Attached;
        public string Summary;
    }

    private static State Read(IDrawingDoc drawing)
    {
        var s = new State();
        var lines = new List<string>();
        var v = drawing.GetFirstView() as IView;
        while (v != null)
        {
            var ann = v.GetAnnotations() as object[];
            if (ann != null)
                foreach (var o in ann)
                {
                    var a = o as IAnnotation;
                    if (a == null) continue;
                    s.Annotations++;
                    bool orphan = false;
                    try { orphan = a.IsDangling(); } catch { }
                    if (orphan) s.Orfane++;
                    int agg = 0;
                    try { var ents = a.GetAttachedEntities3() as object[]; agg = ents == null ? 0 : ents.Length; } catch { }
                    s.Attached += agg;
                    if (a.GetType() == (int)swAnnotationType_e.swDisplayDimension)
                        lines.Add("«" + a.GetName() + "» orfana " + orphan + ", agganciate " + agg);
                }
            v = v.GetNextView() as IView;
        }
        s.Summary = s.Annotations + " annotazioni, " + s.Orfane + " orfane, " + s.Attached +
                      " entita' agganciate" + (lines.Count == 0 ? "" : " [" + string.Join("; ", lines) + "]");
        return s;
    }

    private static int Quote(IDrawingDoc drawing)
    {
        int n = 0;
        var v = drawing.GetFirstView() as IView;
        while (v != null)
        {
            var d = v.GetDisplayDimensions() as object[];
            if (d != null) n += d.Length;
            v = v.GetNextView() as IView;
        }
        return n;
    }

    private static IFeature Extrude(IModelDoc2 doc, double x1, double y1, double x2, double y2, double up)
    {
        doc.ClearSelection2(true);
        if (!SelectBasePlane(doc, 2)) return null;
        doc.SketchManager.InsertSketch(true);
        doc.SketchManager.CreateCornerRectangle(x1, y1, 0, x2, y2, 0);
        doc.SketchManager.InsertSketch(true);
        return doc.FeatureManager.FeatureExtrusion3(
            true, false, false, 0, 0, up, 0.0,
            false, false, false, false, 0, 0, false, false, false, false,
            true, true, true, 0, 0, false) as IFeature;
    }

    private static double Volume(IModelDoc2 doc)
    {
        try { return doc.Extension.CreateMassProperty().Volume * 1e9; }
        catch { return double.NaN; }
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
