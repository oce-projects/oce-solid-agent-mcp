using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Oce.Solid.Agent.Mcp;
using Oce.Solid.Agent.Bridge;
using Oce.Solid.Agent.Tools;

namespace Oce.Solid.Agent
{
    /// <summary>
    /// The main loop: reads a line, answers a line.
    ///
    /// STDOUT BELONGS TO THE PROTOCOL ALONE. Anything else written there breaks the
    /// conversation without saying why. Everything else — the attachment, the
    /// errors, what the add-in tells — goes to stderr.
    ///
    /// IT STARTS EVEN IF SOLIDWORKS IS NOT THERE. An MCP server that dies
    /// at startup has no time to explain itself: whoever launches it sees only a
    /// finished process. Here it starts anyway and the reason can be read by calling
    /// a tool — which is the place where whoever uses the bridge is looking.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// THE VERSION IS READ FROM `VERSION.txt`, WHICH TRAVELS INSIDE
        /// THE EXECUTABLE AS A RESOURCE.
        ///
        /// There used to be `const string Version = "0.2.0"` here, and it stayed
        /// **for seventy-eight minor versions**: the bridge said 0.2.0
        /// in `initialize` while `VERSION.txt` was at 0.80.0. Whoever
        /// recorded the MCP conversation carried around a false number,
        /// and nobody could notice because there was nothing to
        /// compare it with.
        ///
        /// It is the same rot that the TODO had already cured at
        /// its top on 16/9/2026 — that line said «Bridge 0.2.0» and was
        /// true on the 14th — but in the code, where it is seen even less.
        ///
        /// If the resource cannot be read the answer is **«unknown»**, not
        /// a fallback number: *a wrong number is worse than a
        /// missing number*, because whoever reads it has no way of knowing
        /// that it is wrong.
        /// </summary>
        private static readonly string Version = VersionFromResource();

