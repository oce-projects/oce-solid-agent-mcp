using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Oce.Solid.Agent.Mcp
{
    /// <summary>
    /// MCP over stdio: JSON-RPC 2.0, one message per line.
    ///
    /// WHY HAND-WRITTEN AND NOT WITH A LIBRARY.
    /// This program runs on .NET Framework 4.8, because it has to load
    /// the real SolidWorks add-in, which is compiled for that. The MCP libraries
    /// for .NET want modern runtimes. The part of the protocol needed here
    /// is small — three methods and an error format — and rewriting it costs
    /// less than carrying one more runtime onto a designer's
    /// workstation.
    ///
    /// ONE LINE PER MESSAGE, AND STDOUT BELONGS TO THE PROTOCOL ALONE.
    /// Anything written on stdout that is not a JSON message breaks the
    /// conversation without saying why: service messages go on
    /// stderr, always. It is the number one trap of every MCP server over stdio.
    /// </summary>
    internal static class Protocol
    {
        private static readonly JavaScriptSerializer Json = Creator();

        private static JavaScriptSerializer Creator()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 200;
            return s;
        }

        public static string InJson(object o) => Json.Serialize(o);

        public static Dictionary<string, object> FromJson(string text)
        {
            return Json.DeserializeObject(text) as Dictionary<string, object>;
        }

        /// <summary>
        /// Writes a message on stdout. One line, nothing else.
        ///
        /// UNDER A LOCK (brief R13): the heartbeat of a long step writes from a
        /// timer thread while the executor writes its lines from its own, and two
        /// messages written at once would interleave into a line that is not JSON,
        /// which breaks the client. The serializer is shared, so it is inside too.
        /// </summary>
        public static void Send(TextWriter output, object message)
        {
            lock (WriteKey)
            {
                output.Write(InJson(message));
                output.Write("\n");
                output.Flush();
            }
        }

        private static readonly object WriteKey = new object();

        /// <summary>
        /// WHERE WHAT THE BRIDGE TELLS ENDS UP (entry W2.8).
        ///
        /// `stderr` stays and is not touched: it is the only place that works **before**
        /// a client exists — the attachment to SOLIDWORKS, the catalog that does not
        /// load, `--self-test` — and it is what one looks at when something does not
        /// start. Removing it to «do things properly in the protocol» would mean
        /// losing the only lines that explain a failed start.
        ///
        /// What is ADDED is `notifications/message`, which MCP provides for
        /// exactly this: so the same lines also reach whoever is on the other
        /// side of the pipe, who today does not see them **at all**. When the operation
        /// says «I found face F3 again, moved by 2 mm», that line
        /// explains why the part came out that way, and it used to end up in a terminal that
        /// nobody looks at.
        ///
        /// The channel is switched on only when the message loop has started:
        /// before then writing to stdout would be writing to nobody, and
        /// **a non-JSON line on stdout breaks the client** (it is the number
        /// one trap of MCP servers over stdio, already guarded by a check).
        /// </summary>
        private static TextWriter _channel;
        private static string _level = "info";

        /// <summary>
        /// The levels in order, as the spec lists them (syslog). A message
        /// goes out if its level is **at least** the one the client asked for.
        /// </summary>
        private static readonly string[] Levels =
        {
            "debug", "info", "notice", "warning", "error", "critical", "alert", "emergency",
        };

        public static void TurnOnChannel(TextWriter output) { _channel = output; }

        /// <summary>
        /// A notification on the registered channel, and **it never throws**.
        ///
        /// Same reason as `SendAsNotification`, written there and valid
        /// here: if writing the notification failed and the error went up, the
        /// RESPONSE to which the notification was incidental would be lost. *A
        /// story that makes the thing told fail is worse than
        /// silence.*
        ///
        /// Without a channel it does nothing: it is the case of the bridge launched with
        /// `--self-test`, and of every check that builds the message without
        /// having anyone to send it to.
        /// </summary>
        internal static void NotifyOnChannel(object message)
        {
            var channel = _channel;
            if (channel == null) return;
            try { Send(channel, message); }
            catch { }
        }

        /// <summary>
        /// `logging/setLevel`. A level that is not recognized **is not
        /// applied**: changing it at random would mean making messages disappear without
        /// anyone having asked. It returns `true` if it was accepted.
        /// </summary>
        public static bool SetLevel(string level)
        {
            if (string.IsNullOrWhiteSpace(level)) return false;
            var l = level.Trim().ToLowerInvariant();
            if (Array.IndexOf(Levels, l) < 0) return false;
            _level = l;
            return true;
        }

        public static void Note(string text)
        {
            Console.Error.WriteLine(text);
            Console.Error.Flush();
            // ALSO IN THE BRIDGE'S OWN LOG (brief R24): the client keeps stderr
            // only while it connects.
            Bridge.CallLog.Note(text);
            SendAsNotification(text, "info");
        }

        private static void SendAsNotification(string text, string level)
        {
            var channel = _channel;
            if (channel == null) return;
            if (Array.IndexOf(Levels, level) < Array.IndexOf(Levels, _level)) return;

            // EVERYTHING INSIDE A try THAT DOES NOT RETHROW, and it is not laziness: if
            // writing the notification failed and the error went up, the
            // RESPONSE to which the notification was incidental would be lost. A
            // story that makes the thing told fail is worse than silence.
            // And `Note` is not called in here: it would be infinite recursion.
            try
            {
                Send(channel, new Dictionary<string, object>
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "notifications/message",
                    ["params"] = new Dictionary<string, object>
                    {
                        ["level"] = level,
                        ["logger"] = "oce-solid-agent",
                        ["data"] = text,
                    },
                });
            }
            catch { }
        }

        public static Dictionary<string, object> Response(object id, object result)
        {
            return new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["result"] = result,
            };
        }

        /// <summary>
        /// A PROTOCOL error: the request was malformed or the method does not
        /// exist. It is NOT used for a drawing operation that fails — that
        /// is a result, and goes in the content with `isError`, otherwise whoever
        /// calls cannot tell «I did not understand you» from «the part did not
        /// come out».
        /// </summary>
        public static Dictionary<string, object> Error(object id, int code, string message)
        {
            return new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new Dictionary<string, object>
                {
                    ["code"] = code,
                    ["message"] = message,
                },
            };
        }

        /// <summary>The result of a tool: text, with or without error.</summary>
        public static Dictionary<string, object> Content(string text, bool isError = false)
        {
            return new Dictionary<string, object>
            {
                ["content"] = new object[]
                {
                    new Dictionary<string, object> { ["type"] = "text", ["text"] = text },
                },
                ["isError"] = isError,
            };
        }

        /// <summary>
        /// The same content, PLUS the data in a form readable by a program
        /// (entry W2.4).
        ///
        /// ── WHAT CHANGES FOR WHOEVER READS ───────────────────────────────
        ///
        /// Up to 0.57.0 everything came out as **text**: the state of the document,
        /// the measures of a plan, the catalog. A language model reads it
        /// very well; a program does not — it has to re-parse JSON that was already
        /// JSON before being put inside a string. `structuredContent` is
        /// the field MCP provides for exactly this, and it costs one line.
        ///
        /// ── WHY THE TEXT STAYS ──────────────────────────────────────
        ///
        /// It is not replaced: it is put alongside. The clients that read only `content`
        /// are the majority, and taking the text away from them would mean breaking
        /// all of them to suit a few. The spec says it too: the two fields
        /// coexist, and the text is what is shown to a person.
        ///
        /// ── AND WHY AN `outputSchema` IS NOT DECLARED ─────────────────
        ///
        /// The spec is clear: a tool that declares `outputSchema` **must**
        /// return a conforming `structuredContent`. The state of a document
        /// changes shape with what is inside it — an assembly has
        /// `components`, a drawing has `sheets`, a part has `faces` — and every
        /// new field added here would become a promise to keep.
        ///
        /// A **partial schema is worse than no schema**: a client that
        /// validates would reject right answers. So here the data is given without
        /// promising the shape, and the schema is declared the day the
        /// shape is really stable — which is a decision, not a line of
        /// code. Written here so that the next one who passes does not add it
        /// «because it was missing».
        ///
        /// A DIFFERENT NAME instead of one more parameter on `Content`: the
        /// same lesson already paid for twice, the last time with
        /// `ContentWithImages`.
        /// </summary>
        public static Dictionary<string, object> ContentWithData(
            string text, object data, bool isError = false)
        {
            var outside = Content(text, isError);
            // The field is NOT written when there is nothing to put in it: a
            // `structuredContent: null` is a promise not kept, and a client
            // that finds it can only conclude that something went wrong.
            if (data != null) outside["structuredContent"] = data;
            return outside;
        }

        /// <summary>
        /// Text PLUS images, which is the only form in which a model can
        /// SEE the part instead of reading the list of its faces.
        ///
        /// MCP provides a content of type `image`: the bytes in base64 and the MIME
        /// type next to them. Up to 0.31.0 this protocol could only do
        /// `text`, and the bridge had no way of getting
        /// an image out — while the ability to take it existed, proven, in the
        /// bench.
        ///
        /// THE TEXT GOES FIRST, and it is not a matter of taste: it describes what
        /// is being looked at. An image without a caption in the middle of a
        /// conversation is a riddle.
        ///
        /// A DIFFERENT NAME instead of one more parameter on `Content`:
        /// changing the set of signatures of a method breaks whoever calls it by
        /// reflection even when the compiler is silent. It is the lesson of
        /// 0.25.1, applied before falling into it again.
        /// </summary>
        public static Dictionary<string, object> ContentWithImages(
            string text, IEnumerable<KeyValuePair<string, byte[]>> images)
        {
            var parts = new List<object>
            {
                new Dictionary<string, object> { ["type"] = "text", ["text"] = text },
            };

            if (images != null)
                foreach (var i in images)
                {
                    if (i.Value == null || i.Value.Length == 0) continue;
                    parts.Add(new Dictionary<string, object>
                    {
                        ["type"] = "image",
                        ["data"] = Convert.ToBase64String(i.Value),
                        ["mimeType"] = string.IsNullOrEmpty(i.Key) ? "image/png" : i.Key,
                    });
                }

            return new Dictionary<string, object>
            {
                ["content"] = parts.ToArray(),
                ["isError"] = false,
            };
        }

        public const int ParsingError = -32700;
        public const int InvalidRequest = -32600;
        public const int UnknownMethod = -32601;
        public const int InvalidParameters = -32602;
        public const int InternalError = -32603;
    }
}
