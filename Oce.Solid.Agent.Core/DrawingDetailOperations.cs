using System;
using System.Collections.Generic;
using System.Globalization;
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
            // The views of the model only, as «inspect_part» and «model_view» count
            // them (report 8cd843 N42): the sheet is not one.
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

            // ── THE CIRCLE, ON THE SKETCH OF THE VIEW ───────────────────
            //
            // Not on the sketch of the sheet (report 8cd843 N41, round 11): on a view of
            // an assembly the detail was refused three times out of three. Probe
            // on two copies of the drawing of the report (27/10/2026): a circle drawn
            // inside the first view on the sheet sketch gave its detail to the second
            // view, the one below, both at the centre and 40 mm to the right; with
            // IDrawingDoc.ActivateView first, the same circle went in the sketch of the
            // first view, whose detail circles went 0 -> 1, and the detail had it as
            // its base.
            //
            // AND IN THE COORDINATES OF THAT SKETCH (report 8cd843 N47, round 13): the
            // details of round 13 were empty. The sketch of a view does not measure the
            // sheet: its origin is the centre of the view and its unit is the model, so a
            // circle given with sheet coordinates landed far from the place asked (see
            // SheetToViewSketch, measured). And no more the sheet sketch when the view
            // cannot be activated: it hands the circle to another view (N41, measured).
            double[] xform = null;
            try { xform = target.GetXform() as double[]; } catch { }
            double angle = 0;
            try { angle = target.Angle; } catch { }
            bool inView = false;
            try { inView = drawing.ActivateView(targetName); } catch { }
            string noSketch = ViewSketchProblem(inView, xform, angle, targetName, "circle of the detail");
            if (noSketch != null)
            {
                try { drawing.EditSheet(); } catch { }
                return Failed(noSketch);
            }
            var centre = SheetToViewSketch(xform, cx, cy);

            // As for the section: the sketch counted before the circle, so that a
            // circle left behind by a refusal can be removed and the removal
            // checked (Z-C21.8, finding M41).
            int segmentsBefore = ViewSketchSegmentCount(target);
            Func<ISketchSegment, bool> removeCircle = c => RemoveViewSketchSegment(model, target, c, segmentsBefore);
            ISketchSegment circle = null;
            // WITH NO INFERENCE (report 8cd843 N52, round 14): the circles of round 14 sat
            // 1.1 mm lower than asked on the view above. Measured on the PDF of that drawing
            // (its lines fitted with circles by a script outside the repository): the centres of B and C on the line of the part at
            // y 228.39 mm, asked at 229.49; on a copy a second circle asked with radius
            // 3 mm came out 5.117 mm, the radius of the first. SOLIDWORKS snapped the
            // centre onto an edge and the radius onto the other circle; with
            // SketchManager.AddToDB on, the same two circles landed within 0.02 mm of the
            // place asked, radii 5.000 and 3.000. On only around the creation, as the
            // anchors do (AnchorOperations, rule 1).
            bool addBefore = false;
            try
            {
                model.ClearSelection2(true);
                addBefore = model.SketchManager.AddToDB;
                model.SketchManager.AddToDB = true;
                circle = model.SketchManager.CreateCircleByRadius(centre[0], centre[1], 0.0, radius / xform[2])
                          as ISketchSegment;
            }
            catch (Exception ex)
            {
                try { drawing.EditSheet(); } catch { }
                return Failed("I could not draw the circle of the detail: " + ex.Message);
            }
            finally { try { model.SketchManager.AddToDB = addBefore; } catch { } }
            if (circle == null) { try { drawing.EditSheet(); } catch { } }
            if (circle == null)
                return Failed(
                    "«CreateCircleByRadius» produced no circle on the sheet, and without " +
                    "the circle there is nothing to select: «CreateDetailViewAt4» " +
                    "does not take the area as a signature, it takes it from the " +
                    "selection.");

            // THE CIRCLE READ BACK (N52): a snapped circle is a valid circle in the wrong
            // place, and the detail made from it enlarges the wrong lines.
            double[] circleRead = CircleInSketch(circle);
            string circleMoved = LandedElsewhere("circle of the detail",
                new[] { centre[0], centre[1], radius / xform[2] }, circleRead, 1e-6);
            if (circleMoved != null)
            {
                string circleGone = SheetLineRemovedOrNot(removeCircle(circle), "circle");
                try { drawing.EditSheet(); } catch { }
                return Failed(circleMoved + circleGone);
            }

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
            {
                string circleGone = SheetLineRemovedOrNot(removeCircle(circle), "circle");
                try { drawing.EditSheet(); } catch { }
                return Failed(
                    (selectError != null
                        ? "I could not select the detail circle (" + selectError + ")"
                        : "The detail circle is there but does not let itself be selected") +
                    ", and CreateDetailViewAt4 takes the zone from the selection: without it, it would do " +
                    "something other than what you asked" + circleGone);
            }

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
            // Back to the sheet: a step after this one (a note) must not land in
            // the view activated for the circle (N41).
            try { drawing.EditSheet(); } catch { }

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
                    "No view more: the detail was NOT born (there were " + viewsBefore +
                    ", there are " + viewsAfter + "). CreateDetailViewAt4 " +
                    (exploded != null ? "failed: " + exploded
                                     : born == null ? "returned no object"
                                                    : "returned an object") +
                    SheetLineRemovedOrNot(removeCircle(circle), "circle"));

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
                // views again, RemoveViewSketchSegment counts the sketch of the view).
                // ONLY A VIEW THAT WAS NOT THERE BEFORE IS DELETED: with no view
                // returned, the name comes from the last view of the chain, and a
                // name read before the call is a view of the user.
                bool viewIsNew = !viewNames.Contains(detailName);
                bool viewRemoved = viewIsNew && RemoveNewView(model, detailName);
                bool circleRemoved = removeCircle(circle);
                return Failed(
                    "The view «" + detailName + "» was born, but on «" + targetName +
                    "» the detail circles are still " + circlesAfter + ": the circle did not become a detail. " +
                    (viewRemoved ? "I removed the view"
                     : !viewIsNew ? "The view I read, «" + detailName + "», was on the sheet before this step, so I " +
                                    "did not delete it: find the new view and remove it — " + Sentences.HowToWithdrawIt
                     : "I could not remove the view «" + detailName + "» (remove it — " + Sentences.HowToWithdrawIt + ")") +
                    SheetLineRemovedOrNot(circleRemoved, "circle"));
            }

            // ── THE PROOF THAT THE DETAIL SHOWS THE VIEW ASKED (N47) ─────────
            //
            // One more circle on the view was not enough: in round 13 the circle was
            // counted on the view and the detail was empty. The detail must come from
            // the view asked (GetBaseView) and show at least one edge
            // (GetVisibleEntities2): measured, 0 edges for the circle at sheet
            // coordinates, 11 at the coordinates of the view.
            string detailBase = BaseViewName(detailView);
            int detailEdges = VisibleEdgeCount(detailView);
            string notAsked = NotTheViewAsked("detail", targetName, detailBase, detailEdges);
            if (notAsked != null)
            {
                bool viewIsNew = !viewNames.Contains(detailName);
                bool viewRemoved = viewIsNew && RemoveNewView(model, detailName);
                bool circleRemoved = removeCircle(circle);
                return Failed(notAsked + " " +
                    (viewRemoved ? "I removed the view «" + detailName + "»"
                                 : "I could not remove the view «" + detailName + "» (remove it — " + Sentences.HowToWithdrawIt + ")") +
                    SheetLineRemovedOrNot(circleRemoved, "circle"));
            }

            // ── THE DETAIL IN A FREE PLACE (report 8cd843 N43, round 12) ──────
            //
            // CreateDetailViewAt4 puts it where it is told, and it was told the same
            // place for every detail of a view: two details of V1 came out with the
            // same centre, and a detail of V2 on the section A-A, with no operation
            // that moves a view. Now, as the flat pattern view: if the place beside
            // the view touches another view, a table, a note or the title block, the
            // detail goes to the first free place (SheetPlacementOperations); with
            // no free place it is refused and removed, with its circle.
            string placedWhere, placeProblem;
            if (!DetailInAFreePlace(drawing, detailView, out placedWhere, out placeProblem))
            {
                bool viewIsNew = !viewNames.Contains(detailName);
                bool viewRemoved = viewIsNew && RemoveNewView(model, detailName);
                bool circleRemoved = removeCircle(circle);
                return Failed(placeProblem + " " +
                    (viewRemoved ? "I removed the view «" + detailName + "»"
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
                // Where the circle was drawn (N41, N47): always the sketch of the view
                // now, at the centre and radius below, in its own coordinates.
                ["circle_drawn_in"] = "the sketch of the view",
                ["circle_in_the_view_sketch_mm"] = new object[]
                {
                    Math.Round(centre[0] * 1000, 2), Math.Round(centre[1] * 1000, 2),
                    Math.Round(radius / xform[2] * 1000, 2),
                },
                // What the detail was made from and what it shows (N47).
                ["base_view"] = detailBase ?? "not readable",
                ["visible_edges"] = detailEdges,
                // Where the detail went (N43): beside the view, or the first free place.
                ["placed"] = placedWhere,
                ["what_it_does_not_check"] =
                    "IF THAT IS THE DETAIL THAT WAS NEEDED. I can tell you that the circle " +
                    "became a detail and that it is inside the view, not that inside it there " +
                    "is the groove you wanted to show: a circle moved by ten millimeters " +
                    "enlarges another edge, and it is just as valid (one with no edge at all " +
                    "I refuse).\nI do not " +
                    "look at " +
                    "whether the chosen scale makes the detail readable (I do look that it " +
                    "touches no other view, table, note or the title block). And I do not decide whether " +
                    "a detail is needed: that depends on how small the thing to be dimensioned " +
                    "is.",
            };

            _log("[agent] detail «" + detailName + "» from «" + targetName +
                 "»: circles " + circlesBefore + " -> " + circlesAfter +
                 ", views " + viewsBefore + " -> " + viewsAfter);
            return WithoutFeature(outside);
        }

        /// <summary>
        /// The detail where nothing else is (N43): kept where it was born when that
        /// place touches nothing, otherwise moved to the first free place of the sheet
        /// (FreePlace) and judged again where it landed, as the flat pattern view.
        /// False with the reason. A detail view is not aligned to its parent, so
        /// Position moves it on both axes (a free view moved (20, 10) mm, L14-1).
        /// </summary>
        private static bool DetailInAFreePlace(IDrawingDoc drawing, IView view, out string location, out string reason)
        {
            location = null; reason = null;
            // THE VIEW JUST BORN IS NOT YET THE VIEW (scenario t39): see SettleDetailView.
            string readings;
            if (!SettleDetailView(view, out readings))
            {
                reason = "The detail view was born, but its outline keeps changing when it is moved (read " + readings +
                         "), so I cannot tell where it is on the sheet. Try again with a smaller «radius_mm».";
                return false;
            }
            double[] o = ViewWithItsNotes(view), p = null, outline = null;
            try { p = view.Position as double[]; outline = view.GetOutline() as double[]; } catch { }
            if (o == null || p == null || p.Length < 2 || outline == null || outline.Length < 4)
            {
                reason = "The detail view was born but I cannot read where it is on the sheet, so I cannot tell " +
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
            double w = o[2] - o[0], h = o[3] - o[1];
            var down = FreePlace(sheet, w, h, out location);
            if (down == null)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "The detail view with its label ({0:0} x {1:0} mm on the sheet) {2}, and on the sheet there is no free " +
                    "place for it outside the other views, the tables, the notes and the title block: use a bigger sheet " +
                    "(«set_sheet»), a smaller «radius_mm» or «scale», or make room first.",
                    w * 1000, h * 1000, touches);
                return false;
            }
            try { view.Position = new[] { p[0] + down[0] - o[0], p[1] + down[1] - o[1] }; } catch { }
            // THE LABEL GOES WITH THE OUTLINE: a probe on a copy (27/10/2026) moved a
            // detail by (-150, -150) mm; right after Position the outline had moved and
            // the extent of the label still said the old place, and after a redraw it
            // had moved by the same (-150, -150), to 0.1 mm. So the place where the
            // detail landed is the rectangle read before, moved as far as the outline
            // moved (read again now, it is up to date at once, L14-1): read back, the
            // label was judged where it was before the move, and after a redraw (an
            // obsolete call) 0.1 mm of rounding put a detail at the frame out of it.
            double[] moved = null;
            try { moved = view.GetOutline() as double[]; } catch { }
            var o2 = LandedWithTheOutline(o, outline, moved);
            string still = o2 == null ? "cannot be read back" : sheet.Touches(o2[0], o2[1], o2[2], o2[3]);
            if (still != null)
            {
                // The rectangles say by how much (scenario t39 on 27/10/2026: «falls on the
                // title block» with nothing that said where the detail was asked and landed).
                reason = "I moved the detail view " + location + " but it " + still + " (asked: " + MmRect(down[0], down[1], down[0] + w, down[1] + h) +
                         "; with its label before the move " + MmRect(o) + ", after " + MmRect(o2) +
                         "; outline before " + MmRect(outline) + ", after " + MmRect(moved) + ").";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Brings a detail view just born to its true outline (scenario t39, 27/10/2026): right
        /// after CreateDetailViewAt4 its outline and its Position are those of a view not yet
        /// built. A probe on a copy of the drawing (outside the repository) read detail B at
        /// 56.1 x 31.2 mm at birth, the same after Position set to its own value and after a move
        /// of 1 mm, and 100.3 x 50.5 mm after the move back (on a second run already after the
        /// move of 1 mm): the size the PDF shows, the end part of the assembly whole in it.
        /// Position then read 78.4 mm away from the one read at birth, and on the first run the
        /// view had jumped by as much; EditRebuild3 and ForceRebuild3 changed nothing after. Judged
        /// at birth, detail B was sent «above the title block» with its small outline and landed
        /// on it. So the view is moved by 1 mm and back until a whole round trip leaves its outline
        /// where and as big as it was (within 0.1 mm), at most three times; false when it never
        /// settles, with the outlines read.
        /// </summary>
        private static bool SettleDetailView(IView view, out string readings)
        {
            var seen = new List<string>();
            double[] before = OutlineOf(view);
            seen.Add(MmRect(before));
            for (int round = 0; round < 3 && before != null; round++)
            {
                double[] p = null;
                try { p = view.Position as double[]; } catch { }
                if (p == null || p.Length < 2) break;
                try
                {
                    view.Position = new[] { p[0] + 0.001, p[1] };
                    view.Position = new[] { p[0], p[1] };
                }
                catch { }
                var after = OutlineOf(view);
                seen.Add(MmRect(after));
                if (SameRectangle(before, after, 0.0001))
                {
                    readings = string.Join(" -> ", seen);
                    return true;
                }
                before = after;
            }
            readings = string.Join(" -> ", seen);
            return false;
        }

        /// <summary>The outline of a view (x0, y0, x1, y1 in metres), null when it cannot be read.</summary>
        private static double[] OutlineOf(IView view)
        {
            try
            {
                var o = view.GetOutline() as double[];
                return o != null && o.Length >= 4 ? o : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Whether two rectangles are the same within <paramref name="tolerance"/> metres on
        /// every side (SettleDetailView). Pure, so the contract suite reads it.
        /// </summary>
        internal static bool SameRectangle(double[] a, double[] b, double tolerance)
        {
            if (a == null || b == null || a.Length < 4 || b.Length < 4) return false;
            for (int i = 0; i < 4; i++)
                if (Math.Abs(a[i] - b[i]) > tolerance) return false;
            return true;
        }

        /// <summary>A rectangle of the sheet (x0, y0, x1, y1 in metres) in millimetres, for a refusal.</summary>
        private static string MmRect(params double[] r)
        {
            if (r == null || r.Length < 4) return "not read";
            return string.Format(CultureInfo.InvariantCulture, "x {0:0.#}..{1:0.#} mm, y {2:0.#}..{3:0.#} mm",
                                 r[0] * 1000, r[2] * 1000, r[1] * 1000, r[3] * 1000);
        }

        /// <summary>
        /// The rectangle of a view with its notes after a move: <paramref name="before"/>
        /// moved as far as the outline moved from <paramref name="outlineBefore"/> to
        /// <paramref name="outlineAfter"/> (N43). Null when an outline is missing. Pure,
        /// so the contract suite reads it.
        /// </summary>
        internal static double[] LandedWithTheOutline(double[] before, double[] outlineBefore, double[] outlineAfter)
        {
            if (before == null || before.Length < 4 || outlineBefore == null || outlineBefore.Length < 4 ||
                outlineAfter == null || outlineAfter.Length < 4)
                return null;
            double dx = outlineAfter[0] - outlineBefore[0], dy = outlineAfter[1] - outlineBefore[1];
            return new[] { before[0] + dx, before[1] + dy, before[2] + dx, before[3] + dy };
        }

        /// <summary>
        /// The rectangle a view takes on the sheet with its notes (x0, y0, x1, y1 in
        /// metres), null when the outline cannot be read. The label of a detail is a
        /// note of the view, under GetOutline: a probe on a copy of the drawing of
        /// round 12 (27/10/2026) read the outline of detail B at 209.6..249.5 mm in y
        /// and its label at 191..207.2 mm. Left out, a detail moved to the bottom left
        /// had its label below the frame of the sheet (seen on the PDF).
        /// </summary>
        private static double[] ViewWithItsNotes(IView view)
        {
            double[] o = null;
            try { o = view.GetOutline() as double[]; } catch { }
            if (o == null || o.Length < 4) return null;
            double x0 = o[0], y0 = o[1], x1 = o[2], y1 = o[3];
            try
            {
                for (var note = view.GetFirstNote() as INote; note != null; note = note.GetNext() as INote)
                {
                    var e = note.GetExtent() as double[];
                    if (e == null || e.Length < 6) continue;
                    x0 = Math.Min(x0, Math.Min(e[0], e[3])); y0 = Math.Min(y0, Math.Min(e[1], e[4]));
                    x1 = Math.Max(x1, Math.Max(e[0], e[3])); y1 = Math.Max(y1, Math.Max(e[1], e[4]));
                }
            }
            catch { }
            return new[] { x0, y0, x1, y1 };
        }

        /// <summary>
        /// A point of the sheet in the coordinates of the sketch of a view (report 8cd843
        /// N47 and N48, round 13): (sheet - centre) / scale, with the centre and the scale
        /// read from IView.GetXform. Measured on copies of the drawing of the report
        /// (27/10/2026, a probe outside the repository): GetXform of the view below read
        /// [94.5 mm, 110.69 mm, 0.5]; the circle wanted on the sheet at (134.5, 110.69) mm,
        /// radius 10 mm, drawn at (80, 0) mm radius 20 mm in the activated view, gave a
        /// detail with 11 visible edges, and drawn at the sheet coordinates a detail with 0
        /// (the empty details of N47). The cutting line of the view above, drawn the same
        /// way, gave a section of that view with 2 face hatches. Null when the transform is
        /// missing or its scale is not positive. Pure, so the contract suite reads it.
        /// </summary>
        internal static double[] SheetToViewSketch(double[] xform, double x, double y)
        {
            if (xform == null || xform.Length < 3 || !(xform[2] > 0)) return null;
            return new[] { (x - xform[0]) / xform[2], (y - xform[1]) / xform[2] };
        }

        /// <summary>
        /// A circle of a sketch as {centre x, centre y, radius} in the coordinates of its sketch
        /// (metres), read back from ISketchArc; null when it cannot be read (N52).
        /// </summary>
        private static double[] CircleInSketch(ISketchSegment circle)
        {
            try
            {
                // GetCenterPoint2 gives an ISketchPoint, not an array: read as double[] it was
                // always null, and the reading back never refused (measured on the probe of
                // round 14: as ISketchPoint it read the snapped circle 2.175 mm lower).
                var arc = circle as ISketchArc;
                var c = arc?.GetCenterPoint2() as ISketchPoint;
                if (c == null) return null;
                return new[] { c.X, c.Y, arc.GetRadius() };
            }
            catch { return null; }
        }

        /// <summary>
        /// A line of a sketch as {start x, start y, end x, end y} in the coordinates of its sketch
        /// (metres), from ISketchLine; null when it cannot be read (N52).
        /// </summary>
        private static double[] LineInSketch(ISketchSegment line)
        {
            try
            {
                var l = line as ISketchLine;
                var a = l?.GetStartPoint2() as ISketchPoint;
                var b = l?.GetEndPoint2() as ISketchPoint;
                if (a == null || b == null) return null;
                return new[] { a.X, a.Y, b.X, b.Y };
            }
            catch { return null; }
        }

        /// <summary>
        /// Why a sketch entity drawn for a detail or a section is not where it was asked (report
        /// 8cd843 N52, round 14), null when every number read back is within
        /// <paramref name="tolerance"/> metres of the one asked. Both arrays hold the same
        /// numbers in the same order (centre and radius of a circle, or the two ends of a line),
        /// in the coordinates of the view sketch. A reading that is missing is no refusal: the
        /// entity was drawn and its numbers are not known, not known to be wrong. Pure, so the
        /// contract suite reads it.
        /// </summary>
        internal static string LandedElsewhere(string what, double[] asked, double[] read, double tolerance)
        {
            if (asked == null || read == null || read.Length < asked.Length) return null;
            double worst = 0;
            for (int i = 0; i < asked.Length; i++) worst = Math.Max(worst, Math.Abs(asked[i] - read[i]));
            if (worst <= tolerance) return null;
            var a = new List<string>(); var r = new List<string>();
            for (int i = 0; i < asked.Length; i++)
            {
                a.Add((asked[i] * 1000).ToString("0.###", CultureInfo.InvariantCulture));
                r.Add((read[i] * 1000).ToString("0.###", CultureInfo.InvariantCulture));
            }
            return "The " + what + " did not stay where I drew it: asked [" + string.Join(", ", a) + "] mm, read back [" +
                   string.Join(", ", r) + "] mm in the sketch of the view. SOLIDWORKS moved it onto the geometry " +
                   "around it, and a detail or a section made from it shows other lines than the ones asked.";
        }

        /// <summary>
        /// Why a circle or a cutting line cannot go in the sketch of a view (N47, N48),
        /// null when it can: the view did not activate (the sheet sketch hands the segment
        /// to another view, measured), its transform cannot be read, or it is rotated
        /// (SheetToViewSketch does not turn: only views at angle 0 were measured). Pure,
        /// so the contract suite reads it.
        /// </summary>
        internal static string ViewSketchProblem(bool activated, double[] xform, double angle, string viewName, string what)
        {
            if (!activated)
                return "I could not activate the view «" + viewName + "» to draw the " + what + " in its sketch, and the " +
                       "sketch of the sheet hands it to another view (measured: a cutting line drawn there gave a section " +
                       "of the view below). Try again after «inspect_part» on the drawing, or use another view.";
            if (SheetToViewSketch(xform, 0, 0) == null)
                return "I cannot read where the view «" + viewName + "» is on the sheet and at what scale (IView.GetXform), " +
                       "so I cannot draw the " + what + " where you asked. Try again after «inspect_part» on the drawing.";
            if (Math.Abs(angle) > 1e-6)
                return string.Format(CultureInfo.InvariantCulture,
                    "The view «{0}» is turned by {1:0.##} degrees, and I bring the sheet into its sketch without turning " +
                    "(only views at 0 degrees were measured): the {2} would land elsewhere. Use a view that is not turned.",
                    viewName, angle * 180.0 / Math.PI, what);
            return null;
        }

        /// <summary>The name of the view a section or a detail was made from (IView.GetBaseView), null when it cannot be read.</summary>
        private static string BaseViewName(IView view)
        {
            try
            {
                var b = view == null ? null : view.GetBaseView() as IView;
                return b == null ? null : b.GetName2();
            }
            catch { return null; }
        }

        /// <summary>The visible edges of a view (GetVisibleEntities2), -1 when they cannot be read (N47).</summary>
        private static int VisibleEdgeCount(IView view)
        {
            try
            {
                var e = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
                return e == null ? 0 : e.Length;   // ZERO-E-ZERO: null is a view with no visible edge
            }
            catch { return -1; }
        }

        /// <summary>
        /// Why a new detail or section is not the one asked (N47, N48), null when it is:
        /// made from another view (read back with GetBaseView), or with no visible edge
        /// (0). A base that cannot be read (null) and edges that cannot be read (-1, or not
        /// counted) are no refusal: the answer reports them. Pure, so the contract suite
        /// reads it.
        /// </summary>
        internal static string NotTheViewAsked(string kind, string asked, string baseRead, int visibleEdges)
        {
            if (baseRead != null && !string.Equals(baseRead, asked, StringComparison.OrdinalIgnoreCase))
                return "The " + kind + " was made from the view «" + baseRead + "», not from «" + asked + "», which " +
                       "I asked for: it would show the wrong part of the drawing. Try again, and if it happens again " +
                       "use «inspect_part» on the drawing to see which views overlap «" + asked + "».";
            if (visibleEdges == 0)
                return "The " + kind + " shows no edge (visible edges 0): it enlarges empty space of the view «" + asked +
                       "». Use «x_mm», «y_mm» and «radius_mm» on the part of the view you want to enlarge.";
            return null;
        }

        /// <summary>How many segments the sketch of a view holds, -1 when it cannot be read (N41).</summary>
        private static int ViewSketchSegmentCount(IView view)
        {
            try
            {
                var sketch = view == null ? null : view.GetSketch() as ISketch;
                if (sketch == null) return -1;
                var segments = sketch.GetSketchSegments() as object[];
                return segments == null ? 0 : segments.Length;   // ZERO-E-ZERO: null is a sketch with no segment
            }
            catch { return -1; }
        }

        /// <summary>
        /// Removes the circle of a refused detail, or the cutting line of a refused
        /// section, from the sketch of the view it was drawn in (Z-C21.8, finding M41;
        /// N41, N48): true only when that sketch, counted again, is back to
        /// <paramref name="segmentsBefore"/>; DeleteSelection2 and not EditDelete,
        /// which can open a confirmation window. Until library 0.174.4 the section
        /// drew on the sheet sketch and had its own sister for it.
        /// </summary>
        private static bool RemoveViewSketchSegment(IModelDoc2 model, IView view, ISketchSegment segment, int segmentsBefore)
        {
            if (segment == null || segmentsBefore < 0) return false;
            // Already gone (taken along by a view removed first): the count says so.
            if (ViewSketchSegmentCount(view) == segmentsBefore) return true;
            bool deleted = false;
            try
            {
                model.ClearSelection2(true);
                if (segment.Select4(false, null))
                    deleted = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
            }
            catch { }
            if (!deleted) return false;
            int now = ViewSketchSegmentCount(view);
            return now >= 0 && now == segmentsBefore;
        }
    }
}
