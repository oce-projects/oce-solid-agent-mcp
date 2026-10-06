using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  Sheet metal — the measurement that sizes the sheet-metal family (entry GP18, step 2)
// =============================================================================
//
//  On 24/9/2026 it became known that the shop bends sheet metal as a recurring
//  job. The family produces the file that goes to the cutting machine, and
//  is therefore the one in which a silent defect costs real sheet: before
//  building the operations, ONE measurement that can be done by hand.
//
//  THE PART: a base flange from an open L-shaped sketch — one side of 50 mm, one
//  of 30 — thickness T, inner radius R, 40 mm deep. Flattened, it is a
//  rectangle long
//
//      L_flat = leg_1 + leg_2 + BA,    BA = pi/2 * (R + K*T)   (90 degrees)
//
//  where the two segments depend on WHAT the sketch represents: the inner faces
//  (segments = side - R) or the outer ones (segments = side - R - T). It is not
//  guessed: both are computed, and the measurement says which convention SOLIDWORKS
//  uses — as the thickness probe said in which units it works.
//
//  T, R and K are NOT taken from here: they are READ BACK from the sheet metal created. If the
//  document template has a K different from the one that one expects, the hand
//  calculation is done with the real K.
//
//  THE CHECK IN BOTH DIRECTIONS: the flattened volume must be equal to the
//  bent one (the sheet does not lengthen or shorten in the count: it is the
//  definition of neutral fibre). A rectangle of the right length with the
//  wrong volume would mean that something else was measured.
//
//  USAGE
//      .\Checks\SheetMetal\bin\SheetMetal.exe            [--keep]
// =============================================================================

internal static class Program
{
    private const double Side1Mm = 50.0;
    private const double Side2Mm = 30.0;
    private const double DepthMm = 40.0;
    private const double ThicknessMm = 2.0;
    private const double RadiusMm = 2.0;

    private static int _step;
    private static void Says(string what)
    {
        _step++;
        Console.WriteLine("  " + _step.ToString("00") + ". " + what);
    }

