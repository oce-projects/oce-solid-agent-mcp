using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE TEXT OF THE DIMENSIONS, AND WHERE IT FALLS (entry GT12, point 3).
    ///
    /// ── THE EXTENT IS READ, NOT ESTIMATED (26/9/2026, `probe0089 ciecotav`) ──
    ///
    /// GT11 had written «the extent of the text of a dimension cannot be read from
    /// anywhere». By reflection on the interop there is no `GetExtent` on
    /// dimensions (there is only on `INote`); but `IDisplayDimension.GetDisplayData()`
    /// returns an `IDisplayData` that for each text gives position, height,
    /// angle, reference point and — `GetTextInBoxWidthAtIndex` — the
    /// WIDTH, even without a box. Measured: « 10 » 7.76 mm, « 120 »
    /// 10.34 mm, height 3.5; reference 1 = `swLOWER_LEFT` (the position is
    /// the lower left corner of the text, in its direction).
    ///
    /// With those, the true rectangle of every text on the sheet: it is looked at whether it
    /// falls on another text, or on the edges of its own view.
    /// </summary>
    public partial class SwOperations
    {
        private sealed class DimensionText
        {
            public string View, Name, Text;
            public double[][] Angles;    // four points on the sheet, metres
        }

        /// <summary>
        /// The texts of all the dimensions of the drawing, with their rectangle; null
        /// when the walk over views and dimensions broke off (Z-C21.8, finding
        /// N47). GetFirstView, GetNextView, GetFirstDisplayDimension5 and
        /// GetNext5 sit outside the try of each dimension: an exception there
        /// used to end the walk with the texts read so far, handed over as all
        /// the texts of the drawing, and drawing_dimension counted overlaps on
        /// that part as if it were the whole. A dimension whose texts cannot be
        /// read is still skipped inside the walk, as before.
        /// </summary>
        private static List<DimensionText> DimensionTexts(IDrawingDoc drawing)
        {
            var outside = new List<DimensionText>();
            try
            {
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    string viewName = "";
                    try { viewName = v.GetName2() ?? ""; } catch { }
                    var dd = v.GetFirstDisplayDimension5() as IDisplayDimension;
                    while (dd != null)
                    {
                        try
                        {
                            var data = dd.GetDisplayData() as IDisplayData;
                            // ZERO-E-ZERO: a dimension without display data has no texts to
                            // compare, and none is invented; if none can be read,
                            // drawing_dimension says null («I do not know»), not «nobody overlaps».
                            int n = data == null ? 0 : data.GetTextCount();
                            // A POINT ON THE SHEET, NOT AN OFFSET (Z-C21.3, finding C3-1,
                            // NOT REAL). Every GetTextPositionAtIndex page says «Gets the
                            // text item offset relative to the note text point»; for the
                            // texts of a display dimension that sentence does not hold.
                            // The Z-C21.3 probe of C3-1 (28/9/2026): a toleranced 100
                            // (6 texts) and a plain 10 (1 text) moved with SetPosition2 by
                            // (20, 10) mm, then with their view by (30, -10) mm, and every
                            // text position moved by exactly the same; each stayed within
                            // 8 mm of IAnnotation.GetPosition while lying 260-310 mm from
                            // the sheet origin. So p is used as it is, in sheet metres.
                            for (int i = 0; i < n; i++)
                            {
                                var p = data.GetTextPositionAtIndex(i) as double[];
                                double w = data.GetTextInBoxWidthAtIndex(i), h = data.GetTextHeightAtIndex(i);
                                double angle = data.GetTextAngleAtIndex(i);
                                string t = data.GetTextAtIndex(i) ?? "";
                                if (p == null || w <= 0 || h <= 0 || t.Trim().Length == 0) continue;
                                // The reference: lower left (1), which is the only one seen.
                                // The others are brought to the lower left corner.
                                double ux = Math.Cos(angle), uy = Math.Sin(angle), vx = -uy, vy = ux;
                                double x0 = p[0], y0 = p[1];
                                switch (data.GetTextRefPositionAtIndex(i))
                                {
                                    case (int)swTextPosition_e.swUPPER_LEFT: x0 -= vx * h; y0 -= vy * h; break;
                                    case (int)swTextPosition_e.swCENTER: x0 -= ux * w / 2 + vx * h / 2; y0 -= uy * w / 2 + vy * h / 2; break;
                                    case (int)swTextPosition_e.swUPPER_RIGHT: x0 -= ux * w + vx * h; y0 -= uy * w + vy * h; break;
                                    case (int)swTextPosition_e.swLOWER_RIGHT: x0 -= ux * w; y0 -= uy * w; break;
                                    case (int)swTextPosition_e.swUPPER_CENTER: x0 -= ux * w / 2 + vx * h; y0 -= uy * w / 2 + vy * h; break;
                                }
                                outside.Add(new DimensionText
                                {
                                    View = viewName,
                                    Name = SafeName(dd),
                                    Text = t.Trim(),
                                    Angles = new[]
                                    {
                                        new[] { x0, y0 },
                                        new[] { x0 + ux * w, y0 + uy * w },
                                        new[] { x0 + ux * w + vx * h, y0 + uy * w + vy * h },
                                        new[] { x0 + vx * h, y0 + vy * h },
                                    },
                                });
                            }
                        }
                        catch { }
                        dd = dd.GetNext5() as IDisplayDimension;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { return null; }   // N47: a list cut short is not the list of the drawing
            return outside;
        }

        private static string SafeName(IDisplayDimension dd)
        {
            try { return dd.GetNameForSelection() ?? ""; } catch { return ""; }
        }

        /// <summary>
        /// Who overlaps whom: text on text (the whole drawing), text on
        /// an edge of its own view, and since round 21 (point 5) text on a note of
        /// the sheet or of a view and on the title block. The rows say the two names
        /// and the texts; «checkedAgainst» says what was compared.
        /// A margin of 0.3 mm: two texts that graze each other are not read badly.
        /// </summary>
        private List<string> TextsThatOverlap(IDrawingDoc drawing, IView view, List<DimensionText> texts,
                                              out string checkedAgainst)
        {
            checkedAgainst = null;
            // Texts not read (N47) give no overlaps to report, and the caller
            // says «not known» instead of a count.
            if (texts == null) return null;
            var outside = new List<string>();
            const double margin = 0.0003;
            // THE PIECES OF THE SAME DIMENSION ARE NOT COMPARED WITH EACH OTHER (seen at the
            // first bench, 26/9/2026): the callout «Ø8 ↧5» is four texts
            // stuck together — <MOD-DIAM>, 8, <HOLE-DEPTH>, 5 — and the check
            // counted four overlaps that on the sheet are one single inscription.
            for (int i = 0; i < texts.Count; i++)
                for (int j = i + 1; j < texts.Count; j++)
                    if (!(texts[i].View == texts[j].View && texts[i].Name == texts[j].Name && texts[i].Name.Length > 0) &&
                        TheyTouch(texts[i].Angles, texts[j].Angles, margin))
                        outside.Add("«" + texts[i].Text + "» (" + texts[i].View + ") over «" + texts[j].Text + "» (" + texts[j].View + ")");

            // The edges of the view, brought onto the sheet as segments (the curves piecewise).
            string viewName = "";
            try { viewName = view.GetName2() ?? ""; } catch { }
            var segments = ViewSegments(view);
            foreach (var t in texts)
            {
                if (t.View != viewName) continue;
                foreach (var s in segments)
                    if (SegmentInRectangle(s, t.Angles, -margin))
                    {
                        outside.Add("«" + t.Text + "» (" + t.View + ") over a line of the part");
                        break;
                    }
            }

            // THE NOTES AND THE TITLE BLOCK (report 8cd843, round 21, point 5): the
            // label of a detail or of a section is a note of its view (see
            // ViewWithItsNotes), the boxes of the title block are notes of the sheet
            // (an empty drawing counts 67 of them), and a dimension written over them
            // was not said. The title block as a whole when the format declares it
            // (ITitleBlock, read as ReadSheet reads it).
            int notesRead;
            List<string> noteNames;
            var noteBoxes = NoteBoxes(drawing, out noteNames, out notesRead);
            double[] titleBlock = DeclaredTitleBlock(drawing);
            foreach (DimensionText t in texts)
                foreach (string what in WhatTheTextCovers(t.Angles, noteNames, noteBoxes, titleBlock, margin))
                    outside.Add("«" + t.Text + "» (" + t.View + ") over " + what);
            checkedAgainst = OverlapsCheckedAgainst(notesRead, titleBlock != null);
            return outside;
        }

        /// <summary>
        /// The rectangle of every note of the sheet and of its views (x0, y0, x1, y1
        /// in sheet metres, from INote.GetExtent, the reading ViewWithItsNotes uses),
        /// with its name at the same place of «names». «read» is the count of the notes met, -1 when
        /// the walk broke off: a part of the notes is not the notes of the sheet
        /// (N47), and then the list is empty.
        /// </summary>
        private static List<double[]> NoteBoxes(IDrawingDoc drawing, out List<string> names, out int read)
        {
            var boxes = new List<double[]>();
            var found = new List<string>();
            names = found;
            read = 0;
            try
            {
                for (var v = drawing.GetFirstView() as IView; v != null; v = v.GetNextView() as IView)
                {
                    string viewName = "";
                    try { viewName = v.GetName2() ?? ""; } catch { }
                    for (var note = v.GetFirstNote() as INote; note != null; note = note.GetNext() as INote)
                    {
                        read++;
                        string text = null;
                        double[] box = null;
                        try { text = note.GetText(); box = ExtentBox(note.GetExtent() as double[]); } catch { }
                        string shown = NoteShown(text);
                        if (box == null || shown == null) continue;
                        boxes.Add(box);
                        found.Add("the note «" + shown + "» (" + viewName + ")");
                    }
                }
            }
            catch { read = -1; boxes.Clear(); found.Clear(); }
            return boxes;
        }

        /// <summary>The title block the format declares (ITitleBlock.GetExtents), null when it declares none.</summary>
        private static double[] DeclaredTitleBlock(IDrawingDoc drawing)
        {
            try
            {
                var tb = (drawing.GetCurrentSheet() as ISheet)?.TitleBlock;
                if (tb == null) return null;
                double x0 = 0, y0 = 0, x1 = 0, y1 = 0;
                tb.GetExtents(ref x0, ref y0, ref x1, ref y1);
                return ExtentBox(new[] { x0, y0, 0, x1, y1, 0 });
            }
            catch { return null; }
        }

        /// <summary>
        /// The first line of a note, at most 40 characters, null when it has none:
        /// a note with no text has nothing a dimension could hide. Pure.
        /// </summary>
        internal static string NoteShown(string text)
        {
            if (text == null) return null;
            var first = text.Replace("\r", "").Split('\n')[0].Trim();
            if (first.Length == 0) return null;
            return first.Length > 40 ? first.Substring(0, 40) + "..." : first;
        }

        /// <summary>
        /// The rectangle (x0, y0, x1, y1) of an extent of six numbers, two corners
        /// with x, y, z each (INote.GetExtent), null when it is not one: fewer
        /// numbers, or no width or no height. Pure.
        /// </summary>
        internal static double[] ExtentBox(double[] extent)
        {
            if (extent == null || extent.Length < 6) return null;
            double x0 = Math.Min(extent[0], extent[3]), y0 = Math.Min(extent[1], extent[4]);
            double x1 = Math.Max(extent[0], extent[3]), y1 = Math.Max(extent[1], extent[4]);
            if (!(x1 - x0 > 1e-6) || !(y1 - y0 > 1e-6)) return null;
            return new[] { x0, y0, x1, y1 };
        }

        private static double[][] BoxCorners(double[] b)
        {
            return new[] { new[] { b[0], b[1] }, new[] { b[2], b[1] }, new[] { b[2], b[3] }, new[] { b[0], b[3] } };
        }

        /// <summary>
        /// What the text of a dimension covers (round 21, point 5): the title block
        /// when it is declared, then each note it touches by name. A note that lies
        /// inside the title block is one of its boxes: said once, as the title
        /// block. Pure, so the contract suite reads it.
        /// </summary>
        internal static List<string> WhatTheTextCovers(double[][] text, IList<string> noteNames, IList<double[]> noteBoxes,
                                                       double[] titleBlock, double margin)
        {
            var covered = new List<string>();
            if (text == null) return covered;
            if (titleBlock != null && TheyTouch(text, BoxCorners(titleBlock), margin)) covered.Add("the title block");
            for (int i = 0; i < noteBoxes.Count && i < noteNames.Count; i++)
            {
                var b = noteBoxes[i];
                if (b == null) continue;
                if (titleBlock != null && b[0] >= titleBlock[0] - 1e-6 && b[1] >= titleBlock[1] - 1e-6 &&
                    b[2] <= titleBlock[2] + 1e-6 && b[3] <= titleBlock[3] + 1e-6) continue;
                if (TheyTouch(text, BoxCorners(b), margin)) covered.Add(noteNames[i]);
            }
            return covered;
        }

        /// <summary>
        /// What the texts of the dimensions were compared with (round 21, point 5),
        /// in words: a reader who sees no overlap must know whether the notes and
        /// the title block were looked at. Pure.
        /// </summary>
        internal static string OverlapsCheckedAgainst(int notesRead, bool titleBlockDeclared)
        {
            string notes = notesRead < 0
                ? "the notes NOT read (the walk over the views broke off): a text over a label or a box is not said"
                : notesRead + " notes of the sheet and of its views (the labels of details and sections are notes of their view)";
            string block = titleBlockDeclared
                ? "the title block, as the format declares it (ITitleBlock)"
                : "the title block NOT as a whole: the format does not declare it (ITitleBlock), only its boxes that are notes";
            return "the other dimension texts, the lines of the part in this view, " + notes + "; " + block;
        }

        private List<double[]> ViewSegments(IView view)
        {
            var outside = new List<double[]>();
            IMathUtility mu = null; MathTransform tr = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; tr = view.ModelToViewTransform; } catch { }
            if (mu == null || tr == null) return outside;
            object[] edges = null;
            try { edges = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]; } catch { }
            foreach (var o in edges ?? new object[0])
            {
                try
                {
                    var e = o as IEdge;
                    var cp = ApiHelpers.EdgeParameters(e, out ICurve c);
                    if (c == null || cp == null) continue;
                    var tess = c.GetTessPts(1e-4, 1e-3, cp.StartPoint, cp.EndPoint) as double[];
                    if (tess == null || tess.Length < 6) continue;
                    // THE COMPONENT'S PLACEMENT FIRST on a view of an assembly (report
                    // 8cd843 N23, see ToSheetOf): without it the lines of every clip
                    // were looked for on the same 8 mm of the sheet.
                    IComponent2 component;
                    MathTransform placement;
                    var toSheet = ToSheetOf(o, tr, out component, out placement);
                    if (toSheet == null) continue;
                    double[] prec = null;
                    for (int i = 0; i + 2 < tess.Length; i += 3)
                    {
                        var q = ((IMathPoint)((IMathPoint)mu.CreatePoint(new[] { tess[i], tess[i + 1], tess[i + 2] })).MultiplyTransform(toSheet)).ArrayData as double[];
                        if (q == null) continue;
                        if (prec != null) outside.Add(new[] { prec[0], prec[1], q[0], q[1] });
                        prec = q;
                    }
                }
                catch { }
            }
            return outside;
        }

        // ── Planar geometry: oriented rectangles as convex polygons ──────────

        private static bool TheyTouch(double[][] a, double[][] b, double margin)
        {
            // Separating axes (SAT) on the sides of the two rectangles.
            foreach (var poli in new[] { a, b })
                for (int i = 0; i < 4; i++)
                {
                    var p = poli[i]; var q = poli[(i + 1) % 4];
                    double nx = -(q[1] - p[1]), ny = q[0] - p[0];
                    double l = Math.Sqrt(nx * nx + ny * ny);
                    if (l < 1e-12) continue;
                    nx /= l; ny /= l;
                    double amin = double.MaxValue, amax = double.MinValue, bmin = double.MaxValue, bmax = double.MinValue;
                    foreach (var r in a) { double d = r[0] * nx + r[1] * ny; amin = Math.Min(amin, d); amax = Math.Max(amax, d); }
                    foreach (var r in b) { double d = r[0] * nx + r[1] * ny; bmin = Math.Min(bmin, d); bmax = Math.Max(bmax, d); }
                    if (amax + margin < bmin || bmax + margin < amin) return false;
                }
            return true;
        }

        /// <summary>
        /// The segment passes INSIDE the rectangle (shrunk by `-margin` if
        /// negative: a line that grazes the border of the text does not cover it).
        /// </summary>
        private static bool SegmentInRectangle(double[] s, double[][] r, double margin)
        {
            // Frame of the rectangle: origin r[0], axes u (r0->r1), v (r0->r3).
            double ux = r[1][0] - r[0][0], uy = r[1][1] - r[0][1], vx = r[3][0] - r[0][0], vy = r[3][1] - r[0][1];
            double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
            if (lu < 1e-12 || lv < 1e-12) return false;
            ux /= lu; uy /= lu; vx /= lv; vy /= lv;
            double m = -margin;   // positive: how much it shrinks
            Func<double, double, double[]> loc = (x, y) => new[] { (x - r[0][0]) * ux + (y - r[0][1]) * uy, (x - r[0][0]) * vx + (y - r[0][1]) * vy };
            var a = loc(s[0], s[1]); var b = loc(s[2], s[3]);
            // Liang-Barsky clipping on the rectangle [m, lu-m] x [m, lv-m].
            double t0 = 0, t1 = 1, dx = b[0] - a[0], dy = b[1] - a[1];
            double[] pp = { -dx, dx, -dy, dy };
            double[] qq = { a[0] - m, (lu - m) - a[0], a[1] - m, (lv - m) - a[1] };
            for (int k = 0; k < 4; k++)
            {
                if (Math.Abs(pp[k]) < 1e-15) { if (qq[k] < 0) return false; continue; }
                double t = qq[k] / pp[k];
                if (pp[k] < 0) { if (t > t1) return false; if (t > t0) t0 = t; }
                else { if (t < t0) return false; if (t < t1) t1 = t; }
            }
            return t0 <= t1;
        }
    }
}
