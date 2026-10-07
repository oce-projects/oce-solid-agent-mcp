using System;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  FaceFilletProbe — GP7, the «fillet_face» part (face fillet)
// =============================================================================
//
//  CONTEXT (read from the TODO (entry GP7), CHANGELOG.md 0.47.0, and from the
//  historic fillet probe (its face-fillet method), never entered in the repo): four attempts, all on a 40x40x40 cube,
//  top face + one side face (ADJACENT faces, they share
//  an edge), all with
//
//      FeatureManager.FeatureFillet3(
//          Options: Propagate | UniformRadius | both | none (4 runs),
//          Ftyp:    swFeatureFilletType_e.swFeatureFilletType_Face,
//          ... selection marks = swSimpleFilletWhichFaces_e.swFaceFilletSet1/Set2 ...)
//
//  and all four return NULL. The entry (TODO GP7) records the hypothesis that
//  a pair of NON-adjacent faces is needed (the "real" use of the face fillet: a
//  blend between faces separated by a step).
//
//  WHAT THE GUIDE SAYS, AND NONE OF THE 4 ATTEMPTS TOUCHED:
//
//  1) `IFeatureManager::FeatureFillet3` — reference page, section
//     Remarks: it declares the method "obsolete" (its own word, from
//     version 2020 onwards) for a list of cases that EXPLICITLY includes
//     the face fillets, and refers to
//     CreateDefinition/CreateFeature/ISimpleFilletFeatureData2 to create them. SOLIDWORKS
//     installed: 2021 SP5.1 — the method used in all 4 attempts is
//     EXACTLY the one that the guide says is obsolete for the Face
//     type. It is plausible that for this reason it always returns NULL whatever
//     combination of `Options`, while the same method works for the
//     SIMPLE fillet (Ftyp=Simple, already in production: see
//     SwOperations.cs::RunFillet) — the same page lists that one too
//     among the obsolete ones, but evidently tolerated for backward
//     compatibility on the common cases, while Face is not in any
//     combination tried.
//
//  2) `ISimpleFilletFeatureData2` — reference page, Remarks, line
//     "Face fillet/face-face chamfer": it asks for SelectByID2 with Mark=2 for the
//     first set of faces and Mark=4 for the second. The VBA example
//     embedded in the same page (precondition: a SOLIDWORKS 2020 sample cube,
//     postcondition an asymmetric face fillet between the
//     selected faces) confirms it:
//         swFilletData.Initialize swFaceFillet
//         ' marks 2 and 4, the second selection in Append
//     The marks USED in the 4 attempts were instead 1 and 2 — the values
//     of the enum `swSimpleFilletWhichFaces_e` (read by reflection on
//     SolidWorks.Interop.swconst.dll: swFaceFilletSet1=1, swFaceFilletSet2=2).
//     That enum is the WhichFaceList parameter of ISetFaces/GetFaces — a
//     DIFFERENT place from the selection Mark — and its values do not
//     coincide with the marks that the guide asks for (2 and 4). It is a new
//     trap, of the same family as "the enum with the right name is the wrong
//     one" already paid for on GP8: a name that looks like the right place.
//
//  3) The VBA example of the same page uses two ADJACENT faces of a cube
//     (the entry says so explicitly in its own comment) — exactly
//     the geometry of the 4 failed attempts. This makes the hypothesis
//     of entry GP7 ("non-adjacent faces are needed") weak: the guide itself
//     shows a face fillet between adjacent faces that share
//     an edge.
//
//  HYPOTHESES, IN ORDER OF PROBABILITY:
//   H1 — wrong API: FeatureFillet3 is obsolete for Ftyp=Face since SW2020.
//        Proof: FeatureFillet3.html, Remarks (quoted above).
//   H2 — wrong marks: 1/2 instead of 2/4, for having reused the enum
//        swSimpleFilletWhichFaces_e (meant for ISetFaces) as if they were
//        the selection Marks. Proof: ISimpleFilletFeatureData2.html,
//        Remarks, line "Face fillet/face-face chamfer", and the VBA example
//        embedded in the same page.
//   H3 — Even with the right API and marks, the profile specification is missing
//        (ConicTypeForCrossSectionProfile): the guide lists it as an
//        explicit step ("Complete the fillet/chamfer type specification...")
//        before the feature can be created.
//   H4 (demotes the one of the entry) — non-adjacent faces: the guide
//        refutes it (see point 3), but a stepped case is tried anyway
//        to close the question with a number, not with a supposition.
//
//  THE RECIPE OF THE GUIDE (from ISimpleFilletFeatureData2.html + CreateDefinition.html):
//   1. swFeatDataObj = FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet)
//      — swFmFillet is described there as "constant radius, face, full round
//      fillet/chamfer": the same Type for all three, and it is Initialize() afterwards that
//      chooses which.
//   2. swFilletData = (SimpleFilletFeatureData2)swFeatDataObj
//   3. swFilletData.Initialize((int)swSimpleFilletType_e.swFaceFillet)   // = 2
//   4. Select the faces BEFORE calling CreateFeature:
//        SelectData Mark=2 -> Face Set 1
//        SelectData Mark=4 -> Face Set 2   (Append=true on the second)
//   5. (optional but the guide asks for it to complete the type)
//      swFilletData.ConicTypeForCrossSectionProfile =
//          (int)swFeatureFilletProfileType_e.swFeatureFilletCircular
//   6. swFilletData.DefaultRadius = radius (symmetric)
//   7. swFeat = FeatureManager.CreateFeature(swFilletData)
//
//  The part: the same 40x40x40 cube already used in the 4 attempts (same
//  geometry, for direct comparison). Top face (normal +Z) and side
//  face (normal +X): they share the vertical edge at X=+20,Y=+20.
//  A fillet r=5 on that edge (40 long) removes 40*25*(1-pi/4) =
//  214.6 mm3 — the same control number already used in the
//  previous probe, to be able to compare line by line.
//
//  No EditDelete. No windows: no call here opens a modal
//  (CreateFeature on a malformed fillet fails silently or raises
//  a COM exception, it does not open a window). Every part is closed without
//  saving, in a finally block.
//
//  OUTCOME OF 28/9/2026, SOLIDWORKS 29.5.1 (the output of the probe, as it was):
//
//    0. CONTROL (original bug: FeatureFillet3 + Ftyp=Face + marks 1/2) : NULL   volume 64000 -> 64000 mm3 (removed 0, expected 214,6)   bodies 1 -> 1
//    1. RECIPE OF THE GUIDE (CreateDefinition+Initialize(swFaceFillet)+marks 2/4+profile) : «Raccordo1» (Fillet)   volume 64000 -> 63785,4 mm3 (removed 214,6, expected 214,6)   bodies 1 -> 1
//    2. VARIANT H2 (new API, WRONG marks 1/2) : NULL   volume 64000 -> 64000 mm3 (removed 0, expected 214,6)   bodies 1 -> 1
//    3. VARIANT H3 (API and marks right, WITHOUT ConicTypeForCrossSectionProfile) : «Raccordo1» (Fillet)   volume 64000 -> 63785,4 mm3 (removed 214,6, expected 214,6)   bodies 1 -> 1
//    4. BONUS (explicit SetFaces, typed Face2[], no selection) : «Raccordo1» (Fillet)   volume 64000 -> 63785,4 mm3 (removed 214,6, expected 214,6)   bodies 1 -> 1
//       SelectByID2: True/True
//    5. RECIPE OF THE GUIDE with SelectByID2 by coordinates (marks 2/4) : «Raccordo1» (Fillet)   volume 64000 -> 63785,4 mm3 (removed 214,6, expected 214,6)   bodies 1 -> 1
//
//  USAGE   Checks\FaceFillet\bin\FaceFillet.exe, with SOLIDWORKS open
//        (built by Checks\build-checks.ps1 (and by build.ps1, step 1c)).
// =============================================================================
internal static class FaceFilletProbe
{
    private static ISldWorks _sw;

