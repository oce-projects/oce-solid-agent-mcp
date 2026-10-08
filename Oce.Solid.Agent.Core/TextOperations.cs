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
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>How far the lettering is tolerated to overhang, in mm.</summary>
        private const double ToleratedSpill = 0.01;

        private Outcome EngraveText(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string text = Str(a, "text", null);
            if (string.IsNullOrEmpty(text))
                return Failed("The «text» to engrave is missing.");

            var face = ResolveFace(docState, a, Str(a, "face_uid", ""), out string reason);
            if (face == null) return Failed(reason);

            double[] axes = FaceAxes(face, out double offsetM, out string error);
            if (axes == null) return Failed(error);

            double height = Num(a, "height_mm", 5);
            if (height <= 0)
                return Failed("A text of zero or negative height is not a text.");
            double depth = Num(a, "depth_mm", 0.5);
            if (depth <= 0)
                return Failed(
                    "A marking of zero depth leaves no mark. If you only wanted " +
                    "an annotation and not a feature, this is not the operation.");

            bool embossed = Bool(a, "raised", false);
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
                    100, // WidthFactor, in centesimi
                    100  // SpaceBetweenChars, in centesimi
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

            string location = SpillsOutside(textBox, faceBox, axes);
            if (location != null)
                return Failed(string.Format(
                    CultureInfo.InvariantCulture,
                    "«{0}» {1:0.###} mm tall occupies {2:0.##} x {3:0.##} mm and {4} I do not engrave it: " +
                    "the part that sticks out would disappear without anything giving an error, and " +
                    "a truncated lettering would remain in the part. Move the anchor, lower " +
                    "«height_mm», or shorten the text.{5}",
                    text, height,
                    textBox[3] - textBox[0],
                    textBox[4] - textBox[1],
                    location, WithoutSketch(model)));

            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            IFeature feature;
            try
            {
                feature = embossed
                    ? Extrude(model, depth, merge: true)
                    : Cut(model, throughAll: false, depthMm: depth);
            }
            catch (Exception ex)
            {
                return Failed(
                    (embossed ? "The embossing" : "The engraving") + " did not succeed: " +
                    ex.Message + WithoutSketch(model));
            }

            if (feature == null)
                return Failed(
                    (embossed ? "The embossing" : "The engraving") + " was not created, and " +
                    "SOLIDWORKS does not say why. With a text that is entirely outside the " +
                    "face it answers like this." + WithoutSketch(model));

            // ── and now we count ────────────────────────────────────────
            double? after = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);
            if (after == null || bodiesAfter < 0)
                return Succeeded(feature, new Dictionary<string, object>
                {
                    ["text"] = text,
                    ["height_mm"] = height,
                    ["depth_mm"] = depth,
                    ["raised"] = embossed,
                    ["contours"] = contours,
                    ["width_written_mm"] = Round(textBox[3] - textBox[0]),
                    ["height_written_mm"] = Round(textBox[4] - textBox[1]),
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

            if (bodiesAfter != bodiesBefore)
                return Failed(
                    "The bodies went from " + bodiesBefore + " to " + bodiesAfter +
                    ": a marking must not split or detach anything. Withdraw it — " +
                    Sentences.HowToWithdrawIt + ".");

            double lettersArea = moved / depth;
            var measurements = new Dictionary<string, object>
            {
                ["text"] = text,
                ["height_mm"] = height,
                ["depth_mm"] = depth,
                ["raised"] = embossed,
                ["contours"] = contours,
                ["width_written_mm"] = Round(textBox[3] - textBox[0]),
                ["height_written_mm"] = Round(textBox[4] - textBox[1]),
                [embossed ? "material_added_mm3" : "material_removed_mm3"] = Round(moved),
                // THE AREA OF THE LETTERS, which is the volume divided by the depth.
                // It cannot be predicted from the string -- it depends on the font -- but
                // it goes with the SQUARE of the height, and that ratio is the only thing
                // that two different markings have in common.
                ["area_letters_mm2"] = Round(lettersArea),
                ["area_over_height_squared"] = Round(lettersArea / (height * height), 4),
                ["bodies"] = bodiesAfter,
            };
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
                "look at whether the lettering ends up over another feature.",
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

            _log("[agent] «" + text + "» " + (embossed ? "embossed " : "engraved ") +
                 depth + " mm: " + Round(moved) + " mm3, lettering " +
                 Round(textBox[3] - textBox[0]) + " mm wide");

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
                    var curva = border.GetCurve() as ICurve;
                    if (curva == null) continue;
                    for (int i = 1; i < 6; i++)
                    {
                        double t = p[6] + (p[7] - p[6]) * i / 6.0;
                        var point = curva.Evaluate(t) as double[];
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
