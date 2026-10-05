using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// IL CARTIGLIO: COSA CHIEDE, E COSA MOSTRA (voce GT3).
    ///
    /// ── IL CARTIGLIO NON SI SCRIVE: SI ALIMENTA ─────────────────────────
    ///
    /// Un cartiglio da modello non contiene testo: contiene **collegamenti** a
    /// proprieta' personalizzate, scritti `$PRP:"Descrizione"`. Riempirlo vuol
    /// dire scrivere quelle proprieta' — e `set_property` lo sa gia' fare.
    ///
    /// Quello che mancava e' la meta' difficile, e la voce GT3 la nominava:
    /// **«leggere quali variabili il suo cartiglio referenzia davvero (non si
    /// indovinano)»**, due ore di lavoro a mano per ogni modello di tavola.
    ///
    /// ── IL DIFETTO CHE QUESTA OPERAZIONE ESISTE PER PRENDERE ────────────
    ///
    /// Il nome della proprieta' scritto con una grafia diversa da quella che il
    /// cartiglio referenzia — una maiuscola, uno spazio, la lingua — **non da'
    /// nessun errore**: SOLIDWORKS scrive la proprieta', il cartiglio non la
    /// mostra, e il PDF esce con un campo vuoto che nessuno guarda finche' non
    /// e' in officina.
    ///
    /// E' lo stesso difetto gia' pagato due volte oggi: `40x40x3` contro
    /// `40X40X3` sui profili, e `.SLDPRT` contro `.sldprt` sui percorsi.
    /// **Lo stesso nome, due grafie, nessun errore.**
    ///
    /// ── LA COPPIA CHE RISOLVE, E SONO DUE CHIAMATE DIVERSE ──────────────
    ///
    ///     INote.get_PropertyLinkedText()  ->  «$PRP:"Descrizione"»   COSA CHIEDE
    ///     INote.GetText()                 ->  «Coperchio»            COSA MOSTRA
    ///
    /// Chi legge solo `GetText()` vede una casella vuota e non sa perche'. Chi
    /// legge tutt'e due sa **il nome esatto da scrivere**, con la sua grafia,
    /// copiabile alla lettera. *Non si indovina un nome che si puo' chiedere.*
    ///
    /// ── PERCHE' NON SI VERIFICA NEL PDF ─────────────────────────────────
    ///
    /// La voce GT3 proponeva di cercare la stringa dentro il PDF esportato. E'
    /// una prova vera e resta buona, ma e' **a valle di tutto**: dice che
    /// qualcosa non ha funzionato, non quale campo ne' perche'. Questa lettura
    /// e' a monte, per campo, e costa una chiamata.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// I collegamenti che un cartiglio puo' portare. `$PRPSHEET` prende la
        /// proprieta' dal MODELLO mostrato nella vista, non dal disegno: e' la
        /// distinzione che fa scrivere la proprieta' sul documento sbagliato.
        /// </summary>
        private static readonly Regex Connection =
            new Regex("\\$PRP(SHEET)?:\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);

        private Outcome TitleBlock(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "Il documento attivo non e' una tavola: il cartiglio sta su un " +
                    "foglio di disegno.");

            ISheet sheet;
            try { sheet = drawing.GetCurrentSheet() as ISheet; }
            catch (Exception ex) { return Failed("Non riesco a leggere il foglio: " + ex.Message); }
            if (sheet == null) return Failed("La tavola non ha un foglio attivo.");

            var note = TitleBlockNotes(drawing, sheet, out string fromWhere);
            if (note.Count == 0)
                return Failed(
                    "Non trovo nessuna nota nel cartiglio di questo foglio" +
                    (fromWhere == null ? "" : " (cercate " + fromWhere + ")") +
                    ". O il modello di tavola non ha un cartiglio, o e' fatto di " +
                    "disegno invece che di note: nel secondo caso non c'e' niente da " +
                    "alimentare, il testo e' dipinto dentro il formato.");

            // ── COSA CHIEDE E COSA MOSTRA, campo per campo ──────────────
            var fields = new List<object>();
            var requestedItems = new List<string>();
            var empty = new List<string>();
            var shown = new List<string>();
            int connected = 0;

            foreach (var n in note)
            {
                string asks = null, show = null;
                try { asks = n.PropertyLinkedText; } catch { }
                try { show = n.GetText(); } catch { }

                var names = ConnectedNames(asks);
                if (names.Count > 0) connected++;

                foreach (var x in names)
                    if (!requestedItems.Contains(x, StringComparer.Ordinal)) requestedItems.Add(x);

                // UN CAMPO COLLEGATO CHE NON MOSTRA NIENTE E' IL DIFETTO.
                // Non e' «vuoto per scelta»: e' un collegamento che non ha
                // trovato la sua proprieta'.
                bool showNothing = string.IsNullOrWhiteSpace(show);
                if (names.Count > 0 && showNothing)
                    foreach (var x in names)
                        if (!empty.Contains(x, StringComparer.Ordinal)) empty.Add(x);

                // E QUELLE CHE ARRIVANO DAVVERO IN FONDO.
                //
                // Non e' il complemento di «vuote»: e' l'affermazione positiva,
                // e serve perche' **si possa provare che qualcosa funziona**.
                // Un campo che elenca solo cio' che manca non si puo' usare per
                // dimostrare che una scrittura e' arrivata: si potrebbe solo
                // dire che non e' nell'elenco dei guasti, e «non e' fra i
                // rotti» non e' «funziona».
                if (names.Count > 0 && !showNothing)
                    foreach (var x in names)
                        if (!shown.Contains(x, StringComparer.Ordinal)) shown.Add(x);

                var entry = new Dictionary<string, object>
                {
                    ["asks"] = string.IsNullOrWhiteSpace(asks) ? null : asks,
                    ["shows"] = show ?? "",
                };
                if (names.Count > 0) entry["property"] = names.ToArray();
                fields.Add(entry);
            }

            var measurements = new Dictionary<string, object>
            {
                ["sheet"] = SheetName(sheet) ?? "",
                ["sheet_format"] = TestText(() => sheet.GetSheetFormatName()),
                ["notes_in_title_block"] = note.Count,
                ["notes_linked"] = connected,
                ["searched_in"] = fromWhere,
                ["fields"] = fields.ToArray(),
            };

            // ── L'ELENCO CHE TOGLIE L'INVENZIONE ────────────────────────
            //
            // E' il pezzo che vale: i nomi ESATTI che questo cartiglio vuole,
            // copiabili alla lettera dentro `set_property`.
            if (requestedItems.Count > 0)
            {
                measurements["properties_wanted_by_title_block"] = requestedItems.ToArray();
                measurements["how_to_use_it"] =
                    "Copia questi nomi ALLA LETTERA dentro «set_property». Una " +
                    "maiuscola o uno spazio diversi non danno nessun errore: la " +
                    "proprieta' viene scritta, il cartiglio non la mostra, e il PDF " +
                    "esce con la casella vuota.";
            }
            // Anche questo sempre, per la stessa ragione: e' l'affermazione
            // positiva, e un'affermazione che sparisce quando e' falsa non e'
            // un'affermazione.
            measurements["requested_and_shown"] = shown.ToArray();

            // ── VUOTE PERCHE' NON SCRITTE, O VUOTE PERCHE' NON RISOLTE? ──
            //
            // ── PERCHE' QUESTA DIVISIONE ESISTE ──────────────────────────
            //
            // Il 18/9/2026 il caso `title_block_says_what_it_wants` e' stato
            // **verde due volte e rosso la terza**, sulla stessa libreria:
            // `Descrizione`, scritta sul pezzo e riletta, non compariva piu'
            // fra le mostrate. Riprovato dal ponte, **non si e' riprodotto**.
            //
            // Con i campi di prima quel rosso diceva soltanto «non dice
            // Descrizione», e non distingueva due cose opposte:
            //
            //   * la proprieta' NON C'E' sul modello   -> chi disegna deve
            //     scriverla, ed e' il difetto che l'operazione esiste per
            //     prendere;
            //   * la proprieta' C'E' sul modello e il cartiglio la mostra
            //     vuota -> il collegamento non ha risolto: o non ancora, o
            //     e' rotto. Non e' colpa di chi disegna, e scrivere di nuovo
            //     la proprieta' non cambierebbe niente.
            //
            // Sono due diagnosi e due rimedi. **Un elenco che le mette
            // insieme manda a riparare la cosa sbagliata**, ed e' la stessa
            // forma del difetto che l'operazione denuncia: una risposta che
            // sembra completa e non lo e'.
            //
            // Si guarda nel modello referenziato dalla prima vista vera —
            // quello da cui `$PRPSHEET` prende — in tutti e due i cassetti,
            // perche' documento e configurazione sono elenchi separati.
            // E PRIMA DI TUTTO: HO POTUTO GUARDARE?
            //
            // `ViewModelProperty` torna `null` quando non ci arriva.
            // Senza dirlo, l'elenco qui sotto resterebbe vuoto **per ignoranza**
            // e non per misura — e un elenco vuoto per ignoranza passa
            // qualunque controllo che chieda «non c'e' dentro X».
            //
            // Il 18/9/2026 e' successo esattamente questo, ed e' costato un
            // giro di banco: la distinzione era scritta nel commento di quella
            // funzione e **non era visibile a chi legge la risposta**.
            // *Una distinzione che vive solo in un commento non e' una
            // distinzione: e' un'intenzione.*
            string whoWasChecked;
            var onDisk = ViewModelProperty(drawing, out whoWasChecked);
            measurements["model_checked_for_properties"] = whoWasChecked ??
                "NON SONO ARRIVATO AL MODELLO: i due elenchi qui sotto sono vuoti " +
                "perche' non ho potuto guardare, non perche' non ci fosse niente.";

            var presentButMute = new List<string>();
            if (onDisk != null)
                foreach (var x in empty)
                    if (onDisk.Contains(BareName(x), StringComparer.OrdinalIgnoreCase))
                        presentButMute.Add(x);

            measurements["requested_but_empty"] = empty.ToArray();

            // SEMPRE, ANCHE VUOTO, e non e' pignoleria: il banco tratta un
            // campo ASSENTE come prova rossa (voce F4), quindi un campo che
            // compare solo quando ha qualcosa dentro **non si puo' nemmeno
            // pretendere vuoto**. E chi legge la risposta, non trovandolo,
            // conclude che l'operazione non sappia rispondere invece che
            // l'elenco sia vuoto.
            measurements["requested_present_on_model_but_shown_empty"] = presentButMute.ToArray();
            if (presentButMute.Count > 0)
                measurements["not_the_drafters_fault"] =
                    "Queste proprieta' CI SONO sul modello mostrato nella vista, e il " +
                    "cartiglio le mostra vuote lo stesso: il collegamento non ha " +
                    "risolto. Riscriverle non cambia niente. Prova a ricostruire la " +
                    "tavola e a rileggere; se restano vuote, il collegamento e' rotto.";

            string viewByProperty = TestText(() => sheet.CustomPropertyView);
            if (!string.IsNullOrWhiteSpace(viewByProperty))
                measurements["sheet_properties_source_view"] = viewByProperty;

            if (requestedItems.Count == 0)
                measurements["verdict"] =
                    "Il cartiglio ha " + note.Count + (note.Count == 1 ? " nota" : " note") +
                    " ma **nessuna collegata a una proprieta'**: il testo e' scritto " +
                    "fisso dentro il formato. Riempirlo con «set_property» non " +
                    "cambierebbe niente, e non te lo lascio credere.";
            else if (empty.Count == 0)
                measurements["verdict"] =
                    "Il cartiglio vuole " + requestedItems.Count +
                    (requestedItems.Count == 1 ? " proprieta'" : " proprieta'") + " (" +
                    string.Join(", ", requestedItems) + ") e le mostra tutte: nessuna casella " +
                    "vuota.";
            else
                measurements["verdict"] =
                    "ATTENZIONE: il cartiglio chiede " + requestedItems.Count + " proprieta' e " +
                    empty.Count + " NON mostrano niente (" + string.Join(", ", empty) +
                    "). O non sono ancora state scritte, o sono scritte con una grafia " +
                    "diversa da quella che il cartiglio referenzia — e quel caso non da' " +
                    "nessun errore: la tavola esce con le caselle vuote. I nomi qui " +
                    "sopra sono quelli giusti, copiali alla lettera.";

            _log("[agente] " + measurements["verdict"]);
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// Le note del cartiglio. Due strade, e si dice quale ha risposto:
        /// il `TitleBlock` vero (che non tutti i modelli di tavola hanno) e, in
        /// mancanza, le annotazioni del FOGLIO — che nell'API e' la prima
        /// «view», quella che porta il formato.
        /// </summary>
        private static List<INote> TitleBlockNotes(
            IDrawingDoc drawing, ISheet sheet, out string fromWhere)
        {
            fromWhere = null;
            var outside = new List<INote>();

            try
            {
                var block = sheet.TitleBlock as ITitleBlock;
                if (block != null)
                {
                    var n = block.GetNotes() as object[];
                    if (n != null)
                        foreach (var o in n) { var note = o as INote; if (note != null) outside.Add(note); }
                    if (outside.Count > 0) { fromWhere = "nel blocco cartiglio del foglio"; return outside; }
                }
            }
            catch { }

            // LA PRIMA «VISTA» DI UNA TAVOLA E' IL FOGLIO, non una vista del
            // pezzo: e' la stessa cosa che `CountViews` salta per contare le
            // viste vere, e qui invece e' proprio quella che serve.
            try
            {
                var sheetView = drawing.GetFirstView() as IView;
                if (sheetView != null)
                {
                    var ann = sheetView.GetFirstAnnotation3() as IAnnotation;
                    while (ann != null)
                    {
                        // NON SI CHIEDE IL TIPO, SI PROVA IL CAST.
                        //
                        // `IAnnotation` ha un `GetType()` che torna `Int32`, e in
                        // C# quel nome collide con `object.GetType()`: il
                        // compilatore non sa quale intendi. `GetSpecificAnnotation`
                        // torna gia' l'oggetto specifico, e un cast a `INote` che
                        // fallisce da' `null` — che e' la stessa informazione
                        // senza la collisione.
                        try
                        {
                            var note = ann.GetSpecificAnnotation() as INote;
                            if (note != null) outside.Add(note);
                        }
                        catch { }
                        ann = ann.GetNext3() as IAnnotation;
                    }
                    if (outside.Count > 0) fromWhere = "fra le note del formato del foglio";
                }
            }
            catch { }

            if (fromWhere == null) fromWhere = "nel blocco cartiglio e fra le note del formato";
            return outside;
        }

        /// <summary>
        /// I nomi di proprieta' dentro un testo collegato. Si tiene il nome
        /// **com'e' scritto**, senza normalizzare niente: normalizzarlo qui
        /// vorrebbe dire restituire una grafia che il cartiglio non usa, cioe'
        /// esattamente l'errore che questa operazione esiste per prevenire.
        /// </summary>
        /// <summary>
        /// Il nome della proprieta' senza il suffisso che l'operazione ci
        /// appiccica per dire DA DOVE viene: «Descrizione  (dal pezzo, non dal
        /// disegno)» torna «Descrizione».
        ///
        /// Sta qui e non scritto due volte perche' il suffisso e' una scelta
        /// di presentazione, e chi confronta due elenchi deve confrontare i
        /// nomi veri — non quelli vestiti per essere letti.
        /// </summary>
        private static string BareName(string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return itemName;
            int i = itemName.IndexOf("  (", StringComparison.Ordinal);
            return i < 0 ? itemName.Trim() : itemName.Substring(0, i).Trim();
        }

        /// <summary>
        /// I nomi delle proprieta' che esistono sul MODELLO mostrato nella
        /// prima vista vera — che e' quello da cui `$PRPSHEET` prende.
        ///
        /// Si guardano TUTTI E DUE I CASSETTI: le proprieta' del documento e
        /// quelle della configurazione attiva sono elenchi separati, non si
        /// ereditano e non si vedono a vicenda. Misurato il 18/9/2026: una
        /// proprieta' scritta sul documento **non** si vede dalla
        /// configurazione, e il cartiglio la mostra lo stesso — quindi
        /// guardarne uno solo direbbe il falso in un verso o nell'altro.
        ///
        /// Torna `null` — non un elenco vuoto — quando non si e' potuto
        /// guardare: «non ho trovato niente» e «non ho potuto guardare» sono
        /// due risposte diverse, e chi legge deve poterle distinguere.
        /// </summary>
        private static List<string> ViewModelProperty(
            IDrawingDoc drawing, out string whomIChecked)
        {
            whomIChecked = null;
            try
            {
                // La prima «view» e' il FOGLIO: le viste vere cominciano dopo.
                var v = (drawing.GetFirstView() as IView)?.GetNextView() as IView;
                IModelDoc2 refObj = null;
                while (v != null && refObj == null)
                {
                    try { refObj = v.ReferencedDocument as IModelDoc2; } catch { }
                    v = v.GetNextView() as IView;
                }
                if (refObj == null) return null;

                // IL PERCORSO, non il titolo: e' l'unico indirizzo che non
                // mente quando due documenti si chiamano uguale.
                try { whomIChecked = refObj.GetPathName(); } catch { }
                if (string.IsNullOrWhiteSpace(whomIChecked))
                    try { whomIChecked = "senza percorso, titolo «" + refObj.GetTitle() + "»"; } catch { }

                var outside = new List<string>();
                var location = new List<string> { "" };
                try
                {
                    var g = refObj.ConfigurationManager as IConfigurationManager;
                    var att = g?.ActiveConfiguration?.Name;
                    if (!string.IsNullOrWhiteSpace(att)) location.Add(att);
                }
                catch { }

                foreach (var propertyManager in location)
                {
                    try
                    {
                        var gp = refObj.Extension.get_CustomPropertyManager(propertyManager)
                                 as ICustomPropertyManager;
                        var names = gp?.GetNames() as string[];
                        if (names != null)
                            foreach (var n in names)
                                if (!outside.Contains(n, StringComparer.OrdinalIgnoreCase))
                                    outside.Add(n);
                    }
                    catch { }
                }
                return outside;
            }
            catch { return null; }
        }

        private static List<string> ConnectedNames(string text)
        {
            var outside = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return outside;
            foreach (Match m in Connection.Matches(text))
            {
                var itemName = m.Groups[2].Value;
                // `$PRPSHEET` prende la proprieta' dal MODELLO in vista, non
                // dal disegno: e' un cassetto diverso, e confonderli fa
                // scrivere sul documento sbagliato.
                if (m.Groups[1].Success) itemName = itemName + "  (dal pezzo, non dal disegno)";
                if (!outside.Contains(itemName, StringComparer.Ordinal)) outside.Add(itemName);
            }
            return outside;
        }

        private static string TestText(Func<string> f)
        {
            try { return f() ?? ""; } catch { return ""; }
        }
    }
}
