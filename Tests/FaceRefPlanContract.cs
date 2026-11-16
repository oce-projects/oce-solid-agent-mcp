using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Program;
using static Shared.McpClient;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// A FACE THAT THE SAME PLAN CREATES IS NAMED WITH `face_ref`, THE SAME WAY EVERYWHERE
    /// (bridge 0.131.0).
    ///
    /// ── THE FACT IT IS BORN FROM ──────────────────────────────────────────
    ///
    /// The live case of 12/10/2026: «a 150x150x5 plate with its number cut through». On
    /// an empty part the face to write on does not exist when the plan is written, and
    /// `engrave_text` took only `face_uid`: two plans, a reading of the state between
    /// them. `hole_wizard` (and six more) already took `face_ref: {step, kind}`.
    ///
    /// ── WHAT IS HELD HERE ─────────────────────────────────────────────────
    ///
    /// 1. every `face_ref` of the catalogue has the SHAPE of `hole_wizard`'s (an object, `step`
    ///    and `kind` both required, the same kinds), and `engrave_text`'s is the same text,
    ///    word for word (it comes from the one helper, Operations.FaceRefSchema): the model
    ///    learns the argument once;
    /// 2. the operations that name a face with a uid have the reference twin or a stated
    ///    reason (the list below), and the ones that gained it in 0.131.0 keep it;
    /// 3. the plans that were impossible hold on the REAL bridge, over stdio, with SOLIDWORKS
    ///    closed (the dry run): the plate and its text in one plan, the fillet between two
    ///    faces of two steps, the draft of the sides of a block that the plan makes;
    /// 4. the weight is FINDABLE from where the model looks for it (D9 task 12: 0 of 3
    ///    sessions found `mass_properties`, which lives in `checks`, and read the weight from
    ///    `inspect_part`).
    /// </summary>
    internal static class FaceRefPlanContract
    {
        /// <summary>
        /// The operations that take a face (or edge) by identifier and have NO reference
        /// twin, with the reason. Not a list of oversights: «evaluate one by one, add where the
        /// face can be named before it exists» (item 3 of the work) was answered here. A new
        /// operation with a `*_uid` face argument and no twin turns the check red until it is
        /// added to the schema or written here with its reason.
        /// </summary>
        private static readonly Dictionary<string, string> NoTwin =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "sketched_bend.face_uid",
                  "the face of a sheet metal flange: base_flange declares no extrusion direction, and its " +
                  "large faces run ALONG it, so top_face/bottom_face (the end faces) would name the wrong " +
                  "ones; and start_mm/end_mm are model coordinates read from the state" },
                { "weld_bead.start_face_uid", "faces of two bars of a frame: no kind names «the face where bar A meets bar B»" },
                { "weld_bead.end_face_uid", "idem" },
                { "gusset.faces_uid", "idem: the two faces of two bars that form a corner" },
                { "cosmetic_thread.edge_uid", "it attaches to an EDGE (the rim of a hole), not to a face: face_ref does not apply and no kind names a rim" },
                { "variable_fillet.edge_uid", "an edge, not a face; the plural edges_ref of fillet_edges/chamfer_edges has no kind for one edge" },
                { "edge_flange.edge_uid", "an edge of a sheet metal part, not a face" },
                { "hem.edge_uid", "an edge of a sheet metal part, not a face" },
                { "check_body_geometry.body_uid", "a check: it reads what exists, there is no plan to name it in" },
                { "check_overhangs.body_uid", "idem" },
                { "mass_properties.body_uid", "idem" },
                { "body_bounding_box.body_uid", "idem" },
                { "dimension_scheme.references_uid", "three faces for a DimXpert scheme on a part that already exists" },
                { "edit_extrusion.feature_uid", "a feature, not a face" },
                { "reorder_feature.feature_uid", "a feature, not a face" },
                { "suppress_feature.feature_uid", "a feature, not a face" },
                { "delete_feature.feature_uid", "a feature, not a face" },
                { "suppress_instance.feature_uid", "a feature, not a face" },
            };

        internal static void RunAll()
        {
            Group("a face of the same plan is named with face_ref, one shape everywhere (0.131.0)");

            var operations = Served(out string why);
            if (operations == null) { Check("the served catalogue reads", false, why); return; }

            TheShapeIsOne(operations);
            TheTwinsAreThere(operations);
            TheRealBuildAcceptsThem(operations);
            ThePlansHold();
            TheWeightIsFindable(operations);
        }

        // ── 1. ONE SHAPE ─────────────────────────────────────────────────────

        private static void TheShapeIsOne(List<Dictionary<string, object>> operations)
        {
            var hole = Property(operations, "hole_wizard", "face_ref");
            Check("hole_wizard has the face_ref the others are held to", hole != null, "");
            if (hole == null) return;

            // Structure only: the seven hand-copied blocks of before 0.131.0 differ in wording.
            string reference = Structure(hole);

            var bad = new List<string>();
            int seen = 0;
            foreach (var op in operations)
            {
                var one = Property(op, "face_ref");
                if (one == null) continue;
                seen++;
                if (Structure(one) != reference)
                    bad.Add(Convert.ToString(Get(op, "name")));
            }
            Check("every face_ref of the catalogue has hole_wizard's shape (" + seen + " of them)",
                  bad.Count == 0 && seen >= 8, string.Join(", ", bad));

            var engrave = Property(operations, "engrave_text", "face_ref");
            Check("engrave_text takes face_ref", engrave != null, "");
            if (engrave != null)
            {
                Check("and it is hole_wizard's, word for word (one helper, no copy)",
                      Convert.ToString(Get(engrave, "description")) == Convert.ToString(Get(hole, "description")) &&
                      Structure(engrave) == reference,
                      "the helper Operations.FaceRefSchema has drifted from hole_wizard's text");
            }

            // The two faces of fillet_face and the neutral face of draft: the same object, the
            // wall of a hole allowed for the fillet (it works on a curved face), not for the draft.
            string withWall = reference + ",hole_face";
            foreach (var pair in new[] { Tuple.Create("fillet_face", "face_1_ref", true),
                                         Tuple.Create("fillet_face", "face_2_ref", true),
                                         Tuple.Create("draft", "neutral_face_ref", false) })
            {
                var p = Property(operations, pair.Item1, pair.Item2);
                Check(pair.Item1 + "." + pair.Item2 + " is a {step, kind} reference like the others" +
                      (pair.Item3 ? " (plus the wall of a hole)" : ""),
                      p != null && Structure(p) == (pair.Item3 ? withWall : reference),
                      p == null ? "missing" : Structure(p));
            }

            var faces = Property(operations, "draft", "faces_ref");
            var items = faces == null ? null : Get(faces, "items") as Dictionary<string, object>;
            var kinds = items == null ? null
                : Get(Get(Get(items, "properties") as Dictionary<string, object>, "kind") as Dictionary<string, object>, "enum") as object[];
            Check("draft.faces_ref is a list of {step, kind}, with the one plural kind «side_faces»",
                  kinds != null && kinds.Contains("side_faces") &&
                  Get(Get(items, "properties") as Dictionary<string, object>, "step") != null &&
                  (Get(items, "required") as object[] ?? new object[0]).Length == 2,
                  kinds == null ? "missing" : string.Join(",", kinds));
        }

        /// <summary>type, required and the kinds, written as one string.</summary>
        private static string Structure(Dictionary<string, object> reference)
        {
            var props = Get(reference, "properties") as Dictionary<string, object>;
            var kind = Get(props, "kind") as Dictionary<string, object>;
            var step = Get(props, "step") as Dictionary<string, object>;
            var required = (Get(reference, "required") as object[] ?? new object[0]).Select(Convert.ToString).OrderBy(x => x);
            var kinds = (Get(kind, "enum") as object[] ?? new object[0]).Select(Convert.ToString);
            return Convert.ToString(Get(reference, "type")) +
                   "|required:" + string.Join(",", required) +
                   "|step:" + Convert.ToString(Get(step, "type")) +
                   "|kind:" + Convert.ToString(Get(kind, "type")) + ":" + string.Join(",", kinds);
        }

        // ── 2. THE TWINS ──────────────────────────────────────────────────────

        private static void TheTwinsAreThere(List<Dictionary<string, object>> operations)
        {
            // Every `*_uid` argument that names a face (the wording «face»/«faces» in its name)
            // either has its `*_ref` twin or is in NoTwin with a reason.
            var withoutTwin = new List<string>();
            var staleReasons = new List<string>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var op in operations)
            {
                string name = Convert.ToString(Get(op, "name"));
                var props = Get(Get(op, "schema") as Dictionary<string, object>, "properties") as Dictionary<string, object>;
                if (props == null) continue;
                foreach (var key in props.Keys)
                {
                    if (!key.EndsWith("_uid", StringComparison.Ordinal) && !key.EndsWith("_uids", StringComparison.Ordinal)) continue;
                    string twin = key.Substring(0, key.LastIndexOf('_')) + "_ref";
                    if (props.ContainsKey(twin)) continue;
                    string id = name + "." + key;
                    if (NoTwin.ContainsKey(id)) { used.Add(id); continue; }
                    // the uid arguments of the drawing, assembly and check families name other things
                    // (components, views, sheets): only the ones that look like a face or an edge matter
                    if (key.IndexOf("face", StringComparison.Ordinal) >= 0 ||
                        key.IndexOf("edge", StringComparison.Ordinal) >= 0)
                        withoutTwin.Add(id);
                }
            }
            Check("every face/edge uid argument has a reference twin, or a written reason why not",
                  withoutTwin.Count == 0, string.Join(", ", withoutTwin));
            foreach (var id in NoTwin.Keys)
                if (!used.Contains(id)) staleReasons.Add(id);
            Check("and no reason in the list is stale (the argument has no twin and still exists)",
                  staleReasons.Count == 0, string.Join(", ", staleReasons));

            foreach (var id in new[] { "engrave_text.face_ref", "fillet_face.face_1_ref", "fillet_face.face_2_ref",
                                       "draft.neutral_face_ref", "draft.faces_ref" })
            {
                var parts = id.Split('.');
                Check("0.131.0: " + id + " is in the catalogue",
                      Property(operations, parts[0], parts[1]) != null, "");
            }

            // The engraving: both new modes are declared, and a text that is cut through and
            // raised at once is told apart from what is allowed.
            var engrave = Properties(operations, "engrave_text");
            Check("engrave_text declares through_all and centered (booleans)",
                  engrave != null &&
                  Convert.ToString(Get(Get(engrave, "through_all") as Dictionary<string, object>, "type")) == "boolean" &&
                  Convert.ToString(Get(Get(engrave, "centered") as Dictionary<string, object>, "type")) == "boolean",
                  "");
        }

        // ── 2b. THE REAL BUILD ACCEPTS THEM, NOT ONLY THE DRY RUN ────────────
        //
        // THE TRAP THIS BLOCK EXISTS FOR (found on 12/10/2026, writing 0.131.0). The dry run
        // knows that `x_uid` and `x_ref` are twins: a plan with `face_ref` and no `face_uid`
        // «holds». The real `build` goes through Bridge/Shape.Check, which does NOT know the twins:
        // it refuses any plan that lacks a field listed in `required`. The three operations had
        // `face_uid` in `required`, so the dry run said «the plan holds» and the build said
        // «face_uid is needed, and it is missing». The seven operations that already took face_ref
        // do not list their face_uid as required, and that is the rule: the library refuses a
        // face named by neither, saying what to give.
        private static void TheRealBuildAcceptsThem(List<Dictionary<string, object>> operations)
        {
            // 1. the rule, for the whole catalogue: a required field never has its twin
            var bad = new List<string>();
            foreach (var op in operations)
            {
                string name = Convert.ToString(Get(op, "name"));
                var schema = Get(op, "schema") as Dictionary<string, object>;
                var props = Get(schema, "properties") as Dictionary<string, object>;
                foreach (var field in (Get(schema, "required") as object[] ?? new object[0]).Select(Convert.ToString))
                {
                    string twin = field.EndsWith("_uid", StringComparison.Ordinal) ? field.Substring(0, field.Length - 4) + "_ref"
                                : field.EndsWith("_ref", StringComparison.Ordinal) ? field.Substring(0, field.Length - 4) + "_uid"
                                : null;
                    if (twin != null && props != null && props.ContainsKey(twin)) bad.Add(name + "." + field);
                }
            }
            Check("no operation lists as required a field that has its _uid/_ref twin (the real build would refuse the twin)",
                  bad.Count == 0, string.Join(", ", bad));

            // 2. the Shape of the BUILT bridge, called as «build» calls it
            var executable = BridgeOrNotHere("the real build accepts a plan that names its faces with face_ref");
            if (executable == null) return;
            System.Reflection.MethodInfo check;
            try
            {
                var type = Load.Class(Load.Bridge(executable), "Shape");
                check = type == null ? null : type.GetMethod("Check",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            }
            catch (Exception ex) { Check("the bridge loads", false, ex.ToString()); return; }
            if (check == null) { Check("Shape.Check is there", false, ""); return; }

            Func<string, string, string> shape = (operation, json) =>
            {
                var args = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                var schema = Get(operations.First(o => Convert.ToString(Get(o, "name")) == operation), "schema") as Dictionary<string, object>;
                try { return (string)check.Invoke(null, new object[] { operation, schema, args }); }
                catch (System.Reflection.TargetInvocationException ex) { return "THREW " + (ex.InnerException ?? ex).Message; }
            };
            string r1 = shape("engrave_text",
                "{\"text\":\"35\",\"face_ref\":{\"step\":1,\"kind\":\"top_face\"},\"through_all\":true,\"centered\":true,\"height_mm\":20}");
            Check("Shape.Check: engrave_text with face_ref, through_all and centered passes", r1 == null, r1 ?? "accepted");
            string r2 = shape("fillet_face",
                "{\"face_1_ref\":{\"step\":1,\"kind\":\"top_face\"},\"face_2_ref\":{\"step\":2,\"kind\":\"hole_face\"},\"radius_mm\":3}");
            Check("Shape.Check: fillet_face with two references passes", r2 == null, r2 ?? "accepted");
            string r3 = shape("draft",
                "{\"neutral_face_ref\":{\"step\":1,\"kind\":\"top_face\"},\"faces_ref\":[{\"step\":1,\"kind\":\"side_faces\"}],\"angle_deg\":5}");
            Check("Shape.Check: draft with neutral_face_ref and faces_ref passes", r3 == null, r3 ?? "accepted");

            // The control: the same Shape still refuses a kind that is not in the enum, so the three
            // greens above are not a Shape that accepts anything.
            string wrongKind = shape("engrave_text",
                "{\"text\":\"35\",\"face_ref\":{\"step\":1,\"kind\":\"side_faces\"}}");
            Check("control: engrave_text refuses a kind that is not its own («side_faces» is the draft's)",
                  wrongKind != null, wrongKind ?? "accepted: the Shape checks nothing here");
        }

        // ── 3. THE PLANS THAT WERE IMPOSSIBLE HOLD ───────────────────────────

        private static void ThePlansHold()
        {
            var executable = BridgeOrNotHere("the plans with face_ref on the real bridge");
            if (executable == null) return;
            var p = Open(executable);
            if (p == null) { Check("the bridge starts", false, "it did not start"); return; }
            try
            {
                Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"tests\",\"version\":\"1\"}}}");

                string plate =
                    "{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"top\"," +
                    "\"length_mm\":150,\"width_mm\":150,\"thickness_mm\":5,\"centered\":true}}";

                string number = DryRun(p, 20,
                    plate + ",{\"operation\":\"engrave_text\",\"arguments\":{\"text\":\"35\"," +
                    "\"face_ref\":{\"step\":1,\"kind\":\"top_face\"},\"height_mm\":20," +
                    "\"through_all\":true,\"centered\":true}}");
                Check("the plate and its number cut through, centred, are ONE plan that holds",
                      number.Contains("the plan holds"), Short(number));

                string engraved = DryRun(p, 21,
                    plate + ",{\"operation\":\"engrave_text\",\"arguments\":{\"text\":\"SN-0042\"," +
                    "\"face_ref\":{\"step\":1,\"kind\":\"top_face\"},\"x_mm\":-30,\"y_mm\":-5}}");
                Check("the plate and its text engraved to a depth are one plan too",
                      engraved.Contains("the plan holds"), Short(engraved));

                string future = DryRun(p, 22,
                    plate + ",{\"operation\":\"engrave_text\",\"arguments\":{\"text\":\"35\"," +
                    "\"face_ref\":{\"step\":4,\"kind\":\"top_face\"}}}");
                Check("a face_ref to a step that is not done yet is still caught, on engrave_text too",
                      future.Contains("«face_ref» refers to step 4") &&
                      future.Contains("has not been done yet"), Short(future));

                string fillet = DryRun(p, 23,
                    "{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"front\"," +
                    "\"length_mm\":100,\"width_mm\":100,\"thickness_mm\":20,\"centered\":true}}," +
                    "{\"operation\":\"cylindrical_cut\",\"arguments\":{\"face_ref\":{\"step\":1,\"kind\":\"top_face\"}," +
                    "\"x_mm\":0,\"y_mm\":0,\"diameter_mm\":20,\"through_all\":true}}," +
                    "{\"operation\":\"fillet_face\",\"arguments\":{\"face_1_ref\":{\"step\":1,\"kind\":\"top_face\"}," +
                    "\"face_2_ref\":{\"step\":2,\"kind\":\"hole_face\"},\"radius_mm\":3}}");
                Check("the fillet between the top of a plate and the wall of its hole, both from the plan, holds",
                      fillet.Contains("the plan holds"), Short(fillet));

                string draft = DryRun(p, 24,
                    "{\"operation\":\"extrude_rectangle\",\"arguments\":{\"plane\":\"front\"," +
                    "\"length_mm\":100,\"width_mm\":100,\"thickness_mm\":20,\"centered\":true}}," +
                    "{\"operation\":\"draft\",\"arguments\":{\"neutral_face_ref\":{\"step\":1,\"kind\":\"top_face\"}," +
                    "\"faces_ref\":[{\"step\":1,\"kind\":\"side_faces\"}],\"angle_deg\":5}}");
                Check("the draft of the sides of a block that the plan makes holds",
                      draft.Contains("the plan holds"), Short(draft));

                // The control: a name that is NOT in the schema is still reported, so the checks above
                // are not green because the dry run stopped looking.
                string typo = DryRun(p, 25,
                    plate + ",{\"operation\":\"engrave_text\",\"arguments\":{\"text\":\"35\"," +
                    "\"face_reference\":{\"step\":1,\"kind\":\"top_face\"}}}");
                Check("control: a misspelt face_reference is reported as an argument that would be IGNORED",
                      typo.Contains("face_reference") &&
                      typo.IndexOf("IGNORED", StringComparison.OrdinalIgnoreCase) >= 0, Short(typo));
            }
            finally { Close(p); }
        }

        private static string DryRun(Bridge p, int id, string steps)
        {
            var answer = Obj(Ask(p, id,
                "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":" +
                "{\"name\":\"build\",\"arguments\":{\"dry_run\":true,\"steps\":[" + steps + "]}}}"));
            return TextOf(Inside(answer, "result") as Dictionary<string, object>) ?? "";
        }

        // ── 4. THE WEIGHT IS FINDABLE ────────────────────────────────────────

        private static void TheWeightIsFindable(List<Dictionary<string, object>> operations)
        {
            var mass = operations.FirstOrDefault(o => Convert.ToString(Get(o, "name")) == "mass_properties");
            Check("mass_properties is in the catalogue and stays in «checks» (the family «measure» accepts)",
                  mass != null && Convert.ToString(Get(mass, "family")) == "checks", "");
            if (mass == null) return;
            Check("its summary says WEIGHT: it is the word the model looks for",
                  Convert.ToString(Get(mass, "summary")).IndexOf("weight", StringComparison.OrdinalIgnoreCase) >= 0,
                  Convert.ToString(Get(mass, "summary")));

            // The family text (served with the catalogue, and the way list_operations reads it).
            var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(ServedCatalog())) as Dictionary<string, object>;
            var families = Get(root, "families") as Dictionary<string, object>;
            string purpose = Convert.ToString(Get(families, "physical_properties"));
            Check("the family «physical_properties» says that the weight is read by mass_properties",
                  purpose.Contains("mass_properties") && purpose.IndexOf("weight", StringComparison.OrdinalIgnoreCase) >= 0,
                  purpose);

            var material = operations.FirstOrDefault(o => Convert.ToString(Get(o, "name")) == "apply_material");
            string materialText = material == null ? "" :
                Convert.ToString(Get(material, "summary")) + " " + Convert.ToString(Get(material, "details"));
            Check("apply_material points to mass_properties for the weight afterwards",
                  materialText.Contains("mass_properties") && materialText.Contains("measure"),
                  "the operation the model finds when it looks for the weight must name the one that reads it");

            var executable = BridgeOrNotHere("list_operations on the real bridge names mass_properties under physical_properties");
            if (executable == null) return;
            var p = Open(executable);
            if (p == null) { Check("the bridge starts", false, "it did not start"); return; }
            try
            {
                Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"tests\",\"version\":\"1\"}}}");
                var listed = Obj(Ask(p, 30,
                    "{\"jsonrpc\":\"2.0\",\"id\":30,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"list_operations\",\"arguments\":{\"family\":\"physical_properties\"}}}"));
                string text = TextOf(Inside(listed, "result") as Dictionary<string, object>) ?? "";
                Check("list_operations with family physical_properties names mass_properties",
                      text.Contains("mass_properties"), Short(text));
            }
            finally { Close(p); }
        }

        // ── reading the catalogue ────────────────────────────────────────────

        private static List<Dictionary<string, object>> Served(out string why)
        {
            why = null;
            var file = ServedCatalog();
            if (!File.Exists(file)) { why = "missing " + file; return null; }
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                    .DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
                var list = Get(root, "operations") as object[];
                if (list == null) { why = "«operations» is not a list in " + file; return null; }
                return list.OfType<Dictionary<string, object>>().ToList();
            }
            catch (Exception ex) { why = ex.Message; return null; }
        }

        private static Dictionary<string, object> Properties(List<Dictionary<string, object>> operations, string name)
        {
            var op = operations.FirstOrDefault(o => Convert.ToString(Get(o, "name")) == name);
            return Get(Get(op, "schema") as Dictionary<string, object>, "properties") as Dictionary<string, object>;
        }

        private static Dictionary<string, object> Property(List<Dictionary<string, object>> operations, string name, string property)
        {
            return Get(Properties(operations, name), property) as Dictionary<string, object>;
        }

        private static Dictionary<string, object> Property(Dictionary<string, object> op, string property)
        {
            var props = Get(Get(op, "schema") as Dictionary<string, object>, "properties") as Dictionary<string, object>;
            return Get(props, property) as Dictionary<string, object>;
        }

        private static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && key != null && d.TryGetValue(key, out v) ? v : null;
        }
    }
}
