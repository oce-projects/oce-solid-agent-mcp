using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// REPLACING A COMPONENT (entry GA7).
    ///
    /// ── THE SIGNATURE WAS THERE, THE CENSUS LOOKED AT THE WRONG INTERFACE ───
    ///
    /// The entry said: *«the census 80q does NOT list a dedicated signature;
    /// `IComponent2` has 170 methods and almost certainly something like
    /// `ReplaceComponent` exists, but it has to be looked for and read, not assumed»*, and from there the
    /// «not estimable».
    ///
    /// **It is not on `IComponent2`, and never was.** It is on
    /// `IAssemblyDoc`:
    ///
    ///     Boolean ReplaceComponents2(String FileName, String ConfigName,
    ///                                Boolean ReplaceAllInstance,
    ///                                Int32 UseConfigChoice,
    ///                                Boolean ReAttachMates)
    ///
    /// It is the same lesson already paid for twice in this project:
    /// *a name says what a method is called, not which interface it is on*
    /// (note `81s`, `AddConfiguration3`). Searching through a hundred and seventy methods
    /// on the wrong object finds nothing, and «I did not find it» becomes
    /// «it does not exist».
    ///
    /// ── THE RISK THAT THE ENTRY NAMES, AND THAT IS MEASURED HERE ─────────
    ///
    /// *«If the API replaces the document but does NOT keep the mates, the
    /// typical defect shows up identical again: different part, no error.»*
    ///
    /// `ReAttachMates` is a parameter, so it can be **asked** to keep
    /// them. Asking is not getting: the mates are counted **before
    /// and after**, and if they dropped it is said out loud instead of declaring
    /// it done. A replaced cover that has lost its constraints is an assembly
    /// that looks assembled and is not.
    ///
    /// ── AND THE COUNT IS NOT ENOUGH: **REMAINING IS NOT WORKING** ─────────
    ///
    /// Measured by the bench, and it is what this entry found: the constraints
    /// of the old part **are not lost** — two before, two after — **and go
    /// into error**, because they pointed to faces that no longer exist. The
    /// count of the mates cannot see it: on its own it would say
    /// «all fine». `GetWhatsWrongCount` sees it, which is the same question
    /// that a designer asks looking at the red tree.
    ///
    /// Therefore the measures are TWO before/after pairs and not one: how many
    /// mates, and how many problems. **A broken constraint is worse than an
    /// absent constraint**: an absent one is seen, a broken one seems to hold.
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

            // REPLACING A PART WITH ITSELF IS NOT AN OPERATION.
            // It would succeed, it would change nothing, and whoever asked for it believed they
            // had changed the part.
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

            // ── `ReAttachMates` IS NOT A CHOICE: IT IS ALWAYS `false` ─────
            //
            // Measured on 17/9/2026, six conditions. With `true` **and** a
            // component that really has mates to re-attach,
            // `ReplaceComponents2` raises `RPC_E_SERVERFAULT` — always:
            // assembly saved or not, new file open or closed. With `false`
            // it always goes through.
            //
            // **And here I was wrong**, and it must be written where the code is and not
            // only in the changelog: from «with `false` it goes through» I had DEDUCED «with
            // `false` the constraints are lost». The bench measured the opposite —
            // two before, two after, zero lost — and then the true thing: **the constraints
            // remain and go into ERROR.** `ReAttachMates: false` does not mean
            // «discard them», it means «do not try to re-attach them
            // interactively». *Remaining is not working*, and it is exactly
            // for that reason that counting them below is not enough.
            //
            // So a knob that can sit in only one position is not exposed:
            // *an argument that changes nothing is worse than an
            // argument that is not there, because whoever sets it believes they have
            // decided something.* `false` is passed and what it entails is SAID.
            const bool riaggancia = false;

            // ── IT IS COUNTED FIRST, ON THE WHOLE ASSEMBLY ──────────────
            //
            // Not only on the touched component: a mate ties TWO parts,
            // and losing it is seen from both. The total is the measure that does not
            // depend on which handle survives the replacement.
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

            // ── AND IT IS COUNTED AFTER ─────────────────────────────────
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

            // ── THE CONSTRAINTS ARE STILL THERE. DO THEY WORK? ──────────
            //
            // They are two different questions, and the count answers only the
            // first. A constraint that pointed to a face of the old part
            // stays in the tree and goes into ERROR: the count still sees it and
            // would say «two before, two now».
            int problemsAfter = HowManyProblems(model);
            if (problemsBefore >= 0 && problemsAfter >= 0)
            {
                measurements["problems_before"] = problemsBefore;
                measurements["problems_after"] = problemsAfter;
            }
            bool brokenOnes = problemsBefore >= 0 && problemsAfter > problemsBefore;

            // ── THE JUDGMENT, which is the point of the entry ────────────
            string verdictValue;
            string done = "Sostituito «" + measurements["source"] + "» con «" + measurements["target"] +
                           "» su " + uid + ". ";
            if (brokenOnes)
                // THE WORST CASE, AND IT IS NOT «LOST»: the constraints are still there and
                // are BROKEN. Zero constraints is seen; two constraints in error
                // seem to constrain.
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
        /// Selects a COMPONENT. It is not like selecting a face.
        ///
        /// ── THE MISTAKE THIS METHOD EXISTS NOT TO REPEAT ────────────────
        ///
        /// The first draft did `(comp as IEntity).Select4(false, null)`,
        /// which is how faces and edges are selected in the whole rest
        /// of this project. **`IComponent2` cannot be cast to `IEntity`:** the
        /// cast returns `null`, and the call blows up with «Riferimento a un
        /// oggetto non impostato» — a message that talks about something entirely different.
        ///
        /// `IComponent2` has a `Select4` **of its own**, with three parameters instead of
        /// two:
        ///
        ///     Boolean Select4(Boolean Append, SelectData Data, Boolean ShowPopup)
        ///
        /// **The third goes `false`.** `ShowPopup: true` would open a window, and
        /// a modal window hangs the COM call with the bridge behind it: from
        /// here there is nobody who can close it. It is the same trap as
        /// `AddDimension` and `ManuallySelect` above — **three different
        /// calls, in the same operation, that know how to open a window.**
        ///
        /// *Two methods with the same name on two different interfaces, with different
        /// signatures: the compiler does not help, because the wrong cast compiles
        /// perfectly well and fails at runtime.*
        /// </summary>
        private static bool SelectComponent(IComponent2 c)
        {
            if (c == null) return false;
            try { return c.Select4(false, null, false); }
            catch { return false; }
        }

        /// <summary>
        /// How many mates in the whole assembly, counting per component.
        ///
        /// **It is a count of ENDS, not of mates**: every constraint
        /// ties two parts and is counted twice. It is not divided by two,
        /// because not all constraints tie exactly two components and
        /// dividing would introduce a number that is sometimes a half. Here it is needed only
        /// for the before/after COMPARISON, and for that a double count is as good
        /// as a single one — provided it is known to be double, and that is
        /// why it is written down.
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
