using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// WHAT THE PART SAYS ABOUT ITSELF, AND ITS VARIANTS. (entry GP12)
    ///
    /// Drawing number, revision, customer: custom properties are
    /// what ends up in the title block and in the PDM data card. Up to here
    /// `apply_material` was the only hook between the catalogue and that world.
    ///
    /// And configurations are the same family as left/right brackets,
    /// with and without a hole: a standard request of a workshop that works from drawings.
    ///
    /// ── THE TWO ROADS ARE THE SAME ROAD ─────────────────────────────────
    ///
    /// The entry asked to establish «whether writing on
    /// `IConfiguration.get_CustomPropertyManager()` or passing the name to
    /// `IModelDocExtension.get_CustomPropertyManager(nome)` gives equivalent
    /// results: two roads never compared». Measured on 16/9/2026:
    ///
    ///   written «Hand» = SX via Extension(nome)
    ///       re-read via Extension(nome)  ->  «SX»
    ///       re-read via IConfiguration   ->  «SX»
    ///   written «Revision» = B via IConfiguration
    ///       re-read via IConfiguration   ->  «B»
    ///       re-read via Extension(nome)  ->  «B»
    ///
    /// **They are equivalent.** Here `Extension(nome)` is used, which does not need
    /// the configuration object in hand.
    ///
    /// ── BUT THE DOCUMENT AND THE CONFIGURATION ARE TWO DRAWERS ─────────
    ///
    /// And this was NOT something that was known:
    ///
    ///   «Numero disegno», written on the DOCUMENT, read from the config  -> not there
    ///   «Hand», written on the CONFIG, read from the document            -> not there
    ///
    /// They are not inherited and they are not seen. Whoever writes the drawing number on the
    /// document and then reads the properties of a configuration **does not
    /// find it**, and nobody tells them why. Therefore here the place where it is
    /// written is an explicit argument, and it comes back in the answer.
    ///
    /// ── THE NAME OF THE CONFIGURATION IS NOT WHAT IT SEEMS ─────────────
    ///
    ///   ActiveConfiguration.Name       ->  «Default&lt;A macchina&gt;»
    ///   ShowConfiguration2("Default")  ->  FALSE
    ///   ShowConfiguration2("Default&lt;A macchina&gt;")  ->  TRUE
    ///
    /// The angle brackets are **inside the name**, and they are not something else
    /// that the name carries around: a part freshly born from this company's
    /// template has TWO real configurations — «Default&lt;A macchina&gt;» and
    /// «Default&lt;Saldato&gt;», the weldment pair — and `GetConfigurationCount()`
    /// answers **2**. (The display states are another thing
    /// altogether: they live on `IConfiguration.GetDisplayStates()`, here there is one
    /// per configuration and it is called «&lt;Default&gt;_Stato di visualizzazione 1».)
    ///
    /// The only good source is `GetConfigurationNames()`: names are not
    /// constructed, they are asked for.
    ///
    /// ── THE SAME NAME ON TWO INTERFACES, WITH DIFFERENT SIGNATURES ─────
    ///
    /// The entry warned of a «twin-but-different signature». It is worse: there are
    /// TWO FAMILIES of these methods, on two interfaces, and the names repeat.
    ///
    ///   IModelDoc2.AddConfiguration (8 par.)  -> void
    ///   IModelDoc2.AddConfiguration2(8 par.)  -> bool
    ///   IModelDoc2.AddConfiguration3(Name, Comment, AlternateName, Options)
    ///                                         -> object
    ///   IConfigurationManager.AddConfiguration (6 par.)  -> IConfiguration
    ///   IConfigurationManager.AddConfiguration2(7 par., with Rebuild)
    ///                                         -> IConfiguration
    ///
    /// A method looked up on the wrong interface «does not exist» — and it is
    /// exactly the mistake we made before looking: `AddConfiguration3`
    /// **is there**, but on `IModelDoc2`. *A name says what a method is called, not
    /// which interface it is on.*
    ///
    /// Here **`IConfigurationManager.AddConfiguration2`** is used, and not out of
    /// seniority: it is the only one that returns the **configuration object** (from which the
    /// real name is re-read) and that takes `Rebuild`. The three on `IModelDoc2`
    /// return `void`, `bool` and `object`: the first cannot even be
    /// checked.
    ///
    /// **It activates the new configuration at once**, without being asked to:
    /// it is said in the answer, because the following steps work on that one.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome SetProperty(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string itemName = Str(a, "name", null);
            if (string.IsNullOrWhiteSpace(itemName))
                return Failed(
                    "Manca il «name» della proprieta', per esempio «Numero disegno» o " +
                    "«Revision».");
            string val = Str(a, "value_text", null);
            if (val == null)
                return Failed(
                    "Manca il «value_text». Se vuoi svuotare la proprieta', passa una stringa " +
                    "vuota: cancellarla e' un'altra cosa, e non la faccio di nascosto.");

            string configuration = Str(a, "configuration", null);
            bool onConfiguration = !string.IsNullOrWhiteSpace(configuration);
            if (onConfiguration)
            {
                string why;
                configuration = TrueConfigurationName(model, configuration, out why);
                if (configuration == null) return Failed(why);
            }

            // NO ACTIVATION AROUND THIS, ON PURPOSE (Z-C21.4, finding L8-5, NOT
            // REAL). The doubt was that writing and reading the properties of a
            // configuration that was never activated would move the active one,
            // or read stale values, and needed ShowConfiguration2 before and a
            // restore after. The Z-C21.3 probe of L8-5, Get6 on a configuration
            // never activated (28/9/2026, SOLIDWORKS 2021 SP5.1, company part
            // template), did
            // Add3, Get6 with UseCached false, Get6 of a formula property and
            // ForceRebuild3 on configurations never activated, through this very
            // get_CustomPropertyManager(name) and through
            // IConfiguration.CustomPropertyManager: six cases, the active
            // configuration never moved, and the formula resolved to the same
            // value before and after activating that configuration. Activating
            // here would change what the user sees and add a restore that can
            // fail, to fix nothing: do not add it without a probe that shows a
            // case the one above did not cover.
            ICustomPropertyManager propertyManager;
            try
            {
                propertyManager = model.Extension.get_CustomPropertyManager(
                    onConfiguration ? configuration : "") as ICustomPropertyManager;
            }
            catch (Exception ex) { return Failed("Non riesco ad arrivare alle proprieta': " + ex.Message); }
            if (propertyManager == null)
                return Failed("Questo documento non espone le proprieta' personalizzate.");

            int outcome;
            try
            {
                outcome = propertyManager.Add3(
                    itemName,
                    (int)swCustomInfoType_e.swCustomInfoText,
                    val,
                    // IT IS REPLACED, and it is said: «only if new» would leave the
                    // old value and answer all the same, which is the most
                    // comfortable way of believing one has written a revision.
                    (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            }
            catch (Exception ex) { return Failed("La proprieta' non e' stata scritta: " + ex.Message); }

            if (outcome != (int)swCustomInfoAddResult_e.swCustomInfoAddResult_AddedOrChanged)
                return Failed(
                    "SOLIDWORKS ha rifiutato la proprieta' «" + itemName + "»: " +
                    PropertyOutcomeName(outcome) + ".");

            // IT IS ALWAYS RE-READ. `Add3` answers a code, not the value: the
            // value is told only by whoever re-reads it.
            //
            // THE TEXT WRITTEN IS COMPARED WITH THE TEXT READ (Z-C21.8, finding
            // M35). ICustomPropertyManager~Get6.html gives two values: ValOut,
            // the text of the property, and ResolvedValOut, «Evaluated value».
            // The check compared the EVALUATED value with the text written, so a
            // formula came back as its result and an exact write was refused:
            // the Z-C21.3 probe of L8-5 wrote «"SW-Mass@@...SLDPRT"» and read
            // ValOut the same text, ResolvedValOut «0.016». The text is compared
            // with ValOut (PropertyWrittenAsAsked); the evaluated value is
            // reported next to it when it differs («resolved_value»).
            string read, resolved;
            bool present = ReadPropertyBack(propertyManager, itemName, out read, out resolved);
            if (!PropertyWrittenAsAsked(val, present, read))
                return Failed(
                    "Ho scritto «" + val + "» in «" + itemName + "» e rileggendo trovo " +
                    (read == null ? "il vuoto" : "«" + read + "»") + ". Non ti confermo " +
                    "una scheda dati che non e' quella che credi.");

            // AND THE OTHER DRAWER IS LOOKED AT TOO.
            //
            // Not to write: to SAY that it is not there. It is the difference
            // between a note in the catalogue, which whoever reads the answer may never
            // have read, and a number measured on this part now.
            // And it is the only way for the bench to see the day
            // SOLIDWORKS changed its mind and began to inherit them.
            string otherList = onConfiguration
                ? "the document"
                : "la configurazione attiva";
            bool seenOnTheOtherSide = false;
            try
            {
                string whereToLook = "";
                if (!onConfiguration)
                {
                    var handler = model.ConfigurationManager as IConfigurationManager;
                    whereToLook = handler == null ? null : handler.ActiveConfiguration.Name;
                    otherList = "la configurazione «" + whereToLook + "»";
                }
                if (whereToLook != null)
                {
                    var overThere = model.Extension.get_CustomPropertyManager(whereToLook)
                               as ICustomPropertyManager;
                    seenOnTheOtherSide = overThere != null && ReadProperty(overThere, itemName) != null;
                }
            }
            catch { }

            var measurements = new Dictionary<string, object>
            {
                ["property"] = itemName,
                ["value_text"] = read,
                ["location"] = onConfiguration
                    ? "la configurazione «" + configuration + "»"
                    : "the document",
                ["visible_from_other_list"] = seenOnTheOtherSide,
                // THE TWO DRAWERS ARE SEPARATE, and it must be said every time: it is the
                // thing nobody expects.
                ["two_lists"] =
                    "Le proprieta' del documento e quelle di una configurazione sono due " +
                    "elenchi separati: non si ereditano e non si vedono a vicenda. Ho " +
                    "guardato: da " + otherList + " questa proprieta' " +
                    (seenOnTheOtherSide ? "SI VEDE." : "NON si vede."),
            };
            if (onConfiguration) measurements["configuration"] = configuration;
            if (resolved != null && !string.Equals(resolved, read, StringComparison.Ordinal))
                measurements["resolved_value"] = resolved;

            _log("[agente] proprieta' «" + itemName + "» = «" + read + "» su " +
                 (onConfiguration ? configuration : "documento"));
            return SucceededWithoutCreating(measurements);
        }

        private Outcome CreateConfiguration(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string itemName = Str(a, "name", null);
            if (string.IsNullOrWhiteSpace(itemName))
                return Failed("Manca il «name» della configurazione, per esempio «Left».");

            var handler = model.ConfigurationManager as IConfigurationManager;
            if (handler == null)
                return Failed("Questo documento non espone le configurazioni.");

            var alreadyThere = ConfigurationNames(model);
            if (alreadyThere.Any(n => string.Equals(n, itemName, StringComparison.OrdinalIgnoreCase) ||
                               n.StartsWith(itemName + "<", StringComparison.OrdinalIgnoreCase)))
                return Failed(
                    "Una configurazione «" + itemName + "» c'e' gia'. Ci sono: " +
                    string.Join(", ", alreadyThere.Select(n => "«" + n + "»")) + ".");

            IConfiguration newOne;
            try
            {
                // `IConfigurationManager.AddConfiguration2`, not one of the three
                // that are called almost the same on `IModelDoc2`: only this one
                // returns the configuration object. See the comment at the top.
                newOne = handler.AddConfiguration2(
                    itemName,                              // Name
                    Str(a, "comment", "") ?? "",      // Comment
                    "",                                // AlternateName
                    0,                                 // Options
                    "",                                // ParentConfigName
                    Str(a, "details", "") ?? "",   // Description
                    true) as IConfiguration;           // Rebuild
            }
            catch (Exception ex) { return Failed("La configurazione non e' stata creata: " + ex.Message); }

            if (newOne == null)
                return Failed(
                    "La configurazione «" + itemName + "» non e' stata creata, e SOLIDWORKS non " +
                    "dice perche'. Di solito e' il nome: certi caratteri non si possono usare.");

            string activate = "";
            try { activate = handler.ActiveConfiguration.Name; } catch { }

            return SucceededWithoutCreating(new Dictionary<string, object>
            {
                ["configuration"] = newOne.Name,
                ["active_now"] = activate,
                ["configurations"] = ConfigurationNames(model).ToArray(),
                // IT ACTIVATES BY ITSELF, and the following steps work on that one.
                ["note_activation"] =
                    "Creando una configurazione SOLIDWORKS la ATTIVA: adesso il documento e' " +
                    "su «" + activate + "», e i passi che seguono lavorano li'. Con " +
                    "«activate_configuration» si torna a un'altra.",
                ["note_name"] =
                    "Il nome buono per le altre operazioni e' quello scritto qui: puo' avere " +
                    "un suffisso fra parentesi angolari (lo stato di visualizzazione) che fa " +
                    "parte del nome.",
            });
        }

        private Outcome ActivateConfiguration(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string requested = Str(a, "name", null);
            if (string.IsNullOrWhiteSpace(requested))
                return Failed("Manca il «name» della configurazione da attivare.");

            string why;
            string itemName = TrueConfigurationName(model, requested, out why);
            if (itemName == null) return Failed(why);

            bool done;
            try { done = model.ShowConfiguration2(itemName); }
            catch (Exception ex) { return Failed("Non sono riuscito ad attivarla: " + ex.Message); }

            string activate = "";
            try
            {
                var handler = model.ConfigurationManager as IConfigurationManager;
                if (handler != null) activate = handler.ActiveConfiguration.Name;
            }
            catch { }

            // ── THE BOOLEAN IS NOT THE CRITERION, AND NOT BECAUSE IT IS LAZY ──
            //
            // `ShowConfiguration2` answers **false even when the
            // configuration asked for is already the active one**. Measured by the
            // bench on 16/9/2026: two red cases with the message «I asked
            // to activate "Left" and now "Left" is active» — that is the
            // refusal said by itself that the refusal was wrong.
            //
            // That `false` does not tell «I did not do it» from «there was nothing
            // to do». The only thing that tells the two apart is **which
            // configuration is active now**, which is also the only thing that whoever
            // reads really cares about.
            //
            // The boolean is kept, but as a detail in the message: it serves
            // whoever will have to understand why, the day this fired.
            if (!string.Equals(activate, itemName, StringComparison.Ordinal))
                return Failed(
                    "Ho chiesto di attivare «" + itemName + "» e adesso e' attiva " +
                    (string.IsNullOrEmpty(activate) ? "non so quale" : "«" + activate + "»") +
                    " (SOLIDWORKS ha risposto " + (done ? "di si'" : "di no") + "). Non ti " +
                    "confermo di lavorare su una configurazione diversa da quella che credi.");

            return SucceededWithoutCreating(new Dictionary<string, object>
            {
                ["configuration"] = activate,
                ["configurations"] = ConfigurationNames(model).ToArray(),
                // «it was already this one»: whoever reads must be able to tell a
                // move from a non-move, because SOLIDWORKS does not
                // tell them apart (see above) and the difference matters when the following
                // step takes for granted that it has just arrived.
                ["was_already_active"] = !done,
            });
        }

        // =================================================================

        /// <summary>
        /// The name of a configuration, verified against those that the
        /// document declares. Null — with the reason — when it is not there.
        ///
        /// ── WHY THE SHORT NAME IS NOT GUESSED ─────────────────────────────
        ///
        /// `ShowConfiguration2("Default")` answers **false** if the
        /// configurations are called «Default&lt;A macchina&gt;» and
        /// «Default&lt;Saldato&gt;»: the piece between angle brackets is part of
        /// the name.
        ///
        /// The temptation is to translate the short name into the long one. It is not done,
        /// for two reasons that have both been seen:
        ///
        ///   * **that short name is AMBIGUOUS.** A part freshly born from this
        ///     company's template has TWO real configurations that begin with
        ///     «Default» — `GetConfigurationCount()` answers 2. Whoever translated
        ///     would choose, and choosing here means activating the wrong
        ///     variant without anything giving an error;
        ///   * **a road that is never taken is not known to work.** If
        ///     the unique case were really unique, the branch «a single candidate»
        ///     would stay untried — and an untried branch in the middle of a
        ///     choice of configuration is worse than a refusal.
        ///
        /// So: **the name is compared and that is all**, and the message teaches
        /// why — which is the only thing that is really needed here, because whoever
        /// calls has the good names, in the document state.
        /// </summary>
        private static string TrueConfigurationName(
            IModelDoc2 model, string requested, out string why)
        {
            why = null;
            var names = ConfigurationNames(model);
            if (names.Count == 0)
            {
                why = "Questo documento non dichiara nessuna configurazione.";
                return null;
            }
            foreach (var n in names)
                if (string.Equals(n, requested, StringComparison.Ordinal)) return n;

            // If the name asked for is the prefix of one or more real names, almost
            // always whoever calls did not know about the suffix: they are told so, instead
            // of just listing.
            var resembling = names
                .Where(n => n.StartsWith(requested + "<", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (resembling.Count > 0)
            {
                why =
                    "«" + requested + "» non e' un nome intero. Il documento dichiara " +
                    string.Join(" e ", resembling.Select(n => "«" + n + "»")) +
                    ": il pezzo fra parentesi angolari fa parte del nome, e va copiato. " +
                    "Chiesto cosi' com'e', SOLIDWORKS risponde di no in silenzio.";
                return null;
            }
            why =
                "Non trovo la configurazione «" + requested + "». Ci sono: " +
                string.Join(", ", names.Select(n => "«" + n + "»")) + ".";
            return null;
        }

        private static List<string> ConfigurationNames(IModelDoc2 model)
        {
            try
            {
                var names = model.GetConfigurationNames() as string[];
                return names == null ? new List<string>() : names.ToList();
            }
            catch { return new List<string>(); }
        }

        private static string ReadProperty(ICustomPropertyManager propertyManager, string itemName)
        {
            try
            {
                string val, resolved;
                bool wasResolved, connected;
                int outcome = propertyManager.Get6(itemName, false, out val, out resolved,
                                          out wasResolved, out connected);
                if (outcome == (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent)
                    return null;
                return resolved ?? val;
            }
            catch { return null; }
        }

        /// <summary>
        /// The property read back both ways: <paramref name="written"/> is
        /// ValOut, the text of the property, and <paramref name="resolved"/> is
        /// ResolvedValOut, the evaluated value (ICustomPropertyManager~Get6.html).
        /// False when Get6 answers NotPresent or cannot be asked.
        /// </summary>
        private static bool ReadPropertyBack(
            ICustomPropertyManager propertyManager, string itemName, out string written, out string resolved)
        {
            written = null;
            resolved = null;
            try
            {
                string valOut, resolvedValOut;
                bool wasResolved, linkToProperty;
                int result = propertyManager.Get6(itemName, false, out valOut, out resolvedValOut,
                                           out wasResolved, out linkToProperty);
                if (result == (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent) return false;
                written = valOut ?? "";
                resolved = resolvedValOut;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// True when the text read back (ValOut) is the text written (Z-C21.8,
        /// finding M35). The evaluated value takes no part: for a formula it is
        /// the result, not the text. Kept apart from the document so that
        /// Oce.Solid.Agent.ContractTests can call it without SOLIDWORKS.
        /// </summary>
        internal static bool PropertyWrittenAsAsked(string written, bool present, string readBack)
        {
            return present && string.Equals(written ?? "", readBack ?? "", StringComparison.Ordinal);
        }

        private static string PropertyOutcomeName(int code)
        {
            try
            {
                if (Enum.IsDefined(typeof(swCustomInfoAddResult_e), code))
                    return ((swCustomInfoAddResult_e)code).ToString();
            }
            catch { }
            return "codice " + code;
        }
    }
}
