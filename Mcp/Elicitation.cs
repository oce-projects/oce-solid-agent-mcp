using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Oce.Solid.Agent.Mcp
{
    /// <summary>
    /// ASK THE USER, NOT THE MODEL (entry W2.5).
    ///
    /// ── THE PROBLEM ──────────────────────────────────────────────────────
    ///
    /// The confirmations of this bridge are **sentences that the model rereads**:
    /// «attention, this operation cannot be undone». A confirmation that goes
    /// through the model is a confirmation that the model can interpret — and
    /// to interpret a request for confirmation means, sooner or later,
    /// granting it to oneself.
    ///
    /// The protocol has a way to ask **the user**: `elicitation/create`
    /// (MCP 2025-06-18). The server sends a request, the client shows it to
    /// whoever is in front of the screen, and answers `accept`, `decline` or `cancel`.
    ///
    /// ── THE HARD PART: THE LOOP IS SYNCHRONOUS ───────────────────────────
    ///
    /// This bridge reads a line, serves it, answers. To ask something
    /// **in the middle** of a call one has to write a request and then
    /// read from `stdin` until the answer with that id arrives.
    ///
    /// And while waiting **something else** can arrive: another request from the
    /// client, a notification. Throwing it away would be a silent defect — the client
    /// would wait forever for an answer that nobody will give it. So it is
    /// put aside, and the main loop serves it as soon as it can.
    ///
    /// ── AND IF THE CLIENT CANNOT ASK ───────────────────────────────────
    ///
    /// Not all clients support elicitation. The fallback is **declared,
    /// not silent**: it goes on as before — which is the behavior
    /// it always had, not a worsening — but the answer **says** that the confirmation
    /// could not be asked. A fallback kept quiet is the way a
    /// defense disappears without anyone noticing.
    /// </summary>
    public static class Elicitation
    {
        /// <summary>
        /// The lines that arrived while an answer was awaited, which the main
        /// loop still has to serve. See the comment at the top.
        /// </summary>
        private static readonly ConcurrentQueue<string> _backlog =
            new ConcurrentQueue<string>();

        /// <summary>Has the client declared that it can ask the user?</summary>
        public static bool ClientCanAsk { get; private set; }

        /// <summary>
        /// It is read from `initialize`: `capabilities.elicitation`. A capability
        /// that is not declared **is not presumed**: asking a client that cannot
        /// answer would mean hanging the call until the deadline.
        /// </summary>
        public static void ReadCapabilities(Dictionary<string, object> parameters)
        {
            ClientCanAsk = false;
            if (parameters == null) return;
            object c;
            if (!parameters.TryGetValue("capabilities", out c)) return;
            var capacity = c as Dictionary<string, object>;
            if (capacity == null) return;
            ClientCanAsk = capacity.ContainsKey("elicitation");
        }

        /// <summary>The next line put aside, or null.</summary>
        public static string Backlog()
        {
            string line;
            return _backlog.TryDequeue(out line) ? line : null;
        }

        private static int _nextId = 9000;

        /// <summary>
        /// Asks the user for confirmation and returns **true only if they said yes**.
        ///
        /// It returns false for `decline`, for `cancel`, for an answer that is not
        /// understood and for the deadline. *Doubt counts as no*: on an operation that
        /// cannot be undone, interpreting a silence as a yes would be the
        /// worst defect of all.
        /// </summary>
        public static bool Confirm(TextWriter output, TextReader input,
                                    string message, int expiryMs = 300000)
        {
            if (output == null || input == null) return false;

            int id = System.Threading.Interlocked.Increment(ref _nextId);
            Protocol.Send(output, new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = "elicitation/create",
                ["params"] = new Dictionary<string, object>
                {
                    ["message"] = message,
                    // AN EMPTY SCHEMA, and it is not laziness: here one does not ask for a
                    // piece of data, one asks for a permission. The answer that counts is
                    // `action`, and asking for one more field would mean making the user
                    // fill in a form to say yes.
                    ["requestedSchema"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>(),
                    },
                },
            });

            var fine = Environment.TickCount + expiryMs;
            while (Environment.TickCount < fine)
            {
                string line;
                try { line = input.ReadLine(); }
                catch { return false; }
                if (line == null) return false;          // the client has gone away
                if (string.IsNullOrWhiteSpace(line)) continue;

                Dictionary<string, object> m;
                try { m = Protocol.FromJson(line); }
                catch { _backlog.Enqueue(line); continue; }
                if (m == null) continue;

                object its;
                if (!m.TryGetValue("id", out its) ||
                    Convert.ToString(its) != id.ToString())
                {
                    // NOT MY ANSWER: it is put aside. Throwing it away
                    // would leave the client waiting forever.
                    _backlog.Enqueue(line);
                    continue;
                }

                var result = (m.TryGetValue("result", out var r)
                    ? r as Dictionary<string, object> : null);
                if (result == null) return false;      // an `error` is a no
                object action;
                if (!result.TryGetValue("action", out action)) return false;
                return string.Equals(Convert.ToString(action), "accept",
                                     StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
    }
}
