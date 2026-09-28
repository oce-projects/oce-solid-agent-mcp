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
                fatto = f.ModifyDefinition(dati, modello, null);
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
            bool taglio = tipo == "ICE";
            if (tipo != "Extrusion" && !taglio)
                return Fallito("«" + f.Name + "» e' una lavorazione di tipo «" + tipo + "»: so cambiare le estrusioni e i tagli " +
                               "estrusi. Per una quota qualunque c'e' «modifica_quota».");
            if (chiedePassante && !taglio)
                return Fallito("«passante» vale per un taglio: un'estrusione che «attraversa tutto» non ha niente da " +
                               "attraversare. Per farla crescere dimmi «profondita_mm».");
            var prima = f.GetDefinition() as IExtrudeFeatureData2;
            if (prima == null) return Fallito("Non leggo la definizione di «" + f.Name + "».");
            int finePrima = prima.GetEndCondition(true);
            double profPrima = prima.GetDepth(true);
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
