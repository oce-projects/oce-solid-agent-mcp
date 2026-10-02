using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;

namespace Riscontro.Disegno
{
    /// <summary>
    /// UN'IMMAGINE DA RICALCARE, IN SCALA, IN UNO SCHIZZO (voce Z-F5).
    ///
    /// L'ingresso piu' frequente in un'officina piccola non e' un DXF: e' una
    /// foto, uno schizzo con le quote a penna. Il modello sa leggere
    /// l'immagine; gli manca il posto dove metterla, in scala, su un piano.
    ///
    /// Misurato il 27/9/2026 (`prova0089 zf5`, un PNG 400x200 px):
    ///
    ///  * `ISketchManager.InsertSketchPicture(file)` nello schizzo aperto su un
    ///    piano: entra a 1 px = 1 mm (400 x 200 mm), origine 0,0; la feature e'
    ///    «Immagine per schizzo1», tipo `SketchBitmap`.
    ///  * `SetSize(0,12, 0,06, true)` + `SetOrigin(0,01, 0,02)`: 120 x 60 mm con
    ///    l'angolo in basso a sinistra in 10,20.
    ///  * **`GetPointOnSketchFromPixel(Row, Column)` HA GLI ARGOMENTI AL
    ///    CONTRARIO DEL NOME**: il primo sposta in X (e' la colonna), il secondo
    ///    in Y (la riga, dall'alto). (0,0) -> 10,80 (l'angolo in alto a
    ///    sinistra); (199,0) -> 69,7,80 — 199 colonne per 0,3 mm; (0,399) -> null,
    ///    perche' 399 supera l'altezza. Un numero sbagliato e plausibile, da
    ///    non credere al nome dell'argomento.
    ///  * Un PDF non entra (null): va prima convertito in un'immagine.
    ///
    /// La scala si VERIFICA: il primo e l'ultimo pixel della prima riga, portati
    /// nello schizzo, devono distare (colonne - 1) x mm-per-pixel entro l'1%.
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
    public partial class OperazioniSw
    {
        private static readonly string[] Immagini = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

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
        /// in mm. Kept apart from the document so that Prove.Contratti can call
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

