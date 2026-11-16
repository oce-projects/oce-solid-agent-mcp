using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE SCALE OF A VIEW (report 8cd843, round 21, point 1, 28/10/2026). «standard_views»
    /// put a part 175 mm long at 1:2 on an A3 with the sheet at 1:1, and no operation could
    /// change it: «model_view» and «standard_views» had no «scale», and a view already on the
    /// sheet could not be scaled. Now «scale» («1:2», «2:1») on both, and «set_view_scale» for
    /// a view that is already there.
    ///
    /// The recipe is the one of the guide. IView~ScaleRatio.html: «Gets or sets the scale of
    /// the drawing view», an «Array of 2 doubles», the numerator first. The example Set View
    /// Scale (VBA): changing the scale turns IView::UseSheetScale off by itself, then
    /// EditRebuild3. IView~UseParentScale.html: a view that follows its parent changes with it
    /// after the rebuild. The scale is read back from each view after the rebuild, never
    /// taken from the call.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// «1:2», «2:1», «1:2.5» into numerator and denominator; null with the reason
        /// otherwise. A scale beyond 100:1 or 1:1000 is not a drawing scale. Pure, so the
        /// contract suite reads it.
        /// </summary>
        internal static double[] ParseScale(string text, out string why)
        {
            why = null;
            if (string.IsNullOrWhiteSpace(text)) { why = "it is empty"; return null; }
            string[] parts = text.Trim().Split(':');
            if (parts.Length != 2) { why = "it is not written as two numbers with «:» between them"; return null; }
            double numerator, denominator;
            if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out numerator) ||
                !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out denominator))
            {
                why = "its two numbers cannot be read (a decimal is written with a point, «1:2.5»)";
                return null;
            }
            if (double.IsNaN(numerator) || double.IsNaN(denominator) || double.IsInfinity(numerator) ||
                double.IsInfinity(denominator) || numerator <= 0 || denominator <= 0)
            {
                why = "both numbers must be greater than zero";
                return null;
            }
            double value = numerator / denominator;
            if (value > 100 || value < 0.001) { why = "a scale beyond 100:1 or 1:1000 is not a drawing scale"; return null; }
            return new[] { numerator, denominator };
        }

        /// <summary>A ratio written as the drawing writes it, «1:2»; null when there is none. Pure.</summary>
        internal static string ScaleText(double[] ratio)
        {
            if (ratio == null || ratio.Length < 2) return null;
            return ratio[0].ToString("0.###", CultureInfo.InvariantCulture) + ":" +
                   ratio[1].ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>Whether a scale read from a view is the ratio asked. Pure.</summary>
        internal static bool SameScale(double read, double[] ratio)
        {
            if (ratio == null || ratio.Length < 2 || ratio[1] == 0 || read <= 0) return false;
            double wanted = ratio[0] / ratio[1];
            return Math.Abs(read - wanted) <= 1e-6 * Math.Max(1.0, wanted);
        }

        /// <summary>
        /// The scale of every view of the active sheet, as the drawing writes it, and whether
        /// each one follows its parent or the sheet: what «standard_views» and
        /// «set_view_scale» answer.
        /// </summary>
        private static List<Dictionary<string, object>> ViewScales(IDrawingDoc drawing)
        {
            var list = new List<Dictionary<string, object>>();
            try
            {
                IView sheet = drawing.GetFirstView() as IView;   // the sheet itself
                IView view = sheet == null ? null : sheet.GetNextView() as IView;
                int guard = 0;
                while (view != null && guard++ < 500)
                {
                    var entry = new Dictionary<string, object>();
                    string name = null;
                    try { name = view.GetName2(); } catch { }
                    entry["name"] = name;
                    double[] ratio = null;
                    try { ratio = view.ScaleRatio as double[]; } catch { }
                    entry["scale"] = ScaleText(ratio);
                    bool? followsParent = null;
                    try { followsParent = view.UseParentScale; } catch { }
                    entry["uses_parent_scale"] = followsParent;
                    bool? followsSheet = null;
                    try { followsSheet = view.UseSheetScale == 1; } catch { }
                    entry["uses_sheet_scale"] = followsSheet;
                    list.Add(entry);
                    view = view.GetNextView() as IView;
                }
            }
            catch { }
            return list;
        }

        /// <summary>The scales of the views in one line, for a refusal.</summary>
        private static string ViewScalesText(List<Dictionary<string, object>> scales)
        {
            var parts = new List<string>();
            foreach (var entry in scales)
                parts.Add((entry["name"] ?? "?") + " " + (entry["scale"] ?? "?"));
            return parts.Count == 0 ? "no view could be read" : string.Join(", ", parts);
        }

        /// <summary>
        /// Sets the scale of one view (IView~ScaleRatio.html), rebuilds, and reads it back.
        /// Null when the view reads the scale asked; the reason otherwise.
        /// </summary>
        private static string ApplyViewScale(IModelDoc2 model, IView view, double[] ratio)
        {
            try { view.ScaleRatio = new[] { ratio[0], ratio[1] }; }
            catch (Exception ex) { return "SOLIDWORKS refused the scale: " + ex.Message; }
            try { model.EditRebuild3(); } catch { }
            double read = -1;
            try { read = view.ScaleDecimal; } catch { }
            if (read <= 0) return "the scale cannot be read back from the view";
            if (!SameScale(read, ratio))
                return "the view reads " + read.ToString("0.####", CultureInfo.InvariantCulture) + " instead of " +
                       (ratio[0] / ratio[1]).ToString("0.####", CultureInfo.InvariantCulture);
            return null;
        }

        /// <summary>
        /// The scale asked for the views a call has just made: set on the ones that do not
        /// follow a parent (IView~UseParentScale.html); the projected ones follow after the
        /// rebuild. Every new view is read back. Null when every new view reads it; the
        /// reason otherwise. <paramref name="setOn"/> names the views the scale was set on.
        /// </summary>
        private static string ScaleNewViews(IModelDoc2 model, IDrawingDoc drawing, HashSet<string> before,
                                            double[] ratio, out List<string> setOn)
        {
            var setOnViews = new List<string>();
            setOn = setOnViews;
            var fresh = new List<IView>();
            try
            {
                IView sheet = drawing.GetFirstView() as IView;   // the sheet itself
                IView view = sheet == null ? null : sheet.GetNextView() as IView;
                int guard = 0;
                while (view != null && guard++ < 500)
                {
                    string name = null;
                    try { name = view.GetName2(); } catch { }
                    if (name != null && !before.Contains(name)) fresh.Add(view);
                    view = view.GetNextView() as IView;
                }
            }
            catch { }
            if (fresh.Count == 0) return "the views this call made cannot be found again";
            foreach (IView view in fresh)
            {
                bool followsParent = false;
                try { followsParent = view.UseParentScale; } catch { }
                if (followsParent) continue;
                try { view.ScaleRatio = new[] { ratio[0], ratio[1] }; }
                catch (Exception ex) { return "SOLIDWORKS refused the scale: " + ex.Message; }
                string name = null;
                try { name = view.GetName2(); } catch { }
                setOnViews.Add(name ?? "?");
            }
            try { model.EditRebuild3(); } catch { }
            var wrong = new List<string>();
            foreach (IView view in fresh)
            {
                double read = -1;
                try { read = view.ScaleDecimal; } catch { }
                if (SameScale(read, ratio)) continue;
                string name = null;
                try { name = view.GetName2(); } catch { }
                wrong.Add((name ?? "?") + " reads " + (read > 0 ? read.ToString("0.####", CultureInfo.InvariantCulture) : "nothing"));
            }
            return wrong.Count == 0 ? null : "these views do not read it: " + string.Join(", ", wrong);
        }

        /// <summary>
        /// Where a view goes beyond its sheet after a change of scale: its outline
        /// (IView.GetOutline, metres of sheet) against the size of the sheet. Null when it is
        /// inside, or when either cannot be read.
        /// </summary>
        private static string OutsideTheSheet(IDrawingDoc drawing, IView view)
        {
            double[] outline = null;
            try { outline = view.GetOutline() as double[]; } catch { }
            ISheet sheet = null;
            try { sheet = drawing.GetCurrentSheet() as ISheet; } catch { }
            double[] size = sheet == null ? null : SheetMeasurements(sheet);
            if (outline == null || outline.Length < 4 || size == null) return null;
            double x0 = Units.Mm(outline[0]), y0 = Units.Mm(outline[1]), x1 = Units.Mm(outline[2]), y1 = Units.Mm(outline[3]);
            if (x0 >= 0 && y0 >= 0 && x1 <= size[0] && y1 <= size[1]) return null;
            return "Its outline goes from x " + Math.Round(x0, 1).ToString(CultureInfo.InvariantCulture) + " to " +
                   Math.Round(x1, 1).ToString(CultureInfo.InvariantCulture) + " and from y " +
                   Math.Round(y0, 1).ToString(CultureInfo.InvariantCulture) + " to " +
                   Math.Round(y1, 1).ToString(CultureInfo.InvariantCulture) + " mm, beyond the sheet of " +
                   Math.Round(size[0], 1).ToString(CultureInfo.InvariantCulture) + " x " +
                   Math.Round(size[1], 1).ToString(CultureInfo.InvariantCulture) +
                   " mm: move it with «move_view» or give a smaller scale.";
        }

        /// <summary>
        /// The views whose scale is not the sheet's, in one note (report 8cd843, round 21):
        /// the title block prints the scale of the sheet, and on the drawings of 26P000007,
        /// 08 and 26M000002 it printed «1:1» with the views at 1:2. Null when there is none.
        /// Pure, so the contract suite reads it.
        /// </summary>
        internal static string DifferentScaleText(string sheetScale, IList<string> differing)
        {
            if (string.IsNullOrEmpty(sheetScale) || differing == null || differing.Count == 0) return null;
            return "The sheet is at " + sheetScale + " and " +
                   (differing.Count == 1 ? "this view is not: " : "these views are not: ") +
                   string.Join(", ", differing) + ". The title block prints the scale of the sheet: give the " +
                   "sheet the scale of the main view with «set_sheet» («scale_numerator», «scale_denominator»), " +
                   "or write the scale next to each view that has another one.";
        }

        /// <summary>
        /// The scale of the active sheet («1:1») and the note on the views listed in
        /// <paramref name="scales"/> that do not have it; only the views named in
        /// <paramref name="only"/> when it is given. The note is null when they all have it,
        /// and when the scale of the sheet cannot be read.
        /// </summary>
        private static string SheetScaleNote(IDrawingDoc drawing, List<Dictionary<string, object>> scales,
                                             ICollection<string> only, out string sheetScale)
        {
            sheetScale = null;
            ISheet sheet = null;
            try { sheet = drawing.GetCurrentSheet() as ISheet; } catch { }
            double[] sheetRatio = sheet == null ? null : ReadScale(sheet);
            if (sheetRatio == null) return null;
            sheetScale = ScaleText(sheetRatio);
            var differing = new List<string>();
            foreach (var entry in scales)
            {
                string name = entry["name"] as string;
                if (only != null && (name == null || !only.Contains(name))) continue;
                string scale = entry["scale"] as string;
                string unused;
                double[] ratio = ParseScale(scale, out unused);
                if (ratio == null) continue;
                if (!SameScale(ratio[0] / ratio[1], sheetRatio)) differing.Add("«" + name + "» " + scale);
            }
            return DifferentScaleText(sheetScale, differing);
        }

        // ─────────────────────────────────────────────────────────────────
        // set_view_scale
        // ─────────────────────────────────────────────────────────────────

        private Outcome ViewScale(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null) return Failed("The active document is not a drawing.");

            string asked = Str(a, "scale", null);
            string why;
            double[] ratio = ParseScale(asked, out why);
            if (ratio == null)
                return Failed("The scale «" + (asked ?? "") + "» cannot be used: " + why +
                              ". Give it as «1:2» or «2:1». I touched nothing.");

            string wanted = Str(a, "view", null);
            var names = ViewNames(drawing);
            string reason;
            IView view = string.IsNullOrWhiteSpace(wanted) ? null : ResolveView(drawing, names, wanted, out reason);
            if (view == null)
                return Failed("There is no view «" + (wanted ?? "") + "» on the active sheet. The views are: " +
                              (names.Count == 0 ? "none" : string.Join(", ", names)) +
                              ". Use one of them, by name or as the state writes it («V1»). I touched nothing.");

            string name = null;
            try { name = view.GetName2(); } catch { }
            double[] before = null;
            try { before = view.ScaleRatio as double[]; } catch { }
            bool? followedParent = null;
            try { followedParent = view.UseParentScale; } catch { }
            if (before != null && before.Length >= 2 && before[1] != 0 && SameScale(before[0] / before[1], ratio))
                return Failed("The view «" + name + "» is already at " + ScaleText(before) +
                              ": nothing to change. Give another «scale». I touched nothing.");

            string problem = ApplyViewScale(model, view, ratio);
            var scales = ViewScales(drawing);
            if (problem != null)
                return Failed("I asked the view «" + name + "» for " + ScaleText(ratio) + " and " + problem +
                              ". The views now read: " + ViewScalesText(scales) + ". «undo» withdraws the plan.");

            double[] after = null;
            try { after = view.ScaleRatio as double[]; } catch { }
            var measurements = new Dictionary<string, object>
            {
                ["view"] = name,
                ["scale_before"] = ScaleText(before),
                ["scale_after"] = ScaleText(after),
                ["followed_its_parent_before"] = followedParent,
                ["view_scales"] = scales,
            };
            if (followedParent == true)
                measurements["parent_note"] = "«" + name + "» followed the scale of its parent view: now it has its own.";
            string sheetScale;
            string sheetNote = SheetScaleNote(drawing, scales, new[] { name }, out sheetScale);
            measurements["sheet_scale"] = sheetScale;
            if (sheetNote != null) measurements["scale_note"] = sheetNote;
            string outside = OutsideTheSheet(drawing, view);
            if (outside != null) measurements["outside_the_sheet"] = outside;
            return WithoutFeature(measurements);
        }
    }
}
