using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// TAKES A TABLE AWAY FROM A DRAWING: «delete_annotation» with «table» (0.131.0, item 6 of the
    /// drawing branch, ported into library 0.199.0).
    ///
    /// The proof session of 12/10/2026 put the hole table on the front view and had no way back
    /// but redoing the whole drawing. Branch 0.131.0 answered with `move_view` and
    /// `delete_drawing_item` (a view, a dimension or a table). The main line had answered the same
    /// report its own way since: `move_view`, `delete_view` and `delete_annotation` (a dimension or a
    /// note). What it could not take away was a TABLE, and that is the part kept here, as an argument
    /// of `delete_annotation`.
    ///
    /// GUIDE PAGES (sldworksapi.chm, extracted):
    ///  - IView~GetTableAnnotations for the walk: the tables of the sheet sit on its first «view»
    ///    (the sheet itself), the others on their view;
    ///  - IAnnotation~Select3 and IModelDocExtension~DeleteSelection2 with swDelete_Absorbed, through
    ///    `RemoveTable` (HoleTableOperations.cs), the way the hole table already takes away a table it
    ///    refuses (EditDelete may open a confirmation window and the COM call then never returns).
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>The tables of the drawing in sheet order: view by view, the sheet first.</summary>
        private static List<ITableAnnotation> TablesInOrder(IDrawingDoc drawing)
        {
            var outside = new List<ITableAnnotation>();
            try
            {
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    foreach (var o in (v.GetTableAnnotations() as object[]) ?? new object[0])
                    {
                        var t = o as ITableAnnotation;
                        if (t != null) outside.Add(t);
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return outside;
        }

        private static string TableKind(ITableAnnotation t)
        {
            try
            {
                switch (t.Type)
                {
                    case (int)swTableAnnotationType_e.swTableAnnotation_HoleChart: return "hole table";
                    case (int)swTableAnnotationType_e.swTableAnnotation_BillOfMaterials: return "bill of materials";
                    default: return "table (type " + t.Type + ")";
                }
            }
            catch { return "table"; }
        }

        /// <summary>
        /// Takes away the table «T1», «T2»... (the order of <see cref="TablesInOrder"/>) and counts the
        /// tables before and after: «removed» only when the count went down.
        /// </summary>
        private Outcome DeleteTable(IModelDoc2 model, IDrawingDoc drawing, string name)
        {
            var tables = TablesInOrder(drawing);
            var listing = new List<string>();
            for (int i = 0; i < tables.Count; i++) listing.Add("T" + (i + 1) + " (" + TableKind(tables[i]) + ")");
            if (tables.Count == 0)
                return Failed("The drawing has no table to take away: put the table first with «hole_table» or «drawing_bom».");
            // «T» and a number, nothing else: a plain parse, no pattern.
            bool shaped = name.Length >= 2 && (name[0] == 'T' || name[0] == 't');
            int k = 0;
            if (!shaped || !int.TryParse(name.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out k) || k < 1 || k > tables.Count)
                return Failed("There is no table «" + name + "». Use one of the " + tables.Count + " there are: " + string.Join(", ", listing) + ".");

            int before = DrawingTables(drawing);
            string kindOf = TableKind(tables[k - 1]);
            string result = RemoveTable(model, drawing, tables[k - 1], before);
            int after = DrawingTables(drawing);
            if (before < 0 || after < 0 || after >= before)
                return Failed("The table T" + k + " (" + kindOf + ") is still on the drawing (tables " + before + " -> " + after + ")." + result +
                              " Try again, or remove it by hand.");
            _log("[agent] table T" + k + " (" + kindOf + ") taken away");
            return WithoutFeature(new Dictionary<string, object>
            {
                ["kind"] = "table",
                ["removed"] = "T" + k,
                ["table_kind"] = kindOf,
                ["tables_before"] = before,
                ["tables_after"] = after,
            });
        }
    }
}
