using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// GLI OCCHI SULL'ASSIEME (voce GA1).
    ///
    /// ── COS'ERA PRIMA, E PERCHE' NON BASTAVA ─────────────────────────────
    ///
    /// Fino alla 0.91.0 `StatoDocumento.Leggi` aveva un `if (parte == null)` che
    /// rispondeva, per QUALUNQUE assieme:
    ///
    ///     corpo_solido: false
    ///     nota: "documento di assieme: l'assistente non lo modifica ancora"
    ///
    /// e tornava. Era onesto — nessuna operazione di assieme esisteva — ma
    /// lasciava il modello **cieco**: lo stato e' l'unica cosa che vede, e da
    /// quella riga non puo' sapere quanti componenti ci sono, come si chiamano,
    /// quali sono soppressi. Non poteva nemmeno RIFIUTARE con cognizione, perche'
    /// non sapeva niente di cio' che aveva davanti.
    ///
    /// ── LA DIFFERENZA CHE CONTA CON LE FACCE ─────────────────────────────
    ///
    /// Gli uid delle facce e degli spigoli sono **POSIZIONALI**: si rinumerano
    /// per area decrescente a ogni lettura, e dopo una modifica vanno riletti.
    /// E' la trappola piu' cara del progetto.
    ///
    /// **Per i componenti non e' cosi', e va sfruttato.** Ogni componente ha un
    /// `Name2` unico dentro l'assieme («base-1», «base-2», «coperchio-1»):
    /// SOLIDWORKS lo garantisce, perche' e' il nome con cui li distingue nella
    /// struttura. Quindi qui l'ordine si puo' rendere **stabile** invece che
    /// posizionale: si ordina per `Name2` e `C1..Cn` resta lo stesso fra due
    /// letture, purche' nessuno abbia aggiunto o tolto componenti.
    ///
    /// Non e' un dettaglio estetico: un uid che cambia da solo costringe a
    /// rileggere prima di ogni passo, e il giorno che qualcuno se ne dimentica
    /// l'operazione va sul componente sbagliato **senza dare errore**.
    ///
    /// ── LA REGOLA SUL TRY/CATCH, che qui vale doppio ─────────────────────
    ///
    /// Un `try` unico attorno a tutto farebbe sparire l'intero elenco per colpa
    /// di **un** componente con un riferimento rotto — e i riferimenti rotti, in
    /// un assieme vero, sono la normalita', non l'eccezione. Ogni campo ha il
    /// suo `try`, come in `StatoTavola`: quello che non si legge si tace, il
    /// resto arriva.
    ///
    /// E soprattutto: se questa funzione lanciasse, romperebbe
    /// `StatoDocumento.Leggi` **anche per le parti normali**, che passano dalla
    /// stessa chiamata.
    /// </summary>
    public partial class StatoDocumento
    {
        /// <summary>Quanti componenti si descrivono per esteso, al massimo.</summary>
        ///
        /// Stesso ragionamento del tetto sulle facce (voce E12): un assieme di
        /// carpenteria vero ne ha centinaia, e scriverli tutti scaccerebbe dal
        /// contesto le cose che servono. Chi ne ha di piu' lo SA, perche' il
        /// conteggio totale si scrive comunque.
        private const int MaxComponenti = 40;

        /// <summary>
        /// I componenti nell'ordine che decide gli uid `C1..Cn`.
        ///
        /// ── PERCHE' STA IN UN METODO E NON DENTRO `DescriviAssieme` ──────
        ///
        /// Questo ordine **e' il significato di `C1`**. Lo stato lo usa per
        /// numerare, e `accoppia` (voce GA4) lo usa per ritrovare il componente
        /// che l'utente ha nominato. Se le due liste fossero costruite in due
        /// posti, basterebbe che una cambiasse perche' **`C1` volesse dire due
        /// cose diverse** — e allora si accoppierebbe il componente sbagliato
        /// senza che niente dia errore. E' il difetto che non da' errore nella
        /// forma piu' banale e piu' cara: due copie della stessa regola.
        ///
        /// L'ordine e' per `Name2`, che dentro un assieme e' unico: cosi'
        /// `C1..Cn` resta lo stesso fra due letture, al contrario degli uid
        /// delle facce, che sono posizionali e si rinumerano.
        /// </summary>
        internal static List<KeyValuePair<string, IComponent2>> ComponentiInOrdine(object[] tutti)
        {
            var voci = new List<KeyValuePair<string, IComponent2>>();
            if (tutti == null) return voci;
            foreach (var o in tutti)
            {
                var c = o as IComponent2;
                if (c == null) continue;
                string nome = null;
                try { nome = c.Name2; } catch { }
                voci.Add(new KeyValuePair<string, IComponent2>(nome ?? "", c));
            }
            voci.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return voci;
        }

        /// <summary>Componenti, conteggi e configurazione di un assieme.</summary>
        private static void DescriviAssieme(IAssemblyDoc assieme, Dictionary<string, object> dati)
        {
            if (assieme == null)
            {
                dati["nota"] = "assieme illeggibile";
                return;
            }

            // `false` = TUTTI i componenti, anche dentro i sottoassiemi.
            //
            // La scelta e' deliberata e va detta: con `true` si vedrebbe solo il
            // primo livello, e un sottoassieme apparirebbe come **un** componente
            // con dentro il nulla. Il modello leggerebbe «2 componenti» su un
            // assieme che ne ha trenta, e ogni suo ragionamento sarebbe
            // plausibile e sbagliato. Il conteggio del primo livello si scrive
            // lo stesso, a parte, perche' e' l'altra domanda vera.
            object[] tutti = null;
            try { tutti = assieme.GetComponents(false) as object[]; } catch { }
            if (tutti == null)
            {
                dati["nota"] = "l'assieme non ha risposto all'elenco dei componenti";
                return;
            }

            try
            {
                var primi = assieme.GetComponents(true) as object[];
                if (primi != null) dati["componenti_primo_livello"] = primi.Length;
            }
            catch { }

            dati["quanti_componenti"] = tutti.Length;

            // ── L'ORDINE STABILE ─────────────────────────────────────────
            //
            // Si ordina per `Name2`, che dentro un assieme e' unico. L'ordine in
            // cui `GetComponents` li restituisce non e' dichiarato da nessuna
            // parte: fidarsene vorrebbe dire uid che cambiano quando SOLIDWORKS
            // decide di cambiarlo, cioe' la trappola delle facce, ripetuta dove
            // si poteva evitare.
            var voci = ComponentiInOrdine(tutti);

            var elenco = new List<Dictionary<string, object>>();
            int soppressi = 0, leggeri = 0, fissi = 0;
            int contati = 0, appoggiati = 0;
            bool letturaGradiFallita = false;
            var liberi = new List<string>();
            for (int i = 0; i < voci.Count; i++)
            {
                var c = voci[i].Value;
                var voce = new Dictionary<string, object>
                {
                    ["uid"] = "C" + (i + 1),
                    ["nome"] = voci[i].Key,
                };

                try
                {
                    var p = c.GetPathName();
                    if (!string.IsNullOrWhiteSpace(p)) voce["percorso"] = p;
                }
                catch { }

                try
                {
                    var cfg = c.ReferencedConfiguration;
                    if (!string.IsNullOrWhiteSpace(cfg)) voce["configurazione"] = cfg;
                }
                catch { }

                // LO STATO SI SCRIVE COL NOME, NON COL NUMERO.
                // `GetSuppression2` risponde un intero di `swComponentSuppressionState_e`:
                // «2» non dice niente a chi legge, «risolto» si'. E' la stessa
                // regola dei difetti di `Check3`, gia' pagata altrove.
                try
                {
                    int stato = c.GetSuppression2();
                    voce["stato"] = NomeDelloStato(stato);
                    if (stato == (int)swComponentSuppressionState_e.swComponentSuppressed) soppressi++;
                    if (stato == (int)swComponentSuppressionState_e.swComponentLightweight ||
                        stato == (int)swComponentSuppressionState_e.swComponentFullyLightweight)
                        leggeri++;
                }
                catch { }

                // FISSO O LIBERO. E' la prima domanda vera su un assieme — «e'
                // vincolato?» — e la sua meta' piu' economica: un componente
                // fisso non si muove perche' qualcuno l'ha inchiodato, non
                // perche' sia accoppiato. Confondere le due cose fa credere
                // vincolato un assieme che non lo e'.
                try
                {
                    bool f = c.IsFixed();
                    voce["fisso"] = f;
                    if (f) fissi++;
                }
                catch { }

                // ── DOVE STA L'ORIGINE, IN MM (parte di GA2) ────────────
                //
                // IL CAMPO SI CHIAMA `origine_mm` E NON `posizione_mm`, ED E'
                // LA RIPARAZIONE DI UN DIFETTO CHE NON DAVA ERRORE.
                //
                // Misurato il 17/9/2026: **`AddComponent5(x, y, z)` posiziona il
                // CENTRO dell'ingombro del componente, non la sua origine.** Un
                // pezzo estruso a una direzione sola — che e' quello che fa
                // `estrudi_rettangolo` — ha l'origine su una faccia, non a meta'
                // spessore: inserito a `(0, 0, 30)`, la sua origine finisce a
                // `(0, -2, 30)` se e' spesso 4, a `(0, -5, 0)` se e' spesso 10 e
                // lo si chiede a zero. Previsto prima di misurarlo, e misurato.
                //
                // Chiamare questo campo «posizione» invitava a confrontarlo con
                // il punto CHIESTO, e i due non coincidono quasi mai: nessuno
                // dei due numeri e' sbagliato, sono due cose diverse, e niente
                // lo diceva. Il nome adesso lo dice.
                //
                //
                // `GetTotalTransform(false)`: il parametro si chiama
                // `IncludePresentationXform`, e `true` darebbe la posizione
                // **nella vista esplosa** — un numero plausibile e sbagliato
                // ogni volta che un esploso esiste.
                //
                // L'INDICE DELLA TRASLAZIONE NON E' ASSUNTO, E' PROVATO. La
                // forma classica di `MathTransform.ArrayData` e' 9 numeri di
                // rotazione, 3 di traslazione, 1 di scala — quindi 9, 10, 11.
                // «Classica» pero' e' esattamente il genere di cosa che in
                // questo progetto si e' gia' rivelata falsa (la normale del
                // piano stava nella terza RIGA, non nella terza colonna, voce
                // GP9). Percio' il caso di banco inserisce un componente a
                // **z = 30 mm** e pretende di rileggere `[0, 0, 30]`: se gli
                // indici fossero altri, diventa rosso qui invece che in
                // officina.
                var dove = PosizioneDelComponenteMm(c);
                if (dove != null) voce["origine_mm"] = dove;

                // Gli accoppiamenti: per ora il CONTEGGIO. Il formato di quello
                // che `GetMates()` restituisce e' `Object` e va accertato prima
                // di leggerne dentro (voce GA4); il numero invece si conta
                // subito, ed e' gia' qualcosa: zero accoppiamenti su un
                // componente non fisso vuol dire appoggiato.
                // `GetMates()` risponde **null quando sono zero**, non un array
                // vuoto: il conteggio passa da `OperazioniSw.AccoppiamentiDi`,
                // che separa «zero» da «non ho potuto leggere» guardando se il
                // componente risponde ancora al proprio nome.
                int suoiAcc = OperazioniSw.AccoppiamentiDi(c);
                if (suoiAcc >= 0) voce["accoppiamenti"] = suoiAcc;

                // ── I GRADI DI LIBERTA' RESIDUI (voce GA5) ───────────────
                //
                // E' la misura che distingue un assieme da quattro pezzi nella
                // stessa stanza, e si legge senza nessuna chiamata in piu'
                // rispetto a quelle che questo giro fa gia'.
                //
                // NON SI CONTANO I FISSI. Un componente fisso ha zero gradi
                // **per definizione**, non perche' qualcuno l'abbia accoppiato:
                // sommarlo farebbe **sembrare vincolato** un assieme che lo e'
                // solo perche' il primo pezzo non si muove. Il primo componente
                // inserito e' normalmente fisso, quindi l'errore scatterebbe
                // sempre, su ogni assieme.
                var gradi = OperazioniSw.GradiDiLiberta(c);
                voce["stato_gradi_di_liberta"] = gradi.Stato;
                if (gradi.Disponibile) voce["gradi_di_liberta_residui"] = gradi.Quanti;
                else letturaGradiFallita = true;

                // «E' APPOGGIATO?» SI RISPONDE CON DUE COSE CHE FUNZIONANO.
                //
                // I gradi di liberta' non si possono avere (vedi
                // `OperazioniSw.GradiDiLiberta`). Quello che si sa per certo e':
                // un componente **non fisso** con **zero accoppiamenti** e'
                // appoggiato li'. Non si sa di quanto sia libero, e non lo si
                // dice: *un numero inventato e' peggio di un numero assente*.
                bool eFisso = voce.ContainsKey("fisso") &&
                              Convert.ToString(voce["fisso"]).Equals(
                                  "True", StringComparison.OrdinalIgnoreCase);
                int suoiAccoppiamenti = suoiAcc;
                if (!eFisso)
                {
                    contati++;
                    if (suoiAccoppiamenti == 0)
                    {
                        voce["appoggiato"] = true;
                        appoggiati++;
                        if (liberi.Count < 8) liberi.Add(voci[i].Key);
                    }
                    else if (suoiAccoppiamenti > 0) voce["appoggiato"] = false;
                }

                if (elenco.Count < MaxComponenti) elenco.Add(voce);
            }

            dati["componenti"] = elenco;
            if (voci.Count > MaxComponenti)
                dati["componenti_non_elencati"] = voci.Count - MaxComponenti;

            // I CONTEGGI SI SCRIVONO ANCHE QUANDO VALGONO ZERO.
            // «soppressi: 0» e' un'informazione; l'assenza del campo si legge
            // come «non l'ho guardato», e sono due cose diverse. E' la stessa
            // riga che il README difende per la regressione: *un conteggio che
            // sparisce quando e' zero e' un conteggio che nessuno controlla.*
            dati["componenti_soppressi"] = soppressi;
            dati["componenti_leggeri"] = leggeri;
            dati["componenti_fissi"] = fissi;

            // ── «QUESTO ASSIEME E' VINCOLATO?» IN UNA RIGA (voce GA5) ────
            //
            // Il totale traduce la misura tecnica in qualcosa che chi conferma
            // legge in tre secondi. Ed e' la domanda vera: un assieme con
            // dodici gradi residui non e' montato, e' appoggiato.
            //
            // SI DICE ANCHE QUANTI COMPONENTI SONO ENTRATI NEL CONTO, e se la
            // lettura e' fallita su qualcuno. Un totale che copre in silenzio
            // solo una parte e' peggio di nessun totale: *un numero che non dice
            // su cosa e' calcolato e' un numero di cui fidarsi per sbaglio.*
            if (contati > 0)
            {
                dati["componenti_non_fissi"] = contati;
                dati["componenti_appoggiati"] = appoggiati;
                dati["assieme_vincolato"] = appoggiati == 0;
                if (liberi.Count > 0) dati["componenti_ancora_liberi"] = liberi;

                dati["giudizio_vincoli"] = appoggiati == 0
                    ? "Tutti i " + contati + (contati == 1 ? " componente non fisso ha" : " componenti non fissi hanno") +
                      " almeno un accoppiamento. Di QUANTO siano ancora liberi non " +
                      "lo so: SOLIDWORKS non fornisce i gradi di liberta' residui da qui."
                    : "NON e' montato: " + appoggiati + " su " + contati +
                      (contati == 1 ? " componente non fisso" : " componenti non fissi") +
                      " non ha nessun accoppiamento" +
                      (liberi.Count == 0 ? "" : " (" + string.Join(", ", liberi) + ")") +
                      ". Sono appoggiati li': si spostano se qualcuno li trascina, e " +
                      "l'assieme sembra montato senza esserlo.";
            }

            // LA COSA CHE NON SI SA, DETTA. Non e' un dettaglio tecnico: chi
            // legge «assieme_vincolato: true» deve sapere che vuol dire «ogni
            // componente ha almeno un accoppiamento», NON «non si muove piu'».
            // Un accoppiamento solo lascia liberi cinque gradi su sei, e questo
            // stato non li vede.
            if (letturaGradiFallita)
                dati["cosa_non_so_dei_vincoli"] =
                    "SOLIDWORKS non mi da' i gradi di liberta' residui (risponde «non " +
                    "disponibile» su ogni componente, fisso o libero che sia). Quindi " +
                    "«vincolato» qui vuol dire «ha almeno un accoppiamento», non «non si " +
                    "muove piu'»: un accoppiamento solo ne lascia liberi cinque su sei.";

            // ── LA CONFIGURAZIONE ATTIVA ─────────────────────────────────
            // Un assieme ne ha come una parte, e sbagliarla vuol dire misurare
            // una variante diversa da quella confermata.
            try
            {
                var doc = assieme as IModelDoc2;
                var cfg = doc == null ? null : doc.GetActiveConfiguration() as IConfiguration;
                if (cfg != null)
                {
                    dati["configurazione"] = cfg.Name;
                    DescriviLaDistinta(cfg, dati);
                }
            }
            catch { }

            // ── COSA ANCORA NON SI SA FARE, DETTO QUI ────────────────────
            //
            // La nota resta, ma non e' piu' la stessa: prima diceva «non lo
            // modifica ancora» e basta, cioe' chiudeva il discorso. Adesso lo
            // stato c'e', e la nota dice **cosa manca**, che e' un'altra cosa.
            // Un modello che legge «vedo quattro componenti ma non so
            // accoppiarli» sa cosa puo' chiedere; uno che legge «non lo modifico»
            // puo' solo smettere.
            dati["corpo_solido"] = false;
            dati["cosa_non_so_ancora_fare"] =
                "Di questo assieme so dire com'e' fatto, non cambiarlo: inserire " +
                "componenti, accoppiarli e leggerne i gradi di liberta' residui " +
                "non sono ancora operazioni. Le posizioni dei componenti non " +
                "sono in questo stato.";
        }

        /// <summary>
        /// Dove sta il componente, in mm, o `null` se non si e' potuto leggere.
        ///
        /// NON si usa `GetXform`, che la voce GA2 nominava: **su `IComponent2`
        /// non esiste** (verificato per riflessione il 16/9/2026; ci sono
        /// `get_Transform`, `get_Transform2`, `get_PresentationTransform` e
        /// `GetSpecificTransform`). Era un nome ricordato, non letto.
        /// </summary>
        private static object[] PosizioneDelComponenteMm(IComponent2 c)
        {
            try
            {
                var t = c.GetTotalTransform(false) as IMathTransform;
                if (t == null) return null;
                var d = t.ArrayData as double[];
                // Meno di dodici numeri vuol dire che la forma NON e' quella
                // che si crede: meglio non scrivere niente che scrivere tre
                // numeri presi dal posto sbagliato. Un campo assente si vede;
                // una coordinata sbagliata no.
                if (d == null || d.Length < 12) return null;
                return new object[]
                {
                    Math.Round(Unita.Mm(d[9]), 3),
                    Math.Round(Unita.Mm(d[10]), 3),
                    Math.Round(Unita.Mm(d[11]), 3),
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// COME QUESTA CONFIGURAZIONE ANDRA' IN DISTINTA (voce GA6).
        ///
        /// SONO SEI CAMPI, NON QUATTRO. La voce del TODO ne elencava quattro;
        /// la riflessione sull'interop di questa installazione ne trova sei, e
        /// i due che mancavano sono proprio quelli che decidono se un
        /// sottoassieme si apre in distinta:
        ///
        ///   BOMPartNoSource          da dove viene il codice articolo
        ///   UseAlternateNameInBOM    usare un nome diverso dal file
        ///   AlternateName            quale
        ///   UseDescriptionInBOM      usare la descrizione al posto del nome
        ///   ShowChildComponentsInBOM mostrare i figli di un sottoassieme
        ///   ChildComponentDisplayInBOM come mostrarli
        ///
        /// PERCHE' CONTANO QUI E NON SOLO IN TAVOLA. Una distinta sbagliata non
        /// da' nessun errore: esce un elenco plausibile, con dentro pezzi di
        /// troppo o di meno, e chi la legge in officina non ha modo di
        /// accorgersene. Gli ultimi due sono i piu' pericolosi: un sottoassieme
        /// che si apre quando non doveva aggiunge righe che nessuno ha chiesto.
        ///
        /// Si LEGGONO e basta. Scriverli sarebbe un'operazione, con la sua
        /// conferma: qui si dice com'e' messa la configurazione, cosi' chi
        /// guarda puo' accorgersi prima di stampare.
        /// </summary>
        private static void DescriviLaDistinta(IConfiguration cfg, Dictionary<string, object> dati)
        {
            var d = new Dictionary<string, object>();

            // Ogni campo il suo try: su una configurazione vera basta che uno
            // non risponda perche' un try unico li faccia sparire tutti.
            try { d["codice_da"] = NomeDellaFonteDelCodice(cfg.BOMPartNoSource); } catch { }
            try { d["usa_nome_alternativo"] = cfg.UseAlternateNameInBOM; } catch { }
            try
            {
                var n = cfg.AlternateName;
                if (!string.IsNullOrWhiteSpace(n)) d["nome_alternativo"] = n;
            }
            catch { }
            try { d["usa_descrizione"] = cfg.UseDescriptionInBOM; } catch { }
            try { d["mostra_i_figli"] = cfg.ShowChildComponentsInBOM; } catch { }
            try { d["come_mostra_i_figli"] = cfg.ChildComponentDisplayInBOM; } catch { }

            if (d.Count > 0) dati["distinta"] = d;
        }

        /// <summary>
        /// Da dove viene il codice articolo in distinta, col nome.
        ///
        /// I valori sono di `swBOMPartNumberSource_e`. Come per la soppressione:
        /// quelli che non si riconoscono si scrivono col numero invece di
        /// essere tradotti a caso — **un nome inventato e' peggio di un numero
        /// onesto**, perche' chi legge il nome ci crede.
        /// </summary>
        private static string NomeDellaFonteDelCodice(int fonte)
        {
            switch (fonte)
            {
                case (int)swBOMPartNumberSource_e.swBOMPartNumber_DocumentName:
                    return "nome del documento";
                case (int)swBOMPartNumberSource_e.swBOMPartNumber_ConfigurationName:
                    return "nome della configurazione";
                case (int)swBOMPartNumberSource_e.swBOMPartNumber_ParentName:
                    return "nome del genitore";
                case (int)swBOMPartNumberSource_e.swBOMPartNumber_UserSpecified:
                    return "scritto a mano";
                default:
                    return "sconosciuta (" +
                           fonte.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        /// <summary>
        /// Lo stato di soppressione, col nome.
        ///
        /// I valori sono di `swComponentSuppressionState_e`. Quelli che non si
        /// riconoscono si scrivono col numero fra parentesi invece di essere
        /// tradotti a caso: **un nome inventato e' peggio di un numero onesto**,
        /// perche' chi legge il nome ci crede.
        /// </summary>
        private static string NomeDelloStato(int stato)
        {
            switch (stato)
            {
                case (int)swComponentSuppressionState_e.swComponentSuppressed:
                    return "soppresso";
                case (int)swComponentSuppressionState_e.swComponentLightweight:
                    return "leggero";
                case (int)swComponentSuppressionState_e.swComponentFullyResolved:
                    return "risolto";
                case (int)swComponentSuppressionState_e.swComponentResolved:
                    return "risolto";
                case (int)swComponentSuppressionState_e.swComponentFullyLightweight:
                    return "leggero (tutto)";
                case (int)swComponentSuppressionState_e.swComponentInternalIdMismatch:
                    return "identificativo non corrispondente";
                default:
                    return "sconosciuto (" +
                           stato.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }
    }
}
