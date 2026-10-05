using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// GLI OCCHI SULL'ASSIEME (voce GA1).
    ///
    /// ── COS'ERA PRIMA, E PERCHE' NON BASTAVA ─────────────────────────────
    ///
    /// Fino alla 0.91.0 `DocumentState.Read` aveva un `if (parte == null)` che
    /// rispondeva, per QUALUNQUE assieme:
    ///
    ///     solid_body: false
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
    /// `DocumentState.Read` **anche per le parti normali**, che passano dalla
    /// stessa chiamata.
    /// </summary>
    public partial class DocumentState
    {
        /// <summary>Quanti componenti si descrivono per esteso, al massimo.</summary>
        ///
        /// Stesso ragionamento del tetto sulle facce (voce E12): un assieme di
        /// carpenteria vero ne ha centinaia, e scriverli tutti scaccerebbe dal
        /// contesto le cose che servono. Chi ne ha di piu' lo SA, perche' il
        /// conteggio totale si scrive comunque.
        private const int MaxComponents = 40;

        /// <summary>
        /// I componenti nell'ordine che decide gli uid `C1..Cn`.
        ///
        /// ── PERCHE' STA IN UN METODO E NON DENTRO `DescribeAssembly` ──────
        ///
        /// Questo ordine **e' il significato di `C1`**. Lo stato lo usa per
        /// numerare, e `mate` (voce GA4) lo usa per ritrovare il componente
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
        internal static List<KeyValuePair<string, IComponent2>> ComponentsInOrder(object[] all)
        {
            var entries = new List<KeyValuePair<string, IComponent2>>();
            if (all == null) return entries;
            foreach (var o in all)
            {
                var c = o as IComponent2;
                if (c == null) continue;
                string itemName = null;
                try { itemName = c.Name2; } catch { }
                entries.Add(new KeyValuePair<string, IComponent2>(itemName ?? "", c));
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return entries;
        }

        /// <summary>
        /// THE NAMES OF THE TOP-LEVEL COMPONENTS (findings L11-3 and L11-5,
        /// lot Z-C21.5), from IAssemblyDoc.GetComponents(true); null when the
        /// list cannot be read, which is «I do not know», not «none».
        ///
        /// It is the one rule for «top level» in the library, for the same
        /// reason as <see cref="ComponentsInOrder"/>: the state publishes
        /// «top_level» from it, and fix_component and replace_component
        /// refuse a nested component with it. Two copies of the rule could
        /// disagree, and a component the state calls top-level would be
        /// refused, or the other way round.
        ///
        /// Name2 is unique inside an assembly, and for a component inside a
        /// sub-assembly it carries the whole path («subAssem1-2/Part1-1»,
        /// IComponent2~Name2.html, Remarks), so a nested component never
        /// shares its name with a top-level one.
        /// </summary>
        internal static HashSet<string> TopLevelNames(object[] topLevel)
        {
            if (topLevel == null) return null;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in topLevel)
            {
                var component = o as IComponent2;
                if (component == null) continue;
                string name = null;
                try { name = component.Name2; } catch { }
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return names;
        }

        /// <summary>Componenti, conteggi e configurazione di un assieme.</summary>
        private static void DescribeAssembly(IAssemblyDoc assemblyDoc, Dictionary<string, object> data)
        {
            if (assemblyDoc == null)
            {
                data["note"] = "unreadable assembly";
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
            object[] all = null;
            try { all = assemblyDoc.GetComponents(false) as object[]; } catch { }
            if (all == null)
            {
                data["note"] = "l'assieme non ha risposto all'elenco dei componenti";
                return;
            }

            // The top-level names serve twice: the count, and «top_level» on
            // each component below (L11-3, Z-C21.5).
            HashSet<string> topLevelNames = null;
            try
            {
                var first = assemblyDoc.GetComponents(true) as object[];
                if (first != null) data["components_top_level"] = first.Length;
                topLevelNames = TopLevelNames(first);
            }
            catch { }

            data["how_many_components"] = all.Length;

            // ── L'ORDINE STABILE ─────────────────────────────────────────
            //
            // Si ordina per `Name2`, che dentro un assieme e' unico. L'ordine in
            // cui `GetComponents` li restituisce non e' dichiarato da nessuna
            // parte: fidarsene vorrebbe dire uid che cambiano quando SOLIDWORKS
            // decide di cambiarlo, cioe' la trappola delle facce, ripetuta dove
            // si poteva evitare.
            var entries = ComponentsInOrder(all);

            var list = new List<Dictionary<string, object>>();
            int suppressedOnes = 0, light = 0, grounded = 0;
            int counted = 0, resting = 0;
            int nested = 0;
            bool degreesReadFailed = false;
            var free = new List<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                var c = entries[i].Value;
                var entry = new Dictionary<string, object>
                {
                    ["uid"] = "C" + (i + 1),
                    ["name"] = entries[i].Key,
                };

                // TOP LEVEL OR INSIDE A SUB-ASSEMBLY (L11-3, Z-C21.5). The list
                // above holds every level, and some answers only hold for the
                // top one: IsFixed «only applies to the top level of components»
                // (IComponent2~IsFixed.html), and ReplaceComponents2 wants «a
                // top-level component». Null when GetComponents(true) did not
                // answer: «I do not know», not «no».
                bool? topLevel = topLevelNames == null ? (bool?)null : topLevelNames.Contains(entries[i].Key);
                entry["top_level"] = topLevel;
                if (topLevel == false) nested++;

                try
                {
                    var p = c.GetPathName();
                    if (!string.IsNullOrWhiteSpace(p))
                    {
                        entry["path"] = p;
                        // IL FILE C'E'? (voce Z-B3) Un componente il cui file manca
                        // sul disco l'assieme lo mostra soppresso, e nient'altro lo
                        // dice. Si scrive solo quando manca: e' il caso raro.
                        if (!System.IO.File.Exists(p)) entry["file_missing"] = true;
                    }
                }
                catch { }

                try
                {
                    var cfg = c.ReferencedConfiguration;
                    if (!string.IsNullOrWhiteSpace(cfg)) entry["configuration"] = cfg;
                }
                catch { }

                // LO STATO SI SCRIVE COL NOME, NON COL NUMERO.
                // `GetSuppression2` risponde un intero di `swComponentSuppressionState_e`:
                // «2» non dice niente a chi legge, «risolto» si'. E' la stessa
                // regola dei difetti di `Check3`, gia' pagata altrove.
                try
                {
                    int docState = c.GetSuppression2();
                    entry["state"] = StateName(docState);
                    if (docState == (int)swComponentSuppressionState_e.swComponentSuppressed) suppressedOnes++;
                    if (docState == (int)swComponentSuppressionState_e.swComponentLightweight ||
                        docState == (int)swComponentSuppressionState_e.swComponentFullyLightweight)
                        light++;
                }
                catch { }

                // FISSO O LIBERO. E' la prima domanda vera su un assieme — «e'
                // vincolato?» — e la sua meta' piu' economica: un componente
                // fisso non si muove perche' qualcuno l'ha inchiodato, non
                // perche' sia accoppiato. Confondere le due cose fa credere
                // vincolato un assieme che non lo e'.
                //
                // NOT ON A NESTED COMPONENT (L11-3): the guide says IsFixed does
                // not apply there («To get the actual state of sub-assemblies,
                // you must get the IModelDoc2 object of the subassembly»), so
                // its answer from the root is not published: the fixed flag is
                // null, «not known from here», and the component is not counted.
                if (topLevel == false) entry["grounded"] = null;
                else
                {
                    try
                    {
                        bool f = c.IsFixed();
                        entry["grounded"] = f;
                        if (f) grounded++;
                    }
                    catch { }
                }

                // ── DOVE STA L'ORIGINE, IN MM (parte di GA2) ────────────
                //
                // IL CAMPO SI CHIAMA `origin_mm` E NON `position_mm`, ED E'
                // LA RIPARAZIONE DI UN DIFETTO CHE NON DAVA ERRORE.
                //
                // Misurato il 17/9/2026: **`AddComponent5(x, y, z)` posiziona il
                // CENTRO dell'ingombro del componente, non la sua origine.** Un
                // pezzo estruso a una direzione sola — che e' quello che fa
                // `extrude_rectangle` — ha l'origine su una faccia, non a meta'
                // spessore: inserito a `(0, 0, 30)`, la sua origine finisce a
                // `(0, -2, 30)` se e' spesso 4, a `(0, -5, 0)` se e' spesso 10 e
                // lo si chiede a zero. Previsto prima di misurarlo, e misurato.
                //
                // Chiamare questo campo «position» invitava a confrontarlo con
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
                var location = ComponentPositionMm(c);
                if (location != null) entry["origin_mm"] = location;

                // Gli accoppiamenti: per ora il CONTEGGIO. Il formato di quello
                // che `GetMates()` restituisce e' `Object` e va accertato prima
                // di leggerne dentro (voce GA4); il numero invece si conta
                // subito, ed e' gia' qualcosa: zero accoppiamenti su un
                // componente non fisso vuol dire appoggiato.
                // `GetMates()` risponde **null quando sono zero**, non un array
                // vuoto: il conteggio passa da `SwOperations.MatesOf`,
                // che separa «zero» da «non ho potuto leggere» guardando se il
                // componente risponde ancora al proprio nome.
                int itsAcc = SwOperations.MatesOf(c);
                if (itsAcc >= 0) entry["mates"] = itsAcc;

                // ── CHE ACCOPPIAMENTI (voce Z-D3) ───────────────────────
                // Misurato il 27/9/2026 (`prova0089 zd345`): `GetMates` da' oggetti
                // IMate2 (e IFeature); `Type` e' `swMateType_e`. Si scrivono per
                // NOME, nell'ordine in cui SOLIDWORKS li da'. «Come e' montato
                // questo pezzo, incernierato o appoggiato?» si legge qui.
                // Zero accoppiamenti (contati sopra) e' un elenco vuoto; un GetMates
                // null con accoppiamenti contati, o illeggibili, e' «non lo so».
                if (itsAcc == 0) entry["mates_kinds"] = new string[0];
                else if (itsAcc > 0)
                {
                    var types = MateTypes(c);
                    entry["mates_kinds"] = types;   // null se non si leggono
                }

                // ── SOTTOASSIEME, VISIBILE, TRASPARENTE (voce Z-D4) ─────
                // Un sotto-assieme e' un blocco che ne nasconde altri: lo si dice.
                // `GetModelDoc2` di un soppresso e' null: «non lo so», non «no».
                try
                {
                    var its = c.GetModelDoc2();
                    entry["is_sub_assembly"] = its == null ? null : (object)(its is IAssemblyDoc);
                }
                catch { entry["is_sub_assembly"] = null; }
                // `Visible` e' swComponentVisibilityState_e: 1 visibile, 0 nascosto.
                try
                {
                    int vis = c.Visible;
                    entry["visible"] = vis == 1 ? (object)true : vis == 0 ? (object)false : null;
                }
                catch { entry["visible"] = null; }
                // LA TRASPARENZA NON E' UN CAMPO DEL COMPONENTE (accertato il
                // 23/9): sta in `MaterialPropertyValues`, posizione 7, e c'e' solo
                // se il componente ha un aspetto suo (misurato: 0,75 dopo
                // `SetComponentTransparent`). Trasparente e visibile sono due stati
                // diversi, non alternativi.
                try
                {
                    if (!c.HasMaterialPropertyValues()) entry["transparent"] = false;
                    else
                    {
                        var mpv = c.MaterialPropertyValues as double[];
                        entry["transparent"] = mpv != null && mpv.Length > 7 ? (object)(mpv[7] > 0) : null;
                    }
                }
                catch { entry["transparent"] = null; }

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
                var deg = SwOperations.DegreesOfFreedom(c);
                entry["degrees_of_freedom_state"] = deg.State;
                if (deg.Available) entry["degrees_of_freedom_remaining"] = deg.HowMany;
                else degreesReadFailed = true;

                // «E' APPOGGIATO?» SI RISPONDE CON DUE COSE CHE FUNZIONANO.
                //
                // I gradi di liberta' non si possono avere (vedi
                // `SwOperations.DegreesOfFreedom`). Quello che si sa per certo e':
                // un componente **non fisso** con **zero accoppiamenti** e'
                // appoggiato li'. Non si sa di quanto sia libero, e non lo si
                // dice: *un numero inventato e' peggio di un numero assente*.
                bool isFixed = entry.ContainsKey("grounded") &&
                              Convert.ToString(entry["grounded"]).Equals(
                                  "True", StringComparison.OrdinalIgnoreCase);
                int itsMates = itsAcc;
                // A NESTED COMPONENT IS NOT JUDGED HERE (L11-3): whether it is
                // fixed is not known from the root (null above), and it moves
                // with its sub-assembly, which is judged as one top-level
                // component. Counting it as «not fixed» would call the second
                // part of every rigid sub-assembly loose.
                if (!isFixed && topLevel != false)
                {
                    counted++;
                    if (itsMates == 0)
                    {
                        entry["resting"] = true;
                        resting++;
                        if (free.Count < 8) free.Add(entries[i].Key);
                    }
                    else if (itsMates > 0) entry["resting"] = false;
                }

                if (list.Count < MaxComponents) list.Add(entry);
            }

            data["components"] = list;
            if (entries.Count > MaxComponents)
                data["components_not_listed"] = entries.Count - MaxComponents;

            // I CONTEGGI SI SCRIVONO ANCHE QUANDO VALGONO ZERO.
            // «soppressi: 0» e' un'informazione; l'assenza del campo si legge
            // come «non l'ho guardato», e sono due cose diverse. E' la stessa
            // riga che il README difende per la regressione: *un conteggio che
            // sparisce quando e' zero e' un conteggio che nessuno controlla.*
            data["components_suppressed"] = suppressedOnes;
            data["components_lightweight"] = light;
            data["components_grounded"] = grounded;
            // How many components sit inside a sub-assembly (L11-3): they are
            // left out of the fixed count above and of the count below.
            // Written only when the top-level list was read, zero included.
            if (topLevelNames != null) data["nested_components"] = nested;

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
            if (counted > 0)
            {
                data["components_not_grounded"] = counted;
                data["components_resting"] = resting;
                data["assembly_constrained"] = resting == 0;
                if (free.Count > 0) data["components_still_free"] = free;

                data["verdict_constraints"] = resting == 0
                    ? "Tutti i " + counted + (counted == 1 ? " componente non fisso ha" : " componenti non fissi hanno") +
                      " almeno un accoppiamento. Di QUANTO siano ancora liberi non " +
                      "lo so: SOLIDWORKS non fornisce i gradi di liberta' residui da qui."
                    : "NON e' montato: " + resting + " su " + counted +
                      (counted == 1 ? " componente non fisso" : " componenti non fissi") +
                      " non ha nessun accoppiamento" +
                      (free.Count == 0 ? "" : " (" + string.Join(", ", free) + ")") +
                      ". Sono appoggiati li': si spostano se qualcuno li trascina, e " +
                      "l'assieme sembra montato senza esserlo.";
                if (nested > 0)
                    data["verdict_constraints"] += " The " + nested + " component(s) inside sub-assemblies are " +
                                                "not in this count: each moves with its sub-assembly, counted " +
                                                "as one top-level component.";
            }

            // LA COSA CHE NON SI SA, DETTA. Non e' un dettaglio tecnico: chi
            // legge «assembly_constrained: true» deve sapere che vuol dire «ogni
            // componente ha almeno un accoppiamento», NON «non si muove piu'».
            // One coincident mate leaves three of the six directions free (probe
            // zc14, 30/9/2026: TX, TZ and RY stay free), and this state does not
            // see them; «mate» measures them when it makes a mate (Z-C14).
            if (degreesReadFailed)
                data["what_is_not_known_about_constraints"] =
                    "SOLIDWORKS does not give the remaining degrees of freedom (it answers " +
                    "«unavailable» on every component, fixed or free). So «vincolato» here " +
                    "means «has at least one mate», not «does not move any more»: one " +
                    "coincident mate leaves three of the six directions free (measured). " +
                    "«mate» says which directions each mate blocks.";

            // ── L'ESPLOSO, E NON E' UN VEZZO (voce GA8) ──────────────────
            //
            // `IComponent2.GetTotalTransform(true)` **include la trasformazione
            // di esploso**. Se l'assieme e' mostrato esploso, una posizione
            // letta con `true` e' la posizione NELLA VISTA ESPLOSA: plausibile,
            // e sbagliata di dieci centimetri. Senza questo campo non c'e' modo
            // di accorgersene — ne' per chi legge, ne' per un caso di banco.
            //
            // Si scrive SEMPRE, anche quando e' falso: *un campo che compare
            // solo quando la cosa e' andata storta insegna a non cercarlo.*
            DescribeExplodedView(assemblyDoc, data);

            // ── LA CONFIGURAZIONE ATTIVA ─────────────────────────────────
            // Un assieme ne ha come una parte, e sbagliarla vuol dire misurare
            // una variante diversa da quella confermata.
            try
            {
                var doc = assemblyDoc as IModelDoc2;
                var cfg = doc == null ? null : doc.GetActiveConfiguration() as IConfiguration;
                if (cfg != null)
                {
                    data["configuration"] = cfg.Name;
                    DescribeBom(cfg, data);
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
            data["solid_body"] = false;
            data["what_cannot_be_done_yet"] =
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
        private static object[] ComponentPositionMm(IComponent2 c)
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
                    Math.Round(Units.Mm(d[9]), 3),
                    Math.Round(Units.Mm(d[10]), 3),
                    Math.Round(Units.Mm(d[11]), 3),
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
        private static void DescribeBom(IConfiguration cfg, Dictionary<string, object> data)
        {
            var d = new Dictionary<string, object>();

            // Ogni campo il suo try: su una configurazione vera basta che uno
            // non risponda perche' un try unico li faccia sparire tutti.
            try { d["part_number_source"] = SourceCodeName(cfg.BOMPartNoSource); } catch { }
            try { d["use_alternative_name"] = cfg.UseAlternateNameInBOM; } catch { }
            try
            {
                var n = cfg.AlternateName;
                if (!string.IsNullOrWhiteSpace(n)) d["alternative_name"] = n;
            }
            catch { }
            try { d["use_details_in_bom"] = cfg.UseDescriptionInBOM; } catch { }
            try { d["shows_children"] = cfg.ShowChildComponentsInBOM; } catch { }
            try { d["child_display_mode"] = cfg.ChildComponentDisplayInBOM; } catch { }

            if (d.Count > 0) data["bom"] = d;
        }

        /// <summary>
        /// Da dove viene il codice articolo in distinta, col nome.
        ///
        /// I valori sono di `swBOMPartNumberSource_e`. Come per la soppressione:
        /// quelli che non si riconoscono si scrivono col numero invece di
        /// essere tradotti a caso — **un nome inventato e' peggio di un numero
        /// onesto**, perche' chi legge il nome ci crede.
        /// </summary>
        private static string SourceCodeName(int source)
        {
            switch (source)
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
                           source.ToString(CultureInfo.InvariantCulture) + ")";
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
        /// <summary>I tipi degli accoppiamenti di un componente, per nome; null se non si leggono.</summary>
        private static string[] MateTypes(IComponent2 c)
        {
            try
            {
                var rawItems = c.GetMates() as object[];
                if (rawItems == null) return null;   // chi chiama sa gia' che ce ne sono: null e' «non lo so»
                var outside = new List<string>();
                foreach (var o in rawItems)
                {
                    var m = o as IMate2;
                    if (m == null) { outside.Add("?"); continue; }
                    outside.Add(TypeName(m.Type));
                }
                return outside.ToArray();
            }
            catch { return null; }
        }

        private static string TypeName(int t)
        {
            switch (t)
            {
                case (int)swMateType_e.swMateCOINCIDENT: return "coincidente";
                case (int)swMateType_e.swMateCONCENTRIC: return "concentrico";
                case (int)swMateType_e.swMatePERPENDICULAR: return "perpendicolare";
                case (int)swMateType_e.swMatePARALLEL: return "parallelo";
                case (int)swMateType_e.swMateTANGENT: return "tangente";
                case (int)swMateType_e.swMateDISTANCE: return "distanza";
                case (int)swMateType_e.swMateANGLE: return "angolo";
                case (int)swMateType_e.swMateSYMMETRIC: return "simmetrico";
                case (int)swMateType_e.swMateWIDTH: return "larghezza";
                case (int)swMateType_e.swMateLOCK: return "blocco";
                case (int)swMateType_e.swMateHINGE: return "cerniera";
                case (int)swMateType_e.swMateSLOT: return "asola";
                case (int)swMateType_e.swMateGEAR: return "ingranaggio";
                case (int)swMateType_e.swMateSCREW: return "vite";
                default:
                    var n = Enum.GetName(typeof(swMateType_e), t);
                    return n == null ? "tipo " + t : n.Replace("swMate", "").ToLowerInvariant();
            }
        }

        private static string StateName(int docState)
        {
            switch (docState)
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
                           docState.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        /// <summary>
        /// L'ESPLOSO: se c'e', se e' MOSTRATO, e quanto muove davvero (GA8).
        ///
        /// ── PERCHE' STA NELLO STATO E NON SOLO IN UN'OPERAZIONE ─────────
        ///
        /// `IComponent2.GetTotalTransform(true)` include la trasformazione di
        /// esploso. Un assieme mostrato esploso risponde, a quella chiamata,
        /// **la posizione nella vista esplosa**: plausibile e sbagliata di
        /// dieci centimetri, senza nessun errore. Chi legge lo stato deve
        /// poterlo sapere prima di credere a una posizione.
        ///
        /// ── E `exploded_view` DA SOLO NON BASTA ───────────────────────────────
        ///
        /// Misurato il 17/9/2026, quattro sonde: `CreateExplodedView()` risponde
        /// `True`, `IsExploded` diventa `si'`, e **niente si muove** — il passo
        /// che nasce si chiama «Catena1» e ha distanza 0 mm. Un esploso in cui
        /// nessun componente si sposta e' un esploso che non serve a niente, e
        /// `exploded_view: true` da solo lo farebbe sembrare fatto.
        ///
        /// Percio' si scrive anche **quanto si e' spostato il componente che si
        /// e' spostato di piu'**, in millimetri: e' la differenza fra le due
        /// trasformazioni, e zero vuol dire zero. *Un campo booleano che puo'
        /// essere vero mentre la cosa non e' successa non e' una misura: e' una
        /// promessa.*
        /// </summary>
        private static void DescribeExplodedView(
            IAssemblyDoc assemblyDoc, Dictionary<string, object> data)
        {
            var doc = assemblyDoc as IModelDoc2;
            if (doc == null) return;

            bool exploded = false;
            string which = null;
            try { exploded = doc.Extension.IsExploded(out which); }
            catch { return; }   // se non si puo' leggere non si inventa un falso

            data["exploded_view"] = exploded;
            if (exploded && !string.IsNullOrWhiteSpace(which)) data["view_exploded"] = which;

            try
            {
                var cfg = doc.ConfigurationManager.ActiveConfiguration as IConfiguration;
                if (cfg != null)
                {
                    int views = assemblyDoc.GetExplodedViewCount2(cfg.Name);
                    if (views > 0) data["views_exploded"] = views;
                    int steps = cfg.GetNumberOfExplodeSteps();
                    if (steps > 0) data["exploded_view_steps"] = steps;
                }
            }
            catch { }

            if (!exploded) return;

            // QUANTO MUOVE DAVVERO. Si guarda il massimo su tutti i componenti:
            // basta che UNO si sia spostato perche' una posizione letta con
            // `true` sia diversa da quella vera.
            double maxValue = 0;
            bool read = false;
            try
            {
                var all = assemblyDoc.GetComponents(true) as object[];
                if (all != null)
                    foreach (var o in all)
                    {
                        var c = o as IComponent2;
                        if (c == null) continue;
                        double d;
                        if (!ExplodedOffsetMm(c, out d)) continue;
                        read = true;
                        if (d > maxValue) maxValue = d;
                    }
            }
            catch { }

            if (!read) return;
            data["exploded_view_max_move_mm"] = Math.Round(maxValue, 3);
            if (maxValue < 0.001)
                data["what_is_not_known_about_exploded_view"] =
                    "La vista esplosa c'e' ed e' mostrata, ma NESSUN componente si " +
                    "sposta: lo scostamento massimo e' zero. Un esploso che non " +
                    "separa niente non serve a una tavola di montaggio. Su questa " +
                    "installazione non si e' trovato il modo di aggiungere passi di " +
                    "esploso da codice (voce GA8).";
        }

        /// <summary>
        /// Di quanto l'esploso sposta questo componente, in mm. `false` se non
        /// si e' potuto leggere — che **non** e' la stessa cosa di zero.
        /// </summary>
        private static bool ExplodedOffsetMm(IComponent2 c, out double mm)
        {
            mm = 0;
            try
            {
                var conE = c.GetTotalTransform(true) as IMathTransform;
                var without = c.GetTotalTransform(false) as IMathTransform;
                if (conE == null || without == null) return false;
                var a = conE.ArrayData as double[];
                var b = without.ArrayData as double[];
                if (a == null || b == null || a.Length < 12 || b.Length < 12) return false;
                double dx = a[9] - b[9], dy = a[10] - b[10], dz = a[11] - b[11];
                mm = Units.Mm(Math.Sqrt(dx * dx + dy * dy + dz * dz));
                return true;
            }
            catch { return false; }
        }
    }
}
