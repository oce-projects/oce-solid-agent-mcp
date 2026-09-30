using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// QUELLO CHE IL PEZZO DICE DI SE', E LE SUE VARIANTI. (voce GP12)
    ///
    /// Numero disegno, revisione, cliente: le proprieta' personalizzate sono
    /// cio' che finisce nel cartiglio e nella scheda del PDM. Fino a qui
    /// `applica_materiale` era l'unico aggancio fra il catalogo e quel mondo.
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
    ///   scritta «Mano» = SX via Extension(nome)
    ///       riletta via Extension(nome)  ->  «SX»
    ///       riletta via IConfiguration   ->  «SX»
    ///   scritta «Revisione» = B via IConfiguration
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
    ///   «Mano», scritta sulla CONFIG, letta dal documento            -> non c'e'
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
    public partial class OperazioniSw
    {
        private Esito ScriviProprieta(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string nome = Str(a, "nome", null);
            if (string.IsNullOrWhiteSpace(nome))
                return Fallito(
                    "Manca il «nome» della proprieta', per esempio «Numero disegno» o " +
                    "«Revisione».");
            string valore = Str(a, "valore", null);
            if (valore == null)
                return Fallito(
                    "Manca il «valore». Se vuoi svuotare la proprieta', passa una stringa " +
                    "vuota: cancellarla e' un'altra cosa, e non la faccio di nascosto.");

            string configurazione = Str(a, "configurazione", null);
            bool sullaConfigurazione = !string.IsNullOrWhiteSpace(configurazione);
            if (sullaConfigurazione)
            {
                string perche;
                configurazione = NomeVeroDellaConfigurazione(modello, configurazione, out perche);
                if (configurazione == null) return Fallito(perche);
            }

            ICustomPropertyManager cassetto;
            try
            {
                cassetto = modello.Extension.get_CustomPropertyManager(
                    sullaConfigurazione ? configurazione : "") as ICustomPropertyManager;
            }
            catch (Exception ex) { return Fallito("Non riesco ad arrivare alle proprieta': " + ex.Message); }
            if (cassetto == null)
                return Fallito("Questo documento non espone le proprieta' personalizzate.");

            int esito;
            try
            {
                esito = cassetto.Add3(
                    nome,
                    (int)swCustomInfoType_e.swCustomInfoText,
                    valore,
                    // SI SOSTITUISCE, e si dice: «solo se nuova» lascerebbe il
                    // valore vecchio e risponderebbe lo stesso, che e' il modo
                    // piu' comodo di credere di aver scritto una revisione.
                    (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
            }
            catch (Exception ex) { return Fallito("La proprieta' non e' stata scritta: " + ex.Message); }

            if (esito != (int)swCustomInfoAddResult_e.swCustomInfoAddResult_AddedOrChanged)
                return Fallito(
                    "SOLIDWORKS ha rifiutato la proprieta' «" + nome + "»: " +
                    NomeEsitoProprieta(esito) + ".");

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
            string letto, risolto;
            bool presente = ReadPropertyBack(cassetto, nome, out letto, out risolto);
            if (!PropertyWrittenAsAsked(valore, presente, letto))
                return Fallito(
                    "Ho scritto «" + valore + "» in «" + nome + "» e rileggendo trovo " +
                    (letto == null ? "il vuoto" : "«" + letto + "»") + ". Non ti confermo " +
                    "una scheda dati che non e' quella che credi.");

            // E SI GUARDA ANCHE NELL'ALTRO CASSETTO.
            //
            // Non per scrivere: per DIRE che li' non c'e'. E' la differenza
            // fra una nota nel catalogo, che chi legge la risposta puo' non
            // aver mai letto, e un numero misurato su questo pezzo adesso.
            // Ed e' l'unico modo perche' il banco possa vedere il giorno in
            // cui SOLIDWORKS cambiasse idea e cominciasse a ereditarle.
            string altroCassetto = sullaConfigurazione
                ? "il documento"
                : "la configurazione attiva";
            bool siVedeDiLa = false;
            try
            {
                string doveGuardare = "";
                if (!sullaConfigurazione)
                {
                    var gestore = modello.ConfigurationManager as IConfigurationManager;
                    doveGuardare = gestore == null ? null : gestore.ActiveConfiguration.Name;
                    altroCassetto = "la configurazione «" + doveGuardare + "»";
                }
                if (doveGuardare != null)
                {
                    var diLa = modello.Extension.get_CustomPropertyManager(doveGuardare)
                               as ICustomPropertyManager;
                    siVedeDiLa = diLa != null && LeggiProprieta(diLa, nome) != null;
                }
            }
            catch { }

            var misure = new Dictionary<string, object>
            {
                ["proprieta"] = nome,
                ["valore"] = letto,
                ["dove"] = sullaConfigurazione
                    ? "la configurazione «" + configurazione + "»"
                    : "il documento",
                ["si_vede_dall_altro_cassetto"] = siVedeDiLa,
                // I DUE CASSETTI SONO SEPARATI, e va detto ogni volta: e' la
                // cosa che nessuno si aspetta.
                ["i_due_cassetti"] =
                    "Le proprieta' del documento e quelle di una configurazione sono due " +
                    "elenchi separati: non si ereditano e non si vedono a vicenda. Ho " +
                    "guardato: da " + altroCassetto + " questa proprieta' " +
                    (siVedeDiLa ? "SI VEDE." : "NON si vede."),
            };
            if (sullaConfigurazione) misure["configurazione"] = configurazione;
            if (risolto != null && !string.Equals(risolto, letto, StringComparison.Ordinal))
                misure["resolved_value"] = risolto;

            _log("[agente] proprieta' «" + nome + "» = «" + letto + "» su " +
                 (sullaConfigurazione ? configurazione : "documento"));
            return RiuscitoSenzaCreare(misure);
        }

        private Esito CreaConfigurazione(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string nome = Str(a, "nome", null);
            if (string.IsNullOrWhiteSpace(nome))
                return Fallito("Manca il «nome» della configurazione, per esempio «Sinistra».");

            var gestore = modello.ConfigurationManager as IConfigurationManager;
            if (gestore == null)
                return Fallito("Questo documento non espone le configurazioni.");

            var giaCi = NomiDelleConfigurazioni(modello);
            if (giaCi.Any(n => string.Equals(n, nome, StringComparison.OrdinalIgnoreCase) ||
                               n.StartsWith(nome + "<", StringComparison.OrdinalIgnoreCase)))
                return Fallito(
                    "Una configurazione «" + nome + "» c'e' gia'. Ci sono: " +
                    string.Join(", ", giaCi.Select(n => "«" + n + "»")) + ".");

            IConfiguration nuova;
            try
            {
                // `IConfigurationManager.AddConfiguration2`, non una delle tre
                // che si chiamano quasi uguale su `IModelDoc2`: solo questa
                // torna l'oggetto configurazione. Vedi il commento in testa.
                nuova = gestore.AddConfiguration2(
                    nome,                              // Name
                    Str(a, "commento", "") ?? "",      // Comment
                    "",                                // AlternateName
                    0,                                 // Options
                    "",                                // ParentConfigName
                    Str(a, "descrizione", "") ?? "",   // Description
                    true) as IConfiguration;           // Rebuild
            }
            catch (Exception ex) { return Fallito("La configurazione non e' stata creata: " + ex.Message); }

            if (nuova == null)
                return Fallito(
                    "La configurazione «" + nome + "» non e' stata creata, e SOLIDWORKS non " +
                    "dice perche'. Di solito e' il nome: certi caratteri non si possono usare.");

            string attiva = "";
            try { attiva = gestore.ActiveConfiguration.Name; } catch { }

            return RiuscitoSenzaCreare(new Dictionary<string, object>
            {
                ["configurazione"] = nuova.Name,
                ["attiva_adesso"] = attiva,
                ["configurazioni"] = NomiDelleConfigurazioni(modello).ToArray(),
                // ATTIVA DA SOLA, e i passi dopo lavorano su quella.
                ["nota_attivazione"] =
                    "Creando una configurazione SOLIDWORKS la ATTIVA: adesso il documento e' " +
                    "su «" + attiva + "», e i passi che seguono lavorano li'. Con " +
                    "«attiva_configurazione» si torna a un'altra.",
                ["nota_nome"] =
                    "Il nome buono per le altre operazioni e' quello scritto qui: puo' avere " +
                    "un suffisso fra parentesi angolari (lo stato di visualizzazione) che fa " +
                    "parte del nome.",
            });
        }

        private Esito AttivaConfigurazione(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string chiesta = Str(a, "nome", null);
            if (string.IsNullOrWhiteSpace(chiesta))
                return Fallito("Manca il «nome» della configurazione da attivare.");

            string perche;
            string nome = NomeVeroDellaConfigurazione(modello, chiesta, out perche);
            if (nome == null) return Fallito(perche);

            bool fatto;
            try { fatto = modello.ShowConfiguration2(nome); }
            catch (Exception ex) { return Fallito("Non sono riuscito ad attivarla: " + ex.Message); }

            string attiva = "";
            try
            {
                var gestore = modello.ConfigurationManager as IConfigurationManager;
                if (gestore != null) attiva = gestore.ActiveConfiguration.Name;
            }
            catch { }

            // ── IL BOOLEANO NON E' IL CRITERIO, E NON PERCHE' SIA PIGRO ──
            //
            // `ShowConfiguration2` risponde **false anche quando la
            // configurazione chiesta e' gia' quella attiva**. Misurato dal
            // banco il 16/9/2026: due casi rossi con il messaggio «ho chiesto
            // di attivare "Sinistra" e adesso e' attiva "Sinistra"» — cioe' il
            // rifiuto diceva da solo che il rifiuto era sbagliato.
            //
            // Quel `false` non distingue «non l'ho fatto» da «non c'era niente
            // da fare». L'unica cosa che distingue le due e' **quale
            // configurazione e' attiva adesso**, che e' anche l'unica che a chi
            // legge interessi davvero.
            //
            // Il booleano si tiene, ma come dettaglio nel messaggio: serve a
            // chi dovra' capire perche' il giorno in cui questo scattasse.
            if (!string.Equals(attiva, nome, StringComparison.Ordinal))
                return Fallito(
                    "Ho chiesto di attivare «" + nome + "» e adesso e' attiva " +
                    (string.IsNullOrEmpty(attiva) ? "non so quale" : "«" + attiva + "»") +
                    " (SOLIDWORKS ha risposto " + (fatto ? "di si'" : "di no") + "). Non ti " +
                    "confermo di lavorare su una configurazione diversa da quella che credi.");

            return RiuscitoSenzaCreare(new Dictionary<string, object>
            {
                ["configurazione"] = attiva,
                ["configurazioni"] = NomiDelleConfigurazioni(modello).ToArray(),
                // «era gia' questa»: chi legge deve poter distinguere uno
                // spostamento da un non-spostamento, perche' SOLIDWORKS non lo
                // distingue (vedi sopra) e la differenza conta quando il passo
                // seguente da' per scontato di essere appena arrivato.
                ["era_gia_attiva"] = !fatto,
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
        private static string NomeVeroDellaConfigurazione(
            IModelDoc2 modello, string chiesta, out string perche)
        {
            perche = null;
            var nomi = NomiDelleConfigurazioni(modello);
            if (nomi.Count == 0)
            {
                perche = "Questo documento non dichiara nessuna configurazione.";
                return null;
            }
            foreach (var n in nomi)
                if (string.Equals(n, chiesta, StringComparison.Ordinal)) return n;

            // Se il nome chiesto e' il prefisso di uno o piu' nomi veri, quasi
            // sempre chi chiama non sapeva del suffisso: glielo si dice, invece
            // di elencare e basta.
            var somiglianti = nomi
                .Where(n => n.StartsWith(chiesta + "<", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (somiglianti.Count > 0)
            {
                perche =
                    "«" + chiesta + "» non e' un nome intero. Il documento dichiara " +
                    string.Join(" e ", somiglianti.Select(n => "«" + n + "»")) +
                    ": il pezzo fra parentesi angolari fa parte del nome, e va copiato. " +
                    "Chiesto cosi' com'e', SOLIDWORKS risponde di no in silenzio.";
                return null;
            }
            perche =
                "Non trovo la configurazione «" + chiesta + "». Ci sono: " +
                string.Join(", ", nomi.Select(n => "«" + n + "»")) + ".";
            return null;
        }

        private static List<string> NomiDelleConfigurazioni(IModelDoc2 modello)
        {
            try
            {
                var nomi = modello.GetConfigurationNames() as string[];
                return nomi == null ? new List<string>() : nomi.ToList();
            }
            catch { return new List<string>(); }
        }

        private static string LeggiProprieta(ICustomPropertyManager cassetto, string nome)
        {
            try
            {
                string valore, risolto;
                bool eraRisolto, collegata;
                int esito = cassetto.Get6(nome, false, out valore, out risolto,
                                          out eraRisolto, out collegata);
                if (esito == (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent)
                    return null;
                return risolto ?? valore;
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
            ICustomPropertyManager cassetto, string nome, out string written, out string resolved)
        {
            written = null;
            resolved = null;
            try
            {
                string valOut, resolvedValOut;
                bool wasResolved, linkToProperty;
                int result = cassetto.Get6(nome, false, out valOut, out resolvedValOut,
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
        /// Prove.Contratti can call it without SOLIDWORKS.
        /// </summary>
        internal static bool PropertyWrittenAsAsked(string written, bool present, string readBack)
        {
            return present && string.Equals(written ?? "", readBack ?? "", StringComparison.Ordinal);
        }

        private static string NomeEsitoProprieta(int codice)
        {
            try
            {
                if (Enum.IsDefined(typeof(swCustomInfoAddResult_e), codice))
                    return ((swCustomInfoAddResult_e)codice).ToString();
            }
            catch { }
            return "codice " + codice;
        }
    }
}
