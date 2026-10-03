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

internal static class Programma
{
    private static int _passo;
    private static bool _orfanaPerSpigolo;
    private static void Dice(string cosa)
    {
        _passo++;
        Console.WriteLine("  " + _passo.ToString("00") + ". " + cosa);
    }

    private static int Main(string[] argomenti)
    {
        bool tieni = Array.IndexOf(argomenti, "--tieni") >= 0;
        Console.WriteLine();
        Console.WriteLine("ORFANA — IsDangling diventa vero? (voce Z-A2)");
        Console.WriteLine("  piastra 100 x 60 x 10, blocco 20 x 20 x 20 sopra; si quota il blocco e lo si sopprime");
        Console.WriteLine();

        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS non risponde: " + ex.Message); return 2; }
        Dice("agganciato a SOLIDWORKS " + sw.RevisionNumber());

        string file = Path.Combine(Path.GetTempPath(), "orfana-sonda.SLDPRT");
        IModelDoc2 pezzo = null, tavolaDoc = null;
        bool chiedevaIlValore = true, spenta = false;
        try
        {
            // ── IL PEZZO, salvato: una vista di tavola vuole un file ─────
            string modello = sw.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            pezzo = (IModelDoc2)sw.NewDocument(modello, 0, 0, 0);
            if (pezzo == null) { Console.WriteLine("  NewDocument(parte) -> null"); return 4; }

            if (Estrudi(pezzo, 0, 0, 0.100, 0.060, 0.010) == null)
            { Console.WriteLine("  la piastra non e' venuta"); return 4; }
            var blocco = Estrudi(pezzo, 0.040, 0.020, 0.060, 0.040, 0.020);
            if (blocco == null) { Console.WriteLine("  il blocco non e' venuto"); return 4; }
            double vol = Volume(pezzo);
            // il blocco parte dallo stesso piano della piastra: sporge di 10
            Dice("pezzo: volume " + vol.ToString("0") + " mm^3 (atteso " + (100 * 60 * 10 + 20 * 20 * 10) +
                 "), blocco «" + blocco.Name + "»");

            try { if (File.Exists(file)) File.Delete(file); } catch { }
            int err = 0, avv = 0;
            if (!pezzo.Extension.SaveAs(file, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref err, ref avv))
            { Console.WriteLine("  SaveAs -> false, errori " + err); return 4; }
            Dice("salvato in " + file);

            // ── LA TAVOLA, con la vista frontale ────────────────────────
            string modTavola = sw.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
            tavolaDoc = (IModelDoc2)sw.NewDocument(modTavola, (int)swDwgPaperSizes_e.swDwgPaperA3size, 0.42, 0.297);
            if (tavolaDoc == null) { Console.WriteLine("  NewDocument(tavola) -> null"); return 4; }
            var tavola = (IDrawingDoc)tavolaDoc;

            IView vista = null;
            string presa = null;
            foreach (var nome in new[] { "*Frontale", "*Anteriore", "*Front" })
            {
                try { vista = tavola.CreateDrawViewFromModelView3(file, nome, 0.15, 0.15, 0); } catch { vista = null; }
                if (vista != null) { presa = nome; break; }
            }
            if (vista == null) { Console.WriteLine("  nessuna vista frontale creata"); return 4; }
            tavolaDoc.EditRebuild3();
            Dice("vista «" + presa + "» -> «" + vista.GetName2() + "»");

            // ── LO SPIGOLO ALTO DEL BLOCCO ──────────────────────────────
            var spigoli = vista.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
            int n = spigoli == null ? 0 : spigoli.Length;
            IEntity scelto = null;
            string descr = null;
            for (int i = 0; i < n && scelto == null; i++)
            {
                var e = spigoli[i] as IEdge;
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
                    scelto = spigoli[i] as IEntity;
                    descr = "spigolo " + i + " di " + n + ": lungo X, 20 mm, a Y 20 e Z " + (a[2] * 1000).ToString("0.###");
                }
            }
            if (scelto == null)
            {
                Console.WriteLine("  fra i " + n + " spigoli visibili non c'e' quello alto del blocco");
                return 4;
            }
            Dice(descr);

            // ── LA QUOTA, come la crea drawing_dimension ──────────────────
            chiedevaIlValore = sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
            sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            spenta = true;

            var contorno = vista.GetOutline() as double[];
            int quotePrima = Quote(tavola);
            tavolaDoc.ClearSelection2(true);
            if (!scelto.Select4(false, null)) { Console.WriteLine("  lo spigolo non si seleziona"); return 4; }
            var nata = tavolaDoc.Extension.AddDimension(
                (contorno[0] + contorno[2]) / 2, contorno[3] + 0.012, 0, 0) as IDisplayDimension;
            tavolaDoc.ClearSelection2(true);
            tavolaDoc.EditRebuild3();
            int quoteDopo = Quote(tavola);
            if (quoteDopo <= quotePrima) { Console.WriteLine("  AddDimension non ha fatto nascere la quota"); return 4; }
            double valore = double.NaN;
            try { valore = nata.GetDimension2(0).SystemValue * 1000; } catch { }
            Dice("quota nata: " + quotePrima + " -> " + quoteDopo + " quote in tavola, valore " + valore.ToString("0.###") + " mm");

            // ── VERDE: prima di sopprimere ──────────────────────────────
            var primaStato = Leggi(tavola);
            Dice("PRIMA  — " + primaStato.Riassunto);
            // Il verso verde del rivelatore indipendente: prima di sopprimere,
            // lo spigolo attaccato DEVE essere fra i visibili.
            int vistiPrima = 0;
            try
            {
                var ents0 = (nata.GetAnnotation() as IAnnotation)?.GetAttachedEntities3() as object[];
                var vis0 = vista.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
                foreach (var e in ents0 ?? new object[0])
                    foreach (var v in vis0 ?? new object[0])
                        if (sw.IsSame(e, v) == (int)swObjectEquality.swObjectSame) { vistiPrima++; break; }
            }
            catch (Exception ex) { Console.WriteLine("      confronto degli attaccati, prima: " + ex.Message); }
            Dice("rivelatore indipendente, PRIMA: attaccati ancora fra i visibili " + vistiPrima + " (atteso 1)");
            if (vistiPrima != 1) { Console.WriteLine("  il rivelatore non vede nemmeno lo spigolo giusto: non misura niente"); return 7; }

            // ── IL ROSSO: si sopprime il blocco, e si ricostruisce ──────
            bool soppresso = blocco.SetSuppression2(
                (int)swFeatureSuppressionAction_e.swSuppressFeature,
                (int)swInConfigurationOpts_e.swThisConfiguration, null);
            pezzo.EditRebuild3();
            double volDopo = Volume(pezzo);
            Dice("blocco soppresso: " + soppresso + ", volume del pezzo " + volDopo.ToString("0") + " mm^3 (atteso 60000)");
            tavolaDoc.ForceRebuild3(false);
            tavolaDoc.EditRebuild3();

            // LA VISTA SI E' AGGIORNATA? Senza questo, «IsDangling falso»
            // potrebbe voler dire solo che la tavola mostra ancora il blocco.
            // Misurato il primo giro (24/9 sera): falso, e non si sapeva perche'.
            int spigoliPrima = n;
            var sp1 = vista.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
            var dopoStato = Leggi(tavola);
            Dice("DOPO   — spigoli visibili " + spigoliPrima + " -> " + (sp1 == null ? 0 : sp1.Length) + "; " + dopoStato.Riassunto);

            // Secondo tentativo: la tavola DAVANTI e ricostruita tutta.
            int errAtt = 0;
            sw.ActivateDoc3(tavolaDoc.GetTitle(), false, (int)swRebuildOnActivation_e.swRebuildActiveDoc, ref errAtt);
            tavolaDoc.ForceRebuild3(false);
            var sp2 = vista.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
            var dopo2 = Leggi(tavola);
            Dice("DOPO, tavola davanti e ricostruita — spigoli visibili " + (sp2 == null ? 0 : sp2.Length) + "; " + dopo2.Riassunto);
            if (dopo2.Orfane > dopoStato.Orfane) dopoStato = dopo2;

            // UN RIVELATORE CHE NON CHIEDE A SOLIDWORKS SE E' ORFANA: lo
            // spigolo a cui la quota e' attaccata e' ancora fra quelli che la
            // vista mostra? Se IsDangling e' morto, questo e' quello che resta.
            int attaccatiVisti = 0, attaccati = 0;
            string valoreDopo = "-";
            try { valoreDopo = (nata.GetDimension2(0).SystemValue * 1000).ToString("0.###") + " mm"; } catch { }
            try
            {
                var ents = (nata.GetAnnotation() as IAnnotation)?.GetAttachedEntities3() as object[];
                foreach (var e in ents ?? new object[0])
                {
                    attaccati++;
                    foreach (var v in sp2 ?? new object[0])
                        if (sw.IsSame(e, v) == (int)swObjectEquality.swObjectSame) { attaccatiVisti++; break; }
                }
            }
            catch (Exception ex) { Console.WriteLine("      confronto degli attaccati: " + ex.Message); }
            // E il verso verde dello stesso rivelatore, sul PRIMA: lo spigolo
            // scelto era fra i visibili per costruzione.
            Dice("rivelatore indipendente: attaccati " + attaccati + ", di cui ancora fra i visibili " + attaccatiVisti +
                 "; valore della quota adesso " + valoreDopo);
            _orfanaPerSpigolo = attaccati > 0 && attaccatiVisti == 0;

            Console.WriteLine();
            if (Math.Abs(volDopo - 60000) > 1)
            {
                Console.WriteLine("Il blocco non risulta soppresso (volume " + volDopo.ToString("0") + "): la prova non ha costruito il rosso.");
                return 5;
            }
            if (primaStato.Orfane != 0)
            {
                Console.WriteLine("GIA' ORFANA PRIMA di sopprimere: " + primaStato.Orfane + ". Il verde non regge, il campo non distingue.");
                return 7;
            }
            if (dopoStato.Orfane > 0)
            {
                Console.WriteLine("REGGE: IsDangling 0 -> " + dopoStato.Orfane + " quando lo spigolo sparisce. Z-A2 ha la sua misura.");
                return 0;
            }
            Console.WriteLine("IsDangling resta FALSE anche su una quota senza spigolo: il campo e' morto per le quote in tavola.");
            Console.WriteLine("Entita' agganciate: " + primaStato.Agganciate + " prima, " + dopoStato.Agganciate + " dopo.");
            if (_orfanaPerSpigolo)
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
            if (spenta)
                try { sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, chiedevaIlValore); } catch { }
            if (!tieni)
            {
                if (tavolaDoc != null) try { sw.CloseDoc(tavolaDoc.GetTitle()); } catch { }
                if (pezzo != null) try { sw.CloseDoc(pezzo.GetTitle()); } catch { }
                try { if (File.Exists(file)) File.Delete(file); } catch { }
                Dice("tavola e pezzo chiusi senza salvare, file tolto");
            }
        }
    }

    private sealed class Stato
    {
        public int Annotazioni, Orfane, Agganciate;
        public string Riassunto;
    }

    private static Stato Leggi(IDrawingDoc tavola)
    {
        var s = new Stato();
        var righe = new List<string>();
        var v = tavola.GetFirstView() as IView;
        while (v != null)
        {
            var ann = v.GetAnnotations() as object[];
            if (ann != null)
                foreach (var o in ann)
                {
                    var a = o as IAnnotation;
                    if (a == null) continue;
                    s.Annotazioni++;
                    bool orfana = false;
                    try { orfana = a.IsDangling(); } catch { }
                    if (orfana) s.Orfane++;
                    int agg = 0;
                    try { var ents = a.GetAttachedEntities3() as object[]; agg = ents == null ? 0 : ents.Length; } catch { }
                    s.Agganciate += agg;
                    if (a.GetType() == (int)swAnnotationType_e.swDisplayDimension)
                        righe.Add("«" + a.GetName() + "» orfana " + orfana + ", agganciate " + agg);
                }
            v = v.GetNextView() as IView;
        }
        s.Riassunto = s.Annotazioni + " annotazioni, " + s.Orfane + " orfane, " + s.Agganciate +
                      " entita' agganciate" + (righe.Count == 0 ? "" : " [" + string.Join("; ", righe) + "]");
        return s;
    }

    private static int Quote(IDrawingDoc tavola)
    {
        int n = 0;
        var v = tavola.GetFirstView() as IView;
        while (v != null)
        {
            var d = v.GetDisplayDimensions() as object[];
            if (d != null) n += d.Length;
            v = v.GetNextView() as IView;
        }
        return n;
    }

    private static IFeature Estrudi(IModelDoc2 doc, double x1, double y1, double x2, double y2, double alto)
    {
        doc.ClearSelection2(true);
        if (!SelectBasePlane(doc, 2)) return null;
        doc.SketchManager.InsertSketch(true);
        doc.SketchManager.CreateCornerRectangle(x1, y1, 0, x2, y2, 0);
        doc.SketchManager.InsertSketch(true);
        return doc.FeatureManager.FeatureExtrusion3(
            true, false, false, 0, 0, alto, 0.0,
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
