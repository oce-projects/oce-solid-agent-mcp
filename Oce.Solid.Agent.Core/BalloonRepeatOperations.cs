using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE BALLOONS AGAIN IN ANOTHER VIEW (report 8cd843, round 22, point 9, 28/10/2026). On
    /// the drawing of 26M000002 the owner repeats the balloons on the isometric, the section and
    /// the detail; «balloons» could not: AutoBalloon5 puts none on a component that has its
    /// balloon in another view (seen on the Starlink, 26/9/2026), and the step answered
    /// «every entry already has its balloon».
    ///
    /// «again»: true takes the road of one balloon at a time, the one the spreading of the
    /// arrows already uses (BalloonSpreadOperations.ReinsertBalloon, measured on round 14):
    /// a point on a visible line of the component, `SelectByID2` «EDGE» there,
    /// `IModelDocExtension.CreateBalloonOptions` and `InsertBOMBalloon2`
    /// (IModelDocExtension~InsertBOMBalloon2.html, Remarks: «Select an item for which to
    /// create a BOM balloon. Call IModelDocExtension::CreateBalloonOptions [...] Call this
    /// method using the IBalloonOptions object»; example «Insert and Show BOM Table and BOM
    /// Balloon»). The selection is checked to be on that component before the balloon is
    /// made, the number is read back, and the balloon is moved out of the view on the side of
    /// its point. One balloon for the instances of the same part and configuration, as
    /// AutoBalloon5 with IgnoreMultiple; a component that already has its balloon in THIS view
    /// is left as it is.
    ///
    /// MEASURED ON SOLIDWORKS (02/11/2026, a copy of 26M000002, note 101): on the section every part
    /// was refused as «covered by another component», with nothing covering it: the edge was picked,
    /// and GetSelectedObjectsComponent4 gave no IComponent2, because in a drawing it gives «Component
    /// or Drawing component» (ISelectionMgr~GetSelectedObjectsComponent4.html). And the components of
    /// a section have no configuration, those of the model one. On the detail no edge could be picked:
    /// the longest lines of a part are outside its circle, where the view shows nothing.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>How far beyond the outline of the view a balloon goes, in metres of sheet.</summary>
        internal const double BalloonBeyondOutline = 0.010;

        /// <summary>
        /// Where a balloon goes for a point (x, y) of its component: out of the outline of the
        /// view (x0, y0, x1, y1) along the line from the centre of the view through the point,
        /// <paramref name="beyond"/> past the edge it crosses. A point on the centre goes up.
        /// Pure, so the contract suite reads it.
        /// </summary>
        internal static double[] BalloonOutside(double x, double y, double[] outline, double beyond)
        {
            double cx = (outline[0] + outline[2]) / 2, cy = (outline[1] + outline[3]) / 2;
            double vx = x - cx, vy = y - cy, l = Math.Sqrt(vx * vx + vy * vy);
            if (l < 1e-9) { vx = 0; vy = 1; l = 1; }
            vx /= l; vy /= l;
            double tx = vx > 1e-12 ? (outline[2] - cx) / vx : vx < -1e-12 ? (outline[0] - cx) / vx : double.MaxValue;
            double ty = vy > 1e-12 ? (outline[3] - cy) / vy : vy < -1e-12 ? (outline[1] - cy) / vy : double.MaxValue;
            double t = Math.Min(tx, ty) + beyond;
            return new[] { cx + vx * t, cy + vy * t };
        }

        /// <summary>
        /// The places tried for a balloon whose arrow ends at (x, y) in a view of outline {x0, y0, x1, y1}:
        /// out of the outline along the line from its centre through the arrow (BalloonOutside), then that
        /// line turned by 15°, 30° ... 90° either side, the smaller turn first, at 10, 18 and 26 mm beyond
        /// the edge; each {x, y}, 39 in all. Pure, so the contract suite reads it.
        /// </summary>
        internal static List<double[]> BalloonCandidates(double x, double y, double[] outline)
        {
            var places = new List<double[]>();
            double cx = (outline[0] + outline[2]) / 2, cy = (outline[1] + outline[3]) / 2;
            double vx = x - cx, vy = y - cy, l = Math.Sqrt(vx * vx + vy * vy);
            if (l < 1e-9) { vx = 0; vy = 1; l = 1; }
            vx /= l; vy /= l;
            foreach (double beyond in new[] { 0.010, 0.018, 0.026 })
                foreach (double deg in new[] { 0.0, 15, -15, 30, -30, 45, -45, 60, -60, 75, -75, 90, -90 })
                {
                    double r = deg * Math.PI / 180.0;
                    double ux = vx * Math.Cos(r) - vy * Math.Sin(r), uy = vx * Math.Sin(r) + vy * Math.Cos(r);
                    places.Add(BalloonOutside(cx + ux * 0.001, cy + uy * 0.001, outline, beyond));
                }
            return places;
        }

        /// <summary>
        /// The places tried first for a balloon of size «size» (metres) centred at (x, y): three rings
        /// around it, 1.25, 2.5 and 3.75 sizes away, every 30° from the right; each {x, y}, 36 in all,
        /// the nearer ring first. A balloon moved there keeps a leader close to the one it had. Pure.
        /// </summary>
        internal static List<double[]> BalloonRing(double x, double y, double size)
        {
            var places = new List<double[]>();
            for (int k = 1; k <= 3; k++)
                for (int a = 0; a < 360; a += 30)
                {
                    double r = 1.25 * k * size, rad = a * Math.PI / 180.0;
                    places.Add(new[] { x + r * Math.Cos(rad), y + r * Math.Sin(rad) });
                }
            return places;
        }

        /// <summary>
        /// The box {x0, y0, x1, y1} of the circle of a balloon from its leader: the first point of the
        /// leader lies on the circle, so the centre is that point «radius» further along the leader, away
        /// from its tip (measured 02/11/2026 on the 4 of the isometric of 26M000002: 402.96, 187.75 mm
        /// against 402.78, 187.41 mm of INote.GetBalloonInfo, whose centre is not updated after
        /// IAnnotation.SetPosition2; its radius, 4.98 mm, is). Null when the two points are one. Pure.
        /// </summary>
        internal static double[] BalloonCircleBox(double[] leaderStart, double[] tip, double radius)
        {
            if (leaderStart == null || tip == null || leaderStart.Length < 2 || tip.Length < 2) return null;
            double ux = leaderStart[0] - tip[0], uy = leaderStart[1] - tip[1], l = Math.Sqrt(ux * ux + uy * uy);
            if (l < 1e-9) return null;
            double cx = leaderStart[0] + ux / l * radius, cy = leaderStart[1] + uy / l * radius;
            return new[] { cx - radius, cy - radius, cx + radius, cy + radius };
        }

        /// <summary>
        /// What a balloon whose circle is the box {x0, y0, x1, y1} and whose leader is the segment «leader»
        /// {x0, y0, x1, y1} (null: not judged) falls on, or null when it is free: the frame of the sheet
        /// (FrameOfTheSheet from the edge of the paper {width, height}, not judged when null), one of the
        /// boxes (named at the same place of «names»: notes, circles of balloons, outlines of views), a
        /// leader of another balloon across its circle, its own leader across a box that «crossable»
        /// does not let it cross (a note, a circle; an outline yes: a leader goes into its view), or the
        /// title block, within «margin». Pure, so the contract suite reads it.
        /// </summary>
        internal static string BalloonBlocked(double[] box, double[] leader, double[] sheet, IList<double[]> boxes, IList<string> names,
                                              IList<bool> crossable, IList<double[]> leaders, IList<string> leaderNames,
                                              double[] titleBlock, double margin)
        {
            if (sheet != null && (box[0] < FrameOfTheSheet || box[1] < FrameOfTheSheet ||
                                  box[2] > sheet[0] - FrameOfTheSheet || box[3] > sheet[1] - FrameOfTheSheet))
                return "across the frame of the sheet";
            for (int i = 0; i < boxes.Count; i++)
                if (BoxesTouch(box, boxes[i], margin)) return "over " + names[i];
            var corners = BoxCorners(box);
            for (int i = 0; i < leaders.Count; i++)
                if (SegmentInRectangle(leaders[i], corners, -margin)) return "on the leader of " + leaderNames[i];
            if (leader != null)
                for (int i = 0; i < boxes.Count; i++)
                    if (!crossable[i] && SegmentInRectangle(leader, BoxCorners(boxes[i]), -margin)) return "its leader across " + names[i];
            if (titleBlock != null && BoxesTouch(box, titleBlock, margin)) return "over the title block";
            return null;
        }

        private static bool BoxesTouch(double[] a, double[] b, double margin)
        {
            return a[0] < b[2] + margin && b[0] < a[2] + margin && a[1] < b[3] + margin && b[1] < a[3] + margin;
        }

        /// <summary>The balloons of a view by «view name|annotation name», to tell the ones a call made.</summary>
        private static HashSet<string> BalloonKeys(IView view)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            string viewName = "";
            try { viewName = view.GetName2() ?? ""; } catch { }
            foreach (BalloonRead b in BalloonsOfView(view))
            {
                try { keys.Add(viewName + "|" + b.Annotation.GetName()); } catch { }
            }
            return keys;
        }

        /// <summary>
        /// THE BALLOONS A CALL MADE, EACH WHERE IT IS FREE (note 98, defect 7; seen on 26M000002 with 0.168.0,
        /// 02/11/2026). «again» put every balloon on the line from the centre of the view through its arrow,
        /// 10 mm past the outline: the 2 and the 4 of section A-A fell at 202.78, 262.43 and 203.98, 262.42 mm,
        /// the 4 and the 2 of detail B at 61.37, 67.76 and 61.30, 65.83 mm, one on the other, and the 1 of the
        /// section at 202.78, 196.55 mm on its label; AutoBalloon5 put the 2 of the isometric across the frame.
        ///
        /// A balloon born in this call («before» holds the others, left as they are) is judged by its CIRCLE
        /// (BalloonCircleBox, radius of INote.GetBalloonInfo) and its leader, not by INote.GetExtent: the
        /// extent of a balloon holds its leader down to the tip (the 1 of the section: 161.27..218 mm high
        /// for a circle of 9.96 mm), and judging that box moved balloons whose leader only went into their
        /// view, 50 mm away, with leaders across the label (the second run of 0.170.0). It falls on
        /// something when BalloonBlocked says so: the frame, the notes of the sheet and its views by their
        /// extent (also those whose text reads empty: the label of the section reads « \r\n », its text is
        /// linked), the circles and leaders of the other balloons, the outlines of the other views, its own
        /// view's outline only when «outOfItsView» (the balloons of «again»; AutoBalloon5 puts balloons
        /// inside the corners of the outline), the title block, and (library 0.198.6) the tables of the
        /// sheet and of its views, the bill of materials first (TableBoundingBox, the box ReadSheet uses for
        /// free places; a leader does not cross them). Then it is moved, its tip kept, to the
        /// first free place: brought inside the frame, then BalloonRing around that, then BalloonCandidates
        /// for its arrow. With none it stays, said. One row per balloon looked at.
        /// </summary>
        private static List<Dictionary<string, object>> FreeTheBalloons(IDrawingDoc drawing, IView view, HashSet<string> before,
                                                                        bool outOfItsView, out string note)
        {
            note = null;
            var rows = new List<Dictionary<string, object>>();
            string viewName = "";
            try { viewName = view.GetName2() ?? ""; } catch { }
            double[] outline = null;
            try { outline = view.GetOutline() as double[]; } catch { }
            if (outline == null || outline.Length < 4) { note = "the outline of the view could not be read: the balloons are left where they are"; return rows; }
            double[] sheet = SheetDimensions(drawing);
            double[] titleBlock = TitleBlockOfTheDrawing(drawing, out _);
            // Every box (notes, circles, outlines of the other views) and every leader of the sheet.
            var keys = new List<string>();
            var boxes = new List<double[]>();
            var names = new List<string>();
            var crossable = new List<bool>();
            var leaderKeys = new List<string>();
            var leaders = new List<double[]>();
            var leaderNames = new List<string>();
            var tips = new Dictionary<string, double[]>(StringComparer.Ordinal);
            const double fallbackRadius = 0.005;
            try
            {
                bool sheetView = true;
                for (var v = drawing.GetFirstView() as IView; v != null; v = v.GetNextView() as IView)
                {
                    string vn = "";
                    try { vn = v.GetName2() ?? ""; } catch { }
                    if (!sheetView && vn != viewName)
                    {
                        double[] o = null;
                        try { o = v.GetOutline() as double[]; } catch { }
                        if (o != null && o.Length >= 4)
                        {
                            keys.Add("view|" + vn); boxes.Add(new[] { o[0], o[1], o[2], o[3] }); names.Add("the view «" + vn + "»"); crossable.Add(true);
                        }
                    }
                    sheetView = false;
                    for (var n = v.GetFirstNote() as INote; n != null; n = n.GetNext() as INote)
                    {
                        try
                        {
                            var annotation = n.GetAnnotation() as IAnnotation;
                            string key = vn + "|" + annotation?.GetName();
                            bool balloon = n.IsBomBalloon();
                            string text = n.GetText();
                            string shown = balloon ? (text ?? "").Trim() : NoteShown(text);
                            string name = string.IsNullOrEmpty(shown) ? "a note whose text reads empty (" + vn + ")"
                                        : (balloon ? "the balloon «" : "the note «") + shown + "» (" + vn + ")";
                            double[] box = null;
                            if (balloon)
                            {
                                double[] lead = null;
                                try { lead = annotation.GetLeaderPointsAtIndex(0) as double[]; } catch { }
                                double radius = fallbackRadius;
                                try { var info = n.GetBalloonInfo() as double[]; if (info != null && info.Length >= 7 && info[6] > 0) radius = info[6]; } catch { }
                                if (lead != null && lead.Length >= 6)
                                {
                                    var start = new[] { lead[0], lead[1] };
                                    var tip = new[] { lead[lead.Length - 3], lead[lead.Length - 2] };
                                    box = BalloonCircleBox(start, tip, radius);
                                    if (box != null)
                                    {
                                        tips[key] = tip;
                                        leaderKeys.Add(key);
                                        leaders.Add(new[] { (box[0] + box[2]) / 2, (box[1] + box[3]) / 2, tip[0], tip[1] });
                                        leaderNames.Add(name);
                                    }
                                }
                            }
                            if (box == null) box = ExtentBox(n.GetExtent() as double[]);
                            if (box == null) continue;
                            keys.Add(key);
                            boxes.Add(box);
                            names.Add(name);
                            crossable.Add(false);
                        }
                        catch { }
                    }
                    // THE TABLES (library 0.198.6): up to 0.198.5 a balloon could be moved onto the bill of
                    // materials, which is a table and not a note.
                    foreach (var o in (v.GetTableAnnotations() as object[]) ?? new object[0])
                    {
                        var table = o as ITableAnnotation;
                        double[] tableBox = TableBoundingBox(table, null);
                        if (tableBox == null) continue;
                        string tableName = null;
                        try { tableName = (table.GetAnnotation() as IAnnotation)?.GetName(); } catch { }
                        keys.Add("table|" + vn + "|" + tableName); boxes.Add(tableBox); names.Add("the table «" + (tableName ?? "") + "» (" + vn + ")"); crossable.Add(false);
                    }
                }
            }
            catch { note = "the notes of the sheet could not be read: the balloons are left where they are"; return rows; }

            const double margin = 0.0005;
            foreach (BalloonRead b in BalloonsOfView(view))
            {
                string name = null;
                try { name = b.Annotation.GetName(); } catch { }
                string key = viewName + "|" + name;
                if (name == null || before.Contains(key) || b.Position == null || b.Position.Length < 2) continue;
                int self = keys.IndexOf(key);
                if (self < 0) continue;
                var others = new List<double[]>();
                var otherNames = new List<string>();
                var otherCrossable = new List<bool>();
                for (int i = 0; i < keys.Count; i++)
                    if (i != self) { others.Add(boxes[i]); otherNames.Add(names[i]); otherCrossable.Add(crossable[i]); }
                if (outOfItsView) { others.Add(new[] { outline[0], outline[1], outline[2], outline[3] }); otherNames.Add("its own view"); otherCrossable.Add(true); }
                var otherLeaders = new List<double[]>();
                var otherLeaderNames = new List<string>();
                for (int i = 0; i < leaderKeys.Count; i++)
                    if (leaderKeys[i] != key) { otherLeaders.Add(leaders[i]); otherLeaderNames.Add(leaderNames[i]); }
                double[] box = boxes[self];
                double bx = (box[0] + box[2]) / 2, by = (box[1] + box[3]) / 2;
                double[] tip;
                tips.TryGetValue(key, out tip);
                Func<double, double, double[]> leaderFrom = (x, y) => tip == null ? null : new[] { x, y, tip[0], tip[1] };
                string why = BalloonBlocked(box, leaderFrom(bx, by), sheet, others, otherNames, otherCrossable, otherLeaders, otherLeaderNames, titleBlock, margin);
                if (why == null) continue;
                double bw = box[2] - box[0], bh = box[3] - box[1];
                // The places, the nearest first: inside the frame, rings around, then around the view.
                double sx = bx, sy = by;
                if (sheet != null)
                {
                    double edge = FrameOfTheSheet + margin;
                    sx = Math.Max(edge + bw / 2, Math.Min(sheet[0] - edge - bw / 2, bx));
                    sy = Math.Max(edge + bh / 2, Math.Min(sheet[1] - edge - bh / 2, by));
                }
                var centres = new List<double[]>();
                if (Math.Abs(sx - bx) + Math.Abs(sy - by) > 1e-9) centres.Add(new[] { sx, sy });
                centres.AddRange(BalloonRing(sx, sy, Math.Max(bw, bh)));
                double[] arrow = tip ?? b.Tip ?? new[] { bx, by };
                centres.AddRange(BalloonCandidates(arrow[0], arrow[1], outline));
                double[] shift = null;
                foreach (double[] cc in centres)
                {
                    double dx = cc[0] - bx, dy = cc[1] - by;
                    if (BalloonBlocked(new[] { box[0] + dx, box[1] + dy, box[2] + dx, box[3] + dy }, leaderFrom(cc[0], cc[1]), sheet,
                                       others, otherNames, otherCrossable, otherLeaders, otherLeaderNames, titleBlock, margin) == null)
                    {
                        shift = new[] { dx, dy };
                        break;
                    }
                }
                var row = new Dictionary<string, object>
                {
                    ["number"] = b.Number,
                    ["was"] = why,
                    ["from_mm"] = new object[] { Math.Round(bx * 1000, 2), Math.Round(by * 1000, 2) },
                };
                bool moved = false;
                if (shift != null)
                    try { moved = b.Annotation.SetPosition2(b.Position[0] + shift[0], b.Position[1] + shift[1], 0); } catch { }
                if (moved)
                {
                    boxes[self] = new[] { box[0] + shift[0], box[1] + shift[1], box[2] + shift[0], box[3] + shift[1] };
                    int li = leaderKeys.IndexOf(key);
                    if (li >= 0 && tip != null) leaders[li] = new[] { bx + shift[0], by + shift[1], tip[0], tip[1] };
                    row["to_mm"] = new object[] { Math.Round((bx + shift[0]) * 1000, 2), Math.Round((by + shift[1]) * 1000, 2) };
                }
                else row["left"] = shift == null ? "no free place near it nor 10 to 26 mm around the view: move it with «move_annotation»"
                                                 : "SOLIDWORKS refused the new place: move it with «move_annotation»";
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Whether two keys of BomKeyOf name the same row of a bill of materials: the same file, and the
        /// same configuration when both say one (a component of a section says none). Pure, so the
        /// contract suite reads it.
        /// </summary>
        internal static bool SameBomPart(string a, string b)
        {
            if (a == null || b == null) return false;
            int i = a.LastIndexOf('|'), j = b.LastIndexOf('|');
            if (i < 0 || j < 0) return a == b;
            if (a.Substring(0, i) != b.Substring(0, j) || i == 0) return false;
            string ca = a.Substring(i + 1), cb = b.Substring(j + 1);
            return ca.Length == 0 || cb.Length == 0 || ca == cb;
        }

        /// <summary>
        /// The part of a segment (x0, y0, x1, y1) inside a rectangle (x0, y0, x1, y1), null when none
        /// (Liang-Barsky). Pure, so the contract suite reads it.
        /// </summary>
        internal static double[] ClippedToOutline(double[] s, double[] r)
        {
            if (s == null || r == null || s.Length < 4 || r.Length < 4) return null;
            double t0 = 0, t1 = 1, dx = s[2] - s[0], dy = s[3] - s[1];
            double[] p = { -dx, dx, -dy, dy }, q = { s[0] - r[0], r[2] - s[0], s[1] - r[1], r[3] - s[1] };
            for (int k = 0; k < 4; k++)
            {
                if (Math.Abs(p[k]) < 1e-15) { if (q[k] < 0) return null; continue; }
                double u = q[k] / p[k];
                if (p[k] < 0) { if (u > t1) return null; if (u > t0) t0 = u; }
                else { if (u < t0) return null; if (u < t1) t1 = u; }
            }
            if (t1 - t0 < 1e-12) return null;
            return new[] { s[0] + t0 * dx, s[1] + t0 * dy, s[0] + t1 * dx, s[1] + t1 * dy };
        }

        /// <summary>
        /// The number a part has in the balloons of the views ({key, number} pairs), by its key (SameBomPart); null when no
        /// balloon names it. Pure, so the contract suite reads it.
        /// </summary>
        internal static string ExpectedNumber(List<string[]> numbers, string key)
        {
            if (numbers == null || key == null) return null;
            foreach (string[] pair in numbers)
                if (pair != null && pair.Length == 2 && SameBomPart(pair[0], key)) return pair[1];
            return null;
        }

        /// <summary>
        /// The points to try for a balloon: of the first <paramref name="lines"/> segments, in their order,
        /// the middle, then the quarter and three quarters of each. Pure, so the contract suite reads it.
        /// </summary>
        internal static List<double[]> PointsToTry(List<double[]> segments, int lines)
        {
            var points = new List<double[]>();
            if (segments == null) return points;
            int n = Math.Min(lines, segments.Count);
            foreach (double f in new[] { 0.5, 0.25, 0.75 })
                for (int i = 0; i < n; i++)
                {
                    double[] s = segments[i];
                    points.Add(new[] { s[0] + f * (s[2] - s[0]), s[1] + f * (s[3] - s[1]) });
                }
            return points;
        }

        /// <summary>
        /// The component of the first selection: an IComponent2, the component of an IDrawingComponent
        /// (IDrawingComponent~Component.html), or the owner of the selected entity (IEntity~GetComponent
        /// .html); null when none says it.
        /// </summary>
        private static IComponent2 PickedComponent(ISelectionMgr manager)
        {
            if (manager == null) return null;
            object raw = null;
            try { raw = manager.GetSelectedObjectsComponent4(1, -1); } catch { }
            var c = raw as IComponent2;
            if (c == null) try { c = (raw as IDrawingComponent)?.Component as IComponent2; } catch { }
            if (c == null) try { c = (manager.GetSelectedObject6(1, -1) as IEntity)?.GetComponent() as IComponent2; } catch { }
            return c;
        }

        /// <summary>The key of a component in a bill of materials: its file and configuration.</summary>
        private static string BomKeyOf(IComponent2 c)
        {
            if (c == null) return null;
            string path = null, configuration = null;
            try { path = c.GetPathName(); } catch { }
            try { configuration = c.ReferencedConfiguration; } catch { }
            return (path ?? "").ToLowerInvariant() + "|" + (configuration ?? "");
        }

        /// <summary>
        /// The route of «again»: a balloon for each part the view shows that has none in this
        /// view, even when another view has it.
        /// </summary>
        private Outcome BalloonsAgain(IModelDoc2 model, IDrawingDoc drawing, IView view)
        {
            string viewName = view.GetName2();
            double[] outline = null;
            try { outline = view.GetOutline() as double[]; } catch { }
            IMathUtility mu = null;
            try { mu = _sw.GetMathUtility() as IMathUtility; } catch { }
            object[] visible = null;
            try { visible = view.GetVisibleComponents() as object[]; } catch { }
            if (outline == null || outline.Length < 4 || mu == null || visible == null)
                return Failed("I cannot read the view «" + viewName + "» (its outline, its components or the transform to the " +
                              "sheet): without them I would not know where to put the balloons. I touched nothing.");

            // The parts that already have a balloon in THIS view.
            var here = new HashSet<string>(StringComparer.Ordinal);
            foreach (BalloonRead b in BalloonsOfView(view))
            {
                string key = BomKeyOf(b.Component);
                if (key != null) here.Add(key);
            }
            // One component per part and configuration.
            var groups = new List<IComponent2>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            int alreadyHere = 0;
            foreach (object o in visible)
            {
                var c = o as IComponent2;
                string key = BomKeyOf(c);
                if (key == null || !keys.Add(key)) continue;
                if (here.Contains(key)) { alreadyHere++; continue; }
                groups.Add(c);
            }
            int before = ViewBalloons(view);
            var balloonsBefore = BalloonKeys(view);
            if (groups.Count == 0)
            {
                var nothing = new Dictionary<string, object>
                {
                    ["view"] = viewName,
                    ["again"] = true,
                    ["parts_visible"] = keys.Count,
                    ["parts_with_a_balloon_here"] = alreadyHere,
                    ["balloons_placed"] = 0,
                    ["numbers"] = BalloonNumbers(view).ToArray(),
                    ["verdict"] = "Every part this view shows already has its balloon in this view: nothing to add.",
                };
                return SucceededWithoutCreating(nothing);
            }

            // THE NUMBER EACH PART HAS IN THE OTHER VIEWS: in a section the edge picked says no component
            // at all (02/11/2026, note 101), so there the balloon is checked by its number once born.
            var numbersElsewhere = new List<string[]>();
            try
            {
                for (var other = (drawing.GetFirstView() as IView)?.GetNextView() as IView; other != null; other = other.GetNextView() as IView)
                    foreach (BalloonRead b in BalloonsOfView(other))
                    {
                        string k = BomKeyOf(b.Component);
                        if (k != null && !string.IsNullOrEmpty(b.Number)) numbersElsewhere.Add(new[] { k, b.Number });
                    }
            }
            catch { }

            var placed = new List<Dictionary<string, object>>();
            var failed = new List<string>();
            foreach (IComponent2 c in groups)
            {
                string name = null;
                try { name = c.Name2; } catch { }
                string key = BomKeyOf(c);
                // The longest visible lines of the component INSIDE the view, the first eight tried: a
                // detail lists the lines outside its circle too, where nothing can be picked.
                var segments = new List<double[]>();
                foreach (double[] whole in ComponentSegmentsOnSheet(view, c, mu))
                {
                    double[] inside = ClippedToOutline(whole, outline);
                    if (inside != null) segments.Add(inside);
                }
                segments.Sort((p, q) => Math.Sqrt((q[2] - q[0]) * (q[2] - q[0]) + (q[3] - q[1]) * (q[3] - q[1]))
                    .CompareTo(Math.Sqrt((p[2] - p[0]) * (p[2] - p[0]) + (p[3] - p[1]) * (p[3] - p[1]))));
                INote born = null;
                double[] point = null;
                string why = segments.Count == 0 ? "it shows no visible line in this view" : null;
                string expected = ExpectedNumber(numbersElsewhere, key);
                // Three points of each line, its middle first: the circle of a detail cuts a line that
                // its rectangle keeps whole.
                var points = PointsToTry(segments, 8);
                for (int i = 0; i < Math.Min(16, points.Count) && born == null; i++)
                {
                    double[] at = points[i];
                    try
                    {
                        model.ClearSelection2(true);
                        if (!model.Extension.SelectByID2("", "EDGE", at[0], at[1], 0, false, 0, null, 0))
                        {
                            why = "no edge could be selected on its lines";
                            continue;
                        }
                        // THE SELECTION ON THAT PART: a line of another component over it
                        // would give that one's number.
                        var manager = model.SelectionManager as ISelectionMgr;
                        var picked = PickedComponent(manager);
                        if (picked == null && expected == null)
                        {
                            why = "the line picked says no component, and no other view gives its number to check a balloon against";
                            continue;
                        }
                        if (picked != null && !SameBomPart(BomKeyOf(picked), key))
                        {
                            string pickedName = null;
                            try { pickedName = picked.Name2; } catch { }
                            why = "its lines are covered by another component where I tried («" + (pickedName ?? "?") + "» picked)";
                            continue;
                        }
                        var options = model.Extension.CreateBalloonOptions() as BalloonOptions;
                        options.Style = (int)swBalloonStyle_e.swBS_Circular;
                        options.Size = (int)swBalloonFit_e.swBF_2Chars;
                        options.UpperTextContent = (int)swBalloonTextContent_e.swBalloonTextItemNumber;
                        options.ItemOrder = (int)swBalloonItemNumbersOrder_e.swBalloonItemNumbers_DoNotChangeItemNumbers;
                        born = model.Extension.InsertBOMBalloon2(options) as INote;
                        if (born == null) { why = "InsertBOMBalloon2 gave no balloon"; continue; }
                        if (picked == null)
                        {
                            string got = null;
                            try { got = (born.GetText() ?? "").Trim(); } catch { }
                            if (got != expected)
                            {
                                DeleteAnnotationSelected(model, born.GetAnnotation() as IAnnotation);
                                why = "the balloon born on a line that says no component reads «" + (got ?? "") + "», not «" + expected +
                                      "» of its part: I took it away";
                                born = null;
                                continue;
                            }
                        }
                        point = at;
                    }
                    catch (Exception ex) { why = Shorter(ex.Message); }
                }
                model.ClearSelection2(true);
                if (born == null)
                {
                    failed.Add((name ?? "?") + " (" + why + ")");
                    continue;
                }
                string number = null;
                try { number = (born.GetText() ?? "").Trim(); } catch { }
                double[] where = BalloonOutside(point[0], point[1], outline, BalloonBeyondOutline);
                bool moved = false;
                try { moved = (born.GetAnnotation() as IAnnotation).SetPosition2(where[0], where[1], 0); } catch { }
                placed.Add(new Dictionary<string, object>
                {
                    ["component"] = name,
                    ["number"] = number,
                    ["arrow_on_mm"] = new object[] { Math.Round(point[0] * 1000, 2), Math.Round(point[1] * 1000, 2) },
                    ["balloon_at_mm"] = moved ? new object[] { Math.Round(where[0] * 1000, 2), Math.Round(where[1] * 1000, 2) } : null,
                });
            }
            int after = ViewBalloons(view);
            var measurements = new Dictionary<string, object>
            {
                ["view"] = viewName,
                ["again"] = true,
                ["parts_visible"] = keys.Count,
                ["parts_with_a_balloon_here"] = alreadyHere,
                ["balloons_before"] = NumberOrNull(before),
                ["balloons_after"] = NumberOrNull(after),
                ["balloons_placed"] = before >= 0 && after >= 0 ? (object)(after - before) : null,
                ["placed"] = placed.ToArray(),
                ["not_placed"] = failed.ToArray(),
            };
            if (after > before)
            {
                string spreadNote;
                var moves = SpreadCrowdedBalloons(model, view, out spreadNote);
                measurements["arrows_moved"] = moves.ToArray();
                if (spreadNote != null) measurements["arrows_note"] = spreadNote;
                string freeNote;
                measurements["balloons_moved_to_a_free_place"] = FreeTheBalloons(drawing, view, balloonsBefore, true, out freeNote).ToArray();
                if (freeNote != null) measurements["balloons_free_note"] = freeNote;
            }
            measurements["numbers"] = BalloonNumbers(view).ToArray();
            if (before < 0 || after < 0)
                return Failed("I cannot count the balloons of the view: I do not know whether they were created. Look at the drawing.");
            if (after <= before)
                return Failed("No balloon was created in the view «" + viewName + "» (there were " + before + ", there are " + after +
                              "): " + string.Join("; ", failed) + ".");
            return SucceededWithoutCreating(measurements);
        }
    }
}
