using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// A FREE PLACE ON THE SHEET, for a table or a view placed after the others.
    ///
    /// ── BORN LOOKING AT TWO PNGs, 26/9/2026, with the bench green both times ──
    ///
    /// The cut list at 250/200 mm came out of the sheet on the right after the
    /// fourth of its 17 columns; the flat pattern view at 300/90 mm, at scale
    /// 2:1, went down into the title block. The measures were right — rows, quantities,
    /// bounding box of the flat pattern — and the sheet was wrong.
    ///
    /// The place: inside the frame (12 mm from the edge), outside the other views
    /// (`GetOutline`, 4 mm clearance) and outside the title block — read from the format with
    /// `ITitleBlock.GetExtents` when the format declares it, otherwise from the lines the format
    /// draws (TitleBlockFromLines), otherwise estimated in the bottom-right corner (half the sheet
    /// x 65 mm), and it is said.
    /// </summary>
    public partial class SwOperations
    {
        private sealed class FreeSheet
        {
            public double Width = 0.42, Height = 0.297;
            public double[] TitleBlock;             // x0, y0, x1, y1 in metres
            public bool EstimatedTitleBlock;
            /// <summary>Where the title block comes from (TitleBlockOfTheSheet), null when estimated.</summary>
            public string TitleBlockFrom;
            public List<double[]> BoundingBoxes = new List<double[]>();
            public const double FrameBorder = 0.012, Detachment = 0.004;
            /// <summary>
            /// A micrometre: the places of FreePlace lie on the edge of the clearance, and the outline
            /// read back after the move can come out a hair inside it (replay of 02/11/2026, step 035:
            /// detail B of 26M000002 landed at y 69 mm over a title block ending at 65 mm, to 0.1 mm,
            /// and was removed as falling on it). The frame had it already; the boxes have it too.
            /// </summary>
            public const double Hair = 1e-6;

            /// <summary>Null if the rectangle is free, otherwise what it touches.</summary>
            public string Touches(double x0, double y0, double x1, double y1)
            {
                if (x0 < FrameBorder - Hair || x1 > Width - FrameBorder + Hair || y0 < FrameBorder - Hair || y1 > Height - FrameBorder + Hair)
                    return "goes out of the frame of the sheet";
                double d = Detachment - Hair;
                foreach (var r in BoundingBoxes)
                    if (x1 > r[0] - d && x0 < r[2] + d && y1 > r[1] - d && y0 < r[3] + d)
                        return ReferenceEquals(r, TitleBlock) ? "falls on the title block" : "falls on a view, a table or a note";
                return null;
            }

            /// <summary>
            /// What the rectangle touches among the boxes that do not lie inside <paramref name="apart"/>
            /// widened by a millimetre (the title block a table rests on, and its notes), the frame
            /// left out: null if nothing. A bill of materials resting on the title block is as wide
            /// as the title block and reaches the frame line of the format, inside the 12 mm.
            /// </summary>
            public string TouchesBeside(double x0, double y0, double x1, double y1, double[] apart)
            {
                double d = Detachment - Hair, m = 0.001;
                foreach (var r in BoundingBoxes)
                {
                    if (apart != null && r[0] >= apart[0] - m && r[2] <= apart[2] + m && r[1] >= apart[1] - m && r[3] <= apart[3] + m) continue;
                    if (x1 > r[0] - d && x0 < r[2] + d && y1 > r[1] - d && y0 < r[3] + d)
                        return "falls on a view, a table or a note";
                }
                return null;
            }
        }

        /// <summary>
        /// THE TITLE BLOCK THE FORMAT DRAWS (note 98, defect 8). A format that declares no title
        /// block (ITitleBlock null: the owner's A3.slddrt) still draws it with the lines of its
        /// sketch (ISheet~GetTemplateSketch.html, «Gets the sheet format sketch for this drawing
        /// sheet»). Until 0.167.1 the title block was then estimated at half the sheet x 65 mm, and
        /// the bill of materials asked «over the title block» went to a free place 4 mm above the
        /// estimate: about 16 mm above the real one and to its left (26M000002, 30/10/2026).
        ///
        /// Lines {x0, y0, x1, y1}, metres of sheet. The frame: the outermost long horizontal and
        /// vertical lines (half the sheet or more) that are not the edge of the paper, the edge when
        /// there are none. The title block: the box of the lines inside the frame and not on it,
        /// joined to one another (an end on the other line, 0.1 mm), that reach the bottom-right
        /// corner of the frame. Null when none does, or when the box is smaller than 20 x 10 mm or
        /// covers nine tenths of the frame both ways. Pure.
        ///
        /// Measured on the owner's A3 (02/11/2026, a probe on a copy): 71 segments, 69 lines, the
        /// edge of the paper, the frame at 7..413 x 7..290 mm, the title block at 217..413 x 7..53.5 mm.
        /// </summary>
        internal static double[] TitleBlockFromLines(IList<double[]> lines, double width, double height)
        {
            if (lines == null || lines.Count == 0 || width <= 0 || height <= 0) return null;
            const double tol = 0.0001;
            double fx0 = double.MaxValue, fx1 = double.MinValue, fy0 = double.MaxValue, fy1 = double.MinValue;
            foreach (double[] l in lines)
            {
                if (l == null || l.Length < 4) continue;
                bool level = Math.Abs(l[1] - l[3]) < tol, upright = Math.Abs(l[0] - l[2]) < tol;
                if (level && Math.Abs(l[2] - l[0]) >= width / 2 && l[1] > tol && l[1] < height - tol)
                {
                    fy0 = Math.Min(fy0, l[1]);
                    fy1 = Math.Max(fy1, l[1]);
                }
                if (upright && Math.Abs(l[3] - l[1]) >= height / 2 && l[0] > tol && l[0] < width - tol)
                {
                    fx0 = Math.Min(fx0, l[0]);
                    fx1 = Math.Max(fx1, l[0]);
                }
            }
            if (fy0 == double.MaxValue || fy0 > height / 2) fy0 = 0;
            if (fy1 == double.MinValue || fy1 < height / 2) fy1 = height;
            if (fx0 == double.MaxValue || fx0 > width / 2) fx0 = 0;
            if (fx1 == double.MinValue || fx1 < width / 2) fx1 = width;

            // The lines inside the frame and not on it.
            var inside = new List<double[]>();
            foreach (double[] l in lines)
            {
                if (l == null || l.Length < 4) continue;
                if (Math.Min(l[0], l[2]) < fx0 - tol || Math.Max(l[0], l[2]) > fx1 + tol ||
                    Math.Min(l[1], l[3]) < fy0 - tol || Math.Max(l[1], l[3]) > fy1 + tol) continue;
                bool level = Math.Abs(l[1] - l[3]) < tol, upright = Math.Abs(l[0] - l[2]) < tol;
                if (level && upright) continue;
                if (level && (Math.Abs(l[1] - fy0) < tol || Math.Abs(l[1] - fy1) < tol)) continue;
                if (upright && (Math.Abs(l[0] - fx0) < tol || Math.Abs(l[0] - fx1) < tol)) continue;
                inside.Add(l);
            }
            int n = inside.Count;
            if (n == 0) return null;
            var group = new int[n];
            for (int i = 0; i < n; i++) group[i] = i;
            Func<int, int> root = null;
            root = i => group[i] == i ? i : (group[i] = root(group[i]));
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    double[] a = inside[i], b = inside[j];
                    if (PointToSegment(a[0], a[1], b) < tol || PointToSegment(a[2], a[3], b) < tol ||
                        PointToSegment(b[0], b[1], a) < tol || PointToSegment(b[2], b[3], a) < tol)
                        group[root(i)] = root(j);
                }
            // The box of each group, and the union of those that reach the corner.
            var boxes = new Dictionary<int, double[]>();
            for (int i = 0; i < n; i++)
            {
                double[] l = inside[i], box;
                int r = root(i);
                if (!boxes.TryGetValue(r, out box)) boxes[r] = box = new[] { double.MaxValue, double.MaxValue, double.MinValue, double.MinValue };
                box[0] = Math.Min(box[0], Math.Min(l[0], l[2]));
                box[1] = Math.Min(box[1], Math.Min(l[1], l[3]));
                box[2] = Math.Max(box[2], Math.Max(l[0], l[2]));
                box[3] = Math.Max(box[3], Math.Max(l[1], l[3]));
            }
            double[] block = null;
            foreach (double[] box in boxes.Values)
            {
                if (box[2] < fx1 - tol || box[1] > fy0 + tol) continue;
                block = block == null ? (double[])box.Clone()
                      : new[] { Math.Min(block[0], box[0]), Math.Min(block[1], box[1]), Math.Max(block[2], box[2]), Math.Max(block[3], box[3]) };
            }
            if (block == null) return null;
            double w = block[2] - block[0], h = block[3] - block[1];
            if (w < 0.02 || h < 0.01) return null;
            if (w >= 0.9 * (fx1 - fx0) && h >= 0.9 * (fy1 - fy0)) return null;
            return block;
        }

        /// <summary>The distance of a point from a segment {x0, y0, x1, y1}. Pure.</summary>
        private static double PointToSegment(double x, double y, double[] s)
        {
            double dx = s[2] - s[0], dy = s[3] - s[1], len = dx * dx + dy * dy;
            double k = len <= 0 ? 0 : Math.Max(0, Math.Min(1, ((x - s[0]) * dx + (y - s[1]) * dy) / len));
            double px = s[0] + k * dx - x, py = s[1] + k * dy - y;
            return Math.Sqrt(px * px + py * py);
        }

        /// <summary>The straight lines of the sketch of the sheet format, metres of sheet; null when it cannot be read.</summary>
        /// Library 0.198.4: ISketch.GetLines2 gives them all in one call. Read one segment at a time
        /// (GetStartPoint2/GetEndPoint2 and their X, Y) the 69 lines of the owner's A3 took 1.708,4 to
        /// 4.854,0 ms, about 0,7 s inside every drawing call that looks for a free place; GetLines2
        /// 6,2 to 11,8 ms, the same 69 lines (0 mm apart both ways) and the same title block, 217..413 x
        /// 7..53,5 mm (a read-only probe from outside SOLIDWORKS, 02/11/2026, a new A3 drawing and 26M000002).
        /// The segments stay as the way back when GetLines2 gives no whole records.
        private static List<double[]> FormatLines(ISheet sheet)
        {
            try
            {
                var sketch = sheet?.GetTemplateSketch() as ISketch;
                if (sketch == null) return null;
                List<double[]> inOneCall = null;
                try { inOneCall = LinesOfGetLines2(sketch.GetLines2((short)swCrossHatchFilter_e.swCrossHatchExclude) as double[]); }
                catch { }
                if (inOneCall != null) return inOneCall;
                var segments = sketch.GetSketchSegments() as object[];
                if (segments == null) return null;
                var lines = new List<double[]>();
                foreach (object o in segments)
                {
                    var segment = o as ISketchSegment;
                    if (segment == null || segment.GetType() != (int)swSketchSegments_e.swSketchLINE) continue;
                    var line = segment as ISketchLine;
                    var a = line?.GetStartPoint2() as ISketchPoint;
                    var b = line?.GetEndPoint2() as ISketchPoint;
                    if (a != null && b != null) lines.Add(new[] { a.X, a.Y, b.X, b.Y });
                }
                return lines;
            }
            catch { return null; }
        }

        /// <summary>
        /// The lines of ISketch.GetLines2 as {x0, y0, x1, y1}: 12 doubles a line, colour, type, style,
        /// width, layer, override, start x y z, end x y z (API guide, ISketch~GetLines2.html, Remarks;
        /// the order measured on 69 lines of the owner's A3 against GetStartPoint2/GetEndPoint2, 0 mm
        /// apart). Null when the array is not whole records. Pure.
        /// </summary>
        internal static List<double[]> LinesOfGetLines2(double[] raw)
        {
            if (raw == null || raw.Length < 12 || raw.Length % 12 != 0) return null;
            var lines = new List<double[]>(raw.Length / 12);
            for (int i = 0; i < raw.Length; i += 12)
                lines.Add(new[] { raw[i + 6], raw[i + 7], raw[i + 9], raw[i + 10] });
            return lines;
        }

        /// <summary>
        /// The title block of a sheet: the one the format declares (ITitleBlock.GetExtents),
        /// otherwise the one its lines draw (TitleBlockFromLines); null when neither.
        /// <paramref name="from"/> says which, null when neither.
        /// </summary>
        private static double[] TitleBlockOfTheSheet(ISheet sheet, out string from)
        {
            // Library 0.198.3: timed as «sheet.title_block» (and its lines as «sheet.format_lines»): after
            // 0.196.0 every drawing call of the bench took about 1 s more (02/11/2026, runs of 31/10 and 02/11).
            string said = null;
            double[] block = PhaseClock.Time("sheet.title_block", () => { string f; var b = TitleBlockOfTheSheetUntimed(sheet, out f); said = f; return b; });
            from = said;
            return block;
        }

        private static double[] TitleBlockOfTheSheetUntimed(ISheet sheet, out string from)
        {
            from = null;
            if (sheet == null) return null;
            try
            {
                var tb = sheet.TitleBlock;
                if (tb != null)
                {
                    double x0 = 0, y0 = 0, x1 = 0, y1 = 0;
                    tb.GetExtents(ref x0, ref y0, ref x1, ref y1);
                    if (Math.Abs(x1 - x0) > 0.01 && Math.Abs(y1 - y0) > 0.01)
                    {
                        from = "declared by the format";
                        return new[] { Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1) };
                    }
                }
            }
            catch { }
            double w = 0, h = 0;
            try { sheet.GetSize(ref w, ref h); } catch { }
            if (w < 0.05 || h < 0.05) return null;
            double[] drawn = TitleBlockFromLines(PhaseClock.Time("sheet.format_lines", () => FormatLines(sheet)), w, h);
            if (drawn != null) from = "drawn by the lines of the format";
            return drawn;
        }

        /// <summary>
        /// The current sheet with what occupies it: the title block and the views,
        /// except `excluded` (the view that is about to be moved) and the views named in
        /// `alsoExcluded`, with their notes (move_view, report 8cd843 N54: the views that
        /// moved together with the one asked are not in its way).
        /// </summary>
        private static FreeSheet ReadSheet(IDrawingDoc drawing, IView excluded, string excludedTable = null,
                                           ICollection<string> alsoExcluded = null)
        {
            var f = new FreeSheet();
            ISheet sheet = null;
            try
            {
                sheet = drawing.GetCurrentSheet() as ISheet;
                double w = 0, h = 0;
                sheet?.GetSize(ref w, ref h);
                if (w > 0.05 && h > 0.05) { f.Width = w; f.Height = h; }
            }
            catch { }
            string blockFrom;
            f.TitleBlock = TitleBlockOfTheSheet(sheet, out blockFrom);
            f.TitleBlockFrom = blockFrom;
            f.EstimatedTitleBlock = f.TitleBlock == null;
            if (f.TitleBlock == null) f.TitleBlock = new[] { f.Width / 2, 0, f.Width, 0.065 };
            f.BoundingBoxes.Add(f.TitleBlock);
            string excludedName = null;
            try { excludedName = excluded?.GetName2(); } catch { }
            try
            {
                var v = (drawing.GetFirstView() as IView)?.GetNextView() as IView;
                while (v != null)
                {
                    string n = null;
                    try { n = v.GetName2(); } catch { }
                    if ((excludedName == null || n != excludedName) && (alsoExcluded == null || n == null || !alsoExcluded.Contains(n)))
                    {
                        var o = v.GetOutline() as double[];
                        if (o != null && o.Length >= 4) f.BoundingBoxes.Add(new[] { o[0], o[1], o[2], o[3] });
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            // TABLES AND NOTES ALREADY ON THE SHEET TOO (review of 26/9/2026): a
            // hole table at 15/(H-15) and a cut table «top left»
            // ended up one on top of the other. The sheet and every view are looked at.
            try
            {
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    foreach (var o in (v.GetTableAnnotations() as object[]) ?? new object[0])
                    {
                        var t = o as ITableAnnotation;
                        var r = TableBoundingBox(t, excludedTable);
                        if (r != null) f.BoundingBoxes.Add(r);
                    }
                    // THE NOTES OF THE VIEW BEING MOVED ARE NOT IN ITS WAY (report 8cd843
                    // N43, round 12): the label of a detail view is a note of that view,
                    // 2.4 mm under its outline (a probe on a copy, 27/10/2026), closer than
                    // the clearance, so the view touched its own label wherever it was.
                    string viewName = null;
                    try { viewName = v.GetName2(); } catch { }
                    var note = (excludedName != null && viewName == excludedName) ||
                               (alsoExcluded != null && viewName != null && alsoExcluded.Contains(viewName))
                        ? null : v.GetFirstNote() as INote;
                    while (note != null)
                    {
                        try
                        {
                            var e = note.GetExtent() as double[];
                            if (e != null && e.Length >= 6)
                            {
                                double x0 = Math.Min(e[0], e[3]), y0 = Math.Min(e[1], e[4]), x1 = Math.Max(e[0], e[3]), y1 = Math.Max(e[1], e[4]);
                                // a «note» as big as half the sheet is a frame of the format, not an obstruction
                                if ((x1 - x0) * (y1 - y0) < 0.5 * f.Width * f.Height && x1 > x0 && y1 > y0)
                                    f.BoundingBoxes.Add(new[] { x0, y0, x1, y1 });
                            }
                        }
                        catch { }
                        note = note.GetNext() as INote;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return f;
        }

        /// <summary>
        /// The rectangle of a table on the sheet (x0, y0, x1, y1), from the anchor and
        /// from the sum of columns and rows; null if it is the one to exclude or it cannot
        /// be read.
        /// </summary>
        private static double[] TableBoundingBox(ITableAnnotation t, string excluded)
        {
            if (t == null) return null;
            try
            {
                var an = t.GetAnnotation() as IAnnotation;
                if (an == null) return null;
                if (excluded != null && an.GetName() == excluded) return null;
                var p = an.GetPosition() as double[];
                if (p == null || p.Length < 2) return null;
                double w = 0, h = 0;
                for (int c = 0; c < t.ColumnCount; c++) w += t.GetColumnWidth(c);
                for (int r = 0; r < t.RowCount; r++) h += t.GetRowHeight(r);
                if (w <= 0 || h <= 0) return null;
                double x0 = p[0], y1 = p[1];
                switch (t.AnchorType)
                {
                    case (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopRight: x0 = p[0] - w; break;
                    case (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_BottomLeft: y1 = p[1] + h; break;
                    case (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_BottomRight: x0 = p[0] - w; y1 = p[1] + h; break;
                }
                return new[] { x0, y1 - h, x0 + w, y1 };
            }
            catch { return null; }
        }

        /// <summary>
        /// The first free place for a rectangle w x h (metres), among: above the
        /// title block on the right, top right, top left, bottom
        /// left, then a grid at 10 mm steps. It returns the bottom-left
        /// corner and where, or null.
        /// </summary>
        private static double[] FreePlace(FreeSheet f, double w, double h, out string location)
        {
            double c = FreeSheet.FrameBorder, s = FreeSheet.Detachment;
            var candidates = new List<Tuple<double, double, string>>
            {
                Tuple.Create(Math.Min(f.TitleBlock[2], f.Width - c) - w, f.TitleBlock[3] + s, "above the title block"),
                Tuple.Create(f.Width - c - w, f.Height - c - h, "top right"),
                Tuple.Create(c, f.Height - c - h, "top left"),
                Tuple.Create(c, c, "bottom left"),
            };
            foreach (var k in candidates)
                if (f.Touches(k.Item1, k.Item2, k.Item1 + w, k.Item2 + h) == null)
                {
                    location = k.Item3;
                    return new[] { k.Item1, k.Item2 };
                }
            // The grid: from right to left, from top to bottom.
            for (double y = f.Height - c - h; y >= c - 1e-9; y -= 0.01)
                for (double x = f.Width - c - w; x >= c - 1e-9; x -= 0.01)
                    if (f.Touches(x, y, x + w, y + h) == null)
                    {
                        // What the two numbers are (report 8cd843, round 13, observation: «I
                        // do not know what 147 and 144 are»): the bottom-left corner, which a
                        // reader compared with a centre.
                        location = FirstFreePlaceText(x, y);
                        return new[] { x, y };
                    }
            location = null;
            return null;
        }

        /// <summary>
        /// Where a rectangle went when no named place was free, saying what the numbers
        /// are: its bottom-left corner in millimetres of sheet (round 13 of 8cd843). Pure,
        /// so the contract suite reads it.
        /// </summary>
        internal static string FirstFreePlaceText(double x, double y)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "in the first free place, bottom-left corner at x {0:0} mm, y {1:0} mm of the sheet", x * 1000, y * 1000);
        }
    }
}
