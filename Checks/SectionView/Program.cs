using System;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.SectionView
{
    /// <summary>
    /// THE SECTION IN THE DRAWING, FROM START TO FINISH, HANDS-FREE.
    ///
    /// It is the check that closes measurement 0.6 and unblocks GT4. `SketchOnSheet`
    /// had answered the two questions in the middle — one draws on the sheet, and the
    /// segment is taken back with `Select4` — but «a line can be selected»
    /// is not yet «a section comes out». Between the two there is
    /// `CreateSectionViewAt5`, which wants that line selected and does not
    /// take it in its signature.
    ///
    /// The procedure, step by step:
    ///
    ///   1. a view of the model is found on the sheet, and we read WHERE it is
    ///      (`Position`) and HOW BIG it is (`GetOutline`);
    ///   2. we enter the context of the sheet (`EditSheet`);
    ///   3. a line is drawn that CROSSES the view from side to side:
    ///      a line that grazes it will give an empty section without saying anything;
    ///   4. the line is selected;
    ///   5. `CreateSectionViewAt5` is called at a point beside it.
    ///
    /// AND THEN WE MEASURE WHETHER IT CAME OUT. The count of the views before and after, and the
    /// name of the new one. `CreateSectionViewAt5` can answer an object
    /// and have done nothing useful: the number of views is the check,
    /// the return value is only a promise.
    ///
    ///     SectionView.exe        works on the active drawing
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

            // 1. THE VIEW TO SECTION.
            // The first view of the sheet IS the sheet (type 1): it is skipped. The
            // first real model view is taken.
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

            // 3. THE LINE THAT CROSSES FROM SIDE TO SIDE.
            // It goes out by a tenth of the height above and below: a line that
            // ends exactly on the border is a free limit case.
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

            // The sketch is CLOSED before asking for the section: with an open
            // sketch SOLIDWORKS is inside a command, and the APIs that
            // create views behave differently.
            try { sketches.InsertSketch(true); } catch { }

            // 4. THE SELECTION.
            doc.ClearSelection2(true);
            bool taken = false;
            try { taken = line.Select4(false, null); }
            catch (Exception e) { Console.WriteLine("  Select4: " + e.Message); }
            var selection = (ISelectionMgr)doc.SelectionManager;
            Console.WriteLine("linea selezionata: " + taken +
                ", oggetti selezionati: " + selection.GetSelectedObjectCount2(-1) +
                ", tipo: " + (selection.GetSelectedObjectCount2(-1) > 0
                    ? selection.GetSelectedObjectType3(1, -1).ToString() : "-"));

            // 5. THE SECTION, beside the view.
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

            // THE REAL CHECK: how many views there are now.
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

            // ── THE HATCHING: TWO NUMBERS, AND BEFORE AND AFTER THE RECALCULATION ──
            //
            // Measured on 23/9/2026 because the operation `section_view`
            // refused at its first real use saying «non ha tratteggio»
            // on a cut that crosses a solid plate. It is one of two things:
            // either the hatching is not there until the drawing is recalculated, or
            // the number I was reading is not what I believed.
            //
            // `GetSolidHatchCount(out int ArraySize)` has TWO outputs — the return
            // value and the `out` — and so far I have used only one, choosing
            // by eye. Here both are printed, before and after
            // `EditRebuild3`, and the guessing stops.
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

                // ── WHAT IF THE COUNTER WERE ANOTHER ONE ────────────────
                //
                // `GetSolidHatchCount` answers zero before and after the recalculation
                // on a section that cuts a solid plate. Either the hatching
                // of a section is not a «solid hatch» for this API, or it is
                // counted elsewhere. `IView` has a second one, `GetFaceHatchCount`,
                // and until both are looked at we are guessing.
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
