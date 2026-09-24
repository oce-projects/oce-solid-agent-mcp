using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.gtswutilities;

// =============================================================================
//  Spessore — la misura che dimensiona la voce Z-H1 (lo spessore minimo di
//             parete, con SOLIDWORKS Utilities)
// =============================================================================
//
//  PERCHE' QUESTO PEZZO
//
//  Come per la mensola di S3: la risposta si sa SENZA il calcolatore. Una
//  piastra 100 x 60 alta 2 mm, e sopra, su meta' della sua lunghezza, un blocco
//  alto 10. Due estrusioni dal piano superiore, nessuna faccia da scegliere:
//
//      spessore minimo = 2,0 mm   (la meta' bassa)
//
//  Il goal lo chiede perche' una parete troppo sottile non da' nessun errore e
//  cede in barca: e' il difetto che non si vede, portato nella geometria.
//
//  IL CONTROLLO NEI DUE VERSI, e non e' un ornamento. Una sola analisi che
//  dicesse «2,0» potrebbe essere giusta o fortunata. Quindi due soglie:
//
//      soglia 3,0 mm  -> la meta' da 2 mm e' SOTTO: facce critiche > 0
//      soglia 1,5 mm  -> niente e' sotto:           facce critiche = 0
//
//  Se la seconda trova facce critiche, o la prima non ne trova, il numero non
//  e' una misura e lo si dice.
//
//  ── LA CATENA, accertata per riflessione il 24/9/2026 ───────────────────────
//
//      ISldWorks.GetAddInObject(CLSID di Utilities.UtilitiesApp) -> IUtilities
//        .GetToolInterface(gtSWThickChk = 7, out err)             -> IThicknessAnalysis
//        .Init()
//        .SetThicknessAnalysisBody(IBody2)
//        .RunThinAnalysis2(soglia, risoluzione, gtResultNoUI, report, ...)
//        .GetMinTckOnAnalArea(out err) / GetNumCriticalFaces(out err)
//        .Close()
//
//  Cosa la riflessione NON puo' dire, e per cui la sonda esiste: in che UNITA'
//  vuole la soglia (millimetri del documento o metri dell'API?) e in che unita'
//  risponde. Si prova e si stampa, invece di indovinare.
//
//  USO
//      .\Controlli\Spessore\bin\Spessore.exe
//      .\Controlli\Spessore\bin\Spessore.exe --tieni     lascia il pezzo aperto
//      .\Controlli\Spessore\bin\Spessore.exe --carica    carica Utilities nella
//                                                        sessione se non c'e'
// =============================================================================

internal static class Programma
{
    // Da HKCR\Utilities.UtilitiesApp\CLSID, letto il 24/9/2026 (voce K20).
    private const string ClsidUtilities = "{F80FA0F1-B13D-11d4-944A-000629992CFE}";

    // gtSwTools_e / gttckResolutionOptions_e / gtResultOptions_e, per riflessione.
    private const int StrumentoSpessore = 7;      // gtSWThickChk
    private const int RisoluzioneAlta = 3;        // gttckHighResolution
    private const int SenzaFinestra = 0;          // gtResultNoUI

    private const double LunghezzaMm = 100.0;
    private const double LarghezzaMm = 60.0;
    private const double PiastraMm = 2.0;
    private const double BloccoMm = 10.0;

    private static int _passo;
    private static void Dice(string cosa)
    {
        _passo++;
        Console.WriteLine("  " + _passo.ToString("00") + ". " + cosa);
    }

    private static int Main(string[] argomenti)
    {
        bool tieni = Array.IndexOf(argomenti, "--tieni") >= 0;
        bool carica = Array.IndexOf(argomenti, "--carica") >= 0;

        Console.WriteLine();
        Console.WriteLine("SPESSORE — la misura che dimensiona Z-H1 (SOLIDWORKS Utilities)");
        Console.WriteLine("  piastra " + LunghezzaMm + " x " + LarghezzaMm + " x " + PiastraMm +
                          " mm, e su meta' un blocco alto " + BloccoMm + " mm");
        Console.WriteLine("  spessore minimo = " + PiastraMm.ToString("0.0") + " mm   <- si sa senza calcolatore");
        Console.WriteLine();

        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex)
        {
            Console.WriteLine("SOLIDWORKS non risponde: " + ex.Message);
            return 2;
        }
        // Il controllo positivo sull'aggancio: se RevisionNumber non risponde,
        // un null piu' sotto non direbbe niente (e' successo in S3).
        Dice("agganciato a SOLIDWORKS " + sw.RevisionNumber());

