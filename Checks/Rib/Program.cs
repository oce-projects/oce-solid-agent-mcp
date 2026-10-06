using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  ProbeRib — the rib ("rib", TODO W1.2), rewritten by the review of 28/9/2026
// =============================================================================
//
//  WHAT THE HISTORICAL PROBES REALLY DID (the three probes of
//  16/9/2026, never in the repo; RAG note 82zz-quinquies): EVERY InsertRib call passed IsNormToSketch: true.
//  The last two even had the right geometry — an L bracket 60x60x10,
//  50 deep, a plane at mid depth (z = 25), a diagonal (10,40)->(40,10) that
//  touches both inner walls and lies in the void — and still asked for an
//  extrusion NORMAL to the sketch, which on that line has nothing to hit.
//  They also never flipped ReverseMaterialDir on the L.
//
//  WHAT THE GUIDE SAYS:
//   - IFeatureManager~InsertRib: "IsNormToSketch — True if extrusion direction
//     is normal to sketch, false if parallel to sketch"; "ReverseMaterialDir —
//     True to flip the direction of the extrusion".
//   - Get_Rib_Feature_Data_Example_CSharp.htm (the only worked example; it
//     creates Shell1, Plane1 and Rib1): InsertRib(true, false, 0.00254, 0,
//     false, false, true, 0.0174532925199433, FALSE, false) — the ninth
//     argument, IsNormToSketch, is FALSE: parallel to the sketch, i.e. the
//     rib grows IN the sketch plane from the line to the walls, and is
//     thickened across it. That is the gusset of an L.
//
//  FIRST DRAFT OF THIS PROBE, fixed here: it built the block on the Top plane
//  (height along +Y) but looked for the top face at constant Z and drew the
//  profile with Y as the horizontal: the shell never happened (A == B) and the
//  profile fell below the part. Now every face is found by geometry and every
//  sketch point goes through ModelToSketchTransform (RAG 81r: "the way to
//  know it is not to reason about it").
//
//  CASES (one fresh part each):
//   L0  L bracket, IsNormToSketch = TRUE (the historical call)       -> control, expected nothing
//   L1  L bracket, IsNormToSketch = false, ReverseMaterialDir = false -> expected +2700 mm3 (450 mm2 x 6)
//   L2  L bracket, IsNormToSketch = false, ReverseMaterialDir = true
//   L3  L bracket, as L1 but the sketch left OPEN at the call (TODO W1.2, "where to restart from")
//   S1  shelled box 40x40x20 (guide shape), line at mid height on the Right plane, parallel, material false
//   S2  as S1, material true                                           -> expected +720 mm3 (36 x 10 x 2)
//   P1  plate 40x40x2 with the line 10 mm above (the first historical probe), parallel, material false
//   P2  as P1, material true   (no walls: tells whether the region must be bounded)
//
//  Ground truth: a NEW feature of type "Rib" (IFeature~GetTypeName2) AND the
//  volume delta. NO EditDelete, no AddDimension, no save.
//
//  USAGE      Checks\Rib\bin\Rib.exe [--keep]
//             (built by Checks\build-checks.ps1 (and by build.ps1, step 1c))
//
//  OUTCOME OF 28/9/2026, SOLIDWORKS 29.5.1 (the output of the probe, as it was):
//
//    PROBE RIB (W1.2) — SOLIDWORKS 29.5.1
//      [  -  ] L0 L, normal to the sketch (like the historical probes) (expected nothing)  ->  «Schizzo2» selected True, Rib none, volume 55000 -> 55000 (0), bodies 1 -> 1
//      [BORN]  L1 L, parallel, material straight (expected +2700)  ->  «Schizzo2» selected True, Rib «Nervatura1», volume 55000 -> 57700 (+2700), bodies 1 -> 1
//      [  -  ] L2 L, parallel, material flipped (expected +2700)  ->  «Schizzo2» selected True, Rib none, volume 55000 -> 55000 (0), bodies 1 -> 1
//      [BORN]  L3 L, parallel, sketch left OPEN (expected +2700)  ->  sketch open, no selection, Rib «Nervatura1», volume 55000 -> 57700 (+2700), bodies 1 -> 1
//      [  -  ] S1 hollowed box, parallel, material straight (expected +720)  ->  «Schizzo2» selected True, Rib none, volume 8672 -> 8672 (0), bodies 1 -> 1
//      [BORN]  S2 hollowed box, parallel, material flipped (expected +720)  ->  «Schizzo2» selected True, Rib «Nervatura1», volume 8672 -> 9392 (+720), bodies 1 -> 1
//      [  -  ] P1 plate without walls, parallel, material straight (expected ?)  ->  «Schizzo2» selected True, Rib none, volume 3200 -> 3200 (0), bodies 1 -> 1
//      [  -  ] P2 plate without walls, parallel, material flipped (expected ?)  ->  «Schizzo2» selected True, Rib none, volume 3200 -> 3200 (0), bodies 1 -> 1
//    SUMMARY: 3/8 cases gave birth to a «Rib».
// =============================================================================

