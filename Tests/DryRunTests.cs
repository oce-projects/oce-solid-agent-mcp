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
            Group("the dry run: making a mistake without touching the part (Z-G6)");

            var executable = BridgeOrNotHere("the dry run on the real bridge");
            if (executable == null) return;

            var p = Open(executable);
            if (p == null) { Check("the bridge starts", false, "it did not start"); return; }
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

                Check("the dry run answers even with SOLIDWORKS closed",
                      !string.IsNullOrEmpty(text), Short(text));

                Check("and says it is a dry run",
                      text.IndexOf("DRY RUN", StringComparison.OrdinalIgnoreCase) >= 0,
                      Short(text));

                Check("it catches the operation that does not exist",
                      text.Contains("estrudi_rettangolino") && text.Contains("does not exist"),
                      "and tries to suggest similar names");

                Check("it catches the required argument that is missing",
                      text.Contains("width_mm") || text.Contains("thickness_mm"),
                      "on «extrude_rectangle» more than one required argument is missing");

                // THE MOST PRECIOUS OF THE FOUR: a badly written argument gives
                // no error, it is ignored, and the part comes out different
                // from how it was asked for. It is the defect that is not seen.
                Check("takes the UNKNOWN argument, which would be ignored",
                      text.Contains("depth_mm") &&
                      text.IndexOf("IGNORED", StringComparison.OrdinalIgnoreCase) >= 0,
                      "the defect that gives no error");

                Check("it catches the reference to a step that is not there",
                      text.Contains("step 9") ||
                      text.IndexOf("has not been done yet", StringComparison.Ordinal) >= 0,
                      "face_ref pointing to step 9, in a plan of four");

                Check("and the answer is marked as an error",
                      result != null && result.ContainsKey("isError") &&
                      Convert.ToString(result["isError"]).Equals(
                          "True", StringComparison.OrdinalIgnoreCase),
                      "a plan that does not hold is not a neutral answer");

                Check("and it declares what it could NOT check",
                      text.Contains("NOT checked") && text.Contains("uid"),
                      "staying silent about the limits would make people believe the plan is fine");

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

                Check("a good plan is NOT rejected",
                      goodText.Contains("the plan holds"), Short(goodText));

                Check("but even there it says what it did not look at",
                      goodText.Contains("NOT checked"),
                      "above all when it holds");

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
                Check("«dry_run» is declared in the schema of «build»",
                      toolsLine != null &&
                      toolsLine.Contains("\"dry_run\"") &&
                      toolsLine.Contains("DRY RUN"),   // the DESCRIPTION of the tool, which window .9 translates: until then it still says this
                      "a capability that does not show in tools/list is used by nobody");
            }
            finally { Close(p); }
        }
    }
}
