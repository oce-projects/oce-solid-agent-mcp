using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE ANCHOR: a hole or a pocket on a face that follow the edge when
    /// the part changes (entry E18, parts a and b).
    ///
    /// ── THE DEFECT ────────────────────────────────────────────────────────
    ///
    /// `x_mm`/`y_mm` are ABSOLUTE coordinates of the model. Four holes «20
    /// mm from the corners» of a plate that gets wider stay where they were, and a
    /// pocket «in the middle» stays at x = 0 when the plate grows on one side only
    /// (it grows on ONE side even if the sketch is centred: entry E2).
    /// Clean rebuild, no error, wrong part: three of the four
    /// silences of `--regressione` (K5).
    ///
    /// ── THE SHAPE: ONE ANCHOR PER AXIS, AND BOTH AXES ─────────────────────
    ///
    ///   "anchor": { "x": {"edge_uid": "E7", "distance_mm": 20},
    ///               "y": {"centered_between": ["E3", "E5"]} }
    ///
    /// For each axis one of two things: a DIMENSION from the centre of the profile to
    /// an edge, or the centre HALFWAY between two opposite edges. Both
    /// axes, always: an axis without an anchor would leave the point free, and the
    /// SOLIDWORKS solver would move it wherever it likes at the first
    /// edit (risk 8 of the adversarial critic, 25/9/2026).
    ///
    /// ── WHY «CENTRED» IS PER AXIS, AND NOT PER POINT ──────────────────────
    ///
    /// Measured on 25/9/2026, 200 x 120 plate lengthened and then raised:
    ///
    ///   midline between the MIDPOINTS of the two sides -> the hole at y = -20 jumps
    ///                                               to y = 0 ALREADY AT CREATION,
    ///                                               and ignores the dimension in y
    ///   midline with its ends on the LINE of the   -> (0,-20) -> (30,-20) ->
    ///   sides, perpendicular, centre halfway         (30,-20): centred in x,
    ///   + dimension of 40 from the bottom for y      40 from the bottom in y; exact,
    ///                                               zero errors, sketch
    ///                                               fully defined
    ///
    /// The first one centred in TWO dimensions and depended on the length of the
    /// sides (a chamfer on a corner would have moved it); the second centres
    /// in one only, and the other is decided by the other axis.
    ///
    /// ── THE RULES THAT THE MEASUREMENT AND THE CRITIC IMPOSED ─────────────
    ///
    ///   1. it is drawn with `AddToDB` set to true, but ONLY around each creation:
    ///      the relations that SOLIDWORKS guesses overconstrain the sketch at the
    ///      first edit (code 51), and an `AddToDB` left on would take the
    ///      inferences away from whoever draws afterwards;
    ///   2. the centre of the pocket is BUILT (diagonal + midpoint):
    ///      the one of `CreateCenterRectangle` is not among the points of the sketch;
    ///   3. the sketch, before it leaves, must be FULLY DEFINED
    ///      (`GetConstrainedStatus` = 3): an underconstrained sketch goes wherever
    ///      the solver wants, an overconstrained one breaks at the edit;
    ///   4. after the cut, a MOVEMENT TEST: every anchor dimension +0.1 mm,
    ///      rebuild, no new feature in error, value put back.
    ///      The count of errors right after the cut is not enough: the
    ///      second draft of the probe broke only AT THE EDIT;
    ///   5. if anything does not hold after the cut, the cut is removed.
    ///
    /// ── `x_mm`/`y_mm` STAY MANDATORY ──────────────────────────────────────
    ///
    /// They say WHERE the point is now; the anchor says WHAT holds it there. They must
    /// say the same point, and if they do not it is refused: a dimension of 20 on
    /// a point that sits at 25 would move the profile by 5 mm with no error.
    /// </summary>
    public partial class SwOperations
    {
        private sealed class AnchorEdge
        {
            public string Uid;
            public IEdge Edge;
            public double[] StartMm, FineMm;   // in the system of the MODEL
        }

        private sealed class AnchorAxis
        {
            public string Name;                 // «x» o «y»
            public int Index;                  // 0 = x, 1 = y (along axes[Index])
            public AnchorEdge Edge;     // dimension from an edge
            public double DistanceMm;
            public AnchorEdge Fra1, Fra2;  // or centred between two
            public bool Centered { get { return Fra1 != null; } }
            public IDimension CreatedDimension;        // for the movement test
        }

        private sealed class Anchoring
        {
            public AnchorAxis X, Y;
            public bool Empty { get { return X == null && Y == null; } }
            public IEnumerable<AnchorAxis> Axes() { if (X != null) yield return X; if (Y != null) yield return Y; }
        }

        /// <summary>
        /// Reads `anchor` and checks it against the face, against `x_mm`/`y_mm` and
        /// against the extent of the profile (`halfU`, `halfV`: half of the width of the
        /// profile along x and along y — the radius for a hole). Returns an EMPTY
        /// anchoring if the argument is not there (the old way, absolute
        /// coordinates, stays as it is); null with the reason if the anchor is wrong.
        /// </summary>
        private Anchoring ReadAnchor(
            Dictionary<string, object> a, DocumentState docState, IFace2 face,
            double[] axes, double offsetM, double x, double y, double halfU, double halfV,
            out string reason)
        {
            reason = null;
            object raw;
            if (a == null || !a.TryGetValue("anchor", out raw) || raw == null) return new Anchoring();

            var d = raw as Dictionary<string, object>;
            if (d == null)
            {
                reason = "«anchor» must be an object with «x» and «y»: for each " +
                         "one {\"edge_uid\": \"E7\", \"distance_mm\": 20} or {\"centered_between\": [\"E3\", \"E5\"]}.";
                return null;
            }
            foreach (var key in d.Keys)
                if (key != "x" && key != "y")
                {
                    reason = "«anchor» does not know «" + key + "»: the keys are «x» and «y», one per axis.";
                    return null;
                }
            if (!d.ContainsKey("x") || !d.ContainsKey("y"))
            {
                reason = "The anchor wants BOTH axes, «x» and «y». An axis without an anchor would stay free, " +
                         "and at the first change of the part the profile would go wherever the solver puts " +
                         "it, with no error. If on one axis you want the position it has now, anchor it to the " +
                         "edge you would measure it from.";
                return null;
            }

            // THE NORMAL OF A PLANAR FACE ONLY (entry Z-C21.2, finding N24).
            // IFace2~Normal.html, Remarks: «If the face is not a planar face, then
            // this property returns 0,0,0». With (0,0,0) the check of the chamfer
            // edge below gave cosine 0 for EVERY adjacent face, and
            // let everything through. FaceNormal asks IsPlane() first,
            // like References.Normal; without a planar face the anchor is refused.
            double[] normal = FaceNormal(face);
            if (normal == null || normal.Length < 3)
            {
                reason = "The face you are working on is not planar (or I cannot read its normal): the " +
                         "anchor measures distances from edges in the plane of the face, and on a curved " +
                         "face that plane does not exist. Remove «anchor» and give the position with " +
                         "x_mm and y_mm.";
                return null;
            }
            var anc = new Anchoring();
            foreach (var itemName in new[] { "x", "y" })
            {
                int k = itemName == "x" ? 0 : 1;
                double here = k == 0 ? x : y;
                double half = k == 0 ? halfU : halfV;
                var entry = d[itemName] as Dictionary<string, object>;
                if (entry == null)
                {
                    reason = "«anchor." + itemName + "» must be {\"edge_uid\": ..., \"distance_mm\": ...} " +
                             "or {\"centered_between\": [..., ...]}.";
                    return null;
                }
                var axis = new AnchorAxis { Name = itemName, Index = k };

                if (entry.ContainsKey("centered_between"))
                {
                    var uid = new List<string>();
                    var list = entry["centered_between"] as IEnumerable;
                    if (list != null && !(entry["centered_between"] is string))
                        foreach (var o in list) uid.Add(Convert.ToString(o, CultureInfo.InvariantCulture));
                    if (uid.Count != 2 || uid[0] == uid[1])
                    {
                        reason = "«anchor." + itemName + ".centrata_fra» wants TWO different, opposite edges, taken from the state.";
                        return null;
                    }
                    axis.Fra1 = EdgeForAnchor(a, docState, uid[0], offsetM, axes, normal, out reason);
                    if (axis.Fra1 == null) return null;
                    axis.Fra2 = EdgeForAnchor(a, docState, uid[1], offsetM, axes, normal, out reason);
                    if (axis.Fra2 == null) return null;
                    // TO CENTRE ALONG x the two edges run along y.
                    if (EdgeAxis(axis.Fra1, axes) != 1 - k || EdgeAxis(axis.Fra2, axes) != 1 - k)
                    {
                        reason = "To center along «" + itemName + "» the two edges must run along «" +
                                 (k == 0 ? "y" : "x") + "» of the face: «" + uid[0] + "» and «" + uid[1] + "» do not.";
                        return null;
                    }
                    double c1 = Coord(axis.Fra1.StartMm, axes, k), c2 = Coord(axis.Fra2.StartMm, axes, k);
                    if ((c1 - here) * (c2 - here) >= 0)
                    {
                        reason = "The point is not BETWEEN «" + uid[0] + "» and «" + uid[1] + "» along «" + itemName +
                                 "»: they are on the same side, and there is nothing between them to center on.";
                        return null;
                    }
                    double center = (c1 + c2) / 2;
                    if (Math.Abs(center - here) > PointToleranceMm)
                    {
                        reason = string.Format(CultureInfo.InvariantCulture,
                            "The point you gave has {0} = {1:0.##}, but halfway between «{2}» and «{3}» there is {0} = {4:0.##}. " +
                            "x_mm/y_mm say where the point is NOW, the anchor says what holds it there: they " +
                            "must say the same point. I refuse rather than move it silently.",
                            itemName, here, uid[0], uid[1], center);
                        return null;
                    }
                    if (Math.Abs(center - c1) < half)
                    {
                        reason = string.Format(CultureInfo.InvariantCulture,
                            "Between «{0}» and «{1}» there are {2:0.##} mm along «{3}», and the profile is {4:0.##} wide: it " +
                            "does not fit.", uid[0], uid[1], Math.Abs(c2 - c1), itemName, 2 * half);
                        return null;
                    }
                }
                else
                {
                    string uid = entry.ContainsKey("edge_uid")
                        ? Convert.ToString(entry["edge_uid"], CultureInfo.InvariantCulture) : "";
                    double distance;
                    if (uid.Length == 0 || !entry.ContainsKey("distance_mm") ||
                        !double.TryParse(Convert.ToString(entry["distance_mm"], CultureInfo.InvariantCulture),
                                         NumberStyles.Any, CultureInfo.InvariantCulture, out distance))
                    {
                        reason = "«anchor." + itemName + "» wants {\"edge_uid\": \"E7\", \"distance_mm\": 20} " +
                                 "or {\"centered_between\": [\"E3\", \"E5\"]}.";
                        return null;
                    }
                    // THE DISTANCE IS FROM THE CENTRE of the hole or pocket, and it
                    // cannot be less than half of its width: the profile
                    // would run over the edge (risk 5 of the critic). Equal
                    // is allowed: it is a stop.
                    if (distance + 1e-9 < half)
                    {
                        reason = string.Format(CultureInfo.InvariantCulture,
                            "«distance_mm» is from the CENTER of the profile to the edge, and {0:0.##} is less than half " +
                            "of its width along «{1}» ({2:0.##}): the profile would cross over the edge «{3}».",
                            distance, itemName, half, uid);
                        return null;
                    }
                    var sp = EdgeForAnchor(a, docState, uid, offsetM, axes, normal, out reason);
                    if (sp == null) return null;
                    if (EdgeAxis(sp, axes) != 1 - k)
                    {
                        reason = "To anchor «" + itemName + "» you need an edge that runs along «" + (k == 0 ? "y" : "x") +
                                 "» of the face (the distance is measured along «" + itemName + "»): «" + uid + "» does not.";
                        return null;
                    }
                    double real = Math.Abs(here - Coord(sp.StartMm, axes, k));
                    if (Math.Abs(real - distance) > PointToleranceMm)
                    {
                        reason = string.Format(CultureInfo.InvariantCulture,
                            "The point you gave is {0:0.##} mm from «{1}», not {2:0.##}. x_mm/y_mm say where the point " +
                            "is NOW, the anchor says what holds it there: they must say the same point. I refuse rather " +
                            "than move the profile by {3:0.##} mm silently.",
                            real, uid, distance, Math.Abs(real - distance));
                        return null;
                    }
                    axis.Edge = sp;
                    axis.DistanceMm = distance;
                }
                if (k == 0) anc.X = axis; else anc.Y = axis;
            }
            return anc;
        }

        /// <summary>
        /// The edge by uid: with the signature, straight, in the plane of the face, and
        /// not on the border of a chamfer or a fillet.
        /// </summary>
        private AnchorEdge EdgeForAnchor(
            Dictionary<string, object> a, DocumentState docState, string uid,
            double offsetM, double[] axes, double[] faceNormal, out string reason)
        {
            reason = null;
            // THE SIGNATURE IS MANDATORY WHEN THE PLAN CARRIES ONE (risk 2 of the
            // critic): `SignatureMatches` without a signature answers yes, and among equal
            // edges the order of the state is not guaranteed — a hole at half
            // height would pass the consistency check even tied to the wrong side. A
            // plan that carries `_signatures` but not the one of this edge is a plan
            // that does not know which edge it meant.
            if (a != null && a.ContainsKey("_signatures") && ExpectedSignature(a, uid) == null)
            {
                reason = "The plan carries the signatures of the entities but not that of the edge «" + uid + "»: " +
                         "without it, I do not know whether «" + uid + "» is still the edge you saw.";
                return null;
            }
            // If the uid changed between the reading and now, the edge
            // is found again by signature (entry E19): same length, same point.
            var signature = EdgeSignature(
                a, docState, uid,
                "The edge «" + uid + "» is not in this document: the edges of an anchor are " +
                "taken from the state (E1, E2...).", out reason);
            if (signature == null) return null;
            var edge = signature.Entity as IEdge;
            ICurve curva = null;
            ICurveParamData par = null;
            try { par = ApiHelpers.EdgeParameters(edge, out curva); } catch { }
            if (curva == null || par == null || !curva.IsLine())
            {
                reason = "The edge «" + uid + "» is not straight: an anchor is taken from a straight edge.";
                return null;
            }
            var p0 = par.StartPoint as double[];
            var p1 = par.EndPoint as double[];
            if (p0 == null || p1 == null || p0.Length < 3 || p1.Length < 3)
            {
                reason = "I cannot read the ends of the edge «" + uid + "».";
                return null;
            }
            int n = (int)axes[2];
            if (Math.Abs(p0[n] - offsetM) > 1e-6 || Math.Abs(p1[n] - offsetM) > 1e-6)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "The edge «{0}» is not in the plane of the face you are working on (it is {1:0.##} mm from that plane): " +
                    "an anchor is taken from an edge of the face itself.",
                    uid, Math.Max(Math.Abs(p0[n] - offsetM), Math.Abs(p1[n] - offsetM)) * 1000.0);
                return null;
            }
            // THE BORDER OF A CHAMFER OR A FILLET (risk 9 of the critic):
            // the edge between the face and a chamfer lies in the plane, is straight, is
            // parallel — and if the chamfer goes from 2 to 5 the hole moves by 3
            // without anyone knowing. The other face of the edge must be
            // planar and perpendicular to the one being worked on.
            //
            // «PLANAR» WAS NOT CHECKED (entry Z-C21.2, finding C2-1).
            // IFace2~Normal.html: «Gets the unit normal vector for any planar
            // face» and, in the Remarks, «If the face is not a planar face, then
            // this property returns 0,0,0». The fillet is a cylinder: Normal
            // gave (0,0,0), the cosine came out 0 as for a true perpendicular
            // wall, and the tangent edge of the fillet passed for a sharp edge.
            // The check worked only for the chamfer (oblique planar face).
            // Now IsPlane() comes before Normal, as in
            // References.Normal and FaceNormal; a non-planar face is
            // refused with the same message.
            //
            // AND IF IT CANNOT LOOK, IT DOES NOT ACCEPT (finding N6). Before, a
            // `catch { }` and an `if` that skipped when the faces or the normal were
            // missing let the edge through without having looked at it: a
            // check that does not look and says yes is worse than no check
            // (memory «check never seen to fire»).
            string notChecked = null;
            try
            {
                var faces = edge.GetTwoAdjacentFaces2() as object[];
                if (faces == null || faces.Length == 0)
                    notChecked = "SOLIDWORKS does not give me the faces of the edge";
                else if (faceNormal == null || faceNormal.Length < 3)
                    notChecked = "I do not have the normal of the face you are working on";
                else
                    foreach (var o in faces)
                    {
                        var f = o as IFace2;
                        if (f == null) continue;   // a border edge has one face only and a NULL (GetTwoAdjacentFaces2.html)
                        bool planar = false;
                        ISurface surface = null;
                        try { surface = f.GetSurface() as ISurface; planar = surface?.IsPlane() == true; } catch { }
                        // THE NORMAL: of a planar face from Normal; of a curved one
                        // at the midpoint of the edge, from EvaluateAtPoint
                        // (review of Z-C21.2, defect 8). ISurface~EvaluateAtPoint.html:
                        // «This method calculates the normal, the principal
                        // directions, and the principal curvatures, of the surface
                        // at the specified point», and the return begins with
                        // «surfNorm[i, j, k]». The direction does not matter: the absolute
                        // value of the cosine is used. The first draft refused EVERY
                        // non-planar face, even a cylinder that meets the face at 90
                        // degrees along a line (a half-round resting on it): a
                        // sharp edge, and the message «bordo di un raccordo» there
                        // was false. The fillet is recognised because it is TANGENT:
                        // its normal on the edge is that of the face.
                        double[] nf = null;
                        if (planar) nf = ApiHelpers.NormalIfPlanar(f);
                        else if (surface != null)
                        {
                            try
                            {
                                var v = surface.EvaluateAtPoint(
                                    (p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2, (p0[2] + p1[2]) / 2) as double[];
                                if (v != null && v.Length >= 3) nf = new[] { v[0], v[1], v[2] };
                            }
                            catch { nf = null; }
                        }
                        double length = nf == null || nf.Length < 3 ? 0
                            : Math.Sqrt(nf[0] * nf[0] + nf[1] * nf[1] + nf[2] * nf[2]);
                        if (length < 1e-9)
                        {
                            notChecked = "I cannot read the normal of a face of the edge" +
                                          (planar ? "" : " (it is not planar)");
                            break;
                        }
                        double cosine = Math.Abs(nf[0] * faceNormal[0] + nf[1] * faceNormal[1] +
                                                 nf[2] * faceNormal[2]) / length;
                        if (planar && cosine > 0.999) continue;          // the face itself, or a coplanar one
                        if (!planar && cosine > 0.999)
                        {
                            reason = "The edge «" + uid + "» is on the border of a fillet " +
                                     "(its other face is curved and tangent to this one): if the fillet changes, " +
                                     "the edge moves and the profile with it. Anchor it to a sharp edge of the part.";
                            return null;
                        }
                        if (cosine > 0.01)
                        {
                            reason = "The edge «" + uid + "» is on the border of a chamfer or a fillet (its " +
                                     "other face " + (planar ? "" : "is curved and ") + "is not perpendicular to this one): " +
                                     "if the chamfer or the fillet changes, the profile moves with it. Anchor it " +
                                     "to a sharp edge of the part.";
                            return null;
                        }
                    }
            }
            catch (Exception ex) { notChecked = ex.Message; }
            if (notChecked != null)
            {
                reason = "I could not look at the other face of the edge «" + uid + "» (" + notChecked +
                         "): I do not know whether it is on the border of a chamfer or a fillet, and an anchor taken " +
                         "there would move with it without any error. Anchor it to another sharp edge of the part.";
                return null;
            }
            return new AnchorEdge
            {
                Uid = uid,
                Edge = edge,
                StartMm = new[] { p0[0] * 1000.0, p0[1] * 1000.0, p0[2] * 1000.0 },
                FineMm = new[] { p1[0] * 1000.0, p1[1] * 1000.0, p1[2] * 1000.0 },
            };
        }

        /// <summary>Along which axis of the face the edge runs: 0 = x, 1 = y, -1 = oblique.</summary>
        private static int EdgeAxis(AnchorEdge s, double[] axes)
        {
            double du = Math.Abs(Coord(s.FineMm, axes, 0) - Coord(s.StartMm, axes, 0));
            double dv = Math.Abs(Coord(s.FineMm, axes, 1) - Coord(s.StartMm, axes, 1));
            if (du > 0.01 && dv < 0.001) return 0;
            if (dv > 0.01 && du < 0.001) return 1;
            return -1;
        }

        private static double Coord(double[] pointMm, double[] axes, int which)
        {
            return pointMm[(int)axes[which]];
        }

        /// <summary>
        /// Puts in the OPEN sketch what holds the point where the anchor says,
        /// axis by axis, reads everything back, and demands the sketch FULLY
        /// DEFINED. Returns false with the reason if something does not add up.
        /// </summary>
        private bool ApplyAnchor(
            IModelDoc2 model, Anchoring anc, ISketchPoint point,
            double[] axes, double offsetM, double x, double y,
            Dictionary<string, object> measurements, out string reason, bool expectDefined = true)
        {
            reason = null;
            if (anc == null || anc.Empty) return true;
            if (point == null)
            {
                reason = "I do not have the reference point of the profile to attach the anchor to.";
                return false;
            }
            var said = new Dictionary<string, object>();
            var sketches = model.SketchManager;

            foreach (var axis in anc.Axes())
            {
                int k = axis.Index;
                string error;
                if (axis.Centered)
                {
                    // THE MIDLINE: along axis k, at the height of the point
                    // on the other axis, with its ends on the LINE of the two
                    // edges (coincidence), perpendicular to them, and the point at
                    // the middle. Measured on 25/9/2026: it centres along k and leaves
                    // the other axis to the other anchor.
                    double other = k == 0 ? y : x;
                    double c1 = Coord(axis.Fra1.StartMm, axes, k), c2 = Coord(axis.Fra2.StartMm, axes, k);
                    var m1 = PointInSketch(model, axes, offsetM, k == 0 ? c1 : other, k == 0 ? other : c1, out error);
                    var m2 = m1 == null ? null
                        : PointInSketch(model, axes, offsetM, k == 0 ? c2 : other, k == 0 ? other : c2, out error);
                    if (m1 == null || m2 == null) { reason = error; return false; }

                    int relBefore = SketchRelations(sketches);
                    ISketchSegment centerline;
                    bool addBefore = sketches.AddToDB;
                    try
                    {
                        sketches.AddToDB = true;
                        centerline = sketches.CreateCenterLine(m1[0], m1[1], m1[2], m2[0], m2[1], m2[2]);
                    }
                    finally { sketches.AddToDB = addBefore; }
                    var line = centerline as ISketchLine;
                    if (line == null) { reason = "The midline between «" + axis.Fra1.Uid + "» and «" + axis.Fra2.Uid + "» was not created."; return false; }

                    // THE ENDS ARE CHECKED (Z-C21.8, finding N49): ISketchLine~
                    // GetStartPoint2.html, «Start sketch point or NULL if operation
                    // fails». A null end made the relation fail and the refusal
                    // count relations, which pointed at the wrong thing.
                    var centreLineStart = line.GetStartPoint2() as ISketchPoint;
                    var centreLineEnd = line.GetEndPoint2() as ISketchPoint;
                    if (centreLineStart == null || centreLineEnd == null)
                    {
                        reason = "The centre line between «" + axis.Fra1.Uid + "» and «" + axis.Fra2.Uid + "» gives no " +
                                 (centreLineStart == null ? "start" : "end") + " point (GetStartPoint2 / GetEndPoint2 " +
                                 "answered null): I cannot centre along «" + axis.Name + "».";
                        return false;
                    }
                    bool r1 = Relation(model, centreLineStart, axis.Fra1.Edge, "sgCOINCIDENT");
                    bool r2 = Relation(model, centreLineEnd, axis.Fra2.Edge, "sgCOINCIDENT");
                    bool r3 = Relation(model, centerline, axis.Fra1.Edge, "sgPERPENDICULAR");
                    bool r4 = Relation(model, point, centerline, "sgATMIDDLE");
                    int relAfter = SketchRelations(sketches);
                    // FOUR RELATIONS, COUNTED: `SketchAddConstraints` is Void,
                    // and a relation that is not born is not reported by anyone.
                    if (!r1 || !r2 || !r3 || !r4 || (relBefore >= 0 && relAfter - relBefore < 4))
                    {
                        reason = "I could not center along «" + axis.Name + "» between «" + axis.Fra1.Uid + "» and «" +
                                 axis.Fra2.Uid + "» (relations created: " +
                                 (relBefore < 0 ? "?" : (relAfter - relBefore).ToString(CultureInfo.InvariantCulture)) + " of 4).";
                        return false;
                    }
                    said[axis.Name] = new Dictionary<string, object>
                    {
                        ["centered_between"] = new[] { axis.Fra1.Uid, axis.Fra2.Uid },
                    };
                }
                else
                {
                    double its = Coord(axis.Edge.StartMm, axes, k);
                    // The number halfway between point and edge, a little to the side:
                    // only WHERE it is written, not what it measures.
                    double tu = k == 0 ? (x + its) / 2 : x + 6;
                    double tv = k == 0 ? y + 6 : (y + its) / 2;
                    var text = PointInSketch(model, axes, offsetM, tu, tv, out error);
                    if (text == null) { reason = error; return false; }
                    // THE DIRECTION IS READ IN THE SKETCH, not in the model: the
                    // edge runs along the other axis of the face, and in the
                    // sketch that axis can be X or Y (on the right
                    // face x_mm is the Y of the model). The two ends are brought
                    // into the sketch and we look at where it runs.
                    double other1 = Coord(axis.Edge.StartMm, axes, 1 - k), other2 = Coord(axis.Edge.FineMm, axes, 1 - k);
                    var e1 = PointInSketch(model, axes, offsetM, k == 0 ? its : other1, k == 0 ? other1 : its, out error);
                    var e2 = e1 == null ? null
                        : PointInSketch(model, axes, offsetM, k == 0 ? its : other2, k == 0 ? other2 : its, out error);
                    if (e2 == null) { reason = error; return false; }
                    bool verticalEdge = Math.Abs(e2[1] - e1[1]) > Math.Abs(e2[0] - e1[0]);
                    IDimension born;
                    double reread = DimensionPointEdge(model, point, axis.Edge.Edge, text, verticalEdge, out born);
                    if (reread < 0)
                    {
                        reason = "The dimension between the point and the edge «" + axis.Edge.Uid + "» was not created.";
                        return false;
                    }
                    if (Math.Abs(reread - axis.DistanceMm) > PointToleranceMm)
                    {
                        reason = string.Format(CultureInfo.InvariantCulture,
                            "The dimension from the edge «{0}» was created but is {1:0.###} mm instead of {2:0.###}: it measures " +
                            "something else, and I do not keep it.", axis.Edge.Uid, reread, axis.DistanceMm);
                        return false;
                    }
                    axis.CreatedDimension = born;
                    said[axis.Name] = new Dictionary<string, object>
                    {
                        ["edge"] = axis.Edge.Uid,
                        ["distance_mm"] = axis.DistanceMm,
                        ["dimension_reread_mm"] = Math.Round(reread, 3),
                    };
                }
            }

            // FULLY DEFINED, OR NOTHING — but with more points in the same
            // sketch (the holes of `hole_wizard`) it is looked at after the last one.
            if (expectDefined)
            {
                if (!SketchDefinedAndHealthy(sketches, out reason)) return false;
                said["sketch"] = "fully defined";
            }
            // THE DRAWING (risk 15): a centred axis is held by a relation,
            // not by a dimension, and `model_dimensions` has nothing to report.
            if ((anc.X != null && anc.X.Centered) || (anc.Y != null && anc.Y.Centered))
                said["note_drawing"] = "Along the centered axis the position is held by a relation, not by a dimension: " +
                                       "model_dimensions does not report it, drawing_dimension measures it from the borders.";
            measurements["anchor"] = said;
            return true;
        }

        /// <summary>
        /// Is the open sketch fully defined and free of sick relations?
        /// Underconstrained, the profile goes where the solver wants; overconstrained,
        /// it breaks at the edit; a dangling, overconstraining or broken relation
        /// (risk 1 of the critic) is not ruled out by the «defined» state.
        /// </summary>
        private static bool SketchDefinedAndHealthy(ISketchManager sketches, out string reason)
        {
            reason = null;
            int docState = -1;
            try { docState = sketches.ActiveSketch.GetConstrainedStatus(); } catch { docState = -1; }
            if (docState != (int)swConstrainedStatus_e.swFullyConstrained)
            {
                reason = ConstrainedStatusRefusal(docState);
                return false;
            }
            string unhealthy, notRead;
            if (!UnhealthyRelations(sketches, out unhealthy, out notRead))
            {
                reason = "I cannot read the relations of the anchor sketch (" + notRead + "): fully defined is " +
                         "not enough, a dangling, over-defining or broken relation would break it at the first " +
                         "change, and I do not cut without having looked.";
                return false;
            }
            if (unhealthy != null)
            {
                reason = "The sketch of the anchor has relations that are not healthy: " + unhealthy + ". I do not cut, it would break at the first change.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// THE ANCHOR OF `hole_wizard` (entry E18, the last piece). The sketch of the
        /// points is made by HoleWizard, and `PositionHoles` puts the points in it: here
        /// we re-enter it, and each point receives its anchor. Measured on 25/9/2026
        /// (`prova0089 guidato`, case r01): the positioning sketch is
        /// the sub-sketch with the points and without segments, it is opened with
        /// `EditSketch`, eight horizontal/vertical dimensions to the edges make it
        /// fully defined, and after the edit the holes stand at
        /// y -40 -40 100 100 and x -80 -80 140 140: exactly the expectation of r01.
        ///
        /// The definition state is looked at AFTER the last point: a point not
        /// yet anchored leaves the sketch underconstrained by construction.
        /// </summary>
        private bool AnchorHoles(
            IModelDoc2 model, IFeature hole, double[] axes, double offsetM,
            List<double[]> positions, List<Anchoring> anchors,
            Dictionary<string, object> measurements, out string reason)
        {
            reason = null;
            var featSketch = PositioningSketch(hole);
            var sketch = featSketch?.GetSpecificFeature2() as ISketch;
            if (sketch == null) { reason = "I cannot find the sketch that holds the position of the holes."; return false; }
            var handler = model.SketchManager;
            var called = new List<object>();
            bool beingEdited = false;
            try
            {
                model.ClearSelection2(true);
                if (!featSketch.Select2(false, 0)) { reason = "I cannot select the sketch of the holes."; return false; }
                model.EditSketch();
                // EDIT MODE IS READ BACK, NOT ASSUMED (Z-C21.8, finding N56).
                // IModelDoc2~EditSketch.html returns nothing: «If the selection is
                // a feature, its underlying sketch is placed in edit mode». The
                // flag was set without looking, and a sketch that did not open
                // went on to ActiveSketch.ModelToSketchTransform and a .NET null
                // reference. Now the open sketch must be there, and must be the
                // positioning sketch when its name can be read.
                var openSketch = handler.ActiveSketch;
                string openName = null, expectedName = null;
                try { openName = (openSketch as IFeature)?.Name; } catch { openName = null; }
                try { expectedName = featSketch.Name; } catch { expectedName = null; }
                bool anotherSketch = openName != null && expectedName != null &&
                                     !string.Equals(openName, expectedName, StringComparison.Ordinal);
                if (openSketch == null || anotherSketch)
                {
                    beingEdited = openSketch != null;   // another sketch is open: the finally closes it
                    reason = openSketch == null
                        ? "The positioning sketch of the holes did not open for editing (EditSketch returns nothing, " +
                          "and no sketch is active after it): I cannot anchor the holes."
                        : "Editing the positioning sketch «" + expectedName + "» opened «" + openName + "» instead: I " +
                          "cannot anchor the holes.";
                    return false;
                }
                beingEdited = true;
                var math = _sw.GetMathUtility() as IMathUtility;
                var transformation = handler.ActiveSketch.ModelToSketchTransform;
                var points = (handler.ActiveSketch.GetSketchPoints2() as object[]) ?? new object[0];
                for (int i = 0; i < positions.Count; i++)
                {
                    var expected = InSketch(math, transformation, axes, offsetM, positions[i]);
                    var candidates = new List<ISketchPoint>();
                    foreach (var o in points)
                    {
                        var p = o as ISketchPoint;
                        if (p != null && expected != null && Math.Abs(p.X - expected[0]) < 1e-7 && Math.Abs(p.Y - expected[1]) < 1e-7)
                            candidates.Add(p);
                    }
                    if (candidates.Count == 0)
                    {
                        reason = "I cannot find in the sketch the point of hole " + (i + 1) + ".";
                        return false;
                    }
                    // N23: MORE THAN ONE SKETCH POINT CAN ANSWER TO THE SAME
                    // SEARCH WINDOW (1e-7 m above — loose on purpose, since
                    // InSketch's own transform carries some numerical
                    // slack). Up to library 0.146.0 the loop just kept the LAST
                    // one it found: a guess. SameHolePoint proves the candidates
                    // are the same point by the sketch point's own ID, not by
                    // how close they are: two points at the same coordinates are
                    // still two points (two coincident holes in a Hole Wizard
                    // sketch). When it cannot prove it, the call is refused and
                    // names every candidate, in millimetres, with its ID.
                    var candidateXY = new List<double[]>();
                    var candidateIds = new List<long[]>();
                    foreach (ISketchPoint c in candidates)
                    {
                        candidateXY.Add(new[] { c.X, c.Y });
                        candidateIds.Add(SketchPointId(c));
                    }
                    string ambiguity;
                    if (!SameHolePoint(candidateXY.ToArray(), candidateIds.ToArray(), out ambiguity))
                    {
                        reason = "Hole " + (i + 1) + ": " + ambiguity;
                        return false;
                    }
                    var its = candidates[0];
                    var itsMeasure = new Dictionary<string, object>();
                    if (!ApplyAnchor(model, anchors[i], its, axes, offsetM, positions[i][0], positions[i][1],
                                       itsMeasure, out reason, expectDefined: false))
                    {
                        reason = "Hole " + (i + 1) + ": " + reason;
                        return false;
                    }
                    called.Add(itsMeasure.ContainsKey("anchor") ? itsMeasure["anchor"] : null);
                }
                if (!SketchDefinedAndHealthy(handler, out reason)) return false;
                measurements["anchors"] = called.ToArray();
                measurements["hole_sketch"] = "fully defined";
                return true;
            }
            catch (Exception ex) { reason = "The anchor of the holes did not succeed: " + ex.Message; return false; }
            finally
            {
                // THE SELECTION IS CLEARED BEFORE THE SKETCH IS CLOSED (Z-C21.8,
                // finding N56): InsertSketch(true) toggles the sketch, and with no
                // sketch open it starts a new one on the selected plane or face.
                // The anchoring leaves points and edges selected behind it; with
                // the selection empty, the toggle can only close.
                try { model.ClearSelection2(true); } catch { }
                if (beingEdited) { try { handler.InsertSketch(true); } catch { } }
                try { model.ClearSelection2(true); } catch { }
            }
        }

        /// <summary>
        /// N23: are the sketch points AnchorHoles found within its search
        /// window the SAME point, or an ambiguity to refuse instead of guess?
        ///
        /// SAME POINT MEANS SAME ID, NOT SAME PLACE. ISketchPoint~GetID.html,
        /// Remarks: «Each point within a specific sketch has a unique ID», an
        /// ordered pair whose «combination ... is always unique within a
        /// specific sketch». All candidates come from one GetSketchPoints2 of
        /// one sketch, so equal IDs are one point met more than once, and
        /// different IDs are different points however close they sit: two
        /// points at the very same coordinates are two holes on top of each
        /// other in a Hole Wizard sketch, and anchoring either one would be the
        /// guess this file's own rule refuses. A candidate
        /// whose ID could not be read (null) proves nothing either.
        ///
        /// Pure (numbers only, no interop), so Oce.Solid.Agent.ContractTests can drive the
        /// duplicate case a bench case cannot build on purpose.
        /// <paramref name="candidatesXY"/> holds one {x, y} pair per candidate,
        /// in metres, only for the message; <paramref name="candidateIds"/> the
        /// matching {i1, i2} IDs (<see cref="SketchPointId"/>). Empty or null
        /// answers false with no ambiguity text (the caller's own "no
        /// candidate" refusal covers that case).
        /// </summary>
        internal static bool SameHolePoint(double[][] candidatesXY, long[][] candidateIds, out string ambiguity)
        {
            ambiguity = null;
            if (candidatesXY == null || candidatesXY.Length == 0) return false;
            if (candidatesXY.Length == 1) return true;

            bool unreadId = candidateIds == null || candidateIds.Length != candidatesXY.Length;
            for (int c = 0; !unreadId && c < candidateIds.Length; c++)
                if (candidateIds[c] == null || candidateIds[c].Length < 2) unreadId = true;
            bool sameId = !unreadId;
            for (int c = 1; sameId && c < candidateIds.Length; c++)
                if (candidateIds[c][0] != candidateIds[0][0] || candidateIds[c][1] != candidateIds[0][1])
                    sameId = false;
            if (sameId) return true;

            // A plain loop, not a LINQ projection: the obsolete-call scanner of
            // Oce.Solid.Agent.ContractTests reads a bare ".Select(" on an untyped receiver as
            // the old selection call.
            var found = new List<string>();
            for (int c = 0; c < candidatesXY.Length; c++)
            {
                double[] q = candidatesXY[c];
                long[] id = candidateIds != null && c < candidateIds.Length ? candidateIds[c] : null;
                found.Add(string.Format(CultureInfo.InvariantCulture, "({0:0.######}, {1:0.######}) ID {2}",
                                        q[0] * 1000.0, q[1] * 1000.0,
                                        id != null && id.Length >= 2 ? id[0] + "," + id[1] : "unread"));
            }
            ambiguity = candidatesXY.Length + " sketch points answer to the same position, and " +
                        (unreadId
                            ? "the ID of at least one could not be read, so nothing proves they are the same point"
                            : "their IDs differ, so they are not the same point") +
                        ": I will not guess which one is the hole's. Points found (mm): " +
                        string.Join("; ", found) + ".";
            return false;
        }

        /// <summary>
        /// The ID of a sketch point, {i1, i2} (ISketchPoint~GetID.html, Return
        /// Value: «Array with two longs or two integers ... identifying this
        /// sketch point ID»), or null when it cannot be read: null is «not
        /// known», and <see cref="SameHolePoint"/> does not prove anything with
        /// it.
        /// </summary>
        private static long[] SketchPointId(ISketchPoint point)
        {
            try
            {
                var pair = point == null ? null : point.GetID() as Array;
                if (pair == null || pair.Length < 2) return null;
                return new[]
                {
                    Convert.ToInt64(pair.GetValue(0), CultureInfo.InvariantCulture),
                    Convert.ToInt64(pair.GetValue(1), CultureInfo.InvariantCulture),
                };
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// The anchor of a guided hole is read before the diameter is known (the
        /// ISO table decides it, later): here, once the hole exists, no anchor
        /// may overrun an edge with the real radius.
        ///
        /// CENTRED AXES TOO, AND THE WIDEST CUT (Z-C21.2, finding N50). This
        /// check skipped centred axes (`!asse.Centrata`), and ReadAnchor runs
        /// with the half sizes at 0 for hole_wizard, so its rule «the profile fits
        /// between the two edges» (the «non ci sta» refusal in ReadAnchor)
        /// never fired: an M8 centred between two edges 6 mm apart was
        /// confirmed, and the hole cut through both of them. The same rule is
        /// applied here, half the gap against the radius, with the same `Coord`
        /// reading of the two edges; equal is allowed, as for a distance.
        /// <paramref name="radiusMm"/> is the radius of the widest cut of the
        /// hole (counterbore or countersink included, see HoleWizard), and
        /// <paramref name="widestCut"/> names it in the message.
        /// </summary>
        private static string BypassWithRadius(List<Anchoring> anchors, double[] axes, double radiusMm, string widestCut)
        {
            for (int i = 0; i < anchors.Count; i++)
                foreach (var axis in anchors[i].Axes())
                {
                    if (axis.Centered)
                    {
                        double gap = Math.Abs(Coord(axis.Fra2.StartMm, axes, axis.Index) -
                                              Coord(axis.Fra1.StartMm, axes, axis.Index));
                        if (gap / 2 + 1e-9 < radiusMm)
                            return string.Format(CultureInfo.InvariantCulture,
                                "Hole {0}: between «{1}» and «{2}» there are {3:0.##} mm along «{4}», and the {5} is " +
                                "{6:0.##} mm wide: it does not fit, and it would cut through both edges.",
                                i + 1, axis.Fra1.Uid, axis.Fra2.Uid, gap, axis.Name, widestCut, 2 * radiusMm);
                    }
                    else if (axis.DistanceMm + 1e-9 < radiusMm)
                        return string.Format(CultureInfo.InvariantCulture,
                            "Hole {0}: «distance_mm» {1:0.##} from edge «{2}» is less than the radius of the {3} " +
                            "({4:0.##} mm): the {3} would cut past the edge.",
                            i + 1, axis.DistanceMm, axis.Edge.Uid, widestCut, radiusMm);
                }
            return null;
        }

        /// <summary>
        /// The refusal for an anchor sketch that is not fully defined, by its
        /// status (Z-C21.8, finding N53). swConstrainedStatus_e.html has seven
        /// values; the old name covered four (under, over, no solution, invalid
        /// solution) and had only an «I do not know» for swUnknownConstraint 1
        /// and swAutosolveOff 7. With automatic solving off the refusal said the
        /// profile «would go where the solver puts it», which is not the
        /// trouble: the solver is not running. -1 is a status that could not be
        /// read. Kept apart from the document so that Oce.Solid.Agent.ContractTests can call it
        /// without SOLIDWORKS.
        /// </summary>
        internal static string ConstrainedStatusRefusal(int status)
        {
            string code = status.ToString(CultureInfo.InvariantCulture);
            switch (status)
            {
                case (int)swConstrainedStatus_e.swAutosolveOff:
                    return "The anchor sketch cannot tell whether it is fully defined: automatic solving of sketches " +
                           "is off (status " + code + ", swAutosolveOff), so SOLIDWORKS is not solving it. I do not cut. " +
                           "Turn automatic solving of sketches back on, or cut without an anchor.";
                case (int)swConstrainedStatus_e.swUnknownConstraint:
                    return "SOLIDWORKS does not know whether the anchor sketch is fully defined (status " + code +
                           ", swUnknownConstraint). I do not cut: a sketch that is not known to be fully defined may " +
                           "move at the first change of the part.";
                case -1:
                    return "I could not read whether the anchor sketch is fully defined (GetConstrainedStatus gave no " +
                           "answer). I do not cut: a sketch that is not known to be fully defined may move at the first " +
                           "change of the part.";
                default:
                    return "With the anchor the sketch should be fully defined, and it is not (status " + code + ": " +
                           ConstrainedStatusName(status) + "). I do not cut: at the first change of the part the profile " +
                           "would go where the solver puts it.";
            }
        }

        /// <summary>The name of a swConstrainedStatus_e value, all seven (finding N53).</summary>
        internal static string ConstrainedStatusName(int status)
        {
            switch (status)
            {
                case (int)swConstrainedStatus_e.swUnknownConstraint: return "unknown";
                case (int)swConstrainedStatus_e.swUnderConstrained: return "under defined";
                case (int)swConstrainedStatus_e.swFullyConstrained: return "fully defined";
                case (int)swConstrainedStatus_e.swOverConstrained: return "over defined";
                case (int)swConstrainedStatus_e.swNoSolution: return "no solution";
                case (int)swConstrainedStatus_e.swInvalidSolution: return "invalid solution";
                case (int)swConstrainedStatus_e.swAutosolveOff: return "automatic solving off";
                default: return "not a swConstrainedStatus_e value";
            }
        }

        /// <summary>
        /// THE MOVEMENT TEST (risk 1 of the critic): every anchor dimension +0.1
        /// mm, rebuild, and the new feature and its sketch must not
        /// go into error; then the value is put back and read again. Returns null if
        /// it holds, the reason if not.
        /// </summary>
        private string MovementTest(IModelDoc2 model, Anchoring anc, IFeature cut)
        {
            if (anc == null || anc.Empty) return null;
            foreach (var axis in anc.Axes())
            {
                // A CENTRED AXIS HAS NO DIMENSION TO MOVE (Z-C21.2, finding
                // N51): the sgATMIDDLE relation on the centre line holds it, and
                // skipping it is the design. A DISTANCE axis without its
                // dimension, or whose dimension cannot be read, is a different
                // thing: both went through a `continue`, and the test «passed»
                // having moved nothing, the pattern that M12 closed below for
                // the return of SetSystemValue3. Now they stop the test.
                if (axis.Centered) continue;
                var q = axis.CreatedDimension;
                if (q == null)
                    return MovementTestNotVerifiable(axis, "the anchor dimension was not kept when it was created");
                double era;
                try { era = ApiHelpers.ReadDimensionInConfiguration(q, ""); }
                catch (Exception ex)
                {
                    return MovementTestNotVerifiable(axis, "I cannot read the anchor dimension: " + ex.Message);
                }
                string broken = null;
                string stop = null;
                string notVerified = null;
                try
                {
                    // THE RETURN IS READ (entry Z-C21.2, finding M12).
                    // IDimension~SetSystemValue3.html: «Return Value Success
                    // indicator value as defined in swSetValueReturnStatus_e»
                    // (Successful = 0; Failure = 1, InvalidValue = 2,
                    // DrivenDimension = 3, ModelNotLoaded = 4, FrozenFeatureOwner
                    // = 5, swconst inventory). It was discarded: if the dimension did not
                    // move, the rebuild found no errors because nothing had
                    // changed, and the test «passed» without having tested.
                    // The value is read back too: moved means moved.
                    int moveOutcome = q.SetSystemValue3(era + 0.0001, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
                    if (moveOutcome != (int)swSetValueReturnStatus_e.swSetValue_Successful)
                        stop = "SOLIDWORKS answered " + moveOutcome.ToString(CultureInfo.InvariantCulture) +
                                " to SetSystemValue3 (" + ErrorCodeDecoder.SetValueStatus(moveOutcome) + ")";
                    else
                    {
                        model.EditRebuild3();
                        double moved = era;
                        try { moved = ApiHelpers.ReadDimensionInConfiguration(q, ""); } catch { }
                        if (Math.Abs(moved - (era + 0.0001)) > 1e-9)
                            stop = "read back, the dimension did not move";
                        else
                            broken = FeatureInError(cut, out notVerified);
                    }
                }
                catch (Exception ex) { broken = ex.Message; }
                finally
                {
                    try
                    {
                        q.SetSystemValue3(era, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
                        model.EditRebuild3();
                    }
                    catch { }
                }
                if (stop != null)
                    return "I could not move the anchor dimension «" + axis.Name + "» by a tenth (" + stop +
                           "): the movement check proved nothing, and I do not confirm an anchor that I " +
                           "cannot make move.";
                if (notVerified != null)
                    return MovementTestNotVerifiable(axis, "the anchor dimension moved, but " + notVerified);
                if (broken != null)
                    return "Moving the anchor dimension «" + axis.Name + "» by a tenth, the cut goes into error (" + broken +
                           "): the sketch does not hold up to a change, and an anchor that does not hold up to a change is of no use.";
                double returned = -1;
                try { returned = ApiHelpers.ReadDimensionInConfiguration(q, ""); } catch { }
                if (Math.Abs(returned - era) > 1e-9)
                    return "After the movement check the anchor dimension «" + axis.Name + "» did not return to its value.";
            }
            return null;
        }

        /// <summary>
        /// The movement test that could not test anything, in words: an anchor
        /// that cannot be tested is not confirmed (Z-C21.2, finding N51).
        /// </summary>
        private static string MovementTestNotVerifiable(AnchorAxis axis, string why)
        {
            return "The movement test of the anchor on «" + axis.Name + "» is not verifiable (" + why +
                   "): it would pass without having tested anything, and I do not confirm an anchor I cannot test.";
        }

        /// <summary>
        /// The feature or its sketch in error, in words; null if both are
        /// healthy. <paramref name="notVerified"/> is not null when the error
        /// codes could not be read (Z-C21.2, finding N51): before, a `catch { }`
        /// answered null, that is «healthy», and the movement test passed
        /// without having looked. IFeature~GetErrorCode2.html, «Return Value
        /// Feature error code as defined in swFeatureError_e», is read through
        /// ApiHelpers.RebuildError(feature, out known), which says when the call
        /// threw. A feature without a sketch is not a gap:
        /// IFeature~GetFirstSubFeature.html, «If no sub-features exist for this
        /// feature, then this method returns NULL».
        /// </summary>
        private static string FeatureInError(IFeature cut, out string notVerified)
        {
            notVerified = null;
            if (cut == null)
            {
                notVerified = "there is no feature to watch";
                return null;
            }
            try
            {
                // S11: ApiHelpers.RebuildError (GetErrorCode2, warnings excluded),
                // which also says the code in words (FeatureErrors), as the summary promises.
                bool known;
                var cutError = ApiHelpers.RebuildError(cut, out known);
                if (!known)
                {
                    notVerified = "GetErrorCode2 did not answer on the feature";
                    return null;
                }
                if (cutError != null) return cutError;
                var sketch = cut.GetFirstSubFeature() as IFeature;
                if (sketch == null) return null;
                var sketchError = ApiHelpers.RebuildError(sketch, out known);
                if (!known)
                {
                    notVerified = "GetErrorCode2 did not answer on its sketch";
                    return null;
                }
                if (sketchError != null) return "sketch: " + sketchError;
            }
            catch (Exception ex)
            {
                notVerified = "reading its rebuild errors failed: " + ex.Message;
                return null;
            }
            return null;
        }

        /// <summary>
        /// Removes a cut just made and its sketch, when the anchor does not
        /// hold (risk 11 of the critic): a feature that does not survive an
        /// edit is not left in the part with «you take it back». Returns the tail
        /// of the message: removed and part as it was, or what was left.
        ///
        /// «It said it removed it» is not enough, as in `DeleteFeature`:
        /// the features are recounted and the volume is measured again.
        /// </summary>
        private string RemoveCut(IModelDoc2 model, IFeature cut, double? volumeBefore)
        {
            bool removed = false;
            try
            {
                model.ClearSelection2(true);
                if (cut.Select2(false, 0))
                    removed = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch { }
            finally { try { model.ClearSelection2(true); } catch { } }
            if (!removed) return RemainsInPart;
            try { model.EditRebuild3(); } catch { }
            int features = CountFeature(model);
            if (features < 0 || _featuresAtStepStart < 0)
                return " I removed it, with its sketch, but I could not recount " +
                       "the tree to check that the part is as it was: look at it.";
            if (features > _featuresAtStepStart)
                return RemovedButTreeLonger(features - _featuresAtStepStart);
            double? volumeAfter = VolumeMm3(model);
            if (volumeBefore == null || volumeAfter == null)
                return " I removed it, with its sketch, but I could not measure the " +
                       "volume again to check that the part is as it was: look at it.";
            if (Math.Abs(volumeAfter.GetValueOrDefault() - volumeBefore.GetValueOrDefault()) > 0.01)
                return RemovedButVolumeChanged(volumeAfter.GetValueOrDefault(), volumeBefore.GetValueOrDefault());
            return " I removed it, with its sketch: the part is as it was before this step.";
        }

        /// <summary>
        /// The tail of a refusal when DeleteSelection2 said it removed what the
        /// step had made, and the tree read back is still LONGER than before the
        /// step (Z-C21.9, finding C9-6). Something of this step is still there,
        /// but not necessarily the feature that was removed: until library
        /// 0.144.0 the three removal helpers (RemoveCut, RemoveFill,
        /// RemoveGusset) answered «it is still in the part: undo it» about
        /// a feature that was gone. The same words as the face fillet
        /// (RemoveFaceFillet): the features in excess, counted.
        /// </summary>
        internal static string RemovedButTreeLonger(int extraFeatures)
        {
            return " I tried to remove it, but the tree still has " +
                   extraFeatures.ToString(CultureInfo.InvariantCulture) +
                   (extraFeatures == 1 ? " feature" : " features") + " more than before this step: undo " +
                   (extraFeatures == 1 ? "it" : "them") + " (" + Sentences.HowToWithdrawIt + ").";
        }

        /// <summary>
        /// The tail of a refusal when DeleteSelection2 removed what the step had
        /// made and the tree is back as it was, but the volume read back is not
        /// the one before the step (Z-C21.9, finding C9-6). The removed feature
        /// is gone, so the sentence says so and gives the new volume, as the
        /// face fillet does (RemoveFaceFillet), instead of «it is still in
        /// the part: undo it».
        /// </summary>
        internal static string RemovedButVolumeChanged(double volumeNowMm3, double volumeBeforeMm3)
        {
            return " I removed it, but the volume is now " +
                   volumeNowMm3.ToString("0.##", CultureInfo.InvariantCulture) + " mm3 instead of " +
                   volumeBeforeMm3.ToString("0.##", CultureInfo.InvariantCulture) +
                   ": look at the part before going on.";
        }

        /// <summary>
        /// A dimension between a point of the sketch and an edge of the MODEL, with the
        /// text in `text` (sketch coordinates). Returns the value read back in
        /// mm, or -1. The little value window is turned off and put back as it was.
        ///
        /// HORIZONTAL OR VERTICAL, and not `Extension.AddDimension`. Measured on
        /// 25/9/2026 on the front, back and right faces: with a sketch point
        /// and a model edge selected, `Extension.
        /// AddDimension` returns null in all four directions — the
        /// call that entry GP6 recommends for segments creates nothing
        /// here. `AddHorizontalDimension2` and `AddVerticalDimension2` give
        /// exactly 20 and 30; `AddDimension2` too, but it is forbidden by contract
        /// (no direction). The choice is made from the direction of the edge IN THE
        /// SKETCH: vertical edge, horizontal dimension.
        /// </summary>
        private double DimensionPointEdge(
            IModelDoc2 model, ISketchPoint point, IEdge edge, double[] text, bool verticalEdge,
            out IDimension born)
        {
            born = null;
            bool asked = true;
            try
            {
                asked = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            }
            catch { }
            try
            {
                model.ClearSelection2(true);
                if (!point.Select4(false, null)) return -1;
                if (!((IEntity)edge).Select4(true, null)) return -1;
                var dimension = (verticalEdge
                    ? model.AddHorizontalDimension2(text[0], text[1], text[2])
                    : model.AddVerticalDimension2(text[0], text[1], text[2])) as IDisplayDimension;
                born = dimension?.GetDimension2(0);
                if (born == null) return -1;
                try { dimension.MarkedForDrawing = true; } catch { }
                return ApiHelpers.ReadDimensionInConfiguration(born, "") * 1000.0;
            }
            catch { return -1; }
            finally
            {
                try { model.ClearSelection2(true); } catch { }
                try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, asked); } catch { }
            }
        }

        /// <summary>
        /// THE STADIUM OF THE SLOT, DEFINED (entry E18). `DrawStadium` draws
        /// it with four entities under `AddToDB` and that is all: no relations,
        /// no dimensions. Here is added what makes it a slot even after
        /// an edit, and — if there is one — the anchor. Returns how many dimensions it put;
        /// `error` not null means the sketch is not as needed (with
        /// the anchor it is a refusal, without it it is only one dimension fewer).
        ///
        /// MEASURED on 25/9/2026 (`prova0089 stadio`, `stadio2`), plate 200 x
        /// 120, slot 60 x 20 at 0 and at 30 degrees:
        ///
        ///   the ends drawn with AddToDB         already merged: 6 points (4 fillets and
        ///                                       2 centres), the coincidences are not
        ///                                       needed
        ///   4 tangencies + equal radii          +5 relations
        ///   dimension on the arc                the RADIUS (10)
        ///   dimension between the two centres   null, with both calls:
        ///                                       a construction midline tied to the
        ///                                       centres is needed, and it is dimensioned (40)
        ///   orientation at 0 degrees            horizontal on the midline
        ///   orientation at 30 degrees           Extension.AddDimension null,
        ///                                       AddDimension2 30 (forbidden, GP6),
        ///                                       AddSpecificDimension ANGULAR 30
        ///   radius 15 and centres at 60 after   volume expected to the hundredth, zero
        ///                                       errors: it stays a slot
        ///
        /// Without an anchor the oblique slot stays free to rotate (no edge
        /// to measure the angle from): this is said in the measurements. With the anchor the angle
        /// is dimensioned against its first edge.
        /// </summary>
        private int DefineStadium(
            IModelDoc2 model, ISketchSegment[] stadium, double[] axes, double offsetM,
            double x, double y, double angleDeg, double travel, double radius,
            Anchoring anchor, Dictionary<string, object> measurements, out string error)
        {
            error = null;
            if (stadium == null || stadium.Length < 4 || stadium.Any(s => s == null))
            {
                error = "The stadium does not have its four pieces: I cannot define it.";
                return 0;
            }
            ISketchSegment l1 = stadium[0], l2 = stadium[1], a1 = stadium[2], a2 = stadium[3];
            var arcB = a1 as ISketchArc;   // a1 is the end on the +stroke side
            var arcA = a2 as ISketchArc;
            if (arcA == null || arcB == null)
            {
                error = "The ends of the slot are not arcs: I cannot define it.";
                return 0;
            }
            var sketches = model.SketchManager;

            // TANGENCIES AND EQUAL RADII: four line-arcs, and the two equal arcs.
            int relBefore = SketchRelations(sketches);
            foreach (var pair in new[] { new[] { l1, a1 }, new[] { l1, a2 }, new[] { l2, a1 }, new[] { l2, a2 } })
                Relation(model, pair[0], pair[1], "sgTANGENT");
            Relation(model, a1, a2, "sgSAMELENGTH");
            // THE FIVE ARE COUNTED HERE, BEFORE THE CENTRE LINE (Z-C21.2,
            // finding N54). IModelDoc2~SketchAddConstraints.html gives it no
            // return value (C# «void SketchAddConstraints»), so the relation
            // count is the only proof that a tangency was born. It was taken
            // after the two coincidences below, which are requested exactly for
            // the case in which the centre line does NOT fuse with the centres:
            // then they add two relations, and two missing tangencies passed as
            // «5 of 5».
            int stadiumRelations = SketchRelations(sketches);
            if (relBefore < 0 || stadiumRelations < 0)
                measurements["slot_relations"] = "not counted: SOLIDWORKS did not give the relation count of the " +
                                           "sketch, so I could not check that the four tangencies and the " +
                                           "equal radii were born";
            else if (stadiumRelations - relBefore < 5)
            {
                error = "Not all the relations of the slot were born (" +
                         (stadiumRelations - relBefore).ToString(CultureInfo.InvariantCulture) +
                         " of 5: four tangencies and the equal radii).";
                // Without an anchor the caller does not stop on `error` (a slot
                // that is only less constrained is still cut): the response says it.
                measurements["slot_relations"] = (stadiumRelations - relBefore).ToString(CultureInfo.InvariantCulture) +
                                           " of 5 born (four tangencies and the equal radii): after a change the " +
                                           "slot may lose its shape";
            }

            // THE MIDLINE BETWEEN THE CENTRES, construction and tied to both.
            double rad = angleDeg * Math.PI / 180.0, cos = Math.Cos(rad), sin = Math.Sin(rad);
            string e1;
            var cA = PointInSketch(model, axes, offsetM, x - travel * cos, y - travel * sin, out e1);
            var cB = cA == null ? null : PointInSketch(model, axes, offsetM, x + travel * cos, y + travel * sin, out e1);
            if (cB == null) { error = e1; return 0; }
            ISketchSegment centerline;
            bool addBefore = sketches.AddToDB;
            try
            {
                sketches.AddToDB = true;
                centerline = sketches.CreateCenterLine(cA[0], cA[1], cA[2], cB[0], cB[1], cB[2]);
            }
            finally { sketches.AddToDB = addBefore; }
            var line = centerline as ISketchLine;
            if (line == null) { error = "The midline between the centers of the slot was not created."; return 0; }
            // Drawn under AddToDB exactly on the centres, the midline
            // MERGES with them (measured: the coincidences add no relations). They are
            // asked for all the same, in case a rounding left them apart; the count that counts
            // is that of the other five.
            // The points go in with `as` (Z-C21.8, finding N49): a null point
            // makes `Relation` answer false without an exception, and these two
            // relations are optional by design, so their answer is not needed.
            Relation(model, line.GetStartPoint2() as ISketchPoint, arcA.GetCenterPoint2() as ISketchPoint, "sgCOINCIDENT");
            Relation(model, line.GetEndPoint2() as ISketchPoint, arcB.GetCenterPoint2() as ISketchPoint, "sgCOINCIDENT");

            // THE DIMENSIONS: the radius and the midline (the length between the centres).
            string e2;
            var textR = PointInSketch(model, axes, offsetM,
                x + (travel + radius + 6) * cos, y + (travel + radius + 6) * sin + 6, out e2);
            var textL = PointInSketch(model, axes, offsetM,
                x - (radius + 8) * sin, y + (radius + 8) * cos, out e2);
            var toDimension = new List<Dimension>();
            if (textR != null) toDimension.Add(Dimension.Su(a1, textR));
            if (textL != null) toDimension.Add(Dimension.Su(centerline, textL));
            int quote = DimensionSegments(model, toDimension);
            if (quote < 2 && error == null) error = "The dimensions of the slot were not all created (" + quote + " of 2).";

            // THE ORIENTATION. Horizontal or vertical midline IN THE SKETCH:
            // a relation. Oblique: an angle against the first edge of
            // the anchor, if there is one; otherwise it stays free, and this is said.
            bool horizontal = Math.Abs(cB[1] - cA[1]) < 1e-9, vertical = Math.Abs(cB[0] - cA[0]) < 1e-9;
            if (horizontal || vertical)
                SingleRelation(model, centerline, horizontal ? "sgHORIZONTAL2D" : "sgVERTICAL2D");
            else if (!anchor.Empty)
            {
                var axis = anchor.X ?? anchor.Y;
                var reference = axis.Centered ? axis.Fra1 : axis.Edge;
                string e3;
                var textA = PointInSketch(model, axes, offsetM,
                    x + (travel + radius + 14) * cos, y + (travel + radius + 14) * sin, out e3);
                // The angle the dimension must read: between the slot line and
                // the reference edge, both on the face (Z-C21.2, finding N55).
                // It is not `angleDeg` itself: against an edge that runs
                // along y (the edge of an «x» anchor) a slot at 30 degrees reads
                // 60, or its supplement.
                double edgeDeg = Math.Atan2(
                    Coord(reference.FineMm, axes, 1) - Coord(reference.StartMm, axes, 1),
                    Coord(reference.FineMm, axes, 0) - Coord(reference.StartMm, axes, 0)) * 180.0 / Math.PI;
                string angleNotKept = null;
                if (textA == null ||
                    !AngleDimension(model, centerline, reference.Edge, textA,
                                 AngleBetweenLinesDeg(angleDeg, edgeDeg), out angleNotKept))
                {
                    if (error == null)
                        error = "The angle of the slot against edge «" + reference.Uid + "» " +
                                 (angleNotKept ?? "was not born.");
                }
                else quote++;
            }
            else
                measurements["orientation"] = "free: a slanted slot without an anchor has no edge to measure " +
                                         "the angle from, and after a change it can rotate.";

            // THE ANCHOR, on the point halfway along the midline.
            if (!anchor.Empty)
            {
                if (error != null) return quote;
                string e4;
                var center = PointInSketch(model, axes, offsetM, x, y, out e4);
                if (center == null) { error = e4; return quote; }
                ISketchPoint point;
                try
                {
                    sketches.AddToDB = true;
                    point = sketches.CreatePoint(center[0], center[1], center[2]);
                }
                finally { sketches.AddToDB = addBefore; }
                if (point == null || !Relation(model, point, centerline, "sgATMIDDLE"))
                {
                    error = "I could not put the point at the center of the slot.";
                    return quote;
                }
                string e5;
                if (!ApplyAnchor(model, anchor, point, axes, offsetM, x, y, measurements, out e5))
                    error = e5;
            }
            return quote;
        }

        /// <summary>
        /// The centre of the contour of `extrude_on_face` to which the anchor is attached
        /// (entry E18), and the half extent per axis. It holds for ONE contour only:
        /// a circle, or a polyline of four vertices aligned with the axes
        /// of the face (a rectangle). For everything else a refusal that says
        /// why: which one is «the centre» of any polygon is decided by whoever
        /// draws it, and guessing it would mean anchoring a point that nobody
        /// chose.
        /// </summary>
        private static bool ContourCenterOnly(
            List<Contour> contours, out double cx, out double cy, out double halfU, out double halfV,
            out string reason)
        {
            cx = cy = halfU = halfV = 0;
            reason = null;
            const string Rule =
                "The anchor of a boss holds ONE contour only, round or rectangular and aligned " +
                "with the axes of the face: the point it holds is its center. ";
            int howMany = contours == null ? -1 : contours.Count;
            if (howMany != 1)
            {
                reason = Rule + (howMany < 0 ? "There are no contours here." : "Here the contours are " + howMany +
                         ": make one boss per contour, and anchor each one.");
                return false;
            }
            var c = contours[0];
            if (c.Circle)
            {
                cx = c.Center[0]; cy = c.Center[1];
                halfU = halfV = c.Diameter / 2.0;
                return true;
            }
            var points = new List<double[]>();
            if (c.Strokes.Count == 1 && !c.Strokes[0].Spline)
                foreach (var p in c.Strokes[0].Points)
                    if (!points.Any(q => Math.Abs(q[0] - p[0]) < 1e-9 && Math.Abs(q[1] - p[1]) < 1e-9))
                        points.Add(p);
            bool rectangle = points.Count == 4;
            for (int i = 0; rectangle && i < 4; i++)
            {
                var p = points[i]; var q = points[(i + 1) % 4];
                bool horizontal = Math.Abs(p[1] - q[1]) < 1e-9, vertical = Math.Abs(p[0] - q[0]) < 1e-9;
                if (horizontal == vertical) rectangle = false;
            }
            if (!rectangle)
            {
                reason = Rule + "This contour is neither a circle nor an aligned rectangle.";
                return false;
            }
            double minX = points.Min(p => p[0]), maxX = points.Max(p => p[0]);
            double minY = points.Min(p => p[1]), maxY = points.Max(p => p[1]);
            cx = (minX + maxX) / 2; cy = (minY + maxY) / 2;
            halfU = (maxX - minX) / 2; halfV = (maxY - minY) / 2;
            return true;
        }

        /// <summary>
        /// Defines the contour of the boss and attaches the anchor to it, with the sketch
        /// open (entry E18). Circle: the diameter, and the anchor on the centre.
        /// Rectangle of four lines (the corners, drawn under AddToDB, are
        /// already merged — measured on the slot): horizontals and verticals, two dimensions,
        /// and the anchor on the centre built with the diagonal, like the pocket.
        /// </summary>
        private bool AnchorContour(
            IModelDoc2 model, Contour contour, List<ISketchSegment> created, double[] axes, double offsetM,
            double cx, double cy, Anchoring anchor, Dictionary<string, object> measurements, out string error)
        {
            error = null;
            ISketchPoint point;
            if (contour.Circle)
            {
                var circle = created.Count == 1 ? created[0] as ISketchArc : null;
                if (circle == null) { error = "The circle of the boss was not created as expected."; return false; }
                var text = PointInSketch(model, axes, offsetM,
                    cx + contour.Diameter * 0.7 + 5, cy + contour.Diameter * 0.7 + 5, out error);
                if (text == null || DimensionSegments(model, new List<Dimension> { Dimension.Su(created[0], text) }) < 1)
                {
                    error = error ?? "The diameter of the boss could not be dimensioned.";
                    return false;
                }
                point = circle.GetCenterPoint2() as ISketchPoint;
            }
            else
            {
                if (created.Count != 4 || created.Any(s => !(s is ISketchLine)))
                {
                    error = "The rectangle of the boss does not have its four sides.";
                    return false;
                }
                // THE ENDS ARE CHECKED (Z-C21.8, finding N49): ISketchLine~
                // GetStartPoint2.html, «Start sketch point or NULL if operation
                // fails». A null end reached .X and gave the bare .NET message;
                // the twin in RectangleCenter already skipped nulls.
                const string noEnd =
                    "A side of the rectangle of the boss gives no end point (GetStartPoint2 / GetEndPoint2 answered " +
                    "null): I cannot anchor the rectangle.";
                var angles = new List<double[]>();
                foreach (var s in created)
                {
                    var l = (ISketchLine)s;
                    var a0 = l.GetStartPoint2() as ISketchPoint;
                    var a1 = l.GetEndPoint2() as ISketchPoint;
                    if (a0 == null || a1 == null) { error = noEnd; return false; }
                    bool horizontal = Math.Abs(a0.Y - a1.Y) < 1e-9;
                    SingleRelation(model, s, horizontal ? "sgHORIZONTAL2D" : "sgVERTICAL2D");
                    angles.Add(new[] { a0.X, a0.Y, a0.Z });
                }
                // The two dimensions: one side per direction, with the text outside the rectangle.
                var p0 = ((ISketchLine)created[0]).GetStartPoint2() as ISketchPoint;
                var p1 = ((ISketchLine)created[0]).GetEndPoint2() as ISketchPoint;
                var q1 = ((ISketchLine)created[1]).GetEndPoint2() as ISketchPoint;
                if (p0 == null || p1 == null || q1 == null) { error = noEnd; return false; }
                var toDimension = new List<Dimension>
                {
                    Dimension.Su(created[0], new[] { (p0.X + p1.X) / 2 + (p1.X - q1.X) * 0.2, (p0.Y + p1.Y) / 2 + (p1.Y - q1.Y) * 0.2, 0.0 }),
                    Dimension.Su(created[1], new[] { (p1.X + q1.X) / 2 + (p1.X - p0.X) * 0.2, (p1.Y + q1.Y) / 2 + (p1.Y - p0.Y) * 0.2, 0.0 }),
                };
                if (DimensionSegments(model, toDimension) < 2)
                {
                    error = "The dimensions of the rectangle of the boss were not all created.";
                    return false;
                }
                point = RectangleCenter(model, created.Cast<object>().ToArray(), angles[0], angles[2]);
                if (point == null)
                {
                    error = "I could not build the center of the rectangle to attach the anchor to.";
                    return false;
                }
            }
            return ApplyAnchor(model, anchor, point, axes, offsetM, cx, cy, measurements, out error);
        }

        /// <summary>
        /// An ANGULAR dimension between a line of the sketch and an edge of the
        /// model. `AddSpecificDimension` with the explicit type: `Extension.
        /// AddDimension` returns null on this pair, and `AddDimension2` is
        /// forbidden (GP6). Measured on 25/9/2026: 30 degrees out of 30.
        ///
        /// THE STATUS AND THE VALUE ARE READ (Z-C21.2, finding N55).
        /// IModelDocExtension~AddSpecificDimension.html: «Error Result status as
        /// defined in swAddSpecificDimension_e», and swAddSpecificDimension_e
        /// has swAddSpecificDimension_Success 0 and
        /// swAddSpecificDimension_DimTypeMismatch 1. Only `quota == null` was
        /// checked, and the caller counted a dimension of unknown type and
        /// value. Now the status must be Success and the value, read back,
        /// must be <paramref name="betweenDeg"/> or its supplement: an angular
        /// dimension between two lines shows one of the two, depending on
        /// where its text sits, like DimensionPointEdge refuses a dimension that
        /// measures something else. False with <paramref name="whyNot"/> null
        /// means «not born» (the caller's message); not null says what is wrong.
        /// </summary>
        private bool AngleDimension(IModelDoc2 model, ISketchSegment line, IEdge edge, double[] text,
                                 double betweenDeg, out string whyNot)
        {
            whyNot = null;
            bool asked = true;
            try
            {
                asked = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            }
            catch { }
            try
            {
                model.ClearSelection2(true);
                if (!line.Select4(false, null) || !((IEntity)edge).Select4(true, null)) return false;
                int error = 0;
                var dimension = model.Extension.AddSpecificDimension(
                    text[0], text[1], text[2], (int)swDimensionType_e.swAngularDimension, ref error) as IDisplayDimension;
                if (dimension == null) return false;
                if (error != (int)swAddSpecificDimension_e.swAddSpecificDimension_Success)
                {
                    whyNot = "was born, but AddSpecificDimension answered " + (swAddSpecificDimension_e)error +
                             ": I do not keep a dimension of the wrong type.";
                    return false;
                }
                double readDeg;
                try { readDeg = Units.Degrees(ApiHelpers.ReadDimensionInConfiguration(dimension.GetDimension2(0), "")); }
                catch (Exception ex)
                {
                    whyNot = "was born, but I cannot read its value back (" + ex.Message + ").";
                    return false;
                }
                const double AngleToleranceDeg = 0.01;
                if (Math.Abs(readDeg - betweenDeg) > AngleToleranceDeg &&
                    Math.Abs(readDeg - (180.0 - betweenDeg)) > AngleToleranceDeg)
                {
                    whyNot = string.Format(CultureInfo.InvariantCulture,
                        "was born but reads {0:0.###} degrees instead of {1:0.###} (or {2:0.###}): it measures " +
                        "something else, and I do not keep it.", readDeg, betweenDeg, 180.0 - betweenDeg);
                    return false;
                }
                try { dimension.MarkedForDrawing = true; } catch { }
                return true;
            }
            catch { return false; }
            finally
            {
                try { model.ClearSelection2(true); } catch { }
                try { _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, asked); } catch { }
            }
        }

        /// <summary>
        /// The angle between two LINES given by their directions in degrees,
        /// from 0 to 90: a line has no verse, so 30 and 210 are the same line.
        /// </summary>
        private static double AngleBetweenLinesDeg(double aDeg, double bDeg)
        {
            double d = Math.Abs(aDeg - bDeg) % 180.0;
            return Math.Min(d, 180.0 - d);
        }

        /// <summary>
        /// A relation between two things of the sketch or of the model. They are passed
        /// as `object` because each is selected with ITS OWN `Select4`:
        /// measured on 25/9/2026, a sketch point or segment is NOT an
        /// `IEntity` — «as IEntity» returns null silently, and the first draft of the
        /// midline made two relations out of four be born.
        /// </summary>
        private static bool Relation(IModelDoc2 model, object one, object due, string which)
        {
            try
            {
                model.ClearSelection2(true);
                if (!Select(one, false) || !Select(due, true)) return false;
                model.SketchAddConstraints(which);
                return true;
            }
            catch { return false; }
            finally { try { model.ClearSelection2(true); } catch { } }
        }

        /// <summary>A relation on a single thing (horizontal, vertical).</summary>
        private static bool SingleRelation(IModelDoc2 model, object what, string which)
        {
            try
            {
                model.ClearSelection2(true);
                if (!Select(what, false)) return false;
                model.SketchAddConstraints(which);
                return true;
            }
            catch { return false; }
            finally { try { model.ClearSelection2(true); } catch { } }
        }

        private static bool Select(object what, bool append)
        {
            var point = what as ISketchPoint;
            if (point != null) return point.Select4(append, null);
            var segment = what as ISketchSegment;
            if (segment != null) return segment.Select4(append, null);
            var entity = what as IEntity;
            if (entity != null) return entity.Select4(append, null);
            return false;
        }

        /// <summary>
        /// The dangling, over-defining and broken relations of the open sketch,
        /// in words in <paramref name="unhealthy"/> (null if there are none).
        ///
        /// «NONE» AND «NOT READ» ARE TWO ANSWERS (Z-C21.2, finding N52). The
        /// function returned null both for a sketch without sick relations and
        /// when the RelationManager did not come or GetRelationsCount threw,
        /// and SketchDefinedAndHealthy read null as healthy: the check said yes
        /// without having looked. ISketchRelationManager~GetRelationsCount.html:
        /// «Return Value Number of sketch relations»; a count that does not come,
        /// or comes negative, is not a count. Now the function returns false,
        /// with the reason in <paramref name="notRead"/>, and the caller refuses.
        /// </summary>
        private static bool UnhealthyRelations(ISketchManager sketches, out string unhealthy, out string notRead)
        {
            unhealthy = null;
            notRead = null;
            try
            {
                var handler = sketches.ActiveSketch?.RelationManager as ISketchRelationManager;
                if (handler == null)
                {
                    notRead = "SOLIDWORKS does not give the relation manager of the open sketch";
                    return false;
                }
                var called = new List<string>();
                int n = handler.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling);
                if (n > 0) called.Add(n + " dangling relation" + (n == 1 ? "" : "s"));
                int dangling = n;
                n = handler.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining);
                if (n > 0) called.Add(n + " over-defining relation" + (n == 1 ? "" : "s"));
                int overDefining = n;
                n = handler.GetRelationsCount((int)swSketchRelationFilterType_e.swBroken);
                if (n > 0) called.Add(n + " broken relation" + (n == 1 ? "" : "s"));
                if (dangling < 0 || overDefining < 0 || n < 0)
                {
                    notRead = string.Format(CultureInfo.InvariantCulture,
                        "GetRelationsCount answered {0} dangling, {1} over-defining, {2} broken", dangling, overDefining, n);
                    return false;
                }
                unhealthy = called.Count == 0 ? null : string.Join(", ", called);
                return true;
            }
            catch (Exception ex)
            {
                notRead = "counting them failed: " + ex.Message;
                return false;
            }
        }

        private static int SketchRelations(ISketchManager sketches)
        {
            try
            {
                var handler = sketches.ActiveSketch?.RelationManager as ISketchRelationManager;
                return handler == null ? -1 : handler.GetRelationsCount((int)swSketchRelationFilterType_e.swAll);
            }
            catch { return -1; }
        }

        /// <summary>
        /// The point at the centre of the pocket, built: a construction
        /// diagonal tied to two opposite corners, and a midpoint. Measured
        /// on 25/9/2026: this way the pocket follows the midline; with the centre of
        /// `CreateCenterRectangle` it does not. Null if something was not born.
        /// </summary>
        private static ISketchPoint RectangleCenter(IModelDoc2 model, object[] sides, double[] p1, double[] p2)
        {
            if (sides == null) return null;
            ISketchPoint a1 = null, a2 = null;
            foreach (var o in sides)
            {
                var l = o as ISketchLine;
                if (l == null) continue;
                foreach (var p in new[] { l.GetStartPoint2() as ISketchPoint, l.GetEndPoint2() as ISketchPoint })
                {
                    if (p == null) continue;
                    if (Math.Abs(p.X - p1[0]) < 1e-9 && Math.Abs(p.Y - p1[1]) < 1e-9) a1 = p;
                    if (Math.Abs(p.X - p2[0]) < 1e-9 && Math.Abs(p.Y - p2[1]) < 1e-9) a2 = p;
                }
            }
            if (a1 == null || a2 == null) return null;
            var sketches = model.SketchManager;
            ISketchSegment diagonal;
            ISketchPoint center;
            bool addBefore = sketches.AddToDB;
            try
            {
                sketches.AddToDB = true;
                diagonal = sketches.CreateLine(p1[0], p1[1], p1[2], p2[0], p2[1], p2[2]);
            }
            finally { sketches.AddToDB = addBefore; }
            var dl = diagonal as ISketchLine;
            if (dl == null) return null;
            try { diagonal.ConstructionGeometry = true; } catch { return null; }
            // `as` and a null check (Z-C21.8, finding N49); null is «nothing
            // born», which the caller already refuses in words.
            var diagonalStart = dl.GetStartPoint2() as ISketchPoint;
            var diagonalEnd = dl.GetEndPoint2() as ISketchPoint;
            if (diagonalStart == null || diagonalEnd == null) return null;
            if (!Relation(model, diagonalStart, a1, "sgCOINCIDENT")) return null;
            if (!Relation(model, diagonalEnd, a2, "sgCOINCIDENT")) return null;
            try
            {
                sketches.AddToDB = true;
                center = sketches.CreatePoint((p1[0] + p2[0]) / 2, (p1[1] + p2[1]) / 2, (p1[2] + p2[2]) / 2);
            }
            finally { sketches.AddToDB = addBefore; }
            if (center == null || !Relation(model, center, diagonal, "sgATMIDDLE")) return null;
            return center;
        }
    }
}
