using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// A PLANE FROM THREE POINTS. (entry GP9)
    ///
    /// `create_plane` could only make the plane OFFSET from one of the three
    /// standard ones: that serves everything aligned with the axes, and nothing
    /// else. A slanted hole, an oblique face, a bracket at 30 degrees have no
    /// plane to draw on.
    ///
    /// ── WHY THREE COORDINATES AND NOT THREE VERTICES ──────────────────
    ///
    ///   IModelDoc2.CreatePlaneThru3Points3(AutoSize)  -> three selected VERTICES
    ///   IModelDoc2.CreatePlaneFixed2(P1,P2,P3,UseGlobal) -> three COORDINATES
    ///
    /// The second is used, and it is not a preference: **the document state does
    /// not list the vertices**. Faces and edges have an identifier, vertices
    /// do not — so «three selected vertices» is something the language
    /// model has no way to ask for, while three triples of numbers it does.
    ///
    /// ── THE NORMAL IS IN THE THIRD ROW, AND IT COULD HAVE BEEN GOT WRONG ──
    ///
    /// The plane's normal is read from `IRefPlane.Transform.ArrayData`, which has
    /// sixteen numbers: the first nine are the rotation. **The normal is the third
    /// ROW** (indices 6,7,8), not the third column. Measured on 16/9/2026 on
    /// three planes of known normal:
    ///
    ///   points                               expected normal     third row
    ///   (0,0,20) (100,0,20) (0,100,20)       (0, 0, 1)           (0, 0, 1)
    ///   (0,0,0)  (100,0,0)  (0,0,100)        (0, -1, 0)          (0, -1, 0)
    ///   (0,0,0)  (100,0,0)  (0,100,100)      (0, -.7071, .7071)  (0, -.7071, .7071)
    ///
    /// On the first two the third row and the third column **give the same
    /// result**, and a probe made only with those would have chosen at random between
    /// the two, with one chance in two of being wrong forever. On the third the
    /// column gives `(0, 0.7071, 0.7071)` — turned. *A case in which two wrong
    /// values give the same result proves nothing*: the oblique plane
    /// is in the probe on purpose.
    ///
    /// ── `UseGlobal` DOES NOT MOVE THE NORMAL; IT TURNS THE PLANE'S X AXIS ──
    ///
    /// Tried `true` and `false` on the same three points: the same transform,
    /// number for number. That measure does not decide what UseGlobal does:
    /// all three point sets above have P1->P2 = (100, 0, 0), along the global
    /// X axis, and on those the two branches coincide by construction (audit
    /// finding L8-3). IModelDoc2~CreatePlaneFixed2.html, Remarks: «The x-axis
    /// of the planes' coordinate system are a vector from P1 to P2 or a vector
    /// projected from the x-axis of the global coordinate system onto the
    /// plane», and with true «This does not reorient the plane. Instead, it
    /// rotates the plane coordinate system about P1 until the x-axis of the
    /// plane aligns with the projected vector». So the NORMAL, the only thing
    /// read back here, is the same either way; the plane's own X axis is not.
    /// Nothing in the catalogue draws on a plane made here, so today the
    /// difference cannot be seen: `true` stays, unexposed, and the probe with
    /// P1->P2 along Y is due the day an operation uses the plane's own axes.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome PlaneThroughThreePoints(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var points = new List<double[]>();
            foreach (var key in new[] { "point1_mm", "point2_mm", "point3_mm" })
            {
                var p = TripletMm(a, key);
                if (p == null)
                    return Failed(
                        "«" + key + "» must be three numbers in millimeters, for example [0, " +
                        "0, 20]. All three points are needed: a plane through two points is not one, " +
                        "there are infinitely many.");
                points.Add(p);
            }

            // ── THREE POINTS IN A ROW DO NOT MAKE A PLANE ────────────────
            //
            // SOLIDWORKS answers null and does not say why. Here we look first,
            // because the why is known: the cross product is zero.
            var u = Less(points[1], points[0]);
            var v = Less(points[2], points[0]);
            var n = VectorProduct(u, v);
            double module = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
            double lu = Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
            double lv = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (lu < 1e-9 || lv < 1e-9)
                return Failed("Two of the three points are the same point: like that they do not make a plane.");
            if (module / (lu * lv) < 1e-6)
                return Failed(string.Format(
                    CultureInfo.InvariantCulture,
                    "The three points are in a line: ({0}), ({1}) and ({2}) are on the same straight " +
                    "line, and through a line pass infinitely many planes. Move one of them off it.",
                    Write(points[0]), Write(points[1]), Write(points[2])));

            var expectation = new[] { n[0] / module, n[1] / module, n[2] / module };

            int planesBefore = CountPlanes(model);
            var namesBefore = FeatureNamesNow(model);
            object raw;
            try
            {
                model.ClearSelection2(true);
                raw = model.CreatePlaneFixed2(
                    points[0].Select(Units.M).ToArray(),
                    points[1].Select(Units.M).ToArray(),
                    points[2].Select(Units.M).ToArray(),
                    // UseGlobal: same normal either way; it turns only the
                    // plane's own X axis (see the note at the top, L8-3).
                    true);
            }
            catch (Exception ex) { return Failed("The plane was not created: " + ex.Message); }

            int planesAfter = CountPlanes(model);
            // «No plane appeared» is said of two counts that were read; with
            // one unreadable, the returned object stays (entry Z-C8).
            if ((planesBefore >= 0 && planesAfter >= 0 && planesAfter == planesBefore) ||
                ((planesBefore < 0 || planesAfter < 0) && raw == null))
                return Failed(
                    "The plane was not created, and none appeared in the tree. " +
                    "SOLIDWORKS does not say why.");

            // THE PLANE IS WHAT THE CALL RETURNED, or else the ONE RefPlane born
            // since the call (FeatureBorn, scheme S7, Z-C21.5). Until then the
            // feature reported was the last one of the tree: on a sheet-metal
            // part the flat pattern, answered as the plane just made (finding
            // M9), while the normal came from the returned object.
            var feature = raw as IFeature;
            if (feature == null)
                feature = FeatureBorn(model, namesBefore, "RefPlane", out _);
            var plane = raw as IRefPlane;
            if (plane == null && feature != null)
                try { plane = feature.GetSpecificFeature2() as IRefPlane; } catch { }

            // ── AND NOW THE NORMAL IS READ BACK ──────────────────────────
            //
            // We already did the hand calculation above, and it was needed
            // anyway to refuse the points in a row. Comparing it with what
            // the plane says it is is the only proof that the plane is
            // THE ONE: «one more feature in the tree» would say it of a wrong
            // plane too.
            double[] read = PlaneNormal(plane);
            var measurements = new Dictionary<string, object>
            {
                ["points_mm"] = points.Select(p => p.Select(x => Math.Round(x, 4)).ToArray()).ToArray(),
                ["normal_computed"] = expectation.Select(x => Math.Round(x, 6)).ToArray(),
                ["planes_before"] = NumberOrNull(planesBefore),
                ["planes_now"] = NumberOrNull(planesAfter),
            };

            if (read == null)
            {
                measurements["normal_reread"] = null;
                measurements["warning"] =
                    "The plane is there but I could not read its orientation back, so I " +
                    "cannot guarantee that it is the one of the three points you gave me.";
            }
            else
            {
                // The direction may be one or the other: a plane has no
                // up and down. The DIRECTION is compared.
                double cosine = Math.Abs(
                    read[0] * expectation[0] + read[1] * expectation[1] + read[2] * expectation[2]);
                if (cosine > 1) cosine = 1;
                double deviation = Math.Acos(cosine) * 180.0 / Math.PI;
                measurements["normal_reread"] = read.Select(x => Math.Round(x, 6)).ToArray();
                measurements["deviation_deg"] = Math.Round(deviation, 4);
                if (deviation > 0.01)
                    return Failed(string.Format(
                        CultureInfo.InvariantCulture,
                        "The plane was created but it is not what I asked for: from the three points " +
                        "the normal comes out ({0}), and the plane says ({1}) — {2:0.###} degrees " +
                        "of deviation. I do not confirm a plane on which you would draw crooked. " +
                        "Withdraw it — " + Sentences.HowToWithdrawIt + ".",
                        Write(expectation), Write(read), deviation));
            }

            _log("[agent] plane through three points, normal (" + Write(expectation) + ")");

            return feature != null
                ? Succeeded(feature, measurements, expectation)
                : SucceededWithoutCreating(measurements);
        }

        // =================================================================

        /// <summary>
        /// The normal of a reference plane.
        ///
        /// `IRefPlane.Transform.ArrayData` has sixteen numbers; the first nine are
        /// the rotation. **The normal is the third ROW**, indices 6, 7 and 8 —
        /// measured, and the third column on an oblique plane gives a turned
        /// vector. See the note at the top of the class.
        ///
        /// One reading for the whole library since Z-C21.5:
        /// References.PlaneFrame, which the base planes are recognised by too
        /// (scheme S19).
        /// </summary>
        private static double[] PlaneNormal(IRefPlane plane)
        {
            double[] normal, origin;
            return References.PlaneFrame(plane, out normal, out origin) ? normal : null;
        }

        private static double[] TripletMm(Dictionary<string, object> a, string key)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null) return null;
            var e = v as System.Collections.IEnumerable;
            if (e == null || v is string) return null;
            var n = new List<double>();
            foreach (var x in e)
            {
                try { n.Add(Convert.ToDouble(x, CultureInfo.InvariantCulture)); }
                catch { return null; }
            }
            return n.Count == 3 ? n.ToArray() : null;
        }

        private static double[] Less(double[] a, double[] b)
        {
            return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        }

        private static double[] VectorProduct(double[] u, double[] v)
        {
            return new[]
            {
                u[1] * v[2] - u[2] * v[1],
                u[2] * v[0] - u[0] * v[2],
                u[0] * v[1] - u[1] * v[0],
            };
        }

        private static string Write(double[] v)
        {
            return string.Join(", ", v.Select(x =>
                x.ToString("0.####", CultureInfo.InvariantCulture)));
        }
    }
}
