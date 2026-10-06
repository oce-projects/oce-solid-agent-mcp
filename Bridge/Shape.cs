using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Oce.Solid.Agent.Bridge
{
    /// <summary>
    /// DO THE ARGUMENTS RESPECT THE SCHEMA THAT THE CATALOG DECLARES?
    ///
    /// The bridge offers thirty-six operations with detailed schemas and then, up
    /// to 0.9.0, did not look at whether the arguments respected them: the schema
    /// served only to suggest. A forgotten `thickness_mm` reached `Num()`
    /// as zero and became an extrusion of zero thickness, which SOLIDWORKS
    /// refuses with a message of its own — in the best case.
    ///
    /// THE FORM IS CHECKED, NEVER THE MERIT. It is the line to keep in mind
    /// while reading this file, and the boundary is precise:
    ///
    ///   form  = is the mandatory parameter there? is it a number where a
    ///            number is needed? is it one of the allowed values?
    ///   merit = does that number make sense? is that center inside the material?
    ///
    /// The merit is not judged here, and it is not laziness: enlarging a hole has its
    /// center **exactly in the void**, and a zealous check would refuse it.
    /// Refusing what is merely unusual is the worst defect of all,
    /// because nobody reviews a wrong refusal whereas a wrong
    /// explanation is corrected in three seconds by whoever reads.
    ///
    /// AN INTEGER IS A NUMBER. The easiest defect to write here is a
    /// `type: number` that refuses the `20` of a JSON because it arrives as an integer:
    /// it would block operations that work today, and it would do so with a
    /// message that talks about types.
    /// </summary>
    internal static class Shape
    {
        /// <summary>
        /// Returns the reason for the refusal, or null if the arguments are fine.
        ///
        /// A schema that is not understood **is not a refusal**: if the catalog
        /// is written in a way that this check cannot read, the
        /// problem is the check's, and stopping the operation would mean making
        /// whoever draws pay for a defect of ours.
        ///
        /// IT ALSO REWRITES, IN <paramref name="arguments"/> ITSELF, every enum
        /// value spelled differently from the catalogue (Z-C21.10, pattern
        /// S20): «Top» is accepted and goes on as «top». The caller
        /// hands the same dictionary to the library, so whatever passes here
        /// reaches the library spelled as the catalogue spells it. See the
        /// enum block in <see cref="Fail"/> for why.
        /// </summary>
        public static string Check(
            string operation,
            Dictionary<string, object> schema,
            Dictionary<string, object> arguments)
        {
            if (schema == null) return null;
            var property = Take(schema, "properties") as Dictionary<string, object>;
            if (property == null) return null;

            arguments = arguments ?? new Dictionary<string, object>();

            // ── 1. what must be there ───────────────────────────────
            var missingItems = new List<string>();
            foreach (var itemName in Items(Take(schema, "required")))
            {
                object v;
                if (!arguments.TryGetValue(itemName, out v) || v == null) missingItems.Add(itemName);
            }
            if (missingItems.Count == 1)
                return "For «" + operation + "», «" + missingItems[0] + "» is needed, and it is missing." +
                       Suggest(property, missingItems[0]);
            if (missingItems.Count > 1)
                return "For «" + operation + "», " +
                       string.Join(", ", missingItems.Select(x => "«" + x + "»")) +
                       " are needed, and they are missing.";

            // ── 2. what is there, but of the wrong form ─────────────
            //
            // OVER A COPY OF THE ENTRIES, because an enum value can be
            // rewritten in `arguments` on the way (see Fail). On .NET
            // Framework even overwriting an existing key bumps the
            // dictionary's version, and the next MoveNext of a foreach over
            // that same dictionary throws InvalidOperationException.
            foreach (var entry in arguments.ToList())
            {
                // Keys that start with an underscore are not
                // parameters of the operation: they are our own stuff («_signatures»), and the
                // schema rightly does not name them.
                if (entry.Key.StartsWith("_")) continue;

                object raw;
                if (!property.TryGetValue(entry.Key, out raw)) continue;
                var rule = raw as Dictionary<string, object>;
                if (rule == null || entry.Value == null) continue;

                string catalogueSpelling;
                var crooked = Fail(operation, entry.Key, rule, entry.Value, 0, out catalogueSpelling);
                if (crooked != null) return crooked;
                if (catalogueSpelling != null) arguments[entry.Key] = catalogueSpelling;
            }

            return null;
        }

        /// <summary>
        /// How deep one goes into nested objects.
        ///
        /// The schemas of this catalog reach two levels
        /// (`feature_rif.passo`). Three is a margin, and the ceiling is there because a
        /// check that calls itself with no bottom is a way of getting stuck.
        /// </summary>
        private const int MaximumDepth = 3;

        /// <summary>
        /// Checks a value against its rule. It also goes down INTO
        /// nested objects, and up to 0.25.2 it did not.
        ///
        /// THE PROMISE THAT WAS NOT KEPT. The description of the tool
        /// `build` tells the model: «The plan is checked ENTIRELY before
        /// starting: if any step has a wrong name or arguments
        /// that do not respect the schema, nothing is executed and the
        /// document stays as it was». `PlanInMemory` repeats the same
        /// guarantee.
        ///
        /// But the check stopped at the first level: for `type: "object"`
        /// it verified that it was an object and nothing else, without looking at the `required`
        /// and the `properties` inside. The schema of `linear_pattern` declares
        /// `feature_ref` as an object with `required: ["step", "kind"]`, and a
        /// plan of four steps whose fourth writes `feature_ref: {}`
        /// passed the whole preventive validation. The first three steps were
        /// built in the document, then the fourth failed.
        ///
        /// The document was left half done. The all-or-nothing guarantee is
        /// exactly what this check exists for: keeping it only at the
        /// first level means not keeping it.
        ///
        /// <paramref name="catalogueSpelling"/> is not null only when the value
        /// is an enum written with a different case: it is the catalogue's
        /// spelling, and the caller writes it back in place of the value.
        /// Nested objects are rewritten inside <see cref="Nested"/>.
        /// </summary>
        private static string Fail(
            string operation, string itemName, Dictionary<string, object> rule,
            object val, int depth, out string catalogueSpelling)
        {
            catalogueSpelling = null;
            var typ = Convert.ToString(Take(rule, "type") ?? "");

            switch (typ)
            {
                case "number":
                case "integer":
                    if (!ANumber(val))
                        return "For «" + operation + "», «" + itemName + "» wants a number and " +
                               "instead it is " + WhatItIsCalled(val) + ".";
                    break;

                case "boolean":
                    if (!(val is bool))
                        return "For «" + operation + "», «" + itemName + "» wants true or false and " +
                               "instead it is " + WhatItIsCalled(val) + ".";
                    break;

                case "array":
                    if (val is string || !(val is IEnumerable))
                        return "For «" + operation + "», «" + itemName + "» wants a list and " +
                               "instead it is " + WhatItIsCalled(val) + ".";
                    break;

                case "object":
                    var inside = val as Dictionary<string, object>;
                    if (inside == null)
                        return "For «" + operation + "», «" + itemName + "» wants an object and " +
                               "instead it is " + WhatItIsCalled(val) + ".";
                    if (depth < MaximumDepth)
                    {
                        var crooked = Nested(operation, itemName, rule, inside, depth + 1);
                        if (crooked != null) return crooked;
                    }
                    break;

                case "string":
                    // A number where a string goes is NOT refused: whoever writes
                    // «configuration: 80» means «"80"», and refusing it would be
                    // pedantry that costs a round to whoever draws.
                    break;
            }

            // ── the allowed values ───────────────────────────────────
            //
            // THE VALUE GOES ON SPELLED AS THE CATALOGUE SPELLS IT (Z-C21.10,
            // pattern S20). The case of a word is form, not merit, so
            // «Top» is accepted here, as it always was. Until 0.140.0
            // it then went on to the library untouched, and the library
            // compares Ordinal: `SelectPlane` read «Top» as an
            // unknown name and fell back to the front plane (the box on the
            // wrong plane, riuscita:true, finding N33), and
            // `rectangular_frame` read «Overall» as not-«overall», that
            // is the centreline branch (a 1080x880 frame for 1000x800, N32).
            // Teaching every comparison in the library to ignore case would
            // leave the next one to be forgotten; rewriting the value here
            // covers them all, because this is the one door every step of
            // «build» comes through (Tools/McpTools.cs).
            //
            // An exact match wins. Otherwise the one value that differs only
            // in case. Two such values (a catalogue spelling one word two
            // ways) leave nothing to choose from, and that stays a rejection.
            var allowed = Items(Take(rule, "enum"));
            if (allowed.Count > 0)
            {
                var written = Convert.ToString(val);
                if (allowed.Contains(written, StringComparer.Ordinal)) return null;
                var sameWord = allowed
                    .Where(x => string.Equals(x, written, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (sameWord.Count == 1)
                {
                    catalogueSpelling = sameWord[0];
                    return null;
                }
                return "For «" + operation + "», «" + itemName + "» can only be " +
                       string.Join(", ", allowed.Select(x => "«" + x + "»")) +
                       ", and instead it says «" + written + "».";
            }

            return null;
        }

        /// <summary>
        /// Inside a nested object: first what is missing, then what is there
        /// of the wrong form. It is the same form as <see cref="Check"/>,
        /// one level further down.
        ///
        /// The name of the field is written in full — «feature_rif.passo» — or whoever
        /// reads the message does not know where to look: the same name «step» can
        /// be under three different references in the same schema.
        /// </summary>
        private static string Nested(
            string operation, string filePath, Dictionary<string, object> rule,
            Dictionary<string, object> val, int depth)
        {
            var property = Take(rule, "properties") as Dictionary<string, object>;
            if (property == null) return null;

            // ── what is missing ──
            var mandatory = Items(Take(rule, "required"));
            var missingItems = mandatory
                .Where(n => !val.ContainsKey(n) || val[n] == null)
                .ToList();

            if (missingItems.Count == 1)
                return "For «" + operation + "», «" + filePath + "» must also say «" +
                       missingItems[0] + "», and it does not.";
            if (missingItems.Count > 1)
                return "For «" + operation + "», «" + filePath + "» must also say " +
                       string.Join(", ", missingItems.Select(x => "«" + x + "»")) +
                       ", and it does not.";

            // ── what is there, but of the wrong form ──
            // Over a copy, as in Check: an enum value such as
            // «faccia_rif.genere» can be rewritten in `value_text` on the way.
            foreach (var entry in val.ToList())
            {
                if (entry.Key.StartsWith("_")) continue;
                object raw;
                if (!property.TryGetValue(entry.Key, out raw)) continue;
                var inside = raw as Dictionary<string, object>;
                if (inside == null || entry.Value == null) continue;

                string catalogueSpelling;
                var crooked = Fail(
                    operation, filePath + "." + entry.Key, inside, entry.Value, depth,
                    out catalogueSpelling);
                if (crooked != null) return crooked;
                if (catalogueSpelling != null) val[entry.Key] = catalogueSpelling;
            }

            return null;
        }

        /// <summary>
        /// A JSON integer is a number. The easiest defect to write in
        /// this file is a `type: number` that refuses the `20` of
        /// `"thickness_mm": 20` because the deserializer made it an `int`.
        /// </summary>
        private static bool ANumber(object v)
        {
            if (v is bool) return false;
            if (v is sbyte || v is byte || v is short || v is ushort ||
                v is int || v is uint || v is long || v is ulong ||
                v is float || v is double || v is decimal) return true;

            // Also a number written between quotes: whoever sends «"20"» said
            // twenty, and stopping it for the quotes would be refusing what was
            // understood perfectly well.
            var s = v as string;
            double _;
            return s != null &&
                   double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        private static string WhatItIsCalled(object v)
        {
            if (v == null) return "empty";
            if (v is bool) return "true/false";
            if (v is string) return "a text («" + v + "»)";
            if (v is Dictionary<string, object>) return "an object";
            if (v is IEnumerable) return "a list";
            return "a " + v.GetType().Name;
        }

        /// <summary>The description of the missing parameter, if the catalog has one.</summary>
        private static string Suggest(Dictionary<string, object> property, string itemName)
        {
            object raw;
            if (!property.TryGetValue(itemName, out raw)) return "";
            var rule = raw as Dictionary<string, object>;
            if (rule == null) return "";
            var d = Convert.ToString(Take(rule, "description") ?? "").Trim();
            if (string.IsNullOrEmpty(d)) return "";
            if (d.Length > 160) d = d.Substring(0, 160).TrimEnd() + "…";
            return " (" + d + ")";
        }

        private static List<string> Items(object raw)
        {
            var outside = new List<string>();
            var e = raw as IEnumerable;
            if (e == null || raw is string) return outside;
            foreach (var x in e)
            {
                var s = Convert.ToString(x);
                if (!string.IsNullOrWhiteSpace(s)) outside.Add(s);
            }
            return outside;
        }

        private static object Take(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }
    }
}
