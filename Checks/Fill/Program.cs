using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  ProbeFill — fill pattern and table pattern (TODO W1.1), rewritten by the review of 28/9/2026
// =============================================================================
//
//  WHAT THE SEVEN BLOCKED VARIANTS REALLY DID (the three historical probes of
//  16/9/2026, never in the repo): all seven called
//  CreateDefinition(swFmFillPattern) FIRST, with NOTHING selected, and then
//  wrote the object properties (PatternFillBoundaryArray, PatternFeatureArray,
//  PatternDirection) on the data object. Not one pre-selected with marks.
//
//  WHAT THE GUIDE SAYS:
//   - IFillPatternFeatureData, Remarks: "Before calling
//     IFeatureManager::CreateDefinition, you can pre-select the entities...":
//     fill boundary 16384, direction (edge/axis, optional) 1, seed location
//     (vertex/sketch point, optional) 3, pattern FEATURES 4, FACES 5, BODIES 6.
//     "Or, after calling CreateDefinition, you can explicitly set the
//     corresponding properties".
//   - Create_Fill_Pattern_Example_CSharp.htm, the only worked example: selects
//     Hole1 as BODYFEATURE mark 4, an edge by ray mark 1, a face by ray mark
//     16384, THEN CreateDefinition, sets only scalars, CreateFeature.
//   - ITablePatternFeatureData, Remarks + Create_Table_Pattern_Example_CSharp.htm:
//     seed feature 4, coordinate system 16 (reference point 32, seed face 128,
//     seed body 256), PointArray = plain double[] (3 numbers per point),
//     GeometryPattern, PropagateVisualProperty, UseCentroid, CreateFeature.
//   - Enum values by reflection on swconst 29.5.1.1: swPatternLayoutCircular=0,
//     swPatternLayoutInstances=1, swFeaturesToPatternSelectedFeatures=0 — the
//     example's zeros and ones mean those.
//
//  FIRST DRAFT, fixed here: it did not compile (IFeature.GetErrorCode2 wants
//  `out bool`), opened the plate sketch with no plane selected, left out the
//  direction (mark 1) that the example does select, and gave mark 4 to a FACE,
//  which the Remarks table itself assigns to mark 5.
//
//  ATTEMPTS (one fresh part each; a through hole is 603.2 mm3):
//   A   official order: feature mark 4 + edge mark 1 + face mark 16384, then CreateDefinition, example scalars
//   A2  as A without the direction (the Remarks say it is optional)
//   C   same selections, but made AFTER CreateDefinition (is "before" binding?)
//   D   the hole's cylindrical face as pattern object, mark 5 (+ PatternElement = swFeatureFaces)
//   B   control: the historical route (nothing selected, properties written, DispatchWrapper)
//   T   table pattern: seed feature mark 4, no coordinate system, flat PointArray, as the example
//
//  Ground truth: new feature + its type + volume delta + bodies + features in error.
//  No EditDelete, no AddDimension, no SimpleHole2 / ActivateSelectedFeature (they open PropertyManager pages).
//
//  USAGE      Checks\Fill\bin\Fill.exe [--keep]
//             (built by Checks\build-checks.ps1 (and by build.ps1, step 1c))
//
//  OUTCOME OF 28/9/2026, SOLIDWORKS 29.5.1 (the output of the probe, as it was):
//
//    PROBE FILL (W1.1) — SOLIDWORKS 29.5.1
//          selected 3 (mark 4: 1, 5: 2, 1: 1, 16384: 1)
//      A  official recipe: 4 + 1 + 16384 BEFORE CreateDefinition  ->  «Ripetizione di riempimento1» [APattern], volume 71396.8 -> 62952.2 (extra holes 14), bodies 1 -> 1, features in error 0
//          selected 2 (mark 4: 1, 5: 1, 1: 0, 16384: 1)
//      A2 as A, without direction  ->  CreateFeature NULL, volume 71396.8 -> 71396.8 (extra holes 0), bodies 1 -> 1, features in error 0
//          selected 3 (mark 4: 1, 5: 2, 1: 1, 16384: 1)
//      C  same selections, but AFTER CreateDefinition  ->  «Ripetizione di riempimento1» [APattern], volume 71396.8 -> 62952.2 (extra holes 14), bodies 1 -> 1, features in error 0
//          selected 3 (mark 4: 1, 5: 2, 1: 2, 16384: 1)
//      D  hole face with mark 5 (FACES)  ->  «Ripetizione di riempimento1» [APattern], volume 71396.8 -> 62952.2 (extra holes 14), bodies 1 -> 1, features in error 0
//      B  control: historical route, properties written, no selection  ->  CreateFeature NULL, volume 71396.8 -> 71396.8 (extra holes 0), bodies 1 -> 1, features in error 0
//      T  table: seed mark 4, flat PointArray  ->  CreateFeature NULL, volume 71396.8 -> 71396.8 (extra holes 0), bodies 1 -> 1, features in error 0
//    SUMMARY: 3/6 attempts created a feature.
// =============================================================================

