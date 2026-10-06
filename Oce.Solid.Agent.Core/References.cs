using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// Translates «the face on top of what you did at step 1» into the
    /// real face, inside SolidWorks, after step 1 has been executed.
    ///
    /// IT IS NEEDED BECAUSE A PLAN IS WRITTEN ENTIRELY BEFORE ANY PIECE OF IT IS EXECUTED.
    /// At the moment when the model proposes step 2, the face the
    /// hole goes on does not exist: it has no identifier to cite, and nobody
    /// can give it one. The reference is the name that can be given to something
    /// that is not there yet; this file is the place where that name becomes
    /// a face with an area.
    ///
    /// HERE NOTHING IS EVER GUESSED. If a kind does not resolve unambiguously —
    /// two cylindrical faces where one was needed, no planar face
    /// on top, a through pocket of which the bottom is asked for — the answer is a
    /// written reason, and the step fails. The «most likely» face is
    /// exactly the kind of choice that here produces a wrong part without
    /// any error: the feature succeeds, the rebuild is clean, and the hole
    /// is on the other side.
    /// </summary>
    public static class References
    {
        /// <summary>Cosine beyond which two directions are called the same.</summary>
        /// <remarks>
        /// 0.999 is about 2.5 degrees. It is not a measurement tolerance: the faces
        /// of an extrusion are exactly parallel or exactly
        /// perpendicular to the axis, and this margin only serves to cover
        /// the representation error of the doubles.
        /// </remarks>
        private const double Cosine = 0.999;

        /// <summary>What an already executed step left behind it.</summary>
        public class Step
        {
            /// <summary>The feature created by that step.</summary>
            public IFeature Feature;

            /// <summary>
            /// The direction along which that step extruded or cut, in
            /// model coordinates. Null if the step has none (a
            /// fillet does not extrude in any direction).
            ///
            /// It is not derived from the geometry and no attempt is made: for a
            /// cuboid the six normals are +-X, +-Y, +-Z and there is
            /// nothing that tells the extrusion axis from the other two. Whoever
            /// executes the operation KNOWS the direction — it is the normal of the sketch
            /// plane, or the normal of the face being drilled — and writes it here.
            /// </summary>
            public double[] Direction;

            /// <summary>Name of the operation, for the error messages.</summary>
            public string Operation = "";
        }

        /// <summary>The normal of a reference plane, in model coordinates.</summary>
        /// <remarks>
        /// The canonical normal of each base plane, and since Z-C21.5 also the
        /// rule that FINDS the plane (<see cref="BasePlane"/>): the plane of
        /// that name is the reference plane with this normal, same sign,
        /// through the world origin. Not the first, second or third RefPlane of
        /// the tree, and not a name (the names are localized). Measured on the
        /// template by the Z-C21.3 probe of C3-6: front (0, 0, 1), top
        /// (0, 1, 0), right (1, 0, 0).
        /// </remarks>
        public static double[] PlaneNormal(string plane)
        {
            switch (plane)
            {
                case "top": return new double[] { 0, 1, 0 };
                case "right": return new double[] { 1, 0, 0 };
                default: return new double[] { 0, 0, 1 }; // front
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // The three base planes, found by their geometry (scheme S19)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The cosine above which a plane's normal is the canonical one. The
        /// base planes read (0, 0, 1), (0, 1, 0) and (1, 0, 0) exactly (probe of
        /// C3-6), so this only absorbs the representation error of doubles: a
        /// plane turned by a tenth of a degree is not a base plane.
        /// </summary>
        private const double BasePlaneCosine = 0.999999;

        /// <summary>
        /// How far from the world origin, in metres, a base plane may pass. The
        /// three base planes pass through it exactly (probe of C3-6: 0 mm), an
        /// offset plane parallel to one of them does not (20 mm in the probe).
        /// </summary>
        private const double BasePlaneOffsetM = 1e-8;

        /// <summary>
        /// ONE OF THE THREE BASE PLANES, RECOGNISED BY ITS GEOMETRY (scheme S19:
        /// findings L10-6, C3-6 and C3-6 extended; Z-C21.5).
        ///
        /// Until Z-C21.5 the bridge took «frontale / superiore / destro» as the
        /// first / second / third RefPlane of the tree, in four places
        /// (FindBasePlane, SelectPlane, the section of PartView and
        /// mirror_component). IPartDoc~IFirstFeature.html: «This method
        /// returns features in the current order for the part, and this order
        /// changes when the part is edited»; the same page rules out the names
        /// too («you cannot assume that the first reference plane feature is
        /// named Plane1»). A part template with one plane more before the three
        /// default ones moved every sketch, offset plane, mirror and section of
        /// the catalogue onto the wrong plane, with no error.
        ///
        /// THE RULE is in <see cref="BasePlaneIndex"/>: the plane whose normal
        /// is the canonical one of that name (<see cref="PlaneNormal"/>),
        /// with the same sign, and which passes through the world origin. The
        /// normal and the origin come from IRefPlane.Transform
        /// (<see cref="PlaneFrame"/>).
        ///
        /// Parts and assemblies alike (mirror_component mirrors on the
        /// assembly planes). The probe of C3-6 read the frames on a PART; on an
        /// assembly the reading is not probed, and the bench cases of
        /// mirror_component (right_bracket_and_left_bracket,
        /// the_mirrored_instance_turns_as_asked) mirror on the assembly's Right,
        /// Top and Front planes: they are the check. An unknown name, or no
        /// plane passing the rule, is null with <paramref name="reason"/> saying
        /// which normal was looked for and how many planes were read.
        /// </summary>
        public static IFeature BasePlane(IModelDoc2 model, string name, out string reason)
        {
            reason = null;
            if (Array.IndexOf(BasePlaneNames, name) < 0)
            {
                reason = "«" + name + "» is not a base plane: they are " + string.Join(", ", BasePlaneNames) + ".";
                return null;
            }
            if (model == null) { reason = "There is no document."; return null; }

            var features = new List<IFeature>();
            var normals = new List<double[]>();
            var origins = new List<double[]>();
            int unread = 0;
            try
            {
                var feature = model.FirstFeature() as IFeature;
                while (feature != null)
                {
                    string type = "";
                    try { type = feature.GetTypeName2(); } catch { }
                    if (type == "RefPlane")
                    {
                        IRefPlane plane = null;
                        try { plane = feature.GetSpecificFeature2() as IRefPlane; } catch { }
                        double[] normal, origin;
                        if (!PlaneFrame(plane, out normal, out origin)) unread++;
                        features.Add(feature);
                        normals.Add(normal);
                        origins.Add(origin);
                    }
                    feature = feature.GetNextFeature() as IFeature;
                }
            }
            catch (Exception ex)
            {
                reason = "The feature tree could not be read to the end (" + ex.Message + "), so the «" + name +
                         "» plane was not looked for among all the planes.";
                return null;
            }

            int index = BasePlaneIndex(normals, origins, name);
            if (index >= 0) return features[index];
            var wanted = PlaneNormal(name);
            reason = "no reference plane of this document has the normal (" + string.Join(", ", wanted) +
                     ") and passes through the origin, which is what the «" + name + "» plane is (" +
                     features.Count + " reference planes read" +
                     (unread > 0 ? ", " + unread + " of them without a readable transform" : "") + ").";
            return null;
        }

        /// <summary>The names of the three base planes, in the catalogue's spelling.</summary>
        public static readonly string[] BasePlaneNames = { "front", "top", "right" };

        /// <summary>
        /// THE DECISION OF <see cref="BasePlane"/>, WITH NO INTEROP: which of the
        /// reference planes, given in tree order by their unit normal and their
        /// origin in metres (null = not read), is the base plane of that name.
        /// -1 for an unknown name or when none qualifies. Pure, so that
        /// Oce.Solid.Agent.ContractTests checks it without SOLIDWORKS (Zc215IdentityContract).
        ///
        /// A plane qualifies with the canonical normal of the name, SAME SIGN,
        /// and passing through the world origin. Measured by the Z-C21.3 probe of
        /// C3-6 (28/9/2026, SOLIDWORKS 29.5.1) on the template: «Piano frontale»
        /// normal (0, 0, 1), «Piano superiore» (0, 1, 0), «Piano destro»
        /// (1, 0, 0), all three with origin (0, 0, 0); a plane offset 20 mm from
        /// the front read (0, 0, 1) through (0, 0, 20), and a plane through three
        /// points at y = 30 read (0, -1, 0): neither qualifies. The sign is
        /// required because the side of an offset (OptionFlip), the direction of
        /// an extrusion and the orientation of a sketch follow the normal: a
        /// plane through the origin facing -Y would turn the work on the top
        /// plane upside down.
        ///
        /// TWO PLANES QUALIFY only when a second plane coincides with a base
        /// plane, normal and sign included. The first in the tree is taken: the
        /// probe found every ReorderFeature that tried to put a plane or a sketch
        /// before the base planes refused (its cases P1 and P2), so the first is
        /// the template's own, and for a sketch, an offset, a mirror or a section
        /// the two are the same plane. It is the only use of the tree order
        /// left, and only between planes the geometry cannot tell apart.
        /// </summary>
        public static int BasePlaneIndex(IList<double[]> normals, IList<double[]> origins, string name)
        {
            if (normals == null || origins == null || Array.IndexOf(BasePlaneNames, name) < 0) return -1;
            var wanted = PlaneNormal(name);
            for (int i = 0; i < normals.Count && i < origins.Count; i++)
            {
                var normal = normals[i];
                var origin = origins[i];
                if (normal == null || origin == null || normal.Length < 3 || origin.Length < 3) continue;
                if (Product(normal, wanted) < BasePlaneCosine) continue;
                if (Math.Abs(Product(normal, origin)) > BasePlaneOffsetM) continue;
                return i;
            }
            return -1;
        }

        /// <summary>
        /// The unit normal and the origin (metres) of a reference plane, in
        /// world coordinates, from IRefPlane.Transform. False when unreadable.
        ///
        /// IRefPlane~Transform.html: «Gets the plane transform with respect to
        /// the world»; its example (Get_the_Normal_and_Origin_of_a_Reference_
        /// Plane_Using_Its_Transform_Example_VB.htm) takes the normal as
        /// (0, 0, 1) and the origin as (0, 0, 0) through the transform, the
        /// canonical position being the Front Plane. IMathTransform~ArrayData.html:
        /// «The first 9 elements define the 3x3 rotation matrix. The next 3
        /// elements define the translation component». So the NORMAL IS THE
        /// THIRD ROW (indices 6, 7, 8), not the third column, measured on
        /// 16/9/2026 on an oblique plane where the column gives a turned vector
        /// (plane_three_points, OperazioniPiani); the ORIGIN is the translation
        /// (indices 9, 10, 11), which is what the bench reads as
        /// «last_plane_mm» (top_plane_plus_100: [0, 100, 0]).
        /// </summary>
        public static bool PlaneFrame(IRefPlane plane, out double[] normal, out double[] origin)
        {
            normal = null;
            origin = null;
            if (plane == null) return false;
            try
            {
                var transform = plane.Transform as IMathTransform;
                var data = transform == null ? null : transform.ArrayData as double[];
                if (data == null || data.Length < 12) return false;
                double length = Math.Sqrt(data[6] * data[6] + data[7] * data[7] + data[8] * data[8]);
                if (length < 1e-9) return false;
                normal = new[] { data[6] / length, data[7] / length, data[8] / length };
                origin = new[] { data[9], data[10], data[11] };
                return true;
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────────────────
        // Resolution
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The face indicated by a kind of the «face» family.
        /// Returns false with a readable reason if there is not exactly one.
        /// </summary>
        public static bool Face(
            Step planStep, string kind, out IFace2 face, out string reason)
        {
            face = null;
            reason = null;
            var faces = FacesOf(planStep, out reason);
            if (faces == null) return false;

            switch (kind)
            {
                case "top_face":
                    return End(planStep, faces, +1, out face, out reason);
                case "bottom_face":
                    return End(planStep, faces, -1, out face, out reason);

                case "hole_face":
                {
                    // ── A HOLE IS CONCAVE, A FILLET IS CONVEX ───────
                    //
                    // Up to 0.73.0 «cylindrical» was enough here, and a fillet
                    // leaves cylindrical faces like a hole. If a step
                    // left **only one** and it was a fillet, this
                    // branch returned it **without saying anything**: whoever had
                    // asked for the wall of a hole found the surface
                    // of a fillet, and the following feature ended up
                    // on it. A defect that gives no error.
                    //
                    // Measured by `Checks\HoleOrFillet` on a plate
                    // with a Ø12 hole and an R5 fillet:
                    //
                    //     hole wall               FaceInSurfaceSense = true
                    //     fillet surface          FaceInSurfaceSense = false
                    //
                    // The wall of a hole has the material OUTSIDE the cylinder,
                    // and its normal looks toward the axis: it is concave.
                    var concave = new List<IFace2>();
                    int convex = 0;
                    foreach (var f in faces)
                    {
                        if (!Cylindrical(f)) continue;
                        if (Concave(f)) concave.Add(f); else convex++;
                    }

                    if (concave.Count == 0)
                    {
                        reason = convex > 0
                            ? "The step «" + planStep.Operation + "» left " +
                              convex + " cylindrical " + (convex == 1 ? "face" : "faces") +
                              ", but convex" + (convex == 1 ? ": it is" : ": they are") + " " +
                              (convex == 1 ? "a fillet surface or part of a solid cylinder" : "fillet surfaces or parts of solid cylinders") +
                              ", " +
                              "not hole walls. The wall of a hole is " +
                              "concave — " +
                              "the material is outside."
                            : "The step «" + planStep.Operation + "» left no " +
                              "cylindrical face: there is no hole " +
                              "wall to take.";
                        return false;
                    }

                    // MORE THAN ONE HOLE WALL REMAINS A REFUSAL, and it is not a
                    // lack to be repaired.
                    //
                    // Entry E14 proposed choosing one «with a criterion —
                    // the diameter, or the position». Among four identical M10 there
                    // is no criterion: whatever is chosen is
                    // **guessing**, and the following step would end up on a hole at
                    // random without anything failing. It is exactly the defect
                    // that this project exists to make impossible, offered
                    // as a convenience.
                    //
                    // The refusal however now says how many there are and of what
                    // diameter: whoever reads already has in hand what is needed to
                    // name one.
                    if (concave.Count > 1)
                    {
                        reason =
                            "Step «" + planStep.Operation + "» left " +
                            concave.Count + " hole walls (" + Diameters(concave) +
                            "): «the» hole wall does not exist. Read the part again and " +
                            "name the one you mean, by identifier.";
                        return false;
                    }
                    face = concave[0];
                    return true;
                }

                case "pocket_floor":
                {
                    // The bottom of a pocket is the planar face perpendicular
                    // to the cutting direction. If the pocket is through it does not
                    // exist, and saying so is the only correct answer: the «most
                    // similar» face would be a side wall, and the
                    // following feature would end up sideways.
                    if (planStep.Direction == null)
                    {
                        reason =
                            "Of the step «" + planStep.Operation + "» I do not know " +
                            "the direction of the cut, so I cannot tell which is the bottom.";
                        return false;
                    }
                    var planar = new List<IFace2>();
                    foreach (var f in faces)
                    {
                        var n = Normal(f);
                        if (n != null && Math.Abs(Product(n, planStep.Direction)) > Cosine)
                            planar.Add(f);
                    }
                    if (planar.Count != 1)
                    {
                        reason = planar.Count == 0
                            ? "The pocket of the step «" + planStep.Operation + "» has no " +
                              "bottom: it is through."
                            : "The pocket of the step «" + planStep.Operation + "» has " +
                              planar.Count + " faces that could be the bottom: " +
                              "name the face by its identifier.";
                        return false;
                    }
                    face = planar[0];
                    return true;
                }

                default:
                    reason = "Unknown face kind: «" + kind + "».";
                    return false;
            }
        }

        /// <summary>
        /// The edges indicated by a kind of the «edges» family.
        ///
        /// The edges are NOT deduplicated, and it is intended: an edge belongs
        /// to two faces, so going through the faces it is met twice.
        /// Selecting it twice in SolidWorks does not add it twice, and
        /// comparing the identity of two COM objects to avoid it would be more
        /// fragile than the problem it solves. The TRUE number of edges is
        /// asked afterwards of SolidWorks, with GetSelectedObjectCount2.
        /// </summary>
        public static bool Edges(
            Step planStep, string kind, out List<IEntity> edges, out string reason)
        {
            edges = new List<IEntity>();
            reason = null;
            var faces = FacesOf(planStep, out reason);
            if (faces == null) return false;

            if (kind == "top_edges" || kind == "bottom_edges")
            {
                IFace2 top;
                if (!End(planStep, faces, kind == "top_edges" ? +1 : -1,
                             out top, out reason))
                    return false;

                // ONLY THE OUTER LOOP. IFace2.GetEdges() returns the
                // edges of ALL the loops, and a top face may have
                // more than one: it is enough that a previous step made a
                // hole in it, or that the profile is hollow.
                //
                // The defect this closes: «100x60x8 plate, a 12 hole
                // in the centre, round the top edges with R2». The
                // panel showed «fillet R2 on top edges of
                // step 1» — exactly what had been asked — and
                // the edges of the hole were filleted too. On an 8
                // plate the useful cylindrical part dropped from 8 to 6 mm and the
                // screw head no longer seated. No error, clean
                // rebuild, step «succeeded». And the very same text with the
                // steps in reverse order gave the right part.
                if (!OuterBorderEdges(top, edges, out reason)) return false;
            }
            else if (kind == "side_edges")
            {
                if (planStep.Direction == null)
                {
                    reason =
                        "Of the step «" + planStep.Operation + "» I do not know " +
                        "the direction of extrusion, so I cannot tell which " +
                        "edges are the lateral ones.";
                    return false;
                }
                // An edge is «lateral» if it runs along the direction of
                // extrusion. Its direction is looked at, not which face it
                // belongs to: this way there is no need to compare face identities.
                foreach (var f in faces)
                {
                    var list = new List<IEntity>();
                    AddEdges(f, list);
                    foreach (var e in list)
                    {
                        var d = EdgeDirection(e as IEdge);
                        if (d != null && Math.Abs(Product(d, planStep.Direction)) > Cosine)
                            edges.Add(e);
                    }
                }
            }
            else if (kind == "all_edges")
            {
                foreach (var f in faces) AddEdges(f, edges);
            }
            else
            {
                reason = "Unknown edge kind: «" + kind + "».";
                return false;
            }

            if (edges.Count == 0)
            {
                reason =
                    "The step «" + planStep.Operation + "» left no edge " +
                    "of the kind «" + kind + "».";
                return false;
            }
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // Weldments: the bodies and the ends of a step
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The BODIES created by a step — the bars of a group of structural
        /// members.
        ///
        /// WHY WE GO THROUGH THE FACES AND DO NOT ASK FOR THE BODIES DIRECTLY
        /// `IFeature` has no «give me the bodies you created». It has `GetFaces()`,
        /// and every face knows which body it belongs to (`IFace2.GetBody()`). We
        /// go back up from there, collecting the distinct bodies.
        ///
        /// The comparison between bodies is done on COM identity: two faces of the
        /// same body return the same object, and a body counted twice
        /// would become a bar counted twice in the bill of materials.
        /// </summary>
        public static List<IBody2> BodiesOf(Step planStep, out string reason)
        {
            reason = null;
            var faces = FacesOf(planStep, out reason);
            if (faces == null) return null;

            var bodies = new List<IBody2>();
            foreach (var f in faces)
            {
                IBody2 body;
                try { body = f.GetBody() as IBody2; }
                catch { continue; }
                if (body == null) continue;
                bool already = false;
                foreach (var c in bodies)
                {
                    if (ReferenceEquals(c, body) || Equals(c, body)) { already = true; break; }
                }
                if (!already) bodies.Add(body);
            }

            if (bodies.Count == 0)
            {
                reason =
                    "The step «" + planStep.Operation + "» left no body: " +
                    "there are no bars to work on.";
                return null;
            }
            return bodies;
        }

        /// <summary>
        /// The OPEN END faces of the bars a step made: the ones an end cap
        /// closes.
        ///
        /// HOW AN END IS RECOGNISED, AND WHY IT IS NOT GUESSED
        /// A bar is a profile swept along a segment. The faces that count are
        /// the PLANAR ones across the axis of the bar: the two cuts. Everything
        /// else is the side of the bar.
        ///
        /// The axis of the bar is not known here (the segments are in the
        /// sketch, not in the feature), so a sturdier invariant is used: the
        /// end face of a HOLLOW profile has more than one loop, the outer
        /// outline and the outline of the void, and the void was made by the
        /// feature that made the outer walls or the end itself. That is exactly what is
        /// capped. A solid profile has no end to cap, and saying so is the
        /// right answer.
        ///
        /// The second half of the invariant is finding N7 (Z-C21.5): «planar
        /// with more than one loop» alone also holds for a side wall of a tube
        /// with a hole drilled through it, and until library 0.146.0 both faces
        /// of the drilled wall went to InsertEndCapFeature3 as ends to cap. See
        /// <see cref="InnerLoopsAreTheProfileVoid"/> for the rule.
        ///
        /// Mitred faces stay planar and hollow: they are capped too, and that
        /// is right, a frame with 45 degree corners still has open ends where
        /// a bar meets no other bar.
        ///
        /// <paramref name="machinedFaces"/> counts the planar faces with more
        /// than one loop that were NOT taken, because an inner loop of theirs
        /// was made by another feature (a drilled wall counts twice: its outer
        /// and its inner face). Null is returned, with the reason, when a face
        /// could not be told apart: see <see cref="OpenEndsOnly"/>.
        /// </summary>
        public static List<IFace2> FreeEnds(Step planStep, out int machinedFaces, out string reason)
        {
            machinedFaces = 0;
            reason = null;
            var faces = FacesOf(planStep, out reason);
            if (faces == null) return null;
            return OpenEndsOnly(faces, out machinedFaces, out reason);
        }

        /// <summary>
        /// The open ends of BODIES already in the document, not of a step.
        ///
        /// WHY A SECOND ROAD IS NEEDED
        /// `FreeEnds(Step)` works only inside the same plan: it cites
        /// the step that created the bars. In a NEW message — «cap the
        /// ends of the frame you made before» — that step is no
        /// longer there, and up to 0.29.1 the request had no valid
        /// form: the model could only redraw the bars from scratch, and
        /// overlapping bars would have come out.
        ///
        /// Now the document state carries the bodies with their
        /// identifier, and this is the road that uses them.
        ///
        /// The same rule as <see cref="FreeEnds"/>, and the same
        /// <paramref name="machinedFaces"/>. A body whose faces cannot be read
        /// stops the step (Z-C21.5, with N7): skipping it left that bar
        /// uncapped while the answer counted the caps of the others.
        /// </summary>
        public static List<IFace2> FreeEndsOf(
            IEnumerable<IBody2> bodies, out int machinedFaces, out string reason)
        {
            machinedFaces = 0;
            reason = null;
            var faces = new List<IFace2>();
            foreach (var body in bodies)
            {
                object[] list;
                try { list = body.GetFaces() as object[]; }
                catch { list = null; }
                if (list == null)
                {
                    reason = "I could not read the faces of one of the bodies, so I cannot say " +
                             "which of its ends are open: nothing was capped.";
                    return null;
                }
                foreach (var o in list)
                {
                    var f = o as IFace2;
                    if (f != null) faces.Add(f);
                }
            }
            if (faces.Count == 0)
            {
                reason = "The bodies indicated have no readable faces.";
                return null;
            }
            return OpenEndsOnly(faces, out machinedFaces, out reason);
        }

        /// <summary>
        /// Out of a heap of faces, the open ends of a hollow profile: PLANAR,
        /// with more than one loop, and every inner loop the void of the
        /// profile (<see cref="InnerLoopsAreTheProfileVoid"/>). A solid profile
        /// has none, and an empty list is the right answer there.
        ///
        /// A FACE THAT CANNOT BE TOLD APART STOPS EVERYTHING. Taking it would
        /// cap a drilled wall (InsertEndCapFeature3 wants «one or more end
        /// faces of a structural member», IFeatureManager~InsertEndCapFeature3
        /// .html, Remarks); leaving it out would leave an end open while the
        /// answer counts the caps. Either way the step would say it worked on
        /// a part that is not what was asked, so null comes back with the
        /// reason (memory «a check never seen firing»: a check that cannot
        /// look must not say yes). The same holds for a planar face whose loop
        /// count does not answer: until library 0.146.0 it was skipped.
        /// </summary>
        private static List<IFace2> OpenEndsOnly(
            IEnumerable<IFace2> faces, out int machinedFaces, out string reason)
        {
            machinedFaces = 0;
            reason = null;
            var ends = new List<IFace2>();
            foreach (IFace2 face in faces)
            {
                if (Normal(face) == null) continue;  // not planar: a side of the bar
                int loops;
                try { loops = face.GetLoopCount(); }   // IFace2~GetLoopCount.html: «Number of loops in this face»
                catch (Exception ex)
                {
                    reason = CannotTellEndsApart("the number of loops (" + ex.Message + ")");
                    return null;
                }
                if (loops < 2) continue;

                string unread;
                bool? isVoid = InnerLoopsAreTheProfileVoid(face, out unread);
                if (isVoid == null)
                {
                    reason = CannotTellEndsApart(unread);
                    return null;
                }
                if (isVoid == true) ends.Add(face);
                else machinedFaces++;
            }
            return ends;
        }

        private static string CannotTellEndsApart(string what)
        {
            return "On a planar face with a hole in it I could not read " + what + ", so I cannot " +
                   "tell an open end of a bar from a wall with a hole machined into it: nothing was capped.";
        }

        /// <summary>
        /// ARE THE INNER LOOPS OF THIS PLANAR FACE THE VOID OF A HOLLOW
        /// PROFILE, OR HOLES MACHINED INTO A WALL? (finding N7, Z-C21.5)
        ///
        /// WHY LOCAL GEOMETRY CANNOT TELL. The top wall of a square tube with a
        /// round hole drilled through it is, seen from the wall, the end of a
        /// prism: planar, two loops, every neighbour square to it (the two side
        /// walls and the two end faces of the bar all contain the direction of
        /// the hole). Its inner face, the ceiling of the tube, is the same. No
        /// normal, no direction and no loop count separates them from the true
        /// end of the tube; only the WHOLE bar does, and the axis of the bar is
        /// not known here.
        ///
        /// WHAT DOES TELL: WHO MADE THE WALLS OF THE HOLE. At an open end the
        /// faces across the inner loop are the inner walls of the tube, made by
        /// the same feature (the structural member) that made the outer walls
        /// across the outer loop, or the end face itself. A drilled hole has a
        /// wall made by the cut. So an inner loop is the void of the profile
        /// when every face across it was made by a feature that also made a
        /// face across the outer loop or the face itself, and a machined hole
        /// otherwise. IFace2~GetFeature.html, Remarks: a face «can be owned by
        /// several features» and GetFeature returns «Only the oldest feature
        /// from the face ... the first owning feature in the FeatureManager
        /// design tree». The wall of the hole was born in the cut, so its
        /// oldest owner is the cut; the walls of the tube keep the member as
        /// oldest owner even after a cut or a trim changed them. The state
        /// reads the same thing (DocumentState, FeatureId: a fillet
        /// shrinks three faces and all three still answer the extrusion,
        /// measured 14/9/2026; and the faces of a bar all answered the member,
        /// measured 10/9/2026, see «from_feature» there). Features are
        /// compared by IFeature.GetID, «unique within the document»
        /// (IFeature~GetID.html), not by name.
        ///
        /// WHY BOTH, AND NOT ONE FEATURE: an end trimmed by trim_members
        /// belongs to the trim while the walls around it still belong to the
        /// member; an end whose OUTER edges were filleted or chamfered has the
        /// fillet across its outer loop, while the end face itself still
        /// answers the member; a cope that cuts the outline of an end adds its
        /// own faces around the outer loop without touching the void. In all
        /// three the void is still the member's, and the end is still an end.
        ///
        /// THE FACE ACROSS AN EDGE is reached through the partner coedge, as
        /// Select_Tangent_Faces_Example_VB.htm does: «use the partner coedge
        /// to get the partner loop, which provides access to the partner
        /// face» (ICoEdge~GetPartner.html, ICoEdge~GetLoop.html,
        /// ILoop2~GetFace.html). No COM identity is compared. The outer loop is
        /// ILoop2.IsOuter (ILoop2~IsOuter.html: «True if the loop is the
        /// outermost loop»); the same page warns that some faces have no clear
        /// outer loop, so a planar face that does not answer exactly one outer
        /// loop is not guessed either.
        ///
        /// KNOWN LIMITS, both on the side of saying so rather than capping the
        /// wrong face. (1) One feature that makes a hole in a wall AND cuts the
        /// outline of the same face, or makes the face itself (a single cut
        /// that drills a wall and notches its edge, or mills a flat and drills
        /// it; a pattern whose instances do both), passes as a void. No
        /// operation of the catalogue does that in one feature. (2) An end
        /// whose void was machined at the mouth (a chamfer or a fillet on the
        /// INNER edges of the end, a counterbore) is not taken for an open end:
        /// it is counted in machinedFaces, and when no end is left the step
        /// says why instead of capping.
        ///
        /// NOT MEASURED ON SOLIDWORKS when written (no CAD available): the
        /// bench case saldature/the_end_cap_skips_a_drilled_wall is the proof.
        ///
        /// True: every inner loop is the void. False: at least one inner loop
        /// is a machined hole. Null: something could not be read, and
        /// <paramref name="unread"/> says what.
        /// </summary>
        private static bool? InnerLoopsAreTheProfileVoid(IFace2 face, out string unread)
        {
            unread = null;
            object[] loops;
            try { loops = face.GetLoops() as object[]; }   // IFace2~GetLoops.html: «Array of all of the loops on the face»
            catch (Exception ex)
            {
                unread = "the loops (" + ex.Message + ")";
                return null;
            }
            if (loops == null || loops.Length < 2)
            {
                unread = "the loops";
                return null;
            }

            HashSet<int> outerOwners = null;
            int outerLoops = 0;
            var innerLoops = new List<ILoop2>();
            foreach (var o in loops)
            {
                ILoop2 loop = o as ILoop2;
                if (loop == null)
                {
                    unread = "one of the loops";
                    return null;
                }
                bool isOuter;
                try { isOuter = loop.IsOuter(); }
                catch (Exception ex)
                {
                    unread = "which loop is the outer one (" + ex.Message + ")";
                    return null;
                }
                if (!isOuter) { innerLoops.Add(loop); continue; }
                outerLoops++;
                outerOwners = OwnersAcross(loop, out unread);
                if (outerOwners == null) return null;
            }
            if (outerLoops != 1 || innerLoops.Count == 0)
            {
                unread = "which loop is the outer one (" + outerLoops + " of " + loops.Length +
                         " answered that they are)";
                return null;
            }
            int? ownId = OwnerId(face);
            if (ownId == null)
            {
                unread = "the feature that made it";
                return null;
            }
            // The features that may have made the void: those around the
            // outer loop, and the one that made the face.
            var allowed = new HashSet<int>(outerOwners);
            allowed.Add((int)ownId);

            foreach (ILoop2 inner in innerLoops)
            {
                var innerOwners = OwnersAcross(inner, out unread);
                if (innerOwners == null) return null;
                if (!innerOwners.IsSubsetOf(allowed)) return false;   // a hole made by another feature
            }
            return true;
        }

        /// <summary>
        /// The ID of the feature that owns a face, the oldest of its owners
        /// (IFace2~GetFeature.html, Remarks), or null when it cannot be read.
        /// </summary>
        private static int? OwnerId(IFace2 face)
        {
            try
            {
                IFeature owner = face == null ? null : face.GetFeature() as IFeature;
                return owner == null ? (int?)null : owner.GetID();
            }
            catch { return null; }
        }

        /// <summary>
        /// The IDs of the features that made the faces across the edges of a
        /// loop (the oldest owner of each, IFace2~GetFeature.html). Null when
        /// one of them cannot be read, with <paramref name="unread"/> saying
        /// what: a set with a face missing would make «made by the member»
        /// true by leaving out the one face that says otherwise.
        /// </summary>
        private static HashSet<int> OwnersAcross(ILoop2 loop, out string unread)
        {
            unread = null;
            object[] coedges;
            try { coedges = loop.GetCoEdges() as object[]; }   // ILoop2~GetCoEdges.html: «Array of coedges»
            catch (Exception ex)
            {
                unread = "the edges of one of the loops (" + ex.Message + ")";
                return null;
            }
            if (coedges == null || coedges.Length == 0)
            {
                // ILoop2~GetEdges.html: a singular loop (a single vertex) has
                // no edges. It is not the void of a profile, nor a hole with a
                // wall: it is not guessed.
                unread = "the edges of one of the loops";
                return null;
            }

            var owners = new HashSet<int>();
            foreach (var c in coedges)
            {
                IFace2 across = null;
                try
                {
                    ICoEdge coedge = c as ICoEdge;
                    ICoEdge partner = coedge == null ? null : coedge.GetPartner() as ICoEdge;
                    ILoop2 partnerLoop = partner == null ? null : partner.GetLoop() as ILoop2;
                    across = partnerLoop == null ? null : partnerLoop.GetFace() as IFace2;
                }
                catch { across = null; }
                int? id = OwnerId(across);
                if (id == null)
                {
                    unread = "the feature that made a face next to it";
                    return null;
                }
                owners.Add((int)id);
            }
            return owners;
        }

        // ─────────────────────────────────────────────────────────────────
        // Supporting geometry
        // ─────────────────────────────────────────────────────────────────

        private static IFace2[] FacesOf(Step planStep, out string reason)
        {
            reason = null;
            if (planStep == null || planStep.Feature == null)
            {
                reason =
                    "The step you refer to left no " +
                    "geometry to name.";
                return null;
            }
            object[] raw;
            try { raw = planStep.Feature.GetFaces() as object[]; }
            catch (Exception ex)
            {
                reason = "I cannot read the faces of the previous step: " + ex.Message;
                return null;
            }
            if (raw == null || raw.Length == 0)
            {
                reason =
                    "The step «" + planStep.Operation + "» left no faces of " +
                    "its own: there is nothing to name.";
                return null;
            }
            var faces = new List<IFace2>();
            foreach (var g in raw)
            {
                var f = g as IFace2;
                if (f != null) faces.Add(f);
            }
            if (faces.Count == 0)
            {
                reason = "The faces of the previous step are not readable.";
                return null;
            }
            return faces.ToArray();
        }

        /// <summary>
        /// The planar face perpendicular to the direction, at the indicated extreme
        /// (+1 = the one furthest ahead along the direction, -1 = the one furthest back).
        /// </summary>
        private static bool End(
            Step planStep, IFace2[] faces, int facing, out IFace2 choice, out string reason)
        {
            choice = null;
            reason = null;
            if (planStep.Direction == null)
            {
                reason =
                    "Of the step «" + planStep.Operation + "» I do not know the direction " +
                    "of extrusion, so I cannot tell which face is on top. " +
                    "Name the face by its identifier.";
                return false;
            }

            double bestProjection = 0;
            int unmeasured = 0;
            foreach (var f in faces)
            {
                var n = Normal(f);
                if (n == null) continue;
                if (Math.Abs(Product(n, planStep.Direction)) <= Cosine) continue;

                var center = PointToProject(f);
                if (center == null) { unmeasured++; continue; }
                double projection = Product(center, planStep.Direction) * facing;
                if (choice == null || projection > bestProjection)
                {
                    choice = f;
                    bestProjection = projection;
                }
            }

            if (choice == null && unmeasured > 0)
            {
                // Not "there is no such face": there are, and none could be measured.
                reason =
                    "The step «" + planStep.Operation + "» left " + unmeasured + " planar face(s) " +
                    "perpendicular to the extrusion direction, but I could not measure where any " +
                    "of them is (neither the true bounding box nor a point on the face answered). " +
                    "Name the face by its identifier.";
                return false;
            }
            if (choice == null)
            {
                reason =
                    "The step «" + planStep.Operation + "» did not leave a planar face " +
                    "perpendicular to the direction of extrusion: " +
                    (facing > 0 ? "there is no face on top." : "there is no bottom.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// The point of a planar face perpendicular to the direction that is
        /// projected on the direction to rank the faces: the centre of its true
        /// box, or, when the box does not answer, a point ON the face. Null
        /// only when neither answers.
        ///
        /// For a face perpendicular to the direction every point of the face
        /// has (within the Cosine tolerance) the same projection, so a point on
        /// the face ranks it as well as the centre of its box. Guide:
        /// IFace2~GetClosestPointOn.html, «Array of five doubles representing
        /// the X,Y,Z point on the face followed by the U, V parameter»; the box
        /// is ApiHelpers.TrueBoundingBox (S6), which can fail where the
        /// approximate IFace2.GetBox always answered.
        /// </summary>
        private static double[] PointToProject(IFace2 face)
        {
            try
            {
                var box = ApiHelpers.TrueBoundingBox(face);   // S6: not IFace2.GetBox, approximate
                if (box != null && box.Length >= 6)
                    return new[] { (box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2 };
            }
            catch { }
            try
            {
                var p = face.GetClosestPointOn(0, 0, 0) as double[];
                if (p != null && p.Length >= 3) return new[] { p[0], p[1], p[2] };
            }
            catch { }
            return null;
        }

        /// <summary>
        /// The edges of the OUTER border only of a face.
        ///
        /// «The top edges» means the perimeter, not the borders of the holes
        /// that were opened inside it. Telling them apart is not a finesse:
        /// filleting the mouth of a hole shortens the useful cylindrical part,
        /// and whoever confirmed «top edges» did not confirm that.
        /// </summary>
        private static bool OuterBorderEdges(
            IFace2 face, List<IEntity> inside, out string reason)
        {
            reason = null;
            object[] loop;
            try { loop = face.GetLoops() as object[]; }
            catch (Exception ex)
            {
                reason = "I cannot read the borders of the face: " + ex.Message;
                return false;
            }
            if (loop == null || loop.Length == 0)
            {
                reason = "The face has no readable borders.";
                return false;
            }

            int outer = 0;
            foreach (var g in loop)
            {
                var l = g as ILoop2;
                if (l == null) continue;
                bool outside;
                try { outside = l.IsOuter(); }
                catch { continue; }
                if (!outside) continue;
                outer++;

                object[] rawItems;
                try { rawItems = l.GetEdges() as object[]; }
                catch { continue; }
                if (rawItems == null) continue;
                foreach (var e in rawItems)
                {
                    var ent = e as IEntity;
                    if (ent != null) inside.Add(ent);
                }
            }

            if (outer == 0)
            {
                // Here we do not fall back on «take all the edges»: it would be
                // exactly the defect that this method exists to close.
                reason =
                    "I did not find the outer border of the face, so I cannot " +
                    "tell it from the borders of the holes. Name the " +
                    "edges by their identifiers.";
                return false;
            }
            return true;
        }

        private static void AddEdges(IFace2 face, List<IEntity> inside)
        {
            object[] rawItems;
            try { rawItems = face.GetEdges() as object[]; }
            catch { return; }
            if (rawItems == null) return;
            foreach (var g in rawItems)
            {
                var e = g as IEntity;
                if (e != null) inside.Add(e);
            }
        }

        /// <summary>The outward normal of a PLANAR face, or null if it is not planar.</summary>
        private static double[] Normal(IFace2 face)
        {
            try
            {
                // S17: the rule (and why Face2.Normal and not PlaneParams)
                // now lives in ApiHelpers.NormalIfPlanar.
                var normal = ApiHelpers.NormalIfPlanar(face);
                return normal == null ? null : Normalize(normal);
            }
            catch { return null; }
        }

        private static bool Cylindrical(IFace2 face)
        {
            try
            {
                var surface = face.GetSurface() as ISurface;
                return surface != null && surface.IsCylinder();
            }
            catch { return false; }
        }

        /// <summary>
        /// Is a cylindrical face CONCAVE — that is, the wall of a hole?
        ///
        /// `FaceInSurfaceSense()` says whether the face normal is reversed
        /// with respect to that of the surface. For a cylinder the natural
        /// normal points out from the axis: if the face reverses it, the
        /// material is OUTSIDE the cylinder, and it is a hole.
        ///
        /// The guide page says why this works (Z-C21.9, finding L10-7):
        /// IFace2~FaceInSurfaceSense.html, Remarks, «True if face normal and
        /// surface normal are in the opposite direction», and «The normal vector
        /// of faces, however, are directed away from the body material». The
        /// face normal of a hole wall points into the hole, away from the
        /// material, so against the cylinder's own outward normal: true. The
        /// same page is the reason IFace2.Normal is taken as the outward normal
        /// everywhere (ApiHelpers.NormalIfPlanar).
        ///
        /// Measured, not deduced (`Checks\HoleOrFillet`, 15/9/2026):
        /// on a plate with a Ø12 hole and an R5 fillet, the hole wall
        /// answers **true** and the fillet surface **false**.
        ///
        /// When in doubt the answer is **false**, that is «it is not a hole»: the cost
        /// of getting it wrong in the two directions is not the same. Mistaking a fillet
        /// for a hole makes a feature end up on the wrong face without
        /// anything failing; mistaking a hole for a fillet produces a
        /// refusal, which is read.
        /// </summary>
        private static bool Concave(IFace2 face)
        {
            try { return face.FaceInSurfaceSense(); }
            catch { return false; }
        }

        /// <summary>The diameters of these cylindrical faces, for the refusal.</summary>
        private static string Diameters(List<IFace2> faces)
        {
            var seen = new List<string>();
            foreach (var f in faces)
            {
                try
                {
                    var par = (f.GetSurface() as ISurface)?.CylinderParams as double[];
                    if (par == null || par.Length < 7) continue;
                    string d = "Ø" + Units.Mm(par[6] * 2).ToString(
                        "0.##", System.Globalization.CultureInfo.InvariantCulture);
                    if (!seen.Contains(d)) seen.Add(d);
                }
                catch { }
            }
            return seen.Count == 0 ? "diameters cannot be read" : string.Join(", ", seen);
        }

        /// <summary>The direction of a STRAIGHT edge, or null if it is not one.</summary>
        private static double[] EdgeDirection(IEdge edge)
        {
            if (edge == null) return null;
            try
            {
                var a = (edge.GetStartVertex() as IVertex)?.GetPoint() as double[];
                var b = (edge.GetEndVertex() as IVertex)?.GetPoint() as double[];
                if (a == null || b == null || a.Length < 3 || b.Length < 3) return null;
                var d = new double[] { b[0] - a[0], b[1] - a[1], b[2] - a[2] };
                return Normalize(d);
            }
            catch { return null; }
        }

        private static double[] Normalize(double[] v)
        {
            if (v == null || v.Length < 3) return null;
            double n = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (n < 1e-12) return null;
            return new double[] { v[0] / n, v[1] / n, v[2] / n };
        }

        private static double Product(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }
    }
}
