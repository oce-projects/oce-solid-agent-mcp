using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  TextThroughProbe — engrave_text: the text CUT THROUGH, and the text CENTRED
// =============================================================================
//
//  WHY THIS PROBE EXISTS (bridge 0.131.0, the «through mode» of the face-ref
//  work). The live case of 12/10/2026 was "a 150x150x5 plate with the number 35
//  cut through, centred". engrave_text could do neither: it writes blind (or
//  raised), and it anchors the BOTTOM LEFT corner of the text, whose width
//  nobody knows before SOLIDWORKS draws the glyphs. Three things have to be
//  MEASURED before the operation is changed, not assumed:
//
//   1. CENTRING. The anchor is the corner, the width depends on the font. The
//      only way to centre is to draw the text, read its box, and move it. The
//      guide has the call: ISketchText::SetCoordinates ("Sets the position of
//      this sketch text", arguments X, Y, Z of the sketch text position) and
//      ISketchText::GetCoordinates ("Array of 3 double values, the x,y,z
//      coordinate of the sketch text"). What the pages do not say, and this
//      probe measures: does the box that GetEdges() reads follow the move at
//      once, inside the same open sketch? and is the text really where that box
//      says AFTER the cut (the faces the cut creates are the walls of the
//      letters: their union box is the text seen from the solid, independent of
//      GetEdges)?
//
//   2. THROUGH. IFeatureManager::FeatureCut4 with swEndCondThroughAll (the call
//      the library already makes for every through cut: SwOperations.Cut).
//      Measured here: the volume removed against the area of the same text
//      engraved blind (area = volume / depth, the law engrave_text already
//      reports), and the number of bodies.
//
//   3. THE LETTERS WITH A HOLE INSIDE (0, 6, 8, 9, A, B, D, O, P, Q, R). Cut
//      through a plate, the inside of the «0» is not attached to anything: it
//      falls out and the plate has two bodies. engrave_text already refuses a
//      marking that "splits or detaches" after a blind cut, but a through cut
//      is the case where it is certain to happen, and the refusal should be
//      worded for it. The probe measures what SOLIDWORKS does with «08».
//
//  NO API IS CALLED THAT THE LIBRARY DOES NOT ALREADY CALL, except
//  SetCoordinates/GetCoordinates (ISketchText~SetCoordinates.html,
//  ISketchText~GetCoordinates.html). No dimension is added (no modal window),
//  no part is saved, every document is closed at the end.
//
//  USAGE   Checks\TextThrough\bin\TextThrough.exe [--keep]
//          with SOLIDWORKS open; --keep leaves the parts open to look at them.
//          Built by Checks\build-checks.ps1.
//
//  OUTCOME OF 12/10/2026, SOLIDWORKS 29.5.1 (the output of the probe, as it was; the document
//  closed at the end of the run, nothing saved; the numbers are in mm and mm3):
//
//    1 — CENTRING «35» 20 mm on a 150x150x5 plate
//       anchor (0,0): box x 0.769 .. 29.487  y -0.513 .. 20.513   (28.718 x 21.026 mm)
//       GetCoordinates before: (0, 0, 0) mm
//       SetCoordinates -> True   GetCoordinates after: (-15.128, -10, 0) mm
//       box after the move, read from GetEdges in the SAME open sketch: x -14.359 .. 14.359  y -10.513 .. 10.513   centre (0, 0)
//       box of the walls the cut made (the text seen from the solid): x -14.359 .. 14.359  y -10.513 .. 10.513   centre (0, 0)
//       volume 112500 -> 111617.62 mm3   bodies 1
//    2a — AREA of «35» 20 mm, from a blind cut of 0.5 mm
//       removed 88.422 mm3   area = 176.844 mm2   bodies 1
//    2b — THROUGH «35» 20 mm, anchor at (0,0)
//       removed 883.987 mm3   area = removed/5 = 176.797 mm2   bodies 1   walls box centre (15.128, 10)
//    2b — THROUGH «35» 20 mm, centred
//       removed 882.377 mm3   area = removed/5 = 176.475 mm2   bodies 1   walls box centre (0, 0)
//    2b — THROUGH «08» 20 mm, centred
//       removed 1031.506 mm3   area = removed/5 = 206.301 mm2   bodies 4   walls box centre (0, 0)
//
//  READ: (1) SetCoordinates works and GetEdges follows at once, so the centring is «draw, measure, move,
//  measure again», and the solid agrees with the outlines to the thousandth. (2) The through cut removes the
//  blind area times the thickness to 0.2 per cent (the noise of the mass properties of a body of tiny faces).
//  (3) «08» through leaves 4 bodies — the plate and the three insides of the letters — and opens no window:
//  the refusal for letters with a hole is the operation's to make, and it must take the cut away.
// =============================================================================
internal static class TextThroughProbe
{
    private static ISldWorks _sw;
    private static bool _keep;
    private static readonly List<IModelDoc2> _opened = new List<IModelDoc2>();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static int Main(string[] args)
    {
        _keep = args.Contains("--keep");
        _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
        Console.WriteLine("PROBE TEXT-THROUGH — engrave_text through and centred");
        Console.WriteLine("attached to SOLIDWORKS " + _sw.RevisionNumber());
        try
        {
            Centring("35", 20);
            BlindArea("35", 20, 0.5);
            Through("35", 20, centre: false);
            Through("35", 20, centre: true);
            Through("08", 20, centre: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine("PROBE STOPPED: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
        finally
        {
            if (!_keep)
                foreach (var d in _opened)
                    try { _sw.CloseDoc(d.GetTitle()); } catch { }
        }
        return 0;
    }

    // ------------------------------------------------------------------
    //  1. CENTRING: the box before the move, after the move, and from the solid
    // ------------------------------------------------------------------
    private static void Centring(string text, double heightMm)
    {
        Console.WriteLine();
        Console.WriteLine("1 — CENTRING «" + text + "» " + heightMm + " mm on a 150x150x5 plate");
        var doc = Plate(out IFace2 top);
        var sm = doc.SketchManager;
        double before = Volume(doc);
        Select(top);
        sm.InsertSketch(true);
        sm.AddToDB = true;
        sm.DisplayWhenAdded = false;
        double[] p0 = ToSketch(doc, 0, 0, 0.005);
        var written = Write(doc, text, heightMm, p0);
        var edges0 = Box(written);
        Console.WriteLine("   anchor (0,0): box x " + Fmt(edges0, 0) + " .. " + Fmt(edges0, 3) +
                          "  y " + Fmt(edges0, 1) + " .. " + Fmt(edges0, 4) +
                          "   (" + Fmt(edges0, 3, 0) + " x " + Fmt(edges0, 4, 1) + " mm)");
        var c0 = written.GetCoordinates() as double[];
        Console.WriteLine("   GetCoordinates before: " + Arr(c0));

        double dx = -(edges0[0] + edges0[3]) / 2.0;   // model mm, plane X
        double dy = -(edges0[1] + edges0[4]) / 2.0;   // model mm, plane Y
        double[] p1 = ToSketch(doc, dx, dy, 0.005);
        bool moved = written.SetCoordinates(p1[0], p1[1], p1[2]);
        var c1 = written.GetCoordinates() as double[];
        Console.WriteLine("   SetCoordinates -> " + moved + "   GetCoordinates after: " + Arr(c1));
        var edges1 = Box(written);
        Console.WriteLine("   box after the move, read from GetEdges in the SAME open sketch: x " +
                          Fmt(edges1, 0) + " .. " + Fmt(edges1, 3) + "  y " + Fmt(edges1, 1) + " .. " + Fmt(edges1, 4) +
                          "   centre (" + ((edges1[0] + edges1[3]) / 2).ToString("0.###", Inv) + ", " +
                          ((edges1[1] + edges1[4]) / 2).ToString("0.###", Inv) + ")");

        sm.AddToDB = false;
        sm.DisplayWhenAdded = true;
        sm.InsertSketch(false);
        SelectLastSketch(doc);
        var cut = CutThrough(doc);
        if (cut == null) { Console.WriteLine("   the through cut was NOT created"); return; }
        var walls = FacesBox(cut);
        Console.WriteLine("   box of the walls the cut made (the text seen from the solid): x " +
                          Fmt(walls, 0) + " .. " + Fmt(walls, 3) + "  y " + Fmt(walls, 1) + " .. " + Fmt(walls, 4) +
                          "   centre (" + ((walls[0] + walls[3]) / 2).ToString("0.###", Inv) + ", " +
                          ((walls[1] + walls[4]) / 2).ToString("0.###", Inv) + ")");
        Console.WriteLine("   volume " + before.ToString("0.##", Inv) + " -> " + Volume(doc).ToString("0.##", Inv) +
                          " mm3   bodies " + Bodies(doc));
    }

    // ------------------------------------------------------------------
    //  2a. THE AREA OF THE LETTERS, from a blind engraving (the law of
    //      engrave_text: area = volume removed / depth)
    // ------------------------------------------------------------------
    private static void BlindArea(string text, double heightMm, double depthMm)
    {
        Console.WriteLine();
        Console.WriteLine("2a — AREA of «" + text + "» " + heightMm + " mm, from a blind cut of " + depthMm + " mm");
        var doc = Plate(out IFace2 top);
        var sm = doc.SketchManager;
        double before = Volume(doc);
        Select(top);
        sm.InsertSketch(true);
        sm.AddToDB = true;
        sm.DisplayWhenAdded = false;
        Write(doc, text, heightMm, ToSketch(doc, 0, 0, 0.005));
        sm.AddToDB = false;
        sm.DisplayWhenAdded = true;
        sm.InsertSketch(false);
        SelectLastSketch(doc);
        var cut = doc.FeatureManager.FeatureCut4(
            true, false, false, (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
            depthMm / 1000.0, 0, false, false, false, false, 0, 0, false, false, false, false,
            false, true, true, false, false, false, (int)swStartConditions_e.swStartSketchPlane, 0, false, true) as IFeature;
        if (cut == null) { Console.WriteLine("   the blind cut was NOT created"); return; }
        double removed = before - Volume(doc);
        Console.WriteLine("   removed " + removed.ToString("0.###", Inv) + " mm3   area = " +
                          (removed / depthMm).ToString("0.###", Inv) + " mm2   bodies " + Bodies(doc));
    }

    // ------------------------------------------------------------------
    //  2b / 3. THROUGH: removed volume against the area, and the bodies
    // ------------------------------------------------------------------
    private static void Through(string text, double heightMm, bool centre)
    {
        Console.WriteLine();
        Console.WriteLine("2b — THROUGH «" + text + "» " + heightMm + " mm, " + (centre ? "centred" : "anchor at (0,0)"));
        var doc = Plate(out IFace2 top);
        var sm = doc.SketchManager;
        double before = Volume(doc);
        Select(top);
        sm.InsertSketch(true);
        sm.AddToDB = true;
        sm.DisplayWhenAdded = false;
        var written = Write(doc, text, heightMm, ToSketch(doc, 0, 0, 0.005));
        if (centre)
        {
            var b = Box(written);
            double[] p = ToSketch(doc, -(b[0] + b[3]) / 2.0, -(b[1] + b[4]) / 2.0, 0.005);
            written.SetCoordinates(p[0], p[1], p[2]);
        }
        sm.AddToDB = false;
        sm.DisplayWhenAdded = true;
        sm.InsertSketch(false);
        SelectLastSketch(doc);
        var cut = CutThrough(doc);
        if (cut == null) { Console.WriteLine("   the through cut was NOT created"); return; }
        double removed = before - Volume(doc);
        var walls = FacesBox(cut);
        Console.WriteLine("   removed " + removed.ToString("0.###", Inv) + " mm3   area = removed/5 = " +
                          (removed / 5.0).ToString("0.###", Inv) + " mm2   bodies " + Bodies(doc) +
                          "   walls box centre (" + ((walls[0] + walls[3]) / 2).ToString("0.###", Inv) + ", " +
                          ((walls[1] + walls[4]) / 2).ToString("0.###", Inv) + ")");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// IFeatureManager~FeatureCut4.html, the same call as SwOperations.Cut with
    /// swEndCondThroughAll as the first end condition.
    /// </summary>
    private static IFeature CutThrough(IModelDoc2 doc)
    {
        return doc.FeatureManager.FeatureCut4(
            true, false, false, (int)swEndConditions_e.swEndCondThroughAll, (int)swEndConditions_e.swEndCondBlind,
            0, 0, false, false, false, false, 0, 0, false, false, false, false,
            false, true, true, false, false, false, (int)swStartConditions_e.swStartSketchPlane, 0, false, true) as IFeature;
    }

    /// 150x150x5 plate, front plane, extruded along +Z: top face at z = 5 mm.
    private static IModelDoc2 Plate(out IFace2 top)
    {
        string tpl = _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        var doc = (IModelDoc2)_sw.NewDocument(tpl, 0, 0, 0);
        _opened.Add(doc);
        doc.ClearSelection2(true);
        SelectBasePlane(doc, 1);
        var sm = doc.SketchManager;
        sm.InsertSketch(true);
        sm.CreateCornerRectangle(-0.075, -0.075, 0, 0.075, 0.075, 0);
        sm.InsertSketch(false);
        SelectLastSketch(doc);
        doc.FeatureManager.FeatureExtrusion3(
            true, false, false, (int)swEndConditions_e.swEndCondBlind, 0, 0.005, 0,
            false, false, false, false, 0, 0, false, false, false, false,
            true, true, true, 0, 0, false);
        var bodies = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (bodies == null || bodies.Length == 0) throw new Exception("the plate has no body");
        top = null;
        foreach (var o in ((IBody2)bodies[0]).GetFaces() as object[])
        {
            var f = (IFace2)o;
            var n = f.Normal as double[];
            if (n != null && n[2] > 0.99) top = f;
        }
        if (top == null) throw new Exception("the plate has no face with normal +Z");
        return doc;
    }

    private static void Select(IFace2 face)
    {
        ((IEntity)face).Select4(false, null);
    }

    /// Model point (mm on the plane axes X,Y; zM metres) converted to sketch coordinates.
    private static double[] ToSketch(IModelDoc2 doc, double xMm, double yMm, double zM)
    {
        var sketch = doc.SketchManager.ActiveSketch;
        var math = (IMathUtility)_sw.GetMathUtility();
        var pt = (IMathPoint)math.CreatePoint(new[] { xMm / 1000.0, yMm / 1000.0, zM });
        var res = (IMathPoint)pt.MultiplyTransform(sketch.ModelToSketchTransform);
        return (double[])res.ArrayData;
    }

    private static ISketchText Write(IModelDoc2 doc, string text, double heightMm, double[] p)
    {
        var t = doc.InsertSketchText(p[0], p[1], p[2], text, 0, 0, 0, 100, 100) as ISketchText;
        if (t == null) throw new Exception("InsertSketchText returned null");
        var fmt = (ITextFormat)t.GetTextFormat();
        fmt.CharHeight = heightMm / 1000.0;
        if (!t.SetTextFormat(false, fmt)) throw new Exception("SetTextFormat refused");
        return t;
    }

    /// Box of the outlines of the text, in mm, MODEL coordinates: minX,minY,minZ,maxX,maxY,maxZ.
    private static double[] Box(ISketchText t)
    {
        var edges = t.GetEdges() as object[];
        if (edges == null || edges.Length == 0) throw new Exception("the text has no outlines");
        var b = new[] { double.MaxValue, double.MaxValue, double.MaxValue, double.MinValue, double.MinValue, double.MinValue };
        foreach (var o in edges)
        {
            var e = o as IEdge;
            if (e == null) continue;
            var c = e.GetCurve() as ICurve;
            if (c == null) continue;
            // IEdge~GetCurveParams3.html, Remarks: GetCurve first; the data carries
            // UMinValue/UMaxValue, the parameter range of THIS EDGE on its curve (the
            // obsolete GetCurveParams2 is not used).
            var data = e.GetCurveParams3() as ICurveParamData;
            if (data == null) continue;
            double t0 = data.UMinValue, t1 = data.UMaxValue;
            for (int i = 0; i <= 6; i++)
            {
                var q = c.Evaluate(t0 + (t1 - t0) * i / 6.0) as double[];
                if (q != null && q.Length >= 3) Widen(b, q[0], q[1], q[2]);
            }
        }
        for (int i = 0; i < 6; i++) b[i] *= 1000.0;
        return b;
    }

    private static void Widen(double[] b, double x, double y, double z)
    {
        if (x < b[0]) b[0] = x;
        if (y < b[1]) b[1] = y;
        if (z < b[2]) b[2] = z;
        if (x > b[3]) b[3] = x;
        if (y > b[4]) b[4] = y;
        if (z > b[5]) b[5] = z;
    }

    /// Union of the true boxes of the faces a feature made, mm. IFace2~CreateSheetBody.html
    /// then IBody2~GetExtremePoint, as ApiHelpers.TrueBoundingBox does.
    private static double[] FacesBox(IFeature f)
    {
        var faces = f.GetFaces() as object[];
        var b = new[] { double.MaxValue, double.MaxValue, double.MaxValue, double.MinValue, double.MinValue, double.MinValue };
        var dirs = new[] { new[] { -1.0, 0, 0 }, new[] { 0, -1.0, 0 }, new[] { 0, 0, -1.0 },
                           new[] { 1.0, 0, 0 }, new[] { 0, 1.0, 0 }, new[] { 0, 0, 1.0 } };
        foreach (var o in faces ?? new object[0])
        {
            var sheet = ((IFace2)o).CreateSheetBody() as IBody2;
            if (sheet == null) continue;
            for (int i = 0; i < 6; i++)
            {
                if (!sheet.GetExtremePoint(dirs[i][0], dirs[i][1], dirs[i][2], out double x, out double y, out double z)) continue;
                double[] pt = { x * 1000, y * 1000, z * 1000 };
                double v = pt[i % 3];
                if (i < 3) b[i] = Math.Min(b[i], v); else b[i] = Math.Max(b[i], v);
            }
        }
        return b;
    }

    private static double Volume(IModelDoc2 doc)
    {
        double v = 0;
        var bodies = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (bodies == null) return 0;
        foreach (var c in bodies)
        {
            var mp = (c as IBody2).GetMassProperties(1000.0) as double[];
            if (mp != null && mp.Length > 4) v += mp[3] * 1e9;
        }
        return v;
    }

    private static int Bodies(IModelDoc2 doc)
    {
        var bodies = ((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        return bodies == null ? 0 : bodies.Length;
    }

    private static string Fmt(double[] b, int i) { return b[i].ToString("0.###", Inv); }
    private static string Fmt(double[] b, int hi, int lo) { return (b[hi] - b[lo]).ToString("0.###", Inv); }
    private static string Arr(double[] a) { return a == null ? "null" : "(" + string.Join(", ", a.Select(v => (v * 1000).ToString("0.###", Inv))) + ") mm"; }

    // The sketch just closed, by TYPE and not by name (the names follow the language).
    private static void SelectLastSketch(IModelDoc2 doc)
    {
        IFeature last = null;
        var f = doc.FirstFeature() as IFeature;
        while (f != null)
        {
            if (f.GetTypeName2() == "ProfileFeature") last = f;
            f = f.GetNextFeature() as IFeature;
        }
        doc.ClearSelection2(true);
        if (last == null || !last.Select2(false, 0))
            throw new Exception("cannot select the sketch just closed (no ProfileFeature in the tree).");
    }

    // Base planes by POSITION (the first three RefPlane features), as the other probes.
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
