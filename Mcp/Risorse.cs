using System;
using System.Collections.Generic;
using Oce.Solid.Agent.Tools;

namespace Oce.Solid.Agent.Mcp
{
    /// <summary>
    /// THE CATALOG AS A **RESOURCE**, not only as a tool (entry W2.12).
    ///
    /// ── WHAT DIFFERENCE IT MAKES, GIVEN THAT THE CONTENT IS THE SAME ────
    ///
    /// A tool is called by the MODEL, when it decides to call it. A
    /// resource is attached by **whoever uses the client**, before the model
    /// even speaks: it is chosen from the list and ends up in the context. They are two different
    /// moments and two decisions by two different people.
    ///
    /// For this catalog the difference is concrete: `list_operations` costs
    /// a tool round — the model must first understand that it needs it, then
    /// ask for it, then read the answer. Whoever works already knows they need it, and
    /// with a resource they just put it there. **It is not another copy of the
    /// catalog: it is another door onto the same one.**
    ///
    /// ── AND HERE WAS THE REAL DANGER ────────────────────────────────────
    ///
    /// Two doors onto the same thing become two things the day one
    /// of the two is written separately. So this class **knows nothing about the
    /// catalog**: it takes the same `Catalog` object that the tools use
    /// and calls the same two methods, `Items(null)` and `Description(nome)`.
    /// There is no point where one could write «also» here.
    /// A contract check verifies it byte for byte, because *a rule
    /// that lives only in a comment is a rule that one day someone does not
    /// read.*
    ///
    /// ── WHY ONE RESOURCE PLUS A URI TEMPLATE ────────────────────────────
    ///
    /// The operations are more than sixty — **the number is not written here**,
    /// because it changes and a fixed number in a comment becomes a lie (it is the
    /// same reason why the README is checked by a test instead of
    /// by memory). Listing them one by one as resources
    /// would mean sending sixty lines on every `resources/list`, and whoever
    /// looks at the list is looking for *the catalog*, not `fillet_edges`. So:
    ///
    ///   - **one resource**, `catalog://operations`, which is the short list —
    ///     the same thing that `list_operations` answers;
    ///   - **a URI template**, `catalog://operation/{name}`, for the full
    ///     schema of a single one. A URI template is the form the
    ///     protocol has for saying «there are many and they are called like this»
    ///     instead of listing them.
    ///
    /// It is the same two-level catalog that holds up the tools, said in the
    /// vocabulary of resources.
    /// </summary>
    internal sealed class Risorse
    {
        /// <summary>
        /// The short list. Written only once because it appears in three
        /// places — the list, the reading and the check — and three equal strings
        /// written by hand become three different strings.
        /// </summary>
        public const string UriList = "catalog://operations";

        /// <summary>The fixed part of `catalog://operation/&lt;name&gt;`.</summary>
        public const string OperationPrefix = "catalog://operation/";

        private readonly Catalog _catalog;

        public Risorse(Catalog catalog) { _catalog = catalog; }

        /// <summary>
        /// What there is to attach. A single entry: see above.
        /// </summary>
        public object Items()
        {
            return new object[]
            {
                new Dictionary<string, object>
                {
                    ["uri"] = UriList,
                    ["name"] = "Operation catalog",
                    ["title"] = "Operation catalog",
                    ["description"] =
                        "The " + _catalog.HowMany + " drawing operations this " +
                        "bridge can run, grouped by family, one line " +
                        "each. The full schema of just one is read from " +
                        OperationPrefix + "<name>, or with the tool " +
                        "«describe_operation».",
                    ["mimeType"] = "application/json",
                },
            };
        }

        /// <summary>
        /// The URI templates: the operations one by one, without listing them.
        /// </summary>
        public object Models()
        {
            return new object[]
            {
                new Dictionary<string, object>
                {
                    ["uriTemplate"] = OperationPrefix + "{name}",
                    ["name"] = "Single operation",
                    ["title"] = "Single operation",
                    ["description"] =
                        "The full schema of ONE operation: what it needs first, what it " +
                        "does, and all its arguments. The names are listed in " + UriList + ".",
                    ["mimeType"] = "application/json",
                },
            };
        }

        /// <summary>
        /// The content of a URI, or `null` if I do not know this URI.
        ///
        /// **`null` is not a silent error**: whoever calls turns it into
        /// a protocol error that says which URIs exist. Here `null` is returned
        /// and nothing else because this class does not know how a JSON-RPC
        /// error is written, and must not need to.
        /// </summary>
        public object Read(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) return null;

            if (string.Equals(uri, UriList, StringComparison.Ordinal))
                return Content(uri, _catalog.Items(null));

            if (uri.StartsWith(OperationPrefix, StringComparison.Ordinal))
            {
                var itemName = uri.Substring(OperationPrefix.Length);
                // A URI can arrive with the non-ASCII letters encoded.
                // The names of the operations are all ASCII, but `%20` on a
                // wrong name must give «I do not know that name», not «crooked
                // URI»: they are two different messages for the reader.
                try { itemName = Uri.UnescapeDataString(itemName); } catch { }
                var descriptionText = _catalog.Description(itemName);
                if (descriptionText == null) return null;
                return Content(uri, descriptionText);
            }

            return null;
        }

        /// <summary>
        /// The names that resemble the one asked for, for the error. It lives here and not
        /// in the caller because the catalog is what knows it.
        /// </summary>
        public List<string> Similar(string uri)
        {
            if (uri == null || !uri.StartsWith(OperationPrefix, StringComparison.Ordinal))
                return new List<string>();
            var itemName = uri.Substring(OperationPrefix.Length);
            try { itemName = Uri.UnescapeDataString(itemName); } catch { }
            return _catalog.Similar(itemName) ?? new List<string>();
        }

        private static object Content(string uri, object data)
        {
            return new Dictionary<string, object>
            {
                ["contents"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["uri"] = uri,
                        ["mimeType"] = "application/json",
                        // THE TEXT, NOT THE OBJECT. `resources/read` wants
                        // `text` or `blob`: sending the structured object
                        // would be an answer that looks right and that a
                        // client reads as empty.
                        ["text"] = Protocol.InJson(data),
                    },
                },
            };
        }
    }
}
