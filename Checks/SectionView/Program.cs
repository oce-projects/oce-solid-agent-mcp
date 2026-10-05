using System;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.SectionView
{
    /// <summary>
    /// LA SEZIONE IN TAVOLA, DAL PRINCIPIO ALLA FINE, SENZA MANI.
    ///
    /// E' la prova che chiude la misura 0.6 e sblocca GT4. `SketchOnSheet`
    /// aveva risposto alle due domande di mezzo — si traccia sul foglio, e il
    /// segmento si riprende con `Select4` — ma «si puo' selezionare una linea»
    /// non e' ancora «esce una sezione». Fra le due c'e'
    /// `CreateSectionViewAt5`, che vuole quella linea selezionata e non la
    /// prende in firma.
    ///
    /// Il giro, passo per passo:
    ///
    ///   1. si trova una vista del modello sul foglio, e si legge DOVE sta
    ///      (`Position`) e QUANTO e' grande (`GetOutline`);
    ///   2. si entra nel contesto del foglio (`EditSheet`);
    ///   3. si traccia una linea che ATTRAVERSA la vista da parte a parte:
    ///      una linea che la sfiora dara' una sezione vuota senza dire niente;
    ///   4. si seleziona la linea;
    ///   5. si chiama `CreateSectionViewAt5` in un punto a fianco.
    ///
    /// E POI SI MISURA SE E' USCITA. Il conto delle viste prima e dopo, e il
    /// nome di quella nuova. `CreateSectionViewAt5` puo' rispondere un oggetto
    /// e non aver fatto niente di utile: il numero delle viste e' il riscontro,
    /// il valore di ritorno e' solo una promessa.
    ///
    ///     SectionView.exe        lavora sulla tavola attiva
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

            // 1. LA VISTA DA SEZIONARE.
            // La prima vista del foglio E' il foglio (tipo 1): si salta. Si
            // prende la prima vista di modello vera.
            IView target = null;
            int views = 0;
            var v = drawing.GetFirstView() as IView;
            while (v != null)
            {
                views++;
                if (target == null && v.Type != (int)swDrawingViewTypes_e.swDrawingSheet)
                    target = v;
                v = v.GetNextView() as IView;
                if (views > 50) break;
            }
            Console.WriteLine("viste sul foglio, prima: " + views + " (il foglio compreso)");
            if (target == null)
            {
                Console.Error.WriteLine("Sul foglio non c'e' nessuna vista da sezionare.");
                return 1;
            }

            var location = target.Position as double[];
            var outline = target.GetOutline() as double[];
            if (location == null || outline == null || outline.Length < 4)
            {
                Console.Error.WriteLine("Non riesco a leggere posizione e ingombro della vista.");
                return 1;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "vista «{0}» (tipo {1}) in ({2:0.####}, {3:0.####}) m",
                target.GetName2(), target.Type, location[0], location[1]));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "ingombro sul foglio: x {0:0.####}..{1:0.####}  y {2:0.####}..{3:0.####} m",
                outline[0], outline[2], outline[1], outline[3]));

            // 3. LA LINEA CHE ATTRAVERSA DA PARTE A PARTE.
            // Si esce di un decimo dell'altezza sopra e sotto: una linea che
            // finisce esattamente sul bordo e' un caso limite gratuito.
            double xCut = (outline[0] + outline[2]) / 2.0;
            double height = outline[3] - outline[1];
            double y1 = outline[1] - height * 0.1;
            double y2 = outline[3] + height * 0.1;

            Console.WriteLine("EditSheet()...");
            try { drawing.EditSheet(); }
            catch (Exception e) { Console.WriteLine("  EditSheet: " + e.Message); }

            doc.ClearSelection2(true);
            var sketches = doc.SketchManager;
            ISketchSegment line = null;
            try { line = sketches.CreateLine(xCut, y1, 0.0, xCut, y2, 0.0) as ISketchSegment; }
            catch (Exception e) { Console.WriteLine("  CreateLine: " + e.Message); }
            if (line == null) { Console.Error.WriteLine("Niente linea, niente sezione."); return 1; }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "linea tracciata in x={0:0.####}, da y={1:0.####} a y={2:0.####}",
                xCut, y1, y2));

            // Lo schizzo si CHIUDE prima di chiedere la sezione: con uno
            // schizzo aperto SOLIDWORKS e' dentro un comando, e le API che
            // creano viste si comportano diversamente.
            try { sketches.InsertSketch(true); } catch { }

            // 4. LA SELEZIONE.
            doc.ClearSelection2(true);
            bool taken = false;
            try { taken = line.Select4(false, null); }
            catch (Exception e) { Console.WriteLine("  Select4: " + e.Message); }
            var selection = (ISelectionMgr)doc.SelectionManager;
            Console.WriteLine("linea selezionata: " + taken +
                ", oggetti selezionati: " + selection.GetSelectedObjectCount2(-1) +
                ", tipo: " + (selection.GetSelectedObjectCount2(-1) > 0
                    ? selection.GetSelectedObjectType3(1, -1).ToString() : "-"));

            // 5. LA SEZIONE, a fianco della vista.
            double xSection = outline[2] + (outline[2] - outline[0]) * 0.8;
            double ySection = (outline[1] + outline[3]) / 2.0;
            object born = null;
            try
            {
                born = drawing.CreateSectionViewAt5(
                    xSection, ySection, 0.0, "A", 0, null, 0);
            }
            catch (Exception e) { Console.WriteLine("  CreateSectionViewAt5 ha sollevato: " + e.Message); }
            Console.WriteLine("CreateSectionViewAt5 ha risposto: " +
                (born == null ? "null" : "un oggetto"));

            // IL RISCONTRO VERO: quante viste ci sono adesso.
            int after = 0;
            string last = "";
            IView lastView = null;
            var w = drawing.GetFirstView() as IView;
            while (w != null)
            {
                after++;
                last = w.GetName2();
                lastView = w;
                w = w.GetNextView() as IView;
                if (after > 50) break;
            }
            Console.WriteLine("viste sul foglio, dopo:  " + after);
            Console.WriteLine(after > views
                ? "  -> LA SEZIONE C'E'. Ultima vista: «" + last + "»"
                : "  -> nessuna vista in piu': la sezione NON e' nata");

            // ── IL TRATTEGGIO: DUE NUMERI, E PRIMA E DOPO IL RICALCOLO ──
            //
            // Misurato il 23/9/2026 perche' l'operazione `section_view`
            // si e' rifiutata al primo uso vero dicendo «non ha tratteggio»
            // su un taglio che attraversa una piastra piena. Delle due l'una:
            // o il tratteggio non c'e' finche' la tavola non si ricalcola, o
            // il numero che leggevo non e' quello che credevo.
            //
            // `GetSolidHatchCount(out int ArraySize)` ha DUE uscite — il valore
            // di ritorno e l'`out` — e finora ne ho usata una sola, scegliendo
            // a occhio. Qui si stampano tutte e due, prima e dopo
            // `EditRebuild3`, e si smette di indovinare.
            if (lastView != null)
            {
                int howMany1 = -1, returned1 = -1;
                try { returned1 = lastView.GetSolidHatchCount(out howMany1); } catch (Exception e)
                { Console.WriteLine("  GetSolidHatchCount (prima): " + e.Message); }
                Console.WriteLine("tratteggio PRIMA del ricalcolo: ritorno=" + returned1 +
                                  "  out=" + howMany1);
            }

            try { doc.EditRebuild3(); } catch { }

            if (lastView != null)
            {
                int howMany2 = -1, returned2 = -1;
                try { returned2 = lastView.GetSolidHatchCount(out howMany2); } catch (Exception e)
                { Console.WriteLine("  GetSolidHatchCount (dopo): " + e.Message); }
                Console.WriteLine("tratteggio DOPO  il ricalcolo: ritorno=" + returned2 +
                                  "  out=" + howMany2);

                // ── E SE IL CONTATORE FOSSE UN ALTRO ────────────────────
                //
                // `GetSolidHatchCount` risponde zero prima e dopo il ricalcolo
                // su una sezione che taglia una piastra piena. O il tratteggio
                // di una sezione non e' un «solid hatch» per questa API, o si
                // conta altrove. `IView` ne ha un secondo, `GetFaceHatchCount`,
                // e finche' non si guardano tutti e due si sta indovinando.
                int faces = -1;
                try { faces = lastView.GetFaceHatchCount(); } catch (Exception e)
                { Console.WriteLine("  GetFaceHatchCount: " + e.Message); }
                Console.WriteLine("tratteggio di FACCIA:          " + faces);

                int typ = -1;
                try { typ = lastView.Type; } catch { }
                bool hasSection = false;
                try { hasSection = lastView.GetSection() != null; } catch { }
                int cutLines = -1;
                try { int rt; lastView.GetSectionLineCount2(out rt); cutLines = rt; } catch { }
                Console.WriteLine("tipo vista: " + typ +
                                  "  (swDrawingSectionView = " +
                                  (int)swDrawingViewTypes_e.swDrawingSectionView + ")");
                Console.WriteLine("GetSection() risponde: " + (hasSection ? "un oggetto" : "null") +
                                  "   righe di taglio sulla vista di partenza: " + cutLines);
            }

            return after > views ? 0 : 1;
        }
    }
}
