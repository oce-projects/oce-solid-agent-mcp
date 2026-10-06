using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.FramingTest
{
    /// <summary>
    /// DOES THE PART FIT INSIDE THE FRAME? (entry V5)
    ///
    /// `PartView.Fire` turns the part, calls `ViewZoomtofit2` and saves a
    /// 1024x768 PNG. The isometric comes out whole and centred. **The three standard
    /// views do not.**
    ///
    /// The red was not found by me: it was written by two models while
    /// answering something else, during the measurement of entry V2 —
    /// «the third dimension cannot be determined because it **falls
    /// outside the frame**», «the view turns out **cut on the right side**».
    ///
    /// **A cut part is a different part.** An image that hides half of it
    /// is worse than no image, because whoever looks at it reasons on it
    /// all the same — and it is exactly what happened.
    ///
    /// ── WHAT IT MEASURES ───────────────────────────────────────────────────
    ///
    /// It shoots, then counts the PIXELS. The SOLIDWORKS background is a light
    /// gradient; the part is grey. We take the box of the pixels that are NOT
    /// background and look:
    ///
    ///   * **does it touch an edge?**  -> the part is cut, and it is the defect
    ///   * **how much does it fill?**  -> paid pixels that show nothing
    ///
    /// The first criterion is the real one. The second is a bonus: an image
    /// costs more than a thousand tokens, and half a frame of background is half of that
    /// cost thrown away.
    ///
    ///     Checks\FramingTest\bin\FramingTest.exe
    ///
    /// It touches no document already open: it creates one of its own and closes it.
    /// </summary>
    internal static class Program
    {
        private static int _failed;
        private static int _thinOnes;
        private static Assembly _library;
        private static Type _view;

        /// <summary>
        /// How close to the edge is «touches the edge».
        ///
        /// Not zero: the antialiasing bleeds by a pixel or two, and a part that
        /// reaches two pixels from the edge is cut anyway or on the point of
        /// being so. Three pixels of tolerance out of 1024 are 0.3%.
        /// </summary>
        private const int Allowance = 3;

        /// <summary>How much it must fill, at least, in the best direction.</summary>
        private const double MinimumFill = 0.60;

        private static int Main(string[] arguments)
        {
            string libraryPath = arguments.Length > 0
                ? arguments[0]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(libraryPath))
            {
                Console.Error.WriteLine("cannot find the library: " + libraryPath);
                return 2;
            }
            _library = Assembly.Load(File.ReadAllBytes(libraryPath));
            _view = _library.GetType("Oce.Solid.Agent.Core.PartView", false);
            if (_view == null)
            {
                Console.Error.WriteLine("PartView is not in " + libraryPath);
                return 2;
            }

            ISldWorks sw;
            try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SOLIDWORKS cannot be attached to: " + ex.Message);
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("=== does the part fit inside the frame? ===");
            Console.WriteLine("library " + _library.GetName().Version);
            Console.WriteLine();

            // FIRST THE RULER IS TESTED, THEN WE MEASURE.
            TheRulerSeesWell(sw);
            Console.WriteLine();

            // ── HOW WIDE THE VIEWPORT REALLY IS ──────────────────────────
            //
            // It is always and only cut on the right, never vertically and never on the
            // left. The hypothesis is that the graphics area on screen is
            // WIDER than 4:3, that `ViewZoomtofit2` frames inside that one, and that
            // `SaveBMP` then renders at 1024x768 losing the sides.
            //
            // A hypothesis in this section has already been wrong three times in a row.
            // This line asks for it.
            AspectRatio(sw);
            Console.WriteLine();

            // THREE SHAPES, CHOSEN TO BE UNCOMFORTABLE IN DIFFERENT WAYS:
            //   the L bracket is long and low  -> the front is very wide;
            //   the plate is wide and flat     -> the right is a strip;
            //   the shaft is long and narrow   -> the front is a line.
            // A cubic part would say nothing: it always fits.
            Test(sw, "L bracket 120x60x58", doc =>
            {
                Block(doc, 0.120, 0.060, 0.008);
            });
            Test(sw, "plate 200x120x15", doc =>
            {
                Block(doc, 0.200, 0.120, 0.015);
            });
            Test(sw, "bar 300x20x20", doc =>
            {
                Block(doc, 0.300, 0.020, 0.020);
            });

            Console.WriteLine();
            Console.WriteLine(_failed == 0
                ? "No view cut off."
                : _failed + " VIEWS CUT OFF.");
            if (_thinOnes > 0)
                Console.WriteLine(_thinOnes + " views fill less than " +
                                  (MinimumFill * 100).ToString("0") +
                                  "%: paid pixels that show nothing. " +
                                  "It is not a defect, it is a cost.");
            return _failed == 0 ? 0 : 1;
        }

        private static readonly string[] Views =
            { "isometric", "front", "top", "right" };

        private static void Test(ISldWorks sw, string itemName, Action<IModelDoc2> build)
        {
            IModelDoc2 doc = null;
            try
            {
                string model = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                doc = sw.NewDocument(model, 0, 0, 0) as IModelDoc2;
                if (doc == null) { Check(itemName, false, "part not created"); return; }
                build(doc);

                Console.WriteLine("  " + itemName);
                foreach (var which in Views)
                {
                    byte[] png = Fire(doc, which);
                    if (png == null || png.Length == 0)
                    {
                        Check("    " + which, false, "no image");
                        continue;
                    }
                    int beforeTheFailed = _failed;
                    Measure(which, png);

                    // A TOOL THAT SAYS «TAGLIATA» MUST SHOW WHAT.
                    // The first version of this measurement was wrong — the
                    // background threshold was taken by eye — and the only way
                    // to notice was to LOOK at the image. Now, if
                    // the verdict is red, the frame stays on disk.
                    if (_failed > beforeTheFailed) Deposit(itemName, which, png);
                }
            }
            catch (Exception ex)
            {
                Check(itemName, false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (doc != null) { try { sw.CloseDoc(doc.GetTitle()); } catch { } }
            }
        }

        private static byte[] Fire(IModelDoc2 doc, string which)
        {
            var method = _view.GetMethod("Fire", BindingFlags.Public | BindingFlags.Static);
            var p = new object[] { doc, which, 1024, 768, null };
            return method.Invoke(null, p) as byte[];
        }

        /// <summary>
        /// The box of the pixels that are not background, and the verdict.
        ///
        /// WHAT BACKGROUND IS. The SOLIDWORKS background is a very light and almost
        /// neutral gradient; the part is medium grey. A pixel is called background if it is
        /// **light**: the sum of the three channels above a threshold. We do not look for an exact
        /// colour — the gradient changes from pixel to pixel, and that is precisely
        /// why a comparison with a fixed colour would not work.
        /// </summary>
        private static void Measure(string which, byte[] png)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            int width, height;

            using (var inside = new MemoryStream(png))
            using (var image = new Bitmap(inside))
            {
                width = image.Width;
                height = image.Height;

                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        if (!IsPart(image.GetPixel(x, y))) continue;
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
            }

            if (maxX < 0)
            {
                Check("    " + which, false, "no pixel of part: empty frame");
                return;
            }

            bool touches = minX <= Allowance || minY <= Allowance ||
                         maxX >= width - 1 - Allowance ||
                         maxY >= height - 1 - Allowance;

            double fillX = (maxX - minX + 1) / (double)width;
            double fillY = (maxY - minY + 1) / (double)height;
            double fill = Math.Max(fillX, fillY);

            string detail = string.Format(
                "box x {0}-{1} y {2}-{3}, fills {4:0}% x {5:0}%",
                minX, maxX, minY, maxY, fillX * 100, fillY * 100);

            // ── THE TWO CRITERIA ARE NOT OF THE SAME KIND ─────────────────
            //
            // **Cutting is a defect. Wasting is a waste.** A cut
            // part is a different part, and whoever looks at the image reasons on it
            // all the same; a small part inside a big frame is
            // true, only expensive.
            //
            // So the outcome depends ONLY on the cutting. The fill is printed,
            // is seen, and fails nothing — but it is printed **always**, so as
            // not to let it vanish: it is what whoever uses the bridge pays for.
            if (touches)
            {
                Check("    " + which, false, "TAGLIATA -- " + detail);
                return;
            }
            Check("    " + which, true, detail);
            if (fill < MinimumFill)
            {
                _thinOnes++;
                Console.WriteLine("         (thin: fills " +
                                  (fill * 100).ToString("0") + "%, below the " +
                                  (MinimumFill * 100).ToString("0") + "% we would like)");
            }
        }

        /// <summary>
        /// Is this pixel part, or is it background?
        ///
        /// ── TWO WRONG THRESHOLDS BEFORE THIS ONE ─────────────────────────
        ///
        /// The first said «background if R+G+B > 690». The second derived the
        /// threshold from the four corners. Both declared CUT
        /// twelve views out of twelve, **with the same box on parts of different
        /// shape** — and a measurement that gives the same number on different
        /// inputs is not measuring the input.
        ///
        /// What unmasked them was looking at the frame: on a bar that sits
        /// between y 358 and 410, the ruler said y 5-443. It took for part the
        /// bluish zone of the gradient, which is dark by itself and **is not at the
        /// corners** — that is why the second threshold, taken from the corners, did not
        /// see it.
        ///
        /// ── THE ONE THAT IS THERE NOW ─────────────────────────────────────
        ///
        /// The grey of the part sits around 128-150 on all three channels; the
        /// SOLIDWORKS gradient does not go below ~215 on any of them. 200 is in
        /// the middle, and sits wide from both sides.
        ///
        /// **The lowest channel, not the sum:** a medium grey and a light
        /// blue can have the same sum.
        ///
        /// And above all: we do not trust it: `TheRulerSeesWell` proves it on a
        /// frame in which it is **known** that there is not a single piece of part.
        /// </summary>
        private static bool IsPart(Color c)
        {
            return Math.Min(c.R, Math.Min(c.G, c.B)) < 200;
        }

        /// <summary>
        /// THE RED CASE OF THE RULER.
        ///
        /// An empty document, photographed: the frame is all background, and the
        /// count of the part pixels **must be zero**. If it finds some, the threshold
        /// is taking the gradient for part, and every number that this
        /// program prints afterwards is worthless.
        ///
        /// It costs one shot. The two wrong thresholds before this one would have
        /// died here instead of after three runs and two readings by eye.
        /// </summary>
        private static void TheRulerSeesWell(ISldWorks sw)
        {
            IModelDoc2 doc = null;
            try
            {
                string model = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                doc = sw.NewDocument(model, 0, 0, 0) as IModelDoc2;
                if (doc == null) { Check("the ruler sees well", false, "part not created"); return; }

                byte[] png = Fire(doc, "isometric");
                if (png == null) { Check("the ruler sees well", false, "no image"); return; }

                int howMany = 0;
                using (var inside = new MemoryStream(png))
                using (var image = new Bitmap(inside))
                    for (int y = 0; y < image.Height; y++)
                        for (int x = 0; x < image.Width; x++)
                            if (IsPart(image.GetPixel(x, y))) howMany++;

                Check("the ruler sees well", howMany == 0,
                      howMany == 0
                          ? "on an empty document it sees no part"
                          : howMany + " pixels of «part» on an EMPTY document: the threshold lies");
                if (howMany != 0) Deposit("ruler", "empty", png);
            }
            catch (Exception ex)
            {
                Check("the ruler sees well", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (doc != null) { try { sw.CloseDoc(doc.GetTitle()); } catch { } }
            }
        }

        /// <summary>What shape the graphics area has in which SOLIDWORKS frames.</summary>
        private static void AspectRatio(ISldWorks sw)
        {
            IModelDoc2 doc = null;
            try
            {
                string model = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                doc = sw.NewDocument(model, 0, 0, 0) as IModelDoc2;
                var view = doc == null ? null : doc.ActiveView as IModelView;
                if (view == null) { Console.WriteLine("  (graphics area not readable)"); return; }

                double w = view.FrameWidth, h = view.FrameHeight;
                double viewAspect = h > 0 ? w / h : 0;
                double requestedAspect = 1024.0 / 768.0;
                Console.WriteLine(string.Format(
                    "  graphics area {0:0} x {1:0}  ->  {2:0.###}   asked 1024x768 -> {3:0.###}",
                    w, h, viewAspect, requestedAspect));
                Console.WriteLine(string.Format(
                    "  view scale: {0:0.#####}   ratio between the two aspects: {1:0.###}",
                    view.Scale2, viewAspect > 0 ? viewAspect / requestedAspect : 0));
            }
            catch (Exception ex) { Console.WriteLine("  (aspect not readable: " + ex.Message + ")"); }
            finally { if (doc != null) { try { sw.CloseDoc(doc.GetTitle()); } catch { } } }
        }

        /// <summary>The frame of a red check, left to be looked at.</summary>
        private static void Deposit(string shape, string which, byte[] png)
        {
            try
            {
                string folder = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "frames");
                Directory.CreateDirectory(folder);
                string clean = shape.Replace(' ', '_').Replace('x', 'X');
                string file = Path.Combine(folder, clean + "-" + which + ".png");
                File.WriteAllBytes(file, png);
                Console.WriteLine("         look at it: " + file);
            }
            catch { }
        }

        private static void Check(string itemName, bool ok, string detail)
        {
            if (!ok) _failed++;
            Console.WriteLine((ok ? "  OK   " : "  NO   ") + itemName.PadRight(20) + detail);
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
