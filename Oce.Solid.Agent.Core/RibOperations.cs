using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE RIB. (entry W1.2, which was GP10)
    ///
    /// A thin wall without stiffening warps: the corner of an L bracket,
    /// the bottom of a moulded box. The rib fills, IN THE PLANE
    /// of the sketch, the space between a line and the walls of the part that the
    /// line (extended) meets, and thickens it crosswise.
    ///
    /// ── THE API GUIDE, AND THE BOOLEAN THAT NOBODY HAD TURNED ────────────
    ///
    /// Pages of the API guide (sldworksapi, extracted locally), by name:
    ///
    ///   * `SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~InsertRib.html`
    ///     — `void InsertRib(Is2Sided, ReverseThicknessDir, Thickness,
    ///     ReferenceEdgeIndex, ReverseMaterialDir, IsDrafted, DraftOutward,
    ///     DraftAngle, IsNormToSketch, IsDraftedFromWall)`. «IsNormToSketch:
    ///     True if extrusion direction is normal to sketch, false if parallel
    ///     to sketch»; «ReverseMaterialDir: True to flip the direction of the
    ///     extrusion»; «Is2Sided: ... a double-sided rib thickens on both sides
    ///     of the sketch». **It returns void**: success is measured on the part.
    ///   * `Get_Rib_Feature_Data_Example_CSharp.htm` — the only example. It closes
    ///     the sketch, selects it by name and type («Sketch2», "SKETCH", mark
    ///     0) and calls `InsertRib(true, false, 0.00254, 0, false, false, true,
    ///     0.0174532925199433, false, false)`: the ninth argument,
    ///     IsNormToSketch, is **false**. The rib grows in the plane of the
    ///     sketch, from the line toward the walls. Then it reads the feature with
    ///     `GetSelectedObject6(1, -1)` and `GetDefinition` → `RibFeatureData2`.
    ///   * `...IRibFeatureData2~FlipSide.html` («True if the material is added
    ///     to the reverse side of the rib»), `~Thickness.html`,
    ///     `~IsTwoSided.html`, `~ExtrusionDirection.html`
    ///     (swRibExtrusionDirection_e): they are re-read and reported as
    ///     DECLARED by the feature, next to the volume which is the measure.
    ///
    /// The historic probes (the four probes of 16/9/2026)
    /// ALL passed IsNormToSketch = true — even those that already had
    /// the right L with the diagonal in the void — and never turned the
    /// direction of the material. RAG note 82zz-quinquies.
    ///
    /// ── THE PROBE OF 28/9/2026 (`Checks\Rib\Program.cs`) ──
    ///
    /// A new part for every row; truth = a NEW feature of type «Rib»
    /// plus the volume delta:
    ///
    ///   L 60x60x10 deep 50, plane at z=25, line (10,40)->(40,10), thickness 6
    ///     normal to the sketch                  -> nothing    (55000 -> 55000)
    ///     parallel, material straight           -> «Nervatura1», +2700 = 450 mm2 x 6
    ///     parallel, material turned             -> nothing
    ///     parallel, sketch left open            -> «Nervatura1», +2700
    ///   box 40x40x20 shelled (2), right plane, line at y=12 from z=-15 to 15, thickness 2
    ///     parallel, material straight           -> nothing    (8672 -> 8672)
    ///     parallel, material turned             -> «Nervatura1», +720 = 36 x 10 x 2
    ///   plate 40x40x2, same line                -> nothing in either direction
    ///
    /// **The direction of the material depends on the shape** (L: straight; box:
    /// turned), and from the outside there is no rule to know it beforehand. So
    /// here the straight direction is tried and, if the rib is not born, the turned one;
    /// the one in which it is born and the volume grows is kept. In the wrong direction the
    /// probe never saw anything born (no feature, equal volume):
    /// the failed attempt leaves nothing to remove except the sketch, which
    /// is needed for the second attempt.
    ///
    /// **The second attempt on the SAME part was not measured by the
    /// probe** (each of its rows was a new part): the case
    /// `rib_in_the_hollowed_box` measures it, where the first direction fails and the
    /// second must give +720.
    ///
    /// The plate without walls gives birth to nothing, and that is fine: from the
    /// line there is nothing to meet. It is refused saying so, and naming
    /// `extrude_on_face` for a boss on a planar face.
    ///
    /// ── WHAT STAYS IN THE PART IF I REFUSE ───────────────────────────────
    ///
    /// Nothing. The offset plane (if I made it), the sketch and the possible
    /// rib born crooked are removed with `DeleteSelection2` (not with
    /// EditDelete, which opens a confirmation window and the COM call does not
    /// return: see `DeleteFeature`), from the youngest to the oldest, and
    /// the volume is re-read (`IsBackAsItWas` in GuardOperations).
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// How far an end of the line may be off the plane, in mm. The
        /// same margin as `sketched_bend`: five hundredths cover the
        /// roundings of the state and stay far from any real error.
        /// </summary>
        private const double RibOffPlaneMm = 0.05;

        /// <summary>
        /// Under this volume «grown» is not a measure, it is noise in the
        /// reading in cubic metres (`VolumeMm3`).
        /// </summary>
        private const double RibMinimumGrowthMm3 = 0.01;

        private Outcome Rib(IModelDoc2 model, Dictionary<string, object> a)
        {
            if (!(model is IPartDoc))
                return Failed("The rib is made on a part. Open the part for me.");

            // ── THE ARGUMENTS, BEFORE TOUCHING THE PART ──────────────────
            string plane = (Str(a, "plane", "") ?? "").Trim().ToLowerInvariant();
            if (plane != "front" && plane != "top" && plane != "right")
                return Failed(
                    "Tell me «plane»: front, top or right. It is the plane the line is on and " +
                    "in which the rib grows; with «distance_mm» I move it parallel to itself " +
                    "(for example to half the depth of a bracket).");
            // OPTIONAL, BUT IF IT IS THERE IT MUST BE A NUMBER: `Num` would swallow
            // «25mm» and return zero, that is the rib on the base plane.
            double distance = 0;
            if (a != null && a.TryGetValue("distance_mm", out var raw) && raw != null &&
                !RequiredNum(a, "distance_mm", out distance, out string distanceError))
                return Failed(distanceError + " Write it in mm, for example 25; without it, the rib is on " +
                               "the base plane.");

            if (!RequiredNum(a, "thickness_mm", out double thickness, out string error))
                return Failed(error + " It is the thickness of the rib: write it in mm " +
                               "(it is split in half on the two sides of the plane).");
            if (thickness <= 0)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "«thickness_mm» must be more than zero: {0:0.###} is not a rib. For a " +
                    "bracket in 10 plate, 4-6 mm is the usual value.", thickness));

            var da = Triplet(a, "start_mm");
            var upTo = Triplet(a, "end_mm");
            if (da == null || upTo == null)
                return Failed(
                    "«start_mm» and «end_mm» are the two ends of the line of the rib: points [x, y, z] in mm in " +
                    "the coordinate system of the model, both on the indicated plane. For example, in " +
                    "the corner of an L: from [10, 40, 25] to [40, 10, 25].");

            // DO THE TWO POINTS LIE ON THE PLANE? The base plane «front» is XY
            // (normal +Z), «top» XZ (+Y), «right» YZ (+X): the same
            // table as `References.PlaneNormal`, and the sign of the
            // distance is that of `create_plane` (front -250 -> z = -250,
            // measured by the case front_plane_minus_250).
            var normal = References.PlaneNormal(plane);
            Func<double[], double> dimension = p => p[0] * normal[0] + p[1] * normal[1] + p[2] * normal[2];
            double deviation = Math.Max(Math.Abs(dimension(da) - distance), Math.Abs(dimension(upTo) - distance));
            if (deviation > RibOffPlaneMm)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "The line is not on the plane {0} at distance {1:0.###} mm: one end is {2:0.###} " +
                    "mm away from it. The two points must be taken on the plane of the rib " +
                    "(on the {0} offset by {1:0.###}, the coordinate {3} of both is {1:0.###}), or " +
                    "change «distance_mm».",
                    plane, distance, deviation, plane == "front" ? "z" : plane == "top" ? "y" : "x"));
            double span = Math.Sqrt(Math.Pow(upTo[0] - da[0], 2) + Math.Pow(upTo[1] - da[1], 2) + Math.Pow(upTo[2] - da[2], 2));
            if (span < 1)
                return Failed("The line of the rib is shorter than a millimeter: the two points " +
                               "coincide. Put the two ends at two different points.");

            // ── THE STARTING PART ────────────────────────────────────────
            var photo = SnapshotPart(model);
            if (photo.Volume == null) return Failed(VolumeNotReadBefore("making the rib"));
            if (photo.Bodies == 0)
                return Failed(
                    "There is no solid body in the part: the rib fills the space between a line and walls " +
                    "that are already there. First make the part (for example with «extrude_profile»).");
            var ribBefore = RibNamesOfType(model, "Rib");
            var sketchesBefore = RibNamesOfType(model, "ProfileFeature");

            // What I create, from the oldest to the youngest: if I
            // refuse it is removed in reverse.
            var mie = new List<IFeature>();

            // ── THE PLANE OF THE SKETCH ──────────────────────────────────
            IFeature sketchPlane;
            bool planeMadeHere = Math.Abs(distance) > 1e-9;
            if (!planeMadeHere)
            {
                sketchPlane = FindBasePlane(model, plane);
                if (sketchPlane == null)
                    return Failed("I did not find the plane «" + plane + "» in the tree.");
            }
            else
            {
                sketchPlane = RibPlaneOffset(model, plane, distance, out string planeReason);
                if (sketchPlane == null) return Failed(planeReason);
                mie.Add(sketchPlane);
            }

            // ── THE SKETCH: A LINE, PASSED THROUGH ModelToSketchTransform ──
            //
            // On the right plane the sketch x goes into model -Z (RAG 81r):
            // it is not reasoned about, it is transformed. It is the recipe of the probe and of
            // `sketched_bend`.
            model.ClearSelection2(true);
            if (!sketchPlane.Select2(false, 0))
                return Failed("I could not select the plane of the sketch." +
                               RibWithdraw(model, mie, photo));
            model.SketchManager.InsertSketch(true);
            var sk = model.SketchManager.ActiveSketch as ISketch;
            var math = _sw.GetMathUtility() as IMathUtility;
            if (sk == null || math == null)
            {
                try { if (model.SketchManager.ActiveSketch != null) model.SketchManager.InsertSketch(true); } catch { }
                // Everything born of that type goes back out, not just one.
                mie.AddRange(FeaturesBornOfType(model, "ProfileFeature", sketchesBefore));
                return Failed("The sketch of the rib did not open." + RibWithdraw(model, mie, photo));
            }
            var tr = sk.ModelToSketchTransform;
            Func<double[], double[]> inSketch = p =>
                ((IMathPoint)((IMathPoint)math.CreatePoint(new[] { Units.M(p[0]), Units.M(p[1]), Units.M(p[2]) }))
                    .MultiplyTransform(tr)).ArrayData as double[];
            var s0 = inSketch(da);
            var s1 = inSketch(upTo);
            object line = null;
            bool addBefore = model.SketchManager.AddToDB;
            try
            {
                model.SketchManager.AddToDB = true;
                line = model.SketchManager.CreateLine(s0[0], s0[1], 0, s1[0], s1[1], 0);
            }
            finally { model.SketchManager.AddToDB = addBefore; }
            model.SketchManager.InsertSketch(true);
            int sketchesBorn;
            var sketch = RibNewOfType(model, "ProfileFeature", sketchesBefore, out sketchesBorn);
            if (sketch != null) mie.Add(sketch);
            else mie.AddRange(FeaturesBornOfType(model, "ProfileFeature", sketchesBefore));
            if (line == null || sketch == null)
                return Failed(SketchNotIdentified(sketchesBorn, "The line of the rib was not created in the sketch.") +
                               RibWithdraw(model, mie, photo));

            // THE TREE AFTER THE SKETCH: it is as it must be before EVERY
            // attempt. A failed attempt that leaves a feature of another
            // type (not a «Rib») is seen by nobody, and the second attempt would
            // build on top of it. The probe never saw it happen; if
            // it happens the other direction is not tried, it withdraws and says so.
            var namesAfterTheSketch = RibNamesOfAll(model);

            // ── THE TWO DIRECTIONS OF THE MATERIAL ───────────────────────
            IFeature rib = null;
            bool hasRun = false;
            int tentativi = 0;
            var outcomes = new List<string>();
            foreach (bool flip in new[] { false, true })
            {
                tentativi++;
                string alzato = RibInsert(model, sketch, thickness, flip);
                try { model.EditRebuild3(); } catch { }
                int ribsBorn;
                var born = RibNewOfType(model, "Rib", ribBefore, out ribsBorn);
                string facing = flip ? "flipped material" : "straight material";
                if (born == null && ribsBorn > 1)
                {
                    // Two ribs from one call: which one is ours is not known, so
                    // none is kept and every one goes back out.
                    mie.AddRange(FeaturesBornOfType(model, "Rib", ribBefore));
                    return Failed(ribsBorn + " rib features were born from one InsertRib call (" + facing +
                                   "), and nothing tells which one is the rib asked for: none is kept." +
                                   RibWithdraw(model, mie, photo));
                }
                if (born == null)
                {
                    outcomes.Add(facing + ": no rib" + (alzato == null ? "" : " (" + alzato + ")"));
                    var intruse = RibNewOfAll(model, namesAfterTheSketch);
                    if (intruse.Count > 0)
                    {
                        var intruderNames = new List<string>();
                        foreach (var f in intruse) { intruderNames.Add(RibName(f)); mie.Add(f); }
                        return Failed(
                            "With the " + facing + " the rib was not created, but the tree grew all the same («" +
                            string.Join("», «", intruderNames) + "»): I do not try the other direction on " +
                            "top of a feature that I did not ask for. Usually the line is not between two " +
                            "walls: move it into the empty space, with the ends (or their extension) on the " +
                            "walls to join; for a boss on a planar face use «extrude_on_face»." +
                            RibWithdraw(model, mie, photo));
                    }
                    continue;
                }
                // A RIB BORN THAT ADDS NO MATERIAL. The probe never
                // saw it: in the wrong direction nothing was born. If it
                // happens the other direction is not tried (removing only the
                // rib while leaving the sketch is not measured): everything is
                // withdrawn and it is said.
                mie.Add(born);
                double? after = VolumeMm3(model);
                if (after == null || after.Value - photo.Volume.Value < RibMinimumGrowthMm3)
                    return Failed(string.Format(CultureInfo.InvariantCulture,
                        "With the {0} SOLIDWORKS created «{1}», but the volume of the part {2}: a " +
                        "rib that adds no material stiffens nothing. Usually the line is inside the " +
                        "material instead of in the empty space between the walls: try a line in the empty " +
                        "space, with the ends (or their extension) on the walls to join.{3}",
                        facing, RibName(born),
                        after == null ? "cannot be read back" : "stayed " + after.Value.ToString("0.##", CultureInfo.InvariantCulture) + " mm3",
                        RibWithdraw(model, mie, photo)));
                rib = born;
                hasRun = flip;
                outcomes.Add(facing + ": created «" + RibName(born) + "»");
                break;
            }

            if (rib == null)
                return Failed(
                    "The rib does not come out in either direction of the material (" +
                    string.Join("; ", outcomes) + "). A rib fills, in the plane of the sketch, the space " +
                    "between the line and the walls of the part that the extended line meets: " +
                    "the corner of an L, the bottom and the sides of a hollowed box. When it " +
                    "does not come out in either direction, usually there is nothing for the line to " +
                    "meet: a plate without walls (measured), or a line outside the part. For a boss on " +
                    "a planar face use «extrude_on_face»; or move the line between two walls." +
                    RibWithdraw(model, mie, photo));

            // ── THE GUARDS, ON THE PART AND NOT ON THE FEATURE ───────────
            bool rebuilt = true;
            try { rebuilt = model.EditRebuild3(); } catch { }
            string broken = WhatBroke(model, photo, rebuilt, aperture: false);
            if (broken != null)
                return Failed("The rib «" + RibName(rib) + "» was created, but " + broken + "." +
                               RibWithdraw(model, mie, photo));

            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);
            double? added = volumeAfter - photo.Volume;

            // WHAT THE FEATURE DECLARES (IRibFeatureData2), next to what
            // was measured. It decides nothing: the declared thickness and direction
            // are those asked for even when the part is something else
            // (the lesson of the draft, `angle_declared_by_feature`).
            object declaredThickness = null, declaredFacing = null, declaredParallel = null;
            try
            {
                var d = rib.GetDefinition() as IRibFeatureData2;
                if (d != null)
                {
                    declaredThickness = Math.Round(Units.Mm(d.Thickness), 3);
                    declaredFacing = d.FlipSide;
                    declaredParallel = d.ExtrusionDirection == (int)swRibExtrusionDirection_e.swRibParallelToSketch;
                }
            }
            catch { }

            var measurements = new Dictionary<string, object>
            {
                // ASKED FOR, AND SAID SO: they are the arguments (and the length
                // of the line made with them), not measures. The measures are the
                // volume, the bodies, the attempts and the re-read «declared_*».
                ["requested_thickness_mm"] = Math.Round(thickness, 3),
                ["requested_sketch_plane"] = plane,
                ["requested_plane_offset_mm"] = Math.Round(distance, 3),
                ["requested_line_length_mm"] = Math.Round(span, 3),
                ["plane_created"] = planeMadeHere,
                ["material_side"] = hasRun ? "flipped" : "straight",
                ["attempts"] = tentativi,
                ["added_material_mm3"] = NumberOrNull(added, 2),
                ["volume_mm3"] = NumberOrNull(volumeAfter, 2),
                ["bodies"] = NumberOrNull(bodiesAfter),
                ["declared_thickness_mm"] = declaredThickness,
                ["declared_flip_side"] = declaredFacing,
                ["declared_parallel_to_sketch"] = declaredParallel,
                ["not_checked"] =
                    "I measured that the rib was created and that the part grew, and that it broke nothing; " +
                    "I did not look at WHICH walls it joined. The direction of the material I tried myself " +
                    "(first straight, then flipped) and I kept the first one in which it is created: if " +
                    "the line is between walls on both sides, the straight direction wins even if you thought " +
                    "the other one. No draft: the rib has straight faces.",
            };
            if (volumeAfter == null) measurements["volume_not_reread"] = NoteVolumeNotReread;

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agent] rib {0} on {1}{2}: {3}, volume {4} -> {5}",
                RibName(rib), plane, planeMadeHere ? " a " + distance.ToString("0.###", CultureInfo.InvariantCulture) : "",
                string.Join("; ", outcomes), ReadValue(photo.Volume), ReadValue(volumeAfter)));

            // No direction in the outcome: the thickness goes along the normal of the
            // plane, but the faces of the rib are not the «bottom» of anything.
            return Succeeded(rib, measurements);
        }

        // =================================================================

        /// <summary>
        /// The call from the guide, with its ten arguments by name.
        /// Null if it did not raise, otherwise the message (`InsertRib` is void:
        /// what counts is the tree afterwards).
        ///
        /// The sketch is reselected EVERY time, by name and type and with mark 0
        /// as in the guide's example: a failed attempt may have
        /// consumed the selection.
        /// </summary>
        private static string RibInsert(IModelDoc2 model, IFeature sketch, double thicknessMm, bool flipMaterial)
        {
            try
            {
                model.ClearSelection2(true);
                string itemName = RibName(sketch);
                bool taken = false;
                try { taken = model.Extension.SelectByID2(itemName, "SKETCH", 0, 0, 0, false, 0, null, 0); } catch { }
                if (!taken) taken = sketch.Select2(false, 0);
                if (!taken) return "the sketch cannot be selected";
                model.FeatureManager.InsertRib(
                    Is2Sided: true,                 // thickness split in half between the two parts of the plane
                    ReverseThicknessDir: false,     // holds only for a rib on one side
                    Thickness: Units.M(thicknessMm),
                    ReferenceEdgeIndex: 0,
                    ReverseMaterialDir: flipMaterial,
                    IsDrafted: false,
                    DraftOutward: false,
                    DraftAngle: 0,
                    IsNormToSketch: false,          // PARALLEL to the sketch: the ninth argument of the example
                    IsDraftedFromWall: false);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
            finally { try { model.ClearSelection2(true); } catch { } }
        }

        /// <summary>
        /// The plane parallel to the base plane, offset by `distanceMm`: the
        /// same call as `create_plane` since Z-C21.7 (L8-1), IFeatureManager.
        /// InsertRefPlane with the base plane selected by name with mark 0
        /// (ApiHelpers.SelectOffsetPlaneBase / InsertOffsetPlane, where the
        /// guide pages and the numbers of the Z-C21.3 probe are: G1 top +100,
        /// G4 top -100, G6 front -250 all where they belong). Until then it was
        /// the obsolete CreatePlaneAtOffset3, the call of the rib probe
        /// (Checks/Rib, `CreatePlaneAtOffset3(0.025, false, true)`).
        /// The tree decides whether the plane was born, not the return value.
        /// </summary>
        private static IFeature RibPlaneOffset(IModelDoc2 model, string plane, double distanceMm, out string reason)
        {
            reason = null;
            var basePlane = FindBasePlane(model, plane);
            if (basePlane == null) { reason = "I did not find the plane «" + plane + "» in the tree."; return null; }
            if (!ApiHelpers.SelectOffsetPlaneBase(model, basePlane))
            {
                reason = "I could not select the plane «" + plane + "»."; return null;
            }

            int planesBefore = CountPlanes(model);
            var namesBefore = FeatureNamesNow(model);
            IFeature returned = null;
            try
            {
                returned = ApiHelpers.InsertOffsetPlane(model, distanceMm) as IFeature;
            }
            catch (Exception ex) { reason = "The plane of the rib was not created: " + ex.Message; return null; }
            finally { try { model.ClearSelection2(true); } catch { } }

            // THE PLANE IS WHAT InsertRefPlane RETURNED, or else the ONE RefPlane
            // born since the call (FeatureBorn, scheme S7, Z-C21.5). Until then
            // it was the last feature of the tree, checked to be a RefPlane: on a
            // sheet-metal part that is the flat pattern, and a plane that was
            // born was refused and left in the tree.
            int planesAfter = CountPlanes(model);
            IFeature last = returned ?? FeatureBorn(model, namesBefore, "RefPlane", out _);
            string typ = "";
            try { typ = last?.GetTypeName2() ?? ""; } catch { }
            if (planesBefore < 0 || planesAfter != planesBefore + 1 || typ != "RefPlane")
            {
                reason = "The plane of the rib, parallel to the " + plane + " at " +
                         distanceMm.ToString("0.###", CultureInfo.InvariantCulture) +
                         " mm, was not created (planes " + planesBefore + " -> " + planesAfter + "). You can make it first " +
                         "with «create_plane» and look where it ends up.";
                return null;
            }
            return last;
        }

        /// <summary>
        /// Withdraws what this operation put in the part (from the
        /// youngest to the oldest) and returns the tail of the refusal
        /// sentence, with the volume re-read.
        ///
        /// `DeleteSelection2(swDelete_Absorbed)`: the rib takes away the
        /// sketch it absorbed, the sketch its relations. Before touching a
        /// feature we look that it has no children: if it has, something
        /// leaned on it and it is not my stuff.
        /// </summary>
        private string RibWithdraw(IModelDoc2 model, List<IFeature> mie, PartPhoto before)
        {
            if (mie.Count == 0) return " I did not touch the part: it is as it was.";
            bool everything = true;
            var names = new List<string>();
            // A SKETCH THAT A FAILED InsertRib HAS TOUCHED CANNOT BE DELETED, ONLY
            // SUPPRESSED. Measured on 28/9/2026 (probe five/probe_rib_withdraw,
            // SOLIDWORKS 29.5.1): after two failed InsertRib on a plate with no walls
            // the rib sketch has no children and selects fine, yet
            // DeleteSelection2 answers false with swDelete_Absorbed, swDelete_Children
            // and 0, EditDelete leaves it and three EditUndo2 do not take it away.
            // A new sketch made after the failures deletes normally, so the lock is
            // on that sketch only. EditSuppress2 works (IsSuppressed read back true).
            // Such a sketch is suppressed and named in the answer, instead of
            // declaring a restore that did not happen (full bench on 0.141.0,
            // limit_rib_on_a_plate_without_walls).
            var suppressedOnes = new List<string>();
            for (int i = mie.Count - 1; i >= 0; i--)
            {
                string itemName = RibName(mie[i]);
                names.Add(itemName);
                if (string.IsNullOrEmpty(itemName) || FindFeature(model, itemName) == null) continue;   // absorbed by whoever removed it before
                try
                {
                    if (mie[i].IGetChildCount() > 0) { everything = false; continue; }
                    model.ClearSelection2(true);
                    if (!mie[i].Select2(false, 0)) { everything = false; continue; }
                    if (!model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed))
                    {
                        string typ = "";
                        try { typ = mie[i].GetTypeName2(); } catch { }
                        bool suppressed = false;
                        if (typ == "ProfileFeature")
                        {
                            model.ClearSelection2(true);
                            if (mie[i].Select2(false, 0))
                            {
                                try { model.EditSuppress2(); } catch { }
                                // IsSuppressed2 in the active configuration since Z-C21.7 (S3).
                                try { suppressed = ApiHelpers.IsSuppressedHere(mie[i]); } catch { }
                            }
                        }
                        if (suppressed) suppressedOnes.Add(itemName);
                        else everything = false;
                    }
                }
                catch (Exception ex)
                {
                    _log("[agent] rib: I cannot remove «" + itemName + "»: " + ex.Message);
                    everything = false;
                }
                finally { try { model.ClearSelection2(true); } catch { } }
            }
            try { model.EditRebuild3(); } catch { }
            // THE TREE IS RE-READ, NOT THE RETURN VALUE.
            foreach (var n in names)
                if (!string.IsNullOrEmpty(n) && !suppressedOnes.Contains(n) && FindFeature(model, n) != null) everything = false;
            names.Reverse();
            var removed = names.FindAll(n => !suppressedOnes.Contains(n));
            string queue = IsBackAsItWas(model, before, everything,
                "the part as I found it" + (removed.Count > 0 ? " (removed «" + string.Join("», «", removed) + "»)" : ""));
            if (suppressedOnes.Count > 0)
                queue += " The sketch «" + string.Join("», «", suppressedOnes) + "» stays in the tree, SUPPRESSED: after " +
                        "a failed rib SOLIDWORKS does not let it be deleted (measured), only suppressed.";
            return queue;
        }

        /// <summary>The names of the features of that type, now.</summary>
        private static HashSet<string> RibNamesOfType(IModelDoc2 model, string typ)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var f = model.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (f.GetTypeName2() == typ) names.Add(f.Name);
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return names;
        }

        /// <summary>The names of all the features of the tree, now.</summary>
        private static HashSet<string> RibNamesOfAll(IModelDoc2 model)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var f = model.FirstFeature() as IFeature;
                while (f != null)
                {
                    names.Add(f.Name);
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return names;
        }

        /// <summary>The features that were not there before, from the oldest to the youngest.</summary>
        private static List<IFeature> RibNewOfAll(IModelDoc2 model, HashSet<string> before)
        {
            var newOnes = new List<IFeature>();
            try
            {
                var f = model.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (!before.Contains(f.Name)) newOnes.Add(f);
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return newOnes;
        }

        /// <summary>
        /// The ONE feature of that type whose name was not in <paramref name="before"/>,
        /// or null; <paramref name="born"/> says how many were born (-1: the tree
        /// was not read). Z-C21.5 (S7, review of the leftovers, 30/9/2026): this
        /// kept the LAST new one, so two born would have given one of them without
        /// a word; now it is <see cref="FeatureBorn"/>, which answers null for two.
        /// </summary>
        private static IFeature RibNewOfType(IModelDoc2 model, string typ, HashSet<string> before, out int born)
        {
            return FeatureBorn(model, before, typ, out born);
        }

        /// <summary>
        /// Every feature of that type whose name was not in <paramref name="before"/>,
        /// in tree order: what goes back out when a step is taken away. Empty when
        /// the tree cannot be read.
        /// </summary>
        private static List<IFeature> FeaturesBornOfType(IModelDoc2 model, string typ, HashSet<string> before)
        {
            var newOnes = new List<IFeature>();
            List<IFeature> features;
            List<string> names, types;
            if (before == null || !ReadFeatureTree(model, out features, out names, out types)) return newOnes;
            foreach (int i in BornFeatureIndices(names, before))
                if (i < types.Count && types[i] == typ) newOnes.Add(features[i]);
            return newOnes;
        }

        private static string RibName(IFeature f)
        {
            try { return f?.Name ?? ""; } catch { return ""; }
        }
    }
}
