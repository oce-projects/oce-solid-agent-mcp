using System;
using System.Collections.Generic;
using static Oce.Solid.Agent.ContractTests.Program;
using static Shared.McpClient;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// SBAGLIARE SENZA TOCCARE IL PEZZO (voce Z-G6).
    ///
    /// ── PERCHE' QUESTE PROVE VALGONO PIU' DELLE ALTRE DI OGGI ────────────
    ///
    /// Quasi tutti i controlli scritti il 23/9/2026 guardano un file: il
    /// catalogo, un sorgente, un JSON. Questi **parlano col ponte vero**, su
    /// stdio, come farebbe un client — e lo fanno **con SOLIDWORKS chiuso**,
    /// che e' esattamente la condizione in cui la prova a vuoto deve servire.
    ///
    /// Non e' una prova di struttura: e' una prova di comportamento.
    ///
    /// ── E LA RIGA CHE CONTA DI PIU' ──────────────────────────────────────
    ///
    /// Che la risposta dica **cosa non ha guardato**. Una prova a vuoto che
    /// tace sui limiti fa credere che il piano sia a posto, e allora il guaio
    /// arriva un passo dopo invece che subito — con in piu' la fiducia di chi
    /// l'aveva chiesta.
    /// </summary>
    internal static class DryRunTests
    {
        internal static void All()
        {
            Group("la prova a vuoto: sbagliare senza toccare il pezzo (Z-G6)");

            var executable = BridgeOrNotHere("la prova a vuoto sul ponte vero");
            if (executable == null) return;

            var p = Open(executable);
            if (p == null) { Check("il ponte parte", false, "non si e' avviato"); return; }
            try
            {
                Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"prove\",\"version\":\"1\"}}}");

                // ── UN PIANO SBAGLIATO IN QUATTRO MODI DIVERSI ───────────
                //
                // 1. un'operazione che non esiste
                // 2. un argomento obbligatorio che manca
                // 3. un argomento SCONOSCIUTO, che verrebbe ignorato in silenzio
                // 4. un rimando a un passo che non c'e' ancora
                var bad = Obj(Ask(p, 20,
                    "{\"jsonrpc\":\"2.0\",\"id\":20,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"build\",\"arguments\":{\"dry_run\":true,\"steps\":[" +
                    "{\"operation\":\"estrudi_rettangolino\"}," +
                    "{\"operation\":\"extrude_rectangle\",\"arguments\":{\"length_mm\":100}}," +
                    "{\"operation\":\"extrude_circle\",\"arguments\":" +
                    "{\"diameter_mm\":40,\"height_mm\":10,\"depth_mm\":5}}," +
                    "{\"operation\":\"shell\",\"arguments\":{\"thickness_mm\":3," +
                    "\"face_ref\":{\"step\":9,\"kind\":\"top_face\"}}}" +
                    "]}}}"));

                var result = Inside(bad, "result") as Dictionary<string, object>;
                var text = TextOf(result);

                Check("la prova a vuoto risponde anche con SOLIDWORKS chiuso",
                      !string.IsNullOrEmpty(text), Short(text));

                Check("e dice che e' una prova a vuoto",
                      text.IndexOf("PROVA A VUOTO", StringComparison.OrdinalIgnoreCase) >= 0,
                      Short(text));

                Check("prende l'operazione che non esiste",
                      text.Contains("estrudi_rettangolino") && text.Contains("non esiste"),
                      "e prova a suggerire nomi simili");

                Check("prende l'argomento obbligatorio che manca",
                      text.Contains("width_mm") || text.Contains("thickness_mm"),
                      "su «extrude_rectangle» manca piu' di un obbligatorio");

                // IL PIU' PREZIOSO DEI QUATTRO: un argomento scritto male non
                // da' nessun errore, viene ignorato, e il pezzo esce diverso
                // da come lo si e' chiesto. E' il difetto che non si vede.
                Check("prende l'argomento SCONOSCIUTO, che verrebbe ignorato",
                      text.Contains("depth_mm") &&
                      text.IndexOf("IGNORATO", StringComparison.OrdinalIgnoreCase) >= 0,
                      "il difetto che non da' errore");

                Check("prende il rimando a un passo che non c'e'",
                      text.Contains("passo 9") ||
                      text.IndexOf("non e' ancora stato fatto", StringComparison.Ordinal) >= 0,
                      "face_ref verso il passo 9, in un piano di quattro");

                Check("e la risposta e' marcata come errore",
                      result != null && result.ContainsKey("isError") &&
                      Convert.ToString(result["isError"]).Equals(
                          "True", StringComparison.OrdinalIgnoreCase),
                      "un piano che non regge non e' una risposta neutra");

                Check("e dichiara cosa NON ha potuto controllare",
                      text.Contains("NON controllati") && text.Contains("uid"),
                      "tacere sui limiti farebbe credere che il piano sia a posto");

                // ── UN PIANO BUONO NON VIENE BOCCIATO ────────────────────
                //
                // La meta' che di solito non si prova. Un controllo che boccia
                // anche cio' che va bene si smette di usarlo, e allora non
                // protegge piu' niente.
                var good = Obj(Ask(p, 21,
                    "{\"jsonrpc\":\"2.0\",\"id\":21,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"build\",\"arguments\":{\"dry_run\":true,\"steps\":[" +
                    "{\"operation\":\"extrude_rectangle\",\"arguments\":" +
                    "{\"plane\":\"front\",\"length_mm\":120,\"width_mm\":80," +
                    "\"thickness_mm\":20}}]}}}"));
                var itsGoodOne = Inside(good, "result") as Dictionary<string, object>;
                var goodText = TextOf(itsGoodOne);

                Check("un piano buono NON viene bocciato",
                      goodText.Contains("il piano regge"), Short(goodText));

                Check("ma anche li' dice cosa non ha guardato",
                      goodText.Contains("NON controllati"),
                      "soprattutto quando regge");

                // ── E `dry_run` E' DICHIARATO NELLO SCHEMA ─────────────────
                //
                // Una capacita' che c'e' e non si vede in `tools/list` e' una
                // capacita' che nessun modello usera' mai.
                // LA RIGA GREZZA, non il dizionario: `Convert.ToString` su un
                // `Dictionary` da' il nome del tipo, e questa riga era nata
                // rossa proprio cosi' — cercava «dry_run» dentro
                // «System.Collections.Generic.Dictionary...».
                var toolsLine = Ask(p, 22,
                    "{\"jsonrpc\":\"2.0\",\"id\":22,\"method\":\"tools/list\",\"params\":{}}");
                Check("«dry_run» e' dichiarato nello schema di «build»",
                      toolsLine != null &&
                      toolsLine.Contains("\"dry_run\"") &&
                      toolsLine.Contains("PROVA A VUOTO"),
                      "una capacita' che non si vede in tools/list non la usa nessuno");
            }
            finally { Close(p); }
        }
    }
}
