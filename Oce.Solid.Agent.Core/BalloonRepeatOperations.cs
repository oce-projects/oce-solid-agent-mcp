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
    /// is left as it is. Not measured on SOLIDWORKS as a route of its own yet.
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

            var placed = new List<Dictionary<string, object>>();
            var failed = new List<string>();
            foreach (IComponent2 c in groups)
            {
                string name = null;
                try { name = c.Name2; } catch { }
                string key = BomKeyOf(c);
                // The longest visible lines of the component, the first four tried.
                var segments = ComponentSegmentsOnSheet(view, c, mu);
                segments.Sort((p, q) => Math.Sqrt((q[2] - q[0]) * (q[2] - q[0]) + (q[3] - q[1]) * (q[3] - q[1]))
                    .CompareTo(Math.Sqrt((p[2] - p[0]) * (p[2] - p[0]) + (p[3] - p[1]) * (p[3] - p[1]))));
                INote born = null;
                double[] point = null;
                string why = segments.Count == 0 ? "it shows no visible line in this view" : null;
                for (int i = 0; i < Math.Min(4, segments.Count) && born == null; i++)
                {
                    double[] s = segments[i];
                    double[] at = { (s[0] + s[2]) / 2, (s[1] + s[3]) / 2 };
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
                        var picked = manager == null ? null : manager.GetSelectedObjectsComponent4(1, -1) as IComponent2;
                        if (BomKeyOf(picked) != key)
                        {
                            why = "its lines are covered by another component where I tried";
                            continue;
                        }
                        var options = model.Extension.CreateBalloonOptions() as BalloonOptions;
                        options.Style = (int)swBalloonStyle_e.swBS_Circular;
                        options.Size = (int)swBalloonFit_e.swBF_2Chars;
                        options.UpperTextContent = (int)swBalloonTextContent_e.swBalloonTextItemNumber;
                        options.ItemOrder = (int)swBalloonItemNumbersOrder_e.swBalloonItemNumbers_DoNotChangeItemNumbers;
                        born = model.Extension.InsertBOMBalloon2(options) as INote;
                        if (born == null) why = "InsertBOMBalloon2 gave no balloon";
                        else point = at;
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
