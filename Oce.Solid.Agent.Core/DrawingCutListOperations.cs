using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE CUT LIST IN THE DRAWING (entry GT2).
    ///
    /// ── WHY IT IS THE ENTRY WITH THE BEST RATIO ─────────────────────────
    ///
    /// The hard calculation — lengths and quantities of the profiles of a
    /// steelwork — **is already done and measured** by `cut_list`. Here that datum is
    /// brought into the drawing. And above all: **the independent source of
    /// truth already exists**, so the table is not declared inserted,
    /// it is COMPARED.
    ///
    /// ── THE SIGNATURE THAT THE ENTRY ASKED TO FIND ──────────────────────
    ///
    /// The entry said: *«`InsertTableAnnotation2` with a TableTemplate of type
    /// WeldmentCutList, **or** `IWeldmentCutListAnnotation` if it exposes a
    /// direct insertion — to be verified which»*. **Neither of the two.**
    ///
    ///     IView :: WeldmentCutListAnnotation InsertWeldmentTable(
    ///         Boolean UseAnchorPoint, Double X, Double Y, Int32 AnchorType,
    ///         String Configuration, String TableTemplate)
    ///
    /// It is on `IView` — the fourth time in two days that a table is on
    /// an interface different from the one the name suggests (bill of materials on
    /// `IView`, hole table on `IView`, revision table on `ISheet`).
    /// `IWeldmentCutListAnnotation` has **seven methods and none touches the
    /// cells**: to read the content one goes through `ITableAnnotation`, which the
    /// same object implements.
    ///
    /// ── THE QUANTITY COLUMN IS NOT GUESSED ──────────────────────────────
    ///
    /// `GetColumnCustomProperty(i)` says **which property is behind each
    /// column**. Looking for the column «QTY» by position, or by header,
    /// would mean summing the wrong column on a table template
    /// different from the house one — and a cut list with the quantities of the
    /// column «length» is plausible, gets printed, and in the workshop the
    /// wrong number of bars is cut.
    ///
    /// ── AND THE COMPARISON, WHICH IS THE POINT ──────────────────────────
    ///
    /// The table says how many bars there are. The part already knows, on its
    /// own, from the cut list folders (`GetBodyCount`). **Two independent
    /// readings of the same fact**: if they diverge, one of the two lies and
    /// it is not known which — and this is said instead of choosing the more convenient.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome DrawingCutList(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "The active document is not a drawing: the cut list in a drawing " +
                    "needs a drawing with a view of the weldment in it.");

            var names = ViewNames(drawing);
            if (names.Count == 0)
                return Failed(
                    "The drawing has no views: a cut list attaches to a view " +
                    "of the part. Put one with «standard_views».");

            // THE VIEW LIKE THE OTHER DRAWING OPERATIONS: by uid (V1, V2...) or
            // by name. Up to 0.124.0 only by name — «V1», as the state calls it,
            // answered «it is not there» (seen by the first bench case that
            // called it, 26/9/2026).
            string reason;
            var view = ResolveView(drawing, names, Str(a, "view", null), out reason);
            if (reason != null) return Failed(reason);
            if (view == null)
                return Failed("I cannot get the view of the drawing.");

            string viewName = null;
            try { viewName = view.Name; } catch { }

            // ── THE PART BEHIND THE VIEW, AND ITS CUT LIST ──────────────
            //
            // It is read BEFORE inserting: it is the number against which the table will be
            // compared, and taking it afterwards would mean taking it from a
            // document that the insertion might have touched.
            int bodiesInPart = -1;
            string partPath = null;
            try
            {
                var refObj = view.ReferencedDocument as IModelDoc2;
                if (refObj != null)
                {
                    partPath = refObj.GetPathName();
                    bodiesInPart = BodiesInCutListFolders(refObj);
                }
            }
            catch { }

            if (bodiesInPart == 0)
                return Failed(
                    "The part behind the view has no cut list folder: the " +
                    "table would be created empty. Ask for «cut_list» " +
                    "on the part first.");

            double x = Units.M(Num(a, "x_mm", 250));
            double y = Units.M(Num(a, "y_mm", 200));
            // THE CONFIGURATION IS STILL THE CALLER'S, OR EMPTY (Z-C21.4, finding
            // M32, open). IView~InsertWeldmentTable.html: «Configuration Name of
            // the "As Welded" configuration for the weldment cut-list table».
            // The view's own configuration (IView.ReferencedConfiguration) is NOT
            // passed in its place: the view shows the configuration that was
            // active when it was made, and a part of the company template is born
            // with the machined one active, not the as-welded one (Z-C21.3 probe
            // of L8-5); what InsertWeldmentTable does with it was never measured, while
            // the empty name makes the table of the bench case
            // cut_list_in_the_drawing. Which configuration SOLIDWORKS
            // takes for the empty name is not measured either. A probe decides
            // it first; the count it is compared with (BodiesInCutListFolders)
            // is read in the part's active configuration.
            string configuration = Str(a, "configuration", "");
            string template = Str(a, "template", "");

            object raw;
            try
            {
                raw = view.InsertWeldmentTable(
                    false,              // UseAnchorPoint: it is placed where it is said
                    x, y,
                    (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_TopLeft,
                    configuration ?? "",
                    template ?? "");
            }
            catch (Exception ex)
            {
                return Failed("Cut list not inserted in the drawing: " + ex.Message);
            }
            if (raw == null)
                return Failed(
                    "SOLIDWORKS did not create any table and did not say why. " +
                    "Usually it means that the part behind the view is not a " +
                    "weldment with a cut list.");

            // The same COM object implements both interfaces: that
            // of the cut list (seven methods, none on the cells) and that of the
            // table (the cells). Both are taken.
            var cutList = raw as IWeldmentCutListAnnotation;
            var table = raw as ITableAnnotation;
            if (table == null)
                return Failed(
                    "The table was created but it cannot be read as a table: without " +
                    "reading it I cannot say what is inside, and I do not call it done.");

            int lines = 0, columns = 0;
            try { lines = table.RowCount; } catch { }
            try { columns = table.ColumnCount; } catch { }
            if (lines <= 1)
                return Failed(
                    "The table was created with " + lines + " records: it is empty or has " +
                    "only the header. An empty cut list in a drawing is worse than no cut " +
                    "list, because it gets printed and it looks as if the metalwork had no " +
                    "bars.");

            // ── THE QUANTITY COLUMN, ASKED FOR AND NOT GUESSED ──────────
            //
            // FIRST BY TYPE, THEN BY NAME (entry Z-C21.2, finding L16-8).
            // IWeldmentCutListAnnotation~GetColumnCustomProperty.html, Remarks:
            // «This method returns an empty string if the column is not a
            // user-defined column». The QTY of the default table template
            // (template "", that is «cut list.sldwldtbt») can be a column of
            // quantity TYPE, not user-defined: looked for only by property
            // name it stayed -1, the comparison with the cut list of the part
            // — the reason why the operation exists — was skipped, and the answer
            // was a success with a note at the bottom. The type is read with
            // ITableAnnotation~GetColumnType2.html («Type of column as defined by
            // swTableColumnTypes_e»; swWeldTableColumnType_Quantity = 402), as
            // ColumnsAndPosition already does below. The name «QUANTITY» stays the
            // fallback: the workshop template has the QTY as properties.
            int quantityColumn = -1;
            string asFound = null;
            var columnProperty = new List<string>();
            for (int c = 0; c < columns; c++)
            {
                string p = null;
                try { if (cutList != null) p = cutList.GetColumnCustomProperty(c); }
                catch { }
                columnProperty.Add(p ?? "");
                int columnType = -1;
                try { columnType = table.GetColumnType2(c, true); } catch { }
                if (quantityColumn < 0 &&
                    columnType == (int)swTableColumnTypes_e.swWeldTableColumnType_Quantity)
                {
                    quantityColumn = c;
                    asFound = "type";
                }
            }
            for (int c = 0; quantityColumn < 0 && c < columns; c++)
            {
                string p = columnProperty[c];
                if (!string.IsNullOrWhiteSpace(p) &&
                    p.IndexOf("QUANTITY", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    quantityColumn = c;
                    asFound = "name";
                }
            }

            var headers = new List<string>();
            for (int c = 0; c < columns; c++) headers.Add(Cell(table, 0, c));

            // The rows, as text. Nothing is interpreted: what is written is
            // reported, which is what ends up on the sheet.
            var entries = new List<object>();
            int totalQuantity = 0;
            bool quantityRead = quantityColumn >= 0;
            for (int r = 1; r < lines; r++)
            {
                var cells = new List<string>();
                for (int c = 0; c < columns; c++) cells.Add(Cell(table, r, c));
                entries.Add(cells.ToArray());

                if (quantityColumn >= 0)
                {
                    int n;
                    if (int.TryParse(Cell(table, r, quantityColumn).Trim(),
                                     NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                        totalQuantity += n;
                    else quantityRead = false;
                }
            }

            var measurements = new Dictionary<string, object>
            {
                ["view"] = viewName ?? "(no name)",
                ["rows"] = lines,
                ["columns"] = columns,
                ["entries"] = lines - 1,
                ["headers"] = headers.ToArray(),
                ["column_properties"] = columnProperty.ToArray(),
                ["rows_read"] = entries.ToArray(),
            };
            if (!string.IsNullOrWhiteSpace(partPath)) measurements["part"] = partPath;
            if (bodiesInPart >= 0) measurements["bodies_in_part_cut_list"] = bodiesInPart;
            if (quantityRead) measurements["quantity_in_table"] = totalQuantity;
            // DEDICATED FIELDS, not a sentence at the end (finding L16-8). New names,
            // in English like the next ones of the bridge: how the quantity column
            // was found («type», «name» or null) and whether the comparison with the
            // cut list of the part was made.
            measurements["quantity_column_found_by"] = asFound == "type" ? "type" : asFound == "name" ? "name" : null;
            measurements["comparison_done"] = quantityRead && bodiesInPart > 0;

            // ── THE COLUMNS THAT ARE NEEDED, AND A PLACE ON THE SHEET ───
            //
            // SEEN IN THE PNG on 26/9/2026, with the bench green: the workshop's
            // table template has 17 columns (author, code, treatment, supply
            // state, QUANTITY and TOTAL LENGTH duplicates...), and the table put
            // at 250/200 ran off the sheet after the fourth. The rows could all be
            // read — the check looked at the cells, not at where they fell.
            // They are read first (above) with all the columns; then those that
            // are not needed in the workshop are hidden, the table is measured and it is
            // put in a free place inside the frame.
            string placeReason;
            if (!ColumnsAndPosition(drawing, table, columns, columnProperty, headers, a, measurements, out placeReason))
            {
                bool removed = RemoveCutTable(model, table);
                return Failed(placeReason + (removed ? " I removed the table." : " Remove the table: " + Sentences.HowToWithdrawIt + "."));
            }

            // ── THE COMPARISON ─────────────────────────────────────────
            //
            // It is the reason why this entry was worth it: the second
            // reading already existed. It is not chosen which of the two to believe —
            // it is said that they do not match.
            if (quantityRead && bodiesInPart > 0)
            {
                measurements["match"] = totalQuantity == bodiesInPart;
                if (totalQuantity != bodiesInPart)
                {
                    measurements["verdict"] =
                        "WARNING: the quantity column of the table in the drawing adds up to " + totalQuantity +
                        (totalQuantity == 1 ? " bar" : " bars") + ", and the cut list " +
                        "of the part has " + bodiesInPart + ". **The two do not match.** I " +
                        "do not choose which one to believe: a drawing that says a different " +
                        "number from the model sends the wrong quantity to the workshop, " +
                        "and neither side gives an error. Usually it means that the part " +
                        "changed after the last update of the cut list: do «cut_list» " +
                        "again on the part and then put the table back.";
                    _log("[agent] " + measurements["verdict"]);
                    return SucceededWithoutCreating(measurements);
                }
            }

            measurements["verdict"] =
                "Cut list in the drawing on the view «" + (viewName ?? "?") + "»: " +
                (lines - 1) + (lines - 1 == 1 ? " entry" : " entries") +
                (quantityRead
                    ? ", " + totalQuantity + (totalQuantity == 1 ? " bar" : " bars")
                    : "") +
                (quantityRead && bodiesInPart > 0
                    ? ", and it matches the cut list of the part (" + bodiesInPart + ")."
                    : ". The comparison with the cut list of the part could not be made.") +
                (quantityColumn < 0
                    ? " NOTE: I did not find the quantity column either by type or among " +
                      "the column properties, so I added up nothing (comparison_done: false)."
                    : "");
            _log("[agent] " + measurements["verdict"]);
            return SucceededWithoutCreating(measurements);
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// A cell as text. `get_Text` can raise on a merged or
        /// out-of-range cell: empty, and whoever reads sees an empty cell instead of
        /// losing the whole table.
        /// </summary>
        private static string Cell(ITableAnnotation t, int line, int column)
        {
            try { return WithoutFormatCodes(t.Text[line, column] ?? ""); }
            catch { return ""; }
        }

        /// <summary>
        /// A cell without the FONT codes of SOLIDWORKS notes (report 8cd843 N21,
        /// 27/10/2026): the headers of a bill of materials came from
        /// ITableAnnotation.Text with «&lt;FONT size=3.5&gt;» before the name of
        /// the column. Only «&lt;FONT ...&gt;»
        /// and «&lt;/FONT&gt;» are taken out, the one kind measured; any other text
        /// stays as it is. No interop type: Oce.Solid.Agent.ContractTests calls
        /// it by reflection.
        /// </summary>
        internal static string WithoutFormatCodes(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            string s = text;
            int from = 0;
            while (from < s.Length)
            {
                int at = s.IndexOf("<FONT", from, StringComparison.OrdinalIgnoreCase);
                if (at < 0) break;
                int next = at + 5;
                int end = s.IndexOf('>', at);
                if (end < 0) break;
                if (next < s.Length && (s[next] == ' ' || s[next] == '>')) s = s.Remove(at, end - at + 1);
                else from = next;
            }
            int close;
            while ((close = s.IndexOf("</FONT>", StringComparison.OrdinalIgnoreCase)) >= 0) s = s.Remove(close, 7);
            return s;
        }

        /// <summary>
        /// The roles of the column properties that are needed in the workshop to
        /// cut: the length, the two angles, the description (the profile). With
        /// the item number and the quantity, which are columns of TYPE
        /// (`swWeldTableColumnType_*`) and need no name at all.
        ///
        /// A property of the cut list has the name SOLIDWORKS gave it in the
        /// language it ran in when the cut list (or the profile, or the table
        /// template) was made, so the names are those of the table
        /// (solidworks-names.json, keys cutlist.property.*) in EVERY column: a
        /// template made in another language than the interface still carries its
        /// own words, and recognising all of them is the robust choice.
        /// </summary>
        private static readonly string[] WorkshopRoles = { "length", "angle1", "angle2", "description" };

        /// <summary>
        /// The role of a column of the table of the cut list, told by its TYPE
        /// first and by the name of its property (every language of the table)
        /// second: "item_number", "quantity", "length", "angle1", "angle2",
        /// "description", or null for a column the workshop does not need.
        /// </summary>
        internal static string ColumnRole(int columnType, string property)
        {
            if (columnType == (int)swTableColumnTypes_e.swWeldTableColumnType_ItemNumber) return "item_number";
            if (columnType == (int)swTableColumnTypes_e.swWeldTableColumnType_Quantity) return "quantity";
            string itsName = (property ?? "").Trim();
            if (itsName.Length == 0) return null;
            var names = SolidWorksNameTable.Embedded();
            foreach (var role in WorkshopRoles)
                foreach (var spelling in names.All("cutlist.property." + role))
                    if (string.Equals(spelling, itsName, StringComparison.OrdinalIgnoreCase)) return role;
            return null;
        }

        /// <summary>
        /// Hides the columns that are not needed (or keeps those asked for with
        /// «columns»), measures the table and puts it in a free place of the
        /// sheet: inside the frame, outside the views and the title block. False
        /// with the reason if the place is not there or if a column asked for does not exist.
        /// </summary>
        private static bool ColumnsAndPosition(IDrawingDoc drawing, ITableAnnotation table, int columns,
                                          List<string> property, List<string> headers,
                                          Dictionary<string, object> a, Dictionary<string, object> measurements, out string reason)
        {
            reason = null;
            // ── 1. WHICH COLUMNS ────────────────────────────────────────────
            // THE TYPE OF EVERY COLUMN, read once (ITableAnnotation~GetColumnType2).
            var types = new int[columns];
            for (int c = 0; c < columns; c++)
            {
                types[c] = -1;
                try { types[c] = table.GetColumnType2(c, true); } catch { }
            }
            var requestedItems = StringList(a, "columns");
            bool all = requestedItems.Any(c => string.Equals((c ?? "").Trim(), "all", StringComparison.OrdinalIgnoreCase));
            var keep = new bool[columns];
            if (all)
                for (int c = 0; c < columns; c++) keep[c] = true;
            else if (requestedItems.Count > 0)
            {
                foreach (var ch in requestedItems)
                {
                    int i = headers.FindIndex(t => string.Equals((t ?? "").Trim(), (ch ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                    if (i < 0)
                    {
                        reason = "The table has no column «" + ch + "». It has: " + string.Join(", ", headers) +
                                 " (or ask for «all»).";
                        return false;
                    }
                    keep[i] = true;
                }
            }
            else
            {
                int held = 0;
                for (int c = 0; c < columns; c++)
                {
                    keep[c] = ColumnRole(types[c], property[c]) != null;
                    if (keep[c]) held++;
                }
                // A table template that is not recognised stays as it is: better
                // all the columns than a table without lengths.
                if (held < 3) for (int c = 0; c < columns; c++) keep[c] = true;
            }
            var visible = new List<string>();
            var hiddenOnes = new List<string>();
            for (int c = columns - 1; c >= 0; c--)   // from the last one: hiding does not move those before
            {
                if (keep[c]) continue;
                try { table.ColumnHidden[c] = true; } catch { }
            }
            // THEY ARE READ BACK (review of 26/9/2026): an ignored assignment
            // would leave all 17 columns in the drawing with the answer saying
            // the opposite.
            for (int c = 0; c < columns; c++)
            {
                bool hidden = false;
                try { hidden = table.ColumnHidden[c]; } catch { }
                (hidden ? hiddenOnes : visible).Add(headers[c]);
                if (hidden == keep[c])
                {
                    reason = "The column «" + headers[c] + "» had to be " + (keep[c] ? "visible" : "hidden") +
                             " and it is not: the table is not the one I would tell you about.";
                    return false;
                }
            }
            measurements["columns_visible"] = visible.ToArray();
            measurements["columns_hidden"] = hiddenOnes.ToArray();
            // THE SAME THING BY ROLE, which does not depend on what the template
            // prints in the header nor on the language of the property names:
            // «length», «quantity»... (the cases of the bench assert this one).
            var rolesVisible = new List<string>();
            for (int c = 0; c < columns; c++)
            {
                bool hiddenNow = false;
                try { hiddenNow = table.ColumnHidden[c]; } catch { }
                if (hiddenNow) continue;
                string role = ColumnRole(types[c], property[c]);
                if (role != null && !rolesVisible.Contains(role)) rolesVisible.Add(role);
            }
            measurements["workshop_columns_visible"] = rolesVisible.ToArray();

            // ── 2. HOW BIG IT IS ────────────────────────────────────────────
            double w = 0, h = 0;
            try
            {
                int nc = table.ColumnCount, nr = table.RowCount;
                for (int c = 0; c < nc; c++) w += table.GetColumnWidth(c);
                for (int r = 0; r < nr; r++) h += table.GetRowHeight(r);
            }
            catch { }
            if (w <= 0 || h <= 0)
            {
                reason = "The table was created but I cannot measure it: without a measurement I do not know whether it fits in the sheet.";
                return false;
            }
            measurements["bounding_box_table_mm"] = new object[] { Math.Round(w * 1000, 1), Math.Round(h * 1000, 1) };

            // ── 3. DOVE (SheetPlacementOperations) ──────────────────────────
            string tableName = null;
            try { tableName = (table.GetAnnotation() as IAnnotation)?.GetName(); } catch { }
            var sheet = ReadSheet(drawing, null, tableName);
            measurements["title_block_estimated"] = sheet.EstimatedTitleBlock;
            bool requested = a.ContainsKey("x_mm") || a.ContainsKey("y_mm");
            double px, py; string location;
            if (requested)
            {
                // x_mm/y_mm are the TOP left corner of the table.
                px = Units.M(Num(a, "x_mm", 250)); py = Units.M(Num(a, "y_mm", 200));
                string why = sheet.Touches(px, py - h, px + w, py);
                if (why != null)
                {
                    reason = string.Format(CultureInfo.InvariantCulture,
                        "The table ({0:0} x {1:0} mm) put at {2:0}/{3:0} {4}. Remove «x_mm» and «y_mm» and I put " +
                        "it in a free place, or hide columns with «columns».", w * 1000, h * 1000, px * 1000, py * 1000, why);
                    return false;
                }
                location = "where requested";
            }
            else
            {
                var down = FreePlace(sheet, w, h, out location);
                if (down == null)
                {
                    reason = string.Format(CultureInfo.InvariantCulture,
                        "On the sheet there is no free place for the table ({0:0} x {1:0} mm) outside the views " +
                        "and the title block. Hide columns with «columns», or use a bigger sheet.", w * 1000, h * 1000);
                    return false;
                }
                px = down[0]; py = down[1] + h;   // the anchor is at the top left
            }
            double[] read = null;
            try
            {
                var an = table.GetAnnotation() as IAnnotation;
                an?.SetPosition2(px, py, 0);
                read = an?.GetPosition() as double[];
            }
            catch { }
            if (read == null || Math.Abs(read[0] - px) > 0.0005 || Math.Abs(read[1] - py) > 0.0005)
            {
                reason = "I tried to move the table " + location + " but it did not move (read back: " +
                         (read == null ? "nothing" : string.Format(CultureInfo.InvariantCulture, "{0:0.#}/{1:0.#} mm", read[0] * 1000, read[1] * 1000)) + ").";
                return false;
            }
            measurements["position_table_mm"] = new object[] { Math.Round(px * 1000, 1), Math.Round(py * 1000, 1) };
            measurements["table_location"] = location;
            measurements["inside_sheet"] = true;
            return true;
        }

        /// <summary>
        /// Removes the table just put. `DeleteSelection2` and not `EditDelete`
        /// (the second can open a confirmation window).
        /// </summary>
        private static bool RemoveCutTable(IModelDoc2 model, ITableAnnotation table)
        {
            try
            {
                model.ClearSelection2(true);
                var an = table.GetAnnotation() as IAnnotation;
                bool removed = an != null && an.Select3(false, null) &&
                             model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
                return removed;
            }
            catch { return false; }
        }

        /// <summary>
        /// How many bodies are in the cut list folders of the part.
        /// `-1` when it could not be read — which is not zero.
        ///
        /// ONE WELDMENT ONLY (Z-C21.4, finding M32, open). The walk is
        /// FirstFeature / GetNextFeature, the top of the tree: the cut-list
        /// folders of a sub-weldment are not descended into, so on a part with
        /// sub-weldments their bodies are not counted and the comparison of
        /// drawing_cut_list can report a mismatch that is this count's.
        /// Descending (GetFirstSubFeature of the sub-weldment folders) waits for
        /// a probe on a part with a sub-weldment: whether those folders also
        /// show up in the top-level walk, and would be counted twice, is not
        /// known, and no bench case can build a sub-weldment yet.
        /// </summary>
        private static int BodiesInCutListFolders(IModelDoc2 part)
        {
            try
            {
                int n = 0;
                bool found = false;
                var f = part.FirstFeature() as IFeature;
                while (f != null)
                {
                    string typ = null;
                    try { typ = f.GetTypeName2(); } catch { }
                    if (typ == "CutListFolder")
                    {
                        var folder = f.GetSpecificFeature2() as IBodyFolder;
                        if (folder != null)
                        {
                            found = true;
                            try { n += folder.GetBodyCount(); } catch { }
                        }
                    }
                    f = f.GetNextFeature() as IFeature;
                }
                return found ? n : 0;
            }
            catch { return -1; }
        }
    }
}
