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
    /// </summary>
    public partial class OperazioniSw
    {
        private static readonly string[] Immagini = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

        private Esito ImmagineNelloSchizzo(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            if (!(modello is IPartDoc))
                return Fallito("Metto l'immagine in uno schizzo di un pezzo. Aprimi un pezzo (anche vuoto).");
            string percorso = (Str(a, "percorso", "") ?? "").Trim();
            if (percorso.Length == 0)
                return Fallito("Dimmi «percorso»: il file dell'immagine da ricalcare (PNG, JPG, BMP, TIF).");
            string est = (Path.GetExtension(percorso) ?? "").ToLowerInvariant();
            if (est == ".pdf")
                return Fallito("Un PDF in uno schizzo non entra (misurato: InsertSketchPicture risponde null). " +
                               "Converti la pagina in un'immagine (PNG) e dammi quella.");
            if (Array.IndexOf(Immagini, est) < 0)
                return Fallito("«" + percorso + "» non e' un'immagine che so mettere: PNG, JPG, BMP, GIF o TIF.");
            if (!File.Exists(percorso))
                return Fallito("Il file «" + percorso + "» non c'e'.");
            string piano = (Str(a, "piano", "") ?? "").Trim().ToLowerInvariant();
            if (piano != "frontale" && piano != "superiore" && piano != "destro")
                return Fallito("«piano» e' «frontale», «superiore» o «destro», non «" + piano + "».");
            double larghezza; string errore;
            if (!NumObbligatorio(a, "larghezza_mm", out larghezza, out errore)) return Fallito(errore);
            if (larghezza <= 0)
                return Fallito("«larghezza_mm» e' la larghezza VERA dell'immagine in millimetri (una misura nota " +
                               "divisa per i suoi pixel, per tutta la larghezza): deve essere positiva.");
            double x = Num(a, "x_mm", 0), y = Num(a, "y_mm", 0);

            if (!SelezionaPiano(modello, piano))
                return Fallito("Non riesco a selezionare il piano " + piano + ".");
            ISketchPicture foto = null;
            try
            {
                modello.SketchManager.InsertSketch(true);
                foto = modello.SketchManager.InsertSketchPicture(percorso) as ISketchPicture;
            }
            catch (Exception ex)
            {
                try { modello.SketchManager.InsertSketch(true); } catch { }
                return Fallito("L'immagine non e' entrata: " + ex.Message);
            }
            if (foto == null)
            {
                try { modello.SketchManager.InsertSketch(true); } catch { }
                return Fallito("SOLIDWORKS non ha messo l'immagine (InsertSketchPicture ha risposto null): il file " +
                               "c'e' ma forse non e' un'immagine vera. Prova a risalvarla in PNG.");
            }
            int w = 0, h = 0;
            double lw = 0, lh = 0, ox = 0, oy = 0;
            bool misura = false;
            double distanza = double.NaN;
            try
            {
                foto.GetPixelmapSize(ref w, ref h);
                if (w > 0 && h > 0)
                {
                    foto.SetSize(Unita.M(larghezza), Unita.M(larghezza * h / w), true);
                    foto.SetOrigin(Unita.M(x), Unita.M(y));
                    foto.GetSize(ref lw, ref lh);
                    foto.GetOrigin(ref ox, ref oy);
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
            double mmPerPixel = larghezza / w;
            double larghezzaLetta = lw * 1000, altezzaLetta = lh * 1000;
            if (Math.Abs(larghezzaLetta - larghezza) > larghezza * 0.01)
                return Fallito("Ho chiesto l'immagine larga " + larghezza.ToString("0.###") + " mm e rileggendola e' larga " +
                               larghezzaLetta.ToString("0.###") + ": la scala non ha preso.");
            double attesa = (w - 1) * mmPerPixel;
            if (!misura || Math.Abs(distanza - attesa) > attesa * 0.01)
                return Fallito("L'immagine e' larga " + larghezzaLetta.ToString("0.###") + " mm, ma il primo e l'ultimo pixel " +
                               "della prima riga, nello schizzo, distano " + (misura ? distanza.ToString("0.###") : "?") +
                               " mm invece di " + attesa.ToString("0.###") + ": la scala non e' quella che dice.");
            var misure = new Dictionary<string, object>
            {
                ["immagine"] = nomeFeature,
                ["piano"] = piano,
                ["pixel"] = new[] { w, h },
                ["larghezza_mm"] = Math.Round(larghezzaLetta, 4),
                ["altezza_mm"] = Math.Round(altezzaLetta, 4),
                ["mm_per_pixel"] = Math.Round(mmPerPixel, 6),
                ["origine_mm"] = new[] { Math.Round(ox * 1000, 4), Math.Round(oy * 1000, 4) },
                ["verifica_scala_mm"] = Math.Round(distanza, 4),
                ["come_leggerla"] =
                    "Nel piano dello schizzo l'angolo in basso a sinistra dell'immagine sta in «origine_mm». Il " +
                    "pixel in colonna c e riga r (contate da 0, la riga dall'alto) sta in x = origine_x + c * " +
                    "mm_per_pixel, y = origine_y + (" + h + " - r) * mm_per_pixel. Da qui si ricalca con le " +
                    "operazioni di schizzo sullo stesso piano.",
            };
            _log("[agente] immagine " + w + "x" + h + " px in scala " + mmPerPixel.ToString("0.####") + " mm/px sul piano " + piano);
            return RiuscitoSenzaCreare(misure);
        }
    }
}
