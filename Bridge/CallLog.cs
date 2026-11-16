using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Oce.Solid.Agent.Bridge
{
    /// <summary>
    /// THE BRIDGE'S OWN LOG: one JSON line per tool call, in a file of the day
    /// (brief R24, 25/10/2026).
    ///
    /// ── WHY IT EXISTS ──────────────────────────────────────────────────
    ///
    /// Up to bridge 0.130.2 the bridge wrote only on stderr. The MCP client
    /// used on 24-25/10/2026 kept stderr ONLY while connecting: two lines in two
    /// days, while the bridge had written «[agent] step N» at every step of
    /// sixteen plans. To find out where 102 minutes of waiting had gone, the
    /// calls had to be dug out of the client's conversation file, and the time
    /// of each phase was not there at all. A crash of the bridge (an unhandled
    /// exception in `api_search`, 25/10/2026 00:36) was found only in the
    /// Windows event log, and the client called it «Connection closed».
    ///
    /// ── WHAT A LINE SAYS ───────────────────────────────────────────────
    ///
    /// `kind: "call"`: start and end, milliseconds, tool, request id, a
    /// summary of the arguments (for `build` the list of operations and
    /// `dry_run`, never the values: the values of a plan may carry paths and
    /// names), whether the answer is an error, how many characters it has, the
    /// first characters of an error, and the phases (<see
    /// cref="Oce.Solid.Agent.Core.PhaseClock"/>): name → total milliseconds and
    /// count. `kind: "note"`: every line the bridge writes on stderr.
    /// `kind: "start"`: version, process, executable.
    ///
    /// ── WHERE, AND HOW TO TURN IT OFF ─────────────────────────────────
    ///
    /// `%LOCALAPPDATA%\Oce.Solid.Agent\logs\bridge-yyyy-MM-dd.jsonl`.
    /// `OCE_LOG` set to a folder writes there; set to `off`, `0`, `false` or
    /// to an empty string it writes nothing. The log stays on the machine:
    /// it carries local paths.
    ///
    /// ── IT NEVER BREAKS THE BRIDGE ────────────────────────────────────
    ///
    /// Every write is inside a try that swallows: a full disk or a folder
    /// without permission must cost the log, not the answer to the client.
    /// </summary>
    internal static class CallLog
    {
        private static readonly object Key = new object();
        private static string _folder;
        private static bool _resolved;

        /// <summary>
        /// The folder of the log, or null when it is turned off. Read once.
        /// </summary>
        internal static string Folder
        {
            get
            {
                lock (Key)
                {
                    if (_resolved) return _folder;
                    _resolved = true;
                    _folder = Resolve(Environment.GetEnvironmentVariable("OCE_LOG"));
                    return _folder;
                }
            }
        }

        /// <summary>
        /// What `OCE_LOG` means, apart from reading it, so the rule can be
        /// checked without starting a bridge.
        /// </summary>
        internal static string Resolve(string value)
        {
            if (value != null)
            {
                var v = value.Trim();
                if (v.Length == 0 ||
                    string.Equals(v, "off", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) ||
                    v == "0")
                    return null;
                return v;
            }
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local)) return null;
            return Path.Combine(local, "Oce.Solid.Agent", "logs");
        }

        /// <summary>The line of a starting bridge.</summary>
        internal static void Start(string version)
        {
            string exe = null;
            int pid = 0;
            try
            {
                var me = Process.GetCurrentProcess();
                pid = me.Id;
                exe = me.MainModule?.FileName;
            }
            catch { }
            Write(new Dictionary<string, object>
            {
                ["kind"] = "start",
                ["at"] = Now(),
                ["version"] = version,
                ["pid"] = pid,
                ["executable"] = exe,
            });
        }

        /// <summary>A line the bridge also wrote on stderr.</summary>
        internal static void Note(string text)
        {
            Write(new Dictionary<string, object>
            {
                ["kind"] = "note",
                ["at"] = Now(),
                ["text"] = text,
            });
        }

        /// <summary>One call in progress: opened by <see cref="Begin"/>.</summary>
        internal sealed class Call
        {
            internal string Tool;
            internal object Id;
            internal string StartedAt;
            internal Stopwatch Clock;
            internal Dictionary<string, object> Arguments;
        }

        /// <summary>
        /// Opens the log of a call and the collection of its phases.
        /// </summary>
        internal static Call Begin(string tool, Dictionary<string, object> arguments, object id)
        {
            Oce.Solid.Agent.Core.PhaseClock.Begin();
            return new Call
            {
                Tool = tool,
                Id = id,
                StartedAt = Now(),
                Clock = Stopwatch.StartNew(),
                Arguments = Summary(tool, arguments),
            };
        }

        /// <summary>
        /// Closes the call and writes its line. <paramref name="answer"/> is
        /// the result the tool gave back (content + isError).
        /// </summary>
        internal static void End(Call call, Dictionary<string, object> answer, Exception thrown)
        {
            if (call == null) return;
            var phases = Oce.Solid.Agent.Core.PhaseClock.Finish();
            try
            {
                bool isError = thrown != null;
                string text = null;
                if (answer != null)
                {
                    object e;
                    if (answer.TryGetValue("isError", out e) && e is bool && (bool)e) isError = true;
                    text = TextOf(answer);
                }
                var line = new Dictionary<string, object>
                {
                    ["kind"] = "call",
                    ["started"] = call.StartedAt,
                    ["ended"] = Now(),
                    ["ms"] = Math.Round(call.Clock.Elapsed.TotalMilliseconds, 1),
                    ["tool"] = call.Tool,
                    ["id"] = call.Id,
                    ["arguments"] = call.Arguments,
                    ["is_error"] = isError,
                    ["answer_chars"] = text?.Length ?? 0,
                    ["phases"] = phases,
                };
                if (thrown != null) line["exception"] = thrown.GetType().FullName + ": " + thrown.Message;
                else if (isError && text != null)
                    line["error"] = text.Length > 400 ? text.Substring(0, 400) : text;
                Write(line);
            }
            catch { }
        }

        /// <summary>
        /// WHAT OF THE ARGUMENTS GOES IN THE LOG. The names of the
        /// arguments, and for `build` the operations of the plan and
        /// `dry_run`: enough to tell one call from another, never the values,
        /// which may carry paths, names and text written by a person.
        /// </summary>
        internal static Dictionary<string, object> Summary(string tool, Dictionary<string, object> arguments)
        {
            var summary = new Dictionary<string, object>();
            if (arguments == null) return summary;
            var keys = new List<string>(arguments.Keys);
            keys.Sort(StringComparer.Ordinal);
            summary["keys"] = keys;
            object plan;
            if (arguments.TryGetValue("plan", out plan) || arguments.TryGetValue("steps", out plan))
            {
                var operations = new List<string>();
                var list = plan as IEnumerable;
                if (list != null && !(plan is string))
                {
                    foreach (var item in list)
                    {
                        var step = item as Dictionary<string, object>;
                        object op;
                        if (step != null && step.TryGetValue("operation", out op))
                            operations.Add(Convert.ToString(op, CultureInfo.InvariantCulture));
                    }
                }
                summary["operations"] = operations;
            }
            object dry;
            if (arguments.TryGetValue("dry_run", out dry)) summary["dry_run"] = dry;
            return summary;
        }

        private static string TextOf(Dictionary<string, object> answer)
        {
            object content;
            if (!answer.TryGetValue("content", out content)) return null;
            var items = content as IEnumerable;
            if (items == null) return null;
            var parts = new List<string>();
            foreach (var item in items)
            {
                var block = item as Dictionary<string, object>;
                object t;
                if (block != null && block.TryGetValue("text", out t)) parts.Add(Convert.ToString(t));
            }
            return string.Concat(parts);
        }

        private static string Now()
        {
            return DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        }

        private static void Write(Dictionary<string, object> line)
        {
            try
            {
                var folder = Folder;
                if (folder == null) return;
                var json = Mcp.Protocol.InJson(line);
                var file = Path.Combine(folder,
                    "bridge-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");
                lock (Key)
                {
                    Directory.CreateDirectory(folder);
                    using (var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    // UTF-8 without a byte order mark: StreamWriter's default.
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.Write(json);
                        writer.Write('\n');
                    }
                }
            }
            catch { }
        }
    }
}
