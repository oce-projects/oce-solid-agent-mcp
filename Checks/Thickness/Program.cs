using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.gtswutilities;

// =============================================================================
//  Thickness — the measurement that sizes entry Z-H1 (the minimum wall
//             thickness, with SOLIDWORKS Utilities)
// =============================================================================
//
//  WHY THIS PART
//
//  As for the S3 bracket: the answer is known WITHOUT the computer. A
//  plate 100 x 60 and 2 mm high, and on top, over half of its length, a block
//  10 high. Two extrusions from the top plane, no face to choose:
//
//      minimum thickness = 2.0 mm   (the low half)
//
//  The goal asks for it because a wall that is too thin gives no error and
//  gives way on the boat: it is the defect that cannot be seen, brought into the geometry.
//
//  THE CHECK IN BOTH DIRECTIONS, and it is not an ornament. A single analysis that
//  said «2.0» could be right or lucky. So two thresholds:
//
//      threshold 3.0 mm  -> the 2 mm half is BELOW: critical faces > 0
//      threshold 1.5 mm  -> nothing is below:       critical faces = 0
//
//  If the second finds critical faces, or the first finds none, the number is not
//  a measurement and it is said so.
//
//  ── THE CHAIN, established by reflection on 24/9/2026 ───────────────────────
//
//      ISldWorks.GetAddInObject(CLSID of Utilities.UtilitiesApp) -> IUtilities
//        .GetToolInterface(gtSWThickChk = 7, out err)             -> IThicknessAnalysis
//        .Init()
//        .SetThicknessAnalysisBody(IBody2)
//        .RunThinAnalysis2(soglia, risoluzione, gtResultNoUI, report, ...)
//        .GetMinTckOnAnalArea(out err) / GetNumCriticalFaces(out err)
//        .Close()
//
//  What reflection CANNOT say, and why the probe exists: in which UNITS
//  it wants the threshold (millimetres of the document or metres of the API?) and in which units
//  it answers. It is tried and printed, instead of guessed.
//
//  USAGE
//      .\Checks\Thickness\bin\Thickness.exe
//      .\Checks\Thickness\bin\Thickness.exe --tieni     leaves the part open
//      .\Checks\Thickness\bin\Thickness.exe --carica    loads Utilities into the
//                                                        session if it is not there
// =============================================================================

internal static class Program
{
    // From HKCR\Utilities.UtilitiesApp\CLSID, read on 24/9/2026 (entry K20).
    private const string ClsidUtilities = "{F80FA0F1-B13D-11d4-944A-000629992CFE}";

    // gtSwTools_e / gttckResolutionOptions_e / gtResultOptions_e, per riflessione.
    private const int ThicknessTool = 7;      // gtSWThickChk
    private const int HighResolution = 3;        // gttckHighResolution
    private const int WithoutWindow = 0;          // gtResultNoUI

    private const double LengthMm = 100.0;
    private const double WidthMm = 60.0;
    private const double PlateMm = 2.0;
    private const double BlockMm = 10.0;

    private static int _step;
    private static void Says(string what)
    {
        _step++;
        Console.WriteLine("  " + _step.ToString("00") + ". " + what);
    }

    private static int Main(string[] arguments)
    {
        bool keep = Array.IndexOf(arguments, "--tieni") >= 0;
        bool loadFlag = Array.IndexOf(arguments, "--carica") >= 0;

        Console.WriteLine();
        Console.WriteLine("THICKNESS — the measurement that sizes Z-H1 (SOLIDWORKS Utilities)");
        Console.WriteLine("  plate " + LengthMm + " x " + WidthMm + " x " + PlateMm +
                          " mm, and on half of it a block " + BlockMm + " mm high");
        Console.WriteLine("  minimum thickness = " + PlateMm.ToString("0.0") + " mm   <- known without a calculator");
        Console.WriteLine();

        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex)
        {
            Console.WriteLine("SOLIDWORKS does not respond: " + ex.Message);
            return 2;
        }
        // The positive control on the attachment: if RevisionNumber does not answer,
        // a null further down would say nothing (it happened in S3).
        Says("attached to SOLIDWORKS " + sw.RevisionNumber());

