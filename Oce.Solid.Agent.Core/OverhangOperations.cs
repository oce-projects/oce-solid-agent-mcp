using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE OVERHANGS: what cannot be printed without supports. (entry GP16)
    ///
    /// A wall that hangs in the void beyond a certain angle is printed with
    /// lost filaments. SOLIDWORKS says nothing — the part is sound, the
    /// volume is right, the geometry is closed — and the defect is seen only
    /// on the machine, once the print is done.
    ///
    /// ── THE NORMAL: MEASURED, NOT CORRECTED AT RANDOM ─────────────────
    ///
    /// The entry warned that the normal of `ISurface` is that of the
    /// underlying surface and must be flipped when `FaceInSurfaceSense()` is
    /// false, and that getting it wrong gives *«an analysis that declares supported
    /// precisely the face that hangs in the void»*.
    ///
    /// Measured on 16/9/2026 on a block of which everything is known (100x100x20, six
    /// faces with known outward normals):
    ///
    ///   IFace2.Normal points out of the material   6 times out of 6
    ///   Normal agreeing with PlaneParams           1 time out of 6
    ///   FaceInSurfaceSense() = true                on the 5 where they are OPPOSITE
    ///   FaceInSurfaceSense() = false               on the 1 where they agree
    ///
    /// Two things, and both count:
    ///
    ///   1. **`IFace2.Normal` is already right.** It always points out of the
    ///      material, and must not be corrected with anything. It is the one this
    ///      project uses everywhere, and now it is measured instead of hoped.
    ///   2. **The rule «if the sense is false, flip» would have been wrong
    ///      the other way round**, and not on half of the faces but on five sixths.
    ///      Here the sense is not used: `Normal` is used.
    ///
    /// ── THE CURVED FACES ──────────────────────────────────────────────
    ///
    /// A curved face does not have ONE normal: the underside of a horizontal
    /// cylinder hangs in the void in its lower part and not in the upper
    /// part. Therefore the faces are tessellated and looked at triangle by
    /// triangle, weighting by area.
    ///
    /// The direction of the triangles is decided by the winding, which is a convention
    /// and not a fact: **it is checked against `IFace2.Normal` on all the
    /// PLANAR faces**, where the right answer is known. If it does not match, it
    /// refuses instead of answering with an upside-down analysis. It is a check
    /// that carries its own calibration with it.
    ///
    /// `GetTessTriangles` answers **`float[]`, not `double[]`**: the wrong
    /// cast returns null silently (RAG note 80zf).
    ///
    /// ── AND WHAT SITS ON THE PLATE IS NOT AN OVERHANG ─────────────────
    ///
    /// The underside face looks straight down — 90 degrees of overhang, the
    /// worst case possible — but rests on the plate of the machine and prints perfectly.
    /// Counting it would be declaring unprintable every part
    /// that exists.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>How much is tolerated on the support plane, in mm.</summary>
        private const double OnPlaneMm = 0.05;

        private Outcome CheckOverhangs(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double threshold = Num(a, "threshold_deg", 45);
            if (threshold <= 0 || threshold >= 90)
                return Failed(
                    "«threshold_deg» must be between 0 and 90, both excluded. It is the angle beyond which a " +
                    "wall does not hold itself up: 45 for a normal filament printer, 30-35 " +
                    "if you want a safe margin, 60 if the machine cools well.");

            double[] su = Direction(a, "print_direction");
            if (su == null)
                return Failed(
                    "«print_direction» must be three numbers, for example [0, 0, 1]. It " +
                    "is the direction in which the print grows: the plane of the machine is below, " +
                    "perpendicular to this.");

            var bodies = BodiesToCheck(model, a, docState, out string reason);
            if (bodies == null) return Failed(reason);
            int hiddenBodies = 0;
            foreach (var c in bodies)
            {
                try { if (!c.Visible) hiddenBodies++; } catch { }
            }

            // The height of the support plane: the lowest point of the part
            // in the printing direction.
            double bedLevel = double.MaxValue;
            foreach (var c in bodies)
            {
                var end = new double[3];
                if (c.GetExtremePoint(-su[0], -su[1], -su[2],
                                      out end[0], out end[1], out end[2]))
                {
                    double q = end[0] * su[0] + end[1] * su[1] + end[2] * su[2];
                    if (q < bedLevel) bedLevel = q;
                }
            }
            if (bedLevel == double.MaxValue)
                return Failed("I cannot find the lowest point of the part.");

            double totalArea = 0, overhangingArea = 0, areaOnPlane = 0;
            double worse = 0;
            int checkedFaces = 0, overhangingFaces = 0, curvedFaces = 0;
            int unanalyzedFaces = 0;
            double unanalyzedArea = 0;
            // Planar faces that hang over the threshold but whose true box did
            // not answer: whether they rest on the plate is unknown, so they
            // count as not analysed (part of unanalyzedFaces), never as
            // overhangs and never as resting.
            int planarWithoutBox = 0;
            double planarWithoutBoxArea = 0;
            int consistent = 0, conflicting = 0;
            var list = new List<Dictionary<string, object>>();

            foreach (var body in bodies)
            {
                var f = body.GetFirstFace() as IFace2;
                while (f != null)
                {
                    var face = f;
                    f = f.GetNextFace() as IFace2;
                    checkedFaces++;

                    double[] normal = null;
                    try { normal = ApiHelpers.NormalIfPlanar(face); } catch { }
                    bool planar = false;
                    try { var s = face.GetSurface() as ISurface; planar = s != null && s.IsPlane(); }
                    catch { }
                    if (!planar) curvedFaces++;

                    double faceArea = 0;
                    try { faceArea = face.GetArea() * 1e6; } catch { }
                    totalArea += faceArea;

                    // ── the triangles, and the check of the winding ────
                    var tri = Triangles(face);
                    if (tri == null || tri.Count == 0)
                    {
                        // No tessellation: it falls back on the normal, which
                        // for a planar face is exact. FOR A CURVED ONE THERE IS NONE
                        // (entry Z-C21.2, finding L12-2).
                        //
                        // IFace2~Normal.html, Remarks: «If the face is not a
                        // planar face, then this property returns 0,0,0.» The
                        // comment that stood here promised «and it is stated», and the
                        // code looked only at `normale == null`: with (0,0,0)
                        // Product gave 0, the face turned out «facing up» and
                        // was never flagged, whatever its
                        // slope. If no face tessellated, the answer
                        // was «prints without supports» without having looked at
                        // anything. Now the curved face without triangles is
                        // COUNTED as not analysed, and the verdict becomes
                        // inconclusive (below).
                        if (!planar)
                        {
                            unanalyzedFaces++;
                            unanalyzedArea += faceArea;
                            continue;
                        }
                        if (normal == null) continue;
                        double degP = Overhang(normal, su);
                        bool downP = Product(normal, su) < 0;
                        bool? resting = downP && degP > threshold ? OnPlane(face, su, bedLevel) : true;
                        if (resting == null)
                        {
                            // S6: the true box replaced IFace2.GetBox, which always
                            // answered. A box that does not answer is not "not on
                            // the plate": it is a face this check did not look at.
                            unanalyzedFaces++;
                            unanalyzedArea += faceArea;
                            planarWithoutBox++;
                            planarWithoutBoxArea += faceArea;
                            continue;
                        }
                        if (resting == false)
                        {
                            overhangingFaces++;
                            overhangingArea += faceArea;
                            if (degP > worse) worse = degP;
                            Add(list, degP, faceArea, planar, face, su);
                        }
                        continue;
                    }

                    double downArea = 0;
                    double worseHere = 0;
                    foreach (var t in tri)
                    {
                        // The direction of the winding against that of the
                        // face: it is checked where the answer is known.
                        if (planar && normal != null)
                        {
                            if (Product(t.Normal, normal) > 0) consistent++; else conflicting++;
                        }
                        if (Product(t.Normal, su) >= 0) continue;   // looks up
                        double deg = Overhang(t.Normal, su);
                        if (deg <= threshold) continue;
                        if (PointOnPlane(t.Center, su, bedLevel)) { areaOnPlane += t.Area; continue; }
                        downArea += t.Area;
                        if (deg > worseHere) worseHere = deg;
                    }

                    if (downArea > 0)
                    {
                        overhangingFaces++;
                        overhangingArea += downArea;
                        if (worseHere > worse) worse = worseHere;
                        Add(list, worseHere, downArea, planar, face, su);
                    }
                }
            }

            // ── THE CHECK THAT CARRIES ITS OWN CALIBRATION ──────────────
            //
            // If the winding of the triangles does not agree with the normal
            // of the PLANAR faces -- where the answer is known -- then
            // the analysis of the CURVED faces is upside down, and there is no way
            // to notice it by looking at the result.
            if (consistent + conflicting > 0 && conflicting > 0)
                return Failed(string.Format(
                    CultureInfo.InvariantCulture,
                    "I do not trust my own analysis: out of {0} triangles of planar faces, {1} " +
                    "have the direction opposite to that of the face. It means that the " +
                    "winding of the tessellation here does not follow the rule I verified, " +
                    "and then on curved faces I would say the opposite of the truth without " +
                    "it showing. I do not give you a reversed analysis.",
                    consistent + conflicting, conflicting));

            list = list.OrderByDescending(v => Convert.ToDouble(v["overhang_deg"])).ToList();
            const int howManyISay = 12;
            bool truncated = list.Count > howManyISay;

            var measurements = new Dictionary<string, object>
            {
                ["threshold_deg"] = threshold,
                ["print_direction"] = su.Select(x => Math.Round(x, 4)).ToArray(),
                ["faces_checked"] = checkedFaces,
                ["faces_curved"] = curvedFaces,
                ["faces_overhanging"] = overhangingFaces,
                ["overhang_worst_deg"] = Math.Round(worse, 2),
                ["area_overhanging_mm2"] = Math.Round(overhangingArea, 2),
                ["area_total_mm2"] = Math.Round(totalArea, 2),
                ["area_resting_on_plane_mm2"] = Math.Round(areaOnPlane, 2),
                // INCONCLUSIVE IS A THIRD VALUE, not a «yes» (finding
                // L12-2): with a curved face that could not be looked at,
                // «no overhang found» does not mean «no overhang».
                // An overhang found stays a «no» even so.
                ["prints_without_supports"] = overhangingFaces > 0
                    ? (object)false
                    : unanalyzedFaces > 0 ? null : (object)true,
                ["overhangs"] = list.Take(howManyISay).ToArray(),
                ["triangles_checked"] = consistent + conflicting,
                // New names, in English like the next ones of the bridge.
                ["faces_not_analyzed"] = unanalyzedFaces,
                ["hidden_bodies"] = hiddenBodies,
            };
            if (unanalyzedFaces > 0)
                measurements["area_not_analyzed_mm2"] = Math.Round(unanalyzedArea, 2);
            if (planarWithoutBox > 0)
            {
                measurements["planar_faces_without_true_box"] = planarWithoutBox;
                measurements["planar_area_without_true_box_mm2"] = Math.Round(planarWithoutBoxArea, 2);
            }
            if (truncated)
            {
                measurements["overhangs_not_listed"] = list.Count - howManyISay;
                measurements["truncated"] = true;
            }

            int curvedNotAnalysed = unanalyzedFaces - planarWithoutBox;
            string notChecked = curvedNotAnalysed == 0 ? "" : string.Format(
                CultureInfo.InvariantCulture,
                " WARNING: {0} curved faces ({1:0.##} mm2) could not be tessellated and I " +
                "could not look at them: a curved face does not have a single normal.",
                curvedNotAnalysed, unanalyzedArea - planarWithoutBoxArea);
            if (planarWithoutBox > 0)
                notChecked += string.Format(
                    CultureInfo.InvariantCulture,
                    " WARNING: {0} planar faces ({1:0.##} mm2) hang over the threshold, but their true " +
                    "bounding box could not be measured, so I cannot tell whether they rest on the plate.",
                    planarWithoutBox, planarWithoutBoxArea);
            measurements["verdict"] = overhangingFaces > 0
                ? string.Format(CultureInfo.InvariantCulture,
                    "It does NOT print without supports: {0} faces overhang beyond {1:0.#} " +
                    "degrees, the worst at {2:0.#}, for {3:0.##} mm2 out of {4:0.##} of " +
                    "surface. Turn the part, or add supports, or chamfer those faces.",
                    overhangingFaces, threshold, worse, overhangingArea, totalArea) + notChecked
                : unanalyzedFaces > 0
                    ? string.Format(CultureInfo.InvariantCulture,
                        "I CANNOT SAY whether it can print without supports with the threshold of {0:0.#} " +
                        "degrees: on the faces I looked at nothing overhangs beyond it.", threshold) +
                      notChecked +
                      " Try again after a rebuild, or look at those faces by eye."
                    : string.Format(CultureInfo.InvariantCulture,
                        "It can print without supports with the threshold of {0:0.#} degrees: there " +
                        "is no surface that overhangs beyond it.", threshold);

            measurements["what_it_does_not_check"] =
                "I look at the angle of each surface with respect to the print direction, " +
                "and that is all. I do NOT look at: bridges between two supports " +
                "(a short crossbar prints even if it is horizontal), wall thickness, detached islands " +
                "that form in mid-air, and whether the part fits in the volume of the machine. " +
                "And what rests on the plane I do not count as an overhang, because the plane " +
                "holds it.";

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agent] overhangs beyond {0:0.#} degrees: {1} faces, worst {2:0.#}, " +
                "{3:0.##} mm2", threshold, overhangingFaces, worse, overhangingArea));

            return SucceededWithoutCreating(measurements);
        }

        // =================================================================

        private sealed class Triangle
        {
            public double[] Normal;
            public double[] Center;
            public double Area;
        }

        /// <summary>
        /// The triangles of a face, with normal, centre and area.
        ///
        /// `GetTessTriangles` answers **float[]**: nine numbers per triangle,
        /// three vertices. The cast to `double[]` returns null silently — it is the
        /// trap already documented in the RAG, and it is written here so that whoever
        /// touches this code does not pay for it again.
        /// </summary>
        private static List<Triangle> Triangles(IFace2 face)
        {
            float[] raw;
            try { raw = face.GetTessTriangles(true) as float[]; }
            catch { return null; }
            if (raw == null || raw.Length < 9) return null;

            var outside = new List<Triangle>(raw.Length / 9);
            for (int i = 0; i + 8 < raw.Length; i += 9)
            {
                var ax = raw[i]; var ay = raw[i + 1]; var az = raw[i + 2];
                var bx = raw[i + 3]; var by = raw[i + 4]; var bz = raw[i + 5];
                var cx = raw[i + 6]; var cy = raw[i + 7]; var cz = raw[i + 8];

                double ux = bx - ax, uy = by - ay, uz = bz - az;
                double vx = cx - ax, vy = cy - ay, vz = cz - az;
                double nx = uy * vz - uz * vy;
                double ny = uz * vx - ux * vz;
                double nz = ux * vy - uy * vx;
                double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-15) continue;   // degenerate triangle
                outside.Add(new Triangle
                {
                    Normal = new[] { nx / len, ny / len, nz / len },
                    Center = new[] { (ax + bx + cx) / 3.0, (ay + by + cy) / 3.0, (az + bz + cz) / 3.0 },
                    // half the magnitude of the cross product, in mm2
                    Area = len / 2.0 * 1e6,
                });
            }
            return outside;
        }

        /// <summary>
        /// By how much a surface hangs, in degrees, counted FROM THE VERTICAL:
        /// a straight wall is 0, a horizontal ceiling is 90.
        ///
        /// It is the slicers' convention -- «threshold 45 degrees» means that
        /// -- and not that of the angle with the plane, which is the complement. Two
        /// numbers that look alike and say the opposite: here it is written which
        /// of the two it is, and the field is called `overhang_deg`.
        /// </summary>
        private static double Overhang(double[] normal, double[] su)
        {
            double c = Math.Abs(Product(normal, su));
            if (c > 1) c = 1;
            return 90.0 - Math.Acos(c) * 180.0 / Math.PI;
        }

        private static double Product(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        /// <summary>
        /// Whether the face rests on the plate; null when its true box cannot
        /// be measured (the caller counts it as not analysed).
        /// </summary>
        private static bool? OnPlane(IFace2 face, double[] su, double bedLevel)
        {
            try
            {
                var b = ApiHelpers.TrueBoundingBox(face);   // S6: not IFace2.GetBox, approximate
                if (b == null || b.Length < 6) return null;
                // the highest corner of the box, projected onto the direction
                double up = double.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    double x = (i & 1) == 0 ? b[0] : b[3];
                    double y = (i & 2) == 0 ? b[1] : b[4];
                    double z = (i & 4) == 0 ? b[2] : b[5];
                    double q = x * su[0] + y * su[1] + z * su[2];
                    if (q > up) up = q;
                }
                return up - bedLevel < Units.M(OnPlaneMm);
            }
            catch { return null; }
        }

        private static bool PointOnPlane(double[] point, double[] su, double bedLevel)
        {
            double q = point[0] * su[0] + point[1] * su[1] + point[2] * su[2];
            return q - bedLevel < Units.M(OnPlaneMm);
        }

        private static void Add(
            List<Dictionary<string, object>> list, double deg, double area,
            bool planar, IFace2 face, double[] su)
        {
            var entry = new Dictionary<string, object>
            {
                ["overhang_deg"] = Math.Round(deg, 2),
                ["area_mm2"] = Math.Round(area, 2),
                ["planar"] = planar,
            };
            try
            {
                var b = ApiHelpers.TrueBoundingBox(face);   // S6: not IFace2.GetBox, approximate
                if (b != null && b.Length >= 6)
                    entry["is_between_mm"] = new[]
                    {
                        Math.Round(Units.Mm(b[0]), 2), Math.Round(Units.Mm(b[1]), 2),
                        Math.Round(Units.Mm(b[2]), 2), Math.Round(Units.Mm(b[3]), 2),
                        Math.Round(Units.Mm(b[4]), 2), Math.Round(Units.Mm(b[5]), 2),
                    };
            }
            catch { }
            list.Add(entry);
        }

        private static double[] Direction(Dictionary<string, object> a, string key)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null)
                return new double[] { 0, 0, 1 };
            var e = v as System.Collections.IEnumerable;
            if (e == null || v is string) return null;
            var n = new List<double>();
            foreach (var x in e)
            {
                try { n.Add(Convert.ToDouble(x, CultureInfo.InvariantCulture)); }
                catch { return null; }
            }
            if (n.Count != 3) return null;
            double len = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
            if (len < 1e-9) return null;
            return new[] { n[0] / len, n[1] / len, n[2] / len };
        }

        private List<IBody2> BodiesToCheck(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState,
            out string reason)
        {
            reason = null;
            var partDoc = model as IPartDoc;
            if (partDoc == null) { reason = "This is not a part document."; return null; }
            // ALL THE BODIES, EVEN THE HIDDEN ONES (entry Z-C21.2, finding L12-1).
            //
            // IPartDoc~GetBodies2.html, parameter BVisibleOnly: «True gets only
            // the visible bodies, false gets all of the bodies in the part». Here
            // there was `true`: a body hidden to look inside the part came out of
            // the analysis without anything saying so, and «prints_without_supports»
            // could say yes on a body that hangs. The state has used `false` for a long time
            // (DocumentState.cs, «and the false is intended»), and with `true` here «C2»
            // pointed to a different body from the one in the state.
            //
            // The guide does NOT say whether `GetTessTriangles` answers on the faces of a
            // hidden body. For that reason the branch without triangles (finding L12-2)
            // counts the curved faces as not analysed instead of skipping them: if the
            // hidden body does not tessellate, the verdict becomes «I cannot say»
            // and the silent «yes» does not come back. The mini-probe that measures it is
            // still to be run (entry Z-C21.2, CHANGELOG).
            var rawItems = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            var all = (rawItems ?? new object[0]).Select(o => o as IBody2).Where(b => b != null).ToList();
            if (all.Count == 0) { reason = "The part has no solid bodies to look at."; return null; }

            string uid = Str(a, "body_uid", null);
            if (string.IsNullOrWhiteSpace(uid)) return all;

            // THE BODY BY SIGNATURE, as in combine_bodies and move_bodies (scheme S5,
            // finding L6-1): GetBodies2.html, «May vary the order in which bodies
            // are returned».
            var foundItems = BodiesFromUid(docState, new List<string> { uid.Trim() }, a, out reason);
            if (foundItems == null) return null;
            return new List<IBody2> { foundItems[0] };
        }
    }
}
