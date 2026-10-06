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
    /// THE DESCRIPTIONS OF THE TOOLS AGREE WITH THEIR SCHEMAS (W3.1-bis.5, rewritten in W3.1-bis.11).
    ///
    /// Over stdio, from `tools/list`:
    ///   1. the families `list_operations` takes are the families of the
    ///      catalogue, every one and no other;
    ///   2. an object written in a description ({step: 1, kind: ...}) has only
    ///      keys that exist somewhere in the protocol: a property of a tool, of
    ///      an operation of the catalogue, or a key of the references between
    ///      steps. A key that is nowhere is a name that went stale.
    ///
    /// The first form of this contract compared the descriptions with the name
    /// map of the rename (the Italian values and keys of before). The map is
    /// archived: that the descriptions hold no Italian is now the language gate's
    /// job (`json_values` of Tools/catalog.json at 0).
    /// </summary>
    internal static class DescriptionValuesContract
    {
        internal static void RunAll()
        {
            Group("the descriptions of the tools agree with their schemas (W3.1-bis.5, W3.1-bis.11)");
            Tools();
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
                    "\"clientInfo\":{\"name\":\"tests\",\"version\":\"1\"}}}");
                var list = Inside(Obj(Ask(p, 2, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}")), "result") as Dictionary<string, object>;
                var tools = Inside(list, "tools") as object[];
                if (tools == null) { Check("the bridge lists its tools", false, "no result.tools"); return; }

                var catalogue = Catalogue();
                var families = catalogue == null ? new List<string>() : FamiliesOf(catalogue);
                var listOps = tools.OfType<Dictionary<string, object>>().FirstOrDefault(t => Equals(t["name"], "list_operations"));
                var family = Inside(Inside(Inside(listOps, "inputSchema"), "properties"), "family");
                var familyText = Convert.ToString(Inside(family, "description")) ?? "";
                var listed = Regex.Matches(familyText.Substring(familyText.IndexOf(':') + 1), "[a-z_]+").Cast<Match>().Select(m => m.Value).ToList();
                Check("list_operations names the families of the catalogue, every one and no other",
                      families.Count > 0 && listed.Count == families.Count && !listed.Except(families).Any(),
                      "listed: " + string.Join(", ", listed) + "; catalogue: " + string.Join(", ", families));

                // every property name of the protocol: the tools and the catalogue
                var known = new HashSet<string>(StringComparer.Ordinal);
                foreach (var t in tools.OfType<Dictionary<string, object>>()) PropertyNames(t, known);
                if (catalogue != null) PropertyNames(catalogue, known);

                var stale = new List<string>();
                foreach (var t in tools.OfType<Dictionary<string, object>>())
                    foreach (var text in Texts(t))
                        foreach (Match m in Regex.Matches(text, "\\{[a-z_]+: [^{}]*\\}"))
                            foreach (Match k in Regex.Matches(m.Value, "(?<=[{,] ?)([a-z_]+):"))
                                if (!known.Contains(k.Groups[1].Value)) stale.Add(t["name"] + ": «" + k.Groups[1].Value + "» in «" + m.Value + "»");
                Check("an object written in the description of a tool has only keys the protocol has",
                      stale.Count == 0,
                      stale.Count == 0 ? known.Count + " property names known" : string.Join("; ", stale.Distinct().Take(5)));
            }
            finally { Close(p); }
        }

        private static Dictionary<string, object> Catalogue()
        {
            var file = Path.Combine(Root(), "Tools", "catalog.json");
            if (!File.Exists(file)) { Check("the catalogue is there", false, "missing " + file); return null; }
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return ser.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
        }

        private static List<string> FamiliesOf(Dictionary<string, object> catalogue)
        {
            var f = catalogue.ContainsKey("families") ? catalogue["families"] as Dictionary<string, object> : null;
            return f == null ? new List<string>() : f.Keys.ToList();
        }

        // Every key of every "properties" object, at any depth, plus the keys of "required".
        private static void PropertyNames(object node, HashSet<string> into)
        {
            var d = node as Dictionary<string, object>;
            if (d != null)
            {
                foreach (var kv in d)
                {
                    if (kv.Key == "properties" && kv.Value is Dictionary<string, object>)
                        foreach (var name in ((Dictionary<string, object>)kv.Value).Keys) into.Add(name);
                    PropertyNames(kv.Value, into);
                }
                return;
            }
            var a = node as object[];
            if (a != null) foreach (var v in a) PropertyNames(v, into);
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
