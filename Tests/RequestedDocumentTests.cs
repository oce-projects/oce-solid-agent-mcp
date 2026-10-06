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
    /// EVERY OPERATION SAYS WHICH DOCUMENT IT RUNS ON (entry Z-D0-ter).
    ///
    /// ── WHY THE FAMILY WAS NOT ENOUGH ────────────────────────────────────
    ///
    /// `Z-D0-bis` compared the FAMILIES of the catalogue with the document types
    /// that the executor admits, and it was a good check: it was born red and
    /// found a real hole. But the family is an approximation, and
    /// `checks` proves it by itself:
    ///
    ///     title_block              -> DRAWING
    ///     interference_check       -> ASSEMBLY
    ///     mass_properties          -> PART      ... and five more
    ///
    /// Three different documents under the same label. As long as it was not
    /// written, that fact lived in the head of whoever had written
    /// the operation and in a sentence of prose **that no check reads**.
    ///
    /// ── AND IT SERVES THE MODEL, NOT ONLY THE CHECK ──────────────────────
    ///
    /// An operation that declares which document it runs on is an operation that
    /// the model **does not try** on the wrong document. Trying it costs a
    /// turn, and the refusal that follows costs trust in the catalogue.
    /// </summary>
    internal static class RequestedDocumentTests
    {
        /// <summary>The values that the field can take, and nothing else. An
        /// open vocabulary becomes «part», «Part», «parts», «prt» in
        /// six months, and then no check can read it any more.</summary>
        private static readonly string[] Allowed =
            { "part", "assembly", "drawing", "any", "none" };

        internal static void All()
        {
            Group("every operation says what document it runs on (Z-D0-ter)");

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

            var withoutField = new List<string>();
            var outsideVocabulary = new List<string>();
            var perValue = new Dictionary<string, int>(StringComparer.Ordinal);
            var perFamily = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var v in entries)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n, d, f;
                o.TryGetValue("name", out n);
                o.TryGetValue("document", out d);
                o.TryGetValue("family", out f);
                string itemName = Convert.ToString(n);
                string doc = Convert.ToString(d);

                if (string.IsNullOrWhiteSpace(doc)) { withoutField.Add(itemName); continue; }

                foreach (var part in doc.Split('/'))
                {
                    if (!Allowed.Contains(part))
                        outsideVocabulary.Add(itemName + " -> «" + part + "»");
                }

                int q; perValue.TryGetValue(doc, out q);
                perValue[doc] = q + 1;

                string fam = Convert.ToString(f);
                HashSet<string> nameSet;
                if (!perFamily.TryGetValue(fam, out nameSet))
                    perFamily[fam] = nameSet = new HashSet<string>(StringComparer.Ordinal);
                nameSet.Add(doc);
            }

            Check("no operation stays silent about the document", withoutField.Count == 0,
                  withoutField.Count == 0
                      ? entries.Length + " operations, all declared"
                      : "silent: " + string.Join(", ", withoutField));

            Check("and none uses a word outside the vocabulary",
                  outsideVocabulary.Count == 0,
                  outsideVocabulary.Count == 0
                      ? "solo: " + string.Join(", ", Allowed)
                      : string.Join(" | ", outsideVocabulary));

            // ── THE PROOF THAT THE FIELD WAS NEEDED ──────────────────────
            //
            // If every family had a single document, this field would be
            // a duplicate of the family and should be removed. The check says
            // so instead of letting it be believed: the MIXED families are
            // the reason the entry exists.
            var mixed = perFamily.Where(x => x.Value.Count > 1)
                .Select(x => x.Key + " (" + string.Join(", ", x.Value.OrderBy(s => s)) + ")")
                .ToList();
            Check("the field says something that the family did not say",
                  mixed.Count > 0,
                  mixed.Count > 0
                      ? "families with more than one document: " + string.Join(" | ", mixed)
                      : "NO mixed family: the field is a duplicate, remove it");

            // ── AND WHAT IT DECLARES MUST BE EXECUTABLE ──────────────────
            //
            // The executor admits certain document types and nothing else (the same
            // reading that Z-D0-bis does, from the source and not from the text of the
            // csproj). An operation that declared a document that
            // the executor refuses would be an unreachable promise —
            // exactly the defect that Z-D0-bis found on the families.
            string executor = Path.Combine(Root(), "Oce.Solid.Agent.Core", "PlanExecutor.cs");
            if (!File.Exists(executor))
            {
                Check("the executor source is there", false, "missing " + executor);
                return;
            }
            var text = File.ReadAllText(executor);
            int start = text.IndexOf("bool allowed", StringComparison.Ordinal);
            string block = start < 0 ? "" : text.Substring(start, Math.Min(600, text.Length - start));

            var allowedByExecutor = new HashSet<string>(StringComparer.Ordinal);
            if (Regex.IsMatch(block, @"swDocPART")) allowedByExecutor.Add("part");
            if (Regex.IsMatch(block, @"swDocASSEMBLY")) allowedByExecutor.Add("assembly");
            if (Regex.IsMatch(block, @"swDocDRAWING")) allowedByExecutor.Add("drawing");

            Check("the accepted types are read from the source of the executor",
                  allowedByExecutor.Count > 0,
                  string.Join(", ", allowedByExecutor.OrderBy(s => s)));

            var unreachable = new List<string>();
            foreach (var pair in perValue)
            {
                foreach (var part in pair.Key.Split('/'))
                {
                    if (part == "any" || part == "none") continue;
                    if (!allowedByExecutor.Contains(part))
                        unreachable.Add(part + " (" + pair.Value + " operations)");
                }
            }
            Check("no operation promises a document that the executor refuses",
                  unreachable.Count == 0,
                  unreachable.Count == 0
                      ? "all the declared documents are executable"
                      : "PROMISED AND UNREACHABLE: " + string.Join(", ", unreachable.Distinct()));
        }
    }
}