internal static class Program
{
    private enum Shape { LBracket, ShelledBox, Plate }

    private sealed class Case
    {
        public string Name; public Shape Shape;
        public bool NormToSketch, FlipMaterial, LeaveOpen;
        public double ThicknessM; public string Expected;
        public bool Born; public string Outcome;
    }

    private static ISldWorks _sw;

    private static int Main(string[] args)
    {
        bool keep = Array.IndexOf(args, "--keep") >= 0;
        try { _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS does not respond: " + ex.Message); return 2; }
        Console.WriteLine("PROBE RIB (W1.2) — SOLIDWORKS " + _sw.RevisionNumber());

        var cases = new List<Case>
        {
            new Case { Name = "L0 L, normal to the sketch (like the historical probes)", Shape = Shape.LBracket, NormToSketch = true,  FlipMaterial = false, ThicknessM = 0.006, Expected = "nothing" },
            new Case { Name = "L1 L, parallel, material straight",                   Shape = Shape.LBracket, NormToSketch = false, FlipMaterial = false, ThicknessM = 0.006, Expected = "+2700" },
            new Case { Name = "L2 L, parallel, material flipped",                   Shape = Shape.LBracket, NormToSketch = false, FlipMaterial = true,  ThicknessM = 0.006, Expected = "+2700" },
            new Case { Name = "L3 L, parallel, sketch left OPEN",            Shape = Shape.LBracket, NormToSketch = false, FlipMaterial = false, ThicknessM = 0.006, Expected = "+2700", LeaveOpen = true },
            new Case { Name = "S1 hollowed box, parallel, material straight",    Shape = Shape.ShelledBox, NormToSketch = false, FlipMaterial = false, ThicknessM = 0.002, Expected = "+720" },
            new Case { Name = "S2 hollowed box, parallel, material flipped",    Shape = Shape.ShelledBox, NormToSketch = false, FlipMaterial = true,  ThicknessM = 0.002, Expected = "+720" },
            new Case { Name = "P1 plate without walls, parallel, material straight", Shape = Shape.Plate, NormToSketch = false, FlipMaterial = false, ThicknessM = 0.002, Expected = "?" },
            new Case { Name = "P2 plate without walls, parallel, material flipped", Shape = Shape.Plate, NormToSketch = false, FlipMaterial = true,  ThicknessM = 0.002, Expected = "?" },
        };

        foreach (var c in cases)
        {
            try { Run(c, keep); }
            catch (Exception ex) { while (ex.InnerException != null) ex = ex.InnerException; c.Outcome = "EXCEPTION " + ex.Message; }
            Console.WriteLine((c.Born ? "  [BORN]  " : "  [  -  ] ") + c.Name + " (expected " + c.Expected + ")  ->  " + c.Outcome);
        }
        int born = cases.FindAll(x => x.Born).Count;
        Console.WriteLine();
        Console.WriteLine("SUMMARY: " + born + "/" + cases.Count + " cases brought a «Rib» into being.");
        return born > 0 ? 0 : 5;
    }

    private static void Run(Case c, bool keep)
    {
        IModelDoc2 doc = null;
        try
        {
            string tpl = _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            doc = _sw.NewDocument(tpl, 0, 0, 0) as IModelDoc2;
            if (doc == null) { c.Outcome = "NewDocument -> null"; return; }

            // ── the part, and the sketch plane + the two points of the rib line (model coordinates) ──
            IFeature sketchPlane; double[] a, b;
            string built = Build(doc, c.Shape, out sketchPlane, out a, out b);
            if (built != null) { c.Outcome = built; return; }

            var before = FeatureKeys(doc);
            double v0 = Volume(doc);
            int bodies0 = Bodies(doc);

            doc.ClearSelection2(true);
            if (!sketchPlane.Select2(false, 0)) { c.Outcome = "sketch plane not selected"; return; }
            doc.SketchManager.InsertSketch(true);
            var sk = doc.SketchManager.ActiveSketch as ISketch;
            if (sk == null) { c.Outcome = "sketch not open"; return; }
            var mu = (IMathUtility)_sw.GetMathUtility();
            var tr = sk.ModelToSketchTransform;
            Func<double[], double[]> toSk = p => ((IMathPoint)((IMathPoint)mu.CreatePoint(p)).MultiplyTransform(tr)).ArrayData as double[];
            var pa = toSk(a); var pb = toSk(b);
            doc.SketchManager.AddToDB = true;
            var line = doc.SketchManager.CreateLine(pa[0], pa[1], 0, pb[0], pb[1], 0);
            doc.SketchManager.AddToDB = false;
            if (line == null) { c.Outcome = "line not created"; return; }

            string sel = "sketch open, no selection";
            if (!c.LeaveOpen)
            {
                doc.SketchManager.InsertSketch(true);
                string name = LastOfType(doc, "ProfileFeature");
                doc.ClearSelection2(true);
                // exactly as the guide's example: by name and type, mark 0
                bool ok = doc.Extension.SelectByID2(name, "SKETCH", 0, 0, 0, false, 0, null, 0);
                sel = "«" + name + "» selected " + ok;
            }

            string raised = "";
            try
            {
                doc.FeatureManager.InsertRib(
                    Is2Sided: true, ReverseThicknessDir: false, Thickness: c.ThicknessM,
                    ReferenceEdgeIndex: 0, ReverseMaterialDir: c.FlipMaterial,
                    IsDrafted: false, DraftOutward: false, DraftAngle: 0,
                    IsNormToSketch: c.NormToSketch, IsDraftedFromWall: false);
            }
            catch (Exception ex) { raised = " RAISED: " + ex.Message; }
            if (c.LeaveOpen) { try { doc.SketchManager.InsertSketch(true); } catch { } }
            try { doc.EditRebuild3(); } catch { }

            IFeature rib = null;
            var f = doc.FirstFeature() as IFeature;
            while (f != null)
            {
                if (f.GetTypeName2() == "Rib" && !before.Contains(f.Name + "|Rib")) rib = f;
                f = f.GetNextFeature() as IFeature;
            }
            double v1 = Volume(doc);
            c.Born = rib != null;
            string err = "";
            if (rib != null) { bool warn; int code = rib.GetErrorCode2(out warn); if (code != 0) err = " error " + code + (warn ? " (warning)" : ""); }
            c.Outcome = sel + raised + ", Rib " + (rib == null ? "none" : "«" + rib.Name + "»" + err) +
                        ", volume " + Fmt(v0) + " -> " + Fmt(v1) + " (" + (v1 - v0).ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture) + ")" +
                        ", bodies " + bodies0 + " -> " + Bodies(doc);
        }
        finally
        {
            if (doc != null && !keep) { try { _sw.CloseDoc(doc.GetTitle()); } catch { } }
        }
    }

