using System;
using System.Collections.Generic;

namespace Oce.Solid.Agent.Mcp
{
    /// <summary>
    /// PROGRESS, WITH THE MECHANISM OF THE PROTOCOL (entry W2.2)
    ///
    /// ── WHY NOT A SECOND TOOL «HOW FAR ARE YOU» ─────────────────────────
    ///
    /// Because it already exists in the MCP 2025-06-18 specification, and reinventing it
    /// would cost more to be understood by fewer people: a generic client
    /// can read `notifications/progress`, and will never know how to read a tool
    /// invented here.
    ///
    /// The client that wants to be kept up to date puts a `progressToken` inside
    /// `params._meta` of the request. Whoever does not put it does not want to be
    /// updated, and then **nothing is sent**: a notification that nobody
    /// is waiting for is noise on the channel of the protocol.
    ///
    /// ── THE TOKEN IS SENT BACK AS IT ARRIVED ─────────────────────────────
    ///
    /// The specification admits a string or an integer. This code does not
    /// look at it: it keeps it as an `object` and copies it back. Interpreting it would mean
    /// being able to change it — a `42` that comes back as `"42"` no longer
    /// matches anything on the client's side, and the client has no
    /// way to say why.
    ///
    /// ── «progress» MUST GROW, AND HERE IT CANNOT FAIL TO GROW ───────────
    ///
    /// The specification demands it. Leaving it to the caller would mean
    /// discovering it broken in a client, that is, never. The <see cref="Wire"/> keeps
    /// the last value sent and **discards** a step that does not grow: a
    /// single rule, in a single place, that cannot be forgotten in any
    /// of the four points that use it.
    /// </summary>
    internal static class Progress
    {
        /// <summary>
        /// The token inside `params._meta.progressToken`, or null if the
        /// client did not put one.
        ///
        /// An empty string counts as «nothing»: it is almost always a badly
        /// filled field, and sending it back would not help anyone
        /// recognize their own request.
        /// </summary>
        public static object Token(Dictionary<string, object> parameters)
        {
            if (parameters == null) return null;
            object meta;
            if (!parameters.TryGetValue("_meta", out meta)) return null;
            var inside = meta as Dictionary<string, object>;
            if (inside == null) return null;
            object token;
            if (!inside.TryGetValue("progressToken", out token)) return null;
            if (token == null) return null;
            var text = token as string;
            if (text != null && text.Length == 0) return null;
            return token;
        }

        /// <summary>
        /// The notification, detached from whoever sends it, so that it can be looked at
        /// without an open channel.
        ///
        /// `total` is omitted when it is not known how many steps there will be: the
        /// specification allows it, and writing an invented total would give
        /// the reader a progress bar that lies.
        /// </summary>
        public static Dictionary<string, object> Notify(
            object token, double done, double total, string message)
        {
            var parameters = new Dictionary<string, object>
            {
                ["progressToken"] = token,
                ["progress"] = done,
            };
            if (total > 0) parameters["total"] = total;
            if (!string.IsNullOrWhiteSpace(message)) parameters["message"] = message;

            return new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/progress",
                ["params"] = parameters,
            };
        }

        /// <summary>
        /// THE THREAD OF A REQUEST: the token, and how much has already been said.
        ///
        /// It is always built, even without a token: so the points that use
        /// it do not each have an `if`. An `if (wire != null)` scattered over
        /// four places is the way the fifth forgets it —
        /// it is the same reason why `McpTools._registry` is never null.
        /// </summary>
        internal sealed class Wire
        {
            private readonly object _token;
            private readonly Func<DateTime> _clock;
            private readonly DateTime _start;
            private double _last;

            /// <summary>How many notifications really went out.</summary>
            public int Mandate { get; private set; }

            /// <summary>How many steps were discarded because they did not grow.</summary>
            public int Discarded { get; private set; }

            /// <summary>
            /// How many steps in all, if known. Zero means «I do not know»,
            /// and then `total` is not sent at all.
            ///
            /// IT LIVES HERE AND NOT IN THE POINTS THAT SEND, and it is not convenience: a
            /// plan of five steps is announced twice — once when it is
            /// checked, once when it is executed — and the two points are in
            /// two different files. If each worked out the total on its own
            /// account, sooner or later they would say different numbers and the reader's
            /// bar would go backwards.
            /// </summary>
            public double Total { get; set; }

            /// <summary>
            /// The last value sent. It serves whoever resumes the count where
            /// another left it — the execution after the check —
            /// without having to remember how many steps the other had.
            /// </summary>
            public double Last { get { return _last; } }

        /// <summary>
        /// THE CLOCK IS PASSED IN, NOT READ, and for the usual reason: a
        /// check that cannot fix the time cannot look at what is
        /// written. It is the same choice as `Leash` and `SolidWorksSentinel`.
        /// </summary>
            public Wire(object token) : this(token, () => DateTime.Now) { }

            public Wire(object token, Func<DateTime> clock)
            {
                _token = token;
                _clock = clock ?? (() => DateTime.Now);
                _start = _clock();
            }

            /// <summary>For how long it has been working.</summary>
            public TimeSpan Elapsed { get { return _clock() - _start; } }

            /// <summary>Has the client asked to be kept up to date?</summary>
            public bool On { get { return _token != null; } }

            /// <summary>
            /// THE TIME NEXT TO THE STEP (entry S0).
            ///
            /// The criterion of S0 does not ask for «a progress report»: it asks that whoever
            /// watches from outside can tell **«it is working» from «it is
            /// stuck» without looking at the CPU**. The step number alone
            /// does not say it — «step 3 of 5» is identical whether step 3
            /// began two seconds ago or eleven minutes ago.
            ///
            /// It is written as a person reads it (`2m14s`, not `134`): the
            /// `message` is the only field of the notification that a human reads,
            /// and putting raw seconds in it would mean making them do the division.
            /// </summary>
            private string ColTempo(string message)
            {
                var t = Elapsed;
                if (t < TimeSpan.Zero) t = TimeSpan.Zero;
                var howMuch = t.TotalMinutes >= 1
                    ? ((int)t.TotalMinutes) + "m" + t.Seconds.ToString("00") + "s"
                    : ((int)t.TotalSeconds) + "s";
                return string.IsNullOrWhiteSpace(message)
                    ? howMuch
                    : message + " \u2014 " + howMuch;
            }

            public void Step(double done, string message)
            {
                if (_token == null) return;
                if (done <= _last) { Discarded++; return; }
                _last = done;
                Mandate++;
                Protocol.NotifyOnChannel(
                    Notify(_token, done, Total, ColTempo(message)));
            }
        }
    }
}
