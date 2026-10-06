using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FORMA HANDS EVERY ENUM VALUE ON IN THE CATALOGUE'S SPELLING
    /// (Z-C21.10, pattern S20).
    ///
    /// Bridge/Shape.cs accepts an enum value ignoring case: the case of a word
    /// is form, not merit. Until library 0.140.0 it then passed the value on
    /// untouched, and the library compares Ordinal, so the accepted
    /// «Top» became an unknown name there: SelectPlane fell back to
    /// the front plane (finding N33, the plate on the wrong plane with
    /// succeeded:true) and rectangular_frame read «Overall» as the
    /// centreline frame (finding N32, 1080x880 for 1000x800). The fix is one
    /// rewrite in the Shape, in the very dictionary that «build» then hands
    /// to the library (Tools/McpTools.cs).
    ///
    /// This group runs the Forma of the BUILT bridge, by reflection, against
    /// the schemas of the served catalogue: no SOLIDWORKS. The library half of
    /// the fix (an unknown plane name is refused, never the front plane) needs
    /// the CAD and is the bench case
    /// `extrusions/unknown_plane_does_not_fall_back_to_front`.
    /// </summary>
    internal static class ShapeSpellingContract
    {
        internal static void RunAll()
        {
            Group("the Shape rewrites enum values in the catalogue's spelling (Z-C21.10, S20)");

            var bridge = BridgeOrNotHere("the Shape rewrites enum values in the catalogue's spelling, in full");
            if (bridge == null) return;

            MethodInfo check;
            try
            {
                var type = Load.Class(Load.Bridge(bridge), "Shape");
                if (type == null) { Check("the Shape class is there", false, ""); return; }
                check = type.GetMethod("Check",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (check == null) { Check("Shape.Check is there", false, ""); return; }
            }
            catch (Exception ex) { Check("the bridge loads", false, ex.ToString()); return; }

            var operations = ServedOperations(out string unreadable);
            if (operations == null) { Check("the served catalogue reads", false, unreadable); return; }

            // The refusal, or null; the arguments are rewritten in place, as
            // «build» sees them. An exception comes back as a refusal that
            // says so: a Forma that throws (say, writing into the dictionary it
            // is walking) must turn these checks red, not stop the whole run.
            Func<string, Dictionary<string, object>, Dictionary<string, object>, string> shape =
                (operation, schema, args) =>
                {
                    try { return (string)check.Invoke(null, new object[] { operation, schema, args }); }
                    catch (TargetInvocationException ex)
                    {
                        return "THREW " + (ex.InnerException ?? ex).GetType().Name + ": " +
                               (ex.InnerException ?? ex).Message;
                    }
                };
            Func<string, Dictionary<string, object>, string> onCatalogue =
                (operation, args) => shape(operation, SchemaOf(operations, operation), args);

            // ── THE CASE OF THE FINDING: «Top» ────────────────────────
            var plate = Plate("Top");
            var refusal = onCatalogue("extrude_rectangle", plate);
            Check("extrude_rectangle: «Top» passes the Shape, as it always did",
                  refusal == null, refusal ?? "accepted");
            Check("and reaches the library as «top», not «Top» (N33)",
                  Same(plate["plane"], "top"), "piano = " + Show(plate["plane"]));

            var allCaps = Plate("RIGHT");
            onCatalogue("extrude_rectangle", allCaps);
            Check("«RIGHT» reaches the library as «right»",
                  Same(allCaps["plane"], "right"), "piano = " + Show(allCaps["plane"]));

            var right = Plate("top");
            refusal = onCatalogue("extrude_rectangle", right);
            Check("the catalogue's own spelling is accepted and left as it is",
                  refusal == null && Same(right["plane"], "top"),
                  (refusal ?? "accepted") + ", piano = " + Show(right["plane"]));

            // ── THE CATALOGUE'S SPELLING, WHICH IS NOT ALWAYS LOWER CASE ────
            var sheet = new Dictionary<string, object> { ["paper_size"] = "a4v" };
            onCatalogue("create_drawing", sheet);
            var step = new Dictionary<string, object> { ["protocol"] = "ap214" };
            onCatalogue("export_step", step);
            Check("«a4v» becomes «A4V» and «ap214» «AP214»: the catalogue's spelling, not lower case",
                  Same(sheet["paper_size"], "A4V") && Same(step["protocol"], "AP214"),
                  "format = " + Show(sheet["paper_size"]) + ", protocol = " + Show(step["protocol"]));

            // ── N32 THROUGH THE BRIDGE, AND A FREE TEXT LEFT ALONE ──────────
            var frame = new Dictionary<string, object>
            {
                ["profile_path"] = "profiles/square.SLDLFP",
                ["configuration"] = "80X80X5",
                ["length_mm"] = 1000,
                ["width_mm"] = 800,
                ["measured_as"] = "Overall",
            };
            refusal = onCatalogue("rectangular_frame", frame);
            Check("rectangular_frame: «Overall» reaches the library as «overall» (N32)",
                  refusal == null && Same(frame["measured_as"], "overall"),
                  (refusal ?? "accepted") + ", measures = " + Show(frame["measured_as"]));
            Check("a string that is not an enum is never rewritten («80X80X5» stays)",
                  Same(frame["configuration"], "80X80X5"),
                  "configuration = " + Show(frame["configuration"]));

            // ── INSIDE A NESTED OBJECT ──────────────────────────────────────
            var face = new Dictionary<string, object> { ["step"] = 1, ["kind"] = "Top_Face" };
            var hole = new Dictionary<string, object>
            {
                ["x_mm"] = 0, ["y_mm"] = 0, ["diameter_mm"] = 10, ["face_ref"] = face,
            };
            refusal = onCatalogue("cylindrical_cut", hole);
            Check("cylindrical_cut: «face_ref.kind» «Top_Face» becomes «top_face»",
                  refusal == null && Same(face["kind"], "top_face"),
                  (refusal ?? "accepted") + ", kind = " + Show(face["kind"]));

            // ── WHAT IS NOT IN THE LIST IS STILL REFUSED, AND NOT TOUCHED ───
            var unknown = Plate("oblique");
            refusal = onCatalogue("extrude_rectangle", unknown);
            Check("«oblique» is still refused, and the refusal names it",
                  refusal != null && refusal.Contains("«oblique»"), refusal ?? "accepted");
            Check("and a refused value is not rewritten",
                  Same(unknown["plane"], "oblique"), "plane = " + Show(unknown["plane"]));

            // ── TWO SPELLINGS OF ONE WORD LEAVE NOTHING TO CHOOSE ───────────
            //
            // No enum of the catalogue does this today (the sweep below would
            // say so); the branch is shown on a schema written to have it.
            var twoWays = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["mode"] = new Dictionary<string, object>
                    {
                        ["type"] = "string", ["enum"] = new object[] { "abc", "ABC" },
                    },
                },
            };
            var ambiguous = new Dictionary<string, object> { ["mode"] = "Abc" };
            refusal = shape("fake", twoWays, ambiguous);
            Check("«Abc» against «abc» and «ABC» is refused, not guessed",
                  refusal != null && Same(ambiguous["mode"], "Abc"),
                  (refusal ?? "accepted") + ", mode = " + Show(ambiguous["mode"]));

            // ── EVERY TOP-LEVEL ENUM OF THE CATALOGUE ───────────────────────
            //
            // Each value with its case flipped, on a schema holding only that
            // property (so no other required field gets in the way): it must
            // come back spelled as the catalogue spells it. This is also what
            // would say that an enum has two values differing only in case.
            int values = 0;
            var wrong = new List<string>();
            foreach (var op in operations)
            {
                var name = Convert.ToString(Get(op, "name"));
                var properties = Get(Get(op, "schema") as Dictionary<string, object>, "properties")
                                 as Dictionary<string, object>;
                if (properties == null) continue;
                foreach (var p in properties)
                {
                    var rule = p.Value as Dictionary<string, object>;
                    var allowed = Get(rule, "enum") as IEnumerable;
                    if (allowed == null || allowed is string) continue;
                    foreach (var a in allowed)
                    {
                        var spelled = Convert.ToString(a);
                        var flipped = Flip(spelled);
                        if (flipped == spelled) continue;   // no letters to flip
                        values++;
                        var only = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object> { [p.Key] = rule },
                        };
                        var args = new Dictionary<string, object> { [p.Key] = flipped };
                        var r = shape(name, only, args);
                        if (r != null || !Same(args[p.Key], spelled))
                            wrong.Add(name + "." + p.Key + " «" + flipped + "» -> " +
                                      (r != null ? "refused: " + r : Show(args[p.Key])));
                    }
                }
            }
            // Twelve are enough to see the pattern; on library 0.140.0 all 180
            // of them fail, and a red line of 180 entries is read by nobody.
            Check("every top-level enum value, case flipped, comes back in the catalogue's spelling",
                  values > 0 && wrong.Count == 0,
                  wrong.Count == 0
                      ? values + " values"
                      : wrong.Count + " of " + values + ": " + string.Join(" | ", wrong.Take(12)) +
                        (wrong.Count > 12 ? " | ..." : ""));
        }

        private static Dictionary<string, object> Plate(string plane) =>
            new Dictionary<string, object>
            {
                ["plane"] = plane,
                ["length_mm"] = 120,
                ["width_mm"] = 80,
                ["thickness_mm"] = 10,
                ["centered"] = true,
            };

        private static List<Dictionary<string, object>> ServedOperations(out string why)
        {
            why = null;
            var file = ServedCatalog();
            if (!File.Exists(file)) { why = "missing " + file; return null; }
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file))
                           as Dictionary<string, object>;
                var list = Get(root, "operations") as object[];
                if (list == null) { why = "«operations» is not a list in " + file; return null; }
                return list.OfType<Dictionary<string, object>>().ToList();
            }
            catch (Exception ex) { why = ex.Message; return null; }
        }

        private static Dictionary<string, object> SchemaOf(
            List<Dictionary<string, object>> operations, string name)
        {
            var op = operations.FirstOrDefault(o =>
                string.Equals(Convert.ToString(Get(o, "name")), name, StringComparison.Ordinal));
            return Get(op, "schema") as Dictionary<string, object>;
        }

        private static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        private static bool Same(object value, string expected) =>
            string.Equals(value as string, expected, StringComparison.Ordinal);

        private static string Show(object value) => value == null ? "(null)" : "«" + value + "»";

        private static string Flip(string s) =>
            new string(s.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c)
                                   : char.IsLower(c) ? char.ToUpperInvariant(c) : c).ToArray());
    }
}
