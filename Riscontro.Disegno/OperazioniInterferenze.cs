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
    /// Il primo pezzo per cui questo ponte e' stato scritto e' una scatola
    /// stagna in due meta', da montare all'aperto. **«Il coperchio tocca la
    /// base?»** e **«la vite passa dentro la parete?»** sono le domande che
    /// decidono se il pezzo si puo' fare.
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
            int interferencesRead = 0;
            string listProblem = null;
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

                // THE LIST MUST BE AS LONG AS THE COUNT (Z-C21.10, finding N34).
                //
                // IInterferenceDetectionMgr~GetInterferenceCount.html, Remarks:
                // «Call this method before calling
                // IInterferenceDetectionMgr::IGetInterferences to determine the
                // size of the array for the interferences»; and
                // ~GetInterferences.html, Return Value: «Array of the
                // interferences». The count IS the size of the array. With a null
                // list, a shorter one, or an item that is not an IInterference,
                // `volumiLetti` stayed true: the partial sum went into
                // volume_totale_mm3 and into the verdict («2 INTERFERENZE, per 0
                // mm3 in tutto», riuscita:true when GetInterferences threw). Now
                // any of the three makes the volume unknown
                // (`InterferenceListProblem`), and `interferences_read` says how
                // many were actually read.
                object[] elenco = null;
                string whyNoList = null;
                try { elenco = gestore.GetInterferences() as object[]; }
                catch (Exception ex) { whyNoList = "GetInterferences threw: " + ex.Message; }

                int notInterferences = 0;
                if (elenco != null)
                {
                    for (int i = 0; i < elenco.Length; i++)
                    {
                        var interferenza = elenco[i] as IInterference;
                        if (interferenza == null) { notInterferences++; continue; }
                        interferencesRead++;

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
                listProblem = InterferenceListProblem(
                    quante, elenco == null ? (int?)null : elenco.Length, notInterferences, whyNoList);
                if (listProblem != null) volumiLetti = false;

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
                // How many of the counted interferences the array actually gave:
                // equal to «quante» when the list and the count agree (N34).
                ["interferences_read"] = interferencesRead,
                ["componenti_guardati"] = componenti.Count,
                ["toccarsi_conta"] = toccarsiConta,
                ["dentro_le_parti"] = dentroLeParti,
            };
            if (trovate.Count > 0) misure["interferenze"] = trovate.ToArray();
            if (coinvolti.Count > 0) misure["componenti_coinvolti"] = coinvolti.ToArray();
            if (quante > 0 && volumiLetti) misure["volume_totale_mm3"] = Math.Round(volumeTotale, 3);
            if (listProblem != null)
                misure["cosa_non_so"] =
                    "SOLIDWORKS did not give me the whole list of interferences (" + listProblem +
                    "): I read " + interferencesRead + " of " + quante + ". The count comes from " +
                    "GetInterferenceCount; the total volume would be the sum of a part of the list, " +
                    "so I left it out instead of writing one smaller than the truth.";
            else if (quante > 0 && !volumiLetti)
                misure["cosa_non_so"] =
                    "Di almeno una interferenza non sono riuscito a leggere il volume: " +
                    "il conteggio e' giusto, la somma no. Non l'ho scritta, invece di " +
                    "scriverne una piu' piccola del vero.";

            // A ZERO COUNT WITH INTERFERENCES IN THE LIST is not «none» (N34):
            // the two calls disagree, and «Nessuna interferenza» with
            // riuscita:true is the one answer that sends a part that does not
            // fit to the shop.
            if (quante == 0 && interferencesRead > 0)
                return Fallito(
                    "SOLIDWORKS counted 0 interferences and then listed " + interferencesRead +
                    " (" + listProblem + "): I cannot tell you that the parts do not interfere. " +
                    "Ask me again; if it repeats, check with Interference Detection in SOLIDWORKS.");

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
        /// What is wrong with the array GetInterferences returned, measured
        /// against the count (N34); null when it is whole. The array must be as
        /// long as the count (GetInterferenceCount.html: «to determine the size
        /// of the array for the interferences») and every item an
        /// IInterference. `arrayLength` is null when there was no array,
        /// `whyNoList` the exception that took it, if any.
        ///
        /// No interop type in the signature or the body, on purpose: the
        /// branches a bench case cannot build (no array, a short one, an item
        /// of another type) can be tested without SOLIDWORKS if this is moved
        /// to a file Prove.Contratti compiles.
        /// </summary>
        internal static string InterferenceListProblem(
            int counted, int? arrayLength, int notInterferences, string whyNoList)
        {
            var problems = new List<string>();
            if (arrayLength == null)
            {
                if (counted > 0)
                    problems.Add((whyNoList ?? "GetInterferences returned no array") +
                                 ", after GetInterferenceCount counted " + counted);
            }
            else if ((int)arrayLength != counted)
                problems.Add("GetInterferenceCount counted " + counted +
                             " and GetInterferences returned an array of " + (int)arrayLength);
            if (notInterferences > 0)
                problems.Add(notInterferences + (notInterferences == 1 ? " item" : " items") +
                             " of the array not an IInterference");
            return problems.Count == 0 ? null : string.Join("; ", problems);
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
