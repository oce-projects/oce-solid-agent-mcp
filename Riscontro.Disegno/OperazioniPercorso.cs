using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// TUBI E TRANSIZIONI: sweep e loft. (voce GP17)
    ///
    /// Un bocchettone passacavo, un manico, una transizione fra due sezioni.
    /// Non e' il lavoro quotidiano di un'officina prismatica, ma senza queste
    /// due il catalogo non sa fare niente che non sia dritto.
    ///
    /// ── IL PROFILO E' UN NUMERO, NON UNO SCHIZZO ──────────────────────
    ///
    /// `InsertProtrusionSwept4` ha **`CircularProfile` + `CircularProfileDiameter`**:
    /// con quelli lo schizzo del profilo non serve, basta il percorso. E' la
    /// forma giusta per questo progetto — un diametro e' un numero che il
    /// modello linguistico puo' dire, uno schizzo di profilo sarebbe una cosa
    /// che non ha modo di indirizzare.
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
    /// che non c'e'. `rivoluzione` sullo stesso trapezio da il cono esatto, e
    /// il banco tiene i due numeri accanto apposta.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito SweepCircolare(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            double diametro = Num(a, "diametro_mm", 0);
            if (diametro <= 0)
                return Fallito("Manca «diametro_mm», o e' zero: un tubo di diametro nullo non e' un tubo.");

            var punti = Punti(a, "percorso_mm");
            if (punti == null || punti.Count < 2)
                return Fallito(
                    "«percorso_mm» dev'essere un elenco di almeno due punti, ognuno tre " +
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

            var percorso = UltimoSchizzoFeature(modello);
            if (percorso == null)
                return Fallito("Non ritrovo lo schizzo del percorso appena fatto." + SenzaLoSchizzo(modello));

            modello.ClearSelection2(true);
            // MARCA 4: il percorso. Vedi la tabella nella nota in testa.
            if (!percorso.Select2(true, 4))
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
                    ["diametro_mm"] = diametro,
                    ["punti_percorso"] = punti.Count,
                    ["lunghezza_percorso_mm"] = Math.Round(lunghezza, 3),
                    ["materiale_aggiunto_mm3"] = null,
                    ["corpi"] = NumeroOppureNull(corpiDopo),
                    ["volume_non_riletto"] = NotaVolumeNonRiletto,
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
                ["diametro_mm"] = diametro,
                ["punti_percorso"] = punti.Count,
                ["lunghezza_percorso_mm"] = Math.Round(lunghezza, 3),
                ["materiale_aggiunto_mm3"] = Math.Round(aggiunto, 3),
                ["volume_teorico_mm3"] = Math.Round(teorico, 3),
                ["percorso_dritto"] = dritto,
                ["corpi"] = NumeroOppureNull(corpiDopo),
            };
            if (dritto)
            {
                double scarto = Math.Abs(aggiunto - teorico) / teorico;
                misure["scarto_dal_teorico"] = Math.Round(scarto, 8);
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
                misure["nota_sul_volume"] =
                    "Il percorso ha dei gomiti: nei gomiti il materiale si sovrappone, " +
                    "quindi il volume vero e' MINORE di area per lunghezza. Il teorico qui " +
                    "sopra e' il limite superiore, non la risposta.";
            }
            if (corpiDopo >= 0 && corpiPrima > 0 && corpiDopo != corpiPrima)
                misure["attenzione_corpi"] =
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
            string piano = Str(a, "piano", "frontale");
            double d1 = Num(a, "diametro1_mm", 0);
            double d2 = Num(a, "diametro2_mm", 0);
            double distanza = Num(a, "distanza_mm", 0);
            if (d1 <= 0 || d2 <= 0)
                return Fallito(
                    "Servono «diametro1_mm» e «diametro2_mm», tutti e due maggiori di zero. " +
                    "Per una transizione da tondo a punta non c'e' questa operazione.");
            if (distanza <= 0)
                return Fallito(
                    "Manca «distanza_mm»: quanto dista il secondo cerchio dal primo. A " +
                    "distanza zero i due cerchi sono sullo stesso piano e non c'e' niente " +
                    "da raccordare.");

            double sfalsa1 = Num(a, "sfalsa1_mm", 0);
            double sfalsa2 = Num(a, "sfalsa2_mm", 0);

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
                schizzi.InsertSketch(true);
                schizzi.CreateCircleByRadius(Unita.M(sfalsa1), 0, 0, Unita.M(d1 / 2));
                schizzi.InsertSketch(false);
                s1 = UltimoSchizzoFeature(modello);

                // un piano alla distanza chiesta, e sopra il secondo cerchio
                if (!SelezionaPiano(modello, piano))
                    return Fallito("Non ho trovato il piano «" + piano + "» nell'albero.");
                modello.CreatePlaneAtOffset3(Unita.M(Math.Abs(distanza)), distanza < 0, true);
                int pianiOra = ContaPiani(modello);
                if (pianiPrima < 0 || pianiOra < 0)
                    return Fallito(
                        "Non riesco a contare i piani dell'albero, e senza non so dire se il " +
                        "piano del secondo cerchio e' nato." + SenzaLoSchizzo(modello));
                if (pianiOra <= pianiPrima)
                    return Fallito(
                        "Il piano del secondo cerchio non e' stato creato, e nell'albero non " +
                        "ne e' comparso nessuno.");
                var pianoNuovo = UltimaFeature(modello);
                modello.ClearSelection2(true);
                if (pianoNuovo == null || !pianoNuovo.Select2(false, 0))
                    return Fallito("Non sono riuscito a selezionare il piano appena creato.");
                schizzi.InsertSketch(true);
                schizzi.CreateCircleByRadius(Unita.M(sfalsa2), 0, 0, Unita.M(d2 / 2));
                schizzi.InsertSketch(false);
                s2 = UltimoSchizzoFeature(modello);
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
            if (!s1.Select2(true, 1) || !s2.Select2(true, 1))
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
                    ["diametro1_mm"] = d1,
                    ["diametro2_mm"] = d2,
                    ["distanza_mm"] = distanza,
                    ["materiale_aggiunto_mm3"] = null,
                    ["corpi"] = NumeroOppureNull(corpiDopo),
                    ["piani_aggiunti"] = PianiAggiunti(modello, pianiPrima),
                    ["volume_non_riletto"] = NotaVolumeNonRiletto,
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
                ["diametro1_mm"] = d1,
                ["diametro2_mm"] = d2,
                ["distanza_mm"] = distanza,
                ["materiale_aggiunto_mm3"] = Math.Round(aggiunto, 3),
                ["volume_tronco_di_cono_mm3"] = Math.Round(teorico, 3),
                ["scarto_dal_tronco_di_cono"] = Math.Round(scarto, 8),
                ["corpi"] = NumeroOppureNull(corpiDopo),
                ["piani_aggiunti"] = PianiAggiunti(modello, pianiPrima),
                // NON E' UN CONO, ED E' MISURATO. Vedi la nota in testa.
                ["nota_sul_volume"] =
                    "La superficie di un loft fra due cerchi e' un'APPROSSIMAZIONE A SPLINE " +
                    "della conica, non una conica: il volume si scosta dalla formula del " +
                    "tronco di cono di qualche millesimo di per cento, sempre in difetto. " +
                    "Se ti serve il cono esatto, usa «rivoluzione» su un trapezio.",
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
                misure["attenzione_corpi"] =
                    "I corpi sono passati da " + corpiPrima + " a " + corpiDopo +
                    ": il loft non si e' unito a quello che c'era.";

            _log(string.Format(CultureInfo.InvariantCulture,
                "[agente] loft Ø{0:0.##} -> Ø{1:0.##} su {2:0.##} mm: {3:0.##} mm3 " +
                "(tronco di cono {4:0.##})", d1, d2, distanza, aggiunto, teorico));

            return Riuscito(feature, misure);
        }

        // =================================================================

        /// <summary>L'ultima feature di schizzo dell'albero.</summary>
        private static IFeature UltimoSchizzoFeature(IModelDoc2 modello)
        {
            IFeature ultimo = null;
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null)
                {
                    var tipo = f.GetTypeName2();
                    if (tipo == "ProfileFeature" || tipo == "3DProfileFeature") ultimo = f;
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            try { modello.ClearSelection2(true); } catch { }
            return ultimo;
        }

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
