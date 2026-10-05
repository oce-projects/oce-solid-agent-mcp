using System;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.DetailView
{
    /// <summary>
    /// LA VISTA DI DETTAGLIO, MISURATA INVECE CHE DEDOTTA (voce GT4).
    ///
    /// La misura 0.6 ha chiuso la sezione — linea sul foglio, `Select4`,
    /// `CreateSectionViewAt5`, quattro viste che diventano cinque — e ha
    /// lasciato scritta una cosa onesta:
    ///
    ///     «Resta non misurato il DETTAGLIO (CreateDetailViewAt4, che vuole un
    ///      cerchio invece di una linea). Per analogia dovrebbe funzionare
    ///      uguale — ma "per analogia" e' proprio il salto che qui e' gia'
    ///      costato una correzione.»
    ///
    /// Questa sonda toglie l'analogia di mezzo. Stessa forma di
    /// `SectionView`, con tre differenze che potrebbero non essere
    /// dettagli:
    ///
    ///   - si traccia un CERCHIO invece di una linea, e sta DENTRO la vista
    ///     (una sezione vuole attraversarla, un dettaglio vuole starci dentro);
    ///   - la firma ha dodici argomenti invece di sette;
    ///   - il cerchio e' un `ISketchSegment` come la linea, quindi `Select4`
    ///     dovrebbe bastare — ed e' la parte che va vista, non supposta.
    ///
    ///     IDrawingDoc :: CreateDetailViewAt4(Double X, Double Y, Double Z,
    ///         Int32 Style, Double Scale1, Double Scale2, String LabelIn,
    ///         Int32 Showtype, Boolean FullOutline, Boolean JaggedOutline,
    ///         Boolean NoOutline, Int32 ShapeIntensity)
    ///
    /// E POI SI MISURA SE E' USCITA: il conto delle viste prima e dopo, il tipo
    /// della vista nuova, e quante circonferenze di dettaglio ha la vista di
    /// partenza (`GetDetailCircleCount2`). Il valore di ritorno e' una
    /// promessa; il conto e' la misura.
    ///
    ///     DetailView.exe        lavora sulla tavola attiva
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS non e' aperto."); return 2; }
            var sw = (ISldWorks)raw;

            var doc = (IModelDoc2)sw.ActiveDoc;
            if (doc == null) { Console.Error.WriteLine("Nessun documento attivo."); return 2; }
            if (doc.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                Console.Error.WriteLine("Il documento attivo non e' una tavola.");
                return 2;
            }
            var drawing = (IDrawingDoc)doc;
            Console.WriteLine("tavola: " + doc.GetTitle());

            // 1. LA VISTA DA DETTAGLIARE. La prima «view» e' il foglio: si salta.
            IView target = null;
            int views = 0;
            var v = drawing.GetFirstView() as IView;
            while (v != null && views < 50)
            {
                views++;
                if (target == null && v.Type != (int)swDrawingViewTypes_e.swDrawingSheet)
                    target = v;
                v = v.GetNextView() as IView;
            }
            Console.WriteLine("viste sul foglio, prima: " + views + " (il foglio compreso)");
            if (target == null)
            {
                Console.Error.WriteLine("Sul foglio non c'e' nessuna vista da dettagliare.");
                return 1;
            }

            var outline = target.GetOutline() as double[];
            if (outline == null || outline.Length < 4)
            {
                Console.Error.WriteLine("Non riesco a leggere l'ingombro della vista.");
                return 1;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "vista «{0}» (tipo {1}), ingombro x {2:0.####}..{3:0.####} y {4:0.####}..{5:0.####} m",
                target.GetName2(), target.Type, outline[0], outline[2], outline[1], outline[3]));

            // DUE USCITE, E NESSUNA E' OVVIA. Vedi il commento in testa al
            // file: si stampano tutte e due invece di sceglierne una.
            int circlesBefore = -1, returnedBefore = -1;
            try { returnedBefore = target.GetDetailCircleCount2(out circlesBefore); }
            catch (Exception e) { Console.WriteLine("  GetDetailCircleCount2: " + e.Message); }
            Console.WriteLine("circonferenze PRIMA: ritorno=" + returnedBefore + "  out=" + circlesBefore);

            // 2. IL CERCHIO, DENTRO LA VISTA.
            //
            // Un quarto del lato minore come raggio: abbastanza grande da
            // contenere geometria, abbastanza piccolo da stare dentro. Un
            // cerchio che sborda e' il caso limite gratuito di questa API.
            double cx = (outline[0] + outline[2]) / 2.0;
            double cy = (outline[1] + outline[3]) / 2.0;
            double smaller = Math.Min(outline[2] - outline[0], outline[3] - outline[1]);
            double radius = smaller / 4.0;

            Console.WriteLine("EditSheet()...");
            try { drawing.EditSheet(); } catch (Exception e) { Console.WriteLine("  EditSheet: " + e.Message); }

            doc.ClearSelection2(true);
            ISketchSegment circle = null;
            try
            {
                circle = doc.SketchManager.CreateCircleByRadius(cx, cy, 0.0, radius) as ISketchSegment;
            }
            catch (Exception e) { Console.WriteLine("  CreateCircleByRadius: " + e.Message); }
            if (circle == null)
            {
                Console.Error.WriteLine("Niente cerchio, niente dettaglio.");
                return 1;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "cerchio tracciato in ({0:0.####}, {1:0.####}) raggio {2:0.####} m", cx, cy, radius));

            // Lo schizzo si CHIUDE prima, come per la sezione.
            try { doc.SketchManager.InsertSketch(true); } catch { }

            // 3. LA SELEZIONE.
            doc.ClearSelection2(true);
            bool taken = false;
            try { taken = circle.Select4(false, null); }
            catch (Exception e) { Console.WriteLine("  Select4: " + e.Message); }
            var selection = (ISelectionMgr)doc.SelectionManager;
            Console.WriteLine("cerchio selezionato: " + taken +
                ", oggetti selezionati: " + selection.GetSelectedObjectCount2(-1) +
                ", tipo: " + (selection.GetSelectedObjectCount2(-1) > 0
                    ? selection.GetSelectedObjectType3(1, -1).ToString() : "-"));

            // 4. IL DETTAGLIO, a fianco della vista.
            double xDetail = outline[2] + (outline[2] - outline[0]) * 0.8;
            double yDetail = (outline[1] + outline[3]) / 2.0;
            object born = null;
            try
            {
                // Style 0, scala 2:1, etichetta B, Showtype 0, senza contorni
                // speciali. Sono i valori piu' neutri che la firma ammette: qui
                // si misura SE nasce, non come e' vestita.
                born = drawing.CreateDetailViewAt4(
                    xDetail, yDetail, 0.0,
                    (int)swDetViewStyle_e.swDetViewSTANDARD, 2.0, 1.0, "B",
                    (int)swDetCircleShowType_e.swDetCircleCIRCLE, false, false, false, 0);
            }
            catch (Exception e)
            { Console.WriteLine("  CreateDetailViewAt4 ha sollevato: " + e.Message); }
            Console.WriteLine("CreateDetailViewAt4 ha risposto: " +
                (born == null ? "null" : "un oggetto"));

            // IL RISCONTRO: quante viste ci sono adesso, e di che tipo e' l'ultima.
            int after = 0;
            IView last = null;
            var w = drawing.GetFirstView() as IView;
            while (w != null && after < 50)
            {
                after++;
                last = w;
                w = w.GetNextView() as IView;
            }
            Console.WriteLine("viste sul foglio, dopo:  " + after);

            int circlesAfter = -1, returnedAfter = -1;
            try { returnedAfter = target.GetDetailCircleCount2(out circlesAfter); } catch { }
            Console.WriteLine("circonferenze DOPO : ritorno=" + returnedAfter + "  out=" + circlesAfter);

            // E LA VERSIONE SENZA IL «2», che e' l'altra strada che non si
            // e' provata. Se rispondesse un conto pulito, il «2» sarebbe una
            // complicazione gratuita e va scritto.
            try
            {
                int old = target.GetDetailCircleCount();
                Console.WriteLine("GetDetailCircleCount (senza il 2): " + old);
            }
            catch (Exception e)
            { Console.WriteLine("GetDetailCircleCount (senza il 2): " + e.Message); }

            // E il tratteggio anche qui: un dettaglio mostra la stessa
            // geometria ingrandita, quindi se la vista di partenza non e'
            // una sezione non ci si aspetta campitura. Si guarda lo stesso,
            // perche' «non me lo aspetto» non e' una misura.
            if (last != null)
            {
                int faces = -1;
                try { faces = last.GetFaceHatchCount(); } catch { }
                Console.WriteLine("tratteggio di faccia sul dettaglio: " + faces);
            }

            if (after > views)
            {
                string itemName = "";
                int typ = -1;
                try { itemName = last.GetName2(); } catch { }
                try { typ = last.Type; } catch { }
                Console.WriteLine("  -> IL DETTAGLIO C'E'. Ultima vista: «" + itemName + "», tipo " + typ +
                    (typ == (int)swDrawingViewTypes_e.swDrawingDetailView
                        ? " (swDrawingDetailView)" : " (NON e' un dettaglio!)"));
            }
            else
            {
                Console.WriteLine("  -> nessuna vista in piu': il dettaglio NON e' nato");
            }

            try { doc.EditRebuild3(); } catch { }
            return after > views ? 0 : 1;
        }
    }
}
