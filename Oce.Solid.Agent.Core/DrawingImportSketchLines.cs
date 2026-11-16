using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE DASH-DOT LINES LEFT OUTSIDE THE VIEWS (0.131.0, item 5 of the drawing branch).
    ///
    /// ── WHERE THEY COME FROM, MEASURED ────────────────────────────────────────────────
    ///
    /// The sheet of 12/10/2026 had dash-dot lines running from the border of the sheet to the views, with nothing at the other
    /// end. The first guess (the failed AddChamferDim of drawing_dimension, which fails 34 times in the session) was WRONG.
    /// Measured on a fresh drawing of the same part with Checks/Draw131 «segs», which counts the sketch segments of the sheet and
    /// of every view between two calls of the bridge (12/10/2026, the library of 0.153.1):
    ///
    ///     after the views                         sheet 0   V1 0  V2 0  V3 0  V4 0
    ///     after drawing_dimension V1 (it fails)   0 0 0 0 0
    ///     after drawing_dimension V2              0 0 0 0 0
    ///     after model_dimensions (marked)         0 0 0 0 0
    ///     after model_dimensions all_dimensions   sheet 0   V1 2  V2 2  V3 2  V4 0
    ///
    /// The six segments are lines in the sketch of the view (style 4, for example (-27,-35)-(-93,-35) and (-60,-68)-(-60,-2)).
    /// They are born by the call that takes the dimensions the marked mask does not bring: «InsertModelAnnotations2 with
    /// AllTypes» (IDrawingDoc~InsertModelAnnotations2: «allTypes true to insert all types of annotations»), which brings every
    /// kind of annotation the model has, among them the centre lines of its sketches, drawn in the views' sketches far from the
    /// part. They are not dimensions and not orphans; they were hit with swSelSKETCHSEGS at the points where they are
    /// drawn, and they stay after every dimension of the drawing is taken away (probe «pick»).
    ///
    /// ── WHAT IT DOES ──────────────────────────────────────────────────────────────────
    ///
    /// model_dimensions notes the sketch segments of the sheet and of every view before the import and, after it, takes away
    /// the ones that were not there, counts again, and says which in «sketch_lines_removed». Nothing that was on the
    /// drawing before the call is touched. GUIDE PAGES: IView~GetSketch, ISketch~GetSketchSegments, ISketchSegment~GetType and
    /// Style, ISketchLine~GetStartPoint2 and GetEndPoint2, IModelDocExtension~DeleteSelection2 (swDelete_Absorbed).
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// The sketch segments of the sheet and of every view as keys («type|style|coordinates»), per view name. A view whose
        /// sketch cannot be read has no entry, and nothing is taken away from it.
        /// </summary>
        private static Dictionary<string, HashSet<string>> SketchSegmentKeysOfTheViews(IDrawingDoc drawing)
        {
            var outside = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            try
            {
                var v = drawing.GetFirstView() as IView;
                while (v != null)
                {
                    IView view = v;
                    v = v.GetNextView() as IView;
                    string name = "";
                    try { name = view.GetName2() ?? ""; } catch { }
                    try
                    {
                        ISketch sketch = view.GetSketch() as ISketch;
                        if (sketch == null) continue;
                        var keys = new HashSet<string>(StringComparer.Ordinal);
                        object[] segments = sketch.GetSketchSegments() as object[];
                        if (segments != null)
                            foreach (object o in segments) keys.Add(SketchSegmentKey(o));
                        outside[name] = keys;
                    }
                    catch { }
                }
            }
            catch { }
            return outside;
        }

        private static string SketchSegmentKey(object segment)
        {
            try
            {
                ISketchSegment s = segment as ISketchSegment;
                var ci = CultureInfo.InvariantCulture;
                string key = (s == null ? "?" : s.GetType().ToString(ci)) + "|" + (s == null ? "?" : s.Style.ToString(ci));
                ISketchLine line = segment as ISketchLine;
                if (line != null)
                {
                    ISketchPoint p = line.GetStartPoint2() as ISketchPoint;
                    ISketchPoint q = line.GetEndPoint2() as ISketchPoint;
                    if (p != null && q != null)
                        key += "|" + Math.Round(p.X * 1e6).ToString(ci) + "," + Math.Round(p.Y * 1e6).ToString(ci) + "," +
                                     Math.Round(q.X * 1e6).ToString(ci) + "," + Math.Round(q.Y * 1e6).ToString(ci);
                }
                return key;
            }
            catch { return "?"; }
        }

        /// <summary>
        /// Takes one sketch segment of a view out of the drawing. ISketchSegment.Select4 on a segment of a VIEW's sketch answers
        /// nothing useful (measured on 12/10/2026: the segments stayed after Select4 + DeleteSelection2), so the segment is PICKED where
        /// it is drawn, as a person would click on it: IModelDocExtension.SelectByID2 with no name and no type at a point of the
        /// sheet, which selected the segment with swSelSKETCHSEGS in the pick of 12/10/2026 (Checks/Draw131 «pick»). The point is a
        /// sample along the segment (the sheet position of a point of a view sketch is the position of the view plus its sketch
        /// coordinates, times the scale of the view), and the selection is accepted only if the selected object IS the segment
        /// (ISldWorks.IsSame): a point where the part's own edge lies would select the edge, and that is never deleted.
        /// </summary>
        private bool TakeAwayTheSketchSegment(IModelDoc2 model, IView view, object segment)
        {
            ISketchLine line = segment as ISketchLine;
            if (line == null) return false;
            ISketchPoint p = line.GetStartPoint2() as ISketchPoint;
            ISketchPoint q = line.GetEndPoint2() as ISketchPoint;
            double[] position = null;
            try { position = view.Position as double[]; } catch { }
            if (p == null || q == null || position == null || position.Length < 2) return false;
            double scale = 1;
            try { scale = view.ScaleDecimal; } catch { }
            if (scale <= 0) scale = 1;
            ISelectionMgr selection = model.SelectionManager as ISelectionMgr;
            if (selection == null) return false;
            foreach (double factor in new[] { 1.0, scale })
            {
                for (int k = 1; k <= 19; k++)
                {
                    double t = k / 20.0;
                    double x = position[0] + factor * (p.X + (q.X - p.X) * t);
                    double y = position[1] + factor * (p.Y + (q.Y - p.Y) * t);
                    model.ClearSelection2(true);
                    bool hit = model.Extension.SelectByID2("", "", x, y, 0, false, 0, null, 0);
                    if (!hit || selection.GetSelectedObjectCount2(-1) != 1) continue;
                    if (selection.GetSelectedObjectType3(1, -1) != (int)swSelectType_e.swSelSKETCHSEGS) continue;
                    object picked = selection.GetSelectedObject6(1, -1);
                    if (picked == null || _sw.IsSame(picked, segment) != (int)swObjectEquality.swObjectSame) continue;
                    bool deleted = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                    model.ClearSelection2(true);
                    return deleted;
                }
                if (scale == 1.0) break;
            }
            model.ClearSelection2(true);
            return false;
        }

        /// <summary>
        /// Takes away the sketch segments that are in a view (or on the sheet) now and were not in <paramref name="before"/>,
        /// counts the segments again, and lists what it did.
        /// </summary>
        private void RemoveNewSketchSegments(
            IModelDoc2 model, IDrawingDoc drawing, Dictionary<string, HashSet<string>> before, System.Collections.IList removed, System.Collections.IList problems)
        {
            var v = drawing.GetFirstView() as IView;
            while (v != null)
            {
                IView view = v;
                v = v.GetNextView() as IView;
                string name = "";
                try { name = view.GetName2() ?? ""; } catch { }
                HashSet<string> known;
                if (!before.TryGetValue(name, out known)) continue;   // not there before, or its sketch could not be read
                ISketch sketch = view.GetSketch() as ISketch;
                object[] segments = sketch == null ? null : sketch.GetSketchSegments() as object[];
                if (segments == null) continue;
                var fresh = new List<object>();
                foreach (object o in segments)
                    if (!known.Contains(SketchSegmentKey(o))) fresh.Add(o);
                if (fresh.Count == 0) continue;
                int countBefore = segments.Length;
                foreach (object o in fresh)
                {
                    string key = SketchSegmentKey(o);
                    bool gone = false;
                    try { gone = TakeAwayTheSketchSegment(model, view, o); }
                    catch { }
                    if (gone)
                        removed.Add(new Dictionary<string, object>
                        {
                            ["view"] = name,
                            ["segment"] = key,
                            ["why"] = "a sketch line the import drew in the view (a centre line of the model's sketches, away from the part): " +
                                      "it is not a dimension, and left alone it hangs on the sheet outside the views",
                        });
                    else
                        problems.Add("the sketch line " + key + " of the view «" + name + "» could not be picked and taken away: it stays on the sheet");
                }
                try { model.EditRebuild3(); } catch { }
                ISketch again = view.GetSketch() as ISketch;
                object[] left = again == null ? null : again.GetSketchSegments() as object[];
                int countAfter = left == null ? 0 : left.Length;
                if (countAfter != countBefore - fresh.Count)
                    problems.Add("the view «" + name + "» had " + countBefore + " sketch segments and " + fresh.Count + " were to go: " +
                                 countAfter + " are left");
            }
        }
    }
}
