using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// PIPES AND TRANSITIONS: sweep and loft. (entry GP17)
    ///
    /// A cable gland, a handle, a transition between two sections.
    /// It is not the daily work of a prismatic workshop, but without these
    /// two the catalogue cannot make anything that is not straight.
    ///
    /// ── THE PROFILE IS A NUMBER, NOT A SKETCH ─────────────────────────
    ///
    /// A circular profile given by its diameter (`CircularProfile` +
    /// `CircularProfileDiameter`) needs no profile sketch, only the path: a
    /// diameter is a number the language model can say, a profile sketch is
    /// something it has no way to point at.
    ///
    /// THE CALL IS OBSOLETE, and this is not the form to copy:
    /// IFeatureManager~InsertProtrusionSwept4.html, Remarks: «SOLIDWORKS 2018
    /// introduces a new sweep architecture, making this method obsolete. See
    /// Sweep Features and SweepFeatureData Objects to create this swept-boss or
    /// base feature.» It stays here only until the migration of finding L2-3
    /// (lot Z-C21.7): CreateDefinition(swFmSweep) -> ISweepFeatureData with
    /// the same CircularProfile and CircularProfileDiameter, the path selected
    /// with mark 4 BEFORE CreateDefinition, and the bench case
    /// straight_pipe_area_per_length unchanged to the seventh digit.
    ///
    /// For the same reason **the path is a list of points** and not a
    /// sketch to be named: in the state of the document sketches have no
    /// identifier, and no operation of the catalogue leaves a reusable one
    /// lying around. If the path had to be «a sketch», this
    /// operation could not be called by anybody.
    ///
    /// ── THE MARKS, MEASURED ───────────────────────────────────────────
    ///
    ///   sweep, path with mark 4  ->  «Sweep1», right volume
    ///   sweep, path with mark 0  ->  «Sweep1», right volume
    ///   sweep, path with mark 1  ->  NULL
    ///   sweep, path with mark 2  ->  NULL
    ///
    ///   loft, profiles with mark 1    ->  «Loft1», right volume
    ///   loft, profiles with mark 0    ->  «Loft1», right volume
    ///   loft, profiles with mark 4    ->  NULL
    ///
    /// **4 is used for the path** of the sweep and **1 for the profiles** of the loft:
    /// they are the marks that the SOLIDWORKS documentation associates with the two roles,
    /// and they are among those that work. Zero works in both cases
    /// because it means «no mark», but leaning on it would mean that
    /// two selections with different roles become indistinguishable the day
    /// a third is added.
    ///
    /// ── AND A LOFT BETWEEN TWO CIRCLES IS NOT A CONE ─────────────────
    ///
    /// Measured: circle Ø40 at height 0, circle Ø20 at height 50.
    ///
    ///   formula of the truncated cone   36,651.91 mm3
    ///   loft measured                   36,650.61 mm3
    ///   difference                           1.30 mm3   (0.0035%)
    ///
    /// The surface of the loft is **a spline approximation** of the
    /// conic, not a conic. It is small, and it is systematic: whoever compares the
    /// volume of a loft with the formula must know it, or will chase a defect
    /// that is not there. `revolve` on the same trapezoid gives the exact cone, and
    /// the bench keeps the two numbers side by side on purpose.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome CircularSweep(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double diameter = Num(a, "diameter_mm", 0);
            if (diameter <= 0)
                return Failed("«diameter_mm» is missing, or it is zero: a tube of zero diameter is not a tube.");

            var points = Points(a, "path_mm");
            if (points == null || points.Count < 2)
                return Failed(
                    "«path_mm» must be a list of at least two points, each three " +
                    "numbers in millimeters: for example [[0,0,0], [100,0,0]]. It is the " +
                    "line the tube runs along.");

            // ── THE PATH MUST NOT DOUBLE BACK ON ITSELF ─────────────────
            //
            // Two equal points in a row make a segment of length zero, and
            // the sweep is not born without saying why. It is checked first.
            double length = 0;
            for (int i = 1; i < points.Count; i++)
            {
                double d = DistanceBetweenPoints(points[i - 1], points[i]);
                if (d < 1e-6)
                    return Failed(
                        "Points " + i + " and " + (i + 1) + " of the path are the same " +
                        "point: a segment of length zero makes no way.");
                length += d;
            }

            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            if (volumeBefore == null) return Failed(VolumeNotReadBefore("making the tube"));

            // THE PATH IS THE ONE 3D SKETCH BORN SINCE HERE (scheme S7, Z-C21.5):
            // the names of the tree are taken before the sketch is opened, and
            // the path is the 3DProfileFeature whose name was not among them
            // (SwOperations.FeatureBorn). Until library 0.146.0 it was the last
            // sketch of the tree, 2D or 3D, whatever step had made it.
            // IFeature~GetTypeName2.html lists 3DProfileFeature under Sketch,
            // with ISketch as its specific feature; the frame path, drawn with
            // the same Insert3DSketch, was read back as a 3DProfileFeature (bench
            // case `r12_frame_with_a_thinner_profile`).
            var pathNamesBefore = FeatureNamesNow(model);
            var sketches = model.SketchManager;
            bool drawn = false;
            string error = null;
            try
            {
                model.ClearSelection2(true);
                // A 3D SKETCH, and not one on a plane: a path that turns
                // in space lies on no plane, and demanding that it
                // does would mean knowing how to make only straight pipes.
                sketches.Insert3DSketch(true);
                for (int i = 1; i < points.Count; i++)
                {
                    var p = points[i - 1];
                    var q = points[i];
                    var line = sketches.CreateLine(
                        Units.M(p[0]), Units.M(p[1]), Units.M(p[2]),
                        Units.M(q[0]), Units.M(q[1]), Units.M(q[2]));
                    if (line == null)
                        throw new InvalidOperationException(
                            "segment " + i + " of the path was not drawn");
                }
                drawn = true;
            }
            catch (Exception ex) { error = ex.Message; }
            finally { try { sketches.Insert3DSketch(false); } catch { } }

            if (!drawn)
                return Failed("The path was not drawn: " + error + WithoutSketch(model));

            int pathsBorn;
            IFeature filePath = FeatureBorn(model, pathNamesBefore, "3DProfileFeature", out pathsBorn);
            if (filePath == null)
                return Failed(SketchNotIdentified(pathsBorn, "I cannot find the sketch of the path I just made.") +
                               WithoutSketch(model));

            model.ClearSelection2(true);
            // MARK 4: the path. See the table in the note at the top.
            if (!filePath.Select2(true, SelectionMarks.SweepPath))
                return Failed("I could not select the path." + WithoutSketch(model));

            IFeature feature;
            try
            {
                feature = model.FeatureManager.InsertProtrusionSwept4(
                    Propagate: false, Alignment: false, TwistCtrlOption: 0,
                    KeepTangency: false, BAdvancedSmoothing: false,
                    StartMatchingType: 0, EndMatchingType: 0,
                    IsThinBody: false, Thickness1: 0, Thickness2: 0, ThinType: 0,
                    PathAlign: 0, Merge: true, UseFeatScope: true, UseAutoSelect: true,
                    TwistAngle: 0, BMergeSmoothFaces: false,
                    CircularProfile: true,
                    CircularProfileDiameter: Units.M(diameter),
                    Direction: 0) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("The sweep did not succeed: " + ex.Message + WithoutSketch(model));
            }

            if (feature == null)
                return Failed(
                    "The sweep was not created, and SOLIDWORKS does not say why. Usually the " +
                    "path turns too tightly for the requested diameter: in a curve with a " +
                    "radius smaller than the radius of the tube the surface would " +
                    "cross itself." + WithoutSketch(model));

            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);
            if (volumeAfter == null)
                return Succeeded(feature, new Dictionary<string, object>
                {
                    ["diameter_mm"] = diameter,
                    ["points_path"] = points.Count,
                    ["path_length_mm"] = Math.Round(length, 3),
                    ["material_added_mm3"] = null,
                    ["bodies"] = NumberOrNull(bodiesAfter),
                    ["volume_not_reread"] = NoteVolumeNotReread,
                });
            double added = volumeAfter.Value - volumeBefore.Value;

            if (added <= 0)
                return Failed(
                    "The sweep did not add material: the volume stayed what it was before. " +
                    "Withdraw it — " + Sentences.HowToWithdrawIt + ".");

            // ── THE CALCULATION THAT CAN BE DONE BY HAND ────────────────
            //
            // On a STRAIGHT path the volume is area times length, exact. On
            // a broken one it is not: in the elbows the material overlaps, and the
            // true volume is smaller. It is declared which of the two cases it is, instead
            // of giving a number that matches only some of the time.
            bool straight = points.Count == 2;
            double theoretical = Math.PI * diameter * diameter / 4.0 * length;
            var measurements = new Dictionary<string, object>
            {
                ["diameter_mm"] = diameter,
                ["points_path"] = points.Count,
                ["path_length_mm"] = Math.Round(length, 3),
                ["material_added_mm3"] = Math.Round(added, 3),
                ["volume_theoretical_mm3"] = Math.Round(theoretical, 3),
                ["path_straight"] = straight,
                ["bodies"] = NumberOrNull(bodiesAfter),
            };
            if (straight)
            {
                double deviation = Math.Abs(added - theoretical) / theoretical;
                measurements["deviation_from_theoretical"] = Math.Round(deviation, 8);
                if (deviation > 1e-4)
                    return Failed(string.Format(
                        CultureInfo.InvariantCulture,
                        "The tube was created but it is not right: on a straight path the volume " +
                        "must be area times length, that is {0:0.###} mm3, and instead it is " +
                        "{1:0.###}. Withdraw it — " + Sentences.HowToWithdrawIt + ".",
                        theoretical, added));
            }
            else
            {
                measurements["note_on_volume"] =
                    "The path has elbows: at the elbows the material overlaps, so " +
                    "the true volume is SMALLER than area times length. The theoretical " +
                    "one above is the upper limit, not the answer.";
            }
            if (bodiesAfter >= 0 && bodiesBefore > 0 && bodiesAfter != bodiesBefore)
                measurements["warning_bodies"] =
                    "The bodies went from " + bodiesBefore + " to " + bodiesAfter +
                    ": the tube did not merge with what was there.";

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agent] sweep Ø{0:0.##} on {1:0.##} mm of path: {2:0.##} mm3",
                diameter, length, added));

            return Succeeded(feature, measurements);
        }

        // =================================================================

        private Outcome LoftTwoCircles(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string plane = Str(a, "plane", "front");
            double d1 = Num(a, "diameter1_mm", 0);
            double d2 = Num(a, "diameter2_mm", 0);
            double distance = Num(a, "distance_mm", 0);
            if (d1 <= 0 || d2 <= 0)
                return Failed(
                    "«diameter1_mm» and «diameter2_mm» are needed, both greater than zero. " +
                    "There is no operation for a transition from a round to a point.");
            // N12 (Z-C21.9): the catalogue promised «negative for the other side»
            // while this refused it as "missing". The catalogue now says what the
            // code does: greater than zero, on the side the plane faces.
            if (distance <= 0)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "«distance_mm» must be greater than zero (it is {0:0.###}): how far the " +
                    "second circle is from the first, on the side the chosen plane faces. At " +
                    "zero both circles lie on the same plane and there is nothing to join; the " +
                    "other side is not offered.", distance));

            double offsets1 = Num(a, "offset1_mm", 0);
            double offsets2 = Num(a, "offset2_mm", 0);

            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            int planesBefore = CountPlanes(model);
            if (volumeBefore == null) return Failed(VolumeNotReadBefore("making the loft"));

            var sketches = model.SketchManager;
            IFeature s1, s2;
            try
            {
                // first circle, on the chosen plane
                if (!SelectPlane(model, plane))
                    return Failed("I did not find the plane «" + plane + "» in the tree.");
                // EACH OF THE THREE FEATURES IS THE ONE BORN SINCE ITS OWN CALL
                // (Z-C21.5, finding N13, scheme S7): the first sketch, the plane
                // (what InsertRefPlane returned, else the RefPlane born) and the
                // second sketch. Until then the plane was the last feature of the
                // tree (the flat pattern on a sheet-metal part, finding M9) and
                // each sketch the last sketch of the tree, with nothing to tell a
                // second sketch that was never born from the first one.
                var beforeFirstSketch = FeatureNamesNow(model);
                sketches.InsertSketch(true);
                sketches.CreateCircleByRadius(Units.M(offsets1), 0, 0, Units.M(d1 / 2));
                sketches.InsertSketch(false);
                s1 = FeatureBorn(model, beforeFirstSketch, "ProfileFeature", out _);
                model.ClearSelection2(true);

                // A plane at the distance asked, and the second circle on it.
                // Z-C21.7 (L2-4): InsertRefPlane with the base plane selected
                // by name with mark 0, exactly the loft L2 of the InsertRefPlane
                // probe of Z-C21.3 (43980.736 mm3, the same volume as on the
                // CreatePlaneAtOffset3 plane it replaces, L1). Guide pages and
                // numbers in ApiHelpers.InsertOffsetPlane.
                var basePlane = FindBasePlane(model, plane);
                if (basePlane == null || !ApiHelpers.SelectOffsetPlaneBase(model, basePlane))
                    return Failed("I did not find the plane «" + plane + "» in the tree.");
                var beforePlane = FeatureNamesNow(model);
                var planeReturned = ApiHelpers.InsertOffsetPlane(model, distance) as IFeature;
                int planesNow = CountPlanes(model);
                if (planesBefore < 0 || planesNow < 0)
                    return Failed(
                        "I cannot count the planes of the tree, and without that I cannot tell whether the " +
                        "plane of the second circle was created." + WithoutSketch(model));
                if (planesNow <= planesBefore)
                    return Failed(
                        "The plane of the second circle was not created, and none " +
                        "appeared in the tree.");
                IFeature newPlane = planeReturned ?? FeatureBorn(model, beforePlane, "RefPlane", out _);
                var beforeSecondSketch = FeatureNamesNow(model);
                model.ClearSelection2(true);
                if (newPlane == null || !newPlane.Select2(false, 0))
                    return Failed("I could not select the plane just created.");
                sketches.InsertSketch(true);
                sketches.CreateCircleByRadius(Units.M(offsets2), 0, 0, Units.M(d2 / 2));
                sketches.InsertSketch(false);
                s2 = FeatureBorn(model, beforeSecondSketch, "ProfileFeature", out _);
                model.ClearSelection2(true);
            }
            catch (Exception ex)
            {
                return Failed("The two circles were not drawn: " + ex.Message);
            }
            if (s1 == null || s2 == null)
                return Failed("I cannot find the two sketches I just made.");

            model.ClearSelection2(true);
            // MARK 1 FOR BOTH PROFILES, and the ORDER matters: the loft goes
            // from the first selected to the second.
            if (!s1.Select2(true, SelectionMarks.LoftProfile) || !s2.Select2(true, SelectionMarks.LoftProfile))
                return Failed("I could not select the two profiles.");

            IFeature feature;
            try
            {
                feature = model.FeatureManager.InsertProtrusionBlend2(
                    Closed: false, KeepTangency: true, ForceNonRational: false,
                    TessToleranceFactor: 0.1,
                    StartMatchingType: 0, EndMatchingType: 0,
                    StartTangentLength: 0, EndTangentLength: 0,
                    StartTangentDir: false, EndTangentDir: false,
                    IsThinBody: false, Thickness1: 0, Thickness2: 0, ThinType: 0,
                    Merge: true, UseFeatScope: true, UseAutoSelect: true,
                    GuideCurveInfluence: 0) as IFeature;
            }
            catch (Exception ex) { return Failed("The loft did not succeed: " + ex.Message); }

            if (feature == null)
                return Failed(
                    "The loft was not created, and SOLIDWORKS does not say why. Usually one " +
                    "of the two profiles does not close, or two profiles cross " +
                    "each other.");

            // The bodies are read ONCE (before, three calls for the same
            // number), and the volume after may be unreadable (entry Z-C8).
            int bodiesAfter = CountSolidBodies(model);
            double? volumeAfter = VolumeMm3(model);
            if (volumeAfter == null)
                return Succeeded(feature, new Dictionary<string, object>
                {
                    ["diameter1_mm"] = d1,
                    ["diameter2_mm"] = d2,
                    ["distance_mm"] = distance,
                    ["material_added_mm3"] = null,
                    ["bodies"] = NumberOrNull(bodiesAfter),
                    ["planes_added"] = AddedPlanes(model, planesBefore),
                    ["volume_not_reread"] = NoteVolumeNotReread,
                });
            double added = volumeAfter.Value - volumeBefore.Value;
            if (added <= 0)
                return Failed(
                    "The loft did not add material. Withdraw it — " + Sentences.HowToWithdrawIt + ".");

            // The truncated cone: pi*h/3*(R^2 + R*r + r^2). It holds only if the two
            // circles are coaxial; offsetting them gives an oblique truncated cone, which
            // by Cavalieri has the same volume — but the surface of the loft is
            // another matter, and the number deviates more.
            double R = d1 / 2, r = d2 / 2;
            double theoretical = Math.PI * Math.Abs(distance) / 3.0 * (R * R + R * r + r * r);
            double deviation = Math.Abs(added - theoretical) / theoretical;

            var measurements = new Dictionary<string, object>
            {
                ["diameter1_mm"] = d1,
                ["diameter2_mm"] = d2,
                ["distance_mm"] = distance,
                ["material_added_mm3"] = Math.Round(added, 3),
                ["volume_cone_frustum_mm3"] = Math.Round(theoretical, 3),
                ["deviation_from_cone_frustum"] = Math.Round(deviation, 8),
                ["bodies"] = NumberOrNull(bodiesAfter),
                ["planes_added"] = AddedPlanes(model, planesBefore),
                // IT IS NOT A CONE, AND IT IS MEASURED. See the note at the top.
                ["note_on_volume"] =
                    "The surface of a loft between two circles is a SPLINE APPROXIMATION " +
                    "of the cone, not a cone: the volume deviates from the frustum " +
                    "formula by a few thousandths of a percent, always on the low side. " +
                    "If you need the exact cone, use «revolve» on a trapezoid.",
            };
            if (deviation > 0.005)
                return Failed(string.Format(
                    CultureInfo.InvariantCulture,
                    "The loft was created but the volume does not add up: from the frustum it " +
                    "would be {0:0.###} mm3 and {1:0.###} were added, that is {2:0.###}% of deviation. " +
                    "A loft deviates from the formula by a few thousandths of a percent, not by " +
                    "that much: there is something else here. Withdraw it — " +
                    Sentences.HowToWithdrawIt + ".",
                    theoretical, added, deviation * 100));

            if (bodiesBefore > 0 && bodiesAfter >= 0 && bodiesAfter != bodiesBefore)
                measurements["warning_bodies"] =
                    "The bodies went from " + bodiesBefore + " to " + bodiesAfter +
                    ": the loft did not merge with what was there.";

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agent] loft Ø{0:0.##} -> Ø{1:0.##} on {2:0.##} mm: {3:0.##} " +
                "mm3 (frustum {4:0.##})", d1, d2, distance, added, theoretical));

            return Succeeded(feature, measurements);
        }

        // =================================================================

        private static List<double[]> Points(Dictionary<string, object> a, string key)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null) return null;
            var e = v as System.Collections.IEnumerable;
            if (e == null || v is string) return null;
            var outside = new List<double[]>();
            foreach (var x in e)
            {
                var t = x as System.Collections.IEnumerable;
                if (t == null || x is string) return null;
                var n = new List<double>();
                foreach (var c in t)
                {
                    try { n.Add(Convert.ToDouble(c, CultureInfo.InvariantCulture)); }
                    catch { return null; }
                }
                if (n.Count != 3) return null;
                outside.Add(n.ToArray());
            }
            return outside;
        }

        private static double DistanceBetweenPoints(double[] p, double[] q)
        {
            double dx = q[0] - p[0], dy = q[1] - p[1], dz = q[2] - p[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
