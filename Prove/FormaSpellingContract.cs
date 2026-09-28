using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// THE FORMA HANDS EVERY ENUM VALUE ON IN THE CATALOGUE'S SPELLING
    /// (Z-C21.10, pattern S20).
    ///
    /// Ponte/Forma.cs accepts an enum value ignoring case: the case of a word
    /// is form, not merit. Until library 0.140.0 it then passed the value on
    /// untouched, and the library compares Ordinal, so the accepted
    /// «Superiore» became an unknown name there: SelezionaPiano fell back to
    /// the front plane (finding N33, the plate on the wrong plane with
    /// riuscita:true) and telaio_rettangolare read «Ingombro» as the
    /// centreline frame (finding N32, 1080x880 for 1000x800). The fix is one
    /// rewrite in the Forma, in the very dictionary that «disegna» then hands
    /// to the library (Strumenti/Attrezzi.cs).
    ///
    /// This group runs the Forma of the BUILT bridge, by reflection, against
    /// the schemas of the served catalogue: no SOLIDWORKS. The library half of
    /// the fix (an unknown plane name is refused, never the front plane) needs
    /// the CAD and is the bench case
    /// `estrusioni/unknown_plane_does_not_fall_back_to_front`.
    /// </summary>
    internal static class FormaSpellingContract
    {
        internal static void RunAll()
        {
            Gruppo("the Forma rewrites enum values in the catalogue's spelling (Z-C21.10, S20)");

            var bridge = PonteOppureNonQui("the Forma rewrites enum values in the catalogue's spelling, in full");
            if (bridge == null) return;

            MethodInfo check;
            try
            {
                var type = Carica.Classe(Carica.Ponte(bridge), "Forma");
                if (type == null) { Segna("the Forma class is there", false, ""); return; }
                check = type.GetMethod("Controlla",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (check == null) { Segna("Forma.Controlla is there", false, ""); return; }
            }
            catch (Exception ex) { Segna("the bridge loads", false, ex.ToString()); return; }

            var operations = ServedOperations(out string unreadable);
            if (operations == null) { Segna("the served catalogue reads", false, unreadable); return; }

            // The refusal, or null; the arguments are rewritten in place, as
            // «disegna» sees them. An exception comes back as a refusal that
            // says so: a Forma that throws (say, writing into the dictionary it
            // is walking) must turn these checks red, not stop the whole run.
            Func<string, Dictionary<string, object>, Dictionary<string, object>, string> forma =
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
                (operation, args) => forma(operation, SchemaOf(operations, operation), args);

            // ── THE CASE OF THE FINDING: «Superiore» ────────────────────────
            var plate = Plate("Superiore");
            var refusal = onCatalogue("estrudi_rettangolo", plate);
            Segna("estrudi_rettangolo: «Superiore» passes the Forma, as it always did",
                  refusal == null, refusal ?? "accepted");
            Segna("and reaches the library as «superiore», not «Superiore» (N33)",
                  Same(plate["piano"], "superiore"), "piano = " + Show(plate["piano"]));

            var allCaps = Plate("DESTRO");
            onCatalogue("estrudi_rettangolo", allCaps);
            Segna("«DESTRO» reaches the library as «destro»",
                  Same(allCaps["piano"], "destro"), "piano = " + Show(allCaps["piano"]));

            var right = Plate("superiore");
            refusal = onCatalogue("estrudi_rettangolo", right);
            Segna("the catalogue's own spelling is accepted and left as it is",
                  refusal == null && Same(right["piano"], "superiore"),
                  (refusal ?? "accepted") + ", piano = " + Show(right["piano"]));

            // ── THE CATALOGUE'S SPELLING, WHICH IS NOT ALWAYS LOWER CASE ────
            var sheet = new Dictionary<string, object> { ["formato"] = "a4v" };
            onCatalogue("crea_disegno", sheet);
            var step = new Dictionary<string, object> { ["protocollo"] = "ap214" };
            onCatalogue("esporta_step", step);
            Segna("«a4v» becomes «A4V» and «ap214» «AP214»: the catalogue's spelling, not lower case",
                  Same(sheet["formato"], "A4V") && Same(step["protocollo"], "AP214"),
                  "formato = " + Show(sheet["formato"]) + ", protocollo = " + Show(step["protocollo"]));

            // ── N32 THROUGH THE BRIDGE, AND A FREE TEXT LEFT ALONE ──────────
            var frame = new Dictionary<string, object>
            {
                ["profilo_percorso"] = "profiles/square.SLDLFP",
                ["configurazione"] = "80X80X5",
                ["lunghezza_mm"] = 1000,
                ["larghezza_mm"] = 800,
                ["misure"] = "Ingombro",
            };
            refusal = onCatalogue("telaio_rettangolare", frame);
            Segna("telaio_rettangolare: «Ingombro» reaches the library as «ingombro» (N32)",
                  refusal == null && Same(frame["misure"], "ingombro"),
                  (refusal ?? "accepted") + ", misure = " + Show(frame["misure"]));
            Segna("a string that is not an enum is never rewritten («80X80X5» stays)",
                  Same(frame["configurazione"], "80X80X5"),
                  "configurazione = " + Show(frame["configurazione"]));

            // ── INSIDE A NESTED OBJECT ──────────────────────────────────────
            var face = new Dictionary<string, object> { ["passo"] = 1, ["genere"] = "Faccia_Superiore" };
            var hole = new Dictionary<string, object>
            {
                ["x_mm"] = 0, ["y_mm"] = 0, ["diametro_mm"] = 10, ["faccia_rif"] = face,
            };
            refusal = onCatalogue("taglio_cilindrico", hole);
            Segna("taglio_cilindrico: «faccia_rif.genere» «Faccia_Superiore» becomes «faccia_superiore»",
                  refusal == null && Same(face["genere"], "faccia_superiore"),
                  (refusal ?? "accepted") + ", genere = " + Show(face["genere"]));

            // ── WHAT IS NOT IN THE LIST IS STILL REFUSED, AND NOT TOUCHED ───
            var unknown = Plate("obliquo");
            refusal = onCatalogue("estrudi_rettangolo", unknown);
            Segna("«obliquo» is still refused, and the refusal names it",
                  refusal != null && refusal.Contains("«obliquo»"), refusal ?? "accepted");
            Segna("and a refused value is not rewritten",
                  Same(unknown["piano"], "obliquo"), "piano = " + Show(unknown["piano"]));

            // ── TWO SPELLINGS OF ONE WORD LEAVE NOTHING TO CHOOSE ───────────
            //
            // No enum of the catalogue does this today (the sweep below would
            // say so); the branch is shown on a schema written to have it.
            var twoWays = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["modo"] = new Dictionary<string, object>
                    {
                        ["type"] = "string", ["enum"] = new object[] { "abc", "ABC" },
                    },
                },
            };
            var ambiguous = new Dictionary<string, object> { ["modo"] = "Abc" };
            refusal = forma("finta", twoWays, ambiguous);
            Segna("«Abc» against «abc» and «ABC» is refused, not guessed",
                  refusal != null && Same(ambiguous["modo"], "Abc"),
                  (refusal ?? "accepted") + ", modo = " + Show(ambiguous["modo"]));

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
                var name = Convert.ToString(Get(op, "nome"));
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
                        var r = forma(name, only, args);
                        if (r != null || !Same(args[p.Key], spelled))
                            wrong.Add(name + "." + p.Key + " «" + flipped + "» -> " +
                                      (r != null ? "refused: " + r : Show(args[p.Key])));
                    }
                }
            }
            // Twelve are enough to see the pattern; on library 0.140.0 all 180
            // of them fail, and a red line of 180 entries is read by nobody.
            Segna("every top-level enum value, case flipped, comes back in the catalogue's spelling",
                  values > 0 && wrong.Count == 0,
                  wrong.Count == 0
                      ? values + " values"
                      : wrong.Count + " of " + values + ": " + string.Join(" | ", wrong.Take(12)) +
                        (wrong.Count > 12 ? " | ..." : ""));
        }

        private static Dictionary<string, object> Plate(string plane) =>
            new Dictionary<string, object>
            {
                ["piano"] = plane,
                ["lunghezza_mm"] = 120,
                ["larghezza_mm"] = 80,
                ["spessore_mm"] = 10,
                ["centrato"] = true,
            };

        private static List<Dictionary<string, object>> ServedOperations(out string why)
        {
            why = null;
            var file = CatalogoServito();
            if (!File.Exists(file)) { why = "missing " + file; return null; }
            try
            {
                var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file))
                           as Dictionary<string, object>;
                var list = Get(root, "operazioni") as object[];
                if (list == null) { why = "«operazioni» is not a list in " + file; return null; }
                return list.OfType<Dictionary<string, object>>().ToList();
            }
            catch (Exception ex) { why = ex.Message; return null; }
        }

        private static Dictionary<string, object> SchemaOf(
            List<Dictionary<string, object>> operations, string name)
        {
            var op = operations.FirstOrDefault(o =>
                string.Equals(Convert.ToString(Get(o, "nome")), name, StringComparison.Ordinal));
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
