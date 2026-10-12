using System;
using System.Collections.Generic;
using static Oce.Solid.Agent.ContractTests.Program;
using static Shared.McpClient;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// ASK THE USER, NOT THE MODEL (entry W2.5).
    ///
    /// ── WHY A SENTENCE IS NOT ENOUGH ─────────────────────────────────────
    ///
    /// The confirmations of this bridge were sentences that the model rereads:
    /// «careful, this operation cannot be undone». A confirmation that goes
    /// through the model is a confirmation **that the model can interpret** — and
    /// interpreting a request for permission means, sooner or later,
    /// granting it to oneself.
    ///
    /// `elicitation/create` makes it reach whoever is in front of the screen.
    ///
    /// ── AND THESE CHECKS ARE OF BEHAVIOUR ─────────────────────────────────
    ///
    /// The fake client **declares** `elicitation`, receives the question, and
    /// answers `decline`. The bridge must stop and **touch nothing** —
    /// and this can be seen without SOLIDWORKS, because the confirmation is asked BEFORE
    /// any conversation with the CAD.
    /// </summary>
    internal static class ConfirmationTests
    {
        internal static void All()
        {
            Group("the confirmation goes to the user, not to the model (W2.5)");

            var executable = BridgeOrNotHere("the confirmation on the real bridge");
            if (executable == null) return;

            // ── 1. A CLIENT THAT KNOWS HOW TO ASK, AND SAYS NO ───────────
            var p = Open(executable);
            if (p == null) { Check("the bridge starts", false, ""); return; }
            try
            {
                Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{\"elicitation\":{}}," +
                    "\"clientInfo\":{\"name\":\"tests\",\"version\":\"1\"}}}");

                // The plan is sent WITHOUT waiting for the answer: what
                // must arrive first is the bridge's QUESTION.
                Write(p,
                    "{\"jsonrpc\":\"2.0\",\"id\":30,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"build\",\"arguments\":{\"steps\":[" +
                    "{\"operation\":\"delete_feature\",\"arguments\":" +
                    "{\"feature_uid\":\"Cut-Extrude1\"}}]}}}");

                // The question must arrive before any answer.
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

                Check("the bridge ASKS before deleting", question != null,
                      question == null ? "no elicitation/create" : "elicitation/create");

                if (question != null)
                {
                    var parameters = Inside(question, "params") as Dictionary<string, object>;
                    var message = Convert.ToString(Inside(parameters, "message"));

                    // THE QUESTION MUST SAY WHAT IS ABOUT TO BE LOST. «Confirm?»
                    // on its own is a button, not a question.
                    Check("and the question names the operation",
                          message != null && message.Contains("delete_feature"),
                          Short(message));
                    Check("and says it cannot be undone",
                          message != null &&
                          message.IndexOf("cannot be undone", StringComparison.OrdinalIgnoreCase) >= 0,
                          Short(message));

                    // ── THE ANSWER IS NO ────────────────────────────────
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

                    Check("and on a «no» it STOPS", text != null && text.Contains("Stopped"),
                          Short(text));

                    // THE MOST IMPORTANT LINE: it is not enough to stop, one must
                    // say that nothing was touched. Whoever reads «stopped»
                    // without that sentence has to go and check the part.
                    Check("and says it touched nothing",
                          text != null &&
                          text.IndexOf("I touched nothing", StringComparison.OrdinalIgnoreCase) >= 0,
                          Short(text));
                }
            }
            finally { Close(p); }

            // ── 2. A CLIENT THAT DOES NOT KNOW HOW TO ASK: THE FALLBACK IS DECLARED ─
            //
            // Here the bridge goes on as before — it is not a worsening, it is
            // the usual behaviour — but **it says so**. An unspoken fallback
            // is the way a defense disappears without anyone
            // noticing.
            var q = Open(executable);
            if (q == null) { Check("the bridge restarts", false, ""); return; }
            try
            {
                Ask(q, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"tests\",\"version\":\"1\"}}}");

                var response = Obj(Ask(q, 31,
                    "{\"jsonrpc\":\"2.0\",\"id\":31,\"method\":\"tools/call\",\"params\":" +
                    "{\"name\":\"build\",\"arguments\":{\"steps\":[" +
                    "{\"operation\":\"delete_feature\",\"arguments\":" +
                    "{\"feature_uid\":\"Cut-Extrude1\"}}]}}}"));

                // Without SOLIDWORKS the plan stops right after, and that is fine: the
                // thing to test is that it **has not stayed hung** waiting for
                // a confirmation that this client does not know how to give.
                Check("with a client that cannot ask, it does NOT hang",
                      response != null,
                      "it answers instead of waiting for an impossible confirmation");
            }
            finally { Close(q); }
        }
    }
}
