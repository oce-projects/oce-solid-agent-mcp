using System;
using System.Collections.Generic;
using static Oce.Solid.Agent.ContractTests.Program;
using static Shared.McpClient;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// GETTING IT WRONG WITHOUT TOUCHING THE PART (entry Z-G6).
    ///
    /// ── WHY THESE CHECKS ARE WORTH MORE THAN THE OTHERS OF TODAY ──────────
    ///
    /// Almost all the checks written on 23/9/2026 look at a file: the
    /// catalogue, a source, a JSON. These **talk to the real bridge**, over
    /// stdio, as a client would — and they do so **with SOLIDWORKS closed**,
    /// which is exactly the condition in which the dry run must be of use.
    ///
    /// It is not a check of structure: it is a check of behaviour.
    ///
    /// ── AND THE LINE THAT COUNTS THE MOST ─────────────────────────────────
    ///
    /// That the response says **what it did not look at**. A dry run that
    /// stays silent about its limits makes one believe the plan is fine, and then the trouble
    /// arrives one step later instead of at once — with, on top, the trust of whoever
    /// had asked for it.
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

                // ── A PLAN WRONG IN FOUR DIFFERENT WAYS ───────────────────
                //
                // 1. an operation that does not exist
                // 2. a required argument that is missing
                // 3. an UNKNOWN argument, which would be silently ignored
                // 4. a reference to a step that is not there yet
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

                // THE MOST PRECIOUS OF THE FOUR: a badly written argument gives
                // no error, it is ignored, and the part comes out different
                // from how it was asked for. It is the defect that is not seen.
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

                // ── A GOOD PLAN IS NOT REJECTED ───────────────────────────
                //
                // The half that is usually not tested. A check that rejects
                // even what is fine gets abandoned, and then it
                // no longer protects anything.
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

                // ── AND `dry_run` IS DECLARED IN THE SCHEMA ───────────────
                //
                // A capability that exists and is not seen in `tools/list` is a
                // capability that no model will ever use.
                // THE RAW LINE, not the dictionary: `Convert.ToString` on a
                // `Dictionary` gives the name of the type, and this line was born
                // red exactly like that — it looked for «dry_run» inside
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