    private static int Main(string[] arguments)
    {
        bool keep = Array.IndexOf(arguments, "--keep") >= 0;
        Console.WriteLine();
        Console.WriteLine("SHEET METAL — the measurement that sizes the family (entry GP18)");
        Console.WriteLine("  L base flange: " + Side1Mm + " + " + Side2Mm + " mm, depth " + DepthMm +
                          ", thickness " + ThicknessMm + ", radius " + RadiusMm);
        Console.WriteLine();

        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS does not respond: " + ex.Message); return 2; }
        Says("attached to SOLIDWORKS " + sw.RevisionNumber());

        IModelDoc2 doc = null;
        try
        {
            string model = sw.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            doc = (IModelDoc2)sw.NewDocument(model, 0, 0, 0);
            if (doc == null) { Console.WriteLine("  NewDocument -> null"); return 4; }

            // ── the open L-shaped sketch, on the front plane ─────────────
            doc.ClearSelection2(true);
            if (!SelectBasePlane(doc, 1))
            { Console.WriteLine("  cannot find «Piano frontale»"); return 4; }
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateLine(0, Side2Mm / 1000.0, 0, 0, 0, 0);
            doc.SketchManager.CreateLine(0, 0, 0, Side1Mm / 1000.0, 0, 0);
            doc.SketchManager.InsertSketch(true);

            var flange = doc.FeatureManager.InsertSheetMetalBaseFlange2(
                ThicknessMm / 1000.0, false, RadiusMm / 1000.0, DepthMm / 1000.0, 0.0, false,
                (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind, 0,
                null, true, (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular,
                0.0, 0.0, 0.5, true, false, false, false);
            if (flange == null) { Console.WriteLine("  InsertSheetMetalBaseFlange2 -> null"); return 5; }
            doc.EditRebuild3();
            Says("base flange created: " + flange.Name);

            // ── T, R, K READ BACK from the sheet metal ───────────────────
            ISheetMetalFeatureData data = null;
            var f = doc.FirstFeature() as IFeature;
            while (f != null && data == null)
            {
                if (f.GetTypeName2() == "SheetMetal") data = f.GetDefinition() as ISheetMetalFeatureData;
                f = f.GetNextFeature() as IFeature;
            }
            if (data == null) { Console.WriteLine("  no «SheetMetal» feature in the tree"); return 5; }
            double t = data.Thickness * 1000.0, r = data.BendRadius * 1000.0, k = data.KFactor;
            int typeBa = data.BendAllowanceType;
            Says("read back: T " + t.ToString("0.###") + " mm, R " + r.ToString("0.###") + " mm, K " +
                 k.ToString("0.###") + ", bend calculation type " + typeBa + " (2 = K factor)");

            double bentVol = Volume(doc);
            Says("bent volume " + bentVol.ToString("0.0") + " mm^3");

            // ── FLATTENED ────────────────────────────────────────────────
            //
            // MEASURED on the evening of 24/9: `SetBendState(flattened)` answers 0 and the
            // body stays BENT (bounding box 32 x 40 x 52 before and after). In the
            // sheet metal of today the flat pattern is a FEATURE at the bottom of the tree,
            // suppressed: it is flattened by unsuppressing it. SetBendState is tried
            // all the same and what it did is said, then the feature.
            int stateBefore = doc.GetBendState();
            int outcome = doc.SetBendState((int)swSMBendState_e.swSMBendStateFlattened);
            doc.EditRebuild3();
            var afterSet = BoundingBox(doc);
            Says("GetBendState " + stateBefore + ", SetBendState(flattened) -> " + outcome + ", GetBendState " +
                 doc.GetBendState() + ", bounding box " + Three(afterSet));
            doc.SetBendState((int)swSMBendState_e.swSMBendStateFolded);
            doc.EditRebuild3();

            IFeature flatPattern = null;
            var g = doc.FirstFeature() as IFeature;
            while (g != null && flatPattern == null)
            {
                if (g.GetTypeName2() == "FlatPattern") flatPattern = g;
                g = g.GetNextFeature() as IFeature;
            }
            if (flatPattern == null) { Console.WriteLine("  no «FlatPattern» feature in the tree"); return 5; }
            bool removed = flatPattern.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                (int)swInConfigurationOpts_e.swThisConfiguration, null);
            doc.EditRebuild3();
            double[] boundingBox = BoundingBox(doc);
            double flatVol = Volume(doc);
            Says("«" + flatPattern.Name + "» without suppression: " + removed + ", flat volume " +
                 flatVol.ToString("0.0") + " mm^3");
            flatPattern.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,
                (int)swInConfigurationOpts_e.swThisConfiguration, null);
            doc.EditRebuild3();
            if (boundingBox == null) { Console.WriteLine("  no readable bounding box"); return 6; }
            Array.Sort(boundingBox);
            double length = boundingBox[2];
            Console.WriteLine("      flat bounding box: " + boundingBox[0].ToString("0.###") + " x " +
                              boundingBox[1].ToString("0.###") + " x " + boundingBox[2].ToString("0.###") + " mm");

            // ── the hand calculation, in the two conventions ─────────────
            double ba = Math.PI / 2.0 * (r + k * t);
            double inner = (Side1Mm - r) + (Side2Mm - r) + ba;
            double outer = (Side1Mm - r - t) + (Side2Mm - r - t) + ba;
            Console.WriteLine();
            Console.WriteLine("  BA = pi/2 * (R + K*T) = " + ba.ToString("0.###") + " mm");
            Console.WriteLine("  sketch = inner faces:     " + inner.ToString("0.###") + " mm   (deviation " +
                              (length - inner).ToString("+0.###;-0.###") + ")");
            Console.WriteLine("  sketch = outer faces:     " + outer.ToString("0.###") + " mm   (deviation " +
                              (length - outer).ToString("+0.###;-0.###") + ")");
            Console.WriteLine("  the API, flat pattern:    " + length.ToString("0.###") + " mm");

            // ── the DXF ──────────────────────────────────────────────────
            //
            // MEASURED on the evening of 24/9: `ExportFlatPatternView` on a NEVER
            // SAVED part opens «Save As» and the COM call does not return until
            // somebody closes the window. It is saved first; and the call is
            // made all the same from another thread, with a maximum time, so that a
            // second modal not yet known does not hang the probe.
            string savedPart = Path.Combine(Path.GetTempPath(), "sheet-probe.SLDPRT");
            try { if (File.Exists(savedPart)) File.Delete(savedPart); } catch { }
            int se = 0, knows = 0;
            bool saved = doc.Extension.SaveAs(savedPart, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref se, ref knows);
            Says("part saved before the DXF: " + saved + " (errors " + se + ")");

            string dxf = Path.Combine(Path.GetTempPath(), "sheet-probe.dxf");
            try { if (File.Exists(dxf)) File.Delete(dxf); } catch { }
            bool exported = false;
            string exception = null;
            var wire = new System.Threading.Thread(() =>
            {
                try
                {
                    exported = ((IPartDoc)doc).ExportFlatPatternView(dxf,
                        (int)swExportFlatPatternViewOptions_e.swExportFlatPatternOption_RemoveBends);
                }
                catch (Exception ex) { exception = ex.Message; }
            });
            wire.IsBackground = true;
            wire.Start();
            if (!wire.Join(60000))
            {
                Console.WriteLine("  ExportFlatPatternView DID NOT RETURN in 60 s: almost certainly an open modal.");
                Console.WriteLine("  Close it by hand. I am stopping without closing the part.");
                return 10;
            }
            if (exception != null) Console.WriteLine("      ExportFlatPatternView raises: " + exception);
            long weight = File.Exists(dxf) ? new FileInfo(dxf).Length : 0;
            Says("ExportFlatPatternView -> " + exported + ", DXF " + weight + " byte");

            Console.WriteLine();
            bool equalVolumes = bentVol > 0 && Math.Abs(flatVol - bentVol) / bentVol < 0.005;
            string convention = Math.Abs(length - inner) <= 0.5 ? "inner faces"
                               : Math.Abs(length - outer) <= 0.5 ? "outer faces" : null;
            if (!equalVolumes)
            {
                Console.WriteLine("THE VOLUME IS NOT CONSERVED: bent " + bentVol.ToString("0.0") +
                                  ", flat " + flatVol.ToString("0.0") + ". The length above is not");
                Console.WriteLine("the measure of a flat pattern: the family is NOT built until we know why.");
                return 7;
            }
            if (convention == null)
            {
                Console.WriteLine("THE FLAT PATTERN MATCHES NEITHER OF THE TWO CONVENTIONS within 0,5 mm.");
                Console.WriteLine("The family is NOT built until we know why.");
                return 6;
            }
            if (!exported || weight == 0)
            {
                Console.WriteLine("The flat pattern checks out (" + convention + "), but the DXF did not come out.");
                return 8;
            }
            Console.WriteLine("HOLDS: the flat pattern is " + length.ToString("0.###") + " mm like the hand calculation with the");
            Console.WriteLine("sketch on the " + convention + ", the volume is conserved, and the DXF comes out.");
            Console.WriteLine("The sheet metal family can be built.");
            return 0;
        }
        catch (Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            Console.WriteLine();
            Console.WriteLine("EXCEPTION: " + ex.GetType().Name + ": " + ex.Message);
            return 9;
        }
        finally
        {
            if (doc != null && !keep)
            {
                try { sw.CloseDoc(doc.GetTitle()); Says("part closed without saving"); } catch { }
                try
                {
                    var f = Path.Combine(Path.GetTempPath(), "sheet-probe.SLDPRT");
                    if (File.Exists(f)) File.Delete(f);
                }
                catch { }
            }
        }
    }

