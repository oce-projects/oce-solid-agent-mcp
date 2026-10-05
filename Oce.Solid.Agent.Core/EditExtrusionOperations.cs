using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// CAMBIARE LA DEFINIZIONE DI UNA LAVORAZIONE FATTA (voce Z-F4), per
    /// l'estrusione e il taglio estruso: un cieco che deve diventare passante,
    /// una piastra che deve crescere. Prima l'unica strada era rifare il pezzo.
    ///
    /// Misurato il 27/9/2026 (`prova0089 zf4`, piastra 120x80x10, foro cieco
    /// Ø8 x 5 = 95748,67 mm³): `IFeature.GetDefinition()` come
    /// `IExtrudeFeatureData2`, `AccessSelections`, `SetEndCondition` /
    /// `SetDepth`, `ModifyDefinition`:
    ///
    ///  * passante (`swEndCondThroughAll`) → 95497,35 (= 96000 − π·16·10);
    ///  * di nuovo cieco profondo 7 → 95648,14 (= 96000 − π·16·7);
    ///  * cieco 15 su una piastra da 10 → nessun errore, di fatto passante;
    ///  * l'estrusione base 10 → 12, e il foro sulla faccia di sopra la segue.
    ///
    /// Ogni cosa si rilegge: la condizione e la profondita' dalla definizione,
    /// il volume dal pezzo, le lavorazioni in errore. Se la modifica ne rompe una
    /// a valle, si rimette la definizione di prima e ci si rifiuta.
    /// </summary>
    public partial class SwOperations
    {
        private static string EndName(int c)
        {
            switch (c)
            {
                case (int)swEndConditions_e.swEndCondBlind: return "blind";
                case (int)swEndConditions_e.swEndCondThroughAll: return "through_all";
                case (int)swEndConditions_e.swEndCondThroughNext: return "fino alla prossima";
                case (int)swEndConditions_e.swEndCondMidPlane: return "mid plane";
                case (int)swEndConditions_e.swEndCondUpToSurface: return "fino alla superficie";
                case (int)swEndConditions_e.swEndCondOffsetFromSurface: return "a distanza dalla superficie";
                case (int)swEndConditions_e.swEndCondUpToVertex: return "fino al vertice";
                case (int)swEndConditions_e.swEndCondUpToBody: return "fino al corpo";
                case (int)swEndConditions_e.swEndCondThroughAllBoth: return "passante nei due versi";
                default: return "condizione " + c;
            }
        }

        /// <summary>
        /// Writes the end condition and the depth into the definition and
        /// applies it: true when ModifyDefinition answers true.
        /// <paramref name="refusedBy"/> names the step that said no when it is
        /// false.
        ///
        /// THE ACCESS IS READ (Z-C21.8, finding L5-5).
        /// IExtrudeFeatureData2~AccessSelections.html: «True if the selections
        /// were accessed successfully, false if not»; its Remarks: it «puts the
        /// model into a rollback state», which ModifyDefinition or
        /// ReleaseSelectionAccess restore. The answer was dropped, and a
        /// refused access went on to write and apply the definition. Now a
        /// refused access stops here, before anything is written, and nothing
        /// is released because nothing was accessed.
        /// </summary>
        private static bool WriteExtrusion(IModelDoc2 model, IFeature f, int fine, double depthM, out string refusedBy)
        {
            refusedBy = null;
            var data = f.GetDefinition() as IExtrudeFeatureData2;
            if (data == null) { refusedBy = "GetDefinition gave no extrusion definition"; return false; }
            bool access;
            try { access = data.AccessSelections(model, null); }
            catch (Exception ex) { refusedBy = "AccessSelections threw: " + ex.Message; return false; }
            if (!access) { refusedBy = "AccessSelections answered false"; return false; }
            bool done = false;
            try
            {
                data.SetEndCondition(true, fine);
                // ANCHE IL PIANO MEDIO HA LA SUA PROFONDITA' (revisione del 27/9/2026):
                // senza, il ripristino rimetteva la condizione e lasciava la misura nuova.
                if (fine == (int)swEndConditions_e.swEndCondBlind || fine == (int)swEndConditions_e.swEndCondMidPlane)
                    data.SetDepth(true, depthM);
                // S11: ModifyDefinition always with GetErrorCode2 (ApiHelpers).
                // The verdict on errors stays with the caller, which rebuilds
                // and counts the features in error (FeaturesInError).
                done = ApiHelpers.ModifyAndVerify(f, data, model).Applied;
                if (!done) refusedBy = "ModifyDefinition answered false";
            }
            catch (Exception ex) { done = false; refusedBy = "writing the definition threw: " + ex.Message; }
            if (!done) { try { data.ReleaseSelectionAccess(); } catch { } }
            return done;
        }

        /// <summary>
        /// The tail of the refusal when the new definition was not applied: the
        /// feature and the part READ BACK, never assumed (Z-C21.8, finding M42).
        /// IFeature~ModifyDefinition.html, Remarks: «this method may leave the
        /// feature in an unpredictable state due to errors». Until library
        /// 0.144.0 the refusal said that nothing had been touched, without
        /// looking.
        /// </summary>
        private static string ExtrusionReadBack(IModelDoc2 model, IFeature f, int endBefore, double depthBeforeM, double? volumeBefore)
        {
            int endNow = -1;
            double depthNowM = double.NaN;
            try
            {
                var now = f.GetDefinition() as IExtrudeFeatureData2;
                if (now != null)
                {
                    endNow = now.GetEndCondition(true);
                    depthNowM = now.GetDepth(true);
                }
            }
            catch { endNow = -1; }
            return ExtrusionReadBackTail(endBefore, Units.Mm(depthBeforeM), volumeBefore,
                                         endNow, Units.Mm(depthNowM), VolumeMm3(model));
        }

        /// <summary>
        /// The words of <see cref="ExtrusionReadBack"/>, kept apart from the
        /// document so that Oce.Solid.Agent.ContractTests can call it without SOLIDWORKS.
        /// «Nothing changed» only when the end condition, the depth (for the
        /// conditions that have one) and the volume read now are the ones read
        /// before; -1 for an end condition and null for a volume mean «not
        /// read», and then the tail says so instead of guessing.
        /// </summary>
        internal static string ExtrusionReadBackTail(
            int endBefore, double depthBeforeMm, double? volumeBefore,
            int endNow, double depthNowMm, double? volumeNow)
        {
            if (endNow < 0 || volumeNow == null || volumeBefore == null)
                return " I could not read it back (end condition " + (endNow < 0 ? "not read" : "«" + EndName(endNow) + "»") +
                       ", volume " + (volumeNow == null ? "not read" : volumeNow.GetValueOrDefault().ToString("0.##", CultureInfo.InvariantCulture) + " mm3") +
                       "): look at the feature before going on.";
            bool withDepth = endBefore == (int)swEndConditions_e.swEndCondBlind ||
                             endBefore == (int)swEndConditions_e.swEndCondMidPlane;
            bool sameEnd = endNow == endBefore;
            bool sameDepth = !withDepth || (!double.IsNaN(depthNowMm) && Math.Abs(depthNowMm - depthBeforeMm) <= 0.001);
            bool sameVolume = Math.Abs(volumeNow.GetValueOrDefault() - volumeBefore.GetValueOrDefault()) < 0.01;
            string volumeText = volumeNow.GetValueOrDefault().ToString("0.##", CultureInfo.InvariantCulture) + " mm3";
            if (sameEnd && sameDepth && sameVolume)
                return " Read back: still «" + EndName(endNow) + "»" +
                       (withDepth ? ", " + depthNowMm.ToString("0.###", CultureInfo.InvariantCulture) + " mm deep" : "") +
                       ", and the volume is the one before (" + volumeText + "): nothing changed.";
            bool depthNowShown = endNow == (int)swEndConditions_e.swEndCondBlind ||
                                 endNow == (int)swEndConditions_e.swEndCondMidPlane;
            return " Read back, it is now «" + EndName(endNow) + "»" +
                   (depthNowShown && !double.IsNaN(depthNowMm) ? ", " + depthNowMm.ToString("0.###", CultureInfo.InvariantCulture) + " mm deep" : "") +
                   ", with a volume of " + volumeText + " instead of " +
                   volumeBefore.GetValueOrDefault().ToString("0.##", CultureInfo.InvariantCulture) + " mm3: the feature is not as it was. " +
                   "Look at it, and undo the step (" + Sentences.HowToWithdrawIt + ").";
        }

        private Outcome EditExtrusion(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (!(model is IPartDoc))
                return Failed("Cambio le lavorazioni di un pezzo. Aprimi il pezzo.");
            string uid = (Str(a, "feature_uid", "") ?? "").Trim();
            if (uid.Length == 0)
                return Failed("Dimmi «feature_uid»: la lavorazione da cambiare, col nome dallo stato («feature»).");
            bool asksThroughAll = a != null && a.ContainsKey("through_all") && a["through_all"] is bool && (bool)a["through_all"];
            bool asksDepth = a != null && a.ContainsKey("depth_mm");
            if (asksThroughAll == asksDepth)
                return Failed("Dimmi O «through_all»: true (un taglio che attraversa tutto) O «depth_mm» (quanto va " +
                               "a fondo, in mm), uno dei due.");
            double depth = double.NaN; string error;
            if (asksDepth)
            {
                if (!RequiredNum(a, "depth_mm", out depth, out error)) return Failed(error);
                if (depth <= 0) return Failed("«depth_mm» deve essere positiva.");
            }
            string reason;
            if (!FeatureIsThatOne(model, uid, a, out reason)) return Failed(reason);
            var f = FindFeature(model, uid);
            if (f == null)
            {
                string ci = FeaturesThatExist(model);
                return Failed("Non trovo «" + uid + "» in questo documento." + (ci.Length > 0 ? " Ci sono: " + ci + "." : ""));
            }
            string typ = "";
            try { typ = f.GetTypeName2(); } catch { }
            // «ICE» NON VUOL DIRE TAGLIO (voce Z-C21.2, rilievo M7).
            //
            // IFeature~GetTypeName2.html: «To get the underlying type of feature
            // of an Instant3D feature (i.e., "ICE"), call IFeature::GetTypeName».
            // «ICE» e' un'estrusione fatta su una faccia, e puo' essere un taglio
            // (cylindrical_cut, rectangular_cut) o un RIALZO
            // (extrude_on_face): DocumentState.cs lo misura dal 10/9/2026. Qui
            // «ICE» valeva «cut» e basta: un rialzo diventava «cut», e
            // «through_all» su un rialzo non veniva rifiutato; un «Cut» vero veniva
            // invece rifiutato. Per l'«ICE» taglio o rialzo lo dice la definizione:
            // IExtrudeFeatureData2~IsBossFeature.html, «True if this extrusion is
            // a boss feature, false if not».
            //
            // SOLO PER L'«ICE». La prima stesura chiedeva IsBossFeature a tutte, e
            // la piastra di base (la PRIMA estrusione del pezzo, «Extrusion»)
            // rispondeva false: «through_all» su una piastra passava il controllo e la
            // piastra diventava «attraversa tutto» (blind_becomes_through,
            // passo 6, rosso nel banco intero del 28/9/2026 su 0.138.0). La pagina
            // non ha Remarks che lo dicano. MISURATO il 28/9/2026 (sonda
            // Checks/ExtrusionType, SOLIDWORKS 29.5.1), prima e dopo
            // AccessSelections, stesse risposte:
            //   base (prima estrusione)   GetTypeName2 Extrusion  GetTypeName Extrusion  IsBossFeature false
            //   rialzo (FeatureExtrusion3) GetTypeName2 ICE       GetTypeName Boss       IsBossFeature true
            //   taglio (FeatureCut4)       GetTypeName2 ICE       GetTypeName Cut        IsBossFeature false
            // Anche le estrusioni fatte da uno schizzo su un piano, dopo la base,
            // sono «ICE» per GetTypeName2: «Extrusion» e' solo la base, che non e'
            // un «boss» e non e' un taglio. Dove il tipo e' gia' «Cut» o
            // «Extrusion» decide il tipo, come prima di Z-C21.2.
            if (typ != "Extrusion" && typ != "Cut" && typ != "ICE")
                return Failed("«" + f.Name + "» e' una lavorazione di tipo «" + typ + "»: so cambiare le estrusioni e i tagli " +
                               "estrusi. Per una quota qualunque c'e' «set_dimension».");
            var before = f.GetDefinition() as IExtrudeFeatureData2;
            if (before == null) return Failed("Non leggo la definizione di «" + f.Name + "».");
            bool cut = typ == "Cut";
            if (typ == "ICE")
            {
                try { cut = !before.IsBossFeature(); }
                catch (Exception ex)
                {
                    // «Nothing touched» needs no reread here (Z-C21.8, finding
                    // M42): up to this line the feature has only been read
                    // (GetDefinition, IsBossFeature), never accessed or modified.
                    return Failed("Non riesco a sapere se «" + f.Name + "» e' un taglio o un rialzo (" + ex.Message +
                                   "): non la tocco. Non ho toccato niente.");
                }
            }
            if (asksThroughAll && !cut)
                return Failed("«through_all» vale per un taglio: un'estrusione che «attraversa tutto» non ha niente da " +
                               "attraversare. Per farla crescere dimmi «depth_mm».");
            int endBefore = before.GetEndCondition(true);
            double depthBefore = before.GetDepth(true);

            // DUE VERSI INDIPENDENTI: SI RIFIUTA (voce Z-C21.2, rilievo L5-1).
            //
            // IExtrudeFeatureData2~BothDirections.html: «Gets or sets whether the
            // extrusion is in both directions». Qui si scrive e si rilegge solo il
            // verso 1 (Forward = true in SetEndCondition/SetDepth: «True for
            // forward direction, false for reverse»). Su un'estrusione a due versi
            // un «through_all» cambiava il verso 1, la rilettura del verso 1 tornava,
            // e la risposta diceva «riuscito» con il verso 2 rimasto cieco. Il
            // ponte non crea mai estrusioni a due versi (Sd: true), quindi arrivano
            // solo da pezzi fatti fuori: rifiutare costa poco e non toglie niente
            // che funzionasse davvero. Il piano medio NON e' «due versi» (e' una
            // condizione del verso 1) e resta ammesso.
            bool twoDirections = false;
            try { twoDirections = before.BothDirections; } catch { }
            if (twoDirections && endBefore != (int)swEndConditions_e.swEndCondMidPlane &&
                endBefore != (int)swEndConditions_e.swEndCondThroughAllBoth)
            {
                int fine2 = -1; double prof2 = double.NaN;
                try { fine2 = before.GetEndCondition(false); prof2 = before.GetDepth(false); } catch { }
                bool conProf2 = fine2 == (int)swEndConditions_e.swEndCondBlind;
                return Failed("«" + f.Name + "» va in due versi indipendenti: il primo «" + EndName(endBefore) +
                               "», il secondo «" + EndName(fine2) + "»" +
                               (conProf2 ? " profondo " + Units.Mm(prof2).ToString("0.###") + " mm" : "") +
                               ". Io cambio e rileggo solo il primo, e ti direi «fatto» con il secondo com'era: " +
                               "non la tocco. Cambiala a mano, oppure dimmi una quota da cambiare con " +
                               "«set_dimension». Non ho toccato niente.");
            }
            // LA CONDIZIONE CHE SI CHIEDE DIPENDE DA QUELLA CHE C'E' (revisione
            // avversariale del 27/9/2026). Una piastra simmetrica da 100 (da -50 a
            // +50) portata a 120 diventava cieca da 0 a +120: non cresceva di 10 per
            // parte, si spostava di 60, e i fori sulle facce la seguivano in posti
            // diversi — e la risposta diceva «riuscito». Il piano medio resta piano
            // medio; «through_all» su un piano medio attraversa nei due versi.
            // Le condizioni con un RIFERIMENTO (fino a una superficie, a un vertice,
            // a un corpo) non si toccano: cambiarle perde il riferimento, e se
            // qualcosa si rompe non saprei rimetterlo.
            bool withReference = endBefore == (int)swEndConditions_e.swEndCondUpToSurface ||
                                  endBefore == (int)swEndConditions_e.swEndCondOffsetFromSurface ||
                                  endBefore == (int)swEndConditions_e.swEndCondUpToVertex ||
                                  endBefore == (int)swEndConditions_e.swEndCondUpToBody ||
                                  endBefore == (int)swEndConditions_e.swEndCondUpToSelection ||
                                  endBefore == (int)swEndConditions_e.swEndCondUpToNext;
            if (withReference)
                return Failed("«" + f.Name + "» finisce «" + EndName(endBefore) + "»: cambiarla perderebbe il " +
                               "riferimento, e se qualcosa si rompesse non saprei rimetterlo. Cambiala a mano, oppure " +
                               "dimmi una quota da cambiare con «set_dimension».");
            bool symmetric = endBefore == (int)swEndConditions_e.swEndCondMidPlane ||
                              endBefore == (int)swEndConditions_e.swEndCondThroughAllBoth;
            int requestedEnd = asksThroughAll
                ? (symmetric ? (int)swEndConditions_e.swEndCondThroughAllBoth : (int)swEndConditions_e.swEndCondThroughAll)
                : (symmetric ? (int)swEndConditions_e.swEndCondMidPlane : (int)swEndConditions_e.swEndCondBlind);
            // LE GUARDIE DI set_dimension: corpi, aperture, smussi, errori.
            var photo = SnapshotPart(model);
            double? volumeBefore = photo.Volume;
            string refusedBy;
            if (!WriteExtrusion(model, f, requestedEnd, asksDepth ? Units.M(depth) : depthBefore, out refusedBy))
            {
                try { model.ForceRebuild3(false); } catch { }
                return Failed("SOLIDWORKS did not take the new definition of «" + f.Name + "» (" + refusedBy + ")." +
                               ExtrusionReadBack(model, f, endBefore, depthBefore, volumeBefore));
            }
            bool rebuilt = false;
            try { rebuilt = model.ForceRebuild3(false); } catch { }
            int errorsAfter = FeaturesInError(model);
            // Senza le aperture: cambia solo la profondita', e un passante che torna
            // cieco le fa calare a ragione (vedi WhatBroke).
            string broken = WhatBroke(model, photo, rebuilt, false);
            if (broken != null)
            {
                bool restored = WriteExtrusion(model, f, endBefore, depthBefore, out _);
                try { model.ForceRebuild3(false); } catch { }
                return Failed("Con la definizione nuova di «" + f.Name + "» " + broken + "." +
                               IsBackAsItWas(model, photo, restored, "la definizione di prima (" + EndName(endBefore) + ")"));
            }
            var after = f.GetDefinition() as IExtrudeFeatureData2;
            int endAfter = after == null ? -1 : after.GetEndCondition(true);
            double depthAfter = after == null ? double.NaN : after.GetDepth(true);
            bool withDepth = endAfter == (int)swEndConditions_e.swEndCondBlind || endAfter == (int)swEndConditions_e.swEndCondMidPlane;
            if (endAfter != requestedEnd || (asksDepth && Math.Abs(Units.Mm(depthAfter) - depth) > 0.001))
                return Failed("SOLIDWORKS dice di aver cambiato «" + f.Name + "», ma rileggendola e' " + EndName(endAfter) +
                               (withDepth ? " profonda " + Units.Mm(depthAfter).ToString("0.###") + " mm" : "") +
                               ": non e' quello che hai chiesto.");
            double? volumeAfter = VolumeMm3(model);
            var measurements = new Dictionary<string, object>
            {
                ["feature"] = f.Name,
                ["kind"] = cut ? "cut" : "extrusion",
                ["end_before"] = EndName(endBefore),
                ["depth_before_mm"] = endBefore == (int)swEndConditions_e.swEndCondBlind || endBefore == (int)swEndConditions_e.swEndCondMidPlane
                    ? (object)Math.Round(Units.Mm(depthBefore), 4) : null,
                ["end_after"] = EndName(endAfter),
                ["depth_after_mm"] = withDepth ? (object)Math.Round(Units.Mm(depthAfter), 4) : null,
                ["volume_before_mm3"] = NumberOrNull(volumeBefore, 2),
                ["volume_after_mm3"] = NumberOrNull(volumeAfter, 2),
                ["features_in_error"] = errorsAfter >= 0 ? (object)errorsAfter : null,
            };
            if (volumeBefore != null && volumeAfter != null && Math.Abs(volumeBefore.Value - volumeAfter.Value) < 0.01)
                measurements["warning"] = "La definizione e' cambiata ma il volume no: la profondita' nuova non cambia il pezzo " +
                                       "(un taglio cieco piu' profondo del materiale e' gia' passante, per esempio).";
            _log("[agente] " + f.Name + ": " + EndName(endBefore) + " -> " + EndName(endAfter));
            return SucceededWithoutCreating(measurements);
        }
    }
}
