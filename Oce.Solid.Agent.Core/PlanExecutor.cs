using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// Executes a confirmed plan, one step at a time.
    ///
    /// HOW SERVER AND ADD-IN TALK TO EACH OTHER
    ///   1. the add-in asks the server WHICH STEP IS NEXT, sending the
    ///      document state at that moment;
    ///   2. the server checks everything again (confirmed plan, same machine,
    ///      same document) and answers with ONE step, or "finished";
    ///   3. the add-in executes it inside SOLIDWORKS and tells how it went;
    ///   4. start again.
    ///
    /// WHY IT ASKS EVERY TIME INSTEAD OF DOWNLOADING THE WHOLE PLAN
    /// Because the check has to be redone: between the first step and the last
    /// minutes can pass, and in between the user may have changed document. A
    /// plan downloaded in one block is a plan that trusts a check made
    /// earlier.
    ///
    /// WHY THE MODEL IS NOT CALLED BETWEEN ONE STEP AND THE NEXT
    /// Because the user confirmed a LIST. If the model could
    /// add a step during execution, something different from what was
    /// approved would be executed. The model speaks again at the end.
    ///
    /// WHAT HAPPENS IF A STEP FAILS
    /// It stops. It does not go on with the following ones "since they are independent":
    /// almost never are they — the hole goes on the plate that the step before did not
    /// create — and going on means building on a false assumption. What
    /// had already been created stays, and the plan records which step got
    /// where: whoever looks knows what is in the document and why.
    /// </summary>
    public class PlanExecutor
    {
        private readonly ISldWorks _sw;
        private readonly IPlanSource _source;
        private readonly Action<string> _log;
        private readonly Func<Func<object>, Task<object>> _onSwThread;

        /// <summary>
        /// What the undo entry that gathers the whole
        /// plan is called. It used to say «OpenPDM assistant»: that is the name the user reads
        /// in the SOLIDWORKS menu, and in a library that does not presuppose
        /// OpenPDM it cannot be written in here.
        /// </summary>
        public string UndoName
        {
            get { return _undoName; }
            set { _undoName = string.IsNullOrWhiteSpace(value) ? "Assistente" : value; }
        }
        private string _undoName = "Assistente";

        /// <summary>
        /// Lets it work on a document NEVER SAVED to disk.
        ///
        /// **To be switched on only by the MCP bridge, never by the add-in.** They are two
        /// different trades that use the same executor:
        ///
        ///   * the add-in works inside a PDM. An unsaved file is not
        ///     checked out, and writing into it produces changes that at
        ///     save time fail or end up in a copy. There the refusal
        ///     «salvalo su disco prima» is the right thing;
        ///   * the MCP bridge works on purpose on a freshly opened `Parte1`. There that
        ///     refusal would make the bridge useless for its normal case.
        ///
        /// WHY IT IS NOT JUST ANY BOOLEAN. The danger written in entry
        /// B8 is that someone sets it to true in the add-in too «because it was
        /// annoying», and writing into files not checked out starts again with
        /// nothing failing. That is why:
        ///
        ///   * the name says what it ALLOWS, not what it turns off;
        ///   * the default value is false, so whoever knows nothing about it is
        ///     protected;
        ///   * and there is a check that watches over it —
        ///     `Checks\check-documents-without-file.ps1` fails if this
        ///     property appears in the add-in sources.
        ///
        /// It does not touch the check on READ-ONLY: that one holds in both
        /// trades, and a read-only file cannot be written anyway.
        /// </summary>
        public bool DocumentWithoutFile { get; set; }

        /// <summary>
        /// Lets it work on a DRAWING too, not only on a part.
        ///
        /// Same story as <see cref="DocumentWithoutFile"/>, another trade:
        ///
        ///   * the add-in's assistant draws PARTS. A plan that arrives on
        ///     a drawing there is almost always the mistake of someone who changed
        ///     window, and stopping is right;
        ///   * the MCP bridge has five drawing operations in the catalogue
        ///     (`create_drawing`, `standard_views`, `set_sheet`,
        ///     `model_dimensions`, `export_pdf`). Refusing drawings
        ///     would make precisely those useless.
        ///
        /// Watched over by `Checks\check-documents-without-file.ps1`
        /// together with the other one.
        ///
        /// HERE IT USED TO SAY THAT ASSEMBLIES STAYED OUT «not by
        /// oversight: no operation of the library can work on an
        /// assembly». It was true until 16/9/2026. On the 17th the entries
        /// GA0-GA10 were born and now there are four assembly operations:
        /// see <see cref="AlsoOnAssemblies"/>, and the lesson written there.
        /// </summary>
        public bool AlsoOnDrawings { get; set; }

        /// <summary>
        /// Lets it work on an ASSEMBLY too.
        ///
        /// Third switch, same shape as the two above: **switched on only by the
        /// MCP bridge, off in the add-in**, where the assistant draws parts and
        /// an assembly is almost always the wrong window.
        ///
        /// ── WHY IT EXISTS, AND WHY IT WAS NOT THERE ──────────────────────
        ///
        /// Until 18/9/2026 `PreliminaryChecks` refused **every** assembly,
        /// and the comment line that defended that choice said: *«no
        /// operation of the library can work on an assembly»*. It was true
        /// when it was written. On 17/9 `insert_component`,
        /// `mate`, `replace_component` and `interference_check` arrived — and
        /// **the catalogue served to the clients listed all four of them**.
        ///
        /// The bridge promised four operations that it would have refused before the
        /// first step. None of the bench's 781 checks could see it:
        /// the bench calls the LIBRARY, not the bridge.
        ///
        /// **A comment that explains a choice is also what stops it from being
        /// reviewed**: whoever reads it finds a reason, and finding a reason
        /// is like ceasing to look. That is why the defense is now not
        /// a sentence but a check —
        /// `Tests\Program.cs` → `EveryCatalogFamilyHasAnAllowedDocument`
        /// compares the catalogue's families with the types allowed below, runs
        /// without SOLIDWORKS, and was born red on this defect.
        /// </summary>
        public bool AlsoOnAssemblies { get; set; }

        public PlanExecutor(
            ISldWorks sw,
            IPlanSource source,
            Action<string> log,
            Func<Func<object>, Task<object>> onThreadSw)
        {
            _sw = sw;
            _source = source;
            _log = log ?? (m => { });
            _onSwThread = onThreadSw;
        }

        public class Result
        {
            public bool Completed;
            public int ExecutedSteps;
            public string Error;

            /// <summary>
            /// Z-C21.14: the document the plan started on, held by reference
            /// (the expectedDocument read once at the start), so that the
            /// bridge reads the state after the plan on THAT document and not
            /// on whatever window is in front at the end. Null only when
            /// SOLIDWORKS had no active document.
            /// </summary>
            public IModelDoc2 PlanDocument;

            /// <summary>
            /// Z-C21.14: the document the plan was working on when it ended:
            /// the plan document, or the new one a step declared
            /// (NewDocument, e.g. the drawing of create_drawing).
            /// </summary>
            public IModelDoc2 FinalDocument;

            /// <summary>
            /// Z-C21.14: what FinishRecordingUndoObject answered for the undo
            /// entry recorded on <see cref="PlanDocument"/>. True: the entry
            /// was closed with something in it, so it now sits on top of that
            /// document's undo stack. False: SOLIDWORKS answered false. The
            /// live probe M19 (1/10/2026, phases H4a and H4b) measured only the
            /// answer: false for an EMPTY recording, true for a full one.
            /// Whether an empty recording leaves an entry on the stack is
            /// UNDECIDED: the probe's control H3 failed, so the second undo and
            /// the comparison of those phases never ran. Null when it is not
            /// known: no recording was
            /// opened on the plan document (the plan stopped before, or
            /// StartRecordingUndoObject threw), the call threw, or a second
            /// recording was opened on it later in the same plan.
            /// </summary>
            public bool? UndoEntryOnPlanDocument;

            /// <summary>
            /// Z-C21.14: true when the plan stopped before opening its undo
            /// recording and before any step: the preliminary checks refused,
            /// or the document was not the one the caller named. Nothing was
            /// written and the undo stack was not touched.
            /// </summary>
            public bool StoppedBeforeWriting;
        }

        // `pianoId` is no longer in the signature: it is a server number, and
        // whoever executes must not know that a server exists. The adapter
        // that implements IPlanSource knows it.
        public async Task<Result> RunAsync(
            string expectedPath,
            CancellationToken undo = default(CancellationToken))
        {
            var result = new Result();

            // If the plan does not say which document it applies to, the active
            // one now is taken — but ONCE only, and from here on that is the one.
            // Before, nothing was ever compared: between reading the state and
            // executing there is a network round trip, and in that window the user
            // may have changed window. The feature ended up in the other
            // document, checked out, inside a single undo entry that
            // nobody knows they have to press.
            // THE DOCUMENT IS HELD BY HAND, NOT BY NAME.
            //
            // Here the object being worked on is taken ONCE, and from here on
            // «is it still the same one?» is asked by comparing the object, not a
            // string.
            //
            // Strings do not hold, and it was measured: a plan that
            // SAVES the part halfway changes both the path (from empty to
            // a file) and the title (from «Parte131» to «z06»). A comparison by
            // name saw the name change and stopped saying «il documento
            // attivo e' cambiato mentre eseguivo» — on a document that had
            // not changed at all, it had only been saved. Refusing what is
            // correct is the worse of the two ways of being wrong.
            //
            // The name stays, but only for the MESSAGES: whoever reads wants to know
            // what the part is called, not its pointer.
            var expectedDocument = (IModelDoc2)await _onSwThread(() => _sw.IActiveDoc2);
            // Z-C21.14: the plan document, kept for whoever reads the result.
            var planDocument = expectedDocument;
            result.PlanDocument = planDocument;
            result.FinalDocument = planDocument;

            // ── AND THE CHECKS LOOK AT **THIS ONE**, NOT AT THE ONE OF NOW ──
            //
            // Until 23/9/2026 `PreliminaryChecks()` ran BEFORE
            // this line and fished `_sw.IActiveDoc2` out by itself: two different
            // reads of the same global, with a window in
            // between. The checks could say «all fine» of document A —
            // it is a part, it is saved, it is not read-only — and the plan
            // take document B, which may be a read-only
            // assembly. **No error would have said so**: both
            // reads were legitimate, and it is the fact that there were TWO that
            // was wrong (entry Z-C5).
            //
            // The document is held by hand, not by name — and not even
            // by asking for it twice.
            //
            // The case «no document open» is told by `PreliminaryChecks`
            // with its own sentence, as before: the null check that was
            // here was the SECOND one, and said the same thing in slightly
            // different words depending on which fired first.
            var check = (string)await _onSwThread(
                () => PreliminaryChecks(expectedDocument));
            if (check != null)
            {
                result.Error = check;
                result.StoppedBeforeWriting = true;
                return result;
            }

            // If the caller said which document the plan applies to, it is
            // verified ONCE, now. After that the object commands.
            if (!string.IsNullOrEmpty(expectedPath))
            {
                var here = (string)await _onSwThread(() => Identity(expectedDocument));
                if (!string.Equals(here, expectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    result.Error =
                        "The plan was for «" + expectedPath + "», but «" +
                        here + "» is in front. I touched nothing.";
                    result.StoppedBeforeWriting = true;
                    return result;
                }
            }
            else
            {
                expectedPath = (string)await _onSwThread(() => Identity(expectedDocument));
            }

            // THE ERROR WINDOW IS TURNED OFF, and put back as it was.
            //
            // Every step rebuilds, and a rebuild with an error opens
            // «Cosa c'e' che non va»: a modal. The COM call that triggered it
            // **never returns**, and a plan launched from an unattended bench
            // stays there all night. It is exactly the lesson
            // already paid for with `swInputDimValOnCreate` on dimensioning.
            //
            // Nothing is lost: the errors are read from the state, which
            // now reports them feature by feature.
            bool showedTheErrors = false;
            bool knewHowToShowThem = false;
            await _onSwThread(() =>
            {
                try
                {
                    showedTheErrors = _sw.GetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swShowErrorsEveryRebuild);
                    knewHowToShowThem = true;
                    _sw.SetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swShowErrorsEveryRebuild, false);
                }
                catch { }
                return null;
            });

            // The whole plan inside ONE undo entry: if the user
            // presses Ctrl+Z right after, it goes back to how it was before, not to halfway.
            // (StartRecordingUndoObject/FinishRecordingUndoObject exist
            // in 2021: verified by reflection on the interop 29.5.1.1.)
            //
            // ON THE DOCUMENT HELD, AND WRITTEN DOWN (finding M19, lot
            // Z-C21.5). Up to library 0.145.0 the recording was started on a
            // fresh read of _sw.IActiveDoc2 and finished on expectedDocument:
            // two documents whenever the window changed in between. Now it is
            // started on expectedDocument itself, and undoRecordedOn remembers
            // which document holds it: that is the one it is finished on, and
            // when a step moves the plan to a new document the recording is
            // closed there and a new one opened on the new document (see
            // NewDocument below), never retargeted in silence.
            IModelDoc2 undoRecordedOn = null;
            bool planDocumentReopened = false;
            await _onSwThread(() =>
            {
                undoRecordedOn = StartUndoRecording(expectedDocument);
                return null;
            });

            // The geometry left by each step, by order number. It lives as
            // long as the execution of the plan and not an instant longer: they are
            // SolidWorks objects, valid only as long as the document stays
            // open and is not rebuilt from scratch.
            var stepsDone = new Dictionary<int, References.Step>();

            try
            {
                while (true)
                {
                    if (undo.IsCancellationRequested)
                    {
                        result.Error = "Stopped by you.";
                        break;
                    }

                    // The state of the document the plan works on (M19), not
                    // of whatever window is in front: a changed window is
                    // caught right before the step writes, below.
                    var docState = (Dictionary<string, object>)await _onSwThread(() =>
                        DocumentState.Read(expectedDocument)?.Data);

                    var next = await _source.NextAsync(docState);

                    // A SOURCE THAT DOES NOT ANSWER IS NOT A FINISHED PLAN.
                    // A null here would mean that the adapter
                    // swallowed something: it is said, not mistaken for
                    // the end. See the note on NextStep.
                    if (next == null)
                    {
                        result.Error =
                            "The source of the plan answered nothing: I do not know whether " +
                            "the plan finished or something was dropped. I stop here, " +
                            "after " + result.ExecutedSteps + " steps.";
                        break;
                    }
                    if (next.Done)
                    {
                        result.Completed = true;
                        break;
                    }

                    var planStep = next.Step;
                    if (planStep == null)
                    {
                        result.Error =
                            "The source says the plan is not finished but gives " +
                            "me no step to do.";
                        break;
                    }
                    _log($"[agent] step {planStep.Order}: {planStep.Summary}");
                    if (undo.IsCancellationRequested)
                    {
                        result.Error = "Stopped by you.";
                        break;
                    }

                    var outcome = (SwOperations.Outcome)await _onSwThread(() =>
                    {
                        var model = _sw.IActiveDoc2;

                        // THE CHECK THAT WAS MISSING, AND THIS IS WHERE IT GOES.
                        // The state sent to the server was read BEFORE the
                        // network call; this is the document that is written
                        // to AFTER. Between the two there is the whole HTTP round trip.
                        if (!SameObject(model, expectedDocument))
                        {
                            return new SwOperations.Outcome
                            {
                                Succeeded = false,
                                Error =
                                    "The active document changed while I was running: " +
                                    "the plan was for «" + expectedPath + "», " +
                                    "now «" + Identity(model) + "» is " +
                                    "in front. I touched nothing.",
                            };
                        }

                        var reading = DocumentState.Read(model);
                        var operations = new SwOperations(_sw, _log);

                        // Entry Z-C4: it announces what it is about to do. The
                        // `finally` is mandatory -- without it, a step that
                        // raises would leave the leash taut and the guardian
                        // would report a call that has already finished as hung.
                        TurnOnGuardian();
                        _leash.Begin(planStep.Operation, DateTime.Now);
                        // `SwOperations.Outcome` with the full name: `Outcome` is a
                        // nested class, and without the prefix the compiler
                        // does not find it.
                        SwOperations.Outcome e;
                        try
                        {
                            e = operations.Run(
                                model, planStep.Operation, planStep.Arguments, reading, stepsDone);
                        }
                        finally { _leash.Finished(); }
                        if (e.Succeeded)
                        {
                            // What this step created stays at the
                            // disposal of the following ones: it is the only way
                            // for «make a hole on the plate of step 1»
                            // to find that plate. It lives in memory and
                            // not in the plan because a SolidWorks face
                            // cannot be serialized: it exists only here, only now,
                            // and only inside this session.
                            stepsDone[planStep.Order] = new References.Step
                            {
                                Feature = e.Feature,
                                Direction = e.Direction,
                                Operation = planStep.Operation,
                            };

                            // A rebuild after every step: that way an error is
                            // seen at once and not at the end, when it is no
                            // longer known which step caused it.
                            try { model.EditRebuild3(); } catch { }

                            // IF THE STEP DECLARED THAT IT CHANGED
                            // DOCUMENT, we follow it. Only if it declares it:
                            // a document that changes without anyone saying so
                            // remains a reason to stop.
                            if (e.NewDocument != null)
                            {
                                // THE UNDO RECORDING FOLLOWS, EXPLICITLY (M19):
                                // the steps so far are closed as one entry on
                                // the document they changed, and the steps to
                                // come are recorded on the new one.
                                if (!SameObject(undoRecordedOn, e.NewDocument))
                                {
                                    NoteUndoAnswer(result, undoRecordedOn, planDocument,
                                                   FinishUndoRecording(undoRecordedOn));
                                    undoRecordedOn = StartUndoRecording(e.NewDocument);
                                    // Back on the plan document: a second entry
                                    // there, and one answer no longer says what
                                    // is on top of its stack.
                                    if (undoRecordedOn != null && SameObject(undoRecordedOn, planDocument))
                                        planDocumentReopened = true;
                                }
                                expectedDocument = e.NewDocument;
                                result.FinalDocument = expectedDocument;
                                _log("[agent] from here on the plan works on «" +
                                     Identity(expectedDocument) + "», with its own undo entry");
                            }
                        }
                        return e;
                    });

                    await _source.OutcomeAsync(
                        planStep.Order,
                        outcome.Succeeded,
                        outcome.Succeeded
                            ? new Dictionary<string, object>
                            {
                                ["feature"] = outcome.CreatedFeature,
                                ["measurements"] = outcome.Measurements,
                            }
                            : null,
                        outcome.Error);

                    if (!outcome.Succeeded)
                    {
                        result.Error = outcome.Error;
                        break;
                    }
                    result.ExecutedSteps++;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                _log("[agent] run interrupted: " + ex.Message);
            }
            finally
            {
                await _onSwThread(() =>
                {
                    // The error window goes back to how it was: it is
                    // a setting of whoever is working, not ours. It is put back
                    // only if it could be read — writing an invented
                    // value over it would be worse than leaving it off.
                    if (knewHowToShowThem)
                    {
                        try
                        {
                            _sw.SetUserPreferenceToggle(
                                (int)swUserPreferenceToggle_e.swShowErrorsEveryRebuild,
                                showedTheErrors);
                        }
                        catch { }
                    }

                    // THE RECORDING IS CLOSED ON THE DOCUMENT IT WAS OPENED ON
                    // (M19): the object written down at the start (or at the
                    // last change of document), not the one with the same name
                    // (it may have been saved and renamed in between) and not
                    // the active one. Up to library 0.145.0 the closing waited
                    // for expectedDocument to be the active document, so a plan
                    // stopped because the window changed left the recording
                    // open. The recording belongs to its document (it is a
                    // member of IModelDocExtension), so another window being in
                    // front does not move it; what FinishRecordingUndoObject
                    // answers is read, not assumed.
                    NoteUndoAnswer(result, undoRecordedOn, planDocument,
                                   FinishUndoRecording(undoRecordedOn));
                    if (planDocumentReopened) result.UndoEntryOnPlanDocument = null;
                    return null;
                });
            }

            return result;
        }

        /// <summary>
        /// Z-C21.14: keeps the answer of FinishRecordingUndoObject when the
        /// recording just closed is the one on the plan document.
        /// </summary>
        private static void NoteUndoAnswer(
            Result result, IModelDoc2 recordedOn, IModelDoc2 planDocument, bool? answer)
        {
            if (recordedOn != null && SameObject(recordedOn, planDocument))
                result.UndoEntryOnPlanDocument = answer;
        }

        /// <summary>
        /// M19: starts the undo recording on <paramref name="document"/> and
        /// returns it, the document that now holds the recording; null when
        /// there is no document or the call threw (then nothing is recorded,
        /// and nothing is finished later). StartRecordingUndoObject is void:
        /// there is no answer to read.
        /// </summary>
        private static IModelDoc2 StartUndoRecording(IModelDoc2 document)
        {
            if (document == null) return null;
            try
            {
                document.Extension.StartRecordingUndoObject();
                return document;
            }
            catch { return null; }
        }

        /// <summary>
        /// M19: finishes the undo recording on the document that holds it, the
        /// one <see cref="StartUndoRecording"/> returned; nothing when null.
        /// The only call to FinishRecordingUndoObject in this file (still the
        /// obsolete one, frozen for Z-C21.7 as L17-4). Its answer, «True if
        /// recording of the SOLIDWORKS Undo object ends, false if not»
        /// (IModelDocExtension~FinishRecordingUndoObject.html), is read and a
        /// false one is logged: a recording left open would take in whatever is
        /// done to that document next.
        ///
        /// Returns the answer (Z-C21.14): true or false as SOLIDWORKS gave it,
        /// null when there was nothing to finish or the call threw. The live
        /// probe M19 (1/10/2026, phases H4a and H4b) measured only the answer:
        /// an EMPTY recording answers false, a recording with something in it
        /// answers true. Whether the empty one leaves an entry on the undo
        /// stack is undecided (the probe's control H3 failed, so that part of
        /// H4 never ran).
        /// </summary>
        private bool? FinishUndoRecording(IModelDoc2 recordedOn)
        {
            if (recordedOn == null) return null;
            bool ended = false;
            string failure = null;
            try { ended = recordedOn.Extension.FinishRecordingUndoObject(_undoName); }
            catch (Exception ex) { failure = ex.Message; }
            if (!ended)
                _log("[agent] the undo entry «" + _undoName + "» on «" + Identity(recordedOn) +
                     "» did not close (FinishRecordingUndoObject " +
                     (failure == null ? "answered false" : "threw: " + failure) + ")");
            if (failure != null) return null;
            return ended;
        }

        /// <summary>
        /// WHAT A DOCUMENT IS CALLED, to know whether it is still the one from before.
        ///
        /// The path on disk, when there is one. When there is not — a part just
        /// opened and never saved — the title, with a prefix that tells it apart:
        /// a file that was really called «Parte1» must not be mistaken
        /// for the in-memory «Parte1».
        ///
        /// Before, the bare paths were compared. Without the title, TWO
        /// never-saved documents both have an empty path, and would have
        /// been called the same document: the check that exists so as not to
        /// write into someone else's part would have said yes in exactly the
        /// case in which the bridge always works.
        /// </summary>
        /// <summary>
        /// ARE TWO REFERENCES THE SAME DOCUMENT?
        ///
        /// Names are not compared: a document that is saved halfway through a plan
        /// changes path AND title, and stays the same document. The
        /// COM objects are compared, that is the pointers to `IUnknown`, which
        /// are the true identity and do not change while the document is open.
        ///
        /// `ReferenceEquals` first, because almost always .NET reuses the same
        /// wrapper for the same COM object and the round trip is saved; but it
        /// is not guaranteed, so when it is not enough `IUnknown` is asked. The two
        /// `Release` calls are not optional: `GetIUnknownForObject` increments the
        /// count, and a count that does not return to zero is a document that
        /// SOLIDWORKS no longer closes.
        /// </summary>
        private static bool SameObject(object a, object b)
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

        private static string Identity(IModelDoc2 model)
        {
            if (model == null) return "";
            try
            {
                var filePath = model.GetPathName();
                if (!string.IsNullOrEmpty(filePath)) return filePath;
                var docTitle = model.GetTitle();
                return string.IsNullOrEmpty(docTitle) ? "" : "titolo:" + docTitle;
            }
            catch { return ""; }
        }

        /// <summary>
        /// The checks to be made BEFORE writing into the document.
        /// Returns null if all is well, otherwise the reason for the refusal.
        ///
        /// ── THE DOCUMENT ARRIVES, IT IS NOT FISHED OUT AGAIN (entry Z-C5) ─────────────
        ///
        /// Before, this method read `_sw.IActiveDoc2` on its own, and the plan
        /// re-read it right after: two reads of the same global
        /// with a window in between, that is the possibility of approving one
        /// document and working on another **without anything saying so**.
        ///
        /// A method that fishes out by itself what it has to decide on is
        /// a method that decides on something else, sooner or later. Now it
        /// receives it: if it is `null`, the caller had nothing to give us.
        /// </summary>
        // ══════════════════════════════════════════════════════════════
        // THE LEASH, FOR WHOEVER USES THE BRIDGE (entry Z-C4)
        //
        // The twin of K18: that one protects the bench RUN, this one
        // protects the CLIENT. Same class, `Shared/Leash.cs`,
        // because two expiry mechanisms that behave differently
        // are worse than a single one.
        //
        // ── AND THE PREMISE OF THE ENTRY WAS NARROWER THAN THE TRUTH ────────
        //
        // Z-C4 said «calls at MODAL risk». On 23/9/2026 the call
        // that did not return had no modal at all: it was `NewDocument`
        // stopped inside SMB on a file held by a socket. An expiry must
        // not presume that there is a window to close: it must only
        // **stop waiting and say on what**.
        //
        // ── WHERE IT WRITES, AND WHY IT IS NOT A DETAIL ───────────────
        //
        // **NEVER ON STDOUT.** On stdio, stdout is the JSON-RPC channel: a
        // line that is not JSON breaks it, and the client answers -32700 to a
        // request that has nothing to do with it. The report goes on **stderr**,
        // which the MCP clients collect in their logs.
        //
        // AND IT DOES NOT KILL THE PROCESS, unlike the bench: the bench is a
        // run that either finishes or is not needed, the bridge is someone
        // else's service. What it can do is **say** that a call
        // is hung and which one — so whoever reads the logs knows what happened
        // instead of seeing a bridge that no longer answers.
        // ══════════════════════════════════════════════════════════════
        private static readonly Shared.Leash _leash =
            new Shared.Leash(120000);

        private static System.Threading.Thread _guardian;
        private static readonly object _guardianKey = new object();

        private static void TurnOnGuardian()
        {
            lock (_guardianKey)
            {
                if (_guardian != null) return;
                _guardian = new System.Threading.Thread(() =>
                {
                    string said = null;
                    while (true)
                    {
                        System.Threading.Thread.Sleep(10000);
                        var report = _leash.Hanging(DateTime.Now);
                        if (report == null) { said = null; continue; }
                        // ONCE ONLY PER CALL: a guardian that
                        // repeats the same line every ten seconds fills
                        // the log of whoever works with it, and a full log is not
                        // read any more.
                        if (report == said) continue;
                        said = report;
                        try { Console.Error.WriteLine("[bridge] " + report); }
                        catch { }
                    }
                });
                _guardian.IsBackground = true;
                _guardian.Start();
            }
        }
        private string PreliminaryChecks(IModelDoc2 model)
        {
            if (model == null) return "There is no open document.";
            int typ = model.GetType();
            bool allowed = typ == (int)swDocumentTypes_e.swDocPART
                || (AlsoOnDrawings && typ == (int)swDocumentTypes_e.swDocDRAWING)
                || (AlsoOnAssemblies && typ == (int)swDocumentTypes_e.swDocASSEMBLY);
            if (!allowed)
            {
                // THE REFUSAL SAYS WHAT IS ALLOWED NOW, not a fixed sentence.
                // The old one said «a part or a drawing, not an assembly»
                // even when assemblies were allowed and the document was
                // something else: a message that lists yesterday's world sends
                // the reader to look for the problem where it is not.
                var go = new System.Collections.Generic.List<string> { "a part" };
                if (AlsoOnDrawings) go.Add("a drawing");
                if (AlsoOnAssemblies) go.Add("an assembly");
                return "I work on " + string.Join(" o ", go) +
                       ": this document is none of those.";
            }

            var filePath = model.GetPathName();
            if (string.IsNullOrEmpty(filePath) && !DocumentWithoutFile)
                return "The document has never been saved: save it on disk first.";

            // A READ-ONLY FILE IS NOT TOUCHED, and it is not a detail.
            // The SOLIDWORKS APIs modify it in memory all the same: the
            // part changes on screen and the save then fails, or
            // worse ends up in a copy. Whoever uses a PDM has one more
            // reason — the file is not checked out — but the check
            // holds without it too.
            try
            {
                var info = new System.IO.FileInfo(filePath);
                if (info.Exists && info.IsReadOnly)
                    return "The file is read-only: remove the read-only flag (or check it out) before asking for changes.";
            }
            catch { /* if we cannot read it, the APIs will say so afterwards*/ }

            // THE MODAL IS SEEN FROM THE WINDOW, NOT FROM SOLIDWORKS (entry Z-C3).
            //
            // Measured on 24/9/2026 with `Checks/Modal`: with a SOLIDWORKS modal
            // window open, `CommandInProgress` stays FALSE in
            // three runs out of three. The guard below had never fired because
            // it could not. The main window instead is DISABLED — an
            // owner with a modal open always is — and
            // `IsWindowEnabled` says so in zero milliseconds, from User32, without
            // going through a COM call that with the modal open might not
            // return at all.
            //
            // `CommandInProgress` stays next to it, but it is NOT a guard
            // against an interactive command (audit finding L17-5, real; the
            // sentence here said it was, with no measure behind it). It is a
            // switch an out-of-process client sets for itself:
            // ISldWorks~CommandInProgress.html, «Improves performance of
            // out-of-process applications by informing SOLIDWORKS that a
            // sequence of API calls will be made by the out-of-process
            // application», and «Set to True before calling the sequence of
            // API calls, then set to false after». With a modal window open it
            // reads false (the Z-C3 measure above). So it fires only when some
            // other program driving SOLIDWORKS left it true, and the refusal
            // says exactly that.
            try
            {
                int frameBorder = (_sw.Frame() as IFrame)?.GetHWnd() ?? 0;
                if (frameBorder != 0 && !IsWindowEnabled(new IntPtr(frameBorder)))
                    return ModalSentence;
            }
            catch { }

            try
            {
                if (_sw.CommandInProgress)
                    return "SOLIDWORKS says a sequence of API calls is in progress " +
                           "(ISldWorks.CommandInProgress is true): another program driving " +
                           "SOLIDWORKS set it and has not cleared it. Let that program finish, " +
                           "or close it, then ask again.";
            }
            catch { }

            return null;
        }

        /// <summary>
        /// The refusal sentence when SOLIDWORKS has an open window that
        /// is waiting for an answer. Public because the bench demands it word
        /// for word: a sentence written in two places diverges.
        /// </summary>
        public const string ModalSentence =
            "SOLIDWORKS has a window open that is waiting for an answer (its main window " +
            "is disabled): close it and try again. Working on top of it now " +
            "would mean hanging until someone clicks.";

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr window);
    }
}
