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
        /// «CANDIDATE» the decision has been taken and **has not yet been
        /// carried out**.
        /// </summary>
        private static readonly Dictionary<string, string> Decided =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // ── THEY ARE ASKED FOR: they are the work, not a side matter ──
                { "extrude_profile.contours", "it is the shape: without it there is nothing to extrude" },
                { "revolve.contours", "idem" },
                { "extrude_regions.regions", "the regions and their heights are the shape: without them there is nothing to extrude" },
                { "extrude_on_face.contours", "idem" },
                { "revolve.axis", "what it revolves around: a default would choose the part" },
                { "extrude_section.variant", "which section: IPE and square tube are not alike" },
                { "hole_wizard.size", "M8 or M10 is decided by the bolt, not by the workshop" },
                { "hole_wizard.positions", "where the holes go: it is the feature itself" },
                { "set_dimension.dimension", "which dimension to change: guessing it means changing the wrong part" },
                { "circular_pattern.instance_count", "how many: it is the point of the operation" },
                { "linear_pattern.instance_count1", "idem" },
                { "fill_pattern.copies_per_turn", "how many copies per turn: it is the density of the fill, there is no workshop number" },
                { "rib.plane", "the plane decides where the rib sits: a desk plane would put it crosswise, or outside the part" },
                { "mirror.plane", "with respect to what: a default would mirror at random" },
                { "note.text", "what is written: there is no desk text" },
                { "engrave_text.text", "idem" },
                { "activate_sheet.name", "which sheet: they are all legitimate" },
                { "move_view.view", "which view to move: a default would move one nobody asked for (report 8cd843 N54)" },
                { "delete_view.view", "which view to take away: a default would delete one nobody asked for (N54)" },
                { "cosmetic_thread.designation", "M10x1.5 is decided by the mating" },
                { "measure_between.uid", "between what and what it is measured" },
                { "suppress_instance.copies", "which copies to remove" },
                { "set_property.name", "which property" },
                { "set_property.value_text", "and what to write in it" },
                { "create_configuration.name", "what it is called: a desk name would be a duplicate" },
                { "activate_configuration.name", "which one to activate" },
                { "mate.component_a", "which two parts mate" },
                { "mate.face_a", "and on which faces" },
                { "mate.component_b", "idem" },
                { "mate.face_b", "idem" },
                { "replace_component.component", "which component is replaced" },
                { "replace_component.path", "and with which file" },
                { "insert_component.path", "which file is inserted" },
                { "base_flange.points", "the profile of the sheet metal: it is the sheet itself, and which side the thickness goes on depends on the direction" },
                { "surface_finish.ra_um", "the roughness is decided by the function of the surface: a desk Ra would be stamped on every face" },
                { "dimension_tolerance.dimension", "which dimension to tolerate only the designer knows: a desk tolerance on a dimension chosen by me is an invented fit" },
                { "dimension_tolerance.variant", "± or H7 is the question itself: there is no desk tolerance type" },
                { "fix_component.component", "which component to fix is known by whoever assembles: choosing it myself would be nailing down a random part" },
                { "fix_component.grounded", "fixing and freeing are two opposite things: there is no desk direction" },
                { "suppress_mate.mate", "which mate is wrong is known by whoever assembles: turning off a random one frees a part without saying which" },
                { "delete_mate.mate", "idem, and a deletion cannot be walked back" },
                { "place_component.component", "which part goes where is the assembler's decision: moving a random one rearranges the assembly without saying so" },
                { "global_variable.name", "the name is the very thing that is created: without it there is no variable" },
                { "link_dimension.dimension", "which dimension to link is known by the designer: linking a random one changes a part without saying so" },
                { "link_dimension.expression", "what to link it to is the question itself: there is no desk expression" },
                { "reorder_feature.placement", "before or after give two different parts: there is no desk direction" },
                { "sketch_picture.path", "the image to trace is the very thing: without it there is nothing to place" },
                { "mirror_component.component", "which part to mirror is known by whoever assembles: choosing it myself would be mirroring a random one" },
                { "mirror_component.plane", "the plane decides where the part goes: a desk plane puts it on the wrong side" },
                { "mirror_component.mode", "instance, linked or unlinked are three different parts, and getting it wrong gives no error: it is always asked" },
                { "sketch_picture.plane", "which plane the drawing is on is known by whoever looks at it: a desk plane would put it crosswise" },
                { "geometric_tolerance.characteristic", "flatness or perpendicularity is the question: there is no desk tolerance" },

                // ── THEY DEPEND ON THE WORKSHOP, NOT ON US (entry Z-G8) ──────
                { "structural_member.profile_path",
                  "the library of the profiles belongs to THIS workshop: a default " +
                  "of ours would be the path of our machine. See Z-G8" },
                { "structural_member.configuration", "likewise: the measurement is in that library" },
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
            Group("every mandatory field is a decision taken (Z-G3)");

            var file = ServedCatalog();
            if (!File.Exists(file))
            {
                Check("the served catalog is there", false, "missing " + file);
                return;
            }

            var root = new JavaScriptSerializer()
                .DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
            object raw;
            var entries = root != null && root.TryGetValue("operations", out raw)
                ? raw as object[] : null;
            if (entries == null)
            {
                Check("the catalog has the operations", false, "«operations» is not a list");
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

            Check("the mandatory fields are counted", mandatory > 0,
                  mandatory + " in all: " + quote + " dimensions or references (they are asked for), " +
                  (mandatory - quote) + " to decide");

            Check("every field that is not a dimension has a written decision",
                  withoutDecision.Count == 0,
                  withoutDecision.Count == 0
                      ? Decided.Count + " decisions, all in `RequiredFieldsTests.cs`"
                      : "WITHOUT A DECISION: " + string.Join(", ", withoutDecision));

            // ── AND A DECISION FOR A FIELD THAT IS NO LONGER THERE IS NOISE ──
            //
            // Same reason as the list of exceptions in Z-C7: an expired permission
            // covers the day somebody rewrites that spot.
            var ghosts = Decided.Keys.Where(k => !foundItems.Contains(k)).ToList();
            Check("and no decision concerns a field that no longer exists",
                  ghosts.Count == 0,
                  ghosts.Count == 0
                      ? "no expired decision"
                      : "no longer exist: " + string.Join(", ", ghosts));

            // ── THE CANDIDATES LEFT, if there are any ─────────────────────
            //
            // This counter demanded `> 0` because it counted PROMISED work,
            // and as long as there was some it served not to forget it. On
            // 24/9/2026 the two candidates were done, and a check that
            // demands the existence of work to do would become red
            // precisely when the work ends. Now it limits itself to telling them.
            var candidates = Decided.Where(x => x.Value.StartsWith("CANDIDATE", StringComparison.Ordinal))
                                  .Select(x => x.Key).ToList();
            Check("the fields that would have a workshop value are named", true,
                  candidates.Count == 0
                      ? "no candidate left: the two of Z-G3 are done"
                      : candidates.Count + " candidates, not yet done: " +
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

                Check(operation + "." + theField + " has its default, and is no longer mandatory",
                      Convert.ToString(d) == expected && !mandatoryAnchor,
                      "default=" + (d == null ? "(none)" : "«" + Convert.ToString(d) + "»") +
                      ", mandatory=" + mandatoryAnchor);
                return;
            }
            Check(operation + "." + theField + " has its default, and is no longer mandatory",
                  false, "the operation «" + operation + "» is not in the catalog");
        }
    }
}
