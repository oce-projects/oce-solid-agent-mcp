using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// OGNI OPERAZIONE DICE SU CHE DOCUMENTO GIRA (voce Z-D0-ter).
    ///
    /// ── PERCHE' LA FAMIGLIA NON BASTAVA ──────────────────────────────────
    ///
    /// `Z-D0-bis` confrontava le FAMIGLIE del catalogo coi tipi di documento
    /// che l'esecutore ammette, ed e' stato un buon controllo: e' nato rosso e
    /// ha trovato un buco vero. Ma la famiglia e' un'approssimazione, e
    /// `verifiche` lo dimostra da sola:
    ///
    ///     cartiglio                -> TAVOLA
    ///     verifica_interferenze    -> ASSIEME
    ///     proprieta_di_massa       -> PARTE      ... e altre cinque
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
    internal static class ProveIlDocumentoRichiesto
    {
        /// <summary>I valori che il campo puo' prendere, e nient'altro. Un
        /// vocabolario aperto diventa «parte», «Parte», «part», «pezzo» in
        /// sei mesi, e allora nessun controllo lo puo' piu' leggere.</summary>
        private static readonly string[] Ammessi =
            { "parte", "assieme", "tavola", "qualunque", "nessuno" };

        internal static void Tutte()
        {
            Gruppo("ogni operazione dice su che documento gira (Z-D0-ter)");

            var file = Path.Combine(Radice(), "bin", "catalogo.json");
            if (!File.Exists(file))
            {
                Segna("il catalogo servito c'e'", false, "manca " + file);
                return;
            }

            var radice = new JavaScriptSerializer()
                .DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
            object grezzo;
            var voci = radice != null && radice.TryGetValue("operazioni", out grezzo)
                ? grezzo as object[] : null;
            if (voci == null)
            {
                Segna("il catalogo ha le operazioni", false, "«operazioni» non e' un elenco");
                return;
            }

            var senzaCampo = new List<string>();
            var fuoriVocabolario = new List<string>();
            var perValore = new Dictionary<string, int>(StringComparer.Ordinal);
            var perFamiglia = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var v in voci)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n, d, f;
                o.TryGetValue("nome", out n);
                o.TryGetValue("documento", out d);
                o.TryGetValue("famiglia", out f);
                string nome = Convert.ToString(n);
                string doc = Convert.ToString(d);

                if (string.IsNullOrWhiteSpace(doc)) { senzaCampo.Add(nome); continue; }

                foreach (var pezzo in doc.Split('/'))
                {
                    if (!Ammessi.Contains(pezzo))
                        fuoriVocabolario.Add(nome + " -> «" + pezzo + "»");
                }

                int q; perValore.TryGetValue(doc, out q);
                perValore[doc] = q + 1;

                string fam = Convert.ToString(f);
                HashSet<string> insieme;
                if (!perFamiglia.TryGetValue(fam, out insieme))
                    perFamiglia[fam] = insieme = new HashSet<string>(StringComparer.Ordinal);
                insieme.Add(doc);
            }

            Segna("nessuna operazione tace sul documento", senzaCampo.Count == 0,
                  senzaCampo.Count == 0
                      ? voci.Length + " operazioni, tutte dichiarate"
                      : "tacciono: " + string.Join(", ", senzaCampo));

            Segna("e nessuna usa una parola fuori dal vocabolario",
                  fuoriVocabolario.Count == 0,
                  fuoriVocabolario.Count == 0
                      ? "solo: " + string.Join(", ", Ammessi)
                      : string.Join(" | ", fuoriVocabolario));

            // ── LA PROVA CHE IL CAMPO SERVIVA ───────────────────────────
            //
            // Se ogni famiglia avesse un documento solo, questo campo sarebbe
            // un doppione della famiglia e andrebbe tolto. Il controllo lo
            // dice invece di lasciarlo credere: le famiglie MISTE sono la
            // ragione per cui la voce esiste.
            var miste = perFamiglia.Where(x => x.Value.Count > 1)
                .Select(x => x.Key + " (" + string.Join(", ", x.Value.OrderBy(s => s)) + ")")
                .ToList();
            Segna("il campo dice qualcosa che la famiglia non diceva",
                  miste.Count > 0,
                  miste.Count > 0
                      ? "famiglie con piu' di un documento: " + string.Join(" | ", miste)
                      : "NESSUNA famiglia mista: il campo e' un doppione, toglilo");

            // ── E CIO' CHE DICHIARA DEV'ESSERE ESEGUIBILE ───────────────
            //
            // L'esecutore ammette certi tipi di documento e basta (la stessa
            // lettura che fa Z-D0-bis, dal sorgente e non dal testo del
            // csproj). Un'operazione che dichiarasse un documento che
            // l'esecutore rifiuta sarebbe una promessa irraggiungibile —
            // esattamente il difetto che Z-D0-bis trovo' sulle famiglie.
            string esecutore = Path.Combine(Radice(), "Riscontro.Disegno", "EsecutorePiano.cs");
            if (!File.Exists(esecutore))
            {
                Segna("il sorgente dell'esecutore c'e'", false, "manca " + esecutore);
                return;
            }
            var testo = File.ReadAllText(esecutore);
            int inizio = testo.IndexOf("bool ammesso", StringComparison.Ordinal);
            string blocco = inizio < 0 ? "" : testo.Substring(inizio, Math.Min(600, testo.Length - inizio));

            var ammessiDallEsecutore = new HashSet<string>(StringComparer.Ordinal);
            if (Regex.IsMatch(blocco, @"swDocPART")) ammessiDallEsecutore.Add("parte");
            if (Regex.IsMatch(blocco, @"swDocASSEMBLY")) ammessiDallEsecutore.Add("assieme");
            if (Regex.IsMatch(blocco, @"swDocDRAWING")) ammessiDallEsecutore.Add("tavola");

            Segna("i tipi ammessi si leggono dal sorgente dell'esecutore",
                  ammessiDallEsecutore.Count > 0,
                  string.Join(", ", ammessiDallEsecutore.OrderBy(s => s)));

            var irraggiungibili = new List<string>();
            foreach (var coppia in perValore)
            {
                foreach (var pezzo in coppia.Key.Split('/'))
                {
                    if (pezzo == "qualunque" || pezzo == "nessuno") continue;
                    if (!ammessiDallEsecutore.Contains(pezzo))
                        irraggiungibili.Add(pezzo + " (" + coppia.Value + " operazioni)");
                }
            }
            Segna("nessuna operazione promette un documento che l'esecutore rifiuta",
                  irraggiungibili.Count == 0,
                  irraggiungibili.Count == 0
                      ? "tutti i documenti dichiarati sono eseguibili"
                      : "PROMESSI E IRRAGGIUNGIBILI: " + string.Join(", ", irraggiungibili.Distinct()));
        }
    }
}