        private Esito ImmagineNelloSchizzo(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            if (!(modello is IPartDoc))
                return Fallito("Metto l'immagine in uno schizzo di un pezzo. Aprimi un pezzo (anche vuoto).");
            string percorso = (Str(a, "path", "") ?? "").Trim();
            if (percorso.Length == 0)
                return Fallito("Dimmi «path»: il file dell'immagine da ricalcare (PNG, JPG, BMP, TIF).");
            string est = (Path.GetExtension(percorso) ?? "").ToLowerInvariant();
            if (est == ".pdf")
                return Fallito("Un PDF in uno schizzo non entra (misurato: InsertSketchPicture risponde null). " +
                               "Converti la pagina in un'immagine (PNG) e dammi quella.");
            if (Array.IndexOf(Immagini, est) < 0)
                return Fallito("«" + percorso + "» non e' un'immagine che so mettere: PNG, JPG, BMP, GIF o TIF.");
            if (!File.Exists(percorso))
                return Fallito("Il file «" + percorso + "» non c'e'.");
            string piano = (Str(a, "plane", "") ?? "").Trim().ToLowerInvariant();
            if (piano != "front" && piano != "top" && piano != "right")
                return Fallito("«plane» e' «front», «top» o «right», non «" + piano + "».");
            double larghezza; string errore;
            if (!NumObbligatorio(a, "width_mm", out larghezza, out errore)) return Fallito(errore);
            if (larghezza <= 0)
                return Fallito("«width_mm» e' la larghezza VERA dell'immagine in millimetri (una misura nota " +
                               "divisa per i suoi pixel, per tutta la larghezza): deve essere positiva.");
            double x = Num(a, "x_mm", 0), y = Num(a, "y_mm", 0);
            int fileWidth, fileHeight;
            bool fileSizeKnown = FilePixelSize(percorso, out fileWidth, out fileHeight);

            if (!SelezionaPiano(modello, piano))
                return Fallito("Non riesco a selezionare il piano " + piano + ".");
            // Z-C21.7 (L8-4): InsertSketchPicture2(file, false), not the obsolete
            // InsertSketchPicture (ISketchManager~InsertSketchPicture.html:
            // «Obsolete. Superseded by ISketchManager::InsertSketchPicture2»).
            // HighestResolution false is what a part gets anyway (the Remarks
            // quoted above). The Z-C21.3 probe probe_sketch_picture2 measured it
            // as a drop-in: on the 400 x 200 PNG, P1 = P0 (pixel map 400 x 200,
            // 120 x 60 mm at (10, 20), first row 119.7 mm); on the 4096 x 2048 PNG
            // the same pixel map of 2048 x 1024 as the obsolete call, which is
            // why the file-pixel readback of M11 below stays as it is.
            ISketchPicture foto = null;
            try
            {
                modello.SketchManager.InsertSketch(true);
                foto = modello.SketchManager.InsertSketchPicture2(percorso, false) as ISketchPicture;
            }
            catch (Exception ex)
            {
                try { modello.SketchManager.InsertSketch(true); } catch { }
                return Fallito("L'immagine non e' entrata: " + ex.Message);
            }
            if (foto == null)
            {
                try { modello.SketchManager.InsertSketch(true); } catch { }
                return Fallito("SOLIDWORKS non ha messo l'immagine (InsertSketchPicture2 ha risposto null): il file " +
                               "c'e' ma forse non e' un'immagine vera. Prova a risalvarla in PNG.");
            }
            int w = 0, h = 0;
            double lw = 0, lh = 0, ox = 0, oy = 0;
            bool misura = false;
            double distanza = double.NaN;
            // THE TWO ANSWERS ARE KEPT, AND THE PLACEMENT IS READ BACK (Z-C21.8,
            // finding M36). ISketchPicture~SetSize.html: «True if the size of the
            // picture is set, false if not»; ~SetOrigin.html: «True if the origin
            // is set, false if not». Both answers were dropped, the width was
            // checked within 1% and the origin not at all. PicturePlacementFault
            // below judges all four numbers against what was asked.
            bool sizeSet = false, originSet = false, placementRead = false;
            try
            {
                foto.GetPixelmapSize(ref w, ref h);
                if (w > 0 && h > 0)
                {
                    sizeSet = foto.SetSize(Unita.M(larghezza), Unita.M(larghezza * h / w), true);
                    originSet = foto.SetOrigin(Unita.M(x), Unita.M(y));
                    foto.GetSize(ref lw, ref lh);
                    foto.GetOrigin(ref ox, ref oy);
                    placementRead = true;
                    // LA SCALA VERIFICATA: prima e ultima colonna della prima riga.
                    // GetPointOnSketchFromPixel vuole (colonna, riga), al contrario
                    // del nome dei suoi argomenti (misurato).
                    var p0 = (foto.GetPointOnSketchFromPixel(0, 0) as IMathPoint)?.ArrayData as double[];
                    var p1 = (foto.GetPointOnSketchFromPixel(w - 1, 0) as IMathPoint)?.ArrayData as double[];
                    if (p0 != null && p1 != null)
                    {
                        distanza = Math.Sqrt(Math.Pow(p1[0] - p0[0], 2) + Math.Pow(p1[1] - p0[1], 2) + Math.Pow(p1[2] - p0[2], 2)) * 1000;
                        misura = true;
                    }
                }
            }
            catch { }
            string nomeFeature = null;
            try { nomeFeature = (foto.GetFeature() as IFeature)?.Name; } catch { }
            try { modello.SketchManager.InsertSketch(true); } catch { }
            if (w <= 0 || h <= 0)
                return Fallito("L'immagine e' nello schizzo ma non ne leggo i pixel: non so darle la scala. Toglila (" +
                               (nomeFeature ?? "l'immagine nello schizzo") + ") e riprova con un PNG.");

            // THE PIXELS THE CALLER COUNTS ARE THE FILE'S (finding M11, see the
            // class comment). SOLIDWORKS may hold fewer, never more, and with the
            // same shape: anything else means the two are not the same picture
            // (a JPEG turned by its EXIF orientation, say), and no scale is given.
            string pictureName = nomeFeature ?? "the picture in the sketch";
            int pixelWidth = w, pixelHeight = h;
            bool compressed = false;
            if (fileSizeKnown)
            {
                if (fileWidth != w || fileHeight != h)
                {
                    bool fewer = w <= fileWidth && h <= fileHeight;
                    bool sameShape = Math.Abs((long)w * fileHeight - (long)h * fileWidth) <= Math.Max(fileWidth, fileHeight);
                    if (!fewer || !sameShape)
                        return Fallito("SOLIDWORKS holds the picture as " + w + " x " + h + " pixels and the file is " +
                                       fileWidth + " x " + fileHeight + ": not the same picture shape, so I cannot give " +
                                       "its scale. Remove it (" + pictureName + ") and save the image again as a PNG.");
                    compressed = true;
                }
                pixelWidth = fileWidth;
                pixelHeight = fileHeight;
            }
            else if (Math.Max(w, h) >= PictureSideKeptInPart)
                return Fallito("I cannot read the pixel size of the file «" + percorso + "», and SOLIDWORKS holds the picture " +
                               "at " + w + " x " + h + " pixels: at " + PictureSideKeptInPart + " px it may have compressed " +
                               "it, and a scale per pixel would be wrong by the factor I cannot see. Remove it (" +
                               pictureName + ") and save the image again as a PNG.");

            double mmPerPixel = larghezza / pixelWidth;
            double larghezzaLetta = lw * 1000, altezzaLetta = lh * 1000;
            string placementFault = PicturePlacementFault(
                sizeSet, originSet, placementRead,
                larghezza, larghezza * h / w, x, y,
                larghezzaLetta, altezzaLetta, ox * 1000, oy * 1000);
            if (placementFault != null)
                return Fallito(placementFault + " Remove it (" + pictureName + ") and try again.");
            // The scale check is on the pixel map SOLIDWORKS holds: those are the
            // pixels GetPointOnSketchFromPixel takes.
            double attesa = (w - 1) * (larghezza / w);
            if (!misura || Math.Abs(distanza - attesa) > attesa * 0.01)
                return Fallito("L'immagine e' larga " + larghezzaLetta.ToString("0.###") + " mm, ma il primo e l'ultimo pixel " +
                               "della prima riga, nello schizzo, distano " + (misura ? distanza.ToString("0.###") : "?") +
                               " mm invece di " + attesa.ToString("0.###") + ": la scala non e' quella che dice.");
            var misure = new Dictionary<string, object>
            {
                ["picture"] = nomeFeature,
                ["plane"] = piano,
                ["pixel"] = new[] { pixelWidth, pixelHeight },
                ["solidworks_pixels"] = new[] { w, h },
                ["width_mm"] = Math.Round(larghezzaLetta, 4),
                ["height_mm"] = Math.Round(altezzaLetta, 4),
                ["mm_per_pixel"] = Math.Round(mmPerPixel, 6),
                ["origin_mm"] = new[] { Math.Round(ox * 1000, 4), Math.Round(oy * 1000, 4) },
                ["check_scale_mm"] = Math.Round(distanza, 4),
                ["how_to_read_it"] =
                    "In the plane of the sketch the lower left corner of the picture is at «origin_mm». The pixel " +
                    "of the FILE in column c and row r (counted from 0, rows from the top) is at x = origin_mm[0] + " +
                    "c * mm_per_pixel, y = origin_mm[1] + (" + pixelHeight + " - r) * mm_per_pixel. Trace it from there " +
                    "with the sketch operations on the same plane.",
            };
            if (compressed)
                misure["compression"] =
                    "SOLIDWORKS keeps this picture at " + w + " x " + h + " pixels: it compresses a picture over " +
                    PictureSideKeptInPart + " px when it inserts it in a part (ISketchManager~InsertSketchPicture2.html). " +
                    "The file is " + fileWidth + " x " + fileHeight + ": «pixel» and «mm_per_pixel» are the FILE's, " +
                    "which is what you count in the image; the picture in the sketch has less detail than the file.";
            _log("[agente] picture " + pixelWidth + "x" + pixelHeight + " px (SOLIDWORKS " + w + "x" + h + ") at " +
                 mmPerPixel.ToString("0.####") + " mm/px on the plane " + piano);
            return RiuscitoSenzaCreare(misure);
        }
    }
}
