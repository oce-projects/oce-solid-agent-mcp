using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THREE FIELDS THAT MUST SPEAK OF THE SAME PART (entry Z-A1).
    ///
    /// ── THE DEFECT, FOUND ON 23/9/2026 ───────────────────────────────────
    ///
    /// The bridge keeps three things about the document it has looked at:
    ///
    ///     _guardato        WHICH document
    ///     _signatureSeen   how it was, in short (volume, features, files)
    ///     _seenSignatures      the signatures of the individual faces
    ///
    /// In `RunPlan` there was a line that moved **only the first**:
    /// `_guardato = adesso;`. It was enough for a plan to name another document
    /// — `document: "..."`, the legitimate and documented way — for, from that
    /// moment on, `_guardato` to point to the new part and the signatures to the old one.
    ///
    /// ── WHY IT WAS NOT SEEN ───────────────────────────────────────────────
    ///
    /// Because the trouble does not arrive there: it arrives **at the next call**. At that
    /// point the document in front is the new one, `theCheckedOne` comes back true,
    /// and the comparison runs against the signatures of another part. Two outcomes, both
    /// wrong: refusing a good plan saying «il pezzo e'
    /// cambiato», or **letting through** a plan on uids that are no
    /// longer valid — which is the defect this project has chased for weeks.
    ///
    /// ── AND THE PRINCIPLE WAS ALREADY WRITTEN TWO LINES ABOVE ─────────────
    ///
    /// The comment of the local variable `firme` said: *«better no
    /// comparison than a comparison between two different parts»*. It was right, it was there,
    /// and it held for the local variable — not for the fields that remain. **A
    /// rule written by half is a rule that somebody will apply by
    /// half.**
    /// </summary>
    internal static class DocumentSignaturesTests
    {
        /// <summary>
        /// The lines in which `_guardato` changes **by itself**, that is, without
        /// the signatures being redone or thrown away in the eight lines OF CODE around it.
        ///
        /// ── OF CODE, AND NOT RAW LINES ────────────────────────────────
        ///
        /// The first draft counted raw lines and was **born red on a
        /// correct point**: the three fields stood in the same block, separated
        /// by twenty-five lines of comments explaining why. Counting
        /// raw lines here means punishing whoever explains, in a repository in
        /// which explaining is the rule.
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
                    outside.Add("riga " + (i + 1) + ": _seen cambia da solo" +
                              (seenSignatures ? "" : " (manca _seenSignatures)") +
                              (lightSignature ? "" : " (manca _signatureSeen)"));
            }
            return outside;
        }

        internal static void All()
        {
            Group("le firme non sopravvivono al cambio di documento (Z-A1)");

            // ── FIRST: CAN THE RULE SAY NO? ──────────────────────────────
            //
            // The real defect, reduced to four lines. If this one does not trigger,
            // the green on the source means nothing.
            var defect = new[]
            {
                "            var signatures = theCheckedOne ? _seenSignatures : null;",
                "",
                "            _seen = now;",
                "            return Protocol.Content(\"...\", true);",
            };
            Check("la regola prende il difetto vero", Sole(defect).Count == 1,
                  Sole(defect).Count == 1 ? Sole(defect)[0] : "NON l'ha preso");

            // ── AND IT DOES NOT PUNISH WHOEVER EXPLAINS ───────────────────
            //
            // The other half, and it is the one on which this check has already
            // fallen once: the three fields together, with twenty-five lines of
            // comments in between.
            var good = new List<string>
            {
                "                _seenSignatures = StateSignatures(after);",
                "                _signatureSeen = LightSignature(after);",
            };
            for (int k = 0; k < 25; k++) good.Add("                // " + k + " righe di spiegazione");
            good.Add("                _seen = StateIdentity(after);");
            Check("e non punisce chi spiega", Sole(good.ToArray()).Count == 0,
                  "venticinque righe di commento fra le firme e `_seen`");

            // ── THEN: THE REAL SOURCE ─────────────────────────────────────
            var file = Path.Combine(Root(), "Tools", "McpTools.cs");
            if (!File.Exists(file))
            {
                Check("il sorgente del ponte c'e'", false, "manca " + file);
                return;
            }
            var lines = File.ReadAllLines(file);
            var sole = Sole(lines);

            Check("nessun `_seen =` viaggia da solo", sole.Count == 0,
                  sole.Count == 0
                      ? "ogni cambio di documento rifa' o butta le firme"
                      : string.Join(" | ", sole));

            // ── AND THE PRECISE CASE THAT THE DEFECT EXPLOITED ────────────
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
