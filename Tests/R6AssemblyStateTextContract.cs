using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE ASSEMBLY STATE SAYS WHAT IS TRUE (brief R6). On 25/10/2026 the state of
    /// 26M000002 said «inserting components, mating them … are not operations
    /// yet. The positions of the components are not in this state» while both
    /// existed, and that SOLIDWORKS answers «unavailable» on every component
    /// while C2 had a number beside it. The sentence on what can be done is now
    /// built from the catalog, and the one on the degrees of freedom names the
    /// components it could not read. These checks read both on the built library
    /// by reflection, and the source for the old sentences.
    /// </summary>
    internal static class R6AssemblyStateTextContract
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the assembly state says what is true: what can be done from the catalog, the components not read by name (brief R6)");
            Source();
            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the sentences of the assembly state", library + " is missing: build the library first (build.ps1)");
                return;
            }
            Assembly core;
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
                core = Assembly.Load(File.ReadAllBytes(library));
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            Names(core.GetType("Oce.Solid.Agent.Core.Catalog.Operations", false));
            // The assembly half of DocumentState lives in AssemblyState.cs.
            Sentences(core.GetType("Oce.Solid.Agent.Core.DocumentState", false));
        }

        private static void Source()
        {
            const string label = "the old sentences are gone from AssemblyState.cs, and the new one is built from the catalog";
            var file = Path.Combine(Root(), "Oce.Solid.Agent.Core", "AssemblyState.cs");
            if (!File.Exists(file)) { Check(label, false, file + " is missing"); return; }
            var text = File.ReadAllText(file);
            var wrong = new List<string>();
            foreach (var old in new[] { "are not operations yet", "are not in this state", "on every component, fixed or free" })
                if (text.Contains(old)) wrong.Add("still says «" + old + "»");
            if (!Regex.IsMatch(text, @"using\s+Oce\.Solid\.Agent\.Core\.Catalog\s*;") ||
                !Regex.IsMatch(text, @"WhatCanBeDoneText\s*\(\s*Operations\.NamesFor\s*\(\s*""assembly""\s*\)\s*\)"))
                wrong.Add("«what_can_be_done» is not built from Catalog.Operations.NamesFor(\"assembly\")");
            if (!Regex.IsMatch(text, @"DegreesNotGivenText\s*\(\s*degreesNotGiven\s*,"))
                wrong.Add("the sentence on the degrees of freedom is not built from the components not read");
            Check(label, wrong.Count == 0, wrong.Count == 0 ? "three old sentences absent, two builders called" : string.Join("; ", wrong));
        }

        private static void Names(Type operations)
        {
            const string label = "NamesFor(\"assembly\") gives what the exported catalog says runs on an assembly, and nothing else";
            if (operations == null) { Check(label, false, "Catalog.Operations not found"); return; }
            try
            {
                var m = operations.GetMethod("NamesFor", Any);
                if (m == null) { Check(label, false, "NamesFor is not in this library (0.157.0 or older)"); return; }
                var given = new List<string>((List<string>)m.Invoke(null, new object[] { "assembly" }));
                var json = Path.Combine(Root(), "Tools", "catalog.json");
                var catalog = (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(File.ReadAllText(json));
                var expected = new List<string>();
                foreach (Dictionary<string, object> op in (IEnumerable)catalog["operations"])
                {
                    var doc = op.ContainsKey("document") ? op["document"] as string : null;
                    if (doc == null) continue;
                    if (doc == "any" || Array.IndexOf(doc.Split('/'), "assembly") >= 0) expected.Add((string)op["name"]);
                }
                var wrong = new List<string>();
                foreach (var n in expected) if (!given.Contains(n)) wrong.Add("missing " + n);
                foreach (var n in given) if (!expected.Contains(n)) wrong.Add("extra " + n);
                foreach (var must in new[] { "insert_component", "place_component", "mate", "delete_mate", "save_document" })
                    if (!given.Contains(must)) wrong.Add("not listed: " + must);
                foreach (var never in new[] { "extrude_profile", "create_plane", "title_block" })
                    if (given.Contains(never)) wrong.Add("listed but it does not run on an assembly: " + never);
                var parts = (List<string>)m.Invoke(null, new object[] { "part" });
                if (parts.Contains("mate") || !parts.Contains("extrude_profile")) wrong.Add("NamesFor(\"part\") is wrong too: mate " + parts.Contains("mate") + ", extrude_profile " + parts.Contains("extrude_profile"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? given.Count + " names, the same as the exported catalog" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message); }
        }

        private static void Sentences(Type state)
        {
            const string label = "the sentences: the components not read named with their state, «any of the N» only when none was read, the operations listed";
            if (state == null) { Check(label, false, "DocumentState not found"); return; }
            try
            {
                var degrees = state.GetMethod("DegreesNotGivenText", Any);
                var can = state.GetMethod("WhatCanBeDoneText", Any);
                if (degrees == null || can == null) { Check(label, false, "DegreesNotGivenText or WhatCanBeDoneText is not in this library (0.157.0 or older)"); return; }
                var wrong = new List<string>();
                var one = (string)degrees.Invoke(null, new object[] { new List<string> { "C1 (root component)" }, 2 });
                if (!one.Contains("of C1 (root component):") || one.Contains("any of")) wrong.Add("one of two: " + one);
                var all = (string)degrees.Invoke(null, new object[] { new List<string> { "C1 (unavailable)", "C2 (unavailable)" }, 2 });
                if (!all.Contains("any of the 2 components (C1 (unavailable), C2 (unavailable))")) wrong.Add("two of two: " + all);
                foreach (var kept in new[] { "unavailable", "at least one mate", "three of the six" })
                    if (!one.Contains(kept)) wrong.Add("the bench reads «" + kept + "» and it is gone");
                var listed = (string)can.Invoke(null, new object[] { new List<string> { "mate", "place_component" } });
                if (!listed.Contains("mate, place_component") || !listed.Contains("origin_mm") || !listed.Contains("axes")) wrong.Add("operations: " + listed);
                var none = (string)can.Invoke(null, new object[] { new List<string>() });
                if (!none.Contains("no operation")) wrong.Add("empty catalog: " + none);
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "5 sentences as expected" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message); }
        }
    }
}
