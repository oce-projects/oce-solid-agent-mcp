using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE TWO QUESTIONS ASKED BEFORE ORDERING. (entry GP4)
    ///
    /// «How much does this part weigh in aluminium» and «does it fit in the box
    /// 300x260x40». Both were already calculated inside `DocumentState` —
    /// the mass with `IMassProperty`, the extent with `GetExtremePoint` six
    /// times — but **no operation could ask for them**: they arrived
    /// in the whole state of the document, or did not arrive at all.
    ///
    /// Here they become two questions that can be asked on their own, for example
    /// after a change to verify only that one.
    ///
    /// ── THE UNITS ARE STATED, ALWAYS ────────────────────────────────────
    ///
    /// `UseSystemUnits = true` is written EXPLICITLY (ApiHelpers), but not
    /// because the values would otherwise come in the units of the document:
    /// IMassProperty~UseSystemUnits.html, Remarks, «The default value is True.
    /// Thus, system units (meters, radians, and kilograms) are used» (Z-C21.9,
    /// finding N74; the same words on IMassProperty2~UseSystemUnits.html). The
    /// line stays as a statement of the units the numbers are read in, at no
    /// cost. The answer is in metres and kilograms, and the conversion to
    /// millimetres is ours, in one place.
    ///
    /// ── THE TRAP OF THE TWO SPELLINGS ───────────────────────────────────
    ///
    /// `IMassProperty` writes **«Princip*le*»**:
    ///
    ///   IMassProperty.PrincipleMomentsOfInertia
    ///   IMassProperty.PrincipleAxesOfInertia
    ///
    /// `IMassProperty2` writes **«Princip*al*»**:
    ///
    ///   IMassProperty2.PrincipalMomentsOfInertia
    ///   IMassProperty2.PrincipalAxesOfInertia
    ///
    /// Two different spellings for the same thing, on two interfaces that
    /// look alike. With the typed interop it is a compilation error — and
    /// thank goodness; in late binding it would be a `MissingMemberException` at
    /// runtime, or worse a `catch` that swallows it and an answer without the
    /// moments that nobody notices.
    ///
    /// AND MEASURING IT SOMETHING WORSE WAS FOUND: both spellings, on a
    /// steel cube of 200,000 mm³, answer **three zeros**. The true moments
    /// are in `Extension.GetMassProperties(1)`, positions 6, 7, 8. A field
    /// called «principal moments» that answers zero is not an error: it is
    /// a credible number, and no check would catch it. Here they are not
    /// reported at all.
    ///
    /// ── AND THE MEASUREMENT THAT DECIDED THE REFUSAL ────────────────────
    ///
    /// On the same cube of 200,000 mm³:
    ///
    ///   without material       density 1000   mass 0.2 kg
    ///   with AISI 304          density 8000   mass 1.6 kg
    ///
    /// Without material SOLIDWORKS **does not fail**: it answers with the
    /// default density of the model, which is that of water. The number is
    /// credible and wrong by EIGHT TIMES, and the place where it is discovered is the
    /// quote. Therefore here, without material, the mass is `null` with the
    /// reason written.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome MassProperties(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            // THE ASSEMBLY HAS ITS OWN ROUTE (entry Z-D1): CreateMassProperty on the whole
            // assembly, checked against the sum of the parts (OperazioniMisuraAssieme).
            if (model is IAssemblyDoc) return AssemblyMassProperties(model, a);
            var partDoc = model as IPartDoc;
            if (partDoc == null)
                return Failed("This document is neither a part nor an assembly: it has no bodies to weigh. Open " +
                               "the part or the assembly for me.");

            var bodies = ChosenBodies(partDoc, docState, a, Str(a, "body_uid", null), out string reason);
            if (bodies == null) return Failed(reason);

            // ── A NEW MEASUREMENT FOR EVERY NUMBER, AND IT IS NOT PEDANTRY ─
            //
            // `IMassProperty` **changes its answers according to what
            // it was asked before**. Measured on 15/9/2026 on a cube of
            // AISI 304 of 200,000 mm³, with the same object:
            //
            //   read `Density` right away                 ->  8000   (right)
            //   read `Volume` first, then `Density`       ->     1
            //   read `SurfaceArea` first, then `Density`  ->     1
            //   read `CenterOfMass` first, then `Density` -> 8000   (unharmed)
            //
            // And the mass follows the density: 0.0002 kg instead of 1.6 — EIGHT THOUSAND
            // TIMES lighter, with volume, area and centre of mass all right
            // alongside. No exception, no absurd value: a credible
            // number, and the place where it is discovered is the quote.
            //
            // One could write «read the mass first», but an order of
            // lines is a contract that breaks the day someone
            // reorders them — and nothing would fail. One object per number does not
            // break: it costs four COM calls, which nobody feels.
            var take = new Func<Func<IMassProperty2, object>, string, object>((read, what) =>
            {
                try
                {
                    // S2: a new object in system units of the bodies asked for.
                    // Since Z-C21.7 CreateMassProperty2 with the bodies as
                    // SelectedItems, hidden ones included, and Recalculate
                    // (ApiHelpers.NewPartMassProperty, with the probe numbers).
                    // Without the bodies read back the measure is not of the
                    // part one believes. The order trap above belongs to the
                    // obsolete IMassProperty: the probe found IMassProperty2
                    // free of it (O1: density 8000 read after Volume, O0 with
                    // the old object: 1). One object per number stays anyway.
                    var mp = ApiHelpers.NewPartMassProperty(model, bodies, out bool attached);
                    if (mp == null || !attached) return null;
                    return read(mp);
                }
                catch { return null; }
            });

            object m = take(p => p.Mass, "mass");
            object d = take(p => p.Density, "density");
            object v = take(p => p.Volume, "volume");
            object s = take(p => p.SurfaceArea, "area");
            object c = take(p => p.CenterOfMass, "center_of_mass");

            if (m == null || d == null || v == null || s == null)
                return Failed(
                    "SOLIDWORKS did not give the mass properties: either it did not pick up the " +
                    "bodies, or the measurement failed. I do not give you half numbers.");

            double massKg = Convert.ToDouble(m);
            double density = Convert.ToDouble(d);
            double volumeM3 = Convert.ToDouble(v);
            double areaM2 = Convert.ToDouble(s);
            var center = Scale(c as double[], 1000.0);

            string material = PartMaterial(model);

            var measurements = new Dictionary<string, object>
            {
                ["bodies"] = bodies.Count,
                ["volume_mm3"] = Math.Round(volumeM3 * 1e9, 2),
                ["area_mm2"] = Math.Round(areaM2 * 1e6, 2),
                ["center_of_mass_mm"] = center,
                // THE UNITS IN THE ANSWER, not only in the field names. It is the
                // same reason as the STL: whoever reads may never have seen
                // the description of the operation.
                ["units"] = "millimeters and kilograms; the density in kg/m3",
            };

            // THE MASS WITHOUT MATERIAL IS NOT ZERO, IT IS «NOT KNOWN».
            //
            // SOLIDWORKS answers all the same, with the default density of the
            // model — usually 1000 kg/m3, that is water. A steel part
            // that weighs like water is a credible number and wrong by eight
            // times, and the place where it is discovered is the quote.
            if (string.IsNullOrEmpty(material))
            {
                measurements["material"] = null;
                measurements["mass_kg"] = null;
                measurements["density_kg_m3"] = null;
                measurements["why_no_mass"] =
                    "There is no material set on the part, so the mass is not known. SOLIDWORKS " +
                    "would answer all the same using the default density of the model " +
                    "(usually 1000 kg/m3, that is water): a believable and wrong number, " +
                    "eight times too low on a steel part. Use «apply_material» and ask " +
                    "me again.";
            }
            else
            {
                measurements["material"] = material;
                measurements["mass_kg"] = Math.Round(massKg, 4);
                measurements["density_kg_m3"] = Math.Round(density, 2);
                // THE CALCULATION REDONE BY HAND, next to the one from SOLIDWORKS. If
                // the two diverge, one of the two measurements is in units different
                // from what it says — and it is the way a factor of 1000
                // shows itself instead of hiding.
                double byHand = volumeM3 * density;
                measurements["mass_recomputed_kg"] = Math.Round(byHand, 4);
                measurements["deviation_on_recompute"] = massKg > 0
                    ? (object)Math.Round(Math.Abs(byHand - massKg) / massKg, 6)
                    : null;
            }

            // ── THE PRINCIPAL MOMENTS ARE NOT THERE, AND IT IS MEASURED ───
            //
            // `IMassProperty.PrincipleMomentsOfInertia` answers **three zeros**
            // on a steel cube of 200,000 mm³ — and so does
            // `IMassProperty2.PrincipalMomentsOfInertia`, with the other spelling.
            // The true moments are there, but somewhere else:
            // `Extension.GetMassProperties(1)` returns thirteen numbers and the
            // moments are in positions 6, 7 and 8 (0.001387, 0.001387,
            // 0.002667 kg·m² on the same cube).
            //
            // Three zeros in a field called «principal moments» are the
            // defect that gives no error: a credible number — a null
            // moment is a moment — that no check would catch. So they are
            // NOT reported: nobody asked for them, and reporting them wrong
            // is worse than not having them. If one day they were needed, the route is
            // `GetMassProperties`, not the property.

            _log("[agent] mass properties: volume " +
                 (volumeM3 * 1e9).ToString("0.#") + " mm3" +
                 (string.IsNullOrEmpty(material) ? ", without material" : ", " + material +
                  ", " + massKg.ToString("0.###") + " kg"));
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// THE TRUE BOX OF THE PART, body by body and all together.
        ///
        /// `GetExtremePoint` six times, NOT `GetBodyBox`. `GetBodyBox` is
        /// declaredly APPROXIMATE for non-planar surfaces — it can
        /// be larger than the part — and an extent declared larger
        /// than the true one makes everything around it get sized wrong: the
        /// box to ship it in, the part that goes next to it, the pocket it
        /// must fit into.
        ///
        /// It is the same choice already made in `DocumentState`, and it is written
        /// in both places because it is the kind of thing that someone
        /// «simplifies» reading only one of the two.
        /// </summary>
        private Outcome BodyBoundingBox(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (model is IAssemblyDoc) return AssemblyBoundingBox(model, a);   // entry Z-D1
            var partDoc = model as IPartDoc;
            if (partDoc == null)
                return Failed("This document is neither a part nor an assembly: it has no bodies to measure. Open " +
                               "the part or the assembly for me.");

            string askedUid = Str(a, "body_uid", null);
            var bodies = ChosenBodies(partDoc, docState, a, askedUid, out string reason);
            if (bodies == null) return Failed(reason);
            if (bodies.Count == 0)
                return Failed("The part has no solid body to measure.");

            var list = new List<object>();
            double[] everything = null;
            for (int i = 0; i < bodies.Count; i++)
            {
                var box = BodyBox(bodies[i] as IBody2);
                if (box == null)
                    return Failed(
                        "I could not read the bounding box of the body " + (i + 1) +
                        ": GetExtremePoint did not answer.");
                list.Add(new Dictionary<string, object>
                {
                    // The body asked for keeps its own uid: numbering the list
                    // of one body said «C1» of the body «C2» (Z-C21.5, S5).
                    ["body"] = string.IsNullOrWhiteSpace(askedUid) ? "C" + (i + 1) : askedUid.Trim().ToUpperInvariant(),
                    ["from_mm"] = new[] { Round(box[0]), Round(box[1]), Round(box[2]) },
                    ["to_mm"] = new[] { Round(box[3]), Round(box[4]), Round(box[5]) },
                    ["bounding_box_mm"] = new[]
                    {
                        Round(box[3] - box[0]),
                        Round(box[4] - box[1]),
                        Round(box[5] - box[2]),
                    },
                });
                if (everything == null) everything = (double[])box.Clone();
                else
                {
                    for (int k = 0; k < 3; k++) everything[k] = Math.Min(everything[k], box[k]);
                    for (int k = 3; k < 6; k++) everything[k] = Math.Max(everything[k], box[k]);
                }
            }

            var measurements = new Dictionary<string, object>
            {
                ["bodies"] = list.ToArray(),
                ["from_mm"] = new[] { Round(everything[0]), Round(everything[1]), Round(everything[2]) },
                ["to_mm"] = new[] { Round(everything[3]), Round(everything[4]), Round(everything[5]) },
                ["bounding_box_mm"] = new[]
                {
                    Round(everything[3] - everything[0]),
                    Round(everything[4] - everything[1]),
                    Round(everything[5] - everything[2]),
                },
                ["units"] = "millimeters, in the coordinate system of the model",
                // THE DECLARED LIMIT: it is the AXIS-ALIGNED box. A
                // long part placed diagonally has a box much larger
                // than needed to pack it, and this measurement does not
                // say how to turn it.
                ["what_it_does_not_check"] =
                    "It is the box aligned with the axes of the model, not the smallest " +
                    "possible one: a part placed diagonally has a much bigger bounding " +
                    "box than the one it would need turned the right way.",
            };
            _log("[agent] bounding box: " +
                 string.Join(" x ", ((double[])measurements["bounding_box_mm"]).Select(x => x.ToString("0.##"))) +
                 " mm on " + bodies.Count + " bodies");
            return SucceededWithoutCreating(measurements);
        }

        // =================================================================
        // HOW MUCH MATERIAL REMAINS BETWEEN THESE TWO FACES  (entry GP5)
        // =================================================================
        //
        // It is the wall thickness question — «how much remains between the gasket
        // groove and the outer face?» — asked in a way that can
        // be checked: two NAMED faces and a number, instead of calibrating
        // a vector of ray intersections.
        //
        // ── THE TWO THINGS MEASURED BEFORE WRITING IT ───────────────────
        //
        // 1. **`ClosestDistance` returns METRES.** On a 40x40x10 plate, between
        //    the two large faces, it answers `0,01`. A wrong factor of a thousand
        //    would give 10 where the true value is 0.01 — and both read as
        //    plausible millimetres.
        //
        // 2. **The two points it returns are true**, and are exactly as far apart as
        //    it declares: [0, 0, 10] and [0, 0, 0] mm, zero deviation. It is
        //    the only independent check one has on the number, and here it is
        //    always done: if the value says one thing and its own points say another,
        //    the call has failed and answered all the same.
        // =================================================================

        private Outcome MinimumDistance(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string uidA = Str(a, "start_uid", null);
            string uidB = Str(a, "end_uid", null);
            if (string.IsNullOrWhiteSpace(uidA) || string.IsNullOrWhiteSpace(uidB))
                return Failed(
                    "I need two things to measure between: «start_uid» and «end_uid». " +
                    "The identifiers of faces and edges are in the state of the document.");
            if (string.Equals(uidA, uidB, StringComparison.OrdinalIgnoreCase))
                return Failed(
                    "«start_uid» and «end_uid» are the same thing (" + uidA + "): the distance " +
                    "of a face from itself is zero, and it is not a measurement.");

            string reason;
            object entA = EntityFromState(docState, uidA, out reason);
            if (entA == null) return Failed(reason);
            object entB = EntityFromState(docState, uidB, out reason);
            if (entB == null) return Failed(reason);

            double meters;
            object p1, p2;
            try { meters = model.ClosestDistance(entA, entB, out p1, out p2); }
            catch (Exception ex) { return Failed("The measurement did not succeed: " + ex.Message); }

            // «NO SOLUTION» IS SAID AS SUCH (Z-C21.8, finding L12-9).
            // IModelDoc2~ClosestDistance.html: «Distance in meters between Point1
            // and Point2; -1.0 if no solution». The check on the two points below
            // refuses it too (a distance between two points is never negative,
            // so the gap is at least a metre), but with a sentence about a
            // distance of «-1000 mm» contradicted by its own points, or about
            // points that are missing, which sends the reader to the wrong place.
            if (meters < 0)
                return Failed(
                    "SOLIDWORKS found no closest distance between «" + uidA + "» and «" + uidB +
                    "»: ClosestDistance answered " + meters.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    ", which its guide page gives as «no solution». Check that both are faces, edges or vertices " +
                    "of the model, and ask again with the identifiers read from the state now.");

            var pointA = p1 as double[];
            var pointB = p2 as double[];
            if (pointA == null || pointB == null || pointA.Length < 3 || pointB.Length < 3)
                return Failed(
                    "SOLIDWORKS gave the distance (" + (meters * 1000).ToString("0.###") +
                    " mm) but not the two points between which it measured it. Without those the number " +
                    "cannot be checked, and I do not give it to you.");

            // THE INDEPENDENT CHECK, and it is not a scruple: if the value
            // says 0 and the points are 4 mm apart, the call has failed and
            // answered all the same.
            double betweenThePoints = Math.Sqrt(
                Math.Pow(pointA[0] - pointB[0], 2) +
                Math.Pow(pointA[1] - pointB[1], 2) +
                Math.Pow(pointA[2] - pointB[2], 2));
            double deviationMm = Math.Abs(betweenThePoints - meters) * 1000.0;
            if (deviationMm > 0.001)
                return Failed(
                    "SOLIDWORKS says the distance is " + (meters * 1000).ToString("0.####") +
                    " mm, but the distance between the two points it gave is " +
                    (betweenThePoints * 1000).ToString("0.####") + ". The number is contradicted by its own " +
                    "points: I do not give it to you.");

            var measurements = new Dictionary<string, object>
            {
                ["source"] = uidA,
                ["target"] = uidB,
                ["distance_mm"] = Round(meters * 1000.0),
                ["point_source_mm"] = Scale(pointA, 1000.0),
                ["point_target_mm"] = Scale(pointB, 1000.0),
                // THE CHECK IS REPORTED TOO, not only its outcome: whoever
                // reads sees that the number was verified and with what
                // deviation, instead of having to believe it.
                ["distance_between_points_mm"] = Round(betweenThePoints * 1000.0),
                ["deviation_of_check_mm"] = Round(deviationMm),
                ["units"] = "millimeters; SOLIDWORKS gives them in meters and the conversion is here",
            };
            if (meters * 1000.0 < 0.001)
                measurements["touch"] =
                    "The two things touch or intersect: the distance is zero, and the two " +
                    "points are the same point.";

            _log("[agent] minimum distance " + uidA + " -> " + uidB + ": " +
                 (meters * 1000).ToString("0.###") + " mm");
            return SucceededWithoutCreating(measurements);
        }

        // =================================================================
        // THE FULL MEASUREMENT BETWEEN TWO ENTITIES  (entry GP5)
        // =================================================================
        //
        // ── `Calculate(entita)` IGNORES THE ARGUMENT ────────────────────
        //
        // `IMeasure.Calculate(Object Entities)` **takes an argument and does not
        // look at it**: it measures what is SELECTED. Measured on 15/9/2026:
        //
        //   Calculate(new object[] { faceA, faceB })   ->  FALSE
        //   two faces selected, then Calculate(null)       ->  TRUE, 0.01
        //
        // And worse: called with ONE face in the array, while the selection
        // still held TWO from the previous test, it answered TRUE and
        // gave the area of both (0.0032 m² instead of 0.0016). A credible
        // number, of the right part, of another selection.
        //
        // So: the selection is cleared, the entities are selected by hand,
        // `Calculate(null)` is called and **the boolean is looked at**.
        //
        // ── AND `-1` IS NOT A MEASUREMENT ───────────────────────────────
        //
        // `Perimeter` on a square planar face answers **-1**: it is
        // SOLIDWORKS's way of saying «this thing does not have it». A
        // perimeter negative by a thousandth of a millimetre reported as a number is the
        // defect that gives no error; here the negative values become `null`.
        // =================================================================

        private Outcome MeasureBetween(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var uid = new List<string>();
            foreach (var key in new[] { "uid", "uid2" })
            {
                string u = Str(a, key, null);
                if (!string.IsNullOrWhiteSpace(u)) uid.Add(u);
            }
            if (uid.Count == 0)
                return Failed(
                    "I need at least one thing to measure: «uid» (and «uid2» to measure BETWEEN two). " +
                    "The identifiers of faces and edges are in the state of the " +
                    "document.");
            if (uid.Count > 2)
                return Failed(
                    "You gave " + uid.Count + ": I measure one or two. With more than " +
                    "two, «distance» and «fillet» no longer mean anything.");

            string arcMeasure = (Str(a, "arc_measure", "centre") ?? "centre").Trim().ToLowerInvariant();
            int arcOption;
            if (!ArcMeasureOptions.TryGetValue(arcMeasure, out arcOption))
                return Failed(
                    "«arc_measure» is «centre», «minimum» or «maximum», not «" + arcMeasure + "»: it says how " +
                    "a distance to an arc or a circle is taken (centre to centre, between the nearest points, " +
                    "between the farthest points).");

            var entity = new List<object>();
            foreach (var u in uid)
            {
                string reason;
                var e = EntityFromState(docState, u, out reason);
                if (e == null) return Failed(reason);
                entity.Add(e);
            }

            IMeasure measure;
            try { measure = model.Extension.CreateMeasure() as IMeasure; }
            catch (Exception ex) { return Failed("I cannot prepare the measurement: " + ex.Message); }
            if (measure == null)
                return Failed("This installation does not expose the measurement tool.");

            // THE ARC OPTION IS SET, NEVER INHERITED (finding M29). Measured on
            // 28/9/2026 by probe_measure_arc_option (plate 100 x 60 x 10, holes
            // D10 at x = -25 and D20 at x = +25): between the
            // two cylindrical faces Distance is 50 with ArcOption 0, 35 with 1 and
            // 65 with 2, while CenterDistance stays 50; a fresh IMeasure reads 0/0
            // and the option set on one measure does not stick to the next. Up to
            // library 0.141.0 it was never set: the distance was centre to centre
            // and nothing said so. IMeasure~ArcOption.html: "How to measure an arc
            // or circle as defined by swMeasureArcCircleOption_e"; the guide
            // example (Measure_Selected_Entities_Example_CSharp.htm) sets it before
            // Calculate. ProjectionOption goes to 0 (swMeasureProjectOn_None,
            // IMeasure~ProjectionOption.html) for the same reason: the default
            // measured 0, and the same distance, but it is ours to say.
            try
            {
                measure.ArcOption = arcOption;
                measure.ProjectionOption = (int)swMeasureProjectOnOption_e.swMeasureProjectOn_None;
            }
            catch (Exception ex) { return Failed("The measure did not take the arc option: " + ex.Message); }

            bool succeeded;
            try
            {
                // THE SELECTION IS THE REAL ARGUMENT. See the comment above.
                model.ClearSelection2(true);
                for (int i = 0; i < entity.Count; i++)
                {
                    var ent = entity[i] as IEntity;
                    if (ent == null || !ent.Select4(i > 0, null))
                        return Failed(
                            "I could not select «" + uid[i] + "»: without the " +
                            "selection the measurement would look at something else.");
                }
                succeeded = measure.Calculate(null);
            }
            catch (Exception ex) { return Failed("The measurement did not succeed: " + ex.Message); }
            finally { try { model.ClearSelection2(true); } catch { } }

            if (!succeeded)
                return Failed(
                    "The measurement tool answered «no» on " +
                    string.Join(" and ", uid.Select(u => "«" + u + "»")) +
                    ". I do not give you the numbers it had inside: they would be the old ones.");

            var outside = new Dictionary<string, object>
            {
                ["measured"] = uid.ToArray(),
                ["units"] = "millimeters, square millimeters and degrees; SOLIDWORKS gives them in " +
                            "meters and radians, and the conversion is here",
            };
            // ── `Area` AND `TotalArea` ARE NOT THE SAME THING, AND THEY ALTERNATE
            //
            // Measured on 15/9/2026 on a 40x40x10 plate:
            //
            //   a single face      Area = 1600 mm²    TotalArea = -1
            //   two faces          Area = -1          TotalArea = 3200 mm²
            //
            // That is the one that holds is always the other. Whoever reads only one
            // has a field that vanishes depending on how many things were asked for —
            // and it is what happened: the case «area di una faccia sola»
            // came out with «campo assente: misure.area_mm2». Both are reported,
            // with the name that says which is which.
            Put(outside, "area_mm2", measure.Area, 1e6);
            Put(outside, "area_total_mm2", measure.TotalArea, 1e6);
            Put(outside, "perimeter_mm", measure.Perimeter, 1000.0);
            Put(outside, "length_mm", measure.TotalLength, 1000.0);
            Put(outside, "radius_mm", measure.Radius, 1000.0);
            Put(outside, "diameter_mm", measure.Diameter, 1000.0);
            if (uid.Count == 2)
            {
                // Read back after Calculate: the probe read the value it had set.
                // If SOLIDWORKS answered with another option, «distance_mm» would
                // be another kind of distance under the name the caller asked for.
                int arcOptionRead = measure.ArcOption;
                if (arcOptionRead != arcOption)
                    return Failed(
                        "I asked the measure for «arc_measure» " + arcMeasure + " (ArcOption " + arcOption +
                        ") and it reads back ArcOption " + arcOptionRead + ": the distance would be another " +
                        "kind of distance, and I do not give it.");
                outside["arc_measure"] = arcMeasure;
                outside["projection"] = "none";
                Put(outside, "distance_mm", measure.Distance, 1000.0);
                Put(outside, "normal_distance_mm", measure.NormalDistance, 1000.0);
                Put(outside, "distance_between_centers_mm", measure.CenterDistance, 1000.0);
                // THE DELTAS CAN BE TRULY NEGATIVE: a delta is a
                // difference, and -20 means «twenty on the other side». Here
                // `-1` is not a signal, so it is not filtered.
                outside["delta_mm"] = new[]
                {
                    Round(measure.DeltaX * 1000.0),
                    Round(measure.DeltaY * 1000.0),
                    Round(measure.DeltaZ * 1000.0),
                };
                outside["parallel"] = measure.IsParallel;
                outside["perpendicular"] = measure.IsPerpendicular;
                outside["intersect"] = measure.IsIntersect;
                double deg = measure.Angle * 180.0 / Math.PI;
                if (deg >= 0) outside["angle_deg"] = Round(deg);
            }

            _log("[agent] measurement between " + string.Join(" and ", uid) +
                 (outside.ContainsKey("distance_mm") ? ": " + outside["distance_mm"] + " mm" : ""));
            return SucceededWithoutCreating(outside);
        }

        /// <summary>
        /// The values of «arc_measure» and the IMeasure.ArcOption each one sets
        /// (swMeasureArcCircleOption_e: CenterToCenter 0, MinimumDistance 1,
        /// MaximumDistance 2). «centre» is the default because it is what the
        /// measure did before the option was set (finding M29).
        /// </summary>
        private static readonly Dictionary<string, int> ArcMeasureOptions = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["centre"] = (int)swMeasureArcCircleOption_e.swMeasureArcCircle_CenterToCenter,
            ["minimum"] = (int)swMeasureArcCircleOption_e.swMeasureArcCircle_MinimumDistance,
            ["maximum"] = (int)swMeasureArcCircleOption_e.swMeasureArcCircle_MaximumDistance,
        };

        /// <summary>
        /// Puts a value into the measurements **only if it is a measurement**.
        ///
        /// `IMeasure` answers `-1` when the thing asked does not apply — the
        /// perimeter of a face that has none, the radius of a plane — and a
        /// `-1` reported as a number is the defect that gives no error:
        /// none of these quantities can be negative, so a negative
        /// value is an absence in disguise.
        /// </summary>
        private static void Put(Dictionary<string, object> location, string itemName,
                                  double val, double factor)
        {
            if (double.IsNaN(val) || val < 0) return;
            location[itemName] = Round(val * factor);
        }

        /// <summary>
        /// A face or an edge, taken from the state by identifier.
        /// The refusal says what type that uid was, if there was one: «E3 is
        /// an edge» is a help, «I cannot find it» is not.
        /// </summary>
        private static object EntityFromState(DocumentState docState, string uid, out string reason)
        {
            reason = null;
            if (docState == null)
            {
                reason = "I do not have the state of the document: without it, the " +
                         "identifiers mean nothing.";
                return null;
            }
            DocumentState.EntitySignature signature;
            if (!docState.Signatures.TryGetValue(uid, out signature))
            {
                reason = "«" + uid + "» is not in this document. The identifiers of faces " +
                         "and edges are in the state, and they are renumbered at every " +
                         "reading: read the part again before naming them.";
                return null;
            }
            if (signature.Type != "FACE" && signature.Type != "EDGE")
            {
                reason = "«" + uid + "» is of type «" + signature.Type + "»: I can measure " +
                         "faces and edges.";
                return null;
            }
            if (signature.Entity == null)
            {
                reason = "«" + uid + "» is no longer usable: read the part again.";
                return null;
            }
            return signature.Entity;
        }

        /// <summary>
        /// The bodies to measure: all of them, or the one asked for by
        /// identifier. Null (with a reason) when the identifier does not
        /// exist: an empty list would give an answer with numbers, and
        /// numbers about nothing are worse than a refusal.
        ///
        /// A BODY ASKED FOR IS RESOLVED THROUGH THE DOCUMENT STATE AND ITS
        /// SIGNATURE (Z-C21.5, scheme S5), as combine_bodies and the overhang check (OperazioniSporgenze) do:
        /// <see cref="BodyAskedFor"/>. Until then «C2» was the second body of a
        /// GetBodies2 made now (IPartDoc~GetBodies2.html, Remarks: «May vary the
        /// order in which bodies are returned»), and a body named before a cut
        /// that renumbers the bodies was measured in the place of another one,
        /// with plausible numbers. body_bounding_box and mass_properties both
        /// come through here.
        /// </summary>
        private List<object> ChosenBodies(
            IPartDoc partDoc, DocumentState docState, Dictionary<string, object> a, string uid, out string reason)
        {
            reason = null;
            var outside = new List<object>();
            var rawItems = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (rawItems == null || rawItems.Length == 0)
            {
                reason = "The part has no solid body. First make an extrusion.";
                return null;
            }
            if (!string.IsNullOrWhiteSpace(uid))
            {
                var asked = BodyAskedFor(docState, a, uid.Trim(), out reason);
                if (asked == null) return null;
                outside.Add(asked);
                return outside;
            }
            for (int i = 0; i < rawItems.Length; i++)
            {
                if (rawItems[i] == null) continue;
                // THE OBJECT IS KEPT AS IT CAME OUT of `GetBodies2`, not
                // recast: it is what `AddBodies` attaches. See the
                // comment next to `AddBodies`.
                outside.Add(rawItems[i]);
            }
            if (outside.Count == 0)
            {
                reason = "The part answered GetBodies2 with no readable solid body: there is nothing to measure.";
                return null;
            }
            return outside;
        }

        /// <summary>
        /// THE BODY A «body_uid» NAMES, through the document state and its
        /// signature (Z-C21.5, scheme S5): <c>BodiesFromUid</c>, which calls
        /// SignatureMatches, the helper combine_bodies and OperazioniSporgenze already use.
        /// Null with <paramref name="reason"/> when the uid is not a solid body
        /// of the state (the words of the old refusal, which the bench cases
        /// read), or when it no longer names the body the plan looked at (a cut
        /// renumbered the bodies, E4).
        ///
        /// The Z-C21.3 probe of L12-3 found the solids in the same order in its
        /// four parts: that is a tolerance for the bench, not a licence to
        /// resolve a uid by its position (the lesson of the wrong number that
        /// looks plausible). body_bounding_box, mass_properties and
        /// check_body_geometry come through here.
        /// </summary>
        private IBody2 BodyAskedFor(DocumentState docState, Dictionary<string, object> a, string uid, out string reason)
        {
            reason = null;
            DocumentState.EntitySignature signature;
            if (docState == null || !docState.Signatures.TryGetValue(uid, out signature) || signature.Type != "BODY")
            {
                reason = "I cannot find the body «" + uid + "». The identifiers of the bodies " +
                         "are in the state of the document, inside «bodies».";
                return null;
            }
            var bodies = BodiesFromUid(docState, new List<string> { uid }, a, out string why);
            if (bodies == null || bodies.Count != 1)
            {
                reason = "«body_uid» «" + uid + "» no longer names the body the plan looked at, and I do not " +
                         "measure another one in its place: " + why;
                return null;
            }
            return bodies[0];
        }

        /// <summary>
        /// The box of a body in mm: {minX, minY, minZ, maxX, maxY, maxZ}.
        /// Null if `GetExtremePoint` does not answer — and then it fails,
        /// instead of falling back on `GetBodyBox` and passing off an approximate
        /// number as good without saying so.
        /// </summary>
        private static double[] BodyBox(IBody2 body)
        {
            try
            {
                var box = ApiHelpers.TrueBoundingBox(body);
                if (box == null) return null;
                var outside = new double[6];
                for (int i = 0; i < 6; i++) outside[i] = Units.Mm(box[i]);
                return outside;
            }
            catch { return null; }
        }

        private static string PartMaterial(IModelDoc2 model, string configuration = "")
        {
            try
            {
                string database;
                var partDoc = model as IPartDoc;
                if (partDoc == null) return null;
                // The empty string means the active configuration (measured by
                // Z-C21.3, L17-1), as `DocumentState` asks for it. Since M10
                // `apply_material` sets and reads back every configuration by
                // name instead. The material is PER CONFIGURATION: in an assembly
                // the configuration the component uses is asked.
                string itemName = partDoc.GetMaterialPropertyName2(configuration ?? "", out database);
                return string.IsNullOrWhiteSpace(itemName) ? null : itemName;
            }
            catch { return null; }
        }

        private static double[] Scale(double[] v, double factor)
        {
            if (v == null || v.Length < 3) return null;
            return new[] { Round(v[0] * factor), Round(v[1] * factor), Round(v[2] * factor) };
        }

        private static double Round(double x)
        {
            return Math.Round(x, 4);
        }
    }
}
