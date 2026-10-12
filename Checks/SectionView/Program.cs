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
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }
            var sw = (ISldWorks)raw;

            var doc = (IModelDoc2)sw.ActiveDoc;
            if (doc == null) { Console.Error.WriteLine("No active document."); return 2; }
            if (doc.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                Console.Error.WriteLine("The active document is not a drawing.");
                return 2;
            }
            var drawing = (IDrawingDoc)doc;
            Console.WriteLine("drawing: " + doc.GetTitle());

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
            Console.WriteLine("views on the sheet, before: " + views + " (the sheet included)");
            if (target == null)
            {
                Console.Error.WriteLine("There is no view on the sheet to section.");
                return 1;
            }

            var location = target.Position as double[];
            var outline = target.GetOutline() as double[];
            if (location == null || outline == null || outline.Length < 4)
            {
                Console.Error.WriteLine("Cannot read the position and bounding box of the view.");
                return 1;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "view «{0}» (type {1}) at ({2:0.####}, {3:0.####}) m",
                target.GetName2(), target.Type, location[0], location[1]));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "bounding box on the sheet: x {0:0.####}..{1:0.####}  y {2:0.####}..{3:0.####} m",
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
            if (line == null) { Console.Error.WriteLine("No line, no section."); return 1; }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "line drawn at x={0:0.####}, from y={1:0.####} to y={2:0.####}",
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
            Console.WriteLine("line selected: " + taken +
                ", selected objects: " + selection.GetSelectedObjectCount2(-1) +
                ", type: " + (selection.GetSelectedObjectCount2(-1) > 0
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
            catch (Exception e) { Console.WriteLine("  CreateSectionViewAt5 raised: " + e.Message); }
            Console.WriteLine("CreateSectionViewAt5 answered: " +
                (born == null ? "null" : "an object"));

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
            Console.WriteLine("views on the sheet, after:  " + after);
            Console.WriteLine(after > views
                ? "  -> THE SECTION IS THERE. Last view: «" + last + "»"
                : "  -> no extra view: the section was NOT created");

            // ── THE HATCHING: TWO NUMBERS, AND BEFORE AND AFTER THE RECALCULATION ──
            //
            // Measured on 23/9/2026 because the operation `section_view`
            // refused at its first real use saying «it has no hatching»
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
                { Console.WriteLine("  GetSolidHatchCount (before): " + e.Message); }
                Console.WriteLine("hatching BEFORE the recalculation: return=" + returned1 +
                                  "  out=" + howMany1);
            }

            try { doc.EditRebuild3(); } catch { }

            if (lastView != null)
            {
                int howMany2 = -1, returned2 = -1;
                try { returned2 = lastView.GetSolidHatchCount(out howMany2); } catch (Exception e)
                { Console.WriteLine("  GetSolidHatchCount (after): " + e.Message); }
                Console.WriteLine("hatching AFTER  the recalculation: return=" + returned2 +
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
                Console.WriteLine("FACE hatching:                 " + faces);

                int typ = -1;
                try { typ = lastView.Type; } catch { }
                bool hasSection = false;
                try { hasSection = lastView.GetSection() != null; } catch { }
                int cutLines = -1;
                try { int rt; lastView.GetSectionLineCount2(out rt); cutLines = rt; } catch { }
                Console.WriteLine("view type: " + typ +
                                  "  (swDrawingSectionView = " +
                                  (int)swDrawingViewTypes_e.swDrawingSectionView + ")");
                Console.WriteLine("GetSection() answers: " + (hasSection ? "an object" : "null") +
                                  "   cut lines on the starting view: " + cutLines);
            }

            return after > views ? 0 : 1;
        }
    }
}
