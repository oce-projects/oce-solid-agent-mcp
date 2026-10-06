using System;
using System.IO;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// HOW MANY TIMES THE PLAN ASKS «WHICH IS THE ACTIVE DOCUMENT?» (entry Z-C5).
    ///
    /// ── WHY THE NUMBER IS THE CHECK ───────────────────────────────────────
    ///
    /// `_sw.IActiveDoc2` is a global, and the answer changes by itself: it is enough for
    /// someone to click on another SOLIDWORKS window. **Two readings of the
    /// same global are not one reading done twice: they are two different
    /// questions**, and between the one and the other there is a window in which the answer
    /// can change.
    ///
    /// On 18/9/2026 entry Z-C5 counted them by hand — six, and only one followed
    /// by the comparison — and that count stayed in a text file, where it
    /// ages by itself. Here the run does it, every time, in a tenth of a
    /// second and without SOLIDWORKS.
    ///
    /// **It is not a ban: it is a ceiling.** One more reading is not
    /// necessarily wrong — but it must be a decision, not an oversight,
    /// and this check forces whoever adds it to come through here and
    /// update the number knowing what they are updating.
    /// </summary>
    internal static class DocumentTests
    {
        /// <summary>
        /// How many reads of `_sw.IActiveDoc2` `PlanExecutor.cs` has,
        /// comments left out.
        ///
        /// Two, since finding M19 (lot Z-C21.5; five before, six before 23/9/2026):
        ///
        ///   expectedDocument    taken ONCE at the start: THE GOOD READ
        ///   model              the document a step writes on, compared with
        ///                      expectedDocument right after («still the same?»)
        ///
        /// The three M19 removed: the undo recording was STARTED on a fresh read
        /// (and finished on expectedDocument, two documents whenever the window
        /// changed); the state sent to the plan source was read from the active
        /// document; and the closing of the recording waited for the active
        /// document to be expectedDocument. All three now use the document held.
        /// The one removed on 23/9/2026 was in `PreliminaryChecks`, which now
        /// RECEIVES the document.
        /// </summary>
        private const int Cap = 2;

        internal static void All()
        {
            Group("the document is held by hand (Z-C5)");

            string file = Path.Combine(Root(), "Oce.Solid.Agent.Core", "PlanExecutor.cs");
            if (!File.Exists(file))
            {
                Check("PlanExecutor.cs is there", false, "missing " + file);
                return;
            }

            var lines = File.ReadAllLines(file);
            int howMany = 0;
            var location = new System.Collections.Generic.List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                var r = lines[i];
                // THE COMMENTS TALK ABOUT THESE READINGS, and there are many: counting them
                // would mean that explaining the problem makes it worse.
                var withoutSpaces = r.TrimStart();
                if (withoutSpaces.StartsWith("//", StringComparison.Ordinal) ||
                    withoutSpaces.StartsWith("///", StringComparison.Ordinal)) continue;
                if (r.Contains("_sw.IActiveDoc2")) { howMany++; location.Add((i + 1).ToString()); }
            }

            Check("the reads of the active document have not increased",
                  howMany <= Cap,
                  howMany + " reads (ceiling " + Cap + "), at lines " +
                  string.Join(", ", location));

            // AND THE CEILING MUST NOT BE WIDER THAN THE TRUTH. A ceiling set
            // high «to be on the safe side» is not a ceiling: it is a number that has
            // never said no, and the day it would be needed it will not say it.
            Check("the ceiling is the real one, not a convenient one",
                  howMany == Cap,
                  howMany == Cap
                      ? howMany + ", the ones listed in the comment"
                      : howMany + " left: if that is meant, lower the ceiling");

            // ── AND WHOEVER DECIDES DOES NOT FISH IT OUT AGAIN ──────────
            //
            // The real defect was not the number: it was that `PreliminaryChecks`
            // read the global on its own account and then the plan read it again.
            // They approved two different documents without anything saying so.
            string text = File.ReadAllText(file);
            Check("the preliminary checks RECEIVE the document",
                  Regex.IsMatch(text,
                      @"private string PreliminaryChecks\(\s*IModelDoc2 \w+\s*\)"),
                  "not `PreliminaryChecks()`");

            // And receiving it is not enough if it then fishes it out again all the same: we
            // look at the body of the method, not only the signature.
            int start = text.IndexOf("private string PreliminaryChecks(",
                                       StringComparison.Ordinal);
            string body = start < 0 ? "" : text.Substring(start);
            Check("and inside they do not fetch it again",
                  start >= 0 && !BodyReads(body),
                  "no `_sw.IActiveDoc2` in the body of PreliminaryChecks");

            UndoOnTheDocumentHeld(lines);
        }

        /// <summary>
        /// THE UNDO RECORDING AND THE STATE GO WITH THE DOCUMENT HELD (finding
        /// M19, lot Z-C21.5). On library 0.145.0 the recording was started on
        /// a fresh read of the active document and finished on expectedDocument,
        /// the state sent to the plan source was read from the active document,
        /// and a step that moved the plan to a new document retargeted the
        /// finish without closing the first recording. Checked on the source,
        /// comments left out: no SOLIDWORKS can switch windows in the middle of
        /// a plan on command, so a bench case cannot build it.
        /// </summary>
        private static void UndoOnTheDocumentHeld(string[] lines)
        {
            var code = new System.Text.StringBuilder();
            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                code.Append(line).Append('\n');
            }
            string text = code.ToString();

            int starts = Regex.Matches(text, @"\.\s*StartRecordingUndoObject\s*\(").Count;
            int finishes = Regex.Matches(text, @"\.\s*FinishRecordingUndoObject2?\s*\(").Count;
            string startBody = Body(text, "private static IModelDoc2 StartUndoRecording(");
            // It returns what SOLIDWORKS answered since Z-C21.14 (bool?).
            string finishBody = Body(text, "private bool? FinishUndoRecording(");
            Check("the undo recording is started and finished in one place each",
                  starts == 1 && finishes == 1 &&
                  startBody != null && startBody.Contains("StartRecordingUndoObject") &&
                  finishBody != null && Regex.IsMatch(finishBody, @"FinishRecordingUndoObject2?\s*\("),
                  starts + " start and " + finishes + " finish call(s); StartUndoRecording " +
                  (startBody == null ? "missing" : "found") + ", FinishUndoRecording " +
                  (finishBody == null ? "missing" : "found"));

            // With the two reads of the ceiling above, a state read that is not
            // on the active document can only be on the document held.
            Check("it starts on the document held, and the state is not read from the active one (M19)",
                  text.Contains("undoRecordedOn = StartUndoRecording(expectedDocument)") &&
                  !Regex.IsMatch(text, @"DocumentState\.Read\(\s*_sw\.IActiveDoc2"),
                  "not on a fresh _sw.IActiveDoc2");

            // The change of document: the old recording is closed and a new one
            // opened BEFORE expectedDocument moves.
            int block = text.IndexOf("if (e.NewDocument != null)", StringComparison.Ordinal);
            int move = block < 0 ? -1 : text.IndexOf("expectedDocument = e.NewDocument;", block, StringComparison.Ordinal);
            string between = move < 0 ? "" : text.Substring(block, move - block);
            Check("a step that changes document closes the recording and opens a new one",
                  move > block &&
                  between.Contains("FinishUndoRecording(undoRecordedOn)") &&
                  between.Contains("undoRecordedOn = StartUndoRecording(e.NewDocument)"),
                  move < 0 ? "the NewDocument block or its reassignment is missing"
                           : "FinishUndoRecording, then StartUndoRecording on the new document, before the reassignment");

            // The only finish call sits in FinishUndoRecording (checked above),
            // so these two calls are all the closings there are.
            Check("and the plan closes the recording on the document that holds it",
                  Regex.Matches(text, @"FinishUndoRecording\(undoRecordedOn\)").Count == 2,
                  "FinishUndoRecording(undoRecordedOn) at the change of document and at the end, " +
                  "not a finish on another document behind an active-document test");
        }

        /// <summary>The body of the method whose declaration starts with <paramref name="declaration"/>, braces counted; null if absent.</summary>
        private static string Body(string text, string declaration)
        {
            int start = text.IndexOf(declaration, StringComparison.Ordinal);
            if (start < 0) return null;
            int depth = 0;
            bool open = false;
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == '{') { depth++; open = true; }
                else if (text[i] == '}')
                {
                    depth--;
                    if (open && depth == 0) return text.Substring(start, i - start + 1);
                }
            }
            return null;
        }

        /// <summary>The body of the method, up to the closing brace —
        /// counted, not guessed from an indentation.</summary>
        private static bool BodyReads(string fromHere)
        {
            int braces = 0;
            bool begun = false;
            for (int i = 0; i < fromHere.Length; i++)
            {
                char c = fromHere[i];
                if (c == '{') { braces++; begun = true; }
                else if (c == '}')
                {
                    braces--;
                    if (begun && braces == 0)
                        return fromHere.Substring(0, i).Contains("_sw.IActiveDoc2");
                }
            }
            return fromHere.Contains("_sw.IActiveDoc2");
        }
    }
}
