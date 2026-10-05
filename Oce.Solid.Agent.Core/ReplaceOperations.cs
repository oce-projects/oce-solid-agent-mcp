using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// SOSTITUIRE UN COMPONENTE (voce GA7).
    ///
    /// ── LA FIRMA C'ERA, IL CENSIMENTO GUARDAVA L'INTERFACCIA SBAGLIATA ───
    ///
    /// La voce diceva: *«il censimento 80q NON elenca una firma dedicata;
    /// `IComponent2` ha 170 metodi e quasi certamente esiste qualcosa tipo
    /// `ReplaceComponent`, ma va cercata e letta, non assunta»*, e da li' il
    /// «non stimabile».
    ///
    /// **Su `IComponent2` non c'e', e non c'e' mai stata.** Sta su
    /// `IAssemblyDoc`:
    ///
    ///     Boolean ReplaceComponents2(String FileName, String ConfigName,
    ///                                Boolean ReplaceAllInstance,
    ///                                Int32 UseConfigChoice,
    ///                                Boolean ReAttachMates)
    ///
    /// E' la stessa lezione gia' pagata due volte in questo progetto:
    /// *un nome dice come si chiama un metodo, non su quale interfaccia sta*
    /// (nota `81s`, `AddConfiguration3`). Cercare per centosettanta metodi
    /// sull'oggetto sbagliato non trova niente, e «non l'ho trovata» diventa
    /// «non esiste».
    ///
    /// ── IL RISCHIO CHE LA VOCE NOMINA, E CHE QUI SI MISURA ───────────────
    ///
    /// *«Se l'API sostituisce il documento ma NON conserva gli accoppiamenti, si
    /// ripresenta identico il difetto tipico: pezzo diverso, nessun errore.»*
    ///
    /// `ReAttachMates` e' un parametro, quindi si puo' **chiedere** che li
    /// conservi. Chiedere non e' ottenere: si contano gli accoppiamenti **prima
    /// e dopo**, e se sono calati lo si dice a voce alta invece di dichiarare
    /// fatto. Un coperchio sostituito che ha perso i suoi vincoli e' un assieme
    /// che sembra montato e non lo e'.
    ///
    /// ── E IL CONTEGGIO NON BASTA: **RESTARE NON E' FUNZIONARE** ──────────
    ///
    /// Misurato dal banco, ed e' la cosa che questa voce ha trovato: i vincoli
    /// del pezzo vecchio **non si perdono** — due prima, due dopo — **e vanno
    /// in errore**, perche' puntavano a facce che non ci sono piu'. Il
    /// conteggio degli accoppiamenti non lo puo' vedere: da solo direbbe
    /// «tutto a posto». Lo vede `GetWhatsWrongCount`, che e' la stessa domanda
    /// che si fa un progettista guardando l'albero rosso.
    ///
    /// Percio' le misure sono DUE coppie prima/dopo e non una: quanti
    /// accoppiamenti, e quanti problemi. **Un vincolo rotto e' peggio di un
    /// vincolo assente**: assente si vede, rotto sembra che tenga.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome ReplaceComponent(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var assemblyDoc = model as IAssemblyDoc;
            if (assemblyDoc == null)
                return Failed(
                    "Questo non e' un assieme, e' un documento di tipo «" +
                    DocumentTypeName(TypeOf(model)) + "». I componenti si " +
                    "sostituiscono dentro un assieme.");

            object[] rawItems = null;
            try { rawItems = assemblyDoc.GetComponents(false) as object[]; } catch { }
            var components = DocumentState.ComponentsInOrder(rawItems);
            if (components.Count == 0)
                return Failed("L'assieme non ha componenti da sostituire.");

            string uid = Str(a, "component", null);
            var comp = PerUid(components, uid);
            if (comp == null) return Failed(NotFound(uid, "component", components));
            // TOP LEVEL ONLY (finding L11-5, lot Z-C21.5), with the check that
            // fix_component uses. Up to library 0.145.0 a nested component
            // went to ReplaceComponents2 as it was.
            string notTopLevel = NotTopLevel(assemblyDoc, comp, uid, components,
                "ReplaceComponents2 wants a top-level component: «The component must be a top-level " +
                "component. It cannot be a component of a sub-assembly» (IAssemblyDoc~ReplaceComponents2.html)");
            if (notTopLevel != null) return Failed(notTopLevel);

            string newItem = Str(a, "path", null);
            if (string.IsNullOrWhiteSpace(newItem))
                return Failed(
                    "Non mi hai detto con cosa sostituirlo: «path» e' il percorso " +
                    "completo del file nuovo, e dev'essere gia' aperto. Li trovi in " +
                    "«documents_open» dello stato.");
            if (!File.Exists(newItem))
                return Failed("Il file «" + newItem + "» non esiste. " + WhatIsOpen());
            if (!IsOpen(newItem))
                return Failed(
                    "«" + Path.GetFileName(newItem) + "» non e' aperto in SOLIDWORKS, e la " +
                    "sostituzione vuole un documento gia' aperto. " + WhatIsOpen());

            // SOSTITUIRE UN PEZZO CON SE STESSO NON E' UN'OPERAZIONE.
            // Riuscirebbe, non cambierebbe niente, e chi l'ha chiesta credeva di
            // aver cambiato il pezzo.
            string old = null;
            try { old = comp.GetPathName(); } catch { }
            if (!string.IsNullOrWhiteSpace(old) &&
                string.Equals(Path.GetFullPath(old), Path.GetFullPath(newItem),
                              StringComparison.OrdinalIgnoreCase))
                return Failed(
                    "«" + uid + "» usa gia' quel file. Sostituirlo con se stesso " +
                    "riuscirebbe e non cambierebbe niente: se volevi un altro pezzo, " +
                    "e' il percorso a essere sbagliato.");

            bool all = Bool(a, "all_instances", true);
            string configuration = Str(a, "configuration", "");

            // A CONFIGURATION THE NEW FILE DOES NOT HAVE IS REFUSED BEFORE THE
            // SWAP (Z-C21.4, finding M18). Up to library 0.145.0 any name went to
            // ReplaceComponents2 and nothing checked it. The names are read from
            // the new file's open document, the one the swap uses, and compared
            // whole (the part in angle brackets belongs to the name).
            if (!string.IsNullOrWhiteSpace(configuration))
            {
                var newDocument = OpenDocumentAtPath(newItem);
                var newConfigurations = newDocument == null ? new List<string>() : ConfigurationNames(newDocument);
                if (newConfigurations.Count == 0)
                    return Failed(
                        "I could not read the configurations of «" + Path.GetFileName(newItem) + "», so I cannot " +
                        "check «" + configuration + "» before the swap. Nothing was replaced.");
                if (!newConfigurations.Contains(configuration))
                    return Failed(
                        "«" + Path.GetFileName(newItem) + "» has no configuration «" + configuration + "». It has: " +
                        string.Join(", ", newConfigurations.ConvertAll(n => "«" + n + "»")) + ". The name is compared " +
                        "whole, the part in angle brackets included. Nothing was replaced.");
            }

            // ── `ReAttachMates` NON E' UNA SCELTA: E' SEMPRE `false` ─────
            //
            // Misurato il 17/9/2026, sei condizioni. Con `true` **e** il
            // componente che ha davvero degli accoppiamenti da riagganciare,
            // `ReplaceComponents2` solleva `RPC_E_SERVERFAULT` — sempre:
            // assieme salvato o no, file nuovo aperto o chiuso. Con `false`
            // passa sempre.
            //
            // **E qui mi ero sbagliato**, e va scritto dove sta il codice e non
            // solo nel changelog: da «con `false` passa» avevo DEDOTTO «con
            // `false` i vincoli si perdono». Il banco ha misurato il contrario —
            // due prima, due dopo, zero persi — e poi la cosa vera: **i vincoli
            // restano e vanno in ERRORE.** `ReAttachMates: false` non vuol dire
            // «scartali», vuol dire «non provare a riagganciarli in modo
            // interattivo». *Restare non e' funzionare*, ed e' esattamente
            // percio' che qui sotto non basta contarli.
            //
            // Quindi non si espone una manopola che puo' stare in una posizione
            // sola: *un argomento che non cambia niente e' peggio di un
            // argomento che non c'e', perche' chi lo mette crede di aver
            // deciso qualcosa.* Si passa `false` e si DICE cosa comporta.
            const bool riaggancia = false;

            // ── SI CONTA PRIMA, SU TUTTO L'ASSIEME ──────────────────────
            //
            // Non solo sul componente toccato: un accoppiamento lega DUE pezzi,
            // e perderlo si vede da tutti e due. Il totale e' la misura che non
            // dipende da quale maniglia sopravvive alla sostituzione.
            int accBefore = TotalMates(components);
            int howManyBefore = components.Count;
            int problemsBefore = HowManyProblems(model);

            // THE TARGET, WRITTEN DOWN BEFORE THE CALL (finding L11-5): its
            // name, and the components that ALREADY use the new file. After
            // the call these tell the component that took the target's place
            // from one that was on the new file all along.
            string targetName = NameOf(comp);
            var onNewFileBefore = NamesOnFile(rawItems, newItem);

            bool response;
            try
            {
                model.ClearSelection2(true);
                if (!SelectComponent(comp))
                    return Failed(
                        "Non sono riuscito a selezionare «" + uid + "»: senza selezione " +
                        "la sostituzione lavorerebbe su quello che capita, o su niente.");
                // MatchName, NOT ManuallySelect, UNTIL A PROBE (Z-C21.4, findings
                // M18 and N57). This comment used to say that ManuallySelect opens
                // a window to pick the configuration by hand, and a modal window
                // hangs the COM call for ever (the trap of AddDimension). That was
                // never measured, and the guide says otherwise:
                // swReplaceComponentsConfiguration_e.html, «ManuallySelect 1 = Use
                // the specified configuration», «MatchName 0 = Let SOLIDWORKS
                // attempt to match the configuration of the old components with a
                // configuration in the replacement component». So a non-empty
                // «configuration» may not be what the component ends up with:
                // it is checked before the call (a name the file lacks is
                // refused) and the configuration really referenced is read back
                // after it, with a mismatch flagged. Moving to ManuallySelect
                // waits for the N57 probe in a throwaway container: a window here
                // would hang the bench and the bridge with nobody to close it.
                //
                // UNSAVED EDITS ARE NOT LOST (finding L11-4, NOT REAL). The doubt
                // was that replacing a component whose part, old or new, has
                // unsaved changes would drop them. The Z-C21.3 probe of L11-4
                // (28/9/2026) edited the old part (16000 -> 32000 mm3), the new
                // one (9000 -> 18000), or both, without saving, and called
                // ReplaceComponents2 as here: true every time, the assembly took
                // the new part's unsaved volume, and both parts kept their edits
                // in memory, still dirty (the files on disk untouched). So
                // nothing is saved first.
                response = assemblyDoc.ReplaceComponents2(
                    newItem, configuration ?? "", all,
                    (int)swReplaceComponentsConfiguration_e.swReplaceComponentsConfiguration_MatchName,
                    riaggancia);
            }
            catch (Exception ex)
            {
                return Failed("Non sono riuscito a sostituire il componente: " + ex.Message);
            }
            finally { try { model.ClearSelection2(true); } catch { } }

            try { model.EditRebuild3(); } catch { }

            // ── E SI CONTA DOPO ─────────────────────────────────────────
            object[] rawAfter = null;
            try { rawAfter = assemblyDoc.GetComponents(false) as object[]; } catch { }
            var afterComp = DocumentState.ComponentsInOrder(rawAfter);
            int accAfter = TotalMates(afterComp);

            // THE MEASURE IS NOT THE BOOLEAN, AND NOT «SOME COMPONENT USES THE
            // NEW FILE» EITHER (finding L11-5, lot Z-C21.5). Up to library
            // 0.145.0 any component on the new file made the swap «done»: with
            // the new part already mounted elsewhere, a swap that changed
            // nothing was reported as made. Now the question is about the
            // TARGET: (a) a component is on the new file that was not on it
            // before, and (b) the target's old name no longer designates a
            // component on another file. (b) holds whether SOLIDWORKS renames
            // the replaced component after the new file (the name is gone) or
            // keeps its name (it now uses the new file, and it is the one in
            // (a)); which of the two it does is a user option, and the check
            // does not depend on it.
            var onNewFileAfter = NamesOnFile(rawAfter, newItem);
            var tookThePlace = new List<string>();
            foreach (var n in onNewFileAfter)
                if (!onNewFileBefore.Contains(n)) tookThePlace.Add(n);
            tookThePlace.Sort(StringComparer.Ordinal);
            bool targetGone;
            string targetNow = PathOfNamed(rawAfter, targetName, out targetGone);
            bool targetOnNewFile = targetGone || SameFile(targetNow, newItem);
            bool madeIt = targetName != null && tookThePlace.Count > 0 && targetOnNewFile;

            if (!madeIt)
                return Failed(
                    "ReplaceComponents2 answered " + (response ? "true" : "false") + ", but " +
                    (targetName == null
                        ? "the name of «" + uid + "» did not answer before the call, so I cannot tell what became of it"
                        : !targetOnNewFile
                            ? "«" + uid + "» («" + targetName + "») still uses «" +
                              (string.IsNullOrWhiteSpace(targetNow) ? "(no path)" : Path.GetFileName(targetNow)) + "»"
                            : "no component took up «" + Path.GetFileName(newItem) + "» that was not on it before" +
                              (onNewFileBefore.Count == 0 ? "" : " (" + onNewFileBefore.Count + " already used it, " +
                                                                 "and they do not count)")) +
                    ". I did not count it as done. Read the assembly state again: if something did change, " +
                    "undo the step with «undo»; if nothing did, ask again with the new file open.");

            var measurements = new Dictionary<string, object>
            {
                ["component"] = uid,
                ["source"] = string.IsNullOrWhiteSpace(old) ? "(senza percorso)" : Path.GetFileName(old),
                ["target"] = Path.GetFileName(newItem),
                ["components_before"] = howManyBefore,
                ["components_after"] = afterComp.Count,
                ["mates_before"] = accBefore,
                ["mates_after"] = accAfter,
                ["mates_lost"] = Math.Max(0, accBefore - accAfter),
                // L11-5: the components that took up the new file with this
                // swap, by name (the replaced one, and its other instances when
                // all instances were asked for). The C1..Cn numbers follow the
                // names, so read the state again before naming a component by uid.
                ["took_its_place"] = tookThePlace.ToArray(),
            };

            // THE CONFIGURATION, READ BACK (Z-C21.4, findings M18 and N57): the
            // one each component that took up the new file references now, one
            // name when they agree, all of them when they do not. When one was
            // asked for, «configuration_as_asked» says whether every one of them
            // references it; a mismatch is FLAGGED (the verdict opens with it),
            // not undone: the swap has happened, and the way back is «undo».
            var referencedNow = new List<string>();
            bool referencedUnread = false;
            foreach (var v in afterComp)
            {
                if (!tookThePlace.Contains(v.Key)) continue;
                string itsConfiguration = null;
                try { itsConfiguration = v.Value.ReferencedConfiguration; } catch { }
                if (string.IsNullOrWhiteSpace(itsConfiguration)) { referencedUnread = true; continue; }
                if (!referencedNow.Contains(itsConfiguration)) referencedNow.Add(itsConfiguration);
            }
            if (referencedNow.Count == 1 && !referencedUnread) measurements["configuration"] = referencedNow[0];
            else if (referencedNow.Count > 0) measurements["configuration"] = referencedNow.ToArray();
            bool? configurationAsAsked = null;
            if (!string.IsNullOrWhiteSpace(configuration))
            {
                configurationAsAsked = !referencedUnread && referencedNow.Count == 1 &&
                                       string.Equals(referencedNow[0], configuration, StringComparison.Ordinal);
                measurements["configuration_asked"] = configuration;
                measurements["configuration_as_asked"] = (bool)configurationAsAsked;
            }

            // ── I VINCOLI CI SONO ANCORA. FUNZIONANO? ───────────────────
            //
            // Sono due domande diverse, e il conteggio risponde solo alla
            // prima. Un vincolo che puntava a una faccia del pezzo vecchio
            // resta nell'albero e va in ERRORE: il conteggio lo vede ancora e
            // direbbe «due prima, due adesso».
            int problemsAfter = HowManyProblems(model);
            if (problemsBefore >= 0 && problemsAfter >= 0)
            {
                measurements["problems_before"] = problemsBefore;
                measurements["problems_after"] = problemsAfter;
            }
            bool brokenOnes = problemsBefore >= 0 && problemsAfter > problemsBefore;

            // ── IL GIUDIZIO, che e' il punto della voce ─────────────────
            string verdictValue;
            string done = "Sostituito «" + measurements["source"] + "» con «" + measurements["target"] +
                           "» su " + uid + ". ";
            if (brokenOnes)
                // IL CASO PEGGIORE, E NON E' «PERSI»: i vincoli ci sono ancora e
                // sono ROTTI. Zero vincoli si vede; due vincoli in errore
                // sembrano vincolare.
                verdictValue = done + "ATTENZIONE: gli accoppiamenti ci sono ancora (" +
                           accAfter + ") ma il documento ha " + problemsAfter +
                           " problemi contro i " + problemsBefore + " di prima: **qualcuno " +
                           "di quei vincoli e' in ERRORE**, perche' puntava a una faccia " +
                           "del pezzo vecchio. Un vincolo rotto e' peggio di un vincolo " +
                           "assente: sembra che tenga. Toglilo e rimettilo con «mate», " +
                           "oppure «undo».";
            else if (accBefore == 0)
                verdictValue = done + "Non c'erano accoppiamenti da conservare.";
            else if (accAfter >= accBefore)
                verdictValue = done + "Gli accoppiamenti sono rimasti e sono sani: " +
                           accAfter + (accAfter == 1 ? " vincolo" : " vincoli") +
                           ", e il documento non ha problemi nuovi.";
            else
                verdictValue = done + "**GLI ACCOPPIAMENTI SI SONO PERSI**: ne aveva " +
                           accBefore + ", adesso ne ha " + accAfter + ". Il pezzo nuovo e' " +
                           "dentro e **non e' vincolato**: l'assieme sembra montato e non " +
                           "lo e'. Rimettili con «mate», oppure «undo».";
            if (configurationAsAsked == false)
                verdictValue = "WARNING: you asked for the configuration «" + configuration + "», and the new " +
                           "component references " +
                           (referencedNow.Count == 0
                               ? "a configuration I could not read"
                               : string.Join(", ", referencedNow.ConvertAll(n => "«" + n + "»"))) +
                           (referencedUnread && referencedNow.Count > 0 ? " (and one I could not read)" : "") +
                           ". SOLIDWORKS matched the configuration of the old component by name; the choice " +
                           "that takes the named one is not used yet (see «configuration_as_asked»). Do not " +
                           "build on it as if it were «" + configuration + "»: «undo» takes the swap back. " +
                           verdictValue;
            measurements["verdict"] = verdictValue;
            _log("[agente] " + verdictValue);

            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// Seleziona un COMPONENTE. Non e' come selezionare una faccia.
        ///
        /// ── L'ERRORE CHE QUESTO METODO ESISTE PER NON RIPETERE ──────────
        ///
        /// La prima stesura faceva `(comp as IEntity).Select4(false, null)`,
        /// che e' come si selezionano le facce e gli spigoli in tutto il resto
        /// di questo progetto. **`IComponent2` non si casta a `IEntity`:** il
        /// cast torna `null`, e la chiamata esplode con «Riferimento a un
        /// oggetto non impostato» — un messaggio che parla di tutt'altro.
        ///
        /// `IComponent2` ha un `Select4` **suo**, con tre parametri invece di
        /// due:
        ///
        ///     Boolean Select4(Boolean Append, SelectData Data, Boolean ShowPopup)
        ///
        /// **Il terzo va `false`.** `ShowPopup: true` aprirebbe una finestra, e
        /// una finestra modale appende la chiamata COM con il ponte dietro: da
        /// qui non c'e' nessuno che possa chiuderla. E' la stessa trappola di
        /// `AddDimension` e di `ManuallySelect` qui sopra — **tre chiamate
        /// diverse, nella stessa operazione, che sanno aprire una finestra.**
        ///
        /// *Due metodi con lo stesso nome su due interfacce diverse, con firme
        /// diverse: il compilatore non aiuta, perche' il cast sbagliato compila
        /// benissimo e fallisce a runtime.*
        /// </summary>
        private static bool SelectComponent(IComponent2 c)
        {
            if (c == null) return false;
            try { return c.Select4(false, null, false); }
            catch { return false; }
        }

        /// <summary>
        /// Quanti accoppiamenti in tutto l'assieme, contando per componente.
        ///
        /// **E' un conteggio di ESTREMI, non di accoppiamenti**: ogni vincolo
        /// lega due pezzi e viene contato due volte. Non si divide per due,
        /// perche' non tutti i vincoli legano esattamente due componenti e
        /// dividere introdurrebbe un numero che a volte e' mezzo. Qui serve solo
        /// per il CONFRONTO prima/dopo, e per quello un conteggio doppio va bene
        /// quanto uno singolo — purche' si sappia che e' doppio, ed e' per
        /// questo che c'e' scritto.
        /// </summary>
        private static int TotalMates(
            List<KeyValuePair<string, IComponent2>> components)
        {
            int n = 0;
            foreach (var v in components)
            {
                int itsItems = MatesOf(v.Value);
                if (itsItems > 0) n += itsItems;
            }
            return n;
        }

        /// <summary>
        /// L11-5: the names (Name2) of the components, among those of
        /// GetComponents, whose file is <paramref name="path"/>. A component
        /// whose name or path does not answer is left out.
        /// </summary>
        private static HashSet<string> NamesOnFile(object[] components, string path)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in components ?? new object[0])
            {
                var component = o as IComponent2;
                if (component == null) continue;
                string name = null, itsPath = null;
                try { name = component.Name2; itsPath = component.GetPathName(); } catch { }
                if (!string.IsNullOrEmpty(name) && SameFile(itsPath, path)) names.Add(name);
            }
            return names;
        }

        /// <summary>
        /// L11-5: the file of the component called <paramref name="name"/>,
        /// among those of GetComponents; <paramref name="gone"/> is true when
        /// no component has that name any more (false, and null returned,
        /// when there is no name to look for).
        /// </summary>
        private static string PathOfNamed(object[] components, string name, out bool gone)
        {
            gone = false;
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var o in components ?? new object[0])
            {
                var component = o as IComponent2;
                if (component == null) continue;
                string itsName = null;
                try { itsName = component.Name2; } catch { }
                if (!string.Equals(itsName, name, StringComparison.Ordinal)) continue;
                string itsPath = null;
                try { itsPath = component.GetPathName(); } catch { }
                return itsPath;
            }
            gone = true;
            return null;
        }

        /// <summary>Two paths name the same file (full paths, case ignored); false if either is empty or not a path.</summary>
        private static bool SameFile(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
