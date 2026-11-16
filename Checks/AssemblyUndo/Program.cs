using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.AssemblyUndo
{
    /// <summary>
    /// WHAT DOES ONE UNDO TAKE AWAY IN AN ASSEMBLY? (brief R23.)
    ///
    /// On 26/10/2026 (t9) every plan on an assembly answered false to
    /// FinishRecordingUndoObject (insert, mate, delete: the bridge log says «did
    /// not close … answered false»), and «undo» after a mate said the last build
    /// «ran on another document». Before the bridge can withdraw a plan on an
    /// assembly it has to be known what SOLIDWORKS records there.
    ///
    /// Two block parts are made and saved in a folder of the probe (the system
    /// temporary folder), then each way runs on a NEW assembly closed without
    /// saving: insert A at the origin, insert B 100 mm along X, a coincident mate
    /// between their right planes (B moves onto A), each followed by EditRebuild3
    /// as the bridge does (AssemblyOperations, MateOperations). Then one EditUndo2
    /// at a time, and after each: components, mates, where B is.
    ///
    ///   record     the three operations inside Start/FinishRecordingUndoObject,
    ///              as PlanExecutor does; then two undos;
    ///   native     no recording; then three undos, one per operation if each
    ///              operation is one native entry;
    ///   +norebuild no EditRebuild3 after the operations.
    ///
    /// The first run (26/10/2026 night) gave: with EditRebuild3 the recording
    /// answered false and no undo took anything away, not even the native ones;
    /// without it the recording answered true and one undo emptied the
    /// assembly. So the other rebuilds are measured in place of EditRebuild3:
    ///   +force     ForceRebuild3(false)       +forcetop  ForceRebuild3(true)
    ///   +ext1      Extension.Rebuild(swRebuildAll)
    ///   +ext4      Extension.Rebuild(swUpdateMates)
    ///   +ext16     Extension.Rebuild(swUpdateDirtyOnly)
    ///
    ///     AssemblyUndo.exe                 record with every rebuild, and native+norebuild
    ///     AssemblyUndo.exe native          one of them
    /// </summary>
    internal static class Program
    {
        private static ISldWorks _sw;

        private static int Main(string[] arguments)
        {
            var ways = arguments.Length > 0 ? new[] { arguments[0] }
                : new[] { "record", "record+norebuild", "record+force", "record+forcetop", "record+ext1", "record+ext4",
                          "record+ext16", "native+norebuild" };
            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }
            _sw = (ISldWorks)raw;

            string partTemplate = Template((int)swUserPreferenceStringValue_e.swDefaultTemplatePart, ".prtdot");
            string assemblyTemplate = Template((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly, ".asmdot");
            if (partTemplate == null || assemblyTemplate == null) { Console.Error.WriteLine("No part or assembly template found."); return 2; }

            string folder = Path.Combine(Path.GetTempPath(), "AssemblyUndo");
            Directory.CreateDirectory(folder);
            string blockA = Path.Combine(folder, "BLOCK_A.SLDPRT"), blockB = Path.Combine(folder, "BLOCK_B.SLDPRT");
            if (!Block(partTemplate, blockA, 40) || !Block(partTemplate, blockB, 20)) return 2;

            int worst = 0;
            foreach (var way in ways)
            {
                var parts = way.Split('+');
                bool known = parts[0] == "record" || parts[0] == "native";
                for (int i = 1; i < parts.Length; i++) known = known && Array.IndexOf(Rebuilds, parts[i]) >= 0;
                if (!known) { Console.Error.WriteLine("Unknown way: " + way); return 2; }
                var doc = (IModelDoc2)_sw.NewDocument(assemblyTemplate, 0, 0, 0);
                if (doc == null) { Console.Error.WriteLine("NewDocument returned null."); return 2; }
                string title = doc.GetTitle();
                Console.WriteLine("=== " + way + ", assembly " + title);
                try { worst = Math.Max(worst, Run(doc, way, blockA, blockB)); }
                catch (Exception ex) { Console.WriteLine("EXCEPTION " + ex.GetType().Name + ": " + ex.Message); worst = 1; }
                finally { try { _sw.CloseDoc(title); } catch { } }
            }
            foreach (var block in new[] { blockA, blockB })
                try { var open = _sw.GetOpenDocumentByName(block) as IModelDoc2; if (open != null) _sw.CloseDoc(open.GetTitle()); } catch { }
            return worst;
        }

        private static int Run(IModelDoc2 doc, string way, string blockA, string blockB)
        {
            var parts = way.Split('+');
            bool record = parts[0] == "record";
            string kind = parts.Length > 1 ? parts[1] : "edit";
            var assembly = (IAssemblyDoc)doc;
            Console.WriteLine("before: " + State(doc));
            if (record) doc.Extension.StartRecordingUndoObject();

            var a = assembly.AddComponent5(blockA, (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                                           "", false, "", 0, 0, 0) as IComponent2;
            string nameA = a == null ? null : a.Name2;
            Rebuild(doc, kind);
            var b = assembly.AddComponent5(blockB, (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                                           "", false, "", 0.1, 0, 0) as IComponent2;
            string nameB = b == null ? null : b.Name2;
            Rebuild(doc, kind);
            Console.WriteLine("  inserted «" + nameA + "» and «" + nameB + "»: " + State(doc));

            int error = 0;
            doc.ClearSelection2(true);
            bool selected = SelectRightPlane(doc, ByName(assembly, nameA)) && SelectRightPlane(doc, ByName(assembly, nameB));
            if (selected)
            {
                assembly.AddMate5((int)swMateType_e.swMateCOINCIDENT, (int)swMateAlign_e.swMateAlignALIGNED, false,
                                  0, 0, 0, 0, 0, 0, 0, 0, false, false, 0, out error);
                Rebuild(doc, kind);
            }
            doc.ClearSelection2(true);
            Console.WriteLine("  mate: selected " + selected + ", error " + error + " (1 = none): " + State(doc));

            if (record)
            {
                bool finished = false;
                try { finished = doc.Extension.FinishRecordingUndoObject("AssemblyUndo probe"); }
                catch (Exception ex) { Console.WriteLine("  FinishRecordingUndoObject threw: " + ex.Message); }
                Console.WriteLine("  FinishRecordingUndoObject " + finished);
            }

            int undos = record ? 2 : 3;
            for (int i = 1; i <= undos; i++)
            {
                doc.EditUndo2(1);
                Console.WriteLine("after undo " + i + ": " + State(doc));
            }
            return 0;
        }

        private static readonly string[] Rebuilds = { "norebuild", "force", "forcetop", "ext1", "ext4", "ext16" };

        private static void Rebuild(IModelDoc2 doc, string kind)
        {
            switch (kind)
            {
                case "edit": doc.EditRebuild3(); break;
                case "force": doc.ForceRebuild3(false); break;
                case "forcetop": doc.ForceRebuild3(true); break;
                case "ext1": doc.Extension.Rebuild((int)swRebuildOptions_e.swRebuildAll); break;
                case "ext4": doc.Extension.Rebuild((int)swRebuildOptions_e.swUpdateMates); break;
                case "ext16": doc.Extension.Rebuild((int)swRebuildOptions_e.swUpdateDirtyOnly); break;
            }
        }

        /// <summary>Components, mates, and the X of every component origin.</summary>
        private static string State(IModelDoc2 doc)
        {
            var assembly = (IAssemblyDoc)doc;
            var components = assembly.GetComponents(false) as object[];
            var where = new List<string>();
            if (components != null)
                foreach (var o in components)
                {
                    var c = o as IComponent2;
                    if (c == null) continue;
                    double x = double.NaN;
                    try { var t = c.Transform2?.ArrayData as double[]; if (t != null && t.Length > 9) x = t[9] * 1000; } catch { }
                    where.Add(c.Name2 + " x=" + x.ToString("0.###", CultureInfo.InvariantCulture));
                }
            return (components == null ? 0 : components.Length) + " component(s), " + Mates(doc) + " mate(s)" +
                   (where.Count > 0 ? " [" + string.Join(", ", where) + "]" : "");
        }

        private static int Mates(IModelDoc2 doc)
        {
            int n = 0;
            var f = doc.FirstFeature() as IFeature;
            while (f != null)
            {
                string type = "";
                try { type = f.GetTypeName2(); } catch { }
                if (type == "MateGroup")
                {
                    var sub = f.GetFirstSubFeature() as IFeature;
                    while (sub != null) { n++; sub = sub.GetNextSubFeature() as IFeature; }
                }
                f = f.GetNextFeature() as IFeature;
            }
            return n;
        }

        private static IComponent2 ByName(IAssemblyDoc assembly, string name)
        {
            if (name == null) return null;
            var components = assembly.GetComponents(false) as object[];
            if (components == null) return null;
            foreach (var o in components)
            {
                var c = o as IComponent2;
                if (c != null && c.Name2 == name) return c;
            }
            return null;
        }

        /// <summary>The third reference plane of the component (the right plane of the template), added to the selection.</summary>
        private static bool SelectRightPlane(IModelDoc2 doc, IComponent2 component)
        {
            if (component == null) return false;
            int seen = 0;
            var f = component.FirstFeature() as IFeature;
            while (f != null)
            {
                string type = "";
                try { type = f.GetTypeName2(); } catch { }
                if (type == "RefPlane")
                {
                    if (seen == 2) return f.Select2(true, 0);
                    seen++;
                }
                f = f.GetNextFeature() as IFeature;
            }
            return false;
        }

        /// <summary>
        /// A block of the given width, 20 x 10 mm otherwise, saved to
        /// <paramref name="path"/> and LEFT OPEN: AddComponent5 answers null on
        /// a closed file (the first run of this probe; the bridge refuses a
        /// closed file before calling it, AssemblyOperations.IsOpen).
        /// </summary>
        private static bool Block(string template, string path, double widthMm)
        {
            var part = (IModelDoc2)_sw.NewDocument(template, 0, 0, 0);
            if (part == null) { Console.Error.WriteLine("NewDocument (part) returned null."); return false; }
            string title = part.GetTitle();
            bool made = false;
            try
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
                part.SketchManager.CreateCornerRectangle(0, 0, 0, widthMm / 1000, 0.02, 0);
                part.SketchManager.InsertSketch(true);
                var sketch = part.FeatureByPositionReverse(0) as IFeature;
                part.ClearSelection2(true);
                if (sketch == null || !sketch.Select2(false, 0)) { Console.Error.WriteLine("Block sketch not selected."); return false; }
                var extrusion = part.FeatureManager.FeatureExtrusion3(true, false, false,
                    (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                    0.01, 0, false, false, false, false, 0, 0, false, false, false, false,
                    true, true, true, (int)swStartConditions_e.swStartSketchPlane, 0, false);
                if (extrusion == null) { Console.Error.WriteLine("Block extrusion not created."); return false; }
                int errors = 0, warnings = 0;
                bool saved = part.Extension.SaveAs(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                                                   (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
                if (!saved) { Console.Error.WriteLine("Block not saved to " + path + " (code " + errors + ")."); return false; }
                made = true;
                return true;
            }
            finally { if (!made) try { _sw.CloseDoc(title); } catch { } }
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
