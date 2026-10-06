using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// A DETAIL VIEW ON A DRAWING (entry GT4, second half).
    ///
    /// ── WHY IT EXISTS ───────────────────────────────────────────────────
    ///
    /// The other half of what a real workshop drawing lacks. The
    /// section shows what is inside; the detail shows what is
    /// **too small to be read at the scale of the drawing** — a groove, a
    /// chamfer, a fillet with its dimension.
    ///
    /// ── «BY ANALOGY» WAS NOT ENOUGH, AND IT WAS WRITTEN ─────────────────
    ///
    /// Measurement 0.6 had closed the section and left the detail out with
    /// an honest note: *«by analogy it should work the same — but "by
    /// analogy" is exactly the leap that here has already cost a correction»*.
    ///
    /// That prudence paid off twice in the same
    /// afternoon. On the section, the analogy with the hatch counter was
    /// **wrong**. And here, of the three things that would have been taken for granted,
    /// two were different:
    ///
    ///   - `Showtype` does not want the enum of the detail views: it wants
    ///     `swDetCircleShowType_e`. The other name does not exist in this interop.
    ///   - `GetDetailCircleCount2(out int Size)` has two outputs, and **the
    ///     right one is the return value**. Measured on 23/9/2026:
    ///
    ///         circles BEFORE: return=0  out=1
    ///         circles AFTER : return=1  out=35
    ///
    ///     The `out` is the size of an array, and goes from 1 to 35 for ONE
    ///     circle. Whoever read it as a count would say that the view
    ///     had one already before having one.
    ///
    /// One thing instead really was the same: the circle is an `ISketchSegment`
    /// like the line, and `Select4(false, null)` is enough.
    ///
    /// ── THE CHECK IS NOT THE HATCHING ───────────────────────────────────
    ///
    /// On the section the hatch says that the cut met material.
    /// A detail cuts nothing: it shows the same geometry enlarged, and
    /// indeed `GetFaceHatchCount` on the detail answers **0**. Copying the
    /// check of the section would have produced an operation that always
    /// refuses — which is exactly the defect that the section had at first
    /// use, repeated by analogy.
    ///
    /// Here the proof that the circle has **become** a detail is that the
    /// starting view has one more detail circle: 0 -> 1.
    /// Without it, a hand-drawn circle would stay on the sheet.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome DetailView(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "The active document is not a drawing: a detail view goes on a " +
                    "sheet of a drawing. First create the drawing with «create_drawing».");

            // ── THE VIEW TO DETAIL ──────────────────────────────────────
            //
            // The FIRST «view» of the sheet is the sheet itself, and it is skipped.
            string requested = ViewRequestedByName(drawing, Str(a, "view", null));
            IView target = null;
            var viewNames = new List<string>();
            int viewsBefore = 0;
            try
            {
                var v = drawing.GetFirstView() as IView;
                while (v != null && viewsBefore < 200)
                {
                    viewsBefore++;
                    if (v.Type != (int)swDrawingViewTypes_e.swDrawingSheet)
                    {
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
                    ? "There is no view to detail on the sheet: first put the views " +
                      "there with «standard_views»."
                    : "I cannot find the view «" + requested + "». On the sheet there are: " +
                      (viewNames.Count == 0 ? "no view" : string.Join(", ", viewNames)) + ".");

            string targetName = "";
            try { targetName = target.GetName2() ?? ""; } catch { }

            var outline = target.GetOutline() as double[];
            if (outline == null || outline.Length < 4)
                return Failed(
                    "I cannot read the bounding box of the view «" + targetName +
                    "», and without that I do not know where to put the circle of the detail.");

            double xMin = outline[0], yMin = outline[1], xMax = outline[2], yMax = outline[3];
            double wide = xMax - xMin, up = yMax - yMin;

            // ── WHERE, AND HOW BIG ──────────────────────────────────────
            //
            // In millimetres of SHEET, as for the section: on the sheet the view
            // is scaled, and mixing the two measures is the quickest way to
            // enlarge the wrong place. By default at the centre, radius a
            // quarter of the shorter side.
            double xM = Num(a, "x_mm", double.NaN);
            double yM = Num(a, "y_mm", double.NaN);
            double cx = double.IsNaN(xM) ? (xMin + xMax) / 2.0 : (xMin + xMax) / 2.0 + xM / 1000.0;
            double cy = double.IsNaN(yM) ? (yMin + yMax) / 2.0 : (yMin + yMax) / 2.0 + yM / 1000.0;

            double radiusMm = Num(a, "radius_mm", Math.Min(wide, up) / 4.0 * 1000.0);
            if (radiusMm <= 0)
                return Failed("«radius_mm» must be greater than zero: I read " + radiusMm + ".");
            double radius = radiusMm / 1000.0;

            // THE CIRCLE MUST BE INSIDE THE VIEW. One that overruns enlarges
            // the void, and the enlarged void is still a valid view.
            if (cx - radius < xMin || cx + radius > xMax ||
                cy - radius < yMin || cy + radius > yMax)
                return Failed(
                    "The circle of the detail does not fit inside the view «" + targetName +
                    "»: center (" + Math.Round((cx - (xMin + xMax) / 2.0) * 1000, 1) + ", " +
                    Math.Round((cy - (yMin + yMax) / 2.0) * 1000, 1) + ") mm from the center, radius " +
                    Math.Round(radiusMm, 1) + " mm, on a view " +
                    Math.Round(wide * 1000, 1) + " x " + Math.Round(up * 1000, 1) +
                    " mm of sheet. A circle that sticks out enlarges empty space, and " +
                    "the enlarged empty space is still a view that looks good.");

            double scale1 = Num(a, "scale", 2);
            if (scale1 <= 0)
                return Failed("«scale» must be greater than zero: I read " + scale1 + ".");
            string label = Str(a, "label", "B");
            if (string.IsNullOrWhiteSpace(label)) label = "B";
            // A DETAIL «B» NEXT TO A SECTION «B-B» (entry Z-C13): it is refused beforehand.
            string doubled = LetterAlreadyUsed(drawing, label);
            if (doubled != null) return Failed(doubled);

            // How many circles it had BEFORE. See the docstring: the count is
            // the RETURN value, not the `out`.
            int circlesBefore = -1;
            try { int howMany; circlesBefore = target.GetDetailCircleCount2(out howMany); }
            catch { }

            // ── THE CIRCLE, ON THE SKETCH OF THE SHEET ──────────────────
            try { drawing.EditSheet(); } catch { }

            // As for the section: the sheet sketch counted before the circle, so
            // that a circle left behind by a refusal can be removed and the
            // removal checked (Z-C21.8, finding M41).
            int segmentsBefore = SheetSketchSegmentCount(drawing);
            ISketchSegment circle = null;
            try
            {
                model.ClearSelection2(true);
                circle = model.SketchManager.CreateCircleByRadius(cx, cy, 0.0, radius)
                          as ISketchSegment;
            }
            catch (Exception ex)
            {
                return Failed("I could not draw the circle of the detail: " + ex.Message);
            }
            if (circle == null)
                return Failed(
                    "«CreateCircleByRadius» produced no circle on the sheet, and without " +
                    "the circle there is nothing to select: «CreateDetailViewAt4» " +
                    "does not take the area as a signature, it takes it from the " +
                    "selection.");

            // The sketch is closed first, as for the section.
            try { model.SketchManager.InsertSketch(true); } catch { }

            bool taken = false;
            string selectError = null;
            try
            {
                model.ClearSelection2(true);
                taken = circle.Select4(false, null);
            }
            catch (Exception ex) { selectError = ex.Message; }
            if (!taken)
                return Failed(
                    (selectError != null
                        ? "I could not select the detail circle (" + selectError + ")"
                        : "The detail circle is there but does not let itself be selected") +
                    ", and CreateDetailViewAt4 takes the zone from the selection: without it, it would do " +
                    "something other than what you asked" +
                    SheetLineRemovedOrNot(RemoveSheetSketchSegment(model, drawing, circle, segmentsBefore), "circle"));

            // ── THE DETAIL, ALONGSIDE ───────────────────────────────────
            double xDetail = xMax + wide * 0.8;
            double yDetail = (yMin + yMax) / 2.0;

            object born = null;
            string exploded = null;
            try
            {
                born = drawing.CreateDetailViewAt4(
                    xDetail, yDetail, 0.0,
                    (int)swDetViewStyle_e.swDetViewSTANDARD, scale1, 1.0, label,
                    (int)swDetCircleShowType_e.swDetCircleCIRCLE,
                    false, false, false, 0);
            }
            catch (Exception ex) { exploded = ex.Message; }

            int viewsAfter = 0;
            IView last = null;
            try
            {
                var w = drawing.GetFirstView() as IView;
                while (w != null && viewsAfter < 200)
                {
                    viewsAfter++;
                    last = w;
                    w = w.GetNextView() as IView;
                }
            }
            catch { }

            if (viewsAfter <= viewsBefore)
                return Failed(
                    "No view more: the detail was NOT born (there were " + viewsBefore +
                    ", there are " + viewsAfter + "). CreateDetailViewAt4 " +
                    (exploded != null ? "failed: " + exploded
                                     : born == null ? "returned no object"
                                                    : "returned an object") +
                    SheetLineRemovedOrNot(RemoveSheetSketchSegment(model, drawing, circle, segmentsBefore), "circle"));

            // THE DETAIL IS THE VIEW CreateDetailViewAt4 RETURNS (Z-C21.8, finding
            // M41), as for the section: the last view of the chain only when the
            // call returned no view, and the answer says which one was read.
            IView detailView = born as IView;
            string viewReadFrom = "returned by CreateDetailViewAt4";
            if (detailView == null)
            {
                detailView = last;
                viewReadFrom = "last view of the sheet (CreateDetailViewAt4 returned no view)";
            }

            string detailName = "";
            int detailType = -1;
            try { detailName = detailView.GetName2() ?? ""; } catch { }
            try { detailType = detailView.Type; } catch { }

            int circlesAfter = -1;
            try { int howMany; circlesAfter = target.GetDetailCircleCount2(out howMany); }
            catch { }

            // ── THE PROOF THAT THE CIRCLE BECAME A DETAIL ───────────────
            //
            // Not the hatching: a detail cuts nothing and the hatch
            // there is zero (measured). The proof is that the STARTING view has
            // one more detail circle. Without it, a hand-drawn circle
            // would stay on the sheet next to one more view that is not
            // tied to it.
            if (circlesBefore >= 0 && circlesAfter >= 0 && circlesAfter <= circlesBefore)
            {
                // NEITHER IS LEFT ON THE SHEET (Z-C21.8, finding M41): until library
                // 0.144.0 this refusal described the loose circle and the unlinked
                // view, and left both. The view goes first, then the circle; each
                // is said removed only after its reread (RemoveNewView reads the
                // views again, RemoveSheetSketchSegment counts the sheet sketch).
                // ONLY A VIEW THAT WAS NOT THERE BEFORE IS DELETED: with no view
                // returned, the name comes from the last view of the chain, and a
                // name read before the call is a view of the user.
                bool viewIsNew = !viewNames.Contains(detailName);
                bool viewRemoved = viewIsNew && RemoveNewView(model, detailName);
                bool circleRemoved = RemoveSheetSketchSegment(model, drawing, circle, segmentsBefore);
                return Failed(
                    "The view «" + detailName + "» was born, but on «" + targetName +
                    "» the detail circles are still " + circlesAfter + ": the circle did not become a detail. " +
                    (viewRemoved ? "I removed the view"
                     : !viewIsNew ? "The view I read, «" + detailName + "», was on the sheet before this step, so I " +
                                    "did not delete it: find the new view and remove it — " + Sentences.HowToWithdrawIt
                     : "I could not remove the view «" + detailName + "» (remove it — " + Sentences.HowToWithdrawIt + ")") +
                    SheetLineRemovedOrNot(circleRemoved, "circle"));
            }

            string readLetter = ViewLetter(detailView);
            var outside = new Dictionary<string, object>
            {
                ["label_reread"] = readLetter,
                ["view_read_from"] = viewReadFrom,
                ["view_detailed"] = targetName,
                ["views_before"] = viewsBefore,
                ["views_after"] = viewsAfter,
                ["view_detail"] = detailName,
                ["view_kind"] = detailType,
                ["is_detail"] =
                    detailType == (int)swDrawingViewTypes_e.swDrawingDetailView,
                ["label"] = label,
                ["scale"] = scale1,
                ["circle_mm"] = new object[]
                {
                    Math.Round((cx - (xMin + xMax) / 2.0) * 1000, 2),
                    Math.Round((cy - (yMin + yMax) / 2.0) * 1000, 2),
                    Math.Round(radiusMm, 2),
                },
                ["circles_before"] = circlesBefore,
                ["circles_after"] = circlesAfter,
                ["what_it_does_not_check"] =
                    "IF THAT IS THE DETAIL THAT WAS NEEDED. I can tell you that the circle " +
                    "became a detail and that it is inside the view, not that inside it there " +
                    "is the groove you wanted to show: a circle moved by ten millimeters " +
                    "enlarges a piece of smooth surface, and it is just as valid.\nI do not " +
                    "look at " +
                    "whether the chosen scale makes the detail readable, nor whether the detail " +
                    "overlaps the other views or the title block. And I do not decide whether " +
                    "a detail is needed: that depends on how small the thing to be dimensioned " +
                    "is.",
            };

            _log("[agent] detail «" + detailName + "» from «" + targetName +
                 "»: circles " + circlesBefore + " -> " + circlesAfter +
                 ", views " + viewsBefore + " -> " + viewsAfter);
            return WithoutFeature(outside);
        }
    }
}
