using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// DUE DESCRIZIONI DEL CATALOGO CHE SI SMENTISCONO (voce Z-C15).
    ///
    /// Il 24/9/2026 `model_dimensions` diceva di essere «l'unico modo di
    /// quotare che questo catalogo offre», nello stesso catalogo in cui
    /// `drawing_dimension` quota. Il modello riceve le due frasi nella stessa
    /// risposta di `list_operations` e non ha modo di sapere quale e' vera.
    ///
    /// Non e' un caso, e' una famiglia: una descrizione scritta quando
    /// un'operazione era sola invecchia il giorno in cui ne arriva una sorella,
    /// e nessuno la rilegge — chi scrive la sorella legge la propria.
    ///
    /// QUINDI: una descrizione che si dichiara l'UNICA non puo' stare in una
    /// famiglia con due operazioni. Se l'una e' davvero l'unica per un certo
    /// scopo, lo dice con lo scopo («l'unica che importa le quote del pezzo»),
    /// non con la famiglia intera.
    /// </summary>
    internal static class DescriptionsTests
    {
        /// <summary>
        /// Le frasi con cui una descrizione si proclama sola. Scritte in
        /// minuscolo, e confrontate col testo in minuscolo.
        ///
        /// NON c'e' «l'unico modo» da solo: `set_dimension` dice «PRIMA
        /// l'unico modo era cancellare e rifare», che e' storia, non una
        /// pretesa. Visto al primo giro di questo controllo, che lo dava rosso.
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
