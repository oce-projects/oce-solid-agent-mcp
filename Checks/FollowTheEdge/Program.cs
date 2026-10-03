using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  SeguiIlBordo — un foro quotato contro uno SPIGOLO lo segue? (voce E18, a e b)
// =============================================================================
//
//  Tre dei quattro silenzi di `--regressione` sono coordinate dentro uno
//  schizzo che non seguono il bordo quando il pezzo cambia (K5). La cura che la
//  voce propone si regge su DUE fatti che nessuno ha misurato fuori processo:
//
//    (a) una quota fra il centro di un cerchio dello schizzo e uno spigolo del
//        MODELLO si crea da API, resta guidante, e trascina il foro quando lo
//        spigolo si sposta;
//    (b) un punto messo a meta' di una linea di costruzione, i cui estremi sono
//        a meta' di due spigoli opposti, resta al CENTRO della faccia quando la
//        piastra cresce da una parte sola.
//
//  IL PEZZO: una piastra 200 x 120 x 20 dal piano frontale, con le due quote
//  del rettangolo messe qui (cosi' si possono cambiare), e in UNO schizzo sulla
//  faccia davanti tre fori da 10 passanti:
//    A — a 20 dallo spigolo destro e a 20 da quello alto   (quote allo spigolo)
//    B — al centro, sulla mezzeria di una linea fra i due fianchi (relazioni)
//    C — a coordinate assolute (-60, -30), niente relazioni (il GEMELLO: il
//        vecchio modo deve restare dov'e')
//
//  Poi la lunghezza 200 -> 260 e l'altezza 120 -> 180, e dopo ognuna si
//  rimisura: A a (xmax-20, ymax-20), B al centro dell'ingombro, C fermo. Il
//  pezzo cresce dalla parte che decide SOLIDWORKS (il rettangolo non e'
//  ancorato): il criterio e' scritto rispetto all'ingombro, quindi vale da
//  qualunque parte cresca.
//
//  E dopo ogni ricostruzione: nessuna lavorazione in errore. Uno schizzo
//  sovravincolato non da' errore in `Insert*`: da' una lavorazione rossa dopo.
//
//  USO
//      .\Checks\FollowTheEdge\bin\FollowTheEdge.exe            [--tieni]
// =============================================================================

internal static class Programma
{
    private static int _passo;
    private static void Dice(string cosa)
    {
        _passo++;
        Console.WriteLine("  " + _passo.ToString("00") + ". " + cosa);
    }

    private static ISldWorks _sw;
    private static string _variante = "bordi";
    private static ISketch _schizzoTaglio;
    private static bool _elencaRelazioni = true;
    private static IModelDoc2 _doc;
    private static IMathUtility _mu;

