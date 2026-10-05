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
    /// `GetCenterMarkCount`. On 26/9 (`prova0089 croci2..croci7`):
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
    /// ── SO IT IS A CHECK ────────────────────────────────────────────────
    ///
    /// For every visible hole it is looked at whether it has the mark (a mark within half a
    /// millimetre of the centre). If any is missing `AddCenterMark` is tried
    /// (here it is not born, elsewhere who knows), and if it stays without one it is REFUSED saying the
    /// way that works: the document option turned on and the view redone.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome CenterMarks(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "Il documento attivo non e' una tavola: le croci di centro stanno " +
                    "su una vista di disegno. Crea prima la tavola con «create_drawing».");

            var names = ViewNames(drawing);
            if (names.Count == 0)
                return Failed(
                    "La tavola non ha viste: le croci non avrebbero dove attaccarsi. " +
                    "Metti prima le viste con «standard_views» o «model_view».");

            string wanted = ViewRequestedByName(drawing, Str(a, "view", null));
            if (!string.IsNullOrWhiteSpace(wanted) && !KnownViewName(names, wanted))
                return Failed(
                    "Non c'e' nessuna vista «" + wanted + "». Ci sono: " + string.Join(", ", names) +
                    " (o V1..V" + names.Count + ").");

            // ONLY THE HOLES (whole circles). Measured on 26/9/2026 (`prova0089
            // croci6`): `AddCenterMark` on a fillet ARC selected in the
            // view makes nothing be born, 4 arcs out of 4. Slots have two arcs
            // and one single mark in the middle. Both are refused, with the measurement.
            string su = (Str(a, "applies_to", "holes") ?? "holes").Trim().ToLowerInvariant();
            bool allWhole = true;
            switch (su)
            {
                case "": case "holes": break;
                case "raccordi":
                case "tutti":
                    return Failed("Sui raccordi la croce da codice non nasce: misurato, AddCenterMark su un arco " +
                                   "selezionato nella vista non mette niente (4 archi su 4). Chiedi «holes»; i centri " +
                                   "dei raccordi, se servono, vanno messi a mano in SOLIDWORKS.");
                case "asole":
                    return Failed("Le asole non le so: un'asola ha due archi e una croce sola in mezzo, e una croce " +
                                   "per arco sarebbe sbagliata. Chiedi «holes».");
                default:
                    return Failed("«su» puo' essere solo «holes», non «" + su + "».");
            }

            // WITHOUT `size_mm` the size of the document: it is a
            // studio convention, not a number of mine.
            bool itsDimension = a != null && a.ContainsKey("size_mm");
            double markSize = Units.M(Num(a, "size_mm", DefaultCentreMarkSizeMm));
            if (!itsDimension) markSize = DocumentCentreMarkSize(model, markSize);
            if (markSize <= 0) return Failed("«size_mm» deve essere maggiore di zero.");
            bool lines = Bool(a, "extended_lines", false);

            IMathUtility mu = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; } catch { }
            var selMgr = model.SelectionManager as ISelectionMgr;

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
                var marks = MarkHolesOfView(model, drawing, view, markSize, lines, mu, selMgr);
                int before = marks.Before;
                if (before < 0)
                    return Failed(
                        "Non riesco a contare le croci di centro della vista «" + (itemName ?? "?") +
                        "» prima di cominciare: senza quel numero non saprei dirti quali ho " +
                        "messo io. Non ho toccato niente.");
                foreach (bool notSelectable in marks.LeftWithoutMark)
                    notCreated.Add((itemName ?? "?") + ": un cerchio" + (notSelectable ? " (non si e' lasciato selezionare)" : ""));

                int after = marks.After;
                totalBefore += before;
                totalAfter += after < 0 ? before : after;
                already += marks.AlreadyMarked;
                perView.Add(new Dictionary<string, object>
                {
                    ["view"] = itemName ?? "(senza nome)",
                    ["circles"] = marks.Holes,
                    ["circles_not_holes"] = marks.CirclesThatAreNotHoles,
                    ["marks_before"] = before,
                    ["marks_after"] = NumberOrNull(after),
                    ["marks_added"] = marks.Added,
                    ["circles_already_marked"] = marks.AlreadyMarked,
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
                return Failed(notCreated.Count + (notCreated.Count == 1 ? " foro e'" : " fori sono") +
                               " senza croce di centro (" + string.Join("; ", notCreated) + ") e da codice la croce non " +
                               "nasce (misurato: AddCenterMark non la mette). La mette SOLIDWORKS creando la vista, se " +
                               "l'opzione del documento «Croci di centro sui fori» e' accesa: accendila nel modello di " +
                               "tavola e rifai la vista. Una tavola con qualche foro senza centro sembra finita e non lo e'.");
            }

            // ── ZERO MARKS IS NOT A SILENT SUCCESS ──────────────────────
            if (additions == 0)
            {
                measurements["verdict"] =
                    "NESSUNA CROCE AGGIUNTA. " +
                    (already > 0
                        ? already + (already == 1 ? " cerchio aveva" : " cerchi avevano") + " gia' la croce: niente di nuovo da mettere."
                        : "Su " + perView.Count + (perView.Count == 1 ? " vista" : " viste") +
                          " non c'e' nessun " + (allWhole ? "foro" : "arco") + " visibile: e' la vista sbagliata " +
                          "(i fori si vedono come cerchi dall'alto, di solito V2), oppure il pezzo non ne ha.") +
                    " Il numero e' zero e te lo dico.";
                _log("[agente] " + measurements["verdict"]);
                return SucceededWithoutCreating(measurements);
            }

            measurements["verdict"] =
                "Messe " + additions + (additions == 1 ? " croce di centro" : " croci di centro") +
                " su " + perView.Count + (perView.Count == 1 ? " vista" : " viste") +
                " (" + totalBefore + " -> " + totalAfter + "), una per cerchio, contate sulla tavola.";
            _log("[agente] " + measurements["verdict"]);
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
            /// <summary>Marks added, each one seen in the recount right after its call.</summary>
            public int Added;
            /// <summary>Holes that already had a mark within half a millimetre of their centre.</summary>
            public int AlreadyMarked;
            /// <summary>One entry per hole left without a mark: true when its edge could not even be selected.</summary>
            public List<bool> LeftWithoutMark = new List<bool>();
        }

        /// <summary>
        /// THE CENTRE MARKS ON THE HOLES OF ONE VIEW, shared by center_marks
        /// and standard_views (Z-C21.13; before, this walk lived inside
        /// CenterMarks).
        ///
        /// For every WHOLE circle of the view that is a hole seen end-on
        /// (<see cref="IsAVisibleHole"/>) and has no mark within half a
        /// millimetre of its centre, the edge is selected in the view and
        /// AddCenterMark is called; a mark counts as added only when the
        /// enumerated count went up by exactly one right after the call. What
        /// the file summary measured still holds: once SOLIDWORKS's own marks
        /// were deleted, AddCenterMark put none back, so a hole left without a
        /// mark is reported (<see cref="HoleCentreMarks.LeftWithoutMark"/>) and
        /// each caller says it in its own words.
        ///
        /// When the marks cannot be counted first, nothing is touched and
        /// Before is -1.
        /// </summary>
        private static HoleCentreMarks MarkHolesOfView(
            IModelDoc2 model, IDrawingDoc drawing, IView view,
            double size, bool extendedLines, IMathUtility mu, ISelectionMgr selMgr)
        {
            var marks = new HoleCentreMarks();
            marks.Before = HowManyCenterMarks(view);
            if (marks.Before < 0) return marks;

            MathTransform toSheet = null;
            try { toSheet = view.ModelToViewTransform; } catch { }
            var knownCentres = CenterMarkCenters(view);
            var leftWithoutMark = new List<bool>();
            int count = marks.Before;
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

                bool selected = false;
                try
                {
                    model.ClearSelection2(true);
                    var sd = selMgr?.CreateSelectData() as SelectData;
                    if (sd != null) { sd.View = (View)view; selected = (e as IEntity).Select4(false, sd); }
                }
                catch { }
                if (selected)
                    // OBSOLETE-MEASURED: center_marks_on_the_holes — IDrawingDoc.AddCenterMark is
                    // obsolete; its replacements InsertCenterMark2/3 answer True and add nothing
                    // (probes of 17/9 and 26/9/2026, see the summary of this file).
                    try { drawing.AddCenterMark(size, extendedLines); } catch { }
                model.ClearSelection2(true);
                int now = HowManyCenterMarks(view);
                if (now == count + 1)
                {
                    marks.Added++;
                    count = now;
                    if (centre != null) knownCentres.Add(RelativeToView(view, centre));
                }
                else
                {
                    leftWithoutMark.Add(!selected);
                    if (now >= 0) count = now;
                }
            }
            marks.LeftWithoutMark = leftWithoutMark;
            marks.After = HowManyCenterMarks(view);
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
                // Measured on 26/9/2026 (\`prova0089 croci8\`): FaceInSurfaceSense
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
        /// sheet. Measured on 26/9/2026 (`prova0089 croci4`): a circle at
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
        /// looked new and the message said «messe N croci» even on a
        /// view that already had them; if the one AFTER failed, the loop tried
        /// all three styles (double marks) and then said «quella vista non
        /// ha fori». A number that was not read cannot be compared.
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
    /// THE CENTRE MARKS ARE COUNTED BY ENUMERATING THEM (26/9/2026, `prova0089 croci5`).
    ///
    /// `IView.GetCenterMarkCount()` answered **0** on a view with three marks:
    /// the ones that SOLIDWORKS puts by itself on the holes when it creates the view (document
    /// option `swDetailingAutoInsertCenterMarksForHoles`, turned on here) it does not
    /// count; it counts only those added afterwards. `GetFirstCenterMark` /
    /// `GetNext` sees them all. The «blocco» of W2.11 of 17/9 — «l'API dice si'
    /// e non mette niente» — was largely this counter: the marks
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
