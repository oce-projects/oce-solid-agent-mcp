using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Oce.Solid.Agent.Core;
// BEWARE OF THE NAME: SolidWorks.Interop.sldworks has a type `Environment`,
// which makes `Environment.GetEnvironmentVariable` ambiguous. Below, those
// calls are qualified `System.Environment`: it is not verbosity, without it
// it does not compile.
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Bridge
{
    /// <summary>
    /// The attachment to SolidWorks and to the real drawing library.
    ///
    /// NOTHING IS REWRITTEN. The drawing operations are those of
    /// `Oce.Solid.Agent.Core`, called directly: so what is
    /// tested from here is EXACTLY what the designer will have in hand.
    /// A second implementation «only for tests» would measure itself —
    /// which is the most elegant way of measuring nothing.
    ///
    /// Up to add-in 0.46.0 the library loaded was `OpenPDM.SwAddin.dll`,
    /// that is the bridge pulled in the whole PDM to draw a hole, and
    /// reflection was the only way: that DLL is private and this
    /// repository could not reference it.
    ///
    /// Since 0.6.0 `Oce.Solid.Agent.Core` is in here and is referenced like
    /// any project. **Reflection on the types of the library is
    /// gone**: names are names, not strings, and a typing error
    /// is caught by the compiler instead of by the customer.
    ///
    /// The late binding towards SOLIDWORKS remains (`ActiveDocument`,
    /// `Identity`, `Invoke`), and it is not an oversight: it is the measured road,
    /// and the comment above `ActiveDocument` tells why.
    ///
    /// SOLIDWORKS IS NOT STARTED. It attaches to a session that is already there.
    /// A bridge that starts the program decides on behalf of whoever uses it which
    /// version, which license and which document: if it is not open, it says so and
    /// stops.
    /// </summary>
    // `partial`: the life cycle of the document (new, open, list, activate)
    // is in Bridge/Documents.cs, entries Z-B1, Z-B2, Z-B5, Z-B6.
    internal sealed partial class Session
    {
        private object _sw;                 // ISldWorks, kept as object

        /// <summary>
        /// The assembly of the library, kept ONLY to tell its version.
        /// The types are no longer looked for in here: they are named.
        /// </summary>
        private Assembly _library;
        private bool _loaded;

        /// <summary>
        /// Where the drawing library is. It can be changed with
        /// `OCE_DRAWING_LIBRARY`.
        ///
        /// **It is no longer `OpenPDM.SwAddin.dll`.** The operations left
        /// the add-in and live in `Oce.Solid.Agent.Core.dll` (add-in 0.47.0): the
        /// bridge no longer needs the PDM to draw.
        ///
        /// Since the library is referenced instead of loaded by hand,
        /// **this property no longer decides anything**: .NET loads the DLL by
        /// itself, next to the executable. It serves two things, both for whoever
        /// reads and not for the program — saying WHICH file is really being used, and
        /// warning if someone has set a variable that points elsewhere,
        /// instead of letting them believe that variable still counts.
        /// The old `OPENPDM_ADDIN` is still read for the same reason.
        /// </summary>
        public string LibraryPath { get; private set; }

        public bool Attached => _sw != null && _loaded;

        /// <summary>
        /// WHY THE ATTACH FAILED, as a value and not as a sentence (entry H19,
        /// B3). The installer used to decide whether an install had worked by
        /// looking for a phrase in the output of `--self-test`; that phrase is
        /// for a person, and it was about to be translated. `--self-test` now
        /// turns this value into its exit code (Program.SelfTestExitCode),
        /// and the sentence stays for whoever reads it.
        /// </summary>
        public AttachFailure Failure { get; private set; } = AttachFailure.None;

        /// <summary>
        /// Where SolidWorks keeps its interoperability libraries.
        /// It can be changed with SW_INSTALL_PATH (see <see cref="RedistRequested"/>).
        /// </summary>
        private static readonly string[] RedistPlaces =
        {
            @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist",
            @"C:\Program Files\SolidWorks Corp\SolidWorks\api\redist",
        };

        /// <summary>
        /// THE REDIST FOLDER REQUESTED BY WHOEVER INSTALLED, OR `null` (entry H10).
        ///
        /// ── WHY TWO VARIABLES, AND WHY ONLY ONE IS THE RIGHT ONE ───
        ///
        /// There were **three ways** of saying where SOLIDWORKS is, with **two
        /// meanings**: the installation root (`SW_INSTALL_PATH`, which
        /// the `.csproj` files already read) and the `api\redist` subfolder
        /// (`SW_REDIST`). Neither was wrong, but *two names for the
        /// same thing are two things that one day diverge*, and here they
        /// were diverging already: whoever set one in order to build had to
        /// set **also** the other to make the bridge run, without
        /// anything telling them.
        ///
        /// **The root wins.** From `...\SOLIDWORKS` one derives `api\redist`; the
        /// other way round no — from a subfolder one would go up by convention,
        /// and a guessed convention is the place where plausible
        /// and wrong paths are born.
        ///
        /// `SW_REDIST` **is still read**, and stays second: whoever already has it in
        /// their own profile must not notice anything. *A variable that
        /// stops working without saying so is worse than one that was never
        /// there.*
        /// </summary>
        internal static string RedistRequested()
        {
            var root = System.Environment.GetEnvironmentVariable("SW_INSTALL_PATH");
            if (!string.IsNullOrWhiteSpace(root))
                return Path.Combine(root.Trim(), "api", "redist");
            var its = System.Environment.GetEnvironmentVariable("SW_REDIST");
            return string.IsNullOrWhiteSpace(its) ? null : its.Trim();
        }

        /// <summary>
        /// THE DASSAULT LIBRARIES ARE NOT COPIED INTO THE REPOSITORY.
        ///
        /// The add-in is compiled against `SolidWorks.Interop.sldworks.dll`:
        /// as soon as a document is touched, .NET goes to look for it and looks next
        /// to the executable. Putting it there would mean redistributing
        /// a Dassault binary inside a repository — and tying oneself to a
        /// version, which is the surest way of breaking at the next
        /// SolidWorks update.
        ///
        /// The one INSTALLED on this machine is taken instead. So the
        /// version is always the right one by definition: it is the same
        /// one that is running.
        /// </summary>
        private static void FindTheSolidWorksLibraries()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, argument) =>
            {
                var itemName = new AssemblyName(argument.Name).Name;
                if (!itemName.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase))
                    return null;

                var places = new List<string>();
                var its = RedistRequested();
                if (!string.IsNullOrWhiteSpace(its)) places.Add(its);
                places.AddRange(RedistPlaces);

                foreach (var folder in places)
                {
                    var file = Path.Combine(folder, itemName + ".dll");
                    if (!File.Exists(file)) continue;
                    try { return Assembly.LoadFrom(file); }
                    catch (Exception ex)
                    {
                        Mcp.Protocol.Note("[bridge] " + file + ": " + ex.Message);
                    }
                }
                Mcp.Protocol.Note(
                    "[bridge] cannot find " + itemName + ": say where " +
                    "SolidWorks is installed in the variable SW_INSTALL_PATH (the root, " +
                    "the one that ends with «\\SOLIDWORKS»).");
                return null;
            };
        }

        /// <summary>
        /// It attaches, or explains why not. It does not throw: a bridge that
        /// dies at startup has no time to say anything to whoever calls it.
        /// </summary>
        public string Attach()
        {
            FindTheSolidWorksLibraries();
            LibraryPath = System.Environment.GetEnvironmentVariable("OCE_DRAWING_LIBRARY");
            if (string.IsNullOrWhiteSpace(LibraryPath))
                LibraryPath = System.Environment.GetEnvironmentVariable("OPENPDM_ADDIN");
            if (string.IsNullOrWhiteSpace(LibraryPath))
                LibraryPath = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
                    "Oce.Solid.Agent.Core.dll");

            // IT NO LONGER REFUSES IF THE FILE IS NOT WHERE IT IS EXPECTED.
            // The type is now compiled in: if .NET could not find the
            // DLL, the bridge would not even have started. Refusing here
            // would mean stopping for a wrong path while the right
            // library is already in hand — a refusal of what
            // works, which is the worst defect.
            if (!File.Exists(LibraryPath))
                Mcp.Protocol.Note(
                    "[bridge] «" + LibraryPath + "» does not exist; using the one " +
                    "loaded with the executable.");

            try
            {
                // IT SAYS WHICH FILE, EVEN IF THE TYPE IS NOW COMPILED.
                //
                // `SwOperations` comes from the project reference, so
                // .NET loads it by itself from the executable's folder. But
                // whoever set OCE_DRAWING_LIBRARY or OPENPDM_ADDIN
                // expects it to serve some purpose, and a variable that does
                // nothing is worse than one that does not exist: the type is touched and
                // it is seen WHERE it really came from, then it is compared
                // with what had been asked.
                _library = typeof(SwOperations).Assembly;
                _loaded = true;

                var really = "";
                try { really = _library.Location ?? ""; } catch { }
                if (!string.IsNullOrEmpty(really) &&
                    !string.Equals(
                        Path.GetFullPath(really), Path.GetFullPath(LibraryPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    Mcp.Protocol.Note(
                        "[bridge] warning: you said «" + LibraryPath +
                        "» but I am using «" + really + "». The operations are " +
                        "those of the second.");
                    LibraryPath = really;
                }
            }
            catch (Exception ex)
            {
                Failure = AttachFailure.Other;
                return "The drawing library does not load: " + ex.Message;
            }

            // OCE_NO_ATTACH: START WITHOUT TOUCHING SOLIDWORKS (entry H19, L1).
            //
            // A SOLIDWORKS session can be busy with work that must not be
            // disturbed: a batch job, or the bench of this repository, which
            // drives the same session for a quarter of an hour. The installer
            // runs this bridge twice after copying it (the self-test and, with
            // --verify, an MCP handshake), and both used to attach to whatever
            // session was running and read its active document. With this
            // variable set the bridge starts, answers the protocol and lists
            // its tools, and says why it is not attached; the tools that need
            // SOLIDWORKS refuse with that reason. It is read here and nowhere
            // else, so there is one place that decides.
            if (NoAttachRequested())
            {
                Failure = AttachFailure.Skipped;
                return "Not attaching to SOLIDWORKS: OCE_NO_ATTACH is set. Unset it " +
                       "(or set it to 0) and restart the bridge to attach.";
            }

            try
            {
                _sw = Marshal.GetActiveObject("SldWorks.Application");
            }
            catch (COMException ex)
            {
                Failure = FailureOf(ex.ErrorCode);
                return AttachMessage(ex.ErrorCode);
            }
            catch (Exception ex)
            {
                Failure = AttachFailure.Other;
                return "I cannot attach to SolidWorks: " + ex.Message;
            }

            Failure = AttachFailure.None;
            RememberProcess();
            return null;
        }

        /// <summary>
        /// The HRESULT of a failed `GetActiveObject` as a kind of failure:
        /// the same three codes AttachMessage tells apart in words.
        /// </summary>
        internal static AttachFailure FailureOf(int hresult)
        {
            switch (unchecked((uint)hresult))
            {
                case 0x800401E3: return AttachFailure.NotRunning;
                case 0x800401F3:
                case 0x80040154: return AttachFailure.NotInstalled;
                default: return AttachFailure.Other;
            }
        }

        /// <summary>
        /// True when OCE_NO_ATTACH asks the bridge not to attach: any value
        /// except empty, "0" and "false". A variable that is set to "0" and
        /// still switches something off would be the surprise this avoids.
        /// </summary>
        /// <summary>
        /// THE SEQUENCE OF API CALLS IS ANNOUNCED TO SOLIDWORKS (brief R2).
        ///
        /// The guide, ISldWorks~CommandInProgress.html: «Improves performance of
        /// out-of-process applications by informing SOLIDWORKS that a sequence
        /// of API calls will be made by the out-of-process application», and
        /// «Set to True before calling the sequence of API calls, then set to
        /// false after». The bridge is exactly that application, and up to
        /// 0.130.2 it never set it: on 26/10/2026 reading the tree of an EMPTY
        /// part (fifteen features) cost 0.6-1.3 s, tens of milliseconds per call.
        ///
        /// IT IS SET ONLY IF IT WAS FALSE. True at the start of a call means
        /// another program driving SOLIDWORKS left it on: it is not ours to
        /// clear, and the plan refuses as before (PreliminaryChecks). The
        /// answer says whether it is ours, and only ours is put back.
        ///
        /// `OCE_API_SEQUENCE` set to `0`, `false` or `off` turns it off, so the
        /// two ways can be measured one against the other.
        /// </summary>
        public bool BeginApiSequence()
        {
            ApiSequenceOurs = false;
            if (!Attached || !ApiSequenceWanted()) return false;
            try
            {
                var sw = _sw as ISldWorks;
                if (sw == null || sw.CommandInProgress) return false;
                sw.CommandInProgress = true;
                ApiSequenceOurs = true;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Puts back what <see cref="BeginApiSequence"/> set, and only that.</summary>
        public void EndApiSequence(bool ours)
        {
            ApiSequenceOurs = false;
            if (!ours) return;
            try
            {
                var sw = _sw as ISldWorks;
                if (sw != null) sw.CommandInProgress = false;
            }
            catch { }
        }

        /// <summary>
        /// Whether the next plan brings its document back to the front when
        /// another window comes there (brief R1). The `build` tool sets it from
        /// `on_window_change` before every plan; on by default.
        /// </summary>
        public bool ReactivateWindowInPlans { get; set; } = true;

        /// <summary>
        /// Why a material name would not resolve on this workstation, or null
        /// (brief R4). Null also when the bridge is not attached: then nobody
        /// can tell, and the step will say it when it runs.
        /// </summary>
        public string MaterialProblem(string requested, string archive)
        {
            if (!Attached) return null;
            try
            {
                return Oce.Solid.Agent.Core.SolidWorksMaterials.ProblemWith(
                    _sw as ISldWorks, requested, archive);
            }
            catch { return null; }
        }

        /// <summary>True while the call in progress is the one that set CommandInProgress.</summary>
        public bool ApiSequenceOurs { get; private set; }

        /// <summary>What `OCE_API_SEQUENCE` means: on unless `0`, `false` or `off`.</summary>
        internal static bool ApiSequenceWanted()
        {
            var v = System.Environment.GetEnvironmentVariable("OCE_API_SEQUENCE");
            if (string.IsNullOrWhiteSpace(v)) return true;
            v = v.Trim();
            return !(v == "0" ||
                     string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(v, "off", StringComparison.OrdinalIgnoreCase));
        }

        internal static bool NoAttachRequested()
        {
            var v = System.Environment.GetEnvironmentVariable("OCE_NO_ATTACH");
            if (string.IsNullOrWhiteSpace(v)) return false;
            v = v.Trim();
            return v != "0" && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// What to say when `GetActiveObject("SldWorks.Application")` fails
        /// (entry H16). They are TWO different shortcomings with two different actions, and up
        /// to 0.87.0 they had one message only:
        ///
        ///   MK_E_UNAVAILABLE    0x800401E3   installed, not open      -> open it
        ///   CO_E_CLASSSTRING    0x800401F3   the ProgID is not there  -> it is not installed
        ///   REGDB_E_CLASSNOTREG 0x80040154   class not registered     -> same
        ///
        /// Since entry H19 (B3) no program reads these sentences: the installer
        /// reads the exit code of `--self-test`, which comes from FailureOf. They are
        /// for a person, and can be translated like any other message.
        /// </summary>
        internal static string AttachMessage(int hresult)
        {
            switch (unchecked((uint)hresult))
            {
                case 0x800401E3:
                    return "SolidWorks does not seem to be open. This bridge attaches to " +
                           "a session that already exists: open it, open a part, and try again.";
                case 0x800401F3:
                case 0x80040154:
                    return "SolidWorks is not installed on this machine, or is not " +
                           "registered: Windows does not know «SldWorks.Application». " +
                           "Opening it is not enough — it has to be installed, or repaired from the Installation Manager.";
                default:
                    return "I cannot attach to SolidWorks (code 0x" +
                           unchecked((uint)hresult).ToString("X8") + ").";
            }
        }

        /// <summary>
        /// The PID of the `SLDWORKS.exe` process that we attached to.
        /// `0` when it was not possible to find out.
        /// </summary>
        private int _pid;

        /// <summary>
        /// SOLIDWORKS is asked which is its process, and the number is kept.
        ///
        /// WHY NOT `Process.GetProcessesByName("SLDWORKS")`. On this
        /// machine there is more than one SOLIDWORKS session — it is written among
        /// the constraints of the project and we have already been wrong once: searching
        /// by name finds *a* process, not *the* process this bridge
        /// talks to, and a check that looks at the wrong process answers
        /// «alive» while its own is dead. It is the defect that gives no error,
        /// in a check written to catch it.
        ///
        /// IT IS DONE BY REFLECTION and failure is accepted: `_sw` is kept
        /// as `object` on purpose, and if `GetProcessID` is not there on this
        /// version of the interface the bridge must work **as before**,
        /// not refuse. A new check that becomes a new cause of
        /// refusal is a worsening, not a defense.
        /// </summary>
        private void RememberProcess()
        {
            _pid = 0;
            // A new attachment erases the memory of the old one: it is exactly the
            // thing that is done after restarting SOLIDWORKS.
            _lossReason = null;
            if (_sw == null) return;
            try
            {
                var v = Invoke(_sw, "GetProcessID");
                if (v != null) _pid = Convert.ToInt32(v);
            }
            catch
            {
                // Deliberately mute: it is said once only, and further down, that
                // the check is not available. Shouting it at every attachment
                // would teach people to ignore it.
            }
            if (_pid <= 0)
                Mcp.Protocol.Note(
                    "[bridge] I could not find out the SolidWorks process: " +
                    "if it closes in the middle of a plan, I will notice it from the error " +
                    "of the call and not before.");
        }

        /// <summary>
        /// Is SOLIDWORKS still there? Returns `null` if yes, the reason if not.
        ///
        /// It must be called BEFORE a plan and BETWEEN one step and the next. Without it, a
        /// SOLIDWORKS that closes halfway shows up as a null reference
        /// three calls later, at a point that has nothing to do with
        /// the cause — and whoever reads the error looks for the defect where it is not.
        ///
        /// **When the PID could not be found out, this answers `null`**,
        /// that is «as far as I know, alive». It is not an oversight: the check that
        /// is not there must not become a refusal. That it is not there has already been
        /// said at the attachment.
        /// </summary>
        public string SolidWorksHasGone()
        {
            // ONCE IT HAS GONE AWAY, IT STAYS GONE.
            // The reason is kept and repeated: from the second call on one does not
            // go and recheck a process that is not there, and above all the
            // message is not degraded.
            if (_lossReason != null) return _lossReason;
            if (_pid <= 0) return null;
            try
            {
                var p = System.Diagnostics.Process.GetProcessById(_pid);
                if (p == null || p.HasExited) return Disappeared();
                return null;
            }
            catch (ArgumentException)
            {
                // Process.GetProcessById raises ArgumentException when the
                // process no longer exists. It is the case we are looking for.
                return Disappeared();
            }
            catch
            {
                // Any other trouble (permissions, WMI not answering) is NOT
                // a proof that SOLIDWORKS is dead: we stay silent.
                return null;
            }
        }

        private string _lossReason;

        /// <summary>
        /// `_sw` is NOT reset here, and it is a choice.
        ///
        /// Resetting it seemed hygiene — the COM object is dead anyway — but
        /// it makes `Attached` false, and then `build` answers «The bridge is not
        /// attached to SolidWorks»: true, generic, and **worse** than the
        /// message this function has just built. The cause would have been
        /// replaced by a symptom, one run after having found it.
        /// </summary>
        private string Disappeared()
        {
            _lossReason =
                "SolidWorks closed: the process I was attached to " +
                "(PID " + _pid + ") is gone. What had already been " +
                "built stayed in the document and was NOT saved. " +
                "Reopen SolidWorks, reopen the part, and restart the bridge.";
            return _lossReason;
        }

        public string SolidWorksVersion()
        {
            try
            {
                var m = _sw.GetType().InvokeMember(
                    "RevisionNumber", BindingFlags.InvokeMethod, null, _sw, null);
                return Convert.ToString(m);
            }
            catch { return "?"; }
        }

        public string LibraryVersion()
        {
            try { return _library.GetName().Version.ToString(); }
            catch { return "?"; }
        }

        /// <summary>
        /// The document that SolidWorks has in front, or null.
        ///
        /// THE RIGHT NAME IS NOT THE C# ONE. Calling by reflection
        /// on a COM object goes through its IDispatch, and in there there is
        /// no `IActiveDoc2` — that is the name that typed interoperability
        /// exposes in C#. The name that COM knows is
        /// `ActiveDoc`. Asking for the wrong one gives no useful error:
        /// the exception is swallowed and the bridge answers «there is no
        /// document open» while there are twenty — measured on 11
        /// September 2026 at the first test of the bridge.
        /// </summary>
        public object ActiveDocument()
        {
            foreach (var itemName in new[] { "ActiveDoc", "IActiveDoc2" })
            {
                try
                {
                    var d = _sw.GetType().InvokeMember(
                        itemName, BindingFlags.GetProperty, null, _sw, null);
                    if (d != null) return d;
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// WHO the document is: title and path, to be able to recognize it afterwards.
        ///
        /// WHY IT IS NEEDED, AND IT COST SOMEONE ELSE'S PART.
        /// On 14 September 2026 a test session extruded a 314x274x52
        /// block inside «Part4», which was not its document. Not because of a
        /// geometry defect: between the creation of its own part and the
        /// call, the ACTIVE document had changed — a window brought
        /// to the front, and the bridge worked on that one. No error, no
        /// warning, geometry written in the wrong part.
        ///
        /// It is the defect that this project exists to make impossible,
        /// in its purest form, and it was in the bridge.
        /// </summary>
        public Dictionary<string, object> Identity(object doc)
        {
            if (doc == null) return null;
            string docTitle = "", filePath = "";
            try { docTitle = Convert.ToString(Invoke(doc, "GetTitle")); } catch { }
            try { filePath = Convert.ToString(Invoke(doc, "GetPathName")); } catch { }
            return new Dictionary<string, object>
            {
                ["document_title"] = docTitle ?? "",
                ["path"] = filePath ?? "",
            };
        }

        private static object Invoke(object obj, string method)
        {
            return obj.GetType().InvokeMember(
                method, BindingFlags.InvokeMethod, null, obj, null);
        }

        /// <summary>
        /// Are two identities the same document?
        ///
        /// The PATH wins when there is one: it is the only thing that does not change. The
        /// title is the fallback for documents never saved, which have no path
        /// — and they are exactly the ones worked on in tests.
        /// </summary>
        public static bool SameDocument(
            Dictionary<string, object> a, Dictionary<string, object> b)
        {
            if (a == null || b == null) return false;
            string pa = Convert.ToString(a.ContainsKey("path") ? a["path"] : "");
            string pb = Convert.ToString(b.ContainsKey("path") ? b["path"] : "");
            if (!string.IsNullOrWhiteSpace(pa) || !string.IsNullOrWhiteSpace(pb))
                return string.Equals(
                    (pa ?? "").Trim().Replace('/', '\\'),
                    (pb ?? "").Trim().Replace('/', '\\'),
                    StringComparison.OrdinalIgnoreCase);

            string ta = Convert.ToString(a.ContainsKey("document_title") ? a["document_title"] : "");
            string tb = Convert.ToString(b.ContainsKey("document_title") ? b["document_title"] : "");
            return !string.IsNullOrWhiteSpace(ta) &&
                   string.Equals(ta, tb, StringComparison.Ordinal);
        }

        /// <summary>
        /// The state of the active document: it is what the model MUST see
        /// before proposing anything.
        /// </summary>
        /// <summary>
        /// The state of the document, anchor included: it is the reading from which
        /// the SIGNATURES of the steps are built.
        /// </summary>
        public Dictionary<string, object> State(out string error)
        {
            return State(true, out error);
        }

        /// <summary>
        /// The state of the document WITHOUT the anchor.
        ///
        /// The anchor costs: a `GetTessTriangles` and an area-weighted
        /// centroid on EVERY face. On a welded frame that is dozens of COM calls
        /// for a datum that serves one thing only — corroborating the
        /// finding of a face — and therefore only whoever builds the
        /// signatures.
        ///
        /// Up to 0.25.2 the flag was hardwired to `true` and it was paid for even
        /// for the «before» reading of `build`, which only serves to know on
        /// which document one is working, how much volume there was and how many features
        /// were in error: three things the anchor is not about. That state does not
        /// even come back to whoever reads.
        ///
        /// The name is different instead of being one more parameter on the
        /// same method: it is the lesson of `ReadWithAnchor` — changing the
        /// set of signatures of a method breaks whoever calls it by
        /// reflection, even when the compiler says nothing.
        /// </summary>
        public Dictionary<string, object> StateWithoutAnchor(out string error)
        {
            return State(false, out error);
        }

        /// <summary>
        /// The state of the document, anchor included, listing the faces a
        /// filter asks for instead of the sixty largest (entry W3.1-bis.12; see
        /// <see cref="Oce.Solid.Agent.Core.FaceFilter"/>). A new name, as for the
        /// two above: a parameter added to a method the bench calls by
        /// reflection breaks it.
        /// </summary>
        public Dictionary<string, object> StateWithFaces(
            Oce.Solid.Agent.Core.FaceFilter filter, out string error)
        {
            return State(true, filter, out error);
        }

        /// <summary>
        /// THE STATE OF A GIVEN DOCUMENT, not of the window in front (Z-C21.14).
        ///
        /// After a plan the document in front may be another one: a
        /// `create_drawing` puts its drawing in front, another session or the
        /// person may bring a window forward. The live probe M19 (1/10/2026,
        /// line BRIDGE-R1 (X2)) measured what reading the front window did to
        /// «undo»: the plan's part was compared with the numbers of the
        /// other document and a right undo was refused. This reads the plan's
        /// own document, by the reference the executor kept. Without the
        /// anchor: the caller wants the volume, the feature count and the
        /// identity, not signatures.
        /// </summary>
        public Dictionary<string, object> StateOfDocument(object document, out string error)
        {
            error = null;
            var model = document as IModelDoc2;
            if (model == null)
            {
                error = "The document of the plan is not held any more.";
                return null;
            }
            try
            {
                var docState = Oce.Solid.Agent.Core.PhaseClock.Time(
                    "session.state_read", () => ReadState(model, false));
                return docState?.Data;
            }
            catch (Exception ex)
            {
                error = "I cannot read the document of the plan: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Is <paramref name="document"/> the document in front? By the COM
        /// object (its IUnknown), not by name: a plan that saves its part
        /// changes the title and the path of the same document (Z-C21.14).
        /// </summary>
        public bool IsInFront(object document)
        {
            return SameComObject(document, ActiveDocument());
        }

        /// <summary>
        /// Are <paramref name="a"/> and <paramref name="b"/> the same COM
        /// object? By IUnknown, like the executor's SameObject: the name of
        /// a document changes when it is saved, the object does not, and a
        /// document closed and opened again is another object with an empty
        /// undo stack (Z-C21.14). False when either is null.
        /// </summary>
        public static bool SameComObject(object a, object b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            IntPtr pa = IntPtr.Zero, pb = IntPtr.Zero;
            try
            {
                pa = Marshal.GetIUnknownForObject(a);
                pb = Marshal.GetIUnknownForObject(b);
                return pa == pb;
            }
            catch { return false; }
            finally
            {
                if (pa != IntPtr.Zero) Marshal.Release(pa);
                if (pb != IntPtr.Zero) Marshal.Release(pb);
            }
        }

        /// <summary>
        /// What the executor reported about the last plan (Z-C21.14): its
        /// document, the document it ended on and what FinishRecordingUndoObject
        /// answered for the plan's undo entry. Null when the last call to
        /// <see cref="ExecutePlan(List{PlanStep}, out string, Mcp.Progress.Wire, string)"/>
        /// stopped before an executor was made (SOLIDWORKS gone, no document,
        /// another bridge's turn): then nothing was written and no undo entry
        /// was recorded. When the executor threw, a result with nothing filled
        /// and <see cref="LastPlanExecutorThrew"/> true: what it wrote and
        /// recorded is not known.
        /// </summary>
        public PlanExecutor.Result LastPlanResult { get; private set; }

        /// <summary>
        /// True when the executor of the last plan threw instead of returning
        /// its result (Z-C21.14, review F9). Its result is then empty: no plan
        /// document, no answer of FinishRecordingUndoObject.
        /// </summary>
        public bool LastPlanExecutorThrew { get; private set; }

        /// <summary>
        /// The document whose turn was refused to the last plan because another
        /// bridge process held it (Z-C21.14, review F4); null otherwise. At
        /// that moment another writer is known to be working on that document,
        /// and whatever it records lands on top of its undo stack.
        /// </summary>
        public object LastTurnRefusedDocument { get; private set; }

        private Dictionary<string, object> State(bool withAnchor, out string error)
        {
            return State(withAnchor, null, out error);
        }

        private Dictionary<string, object> State(
            bool withAnchor, Oce.Solid.Agent.Core.FaceFilter filter, out string error)
        {
            error = null;
            var doc = ActiveDocument();
            if (doc == null)
            {
                error = "There is no open document in SolidWorks.";
                return null;
            }
            try
            {
                var docState = Oce.Solid.Agent.Core.PhaseClock.Time(
                    withAnchor ? "session.state_read_anchor" : "session.state_read",
                    () => filter == null
                        ? ReadState(doc, withAnchor)
                        : DocumentState.ReadFaces(doc as IModelDoc2, _sw as ISldWorks, withAnchor, filter));
                return docState?.Data;
            }
            catch (Exception ex)
            {
                error = "I cannot read the document: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// The state of the document. The second parameter is the application:
        /// without it, the state cannot say which OTHER documents are open, and
        /// for drawings that is not an extra but a precondition.
        /// </summary>
        private DocumentState ReadState(object doc, bool withAnchor)
        {
            return DocumentState.ReadWithAnchor(doc as IModelDoc2, _sw as ISldWorks, withAnchor);
        }

        /// <summary>
        /// A PHOTOGRAPH of the active document, as PNG. Null with the reason.
        ///
        /// It is here and not in `McpTools` for the same reason as `State`: it is
        /// `Session` that knows which is the document in front, and finding it is the
        /// hard half — the comment above `ActiveDocument` tells
        /// how much it cost.
        ///
        /// The name is new and not one more parameter on something that existed:
        /// see `StateWithoutAnchor`, same rule, same page.
        /// </summary>
        public byte[] Snapshot(string which, int width, int height, out string error)
        {
            error = null;
            var doc = ActiveDocument();
            if (doc == null)
            {
                error = "There is no open document in SolidWorks.";
                return null;
            }

            var model = doc as IModelDoc2;
            if (model == null)
            {
                error = "The active document cannot be photographed.";
                return null;
            }

            return PartView.Fire(model, which, width, height, out error);
        }

        /// <summary>The names of the views that can be asked for.</summary>
        public static IEnumerable<string> ViewNames
        {
            get { return PartView.ViewNames; }
        }

        /// <summary>The planes on which one can section.</summary>
        public static IEnumerable<string> PlaneNames
        {
            get { return PartView.PlaneNames; }
        }

        /// <summary>
        /// A CUTAWAY of the active document, as PNG (entry V4). Null with the
        /// reason.
        ///
        /// New name next to `Snapshot`, not more parameters on that one:
        /// same rule as `StateWithoutAnchor`, and here it counts double because
        /// `Snapshot` is already called by `McpTools`.
        /// </summary>
        public byte[] Cutaway(
            string which, string plane, double offsetMm,
            int width, int height, out string error)
        {
            error = null;
            var doc = ActiveDocument();
            if (doc == null)
            {
                error = "There is no open document in SolidWorks.";
                return null;
            }

            var model = doc as IModelDoc2;
            if (model == null)
            {
                error = "The active document cannot be photographed.";
                return null;
            }

            return PartView.CaptureInSection(
                model, which, plane, offsetMm, width, height, out error);
        }

        /// <summary>
        /// ONE STEP BACK on the document in front. It returns the reason if it
        /// could not be done, null if it went well.
        ///
        /// WHY `EditUndo2` AND NOT `RunCommand`. The menu command depends on
        /// the language of the installation and on what has the focus; `EditUndo2`
        /// is the API, and it does the same thing that `Ctrl+Z` does on
        /// `Checks\Undo`, where it is already proven.
        ///
        /// ONE STEP ONLY, AND IT IS NOT A DETAIL. `PlanExecutor` opens and closes
        /// ONE undo entry around the whole plan
        /// (`StartRecordingUndoObject` / `FinishRecordingUndoObject`): one step
        /// back undoes the whole plan. It is the closing criterion of
        /// entry B8, already measured. Asking for two would mean undoing also
        /// something this bridge did not do.
        ///
        /// `EditUndo2` returns `void`: it does not say whether it undid. Whoever calls must
        /// LOOK at the document afterwards — which is exactly what
        /// `McpTools.Undo` does, and the only reason this operation is
        /// defensible.
        ///
        /// AND A WRONG PRESS CANNOT BE TAKEN BACK when what it removed was an
        /// undo entry recorded through the API (see `StepForward`, probe
        /// `redo` of 1/10/2026). That is why `McpTools.Undo` refuses before
        /// pressing whenever it knows a foreign entry is on top (Z-C21.14).
        /// </summary>
        public string StepBack()
        {
            var doc = ActiveDocument() as IModelDoc2;
            if (doc == null) return "There is no open document in SolidWorks.";
            try { doc.EditUndo2(1); return null; }
            catch (Exception ex) { return "SOLIDWORKS did not undo: " + ex.Message; }
        }

        /// <summary>
        /// ONE STEP FORWARD: EditRedo2(1) on the document in front. Returns the
        /// reason when SOLIDWORKS threw, null when the call returned; EditRedo2
        /// is void, so null says only that it did not throw, never that
        /// anything came back.
        ///
        /// WHAT IT CAN AND CANNOT PUT BACK (measured, probe `redo`, 1/10/2026;
        /// RAG note 82zz_duodecies, section «The live probe of M19»): an
        /// ordinary API action that was not recorded (an extrusion made without
        /// Start/FinishRecordingUndoObject) is redone at once. An undo entry
        /// RECORDED through the API is never redone: not with EditRedo2(1) or
        /// (2), not with RunCommand(swCommands_Redo), not after EditRebuild3 or
        /// ForceRebuild3, with the obsolete Finish and with Finish2, first item
        /// or not, the plan executor's own entry included. Every plan of this
        /// bridge is such an entry. So this is not a way to take back a wrong
        /// undo: `McpTools.Undo` calls it only after a press that took away
        /// something visible, and re-reads the part to say whether it came
        /// back, never assuming it.
        /// </summary>
        public string StepForward()
        {
            var doc = ActiveDocument() as IModelDoc2;
            if (doc == null) return "There is no open document in SolidWorks.";
            try { doc.EditRedo2(1); return null; }
            catch (Exception ex) { return "SOLIDWORKS did not redo: " + ex.Message; }
        }

        /// <summary>
        /// THE WHOLE PLAN, EXECUTED BY THE LIBRARY AND NOT BY THE BRIDGE.
        ///
        /// Up to 0.14.0 the bridge had a loop of its own: a new
        /// <see cref="SwOperations"/> at every step and `stepsDone` set to
        /// `null`. It worked, and it cost three measurable things:
        ///
        ///   * **no references between steps.** A plan of two steps in which the
        ///     second said «the face on top of what you did at step
        ///     1» failed with «I do not have the geometry of that step». Measured on
        ///     14/9/2026: step 1 succeeded, volume 120,000, step 2 refused;
        ///   * **no single undo entry.** `Ctrl+Z` undid only one
        ///     step;
        ///   * **a second implementation of the same cycle**, poorer
        ///     than the one the add-in uses every day. Two implementations
        ///     are two behaviors that diverge, and the checks are done on only
        ///     one of the two.
        ///
        /// Now the cycle is <see cref="PlanExecutor"/>, the same one
        /// as the add-in, and the bridge passes it the steps through
        /// <see cref="PlanInMemory"/>.
        ///
        /// ON THE THREAD AND ON ASYNC. The bridge is synchronous: it reads a line from
        /// standard input, answers, starts again. There is no separate SOLIDWORKS
        /// thread on which to queue, so `onThreadSw` executes
        /// in place and `GetAwaiter().GetResult()` cannot get stuck — all
        /// the waits complete before being awaited.
        /// </summary>
        public List<Dictionary<string, object>> ExecutePlan(
            List<PlanStep> steps, out string error)
        {
            return ExecutePlan(steps, out error, null, null);
        }

        public List<Dictionary<string, object>> ExecutePlan(
            List<PlanStep> steps, out string error,
            Mcp.Progress.Wire wire)
        {
            return ExecutePlan(steps, out error, wire, null);
        }

        /// <summary>
        /// The plan, on the document the caller has already checked (Z-C21.14,
        /// review F7). <paramref name="expectedDocument"/> is that document's
        /// identity in the executor's form (its path, or «title:» and its
        /// title when it has never been saved): the executor reads the active
        /// document once and stops before writing anything when it is not this
        /// one, instead of running on whatever window came to the front between
        /// the caller's reading and now. Null: the executor takes the active
        /// document, as before.
        /// </summary>
        public List<Dictionary<string, object>> ExecutePlan(
            List<PlanStep> steps, out string error,
            Mcp.Progress.Wire wire, string expectedDocument)
        {
            error = null;
            // Z-C21.14: what a plan before this one reported must not be read
            // as this plan's.
            LastPlanResult = null;
            LastPlanExecutorThrew = false;
            LastTurnRefusedDocument = null;

            // FIRST OF ALL: is anyone still on the other side?
            //
            // Without this line, a SOLIDWORKS closed after the attachment shows up as
            // `ActiveDocument()` returning null, and the bridge
            // answers «there is no document open» — which is true and sends
            // one looking for the wrong thing: one goes to open a part inside a
            // SOLIDWORKS that no longer exists.
            error = SolidWorksHasGone();
            if (error != null) return null;

            var doc = ActiveDocument();
            if (doc == null)
            {
                error = "There is no open document in SolidWorks.";
                return null;
            }

            // ── ONE BRIDGE AT A TIME ON A DOCUMENT (entry W2.9) ─────────
            //
            // The precedent has already cost: two sessions on the same
            // SOLIDWORKS, and a test that ended up inside the other's part — a
            // 314x274x52 block extruded into someone else's document, without
            // an error and without a warning. There it was repaired by keeping the
            // `IModelDoc2` reference instead of the active one, and that is enough **inside one
            // process**. Between two bridge PROCESSES that reference says
            // nothing: they are two different COM objects looking at the same file.
            //
            // Here they take turns with a system mutex.
            using (var turn = DocumentTurn.Take(doc, out string whose))
            {
                if (turn == null)
                {
                    error =
                        "Another bridge is already working on this document (" +
                        whose + "). I do not run: two plans on the same part, " +
                        "one on top of the other, give a part that is neither of the " +
                        "two, and neither of them notices. Wait for it to finish, " +
                        "or work on another document.";
                    // Another writer is on this document now (review F4).
                    LastTurnRefusedDocument = doc;
                    return null;
                }
                return RunPlan(steps, out error, wire, expectedDocument);
            }
        }

        /// <summary>
        /// The plan proper, once the turn on the document is
        /// ours.
        ///
        /// It does NOT take `doc`: `PlanExecutor` works through `_sw` and finds the
        /// document by itself. Passing it would be a parameter that says something false
        /// about what this method needs — and the first to read it would think
        /// that the document arrives from here.
        ///
        /// It takes <paramref name="expectedDocument"/>, which is not the
        /// document but the identity the executor checks the active one
        /// against (Z-C21.14, review F7): the executor still finds the document
        /// itself, and refuses before writing when it is another one.
        /// </summary>
        private List<Dictionary<string, object>> RunPlan(
            List<PlanStep> steps, out string error,
            Mcp.Progress.Wire wire, string expectedDocument)
        {
            error = null;
            var source = new PlanInMemory(steps, wire);
            var executor = new PlanExecutor(
                _sw as ISldWorks,
                source,
                line => Mcp.Protocol.Note("  " + line),
                f => System.Threading.Tasks.Task.FromResult(f()))
            {
                // The name that the user reads in the «Undo» menu of SOLIDWORKS.
                UndoName = "MCP bridge",

                // HERE YES, AND ONLY HERE. The bridge works on purpose on parts just
                // opened and never saved: without this, its normal case
                // would be refused with «save it to disk first». In the add-in
                // it stays false, and there is a check that watches over it.
                DocumentWithoutFile = true,

                // AND HERE TOO, AND ONLY HERE. The bridge catalog carries five
                // drawing operations: refusing drawings would make
                // exactly those useless. The add-in's assistant draws parts, and there
                // it stays false.
                AlsoOnDrawings = true,

                // AND THE THIRD, for the exact same reason. The catalog of the
                // bridge carries `insert_component`, `mate`,
                // `replace_component` and `interference_check`: until
                // 18/9/2026 it LISTED them and the executor would have refused them
                // before the first step, without anything saying so.
                // The bench could not notice: it calls the library, not the
                // bridge. Now there is a check that compares the families of the
                // catalog with the allowed types — `Tests\Program.cs` →
                // `EveryCatalogFamilyHasAnAllowedDocument`.
                AlsoOnAssemblies = true,

                // CommandInProgress set by THIS call is not someone else's
                // (brief R2): see BeginApiSequence.
                CommandInProgressIsOurs = ApiSequenceOurs,

                // Another window in front is brought back, not a reason to
                // stop, unless the caller asked to stop (brief R1).
                ReactivateWindow = ReactivateWindowInPlans,
            };

            try
            {
                // Until the executor returns, what it recorded is unknown
                // (Z-C21.14): a throw below leaves this result, not null, and
                // LastPlanExecutorThrew says so.
                LastPlanResult = new PlanExecutor.Result();
                LastPlanExecutorThrew = true;
                var outcome = executor
                    .RunAsync(string.IsNullOrEmpty(expectedDocument) ? null : expectedDocument)
                    .GetAwaiter()
                    .GetResult();
                LastPlanResult = outcome;
                LastPlanExecutorThrew = false;

                // THE ERROR IS REPORTED EVEN WHEN THE OUTCOMES ALREADY SAY SO.
                // A plan stopped BEFORE the first step — wrong document,
                // preliminary checks — leaves no outcome, and without
                // this the bridge would answer «no step, all is well».
                if (!outcome.Completed && !string.IsNullOrEmpty(outcome.Error))
                    error = NameTheTrueCause(outcome.Error, source.Outcomes);
                return source.Outcomes;
            }
            catch (TargetInvocationException ex)
            {
                error = NameTheTrueCause((ex.InnerException ?? ex).Message, source.Outcomes);
                return source.Outcomes;
            }
            catch (Exception ex)
            {
                error = NameTheTrueCause(ex.Message, source.Outcomes);
                return source.Outcomes;
            }
        }

        /// <summary>
        /// If the plan stopped AND in the meantime SOLIDWORKS has gone, the
        /// cause is that — not the message of the call that found
        /// the void.
        ///
        /// WHY IT IS LOOKED AT AFTER AND NOT ONLY BEFORE. The check at the start
        /// catches the case «it was already closed»; this one catches the one that matters
        /// more: **closed halfway through the plan**. There the error that comes up is that
        /// of the first call that found the dead reference — an
        /// «Object reference not set to an instance of an object», or an HRESULT — and whoever
        /// reads it goes looking for a defect in the operation that was running.
        ///
        /// The original message is NOT thrown away: both things are said,
        /// because if one day the two did not match — SOLIDWORKS alive and
        /// a real error — throwing away the original would have hidden the thing to
        /// repair. And **how many steps had already gone** is said, which is what
        /// is needed to know how the document was left.
        /// </summary>
        private string NameTheTrueCause(
            string original, List<Dictionary<string, object>> outcomes)
        {
            var disappeared = SolidWorksHasGone();
            if (disappeared == null) return original;

            // ZERO-E-ZERO: `outcomes` is our list, not a reading from the CAD.
            int done = outcomes == null ? 0 : outcomes.Count;
            return disappeared +
                   " It stopped after " + done +
                   (done == 1 ? " step" : " steps") +
                   ". What SolidWorks last answered: «" +
                   original + "».";
        }
    }

    /// <summary>Why the bridge is not attached to SOLIDWORKS (entry H19, B3).</summary>
    internal enum AttachFailure
    {
        /// <summary>Attached, or not tried yet.</summary>
        None,
        /// <summary>MK_E_UNAVAILABLE: installed, no session running (or one at another integrity level).</summary>
        NotRunning,
        /// <summary>CO_E_CLASSSTRING or REGDB_E_CLASSNOTREG: Windows does not know SldWorks.Application.</summary>
        NotInstalled,
        /// <summary>OCE_NO_ATTACH is set: not tried, on request.</summary>
        Skipped,
        /// <summary>Anything else: the drawing library did not load, or another HRESULT.</summary>
        Other,
    }
}

