using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LA RIPETIZIONE A RIEMPIMENTO. (voce W1.1, residuo di GP14)
    ///
    /// Una faccia piena di fori: una griglia di sfiato, una piastra forata per
    /// alleggerire, un crivello. La lavorazione si fa una volta, e SOLIDWORKS la
    /// copia a giri concentrici finche' la faccia di contorno la contiene.
    ///
    /// ── LA GUIDA API, CITATA (estratto locale di sldworksapi.chm) ─────────
    ///
    ///   SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureFillPattern.html
    ///     «Obsolete. See IFeatureManager::CreateFeature and the Remarks in
    ///     IFillPatternFeatureData.» Le quindici combinazioni NULL di GP14
    ///     (nota 81u) erano tutte su questo binario morto.
    ///
    ///   SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFillPatternFeatureData.html
    ///     Remarks: «Before calling IFeatureManager::CreateDefinition, you can
    ///     pre-select the entities»; la tabella delle marche:
    ///       contorno (faccia, schizzo, curva piana)   16384
    ///       direzione (spigolo o asse)                    1   («if not selected,
    ///                                                      SOLIDWORKS chooses»)
    ///       posizione del seme (vertice, punto)           3   (facoltativa: senza,
    ///                                                      «the pattern seed instance
    ///                                                      is placed at the center of
    ///                                                      the fill boundary region»)
    ///       feature da ripetere                           4   (facce 5, corpi 6)
    ///     «Or, after calling CreateDefinition, you can explicitly set the
    ///     corresponding properties» — la seconda strada, quella che GP14 aveva
    ///     provato sette volte e che torna NULL (misurato di nuovo sotto).
    ///
    ///   Create_Fill_Pattern_Example_CSharp.htm — l'unico esempio: Hole1 con
    ///     marca 4, uno spigolo con marca 1, una faccia con marca 16384, POI
    ///     CreateDefinition(swFmFillPattern), solo scalari, CreateFeature. Gli
    ///     scalari di questo file sono i suoi, uno per uno (vedi ScalariDelRiempimento).
    ///
    ///   ...IFillPatternFeatureData~NoOfInstances.html: «the number of instances
    ///     per loop or side», valido con LayoutSpacingType = swPatternLayoutInstances.
    ///   ...IFillPatternFeatureData~LoopSpacing.html: «the spacing between the
    ///     centers of instances in adjacent pattern loops», per la disposizione
    ///     circolare.
    ///   ...IFillPatternFeatureData~Margins.html: «distance between the fill
    ///     boundary and the outermost pattern instance; 0 for no margin».
    ///
    /// ── LA SONDA, LANCIATA IL 28/9/2026 (Controlli\Riempimento\Programma.cs) ──
    ///
    /// Piastra 100 x 60 x 12, un foro passante Ø8 a 30 mm dal centro (603,2 mm3),
    /// SOLIDWORKS 29.5.1:
    ///
    ///   A  feature 4 + spigolo 1 + faccia 16384 PRIMA di CreateDefinition
    ///        -> «Ripetizione di riempimento1» [APattern], volume 71396,8 -> 62952,2:
    ///           14 fori in piu', -8444,6 = 14 x 603,2 esatto, corpi 1 -> 1, 0 in errore
    ///   C  stesse selezioni DOPO CreateDefinition          -> uguale ad A
    ///   D  la faccia del foro con marca 5 invece della 4   -> uguale ad A
    ///   A2 come A SENZA lo spigolo di direzione            -> NULL
    ///   B  la via storica: niente selezione, proprieta' scritte -> NULL
    ///
    /// Due cose da tenere. **La direzione e' obbligatoria**, anche se la guida la
    /// dice facoltativa: senza, NULL e nessun messaggio. Percio' qui si rifiuta
    /// PRIMA di toccare il pezzo. E **il tipo della feature e' «APattern»**, non
    /// un nome che contenga «Fill»: si controlla quello misurato.
    ///
    /// Qui si fa la ricetta A, nello stesso ordine: lavorazione, direzione,
    /// contorno, poi CreateDefinition.
    ///
    /// ── COSA NON C'E', E PERCHE' ──────────────────────────────────────
    ///
    /// Solo la disposizione CIRCOLARE, con il numero di copie per giro: e' quella
    /// dell'esempio, ed e' l'unica misurata. Quadrata, poligonale e a
    /// perforazione hanno proprieta' loro (InstanceSpacing, StaggerAngle,
    /// PatternLayoutPolygonSides) che nessuna sonda ha visto nascere.
    /// La TABELLA (ITablePatternFeatureData, l'altra meta' di W1.1) resta fuori:
    /// misurata NULL senza il sistema di coordinate con marca 16 che il suo
    /// esempio seleziona.
    /// </summary>
    public partial class OperazioniSw
    {
        /// <summary>Il tipo della feature nata, misurato (GetTypeName2) sulla sonda A.</summary>
        private const string TipoRiempimento = "APattern";

        /// <summary>
        /// Quanto la direzione puo' uscire dal piano della faccia di contorno:
        /// il seno dell'angolo, 0,01 = circa mezzo grado. Uno spigolo della faccia
        /// ci sta dentro con uno zero di macchina.
        /// </summary>
        private const double RiempimentoFuoriPiano = 0.01;

        private Esito RipetiRiempimento(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            if (!(modello is IPartDoc))
                return Fallito("La ripetizione a riempimento si fa su un pezzo: apri il pezzo e richiedimela.");

            // ── I NUMERI, prima di toccare qualunque cosa ──
            double passo, copieGrezze;
            string errore;
            if (!NumObbligatorio(a, "passo_mm", out passo, out errore))
                return Fallito(errore + " E' la distanza fra un giro di copie e il successivo, " +
                               "in mm: metti per esempio due volte il diametro del foro.");
            if (passo <= 0)
                return Fallito("«passo_mm» deve essere maggiore di zero: e' la distanza fra un giro " +
                               "di copie e il successivo. Metti per esempio due volte il diametro del foro.");
            if (!NumObbligatorio(a, "copie_per_giro", out copieGrezze, out errore))
                return Fallito(errore + " E' quante copie stanno su ogni giro attorno al centro " +
                               "della faccia: per esempio 10.");
            int copiePerGiro = (int)copieGrezze;
            if (copiePerGiro < 1 || Math.Abs(copieGrezze - copiePerGiro) > 1e-9)
                return Fallito("«copie_per_giro» e' un numero intero da 1 in su, non " +
                               copieGrezze.ToString(CultureInfo.InvariantCulture) + ": per esempio 10.");
            double margine = Num(a, "margine_mm", 0);
            if (margine < 0)
                return Fallito("«margine_mm» non puo' essere negativo: e' quanto le copie restano " +
                               "lontane dal bordo della faccia. Metti 0 per arrivare al bordo.");

            // ── LA DIREZIONE E' OBBLIGATORIA, E SI DICE PRIMA ──
            //
            // La guida la dice facoltativa («SOLIDWORKS chooses a direction
            // reference»); la sonda A2 del 28/9/2026 dice NULL senza di lei, e
            // senza nessun messaggio. Un rifiuto qui costa zero; un NULL dopo aver
            // selezionato mezzo pezzo costa un turno a indovinare.
            if (!HaRiferimento(a, "asse_rif") && string.IsNullOrEmpty(Str(a, "asse_uid", "")))
                return Fallito(
                    "Manca la direzione: senza una direzione la ripetizione a riempimento non " +
                    "nasce. La guida API la dice facoltativa, ma misurato su SOLIDWORKS 29.5.1 " +
                    "(28/9/2026) senza lo spigolo di direzione SOLIDWORKS risponde vuoto e non " +
                    "dice perche'. Indica in «asse_uid» uno spigolo dritto della faccia di " +
                    "contorno (nello stato «E1», «E2»...), per esempio il suo bordo lungo, " +
                    "oppure in «asse_rif» un asse fatto con «crea_asse».");

            // ── LA FACCIA DI CONTORNO, E DEVE ESSERE PIANA ──
            string facciaUid = Str(a, "faccia_uid", "");
            string motivo;
            var contorno = RisolviFaccia(stato, a, facciaUid, out motivo);
            if (contorno == null) return Fallito("Faccia di contorno: " + motivo);
            var normale = NormaleDellaFaccia(contorno);
            if (normale == null || normale.Length < 3)
                return Fallito(
                    "La faccia di contorno" + (facciaUid.Length > 0 ? " «" + facciaUid + "»" : "") +
                    " non e' piana: il riempimento dispone le copie dentro una faccia piana. " +
                    "Indica la faccia piana su cui sta la lavorazione da ripetere, per esempio " +
                    "quella da cui parte il foro.");

            // ── LA FOTOGRAFIA, PRIMA ──
            var prima = FotografaIlPezzo(modello);
            double? volumePrima; int corpiPrima, faccePrima;
            Fotografia(modello, out volumePrima, out corpiPrima, out faccePrima);
            if (volumePrima == null || corpiPrima < 0 || faccePrima < 0)
                return Fallito(VolumeNonLettoPrima("fare la ripetizione a riempimento"));

            IFeature seme = null;
            IFeature feature = null;
            string direzioneDetta = HaRiferimento(a, "asse_rif") ? "la direzione del passo citato" : "«" + Str(a, "asse_uid", "") + "»";
            try
            {
                modello.ClearSelection2(true);

                // 1. LA DIREZIONE PASSA DAL SUO AIUTANTE, che controlla che lo
                //    spigolo sia dritto e che sia ancora quello (firma, E19) — la
                //    stessa difesa di «ripeti_lineare». Poi la si rilegge dalla
                //    selezione e si ricomincia: l'ordine dell'esempio e della
                //    sonda A e' lavorazione, direzione, contorno, e l'aiutante con
                //    la marca 1 seleziona da capo invece di aggiungere.
                if (!SelezionaAsse(modello, a, "asse", stato, SelectionMarks.FillPatternDirection, append: false, out errore))
                    return Fallito("Direzione: " + errore);
                object direzione = DirezioneSelezionataPerRiempimento(modello);
                if (direzione == null)
                    return Fallito("Ho selezionato la direzione ma non riesco a rileggerla dalla " +
                                   "selezione. Prova con uno spigolo dritto dello stato invece di un asse.");

                // LA DIREZIONE DEVE STARE NELLA FACCIA. Il riempimento dispone le
                // copie dentro il contorno: uno spigolo che esce dalla faccia (lo
                // spessore della piastra) non ha un verso da seguire la' dentro.
                // Se il verso non si legge non si inventa un rifiuto.
                var versore = VersoreDellaDirezione(direzione);
                if (versore != null)
                {
                    double seno = Math.Abs(versore[0] * normale[0] + versore[1] * normale[1] + versore[2] * normale[2]);
                    if (seno > RiempimentoFuoriPiano)
                        return Fallito(string.Format(CultureInfo.InvariantCulture,
                            "La direzione {0} non sta nel piano della faccia di contorno: ne esce di " +
                            "{1:0.#} gradi. Il riempimento dispone le copie dentro la faccia, e una " +
                            "direzione che ne esce non ha un verso da seguire. Indica uno spigolo che " +
                            "corre lungo la faccia, per esempio uno dei suoi bordi dritti.",
                            direzioneDetta, Math.Asin(Math.Min(1.0, seno)) * 180.0 / Math.PI));
                }
                modello.ClearSelection2(true);

                // 2. La lavorazione da ripetere, marca 4 (SelectByID2 BODYFEATURE,
                //    come l'esempio; o l'oggetto del passo citato).
                if (!SelezionaLavorazione(modello, a, out errore, SelectionMarks.FillPatternFeatures))
                    return Fallito(errore);
                seme = SemeSelezionato(modello, SelectionMarks.FillPatternFeatures);

                // 3. La direzione di nuovo, marca 1, aggiunta.
                if (!RiselezionaDirezione(modello, direzione))
                    return Fallito("Non sono riuscito a riselezionare la direzione " + direzioneDetta +
                                   " insieme alla lavorazione. Prova con un altro spigolo dritto della faccia.");

                // 4. Il contorno, marca 16384, aggiunto.
                var gestore = modello.SelectionManager as ISelectionMgr;
                if (gestore == null || !Seleziona(gestore, contorno, SelectionMarks.FillPatternBoundary))
                    return Fallito("Non sono riuscito a selezionare la faccia di contorno" +
                                   (facciaUid.Length > 0 ? " «" + facciaUid + "»" : "") +
                                   ". Rileggi lo stato del documento e indica di nuovo la faccia.");

                // 5. SOLO ADESSO la definizione, e soltanto scalari.
                var dati = modello.FeatureManager.CreateDefinition(
                    (int)swFeatureNameID_e.swFmFillPattern) as IFillPatternFeatureData;
                if (dati == null)
                    return Fallito("SOLIDWORKS non mi ha dato la definizione di una ripetizione a " +
                                   "riempimento (CreateDefinition vuoto). Non ho toccato il pezzo. " +
                                   "Per copie in fila o in griglia usa «ripeti_lineare».");
                ScalariDelRiempimento(dati, Unita.M(passo), copiePerGiro, Unita.M(margine));
                feature = modello.FeatureManager.CreateFeature(dati) as IFeature;
            }
            catch (Exception ex)
            {
                return Fallito("La ripetizione a riempimento non e' riuscita: " + ex.Message +
                               ". Per copie in fila o in griglia usa «ripeti_lineare».");
            }
            finally
            {
                try { modello.ClearSelection2(true); } catch { }
            }

            if (feature == null)
                return Fallito(
                    "La ripetizione a riempimento non e' stata creata, e SOLIDWORKS non dice " +
                    "perche'. Le tre selezioni c'erano (lavorazione, direzione, contorno): di " +
                    "solito vuol dire che nessuna copia entra nella faccia -- il passo e' troppo " +
                    "grande o il margine troppo largo -- oppure che la lavorazione non sta su " +
                    "quella faccia. Prova un «passo_mm» piu' piccolo o «margine_mm» 0; per copie " +
                    "in fila o in griglia c'e' «ripeti_lineare», per un cerchio attorno a un asse " +
                    "«ripeti_circolare».");

            // ── E ADESSO SI MISURA IL PEZZO ──
            bool ricostruito = false;
            try { ricostruito = modello.EditRebuild3(); } catch { }

            string tipo = "";
            try { tipo = feature.GetTypeName2() ?? ""; } catch { }
            if (!string.Equals(tipo, TipoRiempimento, StringComparison.Ordinal))
                return Fallito(
                    "SOLIDWORKS ha creato una feature di tipo «" + tipo + "», non una ripetizione " +
                    "a riempimento («" + TipoRiempimento + "», misurato su SOLIDWORKS 29.5.1). " +
                    "Non ti confermo una cosa diversa da quella che hai chiesto." +
                    TogliIlRiempimento(modello, feature, volumePrima));

            // Le guardie comuni (OperazioniGuardie.cs): lavorazioni in errore,
            // ricostruzione, corpi, aperture che escono dal bordo o si fondono.
            // Le aperture si contano: una copia che morde il bordo della faccia o
            // si fonde con un'altra le fa calare.
            string rotto = CosaSiERotto(modello, prima, ricostruito, true);
            if (rotto != null)
                return Fallito(
                    "La ripetizione a riempimento e' nata, ma " + rotto + ". Prova un «margine_mm» " +
                    "piu' largo, perche' le copie restino lontane dal bordo, o un «passo_mm» piu' " +
                    "grande, perche' non si tocchino." + TogliIlRiempimento(modello, feature, volumePrima));

            // COSA DICHIARA LA FEATURE, riletto con IFeature::GetDefinition
            // come IFillPatternFeatureData — non gli argomenti ripetuti. Non
            // decide niente (decidono il volume, le copie contate e le
            // guardie); un valore che non si rilegge e' null.
            var misure = new Dictionary<string, object> { ["feature_type"] = tipo };
            DichiaratoDalRiempimento(feature, misure);

            // «Niente e' cambiato» e' il falso successo: lo prende ConfrontaCopia.
            var fermo = ConfrontaCopia(
                modello, volumePrima, corpiPrima, faccePrima, misure, "La ripetizione a riempimento");
            if (fermo != null) return Fallito(fermo + TogliIlRiempimento(modello, feature, volumePrima));

            // ── LE COPIE, CONTATE DALLE FACCE (come CopieMancanti) ──
            //
            // Di un riempimento non si sa in anticipo quante copie entrano: lo
            // decide la forma della faccia. Si contano e si dicono. Zero facce
            // possedute vuol dire zero copie, e quello si rifiuta; un conto che
            // non si divide (copie fuse fra loro) si tace invece di inventarlo.
            int facceRipetizione = FacceDellaFeature(feature);
            int facceSeme = FacceDellaFeature(seme);
            if (facceRipetizione == 0)
                return Fallito(
                    "La ripetizione a riempimento e' nata ma non possiede nessuna faccia: nessuna " +
                    "copia e' entrata nella faccia di contorno. Prova un «passo_mm» piu' piccolo " +
                    "o «margine_mm» 0." + TogliIlRiempimento(modello, feature, volumePrima));
            int copie = facceRipetizione > 0 && facceSeme > 0 && facceRipetizione % facceSeme == 0
                ? facceRipetizione / facceSeme : -1;
            misure["copies_made"] = NumeroOppureNull(copie);
            double? volumeDopo = VolumeMm3(modello);
            misure["volume_per_copy_mm3"] = copie > 0 && volumeDopo.HasValue
                ? NumeroOppureNull(Math.Abs(volumeDopo.Value - volumePrima.Value) / copie, 2)
                : null;

            return Riuscito(feature, misure);
        }

        /// <summary>
        /// Gli scalari di Create_Fill_Pattern_Example_CSharp.htm, uno per uno, con
        /// i tre che l'operazione espone (passo, copie per giro, margine) al posto
        /// dei suoi 17,024 mm, 10 e 0.
        ///
        /// I valori della forma del seme (Diameter, Diagonal, Dimension, Inner/
        /// OuterRadius, CreateSeedCutPolygonSides) servono solo a
        /// FeaturesToPatternType = CreateSeedCut: con swCreateSeedCutNone non
        /// contano, e si scrivono lo stesso identici all'esempio perche' la
        /// ricetta misurata e' quella intera. Cosi' StaggerAngle (solo per la
        /// perforazione) e InstanceSpacing, che l'esempio mette uguale al passo.
        ///
        /// GeometryPattern resta false: «valid only if PatternElement is
        /// swFeatureFaces» (GeometryPattern.html), e qui si ripete la feature.
        /// </summary>
        private static void ScalariDelRiempimento(
            IFillPatternFeatureData d, double passoM, int copiePerGiro, double margineM)
        {
            d.CreateSeedCutPolygonSides = 0;
            d.CreateSeedCutType = (int)swCreateSeedCutType_e.swCreateSeedCutNone;
            d.Diagonal = 0.008;
            d.Diameter = 0.008;
            d.Dimension = 0.00565685424949238;
            d.FeaturesToPatternType = (int)swFeaturesToPatternType_e.swFeaturesToPatternSelectedFeatures;
            d.GeometryPattern = false;
            d.InnerRadius = 0.00380422606518061;
            d.InstanceSpacing = passoM;
            d.LayoutSpacingType = (int)swPatternLayoutSpacingType_e.swPatternLayoutInstances;
            d.LoopSpacing = passoM;
            d.Margins = margineM;
            d.NoOfInstances = copiePerGiro;
            d.OuterRadius = 0.004;
            d.PatternLayoutPolygonSides = 0;
            d.PatternLayoutType = (int)swPatternLayoutType_e.swPatternLayoutCircular;
            d.Rotation = 0;
            d.SeedCutFlipShapeDirection = false;
            d.StaggerAngle = Math.PI / 3.0;
        }

        /// <summary>Il primo oggetto della selezione, o null.</summary>
        private static object DirezioneSelezionataPerRiempimento(IModelDoc2 modello)
        {
            try
            {
                var sel = modello.SelectionManager as ISelectionMgr;
                if (sel == null || sel.GetSelectedObjectCount2(-1) < 1) return null;
                return sel.GetSelectedObject6(1, -1);
            }
            catch { return null; }
        }

        /// <summary>
        /// Il versore di una direzione selezionata: uno spigolo dritto
        /// (ICurve.LineParams: punto 0-2, direzione 3-5, come in StatoDocumento)
        /// o un asse di riferimento (IRefAxis.GetRefAxisParams: i due estremi).
        /// Null se non si legge: chi chiama allora non rifiuta.
        /// </summary>
        private static double[] VersoreDellaDirezione(object direzione)
        {
            double[] v = null;
            try
            {
                var spigolo = direzione as IEdge;
                if (spigolo != null)
                {
                    var curva = spigolo.IGetCurve();
                    var p = curva != null && curva.IsLine() ? curva.LineParams as double[] : null;
                    if (p != null && p.Length >= 6) v = new[] { p[3], p[4], p[5] };
                }
                else
                {
                    var asse = (direzione as IFeature)?.GetSpecificFeature2() as IRefAxis;
                    var p = asse?.GetRefAxisParams() as double[];
                    if (p != null && p.Length >= 6) v = new[] { p[3] - p[0], p[4] - p[1], p[5] - p[2] };
                }
            }
            catch { return null; }
            if (v == null) return null;
            double l = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (l < 1e-12) return null;
            return new[] { v[0] / l, v[1] / l, v[2] / l };
        }

        /// <summary>
        /// La direzione gia' validata, riselezionata con la marca 1 IN AGGIUNTA
        /// alla lavorazione. Uno spigolo con `IEntity.Select4` e un
        /// `SelectData` con `Mark = 1`: la chiamata della sonda
        /// (Controlli\Riempimento, `Mark(selMgr, 1)`), la stessa di
        /// `Seleziona(gestore, faccia, marca)` per la faccia di contorno. Un asse
        /// di riferimento per feature (`Select2` con la marca): la sonda non ne
        /// ha usati, ed e' detto nel catalogo.
        /// </summary>
        private static bool RiselezionaDirezione(IModelDoc2 modello, object direzione)
        {
            try
            {
                if (direzione is IEdge)
                {
                    var entita = direzione as IEntity;
                    var gestore = modello.SelectionManager as ISelectionMgr;
                    if (entita == null || gestore == null) return false;
                    var d = (SelectData)gestore.CreateSelectData();
                    d.Mark = SelectionMarks.FillPatternDirection;
                    return entita.Select4(true, d);
                }
                var f = direzione as IFeature;
                if (f != null) return f.Select2(true, SelectionMarks.FillPatternDirection);
            }
            catch { }
            return false;
        }

        /// <summary>
        /// I quattro scalari che l'operazione chiede, come la feature nata li
        /// DICHIARA (pagine della guida
        /// `IFillPatternFeatureData~PatternLayoutType.html`, `~LoopSpacing.html`
        /// «Distance between loops of pattern instances», `~NoOfInstances.html`
        /// «Number of instances per loop or side», `~Margins.html`), letti da
        /// `IFeature::GetDefinition` (`IFeature~GetDefinition.html`: «query the
        /// feature data object»). La disposizione si scrive per NOME dell'enum
        /// `swPatternLayoutType_e`, mai per cifra. Quello che non si rilegge
        /// resta null: «non lo so» non si riempie con l'argomento.
        /// </summary>
        private static void DichiaratoDalRiempimento(IFeature feature, Dictionary<string, object> misure)
        {
            object disposizione = null, passo = null, copie = null, margine = null;
            try
            {
                var d = feature.GetDefinition() as IFillPatternFeatureData;
                if (d != null)
                {
                    try { disposizione = NomeDellaDisposizione(d.PatternLayoutType); } catch { }
                    try { passo = Math.Round(Unita.Mm(d.LoopSpacing), 3); } catch { }
                    try { copie = d.NoOfInstances; } catch { }
                    try { margine = Math.Round(Unita.Mm(d.Margins), 3); } catch { }
                }
            }
            catch { }
            misure["declared_layout"] = disposizione;
            misure["declared_loop_spacing_mm"] = passo;
            misure["declared_instances_per_loop"] = copie;
            misure["declared_margin_mm"] = margine;
        }

        /// <summary>Il nome in inglese della disposizione, dal nome dell'enum; null se sconosciuta.</summary>
        private static string NomeDellaDisposizione(int valore)
        {
            if (valore == (int)swPatternLayoutType_e.swPatternLayoutCircular) return "circular";
            if (valore == (int)swPatternLayoutType_e.swPatternLayoutSquare) return "square";
            if (valore == (int)swPatternLayoutType_e.swPatternLayoutPolygon) return "polygon";
            if (valore == (int)swPatternLayoutType_e.swPatternLayoutPerforation) return "perforation";
            return null;
        }

        /// <summary>
        /// Toglie la ripetizione appena nata quando una guardia la rifiuta, e torna
        /// la coda del messaggio. Non EditDelete (apre una finestra di conferma:
        /// ProveLeFirmeVietate), ma DeleteSelection2 come `TogliIlTaglio`; e «ha
        /// detto di averla tolta» non basta: si riconta l'albero e si rimisura il
        /// volume.
        /// </summary>
        private string TogliIlRiempimento(IModelDoc2 modello, IFeature ripetizione, double? volumePrima)
        {
            bool tolta = false;
            try
            {
                modello.ClearSelection2(true);
                if (ripetizione != null && ripetizione.Select2(false, 0))
                    tolta = modello.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch { }
            finally { try { modello.ClearSelection2(true); } catch { } }

            const string resta = " La ripetizione pero' e' rimasta nel pezzo: ritirala — " + Frasi.CosiSiRitira + ".";
            if (!tolta) return resta;
            try { modello.EditRebuild3(); } catch { }
            int lavorazioni = ContaFeature(modello);
            if (lavorazioni < 0 || _featureAllInizioDelPasso < 0)
                return " L'ho tolta, ma non ho potuto ricontare l'albero per controllare che il " +
                       "pezzo sia com'era: guardalo.";
            if (lavorazioni > _featureAllInizioDelPasso) return resta;
            double? volumeDopo = VolumeMm3(modello);
            if (volumePrima == null || volumeDopo == null)
                return " L'ho tolta, ma non ho potuto rimisurare il volume per controllare che il " +
                       "pezzo sia com'era: guardalo.";
            if (Math.Abs(volumeDopo.Value - volumePrima.Value) > 0.01) return resta;
            return " L'ho tolta: il pezzo e' com'era prima di questo passo.";
        }
    }
}
