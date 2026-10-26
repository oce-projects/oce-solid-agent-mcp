using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// WHERE A PROPERTY GOES, SAID (brief R8). On 25/10/2026 «set_property» wrote
    /// the drawing number on the document and answered that from the
    /// configuration «Default&lt;A macchina&gt;» it was NOT visible: two drawers,
    /// and only one step to fill one. «scope» names the drawer («file»,
    /// «configuration», «both»), and a name close to an argument («value» for
    /// «value_text») is said in the refusal instead of being taken: one name per
    /// argument. These are the rules of the two, read by reflection on the built
    /// library and bridge.
    /// </summary>
    internal static class R8PropertyScopeContract
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("where a property goes: «scope» of set_property, and the near name said (brief R8)");
            Scope();
            Near();
        }

        private static void Scope()
        {
            const string label = "«scope»: file, configuration and both taken; another word refused; «file» with a configuration named refused";
            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library)) { NotHere(label, library + " is missing: build the library first (build.ps1)"); return; }
            try
            {
                var redist = WhereTheInteropIs();
                if (redist != null)
                    AppDomain.CurrentDomain.AssemblyResolve += (s, a) =>
                    {
                        var name = new AssemblyName(a.Name).Name;
                        if (!name.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase)) return null;
                        var file = Path.Combine(redist, name + ".dll");
                        return File.Exists(file) ? Assembly.LoadFrom(file) : null;
                    };
                var operations = Assembly.Load(File.ReadAllBytes(library)).GetType("Oce.Solid.Agent.Core.SwOperations", false);
                var rule = operations == null ? null : operations.GetMethod("PropertyScopeProblem", Any);
                if (rule == null) { Check(label, false, "SwOperations.PropertyScopeProblem is not in this library (0.158.0 or older)"); return; }
                Func<string, bool, string> problem = (s, named) => (string)rule.Invoke(null, new object[] { s, named });
                var wrong = new List<string>();
                var taken = new[] { Tuple.Create((string)null, false), Tuple.Create((string)null, true), Tuple.Create("file", false),
                                    Tuple.Create("configuration", false), Tuple.Create("configuration", true),
                                    Tuple.Create("both", false), Tuple.Create("both", true) };
                foreach (var t in taken)
                    if (problem(t.Item1, t.Item2) != null) wrong.Add("refused " + (t.Item1 ?? "(none)") + (t.Item2 ? " with a configuration" : "") + ": " + problem(t.Item1, t.Item2));
                var refused = new[] { Tuple.Create("document", false), Tuple.Create("File", false), Tuple.Create("", false), Tuple.Create("file", true) };
                foreach (var t in refused)
                    if (problem(t.Item1, t.Item2) == null) wrong.Add("took «" + t.Item1 + "»" + (t.Item2 ? " with a configuration" : ""));
                var contradiction = problem("file", true) ?? "";
                if (!contradiction.Contains("«both»")) wrong.Add("the refusal of file + configuration does not say «both»: " + contradiction);
                Check(label, wrong.Count == 0, wrong.Count == 0 ? taken.Length + " taken, " + refused.Length + " refused" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message); }
        }

        private static void Near()
        {
            const string label = "an unknown argument names the near ones (value -> value_text, radius -> radius_mm, a typo), and never a far one";
            var bridge = Path.Combine(Root(), "bin", "oce-solid-agent-mcp.exe");
            if (!File.Exists(bridge)) { NotHere(label, bridge + " is missing: build first (build.ps1)"); return; }
            try
            {
                var tools = Load.Class(Load.Bridge(bridge), "McpTools");
                var near = tools == null ? null : tools.GetMethod("NearArguments", Any);
                if (near == null) { Check(label, false, "McpTools.NearArguments is not in this bridge (0.135.0 or older)"); return; }
                Func<string, string[], List<string>> ask = (g, names) => (List<string>)near.Invoke(null, new object[] { g, names });
                var wrong = new List<string>();
                var setProperty = new[] { "name", "value_text", "configuration", "scope" };
                var rows = new[]
                {
                    Tuple.Create("value", setProperty, "value_text"),
                    Tuple.Create("radius", new[] { "radius_mm", "depth_mm" }, "radius_mm"),
                    Tuple.Create("configuraton", setProperty, "configuration"),
                    Tuple.Create("depth_mm_total", new[] { "depth_mm", "width_mm" }, "depth_mm"),
                    Tuple.Create("colour", setProperty, ""),
                    Tuple.Create("x", new[] { "x_mm", "y_mm" }, "x_mm"),
                };
                foreach (var r in rows)
                {
                    var got = string.Join(",", ask(r.Item1, r.Item2));
                    if (got != r.Item3) wrong.Add(r.Item1 + ": «" + got + "» instead of «" + r.Item3 + "»");
                }
                Check(label, wrong.Count == 0, wrong.Count == 0 ? rows.Length + " names as expected" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message); }
        }
    }
}
