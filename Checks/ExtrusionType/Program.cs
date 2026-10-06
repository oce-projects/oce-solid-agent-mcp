using System;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  probe_cambia_estrusione_base — what IExtrudeFeatureData2.IsBossFeature says
//  for the FIRST extrusion of a part (the base), for a later boss and for a cut
// =============================================================================
//
//  Full bench of 28/9/2026 on library 0.138.0: blind_becomes_through, step 6,
//  «through_all» on the base plate was NOT refused. Z-C21.2 had moved the cut/boss
//  test of edit_extrusion from the type name to IsBossFeature() for every
//  extrusion; on the ICE boss (boss_grows_and_is_not_a_cut) it answers
//  true. Hypothesis: on the base extrusion it answers false. The page
//  (IExtrudeFeatureData2~IsBossFeature.html) has no Remarks.
//
//  The probe builds: plate 120 x 80 x 10 on the Top plane (the base), a boss
//  20 x 20 x 20 on the same plane (a second «Extrusion»), a cut Ø8 through all
//  in both directions. For each one it prints GetTypeName2, GetTypeName and
//  IsBossFeature, before and after AccessSelections.
//  To be run with the bench STOPPED. --keep leaves the part open.
//
//  MEASURED on 28/9/2026 (SOLIDWORKS 29.5.1), same answers before and after
//  AccessSelections:
//    base    GetTypeName2 Extrusion  GetTypeName Extrusion  IsBossFeature False
//    boss    GetTypeName2 ICE        GetTypeName Boss       IsBossFeature True
//    cut     GetTypeName2 ICE        GetTypeName Cut        IsBossFeature False
//  Every extrusion after the base is "ICE" for GetTypeName2, even one made from
//  a sketch on a plane. EditExtrusionOperations.cs asks IsBossFeature only for
//  "ICE"; "Extrusion" is the base, neither a boss for IsBossFeature nor a cut.
internal static class Program
{
    private static ISldWorks _sw;

    private static int Main(string[] args)
    {
        bool keep = Array.IndexOf(args, "--keep") >= 0;
        try { _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS does not respond: " + ex.Message); return 2; }
        Console.WriteLine("PROBE IsBossFeature — SOLIDWORKS " + _sw.RevisionNumber());
        string tpl = _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        var doc = _sw.NewDocument(tpl, 0, 0, 0) as IModelDoc2;
        if (doc == null) { Console.WriteLine("NewDocument -> null"); return 3; }
        try
        {
            var sm = doc.SketchManager;
            // base plate
            if (!SelectBasePlane(doc, 2)) { Console.WriteLine("top plane not selected"); return 4; }
            sm.InsertSketch(true);
            sm.CreateCenterRectangle(0, 0, 0, 0.060, 0.040, 0);
            var basePlate = Extrude(doc, 0.010);
            Console.WriteLine("base: " + (basePlate == null ? "null" : basePlate.Name) + ", volume " + Fmt(Volume(doc)));
            // second boss on the same plane
            if (!SelectBasePlane(doc, 2)) { Console.WriteLine("top plane not selected (2)"); return 4; }
            sm.InsertSketch(true);
            sm.CreateCenterRectangle(0.030, 0, 0, 0.040, 0.010, 0);
            var boss = Extrude(doc, 0.020);
            Console.WriteLine("rialzo: " + (boss == null ? "null" : boss.Name) + ", volume " + Fmt(Volume(doc)));
            // cut through all, both directions
            if (!SelectBasePlane(doc, 2)) { Console.WriteLine("top plane not selected (3)"); return 4; }
            sm.InsertSketch(true);
            sm.CreateCircleByRadius(-0.030, 0, 0, 0.004);
            sm.InsertSketch(true);
            doc.ClearSelection2(true);
            doc.Extension.SelectByID2(LastOfType(doc, "ProfileFeature"), "SKETCH", 0, 0, 0, false, 0, null, 0);
            var cut = doc.FeatureManager.FeatureCut4(
                false, false, false,
                (int)swEndConditions_e.swEndCondThroughAll, (int)swEndConditions_e.swEndCondThroughAll, 0, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                false, true, true, false, false, false,
                (int)swStartConditions_e.swStartSketchPlane, 0, false, true) as IFeature;
            doc.ClearSelection2(true);
            Console.WriteLine("taglio: " + (cut == null ? "null" : cut.Name) + ", volume " + Fmt(Volume(doc)));

            foreach (var f in new[] { basePlate, boss, cut })
            {
                if (f == null) continue;
                string t2 = "", t1 = "";
                try { t2 = f.GetTypeName2(); } catch { }
                try { t1 = f.GetTypeName(); } catch { }
                var d = f.GetDefinition() as IExtrudeFeatureData2;
                string b0 = "?", b1 = "?";
                try { b0 = d.IsBossFeature().ToString(); } catch (Exception ex) { b0 = "ALZA " + ex.Message; }
                try
                {
                    bool acc = d.AccessSelections(doc, null);
                    b1 = d.IsBossFeature() + " (AccessSelections " + acc + ")";
                    d.ReleaseSelectionAccess();
                }
                catch (Exception ex) { b1 = "ALZA " + ex.Message; }
                Console.WriteLine("  «" + f.Name + "» GetTypeName2=" + t2 + " GetTypeName=" + t1 +
                                  " IsBossFeature=" + b0 + " | after AccessSelections=" + b1);
            }
        }
        finally
        {
            if (!keep) { try { _sw.CloseDoc(doc.GetTitle()); } catch { } }
        }
        return 0;
    }

    private static IFeature Extrude(IModelDoc2 doc, double depth)
    {
        doc.SketchManager.InsertSketch(true);
        doc.ClearSelection2(true);
        doc.Extension.SelectByID2(LastOfType(doc, "ProfileFeature"), "SKETCH", 0, 0, 0, false, 0, null, 0);
        var f = doc.FeatureManager.FeatureExtrusion3(
            true, false, false, (int)swEndConditions_e.swEndCondBlind, 0, depth, 0,
            false, false, false, false, 0, 0, false, false, false, false,
            true, true, true, 0, 0, false) as IFeature;
        doc.ClearSelection2(true);
        return f;
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

    private static string LastOfType(IModelDoc2 doc, string type)
    {
        string last = "";
        var f = doc.FirstFeature() as IFeature;
        while (f != null) { if (f.GetTypeName2() == type) last = f.Name; f = f.GetNextFeature() as IFeature; }
        return last;
    }

    private static double Volume(IModelDoc2 doc)
    {
        try
        {
            var mp = doc.Extension.CreateMassProperty() as IMassProperty;
            return mp == null ? double.NaN : mp.Volume * 1e9;
        }
        catch { return double.NaN; }
    }

    private static string Fmt(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
}
