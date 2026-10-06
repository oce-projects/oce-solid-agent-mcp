using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// SHEET METAL (entry GP18): the base flange and the flat pattern for cutting.
    ///
    /// ── THE MEASURE THAT MADE IT BUILDABLE, 24/9/2026 ──────────────────────
    ///
    /// `Checks/SheetMetal`: an L of 50 + 30, 40 deep, T 2, R 2, K 0.5
    /// re-read from the sheet metal. The flat pattern comes out 80.712 mm, and the hand calculation with
    /// the sketch on the INNER faces gives 80.712 — zero deviation; with the
    /// outer faces 76.712. Bent and flattened volume equal. **The convention is
    /// measured, not read**: the points given here are the inner faces, and the
    /// thickness goes outside.
    ///
    /// ── THREE TRAPS, all without an error ─────────────────────────────────
    ///
    ///  1. `SetBendState(flattened)` answers 0 and `GetBendState` says
    ///     «flattened», and the body stays BENT. The flat pattern is read by removing
    ///     the suppression from the «FlatPattern» feature at the bottom of the tree, and
    ///     putting it back.
    ///  2. The export of the flat pattern on a part NEVER SAVED: the obsolete
    ///     `ExportFlatPatternView` (used until Z-C21.7) opened "Save As" and the
    ///     COM call did not return. `ExportToDWG2`, used since then, takes the
    ///     path of the part as ModelName, and on an unsaved part it was not
    ///     measured (the Z-C21.3 probe left that case to a run with someone at
    ///     the screen). `flat_pattern_dxf` still refuses a part without a file,
    ///     before calling.
    ///  3. The sketch is drawn with `AddToDB` true: the relations that
    ///     SOLIDWORKS guesses by itself overconstrain at the first modification
    ///     (measured with `Checks/FollowTheEdge`).
    /// </summary>
    public partial class SwOperations
    {
        private Outcome BaseFlange(IModelDoc2 model, Dictionary<string, object> a)
        {
            string error;
            double thickness, radius, depth;
            if (!RequiredNum(a, "thickness_mm", out thickness, out error)) return Failed(error);
            if (!RequiredNum(a, "radius_mm", out radius, out error)) return Failed(error);
            if (!RequiredNum(a, "depth_mm", out depth, out error)) return Failed(error);
            if (thickness <= 0 || depth <= 0)
                return Failed("«thickness_mm» and «depth_mm» must be positive.");
            if (radius < 0)
                return Failed("«radius_mm» cannot be negative: it is the INNER radius of the bend.");

            var points = ProfilePoints(a, out error);
            if (points == null) return Failed(error);

            // A STRETCH SHORTER THAN THE RADIUS DOES NOT BEND: the bend eats R from
            // every inner side. The limit is declared before calling, because
            // afterwards SOLIDWORKS would give a red feature without saying which side.
            for (int i = 0; i < points.Count - 1; i++)
            {
                double l = Math.Sqrt(Math.Pow(points[i + 1][0] - points[i][0], 2) +
                                     Math.Pow(points[i + 1][1] - points[i][1], 2));
                if (l <= 0.001)
                    return Failed("Segment " + (i + 1) + " has zero length: two equal points in a row.");
                bool has2Bends = i > 0 && i < points.Count - 2;
                double serve = has2Bends ? 2 * radius : radius;
                if (points.Count > 2 && l <= serve)
                    return Failed(string.Format(CultureInfo.InvariantCulture,
                        "Segment {0} is {1:0.###} mm long and the bends with radius {2:0.###} " +
                        "use up {3:0.###}: nothing straight is left. Lengthen the segment or " +
                        "tighten the radius.", i + 1, l, radius, serve));
            }

            string plane = Str(a, "plane", "front");
            if (!SelectPlane(model, plane))
                return Failed("I cannot select the plane «" + plane + "».");

            double? volumeBefore = VolumeMm3(model);
            model.SketchManager.InsertSketch(true);
            bool addBefore = model.SketchManager.AddToDB;
            try
            {
                model.SketchManager.AddToDB = true;
                for (int i = 0; i < points.Count - 1; i++)
                    model.SketchManager.CreateLine(
                        Units.M(points[i][0]), Units.M(points[i][1]), 0,
                        Units.M(points[i + 1][0]), Units.M(points[i + 1][1]), 0);
            }
            finally { model.SketchManager.AddToDB = addBefore; }
            model.SketchManager.InsertSketch(true);

            IFeature flange;
            try
            {
                flange = model.FeatureManager.InsertSheetMetalBaseFlange2(
                    Units.M(thickness), false, Units.M(radius), Units.M(depth), 0.0, false,
                    (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind, 0,
                    null, true, (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular,
                    0.0, 0.0, 0.5, true, false, false, false) as IFeature;
            }
            catch (Exception ex) { return Failed("The base flange was not created: " + ex.Message + LeftSketchBehind); }
            if (flange == null)
                return Failed("The base flange was not created, and SOLIDWORKS does not say why." + LeftSketchBehind);
            model.EditRebuild3();

            // ── BORN IN ERROR? (entry Z-C21.2, finding L10-4) ─────────────
            //
            // IFeature~GetErrorCode2.html: «This method returns many of the
            // errors indicated by the What's Wrong icon seen with an invalid
            // feature in the FeatureManager design tree». The three sisters
            // (edge_flange, hem, sketched_bend) read it, this one did not:
            // the re-read of T and R below comes from the «SheetMetal» feature,
            // which exists even with the flange red, and proves nothing about the flange.
            var flangeError = ApiHelpers.RebuildError(flange);   // S11: GetErrorCode2, warnings excluded
            if (flangeError != null)
                return Failed("The base flange «" + flange.Name + "» was created in error (" +
                               flangeError + "): withdraw it — " + Sentences.HowToWithdrawIt + ".");

            // ── T, R, K RE-READ from the sheet metal, not repeated from the arguments ──
            var data = SheetMetalData(model);
            if (data == null)
                return Failed("The flange is there but I cannot find the feature «SheetMetal»: I do not know " +
                               "with what thickness and K factor it was created. Withdraw it — " + Sentences.HowToWithdrawIt + ".");
            double t = Units.Mm(data.Thickness), r = Units.Mm(data.BendRadius), k = data.KFactor;
            if (Math.Abs(t - thickness) > 0.001 || Math.Abs(r - radius) > 0.001)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "I asked for thickness {0} and radius {1}, and the sheet metal says {2:0.###} and {3:0.###}. " +
                    "Withdraw it — {4}.", thickness, radius, t, r, Sentences.HowToWithdrawIt));

            double? bentVolume = VolumeMm3(model);
            double flatPattern = FlattenedLength(model, out double flattenedVolume, out string flatPatternProblem);

            // THE HAND CALCULATION, in the two conventions. Measured: with the L given
            // from top to right the thickness goes to the RIGHT of the direction
            // of travel and the points are the inner faces. Given the other way round,
            // the thickness goes the other way and the points become the OUTER
            // faces — the flat pattern changes by 2T per bend. It is not guessed: both are
            // calculated and it is said which one matches SOLIDWORKS.
            double sum = 0;
            for (int i = 0; i < points.Count - 1; i++)
                sum += Math.Sqrt(Math.Pow(points[i + 1][0] - points[i][0], 2) +
                                   Math.Pow(points[i + 1][1] - points[i][1], 2));
            int bends = points.Count - 2;
            double angles = 0;
            for (int i = 1; i < points.Count - 1; i++) angles += BendAngle(points[i - 1], points[i], points[i + 1]);
            double inner = sum - 2 * r * bends + angles * (r + k * t);
            double outer = sum - 2 * (r + t) * bends + angles * (r + k * t);
            string convention = double.IsNaN(flatPattern) ? "not measured"
                : Math.Abs(flatPattern - inner) <= 0.5 ? "inner"
                : Math.Abs(flatPattern - outer) <= 0.5 ? "outer" : "neither of the two";
            double expected = convention == "outer" ? outer : inner;

            // ── AND DID IT ADD MATERIAL? (finding L10-4, like hem and edge
            // flange). The volume of a bent sheet is the flat pattern times the
            // thickness times the depth, give or take where the neutral
            // fibre is taken: half of the shorter calculation of the two conventions is a
            // threshold that no real flange misses, and an empty flange does.
            // Before, this number was only reported.
            double? addedFlange = bentVolume - volumeBefore;
            double flangeThreshold = 0.5 * Math.Min(inner, outer) * t * depth;
            if (addedFlange.HasValue && addedFlange.Value < flangeThreshold)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "The base flange «{0}» is in the tree but it added {1:0.##} mm3, less than half of the hand " +
                    "calculation ({2:0.##} mm3 of flat length times thickness times depth): it is not the sheet " +
                    "metal asked for. Withdraw it — {3}.",
                    flange.Name, addedFlange.Value, 2 * flangeThreshold, Sentences.HowToWithdrawIt));

            var measurements = new Dictionary<string, object>
            {
                ["thickness_mm"] = Math.Round(t, 3),
                ["radius_mm"] = Math.Round(r, 3),
                ["k_factor"] = Math.Round(k, 4),
                ["bends"] = bends,
                ["volume_mm3"] = NumberOrNull(bentVolume - volumeBefore, 2),
                ["flat_pattern_length_mm"] = double.IsNaN(flatPattern) ? null : (object)Math.Round(flatPattern, 3),
                ["flat_pattern_length_by_hand_mm"] = Math.Round(expected, 3),
                ["points_are_faces"] = convention,
            };
            // Z-C21.8, finding M24: the flat pattern that SetSuppression2 did not
            // unsuppress, or did not suppress again, is said in the answer.
            if (flatPatternProblem != null) measurements["flat_pattern_problem"] = flatPatternProblem;

            // THE CHECK THAT GIVES THE FAMILY ITS MEANING: the SOLIDWORKS flat pattern
            // against the hand calculation. If they diverge, the DXF that will go to the laser
            // is not what one expects, and it is said now.
            if (convention == "neither of the two")
                measurements["warning"] = string.Format(CultureInfo.InvariantCulture,
                    "The flat length of SOLIDWORKS is {0:0.###} mm and the hand calculation gives " +
                    "{1:0.###}: before sending the DXF to cutting, check the bend table of " +
                    "the document template.", flatPattern, expected);
            if (!double.IsNaN(flattenedVolume) && bentVolume.HasValue &&
                Math.Abs(flattenedVolume - bentVolume.Value) > 0.005 * bentVolume.Value)
                measurements["warning_volume"] = "The flattened volume is not the bent one: the flat pattern is not reliable.";

            return Succeeded(flange, measurements);
        }

        /// <summary>
        /// THE EDGE FLANGE (entry GP18): a new bend on a border edge of a
        /// sheet metal that already exists. It serves boxes — bends on
        /// edges of different directions, which `base_flange` from a single profile does not make.
        ///
        /// ── HOW IT IS MADE (Z-C21.7, L10-1) ─────────────────────────────────
        ///
        /// The guide's recipe: InsertSketchForEdgeFlange, the profile in that
        /// sketch (<see cref="FlangeProfile"/>), CreateDefinition(swFmEdgeFlange),
        /// AddEdges, OffsetDistance = the length at the OUTER virtual sharp,
        /// CreateFeature. The Z-C21.3 probe probe_edge_flange_definition measured
        /// it (case E1) giving the same flange as the obsolete
        /// InsertSheetMetalEdgeFlange2 used until then (E0): +1736.995 mm3,
        /// OffsetDistance 25, flat 80.712 -> 102.425 on the L of the bench case.
        ///
        /// The obsolete call had been measured on 25-26/9/2026
        /// (`Checks/EdgeFlange`, ten attempts): it was born and added
        /// material only with a RECTANGLE on the plane of the flange (the head
        /// face), L - T high, and there the profile governed the length. With
        /// the new recipe OffsetDistance governs it (E3: offset 35 gives
        /// +2536.997 mm3 on the same profile), and the extra volume is still the
        /// extra flat length, (L - R - T) + BA - (R + T), times T times the
        /// length of the edge.
        /// </summary>
        private Outcome EdgeFlange(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string error;
            double length;
            if (!RequiredNum(a, "length_mm", out length, out error)) return Failed(error);

            var data = SheetMetalData(model);
            if (data == null)
                return Failed("This part is not sheet metal: the edge flange attaches to a sheet metal " +
                               "that is already there (made with «base_flange»).");
            double t = Units.Mm(data.Thickness), r = Units.Mm(data.BendRadius), k = data.KFactor;

            // NOTHING STRAIGHT: the bend consumes R + T from the length measured at the
            // outer edge. It is said before touching the part.
            if (length <= r + t + 0.001)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "A flange {0:0.###} mm long on the outer edge has nothing straight: the " +
                    "bend with radius {1:0.###} and thickness {2:0.###} use up {3:0.###}. Ask " +
                    "for it longer than {3:0.###} mm.", length, r, t, r + t));

            string uid = Str(a, "edge_uid", "");
            var border = BorderEdge(a, docState, uid, t, "a flange", out error);
            if (border == null) return Failed(error);
            var edge = border.Edge;
            var n = border.Normal;
            double along = border.LengthM;
            double h = Units.M(length - t);

            double? volumeBefore = VolumeMm3(model);

            // THE RECIPE OF THE GUIDE (Z-C21.7, L10-1), not the obsolete
            // InsertSheetMetalEdgeFlange2 («Obsolete. Superseded by
            // IFeatureManager::CreateDefinition and IFeatureManager::CreateFeature»).
            // IModelDoc2~InsertSketchForEdgeFlange.html: «After calling this
            // method, you must create the profile for the flange on the
            // appropriate plane. Then use IFeatureManager::CreateDefinition,
            // IEdgeFlangeFeatureData, and IFeatureManager::CreateFeature»;
            // IEdgeFlangeFeatureData~AddEdges.html: «Before calling this method,
            // call IModelDoc2::InsertSketchForEdgeFlange to associate the
            // sketches with the edges». The steps are those of
            // Create_Edge_Flange_Example_CSharp.htm with its obsolete sketch
            // helpers replaced (SketchUseEdge3, SketchManager.ActiveSketch,
            // SketchManager.CreateLine), exactly the case E1 of the Z-C21.3 probe
            // probe_edge_flange_definition, on the L of the bench case
            // (T 2, R 2, depth 40, L 25): born without error, OffsetDistance 25,
            // volume +1736.995 mm3, flat 80.712 -> 102.425, the same numbers as
            // InsertSheetMetalEdgeFlange2 on the same edge (E0). The profile is
            // L - T high as before, although the probe found the height does not
            // matter (E2, L high: the same); the LENGTH is OffsetDistance (E3,
            // offset 35: +2536.997 mm3), no longer the profile as with the
            // obsolete call. A pre-selected edge changes nothing (E4).
            model.ClearSelection2(true);
            IFeature sketch;
            try { sketch = model.InsertSketchForEdgeFlange(edge, Math.PI / 2, false) as IFeature; }
            catch (Exception ex) { return Failed("The sketch of the edge flange was not created: " + ex.Message + WithoutSketch(model)); }
            if (sketch == null)
                return Failed("The sketch of the edge flange was not created (InsertSketchForEdgeFlange returned null)." + WithoutSketch(model));

            ISketch sk = null;
            string profileProblem = null;
            try
            {
                sketch.Select2(false, 0);
                model.EditSketch();
                sk = model.SketchManager.ActiveSketch as ISketch;
                profileProblem = FlangeProfile(model, sk, edge, h, n);
            }
            catch (Exception ex) { profileProblem = "the profile of the edge flange was not drawn: " + ex.Message; }
            finally
            {
                // The sketch is closed on every path: a sketch left in edit mode
                // would take the next operation's geometry.
                try { if (model.SketchManager.ActiveSketch != null) model.SketchManager.InsertSketch(true); } catch { }
            }
            if (profileProblem != null)
                return Failed("The edge flange was not made: " + profileProblem + "." + WithoutSketch(model));

            model.ClearSelection2(true);
            IFeature flange;
            try
            {
                var creation = ApiHelpers.CreateFeatureFromDefinition<IEdgeFlangeFeatureData>(
                    model.FeatureManager, (int)swFeatureNameID_e.swFmEdgeFlange, flangeData =>
                    {
                        int code = flangeData.AddEdges(new Edge[] { (Edge)edge }, new Sketch[] { (Sketch)sk });
                        if (code != (int)swEdgeFlangeError_e.swEdgeFlangeError_NoError) return false;
                        flangeData.UseDefaultBendRadius = true;
                        flangeData.BendAngle = Math.PI / 2;
                        flangeData.OffsetType = (int)swFlangeOffsetTypes_e.swFlangeOffsetBlind;
                        flangeData.OffsetDistance = Units.M(length);
                        flangeData.OffsetDimType = (int)swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp;
                        flangeData.PositionType = (int)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside;
                        flangeData.UseDefaultBendRelief = true;
                        flangeData.UseDefaultBendAllowance = true;
                        return true;
                    });
                flange = creation.Feature;
                if (flange == null)
                    return Failed("The edge flange was not made: " +
                                   (creation.Stage == ApiHelpers.CreationStage.Fill ? "AddEdges refused the edge and its sketch"
                                    : creation.Stage == ApiHelpers.CreationStage.Creation ? "CreateFeature returned null, and SOLIDWORKS does not say why"
                                    : "CreateDefinition(swFmEdgeFlange) returned nothing") +
                                   "." + WithoutSketch(model));
            }
            catch (Exception ex) { return Failed("The edge flange was not created: " + ex.Message + WithoutSketch(model)); }
            finally { try { model.ClearSelection2(true); } catch { } }
            model.EditRebuild3();

            var flangeError = ApiHelpers.RebuildError(flange);   // S11: GetErrorCode2, warnings excluded
            if (flangeError != null)
                return Failed("The edge flange «" + flange.Name + "» was created in error (" + flangeError +
                               "): withdraw it — " + Sentences.HowToWithdrawIt + ".");

            double read = -1;
            try { var def = flange.GetDefinition() as IEdgeFlangeFeatureData; if (def != null) read = Units.Mm(def.OffsetDistance); }
            catch { }
            double? volumeAfter = VolumeMm3(model);
            double ba = Math.PI / 2 * (r + k * t);
            double extraFlatPattern = (length - r - t) + ba - (r + t);
            double expectedVolume = extraFlatPattern * t * Units.Mm(along);
            double? added = volumeAfter - volumeBefore;

            // BORN EMPTY: it is attempt 9 of the probe — a «Edge-Flange»
            // in the tree that adds nothing. A part equal to before is not
            // confirmed.
            if (added.HasValue && added.Value < 0.5 * expectedVolume)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "The flange «{0}» is in the tree but it added {1:0.##} mm3 instead of the {2:0.##} of the calculation: " +
                    "it is not the flange asked for. Withdraw it — {3}.",
                    flange.Name, added.Value, expectedVolume, Sentences.HowToWithdrawIt));

            double flatPattern = FlattenedLength(model, out double _, out string flatPatternProblem);
            var measurements = new Dictionary<string, object>
            {
                ["length_mm"] = read >= 0 ? (object)Math.Round(read, 3) : null,
                ["thickness_mm"] = Math.Round(t, 3),
                ["radius_mm"] = Math.Round(r, 3),
                ["k_factor"] = Math.Round(k, 4),
                ["along_edge_mm"] = Math.Round(Units.Mm(along), 3),
                ["volume_mm3"] = NumberOrNull(added, 2),
                ["volume_by_hand_mm3"] = Math.Round(expectedVolume, 2),
                ["flat_pattern_length_mm"] = double.IsNaN(flatPattern) ? null : (object)Math.Round(flatPattern, 3),
            };
            if (!volumeAfter.HasValue || !volumeBefore.HasValue) measurements["volume_not_reread"] = NoteVolumeNotReread;
            else if (Math.Abs(added.Value - expectedVolume) > 0.005 * expectedVolume)
                measurements["warning"] = string.Format(CultureInfo.InvariantCulture,
                    "The flange added {0:0.##} mm3 and the hand calculation gives {1:0.##}: check the flat " +
                    "pattern before cutting.", added.Value, expectedVolume);
            if (read >= 0 && Math.Abs(read - length) > 0.01)
                measurements["warning_length"] = string.Format(CultureInfo.InvariantCulture,
                    "I asked for {0:0.###} mm on the outer edge and the flange says {1:0.###}.", length, read);
            if (flatPatternProblem != null) measurements["flat_pattern_problem"] = flatPatternProblem;   // M24
            return Succeeded(flange, measurements);
        }

        /// <summary>
        /// The profile of an edge flange, drawn in the sketch that
        /// InsertSketchForEdgeFlange made (open for editing): the edge converted
        /// into the sketch, then three lines <paramref name="heightM"/> up in the
        /// sketch's +Y, as Create_Edge_Flange_Example_CSharp.htm does and as the
        /// case E1 of the Z-C21.3 probe probe_edge_flange_definition measured.
        /// Null when drawn, otherwise what went wrong.
        ///
        /// THE SIDE IS CHECKED BEFORE DRAWING. The catalogue promises that the
        /// flange rises on the side the broad face looks toward, which the
        /// obsolete call got by construction (the rectangle was drawn along that
        /// normal). Here the side is the sketch's +Y, which SOLIDWORKS chooses:
        /// on the bench edge the probe found it along the broad face's normal
        /// ((0, 1, 0), corners (50, 23, 40) and (50, 23, 0)). A flange on the
        /// other side adds the same volume (probe E5, FlipDir true: +1736.995
        /// mm3), so no later check would see it: a profile that would go the
        /// other way is refused instead of built.
        /// </summary>
        private string FlangeProfile(IModelDoc2 model, ISketch sk, IEdge edge, double heightM, double[] normal)
        {
            if (sk == null) return "the sketch made by InsertSketchForEdgeFlange did not open for editing";
            if (!((IEntity)edge).Select4(false, null)) return "the edge could not be selected inside the flange sketch";
            if (!model.SketchManager.SketchUseEdge3(false, false)) return "SketchUseEdge3 did not convert the edge into the flange sketch";
            ISketchLine line = null;
            foreach (var o in (sk.GetSketchSegments() as object[]) ?? new object[0]) { line = o as ISketchLine; if (line != null) break; }
            var start = line?.GetStartPoint2() as ISketchPoint;
            var end = line?.GetEndPoint2() as ISketchPoint;
            if (start == null || end == null) return "the converted edge is not in the flange sketch";

            var math = _sw.GetMathUtility() as IMathUtility;
            var toModel = sk.ModelToSketchTransform?.Inverse() as MathTransform;
            if (math == null || toModel == null) return "the flange sketch cannot be read back in model coordinates";
            Func<double, double, double[]> inModel = (x, y) =>
                ((IMathPoint)((IMathPoint)math.CreatePoint(new[] { x, y, 0.0 })).MultiplyTransform(toModel)).ArrayData as double[];
            var foot = inModel(start.X, start.Y);
            var top = inModel(start.X, start.Y + heightM);
            if (foot == null || top == null || normal == null) return "the flange sketch cannot be read back in model coordinates";
            double along = (top[0] - foot[0]) * normal[0] + (top[1] - foot[1]) * normal[1] + (top[2] - foot[2]) * normal[2];
            if (along < 0.5 * heightM)
                return string.Format(CultureInfo.InvariantCulture,
                    "the sketch SOLIDWORKS made for the flange points {0:0.###} mm along the outward normal of the broad " +
                    "face instead of {1:0.###}: the flange would not rise on the side the broad face looks toward",
                    Units.Mm(along), Units.Mm(heightM));

            bool addToDbBefore = model.SketchManager.AddToDB;
            try
            {
                model.SketchManager.AddToDB = true;
                model.SketchManager.CreateLine(start.X, start.Y, 0, start.X, start.Y + heightM, 0);
                model.SketchManager.CreateLine(start.X, start.Y + heightM, 0, end.X, end.Y + heightM, 0);
                model.SketchManager.CreateLine(end.X, end.Y + heightM, 0, end.X, end.Y, 0);
            }
            finally { model.SketchManager.AddToDB = addToDbBefore; }
            return null;
        }

        /// <summary>A border edge of a sheet metal, with its two faces.</summary>
        private sealed class SheetMetalEdge
        {
            public IEdge Edge;
            public IFace2 Head, Wide;
            public double[] P0, P1;
            /// <summary>
            /// The OUTWARD normal of the broad face, unit length. Outward by
            /// definition, not by luck: IFace2~FaceInSurfaceSense.html, Remarks,
            /// «The normal vector of faces, however, are directed away from the
            /// body material» (audit finding L10-7, not real). The flag on that
            /// page is about the underlying surface, and is not needed here.
            /// </summary>
            public double[] Normal;
            public double LengthM;
        }

        /// <summary>
        /// The border edge on which it bends (edge flange, hem): taken
        /// by signature (entry E19), straight, between the WIDE face of the sheet metal and a
        /// flat END face, as wide as the thickness (area = T x length).
        /// Null with the reason, which says which edge is needed.
        ///
        /// THE SIDE OF A SHEET METAL IS NOT A SINGLE FACE (`probe0089 ell`,
        /// 26/9/2026): it is broken into stretches, and every stretch is exactly as wide as the
        /// thickness. So the edges along the sides are border edges too, and they
        /// really are; the tangent edge of a bend is not, where one
        /// face is the cylinder of the bend.
        /// </summary>
        private SheetMetalEdge BorderEdge(
            Dictionary<string, object> a, DocumentState docState, string uid, double t,
            string what, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(uid))
            {
                reason = "«edge_uid» is missing: the border edge of the sheet metal to bend on, " +
                         "taken from the state of the document (E1, E2...).";
                return null;
            }
            var signature = EdgeSignature(
                a, docState, uid, "The edge «" + uid + "» is no longer in this document.", out reason);
            if (signature == null) return null;
            var b = new SheetMetalEdge { Edge = signature.Entity as IEdge };

            bool straight = false;
            try
            {
                var par = ApiHelpers.EdgeParameters(b.Edge, out ICurve curva);
                straight = curva != null && curva.IsLine();
                b.P0 = par?.StartPoint as double[];
                b.P1 = par?.EndPoint as double[];
            }
            catch { }
            if (!straight || b.P0 == null || b.P1 == null)
            {
                reason = "The edge «" + uid + "» is not straight: " + what + " is bent on a straight edge.";
                return null;
            }
            b.LengthM = Math.Sqrt(Math.Pow(b.P1[0] - b.P0[0], 2) + Math.Pow(b.P1[1] - b.P0[1], 2) +
                                 Math.Pow(b.P1[2] - b.P0[2], 2));
            try
            {
                var faces = b.Edge.GetTwoAdjacentFaces2() as object[];
                if (faces != null && faces.Length == 2)
                {
                    double headArea = Units.M(t) * b.LengthM;
                    foreach (var o in faces)
                    {
                        var f = o as IFace2;
                        if (f == null || (f.GetSurface() as ISurface)?.IsPlane() != true) continue;
                        if (Math.Abs(f.GetArea() - headArea) <= 0.01 * headArea) b.Head = f;
                        else b.Wide = f;
                    }
                }
            }
            catch { }
            if (b.Head == null || b.Wide == null)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "The edge «{0}» is not on the border of the sheet: {1} attaches to an edge " +
                    "between the wide face and an end face as wide as the thickness ({2:0.###} mm), " +
                    "both planar. Choose an edge of the border.", uid, what, t);
                return null;
            }
            var n = ApiHelpers.NormalIfPlanar(b.Wide);
            // -1 is «not read» (entry Z-C8), and it is refused right below.
            double nn = n == null || n.Length < 3 ? -1 : Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
            if (nn <= 1e-9)
            {
                reason = "I cannot read which way the wide face of the edge «" + uid + "» faces.";
                return null;
            }
            b.Normal = new[] { n[0] / nn, n[1] / nn, n[2] / nn };
            return b;
        }

        /// <summary>
        /// THE HEM (entry GP18): the edge folded back on itself, closed or open.
        ///
        /// ── MEASURED BEFORE WRITING IT, 26/9/2026 (`probe0089 hem`) ────────
        ///
        /// `InsertSheetMetalHem2` works on the SELECTED edge and is born at the
        /// first try — no sketch, unlike the edge flange.
        /// On the L (T 2, K 0.5), «inside»:
        ///
        ///   open, gap 1, length 10:  +9.712 of flat pattern
        ///   open, gap 2, length 10:  +10.283
        ///   open, gap 1, length 15:  +14.712
        ///   closed, length 10 / 15:  +9.199 / +14.199
        ///
        /// THE OPEN ONE is the formula of the flange with the bend at 180 degrees and the
        /// inner radius equal to half the gap: (L - R - T) + pi (R + K T) -
        /// (R + T), R = G/2 — three measures out of three, to the thousandth. The CLOSED one is
        /// 0.057 above the same formula with R = 0: like an inner radius of 0.05
        /// mm, measured with T 2 only. That is why the calculation for the closed one is stated
        /// next to the measure, and decides nothing.
        /// </summary>
        private Outcome Hem(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string error;
            double length;
            if (!RequiredNum(a, "length_mm", out length, out error)) return Failed(error);
            string typ = Str(a, "variant", "closed");
            if (typ != "closed" && typ != "open")
                return Failed("«variant» is «closed» or «open», not «" + typ + "».");
            double gap = Num(a, "gap_mm");
            if (typ == "open" && gap <= 0)
                return Failed("An open hem wants «gap_mm» greater than zero: it is the space between the " +
                               "folded border and the sheet. Without a gap it is a «closed» hem.");

            var data = SheetMetalData(model);
            if (data == null)
                return Failed("This part is not sheet metal: the hem is made on the border of a sheet metal that is " +
                               "already there (made with «base_flange»).");
            double t = Units.Mm(data.Thickness), k = data.KFactor;
            double r = typ == "open" ? gap / 2 : 0.05;
            if (length <= r + t + 0.001)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "A hem {0:0.###} mm long has nothing straight: the 180 degree bend and the thickness " +
                    "use up {1:0.###}. Ask for it longer than {1:0.###} mm.", length, r + t));

            string uid = Str(a, "edge_uid", "");
            var border = BorderEdge(a, docState, uid, t, "a hem", out error);
            if (border == null) return Failed(error);

            double? volumeBefore = VolumeMm3(model);
            model.ClearSelection2(true);
            if (!(border.Edge as IEntity).Select4(false, null))
                return Failed("I could not select the edge «" + uid + "».");
            IFeature hem;
            try
            {
                hem = model.FeatureManager.InsertSheetMetalHem2(
                    typ == "closed" ? (int)swHemTypes_e.swHemTypeClosed : (int)swHemTypes_e.swHemTypeOpen,
                    (int)swHemPositionTypes_e.swHemPositionTypeInside, false,
                    Units.M(length), typ == "open" ? Units.M(gap) : 0, 0, 0, 0,
                    null, true, (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0, true, 0.5, 0, 0) as IFeature;
            }
            catch (Exception ex) { return Failed("The hem was not created: " + ex.Message); }
            if (hem == null) return Failed("The hem was not created, and SOLIDWORKS does not say why.");
            model.EditRebuild3();

            var hemError = ApiHelpers.RebuildError(hem);   // S11: GetErrorCode2, warnings excluded
            if (hemError != null)
                return Failed("The hem «" + hem.Name + "» was created in error (" + hemError + "): withdraw it — " +
                               Sentences.HowToWithdrawIt + ".");

            double? volumeAfter = VolumeMm3(model);
            double extraFlatPattern = (length - r - t) + Math.PI * (r + k * t) - (r + t);
            double expectedVolume = extraFlatPattern * t * Units.Mm(border.LengthM);
            double? added = volumeAfter - volumeBefore;
            if (added.HasValue && added.Value < 0.5 * expectedVolume)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "The hem «{0}» is in the tree but it added {1:0.##} mm3 instead of the {2:0.##} of the calculation: it " +
                    "is not the hem asked for. Withdraw it — {3}.", hem.Name, added.Value, expectedVolume, Sentences.HowToWithdrawIt));

            double flatPattern = FlattenedLength(model, out double _, out string flatPatternProblem);
            var measurements = new Dictionary<string, object>
            {
                ["kind"] = typ,
                ["length_mm"] = Math.Round(length, 3),
                ["thickness_mm"] = Math.Round(t, 3),
                ["k_factor"] = Math.Round(k, 4),
                ["along_edge_mm"] = Math.Round(Units.Mm(border.LengthM), 3),
                ["volume_mm3"] = NumberOrNull(added, 2),
                ["volume_by_hand_mm3"] = Math.Round(expectedVolume, 2),
                ["flat_pattern_length_mm"] = double.IsNaN(flatPattern) ? null : (object)Math.Round(flatPattern, 3),
            };
            if (typ == "open") measurements["gap_mm"] = Math.Round(gap, 3);
            if (!volumeAfter.HasValue || !volumeBefore.HasValue) measurements["volume_not_reread"] = NoteVolumeNotReread;
            else if (Math.Abs(added.Value - expectedVolume) > 0.005 * expectedVolume)
                measurements["warning"] = string.Format(CultureInfo.InvariantCulture,
                    "The hem added {0:0.##} mm3 and the hand calculation gives {1:0.##}" +
                    (typ == "closed" ? " " +
                                        "(for the closed hem the calculation uses an inner radius of 0.05 mm, measured with thickness 2)" : "") + ": check the flat pattern before cutting.",
                    added.Value, expectedVolume);
            if (flatPatternProblem != null) measurements["flat_pattern_problem"] = flatPatternProblem;   // M24
            return Succeeded(hem, measurements);
        }

        /// <summary>
        /// THE SKETCHED BEND (entry GP18): the sheet metal bends along a line
        /// drawn on one of its faces.
        ///
        /// ── MEASURED BEFORE WRITING IT, 26/9/2026 (`probe0089 bend`) ────────
        ///
        /// `InsertSheetMetal3dBend(Angle, UseDefaultRadius, Radius, FlipDir,
        /// BendPos, PCBA)` with the SKETCH of the line selected is born at the first
        /// try; the fixed face is not needed (with or without, same part). On a
        /// flat sheet 100 x 40 x 2 (R 2, K 0.5), line across at x = 50
        /// on the top face, 90 degrees, «material inside»: volume unchanged
        /// (8000), bounding box 50 x 53.288 x 40 — 46 of flat (50 - R - T), the bend
        /// of 4.712, and 49.288 of side rising from y = 2. The flat pattern does not change:
        /// a bend does not add sheet, it moves it.
        ///
        /// The sheet bends toward the side that the face the line lies on
        /// LOOKS to: to bend the other way the line is drawn on the other
        /// face. The side that stays still is chosen by SOLIDWORKS: the answer
        /// carries the bounding box before and after, which says it.
        ///
        /// THE JOG no: `IModelDoc2.InsertSheetMetalJog` (Void) gave birth to
        /// nothing in seven variants of selection, and among the feature
        /// definitions there is no jog. It is in the TODO, entry GP18.
        /// </summary>
        private Outcome SketchedBend(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var data = SheetMetalData(model);
            if (data == null)
                return Failed("This part is not sheet metal: the sketched bend bends a sheet metal that is " +
                               "already there (made with «base_flange»).");
            double t = Units.Mm(data.Thickness), r = Units.Mm(data.BendRadius);
            double angle = Num(a, "angle_deg", 90);
            if (angle <= 0 || angle >= 180)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "«angle_deg» must be between 0 and 180, both excluded: {0:0.##} is not a bend. At 180 the sheet " +
                    "metal would fold back on itself — for that there is «hem».", angle));
            var da = Triplet(a, "start_mm");
            var upTo = Triplet(a, "end_mm");
            if (da == null || upTo == null)
                return Failed("«start_mm» and «end_mm» are the two ends of the bend line, points [x, y, z] in mm on " +
                               "the face (from the state of the document).");

            string uidFace = Str(a, "face_uid", "");
            var face = ResolveFace(docState, a, uidFace, out string reason);
            if (face == null) return Failed(reason ?? "No face indicated.");
            double[] plane = null;
            try
            {
                var sup = face.GetSurface() as ISurface;
                if (sup != null && sup.IsPlane()) plane = sup.PlaneParams as double[];
            }
            catch { }
            if (plane == null || plane.Length < 6)
                return Failed("The face «" + uidFace + "» is not planar: the bend line is drawn on a planar face.");
            // THE TWO POINTS LIE ON THE FACE: distance from its plane within 5 hundredths.
            Func<double[], double> fromPlane = p =>
                Math.Abs((Units.M(p[0]) - plane[3]) * plane[0] + (Units.M(p[1]) - plane[4]) * plane[1] +
                         (Units.M(p[2]) - plane[5]) * plane[2]) * 1000.0;
            double deviation = Math.Max(fromPlane(da), fromPlane(upTo));
            if (deviation > 0.05)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "The bend line is not on the face «{0}»: one end is {1:0.###} mm away from it. The two points must " +
                    "be taken on the plane of the face.", uidFace, deviation));
            double longLine = Math.Sqrt(Math.Pow(upTo[0] - da[0], 2) + Math.Pow(upTo[1] - da[1], 2) + Math.Pow(upTo[2] - da[2], 2));
            if (longLine < 1)
                return Failed("The bend line is shorter than a millimeter: the two points coincide.");

            double? volumeBefore = VolumeMm3(model);
            double flatPatternBefore = FlattenedLength(model, out double _, out string flatProblemBefore);
            // Z-C21.8, finding M24: a flat pattern that did not toggle as asked
            // stops the step before the bend, the only thing touched so far.
            if (flatProblemBefore != null)
                return Failed("Before bending, I measured the flat length and " + flatProblemBefore +
                               ". I did not make the bend.");
            var boxBefore = PartBoxMm(model);
            // The names of the tree before the sketch (Z-C21.5, S7): null when the
            // tree cannot be read, and then no sketch is taken for the new one.
            var sketchesBefore = FeatureNamesNow(model);

            model.ClearSelection2(true);
            if (!(face as IEntity).Select4(false, null))
                return Failed("I cannot select the face «" + uidFace + "».");
            model.SketchManager.InsertSketch(true);
            var sk = model.SketchManager.ActiveSketch as ISketch;
            var math = _sw.GetMathUtility() as IMathUtility;
            if (sk == null || math == null)
                return Failed("The sketch of the bend line did not open." + WithoutSketch(model));
            var tr = sk.ModelToSketchTransform;
            Func<double[], double[]> inSketch = p =>
                ((IMathPoint)((IMathPoint)math.CreatePoint(new[] { Units.M(p[0]), Units.M(p[1]), Units.M(p[2]) }))
                    .MultiplyTransform(tr)).ArrayData as double[];
            var s0 = inSketch(da); var s1 = inSketch(upTo);
            bool addBefore = model.SketchManager.AddToDB;
            try
            {
                model.SketchManager.AddToDB = true;
                model.SketchManager.CreateLine(s0[0], s0[1], 0, s1[0], s1[1], 0);
            }
            finally { model.SketchManager.AddToDB = addBefore; }
            model.SketchManager.InsertSketch(true);
            // As for the edge flange: on sheet metal the last feature is the flat
            // pattern, and the new sketch is the one that was not there before.
            // Z-C21.5 (S7, review of the leftovers): the ONE born, through
            // FeatureBorn. The loop that was here kept the LAST new sketch, so two
            // born would have given one of them without a word.
            int sketchesBorn;
            IFeature sketch = FeatureBorn(model, sketchesBefore, "ProfileFeature", out sketchesBorn);
            if (sketch == null)
                return Failed(SketchNotIdentified(sketchesBorn, "I cannot find the sketch of the bend line I just made.") +
                               WithoutSketch(model));

            model.ClearSelection2(true);
            sketch.Select2(false, 0);
            IFeature bend;
            try
            {
                bend = model.FeatureManager.InsertSheetMetal3dBend(
                    angle * Math.PI / 180.0, true, Units.M(r), false,
                    (short)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside, null) as IFeature;
            }
            catch (Exception ex) { return Failed("The bend was not created: " + ex.Message + WithoutSketch(model)); }
            if (bend == null)
                return Failed("The bend was not created, and SOLIDWORKS does not say why. Usually the line does " +
                               "not cross the face from border to border." + WithoutSketch(model));
            model.EditRebuild3();

            var bendError = ApiHelpers.RebuildError(bend);   // S11: GetErrorCode2, warnings excluded
            if (bendError != null)
                return Failed("The bend «" + bend.Name + "» was created in error (" + bendError + "): withdraw it — " +
                               Sentences.HowToWithdrawIt + ".");

            var boxAfter = PartBoxMm(model);
            bool moved = boxBefore == null || boxAfter == null;
            for (int i = 0; !moved && i < 6; i++) moved = Math.Abs(boxAfter[i] - boxBefore[i]) > 0.01;
            if (!moved)
                return Failed("The bend «" + bend.Name + "» is in the tree but the part has the same bounding box " +
                               "as before: nothing was bent. Withdraw it — " + Sentences.HowToWithdrawIt + ".");

            double? volumeAfter = VolumeMm3(model);
            double flatPatternAfter = FlattenedLength(model, out double _, out string flatPatternProblem);
            Func<double[], object> roundOff = s => s == null ? null : (object)Array.ConvertAll(s, x => Math.Round(x, 3));
            var measurements = new Dictionary<string, object>
            {
                ["angle_deg"] = Math.Round(angle, 3),
                ["radius_mm"] = Math.Round(r, 3),
                ["thickness_mm"] = Math.Round(t, 3),
                ["box_before_mm"] = roundOff(boxBefore),
                ["box_after_mm"] = roundOff(boxAfter),
                ["volume_mm3"] = NumberOrNull(volumeAfter, 2),
                ["flat_pattern_length_mm"] = double.IsNaN(flatPatternAfter) ? null : (object)Math.Round(flatPatternAfter, 3),
            };
            if (flatPatternProblem != null) measurements["flat_pattern_problem"] = flatPatternProblem;   // M24
            // A BEND DOES NOT ADD SHEET: volume and flat pattern stay as they were
            // before. If they change, the part is not what is believed.
            if (!volumeAfter.HasValue || !volumeBefore.HasValue) measurements["volume_not_reread"] = NoteVolumeNotReread;
            else if (Math.Abs(volumeAfter.Value - volumeBefore.Value) > 0.001 * volumeBefore.Value)
                measurements["warning"] = string.Format(CultureInfo.InvariantCulture,
                    "The volume went from {0:0.##} to {1:0.##} mm3: a bend neither adds nor removes sheet metal.",
                    volumeBefore.Value, volumeAfter.Value);
            if (!double.IsNaN(flatPatternBefore) && !double.IsNaN(flatPatternAfter) && Math.Abs(flatPatternAfter - flatPatternBefore) > 0.05)
                measurements["warning_flat_pattern"] = string.Format(CultureInfo.InvariantCulture,
                    "The flat length went from {0:0.###} to {1:0.###} mm: with a sketched bend it should stay the same.",
                    flatPatternBefore, flatPatternAfter);
            return Succeeded(bend, measurements);
        }

        private Outcome FlatPatternDxf(IModelDoc2 model, Dictionary<string, object> a)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return Failed("The flat pattern is made on a part.");
            if (SheetMetalData(model) == null)
                return Failed("This part is not sheet metal: it has no «SheetMetal» feature to flatten.");

            // TRAP 2 of the note at the top: without a file, «Save As» is a
            // call that does not return.
            string its = model.GetPathName() ?? "";
            if (string.IsNullOrWhiteSpace(its))
                return Failed("The part has never been saved, and on a part without a " +
                               "file SOLIDWORKS opens «Save As» and stays waiting. Save it " +
                               "first («save_document»).");

            string location = Str(a, "path", null);
            if (string.IsNullOrWhiteSpace(location)) location = Path.ChangeExtension(its, ".dxf");
            if (!string.Equals(Path.GetExtension(location), ".dxf", StringComparison.OrdinalIgnoreCase))
                return Failed("«path» must end in .dxf.");
            try { if (File.Exists(location)) File.Delete(location); }
            catch (Exception ex) { return Failed("There is already a DXF there and I cannot replace it: " + ex.Message); }

            // Z-C21.7 (L10-2): ExportToDWG2, not the obsolete ExportFlatPatternView
            // (IPartDoc~ExportFlatPatternView.html: «Obsolete. Superseded by
            // IPartDoc::ExportToDWG2»). IPartDoc~ExportToDWG2.html: ModelName is
            // «Path and file name of the active part document», SheetMetalOptions
            // «Bit #1: 1 to export flat-pattern geometry». Exactly case X1 of the
            // Z-C21.3 probe probe_export_dwg2, on the saved bench L: True, a DXF
            // of 4 LINE entities, box 40 x 80.712 mm, the part unchanged, the
            // same box as ExportFlatPatternView (X0). Bend lines (options 5, X3)
            // would not change the box; they are not asked, as before.
            //
            // ModelName names the ACTIVE document, and more than one session can
            // work on the same SOLIDWORKS: OUR document is brought to the front
            // first, checked, and the one that was in front goes back there.
            // Z-C21.13 (pattern S18): both documents are held by REFERENCE and
            // brought to the front by path through DocumentByPath, never by title.
            IModelDoc2 previousFront;
            string activationProblem = BringToFront(model, out previousFront);
            // The one that was in front goes back there, even when bringing ours
            // failed: a failed attempt may have brought another document forward.
            Action putBackTheFront = () =>
            {
                if (previousFront == null) return;
                int activationErrors;
                string problem;
                if (!DocumentByPath.ActivateAndReadBack(_sw, previousFront, out activationErrors, out problem))
                    _log("[agent] flat pattern: " + problem);
                else
                {
                    string stale = DocumentByPath.PutBackNeedsRebuild(previousFront, activationErrors);
                    if (stale != null) _log("[agent] flat pattern: " + stale);
                }
            };
            if (activationProblem != null)
            {
                putBackTheFront();
                return Failed("The flat pattern was not exported: " + activationProblem + ".");
            }
            bool ok;
            try
            {
                ok = partDoc.ExportToDWG2(location, its, (int)swExportToDWG_e.swExportToDWG_ExportSheetMetal,
                                        true, null, false, false, 1, null);
            }
            catch (Exception ex) { return Failed("The flat pattern did not come out: " + ex.Message); }
            finally { putBackTheFront(); }
            if (!ok || !File.Exists(location) || new FileInfo(location).Length == 0)
                return Failed("SOLIDWORKS says it exported the flat pattern, but the file «" + location +
                               "» " + (File.Exists(location) ? "is empty." : "is not there."));

            // THE DXF IS RE-READ: the bounding box written in the file against the flat pattern
            // measured on the part. A DXF of something else opens just the same.
            double flatPattern = FlattenedLength(model, out double _, out string flatPatternProblem);
            var boundingBox = DxfBoundingBox(location);
            var measurements = new Dictionary<string, object>
            {
                ["path"] = location,
                ["byte"] = new FileInfo(location).Length,
                ["bounding_box_dxf_mm"] = boundingBox,
                ["flat_pattern_length_mm"] = double.IsNaN(flatPattern) ? null : (object)Math.Round(flatPattern, 3),
            };
            if (flatPatternProblem != null) measurements["flat_pattern_problem"] = flatPatternProblem;   // M24
            if (boundingBox != null && !double.IsNaN(flatPattern))
            {
                double side = Math.Max(boundingBox[0], boundingBox[1]);
                if (Math.Abs(side - flatPattern) > 0.5)
                    return Failed(string.Format(CultureInfo.InvariantCulture,
                        "The DXF came out ({0}) but its long side is {1:0.###} mm and the flat pattern " +
                        "of the part {2:0.###}: it is not the drawing of this part, or it is not flattened.",
                        location, side, flatPattern));
            }
            return WithoutFeature(measurements);
        }

        /// <summary>
        /// Brings <paramref name="document"/> to the front of SOLIDWORKS for a
        /// call that works on the ACTIVE document (ExportToDWG2). Null when the
        /// active document is ours afterwards, otherwise what went wrong;
        /// <paramref name="previousFront"/> is the document that was in front
        /// before, when it was another one, so that the caller can bring it back.
        ///
        /// Z-C21.13 (pattern S18): through DocumentByPath.ActivateAndReadBack,
        /// which gives ActivateDoc3 the file name with extension, then the full
        /// path, never the title (ISldWorks~ActivateDoc3.html warns that a name
        /// without extension can fit a part and an assembly at once), with
        /// swDontRebuildActiveDoc, and reads back that the active document is
        /// this very object. The page: «This method brings the document
        /// specified in Name to the foreground of SOLIDWORKS and returns a
        /// pointer to the document»; the Z-C21.3 probe of C4-4 measured it
        /// bringing even a hidden part forward. The previous front is kept by
        /// reference: until library 0.143.0 it was its title.
        /// </summary>
        private string BringToFront(IModelDoc2 document, out IModelDoc2 previousFront)
        {
            previousFront = null;
            IModelDoc2 front = null;
            try { front = _sw.ActiveDoc as IModelDoc2; } catch { }
            // Ours already in front leaves nothing to put back (and if the two
            // wrappers differ for one document, putting it back is harmless).
            if (front != null && !ReferenceEquals(front, document)) previousFront = front;
            int activationErrors;
            string problem;
            if (!DocumentByPath.ActivateAndReadBack(_sw, document, out activationErrors, out problem))
                return problem + ", and ExportToDWG2 exports the active document";
            // C4-3: brought here with swDontRebuildActiveDoc, so a pending rebuild
            // is done now, before the flat pattern is written from it.
            string rebuilt = DocumentByPath.RebuildIfActivationAsked(document, activationErrors);
            if (rebuilt != null) _log("[agent] " + rebuilt);
            return null;
        }

        // =================================================================

        /// <summary>
        /// The thickness, the radius and the K factor of the sheet metal of the part, from the
        /// «SheetMetal» feature. Null if the part is not sheet metal.
        /// </summary>
        private static ISheetMetalFeatureData SheetMetalData(IModelDoc2 model)
        {
            try
            {
                var f = model.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (f.GetTypeName2() == "SheetMetal") return f.GetDefinition() as ISheetMetalFeatureData;
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// The long side of the FLATTENED part, in mm, and its volume. NaN if it
        /// cannot be done. The suppression is removed from the «FlatPattern» feature,
        /// it is measured, and it is put back — `SetBendState` does nothing (note at
        /// the top, trap 1).
        ///
        /// <paramref name="flatPatternProblem"/> is null when the flat pattern
        /// was unsuppressed and suppressed again as asked, otherwise the reason.
        ///
        /// THE TWO ANSWERS OF SetSuppression2 ARE READ (Z-C21.8, finding M24).
        /// IFeature~SetSuppression2.html: «True if successful, false if not».
        /// Both were dropped: an unsuppress that did not happen measured the
        /// FOLDED part as the flat one, and a suppress that did not happen left
        /// the part flattened after an answer that said nothing. Now a refused
        /// unsuppress gives NaN and the reason, and a refused suppress at the end
        /// gives the reason too. The other half of M24, the length from the true
        /// box and not from GetBodyBox (S6), is below.
        /// </summary>
        private static double FlattenedLength(IModelDoc2 model, out double volume, out string flatPatternProblem)
        {
            volume = double.NaN;
            flatPatternProblem = null;
            IFeature flatPattern = null;
            try
            {
                var f = model.FirstFeature() as IFeature;
                while (f != null && flatPattern == null)
                {
                    if (f.GetTypeName2() == "FlatPattern") flatPattern = f;
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            if (flatPattern == null) return double.NaN;
            string flatName = "FlatPattern";
            try { flatName = flatPattern.Name; } catch { flatName = "FlatPattern"; }
            // True from the moment the unsuppress may have happened: a call that
            // threw leaves the state unknown, and the finally suppresses again.
            bool mayBeUnsuppressed = false;
            try
            {
                mayBeUnsuppressed = true;
                bool unsuppressed = flatPattern.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
                if (!unsuppressed)
                {
                    mayBeUnsuppressed = false;
                    flatPatternProblem = "SetSuppression2 did not unsuppress the flat pattern «" + flatName +
                                         "», so the flat length was not measured";
                    return double.NaN;
                }
                model.EditRebuild3();
                volume = VolumeMm3(model) ?? double.NaN;   // NaN here already means «I do not know»
                var bodies = (model as IPartDoc)?.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                // S6: the flat length is a measure, so the TRUE box
                // (ApiHelpers.TrueBoundingBox), not GetBodyBox, which the guide
                // says is approximate and not for calculations.
                var box = bodies == null || bodies.Length == 0 ? null : ApiHelpers.TrueBoundingBox(bodies[0] as IBody2);
                if (box == null) return double.NaN;
                return Math.Max(Math.Abs(box[3] - box[0]), Math.Max(Math.Abs(box[4] - box[1]), Math.Abs(box[5] - box[2]))) * 1000.0;
            }
            catch { return double.NaN; }
            finally
            {
                if (mayBeUnsuppressed)
                {
                    bool suppressedAgain = false;
                    try
                    {
                        suppressedAgain = flatPattern.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,
                            (int)swInConfigurationOpts_e.swThisConfiguration, null);
                        model.EditRebuild3();
                    }
                    catch { suppressedAgain = false; }
                    if (!suppressedAgain)
                        flatPatternProblem = "SetSuppression2 did not suppress the flat pattern «" + flatName +
                                             "» again after measuring it: the part may be left flattened, look at " +
                                             "it before going on";
                }
            }
        }

        /// <summary>
        /// The plan bounding box of the DXF, in mm, calculated from the ENTITIES — not
        /// from the header. Null if the file has no readable entities.
        ///
        /// MEASURED on 25/9/2026, at the first test: `$EXTMIN/$EXTMAX`
        /// of the header said 279.4 mm for a flat pattern of 80.712 —
        /// 279.4 is 11 inches, the sheet of the DXF template, not the part. A
        /// check built on the header would have refused every right DXF,
        /// or worse accepted the wrong ones of the right format.
        /// Here the (code, value) pairs of the ENTITIES section are read:
        /// points 10/20 and 11/21, and for arcs and circles the centre plus the radius.
        /// </summary>
        private static double[] DxfBoundingBox(string filePath)
        {
            try
            {
                var r = File.ReadAllLines(filePath);
                int start = -1;
                for (int i = 0; i + 1 < r.Length; i++)
                    if (r[i].Trim() == "2" && r[i + 1].Trim() == "ENTITIES") { start = i + 2; break; }
                if (start < 0) return null;
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                double cx = double.NaN, cy = double.NaN;
                bool arc = false;
                for (int i = start; i + 1 < r.Length; i += 2)
                {
                    string code = r[i].Trim(), val = r[i + 1].Trim();
                    if (code == "0")
                    {
                        if (val == "ENDSEC") break;
                        arc = val == "ARC" || val == "CIRCLE";
                        cx = cy = double.NaN;
                        continue;
                    }
                    double v;
                    if (!double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                    if (code == "10" || code == "11") { if (arc) cx = v; else { minX = Math.Min(minX, v); maxX = Math.Max(maxX, v); } }
                    else if (code == "20" || code == "21") { if (arc) cy = v; else { minY = Math.Min(minY, v); maxY = Math.Max(maxY, v); } }
                    else if (code == "40" && arc && !double.IsNaN(cx) && !double.IsNaN(cy))
                    {
                        minX = Math.Min(minX, cx - v); maxX = Math.Max(maxX, cx + v);
                        minY = Math.Min(minY, cy - v); maxY = Math.Max(maxY, cy + v);
                    }
                }
                if (minX == double.MaxValue || minY == double.MaxValue) return null;
                return new[] { Math.Round(maxX - minX, 3), Math.Round(maxY - minY, 3) };
            }
            catch { return null; }
        }

        private static List<double[]> ProfilePoints(Dictionary<string, object> a, out string error)
        {
            error = null;
            const string howItIsWritten =
                "«points» needs at least two points [x, y] in mm on the plane: the OPEN " +
                "profile of the sheet metal. An L: [[0,30],[0,0],[50,0]].";
            // A LIST IS AN IEnumerable, not an `object[]`. Measured at the first
            // bench run (25/9/2026): the arguments arrive as lists of the
            // serializer of whoever calls, and an `as object[]` gave null on an L
            // written correctly — refused with «it wants at least two points». It is the
            // same rule as `HasList`, which was already there.
            object raw;
            var list = a != null && a.TryGetValue("points", out raw) && !(raw is string)
                ? raw as System.Collections.IEnumerable : null;
            if (list == null) { error = howItIsWritten; return null; }
            var outside = new List<double[]>();
            foreach (var o in list)
            {
                var pair = new List<double>();
                var p = o is string ? null : o as System.Collections.IEnumerable;
                if (p != null)
                    foreach (var c in p) pair.Add(Convert.ToDouble(c, CultureInfo.InvariantCulture));
                if (pair.Count != 2)
                {
                    error = "Every point is a pair [x, y] in mm.";
                    return null;
                }
                outside.Add(pair.ToArray());
            }
            if (outside.Count < 2) { error = howItIsWritten; return null; }
            return outside;
        }

        /// <summary>The bend angle at point b, in radians (0 = straight).</summary>
        private static double BendAngle(double[] a, double[] b, double[] c)
        {
            double ux = b[0] - a[0], uy = b[1] - a[1], vx = c[0] - b[0], vy = c[1] - b[1];
            double cos = (ux * vx + uy * vy) / (Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy));
            return Math.Acos(Math.Max(-1, Math.Min(1, cos)));
        }
    }
}
