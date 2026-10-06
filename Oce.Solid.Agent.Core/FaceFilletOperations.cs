using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE FACE FILLET (entry GP7, the half that was missing).
    ///
    /// A fillet that attaches not to an EDGE but to TWO FACES: the ball of
    /// radius r rolls resting on both, and the surface it leaves is the
    /// fillet. On the edge of a cube it gives the same part as
    /// «fillet_edges»; the difference is that no edge needs to be
    /// named — the two faces are enough, even when the edge between them is not
    /// a single one or changes with every feature.
    ///
    /// ── THE GUIDE FIRST (method of the entry, 28/9/2026) ─────────────────
    ///
    /// Pages of the API guide (sldworksapi, extracted locally), by name:
    ///
    ///  * `IFeatureManager~FeatureFillet3.html`, Remarks: «As of SOLIDWORKS 2020,
    ///    this method is obsolete for creating: constant radius fillets, offset
    ///    face chamfers, face fillets, face-face chamfers, full round fillets».
    ///    The old way, that of `fillet_edges` and `variable_fillet`, is not
    ///    used here: it is the first of the two causes of the four historic NULLs.
    ///  * `ISimpleFilletFeatureData2.html`, Remarks, recipe: `CreateDefinition`
    ///    with `swFmFillet`; `Initialize` with `swSimpleFilletType_e.swFaceFillet`;
    ///    row «Face fillet/face-face chamfer»: «Call IModelDocExtension::SelectByID2
    ///    with: Mark = 2 to select Face Set 1. Mark = 4 to select Face Set 2»; for
    ///    the symmetric one `DefaultRadius`; then `IFeatureManager::CreateFeature`.
    ///    The VBA example of the same page selects «adjacent faces using Marks
    ///    2 and 4»: two adjacent faces of a cube, as here.
    ///  * `ISimpleFilletFeatureData2~Initialize.html`, Remarks: after `Initialize`
    ///    the type is narrowed with `ConicTypeForCrossSectionProfile`.
    ///  * `ISimpleFilletFeatureData2~SetFaces.html`: `WhichFaceList` is «Face as
    ///    defined in swSimpleFilletWhichFaces_e». It is the SECOND cause of the
    ///    historic NULLs: the selection marks had been taken from that enum
    ///    (Set1 = 1, Set2 = 2), which is the parameter of `SetFaces` and not a mark.
    ///    The enum with the right name is the wrong one, again (see GP8).
    ///
    /// ── MEASURED BEFORE WRITING (probe Checks\FaceFillet, 28/9/2026) ────
    ///
    /// Cube 40x40x40, face +Z (mark 2) and face +X (mark 4), r = 5. A
    /// fillet r on an edge of 40 between two faces at 90 degrees removes
    /// 40·5²·(1 − π/4) = 214.60 mm³:
    ///
    ///   FeatureFillet3 + Ftyp=Face + marks 1/2 (the old one)     NULL, removed 0
    ///   recipe of the guide (marks 2/4, circular profile)        «Raccordo1» (Fillet), removed 214.6
    ///   recipe of the guide with marks 1/2                       NULL, removed 0
    ///   recipe without ConicTypeForCrossSectionProfile           «Raccordo1» (Fillet), removed 214.6
    ///   SetFaces with typed Face2[], without selection           «Raccordo1» (Fillet), removed 214.6
    ///   SelectByID2 by coordinates, marks 2/4                    «Raccordo1» (Fillet), removed 214.6
    ///
    /// The second row is used, word for word: selection with mark 2 and 4 on
    /// real faces (IEntity.Select4), profile declared as the guide asks.
    /// Bodies 1 → 1 in all of them.
    ///
    /// ── WHAT IS MEASURED, AND WHEN IT REFUSES ───────────────────────────
    ///
    /// Before: the faces must be two, and if both are planar and
    /// PARALLEL no ball touches both of them from the air side (except for the
    /// case of two walls facing each other at an exact distance of 2r) — it says so without
    /// calling anything. After: the new feature and its type (`Fillet`), the
    /// volume before and after (a fillet that does not change the volume has done
    /// nothing), the bodies and the features in error (the guards of
    /// GuardOperations.cs). If something does not add up the fillet is REMOVED
    /// (DeleteSelection2, never the call with the confirmation window) and it
    /// is checked again that the part is as it was.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>Two normals with |cosine| above this are parallel.</summary>
        private const double FaceFilletAlmostParallel = 0.9999;

        private Outcome FaceFillet(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (!(model is IPartDoc))
                return Failed("The face fillet is done on a part: open the part and ask me there.");

            // TWO FACES, AND «face_ref» NAMES ONLY ONE. `ResolveFace`
            // looks at «face_ref» before the uid: with two faces to resolve the same
            // reference would win both times, and the fillet
            // would be asked between a face and itself.
            if (HasReference(a, "face_ref"))
                return Failed(
                    "Two different faces are needed here, and «face_ref» names only one: " +
                    "use «face_1_uid» and «face_2_uid» with the identifiers of the state. If one of " +
                    "the two is created by a step of this plan, read the state again after that step.");

            double radius;
            string error;
            if (!RequiredNum(a, "radius_mm", out radius, out error)) return Failed(error);
            if (radius <= 0)
                return Failed(
                    "«radius_mm» must be greater than zero: you asked " +
                    radius.ToString("0.##", CultureInfo.InvariantCulture) + ". For example 5.");

            string uid1 = (Str(a, "face_1_uid", "") ?? "").Trim();
            string uid2 = (Str(a, "face_2_uid", "") ?? "").Trim();
            if (uid1.Length == 0 || uid2.Length == 0)
                return Failed(
                    "Tell me the two faces: «face_1_uid» and «face_2_uid», with the identifiers " +
                    "of the state of the document (for example «F1» and «F3»). The fillet rests on " +
                    "both of them.");

            // THE SAME FACE TWICE. It is stated before calling: it is the
            // confusion of someone who wanted to round the edges of ONE face, and for
            // that there is another operation.
            if (string.Equals(uid1, uid2, StringComparison.OrdinalIgnoreCase))
                return Failed(
                    "«" + uid1 + "» is in both sets: a face fillet joins TWO different " +
                    "faces, resting on both of them. To round the borders of a single " +
                    "face use «fillet_edges» on its edges.");

            string reason;
            var face1 = ResolveFace(docState, a, uid1, out reason);
            if (face1 == null) return Failed("Face «" + uid1 + "»: " + reason);
            var face2 = ResolveFace(docState, a, uid2, out reason);
            if (face2 == null) return Failed("Face «" + uid2 + "»: " + reason);

            // TWO UIDs, ONE FACE. After a recovery by signature two different
            // names can have ended up on the same face: SOLIDWORKS is asked
            // (ISldWorks~IsSame.html, «Gets whether the two specified
            // objects are the same object»), as in OperazioniCorpi.
            bool same = ReferenceEquals(face1, face2);
            if (!same)
            {
                try { same = _sw.IsSame(face1, face2) == (int)swObjectEquality.swObjectSame; }
                catch { }
            }
            if (same)
                return Failed(
                    "«" + uid1 + "» and «" + uid2 + "» are now the same face: one of the two moved and " +
                    "was found again on the other. Read the state again and point to the two " +
                    "faces again.");

            // ── TWO PARALLEL PLANAR FACES ────────────────────────────────
            //
            // No call to SOLIDWORKS, only geometry: the ball of the
            // fillet must touch both faces from the air side.
            //  * same normal (coplanar, or a step): impossible;
            //  * opposite normals and the faces turn their backs on each other (front and back
            //    of a block): impossible;
            //  * opposite normals and they face each other (the walls of a pocket) at distance
            //    d: only with r = d/2. With r = d/2 SOLIDWORKS is let to try,
            //    and the guards afterwards say whether it was born.
            // The probe took the «facce parallele» variant out of the run
            // (Checks\FaceFillet, REVISION 28/9/2026) precisely because a
            // NULL there would not have measured the API but the geometry.
            string parallel = ParallelPlanarFaces(face1, face2, uid1, uid2, radius);
            if (parallel != null) return Failed(parallel);

            // ── THE HAND CALCULATION, READ BEFORE TOUCHING THE PART ──────
            //
            // Where there is a closed calculation: two planar faces at 90 degrees that
            // meet on an edge of length L. The section removed (or added, in
            // an inside corner) is r²(1 − π/4), times the length L. Elsewhere
            // (curved faces, other angles) an invented number is not written:
            // null, and stated.
            //
            // BEFORE and not after: after the fillet the two faces do NOT touch
            // any more (between them there is the face of the fillet), and the common
            // edge read there would be zero — the calculation would never have existed.
            double edge = -1;
            try { edge = LengthThatLiesOn(face1, face2); } catch { }
            double? expected = null;
            var n1 = FaceNormal(face1);
            var n2 = FaceNormal(face2);
            if (edge > 0 && n1 != null && n2 != null && n1.Length >= 3 && n2.Length >= 3 &&
                Math.Abs(n1[0] * n2[0] + n1[1] * n2[1] + n1[2] * n2[2]) < 1e-6)
                expected = Math.Round(edge * radius * radius * (1 - Math.PI / 4), 2);

            // ── THE SNAPSHOT FIRST (GuardOperations.cs) ────────────────
            var photo = SnapshotPart(model);
            if (photo.Volume == null) return Failed(VolumeNotReadBefore("making the face fillet"));
            int featureBefore = CountFeature(model);
            if (featureBefore < 0 || photo.Bodies < 0)
                return Failed(VolumeNotReadBefore("making the face fillet")
                    .Replace("the volume of the part", "the tree and the bodies of the part"));

            // ── THE RECIPE OF THE GUIDE, IN THE MEASURED ORDER ───────────
            IFeature feature;
            try
            {
                model.ClearSelection2(true);
                var data = model.FeatureManager.CreateDefinition(
                    (int)swFeatureNameID_e.swFmFillet) as SimpleFilletFeatureData2;
                if (data == null)
                    return Failed(
                        "SOLIDWORKS did not give me the definition of a fillet (CreateDefinition). " +
                        "I touched nothing: try «fillet_edges» on the edge between the two faces.");
                if (!data.Initialize((int)swSimpleFilletType_e.swFaceFillet))
                    return Failed(
                        "SOLIDWORKS does not accept a face fillet on this definition " +
                        "(Initialize answered false). I touched nothing: try «fillet_edges» on " +
                        "the edge between the two faces.");

                var handler = model.SelectionManager as ISelectionMgr;
                if (handler == null)
                    return Failed("I cannot use the selection manager. I touched nothing.");
                // SET 1 MARK 2, SET 2 MARK 4 (see the constants).
                if (!Select(handler, face1, SelectionMarks.FaceFilletFaceSet1))
                    return Failed("I could not select the face «" + uid1 + "». I touched nothing: " +
                                   "read the state again and point to the two faces again.");
                if (!Select(handler, face2, SelectionMarks.FaceFilletFaceSet2))
                    return Failed("I could not select the face «" + uid2 + "». I touched nothing: " +
                                   "read the state again and point to the two faces again.");

                // The circular profile: the guide asks for it to «complete the type»
                // (measured: without it, the fillet is born all the same; it is put in because the
                // recipe says so and it costs nothing).
                data.ConicTypeForCrossSectionProfile =
                    (int)swFeatureFilletProfileType_e.swFeatureFilletCircular;
                data.DefaultRadius = Units.M(radius);

                feature = model.FeatureManager.CreateFeature(data) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("The face fillet did not succeed: " + ex.Message +
                               ". Try «fillet_edges» on the edge between the two faces.");
            }
            finally { try { model.ClearSelection2(true); } catch { } }

            if (feature == null)
            {
                int now = CountFeature(model);
                string queue = now > featureBefore
                    ? " And yet the tree grew (" + featureBefore + " -> " + now + "): look at it " +
                      "and withdraw what is extra — " + Sentences.HowToWithdrawIt + "."
                    : " I touched nothing.";
                return Failed(
                    "The face fillet was not created, and SOLIDWORKS does not say why. " +
                    "Usually it is the radius: the ball must rest on both faces without " +
                    "leaving them. Try a smaller radius; if the two faces touch along " +
                    "an edge, the same fillet is made with «fillet_edges» on that edge." + queue);
            }

            // ── AND NOW THE PART IS MEASURED ─────────────────────────────
            //
            // ForceRebuild3 and not the simple rebuild: it is the one of
            // `edit_extrusion`, whose answer `WhatBroke` already knows how to
            // read on a sound part (case blind_becomes_through).
            bool rebuilt = false;
            try { rebuilt = model.ForceRebuild3(false); } catch { }

            string itemName = "";
            string typ = "";
            try { itemName = feature.Name; } catch { }
            try { typ = feature.GetTypeName2(); } catch { }

            // THE NEW FEATURE BY TYPE: the probe saw it born «Fillet».
            if (!string.Equals(typ, "Fillet", StringComparison.Ordinal))
                return Failed(
                    "A feature «" + itemName + "» of type «" + typ + "» was created, not a " +
                    "fillet («Fillet»): it is not what I asked for." +
                    RemoveFaceFillet(model, feature, photo, featureBefore) +
                    " Try «fillet_edges» on the edge between the two faces.");

            string broken = WhatBroke(model, photo, rebuilt, false);
            if (broken != null)
                return Failed(
                    "The face fillet «" + itemName + "» was created, but " + broken + "." +
                    RemoveFaceFillet(model, feature, photo, featureBefore) +
                    " Try a smaller radius, or «fillet_edges» on the edge between the two faces.");

            double? volumeAfter = VolumeMm3(model);
            double? cambio = volumeAfter - photo.Volume;

            // A FILLET THAT DOES NOT CHANGE THE VOLUME HAS DONE NOTHING. It removes
            // material on an outside edge, adds some in an inside corner:
            // zero means neither of the two. It is said only of a volume read back
            // (entry Z-C8).
            if (cambio.HasValue && Math.Abs(cambio.Value) < 0.01)
                return Failed(
                    "The face fillet «" + itemName + "» was created but the volume stayed " +
                    photo.Volume.Value.ToString("0.##", CultureInfo.InvariantCulture) + " mm3: it " +
                    "rounded nothing." + RemoveFaceFillet(model, feature, photo, featureBefore) +
                    " Try «fillet_edges» on the edge between the two faces.");

            // THE HAND CALCULATION, COMPARED. Where there is one (see above), a fillet
            // that removes another quantity is not the one asked for: partial, or
            // resting elsewhere. The tolerance allowed is half a tenth, or one
            // ten-thousandth of the calculation if that is larger: the number measured
            // by the probe (214.6) and the calculation (214.602) agree to the hundredth.
            if (expected.HasValue && cambio.HasValue &&
                Math.Abs(Math.Abs(cambio.Value) - expected.Value) > Math.Max(0.05, expected.Value * 1e-4))
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "The face fillet «{0}» was created, but it changed the volume by {1:0.##} mm3 and the " +
                    "hand calculation wants {2:0.##} (common edge {3:0.##} mm x r² x (1 − π/4)): it did " +
                    "not end up laid down on the two faces as requested.{4} For the same fillet on the " +
                    "edge between the two faces use «fillet_edges».",
                    itemName, Math.Abs(cambio.Value), expected.Value, edge,
                    RemoveFaceFillet(model, feature, photo, featureBefore)));

            // THE RADIUS THAT THE FEATURE DECLARES (ISimpleFilletFeatureData2~
            // DefaultRadius.html: «Default radius of the symmetric fillet»),
            // read back with IFeature::GetDefinition — not the repeated argument.
            // It decides nothing: the volume decides. Null if it cannot be read back.
            object declaredRadius = null;
            try
            {
                var read = feature.GetDefinition() as ISimpleFilletFeatureData2;
                if (read != null) declaredRadius = Math.Round(Units.Mm(read.DefaultRadius), 3);
            }
            catch { }

            int bodiesAfter = CountSolidBodies(model);
            int inErrorAfter = FeaturesInError(model);

            var measurements = new Dictionary<string, object>
            {
                // THE NEW FIELDS ARE IN ENGLISH (language decision, W3.1).
                // No repeated arguments as if they were measurements: the radius is
                // what the feature DECLARES, the rest is read from the part.
                ["declared_radius_mm"] = declaredRadius,
                ["feature_type"] = typ,
                ["volume_before_mm3"] = NumberOrNull(photo.Volume, 2),
                ["volume_after_mm3"] = NumberOrNull(volumeAfter, 2),
                ["volume_change_mm3"] = NumberOrNull(cambio, 2),
                ["material_removed"] = cambio.HasValue ? (object)(cambio.Value < 0) : null,
                ["bodies"] = NumberOrNull(bodiesAfter),
                ["features_in_error"] = NumberOrNull(inErrorAfter),
                ["shared_edge_mm"] = edge >= 0 ? (object)Math.Round(edge, 2) : null,
                ["expected_change_abs_mm3"] = NumberOrNull(expected, 2),
                ["how_expected_is_counted"] =
                    "shared edge length x r^2 x (1 - pi/4): exact for two flat faces at 90 degrees " +
                    "that touch along an edge (material removed on an outside edge, added in an " +
                    "inside corner), read BEFORE the fillet (afterwards the faces no longer touch) " +
                    "and compared with the volume change. Null when the two faces are not like that: " +
                    "no number is made up.",
            };
            if (!cambio.HasValue) measurements["volume_not_read_again"] = NoteVolumeNotReread;

            _log("[agent] face fillet r=" + radius.ToString("0.##", CultureInfo.InvariantCulture) +
                 " between " + uid1 + " and " + uid2 + ": «" + itemName + "», volume " + ReadValue(photo.Volume) +
                 " -> " + ReadValue(volumeAfter));
            return Succeeded(feature, measurements);
        }

        /// <summary>
        /// The refusal sentence when the two faces are planar and parallel and the
        /// fillet is impossible by geometry; null if it can be tried (faces
        /// not planar, not parallel, or two walls facing each other at distance 2r).
        /// </summary>
        private static string ParallelPlanarFaces(IFace2 f1, IFace2 f2, string uid1, string uid2, double radiusMm)
        {
            var n1 = FaceNormal(f1);
            var n2 = FaceNormal(f2);
            if (n1 == null || n2 == null || n1.Length < 3 || n2.Length < 3) return null;
            double cosine = n1[0] * n2[0] + n1[1] * n2[1] + n1[2] * n2[2];
            if (Math.Abs(cosine) < FaceFilletAlmostParallel) return null;

            if (cosine > 0)
                return "«" + uid1 + "» and «" + uid2 + "» are planar, parallel and face the same way " +
                       "(coplanar, or separated by a step): no ball touches both of them from the air " +
                       "side, and the fillet cannot be created. If there is a step between them, use " +
                       "«fillet_edges» on the edges of the step.";

            // Opposite normals: do they face each other or turn their backs? The point of the
            // plane of the second, seen from the normal of the first.
            var p1 = PointOnFacePlane(f1);
            var p2 = PointOnFacePlane(f2);
            if (p1 == null || p2 == null) return null;   // cannot be read: SOLIDWORKS decides, and the guards afterwards
            double distanceMm = Units.Mm(
                (p2[0] - p1[0]) * n1[0] + (p2[1] - p1[1]) * n1[1] + (p2[2] - p1[2]) * n1[2]);
            if (distanceMm <= 0)
                return "«" + uid1 + "» and «" + uid2 + "» are planar, parallel and face away from each other " +
                       "(like the front and the back of a block): no ball touches both of them from " +
                       "the air side, and the fillet cannot be created. To round the edges of one of " +
                       "the two use «fillet_edges».";
            if (Math.Abs(distanceMm - 2 * radiusMm) > 0.01)
                return string.Format(CultureInfo.InvariantCulture,
                    "«{0}» and «{1}» are planar, parallel and face each other at {2:0.##} mm: a " +
                    "constant-radius fillet touches both of them only if the radius is half of the distance, " +
                    "{3:0.##} mm, and you asked for {4:0.##}. If you want to round the bottom between " +
                    "the two, try «radius_mm»: {3:0.##}.",
                    uid1, uid2, distanceMm, distanceMm / 2, radiusMm);
            return null;
        }

        /// <summary>
        /// A point of the plane of the face, in metres, from `ISurface.PlaneParams`
        /// (the six numbers: normal and point, as in `FaceAxes`); null if the
        /// face is not planar or cannot be read.
        /// </summary>
        private static double[] PointOnFacePlane(IFace2 face)
        {
            try
            {
                var surface = face?.IGetSurface();
                if (surface == null || !surface.IsPlane()) return null;
                var plane = surface.PlaneParams as double[];
                if (plane == null || plane.Length < 6) return null;
                return new[] { plane[3], plane[4], plane[5] };
            }
            catch { return null; }
        }

        /// <summary>
        /// Removes the fillet just born when a guard rejects it, and says
        /// how it went: the tail of the refusal message. Same route as
        /// `RemoveCut` (AnchorOperations.cs): DeleteSelection2 with
        /// swDelete_Absorbed, which asks nobody for confirmation. «It said
        /// it removed it» is not enough: the features are counted again and the
        /// volume is measured again.
        /// </summary>
        private string RemoveFaceFillet(
            IModelDoc2 model, IFeature fillet, PartPhoto before, int featureBefore)
        {
            bool removed = false;
            try
            {
                model.ClearSelection2(true);
                if (fillet.Select2(false, 0))
                    removed = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch { }
            finally { try { model.ClearSelection2(true); } catch { } }
            if (!removed)
                return " And I could not remove it: it is still in the part, withdraw it — " + Sentences.HowToWithdrawIt + ".";
            try { model.ForceRebuild3(false); } catch { }
            int features = CountFeature(model);
            if (features < 0)
                return " I removed it, but I could not count the tree again to check that the part is as " +
                       "it was: look at it.";
            if (features > featureBefore)
                return " I tried to remove it, but the tree still has " + (features - featureBefore) +
                       " features more: withdraw them — " + Sentences.HowToWithdrawIt + ".";
            double? now = VolumeMm3(model);
            if (now == null || before.Volume == null)
                return " I removed it, but I could not measure the volume again to check that the part is as " +
                       "it was: look at it.";
            if (Math.Abs(now.Value - before.Volume.Value) > 0.01)
                return " I removed it, but the volume is now " +
                       now.Value.ToString("0.##", CultureInfo.InvariantCulture) + " mm3 instead of " +
                       before.Volume.Value.ToString("0.##", CultureInfo.InvariantCulture) +
                       ": look at the part before going on.";
            return " I did not leave it: I removed it and the part is as it was before this step.";
        }
    }
}
