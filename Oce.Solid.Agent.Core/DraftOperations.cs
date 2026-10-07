using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE DRAFT. (entry GP15)
    ///
    /// The draft angle: the walls of a part moulded in a mould must
    /// lean by a degree or two, or the part does not come out of the form. It is also needed
    /// for castings and for machining with a draft angle.
    ///
    /// ── FOUR FAMILIES, AND TWO HAVE THE IDENTICAL SIGNATURE ───────────
    ///
    ///   IFeatureManager.InsertMultiFaceDraft(Angle, FlipDir, EdgeDraft,
    ///                          PropType, IsStepDraft, IsBodyDraft) -> Feature
    ///   IFeatureManager.DraftXpertChange    (Angle, FlipDir, EdgeDraft,
    ///                          PropType, IsStepDraft, IsBodyDraft) -> Feature
    ///   IModelDoc2.InsertMfDraft (Angle, FlipDir, IsEdgeDraft, PropType)
    ///                                                             -> void
    ///   IModelDoc2.InsertMfDraft2(... , StepDraft)                 -> void
    ///
    /// The first two **cannot be told apart by reading**: same parameter names,
    /// same types, same return type. They are told apart only by measuring, and
    /// measured they answer differently: with the very same selections and
    /// the very same arguments, `InsertMultiFaceDraft` makes the draft and
    /// **`DraftXpertChange` returns null**. The last two return `void` and cannot
    /// even be checked.
    ///
    /// `InsertMultiFaceDraft` is used.
    ///
    /// ── THE MARKS, AND THE DEFECT INSIDE THEM ─────────────────────────
    ///
    /// **Neutral face mark 1, faces to draft mark 2.** Measured on
    /// 16/9/2026 on a 100x100x20 block, draft of 5 degrees:
    ///
    ///   neutral=1, faces=2  ->  «Draft1», sides at 5.00 degrees,
    ///                           volume 200000 -> 207080.7
    ///   neutral=2, faces=1  ->  «Draft1», sides at 0.00 degrees,
    ///                           volume 200000 -> 243744.3
    ///
    /// **The second row is the defect.** Swapping the marks the feature is born
    /// all the same, is called the same, gives no error — and has drafted
    /// other faces, by another amount. Whoever does not look at the part from the side does not
    /// notice.
    ///
    /// ── AND THE ANGLE OF THE FEATURE IS NOT A MEASURE ─────────────────
    ///
    /// In both cases `IDraftFeatureData.Angle` reads back **5 degrees**:
    /// that is, what was asked, not what happened. Even in the
    /// case in which the faces stayed at zero.
    ///
    /// So here the angle is measured **on the faces of the part**, counting
    /// how many are at the inclination asked for with respect to the neutral one, before and
    /// after. It is the only number that comes out different in the wrong case.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>How much is tolerated on the angle read from the faces, in degrees.</summary>
        private const double DraftToleranceDeg = 0.05;

        private Outcome Draft(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double angle = Num(a, "angle_deg", 0);
            if (angle <= 0)
                return Failed(
                    "«angle_deg» is missing, or it is zero: a draft of zero degrees is not " +
                    "a draft, it is a straight wall.");
            if (angle >= 90)
                return Failed(
                    "«angle_deg» must be under 90: at 90 degrees the wall lies " +
                    "down on the neutral face. For a mold, use an angle of 1-3 degrees.");

            string neutralUid = Str(a, "neutral_face_uid", "");
            var neutral = ResolveFace(docState, a, neutralUid, out string reason);
            if (neutral == null) return Failed("Neutral face: " + reason);

            var uid = StringList(a, "faces_uid");
            if (uid.Count == 0)
                return Failed(
                    "«faces_uid» is missing: which faces to draft. The NEUTRAL face -- the one " +
                    "that stays still -- is given separately, in «neutral_face_uid».");
            if (uid.Contains(neutralUid, StringComparer.OrdinalIgnoreCase))
                return Failed(
                    "«" + neutralUid + "» is both the neutral face and one to draft. The " +
                    "neutral one is the one that does NOT move: it is the plane the angle is " +
                    "measured from.");

            var faces = new List<IFace2>();
            foreach (var u in uid)
            {
                var f = ResolveFace(docState, a, u, out reason);
                if (f == null) return Failed("Face «" + u + "»: " + reason);
                faces.Add(f);
            }

            var neutralNormal = FaceNormal(neutral);
            if (neutralNormal == null)
                return Failed("I cannot read the orientation of the neutral face.");

            bool flip = Bool(a, "flip", false);
            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            int inclinedBefore = HowManyInclinedFaces(model, neutralNormal, angle);
            if (bodiesBefore < 0 || inclinedBefore < 0)
                return Failed(VolumeNotReadBefore("making the draft").Replace("the volume of the part", "the faces of the part"));

            model.ClearSelection2(true);
            var handler = model.SelectionManager as ISelectionMgr;
            if (handler == null) return Failed("I cannot use the selection manager.");

            // NEUTRAL MARK 1, FACES MARK 2. Swapped, the feature is born
            // all the same and drafts the wrong faces: see the note at the head.
            if (!Select(handler, neutral, SelectionMarks.DraftNeutralFace))
                return Failed("I could not select the neutral face.");
            for (int i = 0; i < faces.Count; i++)
                if (!Select(handler, faces[i], SelectionMarks.DraftFaces))
                    return Failed("I could not select the face «" + uid[i] + "».");

            IFeature feature;
            try
            {
                feature = model.FeatureManager.InsertMultiFaceDraft(
                    Angle: angle * Math.PI / 180.0,
                    FlipDir: flip,
                    EdgeDraft: false,
                    PropType: (int)swDraftFacePropagationType_e.swFacePropNone,
                    IsStepDraft: false,
                    IsBodyDraft: false) as IFeature;
            }
            catch (Exception ex) { return Failed("The draft did not succeed: " + ex.Message); }

            if (feature == null)
                return Failed(
                    "The draft was not created, and SOLIDWORKS does not say why. " +
                    "Usually the angle is too big for the height of the wall, or a face " +
                    "does not touch the neutral one.");

            // ── AND NOW THE PART IS MEASURED, NOT THE FEATURE ────────────
            //
            // `IDraftFeatureData.Angle` reads back the angle ASKED for even when
            // nothing tilted. The only true measure is how many faces
            // of the part are at the angle asked for with respect to the neutral one.
            int inclinedAfter = HowManyInclinedFaces(model, neutralNormal, angle);
            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);
            int newOnes = inclinedAfter - inclinedBefore;
            if (inclinedAfter < 0)
                return Failed(
                    "The draft was created, but then I cannot count the tilted faces " +
                    "again: the feature declares the angle even when it tilted nothing, so " +
                    "without that count I cannot tell you that it worked. Look at the part — " +
                    Sentences.HowToWithdrawIt + ".");

            if (newOnes < faces.Count)
            {
                string howToWithdraw =
                    " Ritiralo — " + Sentences.HowToWithdrawIt + ".";
                return Failed(string.Format(
                    CultureInfo.InvariantCulture,
                    "I asked to draft {0} faces by {1:0.###} degrees and the feature was " +
                    "created, but on the part the faces at that angle went from {2} to " +
                    "{3}: {4}. The feature declares the angle I asked for even when it " +
                    "tilted nothing, so the only way to know is to measure the faces -- " +
                    "and that is what I did. Almost always it means that the neutral " +
                    "face is not the right one, or that the faces indicated do not " +
                    "touch it.{5}",
                    faces.Count, angle, inclinedBefore, inclinedAfter,
                    newOnes <= 0 ? "none of them was tilted" : "missing: " + (faces.Count - newOnes),
                    howToWithdraw));
            }

            if (bodiesAfter >= 0 && bodiesAfter != bodiesBefore)
                return Failed(
                    "The bodies went from " + bodiesBefore + " to " + bodiesAfter +
                    ": a draft must not split the part. Withdraw it — " +
                    Sentences.HowToWithdrawIt + ".");

            // The draft is not decided on the volume (the faces are counted): here the
            // volume is only reported, and if it cannot be read that is said (entry Z-C8).
            double? moved = volumeAfter - volumeBefore;
            var measurements = new Dictionary<string, object>
            {
                ["angle_deg"] = angle,
                ["faces_drafted"] = faces.Count,
                ["faces_at_corner_before"] = inclinedBefore,
                ["faces_at_corner_after"] = inclinedAfter,
                ["flip"] = flip,
                ["material_mm3"] = NumberOrNull(moved, 2),
                ["part_grew"] = moved.HasValue ? (object)(moved.Value > 0) : null,
                ["bodies"] = NumberOrNull(bodiesAfter),
                // THE ANGLE DECLARED BY THE FEATURE, next to the measured one
                // and with what it is written. It is not hidden: it is named.
                ["angle_declared_by_feature"] = FeatureAngle(feature),
                ["what_it_does_not_check"] =
                    "I counted the faces that are at the requested angle to the neutral one, I " +
                    "did NOT VERIFY ONE BY ONE that they are the very ones you indicated: " +
                    "after a draft the faces are renumbered. If the count matches and the " +
                    "volume went the right way, the part is the one. And I do not check " +
                    "whether the angle is enough for the mold you have in mind.",
            };

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agent] draft of {0:0.###} degrees on {1} faces: at the angle {2} -> {3}, " +
                "volume {4}", angle, faces.Count, inclinedBefore, inclinedAfter, ReadValue(moved)));

            return Succeeded(feature, measurements, neutralNormal);
        }

        // =================================================================

        private static bool Select(ISelectionMgr handler, IFace2 face, int mark)
        {
            try
            {
                var d = (SelectData)handler.CreateSelectData();
                d.Mark = mark;
                return (face as IEntity).Select4(true, d);
            }
            catch { return false; }
        }

        /// <summary>
        /// How many planar faces of the part are at the angle asked for with respect to the
        /// neutral face.
        ///
        /// A straight wall is perpendicular to the neutral one: its angle with
        /// the normal of the neutral one is 90 degrees. Drafted by `alpha`, it becomes
        /// `90 - alpha` or `90 + alpha` depending on the direction — and since the direction
        /// can be flipped, both are accepted and counted.
        ///
        /// Only PLANAR faces are counted: a cylindrical face that is drafted
        /// becomes conical and does not have a single inclination.
        ///
        /// **-1 if it could not be read** (entry Z-C8). ZERO-E-ZERO: a part
        /// with no solid bodies (`GetBodies2` null) has no tilted faces.
        /// </summary>
        private static int HowManyInclinedFaces(
            IModelDoc2 model, double[] neutralNormal, double angleDeg)
        {
            int howMany = 0;
            try
            {
                var bodies = (model as IPartDoc)
                    .GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (bodies == null) return 0;
                foreach (var oc in bodies)
                {
                    var body = oc as IBody2;
                    if (body == null) continue;
                    var f = body.GetFirstFace() as IFace2;
                    while (f != null)
                    {
                        var n = ApiHelpers.NormalIfPlanar(f);
                        if (n != null)
                        {
                            double cosine =
                                n[0] * neutralNormal[0] +
                                n[1] * neutralNormal[1] +
                                n[2] * neutralNormal[2];
                            if (cosine > 1) cosine = 1;
                            if (cosine < -1) cosine = -1;
                            double deg = Math.Acos(cosine) * 180.0 / Math.PI;
                            if (Math.Abs(deg - (90.0 - angleDeg)) < DraftToleranceDeg ||
                                Math.Abs(deg - (90.0 + angleDeg)) < DraftToleranceDeg)
                                howMany++;
                        }
                        f = f.GetNextFace() as IFace2;
                    }
                }
            }
            catch { return -1; }
            return howMany;
        }

        private static object FeatureAngle(IFeature feature)
        {
            try
            {
                var d = feature.GetDefinition() as IDraftFeatureData;
                if (d == null) return null;
                return Math.Round(d.Angle * 180.0 / Math.PI, 3);
            }
            catch { return null; }
        }
    }
}
