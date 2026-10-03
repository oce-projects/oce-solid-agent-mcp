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
    public partial class OperazioniSw
    {
        /// <summary>Quanto si tollera sull'angolo letto dalle facce, in gradi.</summary>
        private const double SformoTolleranzaGradi = 0.05;

        private Esito Sformo(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            double angolo = Num(a, "angle_deg", 0);
            if (angolo <= 0)
                return Fallito(
                    "Manca «angle_deg», o e' zero: uno sformo di zero gradi non e' uno " +
                    "sformo, e' una parete dritta.");
            if (angolo >= 90)
                return Fallito(
                    "«angle_deg» dev'essere sotto i 90: a 90 gradi la parete si " +
                    "sdraia sulla faccia neutra. Per uno stampo si usa 1-3 gradi.");

            string neutraUid = Str(a, "neutral_face_uid", "");
            var neutra = RisolviFaccia(stato, a, neutraUid, out string motivo);
            if (neutra == null) return Fallito("Faccia neutra: " + motivo);

            var uid = Lista(a, "faces_uid");
            if (uid.Count == 0)
                return Fallito(
                    "Manca «faces_uid»: quali facce sformare. La faccia NEUTRA -- quella " +
                    "che resta ferma -- si dice a parte, in «neutral_face_uid».");
            if (uid.Contains(neutraUid, StringComparer.OrdinalIgnoreCase))
                return Fallito(
                    "«" + neutraUid + "» e' insieme la faccia neutra e una da sformare. " +
                    "La neutra e' quella che NON si muove: e' il piano da cui si misura " +
                    "l'angolo.");

            var facce = new List<IFace2>();
            foreach (var u in uid)
            {
                var f = RisolviFaccia(stato, a, u, out motivo);
                if (f == null) return Fallito("Faccia «" + u + "»: " + motivo);
                facce.Add(f);
            }

            var normaleNeutra = NormaleDellaFaccia(neutra);
            if (normaleNeutra == null)
                return Fallito("Non riesco a leggere l'orientamento della faccia neutra.");

            bool capovolgi = Bool(a, "flip", false);
            double? volumePrima = VolumeMm3(modello);
            int corpiPrima = ContaCorpiSolidi(modello);
            int inclinatePrima = QuanteFacceInclinate(modello, normaleNeutra, angolo);
            if (corpiPrima < 0 || inclinatePrima < 0)
                return Fallito(VolumeNonLettoPrima("fare lo sformo").Replace("il volume del pezzo", "le facce del pezzo"));

            modello.ClearSelection2(true);
            var gestore = modello.SelectionManager as ISelectionMgr;
            if (gestore == null) return Fallito("Non riesco a usare il gestore di selezione.");

            // NEUTRA MARCHIO 1, FACCE MARCHIO 2. Scambiati, la feature nasce
            // lo stesso e sforma le facce sbagliate: vedi la nota in testa.
            if (!Seleziona(gestore, neutra, SelectionMarks.DraftNeutralFace))
                return Fallito("Non sono riuscito a selezionare la faccia neutra.");
            for (int i = 0; i < facce.Count; i++)
                if (!Seleziona(gestore, facce[i], SelectionMarks.DraftFaces))
                    return Fallito("Non sono riuscito a selezionare la faccia «" + uid[i] + "».");

            IFeature feature;
            try
            {
                feature = modello.FeatureManager.InsertMultiFaceDraft(
                    Angle: angolo * Math.PI / 180.0,
                    FlipDir: capovolgi,
                    EdgeDraft: false,
                    PropType: (int)swDraftFacePropagationType_e.swFacePropNone,
                    IsStepDraft: false,
                    IsBodyDraft: false) as IFeature;
            }
            catch (Exception ex) { return Fallito("Lo sformo non e' riuscito: " + ex.Message); }

            if (feature == null)
                return Fallito(
                    "Lo sformo non e' stato creato, e SOLIDWORKS non dice perche'. Di " +
                    "solito e' l'angolo troppo grande per l'altezza della parete, o una " +
                    "faccia che non tocca la neutra.");

            // ── E ADESSO SI MISURA IL PEZZO, NON LA FEATURE ──────────────
            //
            // `IDraftFeatureData.Angle` rilegge l'angolo CHIESTO anche quando
            // non si e' inclinato niente. L'unica misura vera e' quante facce
            // del pezzo stanno all'angolo chiesto rispetto alla neutra.
            int inclinateDopo = QuanteFacceInclinate(modello, normaleNeutra, angolo);
            double? volumeDopo = VolumeMm3(modello);
            int corpiDopo = ContaCorpiSolidi(modello);
            int nuove = inclinateDopo - inclinatePrima;
            if (inclinateDopo < 0)
                return Fallito(
                    "Lo sformo e' nato, ma poi non riesco a ricontare le facce inclinate: " +
                    "la feature dichiara l'angolo anche quando non ha inclinato niente, quindi " +
                    "senza quel conto non posso dirti che ha funzionato. Guarda il pezzo — " +
                    Frasi.CosiSiRitira + ".");

            if (nuove < facce.Count)
            {
                string comeSiRitira =
                    " Ritiralo — " + Frasi.CosiSiRitira + ".";
                return Fallito(string.Format(
                    CultureInfo.InvariantCulture,
                    "Ho chiesto di sformare {0} facce di {1:0.###} gradi e la lavorazione " +
                    "e' nata, ma sul pezzo le facce a quell'angolo sono passate da {2} a " +
                    "{3}: {4}. La feature dichiara l'angolo che le ho chiesto anche quando " +
                    "non ha inclinato niente, quindi l'unico modo di saperlo e' misurare le " +
                    "facce -- ed e' quello che ho fatto. Quasi sempre vuol dire che la " +
                    "faccia neutra non e' quella giusta, o che le facce indicate non la " +
                    "toccano.{5}",
                    facce.Count, angolo, inclinatePrima, inclinateDopo,
                    nuove <= 0 ? "non se n'e' inclinata nessuna" : "ne mancano " + (facce.Count - nuove),
                    comeSiRitira));
            }

            if (corpiDopo >= 0 && corpiDopo != corpiPrima)
                return Fallito(
                    "I corpi sono passati da " + corpiPrima + " a " + corpiDopo +
                    ": uno sformo non deve spezzare il pezzo. Ritiralo — " +
                    Frasi.CosiSiRitira + ".");

            // Lo sformo non si decide sul volume (le facce si contano): qui il
            // volume si riporta soltanto, e se non si legge si dice (voce Z-C8).
            double? mosso = volumeDopo - volumePrima;
            var misure = new Dictionary<string, object>
            {
                ["angle_deg"] = angolo,
                ["faces_drafted"] = facce.Count,
                ["faces_at_corner_before"] = inclinatePrima,
                ["faces_at_corner_after"] = inclinateDopo,
                ["flip"] = capovolgi,
                ["material_mm3"] = NumeroOppureNull(mosso, 2),
                ["part_grew"] = mosso.HasValue ? (object)(mosso.Value > 0) : null,
                ["bodies"] = NumeroOppureNull(corpiDopo),
                // L'ANGOLO DICHIARATO DALLA FEATURE, accanto a quello misurato
                // e con scritto che cos'e'. Non si nasconde: si nomina.
                ["angle_declared_by_feature"] = AngoloDellaFeature(feature),
                ["what_it_does_not_check"] =
                    "Ho contato le facce che stanno all'angolo chiesto rispetto alla neutra, " +
                    "non VERIFICATO UNA PER UNA che siano proprio quelle che mi hai " +
                    "indicato: dopo uno sformo le facce si rinumerano. Se il conto torna e " +
                    "il volume e' andato dalla parte giusta, il pezzo e' quello. E non " +
                    "guardo se l'angolo basta per lo stampo che hai in mente.",
            };

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agente] sformo di {0:0.###} gradi su {1} facce: all'angolo {2} -> {3}, " +
                "volume {4}", angolo, facce.Count, inclinatePrima, inclinateDopo, Letto(mosso)));

            return Riuscito(feature, misure, normaleNeutra);
        }

        // =================================================================

        private static bool Seleziona(ISelectionMgr gestore, IFace2 faccia, int marchio)
        {
            try
            {
                var d = (SelectData)gestore.CreateSelectData();
                d.Mark = marchio;
                return (faccia as IEntity).Select4(true, d);
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
        private static int QuanteFacceInclinate(
            IModelDoc2 modello, double[] normaleNeutra, double angoloGradi)
        {
            int quante = 0;
            try
            {
                var corpi = (modello as IPartDoc)
                    .GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (corpi == null) return 0;
                foreach (var oc in corpi)
                {
                    var corpo = oc as IBody2;
                    if (corpo == null) continue;
                    var f = corpo.GetFirstFace() as IFace2;
                    while (f != null)
                    {
                        var n = ApiHelpers.NormalIfPlanar(f);
                        if (n != null)
                        {
                            double coseno =
                                n[0] * normaleNeutra[0] +
                                n[1] * normaleNeutra[1] +
                                n[2] * normaleNeutra[2];
                            if (coseno > 1) coseno = 1;
                            if (coseno < -1) coseno = -1;
                            double gradi = Math.Acos(coseno) * 180.0 / Math.PI;
                            if (Math.Abs(gradi - (90.0 - angoloGradi)) < SformoTolleranzaGradi ||
                                Math.Abs(gradi - (90.0 + angoloGradi)) < SformoTolleranzaGradi)
                                quante++;
                        }
                        f = f.GetNextFace() as IFace2;
                    }
                }
            }
            catch { return -1; }
            return quante;
        }

        private static object AngoloDellaFeature(IFeature feature)
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
