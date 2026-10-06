using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;
using static Shared.McpClient;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE BRIDGE KNOWS ONE NAME FOR EVERYTHING (W3.1-bis.11, decision D4).
    ///
    /// Until the first release the bridge translated, on the way in, the Italian
    /// names the surface had before the rename (tools, operations, arguments,
    /// values, selectors, resource URIs). The dictionary is gone: a name that
    /// is not the English one is an unknown name, and is treated as every other
    /// unknown name. Two ways to see it, neither of which needs SOLIDWORKS:
    ///
    ///   1. the library, by reflection: there is no type that translates names,
    ///      and no name map embedded in the assembly;
    ///   2. the bridge over stdio, with a dry run (it touches no document): an
    ///      unknown tool is `isError` and lists the tools there are, an unknown
    ///      operation is refused as unknown, an argument the schema does not
    ///      have is reported as ignored (never moved to another argument), an
    ///      unknown resource URI is an error, and no answer carries a
    ///      `deprecated` line, because nothing is deprecated any more.
    ///
    /// The names asked are not in the catalogue, whatever language they look
    /// like: the proof is that the bridge has no table to look them up in.
    /// </summary>
    internal static class NoAliasContract
    {
        internal static void RunAll()
        {
            Group("an old or foreign name is an unknown name: no alias is translated on input (W3.1-bis.11, D4)");
            Library();
            Bridge();
        }

        // ── the library, by reflection ───────────────────────────────────

        private static void Library()
        {
            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the library has no alias layer", library + " is missing: build the library first (build.ps1)");
                return;
            }
            Assembly asm;
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
                asm = Assembly.Load(File.ReadAllBytes(library));
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }

            Check("the library has no type that translates names (Oce.Solid.Agent.Core.Alias)",
                  !asm.GetTypes().Any(t => t.FullName == "Oce.Solid.Agent.Core.Alias"), "");
            var embedded = asm.GetManifestResourceNames().Where(n => n.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)).ToList();
            Check("and no name map is embedded in it", embedded.Count == 0,
                  embedded.Count == 0 ? "no .tsv resource" : string.Join(", ", embedded));
            var ops = asm.GetType("Oce.Solid.Agent.Core.SwOperations", false);
            Check("an outcome has no way to carry «deprecated» lines (SwOperations.AttachDeprecated is gone)",
                  ops != null && !ops.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                      .Any(m => m.Name == "AttachDeprecated"), "");
        }

        // ── the bridge, over stdio ───────────────────────────────────────

        private static void Bridge()
        {
            var exe = BridgeOrNotHere("the unknown names of the bridge");
            if (exe == null) return;
            var p = Open(exe);
            if (p == null) { Check("the bridge starts", false, "it did not start"); return; }
            try
            {
                Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"tests\",\"version\":\"1\"}}}");

                // an unknown tool
                var tool = Inside(Obj(Ask(p, 50,
                    "{\"jsonrpc\":\"2.0\",\"id\":50,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"construct_the_part\",\"arguments\":{\"steps\":[]}}}")), "result") as Dictionary<string, object>;
                var toolText = AllText(tool);
                Check("an unknown tool is refused (isError) and lists the tools there are",
                      Equals(Inside(tool, "isError"), true) && toolText.Contains("build") && toolText.Contains("inspect_part"),
                      Short(toolText));
                Check("and nothing is «deprecated»", NoDeprecated(tool), "");

                // a plan: an unknown operation, an unknown argument
                var plan = Inside(Obj(Ask(p, 51,
                    "{\"jsonrpc\":\"2.0\",\"id\":51,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"build\",\"arguments\":{\"dry_run\":true,\"steps\":[" +
                    "{\"operation\":\"extrude_a_rectangle\",\"arguments\":{\"length_mm\":100}}," +
                    "{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"top\",\"length_mm\":100,\"width_mm\":50,\"thickness_mm\":10,\"height_mm\":7}}" +
                    "]}}}")), "result") as Dictionary<string, object>;
                var planText = AllText(plan);
                Check("an unknown operation is refused as unknown",
                      planText.Contains("extrude_a_rectangle") && planText.Contains("does not exist"), Short(planText));
                Check("an argument the schema does not have is reported as ignored, not moved to another one",
                      planText.Contains("height_mm") && planText.IndexOf("IGNORED", StringComparison.OrdinalIgnoreCase) >= 0, Short(planText));
                Check("and no line of the answer says «deprecated»", NoDeprecated(plan), "");

                // an unknown resource
                var resource = Obj(Ask(p, 52,
                    "{\"jsonrpc\":\"2.0\",\"id\":52,\"method\":\"resources/read\",\"params\":" +
                    "{\"uri\":\"legacy://operations\"}}"));
                Check("a resource URI of another scheme is an error, not the list of operations",
                      resource != null && resource.ContainsKey("error") && !(Inside(resource, "result") is Dictionary<string, object>),
                      Short(Convert.ToString(Inside(resource, "error"))));
            }
            finally { Close(p); }
        }

        private static bool NoDeprecated(Dictionary<string, object> answer)
        {
            if (answer == null) return false;
            var structured = Inside(answer, "structuredContent") as Dictionary<string, object>;
            if (structured != null && structured.ContainsKey("deprecated")) return false;
            return AllText(answer).IndexOf("deprecated", StringComparison.OrdinalIgnoreCase) < 0;
        }

        // Every text item of an answer.
        private static string AllText(Dictionary<string, object> answer)
        {
            var content = Inside(answer, "content") as object[];
            if (content == null) return "";
            return string.Join("\n", content.Select(c => Convert.ToString(Inside(c, "text")) ?? ""));
        }
    }
}
