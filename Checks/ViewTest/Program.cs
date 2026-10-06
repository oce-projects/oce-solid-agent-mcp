using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.ViewTest
{
    /// <summary>
    /// HOW IS THE VIEW OF WHOEVER WORKS PUT BACK? (measurement for entry V1)
    ///
    /// `PartView` turns the part to photograph it and **must** put it back
    /// as it was: the bridge attaches to the document that a designer has in front of them,
    /// and leaving it turned is a real damage done by a read-only
    /// operation.
    ///
    /// On 15/9/2026 three roads were tried and rejected, **one per bench
    /// run of seventeen minutes**:
    ///
    ///   1. keep `IModelView.Transform` and reassign it      -> does not come back
    ///   2. `IModelDoc2.ViewOrientationUndo()`                -> does not come back
    ///   3. the triplet Orientation3 + Translation3 + Scale2     -> does not come back
    ///
    /// **This tool exists because that price was absurd.** The question
    /// takes three seconds: it opens a part, puts a block in it, turns the view,
    /// tries to put it back in all the ways that the API offers, and prints which
    /// ones work.
    ///
    ///     Checks\ViewTest\bin\ViewTest.exe
    ///
    /// It touches no document already open: it creates one of its own and closes it.
    ///
    /// WHAT IT COMPARES. The sixteen numbers of `IModelView.Transform`, which is the only
    /// readable description of «how the part looks now» — and it is already
    /// measured that it changes when the view changes, so the comparison has a real
    /// input.
    /// </summary>
    internal static class Program
    {
        private static int _failed;

        private static int Main()
        {
            ISldWorks sw;
            try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SOLIDWORKS cannot be attached to: " + ex.Message);
                return 2;
            }

            IModelDoc2 doc = null;
            try
            {
                string model = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                doc = sw.NewDocument(model, 0, 0, 0) as IModelDoc2;
                if (doc == null) { Console.Error.WriteLine("part not created"); return 2; }

                // A PART IS NEEDED. On an empty document `ViewZoomtofit2` has
                // nothing to frame, and half a check would prove nothing.
                Block(doc, 0.2, 0.1, 0.02);
                doc.ShowNamedView2("", (int)swStandardViews_e.swIsometricView);
                doc.GraphicsRedraw2();
                doc.ViewZoomtofit2();
                doc.GraphicsRedraw2();

                var view = doc.ActiveView as IModelView;
                if (view == null) { Console.Error.WriteLine("no active view"); return 2; }

                Console.WriteLine();
                Console.WriteLine("=== how to put the view back ===");
                Console.WriteLine();

                // ── does it make sense to compare? ───────────────────────
                double[] a = Numbers(view);
                Turn(doc, swStandardViews_e.swFrontView);
                double[] b = Numbers(view);
                Check("ShowNamedView2 really changes the sixteen numbers",
                      a != null && !Equal(a, b),
                      a == null ? "not readable" : "");

                // ── 1. riassegnare Transform ─────────────────────────────
                Test(doc, view, "riassegnare Transform", restore =>
                {
                    var kept = view.Transform;
                    restore(() => { try { view.Transform = kept; } catch { } });
                });

                // ── 2. Transform COPIED (IInverse twice) ─────────────────
                Test(doc, view, "Transform copied with IInverse x2", restore =>
                {
                    var alive = view.Transform as IMathTransform;
                    var rov = alive == null ? null : alive.IInverse() as IMathTransform;
                    var copy = rov == null ? null : rov.IInverse();
                    restore(() => { try { if (copy != null) view.Transform = copy; } catch { } });
                });

                // ── 3. ViewOrientationUndo ───────────────────────────────
                Test(doc, view, "ViewOrientationUndo()", restore =>
                {
                    restore(() => { try { doc.ViewOrientationUndo(); } catch { } });
                });

                // ── 4. the triplet, by reference ─────────────────────────
                Test(doc, view, "triplet Orientation3/Translation3/Scale2 (reference)", restore =>
                {
                    var o = view.Orientation3;
                    var t = view.Translation3;
                    double s = view.Scale2;
                    restore(() =>
                    {
                        try { view.Orientation3 = o; } catch { }
                        try { view.Translation3 = t; } catch { }
                        try { view.Scale2 = s; } catch { }
                    });
                });

                // ── 5. the triplet, COPIED ───────────────────────────────
                Test(doc, view, "COPIED triplet (IInverse x2 + IScale 1.0)", restore =>
                {
                    var o = view.Orientation3 as IMathTransform;
                    var rov = o == null ? null : o.IInverse() as IMathTransform;
                    var orCopy = rov == null ? null : rov.IInverse();
                    var t = view.Translation3 as IMathVector;
                    var tCopy = t == null ? null : t.IScale(1.0);
                    double s = view.Scale2;
                    restore(() =>
                    {
                        try { if (orCopy != null) view.Orientation3 = orCopy; } catch { }
                        try { if (tCopy != null) view.Translation3 = tCopy; } catch { }
                        try { if (s > 0) view.Scale2 = s; } catch { }
                    });
                });

                // ── 6. the copied triplet, on the RE-READ view ───────────
                // If `ShowNamedView2` left the `IModelView` object stale,
                // the writes would go to something disconnected while the
                // reads would keep working. `ActiveView` is re-read
                // before writing and we look at whether anything changes.
                Test(doc, view, "triplet copied, re-read on ActiveView", restore =>
                {
                    var o = view.Orientation3 as IMathTransform;
                    var rov = o == null ? null : o.IInverse() as IMathTransform;
                    var orCopy = rov == null ? null : rov.IInverse();
                    var t = view.Translation3 as IMathVector;
                    var tCopy = t == null ? null : t.IScale(1.0);
                    double s = view.Scale2;
                    restore(() =>
                    {
                        var now = doc.ActiveView as IModelView;
                        if (now == null) return;
                        try { if (orCopy != null) now.Orientation3 = orCopy; } catch { }
                        try { if (tCopy != null) now.Translation3 = tCopy; } catch { }
                        try { if (s > 0) now.Scale2 = s; } catch { }
                    });
                });

                // ── 7. the copied triplet, in the order O -> S -> T ──────
                //
                // The numbers of check 5 say where the rest is: the rotation
                // (indices 0-8 of the matrix) comes back **exact**, and only index 9 is wrong,
                // that is the translation, by 4.77. Putting the scale
                // last puts back a translation that holds for the scale
                // of before. Here the opposite order is tried.
                Test(doc, view, "triplet copied, in order O -> S -> T", restore =>
                {
                    var o = view.Orientation3 as IMathTransform;
                    var rov = o == null ? null : o.IInverse() as IMathTransform;
                    var orCopy = rov == null ? null : rov.IInverse();
                    var t = view.Translation3 as IMathVector;
                    var tCopy = t == null ? null : t.IScale(1.0);
                    double s = view.Scale2;
                    restore(() =>
                    {
                        try { if (orCopy != null) view.Orientation3 = orCopy; } catch { }
                        try { if (s > 0) view.Scale2 = s; } catch { }
                        try { if (tCopy != null) view.Translation3 = tCopy; } catch { }
                    });
                });

                // ── 8. the triplet, applied TWICE ────────────────────────
                // If every write carries along a recalculation of the others,
                // a second round on the same values converges. If it does not
                // converge, it is not an ordering problem.
                Test(doc, view, "triplet copied, applied twice", restore =>
                {
                    var o = view.Orientation3 as IMathTransform;
                    var rov = o == null ? null : o.IInverse() as IMathTransform;
                    var orCopy = rov == null ? null : rov.IInverse();
                    var t = view.Translation3 as IMathVector;
                    var tCopy = t == null ? null : t.IScale(1.0);
                    double s = view.Scale2;
                    restore(() =>
                    {
                        for (int benchRun = 0; benchRun < 2; benchRun++)
                        {
                            try { if (orCopy != null) view.Orientation3 = orCopy; } catch { }
                            try { if (s > 0) view.Scale2 = s; } catch { }
                            try { if (tCopy != null) view.Translation3 = tCopy; } catch { }
                        }
                    });
                });

                // ── 9. the name of the view, put back ────────────────────
                // It works ONLY if one started from a standard view, and for
                // that reason it is not a solution: the orientation of whoever works
                // almost never has a name. But if it is the only one that comes back, it says that the
                // problem lies in the writes and not in the reads.
                Test(doc, view, "ShowNamedView2 on the starting view", restore =>
                {
                    restore(() =>
                    {
                        try
                        {
                            doc.ShowNamedView2("", (int)swStandardViews_e.swIsometricView);
                            doc.ViewZoomtofit2();
                        }
                        catch { }
                    });
                });

                Console.WriteLine();
                Console.WriteLine(_failed == 0
                    ? "All the ways work."
                    : _failed + " ways out of 9 do not put the view back.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("the check broke: " + ex.GetType().Name +
                                        ": " + ex.Message);
                return 2;
            }
            finally
            {
                if (doc != null) { try { sw.CloseDoc(doc.GetTitle()); } catch { } }
            }
        }

        /// <summary>
        /// One road: we ALWAYS restart from the framed isometric, we keep
        /// a note of how it is, we turn and zoom, we try to put it back, we
        /// compare.
        ///
        /// The start is redone at every run because the previous road may
        /// have left the view somewhere else, and then the following check
        /// would start from a place it does not know.
        /// </summary>
        private static void Test(
            IModelDoc2 doc, IModelView view, string itemName, Action<Action<Action>> prepareAndPutBack)
        {
            doc.ShowNamedView2("", (int)swStandardViews_e.swIsometricView);
            doc.GraphicsRedraw2();
            doc.ViewZoomtofit2();
            doc.GraphicsRedraw2();

            double[] start = Numbers(view);
            Action restored = null;
            prepareAndPutBack(action => { restored = action; });

            Turn(doc, swStandardViews_e.swRightView);
            doc.ViewZoomtofit2();
            doc.GraphicsRedraw2();

            if (restored != null) restored();
            doc.GraphicsRedraw2();

            double[] final = Numbers(view);
            Check(itemName, Equal(start, final),
                  start == null ? "numbers not readable" : Deviation(start, final));
        }

        private static void Turn(IModelDoc2 doc, swStandardViews_e which)
        {
            doc.ShowNamedView2("", (int)which);
            doc.GraphicsRedraw2();
        }

        private static double[] Numbers(IModelView view)
        {
            try
            {
                var m = view.Transform as IMathTransform;
                return m == null ? null : m.ArrayData as double[];
            }
            catch { return null; }
        }

        private static bool Equal(double[] a, double[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > 1e-9) return false;
            return true;
        }

        /// <summary>By how much it was wrong, on the number that deviated most.</summary>
        private static string Deviation(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return "";
            double worse = 0;
            int location = -1;
            for (int i = 0; i < a.Length; i++)
            {
                double d = Math.Abs(a[i] - b[i]);
                if (d > worse) { worse = d; location = i; }
            }
            if (worse <= 1e-9) return "";
            return "maximum deviation " + worse.ToString("0.####") + " on number " + location;
        }

        private static void Check(string itemName, bool ok, string detail)
        {
            if (!ok) _failed++;
            Console.WriteLine((ok ? "  OK   " : "  NO   ") + itemName.PadRight(52) + detail);
        }

        /// <summary>A block, in metres: SOLIDWORKS works in metres.</summary>
        private static void Block(IModelDoc2 doc, double l, double w, double h)
        {
            doc.SketchManager.InsertSketch(true);
            doc.SketchManager.CreateCornerRectangle(-l / 2, -w / 2, 0, l / 2, w / 2, 0);
            doc.SketchManager.InsertSketch(true);
            doc.FeatureManager.FeatureExtrusion3(
                true, false, false, 0, 0, h, 0, false, false, false, false,
                0, 0, false, false, false, false, true, true, true, 0, 0, false);
        }
    }
}
