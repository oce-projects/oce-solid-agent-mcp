using System;
using System.Collections.Generic;
using System.IO;
using Oce.Solid.Agent.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Bridge
{
    /// <summary>
    /// THE LIFE CYCLE OF THE DOCUMENT: creating one, opening one, knowing which
    /// are open, bringing one to the front, closing one that has nothing to
    /// save (entries Z-B6, Z-B1, Z-B5, Z-B2).
    ///
    /// ── WHY IT LIVES IN THE BRIDGE AND NOT IN THE CATALOG ───────────────────
    ///
    /// The operations of the catalog are steps of a plan, and a plan works ON
    /// a document: the executor holds it by the hand from beginning to end, compares
    /// it step by step, and closes everything inside an undo
    /// entry. Creating or opening a document is not a step of that
    /// kind — it is changing the document one works on. And whoever knows «which
    /// part I have looked at» is the bridge (`_guardato`, the signatures seen): entry
    /// Z-B6 already said it, «it changes the active document from inside the bridge, and
    /// it must invalidate the signatures at the same instant».
    ///
    /// Up to here, with no document open the bridge answered «there is
    /// no document open» and stopped: to start a part from scratch
    /// a person was needed who would click File > New. The acid test (P1)
    /// could not even start by itself.
    ///
    /// ── THE TRAPS, written in the entries and kept here ─────────────────────
    ///
    ///  - A wrong document template gives NO error: it gives a document of the wrong
    ///    TYPE, which is discovered when a cast fails further on. The
    ///    type is REREAD after creation.
    ///  - A successful `OpenDoc6` does not mean «the requested file is open»: `GetPathName()`
    ///    is reread and compared.
    ///  - A file already open by a colleague on a network folder opens
    ///    read-only WITHOUT asking anything (Z-B2). It is always said.
    ///  - A successful `ActivateDoc3` does not mean «that one is in front»: the
    ///    active document is reread.
    ///  - `ActivateDoc3` with swDontRebuildActiveDoc does not rebuild by
    ///    itself: if Errors carries swDocNeedsRebuildWarning, EditRebuild3 is
    ///    called here, or a first read-only step right after would measure
    ///    stale geometry (C4-3).
    /// </summary>
    internal sealed partial class Session
    {
        /// <summary>Creates an empty part or assembly from the default template.</summary>
        public Dictionary<string, object> CreateDocument(string typ, out string error)
        {
            error = null;
            var sw = _sw as ISldWorks;
            if (sw == null) { error = "The bridge is not attached to SolidWorks."; return null; }

            int preference, expected;
            string typeName;
            switch ((typ ?? "").Trim().ToLowerInvariant())
            {
                case "part":
                    preference = (int)swUserPreferenceStringValue_e.swDefaultTemplatePart;
                    expected = (int)swDocumentTypes_e.swDocPART;
                    typeName = "part";
                    break;
                case "assembly":
                    preference = (int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly;
                    expected = (int)swDocumentTypes_e.swDocASSEMBLY;
                    typeName = "assembly";
                    break;
                default:
                    error = "«variant» is «part» or «assembly». A drawing starts from a " +
                             "part, with «create_drawing» inside a plan.";
                    return null;
            }

            string model = "";
            try { model = sw.GetUserPreferenceStringValue(preference) ?? ""; } catch { }
            if (string.IsNullOrWhiteSpace(model))
            {
                error = "SOLIDWORKS has no default template for a " + typeName +
                         " (Options > File Locations > Document Templates). Without one, " +
                         "NewDocument would open a choice window that nobody closes.";
                return null;
            }
            if (!File.Exists(model))
            {
                error = "The default template for a " + typeName + " does not exist: «" +
                         model + "». If it is on a network drive, it may not " +
                         "be answering right now.";
                return null;
            }

            var doc = sw.NewDocument(model, 0, 0, 0) as IModelDoc2;
            if (doc == null)
            {
                error = "NewDocument created nothing, with the template «" + model + "».";
                return null;
            }

            // THE TYPE IS REREAD: a wrong template gives a document of the wrong
            // type with no error at all.
            int real = doc.GetType();
            if (real != expected)
            {
                // Just born and empty: closing it throws away nobody's work. By
                // reference, not by title (Z-C21.13 N11): it is the active
                // document, and a new document's title can be the stem of an
                // open file. Read back before saying it is closed.
                var closing = DocumentByPath.CloseAndReadBack(sw, doc);
                error = "The default template for a new " + TypeNameInEnglish(expected) + " («" + model +
                         "») made a document of type " + TypeNameInEnglish(real) + ". " +
                         (closing.IsClosed
                             ? "I closed it at once: it was empty."
                             : "It was empty, and it is NOT closed: " + closing.Message + ".");
                return null;
            }

            var outside = Card(doc, ActiveDocument());
            outside["model"] = model;
            return outside;
        }

        /// <summary>Opens a SOLIDWORKS file from disk and brings it to the front.</summary>
        public Dictionary<string, object> OpenDocument(string filePath, out string error)
        {
            error = null;
            var sw = _sw as ISldWorks;
            if (sw == null) { error = "The bridge is not attached to SolidWorks."; return null; }
            if (string.IsNullOrWhiteSpace(filePath))
            {
                error = "«path» is required: the file to open, with its full path.";
                return null;
            }
            filePath = filePath.Trim().Trim('"');

            int typ;
            switch (Path.GetExtension(filePath).ToLowerInvariant())
            {
                case ".sldprt": typ = (int)swDocumentTypes_e.swDocPART; break;
                case ".sldasm": typ = (int)swDocumentTypes_e.swDocASSEMBLY; break;
                case ".slddrw": typ = (int)swDocumentTypes_e.swDocDRAWING; break;
                default:
                    error = "I only open SOLIDWORKS files: .sldprt, .sldasm, .slddrw. A " +
                             "STEP or an IGES is imported, not opened: that is another way.";
                    return null;
            }
            if (!Path.IsPathRooted(filePath))
            {
                error = "I want the full path, not «" + filePath + "»: a relative path " +
                         "depends on where SOLIDWORKS thinks it is.";
                return null;
            }
            if (!File.Exists(filePath))
            {
                error = "The file does not exist: «" + filePath + "». The active document " +
                         "did not change.";
                return null;
            }

            // ALREADY OPEN: it is not reopened, it is brought to the front. Reopening it would give an
            // «already open» warning and no change, which reads as a
            // success only if one knows what to look at.
            //
            // A PART LOADED HIDDEN BY AN ASSEMBLY IS FOUND AND SHOWN TOO
            // (finding C4-4, measured: it works). ISldWorks~GetOpenDocumentByName.html
            // says «Only call this method for a document that has already been
            // opened in its own window», and the doubt was a false refusal for a
            // part the assembly holds without a window. The Z-C21.3 probe of
            // C4-4 (28/9/2026) closed the part's window with the assembly
            // holding it: GetOpenDocumentByName gave the same object (Visible
            // False), and ActivateDoc3 in BringToFront brought it to the front,
            // errors 0, Visible True afterwards. The
            // guide's way (a Visible filter, then OpenDoc6) ended the same, with
            // swFileLoadWarning_AlreadyOpen. So no filter here.
            var already = sw.GetOpenDocumentByName(filePath) as IModelDoc2;
            if (already != null)
            {
                var brought = BringToFront(sw, already, out error);
                if (brought == null) return null;
                brought["was_already_open"] = true;
                return brought;
            }

            int errors = 0, warnings = 0;
            var doc = sw.OpenDoc6(filePath, typ,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings) as IModelDoc2;
            if (doc == null || errors != 0)
            {
                error = "SOLIDWORKS did not open «" + filePath + "»: " + OpeningErrors(errors) +
                         (warnings != 0 ? " Warnings: " + OpeningWarnings(warnings) + "." : "");
                return null;
            }

            // IS THE OPEN FILE THE REQUESTED ONE? It is reread, not believed.
            string open = doc.GetPathName() ?? "";
            if (!string.Equals(Normal(open), Normal(filePath), StringComparison.OrdinalIgnoreCase))
            {
                error = "I asked for «" + filePath + "» and SOLIDWORKS says it opened «" +
                         open + "». I will not use it: it could be another file with the " +
                         "same name.";
                return null;
            }

            // AND IS IT IN FRONT? OpenDoc6 usually activates it; it is checked.
            var outside = BringToFront(sw, doc, out error);
            if (outside == null) return null;
            outside["was_already_open"] = false;
            if (warnings != 0) outside["opening_warnings"] = OpeningWarnings(warnings);
            return outside;
        }

        /// <summary>The documents that are open and visible, with which one is in front.</summary>
        public List<Dictionary<string, object>> Documents(out int hidden, out string error)
        {
            error = null;
            hidden = 0;
            var sw = _sw as ISldWorks;
            if (sw == null) { error = "The bridge is not attached to SolidWorks."; return null; }

            var active = ActiveDocument();
            var outside = new List<Dictionary<string, object>>();
            var doc = sw.GetFirstDocument() as IModelDoc2;
            int benchRun = 0;
            while (doc != null && benchRun++ < 500)
            {
                bool visible = true;
                try { visible = doc.Visible; } catch { }
                // THE HIDDEN ONES ARE THE PARTS LOADED INSIDE AN ASSEMBLY: they are
                // documents for SOLIDWORKS and not for whoever works. They are counted and
                // reported, not listed.
                if (visible) outside.Add(Card(doc, active));
                else hidden++;
                doc = doc.GetNext() as IModelDoc2;
            }
            return outside;
        }

        /// <summary>Brings an already open document to the front, by path or title.</summary>
        public Dictionary<string, object> ActivateDocument(string which, out string error)
        {
            error = null;
            var sw = _sw as ISldWorks;
            if (sw == null) { error = "The bridge is not attached to SolidWorks."; return null; }
            if (string.IsNullOrWhiteSpace(which))
            {
                error = "«path» is required: the path or the title of an open document.";
                return null;
            }
            which = which.Trim().Trim('"');

            List<string> titles;
            var found = FindOpen(sw, which, out titles);
            if (found == null)
            {
                error = "«" + which + "» is not among the open documents. Open now: " +
                         (titles.Count == 0 ? "none" : "«" + string.Join("», «", titles) + "»") +
                         ". The active document did not change.";
                return null;
            }
            return BringToFront(sw, found, out error);
        }

        /// <summary>
        /// Closes an open document, by path or title, and REFUSES if it has
        /// unsaved changes (entry Z-B1).
        ///
        /// There is no «close discarding the changes»: whoever works on behalf of
        /// someone does not decide to throw away their work, and an option that does it
        /// is an option that sooner or later is passed by mistake. Whoever wants to
        /// discard them closes by hand, and the SOLIDWORKS window asks them.
        ///
        /// IT IS REREAD that it is really closed: `CloseDoc` returns `void`.
        /// </summary>
        public Dictionary<string, object> CloseDocument(string which, out string error)
        {
            error = null;
            var sw = _sw as ISldWorks;
            if (sw == null) { error = "The bridge is not attached to SolidWorks."; return null; }
            if (string.IsNullOrWhiteSpace(which))
            {
                error = "«path» is required: the path or the title of the document to close.";
                return null;
            }
            which = which.Trim().Trim('"');

            List<string> titles;
            var doc = FindOpen(sw, which, out titles);
            if (doc == null)
            {
                error = "«" + which + "» is not among the open documents. Open now: " +
                         (titles.Count == 0 ? "none" : "«" + string.Join("», «", titles) + "»") + ".";
                return null;
            }

            bool dirty = false;
            try { dirty = doc.GetSaveFlag(); } catch { }
            string docTitle = doc.GetTitle() ?? "";
            if (dirty)
            {
                error = "«" + docTitle + "» has unsaved changes: I do not close it, I would throw them away. " +
                         "Save it first («save_document», inside a plan), or close it by " +
                         "hand if those changes are to be thrown away.";
                return null;
            }

            // THE HIDDEN DOCUMENTS IT WOULD TAKE WITH IT (Z-C21.13 N11).
            // ISldWorks~CloseDoc.html: «This method also closes any non-active
            // hidden documents», and the N11 probe (29/9/2026) saw CloseDoc of
            // one document close a hidden part with an unsaved edit, which was
            // lost (H4). The close action promises to throw away nobody's work,
            // so the hidden documents with unsaved changes that no other open
            // document holds are a refusal too.
            var atRisk = DocumentByPath.UnsavedHiddenDocumentsAtRisk(sw, doc);
            if (atRisk.Count > 0)
            {
                error = "Not closing «" + docTitle + "»: CloseDoc also closes the hidden documents that no " +
                         "other open document uses, without saving them, and these have unsaved changes: «" +
                         string.Join("», «", atRisk) + "». Save them first (a component is saved with the " +
                         "assembly that uses it), or close by hand if those changes are to be thrown away.";
                return null;
            }

            // BY PATH, OR FOR A DOCUMENT NEVER SAVED BY REFERENCE, NEVER BY TITLE
            // (Z-C21.13 N11): with the extensions hidden Base.SLDPRT and
            // Base.SLDASM have the same title, and nothing documents which one
            // CloseDoc closes. DocumentByPath reads back whether it is gone.
            var id = Identity(doc);
            string filePath = Convert.ToString(id["path"] ?? "");
            var closing = DocumentByPath.CloseAndReadBack(sw, doc);
            if (closing.Result != DocumentByPath.CloseResult.Closed &&
                closing.Result != DocumentByPath.CloseResult.StillLoadedHidden)
            {
                error = "«" + docTitle + "» is not closed: " + closing.Message + ".";
                return null;
            }

            // READ BACK from the bridge's side too: not among the visible documents.
            List<string> after;
            if (FindOpen(sw, string.IsNullOrEmpty(filePath) ? docTitle : filePath, out after) != null)
            {
                error = "I asked to close «" + docTitle + "» and it is still open (" + closing.Message + ").";
                return null;
            }
            var front = ActiveDocument();
            var answer = new Dictionary<string, object>
            {
                ["closed"] = docTitle,
                ["path"] = string.IsNullOrEmpty(filePath) ? null : filePath,
                ["open_now"] = after.Count,
                ["in_front_now"] = front == null ? null : Identity(front)["document_title"],
                ["closed_by"] = closing.NamePassed == ""
                    ? "the active document (CloseDoc with an empty name)"
                    : closing.NamePassed,
            };
            // ITS WINDOW IS GONE, ITS MODEL IS NOT: another open document
            // references it (the N11 probe, H1). It had no unsaved changes, or
            // it would have been refused above, so nothing is at risk; it is said.
            if (closing.Result == DocumentByPath.CloseResult.StillLoadedHidden)
            {
                answer["still_loaded_hidden"] = true;
                answer["still_loaded_reason"] = closing.Message;
            }
            if (closing.AlsoClosed.Count > 0) answer["also_closed"] = closing.AlsoClosed;
            if (closing.AlsoClosedUnsaved.Count > 0) answer["also_closed_unsaved"] = closing.AlsoClosedUnsaved;
            return answer;
        }

        /// <summary>
        /// An open and visible document, by path or — if it has never been
        /// saved — by title. `titoli` reports what there was, to say it in a
        /// refusal.
        /// </summary>
        private IModelDoc2 FindOpen(ISldWorks sw, string which, out List<string> titles)
        {
            IModelDoc2 found = null;
            titles = new List<string>();
            var doc = sw.GetFirstDocument() as IModelDoc2;
            int benchRun = 0;
            while (doc != null && benchRun++ < 500)
            {
                bool visible = true;
                try { visible = doc.Visible; } catch { }
                if (visible)
                {
                    string p = doc.GetPathName() ?? "", t = doc.GetTitle() ?? "";
                    titles.Add(string.IsNullOrEmpty(p) ? t : p);
                    bool matches = !string.IsNullOrEmpty(p)
                        ? string.Equals(Normal(p), Normal(which), StringComparison.OrdinalIgnoreCase)
                        : string.Equals(t, which, StringComparison.Ordinal);
                    if (matches && found == null) found = doc;
                }
                doc = doc.GetNext() as IModelDoc2;
            }
            return found;
        }

        // ── tools ────────────────────────────────────────────────────────

        /// <summary>
        /// Brings <paramref name="doc"/> to the front by its file name or path,
        /// or by title only when it was never saved and no other open document
        /// answers to that title (Z-C21.13, pattern S18: ActivateDoc3.html warns
        /// that a name without extension can fit a part and an assembly at once).
        /// DocumentByPath reads back the active document by reference.
        ///
        /// C4-3: DocumentByPath always asks ActivateDoc3 with
        /// swDontRebuildActiveDoc (finding N8: swUserDecision opens a dialog
        /// that waits on a person). ActivateDoc3.html (ISldWorks): «If the
        /// document needs a rebuild upon activation, and you have specified
        /// Option with swRebuildOnActivation_e.swDontRebuildActiveDoc, then the
        /// activated document is not rebuilt, and Errors contains
        /// swActivateDocError_e.swDocNeedsRebuildWarning. You can then rebuild
        /// the document using IModelDoc2::EditRebuild3.» Up to library 0.146.0
        /// <paramref name="doc"/>'s caller got this activation for free but the
        /// bit was thrown away: a read-only step right after (`mass_properties`,
        /// `overall`) would measure stale geometry, since the executor's own
        /// rebuild only runs after a successful step (PlanExecutor.cs), too
        /// late for the very first one.
        /// </summary>
        private Dictionary<string, object> BringToFront(ISldWorks sw, IModelDoc2 doc, out string error)
        {
            error = null;
            int activationErrors;
            string problem;
            if (!DocumentByPath.ActivateAndReadBack(sw, doc, out activationErrors, out problem))
            {
                error = problem + ".";
                return null;
            }
            // READ BACK from the bridge's side too: the identity it compares.
            var front = ActiveDocument();
            if (!SameDocument(Identity(front), Identity(doc)))
            {
                error = "I asked to bring «" + doc.GetTitle() + "» to the front, and the active document " +
                         "is «" + Convert.ToString(Identity(front)?["document_title"] ?? "nothing") + "».";
                return null;
            }

            bool needsRebuild = NeedsRebuildAfterActivation(activationErrors);
            bool rebuiltOnActivation = false;
            string rebuildFault = null;
            if (needsRebuild)
            {
                // IModelDoc2~EditRebuild3.html, Return Value: "True if only those
                // features that need to be rebuilt are rebuilt in the active
                // configuration in the model, false if not".
                try
                {
                    rebuiltOnActivation = doc.EditRebuild3();
                    if (!rebuiltOnActivation) rebuildFault = "EditRebuild3 answered false";
                }
                catch (Exception ex) { rebuildFault = "EditRebuild3 threw: " + ex.Message; }
            }

            var card = Card(doc, front);
            if (needsRebuild)
            {
                card["rebuilt_on_activation"] = rebuiltOnActivation;
                card["rebuilt_on_activation_reason"] =
                    "ActivateDoc3 reported swDocNeedsRebuildWarning: with swDontRebuildActiveDoc it does not " +
                    "rebuild on activation by itself, " +
                    (rebuiltOnActivation
                        ? "so it was rebuilt here before answering."
                        : "and the rebuild asked here failed (" + rebuildFault + "): what is read from it now " +
                          "may be stale geometry.");
            }
            return card;
        }

        /// <summary>
        /// C4-3: does the bitmask ActivateDoc3 filled in <c>Errors</c> (with
        /// swDontRebuildActiveDoc) say the document was left un-rebuilt?
        /// <paramref name="activationErrors"/> is -1 when ActivateDoc3 was not
        /// called at all — <see cref="DocumentByPath.ActivateAndReadBack"/>
        /// skips the call when the document is already in front — and that is
        /// not a warning to act on: nothing was skipped.
        /// </summary>
        internal static bool NeedsRebuildAfterActivation(int activationErrors)
        {
            return activationErrors >= 0 &&
                   (activationErrors & (int)swActivateDocError_e.swDocNeedsRebuildWarning) != 0;
        }

        private Dictionary<string, object> Card(IModelDoc2 doc, object active)
        {
            bool isReadOnly = false, modified = false;
            try { isReadOnly = doc.IsOpenedReadOnly(); } catch { }
            try { modified = doc.GetSaveFlag(); } catch { }
            var id = Identity(doc);
            var card = new Dictionary<string, object>
            {
                ["document_title"] = id["document_title"],
                ["path"] = id["path"],
                ["kind"] = TypeName(doc.GetType()),
                ["read_only"] = isReadOnly,
                ["unsaved_changes"] = modified,
                ["in_front"] = active != null && SameDocument(Identity(active), id),
            };
            if (isReadOnly)
                card["warning"] =
                    "READ ONLY: usually it means a colleague has the file open. " +
                    "«build» does not write to it: the changes would stay in memory and the " +
                    "save would fail, or end up in a copy.";
            return card;
        }

        private static string TypeName(int typ)
        {
            switch (typ)
            {
                case (int)swDocumentTypes_e.swDocPART: return "part";
                case (int)swDocumentTypes_e.swDocASSEMBLY: return "assembly";
                case (int)swDocumentTypes_e.swDocDRAWING: return "drawing";
                default: return "other (" + typ + ")";
            }
        }

        /// <summary>The document type for the English messages of Z-C21.13.</summary>
        private static string TypeNameInEnglish(int type)
        {
            switch (type)
            {
                case (int)swDocumentTypes_e.swDocPART: return "part";
                case (int)swDocumentTypes_e.swDocASSEMBLY: return "assembly";
                case (int)swDocumentTypes_e.swDocDRAWING: return "drawing";
                default: return "type " + type;
            }
        }

        private static string Normal(string p)
        {
            return (p ?? "").Trim().Trim('"').Replace('/', '\\');
        }

        private static string OpeningErrors(int e)
        {
            var names = new List<string>();
            if ((e & (int)swFileLoadError_e.swFileNotFoundError) != 0) names.Add("file not found");
            if ((e & (int)swFileLoadError_e.swInvalidFileTypeError) != 0) names.Add("invalid file type");
            if ((e & (int)swFileLoadError_e.swFutureVersion) != 0) names.Add("saved by a newer version of SOLIDWORKS");
            if ((e & (int)swFileLoadError_e.swGenericError) != 0) names.Add("generic error");
            return names.Count == 0 ? "code " + e : string.Join(", ", names) + " (code " + e + ")";
        }

        private static string OpeningWarnings(int a)
        {
            var names = new List<string>();
            if ((a & (int)swFileLoadWarning_e.swFileLoadWarning_ReadOnly) != 0) names.Add("opened read-only");
            if ((a & (int)swFileLoadWarning_e.swFileLoadWarning_SharingViolation) != 0) names.Add("the file is in use by someone else");
            if ((a & (int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen) != 0) names.Add("was already open");
            if ((a & (int)swFileLoadWarning_e.swFileLoadWarning_NeedsRegen) != 0) names.Add("needs a rebuild");
            if ((a & (int)swFileLoadWarning_e.swFileLoadWarning_ModelOutOfDate) != 0) names.Add("model out of date");
            return names.Count == 0 ? "code " + a : string.Join(", ", names) + " (code " + a + ")";
        }
    }
}
