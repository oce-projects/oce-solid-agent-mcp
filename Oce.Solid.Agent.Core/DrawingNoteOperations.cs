using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// A TEXT NOTE ON A DRAWING (entry E2-bis).
    ///
    /// ── WHY IT EXISTS ───────────────────────────────────────────────────
    ///
    /// A workshop drawing is not made only of geometry and dimensions: it carries the
    /// general notes — «chamfer all sharp edges», «galvanise after
    /// welding», «general tolerances ISO 2768-mK». Until today the bridge
    /// did not know how to write even one.
    ///
    /// ── THE TWO APIs FOR THE SAME THING, AND ONE IS MUTE ────────────────
    ///
    /// Measured on 18/9/2026 out of process, with the positive check green
    /// in the same run (three views created, answer `True`):
    ///
    ///     IModelDoc2.InsertNote      -> __ComObject    annotations 67 -> 68
    ///     IDrawingDoc.InsertNewNote3 -> Void           annotations 68 -> 68
    ///
    /// `InsertNewNote3` returns `Void`: it is a button, not an API — it has no way
    /// to say that it did nothing, and in fact it does not say it. `InsertNote`
    /// is used, which answers. **It is written here so that nobody retries
    /// the other one.** (The TODO called it `InsertNewNote2`: on this
    /// installation the type exposes the `3`, and it is just as mute.)
    ///
    /// ── THE TRAP, AND IT IS IN THE TOOL USED TO LOOK ────────────────────
    ///
    /// `InsertNote` writes **ON THE SHEET**, not inside a view. But
    /// `GetNoteCount()` lives on `IView`, and the real views of a drawing
    /// start AFTER the first one — because the first «view» **is the sheet**.
    ///
    /// Whoever counted only the real views would read `0 -> 0` with the note
    /// regularly on the sheet, and would write «notes are not born either:
    /// it is a systemic block». That false negative would have closed by
    /// mistake four entries of this TODO (surface finish, datum, welds,
    /// GD&T), all unblocked precisely by the fact that a note is born.
    ///
    /// Here both are counted, and both come out in the answer:
    /// `sheet_notes_*` and `view_notes_*`. *Two numbers, two different things, and
    /// adding them would make an empty drawing look full.*
    ///
    /// ── WHERE IT ENDS UP, AND WHY IT IS CHECKED FIRST ───────────────────
    ///
    /// The coordinates are those of the sheet, in metres. A note put off the
    /// sheet **exists, is counted, and is not on the paper**: the same defect
    /// as dimensions off the sheet, and it gives no error. So the
    /// size of the sheet is read from `GetProperties2` and it is refused before writing.
    ///
    /// ── WHAT THIS OPERATION CANNOT KNOW ─────────────────────────────────
    ///
    /// A note is free text. «Material S355» on a part whose
    /// properties say S275 passes every count and every reread: the text
    /// comes back identical to what was asked for, and it is wrong all the same. Here
    /// it is guaranteed that the note **is there and says what you wrote** — not that
    /// what you wrote is true.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome DrawingNote(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "The active document is not a drawing: a drawing note goes on a " +
                    "sheet of a drawing. First create the drawing with «create_drawing».");

            string text = Str(a, "text", null);
            if (string.IsNullOrWhiteSpace(text))
                return Failed(
                    "«text» is required: an empty note is an annotation that takes " +
                    "up a place on the sheet and says nothing.");

            // ── THE SHEET, AND HOW BIG IT IS ────────────────────────────
            ISheet sheet = null;
            try { sheet = drawing.GetCurrentSheet() as ISheet; } catch { }
            if (sheet == null)
                return Failed("I cannot get the active sheet of the drawing.");

            string sheetName = "";
            try { sheetName = sheet.GetName() ?? ""; } catch { }

            double[] measurements = SheetMeasurements(sheet);
            if (measurements == null || measurements.Length < 2)
                return Failed(
                    "I cannot read the size of the sheet, and without it I cannot say " +
                    "whether the note would end up inside or outside — and a note " +
                    "outside the sheet exists, is counted, and on paper is not there.");

            double wideMm = measurements[0], topMm = measurements[1];

            // By default at the top left, inside the margin: it is where the general
            // notes sit on a mechanical drawing. Not in the centre, which is where
            // the drawing sits.
            double xMm = Num(a, "x_mm", 20);
            double yMm = Num(a, "y_mm", topMm - 20);

            if (xMm < 0 || yMm < 0 || xMm > wideMm || yMm > topMm)
                return Failed(
                    "The note would end up outside the sheet: you asked for x=" + xMm +
                    " y=" + yMm + " mm on a sheet " + wideMm + " x " + topMm +
                    " mm. Outside the sheet the note really exists and is counted, but " +
                    "when printing it does not appear — and no error would say so.");

            int sheetBefore = CountSheetNotes(drawing);
            int viewsBefore = CountViewNotes(drawing);
            if (sheetBefore < 0 || viewsBefore < 0)
                return Failed(
                    "I cannot count the notes of the drawing before starting: without that number " +
                    "I could not tell you whether the note was created. I touched nothing.");

            object born = null;
            try
            {
                model.ClearSelection2(true);
                born = model.InsertNote(text);
            }
            catch (Exception ex)
            {
                return Failed("«InsertNote» went into error: " + ex.Message);
            }

            // ── WHERE TO PUT IT ─────────────────────────────────────────
            //
            // First it is positioned, then it is read back: the position is asked of
            // the object AFTER setting it, because it is the only way to
            // know whether it was accepted.
            var note = born as INote;
            bool moved = false;
            try
            {
                if (note != null)
                {
                    note.SetTextPoint(xMm / 1000.0, yMm / 1000.0, 0);
                    moved = true;
                }
            }
            catch { }

            try { model.EditRebuild3(); } catch { }
            try { model.ClearSelection2(true); } catch { }

            int sheetAfter = CountSheetNotes(drawing);
            int viewsAfter = CountViewNotes(drawing);
            if (sheetAfter < 0 || viewsAfter < 0)
                return Failed(
                    "I wrote the note, but then I cannot count the notes of the drawing again: I cannot " +
                    "tell you whether it was created nor where. Look at the drawing before going on.");
            int put = (sheetAfter - sheetBefore) + (viewsAfter - viewsBefore);

            // WE DO NOT LOOK AT WHETHER `InsertNote` ANSWERED: we look at whether the note
            // is there. It is the house rule — the value that comes back from whoever did
            // the thing is not the measurement of the thing.
            if (put <= 0)
                return Failed(
                    "No note was created (on the sheet there were " + sheetBefore + ", there are " +
                    sheetAfter + "; in the views there were " + viewsBefore + ", there are " +
                    viewsAfter + "). `InsertNote` " +
                    (born == null ? "did not answer an object" : "answered an object") +
                    ", and the count did not go up all the same.");

            // ── THE TEXT IS READ BACK ───────────────────────────────────
            string reread = null;
            try { if (note != null) reread = note.GetText(); } catch { }

            double[] whereMm = WhereItEnded(note);

            var outside = new Dictionary<string, object>
            {
                ["sheet"] = sheetName,
                ["sheet_mm"] = new object[] { wideMm, topMm },
                // TWO COUNTS, NEVER ADDED INTO ONE: see the docstring.
                ["notes_sheet_before"] = sheetBefore,
                ["notes_sheet_after"] = sheetAfter,
                ["notes_views_before"] = viewsBefore,
                ["notes_views_after"] = viewsAfter,
                ["notes_placed"] = put,
                // THE TEXT READ BACK FROM SOLIDWORKS, not what I thought I had
                // asked for: a note that appears with a different text reads
                // as a right note until somebody compares it.
                ["text_requested"] = text,
                ["text_reread"] = reread,
                ["text_matches"] = reread != null &&
                                     string.Equals(reread.Trim(), text.Trim(), StringComparison.Ordinal),
                ["where_mm"] = whereMm,
                ["position_set"] = moved,
                ["what_it_does_not_check"] =
                    "That what is written is TRUE. A note is free text: «material S355» " +
                    "on a part that says S275 in its properties passes every count and " +
                    "every re-reading. What is guaranteed here is that the note is there and " +
                    "says what you wrote, not that it is right to write it. And I do " +
                    "not look at whether it overlaps the drawing or the title block.",
            };

            _log("[agent] note on the sheet «" + sheetName + "»: " +
                 (reread ?? text) + (whereMm == null ? "" : " a " + whereMm[0] + "," + whereMm[1] + " mm"));
            return WithoutFeature(outside);
        }

        /// <summary>
        /// Where the note really ended up, in sheet millimetres, or
        /// `null` if it could not be read.
        ///
        /// THREE ROUTES, BECAUSE THE FIRST DOES NOT ANSWER. Measured on 22/9/2026
        /// on the real drawing: the note is born, the text is read back, and
        /// `GetTextPoint2()` returns an object whose `ArrayData` is null —
        /// position read `null` with the note in its place. A false negative
        /// that, without the other two routes, would have made us write «the
        /// position cannot be reread on this version» as if it were a
        /// property of SOLIDWORKS instead of of the first call tried.
        ///
        /// `null` here means «could not read it», NEVER «zero»: a zero
        /// position is the corner of the sheet, and it is a real place.
        /// </summary>
        private static double[] WhereItEnded(INote note)
        {
            if (note == null) return null;

            // 1. The direct array, which is the shortest route.
            try
            {
                var arr = note.GetTextPoint() as double[];
                if (arr != null && arr.Length >= 2) return InMm(arr);
            }
            catch { }

            // 2. The math point, and its `ArrayData`.
            try
            {
                var point = note.GetTextPoint2() as IMathPoint;
                var arr = point == null ? null : point.ArrayData as double[];
                if (arr != null && arr.Length >= 2) return InMm(arr);
            }
            catch { }

            // 3. The annotation under the note: `IAnnotation.GetPosition()`.
            try
            {
                var ann = note.GetAnnotation() as IAnnotation;
                var arr = ann == null ? null : ann.GetPosition() as double[];
                if (arr != null && arr.Length >= 2) return InMm(arr);
            }
            catch { }

            return null;
        }

        private static double[] InMm(double[] meters)
        {
            return new[]
            {
                Math.Round(meters[0] * 1000, 2),
                Math.Round(meters[1] * 1000, 2),
            };
        }

        /// <summary>
        /// The notes of the SHEET. The first «view» of a drawing is the sheet, and
        /// it is there that `InsertNote` writes: counting from `GetNextView()` onwards —
        /// that is the real views — would answer zero with the note in its place.
        ///
        /// **-1 when it could not be read** (entry Z-C8). With zero, a
        /// failed read AFTER gave the refusal «no note was born» with the
        /// note in its place. A drawing always has the sheet: if it is not there, it
        /// is not a zero, it is a read that went badly.
        /// </summary>
        private int CountSheetNotes(IDrawingDoc drawing)
        {
            try
            {
                var sheet = drawing.GetFirstView() as IView;
                if (sheet == null) return -1;
                return sheet.GetNoteCount();
            }
            catch { return -1; }
        }

        /// <summary>
        /// The notes of the real views, sheet excluded. It serves as a COUNTERPART:
        /// if this one went up instead of the sheet's, the note would have
        /// ended up inside a view and would move together with it.
        ///
        /// -1 when even a single view does not answer: the sum of the others
        /// would be a number lower than the true one, stated as if it were true.
        /// </summary>
        private int CountViewNotes(IDrawingDoc drawing)
        {
            int n = 0;
            try
            {
                var sheet = drawing.GetFirstView() as IView;
                if (sheet == null) return -1;
                var v = sheet.GetNextView() as IView;
                while (v != null)
                {
                    n += v.GetNoteCount();
                    v = v.GetNextView() as IView;
                }
            }
            catch { return -1; }
            return n;
        }
    }
}