        // ── L'ADD-IN ─────────────────────────────────────────────────────
        object addin = sw.GetAddInObject(ClsidUtilities);
        if (addin == null && carica)
        {
            // CARICARE NELLA SESSIONE, NON ALL'AVVIO. `LoadAddIn` lo accende per
            // questa sessione e non tocca AddInsStartup: si toglie chiudendo
            // SOLIDWORKS, o con UnloadAddIn. Solo con --carica: accendere un
            // modulo nella sessione di chi lavora e' una scelta sua, non della
            // sonda.
            string dll = Path.Combine(RadiceSolidWorks(sw), "sldUtils", "SwLoaderSw.dll");
            int esito = sw.LoadAddIn(dll);
            Dice("LoadAddIn(" + dll + ") -> " + esito);
            addin = sw.GetAddInObject(ClsidUtilities);
        }
        if (addin == null)
        {
            Console.WriteLine();
            Console.WriteLine("UTILITIES NON E' CARICATO in questa sessione.");
            Console.WriteLine("  Non vuol dire che non c'e' (K20 lo trova registrato). Spunta");
            Console.WriteLine("  'SOLIDWORKS Utilities' in Strumenti > Componenti aggiuntivi, oppure");
            Console.WriteLine("  rilancia con --carica.");
            return 3;
        }
        Dice("Utilities caricato: " + addin.GetType().Name);

        var utilita = addin as IUtilities;
        if (utilita == null)
        {
            Console.WriteLine("  l'oggetto dell'add-in non e' un IUtilities: la catena accertata per");
            Console.WriteLine("  riflessione non e' quella che gira.");
            return 5;
        }

        IModelDoc2 doc = null;
        try
        {
            doc = CreaIlPezzo(sw);
            if (doc == null) return 4;

            IBody2 corpo = PrimoCorpo(doc);
            if (corpo == null) { Console.WriteLine("  nessun corpo solido"); return 4; }

            // Il volume, perche' il pezzo sia quello che si crede prima di
            // misurarne lo spessore: 100x60x2 + 50x60x8 = 12000 + 24000 mm^3.
            double[] massa = doc.Extension.GetMassProperties2(1, out int statoMassa, false) as double[];
            double volMm3 = massa == null ? double.NaN : massa[3] * 1e9;
            Dice("volume " + volMm3.ToString("0") + " mm^3 (atteso 36000)");

            // ── DUE SOGLIE, E IN DUE UNITA' ──────────────────────────────
            //
            // Non si sa se la soglia va in millimetri o in metri. Si provano
            // tutt'e due con la soglia da 3 mm: quella giusta trova facce
            // critiche e un minimo intorno a 2; quella sbagliata o rifiuta o
            // risponde qualcosa che si riconosce come assurdo.
            Console.WriteLine();
            Console.WriteLine("  soglia       risposta  minimo      massimo     facce critiche");
            Console.WriteLine("  ----------   --------  ----------  ----------  --------------");
            var mm3 = Analizza(utilita, corpo, 3.0, "3,0 (mm?)");
            var m3 = Analizza(utilita, corpo, 0.003, "0,003 (m?)");

            Esito giusta = null;
            string unita = null;
            if (mm3 != null && mm3.Critiche > 0 && Vicino(mm3.Minimo, 2.0, 0.2)) { giusta = mm3; unita = "mm"; }
            else if (m3 != null && m3.Critiche > 0 && Vicino(m3.Minimo * 1000.0, 2.0, 0.2)) { giusta = m3; unita = "m"; }

            if (giusta == null)
            {
                Console.WriteLine();
                Console.WriteLine("NESSUNA DELLE DUE UNITA' DA' UN MINIMO VICINO A 2 mm CON FACCE CRITICHE.");
                Console.WriteLine("  La catena gira ma il numero non si riconosce: leggere la tabella sopra");
                Console.WriteLine("  prima di concludere qualunque cosa. NON si costruisce l'operazione.");
                return 6;
            }

            // L'altro verso: una soglia sotto lo spessore minimo non deve
            // trovare niente.
            double soglia15 = unita == "mm" ? 1.5 : 0.0015;
            var sotto = Analizza(utilita, corpo, soglia15, unita == "mm" ? "1,5 (mm)" : "0,0015 (m)");
            Console.WriteLine();

            double minimoMm = unita == "mm" ? giusta.Minimo : giusta.Minimo * 1000.0;
            Console.WriteLine("  unita' della soglia: " + unita);
            Console.WriteLine("  a mano:              " + PiastraMm.ToString("0.00") + " mm");
            Console.WriteLine("  l'API:               " + minimoMm.ToString("0.00") + " mm");

            if (sotto == null || sotto.Critiche != 0)
            {
                Console.WriteLine();
                Console.WriteLine("L'ALTRO VERSO NON TORNA: con la soglia sotto lo spessore minimo le");
                Console.WriteLine("facce critiche dovevano essere zero. Il minimo sopra potrebbe essere");
                Console.WriteLine("una coincidenza.");
                return 7;
            }

            Console.WriteLine();
            Console.WriteLine("REGGE NEI DUE VERSI: soglia sopra -> facce critiche; soglia sotto -> nessuna;");
            Console.WriteLine("minimo entro 0,2 mm dal calcolato. La voce Z-H1 si puo' costruire.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("ECCEZIONE: " + Radice(ex).GetType().Name + ": " + Radice(ex).Message);
            return 8;
        }
        finally
        {
            if (doc != null && !tieni)
            {
                try { sw.CloseDoc(doc.GetTitle()); Dice("pezzo chiuso senza salvare"); } catch { }
            }
        }
    }

