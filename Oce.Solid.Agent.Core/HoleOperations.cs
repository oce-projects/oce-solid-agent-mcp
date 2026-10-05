using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// La creazione guidata fori: un foro che e' un foro, non un taglio tondo.
    ///
    /// PERCHE' NON BASTA UN TAGLIO CILINDRICO
    /// Fino alla 0.29.1 «foro» voleva dire uno schizzo circolare e un
    /// FeatureCut4. Geometricamente somiglia; come lavorazione non e' la
    /// stessa cosa, e si perdono sette informazioni che qualcuno a valle usa:
    /// il richiamo del foro nel disegno, la filettatura, il diametro di norma,
    /// il cono della punta in fondo a un foro cieco, la possibilita' di
    /// passare da M8 a M10 cambiando una voce, il riconoscimento da parte di
    /// CAM e preventivatori, e l'accoppiamento della viteria in assieme.
    /// Il committente lo ha chiesto per nome: «tutti i fori devono essere
    /// fatti possibilmente con la funzione creazione guidata fori usando tutte
    /// le impostazioni che fornisce».
    ///
    /// LA RICETTA, MISURATA IL 9 SETTEMBRE 2026 SU SOLIDWORKS 2021 SP5
    /// Chiamare HoleWizard5 e basta NON FUNZIONA, e non fallisce: costruisce
    /// un foro da 50.8 mm — due pollici — con il nome giusto nell'albero
    /// («Foro filettato M8»). Verificato sul VOLUME ASPORTATO, non sulle
    /// facce, che ingannano.
    ///
    /// Quello che funziona, in quest'ordine esatto:
    ///
    ///  1. si crea SEMPRE passante. HoleWizard5 applica gia' lo standard
    ///     (dopo la chiamata la definizione dice ISO, M8, filetto 8) ma lascia
    ///     a zero ThruTapDrillDiameter, ed e' quello zero a produrre il foro
    ///     da due pollici;
    ///  2. IWizardHoleFeatureData2.ChangeStandard(norma, elemento, taglia)
    ///     riempie i campi mancanti;
    ///  3. si scrive Type (swWzdHoleTypes_e). E' il campo che comanda davvero:
    ///     con swTapThru (33) un M10 usciva ancora da due pollici, con
    ///     swTapThruCosmeticThread (48) tutte le taglie da M3 a M24 tolgono
    ///     esattamente il volume del preforo di norma;
    ///  4. ModifyDefinition;
    ///  5. se il foro e' CIECO, un SECONDO giro di ModifyDefinition. Non e'
    ///     un capriccio: ChangeStandard riempie solo le varianti «Thru» dei
    ///     campi e lascia a zero le gemelle cieche (HoleDiameter, HoleDepth,
    ///     TapDrillDepth), e un cieco creato in un colpo solo esce come
    ///     capita. Creando passante e poi modificando, i volumi tornano al
    ///     centesimo, cono della punta compreso;
    ///  6. le posizioni si scrivono nello schizzo di posizionamento DENTRO
    ///     EditSketch, con AddToDB acceso e la deduzione spenta. Fuori dalla
    ///     modifica SetCoords restituisce False; con la deduzione accesa i
    ///     punti nuovi si agganciano al primo e l'errore del primo si propaga
    ///     a tutti. Con gli accorgimenti: errore 0.0000 mm su quattro fori.
    ///
    /// Volumi misurati che questa ricetta produce (piastra da 20, foro al
    /// centro): M3 98.2, M4 171.1, M5 277.1, M6 392.7, M8 726.3, M10 1134.9,
    /// M12 1634.3, M14 2261.9, M16 3078.8, M20 4810.6, M24 6927.2 mm3 —
    /// cioe' pi*r^2*spessore sul preforo ISO, esatti.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// I quattro generi di foro che l'operazione sa fare, con il tipo di
        /// elemento ISO da cui prendere le misure e le due varianti di Type.
        ///
        /// I numeri sono swWzdGeneralHoleTypes_e,
        /// swWzdHoleStandardFastenerTypes_e e swWzdHoleTypes_e, letti per
        /// riflessione sull'interop 29.5.1.1.
        /// </summary>
        private struct HoleKind
        {
            public int Generic;      // swWzdGeneralHoleTypes_e
            public int Element;      // swWzdHoleStandardFastenerTypes_e
            public int ThroughAllType;  // swWzdHoleTypes_e
            public int BlindType;     // swWzdHoleTypes_e
            public string Human;
        }

        private static readonly Dictionary<string, HoleKind> HoleKinds =
            new Dictionary<string, HoleKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["threaded"] = new HoleKind
            {
                Generic = (int)swWzdGeneralHoleTypes_e.swWzdTap,
                Element = (int)swWzdHoleStandardFastenerTypes_e.swStandardISOTappedHole,
                ThroughAllType = (int)swWzdHoleTypes_e.swTapThruCosmeticThread,
                BlindType = (int)swWzdHoleTypes_e.swTapBlindCosmeticThread,
                Human = "threaded",
            },
            ["clearance"] = new HoleKind
            {
                Generic = (int)swWzdGeneralHoleTypes_e.swWzdHole,
                Element = (int)swWzdHoleStandardFastenerTypes_e.swStandardISOScrewClearances,
                ThroughAllType = (int)swWzdHoleTypes_e.swHoleThru,
                BlindType = (int)swWzdHoleTypes_e.swHoleBlind,
                Human = "di passaggio per vite",
            },
            ["counterbore"] = new HoleKind
            {
                Generic = (int)swWzdGeneralHoleTypes_e.swWzdCounterBore,
                Element = (int)swWzdHoleStandardFastenerTypes_e.swStandardISOSocketHeadCap,
                ThroughAllType = (int)swWzdHoleTypes_e.swCounterBoreThru,
                BlindType = (int)swWzdHoleTypes_e.swCounterBoreBlind,
                Human = "lamato per vite a testa cilindrica ISO 4762",
            },
            ["countersink"] = new HoleKind
            {
                Generic = (int)swWzdGeneralHoleTypes_e.swWzdCounterSink,
                Element = (int)swWzdHoleStandardFastenerTypes_e.swStandardISOSocketCTSKFlatHead,
                ThroughAllType = (int)swWzdHoleTypes_e.swCounterSinkThru,
                BlindType = (int)swWzdHoleTypes_e.swCounterSinkBlind,
                Human = "svasato per vite a testa svasata piana ISO 10642",
            },
        };

        private const int IsoStandard = (int)swWzdHoleStandards_e.swStandardISO;

        /// <summary>Quanti fori al massimo in una feature sola.</summary>
        private const int MaximumHoles = 60;

        private Outcome HoleWizard(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var face = ResolveFace(docState, a, Str(a, "face_uid", ""), out string reason);
            if (face == null) return Failed(reason);

            double[] axes = FaceAxes(face, out double offsetM, out string error);
            if (axes == null) return Failed(error);

            string kindName = Str(a, "variant", "clearance");
            HoleKind kind;
            if (!HoleKinds.TryGetValue(kindName, out kind))
                return Failed(
                    "Non conosco il tipo di foro «" + kindName + "». " +
                    "So fare: filettato, passaggio, lamato, svasato.");

            string size = (Str(a, "size", "") ?? "").Trim();
            if (size.Length == 0)
                return Failed("Manca la taglia del foro (per esempio «M8»).");

            bool throughAll = Bool(a, "through_all", true);
            double depth = Num(a, "depth_mm", 0);
            if (!throughAll && depth <= 0)
                return Failed(
                    "Un foro cieco vuole la profondita'. Dimmela, oppure di' che " +
                    "e' passante.");

            var positions = HolePositions(a, out error);
            if (positions == null) return Failed(error);

            // L'ANCORA (voce E18): una per posizione, O TUTTE O NESSUNA — un
            // foro senza ancora lascerebbe lo schizzo sottovincolato, e lo
            // schizzo e' uno solo per tutti. Si legge PRIMA di creare niente.
            List<Anchoring> anchors;
            if (!ReadHoleAnchors(a, docState, face, axes, offsetM, positions, out anchors, out error))
                return Failed(error);
            // IL VOLUME SI LEGGE SEMPRE (voce Z-C21.2, rilievo L4-3), non solo
            // con le ancore: e' il controllo con cui il commento in testa dice di
            // aver scoperto il foro da due pollici, e senza il foro in errore
            // passava come «positions_not_checked».
            double? volumeBefore = VolumeMm3(model);

            // ── 1. si crea PASSANTE, sempre ──────────────────────────────
            model.ClearSelection2(true);
            if (!SelectFace(face))
                return Failed("Non sono riuscito a selezionare la faccia.");

            IFeature feature;
            try
            {
                feature = model.FeatureManager.HoleWizard5(
                    GenericHoleType: kind.Generic,
                    StandardIndex: IsoStandard,
                    FastenerTypeIndex: kind.Element,
                    SSize: size,
                    EndType: (short)swEndConditions_e.swEndCondThroughAll,
                    Diameter: 0, Depth: 0, Length: 0,
                    Value1: 0, Value2: 0, Value3: 0, Value4: 0, Value5: 0, Value6: 0,
                    Value7: 0, Value8: 0, Value9: 0, Value10: 0, Value11: 0, Value12: 0,
                    ThreadClass: "", RevDir: false,
                    FeatureScope: false, AutoSelect: true,
                    AssemblyFeatureScope: false, AutoSelectComponents: false,
                    PropagateFeatureToParts: false) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("La creazione guidata fori non e' riuscita: " + ex.Message);
            }

            if (feature == null)
                return Failed(
                    "SolidWorks non ha accettato la taglia «" + size + "» per un foro " +
                    kind.Human + " secondo ISO. Le taglie metriche si scrivono " +
                    "«M3», «M4», «M5», «M6», «M8», «M10», «M12», «M14», «M16», «M20», " +
                    "«M24» — con la M maiuscola e senza spazi.");

            // ── 2-4. lo standard, il tipo, la modifica ───────────────────
            if (!FixUpTheHole(model, feature, kind, size, kind.ThroughAllType,
                               false, 0, out error))
                return Failed(error);
            // IFeature~ModifyDefinition.html, Remarks: «For some feature
            // modifications, this method applies the new properties, returns
            // true, but results in a new rebuild error on the feature. [...]
            // After calling this method, call IFeature::GetErrorCode2 to
            // ascertain whether the feature has rebuild errors» (rilievo L4-3).
            //
            // SI LEGGE QUI, MA SI DECIDE AL PUNTO 6a (revisione di Z-C21.2). A
            // questo punto il foro sta ancora dove l'ha messo HoleWizard5 — la
            // faccia e' selezionata senza un punto (SelectFace) — e su una
            // faccia che non contiene quel punto provvisorio il foro puo' essere
            // in errore adesso e sano dopo PositionHoles. Rifiutare qui vorrebbe
            // dire rifiutare un foro buono. Il codice di adesso si tiene e si
            // riporta se alla fine il foro resta in errore.
            string intermediateErrors = null;
            string inError = HoleInError(feature);
            if (inError != null) intermediateErrors = "dopo la norma ISO: " + inError;

            // ── 5. il cieco, in un secondo giro ──────────────────────────
            if (!throughAll &&
                !FixUpTheHole(model, feature, kind, size, kind.BlindType,
                               true, depth, out error))
                return Failed(error);
            inError = throughAll ? null : HoleInError(feature);
            if (inError != null)
                intermediateErrors = (intermediateErrors == null ? "" : intermediateErrors + "; ") +
                                  "portato a cieco: " + inError;

            // ── 6. le posizioni ──────────────────────────────────────────
            if (!PositionHoles(model, feature, axes, offsetM, positions, out error))
                return Failed(error);

            try { model.EditRebuild3(); } catch { }

            // ── 6a. A FORI POSIZIONATI: IN ERRORE? HA TOLTO QUALCOSA? ─────
            //
            // (rilievo L4-3.) Un foro spostato dove non c'e' materiale e' una
            // feature che ricostruisce in errore, o che non toglie niente: in
            // tutti e due i casi non ci sono cilindri da trovare, il giudizio
            // delle posizioni diceva «non verificabile», e l'operazione
            // rispondeva riuscita. GetErrorCode2 prima, il volume poi — il
            // progetto ha misurato che GetErrorCode2 da solo puo' tacere su un
            // difetto vero (voce MissingCopies, SwOperations.cs).
            inError = HoleInError(feature);
            if (inError != null)
                return Failed("Il foro guidato non e' venuto: nelle posizioni chieste «" + feature.Name +
                               "» e' in errore (" + inError + "). Controlla che ogni posizione stia sulla " +
                               "faccia." +
                               (intermediateErrors == null ? "" : " Era gia' in errore prima di posizionarlo (" +
                                                               intermediateErrors + ").") +
                               RemoveCut(model, feature, volumeBefore));
            double? drilledVolume = VolumeMm3(model);
            if (volumeBefore.HasValue && drilledVolume.HasValue && drilledVolume.Value > volumeBefore.Value - 0.01)
                return Failed(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Il foro guidato non e' venuto: «{0}» e' nell'albero ma il pezzo pesa come prima " +
                    "({1:0.##} mm3), cioe' non ha tolto materiale. Controlla che ogni posizione stia " +
                    "sulla faccia.", feature.Name, volumeBefore.Value) +
                    RemoveCut(model, feature, volumeBefore));

            var measurements = new Dictionary<string, object>
            {
                ["kind"] = kind.Human,
                ["size"] = size,
                ["holes"] = positions.Count,
                ["through_all"] = throughAll,
            };

            // ── 6b. l'ancora, a fori posizionati ─────────────────────────
            //
            // THE SIZE OF THE HOLE IS READ FIRST, BEFORE THE ANCHOR.
            // The diameters do not change with the anchor, and the fit check
            // below needs them: it runs before AnchorHoles, so that a hole that
            // does not fit is refused on its size, and not on whatever the
            // anchor makes of the edges it has just cut (Z-C21.2, finding N50).
            var definition = feature.GetDefinition() as IWizardHoleFeatureData2;
            double holeDiameter = 0;
            // THE WIDEST CUT (N50): the anchor must fit the counterbore or the
            // countersink too, not only the drilled hole. The two diameters are
            // read only for their own type of hole, as their pages say:
            // IWizardHoleFeatureData2~CounterBoreDiameter.html, «This property
            // is relevant only for swCounterBored and swCounterBoredDrilled
            // holes»; ~CounterSinkDiameter.html, «This property is relevant only
            // for swCounterSunk and swCounterSunkDrilled holes».
            double widestDiameter = 0;
            string widestCut = "hole";
            if (definition != null)
            {
                holeDiameter = Math.Max(
                    Math.Max(definition.ThruTapDrillDiameter, definition.TapDrillDiameter),
                    Math.Max(definition.ThruHoleDiameter, definition.HoleDiameter)) * 1000.0;
                if (holeDiameter > 0)
                    measurements["diameter_machined_mm"] = Math.Round(holeDiameter, 2);
                if (definition.ThreadDiameter > 0)
                    measurements["thread_mm"] = Math.Round(definition.ThreadDiameter * 1000, 2);
                if (definition.CounterBoreDiameter > 0)
                    measurements["counterbore_mm"] =
                        Math.Round(definition.CounterBoreDiameter * 1000, 2) + "x" +
                        Math.Round(definition.CounterBoreDepth * 1000, 2);
                widestDiameter = holeDiameter;
                if (kind.Generic == (int)swWzdGeneralHoleTypes_e.swWzdCounterBore &&
                    definition.CounterBoreDiameter > 0 &&
                    definition.CounterBoreDiameter * 1000.0 >= widestDiameter)
                {
                    widestDiameter = definition.CounterBoreDiameter * 1000.0;
                    widestCut = "counterbore";
                }
                if (kind.Generic == (int)swWzdGeneralHoleTypes_e.swWzdCounterSink &&
                    definition.CounterSinkDiameter > 0 &&
                    definition.CounterSinkDiameter * 1000.0 >= widestDiameter)
                {
                    widestDiameter = definition.CounterSinkDiameter * 1000.0;
                    widestCut = "countersink";
                }
            }

            // THE ANCHOR WITH THE REAL RADIUS: distances and centred axes.
            if (anchors != null)
            {
                if (widestDiameter > 0)
                {
                    string bypass = BypassWithRadius(anchors, axes, widestDiameter / 2, widestCut);
                    if (bypass != null) return Failed(bypass + RemoveCut(model, feature, volumeBefore));
                }
                else
                    // A check that could not run says so (N50): the anchor was
                    // read with a zero radius, and nothing else has looked.
                    measurements["anchor_fit_not_checked"] = "SOLIDWORKS does not declare a diameter for this hole: " +
                                                       "I could not check that it fits its anchor edges";
            }

            if (anchors != null)
            {
                if (!AnchorHoles(model, feature, axes, offsetM, positions, anchors, measurements, out error))
                    return Failed(error + RemoveCut(model, feature, volumeBefore));
                try { model.EditRebuild3(); } catch { }
            }

            // ── 7. si controlla che i fori siano dove sono stati chiesti ──
            if (!throughAll) measurements["depth_mm"] = depth;

            // TRE ESITI, NON DUE. «Non ho potuto guardare» e' una risposta
            // diversa da «ho guardato e va bene», e finche' erano la stessa il
            // banco stampava «posizioni verificate» su un campo che non voleva
            // dire niente. Vedi PositionsVerdict per com'e' andata.
            //
            // Non verificabile NON e' un rifiuto: la lavorazione e' riuscita, e'
            // il controllo che non c'e' stato. Si dice, e si va avanti.
            // The movement test of the anchor (its fit was checked at 6b).
            if (anchors != null)
            {
                foreach (var anc in anchors)
                {
                    string doesNotHold = MovementTest(model, anc, feature);
                    if (doesNotHold != null) return Failed(doesNotHold + RemoveCut(model, feature, volumeBefore));
                }
            }

            string cannot;
            var foundAxes = HoleAxes(feature, holeDiameter, out cannot);

            var outcome = PositionsVerdict.Judge(
                foundAxes, cannot, axes, offsetM, positions, out string wherePositions);

            if (outcome == PositionsOutcome.Wrong) return Failed(wherePositions);
            if (outcome == PositionsOutcome.Verified) measurements["positions_checked"] = true;
            else measurements["positions_not_checked"] = wherePositions;

            return Succeeded(feature, measurements, FaceNormal(face));
        }

        /// <summary>
        /// Il foro e' in errore di ricostruzione? La frase, o null se e' sano o
        /// se il codice e' solo un avviso (IFeature~GetErrorCode2.html:
        /// «IsWarning: True if the error is a warning, false if not»). Un
        /// codice che non si legge non diventa un errore: decide il volume
        /// (voce Z-C21.2, rilievo L4-3).
        /// </summary>
        private static string HoleInError(IFeature hole)
        {
            return ApiHelpers.RebuildError(hole);
        }

        /// <summary>
        /// Scrive lo standard e il tipo sulla definizione, e la applica.
        ///
        /// Chiamata due volte per un foro cieco: la prima per fissare le
        /// misure di norma sul passante, la seconda per portarlo a cieco. Il
        /// secondo giro riempie a mano le gemelle cieche dei campi «Thru»,
        /// che ChangeStandard non tocca.
        /// </summary>
        private bool FixUpTheHole(
            IModelDoc2 model, IFeature feature, HoleKind kind, string size,
            int typ, bool blind, double depthMm, out string error)
        {
            error = null;
            IWizardHoleFeatureData2 d;
            try { d = feature.GetDefinition() as IWizardHoleFeatureData2; }
            catch (Exception ex) { error = "Non riesco a leggere il foro: " + ex.Message; return false; }
            if (d == null) { error = "Non riesco a leggere la definizione del foro."; return false; }

            bool access = false;
            try { access = d.AccessSelections(model, null); }
            catch (Exception ex) { error = "Non riesco ad aprire il foro: " + ex.Message; return false; }
            if (!access) { error = "SolidWorks non mi lascia modificare il foro appena creato."; return false; }

            try
            {
                // TYPE AFTER ChangeStandard, and the guide's rule on the order does
                // not apply: IWizardHoleFeatureData2~ChangeStandard.html, Remarks,
                // «If changing the standard requires you to change the type, then
                // you must call IWizardHoleFeatureData2::Type before calling
                // IWizardHoleFeatureData2::ChangeStandard». Here the standard does
                // not change: HoleWizard5 already created the hole with
                // StandardIndex IsoStandard, and this passes the same IsoStandard. The
                // Type written below is the measured type correction of step 3 in
                // the note at the top, not an effect of a change of standard
                // (audit finding L4-1, not real).
                if (!blind && !d.ChangeStandard(IsoStandard, kind.Element, size))
                {
                    try { d.ReleaseSelectionAccess(); } catch { }
                    error =
                        "La taglia «" + size + "» non c'e' nella tabella ISO dei fori " +
                        kind.Human + ".";
                    return false;
                }
                d.Type = typ;

                if (blind)
                {
                    double p = depthMm / 1000.0;
                    d.EndCondition = (int)swEndConditions_e.swEndCondBlind;
                    d.Depth = p;
                    // Le gemelle cieche: senza, il foro esce come capita.
                    if (d.HoleDiameter <= 0 && d.ThruHoleDiameter > 0)
                        d.HoleDiameter = d.ThruHoleDiameter;
                    if (d.HoleDiameter > 0) d.HoleDepth = p;
                    if (d.TapDrillDiameter <= 0 && d.ThruTapDrillDiameter > 0)
                        d.TapDrillDiameter = d.ThruTapDrillDiameter;
                    if (d.TapDrillDiameter > 0)
                    {
                        d.TapDrillDepth = p;
                        // Il filetto non arriva in fondo al preforo: e' cosi'
                        // che si fila un foro cieco, e la punta lascia il cono.
                        d.ThreadDepth = p * 0.7;
                        d.ThreadEndCondition =
                            (int)swWzdHoleThreadEndCondition_e.swEndThreadTypeBLIND;
                    }
                }
            }
            catch (Exception ex)
            {
                try { d.ReleaseSelectionAccess(); } catch { }
                error = "Non riesco a impostare il foro: " + ex.Message;
                return false;
            }

            bool applied;
            // S11 (ApiHelpers.ModifyAndVerify); the error code is judged by the
            // caller at step 6a, once the holes are positioned (HoleInError).
            try { applied = ApiHelpers.ModifyAndVerify(feature, d, model).Applied; }
            catch (Exception ex)
            {
                try { d.ReleaseSelectionAccess(); } catch { }
                error = "La modifica del foro non e' riuscita: " + ex.Message;
                return false;
            }
            if (!applied)
            {
                try { d.ReleaseSelectionAccess(); } catch { }
                error = "SolidWorks non ha applicato le misure di norma al foro.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Le ancore dei fori (voce E18), una per posizione nello stesso ordine.
        /// `anchors` resta null se nessuna posizione ne ha una (il vecchio modo);
        /// false col motivo se qualcuna ce l'ha e qualcuna no, o se un'ancora e'
        /// sbagliata. Un foro solo con x_mm/y_mm in cima prende `anchor` in cima.
        /// </summary>
        private bool ReadHoleAnchors(
            Dictionary<string, object> a, DocumentState docState, IFace2 face, double[] axes, double offsetM,
            List<double[]> positions, out List<Anchoring> anchors, out string error)
        {
            anchors = null;
            error = null;
            var raw = new List<object>();
            object itemList;
            if (a.TryGetValue("positions", out itemList) && itemList is System.Collections.IEnumerable)
                foreach (var entry in (System.Collections.IEnumerable)itemList)
                {
                    var m = entry as Dictionary<string, object>;
                    object anc = null;
                    if (m != null) m.TryGetValue("anchor", out anc);
                    raw.Add(anc);
                }
            else
            {
                object anc;
                a.TryGetValue("anchor", out anc);
                raw.Add(anc);
            }
            int withAnchor = raw.Count(g => g != null);
            if (withAnchor == 0) return true;
            if (withAnchor != positions.Count)
            {
                error = "L'ancora dei fori va data a TUTTE le posizioni o a nessuna: qui ce l'hanno " +
                         withAnchor + " su " + positions.Count + ". I fori stanno in uno schizzo solo, e un " +
                         "punto senza ancora lo lascerebbe libero di muoversi alla prima modifica.";
                return false;
            }
            anchors = new List<Anchoring>();
            object signatures;
            a.TryGetValue("_signatures", out signatures);
            for (int i = 0; i < positions.Count; i++)
            {
                var its = new Dictionary<string, object> { ["anchor"] = raw[i] };
                if (signatures != null) its["_signatures"] = signatures;
                string reason;
                // Il raggio non si sa ancora (lo decide la tabella): 0 qui, e il
                // controllo dello scavalcamento si rifa' a foro nato.
                var anc = ReadAnchor(its, docState, face, axes, offsetM, positions[i][0], positions[i][1], 0, 0, out reason);
                if (anc == null) { error = "Foro " + (i + 1) + ": " + reason; anchors = null; return false; }
                anchors.Add(anc);
            }
            return true;
        }

        /// <summary>Le posizioni chieste, in mm del modello sui due assi della faccia.</summary>
        private static List<double[]> HolePositions(
            Dictionary<string, object> a, out string error)
        {
            error = null;
            var outside = new List<double[]>();

            object raw;
            if (a != null && a.TryGetValue("positions", out raw) && raw != null)
            {
                var itemList = raw as System.Collections.IEnumerable;
                if (itemList == null) { error = "Le posizioni dei fori non sono leggibili."; return null; }
                int i = 0;
                foreach (var entry in itemList)
                {
                    i++;
                    var m = entry as Dictionary<string, object>;
                    if (m == null) { error = "La posizione " + i + " non e' leggibile."; return null; }
                    double x, y;
                    if (!RequiredNum(m, "x_mm", out x, out error)) return null;
                    if (!RequiredNum(m, "y_mm", out y, out error)) return null;
                    outside.Add(new[] { x, y });
                }
            }
            else
            {
                // Un foro solo si puo' chiedere anche con x_mm/y_mm in piano,
                // come fa cylindrical_cut: e' la forma che il modello
                // conosce gia'.
                double x, y;
                if (!RequiredNum(a, "x_mm", out x, out error)) return null;
                if (!RequiredNum(a, "y_mm", out y, out error)) return null;
                outside.Add(new[] { x, y });
            }

            if (outside.Count == 0) { error = "Nessuna posizione: non c'e' foro da fare."; return null; }
            if (outside.Count > MaximumHoles)
            {
                error = "Sono " + outside.Count + " fori in una feature sola: piu' di " +
                         MaximumHoles + " non li faccio, meglio dividerli.";
                return null;
            }

            // Due fori nello stesso punto sono un errore di chi ha scritto il
            // piano, e SolidWorks li accetterebbe sovrapponendoli.
            for (int i = 0; i < outside.Count; i++)
                for (int j = i + 1; j < outside.Count; j++)
                    if (Math.Abs(outside[i][0] - outside[j][0]) < 0.01 &&
                        Math.Abs(outside[i][1] - outside[j][1]) < 0.01)
                    {
                        error = "I fori " + (i + 1) + " e " + (j + 1) +
                                 " sono nello stesso punto.";
                        return null;
                    }
            return outside;
        }

        /// <summary>
        /// Sposta il punto che SolidWorks ha messo da solo e ne aggiunge uno
        /// per ogni altro foro.
        ///
        /// Tre accorgimenti, tutti e tre necessari e tutti e tre misurati:
        ///   - si sta DENTRO EditSketch: fuori, SetCoords torna False;
        ///   - AddToDB acceso: la geometria entra alle coordinate scritte
        ///     invece che dove il motore di aggancio la porta;
        ///   - deduzione spenta: altrimenti i punti nuovi prendono relazioni
        ///     di allineamento col primo, e l'errore del primo diventa
        ///     l'errore di tutti.
        /// </summary>
        private bool PositionHoles(
            IModelDoc2 model, IFeature feature, double[] axes, double offsetM,
            List<double[]> positions, out string error)
        {
            error = null;
            var featSketch = PositioningSketch(feature);
            if (featSketch == null)
            {
                error = "Non trovo lo schizzo che tiene la posizione dei fori.";
                return false;
            }
            var sketch = featSketch.GetSpecificFeature2() as ISketch;
            if (sketch == null) { error = "Lo schizzo dei fori non e' leggibile."; return false; }

            IMathTransform transformation;
            IMathUtility math;
            try
            {
                transformation = sketch.ModelToSketchTransform;
                math = _sw.GetMathUtility() as IMathUtility;
            }
            catch (Exception ex)
            {
                error = "Non riesco a leggere l'orientamento dello schizzo: " + ex.Message;
                return false;
            }
            if (transformation == null || math == null)
            {
                error = "SolidWorks non mi da' l'orientamento dello schizzo dei fori.";
                return false;
            }

            var handler = model.SketchManager;
            bool addToDb = handler.AddToDB;
            bool deduction = handler.AutoInference;
            bool show = handler.DisplayWhenAdded;
            bool beingEdited = false;
            try
            {
                model.ClearSelection2(true);
                if (!featSketch.Select2(false, 0))
                {
                    error = "Non riesco a selezionare lo schizzo dei fori.";
                    return false;
                }
                model.EditSketch();
                beingEdited = true;
                handler.AddToDB = true;
                handler.AutoInference = false;
                handler.DisplayWhenAdded = false;

                var points = (sketch.GetSketchPoints2() as object[]) ?? new object[0];
                if (points.Length == 0)
                {
                    error = "Lo schizzo dei fori e' vuoto: non so dove metterli.";
                    return false;
                }

                var first = InSketch(math, transformation, axes, offsetM, positions[0]);
                if (first == null || !(points[0] as ISketchPoint).SetCoords(first[0], first[1], 0))
                {
                    error = "Non sono riuscito a mettere il primo foro dove chiesto.";
                    return false;
                }

                for (int i = 1; i < positions.Count; i++)
                {
                    var p = InSketch(math, transformation, axes, offsetM, positions[i]);
                    if (p == null || handler.CreatePoint(p[0], p[1], 0) == null)
                    {
                        error = "Non sono riuscito ad aggiungere il foro " + (i + 1) + ".";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "Il posizionamento dei fori non e' riuscito: " + ex.Message;
                return false;
            }
            finally
            {
                handler.AddToDB = addToDb;
                handler.AutoInference = deduction;
                handler.DisplayWhenAdded = show;
                if (beingEdited) { try { handler.InsertSketch(true); } catch { } }
                try { model.ClearSelection2(true); } catch { }
            }
        }

        private static double[] InSketch(
            IMathUtility math, IMathTransform transformation,
            double[] axes, double offsetM, double[] positionMm)
        {
            try
            {
                var inModel = new double[3];
                inModel[(int)axes[0]] = Units.M(positionMm[0]);
                inModel[(int)axes[1]] = Units.M(positionMm[1]);
                inModel[(int)axes[2]] = offsetM;
                var point = math.CreatePoint(inModel) as IMathPoint;
                var inside = point?.MultiplyTransform(transformation) as IMathPoint;
                var v = inside?.ArrayData as double[];
                return (v == null || v.Length < 2) ? null : v;
            }
            catch { return null; }
        }

        /// <summary>
        /// Fra le sotto-feature di un foro guidato ci sono due schizzi: quello
        /// del profilo di rivoluzione, pieno di segmenti, e quello di
        /// posizionamento, fatto di soli punti. Si prende quello con meno
        /// segmenti, che e' un invariante e non un nome localizzato.
        /// </summary>
        private static IFeature PositioningSketch(IFeature hole)
        {
            IFeature best = null;
            int less = int.MaxValue;
            try
            {
                IFeature s = hole.IGetFirstSubFeature();
                while (s != null)
                {
                    var sk = s.GetSpecificFeature2() as ISketch;
                    if (sk != null)
                    {
                        var seg = sk.GetSketchSegments() as object[];
                        // ZERO-E-ZERO: uno schizzo di soli punti non ha segmenti, e
                        // null e' proprio lo schizzo di posizionamento che si cerca.
                        int n = seg == null ? 0 : seg.Length;
                        if (n < less) { less = n; best = s; }
                    }
                    s = s.IGetNextSubFeature();
                }
            }
            catch { }
            return best;
        }

        /// <summary>
        /// Raccoglie gli assi delle facce cilindriche del diametro atteso.
        ///
        /// SI GUARDA IL PEZZO, NON LA DEFINIZIONE. La definizione l'abbiamo
        /// scritta noi: rileggerla direbbe soltanto che l'abbiamo scritta.
        ///
        /// QUESTA FUNZIONE NON GIUDICA, RACCOGLIE. E' l'unica meta' che ha
        /// bisogno di SOLIDWORKS; il giudizio sta in
        /// <see cref="PositionsVerdict"/>, che si prova a tavolino.
        ///
        /// Quando non riesce a raccogliere, lo DICE in <paramref name="cannot"/>
        /// invece di tornare una lista vuota che somiglia a «nessun foro
        /// fuori posto». La versione precedente tornava `null` — cioe' «tutto
        /// a posto» — in quattro strade in cui non aveva guardato niente, e chi
        /// la chiamava scriveva `positions_checked = true` senza condizione.
        /// </summary>
        private List<double[]> HoleAxes(
            IFeature feature, double diameterMm, out string cannot)
        {
            cannot = null;

            if (diameterMm <= 0)
            {
                cannot = "SOLIDWORKS non dichiara un diametro per questa lavorazione: " +
                           "non ho un termine di confronto";
                return null;
            }

            var foundAxes = new List<double[]>();
            try
            {
                var faces = feature.GetFaces() as object[];
                if (faces == null)
                {
                    cannot = "la lavorazione non espone le sue facce";
                    return null;
                }
                foreach (var o in faces)
                {
                    var surface = (o as IFace2)?.IGetSurface();
                    if (surface == null || !surface.IsCylinder()) continue;
                    var par = surface.CylinderParams as double[];
                    if (par == null || par.Length < 7) continue;
                    if (Math.Abs(par[6] * 2000.0 - diameterMm) > 0.05) continue;
                    foundAxes.Add(par);
                }
            }
            catch (Exception ex)
            {
                cannot = "non sono riuscito a leggere le facce della lavorazione: " + ex.Message;
                return null;
            }

            return foundAxes;
        }
    }
}