        // ── THE ADD-IN ───────────────────────────────────────────────────
        object addin = sw.GetAddInObject(ClsidUtilities);
        if (addin == null && loadFlag)
        {
            // LOAD INTO THE SESSION, NOT AT STARTUP. `LoadAddIn` turns it on for
            // this session and does not touch AddInsStartup: it is removed by closing
            // SOLIDWORKS, or with UnloadAddIn. Only with --carica: turning on a
            // module in the session of whoever works is their choice, not the
            // probe's.
            string dll = Path.Combine(SolidWorksRoot(sw), "sldUtils", "SwLoaderSw.dll");
            int outcome = sw.LoadAddIn(dll);
            Says("LoadAddIn(" + dll + ") -> " + outcome);
            addin = sw.GetAddInObject(ClsidUtilities);
        }
        if (addin == null)
        {
            Console.WriteLine();
            Console.WriteLine("UTILITIES IS NOT LOADED in this session.");
            Console.WriteLine("  That does not mean it is not there (K20 finds it registered). Tick");
            Console.WriteLine("  'SOLIDWORKS Utilities' in Tools > Add-Ins, or else");
            Console.WriteLine("  rilancia con --carica.");
            return 3;
        }
        Says("Utilities caricato: " + addin.GetType().Name);

        var utility = addin as IUtilities;
        if (utility == null)
        {
            Console.WriteLine("  the add-in's object is not an IUtilities: the chain established by");
            Console.WriteLine("  reflection is not the one that runs.");
            return 5;
        }

