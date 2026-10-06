using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  EdgeFlange — the edge flange, before writing it (entry GP18)
// =============================================================================
//
//  `InsertSheetMetalEdgeFlange2` wants a list of edges AND a list of
//  profile sketches. The length is there: it is the seventh parameter,
//  `FlangeOffsetDist` (by reflection, night of 25/9/2026), the same quantity
//  as `IEdgeFlangeFeatureData.OffsetDistance` — the first draft of this
//  probe read it as an offset and passed 0. What is not known:
//    1. whether with the sketches null the flange is born, and how long;
//    2. whether changing OffsetDistance and then ModifyDefinition really lengthens it;
//    3. by how much the flat pattern grows, and with which dimension convention
//       (OffsetDimType: outer virtual sharp, inner, tangent).
//
//  THE PART: the L of `Checks/SheetMetal` (50 + 30, T 2, R 2, 40 deep), and
//  a flange at 90 degrees on the edge at the end of the 50 side, brought to 25.
//  The flat pattern before and after, read by unsuppressing «FlatPattern»
//  (SetBendState does not unfold anything: measured).
//
//  USAGE
//      .\Checks\EdgeFlange\bin\EdgeFlange.exe            [--tieni]
// =============================================================================

internal static class Program
{
    private static int _step;
    private static void Says(string what) { _step++; Console.WriteLine("  " + _step.ToString("00") + ". " + what); }

