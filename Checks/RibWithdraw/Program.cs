using System;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// probe_rib_withdraw — why the rib sketch cannot be removed after two failed InsertRib
//
// Full bench on 0.141.0, limit_rib_on_a_plate_without_walls step 2: no rib is born in
// either material direction (right: nothing to meet on a plate), and RibWithdraw answers
// «I could not put the part back as I found it (removed «Schizzo2»)».
// The bridge: IGetChildCount > 0 -> give up; Select2(false,0); DeleteSelection2(swDelete_Absorbed).
// This probe rebuilds the plate 40x40x2 on the Top plane, the sketch line on the Right plane at
// y = 12 (z -15..15), calls InsertRib twice (flip false/true, IsNormToSketch false, as the bridge),
// then prints, for the sketch: ActiveSketch, IsSuppressed, IGetChildCount (and the children),
// Select2, DeleteSelection2 with swDelete_Absorbed, and if still there the variants:
// exit sketch edit first, SelectByID2(name,"SKETCH"), DeleteSelection2(swDelete_Children), 0.
//
// MEASURED on 28/9/2026 (SOLIDWORKS 29.5.1): a control sketch without InsertRib deletes;
// after the two failed InsertRib the rib sketch has 0 children, selects, and is NOT deleted by
// DeleteSelection2 (Absorbed, Children, 0), EditDelete, EditSketch+exit, or three EditUndo2.
// A new sketch made after the failures deletes normally (the lock is on that sketch only).
// EditSuppress2 works (IsSuppressed true). RibOperations.RibWithdraw suppresses it
// and says so.
internal static class Program
{
    private static int Main(string[] args)
    {
        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS does not respond: " + ex.Message); return 2; }
        Console.WriteLine("PROBE rib sketch removal — SOLIDWORKS " + sw.RevisionNumber());
        string tpl = sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        var doc = sw.NewDocument(tpl, 0, 0, 0) as IModelDoc2;
        if (doc == null) { Console.WriteLine("NewDocument -> null"); return 3; }
        try
        {
            var sm = doc.SketchManager;
            Plane(doc, 2); sm.InsertSketch(true); sm.CreateCornerRectangle(-0.020, -0.020, 0, 0.020, 0.020, 0);
            sm.InsertSketch(true); Sel(doc, Last(doc, "ProfileFeature"));
            doc.FeatureManager.FeatureExtrusion3(true, false, false, (int)swEndConditions_e.swEndCondBlind, 0, 0.002, 0,
                false, false, false, false, 0, 0, false, false, false, false, true, true, true, 0, 0, false);
            doc.ClearSelection2(true);
            Console.WriteLine("plate volume " + Vol(doc));
            var right = Plane(doc, 3);
            sm.InsertSketch(true);
            var sk = sm.ActiveSketch as ISketch;
            var mu = (IMathUtility)sw.GetMathUtility();
            Func<double[], double[]> toSk = p => ((IMathPoint)((IMathPoint)mu.CreatePoint(p)).MultiplyTransform(sk.ModelToSketchTransform)).ArrayData as double[];
            var a = toSk(new[] { 0.0, 0.012, -0.015 }); var b = toSk(new[] { 0.0, 0.012, 0.015 });
            sm.AddToDB = true; sm.CreateLine(a[0], a[1], 0, b[0], b[1], 0); sm.AddToDB = false;
            sm.InsertSketch(true);
            string skName = Last(doc, "ProfileFeature");
            Console.WriteLine("sketch «" + skName + "»");
            // CONTROL: a second sketch on the same plane, no InsertRib: can it be deleted?
            Plane(doc, 3); sm.InsertSketch(true); var sk2 = sm.ActiveSketch as ISketch;
            var c0 = ((IMathPoint)((IMathPoint)mu.CreatePoint(new[] { 0.0, 0.015, -0.010 })).MultiplyTransform(sk2.ModelToSketchTransform)).ArrayData as double[];
            var c1 = ((IMathPoint)((IMathPoint)mu.CreatePoint(new[] { 0.0, 0.015, 0.010 })).MultiplyTransform(sk2.ModelToSketchTransform)).ArrayData as double[];
            sm.AddToDB = true; sm.CreateLine(c0[0], c0[1], 0, c1[0], c1[1], 0); sm.AddToDB = false; sm.InsertSketch(true);
            string ctl = Last(doc, "ProfileFeature");
            doc.ClearSelection2(true); bool cs = Find(doc, ctl).Select2(false, 0);
            bool cd = cs && doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            Console.WriteLine("CONTROL sketch «" + ctl + "» without InsertRib: Select2 " + cs + ", DeleteSelection2(Absorbed) " + cd + ", still there " + (Find(doc, ctl) != null) + ", CommandInProgress " + sw.CommandInProgress);
            foreach (bool flip in new[] { false, true })
            {
                doc.ClearSelection2(true);
                doc.Extension.SelectByID2(skName, "SKETCH", 0, 0, 0, false, 0, null, 0);
                try { doc.FeatureManager.InsertRib(true, false, 0.002, 0, flip, false, false, 0, false, false); }
                catch (Exception ex) { Console.WriteLine("  InsertRib raises: " + ex.Message); }
                try { doc.EditRebuild3(); } catch { }
                Console.WriteLine("  after InsertRib flip=" + flip + ": Rib? " + (Last(doc, "Rib") != "") + ", ActiveSketch " + (sm.ActiveSketch != null) + ", volume " + Vol(doc));
            }
            var f = Find(doc, skName);
            Console.WriteLine("state: ActiveSketch " + (sm.ActiveSketch != null) + ", feature found " + (f != null) + ", CommandInProgress " + sw.CommandInProgress + ", selected count " + ((ISelectionMgr)doc.SelectionManager).GetSelectedObjectCount2(-1));
            if (f == null) return 0;
            int children = -1; string names = "";
            try { children = f.IGetChildCount(); var ch = f.GetChildren() as object[]; if (ch != null) foreach (var c in ch) names += " «" + ((IFeature)c).Name + "»(" + ((IFeature)c).GetTypeName2() + ")"; } catch (Exception ex) { names = " raise " + ex.Message; }
            bool sup = false; try { sup = f.IsSuppressed(); } catch { }
            Console.WriteLine("sketch: IGetChildCount " + children + names + ", IsSuppressed " + sup);
            doc.ClearSelection2(true);
            bool s1 = f.Select2(false, 0);
            bool d1 = s1 && doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            Console.WriteLine("A bridge way: Select2 " + s1 + ", DeleteSelection2(Absorbed) " + d1 + ", still there " + (Find(doc, skName) != null));
            if (Find(doc, skName) == null) return 0;
            if (sm.ActiveSketch != null) { sm.InsertSketch(true); Console.WriteLine("B exited an open sketch"); }
            doc.ClearSelection2(true);
            bool s2 = doc.Extension.SelectByID2(skName, "SKETCH", 0, 0, 0, false, 0, null, 0);
            bool d2 = s2 && doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            Console.WriteLine("B SelectByID2 " + s2 + ", DeleteSelection2(Absorbed) " + d2 + ", still there " + (Find(doc, skName) != null));
            if (Find(doc, skName) == null) return 0;
            doc.ClearSelection2(true);
            bool s3 = Find(doc, skName).Select2(false, 0);
            bool d3 = s3 && doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Children);
            Console.WriteLine("C Select2 " + s3 + ", DeleteSelection2(Children) " + d3 + ", still there " + (Find(doc, skName) != null));
            if (Find(doc, skName) == null) return 0;
            doc.ClearSelection2(true);
            bool s4 = Find(doc, skName).Select2(false, 0);
            bool d4 = s4 && doc.Extension.DeleteSelection2(0);
            Console.WriteLine("D Select2 " + s4 + ", DeleteSelection2(0) " + d4 + ", still there " + (Find(doc, skName) != null));
            if (Find(doc, skName) == null) return 0;
            // G: is the lock document-wide? A new sketch after the failures, then delete it.
            Plane(doc, 3); sm.InsertSketch(true); var sk3 = sm.ActiveSketch as ISketch;
            var g0 = ((IMathPoint)((IMathPoint)mu.CreatePoint(new[] { 0.0, 0.018, -0.005 })).MultiplyTransform(sk3.ModelToSketchTransform)).ArrayData as double[];
            var g1 = ((IMathPoint)((IMathPoint)mu.CreatePoint(new[] { 0.0, 0.018, 0.005 })).MultiplyTransform(sk3.ModelToSketchTransform)).ArrayData as double[];
            sm.AddToDB = true; sm.CreateLine(g0[0], g0[1], 0, g1[0], g1[1], 0); sm.AddToDB = false; sm.InsertSketch(true);
            string gName = Last(doc, "ProfileFeature");
            doc.ClearSelection2(true); bool gs = Find(doc, gName).Select2(false, 0);
            bool gd = gs && doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            Console.WriteLine("G new sketch «" + gName + "» AFTER the failures: Select2 " + gs + ", DeleteSelection2 " + gd + ", still there " + (Find(doc, gName) != null));
            // H: enter and leave edit of the rib sketch, then delete
            doc.ClearSelection2(true); Find(doc, skName).Select2(false, 0); doc.EditSketch(); bool inEdit = sm.ActiveSketch != null; sm.InsertSketch(true);
            doc.ClearSelection2(true); bool hs = Find(doc, skName).Select2(false, 0);
            bool hd = hs && doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            Console.WriteLine("H EditSketch (in edit " + inEdit + ") then delete: " + hd + ", still there " + (Find(doc, skName) != null));
            if (Find(doc, skName) == null) return 0;
            // I: suppress it at least
            doc.ClearSelection2(true); Find(doc, skName).Select2(false, 0);
            bool supOk = false; try { supOk = doc.EditSuppress2(); } catch { }
            bool supNow = false; try { supNow = Find(doc, skName).IsSuppressed(); } catch { }
            Console.WriteLine("I EditSuppress2 " + supOk + ", IsSuppressed " + supNow);
            // E: after a rebuild and a fresh ClearSelection, and with EditDelete (probe only: the bridge forbids it)
            try { doc.ForceRebuild3(false); } catch { }
            doc.ClearSelection2(true);
            bool s5 = Find(doc, skName).Select2(false, 0);
            try { doc.EditDelete(); } catch (Exception ex) { Console.WriteLine("E EditDelete raises " + ex.Message); }
            Console.WriteLine("E ForceRebuild3 + Select2 " + s5 + " + EditDelete, still there " + (Find(doc, skName) != null));
            if (Find(doc, skName) == null) return 0;
            // F: Undo of the sketch creation (EditUndo2 by one step at a time) — does it go?
            for (int k = 1; k <= 3 && Find(doc, skName) != null; k++) { try { doc.EditUndo2(1); } catch { } Console.WriteLine("F after EditUndo2 x" + k + ": still there " + (Find(doc, skName) != null) + ", volume " + Vol(doc)); }
        }
        finally { try { sw.CloseDoc(doc.GetTitle()); } catch { } }
        return 0;
    }

    private static IFeature Plane(IModelDoc2 doc, int which)
    {
        int seen = 0; var f = doc.FirstFeature() as IFeature;
        while (f != null) { if (f.GetTypeName2() == "RefPlane" && ++seen == which) { doc.ClearSelection2(true); f.Select2(false, 0); return f; } f = f.GetNextFeature() as IFeature; }
        return null;
    }
    private static string Last(IModelDoc2 doc, string type)
    {
        string last = ""; var f = doc.FirstFeature() as IFeature;
        while (f != null) { if (f.GetTypeName2() == type) last = f.Name; f = f.GetNextFeature() as IFeature; }
        return last;
    }
    private static IFeature Find(IModelDoc2 doc, string name)
    {
        var f = doc.FirstFeature() as IFeature;
        while (f != null) { if (f.Name == name) return f; f = f.GetNextFeature() as IFeature; }
        return null;
    }
    private static void Sel(IModelDoc2 doc, string sketch) { doc.ClearSelection2(true); doc.Extension.SelectByID2(sketch, "SKETCH", 0, 0, 0, false, 0, null, 0); }
    private static string Vol(IModelDoc2 doc)
    {
        try { var mp = doc.Extension.CreateMassProperty() as IMassProperty; return (mp.Volume * 1e9).ToString("0.###", CultureInfo.InvariantCulture); }
        catch { return "?"; }
    }
}