    private static string Three(double[] b)
    {
        return b == null ? "-" : b[0].ToString("0.###") + " x " + b[1].ToString("0.###") + " x " + b[2].ToString("0.###");
    }

    private static double Volume(IModelDoc2 doc)
    {
        try
        {
            var m = doc.Extension.CreateMassProperty();
            return m == null ? double.NaN : m.Volume * 1e9;
        }
        catch { return double.NaN; }
    }

    private static double[] BoundingBox(IModelDoc2 doc)
    {
        var partDoc = doc as IPartDoc;
        var bodies = partDoc == null ? null : partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
        if (bodies == null || bodies.Length == 0) return null;
        var box = (bodies[0] as IBody2).GetBodyBox() as double[];
        if (box == null || box.Length < 6) return null;
        return new[]
        {
            Math.Abs(box[3] - box[0]) * 1000.0,
            Math.Abs(box[4] - box[1]) * 1000.0,
            Math.Abs(box[5] - box[2]) * 1000.0,
        };
    }

    // W3.1-bis.2 point 10: base planes by position, not by their localized name.
    // The first three RefPlane features of the tree, same rule as
    // Checks/Rib, Checks/Fill and Checks/ExtrusionType.
    // Since Z-C21.5 the library itself (Oce.Solid.Agent.Core/References.cs,
    // BasePlane) recognises a base plane by its canonical normal through the
    // world origin instead, because a template with an extra plane ahead of the
    // three defaults fools a position-based rule with no error; this probe
    // stays positional because it is a throwaway part on SOLIDWORKS' own
    // default template and cannot reference the library (single-file compile,
    // Checks/build-checks.ps1).
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
}