    private static int Main()
    {
        _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");

        Check_OriginalBug();
        Ricetta_Guida();
        Variant_H2_WrongMarks();
        Variant_H3_WithoutProfile();
        Bonus_SetFacesTipizzato();
        // REVISION 28/9/2026: H4 removed from the run. The two faces of the "step"
        // have the SAME normal (+X) and sit on parallel planes offset by
        // 5 mm: no sphere can be tangent to both of them from the side of the
        // air, so that fillet is impossible by geometry and a
        // NULL would say nothing about the API. In its place the recipe with the
        // EXACT selection of the Remarks (SelectByID2 by coordinates).
        // Variant_H4_FacesNotAdjacent();
        Ricetta_SelectByID2();

        return 0;
    }

    // ------------------------------------------------------------------
    // 5. RECIPE with SelectByID2 — the Remarks of ISimpleFilletFeatureData2
    //    say "Call IModelDocExtension::SelectByID2 with Mark = 2 ... Mark = 4":
    //    same recipe as number 1, but selection by coordinates instead of with
    //    IEntity.Select4. Cube: X,Y in [-20,20], Z in [0,40] (extruded from the
    //    front plane along +Z): top face z=40, side x=+20.
    // ------------------------------------------------------------------
    private static void Ricetta_SelectByID2()
    {
        Run("5. RECIPE FROM THE GUIDE with SelectByID2 by coordinates (marks 2/4)",
            (doc, up, side) =>
            {
                var data = (SimpleFilletFeatureData2)doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet);
                data.Initialize((int)swSimpleFilletType_e.swFaceFillet);
                // points taken from the real box of the two faces, not assumed
                var ba = up.GetBox() as double[]; var bf = side.GetBox() as double[];
                bool s1 = doc.Extension.SelectByID2("", "FACE", (ba[0] + ba[3]) / 2, (ba[1] + ba[4]) / 2, (ba[2] + ba[5]) / 2, false, 2, null, 0);
                bool s2 = doc.Extension.SelectByID2("", "FACE", (bf[0] + bf[3]) / 2, (bf[1] + bf[4]) / 2, (bf[2] + bf[5]) / 2, true, 4, null, 0);
                Console.WriteLine("   SelectByID2: " + s1 + "/" + s2);
                data.ConicTypeForCrossSectionProfile = (int)swFeatureFilletProfileType_e.swFeatureFilletCircular;
                data.DefaultRadius = 0.005;
                return doc.FeatureManager.CreateFeature(data) as IFeature;
            });
    }

    // ------------------------------------------------------------------
    // 0. CONTROL — reproduces EXACTLY the attempt already made (old
    //    API, marks 1/2), to have the starting number in the same probe.
    //    Expected: NULL, as in the 4 runs already recorded.
    // ------------------------------------------------------------------
    private static void Check_OriginalBug()
    {
        Run("0. CONTROL (original bug: FeatureFillet3 + Ftyp=Face + marks 1/2)",
            (doc, up, side) =>
            {
                var handler = (ISelectionMgr)doc.SelectionManager;
                var d1 = handler.CreateSelectData();
                d1.Mark = (int)swSimpleFilletWhichFaces_e.swFaceFilletSet1; // = 1 (WRONG as a Mark)
                (up as IEntity).Select4(true, d1);
                var d2 = handler.CreateSelectData();
                d2.Mark = (int)swSimpleFilletWhichFaces_e.swFaceFilletSet2; // = 2 (WRONG as a Mark)
                (side as IEntity).Select4(true, d2);

                return doc.FeatureManager.FeatureFillet3(
                    Options: (int)(swFeatureFilletOptions_e.swFeatureFilletUniformRadius |
                                   swFeatureFilletOptions_e.swFeatureFilletPropagate),
                    R1: 0.005, R2: 0.005, Rho: 0,
                    Ftyp: (int)swFeatureFilletType_e.swFeatureFilletType_Face,
                    OverflowType: (int)swFilletOverFlowType_e.swFilletOverFlowType_Default,
                    ConicRhoType: (int)swFeatureFilletProfileType_e.swFeatureFilletCircular,
                    Radii: null, Dist2Arr: null, RhoArr: null, SetBackDistances: null,
                    PointRadiusArray: null, PointDist2Array: null, PointRhoArray: null) as IFeature;
            });
    }

    // ------------------------------------------------------------------
    // 1. RECIPE OF THE GUIDE — CreateDefinition/Initialize/CreateFeature,
    //    marks 2 and 4, profile declared. Expected: feature created, removed
    //    214.6 mm3.
    // ------------------------------------------------------------------
    private static void Ricetta_Guida()
    {
        Run("1. RECIPE FROM THE GUIDE (CreateDefinition+Initialize(swFaceFillet)+marks 2/4+profile)",
            (doc, up, side) =>
            {
                var dataObj = doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet);
                var data = (SimpleFilletFeatureData2)dataObj;
                if (!data.Initialize((int)swSimpleFilletType_e.swFaceFillet))
                {
                    Console.WriteLine("   Initialize(swFaceFillet) answered false.");
                    return null;
                }

                var handler = (ISelectionMgr)doc.SelectionManager;
                var d1 = handler.CreateSelectData(); d1.Mark = 2; // Face Set 1 — from the guide
                (up as IEntity).Select4(true, d1);
                var d2 = handler.CreateSelectData(); d2.Mark = 4; // Face Set 2 — from the guide
                (side as IEntity).Select4(true, d2);

                data.ConicTypeForCrossSectionProfile = (int)swFeatureFilletProfileType_e.swFeatureFilletCircular;
                data.DefaultRadius = 0.005;

                return doc.FeatureManager.CreateFeature(data) as IFeature;
            });
    }

    // ------------------------------------------------------------------
    // 2. VARIANT H2 — same new API, but with the WRONG marks (1/2,
    //    those reused from the WhichFaceList enum) to isolate whether the mark
    //    alone is enough to make even the correct API fail.
    // ------------------------------------------------------------------
    private static void Variant_H2_WrongMarks()
    {
        Run("2. VARIANT H2 (new API, WRONG marks 1/2)",
            (doc, up, side) =>
            {
                var dataObj = doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet);
                var data = (SimpleFilletFeatureData2)dataObj;
                data.Initialize((int)swSimpleFilletType_e.swFaceFillet);

                var handler = (ISelectionMgr)doc.SelectionManager;
                var d1 = handler.CreateSelectData();
                d1.Mark = (int)swSimpleFilletWhichFaces_e.swFaceFilletSet1; // = 1, wrong
                (up as IEntity).Select4(true, d1);
                var d2 = handler.CreateSelectData();
                d2.Mark = (int)swSimpleFilletWhichFaces_e.swFaceFilletSet2; // = 2, wrong
                (side as IEntity).Select4(true, d2);

                data.ConicTypeForCrossSectionProfile = (int)swFeatureFilletProfileType_e.swFeatureFilletCircular;
                data.DefaultRadius = 0.005;

                return doc.FeatureManager.CreateFeature(data) as IFeature;
            });
    }

    // ------------------------------------------------------------------
    // 3. VARIANT H3 — correct API and marks, but WITHOUT declaring
    //    ConicTypeForCrossSectionProfile, to see whether it is really
    //    necessary or whether CreateFeature copes with a default.
    // ------------------------------------------------------------------
    private static void Variant_H3_WithoutProfile()
    {
        Run("3. VARIANT H3 (API and marks correct, WITHOUT ConicTypeForCrossSectionProfile)",
            (doc, up, side) =>
            {
                var dataObj = doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet);
                var data = (SimpleFilletFeatureData2)dataObj;
                data.Initialize((int)swSimpleFilletType_e.swFaceFillet);

                var handler = (ISelectionMgr)doc.SelectionManager;
                var d1 = handler.CreateSelectData(); d1.Mark = 2;
                (up as IEntity).Select4(true, d1);
                var d2 = handler.CreateSelectData(); d2.Mark = 4;
                (side as IEntity).Select4(true, d2);

                // On purpose NOT set: data.ConicTypeForCrossSectionProfile
                data.DefaultRadius = 0.005;

                return doc.FeatureManager.CreateFeature(data) as IFeature;
            });
    }

    // ------------------------------------------------------------------
    // BONUS — the same recipe but without going through the selection: the faces
    // are passed directly to SetFaces with a TYPED array Face2[]
    // (never object[]: it is the trap already paid for on GP8/combine_bodies and
    // on the APIs with Body2[]/Face2[]). It uses the CORRECT values of
    // swSimpleFilletWhichFaces_e (here it is the right place for that enum:
    // it is precisely the WhichFaceList parameter of SetFaces, not a Mark).
    // ------------------------------------------------------------------
    private static void Bonus_SetFacesTipizzato()
    {
        Run("4. BONUS (explicit SetFaces, typed Face2[], no selection)",
            (doc, up, side) =>
            {
                var dataObj = doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet);
                var data = (SimpleFilletFeatureData2)dataObj;
                data.Initialize((int)swSimpleFilletType_e.swFaceFillet);

                doc.ClearSelection2(true); // here NOTHING is selected: we go through SetFaces
                // TYPED array Face2[], not object[] (see the memory
                // note on typed arrays): with object[] the interop passes a
                // SAFEARRAY of VARIANT and the call may answer with a
                // phantom feature instead of NULL or a real error.
                data.SetFaces((int)swSimpleFilletWhichFaces_e.swFaceFilletSet1, new Face2[] { (Face2)up });
                data.SetFaces((int)swSimpleFilletWhichFaces_e.swFaceFilletSet2, new Face2[] { (Face2)side });

                data.ConicTypeForCrossSectionProfile = (int)swFeatureFilletProfileType_e.swFeatureFilletCircular;
                data.DefaultRadius = 0.005;

                return doc.FeatureManager.CreateFeature(data) as IFeature;
            });
    }

    // ------------------------------------------------------------------
    // VARIANT H4 — demoted by the guide (the VBA example of
    // ISimpleFilletFeatureData2 declares in its own comment that it selects
    // ADJACENT faces with marks 2 and 4), but it is tried anyway to close
    // the question with a number: two faces with the SAME
    // normal (+X), NOT adjacent, separated by a step (a second
    // smaller extrusion, taken from the top face of the cube and
    // set back on all sides — so the step is born along whichever
    // is the real "side" axis, without having to guess it). Correct API and
    // marks (2/4).
    // ------------------------------------------------------------------
    private static void Variant_H4_FacesNotAdjacent()
    {
        IModelDoc2 doc = null;
        try
        {
            doc = CubeWithStep();
            var partDoc = (IPartDoc)doc;
            double vBefore = Volume(partDoc);
            int bodiesBefore = CountBodies(partDoc);
            if (bodiesBefore != 1)
                Console.WriteLine("5. VARIANT H4 : WARNING, " + bodiesBefore +
                                   " bodies instead of 1 — the second extrusion did not merge with the cube.");

            var body = (partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[])[0] as IBody2;
            var faces = (body.GetFaces() as object[]).Cast<IFace2>().ToList();

            // The two faces with normal +X: that of the base block (lower
            // along Y, the extrusion axis) and that of the raised
            // block, set back and higher along Y — they do NOT share
            // an edge, they are separated by the horizontal face of the
            // step (the old "top" of the cube, now in the middle).
            var candidate = faces.Where(x => Normal(x)[0] > 0.9)
                                  .OrderByDescending(x => MeanDimension(x, 1))
                                  .ToList();
            if (candidate.Count < 2)
            {
                Console.WriteLine("5. VARIANT H4 (non-adjacent faces, step) : no candidate faces " +
                                   "(found " + candidate.Count + " faces with +X normal; expected 2)");
                return;
            }
            var top = candidate[0];
            var lower = candidate[candidate.Count - 1];

            var dataObj = doc.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmFillet);
            var data = (SimpleFilletFeatureData2)dataObj;
            data.Initialize((int)swSimpleFilletType_e.swFaceFillet);

            doc.ClearSelection2(true);
            var handler = (ISelectionMgr)doc.SelectionManager;
            var d1 = handler.CreateSelectData(); d1.Mark = 2;
            (top as IEntity).Select4(true, d1);
            var d2 = handler.CreateSelectData(); d2.Mark = 4;
            (lower as IEntity).Select4(true, d2);

            data.ConicTypeForCrossSectionProfile = (int)swFeatureFilletProfileType_e.swFeatureFilletCircular;
            data.DefaultRadius = 0.005;

            IFeature f = null;
            try { f = doc.FeatureManager.CreateFeature(data) as IFeature; }
            catch (Exception ex) { Console.WriteLine("5. VARIANT H4 (non-adjacent faces, step) : exception " + ex.Message); return; }

            try { doc.EditRebuild3(); } catch { }
            double removed = vBefore - Volume(partDoc);
            int bodiesAfter = CountBodies(partDoc);
            Console.WriteLine("5. VARIANT H4 (non-adjacent faces, step) : " +
                (f == null ? "NULL" : "«" + f.Name + "» (" + f.GetTypeName2() + ")") +
                "   volume " + vBefore.ToString("0.#") + " -> " + Volume(partDoc).ToString("0.#") +
                " mm3 (removed " + removed.ToString("0.#") + ")" +
                "   bodies " + bodiesBefore + " -> " + bodiesAfter);
        }
        catch (Exception ex)
        {
            Console.WriteLine("5. VARIANT H4 (non-adjacent faces, step) : " + ex.Message);
        }
        finally
        {
            if (doc != null) { try { _sw.CloseDoc(doc.GetTitle()); } catch { } }
        }
    }

    // ------------------------------------------------------------------
    // Engine common to the first 4 scenes: same cube, same two faces
    // (top = normal +Z, side = normal +X, adjacent on an edge),
    // same before/after photograph, same closing without saving.
    // `build` receives (doc, top_face, side_face) and returns the
    // IFeature (or null) — the whole part that changes between the scenes.
    // ------------------------------------------------------------------
    private static void Run(string label, Func<IModelDoc2, IFace2, IFace2, IFeature> build)
    {
        IModelDoc2 doc = null;
        try
        {
            doc = Cube();
            var partDoc = (IPartDoc)doc;
            double vBefore = Volume(partDoc);
            int bodiesBefore = CountBodies(partDoc);

            var body = (partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[])[0] as IBody2;
            var faces = (body.GetFaces() as object[]).Cast<IFace2>().ToList();
            var up = faces.FirstOrDefault(x => Normal(x)[2] > 0.9);
            var side = faces.FirstOrDefault(x => Normal(x)[0] > 0.9);
            if (up == null || side == null)
            {
                Console.WriteLine(label + " : no faces");
                return;
            }

            doc.ClearSelection2(true);

            IFeature f = null;
            try { f = build(doc, up, side); }
            catch (Exception ex) { Console.WriteLine(label + " : exception " + ex.Message); return; }

            try { doc.EditRebuild3(); } catch { }
            double vAfter = Volume(partDoc);
            double removed = vBefore - vAfter;
            int bodiesAfter = CountBodies(partDoc);

            string typ = "";
            try { if (f != null) typ = " (" + f.GetTypeName2() + ")"; } catch { }

            Console.WriteLine(label + " : " +
                (f == null ? "NULL" : "«" + f.Name + "»" + typ) +
                "   volume " + vBefore.ToString("0.#") + " -> " + vAfter.ToString("0.#") +
                " mm3 (removed " + removed.ToString("0.#") + ", expected 214,6)" +
                "   bodies " + bodiesBefore + " -> " + bodiesAfter);
        }
        catch (Exception ex)
        {
            Console.WriteLine(label + " : " + ex.Message);
        }
        finally
        {
            if (doc != null) { try { _sw.CloseDoc(doc.GetTitle()); } catch { } }
        }
    }

    // The normal of a PLANAR face, from the underlying surface, flipped if
    // the face is reversed: the normal of `PlaneParams` is that of the
    // plane, not that of the face. (Copied identically from the historic fillet probe.)
    private static double[] Normal(IFace2 f)
    {
        try
        {
            var s = f.IGetSurface();
            if (s == null || !s.IsPlane()) return new double[] { 0, 0, 0 };
            var p = s.PlaneParams as double[];
            if (p == null || p.Length < 3) return new double[] { 0, 0, 0 };
            double facing = f.FaceInSurfaceSense() ? 1 : -1;
            return new[] { p[0] * facing, p[1] * facing, p[2] * facing };
        }
        catch { return new double[] { 0, 0, 0 }; }
    }

    // Mean position of a planar face along the axis `index` (0=X,1=Y,2=Z),
    // from its bounding box — enough to ORDER two parallel faces
    // among themselves, not to measure them.
    private static double MeanDimension(IFace2 f, int index)
    {
        try
        {
            var box = f.GetBox() as double[]; // [xmin,ymin,zmin,xmax,ymax,zmax]
            if (box == null || box.Length < 6) return 0;
            return (box[index] + box[index + 3]) / 2.0;
        }
        catch { return 0; }
    }

    private static double Volume(IPartDoc partDoc)
    {
        double v = 0;
        var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        if (bodies == null) return 0;
        foreach (var c in bodies)
        {
            var mp = (c as IBody2).GetMassProperties(1000.0) as double[];
            if (mp != null && mp.Length > 4) v += mp[3] * 1e9;
        }
        return v;
    }

    private static int CountBodies(IPartDoc partDoc)
    {
        var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
        return bodies == null ? 0 : bodies.Length;
    }

    // The same 40x40x40 cube as the historic fillet probe, to compare
    // line by line with the 4 attempts already recorded.
    private static IModelDoc2 Cube()
    {
        string tpl = _sw.GetUserPreferenceStringValue(
            (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
        var doc = (IModelDoc2)_sw.NewDocument(tpl, 0, 0, 0);
        doc.ClearSelection2(true);
        SelectBasePlane(doc, 1);
        var sm = doc.SketchManager;
        sm.InsertSketch(true);
        sm.CreateCornerRectangle(-0.02, -0.02, 0, 0.02, 0.02, 0);
        sm.InsertSketch(false);
        doc.Extension.SelectByID2("Sketch1", "SKETCH", 0, 0, 0, false, 0, null, 0);
        doc.FeatureManager.FeatureExtrusion3(
            true, false, false, (int)swEndConditions_e.swEndCondBlind, 0, 0.040, 0,
            false, false, false, false, 0, 0, false, false, false, false,
            true, true, true, 0, 0, false);
        return doc;
    }

    // A 40x40x40 base block (Cube()) with a second smaller block
    // on top, set back SYMMETRICALLY on all sides with respect to the
    // face on which it is born — so the step forms on whichever pair
    // of axes is really the plane of that face, WITHOUT having to guess
    // which coordinate of `CreateCornerRectangle` corresponds to which global
    // axis (and without selecting by COORDINATES, which is fragile: here the
    // real IFace2 is selected, found by normal as "top"/"side").
    //
    // We start from the face with normal +Y (neither of the two already used,
    // "top"=+Z and "side"=+X): it is anyway ONE of the six faces of the cube,
    // and its plane contains by construction the TWO axes different from its
    // normal — so it always contains X, the "side" axis. A smaller rectangle
    // centred on the origin of the new sketch (which SOLIDWORKS
    // puts by default in the projection of the part origin, and the origin
    // is inside this face because the cube is centred on X and Z) stays
    // set back on all four sides, whatever the real order
    // of the axes in that sketch.
    private static IModelDoc2 CubeWithStep()
    {
        var doc = Cube();
        var partDoc = (IPartDoc)doc;
        var body = (partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[])[0] as IBody2;
        var faces = (body.GetFaces() as object[]).Cast<IFace2>().ToList();
        var top = faces.FirstOrDefault(x => Normal(x)[1] > 0.9);
        if (top == null) throw new Exception("CubeWithStep: cannot find the face with normal +Y.");

        doc.ClearSelection2(true);
        (top as IEntity).Select4(true, null);
        var sm = doc.SketchManager;
        sm.InsertSketch(true);
        // Set back by 0.005 on every side with respect to the half side of the cube
        // (0.020): the raised block turns out 0.030 x 0.030, centred.
        sm.CreateCornerRectangle(-0.015, -0.015, 0, 0.015, 0.015, 0);
        sm.InsertSketch(false);
        doc.Extension.SelectByID2("Sketch2", "SKETCH", 0, 0, 0, false, 0, null, 0);
        doc.FeatureManager.FeatureExtrusion3(
            true, false, false, (int)swEndConditions_e.swEndCondBlind, 0, 0.020, 0,
            false, false, false, false, 0, 0, false, false, false, false,
            true, true, true, 0, 0, false);
        return doc;
    }

    // W3.1-bis.2 point 10: base planes by position, not by their localized name
    // (this probe used to try the plane's Italian name, then its English one,
    // which only ever works on those two SOLIDWORKS UI languages).
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
