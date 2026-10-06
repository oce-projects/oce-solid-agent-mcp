using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE ROUND PART SEEN FROM THE SIDE (entry GT14): the lengths between the faces and the
    /// diameters, in the longitudinal view of a bushing, a pin, a
    /// stepped shaft.
    ///
    /// ── WHAT WAS MEASURED FIRST (27/9/2026, `prova0089 tornito`..`tornito8`) ──
    ///
    /// On the stepped shaft of the «revolve» example (Ø30x40, Ø20x30,
    /// Ø34x20), in the front view:
    ///
    ///  * TWO CIRCLES SEEN EDGE-ON selected and `AddDimension2`: the
    ///    distance between the two faces is born (20, 50, 40 — right). It is the length.
    ///  * TWO GENERATRICES OF THE SAME FACE (`GetVisibleEntities2` with type
    ///    `swViewEntityType_SilhouetteEdge` = 4) and `AddDimension2`: the diameter
    ///    dimension is born by itself, with the prefix «<MOD-DIAM>» (Ø34, Ø30).
    ///  * BUT IT IS NOT ALWAYS POSSIBLE: the face of the Ø20 does not appear among the
    ///    visible faces of the view, one of its generatrices has `GetFace` null and the other
    ///    cannot be selected (in `GetPolylines7` the object is null). Of that
    ///    diameter it is said that it is not reached, instead of keeping quiet.
    ///  * ROUTES TRIED AND CLOSED: `ISilhouetteEdge.GetStartPoint` from out of
    ///    process makes SOLIDWORKS raise an exception (RPC_E_SERVERFAULT) and
    ///    `GetCurve` answers null; selection by position (`SelectByID2`
    ///    «SILHOUETTE») takes a single generatrix at a time; the selected
    ///    cylindrical face makes nothing be born; the axis line
    ///    (`InsertCenterLine2`) plus one generatrix makes a dimension measured
    ///    from the origin of the SHEET (212.49), and `Diametric` cannot be
    ///    switched on. Edge-on `AddDiameterDimension2` says 60 for a Ø30.
    ///
    /// IN THE CODE the `IModelDoc2.AddDimension2` of the probes is not used (forbidden by
    /// contract: no direction, entry GP6) and `Extension.AddDimension` with two
    /// entities makes nothing be born: the declared HORIZONTAL and VERTICAL dimensions
    /// are used (`AddHorizontalDimension2` 20 on the two circles,
    /// `AddVerticalDimension2` Ø34 on the two generatrices, with the Ø put by
    /// SOLIDWORKS — probe `orvert`).
    ///
    /// The lengths are IN PARALLEL from the face at one end — the same
    /// criterion as the single zero for all the dimensions (decided on 26/9/2026 for the
    /// holes): the leftmost face, or the lowest if the axis is vertical.
    /// Every dimension is read back against the geometry before staying on the drawing.
    /// </summary>
    public partial class SwOperations
    {
        private sealed class RoundOutcome
        {
            public readonly List<double> Lengths = new List<double>();
            public readonly List<double> Diameters = new List<double>();
            public readonly List<string> NotReached = new List<string>();
            public readonly List<string> Note = new List<string>();
            public int FacelessGeneratrices;
            public int DiscardedPairs;
            public string Axis;
        }

        /// <summary>
        /// Dimensions the round part seen from the side, if the edge-on circles of the view are
        /// coaxial (at least two). Null if the view is not that of a round part.
        /// </summary>
        private RoundOutcome DimensionTheRoundFromTheSide(
            IModelDoc2 model, IDrawingDoc drawing, IView view, IMathUtility mu, MathTransform tr,
            List<Circle> cutting, HashSet<string> already, double[] contour, double dimensionOffset, double planStep,
            ref int below, ref int sideways, List<Dictionary<string, object>> born,
            List<string> elsewhere, List<string> failed)
        {
            if (cutting.Count < 2 || cutting[0].ModelAxis == null) return null;
            var axis = cutting[0].ModelAxis;
            var o = cutting[0].ModelCenter;
            // COAXIAL: same axis, centres on the same line.
            var onAxis = new List<Tuple<double, Circle>>();
            foreach (var c in cutting)
            {
                if (c.ModelAxis == null || c.ModelCenter == null) return null;
                double par = Math.Abs(Dot(c.ModelAxis, axis));
                if (par < 0.9999) return null;
                var d = Sub(c.ModelCenter, o);
                double t = Dot(d, axis);
                var outside = Sub(d, Mul(axis, t));
                if (Standard(outside) > 1e-6) return null;
                onAxis.Add(Tuple.Create(t, c));
            }
            var outcome = new RoundOutcome();
            var sheetAxis = VectorOnSheet(axis, mu, tr);
            if (sheetAxis == null) return null;
            bool horizontal = Math.Abs(sheetAxis[0]) >= Math.Abs(sheetAxis[1]);
            outcome.Axis = horizontal ? "horizontal" : "vertical";
            // The direction of the parameter t on the sheet: from left to right (or from
            // bottom to top), to start from the right face.
            double sheetFacing = horizontal ? sheetAxis[0] : sheetAxis[1];
            onAxis.Sort((a, b) => (a.Item1 * Math.Sign(sheetFacing)).CompareTo(b.Item1 * Math.Sign(sheetFacing)));
            // One position per face: two circles at the same t are the same face.
            //
            // AND ONLY THE CIRCLES OF A PLANAR END FACE OR SHOULDER FACE
            // (adversarial review of 27/9/2026). A chamfer makes two circles,
            // cone-plane and cone-cylinder, and a fillet with the tangent edges
            // visible does the same: counted as faces, on a Ø30x40 chamfered 1x45°
            // 1, 39 and 40 were born — the «legs» of the chamfer that the rest of the
            // drawing discards as extra dimensions (IsChamferLeg). A circle is a
            // face of the round part if it borders a PLANAR face perpendicular to the axis;
            // the others are stated, not dimensioned.
            var faces = new List<Tuple<double, Circle>>();
            var chamfers = new List<string>();
            foreach (var p in onAxis)
            {
                string border = WhatBordersTheCircle(p.Item2.Edge, axis);
                if (border != "plane")
                {
                    if (border == "cone" || border == "torus")
                    {
                        string said = (border == "cone" ? "chamfer" : "fillet") + " on Ø" + p.Item2.DiameterMm.ToString("0.##", Ci);
                        if (!chamfers.Contains(said)) chamfers.Add(said);
                    }
                    continue;
                }
                if (faces.Count == 0 || Math.Abs(p.Item1 - faces[faces.Count - 1].Item1) > 1e-6) faces.Add(p);
            }
            foreach (var sm in chamfers)
                outcome.Note.Add(sm + " " +
                                       "(it is not a face of the round part: its measure goes with the chamfer or fillet dimension, which this view does not put)");
            if (faces.Count < 2)
                outcome.Note.Add("lengths (fewer than two planar end or shoulder faces seen edge-on)");

            var selMgr = model.SelectionManager as ISelectionMgr;
            // ── THE LENGTHS, IN PARALLEL FROM THE FIRST FACE ───────────────────
            var zero = faces.Count > 0 ? faces[0] : null;
            for (int i = 1; i < faces.Count; i++)
            {
                var f = faces[i];
                double expectedValueMm = Math.Round(Math.Abs(f.Item1 - zero.Item1) * 1000.0, 3);
                string key = RoundKey(zero.Item2.ModelCenter, f.Item2.ModelCenter);
                string what = "length " + expectedValueMm.ToString("0.##", Ci);
                if (already.Contains(key)) { elsewhere.Add(what + " (already dimensioned in this drawing)"); continue; }
                double x, y;
                if (horizontal)
                {
                    x = (zero.Item2.SheetCenter[0] + f.Item2.SheetCenter[0]) / 2;
                    y = contour[1] - dimensionOffset - below * planStep;
                }
                else
                {
                    x = contour[2] + dimensionOffset + sideways * planStep;
                    y = (zero.Item2.SheetCenter[1] + f.Item2.SheetCenter[1]) / 2;
                }
                bool bornWithoutObject;
                var q = DimensionBetweenTwo(model, drawing, view, selMgr,
                                    e => (zero.Item2.Edge as IEntity).Select4(false, e) &&
                                         (f.Item2.Edge as IEntity).Select4(true, e), x, y, horizontal,
                                    out bornWithoutObject);
                if (q == null && !bornWithoutObject) { failed.Add(what + " (the dimension was not created)"); continue; }
                // BORN WITHOUT ITS OBJECT (finding N80, the site Z-C21.8/9 left
                // out): the dimension is on the sheet and its value cannot be read
                // back. It keeps its place (the next length goes one step
                // further out), its row with the value unknown, and its key in
                // `gia`: another view of the sheet must not dimension it again.
                if (q != null && !CorrectReread(model, q, expectedValueMm, false, what, failed)) continue;
                if (horizontal) below++; else sideways++;
                var described = DescribeDimension(q, f.Item2.Index);
                described["orientation"] = "round length";
                described["position_mm"] = WhereItEnded(q);
                if (q != null)
                {
                    born.Add(described);
                    outcome.Lengths.Add(expectedValueMm);
                }
                else RecordBornWithoutObject(born, described, failed,
                    "turned length " + expectedValueMm.ToString("0.##", Ci) + " (the dimension was born but SOLIDWORKS did not " +
                    "hand it back: its value was not read back, check it on the sheet)");
                already.Add(key);
            }

            // THE COAXIAL CYLINDERS OF THE PART (diameter, and the point halfway along
            // the axis): they say which diameters are missing, and they recognise a dimension born
            // from two generatrices whose face SOLIDWORKS does not tell.
            var cylinders = CoaxialCylinders(view, axis, o);

            // ── THE DIAMETERS, FROM THE TWO GENERATRICES OF EACH FACE ────────────
            //
            // THE GENERATRICES ARE READ AGAIN BEFORE EVERY DIMENSION: they are objects of the
            // view, not of the model, and after a new dimension the ones read before
            // may no longer be selectable (measured on 27/9/2026, `orvert`: the
            // second pair «presa True/False»). The dimension is a declared HORIZONTAL or
            // VERTICAL one (AddVerticalDimension2 for a horizontal axis):
            // on two generatrices of the same face SOLIDWORKS puts the
            // Ø in by itself. `Extension.AddDimension` with two entities makes nothing be born.
            bool horizontalDiameterDimension = !horizontal;
            var done = new HashSet<string>(StringComparer.Ordinal);
            for (int benchRun = 0; benchRun < 12; benchRun++)
            {
                var readOnes = ReadGeneratrices(model, view);
                outcome.FacelessGeneratrices = readOnes.WithoutFace.Count;
                string choice = null;
                foreach (var kv in readOnes.Groups)
                    if (kv.Value.Count == 2 && !done.Contains(kv.Key)) { choice = kv.Key; break; }
                if (choice == null) break;
                done.Add(choice);
                var m = readOnes.Meta[choice];
                double expectedValueMm = Math.Round(m[0] * 2000.0, 3);
                string what = "diameter Ø" + expectedValueMm.ToString("0.##", Ci);
                string key = "hole|" + Math.Round(expectedValueMm, 2).ToString("0.00", Ci);
                // HALFWAY ALONG THE FACE, on the axis: the measurement line crosses its
                // cylinder, the way a shaft is dimensioned. The same point makes the
                // CYLINDER KEY (review of 27/9/2026): two Ø30 pins
                // separated by a collar are two dimensions, not one.
                double tm = (m[1] + m[2]) / 2;
                string cylinder = CylinderKey(m[0] * 2000.0, new[] { m[3] + tm * m[6], m[4] + tm * m[7], m[5] + tm * m[8] });
                if (already.Contains(cylinder) || already.Contains("cyl?|" + key.Substring(5)))
                { elsewhere.Add(what + " (already dimensioned in this drawing)"); continue; }
                var half = OnSheet(new[] { m[3] + tm * m[6], m[4] + tm * m[7], m[5] + tm * m[8] }, mu, tr);
                if (half == null) { failed.Add(what + " (I do not know where it is on the sheet)"); continue; }
                var a = readOnes.Groups[choice][0]; var b = readOnes.Groups[choice][1];
                bool bornWithoutObject;
                var q = DimensionBetweenTwo(model, drawing, view, selMgr, e => a.Select2(false, e) && b.Select2(true, e),
                                    half[0], half[1], horizontalDiameterDimension, out bornWithoutObject);
                if (q == null && !bornWithoutObject) { failed.Add(what + " (the dimension was not created)"); continue; }
                // BORN WITHOUT ITS OBJECT (N80), as the lengths above: the row
                // with the value unknown, not in `round_diameters_mm` (which lists
                // the diameters read back with their Ø), and the keys of this
                // cylinder in `gia`, so that it is neither dimensioned again nor
                // reported as not reached.
                if (q != null && !CorrectReread(model, q, expectedValueMm, true, what, failed)) continue;
                var described = DescribeDimension(q, -1);
                described["orientation"] = "round diameter";
                described["position_mm"] = WhereItEnded(q);
                if (q != null)
                {
                    born.Add(described);
                    outcome.Diameters.Add(expectedValueMm);
                }
                else RecordBornWithoutObject(born, described, failed,
                    "turned diameter Ø" + expectedValueMm.ToString("0.##", Ci) + " (the dimension was born but SOLIDWORKS did not " +
                    "hand it back: its value and its Ø symbol were not read back, check it on the sheet)");
                already.Add(key);
                already.Add(cylinder);
            }

            // ── THE GENERATRICES WITHOUT FACE, TRIED IN PAIRS ───────────────────
            // Measured on 27/9/2026: on the bushing GetFace stays null even after
            // three passes, and those generatrices can be selected all the same. Every
            // pair is tried (by position in the list, which is re-read every time)
            // and the dimension stays ONLY IF SOLIDWORKS itself puts the Ø in (it does so
            // when the two generatrices belong to the same cylinder) AND the value is
            // the diameter of a coaxial cylinder of the part not yet dimensioned.
            // Otherwise it is removed. Then the text goes to the middle of the cylinder.
            // The generatrices that have the face but were left alone enter the test: their
            // partner is among those without.
            var tested = new HashSet<string>(StringComparer.Ordinal);
            double cx = (contour[0] + contour[2]) / 2, cy = (contour[1] + contour[3]) / 2;
            for (int benchRun = 0; benchRun < 30; benchRun++)
            {
                var readOnes = ReadGeneratrices(model, view);
                var candidate = new List<int>(readOnes.WithoutFace);
                foreach (var kv in readOnes.Groups) if (kv.Value.Count == 1) candidate.Add(readOnes.Indices[kv.Key][0]);
                candidate.Sort();
                int ia = -1, ib = -1;
                for (int x = 0; x < candidate.Count && ia < 0; x++)
                    for (int y = x + 1; y < candidate.Count; y++)
                        if (tested.Add(candidate[x] + "|" + candidate[y])) { ia = candidate[x]; ib = candidate[y]; break; }
                if (ia < 0) break;
                var a = readOnes[ia]; var b = readOnes[ib];
                if (a == null || b == null) continue;
                bool bornWithoutObject;
                var q = DimensionBetweenTwo(model, drawing, view, selMgr, e => a.Select2(false, e) && b.Select2(true, e),
                                    cx, cy, horizontalDiameterDimension, out bornWithoutObject);
                if (bornWithoutObject)
                {
                    // BORN WITHOUT ITS OBJECT (N80), and here it cannot be judged:
                    // without the object there is no value to match to a
                    // cylinder, no Ø to look for, and nothing to remove
                    // (RemoveDimension selects the annotation of the object). Until
                    // library 0.145.0 this was «not born» and the next pair was
                    // tried: the dimension stayed on the sheet unreported, and a
                    // later pair of the same cylinder could dimension it a second
                    // time. Now it is said, and no further pair is tried.
                    RecordBornWithoutObject(born, DescribeDimension(null, -1), failed,
                        "a dimension between two silhouette edges without a face (it was born but SOLIDWORKS did not " +
                        "hand it back: it could not be read, matched to a cylinder or removed, and the remaining pairs " +
                        "were not tried, so as not to dimension the same cylinder twice; it may be one of the diameters " +
                        "reported as not reached. Check it on the sheet)");
                    break;
                }
                if (q == null) continue;
                double read = double.NaN; string prefix = "";
                try { read = Math.Round(ApiHelpers.ReadDimensionInConfiguration(q.GetDimension2(0) as IDimension, "") * 1000.0, 3); } catch { }
                try { prefix = q.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? ""; } catch { }
                RoundCylinder its = null;
                foreach (var c in cylinders)
                    if (!double.IsNaN(read) && Math.Abs(c.DiameterMm - read) <= 0.01 && !DimensionedCylinder(already, c)) { its = c; break; }
                if (prefix.Contains("<MOD-DIAM>") && its == null && !double.IsNaN(read) &&
                    cylinders.Any(c => Math.Abs(c.DiameterMm - read) <= 0.01))
                {
                    // A TRUE DIAMETER, but already dimensioned elsewhere: it is removed and stated.
                    RemoveDimension(model, q);
                    string said = "diameter Ø" + read.ToString("0.##", Ci) + " (already dimensioned in this drawing)";
                    if (!elsewhere.Contains(said)) elsewhere.Add(said);
                    continue;
                }
                if (!prefix.Contains("<MOD-DIAM>") || its == null) { RemoveDimension(model, q); outcome.DiscardedPairs++; continue; }
                var half = OnSheet(its.Meta, mu, tr);
                try { if (half != null) (q.GetAnnotation() as IAnnotation)?.SetPosition2(half[0], half[1], 0); } catch { }
                var described = DescribeDimension(q, -1);
                described["orientation"] = "diameter of the round part (silhouette edges with no face, recognized by the value)";
                described["position_mm"] = WhereItEnded(q);
                born.Add(described);
                outcome.Diameters.Add(Math.Round(its.DiameterMm, 3));
                already.Add("hole|" + Math.Round(its.DiameterMm, 2).ToString("0.00", Ci));
                already.Add(its.Key);
            }

            // ── AND THOSE THE VIEW DOES NOT EXPOSE ──────────────────────────────
            // The coaxial cylinders of the part whose diameter, at this point, is not
            // dimensioned in any view of the drawing.
            var called = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in cylinders)
            {
                string itemName = "Ø" + Math.Round(c.DiameterMm, 2).ToString("0.##", Ci);
                if (DimensionedCylinder(already, c) || !called.Add(c.Key)) continue;
                outcome.NotReached.Add(itemName + " " +
                                       "(not reached by this view: hidden silhouette edges, or ones that SOLIDWORKS does not give. If it is a hole, dimension it from the view where it is a circle)");
            }
            return outcome;
        }

        /// <summary>A cylinder of the round part: the diameter, the point halfway along the axis (m, in the model), the key.</summary>
        private sealed class RoundCylinder
        {
            public double DiameterMm;
            public double[] Meta;
            public string Key;
        }

        /// <summary>That cylinder is already dimensioned: its key, or an equal Ø of which the cylinder is not known.</summary>
        private static bool DimensionedCylinder(HashSet<string> already, RoundCylinder c)
        {
            return already.Contains(c.Key) || already.Contains("cyl?|" + Math.Round(c.DiameterMm, 2).ToString("0.00", Ci));
        }

        /// <summary>
        /// The cylinders of the part coaxial with the round part, each with the point halfway along
        /// the axis and its key. Empty if the view is not of a part.
        /// </summary>
        private static List<RoundCylinder> CoaxialCylinders(IView view, double[] axis, double[] o)
        {
            var outside = new List<RoundCylinder>();
            try
            {
                var partDoc = view.ReferencedDocument as IPartDoc;
                if (partDoc == null) return outside;
                foreach (var ob in (partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[]) ?? new object[0])
                    foreach (var fo in ((ob as IBody2)?.GetFaces() as object[]) ?? new object[0])
                    {
                        var face = fo as IFace2;
                        var su = face?.GetSurface() as ISurface;
                        if (su == null || !su.IsCylinder()) continue;
                        var cp = su.CylinderParams as double[];
                        if (Math.Abs(Dot(new[] { cp[3], cp[4], cp[5] }, axis)) < 0.9999) continue;
                        var d = Sub(new[] { cp[0], cp[1], cp[2] }, o);
                        if (Standard(Sub(d, Mul(axis, Dot(d, axis)))) > 1e-6) continue;
                        double tmin, tmax;
                        if (!FaceEnds(face, cp, out tmin, out tmax)) continue;
                        double tm = (tmin + tmax) / 2;
                        var meta = new[] { cp[0] + tm * cp[3], cp[1] + tm * cp[4], cp[2] + tm * cp[5] };
                        outside.Add(new RoundCylinder { DiameterMm = Math.Round(cp[6] * 2000.0, 3), Meta = meta, Key = CylinderKey(cp[6] * 2000.0, meta) });
                    }
            }
            catch { }
            return outside;
        }

        /// <summary>Removes a dimension just born. DeleteSelection2, not EditDelete (it can open a window).</summary>
        private static bool RemoveDimension(IModelDoc2 model, IDisplayDimension q)
        {
            bool removed = false;
            try
            {
                model.ClearSelection2(true);
                var an = q.GetAnnotation() as IAnnotation;
                if (an != null && an.Select3(false, null))
                    removed = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
            }
            catch { }
            return removed;
        }

        private sealed class Generatrices
        {
            public object[] All = new object[0];
            public readonly Dictionary<string, List<ISilhouetteEdge>> Groups = new Dictionary<string, List<ISilhouetteEdge>>(StringComparer.Ordinal);
            public readonly Dictionary<string, List<int>> Indices = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            public readonly Dictionary<string, double[]> Meta = new Dictionary<string, double[]>(StringComparer.Ordinal);
            public readonly List<int> WithoutFace = new List<int>();
            public ISilhouetteEdge this[int i] { get { return All[i] as ISilhouetteEdge; } }
        }

        /// <summary>
        /// The generatrices of the view, grouped by face (radius and ends
        /// along the axis). FIRST IT IS REDRAWN AND THE FACE IS ASKED SEVERAL TIMES:
        /// measured on 27/9/2026 (`prova0089 gendiag`), `GetFace` answers null
        /// on all of them until the drawing is redrawn and the question repeated.
        /// </summary>
        private static Generatrices ReadGeneratrices(IModelDoc2 model, IView view)
        {
            var g = new Generatrices();
            // OBSOLETE-MEASURED: stepped_shaft_dimensioned_from_the_side — IModelDoc2~GraphicsRedraw2.html:
            // "Obsolete. Superseded by IModelView::GraphicsRedraw". The redraw is what makes
            // ISilhouetteEdge.GetFace answer (null on every edge until then: probe prova0089
            // gendiag, 27/9/2026). The replacement was not measured here: remeasure in Z-C21.7 (N20).
            try { model.GraphicsRedraw2(); } catch { }
            try { g.All = (view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_SilhouetteEdge) as object[]) ?? new object[0]; } catch { }
            var faces = new IFace2[g.All.Length];
            for (int benchRun = 0; benchRun < 3; benchRun++)
            {
                bool isMissing = false;
                for (int i = 0; i < g.All.Length; i++)
                {
                    if (faces[i] != null) continue;
                    try { faces[i] = (g.All[i] as ISilhouetteEdge)?.GetFace() as IFace2; } catch { }
                    if (faces[i] == null) isMissing = true;
                }
                if (!isMissing) break;
                // OBSOLETE-MEASURED: stepped_shaft_dimensioned_from_the_side — same measure as above.
                try { model.GraphicsRedraw2(); } catch { }
            }
            for (int i = 0; i < g.All.Length; i++)
            {
                var se = g.All[i] as ISilhouetteEdge;
                if (se == null) continue;
                var su = faces[i]?.GetSurface() as ISurface;
                double tmin, tmax;
                double[] cp = su != null && su.IsCylinder() ? su.CylinderParams as double[] : null;
                if (cp == null || !FaceEnds(faces[i], cp, out tmin, out tmax)) { g.WithoutFace.Add(i); continue; }
                string k = Math.Round(cp[6] * 1000, 3).ToString("0.###", Ci) + "|" +
                           Math.Round(tmin * 1000, 2).ToString("0.##", Ci) + "|" + Math.Round(tmax * 1000, 2).ToString("0.##", Ci);
                if (!g.Groups.ContainsKey(k))
                {
                    g.Groups[k] = new List<ISilhouetteEdge>();
                    g.Indices[k] = new List<int>();
                    g.Meta[k] = new[] { cp[6], tmin, tmax, cp[0], cp[1], cp[2], cp[3], cp[4], cp[5] };
                }
                g.Groups[k].Add(se);
                g.Indices[k].Add(i);
            }
            return g;
        }

        /// <summary>
        /// Selects (with the view in the SelectData) and makes a declared HORIZONTAL
        /// or VERTICAL dimension be born; null if it is not born.
        ///
        /// «Not born» is the count of dimensions, asked FIRST (finding N80 of
        /// Z-C21.8, at the one site that lot left out). When the count went up
        /// and the call returned null, the dimension is on the sheet without its
        /// object: the answer is null with <paramref name="bornWithoutObject"/>
        /// true, and each caller describes it instead of taking it for «not
        /// born». Until library 0.145.0 this read «q == null || not grown» and
        /// answered null for both, so the three callers reported as missing a
        /// dimension that was on the sheet and left its key out of `gia`: another
        /// view, or another pair of silhouette edges, could then dimension the
        /// same thing a second time.
        /// </summary>
        private IDisplayDimension DimensionBetweenTwo(IModelDoc2 model, IDrawingDoc drawing, IView view, ISelectionMgr selMgr,
                                              Func<SelectData, bool> doSelect, double x, double y, bool horizontal,
                                              out bool bornWithoutObject)
        {
            bornWithoutObject = false;
            int before = CountDimensions(drawing);
            IDisplayDimension q = null;
            try
            {
                model.ClearSelection2(true);
                var sd = selMgr?.CreateSelectData() as SelectData;
                if (sd == null) return null;
                sd.View = (View)view;
                // DECLARED HORIZONTAL OR VERTICAL: IModelDoc2.AddDimension2 is
                // forbidden (no direction, entry GP6) and Extension.AddDimension with
                // two entities selected makes nothing be born (measured).
                if (doSelect(sd))
                    q = (horizontal ? model.AddHorizontalDimension2(x, y, 0)
                                     : model.AddVerticalDimension2(x, y, 0)) as IDisplayDimension;
            }
            catch { }
            try { model.ClearSelection2(true); } catch { }
            if (NotGrown(drawing, before, q)) return null;
            bornWithoutObject = q == null;
            return q;
        }

        /// <summary>
        /// Does the dimension read back equal the geometry (and, if it is a diameter, does it carry
        /// the symbol)? If not it is removed: a wrong and plausible number does not
        /// stay on the drawing.
        /// </summary>
        private static bool CorrectReread(IModelDoc2 model, IDisplayDimension q, double expectedValueMm, bool diameter,
                                          string what, List<string> failed)
        {
            double read = double.NaN;
            string prefix = "";
            try { read = ApiHelpers.ReadDimensionInConfiguration(q.GetDimension2(0) as IDimension, "") * 1000.0; } catch { }
            try { prefix = q.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? ""; } catch { }
            bool correct = !double.IsNaN(read) && Math.Abs(read - expectedValueMm) <= 0.01 &&
                          (!diameter || prefix.Contains("<MOD-DIAM>"));
            if (correct) return true;
            bool removed = RemoveDimension(model, q);
            failed.Add(what + " (SOLIDWORKS wrote " +
                        (double.IsNaN(read) ? "?" : Math.Round(read, 3).ToString("0.###", Ci)) +
                        (diameter && !prefix.Contains("<MOD-DIAM>") ? " without Ø" : "") +
                        (removed ? ": I removed it)" : ": remove it)"));
            return false;
        }

        /// <summary>The ends of the cylindrical face along its axis, from its circles.</summary>
        private static bool FaceEnds(IFace2 face, double[] cp, out double tmin, out double tmax)
        {
            tmin = double.MaxValue; tmax = double.MinValue;
            try
            {
                foreach (var oe in (face.GetEdges() as object[]) ?? new object[0])
                {
                    var c = (oe as IEdge)?.GetCurve() as ICurve;
                    if (c == null || !c.IsCircle()) continue;
                    var cc = c.CircleParams as double[];
                    double t = (cc[0] - cp[0]) * cp[3] + (cc[1] - cp[1]) * cp[4] + (cc[2] - cp[2]) * cp[5];
                    tmin = Math.Min(tmin, t); tmax = Math.Max(tmax, t);
                }
            }
            catch { return false; }
            return tmax > tmin;
        }

        /// <summary>
        /// The key of ONE cylinder of the part: the diameter and the point halfway along
        /// the axis, in the model («cil|30.00|x,y,z», millimetres to the tenth). Two
        /// steps of the same Ø, or a hole of the same Ø as a step, are
        /// different cylinders and have different keys: with the key of the diameter
        /// alone one cancelled the other (adversarial review of 27/9/2026).
        /// The midpoint does not depend on the origin the surface chooses
        /// on the axis, so the two halves of a split face give the same one.
        /// </summary>
        private static string CylinderKey(double diameterMm, double[] meta)
        {
            return "cil|" + Math.Round(diameterMm, 2).ToString("0.00", Ci) + "|" +
                   string.Join(",", meta.Take(3).Select(v => (Math.Round(v * 1000, 1) + 0.0).ToString("0.0", Ci)));
        }

        /// <summary>The cylinder key of a face, or null if the face is not a cylinder that can be read.</summary>
        private static string CylinderKey(IFace2 face)
        {
            try
            {
                var su = face?.GetSurface() as ISurface;
                if (su == null || !su.IsCylinder()) return null;
                var cp = su.CylinderParams as double[];
                double tmin, tmax;
                if (cp == null || cp.Length < 7 || !FaceEnds(face, cp, out tmin, out tmax)) return null;
                double tm = (tmin + tmax) / 2;
                return CylinderKey(cp[6] * 2000.0, new[] { cp[0] + tm * cp[3], cp[1] + tm * cp[4], cp[2] + tm * cp[5] });
            }
            catch { return null; }
        }

        /// <summary>
        /// The key of the cylinder of which the circle is an edge (same radius), or
        /// null. By co-edges and rings, like ThroughAllFromGeometry: it is the route
        /// that answers from out of process.
        /// </summary>
        private static string CylinderCircleKey(IEdge e)
        {
            try
            {
                var cb = (e?.GetCurve() as ICurve)?.CircleParams as double[];
                if (cb == null || cb.Length < 7) return null;
                foreach (var oc in (e.GetCoEdges() as object[]) ?? new object[0])
                {
                    var f = ((oc as ICoEdge)?.GetLoop() as ILoop2)?.GetFace() as IFace2;
                    var su = f?.GetSurface() as ISurface;
                    if (su == null || !su.IsCylinder()) continue;
                    var cp = su.CylinderParams as double[];
                    if (cp == null || cp.Length < 7 || Math.Abs(cp[6] - cb[6]) > 1e-8) continue;
                    return CylinderKey(f);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Which face a circle of the round part borders: «plane» if one of its faces is a
        /// plane perpendicular to the axis (a head, a shoulder), otherwise
        /// «cono» (a chamfer), «toro» (a fillet) or «altro».
        /// </summary>
        private static string WhatBordersTheCircle(IEdge edge, double[] axis)
        {
            string found = "other";
            try
            {
                foreach (var oc in (edge?.GetCoEdges() as object[]) ?? new object[0])
                {
                    var su = (((oc as ICoEdge)?.GetLoop() as ILoop2)?.GetFace() as IFace2)?.GetSurface() as ISurface;
                    if (su == null) continue;
                    if (su.IsPlane())
                    {
                        var pp = su.PlaneParams as double[];
                        if (pp != null && pp.Length >= 3 && Math.Abs(Dot(new[] { pp[0], pp[1], pp[2] }, axis)) > 0.9999) return "plane";
                    }
                    else if (su.IsCone()) found = "cone";
                    else if (su.IsTorus() && found != "cone") found = "torus";
                }
            }
            catch { }
            return found;
        }

        /// <summary>The key of a length of the round part: the centres of the two faces, in the model.</summary>
        private static string RoundKey(double[] a, double[] b)
        {
            Func<double[], string> s = p => string.Join(",", p.Take(3).Select(v => Math.Round(v * 1000, 2).ToString("0.##", Ci)));
            var x = s(a); var y = s(b);
            return "round|" + (string.CompareOrdinal(x, y) <= 0 ? x + "|" + y : y + "|" + x);
        }

        /// <summary>Two circles (CircleParams) on the same axis, with different centres?</summary>
        private static bool Coaxial(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length < 6 || b.Length < 6) return false;
            var axis = new[] { a[3], a[4], a[5] };
            if (Math.Abs(Dot(axis, new[] { b[3], b[4], b[5] })) < 0.9999) return false;
            var d = new[] { b[0] - a[0], b[1] - a[1], b[2] - a[2] };
            double t = Dot(d, axis);
            return Math.Abs(t) > 1e-7 && Standard(Sub(d, Mul(axis, t))) < 1e-6;
        }

        private static double[] VectorOnSheet(double[] v, IMathUtility mu, MathTransform tr)
        {
            try { return ((IMathVector)((IMathVector)mu.CreateVector(v)).MultiplyTransform(tr)).ArrayData as double[]; }
            catch { return null; }
        }

        private static double Dot(double[] a, double[] b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
        private static double[] Sub(double[] a, double[] b) { return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] }; }
        private static double[] Mul(double[] a, double k) { return new[] { a[0] * k, a[1] * k, a[2] * k }; }
        private static double Standard(double[] a) { return Math.Sqrt(Dot(a, a)); }
    }
}
