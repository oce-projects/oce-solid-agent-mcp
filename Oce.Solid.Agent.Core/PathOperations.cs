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
    public partial class SwOperations
    {
        private Outcome CircularSweep(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double diameter = Num(a, "diameter_mm", 0);
            if (diameter <= 0)
                return Failed("Manca «diameter_mm», o e' zero: un tubo di diametro nullo non e' un tubo.");

            var points = Points(a, "path_mm");
            if (points == null || points.Count < 2)
                return Failed(
                    "«path_mm» dev'essere un elenco di almeno due punti, ognuno tre " +
                    "numeri in millimetri: per esempio [[0,0,0], [100,0,0]]. E' la linea " +
                    "lungo cui corre il tubo.");

            // ── IL PERCORSO NON DEVE TORNARE SU SE STESSO ───────────────
            //
            // Due punti uguali di fila fanno un segmento di lunghezza zero, e
            // lo sweep non nasce senza dire perche'. Si guarda prima.
            double length = 0;
            for (int i = 1; i < points.Count; i++)
            {
                double d = DistanceBetweenPoints(points[i - 1], points[i]);
                if (d < 1e-6)
                    return Failed(
                        "I punti " + i + " e " + (i + 1) + " del percorso sono lo stesso " +
                        "punto: un tratto di lunghezza zero non fa strada.");
                length += d;
            }

            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            if (volumeBefore == null) return Failed(VolumeNotReadBefore("fare il tubo"));

            // THE PATH IS THE ONE 3D SKETCH BORN SINCE HERE (scheme S7, Z-C21.5):
            // the names of the tree are taken before the sketch is opened, and
            // the path is the 3DProfileFeature whose name was not among them
            // (SwOperations.FeatureBorn). Until library 0.146.0 it was the last
            // sketch of the tree, 2D or 3D, whatever step had made it.
            // IFeature~GetTypeName2.html lists 3DProfileFeature under Sketch,
            // with ISketch as its specific feature; the frame path, drawn with
            // the same Insert3DSketch, was read back as a 3DProfileFeature (bench
            // case `r12_frame_with_a_thinner_profile`).
            var pathNamesBefore = FeatureNamesNow(model);
            var sketches = model.SketchManager;
            bool drawn = false;
            string error = null;
            try
            {
                model.ClearSelection2(true);
                // UNO SCHIZZO 3D, e non uno su un piano: un percorso che gira
                // nello spazio non sta su nessun piano, e pretendere che ci
                // stia vorrebbe dire saper fare solo i tubi dritti.
                sketches.Insert3DSketch(true);
                for (int i = 1; i < points.Count; i++)
                {
                    var p = points[i - 1];
                    var q = points[i];
                    var line = sketches.CreateLine(
                        Units.M(p[0]), Units.M(p[1]), Units.M(p[2]),
                        Units.M(q[0]), Units.M(q[1]), Units.M(q[2]));
                    if (line == null)
                        throw new InvalidOperationException(
                            "il tratto " + i + " del percorso non e' stato disegnato");
                }
                drawn = true;
            }
            catch (Exception ex) { error = ex.Message; }
            finally { try { sketches.Insert3DSketch(false); } catch { } }

            if (!drawn)
                return Failed("Il percorso non e' stato disegnato: " + error + WithoutSketch(model));

            int pathsBorn;
            IFeature filePath = FeatureBorn(model, pathNamesBefore, "3DProfileFeature", out pathsBorn);
            if (filePath == null)
                return Failed(SketchNotIdentified(pathsBorn, "Non ritrovo lo schizzo del percorso appena fatto.") +
                               WithoutSketch(model));

            model.ClearSelection2(true);
            // MARCA 4: il percorso. Vedi la tabella nella nota in testa.
            if (!filePath.Select2(true, SelectionMarks.SweepPath))
                return Failed("Non sono riuscito a selezionare il percorso." + WithoutSketch(model));

            IFeature feature;
            try
            {
                feature = model.FeatureManager.InsertProtrusionSwept4(
                    Propagate: false, Alignment: false, TwistCtrlOption: 0,
                    KeepTangency: false, BAdvancedSmoothing: false,
                    StartMatchingType: 0, EndMatchingType: 0,
                    IsThinBody: false, Thickness1: 0, Thickness2: 0, ThinType: 0,
                    PathAlign: 0, Merge: true, UseFeatScope: true, UseAutoSelect: true,
                    TwistAngle: 0, BMergeSmoothFaces: false,
                    CircularProfile: true,
                    CircularProfileDiameter: Units.M(diameter),
                    Direction: 0) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("Lo sweep non e' riuscito: " + ex.Message + WithoutSketch(model));
            }

            if (feature == null)
                return Failed(
                    "Lo sweep non e' stato creato, e SOLIDWORKS non dice perche'. Di solito " +
                    "e' il percorso che gira troppo stretto per il diametro chiesto: in una " +
                    "curva di raggio minore del raggio del tubo la superficie si " +
                    "attraverserebbe." + WithoutSketch(model));

            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);
            if (volumeAfter == null)
                return Succeeded(feature, new Dictionary<string, object>
                {
                    ["diameter_mm"] = diameter,
                    ["points_path"] = points.Count,
                    ["path_length_mm"] = Math.Round(length, 3),
                    ["material_added_mm3"] = null,
                    ["bodies"] = NumberOrNull(bodiesAfter),
                    ["volume_not_reread"] = NoteVolumeNotReread,
                });
            double added = volumeAfter.Value - volumeBefore.Value;

            if (added <= 0)
                return Failed(
                    "Lo sweep non ha aggiunto materiale: il volume e' rimasto quello di " +
                    "prima. Ritiralo — " + Sentences.HowToWithdrawIt + ".");

            // ── IL CONTO CHE SI PUO' FARE A MANO ────────────────────────
            //
            // Su un percorso DRITTO il volume e' area per lunghezza, esatto. Su
            // uno spezzato no: nei gomiti il materiale si sovrappone, e il
            // volume vero e' minore. Si dichiara quale dei due casi e', invece
            // di dare un numero che torna solo qualche volta.
            bool straight = points.Count == 2;
            double theoretical = Math.PI * diameter * diameter / 4.0 * length;
            var measurements = new Dictionary<string, object>
            {
                ["diameter_mm"] = diameter,
                ["points_path"] = points.Count,
                ["path_length_mm"] = Math.Round(length, 3),
                ["material_added_mm3"] = Math.Round(added, 3),
                ["volume_theoretical_mm3"] = Math.Round(theoretical, 3),
                ["path_straight"] = straight,
                ["bodies"] = NumberOrNull(bodiesAfter),
            };
            if (straight)
            {
                double deviation = Math.Abs(added - theoretical) / theoretical;
                measurements["deviation_from_theoretical"] = Math.Round(deviation, 8);
                if (deviation > 1e-4)
                    return Failed(string.Format(
                        CultureInfo.InvariantCulture,
                        "Il tubo e' nato ma non e' quello: su un percorso dritto il volume " +
                        "dev'essere area per lunghezza, cioe' {0:0.###} mm3, e invece e' " +
                        "{1:0.###}. Ritiralo — " + Sentences.HowToWithdrawIt + ".",
                        theoretical, added));
            }
            else
            {
                measurements["note_on_volume"] =
                    "Il percorso ha dei gomiti: nei gomiti il materiale si sovrappone, " +
                    "quindi il volume vero e' MINORE di area per lunghezza. Il teorico qui " +
                    "sopra e' il limite superiore, non la risposta.";
            }
            if (bodiesAfter >= 0 && bodiesBefore > 0 && bodiesAfter != bodiesBefore)
                measurements["warning_bodies"] =
                    "I corpi sono passati da " + bodiesBefore + " a " + bodiesAfter +
                    ": il tubo non si e' unito a quello che c'era.";

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agente] sweep Ø{0:0.##} su {1:0.##} mm di percorso: {2:0.##} mm3",
                diameter, length, added));

            return Succeeded(feature, measurements);
        }

        // =================================================================

        private Outcome LoftTwoCircles(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string plane = Str(a, "plane", "front");
            double d1 = Num(a, "diameter1_mm", 0);
            double d2 = Num(a, "diameter2_mm", 0);
            double distance = Num(a, "distance_mm", 0);
            if (d1 <= 0 || d2 <= 0)
                return Failed(
                    "Servono «diameter1_mm» e «diameter2_mm», tutti e due maggiori di zero. " +
                    "Per una transizione da tondo a punta non c'e' questa operazione.");
            // N12 (Z-C21.9): the catalogue promised «negative for the other side»
            // while this refused it as "missing". The catalogue now says what the
            // code does: greater than zero, on the side the plane faces.
            if (distance <= 0)
                return Failed(string.Format(CultureInfo.InvariantCulture,
                    "«distance_mm» must be greater than zero (it is {0:0.###}): how far the " +
                    "second circle is from the first, on the side the chosen plane faces. At " +
                    "zero both circles lie on the same plane and there is nothing to join; the " +
                    "other side is not offered.", distance));

            double offsets1 = Num(a, "offset1_mm", 0);
            double offsets2 = Num(a, "offset2_mm", 0);

            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            int planesBefore = CountPlanes(model);
            if (volumeBefore == null) return Failed(VolumeNotReadBefore("fare il loft"));

            var sketches = model.SketchManager;
            IFeature s1, s2;
            try
            {
                // primo cerchio, sul piano scelto
                if (!SelectPlane(model, plane))
                    return Failed("Non ho trovato il piano «" + plane + "» nell'albero.");
                // EACH OF THE THREE FEATURES IS THE ONE BORN SINCE ITS OWN CALL
                // (Z-C21.5, finding N13, scheme S7): the first sketch, the plane
                // (what InsertRefPlane returned, else the RefPlane born) and the
                // second sketch. Until then the plane was the last feature of the
                // tree (the flat pattern on a sheet-metal part, finding M9) and
                // each sketch the last sketch of the tree, with nothing to tell a
                // second sketch that was never born from the first one.
                var beforeFirstSketch = FeatureNamesNow(model);
                sketches.InsertSketch(true);
                sketches.CreateCircleByRadius(Units.M(offsets1), 0, 0, Units.M(d1 / 2));
                sketches.InsertSketch(false);
                s1 = FeatureBorn(model, beforeFirstSketch, "ProfileFeature", out _);
                model.ClearSelection2(true);

                // A plane at the distance asked, and the second circle on it.
                // Z-C21.7 (L2-4): InsertRefPlane with the base plane selected
                // by name with mark 0, exactly the loft L2 of the InsertRefPlane
                // probe of Z-C21.3 (43980.736 mm3, the same volume as on the
                // CreatePlaneAtOffset3 plane it replaces, L1). Guide pages and
                // numbers in ApiHelpers.InsertOffsetPlane.
                var basePlane = FindBasePlane(model, plane);
                if (basePlane == null || !ApiHelpers.SelectOffsetPlaneBase(model, basePlane))
                    return Failed("Non ho trovato il piano «" + plane + "» nell'albero.");
                var beforePlane = FeatureNamesNow(model);
                var planeReturned = ApiHelpers.InsertOffsetPlane(model, distance) as IFeature;
                int planesNow = CountPlanes(model);
                if (planesBefore < 0 || planesNow < 0)
                    return Failed(
                        "Non riesco a contare i piani dell'albero, e senza non so dire se il " +
                        "piano del secondo cerchio e' nato." + WithoutSketch(model));
                if (planesNow <= planesBefore)
                    return Failed(
                        "Il piano del secondo cerchio non e' stato creato, e nell'albero non " +
                        "ne e' comparso nessuno.");
                IFeature newPlane = planeReturned ?? FeatureBorn(model, beforePlane, "RefPlane", out _);
                var beforeSecondSketch = FeatureNamesNow(model);
                model.ClearSelection2(true);
                if (newPlane == null || !newPlane.Select2(false, 0))
                    return Failed("Non sono riuscito a selezionare il piano appena creato.");
                sketches.InsertSketch(true);
                sketches.CreateCircleByRadius(Units.M(offsets2), 0, 0, Units.M(d2 / 2));
                sketches.InsertSketch(false);
                s2 = FeatureBorn(model, beforeSecondSketch, "ProfileFeature", out _);
                model.ClearSelection2(true);
            }
            catch (Exception ex)
            {
                return Failed("I due cerchi non sono stati disegnati: " + ex.Message);
            }
            if (s1 == null || s2 == null)
                return Failed("Non ritrovo i due schizzi appena fatti.");

            model.ClearSelection2(true);
            // MARCA 1 PER TUTTI E DUE I PROFILI, e l'ORDINE conta: il loft va
            // dal primo selezionato al secondo.
            if (!s1.Select2(true, SelectionMarks.LoftProfile) || !s2.Select2(true, SelectionMarks.LoftProfile))
                return Failed("Non sono riuscito a selezionare i due profili.");

            IFeature feature;
            try
            {
                feature = model.FeatureManager.InsertProtrusionBlend2(
                    Closed: false, KeepTangency: true, ForceNonRational: false,
                    TessToleranceFactor: 0.1,
                    StartMatchingType: 0, EndMatchingType: 0,
                    StartTangentLength: 0, EndTangentLength: 0,
                    StartTangentDir: false, EndTangentDir: false,
                    IsThinBody: false, Thickness1: 0, Thickness2: 0, ThinType: 0,
                    Merge: true, UseFeatScope: true, UseAutoSelect: true,
                    GuideCurveInfluence: 0) as IFeature;
            }
            catch (Exception ex) { return Failed("Il loft non e' riuscito: " + ex.Message); }

            if (feature == null)
                return Failed(
                    "Il loft non e' stato creato, e SOLIDWORKS non dice perche'. Di solito e' " +
                    "uno dei due profili che non si chiude, o due profili che si " +
                    "attraversano.");

            // I corpi si leggono UNA volta (prima tre chiamate per lo stesso
            // numero), e il volume dopo puo' non leggersi (voce Z-C8).
            int bodiesAfter = CountSolidBodies(model);
            double? volumeAfter = VolumeMm3(model);
            if (volumeAfter == null)
                return Succeeded(feature, new Dictionary<string, object>
                {
                    ["diameter1_mm"] = d1,
                    ["diameter2_mm"] = d2,
                    ["distance_mm"] = distance,
                    ["material_added_mm3"] = null,
                    ["bodies"] = NumberOrNull(bodiesAfter),
                    ["planes_added"] = AddedPlanes(model, planesBefore),
                    ["volume_not_reread"] = NoteVolumeNotReread,
                });
            double added = volumeAfter.Value - volumeBefore.Value;
            if (added <= 0)
                return Failed(
                    "Il loft non ha aggiunto materiale. Ritiralo — " + Sentences.HowToWithdrawIt + ".");

            // Il tronco di cono: pi*h/3*(R^2 + R*r + r^2). Vale solo se i due
            // cerchi sono coassiali; sfalsandoli viene un tronco obliquo, che
            // per Cavalieri ha lo stesso volume — ma la superficie del loft e'
            // un'altra cosa, e il numero si scosta di piu'.
            double R = d1 / 2, r = d2 / 2;
            double theoretical = Math.PI * Math.Abs(distance) / 3.0 * (R * R + R * r + r * r);
            double deviation = Math.Abs(added - theoretical) / theoretical;

            var measurements = new Dictionary<string, object>
            {
                ["diameter1_mm"] = d1,
                ["diameter2_mm"] = d2,
                ["distance_mm"] = distance,
                ["material_added_mm3"] = Math.Round(added, 3),
                ["volume_cone_frustum_mm3"] = Math.Round(theoretical, 3),
                ["deviation_from_cone_frustum"] = Math.Round(deviation, 8),
                ["bodies"] = NumberOrNull(bodiesAfter),
                ["planes_added"] = AddedPlanes(model, planesBefore),
                // NON E' UN CONO, ED E' MISURATO. Vedi la nota in testa.
                ["note_on_volume"] =
                    "La superficie di un loft fra due cerchi e' un'APPROSSIMAZIONE A SPLINE " +
                    "della conica, non una conica: il volume si scosta dalla formula del " +
                    "tronco di cono di qualche millesimo di per cento, sempre in difetto. " +
                    "Se ti serve il cono esatto, usa «revolve» su un trapezio.",
            };
            if (deviation > 0.005)
                return Failed(string.Format(
                    CultureInfo.InvariantCulture,
                    "Il loft e' nato ma il volume non torna: dal tronco di cono verrebbe " +
                    "{0:0.###} mm3 e ne sono stati aggiunti {1:0.###}, cioe' {2:0.###}% di " +
                    "scarto. Un loft si scosta dalla formula di qualche millesimo di per " +
                    "cento, non di tanto: qui c'e' dell'altro. Ritiralo — " +
                    Sentences.HowToWithdrawIt + ".",
                    theoretical, added, deviation * 100));

            if (bodiesBefore > 0 && bodiesAfter >= 0 && bodiesAfter != bodiesBefore)
                measurements["warning_bodies"] =
                    "I corpi sono passati da " + bodiesBefore + " a " + bodiesAfter +
                    ": il loft non si e' unito a quello che c'era.";

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agente] loft Ø{0:0.##} -> Ø{1:0.##} su {2:0.##} mm: {3:0.##} mm3 " +
                "(tronco di cono {4:0.##})", d1, d2, distance, added, theoretical));

            return Succeeded(feature, measurements);
        }

        // =================================================================

        private static List<double[]> Points(Dictionary<string, object> a, string key)
        {
            object v;
            if (a == null || !a.TryGetValue(key, out v) || v == null) return null;
            var e = v as System.Collections.IEnumerable;
            if (e == null || v is string) return null;
            var outside = new List<double[]>();
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
                outside.Add(n.ToArray());
            }
            return outside;
        }

        private static double DistanceBetweenPoints(double[] p, double[] q)
        {
            double dx = q[0] - p[0], dy = q[1] - p[1], dz = q[2] - p[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
