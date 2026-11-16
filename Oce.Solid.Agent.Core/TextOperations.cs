using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE MARKING ON A FACE. (entry GP13)
    ///
    /// Serial number, mounting direction, customer name, «ALTO»: it is
    /// what the shop asks for on one part in two, and up to here the
    /// catalog could not write anything on the part.
    ///
    /// ── IT IS NOT A SINGLE API ─────────────────────────────────────────
    ///
    ///   IModelDoc2.InsertSketchText(Ptx, Pty, Ptz, Text, Alignment,
    ///                               FlipDirection, HorizontalMirror,
    ///                               WidthFactor, SpaceBetweenChars)
    ///                               -> ISketchText
    ///
    /// makes the SKETCH. The material is removed (or added) by the cut or
    /// the extrusion that come afterwards, which are the same ones already measured.
    ///
    /// Measured on 16/9/2026: **it opens no window** (it is not like
    /// `AddDimension`), and it works with `AddToDB = true` too.
    ///
    /// ── THE DEFECT THAT GIVES NO ERROR, AND HERE IT IS A BIG ONE ───────
    ///
    /// «PROVA» 10 mm high, engraved 2 mm, on a 100x100 square:
    ///
    ///     anchored at (0, 0)    ->  removed 230.70 mm3
    ///     anchored at (30, 10)  ->  removed 111.20 mm3
    ///
    /// **Half of the lettering falls outside the face and vanishes silently.**
    /// The feature is born green, it is called `Cut-Extrude1`, and in the part there is
    /// «PRO». No error, anywhere — and on a serial number there is
    /// nothing to notice it by looking at the tree.
    ///
    /// Entirely outside, instead, is already caught: `FeatureCut4` returns `null`. It is the
    /// easy case. What matters is the PARTIAL overlap.
    ///
    /// ── HOW IT IS CAUGHT: THE LETTERING'S BOX ─────────────────────────
    ///
    /// `ISketchText.GetEdges()` returns the contours of the letters — 56 `IEdge`
    /// for «PROVA» — and from those one reads **where the lettering really ends**,
    /// in model coordinates and in meters. It is checked BEFORE cutting, and
    /// refused.
    ///
    /// Before, it could not be known: the width of a lettering is not computed
    /// from the string, it depends on the font. Measured, instead, it is known — and for
    /// «PROVA» in Century Gothic 10 mm high it is **47.05 x 10.51 mm**.
    ///
    /// ── WHAT HAS A NUMBER, AND WHAT DOES NOT ──────────────────────────
    ///
    /// The area of the letters cannot be predicted. But it goes with the **square
    /// of the height**, and it is measured:
    ///
    ///     height  5 mm  ->  area 28.83 mm2   ->  area/h2 = 1.1533
    ///     height 10 mm  ->  area 115.35 mm2  ->  area/h2 = 1.1535
    ///     height 20 mm  ->  area 265.88 mm2  ->  area/h2 = 0.6647   <- broken
    ///
    /// The third number is broken because at 20 mm «PROVA» is 94 mm wide and on a
    /// face of 100 anchored at x=0 it does not fit. **The same law that holds the
    /// first two is the one that denounces the third**: two measures that
    /// check each other, and neither of the two is a constant written by
    /// hand.
    ///
    /// ── `Alignment` MOVES NOTHING ON FREE TEXT ───────────────────────
    ///
    /// Narrowed in Z-C21.9 (finding C6-2). IModelDoc2~InsertSketchText.html:
    /// «Alignment 0 = Left 1 = Center 2 = Right 3 = Fully justified». It is the
    /// justification of the text, not a way to make the text follow a curve:
    /// that takes the edge or sketch segment selected with mark 1 (Remarks:
    /// «If you want to align the text with an edge or sketch segment, you must
    /// select the edge or sketch segment with a mark of 1»), and the guide's
    /// own example Align_Text_with_Line selects the line with mark 1 and still
    /// passes Alignment 0.
    ///
    /// Measured on 16/9/2026 on FREE text, one line and nothing selected, which
    /// is what engrave_text writes: the five values 0..4 gave the same box to
    /// the thousandth of a millimetre (4 is outside the documented range). So
    /// on free text the justification has nothing to move; with a curve
    /// selected it was not measured, and this operation never selects one.
    /// Hence the argument is not exposed, and 0 is passed. Until Z-C21.9 this
    /// note said the call ignores the argument altogether, which the page
    /// does not support.
    ///
    /// ── 0.131.0: THE FACE OF THE SAME PLAN, THROUGH, CENTRED ──────────────
    ///
    /// The live case of 12/10/2026 was «a 150x150x5 plate with the number 35 cut
    /// through, centred»: three things this operation could not do.
    ///
    ///   * `face_ref` {step, kind}: the face is named before it exists, with the
    ///     same argument and the same resolution (SwOperations.ResolveFace,
    ///     References.Face) as hole_wizard. The plate and its text are ONE plan.
    ///   * `through_all`: FeatureCut4 with swEndCondThroughAll (SwOperations.Cut, the
    ///     call every through cut makes). The same check first (the letters inside the
    ///     face, refused with the overflow measured), and after it one more: a letter
    ///     with a hole inside (0 6 8 9 A B D O P Q R) leaves its inside as a body of its own
    ///     once the cut goes through, so the cut is taken away (RemoveCut) and refused,
    ///     saying so. `depth_mm` of the answer is read from the walls the cut made
    ///     (IFeature~GetFaces.html, the true box of each).
    ///   * `centered`: x_mm/y_mm become the CENTRE of the lettering (default: the centre
    ///     of the face). The width of a text is not in the string, so it is drawn at the
    ///     centre asked for, its box is read, and ISketchText::SetCoordinates moves it
    ///     (ISketchText~SetCoordinates.html); the box is read AGAIN and a text that is not
    ///     within 0.05 mm of the centre is refused. Probe: Checks\TextThrough.
    ///
    /// ALSO FIXED HERE: `width_written_mm`, `height_written_mm` and the sizes in the
    /// overflow message were read on X and Y whatever the face. They are right only
    /// for a face whose normal is Z; on the top face of a plate (normal Y) the height
    /// came out as the thickness of the sketch, zero. They are read on the two axes of the
    /// face now.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>How far the lettering is tolerated to overhang, in mm.</summary>
        private const double ToleratedSpill = 0.01;

        /// <summary>
        /// How far from the requested centre the lettering may end up after it
        /// was moved there, in mm. Above it the text is not where it was asked to
        /// be and the step is refused: a centring that is 2 mm off is a defect that
        /// gives no error, and the box is read twice (before and after the move),
        /// so the comparison costs nothing.
        /// </summary>
        private const double CentringToleranceMm = 0.05;

        private Outcome EngraveText(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string text = Str(a, "text", null);
            if (string.IsNullOrEmpty(text))
                return Failed("The «text» to engrave is missing.");

            bool through = Bool(a, "through_all", false);
            bool embossed = Bool(a, "raised", false);
            bool centered = Bool(a, "centered", false);

            // ARGUMENTS THAT CONTRADICT EACH OTHER ARE REFUSED, not obeyed in part: a
            // «depth_mm: 0.5» next to «through_all» would be IGNORED in silence, and the
            // person who wrote it would believe a half millimetre mark had been made.
            if (through && embossed)
                return Failed(
                    "«raised» and «through_all» cannot go together: a text cannot be " +
                    "embossed and cut through the part at the same time. Leave «raised» out to cut " +
                    "it through, or leave «through_all» out to emboss it.");
            if (through && HasReference(a, "depth_mm"))
                return Failed(
                    "«through_all» cuts the whole thickness, so «depth_mm» would be ignored. " +
                    "Leave «depth_mm» out for a text cut through, or leave «through_all» out " +
                    "to engrave to that depth.");

            var face = ResolveFace(docState, a, Str(a, "face_uid", ""), out string reason);
            if (face == null) return Failed(reason);

            double[] axes = FaceAxes(face, out double offsetM, out string error);
            if (axes == null) return Failed(error);
            int axisU = (int)axes[0], axisV = (int)axes[1];

            double height = Num(a, "height_mm", 5);
            if (height <= 0)
                return Failed("A text of zero or negative height is not a text.");
            double depth = Num(a, "depth_mm", 0.5);
            if (!through && depth <= 0)
                return Failed(
                    "A marking of zero depth leaves no mark. If you only wanted " +
                    "an annotation and not a feature, this is not the operation.");

            double xMm = Num(a, "x_mm", 0);
            double yMm = Num(a, "y_mm", 0);
            string font = Str(a, "font", null);
            bool bold = Bool(a, "bold", false);

            int bodiesBefore = CountSolidBodies(model);
            double? volumeBefore = VolumeMm3(model);
            if (bodiesBefore < 0 || volumeBefore == null)
                return Failed(VolumeNotReadBefore("marking the face"));

            // The FACE's box, to know where the lettering may sit.
            double[] faceBox = FaceBox(face);
            // Without the face box the overflow check (SpillsOutside) cannot run,
            // and a truncated inscription would be engraved without a word.
            // Refuse before touching the part (S6: the true box can fail where
            // IFace2.GetBox always answered).
            if (faceBox == null)
                return Failed(
                    "I cannot measure the face: its true bounding box did not answer, so I " +
                    "cannot check that the text stays inside it. Nothing was changed.");
            double faceArea = 0;
            try { faceArea = face.GetArea() * 1e6; } catch { }

            // CENTRED: «x_mm»/«y_mm» are where the CENTRE of the lettering must end up, and
            // the centre of the face when they are not given. The text is first drawn
            // there and then moved by what the measure of its box says.
            double targetX = xMm, targetY = yMm;
            if (centered)
            {
                if (!HasReference(a, "x_mm")) targetX = (faceBox[axisU] + faceBox[3 + axisU]) / 2;
                if (!HasReference(a, "y_mm")) targetY = (faceBox[axisV] + faceBox[3 + axisV]) / 2;
                xMm = targetX;
                yMm = targetY;
            }

            // The names of the tree before the sketch: the sketch engraved below
            // is the one born since (scheme S7, SwOperations.JustCreatedSketch),
            // not the last sketch of the tree.
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectFace(face)) return Failed("I could not select the face.");

            var sketches = model.SketchManager;
            bool addToDbBefore = false, displayBefore = false;
            try { addToDbBefore = sketches.AddToDB; displayBefore = sketches.DisplayWhenAdded; } catch { }

            ISketchText written = null;
            double[] textBox = null;
            int contours = 0;
            try
            {
                sketches.InsertSketch(true);
                try { sketches.AddToDB = true; sketches.DisplayWhenAdded = false; } catch { }

                var point = PointInSketch(model, axes, offsetM, xMm, yMm, out error);
                if (point == null) throw new InvalidOperationException(error);

                written = model.InsertSketchText(
                    point[0], point[1], point[2],
                    text,
                    // Alignment (justification): on free text it moves nothing,
                    // measured; see the note at the top.
                    0,
                    0,   // FlipDirection
                    0,   // HorizontalMirror
                    100, // WidthFactor, in hundredths
                    100  // SpaceBetweenChars, in hundredths
                ) as ISketchText;
                if (written == null)
                    throw new InvalidOperationException(
                        "SOLIDWORKS did not create the sketch text, and does not say why.");

                var sheetFormat = written.GetTextFormat() as ITextFormat;
                if (sheetFormat == null)
                    throw new InvalidOperationException(
                        "I cannot read the format of the text: without it, the height " +
                        "would stay the default one and you would think you had chosen it.");
                sheetFormat.CharHeight = Units.M(height);
                sheetFormat.Bold = bold;
                if (!string.IsNullOrWhiteSpace(font)) sheetFormat.TypeFaceName = font;
                if (!written.SetTextFormat(false, sheetFormat))
                    throw new InvalidOperationException(
                        "SOLIDWORKS refused the text format (height " + height +
                        " mm" + (font == null ? "" : ", font «" + font + "»") + ").");

                // AND IT IS REREAD. `CharHeight` is the only thing that tells a
                // marking to be read from one to be guessed.
                var reread = written.GetTextFormat() as ITextFormat;
                double trueHeight = reread == null ? -1 : Units.Mm(reread.CharHeight);
                if (Math.Abs(trueHeight - height) > 0.001)
                    throw new InvalidOperationException(string.Format(
                        CultureInfo.InvariantCulture,
                        "I asked for letters {0:0.###} mm high and on reading back I find {1}.",
                        height,
                        trueHeight < 0 ? "nothing"
                            : trueHeight.ToString("0.###", CultureInfo.InvariantCulture) + " mm"));

                textBox = CurvesBox(written, out contours);

                // ── CENTRING: MEASURE, MOVE, MEASURE AGAIN ──────────────────────
                //
                // The width of a text is not in the string: only the box of the letters SOLIDWORKS
                // drew says where its centre is. ISketchText::SetCoordinates moves the text
                // (guide: ISketchText~SetCoordinates.html, «Sets the position of this sketch
                // text»; the point is given in SKETCH coordinates, hence PointInSketch as for
                // the insertion). The box is READ AGAIN afterwards and compared with the centre
                // asked for: a move that did not happen, or that happened by a different
                // amount, is a refusal and not a centred text that is not centred.
                if (centered && textBox != null)
                {
                    double dx = targetX - (textBox[axisU] + textBox[3 + axisU]) / 2;
                    double dy = targetY - (textBox[axisV] + textBox[3 + axisV]) / 2;
                    if (Math.Abs(dx) > 1e-6 || Math.Abs(dy) > 1e-6)
                    {
                        var shifted = PointInSketch(model, axes, offsetM, xMm + dx, yMm + dy, out error);
                        if (shifted == null) throw new InvalidOperationException(error);
                        if (!written.SetCoordinates(shifted[0], shifted[1], shifted[2]))
                            throw new InvalidOperationException(
                                "SOLIDWORKS refused to move the text to centre it.");
                        textBox = CurvesBox(written, out contours);
                        if (textBox == null)
                            throw new InvalidOperationException(
                                "I moved the text to centre it and then I could not read where it is.");
                        double offU = (textBox[axisU] + textBox[3 + axisU]) / 2 - targetX;
                        double offV = (textBox[axisV] + textBox[3 + axisV]) / 2 - targetY;
                        if (Math.Abs(offU) > CentringToleranceMm || Math.Abs(offV) > CentringToleranceMm)
                            throw new InvalidOperationException(string.Format(
                                CultureInfo.InvariantCulture,
                                "I moved the text to centre it and it is still off by {0:0.###} x {1:0.###} mm " +
                                "from the centre asked for: I do not engrave a text that is not where you said.",
                                offU, offV));
                    }
                }
            }
            catch (Exception ex) { error = ex.Message; written = null; }
            finally
            {
                try { sketches.AddToDB = addToDbBefore; sketches.DisplayWhenAdded = displayBefore; } catch { }
                try { sketches.InsertSketch(false); } catch { }
            }
            if (written == null)
                return Failed((error ?? "the text was not created") + WithoutSketch(model));

            // ── AND HERE WE LOOK WHETHER IT FITS, BEFORE CUTTING ─────────────
            //
            // Afterwards it is no longer possible: the feature is green and the extra
            // letters are not there. The sketch is taken away — a part with an
            // orphan sketch that nobody sees is worse than a refusal.
            if (textBox == null)
                return Failed(
                    "I cannot read where the lettering ends (" + contours + " contours " +
                    "read). Without that, I cannot tell you whether it fits on the face, and " +
                    "a lettering cut in half gives no error." + WithoutSketch(model));

            // THE WIDTH AND THE HEIGHT OF THE LETTERING are read on the two axes of the
            // FACE, not on X and Y: up to 0.130.2 they were textBox[3]-textBox[0] and
            // textBox[4]-textBox[1], right only on a face whose normal is Z. On the top
            // face of a plate (normal Y) the «height» was the thickness of the sketch, zero.
            double textWidth = textBox[3 + axisU] - textBox[axisU];
            double textHeight = textBox[3 + axisV] - textBox[axisV];

            string location = SpillsOutside(textBox, faceBox, axes);
            if (location != null)
                return Failed(string.Format(
                    CultureInfo.InvariantCulture,
                    "«{0}» {1:0.###} mm tall occupies {2:0.##} x {3:0.##} mm and {4} I do not {6} it: " +
                    "the part that sticks out would disappear without anything giving an error, and " +
                    "a truncated lettering would remain in the part. {7}{5}",
                    text, height,
                    textWidth,
                    textHeight,
                    location, WithoutSketch(model),
                    through ? "cut" : "engrave",
                    centered
                        ? "Lower «height_mm» or shorten the text: centred, moving the anchor will not help."
                        : "Move the anchor, lower «height_mm», or shorten the text."));

            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            IFeature feature;
            try
            {
                feature = embossed
                    ? Extrude(model, depth, merge: true)
                    : Cut(model, throughAll: through, depthMm: depth);
            }
            catch (Exception ex)
            {
                return Failed(
                    (embossed ? "The embossing" : through ? "The cut" : "The engraving") +
                    " did not succeed: " + ex.Message + WithoutSketch(model));
            }

            if (feature == null)
                return Failed(
                    (embossed ? "The embossing" : through ? "The cut" : "The engraving") +
                    " was not created, and " +
                    "SOLIDWORKS does not say why. With a text that is entirely outside the " +
                    "face it answers like this." + WithoutSketch(model));

            // THE CENTRE WHERE THE LETTERING REALLY IS, in the coordinates of the face's two
            // axes (the same numbers as «x_mm» and «y_mm»), read from the box measured
            // BEFORE the cut: after it the sketch is consumed.
            var centre = new object[]
            {
                Round((textBox[axisU] + textBox[3 + axisU]) / 2),
                Round((textBox[axisV] + textBox[3 + axisV]) / 2),
            };

            // HOW DEEP A CUT THROUGH WENT. «Through» has no depth to ask for, and the
            // thickness of the part is not the depth of THIS cut when there is a step under
            // the text: the walls the cut made say it. Null if they cannot be read.
            double? cutDepth = through ? DepthOfTheWalls(feature, (int)axes[2]) : (double?)depth;

            // ── and now we count ────────────────────────────────────────
            double? after = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);
            if (after == null || bodiesAfter < 0)
                return Succeeded(feature, new Dictionary<string, object>
                {
                    ["text"] = text,
                    ["height_mm"] = height,
                    ["depth_mm"] = NumberOrNull(cutDepth, 3),
                    ["raised"] = embossed,
                    ["through_all"] = through,
                    ["contours"] = contours,
                    ["width_written_mm"] = Round(textWidth),
                    ["height_written_mm"] = Round(textHeight),
                    ["centre_mm"] = centre,
                    [embossed ? "material_added_mm3" : "material_removed_mm3"] = null,
                    ["bodies"] = NumberOrNull(bodiesAfter),
                    ["volume_not_reread"] = NoteVolumeNotReread,
                }, FaceNormal(face));
            double moved = embossed ? after.Value - volumeBefore.Value : volumeBefore.Value - after.Value;

            if (moved <= 0)
                return Failed(
                    "The marking did not " + (embossed ? "add" : "remove") +
                    " material: the volume " +
                    (Math.Abs(moved) < 1e-9 ? "stayed the same" : "went the other way") +
                    ". " + (embossed
                        ? "Usually it means that the embossing grew INTO the part."
                        : "Usually it means that the engraving went outward.") +
                    " Withdraw it — " + Sentences.HowToWithdrawIt + ".");

            // A TEXT CUT THROUGH THAT LEAVES PIECES BEHIND. A letter with a hole inside (0 6 8 9
            // A B D O P Q R) has an inside that touches nothing once the cut goes through: it
            // falls out as a body of its own. This is not a surprise to be reported after the
            // fact, it is the one certain consequence of the request, so the cut is TAKEN AWAY
            // (RemoveCut reads the tree and the volume again) and the refusal says why.
            if (through && bodiesAfter > bodiesBefore)
                return Failed(string.Format(
                    CultureInfo.InvariantCulture,
                    "«{0}» cut right through the part would leave {1} loose piece{2}: it has letters " +
                    "with a hole inside (as 0, 6, 8, 9, A, B, D, O, P, Q, R do), and the inside of such a " +
                    "letter is attached to nothing once the cut goes through, so it falls out as a body of " +
                    "its own. A text cut through must be made of letters without a hole: use another text, " +
                    "or leave «through_all» out to engrave it to a depth instead.{3}",
                    text, bodiesAfter - bodiesBefore, bodiesAfter - bodiesBefore == 1 ? "" : "s",
                    RemoveCut(model, feature, volumeBefore)));

            if (bodiesAfter != bodiesBefore)
                return Failed(
                    "The bodies went from " + bodiesBefore + " to " + bodiesAfter +
                    ": a marking must not split or detach anything. Withdraw it — " +
                    Sentences.HowToWithdrawIt + ".");

            // The area is the volume over the depth that really was cut (0.5 by default,
            // the thickness read from the walls for a cut through). If the depth cannot be read the area is
            // NOT guessed from the box of the part: it is null, and the answer says so.
            double depthCut = cutDepth.GetValueOrDefault();
            double? lettersArea = depthCut > 0 ? moved / depthCut : (double?)null;
            double area = lettersArea.GetValueOrDefault();
            var measurements = new Dictionary<string, object>
            {
                ["text"] = text,
                ["height_mm"] = height,
                ["depth_mm"] = NumberOrNull(cutDepth, 3),
                ["raised"] = embossed,
                ["through_all"] = through,
                ["contours"] = contours,
                ["width_written_mm"] = Round(textWidth),
                ["height_written_mm"] = Round(textHeight),
                ["centre_mm"] = centre,
                [embossed ? "material_added_mm3" : "material_removed_mm3"] = Round(moved),
                // THE AREA OF THE LETTERS, which is the volume divided by the depth.
                // It cannot be predicted from the string -- it depends on the font -- but
                // it goes with the SQUARE of the height, and that ratio is the only thing
                // that two different markings have in common.
                ["area_letters_mm2"] = lettersArea.HasValue ? (object)Round(area) : null,
                ["area_over_height_squared"] =
                    lettersArea.HasValue ? (object)Round(area / (height * height), 4) : null,
                ["bodies"] = bodiesAfter,
            };
            if (!cutDepth.HasValue)
                measurements["depth_not_read"] =
                    "I could not read how deep the cut went from the walls it made, so the area of the " +
                    "letters is null: the volume above is what was removed.";
            if (!string.IsNullOrWhiteSpace(font)) measurements["font"] = font;

            // WHAT I DID NOT LOOK AT, said at every call and not once in the
            // catalog: whoever reads the answer may never have read it.
            double boxArea = 0;
            if (faceBox != null)
            {
                var sides = SidesOnPlane(faceBox, axes);
                boxArea = sides[0] * sides[1];
            }
            bool fullFace = boxArea > 0 && Math.Abs(faceArea - boxArea) / boxArea < 0.01;
            measurements["what_it_does_not_check"] = string.Format(
                CultureInfo.InvariantCulture,
                "I checked that the lettering stays inside the BOX of the face{0} And I do not " +
                "look at whether the lettering ends up over another feature." +
                (through
                    ? " The text was cut through EVERYTHING behind the face, along its normal: on a hollow " +
                      "part, or with more material behind the face, that is more than the one plate " +
                      "you may have in mind."
                    : ""),
                fullFace
                    ? string.Format(CultureInfo.InvariantCulture,
                        ", and this face fills it entirely " +
                        "(area {0:0.##} mm2 against {1:0.##} of box), so the check is complete.",
                        faceArea, boxArea)
                    : string.Format(CultureInfo.InvariantCulture,
                        ", but this face does NOT fill its box " +
                        "(area {0:0.##} mm2 against {1:0.##} of box): it has a hole " +
                        "or a re-entrant contour, and a letter could fall there " +
                        "without my noticing. Look at the material removed.",
                        faceArea, boxArea));

            _log("[agent] «" + text + "» " + (embossed ? "embossed " : through ? "cut through " : "engraved ") +
                 (through ? "" : depth + " mm: ") + Round(moved) + " mm3, lettering " +
                 Round(textWidth) + " mm wide");

            return Succeeded(feature, measurements, FaceNormal(face));
        }

        // =================================================================

        /// <summary>
        /// The box of the letters' contours, in mm and in the MODEL's
        /// coordinates: {minX, minY, minZ, maxX, maxY, maxZ}.
        ///
        /// `ISketchText.GetEdges()` returns one `IEdge` for each contour — 56
        /// for «PROVA», because every letter has an outer one and the round ones also
        /// the inner one. Of each, the two ends **and five points
        /// in between** are looked at: an arc lies outside the segment joining its
        /// ends, and the belly of an «O» is exactly the point that
        /// would overhang first.
        /// </summary>
        private static double[] CurvesBox(ISketchText written, out int contours)
        {
            contours = 0;
            object[] borders;
            try { borders = written.GetEdges() as object[]; }
            catch { return null; }
            if (borders == null || borders.Length == 0) return null;

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (var o in borders)
            {
                var border = o as IEdge;
                if (border == null) continue;
                double[] p;
                try { p = ApiHelpers.EdgeParameterArray(border); }
                catch { continue; }
                if (p == null || p.Length < 8) continue;
                contours++;
                Widen(p[0], p[1], p[2], ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                Widen(p[3], p[4], p[5], ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                try
                {
                    var edgeCurve = border.GetCurve() as ICurve;
                    if (edgeCurve == null) continue;
                    for (int i = 1; i < 6; i++)
                    {
                        double t = p[6] + (p[7] - p[6]) * i / 6.0;
                        var point = edgeCurve.Evaluate(t) as double[];
                        if (point != null && point.Length >= 3)
                            Widen(point[0], point[1], point[2],
                                    ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                    }
                }
                catch { }
            }
            if (contours == 0) return null;
            return new[]
            {
                Units.Mm(minX), Units.Mm(minY), Units.Mm(minZ),
                Units.Mm(maxX), Units.Mm(maxY), Units.Mm(maxZ),
            };
        }

        private static void Widen(
            double x, double y, double z,
            ref double minX, ref double minY, ref double minZ,
            ref double maxX, ref double maxY, ref double maxZ)
        {
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
            if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
        }

        /// <summary>
        /// How deep a cut went, in mm, read from the faces the cut made: the extent of the
        /// union of their TRUE boxes along the axis of the face's normal. Null if no face
        /// could be measured (a depth guessed from the box of the part would be a number
        /// without a source).
        ///
        /// IFeature~GetFaces.html gives the faces of the feature, which for a cut are the
        /// walls of the letters; <see cref="ApiHelpers.TrueBoundingBox(IFace2)"/> measures
        /// each one (IFace2~CreateSheetBody.html + GetExtremePoint, S6). Probe
        /// Checks\TextThrough measured it equal to the thickness of the plate.
        /// </summary>
        private static double? DepthOfTheWalls(IFeature cut, int axis)
        {
            object[] faces;
            try { faces = cut.GetFaces() as object[]; }
            catch { return null; }
            if (faces == null) return null;
            double low = double.MaxValue, high = double.MinValue;
            foreach (var o in faces)
            {
                double[] box;
                try { box = ApiHelpers.TrueBoundingBox(o as IFace2); }
                catch { continue; }
                if (box == null || box.Length < 6) continue;
                low = Math.Min(low, box[axis]);
                high = Math.Max(high, box[3 + axis]);
            }
            if (high <= low) return null;
            return Units.Mm(high - low);
        }

        /// <summary>The box of a face, in mm and in model coordinates.</summary>
        private static double[] FaceBox(IFace2 face)
        {
            try
            {
                var b = ApiHelpers.TrueBoundingBox(face);   // S6: not IFace2.GetBox, approximate
                if (b == null || b.Length < 6) return null;
                var outside = new double[6];
                for (int i = 0; i < 6; i++) outside[i] = Units.Mm(b[i]);
                return outside;
            }
            catch { return null; }
        }

        /// <summary>The two sides of the box on the face's plane, in mm.</summary>
        private static double[] SidesOnPlane(double[] box, double[] axes)
        {
            int a0 = (int)axes[0], a1 = (int)axes[1];
            return new[]
            {
                box[3 + a0] - box[a0],
                box[3 + a1] - box[a1],
            };
        }

        /// <summary>
        /// Where the lettering leaves the face, or null if it fits.
        ///
        /// The TWO axes of the face's plane are looked at, not all three: the
        /// third is the thickness, the lettering fits by construction, and
        /// comparing it would give a refusal over a thousandth of rounding.
        /// </summary>
        private static string SpillsOutside(double[] text, double[] face, double[] axes)
        {
            if (face == null) return null;
            var names = new[] { "X", "Y", "Z" };
            var outside = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                int axis = (int)axes[i];
                if (text[axis] < face[axis] - ToleratedSpill)
                    outside.Add(string.Format(CultureInfo.InvariantCulture,
                        "sticks out by {0:0.##} mm past the bottom {1} edge",
                        face[axis] - text[axis], names[axis]));
                if (text[3 + axis] > face[3 + axis] + ToleratedSpill)
                    outside.Add(string.Format(CultureInfo.InvariantCulture,
                        "sticks out by {0:0.##} mm past the top {1} edge",
                        text[3 + axis] - face[3 + axis], names[axis]));
            }
            if (outside.Count == 0) return null;
            return string.Join(", ", outside) + " of the face.";
        }

        private static double Round(double v, int digits = 2)
        {
            return Math.Round(v, digits);
        }
    }
}
