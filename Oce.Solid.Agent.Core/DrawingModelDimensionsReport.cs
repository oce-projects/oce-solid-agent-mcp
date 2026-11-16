using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// WHAT `model_dimensions` DID NOT BRING, AND WHY (0.131.0, item 2 of the drawing branch).
    ///
    /// ── THE QUESTION OF 12/10/2026 ───────────────────────────────────────────────────
    ///
    /// On the L bracket (sheet metal made by `base_flange`) the import brought 7 dimensions, then 3 more with
    /// all_dimensions, and none of them was the 120, the 70, the 8 or the R8. The answer said «done» with a count and
    /// nothing about what the part has that the drawing does not.
    ///
    /// ── WHAT WAS MEASURED (Checks/Draw131 «dims» and «marks», the saved sheet, 12/10/2026) ──────────
    ///
    /// The part has 14 dimensions of its features and ALL of them are marked for the drawing. They are the depth 60
    /// (D2@Base-Flangia2) and a D3@Base-Flangia2 that reads 0, the hole wizard (Ø11, depth 8, 30 and 90), the slot (R5.5, 19,
    /// 35: twice, once on the sketch and once on the cut) and the chamfer (1 and 45°). There is no 120, no 70, no 8 and no
    /// R8 among them:
    ///
    ///  - 120 and 70 are not dimensions of the model. `base_flange` draws the profile with CreateLine (SheetMetalOperations)
    ///    and passes it to InsertSheetMetalBaseFlange2: the sketch of the base flange has no dimension, and Insert Model
    ///    Items has nothing to bring. The flat length 176.85 is computed from the lines, it is not a dimension either.
    ///  - the thickness and the bend radius belong to the «SheetMetal» feature (ISheetMetalFeatureData.Thickness and
    ///    BendRadius): the feature has no display dimension (the walk over the display dimensions of every feature of the
    ///    part finds none on it).
    ///
    /// Marking every dimension in memory and importing with the MARKED mask, with all the masks together, brings the same six
    /// dimensions (the 60, the slot, the chamfer): the flag is not what keeps them out. So no flag of the import
    /// brings them, and nothing here pretends otherwise. The way to get them on the drawing is `drawing_dimension`, which
    /// dimensions the overall size between the extreme edges, the thickness of the sheet and the inner bend radius
    /// (DrawingOverallOperations.cs). This report says so in the answer instead of leaving the reader to find out from the
    /// sheet, and lists, dimension by dimension, which dimensions of the part are in the drawing and which are not.
    /// </summary>
    public partial class SwOperations
    {
        private void AddPartDimensionsReport(IDrawingDoc drawing, Dictionary<string, object> measurements)
        {
            try
            {
                string ignore;
                string path = ModelPathInView(drawing) ?? ModelToPutInView(out ignore);
                var partDimensions = ModelDimensionNames(path);   // «D1@Sketch1 [marked]»

                // What the drawing carries, by the name the model gave each dimension («D1@Sketch1@Part.SLDPRT»).
                var inDrawing = new List<string>();
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    var d = v.GetFirstDisplayDimension5() as IDisplayDimension;
                    while (d != null)
                    {
                        try
                        {
                            var dim = d.GetDimension2(0) as IDimension;
                            string full = dim == null ? null : dim.FullName;
                            if (!string.IsNullOrEmpty(full)) inDrawing.Add(full);
                        }
                        catch { }
                        d = d.GetNext5() as IDisplayDimension;
                    }
                    v = v.GetNextView() as IView;
                }

                // The dimensions this call brought and took away again as duplicates (TidyImportedDimensions): they are not on the drawing, and the
                // reason is that the drawing says the same thing already, not that no view shows them.
                var takenAway = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                object removedRows;
                if (measurements.TryGetValue("imported_dimensions_removed", out removedRows) && removedRows is object[])
                    foreach (object row in (object[])removedRows)
                    {
                        var oneRow = row as Dictionary<string, object>;
                        if (oneRow == null || !oneRow.ContainsKey("name") || !oneRow.ContainsKey("why")) continue;
                        takenAway[Convert.ToString(oneRow["name"])] = Convert.ToString(oneRow["why"]);
                    }
                var notInserted = new List<object>();
                foreach (var entry in partDimensions)
                {
                    string name = entry;
                    bool marked = false;
                    int cut = entry.IndexOf(" [", StringComparison.Ordinal);
                    if (cut > 0) { name = entry.Substring(0, cut); marked = entry.EndsWith("[marked]", StringComparison.Ordinal); }
                    bool present = inDrawing.Exists(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase) ||
                                                         f.StartsWith(name + "@", StringComparison.OrdinalIgnoreCase));
                    if (present) continue;
                    string removedBecause = null;
                    foreach (string taken in takenAway.Keys)
                        if (string.Equals(taken, name, StringComparison.OrdinalIgnoreCase) || taken.StartsWith(name + "@", StringComparison.OrdinalIgnoreCase))
                            removedBecause = takenAway[taken];
                    notInserted.Add(new Dictionary<string, object>
                    {
                        ["name"] = name,
                        ["marked_for_drawing"] = marked,
                        ["why"] = removedBecause != null
                            ? "it arrived and was taken away again by this call: " + removedBecause
                            : marked
                            ? "it is marked for the drawing and no view of the drawing carries it: it belongs to a feature that no view shows, or it was the same as another one (duplicates are eliminated)"
                            : "it is not marked for the drawing: pass all_dimensions to import it",
                    });
                }
                measurements["part_dimensions"] = partDimensions.Count;
                measurements["part_dimensions_not_in_drawing"] = notInserted.ToArray();

                // THE SHEET METAL: what no import can bring, and where to get it.
                IModelDoc2 part = FindOpenDocument(path);
                if (part != null && SheetMetalData(part) != null)
                    measurements["sheet_metal_note"] =
                        "The part is sheet metal. The lengths of the flanges are not dimensions of the model (the profile of the base flange " +
                        "is not dimensioned), and the thickness and the bend radius belong to the «SheetMetal» feature, which has no dimension " +
                        "to import: no flag of this operation brings them. «drawing_dimension» dimensions them from the edges: the overall size " +
                        "between the extreme edges, the thickness and the inner bend radius.";
            }
            catch { }
        }
    }
}
