using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Shared
{
    /// <summary>
    /// TALKING TO THE BRIDGE AS AN MCP CLIENT WOULD: the transport, and nothing else.
    ///
    /// ── WHY IT LIVES HERE AND NOT IN ONE OF THE TWO PROJECTS ───────────────
    ///
    /// Two use it. `Oce.Solid.Agent.ContractTests` for days, to test the protocol
    /// **without** SOLIDWORKS; the bench from now on (entry F14), to run
    /// cases **through the client's door**, with a real document in front.
    ///
    /// Copying it would have been quicker and wrong. Inside there is something
    /// that cost an afternoon — the UTF-8 BOM that `AutoFlush = true`
    /// writes before the first line, three bytes that made the
    /// bridge answer «Invalid JSON primitive» while the symptom looked like
    /// «initialize does not respond». *A defect paid for once is fixed in one
    /// place only, otherwise it is paid for again in the copy.*
    ///
    /// There is no assertion here: whoever calls decides what to demand.
    /// </summary>
    internal static class McpClient
    {
        /// <summary>
        /// How long a response is awaited. IT IS NOT A CONVENIENCE: without it, a
        /// check that fails hangs the command instead of turning red.
        /// Ninety seconds because the first `tools/call` attaches to SOLIDWORKS,
        /// and if SOLIDWORKS is there and busy that line can take long.
        /// </summary>
        internal const int TimeoutMs = 90000;
        // ==================================================================
        // IL SOTTOPROCESSO
        // ==================================================================
        internal sealed class Bridge : IDisposable
        {
            public Process ServerProcess;
            public readonly System.Collections.Concurrent.BlockingCollection<string> Lines =
                new System.Collections.Concurrent.BlockingCollection<string>();
            public readonly List<string> LinesNotJson = new List<string>();
            /// <summary>Everything that arrived, so that it can be TOLD when
            /// a check goes red: «nothing arrived» without saying what
            /// arrived instead costs the reader an hour.</summary>
            public readonly List<string> Views = new List<string>();
            public int LinesRead;

            public void Dispose()
            {
                try { if (ServerProcess != null && !ServerProcess.HasExited) ServerProcess.Kill(); } catch { }
                try { ServerProcess?.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// The bridge, but with the catalog deliberately broken.
        ///
        /// A SEPARATE METHOD AND NOT ONE MORE PARAMETER ON `Open`. Adding an
        /// argument with a default value has already cost a whole bench run
        /// (note `80zf`: «Parameter count mismatch», from
        /// 88/0 to 70/58), and the repair that worked was **a different
        /// name**. Here `Open` is not called by reflection, but the rule
        /// holds all the same: it costs one line and removes a category of
        /// surprises.
        /// </summary>
        internal static Bridge OpenWithBrokenCatalog(string executable)
        {
            var fake = Path.Combine(
                Path.GetTempPath(), "catalog_that_does_not_exist_" + Guid.NewGuid().ToString("N") + ".json");
            return Open(executable, fake);
        }

        internal static Bridge Open(string executable)
        {
            return Open(executable, null);
        }

        internal static Bridge Open(string executable, string fakeCatalog)
        {
            // ── THE BOM THAT BROKE THE FIRST LINE ───────────────────────
            //
            // `Process.StandardInput` on .NET Framework is born with the encoding
            // of `Console.InputEncoding`, and as soon as it is created
            // `AutoFlush = true` is set on it — which **does a Flush**, and a Flush of a
            // UTF-8 `StreamWriter` writes the preamble: three bytes, `EF BB BF`,
            // before the first line.
            //
            // The bridge answered `-32700: Primitiva JSON non valida`, and it was
            // true — what reached it was not JSON, it was three bytes and then
            // some JSON. The symptom, though, looked like something else, «initialize does not
            // respond», because the error line does not carry the id of the
            // request: it was seen only by printing the lines that arrived.
            //
            // **It is the number one trap of MCP clients over stdio**, the twin
            // of the one this group tests on the bridge. It is fixed before
            // starting the process, with a UTF-8 that does not have the
            // preamble.
            try { Console.InputEncoding = new UTF8Encoding(false); } catch { }

            try
            {
                var p = new Bridge
                {
                    ServerProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo(executable)
                        {
                            RedirectStandardInput = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            StandardOutputEncoding = new UTF8Encoding(false),
                            WorkingDirectory = Path.GetDirectoryName(executable),
                        },
                    },
                };
                // `OCE_CATALOG` is the same variable that whoever
                // installs the package elsewhere would use: pointing it at a file that
                // does not exist is the honest way to test what the bridge does when the
                // catalog does not load.
                if (fakeCatalog != null)
                    p.ServerProcess.StartInfo.EnvironmentVariables["OCE_CATALOG"] = fakeCatalog;
                p.ServerProcess.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    p.LinesRead++;
                    p.Views.Add(e.Data);
                    var t = e.Data.Trim();
                    // WHAT IS NOT JSON IS KEPT ASIDE, not thrown away:
                    // it is the check, not an accident.
                    if (t.Length > 0 && !(t.StartsWith("{") || t.StartsWith("[")))
                        p.LinesNotJson.Add(t);
                    p.Lines.Add(e.Data);
                };
                // stderr is NOT watched: that is where the bridge tells its story, and it
                // is right that it tells it to us.
                p.ServerProcess.ErrorDataReceived += (s, e) => { };
                p.ServerProcess.Start();
                p.ServerProcess.BeginOutputReadLine();
                p.ServerProcess.BeginErrorReadLine();
                return p;
            }
            catch { return null; }
        }

        /// <summary>A line on the stdin of the bridge. See `Open` for the BOM.</summary>
        internal static void Write(Bridge p, string line)
        {
            p.ServerProcess.StandardInput.Write(line + "\n");
            p.ServerProcess.StandardInput.Flush();
        }

        /// <summary>
        /// A line, or null after the timeout. **The timeout is mandatory**:
        /// without it, a check that fails hangs the command instead of turning
        /// red.
        /// </summary>
        internal static string Line(Bridge p, int ms)
        {
            string line;
            return p.Lines.TryTake(out line, ms) ? line : null;
        }

        internal static string Ask(Bridge p, int id, string request)
        {
            Write(p, request);
            // What is not the expected response is discarded: the bridge may
            // write other things, and looking for the id is the only way that does not depend
            // on the order.
            var expiry = Environment.TickCount + TimeoutMs;
            while (Environment.TickCount < expiry)
            {
                var line = Line(p, Math.Max(200, expiry - Environment.TickCount));
                if (line == null) return null;
                var o = Obj(line);
                if (o != null && o.ContainsKey("id") && Convert.ToString(o["id"]) == id.ToString())
                    return line;
            }
            return null;
        }

        internal static void Close(Bridge p)
        {
            try { p.ServerProcess.StandardInput.Close(); } catch { }
            try { p.ServerProcess.WaitForExit(3000); } catch { }
        }

        internal static Dictionary<string, object> Obj(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                s.MaxJsonLength = 40 * 1024 * 1024;
                return s.DeserializeObject(line) as Dictionary<string, object>;
            }
            catch { return null; }
        }

        internal static object Inside(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            if (d == null || !d.ContainsKey(key)) return null;
            return d[key];
        }

        /// <summary>
        /// The text inside a `result` of `tools/call`: `content[0].text`.
        ///
        /// It returns the empty string and not `null` when there is none: an expectation that
        /// looks for a word inside a `null` would blow up, and the blow-up would
        /// be read as a defect of the bridge instead of as «the content
        /// was empty».
        /// </summary>
        internal static string TextOf(Dictionary<string, object> result)
        {
            var content = Inside(result, "content") as object[];
            if (content == null || content.Length == 0) return "";
            return Convert.ToString(Inside(content[0], "text")) ?? "";
        }

        internal static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(nothing)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 90 ? s.Substring(0, 90) + "..." : s;
        }
    }
}
