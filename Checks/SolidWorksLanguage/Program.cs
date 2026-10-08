using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Oce.Solid.Agent.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.LanguageProbe
{
    /// <summary>
    /// WHAT SOLIDWORKS WRITES, IN THE LANGUAGE IT RUNS IN (the measurement under
    /// the table of the names, <c>Localization/solidworks-names.json</c>).
    ///
    /// It measures BEFORE the table says anything: for every name the bridge needs
    /// by word it prints the name SOLIDWORKS wrote now, next to what the API gives
    /// without a name (the type, the rotation, the order), so that the same run in
    /// two languages shows which names change and which do not.
    ///
    ///   SolidWorksLanguage.exe --columns the columns of the table in use, on one line (for scripts)
    ///   SolidWorksLanguage.exe native    leave SOLIDWORKS as it is
    ///   SolidWorksLanguage.exe en        the toggle «Use English feature and file names» ON for the run
    ///   SolidWorksLanguage.exe it        the toggle OFF for the run (names in the language of the interface)
    ///
    /// The toggle is put back as it was when the probe ends (also if it fails).
    /// It opens a new part, a new drawing, and closes both by title: it never
    /// touches another document and never asks for the active one.
    ///
    /// Pages of the API guide (7/10/2026): ISldWorks~GetCurrentLanguage,
    /// swUserPreferenceToggle_e (swUseEnglishLanguage, swUseEnglishLanguageFeatureNames,
    /// SO_General.htm), IModelDoc2~GetStandardViewRotation,
    /// IModelDocExtension~GetNamedViewRotation, IModelDoc2~GetModelViewNames,
    /// IBodyFolder~SetAutomaticCutList/UpdateCutList, IEquationMgr~Add3.
    /// </summary>
    internal static class Program
    {
        private static ISldWorks _sw;
        private static readonly List<string> Titles = new List<string>();

        private static void Says(string text) { Console.WriteLine("  " + text); }

        private static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "native";
            try { _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }

            // --columns: only the columns of the table SOLIDWORKS uses now, on one line, for
            // the scripts (Bench/run_plans.js replaces {SW:key} in the plans with them).
            // --english-names on|off: sets the toggle «Use English feature and file names» and says
            // what it reads back; the way to put it back by hand if a bench run was killed.
            if (mode == "--english-names")
            {
                bool want = args.Length > 1 && args[1].ToLowerInvariant() == "on";
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames, want);
                Console.WriteLine("toggle set to " + want + ", read back " +
                    _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames));
                return 0;
            }

            if (mode == "--columns")
            {
                var now = SolidWorksLanguage.Read(_sw);
                Console.WriteLine("feature=" + now.FeatureColumn + " ui=" + now.InterfaceColumn);
                return 0;
            }

            bool? before = null;
            try
            {
                var table = SolidWorksNameTable.Embedded();
                var now = SolidWorksLanguage.Read(_sw);
                Console.WriteLine("== language (mode " + mode + ")");
                Says("GetCurrentLanguage = " + now.Language);
                Says("swUseEnglishLanguage = " + now.EnglishInterface + ", swUseEnglishLanguageFeatureNames = " + now.EnglishFeatureNames);
                Says("columns: interface " + now.InterfaceColumn + ", features " + now.FeatureColumn + (now.Warning != null ? "   WARNING " + now.Warning : ""));

                before = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames);
                if (mode == "en" || mode == "it")
                {
                    _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames, mode == "en");
                    Says("toggle set to " + (mode == "en") + ", read back " +
                         _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames));
                }
                var language = SolidWorksLanguage.Read(_sw);
                Says("columns now: interface " + language.InterfaceColumn + ", features " + language.FeatureColumn);

                Materials();
                var part = NewPart();
                if (part != null)
                {
                    Tree(part, "the new part");
                    ModelViews(part);
                    ModelViewsByShowing(part);
                    Geometry(part);
                    Weldment(part);
                    Equations(part);
                    var drawing = NewDrawing(part);
                }
                Console.WriteLine("== done");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED: " + ex);
                return 1;
            }
            finally
            {
                foreach (var title in Titles)
                {
                    try { _sw.CloseDoc(title); } catch { }
                }
                try { File.Delete(Path.Combine(Path.GetTempPath(), "oce-language-probe.sldprt")); } catch { }
                if (before.HasValue)
                {
                    try
                    {
                        _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames, before.Value);
                        Console.WriteLine("toggle put back to " + before.Value + ", read back " +
                            _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swUseEnglishLanguageFeatureNames));
                    }
                    catch (Exception ex) { Console.WriteLine("COULD NOT PUT THE TOGGLE BACK: " + ex.Message); }
                }
            }
        }

        private static void Materials()
        {
            Console.WriteLine("== material databases (the classes the XML declares)");
            try
            {
                var files = _sw.GetMaterialDatabases() as string[] ?? new string[0];
                foreach (var file in files)
                {
                    var classes = XDocument.Load(file).Descendants()
                        .Where(e => e.Name.LocalName == "classification")
                        .Select(e => (string)e.Attribute("name")).Where(n => n != null).Distinct().Take(6);
                    Says(Path.GetFileName(file) + ": " + string.Join(" | ", classes));
                }
            }
            catch (Exception ex) { Says("unreadable: " + ex.Message); }
        }

        private static IModelDoc2 NewPart()
        {
            Console.WriteLine("== new part");
            string template = _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            Says("template: " + template);
            var doc = (IModelDoc2)_sw.NewDocument(template, 0, 0, 0);
            if (doc == null) { Says("NewDocument -> null"); return null; }
            Titles.Add(doc.GetTitle());
            Says("title: " + doc.GetTitle());
            return doc;
        }

        private static void Tree(IModelDoc2 doc, string what)
        {
            Console.WriteLine("== tree of " + what);
            var f = doc.FirstFeature() as IFeature;
            int n = 0;
            while (f != null && n++ < 80)
            {
                string type = "", name = "";
                try { type = f.GetTypeName2(); name = f.Name; } catch { }
                string extra = "";
                if (type == "RefPlane")
                {
                    double[] normal, origin;
                    var plane = f.GetSpecificFeature2() as IRefPlane;
                    if (References.PlaneFrame(plane, out normal, out origin))
                        extra = "  normal (" + string.Join(", ", normal.Select(v => Math.Round(v, 3))) + ")";
                }
                Says(type.PadRight(22) + " «" + name + "»" + extra);
                f = f.GetNextFeature() as IFeature;
            }
            try
            {
                var config = doc.ConfigurationManager.ActiveConfiguration;
                Says("active configuration: «" + config.Name + "»");
                var all = doc.GetConfigurationNames() as string[] ?? new string[0];
                Says("configurations: " + string.Join(" | ", all.Select(c => "«" + c + "»")));
                var states = config.GetDisplayStates() as string[] ?? new string[0];
                Says("display states of the active one: " + string.Join(" | ", states.Select(c => "«" + c + "»")));
            }
            catch (Exception ex) { Says("configurations unreadable: " + ex.Message); }
        }

        private static void ModelViews(IModelDoc2 doc)
        {
            Console.WriteLine("== named views, with the standard view their ROTATION says they are");
            var names = doc.GetModelViewNames() as string[] ?? new string[0];
            var wanted = new Dictionary<string, double[]>();
            foreach (swStandardViews_e id in Enum.GetValues(typeof(swStandardViews_e)))
                wanted[id.ToString()] = doc.GetStandardViewRotation((int)id) as double[];
            foreach (var kv in wanted)
                Says("standard " + kv.Key + " = " + (kv.Value == null ? "null" : string.Join(", ", kv.Value.Select(v => Math.Round(v, 4)))));
            foreach (var n in names)
            {
                double[] got = null;
                try { got = doc.Extension.GetNamedViewRotation(n) as double[]; } catch { }
                string which = wanted.Where(w => w.Value != null && got != null && w.Value.Length == 9 &&
                                                 Enumerable.Range(0, 9).All(i => Math.Abs(w.Value[i] - got[i]) < 1e-6))
                                     .Select(w => w.Key).FirstOrDefault() ?? "(none)";
                Says("«" + n + "»  ->  " + which + (got == null ? "  (rotation not readable)" : "   [" + string.Join(", ", got.Select(v => Math.Round(v, 4))) + "]"));
            }
        }

        private static double[] Orientation(IModelDoc2 doc)
        {
            try
            {
                var view = doc.ActiveView as IModelView;
                var t = view == null ? null : view.Orientation3 as IMathTransform;
                return t == null ? null : t.ArrayData as double[];
            }
            catch { return null; }
        }

        private static bool Same(double[] a, double[] b)
        {
            return a != null && b != null && a.Length == b.Length && Enumerable.Range(0, a.Length).All(i => Math.Abs(a[i] - b[i]) < 1e-6);
        }

        private static void ModelViewsByShowing(IModelDoc2 doc)
        {
            Console.WriteLine("== named views told by what the model view DOES when shown (Orientation3 after ShowNamedView2)");
            var byId = new Dictionary<string, double[]>();
            var original = Orientation(doc);
            Says("original view data: " + (original == null ? "null" : string.Join(", ", original.Select(v => Math.Round(v, 4)))));
            foreach (swStandardViews_e id in Enum.GetValues(typeof(swStandardViews_e)))
            {
                doc.ShowNamedView2("", (int)id);
                byId[id.ToString()] = Orientation(doc);
                Says("standard " + id + " -> " + (byId[id.ToString()] == null ? "null" : string.Join(", ", byId[id.ToString()].Take(9).Select(v => Math.Round(v, 3)))));
            }
            foreach (var n in (doc.GetModelViewNames() as string[]) ?? new string[0])
            {
                doc.ShowNamedView2(n, -1);
                var o = Orientation(doc);
                string which = byId.Where(w => Same(w.Value, o)).Select(w => w.Key).FirstOrDefault() ?? "(none)";
                Says("«" + n + "»  ->  " + which);
            }
            // Put the view back as it was, from the copy of its data.
            try
            {
                var mu = (IMathUtility)_sw.GetMathUtility();
                var view = (IModelView)doc.ActiveView;
                view.Orientation3 = (MathTransform)mu.CreateTransform(original);
                var again = Orientation(doc);
                Says("view restored from the copy: " + (Same(original, again) ? "IDENTICAL" : "DIFFERENT -> " + (again == null ? "null" : string.Join(", ", again.Select(v => Math.Round(v, 4))))));
            }
            catch (Exception ex) { Says("restore failed: " + ex.Message); }
        }

        private static void Geometry(IModelDoc2 doc)
        {
            Console.WriteLine("== features the program names (a boss, a cut, a shell, an axis, a sketch)");
            var sm = doc.SketchManager;
            doc.ClearSelection2(true);
            if (!PlaneNumber(doc, 1)) { Says("no plane"); return; }
            sm.InsertSketch(true);
            sm.CreateCornerRectangle(-0.050, -0.030, 0, 0.050, 0.030, 0);
            sm.InsertSketch(true);
            var boss = doc.FeatureManager.FeatureExtrusion3(
                true, false, false, 0, 0, 0.020, 0.0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, 0, 0, false) as IFeature;
            Says("boss: " + (boss == null ? "null" : "«" + boss.Name + "»  type " + boss.GetTypeName2()));
            var sketchName = LastOfType(doc, "ProfileFeature");
            Says("its sketch: «" + sketchName + "»");

            doc.ClearSelection2(true);
            if (PlaneNumber(doc, 1))
            {
                sm.InsertSketch(true);
                sm.CreateCircleByRadius(0, 0, 0, 0.008);
                sm.InsertSketch(true);
                doc.ClearSelection2(true);
                doc.Extension.SelectByID2(LastOfType(doc, "ProfileFeature"), "SKETCH", 0, 0, 0, false, 0, null, 0);
                var cut = doc.FeatureManager.FeatureCut4(
                    false, false, false,
                    (int)swEndConditions_e.swEndCondThroughAll, (int)swEndConditions_e.swEndCondThroughAll, 0, 0,
                    false, false, false, false, 0, 0, false, false, false, false,
                    false, true, true, false, false, false,
                    (int)swStartConditions_e.swStartSketchPlane, 0, false, true) as IFeature;
                Says("cut: " + (cut == null ? "null" : "«" + cut.Name + "»  type " + cut.GetTypeName2()));
            }

            // A reference axis through the hole: the cylindrical face is picked by a point on its wall.
            doc.ClearSelection2(true);
            bool picked = doc.Extension.SelectByID2("", "FACE", 0.008, 0, 0.010, false, 0, null, 0);
            if (picked)
            {
                doc.InsertAxis2(true);
                Says("axis: last RefAxis «" + LastOfType(doc, "RefAxis") + "»");
            }
            else Says("axis: the wall of the hole was not picked");

            // A shell on the back face (the one at z = 0 of the model, facing -Z) leaves the hole alone.
            doc.ClearSelection2(true);
            if (doc.Extension.SelectByID2("", "FACE", 0.03, 0.02, 0.020, false, 1, null, 0))
            {
                doc.InsertFeatureShell(0.002, false);
                bool shell = LastOfType(doc, "Shell") != null;
                Says("shell: " + (shell ? "made" : "refused") + ", last feature of type Shell: «" + LastOfType(doc, "Shell") + "»");
            }
            else Says("shell: the face was not picked");
            doc.ClearSelection2(true);
            Tree(doc, "the part after the features");
        }

        private static void Weldment(IModelDoc2 doc)
        {
            Console.WriteLine("== weldment: the folders, the cut list and its properties");
            try
            {
                var part = doc as IPartDoc;
                var feature = doc.FeatureManager.InsertWeldmentFeature() as IFeature;
                Says("InsertWeldmentFeature -> " + (feature == null ? "null" : "«" + feature.Name + "»  type " + feature.GetTypeName2()));
                var f = doc.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (f.GetTypeName2() == "SolidBodyFolder")
                    {
                        var folder = f.GetSpecificFeature2() as IBodyFolder;
                        if (folder != null)
                        {
                            Says("body folder «" + f.Name + "»: automatic " + folder.GetAutomaticCutList());
                            try { folder.SetAutomaticCutList(true); } catch { }
                            try { folder.UpdateCutList(); } catch { }
                        }
                    }
                    f = f.GetNextFeature() as IFeature;
                }
                doc.ForceRebuild3(false);
                f = doc.FirstFeature() as IFeature;
                while (f != null)
                {
                    string type = f.GetTypeName2();
                    if (type == "CutListFolder" || type == "SolidBodyFolder")
                    {
                        Says(type + " «" + f.Name + "»");
                        var cpm = f.CustomPropertyManager;
                        var names = (cpm == null ? null : cpm.GetNames() as object[]) ?? new object[0];
                        foreach (var o in names)
                        {
                            string name = Convert.ToString(o), value, resolved; bool used, link;
                            cpm.Get6(name, false, out value, out resolved, out used, out link);
                            Says("    property «" + name + "» = «" + value + "» -> «" + resolved + "»");
                        }
                    }
                    f = f.GetNextFeature() as IFeature;
                }
                var all = doc.GetConfigurationNames() as string[] ?? new string[0];
                Says("configurations after the weldment: " + string.Join(" | ", all.Select(c => "«" + c + "»")));
            }
            catch (Exception ex) { Says("weldment failed: " + ex.Message); }
        }

        private static void Equations(IModelDoc2 doc)
        {
            Console.WriteLine("== global variables: which names SOLIDWORKS refuses");
            var em = doc.GetEquationMgr();
            // The names the table says SOLIDWORKS reserves in some language, both capitalisations, and one that is free.
            var candidates = new List<string>();
            foreach (var spelling in SolidWorksNameTable.Embedded().All("equation.reserved.thickness"))
                candidates.AddRange(new[] { spelling, char.ToUpperInvariant(spelling[0]) + spelling.Substring(1), char.ToLowerInvariant(spelling[0]) + spelling.Substring(1) });
            candidates.Add("wall");
            foreach (var name in candidates.Distinct())
            {
                int index = -1;
                try { index = em.Add3(-1, "\"" + name + "\" = 5", true, (int)swInConfigurationOpts_e.swAllConfiguration, null); }
                catch { index = -1; }
                Says("«" + name + "» -> " + (index < 0 ? "REFUSED" : "accepted (index " + index + ")"));
                if (index >= 0) { try { em.Delete(index); } catch { } }
            }
        }

        private static object NewDrawing(IModelDoc2 part)
        {
            Console.WriteLine("== new drawing");
            string template = _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
            Says("template: " + template);
            var doc = (IModelDoc2)_sw.NewDocument(template, (int)swDwgPaperSizes_e.swDwgPaperA3size, 0.42, 0.297);
            if (doc == null) { Says("NewDocument -> null"); return null; }
            Titles.Insert(0, doc.GetTitle());
            Says("title: " + doc.GetTitle());
            var drawing = (IDrawingDoc)doc;
            var sheets = drawing.GetSheetNames() as string[] ?? new string[0];
            Says("sheets: " + string.Join(" | ", sheets.Select(s => "«" + s + "»")));

            // The part has to be saved for the views to be created by path: a temp file, deleted at the end.
            string file = Path.Combine(Path.GetTempPath(), "oce-language-probe.sldprt");
            int errors = 0, warnings = 0;
            part.Extension.SaveAs(file, 0, 1, null, ref errors, ref warnings);
            Says("part saved: errors " + errors + ", warnings " + warnings);
            string partName = part.GetPathName();
            foreach (var id in new[] { swStandardViews_e.swFrontView, swStandardViews_e.swIsometricView })
            {
                var wanted = part.GetStandardViewRotation((int)id) as double[];
                string picked = null;
                foreach (var n in (part.GetModelViewNames() as string[]) ?? new string[0])
                {
                    var got = part.Extension.GetNamedViewRotation(n) as double[];
                    if (got != null && wanted != null && Enumerable.Range(0, 9).All(i => Math.Abs(wanted[i] - got[i]) < 1e-6)) { picked = n; break; }
                }
                IView view = null;
                if (picked != null) view = drawing.CreateDrawViewFromModelView3(partName, picked, id == swStandardViews_e.swFrontView ? 0.15 : 0.30, 0.15, 0);
                Says(id + ": by rotation «" + picked + "» -> view " + (view == null ? "null" : "«" + view.GetName2() + "»"));
            }
            Titles.Add(part.GetTitle());
            return drawing;
        }

        private static bool PlaneNumber(IModelDoc2 doc, int which)
        {
            int seen = 0;
            var f = doc.FirstFeature() as IFeature;
            while (f != null)
            {
                if (f.GetTypeName2() == "RefPlane" && ++seen == which) { doc.ClearSelection2(true); return f.Select2(false, 0); }
                f = f.GetNextFeature() as IFeature;
            }
            return false;
        }

        private static string LastOfType(IModelDoc2 doc, string type)
        {
            string last = null;
            var f = doc.FirstFeature() as IFeature;
            while (f != null)
            {
                if (f.GetTypeName2() == type) last = f.Name;
                f = f.GetNextFeature() as IFeature;
            }
            return last;
        }
    }
}
