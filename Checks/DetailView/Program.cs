using System;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.DetailView
{
    /// <summary>
    /// THE DETAIL VIEW, MEASURED INSTEAD OF DEDUCED (entry GT4).
    ///
    /// The measurement 0.6 closed the section — line on the sheet, `Select4`,
    /// `CreateSectionViewAt5`, four views that become five — and
    /// left an honest thing written:
    ///
    ///     «The DETAIL remains unmeasured (CreateDetailViewAt4, which wants a
    ///      circle instead of a line). By analogy it should work the
    ///      same — but "by analogy" is exactly the leap that here has already
    ///      cost a correction.»
    ///
    /// This probe removes the analogy from the middle. Same shape as
    /// `SectionView`, with three differences that might not be
    /// details:
    ///
    ///   - a CIRCLE is drawn instead of a line, and it sits INSIDE the view
    ///     (a section wants to cross it, a detail wants to be inside it);
    ///   - the signature has twelve arguments instead of seven;
    ///   - the circle is an `ISketchSegment` like the line, so `Select4`
    ///     should be enough — and it is the part that must be seen, not assumed.
    ///
    ///     IDrawingDoc :: CreateDetailViewAt4(Double X, Double Y, Double Z,
    ///         Int32 Style, Double Scale1, Double Scale2, String LabelIn,
    ///         Int32 Showtype, Boolean FullOutline, Boolean JaggedOutline,
    ///         Boolean NoOutline, Int32 ShapeIntensity)
    ///
    /// AND THEN WE MEASURE WHETHER IT CAME OUT: the count of the views before and after, the type
    /// of the new view, and how many detail circles the starting view has
    /// (`GetDetailCircleCount2`). The return value is a
    /// promise; the count is the measure.
    ///
    ///     DetailView.exe        works on the active drawing
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

            // 1. THE VIEW TO BE DETAILED. The first «view» is the sheet: it is skipped.
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

            // TWO OUTPUTS, AND NEITHER IS OBVIOUS. See the comment at the top of the
            // file: both are printed instead of choosing one.
            int circlesBefore = -1, returnedBefore = -1;
            try { returnedBefore = target.GetDetailCircleCount2(out circlesBefore); }
            catch (Exception e) { Console.WriteLine("  GetDetailCircleCount2: " + e.Message); }
            Console.WriteLine("circonferenze PRIMA: ritorno=" + returnedBefore + "  out=" + circlesBefore);

            // 2. THE CIRCLE, INSIDE THE VIEW.
            //
            // A quarter of the shorter side as radius: big enough to
            // contain geometry, small enough to stay inside. A
            // circle that overflows is the free limit case of this API.
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

            // The sketch is CLOSED first, as for the section.
            try { doc.SketchManager.InsertSketch(true); } catch { }

            // 3. THE SELECTION.
            doc.ClearSelection2(true);
            bool taken = false;
            try { taken = circle.Select4(false, null); }
            catch (Exception e) { Console.WriteLine("  Select4: " + e.Message); }
            var selection = (ISelectionMgr)doc.SelectionManager;
            Console.WriteLine("cerchio selezionato: " + taken +
                ", oggetti selezionati: " + selection.GetSelectedObjectCount2(-1) +
                ", tipo: " + (selection.GetSelectedObjectCount2(-1) > 0
                    ? selection.GetSelectedObjectType3(1, -1).ToString() : "-"));

            // 4. THE DETAIL, beside the view.
            double xDetail = outline[2] + (outline[2] - outline[0]) * 0.8;
            double yDetail = (outline[1] + outline[3]) / 2.0;
            object born = null;
            try
            {
                // Style 0, scale 2:1, label B, Showtype 0, without special
                // outlines. They are the most neutral values that the signature allows: here
                // we measure WHETHER it is born, not how it is dressed.
                born = drawing.CreateDetailViewAt4(
                    xDetail, yDetail, 0.0,
                    (int)swDetViewStyle_e.swDetViewSTANDARD, 2.0, 1.0, "B",
                    (int)swDetCircleShowType_e.swDetCircleCIRCLE, false, false, false, 0);
            }
            catch (Exception e)
            { Console.WriteLine("  CreateDetailViewAt4 ha sollevato: " + e.Message); }
            Console.WriteLine("CreateDetailViewAt4 ha risposto: " +
                (born == null ? "null" : "un oggetto"));

            // THE CHECK: how many views there are now, and what type the last one is.
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

            // AND THE VERSION WITHOUT THE «2», which is the other road that has not been
            // tried. If it answered a clean count, the «2» would be a
            // gratuitous complication and it must be written down.
            try
            {
                int old = target.GetDetailCircleCount();
                Console.WriteLine("GetDetailCircleCount (senza il 2): " + old);
            }
            catch (Exception e)
            { Console.WriteLine("GetDetailCircleCount (senza il 2): " + e.Message); }

            // And the hatching here too: a detail shows the same
            // geometry enlarged, so if the starting view is not
            // a section no hatching is expected. We look anyway,
            // because «I do not expect it» is not a measurement.
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