    private static int Main(string[] argomenti)
    {
        bool tieni = Array.IndexOf(argomenti, "--tieni") >= 0;
        if (Array.IndexOf(argomenti, "--b=convertiti") >= 0) _variante = "convertiti";
        Console.WriteLine();
        Console.WriteLine("SEGUI IL BORDO — un foro quotato contro lo spigolo lo segue? (voce E18, variante B: " + _variante + ")");
        Console.WriteLine();

        try { _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS non risponde: " + ex.Message); return 2; }
        Dice("agganciato a SOLIDWORKS " + _sw.RevisionNumber());
        _mu = (IMathUtility)_sw.GetMathUtility();

        bool chiedeva = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
        _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
        try
        {
            string modello = _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            _doc = (IModelDoc2)_sw.NewDocument(modello, 0, 0, 0);
            if (_doc == null) { Console.WriteLine("  NewDocument -> null"); return 4; }

            // ── LA PIASTRA, con le sue due quote ────────────────────────
            _doc.ClearSelection2(true);
            if (!SelectBasePlane(_doc, 1))
            { Console.WriteLine("  non trovo «Piano frontale»"); return 4; }
            _doc.SketchManager.InsertSketch(true);
            var lati = _doc.SketchManager.CreateCornerRectangle(-0.100, -0.060, 0, 0.100, 0.060, 0) as object[];
            ISketchSegment alto = null, destro = null;
            foreach (var o in lati ?? new object[0])
            {
                var l = o as ISketchLine;
                if (l == null) continue;
                var a = (ISketchPoint)l.GetStartPoint2(); var b = (ISketchPoint)l.GetEndPoint2();
                if (Math.Abs(a.Y - 0.060) < 1e-9 && Math.Abs(b.Y - 0.060) < 1e-9) alto = (ISketchSegment)l;
                if (Math.Abs(a.X - 0.100) < 1e-9 && Math.Abs(b.X - 0.100) < 1e-9) destro = (ISketchSegment)l;
            }
            if (alto == null || destro == null) { Console.WriteLine("  non ritrovo i lati del rettangolo"); return 4; }
            _doc.ClearSelection2(true); alto.Select4(false, null);
            var qLung = (_doc.AddDimension2(0, 0.090, 0) as IDisplayDimension)?.GetDimension2(0);
            _doc.ClearSelection2(true); destro.Select4(false, null);
            var qAlt = (_doc.AddDimension2(0.130, 0, 0) as IDisplayDimension)?.GetDimension2(0);
            _doc.ClearSelection2(true);
            _doc.SketchManager.InsertSketch(true);
            if (qLung == null || qAlt == null) { Console.WriteLine("  le quote del rettangolo non sono nate"); return 4; }
            var piastra = _doc.FeatureManager.FeatureExtrusion3(
                true, false, false, 0, 0, 0.020, 0.0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, 0, 0, false) as IFeature;
            if (piastra == null) { Console.WriteLine("  la piastra non e' venuta"); return 4; }
            var box = Ingombro();
            Dice("piastra: ingombro " + Box(box) + ", quote " + Mm(qLung.SystemValue) + " e " + Mm(qAlt.SystemValue));
            double zDavanti = Math.Abs(box[2]) > Math.Abs(box[5]) ? box[2] : box[5];

            // ── LO SCHIZZO SULLA FACCIA DAVANTI ─────────────────────────
            _doc.ClearSelection2(true);
            if (!_doc.Extension.SelectByID2("", "FACE", 0.03, 0.01, zDavanti, false, 0, null, 0))
            { Console.WriteLine("  non seleziono la faccia davanti a Z " + Mm(zDavanti)); return 4; }
            _doc.SketchManager.InsertSketch(true);
            var schizzo = _doc.SketchManager.ActiveSketch;
            var tr = schizzo.ModelToSketchTransform;

            // MISURATO il primo giro (24/9 sera): la piastra cresce a SINISTRA e
            // in BASSO (X -160..100, Y -120..60), e l'angolo in alto a destra
            // resta fermo. Un foro quotato solo all'angolo che non si muove
            // passa senza provare niente. Quindi due fori quotati, uno per
            // angolo, e ognuno si giudica contro i SUOI spigoli: da qualunque
            // parte cresca, uno dei due deve muoversi davvero.
            //
            // I FORI SI RICONOSCONO DAL DIAMETRO: A 6, D 8, B 10, C 12. Il primo
            // giro li riconosceva dalla vicinanza al punto atteso, e un foro
            // rimasto fermo poteva essere scambiato per un altro.

            // A: in basso a sinistra, a 20 dallo spigolo sinistro e da quello basso
            var cA = Cerchio(tr, -0.080, -0.040, zDavanti, 0.003);
            var pA = (ISketchPoint)((ISketchArc)cA).GetCenterPoint2();
            var qA1 = QuotaAlloSpigolo(pA, -0.100, 0.0, zDavanti, -0.115, -0.045, zDavanti);
            var qA2 = QuotaAlloSpigolo(pA, 0.0, -0.060, zDavanti, -0.085, -0.075, zDavanti);
            Dice("A (Ø6): quote allo spigolo " + (qA1 == null ? "NON NATA" : Mm(qA1.SystemValue)) + " (sinistro), " +
                 (qA2 == null ? "NON NATA" : Mm(qA2.SystemValue)) + " (basso)");

            // D: in alto a destra, a 20 dallo spigolo destro e da quello alto
            var cD = Cerchio(tr, 0.080, 0.040, zDavanti, 0.004);
            var pD = (ISketchPoint)((ISketchArc)cD).GetCenterPoint2();
            var qD1 = QuotaAlloSpigolo(pD, 0.100, 0.0, zDavanti, 0.115, 0.045, zDavanti);
            var qD2 = QuotaAlloSpigolo(pD, 0.0, 0.060, zDavanti, 0.085, 0.075, zDavanti);
            Dice("D (Ø8): quote allo spigolo " + (qD1 == null ? "NON NATA" : Mm(qD1.SystemValue)) + " (destro), " +
                 (qD2 == null ? "NON NATA" : Mm(qD2.SystemValue)) + " (alto)");

            // B: al centro. Due varianti, una per giro (--b=bordi | --b=convertiti).
            var cB = Cerchio(tr, 0.0, 0.0, zDavanti, 0.005);
            var pB = (ISketchPoint)((ISketchArc)cB).GetCenterPoint2();
            ISketchSegment linea = null;
            if (_variante == "convertiti")
            {
                // Gli spigoli sinistro e destro CONVERTITI nello schizzo (seguono
                // il modello da soli) e messi a COSTRUZIONE, se no entrerebbero
                // nel profilo del taglio. La mezzeria si appoggia a loro: sono
                // entita' dello schizzo, non spigoli del modello.
                var sx = Converti(-0.100, 0.0, zDavanti);
                var dx = Converti(0.100, 0.0, zDavanti);
                linea = Mezzeria(tr, zDavanti);

                var ls = linea as ISketchLine;
                bool r1 = sx != null && RelazioneFraSchizzi((ISketchPoint)ls.GetStartPoint2(), sx, "sgATMIDDLE");
                bool r2 = dx != null && RelazioneFraSchizzi((ISketchPoint)ls.GetEndPoint2(), dx, "sgATMIDDLE");
                Dice("B (Ø10), variante convertiti: fianchi convertiti " + (sx != null) + "/" + (dx != null) +
                     ", estremi della mezzeria a meta' " + r1 + "/" + r2);
            }
            else
            {
                linea = Mezzeria(tr, zDavanti);

                var ls = linea as ISketchLine;
                bool rB1 = Relazione((ISketchPoint)ls.GetStartPoint2(), -0.100, 0.0, zDavanti, "sgATMIDDLE");
                bool rB2 = Relazione((ISketchPoint)ls.GetEndPoint2(), 0.100, 0.0, zDavanti, "sgATMIDDLE");
                Dice("B (Ø10), variante bordi: estremi della mezzeria a meta' degli spigoli del modello " + rB1 + "/" + rB2);
            }
            RelazioneFraSchizzi(pB, linea, "sgATMIDDLE");

            // C: il gemello, a coordinate assolute
            Cerchio(tr, -0.040, 0.030, zDavanti, 0.006);
            Dice("C (Ø12): a (-40, 30), senza relazioni");

            _doc.SketchManager.InsertSketch(true);
            var taglio = _doc.FeatureManager.FeatureCut4(
                true, false, false, (int)swEndConditions_e.swEndCondThroughAll, 0, 0, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                false, true, true, true, true, false, 0, 0, false, false) as IFeature;
            if (taglio == null) { Console.WriteLine("  il taglio non e' venuto"); return 4; }
            _doc.EditRebuild3();

            _schizzoTaglio = (taglio.GetFirstSubFeature() as IFeature)?.GetSpecificFeature2() as ISketch;
            int sbagli = 0;
            var primaC = Foro(Fori(), 0.006);
            sbagli += Verifica("dopo il taglio", primaC);

            // ── LA PIASTRA CAMBIA ────────────────────────────────────────
            qLung.SetSystemValue3(0.260, (int)swSetValueInConfiguration_e.swSetValue_InAllConfigurations, null);
            _doc.EditRebuild3();
            int s1 = Verifica("lunghezza 200 -> 260", primaC);
            if (s1 > 0)
            {
                // Una ricostruzione PIENA risolve i riferimenti che quella
                // normale lascia rotti? Se si', e' un problema d'ordine.
                _doc.ForceRebuild3(false);
                s1 = Verifica("lunghezza 200 -> 260, dopo ForceRebuild3", primaC);
            }
            sbagli += s1;

            qAlt.SetSystemValue3(0.180, (int)swSetValueInConfiguration_e.swSetValue_InAllConfigurations, null);
            _doc.EditRebuild3();
            int s2 = Verifica("altezza 120 -> 180", primaC);
            if (s2 > 0)
            {
                _doc.ForceRebuild3(false);
                s2 = Verifica("altezza 120 -> 180, dopo ForceRebuild3", primaC);
            }
            sbagli += s2;

            Console.WriteLine();
            if (sbagli == 0)
            {
                Console.WriteLine("REGGE: A segue l'angolo, B resta al centro, C resta dov'era, e nessuna lavorazione");
                Console.WriteLine("e' in errore. I due meccanismi di E18 (a) e (b) si costruiscono da API.");
                return 0;
            }
            Console.WriteLine(sbagli + " misure fuori posto: guarda sopra quale meccanismo non regge.");
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
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, chiedeva); } catch { }
            if (_doc != null && !tieni)
            {
                try { _sw.CloseDoc(_doc.GetTitle()); Dice("pezzo chiuso senza salvare"); } catch { }
            }
        }
    }

    // ── misure ───────────────────────────────────────────────────────────

    private static int Verifica(string quando, double[] cPrima)
    {
        var box = Ingombro();
        var fori = Fori();
        int errori = 0;
        try { errori = _doc.Extension.GetWhatsWrongCount(); } catch { }
        Console.WriteLine();
        Console.WriteLine("      " + quando + ": ingombro " + Box(box) + ", fori " + fori.Count + ", cose che non vanno " + errori);
        if (errori > 0)
        {
            // QUALE lavorazione, e con che codice: il primo giro diceva solo «1».
            try
            {
                object feats, codici, avvisi;
                _doc.Extension.GetWhatsWrong(out feats, out codici, out avvisi);
                var f = feats as object[]; var c = codici as int[]; var w = avvisi as bool[];
                for (int i = 0; f != null && i < f.Length; i++)
                    Console.WriteLine("        «" + ((IFeature)f[i]).Name + "» codice " + (c != null ? c[i].ToString() : "?") +
                                      (w != null && w[i] ? " (avviso)" : " (ERRORE)"));
            }
            catch (Exception ex) { Console.WriteLine("        GetWhatsWrong: " + ex.Message); }
        }
        int sbagli = errori > 0 ? 1 : 0;
        try
        {
            var rm = _schizzoTaglio?.RelationManager;
            if (rm != null)
            {
                Console.WriteLine("        relazioni dello schizzo del taglio: tutte " + rm.GetRelationsCount(0) +
                                  ", esterne " + rm.GetRelationsCount(3) + ", penzolanti " + rm.GetRelationsCount(1) +
                                  ", rotte " + rm.GetRelationsCount(6) + ", sovravincolanti " + rm.GetRelationsCount(2));
                if (_elencaRelazioni)
                {
                    _elencaRelazioni = false;
                    foreach (var o in (rm.GetRelations(0) as object[]) ?? new object[0])
                    {
                        var r = (ISketchRelation)o;
                        var tipi = r.GetEntitiesType() as int[];
                        Console.WriteLine("          tipo " + r.GetRelationType() + " su " + r.GetEntitiesCount() +
                                          " entita' (" + (tipi == null ? "-" : string.Join(",", tipi)) + ")" +
                                          (r.GetDisplayDimension() != null ? " [quota]" : ""));
                    }
                }
            }
        }
        catch (Exception ex) { Console.WriteLine("        relazioni: " + ex.Message); }

        double xmin = Math.Min(box[0], box[3]), ymin = Math.Min(box[1], box[4]);
        double xmax = Math.Max(box[0], box[3]), ymax = Math.Max(box[1], box[4]);
        double xc = (box[0] + box[3]) / 2, yc = (box[1] + box[4]) / 2;
        sbagli += Riga("A (Ø6)  a 20 dal basso-sinistra", Foro(fori, 0.003), xmin + 0.020, ymin + 0.020);
        sbagli += Riga("D (Ø8)  a 20 dall'alto-destra  ", Foro(fori, 0.004), xmax - 0.020, ymax - 0.020);
        sbagli += Riga("B (Ø10) al centro              ", Foro(fori, 0.005), xc, yc);
        if (cPrima != null) sbagli += Riga("C (Ø12) dov'era                ", Foro(fori, 0.006), cPrima[0], cPrima[1]);
        return sbagli;
    }

    private static double[] Foro(List<double[]> fori, double raggio)
    {
        return fori.Find(f => Math.Abs(f[2] - raggio) < 1e-6);
    }

    private static int Riga(string nome, double[] f, double x, double y)
    {
        if (f == null) { Console.WriteLine("      NO  " + nome + ": il foro non c'e'"); return 1; }
        double d = Math.Sqrt(Math.Pow(f[0] - x, 2) + Math.Pow(f[1] - y, 2));
        bool ok = d < 0.00005;
        Console.WriteLine("      " + (ok ? "ok  " : "NO  ") + nome + " (" + Mm(f[0]) + ", " + Mm(f[1]) + ")  atteso (" +
                          Mm(x) + ", " + Mm(y) + ")" + (ok ? "" : "  scarto " + Mm(d)));
        return ok ? 0 : 1;
    }

    private static double[] Vicino(List<double[]> fori, double x, double y)
    {
        double[] meglio = null; double dm = double.MaxValue;
        foreach (var f in fori)
        {
            double d = Math.Pow(f[0] - x, 2) + Math.Pow(f[1] - y, 2);
            if (d < dm) { dm = d; meglio = f; }
        }
        return meglio;
    }

    /// <summary>I centri dei fori: le facce cilindriche, dal punto sull'asse.</summary>
    private static List<double[]> Fori()
    {
        var fuori = new List<double[]>();
        var corpi = ((IPartDoc)_doc).GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
        if (corpi == null) return fuori;
        foreach (var c in corpi)
        {
            var f = ((IBody2)c).GetFirstFace() as IFace2;
            while (f != null)
            {
                var s = f.GetSurface() as ISurface;
                if (s != null && s.IsCylinder())
                {
                    var p = s.CylinderParams as double[];
                    if (p != null) fuori.Add(new[] { p[0], p[1], p[6] });
                }
                f = f.GetNextFace() as IFace2;
            }
        }
        return fuori;
    }

    private static double[] Ingombro()
    {
        var corpi = ((IPartDoc)_doc).GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
        return (corpi[0] as IBody2).GetBodyBox() as double[];
    }

    // ── schizzo ──────────────────────────────────────────────────────────

    private static double[] Sk(MathTransform tr, double x, double y, double z)
    {
        var p = (IMathPoint)_mu.CreatePoint(new[] { x, y, z });
        return (double[])((IMathPoint)p.MultiplyTransform(tr)).ArrayData;
    }

    // AddToDB a vero mentre si disegna: senza, SOLIDWORKS aggiunge da solo le
    // relazioni che «indovina» (coincidenze con gli spigoli, orizzontali), e
    // sommate a quelle messe apposta sovravincolano lo schizzo appena il pezzo
    // cambia. Misurato il 24/9 sera: 5 sovravincolanti dopo la prima modifica.
    private static bool _senzaInferenze = System.Environment.GetCommandLineArgs().Length > 0 &&
        Array.IndexOf(System.Environment.GetCommandLineArgs(), "--con-inferenze") < 0;

    private static ISketchSegment Cerchio(MathTransform tr, double x, double y, double z, double raggio)
    {
        var c = Sk(tr, x, y, z);
        _doc.SketchManager.AddToDB = _senzaInferenze;
        var s = _doc.SketchManager.CreateCircleByRadius(c[0], c[1], 0, raggio);
        _doc.SketchManager.AddToDB = false;
        return s;
    }

    private static ISketchSegment Mezzeria(MathTransform tr, double z)
    {
        _doc.SketchManager.AddToDB = _senzaInferenze;
        var s = _doc.SketchManager.CreateCenterLine(
            Sk(tr, -0.100, 0, z)[0], Sk(tr, -0.100, 0, z)[1], 0,
            Sk(tr, 0.100, 0, z)[0], Sk(tr, 0.100, 0, z)[1], 0);
        _doc.SketchManager.AddToDB = false;
        return s;
    }

    /// <summary>
    /// Converte nello schizzo lo spigolo del modello che passa per (x, y, z) e
    /// lo mette a costruzione. Il segmento nuovo e' quello che prima non c'era.
    /// </summary>
    private static ISketchSegment Converti(double x, double y, double z)
    {
        var schizzo = _doc.SketchManager.ActiveSketch;
        var prima = new List<ISketchSegment>();
        foreach (var o in (schizzo.GetSketchSegments() as object[]) ?? new object[0]) prima.Add((ISketchSegment)o);
        _doc.ClearSelection2(true);
        if (!_doc.Extension.SelectByID2("", "EDGE", x, y, z, false, 0, null, 0)) return null;
        _doc.SketchManager.SketchUseEdge3(false, false);
        _doc.ClearSelection2(true);
        foreach (var o in (schizzo.GetSketchSegments() as object[]) ?? new object[0])
        {
            var s = (ISketchSegment)o;
            bool nuovo = true;
            foreach (var p in prima) if (_sw.IsSame(p, s) == (int)swObjectEquality.swObjectSame) { nuovo = false; break; }
            if (nuovo) { s.ConstructionGeometry = true; return s; }
        }
        return null;
    }

    private static bool RelazioneFraSchizzi(ISketchPoint p, ISketchSegment s, string quale)
    {
        _doc.ClearSelection2(true);
        if (!p.Select4(false, null) || !s.Select4(true, null)) return false;
        _doc.SketchAddConstraints(quale);
        _doc.ClearSelection2(true);
        return true;
    }

    /// <summary>
    /// Una quota fra un punto dello schizzo e lo spigolo del modello che passa
    /// per (ex, ey, ez), con il testo in (tx, ty, tz). Si rilegge il valore:
    /// una quota nata «a caso» fra due cose sbagliate avrebbe un altro numero.
    /// </summary>
    private static IDimension QuotaAlloSpigolo(ISketchPoint p, double ex, double ey, double ez,
                                               double tx, double ty, double tz)
    {
        _doc.ClearSelection2(true);
        if (!p.Select4(false, null)) return null;
        if (!_doc.Extension.SelectByID2("", "EDGE", ex, ey, ez, true, 0, null, 0)) return null;
        var d = (_doc.AddDimension2(tx, ty, tz) as IDisplayDimension)?.GetDimension2(0);
        _doc.ClearSelection2(true);
        return d;
    }

    private static bool Relazione(ISketchPoint p, double ex, double ey, double ez, string quale)
    {
        _doc.ClearSelection2(true);
        if (!p.Select4(false, null)) return false;
        if (!_doc.Extension.SelectByID2("", "EDGE", ex, ey, ez, true, 0, null, 0)) return false;
        _doc.SketchAddConstraints(quale);
        _doc.ClearSelection2(true);
        return true;
    }

    private static string Mm(double m) { return (m * 1000).ToString("0.###"); }
    private static string Box(double[] b)
    {
        return Mm(Math.Abs(b[3] - b[0])) + " x " + Mm(Math.Abs(b[4] - b[1])) + " x " + Mm(Math.Abs(b[5] - b[2])) +
               " (X " + Mm(b[0]) + ".." + Mm(b[3]) + ", Y " + Mm(b[1]) + ".." + Mm(b[4]) + ")";
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
