using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Program;
using static Shared.McpClient;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// A DESCRIPTION SHOWN TO THE MODEL TEACHES THE VALUES ITS SCHEMA TAKES (W3.1-bis.5).
    ///
    /// Window 2 renamed the values of every enum; a description that still says
    /// «Default: passaggio» next to an enum [clearance, threaded, ...] teaches
    /// the model the Italian value, which works until W3.1-bis.11 only because of
    /// the aliases, and is refused after. Red when, in the description of an
    /// argument of the catalogue (Tools/catalog.json), an Italian value of
    /// THAT argument (the enum_value rows of the name map whose English value the
    /// enum lists) stands where the sentence uses it as a value:
    ///   after «default»; as «value = ...»; as «value (gloss)»;
    ///   in a list of values only («: a, b or c»); as a bullet under «Allowed
    ///   kinds:»; as «value if you say nothing».
    /// The same words elsewhere are prose and stay («the axis made by the step»).
    /// And in the tools of the bridge (tools/list, over stdio): the families
    /// list_operations takes must be the catalogue's, and an object written in a
    /// description ({step: 1, kind: ...}) must have English keys.
    ///
    /// Seen red on the catalogue of before the fix (86cace9): 69 places, in 33
    /// descriptions; and every run sees the detector red on planted descriptions
    /// first. REMOVED or rewritten in W3.1-bis.11, when the map leaves.
    /// </summary>
    internal static class DescriptionValuesContract
    {
        internal static void RunAll()
        {
            Group("the descriptions teach the English values of their arguments (W3.1-bis.5)");

            var map = ReadEnumValues();
            if (map == null) return;
            SeenRed(map);
            Catalogue(map);
            Tools();
        }

        // The Italian value of an English argument=value, from the map.
        private static string It(Dictionary<string, Dictionary<string, string>> map, string arg, string value)
        {
            Dictionary<string, string> values;
            string it;
            return map.TryGetValue(arg, out values) && values.TryGetValue(value, out it) ? it : "?" + value;
        }

        // English argument -> English value -> Italian value (enum_value rows).
        private static Dictionary<string, Dictionary<string, string>> ReadEnumValues()
        {
            var file = Path.Combine(Root(), "Checks", "language", "name-map.tsv");
            if (!File.Exists(file)) { Check("the name map is there", false, "missing " + file); return null; }
            var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
            {
                var c = line.Split('\t');
                if (c.Length < 3 || c[0] != "enum_value") continue;
                int a = c[1].IndexOf('='), b = c[2].IndexOf('=');
                if (a <= 0 || b <= 0) continue;
                string arg = c[2].Substring(0, b), en = c[2].Substring(b + 1), it = c[1].Substring(a + 1);
                if (en == it) continue;
                Dictionary<string, string> values;
                if (!map.TryGetValue(arg, out values)) map[arg] = values = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!values.ContainsKey(en)) values[en] = it;
            }
            return map;
        }

        private const string L = "A-Za-zÀ-ÿ0-9_";

        /// <summary>The Italian values a description uses as values (empty when none).</summary>
        internal static List<string> ItalianValuesIn(string description, ICollection<string> italian, ICollection<string> bulletItalian)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(description)) return found;
            Func<IEnumerable<string>, string> alt = words => string.Join("|", words.OrderByDescending(w => w.Length).Select(Regex.Escape));
            string nb = "(?![" + L + "])";
            if (italian != null && italian.Count > 0)
            {
                string w = alt(italian);
                var rules = new[]
                {
                    "[Dd]efault:? «?(" + w + ")" + nb,
                    "(?:^|; |\\. )(" + w + ") = ",
                    "(?:^|: |, | or )(?=(" + w + ") \\()",
                    "(?:^|[.;] )(" + w + ") if you (?:say nothing|do not say)",
                };
                foreach (var r in rules)
                    foreach (Match m in Regex.Matches(description, r)) found.Add(m.Groups[1].Value);
                // a list of values only: every word of it
                foreach (Match m in Regex.Matches(description, "(?:^|: )((?:" + w + ")(?:, (?:" + w + "))*,? or (?:" + w + "))" + nb))
                    foreach (Match one in Regex.Matches(m.Groups[1].Value, "(?<![" + L + "])(" + w + ")" + nb))
                        found.Add(one.Value);
            }
            if (bulletItalian != null && bulletItalian.Count > 0)
            {
                var h = Regex.Match(description, "Allowed kinds:");
                if (h.Success)
                    foreach (Match m in Regex.Matches(description.Substring(h.Index), "\\n- (" + alt(bulletItalian) + "):"))
                        found.Add(m.Groups[1].Value);
            }
            return found;
        }

        // The detector, red on what it must find and green on prose. The Italian
        // values come from the map; the sentences are the shapes the rules read.
        private static void SeenRed(Dictionary<string, Dictionary<string, string>> map)
        {
            string clearance = It(map, "variant", "clearance"), threaded = It(map, "variant", "threaded");
            string front = It(map, "plane", "front"), top = It(map, "plane", "top"), right = It(map, "plane", "right");
            string axis = It(map, "kind", "axis");
            var planted = ItalianValuesIn(clearance + " = a plain hole; " + threaded + " = tapped. Default: " + clearance + ".",
                                          new[] { clearance, threaded }, null);
            var list = ItalianValuesIn("the plane: " + front + ", " + top + " or " + right, new[] { front, top, right }, null);
            var bullets = ItalianValuesIn("the " + axis + " of a step.\n\nAllowed kinds:\n- " + axis + ": the axis it made", null, new[] { axis });
            var prose = ItalianValuesIn("the " + axis + " made by the step, the " + top + " face stays", new[] { axis, top }, new[] { axis });
            Check("the detector finds Italian values planted where a description names values, and not the same words in prose",
                  planted.Count == 3 && list.Count == 3 && bullets.Count == 1 && prose.Count == 0,
                  "planted " + planted.Count + "/3, list " + list.Count + "/3, bullet " + bullets.Count + "/1, prose " + prose.Count + "/0");
        }

        private static void Catalogue(Dictionary<string, Dictionary<string, string>> map)
        {
            var file = Path.Combine(Root(), "Tools", "catalog.json");
            if (!File.Exists(file)) { Check("the catalogue is there", false, "missing " + file); return; }
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var catalogue = ser.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
            var ops = catalogue != null && catalogue.ContainsKey("operations") ? catalogue["operations"] as object[] : null;
            if (ops == null) { Check("the catalogue has its operations", false, "no «operations»"); return; }
            Dictionary<string, string> kinds;
            var bulletItalian = map.TryGetValue("kind", out kinds) ? kinds.Values.ToList() : new List<string>();
            var bad = new List<string>();
            int described = 0;
            foreach (var o in ops.OfType<Dictionary<string, object>>())
            {
                var name = Convert.ToString(o.ContainsKey("name") ? o["name"] : null);
                Walk(o.ContainsKey("schema") ? o["schema"] as Dictionary<string, object> : null, null, (prop, node) =>
                {
                    object d;
                    if (!node.TryGetValue("description", out d) || !(d is string)) return;
                    described++;
                    var italian = new List<string>();
                    object e;
                    Dictionary<string, string> values;
                    if (prop != null && node.TryGetValue("enum", out e) && e is object[] && map.TryGetValue(prop, out values))
                        foreach (var v in ((object[])e).OfType<string>())
                        {
                            string it;
                            if (values.TryGetValue(v, out it)) italian.Add(it);
                        }
                    foreach (var w in ItalianValuesIn((string)d, italian, bulletItalian))
                        bad.Add(name + "." + (prop ?? "(schema)") + ": «" + w + "»");
                });
            }
            Check("no description of the catalogue names an Italian value of its argument",
                  bad.Count == 0,
                  bad.Count == 0 ? described + " descriptions read" : bad.Count + ": " + string.Join(", ", bad.Take(8)) + (bad.Count > 8 ? " ..." : ""));
        }

        // Every schema node with the argument it describes (items describe the argument of their array).
        private static void Walk(Dictionary<string, object> node, string prop, Action<string, Dictionary<string, object>> visit)
        {
            if (node == null) return;
            visit(prop, node);
            object p;
            if (node.TryGetValue("properties", out p) && p is Dictionary<string, object>)
                foreach (var kv in (Dictionary<string, object>)p) Walk(kv.Value as Dictionary<string, object>, kv.Key, visit);
            object i;
            if (node.TryGetValue("items", out i)) Walk(i as Dictionary<string, object>, prop, visit);
        }

        // The tools of the bridge: the families of list_operations and the
        // objects written in the descriptions.
        private static void Tools()
        {
            var exe = BridgeOrNotHere("the descriptions of the tools");
            if (exe == null) return;
            var p = Open(exe);
            if (p == null) { Check("the bridge starts", false, "it did not start"); return; }
            try
            {
                Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"prove\",\"version\":\"1\"}}}");
                var list = Inside(Obj(Ask(p, 2, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}")), "result") as Dictionary<string, object>;
                var tools = Inside(list, "tools") as object[];
                if (tools == null) { Check("the bridge lists its tools", false, "no result.tools"); return; }

                var families = FamiliesOfTheCatalogue();
                var listOps = tools.OfType<Dictionary<string, object>>().FirstOrDefault(t => Equals(t["name"], "list_operations"));
                var family = Inside(Inside(Inside(listOps, "inputSchema"), "properties"), "family");
                var familyText = Convert.ToString(Inside(family, "description")) ?? "";
                var listed = Regex.Matches(familyText.Substring(familyText.IndexOf(':') + 1), "[a-z_]+").Cast<Match>().Select(m => m.Value).ToList();
                Check("list_operations names the families of the catalogue, every one and no other",
                      families.Count > 0 && listed.Count == families.Count && !listed.Except(families).Any(),
                      "listed: " + string.Join(", ", listed) + "; catalogue: " + string.Join(", ", families));

                var italianKeys = new List<string>();
                foreach (var t in tools.OfType<Dictionary<string, object>>())
                    foreach (var text in Texts(t))
                        foreach (Match m in Regex.Matches(text, "\\{[a-z_]+: [^{}]*\\}"))
                            foreach (Match k in Regex.Matches(m.Value, "(?<=[{,] ?)([a-z_]+):"))
                                if (IsItalianArgument(k.Groups[1].Value)) italianKeys.Add(t["name"] + ": «" + m.Value + "»");
                Check("an object written in the description of a tool has the keys of the protocol",
                      italianKeys.Count == 0, italianKeys.Count == 0 ? "no key is an Italian argument of the map" : string.Join("; ", italianKeys.Distinct().Take(5)));
            }
            finally { Close(p); }
        }

        private static List<string> FamiliesOfTheCatalogue()
        {
            var file = Path.Combine(Root(), "Tools", "catalog.json");
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var c = ser.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
            var f = c != null && c.ContainsKey("families") ? c["families"] as Dictionary<string, object> : null;
            return f == null ? new List<string>() : f.Keys.ToList();
        }

        // The Italian argument names of the map that are not also an English one
        // (the second column of the argument rows, minus the third).
        private static HashSet<string> _italian;
        private static bool IsItalianArgument(string key)
        {
            if (_italian == null)
            {
                var it = new HashSet<string>(StringComparer.Ordinal);
                var en = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in File.ReadAllLines(Path.Combine(Root(), "Checks", "language", "name-map.tsv"), Encoding.UTF8))
                {
                    var c = line.Split('\t');
                    if (c.Length < 3 || c[0] != "argument") continue;
                    it.Add(c[1]);
                    en.Add(c[2]);
                }
                it.ExceptWith(en);
                _italian = it;
            }
            return _italian.Contains(key);
        }

        private static IEnumerable<string> Texts(object node)
        {
            var s = node as string;
            if (s != null) { yield return s; yield break; }
            var d = node as Dictionary<string, object>;
            if (d != null) { foreach (var v in d.Values) foreach (var x in Texts(v)) yield return x; yield break; }
            var a = node as object[];
            if (a != null) foreach (var v in a) foreach (var x in Texts(v)) yield return x;
        }
    }
}
