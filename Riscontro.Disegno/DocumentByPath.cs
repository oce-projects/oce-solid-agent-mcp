using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// CLOSE OR ACTIVATE A DOCUMENT BY ITS PATH OR ITS REFERENCE, NEVER BY ITS
    /// TITLE (Z-C21.13, findings N11 and M4, pattern S18 of the Z-C21.12 audit).
    ///
    /// ISldWorks.CloseDoc and ISldWorks.ActivateDoc3 take a NAME, and up to
    /// library 0.143.0 every caller passed IModelDoc2.GetTitle(). A title is not
    /// an identity:
    ///  - IModelDoc2~GetTitle.html: «If you chose to suppress known file
    ///    extensions, then the title shown in the window, and returned by this
    ///    method, varies (for example, Part1.sldprt vs. Part1)». With the
    ///    extensions hidden, Base.SLDPRT and Base.SLDASM have the same title, and
    ///    a document never saved has no extension at all;
    ///  - ISldWorks~ActivateDoc3.html: «If you do not specify a file extension in
    ///    Name, the document activated is based on its filename without the file
    ///    extension. This can cause problems if you have loaded two different
    ///    document types with the same name (e.g., 12345.sldprt and
    ///    12345.sldasm). To avoid this problem, specify the file extension in
    ///    Name»;
    ///  - no source, in the 2021 guide or online (help.solidworks.com 2016-2025,
    ///    forums; the research of 29/9/2026 in RAG 82zz-decies), says which of
    ///    two documents CloseDoc closes when the name fits both.
    /// And CloseDoc gives nothing back to read (ISldWorks~CloseDoc.html: «void
    /// CloseDoc(System.string Name)»), while its Remarks name the ways it loses
    /// work: «the name of a document in the dirty state (modified, but not
    /// saved), then this method closes the document without saving it», «This
    /// method also closes any non-active hidden documents», and a name that fits
    /// no open document: «this method does nothing».
    ///
    /// ── WHAT THE N11 PROBE MEASURED (29/9/2026, SOLIDWORKS 2021 SP5.1) ──────
    ///
    /// On this station the extensions are shown, so the equal titles could not
    /// be built; the stem without extension stands in for them:
    ///  - CloseDoc(stem) with an assembly and a part of the same stem open
    ///    closed the PART, which was dirty: the edit was lost (A-S);
    ///  - CloseDoc(GetPathName()) and CloseDoc(file name with extension) closed
    ///    exactly the target, part or assembly, whichever was opened first
    ///    (A-G1, A-G3, P-G1, P-G3);
    ///  - IModelDoc2.Close() throws NotImplementedException (A-G2; the guide:
    ///    «Not implemented. Use ISldWorks::CloseDoc»);
    ///  - a part referenced by an open assembly STAYS LOADED, hidden, after two
    ///    CloseDoc (H1), and when it was dirty its save flag then reads false
    ///    with the edit still in memory (H1d);
    ///  - a hidden dirty part nobody references was closed, edit lost, by the
    ///    CloseDoc of ANOTHER document (H4, H5).
    ///
    /// ── THE RULE ─────────────────────────────────────────────────────────
    ///
    /// Close: a saved document by its full path (then, if it is still open, by
    /// its file name with extension: both measured). A document never saved has
    /// no path (IModelDoc2~GetPathName.html: «if the document has not yet been
    /// saved, then the string is empty»): when it is the ACTIVE document it is
    /// closed with the empty name, which the guide gives for exactly that («an
    /// empty string (""), then the active document is closed without saving»),
    /// so no name can point elsewhere (the probe did not measure "": if the read
    /// back finds it still open, its title follows under the rule below);
    /// otherwise its title is used only when no
    /// other open document, hidden ones included, answers to it (same title, the
    /// same without extension, the same file name with or without extension),
    /// and when one does the call is REFUSED and the refusal says which one.
    ///
    /// Activate: a saved document by its file name with extension (what the page
    /// asks for, and what the C4-4 probe used), then by its full path; one never
    /// saved by its title under the same collision rule. Always
    /// swDontRebuildActiveDoc: swUserDecision (0) opens a dialog that the call
    /// waits on (findings L13-3, N8).
    ///
    /// Both calls are READ BACK by reference: «closed» only when the document is
    /// no longer among the open ones, «in front» only when the active document
    /// is this very object. The pure rules (<see cref="CloseNames"/>,
    /// <see cref="ActivateNames"/>, <see cref="TitleCollision"/>) have no interop
    /// in the signature: Prove.Contratti (DocumentByPathContract) calls them by
    /// reflection with the situations of the probe, since no bench case can hide
    /// the extensions of this station.
    /// </summary>
    public static class DocumentByPath
    {
        /// <summary>The verdict of the read back after CloseDoc.</summary>
        public enum CloseResult
        {
            /// <summary>No longer among the open documents.</summary>
            Closed,
            /// <summary>
            /// Still loaded, hidden: another open document references it. The
            /// guide: «For documents referenced by other documents loaded in
            /// memory, call this method to release user-interface resources while
            /// keeping the model data loaded».
            /// </summary>
            StillLoadedHidden,
            /// <summary>Still open and visible: the call did nothing.</summary>
            StillOpen,
            /// <summary>CloseDoc was not called: no name points only at it.</summary>
            Refused,
            /// <summary>CloseDoc threw.</summary>
            Failed,
        }

        /// <summary>What happened when a document was closed, read back.</summary>
        public sealed class CloseOutcome
        {
            public CloseResult Result = CloseResult.Refused;
            /// <summary>The title read before the call, for the messages.</summary>
            public string WindowTitle = "";
            /// <summary>The path read before the call; empty for a document never saved.</summary>
            public string FilePath = "";
            /// <summary>The save flag read before the call.</summary>
            public bool HadUnsavedChanges;
            /// <summary>The last name given to CloseDoc; null when it was not called, "" for the active document.</summary>
            public string NamePassed;
            /// <summary>One English sentence: what was asked and what the read back found.</summary>
            public string Message = "";
            /// <summary>Other documents open before the call and gone after it (hidden ones: see H4).</summary>
            public readonly List<string> AlsoClosed = new List<string>();
            /// <summary>Of <see cref="AlsoClosed"/>, the ones that had unsaved changes: those changes are gone.</summary>
            public readonly List<string> AlsoClosedUnsaved = new List<string>();

            public bool IsClosed { get { return Result == CloseResult.Closed; } }
        }

        private sealed class Snapshot
        {
            public IModelDoc2 Doc;
            /// <summary>The IUnknown pointer read before the call (<see cref="IdentityOf"/>), null when unreadable.</summary>
            public long? Identity;
            public string FilePath = "";
            public string WindowTitle = "";
            public bool IsVisible = true;
            public bool HasUnsavedChanges;
        }

        private static readonly string[] SolidWorksExtensions = { ".sldprt", ".sldasm", ".slddrw", ".sldlfp" };

        // ── the pure rules ──────────────────────────────────────────────────

        /// <summary>
        /// The names to give CloseDoc, in order, for a document with this path and
        /// title; null (and <paramref name="refusal"/>) when no name points only at
        /// it. "" means «the active document» (ISldWorks~CloseDoc.html: «an empty
        /// string (""), then the active document is closed without saving»), and
        /// is returned only when <paramref name="isActive"/> says the document is
        /// the active one, compared by reference by the caller.
        /// <paramref name="otherPaths"/> and <paramref name="otherTitles"/> are the
        /// OTHER open documents, hidden ones included, one index per document.
        /// </summary>
        public static string[] CloseNames(string path, string title, bool isActive,
                                          string[] otherPaths, string[] otherTitles, out string refusal)
        {
            refusal = null;
            string p = (path ?? "").Trim();
            if (p.Length > 0)
            {
                string fileName = FileNameOf(p);
                return fileName.Length > 0 && !SameName(fileName, p) ? new[] { p, fileName } : new[] { p };
            }
            string t = (title ?? "").Trim();
            string other = t.Length == 0 ? null : TitleCollision(t, otherPaths, otherTitles);
            if (isActive)
            {
                // "" comes from the guide and was NOT measured by the N11 probe
                // (review of Z-C21.13): if it closes nothing, the read back sees
                // the document still open and the title is tried next, but only a
                // title no other open document answers to.
                return t.Length > 0 && other == null ? new[] { "", t } : new[] { "" };
            }
            if (t.Length == 0)
            {
                refusal = "the document has neither a path nor a title: there is no name to give CloseDoc";
                return null;
            }
            if (other != null)
            {
                refusal = "«" + t + "» was never saved, so its title is the only name CloseDoc can take, " +
                          "and the open document «" + other + "» answers to the same name: which of the two " +
                          "CloseDoc would close is documented nowhere, and it closes without saving. Not closed";
                return null;
            }
            return new[] { t };
        }

        /// <summary>
        /// The names to give ActivateDoc3, in order: for a saved document its file
        /// name with extension (ISldWorks~ActivateDoc3.html: «specify the file
        /// extension in Name»), then its full path; for one never saved its title,
        /// under the collision rule of <see cref="CloseNames"/>. Null (and
        /// <paramref name="refusal"/>) when no name points only at it.
        /// </summary>
        public static string[] ActivateNames(string path, string title,
                                             string[] otherPaths, string[] otherTitles, out string refusal)
        {
            refusal = null;
            string p = (path ?? "").Trim();
            if (p.Length > 0)
            {
                string fileName = FileNameOf(p);
                return fileName.Length > 0 && !SameName(fileName, p) ? new[] { fileName, p } : new[] { p };
            }
            string t = (title ?? "").Trim();
            if (t.Length == 0)
            {
                refusal = "the document has neither a path nor a title: there is no name to give ActivateDoc3";
                return null;
            }
            string other = TitleCollision(t, otherPaths, otherTitles);
            if (other != null)
            {
                refusal = "«" + t + "» was never saved, so its title is the only name ActivateDoc3 can take, " +
                          "and the open document «" + other + "» answers to the same name";
                return null;
            }
            return new[] { t };
        }

        /// <summary>
        /// The other open document that answers to <paramref name="title"/>, or
        /// null: one whose title, title without extension, file name or file name
        /// without extension equals the title or the title without extension,
        /// ignoring case (file names on Windows). The stem counts because of the
        /// ActivateDoc3 page («the document activated is based on its filename
        /// without the file extension») and because the N11 probe saw
        /// CloseDoc(stem) close a document whose title had the extension (A-S).
        /// Returns the other document's path, or its title when it has none.
        /// </summary>
        public static string TitleCollision(string title, string[] otherPaths, string[] otherTitles)
        {
            string t = (title ?? "").Trim();
            if (t.Length == 0) return null;
            string tStem = WithoutSolidWorksExtension(t);
            int pathCount = otherPaths == null ? -1 : otherPaths.Length;
            int titleCount = otherTitles == null ? -1 : otherTitles.Length;
            int count = Math.Max(pathCount, titleCount);
            for (int i = 0; i < count; i++)
            {
                string op = i < pathCount ? (otherPaths[i] ?? "").Trim() : "";
                string ot = i < titleCount ? (otherTitles[i] ?? "").Trim() : "";
                var names = new List<string> { ot, WithoutSolidWorksExtension(ot) };
                if (op.Length > 0)
                {
                    string fileName = FileNameOf(op);
                    names.Add(fileName);
                    names.Add(WithoutSolidWorksExtension(fileName));
                }
                foreach (var name in names)
                    if (name.Length > 0 && (SameName(name, t) || SameName(name, tStem)))
                        return op.Length > 0 ? op : ot;
            }
            return null;
        }

        private static string WithoutSolidWorksExtension(string name)
        {
            string n = name ?? "";
            foreach (var extension in SolidWorksExtensions)
                if (n.Length > extension.Length && n.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return n.Substring(0, n.Length - extension.Length);
            return n;
        }

        private static string FileNameOf(string path)
        {
            try { return System.IO.Path.GetFileName(path) ?? ""; }
            catch (ArgumentException) { return ""; }
        }

        private static bool SameName(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalPath(string p)
        {
            return (p ?? "").Trim().Trim('"').Replace('/', '\\');
        }

        // ── the calls ───────────────────────────────────────────────────────

        /// <summary>
        /// Closes <paramref name="doc"/> WITHOUT SAVING (that is what CloseDoc
        /// does) by the rule in the summary, and reads back whether it is gone.
        /// It checks no save flag: whether the document may be closed is the
        /// caller's decision. Never throws.
        /// </summary>
        public static CloseOutcome CloseAndReadBack(ISldWorks sw, IModelDoc2 doc)
        {
            var outcome = new CloseOutcome();
            if (sw == null || doc == null)
            {
                outcome.Message = "no SOLIDWORKS session or no document to close";
                return outcome;
            }
            try { outcome.FilePath = PathOnDisk(doc); } catch (Exception) { }
            try { outcome.WindowTitle = (doc.GetTitle() ?? "").Trim(); } catch (Exception) { }
            try { outcome.HadUnsavedChanges = doc.GetSaveFlag(); } catch (Exception) { outcome.HadUnsavedChanges = true; }
            bool wasVisible = true;
            try { wasVisible = doc.Visible; } catch (Exception) { }
            string label = Label(outcome.FilePath, outcome.WindowTitle);

            bool isActive = false;
            try
            {
                IModelDoc2 active = sw.ActiveDoc as IModelDoc2;
                isActive = SameObject(active, doc);
            }
            catch (Exception) { }

            // The others, read BEFORE the call: their names for the collision rule
            // of a document never saved, and to say afterwards which ones went
            // with it (a closed document can no longer be asked its name).
            var others = new List<Snapshot>();
            foreach (IModelDoc2 d in LoadedDocuments(sw))
            {
                if (SameObject(d, doc)) continue;
                others.Add(Read(d));
            }

            string refusal;
            var names = CloseNames(outcome.FilePath, outcome.WindowTitle, isActive,
                                   PathsOf(others), TitlesOf(others), out refusal);
            if (names == null)
            {
                outcome.Result = CloseResult.Refused;
                outcome.Message = refusal;
                return outcome;
            }

            IModelDoc2 still = null;
            foreach (var name in names)
            {
                outcome.NamePassed = name;
                try { sw.CloseDoc(name); }
                catch (Exception ex)
                {
                    outcome.Result = CloseResult.Failed;
                    outcome.Message = "CloseDoc(«" + name + "») threw: " + ex.Message;
                    Collateral(sw, others, outcome);
                    return outcome;
                }
                // READ BACK: CloseDoc returns void, and «does nothing» on a name
                // that fits no open document.
                still = FindLoaded(sw, doc, outcome.FilePath);
                if (still == null) break;
                bool visibleNow = true;
                try { visibleNow = still.Visible; } catch (Exception) { }
                // Its window is gone and it was visible: it is kept for a
                // reference, and another name would not do more.
                if (!visibleNow && wasVisible) break;
            }
            Collateral(sw, others, outcome);

            string asked = outcome.NamePassed.Length == 0
                ? "CloseDoc(\"\") on the active document «" + label + "»"
                : "CloseDoc(«" + outcome.NamePassed + "»)";
            if (still == null)
            {
                outcome.Result = CloseResult.Closed;
                outcome.Message = "closed «" + label + "» without saving (" + asked + "), read back: no longer open";
            }
            else
            {
                bool visibleAfter = true;
                try { visibleAfter = still.Visible; } catch (Exception) { }
                if (visibleAfter)
                {
                    outcome.Result = CloseResult.StillOpen;
                    outcome.Message = asked + " returned, and «" + label + "» is still open";
                }
                else
                {
                    outcome.Result = CloseResult.StillLoadedHidden;
                    outcome.Message = asked + " returned, and «" + label + "» is still loaded, hidden" +
                                      (wasVisible
                                          ? ": another open document references it, and SOLIDWORKS keeps the " +
                                            "model data of a referenced document"
                                          // Hidden before the call too: nothing here tells a
                                          // referenced document from a call that did nothing.
                                          : " as it was before the call: either another open document " +
                                            "references it, or the call did nothing") +
                                      (outcome.HadUnsavedChanges
                                          ? ". It had unsaved changes, and its save flag is not to be believed " +
                                            "now: the N11 probe (29/9/2026) read it clean with the edit still in memory"
                                          : "");
                }
            }
            if (outcome.AlsoClosed.Count > 0)
                outcome.Message += ". The call also closed " + outcome.AlsoClosed.Count + " other document(s): «" +
                                   string.Join("», «", outcome.AlsoClosed) + "»" +
                                   (outcome.AlsoClosedUnsaved.Count > 0
                                       ? ", with unsaved changes now discarded in «" +
                                         string.Join("», «", outcome.AlsoClosedUnsaved) + "»"
                                       : "");
            return outcome;
        }

        /// <summary>
        /// Brings <paramref name="doc"/> to the front by the rule in the summary,
        /// with swDontRebuildActiveDoc, and reads back that the active document is
        /// this very object. True when it is; otherwise
        /// <paramref name="problem"/> says what was asked and what is in front.
        /// <paramref name="errors"/> is the swActivateDocError_e bitmask of the
        /// last call, -1 when ActivateDoc3 was not called (already in front, or
        /// refused). Never throws.
        /// </summary>
        public static bool ActivateAndReadBack(ISldWorks sw, IModelDoc2 doc, out int errors, out string problem)
        {
            errors = -1;
            problem = null;
            if (sw == null || doc == null)
            {
                problem = "no SOLIDWORKS session or no document to bring to the front";
                return false;
            }
            string path = "", title = "";
            try { path = PathOnDisk(doc); } catch (Exception) { }
            try { title = (doc.GetTitle() ?? "").Trim(); } catch (Exception) { }
            if (IsFront(sw, doc, path)) return true;

            var others = new List<Snapshot>();
            if (path.Length == 0)
                foreach (IModelDoc2 d in LoadedDocuments(sw))
                    if (!SameObject(d, doc)) others.Add(Read(d));

            string refusal;
            var names = ActivateNames(path, title, PathsOf(others), TitlesOf(others), out refusal);
            if (names == null)
            {
                problem = "«" + Label(path, title) + "» was not brought to the front: " + refusal;
                return false;
            }

            var tried = new List<string>();
            string thrown = null;
            foreach (var name in names)
            {
                tried.Add("«" + name + "»");
                int activationErrors = -1;
                try
                {
                    sw.ActivateDoc3(name, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
                                    ref activationErrors);
                }
                catch (Exception ex) { thrown = ex.Message; continue; }
                errors = activationErrors;
                // READ BACK: a call that returns is not a document in front.
                if (IsFront(sw, doc, path)) return true;
            }

            string front = "nothing";
            try
            {
                IModelDoc2 active = sw.ActiveDoc as IModelDoc2;
                if (active != null) front = Label(active.GetPathName(), active.GetTitle());
            }
            catch (Exception) { front = "unreadable"; }
            problem = "asked ActivateDoc3 to bring «" + Label(path, title) + "» to the front with " +
                      string.Join(", then ", tried) + ", and the active document is «" + front + "»" +
                      (errors >= 0 ? " (activation errors: " + ActivationErrorNames(errors) + ")" : "") +
                      (thrown != null ? "; ActivateDoc3 threw: " + thrown : "");
            return false;
        }

        /// <summary>
        /// The hidden documents with unsaved changes that closing
        /// <paramref name="target"/> would put at risk: CloseDoc «also closes any
        /// non-active hidden documents», and the N11 probe saw it close a hidden
        /// dirty part and lose the edit (H4). A hidden document referenced by
        /// another VISIBLE open document stays loaded (H1, H1d), so it is not at
        /// risk; the references read are the components of an open assembly
        /// (IAssemblyDoc.GetComponents, every level) and the models of the views
        /// of an open drawing (IView.GetReferencedModelName). A document
        /// referenced only by the target is at risk: it goes with it. An
        /// unreadable save flag counts as unsaved.
        /// </summary>
        public static List<string> UnsavedHiddenDocumentsAtRisk(ISldWorks sw, IModelDoc2 target)
        {
            var atRisk = new List<string>();
            if (sw == null) return atRisk;
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hiddenUnsaved = new List<Snapshot>();
            foreach (IModelDoc2 d in LoadedDocuments(sw))
            {
                if (SameObject(d, target)) continue;
                bool visible = true;
                try { visible = d.Visible; } catch (Exception) { }
                if (visible)
                {
                    referenced.UnionWith(ReferencedPaths(d));
                    continue;
                }
                bool unsaved;
                try { unsaved = d.GetSaveFlag(); } catch (Exception) { unsaved = true; }
                if (unsaved) hiddenUnsaved.Add(Read(d));
            }
            foreach (var h in hiddenUnsaved)
                if (h.FilePath.Length == 0 || !referenced.Contains(NormalPath(h.FilePath)))
                    atRisk.Add(Label(h.FilePath, h.WindowTitle));
            return atRisk;
        }

        // ── tools ───────────────────────────────────────────────────────────

        private static List<IModelDoc2> LoadedDocuments(ISldWorks sw)
        {
            var loaded = new List<IModelDoc2>();
            try
            {
                IModelDoc2 d = sw.GetFirstDocument() as IModelDoc2;
                int guard = 0;
                while (d != null && guard++ < 2000)
                {
                    loaded.Add(d);
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch (Exception) { }
            return loaded;
        }

        /// <summary>
        /// The document's path when a file is really there, "" otherwise.
        ///
        /// A PATH THAT IS NOT ON DISK IS NOT A NAME (full bench on 0.144.0,
        /// 29/9/2026, group iges_solidi). A part reopened from an IGES with
        /// LoadFile4 was never saved, yet GetPathName answered
        /// «...\lavoro\gp3_solidi.SLDPRT», a file that does not exist: the stem
        /// of the IGES with the part extension. CloseDoc given that path closed
        /// the reopened part AND the original part the export was made from
        /// («Parte2115», never saved), discarding its changes; the collateral
        /// check saw it and said so, after the fact. Such a document is treated
        /// as never saved: its title, under the collision rule.
        /// </summary>
        internal static string PathOnDisk(IModelDoc2 d)
        {
            string p = (d.GetPathName() ?? "").Trim();
            if (p.Length == 0) return "";
            bool there = false;
            try { there = System.IO.File.Exists(p); } catch (Exception) { }
            return there ? p : "";
        }

        private static Snapshot Read(IModelDoc2 d)
        {
            var s = new Snapshot { Doc = d, Identity = IdentityOf(d) };
            try { s.IsVisible = d.Visible; } catch (Exception) { }
            try { s.FilePath = (d.GetPathName() ?? "").Trim(); } catch (Exception) { }
            try { s.WindowTitle = (d.GetTitle() ?? "").Trim(); } catch (Exception) { }
            // An unreadable flag is not a clean one.
            try { s.HasUnsavedChanges = d.GetSaveFlag(); } catch (Exception) { s.HasUnsavedChanges = true; }
            return s;
        }

        /// <summary>
        /// The same COM object? ReferenceEquals first, since .NET almost always
        /// keeps one wrapper per COM object (the N11 probe compared documents by
        /// reference, and it agreed with the paths every time); otherwise the
        /// IUnknown pointers, which are the COM identity (as
        /// EsecutorePiano.StessoOggetto).
        /// </summary>
        private static bool SameObject(object a, object b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            long? ia = IdentityOf(a);
            return ia.HasValue && ia == IdentityOf(b);
        }

        /// <summary>
        /// The IUnknown pointer of a COM object as a number, null when it cannot
        /// be read. The reference GetIUnknownForObject adds is released at once:
        /// a count that does not go back is a document SOLIDWORKS cannot close.
        /// The number stays a valid identity while the object lives, and our
        /// wrapper keeps the proxy alive.
        /// </summary>
        private static long? IdentityOf(object o)
        {
            if (o == null) return null;
            IntPtr unknown = IntPtr.Zero;
            try
            {
                unknown = Marshal.GetIUnknownForObject(o);
                if (unknown == IntPtr.Zero) return null;
                return unknown.ToInt64();
            }
            catch (Exception) { return null; }
            finally
            {
                if (unknown != IntPtr.Zero) Marshal.Release(unknown);
            }
        }

        private static string[] PathsOf(List<Snapshot> list)
        {
            var paths = new string[list.Count];
            for (int i = 0; i < list.Count; i++) paths[i] = list[i].FilePath;
            return paths;
        }

        private static string[] TitlesOf(List<Snapshot> list)
        {
            var titles = new string[list.Count];
            for (int i = 0; i < list.Count; i++) titles[i] = list[i].WindowTitle;
            return titles;
        }

        private static string Label(string path, string title)
        {
            string p = (path ?? "").Trim();
            return p.Length > 0 ? p : (title ?? "").Trim();
        }

        /// <summary>The document, if still loaded: by reference, or by path when it has one.</summary>
        private static IModelDoc2 FindLoaded(ISldWorks sw, IModelDoc2 doc, string path)
        {
            string wanted = NormalPath(path);
            foreach (IModelDoc2 d in LoadedDocuments(sw))
            {
                if (SameObject(d, doc)) return d;
                if (wanted.Length == 0) continue;
                string its = "";
                try { its = NormalPath(d.GetPathName()); } catch (Exception) { }
                if (SameName(its, wanted)) return d;
            }
            return null;
        }

        private static bool IsFront(ISldWorks sw, IModelDoc2 doc, string path)
        {
            try
            {
                IModelDoc2 active = sw.ActiveDoc as IModelDoc2;
                if (active == null) return false;
                if (SameObject(active, doc)) return true;
                string wanted = NormalPath(path);
                return wanted.Length > 0 && SameName(NormalPath(active.GetPathName()), wanted);
            }
            catch (Exception) { return false; }
        }

        private static void Collateral(ISldWorks sw, List<Snapshot> before, CloseOutcome outcome)
        {
            var after = LoadedDocuments(sw);
            var afterIdentities = new HashSet<long>();
            foreach (IModelDoc2 d in after)
            {
                long? identity = IdentityOf(d);
                if (identity.HasValue) afterIdentities.Add((long)identity);
            }
            var gone = new List<string>();
            var goneUnsaved = new List<string>();
            foreach (var s in before)
            {
                bool present = s.Identity.HasValue && afterIdentities.Contains((long)s.Identity);
                if (!present)
                    foreach (IModelDoc2 d in after)
                        if (ReferenceEquals(d, s.Doc)) { present = true; break; }
                if (present) continue;
                string label = Label(s.FilePath, s.WindowTitle);
                if (label.Length == 0) label = "(a document whose name could not be read)";
                gone.Add(label);
                if (s.HasUnsavedChanges) goneUnsaved.Add(label);
            }
            outcome.AlsoClosed.AddRange(gone);
            outcome.AlsoClosedUnsaved.AddRange(goneUnsaved);
        }

        /// <summary>
        /// The swActivateDocError_e names of the bits set in the Errors of
        /// ActivateDoc3 (a bitmask: swActivateDocError_e, «Remarks Bitmask»), from
        /// the interop enum itself, as ErrorCodeDecoder does for the save masks.
        /// </summary>
        private static string ActivationErrorNames(int mask)
        {
            var parts = new List<string>();
            int known = 0;
            foreach (swActivateDocError_e value in Enum.GetValues(typeof(swActivateDocError_e)))
            {
                int bit = (int)value;
                if (bit == 0 || (mask & bit) != bit) continue;
                known |= bit;
                parts.Add(value.ToString());
            }
            int rest = mask & ~known;
            if (rest != 0)
                parts.Add(nameof(swActivateDocError_e) + "(" + rest.ToString(CultureInfo.InvariantCulture) + ")");
            return parts.Count == 0 ? "none" : string.Join(" | ", parts);
        }

        /// <summary>
        /// The paths of the models an open document references: the components
        /// of an assembly (every level) or the models of a drawing's views.
        /// </summary>
        private static List<string> ReferencedPaths(IModelDoc2 d)
        {
            var found = new List<string>();
            int type = -1;
            try { type = d.GetType(); } catch (Exception) { }
            if (type == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                IAssemblyDoc assembly = d as IAssemblyDoc;
                object[] components = null;
                try { if (assembly != null) components = assembly.GetComponents(false) as object[]; }
                catch (Exception) { }
                if (components == null) return found;
                foreach (object c in components)
                {
                    IComponent2 component = c as IComponent2;
                    if (component == null) continue;
                    string p = "";
                    try { p = component.GetPathName() ?? ""; } catch (Exception) { }
                    if (p.Trim().Length > 0) found.Add(NormalPath(p));
                }
            }
            else if (type == (int)swDocumentTypes_e.swDocDRAWING)
            {
                IDrawingDoc drawing = d as IDrawingDoc;
                object[] sheets = null;
                try { if (drawing != null) sheets = drawing.GetViews() as object[]; }
                catch (Exception) { }
                if (sheets == null) return found;
                foreach (object sheet in sheets)
                {
                    object[] views = sheet as object[];
                    if (views == null) continue;
                    foreach (object v in views)
                    {
                        IView view = v as IView;
                        if (view == null) continue;
                        string p = "";
                        try { p = view.GetReferencedModelName() ?? ""; } catch (Exception) { }
                        if (p.Trim().Length > 0) found.Add(NormalPath(p));
                    }
                }
            }
            return found;
        }
    }
}
