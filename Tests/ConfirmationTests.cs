using System;
using System.Collections.Generic;
using static Oce.Solid.Agent.ContractTests.Program;
using static Shared.McpClient;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// CHIEDERE ALL'UTENTE, NON AL MODELLO (voce W2.5).
    ///
    /// ── PERCHE' NON BASTA UNA FRASE ──────────────────────────────────────
    ///
    /// Le conferme di questo ponte erano frasi che il modello rilegge:
    /// «attenzione, questa operazione non si annulla». Una conferma che passa
    /// dal modello e' una conferma **che il modello puo' interpretare** — e
    /// interpretare una richiesta di permesso vuol dire, prima o poi,
    /// concedersela da solo.
    ///
    /// `elicitation/create` la fa arrivare a chi sta davanti allo schermo.
    ///
    /// ── E QUESTE PROVE SONO DI COMPORTAMENTO ─────────────────────────────
    ///
    /// Il client finto **dichiara** `elicitation`, riceve la domanda, e
    /// risponde `decline`. Il ponte deve fermarsi e **non toccare niente** —
    /// e lo si vede senza SOLIDWORKS, perche' la conferma si chiede PRIMA di
    /// qualunque conversazione col CAD.
    /// </summary>
    internal static class ConfirmationTests
    {
        internal static void All()
        {
            Group("la conferma all'utente, non al modello (W2.5)");

            var executable = BridgeOrNotHere("la conferma sul ponte vero");
            if (executable == null) return;

            // ── 1. UN CLIENT CHE SA CHIEDERE, E DICE DI NO ───────────────
            var p = Open(executable);
            if (p == null) { Check("il ponte parte", false, ""); return; }
            try
            {
                Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{\"elicitation\":{}}," +
                    "\"clientInfo\":{\"name\":\"prove\",\"version\":\"1\"}}}");

                // Si manda il piano SENZA aspettare la risposta: quello che
                // deve arrivare per primo e' la DOMANDA del ponte.
                Write(p,
                    "{\"jsonrpc\":\"2.0\",\"id\":30,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"build\",\"arguments\":{\"steps\":[" +
                    "{\"operation\":\"delete_feature\",\"arguments\":" +
                    "{\"feature_uid\":\"Taglio-Estrusione1\"}}]}}}");

                // La domanda deve arrivare prima di qualunque risposta.
                Dictionary<string, object> question = null;
                object questionId = null;
                for (int i = 0; i < 8 && question == null; i++)
                {
                    var line = Line(p, 15000);
                    if (line == null) break;
                    var o = Obj(line);
                    if (o == null) continue;
                    if (Convert.ToString(Inside(o, "method")) == "elicitation/create")
                    { question = o; questionId = Inside(o, "id"); }
                }

                Check("il ponte CHIEDE prima di eliminare", question != null,
                      question == null ? "nessuna elicitation/create" : "elicitation/create");

                if (question != null)
                {
                    var parameters = Inside(question, "params") as Dictionary<string, object>;
                    var message = Convert.ToString(Inside(parameters, "message"));

                    // LA DOMANDA DEVE DIRE COSA SI STA PER PERDERE. «Confermi?»
                    // da solo e' un pulsante, non una domanda.
                    Check("e la domanda nomina l'operazione",
                          message != null && message.Contains("delete_feature"),
                          Short(message));
                    Check("e dice che non si annulla",
                          message != null &&
                          message.IndexOf("non si annulla", StringComparison.OrdinalIgnoreCase) >= 0,
                          Short(message));

                    // ── SI RISPONDE DI NO ────────────────────────────────
                    Write(p,
                        "{\"jsonrpc\":\"2.0\",\"id\":" + Convert.ToString(questionId) +
                        ",\"result\":{\"action\":\"decline\"}}");

                    string text = null;
                    for (int i = 0; i < 8 && text == null; i++)
                    {
                        var line = Line(p, 15000);
                        if (line == null) break;
                        var o = Obj(line);
                        if (o == null) continue;
                        if (Convert.ToString(Inside(o, "id")) != "30") continue;
                        text = TextOf(Inside(o, "result") as Dictionary<string, object>);
                    }

                    Check("e a un «no» SI FERMA", text != null && text.Contains("Fermato"),
                          Short(text));

                    // LA RIGA PIU' IMPORTANTE: non basta fermarsi, bisogna
                    // dire che non si e' toccato niente. Chi legge «fermato»
                    // senza quella frase deve andare a controllare il pezzo.
                    Check("e dice di non aver toccato niente",
                          text != null &&
                          text.IndexOf("non ho toccato niente", StringComparison.OrdinalIgnoreCase) >= 0,
                          Short(text));
                }
            }
            finally { Close(p); }

            // ── 2. UN CLIENT CHE NON SA CHIEDERE: IL RIPIEGO E' DICHIARATO ─
            //
            // Qui il ponte va avanti come prima — non e' un peggioramento, e'
            // il comportamento di sempre — ma **lo dice**. Un ripiego taciuto
            // e' il modo in cui una difesa sparisce senza che nessuno se ne
            // accorga.
            var q = Open(executable);
            if (q == null) { Check("il ponte riparte", false, ""); return; }
            try
            {
                Ask(q, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"prove\",\"version\":\"1\"}}}");

                var response = Obj(Ask(q, 31,
                    "{\"jsonrpc\":\"2.0\",\"id\":31,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"build\",\"arguments\":{\"steps\":[" +
                    "{\"operation\":\"delete_feature\",\"arguments\":" +
                    "{\"feature_uid\":\"Taglio-Estrusione1\"}}]}}}"));

                // Senza SOLIDWORKS il piano si ferma subito dopo, e va bene: la
                // cosa da provare e' che **non sia rimasto appeso** ad aspettare
                // una conferma che questo client non sa dare.
                Check("con un client che non sa chiedere, NON resta appeso",
                      response != null,
                      "risponde invece di aspettare una conferma impossibile");
            }
            finally { Close(q); }
        }
    }
}
