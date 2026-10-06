using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.ChangeByHand
{
    /// <summary>
    /// CHANGES THE OPEN PART WITHOUT GOING THROUGH THE BRIDGE (entry X2).
    ///
    /// It is used to test a defence that, by definition, cannot be tested
    /// from the inside: «somebody touched the part while the plan was already
    /// written». As long as the change comes from the bridge, the bridge knows it and
    /// updates its signature; the defence only triggers on a change the
    /// bridge knows nothing about. So a second actor is needed, and this is it.
    ///
    /// WHY AN EXECUTABLE AND NOT A SCRIPT. `ISldWorks` by late binding from
    /// PowerShell 5.1 answers TYPE_E_ELEMENTNOTFOUND: PowerShell does not resolve the
    /// methods on the SOLIDWORKS IDispatch. It is the same reason why all
    /// the COM probes of this project are compiled C#, and it is also written in
    /// `Checks\NewPart`.
    ///
    /// WHAT IT DOES: it extrudes a small block on the first reference plane of the
    /// active document. It is not «pulling a dimension», and it is honest to say so: it is a
    /// change to the geometry that changes the volume and adds a
    /// feature, that is exactly the two things the light signature looks at.
    /// A dimension pulled by hand would change the volume and nothing else, and would be a
    /// WEAKER case than this one.
    ///
    /// IT DOES NOT SAVE. The document stays dirty, as a person would leave it
    /// who touched something and has not saved yet.
    ///
    ///     ChangeByHand.exe            a 10x10x10 block
    ///     ChangeByHand.exe 25         a 25x25x25 block
    ///
    /// Exits 0 if it changed something, 1 if it did not manage to - and in that
    /// case it says so, because a probe that fails silently would make green the
    /// defence it was supposed to put to the test.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            double side = 10;
            if (arguments.Length > 0 &&
                !double.TryParse(arguments[0],
                                 System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture,
                                 out side))
            {
                Console.WriteLine("the side is written in millimeters, for example 25");
                return 1;
            }

            ISldWorks sw;
            try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (Exception ex)
            {
                Console.WriteLine("I am not attaching to SOLIDWORKS: " + ex.Message);
                return 1;
            }

            var doc = (IModelDoc2)sw.ActiveDoc;
            if (doc == null)
            {
                Console.WriteLine("no active document: there is nothing to change.");
                return 1;
            }
            if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                Console.WriteLine("the active document is not a part.");
                return 1;
            }

            Console.WriteLine("document: " + doc.GetTitle());

            // THE VOLUME BEFORE AND AFTER, because «I called the extrusion» is not
            // «the part has changed». It is the same rule the bridge applies
            // to every operation: we read back.
            double before = Volume(doc);

            IFeature plane = null;
            try
            {
                var tree = doc.FeatureManager.GetFeatures(false) as object[];
                if (tree != null)
                {
                    foreach (var o in tree)
                    {
                        var f = o as IFeature;
                        if (f == null) continue;
                        try { if (f.GetTypeName2() == "RefPlane") { plane = f; break; } }
                        catch { }
                    }
                }
            }
            catch { }

            if (plane == null)
            {
                Console.WriteLine("the document has no reference planes.");
                return 1;
            }

            try
            {
                doc.ClearSelection2(true);
                plane.Select2(false, 0);
                var sm = doc.SketchManager;
                sm.InsertSketch(true);
                // Away from the origin, so it is one more body and not a cut.
                sm.CreateCenterRectangle(side / 1000.0, side / 1000.0, 0,
                                         side / 1000.0 + side / 2000.0,
                                         side / 1000.0 + side / 2000.0, 0);
                sm.InsertSketch(true);
                doc.FeatureManager.FeatureExtrusion3(
                    true, false, false, (int)swEndConditions_e.swEndCondBlind, 0,
                    side / 1000.0, 0, false, false, false, false, 0, 0,
                    false, false, false, false, true, true, true, 0, 0, false);
                doc.ClearSelection2(true);
                doc.EditRebuild3();
            }
            catch (Exception ex)
            {
                Console.WriteLine("the extrusion blew up: " + ex.Message);
                return 1;
            }

            double after = Volume(doc);
            Console.WriteLine("volume: " + before.ToString("0.#") + " -> " + after.ToString("0.#") + " mm3");

            if (Math.Abs(after - before) < 1e-6)
            {
                Console.WriteLine("NOTHING CHANGED: the probe did not do its job.");
                return 1;
            }
            return 0;
        }

        /// <summary>The volume in mm3, or 0 if it cannot be read.</summary>
        private static double Volume(IModelDoc2 doc)
        {
            try
            {
                var p = doc.Extension.CreateMassProperty();
                if (p == null) return 0;
                return p.Volume * 1e9;
            }
            catch { return 0; }
        }
    }
}
