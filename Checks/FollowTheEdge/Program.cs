using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  SeguiIlBordo — does a hole dimensioned against an EDGE follow it? (entry E18, a and b)
// =============================================================================
//
//  Three of the four silences of `--regressione` are coordinates inside a
//  sketch that do not follow the edge when the part changes (K5). The cure that the
//  entry proposes rests on TWO facts that nobody has measured out of process:
//
//    (a) a dimension between the centre of a sketch circle and an edge of the
//        MODEL is created from the API, stays driving, and drags the hole when the
//        edge moves;
//    (b) a point put halfway along a construction line, whose ends are
//        at the middle of two opposite edges, stays at the CENTRE of the face when the
//        plate grows on one side only.
//
//  THE PART: a plate 200 x 120 x 20 from the front plane, with the two dimensions
//  of the rectangle set here (so they can be changed), and in ONE sketch on the
//  front face three through holes of 10:
//    A — at 20 from the right edge and at 20 from the top one   (dimensions to the edge)
//    B — in the centre, on the midline of a line between the two sides (relations)
//    C — at absolute coordinates (-60, -30), no relations (the TWIN: the
//        old way must stay where it is)
//
//  Then the length 200 -> 260 and the height 120 -> 180, and after each one we
//  measure again: A at (xmax-20, ymax-20), B at the centre of the bounding box, C still. The
//  part grows on the side that SOLIDWORKS decides (the rectangle is not
//  anchored): the criterion is written with respect to the bounding box, so it holds from
//  whichever side it grows.
//
//  And after every rebuild: no feature in error. An over-constrained
//  sketch gives no error in `Insert*`: it gives a red feature afterwards.
//
//  USAGE
//      .\Checks\FollowTheEdge\bin\FollowTheEdge.exe            [--tieni]
// =============================================================================

internal static class Program
{
    private static int _step;
    private static void Says(string what)
    {
        _step++;
        Console.WriteLine("  " + _step.ToString("00") + ". " + what);
    }

    private static ISldWorks _sw;
    private static string _variant = "bordi";
    private static ISketch _cutSketch;
    private static bool _listRelations = true;
    private static IModelDoc2 _doc;
    private static IMathUtility _mu;

