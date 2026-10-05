using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using static Oce.Solid.Agent.ContractTests.Program;
using static Shared.McpClient;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE NAMES OF BEFORE THE RENAME, ACCEPTED ON INPUT (W3.1-bis.5, decision D4).
    ///
    /// Two ways in, both without SOLIDWORKS doing anything:
    ///
    ///   1. the bridge over stdio, with a DRY RUN (it touches no document): the
    ///      tool, its arguments, the operation and the arguments and values of a
    ///      step written in Italian must give the dry run of the English plan,
    ///      and the answer must list every old name in `deprecated`; the
    ///      catalogue tools asked with an Italian family and operation answer
    ///      in English only;
    ///   2. the library by reflection (Oce.Solid.Agent.Core.Alias): an operation
    ///      step, a word of two senses (`a_mm` in the weld symbol and in the
    ///      section line), a value only into a value the schema lists, and a
    ///      selector; a free-text argument left alone (mate's variant) while
    ///      the facings of mate are translated; the URIs of the resources of
    ///      before; and the deprecated line kept on a step that worked with no
    ///      measurements (SwOperations.AttachDeprecated). Over stdio, a
    ///      resource read with the URI of before.
    ///
    /// The Italian names are read from the name map (Checks/language/name-map.tsv)
    /// by their English name, not written here: the contract is about the
    /// dictionary the bridge embeds, not about a list of its own.
    ///
    /// Seen red with the dictionary taken out of the library (the embedded map
    /// removed from Oce.Solid.Agent.Core.csproj): «Alias.Problem» and every
    /// translation check fail. REMOVED IN W3.1-bis.11 with the aliases.
    /// </summary>
    internal static class AliasLayerContract
    {
        internal static void RunAll()
        {
            Group("the Italian names of before the rename, accepted on input until the first release (W3.1-bis.5, D4)");

            var map = ReadMap();
            if (map == null) return;
            Func<string, string, string> it = (type, en) =>
            {
                string v;
                return map.TryGetValue(type + "\t" + en, out v) ? v : null;
            };

            Library(it);
            Bridge(it);
        }

        // English name -> Italian name, per type, from the map (the first row wins).
        private static Dictionary<string, string> ReadMap()
        {
            var file = Path.Combine(Root(), "Checks", "language", "name-map.tsv");
            if (!File.Exists(file)) { Check("the name map is there", false, "missing " + file); return null; }
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                if (c.Length < 3 || c[1] == c[2]) continue;
                var key = c[0] + "\t" + c[2];
                if (!map.ContainsKey(key)) map[key] = c[1];
            }
            return map;
        }

        // ── the library, by reflection ───────────────────────────────────

        private static void Library(Func<string, string, string> it)
        {
            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the aliases of the library", library + " is missing: build the library first (build.ps1)");
                return;
            }
            Type alias;
            try
            {
                var redist = WhereTheInteropIs();
                if (redist != null)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += (s, a) =>
                    {
                        var name = new AssemblyName(a.Name).Name;
                        if (!name.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase)) return null;
                        var f = Path.Combine(redist, name + ".dll");
                        return File.Exists(f) ? Assembly.LoadFrom(f) : null;
                    };
                }
                var asm = Assembly.Load(File.ReadAllBytes(library));
                alias = asm.GetType("Oce.Solid.Agent.Core.Alias", false);
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (alias == null) { Check("the library has the aliases", false, "type Oce.Solid.Agent.Core.Alias not found"); return; }

            var problem = Convert.ToString(alias.GetProperty("Problem").GetValue(null));
            Check("the library reads its dictionary (the embedded name map)", string.IsNullOrEmpty(problem),
                  string.IsNullOrEmpty(problem) ? "read" : problem);

            var step = alias.GetMethod("Step");
            var selector = alias.GetMethod("Selector");

            // an operation and its arguments, values included
            string opIt = it("operation", "hole_wizard"), variantIt = it("argument", "variant"), sizeIt = it("argument", "size");
            string throughIt = it("argument", "through_all");
            string clearanceIt = ValueOf(it, "variant", "clearance");
            var args = new Dictionary<string, object>
            {
                [variantIt ?? "?"] = clearanceIt ?? "?",
                [sizeIt ?? "?"] = "M10",
                [throughIt ?? "?"] = true,
            };
            var call = new object[] { opIt, args };
            var notes = step.Invoke(null, call) as List<string>;
            Check("an Italian operation becomes the English one", (string)call[0] == "hole_wizard",
                  "«" + opIt + "» -> «" + call[0] + "»");
            Check("its Italian arguments the arguments of its schema",
                  args.ContainsKey("variant") && args.ContainsKey("size") && args.ContainsKey("through_all") && args.Count == 3,
                  string.Join(", ", args.Keys));
            Check("and an Italian value the value the schema lists",
                  Equals(args.ContainsKey("variant") ? args["variant"] : null, "clearance"),
                  "variant = " + (args.ContainsKey("variant") ? args["variant"] : "(none)"));
            Check("every old name is in the deprecated lines", notes != null && notes.Count == 5 &&
                  notes.All(n => n.Contains("is deprecated, use")),
                  notes == null ? "(none)" : notes.Count + ": " + Short(string.Join(" | ", notes)));

            // the English names go through untouched, and leave no line
            var english = new Dictionary<string, object> { ["variant"] = "clearance", ["size"] = "M10" };
            var call2 = new object[] { "hole_wizard", english };
            var none = step.Invoke(null, call2) as List<string>;
            Check("an English step is left as it is, with no deprecated line",
                  none != null && none.Count == 0 && (string)call2[0] == "hole_wizard" && english.Count == 2, "");

            // a word of two senses: the schema of the operation decides
            string weldIt = it("operation", "drawing_weld_symbol"), sectionIt = it("operation", "section_view");
            var weld = new Dictionary<string, object> { ["a_mm"] = 5 };
            step.Invoke(null, new object[] { weldIt, weld });
            var section = new Dictionary<string, object> { ["a_mm"] = 5 };
            step.Invoke(null, new object[] { sectionIt, section });
            Check("«a_mm» is the throat in the weld symbol and the offset in the section line",
                  weld.ContainsKey("throat_mm") && section.ContainsKey("offset_mm"),
                  "weld: " + string.Join(",", weld.Keys) + "; section: " + string.Join(",", section.Keys));

            // a value only into a value the schema lists
            string partIt = ValueOf(it, "variant", "part");
            var odd = new Dictionary<string, object> { ["variant"] = partIt };
            step.Invoke(null, new object[] { "hole_wizard", odd });
            Check("a value is not translated into one the schema does not list",
                  partIt != null && Equals(odd["variant"], partIt), "«" + partIt + "» is a value of the document tool, not of hole_wizard: variant = " + odd["variant"]);

            // a selector of the bench
            string faceIt = it("selector", "@face"), frontIt = it("selector", "normal=front");
            var old = faceIt + "(" + frontIt + ")";
            var selNotes = new List<string>();
            var sel = selector.Invoke(null, new object[] { old, selNotes }) as string;
            Check("an Italian selector becomes the English one", sel == "@face(normal=front)" && selNotes.Count == 1,
                  "«" + old + "» -> «" + sel + "»");

            // an argument without an enum takes free text: a word that is a value
            // of another operation's argument of the same name stays as written
            // (mate's variant is free; «fillet» is a value of the weld symbol)
            string filletIt = ValueOf(it, "variant", "fillet");
            var free = new Dictionary<string, object> { ["variant"] = filletIt };
            var freeNotes = step.Invoke(null, new object[] { "mate", free }) as List<string>;
            Check("a free-text argument keeps a word that is an enum value elsewhere",
                  filletIt != null && Equals(free["variant"], filletIt) && freeNotes != null && freeNotes.Count == 0,
                  "mate variant = " + free["variant"] + (freeNotes == null ? "" : ", " + freeNotes.Count + " deprecated lines"));
            // ... but the facings of mate are values, though no enum lists them
            string frontFacing = frontIt == null ? null : frontIt.Substring(frontIt.IndexOf('=') + 1);
            var facing = new Dictionary<string, object> { ["face_a"] = frontFacing };
            step.Invoke(null, new object[] { "mate", facing });
            Check("the facings of mate are translated, though no enum lists them",
                  Equals(facing["face_a"], "front"), "face_a = " + facing["face_a"]);

            // the URIs of the catalogue resources of before
            var resourceUri = alias.GetMethod("ResourceUri");
            string slotIt = it("operation", "slot");
            string oldList = OldUri("operazioni"), oldOne = OldUri("operazione/");
            var uriNotes = new List<string>();
            var listUri = resourceUri == null ? null : resourceUri.Invoke(null, new object[] { oldList, uriNotes }) as string;
            var oneUri = resourceUri == null ? null : resourceUri.Invoke(null, new object[] { oldOne + slotIt, uriNotes }) as string;
            var mixedUri = resourceUri == null ? null : resourceUri.Invoke(null, new object[] { "catalog://operation/" + slotIt, uriNotes }) as string;
            Check("an old URI of the resources is read as the English one, with a deprecated line each",
                  listUri == "catalog://operations" && oneUri == "catalog://operation/slot" && mixedUri == "catalog://operation/slot" && uriNotes.Count == 3,
                  listUri + ", " + oneUri + ", " + mixedUri + "; " + uriNotes.Count + " lines");

            // a step that worked and left no measurements keeps its warning
            var ops = alias.Assembly.GetType("Oce.Solid.Agent.Core.SwOperations", false);
            var outcomeType = alias.Assembly.GetType("Oce.Solid.Agent.Core.SwOperations+Outcome", false);
            var attach = ops == null ? null : ops.GetMethod("AttachDeprecated");
            if (attach == null || outcomeType == null) { Check("the library attaches the deprecated lines to an outcome", false, "SwOperations.AttachDeprecated or Outcome not found"); return; }
            var outcome = Activator.CreateInstance(outcomeType);
            outcomeType.GetField("Succeeded").SetValue(outcome, true);
            outcomeType.GetField("Measurements").SetValue(outcome, null);
            attach.Invoke(null, new object[] { outcome, new List<string> { "«x» is deprecated, use «y»" } });
            var measured = outcomeType.GetField("Measurements").GetValue(outcome) as Dictionary<string, object>;
            Check("a step that worked with no measurements keeps its deprecated line",
                  measured != null && measured.ContainsKey("deprecated"),
                  measured == null ? "measurements still null: the warning is lost" : string.Join(", ", measured.Keys));
        }

        // An old URI of the resources (Mcp/Risorse.cs before window 2): the map
        // has no row for the URIs, so they are written here.
        private static string OldUri(string tail) { return "catalogo://" + tail; }

        // The Italian value of an English argument=value, from the map.
        private static string ValueOf(Func<string, string, string> it, string argument, string value)
        {
            var row = it("enum_value", argument + "=" + value);
            if (row == null) return null;
            int eq = row.IndexOf('=');
            return eq < 0 ? null : row.Substring(eq + 1);
        }

        // ── the bridge, over stdio ───────────────────────────────────────

        private static void Bridge(Func<string, string, string> it)
        {
            var exe = BridgeOrNotHere("the aliases of the bridge");
            if (exe == null) return;
            var p = Open(exe);
            if (p == null) { Check("the bridge starts", false, "it did not start"); return; }
            try
            {
                Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"prove\",\"version\":\"1\"}}}");

                // a dry run, all in Italian: the tool, its arguments, the step
                string buildIt = it("tool", "build"), stepsIt = it("argument", "steps");
                string operationIt = it("argument", "operation"), argumentsIt = it("argument", "arguments");
                string opIt = it("operation", "extrude_rectangle"), planeIt = it("argument", "plane");
                string lengthIt = it("argument", "length_mm"), widthIt = it("argument", "width_mm");
                string thicknessIt = it("argument", "thickness_mm"), topIt = ValueOf(it, "plane", "top");
                var plan = "{\"jsonrpc\":\"2.0\",\"id\":40,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"" + buildIt + "\",\"arguments\":{\"dry_run\":true,\"" + stepsIt + "\":[" +
                    "{\"" + operationIt + "\":\"" + opIt + "\",\"" + argumentsIt + "\":{\"" + planeIt + "\":\"" + topIt + "\"," +
                    "\"" + lengthIt + "\":100,\"" + widthIt + "\":50,\"" + thicknessIt + "\":10}}]}}}";
                var answer = Inside(Obj(Ask(p, 40, plan)), "result") as Dictionary<string, object>;
                var text = AllText(answer);
                Check("an Italian tool, plan and step give the dry run of the English plan",
                      text.IndexOf("PROVA A VUOTO", StringComparison.OrdinalIgnoreCase) >= 0 &&
                      text.IndexOf("non esiste", StringComparison.OrdinalIgnoreCase) < 0 &&
                      !Equals(Inside(answer, "isError"), true),
                      Short(text));
                var expected = new[] { buildIt, stepsIt, operationIt, argumentsIt, opIt, planeIt, topIt, lengthIt, widthIt, thicknessIt };
                var missing = expected.Where(n => text.IndexOf("«" + n + "» is deprecated", StringComparison.Ordinal) < 0).ToList();
                Check("and the answer lists every old name in «deprecated»", missing.Count == 0,
                      missing.Count == 0 ? expected.Length + " names" : "not listed: " + string.Join(", ", missing));
                var structured = Inside(answer, "structuredContent") as Dictionary<string, object>;
                if (structured != null)
                    Check("structuredContent carries them too", Inside(structured, "deprecated") is object[], "");

                // the catalogue tools: Italian in, English out
                string listIt = it("tool", "list_operations"), familyIt = it("argument", "family");
                string featuresIt = it("catalogue_family", "features");
                var list = Inside(Obj(Ask(p, 41,
                    "{\"jsonrpc\":\"2.0\",\"id\":41,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"" + listIt + "\",\"arguments\":{\"" + familyIt + "\":\"" + featuresIt + "\"}}}")), "result") as Dictionary<string, object>;
                var listText = AllText(list);
                Check("an Italian family lists the English operations of that family",
                      listText.Contains("fillet_edges") && listText.Contains("«" + featuresIt + "» is deprecated"), Short(listText));

                string describeIt = it("tool", "describe_operation"), nameIt = it("argument", "name");
                string slotIt = it("operation", "slot");
                var described = Inside(Obj(Ask(p, 42,
                    "{\"jsonrpc\":\"2.0\",\"id\":42,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"" + describeIt + "\",\"arguments\":{\"" + nameIt + "\":\"" + slotIt + "\"}}}")), "result") as Dictionary<string, object>;
                var describedText = AllText(described);
                Check("an Italian operation is described, in English only",
                      describedText.Contains("slot") && !Equals(Inside(described, "isError"), true),
                      Short(describedText));
                Check("and the description carries no Italian name of its schema",
                      describedText.IndexOf("\"" + it("argument", "length_mm") + "\"", StringComparison.Ordinal) < 0,
                      "no «" + it("argument", "length_mm") + "» key");

                // a resource read with the URI of before
                var resource = Inside(Obj(Ask(p, 43,
                    "{\"jsonrpc\":\"2.0\",\"id\":43,\"method\":\"resources/read\",\"params\":" +
                    "{\"uri\":\"" + OldUri("operazione/") + slotIt + "\"}}")), "result") as Dictionary<string, object>;
                var contents = Inside(resource, "contents") as object[];
                var first = contents != null && contents.Length > 0 ? contents[0] : null;
                Check("a resource asked with the URI of before is the English one, and says so in «deprecated»",
                      Equals(Inside(first, "uri"), "catalog://operation/slot") && Inside(resource, "deprecated") is object[] &&
                      Convert.ToString(Inside(first, "text")).Contains("\"name\""),
                      "uri " + Inside(first, "uri") + ", deprecated " + (Inside(resource, "deprecated") is object[] ? "listed" : "missing"));
            }
            finally { Close(p); }
        }

        // Every text item of an answer: the deprecated lines are the last one.
        private static string AllText(Dictionary<string, object> answer)
        {
            var content = Inside(answer, "content") as object[];
            if (content == null) return "";
            return string.Join("\n", content.Select(c => Convert.ToString(Inside(c, "text")) ?? ""));
        }
    }
}
