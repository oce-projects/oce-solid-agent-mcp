using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// THE KEYS A BENCH CASE MAY CARRY, AND THE REFUSAL OF EVERY OTHER KEY
    /// (entry W3.1-bis.2, item 4).
    ///
    /// ── WHY ─────────────────────────────────────────────────────────────────
    ///
    /// The loader reads a case with ContainsKey and a lookup that answers null
    /// for a missing key: an absent key reads as "not asked". A key written
    /// wrong, or renamed in the cases and not in the reader, is then skipped in
    /// silence. A step that must fail and spells its volume check differently
    /// still refuses, the volume is never compared, and the case stays green.
    /// The translation of the repository (W3.1-bis.4) renames every key of
    /// every case at once: this is the net under that step.
    ///
    /// ── WHAT IS ADMITTED ────────────────────────────────────────────────────
    ///
    /// `Bench/case-keys.json` lists, for every context of a case (the case, a
    /// step, an expectation...), the keys the loader READS, the keys it only
    /// DOCUMENTS (prose it never reads), the CHILDREN that lead to another
    /// context, and the FROZEN free keys: notes with invented names, written
    /// before this net existed. The frozen list only shrinks. A new free note
    /// starts with the prefix below, is admitted in every context, and is
    /// never read.
    ///
    /// The list is data and not code, and each key is written where the
    /// language gate already counts it: a read key as a string, the literal
    /// the reader carries in C#; a documented or frozen key as a JSON key, the
    /// way the cases carry it. Written the other way round, or as C# literals
    /// here, most of them would be new names for the gate. The renaming script
    /// of W3.1-bis.4 rewrites this file together with the cases, in the same
    /// commit.
    ///
    /// ── WHO COMPILES THIS FILE ──────────────────────────────────────────────
    ///
    /// The bench, which refuses a case with an unknown key as a bench error,
    /// and Oce.Solid.Agent.ContractTests, whose CaseKeysTests apply the same rule to every
    /// case before the bench runs: the bench is the last defence, not the
    /// first.
    /// </summary>
    internal sealed class CaseKeys
    {
        /// <summary>The prefix of a free note: admitted in every context, never read.</summary>
        internal const string FreePrefix = "_";

        /// <summary>The file, next to the cases folder.</summary>
        internal const string FileName = "case-keys.json";

        /// <summary>One place of a case, and what it may carry.</summary>
        internal sealed class Context
        {
            public string ContextName;
            public readonly HashSet<string> Read = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> Documented = new HashSet<string>(StringComparer.Ordinal);
            public readonly HashSet<string> FrozenFree = new HashSet<string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> Children = new Dictionary<string, string>(StringComparer.Ordinal);

            public bool Admits(string key)
            {
                return key.StartsWith(FreePrefix, StringComparison.Ordinal) ||
                       Read.Contains(key) || Documented.Contains(key) || FrozenFree.Contains(key);
            }
        }

        internal readonly Dictionary<string, Context> Contexts =
            new Dictionary<string, Context>(StringComparer.Ordinal);

        /// <summary>The context of the root of a declared case (the folders the bench walks).</summary>
        internal string DeclaredRoot { get; private set; }

        /// <summary>The context of the root of a regression case (`cases/regression`).</summary>
        internal string RegressionRoot { get; private set; }

        /// <summary>How many (context, key) pairs the frozen list holds.</summary>
        internal int FrozenCount
        {
            get { return Contexts.Values.Sum(c => c.FrozenFree.Count); }
        }

        private static readonly string[] TopKeys = { "about", "format", "roots", "contexts" };
        private static readonly string[] ContextKeys = { "read", "documented", "children", "frozen_free" };

        /// <summary>
        /// Reads the list from its file. Throws with the reason when the file is
        /// missing or malformed: a list that cannot be read is never an empty
        /// list, because an empty list would refuse every case for the wrong
        /// reason, and a missing one must not admit every case either.
        /// </summary>
        internal static CaseKeys Load(string path)
        {
            if (!File.Exists(path)) throw new FormatException("the list of case keys is missing: " + path);
            return Parse(File.ReadAllText(path));
        }

        /// <summary>
        /// Parses the list, and refuses what it does not know: the list itself
        /// follows the rule it enforces.
        /// </summary>
        internal static CaseKeys Parse(string json)
        {
            object parsed;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                s.MaxJsonLength = int.MaxValue;
                parsed = s.DeserializeObject(json);
            }
            catch (Exception ex) { throw new FormatException("the list of case keys is not valid JSON: " + ex.Message); }

            var top = parsed as Dictionary<string, object>;
            if (top == null) throw new FormatException("the list of case keys is not a JSON object");
            foreach (var k in top.Keys)
                if (Array.IndexOf(TopKeys, k) < 0)
                    throw new FormatException("the list of case keys has an unknown top key «" + k + "»");

            object format;
            if (!top.TryGetValue("format", out format) ||
                Convert.ToString(format, CultureInfo.InvariantCulture) != "1")
                throw new FormatException("the list of case keys must say format 1");

            var roots = Obj(top, "roots", "the top");
            var contexts = Obj(top, "contexts", "the top");
            if (contexts.Count == 0) throw new FormatException("the list of case keys has no context");

            var keys = new CaseKeys();
            foreach (var contextName in contexts.Keys)
            {
                var body = contexts[contextName] as Dictionary<string, object>;
                if (body == null) throw new FormatException("context «" + contextName + "» is not an object");
                foreach (var k in body.Keys)
                    if (Array.IndexOf(ContextKeys, k) < 0)
                        throw new FormatException("context «" + contextName + "» has an unknown key «" + k + "»");

                var c = new Context { ContextName = contextName };
                c.Read.UnionWith(StringsOf(body, "read", contextName));
                c.Documented.UnionWith(KeysSetToTrue(body, "documented", contextName));
                c.FrozenFree.UnionWith(KeysSetToTrue(body, "frozen_free", contextName));
                object children;
                if (body.TryGetValue("children", out children) && children != null)
                {
                    var map = children as Dictionary<string, object>;
                    if (map == null) throw new FormatException("context «" + contextName + "»: «children» is not an object");
                    foreach (var childKey in map.Keys)
                    {
                        var target = map[childKey] as string;
                        if (string.IsNullOrEmpty(target))
                            throw new FormatException("context «" + contextName + "»: child «" + childKey + "» names no context");
                        c.Children[childKey] = target;
                    }
                }

                // ONE KEY, ONE MEANING. A key both read and frozen would stay
                // admitted after it stops being read, and a child that is not
                // a read or documented key would lead to a context nobody enters.
                foreach (var k in c.FrozenFree)
                {
                    if (c.Read.Contains(k) || c.Documented.Contains(k))
                        throw new FormatException("context «" + contextName + "»: «" + k + "» is frozen and also read or documented");
                    if (k.StartsWith(FreePrefix, StringComparison.Ordinal))
                        throw new FormatException("context «" + contextName + "»: «" + k + "» starts with the free prefix and needs no freezing");
                }
                foreach (var k in c.Read)
                    if (c.Documented.Contains(k))
                        throw new FormatException("context «" + contextName + "»: «" + k + "» is both read and documented");
                foreach (var k in c.Children.Keys)
                    if (!c.Read.Contains(k) && !c.Documented.Contains(k))
                        throw new FormatException("context «" + contextName + "»: child «" + k + "» is neither read nor documented");

                keys.Contexts[contextName] = c;
            }

            foreach (var contextName in keys.Contexts.Keys)
            {
                var c = keys.Contexts[contextName];
                foreach (var childKey in c.Children.Keys)
                    if (!keys.Contexts.ContainsKey(c.Children[childKey]))
                        throw new FormatException("context «" + contextName + "»: child «" + childKey + "» leads to «" +
                                                  c.Children[childKey] + "», which is not a context");
            }

            keys.DeclaredRoot = Text(roots, "declared");
            keys.RegressionRoot = Text(roots, "regression");
            foreach (var k in roots.Keys)
                if (k != "declared" && k != "regression")
                    throw new FormatException("the list of case keys has an unknown root «" + k + "»");
            foreach (var r in new[] { keys.DeclaredRoot, keys.RegressionRoot })
                if (!keys.Contexts.ContainsKey(r))
                    throw new FormatException("the root context «" + r + "» is not a context");
            return keys;
        }

        /// <summary>
        /// The keys of a case that no context admits, each with its path
        /// (`passi[3].attese[1].the_key`). Empty when the case is clean. Values
        /// that are not objects or lists of objects are not entered: argument
        /// names belong to the catalogue, not to the case.
        /// </summary>
        internal List<string> Unknown(Dictionary<string, object> caseRoot, string rootContext)
        {
            var found = new List<string>();
            foreach (var f in Walk(caseRoot, Contexts[rootContext], ""))
                if (!f.Frozen) found.Add(f.Where);
            return found;
        }

        /// <summary>
        /// Adds «context/key» for every frozen free key this case uses: the
        /// contract uses it to find frozen entries that no case uses any more.
        /// </summary>
        internal void CollectFrozenUse(Dictionary<string, object> caseRoot, string rootContext, ISet<string> used)
        {
            var mine = new List<string>();
            foreach (var f in Walk(caseRoot, Contexts[rootContext], ""))
                if (f.Frozen) mine.Add(f.Where);
            used.UnionWith(mine);
        }

        /// <summary>What the walk finds: an unknown key (its path), or a frozen key in use («context/key»).</summary>
        private sealed class Finding
        {
            public bool Frozen;
            public string Where;
        }

        private IEnumerable<Finding> Walk(Dictionary<string, object> node, Context context, string path)
        {
            foreach (var key in node.Keys)
            {
                var here = path.Length == 0 ? key : path + "." + key;
                if (key.StartsWith(FreePrefix, StringComparison.Ordinal)) continue;
                if (!context.Admits(key))
                {
                    yield return new Finding { Frozen = false, Where = here };
                    continue;
                }
                if (context.FrozenFree.Contains(key))
                {
                    yield return new Finding { Frozen = true, Where = context.ContextName + "/" + key };
                    continue;
                }
                string child;
                if (!context.Children.TryGetValue(key, out child)) continue;
                var target = Contexts[child];
                var one = node[key] as Dictionary<string, object>;
                if (one != null)
                {
                    foreach (var f in Walk(one, target, here)) yield return f;
                    continue;
                }
                var list = node[key] as System.Collections.IList;
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i] as Dictionary<string, object>;
                    if (item == null) continue;
                    foreach (var f in Walk(item, target, here + "[" + (i + 1) + "]")) yield return f;
                }
            }
        }

        /// <summary>
        /// The read keys that appear as a string literal in none of the given
        /// sources, or only where a key is WRITTEN. A key read by nobody is a
        /// key renamed on one side only: the cases and this list say one name,
        /// the reader another.
        ///
        /// The sources must come without their comments: a doc comment that
        /// quotes a key is not a reader (seen on 1/10/2026, the reader of
        /// «document» renamed alone stayed green because a comment still said
        /// it). And a literal in a write («["key"] = ...», an initializer
        /// «{ "key", ... }») builds a case or a report, it reads nothing: the
        /// self-tests of the bench build fake cases with every key, and a
        /// renaming script that rewrote them and not the reader would keep
        /// this check green.
        /// </summary>
        internal List<string> ReadKeysWithoutReader(IEnumerable<string> sources)
        {
            var all = string.Join("\n", sources);
            var missing = new List<string>();
            foreach (var contextName in Contexts.Keys.OrderBy(x => x, StringComparer.Ordinal))
                foreach (var k in Contexts[contextName].Read.OrderBy(x => x, StringComparer.Ordinal))
                    if (!IsReadIn(all, k))
                        missing.Add(contextName + "/" + k);
            return missing;
        }

        /// <summary>
        /// True when the code holds the key as a literal somewhere that is not
        /// a write: not the index of an assignment («["key"] = value») and not
        /// the first element of an initializer entry («{ "key", value }»).
        /// </summary>
        internal static bool IsReadIn(string code, string key)
        {
            var literal = "\"" + key + "\"";
            for (int i = code.IndexOf(literal, StringComparison.Ordinal); i >= 0;
                 i = code.IndexOf(literal, i + 1, StringComparison.Ordinal))
            {
                int before = i - 1;
                while (before >= 0 && char.IsWhiteSpace(code[before])) before--;
                int after = i + literal.Length;
                while (after < code.Length && char.IsWhiteSpace(code[after])) after++;
                char b = before >= 0 ? code[before] : '\0';
                char a = after < code.Length ? code[after] : '\0';
                bool indexWrite = false;
                if (b == '[' && a == ']')
                {
                    int eq = after + 1;
                    while (eq < code.Length && char.IsWhiteSpace(code[eq])) eq++;
                    indexWrite = eq < code.Length && code[eq] == '=' && (eq + 1 >= code.Length || code[eq + 1] != '=');
                }
                bool initializerEntry = b == '{' && a == ',';
                if (!indexWrite && !initializerEntry) return true;
            }
            return false;
        }

        /// <summary>The message a bench error carries for the unknown keys of a case.</summary>
        internal static string Message(List<string> unknown)
        {
            return "keys the bench does not read: " + string.Join(", ", unknown.Take(8)) +
                   (unknown.Count > 8 ? " and " + (unknown.Count - 8) + " more" : "") +
                   ". A free note starts with «" + FreePrefix + "»; the admitted keys are in Bench/" + FileName;
        }

        private static Dictionary<string, object> Obj(Dictionary<string, object> map, string key, string where)
        {
            object v;
            var o = map.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
            if (o == null) throw new FormatException(where + " of the list of case keys has no object «" + key + "»");
            return o;
        }

        private static string Text(Dictionary<string, object> map, string key)
        {
            object v;
            var s = map.TryGetValue(key, out v) ? v as string : null;
            if (string.IsNullOrEmpty(s)) throw new FormatException("the list of case keys has no root «" + key + "»");
            return s;
        }

        /// <summary>
        /// The read keys are a list of strings, written as the reader writes
        /// them in C#: the literal the reader check looks for is the same text,
        /// and for the language gate it is a name already counted in the code.
        /// </summary>
        private static HashSet<string> StringsOf(Dictionary<string, object> body, string key, string context)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            object v;
            if (!body.TryGetValue(key, out v) || v == null) return found;
            var list = v as System.Collections.IList;
            if (list == null) throw new FormatException("context «" + context + "»: «" + key + "» is not a list");
            foreach (var item in list)
            {
                var s = item as string;
                if (string.IsNullOrEmpty(s))
                    throw new FormatException("context «" + context + "»: «" + key + "» holds something that is not a key");
                if (!found.Add(s))
                    throw new FormatException("context «" + context + "»: «" + key + "» names «" + s + "» twice");
            }
            return found;
        }

        /// <summary>The documented and frozen keys: an object whose keys are the case keys, each set to true.</summary>
        private static HashSet<string> KeysSetToTrue(Dictionary<string, object> body, string key, string context)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            object v;
            if (!body.TryGetValue(key, out v) || v == null) return found;
            var map = v as Dictionary<string, object>;
            if (map == null) throw new FormatException("context «" + context + "»: «" + key + "» is not an object");
            foreach (var k in map.Keys)
            {
                var flag = map[k];
                if (!(flag is bool) || !(bool)flag)
                    throw new FormatException("context «" + context + "»: «" + key + "." + k + "» must be true");
                found.Add(k);
            }
            return found;
        }
    }
}
