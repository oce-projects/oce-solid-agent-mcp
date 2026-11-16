using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// MOVES A DIMENSION OR A NOTE OF A DRAWING (report 8cd843, round 22, point 8,
    /// 28/10/2026). A dimension in the wrong place could only be taken away with
    /// «delete_annotation» and placed again, losing its name and its decimals.
    ///
    /// The annotation is found as «delete_annotation» finds it (FindAnnotations: by the
    /// name the plan gave, or a note by its exact text, on the current sheet only), moved
    /// with `IAnnotation.SetPosition2` (sheet metres: for the texts of a display dimension
    /// IAnnotation.GetPosition was measured within 8 mm of each text position on the
    /// sheet, probe Z-C21.3 of C3-1, see DimensionTextOperations.cs) and read back: «moved»
    /// is said only when GetPosition answers the place asked. «arrows» sets the side of the
    /// arrows of a dimension (IDisplayDimension.ArrowSide, swDimensionArrowsSide_e), read back
    /// too. Not measured on SOLIDWORKS as an operation of its own yet.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// Why «x_mm»/«y_mm» and «dx_mm»/«dy_mm» do not make a place, or null: either the
        /// place on the sheet with both numbers, or a shift with at least one. Pure, so the
        /// contract suite reads it.
        /// </summary>
        internal static string MoveProblem(bool x, bool y, bool dx, bool dy)
        {
            bool absolute = x || y, relative = dx || dy;
            if (absolute && relative)
                return "give the place on the sheet («x_mm» and «y_mm») or a shift («dx_mm», «dy_mm»), not both";
            if (!absolute && !relative)
                return "give where it goes: «x_mm» and «y_mm» on the sheet, or a shift «dx_mm», «dy_mm»";
            if (absolute && !(x && y))
                return "the place on the sheet wants both «x_mm» and «y_mm»";
            return null;
        }

        /// <summary>Whether a point (metres) lies on a sheet of the given size (metres); true when the size is unknown. Pure.</summary>
        internal static bool OnTheSheet(double x, double y, double[] sheetSize)
        {
            if (sheetSize == null || sheetSize.Length < 2 || sheetSize[0] <= 0 || sheetSize[1] <= 0) return true;
            return x >= 0 && y >= 0 && x <= sheetSize[0] && y <= sheetSize[1];
        }

        private Outcome MoveAnnotation(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed("The active document is not a drawing: «move_annotation» moves a dimension or a note of a drawing. Open the drawing and try again.");

            string name = Str(a, "name", null), text = Str(a, "text", null);
            if (string.IsNullOrWhiteSpace(name)) name = null; else name = name.Trim();
            if (string.IsNullOrWhiteSpace(text)) text = null;
            if ((name == null) == (text == null))
                return Failed("Write «name» or «text», one of the two: «name» is the name of a dimension as drawing_dimension " +
                              "gives it («RD1@Drawing View1@...», or «RD1» alone when it is the only one) or of a note as «note» " +
                              "gives it; «text» is the exact text of a note. I touched nothing.");
            string moveWhy = MoveProblem(a.ContainsKey("x_mm"), a.ContainsKey("y_mm"), a.ContainsKey("dx_mm"), a.ContainsKey("dy_mm"));
            if (moveWhy != null) return Failed("I do not know where to move it: " + moveWhy + ". I touched nothing.");
            string arrowsAsked = Str(a, "arrows", null);
            if (arrowsAsked != null && arrowsAsked != "inside" && arrowsAsked != "outside")
                return Failed("«arrows» is «inside» or «outside», not «" + arrowsAsked + "». I touched nothing.");

            string sheetName = "";
            try { sheetName = (drawing.GetCurrentSheet() as ISheet)?.GetName() ?? ""; } catch { }
            var found = FindAnnotations(drawing, name, text);
            if (found == null)
                return Failed("I cannot walk the annotations of the sheet «" + sheetName + "»: I cannot say which one answers. I touched nothing: try again once the drawing answers.");
            string asked = name != null ? "the name «" + name + "»" : "the text «" + text + "»";
            if (found.Count == 0)
                return Failed("No dimension or note of the sheet «" + sheetName + "» answers to " + asked + ". Use the name the plan " +
                              "that placed it gave («dimensions[].name» of drawing_dimension, «name» of note), or the exact text " +
                              "of the note; if it is on another sheet, activate it with «activate_sheet» first. I touched nothing.");
            if (found.Count > 1)
            {
                var which = new List<string>();
                foreach (AnnotationFound f in found) which.Add(f.Kind + " «" + (f.Name ?? f.Text) + "» (" + f.View + ")");
                return Failed(found.Count + " annotations answer to " + asked + ": " + string.Join(", ", which) +
                              ". Write the full name of the one to move. I touched nothing.");
            }
            AnnotationFound target = found[0];
            bool isDimension = target.Kind == "dimension";
            if (arrowsAsked != null && !isDimension)
                return Failed("«arrows» is for a dimension, and «" + (target.Name ?? target.Text) + "» is a note. I touched nothing.");
            IAnnotation annotation = target.Annotation;
            double[] before = null;
            try { before = annotation == null ? null : annotation.GetPosition() as double[]; } catch { }
            if (before == null || before.Length < 2)
                return Failed("I cannot read where the " + target.Kind + " «" + (target.Name ?? target.Text) + "» is: I would not " +
                              "know whether it moved. I touched nothing.");

            double x = a.ContainsKey("x_mm") ? Num(a, "x_mm", 0) / 1000.0 : before[0] + Num(a, "dx_mm", 0) / 1000.0;
            double y = a.ContainsKey("y_mm") ? Num(a, "y_mm", 0) / 1000.0 : before[1] + Num(a, "dy_mm", 0) / 1000.0;
            double[] sheetSize = SheetDimensions(drawing);
            if (!OnTheSheet(x, y, sheetSize))
                return Failed("The place asked, x " + Math.Round(x * 1000, 1).ToString(Ci) + " y " + Math.Round(y * 1000, 1).ToString(Ci) +
                              " mm, is outside the sheet of " + Math.Round(sheetSize[0] * 1000, 1).ToString(Ci) + " x " +
                              Math.Round(sheetSize[1] * 1000, 1).ToString(Ci) + " mm. Give a place inside it, or use «dx_mm» and «dy_mm» from where it is. I touched nothing.");

            try { annotation.SetPosition2(x, y, 0); }
            catch (Exception ex) { return Failed("Moving the " + target.Kind + " went into error: " + ex.Message + ". Look at the drawing."); }
            double[] after = null;
            try { after = annotation.GetPosition() as double[]; } catch { }
            if (after == null || after.Length < 2 || Math.Abs(after[0] - x) > 0.0001 || Math.Abs(after[1] - y) > 0.0001)
                return Failed("I asked the " + target.Kind + " «" + (target.Name ?? target.Text) + "» at x " +
                              Math.Round(x * 1000, 2).ToString(Ci) + " y " + Math.Round(y * 1000, 2).ToString(Ci) + " mm, and it reads " +
                              (after == null || after.Length < 2 ? "no position" :
                               "x " + Math.Round(after[0] * 1000, 2).ToString(Ci) + " y " + Math.Round(after[1] * 1000, 2).ToString(Ci) + " mm") +
                              ". Look at the drawing.");

            var measurements = new Dictionary<string, object>
            {
                ["kind"] = target.Kind,
                ["name"] = target.Name,
                ["view"] = target.View,
                ["sheet"] = sheetName,
                ["position_before_mm"] = new object[] { Math.Round(before[0] * 1000, 2), Math.Round(before[1] * 1000, 2) },
                ["position_after_mm"] = new object[] { Math.Round(after[0] * 1000, 2), Math.Round(after[1] * 1000, 2) },
                ["moved"] = true,
            };
            if (isDimension)
            {
                IDisplayDimension d = null;
                IView view = ViewByName(drawing, target.View);
                try
                {
                    for (IDisplayDimension dd = view == null ? null : view.GetFirstDisplayDimension5(); dd != null && d == null; dd = dd.GetNext5())
                    {
                        string full = null;
                        try { full = (dd.GetDimension2(0) as IDimension)?.FullName; } catch { }
                        if (full == target.Name) d = dd;
                    }
                }
                catch { }
                if (arrowsAsked != null && d != null)
                {
                    int wanted = arrowsAsked == "inside" ? (int)swDimensionArrowsSide_e.swDimArrowsInside : (int)swDimensionArrowsSide_e.swDimArrowsOutside;
                    try { d.ArrowSide = wanted; } catch { }
                }
                int read = -1;
                try { if (d != null) read = d.ArrowSide; } catch { }
                measurements["arrows"] = read == (int)swDimensionArrowsSide_e.swDimArrowsInside ? "inside"
                                       : read == (int)swDimensionArrowsSide_e.swDimArrowsOutside ? "outside"
                                       : read == (int)swDimensionArrowsSide_e.swDimArrowsSmart ? "smart" : null;
                if (arrowsAsked != null && measurements["arrows"] as string != arrowsAsked)
                    measurements["arrows_note"] = "I asked the arrows " + arrowsAsked + " and they read " +
                                                  (measurements["arrows"] ?? "nothing") + ": set them by hand.";
                if (view != null)
                {
                    var texts = DimensionTexts(drawing);
                    string checkedAgainst;
                    var overlapping = TextsThatOverlap(drawing, view, texts, out checkedAgainst);
                    measurements["overlapping_texts"] = overlapping == null ? null : overlapping.ToArray();
                    measurements["overlaps_checked_against"] = checkedAgainst;
                }
            }
            return WithoutFeature(measurements);
        }
    }
}
