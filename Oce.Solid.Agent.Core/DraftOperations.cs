using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LO SFORMO. (voce GP15)
    ///
    /// L'angolo di spoglia: le pareti di un pezzo stampato in stampo devono
    /// pendere di un grado o due, o il pezzo non esce dalla forma. Serve anche
    /// alle fusioni e alle lavorazioni con angolo di spoglia.
    ///
    /// ── QUATTRO FAMIGLIE, E DUE HANNO LA FIRMA IDENTICA ───────────────
    ///
    ///   IFeatureManager.InsertMultiFaceDraft(Angle, FlipDir, EdgeDraft,
    ///                          PropType, IsStepDraft, IsBodyDraft) -> Feature
    ///   IFeatureManager.DraftXpertChange    (Angle, FlipDir, EdgeDraft,
    ///                          PropType, IsStepDraft, IsBodyDraft) -> Feature
    ///   IModelDoc2.InsertMfDraft (Angle, FlipDir, IsEdgeDraft, PropType)
    ///                                                             -> void
    ///   IModelDoc2.InsertMfDraft2(... , StepDraft)                 -> void
    ///
    /// Le prime due **non si distinguono leggendo**: stessi nomi di parametro,
    /// stessi tipi, stesso tipo di ritorno. Si distinguono solo misurando, e
    /// misurate rispondono in modo diverso: con le stesse identiche selezioni e
    /// gli stessi identici argomenti, `InsertMultiFaceDraft` fa lo sformo e
    /// **`DraftXpertChange` torna null**. Le ultime due tornano `void` e non si
    /// possono nemmeno controllare.
    ///
    /// Si usa `InsertMultiFaceDraft`.
    ///
    /// ── I MARCHI, E IL DIFETTO CHE CI STA DENTRO ──────────────────────
    ///
    /// **Faccia neutra marchio 1, facce da sformare marchio 2.** Misurato il
    /// 16/9/2026 su un blocco 100x100x20, sformo di 5 gradi:
    ///
    ///   neutra=1, facce=2   ->  «Sformo1», laterali a 5,00 gradi,
    ///                           volume 200000 -> 207080,7
    ///   neutra=2, facce=1   ->  «Sformo1», laterali a 0,00 gradi,
    ///                           volume 200000 -> 243744,3
    ///
    /// **La seconda riga e' il difetto.** Scambiando i marchi la feature nasce
    /// lo stesso, si chiama lo stesso, non da' nessun errore — e ha sformato
    /// altre facce, di un'altra quantita'. Chi non guarda il pezzo di lato non
    /// se ne accorge.
    ///
    /// ── E L'ANGOLO DELLA FEATURE NON E' UNA MISURA ────────────────────
    ///
    /// In tutti e due i casi `IDraftFeatureData.Angle` rilegge **5 gradi**:
    /// cioe' quello che si e' chiesto, non quello che e' successo. Anche nel
    /// caso in cui le facce sono rimaste a zero.
    ///
    /// Percio' qui l'angolo si misura **sulle facce del pezzo**, contando
    /// quante stanno all'inclinazione chiesta rispetto alla neutra, prima e
    /// dopo. E' l'unico numero che nel caso sbagliato viene diverso.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>Quanto si tollera sull'angolo letto dalle facce, in gradi.</summary>
        private const double DraftToleranceDeg = 0.05;

        private Outcome Draft(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double angle = Num(a, "angle_deg", 0);
            if (angle <= 0)
                return Failed(
                    "Manca «angle_deg», o e' zero: uno sformo di zero gradi non e' uno " +
                    "sformo, e' una parete dritta.");
            if (angle >= 90)
                return Failed(
                    "«angle_deg» dev'essere sotto i 90: a 90 gradi la parete si " +
                    "sdraia sulla faccia neutra. Per uno stampo si usa 1-3 gradi.");

            string neutralUid = Str(a, "neutral_face_uid", "");
            var neutral = ResolveFace(docState, a, neutralUid, out string reason);
            if (neutral == null) return Failed("Faccia neutra: " + reason);

            var uid = StringList(a, "faces_uid");
            if (uid.Count == 0)
                return Failed(
                    "Manca «faces_uid»: quali facce sformare. La faccia NEUTRA -- quella " +
                    "che resta ferma -- si dice a parte, in «neutral_face_uid».");
            if (uid.Contains(neutralUid, StringComparer.OrdinalIgnoreCase))
                return Failed(
                    "«" + neutralUid + "» e' insieme la faccia neutra e una da sformare. " +
                    "La neutra e' quella che NON si muove: e' il piano da cui si misura " +
                    "l'angolo.");

            var faces = new List<IFace2>();
            foreach (var u in uid)
            {
                var f = ResolveFace(docState, a, u, out reason);
                if (f == null) return Failed("Faccia «" + u + "»: " + reason);
                faces.Add(f);
            }

            var neutralNormal = FaceNormal(neutral);
            if (neutralNormal == null)
                return Failed("Non riesco a leggere l'orientamento della faccia neutra.");

            bool flip = Bool(a, "flip", false);
            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            int inclinedBefore = HowManyInclinedFaces(model, neutralNormal, angle);
            if (bodiesBefore < 0 || inclinedBefore < 0)
                return Failed(VolumeNotReadBefore("fare lo sformo").Replace("il volume del pezzo", "le facce del pezzo"));

            model.ClearSelection2(true);
            var handler = model.SelectionManager as ISelectionMgr;
            if (handler == null) return Failed("Non riesco a usare il gestore di selezione.");

            // NEUTRA MARCHIO 1, FACCE MARCHIO 2. Scambiati, la feature nasce
            // lo stesso e sforma le facce sbagliate: vedi la nota in testa.
            if (!Select(handler, neutral, SelectionMarks.DraftNeutralFace))
                return Failed("Non sono riuscito a selezionare la faccia neutra.");
            for (int i = 0; i < faces.Count; i++)
                if (!Select(handler, faces[i], SelectionMarks.DraftFaces))
                    return Failed("Non sono riuscito a selezionare la faccia «" + uid[i] + "».");

            IFeature feature;
            try
            {
                feature = model.FeatureManager.InsertMultiFaceDraft(
                    Angle: angle * Math.PI / 180.0,
                    FlipDir: flip,
                    EdgeDraft: false,
                    PropType: (int)swDraftFacePropagationType_e.swFacePropNone,
                    IsStepDraft: false,
                    IsBodyDraft: false) as IFeature;
            }
            catch (Exception ex) { return Failed("Lo sformo non e' riuscito: " + ex.Message); }

            if (feature == null)
                return Failed(
                    "Lo sformo non e' stato creato, e SOLIDWORKS non dice perche'. Di " +
                    "solito e' l'angolo troppo grande per l'altezza della parete, o una " +
                    "faccia che non tocca la neutra.");

            // ── E ADESSO SI MISURA IL PEZZO, NON LA FEATURE ──────────────
            //
            // `IDraftFeatureData.Angle` rilegge l'angolo CHIESTO anche quando
            // non si e' inclinato niente. L'unica misura vera e' quante facce
            // del pezzo stanno all'angolo chiesto rispetto alla neutra.
            int inclinedAfter = HowManyInclinedFaces(model, neutralNormal, angle);
            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);
            int newOnes = inclinedAfter - inclinedBefore;
            if (inclinedAfter < 0)
                return Failed(
                    "Lo sformo e' nato, ma poi non riesco a ricontare le facce inclinate: " +
                    "la feature dichiara l'angolo anche quando non ha inclinato niente, quindi " +
                    "senza quel conto non posso dirti che ha funzionato. Guarda il pezzo — " +
                    Sentences.HowToWithdrawIt + ".");

            if (newOnes < faces.Count)
            {
                string howToWithdraw =
                    " Ritiralo — " + Sentences.HowToWithdrawIt + ".";
                return Failed(string.Format(
                    CultureInfo.InvariantCulture,
                    "Ho chiesto di sformare {0} facce di {1:0.###} gradi e la lavorazione " +
                    "e' nata, ma sul pezzo le facce a quell'angolo sono passate da {2} a " +
                    "{3}: {4}. La feature dichiara l'angolo che le ho chiesto anche quando " +
                    "non ha inclinato niente, quindi l'unico modo di saperlo e' misurare le " +
                    "facce -- ed e' quello che ho fatto. Quasi sempre vuol dire che la " +
                    "faccia neutra non e' quella giusta, o che le facce indicate non la " +
                    "toccano.{5}",
                    faces.Count, angle, inclinedBefore, inclinedAfter,
                    newOnes <= 0 ? "non se n'e' inclinata nessuna" : "ne mancano " + (faces.Count - newOnes),
                    howToWithdraw));
            }

            if (bodiesAfter >= 0 && bodiesAfter != bodiesBefore)
                return Failed(
                    "I corpi sono passati da " + bodiesBefore + " a " + bodiesAfter +
                    ": uno sformo non deve spezzare il pezzo. Ritiralo — " +
                    Sentences.HowToWithdrawIt + ".");

            // Lo sformo non si decide sul volume (le facce si contano): qui il
            // volume si riporta soltanto, e se non si legge si dice (voce Z-C8).
            double? moved = volumeAfter - volumeBefore;
            var measurements = new Dictionary<string, object>
            {
                ["angle_deg"] = angle,
                ["faces_drafted"] = faces.Count,
                ["faces_at_corner_before"] = inclinedBefore,
                ["faces_at_corner_after"] = inclinedAfter,
                ["flip"] = flip,
                ["material_mm3"] = NumberOrNull(moved, 2),
                ["part_grew"] = moved.HasValue ? (object)(moved.Value > 0) : null,
                ["bodies"] = NumberOrNull(bodiesAfter),
                // L'ANGOLO DICHIARATO DALLA FEATURE, accanto a quello misurato
                // e con scritto che cos'e'. Non si nasconde: si nomina.
                ["angle_declared_by_feature"] = FeatureAngle(feature),
                ["what_it_does_not_check"] =
                    "Ho contato le facce che stanno all'angolo chiesto rispetto alla neutra, " +
                    "non VERIFICATO UNA PER UNA che siano proprio quelle che mi hai " +
                    "indicato: dopo uno sformo le facce si rinumerano. Se il conto torna e " +
                    "il volume e' andato dalla parte giusta, il pezzo e' quello. E non " +
                    "guardo se l'angolo basta per lo stampo che hai in mente.",
            };

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agente] sformo di {0:0.###} gradi su {1} facce: all'angolo {2} -> {3}, " +
                "volume {4}", angle, faces.Count, inclinedBefore, inclinedAfter, ReadValue(moved)));

            return Succeeded(feature, measurements, neutralNormal);
        }

        // =================================================================

        private static bool Select(ISelectionMgr handler, IFace2 face, int mark)
        {
            try
            {
                var d = (SelectData)handler.CreateSelectData();
                d.Mark = mark;
                return (face as IEntity).Select4(true, d);
            }
            catch { return false; }
        }

        /// <summary>
        /// Quante facce piane del pezzo stanno all'angolo chiesto rispetto alla
        /// faccia neutra.
        ///
        /// Una parete dritta e' perpendicolare alla neutra: il suo angolo con
        /// la normale della neutra e' 90 gradi. Sformata di `alfa`, diventa
        /// `90 - alfa` oppure `90 + alfa` secondo il verso — e siccome il verso
        /// si puo' capovolgere, si accettano tutt'e due e si conta.
        ///
        /// Si contano le facce PIANE e basta: una faccia cilindrica sformata
        /// diventa conica e non ha un'inclinazione sola.
        ///
        /// **-1 se non si e' potuto leggere** (voce Z-C8). ZERO-E-ZERO: un pezzo
        /// senza corpi solidi (`GetBodies2` null) non ha facce inclinate.
        /// </summary>
        private static int HowManyInclinedFaces(
            IModelDoc2 model, double[] neutralNormal, double angleDeg)
        {
            int howMany = 0;
            try
            {
                var bodies = (model as IPartDoc)
                    .GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (bodies == null) return 0;
                foreach (var oc in bodies)
                {
                    var body = oc as IBody2;
                    if (body == null) continue;
                    var f = body.GetFirstFace() as IFace2;
                    while (f != null)
                    {
                        var n = ApiHelpers.NormalIfPlanar(f);
                        if (n != null)
                        {
                            double cosine =
                                n[0] * neutralNormal[0] +
                                n[1] * neutralNormal[1] +
                                n[2] * neutralNormal[2];
                            if (cosine > 1) cosine = 1;
                            if (cosine < -1) cosine = -1;
                            double deg = Math.Acos(cosine) * 180.0 / Math.PI;
                            if (Math.Abs(deg - (90.0 - angleDeg)) < DraftToleranceDeg ||
                                Math.Abs(deg - (90.0 + angleDeg)) < DraftToleranceDeg)
                                howMany++;
                        }
                        f = f.GetNextFace() as IFace2;
                    }
                }
            }
            catch { return -1; }
            return howMany;
        }

        private static object FeatureAngle(IFeature feature)
        {
            try
            {
                var d = feature.GetDefinition() as IDraftFeatureData;
                if (d == null) return null;
                return Math.Round(d.Angle * 180.0 / Math.PI, 3);
            }
            catch { return null; }
        }
    }
}
