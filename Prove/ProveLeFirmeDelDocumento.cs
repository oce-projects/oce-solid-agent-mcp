using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
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
    /// In `EseguiIlPiano` c'era una riga che spostava **solo la prima**:
    /// `_guardato = adesso;`. Bastava un piano che nominasse un altro documento
    /// — `document: "..."`, la strada legittima e documentata — perche' da quel
    /// momento `_guardato` indicasse il pezzo nuovo e le firme il pezzo vecchio.
    ///
    /// ── PERCHE' NON SI VEDEVA ────────────────────────────────────────────
    ///
    /// Perche' il guaio non arriva li': arriva **alla chiamata dopo**. A quel
    /// punto il documento davanti e' quello nuovo, `quelloGuardato` torna vero,
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
    internal static class ProveLeFirmeDelDocumento
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
        internal static List<string> Sole(string[] righe)
        {
            var fuori = new List<string>();
            for (int i = 0; i < righe.Length; i++)
            {
                var r = righe[i];
                if (r.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                if (!Regex.IsMatch(r, @"^\s*_guardato\s*=")) continue;

                var vicine = new List<string>();
                for (int g = i - 1, presi = 0; g >= 0 && presi < 8; g--)
                {
                    var s = righe[g].TrimStart();
                    if (s.StartsWith("//", StringComparison.Ordinal) || s.Length == 0) continue;
                    vicine.Add(righe[g]); presi++;
                }
                for (int g = i + 1, presi = 0; g < righe.Length && presi < 8; g++)
                {
                    var s = righe[g].TrimStart();
                    if (s.StartsWith("//", StringComparison.Ordinal) || s.Length == 0) continue;
                    vicine.Add(righe[g]); presi++;
                }
                var intorno = string.Join("\n", vicine);

                bool firmeViste = Regex.IsMatch(intorno, @"_firmeViste\s*=");
                bool firmaLeggera = Regex.IsMatch(intorno, @"_firmaGuardata\s*=");
                if (!firmeViste || !firmaLeggera)
                    fuori.Add("riga " + (i + 1) + ": _guardato cambia da solo" +
                              (firmeViste ? "" : " (manca _firmeViste)") +
                              (firmaLeggera ? "" : " (manca _firmaGuardata)"));
            }
            return fuori;
        }

        internal static void Tutte()
        {
            Gruppo("le firme non sopravvivono al cambio di documento (Z-A1)");

            // ── PRIMA: LA REGOLA SA DIRE DI NO? ──────────────────────────
            //
            // Il difetto vero, ridotto a quattro righe. Se questa non scatta,
            // il verde sul sorgente non vuol dire niente.
            var difetto = new[]
            {
                "            var firme = quelloGuardato ? _firmeViste : null;",
                "",
                "            _guardato = adesso;",
                "            return Protocollo.Contenuto(\"...\", true);",
            };
            Segna("la regola prende il difetto vero", Sole(difetto).Count == 1,
                  Sole(difetto).Count == 1 ? Sole(difetto)[0] : "NON l'ha preso");

            // ── E NON PUNISCE CHI SPIEGA ─────────────────────────────────
            //
            // L'altra meta', ed e' quella su cui questo controllo e' gia'
            // caduto una volta: i tre campi insieme, con venticinque righe di
            // commento in mezzo.
            var buono = new List<string>
            {
                "                _firmeViste = FirmeDelloStato(dopo);",
                "                _firmaGuardata = FirmaLeggera(dopo);",
            };
            for (int k = 0; k < 25; k++) buono.Add("                // " + k + " righe di spiegazione");
            buono.Add("                _guardato = IdentitaDelloStato(dopo);");
            Segna("e non punisce chi spiega", Sole(buono.ToArray()).Count == 0,
                  "venticinque righe di commento fra le firme e `_guardato`");

            // ── POI: IL SORGENTE VERO ────────────────────────────────────
            var file = Path.Combine(Radice(), "Strumenti", "Attrezzi.cs");
            if (!File.Exists(file))
            {
                Segna("il sorgente del ponte c'e'", false, "manca " + file);
                return;
            }
            var righe = File.ReadAllLines(file);
            var sole = Sole(righe);

            Segna("nessun `_guardato =` viaggia da solo", sole.Count == 0,
                  sole.Count == 0
                      ? "ogni cambio di documento rifa' o butta le firme"
                      : string.Join(" | ", sole));

            // ── E IL CASO PRECISO CHE IL DIFETTO SFRUTTAVA ───────────────
            var testo = File.ReadAllText(file);
            Segna("un documento nominato butta le firme vecchie",
                  Regex.IsMatch(testo,
                      @"if\s*\(\s*!quelloGuardato\s*\)\s*\{[^}]*_firmeViste\s*=\s*null[^}]*_firmaGuardata\s*=\s*null[^}]*\}\s*(?:\r?\n)\s*_guardato\s*=\s*adesso"),
                  "prima si buttano, poi si sposta `_guardato`");

            Segna("e accanto c'e' il perche'",
                  testo.Contains("meglio nessun confronto che un confronto") ||
                  testo.Contains("le firme vecchie si buttano"),
                  "una regola senza il motivo dura fino al primo che la legge");
        }
    }
}
