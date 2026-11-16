using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// A SECTION VIEW ON A DRAWING (entry GT4).
    ///
    /// ── WHY IT EXISTS ───────────────────────────────────────────────────
    ///
    /// It is the first thing missing for a real mechanical drawing. A part with
    /// an internal cavity, without a section, **cannot be read in the workshop**: the
    /// hidden lines say that there is a void, not what shape it has.
    ///
    /// ── THE API TAKES NO ENTITY IN ITS SIGNATURE, AND THAT CHANGES EVERYTHING ──
    ///
    ///     IDrawingDoc :: CreateSectionViewAt5(Double X, Double Y, Double Z,
    ///                        String SectionLabel, Int32 Options,
    ///                        Object ExcludedComponents, Double SectionDepth)
    ///
    /// There is no argument to say WHERE to cut. The cut line must be
    /// **drawn on the sheet and left selected**: the API takes that one.
    /// This means the operation has five steps that must all
    /// succeed, and each can fail silently.
    ///
    /// Measured on 14/9/2026 (entry 0.6, tool `Checks\SectionView`):
    ///
    ///     line selected: True, objects: 1, type 10 (swSelSKETCHSEGS)
    ///     views on the sheet: 4 -> 5
    ///       -> THE SECTION IS THERE. Last view: «Section View A-A»
    ///
    /// ── THREE THINGS THAT CANNOT BE DEDUCED, AND THEY COST US ───────────
    ///
    /// 1. **The line must cross the view from side to side.** One that just
    ///    grazes it gives an **empty section without saying anything**.
    /// 2. **The sketch is closed first** (`InsertSketch(true)`). With an
    ///    open sketch SOLIDWORKS is inside a command, and the APIs that create
    ///    views behave differently.
    /// 3. **The sketch of the sheet is not `GetTemplateSketch()`** — that one is the
    ///    title block. «Template» there means *format*. The line ends up in the
    ///    sketch of the first «view», which IS the sheet.
    /// 4. **But the sheet sketch is the wrong place** (report 8cd843 N48, round 13):
    ///    on a sheet with two views a line drawn there across the view above gave a
    ///    section of the view below. Since library 0.174.4 the view is activated and
    ///    the line drawn in its own sketch, whose origin is the centre of the view
    ///    and whose unit is the model (SheetToViewSketch, measured).
    ///
    /// ── THE HATCHING IS THE CHECK THAT COUNTS ───────────────────────────
    ///
    /// The real risk of this operation is not that it fails: it is that it
    /// **succeeds in the wrong place**. A cut line that fell outside the
    /// material produces a formally valid view, that passes every
    /// count, and shows nothing. It is the same kind as the projection angle: no geometric
    /// measurement reveals it.
    ///
    /// So afterwards the hatching is counted, and **if it is zero the operation
    /// fails** instead of delivering an empty view that looks successful.
    /// *A section without hatching is a cut line that fell outside the
    /// part.*
    ///
    /// **AND THE COUNTER IS NOT THE ONE THE ENTRY PRESCRIBED.** GT4 said
    /// `GetSolidHatchCount() > 0`; measured, that number is **zero on a
    /// real section**, before and after the rebuild. `GetFaceHatchCount()` is
    /// read, which on a good section is 1. The other one is reported all the
    /// same in the answer, so that it serves as a warning to whoever
    /// retraced the same route.
    ///
    /// ── AND THE COUNT OF THE VIEWS, NOT THE RETURN VALUE ────────────────
    ///
    /// `CreateSectionViewAt5` may answer an object without having done
    /// anything. The views are counted before and after: that is the cross-check, the
    /// return value is a promise.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome SectionView(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "The active document is not a drawing: a section view goes on a " +
                    "sheet of a drawing. First create the drawing with «create_drawing».");

            // ── THE VIEW TO SECTION ─────────────────────────────────────
            //
            // The FIRST «view» of the sheet is the sheet itself (type 1), and it is
            // skipped. Whoever took it for a view would section the sheet.
            string requested = ViewRequestedByName(drawing, Str(a, "view", null));
            IView target = null;
            var viewNames = new List<string>();
            // The views of the model only, as «inspect_part» and «model_view» count
            // them: until library 0.174.1 the sheet was counted too, and the same
            // drawing read 5 views here and 4 there (report 8cd843 N42).
            int viewsBefore = 0, walked = 0;
            try
            {
                var v = drawing.GetFirstView() as IView;
                while (v != null && walked < 200)
                {
                    walked++;
                    if (v.Type != (int)swDrawingViewTypes_e.swDrawingSheet)
                    {
                        viewsBefore++;
                        string itemName = "";
                        try { itemName = v.GetName2() ?? ""; } catch { }
                        viewNames.Add(itemName);
                        if (target == null &&
                            (requested == null ||
                             string.Equals(itemName, requested, StringComparison.OrdinalIgnoreCase)))
                            target = v;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch (Exception ex)
            {
                return Failed("I cannot go through the views of the sheet: " + ex.Message);
            }

            if (target == null)
                return Failed(requested == null
                    ? "There is no view to section on the sheet: first put the views " +
                      "there with «standard_views»."
                    : "I cannot find the view «" + requested + "». On the sheet there are: " +
                      (viewNames.Count == 0 ? "no view" : string.Join(", ", viewNames)) + ".");

            string targetName = "";
            try { targetName = target.GetName2() ?? ""; } catch { }

            var outline = target.GetOutline() as double[];
            if (outline == null || outline.Length < 4)
                return Failed(
                    "I cannot read the bounding box of the view «" + targetName +
                    "», and without it I do not know where to draw the cutting line: a " +
                    "line that grazes the view gives an empty section without saying anything.");

            // ── WHERE TO CUT ────────────────────────────────────────────
            //
            // By default: in the middle of the view. `offset_mm` moves the line, in
            // SHEET millimetres — not of the model: on the sheet the view is
            // scaled, and mixing the two measures is the quickest way to
            // cut where one did not want to.
            string facing = (Str(a, "orientation", "vertical") ?? "vertical").Trim().ToLowerInvariant();
            if (facing != "vertical" && facing != "horizontal")
                return Failed(
                    "«orientation» says how the cutting line runs and is «vertical» or " +
                    "«horizontal»: I read «" + facing + "».");

            double offsetM = Num(a, "offset_mm", 0) / 1000.0;
            string label = Str(a, "label", "A");
            if (string.IsNullOrWhiteSpace(label)) label = "A";
            double depthM = Num(a, "depth_mm", 0) / 1000.0;
            // THE SCALE OF THE SECTION (report 8cd843, round 22, point 4): the section took the
            // scale of its view, 1:2 on 26M000002, where the owner wanted A-A at 2:1. Read before
            // anything is drawn (ViewScaleOperations.cs).
            double[] sectionRatio = null;
            if (a.ContainsKey("scale"))
            {
                string scaleWhy;
                sectionRatio = ParseScale(Str(a, "scale", null), out scaleWhy);
                if (sectionRatio == null)
                    return Failed("The scale «" + Str(a, "scale", "") + "» cannot be used: " + scaleWhy +
                                  ". Give it as «2:1» or «1:2», or leave «scale» out for the scale of the view. I touched nothing.");
            }
            // TWO «A-A» ON THE SAME DRAWING (entry Z-C13): it is refused first.
            string doubled = LetterAlreadyUsed(drawing, label);
            if (doubled != null) return Failed(doubled);

            double xMin = outline[0], yMin = outline[1], xMax = outline[2], yMax = outline[3];
            double wide = xMax - xMin, up = yMax - yMin;

            // WE GO OUT BY A TENTH BEYOND THE EDGES. A line that ends
            // exactly on the edge is a free edge case, and the
            // edge case of this API is an empty section that does not say so.
            double x1, y1, x2, y2;
            if (facing == "vertical")
            {
                double x = (xMin + xMax) / 2.0 + offsetM;
                if (x <= xMin || x >= xMax)
                    return Failed(
                        "With «offset_mm» = " + Num(a, "offset_mm", 0) + " the cutting line falls " +
                        "OUTSIDE the view, and a section cut outside the material " +
                        "comes out empty without any error. The view is " +
                        Math.Round(wide * 1000, 1) + " mm wide on the sheet, so «offset_mm» is " +
                        "between " + Math.Round(-wide * 500, 1) + " and " +
                        Math.Round(wide * 500, 1) + ".");
                x1 = x; y1 = yMin - up * 0.1;
                x2 = x; y2 = yMax + up * 0.1;
            }
            else
            {
                double y = (yMin + yMax) / 2.0 + offsetM;
                if (y <= yMin || y >= yMax)
                    return Failed(
                        "With «offset_mm» = " + Num(a, "offset_mm", 0) + " the cutting line falls " +
                        "OUTSIDE the view, and a section cut outside the material " +
                        "comes out empty without any error. The view is " +
                        Math.Round(up * 1000, 1) + " mm tall on the sheet, so «offset_mm» is " +
                        "between " + Math.Round(-up * 500, 1) + " and " +
                        Math.Round(up * 500, 1) + ".");
                x1 = xMin - wide * 0.1; y1 = y;
                x2 = xMax + wide * 0.1; y2 = y;
            }

            // ── THE LINE, ON THE SKETCH OF THE VIEW ─────────────────────
            //
            // Not on the sketch of the sheet (report 8cd843 N48, round 13): there the
            // line of A-A, wanted across the view above, was given to the view below,
            // and the section came out of that view, at its left end (measured on a
            // copy: base «the view below», 1 face hatch, so «the hatching is there»
            // held for the wrong view). Now as the circle of the detail: the view is
            // activated and the line drawn in the coordinates of its sketch
            // (SheetToViewSketch); measured, the same line gave a section of the view
            // above with 2 face hatches. «cut_line_mm» stays in sheet millimetres.
            double[] xform = null;
            try { xform = target.GetXform() as double[]; } catch { }
            double angle = 0;
            try { angle = target.Angle; } catch { }
            bool inView = false;
            try { inView = drawing.ActivateView(targetName); } catch { }
            string noSketch = ViewSketchProblem(inView, xform, angle, targetName, "cutting line");
            if (noSketch != null)
            {
                try { drawing.EditSheet(); } catch { }
                return Failed(noSketch);
            }
            var from = SheetToViewSketch(xform, x1, y1);
            var to = SheetToViewSketch(xform, x2, y2);

            // The segments of the sketch of the view before the line, so that a line
            // left behind by a refusal can be removed and the removal checked
            // (Z-C21.8, finding M41).
            int segmentsBefore = ViewSketchSegmentCount(target);
            Func<ISketchSegment, bool> removeLine = s => RemoveViewSketchSegment(model, target, s, segmentsBefore);
            ISketchSegment line = null;
            // WITH NO INFERENCE, as the circle of the detail (report 8cd843 N52, round 14:
            // the circles snapped onto an edge 1.1 mm away; see DetailView): a line whose
            // end snaps onto a vertex cuts somewhere else.
            bool addBefore = false;
            try
            {
                model.ClearSelection2(true);
                addBefore = model.SketchManager.AddToDB;
                model.SketchManager.AddToDB = true;
                line = model.SketchManager.CreateLine(from[0], from[1], 0.0, to[0], to[1], 0.0) as ISketchSegment;
            }
            catch (Exception ex)
            {
                try { drawing.EditSheet(); } catch { }
                return Failed("I could not draw the cutting line: " + ex.Message);
            }
            finally { try { model.SketchManager.AddToDB = addBefore; } catch { } }
            if (line == null) { try { drawing.EditSheet(); } catch { } }
            if (line == null)
                return Failed(
                    "«CreateLine» produced no segment on the sheet, and without the line " +
                    "there is nothing to select: «CreateSectionViewAt5» does not take " +
                    "the cut as a signature, it takes it from the selection.");
            // The line read back (N52).
            string lineMoved = LandedElsewhere("cutting line", new[] { from[0], from[1], to[0], to[1] }, LineInSketch(line), 1e-6);
            if (lineMoved != null)
            {
                string lineGone = SheetLineRemovedOrNot(removeLine(line), "cutting line");
                try { drawing.EditSheet(); } catch { }
                return Failed(lineMoved + lineGone);
            }

            // THE SKETCH IS CLOSED FIRST. See the docstring: with an open sketch
            // the APIs that create views behave differently.
            try { model.SketchManager.InsertSketch(true); } catch { }

            bool taken = false;
            string selectError = null;
            try
            {
                model.ClearSelection2(true);
                taken = line.Select4(false, null);
            }
            catch (Exception ex) { selectError = ex.Message; }
            if (!taken)
            {
                string lineGone = SheetLineRemovedOrNot(removeLine(line), "cutting line");
                try { drawing.EditSheet(); } catch { }
                return Failed(
                    (selectError != null
                        ? "I could not select the cutting line (" + selectError + ")"
                        : "The cutting line is there but does not let itself be selected") +
                    ", and CreateSectionViewAt5 takes the cut from the selection: without it, it would do " +
                    "something other than what you asked" + lineGone);
            }

            // ── THE SECTION, ALONGSIDE ──────────────────────────────────
            double xSection, ySection;
            if (facing == "vertical")
            {
                xSection = xMax + wide * 0.8;
                ySection = (yMin + yMax) / 2.0;
            }
            else
            {
                xSection = (xMin + xMax) / 2.0;
                ySection = yMin - up * 0.8;
            }

            object born = null;
            string exploded = null;
            try
            {
                born = drawing.CreateSectionViewAt5(
                    xSection, ySection, 0.0, label, 0, null, depthM);
            }
            catch (Exception ex) { exploded = ex.Message; }
            // Back to the sheet (report 8cd843 N51, round 13): a note given in the same
            // plan after the section went into the view activated for the line.
            try { drawing.EditSheet(); } catch { }

            // ── THE CROSS-CHECK: HOW MANY VIEWS THERE ARE NOW ───────────
            int viewsAfter = 0, walkedAfter = 0;
            IView last = null;
            try
            {
                var w = drawing.GetFirstView() as IView;
                while (w != null && walkedAfter < 200)
                {
                    walkedAfter++;
                    if (w.Type != (int)swDrawingViewTypes_e.swDrawingSheet) viewsAfter++;
                    last = w;
                    w = w.GetNextView() as IView;
                }
            }
            catch { }

            if (viewsAfter <= viewsBefore)
                return Failed(
                    "No view more: the section was NOT born (there were " + viewsBefore +
                    ", there are " + viewsAfter + "). CreateSectionViewAt5 " +
                    (exploded != null ? "failed: " + exploded
                                     : born == null ? "returned no object"
                                                    : "returned an object") +
                    SheetLineRemovedOrNot(removeLine(line), "cutting line"));

            // THE SECTION IS THE VIEW CreateSectionViewAt5 RETURNS (Z-C21.8, finding
            // M41). CreateSectionViewAt5.html, Return Value: «Section view». Until
            // library 0.144.0 the bridge read the LAST view of the chain instead,
            // which is the new one only as long as SOLIDWORKS appends it there. The
            // count of views stays the check that something was born; the last
            // view is only the fallback when the call returned no view, and the
            // answer says which one was read.
            IView sectionView = born as IView;
            string viewReadFrom = "returned by CreateSectionViewAt5";
            if (sectionView == null)
            {
                sectionView = last;
                viewReadFrom = "last view of the sheet (CreateSectionViewAt5 returned no view)";
            }

            string sectionName = "";
            int sectionType = -1;
            int hatching = -1;
            try { sectionName = sectionView.GetName2() ?? ""; } catch { }
            try { sectionType = sectionView.Type; } catch { }
            // ── THE RIGHT COUNTER IS `GetFaceHatchCount` ────────────────
            //
            // Entry GT4 prescribed `GetSolidHatchCount() > 0`, and the
            // criterion was right: a section without hatching is a cut
            // that fell outside the material. **The counter was not.**
            //
            // Measured on 23/9/2026 on a section that crosses a solid
            // 120x80x20 plate, with the probe `Checks\SectionView`:
            //
            //     hatching BEFORE the rebuild: return=0  out=0
            //     hatching AFTER  the rebuild: return=0  out=0
            //     FACE hatching:               1
            //     view type: 2 (swDrawingSectionView), GetSection(): an object
            //
            // `GetSolidHatchCount` answers **zero on a real section**,
            // before and after the rebuild. `GetFaceHatchCount` is read.
            //
            // HOW IT WAS DISCOVERED, and it is worth keeping: the operation
            // refused at the first real use, in the first bench case written for
            // it. The guard was right to exist and wrong about the number, and
            // the two things are told apart only by measuring. *A right criterion
            // resting on the wrong counter is a guard that always
            // says no, and it is indistinguishable from one that works until
            // it is used.*
            try { hatching = sectionView.GetFaceHatchCount(); } catch { }

            // The other counter, GetSolidHatchCount, counts the areas filled solid and is zero on
            // a good section (measured, Checks/SectionView). It was reported beside `hatches`
            // until 0.174.6 so that nobody tried it again; round 15 of report 8cd843 read that
            // 0 as «no hatching» next to a PDF with the hatching, so it stays here and in
            // Checks/SectionView, not in the answer.

            // ── THE SECTION OF THE VIEW ASKED (N48) ──────────────────────
            //
            // The hatching alone was not the proof: in round 13 A-A had its hatch from
            // the view below. The section must come from the view asked (GetBaseView,
            // measured: the view below with the line on the sheet sketch, the view asked
            // with the line in its own sketch).
            string sectionBase = BaseViewName(sectionView);
            string notAsked = NotTheViewAsked("section", targetName, sectionBase, -1);
            if (notAsked != null)
            {
                bool viewIsNew = !viewNames.Contains(sectionName);
                bool viewRemoved = viewIsNew && RemoveNewView(model, sectionName);
                bool lineRemoved = removeLine(line);
                return Failed(notAsked + " " +
                    (viewRemoved ? "I removed the view «" + sectionName + "»"
                                 : "I could not remove the view «" + sectionName + "» (remove it — " + Sentences.HowToWithdrawIt + ")") +
                    SheetLineRemovedOrNot(lineRemoved, "cutting line"));
            }

            // ── THE HATCHING, AND HERE IT FAILS ─────────────────────────
            //
            // See the docstring. A section view without hatching is a
            // cut line that fell outside the material: formally valid,
            // counts as a view, and shows nothing.
            // THE EMPTY VIEW IS REMOVED (review of 26/9/2026): if it stayed, its
            // letter «A» made the remedy the message itself indicates be refused.
            // Only a view that was not on the sheet before is deleted: with no
            // view returned, the name comes from the last view of the chain, and
            // a name read before the call is a view of the user (Z-C21.8, M41).
            bool emptyOneRemoved = hatching == 0 && !viewNames.Contains(sectionName) && RemoveNewView(model, sectionName);
            if (hatching == 0)
                return Failed(
                    (emptyOneRemoved ? "I removed the view «" + sectionName + "»: " : "") +
                    "The view «" + sectionName + "» was created but HAS NO " +
                    "HATCHING (face hatches: 0), " +
                    "and a section without hatching is a cutting line that fell " +
                    "outside the material: on the sheet there is a view that looks " +
                    "good and shows nothing. I drew the line " +
                    (facing == "vertical" ? "vertically at x=" : "horizontally at y=") +
                    Math.Round((facing == "vertical" ? x1 : y1) * 1000, 2) + " mm of sheet. " +
                    "Move the cut with «offset_mm».");

            // ── THE SCALE ASKED, before the free place: it changes the size ─────
            // A section follows its view (IView~UseParentScale.html); it is set free first,
            // then given the ratio and read back after the rebuild (ApplyViewScale).
            if (sectionRatio != null)
            {
                try { sectionView.UseParentScale = false; } catch { }
                string sectionScaleProblem = ApplyViewScale(model, sectionView, sectionRatio);
                if (sectionScaleProblem != null)
                {
                    bool viewIsNew = !viewNames.Contains(sectionName);
                    bool viewRemoved = viewIsNew && RemoveNewView(model, sectionName);
                    bool lineRemoved = removeLine(line);
                    return Failed("I asked the section «" + sectionName + "» for " + ScaleText(sectionRatio) + " and " +
                        sectionScaleProblem + ". " +
                        (viewRemoved ? "I removed the view «" + sectionName + "»"
                                     : "I could not remove the view «" + sectionName + "» (remove it — " + Sentences.HowToWithdrawIt + ")") +
                        SheetLineRemovedOrNot(lineRemoved, "cutting line"));
                }
            }
            double[] sectionRatioRead = null;
            try { sectionRatioRead = sectionView.ScaleRatio as double[]; } catch { }

            // ── THE SECTION IN A FREE PLACE (report 8cd843 N53, round 14) ──────
            //
            // CreateSectionViewAt5 puts it beside the view, where the side view of a
            // standard layout already is: A-A was born half on V3, in both drawings of
            // round 14. As the details (N43), with one difference measured on a copy
            // (27/10/2026): a section is aligned to its view, and Position moves it only
            // along that line (asked (0, +30) mm it stayed, (+30, 0) mm it went), and it
            // is already built at birth (three round trips of 1 mm left its outline as it
            // was). So it slides along its line to the nearest free place.
            string sectionWhere, sectionPlaceProblem;
            if (!SectionInAFreePlace(drawing, sectionView, facing, out sectionWhere, out sectionPlaceProblem))
            {
                bool viewIsNew = !viewNames.Contains(sectionName);
                bool viewRemoved = viewIsNew && RemoveNewView(model, sectionName);
                bool lineRemoved = removeLine(line);
                return Failed(sectionPlaceProblem + " " +
                    (viewRemoved ? "I removed the view «" + sectionName + "»"
                                 : "I could not remove the view «" + sectionName + "» (remove it — " + Sentences.HowToWithdrawIt + ")") +
                    SheetLineRemovedOrNot(lineRemoved, "cutting line"));
            }

            // THE ACTUAL LETTER, read back from the view (Z-C13).
            string readLetter = ViewLetter(sectionView);
            var outside = new Dictionary<string, object>
            {
                ["label_reread"] = readLetter,
                ["view_read_from"] = viewReadFrom,
                ["view_sectioned"] = targetName,
                ["views_before"] = viewsBefore,
                ["views_after"] = viewsAfter,
                ["view_section"] = sectionName,
                ["view_kind"] = sectionType,
                // THE RAW NUMBER IS NOT LEFT ALONE: whoever reads does not have the
                // table of `swDrawingViewTypes_e` at hand.
                ["is_section"] = sectionType == (int)swDrawingViewTypes_e.swDrawingSectionView,
                ["label"] = label,
                ["facing"] = facing,
                ["cut_line_mm"] = new object[]
                {
                    Math.Round(x1 * 1000, 2), Math.Round(y1 * 1000, 2),
                    Math.Round(x2 * 1000, 2), Math.Round(y2 * 1000, 2),
                },
                // `hatches` is the count of face hatchings, which is what says whether
                // the cut met material (the solid fills, zero on a section, are left
                // out: see above).
                ["hatches"] = hatching,
                // What the section was made from (N48), and where the line went in the
                // sketch of that view (its own coordinates, see SheetToViewSketch).
                ["base_view"] = sectionBase ?? "not readable",
                ["cut_line_in_the_view_sketch_mm"] = new object[]
                {
                    Math.Round(from[0] * 1000, 2), Math.Round(from[1] * 1000, 2),
                    Math.Round(to[0] * 1000, 2), Math.Round(to[1] * 1000, 2),
                },
                ["depth_mm"] = Math.Round(depthM * 1000, 2),
                // The scale read back from the section (round 22, point 4), and the one asked.
                ["scale"] = ScaleText(sectionRatioRead),
                ["scale_asked"] = ScaleText(sectionRatio),
                // Where the section went (N53): beside the view or slid along its line.
                ["placed"] = sectionWhere,
                ["what_it_does_not_check"] =
                    "IF THAT IS THE SECTION THAT WAS NEEDED. I can tell you that the cut " +
                    "met material of the view asked (the hatching is there, and the section " +
                    "was made from that view), not that it passes through the " +
                    "cavity you wanted to show: a section cut ten millimeters further on is " +
                    "just as valid and does not show the hole. Choose «offset_mm» by " +
                    "looking at the part, not at random.\nThe section view is put where it " +
                    "touches no other view, table, note or the title block, sliding along the " +
                    "line that aligns it to its view (it cannot leave that line); I do " +
                    "not look at the arrows and the label on the starting view: SOLIDWORKS " +
                    "puts them, I do not read them again. And I do not decide whether " +
                    "a section is needed: that depends on what the part has inside.",
            };

            _log("[agent] section «" + sectionName + "» from «" + targetName + "»: " +
                 hatching + " hatches, views " + viewsBefore + " -> " + viewsAfter);
            return WithoutFeature(outside);
        }

        /// <summary>
        /// The section where nothing else is (report 8cd843 N53, round 14): kept where it was
        /// born when that place, with its label, touches nothing; otherwise slid along the line
        /// that aligns it to its view (horizontally for a vertical cut, vertically for a
        /// horizontal one: measured, Position does not move it off that line) to the nearest
        /// free place, then judged again where it landed. False with the reason.
        /// </summary>
        private static bool SectionInAFreePlace(IDrawingDoc drawing, IView view, string facing, out string location, out string reason)
        {
            location = null; reason = null;
            double[] o = ViewWithItsNotes(view), p = null, outline = null;
            try { p = view.Position as double[]; outline = view.GetOutline() as double[]; } catch { }
            if (o == null || p == null || p.Length < 2 || outline == null || outline.Length < 4)
            {
                reason = "The section view was born but I cannot read where it is on the sheet, so I cannot tell " +
                         "whether it falls on another view.";
                return false;
            }
            var sheet = ReadSheet(drawing, view);
            string touches = sheet.Touches(o[0], o[1], o[2], o[3]);
            if (touches == null)
            {
                location = "beside the view, where it was born";
                return true;
            }
            bool horizontal = facing == "vertical";
            var shift = NearestFreeAlong(o, horizontal, sheet.BoundingBoxes, sheet.Width, sheet.Height);
            if (shift == null)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "The section view with its label ({0:0} x {1:0} mm on the sheet) {2}, and along the line that aligns it " +
                    "to its view ({3}) there is no free place outside the other views, the tables, the notes and the title " +
                    "block. Make room first («move_view» or «delete_view»), or use a bigger sheet («set_sheet»).",
                    (o[2] - o[0]) * 1000, (o[3] - o[1]) * 1000, touches, horizontal ? "a horizontal line" : "a vertical line");
                return false;
            }
            try { view.Position = new[] { p[0] + shift[0], p[1] + shift[1] }; } catch { }
            double[] moved = null;
            try { moved = view.GetOutline() as double[]; } catch { }
            var o2 = LandedWithTheOutline(o, outline, moved);
            string still = o2 == null ? "cannot be read back" : sheet.Touches(o2[0], o2[1], o2[2], o2[3]);
            if (still != null)
            {
                reason = "I slid the section view by " + (shift[0] + shift[1] >= 0 ? "+" : "") +
                         ((horizontal ? shift[0] : shift[1]) * 1000).ToString("0.#", CultureInfo.InvariantCulture) +
                         " mm along its line but it " + still + " (with its label before " + MmRect(o) + ", after " + MmRect(o2) +
                         "; outline before " + MmRect(outline) + ", after " + MmRect(moved) + ").";
                return false;
            }
            location = string.Format(CultureInfo.InvariantCulture, "slid {0:+0.#;-0.#} mm {1} along the line of its view, where nothing else is",
                                     (horizontal ? shift[0] : shift[1]) * 1000, horizontal ? "horizontally" : "vertically");
            return true;
        }

        /// <summary>
        /// The nearest shift of a rectangle (x0, y0, x1, y1 in metres) along one axis that leaves it
        /// inside the frame and clear of every obstacle by the clearance of FreeSheet (N53): 1 mm
        /// steps, the positive side first at equal distance. {dx, dy}, the other one zero; null
        /// when no shift within the sheet is free. Pure, so the contract suite reads it.
        /// </summary>
        internal static double[] NearestFreeAlong(double[] rect, bool horizontal, List<double[]> obstacles, double width, double height)
        {
            if (rect == null || rect.Length < 4) return null;
            var f = new FreeSheet { Width = width, Height = height };
            if (obstacles != null) f.BoundingBoxes.AddRange(obstacles);
            double span = horizontal ? width : height;
            for (int k = 1; k * 0.001 <= span; k++)
                foreach (int sign in new[] { 1, -1 })
                {
                    double d = sign * k * 0.001, dx = horizontal ? d : 0, dy = horizontal ? 0 : d;
                    if (f.Touches(rect[0] + dx, rect[1] + dy, rect[2] + dx, rect[3] + dy) == null)
                        return new[] { dx, dy };
                }
            return null;
        }

        /// <summary>The end of a refusal that removed the segment it drew, or tried to.</summary>
        private static string SheetLineRemovedOrNot(bool removed, string what)
        {
            return removed
                ? ". I removed the " + what + " I had drawn."
                : ". I could not confirm that the " + what + " I drew is gone: look at the sheet and, if it is " +
                  "there, remove it — " + Sentences.HowToWithdrawIt + ".";
        }
    }
}
