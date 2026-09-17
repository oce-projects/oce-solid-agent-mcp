using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// L'ASSIEME SI TOCCA? (voce GA10)
    ///
    /// ── PERCHE' QUESTA VOCE E' ALTA ─────────────────────────────────────
    ///
    /// Lo scopo dichiarato del progetto e' una scatola stagna in due meta',
    /// da montare all'aperto. **«Il coperchio tocca la base?»** e **«la
    /// vite passa dentro la parete?»** sono le domande che decidono se il pezzo
    /// si puo' fare.
    ///
    /// Fino a qui il ponte non sapeva rispondere: lo stato dell'assieme dice
    /// quanti componenti ci sono, se sono fissi, se sono vincolati — **non se
    /// occupano lo stesso spazio.** Due pezzi compenetrati si aprono, si
    /// ricostruiscono, si esportano, si stampano: **nessun errore da nessuna
    /// parte**, e in officina non entrano. E' il difetto che non da' errore
    /// nella forma piu' cara di tutte, perche' si scopre a pezzo fatto.
    ///
    /// ── LA MISURA E' IL VOLUME, NON IL CONTEGGIO ────────────────────────
    ///
    /// `GetInterferenceCount()` dice quante sono. Non basta: **due interferenze
    /// possono essere due decimi di millimetro di tolleranza oppure mezzo
    /// coperchio dentro la base**, e il numero e' lo stesso.
    ///
    /// `IInterference.GetInterferenceBody()` torna il **corpo solido
    /// dell'intersezione**: da li' `GetMassProperties` da' il volume. E' quello
    /// che distingue un problema da un dettaglio.
    ///
    /// *Un conteggio senza una grandezza non distingue un problema da un
    /// dettaglio.*
    ///
    /// ── `ToolsCheckInterference2` NON SI CHIAMA ────────────────────────
    ///
    /// Ritorna `Void`: e' il bottone. Stessa regola misurata su GA8 — **un'API
    /// risponde, un bottone no**. Il manager (`InterferenceDetectionManager`)
    /// e' l'API.
    ///
    /// ── LE FACCE CHE SI TOCCANO SONO UN'INTERFERENZA? DIPENDE ──────────
    ///
    /// E non c'e' una risposta giusta, per questo e' un argomento:
    ///
    ///   - un coperchio **appoggiato** sulla base condivide una faccia con lei.
    ///     Se la coincidenza conta come interferenza, **ogni assieme montato
    ///     bene risulta rotto**;
    ///   - un albero in un foro dello **stesso diametro** condivide la
    ///     superficie cilindrica. Li' la coincidenza e' esattamente il problema:
    ///     quel pezzo non entra.
    ///
    /// Di serie `false`, ed e' scritto nella risposta quale delle due si e'
    /// usata. *Un'impostazione che cambia il verdetto e non compare nel verdetto
    /// e' un verdetto che non si puo' rileggere.*
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito VerificaInterferenze(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var assieme = modello as IAssemblyDoc;
            if (assieme == null)
                return Fallito(
                    "Questo non e' un assieme, e' un documento di tipo «" +
                    NomeTipoDocumento(TipoDi(modello)) + "». Le interferenze si " +
                    "cercano fra i componenti di un assieme: su una parte sola non " +
                    "c'e' niente che possa compenetrare qualcos'altro.");

            object[] grezzi = null;
            try { grezzi = assieme.GetComponents(false) as object[]; } catch { }
            var componenti = StatoDocumento.ComponentiInOrdine(grezzi);
            if (componenti.Count < 2)
                return Fallito(
                    "L'assieme ha " + componenti.Count +
                    (componenti.Count == 1 ? " componente" : " componenti") +
                    ": ce ne vogliono almeno due perche' uno possa toccare l'altro.");

            bool toccarsiConta = Bool(a, "toccarsi_conta", false);
            bool dentroLeParti = Bool(a, "dentro_le_parti", false);

            IInterferenceDetectionMgr gestore;
            try { gestore = assieme.InterferenceDetectionManager as IInterferenceDetectionMgr; }
            catch (Exception ex)
            {
                return Fallito("Non riesco ad aprire il rilevamento interferenze: " + ex.Message);
            }
            if (gestore == null)
                return Fallito(
                    "SOLIDWORKS non mi da' il rilevamento interferenze su questo " +
                    "documento, e non dice perche'.");

            var trovate = new List<object>();
            int quante = 0;
            double volumeTotale = 0;
            bool volumiLetti = true;
            var coinvolti = new List<string>();

            try
            {
                // Le impostazioni PRIMA di contare: cambiano il verdetto, non la
                // sua presentazione.
                try { gestore.TreatCoincidenceAsInterference = toccarsiConta; } catch { }
                try { gestore.IncludeMultibodyPartInterferences = dentroLeParti; } catch { }
                try { gestore.ShowIgnoredInterferences = false; } catch { }

                try { quante = gestore.GetInterferenceCount(); }
                catch (Exception ex) { return Fallito("Non riesco a contare le interferenze: " + ex.Message); }

                object[] elenco = null;
                try { elenco = gestore.GetInterferences() as object[]; } catch { }

                if (elenco != null)
                {
                    for (int i = 0; i < elenco.Length; i++)
                    {
                        var interferenza = elenco[i] as IInterference;
                        if (interferenza == null) continue;

                        double volume;
                        bool letto = VolumeDellInterferenzaMm3(interferenza, out volume);
                        if (!letto) volumiLetti = false; else volumeTotale += volume;

                        bool forse = false;
                        try { forse = interferenza.IsPossibleInterference; } catch { }

                        var voce = new Dictionary<string, object>
                        {
                            ["n"] = i + 1,
                            ["volume_mm3"] = letto ? (object)Math.Round(volume, 3) : null,
                        };
                        // «Possibile» vuol dire che SOLIDWORKS non e' sicuro:
                        // si riporta com'e' invece di arrotondarlo a un si'.
                        if (forse) voce["solo_possibile"] = true;
                        trovate.Add(voce);
                    }
                }

                try
                {
                    var comp = gestore.GetInterferenceComponents() as object[];
                    if (comp != null)
                        foreach (var o in comp)
                        {
                            var c = o as IComponent2;
                            if (c == null) continue;
                            string n = null;
                            try { n = c.Name2; } catch { }
                            if (!string.IsNullOrWhiteSpace(n) && !coinvolti.Contains(n)) coinvolti.Add(n);
                        }
                }
                catch { }
            }
            finally
            {
                // SI SPEGNE SEMPRE, E IN `finally`.
                //
                // Il rilevamento e' uno STATO del documento: lasciarlo acceso
                // lascia SOLIDWORKS in modalita' interferenze, con i corpi
                // evidenziati, per chiunque apra quel documento dopo. E il
                // `finally` non e' pignoleria: l'eccezione arriva proprio quando
                // l'analisi va male (stessa lezione gia' scritta per
                // `IThicknessAnalysis.Close()`, voce W2.14).
                try { gestore.Done(); } catch { }
            }

            var misure = new Dictionary<string, object>
            {
                ["quante"] = quante,
                ["componenti_guardati"] = componenti.Count,
                ["toccarsi_conta"] = toccarsiConta,
                ["dentro_le_parti"] = dentroLeParti,
            };
            if (trovate.Count > 0) misure["interferenze"] = trovate.ToArray();
            if (coinvolti.Count > 0) misure["componenti_coinvolti"] = coinvolti.ToArray();
            if (quante > 0 && volumiLetti) misure["volume_totale_mm3"] = Math.Round(volumeTotale, 3);
            if (quante > 0 && !volumiLetti)
                misure["cosa_non_so"] =
                    "Di almeno una interferenza non sono riuscito a leggere il volume: " +
                    "il conteggio e' giusto, la somma no. Non l'ho scritta, invece di " +
                    "scriverne una piu' piccola del vero.";

            // ── IL GIUDIZIO ─────────────────────────────────────────────
            if (quante == 0)
            {
                misure["giudizio"] =
                    "Nessuna interferenza fra i " + componenti.Count + " componenti" +
                    (toccarsiConta
                        ? ", e le facce che si toccano erano contate come interferenza."
                        : ". ATTENZIONE a cosa vuol dire: le facce che si TOCCANO non " +
                          "sono state contate. Un albero in un foro dello stesso " +
                          "diametro risulta a posto qui, e in officina non entra: se e' " +
                          "quello che ti interessa, richiedimelo con «toccarsi_conta».");
                _log("[agente] " + misure["giudizio"]);
                return RiuscitoSenzaCreare(misure);
            }

            misure["giudizio"] =
                "ATTENZIONE: " + quante + (quante == 1 ? " INTERFERENZA" : " INTERFERENZE") +
                (volumiLetti
                    ? ", per " + Math.Round(volumeTotale, 2).ToString(CultureInfo.InvariantCulture) +
                      " mm3 in tutto"
                    : "") +
                (coinvolti.Count > 0 ? " fra: " + string.Join(", ", coinvolti) : "") +
                ". I pezzi occupano lo stesso spazio: l'assieme si apre, si ricostruisce " +
                "e si esporta lo stesso — **non c'e' nessun errore da nessuna parte**, e " +
                "in officina non entrano. Guarda il volume: pochi centesimi di mm3 sono " +
                "due facce che si sfiorano, migliaia sono un pezzo dentro l'altro.";
            _log("[agente] " + misure["giudizio"]);
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>
        /// Il volume della compenetrazione, in mm3. `false` quando non si e'
        /// potuto leggere — che **non** e' zero: zero vorrebbe dire «si toccano
        /// e basta», e sarebbe una risposta, non una mancanza.
        /// </summary>
        private static bool VolumeDellInterferenzaMm3(IInterference interferenza, out double mm3)
        {
            mm3 = 0;
            try
            {
                var corpo = interferenza.GetInterferenceBody() as IBody2;
                if (corpo == null) return false;
                var mp = corpo.GetMassProperties(1000.0) as double[];
                if (mp == null || mp.Length < 4) return false;
                // `GetMassProperties` da' il volume in metri cubi in posizione 3.
                mm3 = mp[3] * 1e9;
                return true;
            }
            catch { return false; }
        }
    }
}
