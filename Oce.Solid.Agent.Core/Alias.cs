using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using Oce.Solid.Agent.Core.Catalog;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE NAMES OF BEFORE THE RENAME, ACCEPTED ON INPUT UNTIL THE FIRST RELEASE
    /// (entry W3.1-bis.5, decision D4).
    ///
    /// Rename window 2 gave the MCP surface English names: tools, actions,
    /// operations, families, arguments, enum values, selectors. A client that
    /// saved a plan or a prompt with the Italian names keeps working until the
    /// tag of the first release: every Italian name is translated on the way
    /// IN, and the answer says so in `deprecated`, one line per name. Nothing
    /// is translated on the way OUT (an answer speaks English only), the
    /// catalogue and `describe_operation` speak English only, and reflection
    /// never comes through here.
    ///
    /// The dictionary is the name map itself (Checks/language/name-map.tsv,
    /// embedded in this assembly as `name-map.tsv`): the same rows that renamed
    /// the surface translate the old names to it, so the two cannot disagree.
    /// An argument is translated only to a name the schema has at the level
    /// where the Italian key was written, so a word with two senses (`a_mm`:
    /// the end of a segment, the throat of a weld, the offset of a section
    /// line) lands on the one the operation takes; an enum value only to a
    /// value the schema lists.
    ///
    /// REMOVED IN W3.1-bis.11, before the tag: this file, the embedded map in
    /// Oce.Solid.Agent.Core.csproj, the calls in OperazioniSw.Esegui,
    /// Tools/McpTools.cs and Bench/Expectations.cs, the cases of
    /// Bench/cases/aliases and the contract AliasLayerContract.
    /// </summary>
    public static class Alias
    {
        private sealed class Table
        {
            public readonly Dictionary<string, string> Tool = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> SubAction = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> Operation = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> Family = new Dictionary<string, string>(StringComparer.Ordinal);
            // Italian argument -> every English name it may take
            public readonly Dictionary<string, List<string>> Argument = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            // English argument -> Italian value -> English value
            public readonly Dictionary<string, Dictionary<string, string>> EnumValue =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> SelectorKind = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> SelectorField = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> SelectorPair = new Dictionary<string, string>(StringComparer.Ordinal);
            public bool Loaded;
            public string Problem;
        }

        // The second senses of an argument that the map writes in its notes
        // (the weld symbol's throat and the section line's offset are both
        // `a_mm`): the schema of the operation decides which one a call means.
        private static readonly Dictionary<string, string[]> SecondSenses =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["a_mm"] = new[] { "throat_mm", "offset_mm" },
            };

        // The arguments whose values are facings (mate: face_a, face_b): they
        // take the values of the selector field `normal`.
        private static readonly string[] FacingArguments = { "face_a", "face_b" };

        // Read once, when the class is first used (the runtime runs a static
        // initializer once and on one thread). Not a Lazy<T>: its member is a
        // name the obsolete-call scanner cannot decide (Tests/ObsoleteCallsContract.cs).
        private static readonly Table _table = Load();

        private static int _uses;

        /// <summary>How many names were translated since the process started.</summary>
        public static int Uses => Volatile.Read(ref _uses);

        /// <summary>Null when the dictionary was read; otherwise why it was not.</summary>
        public static string Problem => _table.Loaded ? null : _table.Problem;

        private static Table Load()
        {
            var t = new Table();
            try
            {
                var asm = typeof(Alias).Assembly;
                string text = null;
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith("name-map.tsv", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var s = asm.GetManifestResourceStream(name))
                    using (var r = new StreamReader(s))
                        text = r.ReadToEnd();
                }
                if (text == null) { t.Problem = "name-map.tsv is not embedded in the library"; return t; }
                foreach (var raw in text.Split('\n'))
                {
                    var line = raw.TrimEnd('\r');
                    if (line.Length == 0 || line[0] == '#') continue;
                    var c = line.Split('\t');
                    if (c.Length < 3 || c[0] == "type" || c[1].Length == 0 || c[2].Length == 0 || c[1] == c[2]) continue;
                    string type = c[0], it = c[1], en = c[2];
                    switch (type)
                    {
                        case "tool": t.Tool[it] = en; break;
                        case "sub_action": t.SubAction[it] = en; break;
                        case "operation": t.Operation[it] = en; break;
                        case "catalogue_family": t.Family[it] = en; break;
                        case "argument":
                            AddArgument(t, it, en);
                            break;
                        case "enum_value":
                        {
                            int a = it.IndexOf('='), b = en.IndexOf('=');
                            if (a <= 0 || b <= 0) break;
                            AddValue(t, en.Substring(0, b), it.Substring(a + 1), en.Substring(b + 1));
                            break;
                        }
                        case "selector":
                            if (it.StartsWith("@", StringComparison.Ordinal)) t.SelectorKind[it] = en;
                            else if (it.Contains("=")) t.SelectorPair[it] = en;
                            else t.SelectorField[it] = en;
                            break;
                    }
                }
                foreach (string argument in SecondSenses.Keys)
                    foreach (string en in SecondSenses[argument]) AddArgument(t, argument, en);
                // the facings: the values of the selector field normal
                foreach (string pair in t.SelectorPair.Keys)
                {
                    string english = t.SelectorPair[pair];
                    int a = pair.IndexOf('='), b = english.IndexOf('=');
                    if (a <= 0 || b <= 0 || english.Substring(0, b) != "normal") continue;
                    foreach (var f in FacingArguments) AddValue(t, f, pair.Substring(a + 1), english.Substring(b + 1));
                }
                t.Loaded = true;
            }
            catch (Exception ex) { t.Problem = "name-map.tsv not readable: " + ex.Message; }
            return t;
        }

        private static void AddArgument(Table t, string it, string en)
        {
            List<string> names;
            if (!t.Argument.TryGetValue(it, out names)) t.Argument[it] = new List<string> { en };
            else if (!names.Contains(en)) t.Argument[it] = names.Concat(new[] { en }).ToList();
        }

        private static void AddValue(Table t, string argument, string it, string en)
        {
            Dictionary<string, string> values;
            if (!t.EnumValue.TryGetValue(argument, out values))
            {
                values = new Dictionary<string, string>(StringComparer.Ordinal);
                t.EnumValue[argument] = values;
            }
            values[it] = en;
        }

        private static void Record(List<string> notes, string old, string now)
        {
            Interlocked.Increment(ref _uses);
            // at the end of the list; `Insert` and not `Add`, a name the
            // obsolete-call scanner cannot decide on a receiver that is not
            // a SOLIDWORKS interface (Tests/ObsoleteCallsContract.cs)
            if (notes != null)
                notes.Insert(notes.Count, "«" + old + "» is deprecated, use «" + now + "» (the Italian names are accepted until the first release)");
        }

        private static string Lookup(Dictionary<string, string> table, string name, List<string> notes)
        {
            string en;
            if (name == null || !_table.Loaded || !table.TryGetValue(name, out en)) return name;
            Record(notes, name, en);
            return en;
        }

        /// <summary>A tool name: the English one, or the name as it came.</summary>
        public static string Tool(string name, List<string> notes) { return Lookup(_table.Tool, name, notes); }

        /// <summary>A value of the `action` argument of the document tool.</summary>
        public static string SubAction(string value, List<string> notes) { return Lookup(_table.SubAction, value, notes); }

        /// <summary>An operation name.</summary>
        public static string Operation(string name, List<string> notes) { return Lookup(_table.Operation, name, notes); }

        /// <summary>A family of the catalogue.</summary>
        public static string Family(string name, List<string> notes) { return Lookup(_table.Family, name, notes); }

        // The URIs of the MCP resources of the catalogue (Mcp/Risorse.cs), before
        // and after window 2. Written here, not read from the bridge: the bridge
        // is not this assembly, and its constants say only the English URIs.
        private const string OldResourceList = "catalogo://operazioni";
        private const string OldResourceOne = "catalogo://operazione/";
        private const string ResourceList = "catalog://operations";
        private const string ResourceOne = "catalog://operation/";

        /// <summary>
        /// A URI of the catalogue resources written as before window 2
        /// (`catalogo://operazioni`, `catalogo://operazione/&lt;name&gt;` with
        /// the Italian or the English name of an operation): the English URI,
        /// with one deprecated line; any other URI as it came.
        /// </summary>
        public static string ResourceUri(string uri, List<string> notes)
        {
            if (uri == null || !_table.Loaded) return uri;
            if (string.Equals(uri, OldResourceList, StringComparison.Ordinal))
            {
                Record(notes, uri, ResourceList);
                return ResourceList;
            }
            string name = null;
            if (uri.StartsWith(OldResourceOne, StringComparison.Ordinal)) name = uri.Substring(OldResourceOne.Length);
            else if (uri.StartsWith(ResourceOne, StringComparison.Ordinal))
            {
                // the English URI with the Italian name of the operation
                string tail = uri.Substring(ResourceOne.Length), unescaped = tail;
                try { unescaped = Uri.UnescapeDataString(tail); } catch (UriFormatException) { }
                string en;
                if (!_table.Operation.TryGetValue(unescaped, out en)) return uri;
                Record(notes, unescaped, en);
                return ResourceOne + en;
            }
            if (name == null) return uri;
            string decoded = name;
            try { decoded = Uri.UnescapeDataString(name); } catch (UriFormatException) { }
            string english;
            string now = ResourceOne + (_table.Operation.TryGetValue(decoded, out english) ? english : decoded);
            Record(notes, uri, now);
            return now;
        }

        /// <summary>
        /// The keys and values of `arguments`, in place, against a JSON schema
        /// (the deserialized schema of the operation): an Italian key becomes
        /// the English name the schema has at that level; an Italian enum value
        /// the English value the schema lists; a selector its English spelling.
        /// Without a schema a key is left alone: the operation is unknown, and
        /// its refusal names what it got.
        /// </summary>
        public static void Arguments(Dictionary<string, object> arguments, Dictionary<string, object> schema, List<string> notes)
        {
            if (arguments == null || !_table.Loaded) return;
            var properties = PropertiesOf(schema);
            foreach (var key in arguments.Keys.ToList())
            {
                string now = Key(arguments, key, properties, notes);
                arguments[now] = Value(now, arguments[now], RuleOf(properties, now), notes);
            }
        }

        private static Dictionary<string, object> PropertiesOf(Dictionary<string, object> schema)
        {
            object p;
            return schema != null && schema.TryGetValue("properties", out p) ? p as Dictionary<string, object> : null;
        }

        private static Dictionary<string, object> RuleOf(Dictionary<string, object> properties, string key)
        {
            object r;
            return properties != null && properties.TryGetValue(key, out r) ? r as Dictionary<string, object> : null;
        }

        // A key of `arguments`, renamed in place when it is Italian and the
        // schema has its English name at this level.
        private static string Key(Dictionary<string, object> arguments, string key, Dictionary<string, object> properties, List<string> notes)
        {
            List<string> candidates;
            if (properties == null || key.StartsWith("_", StringComparison.Ordinal) || properties.ContainsKey(key) ||
                !_table.Argument.TryGetValue(key, out candidates)) return key;
            string target = candidates.FirstOrDefault(properties.ContainsKey);
            if (target == null || arguments.ContainsKey(target)) return key;
            arguments[target] = arguments[key];
            arguments.Remove(key);
            Record(notes, key, target);
            return target;
        }

        private static object Value(string key, object value, Dictionary<string, object> rule, List<string> notes)
        {
            var s = value as string;
            if (s != null)
            {
                if (s.StartsWith("@", StringComparison.Ordinal)) return Selector(s, notes);
                return EnumValue(key, s, rule, notes);
            }
            var nested = value as Dictionary<string, object>;
            if (nested != null)
            {
                Arguments(nested, rule, notes);
                return nested;
            }
            object i;
            var items = rule != null && rule.TryGetValue("items", out i) ? i as Dictionary<string, object> : null;
            var array = value as object[];
            if (array != null)
            {
                for (int k = 0; k < array.Length; k++) array[k] = Value(key, array[k], items, notes);
                return array;
            }
            var list = value as IList;
            if (list != null && !list.IsReadOnly && !list.IsFixedSize)
                for (int k = 0; k < list.Count; k++) list[k] = Value(key, list[k], items, notes);
            return value;
        }

        private static string EnumValue(string key, string value, Dictionary<string, object> rule, List<string> notes)
        {
            Dictionary<string, string> values;
            string en;
            if (!_table.EnumValue.TryGetValue(key, out values) || !values.TryGetValue(value, out en)) return value;
            // only into a value the schema lists. An argument without an enum
            // takes free text (the `variant` of mate, the `view` of a drawing
            // operation, which names a view): a word there is not a value of
            // the map, even when another operation has an argument of the same
            // name whose enum lists it («angolo» is a fillet weld, not a mate).
            // The facings of mate are the one set of values the schema does
            // not list (face_a, face_b take the values of the selector field
            // normal).
            object e;
            var allowed = rule != null && rule.TryGetValue("enum", out e) ? e as IEnumerable : null;
            if (allowed == null && Array.IndexOf(FacingArguments, key) < 0) return value;
            if (allowed != null && !allowed.Cast<object>().Any(x => string.Equals(Convert.ToString(x), en, StringComparison.Ordinal)))
                return value;
            Record(notes, value, en);
            return en;
        }

        /// <summary>A selector of the bench in its English spelling: kind, fields and the values of normal (@face(normal=front)).</summary>
        public static string Selector(string selector, List<string> notes)
        {
            if (selector == null || !selector.StartsWith("@", StringComparison.Ordinal) || !_table.Loaded) return selector;
            var t = _table;
            int open = selector.IndexOf('(');
            string kind = open < 0 ? selector : selector.Substring(0, open);
            string kindNow;
            if (!t.SelectorKind.TryGetValue(kind, out kindNow)) kindNow = kind;
            if (open < 0 || !selector.EndsWith(")", StringComparison.Ordinal))
            {
                if (kindNow == kind) return selector;
                Record(notes, selector, kindNow);
                return kindNow;
            }
            string inside = selector.Substring(open + 1, selector.Length - open - 2);
            List<string> parts = TopLevelParts(inside).ToList();
            for (int i = 0; i < parts.Count; i++)
            {
                string part = parts[i];
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                bool about = part[eq - 1] == '~';
                string field = part.Substring(0, about ? eq - 1 : eq).Trim();
                string val = part.Substring(eq + 1);
                string pair, f;
                if (t.SelectorPair.TryGetValue(field + "=" + val.Trim(), out pair))
                {
                    int b = pair.IndexOf('=');
                    parts[i] = pair.Substring(0, b) + (about ? "~=" : "=") + pair.Substring(b + 1);
                }
                else if (t.SelectorField.TryGetValue(field, out f))
                    parts[i] = f + (about ? "~=" : "=") + val;
            }
            string now = kindNow + "(" + string.Join(",", parts) + ")";
            if (now == selector) return selector;
            Record(notes, selector, now);
            return now;
        }

        // The parts of a selector between its commas, the commas inside
        // parentheses aside.
        private static IEnumerable<string> TopLevelParts(string inside)
        {
            int depth = 0, start = 0;
            for (int i = 0; i <= inside.Length; i++)
            {
                if (i == inside.Length || (inside[i] == ',' && depth == 0)) { yield return inside.Substring(start, i - start); start = i + 1; continue; }
                if (inside[i] == '(') depth++;
                if (inside[i] == ')') depth--;
            }
        }

        // ── the operations of the library ─────────────────────────────────
        private static readonly object SchemasGate = new object();
        private static Dictionary<string, Dictionary<string, object>> _schemas;

        private static Dictionary<string, Dictionary<string, object>> Schemas()
        {
            lock (SchemasGate)
            {
                if (_schemas != null) return _schemas;
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var schemas = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
                foreach (var o in Operazioni.Tutte)
                {
                    if (o.Nome == null || o.Schema == null) continue;
                    schemas[o.Nome] = serializer.DeserializeObject(Nodo.Json(o.Schema)) as Dictionary<string, object>;
                }
                _schemas = schemas;
                return schemas;
            }
        }

        /// <summary>The schema of an operation of the catalogue of this library, or null.</summary>
        public static Dictionary<string, object> SchemaOf(string operation)
        {
            Dictionary<string, object> s;
            return operation != null && Schemas().TryGetValue(operation, out s) ? s : null;
        }

        /// <summary>
        /// One step for the library: the operation name and its arguments, in
        /// place. The notes are the `deprecated` lines of the answer.
        /// </summary>
        public static List<string> Step(ref string operation, Dictionary<string, object> arguments)
        {
            List<string> notes = new List<string>();
            if (!_table.Loaded) return notes;
            operation = Operation(operation, notes);
            Arguments(arguments, SchemaOf(operation), notes);
            return notes;
        }

        /// <summary>
        /// The steps of a plan of the bridge, in place: the keys of a step
        /// (operation, arguments), the operation name, and the arguments
        /// against the schema of that operation.
        /// </summary>
        public static void Plan(object steps, List<string> notes)
        {
            var list = steps as IList;
            if (list == null || !_table.Loaded) return;
            var stepKeys = new Dictionary<string, object>
            {
                ["operation"] = null, ["arguments"] = null,
            };
            foreach (var x in list)
            {
                var step = x as Dictionary<string, object>;
                if (step == null) continue;
                foreach (var key in step.Keys.ToList()) Key(step, key, stepKeys, notes);
                object op;
                if (step.TryGetValue("operation", out op) && op is string)
                    step["operation"] = Operation((string)op, notes);
                object args;
                var name = step.TryGetValue("operation", out op) ? op as string : null;
                if (step.TryGetValue("arguments", out args))
                    Arguments(args as Dictionary<string, object>, SchemaOf(name), notes);
            }
        }

        /// <summary>
        /// The arguments of a tool, in place, against its input schema: the
        /// keys, the action of `document`, the family of `list_operations`,
        /// the operation of `describe_operation`, the steps of a plan, and the
        /// enum values the schema lists.
        /// </summary>
        public static void ToolArguments(string tool, Dictionary<string, object> arguments, Dictionary<string, object> schema, List<string> notes)
        {
            if (arguments == null || !_table.Loaded) return;
            var properties = PropertiesOf(schema);
            foreach (var key in arguments.Keys.ToList())
            {
                string now = Key(arguments, key, properties, notes);
                object v = arguments[now];
                if (now == "steps") { Plan(v, notes); continue; }
                var s = v as string;
                if (s != null && now == "action") { arguments[now] = SubAction(s, notes); continue; }
                if (s != null && now == "family") { arguments[now] = Family(s, notes); continue; }
                if (s != null && now == "name" && tool == "describe_operation") { arguments[now] = Operation(s, notes); continue; }
                arguments[now] = Value(now, v, RuleOf(properties, now), notes);
            }
        }
    }
}