internal static class Program
{
    private const double L = 0.100, W = 0.060, H = 0.012, HoleX = -0.030, HoleR = 0.004;

    private sealed class Model { public IModelDoc2 Doc; public IFeature Seed; public IFace2 Top; public IEdge EdgeAlongX; public IFace2 Bore; public double YTop; }

    private static ISldWorks _sw;

    private static int Main(string[] args)
    {
        bool keep = Array.IndexOf(args, "--keep") >= 0;
        try { _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS does not respond: " + ex.Message); return 2; }
        Console.WriteLine("PROBE FILL (W1.1) — SOLIDWORKS " + _sw.RevisionNumber());

        var attempts = new List<KeyValuePair<string, Func<Model, IFeature>>>
        {
            new KeyValuePair<string, Func<Model, IFeature>>("A  official recipe: 4 + 1 + 16384 BEFORE CreateDefinition", m => Fill(m, true, true, false, false)),
            new KeyValuePair<string, Func<Model, IFeature>>("A2 like A, without direction",                                   m => Fill(m, false, true, false, false)),
            new KeyValuePair<string, Func<Model, IFeature>>("C  same selections, but AFTER CreateDefinition",                m => Fill(m, true, false, false, false)),
            new KeyValuePair<string, Func<Model, IFeature>>("D  hole face with mark 5 (FACES)",                        m => Fill(m, true, true, true, false)),
            new KeyValuePair<string, Func<Model, IFeature>>("B  control: the historical way, properties written, no selection", m => Fill(m, false, false, false, true)),
            new KeyValuePair<string, Func<Model, IFeature>>("T  table: seed mark 4, flat PointArray",                   Table),
        };

        int born = 0;
        foreach (var at in attempts)
        {
            Model model = null; string outcome;
            try
            {
                model = BuildPlate();
                if (model.Seed == null) outcome = "model not built";
                else
                {
                    double v0 = Volume(model.Doc); int b0 = Bodies(model.Doc); var before = FeatureKeys(model.Doc);
                    IFeature f = null; string raised = "";
                    try { f = at.Value(model); } catch (Exception ex) { while (ex.InnerException != null) ex = ex.InnerException; raised = " RAISED: " + ex.Message; }
                    try { model.Doc.EditRebuild3(); } catch { }
                    double v1 = Volume(model.Doc);
                    bool isNew = f != null && !before.Contains(f.Name + "|" + f.GetTypeName2());
                    if (isNew) born++;
                    outcome = (f == null ? "CreateFeature NULL" : "«" + f.Name + "» [" + f.GetTypeName2() + "]") + raised +
                              ", volume " + Fmt(v0) + " -> " + Fmt(v1) + " (extra holes " + ((v0 - v1) / 603.19).ToString("0.##", CultureInfo.InvariantCulture) + ")" +
                              ", bodies " + b0 + " -> " + Bodies(model.Doc) + ", features in error " + InError(model.Doc);
                }
            }
            catch (Exception ex) { outcome = "EXCEPTION " + ex.Message; }
            finally { if (model != null && model.Doc != null && !keep) { try { _sw.CloseDoc(model.Doc.GetTitle()); } catch { } } }
            Console.WriteLine("  " + at.Key + "  ->  " + outcome);
        }
        Console.WriteLine();
        Console.WriteLine("SUMMARY: " + born + "/" + attempts.Count + " attempts created a feature.");
        return born > 0 ? 0 : 5;
    }

    // ── the fill pattern, in the requested variant ──
    private static IFeature Fill(Model m, bool withDirection, bool selectBefore, bool faceAsObject, bool propertiesRoute)
    {
        var doc = m.Doc; var fm = doc.FeatureManager;
        doc.ClearSelection2(true);
        IFillPatternFeatureData data = null;
        if (!selectBefore) data = fm.CreateDefinition((int)swFeatureNameID_e.swFmFillPattern) as IFillPatternFeatureData;
        if (!propertiesRoute)
        {
            string s = SelectMarked(m, withDirection, faceAsObject);
            if (s != null) { Console.WriteLine("      selection: " + s); return null; }
        }
        if (selectBefore) data = fm.CreateDefinition((int)swFeatureNameID_e.swFmFillPattern) as IFillPatternFeatureData;
        if (data == null) { Console.WriteLine("      CreateDefinition(105) -> null"); return null; }

        ExampleScalars(data);
        if (faceAsObject) data.PatternElement = (int)swPatternElementSelection_e.swFeatureFaces;
        if (propertiesRoute)
        {
            data.PatternFillBoundaryArray = new[] { new DispatchWrapper(m.Top) };
            data.PatternFeatureArray = new[] { new DispatchWrapper(m.Seed) };
        }
        return fm.CreateFeature(data) as IFeature;
    }

    // Order of Create_Fill_Pattern_Example_CSharp.htm: pattern object, direction, boundary.
    private static string SelectMarked(Model m, bool withDirection, bool faceAsObject)
    {
        var doc = m.Doc;
        var selMgr = (ISelectionMgr)doc.SelectionManager;
        bool ok;
        if (faceAsObject) ok = ((IEntity)m.Bore).Select4(false, Mark(selMgr, 5));
        else ok = doc.Extension.SelectByID2(m.Seed.Name, "BODYFEATURE", 0, 0, 0, false, 4, null, 0);
        if (!ok) return "object to repeat not selected";
        if (withDirection && !((IEntity)m.EdgeAlongX).Select4(true, Mark(selMgr, 1))) return "direction edge not selected";
        if (!((IEntity)m.Top).Select4(true, Mark(selMgr, 16384))) return "boundary face not selected";
        Console.WriteLine("      selected " + selMgr.GetSelectedObjectCount2(-1) +
                          " (mark 4: " + selMgr.GetSelectedObjectCount2(4) + ", 5: " + selMgr.GetSelectedObjectCount2(5) +
                          ", 1: " + selMgr.GetSelectedObjectCount2(1) + ", 16384: " + selMgr.GetSelectedObjectCount2(16384) + ")");
        return null;
    }

    private static SelectData Mark(ISelectionMgr selMgr, int mark) { var sd = selMgr.CreateSelectData(); sd.Mark = mark; return sd; }

    // The example's scalars, verbatim (its hole is also 8 mm): circular layout, spacing by instances.
    private static void ExampleScalars(IFillPatternFeatureData d)
    {
        d.CreateSeedCutPolygonSides = 0;
        d.CreateSeedCutType = (int)swCreateSeedCutType_e.swCreateSeedCutNone;
        d.Diagonal = 0.008;
        d.Diameter = 0.008;
        d.Dimension = 0.00565685424949238;
        d.FeaturesToPatternType = (int)swFeaturesToPatternType_e.swFeaturesToPatternSelectedFeatures;
        d.GeometryPattern = false;
        d.InnerRadius = 0.00380422606518061;
        d.InstanceSpacing = 0.0170240490494193;
        d.LayoutSpacingType = (int)swPatternLayoutSpacingType_e.swPatternLayoutInstances;
        d.LoopSpacing = 0.0170240490494193;
        d.Margins = 0;
        d.NoOfInstances = 10;
        d.OuterRadius = 0.004;
        d.PatternLayoutPolygonSides = 0;
        d.PatternLayoutType = (int)swPatternLayoutType_e.swPatternLayoutCircular;
        d.Rotation = 0;
        d.SeedCutFlipShapeDirection = false;
        d.StaggerAngle = 1.0471975511966;
    }

    // ── the table pattern, as Create_Table_Pattern_Example_CSharp.htm but without a coordinate system ──
    private static IFeature Table(Model m)
    {
        var doc = m.Doc;
        doc.ClearSelection2(true);
        if (!doc.Extension.SelectByID2(m.Seed.Name, "BODYFEATURE", 0, 0, 0, true, 4, null, 0)) { Console.WriteLine("      seed not selected"); return null; }
        // two instances, 3 numbers per point; +20 and +40 mm along X stay inside the plate whether read as absolute or as offsets
        var points = new double[] { 0.020, 0, 0, 0.040, 0, 0 };
        var data = doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmTablePattern) as ITablePatternFeatureData;
        if (data == null) { Console.WriteLine("      CreateDefinition(106) -> null"); return null; }
        data.GeometryPattern = false;
        data.PointArray = points;
        data.PropagateVisualProperty = true;
        data.UseCentroid = true;
        return doc.FeatureManager.CreateFeature(data) as IFeature;
    }