        private static string VersionFromResource()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (var itemName in asm.GetManifestResourceNames())
                {
                    if (!itemName.EndsWith("VERSION.txt",
                                       StringComparison.OrdinalIgnoreCase)) continue;
                    using (var flow = asm.GetManifestResourceStream(itemName))
                    using (var reader = new StreamReader(flow))
                        return reader.ReadToEnd().Trim();
                }
            }
            catch { }
            return "unknown";
        }

        /// <summary>
        /// The warning for an executable on a network drive, or null (brief R14).
        ///
        /// Windows maps the executable and its libraries into memory from where
        /// they are, and reads their pages again when it needs them. From a
        /// network drive a page that cannot be read in that moment is an
        /// EXCEPTION_IN_PAGE_ERROR, which .NET raises as a SEHException that no
        /// `catch` sees: the process ends. It is the most likely cause of the
        /// crash of 25/10/2026 (bridge started from a network share, first
        /// `api_search` after 22 idle minutes); it was not reproduced.
        /// </summary>
        internal static string NetworkLocation(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return null;
                bool network = path.StartsWith(@"\", StringComparison.Ordinal);
                if (!network)
                {
                    var root = Path.GetPathRoot(path);
                    network = !string.IsNullOrEmpty(root) &&
                              new DriveInfo(root).DriveType == DriveType.Network;
                }
                return network
                    ? "the bridge runs from a network drive (" + Path.GetDirectoryName(path) + "): if the " +
                      "network drops while Windows reads a page of the program, the bridge ends without " +
                      "an answer. Install it on a local disk (Installer.exe) and point the client there."
                    : null;
            }
            catch { return null; }
        }

        private static int Main(string[] arguments)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.InputEncoding = new UTF8Encoding(false);

            // THE BRIDGE'S OWN LOG, before anything can go wrong (brief R24).
            CallLog.Start(Version);

            // A CRASH LEAVES ITS CAUSE IN THE LOG (brief R14). On 25/10/2026 the
            // bridge died of a SEHException inside `api_search`, the client
            // said «Connection closed», and the cause was found only in the
            // Windows event log. An exception of that kind (a corrupted-state
            // exception) is not caught by the loop's `catch`: this handler is
            // the last place where it can at least be written down.
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                try { Protocol.Note("[bridge] crashed: " + e.ExceptionObject); } catch { }
            };

            // FROM A NETWORK DRIVE THE BRIDGE CAN DIE OF THE NETWORK (brief R14).
            var fromNetwork = NetworkLocation(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (fromNetwork != null) Protocol.Note("[bridge] " + fromNetwork);
            Oce.Solid.Agent.Core.PlanExecutor.GuardianReport = text =>
            {
                Console.Error.WriteLine(text);
                CallLog.Note(text);
            };

            var session = new Session();
            var reason = session.Attach();
            if (reason != null) Protocol.Note("[bridge] " + reason);
            else
                Protocol.Note(
                    "[bridge] SolidWorks " + session.SolidWorksVersion() +
                    ", drawing library " + session.LibraryVersion());

            var catalog = new Catalog();
            if (catalog.LoadingError != null)
                Protocol.Note("[bridge] " + catalog.LoadingError);
            else
                Protocol.Note("[bridge] catalog: " + catalog.HowMany + " operations tested");

            if (arguments != null && arguments.Length > 0 && arguments[0] == "--self-test")
                return Test(session, catalog);

            var tools = new McpTools(session, catalog);
            var output = Console.Out;

            // FROM HERE ON THE BRIDGE ALSO TELLS THE CLIENT (entry W2.8).
            // Not before: the message loop has not started yet, and a line
            // on stdout with nobody reading JSON lines is only noise that
            // breaks whoever comes next.
            Protocol.TurnOnChannel(output);

            // THE BACKLOG BEFORE STDIN (entry W2.5).
            //
            // While the bridge waits for the answer to a confirmation, the client
            // may send something else. Those lines are put aside
            // instead of thrown away — throwing them away would leave the client
            // waiting forever for an answer that nobody will give it — and
            // the loop serves them here, as soon as it can.
            string line;
            while ((line = Elicitation.Backlog() ?? Console.In.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                Dictionary<string, object> message;
                try { message = Protocol.FromJson(line); }
                catch (Exception ex)
                {
                    Protocol.Send(output, Protocol.Error(
                        null, Protocol.ParsingError, "Invalid JSON: " + ex.Message));
                    continue;
                }
                if (message == null) continue;

                object id = message.TryGetValue("id", out var i) ? i : null;
                var method = message.TryGetValue("method", out var m)
                    ? Convert.ToString(m) : null;
                var parameters = message.TryGetValue("params", out var p)
                    ? p as Dictionary<string, object> : null;

                // NOTIFICATIONS have no id and want no answer. Answering
                // a notification is the quickest way to confuse whoever
                // is listening: they would find an answer they were not expecting.
                bool isNotification = !message.ContainsKey("id");

                try
                {
                    var response = Serve(method, parameters, tools, session, catalog, id);
                    if (response != null && !isNotification) Protocol.Send(output, response);
                }
                catch (Exception ex)
                {
                    Protocol.Note("[bridge] " + ex);
                    if (!isNotification)
                        Protocol.Send(output, Protocol.Error(
                            id, Protocol.InternalError, ex.Message));
                }
            }

            Protocol.Note("[bridge] closed");
            return 0;
        }

        private static Dictionary<string, object> Serve(
            string method,
            Dictionary<string, object> parameters,
            McpTools tools,
            Session session,
            Catalog catalog,
            object id)
        {
            switch (method)
            {
                case "initialize":
                    // WHAT THE CLIENT CAN DO is read HERE and only once
                    // (entry W2.5): `capabilities.elicitation` says whether it can
                    // ask the user for a confirmation. Presuming it would mean
                    // hanging a call waiting for an answer
                    // that will never arrive.
                    Elicitation.ReadCapabilities(parameters);
                    return Protocol.Response(id, new Dictionary<string, object>
                    {
                        // The protocol version is sent back as it arrived
                        // when it is recognized: declaring only one would mean
                        // breaking at the first update of the client.
                        ["protocolVersion"] = ProtocolVersion(parameters),
                        ["capabilities"] = new Dictionary<string, object>
                        {
                            ["tools"] = new Dictionary<string, object>(),
                            // The catalog can also be ATTACHED instead of
                            // asked for (W2.12). Without this line a client does not
                            // even try `resources/list`, and the door is there
                            // but nobody sees it. `subscribe` and `listChanged`
                            // stay out on purpose: **the catalog does not change
                            // while the bridge runs** — it is a file read at
                            // startup — and declaring a capability that is not
                            // exercised is a promise that someone will wait for.
                            ["resources"] = new Dictionary<string, object>(),
                            // The bridge tells what it does, and declares it: without
                            // this line a client does not know that it can ask for
                            // `logging/setLevel`, and the notifications reach it
                            // without it ever having been able to adjust them (W2.8).
                            ["logging"] = new Dictionary<string, object>(),
                        },
                        ["serverInfo"] = new Dictionary<string, object>
                        {
                            ["name"] = "oce-solid-agent",
                            ["version"] = Version,
                        },
                        ["instructions"] =
                            "A bridge to SOLIDWORKS. Before proposing " +
                            "anything, call «inspect_part»: the " +
                            "identifiers of faces and edges are born " +
                            "there and hold for that reading only. After every " +
                            "change the part is different, so read it again.\n\n" +
                            "If the shape is in doubt, «inspect_part» with " +
                            "«views» lets you SEE the part: the list of faces " +
                            "says how many there are and how big they are, not " +
                            "what shape it has.\n\n" +
                            "If what you drew is wrong, «undo» withdraws " +
                            "the last plan whole: there is no need to redo " +
                            "the part from scratch.",
                    });

                case "notifications/initialized":
                    return null;

                case "ping":
                    return Protocol.Response(id, new Dictionary<string, object>());

                // The client decides how much it wants to hear (entry W2.8).
                //
                // A LEVEL THAT IS NOT RECOGNIZED IS AN ERROR, not a silence:
                // accepting it and ignoring it would leave the client convinced it had
                // raised the threshold while the messages keep arriving —
                // or, worse, convinced it hears everything while it hears
                // nothing.
                case "logging/setLevel":
                {
                    var wanted = parameters != null && parameters.TryGetValue("level", out var lv)
                        ? Convert.ToString(lv) : null;
                    if (!Protocol.SetLevel(wanted))
                        return Protocol.Error(
                            id, Protocol.InvalidParameters,
                            "Unknown level «" + (wanted ?? "") + "». The levels are: " +
                            "debug, info, notice, warning, error, critical, alert, emergency.");
                    return Protocol.Response(id, new Dictionary<string, object>());
                }

                case "tools/list":
                    return Protocol.Response(id, new Dictionary<string, object>
                    {
                        ["tools"] = tools.Items(),
                    });

                // ── THE RESOURCES (entry W2.12) ─────────────────────────────
                //
                // Same catalog as the tools, taken from another door:
                // `McpResources` receives the `Catalog` object that is already in hand and
                // does not build another one. Building another would mean
                // rereading the file, and two readings of the same file at different
                // moments are two catalogs that may differ.
                case "resources/list":
                    return Protocol.Response(id, new Dictionary<string, object>
                    {
                        ["resources"] = new McpResources(catalog).Items(),
                    });

                case "resources/templates/list":
                    return Protocol.Response(id, new Dictionary<string, object>
                    {
                        ["resourceTemplates"] = new McpResources(catalog).Models(),
                    });

                case "resources/read":
                {
                    var uri = parameters != null && parameters.TryGetValue("uri", out var u)
                        ? Convert.ToString(u) : null;
                    if (string.IsNullOrWhiteSpace(uri))
                        return Protocol.Error(
                            id, Protocol.InvalidParameters, "«uri» is missing");

                    var mcpResources = new McpResources(catalog);
                    var content = mcpResources.Read(uri);
                    if (content != null)
                        return Protocol.Response(id, content);

                    // A URI THAT DOES NOT EXIST SAYS WHAT EXISTS. A bare «not
                    // found» leaves the asker the only remedy that does not
                    // work: trying again the same.
                    var similar = mcpResources.Similar(uri);
                    return Protocol.Error(
                        id, Protocol.InvalidParameters,
                        "I do not know the resource «" + uri + "». There is «" +
                        McpResources.UriList + "» (all the operations in brief) and " +
                        "«" + McpResources.OperationPrefix + "<name>» for a single one." +
                        (similar.Count == 0
                            ? ""
                            : " Did you mean: " + string.Join(", ", similar) + "."));
                }

                case "tools/call":
                {
                    var itemName = parameters != null && parameters.TryGetValue("name", out var n)
                        ? Convert.ToString(n) : null;
                    var itsItems = parameters != null && parameters.TryGetValue("arguments", out var a)
                        ? a as Dictionary<string, object> : null;
                    if (string.IsNullOrWhiteSpace(itemName))
                        return Protocol.Error(
                            id, Protocol.InvalidParameters, "«name» is missing");
                    // THE PROGRESS TOKEN (entry W2.2). It sits on
                    // `params._meta`, that is next to `name` and not inside
                    // `arguments`: it belongs to the PROTOCOL, not to the tool,
                    // and a generic client puts it there without knowing anything
                    // about what this bridge does.
                    // ONE LINE OF THE BRIDGE'S LOG PER CALL (brief R24), with
                    // the time of each phase; written also when the tool throws.
                    var call = CallLog.Begin(itemName, itsItems, id);
                    Dictionary<string, object> answer = null;
                    // THE SEQUENCE IS ANNOUNCED TO SOLIDWORKS for the length of
                    // the call (brief R2), and put back whatever happens.
                    bool sequence = Core.PhaseClock.Time("session.api_sequence", () => session.BeginApiSequence());
                    try
                    {
                        answer = tools.Invoke(itemName, itsItems, Progress.Token(parameters));
                    }
                    catch (Exception ex)
                    {
                        session.EndApiSequence(sequence);
                        CallLog.End(call, null, ex);
                        throw;
                    }
                    session.EndApiSequence(sequence);
                    CallLog.End(call, answer, null);
                    return Protocol.Response(id, answer);
                }

                // ── CANCELLATION, AND A NO THAT IS SAID (entry W2.2) ─────────────
                //
                // Before, it fell through here below, into the `default`. And being a
                // NOTIFICATION, the main loop discarded the error response:
                // **a silent no-op**. The client stayed convinced it had
                // cancelled something.
                //
                // This bridge CANNOT interrupt a call in progress, and
                // it is not a shortcoming to repair with more code: it serves one
                // request at a time on the same thread because SOLIDWORKS
                // wants STA. While a call runs, stdin is not read —
                // so when this line arrives, the request it names is
                // already finished.
                //
                // The specification says that a `cancelled` may be ignored. Here
                // it is not ignored: it is SAID. *A declared no can be read;
                // a silence cannot.*
                case "notifications/cancelled":
                    Protocol.Note(UndoNotPossible(parameters));
                    return null;

                default:
                    return Protocol.Error(
                        id, Protocol.UnknownMethod,
                        "Unhandled method: " + method);
            }
        }

        /// <summary>
        /// What is answered to `notifications/cancelled` (entry W2.2).
        ///
        /// It stands alone so that it can be read in a check without starting a
        /// bridge: the thing that matters about this answer is **that it says the
        /// number of the request and why**, and that is looked at here.
        /// </summary>
        internal static string UndoNotPossible(
            Dictionary<string, object> parameters)
        {
            object which = null;
            string reason = null;
            if (parameters != null)
            {
                parameters.TryGetValue("requestId", out which);
                object m;
                if (parameters.TryGetValue("reason", out m)) reason = Convert.ToString(m);
            }

            return "[bridge] cancellation requested for request " +
                   (which == null ? "(no requestId)" : "«" + Convert.ToString(which) + "»") +
                   (string.IsNullOrWhiteSpace(reason) ? "" : ", reason: " + reason) +
                   ". I did NOT interrupt it: this bridge serves one request " +
                   "at a time on the same thread, because SOLIDWORKS " +
                   "demands it, and while it works it does not read stdin. When this " +
                   "line arrived, that request had already finished. If the " +
                   "answer is of no use to you any more, throw it away: the document is in the " +
                   "state in which that plan left it, and «inspect_part» " +
                   "tells you.";
        }

        private static string ProtocolVersion(Dictionary<string, object> parameters)
        {
            try
            {
                if (parameters != null &&
                    parameters.TryGetValue("protocolVersion", out var v) &&
                    v != null)
                {
                    var requested = Convert.ToString(v);
                    if (!string.IsNullOrWhiteSpace(requested)) return requested;
                }
            }
            catch { }
            return "2024-11-05";
        }

        // ── THE EXIT CODES OF --self-test (entry H19, B3) ────────────────────
        //
        // The installer reads these, and no longer the sentences: a sentence
        // is for a person and gets translated, a code does not. The same
        // numbers are written in Installer/Steps.cs (SelfTest*), which cannot
        // reference this assembly; a contract in Tests reads both.
        //
        //   0  attached (with or without a document)
        //   1  anything else: the library did not load, an unknown HRESULT,
        //      the open document could not be read
        //   3  SOLIDWORKS installed and not running (MK_E_UNAVAILABLE)
        //   4  SOLIDWORKS not installed or not registered (CO_E_CLASSSTRING,
        //      REGDB_E_CLASSNOTREG)
        //   5  not tried: OCE_NO_ATTACH is set
        //   6  MK_E_UNAVAILABLE, but a SLDWORKS process runs in this Windows
        //      session: it runs as administrator while this process does not
        //      (or the other way round: the running object table is not
        //      shared across integrity levels), or it is still starting
        //
        // 2 is left out on purpose: it is the installer's own "not confirmed".
        //
        // 6 IS NOT 3 (H19 review). Until then the elevated case exited 3, the
        // installer accepted 3 as "SOLIDWORKS is simply closed" and said
        // "Installed" with exit 0, and the hint about administrator rights
        // was a line among the others: whoever read only the code (an AI, as
        // INSTALL-FOR-AI asks) concluded the bridge would attach, and it
        // never does until the two are started the same way.
        internal const int SelfTestAttached = 0;
        internal const int SelfTestOther = 1;
        internal const int SelfTestNotRunning = 3;
        internal const int SelfTestNotInstalled = 4;
        internal const int SelfTestSkipped = 5;
        internal const int SelfTestNotReachable = 6;

        internal static int SelfTestExitCode(AttachFailure failure)
        {
            switch (failure)
            {
                case AttachFailure.None: return SelfTestAttached;
                case AttachFailure.NotRunning: return SelfTestNotRunning;
                case AttachFailure.NotInstalled: return SelfTestNotInstalled;
                case AttachFailure.Skipped: return SelfTestSkipped;
                default: return SelfTestOther;
            }
        }

        /// <summary>
        /// The code of --self-test, with what this process can see of SOLIDWORKS:
        /// "not running" while a SLDWORKS process runs in this session is 6.
        /// Pure, so the contracts can try both sides without SOLIDWORKS.
        /// </summary>
        internal static int SelfTestCodeSeen(AttachFailure failure, bool solidWorksProcessInThisSession)
        {
            int code = SelfTestExitCode(failure);
            return code == SelfTestNotRunning && solidWorksProcessInThisSession ? SelfTestNotReachable : code;
        }

        internal static string SelfTestMeaning(int code)
        {
            switch (code)
            {
                case SelfTestAttached: return "attached to SOLIDWORKS";
                case SelfTestNotRunning: return "SOLIDWORKS is installed but not running: open it";
                case SelfTestNotInstalled: return "SOLIDWORKS is not installed, or not registered: install or repair it";
                case SelfTestSkipped: return "attach not tried, OCE_NO_ATTACH is set";
                case SelfTestNotReachable:
                    return "a SOLIDWORKS process is running but did not answer: it may run as administrator " +
                           "while this process does not (or the other way round), or it is still starting. " +
                           "Start SOLIDWORKS and the MCP client the same way, then try again";
                default: return "not attached, for another reason: read the lines above";
            }
        }

        /// <summary>
        /// A SLDWORKS process in THIS Windows session. Another user's
        /// SOLIDWORKS on the same machine (fast user switching, a terminal
        /// server) is not one this process could ever attach to, and must not
        /// turn "not running" into "not reachable". A process whose session
        /// cannot be read counts: on a workstation it is almost always ours.
        /// </summary>
        private static bool SolidWorksProcessInThisSession()
        {
            try
            {
                int mine = System.Diagnostics.Process.GetCurrentProcess().SessionId;
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("SLDWORKS"))
                {
                    try { if (p.SessionId == mine) return true; }
                    catch { return true; }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// `--self-test`: a dry run without a client, to know whether the bridge stands
        /// on this machine. It prints to stderr and does not touch the part.
        /// </summary>
        private static int Test(Session session, Catalog catalog)
        {
            Protocol.Note("[self-test] attached: " + session.Attached);
            Protocol.Note("[self-test] drawing library: " + session.LibraryPath);
            Protocol.Note("[self-test] catalog: " + catalog.FilePath);

            if (catalog.HowMany > 0)
            {
                var someone = string.Join(", ",
                    new List<string>(catalog.Names).GetRange(0, Math.Min(5, catalog.HowMany)));
                Protocol.Note("[self-test] first operations: " + someone);
            }

            if (!session.Attached)
            {
                // SOLIDWORKS "not running" while a SLDWORKS process exists in
                // this session is the elevated case: started as administrator,
                // its session is not in the running object table this process
                // can see. It gets its own code (6), not a line beside a 3.
                bool processHere = session.Failure == AttachFailure.NotRunning && SolidWorksProcessInThisSession();
                int code = SelfTestCodeSeen(session.Failure, processHere);
                Protocol.Note("[self-test] exit code " + code + ": " + SelfTestMeaning(code));
                return code;
            }

            // ATTACHED AND WITHOUT DOCUMENTS IS A BRIDGE THAT STANDS (entry H6-bis).
            //
            // Here the state used to be asked and, without an open document, the
            // state returns null: the dry run exited 1, and the installer said
            // «Installed, but the dry run did not confirm» — with SOLIDWORKS
            // on and empty, which is the normal state of whoever has just turned on the
            // computer. Found on 14/9, left without a checkbox until 24/9, and
            // seen again that evening: the bench had closed everything and case 2
            // of `Checks/test-installer.js` turned red.
            //
            // Three outcomes and not two: attached with a document (it measures it),
            // attached without documents (it says so, and it stands), not attached
            // (exits 1, above). That it spoke with SOLIDWORKS is already said by the
            // line with the version number, printed at the attachment.
            if (session.ActiveDocument() == null)
            {
                Protocol.Note("[self-test] no document open: attached, nothing to measure.");
                Protocol.Note("[self-test] the bridge works.");
                return 0;
            }

            var docState = session.State(out var error);
            if (docState == null) { Protocol.Note("[self-test] " + error); return SelfTestOther; }

            foreach (var key in new[] { "document_title", "kind", "volume_mm3", "bodies_total" })
                if (docState.TryGetValue(key, out var v))
                    Protocol.Note("[self-test] " + key + " = " + v);

            Protocol.Note("[self-test] the bridge works.");
            return 0;
        }
    }
}
