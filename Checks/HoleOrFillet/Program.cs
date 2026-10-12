using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.HoleOrFillet
{
    /// <summary>
    /// IS A CYLINDRICAL FACE A HOLE OR A FILLET? (measurement for entry E14)
    ///
    /// ── WHY THE QUESTION ──────────────────────────────────────────────────
    ///
    /// `References` resolves the role `hole_face` by looking for the cylindrical
    /// faces left by a step: if it finds **one** it returns it, if
    /// it finds **more than one** it refuses.
    ///
    /// Both branches have a problem, and the first is the more serious:
    ///
    ///   * **a fillet leaves cylindrical faces.** If a step leaves
    ///     exactly one, `hole_face` returns it **without saying
    ///     anything** — and whoever asked for the wall of a hole gets back the
    ///     surface of a fillet. A defect that raises no error.
    ///   * four equal holes trigger the refusal, and it is right: «the»
    ///     wall, among four identical ones, does not exist.
    ///
    /// Entry E14 proposed choosing with a criterion instead of refusing.
    /// For the second case it would be guessing. For the FIRST there is a real
    /// criterion, and it is geometric: **the wall of a hole is concave, the surface
    /// of an external fillet is convex.**
    ///
    /// ── WHAT THIS TOOL MEASURES ───────────────────────────────────────────
    ///
    /// It builds a plate with a through hole and a fillet on an
    /// edge, and prints for every cylindrical face what
    /// `FaceInSurfaceSense()` answers and the normal read at a point.
    ///
    ///     Checks\HoleOrFillet\bin\HoleOrFillet.exe
    ///
    /// If the two cases answer the same, the criterion does not exist and entry E14 must be
    /// rewritten. If they answer differently, the number that separates them is below and
    /// must not be deduced.
    ///
    /// It touches no open document: it creates one of its own and closes it.
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            ISldWorks sw;
            try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SOLIDWORKS cannot be attached to: " + ex.Message);
                return 2;
            }

            IModelDoc2 doc = null;
            try
            {
                string model = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                doc = sw.NewDocument(model, 0, 0, 0) as IModelDoc2;
                if (doc == null) { Console.Error.WriteLine("part not created"); return 2; }

                Plate(doc, 0.100, 0.060, 0.020);
                Hole(doc);
                Fillet(doc);

                Console.WriteLine();
                Console.WriteLine("=== is a cylindrical face a hole or a fillet? ===");
                Console.WriteLine();
                Console.WriteLine("  {0,-6} {1,-10} {2,-12} {3,-10} {4}",
                                  "uid", "diameter", "FaceInSurf", "area", "made by");

                var partDoc = doc as IPartDoc;
                var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (bodies == null || bodies.Length == 0) { Console.Error.WriteLine("no body"); return 2; }

                int n = 0, concave = 0, convex = 0;
                var body = bodies[0] as IBody2;
                var f = body.IGetFirstFace();
                while (f != null)
                {
                    double radius;
                    if (Cylindrical(f, out radius))
                    {
                        n++;
                        bool sense = false;
                        try { sense = f.FaceInSurfaceSense(); } catch { }
                        double area = 0;
                        try { area = f.GetArea() * 1e6; } catch { }
                        string madeBy = "";
                        try { madeBy = (f.GetFeature() as IFeature)?.Name ?? ""; } catch { }

                        if (sense) concave++; else convex++;

                        Console.WriteLine("  {0,-6} {1,-10} {2,-12} {3,-10} {4}",
                                          "C" + n,
                                          (radius * 2000).ToString("0.##"),
                                          sense ? "true" : "false",
                                          area.ToString("0.#"),
                                          madeBy);
                    }
                    f = f.IGetNextFace();
                }

                Console.WriteLine();
                Console.WriteLine("  cylindrical faces: " + n +
                                  "   with FaceInSurfaceSense=true: " + concave +
                                  "   =false: " + convex);
                Console.WriteLine();
                Console.WriteLine(n >= 2 && concave >= 1 && convex >= 1
                    ? "THE TWO CASES ANSWER DIFFERENTLY: the criterion exists."
                    : "WARNING: the two cases are NOT separated by this value.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("the check broke: " + ex.GetType().Name +
                                        ": " + ex.Message);
                return 2;
            }
            finally
            {
                if (doc != null) { try { sw.CloseDoc(doc.GetTitle()); } catch { } }
            }
        }

        private static bool Cylindrical(IFace2 face, out double radius)
        {
            radius = 0;
            try
            {
                var s = face.IGetSurface();
                if (s == null || !s.IsCylinder()) return false;
                var par = s.CylinderParams as double[];
                if (par == null || par.Length < 7) return false;
                radius = par[6];
                return radius > 0;
            }
            catch { return false; }
        }

        private static void Plate(IModelDoc2 doc, double l, double w, double h)
        {
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateCornerRectangle(-l / 2, -w / 2, 0, l / 2, w / 2, 0);
            doc.SketchManager.InsertSketch(true);
            doc.FeatureManager.FeatureExtrusion3(
                true, false, false, 0, 0, h, 0, false, false, false, false,
                0, 0, false, false, false, false, true, true, true, 0, 0, false);
        }

        /// <summary>A through hole in the centre of the top face.</summary>
        private static void Hole(IModelDoc2 doc)
        {
            doc.ClearSelection2(true);
            doc.Extension.SelectByRay(0, 0.020, 0, 0, -1, 0, 0.001,
                                      (int)swSelectType_e.swSelFACES, false, 0,
                                      (int)swSelectOption_e.swSelectOptionDefault);
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateCircleByRadius(0, 0, 0, 0.006);
            doc.FeatureManager.FeatureCut4(
                true, false, false, (int)swEndConditions_e.swEndCondThroughAll, 0,
                0.01, 0.01, false, false, false, false, 0, 0,
                false, false, false, false, false, true, true, true, true, false,
                0, 0, false, false);
        }

        /// <summary>A fillet on a vertical edge: CONVEX surface.</summary>
        private static void Fillet(IModelDoc2 doc)
        {
            doc.ClearSelection2(true);
            // The vertical edge at a corner of the 100x60 plate.
            doc.Extension.SelectByRay(0.050, 0.010, 0.030, 0, 0, -1, 0.002,
                                      (int)swSelectType_e.swSelEDGES, false, 0,
                                      (int)swSelectOption_e.swSelectOptionDefault);
            try
            {
                doc.FeatureManager.FeatureFillet3(
                    195, 0.005, 0, 0, 0, 0, 0, null, null, null, null, null, null, null);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("(fillet failed: " + ex.Message + ")");
            }
        }
    }
}
