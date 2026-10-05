using System;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.SketchOnSheet
{
    /// <summary>
    /// THE LIVE HALF OF MEASUREMENT 0.6: can one draw ON THE SHEET of a
    /// drawing, and with what is what was drawn taken back?
    ///
    /// WHY IT MATTERS. `CreateSectionViewAt5` wants a LINE already selected
    /// and `CreateDetailViewAt4` a CIRCLE: neither takes the entity
    /// in its signature. Until we know how to draw and reselect an entity on the
    /// sheet, sections and details in the drawing cannot be made — and they are the
    /// first thing missing from a real shop drawing.
    ///
    /// By reflection it had already been found that the sheet has a sketch of its own
    /// (`ISheet.GetTemplateSketch`) and that `EditSheet()` enters its context.
    /// **Read is not measured**: here we really draw and look at what
    /// happens.
    ///
    /// WHAT IT PRINTS. A number for every question, so that the note in the RAG is written
    /// by copying instead of remembering. Where there is «-» the question got no
    /// answer, and it shows.
    ///
    ///     SketchOnSheet.exe        works on the active drawing
    ///
    /// It wants a DRAWING open and in front. It does not open one by itself: creating a
    /// drawing means choosing a sheet template, and that choice belongs to
    /// whoever works.
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
                Console.Error.WriteLine(
                    "Il documento attivo e' «" + doc.GetTitle() + "», che non e' una tavola.");
                return 2;
            }
            var drawing = (IDrawingDoc)doc;
            Console.WriteLine("tavola: " + doc.GetTitle());

            var sheet = (ISheet)drawing.GetCurrentSheet();
            Console.WriteLine("foglio: " + sheet.GetName());
            var property = sheet.GetProperties2() as double[];
            if (property != null && property.Length >= 7)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "misura foglio: {0:0.###} x {1:0.###} m", property[5], property[6]));

            // 1. HOW MANY ENTITIES THE SHEET SKETCH ALREADY HAS.
            // The starting number is needed: «I have one» means nothing if
            // one does not know how many there were before.
            int before = SheetSegments(sheet);
            Console.WriteLine("segmenti nello schizzo del foglio, prima: " + Show(before));

            // 2. WE ENTER THE CONTEXT OF THE SHEET.
            Console.WriteLine("EditSheet()...");
            try { drawing.EditSheet(); }
            catch (Exception e) { Console.WriteLine("  EditSheet ha sollevato: " + e.Message); }

            // 3. WE DRAW. The coordinates are in METRES and in the system of the
            //    sheet: 0.05 m are 50 mm from the bottom left border.
            var sketches = doc.SketchManager;
            ISketchSegment segment = null;
            try
            {
                segment = sketches.CreateLine(0.05, 0.05, 0.0, 0.15, 0.05, 0.0) as ISketchSegment;
            }
            catch (Exception e) { Console.WriteLine("  CreateLine ha sollevato: " + e.Message); }
            // WARNING: `ISketchSegment.GetType()` is NOT that of Object.
            // The interop hides it with a method of its own that returns the SEGMENT
            // TYPE as an integer (0 = line, 1 = arc, 3 = spline). Writing
            // `.Name` after it does not compile, and it is a good thing: if it compiled one
            // would read a class name in place of a line.
            Console.WriteLine("CreateLine ha risposto: " +
                (segment == null ? "null" : "segmento di tipo " + segment.GetType() +
                 (segment.GetType() == 0 ? " (linea)" : "")));

            int after = SheetSegments(sheet);
            Console.WriteLine("segmenti nello schizzo del foglio, dopo:  " + Show(after));
            if (before >= 0 && after >= 0)
                Console.WriteLine(after > before
                    ? "  -> il segmento E' FINITO nello schizzo del foglio"
                    : "  -> NON e' finito nello schizzo del foglio: sta da un'altra parte");

            if (segment == null)
            {
                Console.WriteLine();
                Console.WriteLine("Senza segmento non si puo' provare la selezione.");
                return 1;
            }

            // 3b. SO WHERE DID IT END UP?
            // The question is not academic: to take back a line that was not
            // just drawn one must know in which sketch to look for it.
            var active = sketches.ActiveSketch as ISketch;
            Console.WriteLine("schizzo attivo: " + (active == null ? "nessuno" : Name(active)));
            if (active != null)
            {
                var inside = active.GetSketchSegments() as object[];
                Console.WriteLine("  segmenti dello schizzo attivo: " +
                    (inside == null ? "-" : inside.Length.ToString()));
            }

            // The first view of a drawing IS THE SHEET: the entities drawn
            // on the sheet sit in its sketch, not in that of the format.
            try
            {
                var view = drawing.GetFirstView() as IView;
                int n = 0;
                while (view != null)
                {
                    var its = view.GetSketch() as ISketch;
                    var segm = its == null ? null : its.GetSketchSegments() as object[];
                    Console.WriteLine("  vista " + n + ": «" + view.GetName2() + "»" +
                        ", tipo " + view.Type +
                        ", segmenti nel suo schizzo: " +
                        (segm == null ? "-" : segm.Length.ToString()));
                    view = view.GetNextView() as IView;
                    n++;
                    if (n > 20) break;
                }
            }
            catch (Exception e) { Console.WriteLine("  giro delle viste: " + e.Message); }

            // 4. THE REAL QUESTION: CAN IT BE TAKEN BACK WITHOUT GOING THROUGH A NAME?
            // `CreateSectionViewAt5` wants the line selected, and a name
            // string would be fragile (localised, and different for every sketch).
            var selection = (ISelectionMgr)doc.SelectionManager;
            doc.ClearSelection2(true);
            bool taken = false;
            try { taken = segment.Select4(false, null); }
            catch (Exception e) { Console.WriteLine("  Select4 ha sollevato: " + e.Message); }
            Console.WriteLine("Select4(false, null): " + taken);
            Console.WriteLine("oggetti selezionati: " +
                selection.GetSelectedObjectCount2(-1));
            if (selection.GetSelectedObjectCount2(-1) > 0)
            {
                int typ = selection.GetSelectedObjectType3(1, -1);
                Console.WriteLine("tipo selezionato: " + typ + "  (" + TypeNameOf(typ) + ")");
                Console.WriteLine("marca: " + selection.GetSelectedObjectMark(1));
            }

            // 5. AND WITH A MARK? The APIs that want an entity «of a certain
            //    role» look for it by mark, not by position in the list.
            doc.ClearSelection2(true);
            bool withMark = false;
            try
            {
                var data = (SelectData)selection.CreateSelectData();
                data.Mark = 1;
                withMark = segment.Select4(false, data);
            }
            catch (Exception e) { Console.WriteLine("  Select4 con marca ha sollevato: " + e.Message); }
            Console.WriteLine("Select4 con Mark=1: " + withMark +
                ", marca riletta: " +
                (selection.GetSelectedObjectCount2(-1) > 0
                    ? selection.GetSelectedObjectMark(1).ToString()
                    : "-"));

            // 6. THE NAME, if there is one. Not to use it — to know whether
            //    it exists and how it is made.
            try
            {
                var entity = segment as IEntity;
                Console.WriteLine("il segmento e' anche IEntity: " + (entity != null));
            }
            catch { }

            Console.WriteLine();
            Console.WriteLine("Lo schizzo resta aperto: guardalo, poi annulla se non lo vuoi.");
            return 0;
        }

        /// <summary>
        /// How many segments the sheet sketch has. -1 if it could not be
        /// counted: a fake zero would be worse, because it would make a check that looked
        /// at nothing read «it has grown from 0 to 1».
        /// </summary>
        private static int SheetSegments(ISheet sheet)
        {
            try
            {
                var sketch = sheet.GetTemplateSketch() as ISketch;
                if (sketch == null) return -1;
                var segments = sketch.GetSketchSegments() as object[];
                return segments == null ? 0 : segments.Length;
            }
            catch { return -1; }
        }

        /// <summary>The name of a sketch, if it can be read.</summary>
        private static string Name(ISketch sketch)
        {
            try
            {
                var f = sketch as IFeature;
                return f == null ? "(senza nome)" : f.Name;
            }
            catch { return "(nome illeggibile)"; }
        }

        private static string Show(int n)
        {
            return n < 0 ? "- (non contati)" : n.ToString();
        }

        private static string TypeNameOf(int typ)
        {
            switch (typ)
            {
                case (int)swSelectType_e.swSelSKETCHSEGS: return "swSelSKETCHSEGS";
                case (int)swSelectType_e.swSelEDGES: return "swSelEDGES";
                case (int)swSelectType_e.swSelSKETCHES: return "swSelSKETCHES";
                case (int)swSelectType_e.swSelNOTHING: return "swSelNOTHING";
                default: return "?";
            }
        }
    }
}
