using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
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
    /// dovunque giri `Oce.Solid.Agent.ContractTests`, e conta nel riepilogo.
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
    internal static class ForbiddenSignaturesTests
    {
        /// <summary>
        /// Le chiamate che il codice non deve fare, e perche'. Il perche' e'
        /// obbligatorio: un divieto senza motivo e' il primo che qualcuno
        /// scavalca «perche' tanto».
        /// </summary>
        private static readonly Dictionary<string, string> Forbidden =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                {
                    @"\bmodel\s*\.\s*AddDimension\s*\(",
                    "IModelDoc2.AddDimension (3 param) ritorna Boolean: la quota, " +
                    "se nasce, NON E' RAGGIUNGIBILE. Si usa " +
                    "`model.Extension.AddDimension(x, y, z, direction)`, che " +
                    "torna l'oggetto (voce GP6)"
                },
                {
                    @"\bmodel\s*\.\s*AddDimension2\s*\(",
                    "IModelDoc2.AddDimension2 non ha controllo di direzione: la " +
                    "quota nasce orientata a caso, ed e' un difetto che sullo " +
                    "schermo non si vede (voce GP6)"
                },
                {
                    @"\.\s*EditDelete\s*\(",
                    "IModelDoc2.EditDelete chiede conferma con una finestra: dentro " +
                    "SOLIDWORKS la chiamata COM non torna e il CAD resta piantato. Si " +
                    "usa `model.Extension.DeleteSelection2(swDelete_Absorbed)`. " +
                    "Era vietata solo nei commenti, e OperazioniTabellaFori la chiamava " +
                    "davvero (trovata dal workflow W-S il 27/9/2026)"
                },
            };

        internal static void All()
        {
            Group("le chiamate vietate per contratto (GP6, EditDelete)");

            // ── PRIMA: LA REGOLA HA I DENTI? ─────────────────────────────
            var mustFire = new[]
            {
                "var q = model.AddDimension(x, y, z);",
                "if (!model.AddDimension2(x, y, z)) return;",
                "model . AddDimension ( 0, 0, 0 );",
                "if (ann.Select3(false, null)) model.EditDelete();",
            };
            int taken = 0;
            foreach (var r in mustFire)
                if (Forbidden.Keys.Any(k => Regex.IsMatch(r, k))) taken++;
            Check("la regola riconosce le chiamate vietate", taken == mustFire.Length,
                  taken + " su " + mustFire.Length);

            // ── E NON PRENDE QUELLA GIUSTA ───────────────────────────────
            //
            // La meta' che conta: `Extension.AddDimension` e' la strada
            // corretta e il codice la usa. Un controllo che la segnalasse
            // verrebbe spento il giorno stesso.
            var mustPass = new[]
            {
                "var nature = model.Extension.AddDimension(x, y, z, 0);",
                "model.Extension.AddDimension(entry.X, entry.Y, entry.Z, 0);",
                "// AddDimension apre un modale: spegnere swInputDimValOnCreate",
                "model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);",
            };
            var shouted = mustPass
                .Where(r => Forbidden.Keys.Any(k => Regex.IsMatch(r, k))).ToList();
            Check("e NON prende `Extension.AddDimension`, che e' quella giusta",
                  shouted.Count == 0,
                  shouted.Count == 0 ? "quattro righe legittime, zero falsi allarmi"
                                    : string.Join(" | ", shouted));

            // ── POI: IL SORGENTE VERO ────────────────────────────────────
            var folders = new[] { "Oce.Solid.Agent.Core", "Tools", "Bridge", "Mcp", "Bench" };
            var foundOnes = new List<string>();
            int checkedLines = 0;

            foreach (var c in folders)
            {
                var dir = Path.Combine(Root(), c);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var lines = File.ReadAllLines(f);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var bare = lines[i].TrimStart();
                        // I COMMENTI DI QUESTO REPOSITORY PARLANO DELLA TRAPPOLA.
                        // Contarli vorrebbe dire che spiegarla la fa diventare
                        // una violazione.
                        if (bare.StartsWith("//", StringComparison.Ordinal)) continue;
                        checkedLines++;
                        foreach (var v in Forbidden)
                            if (Regex.IsMatch(lines[i], v.Key))
                                foundOnes.Add(Path.GetFileName(f) + ":" + (i + 1) + " — " + v.Value);
                    }
                }
            }

            Check("nessuno chiama le firme vietate", foundOnes.Count == 0,
                  foundOnes.Count == 0
                      ? checkedLines + " righe di codice guardate in " + folders.Length + " cartelle"
                      : string.Join(" | ", foundOnes));

            // ── E QUELLA GIUSTA SI USA DAVVERO ───────────────────────────
            //
            // Senza questa riga il controllo sopra sarebbe verde anche in un
            // repository che non quota affatto — cioe' direbbe «rispettiamo il
            // divieto» di una regola che non tocca niente.
            bool used = false;
            var quote = Path.Combine(Root(), "Oce.Solid.Agent.Core", "DrawingDimensionOperations.cs");
            if (File.Exists(quote))
                used = File.ReadAllText(quote).Contains("Extension.AddDimension(");
            Check("e quella giusta e' davvero in uso", used,
                  "DrawingDimensionOperations.cs: senza questa riga il divieto sorveglierebbe il vuoto");
        }
    }
}