    private sealed class Esito
    {
        public double Minimo, Massimo;
        public int Critiche;
    }

    /// <summary>Un'analisi dello spessore sottile, con tutti gli esiti stampati.</summary>
    private static Esito Analizza(IUtilities u, IBody2 corpo, double soglia, string etichetta)
    {
        int err;
        var t = u.GetToolInterface(StrumentoSpessore, out err) as IThicknessAnalysis;
        if (t == null)
        {
            Console.WriteLine("  " + etichetta.PadRight(12) + " GetToolInterface -> null, errore " + err);
            return null;
        }
        try
        {
            int init = t.Init();
            int corpoOk = t.SetThicknessAnalysisBody((Body2)corpo);
            int giro = t.RunThinAnalysis2(soglia, RisoluzioneAlta, SenzaFinestra, "", false, false, true);
            if (giro != 0)
            {
                Console.WriteLine("  " + etichetta.PadRight(12) + " " + giro.ToString().PadLeft(8) +
                                  "  (Init " + init + ", corpo " + corpoOk + ")");
                return null;
            }
            int e1, e2, e3;
            var r = new Esito
            {
                Minimo = t.GetMinTckOnAnalArea(out e1),
                Massimo = t.GetMaxTckOnAnalArea(out e2),
                Critiche = t.GetNumCriticalFaces(out e3),
            };
            Console.WriteLine("  " + etichetta.PadRight(12) + " " + giro.ToString().PadLeft(8) + "  " +
                              r.Minimo.ToString("0.0000").PadLeft(10) + "  " +
                              r.Massimo.ToString("0.0000").PadLeft(10) + "  " +
                              r.Critiche.ToString().PadLeft(14) +
                              (e1 != 0 || e2 != 0 || e3 != 0 ? "   (err " + e1 + "/" + e2 + "/" + e3 + ")" : ""));
            return r;
        }
        finally
        {
            try { t.Close(); } catch { }
        }
    }

    private static bool Vicino(double v, double atteso, double tolleranza)
    {
        return !double.IsNaN(v) && Math.Abs(v - atteso) <= tolleranza;
    }

    private static IModelDoc2 CreaIlPezzo(ISldWorks sw)
    {
        string modello = sw.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        var doc = (IModelDoc2)sw.NewDocument(modello, 0, 0, 0);
        if (doc == null) { Console.WriteLine("  NewDocument -> null"); return null; }

        // 1. la piastra bassa, intera
        if (!Estrusione(doc, 0, 0, LunghezzaMm, LarghezzaMm, PiastraMm, false)) return null;
        // 2. il blocco alto, sulla seconda meta', che si unisce alla piastra
        if (!Estrusione(doc, LunghezzaMm / 2.0, 0, LunghezzaMm, LarghezzaMm, BloccoMm, true)) return null;

        doc.ViewZoomtofit2();
        Dice("pezzo creato: piastra da " + PiastraMm + " mm, blocco da " + BloccoMm + " mm su meta'");
        return doc;
    }

    /// <summary>Un rettangolo sul piano superiore, estruso di `altezzaMm`.</summary>
    private static bool Estrusione(IModelDoc2 doc, double x1, double y1, double x2, double y2,
                                   double altezzaMm, bool unisci)
    {
        doc.ClearSelection2(true);
        if (!doc.Extension.SelectByID2("Piano superiore", "PLANE", 0, 0, 0, false, 0, null, 0))
        {
            Console.WriteLine("  non trovo «Piano superiore» (modello in un'altra lingua?)");
            return false;
        }
        doc.SketchManager.InsertSketch(true);
        doc.SketchManager.CreateCornerRectangle(x1 / 1000.0, y1 / 1000.0, 0, x2 / 1000.0, y2 / 1000.0, 0);
        doc.SketchManager.InsertSketch(true);
        var f = doc.FeatureManager.FeatureExtrusion3(
            true, false, false, 0, 0, altezzaMm / 1000.0, 0.0,
            false, false, false, false, 0, 0, false, false, false, false,
            unisci, true, true, 0, 0, false);
        if (f == null) { Console.WriteLine("  l'estrusione da " + altezzaMm + " mm non e' venuta"); return false; }
        return true;
    }

    private static IBody2 PrimoCorpo(IModelDoc2 doc)
    {
        var parte = doc as IPartDoc;
        var corpi = parte == null ? null : parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (corpi == null || corpi.Length == 0) return null;
        if (corpi.Length > 1) Console.WriteLine("      attenzione: " + corpi.Length + " corpi, si analizza il primo");
        return corpi[0] as IBody2;
    }

    private static string RadiceSolidWorks(ISldWorks sw)
    {
        // GetExecutablePath e' la cartella di SLDWORKS.exe: la radice giusta
        // anche con due alberi installati, che su questa postazione ci sono.
        try { return sw.GetExecutablePath(); } catch { }
        return @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS";
    }

    private static Exception Radice(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex;
    }
}