        IModelDoc2 doc = null;
        try
        {
            doc = CreatePart(sw);
            if (doc == null) return 4;

            IBody2 body = FirstBody(doc);
            if (body == null) { Console.WriteLine("  no solid body"); return 4; }

            // The volume, so that the part is what one believes before
            // measuring its thickness: 100x60x2 + 50x60x8 = 12000 + 24000 mm^3.
            double[] mass = doc.Extension.GetMassProperties2(1, out int massState, false) as double[];
            double volMm3 = mass == null ? double.NaN : mass[3] * 1e9;
            Says("volume " + volMm3.ToString("0") + " mm^3 (expected 36000)");

            // ── TWO THRESHOLDS, AND IN TWO UNITS ─────────────────────────
            //
            // It is not known whether the threshold goes in millimetres or in metres. Both are
            // tried with the 3 mm threshold: the right one finds critical
            // faces and a minimum around 2; the wrong one either refuses or
            // answers something recognisable as absurd.
            Console.WriteLine();
            Console.WriteLine("  threshold    answer    minimum     maximum     critical faces");
            Console.WriteLine("  ----------   --------  ----------  ----------  --------------");

            // --esplora: a scan of thresholds, to know how
            // the API answers when the threshold drops below the minimum thickness. The first
            // real run gave «15» with the threshold at 1.5 mm, without analysing:
            // before calling it «there is nothing below» or «error» it is looked at
            // for nearby thresholds, above and below 2 mm.
            // `System.Environment` spelled out: the SOLIDWORKS interop has its own
            // `Environment`, and the bare name does not compile (it has already happened to the
            // bracket probe).
            if (Array.IndexOf(System.Environment.GetCommandLineArgs(), "--esplora") >= 0)
            {
                foreach (double s in new[] { 0.0010, 0.0015, 0.0019, 0.00199, 0.0020, 0.00201, 0.0021, 0.0025, 0.0030, 0.0050, 0.0099, 0.0101, 0.0150 })
                    Analyze(utility, body, s, s.ToString("0.00000") + " m");
                return 0;
            }

            // ── WHAT THE API REALLY DOES, measured on 24/9/2026 ────────────
            //
            // The first run tried the threshold in millimetres AND in metres. The
            // answer: **metres**, on input and on output (threshold 0.003 -> 2
            // critical faces, minimum 0.0020). And the threshold «3.0» — that is 3 METRES,
            // more than the whole part — is not harmless: the next call
            // answered 15. Removed.
            //
            // And the opposite direction does not have the shape that the probe expected. A
            // threshold BELOW the minimum thickness does not give «zero critical faces»:
            // the analysis does not start and answers **12009**,
            // gtWarningTckThinAnalRangeBelowMaxThick — a WARNING that says
            // «below this threshold there is nothing». And straddling the minimum
            // (2.00 and 2.01 mm) it answers 15. Measured with --esplora on thirteen
            // thresholds: below 2 mm always 12009; from 2.1 to 9.9 mm always 2 faces and
            // minimum 2.00; above 10 mm, 3 faces. *The probe had written the
            // criterion before knowing how the API says «nothing», and mistook it
            // for a failure: it is the case in which it did well to
            // stop instead of closing.*
            const double Meters = 1000.0;
            var above = Analyze(utility, body, 0.003, "0,003 m");
            var up = Analyze(utility, body, 0.012, "0,012 m");
            var below = Analyze(utility, body, 0.0015, "0,0015 m");
            Console.WriteLine();

            if (above == null || above.BenchRun != 0 || above.Critical <= 0 || !Near(above.Minimum * Meters, PlateMm, 0.2))
            {
                Console.WriteLine("THRESHOLD ABOVE THE MINIMUM: no minimum near " + PlateMm + " mm with critical faces.");
                Console.WriteLine("  The operation is NOT built until we know why.");
                return 6;
            }
            Console.WriteLine("  by hand:  " + PlateMm.ToString("0.00") + " mm");
            Console.WriteLine("  the API:  " + (above.Minimum * Meters).ToString("0.00") + " mm   (it answers in meters)");

            const int NothingBelowThreshold = 12009;   // gtWarningTckThinAnalRangeBelowMaxThick
            if (below == null || below.BenchRun != NothingBelowThreshold)
            {
                Console.WriteLine();
                Console.WriteLine("THE OTHER WAY DOES NOT ADD UP: with the threshold below the minimum thickness the API");
                Console.WriteLine("should have said 12009 (nothing under the threshold). It said " +
                                  (below == null ? "nothing" : below.BenchRun.ToString()) + ".");
                return 7;
            }
            if (up == null || up.BenchRun != 0 || up.Critical < above.Critical)
            {
                Console.WriteLine();
                Console.WriteLine("THE HIGHER THRESHOLD HAS FEWER CRITICAL FACES THAN THE LOW ONE: the faces below");
                Console.WriteLine("a threshold must grow with the threshold, not shrink.");
                return 7;
            }

            Console.WriteLine();
            Console.WriteLine("HOLDS BOTH WAYS: above the minimum it finds the thin faces and the right minimum;");
            Console.WriteLine("below the minimum it says there is nothing (12009); the higher the threshold, the more");
            Console.WriteLine("faces (" + above.Critical + " -> " + up.Critical + "). Entry Z-H1 can be built.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("ECCEZIONE: " + Root(ex).GetType().Name + ": " + Root(ex).Message);
            return 8;
        }
        finally
        {
            if (doc != null && !keep)
            {
                try { sw.CloseDoc(doc.GetTitle()); Says("part closed without saving"); } catch { }
            }
        }
    }

    private sealed class Outcome
    {
        public int BenchRun;                 // the answer of RunThinAnalysis2
        public double Minimum = double.NaN, Maximum = double.NaN;   // in METRES
        public int Critical = -1;
    }

