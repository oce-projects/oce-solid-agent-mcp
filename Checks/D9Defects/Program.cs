using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.D9Defects
{
    /// <summary>
    /// THE FOUR DEFECTS OF THE LIVE PROOF OF 6/10/2026, MEASURED BEFORE THEY ARE FIXED.
    ///
    /// The house method (TODO, method box, and the memory rule «guide first»): the
    /// probe measures the real behaviour BEFORE the code changes, replicating the ORDER
    /// and the POINT of the reads the bridge does. Every subcommand below replicates one.
    ///
    ///   D9Defects.exe tree DOC              defect 1: the cut list folders of the open part
    ///                                       DOC (title or path; never the one in front),
    ///                                       with the two answers the library reads to
    ///                                       recognise them (Type, GetCutListType)
    ///   D9Defects.exe gusset DOC X Z        defect 2: the two faces of the foot at (X, Z)
    ///                                       and the three-point contact test of
    ///                                       WeldmentOperations.LengthThatLiesOn, edge by edge
    ///   D9Defects.exe names                 defect 4, the other half: what CloseDoc matches when it is given a name without
    ///                                       the extension, in another case
    ///   D9Defects.exe closing TOOL.exe      defect 4, the case: runs the given CloseLeftovers.exe on a desk where a
    ///                                       document that was never saved shares its base name with a saved one from
    ///                                       another folder; exits 0 only if the saved one is still open afterwards
    ///   D9Defects.exe count                 defect 3: what GetComponents and
    ///                                       GetComponentCount answer on an EMPTY assembly,
    ///                                       and after the first AddComponent5
    ///   D9Defects.exe close                 defect 4: what ISldWorks.CloseDoc closes when
    ///                                       it is given a title, and when it is given a path
    ///
    /// The part of 1 and 2 is built by build-stand.js (through the bridge: the same
    /// operations, in the same order) and left open; 3 and 4 build their own documents
    /// in a temporary folder of their own, and close them.
    ///
    /// ── GUIDE PAGES READ (sldworksapi.chm, extracted 28/9/2026) ──────────────────
    ///   IBodyFolder~GetCutListType.html, IBodyFolder~Type, swCutListType_e.html
    ///       (swSolidBodyCutList 1, swSheetmetalCutlist 2, swWeldmentCutlist 3),
    ///       swBodyFolderFeatureType_e.html (swWeldmentCutListFolder 5)
    ///   IFace2~GetClosestPointOn.html, IEdge~GetCurve.html, IEdge~GetCurveParams3.html
    ///   IAssemblyDoc~GetComponents.html («Array of IComponent2s»; nothing about an
    ///       empty assembly), IAssemblyDoc~GetComponentCount.html («Number of components»)
    ///   ISldWorks~CloseDoc.html («Name of document (see Remarks)»; the Remarks say
    ///       what an empty string and a name not open do, and do not say whether the
    ///       name is a title or a path)
    ///   ISldWorks~GetOpenDocumentByName.html
    /// </summary>
    internal static class Program
    {
        private static ISldWorks _sw;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static int Main(string[] args)
        {
            try { _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }
            string what = args.Length > 0 ? args[0] : "";
            switch (what)
            {
                case "tree":
                    if (args.Length < 2) { Console.Error.WriteLine("tree <title or path of the open part>"); return 2; }
                    return Tree(args[1]);
                case "gusset":
                    if (args.Length < 4) { Console.Error.WriteLine("gusset <title or path of the open part> X Z (mm)"); return 2; }
                    return GussetContact(args[1], double.Parse(args[2], Inv), double.Parse(args[3], Inv));
                case "count": return Count();
                case "close": return Close();
                case "names": return Names();
                case "closing":
                    if (args.Length < 2) { Console.Error.WriteLine("closing <path of CloseLeftovers.exe>"); return 2; }
                    return Closing(args[1]);
                default:
                    Console.Error.WriteLine("tree DOC | gusset DOC X Z | count | close | names | closing TOOL.exe");
                    return 2;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // DEFECT 1 — which folders does the cut list have, and what do they answer?
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The document the probe reads, BY NAME, never ActiveDoc (review of 6/10/2026,
        /// memory rule «SOLIDWORKS: never the active document»): the session is shared,
        /// and the document in front may be another session's. The bridge holds its own
        /// reference; the probe asks for the one it was told
        /// (ISldWorks~GetOpenDocumentByName.html: «Name of the open document», title or path).
        /// </summary>
        private static IModelDoc2 OpenByName(string name)
        {
            var model = _sw.GetOpenDocumentByName(name) as IModelDoc2;
            if (model == null) Console.Error.WriteLine("no open document named «" + name + "»");
            return model;
        }

        private static int Tree(string name)
        {
            var model = OpenByName(name);
            if (model == null) return 2;
            Console.WriteLine("document: " + model.GetTitle());
            var part = model as IPartDoc;
            Console.WriteLine("IsWeldment: " + part.IsWeldment());
            var bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            Console.WriteLine("solid bodies: " + (bodies == null ? 0 : bodies.Length));

            Console.WriteLine("-- every feature and sub-feature that is a body folder");
            var f = (IFeature)model.FirstFeature();
            while (f != null)
            {
                Describe(f, 0);
                f = (IFeature)f.GetNextFeature();
            }

            // The recognition the library does today (WeldmentOperations.Recognize):
            // Type == swWeldmentCutListFolder AND GetCutListType == swWeldmentCutlist.
            Console.WriteLine("-- the library's recognition (Type 5 and CutListType 3), same walk as CutListFolders");
            var seen = new HashSet<string>();
            int kept = 0, bodiesKept = 0, items = 0, itemBodies = 0;
            var g = part.IFirstFeature();
            while (g != null)
            {
                Count(g, seen, ref kept, ref bodiesKept, ref items, ref itemBodies);
                var below = g.IGetFirstSubFeature();
                while (below != null)
                {
                    Count(below, seen, ref kept, ref bodiesKept, ref items, ref itemBodies);
                    below = below.IGetNextSubFeature();
                }
                g = g.IGetNextFeature();
            }
            Console.WriteLine("  folders kept by the library: " + kept + ", bodies in them: " + bodiesKept);
            Console.WriteLine("  ALL item folders of Type 5 (whatever the cut list type): " + items + ", bodies in them: " + itemBodies);
            return 0;
        }

        private static void Count(IFeature f, HashSet<string> seen, ref int kept, ref int bodiesKept, ref int items, ref int itemBodies)
        {
            IBodyFolder folder = null;
            try { folder = f.GetSpecificFeature2() as IBodyFolder; } catch { }
            if (folder == null) return;
            int type = folder.Type;
            if (type != (int)swBodyFolderFeatureType_e.swWeldmentCutListFolder) return;
            if (!seen.Add(f.Name)) return;
            items++; itemBodies += folder.GetBodyCount();
            if (folder.GetCutListType() == (int)swCutListType_e.swWeldmentCutlist)
            {
                kept++; bodiesKept += folder.GetBodyCount();
            }
        }

        private static void Describe(IFeature f, int depth)
        {
            string typeName = "";
            try { typeName = f.GetTypeName2(); } catch { }
            IBodyFolder folder = null;
            try { folder = f.GetSpecificFeature2() as IBodyFolder; } catch { }
            string pad = new string(' ', 2 + depth * 2);
            if (folder != null)
            {
                string bodyNames = "";
                try
                {
                    var bs = folder.GetBodies() as object[];
                    if (bs != null)
                        foreach (var o in bs) bodyNames += ((IBody2)o).Name + "; ";
                }
                catch { bodyNames = "(unread)"; }
                string props = "";
                try
                {
                    var cpm = f.CustomPropertyManager;
                    foreach (var n in (cpm.GetNames() as object[]) ?? new object[0])
                    {
                        string val, res; bool used, conn;
                        cpm.Get6(Convert.ToString(n), false, out val, out res, out used, out conn);
                        // ALL the properties of the item, with the expression behind each one:
                        // which ones an item that is not a structural member has is part of the finding.
                        props += System.Environment.NewLine + "        " + n + " = «" + res + "»" + ((val ?? "") != res ? "   (expression " + val + ")" : "");
                    }
                }
                catch { props = "(unread)"; }
                Console.WriteLine(pad + "[" + typeName + "] «" + f.Name + "»  Type=" + folder.Type +
                    "  CutListType=" + folder.GetCutListType() + "  bodies=" + folder.GetBodyCount() +
                    "  " + props + " {" + bodyNames + "}");
            }
            IFeature below = null;
            try { below = (IFeature)f.GetFirstSubFeature(); } catch { }
            while (below != null)
            {
                Describe(below, depth + 1);
                below = (IFeature)below.GetNextSubFeature();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // DEFECT 2 — the contact test of the gusset, edge by edge
        // ─────────────────────────────────────────────────────────────────

        private static int GussetContact(string name, double x, double z)
        {
            var model = OpenByName(name);
            if (model == null) return 2;
            var part = model as IPartDoc;
            // THE BAR AND THE FOOT, as the plan of the stand names them: the wall of the foot
            // toward the middle of the bar (flat along z), and the underside of the bar.
            double wz = z < 0 ? z + 20 : z - 20;
            double sx = x < 0 ? x + 16 : x - 16;
            var bodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, false);
            IFace2 wall = null, under = null;
            foreach (IBody2 b in bodies)
            {
                foreach (IFace2 face in (object[])b.GetFaces())
                {
                    var box = face.GetBox() as double[];
                    if (box == null) continue;
                    double area = face.GetArea() * 1e6;
                    double[] n = Normal(face);
                    if (n == null) continue;
                    // wall: normal along z, flat in z at wz, 4800 mm2, containing x = sx
                    if (Math.Abs(n[2]) > 0.99 && Math.Abs(box[2] * 1000 - wz) < 0.5 && Math.Abs(box[5] * 1000 - wz) < 0.5 &&
                        Math.Abs(area - 4800) < 1 && box[0] * 1000 - 0.5 <= sx && sx <= box[3] * 1000 + 0.5)
                        wall = face;
                    // underside of the bar: normal along y, flat at y = -20, wider than 10000 mm2, containing sx
                    if (Math.Abs(n[1]) > 0.99 && Math.Abs(box[1] * 1000 + 20) < 0.5 && Math.Abs(box[4] * 1000 + 20) < 0.5 &&
                        area > 10000 && box[0] * 1000 - 0.5 <= sx && sx <= box[3] * 1000 + 0.5)
                        under = face;
                }
            }
            Console.WriteLine("foot at (" + x + ", " + z + "): wall " + (wall == null ? "NOT FOUND" : Describe(wall)) +
                              " | underside " + (under == null ? "NOT FOUND" : Describe(under)));
            if (wall == null || under == null) return 1;
            double a1 = Lies(wall, under, "wall edges on the underside");
            double a2 = Lies(under, wall, "underside edges on the wall");
            Console.WriteLine("RESULT  t1 (wall on underside) = " + a1.ToString("0.###", Inv) + " mm,  t2 (underside on wall) = " +
                              a2.ToString("0.###", Inv) + " mm  -> the library says " +
                              (Math.Max(a1, a2) < 0.01 ? "«the two faces do not touch»" : "they touch (" + Math.Max(a1, a2).ToString("0.###", Inv) + " mm)"));
            return 0;
        }

        private static string Describe(IFace2 f)
        {
            var box = (double[])f.GetBox();
            var n = Normal(f);
            return "area " + (f.GetArea() * 1e6).ToString("0.#", Inv) + " normal [" + string.Join(",", Array.ConvertAll(n, v => v.ToString("0.##", Inv))) + "] box mm [" +
                   string.Join(",", Array.ConvertAll(box, v => (v * 1000).ToString("0.###", Inv))) + "]";
        }

        private static string Pt(double[] p)
        {
            return (p[0] * 1000).ToString("0.###", Inv) + "," + (p[1] * 1000).ToString("0.###", Inv) + "," + (p[2] * 1000).ToString("0.###", Inv);
        }

        private static double[] Normal(IFace2 face)
        {
            var surface = face.IGetSurface();
            if (surface == null || !surface.IsPlane()) return null;
            var plane = surface.PlaneParams as double[];
            if (plane == null) return null;
            double sign = face.FaceInSurfaceSense() ? -1.0 : 1.0;
            return new[] { plane[0] * sign, plane[1] * sign, plane[2] * sign };
        }

        /// <summary>The same three-point test as WeldmentOperations.LengthThatLiesOn, with a line per edge.</summary>
        private static double Lies(IFace2 face, IFace2 other, string label)
        {
            Console.WriteLine("  " + label);
            double total = 0;
            var edges = face.GetEdges() as object[];
            if (edges == null) { Console.WriteLine("    no edges"); return 0; }
            int i = 0;
            foreach (var o in edges)
            {
                var edge = o as IEdge;
                edge.GetCurve();
                var curve = edge.GetCurve() as ICurve;
                var par = edge.GetCurveParams3() as ICurveParamData;
                double u0 = par.UMinValue, u1 = par.UMaxValue;
                Console.WriteLine("    (edge " + (i + 1) + ": IsLine " + curve.IsLine() + ", u0 " + u0.ToString("0.#####", Inv) + ", u1 " + u1.ToString("0.#####", Inv) +
                                  ", start " + Pt((double[])par.StartPoint) + ", end " + Pt((double[])par.EndPoint) + ", Sense " + par.Sense + ")");
                string line = "    edge " + (++i) + " len " + (curve.GetLength3(u0, u1) * 1000).ToString("0.###", Inv) + " mm; distances (µm) at start/mid/end: ";
                bool lies = true;
                foreach (double t in new[] { u0, (u0 + u1) / 2, u1 })
                {
                    var p = curve.Evaluate2(t, 0) as double[];
                    var q = other.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                    if (q == null) { line += "null "; lies = false; continue; }
                    double d = Math.Sqrt(Math.Pow(p[0] - q[0], 2) + Math.Pow(p[1] - q[1], 2) + Math.Pow(p[2] - q[2], 2));
                    line += (d * 1e6).ToString("0.###", Inv) + " [p " + Pt(p) + " -> q " + Pt(q) + "] ";
                    if (d > 1e-6) lies = false;
                }
                Console.WriteLine(line + (lies ? "  -> LIES" : "  -> no"));
                if (lies) total += curve.GetLength3(u0, u1) * 1000.0;
            }
            return total;
        }

        // ─────────────────────────────────────────────────────────────────
        // DEFECT 3 — the empty assembly
        // ─────────────────────────────────────────────────────────────────

        private static string NewTemplate(int preference, string extension)
        {
            string p = _sw.GetUserPreferenceStringValue(preference);
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            foreach (var f in Directory.GetFiles(@"C:\ProgramData\SolidWorks", "*." + extension, SearchOption.AllDirectories))
            {
                string b = f.ToLowerInvariant();
                if (b.Contains(@"\mbd\") || b.Contains("0001mm") || b.Contains("inch")) continue;
                return f;
            }
            return null;
        }

        /// <summary>A real part on disk to insert: a box, made by COM, saved in the temp folder.</summary>
        private static string MakeBox(string folder, string name)
        {
            string template = NewTemplate((int)swUserPreferenceStringValue_e.swDefaultTemplatePart, "prtdot");
            var doc = (IModelDoc2)_sw.NewDocument(template, 0, 0, 0);
            // the first plane of the tree is the front plane, whatever language its name is in
            var f = (IFeature)doc.FirstFeature();
            while (f != null && f.GetTypeName2() != "RefPlane") f = (IFeature)f.GetNextFeature();
            bool selected = f != null && f.Select2(false, 0);
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateCornerRectangle(0, 0, 0, 0.05, 0.03, 0);
            doc.SketchManager.InsertSketch(true);
            var ft = doc.FeatureManager.FeatureExtrusion2(true, false, false, 0, 0, 0.02, 0, false, false, false, false, 0, 0,
                false, false, false, false, true, true, true, 0, 0, false);
            string path = Path.Combine(folder, name);
            int e = 0, w = 0;
            doc.Extension.SaveAs(path, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e, ref w);
            Console.WriteLine("  box " + name + ": plane selected " + selected + ", feature " + (ft != null) + ", saved " + File.Exists(path) + " (errors " + e + ")");
            return path;
        }

        private static string Report(string label, IAssemblyDoc asm)
        {
            string a;
            try { var v = asm.GetComponents(true) as object[]; a = v == null ? "null" : v.Length.ToString(); }
            catch (Exception ex) { a = "throws " + ex.GetType().Name; }
            string b;
            try { b = asm.GetComponentCount(true).ToString(); }
            catch (Exception ex) { b = "throws " + ex.GetType().Name; }
            string c;
            try { c = asm.GetComponentCount(false).ToString(); }
            catch (Exception ex) { c = "throws " + ex.GetType().Name; }
            return label + ": GetComponents(true) -> " + a + " | GetComponentCount(true) -> " + b + " | GetComponentCount(false) -> " + c;
        }

        private static int Count()
        {
            string folder = Path.Combine(Path.GetTempPath(), "d9-defects-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            Console.WriteLine("temp folder " + folder);
            string box = MakeBox(folder, "box.SLDPRT");
            if (!File.Exists(box)) { Console.WriteLine("no box"); return 1; }

            // THE ORDER OF THE BRIDGE (AssemblyOperations.InsertComponent): the part is open, then
            // the count BEFORE, then AddComponent5, then EditRebuild3, then the count AFTER.
            string asmTemplate = NewTemplate((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly, "asmdot");
            var model = (IModelDoc2)_sw.NewDocument(asmTemplate, 0, 0, 0);
            var asm = (IAssemblyDoc)model;
            Console.WriteLine(Report("empty assembly, before", asm));
            var born = asm.AddComponent5(box, (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                "", false, "", 0, 0, 0) as IComponent2;
            Console.WriteLine("AddComponent5 answered " + (born == null ? "null" : "a component «" + born.Name2 + "»"));
            Console.WriteLine(Report("after AddComponent5", asm));
            model.EditRebuild3();
            Console.WriteLine(Report("after EditRebuild3", asm));

            // the second insertion, for the control: before = 1 must read 1, not -1
            var born2 = asm.AddComponent5(box, (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                "", false, "", 0.1, 0, 0) as IComponent2;
            model.EditRebuild3();
            Console.WriteLine(Report("after the second insertion (" + (born2 == null ? "null" : "ok") + ")", asm));

            List("documents open at the end (titles of the document of the component, too)");
            string asmTitle = model.GetTitle();
            _sw.CloseDoc(asmTitle);
            _sw.CloseDoc(box);
            try { Directory.Delete(folder, true); } catch { }
            return 0;
        }

        // ─────────────────────────────────────────────────────────────────
        // DEFECT 4 — CloseDoc by title or by path
        // ─────────────────────────────────────────────────────────────────

        private static int Close()
        {
            string root = Path.Combine(Path.GetTempPath(), "d9-defects-" + Guid.NewGuid().ToString("N"));
            string a = Path.Combine(root, "a"), b = Path.Combine(root, "b");
            Directory.CreateDirectory(a); Directory.CreateDirectory(b);
            string boxA = MakeBox(a, "twin.SLDPRT");
            // the second part, another name, to be closed alone
            string boxB = MakeBox(b, "other.SLDPRT");
            _sw.CloseDoc(boxA); _sw.CloseDoc(boxB);

            // 1. Can two documents with the same title be open at once? Open twin from a, then
            //    a copy of it from b.
            File.Copy(boxA, Path.Combine(b, "twin.SLDPRT"), true);
            string twinB = Path.Combine(b, "twin.SLDPRT");
            int err = 0, warn = 0;
            var d1 = _sw.OpenDoc6(boxA, (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref err, ref warn);
            Console.WriteLine("open a\\twin: " + (d1 != null) + " err " + err);
            err = 0; warn = 0;
            var d2 = _sw.OpenDoc6(twinB, (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref err, ref warn);
            Console.WriteLine("open b\\twin (same title, other folder): " + (d2 != null) + " err " + err + " (65536 = swFileWithSameTitleAlreadyOpen)");
            List("after the two opens");

            // 2. CloseDoc by the TITLE of the one in b (the title is the same as a's)
            _sw.CloseDoc("twin.SLDPRT");
            List("after CloseDoc(\"twin.SLDPRT\")  [title]");

            // 3. now by the FULL PATH of whichever is left
            var left = (IModelDoc2)_sw.GetFirstDocument();
            while (left != null)
            {
                string p = left.GetPathName();
                if (p.IndexOf("d9-defects", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine("closing by path: " + p);
                    _sw.CloseDoc(p);
                    break;
                }
                left = (IModelDoc2)left.GetNext();
            }
            List("after CloseDoc(<path>)");
            // clean up whatever is ours
            foreach (var p in new[] { boxA, boxB, twinB }) { try { _sw.CloseDoc(p); } catch { } }
            try { Directory.Delete(root, true); } catch { }
            return 0;
        }

        /// <summary>
        /// THE CASE OF DEFECT 4, red on the CloseLeftovers of before, green on the one after.
        ///
        /// The desk: a saved part `T.SLDPRT` in a folder the tool is not told about (somebody
        /// else's document), and a document that was never saved whose title is `T` (ours: it
        /// is what the checks leave open). The tool is run as the bench runs it, closing the
        /// documents never saved and sparing the ones that were there before. The saved part
        /// must still be open afterwards: before the fix CloseDoc("T") took it for the part.
        /// </summary>
        private static int Closing(string tool)
        {
            int a = ClosingUnsaved(tool);
            int b = ClosingDrawing(tool);
            Console.WriteLine("SCENARIOS: unsaved-vs-saved " + (a == 0 ? "green" : "RED") + ", drawing in the folder " + (b == 0 ? "green" : "RED"));
            return a == 0 && b == 0 ? 0 : 1;
        }

        private static int ClosingUnsaved(string tool)
        {
            if (!File.Exists(tool)) { Console.Error.WriteLine("no such tool: " + tool); return 2; }
            var before = new List<string>();
            var d0 = (IModelDoc2)_sw.GetFirstDocument();
            while (d0 != null) { before.Add(d0.GetTitle()); d0 = (IModelDoc2)d0.GetNext(); }

            string root = Path.Combine(Path.GetTempPath(), "d9-defects-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string template = NewTemplate((int)swUserPreferenceStringValue_e.swDefaultTemplatePart, "prtdot");
            try
            {
                // THE DESK. A saved part `seed.SLDPRT` is made first, so it is the FIRST of the open
                // documents (they are listed in the order they were opened). Then a document that
                // was never saved, ours, whose title X is whatever SOLIDWORKS gives it. Then the
                // saved part is saved AGAIN under the name X.SLDPRT: it stays where it is in the
                // list, now with the same base name as ours, and no number had to be guessed.
                string seed = MakeBox(root, "seed.SLDPRT");
                var seedDoc = (IModelDoc2)_sw.GetOpenDocumentByName(seed);
                var ours = (IModelDoc2)_sw.NewDocument(template, 0, 0, 0);
                string ourTitle = ours.GetTitle();
                string foreign = Path.Combine(root, ourTitle + ".SLDPRT");
                int e = 0, w = 0;
                bool renamed = seedDoc.Extension.SaveAs(foreign, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e, ref w);
                Console.WriteLine("ours «" + ourTitle + "»; the saved one renamed to " + Path.GetFileName(foreign) + ": " + renamed + " (errors " + e + ")");
                if (!renamed || !File.Exists(foreign)) { Console.WriteLine("RESULT: the desk could not be built"); return 2; }
                List("the desk before the tool");

                var psi = new System.Diagnostics.ProcessStartInfo(tool,
                    "--close --spare \"" + string.Join(",", before) + "\"")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    Console.WriteLine(p.StandardOutput.ReadToEnd().TrimEnd());
                    Console.WriteLine(p.StandardError.ReadToEnd().TrimEnd());
                    p.WaitForExit();
                }
                List("the desk after the tool");
                bool foreignOpen = false, oursOpen = false;
                var d = (IModelDoc2)_sw.GetFirstDocument();
                while (d != null)
                {
                    if (string.Equals(d.GetPathName(), foreign, StringComparison.OrdinalIgnoreCase)) foreignOpen = true;
                    if (d.GetPathName() == "" && string.Equals(d.GetTitle(), ourTitle, StringComparison.OrdinalIgnoreCase)) oursOpen = true;
                    d = (IModelDoc2)d.GetNext();
                }
                Console.WriteLine("the saved document of somebody else is " + (foreignOpen ? "STILL OPEN" : "CLOSED") +
                                  "; the one that was never saved is " + (oursOpen ? "still open" : "closed"));
                try { _sw.CloseDoc(foreign); } catch { }
                try { if (oursOpen) _sw.CloseDoc(ourTitle); } catch { }
                Console.WriteLine(foreignOpen ? "RESULT: green (the other document was not touched)" : "RESULT: RED (the tool closed a document that was not its own)");
                return foreignOpen ? 0 : 1;
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        /// <summary>
        /// The second desk of defect 4: a SAVED drawing and its part, both in the folder the tool
        /// is told to clean. The tool must close both. A drawing's title is not its file name
        /// (a drawing is titled «name - Sheet1», the sheet included: Bench/TestAssembly.cs, K12-bis
        /// found CloseDoc of such a title closing nothing and saying nothing), so closing by the
        /// title leaves the drawing open, and an open drawing keeps its part open.
        /// </summary>
        private static int ClosingDrawing(string tool)
        {
            var before = new List<string>();
            var d0 = (IModelDoc2)_sw.GetFirstDocument();
            while (d0 != null) { before.Add(d0.GetTitle()); d0 = (IModelDoc2)d0.GetNext(); }
            string root = Path.Combine(Path.GetTempPath(), "d9-defects-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string part = MakeBox(root, "sheet_part.SLDPRT");
                string drawingTemplate = NewTemplate((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing, "drwdot");
                var drawing = (IModelDoc2)_sw.NewDocument(drawingTemplate, 0, 0, 0);
                if (drawing == null) { Console.WriteLine("RESULT: no drawing could be made"); return 2; }
                string drawingPath = Path.Combine(root, "sheet_drawing.SLDDRW");
                int e = 0, w = 0;
                bool saved = drawing.Extension.SaveAs(drawingPath, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e, ref w);
                Console.WriteLine("drawing saved: " + saved + " (errors " + e + "), title now «" + drawing.GetTitle() + "»");
                List("the desk before the tool");
                var psi = new System.Diagnostics.ProcessStartInfo(tool,
                    "--close --spare \"" + string.Join(",", before) + "\" \"" + root + "\"")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    Console.WriteLine(p.StandardOutput.ReadToEnd().TrimEnd());
                    Console.WriteLine(p.StandardError.ReadToEnd().TrimEnd());
                    p.WaitForExit();
                }
                List("the desk after the tool");
                int left = 0;
                var d = (IModelDoc2)_sw.GetFirstDocument();
                while (d != null)
                {
                    if (d.GetPathName().StartsWith(root, StringComparison.OrdinalIgnoreCase)) left++;
                    d = (IModelDoc2)d.GetNext();
                }
                Console.WriteLine("documents of the cleaned folder still open: " + left);
                foreach (var p in new[] { drawingPath, part }) { try { _sw.CloseDoc(p); } catch { } }
                Console.WriteLine(left == 0 ? "RESULT: green (everything of the folder was closed)" : "RESULT: RED (the tool left documents of its folder open)");
                return left == 0 ? 0 : 1;
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        /// <summary>
        /// What does CloseDoc match? A part and an assembly of the same base name, open
        /// together (their titles differ in the extension): is a name without the extension,
        /// or in another case, taken for one of them?
        /// </summary>
        private static int Names()
        {
            string root = Path.Combine(Path.GetTempPath(), "d9-defects-" + Guid.NewGuid().ToString("N"));
            string a = Path.Combine(root, "a"), b = Path.Combine(root, "b");
            Directory.CreateDirectory(a); Directory.CreateDirectory(b);
            string boxA = MakeBox(a, "twin.SLDPRT");
            string asmPath = Path.Combine(b, "twin.SLDASM");
            var asmModel = (IModelDoc2)_sw.NewDocument(NewTemplate((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly, "asmdot"), 0, 0, 0);
            int e = 0, w = 0;
            asmModel.Extension.SaveAs(asmPath, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e, ref w);
            Console.WriteLine("assembly saved: " + File.Exists(asmPath));
            List("both open (the part twin.SLDPRT in folder a, the assembly twin.SLDASM in folder b)");
            // BY PATH: the part is the first of the two in the session's list, the assembly the
            // second. Closing the assembly by its path must leave the part alone, and vice versa.
            _sw.CloseDoc(asmPath);
            List("after CloseDoc(<path of the assembly>)");
            int err = 0, warn = 0;
            _sw.OpenDoc6(asmPath, (int)swDocumentTypes_e.swDocASSEMBLY, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref err, ref warn);
            List("the assembly opened again");
            foreach (var name in new[] { "twin", "TWIN.SLDPRT", "twin.sldasm" })
            {
                Console.WriteLine("CloseDoc(\"" + name + "\")");
                _sw.CloseDoc(name);
                List("after");
            }
            foreach (var p in new[] { boxA, asmPath }) { try { _sw.CloseDoc(p); } catch { } }
            try { Directory.Delete(root, true); } catch { }
            return 0;
        }

        private static void List(string label)
        {
            Console.WriteLine("  " + label + ":");
            var d = (IModelDoc2)_sw.GetFirstDocument();
            int n = 0;
            while (d != null) { Console.WriteLine("    «" + d.GetTitle() + "»  " + d.GetPathName()); n++; d = (IModelDoc2)d.GetNext(); }
            if (n == 0) Console.WriteLine("    (none)");
        }
    }
}
