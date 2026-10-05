using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// OGNI CAMPO OBBLIGATORIO E' UNA DECISIONE PRESA, NON UNA DIMENTICANZA
    /// (voce Z-G3).
    ///
    /// ── IL FATTO DA CUI NASCE ────────────────────────────────────────────
    ///
    /// Al modello non mancava il permesso di saltare il materiale: gli mancava
    /// **un valore**. Cinque riscritture della regola non hanno cambiato
    /// niente, perche' il problema non era il permesso.
    ///
    /// Un campo obbligatorio o e' **una quota del pezzo** — e allora si chiede
    /// davvero, perche' nessuno la puo' indovinare — oppure ha **un valore
    /// d'officina**, e allora andrebbe dichiarato invece di essere preteso.
    ///
    /// ── LA MISURA, il 23/9/2026 ──────────────────────────────────────────
    ///
    ///     92  campi obbligatori in tutto il catalogo  (la voce diceva 91)
    ///      0  ne dichiarano un predefinito
    ///     58  sono quote o riferimenti: si chiedono, e va bene cosi'
    ///     34  non sono ne' l'uno ne' l'altro: SERVE UNA DECISIONE
    ///
    /// ── E LA DECISIONE STA SCRITTA QUI SOTTO, UNA PER UNA ────────────────
    ///
    /// Non e' burocrazia: e' la differenza fra «nessuno ci ha pensato» e
    /// «qualcuno ci ha pensato e ha deciso di chiederlo». Le due cose si
    /// leggono uguali nel JSON, e solo una delle due si puo' migliorare.
    ///
    /// Un campo obbligatorio nuovo che non sia una quota fa diventare rosso
    /// questo controllo: la decisione va presa **mentre si scrive**, non sei
    /// mesi dopo.
    /// </summary>
    internal static class RequiredFieldsTests
    {
        /// <summary>
        /// Un campo il cui nome dice gia' che e' una quota o un riferimento a
        /// qualcosa di geometrico. Per questi la decisione e' automatica: si
        /// chiedono.
        /// </summary>
        private static readonly Regex Dimension = new Regex(
            @"_mm$|_deg$|^teeth$|^module|^segments$|^point(?!s)|_uid$|^faces$",
            RegexOptions.Compiled);

        /// <summary>
        /// I campi obbligatori che NON sono quote, e perche' restano
        /// obbligatori. Il testo e' la decisione, e va letto: dove c'e'
        /// «CANDIDATO» la decisione e' stata presa e **non e' ancora
        /// eseguita**.
        /// </summary>
        private static readonly Dictionary<string, string> Decided =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // ── SI CHIEDONO: sono il lavoro, non un contorno ──────────
                { "extrude_profile.contours", "e' la sagoma: senza, non c'e' niente da estrudere" },
                { "revolve.contours", "idem" },
                { "extrude_on_face.contours", "idem" },
                { "revolve.axis", "attorno a cosa gira: un predefinito sceglierebbe il pezzo" },
                { "extrude_section.variant", "quale profilato: IPE e tubo quadro non si somigliano" },
                { "hole_wizard.size", "M8 o M10 lo decide il bullone, non l'officina" },
                { "hole_wizard.positions", "dove vanno i fori: e' la lavorazione stessa" },
                { "set_dimension.dimension", "quale quota cambiare: indovinarla e' cambiare il pezzo sbagliato" },
                { "circular_pattern.instance_count", "quante: e' il senso dell'operazione" },
                { "linear_pattern.instance_count1", "idem" },
                { "fill_pattern.copies_per_turn", "quante copie per giro: e' la densita' del riempimento, non c'e' un numero d'officina" },
                { "rib.plane", "il piano decide dove sta la nervatura: un piano d'ufficio la metterebbe di traverso, o fuori dal pezzo" },
                { "mirror.plane", "rispetto a cosa: un predefinito specchierebbe a caso" },
                { "note.text", "cosa c'e' scritto: non esiste un testo d'ufficio" },
                { "engrave_text.text", "idem" },
                { "activate_sheet.name", "quale foglio: sono tutti legittimi" },
                { "cosmetic_thread.designation", "M10x1.5 lo decide l'accoppiamento" },
                { "measure_between.uid", "fra cosa e cosa si misura" },
                { "suppress_instance.copies", "quali copie togliere" },
                { "set_property.name", "quale proprieta'" },
                { "set_property.value_text", "e cosa scriverci" },
                { "create_configuration.name", "come si chiama: un nome d'ufficio sarebbe un doppione" },
                { "activate_configuration.name", "quale attivare" },
                { "mate.component_a", "quali due pezzi si accoppiano" },
                { "mate.face_a", "e su quali facce" },
                { "mate.component_b", "idem" },
                { "mate.face_b", "idem" },
                { "replace_component.component", "quale componente si sostituisce" },
                { "replace_component.path", "e con quale file" },
                { "insert_component.path", "quale file si inserisce" },
                { "base_flange.points", "il profilo della lamiera: e' la lamiera stessa, e da che parte va lo spessore dipende dal verso" },
                { "surface_finish.ra_um", "la rugosita' la decide la funzione della superficie: un Ra d'ufficio si stamperebbe su ogni faccia" },
                { "dimension_tolerance.dimension", "quale quota tollerare lo sa solo chi progetta: una tolleranza d'ufficio su una quota scelta da me e' un accoppiamento inventato" },
                { "dimension_tolerance.variant", "± o H7 e' la domanda stessa: non c'e' un tipo di tolleranza d'ufficio" },
                { "fix_component.component", "quale componente fissare lo sa chi monta: sceglierlo io sarebbe inchiodare un pezzo a caso" },
                { "fix_component.grounded", "fissare e liberare sono due cose opposte: non c'e' un verso d'ufficio" },
                { "global_variable.name", "il nome e' la cosa stessa che si crea: senza, non c'e' una variabile" },
                { "link_dimension.dimension", "quale quota legare lo sa chi progetta: legarne una a caso cambia un pezzo senza dirlo" },
                { "link_dimension.expression", "a cosa legarla e' la domanda stessa: non c'e' un'espressione d'ufficio" },
                { "reorder_feature.placement", "prima o dopo danno due pezzi diversi: non c'e' un verso d'ufficio" },
                { "sketch_picture.path", "l'immagine da ricalcare e' la cosa stessa: senza, non c'e' niente da mettere" },
                { "mirror_component.component", "quale pezzo specchiare lo sa chi monta: sceglierlo io sarebbe specchiarne uno a caso" },
                { "mirror_component.plane", "il piano decide dove va il pezzo: un piano d'ufficio lo mette dalla parte sbagliata" },
                { "mirror_component.mode", "istanza, legato o slegato sono tre pezzi diversi, e sbagliare non da' errore: si chiede sempre" },
                { "sketch_picture.plane", "su quale piano sta il disegno lo sa chi lo guarda: un piano d'ufficio lo metterebbe di traverso" },
                { "geometric_tolerance.characteristic", "planarita' o perpendicolarita' e' la domanda: non c'e' una tolleranza d'ufficio" },

                // ── DIPENDONO DALL'OFFICINA, NON DA NOI (voce Z-G8) ───────
                { "structural_member.profile_path",
                  "la libreria dei profili e' di QUESTA officina: un predefinito " +
                  "nostro sarebbe il percorso della nostra macchina. Vedi Z-G8" },
                { "structural_member.configuration", "idem: la misura sta in quella libreria" },
                { "rectangular_frame.profile_path", "idem" },
                { "rectangular_frame.configuration", "idem" },

                // ── I DUE CANDIDATI SONO STATI FATTI il 24/9/2026 ─────────
                //
                // `apply_material.material` -> S235JR e
                // `combine_bodies.operation` -> «union» non sono piu'
                // obbligatori: hanno un predefinito d'officina, e la
                // risposta dichiara di averlo usato (`measurements.note_assumption`).
                //
                // Le loro righe sono uscite da qui, e non e' una perdita:
                // questo elenco spiega perche' un campo SI CHIEDE, e quei
                // due non si chiedono piu'. Una decisione che riguarda un
                // campo che non esiste e' rumore, e c'e' un controllo
                // apposta che lo dice — e' scattato su queste due righe
                // appena il catalogo e' stato rigenerato.
                //
                // Cosa li sorveglia adesso: il controllo in fondo a questo
                // file, che pretende il `default` nel catalogo SERVITO, piu'
                // i quattro casi di banco a coppie.
            };

        internal static void All()
        {
            Group("ogni campo obbligatorio e' una decisione presa (Z-G3)");

            var file = ServedCatalog();
            if (!File.Exists(file))
            {
                Check("il catalogo servito c'e'", false, "manca " + file);
                return;
            }

            var root = new JavaScriptSerializer()
                .DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
            object raw;
            var entries = root != null && root.TryGetValue("operations", out raw)
                ? raw as object[] : null;
            if (entries == null)
            {
                Check("il catalogo ha le operazioni", false, "«operations» non e' un elenco");
                return;
            }

            int mandatory = 0, quote = 0;
            var withoutDecision = new List<string>();
            var foundItems = new HashSet<string>(StringComparer.Ordinal);

            foreach (var v in entries)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n, s;
                o.TryGetValue("name", out n);
                o.TryGetValue("schema", out s);
                var schema = s as Dictionary<string, object>;
                if (schema == null) continue;
                object r;
                var requested = schema.TryGetValue("required", out r) ? r as object[] : null;
                if (requested == null) continue;

                foreach (var theField in requested)
                {
                    string itemName = Convert.ToString(n) + "." + Convert.ToString(theField);
                    mandatory++;
                    if (Dimension.IsMatch(Convert.ToString(theField))) { quote++; continue; }
                    foundItems.Add(itemName);
                    if (!Decided.ContainsKey(itemName)) withoutDecision.Add(itemName);
                }
            }

            Check("i campi obbligatori si contano", mandatory > 0,
                  mandatory + " in tutto: " + quote + " quote o riferimenti (si chiedono), " +
                  (mandatory - quote) + " da decidere");

            Check("ogni campo che non e' una quota ha una decisione scritta",
                  withoutDecision.Count == 0,
                  withoutDecision.Count == 0
                      ? Decided.Count + " decisioni, tutte in `RequiredFieldsTests.cs`"
                      : "SENZA DECISIONE: " + string.Join(", ", withoutDecision));

            // ── E UNA DECISIONE PER UN CAMPO CHE NON C'E' PIU' E' RUMORE ──
            //
            // Stessa ragione dell'elenco delle eccezioni in Z-C7: un permesso
            // scaduto copre il giorno in cui qualcuno riscrive quel punto.
            var ghosts = Decided.Keys.Where(k => !foundItems.Contains(k)).ToList();
            Check("e nessuna decisione riguarda un campo che non esiste piu'",
                  ghosts.Count == 0,
                  ghosts.Count == 0
                      ? "nessuna decisione scaduta"
                      : "non esistono piu': " + string.Join(", ", ghosts));

            // ── I CANDIDATI RIMASTI, se ce ne sono ───────────────────────
            //
            // Questo contatore pretendeva `> 0` perche' contava lavoro
            // PROMESSO, e finche' ce n'era serviva a non dimenticarlo. Il
            // 24/9/2026 i due candidati sono stati fatti, e un controllo che
            // pretende l'esistenza di lavoro da fare diventerebbe rosso
            // proprio quando il lavoro finisce. Adesso si limita a dirli.
            var candidates = Decided.Where(x => x.Value.StartsWith("CANDIDATO", StringComparison.Ordinal))
                                  .Select(x => x.Key).ToList();
            Check("i campi che avrebbero un valore d'officina sono nominati", true,
                  candidates.Count == 0
                      ? "nessun candidato rimasto: i due di Z-G3 sono fatti"
                      : candidates.Count + " candidati, non ancora fatti: " +
                        string.Join(", ", candidates));

            // ── E I DUE FATTI SI GUARDANO NEL CATALOGO SERVITO ───────────
            //
            // Non basta che siano usciti da `required`: un campo tolto e
            // basta e' un campo che il ponte accettera' vuoto senza mettere
            // niente. Il `default` nello schema e' cio' che lo ANNUNCIA al
            // modello, ed e' la meta' che si puo' controllare da qui —
            // l'altra meta' (che il valore venga applicato davvero e
            // dichiarato) la misurano i quattro casi di banco.
            DeclaredDefault(entries, "apply_material", "material", "S235JR");
            DeclaredDefault(entries, "combine_bodies", "operation", "union");
        }

        /// <summary>
        /// Il campo ha un `default` nel catalogo servito, ed e' quel valore,
        /// e NON e' piu' fra gli obbligatori.
        ///
        /// Tutte e tre le cose insieme: un `default` accanto a un campo
        /// ancora obbligatorio non serve a nessuno, e un campo tolto da
        /// `required` senza `default` e' peggio di prima — il modello non
        /// sa che puo' ometterlo, e se lo omette non sa cosa riceve.
        /// </summary>
        private static void DeclaredDefault(
            object[] entries, string operation, string theField, string expected)
        {
            foreach (var v in entries)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n;
                o.TryGetValue("name", out n);
                if (Convert.ToString(n) != operation) continue;

                object s;
                o.TryGetValue("schema", out s);
                var schema = s as Dictionary<string, object>;
                if (schema == null) break;

                object p;
                schema.TryGetValue("properties", out p);
                var property = p as Dictionary<string, object>;
                object c = null;
                if (property != null) property.TryGetValue(theField, out c);
                var its = c as Dictionary<string, object>;

                object d = null;
                if (its != null) its.TryGetValue("default", out d);

                object r;
                var requested = schema.TryGetValue("required", out r) ? r as object[] : null;
                bool mandatoryAnchor = requested != null &&
                    requested.Any(x => Convert.ToString(x) == theField);

                Check(operation + "." + theField + " ha il suo predefinito, e non e' piu' obbligatorio",
                      Convert.ToString(d) == expected && !mandatoryAnchor,
                      "default=" + (d == null ? "(nessuno)" : "«" + Convert.ToString(d) + "»") +
                      ", obbligatorio=" + mandatoryAnchor);
                return;
            }
            Check(operation + "." + theField + " ha il suo predefinito, e non e' piu' obbligatorio",
                  false, "l'operazione «" + operation + "» non e' in catalogo");
        }
    }
}
