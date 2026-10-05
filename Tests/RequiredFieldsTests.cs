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
    /// EVERY REQUIRED FIELD IS A DECISION TAKEN, NOT AN OVERSIGHT
    /// (entry Z-G3).
    ///
    /// ── THE FACT IT IS BORN FROM ─────────────────────────────────────────
    ///
    /// The model did not lack the permission to skip the material: it lacked
    /// **a value**. Five rewritings of the rule changed
    /// nothing, because the problem was not the permission.
    ///
    /// A required field is either **a dimension of the part** — and then it is really
    /// asked for, because nobody can guess it — or it has **a workshop
    /// value**, and then it should be declared instead of being demanded.
    ///
    /// ── THE MEASUREMENT, on 23/9/2026 ────────────────────────────────────
    ///
    ///     92  required fields in the whole catalogue  (the entry said 91)
    ///      0  declare a default for them
    ///     58  are dimensions or references: they are asked for, and that is fine
    ///     34  are neither the one nor the other: A DECISION IS NEEDED
    ///
    /// ── AND THE DECISION IS WRITTEN HERE BELOW, ONE BY ONE ───────────────
    ///
    /// It is not bureaucracy: it is the difference between «nobody thought about it» and
    /// «somebody thought about it and decided to ask for it». The two things
    /// read the same in the JSON, and only one of the two can be improved.
    ///
    /// A new required field that is not a dimension turns this check
    /// red: the decision must be taken **while writing**, not six
    /// months later.
    /// </summary>
    internal static class RequiredFieldsTests
    {
        /// <summary>
        /// A field whose name already says that it is a dimension or a reference to
        /// something geometric. For these the decision is automatic: they are
        /// asked for.
        /// </summary>
        private static readonly Regex Dimension = new Regex(
            @"_mm$|_deg$|^teeth$|^module|^segments$|^point(?!s)|_uid$|^faces$",
            RegexOptions.Compiled);

        /// <summary>
        /// The required fields that are NOT dimensions, and why they stay
        /// required. The text is the decision, and it must be read: where there is
        /// «CANDIDATO» the decision has been taken and **has not yet been
        /// carried out**.
        /// </summary>
        private static readonly Dictionary<string, string> Decided =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // ── THEY ARE ASKED FOR: they are the work, not a side matter ──
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

                // ── THEY DEPEND ON THE WORKSHOP, NOT ON US (entry Z-G8) ──────
                { "structural_member.profile_path",
                  "la libreria dei profili e' di QUESTA officina: un predefinito " +
                  "nostro sarebbe il percorso della nostra macchina. Vedi Z-G8" },
                { "structural_member.configuration", "idem: la misura sta in quella libreria" },
                { "rectangular_frame.profile_path", "idem" },
                { "rectangular_frame.configuration", "idem" },

                // ── THE TWO CANDIDATES WERE DONE on 24/9/2026 ────────────────
                //
                // `apply_material.material` -> S235JR and
                // `combine_bodies.operation` -> «union» are no longer
                // required: they have a workshop default, and the
                // response declares having used it (`measurements.note_assumption`).
                //
                // Their lines have left here, and it is not a loss:
                // this list explains why a field IS ASKED FOR, and those
                // two are no longer asked for. A decision about a
                // field that does not exist is noise, and there is a check
                // for that — it triggered on these two lines
                // as soon as the catalogue was regenerated.
                //
                // What watches them now: the check at the bottom of this
                // file, which demands the `default` in the SERVED catalogue, plus
                // the four bench cases in pairs.
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

            // ── AND A DECISION FOR A FIELD THAT IS NO LONGER THERE IS NOISE ──
            //
            // Same reason as the list of exceptions in Z-C7: an expired permission
            // covers the day somebody rewrites that spot.
            var ghosts = Decided.Keys.Where(k => !foundItems.Contains(k)).ToList();
            Check("e nessuna decisione riguarda un campo che non esiste piu'",
                  ghosts.Count == 0,
                  ghosts.Count == 0
                      ? "nessuna decisione scaduta"
                      : "non esistono piu': " + string.Join(", ", ghosts));

            // ── THE CANDIDATES LEFT, if there are any ─────────────────────
            //
            // This counter demanded `> 0` because it counted PROMISED work,
            // and as long as there was some it served not to forget it. On
            // 24/9/2026 the two candidates were done, and a check that
            // demands the existence of work to do would become red
            // precisely when the work ends. Now it limits itself to telling them.
            var candidates = Decided.Where(x => x.Value.StartsWith("CANDIDATO", StringComparison.Ordinal))
                                  .Select(x => x.Key).ToList();
            Check("i campi che avrebbero un valore d'officina sono nominati", true,
                  candidates.Count == 0
                      ? "nessun candidato rimasto: i due di Z-G3 sono fatti"
                      : candidates.Count + " candidati, non ancora fatti: " +
                        string.Join(", ", candidates));

            // ── AND THE TWO FACTS ARE LOOKED AT IN THE SERVED CATALOGUE ───
            //
            // It is not enough that they have left `required`: a field removed and
            // that is all is a field that the bridge will accept empty without putting in
            // anything. The `default` in the schema is what ANNOUNCES it to the
            // model, and it is the half that can be checked from here —
            // the other half (that the value really be applied and
            // declared) is measured by the four bench cases.
            DeclaredDefault(entries, "apply_material", "material", "S235JR");
            DeclaredDefault(entries, "combine_bodies", "operation", "union");
        }

        /// <summary>
        /// The field has a `default` in the served catalogue, and it is that value,
        /// and it is NOT among the required any more.
        ///
        /// All three things together: a `default` next to a field that is
        /// still required is of no use to anybody, and a field removed from
        /// `required` without `default` is worse than before — the model does not
        /// know that it can omit it, and if it omits it it does not know what it receives.
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
