using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// A PHOTOGRAPH OF THE PART, for whoever has no eyes.
    ///
    /// WHY IT EXISTS. Until 0.31.0 the model did not see the part and had
    /// never seen it: it received a list of sixty faces with area,
    /// normal and direction. For a drilled plate it is enough; to understand what SHAPE
    /// a shell with fins has, it is not — sixty faces with an area are not a
    /// shape, they are a list.
    ///
    /// The capability was there and was proven: the bench saves four views out of
    /// four with `SaveBMP`. But it lived in the test bench and never reached
    /// whoever reads. It is the same shape as the defect of entry E8 — measured and
    /// thrown away — with the aggravating factor that here it had never been connected.
    ///
    /// ── TWO RULES THAT ARE NOT NEGOTIABLE ─────────────────────────────────
    ///
    /// **THE VIEW OF WHOEVER IS WORKING IS NOT TOUCHED.** This bridge attaches to the
    /// document that a designer has in front of them, on their screen. Turning the
    /// part to photograph it and leaving it turned means moving their stuff
    /// from under their hands while they work. The orientation is saved first and put back
    /// afterwards, **even when the photograph fails**.
    ///
    /// **ON REQUEST, NEVER BY DEFAULT.** A 1024x768 image costs more than a thousand
    /// tokens: more than ten times the whole state of the document, which fits under
    /// 16,000 CHARACTERS. If every `inspect_part` carried three views, the cost
    /// of the bridge would change by an order of magnitude — and it would be paid also by
    /// whoever only wanted to know how many bodies there are.
    ///
    /// ── WHY PNG AND NOT THE BMP THAT SOLIDWORKS GIVES ─────────────────────
    ///
    /// `SaveBMP` writes an uncompressed BMP: 1024x768 is **2.3 MB**, which in
    /// base64 becomes 3. The same frame in PNG fits in a few tens of
    /// KB, and the pixels are identical — a BMP sent as it is would be
    /// fifty times the weight for the same image.
    /// </summary>
    public static class PartView
    {
        /// <summary>
        /// The views that can be asked for, with the name used by whoever draws.
        ///
        /// The isometric is first because it is the only one that, alone, tells a
        /// shape: the three standard ones tell three silhouettes and leave the
        /// rest to be guessed.
        /// </summary>
        private static readonly Dictionary<string, int> Views =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["isometric"] = (int)swStandardViews_e.swIsometricView,
                ["trimetric"] = (int)swStandardViews_e.swTrimetricView,
                ["dimetric"] = (int)swStandardViews_e.swDimetricView,
                ["front"] = (int)swStandardViews_e.swFrontView,
                ["back"] = (int)swStandardViews_e.swBackView,
                ["left"] = (int)swStandardViews_e.swLeftView,
                ["right"] = (int)swStandardViews_e.swRightView,
                ["top"] = (int)swStandardViews_e.swTopView,
                ["bottom"] = (int)swStandardViews_e.swBottomView,
            };

        public static IEnumerable<string> ViewNames { get { return Views.Keys; } }

        /// <summary>Does a view exist with this name?</summary>
        public static bool Conosciuta(string itemName)
        {
            return !string.IsNullOrWhiteSpace(itemName) && Views.ContainsKey(itemName);
        }

        /// <summary>
        /// Photographs the document and returns the bytes of a PNG.
        /// Null with the reason if it could not be done.
        ///
        /// <param name="which">
        /// The name of a view, or null to photograph the orientation
        /// that is there now — which is the only way to touch NOTHING.
        /// </param>
        /// </summary>
        public static byte[] Fire(
            IModelDoc2 model, string which, int width, int height, out string reason)
        {
            reason = null;
            if (model == null) { reason = "Nessun documento aperto."; return null; }

            if (which != null && !Conosciuta(which))
            {
                reason = "Non conosco la vista «" + which + "». Ci sono: " +
                         string.Join(", ", ViewNames) + ".";
                return null;
            }

            // THE ORIENTATION OF NOW, TO PUT IT BACK AFTERWARDS.
            // It is taken BEFORE touching anything, and put back in the
            // `finally`: if the photograph fails halfway, the part stays
            // turned on the screen of whoever was working, and that is a
            // real damage done by a read-only operation.
            //
            // ── AND `Transform` IS NOT ENOUGH: THE VIEW IS A TRIPLE ──────
            //
            // Three attempts, and the first two were rejected by the bench:
            //
            //   1. keeping `IModelView.Transform` and reassigning it. **It puts
            //      nothing back**, measured.
            //   2. `IModelDoc2.ViewOrientationUndo()`, which looks made
            //      for it. **Not that one either**, measured.
            //   3. the triple `Orientation3` + `Translation3` + `Scale2`.
            //
            // The first explanation I had given myself for the red — «`Transform`
            // returns the live object, so saving it saves nothing» — was
            // **false**, and it was refuted by a measurement written on purpose: the sixteen
            // numbers of the same reference do NOT change after
            // `ShowNamedView2`. It is a copy, and a useless copy.
            //
            // `Transform` DESCRIBES how the part is seen; it is not the state
            // the view is rebuilt from. That is the triple: where you look from
            // (`Orientation3`), where you are centred (`Translation3`), how close you
            // are (`Scale2`). `ViewZoomtofit2` changes the last two, and
            // putting back the orientation alone would leave the part right but
            // framed somewhere else.
            //
            // > The direction of a repair is not worked out by reasoning
            // > about the workshop: the part is asked. Here the part said no
            // > twice.
            MathTransform orientationBefore = null;
            MathVector translationBefore = null;
            double scaleBefore = 0;
            IModelView view = null;
            try
            {
                view = model.ActiveView as IModelView;
                if (view != null)
                {
                    // COPIES, NOT THE REFERENCES THAT THE VIEW GIVES.
                    //
                    // `Transform` is a copy — measured. Of `Orientation3` and
                    // `Translation3` nothing is known, and the third attempt at
                    // putting back failed **exactly as a live
                    // object would**: keep it, turn the view, reassign it, and
                    // you have reassigned the new orientation.
                    //
                    // It is not assumed: it is copied anyway. `IInverse()` twice
                    // gives a new `MathTransform` with the same matrix;
                    // `IScale(1.0)` gives a new `MathVector` with the same
                    // components. They cost two COM calls and remove a
                    // question from the way.
                    var o = view.Orientation3 as IMathTransform;
                    var reverse = o == null ? null : o.IInverse() as IMathTransform;
                    orientationBefore = reverse == null ? null : reverse.IInverse();

                    var t = view.Translation3 as IMathVector;
                    translationBefore = t == null ? null : t.IScale(1.0);

                    scaleBefore = view.Scale2;
                }
            }
            catch { }

            string temporary = null;
            try
            {
                if (which != null)
                {
                    int code = Views[which];

                    // `ShowNamedView2` DOES NOT ANSWER: it returns `void`. There is
                    // no way to ask it whether it turned the part, and the
                    // first writing of this method treated it as if
                    // it returned a `bool` — a check that could not
                    // fire, written for a value that does not exist.
                    //
                    // The only proof that remains is the frame: if the
                    // orientation did not change, the photograph comes out
                    // all the same, only from somewhere else. The name of the view
                    // is already validated above against `Views`, so the
                    // case «wrong name» is excluded before getting there.
                    model.ShowNamedView2("", code);

                    // THE ZOOM GOES AFTER THE REDRAW, AND IT IS REDRAWN AGAIN.
                    // Measured in the bench: on the first pass the part came out cut off
                    // on the right edge, because the fit worked on the
                    // old view.
                    model.GraphicsRedraw2();
                    model.ViewZoomtofit2();
                    model.GraphicsRedraw2();

                    // ── AND THEN IT IS TIGHTENED ────────────────────────
                    //
                    // `ViewZoomtofit2` leaves a wide margin: measured, the
                    // part occupied **half** the width of the frame.
                    // Half the pixels of an image that costs more than a thousand
                    // tokens show nothing.
                    //
                    // The factor is cautious on purpose. The criterion of entry V5
                    // is asymmetric: **cutting is a defect, wasting is a
                    // waste**, and between the two there is no contest. A number that
                    // fills more but now and then cuts would be worse than
                    // now.
                    try { NarrowIfYouCan(view); model.GraphicsRedraw2(); } catch { }
                }

                // `SaveBMP` wants a path: it cannot return bytes.
                temporary = Path.Combine(
                    Path.GetTempPath(), "oce-vista-" + Guid.NewGuid().ToString("N") + ".bmp");

                // ── THE FRAME TAKES THE SHAPE OF THE GRAPHICS AREA ─────
                //
                // **The defect (entry V5).** `ViewZoomtofit2` frames inside
                // the graphics area of SOLIDWORKS; `SaveBMP` then renders into the
                // frame it is asked for. If the two shapes do not
                // coincide, what is left over **is cut away**, and
                // nobody says so.
                //
                // Measured on 15/9/2026 by `Checks\FramingTest`:
                //
                //     graphics area  1845 x 924  ->  1.997
                //     asked for      1024 x 768  ->  1.333
                //
                // On seven views out of twelve the part came out of the RIGHT edge —
                // always on the right, never vertically. Two models had
                // noticed it by themselves during the V2 measurement: «the third dimension
                // cannot be determined because it comes out of the frame».
                //
                // **A cut part is a different part**, and an image that
                // hides half of it is worse than no image: whoever looks
                // at it reasons on it all the same.
                //
                // So the frame no longer imposes its shape: it takes
                // that of the graphics area, within the maximum sizes asked for.
                // With 1024x768 asked for and a 2:1 area, 1024x513 comes out — smaller,
                // and **whole**. The width and height passed
                // stay a CEILING, not a demand.
                int lw = width, lh = height;
                FrameView(view, width, height, out lw, out lh);

                if (!model.SaveBMP(temporary, lw, lh) || !File.Exists(temporary))
                {
                    reason = "SOLIDWORKS non ha scritto l'immagine.";
                    return null;
                }

                return InPng(temporary, out reason);
            }
            catch (Exception ex)
            {
                reason = "Non sono riuscito a fotografare il pezzo: " + ex.Message;
                return null;
            }
            finally
            {
                // ── ALL THREE, AND THE ORDER IS ORIENTATION, SCALE, POSITION
                //
                // **The order is not a style: it is the difference between putting back and
                // not putting back.** Measured by `Checks\ViewTest`, which tries
                // nine routes in three seconds:
                //
                //   reassign Transform                    deviation 2598 on index 12 (the scale)
                //   Transform copied                      same
                //   ViewOrientationUndo()                 deviation 212 on index 9
                //   triple, order O -> T -> S             deviation 4.77 on index 9
                //   triple, order O -> S -> T             **comes back**
                //
                // With the wrong order the ROTATION comes back exact (indices 0-8
                // of the matrix) and only the translation is wrong: `Translation3`
                // holds for the scale that is there **at that moment**, so
                // putting it back before `Scale2` means putting back a position that
                // is good for another framing. The part came out straight and
                // shifted — which is worse than coming out crooked, because it
                // looks right.
                //
                // Each one in its own `try`: if one of the three cannot be put back,
                // the others must be put back all the same.
                if (view != null)
                {
                    try { if (orientationBefore != null) view.Orientation3 = orientationBefore; }
                    catch { }
                    try { if (scaleBefore > 0) view.Scale2 = scaleBefore; }
                    catch { }
                    try { if (translationBefore != null) view.Translation3 = translationBefore; }
                    catch { }
                    try { model.GraphicsRedraw2(); } catch { }
                }
                if (temporary != null)
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                }
            }
        }

        /// <summary>
        /// THE CUTAWAY: the part cut by a plane, photographed (entry V4).
        ///
        /// ── WHY A SECTION AND NOT ANOTHER VIEW ────────────────────────────
        ///
        /// A cutaway tells more about a shell than ten external views: it is
        /// exactly the gesture a designer makes to understand a thickness.
        /// From the outside a hollowed-out box and a solid box are the same
        /// parallelepiped.
        ///
        /// ── IT DOES NOT TOUCH THE PART, AND IT REALLY DOES NOT ────────────
        ///
        /// `GraphicsOnlySection = true`: it cuts **the drawing on screen**, not the
        /// geometry. No feature in the tree, no body split, nothing
        /// to undo. It is the difference between looking and modifying, and this
        /// bridge stands on the side of looking.
        ///
        /// And the section **is removed in the `finally`**, together with the orientation:
        /// leaving it on would change what the designer sees on their
        /// screen, which is the risk written in the entry.
        ///
        /// ── AND IT DOES NOT GO THROUGH `RunCommand` ──────────────────────
        ///
        /// The menu command «Section View» opens a panel and stays there
        /// waiting: a COM call that opens a modal **does not return** — the
        /// trap of `AddDimension`, already paid for once.
        /// `IModelViewManager.CreateSectionView` answers a bool and asks
        /// nobody for anything.
        /// </summary>
        public static byte[] CaptureInSection(
            IModelDoc2 model, string which, string plane, double offsetMm,
            int width, int height, out string reason)
        {
            reason = null;
            if (model == null) { reason = "Nessun documento aperto."; return null; }

            var handler = model.ModelViewManager;
            if (handler == null)
            {
                reason = "Questo documento non sa fare le sezioni.";
                return null;
            }

            // AN ASSEMBLY OR A DRAWING IS SAID AS SUCH (Z-C21.8, finding N21).
            // ReferencePlane walks the features of a PART (IPartDoc), and on
            // any other document it found no plane: the refusal then said «I
            // cannot find the front plane; there are: front, top, right», and
            // contradicted itself. The document is named instead.
            if (!(model is IPartDoc))
            {
                int kind = -1;
                try { kind = model.GetType(); } catch { kind = -1; }
                reason = "The section is taken on a part, and the document in front is " +
                         (kind == (int)swDocumentTypes_e.swDocASSEMBLY ? "an assembly"
                          : kind == (int)swDocumentTypes_e.swDocDRAWING ? "a drawing" : "not a part") +
                         ": open the part and ask again.";
                return null;
            }

            var onWhich = ReferencePlane(model, plane, out string planeProblem);
            if (onWhich == null)
            {
                // A name that is one of the three, and still no plane: no
                // reference plane of the part has that plane's normal through
                // the origin (Z-C21.5, scheme S19), and the reason says what was
                // read (N21: never the self-contradiction of a list).
                reason = Array.IndexOf(References.BasePlaneNames, plane) >= 0
                    ? "I cannot find the «" + plane + "» plane: " + planeProblem
                    : "Non trovo il piano «" + plane + "». Ci sono: " +
                      "frontale, superiore, destro.";
                return null;
            }

            bool sectionOn = false;
            try
            {
                var data = handler.CreateSectionViewData();
                if (data == null)
                {
                    reason = "SOLIDWORKS non ha preparato la sezione.";
                    return null;
                }
                data.FirstPlane = onWhich;
                data.FirstOffset = Units.M(offsetMm);
                // GRAPHICS ONLY: it is the line that makes this operation
                // read-only. Without it, SOLIDWORKS really cuts the model.
                data.GraphicsOnlySection = true;
                data.Redraw = true;

                sectionOn = handler.CreateSectionView(data);
                if (!sectionOn)
                {
                    reason = "SOLIDWORKS non ha aperto la sezione sul piano «" +
                             plane + "».";
                    return null;
                }

                return Fire(model, which, width, height, out reason);
            }
            catch (Exception ex)
            {
                reason = "Non sono riuscito a sezionare il pezzo: " + ex.Message;
                return null;
            }
            finally
            {
                // IT IS ALWAYS REMOVED, even if the photograph failed halfway.
                // A section left on the screen of whoever is working is the
                // same damage as the orientation left turned, and for the
                // same reason: it was done by an operation that was only supposed to
                // look.
                if (sectionOn)
                {
                    try { handler.RemoveSectionView(); } catch { }
                    try { model.GraphicsRedraw2(); } catch { }
                }
            }
        }

        /// <summary>
        /// One of the three base planes, by name. **An unknown name is null**,
        /// not the front plane: a silent fallback here would section on a plane
        /// other than the one asked and still call it «the section». It is the
        /// lesson of `FindBasePlane`, where a final `: 1` sent any name to the
        /// front plane.
        ///
        /// BY GEOMETRY SINCE Z-C21.5 (scheme S19, finding C3-6). Until then the
        /// RefPlanes were counted in tree order and the first three were front,
        /// top and right. IPartDoc~IFirstFeature.html warns that the order of
        /// the features changes when the part is edited, and the same page
        /// forbids trusting the names («Plane1»). The Z-C21.3 probe of C3-6
        /// (28/9/2026, SOLIDWORKS 29.5.1) found the position rule and the
        /// geometry rule on the SAME plane all nine times on the template as it
        /// comes, because every ReorderFeature that tried to put a plane or a
        /// sketch before the three base planes answered false: only a part
        /// TEMPLATE built with an extra plane first breaks the position rule.
        /// The plane is now the one with the canonical normal through the
        /// origin (References.BasePlane); <paramref name="reason"/> says why
        /// when there is none.
        /// </summary>
        private static IFeature ReferencePlane(IModelDoc2 model, string which, out string reason)
        {
            reason = "this document is not a part.";
            if (!(model is IPartDoc)) return null;
            return References.BasePlane(model, which, out reason);
        }

        /// <summary>The names of the planes on which one can section.</summary>
        public static IEnumerable<string> PlaneNames
        {
            get { return new[] { "front", "top", "right" }; }
        }

        /// <summary>
        /// By how much it is tightened after the automatic fit.
        ///
        /// **1.15, and the ceiling is dictated by the direction ALREADY FULL.** After
        /// the fit the part filled ~50% in width but already **78% in
        /// height**: the margin that looks wasted when looking at the width is not
        /// there vertically. 1.35 was tried and measured — the L bracket and the
        /// plate came out of the TOP and BOTTOM edges (box y 0-512 out of
        /// 513), that is width was gained by cutting height.
        ///
        /// 0.78 x 1.15 = 0.90: a tenth of margin remains for the antialiasing and
        /// for the parts that the fit frames a little tighter.
        ///
        /// **A uniform zoom cannot do better**, because the two margins
        /// are different and the factor is a single one. Framing really well
        /// means computing the extent of the part PROJECTED into the view and
        /// fitting the two directions on their own: it is another entry, not
        /// a constant to raise.
        ///
        /// It is changed only by measuring again with `Checks\FramingTest`, which
        /// costs seconds.
        /// </summary>
        private const double HowMuchItNarrows = 1.15;

        private static void NarrowIfYouCan(IModelView view)
        {
            if (view == null) return;
            view.ZoomByFactor(HowMuchItNarrows);
        }

        /// <summary>
        /// The largest frame that fits inside the sizes asked for AND has the
        /// same shape as the graphics area.
        ///
        /// If the graphics area cannot be read, the sizes asked for are kept:
        /// it is the behaviour from before, which cuts — but **cutting is
        /// better than not shooting**, and the case has never been seen.
        /// </summary>
        private static void FrameView(
            IModelView view, int width, int height, out int lw, out int lh)
        {
            lw = width;
            lh = height;
            try
            {
                if (view == null) return;
                double w = view.FrameWidth, h = view.FrameHeight;
                if (w <= 0 || h <= 0) return;

                double shape = w / h;
                // It starts from the full width and derives the height; if
                // the height overflows, the opposite is done. That way the result always touches
                // one of the two ceilings and is never smaller than
                // necessary.
                int testHeight = (int)Math.Round(width / shape);
                if (testHeight <= height && testHeight > 0)
                {
                    lw = width;
                    lh = testHeight;
                    return;
                }
                int testWidth = (int)Math.Round(height * shape);
                if (testWidth > 0)
                {
                    lw = testWidth;
                    lh = height;
                }
            }
            catch { }
        }

        /// <summary>
        /// The SOLIDWORKS BMP becomes a PNG. Null with the reason if it cannot.
        ///
        /// The temporary file is read ENTIRELY into memory before closing, and the
        /// `Bitmap` is built from a `MemoryStream`: building it from the file
        /// keeps it open as long as the Bitmap lives, and the deletion in the
        /// `finally` would fail leaving garbage in the temporary folder
        /// at every shot.
        /// </summary>
        /// <summary>
        /// How much background is left around the part, when cropping.
        ///
        /// 4% of the longer side, and **never less than twelve pixels**. The minimum
        /// is not a whim: the `FramingTest` check considers «cut»
        /// a part that comes within three pixels of the edge, and a margin narrower
        /// than its allowance would make the check fail on a
        /// perfect image.
        /// </summary>
        private const double CutoutMargin = 0.04;
        private const int MinimumMargin = 12;

        /// <summary>
        /// The frame **cropped around the part** (entry V6). Null when
        /// it is not worthwhile or not possible, and then the whole one is kept.
        ///
        /// ── WHY IT IS CROPPED INSTEAD OF ZOOMED ───────────────────────────
        ///
        /// After the fit, measured, the part filled **58% in
        /// width and 89% in height**. It looks like a framing to tighten,
        /// and it is not: **the vertical is the constraint**. `ViewZoomtofit2` scales
        /// until the part fits in both directions, and the one that
        /// touches first ends at ~90%; the other stays behind by as much as the
        /// shape of the part differs from the shape of the frame.
        ///
        /// A zoom cannot repair this, because it acts on the two
        /// directions together: tried with 1.35 and the part came out of the top and bottom
        /// edge to gain width. **The part is not fitted
        /// to the frame: the frame is fitted to the part.**
        ///
        /// ── AND IT CANNOT CUT THE PART, BY CONSTRUCTION ───────────────────
        ///
        /// The crop is made on the box of the pixels that are NOT background, plus
        /// a margin. Cutting away a piece of the part would mean that those
        /// pixels were not part — that is that the threshold lies, and it is the same
        /// threshold that `FramingTest` tests on an empty document.
        ///
        /// It also costs LESS: fewer pixels, hence fewer tokens, for more part.
        /// </summary>
        private static Bitmap AroundPart(Bitmap whole)
        {
            try
            {
                int width = whole.Width, height = whole.Height;
                int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;

                // LockBits AND ONE ROW AT A TIME, not GetPixel: on 1024x513 that is
                // half a million reads, and `GetPixel` pays a call
                // for each.
                //
                // And `Marshal.Copy` instead of an `unsafe` block: turning on
                // `AllowUnsafeBlocks` changes the way **the whole library** is compiled,
                // and it is a bigger commitment than a crop is
                // worth. The copy per row costs a single allocation and the
                // same speed.
                var data = whole.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    var line = new byte[Math.Abs(data.Stride)];
                    for (int y = 0; y < height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(
                            data.Scan0 + y * data.Stride, line, 0, line.Length);
                        for (int x = 0; x < width; x++)
                        {
                            // BACKGROUND = LIGHT ON ALL THREE CHANNELS.
                            // The grey of the part sits around 128-150, the gradient
                            // of SOLIDWORKS does not go below ~215. The channels are looked at and
                            // not the sum: a medium grey and a
                            // light blue can have the same sum.
                            int i = x * 3;
                            if (line[i] >= 200 && line[i + 1] >= 200 && line[i + 2] >= 200)
                                continue;
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            maxY = y;
                        }
                    }
                }
                finally { whole.UnlockBits(data); }

                if (maxX < 0) return null;   // empty frame: it is kept whole

                int margin = Math.Max(
                    MinimumMargin,
                    (int)Math.Round(Math.Max(maxX - minX, maxY - minY) * CutoutMargin));

                int x0 = Math.Max(0, minX - margin);
                int y0 = Math.Max(0, minY - margin);
                int x1 = Math.Min(width - 1, maxX + margin);
                int y1 = Math.Min(height - 1, maxY + margin);
                int w = x1 - x0 + 1, h = y1 - y0 + 1;
                if (w <= 0 || h <= 0) return null;

                // IF IT DOES NOT GAIN, IT IS NOT DONE. Cropping 2% costs a copy
                // of the frame to change nothing, and in exchange takes away
                // margin from whoever looks.
                if (w > width * 0.95 && h > height * 0.95) return null;

                return whole.Clone(new Rectangle(x0, y0, w, h), whole.PixelFormat);
            }
            catch { return null; }
        }

        private static byte[] InPng(string bmp, out string reason)
        {
            reason = null;
            try
            {
                byte[] rawItems = File.ReadAllBytes(bmp);
                using (var inside = new MemoryStream(rawItems))
                using (var whole = new Bitmap(inside))
                using (var outside = new MemoryStream())
                {
                    using (var narrow = AroundPart(whole))
                        (narrow ?? whole).Save(outside, ImageFormat.Png);
                    return outside.ToArray();
                }
            }
            catch (Exception ex)
            {
                reason = "L'immagine non si e' lasciata convertire: " + ex.Message;
                return null;
            }
        }
    }
}
