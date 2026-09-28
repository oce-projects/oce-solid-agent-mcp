using System;
using System.Collections.Generic;
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
                case (int)swEndConditions_e.swEndCondBlind: return "cieca";
                case (int)swEndConditions_e.swEndCondThroughAll: return "passante";
                case (int)swEndConditions_e.swEndCondThroughNext: return "fino alla prossima";
                case (int)swEndConditions_e.swEndCondMidPlane: return "piano medio";
                case (int)swEndConditions_e.swEndCondUpToSurface: return "fino alla superficie";
                case (int)swEndConditions_e.swEndCondOffsetFromSurface: return "a distanza dalla superficie";
                case (int)swEndConditions_e.swEndCondUpToVertex: return "fino al vertice";
                case (int)swEndConditions_e.swEndCondUpToBody: return "fino al corpo";
                case (int)swEndConditions_e.swEndCondThroughAllBoth: return "passante nei due versi";
                default: return "condizione " + c;
            }
        }

        /// <summary>Scrive fine e profondita' nella definizione e la applica; true se ModifyDefinition risponde true.</summary>
        private static bool ScriviEstrusione(IModelDoc2 modello, IFeature f, int fine, double profonditaM)
        {
            var dati = f.GetDefinition() as IExtrudeFeatureData2;
            if (dati == null) return false;
            bool fatto = false;
            try
            {
                dati.AccessSelections(modello, null);
                dati.SetEndCondition(true, fine);
                // ANCHE IL PIANO MEDIO HA LA SUA PROFONDITA' (revisione del 27/9/2026):
                // senza, il ripristino rimetteva la condizione e lasciava la misura nuova.
                if (fine == (int)swEndConditions_e.swEndCondBlind || fine == (int)swEndConditions_e.swEndCondMidPlane)
                    dati.SetDepth(true, profonditaM);
                // S11: ModifyDefinition always with GetErrorCode2 (ApiHelpers).
                // The verdict on errors stays with the caller, which rebuilds
                // and counts the features in error (LavorazioniInErrore).
                fatto = ApiHelpers.ModifyAndVerify(f, dati, modello).Applied;
            }
            catch { fatto = false; }
            if (!fatto) { try { dati.ReleaseSelectionAccess(); } catch { } }
            return fatto;
        }

        private Esito CambiaEstrusione(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            if (!(modello is IPartDoc))
                return Fallito("Cambio le lavorazioni di un pezzo. Aprimi il pezzo.");
            string uid = (Str(a, "feature_uid", "") ?? "").Trim();
            if (uid.Length == 0)
                return Fallito("Dimmi «feature_uid»: la lavorazione da cambiare, col nome dallo stato («feature»).");
            bool chiedePassante = a != null && a.ContainsKey("passante") && a["passante"] is bool && (bool)a["passante"];
            bool chiedeProfondita = a != null && a.ContainsKey("profondita_mm");
            if (chiedePassante == chiedeProfondita)
                return Fallito("Dimmi O «passante»: true (un taglio che attraversa tutto) O «profondita_mm» (quanto va " +
                               "a fondo, in mm), uno dei due.");
            double profondita = double.NaN; string errore;
            if (chiedeProfondita)
            {
                if (!NumObbligatorio(a, "profondita_mm", out profondita, out errore)) return Fallito(errore);
                if (profondita <= 0) return Fallito("«profondita_mm» deve essere positiva.");
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
            // (taglio_cilindrico, taglio_rettangolare) o un RIALZO
            // (estrudi_su_faccia): StatoDocumento.cs lo misura dal 10/9/2026. Qui
            // «ICE» valeva «taglio» e basta: un rialzo diventava «taglio», e
            // «passante» su un rialzo non veniva rifiutato; un «Cut» vero veniva
            // invece rifiutato. Per l'«ICE» taglio o rialzo lo dice la definizione:
            // IExtrudeFeatureData2~IsBossFeature.html, «True if this extrusion is
            // a boss feature, false if not».
            //
            // SOLO PER L'«ICE». La prima stesura chiedeva IsBossFeature a tutte, e
            // la piastra di base (la PRIMA estrusione del pezzo, «Extrusion»)
            // rispondeva false: «passante» su una piastra passava il controllo e la
            // piastra diventava «attraversa tutto» (il_cieco_diventa_passante,
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
                               "estrusi. Per una quota qualunque c'e' «modifica_quota».");
            var prima = f.GetDefinition() as IExtrudeFeatureData2;
            if (prima == null) return Fallito("Non leggo la definizione di «" + f.Name + "».");
            bool taglio = tipo == "Cut";
            if (tipo == "ICE")
            {
                try { taglio = !prima.IsBossFeature(); }
                catch (Exception ex)
                {
                    return Fallito("Non riesco a sapere se «" + f.Name + "» e' un taglio o un rialzo (" + ex.Message +
                                   "): non la tocco. Non ho toccato niente.");
                }
            }
            if (chiedePassante && !taglio)
                return Fallito("«passante» vale per un taglio: un'estrusione che «attraversa tutto» non ha niente da " +
                               "attraversare. Per farla crescere dimmi «profondita_mm».");
            int finePrima = prima.GetEndCondition(true);
            double profPrima = prima.GetDepth(true);

            // DUE VERSI INDIPENDENTI: SI RIFIUTA (voce Z-C21.2, rilievo L5-1).
            //
            // IExtrudeFeatureData2~BothDirections.html: «Gets or sets whether the
            // extrusion is in both directions». Qui si scrive e si rilegge solo il
            // verso 1 (Forward = true in SetEndCondition/SetDepth: «True for
            // forward direction, false for reverse»). Su un'estrusione a due versi
            // un «passante» cambiava il verso 1, la rilettura del verso 1 tornava,
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
                               "«modifica_quota». Non ho toccato niente.");
            }
            // LA CONDIZIONE CHE SI CHIEDE DIPENDE DA QUELLA CHE C'E' (revisione
            // avversariale del 27/9/2026). Una piastra simmetrica da 100 (da -50 a
            // +50) portata a 120 diventava cieca da 0 a +120: non cresceva di 10 per
            // parte, si spostava di 60, e i fori sulle facce la seguivano in posti
            // diversi — e la risposta diceva «riuscito». Il piano medio resta piano
            // medio; «passante» su un piano medio attraversa nei due versi.
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
                               "dimmi una quota da cambiare con «modifica_quota».");
            bool simmetrica = finePrima == (int)swEndConditions_e.swEndCondMidPlane ||
                              finePrima == (int)swEndConditions_e.swEndCondThroughAllBoth;
            int fineChiesta = chiedePassante
                ? (simmetrica ? (int)swEndConditions_e.swEndCondThroughAllBoth : (int)swEndConditions_e.swEndCondThroughAll)
                : (simmetrica ? (int)swEndConditions_e.swEndCondMidPlane : (int)swEndConditions_e.swEndCondBlind);
            // LE GUARDIE DI modifica_quota: corpi, aperture, smussi, errori.
            var foto = FotografaIlPezzo(modello);
            double? volumePrima = foto.Volume;
            if (!ScriviEstrusione(modello, f, fineChiesta, chiedeProfondita ? Unita.M(profondita) : profPrima))
                return Fallito("SOLIDWORKS non accetta la definizione nuova di «" + f.Name + "» (ModifyDefinition ha risposto " +
                               "false). Non ho toccato niente.");
            bool ricostruito = false;
            try { ricostruito = modello.ForceRebuild3(false); } catch { }
            int erroriDopo = LavorazioniInErrore(modello);
            // Senza le aperture: cambia solo la profondita', e un passante che torna
            // cieco le fa calare a ragione (vedi CosaSiERotto).
            string rotto = CosaSiERotto(modello, foto, ricostruito, false);
            if (rotto != null)
            {
                bool rimessa = ScriviEstrusione(modello, f, finePrima, profPrima);
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
                ["lavorazione"] = f.Name,
                ["tipo"] = taglio ? "taglio" : "estrusione",
                ["fine_prima"] = NomeDellaFine(finePrima),
                ["profondita_prima_mm"] = finePrima == (int)swEndConditions_e.swEndCondBlind || finePrima == (int)swEndConditions_e.swEndCondMidPlane
                    ? (object)Math.Round(Unita.Mm(profPrima), 4) : null,
                ["fine_dopo"] = NomeDellaFine(fineDopo),
                ["profondita_dopo_mm"] = conProfondita ? (object)Math.Round(Unita.Mm(profDopo), 4) : null,
                ["volume_prima_mm3"] = NumeroOppureNull(volumePrima, 2),
                ["volume_dopo_mm3"] = NumeroOppureNull(volumeDopo, 2),
                ["lavorazioni_in_errore"] = erroriDopo >= 0 ? (object)erroriDopo : null,
            };
            if (volumePrima != null && volumeDopo != null && Math.Abs(volumePrima.Value - volumeDopo.Value) < 0.01)
                misure["attenzione"] = "La definizione e' cambiata ma il volume no: la profondita' nuova non cambia il pezzo " +
                                       "(un taglio cieco piu' profondo del materiale e' gia' passante, per esempio).";
            _log("[agente] " + f.Name + ": " + NomeDellaFine(finePrima) + " -> " + NomeDellaFine(fineDopo));
            return RiuscitoSenzaCreare(misure);
        }
    }
}
