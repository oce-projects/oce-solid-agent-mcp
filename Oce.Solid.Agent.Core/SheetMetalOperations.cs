using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LA LAMIERA (voce GP18): la flangia base e lo sviluppo per il taglio.
    ///
    /// ── LA MISURA CHE L'HA RESA COSTRUIBILE, 24/9/2026 ─────────────────────
    ///
    /// `Checks/SheetMetal`: una L da 50 + 30, profonda 40, T 2, R 2, K 0,5
    /// riletti dalla lamiera. Lo sviluppo esce 80,712 mm, e il conto a mano con
    /// lo schizzo sulle facce INTERNE da' 80,712 — scarto zero; con le facce
    /// esterne 76,712. Volume piegato e sviluppato uguali. **La convenzione e'
    /// misurata, non letta**: i punti dati qui sono le facce interne, e lo
    /// spessore va fuori.
    ///
    /// ── TRE TRAPPOLE, tutte senza errore ──────────────────────────────────
    ///
    ///  1. `SetBendState(sviluppata)` risponde 0 e `GetBendState` dice
    ///     «sviluppata», e il corpo resta PIEGATO. Lo sviluppo si legge togliendo
    ///     la soppressione alla lavorazione «FlatPattern» in fondo all'albero, e
    ///     rimettendola.
    ///  2. The export of the flat pattern on a part NEVER SAVED: the obsolete
    ///     `ExportFlatPatternView` (used until Z-C21.7) opened "Save As" and the
    ///     COM call did not return. `ExportToDWG2`, used since then, takes the
    ///     path of the part as ModelName, and on an unsaved part it was not
    ///     measured (the Z-C21.3 probe left that case to a run with someone at
    ///     the screen). `flat_pattern_dxf` still refuses a part without a file,
    ///     before calling.
    ///  3. Lo schizzo si disegna con `AddToDB` a vero: le relazioni che
    ///     SOLIDWORKS indovina da solo sovravincolano alla prima modifica
    ///     (misurato con `Checks/FollowTheEdge`).
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
                return Failed("«thickness_mm» e «depth_mm» devono essere positivi.");
            if (radius < 0)
                return Failed("«radius_mm» non puo' essere negativo: e' il raggio INTERNO della piega.");

            var points = ProfilePoints(a, out error);
            if (points == null) return Failed(error);

            // UN TRATTO PIU' CORTO DEL RAGGIO NON SI PIEGA: la piega mangia R da
            // ogni lato interno. Il limite si dichiara prima di chiamare, perche'
            // dopo SOLIDWORKS darebbe una lavorazione rossa senza dire quale lato.
            for (int i = 0; i < points.Count - 1; i++)
            {
                double l = Math.Sqrt(Math.Pow(points[i + 1][0] - points[i][0], 2) +
                                     Math.Pow(points[i + 1][1] - points[i][1], 2));
                if (l <= 0.001)
                    return Failed("Il tratto " + (i + 1) + " e' lungo zero: due punti uguali di fila.");
                bool has2Bends = i > 0 && i < points.Count - 2;
                double serve = has2Bends ? 2 * radius : radius;
                if (points.Count > 2 && l <= serve)
                    return Failed(string.Format(CultureInfo.InvariantCulture,
                        "Il tratto {0} e' lungo {1:0.###} mm e le pieghe col raggio {2:0.###} ne " +
                        "consumano {3:0.###}: non resta niente di dritto. Allunga il tratto o " +
                        "stringi il raggio.", i + 1, l, radius, serve));
            }

            string plane = Str(a, "plane", "front");
            if (!SelectPlane(model, plane))
                return Failed("Non riesco a selezionare il piano «" + plane + "».");

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
            catch (Exception ex) { return Failed("La flangia base non e' nata: " + ex.Message + LeftSketchBehind); }
            if (flange == null)
                return Failed("La flangia base non e' nata, e SOLIDWORKS non dice perche'." + LeftSketchBehind);
            model.EditRebuild3();

            // ── NATA IN ERRORE? (voce Z-C21.2, rilievo L10-4) ─────────────
            //
            // IFeature~GetErrorCode2.html: «This method returns many of the
            // errors indicated by the What's Wrong icon seen with an invalid
            // feature in the FeatureManager design tree». Le tre sorelle
            // (edge_flange, orlo, sketched_bend) lo leggevano, questa no:
            // la rilettura di T e R qui sotto viene dalla lavorazione «SheetMetal»,
            // che c'e' anche con la flangia rossa, e non prova niente sulla flangia.
            var flangeError = ApiHelpers.RebuildError(flange);   // S11: GetErrorCode2, warnings excluded
            if (flangeError != null)
                return Failed("La flangia base «" + flange.Name + "» e' nata in errore (" +
                               flangeError + "): ritirala — " + Sentences.HowToWithdrawIt + ".");

            // ── T, R, K RILETTI dalla lamiera, non ripetuti dagli argomenti ──
            var data = SheetMetalData(model);
            if (data == null)
                return Failed("La flangia c'e' ma non trovo la lavorazione «SheetMetal»: non so " +
                               "con che spessore e che fattore K e' nata. Ritirala — " + Sentences.HowToWithdrawIt + ".");
            double t = Units.Mm(data.Thickness), r = Units.Mm(data.BendRadius), k = data.KFactor;
            if (Math.Abs(t - thickness) > 0.001 || Math.Abs(r - radius) > 0.001)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "Ho chiesto spessore {0} e raggio {1}, e la lamiera dice {2:0.###} e {3:0.###}. " +
                    "Ritirala — {4}.", thickness, radius, t, r, Sentences.HowToWithdrawIt));

            double? bentVolume = VolumeMm3(model);
            double flatPattern = FlattenedLength(model, out double flattenedVolume, out string flatPatternProblem);

            // IL CONTO A MANO, nelle due convenzioni. Misurato: con la L data
            // dall'alto verso destra lo spessore va a DESTRA del verso di
            // percorrenza e i punti sono le facce interne. Dato al contrario,
            // lo spessore va dall'altra parte e i punti diventano le facce
            // ESTERNE — lo sviluppo cambia di 2T per piega. Non si indovina: si
            // calcolano tutte e due e si dice quale combacia con SOLIDWORKS.
            double sum = 0;
            for (int i = 0; i < points.Count - 1; i++)
                sum += Math.Sqrt(Math.Pow(points[i + 1][0] - points[i][0], 2) +
                                   Math.Pow(points[i + 1][1] - points[i][1], 2));
            int bends = points.Count - 2;
            double angles = 0;
            for (int i = 1; i < points.Count - 1; i++) angles += BendAngle(points[i - 1], points[i], points[i + 1]);
            double inner = sum - 2 * r * bends + angles * (r + k * t);
            double outer = sum - 2 * (r + t) * bends + angles * (r + k * t);
            string convention = double.IsNaN(flatPattern) ? "non misurata"
                : Math.Abs(flatPattern - inner) <= 0.5 ? "inner"
                : Math.Abs(flatPattern - outer) <= 0.5 ? "outer" : "nessuna delle due";
            double expected = convention == "outer" ? outer : inner;

            // ── E HA AGGIUNTO MATERIALE? (rilievo L10-4, come orlo e flangia a
            // spigolo). Il volume di una lamiera piegata e' lo sviluppo per lo
            // spessore per la profondita', a meno di dove si prende la fibra
            // neutra: meta' del conto piu' corto delle due convenzioni e' una
            // soglia che nessuna flangia vera manca, e una flangia vuota si'.
            // Prima questo numero si riportava soltanto.
            double? addedFlange = bentVolume - volumeBefore;
            double flangeThreshold = 0.5 * Math.Min(inner, outer) * t * depth;
            if (addedFlange.HasValue && addedFlange.Value < flangeThreshold)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "La flangia base «{0}» e' nell'albero ma ha aggiunto {1:0.##} mm3, meno della meta' " +
                    "del conto a mano ({2:0.##} mm3 di sviluppo per spessore per profondita'): non e' " +
                    "la lamiera chiesta. Ritirala — {3}.",
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

            // IL CONTROLLO CHE DA' SENSO ALLA FAMIGLIA: lo sviluppo di SOLIDWORKS
            // contro il conto a mano. Se divergono, il DXF che andra' al laser
            // non e' quello che uno si aspetta, e lo si dice adesso.
            if (convention == "nessuna delle due")
                measurements["warning"] = string.Format(CultureInfo.InvariantCulture,
                    "Lo sviluppo di SOLIDWORKS e' {0:0.###} mm e il conto a mano ne da' {1:0.###}: " +
                    "prima di mandare il DXF al taglio, controlla la tabella delle pieghe del " +
                    "modello di documento.", flatPattern, expected);
            if (!double.IsNaN(flattenedVolume) && bentVolume.HasValue &&
                Math.Abs(flattenedVolume - bentVolume.Value) > 0.005 * bentVolume.Value)
                measurements["warning_volume"] = "Il volume sviluppato non e' quello piegato: lo sviluppo non e' affidabile.";

            return Succeeded(flange, measurements);
        }

        /// <summary>
        /// LA FLANGIA A SPIGOLO (voce GP18): una piega nuova su uno spigolo di
        /// bordo di una lamiera che c'e' gia'. Serve alle scatole — pieghe su
        /// spigoli di direzioni diverse, che `base_flange` da un profilo solo non fa.
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
                return Failed("Questo pezzo non e' di lamiera: la flangia a spigolo si attacca a una " +
                               "lamiera che c'e' gia' (fatta con «base_flange»).");
            double t = Units.Mm(data.Thickness), r = Units.Mm(data.BendRadius), k = data.KFactor;

            // NIENTE DI DRITTO: la piega consuma R + T dalla lunghezza misurata allo
            // spigolo esterno. Si dice prima di toccare il pezzo.
            if (length <= r + t + 0.001)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "Una flangia lunga {0:0.###} mm allo spigolo esterno non ha niente di dritto: " +
                    "la piega col raggio {1:0.###} e lo spessore {2:0.###} ne consumano {3:0.###}. " +
                    "Chiedila piu' lunga di {3:0.###} mm.", length, r, t, r + t));

            string uid = Str(a, "edge_uid", "");
            var border = BorderEdge(a, docState, uid, t, "una flangia", out error);
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
            catch (Exception ex) { return Failed("La flangia a spigolo non e' nata: " + ex.Message + WithoutSketch(model)); }
            finally { try { model.ClearSelection2(true); } catch { } }
            model.EditRebuild3();

            var flangeError = ApiHelpers.RebuildError(flange);   // S11: GetErrorCode2, warnings excluded
            if (flangeError != null)
                return Failed("La flangia a spigolo «" + flange.Name + "» e' nata in errore (" + flangeError +
                               "): ritirala — " + Sentences.HowToWithdrawIt + ".");

            double read = -1;
            try { var def = flange.GetDefinition() as IEdgeFlangeFeatureData; if (def != null) read = Units.Mm(def.OffsetDistance); }
            catch { }
            double? volumeAfter = VolumeMm3(model);
            double ba = Math.PI / 2 * (r + k * t);
            double extraFlatPattern = (length - r - t) + ba - (r + t);
            double expectedVolume = extraFlatPattern * t * Units.Mm(along);
            double? added = volumeAfter - volumeBefore;

            // NATA VUOTA: e' il tentativo 9 della sonda — una «Bordo-Flangia»
            // nell'albero che non aggiunge niente. Non si conferma un pezzo
            // uguale a prima.
            if (added.HasValue && added.Value < 0.5 * expectedVolume)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "La flangia «{0}» e' nell'albero ma ha aggiunto {1:0.##} mm3 invece dei {2:0.##} del " +
                    "conto: non e' la flangia chiesta. Ritirala — {3}.",
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
                    "La flangia ha aggiunto {0:0.##} mm3 e il conto a mano ne da' {1:0.##}: prima del taglio " +
                    "controlla lo sviluppo.", added.Value, expectedVolume);
            if (read >= 0 && Math.Abs(read - length) > 0.01)
                measurements["warning_length"] = string.Format(CultureInfo.InvariantCulture,
                    "Ho chiesto {0:0.###} mm allo spigolo esterno e la flangia dice {1:0.###}.", length, read);
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

        /// <summary>Uno spigolo di bordo di una lamiera, con le sue due facce.</summary>
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
        /// Lo spigolo di bordo su cui si piega (flangia a spigolo, orlo): preso
        /// per firma (voce E19), dritto, fra la faccia LARGA della lamiera e una
        /// faccia di TESTA piana, larga quanto lo spessore (area = T x lunghezza).
        /// Null col motivo, che dice quale spigolo serve.
        ///
        /// IL FIANCO DI UNA LAMIERA NON E' UNA FACCIA SOLA (`prova0089 elle`,
        /// 26/9/2026): e' spezzato per tratti, e ogni tratto e' largo proprio lo
        /// spessore. Quindi anche gli spigoli lungo i fianchi sono di bordo, e lo
        /// sono davvero; non lo e' lo spigolo di tangenza di una piega, dove una
        /// faccia e' il cilindro della piega.
        /// </summary>
        private SheetMetalEdge BorderEdge(
            Dictionary<string, object> a, DocumentState docState, string uid, double t,
            string what, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(uid))
            {
                reason = "Manca «edge_uid»: lo spigolo di bordo della lamiera su cui piegare, " +
                         "preso dallo stato del documento (E1, E2...).";
                return null;
            }
            var signature = EdgeSignature(
                a, docState, uid, "Lo spigolo «" + uid + "» non c'e' piu' in questo documento.", out reason);
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
                reason = "Lo spigolo «" + uid + "» non e' dritto: " + what + " si piega su uno spigolo dritto.";
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
                    "Lo spigolo «{0}» non sta sul bordo della lamiera: {1} si attacca a uno spigolo fra " +
                    "la faccia larga e una faccia di testa larga quanto lo spessore ({2:0.###} mm), tutte " +
                    "e due piane. Scegli uno spigolo del bordo.", uid, what, t);
                return null;
            }
            var n = ApiHelpers.NormalIfPlanar(b.Wide);
            // -1 e' «non letta» (voce Z-C8), e si rifiuta subito sotto.
            double nn = n == null || n.Length < 3 ? -1 : Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
            if (nn <= 1e-9)
            {
                reason = "Non riesco a leggere da che parte guarda la faccia larga dello spigolo «" + uid + "».";
                return null;
            }
            b.Normal = new[] { n[0] / nn, n[1] / nn, n[2] / nn };
            return b;
        }

        /// <summary>
        /// L'ORLO (voce GP18): il bordo ripiegato su se stesso, chiuso o aperto.
        ///
        /// ── MISURATO PRIMA DI SCRIVERLO, 26/9/2026 (`prova0089 orlo`) ────────
        ///
        /// `InsertSheetMetalHem2` lavora sullo spigolo SELEZIONATO e nasce al
        /// primo colpo — nessuno schizzo, a differenza della flangia a spigolo.
        /// Sulla L (T 2, K 0,5), «dentro»:
        ///
        ///   aperto, gioco 1, lungo 10:  +9,712 di sviluppo
        ///   aperto, gioco 2, lungo 10:  +10,283
        ///   aperto, gioco 1, lungo 15:  +14,712
        ///   chiuso, lungo 10 / 15:      +9,199 / +14,199
        ///
        /// L'APERTO e' la formula della flangia con la piega a 180 gradi e il
        /// raggio interno pari a meta' del gioco: (L - R - T) + pi (R + K T) -
        /// (R + T), R = G/2 — tre misure su tre, al millesimo. Il CHIUSO sta
        /// 0,057 sopra la stessa formula con R = 0: come un raggio interno di 0,05
        /// mm, misurato con T 2 soltanto. Per questo il conto del chiuso si dice
        /// accanto alla misura, e non decide niente.
        /// </summary>
        private Outcome Hem(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string error;
            double length;
            if (!RequiredNum(a, "length_mm", out length, out error)) return Failed(error);
            string typ = Str(a, "variant", "closed");
            if (typ != "closed" && typ != "open")
                return Failed("«variant» e' «closed» o «open», non «" + typ + "».");
            double gap = Num(a, "gap_mm");
            if (typ == "open" && gap <= 0)
                return Failed("Un orlo aperto vuole «gap_mm» maggiore di zero: e' lo spazio fra il " +
                               "bordo ripiegato e la lamiera. Senza gioco e' un orlo «closed».");

            var data = SheetMetalData(model);
            if (data == null)
                return Failed("Questo pezzo non e' di lamiera: l'orlo si fa sul bordo di una lamiera che " +
                               "c'e' gia' (fatta con «base_flange»).");
            double t = Units.Mm(data.Thickness), k = data.KFactor;
            double r = typ == "open" ? gap / 2 : 0.05;
            if (length <= r + t + 0.001)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "Un orlo lungo {0:0.###} mm non ha niente di dritto: la piega a 180 gradi e lo spessore " +
                    "ne consumano {1:0.###}. Chiedilo piu' lungo di {1:0.###} mm.", length, r + t));

            string uid = Str(a, "edge_uid", "");
            var border = BorderEdge(a, docState, uid, t, "un orlo", out error);
            if (border == null) return Failed(error);

            double? volumeBefore = VolumeMm3(model);
            model.ClearSelection2(true);
            if (!(border.Edge as IEntity).Select4(false, null))
                return Failed("Non riesco a selezionare lo spigolo «" + uid + "».");
            IFeature hem;
            try
            {
                hem = model.FeatureManager.InsertSheetMetalHem2(
                    typ == "closed" ? (int)swHemTypes_e.swHemTypeClosed : (int)swHemTypes_e.swHemTypeOpen,
                    (int)swHemPositionTypes_e.swHemPositionTypeInside, false,
                    Units.M(length), typ == "open" ? Units.M(gap) : 0, 0, 0, 0,
                    null, true, (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0, true, 0.5, 0, 0) as IFeature;
            }
            catch (Exception ex) { return Failed("L'orlo non e' nato: " + ex.Message); }
            if (hem == null) return Failed("L'orlo non e' nato, e SOLIDWORKS non dice perche'.");
            model.EditRebuild3();

            var hemError = ApiHelpers.RebuildError(hem);   // S11: GetErrorCode2, warnings excluded
            if (hemError != null)
                return Failed("L'orlo «" + hem.Name + "» e' nato in errore (" + hemError + "): ritiralo — " +
                               Sentences.HowToWithdrawIt + ".");

            double? volumeAfter = VolumeMm3(model);
            double extraFlatPattern = (length - r - t) + Math.PI * (r + k * t) - (r + t);
            double expectedVolume = extraFlatPattern * t * Units.Mm(border.LengthM);
            double? added = volumeAfter - volumeBefore;
            if (added.HasValue && added.Value < 0.5 * expectedVolume)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "L'orlo «{0}» e' nell'albero ma ha aggiunto {1:0.##} mm3 invece dei {2:0.##} del conto: " +
                    "non e' l'orlo chiesto. Ritiralo — {3}.", hem.Name, added.Value, expectedVolume, Sentences.HowToWithdrawIt));

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
                    "L'orlo ha aggiunto {0:0.##} mm3 e il conto a mano ne da' {1:0.##}" +
                    (typ == "closed" ? " (per il chiuso il conto usa un raggio interno di 0,05 mm, misurato " +
                                        "con spessore 2)" : "") + ": prima del taglio controlla lo sviluppo.",
                    added.Value, expectedVolume);
            if (flatPatternProblem != null) measurements["flat_pattern_problem"] = flatPatternProblem;   // M24
            return Succeeded(hem, measurements);
        }

        /// <summary>
        /// LA PIEGA DA SCHIZZO (voce GP18): la lamiera si piega lungo una linea
        /// tracciata su una sua faccia.
        ///
        /// ── MISURATA PRIMA DI SCRIVERLA, 26/9/2026 (`prova0089 piega`) ────────
        ///
        /// `InsertSheetMetal3dBend(Angle, UseDefaultRadius, Radius, FlipDir,
        /// BendPos, PCBA)` con lo SCHIZZO della linea selezionato nasce al primo
        /// colpo; la faccia fissa non serve (con o senza, stesso pezzo). Su una
        /// lamiera piatta 100 x 40 x 2 (R 2, K 0,5), linea di traverso a x = 50
        /// sulla faccia alta, 90 gradi, «materiale dentro»: volume invariato
        /// (8000), ingombro 50 x 53,288 x 40 — 46 di piano (50 - R - T), la piega
        /// da 4,712, e 49,288 di lato che sale da y = 2. Lo sviluppo non cambia:
        /// una piega non aggiunge lamiera, la sposta.
        ///
        /// La lamiera si piega verso la parte a cui GUARDA la faccia su cui sta
        /// la linea: per piegare dall'altra parte si traccia la linea sull'altra
        /// faccia. Il lato che resta fermo lo sceglie SOLIDWORKS: la risposta
        /// porta l'ingombro prima e dopo, che lo dice.
        ///
        /// IL JOG no: `IModelDoc2.InsertSheetMetalJog` (Void) non ha fatto
        /// nascere niente in sette varianti di selezione, e fra le definizioni di
        /// lavorazione non c'e' un jog. E' nel TODO, voce GP18.
        /// </summary>
        private Outcome SketchedBend(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var data = SheetMetalData(model);
            if (data == null)
                return Failed("Questo pezzo non e' di lamiera: la piega da schizzo piega una lamiera che " +
                               "c'e' gia' (fatta con «base_flange»).");
            double t = Units.Mm(data.Thickness), r = Units.Mm(data.BendRadius);
            double angle = Num(a, "angle_deg", 90);
            if (angle <= 0 || angle >= 180)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "«angle_deg» va fra 0 e 180 esclusi: {0:0.##} non e' una piega. A 180 la lamiera " +
                    "si richiuderebbe su se stessa — per quello c'e' «hem».", angle));
            var da = Triplet(a, "start_mm");
            var upTo = Triplet(a, "end_mm");
            if (da == null || upTo == null)
                return Failed("«start_mm» e «end_mm» sono i due estremi della linea di piega, punti [x, y, z] in mm " +
                               "sulla faccia (dallo stato del documento).");

            string uidFace = Str(a, "face_uid", "");
            var face = ResolveFace(docState, a, uidFace, out string reason);
            if (face == null) return Failed(reason ?? "Nessuna faccia indicata.");
            double[] plane = null;
            try
            {
                var sup = face.GetSurface() as ISurface;
                if (sup != null && sup.IsPlane()) plane = sup.PlaneParams as double[];
            }
            catch { }
            if (plane == null || plane.Length < 6)
                return Failed("La faccia «" + uidFace + "» non e' piana: la linea di piega si traccia su una faccia piana.");
            // I DUE PUNTI STANNO SULLA FACCIA: distanza dal suo piano entro 5 centesimi.
            Func<double[], double> fromPlane = p =>
                Math.Abs((Units.M(p[0]) - plane[3]) * plane[0] + (Units.M(p[1]) - plane[4]) * plane[1] +
                         (Units.M(p[2]) - plane[5]) * plane[2]) * 1000.0;
            double deviation = Math.Max(fromPlane(da), fromPlane(upTo));
            if (deviation > 0.05)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "La linea di piega non sta sulla faccia «{0}»: un estremo ne dista {1:0.###} mm. I due punti " +
                    "vanno presi sul piano della faccia.", uidFace, deviation));
            double longLine = Math.Sqrt(Math.Pow(upTo[0] - da[0], 2) + Math.Pow(upTo[1] - da[1], 2) + Math.Pow(upTo[2] - da[2], 2));
            if (longLine < 1)
                return Failed("La linea di piega e' lunga meno di un millimetro: i due punti coincidono.");

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
                return Failed("Non riesco a selezionare la faccia «" + uidFace + "».");
            model.SketchManager.InsertSketch(true);
            var sk = model.SketchManager.ActiveSketch as ISketch;
            var math = _sw.GetMathUtility() as IMathUtility;
            if (sk == null || math == null)
                return Failed("Lo schizzo della linea di piega non si e' aperto." + WithoutSketch(model));
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
                return Failed(SketchNotIdentified(sketchesBorn, "Non ritrovo lo schizzo della linea di piega appena fatto.") +
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
            catch (Exception ex) { return Failed("La piega non e' nata: " + ex.Message + WithoutSketch(model)); }
            if (bend == null)
                return Failed("La piega non e' nata, e SOLIDWORKS non dice perche'. Di solito la linea non " +
                               "attraversa la faccia da bordo a bordo." + WithoutSketch(model));
            model.EditRebuild3();

            var bendError = ApiHelpers.RebuildError(bend);   // S11: GetErrorCode2, warnings excluded
            if (bendError != null)
                return Failed("La piega «" + bend.Name + "» e' nata in errore (" + bendError + "): ritirala — " +
                               Sentences.HowToWithdrawIt + ".");

            var boxAfter = PartBoxMm(model);
            bool moved = boxBefore == null || boxAfter == null;
            for (int i = 0; !moved && i < 6; i++) moved = Math.Abs(boxAfter[i] - boxBefore[i]) > 0.01;
            if (!moved)
                return Failed("La piega «" + bend.Name + "» e' nell'albero ma il pezzo ha lo stesso ingombro di " +
                               "prima: non si e' piegato niente. Ritirala — " + Sentences.HowToWithdrawIt + ".");

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
            // UNA PIEGA NON AGGIUNGE LAMIERA: volume e sviluppo restano quelli di
            // prima. Se cambiano, il pezzo non e' quello che si crede.
            if (!volumeAfter.HasValue || !volumeBefore.HasValue) measurements["volume_not_reread"] = NoteVolumeNotReread;
            else if (Math.Abs(volumeAfter.Value - volumeBefore.Value) > 0.001 * volumeBefore.Value)
                measurements["warning"] = string.Format(CultureInfo.InvariantCulture,
                    "Il volume e' passato da {0:0.##} a {1:0.##} mm3: una piega non aggiunge ne' toglie lamiera.",
                    volumeBefore.Value, volumeAfter.Value);
            if (!double.IsNaN(flatPatternBefore) && !double.IsNaN(flatPatternAfter) && Math.Abs(flatPatternAfter - flatPatternBefore) > 0.05)
                measurements["warning_flat_pattern"] = string.Format(CultureInfo.InvariantCulture,
                    "Lo sviluppo e' passato da {0:0.###} a {1:0.###} mm: con una piega da schizzo dovrebbe restare uguale.",
                    flatPatternBefore, flatPatternAfter);
            return Succeeded(bend, measurements);
        }

        private Outcome FlatPatternDxf(IModelDoc2 model, Dictionary<string, object> a)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return Failed("Lo sviluppo si fa su una parte.");
            if (SheetMetalData(model) == null)
                return Failed("Questo pezzo non e' di lamiera: non ha una lavorazione «SheetMetal» da sviluppare.");

            // TRAPPOLA 2 della nota in testa: senza file, «Salva con nome» e una
            // chiamata che non torna.
            string its = model.GetPathName() ?? "";
            if (string.IsNullOrWhiteSpace(its))
                return Failed("Il pezzo non e' mai stato salvato, e su un pezzo senza file " +
                               "SOLIDWORKS apre «Salva con nome» e resta ad aspettare. Salvalo " +
                               "prima («save_document»).");

            string location = Str(a, "path", null);
            if (string.IsNullOrWhiteSpace(location)) location = Path.ChangeExtension(its, ".dxf");
            if (!string.Equals(Path.GetExtension(location), ".dxf", StringComparison.OrdinalIgnoreCase))
                return Failed("«path» deve finire in .dxf.");
            try { if (File.Exists(location)) File.Delete(location); }
            catch (Exception ex) { return Failed("C'e' gia' un DXF li' e non riesco a sostituirlo: " + ex.Message); }

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
            catch (Exception ex) { return Failed("Lo sviluppo non e' uscito: " + ex.Message); }
            finally { putBackTheFront(); }
            if (!ok || !File.Exists(location) || new FileInfo(location).Length == 0)
                return Failed("SOLIDWORKS dice di aver esportato lo sviluppo, ma il file «" + location +
                               "» " + (File.Exists(location) ? "e' vuoto." : "non c'e'."));

            // SI RILEGGE IL DXF: l'ingombro scritto nel file contro lo sviluppo
            // misurato sul pezzo. Un DXF di un'altra cosa si apre uguale.
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
                        "Il DXF e' uscito ({0}) ma il suo lato lungo e' {1:0.###} mm e lo sviluppo " +
                        "del pezzo {2:0.###}: non e' il disegno di questo pezzo, o non e' sviluppato.",
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
        /// Lo spessore, il raggio e il fattore K della lamiera del pezzo, dalla
        /// lavorazione «SheetMetal». Null se il pezzo non e' di lamiera.
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
        /// Il lato lungo del pezzo SVILUPPATO, in mm, e il suo volume. NaN se non
        /// si riesce. Si toglie la soppressione alla lavorazione «FlatPattern»,
        /// si misura, e la si rimette — `SetBendState` non fa niente (nota in
        /// testa, trappola 1).
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
                volume = VolumeMm3(model) ?? double.NaN;   // NaN qui vuol gia' dire «non lo so»
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
        /// L'ingombro in pianta del DXF, in mm, calcolato dalle ENTITA' — non
        /// dall'intestazione. Null se il file non ha entita' leggibili.
        ///
        /// MISURATO il 25/9/2026, alla prima prova: `$EXTMIN/$EXTMAX`
        /// dell'intestazione dicevano 279,4 mm per uno sviluppo di 80,712 —
        /// 279,4 sono 11 pollici, il foglio del modello DXF, non il pezzo. Un
        /// controllo costruito sull'intestazione avrebbe rifiutato ogni DXF
        /// giusto, o peggio accettato quelli sbagliati del formato giusto.
        /// Qui si leggono le coppie (codice, valore) della sezione ENTITIES:
        /// punti 10/20 e 11/21, e per archi e cerchi il centro piu' il raggio.
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
                "«points» vuole almeno due punti [x, y] in mm sul piano: il profilo APERTO " +
                "della lamiera. Una L: [[0,30],[0,0],[50,0]].";
            // UN ELENCO E' UN IEnumerable, non un `object[]`. Misurato al primo
            // giro di banco (25/9/2026): gli argomenti arrivano come elenchi del
            // serializzatore di chi chiama, e un `as object[]` dava null su una L
            // scritta giusta — rifiutata con «vuole almeno due punti». E' la
            // stessa regola di `HasList`, che c'era gia'.
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
                    error = "Ogni punto e' una coppia [x, y] in mm.";
                    return null;
                }
                outside.Add(pair.ToArray());
            }
            if (outside.Count < 2) { error = howItIsWritten; return null; }
            return outside;
        }

        /// <summary>L'angolo di piega al punto b, in radianti (0 = dritto).</summary>
        private static double BendAngle(double[] a, double[] b, double[] c)
        {
            double ux = b[0] - a[0], uy = b[1] - a[1], vx = c[0] - b[0], vy = c[1] - b[1];
            double cos = (ux * vx + uy * vy) / (Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy));
            return Math.Acos(Math.Max(-1, Math.Min(1, cos)));
        }
    }
}