    // ── plate 100 x 60 x 12 on the Top plane (grows along Y), one through hole at X = -30 ──
    private static Model BuildPlate()
    {
        var m = new Model();
        string tpl = _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        m.Doc = _sw.NewDocument(tpl, 0, 0, 0) as IModelDoc2;
        var doc = m.Doc;
        if (doc == null) return m;
        if (!SelectBasePlane(doc, 2)) return m;
        doc.SketchManager.InsertSketch(true);
        doc.SketchManager.CreateCornerRectangle(-L / 2, -W / 2, 0, L / 2, W / 2, 0);
        doc.SketchManager.InsertSketch(true);
        doc.ClearSelection2(true);
        doc.Extension.SelectByID2(LastName(doc, "ProfileFeature"), "SKETCH", 0, 0, 0, false, 0, null, 0);
        var boss = doc.FeatureManager.FeatureExtrusion3(true, false, false, (int)swEndConditions_e.swEndCondBlind, 0, H, 0,
            false, false, false, false, 0, 0, false, false, false, false, true, true, true, 0, 0, false) as IFeature;
        if (boss == null) return m;

        m.Top = TopFace(doc, out m.YTop);
        if (m.Top == null) return m;
        doc.ClearSelection2(true);
        ((IEntity)m.Top).Select4(false, null);
        doc.SketchManager.InsertSketch(true);
        var sk = doc.SketchManager.ActiveSketch as ISketch;
        var mu = (IMathUtility)_sw.GetMathUtility();
        var c = ((IMathPoint)((IMathPoint)mu.CreatePoint(new[] { HoleX, m.YTop, 0.0 })).MultiplyTransform(sk.ModelToSketchTransform)).ArrayData as double[];
        doc.SketchManager.CreateCircleByRadius(c[0], c[1], 0, HoleR);
        doc.SketchManager.InsertSketch(true);
        doc.ClearSelection2(true);
        doc.Extension.SelectByID2(LastName(doc, "ProfileFeature"), "SKETCH", 0, 0, 0, false, 0, null, 0);
        var cut = doc.FeatureManager.FeatureCut4(true, false, false, (int)swEndConditions_e.swEndCondThroughAll, 0,
            0.01, 0.01, false, false, false, false, 0, 0, false, false, false, false,
            false, true, true, true, true, false, 0, 0, false, false) as IFeature;
        doc.ClearSelection2(true);
        if (cut == null) return m;
        if (Math.Abs((L * W * H - Math.PI * HoleR * HoleR * H) * 1e9 - Volume(doc)) > 5) return m;   // the hole must really go through

        // faces changed after the cut: read them again
        m.Top = TopFace(doc, out m.YTop);
        foreach (var o in (object[])m.Top.GetEdges())
        {
            var e = (IEdge)o;
            var cv = e.IGetCurve();
            if (cv == null || !cv.IsLine()) continue;
            var sp = ((IVertex)e.GetStartVertex()).GetPoint() as double[];
            var ep = ((IVertex)e.GetEndVertex()).GetPoint() as double[];
            if (Math.Abs(sp[2] - ep[2]) < 1e-7 && Math.Abs(Math.Abs(sp[0] - ep[0]) - L) < 1e-6) { m.EdgeAlongX = e; break; }
        }
        var bodies = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        foreach (var o in (object[])((IBody2)bodies[0]).GetFaces())
        {
            var f = (IFace2)o; var s = f.IGetSurface();
            if (s != null && s.IsCylinder()) { m.Bore = f; break; }
        }
        if (m.EdgeAlongX == null || m.Bore == null) return m;
        m.Seed = cut;
        return m;
    }

