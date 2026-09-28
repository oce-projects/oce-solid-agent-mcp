using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// RIORDINARE L'ALBERO (voce Z-F3): uno svuotamento messo prima di un foro da'
    /// un pezzo diverso da uno messo dopo, e accorgersene e spostare la voce e' un
    /// gesto di ogni settimana.
    ///
    /// Misurato il 27/9/2026 (`prova0089 zf3`, scatola 100x60x40, foro Ø10
    /// passante, guscio 3 aprendo l'alto): `IModelDocExtension.ReorderFeature
    /// (daSpostare, rispettoA, swMoveBefore=2 / swMoveAfter=3)` risponde true e
    /// il volume cambia — foro poi guscio 56485,7 mm³, guscio prima del foro
    /// 51952,38 — e rimesso com'era torna 56485,7. Uno spostamento impossibile
    /// (il foro prima dell'estrusione su cui sta) risponde FALSE e non tocca
    /// niente; un nome che non c'e' risponde false.
    ///
    /// LE LAVORAZIONI SI INDICANO PER NOME, come in tutto il ponte, e il nome si
    /// controlla col `feature_id` che lo stato ha dato (un nome si rinomina,
    /// l'ID no): `LavorazioneEQuella`, la stessa difesa di `sopprimi_lavorazione`.
    /// Dopo lo spostamento si ricostruisce e si contano le lavorazioni in errore:
    /// se sono aumentate, la lavorazione torna dov'era e ci si rifiuta.
    /// </summary>
    public partial class OperazioniSw
    {
        /// <summary>I nomi delle lavorazioni in ordine d'albero (dopo l'origine).</summary>
        private static List<string> OrdineDelleLavorazioni(IModelDoc2 modello)
        {
            var fuori = new List<string>();
            try
            {
                var f = modello.FirstFeature() as IFeature;
                bool dopoOrigine = false;
                while (f != null)
                {
                    string t = "";
                    try { t = f.GetTypeName2(); } catch { }
                    if (t == "OriginProfileFeature") dopoOrigine = true;
                    else if (dopoOrigine) { try { fuori.Add(f.Name); } catch { } }
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return fuori;
        }

        /// <summary>Quante lavorazioni sono in errore (non solo avviso); -1 se non si legge.</summary>
        private static int LavorazioniInErrore(IModelDoc2 modello)
        {
            try
            {
                int n = 0;
                var f = modello.FirstFeature() as IFeature;
                while (f != null)
                {
                    // S11: ApiHelpers.RebuildError; a code that cannot be read
                    // makes the count unknown (-1), as the exception did before.
                    bool known;
                    if (ApiHelpers.RebuildError(f, out known) != null) n++;
                    if (!known) return -1;
                    f = f.GetNextFeature() as IFeature;
                }
                return n;
            }
            catch { return -1; }
        }

        private Esito RiordinaLavorazione(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            if (!(modello is IPartDoc))
                return Fallito("Riordino l'albero di un pezzo. Aprimi il pezzo.");
            string uid = (Str(a, "feature_uid", "") ?? "").Trim();
            string rispetto = (Str(a, "rispetto_a_uid", "") ?? "").Trim();
            string dove = (Str(a, "dove", "") ?? "").Trim().ToLowerInvariant();
            if (uid.Length == 0 || rispetto.Length == 0)
                return Fallito("Dimmi «feature_uid» (la lavorazione da spostare) e «rispetto_a_uid» (quella accanto a " +
                               "cui metterla): i nomi stanno nello stato, dentro «feature».");
            if (dove != "prima" && dove != "dopo")
                return Fallito("«dove» e' «prima» o «dopo» di «" + rispetto + "», non «" + dove + "».");
            if (string.Equals(uid, rispetto, StringComparison.OrdinalIgnoreCase))
                return Fallito("«" + uid + "» non si sposta rispetto a se stessa: indica un'altra lavorazione.");
            string motivo;
            if (!LavorazioneEQuella(modello, uid, a, out motivo)) return Fallito(motivo);
            if (!LavorazioneEQuella(modello, rispetto, a, out motivo)) return Fallito(motivo);
            var f = TrovaLavorazione(modello, uid);
            var g = TrovaLavorazione(modello, rispetto);
            if (f == null || g == null)
            {
                string ci = LavorazioniCheCiSono(modello);
                return Fallito("Non trovo «" + (f == null ? uid : rispetto) + "» in questo documento." +
                               (ci.Length > 0 ? " Ci sono: " + ci + "." : ""));
            }
            string nomeF = f.Name, nomeG = g.Name;
            var ordinePrima = OrdineDelleLavorazioni(modello);
            int posPrima = ordinePrima.IndexOf(nomeF);
            // Dove rimetterla se lo spostamento rompe qualcosa: prima di quella che
            // la seguiva, o dopo quella che la precedeva se era l'ultima.
            string seguente = posPrima >= 0 && posPrima + 1 < ordinePrima.Count ? ordinePrima[posPrima + 1] : null;
            string precedente = posPrima > 0 ? ordinePrima[posPrima - 1] : null;
            double? volumePrima = VolumeMm3(modello);
            int corpiPrima = ContaCorpiSolidi(modello);
            int erroriPrima = LavorazioniInErrore(modello);

            bool fatto;
            try
            {
                fatto = modello.Extension.ReorderFeature(nomeF, nomeG,
                    dove == "prima" ? (int)swMoveLocation_e.swMoveBefore : (int)swMoveLocation_e.swMoveAfter);
            }
            catch (Exception ex) { return Fallito("Lo spostamento non e' riuscito: " + ex.Message); }
            if (!fatto)
                return Fallito("SOLIDWORKS non sposta «" + nomeF + "» " + dove + " di «" + nomeG + "» (misurato: succede " +
                               "quando la lavorazione dipende da una che verrebbe dopo, per esempio un foro sulla " +
                               "faccia di un'estrusione messo prima dell'estrusione). Non ho toccato niente: spostala " +
                               "rispetto a una lavorazione che viene dopo quelle da cui dipende.");
            try { modello.ForceRebuild3(false); } catch { }
            int erroriDopo = LavorazioniInErrore(modello);
            if (erroriPrima >= 0 && erroriDopo > erroriPrima)
            {
                // SI RIMETTE COM'ERA: un albero rotto non si consegna.
                bool rimessa = false;
                try
                {
                    if (seguente != null) rimessa = modello.Extension.ReorderFeature(nomeF, seguente, (int)swMoveLocation_e.swMoveBefore);
                    else if (precedente != null) rimessa = modello.Extension.ReorderFeature(nomeF, precedente, (int)swMoveLocation_e.swMoveAfter);
                    modello.ForceRebuild3(false);
                }
                catch { }
                return Fallito("Spostata " + dove + " di «" + nomeG + "», «" + nomeF + "» ha rotto " + (erroriDopo - erroriPrima) +
                               " lavorazioni (" + erroriDopo + " in errore invece di " + erroriPrima + "). " +
                               (rimessa ? "L'ho rimessa dov'era." : "Non sono riuscito a rimetterla: " + Frasi.CosiSiRitira + ".") +
                               " Di solito una lavorazione a valle si appoggiava a una faccia che adesso non c'e' ancora.");
            }
            var ordineDopo = OrdineDelleLavorazioni(modello);
            int iF = ordineDopo.IndexOf(nomeF), iG = ordineDopo.IndexOf(nomeG);
            bool alPosto = iF >= 0 && iG >= 0 && (dove == "prima" ? iF < iG : iF > iG);
            if (!alPosto)
                return Fallito("SOLIDWORKS dice di aver spostato «" + nomeF + "», ma nell'albero sta ancora " +
                               (iF < iG ? "prima" : "dopo") + " di «" + nomeG + "». Non e' successo quello che volevi.");
            double? volumeDopo = VolumeMm3(modello);
            int corpiDopo = ContaCorpiSolidi(modello);
            var misure = new Dictionary<string, object>
            {
                ["spostata"] = nomeF,
                ["dove"] = dove,
                ["rispetto_a"] = nomeG,
                ["ordine_prima"] = ordinePrima.ToArray(),
                ["ordine_dopo"] = ordineDopo.ToArray(),
                ["volume_prima_mm3"] = NumeroOppureNull(volumePrima, 2),
                ["volume_dopo_mm3"] = NumeroOppureNull(volumeDopo, 2),
                ["corpi_prima"] = corpiPrima >= 0 ? (object)corpiPrima : null,
                ["corpi_dopo"] = corpiDopo >= 0 ? (object)corpiDopo : null,
                ["lavorazioni_in_errore"] = erroriDopo >= 0 ? (object)erroriDopo : null,
            };
            // L'ORDINE CAMBIATO SENZA CHE IL PEZZO CAMBI si dice: di solito vuol dire
            // che le due lavorazioni non si toccano, e spostarle non serviva.
            if (volumePrima != null && volumeDopo != null && Math.Abs(volumePrima.Value - volumeDopo.Value) < 0.01 && corpiPrima == corpiDopo)
                misure["attenzione"] = "L'ordine e' cambiato ma il pezzo no (stesso volume, stessi corpi): quelle due " +
                                       "lavorazioni non si toccano, e l'ordine fra loro non conta.";
            _log("[agente] " + nomeF + " spostata " + dove + " di " + nomeG);
            return RiuscitoSenzaCreare(misure);
        }
    }
}
