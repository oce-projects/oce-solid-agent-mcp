using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// `if (errore != 0)` PUO' ESSERE ESATTAMENTE ROVESCIATO (voce Z-C7).
    ///
    /// ── IL FATTO, MISURATO ───────────────────────────────────────────────
    ///
    /// `swAddMateError_NoError` vale **1**; `ErrorUknown` vale **0**. Su quella
    /// API un `if (errore != 0)` dichiara fallito un accoppiamento **riuscito**,
    /// e non da' nessun sintomo: l'accoppiamento c'e', il messaggio dice di no.
    ///
    /// E non e' un caso isolato. Il 23/9/2026, per riflessione sulle interop di
    /// SOLIDWORKS 2021 SP5.1 — `Checks\error-enum.ps1`, che rifa' la
    /// misura quando serve — su **38** enum di errore o stato, **13** hanno
    /// «niente errore» diverso da zero:
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
    /// **Un terzo.** Non e' una stranezza di un'API: e' una probabilita'.
    ///
    /// ── PERCHE' L'ELENCO E' SCRITTO QUI E NON LETTO ──────────────────────
    ///
    /// Perche' queste prove girano **dove SOLIDWORKS non c'e'** — in CI non ci
    /// sono le interop da interrogare. Quindi l'elenco e' un dato copiato, e un
    /// dato copiato invecchia da solo: per questo esiste lo script che lo rifa',
    /// ed e' nominato qui sopra invece di essere sottinteso.
    ///
    /// ── LA REGOLA, CHE E' PIU' SEMPLICE DELL'ELENCO ──────────────────────
    ///
    /// Non serve sapere quale enum torna quale API in ogni punto: basta **non
    /// confrontare mai un errore con una cifra**. Il membro col nome si legge,
    /// non si puo' sbagliare di segno, e se la cifra cambia fra due versioni di
    /// SOLIDWORKS continua a funzionare.
    ///
    /// Le eccezioni esistono e stanno in un elenco con il **perche' misurato**
    /// accanto — non «mi sembra giusto».
    /// </summary>
    internal static class ErrorEnumTests
    {
        /// <summary>
        /// Un errore o uno stato confrontato con una CIFRA invece che col
        /// membro che ha il nome.
        ///
        /// Solo `==` e `!=`: `&gt; 0.5` su una tolleranza non c'entra niente, e
        /// un controllo che urla sulle tolleranze si smette di leggere.
        /// </summary>
        private static readonly Regex Suspicious = new Regex(
            @"\b\w*(?:err|errore|error|stato|status)\w*\s*(?:==|!=)\s*\d+(?![\d.])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Le righe che hanno il permesso, e perche'. Il perche' e' una MISURA,
        /// non un'opinione: chi aggiunge una riga qui deve aver letto il valore
        /// dell'enum, non averlo supposto.
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

            // ── PRIMA: LA GUARDIA HA I DENTI? ────────────────────────────
            //
            // Un controllo statico su sorgenti puliti non scatta mai, e uno che
            // non scatta mai e' indistinguibile da uno rotto. Prima di dire
            // «nessuna riga sospetta» si dimostra che le riconoscerebbe.
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

            // E NON DEVE URLARE SU TUTTO. Una guardia che segnala anche cio' che
            // va bene viene disattivata, ed e' il modo piu' comune in cui un
            // controllo muore.
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

            // ── POI: I SORGENTI ──────────────────────────────────────────
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

                    // I COMMENTI DI QUESTO REPOSITORY PARLANO DEL PROBLEMA, e
                    // spesso citano la riga sbagliata per spiegarla. Contarli
                    // vorrebbe dire che spiegare un difetto lo fa diventare un
                    // difetto.
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

            // ── E LE ECCEZIONI ESISTONO ANCORA? ─────────────────────────
            //
            // Un permesso per una riga che non c'e' piu' e' un buco che resta
            // aperto: il giorno in cui qualcuno riscrive quel punto sbagliando,
            // il permesso lo copre.
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
