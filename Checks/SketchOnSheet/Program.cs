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
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }
            var sw = (ISldWorks)raw;

            var doc = (IModelDoc2)sw.ActiveDoc;
            if (doc == null) { Console.Error.WriteLine("No active document."); return 2; }
            if (doc.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                Console.Error.WriteLine(
                    "The active document is «" + doc.GetTitle() + "», which is not a drawing.");
                return 2;
            }
            var drawing = (IDrawingDoc)doc;
            Console.WriteLine("tavola: " + doc.GetTitle());

            var sheet = (ISheet)drawing.GetCurrentSheet();
            Console.WriteLine("foglio: " + sheet.GetName());
            var property = sheet.GetProperties2() as double[];
            if (property != null && property.Length >= 7)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "sheet size: {0:0.###} x {1:0.###} m", property[5], property[6]));

            // 1. HOW MANY ENTITIES THE SHEET SKETCH ALREADY HAS.
            // The starting number is needed: «I have one» means nothing if
            // one does not know how many there were before.
            int before = SheetSegments(sheet);
            Console.WriteLine("segments in the sheet sketch, before: " + Show(before));

            // 2. WE ENTER THE CONTEXT OF THE SHEET.
            Console.WriteLine("EditSheet()...");
            try { drawing.EditSheet(); }
            catch (Exception e) { Console.WriteLine("  EditSheet raised: " + e.Message); }

            // 3. WE DRAW. The coordinates are in METRES and in the system of the
            //    sheet: 0.05 m are 50 mm from the bottom left border.
            var sketches = doc.SketchManager;
            ISketchSegment segment = null;
            try
            {
                segment = sketches.CreateLine(0.05, 0.05, 0.0, 0.15, 0.05, 0.0) as ISketchSegment;
            }
            catch (Exception e) { Console.WriteLine("  CreateLine raised: " + e.Message); }
            // WARNING: `ISketchSegment.GetType()` is NOT that of Object.
            // The interop hides it with a method of its own that returns the SEGMENT
            // TYPE as an integer (0 = line, 1 = arc, 3 = spline). Writing
            // `.Name` after it does not compile, and it is a good thing: if it compiled one
            // would read a class name in place of a line.
            Console.WriteLine("CreateLine answered: " +
                (segment == null ? "null" : "segment of type " + segment.GetType() +
                 (segment.GetType() == 0 ? " (line)" : "")));

            int after = SheetSegments(sheet);
            Console.WriteLine("segments in the sheet sketch, after:  " + Show(after));
            if (before >= 0 && after >= 0)
                Console.WriteLine(after > before
                    ? "  -> the segment ENDED UP in the sheet sketch"
                    : "  -> it did NOT end up in the sheet sketch: it is somewhere else");

            if (segment == null)
            {
                Console.WriteLine();
                Console.WriteLine("Without a segment the selection cannot be tested.");
                return 1;
            }

            // 3b. SO WHERE DID IT END UP?
            // The question is not academic: to take back a line that was not
            // just drawn one must know in which sketch to look for it.
            var active = sketches.ActiveSketch as ISketch;
            Console.WriteLine("active sketch: " + (active == null ? "none" : Name(active)));
            if (active != null)
            {
                var inside = active.GetSketchSegments() as object[];
                Console.WriteLine("  segments of the active sketch: " +
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
                    Console.WriteLine("  view " + n + ": «" + view.GetName2() + "»" +
                        ", type " + view.Type +
                        ", segments in its sketch: " +
                        (segm == null ? "-" : segm.Length.ToString()));
                    view = view.GetNextView() as IView;
                    n++;
                    if (n > 20) break;
                }
            }
            catch (Exception e) { Console.WriteLine("  view loop: " + e.Message); }

            // 4. THE REAL QUESTION: CAN IT BE TAKEN BACK WITHOUT GOING THROUGH A NAME?
            // `CreateSectionViewAt5` wants the line selected, and a name
            // string would be fragile (localised, and different for every sketch).
            var selection = (ISelectionMgr)doc.SelectionManager;
            doc.ClearSelection2(true);
            bool taken = false;
            try { taken = segment.Select4(false, null); }
            catch (Exception e) { Console.WriteLine("  Select4 raised: " + e.Message); }
            Console.WriteLine("Select4(false, null): " + taken);
            Console.WriteLine("selected objects: " +
                selection.GetSelectedObjectCount2(-1));
            if (selection.GetSelectedObjectCount2(-1) > 0)
            {
                int typ = selection.GetSelectedObjectType3(1, -1);
                Console.WriteLine("selected type: " + typ + "  (" + TypeNameOf(typ) + ")");
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
            catch (Exception e) { Console.WriteLine("  Select4 with mark raised: " + e.Message); }
            Console.WriteLine("Select4 with Mark=1: " + withMark +
                ", mark read back: " +
                (selection.GetSelectedObjectCount2(-1) > 0
                    ? selection.GetSelectedObjectMark(1).ToString()
                    : "-"));

            // 6. THE NAME, if there is one. Not to use it — to know whether
            //    it exists and how it is made.
            try
            {
                var entity = segment as IEntity;
                Console.WriteLine("the segment is also an IEntity: " + (entity != null));
            }
            catch { }

            Console.WriteLine();
            Console.WriteLine("The sketch stays open: look at it, then undo if you do not want it.");
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
                return f == null ? "(no name)" : f.Name;
            }
            catch { return "(unreadable name)"; }
        }

        private static string Show(int n)
        {
            return n < 0 ? "- (not counted)" : n.ToString();
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