    private static bool SelectBasePlane(IModelDoc2 doc, int which)
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

    private static string LastName(IModelDoc2 doc, string type)
    {
        string last = "";
        var f = doc.FirstFeature() as IFeature;
        while (f != null) { if (f.GetTypeName2() == type) last = f.Name; f = f.GetNextFeature() as IFeature; }
        return last;
    }

    private static IFace2 TopFace(IModelDoc2 doc, out double y)
    {
        y = double.MinValue; IFace2 best = null;
        var bodies = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (bodies == null || bodies.Length == 0) return null;
        foreach (var o in (object[])((IBody2)bodies[0]).GetFaces())
        {
            var f = (IFace2)o; var s = f.IGetSurface();
            if (s == null || !s.IsPlane()) continue;
            var bb = f.GetBox() as double[];
            if (bb == null || Math.Abs(bb[1] - bb[4]) > 1e-7) continue;
            if (bb[1] > y) { y = bb[1]; best = f; }
        }
        return best;
    }

    private static HashSet<string> FeatureKeys(IModelDoc2 doc)
    {
        var s = new HashSet<string>();
        var f = doc.FirstFeature() as IFeature;
        while (f != null) { s.Add(f.Name + "|" + f.GetTypeName2()); f = f.GetNextFeature() as IFeature; }
        return s;
    }

    private static int InError(IModelDoc2 doc)
    {
        int n = 0;
        var f = doc.FirstFeature() as IFeature;
        while (f != null) { try { bool w; if (f.GetErrorCode2(out w) != 0 && !w) n++; } catch { } f = f.GetNextFeature() as IFeature; }
        return n;
    }

    private static double Volume(IModelDoc2 doc)
    {
        double v = 0;
        var bodies = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (bodies == null) return 0;
        foreach (var o in bodies) { var mp = ((IBody2)o).GetMassProperties(1000.0) as double[]; if (mp != null && mp.Length > 4) v += mp[3] * 1e9; }
        return v;
    }

    private static int Bodies(IModelDoc2 doc)
    {
        var b = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        return b == null ? 0 : b.Length;
    }

    private static string Fmt(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
