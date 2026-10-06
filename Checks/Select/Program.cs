using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.Select
{
    /// <summary>
    /// SELECTS SOMETHING IN THE PART, as a finger on the screen would.
    ///
    /// It is used to test that the state can say **what is selected**: the
    /// criterion of entry E9 speaks of a click in the interface, and a click
    /// cannot be written in a bench. What can be done — and which tests the
    /// same road — is to select by API and then ask the state what it is
    /// called: `ISelectionMgr` does not know where the selection comes from.
    ///
    ///     Select.exe faccia 1        the first face of the first body
    ///     Select.exe spigolo 1       the first edge
    ///     Select.exe corpo 1         the first body
    ///     Select.exe niente          empties the selection
    ///
    /// The numbers are those of the order in which SOLIDWORKS gives the entities, NOT
    /// the identifiers of the state: they are two different numberings, and it is
    /// precisely the difference that this check serves to measure.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            // `--aggiungi` does not empty the previous selection: it serves to test
            // the case that entry E9 named as a risk — several entities
            // selected together — which otherwise cannot be built.
            bool append = false;
            var clean = new System.Collections.Generic.List<string>();
            foreach (var a in arguments)
            {
                if (a == "--aggiungi") append = true;
                else clean.Add(a);
            }
            arguments = clean.ToArray();

            string what = arguments.Length > 0 ? arguments[0] : "faccia";
            int which = arguments.Length > 1 && int.TryParse(arguments[1], out var n) ? n : 1;

            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }
            var sw = (ISldWorks)raw;

            var doc = (IModelDoc2)sw.ActiveDoc;
            if (doc == null) { Console.Error.WriteLine("No active document."); return 2; }
            if (!append) doc.ClearSelection2(true);
            if (what == "nothing")
            {
                doc.ClearSelection2(true);
                Console.WriteLine("selection emptied");
                return 0;
            }

            var partDoc = doc as IPartDoc;
            if (partDoc == null) { Console.Error.WriteLine("It is not a part."); return 2; }
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            if (bodies == null || bodies.Length == 0)
            {
                Console.Error.WriteLine("The part has no solid bodies.");
                return 1;
            }

            var body = bodies[Math.Min(which, bodies.Length) - 1] as IBody2;
            bool taken = false;
            switch (what)
            {
                case "body":
                    try { taken = body.Select2(append, null); } catch { }
                    break;
                case "edge":
                {
                    var edges = body.GetEdges() as object[];
                    if (edges != null && edges.Length >= which)
                    {
                        var e = edges[which - 1] as IEntity;
                        try { taken = e.Select4(append, null); } catch { }
                    }
                    break;
                }
                default:
                {
                    var faces = body.GetFaces() as object[];
                    if (faces != null && faces.Length >= which)
                    {
                        var f = faces[which - 1] as IEntity;
                        try { taken = f.Select4(append, null); } catch { }
                    }
                    break;
                }
            }

            var selection = (ISelectionMgr)doc.SelectionManager;
            Console.WriteLine("selected: " + taken +
                ", selected objects: " + selection.GetSelectedObjectCount2(-1));
            return taken ? 0 : 1;
        }
    }
}
