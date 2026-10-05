using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// OGNI OPERAZIONE DICE SU CHE DOCUMENTO GIRA (voce Z-D0-ter).
    ///
    /// ── PERCHE' LA FAMIGLIA NON BASTAVA ──────────────────────────────────
    ///
    /// `Z-D0-bis` confrontava le FAMIGLIE del catalogo coi tipi di documento
    /// che l'esecutore ammette, ed e' stato un buon controllo: e' nato rosso e
    /// ha trovato un buco vero. Ma la famiglia e' un'approssimazione, e
    /// `checks` lo dimostra da sola:
    ///
    ///     cartiglio                -> TAVOLA
    ///     interference_check    -> ASSIEME
    ///     mass_properties       -> PARTE      ... e altre cinque
    ///
    /// Tre documenti diversi sotto la stessa etichetta. Finche' non era
    /// scritto, quel fatto viveva nella testa di chi aveva scritto
    /// l'operazione e in una frase di prosa **che nessun controllo legge**.
    ///
    /// ── E SERVE AL MODELLO, NON SOLO AL CONTROLLO ────────────────────────
    ///
    /// Un'operazione che dichiara su che documento gira e' un'operazione che
    /// il modello **non prova** sul documento sbagliato. Provarlo costa un
    /// turno, e il rifiuto che ne segue costa la fiducia nel catalogo.
    /// </summary>
    internal static class RequestedDocumentTests
    {
        /// <summary>I valori che il campo puo' prendere, e nient'altro. Un
        /// vocabolario aperto diventa «part», «Parte», «part», «part» in
        /// sei mesi, e allora nessun controllo lo puo' piu' leggere.</summary>
        private static readonly string[] Allowed =
            { "part", "assembly", "drawing", "any", "none" };

        internal static void All()
        {
            Group("ogni operazione dice su che documento gira (Z-D0-ter)");

            var file = ServedCatalog();
            if (!File.Exists(file))
            {
                Check("il catalogo servito c'e'", false, "manca " + file);
                return;
            }

            var root = new JavaScriptSerializer()
                .DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
            object raw;
            var entries = root != null && root.TryGetValue("operations", out raw)
                ? raw as object[] : null;
            if (entries == null)
            {
                Check("il catalogo ha le operazioni", false, "«operations» non e' un elenco");
                return;
            }

            var withoutField = new List<string>();
            var outsideVocabulary = new List<string>();
            var perValue = new Dictionary<string, int>(StringComparer.Ordinal);
            var perFamily = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var v in entries)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n, d, f;
                o.TryGetValue("name", out n);
                o.TryGetValue("document", out d);
                o.TryGetValue("family", out f);
                string itemName = Convert.ToString(n);
                string doc = Convert.ToString(d);

                if (string.IsNullOrWhiteSpace(doc)) { withoutField.Add(itemName); continue; }

                foreach (var part in doc.Split('/'))
                {
                    if (!Allowed.Contains(part))
                        outsideVocabulary.Add(itemName + " -> «" + part + "»");
                }

                int q; perValue.TryGetValue(doc, out q);
                perValue[doc] = q + 1;

                string fam = Convert.ToString(f);
                HashSet<string> nameSet;
                if (!perFamily.TryGetValue(fam, out nameSet))
                    perFamily[fam] = nameSet = new HashSet<string>(StringComparer.Ordinal);
                nameSet.Add(doc);
            }

            Check("nessuna operazione tace sul documento", withoutField.Count == 0,
                  withoutField.Count == 0
                      ? entries.Length + " operazioni, tutte dichiarate"
                      : "tacciono: " + string.Join(", ", withoutField));

            Check("e nessuna usa una parola fuori dal vocabolario",
                  outsideVocabulary.Count == 0,
                  outsideVocabulary.Count == 0
                      ? "solo: " + string.Join(", ", Allowed)
                      : string.Join(" | ", outsideVocabulary));

            // ── LA PROVA CHE IL CAMPO SERVIVA ───────────────────────────
            //
            // Se ogni famiglia avesse un documento solo, questo campo sarebbe
            // un doppione della famiglia e andrebbe tolto. Il controllo lo
            // dice invece di lasciarlo credere: le famiglie MISTE sono la
            // ragione per cui la voce esiste.
            var mixed = perFamily.Where(x => x.Value.Count > 1)
                .Select(x => x.Key + " (" + string.Join(", ", x.Value.OrderBy(s => s)) + ")")
                .ToList();
            Check("il campo dice qualcosa che la famiglia non diceva",
                  mixed.Count > 0,
                  mixed.Count > 0
                      ? "famiglie con piu' di un documento: " + string.Join(" | ", mixed)
                      : "NESSUNA famiglia mista: il campo e' un doppione, toglilo");

            // ── E CIO' CHE DICHIARA DEV'ESSERE ESEGUIBILE ───────────────
            //
            // L'esecutore ammette certi tipi di documento e basta (la stessa
            // lettura che fa Z-D0-bis, dal sorgente e non dal testo del
            // csproj). Un'operazione che dichiarasse un documento che
            // l'esecutore rifiuta sarebbe una promessa irraggiungibile —
            // esattamente il difetto che Z-D0-bis trovo' sulle famiglie.
            string executor = Path.Combine(Root(), "Oce.Solid.Agent.Core", "PlanExecutor.cs");
            if (!File.Exists(executor))
            {
                Check("il sorgente dell'esecutore c'e'", false, "manca " + executor);
                return;
            }
            var text = File.ReadAllText(executor);
            int start = text.IndexOf("bool allowed", StringComparison.Ordinal);
            string block = start < 0 ? "" : text.Substring(start, Math.Min(600, text.Length - start));

            var allowedByExecutor = new HashSet<string>(StringComparer.Ordinal);
            if (Regex.IsMatch(block, @"swDocPART")) allowedByExecutor.Add("part");
            if (Regex.IsMatch(block, @"swDocASSEMBLY")) allowedByExecutor.Add("assembly");
            if (Regex.IsMatch(block, @"swDocDRAWING")) allowedByExecutor.Add("drawing");

            Check("i tipi ammessi si leggono dal sorgente dell'esecutore",
                  allowedByExecutor.Count > 0,
                  string.Join(", ", allowedByExecutor.OrderBy(s => s)));

            var unreachable = new List<string>();
            foreach (var pair in perValue)
            {
                foreach (var part in pair.Key.Split('/'))
                {
                    if (part == "any" || part == "none") continue;
                    if (!allowedByExecutor.Contains(part))
                        unreachable.Add(part + " (" + pair.Value + " operazioni)");
                }
            }
            Check("nessuna operazione promette un documento che l'esecutore rifiuta",
                  unreachable.Count == 0,
                  unreachable.Count == 0
                      ? "tutti i documenti dichiarati sono eseguibili"
                      : "PROMESSI E IRRAGGIUNGIBILI: " + string.Join(", ", unreachable.Distinct()));
        }
    }
}
