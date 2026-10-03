using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
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
    public partial class OperazioniSw
    {
        private static string NomeDellaFine(int c)
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
        private static bool ScriviEstrusione(IModelDoc2 modello, IFeature f, int fine, double profonditaM, out string refusedBy)
        {
            refusedBy = null;
            var dati = f.GetDefinition() as IExtrudeFeatureData2;
            if (dati == null) { refusedBy = "GetDefinition gave no extrusion definition"; return false; }
            bool access;
            try { access = dati.AccessSelections(modello, null); }
            catch (Exception ex) { refusedBy = "AccessSelections threw: " + ex.Message; return false; }
            if (!access) { refusedBy = "AccessSelections answered false"; return false; }
            bool fatto = false;
            try
            {
                dati.SetEndCondition(true, fine);
                // ANCHE IL PIANO MEDIO HA LA SUA PROFONDITA' (revisione del 27/9/2026):
                // senza, il ripristino rimetteva la condizione e lasciava la misura nuova.
                if (fine == (int)swEndConditions_e.swEndCondBlind || fine == (int)swEndConditions_e.swEndCondMidPlane)
                    dati.SetDepth(true, profonditaM);
                // S11: ModifyDefinition always with GetErrorCode2 (ApiHelpers).
                // The verdict on errors stays with the caller, which rebuilds
                // and counts the features in error (LavorazioniInErrore).
                fatto = ApiHelpers.ModifyAndVerify(f, dati, modello).Applied;
                if (!fatto) refusedBy = "ModifyDefinition answered false";
            }
            catch (Exception ex) { fatto = false; refusedBy = "writing the definition threw: " + ex.Message; }
            if (!fatto) { try { dati.ReleaseSelectionAccess(); } catch { } }
            return fatto;
        }

        /// <summary>
        /// The tail of the refusal when the new definition was not applied: the
        /// feature and the part READ BACK, never assumed (Z-C21.8, finding M42).
        /// IFeature~ModifyDefinition.html, Remarks: «this method may leave the
        /// feature in an unpredictable state due to errors». Until library
        /// 0.144.0 the refusal said that nothing had been touched, without
        /// looking.
        /// </summary>
        private static string ExtrusionReadBack(IModelDoc2 modello, IFeature f, int endBefore, double depthBeforeM, double? volumeBefore)
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
            return ExtrusionReadBackTail(endBefore, Unita.Mm(depthBeforeM), volumeBefore,
                                         endNow, Unita.Mm(depthNowM), VolumeMm3(modello));
        }

        /// <summary>
        /// The words of <see cref="ExtrusionReadBack"/>, kept apart from the
        /// document so that Prove.Contratti can call it without SOLIDWORKS.
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
                return " I could not read it back (end condition " + (endNow < 0 ? "not read" : "«" + NomeDellaFine(endNow) + "»") +
                       ", volume " + (volumeNow == null ? "not read" : volumeNow.GetValueOrDefault().ToString("0.##", CultureInfo.InvariantCulture) + " mm3") +
                       "): look at the feature before going on.";
            bool withDepth = endBefore == (int)swEndConditions_e.swEndCondBlind ||
                             endBefore == (int)swEndConditions_e.swEndCondMidPlane;
            bool sameEnd = endNow == endBefore;
            bool sameDepth = !withDepth || (!double.IsNaN(depthNowMm) && Math.Abs(depthNowMm - depthBeforeMm) <= 0.001);
            bool sameVolume = Math.Abs(volumeNow.GetValueOrDefault() - volumeBefore.GetValueOrDefault()) < 0.01;
            string volumeText = volumeNow.GetValueOrDefault().ToString("0.##", CultureInfo.InvariantCulture) + " mm3";
            if (sameEnd && sameDepth && sameVolume)
                return " Read back: still «" + NomeDellaFine(endNow) + "»" +
                       (withDepth ? ", " + depthNowMm.ToString("0.###", CultureInfo.InvariantCulture) + " mm deep" : "") +
                       ", and the volume is the one before (" + volumeText + "): nothing changed.";
            bool depthNowShown = endNow == (int)swEndConditions_e.swEndCondBlind ||
                                 endNow == (int)swEndConditions_e.swEndCondMidPlane;
            return " Read back, it is now «" + NomeDellaFine(endNow) + "»" +
                   (depthNowShown && !double.IsNaN(depthNowMm) ? ", " + depthNowMm.ToString("0.###", CultureInfo.InvariantCulture) + " mm deep" : "") +
                   ", with a volume of " + volumeText + " instead of " +
                   volumeBefore.GetValueOrDefault().ToString("0.##", CultureInfo.InvariantCulture) + " mm3: the feature is not as it was. " +
                   "Look at it, and undo the step (" + Frasi.CosiSiRitira + ").";
        }

        private Esito CambiaEstrusione(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            if (!(modello is IPartDoc))
                return Fallito("Cambio le lavorazioni di un pezzo. Aprimi il pezzo.");
            string uid = (Str(a, "feature_uid", "") ?? "").Trim();
            if (uid.Length == 0)
                return Fallito("Dimmi «feature_uid»: la lavorazione da cambiare, col nome dallo stato («feature»).");
            bool chiedePassante = a != null && a.ContainsKey("through_all") && a["through_all"] is bool && (bool)a["through_all"];
            bool chiedeProfondita = a != null && a.ContainsKey("depth_mm");
            if (chiedePassante == chiedeProfondita)
                return Fallito("Dimmi O «through_all»: true (un taglio che attraversa tutto) O «depth_mm» (quanto va " +
                               "a fondo, in mm), uno dei due.");
            double profondita = double.NaN; string errore;
            if (chiedeProfondita)
            {
                if (!NumObbligatorio(a, "depth_mm", out profondita, out errore)) return Fallito(errore);
                if (profondita <= 0) return Fallito("«depth_mm» deve essere positiva.");
            }
            string motivo;
            if (!LavorazioneEQuella(modello, uid, a, out motivo)) return Fallito(motivo);
            var f = TrovaLavorazione(modello, uid);
            if (f == null)
            {
                string ci = LavorazioniCheCiSono(modello);
                return Fallito("Non trovo «" + uid + "» in questo documento." + (ci.Length > 0 ? " Ci sono: " + ci + "." : ""));
            }
            string tipo = "";
            try { tipo = f.GetTypeName2(); } catch { }
            // «ICE» NON VUOL DIRE TAGLIO (voce Z-C21.2, rilievo M7).
            //
            // IFeature~GetTypeName2.html: «To get the underlying type of feature
            // of an Instant3D feature (i.e., "ICE"), call IFeature::GetTypeName».
            // «ICE» e' un'estrusione fatta su una faccia, e puo' essere un taglio
            // (cylindrical_cut, rectangular_cut) o un RIALZO
            // (extrude_on_face): StatoDocumento.cs lo misura dal 10/9/2026. Qui
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
            // Controlli/TipoDellEstrusione, SOLIDWORKS 29.5.1), prima e dopo
            // AccessSelections, stesse risposte:
            //   base (prima estrusione)   GetTypeName2 Extrusion  GetTypeName Extrusion  IsBossFeature false
            //   rialzo (FeatureExtrusion3) GetTypeName2 ICE       GetTypeName Boss       IsBossFeature true
            //   taglio (FeatureCut4)       GetTypeName2 ICE       GetTypeName Cut        IsBossFeature false
            // Anche le estrusioni fatte da uno schizzo su un piano, dopo la base,
            // sono «ICE» per GetTypeName2: «Extrusion» e' solo la base, che non e'
            // un «boss» e non e' un taglio. Dove il tipo e' gia' «Cut» o
            // «Extrusion» decide il tipo, come prima di Z-C21.2.
            if (tipo != "Extrusion" && tipo != "Cut" && tipo != "ICE")
                return Fallito("«" + f.Name + "» e' una lavorazione di tipo «" + tipo + "»: so cambiare le estrusioni e i tagli " +
                               "estrusi. Per una quota qualunque c'e' «set_dimension».");
            var prima = f.GetDefinition() as IExtrudeFeatureData2;
            if (prima == null) return Fallito("Non leggo la definizione di «" + f.Name + "».");
            bool taglio = tipo == "Cut";
            if (tipo == "ICE")
            {
                try { taglio = !prima.IsBossFeature(); }
                catch (Exception ex)
                {
                    // «Nothing touched» needs no reread here (Z-C21.8, finding
                    // M42): up to this line the feature has only been read
                    // (GetDefinition, IsBossFeature), never accessed or modified.
                    return Fallito("Non riesco a sapere se «" + f.Name + "» e' un taglio o un rialzo (" + ex.Message +
                                   "): non la tocco. Non ho toccato niente.");
                }
            }
            if (chiedePassante && !taglio)
                return Fallito("«through_all» vale per un taglio: un'estrusione che «attraversa tutto» non ha niente da " +
                               "attraversare. Per farla crescere dimmi «depth_mm».");
            int finePrima = prima.GetEndCondition(true);
            double profPrima = prima.GetDepth(true);

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
            bool dueVersi = false;
            try { dueVersi = prima.BothDirections; } catch { }
            if (dueVersi && finePrima != (int)swEndConditions_e.swEndCondMidPlane &&
                finePrima != (int)swEndConditions_e.swEndCondThroughAllBoth)
            {
                int fine2 = -1; double prof2 = double.NaN;
                try { fine2 = prima.GetEndCondition(false); prof2 = prima.GetDepth(false); } catch { }
                bool conProf2 = fine2 == (int)swEndConditions_e.swEndCondBlind;
                return Fallito("«" + f.Name + "» va in due versi indipendenti: il primo «" + NomeDellaFine(finePrima) +
                               "», il secondo «" + NomeDellaFine(fine2) + "»" +
                               (conProf2 ? " profondo " + Unita.Mm(prof2).ToString("0.###") + " mm" : "") +
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
            bool conRiferimento = finePrima == (int)swEndConditions_e.swEndCondUpToSurface ||
                                  finePrima == (int)swEndConditions_e.swEndCondOffsetFromSurface ||
                                  finePrima == (int)swEndConditions_e.swEndCondUpToVertex ||
                                  finePrima == (int)swEndConditions_e.swEndCondUpToBody ||
                                  finePrima == (int)swEndConditions_e.swEndCondUpToSelection ||
                                  finePrima == (int)swEndConditions_e.swEndCondUpToNext;
            if (conRiferimento)
                return Fallito("«" + f.Name + "» finisce «" + NomeDellaFine(finePrima) + "»: cambiarla perderebbe il " +
                               "riferimento, e se qualcosa si rompesse non saprei rimetterlo. Cambiala a mano, oppure " +
                               "dimmi una quota da cambiare con «set_dimension».");
            bool simmetrica = finePrima == (int)swEndConditions_e.swEndCondMidPlane ||
                              finePrima == (int)swEndConditions_e.swEndCondThroughAllBoth;
            int fineChiesta = chiedePassante
                ? (simmetrica ? (int)swEndConditions_e.swEndCondThroughAllBoth : (int)swEndConditions_e.swEndCondThroughAll)
                : (simmetrica ? (int)swEndConditions_e.swEndCondMidPlane : (int)swEndConditions_e.swEndCondBlind);
            // LE GUARDIE DI set_dimension: corpi, aperture, smussi, errori.
            var foto = FotografaIlPezzo(modello);
            double? volumePrima = foto.Volume;
            string refusedBy;
            if (!ScriviEstrusione(modello, f, fineChiesta, chiedeProfondita ? Unita.M(profondita) : profPrima, out refusedBy))
            {
                try { modello.ForceRebuild3(false); } catch { }
                return Fallito("SOLIDWORKS did not take the new definition of «" + f.Name + "» (" + refusedBy + ")." +
                               ExtrusionReadBack(modello, f, finePrima, profPrima, volumePrima));
            }
            bool ricostruito = false;
            try { ricostruito = modello.ForceRebuild3(false); } catch { }
            int erroriDopo = LavorazioniInErrore(modello);
            // Senza le aperture: cambia solo la profondita', e un passante che torna
            // cieco le fa calare a ragione (vedi CosaSiERotto).
            string rotto = CosaSiERotto(modello, foto, ricostruito, false);
            if (rotto != null)
            {
                bool rimessa = ScriviEstrusione(modello, f, finePrima, profPrima, out _);
                try { modello.ForceRebuild3(false); } catch { }
                return Fallito("Con la definizione nuova di «" + f.Name + "» " + rotto + "." +
                               ETornatoComEra(modello, foto, rimessa, "la definizione di prima (" + NomeDellaFine(finePrima) + ")"));
            }
            var dopo = f.GetDefinition() as IExtrudeFeatureData2;
            int fineDopo = dopo == null ? -1 : dopo.GetEndCondition(true);
            double profDopo = dopo == null ? double.NaN : dopo.GetDepth(true);
            bool conProfondita = fineDopo == (int)swEndConditions_e.swEndCondBlind || fineDopo == (int)swEndConditions_e.swEndCondMidPlane;
            if (fineDopo != fineChiesta || (chiedeProfondita && Math.Abs(Unita.Mm(profDopo) - profondita) > 0.001))
                return Fallito("SOLIDWORKS dice di aver cambiato «" + f.Name + "», ma rileggendola e' " + NomeDellaFine(fineDopo) +
                               (conProfondita ? " profonda " + Unita.Mm(profDopo).ToString("0.###") + " mm" : "") +
                               ": non e' quello che hai chiesto.");
            double? volumeDopo = VolumeMm3(modello);
            var misure = new Dictionary<string, object>
            {
                ["feature"] = f.Name,
                ["kind"] = taglio ? "cut" : "extrusion",
                ["end_before"] = NomeDellaFine(finePrima),
                ["depth_before_mm"] = finePrima == (int)swEndConditions_e.swEndCondBlind || finePrima == (int)swEndConditions_e.swEndCondMidPlane
                    ? (object)Math.Round(Unita.Mm(profPrima), 4) : null,
                ["end_after"] = NomeDellaFine(fineDopo),
                ["depth_after_mm"] = conProfondita ? (object)Math.Round(Unita.Mm(profDopo), 4) : null,
                ["volume_before_mm3"] = NumeroOppureNull(volumePrima, 2),
                ["volume_after_mm3"] = NumeroOppureNull(volumeDopo, 2),
                ["features_in_error"] = erroriDopo >= 0 ? (object)erroriDopo : null,
            };
            if (volumePrima != null && volumeDopo != null && Math.Abs(volumePrima.Value - volumeDopo.Value) < 0.01)
                misure["warning"] = "La definizione e' cambiata ma il volume no: la profondita' nuova non cambia il pezzo " +
                                       "(un taglio cieco piu' profondo del materiale e' gia' passante, per esempio).";
            _log("[agente] " + f.Name + ": " + NomeDellaFine(finePrima) + " -> " + NomeDellaFine(fineDopo));
            return RiuscitoSenzaCreare(misure);
        }
    }
}
