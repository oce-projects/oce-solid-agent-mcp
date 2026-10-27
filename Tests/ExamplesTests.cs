using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// AN EXAMPLE THAT THE CLIENT CANNOT COPY IS WORSE THAN NO EXAMPLE
    /// (entry Z-G1).
    ///
    /// ── WHY THE EXAMPLES ─────────────────────────────────────────────────
    ///
    /// A schema says which fields are lawful; **it does not say which combination
    /// builds a part**. They are two different things, and the second is learned
    /// only by seeing it.
    ///
    /// ── WHY THEY ARE NOT INVENTED ────────────────────────────────────────
    ///
    /// Every example is copied from a step that the bench has **executed and
    /// measured**. A hand-written example is a promise; one copied from a
    /// green case is a fact. The difference shows on the day when
    /// the operation changes and the example stays behind: the case turns red,
    /// the promise does not.
    ///
    /// ── AND THE TRAP THAT THIS GROUP WATCHES ──────────────────────────────
    ///
    /// On 23/9/2026, out of 71 operations, **47 did not have a copyable example**:
    /// their cases use selectors that live only in the bench —
    /// `@face(normal=up)`, `@edge(axis=Z)` — or the placeholders `{WORK}` and
    /// `{PROFILES}`, which the bench replaces and a client does not.
    ///
    /// Copying them all the same would have produced twenty-four good examples and
    /// forty-seven **traps**: a model that copies `@face(normal=up)`
    /// gets a refusal for a reason it does not understand, and next time it
    /// no longer trusts the catalogue. That is why the coverage is 24/71 and not 71/71,
    /// and that is why this group watches it on every run.
    /// </summary>
    internal static class ExamplesTests
    {
        /// <summary>
        /// How many operations have an example. Twenty-four on 23/9/2026, and it
        /// **can only go up**: it is raised by writing a bench case that uses
        /// client arguments instead of bench selectors — that is,
        /// by improving the check, not the example. **25** on 27/9/2026: `fix_component`
        /// is born with the example copied from its case (`fixed_is_not_mated`).
        /// **26**: `global_variable`, from its case (`wall_has_a_name`).
        /// **27**: `reorder_feature` (`order_changes_the_part`).
        /// **28**: `mirror_component` (`right_bracket_and_left_bracket`).
        /// **29**: `edit_extrusion` (`blind_becomes_through`).
        /// **30**: `rib` (`rib_in_the_corner_of_the_l`), entry W1.2. The
        /// other three features of the guide (`fillet_face`,
        /// `fill_pattern`, `gusset`) are born without one: their green cases
        /// name the faces with the bench selectors.
        /// **32** (26/10/2026, brief R22): `suppress_mate` and `delete_mate`
        /// (`a_wrong_mate_is_taken_out`). **33** (same night, brief R12):
        /// `place_component` (`a_part_is_turned_over_and_placed`).
        /// **34** (27/10/2026, brief §5.1): `parameters`
        /// (`variables_and_links_in_one_step`).
        /// </summary>
        private const int Floor = 34;

        /// <summary>What a client cannot copy.</summary>
        private static readonly string[] BenchOnly = { "@", "{WORK}", "{PROFILES}", "{DATA}", "{SOLIDWORKS}" };

        internal static void All()
        {
            Group("the examples are copied from a green case (Z-G1)");

            var file = ServedCatalog();
            if (!File.Exists(file))
            {
                Check("the served catalog is there", false, "missing " + file);
                return;
            }

            var s = new JavaScriptSerializer();
            var root = s.DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
            object raw;
            var entries = root != null && root.TryGetValue("operations", out raw)
                ? raw as object[] : null;
            if (entries == null)
            {
                Check("the catalog has the operations", false, "«operations» is not a list");
                return;
            }

            int withExample = 0;
            var unreadable = new List<string>();
            var contaminated = new List<string>();
            var outsideSchema = new List<string>();

            foreach (var v in entries)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n, e, sc;
                o.TryGetValue("name", out n);
                o.TryGetValue("example", out e);
                o.TryGetValue("schema", out sc);
                string itemName = Convert.ToString(n);
                string example = Convert.ToString(e);
                if (string.IsNullOrWhiteSpace(example)) continue;
                withExample++;

                // ── IS IT JSON? ───────────────────────────────────────────
                Dictionary<string, object> arguments = null;
                try { arguments = s.DeserializeObject(example) as Dictionary<string, object>; }
                catch (Exception ex) { unreadable.Add(itemName + ": " + ex.Message); continue; }
                if (arguments == null) { unreadable.Add(itemName + ": is not an object"); continue; }

                // ── IS IT COPYABLE? ───────────────────────────────────────
                foreach (var bad in BenchOnly)
                    if (example.Contains(bad))
                        contaminated.Add(itemName + " contains «" + bad + "»");

                // ── AND DO THE FIELDS EXIST IN THE SCHEMA? ────────────────
                //
                // An example that names a field that the schema does not have is an
                // aged example: the operation has changed and it has not. It is
                // the most likely defect of this entry, because nobody
                // executes the example.
                var schema = sc as Dictionary<string, object>;
                object pg;
                var props = schema != null && schema.TryGetValue("properties", out pg)
                    ? pg as Dictionary<string, object> : null;
                if (props == null) continue;
                foreach (var theField in arguments.Keys)
                    if (!props.ContainsKey(theField))
                        outsideSchema.Add(itemName + "." + theField);
            }

            Check("the examples have not decreased", withExample >= Floor,
                  withExample + " operations out of " + entries.Length +
                  " have an example (floor " + Floor + ")");

            Check("and the floor is the real one", withExample == Floor,
                  withExample == Floor
                      ? "twenty-four, as measured"
                      : "there are " + withExample + ": raise the floor to this number");

            Check("every example is readable JSON", unreadable.Count == 0,
                  unreadable.Count == 0 ? "all" : string.Join(" | ", unreadable));

            // THIS IS THE CHECK THAT COUNTS.
            Check("no example uses something that lives only in the bench",
                  contaminated.Count == 0,
                  contaminated.Count == 0
                      ? "no @ selectors, no placeholders {WORK}/{PROFILES}/{DATA}/{SOLIDWORKS}"
                      : string.Join(" | ", contaminated));

            Check("and every field of the example exists in the schema",
                  outsideSchema.Count == 0,
                  outsideSchema.Count == 0
                      ? "no outdated example"
                      : "fields that the schema does not have: " + string.Join(", ", outsideSchema));
        }
    }
}
