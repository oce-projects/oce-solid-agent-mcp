using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LA LAMIERA (voce GP18): la flangia base e lo sviluppo per il taglio.
    ///
    /// ── LA MISURA CHE L'HA RESA COSTRUIBILE, 24/9/2026 ─────────────────────
    ///
    /// `Controlli/Lamiera`: una L da 50 + 30, profonda 40, T 2, R 2, K 0,5
    /// riletti dalla lamiera. Lo sviluppo esce 80,712 mm, e il conto a mano con
    /// lo schizzo sulle facce INTERNE da' 80,712 — scarto zero; con le facce
    /// esterne 76,712. Volume piegato e sviluppato uguali. **La convenzione e'
    /// misurata, non letta**: i punti dati qui sono le facce interne, e lo
    /// spessore va fuori.
    ///
    /// ── TRE TRAPPOLE, tutte senza errore ──────────────────────────────────
    ///
    ///  1. `SetBendState(sviluppata)` risponde 0 e `GetBendState` dice
    ///     «sviluppata», e il corpo resta PIEGATO. Lo sviluppo si legge togliendo
    ///     la soppressione alla lavorazione «FlatPattern» in fondo all'albero, e
    ///     rimettendola.
    ///  2. `ExportFlatPatternView` su un pezzo MAI SALVATO apre «Salva con nome»
    ///     e la chiamata COM non torna. `sviluppo_dxf` rifiuta un pezzo senza
    ///     file, prima di chiamare.
    ///  3. Lo schizzo si disegna con `AddToDB` a vero: le relazioni che
    ///     SOLIDWORKS indovina da solo sovravincolano alla prima modifica
    ///     (misurato con `Controlli/SeguiIlBordo`).
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito FlangiaBase(IModelDoc2 modello, Dictionary<string, object> a)
        {
            string errore;
            double spessore, raggio, profondita;
            if (!NumObbligatorio(a, "spessore_mm", out spessore, out errore)) return Fallito(errore);
            if (!NumObbligatorio(a, "raggio_mm", out raggio, out errore)) return Fallito(errore);
            if (!NumObbligatorio(a, "profondita_mm", out profondita, out errore)) return Fallito(errore);
            if (spessore <= 0 || profondita <= 0)
                return Fallito("«spessore_mm» e «profondita_mm» devono essere positivi.");
            if (raggio < 0)
                return Fallito("«raggio_mm» non puo' essere negativo: e' il raggio INTERNO della piega.");

            var punti = PuntiDelProfilo(a, out errore);
            if (punti == null) return Fallito(errore);

            // UN TRATTO PIU' CORTO DEL RAGGIO NON SI PIEGA: la piega mangia R da
            // ogni lato interno. Il limite si dichiara prima di chiamare, perche'
            // dopo SOLIDWORKS darebbe una lavorazione rossa senza dire quale lato.
            for (int i = 0; i < punti.Count - 1; i++)
            {
                double l = Math.Sqrt(Math.Pow(punti[i + 1][0] - punti[i][0], 2) +
                                     Math.Pow(punti[i + 1][1] - punti[i][1], 2));
                if (l <= 0.001)
                    return Fallito("Il tratto " + (i + 1) + " e' lungo zero: due punti uguali di fila.");
                bool ha2Pieghe = i > 0 && i < punti.Count - 2;
                double serve = ha2Pieghe ? 2 * raggio : raggio;
                if (punti.Count > 2 && l <= serve)
                    return Fallito(string.Format(CultureInfo.InvariantCulture,
                        "Il tratto {0} e' lungo {1:0.###} mm e le pieghe col raggio {2:0.###} ne " +
                        "consumano {3:0.###}: non resta niente di dritto. Allunga il tratto o " +
                        "stringi il raggio.", i + 1, l, raggio, serve));
            }

            string piano = Str(a, "piano", "frontale");
            if (!SelezionaPiano(modello, piano))
                return Fallito("Non riesco a selezionare il piano «" + piano + "».");

            double? volumePrima = VolumeMm3(modello);
            modello.SketchManager.InsertSketch(true);
            bool addPrima = modello.SketchManager.AddToDB;
            try
            {
                modello.SketchManager.AddToDB = true;
                for (int i = 0; i < punti.Count - 1; i++)
                    modello.SketchManager.CreateLine(
                        Unita.M(punti[i][0]), Unita.M(punti[i][1]), 0,
                        Unita.M(punti[i + 1][0]), Unita.M(punti[i + 1][1]), 0);
            }
            finally { modello.SketchManager.AddToDB = addPrima; }
            modello.SketchManager.InsertSketch(true);

            IFeature flangia;
            try
            {
                flangia = modello.FeatureManager.InsertSheetMetalBaseFlange2(
                    Unita.M(spessore), false, Unita.M(raggio), Unita.M(profondita), 0.0, false,
                    (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind, 0,
                    null, true, (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular,
                    0.0, 0.0, 0.5, true, false, false, false) as IFeature;
            }
            catch (Exception ex) { return Fallito("La flangia base non e' nata: " + ex.Message + LasciatoSchizzo); }
            if (flangia == null)
                return Fallito("La flangia base non e' nata, e SOLIDWORKS non dice perche'." + LasciatoSchizzo);
            modello.EditRebuild3();

            // ── T, R, K RILETTI dalla lamiera, non ripetuti dagli argomenti ──
            var dati = DatiDellaLamiera(modello);
            if (dati == null)
                return Fallito("La flangia c'e' ma non trovo la lavorazione «SheetMetal»: non so " +
                               "con che spessore e che fattore K e' nata. Ritirala — " + Frasi.CosiSiRitira + ".");
            double t = Unita.Mm(dati.Thickness), r = Unita.Mm(dati.BendRadius), k = dati.KFactor;
            if (Math.Abs(t - spessore) > 0.001 || Math.Abs(r - raggio) > 0.001)
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "Ho chiesto spessore {0} e raggio {1}, e la lamiera dice {2:0.###} e {3:0.###}. " +
                    "Ritirala — {4}.", spessore, raggio, t, r, Frasi.CosiSiRitira));

            double? volumePiegato = VolumeMm3(modello);
            double sviluppo = LunghezzaSviluppata(modello, out double volumeSviluppato);

            // IL CONTO A MANO, nelle due convenzioni. Misurato: con la L data
            // dall'alto verso destra lo spessore va a DESTRA del verso di
            // percorrenza e i punti sono le facce interne. Dato al contrario,
            // lo spessore va dall'altra parte e i punti diventano le facce
            // ESTERNE — lo sviluppo cambia di 2T per piega. Non si indovina: si
            // calcolano tutte e due e si dice quale combacia con SOLIDWORKS.
            double somma = 0;
            for (int i = 0; i < punti.Count - 1; i++)
                somma += Math.Sqrt(Math.Pow(punti[i + 1][0] - punti[i][0], 2) +
                                   Math.Pow(punti[i + 1][1] - punti[i][1], 2));
            int pieghe = punti.Count - 2;
            double angoli = 0;
            for (int i = 1; i < punti.Count - 1; i++) angoli += AngoloDiPiega(punti[i - 1], punti[i], punti[i + 1]);
            double interne = somma - 2 * r * pieghe + angoli * (r + k * t);
            double esterne = somma - 2 * (r + t) * pieghe + angoli * (r + k * t);
            string convenzione = double.IsNaN(sviluppo) ? "non misurata"
                : Math.Abs(sviluppo - interne) <= 0.5 ? "interne"
                : Math.Abs(sviluppo - esterne) <= 0.5 ? "esterne" : "nessuna delle due";
            double atteso = convenzione == "esterne" ? esterne : interne;

            var misure = new Dictionary<string, object>
            {
                ["spessore_mm"] = Math.Round(t, 3),
                ["raggio_mm"] = Math.Round(r, 3),
                ["fattore_k"] = Math.Round(k, 4),
                ["pieghe"] = pieghe,
                ["volume_mm3"] = NumeroOppureNull(volumePiegato - volumePrima, 2),
                ["lunghezza_sviluppo_mm"] = double.IsNaN(sviluppo) ? null : (object)Math.Round(sviluppo, 3),
                ["lunghezza_sviluppo_a_mano_mm"] = Math.Round(atteso, 3),
                ["i_punti_sono_le_facce"] = convenzione,
            };

            // IL CONTROLLO CHE DA' SENSO ALLA FAMIGLIA: lo sviluppo di SOLIDWORKS
            // contro il conto a mano. Se divergono, il DXF che andra' al laser
            // non e' quello che uno si aspetta, e lo si dice adesso.
            if (convenzione == "nessuna delle due")
                misure["attenzione"] = string.Format(CultureInfo.InvariantCulture,
                    "Lo sviluppo di SOLIDWORKS e' {0:0.###} mm e il conto a mano ne da' {1:0.###}: " +
                    "prima di mandare il DXF al taglio, controlla la tabella delle pieghe del " +
                    "modello di documento.", sviluppo, atteso);
            if (!double.IsNaN(volumeSviluppato) && volumePiegato.HasValue &&
                Math.Abs(volumeSviluppato - volumePiegato.Value) > 0.005 * volumePiegato.Value)
                misure["attenzione_volume"] = "Il volume sviluppato non e' quello piegato: lo sviluppo non e' affidabile.";

            return Riuscito(flangia, misure);
        }

        /// <summary>
        /// LA FLANGIA A SPIGOLO (voce GP18): una piega nuova su uno spigolo di
        /// bordo di una lamiera che c'e' gia'. Serve alle scatole — pieghe su
        /// spigoli di direzioni diverse, che `flangia_base` da un profilo solo non fa.
        ///
        /// ── MISURATA PRIMA DI SCRIVERLA, 25-26/9/2026 ────────────────────────
        ///
        /// `Controlli/FlangiaSpigolo`, dieci tentativi. `InsertSheetMetalEdgeFlange2`
        /// risponde null senza eccezione con gli schizzi a null, col settimo
        /// parametro (`FlangeOffsetDist`, la lunghezza) a 0 o a 20, con gli elenchi
        /// tipizzati; nasce VUOTA (volume invariato) con una linea su un piano
        /// perpendicolare allo spigolo. **Nasce e aggiunge materiale solo con un
        /// RETTANGOLO sul piano della flangia**, la base sullo spigolo: qui la
        /// faccia di testa della lamiera, che e' proprio quel piano.
        ///
        /// Misurato sulla L (T 2, R 2, K 0,5, «materiale dentro»), profilo alto 20
        /// e 30: `OffsetDistance` riletta 22 e 32 — profilo + T, allo spigolo
        /// virtuale ESTERNO — e lo sviluppo cresce di (Lo - R - T) + BA - (R + T),
        /// al millesimo. Quindi il rettangolo e' alto Lo - T, e il conto del
        /// volume in piu' e' quello sviluppo per T per la lunghezza dello spigolo.
        ///
        /// Due cose da sapere: SOLIDWORKS SOSTITUISCE lo schizzo passato con uno
        /// suo (la geometria segue il nostro, il nome no); e la lunghezza la
        /// comanda il profilo — `ModifyDefinition` su `OffsetDistance` risponde
        /// vero e non cambia niente.
        /// </summary>
        private Esito FlangiaSpigolo(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string errore;
            double lunghezza;
            if (!NumObbligatorio(a, "lunghezza_mm", out lunghezza, out errore)) return Fallito(errore);

            var dati = DatiDellaLamiera(modello);
            if (dati == null)
                return Fallito("Questo pezzo non e' di lamiera: la flangia a spigolo si attacca a una " +
                               "lamiera che c'e' gia' (fatta con «flangia_base»).");
            double t = Unita.Mm(dati.Thickness), r = Unita.Mm(dati.BendRadius), k = dati.KFactor;

            // NIENTE DI DRITTO: la piega consuma R + T dalla lunghezza misurata allo
            // spigolo esterno. Si dice prima di toccare il pezzo.
            if (lunghezza <= r + t + 0.001)
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "Una flangia lunga {0:0.###} mm allo spigolo esterno non ha niente di dritto: " +
                    "la piega col raggio {1:0.###} e lo spessore {2:0.###} ne consumano {3:0.###}. " +
                    "Chiedila piu' lunga di {3:0.###} mm.", lunghezza, r, t, r + t));

            string uid = Str(a, "spigolo_uid", "");
            var bordo = SpigoloDiBordo(a, stato, uid, t, "una flangia", out errore);
            if (bordo == null) return Fallito(errore);
            var spigolo = bordo.Spigolo; var testa = bordo.Testa;
            var p0 = bordo.P0; var p1 = bordo.P1; var n = bordo.Normale;
            double lungo = bordo.LungoM;
            double h = Unita.M(lunghezza - t);
            var q0 = new[] { p0[0] + n[0] * h, p0[1] + n[1] * h, p0[2] + n[2] * h };
            var q1 = new[] { p1[0] + n[0] * h, p1[1] + n[1] * h, p1[2] + n[2] * h };

            double? volumePrima = VolumeMm3(modello);
            var schizziPrima = new HashSet<string>();
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (f.GetTypeName2() == "ProfileFeature") schizziPrima.Add(f.Name);
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            modello.ClearSelection2(true);
            if (!(testa as IEntity).Select4(false, null))
                return Fallito("Non riesco a selezionare la faccia di testa dello spigolo «" + uid + "».");
            modello.SketchManager.InsertSketch(true);
            var sk = modello.SketchManager.ActiveSketch as ISketch;
            var matematica = _sw.GetMathUtility() as IMathUtility;
            if (sk == null || matematica == null)
                return Fallito("Lo schizzo del profilo non si e' aperto sulla faccia di testa." + SenzaLoSchizzo(modello));
            var tr = sk.ModelToSketchTransform;
            Func<double[], double[]> nelloSchizzo = p =>
                ((IMathPoint)((IMathPoint)matematica.CreatePoint(p)).MultiplyTransform(tr)).ArrayData as double[];
            var angoli = new[] { nelloSchizzo(p0), nelloSchizzo(p1), nelloSchizzo(q1), nelloSchizzo(q0) };
            bool addPrima = modello.SketchManager.AddToDB;
            try
            {
                modello.SketchManager.AddToDB = true;
                for (int i = 0; i < 4; i++)
                {
                    var da = angoli[i]; var ad = angoli[(i + 1) % 4];
                    modello.SketchManager.CreateLine(da[0], da[1], 0, ad[0], ad[1], 0);
                }
            }
            finally { modello.SketchManager.AddToDB = addPrima; }
            modello.SketchManager.InsertSketch(true);
            // LO SCHIZZO NUOVO NON E' L'ULTIMO DELL'ALBERO. In una lamiera la
            // lavorazione in fondo e' sempre lo sviluppo («FlatPattern»), e le
            // lavorazioni nuove entrano PRIMA di lui: misurato al primo giro di
            // banco, `FeatureByPositionReverse(0)` dava lo sviluppo. Si prende lo
            // schizzo che prima non c'era.
            IFeature schizzo = null;
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (f.GetTypeName2() == "ProfileFeature" && !schizziPrima.Contains(f.Name)) schizzo = f;
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            if (schizzo == null)
                return Fallito("Non ritrovo lo schizzo del profilo appena fatto." + SenzaLoSchizzo(modello));

            modello.ClearSelection2(true);
            IFeature flangia;
            try
            {
                int opzioni = (int)(swInsertEdgeFlangeOptions_e.swInsertEdgeFlangeUseDefaultRadius |
                                    swInsertEdgeFlangeOptions_e.swInsertEdgeFlangeUseDefaultRelief);
                flangia = modello.FeatureManager.InsertSheetMetalEdgeFlange2(
                    new Edge[] { (Edge)spigolo }, new Feature[] { (Feature)schizzo }, opzioni,
                    Math.PI / 2, Unita.M(r), (int)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside,
                    Unita.M(lunghezza), (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0.5, 0, 0,
                    (int)swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp, null) as IFeature;
            }
            catch (Exception ex) { return Fallito("La flangia a spigolo non e' nata: " + ex.Message + SenzaLoSchizzo(modello)); }
            if (flangia == null)
                return Fallito("La flangia a spigolo non e' nata, e SOLIDWORKS non dice perche'." + SenzaLoSchizzo(modello));
            modello.EditRebuild3();

            bool avviso = false; int codice = 0;
            try { codice = flangia.GetErrorCode2(out avviso); } catch { }
            if (codice != 0 && !avviso)
                return Fallito("La flangia a spigolo «" + flangia.Name + "» e' nata in errore (codice " + codice +
                               "): ritirala — " + Frasi.CosiSiRitira + ".");

            double letta = -1;
            try { var def = flangia.GetDefinition() as IEdgeFlangeFeatureData; if (def != null) letta = Unita.Mm(def.OffsetDistance); }
            catch { }
            double? volumeDopo = VolumeMm3(modello);
            double ba = Math.PI / 2 * (r + k * t);
            double sviluppoInPiu = (lunghezza - r - t) + ba - (r + t);
            double volumeAtteso = sviluppoInPiu * t * Unita.Mm(lungo);
            double? aggiunto = volumeDopo - volumePrima;

            // NATA VUOTA: e' il tentativo 9 della sonda — una «Bordo-Flangia»
            // nell'albero che non aggiunge niente. Non si conferma un pezzo
            // uguale a prima.
            if (aggiunto.HasValue && aggiunto.Value < 0.5 * volumeAtteso)
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "La flangia «{0}» e' nell'albero ma ha aggiunto {1:0.##} mm3 invece dei {2:0.##} del " +
                    "conto: non e' la flangia chiesta. Ritirala — {3}.",
                    flangia.Name, aggiunto.Value, volumeAtteso, Frasi.CosiSiRitira));

            double sviluppo = LunghezzaSviluppata(modello, out double _);
            var misure = new Dictionary<string, object>
            {
                ["lunghezza_mm"] = letta >= 0 ? (object)Math.Round(letta, 3) : null,
                ["spessore_mm"] = Math.Round(t, 3),
                ["raggio_mm"] = Math.Round(r, 3),
                ["fattore_k"] = Math.Round(k, 4),
                ["lungo_lo_spigolo_mm"] = Math.Round(Unita.Mm(lungo), 3),
                ["volume_mm3"] = NumeroOppureNull(aggiunto, 2),
                ["volume_a_mano_mm3"] = Math.Round(volumeAtteso, 2),
                ["lunghezza_sviluppo_mm"] = double.IsNaN(sviluppo) ? null : (object)Math.Round(sviluppo, 3),
            };
            if (!volumeDopo.HasValue || !volumePrima.HasValue) misure["volume_non_riletto"] = NotaVolumeNonRiletto;
            else if (Math.Abs(aggiunto.Value - volumeAtteso) > 0.005 * volumeAtteso)
                misure["attenzione"] = string.Format(CultureInfo.InvariantCulture,
                    "La flangia ha aggiunto {0:0.##} mm3 e il conto a mano ne da' {1:0.##}: prima del taglio " +
                    "controlla lo sviluppo.", aggiunto.Value, volumeAtteso);
            if (letta >= 0 && Math.Abs(letta - lunghezza) > 0.01)
                misure["attenzione_lunghezza"] = string.Format(CultureInfo.InvariantCulture,
                    "Ho chiesto {0:0.###} mm allo spigolo esterno e la flangia dice {1:0.###}.", lunghezza, letta);
            return Riuscito(flangia, misure);
        }

        /// <summary>Uno spigolo di bordo di una lamiera, con le sue due facce.</summary>
        private sealed class BordoDiLamiera
        {
            public IEdge Spigolo;
            public IFace2 Testa, Larga;
            public double[] P0, P1;
            /// <summary>La normale USCENTE della faccia larga, di lunghezza 1.</summary>
            public double[] Normale;
            public double LungoM;
        }

        /// <summary>
        /// Lo spigolo di bordo su cui si piega (flangia a spigolo, orlo): preso
        /// per firma (voce E19), dritto, fra la faccia LARGA della lamiera e una
        /// faccia di TESTA piana, larga quanto lo spessore (area = T x lunghezza).
        /// Null col motivo, che dice quale spigolo serve.
        ///
        /// IL FIANCO DI UNA LAMIERA NON E' UNA FACCIA SOLA (`prova0089 elle`,
        /// 26/9/2026): e' spezzato per tratti, e ogni tratto e' largo proprio lo
        /// spessore. Quindi anche gli spigoli lungo i fianchi sono di bordo, e lo
        /// sono davvero; non lo e' lo spigolo di tangenza di una piega, dove una
        /// faccia e' il cilindro della piega.
        /// </summary>
        private BordoDiLamiera SpigoloDiBordo(
            Dictionary<string, object> a, StatoDocumento stato, string uid, double t,
            string cosa, out string motivo)
        {
            motivo = null;
            if (string.IsNullOrWhiteSpace(uid))
            {
                motivo = "Manca «spigolo_uid»: lo spigolo di bordo della lamiera su cui piegare, " +
                         "preso dallo stato del documento (E1, E2...).";
                return null;
            }
            var firma = FirmaDelloSpigolo(
                a, stato, uid, "Lo spigolo «" + uid + "» non c'e' piu' in questo documento.", out motivo);
            if (firma == null) return null;
            var b = new BordoDiLamiera { Spigolo = firma.Entita as IEdge };

            bool dritto = false;
            try
            {
                var curva = b.Spigolo?.GetCurve() as ICurve;
                var par = b.Spigolo?.GetCurveParams3() as ICurveParamData;
                dritto = curva != null && curva.IsLine();
                b.P0 = par?.StartPoint as double[];
                b.P1 = par?.EndPoint as double[];
            }
            catch { }
            if (!dritto || b.P0 == null || b.P1 == null)
            {
                motivo = "Lo spigolo «" + uid + "» non e' dritto: " + cosa + " si piega su uno spigolo dritto.";
                return null;
            }
            b.LungoM = Math.Sqrt(Math.Pow(b.P1[0] - b.P0[0], 2) + Math.Pow(b.P1[1] - b.P0[1], 2) +
                                 Math.Pow(b.P1[2] - b.P0[2], 2));
            try
            {
                var facce = b.Spigolo.GetTwoAdjacentFaces2() as object[];
                if (facce != null && facce.Length == 2)
                {
                    double areaTesta = Unita.M(t) * b.LungoM;
                    foreach (var o in facce)
                    {
                        var f = o as IFace2;
                        if (f == null || (f.GetSurface() as ISurface)?.IsPlane() != true) continue;
                        if (Math.Abs(f.GetArea() - areaTesta) <= 0.01 * areaTesta) b.Testa = f;
                        else b.Larga = f;
                    }
                }
            }
            catch { }
            if (b.Testa == null || b.Larga == null)
            {
                motivo = string.Format(CultureInfo.InvariantCulture,
                    "Lo spigolo «{0}» non sta sul bordo della lamiera: {1} si attacca a uno spigolo fra " +
                    "la faccia larga e una faccia di testa larga quanto lo spessore ({2:0.###} mm), tutte " +
                    "e due piane. Scegli uno spigolo del bordo.", uid, cosa, t);
                return null;
            }
            var n = b.Larga.Normal as double[];
            // -1 e' «non letta» (voce Z-C8), e si rifiuta subito sotto.
            double nn = n == null || n.Length < 3 ? -1 : Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
            if (nn <= 1e-9)
            {
                motivo = "Non riesco a leggere da che parte guarda la faccia larga dello spigolo «" + uid + "».";
                return null;
            }
            b.Normale = new[] { n[0] / nn, n[1] / nn, n[2] / nn };
            return b;
        }

        /// <summary>
        /// L'ORLO (voce GP18): il bordo ripiegato su se stesso, chiuso o aperto.
        ///
        /// ── MISURATO PRIMA DI SCRIVERLO, 26/9/2026 (`prova0089 orlo`) ────────
        ///
        /// `InsertSheetMetalHem2` lavora sullo spigolo SELEZIONATO e nasce al
        /// primo colpo — nessuno schizzo, a differenza della flangia a spigolo.
        /// Sulla L (T 2, K 0,5), «dentro»:
        ///
        ///   aperto, gioco 1, lungo 10:  +9,712 di sviluppo
        ///   aperto, gioco 2, lungo 10:  +10,283
        ///   aperto, gioco 1, lungo 15:  +14,712
        ///   chiuso, lungo 10 / 15:      +9,199 / +14,199
        ///
        /// L'APERTO e' la formula della flangia con la piega a 180 gradi e il
        /// raggio interno pari a meta' del gioco: (L - R - T) + pi (R + K T) -
        /// (R + T), R = G/2 — tre misure su tre, al millesimo. Il CHIUSO sta
        /// 0,057 sopra la stessa formula con R = 0: come un raggio interno di 0,05
        /// mm, misurato con T 2 soltanto. Per questo il conto del chiuso si dice
        /// accanto alla misura, e non decide niente.
        /// </summary>
        private Esito Orlo(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string errore;
            double lunghezza;
            if (!NumObbligatorio(a, "lunghezza_mm", out lunghezza, out errore)) return Fallito(errore);
            string tipo = Str(a, "tipo", "chiuso");
            if (tipo != "chiuso" && tipo != "aperto")
                return Fallito("«tipo» e' «chiuso» o «aperto», non «" + tipo + "».");
            double gioco = Num(a, "gioco_mm");
            if (tipo == "aperto" && gioco <= 0)
                return Fallito("Un orlo aperto vuole «gioco_mm» maggiore di zero: e' lo spazio fra il " +
                               "bordo ripiegato e la lamiera. Senza gioco e' un orlo «chiuso».");

            var dati = DatiDellaLamiera(modello);
            if (dati == null)
                return Fallito("Questo pezzo non e' di lamiera: l'orlo si fa sul bordo di una lamiera che " +
                               "c'e' gia' (fatta con «flangia_base»).");
            double t = Unita.Mm(dati.Thickness), k = dati.KFactor;
            double r = tipo == "aperto" ? gioco / 2 : 0.05;
            if (lunghezza <= r + t + 0.001)
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "Un orlo lungo {0:0.###} mm non ha niente di dritto: la piega a 180 gradi e lo spessore " +
                    "ne consumano {1:0.###}. Chiedilo piu' lungo di {1:0.###} mm.", lunghezza, r + t));

            string uid = Str(a, "spigolo_uid", "");
            var bordo = SpigoloDiBordo(a, stato, uid, t, "un orlo", out errore);
            if (bordo == null) return Fallito(errore);

            double? volumePrima = VolumeMm3(modello);
            modello.ClearSelection2(true);
            if (!(bordo.Spigolo as IEntity).Select4(false, null))
                return Fallito("Non riesco a selezionare lo spigolo «" + uid + "».");
            IFeature orlo;
            try
            {
                orlo = modello.FeatureManager.InsertSheetMetalHem2(
                    tipo == "chiuso" ? (int)swHemTypes_e.swHemTypeClosed : (int)swHemTypes_e.swHemTypeOpen,
                    (int)swHemPositionTypes_e.swHemPositionTypeInside, false,
                    Unita.M(lunghezza), tipo == "aperto" ? Unita.M(gioco) : 0, 0, 0, 0,
                    null, true, (int)swSheetMetalReliefTypes_e.swSheetMetalReliefRectangular, 0, true, 0.5, 0, 0) as IFeature;
            }
            catch (Exception ex) { return Fallito("L'orlo non e' nato: " + ex.Message); }
            if (orlo == null) return Fallito("L'orlo non e' nato, e SOLIDWORKS non dice perche'.");
            modello.EditRebuild3();

            bool avviso = false; int codice = 0;
            try { codice = orlo.GetErrorCode2(out avviso); } catch { }
            if (codice != 0 && !avviso)
                return Fallito("L'orlo «" + orlo.Name + "» e' nato in errore (codice " + codice + "): ritiralo — " +
                               Frasi.CosiSiRitira + ".");

            double? volumeDopo = VolumeMm3(modello);
            double sviluppoInPiu = (lunghezza - r - t) + Math.PI * (r + k * t) - (r + t);
            double volumeAtteso = sviluppoInPiu * t * Unita.Mm(bordo.LungoM);
            double? aggiunto = volumeDopo - volumePrima;
            if (aggiunto.HasValue && aggiunto.Value < 0.5 * volumeAtteso)
                return Fallito(string.Format(CultureInfo.InvariantCulture,
                    "L'orlo «{0}» e' nell'albero ma ha aggiunto {1:0.##} mm3 invece dei {2:0.##} del conto: " +
                    "non e' l'orlo chiesto. Ritiralo — {3}.", orlo.Name, aggiunto.Value, volumeAtteso, Frasi.CosiSiRitira));

            double sviluppo = LunghezzaSviluppata(modello, out double _);
            var misure = new Dictionary<string, object>
            {
                ["tipo"] = tipo,
                ["lunghezza_mm"] = Math.Round(lunghezza, 3),
                ["spessore_mm"] = Math.Round(t, 3),
                ["fattore_k"] = Math.Round(k, 4),
                ["lungo_lo_spigolo_mm"] = Math.Round(Unita.Mm(bordo.LungoM), 3),
                ["volume_mm3"] = NumeroOppureNull(aggiunto, 2),
                ["volume_a_mano_mm3"] = Math.Round(volumeAtteso, 2),
                ["lunghezza_sviluppo_mm"] = double.IsNaN(sviluppo) ? null : (object)Math.Round(sviluppo, 3),
            };
            if (tipo == "aperto") misure["gioco_mm"] = Math.Round(gioco, 3);
            if (!volumeDopo.HasValue || !volumePrima.HasValue) misure["volume_non_riletto"] = NotaVolumeNonRiletto;
            else if (Math.Abs(aggiunto.Value - volumeAtteso) > 0.005 * volumeAtteso)
                misure["attenzione"] = string.Format(CultureInfo.InvariantCulture,
                    "L'orlo ha aggiunto {0:0.##} mm3 e il conto a mano ne da' {1:0.##}" +
                    (tipo == "chiuso" ? " (per il chiuso il conto usa un raggio interno di 0,05 mm, misurato " +
                                        "con spessore 2)" : "") + ": prima del taglio controlla lo sviluppo.",
                    aggiunto.Value, volumeAtteso);
            return Riuscito(orlo, misure);
        }

        private Esito SviluppoDxf(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var parte = modello as IPartDoc;
            if (parte == null) return Fallito("Lo sviluppo si fa su una parte.");
            if (DatiDellaLamiera(modello) == null)
                return Fallito("Questo pezzo non e' di lamiera: non ha una lavorazione «SheetMetal» da sviluppare.");

            // TRAPPOLA 2 della nota in testa: senza file, «Salva con nome» e una
            // chiamata che non torna.
            string suo = modello.GetPathName() ?? "";
            if (string.IsNullOrWhiteSpace(suo))
                return Fallito("Il pezzo non e' mai stato salvato, e su un pezzo senza file " +
                               "SOLIDWORKS apre «Salva con nome» e resta ad aspettare. Salvalo " +
                               "prima («salva_documento»).");

            string dove = Str(a, "percorso", null);
            if (string.IsNullOrWhiteSpace(dove)) dove = Path.ChangeExtension(suo, ".dxf");
            if (!string.Equals(Path.GetExtension(dove), ".dxf", StringComparison.OrdinalIgnoreCase))
                return Fallito("«percorso» deve finire in .dxf.");
            try { if (File.Exists(dove)) File.Delete(dove); }
            catch (Exception ex) { return Fallito("C'e' gia' un DXF li' e non riesco a sostituirlo: " + ex.Message); }

            bool ok;
            try
            {
                ok = parte.ExportFlatPatternView(dove,
                    (int)swExportFlatPatternViewOptions_e.swExportFlatPatternOption_RemoveBends);
            }
            catch (Exception ex) { return Fallito("Lo sviluppo non e' uscito: " + ex.Message); }
            if (!ok || !File.Exists(dove) || new FileInfo(dove).Length == 0)
                return Fallito("SOLIDWORKS dice di aver esportato lo sviluppo, ma il file «" + dove +
                               "» " + (File.Exists(dove) ? "e' vuoto." : "non c'e'."));

            // SI RILEGGE IL DXF: l'ingombro scritto nel file contro lo sviluppo
            // misurato sul pezzo. Un DXF di un'altra cosa si apre uguale.
            double sviluppo = LunghezzaSviluppata(modello, out double _);
            var ingombro = IngombroDelDxf(dove);
            var misure = new Dictionary<string, object>
            {
                ["percorso"] = dove,
                ["byte"] = new FileInfo(dove).Length,
                ["ingombro_dxf_mm"] = ingombro,
                ["lunghezza_sviluppo_mm"] = double.IsNaN(sviluppo) ? null : (object)Math.Round(sviluppo, 3),
            };
            if (ingombro != null && !double.IsNaN(sviluppo))
            {
                double lato = Math.Max(ingombro[0], ingombro[1]);
                if (Math.Abs(lato - sviluppo) > 0.5)
                    return Fallito(string.Format(CultureInfo.InvariantCulture,
                        "Il DXF e' uscito ({0}) ma il suo lato lungo e' {1:0.###} mm e lo sviluppo " +
                        "del pezzo {2:0.###}: non e' il disegno di questo pezzo, o non e' sviluppato.",
                        dove, lato, sviluppo));
            }
            return SenzaFeature(misure);
        }

        // =================================================================

        /// <summary>
        /// Lo spessore, il raggio e il fattore K della lamiera del pezzo, dalla
        /// lavorazione «SheetMetal». Null se il pezzo non e' di lamiera.
        /// </summary>
        private static ISheetMetalFeatureData DatiDellaLamiera(IModelDoc2 modello)
        {
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null)
                {
                    if (f.GetTypeName2() == "SheetMetal") return f.GetDefinition() as ISheetMetalFeatureData;
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Il lato lungo del pezzo SVILUPPATO, in mm, e il suo volume. NaN se non
        /// si riesce. Si toglie la soppressione alla lavorazione «FlatPattern»,
        /// si misura, e la si rimette — `SetBendState` non fa niente (nota in
        /// testa, trappola 1).
        /// </summary>
        private static double LunghezzaSviluppata(IModelDoc2 modello, out double volume)
        {
            volume = double.NaN;
            IFeature sviluppo = null;
            try
            {
                var f = modello.FirstFeature() as IFeature;
                while (f != null && sviluppo == null)
                {
                    if (f.GetTypeName2() == "FlatPattern") sviluppo = f;
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            if (sviluppo == null) return double.NaN;
            try
            {
                sviluppo.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
                modello.EditRebuild3();
                volume = VolumeMm3(modello) ?? double.NaN;   // NaN qui vuol gia' dire «non lo so»
                var corpi = (modello as IPartDoc)?.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                var box = corpi == null || corpi.Length == 0 ? null : (corpi[0] as IBody2)?.GetBodyBox() as double[];
                if (box == null) return double.NaN;
                return Math.Max(Math.Abs(box[3] - box[0]), Math.Max(Math.Abs(box[4] - box[1]), Math.Abs(box[5] - box[2]))) * 1000.0;
            }
            catch { return double.NaN; }
            finally
            {
                try
                {
                    sviluppo.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,
                        (int)swInConfigurationOpts_e.swThisConfiguration, null);
                    modello.EditRebuild3();
                }
                catch { }
            }
        }

        /// <summary>
        /// L'ingombro in pianta del DXF, in mm, calcolato dalle ENTITA' — non
        /// dall'intestazione. Null se il file non ha entita' leggibili.
        ///
        /// MISURATO il 25/9/2026, alla prima prova: `$EXTMIN/$EXTMAX`
        /// dell'intestazione dicevano 279,4 mm per uno sviluppo di 80,712 —
        /// 279,4 sono 11 pollici, il foglio del modello DXF, non il pezzo. Un
        /// controllo costruito sull'intestazione avrebbe rifiutato ogni DXF
        /// giusto, o peggio accettato quelli sbagliati del formato giusto.
        /// Qui si leggono le coppie (codice, valore) della sezione ENTITIES:
        /// punti 10/20 e 11/21, e per archi e cerchi il centro piu' il raggio.
        /// </summary>
        private static double[] IngombroDelDxf(string percorso)
        {
            try
            {
                var r = File.ReadAllLines(percorso);
                int inizio = -1;
                for (int i = 0; i + 1 < r.Length; i++)
                    if (r[i].Trim() == "2" && r[i + 1].Trim() == "ENTITIES") { inizio = i + 2; break; }
                if (inizio < 0) return null;
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                double cx = double.NaN, cy = double.NaN;
                bool arco = false;
                for (int i = inizio; i + 1 < r.Length; i += 2)
                {
                    string codice = r[i].Trim(), valore = r[i + 1].Trim();
                    if (codice == "0")
                    {
                        if (valore == "ENDSEC") break;
                        arco = valore == "ARC" || valore == "CIRCLE";
                        cx = cy = double.NaN;
                        continue;
                    }
                    double v;
                    if (!double.TryParse(valore, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                    if (codice == "10" || codice == "11") { if (arco) cx = v; else { minX = Math.Min(minX, v); maxX = Math.Max(maxX, v); } }
                    else if (codice == "20" || codice == "21") { if (arco) cy = v; else { minY = Math.Min(minY, v); maxY = Math.Max(maxY, v); } }
                    else if (codice == "40" && arco && !double.IsNaN(cx) && !double.IsNaN(cy))
                    {
                        minX = Math.Min(minX, cx - v); maxX = Math.Max(maxX, cx + v);
                        minY = Math.Min(minY, cy - v); maxY = Math.Max(maxY, cy + v);
                    }
                }
                if (minX == double.MaxValue || minY == double.MaxValue) return null;
                return new[] { Math.Round(maxX - minX, 3), Math.Round(maxY - minY, 3) };
            }
            catch { return null; }
        }

        private static List<double[]> PuntiDelProfilo(Dictionary<string, object> a, out string errore)
        {
            errore = null;
            const string comeSiScrive =
                "«punti» vuole almeno due punti [x, y] in mm sul piano: il profilo APERTO " +
                "della lamiera. Una L: [[0,30],[0,0],[50,0]].";
            // UN ELENCO E' UN IEnumerable, non un `object[]`. Misurato al primo
            // giro di banco (25/9/2026): gli argomenti arrivano come elenchi del
            // serializzatore di chi chiama, e un `as object[]` dava null su una L
            // scritta giusta — rifiutata con «vuole almeno due punti». E' la
            // stessa regola di `HaElenco`, che c'era gia'.
            object grezzo;
            var elenco = a != null && a.TryGetValue("punti", out grezzo) && !(grezzo is string)
                ? grezzo as System.Collections.IEnumerable : null;
            if (elenco == null) { errore = comeSiScrive; return null; }
            var fuori = new List<double[]>();
            foreach (var o in elenco)
            {
                var coppia = new List<double>();
                var p = o is string ? null : o as System.Collections.IEnumerable;
                if (p != null)
                    foreach (var c in p) coppia.Add(Convert.ToDouble(c, CultureInfo.InvariantCulture));
                if (coppia.Count != 2)
                {
                    errore = "Ogni punto e' una coppia [x, y] in mm.";
                    return null;
                }
                fuori.Add(coppia.ToArray());
            }
            if (fuori.Count < 2) { errore = comeSiScrive; return null; }
            return fuori;
        }

        /// <summary>L'angolo di piega al punto b, in radianti (0 = dritto).</summary>
        private static double AngoloDiPiega(double[] a, double[] b, double[] c)
        {
            double ux = b[0] - a[0], uy = b[1] - a[1], vx = c[0] - b[0], vy = c[1] - b[1];
            double cos = (ux * vx + uy * vy) / (Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy));
            return Math.Acos(Math.Max(-1, Math.Min(1, cos)));
        }
    }
}
