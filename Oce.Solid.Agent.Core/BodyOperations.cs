using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE BOOLEANS BETWEEN BODIES: unite, subtract, intersect. (entry GP8)
    ///
    /// They are needed for moulds and jigs, for composite parts drawn as several
    /// bodies and then united, and for parts to be split for 3D printing when they do not
    /// fit on the bed.
    ///
    /// ── TWO `InsertCombineFeature`, AND ONLY ONE IS THE RIGHT ONE ───────
    ///
    /// Read by reflection from the interop 29.5.1.1:
    ///
    ///   IFeatureManager.InsertCombineFeature(int, Body2, object) -> Feature
    ///   IPartDoc       .InsertCombineFeature(int, object, object) -> Boolean
    ///
    /// Same arity, same name, different types and return, and **they are not the one
    /// the evolution of the other**. The one on `IPartDoc` returns a boolean: the
    /// feature, if it is born, cannot be reached — and a later step that
    /// wanted to cite it would have nothing to cite. Here the one of the
    /// FeatureManager is used.
    ///
    /// ── THE ENUM WITH THE RIGHT NAME IS THE WRONG ONE ───────────────────
    ///
    /// In `swconst` there is `swCombineBodiesOperationType_e`, and its three values
    /// are called **Add = 0, Subtract = 1, Common = 2**: the exact names of the
    /// three booleans. Passed to `InsertCombineFeature` they **all three return
    /// `null`**, without an exception and without a code.
    ///
    /// What the method wants is `swBodyOperationType_e`:
    ///
    ///     SWBODYADD       = 15903     unite
    ///     SWBODYCUT       = 15902     subtract
    ///     SWBODYINTERSECT = 15901     intersect
    ///
    /// **A name says what a constant is called, not which function it
    /// belongs to.** It was the risk written in the entry — «a wrong value
    /// consumes the wrong body without an error» — and reality was milder
    /// than that: it consumes nothing, does nothing, and does not say so.
    ///
    /// ── AND THE ARRAY MUST BE WRAPPED, AND ALSO CONTAINS THE MAIN ONE ───
    ///
    /// Measured on new parts, one variant per part (trying them in a row on the
    /// same document gave inconsistent results):
    ///
    ///                              ADD 15903   CUT 15902   INTERSECT 15901
    ///   object[] with the tool only   NULL        NULL          NULL
    ///   typed Body2[]                 NULL      «Abbina1»       NULL
    ///   DispatchWrapper[] tool only   NULL      «Abbina1»       NULL
    ///   DispatchWrapper[] BOTH     «Abbina1» «Abbina1»     «Abbina1»
    ///
    /// Two things together, and neither of them complains when missing:
    ///
    /// 1. **An `object[]` of RCW never arrives.** The parameter is a `VARIANT`
    ///    that wants a SAFEARRAY of `IDispatch*`, and an `object[]` of COM
    ///    objects does not become one by itself: `DispatchWrapper` is needed.
    /// 2. **The array must also contain the main body**, not only the
    ///    tools — for `union` and `intersect` without it nothing
    ///    happens. For `subtract` the tool would be enough, but putting it in does not
    ///    change the result (600 mm³ both ways), and a single rule
    ///    is worth more than three.
    ///
    /// ── THE THREE NUMBERS, ON A CASE THAT TELLS THEM APART ──────────────
    ///
    /// Two cubes of 10 offset by 2 in X and 5 in Z, overlapping by
    /// 8 x 10 x 5 = 400 mm³:
    ///
    ///     unite       1000 + 1000 - 400 = 1600 mm³
    ///     subtract    1000 - 400        =  600 mm³
    ///     intersect                        400 mm³
    ///
    /// With two equal and centred cubes the three numbers would have been 1500, 500 and
    /// 500 — and «subtract» and «intersect» could not have been told apart. A case in
    /// which two wrong values give the same result tabulates nothing.
    ///
    /// ── AND THE BODIES ARE COUNTED ──────────────────────────────────────
    ///
    /// A union that fails silently leaves **two bodies** where one was needed:
    /// the tree grows just the same, the shape looks the same, and the difference
    /// shows only by counting. It is the same check that `extrude_on_face`
    /// already does on its `Merge`.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// The boolean that is done unless told otherwise (entry Z-G3).
        /// Same reasoning as `DefaultMaterial`: the schema
        /// announces it, this applies it, and the value lives in one place only.
        /// </summary>
        private const string DefaultOperation = "union";

        /// <summary>
        /// Tail of the message when the main body is the default (the first
        /// body, by position) and it coincides with a named tool: without it,
        /// «e' anche il corpo principale» would sound like a choice of whoever asks.
        /// </summary>
        private const string DefaultMain =
            ", the default: the first body, taken by position because you did not " +
            "name it. Name it with «main_body_uid» or «main_body_ref»";

        private Outcome CombineBodies(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null)
                return Failed("This document is not a part: booleans are done between bodies.");

            // ── THE DEFAULT (entry Z-G3) ─────────────────────────────────
            //
            // «union» is by far the most common case: «subtract» and
            // «intersect» are asked for on purpose. Here the default is more
            // dangerous than elsewhere — getting the direction wrong does not leave a part
            // of the wrong material, it leaves **a part with a hole in it
            // that nobody asked for** — and for this it is declared, and
            // for this the bench case measures the VOLUME instead of looking
            // whether it succeeded: among the three booleans the three volumes are
            // unmistakable, and «succeeded: true» would give all three.
            string asRequested = (Str(a, "operation", "") ?? "").Trim().ToLowerInvariant();
            bool assumed = asRequested.Length == 0;
            if (assumed) asRequested = DefaultOperation;
            int typ;
            // `swBodyOperationType_e`, NOT `swCombineBodiesOperationType_e`:
            // see the comment at the head of the file.
            if (asRequested == "union") typ = (int)swBodyOperationType_e.SWBODYADD;
            else if (asRequested == "subtract") typ = (int)swBodyOperationType_e.SWBODYCUT;
            else if (asRequested == "intersect") typ = (int)swBodyOperationType_e.SWBODYINTERSECT;
            else return Failed(
                "You did not say which boolean to do, or I do not know it: «" + asRequested +
                "». There are «union», «subtract» and «intersect».");

            var rawItems = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            // ZERO-E-ZERO: `GetBodies2` above is not in a try — a dead
            // handle raises and does not get this far — so null is «no body».
            if (rawItems == null || rawItems.Length < 2)
                return Failed(
                    "A boolean needs at least two bodies, and this part has " +
                    (rawItems == null ? 0 : rawItems.Length) + ". Separate bodies come from " +
                    "«move_bodies» and the copy, or from an extrusion that does not merge.");

            // ── WHO IS THE MAIN ONE, AND WHO GOES ON TOP OF IT ───────────
            //
            // In subtraction the order CHANGES THE PART: A minus B is not B
            // minus A. The main one is said, and if it is not said it is the first —
            // but then it is written in the answer, because whoever reads must
            // know which of the two was left.
            // ── IDENTIFIERS ARE RESOLVED BY SIGNATURE, NOT BY POSITION ──
            //
            // (entry Z-C21.2, finding L6-1.) Up to library 0.136.0 «C2»
            // meant «the second of a GetBodies2 made now», and the
            // `state` parameter was not even read.
            // IPartDoc~GetBodies2.html, Remarks: «May vary the order in which
            // bodies are returned». A cut between the reading of the state and the
            // boolean renumbers the bodies, and the wrong body was combined with
            // `succeeded: true`: it is entry E4, measured on 14/9/2026 on the
            // twin `move_bodies` and corrected there with `BodiesFromUid`, which
            // compares the signature (centre and box) read from the plan with the one of
            // now. Here it now goes through the same point.
            //
            // AND THE BODIES FOUND ARE PASSED AS THEY ARE, as move_bodies does
            // with `firma.Entity`. The first draft searched for them again inside the
            // GetBodies2 above with ReferenceEquals/Equals: it holds only if
            // SOLIDWORKS gives the same COM object for the same body in two
            // calls, and this is not measured (review of Z-C21.2). Where it is needed
            // to know whether two bodies are the same — the main one among the
            // tools — SOLIDWORKS is asked: ISldWorks~IsSame.html, «Gets
            // whether the two specified objects are the same object», «Return
            // Value Object equality as defined by swObjectEquality», as in
            // StatoTavola.
            //
            // The `_ref` (table L6-1): without them, a plan «cut, then boolean»
            // has no valid way to name the bodies — the uids read
            // before the cut are now refused by signature, and the new ones the
            // plan does not know. Same path as move_bodies:
            // BodiesFromReference.
            string bodyReason;
            Func<object, object, bool> same = (x, y) =>
            {
                if (x == null || y == null) return false;
                if (ReferenceEquals(x, y)) return true;
                try { return _sw.IsSame(x, y) == (int)swObjectEquality.swObjectSame; }
                catch { return false; }
            };
            // The name to report: the uid of the STATE if the body is there, otherwise
            // the position, said as such — the numbers of the GetBodies2 of now
            // are not those of the state (GetBodies2.html, same sentence).
            Func<object, int, string> nameOf = (body, position) =>
            {
                if (docState != null && docState.Signatures != null)
                    foreach (var kv in docState.Signatures)
                        if (kv.Value != null && kv.Value.Type == "BODY" && same(kv.Value.Entity, body))
                            return kv.Key;
                return position < 0 ? "(unnamed body in the state)"
                                     : "C" + (position + 1) + " (by position)";
            };

            bool hasMainRef = HasReference(a, "main_body_ref");
            bool hasToolsRef = HasReference(a, "tool_bodies_ref");
            string mainUid = Str(a, "main_body_uid", null);
            var requested = StringList(a, "tool_bodies_uid");
            if (hasMainRef && !string.IsNullOrWhiteSpace(mainUid))
                return Failed("You gave both «main_body_uid» and «main_body_ref»: one names a " +
                               "body of the state, the other the body of a step. Remove one of the two.");
            if (hasToolsRef && requested != null && requested.Count > 0)
                return Failed("You gave both «tool_bodies_uid» and «tool_bodies_ref»: one names " +
                               "bodies of the state, the other those of a step. Remove one of the two.");

            object main;
            string mainName;
            bool mainByPosition = false;
            if (hasMainRef)
            {
                int howMany;
                var fromStep = BodiesFromReference(a, "main_body_ref", docState, out howMany, out bodyReason);
                if (fromStep == null) return Failed(bodyReason);
                if (fromStep.Length != 1)
                    return Failed("The step of «main_body_ref» left " + fromStep.Length +
                                   " bodies, and there can be only one main body: name it with «main_body_uid» " +
                                   "after reading the part again.");
                main = fromStep[0];
                mainName = nameOf(main, Array.FindIndex(rawItems, g => same(g, main)));
            }
            else if (!string.IsNullOrWhiteSpace(mainUid))
            {
                mainUid = mainUid.Trim();
                var foundItems = BodiesFromUid(docState, new List<string> { mainUid }, a, out bodyReason);
                if (foundItems == null) return Failed(bodyReason);
                main = foundItems[0];
                mainName = mainUid;
            }
            else
            {
                // The DEFAULT stays «the first», and it is by position: it is said.
                main = rawItems[0];
                mainName = nameOf(rawItems[0], 0);
                mainByPosition = true;
            }

            var toolBodies = new List<object>();
            var toolNames = new List<string>();
            if (hasToolsRef)
            {
                int howMany;
                var fromStep = BodiesFromReference(a, "tool_bodies_ref", docState, out howMany, out bodyReason);
                if (fromStep == null) return Failed(bodyReason);
                foreach (var c in fromStep)
                {
                    if (same(c, main))
                        return Failed("Among the bodies of «tool_bodies_ref» there is also the main body (" +
                                       mainName + (mainByPosition ? DefaultMain : "") +
                                       "): a body is not combined with itself.");
                    toolBodies.Add(c);
                    toolNames.Add(nameOf(c, Array.FindIndex(rawItems, g => same(g, c))));
                }
            }
            else if (requested != null && requested.Count > 0)
            {
                foreach (var raw in requested)
                {
                    string u = Convert.ToString(raw).Trim();
                    var foundItems = BodiesFromUid(docState, new List<string> { u }, a, out bodyReason);
                    if (foundItems == null) return Failed(bodyReason);
                    if (same(foundItems[0], main))
                        return Failed(
                            "«" + u + "» is also the main body (" + mainName +
                            (mainByPosition ? DefaultMain : "") +
                            "): a body is not combined with itself.");
                    toolBodies.Add(foundItems[0]);
                    toolNames.Add(u);
                }
            }
            else
            {
                for (int i = 0; i < rawItems.Length; i++)
                {
                    if (same(rawItems[i], main)) continue;
                    toolBodies.Add(rawItems[i]);
                    toolNames.Add(nameOf(rawItems[i], i));
                }
            }
            if (toolBodies.Count == 0)
                return Failed("No body is left to combine with the main one.");

            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            if (volumeBefore == null || bodiesBefore < 0)
                return Failed(VolumeNotReadBefore("combining the bodies"));

            IFeature feature;
            try
            {
                model.ClearSelection2(true);
                // WRAPPED, AND WITH THE MAIN ONE INSIDE: see the comment at the head.
                // An `object[]` of COM objects does not become a SAFEARRAY of
                // IDispatch*, and without the main one in the list «union» and
                // «intersect» do nothing — without saying so.
                var toPass = new List<DispatchWrapper>();
                toPass.Add(new DispatchWrapper(main));
                foreach (var u in toolBodies) toPass.Add(new DispatchWrapper(u));

                feature = model.FeatureManager.InsertCombineFeature(
                    typ, main as Body2, toPass.ToArray()) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("The boolean did not succeed: " + ex.Message);
            }

            // NULL WITHOUT AN EXCEPTION AND WITHOUT A CODE: it is all that
            // this API says when it does not like something. The most
            // frequent reason is geometric — the bodies do not really touch —
            // but it is not the only one, and the sentence says so instead of making it believed
            // certain.
            if (feature == null)
                return Failed(
                    "The boolean «" + asRequested + "» created nothing, and SOLIDWORKS " +
                    "does not say why: this call answers «nothing» and that is it. Usually " +
                    "the bodies do not touch for real: «union» wants bodies that " +
                    "interpenetrate or touch, «subtract» and «intersect» want them to " +
                    "overlap. Look at the bounding boxes with «body_bounding_box».");

            try { model.EditRebuild3(); } catch { }

            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);
            bool reread = volumeAfter.HasValue && bodiesAfter >= 0;

            // A UNION THAT LEAVES TWO BODIES HAS FAILED SILENTLY. The
            // feature is there, the tree has grown, and the part is another.
            //
            // `SWBODYADD`, AND UP TO 0.116.0 NO (entry Z-C18). The comparison was
            // with `swCombineBodiesOperationAdd` (= 0), while `tipo` is 15903:
            // two different enums — exactly the ones that the comment at the head of
            // this file tells apart — and the guard could not fire.
            // Measured on 25/9/2026: with two distant bodies SOLIDWORKS does not create
            // the feature at all (the refusal above), so from the bench this
            // guard cannot be made to fire; it stays for the feature that is born and does not
            // merge, and now it looks at the right thing.
            if (reread && typ == (int)swBodyOperationType_e.SWBODYADD &&
                bodiesAfter >= bodiesBefore)
                return Failed(
                    "The union created the feature but the bodies went from " + bodiesBefore +
                    " to " + bodiesAfter + ": they did not merge. Usually it is that they do not touch " +
                    "for real. I do not confirm a part different from the one I showed you: " +
                    "withdraw it — " + Sentences.HowToWithdrawIt + ".");

            if (reread && volumeAfter <= 0)
                return Failed(
                    "After the boolean the part has no volume left: «" + asRequested +
                    "» consumed everything. Usually it is the order — in a subtraction " +
                    "the main body is the one that REMAINS.");

            var measurements = new Dictionary<string, object>
            {
                ["operation"] = asRequested,
                ["note_assumption"] = assumed
                    ? "operation: «" + DefaultOperation + "» — DEFAULT, " +
                      "you have not asked for it. «subtract» and «intersect» are requested " +
                      "by name."
                    : "operation: «" + asRequested + "» — you asked for it.",
                ["main_body"] = mainName,
                ["tool_bodies"] = toolNames.ToArray(),
                ["bodies_before"] = bodiesBefore,
                ["bodies"] = NumberOrNull(bodiesAfter),
                ["volume_before_mm3"] = NumberOrNull(volumeBefore, 2),
                ["volume_mm3"] = NumberOrNull(volumeAfter, 2),
                ["volume_changed_mm3"] = NumberOrNull(volumeAfter - volumeBefore, 2),
                // THE IDENTIFIERS NO LONGER HOLD, and this is always said: a
                // boolean redoes the faces from scratch.
                ["note_identifiers"] =
                    "The bodies and faces changed: the identifiers (C2, F1, E4) from before " +
                    "are no longer valid. Read the part again before the next step.",
            };
            if (!reread) measurements["volume_not_reread"] = NoteVolumeNotReread;
            _log("[agent] " + asRequested + ": bodies " + bodiesBefore + " -> " + bodiesAfter +
                 ", volume " + ReadValue(volumeBefore) + " -> " + ReadValue(volumeAfter));
            return Succeeded(feature, measurements);
        }

        // =================================================================
        // THE VARIABLE-RADIUS FILLET  (entry GP7)
        // =================================================================
        //
        // It is used to round a long edge gradually: large where the
        // part must take a load, small where there is no room.
        //
        // ── THE RECIPE, MEASURED AND NOT DEDUCED ────────────────────────
        //
        // The entry warned that «the population of Radii / PointRadiusArray /
        // PointRhoArray (order, how many values per vertex) is not verified:
        // a wrong step gives plausible and wrong radii». Tried on a
        // 40x40x40 cube, vertical edge, radii 3 and 8:
        //
        //   edge + its 2 vertices selected, Radii = {3, 8}   277.7 mm³
        //   ONLY the two vertices (without the edge)           NULL
        //   with the marks 1/1/1 instead of no mark          277.7 mm³
        //   adding PointRadiusArray = {3, 8}                 277.7 mm³
        //
        // So: **the edge must be selected**, the vertices too, the marks do not
        // count and `PointRadiusArray` is not needed — that one is for the
        // INTERMEDIATE points, which are not here.
        //
        // ── AND THE NUMBER THAT TELLS THE VARIABLE FROM THE CONSTANT ────
        //
        // The volume removed by a fillet of radius r on an edge of length L is
        // L·r²·(1 − π/4). For a radius going from 3 to 8, the mean of r² is
        // (9 + 24 + 64)/3 = 32.33, and the calculation gives **277.5 mm³** — measured
        // 277.7, that is, the blend is not exactly a linear sweep of the
        // radius but it fits within it to 0.07%.
        //
        // The two wrong numbers are far away: a constant fillet of 3 would
        // remove **77.3**, a constant one of 8 **549.4**. This is how one
        // sees whether it did the variable or took only the first radius.
        // =================================================================

        private Outcome VariableFillet(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string uid = Str(a, "edge_uid", null);
            if (string.IsNullOrWhiteSpace(uid))
                return Failed(
                    "You did not say on which edge. The identifiers of the edges " +
                    "are in the state of the document, and look like «E7».");

            double r1 = Num(a, "start_radius_mm");
            double r2 = Num(a, "end_radius_mm");
            if (r1 <= 0 || r2 <= 0)
                return Failed(
                    "The two radii must both be greater than zero: you asked " +
                    r1.ToString("0.##") + " and " + r2.ToString("0.##") + ". For a fillet that " +
                    "is equal from end to end there is «fillet_edges».");

            string offset;
            var signature = EdgeSignature(
                a, docState, uid, "The edge «" + uid + "» is no longer in this document.",
                out offset);
            if (signature == null) return Failed(offset);

            var edge = signature.Entity as IEdge;
            if (edge == null)
                return Failed("The edge «" + uid + "» is no longer usable.");

            // THE LARGEST RADIUS MUST FIT, and it is measured now: between the
            // proposal and the execution the part may have changed. It is the
            // same check as for the constant fillet, on the larger of the two.
            double maxValue = Math.Max(r1, r2);
            if (signature.Measure > 0 && maxValue > signature.Measure / 2)
                return Failed(string.Format(
                    "{0:0.##} mm is too much for the edge «{1}», which is {2:0.##} mm long: " +
                    "not even half of it fits.", maxValue, uid, signature.Measure));

            var v1 = edge.GetStartVertex() as IEntity;
            var v2 = edge.GetEndVertex() as IEntity;
            if (v1 == null || v2 == null)
                return Failed(
                    "The edge «" + uid + "» does not have two vertices: it is " +
                    "closed on itself (a circle), and a variable fillet needs two ends " +
                    "to vary between.");

            double? volumeBefore = VolumeMm3(model);
            if (volumeBefore == null) return Failed(VolumeNotReadBefore("making the fillet"));

            IFeature feature;
            try
            {
                model.ClearSelection2(true);
                // THE EDGE FIRST, THEN THE TWO VERTICES. Without the edge the
                // call returns null: measured.
                if (!(edge as IEntity).Select4(true, null))
                    return Failed("I could not select the edge «" + uid + "».");
                if (!v1.Select4(true, null) || !v2.Select4(true, null))
                    return Failed(
                        "I could not select the two vertices of the edge «" +
                        uid + "»: without them the radius has nowhere to vary.");

                feature = model.FeatureManager.FeatureFillet3(
                    Options: (int)(swFeatureFilletOptions_e.swFeatureFilletVarRadiusType |
                                   swFeatureFilletOptions_e.swFeatureFilletPropagate),
                    // R1 IS NOT READ FOR A VARIABLE RADIUS (Z-C21.8, finding M37):
                    // IFeatureManager~FeatureFillet3.html, «R1 Uniform radius of
                    // the symmetric fillet; valid only if: Ftyp !=
                    // swFeatureFilletType_e.swFeatureFilletType_VariableRadius».
                    // The radii are the two of `Radii` below. The Z-C21.3 probe of
                    // L7-7 measured 277.694 mm3 with R1 = the first radius; the
                    // bench case fillet_that_changes_radius (277.7) is the check
                    // that R1 = 0, as the page asks, changes nothing.
                    R1: 0,
                    R2: 0,
                    Rho: 0,
                    Ftyp: (int)swFeatureFilletType_e.swFeatureFilletType_VariableRadius,
                    OverflowType: (int)swFilletOverFlowType_e.swFilletOverFlowType_Default,
                    ConicRhoType: (int)swFeatureFilletProfileType_e.swFeatureFilletCircular,
                    // ONE PER VERTEX, in the order in which they were
                    // selected: start and end.
                    Radii: new double[] { Units.M(r1), Units.M(r2) },
                    Dist2Arr: null,
                    RhoArr: null,
                    SetBackDistances: null,
                    // THERE ARE NO INTERMEDIATE POINTS, and `PointRadiusArray` is
                    // for those: passing it changes nothing (measured), and
                    // passing it out of habit would be an argument that nobody
                    // knows what it does.
                    PointRadiusArray: null,
                    PointDist2Array: null,
                    PointRhoArray: null) as IFeature;
            }
            catch (Exception ex) { return Failed("The variable fillet did not succeed: " + ex.Message); }

            if (feature == null)
                return Failed(
                    "The variable fillet was not created, and SOLIDWORKS does " +
                    "not say why. Usually it is the radius: even if it fits on the " +
                    "edge, it may not fit on the faces around it.");

            try { model.EditRebuild3(); } catch { }
            double? volumeAfter = VolumeMm3(model);
            double? removed = volumeBefore - volumeAfter;

            // A FILLET THAT REMOVES NOTHING IS NOT A FILLET. The feature
            // is there, the tree has grown, and the edge is still alive. It is said
            // only from a volume read back (entry Z-C8).
            if (removed <= 0)
                return Failed(
                    "The fillet was created but it removed nothing (volume " +
                    volumeBefore.Value.ToString("0.##") + " mm3 before and after). The edge " +
                    "is still alive: I do not confirm a part different from the one I " +
                    "showed you — " + Sentences.HowToWithdrawIt + ".");

            return Succeeded(feature, new Dictionary<string, object>
            {
                ["edge"] = uid,
                ["radius_start_mm"] = r1,
                ["radius_end_mm"] = r2,
                ["material_removed_mm3"] = NumberOrNull(removed, 2),
                ["volume_not_reread"] = removed.HasValue ? null : NoteVolumeNotReread,
                // THE HAND CALCULATION ALONGSIDE, and it is not redundancy: it tells «it did
                // the variable» from «it took only the first radius».
                // The blend is not exactly a linear sweep, so it is
                // an ORDER OF MAGNITUDE, and it is written that it is.
                ["expected_removed_mm3"] = signature.Measure > 0
                    ? (object)Math.Round(
                        signature.Measure * ((r1 * r1 + r1 * r2 + r2 * r2) / 3.0) * (1 - Math.PI / 4), 2)
                    : null,
                ["how_it_is_counted"] =
                    "length times the mean of r^2 between the two radii, times (1 - pi/4). " +
                    "It is an order of magnitude: the real fillet is not exactly a linear sweep " +
                    "of the radius.",
            });
        }

        // =================================================================
        // THE COSMETIC THREAD  (entry GP11)
        // =================================================================
        //
        // IT CUTS NOTHING: it is an annotation. It says «there is a thread here» to
        // whoever reads the part and to the drawing, and the volume stays what it was.
        // If the volume changes the wrong thing was called, and this
        // operation checks it.
        //
        // ── IT ATTACHES TO AN EDGE, NOT TO A FACE ───────────────────────
        //
        // On the cylindrical face `InsertCosmeticThread3` returns **null** in
        // all the combinations tried. What said so was the data model:
        // `ICosmeticThreadFeatureData` has an **`Edge`** property. With the
        // CIRCULAR edge selected it works at the first try.
        //
        // ── AND THE CALLOUT IS NOT GENERATED: IT IS COPIED ──────────────
        //
        // Entry GP11 said: «the generated note, read back, says M6x1 — it is the
        // only way to know whether `Standard` was the right number». Measured on
        // 16/9/2026, the answer is that **`Standard` is no number at all**:
        //
        //   Standard = 0, 1, 2, 3, 8 (ISO), with StandardType «Tapped hole»,
        //   «Machine Threads», «Maschiatura», «Filettature macchina», and Size
        //   «M6x1.0», «M6», «M6x1»:
        //       the feature is always born, and reading it back
        //       Standard = -2,  StandardType = «»,  Size = «»,  callout = «»
        //
        //   passing the NOTE instead:
        //       callout = «M6x1»   — even with Standard = -2 and the two names empty
        //
        // That is: the standard and the size **are ignored**, and the callout is
        // exactly the text that is given to it. So the operation asks for a
        // designation and writes it, and **does not pretend** that it comes from a table:
        // «generated» and «copied» are not the same thing, and whoever reads a
        // drawing has the right to know which of the two they are looking at.
        // =================================================================

        private Outcome CosmeticThread(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string uid = Str(a, "edge_uid", null);
            if (string.IsNullOrWhiteSpace(uid))
                return Failed(
                    "You did not say on which edge. The cosmetic thread attaches to the " +
                    "CIRCULAR edge of a cylinder or of a hole — not to the face — and " +
                    "the identifiers of the edges are in the state of the document.");

            string designation = Str(a, "designation", null);
            if (string.IsNullOrWhiteSpace(designation))
                return Failed(
                    "The «designation» is missing, for example «M6x1». A cosmetic thread " +
                    "without a designation is an annotation that says nothing: on the drawing " +
                    "there would be a thread with no size, and whoever reads it would have to " +
                    "guess it. I write the text as you give it to me: SOLIDWORKS " +
                    "does not work it out from a table.");

            string offset;
            var signature = EdgeSignature(
                a, docState, uid, "The edge «" + uid + "» is no longer in this document.",
                out offset);
            if (signature == null) return Failed(offset);

            var edge = signature.Entity as IEdge;
            if (edge == null)
                return Failed("The edge «" + uid + "» is no longer usable.");

            double diameter = 0;
            bool round = false;
            try
            {
                var curva = edge.IGetCurve();
                round = curva != null && curva.IsCircle();
                if (round)
                {
                    var p = curva.CircleParams as double[];
                    if (p != null && p.Length >= 7) diameter = p[6] * 2000.0;
                }
            }
            catch { }

            if (!round)
                return Failed(
                    "The edge «" + uid + "» is not a circle: a cosmetic thread attaches " +
                    "to the round border of a cylinder or of a hole. On a straight " +
                    "edge there is nothing to thread.");

            // THE DIAMETER IS SAID BY THE EDGE, not by the caller: it is the border on
            // which the thread attaches, and a diameter different from that one
            // would draw a thread that is not on the part.
            double requestedDiameter = Num(a, "diameter_mm", 0);
            if (requestedDiameter > 0 && diameter > 0 &&
                Math.Abs(requestedDiameter - diameter) > 0.01)
                return Failed(string.Format(
                    "You asked for a thread of {0:0.##} mm, but the edge «{1}» is a circle " +
                    "of {2:0.##}. The thread attaches to that border: either it is the right " +
                    "one, or it is the other edge.", requestedDiameter, uid, diameter));
            if (diameter <= 0) diameter = requestedDiameter;
            if (diameter <= 0)
                return Failed("I cannot measure the diameter of the edge «" + uid + "».");

            double depth = Num(a, "depth_mm", 0);
            bool throughAll = Bool(a, "through_all", false);
            if (!throughAll && depth <= 0)
                return Failed(
                    "Tell me how deep the thread is («depth_mm») or that it is " +
                    "«through_all».");

            double? volumeBefore = VolumeMm3(model);
            if (volumeBefore == null) return Failed(VolumeNotReadBefore("putting the thread on"));

            IFeature feature;
            try
            {
                model.ClearSelection2(true);
                if (!(edge as IEntity).Select4(false, null))
                    return Failed("I could not select the edge «" + uid + "».");

                feature = model.FeatureManager.InsertCosmeticThread3(
                    // Standard and size ARE IGNORED by this call
                    // (measured): «no standard» is passed instead of
                    // pretending that a number serves any purpose.
                    Standard: -2,
                    StandardType: "",
                    Size: "",
                    Diameter: Units.M(diameter),
                    EndType: throughAll
                        ? (int)swCosmeticThreadType_e.swApplyCosmeticThread_ThroughFeature
                        : (int)swCosmeticThreadType_e.swApplyCosmeticThread_Blind,
                    Depth: throughAll ? 0 : Units.M(depth),
                    Note: designation) as IFeature;
            }
            catch (Exception ex) { return Failed("The cosmetic thread did not succeed: " + ex.Message); }

            if (feature == null)
                return Failed(
                    "The cosmetic thread was not created, and SOLIDWORKS does " +
                    "not say why. Check that the edge «" + uid + "» is the border of a " +
                    "cylindrical face.");

            try { model.EditRebuild3(); } catch { }

            // IT IS COSMETIC: if the volume changed, something was really cut.
            double? volumeAfter = VolumeMm3(model);
            if (volumeAfter.HasValue && Math.Abs(volumeAfter.Value - volumeBefore.Value) > 0.01)
                return Failed(string.Format(
                    "The thread had to be COSMETIC and the volume changed: {0:0.##} mm3 " +
                    "before, {1:0.##} after. Something really cut, and it is not " +
                    "what you asked for — withdraw it: " + Sentences.HowToWithdrawIt + ".",
                    volumeBefore, volumeAfter));

            string featureType = "";
            try { featureType = feature.GetTypeName2(); } catch { }
            string callout = "";
            try
            {
                var data = feature.GetDefinition() as ICosmeticThreadFeatureData;
                if (data != null) callout = data.ThreadCallout ?? "";
            }
            catch { }

            // AN ANNOTATION THAT SAYS NOTHING is not an annotation: the
            // feature would be there, the drawing would show a mute thread.
            if (string.IsNullOrEmpty(callout))
                return Failed(
                    "The thread was created but its designation stayed empty: on the " +
                    "drawing there would be a thread with no size. Withdraw it — " +
                    Sentences.HowToWithdrawIt + ".");

            return Succeeded(feature, new Dictionary<string, object>
            {
                ["edge"] = uid,
                ["designation"] = callout,
                ["diameter_mm"] = Math.Round(diameter, 3),
                ["depth_mm"] = throughAll ? (object)null : depth,
                ["through_all"] = throughAll,
                ["feature_kind"] = featureType,
                ["volume_mm3"] = NumberOrNull(volumeAfter, 2),
                ["cuts_nothing"] = volumeAfter.HasValue
                    ? "It is an annotation: the volume of the part is the same as before. " +
                      "If you need the REAL thread, cut, this is not the operation."
                    : NoteVolumeNotReread,
                // THE ORIGIN OF THE TEXT IS DECLARED. See the comment at the head.
                ["designation_is_yours"] =
                    "«" + callout + "» is the text you gave me, not one worked out from " +
                    "a table of threads: this call ignores the standard and the size. If " +
                    "it is wrong, it ends up wrong on the drawing.",
            });
        }

        // `IndiceDelCorpo` («C2» -> index 1 of a new GetBodies2) was
        // removed with entry Z-C21.2: it was the resolution by position that
        // finding L6-1 found wrong. The uids of the bodies go through
        // `BodiesFromUid` (WeldmentOperations.cs), which looks at the signature.
    }
}
