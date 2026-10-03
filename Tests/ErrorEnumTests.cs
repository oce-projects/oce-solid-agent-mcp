using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
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
    /// SOLIDWORKS 2021 SP5.1 — `Controlli\enum-di-errore.ps1`, che rifa' la
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
    internal static class ProveEnumDiErrore
    {
        /// <summary>
        /// Un errore o uno stato confrontato con una CIFRA invece che col
        /// membro che ha il nome.
        ///
        /// Solo `==` e `!=`: `&gt; 0.5` su una tolleranza non c'entra niente, e
        /// un controllo che urla sulle tolleranze si smette di leggere.
        /// </summary>
        private static readonly Regex Sospetto = new Regex(
            @"\b\w*(?:err|errore|error|stato|status)\w*\s*(?:==|!=)\s*\d+(?![\d.])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Le righe che hanno il permesso, e perche'. Il perche' e' una MISURA,
        /// non un'opinione: chi aggiunge una riga qui deve aver letto il valore
        /// dell'enum, non averlo supposto.
        /// </summary>
        private static readonly Dictionary<string, string> Permesse =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                {
                    "errore != 0 || oggetto == null",
                    "GetObjectByPersistReference3: swPersistReferencedObject_Ok vale " +
                    "**0** (Invalid 1, Suppressed 2, Deleted 4), letto per " +
                    "riflessione il 23/9/2026. Qui lo zero e' davvero «tutto bene»."
                },
                {
                    "inErrore == 1",
                    "NON E' UN CODICE DI ERRORE: e' il CONTEGGIO delle lavorazioni " +
                    "in errore (`features_in_error` dello stato), confrontato " +
                    "con 1 per scegliere fra «lavorazione» e «features». " +
                    "Trovata da questa guardia al suo primo giro, il 23/9/2026: " +
                    "il nome dice «error» e il valore e' un numero di cose."
                },
            };

        internal static void Tutte()
        {
            Gruppo("gli errori non si confrontano con una cifra (Z-C7)");

            // ── PRIMA: LA GUARDIA HA I DENTI? ────────────────────────────
            //
            // Un controllo statico su sorgenti puliti non scatta mai, e uno che
            // non scatta mai e' indistinguibile da uno rotto. Prima di dire
            // «nessuna riga sospetta» si dimostra che le riconoscerebbe.
            var devonoScattare = new[]
            {
                "if (errore != 0) return null;",
                "if (error == 0) { }",
                "while (statoUscita != 1)",
                "if (ErroreMate == 0) return;",
            };
            int prese = 0;
            foreach (var r in devonoScattare) if (Sospetto.IsMatch(r)) prese++;
            Segna("la guardia riconosce i confronti con una cifra",
                  prese == devonoScattare.Length,
                  prese + " su " + devonoScattare.Length);

            // E NON DEVE URLARE SU TUTTO. Una guardia che segnala anche cio' che
            // va bene viene disattivata, ed e' il modo piu' comune in cui un
            // controllo muore.
            var devonoTacere = new[]
            {
                "if (errore != (int)swAddMateError_e.swAddMateError_NoError)",
                "if (erroreU > 0.5 || erroreV > 0.5)",
                "if (errori.Count > 0) return;",
                "var stato = 0;",
            };
            var urlate = new List<string>();
            foreach (var r in devonoTacere) if (Sospetto.IsMatch(r)) urlate.Add(r.Trim());
            Segna("e tace su quello che va bene",
                  urlate.Count == 0,
                  urlate.Count == 0 ? "quattro righe legittime, zero falsi allarmi"
                                    : "falsi allarmi: " + string.Join(" | ", urlate));

            // ── POI: I SORGENTI ──────────────────────────────────────────
            var cartella = Path.Combine(Radice(), "Riscontro.Disegno");
            if (!Directory.Exists(cartella))
            {
                Segna("la cartella della libreria c'e'", false, "manca " + cartella);
                return;
            }

            var trovate = new List<string>();
            int guardate = 0;
            foreach (var file in Directory.GetFiles(cartella, "*.cs", SearchOption.AllDirectories))
            {
                var righe = File.ReadAllLines(file);
                for (int i = 0; i < righe.Length; i++)
                {
                    var r = righe[i];
                    var nudo = r.TrimStart();

                    // I COMMENTI DI QUESTO REPOSITORY PARLANO DEL PROBLEMA, e
                    // spesso citano la riga sbagliata per spiegarla. Contarli
                    // vorrebbe dire che spiegare un difetto lo fa diventare un
                    // difetto.
                    if (nudo.StartsWith("//", StringComparison.Ordinal)) continue;
                    guardate++;

                    var m = Sospetto.Match(r);
                    if (!m.Success) continue;

                    bool permessa = false;
                    foreach (var p in Permesse.Keys)
                        if (r.Contains(p)) { permessa = true; break; }
                    if (permessa) continue;

                    trovate.Add(Path.GetFileName(file) + ":" + (i + 1) + "  " + m.Value.Trim());
                }
            }

            Segna("nessun errore confrontato con una cifra",
                  trovate.Count == 0,
                  trovate.Count == 0
                      ? guardate + " righe di codice guardate, " + Permesse.Count +
                        " eccezione dichiarata"
                      : string.Join(" | ", trovate));

            // ── E LE ECCEZIONI ESISTONO ANCORA? ─────────────────────────
            //
            // Un permesso per una riga che non c'e' piu' e' un buco che resta
            // aperto: il giorno in cui qualcuno riscrive quel punto sbagliando,
            // il permesso lo copre.
            foreach (var p in Permesse)
            {
                bool viva = false;
                foreach (var file in Directory.GetFiles(cartella, "*.cs", SearchOption.AllDirectories))
                    if (File.ReadAllText(file).Contains(p.Key)) { viva = true; break; }
                Segna("l'eccezione dichiarata esiste ancora: «" + p.Key + "»", viva,
                      viva ? p.Value : "NON c'e' piu' nel codice: togli il permesso");
            }
        }
    }
}