    private static int Main(string[] arguments)
    {
        bool keep = Array.IndexOf(arguments, "--tieni") >= 0;
        // --testa: skips attempts 1-9 (all measured on 25-26/9) and only tries
        // number 10, the profile drawn on the end face.
        bool head = Array.IndexOf(arguments, "--testa") >= 0;
        // --alta=N: the height of the profile rectangle in mm (20 if not given).
        double high = 0.020;
        foreach (var arg in arguments)
            if (arg.StartsWith("--alta=")) high = double.Parse(arg.Substring(7), System.Globalization.CultureInfo.InvariantCulture) / 1000.0;
        Console.WriteLine();
        Console.WriteLine("EDGE FLANGE — before writing it (entry GP18)");
        Console.WriteLine();
        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS does not respond: " + ex.Message); return 2; }

        IModelDoc2 doc = null;
        try
        {
            string model = sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            doc = (IModelDoc2)sw.NewDocument(model, 0, 0, 0);
            doc.ClearSelection2(true);
            int seen = 0;
            var f0 = doc.FirstFeature() as IFeature;
            while (f0 != null) { if (f0.GetTypeName2() == "RefPlane" && ++seen == 1) { f0.Select2(false, 0); break; } f0 = f0.GetNextFeature() as IFeature; }
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.AddToDB = true;
            doc.SketchManager.CreateLine(0, 0.030, 0, 0, 0, 0);
            doc.SketchManager.CreateLine(0, 0, 0, 0.050, 0, 0);
            doc.SketchManager.AddToDB = false;
            doc.SketchManager.InsertSketch(true);
            var base0 = doc.FeatureManager.InsertSheetMetalBaseFlange2(0.002, false, 0.002, 0.040, 0, false,
                (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind, 0, null, true,
                (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0, 0, 0.5, true, false, false, false);
            doc.EditRebuild3();
            double s0 = FlatPattern(doc);
            Says("base flange: " + (base0 != null) + ", flat pattern " + s0.ToString("0.###") + " mm (expected 80,712)");

            // The edge at the end of the 50 side: the L goes from x -2 to 50, the
            // outer edge at the end sits at x = 50, along Z, on the face
            // below (y = -2) or above (y = 0). The one at y = 0 is taken.
            doc.ClearSelection2(true);
            IEdge chosen = null;
            var bodies = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            foreach (var o in (bodies[0] as IBody2).GetEdges() as object[])
            {
                var e = o as IEdge;
                var a = (e.GetStartVertex() as IVertex)?.GetPoint() as double[];
                var b = (e.GetEndVertex() as IVertex)?.GetPoint() as double[];
                if (a == null || b == null) continue;
                if (Math.Abs(a[0] - 0.050) < 1e-6 && Math.Abs(b[0] - 0.050) < 1e-6 &&
                    Math.Abs(a[1]) < 1e-6 && Math.Abs(b[1]) < 1e-6 && Math.Abs(Math.Abs(b[2] - a[2]) - 0.040) < 1e-6)
                { chosen = e; break; }
            }
            if (chosen == null) { Console.WriteLine("  cannot find the edge at the end of the 50 side"); return 4; }
            Says("edge found: x 50, y 0, along Z for 40");

            // THREE ATTEMPTS, because the first run gave null with the sketches
            // null and does not say why.
            int options = (int)(swInsertEdgeFlangeOptions_e.swInsertEdgeFlangeUseDefaultRadius |
                                swInsertEdgeFlangeOptions_e.swInsertEdgeFlangeUseDefaultRelief);
            // ATTEMPTS 5 AND 6 (night of 25/9/2026). By reflection the seventh
            // parameter is called `FlangeOffsetDist`, and it is the same quantity
            // as `IEdgeFlangeFeatureData.OffsetDistance`: the LENGTH of the
            // flange. The first four attempts passed 0 — a flange of length
            // zero, which is not born without saying why. Here 20 mm.
            // ATTEMPTS 7 AND 8 (26/9/2026). The trap already paid for with the
            // booleans (memory «typed arrays»): a list passed as
            // `object[]` makes it answer null without an exception, and it wants the real
            // type — `Edge[]` for the edges, `Feature[]` for the sketches.
            IFeature flange = null;
            for (int t = 1; t <= 8 && flange == null && !head; t++)
            {
                doc.ClearSelection2(true);
                if (t >= 2) (chosen as IEntity).Select4(false, null);
                object sketches = t == 3 || t == 6 ? (object)new object[] { null }
                               : t == 8 ? (object)new Feature[] { null } : null;
                double length = t >= 5 ? 0.020 : 0;
                object edges = t >= 7 ? (object)new Edge[] { (Edge)chosen } : new object[] { chosen };
                try
                {
                    flange = t == 4
                        ? doc.FeatureManager.InsertSheetMetalEdgeFlange((Edge)chosen, null, options,
                            Math.PI / 2, 0.002, (int)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside, 0,
                            (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0.5, 0, 0,
                            (int)swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp, null) as IFeature
                        : doc.FeatureManager.InsertSheetMetalEdgeFlange2(edges, sketches, options,
                            Math.PI / 2, 0.002, (int)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside, length,
                            (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0.5, 0, 0,
                            (int)swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp, null) as IFeature;
                }
                catch (Exception ex) { Console.WriteLine("      tentativo " + t + " alza: " + ex.Message); }
                Console.WriteLine("      attempt " + t + " (" + (t == 1 ? "nothing selected, sketches null"
                    : t == 2 ? "edge selected, sketches null" : t == 3 ? "edge selected, sketches [null]"
                    : t == 4 ? "InsertSheetMetalEdgeFlange, the old one"
                    : t == 5 ? "edge selected, sketches null, LONG 20"
                    : t == 6 ? "edge selected, sketches [null], LONG 20"
                    : t == 7 ? "Edge[], sketches null, LONG 20"
                    : "Edge[], Feature[]{null}, LONG 20") + "): " + (flange == null ? "null" : flange.Name));
            }
            // ATTEMPT 9: the PROFILE SKETCH, as the TODO says. A plane
            // perpendicular to the edge at its start vertex (marks 0 and
            // 1, constraints perpendicular + coincident), and on the plane a line of
            // 20 mm starting from the vertex towards +Y: the direction of the flange.
            if (flange == null && !head)
            {
                var v0 = (chosen.GetStartVertex() as IVertex);
                var p0 = v0?.GetPoint() as double[];
                var selMgr = (ISelectionMgr)doc.SelectionManager;
                doc.ClearSelection2(true);
                var sd0 = (SelectData)selMgr.CreateSelectData(); sd0.Mark = 0;
                var sd1 = (SelectData)selMgr.CreateSelectData(); sd1.Mark = 1;
                bool sel0 = (chosen as IEntity).Select4(false, sd0);
                bool sel1 = (v0 as IEntity).Select4(true, sd1);
                var plane = doc.FeatureManager.InsertRefPlane(
                    (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Perpendicular, 0,
                    (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Coincident, 0, 0, 0) as IFeature;
                Console.WriteLine("      attempt 9: selection " + sel0 + "/" + sel1 + ", plane " + (plane == null ? "NOT CREATED" : plane.Name) +
                                  ", vertex " + (p0 == null ? "?" : string.Join(" ", Array.ConvertAll(p0, x => (x * 1000).ToString("0.#")))));
                if (plane != null && p0 != null)
                {
                    doc.ClearSelection2(true);
                    plane.Select2(false, 0);
                    doc.SketchManager.InsertSketch(true);
                    var sk = doc.SketchManager.ActiveSketch as ISketch;
                    var mu = (IMathUtility)sw.GetMathUtility();
                    var tr = sk.ModelToSketchTransform;
                    Func<double, double, double, double[]> inSketch = (x, y, z) =>
                        ((IMathPoint)((IMathPoint)mu.CreatePoint(new[] { x, y, z })).MultiplyTransform(tr)).ArrayData as double[];
                    var a = inSketch(p0[0], p0[1], p0[2]);
                    var b = inSketch(p0[0], p0[1] + 0.020, p0[2]);
                    doc.SketchManager.AddToDB = true;
                    var line = doc.SketchManager.CreateLine(a[0], a[1], 0, b[0], b[1], 0);
                    doc.SketchManager.AddToDB = false;
                    doc.SketchManager.InsertSketch(true);
                    IFeature sketch = null;
                    var fs = doc.FirstFeature() as IFeature;
                    while (fs != null) { if (fs.GetTypeName2() == "ProfileFeature") sketch = fs; fs = fs.GetNextFeature() as IFeature; }
                    Console.WriteLine("      attempt 9: line " + (line != null) + ", sketch " + (sketch == null ? "?" : sketch.Name));
                    doc.ClearSelection2(true);
                    (chosen as IEntity).Select4(false, null);
                    try
                    {
                        flange = doc.FeatureManager.InsertSheetMetalEdgeFlange2(new Edge[] { (Edge)chosen },
                            new Feature[] { (Feature)sketch }, options,
                            Math.PI / 2, 0.002, (int)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside, 0.020,
                            (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0.5, 0, 0,
                            (int)swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp, null) as IFeature;
                    }
                    catch (Exception ex) { Console.WriteLine("      attempt 9 raises: " + ex.Message); }
                    Console.WriteLine("      attempt 9 (Edge[], Feature[]{schizzo di profilo}, LONG 20): " + (flange == null ? "null" : flange.Name));
                }
            }

            // ATTEMPT 10 (26/9/2026). Number 9 makes a «Bordo-Flangia» be born that
            // adds no material, and SOLIDWORKS replaces the sketch with one of
            // its own: the line on a perpendicular plane is not a profile. In the
            // window the profile of the edge flange is a RECTANGLE on the
            // plane of the flange, with the base side on the edge. Here: the end
            // face of the 50 side (x = 50, y from -2 to 0) as sketch plane,
            // and the rectangle from (y 0, z 0) to (y 20, z 40).
            if (flange == null && head)
            {
                doc.ClearSelection2(true);
                bool face = doc.Extension.SelectByID2("", "FACE", 0.050, -0.001, 0.020, false, 0, null, 0);
                doc.SketchManager.InsertSketch(true);
                var sk = doc.SketchManager.ActiveSketch as ISketch;
                Console.WriteLine("      attempt 10: end face " + face + ", sketch " + (sk != null));
                if (sk != null)
                {
                    var mu = (IMathUtility)sw.GetMathUtility();
                    var tr = sk.ModelToSketchTransform;
                    Func<double, double, double, double[]> inSketch = (x, y, z) =>
                        ((IMathPoint)((IMathPoint)mu.CreatePoint(new[] { x, y, z })).MultiplyTransform(tr)).ArrayData as double[];
                    var a = inSketch(0.050, 0, 0);
                    var b = inSketch(0.050, high, 0.040);
                    doc.SketchManager.AddToDB = true;
                    var rect = doc.SketchManager.CreateCornerRectangle(a[0], a[1], 0, b[0], b[1], 0) as object[];
                    doc.SketchManager.AddToDB = false;
                    doc.SketchManager.InsertSketch(true);
                    IFeature sketch = null;
                    var fs = doc.FirstFeature() as IFeature;
                    while (fs != null) { if (fs.GetTypeName2() == "ProfileFeature") sketch = fs; fs = fs.GetNextFeature() as IFeature; }
                    Console.WriteLine("      attempt 10: rectangle " + (rect == null ? "null" : rect.Length + " lines") +
                                      ", sketch " + (sketch == null ? "?" : sketch.Name));
                    doc.ClearSelection2(true);
                    try
                    {
                        flange = doc.FeatureManager.InsertSheetMetalEdgeFlange2(new Edge[] { (Edge)chosen },
                            new Feature[] { (Feature)sketch }, options,
                            Math.PI / 2, 0.002, (int)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside, 0.020,
                            (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0.5, 0, 0,
                            (int)swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp, null) as IFeature;
                    }
                    catch (Exception ex) { Console.WriteLine("      attempt 10 raises: " + ex.Message); }
                    Console.WriteLine("      attempt 10 (rectangle on the end): " + (flange == null ? "null" : flange.Name));
                }
            }

            doc.EditRebuild3();
            // THE TREE, afterwards: did the failed attempts leave anything? Is the
            // flange that was born in error? And the volume, which says whether the flange
            // really added material.
            Console.WriteLine("      albero:");
            var fa = doc.FirstFeature() as IFeature;
            while (fa != null)
            {
                bool warning; int code = 0;
                try { code = fa.GetErrorCode2(out warning); } catch { warning = false; }
                string typ = fa.GetTypeName2();
                if (typ != "RefPlane" && typ != "OriginProfileFeature" && !typ.EndsWith("Folder") &&
                    typ != "MaterialFolder" && typ != "DetailCabinet" && typ != "SensorFolder" &&
                    typ != "HistoryFolder" && typ != "SelectionSetFolder" && typ != "EqnFolder")
                    Console.WriteLine("        " + fa.Name + " [" + typ + "]" + (code != 0 ? " ERRORE " + code + (warning ? " (avviso)" : "") : ""));
                fa = fa.GetNextFeature() as IFeature;
            }
            var bodiesAfter = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            var mass = bodiesAfter == null ? null : (bodiesAfter[0] as IBody2).GetMassProperties(1) as double[];
            double volume = mass == null ? -1 : mass[3] * 1e9;
            Console.WriteLine("      volume now " + volume.ToString("0.##") + " mm3 (the L alone: 80,712 x 2 x 40 = 6457)");
            if (flange == null) { Console.WriteLine("  none of the attempts brings the flange into being"); return 5; }
            var data = flange.GetDefinition() as IEdgeFlangeFeatureData;
            double s1 = FlatPattern(doc);
            Says("edge flange created: «" + flange.Name + "», length " + (data.OffsetDistance * 1000).ToString("0.###") +
                 " mm, dimension type " + data.OffsetDimType + ", flat pattern " + s1.ToString("0.###"));

            data.AccessSelections(doc, null);
            data.OffsetDistance = 0.025;
            bool mod = flange.ModifyDefinition(data, doc, null);
            doc.EditRebuild3();
            double s2 = FlatPattern(doc);
            var data2 = flange.GetDefinition() as IEdgeFlangeFeatureData;
            Says("OffsetDistance -> 25: ModifyDefinition " + mod + ", read back " + (data2.OffsetDistance * 1000).ToString("0.###") +
                 " mm, flat pattern " + s2.ToString("0.###") + " (grew by " + (s2 - s0).ToString("0.###") + ")");

            // THE COUNT, measured on 26/9/2026 (attempt 10). The length that the
            // flange declares (OffsetDistance, at the outer virtual sharp) is
            // the height of the profile plus the thickness; with the position «material
            // inside» the 50 side loses its R + T at the end. The flat pattern
            // grows by (Lo - R - T) + BA - (R + T). ModifyDefinition on
            // OffsetDistance answers true and changes nothing: the length is
            // commanded by the profile.
            double ba = Math.PI / 2 * (2 + 0.5 * 2);
            double lo = high * 1000 + 2;
            Console.WriteLine();
            Console.WriteLine("  expected length at the outer virtual edge: " + lo.ToString("0.###") + " mm (profile + T)");
            Console.WriteLine("  expected flat pattern: +" + (lo - 2 - 2 + ba - 2 - 2).ToString("0.###") + " mm");
            Console.WriteLine("  measured:        +" + (s1 - s0).ToString("0.###") + " mm");
            return 0;
        }
        catch (Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            Console.WriteLine("ECCEZIONE: " + ex.GetType().Name + ": " + ex.Message);
            return 9;
        }
        finally
        {
            if (doc != null && !keep) try { sw.CloseDoc(doc.GetTitle()); } catch { }
        }
    }

    private static double FlatPattern(IModelDoc2 doc)
    {
        IFeature fp = null;
        var f = doc.FirstFeature() as IFeature;
        while (f != null && fp == null) { if (f.GetTypeName2() == "FlatPattern") fp = f; f = f.GetNextFeature() as IFeature; }
        if (fp == null) return double.NaN;
        fp.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature, (int)swInConfigurationOpts_e.swThisConfiguration, null);
        doc.EditRebuild3();
        var bodies = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
        var b = (bodies[0] as IBody2).GetBodyBox() as double[];
        double l = Math.Max(Math.Abs(b[3] - b[0]), Math.Max(Math.Abs(b[4] - b[1]), Math.Abs(b[5] - b[2]))) * 1000;
        fp.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature, (int)swInConfigurationOpts_e.swThisConfiguration, null);
        doc.EditRebuild3();
        return l;
    }
}
