using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// A PLAN THAT CITES NOTHING OF THE PART NEEDS NO READING (brief R18). On
    /// 24/10/2026 a session called «document» and «inspect_part» 19 times for 16
    /// plans: after every «document» the bridge forgot which document was in
    /// front, although it had just put it there, and after a change by hand it
    /// stopped every plan, also one that cites no face. Now «document» keeps the
    /// document it put in front, a plan that cites no identifier runs on it, and
    /// one that cites an identifier is still sent to «inspect_part». These are
    /// the rule that tells a cited identifier, read by reflection on the built
    /// bridge, and the places that use it, read in the source.
    /// </summary>
    internal static class R18DocumentWithoutReadingContract
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("a plan that cites no identifier needs no reading (brief R18)");
            Cited();
            Wiring();
        }

        private static Dictionary<string, object> Step(string operation, params object[] pairs)
        {
            var arguments = new Dictionary<string, object>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) arguments[(string)pairs[i]] = pairs[i + 1];
            return new Dictionary<string, object> { ["operation"] = operation, ["arguments"] = arguments };
        }

        private static void Cited()
        {
            const string label = "the identifier a plan cites: «*_uid» keys, the shapes F3/C2/M1 under a key that names a face, a component, a mate; not a plane name, not a «*_ref», not «M6» as a size, not «S235» as a value";
            var bridge = Path.Combine(Root(), "bin", "oce-solid-agent-mcp.exe");
            if (!File.Exists(bridge)) { NotHere(label, bridge + " is missing: build first (build.ps1)"); return; }
            try
            {
                var tools = Load.Class(Load.Bridge(bridge), "McpTools");
                var rule = tools == null ? null : tools.GetMethod("FirstIdentifierCited", Any);
                if (rule == null) { Check(label, false, "McpTools.FirstIdentifierCited is not in this bridge (0.138.0 or older)"); return; }
                Func<object[], string> ask = steps => (string)rule.Invoke(null, new object[] { steps });
                var refStep = new Dictionary<string, object> { ["step"] = 1, ["kind"] = "top_face" };
                var rows = new[]
                {
                    Tuple.Create("a rectangle on top", new object[] { Step("extrude_rectangle", "plane", "top", "length_mm", 100, "width_mm", 50, "thickness_mm", 10) }, (string)null),
                    Tuple.Create("a property", new object[] { Step("set_property", "name", "Material", "value_text", "S235") }, (string)null),
                    Tuple.Create("a fillet by step reference", new object[] { Step("extrude_rectangle", "plane", "front"), Step("fillet_edges", "edges_ref", refStep, "radius_mm", 1) }, (string)null),
                    Tuple.Create("an M6 hole by size", new object[] { Step("hole_wizard", "size", "M6", "plane", "top") }, (string)null),
                    Tuple.Create("an edge by uid", new object[] { Step("fillet_edges", "edges_uid", new object[] { "E45" }, "radius_mm", 1) }, "E45"),
                    Tuple.Create("a feature by uid", new object[] { Step("delete_feature", "feature_uid", "Cut-Extrude2") }, "Cut-Extrude2"),
                    Tuple.Create("a mate on two components", new object[] { Step("mate", "component_a", "C1", "face_a", "up", "component_b", "C2") }, "C1"),
                    Tuple.Create("a face in the second step", new object[] { Step("set_property", "name", "A", "value_text", "B"), Step("sketch_on_face", "face", "F3") }, "F3"),
                    Tuple.Create("an empty uid", new object[] { Step("fillet_edges", "edges_uid", new object[] { " " }) }, (string)null),
                    Tuple.Create("a steel named C45 («material» is not «mate»)", new object[] { Step("apply_material", "material", "C45") }, (string)null),
                    Tuple.Create("faces in a list", new object[] { Step("shell", "faces", new object[] { "F1", "F4" }) }, "F1"),
                    Tuple.Create("a sketch on a face given as plane", new object[] { Step("extrude_rectangle", "plane", "F2", "length_mm", 10) }, "F2"),
                };
                var wrong = new List<string>();
                foreach (var r in rows)
                {
                    string got = ask(r.Item2);
                    if (got != r.Item3) wrong.Add(r.Item1 + ": «" + (got ?? "null") + "» instead of «" + (r.Item3 ?? "null") + "»");
                }
                if (ask(null) != null) wrong.Add("a null plan cites something");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? rows.Length + " plans read as expected" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message); }
        }

        private static void Wiring()
        {
            const string label = "«document» keeps the document it put in front; «build» refuses only a plan that cites an identifier; a change by hand is said, not a stop, for a plan that cites none";
            var source = Path.Combine(Root(), "Tools", "McpTools.cs");
            if (!File.Exists(source)) { NotHere(label, source + " is missing"); return; }
            var text = File.ReadAllText(source);
            var wanted = new[]
            {
                Tuple.Create("«document» keeps the identity", "_seenByDocument = _seen;"),
                Tuple.Create("only when the card says in front", "putInFront"),
                Tuple.Create("the plan is read", "string identifierCited = FirstIdentifierCited(steps);"),
                Tuple.Create("the refusal names the identifier", "since «document» brought it to the front"),
                Tuple.Create("the change is said", "result[\"warning_part_changed\"] = partChangedNote;"),
                Tuple.Create("the X2 stop needs an identifier", "if (changed != null && identifierCited == null)"),
            };
            var missing = new List<string>();
            foreach (var w in wanted) if (text.IndexOf(w.Item2, StringComparison.Ordinal) < 0) missing.Add(w.Item1);
            Check(label, missing.Count == 0, missing.Count == 0 ? wanted.Length + " places found" : "missing: " + string.Join("; ", missing));
        }
    }
}
