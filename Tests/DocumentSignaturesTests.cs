using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// TRE CAMPI CHE DEVONO PARLARE DELLO STESSO PEZZO (voce Z-A1).
    ///
    /// ── IL DIFETTO, TROVATO IL 23/9/2026 ─────────────────────────────────
    ///
    /// Il ponte tiene tre cose sul documento che ha guardato:
    ///
    ///     _guardato        QUALE documento
    ///     _firmaGuardata   com'era, in breve (volume, lavorazioni, file)
    ///     _firmeViste      le firme delle singole facce
    ///
    /// In `RunPlan` c'era una riga che spostava **solo la prima**:
    /// `_guardato = adesso;`. Bastava un piano che nominasse un altro documento
    /// — `document: "..."`, la strada legittima e documentata — perche' da quel
    /// momento `_guardato` indicasse il pezzo nuovo e le firme il pezzo vecchio.
    ///
    /// ── PERCHE' NON SI VEDEVA ────────────────────────────────────────────
    ///
    /// Perche' il guaio non arriva li': arriva **alla chiamata dopo**. A quel
    /// punto il documento davanti e' quello nuovo, `theCheckedOne` torna vero,
    /// e il confronto gira contro le firme di un altro pezzo. Due esiti, tutti
    /// e due sbagliati: rifiutare un piano buono dicendo «il pezzo e'
    /// cambiato», oppure **lasciar passare** un piano su uid che non valgono
    /// piu' — che e' il difetto che questo progetto insegue da settimane.
    ///
    /// ── E IL PRINCIPIO ERA GIA' SCRITTO DUE RIGHE SOPRA ──────────────────
    ///
    /// Il commento della variabile locale `firme` diceva: *«meglio nessun
    /// confronto che un confronto fra due pezzi diversi»*. Era giusto, era li',
    /// e valeva per la variabile locale — non per i campi che restano. **Una
    /// regola scritta per meta' e' una regola che qualcuno applichera' per
    /// meta'.**
    /// </summary>
    internal static class DocumentSignaturesTests
    {
        /// <summary>
        /// Le righe in cui `_guardato` cambia **da solo**, cioe' senza che
        /// nelle otto righe DI CODICE attorno le firme siano rifatte o buttate.
        ///
        /// ── DI CODICE, E NON RIGHE GREZZE ────────────────────────────────
        ///
        /// La prima stesura contava le righe grezze ed e' **nata rossa su un
        /// punto corretto**: i tre campi stavano nello stesso blocco, separati
        /// da venticinque righe di commento che spiegavano perche'. Contare le
        /// righe grezze qui vuol dire punire chi spiega, in un repository in
        /// cui spiegare e' la regola.
        /// </summary>
        internal static List<string> Sole(string[] lines)
        {
            var outside = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                var r = lines[i];
                if (r.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                if (!Regex.IsMatch(r, @"^\s*_seen\s*=")) continue;

                var nearby = new List<string>();
                for (int g = i - 1, takenOnes = 0; g >= 0 && takenOnes < 8; g--)
                {
                    var s = lines[g].TrimStart();
                    if (s.StartsWith("//", StringComparison.Ordinal) || s.Length == 0) continue;
                    nearby.Add(lines[g]); takenOnes++;
                }
                for (int g = i + 1, takenOnes = 0; g < lines.Length && takenOnes < 8; g++)
                {
                    var s = lines[g].TrimStart();
                    if (s.StartsWith("//", StringComparison.Ordinal) || s.Length == 0) continue;
                    nearby.Add(lines[g]); takenOnes++;
                }
                var around = string.Join("\n", nearby);

                bool seenSignatures = Regex.IsMatch(around, @"_seenSignatures\s*=");
                bool lightSignature = Regex.IsMatch(around, @"_signatureSeen\s*=");
                if (!seenSignatures || !lightSignature)
                    outside.Add("riga " + (i + 1) + ": _guardato cambia da solo" +
                              (seenSignatures ? "" : " (manca _firmeViste)") +
                              (lightSignature ? "" : " (manca _firmaGuardata)"));
            }
            return outside;
        }

        internal static void All()
        {
            Group("le firme non sopravvivono al cambio di documento (Z-A1)");

            // ── PRIMA: LA REGOLA SA DIRE DI NO? ──────────────────────────
            //
            // Il difetto vero, ridotto a quattro righe. Se questa non scatta,
            // il verde sul sorgente non vuol dire niente.
            var defect = new[]
            {
                "            var signatures = theCheckedOne ? _seenSignatures : null;",
                "",
                "            _seen = now;",
                "            return Protocol.Content(\"...\", true);",
            };
            Check("la regola prende il difetto vero", Sole(defect).Count == 1,
                  Sole(defect).Count == 1 ? Sole(defect)[0] : "NON l'ha preso");

            // ── E NON PUNISCE CHI SPIEGA ─────────────────────────────────
            //
            // L'altra meta', ed e' quella su cui questo controllo e' gia'
            // caduto una volta: i tre campi insieme, con venticinque righe di
            // commento in mezzo.
            var good = new List<string>
            {
                "                _seenSignatures = StateSignatures(after);",
                "                _signatureSeen = LightSignature(after);",
            };
            for (int k = 0; k < 25; k++) good.Add("                // " + k + " righe di spiegazione");
            good.Add("                _seen = StateIdentity(after);");
            Check("e non punisce chi spiega", Sole(good.ToArray()).Count == 0,
                  "venticinque righe di commento fra le firme e `_seen`");

            // ── POI: IL SORGENTE VERO ────────────────────────────────────
            var file = Path.Combine(Root(), "Tools", "McpTools.cs");
            if (!File.Exists(file))
            {
                Check("il sorgente del ponte c'e'", false, "manca " + file);
                return;
            }
            var lines = File.ReadAllLines(file);
            var sole = Sole(lines);

            Check("nessun `_guardato =` viaggia da solo", sole.Count == 0,
                  sole.Count == 0
                      ? "ogni cambio di documento rifa' o butta le firme"
                      : string.Join(" | ", sole));

            // ── E IL CASO PRECISO CHE IL DIFETTO SFRUTTAVA ───────────────
            var text = File.ReadAllText(file);
            Check("un documento nominato butta le firme vecchie",
                  Regex.IsMatch(text,
                      @"if\s*\(\s*!theCheckedOne\s*\)\s*\{[^}]*_seenSignatures\s*=\s*null[^}]*_signatureSeen\s*=\s*null[^}]*\}\s*(?:\r?\n)\s*_seen\s*=\s*now"),
                  "prima si buttano, poi si sposta `_seen`");

            Check("e accanto c'e' il perche'",
                  text.Contains("meglio nessun confronto che un confronto") ||
                  text.Contains("le firme vecchie si buttano"),
                  "una regola senza il motivo dura fino al primo che la legge");
        }
    }
}