    private static int Main(string[] arguments)
    {
        bool keep = Array.IndexOf(arguments, "--tieni") >= 0;
        if (Array.IndexOf(arguments, "--b=convertiti") >= 0) _variant = "convertiti";
        Console.WriteLine();
        Console.WriteLine("SEGUI IL BORDO — un foro quotato contro lo spigolo lo segue? (voce E18, variante B: " + _variant + ")");
        Console.WriteLine();

        try { _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS non risponde: " + ex.Message); return 2; }
        Says("agganciato a SOLIDWORKS " + _sw.RevisionNumber());
        _mu = (IMathUtility)_sw.GetMathUtility();

        bool asked = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
        _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
        try
        {
            string model = _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            _doc = (IModelDoc2)_sw.NewDocument(model, 0, 0, 0);
            if (_doc == null) { Console.WriteLine("  NewDocument -> null"); return 4; }

            // ── THE PLATE, with its two dimensions ──────────────────────
            _doc.ClearSelection2(true);
            if (!SelectBasePlane(_doc, 1))
            { Console.WriteLine("  non trovo «Piano frontale»"); return 4; }
            _doc.SketchManager.InsertSketch(true);
            var sides = _doc.SketchManager.CreateCornerRectangle(-0.100, -0.060, 0, 0.100, 0.060, 0) as object[];
            ISketchSegment up = null, right = null;
            foreach (var o in sides ?? new object[0])
            {
                var l = o as ISketchLine;
                if (l == null) continue;
                var a = (ISketchPoint)l.GetStartPoint2(); var b = (ISketchPoint)l.GetEndPoint2();
                if (Math.Abs(a.Y - 0.060) < 1e-9 && Math.Abs(b.Y - 0.060) < 1e-9) up = (ISketchSegment)l;
                if (Math.Abs(a.X - 0.100) < 1e-9 && Math.Abs(b.X - 0.100) < 1e-9) right = (ISketchSegment)l;
            }
            if (up == null || right == null) { Console.WriteLine("  non ritrovo i lati del rettangolo"); return 4; }
            _doc.ClearSelection2(true); up.Select4(false, null);
            var qLen = (_doc.AddDimension2(0, 0.090, 0) as IDisplayDimension)?.GetDimension2(0);
            _doc.ClearSelection2(true); right.Select4(false, null);
            var qAlt = (_doc.AddDimension2(0.130, 0, 0) as IDisplayDimension)?.GetDimension2(0);
            _doc.ClearSelection2(true);
            _doc.SketchManager.InsertSketch(true);
            if (qLen == null || qAlt == null) { Console.WriteLine("  le quote del rettangolo non sono nate"); return 4; }
            var plate = _doc.FeatureManager.FeatureExtrusion3(
                true, false, false, 0, 0, 0.020, 0.0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, 0, 0, false) as IFeature;
            if (plate == null) { Console.WriteLine("  la piastra non e' venuta"); return 4; }
            var box = BoundingBox();
            Says("piastra: ingombro " + Box(box) + ", quote " + Mm(qLen.SystemValue) + " e " + Mm(qAlt.SystemValue));
            double zFront = Math.Abs(box[2]) > Math.Abs(box[5]) ? box[2] : box[5];

            // ── THE SKETCH ON THE FRONT FACE ─────────────────────────
            _doc.ClearSelection2(true);
            if (!_doc.Extension.SelectByID2("", "FACE", 0.03, 0.01, zFront, false, 0, null, 0))
            { Console.WriteLine("  non seleziono la faccia davanti a Z " + Mm(zFront)); return 4; }
            _doc.SketchManager.InsertSketch(true);
            var sketch = _doc.SketchManager.ActiveSketch;
            var tr = sketch.ModelToSketchTransform;

            // MEASURED the first run (evening of 24/9): the plate grows to the LEFT and
            // DOWNWARDS (X -160..100, Y -120..60), and the top right corner
            // stays still. A hole dimensioned only at the corner that does not move
            // passes without proving anything. So two dimensioned holes, one per
            // corner, and each is judged against ITS OWN edges: from whichever
            // side it grows, one of the two must really move.
            //
            // THE HOLES ARE RECOGNISED BY THE DIAMETER: A 6, D 8, B 10, C 12. The first
            // run recognised them by the closeness to the expected point, and a hole
            // that stayed still could be mistaken for another.

            // A: bottom left, at 20 from the left edge and from the bottom one
            var cA = Circle(tr, -0.080, -0.040, zFront, 0.003);
            var pA = (ISketchPoint)((ISketchArc)cA).GetCenterPoint2();
            var qA1 = DimensionToEdge(pA, -0.100, 0.0, zFront, -0.115, -0.045, zFront);
            var qA2 = DimensionToEdge(pA, 0.0, -0.060, zFront, -0.085, -0.075, zFront);
            Says("A (Ø6): quote allo spigolo " + (qA1 == null ? "NON NATA" : Mm(qA1.SystemValue)) + " (sinistro), " +
                 (qA2 == null ? "NON NATA" : Mm(qA2.SystemValue)) + " (basso)");

            // D: top right, at 20 from the right edge and from the top one
            var cD = Circle(tr, 0.080, 0.040, zFront, 0.004);
            var pD = (ISketchPoint)((ISketchArc)cD).GetCenterPoint2();
            var qD1 = DimensionToEdge(pD, 0.100, 0.0, zFront, 0.115, 0.045, zFront);
            var qD2 = DimensionToEdge(pD, 0.0, 0.060, zFront, 0.085, 0.075, zFront);
            Says("D (Ø8): quote allo spigolo " + (qD1 == null ? "NON NATA" : Mm(qD1.SystemValue)) + " (destro), " +
                 (qD2 == null ? "NON NATA" : Mm(qD2.SystemValue)) + " (alto)");

            // B: in the centre. Two variants, one per run (--b=bordi | --b=convertiti).
            var cB = Circle(tr, 0.0, 0.0, zFront, 0.005);
            var pB = (ISketchPoint)((ISketchArc)cB).GetCenterPoint2();
            ISketchSegment line = null;
            if (_variant == "convertiti")
            {
                // The left and right edges CONVERTED into the sketch (they follow
                // the model by themselves) and set to CONSTRUCTION, otherwise they would enter
                // the profile of the cut. The midline rests on them: they are
                // sketch entities, not model edges.
                var sx = Convert(-0.100, 0.0, zFront);
                var dx = Convert(0.100, 0.0, zFront);
                line = Centerline(tr, zFront);

                var ls = line as ISketchLine;
                bool r1 = sx != null && RelationBetweenSketches((ISketchPoint)ls.GetStartPoint2(), sx, "sgATMIDDLE");
                bool r2 = dx != null && RelationBetweenSketches((ISketchPoint)ls.GetEndPoint2(), dx, "sgATMIDDLE");
                Says("B (Ø10), variante convertiti: fianchi convertiti " + (sx != null) + "/" + (dx != null) +
                     ", estremi della mezzeria a meta' " + r1 + "/" + r2);
            }
            else
            {
                line = Centerline(tr, zFront);

                var ls = line as ISketchLine;
                bool rB1 = Relation((ISketchPoint)ls.GetStartPoint2(), -0.100, 0.0, zFront, "sgATMIDDLE");
                bool rB2 = Relation((ISketchPoint)ls.GetEndPoint2(), 0.100, 0.0, zFront, "sgATMIDDLE");
                Says("B (Ø10), variante bordi: estremi della mezzeria a meta' degli spigoli del modello " + rB1 + "/" + rB2);
            }
            RelationBetweenSketches(pB, line, "sgATMIDDLE");

            // C: the twin, at absolute coordinates
            Circle(tr, -0.040, 0.030, zFront, 0.006);
            Says("C (Ø12): a (-40, 30), senza relazioni");

            _doc.SketchManager.InsertSketch(true);
            var cut = _doc.FeatureManager.FeatureCut4(
                true, false, false, (int)swEndConditions_e.swEndCondThroughAll, 0, 0, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                false, true, true, true, true, false, 0, 0, false, false) as IFeature;
            if (cut == null) { Console.WriteLine("  il taglio non e' venuto"); return 4; }
            _doc.EditRebuild3();

            _cutSketch = (cut.GetFirstSubFeature() as IFeature)?.GetSpecificFeature2() as ISketch;
            int mistakes = 0;
            var beforeC = Hole(Holes(), 0.006);
            mistakes += Verify("dopo il taglio", beforeC);

            // ── THE PLATE CHANGES ────────────────────────────────────────
            qLen.SetSystemValue3(0.260, (int)swSetValueInConfiguration_e.swSetValue_InAllConfigurations, null);
            _doc.EditRebuild3();
            int s1 = Verify("lunghezza 200 -> 260", beforeC);
            if (s1 > 0)
            {
                // Does a FULL rebuild solve the references that the normal
                // one leaves broken? If so, it is an ordering problem.
                _doc.ForceRebuild3(false);
                s1 = Verify("lunghezza 200 -> 260, dopo ForceRebuild3", beforeC);
            }
            mistakes += s1;

            qAlt.SetSystemValue3(0.180, (int)swSetValueInConfiguration_e.swSetValue_InAllConfigurations, null);
            _doc.EditRebuild3();
            int s2 = Verify("altezza 120 -> 180", beforeC);
            if (s2 > 0)
            {
                _doc.ForceRebuild3(false);
                s2 = Verify("altezza 120 -> 180, dopo ForceRebuild3", beforeC);
            }
            mistakes += s2;

            Console.WriteLine();
            if (mistakes == 0)
            {
                Console.WriteLine("REGGE: A segue l'angolo, B resta al centro, C resta dov'era, e nessuna lavorazione");
                Console.WriteLine("e' in errore. I due meccanismi di E18 (a) e (b) si costruiscono da API.");
                return 0;
            }
            Console.WriteLine(mistakes + " misure fuori posto: guarda sopra quale meccanismo non regge.");
            return 6;
        }
        catch (Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            Console.WriteLine();
            Console.WriteLine("ECCEZIONE: " + ex.GetType().Name + ": " + ex.Message);
            return 9;
        }
        finally
        {
            try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, asked); } catch { }
            if (_doc != null && !keep)
            {
                try { _sw.CloseDoc(_doc.GetTitle()); Says("pezzo chiuso senza salvare"); } catch { }
            }
        }
    }

    // ── measurements ─────────────────────────────────────────────────────

    private static int Verify(string timestamp, double[] cBefore)
    {
        var box = BoundingBox();
        var holes = Holes();
        int errors = 0;
        try { errors = _doc.Extension.GetWhatsWrongCount(); } catch { }
        Console.WriteLine();
        Console.WriteLine("      " + timestamp + ": ingombro " + Box(box) + ", fori " + holes.Count + ", cose che non vanno " + errors);
        if (errors > 0)
        {
            // WHICH feature, and with what code: the first run only said «1».
            try
            {
                object feats, codes, warnings;
                _doc.Extension.GetWhatsWrong(out feats, out codes, out warnings);
                var f = feats as object[]; var c = codes as int[]; var w = warnings as bool[];
                for (int i = 0; f != null && i < f.Length; i++)
                    Console.WriteLine("        «" + ((IFeature)f[i]).Name + "» codice " + (c != null ? c[i].ToString() : "?") +
                                      (w != null && w[i] ? " (avviso)" : " (ERRORE)"));
            }
            catch (Exception ex) { Console.WriteLine("        GetWhatsWrong: " + ex.Message); }
        }
        int mistakes = errors > 0 ? 1 : 0;
        try
        {
            var rm = _cutSketch?.RelationManager;
            if (rm != null)
            {
                Console.WriteLine("        relazioni dello schizzo del taglio: tutte " + rm.GetRelationsCount(0) +
                                  ", esterne " + rm.GetRelationsCount(3) + ", penzolanti " + rm.GetRelationsCount(1) +
                                  ", rotte " + rm.GetRelationsCount(6) + ", sovravincolanti " + rm.GetRelationsCount(2));
                if (_listRelations)
                {
                    _listRelations = false;
                    foreach (var o in (rm.GetRelations(0) as object[]) ?? new object[0])
                    {
                        var r = (ISketchRelation)o;
                        var types = r.GetEntitiesType() as int[];
                        Console.WriteLine("          tipo " + r.GetRelationType() + " su " + r.GetEntitiesCount() +
                                          " entita' (" + (types == null ? "-" : string.Join(",", types)) + ")" +
                                          (r.GetDisplayDimension() != null ? " [quota]" : ""));
                    }
                }
            }
        }
        catch (Exception ex) { Console.WriteLine("        relazioni: " + ex.Message); }

        double xmin = Math.Min(box[0], box[3]), ymin = Math.Min(box[1], box[4]);
        double xmax = Math.Max(box[0], box[3]), ymax = Math.Max(box[1], box[4]);
        double xc = (box[0] + box[3]) / 2, yc = (box[1] + box[4]) / 2;
        mistakes += Line("A (Ø6)  a 20 dal basso-sinistra", Hole(holes, 0.003), xmin + 0.020, ymin + 0.020);
        mistakes += Line("D (Ø8)  a 20 dall'alto-destra  ", Hole(holes, 0.004), xmax - 0.020, ymax - 0.020);
        mistakes += Line("B (Ø10) al centro              ", Hole(holes, 0.005), xc, yc);
        if (cBefore != null) mistakes += Line("C (Ø12) dov'era                ", Hole(holes, 0.006), cBefore[0], cBefore[1]);
        return mistakes;
    }

    private static double[] Hole(List<double[]> holes, double radius)
    {
        return holes.Find(f => Math.Abs(f[2] - radius) < 1e-6);
    }

    private static int Line(string itemName, double[] f, double x, double y)
    {
        if (f == null) { Console.WriteLine("      NO  " + itemName + ": il foro non c'e'"); return 1; }
        double d = Math.Sqrt(Math.Pow(f[0] - x, 2) + Math.Pow(f[1] - y, 2));
        bool ok = d < 0.00005;
        Console.WriteLine("      " + (ok ? "ok  " : "NO  ") + itemName + " (" + Mm(f[0]) + ", " + Mm(f[1]) + ")  atteso (" +
                          Mm(x) + ", " + Mm(y) + ")" + (ok ? "" : "  scarto " + Mm(d)));
        return ok ? 0 : 1;
    }

    private static double[] Near(List<double[]> holes, double x, double y)
    {
        double[] better = null; double dm = double.MaxValue;
        foreach (var f in holes)
        {
            double d = Math.Pow(f[0] - x, 2) + Math.Pow(f[1] - y, 2);
            if (d < dm) { dm = d; better = f; }
        }
        return better;
    }

    /// <summary>The centres of the holes: the cylindrical faces, from the point on the axis.</summary>
    private static List<double[]> Holes()
    {
        var outside = new List<double[]>();
        var bodies = ((IPartDoc)_doc).GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
        if (bodies == null) return outside;
        foreach (var c in bodies)
        {
            var f = ((IBody2)c).GetFirstFace() as IFace2;
            while (f != null)
            {
                var s = f.GetSurface() as ISurface;
                if (s != null && s.IsCylinder())
                {
                    var p = s.CylinderParams as double[];
                    if (p != null) outside.Add(new[] { p[0], p[1], p[6] });
                }
                f = f.GetNextFace() as IFace2;
            }
        }
        return outside;
    }

    private static double[] BoundingBox()
    {
        var bodies = ((IPartDoc)_doc).GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
        return (bodies[0] as IBody2).GetBodyBox() as double[];
    }

    // ── sketch ───────────────────────────────────────────────────────────

    private static double[] Sk(MathTransform tr, double x, double y, double z)
    {
        var p = (IMathPoint)_mu.CreatePoint(new[] { x, y, z });
        return (double[])((IMathPoint)p.MultiplyTransform(tr)).ArrayData;
    }

    // AddToDB true while drawing: without it, SOLIDWORKS adds by itself the
    // relations that it «guesses» (coincidences with the edges, horizontals), and
    // added to those put in on purpose they over-constrain the sketch as soon as the part
    // changes. Measured on the evening of 24/9: 5 over-constraining ones after the first change.
    private static bool _withoutInferences = System.Environment.GetCommandLineArgs().Length > 0 &&
        Array.IndexOf(System.Environment.GetCommandLineArgs(), "--con-inferenze") < 0;

    private static ISketchSegment Circle(MathTransform tr, double x, double y, double z, double radius)
    {
        var c = Sk(tr, x, y, z);
        _doc.SketchManager.AddToDB = _withoutInferences;
        var s = _doc.SketchManager.CreateCircleByRadius(c[0], c[1], 0, radius);
        _doc.SketchManager.AddToDB = false;
        return s;
    }

    private static ISketchSegment Centerline(MathTransform tr, double z)
    {
        _doc.SketchManager.AddToDB = _withoutInferences;
        var s = _doc.SketchManager.CreateCenterLine(
            Sk(tr, -0.100, 0, z)[0], Sk(tr, -0.100, 0, z)[1], 0,
            Sk(tr, 0.100, 0, z)[0], Sk(tr, 0.100, 0, z)[1], 0);
        _doc.SketchManager.AddToDB = false;
        return s;
    }

    /// <summary>
    /// Converts into the sketch the edge of the model that passes through (x, y, z) and
    /// sets it to construction. The new segment is the one that was not there before.
    /// </summary>
    private static ISketchSegment Convert(double x, double y, double z)
    {
        var sketch = _doc.SketchManager.ActiveSketch;
        var before = new List<ISketchSegment>();
        foreach (var o in (sketch.GetSketchSegments() as object[]) ?? new object[0]) before.Add((ISketchSegment)o);
        _doc.ClearSelection2(true);
        if (!_doc.Extension.SelectByID2("", "EDGE", x, y, z, false, 0, null, 0)) return null;
        _doc.SketchManager.SketchUseEdge3(false, false);
        _doc.ClearSelection2(true);
        foreach (var o in (sketch.GetSketchSegments() as object[]) ?? new object[0])
        {
            var s = (ISketchSegment)o;
            bool newItem = true;
            foreach (var p in before) if (_sw.IsSame(p, s) == (int)swObjectEquality.swObjectSame) { newItem = false; break; }
            if (newItem) { s.ConstructionGeometry = true; return s; }
        }
        return null;
    }

    private static bool RelationBetweenSketches(ISketchPoint p, ISketchSegment s, string which)
    {
        _doc.ClearSelection2(true);
        if (!p.Select4(false, null) || !s.Select4(true, null)) return false;
        _doc.SketchAddConstraints(which);
        _doc.ClearSelection2(true);
        return true;
    }

    /// <summary>
    /// A dimension between a sketch point and the edge of the model that passes
    /// through (ex, ey, ez), with the text at (tx, ty, tz). The value is read back:
    /// a dimension born «by chance» between two wrong things would have another number.
    /// </summary>
    private static IDimension DimensionToEdge(ISketchPoint p, double ex, double ey, double ez,
                                               double tx, double ty, double tz)
    {
        _doc.ClearSelection2(true);
        if (!p.Select4(false, null)) return null;
        if (!_doc.Extension.SelectByID2("", "EDGE", ex, ey, ez, true, 0, null, 0)) return null;
        var d = (_doc.AddDimension2(tx, ty, tz) as IDisplayDimension)?.GetDimension2(0);
        _doc.ClearSelection2(true);
        return d;
    }

    private static bool Relation(ISketchPoint p, double ex, double ey, double ez, string which)
    {
        _doc.ClearSelection2(true);
        if (!p.Select4(false, null)) return false;
        if (!_doc.Extension.SelectByID2("", "EDGE", ex, ey, ez, true, 0, null, 0)) return false;
        _doc.SketchAddConstraints(which);
        _doc.ClearSelection2(true);
        return true;
    }

    private static string Mm(double m) { return (m * 1000).ToString("0.###"); }
    private static string Box(double[] b)
    {
        return Mm(Math.Abs(b[3] - b[0])) + " x " + Mm(Math.Abs(b[4] - b[1])) + " x " + Mm(Math.Abs(b[5] - b[2])) +
               " (X " + Mm(b[0]) + ".." + Mm(b[3]) + ", Y " + Mm(b[1]) + ".." + Mm(b[4]) + ")";
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
