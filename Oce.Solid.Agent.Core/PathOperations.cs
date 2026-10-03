using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// TUBI E TRANSIZIONI: sweep e loft. (voce GP17)
    ///
    /// Un bocchettone passacavo, un manico, una transizione fra due sezioni.
    /// Non e' il lavoro quotidiano di un'officina prismatica, ma senza queste
    /// due il catalogo non sa fare niente che non sia dritto.
    ///
    /// ── THE PROFILE IS A NUMBER, NOT A SKETCH ─────────────────────────
    ///
    /// A circular profile given by its diameter (`CircularProfile` +
    /// `CircularProfileDiameter`) needs no profile sketch, only the path: a
    /// diameter is a number the language model can say, a profile sketch is
    /// something it has no way to point at.
    ///
    /// THE CALL IS OBSOLETE, and this is not the form to copy:
    /// IFeatureManager~InsertProtrusionSwept4.html, Remarks: «SOLIDWORKS 2018
    /// introduces a new sweep architecture, making this method obsolete. See
    /// Sweep Features and SweepFeatureData Objects to create this swept-boss or
    /// base feature.» It stays here only until the migration of finding L2-3
    /// (lot Z-C21.7): CreateDefinition(swFmSweep) -> ISweepFeatureData with
    /// the same CircularProfile and CircularProfileDiameter, the path selected
    /// with mark 4 BEFORE CreateDefinition, and the bench case
    /// straight_pipe_area_per_length unchanged to the seventh digit.
    ///
    /// Per la stessa ragione **il percorso e' un elenco di punti** e non uno
    /// schizzo da nominare: nello stato del documento gli schizzi non hanno un
    /// identificativo, e nessuna operazione del catalogo ne lascia in giro uno
    /// riutilizzabile. Se il percorso dovesse essere «uno schizzo», questa
    /// operazione non sarebbe chiamabile da nessuno.
    ///
    /// ── LE MARCHE, MISURATE ───────────────────────────────────────────
    ///
    ///   sweep, percorso con marca 4  ->  «Sweep1», volume giusto
    ///   sweep, percorso con marca 0  ->  «Sweep1», volume giusto
    ///   sweep, percorso con marca 1  ->  NULL
    ///   sweep, percorso con marca 2  ->  NULL
    ///
    ///   loft, profili con marca 1    ->  «Loft1», volume giusto
    ///   loft, profili con marca 0    ->  «Loft1», volume giusto
    ///   loft, profili con marca 4    ->  NULL
    ///
    /// Si usa **4 per il percorso** dello sweep e **1 per i profili** del loft:
    /// sono le marche che la documentazione di SOLIDWORKS associa ai due ruoli,
    /// e sono fra quelle che funzionano. Lo zero funziona in tutti e due i casi
    /// perche' vuol dire «nessuna marca», ma appoggiarcisi vorrebbe dire che
    /// due selezioni con ruoli diversi diventano indistinguibili il giorno che
    /// se ne aggiunge una terza.
    ///
    /// ── E UN LOFT FRA DUE CERCHI NON E' UN CONO ──────────────────────
    ///
    /// Misurato: cerchio Ø40 a quota 0, cerchio Ø20 a quota 50.
    ///
    ///   formula del tronco di cono   36.651,91 mm3
    ///   loft misurato                36.650,61 mm3
    ///   differenza                        1,30 mm3   (0,0035%)
    ///
    /// La superficie del loft e' **un'approssimazione a spline** della
    /// conica, non una conica. E' poco, ed e' sistematico: chi confronta il
    /// volume di un loft con la formula deve saperlo, o inseguira' un difetto
    /// che non c'e'. `revolve` sullo stesso trapezio da il cono esatto, e
    /// il banco tiene i due numeri accanto apposta.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito SweepCircolare(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            double diametro = Num(a, "diameter_mm", 0);
            if (diametro <= 0)
                return Fallito("Manca «diameter_mm», o e' zero: un tubo di diametro nullo non e' un tubo.");

            var punti = Punti(a, "path_mm");
            if (punti == null || punti.Count < 2)
                return Fallito(
                    "«path_mm» dev'essere un elenco di almeno due punti, ognuno tre " +
                    "numeri in millimetri: per esempio [[0,0,0], [100,0,0]]. E' la linea " +
                    "lungo cui corre il tubo.");

            // ── IL PERCORSO NON DEVE TORNARE SU SE STESSO ───────────────
            //
            // Due punti uguali di fila fanno un segmento di lunghezza zero, e
            // lo sweep non nasce senza dire perche'. Si guarda prima.
            double lunghezza = 0;
            for (int i = 1; i < punti.Count; i++)
            {
                double d = DistanzaFraPunti(punti[i - 1], punti[i]);
                if (d < 1e-6)
                    return Fallito(
                        "I punti " + i + " e " + (i + 1) + " del percorso sono lo stesso " +
                        "punto: un tratto di lunghezza zero non fa strada.");
                lunghezza += d;
            }

            double? volumePrima = VolumeMm3(modello);
            int corpiPrima = ContaCorpiSolidi(modello);
            if (volumePrima == null) return Fallito(VolumeNonLettoPrima("fare il tubo"));

            // THE PATH IS THE ONE 3D SKETCH BORN SINCE HERE (scheme S7, Z-C21.5):
            // the names of the tree are taken before the sketch is opened, and
            // the path is the 3DProfileFeature whose name was not among them
            // (OperazioniSw.FeatureBorn). Until library 0.146.0 it was the last
            // sketch of the tree, 2D or 3D, whatever step had made it.
            // IFeature~GetTypeName2.html lists 3DProfileFeature under Sketch,
            // with ISketch as its specific feature; the frame path, drawn with
            // the same Insert3DSketch, was read back as a 3DProfileFeature (bench
            // case `r12_frame_with_a_thinner_profile`).
            var pathNamesBefore = FeatureNamesNow(modello);
            var schizzi = modello.SketchManager;
            bool disegnato = false;
            string errore = null;
            try
            {
                modello.ClearSelection2(true);
                // UNO SCHIZZO 3D, e non uno su un piano: un percorso che gira
                // nello spazio non sta su nessun piano, e pretendere che ci
                // stia vorrebbe dire saper fare solo i tubi dritti.
                schizzi.Insert3DSketch(true);
                for (int i = 1; i < punti.Count; i++)
                {
                    var p = punti[i - 1];
                    var q = punti[i];
                    var linea = schizzi.CreateLine(
                        Unita.M(p[0]), Unita.M(p[1]), Unita.M(p[2]),
                        Unita.M(q[0]), Unita.M(q[1]), Unita.M(q[2]));
                    if (linea == null)
                        throw new InvalidOperationException(
                            "il tratto " + i + " del percorso non e' stato disegnato");
                }
                disegnato = true;
            }
            catch (Exception ex) { errore = ex.Message; }
            finally { try { schizzi.Insert3DSketch(false); } catch { } }

            if (!disegnato)
                return Fallito("Il percorso non e' stato disegnato: " + errore + SenzaLoSchizzo(modello));

            int pathsBorn;
            IFeature percorso = FeatureBorn(modello, pathNamesBefore, "3DProfileFeature", out pathsBorn);
            if (percorso == null)
                return Fallito(SketchNotIdentified(pathsBorn, "Non ritrovo lo schizzo del percorso appena fatto.") +
                               SenzaLoSchizzo(modello));

            modello.ClearSelection2(true);
            // MARCA 4: il percorso. Vedi la tabella nella nota in testa.
            if (!percorso.Select2(true, SelectionMarks.SweepPath))
                return Fallito("Non sono riuscito a selezionare il percorso." + SenzaLoSchizzo(modello));

            IFeature feature;
            try
            {
                feature = modello.FeatureManager.InsertProtrusionSwept4(
                    Propagate: false, Alignment: false, TwistCtrlOption: 0,
                    KeepTangency: false, BAdvancedSmoothing: false,
                    StartMatchingType: 0, EndMatchingType: 0,
                    IsThinBody: false, Thickness1: 0, Thickness2: 0, ThinType: 0,
                    PathAlign: 0, Merge: true, UseFeatScope: true, UseAutoSelect: true,
                    TwistAngle: 0, BMergeSmoothFaces: false,
                    CircularProfile: true,
                    CircularProfileDiameter: Unita.M(diametro),
                    Direction: 0) as IFeature;
            }
            catch (Exception ex)
            {
                return Fallito("Lo sweep non e' riuscito: " + ex.Message + SenzaLoSchizzo(modello));
            }

            if (feature == null)
                return Fallito(
                    "Lo sweep non e' stato creato, e SOLIDWORKS non dice perche'. Di solito " +
                    "e' il percorso che gira troppo stretto per il diametro chiesto: in una " +
                    "curva di raggio minore del raggio del tubo la superficie si " +
                    "attraverserebbe." + SenzaLoSchizzo(modello));

            double? volumeDopo = VolumeMm3(modello);
            int corpiDopo = ContaCorpiSolidi(modello);
            if (volumeDopo == null)
                return Riuscito(feature, new Dictionary<string, object>
                {
                    ["diameter_mm"] = diametro,
                    ["points_path"] = punti.Count,
                    ["path_length_mm"] = Math.Round(lunghezza, 3),
                    ["material_added_mm3"] = null,
                    ["bodies"] = NumeroOppureNull(corpiDopo),
                    ["volume_not_reread"] = NotaVolumeNonRiletto,
                });
            double aggiunto = volumeDopo.Value - volumePrima.Value;

            if (aggiunto <= 0)
                return Fallito(
                    "Lo sweep non ha aggiunto materiale: il volume e' rimasto quello di " +
                    "prima. Ritiralo — " + Frasi.CosiSiRitira + ".");

            // ── IL CONTO CHE SI PUO' FARE A MANO ────────────────────────
            //
            // Su un percorso DRITTO il volume e' area per lunghezza, esatto. Su
            // uno spezzato no: nei gomiti il materiale si sovrappone, e il
            // volume vero e' minore. Si dichiara quale dei due casi e', invece
            // di dare un numero che torna solo qualche volta.
            bool dritto = punti.Count == 2;
            double teorico = Math.PI * diametro * diametro / 4.0 * lunghezza;
            var misure = new Dictionary<string, object>
            {
                ["diameter_mm"] = diametro,
                ["points_path"] = punti.Count,
                ["path_length_mm"] = Math.Round(lunghezza, 3),
                ["material_added_mm3"] = Math.Round(aggiunto, 3),
                ["volume_theoretical_mm3"] = Math.Round(teorico, 3),
                ["path_straight"] = dritto,
                ["bodies"] = NumeroOppureNull(corpiDopo),
            };
            if (dritto)
            {
                double scarto = Math.Abs(aggiunto - teorico) / teorico;
                misure["deviation_from_theoretical"] = Math.Round(scarto, 8);
                if (scarto > 1e-4)
                    return Fallito(string.Format(
                        CultureInfo.InvariantCulture,
                        "Il tubo e' nato ma non e' quello: su un percorso dritto il volume " +
                        "dev'essere area per lunghezza, cioe' {0:0.###} mm3, e invece e' " +
                        "{1:0.###}. Ritiralo — " + Frasi.CosiSiRitira + ".",
                        teorico, aggiunto));
            }
            else
            {
                misure["note_on_volume"] =
                    "Il percorso ha dei gomiti: nei gomiti il materiale si sovrappone, " +
                    "quindi il volume vero e' MINORE di area per lunghezza. Il teorico qui " +
                    "sopra e' il limite superiore, non la risposta.";
            }
            if (corpiDopo >= 0 && corpiPrima > 0 && corpiDopo != corpiPrima)
                misure["warning_bodies"] =
                    "I corpi sono passati da " + corpiPrima + " a " + corpiDopo +
                    ": il tubo non si e' unito a quello che c'era.";

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agente] sweep Ø{0:0.##} su {1:0.##} mm di percorso: {2:0.##} mm3",
                diametro, lunghezza, aggiunto));

            return Riuscito(feature, misure);
        }

        // =================================================================

        private Esito LoftFraDueCerchi(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string piano = Str(a, "plane", "front");
            double d1 = Num(a, "diameter1_mm", 0);
            double d2 = Num(a, "diameter2_mm", 0);
            double distanza = Num(a, "distance_mm", 0);
            if (d1 <= 0 || d2 <= 0)
                return Fallito(
                    "Servono «diameter1_mm» e «diameter2_mm», tutti e due maggiori di zero. " +
                    "Per una transizione da tondo a punta non c'e' questa operazione.");
            // N12 (Z-C21.9): the catalogue promised «negative for the other side»
            // while this refused it as "missing". The catalogue now says what the
            // code does: greater than zero, on the side the plane faces.
            if (distanza <= 0)
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "«distance_mm» must be greater than zero (it is {0:0.###}): how far the " +
                    "second circle is from the first, on the side the chosen plane faces. At " +
                    "zero both circles lie on the same plane and there is nothing to join; the " +
                    "other side is not offered.", distanza));

            double sfalsa1 = Num(a, "offset1_mm", 0);
            double sfalsa2 = Num(a, "offset2_mm", 0);

            double? volumePrima = VolumeMm3(modello);
            int corpiPrima = ContaCorpiSolidi(modello);
            int pianiPrima = ContaPiani(modello);
            if (volumePrima == null) return Fallito(VolumeNonLettoPrima("fare il loft"));

            var schizzi = modello.SketchManager;
            IFeature s1, s2;
            try
            {
                // primo cerchio, sul piano scelto
                if (!SelezionaPiano(modello, piano))
                    return Fallito("Non ho trovato il piano «" + piano + "» nell'albero.");
                // EACH OF THE THREE FEATURES IS THE ONE BORN SINCE ITS OWN CALL
                // (Z-C21.5, finding N13, scheme S7): the first sketch, the plane
                // (what InsertRefPlane returned, else the RefPlane born) and the
                // second sketch. Until then the plane was the last feature of the
                // tree (the flat pattern on a sheet-metal part, finding M9) and
                // each sketch the last sketch of the tree, with nothing to tell a
                // second sketch that was never born from the first one.
                var beforeFirstSketch = FeatureNamesNow(modello);
                schizzi.InsertSketch(true);
                schizzi.CreateCircleByRadius(Unita.M(sfalsa1), 0, 0, Unita.M(d1 / 2));
                schizzi.InsertSketch(false);
                s1 = FeatureBorn(modello, beforeFirstSketch, "ProfileFeature", out _);
                modello.ClearSelection2(true);

                // A plane at the distance asked, and the second circle on it.
                // Z-C21.7 (L2-4): InsertRefPlane with the base plane selected
                // by name with mark 0, exactly the loft L2 of the InsertRefPlane
                // probe of Z-C21.3 (43980.736 mm3, the same volume as on the
                // CreatePlaneAtOffset3 plane it replaces, L1). Guide pages and
                // numbers in ApiHelpers.InsertOffsetPlane.
                var basePlane = TrovaPianoBase(modello, piano);
                if (basePlane == null || !ApiHelpers.SelectOffsetPlaneBase(modello, basePlane))
                    return Fallito("Non ho trovato il piano «" + piano + "» nell'albero.");
                var beforePlane = FeatureNamesNow(modello);
                var planeReturned = ApiHelpers.InsertOffsetPlane(modello, distanza) as IFeature;
                int pianiOra = ContaPiani(modello);
                if (pianiPrima < 0 || pianiOra < 0)
                    return Fallito(
                        "Non riesco a contare i piani dell'albero, e senza non so dire se il " +
                        "piano del secondo cerchio e' nato." + SenzaLoSchizzo(modello));
                if (pianiOra <= pianiPrima)
                    return Fallito(
                        "Il piano del secondo cerchio non e' stato creato, e nell'albero non " +
                        "ne e' comparso nessuno.");
                IFeature pianoNuovo = planeReturned ?? FeatureBorn(modello, beforePlane, "RefPlane", out _);
                var beforeSecondSketch = FeatureNamesNow(modello);
                modello.ClearSelection2(true);
                if (pianoNuovo == null || !pianoNuovo.Select2(false, 0))
                    return Fallito("Non sono riuscito a selezionare il piano appena creato.");
                schizzi.InsertSketch(true);
                schizzi.CreateCircleByRadius(Unita.M(sfalsa2), 0, 0, Unita.M(d2 / 2));
                schizzi.InsertSketch(false);
                s2 = FeatureBorn(modello, beforeSecondSketch, "ProfileFeature", out _);
                modello.ClearSelection2(true);
            }
            catch (Exception ex)
            {
                return Fallito("I due cerchi non sono stati disegnati: " + ex.Message);
            }
            if (s1 == null || s2 == null)
                return Fallito("Non ritrovo i due schizzi appena fatti.");

            modello.ClearSelection2(true);
            // MARCA 1 PER TUTTI E DUE I PROFILI, e l'ORDINE conta: il loft va
            // dal primo selezionato al secondo.
            if (!s1.Select2(true, SelectionMarks.LoftProfile) || !s2.Select2(true, SelectionMarks.LoftProfile))
                return Fallito("Non sono riuscito a selezionare i due profili.");

            IFeature feature;
            try
            {
                feature = modello.FeatureManager.InsertProtrusionBlend2(
                    Closed: false, KeepTangency: true, ForceNonRational: false,
                    TessToleranceFactor: 0.1,
                    StartMatchingType: 0, EndMatchingType: 0,
                    StartTangentLength: 0, EndTangentLength: 0,
                    StartTangentDir: false, EndTangentDir: false,
                    IsThinBody: false, Thickness1: 0, Thickness2: 0, ThinType: 0,
                    Merge: true, UseFeatScope: true, UseAutoSelect: true,
                    GuideCurveInfluence: 0) as IFeature;
            }
            catch (Exception ex) { return Fallito("Il loft non e' riuscito: " + ex.Message); }

            if (feature == null)
                return Fallito(
                    "Il loft non e' stato creato, e SOLIDWORKS non dice perche'. Di solito e' " +
                    "uno dei due profili che non si chiude, o due profili che si " +
                    "attraversano.");

            // I corpi si leggono UNA volta (prima tre chiamate per lo stesso
            // numero), e il volume dopo puo' non leggersi (voce Z-C8).
            int corpiDopo = ContaCorpiSolidi(modello);
            double? volumeDopo = VolumeMm3(modello);
            if (volumeDopo == null)
                return Riuscito(feature, new Dictionary<string, object>
                {
                    ["diameter1_mm"] = d1,
                    ["diameter2_mm"] = d2,
                    ["distance_mm"] = distanza,
                    ["material_added_mm3"] = null,
                    ["bodies"] = NumeroOppureNull(corpiDopo),
                    ["planes_added"] = PianiAggiunti(modello, pianiPrima),
                    ["volume_not_reread"] = NotaVolumeNonRiletto,
                });
            double aggiunto = volumeDopo.Value - volumePrima.Value;
            if (aggiunto <= 0)
                return Fallito(
                    "Il loft non ha aggiunto materiale. Ritiralo — " + Frasi.CosiSiRitira + ".");

            // Il tronco di cono: pi*h/3*(R^2 + R*r + r^2). Vale solo se i due
            // cerchi sono coassiali; sfalsandoli viene un tronco obliquo, che
            // per Cavalieri ha lo stesso volume — ma la superficie del loft e'
            // un'altra cosa, e il numero si scosta di piu'.
            double R = d1 / 2, r = d2 / 2;
            double teorico = Math.PI * Math.Abs(distanza) / 3.0 * (R * R + R * r + r * r);
            double scarto = Math.Abs(aggiunto - teorico) / teorico;

            var misure = new Dictionary<string, object>
            {
                ["diameter1_mm"] = d1,
                ["diameter2_mm"] = d2,
                ["distance_mm"] = distanza,
                ["material_added_mm3"] = Math.Round(aggiunto, 3),
                ["volume_cone_frustum_mm3"] = Math.Round(teorico, 3),
                ["deviation_from_cone_frustum"] = Math.Round(scarto, 8),
                ["bodies"] = NumeroOppureNull(corpiDopo),
                ["planes_added"] = PianiAggiunti(modello, pianiPrima),
                // NON E' UN CONO, ED E' MISURATO. Vedi la nota in testa.
                ["note_on_volume"] =
                    "La superficie di un loft fra due cerchi e' un'APPROSSIMAZIONE A SPLINE " +
                    "della conica, non una conica: il volume si scosta dalla formula del " +
                    "tronco di cono di qualche millesimo di per cento, sempre in difetto. " +
                    "Se ti serve il cono esatto, usa «revolve» su un trapezio.",
            };
            if (scarto > 0.005)
                return Fallito(string.Format(
                    CultureInfo.InvariantCulture,
                    "Il loft e' nato ma il volume non torna: dal tronco di cono verrebbe " +
                    "{0:0.###} mm3 e ne sono stati aggiunti {1:0.###}, cioe' {2:0.###}% di " +
                    "scarto. Un loft si scosta dalla formula di qualche millesimo di per " +
                    "cento, non di tanto: qui c'e' dell'altro. Ritiralo — " +
                    Frasi.CosiSiRitira + ".",
                    teorico, aggiunto, scarto * 100));

            if (corpiPrima > 0 && corpiDopo >= 0 && corpiDopo != corpiPrima)
                misure["warning_bodies"] =
                    "I corpi sono passati da " + corpiPrima + " a " + corpiDopo +
                    ": il loft non si e' unito a quello che c'era.";

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agente] loft Ø{0:0.##} -> Ø{1:0.##} su {2:0.##} mm: {3:0.##} mm3 " +
                "(tronco di cono {4:0.##})", d1, d2, distanza, aggiunto, teorico));

            return Riuscito(feature, misure);
        }

        // =================================================================

        private static List<double[]> Punti(Dictionary<string, object> a, string chiave)
        {
            object v;
            if (a == null || !a.TryGetValue(chiave, out v) || v == null) return null;
            var e = v as System.Collections.IEnumerable;
            if (e == null || v is string) return null;
            var fuori = new List<double[]>();
            foreach (var x in e)
            {
                var t = x as System.Collections.IEnumerable;
                if (t == null || x is string) return null;
                var n = new List<double>();
                foreach (var c in t)
                {
                    try { n.Add(Convert.ToDouble(c, CultureInfo.InvariantCulture)); }
                    catch { return null; }
                }
                if (n.Count != 3) return null;
                fuori.Add(n.ToArray());
            }
            return fuori;
        }

        private static double DistanzaFraPunti(double[] p, double[] q)
        {
            double dx = q[0] - p[0], dy = q[1] - p[1], dz = q[2] - p[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
