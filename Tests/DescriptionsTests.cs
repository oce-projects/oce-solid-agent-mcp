using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// TWO CATALOGUE DESCRIPTIONS THAT CONTRADICT EACH OTHER (entry Z-C15).
    ///
    /// On 24/9/2026 `model_dimensions` said it was «the only way to
    /// dimension that this catalogue offers», in the same catalogue in which
    /// `drawing_dimension` dimensions. The model receives the two sentences in the same
    /// `list_operations` response and has no way of knowing which is true.
    ///
    /// It is not a one-off, it is a family: a description written when
    /// an operation was alone ages the day a sister arrives,
    /// and nobody rereads it — whoever writes the sister reads their own.
    ///
    /// SO: a description that declares itself the ONLY one cannot stand in a
    /// family with two operations. If one is really the only one for a certain
    /// purpose, it says so with the purpose («the only one that imports the part's dimensions»),
    /// not with the whole family.
    /// </summary>
    internal static class DescriptionsTests
    {
        /// <summary>
        /// The phrases with which a description proclaims itself alone. Written in
        /// lowercase, and compared with the text in lowercase.
        ///
        /// There is NO «l'unico modo» on its own: `set_dimension` says «PRIMA
        /// l'unico modo era cancellare e rifare», which is history, not a
        /// claim. Seen on the first run of this check, which gave it red.
        /// </summary>
        private static readonly string[] Exclusive =
        {
            "unico modo di", "e' l'unico modo", "unica operazione", "l'unica che",
        };

        internal static void All()
        {
            Group("le descrizioni del catalogo non si smentiscono (Z-C15)");

            var catalog = Path.Combine(Root(), "bin", "catalog.json");
            if (!File.Exists(catalog))
                catalog = Path.Combine(Root(), "Tools", "catalog.json");
            if (!File.Exists(catalog))
            {
                Check("il catalogo servito c'e'", false, "manca " + catalog);
                return;
            }

            var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(catalog))
                         as Dictionary<string, object>;
            object raw;
            var entries = root != null && root.TryGetValue("operations", out raw)
                ? raw as object[] : null;
            if (entries == null)
            {
                Check("il catalogo ha le operazioni", false, "«operations» non e' un elenco");
                return;
            }

            var perFamily = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var texts = new List<Tuple<string, string, string>>();
            foreach (var v in entries)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n, f, d, s;
                o.TryGetValue("name", out n);
                o.TryGetValue("family", out f);
                o.TryGetValue("details", out d);
                o.TryGetValue("summary", out s);
                string itemName = Convert.ToString(n), family = Convert.ToString(f) ?? "";
                if (string.IsNullOrEmpty(itemName)) continue;
                List<string> l;
                if (!perFamily.TryGetValue(family, out l)) perFamily[family] = l = new List<string>();
                l.Add(itemName);
                texts.Add(Tuple.Create(itemName, family,
                    (Convert.ToString(d) + " " + Convert.ToString(s)).ToLowerInvariant()));
            }

            var contradictions = new List<string>();
            int exclusive = 0;
            foreach (var t in texts)
            {
                string sentence = Exclusive.FirstOrDefault(e => t.Item3.Contains(e));
                if (sentence == null) continue;
                exclusive++;
                var siblings = perFamily[t.Item2].Where(x => x != t.Item1).ToList();
                if (siblings.Count > 0)
                    contradictions.Add(t.Item1 + " dice «" + sentence + "», e in «" + t.Item2 + "» ci sono anche " +
                                 string.Join(", ", siblings.Take(4)) + (siblings.Count > 4 ? "..." : ""));
            }

            Check("nessuna descrizione si dichiara l'unica in una famiglia che non lo e'",
                  contradictions.Count == 0,
                  contradictions.Count == 0
                      ? texts.Count + " descrizioni lette, " + exclusive + " si dicono uniche e sono sole davvero"
                      : string.Join(" | ", contradictions));
        }
    }
}
