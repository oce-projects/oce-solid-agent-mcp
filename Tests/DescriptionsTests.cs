using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Programma;

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
    internal static class ProveLeDescrizioni
    {
        /// <summary>
        /// Le frasi con cui una descrizione si proclama sola. Scritte in
        /// minuscolo, e confrontate col testo in minuscolo.
        ///
        /// NON c'e' «l'unico modo» da solo: `set_dimension` dice «PRIMA
        /// l'unico modo era cancellare e rifare», che e' storia, non una
        /// pretesa. Visto al primo giro di questo controllo, che lo dava rosso.
        /// </summary>
        private static readonly string[] Esclusive =
        {
            "unico modo di", "e' l'unico modo", "unica operazione", "l'unica che",
        };

        internal static void Tutte()
        {
            Gruppo("le descrizioni del catalogo non si smentiscono (Z-C15)");

            var catalogo = Path.Combine(Radice(), "bin", "catalog.json");
            if (!File.Exists(catalogo))
                catalogo = Path.Combine(Radice(), "Tools", "catalog.json");
            if (!File.Exists(catalogo))
            {
                Segna("il catalogo servito c'e'", false, "manca " + catalogo);
                return;
            }

            var radice = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(catalogo))
                         as Dictionary<string, object>;
            object grezze;
            var voci = radice != null && radice.TryGetValue("operations", out grezze)
                ? grezze as object[] : null;
            if (voci == null)
            {
                Segna("il catalogo ha le operazioni", false, "«operations» non e' un elenco");
                return;
            }

            var perFamiglia = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var testi = new List<Tuple<string, string, string>>();
            foreach (var v in voci)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n, f, d, s;
                o.TryGetValue("name", out n);
                o.TryGetValue("family", out f);
                o.TryGetValue("details", out d);
                o.TryGetValue("summary", out s);
                string nome = Convert.ToString(n), famiglia = Convert.ToString(f) ?? "";
                if (string.IsNullOrEmpty(nome)) continue;
                List<string> l;
                if (!perFamiglia.TryGetValue(famiglia, out l)) perFamiglia[famiglia] = l = new List<string>();
                l.Add(nome);
                testi.Add(Tuple.Create(nome, famiglia,
                    (Convert.ToString(d) + " " + Convert.ToString(s)).ToLowerInvariant()));
            }

            var smentite = new List<string>();
            int esclusive = 0;
            foreach (var t in testi)
            {
                string frase = Esclusive.FirstOrDefault(e => t.Item3.Contains(e));
                if (frase == null) continue;
                esclusive++;
                var sorelle = perFamiglia[t.Item2].Where(x => x != t.Item1).ToList();
                if (sorelle.Count > 0)
                    smentite.Add(t.Item1 + " dice «" + frase + "», e in «" + t.Item2 + "» ci sono anche " +
                                 string.Join(", ", sorelle.Take(4)) + (sorelle.Count > 4 ? "..." : ""));
            }

            Segna("nessuna descrizione si dichiara l'unica in una famiglia che non lo e'",
                  smentite.Count == 0,
                  smentite.Count == 0
                      ? testi.Count + " descrizioni lette, " + esclusive + " si dicono uniche e sono sole davvero"
                      : string.Join(" | ", smentite));
        }
    }
}
