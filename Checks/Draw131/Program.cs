using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.Draw131
{
    /// <summary>
    /// THE L BRACKET DRAWING OF 12/10/2026 (items 1 to 6 of the drawing branch of 0.131.0),
    /// MEASURED BEFORE ANY CODE IS CHANGED.
    ///
    /// The sheet of the proof session (drawtest2/L_BRACKET.SLDDRW, sheet metal part
    /// L_BRACKET.SLDPRT: base flange 120 x 70 x 60, thickness 8, bend radius 8, chamfer
    /// 1 x 45 on the whole outline, two holes D11, one slot) is OPENED FROM A COPY, and
    /// what the bridge reads from it is printed as the bridge would read it: no document
    /// is saved, the document reference is kept (never ActiveDoc), and the documents
    /// the probe opened are closed at the end.
    ///
    ///     Draw131.exe edges &lt;drawing.SLDDRW&gt; [viewNumber]
    ///         every edge of every view: kind, ends in the model and on the sheet, the two
    ///         faces it separates (surface, feature), and the verdict the bridge gives it
    ///         (chamfer edge? chamfer leg?). Answers item 1: WHY every edge of this part is
    ///         taken for «a chamfer edge».
    ///     Draw131.exe dims &lt;drawing.SLDDRW&gt;
    ///         every annotation of every view with its type, position, text, attached
    ///         entities, and the dimensions of the PART with their «marked for drawing» flag.
    ///         Answers items 2, 3 and 5.
    ///
    /// Guide pages used (api guide, sldworksapi.chm): IView~GetVisibleEntities2,
    /// IEdge~GetTwoAdjacentFaces2, IEdge~GetCurveParams3, IView~ModelToViewTransform,
    /// IView~GetAnnotations, IAnnotation~GetType, IAnnotation~GetAttachedEntities3,
    /// IDisplayDimension~MarkedForDrawing, IDimension~GetType.
    /// </summary>
    internal static class Program
    {
        private static ISldWorks _sw;
        private static readonly List<string> _openedByMe = new List<string>();
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
        private static StreamWriter _out;

        private static void Say(string s)
        {
            Console.WriteLine(s);
            if (_out != null) { _out.WriteLine(s); _out.Flush(); }
        }

        private static string Mm(double m) { return (m * 1000.0).ToString("0.###", Ci); }
        private static string P(double[] p, int n)
        {
            if (p == null) return "null";
            var sb = new StringBuilder("(");
            for (int i = 0; i < n && i < p.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(Mm(p[i])); }
            return sb.Append(")").ToString();
        }

        private static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("usage: Draw131 edges|dims <drawing.SLDDRW> [view]");
                return 2;
            }
            try { _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (Exception ex) { Console.WriteLine("SOLIDWORKS does not respond: " + ex.Message); return 2; }
            string log = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])), "draw131-" + args[0] + ".txt");
            _out = new StreamWriter(log, false, new UTF8Encoding(false));
            SnapshotOpen();
            int rc = 0;
            IModelDoc2 doc = null;
            try
            {
                doc = Open(args[1]);
                if (doc == null) { Say("cannot open " + args[1]); return 3; }
                var drawing = doc as IDrawingDoc;
                if (drawing == null) { Say("not a drawing"); return 3; }
                switch (args[0])
                {
                    case "edges": Edges(drawing, args.Length > 2 ? int.Parse(args[2]) : 0); break;
                    case "dims": Dims(doc, drawing); break;
                    case "attempt": Attempt(doc, drawing, args.Length > 2 ? int.Parse(args[2]) : 1); break;
                    case "chamfer": ChamferTry(doc, drawing, args.Length > 2 ? int.Parse(args[2]) : 1); break;
                    case "marks": MarkExperiment(doc, drawing); break;
                    case "lines": DimLines(drawing); break;
                    case "segs": Segments(drawing); break;
                    case "delsegs": DeleteSegments(doc, drawing); break;
                    case "pick": PickLines(doc, drawing); break;
                    case "survey":
                        Edges(drawing, 0); Dims(doc, drawing);
                        Guarded("dimension lines", () => DimLines(drawing));
                        Guarded("chamfer V1", () => ChamferTry(doc, drawing, 1));
                        Guarded("chamfer V3", () => ChamferTry(doc, drawing, 3));
                        Guarded("attempt V1", () => Attempt(doc, drawing, 1));
                        Guarded("marks", () => MarkExperiment(doc, drawing));
                        break;
                    default: Say("unknown mode"); rc = 2; break;
                }
            }
            catch (Exception ex) { Say("EXCEPTION " + ex); rc = 4; }
            finally
            {
                // «--keep»: the documents stay open (the probe is called between two calls of the bridge, which works on them)
                if (Array.IndexOf(args, "--keep") < 0) CloseMine();
                if (_out != null) _out.Dispose();
            }
            return rc;
        }

        private static IModelDoc2 Open(string path)
        {
            path = Path.GetFullPath(path);
            string folder = Path.GetDirectoryName(path);
            int type = path.EndsWith(".SLDDRW", StringComparison.OrdinalIgnoreCase) ? (int)swDocumentTypes_e.swDocDRAWING
                     : path.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase) ? (int)swDocumentTypes_e.swDocASSEMBLY
                     : (int)swDocumentTypes_e.swDocPART;
            int err = 0, warn = 0;
            var d = _sw.OpenDoc6(path, type, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref err, ref warn) as IModelDoc2;
            Say("opened " + path + " -> " + (d == null ? "null" : d.GetTitle()) + " errors " + err + " warnings " + warn);
            _openedByMe.Add(folder);
            return d;
        }

        private static readonly HashSet<string> _openBefore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static void SnapshotOpen()
        {
            try
            {
                var docs = _sw.GetDocuments() as object[];
                if (docs == null) return;
                foreach (var o in docs) { var d = o as IModelDoc2; if (d != null) _openBefore.Add((d.GetPathName() ?? "") + "|" + d.GetTitle()); }
                Say("documents open before the probe: " + _openBefore.Count);
            }
            catch { }
        }

        /// <summary>
        /// Closes (without saving) every document that was not open when the probe started: the one it opened and
        /// the part the drawing pulled in. Never one that was open before.
        /// </summary>
        private static void CloseMine()
        {
            try
            {
                var docs = _sw.GetDocuments() as object[];
                if (docs == null) return;
                var titles = new List<string>();
                foreach (var o in docs)
                {
                    var d = o as IModelDoc2;
                    if (d == null) continue;
                    if (!_openBefore.Contains((d.GetPathName() ?? "") + "|" + d.GetTitle())) titles.Add(d.GetTitle());
                }
                foreach (var t in titles) { _sw.CloseDoc(t); Say("closed " + t); }
            }
            catch (Exception ex) { Say("close: " + ex.Message); }
        }

        // ── EDGES ─────────────────────────────────────────────────────────

        private static void Edges(IDrawingDoc drawing, int only)
        {
            var mu = (IMathUtility)_sw.GetMathUtility();
            var v = (drawing.GetFirstView() as IView);
            int n = 0;
            while (v != null)
            {
                IView view = v;
                v = v.GetNextView() as IView;
                if (view.Type == (int)swDrawingViewTypes_e.swDrawingSheet) continue;
                n++;
                if (only > 0 && n != only) continue;
                var tr = view.ModelToViewTransform;
                var outline = view.GetOutline() as double[];
                Say("");
                Say("VIEW V" + n + " «" + view.GetName2() + "» scale " + view.ScaleDecimal.ToString(Ci) +
                    " outline " + P(outline, 4) + " orientation " + view.GetOrientationName());
                var edges = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
                Say("  edges: " + (edges == null ? 0 : edges.Length));
                if (edges == null) continue;
                var rows = new List<Row>();
                for (int i = 0; i < edges.Length; i++) rows.Add(ReadRow(i, edges[i] as IEdge, mu, tr));
                foreach (var r in rows)
                {
                    bool leg = false;
                    if (r.Chamfer == null)
                        foreach (var t in rows)
                        {
                            if (t.Chamfer == null || ReferenceEquals(t, r) || r.A == null || t.A == null) continue;
                            foreach (var p in new[] { r.A, r.B }) foreach (var q in new[] { t.A, t.B })
                                if (Math.Abs(p[0] - q[0]) + Math.Abs(p[1] - q[1]) + Math.Abs(p[2] - q[2]) < 1e-7) leg = true;
                        }
                    Say("  [" + r.I.ToString("00") + "] " + r.Kind.PadRight(6) +
                        " model " + P(r.A, 3) + " -> " + P(r.B, 3) +
                        " | sheet " + P(r.SheetA, 2) + " -> " + P(r.SheetB, 2) +
                        " len " + r.SheetLen.ToString("0.###", Ci) + " " + r.Facing +
                        (r.Radius > 0 ? " R" + r.Radius.ToString("0.###", Ci) : "") +
                        " | faces " + r.Faces +
                        (r.Chamfer != null ? " | CHAMFER-EDGE «" + r.Chamfer + "»" : leg ? " | chamfer-leg" : ""));
                }
            }
        }

        private sealed class Row
        {
            public int I; public string Kind = "?", Facing = "?", Faces = "", Chamfer;
            public double[] A, B, SheetA, SheetB;
            public double SheetLen, Radius;
        }

        private static double[] OnSheet(IMathUtility mu, MathTransform tr, double[] p)
        {
            try
            {
                var q = ((IMathPoint)mu.CreatePoint(p)).MultiplyTransform(tr) as IMathPoint;
                return q.ArrayData as double[];
            }
            catch { return null; }
        }

        private static Row ReadRow(int i, IEdge e, IMathUtility mu, MathTransform tr)
        {
            var r = new Row { I = i };
            if (e == null) return r;
            try
            {
                var cv = e.GetCurve() as ICurve;
                var pa = e.GetCurveParams3() as ICurveParamData;
                r.Kind = cv == null ? "?" : cv.IsLine() ? "line" : cv.IsCircle() ? "circle" : "other";
                if (pa != null)
                {
                    r.A = pa.StartPoint as double[]; r.B = pa.EndPoint as double[];
                    r.SheetA = OnSheet(mu, tr, r.A); r.SheetB = OnSheet(mu, tr, r.B);
                    if (r.SheetA != null && r.SheetB != null)
                    {
                        double dx = r.SheetB[0] - r.SheetA[0], dy = r.SheetB[1] - r.SheetA[1];
                        r.SheetLen = Math.Sqrt(dx * dx + dy * dy) * 1000.0;
                        double l = Math.Sqrt(dx * dx + dy * dy);
                        r.Facing = l < 1e-9 ? "point" : Math.Abs(dy) / l < 1e-3 ? "horizontal" : Math.Abs(dx) / l < 1e-3 ? "vertical" : "oblique";
                    }
                }
                if (cv != null && cv.IsCircle())
                {
                    var cp = cv.CircleParams as double[];
                    if (cp != null && cp.Length >= 7) r.Radius = cp[6] * 1000.0;
                    // a whole circle: start == end
                    if (r.A != null && r.B != null && Math.Abs(r.A[0] - r.B[0]) + Math.Abs(r.A[1] - r.B[1]) + Math.Abs(r.A[2] - r.B[2]) < 1e-8)
                        r.Kind = "FULL-CIRCLE";
                    else r.Kind = "arc";
                }
                var faces = e.GetTwoAdjacentFaces2() as object[];
                var sb = new StringBuilder();
                if (faces != null)
                    foreach (var o in faces)
                    {
                        var f = o as IFace2;
                        if (f == null) { sb.Append("null/"); continue; }
                        var s = f.IGetSurface();
                        string st = "?";
                        try
                        {
                            int id = s.Identity();
                            st = id == 4001 ? "plane" : id == 4002 ? "cyl" : id == 4003 ? "cone" : id == 4004 ? "sphere" : id == 4005 ? "torus" : id.ToString();
                        } catch { }
                        string ft = "";
                        try { var ff = f.GetFeature() as IFeature; ft = ff == null ? "-" : ff.GetTypeName2() + ":" + ff.Name; if (ff != null && ff.GetTypeName2() == "Chamfer") r.Chamfer = ff.Name; } catch { }
                        string nm = "";
                        try
                        {
                            if (st == "plane") { var nv = f.Normal as double[]; if (nv != null) nm = " n(" + nv[0].ToString("0.##", Ci) + "," + nv[1].ToString("0.##", Ci) + "," + nv[2].ToString("0.##", Ci) + ")"; }
                        } catch { }
                        sb.Append(st + nm + "[" + ft + "]/");
                    }
                r.Faces = sb.ToString();
            }
            catch (Exception ex) { r.Faces = "ERR " + ex.Message; }
            return r;
        }

        // ── DIMENSIONS AND ANNOTATIONS ────────────────────────────────────

        private static void Guarded(string what, Action a)
        {
            Say("");
            Say("=== " + what);
            try { a(); } catch (Exception ex) { Say("EXCEPTION in " + what + ": " + ex.Message); }
        }

        private static void Dims(IModelDoc2 doc, IDrawingDoc drawing)
        {
            Say("");
            Say("DRAWING FEATURE TREE (type: name)");
            try
            {
                var ft = doc.FirstFeature() as IFeature;
                while (ft != null)
                {
                    Say("   " + ft.GetTypeName2() + ": " + ft.Name);
                    ft = ft.GetNextFeature() as IFeature;
                }
            }
            catch (Exception ex) { Say("   tree: " + ex.Message); }
            var v = (drawing.GetFirstView() as IView);
            int n = 0;
            while (v != null)
            {
                IView view = v;
                v = v.GetNextView() as IView;
                bool sheet = view.Type == (int)swDrawingViewTypes_e.swDrawingSheet;
                if (!sheet) n++;
                Say("");
                Say((sheet ? "SHEET" : "VIEW V" + n) + " «" + view.GetName2() + "» outline " + P(view.GetOutline() as double[], 4));
                var outline0 = view.GetOutline() as double[];
                var anns = view.GetAnnotations() as object[];
                Say("  annotations: " + (anns == null ? 0 : anns.Length));
                if (anns != null)
                    foreach (var o in anns)
                    {
                        var a = o as IAnnotation;
                        if (a == null) continue;
                        string line = "   type " + a.GetType() + "=" + Enum.GetName(typeof(swAnnotationType_e), a.GetType()) + " «" + a.GetName() + "» pos " + P(a.GetPosition() as double[], 3) +
                                      " visible " + a.Visible;
                        try { var at = a.GetAttachedEntityTypes() as Array; if (at != null) { var l = new List<string>(); foreach (var t in at) l.Add(Convert.ToString(t, Ci)); line += " attached[" + string.Join(",", l) + "]"; } } catch { }
                        try { line += " dangling " + a.IsDangling(); } catch { }
                        line += LinesOf(a, outline0);
                        Say(line);
                    }
                var d = view.GetFirstDisplayDimension5() as IDisplayDimension;
                while (d != null)
                {
                    try
                    {
                        var dim = d.GetDimension2(0) as IDimension;
                        var a = d.GetAnnotation() as IAnnotation;
                        string val = "";
                        try { var vv = dim.GetValue3((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[]; if (vv != null && vv.Length > 0) val = (vv[0] * 1000.0).ToString("0.###", Ci); } catch { }
                        string full = ""; try { full = dim.FullName; } catch { }
                        Say("   DIM «" + full + "» type " + (dim == null ? -1 : dim.GetType()) + " kind " + KindOf(d) + Attached(d) + " value " + val +
                            " text «" + SafeText(d) + "» callout " + SafeCallout(d) +
                            " pos " + P(a == null ? null : a.GetPosition() as double[], 3) + (a == null ? "" : LinesOf(a, view.GetOutline() as double[], true)) +
                            " visible-state " + (a == null ? "?" : a.Visible.ToString()));
                    }
                    catch (Exception ex) { Say("   DIM ERR " + ex.Message); }
                    d = d.GetNext5() as IDisplayDimension;
                }
            }

            // the PART behind the first view
            var first = (drawing.GetFirstView() as IView)?.GetNextView() as IView;
            var part = first == null ? null : first.ReferencedDocument as IModelDoc2;
            if (part != null)
            {
                Say("");
                Say("PART " + part.GetTitle() + ": dimensions of the features, with the «marked for drawing» flag");
                var f = part.FirstFeature() as IFeature;
                while (f != null)
                {
                    try
                    {
                        var dd = f.GetFirstDisplayDimension() as IDisplayDimension;
                        while (dd != null)
                        {
                            var dim = dd.GetDimension2(0) as IDimension;
                            string val = "";
                            try { var vv = dim.GetValue3((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[]; if (vv != null && vv.Length > 0) val = (vv[0] * 1000.0).ToString("0.###", Ci); } catch { }
                            Say("   [" + f.GetTypeName2() + ":" + f.Name + "] «" + dim.FullName + "» type " + dim.GetType() + " value " + val + " marked " + SafeMarked(dd));
                            dd = f.GetNextDisplayDimension(dd) as IDisplayDimension;
                        }
                    }
                    catch (Exception ex) { Say("   feature " + f.Name + ": " + ex.Message); }
                    f = f.GetNextFeature() as IFeature;
                }
            }
        }

        // ── ATTEMPTS: what SOLIDWORKS does when an edge is selected and dimensioned ──

        private static int CountDims(IDrawingDoc drawing)
        {
            int n = 0;
            var v = drawing.GetFirstView() as IView;
            while (v != null) { n += v.GetDimensionCount(); v = v.GetNextView() as IView; }
            return n;
        }

        private static string DimLine(IDisplayDimension d)
        {
            try
            {
                var dim = d.GetDimension2(0) as IDimension;
                string val = "";
                try { var vv = dim.GetValue3((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[]; if (vv != null && vv.Length > 0) val = (vv[0] * 1000.0).ToString("0.###", Ci); } catch { }
                return "«" + dim.FullName + "» type " + dim.GetType() + " kind " + KindOf(d) + " value " + val + " callout " + SafeCallout(d) + Attached(d);
            }
            catch (Exception ex) { return "(unreadable: " + ex.Message + ")"; }
        }

        /// <summary>
        /// Every line and arc edge of one view is selected and dimensioned the way the bridge
        /// does it (IModelDocExtension.AddDimension at a point outside the view; swInputDimValOnCreate
        /// switched off and put back), and what was born is read back. Then the same edges with the
        /// dimension calls of the guide that fit the kind (AddHorizontalDimension2, AddVerticalDimension2,
        /// AddRadialDimension2). Nothing is saved.
        /// </summary>
        private static void Attempt(IModelDoc2 doc, IDrawingDoc drawing, int viewNo)
        {
            var mu = (IMathUtility)_sw.GetMathUtility();
            var v = drawing.GetFirstView() as IView;
            int n = 0; IView view = null;
            while (v != null)
            {
                IView c = v; v = v.GetNextView() as IView;
                if (c.Type == (int)swDrawingViewTypes_e.swDrawingSheet) continue;
                n++;
                if (n == viewNo) { view = c; break; }
            }
            if (view == null) { Say("no view V" + viewNo); return; }
            _sw.ActivateDoc3(doc.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, 0);
            var tr = view.ModelToViewTransform;
            var outline = view.GetOutline() as double[];
            var edges = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
            Say("ATTEMPT on V" + viewNo + " «" + view.GetName2() + "», edges " + (edges == null ? 0 : edges.Length) + ", dimensions before " + CountDims(drawing));
            bool asked = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
            _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            try
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    var r = ReadRow(i, edges[i] as IEdge, mu, tr);
                    if (r.Kind != "line" && r.Kind != "arc") continue;
                    foreach (var how in new[] { "AddDimension", "H/V/Radial" })
                    {
                        doc.ClearSelection2(true);
                        var ent = edges[i] as IEntity;
                        bool sel = ent != null && ent.Select4(false, null);
                        double x = outline[2] + 0.02 + (i % 7) * 0.006, y = (outline[1] + outline[3]) / 2;
                        if (r.Facing == "horizontal") { x = (outline[0] + outline[2]) / 2; y = outline[1] - 0.02 - (i % 7) * 0.006; }
                        int before = CountDims(drawing);
                        object made = null; string call = how;
                        try
                        {
                            if (how == "AddDimension") made = doc.Extension.AddDimension(x, y, 0, 0);
                            else if (r.Kind == "arc") { call = "AddRadialDimension2"; made = doc.AddRadialDimension2(x, y, 0); }
                            else if (r.Facing == "horizontal") { call = "AddHorizontalDimension2"; made = doc.AddHorizontalDimension2(x, y, 0); }
                            else if (r.Facing == "vertical") { call = "AddVerticalDimension2"; made = doc.AddVerticalDimension2(x, y, 0); }
                            else { call = "(oblique: none)"; }
                        }
                        catch (Exception ex) { call += " THREW " + ex.Message; }
                        try { doc.EditRebuild3(); } catch { }
                        int after = CountDims(drawing);
                        var dd = made as IDisplayDimension;
                        Say("  [" + i.ToString("00") + "] " + r.Kind + " " + r.Facing + " len " + r.SheetLen.ToString("0.###", Ci) + " chamfer=" + (r.Chamfer ?? "-") +
                            " | " + call + " selected " + sel + " -> dims " + before + "->" + after + (dd != null ? " " + DimLine(dd) : (after > before ? " (born, object not returned)" : "")));
                    }
                }
                doc.ClearSelection2(true);
            }
            finally { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, asked); }
            Say("dimensions after " + CountDims(drawing));
        }

        // ── CHAMFER DIMENSION: which selection makes AddChamferDim give a dimension ──

        private static void ChamferTry(IModelDoc2 doc, IDrawingDoc drawing, int viewNo)
        {
            var mu = (IMathUtility)_sw.GetMathUtility();
            var v = drawing.GetFirstView() as IView;
            int n = 0; IView view = null;
            while (v != null)
            {
                IView c = v; v = v.GetNextView() as IView;
                if (c.Type == (int)swDrawingViewTypes_e.swDrawingSheet) continue;
                n++;
                if (n == viewNo) { view = c; break; }
            }
            if (view == null) { Say("no view V" + viewNo); return; }
            _sw.ActivateDoc3(doc.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, 0);
            var tr = view.ModelToViewTransform;
            var outline = view.GetOutline() as double[];
            var edges = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
            var rows = new List<Row>();
            for (int i = 0; i < edges.Length; i++) rows.Add(ReadRow(i, edges[i] as IEdge, mu, tr));
            bool asked = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
            _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            int tried = 0;
            try
            {
                foreach (var s in rows)
                {
                    if (s.Chamfer == null || s.Facing != "oblique" || s.Kind != "line" || s.A == null) continue;
                    var legs = new List<Row>();
                    foreach (var t in rows)
                    {
                        if (ReferenceEquals(t, s) || t.A == null) continue;
                        bool shares = false;
                        foreach (var p in new[] { t.A, t.B }) foreach (var q in new[] { s.A, s.B })
                            if (Math.Abs(p[0] - q[0]) + Math.Abs(p[1] - q[1]) + Math.Abs(p[2] - q[2]) < 1e-7) shares = true;
                        if (shares) legs.Add(t);
                    }
                    Say("  chamfer edge [" + s.I + "] «" + s.Chamfer + "» sheet " + P(s.SheetA, 2) + "->" + P(s.SheetB, 2) + ", edges sharing a vertex: " +
                        string.Join(",", legs.ConvertAll(t => t.I + ":" + t.Kind + "/" + t.Facing + (t.Chamfer != null ? "/ch" : ""))));
                    double mx = (s.SheetA[0] + s.SheetB[0]) / 2, my = (s.SheetA[1] + s.SheetB[1]) / 2;
                    double cx = (outline[0] + outline[2]) / 2, cy = (outline[1] + outline[3]) / 2;
                    double vx = mx - cx, vy = my - cy, l = Math.Sqrt(vx * vx + vy * vy);
                    if (l < 1e-9) { vx = -1; vy = 1; l = Math.Sqrt(2); }
                    double x = mx + vx / l * 0.018, y = my + vy / l * 0.018;
                    foreach (var leg in legs)
                    {
                        foreach (int order in new[] { 0, 1 })
                        {
                            if (tried++ > 40) { Say("  (stopped at 40 attempts)"); return; }
                            doc.ClearSelection2(true);
                            var e1 = edges[order == 0 ? s.I : leg.I] as IEntity;
                            var e2 = edges[order == 0 ? leg.I : s.I] as IEntity;
                            bool a1 = e1.Select4(false, null), a2 = e2.Select4(true, null);
                            int before = CountDims(drawing);
                            object made = null; string ex = "";
                            try { made = drawing.AddChamferDim(x, y, 0); } catch (Exception e) { ex = " THREW " + e.Message; }
                            try { doc.EditRebuild3(); } catch { }
                            int after = CountDims(drawing);
                            var dd = made as IDisplayDimension;
                            Say("    order " + (order == 0 ? "chamfer,leg" : "leg,chamfer") + " leg [" + leg.I + "] selected " + a1 + "/" + a2 +
                                " -> dims " + before + "->" + after + ex + (dd != null ? " " + DimLine(dd) : ""));
                        }
                    }
                    // the chamfer edge alone
                    doc.ClearSelection2(true);
                    bool al = (edges[s.I] as IEntity).Select4(false, null);
                    int b2 = CountDims(drawing); object m2 = null; string ex2 = "";
                    try { m2 = drawing.AddChamferDim(x, y, 0); } catch (Exception e) { ex2 = " THREW " + e.Message; }
                    try { doc.EditRebuild3(); } catch { }
                    Say("    chamfer edge alone: selected " + al + " -> dims " + b2 + "->" + CountDims(drawing) + ex2 + (m2 is IDisplayDimension ? " " + DimLine((IDisplayDimension)m2) : ""));
                }
                doc.ClearSelection2(true);
            }
            finally { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, asked); }
            if (tried == 0) Say("  no oblique chamfer edge in this view");
        }

        // ── MARKED FOR DRAWING: do the dimensions of the sheet metal reach the drawing when marked? ──
        // In memory only: nothing is saved, the documents are closed without saving.

        private static void MarkExperiment(IModelDoc2 doc, IDrawingDoc drawing)
        {
            var first = (drawing.GetFirstView() as IView)?.GetNextView() as IView;
            var part = first == null ? null : first.ReferencedDocument as IModelDoc2;
            if (part == null) { Say("no part behind the first view"); return; }
            Say("part " + part.GetPathName());
            // 1. every feature dimension and its mark (the tree, with the sketches)
            var marked = new List<IDisplayDimension>();
            var f = part.FirstFeature() as IFeature;
            int total = 0, mk = 0;
            while (f != null)
            {
                try
                {
                    var dd = f.GetFirstDisplayDimension() as IDisplayDimension;
                    while (dd != null)
                    {
                        total++;
                        bool m = false; try { m = dd.MarkedForDrawing; } catch { }
                        if (m) mk++;
                        var dim = dd.GetDimension2(0) as IDimension;
                        Say("   [" + f.GetTypeName2() + ":" + f.Name + "] «" + dim.FullName + "» marked " + m);
                        marked.Add(dd);
                        dd = f.GetNextDisplayDimension(dd) as IDisplayDimension;
                    }
                }
                catch (Exception ex) { Say("   feature " + f.Name + ": " + ex.Message); }
                f = f.GetNextFeature() as IFeature;
            }
            Say("part dimensions " + total + ", marked " + mk);

            // 2. remove every dimension of the drawing (in memory), then import with the MARKED mask: baseline
            _sw.ActivateDoc3(doc.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, 0);
            int removed = RemoveAllDimensions(doc, drawing);
            Say("dimensions removed from the drawing: " + removed + ", left " + CountDims(drawing));
            Import(drawing, "baseline (MARKED mask)", (int)swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing);

            // 3. mark ALL the feature dimensions of the part (in memory), remove again, import again
            int nowMarked = 0;
            foreach (var dd in marked) { try { dd.MarkedForDrawing = true; nowMarked++; } catch { } }
            Say("marked in memory: " + nowMarked);
            RemoveAllDimensions(doc, drawing);
            Import(drawing, "after marking everything (MARKED mask)", (int)swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing);
            RemoveAllDimensions(doc, drawing);
            Import(drawing, "all masks together", (int)swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing | (int)swInsertAnnotation_e.swInsertDimensionsNotMarkedForDrawing | (int)swInsertAnnotation_e.swInsertDimensions);
        }

        private static void Import(IDrawingDoc drawing, string label, int mask)
        {
            int before = CountDims(drawing);
            object r = null; string ex = "";
            try { r = drawing.InsertModelAnnotations3((int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel, mask, true, true, false, false); }
            catch (Exception e) { ex = " THREW " + e.Message; }
            int after = CountDims(drawing);
            Say("  import " + label + ": dims " + before + "->" + after + " (returned " + ((r as object[]) == null ? 0 : ((object[])r).Length) + ")" + ex);
            var v = (drawing.GetFirstView() as IView);
            while (v != null)
            {
                var d = v.GetFirstDisplayDimension5() as IDisplayDimension;
                while (d != null)
                {
                    Say("      " + (v.GetName2() ?? "") + ": " + DimLine(d));
                    d = d.GetNext5() as IDisplayDimension;
                }
                v = v.GetNextView() as IView;
            }
        }

        private static int RemoveAllDimensions(IModelDoc2 doc, IDrawingDoc drawing)
        {
            var all = new List<IDisplayDimension>();
            var v = drawing.GetFirstView() as IView;
            while (v != null)
            {
                var d = v.GetFirstDisplayDimension5() as IDisplayDimension;
                while (d != null) { all.Add(d); d = d.GetNext5() as IDisplayDimension; }
                v = v.GetNextView() as IView;
            }
            int removed = 0;
            foreach (var d in all)
            {
                try
                {
                    doc.ClearSelection2(true);
                    var ann = d.GetAnnotation() as IAnnotation;
                    if (ann != null && ann.Select3(false, null) && doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed)) removed++;
                }
                catch { }
            }
            doc.ClearSelection2(true);
            return removed;
        }

        /// <summary>
        /// The lines an annotation draws (IAnnotation~GetDisplayData, IDisplayData~GetLineAtIndex3: [color, lineType, lineStyle,
        /// lineWeight, start xyz, end xyz], metres on the sheet): how many, how many in a centre style (dash-dot), the box
        /// they cover, and the ones that run outside the outline of the owner view by more than 5 mm.
        /// </summary>
        private static string LinesOf(IAnnotation a, double[] outline, bool every = false)
        {
            try
            {
                var data = a.GetDisplayData() as IDisplayData;
                if (data == null) return " lines n/a";
                int n = data.GetLineCount();
                if (n <= 0) return " lines 0";
                int centre = 0, outside = 0;
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                var sb = new StringBuilder();
                for (int i = 0; i < n; i++)
                {
                    var l = data.GetLineAtIndex3(i) as double[];
                    if (l == null || l.Length < 10) continue;
                    int style = (int)l[2];
                    string sn = Enum.GetName(typeof(swLineStyles_e), style) ?? style.ToString();
                    bool isCentre = sn.IndexOf("CENTER", StringComparison.OrdinalIgnoreCase) >= 0 || sn.IndexOf("PHANTOM", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isCentre) centre++;
                    x0 = Math.Min(x0, Math.Min(l[4], l[7])); x1 = Math.Max(x1, Math.Max(l[4], l[7]));
                    y0 = Math.Min(y0, Math.Min(l[5], l[8])); y1 = Math.Max(y1, Math.Max(l[5], l[8]));
                    bool out5 = outline != null && (l[4] < outline[0] - 0.005 || l[7] < outline[0] - 0.005 || l[4] > outline[2] + 0.005 || l[7] > outline[2] + 0.005 ||
                                                     l[5] < outline[1] - 0.005 || l[8] < outline[1] - 0.005 || l[5] > outline[3] + 0.005 || l[8] > outline[3] + 0.005);
                    if (out5) outside++;
                    if (isCentre || out5 || every)
                        if (sb.Length < (every ? 1500 : 700)) sb.Append(" [t" + (int)l[1] + " " + sn + " " + Mm(l[4]) + "," + Mm(l[5]) + "->" + Mm(l[7]) + "," + Mm(l[8]) + (out5 ? " OUT" : "") + "]");
                }
                return " lines " + n + " centre-style " + centre + " beyond-view>5mm " + outside + " box(" + Mm(x0) + "," + Mm(y0) + ")-(" + Mm(x1) + "," + Mm(y1) + ")" + sb;
            }
            catch (Exception ex) { return " lines ERR " + ex.Message; }
        }

        private static string KindOf(IDisplayDimension d)
        {
            try
            {
                string k = Enum.GetName(typeof(swDimensionType_e), d.Type2) ?? d.Type2.ToString();
                if (d.Type2 == (int)swDimensionType_e.swRadialDimension) k += d.Diametric ? "/diametric" : "/radius";
                return k;
            }
            catch { return "?"; }
        }

        private static string Attached(IDisplayDimension d)
        {
            try
            {
                var ann = d.GetAnnotation() as IAnnotation;
                var types = ann.GetAttachedEntityTypes() as Array;
                var l = new List<string>();
                if (types != null) foreach (var t in types) l.Add(Convert.ToString(t, Ci));
                string dang = ""; try { dang = " dangling=" + ann.IsDangling(); } catch { }
                return " attached[" + string.Join(",", l) + "]" + dang;
            }
            catch { return ""; }
        }

        private static string SafeText(IDisplayDimension d)
        {
            try { return (d.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? "") + "|" + (d.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix) ?? ""); }
            catch { return "?"; }
        }
        /// <summary>
        /// The lines SOLIDWORKS draws for each dimension (IDisplayDimension~GetDisplayData, IDisplayData~GetLineCount and
        /// GetLineAtIndex3): the raw numbers of each line, to see which ones are the dimension line, the extension lines,
        /// and where they lie on the sheet. Used to detect dimension lines that lie on top of each other and extension
        /// lines that hang from nothing (items 3 and 5 of the drawing branch of 0.131.0).
        /// </summary>
        private static void DimLines(IDrawingDoc drawing)
        {
            var v = drawing.GetFirstView() as IView;
            int n = 0;
            while (v != null)
            {
                IView view = v; v = v.GetNextView() as IView;
                bool sheet = view.Type == (int)swDrawingViewTypes_e.swDrawingSheet;
                if (!sheet) n++;
                var d = view.GetFirstDisplayDimension5() as IDisplayDimension;
                while (d != null)
                {
                    try
                    {
                        var dim = d.GetDimension2(0) as IDimension;
                        var data = d.GetDisplayData() as IDisplayData;
                        int lc = data == null ? 0 : data.GetLineCount();
                        Say((sheet ? "SHEET" : "V" + n) + " DIM «" + (dim == null ? "?" : dim.FullName) + "» lines " + lc + " arrows " + (data == null ? 0 : data.GetArrowHeadCount()));
                        for (int i = 0; i < lc; i++)
                        {
                            var l = data.GetLineAtIndex3(i);
                            var arr = l as Array;
                            var sb = new StringBuilder();
                            if (arr != null) foreach (var o in arr) sb.Append(Convert.ToString(o, Ci) + " ");
                            else sb.Append(l == null ? "null" : l.GetType().Name);
                            Say("      line " + i + ": " + sb);
                        }
                    }
                    catch (Exception ex) { Say("   DIMLINES ERR " + ex.Message); }
                    d = d.GetNext5() as IDisplayDimension;
                }
            }
        }


        // ── WHO DRAWS THE DASH-DOT LINES OUTSIDE THE VIEWS (item 5) ──
        // The lines are not annotations of any view. Picked at points on them (SelectByID2 with empty name and type, the
        // coordinates on the sheet): what is there, then dimension by dimension deleted in memory (nothing is saved)
        // and picked again: the deletion after which the pick fails is the owner.

        private static readonly double[][] _pts = {
            new[] { 0.017, 0.19449 }, new[] { 0.0345, 0.177 }, new[] { 0.017, 0.14069 }, new[] { 0.194, 0.19449 }, new[] { 0.2115, 0.175 } };

        private static string PickAt(IModelDoc2 doc, double[] p)
        {
            try
            {
                doc.ClearSelection2(true);
                bool ok = doc.Extension.SelectByID2("", "", p[0], p[1], 0, false, 0, null, 0);
                var sm = doc.SelectionManager as ISelectionMgr;
                int n = sm == null ? -1 : sm.GetSelectedObjectCount2(-1);
                string what = "";
                if (ok && n > 0)
                {
                    int t = sm.GetSelectedObjectType3(1, -1);
                    what = t + "=" + (Enum.GetName(typeof(swSelectType_e), t) ?? "?");
                    try { var o = sm.GetSelectedObject6(1, -1); what += " " + (o == null ? "null" : o.GetType().Name); var an = o as IAnnotation; if (an != null) what += " ann «" + an.GetName() + "» type " + an.GetType(); } catch { }
                }
                doc.ClearSelection2(true);
                return (ok ? "HIT " : "no  ") + what;
            }
            catch (Exception ex) { return "ERR " + ex.Message; }
        }

        private static void PickAll(IModelDoc2 doc, string label)
        {
            var sb = new StringBuilder(label + ":");
            for (int i = 0; i < _pts.Length; i++) sb.Append(System.Environment.NewLine + "     P" + i + " (" + Mm(_pts[i][0]) + "," + Mm(_pts[i][1]) + ") " + PickAt(doc, _pts[i]));
            Say(sb.ToString());
        }

        private static void PickLines(IModelDoc2 doc, IDrawingDoc drawing)
        {
            _sw.ActivateDoc3(doc.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, 0);
            PickAll(doc, "as the drawing is");
            var all = new List<IDisplayDimension>(); var names = new List<string>();
            var v = drawing.GetFirstView() as IView;
            while (v != null)
            {
                var d = v.GetFirstDisplayDimension5() as IDisplayDimension;
                while (d != null) { all.Add(d); string nm = "?"; try { nm = (v.GetName2() ?? "") + ":" + ((d.GetDimension2(0) as IDimension)?.FullName ?? "?"); } catch { } names.Add(nm); d = d.GetNext5() as IDisplayDimension; }
                v = v.GetNextView() as IView;
            }
            Say("dimensions in the drawing: " + all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                try
                {
                    doc.ClearSelection2(true);
                    var ann = all[i].GetAnnotation() as IAnnotation;
                    bool deleted = ann != null && ann.Select3(false, null) && doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                    try { doc.EditRebuild3(); } catch { }
                    doc.ClearSelection2(true);
                    PickAll(doc, "after deleting " + names[i] + " (deleted " + deleted + ", dimensions left " + CountDims(drawing) + ")");
                }
                catch (Exception ex) { Say("   deleting " + names[i] + ": " + ex.Message); }
            }
        }

        /// <summary>
        /// The sketch segments of the sheet and of every view (IView~GetSketch, ISketch~GetSketchSegments, ISketchSegment~GetType,
        /// ISketchLine~GetStartPoint2 / GetEndPoint2): what a drawing carries that is neither an edge of the part nor an
        /// annotation. The dash-dot lines left outside the views of the L bracket (item 5) are sketch segments: the pick of
        /// 12/10/2026 hits them with swSelSKETCHSEGS at the points where they are drawn, and they stay after every dimension
        /// has been taken away. Called between two calls of the bridge to see which call leaves them.
        /// </summary>
        private static void Segments(IDrawingDoc drawing)
        {
            var v = drawing.GetFirstView() as IView;
            int n = 0;
            while (v != null)
            {
                IView view = v; v = v.GetNextView() as IView;
                bool sheet = view.Type == (int)swDrawingViewTypes_e.swDrawingSheet;
                if (!sheet) n++;
                string label = sheet ? "SHEET" : "V" + n;
                try
                {
                    var sk = view.GetSketch() as ISketch;
                    var segs = sk == null ? null : sk.GetSketchSegments() as object[];
                    Say(label + " sketch segments: " + (sk == null ? "no sketch" : (segs == null ? 0 : segs.Length).ToString()));
                    if (segs != null)
                        foreach (var o in segs)
                        {
                            var s = o as ISketchSegment;
                            if (s == null) continue;
                            string desc = "type " + s.GetType();
                            var ln = o as ISketchLine;
                            if (ln != null)
                            {
                                var p = ln.GetStartPoint2() as ISketchPoint; var q = ln.GetEndPoint2() as ISketchPoint;
                                if (p != null && q != null) desc += " line (" + Mm(p.X) + ", " + Mm(p.Y) + ") -> (" + Mm(q.X) + ", " + Mm(q.Y) + ")";
                            }
                            try { desc += " style " + s.Style; } catch { }
                            Say("      " + desc);
                        }
                }
                catch (Exception ex) { Say(label + " sketch: " + ex.Message); }
            }
        }

        /// <summary>
        /// Runs the library's own RemoveNewSketchSegments (private, by reflection) on a drawing that already has sketch segments, with
        /// «nothing was known before»: every segment in the views is «new» and has to go. It is the test of the function that takes the
        /// dash-dot lines away, on the 6 lines of the proof sheet (the import by the hole-wizard masks no longer makes them: this
        /// is the safety net for the all-types route, and a net that has never caught anything is not trusted).
        /// </summary>
        private static void DeleteSegments(IModelDoc2 doc, IDrawingDoc drawing)
        {
            _sw.ActivateDoc3(doc.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, 0);
            Segments(drawing);
            var before = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var v = drawing.GetFirstView() as IView;
            while (v != null) { before[v.GetName2() ?? ""] = new HashSet<string>(StringComparer.Ordinal); v = v.GetNextView() as IView; }
            var ops = new Oce.Solid.Agent.Core.SwOperations(_sw, null);
            var method = typeof(Oce.Solid.Agent.Core.SwOperations).GetMethod("RemoveNewSketchSegments",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method == null) { Say("RemoveNewSketchSegments not found"); return; }
            var removed = new List<object>(); var problems = new List<string>();
            method.Invoke(ops, new object[] { doc, drawing, before, removed, problems });
            Say("removed " + removed.Count + ", problems " + problems.Count);
            foreach (var p in problems) Say("   PROBLEM " + p);
            Segments(drawing);
        }

        private static string SafeCallout(IDisplayDimension d) { try { return d.IsHoleCallout().ToString(); } catch { return "?"; } }
        private static string SafeMarked(IDisplayDimension d) { try { return d.MarkedForDrawing.ToString(); } catch { return "?"; } }
    }
}
