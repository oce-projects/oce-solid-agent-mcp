using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.UndoGaps
{
    /// <summary>
    /// TWO PLANS WHOSE UNDO ENTRY CLOSED EMPTY (scenarios t9b and t14,
    /// 27/10/2026).
    ///
    /// «parameters» wrote two global variables and «place_component» moved a
    /// component, and both answered «undo_entry_recorded» false. After
    /// «parameters» the undo withdrew the plan BEFORE it (the extrusion went,
    /// the equations stayed), because the snapshot of the bridge does not see
    /// equations. Before deciding what the bridge does with them, it has to be
    /// known whether SOLIDWORKS records them at all, and with which call.
    ///
    /// Each way runs on a NEW document closed without saving, inside one
    /// Start/FinishRecordingUndoObject as PlanExecutor does, then ONE EditUndo2:
    ///
    ///   equations+edit   on a block 20 x 20 x 10: IEquationMgr.Add3 of
    ///                    «"wall" = 12mm» and of «"D1@<extrusion>" = "wall"»,
    ///                    EvaluateAll, then EditRebuild3
    ///   equations+force  the same, ForceRebuild3(false) (what «parameters» does)
    ///   equations+none   the same, no rebuild inside the recording
    ///   move+transform   two blocks in an assembly, the second (floating) moved
    ///                    from x = 100 to x = 200 mm by setting IComponent2.Transform2
    ///                    (what «place_component» does), then Extension.Rebuild
    ///   move+solve       the same with IComponent2.SetTransformAndSolve2
    ///   move+drag        the same with IDragOperator (UseAbsoluteTransform,
    ///                    AddComponent, BeginDrag, Drag, EndDrag): the guide
    ///                    (IComponent2~SetTransformAndSolve2.html, Remarks) names it
    ///                    as the efficient way to move a component
    ///   move+dragturn    the drag with TransformType 2 (General) and the
    ///                    component also turned 180 degrees about X: the rotation
    ///                    of «place_component» (added after the first run, where
    ///                    the drag alone held the undo)
    ///
    ///     UndoGaps.exe                  every way
    ///     UndoGaps.exe move+drag        one of them
    /// </summary>
    internal static class Program
    {
        private static ISldWorks _sw;
        private static readonly string[] Ways =
        {
            "equations+edit", "equations+force", "equations+none", "move+transform", "move+solve", "move+drag",
            "move+dragturn",
        };

        private static int Main(string[] arguments)
        {
            var ways = arguments.Length > 0 ? new[] { arguments[0] } : Ways;
            foreach (var way in ways)
                if (Array.IndexOf(Ways, way) < 0) { Console.Error.WriteLine("Unknown way: " + way); return 2; }
            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }
            _sw = (ISldWorks)raw;

            string partTemplate = Template((int)swUserPreferenceStringValue_e.swDefaultTemplatePart, ".prtdot");
            string assemblyTemplate = Template((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly, ".asmdot");
            if (partTemplate == null || assemblyTemplate == null) { Console.Error.WriteLine("No part or assembly template found."); return 2; }

            string folder = Path.Combine(Path.GetTempPath(), "UndoGaps");
            Directory.CreateDirectory(folder);
            string blockA = Path.Combine(folder, "GAP_BLOCK_A.SLDPRT"), blockB = Path.Combine(folder, "GAP_BLOCK_B.SLDPRT");
            bool blocksMade = false;

            int worst = 0;
            foreach (var way in ways)
            {
                bool equations = way.StartsWith("equations", StringComparison.Ordinal);
                if (!equations && !blocksMade)
                {
                    if (!SavedBlock(partTemplate, blockA) || !SavedBlock(partTemplate, blockB)) return 2;
                    blocksMade = true;
                }
                var doc = (IModelDoc2)_sw.NewDocument(equations ? partTemplate : assemblyTemplate, 0, 0, 0);
                if (doc == null) { Console.Error.WriteLine("NewDocument returned null."); return 2; }
                string title = doc.GetTitle();
                Console.WriteLine("=== " + way + ", document " + title);
                try { worst = Math.Max(worst, equations ? Equations(doc, way.Split('+')[1]) : Move(doc, way.Split('+')[1], blockA, blockB)); }
                catch (Exception ex) { Console.WriteLine("EXCEPTION " + ex.GetType().Name + ": " + ex.Message); worst = 1; }
                finally { try { _sw.CloseDoc(title); } catch { } }
            }
            foreach (var block in new[] { blockA, blockB })
                try { var open = _sw.GetOpenDocumentByName(block) as IModelDoc2; if (open != null) _sw.CloseDoc(open.GetTitle()); } catch { }
            return worst;
        }

        private static int Equations(IModelDoc2 doc, string rebuild)
        {
            IFeature extrusion = Block(doc);
            if (extrusion == null) { Console.WriteLine("  block not made"); return 1; }
            var em = doc.GetEquationMgr() as IEquationMgr;
            if (em == null) { Console.WriteLine("  no equation manager"); return 1; }
            Console.WriteLine("before: " + EquationState(doc, em, extrusion));

            doc.Extension.StartRecordingUndoObject();
            int first = em.Add3(-1, "\"wall\" = 12mm", true, (int)swInConfigurationOpts_e.swAllConfiguration, null);
            int second = em.Add3(-1, "\"D1@" + extrusion.Name + "\" = \"wall\"", true, (int)swInConfigurationOpts_e.swAllConfiguration, null);
            em.EvaluateAll();
            if (rebuild == "edit") doc.EditRebuild3();
            else if (rebuild == "force") doc.ForceRebuild3(false);
            bool finished = Finish(doc);
            Console.WriteLine("  Add3 answered " + first + " and " + second + ", FinishRecordingUndoObject " + finished +
                              ": " + EquationState(doc, em, extrusion));

            doc.EditUndo2(1);
            doc.EditRebuild3();
            Console.WriteLine("after ONE undo: " + EquationState(doc, em, extrusion));
            return 0;
        }

        private static int Move(IModelDoc2 doc, string how, string blockA, string blockB)
        {
            var assembly = (IAssemblyDoc)doc;
            int option = (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig;
            var a = assembly.AddComponent5(blockA, option, "", false, "", 0, 0, 0) as IComponent2;
            var b = assembly.AddComponent5(blockB, option, "", false, "", 0.1, 0, 0) as IComponent2;
            doc.Extension.Rebuild((int)swRebuildOptions_e.swRebuildAll);
            if (a == null || b == null) { Console.WriteLine("  components not inserted"); return 1; }
            Console.WriteLine("before: " + X(b) + ", fixed " + b.IsFixed());
            double startMm = XMm(b);

            var math = _sw.GetMathUtility() as IMathUtility;
            var data = b.Transform2?.ArrayData as double[];
            if (math == null || data == null || data.Length < 16) { Console.WriteLine("  transform not read"); return 1; }
            var moved = (double[])data.Clone();
            moved[9] = 0.2;
            if (how == "dragturn")
            {
                var turned = new double[] { 1, 0, 0, 0, -1, 0, 0, 0, -1 };
                for (int i = 0; i < 9; i++) moved[i] = turned[i];
            }
            var target = math.CreateTransform(moved) as IMathTransform;

            doc.Extension.StartRecordingUndoObject();
            bool answered = false;
            switch (how)
            {
                case "transform":
                    b.Transform2 = (MathTransform)target;
                    answered = true;
                    break;
                case "solve":
                    answered = b.SetTransformAndSolve2((MathTransform)target);
                    break;
                case "drag":
                case "dragturn":
                    var drag = assembly.GetDragOperator() as IDragOperator;
                    if (drag == null) { Console.WriteLine("  no drag operator"); break; }
                    drag.UseAbsoluteTransform = true;
                    if (how == "dragturn") drag.TransformType = 2;
                    bool added = drag.AddComponent(b, false);
                    bool begun = drag.BeginDrag();
                    bool dragged = drag.Drag(target);
                    bool ended = drag.EndDrag();
                    Console.WriteLine("  drag: AddComponent " + added + ", BeginDrag " + begun + ", Drag " + dragged + ", EndDrag " + ended);
                    answered = dragged;
                    break;
            }
            doc.Extension.Rebuild((int)swRebuildOptions_e.swRebuildAll);
            bool finished = Finish(doc);
            Console.WriteLine("  the move answered " + answered + ", FinishRecordingUndoObject " + finished + ": " + X(b));

            doc.EditUndo2(1);
            doc.Extension.Rebuild((int)swRebuildOptions_e.swRebuildAll);
            Console.WriteLine("after ONE undo: " + X(b) + (Math.Abs(XMm(b) - startMm) < 0.001 ? "  -> back where it was" : "  -> NOT back"));
            return 0;
        }

        private static bool Finish(IModelDoc2 doc)
        {
            try { return doc.Extension.FinishRecordingUndoObject("UndoGaps probe"); }
            catch (Exception ex) { Console.WriteLine("  FinishRecordingUndoObject threw: " + ex.Message); return false; }
        }

        private static string EquationState(IModelDoc2 doc, IEquationMgr em, IFeature extrusion)
        {
            var depth = extrusion.Parameter("D1") as IDimension;
            double mm = depth == null ? double.NaN : depth.SystemValue * 1000;
            return em.GetCount() + " equation(s), depth " + mm.ToString("0.###", CultureInfo.InvariantCulture) + " mm, features " +
                   doc.GetFeatureCount();
        }

        private static double XMm(IComponent2 c)
        {
            try { var t = c.Transform2?.ArrayData as double[]; if (t != null && t.Length > 9) return t[9] * 1000; } catch { }
            return double.NaN;
        }

        private static string X(IComponent2 c)
        {
            string axes = "?";
            try
            {
                var t = c.Transform2?.ArrayData as double[];
                if (t != null && t.Length > 8)
                    axes = "y=(" + t[3].ToString("0.###", CultureInfo.InvariantCulture) + "," + t[4].ToString("0.###", CultureInfo.InvariantCulture) +
                           "," + t[5].ToString("0.###", CultureInfo.InvariantCulture) + ")";
            }
            catch { }
            return c.Name2 + " x=" + XMm(c).ToString("0.###", CultureInfo.InvariantCulture) + " mm " + axes;
        }

        /// <summary>A block 20 x 20 x 10 mm on the first plane of the part; its extrusion, or null.</summary>
        private static IFeature Block(IModelDoc2 part)
        {
            part.ClearSelection2(true);
            var f = part.FirstFeature() as IFeature;
            while (f != null)
            {
                string type = "";
                try { type = f.GetTypeName2(); } catch { }
                if (type == "RefPlane") { f.Select2(false, 0); break; }
                f = f.GetNextFeature() as IFeature;
            }
            part.SketchManager.InsertSketch(true);
            part.SketchManager.CreateCornerRectangle(0, 0, 0, 0.02, 0.02, 0);
            part.SketchManager.InsertSketch(true);
            var sketch = part.FeatureByPositionReverse(0) as IFeature;
            part.ClearSelection2(true);
            if (sketch == null || !sketch.Select2(false, 0)) return null;
            return part.FeatureManager.FeatureExtrusion3(true, false, false,
                (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                0.01, 0, false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, (int)swStartConditions_e.swStartSketchPlane, 0, false) as IFeature;
        }

        /// <summary>
        /// A block saved to <paramref name="path"/> and LEFT OPEN: AddComponent5
        /// answers null on a closed file (Checks\AssemblyUndo).
        /// </summary>
        private static bool SavedBlock(string template, string path)
        {
            var part = (IModelDoc2)_sw.NewDocument(template, 0, 0, 0);
            if (part == null) { Console.Error.WriteLine("NewDocument (part) returned null."); return false; }
            string title = part.GetTitle();
            if (Block(part) == null) { Console.Error.WriteLine("Block not made."); try { _sw.CloseDoc(title); } catch { } return false; }
            int errors = 0, warnings = 0;
            bool saved = part.Extension.SaveAs(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                                               (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
            if (!saved) { Console.Error.WriteLine("Block not saved to " + path + " (code " + errors + ")."); try { _sw.CloseDoc(title); } catch { } }
            return saved;
        }

        private static string Template(int preference, string extension)
        {
            try
            {
                var p = _sw.GetUserPreferenceStringValue(preference);
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            }
            catch { }
            var its = System.Environment.GetEnvironmentVariable("OCE_DOCUMENT_TEMPLATES");
            foreach (var folder in new[] { @"C:\ProgramData\SolidWorks", its })
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
                foreach (var f in Directory.GetFiles(folder, "*" + extension, SearchOption.AllDirectories))
                {
                    string b = f.ToLowerInvariant();
                    if (b.Contains(@"\mbd\") || b.Contains("0001mm") || b.Contains("inch")) continue;
                    return f;
                }
            }
            return null;
        }
    }
}
