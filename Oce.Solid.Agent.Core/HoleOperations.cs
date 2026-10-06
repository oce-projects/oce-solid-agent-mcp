using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// The hole wizard: a hole that is a hole, not a round cut.
    ///
    /// WHY A CYLINDRICAL CUT IS NOT ENOUGH
    /// Until 0.29.1 «hole» meant a circular sketch and a
    /// FeatureCut4. Geometrically it looks alike; as a feature it is not the
    /// same thing, and seven pieces of information that someone downstream uses get lost:
    /// the hole callout in the drawing, the thread, the standard diameter,
    /// the cone of the tip at the bottom of a blind hole, the possibility of
    /// going from M8 to M10 by changing one entry, the recognition by
    /// CAM and quoting systems, and the mating of the fasteners in the assembly.
    /// The client asked for it by name: «all holes must preferably be
    /// made with the hole wizard function using all
    /// the settings it provides».
    ///
    /// THE RECIPE, MEASURED ON 9 SEPTEMBER 2026 ON SOLIDWORKS 2021 SP5
    /// Calling HoleWizard5 and nothing else DOES NOT WORK, and does not fail: it builds
    /// a hole of 50.8 mm — two inches — with the right name in the tree
    /// («Foro filettato M8»). Verified on the VOLUME REMOVED, not on the
    /// faces, which deceive.
    ///
    /// What works, in this exact order:
    ///
    ///  1. it is ALWAYS created through. HoleWizard5 already applies the standard
    ///     (after the call the definition says ISO, M8, thread 8) but leaves
    ///     ThruTapDrillDiameter at zero, and that zero is what produces the hole
    ///     of two inches;
    ///  2. IWizardHoleFeatureData2.ChangeStandard(norma, elemento, taglia)
    ///     fills in the missing fields;
    ///  3. Type (swWzdHoleTypes_e) is written. It is the field that really commands:
    ///     with swTapThru (33) an M10 still came out at two inches, with
    ///     swTapThruCosmeticThread (48) all the sizes from M3 to M24 remove
    ///     exactly the volume of the standard pilot hole;
    ///  4. ModifyDefinition;
    ///  5. if the hole is BLIND, a SECOND pass of ModifyDefinition. It is not
    ///     a whim: ChangeStandard fills in only the «Thru» variants of the
    ///     fields and leaves their blind twins at zero (HoleDiameter, HoleDepth,
    ///     TapDrillDepth), and a blind hole created in one go comes out
    ///     any which way. Creating through and then modifying, the volumes match
    ///     to the hundredth, tip cone included;
    ///  6. the positions are written in the positioning sketch INSIDE
    ///     EditSketch, with AddToDB on and inference off. Outside the
    ///     edit SetCoords returns False; with inference on the new
    ///     points snap to the first and the error of the first propagates to
    ///     all of them. With these precautions: error 0.0000 mm on four holes.
    ///
    /// Measured volumes this recipe produces (plate of 20, hole in the
    /// centre): M3 98.2, M4 171.1, M5 277.1, M6 392.7, M8 726.3, M10 1134.9,
    /// M12 1634.3, M14 2261.9, M16 3078.8, M20 4810.6, M24 6927.2 mm3 —
    /// that is pi*r^2*thickness on the ISO pilot hole, exact.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// The four kinds of hole the operation can make, with the type of
        /// ISO element to take the sizes from and the two variants of Type.
        ///
        /// The numbers are swWzdGeneralHoleTypes_e,
        /// swWzdHoleStandardFastenerTypes_e and swWzdHoleTypes_e, read by
        /// reflection on interop 29.5.1.1.
        /// </summary>
        private struct HoleKind
        {
            public int Generic;      // swWzdGeneralHoleTypes_e
            public int Element;      // swWzdHoleStandardFastenerTypes_e
            public int ThroughAllType;  // swWzdHoleTypes_e
            public int BlindType;     // swWzdHoleTypes_e
            public string Human;
        }

        private static readonly Dictionary<string, HoleKind> HoleKinds =
            new Dictionary<string, HoleKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["threaded"] = new HoleKind
            {
                Generic = (int)swWzdGeneralHoleTypes_e.swWzdTap,
                Element = (int)swWzdHoleStandardFastenerTypes_e.swStandardISOTappedHole,
                ThroughAllType = (int)swWzdHoleTypes_e.swTapThruCosmeticThread,
                BlindType = (int)swWzdHoleTypes_e.swTapBlindCosmeticThread,
                Human = "threaded",
            },
            ["clearance"] = new HoleKind
            {
                Generic = (int)swWzdGeneralHoleTypes_e.swWzdHole,
                Element = (int)swWzdHoleStandardFastenerTypes_e.swStandardISOScrewClearances,
                ThroughAllType = (int)swWzdHoleTypes_e.swHoleThru,
                BlindType = (int)swWzdHoleTypes_e.swHoleBlind,
                Human = "clearance for a screw",
            },
            ["counterbore"] = new HoleKind
            {
                Generic = (int)swWzdGeneralHoleTypes_e.swWzdCounterBore,
                Element = (int)swWzdHoleStandardFastenerTypes_e.swStandardISOSocketHeadCap,
                ThroughAllType = (int)swWzdHoleTypes_e.swCounterBoreThru,
                BlindType = (int)swWzdHoleTypes_e.swCounterBoreBlind,
                Human = "counterbore for a cylindrical-head screw ISO 4762",
            },
            ["countersink"] = new HoleKind
            {
                Generic = (int)swWzdGeneralHoleTypes_e.swWzdCounterSink,
                Element = (int)swWzdHoleStandardFastenerTypes_e.swStandardISOSocketCTSKFlatHead,
                ThroughAllType = (int)swWzdHoleTypes_e.swCounterSinkThru,
                BlindType = (int)swWzdHoleTypes_e.swCounterSinkBlind,
                Human = "countersink for a flat countersunk-head screw ISO 10642",
            },
        };

        private const int IsoStandard = (int)swWzdHoleStandards_e.swStandardISO;

        /// <summary>How many holes at most in a single feature.</summary>
        private const int MaximumHoles = 60;

        private Outcome HoleWizard(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var face = ResolveFace(docState, a, Str(a, "face_uid", ""), out string reason);
            if (face == null) return Failed(reason);

            double[] axes = FaceAxes(face, out double offsetM, out string error);
            if (axes == null) return Failed(error);

            string kindName = Str(a, "variant", "clearance");
            HoleKind kind;
            if (!HoleKinds.TryGetValue(kindName, out kind))
                return Failed(
                    "I do not know the hole type «" + kindName + "». " +
                    "I can do: threaded, clearance, counterbore, countersink.");

            string size = (Str(a, "size", "") ?? "").Trim();
            if (size.Length == 0)
                return Failed("The size of the hole is missing (for example «M8»).");

            bool throughAll = Bool(a, "through_all", true);
            double depth = Num(a, "depth_mm", 0);
            if (!throughAll && depth <= 0)
                return Failed(
                    "A blind hole needs the depth. Tell me the depth, or say that it " +
                    "is through.");

            var positions = HolePositions(a, out error);
            if (positions == null) return Failed(error);

            // THE ANCHOR (entry E18): one per position, ALL OR NONE — a
            // hole without an anchor would leave the sketch underconstrained, and the
            // sketch is a single one for all of them. It is read BEFORE creating anything.
            List<Anchoring> anchors;
            if (!ReadHoleAnchors(a, docState, face, axes, offsetM, positions, out anchors, out error))
                return Failed(error);
            // THE VOLUME IS ALWAYS READ (entry Z-C21.2, finding L4-3), not only
            // with the anchors: it is the check by which the comment at the top says it
            // discovered the two-inch hole, and without it the hole in error
            // passed as «positions_not_checked».
            double? volumeBefore = VolumeMm3(model);

            // ── 1. it is created THROUGH, always ─────────────────────────
            model.ClearSelection2(true);
            if (!SelectFace(face))
                return Failed("I could not select the face.");

            IFeature feature;
            try
            {
                feature = model.FeatureManager.HoleWizard5(
                    GenericHoleType: kind.Generic,
                    StandardIndex: IsoStandard,
                    FastenerTypeIndex: kind.Element,
                    SSize: size,
                    EndType: (short)swEndConditions_e.swEndCondThroughAll,
                    Diameter: 0, Depth: 0, Length: 0,
                    Value1: 0, Value2: 0, Value3: 0, Value4: 0, Value5: 0, Value6: 0,
                    Value7: 0, Value8: 0, Value9: 0, Value10: 0, Value11: 0, Value12: 0,
                    ThreadClass: "", RevDir: false,
                    FeatureScope: false, AutoSelect: true,
                    AssemblyFeatureScope: false, AutoSelectComponents: false,
                    PropagateFeatureToParts: false) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("The hole wizard did not succeed: " + ex.Message);
            }

            if (feature == null)
                return Failed(
                    "SolidWorks did not accept the size «" + size + "» for a hole of type " +
                    kind.Human + " according to ISO. Metric sizes are written " +
                    "«M3», «M4», «M5», «M6», «M8», «M10», «M12», «M14», «M16», «M20», " +
                    "«M24» — with a capital M and no spaces.");

            // ── 2-4. the standard, the type, the modification ────────────
            if (!FixUpTheHole(model, feature, kind, size, kind.ThroughAllType,
                               false, 0, out error))
                return Failed(error);
            // IFeature~ModifyDefinition.html, Remarks: «For some feature
            // modifications, this method applies the new properties, returns
            // true, but results in a new rebuild error on the feature. [...]
            // After calling this method, call IFeature::GetErrorCode2 to
            // ascertain whether the feature has rebuild errors» (finding L4-3).
            //
            // IT IS READ HERE, BUT DECIDED AT POINT 6a (review of Z-C21.2). At
            // this point the hole is still where HoleWizard5 put it — the
            // face is selected without a point (SelectFace) — and on a
            // face that does not contain that provisional point the hole may be
            // in error now and sound after PositionHoles. Refusing here would mean
            // refusing a good hole. The present code is kept and
            // reported if in the end the hole stays in error.
            string intermediateErrors = null;
            string inError = HoleInError(feature);
            if (inError != null) intermediateErrors = "after the ISO standard: " + inError;

            // ── 5. the blind one, in a second pass ───────────────────────
            if (!throughAll &&
                !FixUpTheHole(model, feature, kind, size, kind.BlindType,
                               true, depth, out error))
                return Failed(error);
            inError = throughAll ? null : HoleInError(feature);
            if (inError != null)
                intermediateErrors = (intermediateErrors == null ? "" : intermediateErrors + "; ") +
                                  "brought to blind: " + inError;

            // ── 6. the positions ─────────────────────────────────────────
            if (!PositionHoles(model, feature, axes, offsetM, positions, out error))
                return Failed(error);

            try { model.EditRebuild3(); } catch { }

            // ── 6a. WITH THE HOLES POSITIONED: IN ERROR? DID IT REMOVE ANYTHING? ─
            //
            // (finding L4-3.) A hole moved where there is no material is a
            // feature that rebuilds in error, or that removes nothing: in
            // both cases there are no cylinders to find, the verdict
            // of the positions said «not verifiable», and the operation
            // answered succeeded. GetErrorCode2 first, the volume then — the
            // project has measured that GetErrorCode2 alone can stay silent on a
            // real defect (entry MissingCopies, SwOperations.cs).
            inError = HoleInError(feature);
            if (inError != null)
                return Failed("The wizard hole did not come out: in the requested positions «" + feature.Name +
                               "» is in error (" + inError + "). Check that every position is on the " +
                               "face." +
                               (intermediateErrors == null ? "" : " It was already in error before positioning it (" +
                                                               intermediateErrors + ").") +
                               RemoveCut(model, feature, volumeBefore));
            double? drilledVolume = VolumeMm3(model);
            if (volumeBefore.HasValue && drilledVolume.HasValue && drilledVolume.Value > volumeBefore.Value - 0.01)
                return Failed(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The wizard hole did not come out: «{0}» is in the tree but the part weighs as " +
                    "before ({1:0.##} mm3), that is it removed no material. Check that every position " +
                    "is on the face.", feature.Name, volumeBefore.Value) +
                    RemoveCut(model, feature, volumeBefore));

            var measurements = new Dictionary<string, object>
            {
                ["kind"] = kind.Human,
                ["size"] = size,
                ["holes"] = positions.Count,
                ["through_all"] = throughAll,
            };

            // ── 6b. the anchor, with the holes positioned ────────────────
            //
            // THE SIZE OF THE HOLE IS READ FIRST, BEFORE THE ANCHOR.
            // The diameters do not change with the anchor, and the fit check
            // below needs them: it runs before AnchorHoles, so that a hole that
            // does not fit is refused on its size, and not on whatever the
            // anchor makes of the edges it has just cut (Z-C21.2, finding N50).
            var definition = feature.GetDefinition() as IWizardHoleFeatureData2;
            double holeDiameter = 0;
            // THE WIDEST CUT (N50): the anchor must fit the counterbore or the
            // countersink too, not only the drilled hole. The two diameters are
            // read only for their own type of hole, as their pages say:
            // IWizardHoleFeatureData2~CounterBoreDiameter.html, «This property
            // is relevant only for swCounterBored and swCounterBoredDrilled
            // holes»; ~CounterSinkDiameter.html, «This property is relevant only
            // for swCounterSunk and swCounterSunkDrilled holes».
            double widestDiameter = 0;
            string widestCut = "hole";
            if (definition != null)
            {
                holeDiameter = Math.Max(
                    Math.Max(definition.ThruTapDrillDiameter, definition.TapDrillDiameter),
                    Math.Max(definition.ThruHoleDiameter, definition.HoleDiameter)) * 1000.0;
                if (holeDiameter > 0)
                    measurements["diameter_machined_mm"] = Math.Round(holeDiameter, 2);
                if (definition.ThreadDiameter > 0)
                    measurements["thread_mm"] = Math.Round(definition.ThreadDiameter * 1000, 2);
                if (definition.CounterBoreDiameter > 0)
                    measurements["counterbore_mm"] =
                        Math.Round(definition.CounterBoreDiameter * 1000, 2) + "x" +
                        Math.Round(definition.CounterBoreDepth * 1000, 2);
                widestDiameter = holeDiameter;
                if (kind.Generic == (int)swWzdGeneralHoleTypes_e.swWzdCounterBore &&
                    definition.CounterBoreDiameter > 0 &&
                    definition.CounterBoreDiameter * 1000.0 >= widestDiameter)
                {
                    widestDiameter = definition.CounterBoreDiameter * 1000.0;
                    widestCut = "counterbore";
                }
                if (kind.Generic == (int)swWzdGeneralHoleTypes_e.swWzdCounterSink &&
                    definition.CounterSinkDiameter > 0 &&
                    definition.CounterSinkDiameter * 1000.0 >= widestDiameter)
                {
                    widestDiameter = definition.CounterSinkDiameter * 1000.0;
                    widestCut = "countersink";
                }
            }

            // THE ANCHOR WITH THE REAL RADIUS: distances and centred axes.
            if (anchors != null)
            {
                if (widestDiameter > 0)
                {
                    string bypass = BypassWithRadius(anchors, axes, widestDiameter / 2, widestCut);
                    if (bypass != null) return Failed(bypass + RemoveCut(model, feature, volumeBefore));
                }
                else
                    // A check that could not run says so (N50): the anchor was
                    // read with a zero radius, and nothing else has looked.
                    measurements["anchor_fit_not_checked"] = "SOLIDWORKS does not declare a diameter for this hole: " +
                                                       "I could not check that it fits its anchor edges";
            }

            if (anchors != null)
            {
                if (!AnchorHoles(model, feature, axes, offsetM, positions, anchors, measurements, out error))
                    return Failed(error + RemoveCut(model, feature, volumeBefore));
                try { model.EditRebuild3(); } catch { }
            }

            // ── 7. it is checked that the holes are where they were asked for ──
            if (!throughAll) measurements["depth_mm"] = depth;

            // THREE OUTCOMES, NOT TWO. «I could not look» is a different
            // answer from «I looked and it is fine», and while they were the same the
            // bench printed «positions verified» on a field that meant
            // nothing. See PositionsVerdict for how it went.
            //
            // Not verifiable is NOT a refusal: the feature succeeded, it is
            // the check that did not happen. It is stated, and we go on.
            // The movement test of the anchor (its fit was checked at 6b).
            if (anchors != null)
            {
                foreach (var anc in anchors)
                {
                    string doesNotHold = MovementTest(model, anc, feature);
                    if (doesNotHold != null) return Failed(doesNotHold + RemoveCut(model, feature, volumeBefore));
                }
            }

            string cannot;
            var foundAxes = HoleAxes(feature, holeDiameter, out cannot);

            var outcome = PositionsVerdict.Judge(
                foundAxes, cannot, axes, offsetM, positions, out string wherePositions);

            if (outcome == PositionsOutcome.Wrong) return Failed(wherePositions);
            if (outcome == PositionsOutcome.Verified) measurements["positions_checked"] = true;
            else measurements["positions_not_checked"] = wherePositions;

            return Succeeded(feature, measurements, FaceNormal(face));
        }

        /// <summary>
        /// Is the hole in rebuild error? The sentence, or null if it is sound or
        /// if the code is only a warning (IFeature~GetErrorCode2.html:
        /// «IsWarning: True if the error is a warning, false if not»). A
        /// code that cannot be read does not become an error: the volume decides
        /// (entry Z-C21.2, finding L4-3).
        /// </summary>
        private static string HoleInError(IFeature hole)
        {
            return ApiHelpers.RebuildError(hole);
        }

        /// <summary>
        /// Writes the standard and the type on the definition, and applies it.
        ///
        /// Called twice for a blind hole: the first to fix the
        /// standard sizes on the through one, the second to bring it to blind. The
        /// second pass fills in by hand the blind twins of the «Thru» fields,
        /// which ChangeStandard does not touch.
        /// </summary>
        private bool FixUpTheHole(
            IModelDoc2 model, IFeature feature, HoleKind kind, string size,
            int typ, bool blind, double depthMm, out string error)
        {
            error = null;
            IWizardHoleFeatureData2 d;
            try { d = feature.GetDefinition() as IWizardHoleFeatureData2; }
            catch (Exception ex) { error = "I cannot read the hole: " + ex.Message; return false; }
            if (d == null) { error = "I cannot read the definition of the hole."; return false; }

            bool access = false;
            try { access = d.AccessSelections(model, null); }
            catch (Exception ex) { error = "I cannot open the hole: " + ex.Message; return false; }
            if (!access) { error = "SolidWorks does not let me modify the hole just created."; return false; }

            try
            {
                // TYPE AFTER ChangeStandard, and the guide's rule on the order does
                // not apply: IWizardHoleFeatureData2~ChangeStandard.html, Remarks,
                // «If changing the standard requires you to change the type, then
                // you must call IWizardHoleFeatureData2::Type before calling
                // IWizardHoleFeatureData2::ChangeStandard». Here the standard does
                // not change: HoleWizard5 already created the hole with
                // StandardIndex IsoStandard, and this passes the same IsoStandard. The
                // Type written below is the measured type correction of step 3 in
                // the note at the top, not an effect of a change of standard
                // (audit finding L4-1, not real).
                if (!blind && !d.ChangeStandard(IsoStandard, kind.Element, size))
                {
                    try { d.ReleaseSelectionAccess(); } catch { }
                    error =
                        "The size «" + size + "» is not in the ISO table of the holes of type " +
                        kind.Human + ".";
                    return false;
                }
                d.Type = typ;

                if (blind)
                {
                    double p = depthMm / 1000.0;
                    d.EndCondition = (int)swEndConditions_e.swEndCondBlind;
                    d.Depth = p;
                    // The blind twins: without them, the hole comes out any which way.
                    if (d.HoleDiameter <= 0 && d.ThruHoleDiameter > 0)
                        d.HoleDiameter = d.ThruHoleDiameter;
                    if (d.HoleDiameter > 0) d.HoleDepth = p;
                    if (d.TapDrillDiameter <= 0 && d.ThruTapDrillDiameter > 0)
                        d.TapDrillDiameter = d.ThruTapDrillDiameter;
                    if (d.TapDrillDiameter > 0)
                    {
                        d.TapDrillDepth = p;
                        // The thread does not reach the bottom of the pilot hole: it is how
                        // a blind hole is threaded, and the drill tip leaves the cone.
                        d.ThreadDepth = p * 0.7;
                        d.ThreadEndCondition =
                            (int)swWzdHoleThreadEndCondition_e.swEndThreadTypeBLIND;
                    }
                }
            }
            catch (Exception ex)
            {
                try { d.ReleaseSelectionAccess(); } catch { }
                error = "I cannot set up the hole: " + ex.Message;
                return false;
            }

            bool applied;
            // S11 (ApiHelpers.ModifyAndVerify); the error code is judged by the
            // caller at step 6a, once the holes are positioned (HoleInError).
            try { applied = ApiHelpers.ModifyAndVerify(feature, d, model).Applied; }
            catch (Exception ex)
            {
                try { d.ReleaseSelectionAccess(); } catch { }
                error = "The modification of the hole did not succeed: " + ex.Message;
                return false;
            }
            if (!applied)
            {
                try { d.ReleaseSelectionAccess(); } catch { }
                error = "SolidWorks did not apply the standard measures to the hole.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// The anchors of the holes (entry E18), one per position in the same order.
        /// `anchors` stays null if no position has one (the old way);
        /// false with the reason if some have one and some do not, or if an anchor is
        /// wrong. A single hole with x_mm/y_mm at the top takes `anchor` at the top.
        /// </summary>
        private bool ReadHoleAnchors(
            Dictionary<string, object> a, DocumentState docState, IFace2 face, double[] axes, double offsetM,
            List<double[]> positions, out List<Anchoring> anchors, out string error)
        {
            anchors = null;
            error = null;
            var raw = new List<object>();
            object itemList;
            if (a.TryGetValue("positions", out itemList) && itemList is System.Collections.IEnumerable)
                foreach (var entry in (System.Collections.IEnumerable)itemList)
                {
                    var m = entry as Dictionary<string, object>;
                    object anc = null;
                    if (m != null) m.TryGetValue("anchor", out anc);
                    raw.Add(anc);
                }
            else
            {
                object anc;
                a.TryGetValue("anchor", out anc);
                raw.Add(anc);
            }
            int withAnchor = raw.Count(g => g != null);
            if (withAnchor == 0) return true;
            if (withAnchor != positions.Count)
            {
                error = "The anchor of the holes must be given to ALL the positions or to none: here " +
                         withAnchor + " of " + positions.Count + " have it. The holes are in a single sketch, and a " +
                         "point without an anchor would be left free to move at the first change.";
                return false;
            }
            anchors = new List<Anchoring>();
            object signatures;
            a.TryGetValue("_signatures", out signatures);
            for (int i = 0; i < positions.Count; i++)
            {
                var its = new Dictionary<string, object> { ["anchor"] = raw[i] };
                if (signatures != null) its["_signatures"] = signatures;
                string reason;
                // The radius is not known yet (the table decides it): 0 here, and the
                // check for overrun is redone once the hole is born.
                var anc = ReadAnchor(its, docState, face, axes, offsetM, positions[i][0], positions[i][1], 0, 0, out reason);
                if (anc == null) { error = "Hole " + (i + 1) + ": " + reason; anchors = null; return false; }
                anchors.Add(anc);
            }
            return true;
        }

        /// <summary>The positions asked for, in model mm on the two axes of the face.</summary>
        private static List<double[]> HolePositions(
            Dictionary<string, object> a, out string error)
        {
            error = null;
            var outside = new List<double[]>();

            object raw;
            if (a != null && a.TryGetValue("positions", out raw) && raw != null)
            {
                var itemList = raw as System.Collections.IEnumerable;
                if (itemList == null) { error = "The positions of the holes cannot be read."; return null; }
                int i = 0;
                foreach (var entry in itemList)
                {
                    i++;
                    var m = entry as Dictionary<string, object>;
                    if (m == null) { error = "The position " + i + " cannot be read."; return null; }
                    double x, y;
                    if (!RequiredNum(m, "x_mm", out x, out error)) return null;
                    if (!RequiredNum(m, "y_mm", out y, out error)) return null;
                    outside.Add(new[] { x, y });
                }
            }
            else
            {
                // A single hole can also be asked for with x_mm/y_mm at the top level,
                // as cylindrical_cut does: it is the form the model
                // already knows.
                double x, y;
                if (!RequiredNum(a, "x_mm", out x, out error)) return null;
                if (!RequiredNum(a, "y_mm", out y, out error)) return null;
                outside.Add(new[] { x, y });
            }

            if (outside.Count == 0) { error = "No position: there is no hole to make."; return null; }
            if (outside.Count > MaximumHoles)
            {
                error = "There are " + outside.Count + " holes in a single feature: I do not make more than " +
                         MaximumHoles + " in one feature; split them into several.";
                return null;
            }

            // Two holes at the same point are an error of whoever wrote the
            // plan, and SolidWorks would accept them by superimposing them.
            for (int i = 0; i < outside.Count; i++)
                for (int j = i + 1; j < outside.Count; j++)
                    if (Math.Abs(outside[i][0] - outside[j][0]) < 0.01 &&
                        Math.Abs(outside[i][1] - outside[j][1]) < 0.01)
                    {
                        error = "The holes " + (i + 1) + " and " + (j + 1) +
                                 " are in the same point.";
                        return null;
                    }
            return outside;
        }

        /// <summary>
        /// Moves the point that SolidWorks put by itself and adds one
        /// for every other hole.
        ///
        /// Three precautions, all three necessary and all three measured:
        ///   - we stay INSIDE EditSketch: outside, SetCoords returns False;
        ///   - AddToDB on: the geometry enters at the written coordinates
        ///     instead of where the snapping engine takes it;
        ///   - inference off: otherwise the new points take alignment
        ///     relations with the first, and the error of the first becomes
        ///     the error of all.
        /// </summary>
        private bool PositionHoles(
            IModelDoc2 model, IFeature feature, double[] axes, double offsetM,
            List<double[]> positions, out string error)
        {
            error = null;
            var featSketch = PositioningSketch(feature);
            if (featSketch == null)
            {
                error = "I cannot find the sketch that holds the position of the holes.";
                return false;
            }
            var sketch = featSketch.GetSpecificFeature2() as ISketch;
            if (sketch == null) { error = "The sketch of the holes cannot be read."; return false; }

            IMathTransform transformation;
            IMathUtility math;
            try
            {
                transformation = sketch.ModelToSketchTransform;
                math = _sw.GetMathUtility() as IMathUtility;
            }
            catch (Exception ex)
            {
                error = "I cannot read the orientation of the sketch: " + ex.Message;
                return false;
            }
            if (transformation == null || math == null)
            {
                error = "SolidWorks does not give me the orientation of the sketch of the holes.";
                return false;
            }

            var handler = model.SketchManager;
            bool addToDb = handler.AddToDB;
            bool deduction = handler.AutoInference;
            bool show = handler.DisplayWhenAdded;
            bool beingEdited = false;
            try
            {
                model.ClearSelection2(true);
                if (!featSketch.Select2(false, 0))
                {
                    error = "I cannot select the sketch of the holes.";
                    return false;
                }
                model.EditSketch();
                beingEdited = true;
                handler.AddToDB = true;
                handler.AutoInference = false;
                handler.DisplayWhenAdded = false;

                var points = (sketch.GetSketchPoints2() as object[]) ?? new object[0];
                if (points.Length == 0)
                {
                    error = "The sketch of the holes is empty: I do not know where to put them.";
                    return false;
                }

                var first = InSketch(math, transformation, axes, offsetM, positions[0]);
                if (first == null || !(points[0] as ISketchPoint).SetCoords(first[0], first[1], 0))
                {
                    error = "I could not put the first hole where requested.";
                    return false;
                }

                for (int i = 1; i < positions.Count; i++)
                {
                    var p = InSketch(math, transformation, axes, offsetM, positions[i]);
                    if (p == null || handler.CreatePoint(p[0], p[1], 0) == null)
                    {
                        error = "I could not add the hole " + (i + 1) + ".";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "The positioning of the holes did not succeed: " + ex.Message;
                return false;
            }
            finally
            {
                handler.AddToDB = addToDb;
                handler.AutoInference = deduction;
                handler.DisplayWhenAdded = show;
                if (beingEdited) { try { handler.InsertSketch(true); } catch { } }
                try { model.ClearSelection2(true); } catch { }
            }
        }

        private static double[] InSketch(
            IMathUtility math, IMathTransform transformation,
            double[] axes, double offsetM, double[] positionMm)
        {
            try
            {
                var inModel = new double[3];
                inModel[(int)axes[0]] = Units.M(positionMm[0]);
                inModel[(int)axes[1]] = Units.M(positionMm[1]);
                inModel[(int)axes[2]] = offsetM;
                var point = math.CreatePoint(inModel) as IMathPoint;
                var inside = point?.MultiplyTransform(transformation) as IMathPoint;
                var v = inside?.ArrayData as double[];
                return (v == null || v.Length < 2) ? null : v;
            }
            catch { return null; }
        }

        /// <summary>
        /// Among the sub-features of a wizard hole there are two sketches: that
        /// of the revolve profile, full of segments, and the positioning
        /// one, made of points only. The one with fewer segments is taken,
        /// which is an invariant and not a localised name.
        /// </summary>
        private static IFeature PositioningSketch(IFeature hole)
        {
            IFeature best = null;
            int less = int.MaxValue;
            try
            {
                IFeature s = hole.IGetFirstSubFeature();
                while (s != null)
                {
                    var sk = s.GetSpecificFeature2() as ISketch;
                    if (sk != null)
                    {
                        var seg = sk.GetSketchSegments() as object[];
                        // ZERO-E-ZERO: a sketch of points only has no segments, and
                        // null is exactly the positioning sketch being looked for.
                        int n = seg == null ? 0 : seg.Length;
                        if (n < less) { less = n; best = s; }
                    }
                    s = s.IGetNextSubFeature();
                }
            }
            catch { }
            return best;
        }

        /// <summary>
        /// Gathers the axes of the cylindrical faces of the expected diameter.
        ///
        /// THE PART IS LOOKED AT, NOT THE DEFINITION. We wrote the definition
        /// ourselves: rereading it would only say that we wrote it.
        ///
        /// THIS FUNCTION DOES NOT JUDGE, IT GATHERS. It is the only half that
        /// needs SOLIDWORKS; the judgement lives in
        /// <see cref="PositionsVerdict"/>, which is tried at a table.
        ///
        /// When it cannot gather, it SAYS so in <paramref name="cannot"/>
        /// instead of returning an empty list that looks like «no hole
        /// out of place». The previous version returned `null` — that is «all
        /// in order» — on four routes where it had looked at nothing, and whoever
        /// called it wrote `positions_checked = true` unconditionally.
        /// </summary>
        private List<double[]> HoleAxes(
            IFeature feature, double diameterMm, out string cannot)
        {
            cannot = null;

            if (diameterMm <= 0)
            {
                cannot = "SOLIDWORKS does not declare a diameter for this feature: " +
                           "I have nothing to compare with";
                return null;
            }

            var foundAxes = new List<double[]>();
            try
            {
                var faces = feature.GetFaces() as object[];
                if (faces == null)
                {
                    cannot = "the feature does not expose its faces";
                    return null;
                }
                foreach (var o in faces)
                {
                    var surface = (o as IFace2)?.IGetSurface();
                    if (surface == null || !surface.IsCylinder()) continue;
                    var par = surface.CylinderParams as double[];
                    if (par == null || par.Length < 7) continue;
                    if (Math.Abs(par[6] * 2000.0 - diameterMm) > 0.05) continue;
                    foundAxes.Add(par);
                }
            }
            catch (Exception ex)
            {
                cannot = "I could not read the faces of the feature: " + ex.Message;
                return null;
            }

            return foundAxes;
        }
    }
}
