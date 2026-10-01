using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// IL CARTIGLIO: COSA CHIEDE, E COSA MOSTRA (voce GT3).
    ///
    /// ── IL CARTIGLIO NON SI SCRIVE: SI ALIMENTA ─────────────────────────
    ///
    /// Un cartiglio da modello non contiene testo: contiene **collegamenti** a
    /// proprieta' personalizzate, scritti `$PRP:"Descrizione"`. Riempirlo vuol
    /// dire scrivere quelle proprieta' — e `scrivi_proprieta` lo sa gia' fare.
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
    public partial class OperazioniSw
    {
        /// <summary>
        /// I collegamenti che un cartiglio puo' portare. `$PRPSHEET` prende la
        /// proprieta' dal MODELLO mostrato nella vista, non dal disegno: e' la
        /// distinzione che fa scrivere la proprieta' sul documento sbagliato.
        /// </summary>
        private static readonly Regex Collegamento =
            new Regex("\\$PRP(SHEET)?:\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);

        private Esito Cartiglio(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito(
                    "Il documento attivo non e' una tavola: il cartiglio sta su un " +
                    "foglio di disegno.");

            ISheet foglio;
            try { foglio = disegno.GetCurrentSheet() as ISheet; }
            catch (Exception ex) { return Fallito("Non riesco a leggere il foglio: " + ex.Message); }
            if (foglio == null) return Fallito("La tavola non ha un foglio attivo.");

            var note = NoteDelCartiglio(disegno, foglio, out string daDove);
            if (note.Count == 0)
                return Fallito(
                    "Non trovo nessuna nota nel cartiglio di questo foglio" +
                    (daDove == null ? "" : " (cercate " + daDove + ")") +
                    ". O il modello di tavola non ha un cartiglio, o e' fatto di " +
                    "disegno invece che di note: nel secondo caso non c'e' niente da " +
                    "alimentare, il testo e' dipinto dentro il formato.");

            // ── COSA CHIEDE E COSA MOSTRA, campo per campo ──────────────
            var campi = new List<object>();
            var chieste = new List<string>();
            var vuote = new List<string>();
            var mostrate = new List<string>();
            int collegati = 0;

            foreach (var n in note)
            {
                string chiede = null, mostra = null;
                try { chiede = n.PropertyLinkedText; } catch { }
                try { mostra = n.GetText(); } catch { }

                var nomi = NomiCollegati(chiede);
                if (nomi.Count > 0) collegati++;

                foreach (var x in nomi)
                    if (!chieste.Contains(x, StringComparer.Ordinal)) chieste.Add(x);

                // UN CAMPO COLLEGATO CHE NON MOSTRA NIENTE E' IL DIFETTO.
                // Non e' «vuoto per scelta»: e' un collegamento che non ha
                // trovato la sua proprieta'.
                bool mostraNiente = string.IsNullOrWhiteSpace(mostra);
                if (nomi.Count > 0 && mostraNiente)
                    foreach (var x in nomi)
                        if (!vuote.Contains(x, StringComparer.Ordinal)) vuote.Add(x);

                // E QUELLE CHE ARRIVANO DAVVERO IN FONDO.
                //
                // Non e' il complemento di «vuote»: e' l'affermazione positiva,
                // e serve perche' **si possa provare che qualcosa funziona**.
                // Un campo che elenca solo cio' che manca non si puo' usare per
                // dimostrare che una scrittura e' arrivata: si potrebbe solo
                // dire che non e' nell'elenco dei guasti, e «non e' fra i
                // rotti» non e' «funziona».
                if (nomi.Count > 0 && !mostraNiente)
                    foreach (var x in nomi)
                        if (!mostrate.Contains(x, StringComparer.Ordinal)) mostrate.Add(x);

                var voce = new Dictionary<string, object>
                {
                    ["chiede"] = string.IsNullOrWhiteSpace(chiede) ? null : chiede,
                    ["mostra"] = mostra ?? "",
                };
                if (nomi.Count > 0) voce["proprieta"] = nomi.ToArray();
                campi.Add(voce);
            }

            var misure = new Dictionary<string, object>
            {
                ["foglio"] = NomeDelFoglio(foglio) ?? "",
                ["formato"] = ProvaTesto(() => foglio.GetSheetFormatName()),
                ["note_nel_cartiglio"] = note.Count,
                ["note_collegate"] = collegati,
                ["cercate_in"] = daDove,
                ["campi"] = campi.ToArray(),
            };

            // ── L'ELENCO CHE TOGLIE L'INVENZIONE ────────────────────────
            //
            // E' il pezzo che vale: i nomi ESATTI che questo cartiglio vuole,
            // copiabili alla lettera dentro `scrivi_proprieta`.
            if (chieste.Count > 0)
            {
                misure["proprieta_che_il_cartiglio_vuole"] = chieste.ToArray();
                misure["come_si_usa"] =
                    "Copia questi nomi ALLA LETTERA dentro «scrivi_proprieta». Una " +
                    "maiuscola o uno spazio diversi non danno nessun errore: la " +
                    "proprieta' viene scritta, il cartiglio non la mostra, e il PDF " +
                    "esce con la casella vuota.";
            }
            // Anche questo sempre, per la stessa ragione: e' l'affermazione
            // positiva, e un'affermazione che sparisce quando e' falsa non e'
            // un'affermazione.
            misure["chieste_e_mostrate"] = mostrate.ToArray();

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
            // `ProprietaDelModelloInVista` torna `null` quando non ci arriva.
            // Senza dirlo, l'elenco qui sotto resterebbe vuoto **per ignoranza**
            // e non per misura — e un elenco vuoto per ignoranza passa
            // qualunque controllo che chieda «non c'e' dentro X».
            //
            // Il 18/9/2026 e' successo esattamente questo, ed e' costato un
            // giro di banco: la distinzione era scritta nel commento di quella
            // funzione e **non era visibile a chi legge la risposta**.
            // *Una distinzione che vive solo in un commento non e' una
            // distinzione: e' un'intenzione.*
            string chiGuardato;
            var suDisco = ProprietaDelModelloInVista(disegno, out chiGuardato);
            misure["modello_guardato_per_le_proprieta"] = chiGuardato ??
                "NON SONO ARRIVATO AL MODELLO: i due elenchi qui sotto sono vuoti " +
                "perche' non ho potuto guardare, non perche' non ci fosse niente.";

            var presentiMaMute = new List<string>();
            if (suDisco != null)
                foreach (var x in vuote)
                    if (suDisco.Contains(NomeNudo(x), StringComparer.OrdinalIgnoreCase))
                        presentiMaMute.Add(x);

            misure["chieste_ma_vuote"] = vuote.ToArray();

            // SEMPRE, ANCHE VUOTO, e non e' pignoleria: il banco tratta un
            // campo ASSENTE come prova rossa (voce F4), quindi un campo che
            // compare solo quando ha qualcosa dentro **non si puo' nemmeno
            // pretendere vuoto**. E chi legge la risposta, non trovandolo,
            // conclude che l'operazione non sappia rispondere invece che
            // l'elenco sia vuoto.
            misure["chieste_presenti_sul_modello_ma_mostrate_vuote"] = presentiMaMute.ToArray();
            if (presentiMaMute.Count > 0)
                misure["e_questo_non_e_colpa_di_chi_disegna"] =
                    "Queste proprieta' CI SONO sul modello mostrato nella vista, e il " +
                    "cartiglio le mostra vuote lo stesso: il collegamento non ha " +
                    "risolto. Riscriverle non cambia niente. Prova a ricostruire la " +
                    "tavola e a rileggere; se restano vuote, il collegamento e' rotto.";

            string vistaPerProprieta = ProvaTesto(() => foglio.CustomPropertyView);
            if (!string.IsNullOrWhiteSpace(vistaPerProprieta))
                misure["vista_da_cui_prende_le_proprieta_di_foglio"] = vistaPerProprieta;

            if (chieste.Count == 0)
                misure["giudizio"] =
                    "Il cartiglio ha " + note.Count + (note.Count == 1 ? " nota" : " note") +
                    " ma **nessuna collegata a una proprieta'**: il testo e' scritto " +
                    "fisso dentro il formato. Riempirlo con «scrivi_proprieta» non " +
                    "cambierebbe niente, e non te lo lascio credere.";
            else if (vuote.Count == 0)
                misure["giudizio"] =
                    "Il cartiglio vuole " + chieste.Count +
                    (chieste.Count == 1 ? " proprieta'" : " proprieta'") + " (" +
                    string.Join(", ", chieste) + ") e le mostra tutte: nessuna casella " +
                    "vuota.";
            else
                misure["giudizio"] =
                    "ATTENZIONE: il cartiglio chiede " + chieste.Count + " proprieta' e " +
                    vuote.Count + " NON mostrano niente (" + string.Join(", ", vuote) +
                    "). O non sono ancora state scritte, o sono scritte con una grafia " +
                    "diversa da quella che il cartiglio referenzia — e quel caso non da' " +
                    "nessun errore: la tavola esce con le caselle vuote. I nomi qui " +
                    "sopra sono quelli giusti, copiali alla lettera.";

            _log("[agente] " + misure["giudizio"]);
            return RiuscitoSenzaCreare(misure);
        }

        /// <summary>
        /// Le note del cartiglio. Due strade, e si dice quale ha risposto:
        /// il `TitleBlock` vero (che non tutti i modelli di tavola hanno) e, in
        /// mancanza, le annotazioni del FOGLIO — che nell'API e' la prima
        /// «vista», quella che porta il formato.
        /// </summary>
        private static List<INote> NoteDelCartiglio(
            IDrawingDoc disegno, ISheet foglio, out string daDove)
        {
            daDove = null;
            var fuori = new List<INote>();

            try
            {
                var blocco = foglio.TitleBlock as ITitleBlock;
                if (blocco != null)
                {
                    var n = blocco.GetNotes() as object[];
                    if (n != null)
                        foreach (var o in n) { var nota = o as INote; if (nota != null) fuori.Add(nota); }
                    if (fuori.Count > 0) { daDove = "nel blocco cartiglio del foglio"; return fuori; }
                }
            }
            catch { }

            // LA PRIMA «VISTA» DI UNA TAVOLA E' IL FOGLIO, non una vista del
            // pezzo: e' la stessa cosa che `ContaViste` salta per contare le
            // viste vere, e qui invece e' proprio quella che serve.
            try
            {
                var vistaFoglio = disegno.GetFirstView() as IView;
                if (vistaFoglio != null)
                {
                    var ann = vistaFoglio.GetFirstAnnotation3() as IAnnotation;
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
                            var nota = ann.GetSpecificAnnotation() as INote;
                            if (nota != null) fuori.Add(nota);
                        }
                        catch { }
                        ann = ann.GetNext3() as IAnnotation;
                    }
                    if (fuori.Count > 0) daDove = "fra le note del formato del foglio";
                }
            }
            catch { }

            if (daDove == null) daDove = "nel blocco cartiglio e fra le note del formato";
            return fuori;
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
        private static string NomeNudo(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return nome;
            int i = nome.IndexOf("  (", StringComparison.Ordinal);
            return i < 0 ? nome.Trim() : nome.Substring(0, i).Trim();
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
        private static List<string> ProprietaDelModelloInVista(
            IDrawingDoc disegno, out string chiHoGuardato)
        {
            chiHoGuardato = null;
            try
            {
                // La prima «vista» e' il FOGLIO: le viste vere cominciano dopo.
                var v = (disegno.GetFirstView() as IView)?.GetNextView() as IView;
                IModelDoc2 rif = null;
                while (v != null && rif == null)
                {
                    try { rif = v.ReferencedDocument as IModelDoc2; } catch { }
                    v = v.GetNextView() as IView;
                }
                if (rif == null) return null;

                // IL PERCORSO, non il titolo: e' l'unico indirizzo che non
                // mente quando due documenti si chiamano uguale.
                try { chiHoGuardato = rif.GetPathName(); } catch { }
                if (string.IsNullOrWhiteSpace(chiHoGuardato))
                    try { chiHoGuardato = "senza percorso, titolo «" + rif.GetTitle() + "»"; } catch { }

                var fuori = new List<string>();
                var dove = new List<string> { "" };
                try
                {
                    var g = rif.ConfigurationManager as IConfigurationManager;
                    var att = g?.ActiveConfiguration?.Name;
                    if (!string.IsNullOrWhiteSpace(att)) dove.Add(att);
                }
                catch { }

                foreach (var cassetto in dove)
                {
                    try
                    {
                        var gp = rif.Extension.get_CustomPropertyManager(cassetto)
                                 as ICustomPropertyManager;
                        var nomi = gp?.GetNames() as string[];
                        if (nomi != null)
                            foreach (var n in nomi)
                                if (!fuori.Contains(n, StringComparer.OrdinalIgnoreCase))
                                    fuori.Add(n);
                    }
                    catch { }
                }
                return fuori;
            }
            catch { return null; }
        }

        private static List<string> NomiCollegati(string testo)
        {
            var fuori = new List<string>();
            if (string.IsNullOrWhiteSpace(testo)) return fuori;
            foreach (Match m in Collegamento.Matches(testo))
            {
                var nome = m.Groups[2].Value;
                // `$PRPSHEET` prende la proprieta' dal MODELLO in vista, non
                // dal disegno: e' un cassetto diverso, e confonderli fa
                // scrivere sul documento sbagliato.
                if (m.Groups[1].Success) nome = nome + "  (dal pezzo, non dal disegno)";
                if (!fuori.Contains(nome, StringComparer.Ordinal)) fuori.Add(nome);
            }
            return fuori;
        }

        private static string ProvaTesto(Func<string> f)
        {
            try { return f() ?? ""; } catch { return ""; }
        }
    }
}