    // Returns null when the part is ready, otherwise the reason it is not.
    private static string Build(IModelDoc2 doc, Shape shape, out IFeature sketchPlane, out double[] a, out double[] b)
    {
        sketchPlane = null; a = null; b = null;
        var sm = doc.SketchManager;
        if (shape == Shape.LBracket)
        {
            // Same L as the second historical probe: front plane (XY), horizontal wall y 0..10, vertical wall x 0..10, 50 deep in Z.
            if (!SelectBasePlane(doc, 1)) return "front plane not selected";
            sm.InsertSketch(true);
            sm.CreateLine(0, 0, 0, 0.060, 0, 0);
            sm.CreateLine(0.060, 0, 0, 0.060, 0.010, 0);
            sm.CreateLine(0.060, 0.010, 0, 0.010, 0.010, 0);
            sm.CreateLine(0.010, 0.010, 0, 0.010, 0.060, 0);
            sm.CreateLine(0.010, 0.060, 0, 0, 0.060, 0);
            sm.CreateLine(0, 0.060, 0, 0, 0, 0);
            if (!Extrude(doc, 0.050)) return "extrusion of the L -> null";
            // plane at mid depth, z = 25
            if (!SelectBasePlane(doc, 1)) return "front plane not selected (2)";
            doc.CreatePlaneAtOffset3(0.025, false, true);
            sketchPlane = LastFeature(doc, "RefPlane");
            if (sketchPlane == null) return "mid-depth plane not created";
            a = new[] { 0.010, 0.040, 0.025 }; b = new[] { 0.040, 0.010, 0.025 };
            return null;
        }

        // Box and plate: rectangle on the Top plane (XZ), extruded along +Y.
        double height = shape == Shape.Plate ? 0.002 : 0.020;
        if (!SelectBasePlane(doc, 2)) return "top plane not selected";
        sm.InsertSketch(true);
        sm.CreateCornerRectangle(-0.020, -0.020, 0, 0.020, 0.020, 0);
        if (!Extrude(doc, height)) return "estruso -> null";
        double[] box = Box(doc);
        if (Math.Abs((box[4] - box[1]) - height) > 1e-6) return "the extrusion did not grow along Y as expected (Y bounding box " + ((box[4] - box[1]) * 1000) + " mm)";
        double yTop = box[4];
        if (shape == Shape.ShelledBox)
        {
            var top = HorizontalFaceAt(doc, yTop);
            if (top == null) return "top face not found";
            doc.ClearSelection2(true);
            ((IEntity)top).Select4(false, null);
            doc.InsertFeatureShell(0.002, false);
            if (Volume(doc) > 0.9 * 40 * 40 * 20) return "the shell did not happen (volume " + Fmt(Volume(doc)) + ")";
        }
        if (!SelectBasePlane(doc, 3)) return "right plane not selected";
        sketchPlane = SelectedFeature(doc);
        if (sketchPlane == null) return "right plane not read";
        double y = box[1] + (shape == Shape.Plate ? 0.012 : 0.012);   // 10 mm above the floor (floor top at y = 2)
        a = new[] { 0.0, y, -0.015 }; b = new[] { 0.0, y, 0.015 };
        return null;
    }

