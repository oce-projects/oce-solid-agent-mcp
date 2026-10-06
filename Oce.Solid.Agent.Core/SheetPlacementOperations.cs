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
    /// `ITitleBlock.GetExtents` when the format declares it, otherwise estimated
    /// in the bottom-right corner (half the sheet x 65 mm), and it is said.
    /// </summary>
    public partial class SwOperations
    {
        private sealed class FreeSheet
        {
            public double Width = 0.42, Height = 0.297;
            public double[] TitleBlock;             // x0, y0, x1, y1 in metres
            public bool EstimatedTitleBlock;
            public List<double[]> BoundingBoxes = new List<double[]>();
            public const double FrameBorder = 0.012, Detachment = 0.004;

            /// <summary>Null if the rectangle is free, otherwise what it touches.</summary>
            public string Touches(double x0, double y0, double x1, double y1)
            {
                if (x0 < FrameBorder - 1e-6 || x1 > Width - FrameBorder + 1e-6 || y0 < FrameBorder - 1e-6 || y1 > Height - FrameBorder + 1e-6)
                    return "goes out of the frame of the sheet";
                foreach (var r in BoundingBoxes)
                    if (x1 > r[0] - Detachment && x0 < r[2] + Detachment && y1 > r[1] - Detachment && y0 < r[3] + Detachment)
                        return ReferenceEquals(r, TitleBlock) ? "falls on the title block" : "falls on a view, a table or a note";
                return null;
            }
        }

        /// <summary>
        /// The current sheet with what occupies it: the title block and the views,
        /// except `excluded` (the view that is about to be moved).
        /// </summary>
        private static FreeSheet ReadSheet(IDrawingDoc drawing, IView excluded, string excludedTable = null)
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
            try
            {
                var tb = sheet?.TitleBlock;
                if (tb != null)
                {
                    double x0 = 0, y0 = 0, x1 = 0, y1 = 0;
                    tb.GetExtents(ref x0, ref y0, ref x1, ref y1);
                    if (Math.Abs(x1 - x0) > 0.01 && Math.Abs(y1 - y0) > 0.01)
                        f.TitleBlock = new[] { Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1) };
                }
            }
            catch { }
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
                    if (excludedName == null || n != excludedName)
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
                    var note = v.GetFirstNote() as INote;
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
                        location = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                             "in the first free place ({0:0}/{1:0} mm)", x * 1000, y * 1000);
                        return new[] { x, y };
                    }
            location = null;
            return null;
        }
    }
}
