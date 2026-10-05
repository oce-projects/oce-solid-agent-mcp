using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LA RIPETIZIONE A RIEMPIMENTO. (voce W1.1, residuo di GP14)
    ///
    /// Una faccia piena di fori: una griglia di sfiato, una piastra forata per
    /// alleggerire, un crivello. La lavorazione si fa una volta, e SOLIDWORKS la
    /// copia a giri concentrici finche' la faccia di contorno la contiene.
    ///
    /// ── LA GUIDA API, CITATA (estratto locale di sldworksapi.chm) ─────────
    ///
    ///   SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureFillPattern.html
    ///     «Obsolete. See IFeatureManager::CreateFeature and the Remarks in
    ///     IFillPatternFeatureData.» Le quindici combinazioni NULL di GP14
    ///     (nota 81u) erano tutte su questo binario morto.
    ///
    ///   SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFillPatternFeatureData.html
    ///     Remarks: «Before calling IFeatureManager::CreateDefinition, you can
    ///     pre-select the entities»; la tabella delle marche:
    ///       contorno (faccia, schizzo, curva piana)   16384
    ///       direzione (spigolo o asse)                    1   («if not selected,
    ///                                                      SOLIDWORKS chooses»)
    ///       posizione del seme (vertice, punto)           3   (facoltativa: senza,
    ///                                                      «the pattern seed instance
    ///                                                      is placed at the center of
    ///                                                      the fill boundary region»)
    ///       feature da ripetere                           4   (facce 5, corpi 6)
    ///     «Or, after calling CreateDefinition, you can explicitly set the
    ///     corresponding properties» — la seconda strada, quella che GP14 aveva
    ///     provato sette volte e che torna NULL (misurato di nuovo sotto).
    ///
    ///   Create_Fill_Pattern_Example_CSharp.htm — l'unico esempio: Hole1 con
    ///     marca 4, uno spigolo con marca 1, una faccia con marca 16384, POI
    ///     CreateDefinition(swFmFillPattern), solo scalari, CreateFeature. Gli
    ///     scalari di questo file sono i suoi, uno per uno (vedi FillScalars).
    ///
    ///   ...IFillPatternFeatureData~NoOfInstances.html: «the number of instances
    ///     per loop or side», valido con LayoutSpacingType = swPatternLayoutInstances.
    ///   ...IFillPatternFeatureData~LoopSpacing.html: «the spacing between the
    ///     centers of instances in adjacent pattern loops», per la disposizione
    ///     circolare.
    ///   ...IFillPatternFeatureData~Margins.html: «distance between the fill
    ///     boundary and the outermost pattern instance; 0 for no margin».
    ///
    /// ── LA SONDA, LANCIATA IL 28/9/2026 (Checks\Fill\Program.cs) ──
    ///
    /// Piastra 100 x 60 x 12, un foro passante Ø8 a 30 mm dal centro (603,2 mm3),
    /// SOLIDWORKS 29.5.1:
    ///
    ///   A  feature 4 + spigolo 1 + faccia 16384 PRIMA di CreateDefinition
    ///        -> «Ripetizione di riempimento1» [APattern], volume 71396,8 -> 62952,2:
    ///           14 fori in piu', -8444,6 = 14 x 603,2 esatto, corpi 1 -> 1, 0 in errore
    ///   C  stesse selezioni DOPO CreateDefinition          -> uguale ad A
    ///   D  la faccia del foro con marca 5 invece della 4   -> uguale ad A
    ///   A2 come A SENZA lo spigolo di direzione            -> NULL
    ///   B  la via storica: niente selezione, proprieta' scritte -> NULL
    ///
    /// Due cose da tenere. **La direzione e' obbligatoria**, anche se la guida la
    /// dice facoltativa: senza, NULL e nessun messaggio. Percio' qui si rifiuta
    /// PRIMA di toccare il pezzo. E **il tipo della feature e' «APattern»**, non
    /// un nome che contenga «Fill»: si controlla quello misurato.
    ///
    /// Qui si fa la ricetta A, nello stesso ordine: lavorazione, direzione,
    /// contorno, poi CreateDefinition.
    ///
    /// ── COSA NON C'E', E PERCHE' ──────────────────────────────────────
    ///
    /// Solo la disposizione CIRCOLARE, con il numero di copie per giro: e' quella
    /// dell'esempio, ed e' l'unica misurata. Quadrata, poligonale e a
    /// perforazione hanno proprieta' loro (InstanceSpacing, StaggerAngle,
    /// PatternLayoutPolygonSides) che nessuna sonda ha visto nascere.
    /// La TABELLA (ITablePatternFeatureData, l'altra meta' di W1.1) resta fuori:
    /// misurata NULL senza il sistema di coordinate con marca 16 che il suo
    /// esempio seleziona.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>Il tipo della feature nata, misurato (GetTypeName2) sulla sonda A.</summary>
        private const string FillType = "APattern";

        /// <summary>
        /// Quanto la direzione puo' uscire dal piano della faccia di contorno:
        /// il seno dell'angolo, 0,01 = circa mezzo grado. Uno spigolo della faccia
        /// ci sta dentro con uno zero di macchina.
        /// </summary>
        private const double FillOffPlane = 0.01;

        private Outcome RepeatFill(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (!(model is IPartDoc))
                return Failed("La ripetizione a riempimento si fa su un pezzo: apri il pezzo e richiedimela.");

            // ── I NUMERI, prima di toccare qualunque cosa ──
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

            // ── LA DIREZIONE E' OBBLIGATORIA, E SI DICE PRIMA ──
            //
            // La guida la dice facoltativa («SOLIDWORKS chooses a direction
            // reference»); la sonda A2 del 28/9/2026 dice NULL senza di lei, e
            // senza nessun messaggio. Un rifiuto qui costa zero; un NULL dopo aver
            // selezionato mezzo pezzo costa un turno a indovinare.
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

            // ── LA FACCIA DI CONTORNO, E DEVE ESSERE PIANA ──
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

            // ── LA FOTOGRAFIA, PRIMA ──
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

                // LA DIREZIONE DEVE STARE NELLA FACCIA. Il riempimento dispone le
                // copie dentro il contorno: uno spigolo che esce dalla faccia (lo
                // spessore della piastra) non ha un verso da seguire la' dentro.
                // Se il verso non si legge non si inventa un rifiuto.
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

                // 2. La lavorazione da ripetere, marca 4 (SelectByID2 BODYFEATURE,
                //    come l'esempio; o l'oggetto del passo citato).
                if (!SelectFeature(model, a, out error, SelectionMarks.FillPatternFeatures))
                    return Failed(error);
                seed = SelectedSeed(model, SelectionMarks.FillPatternFeatures);

                // 3. La direzione di nuovo, marca 1, aggiunta.
                if (!ReselectDirection(model, direction))
                    return Failed("Non sono riuscito a riselezionare la direzione " + statedDirection +
                                   " insieme alla lavorazione. Prova con un altro spigolo dritto della faccia.");

                // 4. Il contorno, marca 16384, aggiunto.
                var handler = model.SelectionManager as ISelectionMgr;
                if (handler == null || !Select(handler, contour, SelectionMarks.FillPatternBoundary))
                    return Failed("Non sono riuscito a selezionare la faccia di contorno" +
                                   (faceUid.Length > 0 ? " «" + faceUid + "»" : "") +
                                   ". Rileggi lo stato del documento e indica di nuovo la faccia.");

                // 5. SOLO ADESSO la definizione, e soltanto scalari.
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

            // ── E ADESSO SI MISURA IL PEZZO ──
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

            // Le guardie comuni (GuardOperations.cs): lavorazioni in errore,
            // ricostruzione, corpi, aperture che escono dal bordo o si fondono.
            // Le aperture si contano: una copia che morde il bordo della faccia o
            // si fonde con un'altra le fa calare.
            string broken = WhatBroke(model, before, rebuilt, true);
            if (broken != null)
                return Failed(
                    "La ripetizione a riempimento e' nata, ma " + broken + ". Prova un «margin_mm» " +
                    "piu' largo, perche' le copie restino lontane dal bordo, o un «pitch_mm» piu' " +
                    "grande, perche' non si tocchino." + RemoveFill(model, feature, volumeBefore));

            // COSA DICHIARA LA FEATURE, riletto con IFeature::GetDefinition
            // come IFillPatternFeatureData — non gli argomenti ripetuti. Non
            // decide niente (decidono il volume, le copie contate e le
            // guardie); un valore che non si rilegge e' null.
            var measurements = new Dictionary<string, object> { ["feature_type"] = typ };
            DeclaredByFill(feature, measurements);

            // «Niente e' cambiato» e' il falso successo: lo prende CompareCopy.
            var stopped = CompareCopy(
                model, volumeBefore, bodiesBefore, facesBefore, measurements, "La ripetizione a riempimento");
            if (stopped != null) return Failed(stopped + RemoveFill(model, feature, volumeBefore));

            // ── LE COPIE, CONTATE DALLE FACCE (come MissingCopies) ──
            //
            // Di un riempimento non si sa in anticipo quante copie entrano: lo
            // decide la forma della faccia. Si contano e si dicono. Zero facce
            // possedute vuol dire zero copie, e quello si rifiuta; un conto che
            // non si divide (copie fuse fra loro) si tace invece di inventarlo.
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
        /// Gli scalari di Create_Fill_Pattern_Example_CSharp.htm, uno per uno, con
        /// i tre che l'operazione espone (passo, copie per giro, margine) al posto
        /// dei suoi 17,024 mm, 10 e 0.
        ///
        /// I valori della forma del seme (Diameter, Diagonal, Dimension, Inner/
        /// OuterRadius, CreateSeedCutPolygonSides) servono solo a
        /// FeaturesToPatternType = CreateSeedCut: con swCreateSeedCutNone non
        /// contano, e si scrivono lo stesso identici all'esempio perche' la
        /// ricetta misurata e' quella intera. Cosi' StaggerAngle (solo per la
        /// perforazione) e InstanceSpacing, che l'esempio mette uguale al passo.
        ///
        /// GeometryPattern resta false: «valid only if PatternElement is
        /// swFeatureFaces» (GeometryPattern.html), e qui si ripete la feature.
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

        /// <summary>Il primo oggetto della selezione, o null.</summary>
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
        /// Il versore di una direzione selezionata: uno spigolo dritto
        /// (ICurve.LineParams: punto 0-2, direzione 3-5, come in DocumentState)
        /// o un asse di riferimento (IRefAxis.GetRefAxisParams: i due estremi).
        /// Null se non si legge: chi chiama allora non rifiuta.
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
        /// La direzione gia' validata, riselezionata con la marca 1 IN AGGIUNTA
        /// alla lavorazione. Uno spigolo con `IEntity.Select4` e un
        /// `SelectData` con `Mark = 1`: la chiamata della sonda
        /// (Checks\Fill, `Mark(selMgr, 1)`), la stessa di
        /// `Select(gestore, faccia, marca)` per la faccia di contorno. Un asse
        /// di riferimento per feature (`Select2` con la marca): la sonda non ne
        /// ha usati, ed e' detto nel catalogo.
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
        /// I quattro scalari che l'operazione chiede, come la feature nata li
        /// DICHIARA (pagine della guida
        /// `IFillPatternFeatureData~PatternLayoutType.html`, `~LoopSpacing.html`
        /// «Distance between loops of pattern instances», `~NoOfInstances.html`
        /// «Number of instances per loop or side», `~Margins.html`), letti da
        /// `IFeature::GetDefinition` (`IFeature~GetDefinition.html`: «query the
        /// feature data object»). La disposizione si scrive per NOME dell'enum
        /// `swPatternLayoutType_e`, mai per cifra. Quello che non si rilegge
        /// resta null: «non lo so» non si riempie con l'argomento.
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

        /// <summary>Il nome in inglese della disposizione, dal nome dell'enum; null se sconosciuta.</summary>
        private static string ArrangementName(int val)
        {
            if (val == (int)swPatternLayoutType_e.swPatternLayoutCircular) return "circular";
            if (val == (int)swPatternLayoutType_e.swPatternLayoutSquare) return "square";
            if (val == (int)swPatternLayoutType_e.swPatternLayoutPolygon) return "polygon";
            if (val == (int)swPatternLayoutType_e.swPatternLayoutPerforation) return "perforation";
            return null;
        }

        /// <summary>
        /// Toglie la ripetizione appena nata quando una guardia la rifiuta, e torna
        /// la coda del messaggio. Non EditDelete (apre una finestra di conferma:
        /// ForbiddenSignaturesTests), ma DeleteSelection2 come `RemoveCut`; e «ha
        /// detto di averla tolta» non basta: si riconta l'albero e si rimisura il
        /// volume.
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
