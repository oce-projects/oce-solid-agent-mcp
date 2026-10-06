using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// CHANGING THE DEFINITION OF A FEATURE ALREADY MADE (entry Z-F4), for
    /// the extrusion and the extruded cut: a blind that must become through,
    /// a plate that must grow. Before, the only route was to remake the part.
    ///
    /// Measured on 27/9/2026 (`prova0089 zf4`, plate 120x80x10, blind hole
    /// Ø8 x 5 = 95748.67 mm³): `IFeature.GetDefinition()` as
    /// `IExtrudeFeatureData2`, `AccessSelections`, `SetEndCondition` /
    /// `SetDepth`, `ModifyDefinition`:
    ///
    ///  * through (`swEndCondThroughAll`) → 95497.35 (= 96000 − π·16·10);
    ///  * blind again 7 deep → 95648.14 (= 96000 − π·16·7);
    ///  * blind 15 on a plate of 10 → no error, in fact through;
    ///  * the base extrusion 10 → 12, and the hole on the top face follows it.
    ///
    /// Everything is read back: the condition and the depth from the definition,
    /// the volume from the part, the features in error. If the change breaks one
    /// downstream, the previous definition is put back and it refuses.
    /// </summary>
    public partial class SwOperations
    {
        private static string EndName(int c)
        {
            switch (c)
            {
                case (int)swEndConditions_e.swEndCondBlind: return "blind";
                case (int)swEndConditions_e.swEndCondThroughAll: return "through_all";
                case (int)swEndConditions_e.swEndCondThroughNext: return "up to the next";
                case (int)swEndConditions_e.swEndCondMidPlane: return "mid plane";
                case (int)swEndConditions_e.swEndCondUpToSurface: return "up to the surface";
                case (int)swEndConditions_e.swEndCondOffsetFromSurface: return "at a distance from the surface";
                case (int)swEndConditions_e.swEndCondUpToVertex: return "up to the vertex";
                case (int)swEndConditions_e.swEndCondUpToBody: return "up to the body";
                case (int)swEndConditions_e.swEndCondThroughAllBoth: return "through in both directions";
                default: return "condition " + c;
            }
        }

        /// <summary>
        /// Writes the end condition and the depth into the definition and
        /// applies it: true when ModifyDefinition answers true.
        /// <paramref name="refusedBy"/> names the step that said no when it is
        /// false.
        ///
        /// THE ACCESS IS READ (Z-C21.8, finding L5-5).
        /// IExtrudeFeatureData2~AccessSelections.html: «True if the selections
        /// were accessed successfully, false if not»; its Remarks: it «puts the
        /// model into a rollback state», which ModifyDefinition or
        /// ReleaseSelectionAccess restore. The answer was dropped, and a
        /// refused access went on to write and apply the definition. Now a
        /// refused access stops here, before anything is written, and nothing
        /// is released because nothing was accessed.
        /// </summary>
        private static bool WriteExtrusion(IModelDoc2 model, IFeature f, int fine, double depthM, out string refusedBy)
        {
            refusedBy = null;
            var data = f.GetDefinition() as IExtrudeFeatureData2;
            if (data == null) { refusedBy = "GetDefinition gave no extrusion definition"; return false; }
            bool access;
            try { access = data.AccessSelections(model, null); }
            catch (Exception ex) { refusedBy = "AccessSelections threw: " + ex.Message; return false; }
            if (!access) { refusedBy = "AccessSelections answered false"; return false; }
            bool done = false;
            try
            {
                data.SetEndCondition(true, fine);
                // THE MID-PLANE HAS ITS DEPTH TOO (review of 27/9/2026):
                // without it, the restore put the condition back and left the new measure.
                if (fine == (int)swEndConditions_e.swEndCondBlind || fine == (int)swEndConditions_e.swEndCondMidPlane)
                    data.SetDepth(true, depthM);
                // S11: ModifyDefinition always with GetErrorCode2 (ApiHelpers).
                // The verdict on errors stays with the caller, which rebuilds
                // and counts the features in error (FeaturesInError).
                done = ApiHelpers.ModifyAndVerify(f, data, model).Applied;
                if (!done) refusedBy = "ModifyDefinition answered false";
            }
            catch (Exception ex) { done = false; refusedBy = "writing the definition threw: " + ex.Message; }
            if (!done) { try { data.ReleaseSelectionAccess(); } catch { } }
            return done;
        }

        /// <summary>
        /// The tail of the refusal when the new definition was not applied: the
        /// feature and the part READ BACK, never assumed (Z-C21.8, finding M42).
        /// IFeature~ModifyDefinition.html, Remarks: «this method may leave the
        /// feature in an unpredictable state due to errors». Until library
        /// 0.144.0 the refusal said that nothing had been touched, without
        /// looking.
        /// </summary>
        private static string ExtrusionReadBack(IModelDoc2 model, IFeature f, int endBefore, double depthBeforeM, double? volumeBefore)
        {
            int endNow = -1;
            double depthNowM = double.NaN;
            try
            {
                var now = f.GetDefinition() as IExtrudeFeatureData2;
                if (now != null)
                {
                    endNow = now.GetEndCondition(true);
                    depthNowM = now.GetDepth(true);
                }
            }
            catch { endNow = -1; }
            return ExtrusionReadBackTail(endBefore, Units.Mm(depthBeforeM), volumeBefore,
                                         endNow, Units.Mm(depthNowM), VolumeMm3(model));
        }

        /// <summary>
        /// The words of <see cref="ExtrusionReadBack"/>, kept apart from the
        /// document so that Oce.Solid.Agent.ContractTests can call it without SOLIDWORKS.
        /// «Nothing changed» only when the end condition, the depth (for the
        /// conditions that have one) and the volume read now are the ones read
        /// before; -1 for an end condition and null for a volume mean «not
        /// read», and then the tail says so instead of guessing.
        /// </summary>
        internal static string ExtrusionReadBackTail(
            int endBefore, double depthBeforeMm, double? volumeBefore,
            int endNow, double depthNowMm, double? volumeNow)
        {
            if (endNow < 0 || volumeNow == null || volumeBefore == null)
                return " I could not read it back (end condition " + (endNow < 0 ? "not read" : "«" + EndName(endNow) + "»") +
                       ", volume " + (volumeNow == null ? "not read" : volumeNow.GetValueOrDefault().ToString("0.##", CultureInfo.InvariantCulture) + " mm3") +
                       "): look at the feature before going on.";
            bool withDepth = endBefore == (int)swEndConditions_e.swEndCondBlind ||
                             endBefore == (int)swEndConditions_e.swEndCondMidPlane;
            bool sameEnd = endNow == endBefore;
            bool sameDepth = !withDepth || (!double.IsNaN(depthNowMm) && Math.Abs(depthNowMm - depthBeforeMm) <= 0.001);
            bool sameVolume = Math.Abs(volumeNow.GetValueOrDefault() - volumeBefore.GetValueOrDefault()) < 0.01;
            string volumeText = volumeNow.GetValueOrDefault().ToString("0.##", CultureInfo.InvariantCulture) + " mm3";
            if (sameEnd && sameDepth && sameVolume)
                return " Read back: still «" + EndName(endNow) + "»" +
                       (withDepth ? ", " + depthNowMm.ToString("0.###", CultureInfo.InvariantCulture) + " mm deep" : "") +
                       ", and the volume is the one before (" + volumeText + "): nothing changed.";
            bool depthNowShown = endNow == (int)swEndConditions_e.swEndCondBlind ||
                                 endNow == (int)swEndConditions_e.swEndCondMidPlane;
            return " Read back, it is now «" + EndName(endNow) + "»" +
                   (depthNowShown && !double.IsNaN(depthNowMm) ? ", " + depthNowMm.ToString("0.###", CultureInfo.InvariantCulture) + " mm deep" : "") +
                   ", with a volume of " + volumeText + " instead of " +
                   volumeBefore.GetValueOrDefault().ToString("0.##", CultureInfo.InvariantCulture) + " mm3: the feature is not as it was. " +
                   "Look at it, and undo the step (" + Sentences.HowToWithdrawIt + ").";
        }

        private Outcome EditExtrusion(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (!(model is IPartDoc))
                return Failed("I change the features of a part. Open the part for me.");
            string uid = (Str(a, "feature_uid", "") ?? "").Trim();
            if (uid.Length == 0)
                return Failed("Tell me «feature_uid»: the feature to change, with the name from the state («feature»).");
            bool asksThroughAll = a != null && a.ContainsKey("through_all") && a["through_all"] is bool && (bool)a["through_all"];
            bool asksDepth = a != null && a.ContainsKey("depth_mm");
            if (asksThroughAll == asksDepth)
                return Failed("Tell me EITHER «through_all»: true (a cut that goes through everything) OR «depth_mm» " +
                               "(how deep it goes, in mm), one of the two.");
            double depth = double.NaN; string error;
            if (asksDepth)
            {
                if (!RequiredNum(a, "depth_mm", out depth, out error)) return Failed(error);
                if (depth <= 0) return Failed("«depth_mm» must be positive.");
            }
            string reason;
            if (!FeatureIsThatOne(model, uid, a, out reason)) return Failed(reason);
            var f = FindFeature(model, uid);
            if (f == null)
            {
                string ci = FeaturesThatExist(model);
                return Failed("I cannot find «" + uid + "» in this document." + (ci.Length > 0 ? " There are: " + ci + "." : ""));
            }
            string typ = "";
            try { typ = f.GetTypeName2(); } catch { }
            // «ICE» DOES NOT MEAN CUT (entry Z-C21.2, finding M7).
            //
            // IFeature~GetTypeName2.html: «To get the underlying type of feature
            // of an Instant3D feature (i.e., "ICE"), call IFeature::GetTypeName».
            // «ICE» is an extrusion made on a face, and it can be a cut
            // (cylindrical_cut, rectangular_cut) or a BOSS
            // (extrude_on_face): DocumentState.cs has measured it since 10/9/2026. Here
            // «ICE» meant «cut» and nothing else: a boss became «cut», and
            // «through_all» on a boss was not refused; a real «Cut» was
            // refused instead. For the «ICE», cut or boss is told by the definition:
            // IExtrudeFeatureData2~IsBossFeature.html, «True if this extrusion is
            // a boss feature, false if not».
            //
            // ONLY FOR THE «ICE». The first draft asked IsBossFeature of all of them, and
            // the base plate (the FIRST extrusion of the part, «Extrusion»)
            // answered false: «through_all» on a plate passed the check and the
            // plate became «through all» (blind_becomes_through,
            // step 6, red in the whole bench of 28/9/2026 on 0.138.0). The page
            // has no Remarks that say it. MEASURED on 28/9/2026 (probe
            // Checks/ExtrusionType, SOLIDWORKS 29.5.1), before and after
            // AccessSelections, same answers:
            //   base (first extrusion)   GetTypeName2 Extrusion  GetTypeName Extrusion  IsBossFeature false
            //   boss (FeatureExtrusion3) GetTypeName2 ICE        GetTypeName Boss       IsBossFeature true
            //   cut (FeatureCut4)        GetTypeName2 ICE        GetTypeName Cut        IsBossFeature false
            // The extrusions made from a sketch on a plane, after the base,
            // are «ICE» for GetTypeName2 too: «Extrusion» is only the base, which is not
            // a «boss» and is not a cut. Where the type is already «Cut» or
            // «Extrusion» the type decides, as before Z-C21.2.
            if (typ != "Extrusion" && typ != "Cut" && typ != "ICE")
                return Failed("«" + f.Name + "» is a feature of type «" + typ + "»: I can change extrusions and extruded " +
                               "cuts. For any dimension there is «set_dimension».");
            var before = f.GetDefinition() as IExtrudeFeatureData2;
            if (before == null) return Failed("I cannot read the definition of «" + f.Name + "».");
            bool cut = typ == "Cut";
            if (typ == "ICE")
            {
                try { cut = !before.IsBossFeature(); }
                catch (Exception ex)
                {
                    // «Nothing touched» needs no reread here (Z-C21.8, finding
                    // M42): up to this line the feature has only been read
                    // (GetDefinition, IsBossFeature), never accessed or modified.
                    return Failed("I cannot tell whether «" + f.Name + "» is a cut or a boss (" + ex.Message +
                                   "): I do not touch it. I touched nothing.");
                }
            }
            if (asksThroughAll && !cut)
                return Failed("«through_all» applies to a cut: an extrusion that goes «through all» has nothing to " +
                               "go through. To make it grow tell me «depth_mm».");
            int endBefore = before.GetEndCondition(true);
            double depthBefore = before.GetDepth(true);

            // TWO INDEPENDENT DIRECTIONS: IT REFUSES (entry Z-C21.2, finding L5-1).
            //
            // IExtrudeFeatureData2~BothDirections.html: «Gets or sets whether the
            // extrusion is in both directions». Here only direction 1 is written and
            // read back (Forward = true in SetEndCondition/SetDepth: «True for
            // forward direction, false for reverse»). On a two-direction extrusion
            // a «through_all» changed direction 1, the reread of direction 1 matched,
            // and the answer said «succeeded» with direction 2 left blind. The
            // bridge never creates two-direction extrusions (Sd: true), so they come
            // only from parts made elsewhere: refusing costs little and takes away nothing
            // that really worked. The mid-plane is NOT «two directions» (it is a
            // condition of direction 1) and stays allowed.
            bool twoDirections = false;
            try { twoDirections = before.BothDirections; } catch { }
            if (twoDirections && endBefore != (int)swEndConditions_e.swEndCondMidPlane &&
                endBefore != (int)swEndConditions_e.swEndCondThroughAllBoth)
            {
                int fine2 = -1; double prof2 = double.NaN;
                try { fine2 = before.GetEndCondition(false); prof2 = before.GetDepth(false); } catch { }
                bool conProf2 = fine2 == (int)swEndConditions_e.swEndCondBlind;
                return Failed("«" + f.Name + "» goes in two independent directions: the first «" + EndName(endBefore) +
                               "», the second «" + EndName(fine2) + "»" +
                               (conProf2 ? " deep " + Units.Mm(prof2).ToString("0.###") + " mm" : "") +
                               ". I change and read back only the first, and I would tell you «done» with the second as " +
                               "it was: I do not touch it. Change it by hand, or tell me a dimension to change " +
                               "with «set_dimension». I touched nothing.");
            }
            // THE CONDITION THAT IS ASKED FOR DEPENDS ON THE ONE THAT IS THERE (adversarial
            // review of 27/9/2026). A symmetric plate of 100 (from -50 to
            // +50) taken to 120 became blind from 0 to +120: it did not grow by 10 on
            // each side, it moved by 60, and the holes on the faces followed it to different
            // places — and the answer said «succeeded». The mid-plane stays a mid-plane;
            // «through_all» on a mid-plane goes through in both directions.
            // The conditions with a REFERENCE (up to a surface, to a vertex,
            // to a body) are not touched: changing them loses the reference, and if
            // something breaks I would not know how to put it back.
            bool withReference = endBefore == (int)swEndConditions_e.swEndCondUpToSurface ||
                                  endBefore == (int)swEndConditions_e.swEndCondOffsetFromSurface ||
                                  endBefore == (int)swEndConditions_e.swEndCondUpToVertex ||
                                  endBefore == (int)swEndConditions_e.swEndCondUpToBody ||
                                  endBefore == (int)swEndConditions_e.swEndCondUpToSelection ||
                                  endBefore == (int)swEndConditions_e.swEndCondUpToNext;
            if (withReference)
                return Failed("«" + f.Name + "» ends «" + EndName(endBefore) + "»: changing it would lose the " +
                               "reference, and if something broke I would not know how to put it back. Change it by hand, or " +
                               "tell me a dimension to change with «set_dimension».");
            bool symmetric = endBefore == (int)swEndConditions_e.swEndCondMidPlane ||
                              endBefore == (int)swEndConditions_e.swEndCondThroughAllBoth;
            int requestedEnd = asksThroughAll
                ? (symmetric ? (int)swEndConditions_e.swEndCondThroughAllBoth : (int)swEndConditions_e.swEndCondThroughAll)
                : (symmetric ? (int)swEndConditions_e.swEndCondMidPlane : (int)swEndConditions_e.swEndCondBlind);
            // THE GUARDS OF set_dimension: bodies, openings, chamfers, errors.
            var photo = SnapshotPart(model);
            double? volumeBefore = photo.Volume;
            string refusedBy;
            if (!WriteExtrusion(model, f, requestedEnd, asksDepth ? Units.M(depth) : depthBefore, out refusedBy))
            {
                try { model.ForceRebuild3(false); } catch { }
                return Failed("SOLIDWORKS did not take the new definition of «" + f.Name + "» (" + refusedBy + ")." +
                               ExtrusionReadBack(model, f, endBefore, depthBefore, volumeBefore));
            }
            bool rebuilt = false;
            try { rebuilt = model.ForceRebuild3(false); } catch { }
            int errorsAfter = FeaturesInError(model);
            // Without the openings: only the depth changes, and a through that goes back to
            // blind makes them drop, rightly (see WhatBroke).
            string broken = WhatBroke(model, photo, rebuilt, false);
            if (broken != null)
            {
                bool restored = WriteExtrusion(model, f, endBefore, depthBefore, out _);
                try { model.ForceRebuild3(false); } catch { }
                return Failed("With the new definition of «" + f.Name + "» " + broken + "." +
                               IsBackAsItWas(model, photo, restored, "the previous definition (" + EndName(endBefore) + ")"));
            }
            var after = f.GetDefinition() as IExtrudeFeatureData2;
            int endAfter = after == null ? -1 : after.GetEndCondition(true);
            double depthAfter = after == null ? double.NaN : after.GetDepth(true);
            bool withDepth = endAfter == (int)swEndConditions_e.swEndCondBlind || endAfter == (int)swEndConditions_e.swEndCondMidPlane;
            if (endAfter != requestedEnd || (asksDepth && Math.Abs(Units.Mm(depthAfter) - depth) > 0.001))
                return Failed("SOLIDWORKS says it changed «" + f.Name + "», but reading it back it is " + EndName(endAfter) +
                               (withDepth ? " deep " + Units.Mm(depthAfter).ToString("0.###") + " mm" : "") +
                               ": it is not what you asked for.");
            double? volumeAfter = VolumeMm3(model);
            var measurements = new Dictionary<string, object>
            {
                ["feature"] = f.Name,
                ["kind"] = cut ? "cut" : "extrusion",
                ["end_before"] = EndName(endBefore),
                ["depth_before_mm"] = endBefore == (int)swEndConditions_e.swEndCondBlind || endBefore == (int)swEndConditions_e.swEndCondMidPlane
                    ? (object)Math.Round(Units.Mm(depthBefore), 4) : null,
                ["end_after"] = EndName(endAfter),
                ["depth_after_mm"] = withDepth ? (object)Math.Round(Units.Mm(depthAfter), 4) : null,
                ["volume_before_mm3"] = NumberOrNull(volumeBefore, 2),
                ["volume_after_mm3"] = NumberOrNull(volumeAfter, 2),
                ["features_in_error"] = errorsAfter >= 0 ? (object)errorsAfter : null,
            };
            if (volumeBefore != null && volumeAfter != null && Math.Abs(volumeBefore.Value - volumeAfter.Value) < 0.01)
                measurements["warning"] = "The definition changed but the volume did not: the new depth does not change the " +
                                       "part (a blind cut deeper than the material is already through, for example).";
            _log("[agent] " + f.Name + ": " + EndName(endBefore) + " -> " + EndName(endAfter));
            return SucceededWithoutCreating(measurements);
        }
    }
}
