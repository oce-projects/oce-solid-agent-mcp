using System.Collections.Generic;
using System.Linq;

using static Oce.Solid.Agent.Core.Catalog.Node;

namespace Oce.Solid.Agent.Core.Catalog
{
    /// <summary>
    /// From the C# catalog to `catalog.json`.
    ///
    /// The JSON is a PRODUCT, not a source: it is regenerated, not edited. Whoever
    /// wants to change a description touches `Operations.cs` and exports again.
    /// </summary>
    public static class Export
    {
        /// <summary>
        /// The catalog as the bridge reads it.
        ///
        /// **Only the proven operations.** An operation that compiles but has
        /// never been run on a real part does not exist, for the model: if
        /// it entered the catalog, the model would ask for it and would take an
        /// error for something that had been promised to it.
        ///
        /// **Only the families that have at least one operation.** A family
        /// declared and empty reaches the model as a door that opens onto
        /// nothing — and it is a defect that already happened once, in the
        /// previous Python exporter.
        /// </summary>
        public static Node Catalog()
        {
            var tested = Operations.All.Where(o => o.Tested).ToList();
            var used = new HashSet<string>(tested.Select(o => o.Family));

            var families = new List<object>();
            foreach (var f in Operations.Families)
            {
                if (!used.Contains(f.Key)) continue;
                families.Add(f.Key);
                families.Add(f.Value);
            }

            var list = new List<object>();
            foreach (var o in tested)
            {
                list.Add(O(
                    "name", o.Name,
                    "family", o.Family,
                    "summary", o.Summary,
                    "details", o.Description,
                    "schema", o.Schema,
                    "precondition", o.Precondition,
                    "document", o.RequestedDocument,
                    "example", o.Example));
            }

            return O(
                "families", O(families.ToArray()),
                "operations", A(list.ToArray()));
        }

        /// <summary>The catalog written, ready to be saved.</summary>
        public static string Json()
        {
            return Node.Json(Catalog());
        }

        /// <summary>
        /// The declared families that do NOT have even one proven operation.
        /// It is not an error — an operation may be in progress — but whoever
        /// exports must know it, because the normal case is that it is an
        /// oversight.
        /// </summary>
        public static List<string> FamiliesWithoutOperations()
        {
            var used = new HashSet<string>(
                Operations.All.Where(o => o.Tested).Select(o => o.Family));
            return Operations.Families
                .Select(f => f.Key)
                .Where(k => !used.Contains(k))
                .ToList();
        }

        /// <summary>
        /// The operations that the catalog knows but that are not proven.
        /// They stay out of the JSON, and are reported: a list that shortens
        /// silently goes unnoticed.
        /// </summary>
        public static List<string> NotTested()
        {
            return Operations.All.Where(o => !o.Tested).Select(o => o.Name).ToList();
        }
    }
}
