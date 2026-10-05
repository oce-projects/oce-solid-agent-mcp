using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// AN IMAGE TO TRACE OVER, TO SCALE, IN A SKETCH (entry Z-F5).
    ///
    /// The most frequent input in a small workshop is not a DXF: it is a
    /// photo, a sketch with the dimensions in pen. The model can read
    /// the image; what it lacks is the place to put it, to scale, on a plane.
    ///
    /// Measured on 27/9/2026 (`prova0089 zf5`, a 400x200 px PNG):
    ///
    ///  * `ISketchManager.InsertSketchPicture(file)` in the sketch opened on a
    ///    plane: it enters at 1 px = 1 mm (400 x 200 mm), origin 0,0; the feature is
    ///    «Immagine per schizzo1», type `SketchBitmap`.
    ///  * `SetSize(0,12, 0,06, true)` + `SetOrigin(0,01, 0,02)`: 120 x 60 mm with
    ///    the bottom-left corner at 10,20.
    ///  * **`GetPointOnSketchFromPixel(Row, Column)` HAS ITS ARGUMENTS THE
    ///    OTHER WAY ROUND FROM THE NAME**: the first moves in X (it is the column), the second
    ///    in Y (the row, from the top). (0,0) -> 10,80 (the top-left
    ///    corner); (199,0) -> 69.7,80 — 199 columns by 0.3 mm; (0,399) -> null,
    ///    because 399 exceeds the height. A wrong and plausible number, not
    ///    to be trusted from the name of the argument.
    ///  * A PDF does not go in (null): it must first be converted into an image.
    ///
    /// The scale is VERIFIED: the first and the last pixel of the first row, brought
    /// into the sketch, must be apart (columns - 1) x mm-per-pixel within 1%.
    ///
    /// OVER 2048 PX SOLIDWORKS KEEPS FEWER PIXELS THAN THE FILE (finding M11).
    /// Measured on 28/9/2026 (probe_sketch_picture_4096 and
    /// probe_sketch_picture2): PNGs of 1000 x 500 and 2048 x 1024
    /// keep their size, 3000 x 1500 and 4096 x 2048 come back from
    /// GetPixelmapSize as 2048 x 1024, with InsertSketchPicture and with
    /// InsertSketchPicture2 whether HighestResolution is true or false (in a
    /// part it is ignored). ISketchManager~InsertSketchPicture2.html:
    /// "HighestResolution - True to insert images up to 8192 pixels without
    /// compression, false to compress images to 2048 pixels before insertion";
    /// Remarks: "If the document type is not a drawing, then HighestResolution
    /// defaults to false." Up to library 0.141.0 mm_per_pixel was width / the
    /// pixels of SOLIDWORKS: on the 4096 px file 0.2 instead of 0.1, and the
    /// scale check passed, because it used the same wrong pixel count (the last
    /// column of the file landed at 829 mm instead of 419.5). The pixels a caller
    /// counts are the FILE's, read from its header: mm_per_pixel is per file
    /// pixel, the scale check stays on the pixel map SOLIDWORKS holds, and the
    /// result says when the two differ.
    /// </summary>
    public partial class SwOperations
    {
        private static readonly string[] Images = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

        /// <summary>
        /// The longest side SOLIDWORKS keeps in a part: a file over it comes back
        /// from GetPixelmapSize with this long side (measured, see above).
        /// </summary>
        private const int PictureSideKeptInPart = 2048;

        /// <summary>
        /// How far the size and the origin read back may be from the ones asked,
        /// in mm (Z-C21.8, finding M36). The Z-F5 measure of 27/9/2026 read the
        /// 120 x 60 mm picture at (10, 20) back exactly; the bench case
        /// the_picture_is_to_scale holds the same numbers to a hundredth.
        /// </summary>
        private const double PicturePlacementToleranceMm = 0.01;

        /// <summary>
        /// What is wrong with the placement of a sketch picture, in words, or
        /// null when it is where it was asked (Z-C21.8, finding M36): SetSize or
        /// SetOrigin answering false, a placement that could not be read back, or
        /// a width, height or origin read back more than
        /// <see cref="PicturePlacementToleranceMm"/> away from the one asked. All
        /// in mm. Kept apart from the document so that Oce.Solid.Agent.ContractTests can call
        /// it without SOLIDWORKS.
        /// </summary>
        internal static string PicturePlacementFault(
            bool sizeSet, bool originSet, bool placementRead,
            double askedWidthMm, double askedHeightMm, double askedXMm, double askedYMm,
            double readWidthMm, double readHeightMm, double readXMm, double readYMm)
        {
            var faults = new List<string>();
            if (!sizeSet) faults.Add("SetSize answered false");
            if (!originSet) faults.Add("SetOrigin answered false");
            if (!placementRead)
                faults.Add("the size and the origin could not be read back");
            else
            {
                Func<double, string> mm = v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                if (!(Math.Abs(readWidthMm - askedWidthMm) <= PicturePlacementToleranceMm) ||
                    !(Math.Abs(readHeightMm - askedHeightMm) <= PicturePlacementToleranceMm))
                    faults.Add("the size read back is " + mm(readWidthMm) + " x " + mm(readHeightMm) + " mm instead of " +
                               mm(askedWidthMm) + " x " + mm(askedHeightMm));
                if (!(Math.Abs(readXMm - askedXMm) <= PicturePlacementToleranceMm) ||
                    !(Math.Abs(readYMm - askedYMm) <= PicturePlacementToleranceMm))
                    faults.Add("the lower left corner read back is at (" + mm(readXMm) + ", " + mm(readYMm) + ") mm instead of (" +
                               mm(askedXMm) + ", " + mm(askedYMm) + ")");
            }
            return faults.Count == 0
                ? null
                : "The picture did not go where it was asked: " + string.Join("; ", faults) + ".";
        }

        /// <summary>
        /// The pixel size of the image FILE, from its header: GDI+ with
        /// validateImageData = false reads the size without decoding the
        /// pixels. False when the file cannot be read as an image.
        /// </summary>
        private static bool FilePixelSize(string path, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (System.Drawing.Image image = System.Drawing.Image.FromStream(stream, false, false))
                {
                    width = image.Width;
                    height = image.Height;
                }
            }
            catch { return false; }
            return width > 0 && height > 0;
        }

        private Outcome SketchPicture(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (!(model is IPartDoc))
                return Failed("Metto l'immagine in uno schizzo di un pezzo. Aprimi un pezzo (anche vuoto).");
            string filePath = (Str(a, "path", "") ?? "").Trim();
            if (filePath.Length == 0)
                return Failed("Dimmi «path»: il file dell'immagine da ricalcare (PNG, JPG, BMP, TIF).");
            string est = (Path.GetExtension(filePath) ?? "").ToLowerInvariant();
            if (est == ".pdf")
                return Failed("Un PDF in uno schizzo non entra (misurato: InsertSketchPicture risponde null). " +
                               "Converti la pagina in un'immagine (PNG) e dammi quella.");
            if (Array.IndexOf(Images, est) < 0)
                return Failed("«" + filePath + "» non e' un'immagine che so mettere: PNG, JPG, BMP, GIF o TIF.");
            if (!File.Exists(filePath))
                return Failed("Il file «" + filePath + "» non c'e'.");
            string plane = (Str(a, "plane", "") ?? "").Trim().ToLowerInvariant();
            if (plane != "front" && plane != "top" && plane != "right")
                return Failed("«plane» e' «front», «top» o «right», non «" + plane + "».");
            double width; string error;
            if (!RequiredNum(a, "width_mm", out width, out error)) return Failed(error);
            if (width <= 0)
                return Failed("«width_mm» e' la larghezza VERA dell'immagine in millimetri (una misura nota " +
                               "divisa per i suoi pixel, per tutta la larghezza): deve essere positiva.");
            double x = Num(a, "x_mm", 0), y = Num(a, "y_mm", 0);
            int fileWidth, fileHeight;
            bool fileSizeKnown = FilePixelSize(filePath, out fileWidth, out fileHeight);

            if (!SelectPlane(model, plane))
                return Failed("Non riesco a selezionare il piano " + plane + ".");
            // Z-C21.7 (L8-4): InsertSketchPicture2(file, false), not the obsolete
            // InsertSketchPicture (ISketchManager~InsertSketchPicture.html:
            // «Obsolete. Superseded by ISketchManager::InsertSketchPicture2»).
            // HighestResolution false is what a part gets anyway (the Remarks
            // quoted above). The Z-C21.3 probe probe_sketch_picture2 measured it
            // as a drop-in: on the 400 x 200 PNG, P1 = P0 (pixel map 400 x 200,
            // 120 x 60 mm at (10, 20), first row 119.7 mm); on the 4096 x 2048 PNG
            // the same pixel map of 2048 x 1024 as the obsolete call, which is
            // why the file-pixel readback of M11 below stays as it is.
            ISketchPicture photo = null;
            try
            {
                model.SketchManager.InsertSketch(true);
                photo = model.SketchManager.InsertSketchPicture2(filePath, false) as ISketchPicture;
            }
            catch (Exception ex)
            {
                try { model.SketchManager.InsertSketch(true); } catch { }
                return Failed("L'immagine non e' entrata: " + ex.Message);
            }
            if (photo == null)
            {
                try { model.SketchManager.InsertSketch(true); } catch { }
                return Failed("SOLIDWORKS non ha messo l'immagine (InsertSketchPicture2 ha risposto null): il file " +
                               "c'e' ma forse non e' un'immagine vera. Prova a risalvarla in PNG.");
            }
            int w = 0, h = 0;
            double lw = 0, lh = 0, ox = 0, oy = 0;
            bool measure = false;
            double distance = double.NaN;
            // THE TWO ANSWERS ARE KEPT, AND THE PLACEMENT IS READ BACK (Z-C21.8,
            // finding M36). ISketchPicture~SetSize.html: «True if the size of the
            // picture is set, false if not»; ~SetOrigin.html: «True if the origin
            // is set, false if not». Both answers were dropped, the width was
            // checked within 1% and the origin not at all. PicturePlacementFault
            // below judges all four numbers against what was asked.
            bool sizeSet = false, originSet = false, placementRead = false;
            try
            {
                photo.GetPixelmapSize(ref w, ref h);
                if (w > 0 && h > 0)
                {
                    sizeSet = photo.SetSize(Units.M(width), Units.M(width * h / w), true);
                    originSet = photo.SetOrigin(Units.M(x), Units.M(y));
                    photo.GetSize(ref lw, ref lh);
                    photo.GetOrigin(ref ox, ref oy);
                    placementRead = true;
                    // THE SCALE VERIFIED: first and last column of the first row.
                    // GetPointOnSketchFromPixel wants (column, row), the opposite of
                    // the name of its arguments (measured).
                    var p0 = (photo.GetPointOnSketchFromPixel(0, 0) as IMathPoint)?.ArrayData as double[];
                    var p1 = (photo.GetPointOnSketchFromPixel(w - 1, 0) as IMathPoint)?.ArrayData as double[];
                    if (p0 != null && p1 != null)
                    {
                        distance = Math.Sqrt(Math.Pow(p1[0] - p0[0], 2) + Math.Pow(p1[1] - p0[1], 2) + Math.Pow(p1[2] - p0[2], 2)) * 1000;
                        measure = true;
                    }
                }
            }
            catch { }
            string featureName = null;
            try { featureName = (photo.GetFeature() as IFeature)?.Name; } catch { }
            try { model.SketchManager.InsertSketch(true); } catch { }
            if (w <= 0 || h <= 0)
                return Failed("L'immagine e' nello schizzo ma non ne leggo i pixel: non so darle la scala. Toglila (" +
                               (featureName ?? "l'immagine nello schizzo") + ") e riprova con un PNG.");

            // THE PIXELS THE CALLER COUNTS ARE THE FILE'S (finding M11, see the
            // class comment). SOLIDWORKS may hold fewer, never more, and with the
            // same shape: anything else means the two are not the same picture
            // (a JPEG turned by its EXIF orientation, say), and no scale is given.
            string pictureName = featureName ?? "the picture in the sketch";
            int pixelWidth = w, pixelHeight = h;
            bool compressed = false;
            if (fileSizeKnown)
            {
                if (fileWidth != w || fileHeight != h)
                {
                    bool fewer = w <= fileWidth && h <= fileHeight;
                    bool sameShape = Math.Abs((long)w * fileHeight - (long)h * fileWidth) <= Math.Max(fileWidth, fileHeight);
                    if (!fewer || !sameShape)
                        return Failed("SOLIDWORKS holds the picture as " + w + " x " + h + " pixels and the file is " +
                                       fileWidth + " x " + fileHeight + ": not the same picture shape, so I cannot give " +
                                       "its scale. Remove it (" + pictureName + ") and save the image again as a PNG.");
                    compressed = true;
                }
                pixelWidth = fileWidth;
                pixelHeight = fileHeight;
            }
            else if (Math.Max(w, h) >= PictureSideKeptInPart)
                return Failed("I cannot read the pixel size of the file «" + filePath + "», and SOLIDWORKS holds the picture " +
                               "at " + w + " x " + h + " pixels: at " + PictureSideKeptInPart + " px it may have compressed " +
                               "it, and a scale per pixel would be wrong by the factor I cannot see. Remove it (" +
                               pictureName + ") and save the image again as a PNG.");

            double mmPerPixel = width / pixelWidth;
            double readWidth = lw * 1000, heightRead = lh * 1000;
            string placementFault = PicturePlacementFault(
                sizeSet, originSet, placementRead,
                width, width * h / w, x, y,
                readWidth, heightRead, ox * 1000, oy * 1000);
            if (placementFault != null)
                return Failed(placementFault + " Remove it (" + pictureName + ") and try again.");
            // The scale check is on the pixel map SOLIDWORKS holds: those are the
            // pixels GetPointOnSketchFromPixel takes.
            double expectation = (w - 1) * (width / w);
            if (!measure || Math.Abs(distance - expectation) > expectation * 0.01)
                return Failed("L'immagine e' larga " + readWidth.ToString("0.###") + " mm, ma il primo e l'ultimo pixel " +
                               "della prima riga, nello schizzo, distano " + (measure ? distance.ToString("0.###") : "?") +
                               " mm invece di " + expectation.ToString("0.###") + ": la scala non e' quella che dice.");
            var measurements = new Dictionary<string, object>
            {
                ["picture"] = featureName,
                ["plane"] = plane,
                ["pixel"] = new[] { pixelWidth, pixelHeight },
                ["solidworks_pixels"] = new[] { w, h },
                ["width_mm"] = Math.Round(readWidth, 4),
                ["height_mm"] = Math.Round(heightRead, 4),
                ["mm_per_pixel"] = Math.Round(mmPerPixel, 6),
                ["origin_mm"] = new[] { Math.Round(ox * 1000, 4), Math.Round(oy * 1000, 4) },
                ["check_scale_mm"] = Math.Round(distance, 4),
                ["how_to_read_it"] =
                    "In the plane of the sketch the lower left corner of the picture is at «origin_mm». The pixel " +
                    "of the FILE in column c and row r (counted from 0, rows from the top) is at x = origin_mm[0] + " +
                    "c * mm_per_pixel, y = origin_mm[1] + (" + pixelHeight + " - r) * mm_per_pixel. Trace it from there " +
                    "with the sketch operations on the same plane.",
            };
            if (compressed)
                measurements["compression"] =
                    "SOLIDWORKS keeps this picture at " + w + " x " + h + " pixels: it compresses a picture over " +
                    PictureSideKeptInPart + " px when it inserts it in a part (ISketchManager~InsertSketchPicture2.html). " +
                    "The file is " + fileWidth + " x " + fileHeight + ": «pixel» and «mm_per_pixel» are the FILE's, " +
                    "which is what you count in the image; the picture in the sketch has less detail than the file.";
            _log("[agente] picture " + pixelWidth + "x" + pixelHeight + " px (SOLIDWORKS " + w + "x" + h + ") at " +
                 mmPerPixel.ToString("0.####") + " mm/px on the plane " + plane);
            return SucceededWithoutCreating(measurements);
        }
    }
}