    /// <summary>A thin thickness analysis, with all the outcomes printed.</summary>
    private static Outcome Analyze(IUtilities u, IBody2 body, double threshold, string label)
    {
        int err;
        var t = u.GetToolInterface(ThicknessTool, out err) as IThicknessAnalysis;
        if (t == null)
        {
            Console.WriteLine("  " + label.PadRight(12) + " GetToolInterface -> null, error " + err);
            return null;
        }
        try
        {
            int init = t.Init();
            int bodyOk = t.SetThicknessAnalysisBody((Body2)body);
            int benchRun = t.RunThinAnalysis2(threshold, HighResolution, WithoutWindow, "", false, false, true);
            if (benchRun != 0)
            {
                // A non-zero answer is NOT necessarily an error: 12009 means
                // «below the threshold there is nothing». It is returned, and whoever
                // calls decides.
                Console.WriteLine("  " + label.PadRight(12) + " " + benchRun.ToString().PadLeft(8) +
                                  "  (Init " + init + ", body " + bodyOk + ")");
                return new Outcome { BenchRun = benchRun };
            }
            int e1, e2, e3;
            var r = new Outcome
            {
                BenchRun = 0,
                Minimum = t.GetMinTckOnAnalArea(out e1),
                Maximum = t.GetMaxTckOnAnalArea(out e2),
                Critical = t.GetNumCriticalFaces(out e3),
            };
            Console.WriteLine("  " + label.PadRight(12) + " " + benchRun.ToString().PadLeft(8) + "  " +
                              r.Minimum.ToString("0.0000").PadLeft(10) + "  " +
                              r.Maximum.ToString("0.0000").PadLeft(10) + "  " +
                              r.Critical.ToString().PadLeft(14) +
                              (e1 != 0 || e2 != 0 || e3 != 0 ? "   (err " + e1 + "/" + e2 + "/" + e3 + ")" : ""));
            return r;
        }
        finally
        {
            try { t.Close(); } catch { }
        }
    }

    private static bool Near(double v, double expected, double tolerance)
    {
        return !double.IsNaN(v) && Math.Abs(v - expected) <= tolerance;
    }

    private static IModelDoc2 CreatePart(ISldWorks sw)
    {
        string model = sw.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        var doc = (IModelDoc2)sw.NewDocument(model, 0, 0, 0);
        if (doc == null) { Console.WriteLine("  NewDocument -> null"); return null; }

        // 1. the low plate, whole
        if (!Extrusion(doc, 0, 0, LengthMm, WidthMm, PlateMm, false)) return null;
        // 2. the tall block, on the second half, which joins the plate
        if (!Extrusion(doc, LengthMm / 2.0, 0, LengthMm, WidthMm, BlockMm, true)) return null;

        doc.ViewZoomtofit2();
        Says("part created: plate of " + PlateMm + " mm, block of " + BlockMm + " mm on half");
        return doc;
    }

    /// <summary>A rectangle on the top plane, extruded by `heightMm`.</summary>
    private static bool Extrusion(IModelDoc2 doc, double x1, double y1, double x2, double y2,
                                   double heightMm, bool merge)
    {
        doc.ClearSelection2(true);
        if (!SelectBasePlane(doc, 2))
        {
            // W3.1-bis.2 point 10: the plane is now found by tree position, so a
            // failure here is no longer a language mismatch — the template has
            // fewer than two reference planes, or none is a RefPlane feature.
            Console.WriteLine("  top reference plane not found");
            return false;
        }
        doc.SketchManager.InsertSketch(true);
        doc.SketchManager.CreateCornerRectangle(x1 / 1000.0, y1 / 1000.0, 0, x2 / 1000.0, y2 / 1000.0, 0);
        doc.SketchManager.InsertSketch(true);
        var f = doc.FeatureManager.FeatureExtrusion3(
            true, false, false, 0, 0, heightMm / 1000.0, 0.0,
            false, false, false, false, 0, 0, false, false, false, false,
            merge, true, true, 0, 0, false);
        if (f == null) { Console.WriteLine("  the extrusion of " + heightMm + " mm did not come out"); return false; }
        return true;
    }

    private static IBody2 FirstBody(IModelDoc2 doc)
    {
        var partDoc = doc as IPartDoc;
        var bodies = partDoc == null ? null : partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (bodies == null || bodies.Length == 0) return null;
        if (bodies.Length > 1) Console.WriteLine("      warning: " + bodies.Length + " bodies, the first is analyzed");
        return bodies[0] as IBody2;
    }

    private static string SolidWorksRoot(ISldWorks sw)
    {
        // GetExecutablePath is the folder of SLDWORKS.exe: the right root
        // even with two installed trees, which on this workstation there are.
        try { return sw.GetExecutablePath(); } catch { }
        return @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS";
    }

    private static Exception Root(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex;
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
