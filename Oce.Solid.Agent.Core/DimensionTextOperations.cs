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
        /// Who overlaps whom: text on text (the whole drawing), and text on
        /// an edge of its own view. The rows say the two names and the texts.
        /// A margin of 0.3 mm: two texts that graze each other are not read badly.
        /// </summary>
        private List<string> TextsThatOverlap(IDrawingDoc drawing, IView view, List<DimensionText> texts)
        {
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
            return outside;
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
