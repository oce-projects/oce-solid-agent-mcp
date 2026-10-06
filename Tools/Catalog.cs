using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Oce.Solid.Agent.Tools
{
    /// <summary>
    /// The catalogue of operations, read from `catalog.json`.
    ///
    /// A SINGLE SOURCE OF TRUTH, AND AN HONEST COPY.
    /// The real catalogue lives in the server, in `ai_disegno.py`: there are
    /// the descriptions written to be read by a model, the schemas, the
    /// limits, and the `provata` field that decides what exists. This bridge
    /// is written in C# and cannot import Python, so it carries along a
    /// copy — regenerated with one command (`Tools/update.md`), never
    /// written by hand.
    ///
    /// THE FILE HOLDS ONLY THE PROVEN OPERATIONS. It is the rule of the
    /// two-level catalogue: an operation that has not been built inside
    /// SolidWorks and measured is offered to nobody, not even from here.
    /// </summary>
    internal sealed class Catalog
    {
        private readonly List<Dictionary<string, object>> _operations =
            new List<Dictionary<string, object>>();
        private Dictionary<string, object> _families = new Dictionary<string, object>();

        public string FilePath { get; private set; }
        public int HowMany => _operations.Count;
        public string LoadingError { get; private set; }

        public Catalog()
        {
            // ── TWO NAMES, AND ONLY ONE IS THIS PRODUCT'S ───────────────
            //
            // `RISCONTRO_CATALOGO` is the new name. `OPENPDM_CATALOGO` was the
            // old one, and it **is still read**: whoever set it on an installed
            // machine must not notice anything, and a variable that stops
            // working without saying so is the defect that gives no error —
            // the catalogue does not load, the bridge runs all the same, and
            // nobody can tell any more what can be asked for.
            //
            // Order matters: if both are set, the new one wins.
            // Whoever sets two different ones already has a problem, and at least
            // knows which of the two is in charge.
            FilePath = Environment.GetEnvironmentVariable("RISCONTRO_CATALOGO");
            if (string.IsNullOrWhiteSpace(FilePath))
                FilePath = Environment.GetEnvironmentVariable("OPENPDM_CATALOGO");
            if (string.IsNullOrWhiteSpace(FilePath))
                FilePath = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
                    "catalog.json");

            if (!File.Exists(FilePath))
            {
                LoadingError =
                    "I cannot find the catalog in «" + FilePath + "». Without it, the bridge " +
                    "can run operations but cannot say what can be asked for.";
                return;
            }

            try
            {
                var root = Mcp.Protocol.FromJson(File.ReadAllText(FilePath));
                _families = Take(root, "families") as Dictionary<string, object>
                            ?? new Dictionary<string, object>();
                var list = Take(root, "operations") as object[];
                if (list != null)
                    foreach (var o in list)
                        if (o is Dictionary<string, object> entry) _operations.Add(entry);
            }
            catch (Exception ex)
            {
                LoadingError = "The catalog cannot be read: " + ex.Message;
            }
        }

        private static object Take(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            return d.TryGetValue(key, out var v) ? v : null;
        }

        public bool Knows(string itemName) => Find(itemName) != null;

        /// <summary>
        /// The raw schema of an operation, for whoever has to compare its
        /// arguments against it. `Description` packs it for the model; here
        /// it is needed bare.
        /// </summary>
        public Dictionary<string, object> Schema(string itemName)
        {
            var op = Find(itemName);
            return op == null ? null : Take(op, "schema") as Dictionary<string, object>;
        }

        public Dictionary<string, object> Find(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return null;
            return _operations.FirstOrDefault(o =>
                string.Equals(Convert.ToString(Take(o, "name")), itemName,
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The family of an operation, or `null` if the operation does not exist.
        ///
        /// It serves the `measure` tool, which must be able to say NO to
        /// an operation that touches the part — and to say it, it must know
        /// which family it belongs to. It is a NEW METHOD and not one more
        /// parameter on `Find`: the bench and the checks call this class by
        /// reflection, and changing the set of signatures breaks the callers
        /// even when the compiler stays silent.
        /// </summary>
        public string Family(string itemName)
        {
            var op = Find(itemName);
            return op == null ? null : Convert.ToString(Take(op, "family"));
        }

        public IEnumerable<string> Names =>
            _operations.Select(o => Convert.ToString(Take(o, "name")));

        /// <summary>
        /// The short list: name and one line, grouped by family.
        ///
        /// The whole schemas are not sent: they are tens of thousands of
        /// characters, and the reader uses two or three. The schema is asked
        /// for when needed, with `describe_operation` — it is the two-level
        /// catalogue, which holds here for the same reason it holds in the server.
        /// </summary>
        public object Items(string wantedFamily)
        {
            var perFamily = new List<object>();
            var families = _operations
                .Select(o => Convert.ToString(Take(o, "family")))
                .Distinct()
                .OrderBy(f => f, StringComparer.Ordinal);

            foreach (var f in families)
            {
                if (!string.IsNullOrWhiteSpace(wantedFamily) &&
                    !string.Equals(f, wantedFamily, StringComparison.OrdinalIgnoreCase))
                    continue;

                var entries = _operations
                    .Where(o => Convert.ToString(Take(o, "family")) == f)
                    .Select(o => new Dictionary<string, object>
                    {
                        ["name"] = Take(o, "name"),
                        ["does"] = Take(o, "summary"),
                    })
                    .ToArray();

                perFamily.Add(new Dictionary<string, object>
                {
                    ["family"] = f,
                    ["purpose"] = Take(_families, f),
                    ["operations"] = entries,
                });
            }

            return new Dictionary<string, object>
            {
                ["total"] = _operations.Count,
                ["families"] = perFamily,
                ["how_to_use_it"] =
                    "Ask «describe_operation» for the full schema " +
                    "of the one you need, then «build».",
            };
        }

        public object Description(string itemName)
        {
            var op = Find(itemName);
            if (op == null) return null;
            return new Dictionary<string, object>
            {
                ["name"] = Take(op, "name"),
                ["family"] = Take(op, "family"),
                ["precondition"] = Take(op, "precondition"),
                ["details"] = Take(op, "details"),
                ["arguments"] = Take(op, "schema"),
            };
        }

        /// <summary>
        /// Similar names, for an error that helps instead of merely
        /// stating the fact.
        ///
        /// THE ROOTS ARE COMPARED, not the words. The first attempt
        /// looked for the whole word inside the name, and for «asole» it
        /// did not find «slot» — that is, it failed in the very case it
        /// had been written for: whoever gets a name wrong almost always has
        /// a nearby one in mind, and is off by one letter.
        /// </summary>
        public List<string> Similar(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return new List<string>();
            var parts = itemName.ToLowerInvariant().Split('_')
                .Where(p => p.Length >= 4)
                .Select(p => p.Substring(0, Math.Min(p.Length - 1, 6)))
                .ToList();
            if (parts.Count == 0) return new List<string>();
            return Names
                .Where(n => parts.Any(p => n.ToLowerInvariant().Contains(p)))
                .Take(5)
                .ToList();
        }
    }
}
