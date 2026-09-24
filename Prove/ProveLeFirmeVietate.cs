using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// LE CHIAMATE VIETATE PER CONTRATTO (voce GP6).
    ///
    /// ── IL DIVIETO C'ERA, IL CONTROLLO NO ────────────────────────────────
    ///
    /// La voce GP6 scrive nero su bianco che **tre firme quasi identiche
    /// coesistono**, e che due vanno evitate:
    ///
    ///     IModelDocExtension.AddDimension   4 param, Object   <- QUESTA
    ///     IModelDoc2.AddDimension           3 param, Boolean  <- vietata
    ///     IModelDoc2.AddDimension2          3 param, Object   <- niente direzione
    ///
    /// «Va vietata per contratto», dice la voce — e fino al 23/9/2026 quel
    /// divieto viveva **solo in una riga di TODO**. Un divieto che nessuno
    /// controlla e' una preferenza: il giorno in cui qualcuno scrive la riga
    /// sbagliata, la quota nasce e non e' raggiungibile, e non lo dice nessuno.
    ///
    /// ── PERCHE' QUI E NON IN UNA SONDA SULL'INTEROP ──────────────────────
    ///
    /// Perche' non serve l'interop: **si guarda il nostro sorgente**. Una sonda
    /// che legge le interop di SOLIDWORKS e' esclusa dal gancio pre-push e dalla
    /// CI, dove l'interop di Dassault non c'e'; questo controllo invece gira
    /// dovunque giri `Prove.Contratti`, e conta nel riepilogo.
    ///
    /// *Mettere dietro un cancello CAD l'unica parte che il CAD non lo vuole
    /// sarebbe il modo piu' rapido di non farla girare mai.*
    ///
    /// ── E IL CONTROLLO DIMOSTRA DI SAPER DIRE DI NO ──────────────────────
    ///
    /// Un controllo su sorgenti puliti non scatta mai, e uno che non scatta mai
    /// non si distingue da uno rotto: prima di dire «nessuno la chiama», si
    /// prova la regola su righe scritte apposta.
    /// </summary>
    internal static class ProveLeFirmeVietate
    {
        /// <summary>
        /// Le chiamate che il codice non deve fare, e perche'. Il perche' e'
        /// obbligatorio: un divieto senza motivo e' il primo che qualcuno
        /// scavalca «perche' tanto».
        /// </summary>
        private static readonly Dictionary<string, string> Vietate =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                {
                    @"\bmodello\s*\.\s*AddDimension\s*\(",
                    "IModelDoc2.AddDimension (3 param) ritorna Boolean: la quota, " +
                    "se nasce, NON E' RAGGIUNGIBILE. Si usa " +
                    "`modello.Extension.AddDimension(x, y, z, direzione)`, che " +
                    "torna l'oggetto (voce GP6)"
                },
                {
                    @"\bmodello\s*\.\s*AddDimension2\s*\(",
                    "IModelDoc2.AddDimension2 non ha controllo di direzione: la " +
                    "quota nasce orientata a caso, ed e' un difetto che sullo " +
                    "schermo non si vede (voce GP6)"
                },
            };

        internal static void Tutte()
        {
            Gruppo("le chiamate vietate per contratto (GP6)");

            // ── PRIMA: LA REGOLA HA I DENTI? ─────────────────────────────
            var devonoScattare = new[]
            {
                "var q = modello.AddDimension(x, y, z);",
                "if (!modello.AddDimension2(x, y, z)) return;",
                "modello . AddDimension ( 0, 0, 0 );",
            };
            int prese = 0;
            foreach (var r in devonoScattare)
                if (Vietate.Keys.Any(k => Regex.IsMatch(r, k))) prese++;
            Segna("la regola riconosce le chiamate vietate", prese == devonoScattare.Length,
                  prese + " su " + devonoScattare.Length);

            // ── E NON PRENDE QUELLA GIUSTA ───────────────────────────────
            //
            // La meta' che conta: `Extension.AddDimension` e' la strada
            // corretta e il codice la usa. Un controllo che la segnalasse
            // verrebbe spento il giorno stesso.
            var devonoPassare = new[]
            {
                "var natura = modello.Extension.AddDimension(x, y, z, 0);",
                "modello.Extension.AddDimension(voce.X, voce.Y, voce.Z, 0);",
                "// AddDimension apre un modale: spegnere swInputDimValOnCreate",
            };
            var urlate = devonoPassare
                .Where(r => Vietate.Keys.Any(k => Regex.IsMatch(r, k))).ToList();
            Segna("e NON prende `Extension.AddDimension`, che e' quella giusta",
                  urlate.Count == 0,
                  urlate.Count == 0 ? "tre righe legittime, zero falsi allarmi"
                                    : string.Join(" | ", urlate));

            // ── POI: IL SORGENTE VERO ────────────────────────────────────
            var cartelle = new[] { "Riscontro.Disegno", "Strumenti", "Ponte", "Mcp", "Banco" };
            var trovate = new List<string>();
            int righeGuardate = 0;

            foreach (var c in cartelle)
            {
                var dir = Path.Combine(Radice(), c);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var righe = File.ReadAllLines(f);
                    for (int i = 0; i < righe.Length; i++)
                    {
                        var nudo = righe[i].TrimStart();
                        // I COMMENTI DI QUESTO REPOSITORY PARLANO DELLA TRAPPOLA.
                        // Contarli vorrebbe dire che spiegarla la fa diventare
                        // una violazione.
                        if (nudo.StartsWith("//", StringComparison.Ordinal)) continue;
                        righeGuardate++;
                        foreach (var v in Vietate)
                            if (Regex.IsMatch(righe[i], v.Key))
                                trovate.Add(Path.GetFileName(f) + ":" + (i + 1) + " — " + v.Value);
                    }
                }
            }

            Segna("nessuno chiama le firme vietate", trovate.Count == 0,
                  trovate.Count == 0
                      ? righeGuardate + " righe di codice guardate in " + cartelle.Length + " cartelle"
                      : string.Join(" | ", trovate));

            // ── E QUELLA GIUSTA SI USA DAVVERO ───────────────────────────
            //
            // Senza questa riga il controllo sopra sarebbe verde anche in un
            // repository che non quota affatto — cioe' direbbe «rispettiamo il
            // divieto» di una regola che non tocca niente.
            bool usata = false;
            var quote = Path.Combine(Radice(), "Riscontro.Disegno", "OperazioniQuoteTavola.cs");
            if (File.Exists(quote))
                usata = File.ReadAllText(quote).Contains("Extension.AddDimension(");
            Segna("e quella giusta e' davvero in uso", usata,
                  "OperazioniQuoteTavola.cs: senza questa riga il divieto sorveglierebbe il vuoto");
        }
    }
}
