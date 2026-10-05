using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE FILL PATTERN. (entry W1.1, residue of GP14)
    ///
    /// A face full of holes: a vent grille, a plate drilled to
    /// lighten it, a sieve. The feature is made once, and SOLIDWORKS
    /// copies it in concentric loops until the boundary face contains it.
    ///
    /// ── THE API GUIDE, CITED (local extract of sldworksapi.chm) ──────────
    ///
    ///   SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureFillPattern.html
    ///     «Obsolete. See IFeatureManager::CreateFeature and the Remarks in
    ///     IFillPatternFeatureData.» The fifteen NULL combinations of GP14
    ///     (note 81u) were all on this dead track.
    ///
    ///   SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFillPatternFeatureData.html
    ///     Remarks: «Before calling IFeatureManager::CreateDefinition, you can
    ///     pre-select the entities»; the table of the marks:
    ///       boundary (face, sketch, planar curve)     16384
    ///       direction (edge or axis)                      1   («if not selected,
    ///                                                      SOLIDWORKS chooses»)
    ///       seed position (vertex, point)                 3   (optional: without it,
    ///                                                      «the pattern seed instance
    ///                                                      is placed at the center of
    ///                                                      the fill boundary region»)
    ///       feature to pattern                            4   (faces 5, bodies 6)
    ///     «Or, after calling CreateDefinition, you can explicitly set the
    ///     corresponding properties» — the second route, the one GP14 had
    ///     tried seven times and that returns NULL (measured again below).
    ///
    ///   Create_Fill_Pattern_Example_CSharp.htm — the only example: Hole1 with
    ///     mark 4, an edge with mark 1, a face with mark 16384, THEN
    ///     CreateDefinition(swFmFillPattern), scalars only, CreateFeature. The
    ///     scalars of this file are its own, one by one (see FillScalars).
    ///
    ///   ...IFillPatternFeatureData~NoOfInstances.html: «the number of instances
    ///     per loop or side», valid with LayoutSpacingType = swPatternLayoutInstances.
    ///   ...IFillPatternFeatureData~LoopSpacing.html: «the spacing between the
    ///     centers of instances in adjacent pattern loops», for the circular
    ///     layout.
    ///   ...IFillPatternFeatureData~Margins.html: «distance between the fill
    ///     boundary and the outermost pattern instance; 0 for no margin».
    ///
    /// ── THE PROBE, RUN ON 28/9/2026 (Checks\Fill\Program.cs) ──
    ///
    /// Plate 100 x 60 x 12, a through hole Ø8 at 30 mm from the centre (603.2 mm3),
    /// SOLIDWORKS 29.5.1:
    ///
    ///   A  feature 4 + edge 1 + face 16384 BEFORE CreateDefinition
    ///        -> «Ripetizione di riempimento1» [APattern], volume 71396.8 -> 62952.2:
    ///           14 more holes, -8444.6 = 14 x 603.2 exact, bodies 1 -> 1, 0 in error
    ///   C  same selections AFTER CreateDefinition          -> same as A
    ///   D  the face of the hole with mark 5 instead of 4   -> same as A
    ///   A2 like A WITHOUT the direction edge               -> NULL
    ///   B  the historic way: no selection, properties written -> NULL
    ///
    /// Two things to keep. **The direction is mandatory**, even if the guide
    /// says it is optional: without it, NULL and no message. Therefore here it refuses
    /// BEFORE touching the part. And **the type of the feature is «APattern»**, not
    /// a name that contains «Fill»: the measured one is checked.
    ///
    /// Here recipe A is done, in the same order: feature, direction,
    /// boundary, then CreateDefinition.
    ///
    /// ── WHAT IS NOT THERE, AND WHY ────────────────────────────────────
    ///
    /// Only the CIRCULAR layout, with the number of copies per loop: it is the one
    /// of the example, and it is the only one measured. Square, polygonal and
    /// perforation have their own properties (InstanceSpacing, StaggerAngle,
    /// PatternLayoutPolygonSides) that no probe saw born.
    /// The TABLE (ITablePatternFeatureData, the other half of W1.1) stays out:
    /// measured NULL without the coordinate system with mark 16 that its
    /// example selects.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>The type of the feature born, measured (GetTypeName2) on probe A.</summary>
        private const string FillType = "APattern";

        /// <summary>
        /// How far the direction can leave the plane of the boundary face:
        /// the sine of the angle, 0.01 = about half a degree. An edge of the face
        /// stays inside it with a machine zero.
        /// </summary>
        private const double FillOffPlane = 0.01;

        private Outcome RepeatFill(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (!(model is IPartDoc))
                return Failed("La ripetizione a riempimento si fa su un pezzo: apri il pezzo e richiedimela.");

            // ── THE NUMBERS, before touching anything ──
            double planStep, rawCopies;
            string error;
            if (!RequiredNum(a, "pitch_mm", out planStep, out error))
                return Failed(error + " E' la distanza fra un giro di copie e il successivo, " +
                               "in mm: metti per esempio due volte il diametro del foro.");
            if (planStep <= 0)
                return Failed("«pitch_mm» deve essere maggiore di zero: e' la distanza fra un giro " +
                               "di copie e il successivo. Metti per esempio due volte il diametro del foro.");
            if (!RequiredNum(a, "copies_per_turn", out rawCopies, out error))
                return Failed(error + " E' quante copie stanno su ogni giro attorno al centro " +
                               "della faccia: per esempio 10.");
            int copiesPerRound = (int)rawCopies;
            if (copiesPerRound < 1 || Math.Abs(rawCopies - copiesPerRound) > 1e-9)
                return Failed("«copies_per_turn» e' un numero intero da 1 in su, non " +
                               rawCopies.ToString(CultureInfo.InvariantCulture) + ": per esempio 10.");
            double margin = Num(a, "margin_mm", 0);
            if (margin < 0)
                return Failed("«margin_mm» non puo' essere negativo: e' quanto le copie restano " +
                               "lontane dal bordo della faccia. Metti 0 per arrivare al bordo.");

            // ── THE DIRECTION IS MANDATORY, AND IT IS STATED FIRST ──
            //
            // The guide says it is optional («SOLIDWORKS chooses a direction
            // reference»); probe A2 of 28/9/2026 says NULL without it, and
            // without any message. A refusal here costs zero; a NULL after having
            // selected half a part costs a turn of guessing.
            //
            // NOT «AN AXIS MADE WITH create_axis» (Z-C21.9, finding N62). The
            // sentence sent the caller to create_axis, whose axis on a hole runs
            // THROUGH the face of the hole, not along it (the catalogue says so
            // for create_axis itself): the check below («the direction does not lie
            // in the plane of the boundary face») then refused it, 90 degrees
            // out, and a turn was lost. The kind of axis that works is named.
            if (!HasReference(a, "axis_ref") && string.IsNullOrEmpty(Str(a, "axis_uid", "")))
                return Failed(
                    "The direction is missing: without a direction the fill pattern is not born. The API guide " +
                    "calls it optional, but measured on SOLIDWORKS 29.5.1 (28/9/2026) without the direction edge " +
                    "SOLIDWORKS answers nothing and says nothing. Give in «axis_uid» a straight edge that runs " +
                    "along the boundary face (in the state «E1», «E2»...), for example its long edge, or in " +
                    "«axis_ref» an axis that lies along that face. The axis of a hole drilled into the face does " +
                    "not do: it runs across the face, and a direction that leaves the face is refused.");

            // ── THE BOUNDARY FACE, AND IT MUST BE PLANAR ──
            string faceUid = Str(a, "face_uid", "");
            string reason;
            var contour = ResolveFace(docState, a, faceUid, out reason);
            if (contour == null) return Failed("Faccia di contorno: " + reason);
            var normal = FaceNormal(contour);
            if (normal == null || normal.Length < 3)
                return Failed(
                    "La faccia di contorno" + (faceUid.Length > 0 ? " «" + faceUid + "»" : "") +
                    " non e' piana: il riempimento dispone le copie dentro una faccia piana. " +
                    "Indica la faccia piana su cui sta la lavorazione da ripetere, per esempio " +
                    "quella da cui parte il foro.");

            // ── THE SNAPSHOT, FIRST ──
            var before = SnapshotPart(model);
            double? volumeBefore; int bodiesBefore, facesBefore;
            Snapshot(model, out volumeBefore, out bodiesBefore, out facesBefore);
            if (volumeBefore == null || bodiesBefore < 0 || facesBefore < 0)
                return Failed(VolumeNotReadBefore("fare la ripetizione a riempimento"));

            IFeature seed = null;
            IFeature feature = null;
            string statedDirection = HasReference(a, "axis_ref") ? "la direzione del passo citato" : "«" + Str(a, "axis_uid", "") + "»";
            try
            {
                model.ClearSelection2(true);

                // 1. THE DIRECTION GOES THROUGH ITS HELPER, which checks that the
                //    edge is straight and still the same one (signature, E19), the
                //    same defence as «linear_pattern». Then it is read back from
                //    the selection and the selection starts again: the order of
                //    the example and of probe A is feature, direction, boundary.
                //    The direction is the ONLY thing selected here because this
                //    call passes append: false, not because of mark 1 (Z-C21.9,
                //    finding N65: this said «the helper with mark 1 selects from
                //    scratch», the old signature). With append: true,
                //    SelectedFillDirection, which reads
                //    GetSelectedObject6(1), would read another object.
                if (!SelectAxis(model, a, "axis", docState, SelectionMarks.FillPatternDirection, append: false, out error))
                    return Failed("Direzione: " + error);
                object direction = SelectedFillDirection(model);
                if (direction == null)
                    return Failed("Ho selezionato la direzione ma non riesco a rileggerla dalla " +
                                   "selezione. Prova con uno spigolo dritto dello stato invece di un asse.");

                // THE DIRECTION MUST LIE IN THE FACE. The fill arranges the
                // copies inside the boundary: an edge that leaves the face (the
                // thickness of the plate) has no direction to follow in there.
                // If the direction cannot be read, no refusal is invented.
                var versore = UnitDirectionVector(direction);
                if (versore != null)
                {
                    double seno = Math.Abs(versore[0] * normal[0] + versore[1] * normal[1] + versore[2] * normal[2]);
                    if (seno > FillOffPlane)
                        return Failed(string.Format(CultureInfo.InvariantCulture,
                            "La direzione {0} non sta nel piano della faccia di contorno: ne esce di " +
                            "{1:0.#} gradi. Il riempimento dispone le copie dentro la faccia, e una " +
                            "direzione che ne esce non ha un verso da seguire. Indica uno spigolo che " +
                            "corre lungo la faccia, per esempio uno dei suoi bordi dritti.",
                            statedDirection, Math.Asin(Math.Min(1.0, seno)) * 180.0 / Math.PI));
                }
                model.ClearSelection2(true);

                // 2. The feature to pattern, mark 4 (SelectByID2 BODYFEATURE,
                //    like the example; or the object of the step cited).
                if (!SelectFeature(model, a, out error, SelectionMarks.FillPatternFeatures))
                    return Failed(error);
                seed = SelectedSeed(model, SelectionMarks.FillPatternFeatures);

                // 3. The direction again, mark 1, added.
                if (!ReselectDirection(model, direction))
                    return Failed("Non sono riuscito a riselezionare la direzione " + statedDirection +
                                   " insieme alla lavorazione. Prova con un altro spigolo dritto della faccia.");

                // 4. The boundary, mark 16384, added.
                var handler = model.SelectionManager as ISelectionMgr;
                if (handler == null || !Select(handler, contour, SelectionMarks.FillPatternBoundary))
                    return Failed("Non sono riuscito a selezionare la faccia di contorno" +
                                   (faceUid.Length > 0 ? " «" + faceUid + "»" : "") +
                                   ". Rileggi lo stato del documento e indica di nuovo la faccia.");

                // 5. ONLY NOW the definition, and only scalars.
                var data = model.FeatureManager.CreateDefinition(
                    (int)swFeatureNameID_e.swFmFillPattern) as IFillPatternFeatureData;
                if (data == null)
                    return Failed("SOLIDWORKS non mi ha dato la definizione di una ripetizione a " +
                                   "riempimento (CreateDefinition vuoto). Non ho toccato il pezzo. " +
                                   "Per copie in fila o in griglia usa «linear_pattern».");
                FillScalars(data, Units.M(planStep), copiesPerRound, Units.M(margin));
                feature = model.FeatureManager.CreateFeature(data) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("La ripetizione a riempimento non e' riuscita: " + ex.Message +
                               ". Per copie in fila o in griglia usa «linear_pattern».");
            }
            finally
            {
                try { model.ClearSelection2(true); } catch { }
            }

            if (feature == null)
                return Failed(
                    "La ripetizione a riempimento non e' stata creata, e SOLIDWORKS non dice " +
                    "perche'. Le tre selezioni c'erano (lavorazione, direzione, contorno): di " +
                    "solito vuol dire che nessuna copia entra nella faccia -- il passo e' troppo " +
                    "grande o il margine troppo largo -- oppure che la lavorazione non sta su " +
                    "quella faccia. Prova un «pitch_mm» piu' piccolo o «margin_mm» 0; per copie " +
                    "in fila o in griglia c'e' «linear_pattern», per un cerchio attorno a un asse " +
                    "«circular_pattern».");

            // ── AND NOW THE PART IS MEASURED ──
            bool rebuilt = false;
            try { rebuilt = model.EditRebuild3(); } catch { }

            string typ = "";
            try { typ = feature.GetTypeName2() ?? ""; } catch { }
            if (!string.Equals(typ, FillType, StringComparison.Ordinal))
                return Failed(
                    "SOLIDWORKS ha creato una feature di tipo «" + typ + "», non una ripetizione " +
                    "a riempimento («" + FillType + "», misurato su SOLIDWORKS 29.5.1). " +
                    "Non ti confermo una cosa diversa da quella che hai chiesto." +
                    RemoveFill(model, feature, volumeBefore));

            // The common guards (GuardOperations.cs): features in error,
            // rebuild, bodies, openings that leave the edge or merge.
            // The openings are counted: a copy that bites the edge of the face or
            // merges with another makes them drop.
            string broken = WhatBroke(model, before, rebuilt, true);
            if (broken != null)
                return Failed(
                    "La ripetizione a riempimento e' nata, ma " + broken + ". Prova un «margin_mm» " +
                    "piu' largo, perche' le copie restino lontane dal bordo, o un «pitch_mm» piu' " +
                    "grande, perche' non si tocchino." + RemoveFill(model, feature, volumeBefore));

            // WHAT THE FEATURE DECLARES, read back with IFeature::GetDefinition
            // as IFillPatternFeatureData — not the repeated arguments. It
            // decides nothing (the volume, the copies counted and the
            // guards decide); a value that cannot be read back is null.
            var measurements = new Dictionary<string, object> { ["feature_type"] = typ };
            DeclaredByFill(feature, measurements);

            // «Niente e' cambiato» is the false success: CompareCopy catches it.
            var stopped = CompareCopy(
                model, volumeBefore, bodiesBefore, facesBefore, measurements, "La ripetizione a riempimento");
            if (stopped != null) return Failed(stopped + RemoveFill(model, feature, volumeBefore));

            // ── THE COPIES, COUNTED FROM THE FACES (like MissingCopies) ──
            //
            // How many copies fit in a fill is not known in advance: the shape
            // of the face decides it. They are counted and stated. Zero faces
            // owned means zero copies, and that is refused; a count that
            // does not divide (copies merged with each other) is left unsaid instead of invented.
            int patternFaces = FeatureFaces(feature);
            int seedFaceList = FeatureFaces(seed);
            if (patternFaces == 0)
                return Failed(
                    "La ripetizione a riempimento e' nata ma non possiede nessuna faccia: nessuna " +
                    "copia e' entrata nella faccia di contorno. Prova un «pitch_mm» piu' piccolo " +
                    "o «margin_mm» 0." + RemoveFill(model, feature, volumeBefore));
            int copies = patternFaces > 0 && seedFaceList > 0 && patternFaces % seedFaceList == 0
                ? patternFaces / seedFaceList : -1;
            measurements["copies_made"] = NumberOrNull(copies);
            double? volumeAfter = VolumeMm3(model);
            measurements["volume_per_copy_mm3"] = copies > 0 && volumeAfter.HasValue
                ? NumberOrNull(Math.Abs(volumeAfter.Value - volumeBefore.Value) / copies, 2)
                : null;

            return Succeeded(feature, measurements);
        }

        /// <summary>
        /// The scalars of Create_Fill_Pattern_Example_CSharp.htm, one by one, with
        /// the three that the operation exposes (pitch, copies per loop, margin) in place
        /// of its 17.024 mm, 10 and 0.
        ///
        /// The values of the seed shape (Diameter, Diagonal, Dimension, Inner/
        /// OuterRadius, CreateSeedCutPolygonSides) serve only for
        /// FeaturesToPatternType = CreateSeedCut: with swCreateSeedCutNone they do not
        /// count, and they are written all the same identical to the example because the
        /// measured recipe is the whole one. So are StaggerAngle (only for
        /// perforation) and InstanceSpacing, which the example sets equal to the pitch.
        ///
        /// GeometryPattern stays false: «valid only if PatternElement is
        /// swFeatureFaces» (GeometryPattern.html), and here the feature is patterned.
        /// </summary>
        private static void FillScalars(
            IFillPatternFeatureData d, double pitchM, int copiesPerRound, double marginM)
        {
            d.CreateSeedCutPolygonSides = 0;
            d.CreateSeedCutType = (int)swCreateSeedCutType_e.swCreateSeedCutNone;
            d.Diagonal = 0.008;
            d.Diameter = 0.008;
            d.Dimension = 0.00565685424949238;
            d.FeaturesToPatternType = (int)swFeaturesToPatternType_e.swFeaturesToPatternSelectedFeatures;
            d.GeometryPattern = false;
            d.InnerRadius = 0.00380422606518061;
            d.InstanceSpacing = pitchM;
            d.LayoutSpacingType = (int)swPatternLayoutSpacingType_e.swPatternLayoutInstances;
            d.LoopSpacing = pitchM;
            d.Margins = marginM;
            d.NoOfInstances = copiesPerRound;
            d.OuterRadius = 0.004;
            d.PatternLayoutPolygonSides = 0;
            d.PatternLayoutType = (int)swPatternLayoutType_e.swPatternLayoutCircular;
            d.Rotation = 0;
            d.SeedCutFlipShapeDirection = false;
            d.StaggerAngle = Math.PI / 3.0;
        }

        /// <summary>The first object of the selection, or null.</summary>
        private static object SelectedFillDirection(IModelDoc2 model)
        {
            try
            {
                var sel = model.SelectionManager as ISelectionMgr;
                if (sel == null || sel.GetSelectedObjectCount2(-1) < 1) return null;
                return sel.GetSelectedObject6(1, -1);
            }
            catch { return null; }
        }

        /// <summary>
        /// The unit vector of a selected direction: a straight edge
        /// (ICurve.LineParams: point 0-2, direction 3-5, as in DocumentState)
        /// or a reference axis (IRefAxis.GetRefAxisParams: the two ends).
        /// Null if it cannot be read: the caller then does not refuse.
        /// </summary>
        private static double[] UnitDirectionVector(object direction)
        {
            double[] v = null;
            try
            {
                var edge = direction as IEdge;
                if (edge != null)
                {
                    var curva = edge.IGetCurve();
                    var p = curva != null && curva.IsLine() ? curva.LineParams as double[] : null;
                    if (p != null && p.Length >= 6) v = new[] { p[3], p[4], p[5] };
                }
                else
                {
                    var axis = (direction as IFeature)?.GetSpecificFeature2() as IRefAxis;
                    var p = axis?.GetRefAxisParams() as double[];
                    if (p != null && p.Length >= 6) v = new[] { p[3] - p[0], p[4] - p[1], p[5] - p[2] };
                }
            }
            catch { return null; }
            if (v == null) return null;
            double l = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (l < 1e-12) return null;
            return new[] { v[0] / l, v[1] / l, v[2] / l };
        }

        /// <summary>
        /// The direction already validated, reselected with mark 1 IN ADDITION
        /// to the feature. An edge with `IEntity.Select4` and a
        /// `SelectData` with `Mark = 1`: the call of the probe
        /// (Checks\Fill, `Mark(selMgr, 1)`), the same as
        /// `Select(gestore, faccia, marca)` for the boundary face. A reference axis
        /// per feature (`Select2` with the mark): the probe did not use
        /// any, and it is said in the catalogue.
        /// </summary>
        private static bool ReselectDirection(IModelDoc2 model, object direction)
        {
            try
            {
                if (direction is IEdge)
                {
                    var entity = direction as IEntity;
                    var handler = model.SelectionManager as ISelectionMgr;
                    if (entity == null || handler == null) return false;
                    var d = (SelectData)handler.CreateSelectData();
                    d.Mark = SelectionMarks.FillPatternDirection;
                    return entity.Select4(true, d);
                }
                var f = direction as IFeature;
                if (f != null) return f.Select2(true, SelectionMarks.FillPatternDirection);
            }
            catch { }
            return false;
        }

        /// <summary>
        /// The four scalars the operation asks for, as the feature born
        /// DECLARES them (guide pages
        /// `IFillPatternFeatureData~PatternLayoutType.html`, `~LoopSpacing.html`
        /// «Distance between loops of pattern instances», `~NoOfInstances.html`
        /// «Number of instances per loop or side», `~Margins.html`), read from
        /// `IFeature::GetDefinition` (`IFeature~GetDefinition.html`: «query the
        /// feature data object»). The layout is written by NAME of the enum
        /// `swPatternLayoutType_e`, never by digit. What cannot be read back
        /// stays null: «I do not know» is not filled in with the argument.
        /// </summary>
        private static void DeclaredByFill(IFeature feature, Dictionary<string, object> measurements)
        {
            object arrangement = null, planStep = null, copies = null, margin = null;
            try
            {
                var d = feature.GetDefinition() as IFillPatternFeatureData;
                if (d != null)
                {
                    try { arrangement = ArrangementName(d.PatternLayoutType); } catch { }
                    try { planStep = Math.Round(Units.Mm(d.LoopSpacing), 3); } catch { }
                    try { copies = d.NoOfInstances; } catch { }
                    try { margin = Math.Round(Units.Mm(d.Margins), 3); } catch { }
                }
            }
            catch { }
            measurements["declared_layout"] = arrangement;
            measurements["declared_loop_spacing_mm"] = planStep;
            measurements["declared_instances_per_loop"] = copies;
            measurements["declared_margin_mm"] = margin;
        }

        /// <summary>The English name of the layout, from the enum name; null if unknown.</summary>
        private static string ArrangementName(int val)
        {
            if (val == (int)swPatternLayoutType_e.swPatternLayoutCircular) return "circular";
            if (val == (int)swPatternLayoutType_e.swPatternLayoutSquare) return "square";
            if (val == (int)swPatternLayoutType_e.swPatternLayoutPolygon) return "polygon";
            if (val == (int)swPatternLayoutType_e.swPatternLayoutPerforation) return "perforation";
            return null;
        }

        /// <summary>
        /// Removes the pattern just born when a guard refuses it, and returns
        /// the tail of the message. Not EditDelete (it opens a confirmation window:
        /// ForbiddenSignaturesTests), but DeleteSelection2 like `RemoveCut`; and «it
        /// said it removed it» is not enough: the tree is counted again and the
        /// volume is measured again.
        /// </summary>
        private string RemoveFill(IModelDoc2 model, IFeature repetition, double? volumeBefore)
        {
            bool removed = false;
            try
            {
                model.ClearSelection2(true);
                if (repetition != null && repetition.Select2(false, 0))
                    removed = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch { }
            finally { try { model.ClearSelection2(true); } catch { } }

            const string remains = " La ripetizione pero' e' rimasta nel pezzo: ritirala — " + Sentences.HowToWithdrawIt + ".";
            if (!removed) return remains;
            try { model.EditRebuild3(); } catch { }
            int features = CountFeature(model);
            if (features < 0 || _featuresAtStepStart < 0)
                return " L'ho tolta, ma non ho potuto ricontare l'albero per controllare che il " +
                       "pezzo sia com'era: guardalo.";
            // Removed by DeleteSelection2: from here the pattern is gone, and
            // «still in the part» would be false (Z-C21.9, finding C9-6).
            if (features > _featuresAtStepStart)
                return RemovedButTreeLonger(features - _featuresAtStepStart);
            double? volumeAfter = VolumeMm3(model);
            if (volumeBefore == null || volumeAfter == null)
                return " L'ho tolta, ma non ho potuto rimisurare il volume per controllare che il " +
                       "pezzo sia com'era: guardalo.";
            if (Math.Abs(volumeAfter.GetValueOrDefault() - volumeBefore.GetValueOrDefault()) > 0.01)
                return RemovedButVolumeChanged(volumeAfter.GetValueOrDefault(), volumeBefore.GetValueOrDefault());
            return " L'ho tolta: il pezzo e' com'era prima di questo passo.";
        }
    }
}
