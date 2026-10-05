using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LA MARCATURA SU UNA FACCIA. (voce GP13)
    ///
    /// Numero di serie, verso di montaggio, nome del cliente, «ALTO»: e'
    /// quello che in officina si chiede su un pezzo su due, e fino a qui il
    /// catalogo non sapeva scrivere niente sul pezzo.
    ///
    /// ── NON E' UN'API SOLA ─────────────────────────────────────────────
    ///
    ///   IModelDoc2.InsertSketchText(Ptx, Pty, Ptz, Text, Alignment,
    ///                               FlipDirection, HorizontalMirror,
    ///                               WidthFactor, SpaceBetweenChars)
    ///                               -> ISketchText
    ///
    /// fa lo SCHIZZO. Il materiale lo toglie (o lo aggiunge) il taglio o
    /// l'estrusione che vengono dopo, che sono gli stessi gia' misurati.
    ///
    /// Misurata il 16/9/2026: **non apre nessuna finestra** (non e' come
    /// `AddDimension`), e funziona anche con `AddToDB = true`.
    ///
    /// ── IL DIFETTO CHE NON DA' ERRORE, E QUI E' GROSSO ─────────────────
    ///
    /// «PROVA» alta 10 mm, incisa 2 mm, su un quadrato di 100x100:
    ///
    ///     ancorata a (0, 0)     ->  tolti 230,70 mm3
    ///     ancorata a (30, 10)   ->  tolti 111,20 mm3
    ///
    /// **Meta' della scritta cade fuori dalla faccia e sparisce in silenzio.**
    /// La feature nasce verde, si chiama `Taglio-Estrusione1`, e nel pezzo c'e'
    /// «PRO». Nessun errore, da nessuna parte — e su un numero di serie non c'e'
    /// niente da cui accorgersene guardando l'albero.
    ///
    /// Tutta fuori invece si prende gia': `FeatureCut4` torna `null`. E' il
    /// caso facile. Quello che conta e' la sovrapposizione PARZIALE.
    ///
    /// ── COME SI PRENDE: LA SCATOLA DELLA SCRITTA ──────────────────────
    ///
    /// `ISketchText.GetEdges()` torna i contorni delle lettere — 56 `IEdge`
    /// per «PROVA» — e da quelli si legge **dove finisce davvero la scritta**,
    /// in coordinate del modello e in metri. Si controlla PRIMA di tagliare, e
    /// si rifiuta.
    ///
    /// Prima non si poteva sapere: la larghezza di una scritta non si calcola
    /// dalla stringa, dipende dal font. Misurata, invece, si sa — e per
    /// «PROVA» in Century Gothic alta 10 mm e' **47,05 x 10,51 mm**.
    ///
    /// ── QUELLO CHE HA UN NUMERO, E QUELLO CHE NON CE L'HA ─────────────
    ///
    /// L'area delle lettere non si puo' prevedere. Ma va col **quadrato
    /// dell'altezza**, ed e' misurato:
    ///
    ///     altezza  5 mm  ->  area 28,83 mm2   ->  area/h2 = 1,1533
    ///     altezza 10 mm  ->  area 115,35 mm2  ->  area/h2 = 1,1535
    ///     altezza 20 mm  ->  area 265,88 mm2  ->  area/h2 = 0,6647   <- rotto
    ///
    /// Il terzo numero e' rotto perche' a 20 mm «PROVA» e' larga 94 mm e su una
    /// faccia di 100 ancorata a x=0 non ci sta. **La stessa legge che tiene i
    /// primi due e' quella che denuncia il terzo**: due misure che si
    /// controllano a vicenda, e nessuna delle due e' una costante scritta a
    /// mano.
    ///
    /// ── `Alignment` MOVES NOTHING ON FREE TEXT ───────────────────────
    ///
    /// Narrowed in Z-C21.9 (finding C6-2). IModelDoc2~InsertSketchText.html:
    /// «Alignment 0 = Left 1 = Center 2 = Right 3 = Fully justified». It is the
    /// justification of the text, not a way to make the text follow a curve:
    /// that takes the edge or sketch segment selected with mark 1 (Remarks:
    /// «If you want to align the text with an edge or sketch segment, you must
    /// select the edge or sketch segment with a mark of 1»), and the guide's
    /// own example Align_Text_with_Line selects the line with mark 1 and still
    /// passes Alignment 0.
    ///
    /// Measured on 16/9/2026 on FREE text, one line and nothing selected, which
    /// is what engrave_text writes: the five values 0..4 gave the same box to
    /// the thousandth of a millimetre (4 is outside the documented range). So
    /// on free text the justification has nothing to move; with a curve
    /// selected it was not measured, and this operation never selects one.
    /// Hence the argument is not exposed, and 0 is passed. Until Z-C21.9 this
    /// note said the call ignores the argument altogether, which the page
    /// does not support.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>Quanto si tollera che la scritta sbordi, in mm.</summary>
        private const double ToleratedSpill = 0.01;

        private Outcome EngraveText(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string text = Str(a, "text", null);
            if (string.IsNullOrEmpty(text))
                return Failed("Manca il «text» da incidere.");

            var face = ResolveFace(docState, a, Str(a, "face_uid", ""), out string reason);
            if (face == null) return Failed(reason);

            double[] axes = FaceAxes(face, out double offsetM, out string error);
            if (axes == null) return Failed(error);

            double height = Num(a, "height_mm", 5);
            if (height <= 0)
                return Failed("Un testo di altezza nulla o negativa non e' un testo.");
            double depth = Num(a, "depth_mm", 0.5);
            if (depth <= 0)
                return Failed(
                    "Una marcatura di profondita' nulla non lascia segno. Se volevi solo " +
                    "un'annotazione e non una lavorazione, questa non e' l'operazione.");

            bool embossed = Bool(a, "raised", false);
            double xMm = Num(a, "x_mm", 0);
            double yMm = Num(a, "y_mm", 0);
            string font = Str(a, "font", null);
            bool bold = Bool(a, "bold", false);

            int bodiesBefore = CountSolidBodies(model);
            double? volumeBefore = VolumeMm3(model);
            if (bodiesBefore < 0 || volumeBefore == null)
                return Failed(VolumeNotReadBefore("marcare la faccia"));

            // La scatola della FACCIA, per sapere dove la scritta puo' stare.
            double[] faceBox = FaceBox(face);
            // Without the face box the overflow check (SpillsOutside) cannot run,
            // and a truncated inscription would be engraved without a word.
            // Refuse before touching the part (S6: the true box can fail where
            // IFace2.GetBox always answered).
            if (faceBox == null)
                return Failed(
                    "I cannot measure the face: its true bounding box did not answer, so I " +
                    "cannot check that the text stays inside it. Nothing was changed.");
            double faceArea = 0;
            try { faceArea = face.GetArea() * 1e6; } catch { }

            // The names of the tree before the sketch: the sketch engraved below
            // is the one born since (scheme S7, SwOperations.JustCreatedSketch),
            // not the last sketch of the tree.
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectFace(face)) return Failed("Non sono riuscito a selezionare la faccia.");

            var sketches = model.SketchManager;
            bool addToDbBefore = false, displayBefore = false;
            try { addToDbBefore = sketches.AddToDB; displayBefore = sketches.DisplayWhenAdded; } catch { }

            ISketchText written = null;
            double[] textBox = null;
            int contours = 0;
            try
            {
                sketches.InsertSketch(true);
                try { sketches.AddToDB = true; sketches.DisplayWhenAdded = false; } catch { }

                var point = PointInSketch(model, axes, offsetM, xMm, yMm, out error);
                if (point == null) throw new InvalidOperationException(error);

                written = model.InsertSketchText(
                    point[0], point[1], point[2],
                    text,
                    // Alignment (justification): on free text it moves nothing,
                    // measured; see the note at the top.
                    0,
                    0,   // FlipDirection
                    0,   // HorizontalMirror
                    100, // WidthFactor, in centesimi
                    100  // SpaceBetweenChars, in centesimi
                ) as ISketchText;
                if (written == null)
                    throw new InvalidOperationException(
                        "SOLIDWORKS non ha creato il testo di schizzo, e non dice perche'.");

                var sheetFormat = written.GetTextFormat() as ITextFormat;
                if (sheetFormat == null)
                    throw new InvalidOperationException(
                        "Non riesco a leggere il formato del testo: senza, l'altezza " +
                        "resterebbe quella predefinita e tu crederesti di averla scelta.");
                sheetFormat.CharHeight = Units.M(height);
                sheetFormat.Bold = bold;
                if (!string.IsNullOrWhiteSpace(font)) sheetFormat.TypeFaceName = font;
                if (!written.SetTextFormat(false, sheetFormat))
                    throw new InvalidOperationException(
                        "SOLIDWORKS ha rifiutato il formato del testo (altezza " + height +
                        " mm" + (font == null ? "" : ", font «" + font + "»") + ").");

                // E SI RILEGGE. `CharHeight` e' l'unica cosa che distingue una
                // marcatura da leggere da una da indovinare.
                var reread = written.GetTextFormat() as ITextFormat;
                double trueHeight = reread == null ? -1 : Units.Mm(reread.CharHeight);
                if (Math.Abs(trueHeight - height) > 0.001)
                    throw new InvalidOperationException(string.Format(
                        CultureInfo.InvariantCulture,
                        "Ho chiesto lettere alte {0:0.###} mm e rileggendo trovo {1}.",
                        height,
                        trueHeight < 0 ? "niente"
                            : trueHeight.ToString("0.###", CultureInfo.InvariantCulture) + " mm"));

                textBox = CurvesBox(written, out contours);
            }
            catch (Exception ex) { error = ex.Message; written = null; }
            finally
            {
                try { sketches.AddToDB = addToDbBefore; sketches.DisplayWhenAdded = displayBefore; } catch { }
                try { sketches.InsertSketch(false); } catch { }
            }
            if (written == null)
                return Failed((error ?? "il testo non e' stato creato") + WithoutSketch(model));

            // ── E QUI SI GUARDA SE CI STA, PRIMA DI TAGLIARE ─────────────
            //
            // Dopo non si puo' piu': la feature e' verde e le lettere di
            // troppo non ci sono. Lo schizzo si porta via — un pezzo con uno
            // schizzo orfano che nessuno vede e' peggio di un rifiuto.
            if (textBox == null)
                return Failed(
                    "Non riesco a leggere dove finisce la scritta (" + contours + " contorni " +
                    "letti). Senza, non posso dirti se ci sta sulla faccia, e una scritta " +
                    "tagliata a meta' non da' nessun errore." + WithoutSketch(model));

            string location = SpillsOutside(textBox, faceBox, axes);
            if (location != null)
                return Failed(string.Format(
                    CultureInfo.InvariantCulture,
                    "«{0}» alto {1:0.###} mm occupa {2:0.##} x {3:0.##} mm e {4} Non lo " +
                    "incido: la parte che esce sparirebbe senza che niente dia errore, e " +
                    "nel pezzo resterebbe una scritta monca. Sposta l'ancora, abbassa " +
                    "«height_mm», o accorcia il testo.{5}",
                    text, height,
                    textBox[3] - textBox[0],
                    textBox[4] - textBox[1],
                    location, WithoutSketch(model)));

            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            IFeature feature;
            try
            {
                feature = embossed
                    ? Extrude(model, depth, merge: true)
                    : Cut(model, throughAll: false, depthMm: depth);
            }
            catch (Exception ex)
            {
                return Failed(
                    (embossed ? "Il rilievo" : "L'incisione") + " non e' riuscita: " +
                    ex.Message + WithoutSketch(model));
            }

            if (feature == null)
                return Failed(
                    (embossed ? "Il rilievo" : "L'incisione") + " non e' stata creata, e " +
                    "SOLIDWORKS non dice perche'. Con un testo che sta tutto fuori dalla " +
                    "faccia risponde cosi'." + WithoutSketch(model));

            // ── e adesso si conta ────────────────────────────────────────
            double? after = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);
            if (after == null || bodiesAfter < 0)
                return Succeeded(feature, new Dictionary<string, object>
                {
                    ["text"] = text,
                    ["height_mm"] = height,
                    ["depth_mm"] = depth,
                    ["raised"] = embossed,
                    ["contours"] = contours,
                    ["width_written_mm"] = Round(textBox[3] - textBox[0]),
                    ["height_written_mm"] = Round(textBox[4] - textBox[1]),
                    [embossed ? "material_added_mm3" : "material_removed_mm3"] = null,
                    ["bodies"] = NumberOrNull(bodiesAfter),
                    ["volume_not_reread"] = NoteVolumeNotReread,
                }, FaceNormal(face));
            double moved = embossed ? after.Value - volumeBefore.Value : volumeBefore.Value - after.Value;

            if (moved <= 0)
                return Failed(
                    "La marcatura non ha " + (embossed ? "aggiunto" : "tolto") +
                    " materiale: il volume e' " +
                    (Math.Abs(moved) < 1e-9 ? "rimasto uguale" : "andato dall'altra parte") +
                    ". " + (embossed
                        ? "Di solito vuol dire che il rilievo e' cresciuto DENTRO il pezzo."
                        : "Di solito vuol dire che l'incisione e' andata in fuori.") +
                    " Ritirala — " + Sentences.HowToWithdrawIt + ".");

            if (bodiesAfter != bodiesBefore)
                return Failed(
                    "I corpi sono passati da " + bodiesBefore + " a " + bodiesAfter +
                    ": una marcatura non deve spezzare ne' staccare niente. Ritirala — " +
                    Sentences.HowToWithdrawIt + ".");

            double lettersArea = moved / depth;
            var measurements = new Dictionary<string, object>
            {
                ["text"] = text,
                ["height_mm"] = height,
                ["depth_mm"] = depth,
                ["raised"] = embossed,
                ["contours"] = contours,
                ["width_written_mm"] = Round(textBox[3] - textBox[0]),
                ["height_written_mm"] = Round(textBox[4] - textBox[1]),
                [embossed ? "material_added_mm3" : "material_removed_mm3"] = Round(moved),
                // L'AREA DELLE LETTERE, che e' il volume diviso la profondita'.
                // Non si puo' prevedere dalla stringa -- dipende dal font -- ma
                // va col QUADRATO dell'altezza, e quel rapporto e' l'unica cosa
                // che due marcature diverse hanno in comune.
                ["area_letters_mm2"] = Round(lettersArea),
                ["area_over_height_squared"] = Round(lettersArea / (height * height), 4),
                ["bodies"] = bodiesAfter,
            };
            if (!string.IsNullOrWhiteSpace(font)) measurements["font"] = font;

            // COSA NON HO GUARDATO, detto a ogni chiamata e non una volta nel
            // catalogo: chi legge la risposta potrebbe non averlo mai letto.
            double boxArea = 0;
            if (faceBox != null)
            {
                var sides = SidesOnPlane(faceBox, axes);
                boxArea = sides[0] * sides[1];
            }
            bool fullFace = boxArea > 0 && Math.Abs(faceArea - boxArea) / boxArea < 0.01;
            measurements["what_it_does_not_check"] = string.Format(
                CultureInfo.InvariantCulture,
                "Ho controllato che la scritta stia dentro la SCATOLA della faccia{0} E non " +
                "guardo se la scritta finisce sopra un'altra lavorazione.",
                fullFace
                    ? string.Format(CultureInfo.InvariantCulture,
                        ", e questa faccia la riempie tutta (area {0:0.##} mm2 contro " +
                        "{1:0.##} di scatola), quindi il controllo e' completo.",
                        faceArea, boxArea)
                    : string.Format(CultureInfo.InvariantCulture,
                        ", ma questa faccia NON riempie la sua scatola (area {0:0.##} mm2 " +
                        "contro {1:0.##} di scatola): ha un foro o un contorno rientrante, " +
                        "e una lettera potrebbe cadere li' sopra senza che io me ne " +
                        "accorga. Guarda il materiale tolto.",
                        faceArea, boxArea));

            _log("[agente] «" + text + "» " + (embossed ? "in rilievo " : "incisa ") +
                 depth + " mm: " + Round(moved) + " mm3, scritta " +
                 Round(textBox[3] - textBox[0]) + " mm larga");

            return Succeeded(feature, measurements, FaceNormal(face));
        }

        // =================================================================

        /// <summary>
        /// La scatola dei contorni delle lettere, in mm e in coordinate del
        /// MODELLO: {minX, minY, minZ, maxX, maxY, maxZ}.
        ///
        /// `ISketchText.GetEdges()` torna un `IEdge` per ogni contorno — 56
        /// per «PROVA», perche' ogni lettera ne ha uno esterno e le tonde anche
        /// quello interno. Di ognuno si guardano i due estremi **e cinque punti
        /// in mezzo**: un arco sta fuori dal segmento che unisce i suoi
        /// estremi, e la pancia di una «O» e' esattamente il punto che
        /// sborderebbe per primo.
        /// </summary>
        private static double[] CurvesBox(ISketchText written, out int contours)
        {
            contours = 0;
            object[] borders;
            try { borders = written.GetEdges() as object[]; }
            catch { return null; }
            if (borders == null || borders.Length == 0) return null;

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (var o in borders)
            {
                var border = o as IEdge;
                if (border == null) continue;
                double[] p;
                try { p = ApiHelpers.EdgeParameterArray(border); }
                catch { continue; }
                if (p == null || p.Length < 8) continue;
                contours++;
                Widen(p[0], p[1], p[2], ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                Widen(p[3], p[4], p[5], ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                try
                {
                    var curva = border.GetCurve() as ICurve;
                    if (curva == null) continue;
                    for (int i = 1; i < 6; i++)
                    {
                        double t = p[6] + (p[7] - p[6]) * i / 6.0;
                        var point = curva.Evaluate(t) as double[];
                        if (point != null && point.Length >= 3)
                            Widen(point[0], point[1], point[2],
                                    ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                    }
                }
                catch { }
            }
            if (contours == 0) return null;
            return new[]
            {
                Units.Mm(minX), Units.Mm(minY), Units.Mm(minZ),
                Units.Mm(maxX), Units.Mm(maxY), Units.Mm(maxZ),
            };
        }

        private static void Widen(
            double x, double y, double z,
            ref double minX, ref double minY, ref double minZ,
            ref double maxX, ref double maxY, ref double maxZ)
        {
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
            if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
        }

        /// <summary>La scatola di una faccia, in mm e in coordinate del modello.</summary>
        private static double[] FaceBox(IFace2 face)
        {
            try
            {
                var b = ApiHelpers.TrueBoundingBox(face);   // S6: not IFace2.GetBox, approximate
                if (b == null || b.Length < 6) return null;
                var outside = new double[6];
                for (int i = 0; i < 6; i++) outside[i] = Units.Mm(b[i]);
                return outside;
            }
            catch { return null; }
        }

        /// <summary>I due lati della scatola sul piano della faccia, in mm.</summary>
        private static double[] SidesOnPlane(double[] box, double[] axes)
        {
            int a0 = (int)axes[0], a1 = (int)axes[1];
            return new[]
            {
                box[3 + a0] - box[a0],
                box[3 + a1] - box[a1],
            };
        }

        /// <summary>
        /// Dove la scritta esce dalla faccia, o null se ci sta.
        ///
        /// Si guardano i DUE assi del piano della faccia e non tutti e tre: il
        /// terzo e' lo spessore, la scritta ci sta per costruzione, e
        /// confrontarlo darebbe un rifiuto per un millesimo di arrotondamento.
        /// </summary>
        private static string SpillsOutside(double[] text, double[] face, double[] axes)
        {
            if (face == null) return null;
            var names = new[] { "X", "Y", "Z" };
            var outside = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                int axis = (int)axes[i];
                if (text[axis] < face[axis] - ToleratedSpill)
                    outside.Add(string.Format(CultureInfo.InvariantCulture,
                        "esce di {0:0.##} mm dal bordo {1} basso",
                        face[axis] - text[axis], names[axis]));
                if (text[3 + axis] > face[3 + axis] + ToleratedSpill)
                    outside.Add(string.Format(CultureInfo.InvariantCulture,
                        "esce di {0:0.##} mm dal bordo {1} alto",
                        text[3 + axis] - face[3 + axis], names[axis]));
            }
            if (outside.Count == 0) return null;
            return string.Join(", ", outside) + " della faccia.";
        }

        private static double Round(double v, int digits = 2)
        {
            return Math.Round(v, digits);
        }
    }
}
