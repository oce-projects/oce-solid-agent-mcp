using System;
using System.Collections.Generic;
using System.Linq;
using static Oce.Solid.Agent.ContractTests.Program;
using static Shared.McpClient;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// TEN CALLS AT ONCE (brief R14). On 25/10/2026 six «api_search» sent
    /// together got one «Connection closed»; the same call alone answered. Here
    /// ten calls are written on the stdin of the bridge one after the other,
    /// without waiting for any answer (six «api_search», two «list_operations»,
    /// two «describe_operation»), as a client that calls in parallel does. Each
    /// id must get exactly one answer, every line on stdout must be whole JSON
    /// (a line written by two threads at once is not), and the bridge must still
    /// be alive after. The bridge is started with OCE_NO_ATTACH=1: none of these
    /// tools needs SOLIDWORKS, and the check must not touch a session that is
    /// open.
    /// </summary>
    internal static class R14ParallelCallsContract
    {
        internal static void RunAll()
        {
            Group("ten calls at once, each answered once and whole (brief R14)");
            const string label = "ten tools/call written without waiting: ten answers, one per id, every line JSON, the bridge alive";
            var exe = BridgeOrNotHere(label);
            if (exe == null) return;
            string before = Environment.GetEnvironmentVariable("OCE_NO_ATTACH");
            Bridge p;
            try
            {
                Environment.SetEnvironmentVariable("OCE_NO_ATTACH", "1");
                p = Open(exe);
            }
            finally { Environment.SetEnvironmentVariable("OCE_NO_ATTACH", before); }
            if (p == null) { Check(label, false, "the bridge did not start"); return; }
            try
            {
                var init = Ask(p, 1,
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":" +
                    "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"tests\",\"version\":\"1\"}}}");
                if (init == null) { Check(label, false, "initialize did not answer"); return; }
                Write(p, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");

                var queries = new[] { "GetTwoAdjacentFaces2", "AddMate5", "FeatureExtrusion3", "SelectByID2", "GetBodies2", "InsertSketch" };
                var calls = new List<string>();
                for (int i = 0; i < queries.Length; i++)
                    calls.Add("{\"name\":\"api_search\",\"arguments\":{\"query\":\"" + queries[i] + "\"}}");
                calls.Add("{\"name\":\"list_operations\",\"arguments\":{}}");
                calls.Add("{\"name\":\"list_operations\",\"arguments\":{\"family\":\"assemblies\"}}");
                calls.Add("{\"name\":\"describe_operation\",\"arguments\":{\"name\":\"extrude_profile\"}}");
                calls.Add("{\"name\":\"describe_operation\",\"arguments\":{\"name\":\"mate\"}}");

                int notJsonBefore = p.LinesNotJson.Count;
                for (int i = 0; i < calls.Count; i++)
                    Write(p, "{\"jsonrpc\":\"2.0\",\"id\":" + (101 + i) + ",\"method\":\"tools/call\",\"params\":" + calls[i] + "}");

                var answered = new Dictionary<int, int>();
                var errors = new List<string>();
                int expiry = Environment.TickCount + TimeoutMs;
                while (answered.Count < calls.Count && Environment.TickCount < expiry)
                {
                    var line = Line(p, Math.Max(200, expiry - Environment.TickCount));
                    if (line == null) break;
                    var o = Obj(line);
                    if (o == null || !o.ContainsKey("id") || o["id"] == null) continue;
                    int id;
                    if (!int.TryParse(Convert.ToString(o["id"]), out id) || id < 101 || id > 100 + calls.Count) continue;
                    answered[id] = answered.ContainsKey(id) ? answered[id] + 1 : 1;
                    if (o.ContainsKey("error")) errors.Add(id + ": " + Short(Convert.ToString(Inside(o, "error"))));
                }
                // A late duplicate would arrive right after: a short wait more.
                var extra = Line(p, 500);
                while (extra != null)
                {
                    var o = Obj(extra);
                    int id;
                    if (o != null && o.ContainsKey("id") && int.TryParse(Convert.ToString(o["id"]), out id) && answered.ContainsKey(id))
                        answered[id]++;
                    extra = Line(p, 200);
                }

                var wrong = new List<string>();
                var missing = Enumerable.Range(101, calls.Count).Where(id => !answered.ContainsKey(id)).ToList();
                if (missing.Count > 0) wrong.Add("no answer for " + string.Join(", ", missing));
                var twice = answered.Where(kv => kv.Value > 1).Select(kv => kv.Key.ToString()).ToList();
                if (twice.Count > 0) wrong.Add("answered more than once: " + string.Join(", ", twice));
                if (errors.Count > 0) wrong.Add("protocol errors: " + string.Join("; ", errors));
                int notJson = p.LinesNotJson.Count - notJsonBefore;
                if (notJson > 0) wrong.Add(notJson + " line(s) on stdout that are not JSON: " + Short(p.LinesNotJson[p.LinesNotJson.Count - 1]));
                bool alive = !p.ServerProcess.HasExited;
                if (!alive) wrong.Add("the bridge exited");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? calls.Count + " answers, one per id, the bridge alive" : string.Join("; ", wrong));
            }
            finally { Close(p); }
        }
    }
}
