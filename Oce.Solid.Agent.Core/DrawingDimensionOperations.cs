using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// DIMENSIONS ON A DRAWING VIEW, CREATED — not imported (entry K10).
    ///
    /// ── WHY IT EXISTS, AND WHY IT IS NOT CALLED «IMPORT» ────────────────
    ///
    /// `model_dimensions` **imports** the dimensions the model already has, with
    /// `InsertModelAnnotations*`. Measured on 18/9/2026: it brings **zero**, on a
    /// part with seven dimensions all marked for the drawing, on **eight** different
    /// drawing templates (seven standard plus the workshop's own), with five
    /// import routes and the positive check green in the same run
    /// (`InsertNote` on the same drawing works). That route is closed.
    ///
    /// But the goal is not «the model's dimensions on the drawing»: it is **a
    /// dimensioned drawing**. *A name that describes the MEANS instead of the goal
    /// narrows the solutions before anyone looks for them* — and for two days the
    /// name of the other operation decided the route in my place.
    ///
    /// ── THE MEASUREMENT THAT OPENED THIS ROUTE ──────────────────────────
    ///
    ///     visible edges in the view: 7
    ///       edge 0..3: AddDimension -> one object,  dimensions 0 -> 4
    ///
    ///     WHAT THEY MEASURE (part 120 x 80 x 10, chamfer 5):
    ///       RD1  120 mm     RD2  120 mm
    ///       RD3    5 mm     RD4    5 mm
    ///
    /// Four out of four, **out of process**, and with the right values. They are
    /// reference dimensions (`RD`), which is the right type for a drawing.
    ///
    /// **And the value is read back, not presumed.** The first run of the probe
    /// answered `?` for the value, and with that `?` the route would have looked
    /// open all the same: *four dimensions that appeared are not four right
    /// dimensions*, and a dimension that measures something else is worse than a
    /// dimension that is missing — on paper they read the same.
    ///
    /// ── THE TWO TRAPS, BOTH ALREADY PAID FOR BY THIS PROJECT ────────────
    ///
    /// 1. `swInputDimValOnCreate` **was on** in this installation.
    ///    Left like that, `AddDimension` opens the «Modify» window and the
    ///    COM call **never returns**: the bridge hangs, and the client
    ///    behind it with it. It is switched off first and **put back as it was in a
    ///    `finally`** — a preference left off changes SOLIDWORKS for
    ///    whoever uses it afterwards, and nobody would connect it to this operation.
    /// 2. The coordinates of `AddDimension` are those of the **SHEET**, not of the model.
    ///    Getting them wrong puts the dimension off the sheet **without any error**.
    ///    Here they are taken from `IView.GetOutline()`, which is the extent of the
    ///    view on the sheet, and scaled to metres.
    ///
    /// ── THE EDGES ARE ASKED OF THE VIEW ─────────────────────────────────
    ///
    ///     Object IView.GetVisibleEntities2(Component2 comp, Int32 entityType)
    ///
    /// with `swViewEntityType_e.swViewEntityType_Edge`. We do not guess
    /// coordinates to click on: we take the list the view itself
    /// exposes and select with `IEntity.Select4`.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome DrawingDimension(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "The active document is not a drawing: dimensions go on a " +
                    "drawing view. First create the drawing with «create_drawing».");

            var names = ViewNames(drawing);
            if (names.Count == 0)
                return Failed(
                    "The drawing has no views: the dimensions would have nowhere to attach. " +
                    "First put the views with «standard_views» or «model_view».");

            string wanted = Str(a, "view", null);
            string viewReason;
            IView view = ResolveView(drawing, names, wanted, out viewReason);
            if (viewReason != null) return Failed(viewReason);
            if (view == null)
                return Failed("I cannot get the view to dimension on.");

            string viewName = "";
            try { viewName = view.GetName2() ?? ""; } catch { }

            // ── THE EDGES THE VIEW SHOWS ────────────────────────────────
            object[] edges = null;
            try
            {
                edges = view.GetVisibleEntities2(
                    null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
            }
            catch (Exception ex)
            {
                return Failed("I cannot ask the view for its edges: " + ex.Message);
            }

            // ZERO-E-ZERO: the exception already has its refusal above; null is
            // a view without visible edges, and the zero is true.
            int available = edges == null ? 0 : edges.Length;
            if (available == 0)
                return Failed(
                    "The view «" + viewName + "» shows no edge that can be dimensioned. " +
                    "On an empty view, or on a view where the model is not resolved, " +
                    "there is nothing to dimension — and putting zero dimensions " +
                    "while answering «done» would be worse.");

            // WHICH EDGES. Without saying it, the first ones are dimensioned, and how many is stated.
            var chosen = new List<int>();
            var requestedList = StringList(a, "edges");
            if (requestedList.Count > 0)
            {
                foreach (var o in requestedList)
                {
                    int i;
                    if (!int.TryParse((o ?? "").Trim(), out i))
                        return Failed("«edges» wants numbers, and «" + o + "» is not one.");
                    if (i < 0 || i >= available)
                        return Failed(
                            "The edge " + i + " does not exist: the view «" + viewName +
                            "» has " + available + " edges (from 0 to " + (available - 1) + ").");
                    if (!chosen.Contains(i)) chosen.Add(i);
                }
            }
            else
            {
                int howMany = (int)Num(a, "count", 4);
                if (howMany <= 0)
                    return Failed("«count» must be at least 1: zero dimensions are not a dimension.");
                for (int i = 0; i < Math.Min(howMany, available); i++) chosen.Add(i);
            }

            // ── WHERE TO PUT THEM, in SHEET coordinates ─────────────────
            //
            // THE OUTLINE IS READ ONLY ONCE, before placing any
            // dimension. If `GetOutline()` grew together with the annotations
            // attached to the view, rereading it on every pass would push the
            // dimensions further and further away. It is not verified that it grows: for
            // that reason the answer reports the outline from before AND the one from
            // after, so whoever reads can see it instead of trusting.
            double[] contour = null;
            try { contour = view.GetOutline() as double[]; } catch { }
            if (contour == null || contour.Length < 4)
                return Failed(
                    "I cannot read the bounding box of the view on the sheet, and " +
                    "without it the dimensions would end up anywhere — outside the sheet, " +
                    "if it goes badly, and without any error.");

            double dimensionOffset = Num(a, "offset_mm", 12) / 1000.0;
            double planStep = Num(a, "pitch_mm", 10) / 1000.0;

            // ── HOW EACH EDGE SITS ON THE SHEET ─────────────────────────
            //
            // First we look, then we decide where the dimension goes. The previous
            // draft skipped this step and put ALL the dimensions at the same
            // X — the centre of the view — going down one step for each: for a
            // vertical edge SOLIDWORKS then builds a dimension whose measurement
            // line passes through that X, that is **in the middle of the
            // drawing**. It is the defect that could be seen by eye.
            IMathUtility mu = null;
            MathTransform tr = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; tr = view.ModelToViewTransform; } catch { }
            if (mu == null || tr == null)
                return Failed(
                    "I cannot read how the view carries the model onto the sheet: without it, the " +
                    "dimensions would end up in the wrong place, and without any error.");
            double scale = 1;
            try { scale = view.ScaleDecimal; } catch { }
            if (scale <= 0) scale = 1;

            // ALL THE EDGES OF THE VIEW, ONLY ONCE (entry GT11). Holes and
            // chamfers are looked for on all of them, not on the first «count»: a
            // plate with four holes does not stop having them because four
            // edges were asked for.
            var all = new List<ProjectedEdge>();
            var circles = new List<Circle>();
            int holesInTable = 0;   // ZERO-E-ZERO: no hole table in the view, until one looks
            for (int i = 0; i < available; i++)
            {
                var c = CircleOnSheet(edges[i] as IEdge, i, mu, tr);
                if (c != null) { circles.Add(c); continue; }
                var s = ReadEdge(view, edges[i], i, mu, tr);
                if (s != null) all.Add(s);
            }

            // THE DIMENSIONS THE DRAWING ALREADY HAS, IN ALL THE VIEWS (entry GT11). «Each
            // element of the object must be dimensioned no more than once»
            // holds for the DRAWING, not for the view: on 25/9/2026 the drawing of the
            // plate carried the 10 in the front view and in the side view, and
            // the 120 in the front view and in the top view.
            // EVERY SHEET since Z-C21.2 (finding N43), only the views of this model
            // and configuration, and without the dimensions hidden by hand (C8-2):
            // what was read is in the answer, see KeysAlreadyDimensioned.
            var already = KeysAlreadyDimensioned(drawing, view, out var existingRead);

            var failed = new List<string>();
            var externalDiameters = new List<Dictionary<string, object>>();
            var undimensionedBorders = new List<string>();
            var cutting = new List<string>();
            RoundOutcome round = null;
            var measuredValues = new List<ProjectedEdge>();
            var duplicates = new List<string>();
            var elsewhere = new List<string>();
            var onChamfer = new List<string>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var i in chosen)
            {
                if (circles.Exists(c => c.Index == i)) continue;   // the holes have their own route, below
                var s = all.Find(t => t.Index == i);
                if (s == null) { failed.Add(i + " (I cannot measure it on the sheet)"); continue; }

                // THE CHAMFER IS NOT DIMENSIONED WITH ITS EDGES. The first drawing
                // looked at had the chamfer dimensioned 7.071 obliquely (the hypotenuse)
                // and the 5 + 75 chain next to the 80: one writes 5 x 45°, with the
                // chamfer dimension, and the «legs» — the stretches shortened by the
                // chamfer — are told by the chamfer dimension and the overall size together.
                if (s.ChamferFeature != null)
                {
                    onChamfer.Add(i + " (edge of the chamfer «" + s.ChamferFeature + "»: it goes with the chamfer dimension)");
                    continue;
                }
                if (IsChamferLeg(s, all))
                {
                    onChamfer.Add(i + " (shortened by the chamfer: the chamfer dimension and the bounding box say it)");
                    continue;
                }

                // ONE DIMENSION PER ELEMENT, AND THE KEY IS NOT THE VALUE.
                //
                // «Each element of the object must be dimensioned no more than
                // once» (UNI ISO 129-1, in the Sammarone sheet for
                // Zanichelli): a rectangle wants TWO dimensions, not four, and
                // 120/10/120/10 is repeated dimensioning — the source
                // captions it «therefore wrong».
                //
                // But deduplicating on the VALUE would be wrong: two different
                // steps of 10 mm are two distinct elements and both must be
                // dimensioned. The key is geometric — how the edge sits and
                // WHERE its projection sits — so the two opposite sides of a
                // rectangle are recognised as the same measurement, and two equal
                // steps in different places are not.
                if (seen.ContainsKey(s.Key))
                {
                    duplicates.Add(i + " (same measure as the edge " + seen[s.Key] + ")");
                    continue;
                }
                if (s.InModel != null && already.Contains(s.InModel))
                {
                    elsewhere.Add(i + " (" + Math.Round(s.Length * 1000.0 / scale, 2).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                " mm: the same extent is already dimensioned in another view)");
                    continue;
                }
                seen[s.Key] = i;
                if (s.InModel != null) already.Add(s.InModel);
                measuredValues.Add(s);
            }

            // FROM THE SMALL TO THE LARGE, moving away from the part: «the
            // measurement lines shall be drawn in progressive order, from the smaller
            // to the larger, moving away from the outline of the figures».
            measuredValues.Sort((x, y) => x.Length.CompareTo(y.Length));

            int before = CountDimensions(drawing);
            if (before < 0)
                return Failed(
                    "I cannot count the dimensions of the drawing before starting: without " +
                    "that number I could not tell you which ones I put. I touched nothing.");

            // ── THE MODAL TRAP, switched off and put back ───────────────
            bool askedForTheValue = true;
            bool off = false;
            var born = new List<Dictionary<string, object>>();
            var holes = new List<Dictionary<string, object>>();
            var chamfers = new List<Dictionary<string, object>>();
            try
            {
                try
                {
                    askedForTheValue = _sw.GetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                    _sw.SetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
                    off = true;
                }
                catch { /* if it cannot be switched off, the first one that hangs will say so*/ }

                // ONE COUNTER PER BAND, not a single one for all of them. The previous
                // draft had a global one, and so the second dimension
                // went down one step even if it went to the right.
                int below = 0, sideways = 0, onLeft = 0;

                // ── THE HOLES, FIRST OF ALL (entry GT11) ────────────────
                //
                // The first drawing looked at did not dimension the holes: neither the diameter
                // nor the position, that is exactly what the workshop needs
                // most. The diameter with the HOLE CALLOUT (`AddHoleCallout2`,
                // «Ø8 PASSANTE»), one per diameter AND depth (DimensionTheHoles), with the number of equal holes
                // in front («4× »: the prefix is added to what is there, which
                // is the diameter symbol — replacing it erased it,
                // measured). The position with dimensions from the two reference
                // edges, the left one and the bottom one: in parallel,
                // so no chains and no over-dimensioning. Measured on
                // 25/9/2026: circle + edge with `AddHorizontalDimension2`
                // measures from the CENTRE (15 on the plate), with `AddDimension` nothing
                // is born.
                var sheetDimensions = SheetDimensions(drawing);
                // A HOLE TABLE IN THE VIEW ALREADY DIMENSIONS THE HOLES (entry GT5, 26/9/2026).
                // The first drawing with the table, LOOKED AT, had the holes twice:
                // positions and sizes in the table, and again callouts and position
                // dimensions on the view. «Each element is dimensioned only once»:
                // if the table is there, the holes are its own.
                holesInTable = HolesInHoleTable(drawing);
                var trueHoles = circles.FindAll(c => c.Type == "hole");
                if (trueHoles.Count > 0 && holesInTable <= 0)
                    DimensionTheHoles(model, drawing, trueHoles, all, already, contour, dimensionOffset, planStep, scale,
                               ref below, ref sideways, ref onLeft, sheetDimensions, born, holes, elsewhere, failed);
                DimensionTheContours(model, drawing, circles.FindAll(c => c.Type == "outline"), already, contour, dimensionOffset, planStep,
                               born, externalDiameters, undimensionedBorders, elsewhere, failed);
                foreach (var c in circles)
                    if (c.Type == "edge_on") cutting.Add("Ø" + c.DiameterMm.ToString("0.##", Ci));
                // THE ROUND BAR SEEN FROM THE SIDE (entry GT14): coaxial circles seen edge-on
                // mean a turned part in the longitudinal view —
                // lengths between the faces and diameters from the generatrices.
                round = DimensionTheRoundFromTheSide(model, drawing, view, mu, tr, circles.FindAll(c => c.Type == "edge_on"),
                                             already, contour, dimensionOffset, planStep, ref below, ref sideways, born, elsewhere, failed);

                foreach (var s in measuredValues)
                {
                    int dimensionsBefore = CountDimensions(drawing);
                    try
                    {
                        model.ClearSelection2(true);
                        var ent = edges[s.Index] as IEntity;
                        if (ent == null) { failed.Add(s.Index + " (it is not an entity)"); continue; }
                        if (!ent.Select4(false, null)) { failed.Add(s.Index + " (it cannot be selected)"); continue; }

                        // ── WHERE THIS DIMENSION GOES ───────────────────
                        //
                        // «They must be parallel to the dimension they
                        // refer to» and «they must, as far as possible, be
                        // placed outside the figures» (Zanichelli
                        // sheet, ARRANGEMENT OF THE MEASUREMENT LINES).
                        // Therefore: horizontal BELOW, vertical ON THE SIDE.
                        double x, y;
                        if (s.Facing == "horizontal")
                        {
                            x = s.Half;
                            y = contour[1] - dimensionOffset - below * planStep;
                            below++;
                        }
                        else if (s.Facing == "vertical")
                        {
                            x = contour[2] + dimensionOffset + sideways * planStep;
                            y = s.Half;
                            sideways++;
                            // INSIDE THE FRAME: a view on the right edge of the
                            // sheet sends the band out of the drawing. Then to the
                            // left — same rule as the holes.
                            if (sheetDimensions != null && x > sheetDimensions[0] - 0.015)
                            {
                                sideways--;
                                x = contour[0] - dimensionOffset - onLeft * planStep;
                                onLeft++;
                            }
                        }
                        else
                        {
                            // OBLIQUE: there is no right band, and putting it
                            // with the horizontal ones would make it look like one. It goes
                            // to the left, and the answer says so.
                            x = contour[0] - dimensionOffset - onLeft * planStep;
                            y = (contour[1] + contour[3]) / 2;
                            onLeft++;
                        }

                        var nature = model.Extension.AddDimension(x, y, 0, 0)
                                     as IDisplayDimension;
                        try { model.EditRebuild3(); } catch { }

                        if (NotGrown(drawing, dimensionsBefore, nature))
                        {
                            // WE DO NOT LOOK AT WHETHER `AddDimension` HAS A RESPONSE: we
                            // look at whether the dimension is there. It is the house rule —
                            // the value that comes back from whoever did the thing is not
                            // the measurement of the thing.
                            failed.Add(s.Index + " (the call went through, the dimension was not created)");
                            continue;
                        }

                        var described = DescribeDimension(nature, s.Index);
                        described["orientation"] = s.Facing;
                        described["position_mm"] = WhereItEnded(nature);
                        born.Add(described);
                    }
                    catch (Exception ex)
                    {
                        var m = ex.Message ?? "";
                        failed.Add(s.Index + " (" + (m.Length > 90 ? m.Substring(0, 90) + "..." : m) + ")");
                    }
                }

                // ── THE CHAMFERS, WITH THE CHAMFER DIMENSION (entry GT11) ─────
                DimensionTheChamfers(model, drawing, edges, all, already, contour, dimensionOffset, born, chamfers, elsewhere, failed);
            }
            finally
            {
                if (off)
                    try
                    {
                        _sw.SetUserPreferenceToggle(
                            (int)swUserPreferenceToggle_e.swInputDimValOnCreate, askedForTheValue);
                    }
                    catch { }
                try { model.ClearSelection2(true); } catch { }
            }

            int after = CountDimensions(drawing);
            if (after < 0)
                return Failed(
                    "I put the dimensions in the view «" + viewName + "», but then I cannot count " +
                    "them again: I cannot tell you how many were created. Look at the drawing before " +
                    "going on.");
            int put = after - before;

            if (put <= 0)
            {
                // NOTHING TO DIMENSION IS NOT THE SAME THING AS «I DID NOT MANAGE».
                // If everything the view shows is already dimensioned elsewhere, or is
                // a duplicate, or sits in the chamfer, it says so — and it stays
                // a refusal, because zero dimensions are not a «done» answer.
                if (failed.Count == 0 && cutting.Count > 0 && measuredValues.Count == 0 && round == null &&
                    duplicates.Count + elsewhere.Count + onChamfer.Count == 0 && holesInTable <= 0)
                    return Failed(
                        "In view «" + viewName + "» the only edges are circles seen edge-on (" +
                        string.Join(", ", cutting) + ") and they do not lie on one axis: this is not a round part " +
                        "seen from the side, the only kind I can dimension here. For the diameter of a circle, use the view in " +
                        "which it is a circle (edge-on, AddDiameterDimension2 says 60 for a Ø30).");
                if (failed.Count == 0 && ((duplicates.Count + elsewhere.Count + onChamfer.Count) > 0 || holesInTable > 0))
                    return Failed(
                        "In view «" + viewName + "» there is nothing left to dimension that is not " +
                        "already dimensioned: " + duplicates.Count + " duplicates in the view, " + elsewhere.Count +
                        " already dimensioned in another view, " + onChamfer.Count + " on the chamfer" +
                        (holesInTable > 0 ? ", " + holesInTable + " holes in the hole table" : "") + ". " +
                        "Each element is dimensioned once only in the whole drawing (UNI ISO 129-1).");
                return Failed(
                    "No dimension was created (there were " + before + ", there are " + after + "). " +
                    "The view «" + viewName + "» shows " + available + " edges and " +
                    "I tried " + chosen.Count +
                    (failed.Count == 0 ? "" : ": " + string.Join(", ", failed)) + ". " +
                    "If the «Modify» window stayed open, the call hangs instead of " +
                    "failing: that did not happen here, so it is something else.");
            }

            double[] afterContour = ContourOf(view);

            // ── THE TEXTS THAT OVERLAP (entry GT12, point 3) ─────────────
            // The true rectangle of each text, read from IDisplayData
            // (OperazioniTestiQuote): text on text over the whole drawing, text
            // on a line of the part in this view.
            var dimensionsTexts = DimensionTexts(drawing);
            var overlapping = TextsThatOverlap(drawing, view, dimensionsTexts);

            var measurements = new Dictionary<string, object>
            {
                ["view"] = viewName,
                ["edges_available"] = available,
                ["edges_tried"] = chosen.Count,
                ["dimensions_before"] = before,
                ["dimensions_after"] = after,
                ["dimensions_placed"] = put,
                // EVERY DIMENSION WITH ITS VALUE READ BACK, in millimetres. It is the piece
                // that distinguishes this operation from one that draws numbers.
                ["dimensions"] = born.ToArray(),
                // AND THE SAME MEASUREMENTS IN A ROW, WITHOUT THE ORDER.
                //
                // `dimensions[0].mm` forces whoever tests to know **in what order**
                // the view lists its edges — an order that nobody
                // declares, and that changes with the part. On 18/9/2026 a bench case
                // was written on `quote[0]` and `quote[2]` taking the
                // numbers from a probe made on ANOTHER part: seven edges
                // instead of four, and the index no longer meant anything.
                //
                // Here there are only the millimetres: one asks «is there a 120?»
                // instead of «is the third one 5?», and the question stays true even if
                // SOLIDWORKS changes the order.
                // *An expectation that depends on an undeclared order measures the
                // order, not the thing.*
                ["dimensions_mm"] = MillimetersInARow(born),
                ["edges_not_dimensioned"] = failed.ToArray(),

                // ── HOLES, CHAMFERS, AND WHAT WAS ALREADY DIMENSIONED (entry GT11) ──
                //
                // Born on 25/9/2026 from a drawing that was LOOKED AT: the holes were not
                // dimensioned, the chamfer was an oblique 7.071, the 10 and the 120
                // appeared in two views. The bench expectations were all
                // green, because they asked for the millimetres of the dimensions put —
                // not whether the drawing is enough to make the part.
                ["holes"] = holes.ToArray(),
                ["holes_in_hole_table"] = holesInTable > 0 ? (object)holesInTable : null,
                // THE CIRCLES THAT ARE NOT HOLES (26/9/2026): the outline of a round
                // part has a diameter dimension, not a callout; a circle seen
                // edge-on is not dimensioned from here.
                ["outer_diameters"] = externalDiameters.ToArray(),
                ["borders_not_dimensioned"] = undimensionedBorders.ToArray(),
                ["cut_circles"] = cutting.ToArray(),
                // THE ROUND BAR SEEN FROM THE SIDE (entry GT14): lengths in parallel from the
                // first face, diameters from the generatrices; and the diameters the
                // view does not expose, stated.
                ["round_axis"] = round?.Axis,
                ["round_lengths_mm"] = round == null ? null : round.Lengths.ToArray(),
                ["round_diameters_mm"] = round == null ? null : round.Diameters.ToArray(),
                ["round_diameters_not_reached"] = round == null ? null : round.NotReached.ToArray(),
                ["round_notes"] = round == null ? null : round.Note.ToArray(),
                ["round_generatrices_without_face"] = round == null ? null : (object)round.FacelessGeneratrices,
                ["chamfers"] = chamfers.ToArray(),
                ["already_dimensioned_elsewhere"] = elsewhere.ToArray(),
                // WHAT «ALREADY DIMENSIONED» WAS READ FROM (Z-C21.2, findings N43 and
                // C8-2). «every sheet» when IDrawingDoc.GetViews handed over the views
                // of all the sheets, «current sheet only» when it did not and the walk
                // fell back to the sheet in front; «interrupted» when the walk broke
                // off, and then the two counts are null (not known), not zero. The
                // counts are what was left out on purpose: dimensions hidden by hand
                // (not on the paper), and views of another model or configuration
                // (their model coordinates are not this part's).
                ["already_dimensioned_read_from"] =
                    (existingRead.EverySheet ? "every sheet" : "current sheet only") +
                    (existingRead.Complete ? "" : ", interrupted: some views were not read"),
                ["hidden_dimensions_not_counted"] = existingRead.Complete ? (object)existingRead.HiddenByHand : null,
                ["views_of_other_models_not_counted"] = existingRead.Complete ? (object)existingRead.ViewsOfAnotherModel : null,
                ["chamfer_edges"] = onChamfer.ToArray(),

                // ── DUPLICATES ARE STATED, NOT HIDDEN ─────────────
                //
                // Asking for four edges of a rectangle and seeing two of them
                // dimensioned is surprising until one knows why. The
                // reason is a drawing rule, not a defect: «each
                // element of the object must be dimensioned no more than once»
                // (UNI ISO 129-1, Sammarone/Zanichelli sheet), and
                // 120/10/120/10 is repeated dimensioning — «therefore wrong».
                ["edges_discarded_duplicates"] = duplicates.ToArray(),

                // THE OUTLINE BEFORE AND AFTER. It is not established that `GetOutline()`
                // grows with the annotations attached to the view: if the two
                // numbers differ, it grows, and whoever puts more dimensions after
                // us must know it instead of discovering it.
                ["contour_view_mm"] = InMillimeters(contour),
                ["contour_view_after_mm"] = InMillimeters(afterContour),
                // THE COMPARISON IS DONE BY THE CODE, not by whoever writes the expectation. A
                // bench case can demand a value, not compare two fields
                // with each other: putting the comparison here, already done, is the only
                // way for it to become a verifiable line.
                ["contour_grown"] = GrownContour(contour, afterContour),
                // A WALK THAT BROKE OFF IS «NOT KNOWN» (Z-C21.8, finding N47):
                // DimensionTexts gives null then, and the three fields are null,
                // not the count of the texts read before the break.
                ["texts_read"] = dimensionsTexts == null ? null : (object)dimensionsTexts.Count,
                ["overlapping_texts"] = overlapping == null ? null : overlapping.ToArray(),
                // No text read on a drawing with dimensions is «I do not know», not
                // «none overlaps» (review of 26/9/2026).
                ["how_many_texts_overlap"] = dimensionsTexts == null || overlapping == null ||
                                                  (dimensionsTexts.Count == 0 && after > 0)
                    ? null : (object)overlapping.Count,
                ["offset_used_mm"] = Math.Round(dimensionOffset * 1000, 2),
                ["pitch_used_mm"] = Math.Round(planStep * 1000, 2),

                ["where_placed"] =
                    "OUTSIDE the outline of the view, in sheet coordinates: the " +
                    "horizontal ones below, the vertical ones at the side, the oblique ones on the left, " +
                    "in increasing order moving away from the part. It comes from two " +
                    "criteria of the standard — the dimension line is parallel to the " +
                    "dimension it refers to, and lies as far as possible " +
                    "outside the figures (UNI ISO 129-1, in the data sheet " +
                    "Sammarone/Zanichelli) — plus the distance: more than 10 mm from the " +
                    "outline and a constant pitch between 7 and 12 on A3/A4. Inside the border " +
                    "of the sheet: if there is no room on one side, on the other. Holes with the " +
                    "callout and the position from the reference edges (left and " +
                    "bottom: zero is the bottom-left corner, the same as the " +
                    "hole table's), attached to the hole nearest the side of the dimension; " +
                    "chamfers with the chamfer dimension, outside the part on the side of the chamfer.",

                ["what_it_does_not_check"] =
                    "WHICH dimensions are needed, and whether they are enough. The standard " +
                    "distinguishes functional, non-functional and auxiliary dimensions, and a " +
                    "functional dimension must not be derived from others: the designer knows " +
                    "that. I can say how many I put, not whether the part is defined.\n" +
                    "The TEXT of the dimensions I do look at (since 26/9/2026): its real rectangle, " +
                    "read by SOLIDWORKS, against the other texts of the drawing and the lines of the " +
                    "part in this view — «overlapping_texts». I do not look at the dimension " +
                    "lines and the leader lines, nor the title block. And I do not look " +
                    "at: whether a position dimension of the holes should have been taken from another " +
                    "functional reference (I take the borders); which system to adopt among " +
                    "serial, parallel and overlapping dimensions; whether a dimension should have " +
                    "been put in parentheses as auxiliary. Tolerances, surface finish and geometric " +
                    "references I do not put.\nDimensioning well " +
                    "remains a craft: these rules remove the duplicates and bring the dimensions " +
                    "outside the part, they do not make a master drawing.",
            };

            _log("[agent] " + put + " dimensions on the view «" + viewName + "»: " +
                 ValuesInLine(born));
            return WithoutFeature(measurements);
        }

        /// <summary>
        /// One edge, measured ON THE SHEET instead of in the model.
        ///
        /// It serves to know two things before dimensioning: which side it is on — and
        /// therefore where its dimension goes — and where it is, to recognise that two
        /// different edges measure the same element.
        /// </summary>
        private sealed class ProjectedEdge
        {
            public int Index;
            /// <summary>«horizontal», «vertical» or «oblique».</summary>
            public string Facing;
            /// <summary>The centre of the projection, in sheet coordinates.</summary>
            public double Half;
            public double Length;
            /// <summary>
            /// Direction plus projected interval, rounded to the hundredth of a
            /// millimetre. Two edges with the same key measure the same
            /// element: the opposite sides of a rectangle. Two equal steps
            /// in different places have different keys, and both are dimensioned.
            /// </summary>
            public string Key;
            /// <summary>The ends on the sheet and in the model, in metres.</summary>
            public double[] FromSheet, ToSheet, FromModel, ToModel;
            /// <summary>Axis and interval in the MODEL: the key between views. Null if oblique.</summary>
            public string InModel;
            /// <summary>The chamfer feature it belongs to, or null.</summary>
            public string ChamferFeature;
            /// <summary>The entity as the view exposes it, to select it.</summary>
            public object Entity;
        }

        /// <summary>
        /// From a model edge to an edge on the sheet. `null` if it cannot
        /// be measured, which is not an error: it is stated and we go on.
        ///
        /// ── WHY A TRANSFORMATION IS NEEDED ──────────────────────────────
        ///
        /// `IEdge.GetCurveParams3()` gives the ends in MODEL
        /// coordinates. The view shows them rotated and scaled, and a dimension is
        /// placed in SHEET coordinates: without going through
        /// `IView.ModelToViewTransform` an edge that is vertical on the sheet
        /// would be classified as horizontal, and the dimension would end up on the
        /// wrong side.
        ///
        /// FOR THE DIRECTION ALONE THE ROTATIONAL PART IS ENOUGH: a translation does not
        /// change the direction of a vector. For the POINT instead the
        /// translation would be needed too, and it is not established that that matrix carries the
        /// position of the view onto the sheet — for that reason the absolute
        /// coordinates are taken from `GetOutline()`, which is already in sheet
        /// coordinates, and from here only the RELATIVE centre along the axis of
        /// measurement comes out, brought inside the extent.
        /// </summary>
        private static ProjectedEdge ReadEdge(IView view, object entity, int index, IMathUtility mu, MathTransform tr)
        {
            try
            {
                var edge = entity as IEdge;
                if (edge == null) return null;
                var p = ApiHelpers.EdgeParameters(edge);
                if (p == null) return null;
                var da = p.StartPoint as double[];
                var a = p.EndPoint as double[];
                if (da == null || a == null || da.Length < 3 || a.Length < 3) return null;

                // ON THE SHEET WITH THE WHOLE TRANSFORMATION, and the previous draft
                // was wrong twice (measured on 25/9/2026, when the drawing was
                // LOOKED AT and the numbers were seen above the lines):
                //
                //   1. it used the TRANSPOSED matrix: for a rotated view the Y
                //      came out mirrored around the centre — in the top
                //      view the chamfer strip fell at y 70.7
                //      instead of 145.7, and the «5» was written at the bottom of the
                //      view, with a measurement line as long as the part;
                //   2. it added `IView.Position` instead of the translation of the
                //      matrix. They are equal only if the part is centred
                //      on the origin: for a plate 10 high extruded from 0, the
                //      front view put it between 229.5 and 239.5 while it sits
                //      between 224.5 and 234.5 (the outline of the view is symmetric
                //      by 5.94 mm). The text of the vertical «10»s ended up 5 mm higher,
                //      ABOVE the callout line.
                //
                // The horizontal ones were saved because the plate was centred in
                // X. `MultiplyTransform` does the calculation the way SOLIDWORKS does it, and the
                // point comes out already in sheet coordinates.
                double[] fromV = OnSheet(da, mu, tr), aV = OnSheet(a, mu, tr);
                if (fromV == null || aV == null) return null;
                double dx = aV[0] - fromV[0], dy = aV[1] - fromV[1];
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) return null;

                // THE THRESHOLD IS A CODE CHOICE, NOT A PRESCRIPTION.
                // The sources speak of parallelism as an exact geometric
                // criterion; a number is needed all the same, and it is declared. One
                // thousandth on the normalised component: below it, the edge
                // is parallel to an axis within a tenth of a degree.
                const double THRESHOLD = 1e-3;
                var s = new ProjectedEdge { Index = index, Length = len };
                double a0, a1;
                if (Math.Abs(dy) / len < THRESHOLD)
                {
                    s.Facing = "horizontal";
                    a0 = Math.Min(fromV[0], aV[0]); a1 = Math.Max(fromV[0], aV[0]);
                }
                else if (Math.Abs(dx) / len < THRESHOLD)
                {
                    s.Facing = "vertical";
                    a0 = Math.Min(fromV[1], aV[1]); a1 = Math.Max(fromV[1], aV[1]);
                }
                else
                {
                    s.Facing = "oblique";
                    a0 = Math.Min(fromV[0], aV[0]); a1 = Math.Max(fromV[0], aV[0]);
                }
                s.Half = (a0 + a1) / 2;
                s.Key = s.Facing + "|" + Math.Round(a0 * 100000) + "|" + Math.Round(a1 * 100000);
                s.FromSheet = fromV;
                s.ToSheet = aV;
                s.FromModel = da;
                s.ToModel = a;
                s.InModel = KeyInModel(da, a);
                s.ChamferFeature = TouchesAChamfer(edge);
                s.Entity = entity;
                return s;
            }
            catch { return null; }
        }

        /// <summary>A whole circle in the view: a hole, the outline of a round part, or a circle seen edge-on.</summary>
        private sealed class Circle
        {
            public int Index;
            public IEdge Edge;
            public double[] SheetCenter;   // metres
            public double DiameterMm;
            public string Type;             // "hole", "outline", "edge_on"
            public bool? ThroughAll;          // from the geometry; null if it is not understood
            public double DepthMm = double.NaN;
            public double[] ModelCenter;  // metres, in the model
            public double[] ModelAxis;    // unit vector, in the model
        }

        private static readonly System.Globalization.CultureInfo Ci = System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>The circle on the sheet, if the edge is a whole circle; null otherwise.</summary>
        private static Circle CircleOnSheet(IEdge e, int index, IMathUtility mu, MathTransform tr)
        {
            try
            {
                var p = ApiHelpers.EdgeParameters(e, out ICurve curva);
                if (curva == null || !curva.IsCircle()) return null;
                var da = p?.StartPoint as double[];
                var a = p?.EndPoint as double[];
                // A WHOLE CIRCLE has start and end at the same point: an arc
                // (a fillet, a slot) is not a hole to call out.
                if (da == null || a == null ||
                    Math.Abs(da[0] - a[0]) + Math.Abs(da[1] - a[1]) + Math.Abs(da[2] - a[2]) > 1e-8) return null;
                var cp = curva.CircleParams as double[];
                if (cp == null || cp.Length < 7) return null;
                var center = OnSheet(new[] { cp[0], cp[1], cp[2] }, mu, tr);
                if (center == null) return null;
                // WHICH CIRCLE IT IS (26/9/2026, a turned bushing): the Ø30 outline
                // of a round part took the callout «Ø30 ↧30» as if it were a
                // hole, and the two circles of the bushing seen edge-on ended up among
                // the «holes whose callout was not born». A hole faces whoever reads
                // (the axis, brought onto the sheet, is perpendicular to the sheet) AND it is
                // an inner ring of a planar face — the criterion of the centre
                // marks (IsAVisibleHole).
                string typ;
                var axis = ((IMathVector)((IMathVector)mu.CreateVector(new[] { cp[3], cp[4], cp[5] })).MultiplyTransform(tr)).ArrayData as double[];
                // ZERO-E-ZERO: an axis that cannot be brought onto the sheet has length zero
                // on purpose — the circle ends up among the edge-on ones, that is NOT dimensioned
                // and stated in the answer, instead of taking a hole callout.
                double l = axis == null ? 0 : Math.Sqrt(axis[0] * axis[0] + axis[1] * axis[1] + axis[2] * axis[2]);
                if (l < 1e-12 || Math.Abs(axis[2]) / l < 0.99) typ = "edge_on";
                // AN INNER RING IS NOT ENOUGH (adversarial review of 27/9/2026): so
                // is the foot of a boss, and a tangency circle Ø24 at the
                // foot of a filleted Ø20 boss became «a hole» with its
                // position dimensions. A boss has a diameter dimension.
                else typ = IsAVisibleHole(e, curva, mu, tr) && !IsTheFootOfAStud(e) ? "hole" : "outline";
                var c = new Circle { Index = index, Edge = e, SheetCenter = center, DiameterMm = Math.Round(cp[6] * 2000.0, 3), Type = typ,
                                      ModelCenter = new[] { cp[0], cp[1], cp[2] }, ModelAxis = new[] { cp[3], cp[4], cp[5] } };
                if (typ == "hole")
                {
                    double prof;
                    c.ThroughAll = ThroughAllFromGeometry(e, out prof);
                    c.DepthMm = prof;
                }
                return c;
            }
            catch { return null; }
        }

        /// <summary>
        /// Does the edge touch a chamfer edge with one end (in the model)?
        /// Then it is a stretch SHORTENED by the chamfer — the 75 next to the 80,
        /// the 5 next to the 10 — and its measurement is told by the chamfer dimension and
        /// the overall size together: dimensioning it is one dimension too many.
        /// </summary>
        private static bool IsChamferLeg(ProjectedEdge s, List<ProjectedEdge> all)
        {
            foreach (var t in all)
            {
                if (t.ChamferFeature == null || ReferenceEquals(t, s)) continue;
                foreach (var p in new[] { s.FromModel, s.ToModel })
                    foreach (var q in new[] { t.FromModel, t.ToModel })
                        if (Math.Abs(p[0] - q[0]) + Math.Abs(p[1] - q[1]) + Math.Abs(p[2] - q[2]) < 1e-7) return true;
            }
            return false;
        }

        /// <summary>
        /// What is ALREADY dimensioned in the drawing, in all the views: the model
        /// extents (axis and interval) of the dimensions on a single edge, the
        /// holes by diameter, the chamfers by feature. It is read from the
        /// edges the dimensions are attached to, not from the numbers: two different 10 values
        /// are two elements.
        ///
        /// THE WHOLE DRAWING, AS THE CATALOGUE PROMISES (Z-C21.2, finding N43).
        /// Until library 0.140.0 the walk started at IDrawingDoc.GetFirstView,
        /// which is «the first drawing view on the current sheet»
        /// (IDrawingDoc~GetFirstView.html): a second sheet with a view of the
        /// same part got the 120 and the 10 again. Now the views of every sheet
        /// (ApiHelpers.ViewsOfEverySheet, IDrawingDoc.GetViews); when GetViews
        /// does not hand them over, the current sheet as before, and
        /// <paramref name="reading"/> says which.
        ///
        /// ONLY THE VIEWS OF THIS MODEL. The keys are model coordinates, and on
        /// another sheet (one part per sheet is the common layout) a different
        /// plate 120 long from the origin has the same key as this one: its 120
        /// would silently take this part's 120 away. A view whose model and
        /// configuration differ from those of <paramref name="view"/> is left
        /// out; one that does not say counts, as every view of the current
        /// sheet did (ApiHelpers.MayShowTheSameModel).
        ///
        /// NOT THE DIMENSIONS HIDDEN BY HAND (Z-C21.2, finding C8-2).
        /// IView~GetFirstDisplayDimension5.html hands over hidden and suppressed
        /// dimensions too, and a dimension hidden by hand made its edge «already
        /// dimensioned in this drawing» while the paper did not show it. It is
        /// skipped by ApiHelpers.IsHiddenByHand; a hidden layer and a suppressed
        /// owner feature are not seen by IAnnotation.Visible (its Remarks) and
        /// still count.
        /// </summary>
        private static HashSet<string> KeysAlreadyDimensioned(IDrawingDoc drawing, IView view, out ExistingDimensionsReading reading)
        {
            var outside = new HashSet<string>(StringComparer.Ordinal);
            reading = new ExistingDimensionsReading();
            // The generatrices tell their face only once the drawing is redrawn (82zp).
            try { (drawing as IModelDoc2)?.GraphicsRedraw2(); } catch { }
            try
            {
                List<IView> views = null;
                try { views = ApiHelpers.ViewsOfEverySheet(drawing); } catch { }
                reading.EverySheet = views != null;
                if (views == null)
                {
                    var currentSheet = new List<IView>();
                    var w = (drawing.GetFirstView() as IView)?.GetNextView() as IView;   // [0] is the sheet
                    while (w != null) { currentSheet.Add(w); w = w.GetNextView() as IView; }
                    views = currentSheet;
                }
                string thisModel = null;
                try { thisModel = ApiHelpers.ReferencedModelKey(view); } catch { }
                foreach (var v in views)
                {
                    string itsModel = null;
                    try { itsModel = ApiHelpers.ReferencedModelKey(v); } catch { }
                    if (!ApiHelpers.MayShowTheSameModel(thisModel, itsModel)) { reading.ViewsOfAnotherModel++; continue; }
                    for (var d = v.GetFirstDisplayDimension5(); d != null; d = d.GetNext5())
                    {
                        // Not on the paper, not «already dimensioned» (C8-2). A state
                        // that cannot be read counts as shown, as before.
                        bool hidden = false;
                        try { hidden = ApiHelpers.IsHiddenByHand(d); } catch { }
                        if (hidden) { reading.HiddenByHand++; continue; }
                        try
                        {
                            var ann = d.GetAnnotation() as IAnnotation;
                            var ents = ann?.GetAttachedEntities3() as object[];
                            var straight = new List<IEdge>();
                            // TWO COAXIAL CIRCLES ARE A LENGTH OF THE ROUND BAR (entry GT14),
                            // not two diameters already dimensioned; A GENERATRIX carries the
                            // diameter of its face.
                            var attachedCircles = new List<double[]>();
                            bool onGeneratrix = false, withStraightLine = false;
                            string generatrixKey = null;
                            if (ents != null)
                                foreach (var o in ents)
                                {
                                    var there = o as IEdge;
                                    var cc = there == null ? null : there.GetCurve() as ICurve;
                                    if (cc != null && cc.IsCircle()) attachedCircles.Add(cc.CircleParams as double[]);
                                    if (cc != null && cc.IsLine()) withStraightLine = true;
                                    var gen = o as ISilhouetteEdge;
                                    if (gen != null)
                                    {
                                        onGeneratrix = true;
                                        IFace2 fg = null;
                                        try { fg = gen.GetFace() as IFace2; } catch { }
                                        var kg = CylinderKey(fg);
                                        if (kg != null) generatrixKey = kg;
                                    }
                                }
                            // A DIMENSION ON GENERATRICES (adversarial review of 27/9/2026):
                            // with the cylinder, ITS key; WITHOUT a face — on the bushing GetFace
                            // stays null (82zp) — the diameter from the value, if the dimension carries the Ø:
                            // the same criterion by which the «generatrices without face» route
                            // held it. Before, it was not seen at all, and the next call
                            // put the same diameter back.
                            if (onGeneratrix)
                            {
                                if (generatrixKey != null)
                                {
                                    outside.Add(generatrixKey);
                                    outside.Add("hole|" + generatrixKey.Split('|')[1]);
                                }
                                else
                                {
                                    string pre = ""; double mm = double.NaN;
                                    try { pre = d.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? ""; } catch { }
                                    try { mm = ApiHelpers.ReadDimensionInConfiguration(d.GetDimension2(0) as IDimension, "") * 1000.0; } catch { }
                                    if (pre.Contains("<MOD-DIAM>") && !double.IsNaN(mm))
                                    {
                                        string dd = Math.Round(mm, 2).ToString("0.00", Ci);
                                        outside.Add("hole|" + dd);
                                        outside.Add("cyl?|" + dd);   // a Ø30, but it is not known of which cylinder
                                    }
                                }
                            }
                            bool roundLength = attachedCircles.Count == 2 && Coaxial(attachedCircles[0], attachedCircles[1]);
                            if (roundLength)
                                outside.Add(RoundKey(new[] { attachedCircles[0][0], attachedCircles[0][1], attachedCircles[0][2] },
                                                      new[] { attachedCircles[1][0], attachedCircles[1][1], attachedCircles[1][2] }));
                            if (ents != null && !roundLength)
                                foreach (var o in ents)
                                {
                                    var e = o as IEdge;
                                    if (e == null) continue;
                                    var c = e.GetCurve() as ICurve;
                                    if (c != null && c.IsCircle())
                                    {
                                        // A DIAMETER, NOT A POSITION: the position dimension of a
                                        // hole is attached to the circle AND to an edge, and does not dimension the
                                        // hole (the callout is born first and carries its key).
                                        if (withStraightLine) continue;
                                        var cp = c.CircleParams as double[];
                                        if (cp == null || cp.Length < 7) continue;
                                        string dd = Math.Round(cp[6] * 2000.0, 2).ToString("0.00", Ci);
                                        outside.Add("hole|" + dd);
                                        // AND ITS CYLINDER, if found: a step of a round bar and a
                                        // hole of the same Ø are two elements (review of 27/9).
                                        outside.Add(CylinderCircleKey(e) ?? "cyl?|" + dd);
                                        continue;
                                    }
                                    var sm = TouchesAChamfer(e);
                                    if (sm != null) outside.Add("chamfer|" + sm);
                                    if (c != null && c.IsLine()) straight.Add(e);
                                }
                            // THE EXTENT OF AN EDGE IS DIMENSIONED only by a dimension attached
                            // to IT ALONE (adversarial review of 27/9/2026): the position
                            // dimension of a hole takes the edge of the plate as its
                            // reference, and recording its extent made the true height be discarded
                            // in the next view («gia' quotata in un'altra vista»).
                            if (straight.Count == 1 && ents.Length == 1 && attachedCircles.Count == 0 && !onGeneratrix)
                            {
                                var p = ApiHelpers.EdgeParameters(straight[0]);
                                var k = KeyInModel(p.StartPoint as double[], p.EndPoint as double[]);
                                if (k != null) outside.Add(k);
                            }
                        }
                        catch { }
                    }
                }
                reading.Complete = true;
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// What the reading of the existing dimensions covered (Z-C21.2, findings
        /// N43 and C8-2), so that the answer says it instead of implying «the
        /// whole drawing».
        /// </summary>
        private sealed class ExistingDimensionsReading
        {
            /// <summary>
            /// False when the walk broke off (a view or GetNext5 threw): the keys
            /// found so far are used, and the counts below are not a number to
            /// report — zero would be a claim.
            /// </summary>
            public bool Complete;
            /// <summary>True: the views of every sheet (IDrawingDoc.GetViews). False: the current sheet only.</summary>
            public bool EverySheet;
            /// <summary>Display dimensions left out because their annotation is hidden by hand.</summary>
            public int HiddenByHand;
            /// <summary>Views left out because they show another model or configuration.</summary>
            public int ViewsOfAnotherModel;
        }

        /// <summary>
        /// The circle is the foot of a protrusion, not the mouth of a hole: the other
        /// face the circle borders (the boss cylinder, the fillet at the
        /// foot) rises on the OUTSIDE of the planar face, whereas the wall of
        /// a hole (or its chamfer) goes down into the material. It is measured on the
        /// circles and on the ends of the other edges of that face, against the
        /// normal of the planar face. False if not understood: it stays a hole, as before.
        /// </summary>
        private static bool IsTheFootOfAStud(IEdge e)
        {
            try
            {
                var cb = (e.GetCurve() as ICurve)?.CircleParams as double[];
                if (cb == null || cb.Length < 3) return false;
                IFace2 planar = null, other = null;
                foreach (var oc in (e.GetCoEdges() as object[]) ?? new object[0])
                {
                    var f = ((oc as ICoEdge)?.GetLoop() as ILoop2)?.GetFace() as IFace2;
                    var su = f?.GetSurface() as ISurface;
                    if (su == null) continue;
                    if (su.IsPlane() && planar == null) planar = f; else other = f;
                }
                if (planar == null || other == null) return false;
                var n = ApiHelpers.NormalIfPlanar(planar);
                if (n == null || n.Length < 3) return false;
                double farther = 0;
                foreach (var o in (other.GetEdges() as object[]) ?? new object[0])
                {
                    var ed = o as IEdge;
                    var cv = ed?.GetCurve() as ICurve;
                    if (cv == null) continue;
                    var points = new List<double[]>();
                    if (cv.IsCircle()) { var cc = cv.CircleParams as double[]; if (cc != null) points.Add(cc); }
                    else
                    {
                        var pr = ApiHelpers.EdgeParameters(ed);
                        if (pr?.StartPoint is double[] a) points.Add(a);
                        if (pr?.EndPoint is double[] b) points.Add(b);
                    }
                    foreach (var p in points)
                    {
                        double h = (p[0] - cb[0]) * n[0] + (p[1] - cb[1]) * n[1] + (p[2] - cb[2]) * n[2];
                        if (Math.Abs(h) > Math.Abs(farther)) farther = h;
                    }
                }
                return farther > 1e-7;
            }
            catch { return false; }
        }

        /// <summary>
        /// Through or blind, FROM THE GEOMETRY: the hole cylinder and its other
        /// circle. If the other circle is an inner ring of a planar face the
        /// hole goes out there: through. If it is the outer ring of a planar face
        /// (the bottom) or touches a cone (the tip), it is blind, and the depth is the
        /// distance between the two circles along the axis — the one the callout writes
        /// after ↧, tip excluded. null if not understood (the cylinder ends on
        /// another cylinder, for example).
        /// </summary>
        private static bool? ThroughAllFromGeometry(IEdge border, out double depthMm)
        {
            depthMm = double.NaN;
            try
            {
                var cb = (border.GetCurve() as ICurve)?.CircleParams as double[];
                if (cb == null || cb.Length < 7) return null;
                foreach (var oc in (border.GetCoEdges() as object[]) ?? new object[0])
                {
                    var cil = ((oc as ICoEdge)?.GetLoop() as ILoop2)?.GetFace() as IFace2;
                    var sc = cil?.GetSurface() as ISurface;
                    if (sc == null || !sc.IsCylinder()) continue;
                    foreach (var o in (cil.GetEdges() as object[]) ?? new object[0])
                    {
                        var other = o as IEdge;
                        var ca = other?.GetCurve() as ICurve;
                        if (ca == null || !ca.IsCircle()) continue;
                        var cp = ca.CircleParams as double[];
                        if (cp == null || cp.Length < 7) continue;
                        double axisLength = Math.Abs((cp[0] - cb[0]) * cb[3] + (cp[1] - cb[1]) * cb[4] + (cp[2] - cb[2]) * cb[5]);
                        if (axisLength < 1e-7) continue;   // is the circle we start from
                        foreach (var oc2 in (other.GetCoEdges() as object[]) ?? new object[0])
                        {
                            var an = (oc2 as ICoEdge)?.GetLoop() as ILoop2;
                            var s = (an?.GetFace() as IFace2)?.GetSurface() as ISurface;
                            if (s == null || s.IsCylinder()) continue;   // the hole cylinder
                            if (s.IsCone()) { depthMm = Math.Round(axisLength * 1000, 3); return false; }
                            if (s.IsPlane())
                            {
                                if (!an.IsOuter()) return true;
                                depthMm = Math.Round(axisLength * 1000, 3);
                                return false;
                            }
                        }
                        return null;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// THE OUTLINE OF A ROUND PART seen along the axis: a diameter
        /// dimension, not a hole callout. Read back: it must equal the diameter of the
        /// circle — on the same circle seen edge-on `AddDiameterDimension2`
        /// says 60 for a Ø30 (measured on 26/9/2026), and a wrong and
        /// plausible number does not stay on the drawing.
        /// </summary>
        private void DimensionTheContours(
            IModelDoc2 model, IDrawingDoc drawing, List<Circle> contours, HashSet<string> already,
            double[] contour, double dimensionOffset, double planStep, List<Dictionary<string, object>> born,
            List<Dictionary<string, object>> diameters, List<string> borders, List<string> elsewhere, List<string> failed)
        {
            var done = new HashSet<string>(StringComparer.Ordinal);
            // ONE STEP HIGHER FOR EVERY DIAMETER: the outlines of a round bar are
            // concentric, and at the same height the texts were born on top of each other
            // (the stepped shaft seen end-on, 27/9/2026: four overlapping texts).
            int n = 0;
            foreach (var c in contours)
            {
                string dm = c.DiameterMm.ToString("0.##", Ci);
                string key = "hole|" + Math.Round(c.DiameterMm, 2).ToString("0.00", Ci);
                if (!done.Add(key)) continue;
                // ITS CYLINDER, if found (review of 27/9/2026): the outline
                // of a round bar is already dimensioned if THAT cylinder is, not any equal
                // Ø — a transverse Ø30 hole called out in another view does not
                // dimension the Ø30 step.
                string its = CylinderCircleKey(c.Edge);
                // A CIRCLE WITHOUT ITS CYLINDER is not an outer diameter: it is the
                // edge of a chamfer (cone-plane, the Ø28 of a chamfered Ø30 seen
                // end-on) or the tangency line of a fillet (the Ø24 at the foot of a
                // filleted Ø20 boss). Dimensioning it is a dimension that is not manufactured.
                if (its == null)
                {
                    bool onChamfer = false;
                    foreach (var oc in (c.Edge.GetCoEdges() as object[]) ?? new object[0])
                    {
                        var su = (((oc as ICoEdge)?.GetLoop() as ILoop2)?.GetFace() as IFace2)?.GetSurface() as ISurface;
                        if (su != null && (su.IsCone() || su.IsTorus())) onChamfer = true;
                    }
                    if (onChamfer)
                    {
                        borders.Add("Ø" + dm + " (border of a chamfer or a fillet: it goes with its own dimension, not with a diameter)");
                        continue;
                    }
                }
                bool dimensioned = its != null
                    ? already.Contains(its) || already.Contains("cyl?|" + key.Substring(5))
                    : already.Contains(key);
                if (dimensioned) { elsewhere.Add("outer diameter Ø" + dm + " (already dimensioned in this drawing)"); continue; }
                int dimensionsBefore = CountDimensions(drawing);
                IDisplayDimension q = null;
                try
                {
                    model.ClearSelection2(true);
                    if ((c.Edge as IEntity).Select4(false, null))
                        q = model.AddDiameterDimension2(c.SheetCenter[0] + 0.015 + n * 0.012, contour[3] + dimensionOffset * 0.8 + n * planStep, 0) as IDisplayDimension;
                }
                catch { }
                model.ClearSelection2(true);
                if (NotGrown(drawing, dimensionsBefore, q))
                {
                    failed.Add("outer diameter Ø" + dm + " (the dimension was not created)");
                    continue;
                }
                // BORN WITHOUT ITS OBJECT (Z-C21.8, finding N80): the count went up and
                // q is null. The dimension is on the sheet, so it goes on as the
                // others with its value unknown, not refused as a wrong value it
                // cannot be shown to be (see RecordBornWithoutObject).
                var described = DescribeDimension(q, c.Index);
                object read = described.ContainsKey("mm") ? described["mm"] : null;
                if (q != null && (!(read is double) || Math.Abs((double)read - c.DiameterMm) > 0.01))
                {
                    bool removed = false;
                    try
                    {
                        var an = q.GetAnnotation() as IAnnotation;
                        if (an != null && an.Select3(false, null))
                            removed = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                        model.ClearSelection2(true);
                    }
                    catch { }
                    failed.Add("outer diameter Ø" + dm + " (SOLIDWORKS wrote " + (read ?? "?") +
                                (removed ? ": I removed it)" : ": remove it)"));
                    continue;
                }
                described["orientation"] = "outer diameter";
                described["position_mm"] = WhereItEnded(q);
                if (q != null) born.Add(described);
                else RecordBornWithoutObject(born, described, failed,
                    "external diameter Ø" + dm + " (the dimension was born but SOLIDWORKS did not hand it back: " +
                    "its value was not read back, check it on the sheet)");
                diameters.Add(new Dictionary<string, object> { ["diameter_mm"] = Math.Round(c.DiameterMm, 2), ["diameter_reread_mm"] = read });
                already.Add(key);
                if (its != null) already.Add(its);
                n++;
            }
        }

        /// <summary>
        /// THE HOLES: one callout per diameter AND depth (through, blind with its
        /// depth, or unknown), with the count of equal holes in front, and the
        /// position dimensions from the two reference edges, in parallel
        /// (finding N81: the catalogue said one per diameter).
        /// </summary>
        private void DimensionTheHoles(
            IModelDoc2 model, IDrawingDoc drawing, List<Circle> circles, List<ProjectedEdge> all,
            HashSet<string> already, double[] contour, double dimensionOffset, double planStep, double scale,
            ref int below, ref int sideways, ref int onLeft, double[] sheet,
            List<Dictionary<string, object>> born, List<Dictionary<string, object>> holes,
            List<string> elsewhere, List<string> failed)
        {
            // ONE CALLOUT PER DIAMETER *AND DEPTH*: «4× Ø8 ↧5» on four Ø8
            // of which two through would send the workshop to drill them all blind.
            // The second term comes from the geometry: -1 through, -2 not
            // understood, otherwise the depth.
            var groups = new SortedDictionary<Tuple<double, double>, List<Circle>>();
            foreach (var c in circles)
            {
                var k = Tuple.Create(Math.Round(c.DiameterMm, 2),
                    c.ThroughAll == true ? -1.0 : c.ThroughAll == false && !double.IsNaN(c.DepthMm) ? Math.Round(c.DepthMm, 2) : -2.0);
                if (!groups.ContainsKey(k)) groups[k] = new List<Circle>();
                groups[k].Add(c);
            }

            var toPosition = new List<Circle>();
            // ALREADY DIMENSIONED MEANS BEFORE THIS CALL (adversarial review
            // of 27/9/2026). The groups are by diameter AND depth, and with the
            // key of the diameter alone added inside this loop the second group
            // — two blind Ø8 next to two through Ø8 — ended up «gia' quotato»:
            // no callout, no depth, no position. In the workshop all
            // four were drilled through, that is exactly the case the
            // grouping was meant to avoid.
            var alreadyBefore = new HashSet<string>(already, StringComparer.Ordinal);
            foreach (var g in groups)
            {
                string key = "hole|" + g.Key.Item1.ToString("0.00", Ci);
                // AND PER CYLINDER, when found: a step of a round bar of the
                // same Ø, dimensioned in another view, is not this hole.
                var itsItems = new List<string>();
                bool allFound = true;
                foreach (var c in g.Value)
                {
                    string k = CylinderCircleKey(c.Edge);
                    if (k == null) allFound = false; else itsItems.Add(k);
                }
                bool dimensioned = alreadyBefore.Contains("cyl?|" + key.Substring(5));
                if (allFound) { foreach (var k in itsItems) if (alreadyBefore.Contains(k)) dimensioned = true; }
                else if (alreadyBefore.Contains(key)) dimensioned = true;
                if (dimensioned)
                {
                    elsewhere.Add("holes Ø" + g.Key.Item1.ToString("0.##", Ci) + " (already dimensioned in this drawing)");
                    continue;
                }
                // The callout goes on the top left hole: it is the first one
                // read, and the callout line leaves upwards, out of the part.
                Circle head = g.Value[0];
                foreach (var c in g.Value)
                    if (c.SheetCenter[1] > head.SheetCenter[1] + 1e-6 ||
                        (Math.Abs(c.SheetCenter[1] - head.SheetCenter[1]) < 1e-6 && c.SheetCenter[0] < head.SheetCenter[0]))
                        head = c;

                int dimensionsBefore = CountDimensions(drawing);
                IDisplayDimension callout = null;
                // THE DIALOG OF THE GUIDE DOES NOT OPEN HERE (Z-C21.9, finding L15-2,
                // NOT REAL). IDrawingDoc~AddHoleCallout2.html, Remarks: «When you call
                // this method, the user must click OK in the dialog that shows the
                // system-generated hole callout». The call runs inside the try of
                // DrawingDimension that switched swInputDimValOnCreate off, and it has
                // come back in every bench case with holes run so far: the four
                // drawing cases that dimension holes (two equal holes, one through
                // and one blind; the blind hole that states its depth; the turned
                // bush; the plate with holes and chamfer), all green on library
                // 0.144.0. A modal window would have hung the COM call instead of
                // failing it.
                try
                {
                    model.ClearSelection2(true);
                    if ((head.Edge as IEntity).Select4(false, null))
                        callout = drawing.AddHoleCallout2(
                            head.SheetCenter[0] - 0.015, contour[3] + dimensionOffset * 0.8, 0) as IDisplayDimension;
                }
                catch { }
                string come = "hole callout";
                // THE COUNT DECIDES, NOT THE OBJECT (Z-C21.8, finding N80). Until
                // library 0.144.0 a null object sent the hole to the fallback even
                // when the count of dimensions had gone up: the callout was on the
                // sheet, and the fallback put a diameter dimension on the same hole.
                bool calloutNotBorn = NotGrown(drawing, dimensionsBefore, callout);
                bool fellBack = false;
                if (calloutNotBorn)
                {
                    // Without a callout (a hole that does not come from a
                    // recognisable cut), the plain diameter.
                    fellBack = true;
                    callout = null;   // an object from a call that made nothing is not the dimension
                    try
                    {
                        model.ClearSelection2(true);
                        if ((head.Edge as IEntity).Select4(false, null))
                            callout = model.AddDiameterDimension2(
                                head.SheetCenter[0] - 0.015, contour[3] + dimensionOffset * 0.8, 0) as IDisplayDimension;
                        come = "diameter dimension";
                    }
                    catch { }
                    calloutNotBorn = NotGrown(drawing, dimensionsBefore, callout);
                }
                if (calloutNotBorn)
                {
                    failed.Add("holes Ø" + g.Key.Item1.ToString("0.##", Ci) + " (the callout was not created)");
                    continue;
                }
                // BORN WITHOUT ITS OBJECT (N80): with callout null from here on, the
                // writes and reads below fail inside their own try and leave their
                // values unknown (the «N×» is then reported as not stuck, the depth
                // of a blind hole as missing), the positions are still placed as
                // dimensions of their own, and the row says why its values are null.
                string countPrefix = g.Value.Count > 1 ? g.Value.Count.ToString(Ci) + "×" : null;
                if (countPrefix != null)
                {
                    // «4× » IN FRONT of what is there: the prefix of a callout is
                    // already the diameter symbol (<MOD-DIAM>), and replacing it
                    // erased it — measured, «4× 8» came out.
                    try
                    {
                        string prefix = callout.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? "";
                        callout.SetText((int)swDimensionTextParts_e.swDimensionTextPrefix,
                                         countPrefix + " " + prefix);
                    }
                    catch { }
                }
                // THE COUNT IS READ BACK, NOT ASSUMED (Z-C21.2, finding N48).
                // IDisplayDimension~SetText.html returns nothing, and its Remarks
                // end with «NOTE: This method does not support hole callouts.» —
                // which is what AddHoleCallout2 hands back here. GetText carries
                // the same note, yet the guide's own example «Get Whether Display
                // Dimension is a Hole Callout» (VBA) reads the prefix and the suffix
                // of a hole callout with it, and the suffix below is read that way
                // since 26/9/2026. So the prefix is read again after writing it:
                // «how_many» stays the number of holes in the group (the geometry),
                // «prefix_read» is what the sheet says, and a count that did not
                // stick is a failure. Until library 0.140.0 the answer said
                // quanti = 4 whatever the callout carried, and the shop would have
                // read one Ø8.
                string prefixRead = null;
                try { prefixRead = callout.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix); } catch { }
                if (countPrefix != null &&
                    (prefixRead == null || !prefixRead.TrimStart().StartsWith(countPrefix, StringComparison.Ordinal)))
                    failed.Add("holes Ø" + g.Key.Item1.ToString("0.##", Ci) + " (the «" + countPrefix + "» in front did not stick: " +
                                (prefixRead == null ? "the prefix could not be read back" : "the prefix reads «" + prefixRead + "»") +
                                ", so the sheet does not say how many holes there are; add the count by hand)");
                // THE FALLBACK DOES NOT CARRY THE DEPTH (adversarial review of
                // 26/9/2026): if the callout is not born, the diameter dimension says
                // «Ø8» and nothing else, and a blind hole in the workshop gets drilled through. The
                // depth is then written by the bridge, from the geometry, in the
                // suffix — the same «<HOLE-DEPTH>» the callout uses.
                string depthFrom = come == "hole callout" ? "callout" : null;
                if (come == "diameter dimension" && head.ThroughAll == false && !double.IsNaN(head.DepthMm))
                {
                    try
                    {
                        callout.SetText((int)swDimensionTextParts_e.swDimensionTextSuffix,
                                         " <HOLE-DEPTH>" + head.DepthMm.ToString("0.##", Ci));
                        depthFrom = "geometry";
                    }
                    catch { }
                }
                var described = DescribeDimension(callout, head.Index);
                described["orientation"] = come;
                described["position_mm"] = WhereItEnded(callout);
                if (callout != null) born.Add(described);
                else RecordBornWithoutObject(born, described, failed,
                    "holes Ø" + g.Key.Item1.ToString("0.##", Ci) + " (the " + (fellBack ? "diameter dimension" : "hole callout") +
                    " was born but SOLIDWORKS did not hand it back: nothing could be written on it or read from it, " +
                    "check it on the sheet)");
                // THE DEPTH OF THE BLIND HOLE (entry GT12, point 1): the SOLIDWORKS
                // callout puts it in the suffix by itself — measured on 26/9/2026,
                // «<MOD-DIAM>8» + «<HOLE-DEPTH>5» for a Ø8 5 deep, «PASSANTE»
                // for the through one. It is read back and stated: a blind hole without
                // depth on the drawing gets drilled through.
                string suffix = "";
                try { suffix = callout.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix) ?? ""; } catch { }
                object depth = null;
                var mp = Regex.Match(suffix, @"<HOLE-DEPTH>\s*([0-9]+(?:[.,][0-9]+)?)");
                double pv;
                if (mp.Success && double.TryParse(mp.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, Ci, out pv))
                    depth = pv;
                bool throughAll = suffix.IndexOf("PASSANTE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                suffix.IndexOf("THRU", StringComparison.OrdinalIgnoreCase) >= 0;
                if (depth == null) depthFrom = null;
                if (head.ThroughAll == false && depth == null)
                    // A blind one, by the geometry, that leaves the drawing without
                    // depth: it is not «done», and that is stated.
                    failed.Add("holes Ø" + g.Key.Item1.ToString("0.##", Ci) + " (blind" +
                                (double.IsNaN(head.DepthMm) ? "" : ", deep " + head.DepthMm.ToString("0.##", Ci)) +
                                ", but the dimension does not carry the depth: in the workshop it would be drilled through)");
                holes.Add(new Dictionary<string, object>
                {
                    ["diameter_mm"] = g.Key.Item1,
                    ["how_many"] = g.Value.Count,
                    // The prefix as read back after writing «N×» (N48): the count on
                    // the sheet, next to the count of the geometry above.
                    ["prefix_read"] = prefixRead,
                    ["how"] = come,
                    ["diameter_reread_mm"] = described.ContainsKey("mm") ? described["mm"] : null,
                    // Null, not false, for a callout born without its object (N80):
                    // its suffix was never read.
                    ["through_all"] = callout == null ? null : come == "hole callout" ? (object)throughAll : head.ThroughAll,
                    ["depth_mm"] = depth,
                    ["depth_from"] = depthFrom,
                    ["callout_suffix"] = callout == null ? null : suffix,
                    // THE SAME THING READ FROM THE GEOMETRY, always: the hole
                    // cylinder and its other circle. Next to the one from the callout, for
                    // whoever wants to compare them.
                    ["through_all_from_geometry"] = head.ThroughAll,
                    ["depth_from_geometry_mm"] = double.IsNaN(head.DepthMm) ? null : (object)head.DepthMm,
                });
                already.Add(key);
                foreach (var k in itsItems) already.Add(k);
                toPosition.AddRange(g.Value);
            }
            if (toPosition.Count == 0) return;

            // ── THE POSITIONS, FROM THE TWO REFERENCE EDGES ─────────────
            //
            // The leftmost among the verticals and the lowest among
            // the horizontals (on a tie, the longest): they are the edges of the part
            // in this view. A chamfer is not a reference.
            ProjectedEdge sx = null, down = null;
            foreach (var s in all)
            {
                if (s.ChamferFeature != null) continue;
                if (s.Facing == "vertical")
                {
                    double x = s.FromSheet[0];
                    if (sx == null || x < sx.FromSheet[0] - 1e-6 || (Math.Abs(x - sx.FromSheet[0]) < 1e-6 && s.Length > sx.Length)) sx = s;
                }
                else if (s.Facing == "horizontal")
                {
                    double y = s.FromSheet[1];
                    if (down == null || y < down.FromSheet[1] - 1e-6 || (Math.Abs(y - down.FromSheet[1]) < 1e-6 && s.Length > down.Length)) down = s;
                }
            }

            // One column for every distinct X of the centres, one row for every Y:
            // four holes in a rectangle are two columns and two rows, four
            // dimensions. In order of distance, the nearest to the part first.
            //
            // THE DIMENSION IS ATTACHED TO THE HOLE NEAREST TO ITS SIDE (looked at
            // 25/9/2026): the first draft took any hole of the
            // row, and the callout line of the 15 started from the bottom RIGHT hole
            // and crossed the whole plate to the dimension on the left.
            if (sx != null)
            {
                var columns = new SortedDictionary<double, Circle>();
                foreach (var c in toPosition)
                {
                    double d = Math.Round((c.SheetCenter[0] - sx.FromSheet[0]) * 1000.0, 2);
                    if (d <= 0.01) continue;
                    Circle its;
                    // the horizontal ones go BELOW: the lowest hole of the column
                    if (!columns.TryGetValue(d, out its) || c.SheetCenter[1] < its.SheetCenter[1]) columns[d] = c;
                }
                foreach (var col in columns)
                    HolePosition(model, drawing, col.Value, sx, true, col.Key / scale,
                                     (col.Value.SheetCenter[0] + sx.FromSheet[0]) / 2,
                                     contour[1] - dimensionOffset - below++ * planStep, born, failed);
            }
            if (down != null)
            {
                // WHICH SIDE: where there is room INSIDE the frame. The first
                // draft always put them on the left, and with the view near the
                // edge of the sheet the 65 went out of the frame.
                var lines = new SortedDictionary<double, List<Circle>>();
                foreach (var c in toPosition)
                {
                    double d = Math.Round((c.SheetCenter[1] - down.FromSheet[1]) * 1000.0, 2);
                    if (d <= 0.01) continue;
                    if (!lines.ContainsKey(d)) lines[d] = new List<Circle>();
                    lines[d].Add(c);
                }
                double margin = 0.015;
                double lastRight = contour[2] + dimensionOffset + (sideways + lines.Count - 1) * planStep;
                double lastLeft = contour[0] - dimensionOffset - (onLeft + lines.Count - 1) * planStep;
                bool onRight = sheet == null || sheet.Length < 2 || sheet[0] <= 0
                    ? false
                    : lastRight <= sheet[0] - margin || lastLeft < margin;
                foreach (var line in lines)
                {
                    // the hole of the row nearest to the side of the dimension
                    Circle chosen = line.Value[0];
                    foreach (var c in line.Value)
                        if (onRight ? c.SheetCenter[0] > chosen.SheetCenter[0] : c.SheetCenter[0] < chosen.SheetCenter[0]) chosen = c;
                    double x = onRight
                        ? contour[2] + dimensionOffset + sideways++ * planStep
                        : contour[0] - dimensionOffset - onLeft++ * planStep;
                    HolePosition(model, drawing, chosen, down, false, line.Key / scale,
                                     x, (chosen.SheetCenter[1] + down.FromSheet[1]) / 2, born, failed);
                }
            }
        }

        /// <summary>
        /// Width and height of the current sheet, in metres, or null. They serve
        /// not to put a dimension outside the frame.
        /// </summary>
        private static double[] SheetDimensions(IDrawingDoc drawing)
        {
            try
            {
                var sheet = drawing.GetCurrentSheet() as ISheet;
                var p = sheet?.GetProperties2() as double[];
                if (p != null && p.Length >= 7 && p[5] > 0 && p[6] > 0) return new[] { p[5], p[6] };
            }
            catch { }
            return null;
        }

        /// <summary>
        /// A position dimension between the CENTRE of a hole and a reference
        /// edge, horizontal or vertical. It is read back: it must equal the
        /// distance seen on the sheet divided by the scale — if it measured to the edge
        /// of the hole instead of the centre, it would be one radius too much.
        /// </summary>
        private void HolePosition(
            IModelDoc2 model, IDrawingDoc drawing, Circle hole, ProjectedEdge reference, bool horizontal,
            double expectedMm, double x, double y,
            List<Dictionary<string, object>> born, List<string> failed)
        {
            int dimensionsBefore = CountDimensions(drawing);
            IDisplayDimension dimension = null;
            try
            {
                model.ClearSelection2(true);
                if ((hole.Edge as IEntity).Select4(false, null) && reference != null)
                {
                    // The reference edge is taken from the view, like the others.
                    var ent = reference.Entity as IEntity;
                    if (ent != null && ent.Select4(true, null))
                        dimension = (horizontal
                            ? model.AddHorizontalDimension2(x, y, 0)
                            : model.AddVerticalDimension2(x, y, 0)) as IDisplayDimension;
                }
            }
            catch { }
            if (NotGrown(drawing, dimensionsBefore, dimension))
            {
                failed.Add("position of the hole at " + expectedMm.ToString("0.##", Ci) + " mm (the dimension was not created)");
                return;
            }
            // A null dimension here was BORN WITHOUT ITS OBJECT (Z-C21.8, finding N80):
            // described with its value unknown, see RecordBornWithoutObject.
            var described = DescribeDimension(dimension, hole.Index);
            described["orientation"] = horizontal ? "horizontal position of the hole" : "vertical position of the hole";
            described["position_mm"] = WhereItEnded(dimension);
            object mm;
            if (described.TryGetValue("mm", out mm) && mm != null &&
                Math.Abs(Convert.ToDouble(mm, Ci) - expectedMm) > 0.05)
                described["warning"] = "is " + Convert.ToDouble(mm, Ci).ToString("0.##", Ci) + " instead of " +
                                          expectedMm.ToString("0.##", Ci) + ": maybe it measures to the edge of the hole and not to the center";
            if (dimension != null) born.Add(described);
            else RecordBornWithoutObject(born, described, failed,
                "hole position at " + expectedMm.ToString("0.##", Ci) + " mm (the dimension was born but SOLIDWORKS " +
                "did not hand it back: its value was not read back, check it on the sheet)");
        }

        /// <summary>
        /// THE CHAMFERS: one per feature, with the chamfer dimension
        /// (`IDrawingDoc.AddChamferDim`, «5 x 45°»), where the chamfer is seen in
        /// profile — an oblique chamfer edge on the view. The chamfer
        /// edge is selected and one of its «legs», as by hand.
        /// </summary>
        private void DimensionTheChamfers(
            IModelDoc2 model, IDrawingDoc drawing, object[] edges, List<ProjectedEdge> all,
            HashSet<string> already, double[] contour, double dimensionOffset,
            List<Dictionary<string, object>> born, List<Dictionary<string, object>> chamfers,
            List<string> elsewhere, List<string> failed)
        {
            var done = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in all)
            {
                if (s.ChamferFeature == null || s.Facing != "oblique" || done.Contains(s.ChamferFeature)) continue;
                done.Add(s.ChamferFeature);
                string key = "chamfer|" + s.ChamferFeature;
                if (already.Contains(key))
                {
                    elsewhere.Add("chamfer «" + s.ChamferFeature + "» (already dimensioned in this drawing)");
                    continue;
                }
                ProjectedEdge leg = null;
                foreach (var t in all)
                {
                    if (t.ChamferFeature != null || t.Facing == "oblique") continue;
                    foreach (var p in new[] { t.FromModel, t.ToModel })
                        foreach (var q in new[] { s.FromModel, s.ToModel })
                            if (Math.Abs(p[0] - q[0]) + Math.Abs(p[1] - q[1]) + Math.Abs(p[2] - q[2]) < 1e-7) leg = t;
                    if (leg != null) break;
                }
                if (leg == null)
                {
                    failed.Add("chamfer «" + s.ChamferFeature + "» (no edge to rest the chamfer dimension on)");
                    continue;
                }
                // The text OUTSIDE the part, on the side of the chamfer: from the centre
                // of the view towards the chamfer, beyond the outline.
                double mx = (s.FromSheet[0] + s.ToSheet[0]) / 2, my = (s.FromSheet[1] + s.ToSheet[1]) / 2;
                double cx = (contour[0] + contour[2]) / 2, cy = (contour[1] + contour[3]) / 2;
                double vx = mx - cx, vy = my - cy, l = Math.Sqrt(vx * vx + vy * vy);
                if (l < 1e-9) { vx = -1; vy = 1; l = Math.Sqrt(2); }
                double x = mx + vx / l * (dimensionOffset + 0.006), y = my + vy / l * (dimensionOffset + 0.006);

                int dimensionsBefore = CountDimensions(drawing);
                IDisplayDimension dimension = null;
                try
                {
                    model.ClearSelection2(true);
                    if ((edges[s.Index] as IEntity).Select4(false, null) &&
                        (edges[leg.Index] as IEntity).Select4(true, null))
                        dimension = drawing.AddChamferDim(x, y, 0) as IDisplayDimension;
                }
                catch { }
                if (NotGrown(drawing, dimensionsBefore, dimension))
                {
                    failed.Add("chamfer «" + s.ChamferFeature + "» (the chamfer dimension was not created)");
                    continue;
                }
                // A null dimension here was BORN WITHOUT ITS OBJECT (Z-C21.8, finding N80).
                // AddChamferDim.html, Return Value: «Pointer to the newly created
                // chamfer dimension»; when it is null and the count went up, the
                // dimension is there and its two values cannot be read: the reads
                // below fail inside their try, and angle, side and width stay null.
                var described = DescribeDimension(dimension, s.Index);
                described["orientation"] = "chamfer dimension";
                described["position_mm"] = WhereItEnded(dimension);

                // THE CHAMFER DIMENSION IS MADE OF TWO DIMENSIONS, and the first one is
                // THE ANGLE. Measured on 25/9/2026: GetDimension2(0) = 0.785 rad
                // (45°), GetDimension2(1) = 7.071 mm, the width of the chamfer
                // face — and the number on the sheet is «5 x 45°». The side that is
                // read is therefore width x cos(angle). By index it
                // reported 785.398 «mm», that is the angle in radians times a thousand:
                // they are told apart by TYPE (`swDimensionParamType_e`: linear 0,
                // angular 1), not by position.
                double? angle = null, width = null;
                for (int k = 0; k < 2; k++)
                {
                    try
                    {
                        var dk = dimension.GetDimension2(k) as IDimension;
                        if (dk == null) continue;
                        int typ = dk.GetType();
                        if (typ == (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular) angle = ApiHelpers.ReadDimensionInConfiguration(dk, "");
                        else if (typ == (int)swDimensionParamType_e.swDimensionParamTypeDoubleLinear) width = ApiHelpers.ReadDimensionInConfiguration(dk, "");
                    }
                    catch { }
                }
                object side = null;
                if (angle.HasValue && width.HasValue)
                    side = Math.Round(width.Value * Math.Cos(angle.Value) * 1000.0, 3);
                described["mm"] = side;
                var line = new Dictionary<string, object>
                {
                    ["feature"] = s.ChamferFeature,
                    ["leg_mm"] = side,
                    ["angle_deg"] = angle.HasValue ? (object)Math.Round(angle.Value * 180.0 / Math.PI, 2) : null,
                    ["face_width_mm"] = width.HasValue ? (object)Math.Round(width.Value * 1000.0, 3) : null,
                };
                chamfers.Add(line);
                if (dimension != null) born.Add(described);
                else RecordBornWithoutObject(born, described, failed,
                    "chamfer «" + s.ChamferFeature + "» (the chamfer dimension was born but SOLIDWORKS did not hand it " +
                    "back: its values were not read back, check it on the sheet)");
                already.Add(key);
            }
        }

        /// <summary>A model point on the sheet (metres), or null.</summary>
        private static double[] OnSheet(double[] p, IMathUtility mu, MathTransform tr)
        {
            try
            {
                var q = ((IMathPoint)mu.CreatePoint(p)).MultiplyTransform(tr) as IMathPoint;
                var outside = q?.ArrayData as double[];
                return outside != null && outside.Length >= 2 ? outside : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// THE ELEMENT IN THE MODEL: the axis along which the edge runs and its
        /// interval, to the hundredth of a millimetre. Two edges with the same
        /// key measure the same extent of the part, even if they are in
        /// two different views — and that is how repeated dimensioning
        /// BETWEEN views is recognised: the 10 of the front view and the 10 of the
        /// side view are the same thickness. Null for an oblique edge.
        /// </summary>
        private static string KeyInModel(double[] da, double[] a)
        {
            double dx = a[0] - da[0], dy = a[1] - da[1], dz = a[2] - da[2];
            double l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (l < 1e-9) return null;
            int axis = Math.Abs(dx) / l > 0.999 ? 0 : Math.Abs(dy) / l > 0.999 ? 1 : Math.Abs(dz) / l > 0.999 ? 2 : -1;
            if (axis < 0) return null;
            double v0 = Math.Min(da[axis], a[axis]) * 1000.0, v1 = Math.Max(da[axis], a[axis]) * 1000.0;
            return "XYZ"[axis] + "|" + Math.Round(v0, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                   "|" + Math.Round(v1, 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The name of the chamfer feature the edge belongs to
        /// (one of its two faces comes from a chamfer), or null.
        /// </summary>
        private static string TouchesAChamfer(IEdge edge)
        {
            try
            {
                var faces = edge.GetTwoAdjacentFaces2() as object[];
                if (faces == null) return null;
                foreach (var o in faces)
                {
                    var f = (o as IFace2)?.GetFeature() as IFeature;
                    if (f != null && f.GetTypeName2() == "Chamfer") return f.Name;
                }
            }
            catch { }
            return null;
        }

        /// <summary>The extent of the view now, or `null` if it cannot be read.</summary>
        private static double[] ContourOf(IView view)
        {
            try { return view.GetOutline() as double[]; }
            catch { return null; }
        }

        /// <summary>
        /// Did the extent of the view change after the dimensions were put?
        ///
        /// «sconosciuto» when one of the two readings is missing: not «no». A
        /// comparison that could not be made is not a comparison that went well.
        /// </summary>
        private static string GrownContour(double[] before, double[] after)
        {
            if (before == null || after == null || before.Length < 4 || after.Length < 4)
                return "unknown";
            for (int i = 0; i < 4; i++)
                if (Math.Abs(before[i] - after[i]) > 1e-9) return "si";
            return "no";
        }

        /// <summary>From sheet metres to millimetres, for the answer.</summary>
        private static object InMillimeters(double[] v)
        {
            if (v == null) return null;
            var outside = new object[v.Length];
            for (int i = 0; i < v.Length; i++) outside[i] = Math.Round(v[i] * 1000, 2);
            return outside;
        }

        /// <summary>Where the dimension really ended up, in sheet millimetres.</summary>
        private static object WhereItEnded(IDisplayDimension d)
        {
            // IT IS READ BACK, the point that was asked for is not reported: the value
            // that comes back from whoever did the thing is not the measurement of the thing, and
            // here it is needed precisely to be able to assert that the dimension is OUTSIDE
            // the silhouette of the view.
            try
            {
                var ann = d?.GetAnnotation() as IAnnotation;
                var q = ann?.GetPosition() as double[];
                if (q != null && q.Length >= 2)
                    return new object[] { Math.Round(q[0] * 1000, 2), Math.Round(q[1] * 1000, 2) };
            }
            catch { }
            return null;
        }

        /// <summary>
        /// A dimension BORN WITHOUT ITS OBJECT (Z-C21.8, finding N80): the count
        /// of dimensions went up and the call returned null. <see cref="NotGrown"/>
        /// is asked first, and only a dimension that did not grow is «not born»;
        /// one that grew goes on as the others, described through
        /// <see cref="DescribeDimension"/> with null (as the edge dimensions of
        /// DrawingDimension always were), and here it gets its row and a line of
        /// «edges_not_dimensioned» saying that its values were not read back.
        /// Until library 0.144.0 the other call sites asked «null or not grown»
        /// and took such a dimension for missing: the hole callout then fell
        /// back to a diameter dimension on the same hole, two annotations for
        /// one hole, and the others reported as not born a dimension that was on
        /// the sheet. The lists come in as IList because the obsolete-call
        /// contract cannot read a generic parameter type, and an Add on a
        /// receiver it cannot type is counted as undecided.
        /// </summary>
        private static void RecordBornWithoutObject(System.Collections.IList born, Dictionary<string, object> row,
                                                    System.Collections.IList failed, string failure)
        {
            born.Add(row);
            failed.Add(failure);
        }

        /// <summary>
        /// Name and VALUE of a dimension just born, the value read back from
        /// SOLIDWORKS and not what I thought I had asked for.
        ///
        /// `GetSystemValue2("")` returns **metres**, always, whatever the
        /// units of the document: it is the only reading that does not depend on how
        /// the drawing is set up. `IDimension.Value` instead follows the units of the
        /// document, so it is reported alongside but not used to compare.
        /// </summary>
        private static Dictionary<string, object> DescribeDimension(IDisplayDimension d, int edge)
        {
            var outside = new Dictionary<string, object> { ["edge"] = edge };
            if (d == null)
            {
                // The dimension is there — the count went up — but the object did not
                // come back. It is stated, instead of making the row disappear: a list
                // shorter than the count would make one look for a defect that is not
                // there. Every call that places a dimension can end here (N80), not
                // only AddDimension, so the text names none.
                outside["name"] = "(born, but the call that placed it did not hand the object back)";
                outside["mm"] = null;   // not read, not zero
                return outside;
            }
            try
            {
                var dim = d.GetDimension2(0) as IDimension;
                if (dim != null)
                {
                    try { outside["name"] = dim.FullName; } catch { }
                    try { outside["mm"] = Math.Round(ApiHelpers.ReadDimensionInConfiguration(dim, "") * 1000, 3); } catch { }
                }
            }
            catch { }
            if (!outside.ContainsKey("mm"))
                outside["mm"] = null;   // «could not read it», not «zero»
            return outside;
        }

        /// <summary>
        /// The millimetres of the dimensions born, in a row and without the index of the
        /// edge. The dimensions that could not be read back **do not go in**:
        /// a `null` among numbers would be compared as an empty string
        /// and would pass any «does not contain».
        /// </summary>
        private static object[] MillimetersInARow(List<Dictionary<string, object>> quote)
        {
            var outside = new List<object>();
            foreach (var q in quote)
            {
                object v;
                if (q.TryGetValue("mm", out v) && v != null) outside.Add(v);
            }
            return outside.ToArray();
        }

        private static string ValuesInLine(List<Dictionary<string, object>> quote)
        {
            var parts = new List<string>();
            foreach (var q in quote)
            {
                object v;
                parts.Add(q.TryGetValue("mm", out v) && v != null ? v + " mm" : "?");
            }
            return string.Join(", ", parts);
        }

        /// <summary>
        /// The view asked for as the state writes it («V2») or by name, or the first
        /// real one if none is given. Null with the reason when the view is not there; null
        /// without a reason when it is there but cannot be taken.
        ///
        /// «V2», AS THE STATE OF THE DRAWING WRITES IT (25/9/2026). The NAMES of the
        /// views depend on the language of SOLIDWORKS — «Vista del disegno2»
        /// here, «Drawing View2» in English — and a plan that copies them holds on
        /// one installation only. The identifier of the state does not: V1 is the
        /// first view of the sheet, in the same order in which the state
        /// lists them. A real name always wins: if a view is called «V2», it is
        /// that one. Extracted from `drawing_dimension` on 26/9/2026: `hole_table`
        /// uses it too, and two copies diverge.
        /// </summary>
        private static IView ResolveView(IDrawingDoc drawing, List<string> names, string wanted, out string reason)
        {
            reason = null;
            if (!string.IsNullOrWhiteSpace(wanted) && !KnownViewName(names, wanted))
            {
                var uid = System.Text.RegularExpressions.Regex.Match(wanted.Trim(), @"^[Vv](\d+)$");
                if (uid.Success)
                {
                    int k = int.Parse(uid.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (k < 1 || k > names.Count)
                    {
                        reason = "There is no such view: «" + wanted + "» is not among the " + names.Count +
                                 " views of the drawing (V1 to V" + names.Count + ").";
                        return null;
                    }
                    wanted = names[k - 1];
                }
            }
            if (!string.IsNullOrWhiteSpace(wanted) && !KnownViewName(names, wanted))
            {
                reason = "There is no view called «" + wanted + "». There are: " +
                         string.Join(", ", names) + ".";
                return null;
            }
            return ViewByName(drawing, wanted);
        }

        /// <summary>The view asked for by name, or the first real one if none is given.</summary>
        private static IView ViewByName(IDrawingDoc drawing, string wanted)
        {
            try
            {
                // The first «view» is the SHEET: the real views start after it.
                var v = (drawing.GetFirstView() as IView)?.GetNextView() as IView;
                if (string.IsNullOrWhiteSpace(wanted)) return v;
                while (v != null)
                {
                    string its = "";
                    try { its = v.GetName2() ?? ""; } catch { }
                    if (SameName(its, wanted)) return v;
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return null;
        }
    }
}
