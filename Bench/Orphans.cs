using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// THE DIMENSION THAT IS LEFT WITHOUT AN EDGE, SEEN FROM THE STATE (entry Z-A2).
    ///
    /// `IAnnotation.IsDangling` is dead for the dimensions in a drawing out of process:
    /// measured on 24/9/2026 with `Checks/Orphan`, it stays false on a dimension whose
    /// edge was suppressed. From library 0.113.0 the state of the
    /// drawing counts the dimensions attached only to edges that the view no longer
    /// shows (`dimensions_orphan`, `dimensions_orphan_total`).
    ///
    /// Here the two directions, on the same part as the probe: a plate with a
    /// block on top, the dimension on the high edge of the block placed by
    /// `drawing_dimension`, and the state re-read — zero orphans. Then the block is
    /// SUPPRESSED in the part, the drawing is rebuilt, and the state must report one,
    /// with the warning. A declared case cannot do it: after `create_drawing` it follows
    /// the drawing, and does not know how to go back to the part to suppress.
    /// </summary>
    internal static partial class Program
    {
        private static void TestOrphanDimensions()
        {
            Console.WriteLine();
            Console.WriteLine("=== orphans: the dimension left without an edge (Z-A2) ===");
            IModelDoc2 part = null, drawing = null;
            string file = Path.Combine(Expectations.Work, "orphans_bench.sldprt");
            bool asked = true, off = false;
            try
            {
                part = NewPart();
                // Two extrusions from the TOP plane, taken by position and not
                // by name: the name changes with the language of the installation.
                if (Extrusion(part, 0, 0, 0.100, 0.060, 0.010) == null)
                { BenchError("orphans", "the plate did not come out"); return; }
                var block = Extrusion(part, 0.040, 0.020, 0.060, 0.040, 0.020);
                if (block == null) { BenchError("orphans", "the block did not come out"); return; }

                try { if (File.Exists(file)) File.Delete(file); } catch { }
                int e = 0, w = 0;
                part.Extension.SaveAs(file, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e, ref w);
                if (!File.Exists(file)) { BenchError("orphans", "the part cannot be saved (e=" + e + ")"); return; }

                string error;
                Run(part, "create_drawing", new Dictionary<string, object> { ["paper_size"] = "A3" },
                       out error, out drawing);
                if (drawing == null) { BenchError("orphans", "create_drawing: " + error); return; }
                if (Run(drawing, "model_view", new Dictionary<string, object>
                    { ["model"] = file, ["view"] = "front", ["x_mm"] = 150, ["y_mm"] = 150 }, out error) == null)
                { BenchError("orphans", "model_view: " + error); return; }

                // WHICH EDGE: the high one of the block — Y 20 at both ends,
                // along X for 20. It is looked for in the same list that
                // `drawing_dimension` asks of the view, and its index is passed.
                var view = ((IDrawingDoc)drawing).GetFirstView() as IView;
                view = view?.GetNextView() as IView;
                var edges = view?.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
                int index = -1;
                for (int i = 0; edges != null && i < edges.Length && index < 0; i++)
                {
                    var ed = edges[i] as IEdge;
                    var a = (ed?.GetStartVertex() as IVertex)?.GetPoint() as double[];
                    var b = (ed?.GetEndVertex() as IVertex)?.GetPoint() as double[];
                    if (a == null || b == null) continue;
                    if (Math.Abs(a[1] - 0.020) < 1e-6 && Math.Abs(b[1] - 0.020) < 1e-6 &&
                        Math.Abs(Math.Abs(b[0] - a[0]) - 0.020) < 1e-6 && Math.Abs(b[2] - a[2]) < 1e-6)
                        index = i;
                }
                if (index < 0) { BenchError("orphans", "among the visible edges I cannot find the top one of the block"); return; }

                var dimension = Run(drawing, "drawing_dimension",
                    new Dictionary<string, object> { ["edges"] = new object[] { index } }, out error);
                if (dimension == null) { BenchError("orphans", "drawing_dimension: " + error); return; }

                var before = ReadState(drawing);
                Check("orphans: the dimension just put on the block's edge is NOT an orphan",
                      Convert.ToString(before.ContainsKey("dimensions_orphan_total") ? before["dimensions_orphan_total"] : "(missing)") == "0",
                      "dimensions_orphan_total " + Convert.ToString(before.ContainsKey("dimensions_orphan_total") ? before["dimensions_orphan_total"] : "(missing)"));

                // THE RED: the block is suppressed, and the dimension is left hanging.
                bool suppressed = block.SetSuppression2(
                    (int)swFeatureSuppressionAction_e.swSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
                part.EditRebuild3();
                drawing.ForceRebuild3(false);
                var after = ReadState(drawing);
                object orf = after.ContainsKey("dimensions_orphan_total") ? after["dimensions_orphan_total"] : "(missing)";
                Check("orphans: with the block suppressed, the state says ONE orphan dimension, and warns about it",
                      suppressed && Convert.ToString(orf) == "1" && after.ContainsKey("warning_dimensions"),
                      "suppressed " + suppressed + ", dimensions_orphan_total " + Convert.ToString(orf) +
                      ", warning " + (after.ContainsKey("warning_dimensions") ? "yes" : "no"));
            }
            catch (Exception ex) { BenchError("orphans", Why(ex)); }
            finally
            {
                try { if (drawing != null) _sw.CloseDoc(drawing.GetTitle()); } catch { }
                Close(part);
                try { if (File.Exists(file)) File.Delete(file); } catch { }
            }
        }

        /// <summary>A rectangle on the top plane, extruded by `height` meters.</summary>
        private static IFeature Extrusion(IModelDoc2 doc, double x1, double y1, double x2, double y2, double up)
        {
            doc.ClearSelection2(true);
            int seen = 0;
            var f = doc.FirstFeature() as IFeature;
            while (f != null)
            {
                if (f.GetTypeName2() == "RefPlane" && ++seen == 2) { f.Select2(false, 0); break; }
                f = f.GetNextFeature() as IFeature;
            }
            if (seen < 2) return null;
            doc.SketchManager.InsertSketch(true);
            bool before = doc.SketchManager.AddToDB;
            doc.SketchManager.AddToDB = true;
            doc.SketchManager.CreateCornerRectangle(x1, y1, 0, x2, y2, 0);
            doc.SketchManager.AddToDB = before;
            doc.SketchManager.InsertSketch(true);
            return doc.FeatureManager.FeatureExtrusion3(
                true, false, false, 0, 0, up, 0.0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, 0, 0, false) as IFeature;
        }
    }
}
