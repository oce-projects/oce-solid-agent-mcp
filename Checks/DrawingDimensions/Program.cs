using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.DrawingDimensions
{
    /// <summary>
    /// WHY `InsertModelAnnotations3` BRINGS NO DIMENSION INTO THE DRAWING.
    ///
    /// The part has eleven dimensions, the drawing receives zero, and the API does not say
    /// why: it answers an empty list. Trying to guess the right combination
    /// by modifying the production code and rerunning the bench costs
    /// ten minutes per attempt; here they are all tried in one go and counted
    /// after each one.
    ///
    /// WE COUNT, WE DO NOT LOOK AT THE RETURN VALUE. `InsertModelAnnotations3`
    /// can answer a list and have attached nothing, or answer
    /// empty having worked: the only check is how many annotations the
    /// views have before and after.
    ///
    ///     DrawingDimensions.exe        works on the active drawing
    /// </summary>
    internal static class Program
    {
        private static IDrawingDoc _drawing;
        private static IModelDoc2 _doc;

        private static int Main()
        {
            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }
            var sw = (ISldWorks)raw;

            _doc = (IModelDoc2)sw.ActiveDoc;
            if (_doc == null || _doc.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                Console.Error.WriteLine("An active drawing is needed.");
                return 2;
            }
            _drawing = (IDrawingDoc)_doc;
            Console.WriteLine("drawing: " + _doc.GetTitle());

            // 1. THE VIEWS, AND WHAT THEY ALREADY HAVE.
            var viewNames = new List<string>();
            var v = _drawing.GetFirstView() as IView;
            while (v != null)
            {
                var ann = v.GetAnnotations() as object[];
                var dim = v.GetDisplayDimensions() as object[];
                object referred = null;
                try { referred = v.ReferencedDocument; } catch { }
                Console.WriteLine("  view «" + v.GetName2() + "» type " + v.Type +
                    ", model: «" + (v.GetReferencedModelName() ?? "") + "»" +
                    ", RESOLVED: " + (referred != null) +
                    ", annotations " + (ann == null ? 0 : ann.Length) +
                    ", dimensions " + (dim == null ? 0 : dim.Length));
                if (v.Type != (int)swDrawingViewTypes_e.swDrawingSheet)
                    viewNames.Add(v.GetName2());
                v = v.GetNextView() as IView;
            }
            Console.WriteLine("dimensions on the drawing, before: " + CountDimensions());

            // 2. ARE THE DIMENSIONS OF THE PART MARKED FOR THE DRAWING?
            // It is the question that decides whether the problem is in the drawing or in the
            // part. The referenced model is opened and looked at.
            string modelPath = null;
            var before = _drawing.GetFirstView() as IView;
            while (before != null && modelPath == null)
            {
                var n = before.GetReferencedModelName();
                if (!string.IsNullOrEmpty(n)) modelPath = n;
                before = before.GetNextView() as IView;
            }
            Console.WriteLine("referenced model: " + (modelPath ?? "-"));
            if (modelPath != null) LookAtPartDimensions(sw, modelPath);

            // 3. THE ATTEMPTS.
            Console.WriteLine();
            Console.WriteLine("=== Attempts");

            int fromModel = (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel;
            int marked = (int)swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing;
            int notMarked = (int)swInsertAnnotation_e.swInsertDimensionsNotMarkedForDrawing;
            int generic = (int)swInsertAnnotation_e.swInsertDimensions;

            Test("v3 marked, all the views", () => V3(fromModel, marked, true, false));
            Test("v3 not marked, all the views", () => V3(fromModel, notMarked, true, false));
            Test("v3 generic, all the views", () => V3(fromModel, generic, true, false));
            Test("v3 marked|not marked|generic", () => V3(fromModel, marked | notMarked | generic, true, false));
            Test("v3 generic, position from the sketch", () => V3(fromModel, generic, true, true));

            // The same ones, but with a view ACTIVATED first. It is the main
            // hypothesis: the API says «all the views» but might
            // still want a view context.
            foreach (var itemName in viewNames)
            {
                Test("v3 generic after ActivateView(«" + itemName + "»)", () =>
                {
                    _drawing.ActivateView(itemName);
                    return V3(fromModel, generic, true, false);
                });
                Test("v3 generic on THAT view only", () =>
                {
                    _drawing.ActivateView(itemName);
                    return V3(fromModel, generic, false, false);
                });
            }

            // The view SELECTED by identifier, which is not the same
            // thing as ActivateView: one is the editing context, the other
            // is what the API sees as a selection.
            foreach (var itemName in viewNames)
            {
                Test("v3 generic with the view «" + itemName + "» SELECTED", () =>
                {
                    _doc.ClearSelection2(true);
                    _doc.Extension.SelectByID2(
                        itemName, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                    return V3(fromModel, generic, false, false);
                });
            }

            // ALL THE SOURCE VALUES, one by one.
            // `swImportModelItemsFromEntireModel` is ZERO in this interop,
            // and zero is also what is passed by mistake when a
            // constant does not exist: if the API wanted 1 for «the whole model»,
            // passing it 0 it would answer an empty list without complaining.
            // Nothing is deduced: they are tried.
            for (int source = 0; source <= 4; source++)
            {
                int f = source;
                Test("v3 source=" + f + " marked", () => V3(f, marked, true, false));
                Test("v3 source=" + f + " generic", () => V3(f, generic, true, false));
            }

            // The real values of the constants, because a wrong enum gives zero
            // without saying anything.
            Console.WriteLine("  (source=" + fromModel + " marked=" + marked +
                " notMarked=" + notMarked + " generic=" + generic + ")");

            // THE OLD VERSIONS, which have the parameter `AllTypes`: the one
            // that 3 removed. With AllTypes true the type mask no longer
            // counts, so if the trouble were there it would be seen here.
            Test("v1 AllTypes", () =>
            {
                _drawing.InsertModelAnnotations(fromModel, true, 0, true);
                return -1;
            });
            Test("v2 AllTypes", () =>
            {
                _drawing.InsertModelAnnotations2(fromModel, true, 0, true, false, false);
                return -1;
            });
            Test("v2 AllTypes generic", () =>
            {
                _drawing.InsertModelAnnotations2(fromModel, true, generic, true, false, false);
                return -1;
            });
            Test("v1 source=1 AllTypes", () =>
            {
                _drawing.InsertModelAnnotations(1, true, 0, true);
                return -1;
            });

            Console.WriteLine();
            Console.WriteLine("dimensions on the drawing, at the end: " + CountDimensions());
            // WE RECOUNT IN ANOTHER WAY. If «zero dimensions» came from a wrong
            // count instead of a missed insertion, the number of
            // ANNOTATIONS would say so: they are two different roads to the same
            // question, and two zeros in agreement are worth more than one.
            var u = _drawing.GetFirstView() as IView;
            while (u != null)
            {
                Console.WriteLine("  view «" + u.GetName2() + "»: annotations " +
                    u.GetAnnotationCount() + ", dimensions " +
                    ((u.GetDisplayDimensions() as object[]) == null
                        ? 0 : ((object[])u.GetDisplayDimensions()).Length));
                u = u.GetNextView() as IView;
            }
            return 0;
        }

        private static int V3(int source, int types, bool allViews, bool fromSketch)
        {
            var returned = _drawing.InsertModelAnnotations3(
                source, types, allViews, false, false, fromSketch) as object[];
            return returned == null ? 0 : returned.Length;
        }

        private static void Test(string what, Func<int> that)
        {
            int before = CountDimensions();
            int returnedItems;
            try { returnedItems = that(); }
            catch (Exception e)
            {
                Console.WriteLine("  XX " + what + " -> raised: " + e.Message);
                return;
            }
            int after = CountDimensions();
            string outcome = after > before ? "  ok " : "  -- ";
            Console.WriteLine(outcome + what + " -> returned " +
                (returnedItems < 0 ? "?" : returnedItems.ToString()) +
                ", dimensions on the drawing " + before + " -> " + after);
        }

        /// <summary>How many dimensions the views of the drawing have in all.</summary>
        private static int CountDimensions()
        {
            int n = 0;
            try
            {
                var v = _drawing.GetFirstView() as IView;
                while (v != null)
                {
                    var d = v.GetDisplayDimensions() as object[];
                    if (d != null) n += d.Length;
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return n;
        }

        /// <summary>
        /// The dimensions of the part, and whether they are marked for the drawing.
        ///
        /// It is the question that separates «the drawing does not take them» from «the part does not
        /// offer them», and without an answer we keep modifying the wrong
        /// side.
        /// </summary>
        private static void LookAtPartDimensions(ISldWorks sw, string filePath)
        {
            IModelDoc2 part = null;
            try { part = sw.GetOpenDocumentByName(filePath) as IModelDoc2; }
            catch { }
            if (part == null)
            {
                Console.WriteLine("  (the part does not appear to be open: I cannot look at its dimensions)");
                return;
            }

            int totals = 0, marked = 0;
            try
            {
                var f = part.FirstFeature() as IFeature;
                while (f != null)
                {
                    var d = f.GetFirstDisplayDimension() as IDisplayDimension;
                    while (d != null)
                    {
                        totals++;
                        try { if (d.MarkedForDrawing) marked++; } catch { }
                        d = f.GetNextDisplayDimension(d) as IDisplayDimension;
                    }
                    // also the sketches under the feature
                    var below = f.GetFirstSubFeature() as IFeature;
                    while (below != null)
                    {
                        var ds = below.GetFirstDisplayDimension() as IDisplayDimension;
                        while (ds != null)
                        {
                            totals++;
                            try { if (ds.MarkedForDrawing) marked++; } catch { }
                            ds = below.GetNextDisplayDimension(ds) as IDisplayDimension;
                        }
                        below = below.GetNextSubFeature() as IFeature;
                    }
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch (Exception e) { Console.WriteLine("  dimension loop: " + e.Message); }
            Console.WriteLine("  part dimensions: " + totals + ", of which marked for the drawing: " + marked);
        }
    }
}
