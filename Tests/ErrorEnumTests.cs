using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// `if (errore != 0)` CAN BE EXACTLY BACKWARDS (entry Z-C7).
    ///
    /// ── THE FACT, MEASURED ───────────────────────────────────────────────
    ///
    /// `swAddMateError_NoError` is worth **1**; `ErrorUknown` is worth **0**. On that
    /// API an `if (errore != 0)` declares a **successful** mate failed,
    /// and gives no symptom: the mate is there, the message says no.
    ///
    /// And it is not an isolated case. On 23/9/2026, by reflection on the interops of
    /// SOLIDWORKS 2021 SP5.1 — `Checks\error-enum.ps1`, which redoes the
    /// measurement when needed — out of **38** error or status enums, **13** have
    /// «no error» different from zero:
    ///
    ///     swAddMateError_e                  swAddMateError_NoError        1
    ///     swBodyMaterialApplicationError_e  ..._NoError                   1
    ///     swCreateAngRunDimError_e          ..._Success                   1
    ///     swCreateCommandGroupErrors        swCreateCommandGroup_Success  1
    ///     swCropViewErrors_e                ..._NoError                   1
    ///     swExcludeFromBOMError_e           swExcludeFromBOM_Success      1
    ///     swInsertNewAssemblyErrorCode_e    ..._NoError                   1
    ///     swInsertNewPartErrorCode_e        ..._NoError                   1
    ///     swKernelErrorCode_e               swErrorSuccess                1
    ///     swLinkDimensionError_e            ..._NoError                   1
    ///     swRemoveCommandGroupErrors        ..._Success                   1
    ///     swReplaceComponentError_e         swReplaceComponent_Success    1
    ///     swUpdateProgressError_e           ..._Success                   1
    ///
    /// **A third.** It is not a quirk of one API: it is a probability.
    ///
    /// ── WHY THE LIST IS WRITTEN HERE AND NOT READ ─────────────────────────
    ///
    /// Because these checks run **where SOLIDWORKS is not** — in CI there are
    /// no interops to interrogate. So the list is copied data, and
    /// copied data ages by itself: that is why the script that redoes it exists,
    /// and it is named above instead of being left implicit.
    ///
    /// ── THE RULE, WHICH IS SIMPLER THAN THE LIST ──────────────────────────
    ///
    /// There is no need to know which enum comes back from which API at every point: it is enough **never
    /// to compare an error with a digit**. The member with the name is read,
    /// it cannot be got wrong in sign, and if the digit changes between two versions of
    /// SOLIDWORKS it keeps working.
    ///
    /// The exceptions exist and are in a list with the **measured why**
    /// next to them — not «it seems right to me».
    /// </summary>
    internal static class ErrorEnumTests
    {
        /// <summary>
        /// An error or a status compared with a DIGIT instead of with the
        /// member that has the name.
        ///
        /// Only `==` and `!=`: `&gt; 0.5` on a tolerance has nothing to do with it, and
        /// a check that shouts about tolerances stops being read.
        /// </summary>
        private static readonly Regex Suspicious = new Regex(
            @"\b\w*(?:err|errore|error|stato|status)\w*\s*(?:==|!=)\s*\d+(?![\d.])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// The lines that have permission, and why. The why is a MEASUREMENT,
        /// not an opinion: whoever adds a line here must have read the value
        /// of the enum, not supposed it.
        /// </summary>
        private static readonly Dictionary<string, string> Allowed =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                {
                    "error != 0 || obj == null",
                    "GetObjectByPersistReference3: swPersistReferencedObject_Ok vale " +
                    "**0** (Invalid 1, Suppressed 2, Deleted 4), letto per " +
                    "riflessione il 23/9/2026. Qui lo zero e' davvero «tutto bene»."
                },
                {
                    "inError == 1",
                    "NON E' UN CODICE DI ERRORE: e' il CONTEGGIO delle lavorazioni " +
                    "in errore (`features_in_error` dello stato), confrontato " +
                    "con 1 per scegliere fra «lavorazione» e «features». " +
                    "Trovata da questa guardia al suo primo giro, il 23/9/2026: " +
                    "il nome dice «error» e il valore e' un numero di cose."
                },
            };

        internal static void All()
        {
            Group("gli errori non si confrontano con una cifra (Z-C7)");

            // ── FIRST: DOES THE GUARD HAVE TEETH? ────────────────────────
            //
            // A static check on clean sources never triggers, and one that
            // never triggers is indistinguishable from a broken one. Before saying
            // «no suspicious line» one proves that it would recognize them.
            var mustFire = new[]
            {
                "if (errore != 0) return null;",
                "if (error == 0) { }",
                "while (statoUscita != 1)",
                "if (ErroreMate == 0) return;",
            };
            int taken = 0;
            foreach (var r in mustFire) if (Suspicious.IsMatch(r)) taken++;
            Check("la guardia riconosce i confronti con una cifra",
                  taken == mustFire.Length,
                  taken + " su " + mustFire.Length);

            // AND IT MUST NOT SHOUT AT EVERYTHING. A guard that flags even what
            // is fine gets switched off, and it is the most common way in which a
            // check dies.
            var mustStaySilent = new[]
            {
                "if (errore != (int)swAddMateError_e.swAddMateError_NoError)",
                "if (erroreU > 0.5 || erroreV > 0.5)",
                "if (errori.Count > 0) return;",
                "var stato = 0;",
            };
            var shouted = new List<string>();
            foreach (var r in mustStaySilent) if (Suspicious.IsMatch(r)) shouted.Add(r.Trim());
            Check("e tace su quello che va bene",
                  shouted.Count == 0,
                  shouted.Count == 0 ? "quattro righe legittime, zero falsi allarmi"
                                    : "falsi allarmi: " + string.Join(" | ", shouted));

            // ── THEN: THE SOURCES ─────────────────────────────────────────
            var folder = Path.Combine(Root(), "Oce.Solid.Agent.Core");
            if (!Directory.Exists(folder))
            {
                Check("la cartella della libreria c'e'", false, "manca " + folder);
                return;
            }

            var foundOnes = new List<string>();
            int inspected = 0;
            foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var r = lines[i];
                    var bare = r.TrimStart();

                    // THE COMMENTS OF THIS REPOSITORY TALK ABOUT THE PROBLEM, and
                    // often quote the wrong line to explain it. Counting them
                    // would mean that explaining a defect makes it become a
                    // defect.
                    if (bare.StartsWith("//", StringComparison.Ordinal)) continue;
                    inspected++;

                    var m = Suspicious.Match(r);
                    if (!m.Success) continue;

                    bool allowed = false;
                    foreach (var p in Allowed.Keys)
                        if (r.Contains(p)) { allowed = true; break; }
                    if (allowed) continue;

                    foundOnes.Add(Path.GetFileName(file) + ":" + (i + 1) + "  " + m.Value.Trim());
                }
            }

            Check("nessun errore confrontato con una cifra",
                  foundOnes.Count == 0,
                  foundOnes.Count == 0
                      ? inspected + " righe di codice guardate, " + Allowed.Count +
                        " eccezione dichiarata"
                      : string.Join(" | ", foundOnes));

            // ── AND DO THE EXCEPTIONS STILL EXIST? ──────────────────────
            //
            // A permission for a line that is no longer there is a hole that stays
            // open: the day somebody rewrites that spot getting it wrong,
            // the permission covers it.
            foreach (var p in Allowed)
            {
                bool alive = false;
                foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
                    if (File.ReadAllText(file).Contains(p.Key)) { alive = true; break; }
                Check("l'eccezione dichiarata esiste ancora: «" + p.Key + "»", alive,
                      alive ? p.Value : "NON c'e' piu' nel codice: togli il permesso");
            }
        }
    }
}
