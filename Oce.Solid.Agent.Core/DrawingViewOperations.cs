using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// MOVES OR TAKES AWAY A VIEW OF A DRAWING (report 8cd843 N54, round 14, 27/10/2026).
    ///
    /// The bridge could put views on a sheet and not move or take one away: a detail of
    /// trial accepted in round 14 stayed on the drawing, and the only way to a clean drawing
    /// was to make it again from the start; the section A-A born on the side view (N53)
    /// could not be moved off it. delete_feature refused the view name, rightly: a view is
    /// not a feature of a part.
    ///
    /// ── WHAT SOLIDWORKS DOES, MEASURED ON A COPY OF THE DRAWING OF ROUND 14 ──
    ///
    /// (misure\sonda_viste_giro14.ps1, 27/10/2026)
    ///
    /// IView.Position (IView~Position.html, Remarks: «If it is aligned with another view,
    /// it will only be allowed to move along the alignment vector. If it has child views
    /// that are aligned with it, those views will also be moved along with this view»):
    ///   - the front view asked (+10, +10) mm moved by that, and the top view, the side
    ///     view and the section A-A, aligned to it, moved with it; the details did not;
    ///   - the top view, aligned vertically, asked (+20, +20) mm moved (0, +20);
    ///   - a detail, aligned to nothing, asked (-20, +10) mm moved by that.
    /// So the move is read back from the outline: a move SOLIDWORKS did only in part is put
    /// back and refused, with the alignment that stopped it.
    ///
    /// SelectByID2 «DRAWINGVIEW» + DeleteSelection2(swDelete_Absorbed), no window:
    ///   - a detail went with its circle in the view it enlarges (circles 2 -> 1);
    ///   - the front view went WITH its section and its two details, and the top and side
    ///     views, projected from it, stayed without a parent view;
    ///   - the isometric view of sheet 2 went with its 7 balloons, and the bill of
    ///     materials stayed: it is on the sheet, not in the view.
    /// So a view with sections or details made from it is refused unless «with_derived» is
    /// true, and the views that went are read back, not assumed.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome MoveView(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed("The active document is not a drawing: «move_view» moves a view of a drawing.");
            string reason;
            var view = ViewAsked(drawing, Str(a, "view", null), "move", out reason);
            if (view == null) return Failed(reason);
            string viewName = view.GetName2();

            double dx = Num(a, "dx_mm", double.NaN), dy = Num(a, "dy_mm", double.NaN);
            double x = Num(a, "x_mm", double.NaN), y = Num(a, "y_mm", double.NaN);
            string arguments = MoveArgumentsProblem(dx, dy, x, y);
            if (arguments != null) return Failed(arguments);

            double[] p = null, outline = OutlineOf(view);
            try { p = view.Position as double[]; } catch { }
            if (p == null || p.Length < 2 || outline == null)
                return Failed("I cannot read where the view «" + viewName + "» is on the sheet (IView.Position, GetOutline): I moved nothing.");

            // THE CENTRE IS THE ONE THE SHEET SHOWS, the middle of the outline: the Position of a
            // detail is not where the detail is seen (report 8cd843 round 14, observation 2: detail B
            // at Position 201, 127 mm and on the PDF at x 321..395 mm).
            double cx = (outline[0] + outline[2]) / 2.0, cy = (outline[1] + outline[3]) / 2.0;
            var want = MoveAsked(dx, dy, x, y, cx, cy);
            var before = OutlinesOfTheSheet(drawing);
            int alignment = -1;
            try { alignment = view.GetAlignment(); } catch { }

            try { view.Position = new[] { p[0] + want[0], p[1] + want[1] }; }
            catch (Exception ex) { return Failed("SOLIDWORKS did not take the new position of «" + viewName + "»: " + ex.Message); }

            var after = OutlinesOfTheSheet(drawing);
            double[] outlineAfter = OutlineOf(view);
            double gotX = outlineAfter == null ? double.NaN : outlineAfter[0] - outline[0];
            double gotY = outlineAfter == null ? double.NaN : outlineAfter[1] - outline[1];
            string partly = MoveNotDone(want[0], want[1], gotX, gotY, 0.0001);
            if (partly != null)
            {
                string back = PutBack(view, p, outline);
                return Failed("The view «" + viewName + "» " + partly + AlignmentWords(view, alignment) + back);
            }

            // EVERY VIEW THAT MOVED, the one asked and those aligned to it, is judged against what
            // did not move: they keep their places among themselves.
            List<string> moved = MovedViews(before, after);
            if (!moved.Contains(viewName)) moved.Add(viewName);
            foreach (string m in moved)
            {
                var mv = ViewByName(drawing, m);
                var r = mv == null ? null : ViewWithItsNotes(mv);
                if (r == null) continue;
                var sheet = ReadSheet(drawing, mv, null, moved);
                string touches = sheet.Touches(r[0], r[1], r[2], r[3]);
                if (touches == null) continue;
                string back = PutBack(view, p, outline);
                return Failed("Moved so, the view «" + m + "»" + (m == viewName ? "" : " (it moves with «" + viewName + "», aligned to it)") +
                              " " + touches + " (with its labels " + MmRect(r) + "). Move it somewhere else, or make room first" + back);
            }

            var measurements = new Dictionary<string, object>
            {
                ["view"] = viewName,
                ["asked_mm"] = new object[] { Math.Round(want[0] * 1000, 2), Math.Round(want[1] * 1000, 2) },
                ["moved_mm"] = new object[] { Math.Round(gotX * 1000, 2), Math.Round(gotY * 1000, 2) },
                ["centre_before_mm"] = new object[] { Math.Round(cx * 1000, 2), Math.Round(cy * 1000, 2) },
                ["centre_after_mm"] = new object[] { Math.Round((outlineAfter[0] + outlineAfter[2]) / 2.0 * 1000, 2), Math.Round((outlineAfter[1] + outlineAfter[3]) / 2.0 * 1000, 2) },
                ["outline_after_mm"] = new object[] { Math.Round(outlineAfter[0] * 1000, 2), Math.Round(outlineAfter[1] * 1000, 2), Math.Round(outlineAfter[2] * 1000, 2), Math.Round(outlineAfter[3] * 1000, 2) },
                ["views_moved_with_it"] = moved.Where(n => n != viewName).ToArray(),
                ["how_many_moved_with_it"] = moved.Count(n => n != viewName),
                ["alignment"] = AlignmentName(alignment),
            };
            _log("[agent] view «" + viewName + "» moved by " + Math.Round(gotX * 1000, 1) + ", " + Math.Round(gotY * 1000, 1) + " mm, with " + (moved.Count - 1) + " more");
            return WithoutFeature(measurements);
        }

        private Outcome DeleteView(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed("The active document is not a drawing: «delete_view» takes away a view of a drawing.");
            string reason;
            var view = ViewAsked(drawing, Str(a, "view", null), "take away", out reason);
            if (view == null) return Failed(reason);
            string viewName = view.GetName2();
            bool withDerived = Bool(a, "with_derived", false);

            // What depends on it: sections and details go with it, projected views stay without a
            // parent (measured, see the class).
            var derived = new List<string>();
            var orphans = new List<string>();
            var namesBefore = new List<string>();
            int annotations = -1;
            try { annotations = view.GetAnnotationCount(); } catch { }
            for (var v = (drawing.GetFirstView() as IView)?.GetNextView() as IView; v != null; v = v.GetNextView() as IView)
            {
                string n = null;
                try { n = v.GetName2(); } catch { }
                if (n == null) continue;
                namesBefore.Add(n);
                if (n == viewName) continue;
                if (!string.Equals(BaseViewName(v), viewName, StringComparison.OrdinalIgnoreCase)) continue;
                int type = -1;
                try { type = v.Type; } catch { }
                if (type == (int)swDrawingViewTypes_e.swDrawingSectionView || type == (int)swDrawingViewTypes_e.swDrawingDetailView) derived.Add(n);
                else orphans.Add(n);
            }
            string held = DerivedViewsProblem(viewName, derived, withDerived);
            if (held != null) return Failed(held);
            int tablesBefore = SheetTables(drawing);

            bool removed = RemoveNewView(model, viewName);
            var namesAfter = ViewNames(drawing);
            if (!removed || namesAfter.Contains(viewName))
                return Failed("The view «" + viewName + "» is still on the sheet after DeleteSelection2: nothing was taken away. " +
                              "Take it away by hand — " + Sentences.HowToWithdrawIt + ".");
            var gone = namesBefore.Where(n => !namesAfter.Contains(n)).ToList();
            var expected = new List<string> { viewName };
            expected.AddRange(derived);
            var unexpected = gone.Where(n => !expected.Contains(n)).ToList();
            var stayed = expected.Where(n => !gone.Contains(n)).ToList();
            int tablesAfter = SheetTables(drawing);

            var measurements = new Dictionary<string, object>
            {
                ["view"] = viewName,
                ["removed"] = gone.ToArray(),
                ["how_many_removed"] = gone.Count,
                ["views_before"] = namesBefore.Count,
                ["views_after"] = namesAfter.Count,
                ["annotations_of_the_view"] = annotations >= 0 ? (object)annotations : null,
                ["without_parent_now"] = orphans.ToArray(),
                ["tables_on_the_sheet_before"] = tablesBefore >= 0 ? (object)tablesBefore : null,
                ["tables_on_the_sheet_after"] = tablesAfter >= 0 ? (object)tablesAfter : null,
            };
            if (orphans.Count > 0)
                measurements["note"] = "The views " + string.Join(", ", orphans) + " were projected from «" + viewName +
                                       "»: they stay where they are, now without a parent view (no longer aligned to it).";
            if (unexpected.Count > 0 || stayed.Count > 0)
                measurements["warning"] = (unexpected.Count > 0 ? "SOLIDWORKS also took away " + string.Join(", ", unexpected) + ". " : "") +
                                          (stayed.Count > 0 ? "These views made from it are still there: " + string.Join(", ", stayed) + ". " : "") +
                                          "Look at the sheet.";
            _log("[agent] view «" + viewName + "» taken away with " + (gone.Count - 1) + " more");
            return WithoutFeature(measurements);
        }

        /// <summary>The view named by «view» on the current sheet, null with the reason.</summary>
        private IView ViewAsked(IDrawingDoc drawing, string wanted, string verb, out string reason)
        {
            reason = null;
            var names = ViewNames(drawing);
            if (names.Count == 0) { reason = "The current sheet has no views: there is nothing to " + verb + "."; return null; }
            if (string.IsNullOrWhiteSpace(wanted))
            {
                reason = "Name the view to " + verb + " in «view»: its name, or V1, V2 … as the state of the drawing lists them. " +
                         "On the current sheet there are: " + string.Join(", ", names) + ".";
                return null;
            }
            var view = ResolveView(drawing, names, wanted, out reason);
            if (reason != null) return null;
            if (view == null) reason = "I cannot take the view «" + wanted + "».";
            return view;
        }

        /// <summary>
        /// Why the arguments of move_view do not say a move, null when they do: either a shift
        /// (dx_mm, dy_mm) or a place for the centre (x_mm, y_mm), not both and not neither. NaN is
        /// an argument not given. Pure, so the contract suite reads it.
        /// </summary>
        internal static string MoveArgumentsProblem(double dx, double dy, double x, double y)
        {
            bool shift = !double.IsNaN(dx) || !double.IsNaN(dy), place = !double.IsNaN(x) || !double.IsNaN(y);
            if (shift && place)
                return "Give either «dx_mm» and «dy_mm» (move the view by that much) or «x_mm» and «y_mm» (put its centre there), not both.";
            if (!shift && !place)
                return "Say where the view goes: «dx_mm» and «dy_mm» move it by that much, «x_mm» and «y_mm» put its centre there, " +
                       "in millimetres of sheet from its bottom-left corner.";
            return null;
        }

        /// <summary>
        /// The shift asked, in metres {dx, dy}: the shift itself, or from the centre to the place
        /// asked; an axis not given does not move. Pure, so the contract suite reads it.
        /// </summary>
        internal static double[] MoveAsked(double dx, double dy, double x, double y, double centreX, double centreY)
        {
            if (!double.IsNaN(dx) || !double.IsNaN(dy))
                return new[] { double.IsNaN(dx) ? 0 : dx / 1000.0, double.IsNaN(dy) ? 0 : dy / 1000.0 };
            return new[] { double.IsNaN(x) ? 0 : x / 1000.0 - centreX, double.IsNaN(y) ? 0 : y / 1000.0 - centreY };
        }

        /// <summary>
        /// Why the move read back is not the move asked (metres, within <paramref name="tolerance"/>),
        /// null when it is. NaN read back is a move that could not be read. Pure, so the contract
        /// suite reads it.
        /// </summary>
        internal static string MoveNotDone(double askedX, double askedY, double gotX, double gotY, double tolerance)
        {
            if (double.IsNaN(gotX) || double.IsNaN(gotY)) return "moved, but I cannot read where it went";
            if (Math.Abs(askedX - gotX) <= tolerance && Math.Abs(askedY - gotY) <= tolerance) return null;
            return string.Format(CultureInfo.InvariantCulture,
                "moved by ({0:0.##}, {1:0.##}) mm instead of the ({2:0.##}, {3:0.##}) mm asked",
                gotX * 1000, gotY * 1000, askedX * 1000, askedY * 1000);
        }

        /// <summary>
        /// Why a view cannot be taken away as asked (N54), null when it can: sections and details
        /// made from it go with it (measured), so they are named and «with_derived» is asked for.
        /// Pure, so the contract suite reads it.
        /// </summary>
        internal static string DerivedViewsProblem(string viewName, List<string> derived, bool withDerived)
        {
            if (derived == null || derived.Count == 0 || withDerived) return null;
            return "The view «" + viewName + "» has views made from it that SOLIDWORKS takes away together with it " +
                   "(measured: taking away the front view took its section and its two details): " + string.Join(", ", derived) +
                   ". Take them away first with «delete_view», or pass «with_derived»: true to take them all. I touched nothing.";
        }

        /// <summary>Puts a view back where it was and says whether it is there again.</summary>
        private static string PutBack(IView view, double[] position, double[] outline)
        {
            try { view.Position = new[] { position[0], position[1] }; } catch { }
            var o = OutlineOf(view);
            return SameRectangle(o, outline, 0.0001)
                ? ". I put it back where it was."
                : ". I could not put it back where it was: look at the sheet (now " + MmRect(o) + ", before " + MmRect(outline) + ").";
        }

        /// <summary>Why a move stopped, from the alignment of the view (IView.GetAlignment).</summary>
        private static string AlignmentWords(IView view, int alignment)
        {
            if (alignment != (int)swViewAlignment_e.swViewAligned && alignment != (int)swViewAlignment_e.swViewAlignBoth) return "";
            string parent = BaseViewName(view);
            return ": it is aligned to " + (parent != null ? "«" + parent + "»" : "its parent view") +
                   ", and an aligned view moves only along the line that joins them (IView~Position.html). Give only the move " +
                   "along that line, or move «" + (parent ?? "its parent view") + "» instead";
        }

        /// <summary>
        /// The alignment of a view in words. IView~GetAlignment.html: «Alignment information as defined
        /// in swViewAlignment_e»; read on the views of round 14: the front view 1 (views aligned to it),
        /// the top view and the section 2 (aligned to their parent), a detail 0.
        /// </summary>
        private static string AlignmentName(int alignment)
        {
            switch (alignment)
            {
                case (int)swViewAlignment_e.swViewAlignNone: return "not aligned";
                case (int)swViewAlignment_e.swViewAlignedChildren: return "not aligned, with views aligned to it (they move with it)";
                case (int)swViewAlignment_e.swViewAligned: return "aligned to its parent view (it moves only along that line)";
                case (int)swViewAlignment_e.swViewAlignBoth: return "aligned to its parent view, with views aligned to it";
                default: return "not readable";
            }
        }

        /// <summary>The outline of every view of the current sheet, by name.</summary>
        private static Dictionary<string, double[]> OutlinesOfTheSheet(IDrawingDoc drawing)
        {
            var d = new Dictionary<string, double[]>();
            try
            {
                for (var v = (drawing.GetFirstView() as IView)?.GetNextView() as IView; v != null; v = v.GetNextView() as IView)
                {
                    string n = null;
                    try { n = v.GetName2(); } catch { }
                    var o = OutlineOf(v);
                    if (n != null && o != null) d[n] = o;
                }
            }
            catch { }
            return d;
        }

        /// <summary>The names of the views whose outline changed between two readings (more than 0.1 mm).</summary>
        private static List<string> MovedViews(Dictionary<string, double[]> before, Dictionary<string, double[]> after)
        {
            var l = new List<string>();
            foreach (KeyValuePair<string, double[]> kv in after)
            {
                double[] b;
                if (before.TryGetValue(kv.Key, out b) && !SameRectangle(b, kv.Value, 0.0001)) l.Add(kv.Key);
            }
            return l;
        }

        /// <summary>The tables of the sheet itself (a bill of materials is there, not in a view), -1 when unreadable.</summary>
        private static int SheetTables(IDrawingDoc drawing)
        {
            try
            {
                var sheetView = drawing.GetFirstView() as IView;
                var t = sheetView?.GetTableAnnotations() as object[];
                return t == null ? 0 : t.Length;
            }
            catch { return -1; }
        }
    }
}
