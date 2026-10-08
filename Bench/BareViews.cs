using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Bench
{
    internal static partial class Program
    {
        /// <summary>
        /// THE BARE VIEWS, MADE ON PURPOSE (final bench fix, 8/10/2026).
        ///
        /// SOLIDWORKS puts a centre mark on every hole when it creates a view, if
        /// the document toggle swDetailingAutoInsertCenterMarksForHoles is on (it is,
        /// in the house template). In some long bench runs it did not, and the
        /// bridge's remedy for a bare view was AddCenterMark, which adds nothing:
        /// `center_marks_on_the_holes` and `drawing_is_a_second_document` went red
        /// with zero marks on three holes. No catalog operation turns the toggle
        /// off, so no declared case could build a bare view, and the remedy was
        /// never seen working or failing. Here the toggle is turned off by COM on
        /// the new drawing before the views, and the two callers of the shared walk
        /// are measured: `standard_views` must mark the three holes itself, and
        /// `center_marks` on a single bare top view must add three, never doubling
        /// marks that are already there.
        ///
        /// On library 0.152.0 (AddCenterMark) the four checks are red, zero marks
        /// added (measured on 8/10/2026); on 0.152.1 the four are green. The
        /// situation is checked before the remedy: if the
        /// views come out with marks anyway, the group says it measured nothing.
        /// </summary>
        private static void TestBareViews()
        {
            Console.WriteLine();
            Console.WriteLine("=== bare views: the centre marks SOLIDWORKS did not put ===");
            IModelDoc2 part = null, drawing = null, single = null;
            string file = Path.Combine(Expectations.Work, "bare_views_bench.sldprt");
            try
            {
                part = NewPart();
                string error;
                // The plate of center_marks_on_the_holes: 120 x 80 x 10, three Ø8 through.
                if (Run(part, "extrude_rectangle", new Dictionary<string, object>
                    {
                        ["plane"] = "top", ["length_mm"] = 120.0, ["width_mm"] = 80.0,
                        ["thickness_mm"] = 10.0, ["centered"] = true,
                    }, out error) == null)
                { BenchError("bare views", "the plate: " + error); return; }
                foreach (double x in new[] { -40.0, 0.0, 40.0 })
                {
                    var docState = ReadState(part);
                    string up = FaceUp(docState);
                    if (up == null) { BenchError("bare views", "no face up on the plate"); return; }
                    if (Run(part, "cylindrical_cut", new Dictionary<string, object>
                        {
                            ["face_uid"] = up, ["x_mm"] = x, ["y_mm"] = 0.0,
                            ["diameter_mm"] = 8.0, ["through_all"] = true,
                        }, out error, docState) == null)
                    { BenchError("bare views", "the hole at x " + x + ": " + error); return; }
                }
                FileInfo saved = new FileInfo(file);
                try { if (saved.Exists) saved.Delete(); } catch { }
                if (Run(part, "save_document", new Dictionary<string, object> { ["path"] = file }, out error) == null || !File.Exists(file))
                { BenchError("bare views", "the part cannot be saved: " + error); return; }

                // ── 1. standard_views on a drawing that makes bare views ─────
                Run(part, "create_drawing", new Dictionary<string, object> { ["paper_size"] = "A3" }, out error, out drawing);
                if (drawing == null) { BenchError("bare views", "create_drawing: " + error); return; }
                string off = AutoCentreMarksOff(drawing);
                if (off != null) { BenchError("bare views", off); return; }
                var views = Run(drawing, "standard_views", new Dictionary<string, object> { ["model"] = file }, out error);
                if (views == null) { BenchError("bare views", "standard_views: " + error); return; }
                string afterRebuild = Field(views, "centre_marks_after_rebuild");
                if (afterRebuild != "0")
                {
                    BenchError("bare views", "the views were not bare (centre_marks_after_rebuild " + afterRebuild +
                               "): with the toggle off SOLIDWORKS still put marks, and this group measures nothing");
                    return;
                }
                Check("bare views: standard_views marks the three holes SOLIDWORKS left bare",
                      Field(views, "centre_marks_added_by_bridge") == "3",
                      "centre_marks_added_by_bridge " + Field(views, "centre_marks_added_by_bridge") +
                      ", holes_left_without_centre_mark " + Field(views, "holes_left_without_centre_mark"));
                Check("bare views: the state counts three marks on the sheet after standard_views",
                      Field(ReadState(drawing), "center_marks_total") == "3",
                      "center_marks_total " + Field(ReadState(drawing), "center_marks_total"));

                // The check afterwards: all three found, none added, none doubled.
                var again = Run(drawing, "center_marks", new Dictionary<string, object> { ["view"] = "V2" }, out error);
                string totalAgain = Field(ReadState(drawing), "center_marks_total");
                Check("bare views: center_marks afterwards finds the three and adds none (no double marks)",
                      again != null && Field(again, "marks_added") == "0" && Field(again, "circles_already_marked") == "3" && totalAgain == "3",
                      again == null ? Short(error)
                          : "marks_added " + Field(again, "marks_added") + ", circles_already_marked " +
                            Field(again, "circles_already_marked") + ", center_marks_total " + totalAgain);

                // ── 2. center_marks on a single bare top view ──────────────
                Run(part, "create_drawing", new Dictionary<string, object> { ["paper_size"] = "A3" }, out error, out single);
                if (single == null) { BenchError("bare views", "create_drawing (second): " + error); return; }
                off = AutoCentreMarksOff(single);
                if (off != null) { BenchError("bare views", off); return; }
                if (Run(single, "model_view", new Dictionary<string, object>
                    { ["model"] = file, ["view"] = "top", ["x_mm"] = 200, ["y_mm"] = 150 }, out error) == null)
                { BenchError("bare views", "model_view: " + error); return; }
                string bare = Field(ReadState(single), "center_marks_total");
                if (bare != "0")
                {
                    BenchError("bare views", "the top view was not bare (center_marks_total " + bare + "): this check measures nothing");
                    return;
                }
                var marked = Run(single, "center_marks", new Dictionary<string, object> { ["view"] = "V1" }, out error);
                string total = Field(ReadState(single), "center_marks_total");
                Check("bare views: center_marks puts the three marks on a bare top view, one per hole",
                      marked != null && Field(marked, "marks_added") == "3" && total == "3",
                      marked == null ? Short(error) : "marks_added " + Field(marked, "marks_added") + ", center_marks_total " + total);
            }
            catch (Exception ex) { BenchError("bare views", Why(ex)); }
            finally
            {
                try { if (single != null) _sw.CloseDoc(single.GetTitle()); } catch { }
                try { if (drawing != null) _sw.CloseDoc(drawing.GetTitle()); } catch { }
                Close(part);
                FileInfo leftover = new FileInfo(file);
                try { if (leftover.Exists) leftover.Delete(); } catch { }
            }
        }

        /// <summary>
        /// Turns off, on this drawing only, the document toggle that makes
        /// SOLIDWORKS put the centre marks on holes when a view is created, and
        /// reads it back. Null when done; else what went wrong.
        /// </summary>
        private static string AutoCentreMarksOff(IModelDoc2 drawing)
        {
            const int toggle = (int)swUserPreferenceToggle_e.swDetailingAutoInsertCenterMarksForHoles;
            const int option = (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified;
            drawing.Extension.SetUserPreferenceToggle(toggle, option, false);
            return drawing.Extension.GetUserPreferenceToggle(toggle, option)
                ? "the toggle swDetailingAutoInsertCenterMarksForHoles does not turn off on the drawing"
                : null;
        }

        /// <summary>A field of an answer or a state as text; «(missing)» when it is not there.</summary>
        private static string Field(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key)) return "(missing)";
            return d[key] == null ? "null" : Convert.ToString(d[key], System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