    private static bool Extrude(IModelDoc2 doc, double depth)
    {
        doc.SketchManager.InsertSketch(true);
        string name = LastOfType(doc, "ProfileFeature");
        doc.ClearSelection2(true);
        doc.Extension.SelectByID2(name, "SKETCH", 0, 0, 0, false, 0, null, 0);
        var f = doc.FeatureManager.FeatureExtrusion3(
            true, false, false, (int)swEndConditions_e.swEndCondBlind, 0, depth, 0,
            false, false, false, false, 0, 0, false, false, false, false,
            true, true, true, 0, 0, false) as IFeature;
        doc.ClearSelection2(true);
        return f != null;
    }

    // Base planes are the first three RefPlane features, whatever the language (SwOperations.SelectPlane).
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

    private static IFeature SelectedFeature(IModelDoc2 doc)
    {
        try { return ((ISelectionMgr)doc.SelectionManager).GetSelectedObject6(1, -1) as IFeature; } catch { return null; }
    }

    private static IFeature LastFeature(IModelDoc2 doc, string type)
    {
        IFeature last = null;
        var f = doc.FirstFeature() as IFeature;
        while (f != null) { if (f.GetTypeName2() == type) last = f; f = f.GetNextFeature() as IFeature; }
        return last;
    }

    private static string LastOfType(IModelDoc2 doc, string type) { var f = LastFeature(doc, type); return f == null ? "" : f.Name; }

    private static IFace2 HorizontalFaceAt(IModelDoc2 doc, double y)
    {
        var bodies = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (bodies == null || bodies.Length == 0) return null;
        foreach (var o in (object[])((IBody2)bodies[0]).GetFaces())
        {
            var f = (IFace2)o;
            var bb = f.GetBox() as double[];
            if (bb != null && Math.Abs(bb[1] - bb[4]) < 1e-7 && Math.Abs(bb[1] - y) < 1e-6) return f;
        }
        return null;
    }

    private static HashSet<string> FeatureKeys(IModelDoc2 doc)
    {
        var s = new HashSet<string>();
        var f = doc.FirstFeature() as IFeature;
        while (f != null) { s.Add(f.Name + "|" + f.GetTypeName2()); f = f.GetNextFeature() as IFeature; }
        return s;
    }

    private static double Volume(IModelDoc2 doc)
    {
        double v = 0;
        var bodies = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (bodies == null) return 0;
        foreach (var o in bodies)
        {
            var mp = ((IBody2)o).GetMassProperties(1000.0) as double[];
            if (mp != null && mp.Length > 4) v += mp[3] * 1e9;
        }
        return v;
    }

    private static int Bodies(IModelDoc2 doc)
    {
        var bodies = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        return bodies == null ? 0 : bodies.Length;
    }

    // [xmin, ymin, zmin, xmax, ymax, zmax] in meters, over all solid bodies
    private static double[] Box(IModelDoc2 doc)
    {
        double[] r = null;
        var bodies = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (bodies != null)
            foreach (var o in bodies)
            {
                var bb = ((IBody2)o).GetBodyBox() as double[];
                if (bb == null) continue;
                if (r == null) r = (double[])bb.Clone();
                else { for (int i = 0; i < 3; i++) r[i] = Math.Min(r[i], bb[i]); for (int i = 3; i < 6; i++) r[i] = Math.Max(r[i], bb[i]); }
            }
        return r ?? new double[6];
    }

    private static string Fmt(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
