using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE CALLS FORBIDDEN BY CONTRACT (entry GP6).
    ///
    /// ── THE BAN WAS THERE, THE CHECK WAS NOT ─────────────────────────────
    ///
    /// Entry GP6 writes in black and white that **three almost identical signatures
    /// coexist**, and that two must be avoided:
    ///
    ///     IModelDocExtension.AddDimension   4 param, Object   <- THIS ONE
    ///     IModelDoc2.AddDimension           3 param, Boolean  <- forbidden
    ///     IModelDoc2.AddDimension2          3 param, Object   <- no direction
    ///
    /// «It must be forbidden by contract», says the entry — and until 23/9/2026 that
    /// ban lived **only in a line of the TODO**. A ban that nobody
    /// checks is a preference: the day somebody writes the wrong line,
    /// the dimension is born and cannot be reached, and nobody says so.
    ///
    /// ── WHY HERE AND NOT IN A PROBE ON THE INTEROP ────────────────────────
    ///
    /// Because the interop is not needed: **we look at our own source**. A probe
    /// that reads the SOLIDWORKS interops is excluded from the pre-push hook and from the
    /// CI, where the Dassault interop is not there; this check instead runs
    /// wherever `Oce.Solid.Agent.ContractTests` runs, and counts in the summary.
    ///
    /// *Putting behind a CAD gate the only part that does not want the CAD
    /// would be the quickest way of never running it.*
    ///
    /// ── AND THE CHECK PROVES THAT IT KNOWS HOW TO SAY NO ──────────────────
    ///
    /// A check on clean sources never triggers, and one that never triggers
    /// cannot be told from a broken one: before saying «nobody calls it», the
    /// rule is tried on lines written on purpose.
    /// </summary>
    internal static class ForbiddenSignaturesTests
    {
        /// <summary>
        /// The calls that the code must not make, and why. The why is
        /// mandatory: a ban without a reason is the first that somebody
        /// skips «because anyway».
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

            // ── FIRST: DOES THE RULE HAVE TEETH? ─────────────────────────
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

            // ── AND IT DOES NOT CATCH THE RIGHT ONE ───────────────────────
            //
            // The half that counts: `Extension.AddDimension` is the correct
            // way and the code uses it. A check that flagged it
            // would be switched off the same day.
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

            // ── THEN: THE REAL SOURCE ────────────────────────────────────
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
                        // THE COMMENTS OF THIS REPOSITORY TALK ABOUT THE TRAP.
                        // Counting them would mean that explaining it makes it become
                        // a violation.
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

            // ── AND THE RIGHT ONE IS REALLY USED ─────────────────────────
            //
            // Without this line the check above would be green even in a
            // repository that does not dimension at all — that is, it would say «we respect the
            // ban» of a rule that touches nothing.
            bool used = false;
            var quote = Path.Combine(Root(), "Oce.Solid.Agent.Core", "DrawingDimensionOperations.cs");
            if (File.Exists(quote))
                used = File.ReadAllText(quote).Contains("Extension.AddDimension(");
            Check("e quella giusta e' davvero in uso", used,
                  "DrawingDimensionOperations.cs: senza questa riga il divieto sorveglierebbe il vuoto");
        }
    }
}
