using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// CENTRE MARKS ON THE HOLES IN THE DRAWING (entry W2.11).
    ///
    /// ── WHY IT IS AN OPERATION AND NOT A FINISHING TOUCH ────────────────
    ///
    /// A drawing with twelve holes and no centre mark goes out, is printed, and
    /// looks complete. In the workshop the thing needed to drill is missing: **where
    /// the centre is.** No error, unusable result.
    ///
    /// ── WHAT WAS MEASURED, FROM 17/9 TO 26/9/2026 ──────────────────────
    ///
    /// On 17/9 three probes: `AutoInsertCenterMarks2` (25 combinations),
    /// `InsertCenterMark2/3`: «answers True and puts nothing», counting with
    /// `GetCenterMarkCount`. On 26/9 (`probe0089 marks2..marks7`):
    ///
    ///   1. **THE MARKS WERE THERE.** SOLIDWORKS puts them on the holes by itself when it creates
    ///      the view (document option `swDetailingAutoInsertCenterMarksForHoles`,
    ///      turned on in the house template). They show in the PNG.
    ///   2. **`GetCenterMarkCount` DOES NOT COUNT THEM**: 0 on a view with three marks.
    ///      `GetFirstCenterMark`/`GetNext` sees them all (`CountCenterMarks`).
    ///   3. **FROM CODE NONE IS BORN.** `AddCenterMark` on the circle
    ///      selected in the view made `GetCenterMarkCount` go up 0->3 —
    ///      but on holes that already had the mark; with the three automatic
    ///      marks removed, `AddCenterMark` puts none back. On the arcs of the
    ///      fillets, nothing (4 out of 4).
    ///
    ///   4. **8/10/2026, THE BARE VIEW MADE ON PURPOSE** (the toggle above turned
    ///      off in the drawing before the views): `AddCenterMark` returns an object
    ///      and adds nothing, three holes out of three; `IView.AutoInsertCenterMarks2`
    ///      with the hole type puts one mark per hole (0 -> 3), and on a view that
    ///      already has them doubles them (3 -> 6). The verdict of 17/9 on it was
    ///      read with `GetCenterMarkCount`, which does not count those marks.
    ///
    /// ── SO IT IS A CHECK, AND IT MARKS A BARE VIEW ─────────────────────
    ///
    /// For every visible hole it is looked at whether it has the mark (a mark within half a
    /// millimetre of the centre). On a view where no hole has its mark they are
    /// inserted with `AutoInsertCenterMarks2` and each one is found again at its
    /// hole; on a view where some holes are marked and others are not nothing is
    /// inserted (it would double the existing ones) and the bare holes are REFUSED,
    /// saying the way that works.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome CenterMarks(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "The active document is not a drawing: the center marks go on " +
                    "a drawing view. First create the drawing with «create_drawing».");

            var names = ViewNames(drawing);
            if (names.Count == 0)
                return Failed(
                    "The drawing has no views: the center marks would have nowhere to attach. " +
                    "First put the views with «standard_views» or «model_view».");

            string wanted = ViewRequestedByName(drawing, Str(a, "view", null));
            if (!string.IsNullOrWhiteSpace(wanted) && !KnownViewName(names, wanted))
                return Failed(
                    "There is no view «" + wanted + "». There are: " + string.Join(", ", names) +
                    " (or V1..V" + names.Count + ").");

            // ONLY THE HOLES (whole circles). Measured on 26/9/2026 (`probe0089
            // croci6`): `AddCenterMark` on a fillet ARC selected in the
            // view makes nothing be born, 4 arcs out of 4. Slots have two arcs
            // and one single mark in the middle. Both are refused, with the measurement.
            string su = (Str(a, "applies_to", "holes") ?? "holes").Trim().ToLowerInvariant();
            bool allWhole = true;
            switch (su)
            {
                case "": case "holes": break;
                case "fillets":
                case "all":
                    return Failed("On fillets the center mark does not come out from code: measured, AddCenterMark on an " +
                                   "arc selected in the view puts nothing (4 arcs out of 4). Ask for «holes»; the centers of " +
                                   "the fillets, if you need them, must be put by hand in SOLIDWORKS.");
                case "slots":
                    return Failed("I do not do slots: a slot has two arcs and a single center mark in the middle, and a center " +
                                   "mark per arc would be wrong. Ask for «holes».");
                default:
                    return Failed("The «applies to» parameter can only be «holes», not «" + su + "».");
            }

            // WITHOUT `size_mm` the size of the document: it is a
            // studio convention, not a number of mine.
            bool itsDimension = a != null && a.ContainsKey("size_mm");
            double markSize = Units.M(Num(a, "size_mm", DefaultCentreMarkSizeMm));
            if (!itsDimension) markSize = DocumentCentreMarkSize(model, markSize);
            if (markSize <= 0) return Failed("«size_mm» must be greater than zero.");
            bool lines = Bool(a, "extended_lines", false);

            IMathUtility mu = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; } catch { }
            // The display settings of the document unless the call asked for its own.
            bool documentDefaults = !itsDimension && !lines;

            var perView = new List<object>();
            int totalBefore = 0, totalAfter = 0, already = 0;
            var notCreated = new List<string>();

            var sheet = drawing.GetFirstView() as IView;      // the first one is THE SHEET
            var view = sheet == null ? null : sheet.GetNextView() as IView;
            while (view != null)
            {
                string itemName = null;
                try { itemName = view.Name; } catch { }
                if (!string.IsNullOrWhiteSpace(wanted) && !SameName(itemName, wanted))
                {
                    view = view.GetNextView() as IView;
                    continue;
                }

                // The walk over the holes of the view is shared with the standard
                // views (Z-C21.13): MarkHolesOfView, below. Only whole circles:
                // the arcs and the slots were refused above.
                var marks = MarkHolesOfView(model, view, markSize, lines, documentDefaults, mu);
                int before = marks.Before;
                if (before < 0)
                    return Failed(
                        "I cannot count the center marks of the view «" + (itemName ?? "?") +
                        "» before starting: without that number I could not tell you which " +
                        "ones I put. I touched nothing.");
                foreach (string why in marks.LeftWithoutMark)
                    notCreated.Add((itemName ?? "?") + ": a circle (" + why + ")");

                int after = marks.After;
                totalBefore += before;
                totalAfter += after < 0 ? before : after;
                already += marks.AlreadyMarked;
                perView.Add(new Dictionary<string, object>
                {
                    ["view"] = itemName ?? "(no name)",
                    ["circles"] = marks.Holes,
                    ["circles_not_holes"] = marks.CirclesThatAreNotHoles,
                    ["marks_before"] = before,
                    ["marks_after"] = NumberOrNull(after),
                    ["marks_added"] = marks.Added,
                    ["circles_already_marked"] = marks.AlreadyMarked,
                    // Null when the view had no bare hole, or some hole already marked.
                    ["auto_insert_answered"] = marks.AutoInsertAnswered,
                });
                view = view.GetNextView() as IView;
            }

            try { model.EditRebuild3(); } catch { }

            int additions = Math.Max(0, totalAfter - totalBefore);
            var measurements = new Dictionary<string, object>
            {
                ["applies_to"] = su,
                ["views_checked"] = perView.Count,
                ["marks_before"] = totalBefore,
                ["marks_after"] = totalAfter,
                ["marks_added"] = additions,
                ["circles_already_marked"] = already,
                ["per_view"] = perView.ToArray(),
                ["size_mm"] = Math.Round(markSize * 1000, 3),
                ["size_source"] = itsDimension ? "requested" : "document",
            };
            if (notCreated.Count > 0)
            {
                measurements["circles_without_mark"] = notCreated.ToArray();
                return Failed(notCreated.Count + (notCreated.Count == 1 ? " hole is" : " holes are") +
                               " without a center mark (" + string.Join("; ", notCreated) + "). On a view where no hole has its mark " +
                               "I insert them all with AutoInsertCenterMarks2, the marks of the holes, but on a view where some holes already have " +
                               "theirs I do not, because that call doubles the existing ones (measured: 3 -> 6). Add the missing ones by hand, " +
                               "or redo the view with the document option «Center marks-holes» (Detailing, Auto insert on view creation) on. " +
                               "A drawing with some holes without a center looks finished and is not.");
            }

            // ── ZERO MARKS IS NOT A SILENT SUCCESS ──────────────────────
            if (additions == 0)
            {
                measurements["verdict"] =
                    "NO CENTER MARK ADDED. " +
                    (already > 0
                        ? already + (already == 1 ? " circle" : " circles") + " already had the mark: nothing new to put."
                        : "On " + perView.Count + (perView.Count == 1 ? " view" : " views") +
                          " there is no " + (allWhole ? "hole" : "arc") + " visible: it is the wrong view " +
                          "(holes are seen as circles from above, usually V2), or the part has none.") +
                    " The number is zero and I am telling you so.";
                _log("[agent] " + measurements["verdict"]);
                return SucceededWithoutCreating(measurements);
            }

            measurements["verdict"] =
                "Added " + additions + (additions == 1 ? " center mark" : " center marks") +
                " of " + perView.Count + (perView.Count == 1 ? " view" : " views") +
                " (" + totalBefore + " -> " + totalAfter + "), one per circle, counted on the drawing.";
            _log("[agent] " + measurements["verdict"]);
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// The centre mark size when nobody asks for one, in mm: the value of
        /// the operation's own default, used only when the document does not
        /// say its own (<see cref="DocumentCentreMarkSize"/>).
        /// </summary>
        private const double DefaultCentreMarkSizeMm = 2.5;

        /// <summary>
        /// The centre mark size of the document (swDetailingCenterMarkSize), in
        /// metres, or <paramref name="fallback"/> when it cannot be read or is
        /// not positive. It is a house convention of the drawing template, not
        /// a number of the bridge.
        /// </summary>
        private static double DocumentCentreMarkSize(IModelDoc2 model, double fallback)
        {
            try
            {
                double size = model.Extension.GetUserPreferenceDouble(
                    (int)swUserPreferenceDoubleValue_e.swDetailingCenterMarkSize,
                    (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified);
                if (size > 0) return size;
            }
            catch { }
            return fallback;
        }

        /// <summary>What <see cref="MarkHolesOfView"/> found and did on one view.</summary>
        private sealed class HoleCentreMarks
        {
            /// <summary>Marks before anything was touched, enumerated; -1 when they cannot be counted, and then nothing was touched.</summary>
            public int Before = -1;
            /// <summary>Marks at the end, enumerated; -1 when they cannot be counted.</summary>
            public int After = -1;
            // ZERO-E-ZERO: the four counters below count what the walk over the
            // visible edges found; they are read only when Before is not -1, that
            // is when the walk ran, and then a zero is a real zero.
            /// <summary>Whole circles that are holes seen end-on in this view.</summary>
            public int Holes;
            /// <summary>Whole circles that are not holes: the rim of a disc, the end of a shaft.</summary>
            public int CirclesThatAreNotHoles;
            /// <summary>Holes that had no mark and have one after the call, each found again at its centre.</summary>
            public int Added;
            /// <summary>Holes that already had a mark within half a millimetre of their centre.</summary>
            public int AlreadyMarked;
            /// <summary>One entry per hole left without a mark, saying why (empty when the reason is the general one).</summary>
            public List<string> LeftWithoutMark = new List<string>();
            /// <summary>What AutoInsertCenterMarks2 answered; null when it was not called.</summary>
            public bool? AutoInsertAnswered;
        }

        /// <summary>
        /// THE CENTRE MARKS ON THE HOLES OF ONE VIEW, shared by center_marks
        /// and standard_views (Z-C21.13; before, this walk lived inside
        /// CenterMarks).
        ///
        /// Every WHOLE circle of the view that is a hole seen end-on
        /// (<see cref="IsAVisibleHole"/>) is looked for among the marks the view
        /// already has (half a millimetre around its centre). When at least one
        /// hole is bare and NO hole of the view has a mark, the marks come from
        /// `IView.AutoInsertCenterMarks2` with the hole type; then every bare hole
        /// is looked for again at its centre, and only the ones found count as added.
        ///
        /// MEASURED ON 8/10/2026 (probe of the final bench fix, plate 120 x 80 x 10
        /// with three Ø8 holes, the house drawing template with the document
        /// toggle swDetailingAutoInsertCenterMarksForHoles turned off to make the
        /// views bare on purpose):
        ///
        ///   - `IDrawingDoc.AddCenterMark` on the selected circle, with the view
        ///     active or not: it returns an object and the enumerated count stays
        ///     0, three holes out of three. The fallback of 0.144.0 to 0.152.0 was
        ///     this call, so a bare view stayed bare, and the full bench of
        ///     0.152.0 refused «3 holes are without a center mark» once;
        ///   - `IView.AutoInsertCenterMarks2(Hole, 0, ...)`, with the view active or
        ///     not: 0 -> 3, one mark per hole, at -40/0, 0/0 and 40/0;
        ///   - on a view that already has the three marks it DOUBLES them: 3 -> 6,
        ///     and a second call 6 -> 9. So it is called only on a view where no
        ///     hole has its mark; a view with some holes marked and others bare is
        ///     left as it is and the bare holes are reported;
        ///   - on the front view (no hole seen end-on) it answers True and adds 0.
        ///
        /// The page IView~AutoInsertCenterMarks2.html: «Automatically inserts the
        /// specified center marks in this view»; InsertType from
        /// swAutoInsertCenterMarkTypes_e, InsertOption from swCenterMarkConnectionLine_e;
        /// Size, Gap, ExtendedLines and CenterLineFont «valid only if
        /// UseDocumentDefaults is false».
        ///
        /// When the marks cannot be counted first, nothing is touched and
        /// Before is -1.
        /// </summary>
        private static HoleCentreMarks MarkHolesOfView(
            IModelDoc2 model, IView view, double size, bool extendedLines, bool documentDefaults, IMathUtility mu)
        {
            var marks = new HoleCentreMarks();
            marks.Before = HowManyCenterMarks(view);
            if (marks.Before < 0) return marks;

            MathTransform toSheet = null;
            try { toSheet = view.ModelToViewTransform; } catch { }
            var knownCentres = CenterMarkCenters(view);
            // The bare holes: their centre on the sheet, or null when it could not be computed.
            var bare = new List<double[]>();
            foreach (var o in (view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[]) ?? new object[0])
            {
                var e = o as IEdge;
                ICurve c = null;
                try { c = e?.GetCurve() as ICurve; } catch { }
                if (c == null || !c.IsCircle()) continue;
                bool whole = false;
                try { whole = e.GetStartVertex() == null; } catch { }
                // Only whole circles: on the arc of a fillet no mark is born from
                // code (measured on 26/9/2026, 4 arcs of 4).
                if (!whole) continue;
                // A REAL HOLE (review of 26/9/2026): the circle faces the reader
                // (its axis, taken onto the sheet, is normal to the sheet) and is an
                // INNER loop of a planar face. The rim of a disc or the end of a
                // shaft is a whole circle but not a hole: they used to become
                // "holes without a mark" and a right part was refused.
                if (!IsAVisibleHole(e, c, mu, toSheet)) { marks.CirclesThatAreNotHoles++; continue; }
                marks.Holes++;

                // The centre on the sheet, so that one hole never gets two marks.
                double[] centre = null;
                try
                {
                    var cp = c.CircleParams as double[];
                    if (cp != null && mu != null && toSheet != null)
                        centre = ((IMathPoint)((IMathPoint)mu.CreatePoint(new[] { cp[0], cp[1], cp[2] })).MultiplyTransform(toSheet)).ArrayData as double[];
                }
                catch { }
                if (centre != null && knownCentres.Exists(q => SameCenter(view, q, centre)))
                { marks.AlreadyMarked++; continue; }
                bare.Add(centre);
            }

            if (bare.Count == 0) { marks.After = marks.Before; return marks; }
            var left = new List<string>();
            marks.LeftWithoutMark = left;
            if (marks.AlreadyMarked > 0)
            {
                // AutoInsertCenterMarks2 marks EVERY hole of the view, and doubles
                // the ones that have a mark (measured 3 -> 6): not called here.
                foreach (var _ in bare)
                    left.Add("other holes of this view already have their mark, and inserting would double them");
                marks.After = marks.Before;
                return marks;
            }

            double gap = size;
            try
            {
                double g = model.Extension.GetUserPreferenceDouble(
                    (int)swUserPreferenceDoubleValue_e.swDetailingCenterMarkGap,
                    (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified);
                if (g > 0) gap = g;
            }
            catch { }
            // The page, Remarks: «Before calling this method: Call IDrawingDoc::ActivateView
            // with the name of the view to activate.» The probe of 8/10/2026 saw the marks
            // born on a view not activated too, but the order of the page is kept.
            try { (model as IDrawingDoc)?.ActivateView(view.GetName2()); } catch { }
            bool answered = false;
            try
            {
                answered = view.AutoInsertCenterMarks2(
                    (int)swAutoInsertCenterMarkTypes_e.swAutoInsertCenterMarkType_Hole,
                    (int)swCenterMarkConnectionLine_e.swCenterMark_ShowNoConnectLines,
                    true, true, documentDefaults, size, gap, extendedLines, false, 0);
            }
            catch { }
            marks.AutoInsertAnswered = answered;
            marks.After = HowManyCenterMarks(view);

            // Every bare hole is looked for again at its centre: a mark counts as
            // added only where it is. A hole whose centre could not be computed
            // counts only when the marks grew by at least the number of bare holes.
            var nowCentres = CenterMarkCenters(view);
            int grown = marks.After < 0 ? 0 : marks.After - marks.Before;
            foreach (var centre in bare)
            {
                bool found = centre != null
                    ? nowCentres.Exists(q => SameCenter(view, q, centre))
                    : grown >= bare.Count;
                if (found) marks.Added++;
                else left.Add(answered
                    ? "AutoInsertCenterMarks2 answered true and put no mark on it"
                    : "AutoInsertCenterMarks2 answered false");
            }
            return marks;
        }

        /// <summary>
        /// The circle is a hole that in this view is seen as a circle: axis
        /// perpendicular to the sheet, and inner ring of a planar face.
        /// </summary>
        private static bool IsAVisibleHole(IEdge e, ICurve c, IMathUtility mu, MathTransform tr)
        {
            try
            {
                var cp = c.CircleParams as double[];
                if (cp == null || cp.Length < 6 || mu == null || tr == null) return false;
                var axis = ((IMathVector)((IMathVector)mu.CreateVector(new[] { cp[3], cp[4], cp[5] })).MultiplyTransform(tr)).ArrayData as double[];
                double l = Math.Sqrt(axis[0] * axis[0] + axis[1] * axis[1] + axis[2] * axis[2]);
                if (l < 1e-12 || Math.Abs(axis[2]) / l < 0.99) return false;
                // THE CIRCLE IS AN INNER RING OF A PLANAR FACE: the border of a
                // hole lies inside the face it goes through; the border of a disc or the
                // head of a shaft is the OUTER ring of its face.
                // Measured on 26/9/2026 (\`probe0089 marks8\`): FaceInSurfaceSense
                // does NOT tell them apart — real hole, false disc — because the direction of the
                // surface depends on how it was born; the ring does not.
                foreach (var oc in (e.GetCoEdges() as object[]) ?? new object[0])
                {
                    var ring = (oc as ICoEdge)?.GetLoop() as ILoop2;
                    var f = ring?.GetFace() as IFace2;
                    var s = f?.GetSurface() as ISurface;
                    if (s == null || !s.IsPlane()) continue;
                    if (!ring.IsOuter()) return true;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// The centres of the marks that the view already has, as
        /// `ICenterMark.GetPosition(0)` gives them: RELATIVE TO THE CENTRE OF THE VIEW, not on the
        /// sheet. Measured on 26/9/2026 (`probe0089 marks4`): a circle at
        /// 54.5/110.69 on the sheet, in a view centred at 94.5/110.69, has the
        /// mark at -40/0. The first code compared them with the sheet, and the second
        /// call put three double marks.
        /// </summary>
        private static List<double[]> CenterMarkCenters(IView view)
        {
            var outside = new List<double[]>();
            try
            {
                var cm = view.GetFirstCenterMark() as ICenterMark;
                while (cm != null)
                {
                    try
                    {
                        var p = cm.GetPosition(0) as double[];
                        if (p != null && p.Length >= 2) outside.Add(new[] { p[0], p[1] });
                    }
                    catch { }
                    cm = cm.GetNext() as ICenterMark;
                }
            }
            catch { }
            return outside;
        }

        /// <summary>A point of the sheet brought into the reference of the view (like GetPosition).</summary>
        private static double[] RelativeToView(IView view, double[] sheet)
        {
            double[] p = null;
            try { p = view.Position as double[]; } catch { }
            if (p == null || p.Length < 2) return new[] { sheet[0], sheet[1] };
            return new[] { sheet[0] - p[0], sheet[1] - p[1] };
        }

        /// <summary>
        /// Is the mark `q` (view reference) on the centre `sheet`? It is tried
        /// with and without the scale: at scale 1 it could not be told whether GetPosition
        /// applies it, and half a millimetre of tolerance holds in both readings.
        /// </summary>
        private static bool SameCenter(IView view, double[] q, double[] sheet)
        {
            var r = RelativeToView(view, sheet);
            double scale = 1;
            try { scale = view.ScaleDecimal; } catch { }
            if (scale <= 0) scale = 1;
            if (Math.Abs(q[0] - r[0]) < 0.0005 && Math.Abs(q[1] - r[1]) < 0.0005) return true;
            return Math.Abs(q[0] - r[0] / scale) < 0.0005 && Math.Abs(q[1] - r[1] / scale) < 0.0005;
        }

        /// <summary>
        /// How many centre marks this view has, or **-1 if it could not be
        /// read** (entry Z-C8).
        ///
        /// It returned zero, and the comment said it was «the cautious direction». It
        /// was not: if the reading BEFORE failed, any mark afterwards
        /// looked new and the message said «placed N crosses» even on a
        /// view that already had them; if the one AFTER failed, the loop tried
        /// all three styles (double marks) and then said «that view has
        /// no holes». A number that was not read cannot be compared.
        /// </summary>
        private static int HowManyCenterMarks(IView view)
        {
            return CountCenterMarks.OfView(view);
        }

        private static bool SameName(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(),
                                 StringComparison.OrdinalIgnoreCase);
        }

        private static bool KnownViewName(List<string> names, string wanted)
        {
            foreach (var n in names) if (SameName(n, wanted)) return true;
            return false;
        }
    }

    /// <summary>
    /// THE CENTRE MARKS ARE COUNTED BY ENUMERATING THEM (26/9/2026, `probe0089 marks5`).
    ///
    /// `IView.GetCenterMarkCount()` answered **0** on a view with three marks:
    /// the ones that SOLIDWORKS puts by itself on the holes when it creates the view (document
    /// option `swDetailingAutoInsertCenterMarksForHoles`, turned on here) it does not
    /// count; it counts only those added afterwards. `GetFirstCenterMark` /
    /// `GetNext` sees them all. The «block» of W2.11 of 17/9 — «the API says yes
    /// and puts nothing» — was largely this counter: the marks
    /// were there, and nobody saw them. The operation and the state use it.
    /// </summary>
    internal static class CountCenterMarks
    {
        /// <summary>How many centre marks the view has, enumerated; -1 if it cannot be read.</summary>
        internal static int OfView(IView view)
        {
            try
            {
                int n = 0;
                var cm = view.GetFirstCenterMark() as ICenterMark;
                while (cm != null && n < 10000)
                {
                    n++;
                    cm = cm.GetNext() as ICenterMark;
                }
                return n;
            }
            catch { return -1; }
        }
    }
}
