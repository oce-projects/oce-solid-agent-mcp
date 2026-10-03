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

            bool toccarsiConta = Bool(a, "touching_counts", false);
            bool dentroLeParti = Bool(a, "inside_parts", false);

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
            // N38: how many of the interferences read are only POSSIBLE, and
            // their volume; null when the flag of one of them could not be read.
            int? possibleCount = 0;
            double possibleVolume = 0;
            // N18: the three settings as SOLIDWORKS reads them back.
            bool? coincidenceRead = null, multibodyRead = null, ignoredShownRead = null;

            try
            {
                // Le impostazioni PRIMA di contare: cambiano il verdetto, non la
                // sua presentazione.
                //
                // AND THEY ARE READ BACK (finding N18). The three are get/set
                // properties (IInterferenceDetectionMgr~TreatCoincidenceAsInterference.html,
                // ~IncludeMultibodyPartInterferences.html, ~ShowIgnoredInterferences.html:
                // «Gets or sets ...»). Until library 0.144.0 each setter sat in an
                // empty catch and the answer repeated the values asked for: a
                // setter that failed gave a verdict on another question, signed
                // with the question asked. Now the answer gives what SOLIDWORKS
                // reads back, and a value that reads back different stops the
                // count (InterferenceSettingsProblem).
                try { gestore.TreatCoincidenceAsInterference = toccarsiConta; } catch { }
                try { gestore.IncludeMultibodyPartInterferences = dentroLeParti; } catch { }
                try { gestore.ShowIgnoredInterferences = false; } catch { }
                try { coincidenceRead = gestore.TreatCoincidenceAsInterference; } catch { }
                try { multibodyRead = gestore.IncludeMultibodyPartInterferences; } catch { }
                try { ignoredShownRead = gestore.ShowIgnoredInterferences; } catch { }
                string settingsProblem = InterferenceSettingsProblem(
                    toccarsiConta, coincidenceRead, dentroLeParti, multibodyRead, ignoredShownRead);
                if (settingsProblem != null)
                    return Fallito(
                        "SOLIDWORKS did not take the settings of the interference check: " + settingsProblem +
                        ". The count would answer another question than the one asked, so I did not count. " +
                        "Ask again; if it repeats, check with Interference Detection in SOLIDWORKS.");

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
                // volume_total_mm3 and into the verdict («2 INTERFERENZE, per 0
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

                        bool? forse = null;
                        try { forse = interferenza.IsPossibleInterference; } catch { }
                        // N38: counted apart, not rounded up to a certain one.
                        // IInterference~IsPossibleInterference.html: «True if
                        // this is a possible interference, false if not».
                        if (forse == null) possibleCount = null;
                        else if (forse == true)
                        {
                            if (possibleCount.HasValue) possibleCount++;
                            if (letto) possibleVolume += volume;
                        }

                        var voce = new Dictionary<string, object>
                        {
                            ["n"] = i + 1,
                            ["volume_mm3"] = letto ? (object)Math.Round(volume, 3) : null,
                        };
                        // «Possibile» vuol dire che SOLIDWORKS non e' sicuro:
                        // si riporta com'e' invece di arrotondarlo a un si'.
                        if (forse == true) voce["only_possible"] = true;
                        else if (forse == null) voce["possible_not_read"] = true;
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

            // N18: the components the check looks at are the ones that are not
            // suppressed. Until library 0.144.0 the count of components looked
            // at took every component, suppressed ones included. GetSuppression2
            // answers swComponentSuppressionState_e, as in StatoAssieme.
            // The components are read from the array GetComponents gave, the
            // same ones the list above was built from.
            var states = new List<int?>();
            foreach (var o in grezzi ?? new object[0])
            {
                var component = o as IComponent2;
                if (component == null) continue;
                int? state = null;
                try { state = component.GetSuppression2(); } catch { }
                states.Add(state);
            }
            int suppressedComponents;
            int lookedAt = ComponentsLookedAt(states.ToArray(), out suppressedComponents);

            // N38: certain and possible apart, only when every counted
            // interference was read and each said which it is.
            int? possible = interferencesRead == quante ? possibleCount : null;
            object certainOut = null, possibleOut = null;
            if (possible is int possibleKnown)
            {
                certainOut = quante - possibleKnown;
                possibleOut = possibleKnown;
            }

            var misure = new Dictionary<string, object>
            {
                ["how_many"] = quante,
                // How many of the counted interferences the array actually gave:
                // equal to «how_many» when the list and the count agree (N34).
                ["interferences_read"] = interferencesRead,
                ["certain_interferences"] = certainOut,
                ["possible_interferences"] = possibleOut,
                ["components_checked"] = NumeroOppureNull(lookedAt),
                ["suppressed_components"] = NumeroOppureNull(suppressedComponents),
                // N18: as SOLIDWORKS reads them back after setting them (the
                // check above stops when they differ from the request).
                ["touching_counts"] = coincidenceRead,
                ["inside_parts"] = multibodyRead,
            };
            if (suppressedComponents > 0)
                misure["suppressed_note"] =
                    suppressedComponents + (suppressedComponents == 1 ? " component is" : " components are") +
                    " suppressed and not in the check: SOLIDWORKS does not look at them. Unsuppress them to " +
                    "have them checked.";
            if (coincidenceRead == null || multibodyRead == null || ignoredShownRead == null)
                misure["settings_not_read_back"] =
                    "I could not read back every setting of the check (TreatCoincidenceAsInterference, " +
                    "IncludeMultibodyPartInterferences, ShowIgnoredInterferences): those were set as asked " +
                    "but not confirmed, and the answer gives null for them.";
            if (trovate.Count > 0) misure["interferences"] = trovate.ToArray();
            if (coinvolti.Count > 0) misure["components_involved"] = coinvolti.ToArray();
            if (quante > 0 && volumiLetti) misure["volume_total_mm3"] = Math.Round(volumeTotale, 3);
            if (listProblem != null)
                misure["what_is_not_known"] =
                    "SOLIDWORKS did not give me the whole list of interferences (" + listProblem +
                    "): I read " + interferencesRead + " of " + quante + ". The count comes from " +
                    "GetInterferenceCount; the total volume would be the sum of a part of the list, " +
                    "so I left it out instead of writing one smaller than the truth.";
            else if (quante > 0 && !volumiLetti)
                misure["what_is_not_known"] =
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
                misure["verdict"] =
                    "Nessuna interferenza fra i " + (lookedAt >= 0 ? lookedAt : componenti.Count) + " componenti" +
                    (toccarsiConta
                        ? ", e le facce che si toccano erano contate come interferenza."
                        : ". ATTENZIONE a cosa vuol dire: le facce che si TOCCANO non " +
                          "sono state contate. Un albero in un foro dello stesso " +
                          "diametro risulta a posto qui, e in officina non entra: se e' " +
                          "quello che ti interessa, richiedimelo con «touching_counts».");
                _log("[agente] " + misure["verdict"]);
                return RiuscitoSenzaCreare(misure);
            }

            misure["verdict"] = InterferenceVerdict(
                quante, possible, volumiLetti, volumeTotale, possibleVolume, coinvolti);
            _log("[agente] " + misure["verdict"]);
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>
        /// The verdict when SOLIDWORKS counted interferences (a count above
        /// zero), with the certain and the POSSIBLE ones apart (finding N38).
        ///
        /// IInterference~IsPossibleInterference.html: «True if this is a
        /// possible interference, false if not». Until library 0.144.0 the flag
        /// was written next to each interference, but the count and the volume
        /// of the verdict took the possible ones as certain: one possible
        /// interference gave the alarm of a certain one. Now the head counts
        /// the certain ones, the possible ones follow in a sentence of their
        /// own, and when every one is only possible the verdict says that
        /// instead. `possible` is null when it is not known (a flag unread, or
        /// a list shorter than the count): the head then counts them all, as
        /// before, and says it could not tell. No interop type in the
        /// signature: Prove.Contratti calls it by reflection.
        /// </summary>
        internal static string InterferenceVerdict(
            int counted, int? possible, bool volumesRead, double totalVolume, double possibleVolume,
            List<string> involved)
        {
            string among = involved != null && involved.Count > 0 ? string.Join(", ", involved) : null;
            int possibleN = possible.GetValueOrDefault();
            int certain = counted - possibleN;
            double certainVolume = possible.HasValue ? totalVolume - possibleVolume : totalVolume;
            string possibleVolumeText = volumesRead
                ? ", " + Math.Round(possibleVolume, 2).ToString(CultureInfo.InvariantCulture) + " mm3"
                : "";

            if (possible.HasValue && certain <= 0)
                return "SOLIDWORKS found " + counted + (counted == 1 ? " POSSIBLE interference" : " POSSIBLE interferences") +
                       possibleVolumeText + (among != null ? " among: " + among : "") + ", and no certain one: " +
                       "it is not sure that these parts occupy the same space (IsPossibleInterference). Look at " +
                       "them with Interference Detection in SOLIDWORKS before you decide.";

            string head =
                "ATTENZIONE: " + certain + (certain == 1 ? " INTERFERENZA" : " INTERFERENZE") +
                (volumesRead
                    ? ", per " + Math.Round(certainVolume, 2).ToString(CultureInfo.InvariantCulture) +
                      " mm3 in tutto"
                    : "") +
                (among != null ? " fra: " + among : "") +
                ". I pezzi occupano lo stesso spazio: l'assieme si apre, si ricostruisce " +
                "e si esporta lo stesso — **non c'e' nessun errore da nessuna parte**, e " +
                "in officina non entrano. Guarda il volume: pochi centesimi di mm3 sono " +
                "due facce che si sfiorano, migliaia sono un pezzo dentro l'altro.";
            if (!possible.HasValue)
                return head + " Whether some of them are only POSSIBLE interferences I could not read " +
                       "(IsPossibleInterference): they are all counted as certain here.";
            if (possibleN > 0)
                return head + " Besides these, " + possibleN +
                       (possibleN == 1 ? " POSSIBLE interference" : " POSSIBLE interferences") +
                       possibleVolumeText + ": SOLIDWORKS is not sure about " +
                       (possibleN == 1 ? "it" : "them") + " (IsPossibleInterference), look before " +
                       "counting " + (possibleN == 1 ? "it" : "them") + ".";
            return head;
        }

        /// <summary>
        /// How many components the interference check looks at: the ones that
        /// are not suppressed (finding N18). `states` holds the
        /// swComponentSuppressionState_e answer of GetSuppression2 for each
        /// component, null where it could not be read; then the count is -1,
        /// «not known», and so is `suppressed`. No interop type in the
        /// signature: Prove.Contratti calls it by reflection.
        /// </summary>
        internal static int ComponentsLookedAt(int?[] states, out int suppressed)
        {
            suppressed = 0;
            if (states == null) { suppressed = -1; return -1; }
            int looked = 0;
            foreach (var s in states)
            {
                if (!(s is int state)) { suppressed = -1; return -1; }
                if (state == (int)swComponentSuppressionState_e.swComponentSuppressed) suppressed++;
                else looked++;
            }
            return looked;
        }

        /// <summary>
        /// What is wrong with the settings of the check as SOLIDWORKS reads them
        /// back (finding N18), or null. A value that reads back DIFFERENT from
        /// the request is a problem: the count would answer another question.
        /// A value that cannot be read back (null) is not: the setter did not
        /// throw, and the answer says it is unconfirmed. Ignored interferences
        /// are asked NOT shown. No interop type in the signature.
        /// </summary>
        internal static string InterferenceSettingsProblem(
            bool coincidenceAsked, bool? coincidenceRead, bool multibodyAsked, bool? multibodyRead,
            bool? ignoredShownRead)
        {
            var problems = new List<string>();
            if (coincidenceRead is bool coincidence && coincidence != coincidenceAsked)
                problems.Add("TreatCoincidenceAsInterference reads " + coincidence.ToString().ToLowerInvariant() +
                             " where the request asked " + coincidenceAsked.ToString().ToLowerInvariant());
            if (multibodyRead is bool multibody && multibody != multibodyAsked)
                problems.Add("IncludeMultibodyPartInterferences reads " + multibody.ToString().ToLowerInvariant() +
                             " where the request asked " + multibodyAsked.ToString().ToLowerInvariant());
            if (ignoredShownRead == true)
                problems.Add("ShowIgnoredInterferences reads true where it was set to false");
            return problems.Count == 0 ? null : string.Join("; ", problems);
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
