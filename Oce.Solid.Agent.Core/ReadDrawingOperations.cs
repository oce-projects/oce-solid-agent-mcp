using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// READS A FINISHED DRAWING, SHEET BY SHEET (report 8cd843, round 22, point 20,
    /// 28/10/2026). To set a drawing made with the bridge against the one the owner corrected,
    /// session 8cd843 had the views and scales from the state and the title block, and the
    /// dimensions, notes and bill of materials only from the PDF. This reads them all and
    /// touches nothing: no sheet is brought in front.
    ///
    /// Every sheet through `IDrawingDoc.GetViews` (an array per sheet, the sheet itself
    /// first, as KeysAlreadyDimensioned reads them) and `GetSheetNames`. Per view: its name,
    /// kind, scale, model and configuration, outline; each dimension with its value
    /// (`GetSystemValue` through ApiHelpers.ReadDimensionInConfiguration: metres, radians for
    /// an angle), what the sheet prints, prefix, suffix, parentheses, arrows, where it is and
    /// what it is attached to; each note with its text; each BOM balloon with its number and
    /// part; each table with its rows. The notes of the sheet that lie in the title block the
    /// format declares are counted, not listed. Not measured on SOLIDWORKS as an operation of
    /// its own yet.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>The word for a swDimensionType_e («linear», «diameter», «angular» ...); «unknown» otherwise. Pure.</summary>
        internal static string DimensionKindName(int type)
        {
            switch (type)
            {
                case (int)swDimensionType_e.swOrdinateDimension: return "ordinate";
                case (int)swDimensionType_e.swLinearDimension: return "linear";
                case (int)swDimensionType_e.swAngularDimension: return "angular";
                case (int)swDimensionType_e.swArcLengthDimension: return "arc_length";
                case (int)swDimensionType_e.swRadialDimension: return "radius";
                case (int)swDimensionType_e.swDiameterDimension: return "diameter";
                case (int)swDimensionType_e.swHorOrdinateDimension: return "horizontal_ordinate";
                case (int)swDimensionType_e.swVertOrdinateDimension: return "vertical_ordinate";
                case (int)swDimensionType_e.swZAxisDimension: return "z_axis";
                case (int)swDimensionType_e.swChamferDimension: return "chamfer";
                case (int)swDimensionType_e.swHorLinearDimension: return "horizontal";
                case (int)swDimensionType_e.swVertLinearDimension: return "vertical";
                case (int)swDimensionType_e.swScalarDimension: return "scalar";
                default: return "unknown";
            }
        }

        /// <summary>Whether a point (x, y) lies in a box (x0, y0, x1, y1); false without a box. Pure.</summary>
        internal static bool InsideBox(double x, double y, double[] box)
        {
            return box != null && box.Length >= 4 && x >= box[0] && x <= box[2] && y >= box[1] && y <= box[3];
        }

        /// <summary>A point in metres of sheet as millimetres to the hundredth; null without one.</summary>
        private static object PointMm(double[] p)
        {
            if (p == null || p.Length < 2) return null;
            return new object[] { Math.Round(p[0] * 1000, 2), Math.Round(p[1] * 1000, 2) };
        }

        /// <summary>One dimension of a finished drawing, as it is on the sheet.</summary>
        private static Dictionary<string, object> ReadOneDimension(IDisplayDimension d)
        {
            var row = new Dictionary<string, object>();
            int type = -1;
            try { type = d.Type2; } catch { }
            string kind = DimensionKindName(type);
            row["kind"] = kind;
            try
            {
                var dim = d.GetDimension2(0) as IDimension;
                if (dim != null)
                {
                    try { row["name"] = dim.FullName; } catch { }
                    double value = ApiHelpers.ReadDimensionInConfiguration(dim, "");
                    if (kind == "angular") row["deg"] = Math.Round(value * 180.0 / Math.PI, 3);
                    else row["mm"] = Math.Round(value * 1000, 3);
                }
            }
            catch { }
            row["text_shown"] = TextShown(d);
            string prefix = null, suffix = null;
            try { prefix = d.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix); } catch { }
            try { suffix = d.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix); } catch { }
            if (!string.IsNullOrEmpty(prefix)) row["prefix"] = prefix;
            if (!string.IsNullOrEmpty(suffix)) row["suffix"] = suffix;
            try { row["parentheses"] = d.ShowParenthesis; } catch { }
            int arrows = -1;
            try { arrows = d.ArrowSide; } catch { }
            row["arrows"] = arrows == (int)swDimensionArrowsSide_e.swDimArrowsInside ? "inside"
                          : arrows == (int)swDimensionArrowsSide_e.swDimArrowsOutside ? "outside"
                          : arrows == (int)swDimensionArrowsSide_e.swDimArrowsSmart ? "smart"
                          : arrows == (int)swDimensionArrowsSide_e.swDimArrowsFollowDoc ? "as_the_document" : null;
            row["position_mm"] = WhereItEnded(d);
            var attached = new List<object>();
            try
            {
                var annotation = d.GetAnnotation() as IAnnotation;
                var types = annotation == null ? null : annotation.GetAttachedEntityTypes() as int[];
                if (types != null)
                    foreach (int t in types) attached.Add(Enum.IsDefined(typeof(swSelectType_e), t) ? ((swSelectType_e)t).ToString() : t.ToString());
            }
            catch { }
            row["attached_to"] = attached.ToArray();
            return row;
        }

        /// <summary>One table of a view: its kind, where it is, its header and rows.</summary>
        private static Dictionary<string, object> ReadOneTable(ITableAnnotation t)
        {
            var row = new Dictionary<string, object> { ["kind"] = t is IBomTableAnnotation ? "bill_of_materials" : "table" };
            try { row["position_mm"] = PointMm((t.GetAnnotation() as IAnnotation)?.GetPosition() as double[]); } catch { }
            var rows = new List<object>();
            try
            {
                for (int r = 0; r < t.RowCount; r++)
                {
                    var cells = new string[t.ColumnCount];
                    for (int c = 0; c < cells.Length; c++) cells[c] = Cell(t, r, c);
                    rows.Add(cells);
                }
            }
            catch { }
            row["rows"] = rows.ToArray();
            return row;
        }

        /// <summary>One view of a finished drawing: what it shows and every annotation on it.</summary>
        private static Dictionary<string, object> ReadOneView(IView v)
        {
            var row = new Dictionary<string, object>();
            try { row["name"] = v.GetName2(); } catch { }
            int type = -1;
            try { type = v.Type; } catch { }
            row["kind"] = Enum.IsDefined(typeof(swDrawingViewTypes_e), type) ? ((swDrawingViewTypes_e)type).ToString() : null;
            try { row["scale"] = ScaleText(v.ScaleRatio as double[]); } catch { }
            try { row["model"] = v.GetReferencedModelName(); } catch { }
            try { row["configuration"] = v.ReferencedConfiguration; } catch { }
            try { row["outline_mm"] = InMillimeters(v.GetOutline() as double[]); } catch { }

            var dimensions = new List<Dictionary<string, object>>();
            try
            {
                for (var d = v.GetFirstDisplayDimension5() as IDisplayDimension; d != null; d = d.GetNext5() as IDisplayDimension)
                    dimensions.Add(ReadOneDimension(d));
            }
            catch { }
            var notes = new List<Dictionary<string, object>>();
            var balloons = new List<Dictionary<string, object>>();
            foreach (BalloonRead b in BalloonsOfView(v))
            {
                string part = null;
                try { part = b.Component?.Name2; } catch { }
                balloons.Add(new Dictionary<string, object> { ["number"] = b.Number, ["component"] = part, ["position_mm"] = PointMm(b.Position) });
            }
            try
            {
                for (var n = v.GetFirstNote() as INote; n != null; n = n.GetNext() as INote)
                {
                    if (n.IsBomBalloon()) continue;
                    string name = null, text = null;
                    try { name = n.GetName(); } catch { }
                    try { text = n.GetText(); } catch { }
                    double[] at = null;
                    try { at = (n.GetAnnotation() as IAnnotation)?.GetPosition() as double[]; } catch { }
                    notes.Add(new Dictionary<string, object> { ["name"] = name, ["text"] = text, ["position_mm"] = PointMm(at) });
                }
            }
            catch { }
            var tables = new List<Dictionary<string, object>>();
            try
            {
                foreach (object o in (v.GetTableAnnotations() as object[]) ?? new object[0])
                {
                    var t = o as ITableAnnotation;
                    if (t != null) tables.Add(ReadOneTable(t));
                }
            }
            catch { }
            row["dimensions"] = dimensions.ToArray();
            row["notes"] = notes.ToArray();
            row["balloons"] = balloons.ToArray();
            row["tables"] = tables.ToArray();
            return row;
        }

        private Outcome ReadDrawing(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed("The active document is not a drawing: «read_drawing» reads the sheets of a drawing. Open the drawing and try again.");
            string only = Str(a, "sheet", null);
            if (string.IsNullOrWhiteSpace(only)) only = null;

            object[] perSheet = null;
            string[] sheetNames = null;
            try { perSheet = drawing.GetViews() as object[]; } catch { }
            try { sheetNames = drawing.GetSheetNames() as string[]; } catch { }
            if (perSheet == null || perSheet.Length == 0)
                return Failed("The drawing did not hand over its sheets (IDrawingDoc.GetViews gave nothing): I cannot read it. I touched nothing: try again once the drawing answers.");
            bool namesMatch = sheetNames != null && sheetNames.Length == perSheet.Length;
            if (only != null && (!namesMatch || Array.IndexOf(sheetNames, only) < 0))
                return Failed("There is no sheet «" + only + "»: the sheets are " +
                              (sheetNames == null ? "not readable" : string.Join(", ", sheetNames)) + ". Use one of them in «sheet», or leave «sheet» out for every sheet. I touched nothing.");

            var sheets = new List<Dictionary<string, object>>();
            int views = 0, dimensions = 0, notes = 0, balloons = 0, tables = 0, inTitleBlock = 0;
            for (int s = 0; s < perSheet.Length; s++)
            {
                var list = perSheet[s] as object[];
                if (list == null || list.Length == 0) continue;
                string name = namesMatch ? sheetNames[s] : null;
                if (only != null && name != only) continue;
                var sheetRow = new Dictionary<string, object> { ["name"] = name };
                ISheet sheet = null;
                try { if (name != null) sheet = drawing.get_Sheet(name) as ISheet; } catch { }
                if (sheet != null)
                {
                    sheetRow["scale"] = ScaleText(ReadScale(sheet));
                    sheetRow["size_mm"] = SheetMeasurements(sheet);
                }
                double[] block = null;
                try
                {
                    var tb = sheet?.TitleBlock;
                    if (tb != null)
                    {
                        double x0 = 0, y0 = 0, x1 = 0, y1 = 0;
                        tb.GetExtents(ref x0, ref y0, ref x1, ref y1);
                        block = ExtentBox(new[] { x0, y0, 0, x1, y1, 0 });
                    }
                }
                catch { }
                sheetRow["title_block_mm"] = InMillimeters(block);

                // The sheet itself first: its notes, the ones in the title block counted only.
                var sheetNotes = new List<Dictionary<string, object>>();
                int blockNotes = 0;
                var sheetView = list[0] as IView;
                try
                {
                    for (var n = sheetView == null ? null : sheetView.GetFirstNote() as INote; n != null; n = n.GetNext() as INote)
                    {
                        double[] at = null;
                        try { at = (n.GetAnnotation() as IAnnotation)?.GetPosition() as double[]; } catch { }
                        if (at != null && InsideBox(at[0], at[1], block)) { blockNotes++; continue; }
                        string text = null, noteName = null;
                        try { text = n.GetText(); } catch { }
                        try { noteName = n.GetName(); } catch { }
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        sheetNotes.Add(new Dictionary<string, object> { ["name"] = noteName, ["text"] = text, ["position_mm"] = PointMm(at) });
                    }
                }
                catch { }
                var sheetTables = new List<Dictionary<string, object>>();
                try
                {
                    foreach (object o in (sheetView == null ? null : sheetView.GetTableAnnotations() as object[]) ?? new object[0])
                    {
                        var t = o as ITableAnnotation;
                        if (t != null) sheetTables.Add(ReadOneTable(t));
                    }
                }
                catch { }
                var viewRows = new List<Dictionary<string, object>>();
                for (int i = 1; i < list.Length; i++)
                {
                    var v = list[i] as IView;
                    if (v == null) continue;
                    Dictionary<string, object> vr = ReadOneView(v);
                    viewRows.Add(vr);
                    dimensions += ((object[])vr["dimensions"]).Length;
                    notes += ((object[])vr["notes"]).Length;
                    balloons += ((object[])vr["balloons"]).Length;
                    tables += ((object[])vr["tables"]).Length;
                }
                views += viewRows.Count;
                notes += sheetNotes.Count;
                tables += sheetTables.Count;
                inTitleBlock += blockNotes;
                sheetRow["notes"] = sheetNotes.ToArray();
                sheetRow["notes_in_title_block"] = block == null ? null : (object)blockNotes;
                sheetRow["tables"] = sheetTables.ToArray();
                sheetRow["views"] = viewRows.ToArray();
                sheets.Add(sheetRow);
            }
            var measurements = new Dictionary<string, object>
            {
                ["sheets_read"] = sheets.Count,
                ["sheets"] = sheets.ToArray(),
                ["views_total"] = views,
                ["dimensions_total"] = dimensions,
                ["notes_total"] = notes,
                ["balloons_total"] = balloons,
                ["tables_total"] = tables,
                ["notes_in_title_blocks"] = inTitleBlock,
                ["what_it_does_not_read"] =
                    "The lines of the drawing, the hatching, the centre marks and the symbols of finish, datum and tolerance " +
                    "are not listed; a note whose text is empty is left out; the notes of a sheet inside the title block its " +
                    "format declares are counted, not listed. Nothing is changed and no sheet is brought in front.",
            };
            return SucceededWithoutCreating(measurements);
        }
    }
}
