using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// La creazione guidata fori: un foro che e' un foro, non un taglio tondo.
    ///
    /// PERCHE' NON BASTA UN TAGLIO CILINDRICO
    /// Fino alla 0.29.1 «foro» voleva dire uno schizzo circolare e un
    /// FeatureCut4. Geometricamente somiglia; come lavorazione non e' la
    /// stessa cosa, e si perdono sette informazioni che qualcuno a valle usa:
    /// il richiamo del foro nel disegno, la filettatura, il diametro di norma,
    /// il cono della punta in fondo a un foro cieco, la possibilita' di
    /// passare da M8 a M10 cambiando una voce, il riconoscimento da parte di
    /// CAM e preventivatori, e l'accoppiamento della viteria in assieme.
    /// Il committente lo ha chiesto per nome: «tutti i fori devono essere
    /// fatti possibilmente con la funzione creazione guidata fori usando tutte
    /// le impostazioni che fornisce».
    ///
    /// LA RICETTA, MISURATA IL 9 SETTEMBRE 2026 SU SOLIDWORKS 2021 SP5
    /// Chiamare HoleWizard5 e basta NON FUNZIONA, e non fallisce: costruisce
    /// un foro da 50.8 mm — due pollici — con il nome giusto nell'albero
    /// («Foro filettato M8»). Verificato sul VOLUME ASPORTATO, non sulle
    /// facce, che ingannano.
    ///
    /// Quello che funziona, in quest'ordine esatto:
    ///
    ///  1. si crea SEMPRE passante. HoleWizard5 applica gia' lo standard
    ///     (dopo la chiamata la definizione dice ISO, M8, filetto 8) ma lascia
    ///     a zero ThruTapDrillDiameter, ed e' quello zero a produrre il foro
    ///     da due pollici;
    ///  2. IWizardHoleFeatureData2.ChangeStandard(norma, elemento, taglia)
    ///     riempie i campi mancanti;
    ///  3. si scrive Type (swWzdHoleTypes_e). E' il campo che comanda davvero:
    ///     con swTapThru (33) un M10 usciva ancora da due pollici, con
    ///     swTapThruCosmeticThread (48) tutte le taglie da M3 a M24 tolgono
    ///     esattamente il volume del preforo di norma;
    ///  4. ModifyDefinition;
    ///  5. se il foro e' CIECO, un SECONDO giro di ModifyDefinition. Non e'
    ///     un capriccio: ChangeStandard riempie solo le varianti «Thru» dei
    ///     campi e lascia a zero le gemelle cieche (HoleDiameter, HoleDepth,
    ///     TapDrillDepth), e un cieco creato in un colpo solo esce come
    ///     capita. Creando passante e poi modificando, i volumi tornano al
    ///     centesimo, cono della punta compreso;
    ///  6. le posizioni si scrivono nello schizzo di posizionamento DENTRO
    ///     EditSketch, con AddToDB acceso e la deduzione spenta. Fuori dalla
    ///     modifica SetCoords restituisce False; con la deduzione accesa i
    ///     punti nuovi si agganciano al primo e l'errore del primo si propaga
    ///     a tutti. Con gli accorgimenti: errore 0.0000 mm su quattro fori.
    ///
    /// Volumi misurati che questa ricetta produce (piastra da 20, foro al
    /// centro): M3 98.2, M4 171.1, M5 277.1, M6 392.7, M8 726.3, M10 1134.9,
    /// M12 1634.3, M14 2261.9, M16 3078.8, M20 4810.6, M24 6927.2 mm3 —
    /// cioe' pi*r^2*spessore sul preforo ISO, esatti.
    /// </summary>
    public partial class OperazioniSw
    {
        /// <summary>
        /// I quattro generi di foro che l'operazione sa fare, con il tipo di
        /// elemento ISO da cui prendere le misure e le due varianti di Type.
        ///
        /// I numeri sono swWzdGeneralHoleTypes_e,
        /// swWzdHoleStandardFastenerTypes_e e swWzdHoleTypes_e, letti per
        /// riflessione sull'interop 29.5.1.1.
        /// </summary>
        private struct GenereDiForo
        {
            public int Generico;      // swWzdGeneralHoleTypes_e
            public int Elemento;      // swWzdHoleStandardFastenerTypes_e
            public int TipoPassante;  // swWzdHoleTypes_e
            public int TipoCieco;     // swWzdHoleTypes_e
            public string Umano;
        }

        private static readonly Dictionary<string, GenereDiForo> GeneriDiForo =
            new Dictionary<string, GenereDiForo>(StringComparer.OrdinalIgnoreCase)
        {
            ["filettato"] = new GenereDiForo
            {
                Generico = (int)swWzdGeneralHoleTypes_e.swWzdTap,
                Elemento = (int)swWzdHoleStandardFastenerTypes_e.swStandardISOTappedHole,
                TipoPassante = (int)swWzdHoleTypes_e.swTapThruCosmeticThread,
                TipoCieco = (int)swWzdHoleTypes_e.swTapBlindCosmeticThread,
                Umano = "filettato",
            },
            ["passaggio"] = new GenereDiForo
            {
                Generico = (int)swWzdGeneralHoleTypes_e.swWzdHole,
                Elemento = (int)swWzdHoleStandardFastenerTypes_e.swStandardISOScrewClearances,
                TipoPassante = (int)swWzdHoleTypes_e.swHoleThru,
                TipoCieco = (int)swWzdHoleTypes_e.swHoleBlind,
                Umano = "di passaggio per vite",
            },
            ["lamato"] = new GenereDiForo
            {
                Generico = (int)swWzdGeneralHoleTypes_e.swWzdCounterBore,
                Elemento = (int)swWzdHoleStandardFastenerTypes_e.swStandardISOSocketHeadCap,
                TipoPassante = (int)swWzdHoleTypes_e.swCounterBoreThru,
                TipoCieco = (int)swWzdHoleTypes_e.swCounterBoreBlind,
                Umano = "lamato per vite a testa cilindrica ISO 4762",
            },
            ["svasato"] = new GenereDiForo
            {
                Generico = (int)swWzdGeneralHoleTypes_e.swWzdCounterSink,
                Elemento = (int)swWzdHoleStandardFastenerTypes_e.swStandardISOSocketCTSKFlatHead,
                TipoPassante = (int)swWzdHoleTypes_e.swCounterSinkThru,
                TipoCieco = (int)swWzdHoleTypes_e.swCounterSinkBlind,
                Umano = "svasato per vite a testa svasata piana ISO 10642",
            },
        };

        private const int NormaISO = (int)swWzdHoleStandards_e.swStandardISO;

        /// <summary>Quanti fori al massimo in una feature sola.</summary>
        private const int MassimoFori = 60;

        private Esito ForoGuidato(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var faccia = RisolviFaccia(stato, a, Str(a, "faccia_uid", ""), out string motivo);
            if (faccia == null) return Fallito(motivo);

            double[] assi = AssiDellaFaccia(faccia, out double offsetM, out string errore);
            if (assi == null) return Fallito(errore);

            string genereNome = Str(a, "tipo", "passaggio");
            GenereDiForo genere;
            if (!GeneriDiForo.TryGetValue(genereNome, out genere))
                return Fallito(
                    "Non conosco il tipo di foro «" + genereNome + "». " +
                    "So fare: filettato, passaggio, lamato, svasato.");

            string taglia = (Str(a, "taglia", "") ?? "").Trim();
            if (taglia.Length == 0)
                return Fallito("Manca la taglia del foro (per esempio «M8»).");

            bool passante = Bool(a, "passante", true);
            double profondita = Num(a, "profondita_mm", 0);
            if (!passante && profondita <= 0)
                return Fallito(
                    "Un foro cieco vuole la profondita'. Dimmela, oppure di' che " +
                    "e' passante.");

            var posizioni = PosizioniDeiFori(a, out errore);
            if (posizioni == null) return Fallito(errore);

            // ── 1. si crea PASSANTE, sempre ──────────────────────────────
            modello.ClearSelection2(true);
            if (!SelezionaFaccia(faccia))
                return Fallito("Non sono riuscito a selezionare la faccia.");

            IFeature feature;
            try
            {
                feature = modello.FeatureManager.HoleWizard5(
                    GenericHoleType: genere.Generico,
                    StandardIndex: NormaISO,
                    FastenerTypeIndex: genere.Elemento,
                    SSize: taglia,
                    EndType: (short)swEndConditions_e.swEndCondThroughAll,
                    Diameter: 0, Depth: 0, Length: 0,
                    Value1: 0, Value2: 0, Value3: 0, Value4: 0, Value5: 0, Value6: 0,
                    Value7: 0, Value8: 0, Value9: 0, Value10: 0, Value11: 0, Value12: 0,
                    ThreadClass: "", RevDir: false,
                    FeatureScope: false, AutoSelect: true,
                    AssemblyFeatureScope: false, AutoSelectComponents: false,
                    PropagateFeatureToParts: false) as IFeature;
            }
            catch (Exception ex)
            {
                return Fallito("La creazione guidata fori non e' riuscita: " + ex.Message);
            }

            if (feature == null)
                return Fallito(
                    "SolidWorks non ha accettato la taglia «" + taglia + "» per un foro " +
                    genere.Umano + " secondo ISO. Le taglie metriche si scrivono " +
                    "«M3», «M4», «M5», «M6», «M8», «M10», «M12», «M14», «M16», «M20», " +
                    "«M24» — con la M maiuscola e senza spazi.");

            // ── 2-4. lo standard, il tipo, la modifica ───────────────────
            if (!SistemaIlForo(modello, feature, genere, taglia, genere.TipoPassante,
                               false, 0, out errore))
                return Fallito(errore);

            // ── 5. il cieco, in un secondo giro ──────────────────────────
            if (!passante &&
                !SistemaIlForo(modello, feature, genere, taglia, genere.TipoCieco,
                               true, profondita, out errore))
                return Fallito(errore);

            // ── 6. le posizioni ──────────────────────────────────────────
            if (!PosizionaIFori(modello, feature, assi, offsetM, posizioni, out errore))
                return Fallito(errore);

            try { modello.EditRebuild3(); } catch { }

            // ── 7. si controlla che i fori siano dove sono stati chiesti ──
            var misure = new Dictionary<string, object>
            {
                ["tipo"] = genere.Umano,
                ["taglia"] = taglia,
                ["fori"] = posizioni.Count,
                ["passante"] = passante,
            };
            if (!passante) misure["profondita_mm"] = profondita;

            var definizione = feature.GetDefinition() as IWizardHoleFeatureData2;
            double diametroForo = 0;
            if (definizione != null)
            {
                diametroForo = Math.Max(
                    Math.Max(definizione.ThruTapDrillDiameter, definizione.TapDrillDiameter),
                    Math.Max(definizione.ThruHoleDiameter, definizione.HoleDiameter)) * 1000.0;
                if (diametroForo > 0)
                    misure["diametro_lavorato_mm"] = Math.Round(diametroForo, 2);
                if (definizione.ThreadDiameter > 0)
                    misure["filetto_mm"] = Math.Round(definizione.ThreadDiameter * 1000, 2);
                if (definizione.CounterBoreDiameter > 0)
                    misure["lamatura_mm"] =
                        Math.Round(definizione.CounterBoreDiameter * 1000, 2) + "x" +
                        Math.Round(definizione.CounterBoreDepth * 1000, 2);
            }

            // TRE ESITI, NON DUE. «Non ho potuto guardare» e' una risposta
            // diversa da «ho guardato e va bene», e finche' erano la stessa il
            // banco stampava «posizioni verificate» su un campo che non voleva
            // dire niente. Vedi GiudizioPosizioni per com'e' andata.
            //
            // Non verificabile NON e' un rifiuto: la lavorazione e' riuscita, e'
            // il controllo che non c'e' stato. Si dice, e si va avanti.
            string nonPosso;
            var assiTrovati = AssiDeiFori(feature, diametroForo, out nonPosso);

            var esito = GiudizioPosizioni.Giudica(
                assiTrovati, nonPosso, assi, offsetM, posizioni, out string dovePosizioni);

            if (esito == EsitoPosizioni.Sbagliate) return Fallito(dovePosizioni);
            if (esito == EsitoPosizioni.Verificate) misure["posizioni_verificate"] = true;
            else misure["posizioni_non_verificate"] = dovePosizioni;

            return Riuscito(feature, misure, NormaleDellaFaccia(faccia));
        }

        /// <summary>
        /// Scrive lo standard e il tipo sulla definizione, e la applica.
        ///
        /// Chiamata due volte per un foro cieco: la prima per fissare le
        /// misure di norma sul passante, la seconda per portarlo a cieco. Il
        /// secondo giro riempie a mano le gemelle cieche dei campi «Thru»,
        /// che ChangeStandard non tocca.
        /// </summary>
        private bool SistemaIlForo(
            IModelDoc2 modello, IFeature feature, GenereDiForo genere, string taglia,
            int tipo, bool cieco, double profonditaMm, out string errore)
        {
            errore = null;
            IWizardHoleFeatureData2 d;
            try { d = feature.GetDefinition() as IWizardHoleFeatureData2; }
            catch (Exception ex) { errore = "Non riesco a leggere il foro: " + ex.Message; return false; }
            if (d == null) { errore = "Non riesco a leggere la definizione del foro."; return false; }

            bool accesso = false;
            try { accesso = d.AccessSelections(modello, null); }
            catch (Exception ex) { errore = "Non riesco ad aprire il foro: " + ex.Message; return false; }
            if (!accesso) { errore = "SolidWorks non mi lascia modificare il foro appena creato."; return false; }

            try
            {
                if (!cieco && !d.ChangeStandard(NormaISO, genere.Elemento, taglia))
                {
                    try { d.ReleaseSelectionAccess(); } catch { }
                    errore =
                        "La taglia «" + taglia + "» non c'e' nella tabella ISO dei fori " +
                        genere.Umano + ".";
                    return false;
                }
                d.Type = tipo;

                if (cieco)
                {
                    double p = profonditaMm / 1000.0;
                    d.EndCondition = (int)swEndConditions_e.swEndCondBlind;
                    d.Depth = p;
                    // Le gemelle cieche: senza, il foro esce come capita.
                    if (d.HoleDiameter <= 0 && d.ThruHoleDiameter > 0)
                        d.HoleDiameter = d.ThruHoleDiameter;
                    if (d.HoleDiameter > 0) d.HoleDepth = p;
                    if (d.TapDrillDiameter <= 0 && d.ThruTapDrillDiameter > 0)
                        d.TapDrillDiameter = d.ThruTapDrillDiameter;
                    if (d.TapDrillDiameter > 0)
                    {
                        d.TapDrillDepth = p;
                        // Il filetto non arriva in fondo al preforo: e' cosi'
                        // che si fila un foro cieco, e la punta lascia il cono.
                        d.ThreadDepth = p * 0.7;
                        d.ThreadEndCondition =
                            (int)swWzdHoleThreadEndCondition_e.swEndThreadTypeBLIND;
                    }
                }
            }
            catch (Exception ex)
            {
                try { d.ReleaseSelectionAccess(); } catch { }
                errore = "Non riesco a impostare il foro: " + ex.Message;
                return false;
            }

            bool applicato;
            try { applicato = feature.ModifyDefinition(d, modello, null); }
            catch (Exception ex)
            {
                try { d.ReleaseSelectionAccess(); } catch { }
                errore = "La modifica del foro non e' riuscita: " + ex.Message;
                return false;
            }
            if (!applicato)
            {
                try { d.ReleaseSelectionAccess(); } catch { }
                errore = "SolidWorks non ha applicato le misure di norma al foro.";
                return false;
            }
            return true;
        }

        /// <summary>Le posizioni chieste, in mm del modello sui due assi della faccia.</summary>
        private static List<double[]> PosizioniDeiFori(
            Dictionary<string, object> a, out string errore)
        {
            errore = null;
            var fuori = new List<double[]>();

            object grezzo;
            if (a != null && a.TryGetValue("posizioni", out grezzo) && grezzo != null)
            {
                var lista = grezzo as System.Collections.IEnumerable;
                if (lista == null) { errore = "Le posizioni dei fori non sono leggibili."; return null; }
                int i = 0;
                foreach (var voce in lista)
                {
                    i++;
                    var m = voce as Dictionary<string, object>;
                    if (m == null) { errore = "La posizione " + i + " non e' leggibile."; return null; }
                    double x, y;
                    if (!NumObbligatorio(m, "x_mm", out x, out errore)) return null;
                    if (!NumObbligatorio(m, "y_mm", out y, out errore)) return null;
                    fuori.Add(new[] { x, y });
                }
            }
            else
            {
                // Un foro solo si puo' chiedere anche con x_mm/y_mm in piano,
                // come fa taglio_cilindrico: e' la forma che il modello
                // conosce gia'.
                double x, y;
                if (!NumObbligatorio(a, "x_mm", out x, out errore)) return null;
                if (!NumObbligatorio(a, "y_mm", out y, out errore)) return null;
                fuori.Add(new[] { x, y });
            }

            if (fuori.Count == 0) { errore = "Nessuna posizione: non c'e' foro da fare."; return null; }
            if (fuori.Count > MassimoFori)
            {
                errore = "Sono " + fuori.Count + " fori in una feature sola: piu' di " +
                         MassimoFori + " non li faccio, meglio dividerli.";
                return null;
            }

            // Due fori nello stesso punto sono un errore di chi ha scritto il
            // piano, e SolidWorks li accetterebbe sovrapponendoli.
            for (int i = 0; i < fuori.Count; i++)
                for (int j = i + 1; j < fuori.Count; j++)
                    if (Math.Abs(fuori[i][0] - fuori[j][0]) < 0.01 &&
                        Math.Abs(fuori[i][1] - fuori[j][1]) < 0.01)
                    {
                        errore = "I fori " + (i + 1) + " e " + (j + 1) +
                                 " sono nello stesso punto.";
                        return null;
                    }
            return fuori;
        }

        /// <summary>
        /// Sposta il punto che SolidWorks ha messo da solo e ne aggiunge uno
        /// per ogni altro foro.
        ///
        /// Tre accorgimenti, tutti e tre necessari e tutti e tre misurati:
        ///   - si sta DENTRO EditSketch: fuori, SetCoords torna False;
        ///   - AddToDB acceso: la geometria entra alle coordinate scritte
        ///     invece che dove il motore di aggancio la porta;
        ///   - deduzione spenta: altrimenti i punti nuovi prendono relazioni
        ///     di allineamento col primo, e l'errore del primo diventa
        ///     l'errore di tutti.
        /// </summary>
        private bool PosizionaIFori(
            IModelDoc2 modello, IFeature feature, double[] assi, double offsetM,
            List<double[]> posizioni, out string errore)
        {
            errore = null;
            var featSchizzo = SchizzoDiPosizionamento(feature);
            if (featSchizzo == null)
            {
                errore = "Non trovo lo schizzo che tiene la posizione dei fori.";
                return false;
            }
            var schizzo = featSchizzo.GetSpecificFeature2() as ISketch;
            if (schizzo == null) { errore = "Lo schizzo dei fori non e' leggibile."; return false; }

            IMathTransform trasformazione;
            IMathUtility matematica;
            try
            {
                trasformazione = schizzo.ModelToSketchTransform;
                matematica = _sw.GetMathUtility() as IMathUtility;
            }
            catch (Exception ex)
            {
                errore = "Non riesco a leggere l'orientamento dello schizzo: " + ex.Message;
                return false;
            }
            if (trasformazione == null || matematica == null)
            {
                errore = "SolidWorks non mi da' l'orientamento dello schizzo dei fori.";
                return false;
            }

            var gestore = modello.SketchManager;
            bool addToDb = gestore.AddToDB;
            bool deduzione = gestore.AutoInference;
            bool mostra = gestore.DisplayWhenAdded;
            bool inModifica = false;
            try
            {
                modello.ClearSelection2(true);
                if (!featSchizzo.Select2(false, 0))
                {
                    errore = "Non riesco a selezionare lo schizzo dei fori.";
                    return false;
                }
                modello.EditSketch();
                inModifica = true;
                gestore.AddToDB = true;
                gestore.AutoInference = false;
                gestore.DisplayWhenAdded = false;

                var punti = (schizzo.GetSketchPoints2() as object[]) ?? new object[0];
                if (punti.Length == 0)
                {
                    errore = "Lo schizzo dei fori e' vuoto: non so dove metterli.";
                    return false;
                }

                var primo = NelloSchizzo(matematica, trasformazione, assi, offsetM, posizioni[0]);
                if (primo == null || !(punti[0] as ISketchPoint).SetCoords(primo[0], primo[1], 0))
                {
                    errore = "Non sono riuscito a mettere il primo foro dove chiesto.";
                    return false;
                }

                for (int i = 1; i < posizioni.Count; i++)
                {
                    var p = NelloSchizzo(matematica, trasformazione, assi, offsetM, posizioni[i]);
                    if (p == null || gestore.CreatePoint(p[0], p[1], 0) == null)
                    {
                        errore = "Non sono riuscito ad aggiungere il foro " + (i + 1) + ".";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                errore = "Il posizionamento dei fori non e' riuscito: " + ex.Message;
                return false;
            }
            finally
            {
                gestore.AddToDB = addToDb;
                gestore.AutoInference = deduzione;
                gestore.DisplayWhenAdded = mostra;
                if (inModifica) { try { gestore.InsertSketch(true); } catch { } }
                try { modello.ClearSelection2(true); } catch { }
            }
        }

        private static double[] NelloSchizzo(
            IMathUtility matematica, IMathTransform trasformazione,
            double[] assi, double offsetM, double[] posizioneMm)
        {
            try
            {
                var nelModello = new double[3];
                nelModello[(int)assi[0]] = Unita.M(posizioneMm[0]);
                nelModello[(int)assi[1]] = Unita.M(posizioneMm[1]);
                nelModello[(int)assi[2]] = offsetM;
                var punto = matematica.CreatePoint(nelModello) as IMathPoint;
                var dentro = punto?.MultiplyTransform(trasformazione) as IMathPoint;
                var v = dentro?.ArrayData as double[];
                return (v == null || v.Length < 2) ? null : v;
            }
            catch { return null; }
        }

        /// <summary>
        /// Fra le sotto-feature di un foro guidato ci sono due schizzi: quello
        /// del profilo di rivoluzione, pieno di segmenti, e quello di
        /// posizionamento, fatto di soli punti. Si prende quello con meno
        /// segmenti, che e' un invariante e non un nome localizzato.
        /// </summary>
        private static IFeature SchizzoDiPosizionamento(IFeature foro)
        {
            IFeature migliore = null;
            int meno = int.MaxValue;
            try
            {
                IFeature s = foro.IGetFirstSubFeature();
                while (s != null)
                {
                    var sk = s.GetSpecificFeature2() as ISketch;
                    if (sk != null)
                    {
                        var seg = sk.GetSketchSegments() as object[];
                        // ZERO-E-ZERO: uno schizzo di soli punti non ha segmenti, e
                        // null e' proprio lo schizzo di posizionamento che si cerca.
                        int n = seg == null ? 0 : seg.Length;
                        if (n < meno) { meno = n; migliore = s; }
                    }
                    s = s.IGetNextSubFeature();
                }
            }
            catch { }
            return migliore;
        }

        /// <summary>
        /// Raccoglie gli assi delle facce cilindriche del diametro atteso.
        ///
        /// SI GUARDA IL PEZZO, NON LA DEFINIZIONE. La definizione l'abbiamo
        /// scritta noi: rileggerla direbbe soltanto che l'abbiamo scritta.
        ///
        /// QUESTA FUNZIONE NON GIUDICA, RACCOGLIE. E' l'unica meta' che ha
        /// bisogno di SOLIDWORKS; il giudizio sta in
        /// <see cref="GiudizioPosizioni"/>, che si prova a tavolino.
        ///
        /// Quando non riesce a raccogliere, lo DICE in <paramref name="nonPosso"/>
        /// invece di tornare una lista vuota che somiglia a «nessun foro
        /// fuori posto». La versione precedente tornava `null` — cioe' «tutto
        /// a posto» — in quattro strade in cui non aveva guardato niente, e chi
        /// la chiamava scriveva `posizioni_verificate = true` senza condizione.
        /// </summary>
        private List<double[]> AssiDeiFori(
            IFeature feature, double diametroMm, out string nonPosso)
        {
            nonPosso = null;

            if (diametroMm <= 0)
            {
                nonPosso = "SOLIDWORKS non dichiara un diametro per questa lavorazione: " +
                           "non ho un termine di confronto";
                return null;
            }

            var assiTrovati = new List<double[]>();
            try
            {
                var facce = feature.GetFaces() as object[];
                if (facce == null)
                {
                    nonPosso = "la lavorazione non espone le sue facce";
                    return null;
                }
                foreach (var o in facce)
                {
                    var superficie = (o as IFace2)?.IGetSurface();
                    if (superficie == null || !superficie.IsCylinder()) continue;
                    var par = superficie.CylinderParams as double[];
                    if (par == null || par.Length < 7) continue;
                    if (Math.Abs(par[6] * 2000.0 - diametroMm) > 0.05) continue;
                    assiTrovati.Add(par);
                }
            }
            catch (Exception ex)
            {
                nonPosso = "non sono riuscito a leggere le facce della lavorazione: " + ex.Message;
                return null;
            }

            return assiTrovati;
        }
    }
}
