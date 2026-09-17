using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// IL CATALOGO, che e' l'interfaccia vera verso il modello (voce C1/F7).
    ///
    /// Si legge per RIFLESSIONE dal ponte gia' compilato — `Catalogo` e la sua
    /// `Simili()` vivono li' — e il file `catalogo.json` glielo si indica con
    /// `OPENPDM_CATALOGO`, che e' la stessa variabile che userebbe chi installa
    /// il pacchetto altrove.
    ///
    /// Niente di tutto questo tocca SOLIDWORKS: `Catalogo` legge un file JSON e
    /// confronta stringhe.
    /// </summary>
    internal static class ProveCatalogo
    {
        internal static void Tutte()
        {
            Gruppo("il catalogo");

            var json = Path.Combine(Radice(), "bin", "catalogo.json");
            if (!File.Exists(json))
            {
                Segna("il catalogo c'e'", false,
                      "non trovo " + json + " — si produce con .\\build.ps1");
                return;
            }
            Segna("il catalogo c'e'", true, json);

            Dictionary<string, object> radice;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                s.MaxJsonLength = 40 * 1024 * 1024;
                radice = s.DeserializeObject(File.ReadAllText(json)) as Dictionary<string, object>;
            }
            catch (Exception ex) { Segna("il catalogo si legge", false, ex.Message); return; }

            var operazioni = (radice != null && radice.ContainsKey("operazioni")
                ? radice["operazioni"] : null) as object[];
            if (operazioni == null) { Segna("il catalogo ha «operazioni»", false, ""); return; }
            Segna("il catalogo ha almeno trenta operazioni", operazioni.Length >= 30,
                  operazioni.Length + " operazioni");

            IlReadmeDiceIlNumeroGiusto(operazioni.Length);

            // ── OGNI OPERAZIONE HA I CAMPI CHE SERVONO ───────────────────
            var senza = new List<string>();
            var famiglie = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in operazioni)
            {
                var v = o as Dictionary<string, object>;
                if (v == null) { senza.Add("(una voce non e' un oggetto)"); continue; }
                string nome = v.ContainsKey("nome") ? Convert.ToString(v["nome"]) : null;
                foreach (var campo in new[] { "nome", "famiglia", "sommario", "descrizione", "schema" })
                    if (!v.ContainsKey(campo) || v[campo] == null)
                        senza.Add((nome ?? "?") + " senza «" + campo + "»");
                if (v.ContainsKey("famiglia")) famiglie.Add(Convert.ToString(v["famiglia"]));
            }
            Segna("ogni operazione ha nome, famiglia, sommario, descrizione, schema",
                  senza.Count == 0, string.Join("; ", senza.Take(4)));

            // ── LE FAMIGLIE DICHIARATE SONO QUELLE USATE ─────────────────
            //
            // Una famiglia usata e non dichiarata e' un'operazione che nel
            // menu non compare: il modello non la vede, e nessuno se ne
            // accorge finche' non la cerca per nome.
            var dichiarate = (radice.ContainsKey("famiglie") ? radice["famiglie"] : null)
                             as Dictionary<string, object>;
            if (dichiarate == null) Segna("il catalogo dichiara le famiglie", false, "");
            else
            {
                var orfane = famiglie.Where(f => !dichiarate.ContainsKey(f)).ToList();
                Segna("ogni famiglia usata e' dichiarata", orfane.Count == 0,
                      orfane.Count == 0 ? dichiarate.Count + " famiglie"
                                        : "usate e non dichiarate: " + string.Join(", ", orfane));
            }

            // ── NESSUN PERCORSO DI QUESTA MACCHINA ───────────────────────
            //
            // Il catalogo lo legge un modello, e finira' in un pacchetto che
            // va altrove. Un «S:\PDM\...» dentro una descrizione e' un
            // indirizzo che da quella macchina non esiste, e chi legge non ha
            // modo di saperlo.
            // ── E IL CONTROLLO HA GIA' SCATTATO PER IL MOTIVO SBAGLIATO ──
            //
            // La prima scrittura cercava «O:\» senza distinguere maiuscole e
            // minuscole, e ha trovato **«tipo:\n»**: due lettere, i due punti e
            // la barra di un a-capo. Un rosso che scatta per un'altra ragione
            // non prova niente e sembra identico a uno che funziona.
            //
            // La lettera di unita' e' una lettera **non preceduta da un'altra
            // lettera**: cosi' «NO:\n» e «tipo:\n» non la sono, e «S:\PDM» si'.
            var testo = File.ReadAllText(json);
            var brutti = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(testo, @"(?<![A-Za-z])[A-Za-z]:\\"))
                brutti.Add(testo.Substring(m.Index, Math.Min(40, testo.Length - m.Index)));
            foreach (var p in new[] { "scratchpad", "\\\\\\\\" })
                if (testo.IndexOf(p, StringComparison.Ordinal) >= 0) brutti.Add(p);
            Segna("nessun percorso di questa macchina nel catalogo",
                  brutti.Count == 0,
                  brutti.Count == 0 ? "" : "trovati: " + string.Join(" | ", brutti.Take(3)));

            Simili();
        }

        /// <summary>
        /// `Catalogo.Simili()`: il caso per cui la funzione e' stata riscritta.
        ///
        /// Il primo tentativo cercava la parola intera dentro il nome, e su
        /// «asole» **non trovava «asola»** — cioe' falliva proprio dove
        /// serviva: chi sbaglia un nome quasi sempre ne ha in mente uno vicino,
        /// e lo sbaglia di una lettera.
        /// </summary>
        private static void Simili()
        {
            var ponte = Path.Combine(Radice(), "bin", "oce-solid-agent-mcp.exe");
            if (!File.Exists(ponte))
            {
                Segna("Catalogo.Simili: il ponte c'e'", false,
                      "non trovo " + ponte + " — si produce con .\\build.ps1");
                return;
            }

            object catalogo;
            MethodInfo simili;
            try
            {
                Environment.SetEnvironmentVariable(
                    "OPENPDM_CATALOGO", Path.Combine(Radice(), "bin", "catalogo.json"));
                // SI CARICA DAI BYTE: `LoadFrom` su un'unita' di rete risponde
                // NotSupportedException, e questo repository sta su S:.
                var asm = Carica.Ponte(ponte);
                var tipo = Carica.Classe(asm, "Catalogo");
                if (tipo == null) { Segna("Catalogo.Simili: la classe c'e'", false, "Catalogo assente"); return; }
                catalogo = Activator.CreateInstance(tipo, true);
                simili = tipo.GetMethod("Simili");
                if (simili == null) { Segna("Catalogo.Simili: il metodo c'e'", false, ""); return; }
            }
            catch (Exception ex) { Segna("Catalogo.Simili: si carica", false, ex.ToString()); return; }

            Func<string, List<string>> di = n =>
                (simili.Invoke(catalogo, new object[] { n }) as System.Collections.IEnumerable)
                    ?.Cast<object>().Select(Convert.ToString).ToList() ?? new List<string>();

            Segna("«asole» trova «asola»", di("asole").Contains("asola"),
                  string.Join(", ", di("asole")));
            Segna("«foro» trova «foro_guidato»", di("foro").Contains("foro_guidato"),
                  string.Join(", ", di("foro")));
            Segna("un nome vuoto non esplode e non inventa", di("").Count == 0);
            Segna("un nome che non somiglia a niente non inventa",
                  di("qwertyuiop").Count == 0, string.Join(", ", di("qwertyuiop")));
            Segna("non ne offre piu' di cinque", di("a_asola_foro_taglio_piano").Count <= 5,
                  di("a_asola_foro_taglio_piano").Count + " offerti");
        }

        // ================================================================
        // IL NUMERO SCRITTO NEL README E' UNA MISURA, E INVECCHIA COME TUTTE
        //
        // Il 16/9/2026 il README diceva «trentotto operazioni» in due punti
        // mentre il catalogo ne aveva sessanta. Nessun controllo poteva
        // accorgersene: i documenti non si compilano, non si eseguono, e
        // nessuno li rilegge finche' non servono a qualcuno che non c'era.
        //
        // E' ESATTAMENTE IL DIFETTO CHE NON DA' ERRORE, applicato ai
        // documenti: il README sembra giusto, si legge bene, e dice il falso.
        // Chi lo legge non ha modo di accorgersene — e' la sua unica fonte.
        //
        // Il controllo cerca solo DUE forme, non tutti i numeri che compaiono:
        // «<numero> operazioni» e «operazioni di disegno sono <numero>». Cosi'
        // «Cinque, non sessanta» (che parla degli STRUMENTI) non lo fa
        // scattare: un controllo che grida su una frase giusta si impara a
        // ignorare, ed e' peggio di nessun controllo.
        // ================================================================
        private static void IlReadmeDiceIlNumeroGiusto(int quante)
        {
            var readme = Path.Combine(Radice(), "README.md");
            if (!File.Exists(readme))
            {
                Segna("il README c'e'", false, readme);
                return;
            }

            string testo;
            try { testo = File.ReadAllText(readme); }
            catch (Exception ex) { Segna("il README si legge", false, ex.Message); return; }

            var trovati = new List<string>();
            foreach (var forma in new[]
            {
                @"([a-zàèéìòù]+)\s+operazioni",
                @"operazioni\s+di\s+disegno\s+sono\s+([a-zàèéìòù]+)",
            })
            {
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(
                             testo, forma,
                             System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    int n = Numero(m.Groups[1].Value);
                    // Non e' un numero scritto a parole: e' un aggettivo
                    // qualunque («altre operazioni», «sessanta operazioni»).
                    // Si tace, non si inventa un difetto.
                    if (n < 0) continue;
                    if (n != quante) trovati.Add("«" + m.Value.Replace("\n", " ") + "»");
                }
            }

            Segna("il README dice quante sono le operazioni, e sono quelle vere",
                  trovati.Count == 0,
                  trovati.Count == 0
                      ? "il catalogo ne ha " + quante
                      : "il catalogo ne ha " + quante + ", il README dice " +
                        string.Join(" e ", trovati));
        }

        /// <summary>
        /// Un numero italiano scritto a parole, da 0 a 99. `-1` quando la
        /// parola non e' un numero — che e' il caso normale e non un errore.
        /// </summary>
        private static int Numero(string parola)
        {
            if (string.IsNullOrWhiteSpace(parola)) return -1;
            var p = parola.Trim().ToLowerInvariant();

            var unita = new[]
            {
                "zero", "uno", "due", "tre", "quattro", "cinque", "sei", "sette",
                "otto", "nove", "dieci", "undici", "dodici", "tredici",
                "quattordici", "quindici", "sedici", "diciassette", "diciotto",
                "diciannove",
            };
            for (int i = 0; i < unita.Length; i++)
                if (p == unita[i]) return i;

            var decine = new[]
            {
                "venti", "trenta", "quaranta", "cinquanta", "sessanta",
                "settanta", "ottanta", "novanta",
            };
            for (int d = 0; d < decine.Length; d++)
            {
                int valore = (d + 2) * 10;
                if (p == decine[d]) return valore;

                // L'ELISIONE. «venti» + «uno» fa «ventuno», non «ventiuno»:
                // davanti a «uno» e «otto» la decina perde la vocale finale.
                // Senza questa riga «trentotto» — il numero sbagliato che ha
                // fatto nascere il controllo — non verrebbe nemmeno letto.
                var radice = decine[d].Substring(0, decine[d].Length - 1);
                for (int u = 1; u <= 9; u++)
                {
                    bool elide = u == 1 || u == 8;
                    if (p == (elide ? radice : decine[d]) + unita[u]) return valore + u;
                }
            }
            return -1;
        }
    }
}
