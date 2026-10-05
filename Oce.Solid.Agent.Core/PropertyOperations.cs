using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// QUELLO CHE IL PEZZO DICE DI SE', E LE SUE VARIANTI. (voce GP12)
    ///
    /// Numero disegno, revisione, cliente: le proprieta' personalizzate sono
    /// cio' che finisce nel cartiglio e nella scheda del PDM. Fino a qui
    /// `apply_material` era l'unico aggancio fra il catalogo e quel mondo.
    ///
    /// E le configurazioni sono la stessa famiglia di staffe sinistra/destra,
    /// con e senza foro: una richiesta standard di un'officina a disegno.
    ///
    /// ── LE DUE STRADE SONO LA STESSA STRADA ─────────────────────────────
    ///
    /// La voce chiedeva di accertare «se scrivere su
    /// `IConfiguration.get_CustomPropertyManager()` o passare il nome a
    /// `IModelDocExtension.get_CustomPropertyManager(nome)` dia risultati
    /// equivalenti: due strade mai confrontate». Misurato il 16/9/2026:
    ///
    ///   scritta «Hand» = SX via Extension(nome)
    ///       riletta via Extension(nome)  ->  «SX»
    ///       riletta via IConfiguration   ->  «SX»
    ///   scritta «Revision» = B via IConfiguration
    ///       riletta via IConfiguration   ->  «B»
    ///       riletta via Extension(nome)  ->  «B»
    ///
    /// **Sono equivalenti.** Qui si usa `Extension(nome)`, che non ha bisogno
    /// dell'oggetto configurazione in mano.
    ///
    /// ── MA IL DOCUMENTO E LA CONFIGURAZIONE SONO DUE CASSETTI ──────────
    ///
    /// E questa NON e' una cosa che si sapeva:
    ///
    ///   «Numero disegno», scritta sul DOCUMENTO, letta dalla config  -> non c'e'
    ///   «Hand», scritta sulla CONFIG, letta dal documento            -> non c'e'
    ///
    /// Non si ereditano e non si vedono. Chi scrive il numero di disegno sul
    /// documento e poi legge le proprieta' di una configurazione **non lo
    /// trova**, e nessuno gli dice perche'. Percio' qui il posto in cui si
    /// scrive e' un argomento esplicito, e torna nella risposta.
    ///
    /// ── IL NOME DELLA CONFIGURAZIONE NON E' QUELLO CHE SEMBRA ──────────
    ///
    ///   ActiveConfiguration.Name       ->  «Default&lt;A macchina&gt;»
    ///   ShowConfiguration2("Default")  ->  FALSE
    ///   ShowConfiguration2("Default&lt;A macchina&gt;")  ->  TRUE
    ///
    /// Le parentesi angolari sono **dentro il nome**, e non sono un'altra cosa
    /// che il nome si porta appresso: un pezzo appena nato dal modello di
    /// questa azienda ha DUE configurazioni vere — «Default&lt;A macchina&gt;» e
    /// «Default&lt;Saldato&gt;», la coppia dei saldati — e `GetConfigurationCount()`
    /// risponde **2**. (Gli stati di visualizzazione sono un'altra cosa
    /// ancora: stanno su `IConfiguration.GetDisplayStates()`, qui ce n'e' uno
    /// per configurazione e si chiama «&lt;Default&gt;_Stato di visualizzazione 1».)
    ///
    /// L'unica fonte buona e' `GetConfigurationNames()`: i nomi non si
    /// costruiscono, si chiedono.
    ///
    /// ── LO STESSO NOME SU DUE INTERFACCE, CON FIRME DIVERSE ────────────
    ///
    /// La voce avvisava di una «firma gemella-ma-diversa». E' peggio: ci sono
    /// DUE FAMIGLIE di questi metodi, su due interfacce, e i nomi si ripetono.
    ///
    ///   IModelDoc2.AddConfiguration (8 par.)  -> void
    ///   IModelDoc2.AddConfiguration2(8 par.)  -> bool
    ///   IModelDoc2.AddConfiguration3(Name, Comment, AlternateName, Options)
    ///                                         -> object
    ///   IConfigurationManager.AddConfiguration (6 par.)  -> IConfiguration
    ///   IConfigurationManager.AddConfiguration2(7 par., con Rebuild)
    ///                                         -> IConfiguration
    ///
    /// Un metodo cercato sull'interfaccia sbagliata «non esiste» — ed e'
    /// esattamente l'errore che abbiamo fatto prima di guardare: `AddConfiguration3`
    /// **c'e'**, ma su `IModelDoc2`. *Un nome dice come si chiama un metodo, non
    /// su quale interfaccia sta.*
    ///
    /// Qui si usa **`IConfigurationManager.AddConfiguration2`**, e non per
    /// anzianita': e' l'unica che torna l'**oggetto configurazione** (da cui si
    /// rilegge il nome vero) e che prende `Rebuild`. Le tre su `IModelDoc2`
    /// tornano `void`, `bool` e `object`: la prima non si puo' nemmeno
    /// controllare.
    ///
    /// **Attiva subito la configurazione nuova**, senza che glielo si chieda:
    /// si dice nella risposta, perche' i passi dopo lavorano su quella.
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
                    // SI SOSTITUISCE, e si dice: «solo se nuova» lascerebbe il
                    // valore vecchio e risponderebbe lo stesso, che e' il modo
                    // piu' comodo di credere di aver scritto una revisione.
                    (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            }
            catch (Exception ex) { return Failed("La proprieta' non e' stata scritta: " + ex.Message); }

            if (outcome != (int)swCustomInfoAddResult_e.swCustomInfoAddResult_AddedOrChanged)
                return Failed(
                    "SOLIDWORKS ha rifiutato la proprieta' «" + itemName + "»: " +
                    PropertyOutcomeName(outcome) + ".");

            // SI RILEGGE SEMPRE. `Add3` risponde un codice, non il valore: il
            // valore lo dice solo chi lo rilegge.
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

            // E SI GUARDA ANCHE NELL'ALTRO CASSETTO.
            //
            // Non per scrivere: per DIRE che li' non c'e'. E' la differenza
            // fra una nota nel catalogo, che chi legge la risposta puo' non
            // aver mai letto, e un numero misurato su questo pezzo adesso.
            // Ed e' l'unico modo perche' il banco possa vedere il giorno in
            // cui SOLIDWORKS cambiasse idea e cominciasse a ereditarle.
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
                // I DUE CASSETTI SONO SEPARATI, e va detto ogni volta: e' la
                // cosa che nessuno si aspetta.
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
                // `IConfigurationManager.AddConfiguration2`, non una delle tre
                // che si chiamano quasi uguale su `IModelDoc2`: solo questa
                // torna l'oggetto configurazione. Vedi il commento in testa.
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
                // ATTIVA DA SOLA, e i passi dopo lavorano su quella.
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

            // ── IL BOOLEANO NON E' IL CRITERIO, E NON PERCHE' SIA PIGRO ──
            //
            // `ShowConfiguration2` risponde **false anche quando la
            // configurazione chiesta e' gia' quella attiva**. Misurato dal
            // banco il 16/9/2026: due casi rossi con il messaggio «ho chiesto
            // di attivare "Left" e adesso e' attiva "Left"» — cioe' il
            // rifiuto diceva da solo che il rifiuto era sbagliato.
            //
            // Quel `false` non distingue «non l'ho fatto» da «non c'era niente
            // da fare». L'unica cosa che distingue le due e' **quale
            // configurazione e' attiva adesso**, che e' anche l'unica che a chi
            // legge interessi davvero.
            //
            // Il booleano si tiene, ma come dettaglio nel messaggio: serve a
            // chi dovra' capire perche' il giorno in cui questo scattasse.
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
                // «era gia' questa»: chi legge deve poter distinguere uno
                // spostamento da un non-spostamento, perche' SOLIDWORKS non lo
                // distingue (vedi sopra) e la differenza conta quando il passo
                // seguente da' per scontato di essere appena arrivato.
                ["was_already_active"] = !done,
            });
        }

        // =================================================================

        /// <summary>
        /// Il nome di una configurazione, verificato contro quelli che il
        /// documento dichiara. Null — con il motivo — quando non c'e'.
        ///
        /// ── PERCHE' NON SI INDOVINA IL NOME CORTO ─────────────────────────
        ///
        /// `ShowConfiguration2("Default")` risponde **false** se le
        /// configurazioni si chiamano «Default&lt;A macchina&gt;» e
        /// «Default&lt;Saldato&gt;»: il pezzo fra parentesi angolari fa parte
        /// del nome.
        ///
        /// La tentazione e' tradurre il nome corto in quello lungo. Non si fa,
        /// per due motivi che si sono visti tutti e due:
        ///
        ///   * **quel nome corto e' AMBIGUO.** Un pezzo appena nato dal modello
        ///     di questa azienda ha DUE configurazioni vere che cominciano per
        ///     «Default» — `GetConfigurationCount()` risponde 2. Chi traducesse
        ///     sceglierebbe, e scegliere qui vuol dire attivare la variante
        ///     sbagliata senza che niente dia errore;
        ///   * **una strada che non si prende mai non si sa se funziona.** Se
        ///     il caso unico fosse davvero unico, il ramo «un solo candidato»
        ///     resterebbe non provato — e un ramo non provato in mezzo a una
        ///     scelta di configurazione e' peggio di un rifiuto.
        ///
        /// Quindi: **il nome si confronta e basta**, e il messaggio insegna il
        /// perche' — che e' l'unica cosa che qui serve davvero, perche' chi
        /// chiama i nomi buoni ce li ha, nello stato del documento.
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

            // Se il nome chiesto e' il prefisso di uno o piu' nomi veri, quasi
            // sempre chi chiama non sapeva del suffisso: glielo si dice, invece
            // di elencare e basta.
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
