using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// Le sei operazioni che l'agente sa costruire davvero.
    ///
    /// LA REGOLA IN TESTA A TUTTO
    /// Questo file scrive dentro il documento di una persona. Ogni metodo qui
    /// dentro fa una cosa sola, la fa con parametri gia' validati dal server, e
    /// se qualcosa non torna NON prova a rimediare: fallisce e lo dice. Un
    /// tentativo di rimedio automatico dentro il modello di qualcun altro e' il
    /// modo migliore per produrre un pezzo che sembra giusto.
    ///
    /// PARAMETRI NOMINATI, SEMPRE
    /// FeatureExtrusion3 ha 23 parametri, FeatureCut4 ne ha 27. Scritti
    /// posizionalmente, uno scambiato non da' errore di compilazione ne'
    /// eccezione: da' una feature diversa. I nomi qui sotto sono quelli veri,
    /// letti per riflessione dall'interop 29.5.1.1 (SOLIDWORKS 2021), non
    /// ricordati.
    ///
    /// UNITA'
    /// Tutti i valori in ingresso sono in MILLIMETRI e GRADI, come sul
    /// protocollo. La conversione la fa Unita, e solo Unita.
    ///
    /// IL LIMITE DICHIARATO DI QUESTA VERSIONE
    /// Le operazioni su faccia (fori e tasche) lavorano solo su facce PIANE e
    /// perpendicolari a uno degli assi principali. Su una faccia inclinata il
    /// sistema di coordinate dello schizzo non coincide con quello del modello,
    /// e le coordinate x/y che arrivano dal piano finirebbero altrove — senza
    /// nessun errore. Meglio rifiutare: chi legge un rifiuto capisce, chi
    /// guarda un foro spostato di 30 mm spesso non se ne accorge.
    /// </summary>
    public partial class SwOperations
    {
        private readonly ISldWorks _sw;
        private readonly Action<string> _log;

        /// <summary>Cosa hanno lasciato i passi precedenti di questo piano.</summary>
        private IReadOnlyDictionary<int, References.Step> _stepsDone =
            new Dictionary<int, References.Step>();

        /// <summary>
        /// How many features the tree had when this step began.
        ///
        /// Until Z-C21.5 it was what let <see cref="WithoutSketch"/> tell a
        /// sketch made by this step from one already there; that is now
        /// <see cref="_featureNamesAtStepStart"/>, which says WHICH features
        /// were born and not only that the tree grew. The count is still read
        /// after a withdrawal (OperazioniAncora, OperazioniRipetiRiempimento):
        /// a tree longer than at the start means the step left something in
        /// the part.
        ///
        /// The start value is int.MaxValue and NOT zero: if nobody wrote it,
        /// the tree cannot look «grown». A defect of ours must leave one sketch
        /// too many, never delete someone else's feature.
        /// </summary>
        private int _featuresAtStepStart = int.MaxValue;

        /// <summary>
        /// The names of the features of the tree when this step began, or null
        /// (not a part, tree unreadable, or not written yet).
        ///
        /// SINCE Z-C21.5 (scheme S7) this, and no longer the count above, is
        /// what lets <see cref="WithoutSketch"/> tell a sketch made by this
        /// step from one that was already there: the count only said that the
        /// tree had grown, and the sketch was then taken as the last feature of
        /// the tree, which on a sheet-metal part is the flat pattern. Null is
        /// the safe default for the same reason int.MaxValue is above: with no
        /// «before», nothing looks born and nothing is deleted.
        /// </summary>
        private HashSet<string> _featureNamesAtStepStart;

        public SwOperations(ISldWorks sw, Action<string> log)
        {
            _sw = sw;
            _log = log ?? (m => { });
        }

        /// <summary>
        /// QUANDO UN CONTROLLO BOCCIA UNA FORMA, LA FORMA E' GIA' NEL PEZZO.
        ///
        /// Le operazioni che si controllano da sole — l'asola conta il
        /// materiale tolto, il guscio il volume — decidono DOPO aver chiamato
        /// la feature, perche' prima non c'e' niente da misurare. Se la misura
        /// non torna, il taglio resta: disfare una feature che ha TOLTO
        /// materiale non e' la stessa cosa che togliere uno schizzo vuoto
        /// (vedi <see cref="WithoutSketch"/>), e non e' stato provato.
        ///
        /// Allora lo si DICE, sempre e con le stesse parole. Un rifiuto che
        /// tace lascia credere che il pezzo sia intatto.
        ///
        /// Only where the cut is really still in the part (Z-C21.9, finding
        /// C9-6): once DeleteSelection2 has removed it, RemoveCut answers
        /// with RemovedButTreeLonger or RemovedButVolumeChanged instead.
        /// </summary>
        private const string RemainsInPart =
            " Il taglio pero' e' gia' stato fatto: ritiralo — " + Sentences.HowToWithdrawIt + ".";

        /// <summary>
        /// Coda dei messaggi di errore che lasciano uno schizzo nell'albero.
        ///
        /// NON E' PIU' LA STRADA NORMALE: dalla 0.43.0 lo schizzo lo togliamo
        /// noi, e questa frase resta per il caso in cui NON ci riusciamo. Il
        /// motivo del cambio e' misurato: l'11 settembre 2026, otto richieste
        /// oltre i limiti hanno prodotto sette rifiuti giusti e sette schizzi
        /// orfani. Il messaggio era onesto, ma «puoi toglierlo con Ctrl+Z»
        /// sette volte non e' una riparazione: e' un compito dato a chi legge.
        ///
        /// Chi la riceve adesso sa due cose vere insieme: che l'operazione non
        /// e' riuscita, e che nel pezzo e' rimasto qualcosa di nostro.
        /// </summary>
        private const string LeftSketchBehind =
            " Nel pezzo e' rimasto lo schizzo che avevo disegnato: puoi toglierlo — " +
            Sentences.HowToWithdrawIt + ".";

        /// <summary>
        /// Il terzo esito di <see cref="WithoutSketch"/>, e il piu' importante
        /// dei tre: lo schizzo e' stato tolto, ma togliendolo il pezzo e'
        /// cambiato.
        ///
        /// Non dovrebbe succedere — si cancella solo una feature che e' uno
        /// schizzo, che sta in fondo all'albero e da cui non dipende niente —
        /// e proprio per questo va detto se succede. Rispondere
        /// <see cref="LeftSketchBehind"/> qui sarebbe una bugia («lo schizzo e'
        /// rimasto» quando non e' rimasto) che coprirebbe un danno vero.
        ///
        /// NON SI E' MAI VISTA ACCADERE, e il 14/9/2026 si e' provato a farla
        /// accadere. Voce D7 del TODO.
        ///
        /// Perche' e' difficile da raggiungere, ragionando sulle condizioni che
        /// <see cref="WithoutSketch"/> impone prima di arrivare qui: lo schizzo
        /// e' l'ultima feature, e' nato in questo passo, e **non ha figli**. Uno
        /// schizzo senza figli non porta materiale, e `swDelete_Absorbed` toglie
        /// lui e le sue quote e basta. Quindi volume e conteggio corpi non
        /// possono cambiare, e il conteggio feature cala di esattamente uno.
        ///
        /// Provato anche il caso piu' cattivo che si e' saputo costruire: lo
        /// stesso giro su un pezzo **gia' instabile** — con una lavorazione in
        /// errore nell'albero — perche' la cancellazione fa ricostruire, e una
        /// ricostruzione su un pezzo rotto puo' dare un volume diverso. Non e'
        /// successo: lo schizzo si e' tolto pulito e il messaggio non e' uscito.
        ///
        /// COSA LA RENDEREBBE RAGGIUNGIBILE. Uno di questi:
        ///
        ///   * `IGetChildCount()` che risponde 0 per uno schizzo che invece un
        ///     figlio ce l'ha (un riferimento esterno non contato come figlio);
        ///   * un'operazione che lascia dietro di se' un CORPO oltre allo
        ///     schizzo, e poi fallisce: li' il conteggio corpi cambierebbe;
        ///   * `swDelete_Absorbed` che in una versione futura cancelli piu' di
        ///     quello che cancella oggi.
        ///
        /// **Il messaggio resta**, e non e' codice morto: e' la guardia che
        /// distingue «ho rimesso il pezzo com'era» da «credo di averlo rimesso
        /// com'era». Il giorno in cui una delle tre cose sopra diventa vera,
        /// questa riga e' la differenza fra accorgersene e non accorgersene.
        /// Ed e' scritta apposta come «controllalo» e non come «ho rotto il
        /// pezzo»: quello che si sa e' che i numeri non tornano, non chi sia
        /// stato.
        /// </summary>
        private const string SketchRemovedButNotOnlyIt =
            " Ho tolto lo schizzo che avevo disegnato, ma togliendolo e' cambiato" +
            " anche il pezzo: controllalo, e se non e' come lo vuoi ritiralo — " +
            Sentences.HowToWithdrawIt + ".";

        /// <summary>
        /// Il tipo dell'origine del pezzo, l'unica cosa che <see cref="WithoutSketch"/>
        /// rifiuta per NOME di tipo.
        ///
        /// Misurato il 14 settembre 2026: l'origine risponde ISketch a
        /// `GetSpecificFeature2` esattamente come uno schizzo vero, e su un
        /// documento vuoto e' l'ultima feature dell'albero. Non e' un nome
        /// tradotto — quello e' `Name`, che in italiano dice «Origine» — e'
        /// il nome di TIPO dell'API, che non cambia con la lingua.
        /// </summary>
        private const string PartOrigin = "OriginProfileFeature";

        public class Outcome
        {
            public bool Succeeded;
            public string Error;
            /// <summary>Nome della feature creata: serve per tornare indietro.</summary>
            public string CreatedFeature;
            /// <summary>
            /// La feature vera, non il suo nome. La tiene l'esecutore del piano
            /// per poter risolvere i riferimenti dei passi successivi: il nome
            /// e' localizzato e si puo' rinominare, l'oggetto no.
            /// </summary>
            public IFeature Feature;
            /// <summary>
            /// La direzione lungo cui questo passo ha lavorato. Serve a dire
            /// quale faccia e' «in cima»; null per le operazioni che una
            /// direzione non ce l'hanno.
            /// </summary>
            public double[] Direction;
            /// <summary>
            /// IL DOCUMENTO SU CUI SI LAVORA DA ADESSO IN POI, quando questa
            /// operazione lo ha cambiato APPOSTA.
            ///
            /// Ce n'e' una sola che lo fa — `create_drawing` — e non e' un
            /// effetto collaterale: una tavola E' un documento nuovo, e i passi
            /// dopo (le viste, le quote) vanno fatti li'.
            ///
            /// Serve perche' chi esegue il piano sorveglia che il documento non
            /// cambi sotto i piedi: e' la difesa che impedisce di estrudere nel
            /// pezzo di qualcun altro. Senza un modo per DICHIARARE il cambio,
            /// quella difesa fermerebbe un piano corretto («fammi la tavola e
            /// mettici le viste») — e un rifiuto di cio' che e' giusto e' il
            /// peggiore dei due modi di sbagliare.
            ///
            /// Si segue solo cio' che e' dichiarato. Un documento che cambia
            /// senza che nessuno lo dica resta un motivo per fermarsi.
            /// </summary>
            public IModelDoc2 NewDocument;
            public Dictionary<string, object> Measurements = new Dictionary<string, object>();
        }

        /// <summary>
        /// Esegue una operazione del piano. Ritorna sempre un esito, non solleva.
        /// </summary>
        /// <param name="stepsDone">
        /// Cosa hanno lasciato i passi gia' eseguiti di questo stesso piano,
        /// per numero d'ordine. E' cio' che permette al passo 2 di dire «la
        /// faccia in cima a quello che hai fatto al passo 1» invece di citare
        /// un identificativo che, quando il piano e' stato scritto, non
        /// esisteva ancora.
        /// </param>
        /// <summary>
        /// I nomi che lo switch qui sotto sa eseguire.
        ///
        /// SERVE A CONFRONTARE IL CATALOGO CON CIO' CHE ESISTE DAVVERO. Il
        /// catalogo dichiara 38 operazioni; qui ce ne sono 36, e le due che
        /// mancano — `extrude_section` e `spur_gear` — esistono solo perche'
        /// il server le espande in `contours` prima che arrivino qui, e il ramo
        /// `default` ricade sull'estrusione di profilo quando trova dei
        /// contorni. Chiamate con i loro argomenti veri rispondono «operazione
        /// sconosciuta». Il controllo che confronta i due insiemi lo dice; senza
        /// elenco, sarebbe una regex su 4.700 righe che si rompe alla prima
        /// riformattazione.
        ///
        /// **QUESTO ELENCO PUO' DERIVARE DALLO SWITCH, ed e' il suo difetto.**
        /// Ci si difende in due modi, tutti e due nel controllo e nessuno nella
        /// buona volonta': si verifica che ogni nome qui dentro sia davvero
        /// dispatchabile (chiamando `Run` e guardando che NON risponda
        /// «operazione sconosciuta»), e si confronta con i `case` letti dal
        /// sorgente — dove leggerne zero e' un fallimento, non un successo.
        /// </summary>
        public static readonly string[] ExecutableNames =
        {
            "apply_material", "slot", "activate_configuration",
            "mate", "combine_bodies",
            "weld_bead", "create_axis", "create_configuration",
            "create_drawing", "create_plane", "cut_list",
            "plane_three_points", "weldment_profiles", "sketch_picture", "file_dependencies",
            "minimum_distance", "delete_feature",
            "export_iges", "export_pdf", "export_step", "export_stl",
            "extrude_circle", "extrude_profile", "extrude_rectangle",
            "extrude_section", "spur_gear",
            "extrude_on_face", "gusset", "cosmetic_thread",
            "hole_wizard", "shell", "insert_component",
            "set_sheet", "engrave_text", "body_bounding_box", "structural_member",
            "measure_between", "set_dimension", "global_variable", "link_dimension", "reorder_feature", "edit_extrusion", "dimension_tolerance", "mass_properties",
            "loft_two_circles",
            "model_dimensions", "fillet_edges", "variable_fillet", "fillet_face",
            "circular_pattern", "fill_pattern", "set_property",
            "linear_pattern", "revolve", "save_document",
            "draft", "rib", "chamfer_edges", "suppress_instance", "suppress_feature",
            "replace_component", "fix_component", "mirror_component",
            "center_marks", "drawing_dimension", "note", "hole_table", "flat_pattern_view", "drawing_weld_symbol", "drawing_bom", "balloons",
            "surface_finish", "datum", "geometric_tolerance",
            "section_view", "detail_view",
            "new_sheet", "activate_sheet", "drawing_cut_list",
            "interference_check", "title_block",
            "circular_sweep",
            "mirror",
            "move_bodies", "trim_members",
            "cylindrical_cut", "rectangular_cut", "end_cap",
            "rectangular_frame", "check_body_geometry",
            "check_overhangs",
            "model_view", "standard_views",
            "base_flange", "edge_flange", "hem", "sketched_bend", "flat_pattern_dxf", "dimension_scheme",
        };

        /// <summary>
        /// La frase esatta con cui lo switch rifiuta un nome che non conosce.
        /// Sta qui e non scritta due volte perche' il controllo la confronta:
        /// se cambia, il controllo deve accorgersene invece di credere che
        /// tutto vada bene.
        /// </summary>
        public const string UnknownOperation = "Operazione sconosciuta: ";

        public Outcome Run(
            IModelDoc2 model,
            string operation,
            Dictionary<string, object> arguments,
            DocumentState docState,
            IReadOnlyDictionary<int, References.Step> stepsDone = null)
        {
            // THE NAMES OF BEFORE W3.1-bis.5 (Alias.cs, decision D4): an
            // Italian operation, argument or value is translated here, for the
            // callers of the library that do not come through the bridge (the
            // bench, the add-in), and the answer says so in `deprecated`. The
            // bridge translates before its own checks, so its plans arrive here
            // in English. Removed in W3.1-bis.11.
            List<string> deprecated = Alias.Step(ref operation, arguments);
            Outcome outcome = ExecuteWithCurrentNames(model, operation, arguments, docState, stepsDone);
            AttachDeprecated(outcome, deprecated);
            return outcome;
        }

        /// <summary>
        /// The deprecated lines of a step on its outcome: always in
        /// `measurements.deprecated` (an outcome whose operation left no
        /// measurements gets them anyway, or the warning would be lost on a
        /// step that worked), and on a failed step in the error too, which is
        /// what a caller reads first. Removed with the aliases in W3.1-bis.11.
        /// </summary>
        public static void AttachDeprecated(Outcome outcome, List<string> deprecated)
        {
            if (outcome == null || deprecated == null || deprecated.Count == 0) return;
            if (outcome.Measurements == null) outcome.Measurements = new Dictionary<string, object>();
            outcome.Measurements["deprecated"] = deprecated.ToArray();
            if (!outcome.Succeeded) outcome.Error = outcome.Error + " (" + string.Join("; ", deprecated) + ")";
        }

        private Outcome ExecuteWithCurrentNames(
            IModelDoc2 model,
            string operation,
            Dictionary<string, object> arguments,
            DocumentState docState,
            IReadOnlyDictionary<int, References.Step> stepsDone)
        {
            _stepsDone = stepsDone ?? new Dictionary<int, References.Step>();
            // Si svuota all'INIZIO: svuotarlo alla fine lo lascerebbe pieno
            // quando l'operazione fallisce, e la successiva erediterebbe gli
            // spostamenti di quella prima.
            _reanchors.Clear();
            // The picture of the tree BEFORE anything is touched: the count,
            // read after a withdrawal, and the names, which say WHICH features
            // this step made (Z-C21.5, scheme S7) and are what WithoutSketch
            // reads, so as never to delete what was already there. Here and not
            // in each operation, because every operation comes through here.
            _featuresAtStepStart = CountFeature(model);
            _featureNamesAtStepStart = FeatureNamesNow(model);
            try
            {
                switch (operation)
                {
                    case "extrude_rectangle": return ExtrudeRectangle(model, arguments);
                    case "extrude_circle": return ExtrudeCircle(model, arguments);
                    case "extrude_section": return ExtrudeSection(model, arguments);
                    case "spur_gear": return SpurGearOp(model, arguments);
                    // La sagoma libera e le RICETTE finiscono nello stesso
                    // esecutore: una ruota dentata non e' un'altra operazione,
                    // e' un modo di scrivere dei contorni. Il server la espande
                    // e manda gli stessi `contours`. Due esecutori per la stessa
                    // cosa vorrebbero dire due comportamenti che divergono alla
                    // prima modifica.
                    case "extrude_profile":
                        return ExtrudeProfile(model, arguments);
                    case "cylindrical_cut": return CylindricalCut(model, arguments, docState);
                    // Il foro fatto come si fa un foro: creazione guidata,
                    // norma ISO, filetto, lamatura, svasatura. Sta in
                    // HoleOperations.cs perche' la ricetta e' lunga e
                    // ogni riga di quella ricetta e' stata misurata.
                    case "hole_wizard": return HoleWizard(model, arguments, docState);
                    // Il materiale: MaterialOperations.cs.
                    case "apply_material": return ApplyMaterial(model, arguments);
                    case "rectangular_cut": return RectangularCut(model, arguments, docState);
                    case "slot": return Slot(model, arguments, docState);
                    case "extrude_on_face": return ExtrudeOnFace(model, arguments, docState);
                    case "set_dimension": return SetDimension(model, arguments, docState);
                    // Le variabili globali e le equazioni (voce Z-F1): dare un nome a
                    // un numero invece di ripeterlo. EquationOperations.cs.
                    case "global_variable": return GlobalVariable(model, arguments, docState);
                    case "link_dimension": return LinkDimension(model, arguments, docState);
                    // Riordinare l'albero (voce Z-F3). ReorderOperations.cs.
                    case "reorder_feature": return ReorderFeature(model, arguments, docState);
                    // Cambiare un'estrusione o un taglio gia' fatti (voce Z-F4).
                    // EditExtrusionOperations.cs.
                    case "edit_extrusion": return EditExtrusion(model, arguments, docState);
                    case "dimension_tolerance": return DimensionTolerance(model, arguments);
                    case "suppress_feature": return SuppressFeature(model, arguments, docState);
                    case "delete_feature": return DeleteFeature(model, arguments, docState);
                    case "move_bodies": return MoveBodies(model, arguments, docState);
                    // Le booleane fra corpi e il raccordo a raggio variabile
                    // stanno in BodyOperations.cs.
                    case "combine_bodies": return CombineBodies(model, arguments, docState);
                    case "variable_fillet":
                        return VariableFillet(model, arguments, docState);
                    // Il raccordo di FACCIA (voce GP7, la meta' che mancava):
                    // CreateDefinition(swFmFillet) e marche 2/4, non FeatureFillet3.
                    // FaceFilletOperations.cs.
                    case "fillet_face":
                        return FaceFillet(model, arguments, docState);
                    case "cosmetic_thread":
                        return CosmeticThread(model, arguments, docState);
                    case "fillet_edges": return FilletEdges(model, arguments, docState);
                    case "chamfer_edges": return ChamferEdges(model, arguments, docState);
                    case "shell": return Shell(model, arguments, docState);
                    case "create_plane": return CreatePlane(model, arguments);
                    // Il piano obliquo, in PlaneOperations.cs. `create_plane` sa
                    // fare solo lo sfalsato dai tre standard: per una faccia
                    // inclinata non c'era niente su cui disegnare.
                    // L'operazione che il catalogo chiedeva e che non c'era:
                    // sta in ProfileOperations.cs. Senza, «structural_member»
                    // diceva al modello di copiare da una cosa inesistente.
                    case "weldment_profiles":
                        return WeldmentProfiles(model, arguments, docState);

                    // ── assiemi ─────────────────────────────────────────
                    // La prima. Sta in AssemblyOperations.cs, e la cosa che fa
                    // oltre a inserire e' contare i gradi di liberta' residui:
                    // «AddComponent5 piazza senza accoppiare», e quattro corpi
                    // liberi sembrano un assieme.
                    case "insert_component":
                        return InsertComponent(model, arguments, docState);
                    // La seconda, e quella che fa la differenza: inserire non
                    // e' accoppiare. Sta in MateOperations.cs.
                    case "mate":
                        return Mate(model, arguments, docState);
                    // «Il coperchio va rifatto»: sta in ReplaceOperations.cs,
                    // e la cosa che misura e' se gli accoppiamenti sopravvivono.
                    case "replace_component":
                        return ReplaceComponent(model, arguments, docState);
                    // Fissare o liberare (voce Z-D5): l'unico stato di un componente
                    // che cambia come l'assieme si ricostruisce. FixComponentOperations.cs.
                    case "fix_component":
                        return FixComponent(model, arguments, docState);
                    // La staffa destra e la sinistra (voce GA11). MirrorComponentOperations.cs.
                    case "mirror_component":
                        return MirrorComponent(model, arguments, docState);
                    // Un'immagine da ricalcare, in scala, in uno schizzo (voce Z-F5).
                    // RetraceOperations.cs.
                    case "sketch_picture":
                        return SketchPicture(model, arguments, docState);
                    case "plane_three_points":
                        return PlaneThroughThreePoints(model, arguments, docState);

                    // ── tubi e transizioni ──────────────
                    // Stanno in PathOperations.cs. Le uniche due operazioni
                    // che fanno una forma non dritta, e le uniche in cui il
                    // profilo e' un NUMERO e non uno schizzo da nominare.
                    case "circular_sweep":
                        return CircularSweep(model, arguments, docState);
                    case "loft_two_circles":
                        return LoftTwoCircles(model, arguments, docState);
                    // ── lamiera (voce GP18) e quote nel modello (voce Z-H3) ──
                    // In SheetMetalOperations.cs e DimXpertOperations.cs, ognuna
                    // con la misura che l'ha resa costruibile scritta in testa.
                    case "base_flange": return BaseFlange(model, arguments);
                    case "edge_flange": return EdgeFlange(model, arguments, docState);
                    case "hem": return Hem(model, arguments, docState);
                    case "sketched_bend": return SketchedBend(model, arguments, docState);
                    case "flat_pattern_dxf": return FlatPatternDxf(model, arguments);
                    case "dimension_scheme": return DimensionScheme(model, arguments, docState);
                    case "create_axis": return CreateAxis(model, arguments, docState);
                    case "linear_pattern": return LinearPattern(model, arguments, docState);
                    case "mirror": return Mirror(model, arguments);
                    case "revolve": return Revolve(model, arguments);
                    case "circular_pattern": return CircularPattern(model, arguments, docState);
                    // FillPatternOperations.cs (voce W1.1).
                    case "fill_pattern": return RepeatFill(model, arguments, docState);

                    // ── tavola ──────────────────────────
                    // Stanno in DrawingOperations.cs. Sono le prime
                    // operazioni del catalogo che NON lavorano su un pezzo:
                    // creano un secondo documento e lo tengono aperto
                    // insieme al modello. Le tre trappole della famiglia
                    // (il diedro, la quota che mente, il PDF di un foglio
                    // solo) sono spiegate in testa a quel file.
                    case "create_drawing": return CreateDrawing(model, arguments);
                    case "set_sheet": return SetSheet(model, arguments);
                    case "standard_views": return StandardViews(model, arguments);
                    case "model_view": return ModelView(model, arguments);
                    case "model_dimensions": return ModelDimensions(model, arguments);
                    // Le croci di centro, in CenterMarkOperations.cs. Una tavola
                    // con dodici fori e nessun centro si stampa e sembra
                    // completa: in officina manca dove forare.
                    case "center_marks": return CenterMarks(model, arguments);
                    case "drawing_dimension": return DrawingDimension(model, arguments);
                    // La nota va sul FOGLIO, non dentro una vista: vedi
                    // `DrawingNoteOperations.cs`, dove c'e' anche perche'
                    // contarla nel posto sbagliato avrebbe chiuso quattro voci.
                    case "note": return DrawingNote(model, arguments);
                    case "hole_table": return HoleTable(model, arguments);
                    case "flat_pattern_view": return FlatPatternView(model, arguments);
                    case "drawing_weld_symbol": return DrawingWeldSymbol(model, arguments);
                    case "drawing_bom": return DrawingBom(model, arguments);
                    case "balloons": return Balloons(model, arguments);
                    case "surface_finish": return SurfaceFinish(model, arguments);
                    case "datum": return Datum(model, arguments);
                    case "geometric_tolerance": return GeometricTolerance(model, arguments);
                    case "section_view": return SectionView(model, arguments);
                    case "detail_view": return DetailView(model, arguments);
                    // I fogli, in SheetOperations.cs. `export_pdf` conta gia'
                    // le pagine del PDF, e quel conteggio valeva sempre 1
                    // perche' non c'era modo di fare un secondo foglio.
                    case "new_sheet": return NewSheet(model, arguments);
                    case "activate_sheet": return ActivateSheet(model, arguments);
                    // La distinta di taglio portata in tavola, e CONFRONTATA
                    // con quella del pezzo: la seconda lettura esisteva gia'.
                    case "drawing_cut_list":
                        return DrawingCutList(model, arguments);
                    // «Il coperchio tocca la base?» — in InterferenceOperations.cs.
                    // Due pezzi compenetrati non danno nessun errore: si aprono,
                    // si esportano, si stampano, e in officina non entrano.
                    case "interference_check":
                        return InterferenceCheck(model, arguments, docState);
                    // Il cartiglio, in TitleBlockOperations.cs. Non lo riempie:
                    // dice quali proprieta' CHIEDE e quali MOSTRA, cosi' il
                    // nome da scrivere si copia invece di indovinarlo.
                    case "title_block": return TitleBlock(model, arguments);
                    case "export_pdf": return ExportPdf(model, arguments);
                    case "save_document": return SaveDocument(model, arguments);

                    // ── saldature ───────────────────────
                    // Stanno in WeldmentOperations.cs: sono un
                    // sottosistema con le sue API e le sue trappole, e
                    // tenerle qui avrebbe fatto un file da duemila righe.
                    //
                    // «rectangular_frame» finisce nello stesso esecutore:
                    // il server lo espande in segmenti e manda gli stessi
                    // parametri di «structural_member», come gia' succede
                    // per le sezioni normalizzate. Due esecutori per la
                    // stessa cosa vorrebbero dire due comportamenti che
                    // divergono alla prima modifica.
                    case "structural_member":
                    case "rectangular_frame": return StructuralMember(model, arguments);
                    case "trim_members": return TrimMembers(model, arguments, docState);
                    case "cut_list": return CutList(model, arguments);
                    case "end_cap": return EndCap(model, arguments, docState);
                    case "gusset": return Gusset(model, arguments, docState);
                    case "weld_bead": return WeldBead(model, arguments, docState);

                    // ── verifiche ───────────────────────
                    // Stanno in CheckOperations.cs. NON DISEGNANO: dicono
                    // com'e' venuto. `cut_list` e' il precedente —
                    // un'operazione di catalogo che legge invece di costruire.
                    case "check_body_geometry":
                        return CheckBodyGeometry(model, arguments, docState);
                    // Cosa non si stampa senza supporti. Sta in
                    // OverhangOperations.cs, e come le altre verifiche non
                    // tocca il pezzo: guarda l'angolo di ogni superficie.
                    case "check_overhangs":
                        return CheckOverhangs(model, arguments, docState);
                    // Le due domande che si fanno prima di ordinare: quanto
                    // pesa, e in che scatola ci sta. Stanno in
                    // MeasureOperations.cs.
                    case "mass_properties":
                        return MassProperties(model, arguments, docState);
                    // Da quali file dipende il documento, e quali mancano (voce Z-B3).
                    // DependencyOperations.cs.
                    case "file_dependencies":
                        return Dependencies(model, arguments, docState);
                    case "body_bounding_box":
                        return BodyBoundingBox(model, arguments, docState);
                    case "minimum_distance":
                        return MinimumDistance(model, arguments, docState);
                    case "measure_between":
                        return MeasureBetween(model, arguments, docState);

                    // ── quello che il pezzo dice di se' ─
                    // Stanno in PropertyOperations.cs. Non disegnano niente:
                    // riempiono il cartiglio e la scheda del PDM, e fanno le
                    // varianti. Le proprieta' del DOCUMENTO e quelle di una
                    // CONFIGURAZIONE sono due elenchi separati che non si
                    // vedono a vicenda — misurato, non dedotto.
                    case "set_property":
                        return SetProperty(model, arguments, docState);
                    case "create_configuration":
                        return CreateConfiguration(model, arguments, docState);
                    case "activate_configuration":
                        return ActivateConfiguration(model, arguments, docState);

                    // ── la marcatura ────────────────────
                    // Sta in TextOperations.cs. E' l'unica operazione che
                    // scrive delle PAROLE nel pezzo, e l'unica in cui la
                    // grandezza di cio' che si disegna non si puo' calcolare
                    // prima: dipende dal font. Si misura e si controlla.
                    case "engrave_text":
                        return EngraveText(model, arguments, docState);

                    // ── togliere una copia ──────────────
                    // Sta in InstanceOperations.cs. Non e' una lavorazione nuova:
                    // e' una scrittura DENTRO una ripetizione gia' fatta, ed e'
                    // l'unica operazione del catalogo che passa da
                    // GetDefinition() + ModifyDefinition().
                    case "suppress_instance":
                        return SuppressInstance(model, arguments, docState);

                    // ── l'angolo di spoglia ─────────────
                    // Sta in DraftOperations.cs. Quattro API per lo stesso
                    // comando, e due con la firma IDENTICA: quella giusta si e'
                    // trovata misurando, non leggendo.
                    case "draft":
                        return Draft(model, arguments, docState);

                    // ── la nervatura ────────────────────
                    // Sta in RibOperations.cs (voce W1.2). InsertRib
                    // PARALLELA allo schizzo (IsNormToSketch = false, come
                    // l'esempio della guida); il verso del materiale si prova
                    // dritto e poi girato.
                    case "rib":
                        return Rib(model, arguments);

                    // ── esportazioni ────────────────────
                    // Stanno in ExportOperations.cs. `export_stl` chiama
                    // `check_body_geometry` PRIMA di scrivere, e rifiuta
                    // se il pezzo non e' un solido chiuso solo: un STL non
                    // porta con se' niente che dica che il pezzo era a posto.
                    case "export_stl":
                        return ExportStl(model, arguments, docState);
                    // STEP e IGES finiscono nello stesso esecutore: cambiano
                    // le preferenze e il modo di rileggere il file, non la
                    // strada. Due esecutori per la stessa cosa vorrebbero dire
                    // due comportamenti che divergono alla prima modifica.
                    case "export_step":
                    case "export_iges":
                        return ExportExchange(model, arguments, operation, docState);
                    default:
                        // QUALUNQUE operazione che porta dei contorni si disegna
                        // con lo stesso esecutore. Cosi' una RICETTA nuova
                        // (una sezione normalizzata, una ruota, un profilo di
                        // domani) si aggiunge al server senza toccare l'add-in
                        // e senza che nessuno debba reinstallare niente.
                        if (arguments != null && arguments.ContainsKey("contours"))
                            return ExtrudeProfile(model, arguments);
                        return Failed(UnknownOperation + operation);
                }
            }
            catch (Exception ex)
            {
                return Failed($"{operation}: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Le sei operazioni
        // ─────────────────────────────────────────────────────────────────

        private Outcome ExtrudeRectangle(IModelDoc2 model, Dictionary<string, object> a)
        {
            double length = Num(a, "length_mm");
            double width = Num(a, "width_mm");
            double thickness = Num(a, "thickness_mm");
            bool centered = Bool(a, "centered", true);
            string plane = Str(a, "plane", "front");

            // The names of the tree before the sketch: the sketch selected below
            // is the one born since (scheme S7), not the last of the tree.
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectPlane(model, plane))
                return Failed($"Non ho trovato il piano «{plane}» nell'albero.");

            var sketches = model.SketchManager;
            sketches.InsertSketch(true);

            double x1 = centered ? Units.M(-length / 2) : 0;
            double y1 = centered ? Units.M(-width / 2) : 0;
            double x2 = centered ? Units.M(length / 2) : Units.M(length);
            double y2 = centered ? Units.M(width / 2) : Units.M(width);
            var sides = sketches.CreateCornerRectangle(x1, y1, 0, x2, y2, 0) as object[];

            // LE DUE QUOTE CHE RENDONO IL PEZZO MODIFICABILE.
            //
            // Si mettono qui, dentro lo schizzo, sul lato orizzontale e su
            // quello verticale. Da questo momento «portami la vasca a 300»
            // e' una modifica di quota invece che un pezzo da rifare, e le
            // quote esistono anche per la tavola.
            int placedDimensions = DimensionTheRectangle(model, sides, x1, y1, x2, y2);

            sketches.InsertSketch(false);

            // Lo schizzo va SELEZIONATO: uscire dallo schizzo non lo seleziona,
            // e la feature lavora su cio' che e' selezionato.
            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            var feature = Extrude(model, thickness, merge: true);
            if (feature == null) return Failed("L'estrusione non e' stata creata." + WithoutSketch(model));
            return Succeeded(feature, new Dictionary<string, object>
            {
                ["length_mm"] = length,
                ["width_mm"] = width,
                ["thickness_mm"] = thickness,
                ["dimensions_on_sketch"] = placedDimensions,
            }, References.PlaneNormal(plane));
        }

        /// <summary>
        /// Mette una quota sul lato orizzontale e una sul lato verticale di
        /// un rettangolo appena disegnato. Ritorna quante ne ha messe.
        ///
        /// COME SI QUOTA VIA API. `IModelDocExtension.AddDimension(X, Y, Z,
        /// Direction)` non prende nessuna entita': lavora su cio' che e'
        /// SELEZIONATO, ed e' l'ennesima chiamata di questo interop fatta
        /// cosi'. Il punto passato non e' l'entita' da quotare — e' DOVE va
        /// scritto il numero. Quindi: si seleziona il lato, si dice dove
        /// mettere il testo, e la quota nasce.
        ///
        /// NON SI SOLLEVA MAI. Un pezzo senza quote e' peggiore di uno
        /// quotato, ma e' comunque un pezzo giusto: se la quotatura non
        /// riesce si va avanti e si scrive quante ne sono state messe, che
        /// e' un numero che chi legge puo' controllare.
        /// </summary>
        private int DimensionTheRectangle(
            IModelDoc2 model, object[] sides,
            double x1, double y1, double x2, double y2)
        {
            if (sides == null || sides.Length == 0) return 0;

            // Il testo va messo FUORI dal rettangolo, se no finisce sopra il
            // disegno. Un sesto del lato e' abbastanza su un pezzo da 10 mm
            // come su uno da 3 metri.
            double outsideX = Math.Max(Math.Abs(x2 - x1) * 0.15, 0.005);
            double outsideY = Math.Max(Math.Abs(y2 - y1) * 0.15, 0.005);

            var toDimension = new List<Dimension>();
            bool horizontalDone = false, verticalDone = false;

            foreach (var o in sides)
            {
                var line = o as ISketchLine;
                if (line == null) continue;

                double[] p1, p2;
                if (!EndsOf(line, out p1, out p2)) continue;

                bool horizontal = Math.Abs(p1[1] - p2[1]) < 1e-9;
                bool vertical = Math.Abs(p1[0] - p2[0]) < 1e-9;
                if (horizontal && horizontalDone) continue;
                if (vertical && verticalDone) continue;
                if (!horizontal && !vertical) continue;

                double tx = (p1[0] + p2[0]) / 2;
                double ty = (p1[1] + p2[1]) / 2;
                if (horizontal) ty = Math.Min(y1, y2) - outsideY;
                else tx = Math.Min(x1, x2) - outsideX;

                toDimension.Add(Dimension.Su(o, new[] { tx, ty, 0.0 }));
                if (horizontal) horizontalDone = true; else verticalDone = true;
                if (horizontalDone && verticalDone) break;
            }

            return DimensionSegments(model, toDimension);
        }

        /// <summary>
        /// Mette una quota su ognuno dei segmenti dati, con il testo nel punto
        /// indicato (metri, coordinate di schizzo). Ritorna quante ne ha messe.
        ///
        /// COME SI QUOTA VIA API. `IModelDocExtension.AddDimension(X, Y, Z,
        /// Direction)` non prende nessuna entita': lavora su cio' che e'
        /// SELEZIONATO, come meta' di questo interop. Il punto passato non e'
        /// l'entita' da quotare — e' DOVE va scritto il numero.
        ///
        /// SI SPEGNE LA FINESTRELLA DEL VALORE, e questa e' la riga che conta.
        /// `AddDimension` mette la quota e poi apre «Modifica» per chiedere
        /// quanto vale, e resta li' ad aspettare una risposta che non
        /// arrivera' mai. Non fallisce, non solleva, non scade: si pianta. In
        /// produzione questo codice gira DENTRO SolidWorks — vorrebbe dire
        /// bloccare il programma di chi ci sta lavorando. Misurato l'11
        /// settembre 2026, dieci minuti di SolidWorks fermo.
        ///
        /// La preferenza si rimette com'era in OGNI caso: e' un'impostazione
        /// di chi usa il programma, non nostra. Stessa regola di `AddToDB`.
        ///
        /// NON SOLLEVA MAI. Un pezzo senza quote e' peggiore di uno quotato, ma
        /// e' comunque un pezzo giusto: se la quotatura non riesce si va avanti
        /// e si scrive quante ne sono state messe, che e' un numero che chi
        /// legge puo' controllare.
        /// </summary>
        /// <summary>
        /// Un segmento da quotare, e DOVE va scritto il numero (metri,
        /// coordinate del modello).
        ///
        /// La Z c'e' e non e' un di piu'. Prima il punto del testo era
        /// `(x, y, 0)`: va bene per uno schizzo su un piano di riferimento,
        /// dove Z e' zero per costruzione, e **non** per uno schizzo su una
        /// faccia qualunque — che e' dove stanno tutti i tagli. Con la Z
        /// inchiodata a zero la quota di un foro su un fianco nasce fuori dal
        /// piano dello schizzo, e SOLIDWORKS la rifiuta senza spiegare.
        /// </summary>
        private class Dimension
        {
            public object Segment;
            public double X, Y, Z;

            public static Dimension Su(object segment, double[] point)
            {
                return new Dimension
                {
                    Segment = segment,
                    X = point[0],
                    Y = point[1],
                    Z = point.Length > 2 ? point[2] : 0,
                };
            }
        }

        private int DimensionSegments(IModelDoc2 model, List<Dimension> toDimension)
        {
            if (model == null || toDimension == null || toDimension.Count == 0) return 0;

            bool askedForTheValue = true;
            try
            {
                askedForTheValue = _sw.GetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                _sw.SetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            }
            catch { }

            // ZERO-E-ZERO: `messe` conta le quote NATE, una per una; una che non
            // nasce non e' una quota. I catch qui sotto chiudono la selezione e
            // rimettono la preferenza: non toccano il conteggio.
            int put = 0;
            foreach (var entry in toDimension)
            {
                try
                {
                    model.ClearSelection2(true);
                    var segment = entry.Segment as ISketchSegment;
                    if (segment == null || !segment.Select4(false, null)) continue;
                    var born = model.Extension.AddDimension(entry.X, entry.Y, entry.Z, 0)
                        as IDisplayDimension;
                    if (born == null) continue;

                    // MARCATA PER IL DISEGNO, ed e' la riga che fa la
                    // differenza fra una tavola quotata e una muta.
                    //
                    // `model_dimensions` importa di serie solo le quote
                    // MARCATE: sono quelle che un progettista ha scelto di
                    // mostrare, e una tavola con tutte le quote del modello e'
                    // illeggibile. Una quota nata da `AddDimension` non e'
                    // marcata, quindi le nostre non arrivavano mai — misurato
                    // il 14/9/2026: pezzo con 11 quote, tavola con 0, e
                    // nemmeno chiedendo «all_dimensions».
                    //
                    // Marcandole qui, la tavola prende ESATTAMENTE le quote che
                    // le operazioni hanno messo apposta, e non quelle di
                    // servizio.
                    try { born.MarkedForDrawing = true; } catch { }
                }
                catch { continue; }
                put++;
            }

            try { model.ClearSelection2(true); } catch { }
            try
            {
                _sw.SetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swInputDimValOnCreate, askedForTheValue);
            }
            catch { }
            return put;
        }

        /// <summary>Gli estremi di una retta di schizzo, in metri.</summary>
        private static bool EndsOf(ISketchLine line, out double[] p1, out double[] p2)
        {
            p1 = null; p2 = null;
            try
            {
                var a = line.GetStartPoint2() as ISketchPoint;
                var b = line.GetEndPoint2() as ISketchPoint;
                if (a == null || b == null) return false;
                p1 = new[] { a.X, a.Y, a.Z };
                p2 = new[] { b.X, b.Y, b.Z };
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// <summary>
        /// Una RUOTA DENTATA cilindrica a denti dritti.
        ///
        /// Come la sezione, non e' un'altra estrusione: e' un modo di
        /// scrivere dei contorni, e finisce nello stesso esecutore.
        ///
        /// La geometria sta in <see cref="SpurGear"/>, che e' il file
        /// piu' delicato della libreria: una traslitterazione sbagliata
        /// dell'evolvente non da' nessun errore, da' una ruota che ingrana
        /// male.
        /// </summary>
        private Outcome SpurGearOp(IModelDoc2 model, Dictionary<string, object> a)
        {
            double rawTeeth, module, thickness;
            string why;
            if (!RequiredNum(a, "teeth", out rawTeeth, out why))
                return Failed("Per una ruota dentata serve «teeth»: " + why);
            if (!RequiredNum(a, "module_mm", out module, out why))
                return Failed("Per una ruota dentata serve «module_mm»: " + why);
            if (!RequiredNum(a, "thickness_mm", out thickness, out why))
                return Failed("Per una ruota dentata serve «thickness_mm»: " + why);

            int teeth = (int)Math.Round(rawTeeth);
            if (Math.Abs(rawTeeth - teeth) > 1e-9)
                return Failed(
                    "I denti sono " + rawTeeth + ": un numero di denti e' " +
                    "intero, e mezzo dente non si taglia.");
            if (teeth < 3)
                return Failed("Una ruota da " + teeth + " denti non e' una ruota.");
            if (module <= 0 || thickness <= 0)
                return Failed("Modulo e spessore devono essere maggiori di zero.");

            string reason;
            var contours = SpurGear.Contours(
                teeth, module,
                Num(a, "pressure_angle_deg", 20.0),
                Num(a, "bore_mm", 0),
                Bool(a, "keyway", false),
                8,
                out reason);
            if (contours == null) return Failed(reason);

            var itsItems = new Dictionary<string, object>
            {
                ["contours"] = contours.ToArray(),
                ["thickness_mm"] = thickness,
            };
            if (a.ContainsKey("plane")) itsItems["plane"] = a["plane"];

            // Il tetto largo: questi punti li ha calcolati una formula, non
            // scritti un modello. Una ruota da 127 denti sono milleventi
            // segmenti, e il tetto dei duecento la rifiuterebbe.
            var outcome = ExtrudeProfile(model, itsItems, MaxGeneratedVertices);

            // I NUMERI DELLA RUOTA, ACCANTO A QUELLI DELL'ESTRUSIONE.
            // Chi ha chiesto z=20 m=2 vuole poter controllare che il
            // diametro esterno sia 44: e' il modo piu' rapido di accorgersi
            // che qualcosa nella matematica non torna, e non si legge dal
            // volume.
            if (outcome != null && outcome.Succeeded && outcome.Measurements != null)
            {
                outcome.Measurements["teeth"] = teeth;
                outcome.Measurements["module_mm"] = module;
                outcome.Measurements["pitch_diameter_mm"] = Math.Round(module * teeth, 4);
                outcome.Measurements["outside_diameter_mm"] = Math.Round(module * (teeth + 2), 4);
                outcome.Measurements["root_diameter_mm"] =
                    Math.Round(module * teeth - 2.5 * module, 4);
                outcome.Measurements["pitch_mm"] = Math.Round(Math.PI * module, 4);
            }
            return outcome;
        }

        /// <summary>
        /// Estrude una SEZIONE NORMALIZZATA: due o tre quote invece di otto
        /// coordinate.
        ///
        /// Non e' un'altra estrusione: e' un modo di scrivere dei contorni.
        /// La ricetta costruisce i contorni e li passa allo stesso esecutore
        /// di `extrude_profile` — due esecutori per la stessa cosa
        /// divergerebbero alla prima modifica.
        ///
        /// Prima questa ricetta viveva solo nel server Python, quindi
        /// l'operazione era **dichiarata nel catalogo e non eseguibile**: dal
        /// ponte rispondeva «operazione sconosciuta» a chi la chiedeva con i
        /// suoi argomenti veri.
        /// </summary>
        private Outcome ExtrudeSection(IModelDoc2 model, Dictionary<string, object> a)
        {
            string typ = Str(a, "variant", "");

            string[] wanted;
            if (string.IsNullOrWhiteSpace(typ) ||
                !Sections.Wanted.TryGetValue(typ.Trim(), out wanted))
                return Failed(
                    "Sezione «" + typ + "» sconosciuta. Ci sono: " +
                    string.Join(", ", Sections.Wanted.Keys) + ".");

            // LE QUOTE CHE SERVONO A QUESTA SEZIONE, e solo quelle.
            // Lo schema del catalogo le dichiara tutte insieme perche' e'
            // una sola operazione, ma un tubolare non ha un diametro e un
            // tondo non ha una parete: se una manca va detto QUALE, non
            // «parametri non validi».
            var m = new Dictionary<string, double>();
            var missingItems = new List<string>();
            foreach (var q in wanted)
            {
                double v;
                string why;
                if (!RequiredNum(a, q, out v, out why)) { missingItems.Add(q); continue; }
                if (v <= 0) return Failed(
                    "«" + q + "» e' " + v + ": una quota di una sezione non puo' " +
                    "essere zero o negativa.");
                m[q] = v;
            }
            if (missingItems.Count > 0)
                return Failed(
                    "Per una sezione «" + typ + "» " +
                    (missingItems.Count == 1 ? "serve " : "servono ") +
                    string.Join(", ", missingItems.ConvertAll(x => "«" + x + "»")) + ".");

            string reason;
            var contours = Sections.Contours(typ, m, out reason);
            if (contours == null) return Failed(reason);

            // Si riscrivono gli argomenti per l'esecutore dei profili,
            // lasciando fuori le quote della sezione: da qui in poi la
            // sagoma e' fatta, e le quote non servono piu' a nessuno.
            var itsItems = new Dictionary<string, object>
            {
                ["contours"] = contours.ToArray(),
                ["thickness_mm"] = Num(a, "length_mm", 0),
            };
            if (a.ContainsKey("plane")) itsItems["plane"] = a["plane"];
            if (a.ContainsKey("symmetric")) itsItems["symmetric"] = a["symmetric"];

            return ExtrudeProfile(model, itsItems, MaxGeneratedVertices);
        }

        /// <summary>
        /// Estrude una sagoma qualsiasi, data come contorni di vertici.
        ///
        /// E' l'operazione che toglie il tetto a tutto il resto: con una
        /// poligonale chiusa si fanno staffe sagomate, angolari, fazzoletti, e
        /// — con DUE contorni, uno esterno e uno interno — i profili cavi.
        /// Le ricette (la ruota dentata, domani il tubolare) non sono altre
        /// operazioni: il server le espande in questi stessi contorni.
        ///
        /// IL PRIMO CONTORNO E' IL BORDO, GLI ALTRI SONO I VUOTI.
        /// SolidWorks estrude la regione compresa: contorni annidati danno un
        /// pezzo forato senza bisogno di un secondo taglio. E' anche il motivo
        /// per cui un tubolare si fa cosi' invece che con l'estrusione sottile:
        /// l'ingombro esterno sta scritto nelle coordinate, non dipende da un
        /// verso di parete che si puo' sbagliare in silenzio.
        ///
        /// AddToDB durante il disegno: senza, SolidWorks prova a inferire una
        /// relazione a ogni segmento, e su cinquecento punti di una ruota
        /// dentata sono minuti invece di secondi. Si rimette com'era in ogni
        /// caso, anche se qualcosa va storto: e' un'impostazione dell'ambiente
        /// di chi lavora, non nostra.
        /// </summary>
        private Outcome ExtrudeProfile(
            IModelDoc2 model, Dictionary<string, object> a,
            int maxVertices = MaxModelVertices)
        {
            var contours = Contours(a, out string reason, out string noteContours, maxVertices);
            if (contours == null) return Failed(reason);

            double thickness = Num(a, "thickness_mm");
            bool symmetric = Bool(a, "symmetric", false);
            string plane = Str(a, "plane", "front");

            // Before the sketch: it is selected, and locked after the
            // extrusion, as the one sketch born since (scheme S7).
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectPlane(model, plane))
                return Failed($"Non ho trovato il piano «{plane}» nell'albero.");

            var sketches = model.SketchManager;
            bool addToDbBefore = false;
            bool displayBefore = false;
            try { addToDbBefore = sketches.AddToDB; displayBefore = sketches.DisplayWhenAdded; } catch { }

            int segments = 0;
            try
            {
                sketches.InsertSketch(true);
                try { sketches.AddToDB = true; sketches.DisplayWhenAdded = false; } catch { }

                foreach (var contour in contours)
                {
                    segments += DrawContour(sketches, contour);
                }
            }
            finally
            {
                try { sketches.AddToDB = addToDbBefore; sketches.DisplayWhenAdded = displayBefore; } catch { }
            }
            sketches.InsertSketch(false);

            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            var feature = Extrude(model, thickness, merge: true, symmetric: symmetric);
            if (feature == null) return Failed("L'estrusione non e' stata creata." + WithoutSketch(model));

            // The sketch is locked AFTER the extrusion: a FIXED relation added
            // while the sketch was still being drawn raised RPC_E_SERVERFAULT
            // in the probe of 28/9/2026 (finding M5, case R4, which passed an
            // object[]: the typed array was measured only after the feature).
            string noteBlock;
            int fixedRelations;
            bool locked = LockSketch(model, sketchNamesBefore, out noteBlock, out fixedRelations);

            var measurements = new Dictionary<string, object>
            {
                ["sketch_locked"] = locked,
                ["fixed_relations"] = fixedRelations,
                ["contours"] = contours.Count,
                ["segments"] = segments,
                ["thickness_mm"] = thickness,
            };
            // Up to library 0.141.0 the note was computed and dropped: whoever
            // reads a sketch that is not locked must be able to read why.
            if (noteBlock != null) measurements["sketch_lock_note"] = noteBlock;
            // I contorni staccati dal primo non si rifiutano, si DICONO: un
            // contorno fuori dal bordo esterno e' una regione a se', ed e'
            // legittimo (quattro alette su una faccia). Ma diventa un corpo in
            // piu', e chi credeva di fare un foro deve poterlo leggere.
            if (noteContours != null) measurements["note_contours"] = noteContours;
            return Succeeded(feature, measurements, References.PlaneNormal(plane));
        }

        /// <summary>
        /// Disegna un contorno, qualunque forma abbia. Ritorna quante entita'
        /// ha creato.
        ///
        /// TRE FORME, E OGNUNA ESISTE PER UNA RAGIONE
        ///  * cerchio   — un cerchio VERO. Approssimarlo a 48 segmenti inscritti
        ///                mette il materiale dentro la circonferenza: su un foro
        ///                da 20 mm invade di 0,021 mm e l'albero non entra.
        ///  * composito — tratti diversi in fila: linee dove e' una retta,
        ///                SPLINE dove e' una curva. Il fianco a evolvente di un
        ///                dente e' una curva, e a segmenti restava sfaccettato
        ///                con 528 entita' nello schizzo.
        ///  * poligonale— la spezzata chiusa, che resta la forma naturale per
        ///                una staffa o un tubolare.
        /// </summary>
        private int DrawContour(ISketchManager sketches, Contour contour)
        {
            if (contour.Circle)
            {
                sketches.CreateCircleByRadius(
                    Units.M(contour.Center[0]), Units.M(contour.Center[1]), 0,
                    Units.M(contour.Diameter / 2.0));
                return 1;
            }

            int done = 0;
            foreach (var stroke in contour.Strokes)
            {
                if (stroke.Spline)
                {
                    // TRIPLES x,y,z per point, in metres, in one flat array
                    // (ISketchManager~CreateSpline3.html, Remarks: "PointData
                    // with at least six coordinates ... [start_pt_x, start_pt_y,
                    // start_pt_z, end_pt_x, end_pt_y, end_pt_z]"). PAIRS break it
                    // with no error: probe_spline3 (Z-C21.3) cases S4, S6 and S8,
                    // 27 or 28 splines of 34 and no extrusion.
                    var data = new double[stroke.Points.Count * 3];
                    for (int i = 0; i < stroke.Points.Count; i++)
                    {
                        data[i * 3] = Units.M(stroke.Points[i][0]);
                        data[i * 3 + 1] = Units.M(stroke.Points[i][1]);
                        data[i * 3 + 2] = 0;
                    }
                    if (CreateSplineThroughPoints(sketches, data) != null) done++;
                }
                else
                {
                    for (int i = 0; i < stroke.Points.Count - 1; i++)
                    {
                        var da = stroke.Points[i];
                        var a2 = stroke.Points[i + 1];
                        sketches.CreateLine(
                            Units.M(da[0]), Units.M(da[1]), 0,
                            Units.M(a2[0]), Units.M(a2[1]), 0);
                        done++;
                    }
                }
            }
            return done;
        }

        /// <summary>
        /// SimulateNaturalEnds of the spline call: TRUE, and it is a measure,
        /// not a taste. probe_spline3 (Z-C21.3), case S5,
        /// CreateSpline3(triples, null, null, true), gave the bench gear of the
        /// obsolete CreateSpline exactly: 8808.586 mm3, box 37.787 x 10 x
        /// 37.938 mm, all 34 splines read back within 1e-4 mm. FALSE (S3, and
        /// S7 with AddToDB off) gives 8806.568 mm3, which
        /// the_seventeen_tooth_gear_can_be_made (8808.59 +-0.5) turns red. The
        /// guide: "SimulateNaturalEnds with true for zero curvature end
        /// conditions or false to maintain curvature at the ends".
        /// </summary>
        internal const bool SplineSimulateNaturalEnds = true;

        /// <summary>
        /// ONE 2D SPLINE THROUGH THE POINTS, with CreateSpline3 (L1-1, Z-C21.13).
        ///
        /// Up to library 0.143.0 this was the obsolete CreateSpline
        /// (ISketchManager~CreateSpline.html: "Obsolete. Superseded by
        /// ISketchManager::CreateSpline2", which CreateSpline3 supersedes in
        /// turn). The probe's own verdict line printed "keep CreateSpline
        /// frozen": its decision rule only counted SimulateNaturalEnds false,
        /// while its S5 line (true) reproduced the gear exactly
        /// (<see cref="SplineSimulateNaturalEnds"/>).
        ///
        /// ADDTODB OFF AND DISPLAYWHENADDED ON AROUND THE CALL.
        /// ISketchManager~CreateSpline3.html, Remarks: "Does not work with
        /// ISketchManager::AddToDB or ISketchManager::DisplayWhenAdded", and
        /// both callers draw with AddToDB on and DisplayWhenAdded off
        /// (ExtrudeProfile and the revolved profile). The probe measured the
        /// switch with false (S7 = S3: the same volume, every spline exact), so
        /// on this gear the state does not change the geometry; the pair true +
        /// AddToDB off was not probed on its own, and the gear case is its
        /// judge. The state is read first and put back right after the call,
        /// whatever happens; if it cannot be read it is not touched.
        ///
        /// THE RETURN IS READ: "ISketchSegment or null if an error" (the guide).
        /// The obsolete call's return was never read, and a spline that was not
        /// born was counted all the same (L1-1, second half). The probe: 34
        /// calls, 0 null, every return an ISketchSegment. Status is "empty for
        /// 2D splines" (the guide; the probe read null) and is not used.
        /// </summary>
        private static ISketchSegment CreateSplineThroughPoints(ISketchManager sketches, double[] triples)
        {
            bool addToDbBefore = false, displayBefore = false, stateRead = false;
            try
            {
                addToDbBefore = sketches.AddToDB;
                displayBefore = sketches.DisplayWhenAdded;
                stateRead = true;
            }
            catch { }
            object created = null;
            try
            {
                if (stateRead)
                {
                    try { sketches.AddToDB = false; sketches.DisplayWhenAdded = true; } catch { }
                }
                created = sketches.CreateSpline3(triples, null, null, SplineSimulateNaturalEnds, out _);
            }
            finally
            {
                if (stateRead)
                {
                    try { sketches.AddToDB = addToDbBefore; sketches.DisplayWhenAdded = displayBefore; } catch { }
                }
            }
            return created as ISketchSegment;
        }

        /// <summary>
        /// LOCKS THE SKETCH WHERE IT IS.
        ///
        /// A generated sketch without relations stays UNDER-DEFINED: a careless
        /// drag moves a vertex and the part changes without anyone knowing. The
        /// customer noticed it looking at the sketch of the first gear.
        ///
        /// A FIXED relation (swConstraintType_FIXED = 17), NOT FullyDefineSketch:
        /// that one does not measure, it adds relations and MOVES the geometry to
        /// satisfy them (two nearly parallel sides become parallel, and the sketch
        /// turns fully defined and a few tenths away from the confirmed one).
        ///
        /// THE SKETCH MUST BE OPEN AND THE ARRAY TYPED (finding M5,
        /// probe_fixed_relation of 28/9/2026: an L profile of six lines extruded
        /// 10 mm, 11000 mm3, the sketch found by JustCreatedSketch after the
        /// extrusion):
        ///   * sketch closed, object[] (this method up to library 0.141.0):
        ///     AddRelation returns null, 0 FIXED relations before and after the
        ///     rebuild, and the sketch was declared locked all the same;
        ///   * EditSketch, object[]: RPC_E_SERVERFAULT, 0 relations;
        ///   * EditSketch, SketchSegment[] (DispatchWrapper[] the same): an
        ///     ISketchRelation back, 6 FIXED relations read back after
        ///     InsertSketch(true) and EditRebuild3, status 2 (under constrained)
        ///     -> 3 (fully constrained), volume 11000 -> 11000, no sketch left
        ///     active.
        /// ISketchRelationManager~AddRelation.html (Remarks): "A sketch must be
        /// active for this method to have any effect." IModelDoc2~EditSketch.html:
        /// "If the selection is a feature, its underlying sketch is placed in edit
        /// mode." SketchSegment[] and not object[] is the rule of the structural
        /// member group too (WeldmentOperations.cs, measured 9/9/2026).
        ///
        /// THE ANSWER IS WHAT IS READ BACK, not what the call returned: the FIXED
        /// relations are counted before the call and again after leaving the
        /// sketch and rebuilding, on the sketch taken again from its feature. The
        /// sketch is locked only when every segment got its relation;
        /// <paramref name="fixedRelations"/> is the count that was added.
        ///
        /// A failure does not fail the operation: the part is right all the same,
        /// it is only editable by mistake. It goes in the result (the note says
        /// why) and the plan goes on.
        ///
        /// WHICH SKETCH (scheme S7, Z-C21.5): the one ProfileFeature born since
        /// <paramref name="namesBeforeSketch"/>, the names the caller took before
        /// opening the sketch, found again in the tree after the feature as the
        /// probe found it. A sketch used by a feature stays in the top-level
        /// walk of IFirstFeature (measured 14/9/2026, see WithoutSketch), and
        /// the feature itself is no ProfileFeature, so it is still the one born.
        /// Until library 0.146.0 it was the last sketch of the tree.
        /// </summary>
        private bool LockSketch(IModelDoc2 model, HashSet<string> namesBeforeSketch, out string note, out int fixedRelations)
        {
            note = null;
            fixedRelations = 0;
            int sketchesBorn;
            IFeature sketchFeature = JustCreatedSketch(model, namesBeforeSketch, out sketchesBorn);
            var sketch = sketchFeature?.GetSpecificFeature2() as ISketch;
            if (sketch == null)
            {
                note = sketchesBorn > 1
                    ? sketchesBorn + " sketches born since the sketch was opened: which one to lock is not known"
                    : "sketch not found after the feature";
                return false;
            }
            int before = FixedRelationCount(sketch);
            if (before < 0) { note = "the relations of the sketch cannot be read"; return false; }

            int segmentCount = 0;
            bool inEdit = false;
            try
            {
                model.ClearSelection2(true);
                if (!sketchFeature.Select2(false, 0)) note = "the sketch could not be selected";
                else
                {
                    model.EditSketch();
                    inEdit = true;
                    if (model.SketchManager.ActiveSketch == null) note = "EditSketch did not open the sketch";
                    else
                    {
                        // Taken again from the feature once in edit mode, as the probe did.
                        sketch = sketchFeature.GetSpecificFeature2() as ISketch;
                        var segments = sketch?.GetSketchSegments() as object[];
                        if (segments == null || segments.Length == 0) note = "the sketch has no segments";
                        else
                        {
                            segmentCount = segments.Length;
                            var typedSegments = new SketchSegment[segments.Length];
                            for (int i = 0; i < segments.Length; i++) typedSegments[i] = (SketchSegment)segments[i];
                            var relations = sketch.RelationManager;
                            if (relations == null) note = "the sketch has no relation manager";
                            else relations.AddRelation(typedSegments, (int)swConstraintType_e.swConstraintType_FIXED);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                note = "AddRelation FIXED failed: " + ex.Message;
            }
            finally
            {
                // Leave the sketch only if one is open: InsertSketch(true) with
                // no active sketch and a feature selected would OPEN one.
                bool stillOpen = false;
                try { stillOpen = model.SketchManager.ActiveSketch != null; } catch { }
                if (stillOpen) { try { model.SketchManager.InsertSketch(true); } catch { } }
                if (inEdit || stillOpen) { try { model.EditRebuild3(); } catch { } }
                try { model.ClearSelection2(true); } catch { }
            }

            int after = FixedRelationCount(sketchFeature.GetSpecificFeature2() as ISketch);
            if (after < 0)
            {
                note = (note == null ? "" : note + "; ") + "the relations cannot be read back after the rebuild";
                return false;
            }
            fixedRelations = after - before;
            if (note != null) return false;
            if (segmentCount == 0 || fixedRelations < segmentCount)
            {
                note = fixedRelations + " FIXED relation(s) read back after the rebuild for " + segmentCount +
                       " segment(s): the sketch is not locked";
                return false;
            }
            return true;
        }

        /// <summary>
        /// The FIXED relations (swConstraintType_FIXED = 17) of a sketch, read
        /// with GetRelations(swAll); -1 when they cannot be read.
        /// ISketchRelationManager~GetRelations.html (Remarks): "Because some of
        /// the objects may be NULL, you should check for this before accessing
        /// them."
        /// </summary>
        private static int FixedRelationCount(ISketch sketch)
        {
            if (sketch == null) return -1;
            try
            {
                var manager = sketch.RelationManager;
                if (manager == null) return -1;
                var relations = manager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[];
                if (relations == null) return 0;
                int count = 0;
                foreach (var item in relations)
                {
                    var relation = item as ISketchRelation;
                    if (relation != null && relation.GetRelationType() == (int)swConstraintType_e.swConstraintType_FIXED) count++;
                }
                return count;
            }
            catch { return -1; }
        }

        /// <summary>Un contorno letto dal piano: cerchio, composito o spezzata.</summary>
        /// <summary>
        /// Quanti contorni al massimo in una sagoma sola.
        ///
        /// I limiti qui sono di CONTEGGIO, non di quota: quante volte si puo'
        /// spezzare una linea, non quanto puo' essere lunga. Una piastra da tre
        /// metri e' legittima; una sagoma da cinquemila vertici scritta da un
        /// modello quasi mai.
        /// </summary>
        private const int MaxContours = 20;

        /// <summary>Vertici massimi per contorno, quando li scrive un modello.</summary>
        private const int MaxModelVertices = 200;

        /// <summary>
        /// Vertici massimi per contorno, quando li genera una ricetta.
        ///
        /// I contorni che escono da <see cref="SpurGear"/> o da
        /// <see cref="Sections"/> non passano dalla testa di nessuno e possono
        /// essere molto piu' fitti: una ruota da 127 denti sono milleventi
        /// segmenti, ed e' giusto cosi'.
        /// </summary>
        private const int MaxGeneratedVertices = 4000;

        private class Contour
        {
            public bool Circle;
            public double[] Center = new double[2];
            public double Diameter;
            public List<Stroke> Strokes = new List<Stroke>();
        }

        private class Stroke
        {
            public bool Spline;
            public List<double[]> Points = new List<double[]>();
        }

        /// <summary>
        /// I contorni dal piano. Null con il motivo se il dato non e' quello che
        /// deve essere: qui si fallisce invece di tirare a indovinare, perche'
        /// un vertice letto male non da' errore — da' una sagoma diversa.
        /// </summary>
        private static List<Contour> Contours(
            Dictionary<string, object> a, out string reason, out string note,
            int maxVertices = MaxModelVertices)
        {
            reason = null;
            note = null;
            object raw;
            if (a == null || !a.TryGetValue("contours", out raw) || raw == null)
            {
                reason = "Non mi e' arrivata nessuna sagoma da disegnare.";
                return null;
            }
            var list = raw as System.Collections.IEnumerable;
            if (list == null) { reason = "La sagoma non e' un elenco di contorni."; return null; }

            var outside = new List<Contour>();
            foreach (var c in list)
            {
                var entry = c as Dictionary<string, object>;
                if (entry == null)
                {
                    // Forma vecchia: un contorno era una lista nuda di punti.
                    // Si continua ad accettarla, cosi' un piano proposto prima
                    // di questa versione non diventa illeggibile a meta' strada.
                    var points = Points(c, out reason);
                    if (points == null) return null;
                    var old = new Contour();
                    old.Strokes.Add(new Stroke { Points = Close(points) });
                    outside.Add(old);
                    continue;
                }

                // DUE MODI DI SCRIVERE UN CERCHIO, E IL CATALOGO NE
                // RACCOMANDA UNO CHE IL PARSER NON CAPIVA.
                //
                // La descrizione di `extrude_profile` porta questo esempio
                // per esteso — «TUBO diametro esterno 60 spessore 5»:
                //
                //   [{"circle": [0,0], "diameter_mm": 60},
                //    {"circle": [0,0], "diameter_mm": 50}]
                //
                // Il parser capiva solo la forma esplicita con `variant` e
                // `center`, quindi un modello che copiava l'esempio del
                // catalogo finiva nel ramo poligonale, cercava `vertices`,
                // non lo trovava e falliva. Una divergenza fra cio' che si
                // dichiara e cio' che si sa fare, dentro il file che esiste
                // per non averne — e proprio sulla forma che il catalogo
                // consiglia per fori e tondi, dove sbagliare costa un pezzo
                // in cui l'albero non entra.
                bool circleKey = entry.ContainsKey("circle");
                var typ = Convert.ToString(
                    entry.TryGetValue("kind", out var t) ? t
                    : circleKey ? "circle" : "polygonal");
                if (string.Equals(typ, "circle", StringComparison.OrdinalIgnoreCase))
                {
                    object rawCenter = null;
                    if (entry.TryGetValue("center", out var there) && there != null) rawCenter = there;
                    else if (circleKey) rawCenter = entry["circle"];
                    var center = Points(
                        rawCenter != null ? new[] { rawCenter } : null, out reason);
                    if (center == null || center.Count != 1)
                    {
                        reason = "Il centro del cerchio non e' [x, y]. Si scrive \"circle\": [x, y] oppure \"variant\": \"circle\" con \"center\": [x, y].";
                        return null;
                    }

                    // MANCANTE E NON NUMERICO SONO DUE COSE DIVERSE.
                    // Prima finivano tutte e due in «non e' un numero»,
                    // perche' la chiave assente sollevava dentro lo stesso
                    // try. Chi ha scritto {"circle":[0,0]} e basta va
                    // mandato a mettere il diametro, non a controllare un
                    // numero che non ha scritto.
                    object rawDiameter;
                    if (!entry.TryGetValue("diameter_mm", out rawDiameter) || rawDiameter == null)
                    {
                        reason = "Un cerchio senza \"diameter_mm\": un centro da solo non dice quanto e' grande.";
                        return null;
                    }
                    double diameter;
                    try
                    {
                        diameter = Convert.ToDouble(
                            rawDiameter, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch { reason = "Il diametro del cerchio non e' un numero."; return null; }
                    if (diameter <= 0)
                    {
                        reason = "Il diametro del cerchio e' " + diameter + ": un cerchio di diametro zero o negativo non si disegna.";
                        return null;
                    }

                    outside.Add(new Contour { Circle = true, Center = center[0], Diameter = diameter });
                    continue;
                }

                if (string.Equals(typ, "composite", StringComparison.OrdinalIgnoreCase))
                {
                    var contour = new Contour();
                    var strokes = entry.TryGetValue("strokes", out var tr) ? tr as System.Collections.IEnumerable : null;
                    if (strokes == null) { reason = "Un contorno composito non ha tratti."; return null; }
                    foreach (var x in strokes)
                    {
                        var tt = x as Dictionary<string, object>;
                        if (tt == null) { reason = "Un tratto non e' leggibile."; return null; }
                        var points = Points(tt.TryGetValue("points", out var pp) ? pp : null, out reason);
                        if (points == null) return null;
                        if (points.Count < 2) { reason = "Un tratto ha meno di due punti."; return null; }
                        var itemName = Convert.ToString(tt.TryGetValue("stroke", out var nt) ? nt : "lines");
                        contour.Strokes.Add(new Stroke
                        {
                            Spline = string.Equals(itemName, "spline", StringComparison.OrdinalIgnoreCase),
                            Points = points,
                        });
                    }
                    if (contour.Strokes.Count == 0) { reason = "Un contorno composito e' vuoto."; return null; }
                    outside.Add(contour);
                    continue;
                }

                var vertices = Points(entry.TryGetValue("vertices", out var vv) ? vv : null, out reason);
                if (vertices == null) return null;
                if (vertices.Count < 3) { reason = "Un contorno ha meno di tre punti: non si chiude."; return null; }
                var polygonal = new Contour();
                polygonal.Strokes.Add(new Stroke { Points = Close(vertices) });
                outside.Add(polygonal);
            }
            if (outside.Count == 0) { reason = "La sagoma non ha nessun contorno."; return null; }
            if (outside.Count > MaxContours)
            {
                reason = outside.Count + " contorni sono troppi (al massimo " + MaxContours + ").";
                return null;
            }
            if (!SensibleOutline(outside, maxVertices, out reason, out note)) return null;
            return outside;
        }

        /// <summary>
        /// I PUNTI DI UN CONTORNO, per i controlli geometrici e basta.
        ///
        /// Un cerchio si campiona a 64 lati: serve solo a sapere se sta dentro
        /// il bordo esterno, non a disegnarlo. Disegnato resta un cerchio vero.
        /// </summary>
        private static List<double[]> PointsOf(Contour c)
        {
            if (c.Circle)
            {
                const int sides = 64;
                double r = c.Diameter / 2.0;
                var outside = new List<double[]>(sides);
                for (int i = 0; i < sides; i++)
                {
                    double t = 2 * Math.PI * i / sides;
                    outside.Add(new[] { c.Center[0] + r * Math.Cos(t), c.Center[1] + r * Math.Sin(t) });
                }
                return outside;
            }
            var all = new List<double[]>();
            foreach (var stroke in c.Strokes) all.AddRange(stroke.Points);
            return ClosedShape.Ring(all);
        }

        /// <summary>
        /// I MODI IN CUI UNA SAGOMA SBAGLIATA NON DA' ERRORE.
        ///
        /// Nessuno di questi controlli protegge da un crash: proteggono da un
        /// pezzo che si costruisce benissimo e non e' quello chiesto. E' il
        /// motivo per cui stanno qui e non nello schema — lo schema guarda la
        /// FORMA del dato, questo guarda la geometria che ne esce.
        ///
        ///   1. un contorno composito i cui tratti non si attaccano: non da'
        ///      errore, non si estrude e basta;
        ///   2. un contorno che si incrocia da solo: SOLIDWORKS costruisce le
        ///      regioni che riesce e da' un pezzo plausibile e sbagliato;
        ///   3. un contorno di area nulla;
        ///   4. **un vuoto che esce dal bordo esterno**, che invece di forare
        ///      il pezzo lo TAGLIA. Il solido che ne esce e' valido, e nessuno
        ///      dice niente.
        ///
        /// Il quarto e' il piu' insidioso dei quattro, perche' il pezzo
        /// sbagliato ha l'aria di essere venuto bene.
        /// </summary>
        private static bool SensibleOutline(
            List<Contour> contours, int maxVertices, out string reason, out string note)
        {
            reason = null;
            note = null;
            for (int i = 0; i < contours.Count; i++)
            {
                var entry = contours[i];
                string label = i == 0 ? "Il contorno esterno" : "Il vuoto n. " + i;

                // Un cerchio non si puo' incrociare da solo e non ha area
                // nulla: campionarlo per scoprirlo sarebbe lavoro sprecato.
                if (entry.Circle) continue;

                // 1. I TRATTI SI ATTACCANO?
                // La tolleranza e' un millesimo di millimetro e non un
                // micron: i punti dei contorni generati escono arrotondati al
                // decimillesimo, e pretendere di piu' rifiuterebbe geometria
                // giusta.
                for (int j = 0; j < entry.Strokes.Count; j++)
                {
                    var fine = entry.Strokes[j].Points[entry.Strokes[j].Points.Count - 1];
                    var after = entry.Strokes[(j + 1) % entry.Strokes.Count].Points[0];
                    if (!ClosedShape.Neighbors(fine, after, 1e-3))
                    {
                        reason = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0}: il tratto {1} finisce in ({2:0.###}, {3:0.###}) e il " +
                            "seguente comincia in ({4:0.###}, {5:0.###}): il contorno non " +
                            "si chiude, e un contorno aperto non da' errore — non si " +
                            "estrude e basta.",
                            label, j + 1, fine[0], fine[1], after[0], after[1]);
                        return false;
                    }
                }

                var ring = PointsOf(entry);
                if (ring.Count > maxVertices)
                {
                    reason = label + " ha " + ring.Count + " vertici (al massimo " +
                             maxVertices + ").";
                    return false;
                }
                if (ring.Count < 3)
                {
                    reason = label + " ha " + ring.Count + " vertici distinti: con " +
                             "meno di tre non si chiude una sagoma.";
                    return false;
                }

                // 2. SI INCROCIA DA SOLO?
                int location = ClosedShape.WhereItCrosses(ring);
                if (location >= 0)
                {
                    var p = ring[location];
                    // MISURATO, e vale la pena dirlo com'e': su un papillon
                    // SOLIDWORKS si ferma, ma l'unica cosa che dice e'
                    // «L'estrusione non e' stata creata» — che non aiuta a
                    // capire dove sia il guaio. Questo messaggio esiste per
                    // dare il punto, non per aggiungere un rifiuto.
                    reason = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0} si incrocia da solo: il lato che parte da ({1:0.###}, " +
                        "{2:0.###}) ne attraversa un altro. SOLIDWORKS si fermerebbe " +
                        "comunque, ma direbbe soltanto «L'estrusione non e' stata " +
                        "creata». Di solito e' l'ordine dei vertici.",
                        label, p[0], p[1]);
                    return false;
                }

                // 3. AREA NULLA?
                if (ClosedShape.Area(ring) <= 0)
                {
                    reason = label + " ha area nulla: i suoi punti stanno tutti " +
                             "sulla stessa retta.";
                    return false;
                }
            }

            // 4. I CONTORNI DOPO IL PRIMO: DENTRO, A CAVALLO, O FUORI?
            //
            // TRE CASI, E SOLO UNO E' UN ERRORE. La prima stesura ne rifiutava
            // due, e cosi' rifiutava lavoro legittimo:
            //
            //   dentro     e' un vuoto. Normale.
            //   a cavallo  taglia il bordo esterno. Incoerente sempre: non e'
            //              ne' un vuoto ne' una regione, e SOLIDWORKS si ferma
            //              dicendo soltanto «L'estrusione non e' stata creata».
            //   fuori      e' una REGIONE A SE'. E' legittimo: quattro alette
            //              su una faccia sono quattro contorni staccati.
            //
            // Misurato il 14/9/2026: il piano di un coperchio con quattro
            // alette veniva rifiutato da questo controllo, ed era un piano
            // giusto. **Un rifiuto di cio' che e'
            // corretto e' il peggiore dei due modi di sbagliare** — e' la
            // lezione del guscio, e questo controllo l'aveva dimenticata.
            //
            // Del terzo caso si DICE, forte: un contorno staccato diventa un
            // corpo in piu', e chi credeva di fare un foro si trova un disco
            // (misurato: piastra 100x100 con un cerchio Ø30 a 200 mm, 2 corpi,
            // «succeeded»). Ma lo si dice invece di impedirlo, e chi legge ha in
            // mano anche `bodies_total` per controllare.
            if (contours.Count > 1)
            {
                var outer = PointsOf(contours[0]);
                var detached = new List<int>();
                for (int i = 1; i < contours.Count; i++)
                {
                    // TRE CASELLE, NON DUE. Un vertice ESATTAMENTE sul bordo
                    // esterno non e' ne' dentro ne' fuori, e costringerlo a
                    // essere uno dei due faceva dipendere il verdetto dal LATO:
                    // misurato su una piastra 100x100, una cava a filo del
                    // bordo destro veniva rifiutata come «a cavallo» e la
                    // stessa cava specchiata a sinistra passava.
                    //
                    // I DUE LATI DEVONO DARE LA STESSA RISPOSTA, E LA RISPOSTA
                    // E' NO. Misurato: cava a destra, cava a sinistra e rombo
                    // che tocca in un solo vertice falliscono tutti e tre con
                    // «L'estrusione non e' stata creata». Due contorni che si
                    // toccano non delimitano una regione; un'asola aperta si
                    // disegna come UN contorno solo con otto punti.
                    // Il guadagno non e' far passare di piu': e' rispondere
                    // uguale da tutt'e due le parti, e dirlo meglio di come lo
                    // dice SOLIDWORKS.
                    int inside = 0, outside = 0, onBorder = 0;
                    double[] firstOutside = null;
                    foreach (var point in PointsOf(contours[i]))
                    {
                        switch (ClosedShape.WhereItIs(point, outer))
                        {
                            case WhereItIsFound.Inside: inside++; break;
                            case WhereItIsFound.OnBorder: onBorder++; break;
                            default: outside++; if (firstOutside == null) firstOutside = point; break;
                        }
                    }

                    if (inside > 0 && outside > 0)
                    {
                        reason = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "Il contorno n. {0} sta A CAVALLO del bordo esterno: il punto " +
                            "({1:0.###}, {2:0.###}) e' fuori e altri sono dentro. Non e' " +
                            "ne' un vuoto ne' una regione a se', e SOLIDWORKS si ferma " +
                            "dicendo soltanto «L'estrusione non e' stata creata». " +
                            "Spostalo tutto dentro se e' un vuoto, tutto fuori se e' un " +
                            "pezzo a parte.",
                            i, firstOutside[0], firstOutside[1]);
                        return false;
                    }
                    // TOCCA IL BORDO: non si estrude, e si dice perche'.
                    // Non e' «a cavallo» — non c'e' niente fuori — ed e' un
                    // caso a se' che merita il suo messaggio: chi l'ha scritto
                    // voleva un'asola aperta, e la strada per averla c'e'.
                    if (onBorder > 0)
                    {
                        reason = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "Il contorno n. {0} TOCCA il bordo esterno in {1} " +
                            (onBorder == 1 ? "punto" : "punti") + ". Due contorni che si " +
                            "toccano non delimitano una regione, e SOLIDWORKS si ferma " +
                            "dicendo soltanto «L'estrusione non e' stata creata». " +
                            "Se volevi un'asola aperta sul bordo, disegnala come UN " +
                            "contorno solo: il bordo esterno che entra e rientra. Se " +
                            "volevi un foro, spostalo dentro di almeno un decimo.",
                            i, onBorder);
                        return false;
                    }

                    // Staccato vuol dire che non tocca nemmeno.
                    if (inside == 0) detached.Add(i);
                }

                if (detached.Count > 0)
                    note =
                        (detached.Count == 1
                            ? "Il contorno n. " + detached[0] + " non sta dentro il primo"
                            : "I contorni n. " + string.Join(", ", detached) +
                              " non stanno dentro il primo") +
                        ": non sono vuoti, sono regioni a se'. Vengono estrusi lo stesso, " +
                        "e il pezzo puo' avere piu' di un corpo — guarda «bodies_total». " +
                        "Se volevi dei fori, vanno spostati dentro il contorno esterno.";
            }
            return true;
        }

        /// <summary>La spezzata chiusa: si ripete il primo punto in coda.</summary>
        private static List<double[]> Close(List<double[]> points)
        {
            var outside = new List<double[]>(points);
            outside.Add(points[0]);
            return outside;
        }

        /// <summary>
        /// Una lista di punti [x, y] da un dato JSON. Null con il motivo se
        /// qualcosa non e' un numero: un vertice letto male non da' errore, da'
        /// una sagoma diversa.
        /// </summary>
        private static List<double[]> Points(object raw, out string reason)
        {
            reason = null;
            if (raw == null) { reason = "Mancano i punti."; return null; }
            var list = raw as System.Collections.IEnumerable;
            if (list == null) { reason = "I punti non sono un elenco."; return null; }
            var points = new List<double[]>();
            foreach (var p in list)
            {
                var pair = p as System.Collections.IEnumerable;
                if (pair == null) { reason = "Un punto non e' una coppia [x, y]."; return null; }
                var values = new List<double>();
                foreach (var v in pair)
                {
                    try
                    {
                        values.Add(Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture));
                    }
                    catch { reason = "Un punto contiene un valore che non e' un numero: " + v; return null; }
                }
                if (values.Count != 2) { reason = "Un punto non ha esattamente due coordinate."; return null; }
                if (double.IsNaN(values[0]) || double.IsNaN(values[1])
                    || double.IsInfinity(values[0]) || double.IsInfinity(values[1]))
                { reason = "Un punto ha coordinate non utilizzabili."; return null; }
                points.Add(new[] { values[0], values[1] });
            }
            if (points.Count == 0) { reason = "Un elenco di punti e' vuoto."; return null; }
            return points;
        }

        private Outcome ExtrudeCircle(IModelDoc2 model, Dictionary<string, object> a)
        {
            double diameter = Num(a, "diameter_mm");
            double height = Num(a, "height_mm");
            string plane = Str(a, "plane", "front");

            // The names before the sketch (scheme S7): see ExtrudeRectangle.
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectPlane(model, plane))
                return Failed($"Non ho trovato il piano «{plane}» nell'albero.");

            var sketches = model.SketchManager;
            sketches.InsertSketch(true);
            var circle = sketches.CreateCircleByRadius(0, 0, 0, Units.M(diameter / 2));

            // IL DIAMETRO, sullo schizzo. Un pezzo tondo senza la quota del
            // diametro e' modificabile quanto una vasca senza i lati: cioe'
            // per niente. Il testo va messo fuori dal cerchio, in diagonale,
            // se no finisce sopra il disegno.
            double far = Units.M(diameter / 2) * 1.4 + 0.005;
            int placedDimensions = DimensionSegments(model,
                new List<Dimension> { Dimension.Su(circle, new[] { far, far, 0.0 }) });

            sketches.InsertSketch(false);

            // Lo schizzo va SELEZIONATO: uscire dallo schizzo non lo seleziona,
            // e la feature lavora su cio' che e' selezionato.
            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            var feature = Extrude(model, height, merge: true);
            if (feature == null) return Failed("L'estrusione non e' stata creata." + WithoutSketch(model));
            return Succeeded(feature, new Dictionary<string, object>
            {
                ["diameter_mm"] = diameter,
                ["height_mm"] = height,
                ["dimensions_on_sketch"] = placedDimensions,
            }, References.PlaneNormal(plane));
        }

        // ─────────────────────────────────────────────────────────────────
        // DUE DIFETTI DIVERSI NON POSSONO DARE LA STESSA FRASE
        //
        // Misurato l'11 settembre 2026 (RAG 80u, casi 4 e 7): un foro da 500
        // mm su una faccia 314x274, e un foro chiesto a x = 5000 su un pezzo
        // che arriva a 157, finivano tutti e due con
        //
        //     «Il taglio non e' stato creato. Nel pezzo e' rimasto lo schizzo»
        //
        // che non e' una diagnosi: e' la constatazione che SolidWorks ha detto
        // di no. Chi legge non sa se deve rimpicciolire il foro, spostarlo, o
        // cambiare faccia — e le due cose da fare sono opposte.
        //
        // Le due cause pero' si distinguono PRIMA di disegnare, con numeri gia'
        // in mano: l'ingombro del pezzo e il profilo chiesto. Il modello da
        // imitare e' il raccordo, che nella stessa tornata non ha detto «non e'
        // riuscito» ma «200 mm e' troppo per lo spigolo «E1», lungo 314 mm: non
        // ci sta neanche meta'» — cioe' il CRITERIO, non il verdetto: chi legge
        // sa scegliere R150 senza doverlo indovinare.
        //
        // PERCHE' I CONTROLLI SONO QUESTI DUE E NON DUE PIU' STRETTI.
        // Nella stessa tornata il guscio da 30 su una scatola alta 52 sembrava
        // l'impossibile piu' evidente ed era solo brutto: un controllo scritto
        // sull'apparenza avrebbe rifiutato un pezzo legittimo, cioe' avrebbe
        // trasformato una disattenzione in una regola del prodotto. Quindi qui
        // si rifiuta SOLO cio' che non puo' produrre nessun pezzo:
        //
        //   1. il profilo cade tutto fuori dall'ingombro — sotto non c'e'
        //      materiale, non si toglie niente;
        //   2. il profilo COPRE tutto l'ingombro e il taglio va da parte a
        //      parte — non resta niente.
        //
        // Un foro che sborda dalla faccia, un foro centrato sul bordo, una
        // tasca che mangia meta' pezzo passano di qui e li giudica SolidWorks
        // come prima: sono insoliti, non impossibili.
        //
        // E il controllo sta PRIMA dello schizzo apposta. Le operazioni che si
        // controllano da sole devono disegnare per poter misurare, e quando
        // bocciano lo schizzo c'e' gia': tocca a `WithoutSketch` andarlo a
        // togliere, e se non ci riesce a dirlo (`LeftSketchBehind`). Questi due
        // no: non c'e' niente da misurare che non si sappia gia', quindi il
        // pezzo resta come era e non c'e' niente da togliere — il rifiuto piu'
        // economico e' quello che non ha disegnato niente.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Il nome dell'asse del modello, per poterlo dire a chi legge.</summary>
        private static string AxisName(int index)
        {
            return index == 0 ? "X" : index == 1 ? "Y" : "Z";
        }

        /// <summary>
        /// L'ingombro di TUTTO il pezzo in mm, nelle coordinate del modello:
        /// {minX, minY, minZ, maxX, maxY, maxZ}. Null se non c'e' nessun corpo.
        ///
        /// GetBodyBox, non i sei GetExtremePoint che usa lo stato. La scatola
        /// approssimata puo' essere PIU' GRANDE del pezzo vero, mai piu'
        /// piccola — e qui serve esattamente cosi': ogni suo errore rende i due
        /// controlli piu' PERMISSIVI. Un profilo che passa di qui e non doveva
        /// lo boccia SolidWorks un istante dopo; un profilo bocciato per
        /// sbaglio invece non lo rivede nessuno.
        /// </summary>
        private static double[] PartBoxMm(IModelDoc2 model)
        {
            var partDoc = model as IPartDoc;
            var bodies = partDoc?.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null || bodies.Length == 0) return null;
            // Approximate ON PURPOSE, see the summary: a box that can only
            // be larger than the part makes both checks more permissive.
            var outside = ApiHelpers.UnionOfBoxes(bodies, c => ApiHelpers.ApproximateBoundingBox(c as IBody2));
            if (outside == null) return null;
            for (int i = 0; i < 6; i++) outside[i] = Units.Mm(outside[i]);
            return outside;
        }

        /// <summary>
        /// Il taglio esce dall'altra parte del pezzo?
        ///
        /// Serve al secondo controllo e solo a quello: un taglio che NON passa
        /// puo' coprire tutta la faccia senza nessun problema — accorcia il
        /// pezzo, e accorciare un pezzo e' una cosa che si fa. Un cieco esce
        /// comunque quando la profondita' supera lo spessore misurato lungo la
        /// normale della faccia.
        /// </summary>
        private static bool ThroughAll(
            IModelDoc2 model, double[] axes, bool throughAll, double depthMm)
        {
            if (throughAll) return true;
            if (depthMm <= 0 || axes == null) return false;
            double thickness = ExtentAlongAxis(model, (int)axes[2]);
            return thickness > 0 && depthMm >= thickness;
        }

        /// <summary>
        /// I due controlli, per tutte e tre le operazioni che tagliano una
        /// sagoma su una faccia. Ritorna null quando il taglio si puo' provare,
        /// altrimenti il motivo, scritto in modo che chi legge sappia quale
        /// numero cambiare.
        /// </summary>
        /// <param name="whatItIs">
        /// La sagoma con le sue misure, gia' scritta: «Il foro da 500 mm».
        /// </param>
        /// <param name="semiU">Mezzo ingombro del profilo sul primo asse.</param>
        /// <param name="semiV">Mezzo ingombro del profilo sul secondo asse.</param>
        /// <param name="insideTheProfile">Questo punto della faccia sta dentro la sagoma?</param>
        /// <param name="advice">
        /// Date le due distanze dal centro al bordo piu' vicino del pezzo, la
        /// frase che dice cosa ci starebbe. La scrive ogni operazione, perche'
        /// «e' da 274 mm» e «e' 314 x 274 mm» non sono la stessa frase.
        /// </param>
        private static string CutWithoutHope(
            IModelDoc2 model,
            double[] axes,
            string whatItIs,
            double xMm,
            double yMm,
            double semiU,
            double semiV,
            Func<double, double, bool> insideTheProfile,
            bool throughAll,
            Func<double, double, string> advice)
        {
            if (axes == null) return null;
            var box = PartBoxMm(model);
            if (box == null) return null;   // niente corpo: non c'e' niente da confrontare

            int iu = (int)axes[0], iv = (int)axes[1];
            double uMin = box[iu], uMax = box[iu + 3];
            double vMin = box[iv], vMax = box[iv + 3];
            string nameU = AxisName(iu), nameV = AxisName(iv);

            // ── 1. IL PROFILO CADE TUTTO FUORI DAL PEZZO ──────────────────
            // Si confrontano le due SCATOLE: se non si toccano nemmeno quelle,
            // le due forme non si toccano di sicuro. Il contrario non vale — due
            // scatole che si toccano non dicono niente sulle forme — e va bene
            // cosi': il dubbio lo scioglie SolidWorks, non questo controllo.
            if (xMm + semiU < uMin || xMm - semiU > uMax ||
                yMm + semiV < vMin || yMm - semiV > vMax)
            {
                return string.Format(
                    "{0} e' centrato a {1} = {2:0.##}, {3} = {4:0.##} mm, ma il pezzo su {1} " +
                    "va da {5:0.##} a {6:0.##} e su {3} da {7:0.##} a {8:0.##}: il profilo cade " +
                    "tutto fuori dal materiale, quindi non c'e' niente da togliere.",
                    whatItIs, nameU, xMm, nameV, yMm, uMin, uMax, vMin, vMax);
            }

            // ── 2. IL PROFILO COPRE TUTTO IL PEZZO, E IL TAGLIO PASSA ─────
            // Bastano i quattro angoli: cerchio, rettangolo e stadio sono tutti
            // e tre CONVESSI, e una forma convessa che contiene i quattro
            // angoli contiene tutta la scatola.
            if (throughAll &&
                insideTheProfile(uMin, vMin) && insideTheProfile(uMin, vMax) &&
                insideTheProfile(uMax, vMin) && insideTheProfile(uMax, vMax))
            {
                double du = Math.Min(xMm - uMin, uMax - xMm);
                double dv = Math.Min(yMm - vMin, vMax - yMm);
                // Il consiglio ha senso solo se il centro sta DENTRO il pezzo:
                // da un centro fuori, «il piu' grande che ci sta» sarebbe un
                // numero negativo spacciato per una misura.
                string straight = du > 0 && dv > 0 ? " " + advice(du, dv) : "";
                return string.Format(
                    "{0} centrato a {1} = {2:0.##}, {3} = {4:0.##} mm copre tutto il pezzo, che " +
                    "qui occupa {5:0.##} x {6:0.##} mm: un taglio da parte a parte cosi' non " +
                    "lascerebbe niente.{7}",
                    whatItIs, nameU, xMm, nameV, yMm, uMax - uMin, vMax - vMin, straight);
            }

            return null;
        }

        /// <summary>
        /// Il punto (u, v) sta dentro lo stadio dell'asola? E' la distanza dal
        /// SEGMENTO fra i due centri delle estremita': minore del raggio vuol
        /// dire dentro, ed e' vero sia nella parte dritta sia nei due
        /// semicerchi. Gli stessi corsa/raggio/cos/sin che disegnano la sagoma.
        /// </summary>
        private static bool InsideStadium(
            double u, double v,
            double centerU, double centerV,
            double cos, double sin, double travel, double radius)
        {
            double du = u - centerU, dv = v - centerV;
            double t = du * cos + dv * sin;
            if (t > travel) t = travel;
            else if (t < -travel) t = -travel;
            double pu = centerU + t * cos, pv = centerV + t * sin;
            return (u - pu) * (u - pu) + (v - pv) * (v - pv) <= radius * radius;
        }

        // ─────────────────────────────────────────────────────────────────
        // QUANDO NON E' NE' LA MISURA NE' LA POSIZIONE: SI MISURA LO STESSO,
        // MA PER SPIEGARE
        //
        // Misurato il 14 settembre 2026, subito dopo la 0.43.0. Blocco
        // 314x274x52, foro da 270 passante, poi un foro da 20 centrato DENTRO
        // quel buco. I due controlli della 0.42.0 lo lasciano passare — e
        // fanno bene, il profilo sta dentro l'ingombro e non lo copre —
        // SolidWorks non crea la feature, e la 0.43.0 si porta via lo schizzo
        // come deve. Quello che resta in mano a chi legge e':
        //
        //     «Il taglio non e' stato creato.»
        //
        // Quattro parole. E' di nuovo la malattia di 80u — una constatazione
        // invece di una diagnosi — anzi peggio di prima, perche' prima almeno
        // diceva dello schizzo rimasto.
        //
        // PERCHE' QUI NON SI PUO' AGGIUNGERE UN RIFIUTO. La tentazione e'
        // ovvia: «se sotto il centro non c'e' materiale, rifiuta». Ma allargare
        // un foro da 20 a 30 ha il centro ESATTAMENTE nel vuoto ed e'
        // un'operazione che si fa tutti i giorni. Sarebbe la trappola del
        // guscio di 80u presa per la terza volta.
        //
        // Quindi si misura per SPIEGARE. La differenza non e' una sfumatura:
        // un rifiuto sbagliato blocca un lavoro legittimo e nessuno lo rivede,
        // una spiegazione sbagliata la corregge chi legge in tre secondi
        // perche' ha il pezzo davanti. Il costo dell'errore non e' lo stesso,
        // e neanche il diritto di sbagliare.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Questo punto, dato nelle coordinate della faccia (le stesse x/y che
        /// arrivano dal piano), cade DAVVERO sulla faccia?
        ///
        /// FIRMA VERIFICATA per riflessione sull'interop 29.5.1.1:
        ///   IFace2.GetClosestPointOn(Double X, Double Y, Double Z) -> Object
        ///
        /// E LA DIFFERENZA CHE CONTA E' CON `ISurface.GetClosestPointOn`, che
        /// esiste con la stessa identica firma un livello piu' sotto. La
        /// SUPERFICIE non sa niente dei contorni: di un piano bucato conosce il
        /// piano, non il buco. La FACCIA i contorni li ha. Chiesto a una faccia
        /// forata quale sia il suo punto piu' vicino a un punto che sta nel
        /// foro, risponde un punto sul BORDO del foro — cioe' diverso da quello
        /// che le abbiamo dato; se il punto sta sul pieno, risponde il punto
        /// stesso. E' un «dentro o fuori» chiesto a SolidWorks invece che
        /// dedotto da noi, ed e' la stessa postura di `IGetChildCount` in
        /// <see cref="WithoutSketch"/>.
        ///
        /// Non serve nessuno schizzo aperto: qui si resta nel sistema del
        /// MODELLO e non si passa da `PointInSketch`.
        /// </summary>
        private static bool PointOnFace(
            IFace2 face, double[] axes, double offsetM, double xMm, double yMm)
        {
            if (face == null || axes == null) return false;
            try
            {
                var p = new double[3];
                p[(int)axes[0]] = Units.M(xMm);
                p[(int)axes[1]] = Units.M(yMm);
                p[(int)axes[2]] = offsetM;
                var near = face.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                if (near == null || near.Length < 3) return false;
                double dx = near[0] - p[0], dy = near[1] - p[1], dz = near[2] - p[2];
                // Un micron, al quadrato. Non e' una tolleranza di lavorazione:
                // e' il margine per l'aritmetica in virgola mobile, perche' su
                // un punto che sta sulla faccia la risposta e' il punto stesso,
                // non un'approssimazione.
                return dx * dx + dy * dy + dz * dz <= 1e-12;
            }
            catch { return false; }
        }

        /// <summary>
        /// Cosa c'e' sotto il profilo, detto contando. Ritorna la frase da
        /// aggiungere al motivo del fallimento, vuota se non si e' potuto
        /// misurare niente.
        ///
        /// NON RIFIUTA NIENTE E NON DEVE MAI FARLO: si chiama solo quando
        /// SolidWorks ha gia' detto di no, e serve a dire a chi legge quale
        /// delle strade e' ancora aperta.
        /// </summary>
        private static string BelowProfile(
            IFace2 face, string uidFace, double[] axes, double offsetM,
            List<double[]> samples)
        {
            if (face == null || axes == null || samples == null || samples.Count == 0)
                return "";

            int onCount = 0;
            foreach (var c in samples)
                if (PointOnFace(face, axes, offsetM, c[0], c[1])) onCount++;

            // «sulla faccia», non «su la faccia»: la preposizione si articola
            // qui dentro, perche' le tre frasi la usano tutte e tre.
            string location = string.IsNullOrEmpty(uidFace)
                ? "sulla faccia scelta"
                : "sulla faccia «" + uidFace + "»";
            int howMany = samples.Count;

            if (onCount == 0)
                return " Ho provato " + howMany + " punti sotto il profilo e nessuno cade " +
                       location + ": li' il pezzo e' gia' aperto, e dove non c'e' materiale non " +
                       "c'e' niente da togliere. Sposta il profilo sul pieno, oppure scegli " +
                       "la faccia su cui il materiale c'e'.";

            if (onCount == howMany)
                return " Sotto il profilo il materiale c'e': tutti e " + howMany + " i punti " +
                       "che ho provato cadono " + location + ". Quindi non e' la posizione — " +
                       "restano la profondita' e il verso del taglio.";

            return " Sotto il profilo il materiale c'e' solo in parte: " + onCount + " punti su " +
                   howMany + " cadono " + location + ", gli altri no. Il profilo sta a cavallo " +
                   "di un bordo o di un'apertura.";
        }

        /// <summary>
        /// I punti da provare dentro un cerchio: il centro e due anelli. Due e
        /// non uno perche' un anello solo, preso vicino al bordo, racconta il
        /// bordo e non il profilo; preso vicino al centro, racconta il centro.
        /// </summary>
        private static List<double[]> CircleSamples(double x, double y, double radius)
        {
            var outside = new List<double[]> { new[] { x, y } };
            foreach (double fraction in new[] { 0.55, 0.93 })
                for (int i = 0; i < 8; i++)
                {
                    double t = i * Math.PI / 4;
                    outside.Add(new[]
                    {
                        x + radius * fraction * Math.Cos(t),
                        y + radius * fraction * Math.Sin(t),
                    });
                }
            return outside;
        }

        /// <summary>Il centro, i quattro mezzi lati e i quattro angoli, tirati dentro.</summary>
        private static List<double[]> RectangleSamples(
            double x, double y, double length, double width)
        {
            double a = length / 2 * 0.93, b = width / 2 * 0.93;
            return new List<double[]>
            {
                new[] { x, y },
                new[] { x - a, y }, new[] { x + a, y },
                new[] { x, y - b }, new[] { x, y + b },
                new[] { x - a, y - b }, new[] { x + a, y - b },
                new[] { x - a, y + b }, new[] { x + a, y + b },
            };
        }

        /// <summary>
        /// I punti da provare dentro lo stadio: tre stazioni lungo l'asse
        /// lungo, ognuna con il suo punto in mezzo e i due di fianco, piu' i
        /// due colmi. Gli stessi corsa/raggio/cos/sin che lo disegnano.
        /// </summary>
        private static List<double[]> StadiumSamples(
            double x, double y, double cos, double sin, double travel, double radius)
        {
            var outside = new List<double[]>();
            Func<double, double, double[]> inPlane = (u, v) => new[]
            {
                x + u * cos - v * sin,
                y + u * sin + v * cos,
            };
            foreach (double u in new[] { -travel, 0.0, travel })
                foreach (double v in new[] { 0.0, -radius * 0.9, radius * 0.9 })
                    outside.Add(inPlane(u, v));
            outside.Add(inPlane(-(travel + radius) * 0.93, 0));
            outside.Add(inPlane(+(travel + radius) * 0.93, 0));
            return outside;
        }

        private Outcome CylindricalCut(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string uidFace = Str(a, "face_uid", "");
            var face = ResolveFace(docState, a, uidFace, out string reason);
            if (face == null) return Failed(reason);

            double[] axes = FaceAxes(face, out double offsetM, out string error);
            if (axes == null) return Failed(error);

            double diameter = Num(a, "diameter_mm");
            bool throughAll = Bool(a, "through_all", true);
            double depth = Num(a, "depth_mm", 0);
            double x, y;
            if (!RequiredNum(a, "x_mm", out x, out error)) return Failed(error);
            if (!RequiredNum(a, "y_mm", out y, out error)) return Failed(error);

            // SI MISURA PRIMA, perche' dopo non si puo' piu'.
            // Serve al controllo sul volume in fondo: vedi `NoteOnVolume`.
            double? volumeBefore = VolumeMm3(model);

            double holeRadius = diameter / 2;

            // L'ANCORA (voce E18): cosa tiene il foro dov'e' quando il pezzo
            // cambia. Si controlla tutta PRIMA di disegnare: un'ancora sbagliata
            // non deve lasciare niente nel pezzo. Vuota = il vecchio modo.
            var anchor = ReadAnchor(a, docState, face, axes, offsetM, x, y, holeRadius, holeRadius, out reason);
            if (anchor == null) return Failed(reason);

            string withoutHope = CutWithoutHope(
                model, axes,
                "Il foro da " + diameter.ToString("0.##") + " mm",
                x, y, holeRadius, holeRadius,
                (u, v) => (u - x) * (u - x) + (v - y) * (v - y) <= holeRadius * holeRadius,
                ThroughAll(model, axes, throughAll, depth),
                (du, dv) => "Il piu' grande che ci sta, centrato li', e' da " +
                            (2 * Math.Min(du, dv)).ToString("0.##") + " mm.");
            if (withoutHope != null) return Failed(withoutHope);

            // The names before the sketch (scheme S7): see ExtrudeRectangle.
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectFace(face)) return Failed("Non sono riuscito a selezionare la faccia.");
            var sketches = model.SketchManager;
            sketches.InsertSketch(true);
            var center = PointInSketch(model, axes, offsetM, x, y, out error);
            if (center == null)
            {
                // Lo schizzo aperto va chiuso comunque: lasciarlo aperto
                // bloccherebbe il documento in modalita' schizzo davanti a una
                // persona che non ha chiesto niente di tutto questo.
                try { sketches.InsertSketch(false); } catch { }
                return Failed(error);
            }
            // CON L'ANCORA, `AddToDB` a vero attorno alla creazione: le
            // relazioni che SOLIDWORKS indovina (centro sull'origine, su un
            // punto medio) si sommerebbero all'ancora e sovravincolerebbero lo
            // schizzo. Senza ancora il foro nasce come e' sempre nato.
            ISketchSegment cutCircle;
            bool addBefore = sketches.AddToDB;
            try
            {
                if (!anchor.Empty) sketches.AddToDB = true;
                cutCircle = sketches.CreateCircleByRadius(
                    center[0], center[1], center[2], Units.M(diameter / 2));
            }
            finally { sketches.AddToDB = addBefore; }

            // IL DIAMETRO, SULLO SCHIZZO DEL FORO.
            //
            // Senza, il foro non e' modificabile con `set_dimension` e la sua
            // tavola resta muta: `model_dimensions` non ha niente da
            // riportare. E' la lamentela piu' frequente dell'officina —
            // un pezzo che si puo' fare ma non si puo' correggere.
            //
            // SI QUOTA LA DIMENSIONE, NON LA POSIZIONE. La posizione vorrebbe
            // un riferimento da cui misurare, e sceglierlo per conto di chi
            // disegna vuol dire vincolare lo schizzo piu' di quanto abbia
            // chiesto: il `set_dimension` dopo fallirebbe con un messaggio di
            // sovra-vincolo che non parla di niente.
            string textError;
            var dimensionText = PointInSketch(
                model, axes, offsetM,
                x + holeRadius * 1.4 + 5, y + holeRadius * 1.4 + 5, out textError);
            int placedDimensions = cutCircle != null && dimensionText != null
                ? DimensionSegments(model,
                    new List<Dimension> { Dimension.Su(cutCircle, dimensionText) })
                : 0;

            // L'ANCORA SI ATTACCA AL CENTRO DEL CERCHIO, dopo il diametro: lo
            // schizzo deve uscirne completamente definito.
            var anchorMeasurements = new Dictionary<string, object>();
            if (!anchor.Empty)
            {
                ISketchPoint centerPoint = null;
                try { centerPoint = ((ISketchArc)cutCircle).GetCenterPoint2() as ISketchPoint; } catch { }
                if (!ApplyAnchor(model, anchor, centerPoint, axes, offsetM, x, y, anchorMeasurements, out string anchorError))
                {
                    try { sketches.InsertSketch(false); } catch { }
                    return Failed(anchorError + WithoutSketch(model));
                }
            }

            sketches.InsertSketch(false);

            // Lo schizzo va SELEZIONATO: uscire dallo schizzo non lo seleziona,
            // e la feature lavora su cio' che e' selezionato.
            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            var feature = Cut(model, throughAll, depth);
            if (feature == null)
            {
                // Si misura PRIMA di togliere lo schizzo: la faccia resta
                // valida comunque, ma l'ordine giusto e' leggere il pezzo
                // com'era quando SolidWorks ha detto di no.
                string below = BelowProfile(
                    face, uidFace, axes, offsetM,
                    CircleSamples(x, y, holeRadius));
                return Failed("Il taglio non e' stato creato." + below + WithoutSketch(model));
            }

            // UN FORO PASSANTE SU UNA SCATOLA VUOTA NE FA DUE.
            //
            // Misurato il 10 settembre 2026 disegnando il contenitore per
            // l'antenna: chiesto il passaggio del cavo su un fianco, con
            // «through_all». Tolti 3.922,2 mm3 contro i 1.961,1 di una parete
            // sola — esattamente il doppio, perche' il taglio e' uscito
            // anche dal retro. L'operazione ha detto «riuscito», il pezzo
            // aveva un buco in piu', e nessun controllo se n'e' accorto.
            //
            // Chi disegna una scatola e scrive «through_all» intende quasi
            // sempre «attraversa QUESTA parete». Il numero di pareti
            // forate non si indovina, ma si CONTA: ogni parete attraversata
            // lascia una faccia cilindrica del diametro chiesto.
            int walls = DrilledWalls(feature, diameter);
            if (throughAll && walls > 1)
                return Failed(
                    "Il foro passante ha attraversato " + walls + " pareti, " +
                    "non una: il pezzo e' cavo e il taglio e' uscito anche " +
                    "dall'altra parte. Se volevi bucare solo questa parete, " +
                    "chiedilo cieco con la profondita' dello spessore." +
                    RemainsInPart);

            // LA PROVA DI MOVIMENTO: un'ancora che non regge una modifica non
            // serve, e il taglio che la porta non resta nel pezzo.
            string doesNotHold = MovementTest(model, anchor, feature);
            if (doesNotHold != null) return Failed(doesNotHold + RemoveCut(model, feature, volumeBefore));

            var measurements = new Dictionary<string, object>
            {
                ["diameter_mm"] = diameter,
                ["through_all"] = throughAll,
                ["walls_drilled"] = walls >= 0 ? (object)walls : null,
                ["dimensions_on_sketch"] = placedDimensions,
            };
            foreach (var entry in anchorMeasurements) measurements[entry.Key] = entry.Value;

            // QUANTO SE N'E' ANDATO DAVVERO. Vedi `NoteOnVolume`.
            NoteOnVolume(
                measurements, volumeBefore, VolumeMm3(model),
                throughAll ? 0 : Math.PI * holeRadius * holeRadius * depth,
                "Il foro da " + diameter.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " mm");

            return Succeeded(feature, measurements, FaceNormal(face));
        }

        /// <summary>
        /// Quanto una lavorazione puo' discostarsi dal volume atteso prima che
        /// valga la pena dirlo. Un centesimo.
        ///
        /// Misurato il 14/9/2026 sui due casi che contano: un foro tutto sulla
        /// faccia toglie il volume esatto (192.000 - 191.607,3 = 392,7 contro
        /// i 392,699 di pi*5^2*5, cioe' meno di un millesimo di scostamento), e
        /// uno centrato sul bordo ne toglie **la meta'**. Fra i due c'e' un
        /// fattore cinquecento: qualunque soglia in mezzo funziona, e l'uno per
        /// cento lascia passare l'arrotondamento senza lasciar passare niente
        /// che abbia una forma.
        /// </summary>
        private const double VolumeOffset = 0.01;

        /// <summary>
        /// SI DICE QUANTO SE N'E' ANDATO, E QUANTO SE NE DOVEVA ANDARE.
        ///
        /// IL DIFETTO CHE QUESTO CHIUDE (voce E13). Un `cylindrical_cut` con
        /// il profilo a cavallo del bordo della faccia toglie meta' materiale e
        /// risponde `succeeded: true`, `walls_drilled: 1`. Misurato: foro da 10
        /// profondo 5 centrato sul bordo inferiore di una faccia, **196,3 mm³
        /// invece di 392,7** — esattamente meta' — con il fondo del foro ridotto
        /// a un mezzo disco da 39,27 mm² invece di 78,54.
        ///
        /// IL GUARDIANO C'ERA E GUARDAVA DALL'ALTRA PARTE. `BelowProfile` sa
        /// contare quanti punti del profilo cadono sulla faccia e lo dice bene,
        /// ma il suo commento dichiara: «NON RIFIUTA NIENTE E NON DEVE MAI
        /// FARLO: si chiama solo quando SOLIDWORKS ha gia' detto di no». Quando
        /// SOLIDWORKS dice di SI', non lo chiama nessuno.
        ///
        /// NON SI RIFIUTA, SI DICE. L'operazione e' riuscita e il pezzo esiste:
        /// rifiutare adesso vorrebbe dire buttare una lavorazione fatta per un
        /// sospetto. Si scrive quanto se n'e' andato, quanto ci si aspettava, e
        /// di quanto si discosta — poi decide chi legge. E' la stessa forma di
        /// `positions_not_checked` e dell'ancora che si sposta.
        ///
        /// SOLO SUI CIECHI L'ATTESO E' UNA MOLTIPLICAZIONE. Per un passante il
        /// volume dipende da quanto materiale c'e' sotto, che non si sa senza
        /// misurarlo: li' si scrive `removed_mm3` e basta, senza un atteso che
        /// sarebbe inventato. Un allarme sbagliato su un passante legittimo
        /// sarebbe peggio del difetto che si sta chiudendo.
        ///
        /// <param name="expectedMm3">Zero quando non si puo' sapere.</param>
        /// </summary>
        private static void NoteOnVolume(
            Dictionary<string, object> measurements,
            double? volumeBefore, double? volumeAfter, double expectedMm3, string what)
        {
            if (measurements == null) return;

            // Un volume che non si e' potuto leggere non diventa uno zero: uno
            // zero vorrebbe dire «non ha tolto niente», che e' un'affermazione.
            //
            // `<= 0` SU TUTTI E DUE, e la prima scrittura aveva `volumeAfter < 0`.
            // `VolumeMm3` non torna MAI un negativo: risponde 0 quando il
            // documento non e' una parte, quando `GetBodies2` da' null e dal
            // `catch` che avvolge tutto — cioe' su ogni strada di errore.
            // Quindi quel ramo non poteva scattare, e uno zero di lettura
            // fallita passava come se fosse una misura: `tolti` diventava
            // l'INTERO volume del pezzo, e su un foro perfettamente riuscito
            // usciva una `note_volume` che diceva «192.000 mm³ invece di 392,7».
            // L'allarme falso sul corretto, cioe' esattamente cio' che questa
            // voce dichiara peggiore del difetto che sta chiudendo.
            //
            // Il commento qui sopra diceva gia' la cosa giusta, ed e' stata la
            // riga a non farla: e' la forma di difetto piu' frequente trovata
            // in questo progetto, commessa mentre la si documentava.
            //
            // `Shell` la scrive giusta quindici righe piu' in la'
            // (`volumeBefore > 0 && volumeAfter > 0`): la stessa cosa, fatta bene,
            // nello stesso file.
            //
            // DAL 25/9/2026 (voce Z-C8) LO DICE IL TIPO: `VolumeMm3` torna
            // `double?`, null quando non si legge, e questo controllo non deve
            // piu' indovinare quale zero sia una misura.
            if (volumeBefore == null || volumeAfter == null || volumeBefore <= 0) return;
            double removed = volumeBefore.Value - volumeAfter.Value;

            measurements["removed_mm3"] = Math.Round(removed, 2);
            if (expectedMm3 <= 0) return;

            measurements["expected_mm3"] = Math.Round(expectedMm3, 2);
            double offset = Math.Abs(removed - expectedMm3) / expectedMm3;
            if (offset <= VolumeOffset) return;

            measurements["note_volume"] = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0} ha tolto {1:0.##} mm³ invece dei {2:0.##} che ci si aspettava " +
                "({3:0.#}% {4}). L'operazione e' riuscita, ma il pezzo non e' quello " +
                "che il piano descriveva: di solito vuol dire che il profilo cade in " +
                "parte fuori dalla faccia, o su materiale gia' tolto. Guarda il pezzo " +
                "prima di andare avanti.",
                what, removed, expectedMm3, offset * 100,
                removed < expectedMm3 ? "in meno" : "in piu'");
        }

        private Outcome RectangularCut(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string uidFace = Str(a, "face_uid", "");
            var face = ResolveFace(docState, a, uidFace, out string reason);
            if (face == null) return Failed(reason);

            double[] axes = FaceAxes(face, out double offsetM, out string error);
            if (axes == null) return Failed(error);

            double length = Num(a, "length_mm");
            double width = Num(a, "width_mm");
            bool throughAll = Bool(a, "through_all", false);
            double depth = Num(a, "depth_mm", 0);
            if (!throughAll && depth <= 0)
                return Failed("Tasca non passante senza profondita'.");

            // SI MISURA PRIMA, perche' dopo non si puo' piu'.
            double? volumeBefore = VolumeMm3(model);
            double x, y;
            if (!RequiredNum(a, "x_mm", out x, out error)) return Failed(error);
            if (!RequiredNum(a, "y_mm", out y, out error)) return Failed(error);

            // L'ANCORA (voce E18), controllata tutta prima di disegnare.
            var anchor = ReadAnchor(a, docState, face, axes, offsetM, x, y, length / 2, width / 2, out reason);
            if (anchor == null) return Failed(reason);

            string withoutHope = CutWithoutHope(
                model, axes,
                "La tasca " + length.ToString("0.##") + " x " + width.ToString("0.##") + " mm",
                x, y, length / 2, width / 2,
                (u, v) => Math.Abs(u - x) <= length / 2 && Math.Abs(v - y) <= width / 2,
                ThroughAll(model, axes, throughAll, depth),
                (du, dv) => "La piu' grande che ci sta, centrata li', e' " +
                            (2 * du).ToString("0.##") + " x " + (2 * dv).ToString("0.##") + " mm.");
            if (withoutHope != null) return Failed(withoutHope);

            // The names before the sketch (scheme S7): see ExtrudeRectangle.
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectFace(face)) return Failed("Non sono riuscito a selezionare la faccia.");
            var sketches = model.SketchManager;
            sketches.InsertSketch(true);
            var p1 = PointInSketch(model, axes, offsetM, x - length / 2, y - width / 2, out error);
            var p2 = p1 == null
                ? null
                : PointInSketch(model, axes, offsetM, x + length / 2, y + width / 2, out error);
            if (p1 == null || p2 == null)
            {
                try { sketches.InsertSketch(false); } catch { }
                return Failed(error);
            }
            object[] cutSides;
            bool addBefore = sketches.AddToDB;
            try
            {
                if (!anchor.Empty) sketches.AddToDB = true;
                cutSides = sketches.CreateCornerRectangle(
                    p1[0], p1[1], p1[2], p2[0], p2[1], p2[2]) as object[];
            }
            finally { sketches.AddToDB = addBefore; }

            // LE DUE QUOTE DELLA TASCA, per lo stesso motivo del foro tondo.
            //
            // Si prendono i primi due lati e basta: sono adiacenti, quindi
            // perpendicolari, quindi uno da' la lunghezza e l'altro la
            // larghezza. QUALE dei due sia l'uno e quale l'altro non si
            // stabilisce a priori — su una faccia qualunque «horizontal» non
            // vuol dire niente — e non serve saperlo: le due quote nascono
            // comunque giuste, e il criterio della voce si verifica MISURANDO
            // i valori, non deducendoli dall'ordine dei lati.
            int placedDimensions = 0;
            if (cutSides != null && cutSides.Length >= 2)
            {
                string textError;
                var below = PointInSketch(
                    model, axes, offsetM,
                    x, y - width / 2 - 5, out textError);
                var beside = PointInSketch(
                    model, axes, offsetM,
                    x + length / 2 + 5, y, out textError);
                var list = new List<Dimension>();
                if (below != null) list.Add(Dimension.Su(cutSides[0], below));
                if (beside != null) list.Add(Dimension.Su(cutSides[1], beside));
                placedDimensions = DimensionSegments(model, list);
            }

            // L'ANCORA SI ATTACCA AL CENTRO DELLA TASCA, che si costruisce: il
            // rettangolo per angoli non ha un punto al centro.
            var anchorMeasurements = new Dictionary<string, object>();
            if (!anchor.Empty)
            {
                var centerPoint = RectangleCenter(model, cutSides, p1, p2);
                string anchorError = centerPoint == null
                    ? "Non sono riuscito a costruire il centro della tasca a cui attaccare l'ancora."
                    : null;
                if (anchorError != null ||
                    !ApplyAnchor(model, anchor, centerPoint, axes, offsetM, x, y, anchorMeasurements, out anchorError))
                {
                    try { sketches.InsertSketch(false); } catch { }
                    return Failed(anchorError + WithoutSketch(model));
                }
            }

            sketches.InsertSketch(false);

            // Lo schizzo va SELEZIONATO: uscire dallo schizzo non lo seleziona,
            // e la feature lavora su cio' che e' selezionato.
            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            var feature = Cut(model, throughAll, depth);
            if (feature == null)
            {
                string below = BelowProfile(
                    face, uidFace, axes, offsetM,
                    RectangleSamples(x, y, length, width));
                return Failed("Il taglio non e' stato creato." + below + WithoutSketch(model));
            }

            string doesNotHold = MovementTest(model, anchor, feature);
            if (doesNotHold != null) return Failed(doesNotHold + RemoveCut(model, feature, volumeBefore));

            var pocketMeasurements = new Dictionary<string, object>
            {
                ["length_mm"] = length,
                ["width_mm"] = width,
                ["through_all"] = throughAll,
                ["dimensions_on_sketch"] = placedDimensions,
            };
            foreach (var entry in anchorMeasurements) pocketMeasurements[entry.Key] = entry.Value;

            // LO STESSO DIFETTO DI `cylindrical_cut`, e la stessa riparazione
            // (voce E13). Una tasca il cui profilo cade in parte fuori dalla
            // faccia toglie meno materiale di quanto il piano descriveva, e
            // risponde «succeeded» lo stesso.
            NoteOnVolume(
                pocketMeasurements, volumeBefore, VolumeMm3(model),
                throughAll ? 0 : length * width * depth,
                "La tasca " + length.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                "x" + width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

            return Succeeded(feature, pocketMeasurements, FaceNormal(face));
        }

        /// <summary>
        /// L'ASOLA: un foro allungato, cioe' il fissaggio che si registra.
        ///
        /// PERCHE' SERVE UNA OPERAZIONE SUA. In carpenteria l'asola non e' un
        /// vezzo: e' quello che permette di montare. Una staffa con due asole
        /// si allinea in opera, una staffa con due fori tondi o entra o si
        /// rifa'. Con il catalogo di prima si poteva chiedere solo un foro
        /// tondo (`cylindrical_cut`) o una tasca rettangolare
        /// (`rectangular_cut`): la seconda ci somiglia, ma ha gli spigoli
        /// vivi e la vite ci balla dentro invece di scorrere.
        ///
        /// LA FORMA. Uno "stadio": due segmenti dritti lunghi L−W e due
        /// semicerchi di raggio W/2 alle estremita'. L e' la lunghezza TOTALE
        /// da fuori a fuori — come la si misura sul pezzo e come la si scrive
        /// a disegno — e W e' la larghezza, cioe' il diametro delle
        /// estremita'.
        ///
        /// FIRME VERIFICATE per riflessione sull'interop 29.5.1.1:
        ///   ISketchManager.CreateLine(Double X1, Double Y1, Double Z1,
        ///       Double X2, Double Y2, Double Z2) -> SketchSegment
        ///   ISketchManager.CreateArc(Double XC, Double YC, Double Zc,
        ///       Double X1, Double Y1, Double Z1, Double X2, Double Y2,
        ///       Double Z2, Int16 Direction) -> SketchSegment
        ///
        /// IL VERSO DELL'ARCO E' IL DUBBIO, E NON SI SPERA: SI MISURA.
        /// `Direction` dice da che parte girare dal primo punto al secondo, e
        /// vive nel piano dello SCHIZZO — mentre i sei punti qui sotto sono
        /// costruiti nel sistema del MODELLO e ci arrivano attraverso
        /// `PointInSketch`. Quella trasformazione non conserva la mano: il
        /// suo stesso commento dice che «per due facce opposte l'asse Y
        /// risulta invertito». Quindi lo stesso `-1` significa una cosa su una
        /// faccia e l'opposto su quella dirimpetto.
        ///
        /// COSA SUCCEDE SE E' SBAGLIATO — e qui il commento diceva il falso
        /// fino al 10 settembre 2026, quando una review lo ha rifatto con i
        /// numeri in mano. Non esce un arco da 270 gradi e non si autointerseca
        /// niente: i due estremi di ogni arco sono diametralmente opposti
        /// rispetto al centro, quindi TUTTI E DUE gli archi candidati sono da
        /// 180 gradi. Il verso decide solo da che parte passa il semicerchio.
        /// Con il verso giusto sporge in fuori e l'asola e' lunga L; con quello
        /// sbagliato rientra, e l'asola esce lunga L−W con le estremita' MORSE
        /// a mezzaluna. Per una M10 51x11: 40 mm invece di 51, area 344,97
        /// invece di 535,03. Il contorno resta chiuso e semplice, il taglio
        /// riesce, la feature e' verde, e la vite scorre 29 mm invece di 40.
        ///
        /// PERCIO' SI CONTA IL MATERIALE TOLTO. Il caso cieco lo prende per
        /// intero, perche' li' il confronto e' un'uguaglianza. Il caso passante
        /// e' piu' scomodo — quanto materiale ci sia sotto non si sa in
        /// generale — e li' si fanno due cose: un tetto contro le forme molto
        /// piu' grandi, e un controllo che cerca proprio QUESTO errore, che e'
        /// piu' piccolo e sotto il tetto ci passerebbe. E' la stessa lezione di
        /// `HoleWizard5`, che faceva un foro da due pollici senza dare nessun
        /// errore: l'unica difesa contro un pezzo che sembra giusto e' una
        /// misura, ma la misura va scelta in modo che possa fallire.
        /// </summary>
        private Outcome Slot(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string uidFace = Str(a, "face_uid", "");
            var face = ResolveFace(docState, a, uidFace, out string reason);
            if (face == null) return Failed(reason);

            double[] axes = FaceAxes(face, out double offsetM, out string error);
            if (axes == null) return Failed(error);

            double length = Num(a, "length_mm");
            double width = Num(a, "width_mm");
            double angle = Num(a, "angle_deg", 0);
            bool throughAll = Bool(a, "through_all", true);
            double depth = Num(a, "depth_mm", 0);
            double x, y;
            if (!RequiredNum(a, "x_mm", out x, out error)) return Failed(error);
            if (!RequiredNum(a, "y_mm", out y, out error)) return Failed(error);

            // LA LUNGHEZZA E' DA FUORI A FUORI. Un'asola lunga quanto e' larga
            // e' un foro tondo, e chiederla cosi' quasi sempre vuol dire che
            // uno dei due numeri e' sbagliato.
            if (length <= width)
                return Failed(
                    "L'asola e' lunga " + length.ToString("0.##") + " e larga " +
                    width.ToString("0.##") + " mm: la lunghezza si misura da " +
                    "fuori a fuori e deve essere maggiore della larghezza. Se " +
                    "volevi un foro tondo da " + width.ToString("0.##") +
                    " mm usa il foro, non l'asola.");
            if (!throughAll && depth <= 0)
                return Failed("Asola non passante senza profondita'.");

            // Lo stadio, nel sistema della faccia e prima di ruotarlo: mezza
            // corsa fra i due centri, e il raggio delle estremita'.
            double travel = (length - width) / 2.0;
            double radius = width / 2.0;
            double rad = angle * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);

            Func<double, double, double[]> inPlane = (u, v) => new[]
            {
                x + u * cos - v * sin,
                y + u * sin + v * cos,
            };

            // I COLMI: il punto piu' esterno di ogni semicerchio, sull'asse
            // lungo dell'asola. Sono cio' che determina l'arco senza doverne
            // dichiarare il verso.
            var crestA = inPlane(-travel - radius, 0);
            var crestB = inPlane(+travel + radius, 0);
            var topA = inPlane(-travel, +radius);
            var topB = inPlane(+travel, +radius);
            var bottomA = inPlane(-travel, -radius);
            var bottomB = inPlane(+travel, -radius);

            // L'ANCORA (voce E18), controllata tutta prima di disegnare. Il
            // mezzo ingombro per asse e' quello qui sotto: la corsa proiettata
            // piu' il raggio.
            var anchor = ReadAnchor(a, docState, face, axes, offsetM, x, y,
                Math.Abs(travel * cos) + radius, Math.Abs(travel * sin) + radius, out reason);
            if (anchor == null) return Failed(reason);

            // Mezzo ingombro dello stadio sui due assi della faccia: la corsa
            // proiettata piu' il raggio delle estremita'. Con angolo 0 fa
            // lunghezza/2 e larghezza/2, come deve.
            string withoutHope = CutWithoutHope(
                model, axes,
                "L'asola " + length.ToString("0.##") + " x " + width.ToString("0.##") + " mm",
                x, y,
                Math.Abs(travel * cos) + radius, Math.Abs(travel * sin) + radius,
                (u, v) => InsideStadium(u, v, x, y, cos, sin, travel, radius),
                ThroughAll(model, axes, throughAll, depth),
                (du, dv) => "Centrata li', ci sta dentro una sagoma fino a " +
                            (2 * du).ToString("0.##") + " x " + (2 * dv).ToString("0.##") + " mm.");
            if (withoutHope != null) return Failed(withoutHope);

            // The names before the sketch (scheme S7): see ExtrudeRectangle.
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectFace(face)) return Failed("Non sono riuscito a selezionare la faccia.");

            double? volumeBefore = VolumeMm3(model);
            if (volumeBefore == null) return Failed(VolumeNotReadBefore("tagliare l'asola"));
            var sketches = model.SketchManager;
            sketches.InsertSketch(true);

            double[] pTopA = null, pTopB = null, pBottomA = null, pBottomB = null;
            double[] pCrestA = null, pCrestB = null;
            bool drawn = false;
            int segments = 0;
            int stadiumDimensions = 0;
            var anchorMeasurements = new Dictionary<string, object>();
            try
            {
                pTopA = PointInSketch(model, axes, offsetM, topA[0], topA[1], out error);
                pTopB = pTopA == null ? null
                    : PointInSketch(model, axes, offsetM, topB[0], topB[1], out error);
                pBottomA = pTopB == null ? null
                    : PointInSketch(model, axes, offsetM, bottomA[0], bottomA[1], out error);
                pBottomB = pBottomA == null ? null
                    : PointInSketch(model, axes, offsetM, bottomB[0], bottomB[1], out error);
                pCrestA = pBottomB == null ? null
                    : PointInSketch(model, axes, offsetM, crestA[0], crestA[1], out error);
                pCrestB = pCrestA == null ? null
                    : PointInSketch(model, axes, offsetM, crestB[0], crestB[1], out error);

                if (pCrestB != null)
                {
                    // ADD-TO-DB, COME PER «extrude_profile», E QUI NON E' PER
                    // ANDARE PIU' VELOCI. Senza, SolidWorks inferisce una
                    // relazione a ogni segmento e SPOSTA i punti per farla
                    // tornare. I quattro pezzi dello stadio si toccano per
                    // costruzione — l'estremo della linea e' lo STESSO vettore
                    // che fa da capo all'arco — ma se ognuno viene agganciato
                    // per conto suo, il contorno si apre di frazioni di
                    // millimetro. Un contorno aperto non si taglia, e infatti
                    // il 10 settembre 2026 dodici prove su dodici finivano con
                    // «l'asola non e' stata tagliata».
                    ISketchSegment[] created;
                    drawn = DrawStadium(
                        sketches, pTopA, pTopB, pBottomA, pBottomB, pCrestA, pCrestB,
                        out segments, out error, out created);

                    // LO STADIO SI DEFINISCE (voce E18): tangenze, raggi uguali,
                    // la mezzeria fra i centri e le sue quote. Senza ancora e'
                    // un guadagno lo stesso — l'asola si corregge con
                    // `set_dimension` — e un errore qui non ferma il taglio;
                    // con l'ancora lo schizzo deve uscirne completamente
                    // definito, o non si taglia.
                    if (drawn)
                    {
                        string stadiumError;
                        stadiumDimensions = DefineStadium(
                            model, created, axes, offsetM, x, y, angle, travel, radius,
                            anchor, anchorMeasurements, out stadiumError);
                        if (!anchor.Empty && stadiumError != null)
                        {
                            drawn = false;
                            error = stadiumError;
                        }
                    }
                }
            }
            finally
            {
                // Il `catch` vuoto non e' pigrizia: se uscire dallo schizzo
                // fallisse, l'eccezione sostituirebbe il motivo VERO del
                // fallimento — e chi legge si troverebbe l'errore sbagliato.
                try { sketches.InsertSketch(false); } catch { }
            }
            if (!drawn) return Failed(error + WithoutSketch(model));

            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            var feature = Cut(model, throughAll, depth);
            if (feature == null)
            {
                // Con il contorno completo, «il problema e' altrove» era un
                // modo gentile di dire che non lo sapevamo. Adesso si guarda.
                string below = segments < 4
                    ? ""
                    : BelowProfile(
                        face, uidFace, axes, offsetM,
                        StadiumSamples(x, y, cos, sin, travel, radius));
                return Failed(
                    "L'asola non e' stata tagliata. Lo schizzo ha " + segments +
                    " segmenti sui quattro che servono (due linee e due archi): " +
                    (segments < 4
                        ? "il contorno non e' completo, e un contorno aperto non si taglia."
                        : "il contorno e' completo, quindi il problema non e' il disegno.") +
                    below + WithoutSketch(model));
            }

            // LA PROVA DI MOVIMENTO dell'ancora, come per foro e tasca.
            string doesNotHold = MovementTest(model, anchor, feature);
            if (doesNotHold != null) return Failed(doesNotHold + RemoveCut(model, feature, volumeBefore));

            // ── E ADESSO SI CONTA ────────────────────────────────────────────
            // L'area dello stadio: il rettangolo centrale piu' i due
            // semicerchi, che insieme fanno un cerchio.
            double area = (length - width) * width + Math.PI * radius * radius;
            double? volumeAfter = VolumeMm3(model);
            if (volumeAfter == null)
                return Succeeded(feature, new Dictionary<string, object>
                {
                    ["length_mm"] = length,
                    ["width_mm"] = width,
                    ["angle_deg"] = angle,
                    ["through_all"] = throughAll,
                    ["area_mm2"] = Math.Round(area, 2),
                    ["material_removed_mm3"] = null,
                    ["dimensions_on_sketch"] = stadiumDimensions,
                    ["volume_not_reread"] = NoteVolumeNotReread,
                }, FaceNormal(face));
            double removed = volumeBefore.Value - volumeAfter.Value;
            if (removed <= 0)
                return Failed(
                    "L'asola non ha tolto niente: il taglio e' riuscito ma il " +
                    "materiale e' rimasto tutto. Di solito vuol dire che il " +
                    "profilo era fuori dal pezzo." + RemainsInPart);

            if (!throughAll)
            {
                double expected = area * depth;
                if (Math.Abs(removed - expected) > Math.Max(1.0, expected * 0.03))
                    return Failed(
                        "L'asola e' stata tagliata ma ha tolto " + removed.ToString("0.#") +
                        " mm3 invece di " + expected.ToString("0.#") + ": la forma " +
                        "che e' uscita non e' quella chiesta. Non ti confermo un " +
                        "pezzo che non torna." + RemainsInPart);
            }
            else
            {
                // ── Passante: due controlli, e servono a cose diverse ────────
                //
                // Quanto materiale ci sia sotto non si sa in generale: un
                // taglio passante attraversa quello che trova. Per questo qui
                // non c'e' un'uguaglianza come nel caso cieco.
                double maximumThickness = ExtentAlongAxis(model, (int)axes[2]);

                // 1. IL TETTO. Il pezzo non puo' aver perso piu' materiale di
                //    quanto ne stia nell'ingombro attraversato. Prende le forme
                //    molto piu' grandi del dovuto.
                double limit = area * maximumThickness * 1.02;
                if (maximumThickness > 0 && removed > limit)
                    return Failed(
                        "L'asola ha tolto " + removed.ToString("0.#") + " mm3, piu' " +
                        "di quanto ne possa togliere un'asola di quelle misure (" +
                        limit.ToString("0.#") + " al massimo): la forma che e' " +
                        "uscita non e' quella chiesta." + RemainsInPart);

                // 2. IL CONTROLLO CHE CERCA L'ERRORE CHE CONOSCIAMO.
                //
                //    UN TETTO DA SOLO NON BASTA, E IL MOTIVO E' PRECISO: se il
                //    verso degli archi e' sbagliato i semicerchi RIENTRANO
                //    invece di sporgere, e l'asola toglie MENO materiale del
                //    dovuto — quindi passa sotto il tetto e non viene mai
                //    segnalata. Il tetto e' cieco proprio sull'unico errore per
                //    cui l'operazione non e' ancora dichiarata.
                //
                //    L'area sbagliata pero' si sa in anticipo: e' il rettangolo
                //    MENO il cerchio invece che piu'. Se il materiale tolto
                //    somiglia a quella, non e' una coincidenza.
                double wrongArea = (length - width) * width
                                       - Math.PI * radius * radius;
                if (maximumThickness > 0 && wrongArea > 0)
                {
                    double suspicious = wrongArea * maximumThickness;
                    if (Math.Abs(removed - suspicious) <= Math.Max(1.0, suspicious * 0.03))
                        return Failed(
                            "L'asola ha tolto " + removed.ToString("0.#") + " mm3, che e' " +
                            "esattamente quanto ne toglie un'asola con i semicerchi " +
                            "girati in DENTRO invece che in fuori: sarebbe lunga " +
                            (length - width).ToString("0.##") + " mm invece di " +
                            length.ToString("0.##") + ", con le estremita' morse a " +
                            "mezzaluna. Non ti confermo un pezzo cosi'." + RemainsInPart);
                }
            }

            var slotMeasurements = new Dictionary<string, object>
            {
                ["length_mm"] = length,
                ["width_mm"] = width,
                ["angle_deg"] = angle,
                ["through_all"] = throughAll,
                ["area_mm2"] = Math.Round(area, 2),
                ["material_removed_mm3"] = Math.Round(removed, 2),
                ["dimensions_on_sketch"] = stadiumDimensions,
            };
            foreach (var entry in anchorMeasurements) slotMeasurements[entry.Key] = entry.Value;
            return Succeeded(feature, slotMeasurements, FaceNormal(face));
        }

        /// <summary>
        /// Quanto e' spesso il pezzo lungo un asse del modello, in mm. Serve
        /// come limite superiore per un taglio passante: non e' una misura
        /// fine, e' un tetto.
        /// </summary>
        private static double ExtentAlongAxis(IModelDoc2 model, int axis)
        {
            var partDoc = model as IPartDoc;
            var bodies = partDoc?.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null || bodies.Length == 0) return 0;
            // Approximate ON PURPOSE: this is a ceiling for a through cut,
            // and the approximate box encloses the body (IBody2~GetBodyBox.html),
            // so it is never lower than the true one.
            var u = ApiHelpers.UnionOfBoxes(bodies, c => ApiHelpers.ApproximateBoundingBox(c as IBody2));
            if (u == null || u[axis + 3] <= u[axis]) return 0;
            return Units.Mm(u[axis + 3] - u[axis]);
        }

        /// <summary>
        /// SPOSTA (o COPIA) DEI CORPI. Il muro contro cui il banco ha sbattuto.
        ///
        /// PERCHE' ESISTE. Il 9 settembre 2026, alla richiesta «fammi un
        /// castello: due telai 1000x800 distanti 600 mm, uniti da quattro
        /// montanti», il modello ha scritto da solo la diagnosi giusta:
        ///
        ///   «non posso sovrapporre lo stesso telaio due volte. Devo spostare
        ///    il secondo telaio di 600 mm lungo l'asse Z»
        ///
        /// e poi ha proposto lo stesso `rectangular_frame` due volte, che il
        /// validatore ha rifiutato. Non era il modello a sbagliare: non c'era
        /// nessuna operazione capace di spostare cio' che aveva gia' fatto.
        /// Con `copia = true` il castello si fa in un passo — il secondo telaio
        /// e' una COPIA del primo, spostata di 600.
        ///
        /// FIRME VERIFICATE per riflessione sull'interop 29.5.1.1:
        ///   IFeatureManager.InsertMoveCopyBody2(
        ///       Double TransX, Double TransY, Double TransZ, Double TransDist,
        ///       Double RotPointX, Double RotPointY, Double RotPointZ,
        ///       Double RotAngleX, Double RotAngleY, Double RotAngleZ,
        ///       Boolean BCopy, Int32 NumCopies) -> Feature
        ///   ISelectionMgr.CreateSelectData() -> SelectData   (con .Mark)
        ///   IMoveCopyBodyFeatureData ha TransformX/Y/Z E TransformValue,
        ///       cioe' DIREZIONE e MODULO tenuti separati.
        ///
        /// IL DUBBIO, ED E' GROSSO. Siccome direzione e modulo sono due campi
        /// distinti, non e' detto quale dei due comandi: se SolidWorks
        /// prendesse TransX/Y/Z come spostamento assoluto ignorando TransDist,
        /// oppure moltiplicasse i due, il pezzo si sposterebbe della distanza
        /// SBAGLIATA — e nessuno darebbe errore. Qui si passa una direzione
        /// UNITARIA e la distanza in TransDist, che e' la lettura piu'
        /// probabile; e poi si misura.
        ///
        /// COME SI MISURA UNO SPOSTAMENTO. Con il baricentro. Muovere un
        /// sottoinsieme di corpi sposta il baricentro di TUTTO il pezzo di una
        /// frazione nota — quella del volume mosso sul volume totale:
        ///
        ///     centro_dopo = centro_prima + (volume_mosso / volume_totale) * d
        ///
        /// E' un'uguaglianza esatta, non una stima, e cade subito se la
        /// distanza e' sbagliata anche di poco. `IBody2.GetMassProperties`
        /// da' baricentro e volume insieme: i primi tre numeri sono il centro
        /// di massa in metri, il quarto e' il volume.
        /// </summary>
        private Outcome MoveBodies(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            int howMany;
            string reason;
            var bodies = BodiesFromReference(a, "bodies_ref", docState, out howMany, out reason);
            if (bodies == null) return Failed(reason);

            double dx = Num(a, "dx_mm", 0);
            double dy = Num(a, "dy_mm", 0);
            double dz = Num(a, "dz_mm", 0);
            bool copy = Bool(a, "copy", false);
            int copies = (int)Num(a, "copy_count", 1);
            if (!copy) copies = 0;
            else if (copies < 1) copies = 1;

            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance < 1e-6)
                return Failed(
                    "Spostamento nullo: dx, dy e dz sono tutti a zero. Se " +
                    "volevi lasciare i corpi dove sono, questa operazione non " +
                    "serve; se volevi copiarli sul posto, due corpi sovrapposti " +
                    "sono un pezzo sbagliato, non una copia.");

            // Lo stato di partenza: tutto il pezzo, e i soli corpi da muovere.
            double totalVolume, movedVolume;
            double[] totalCenter, movedCenter;
            if (!MassOf(AllSolidBodies(model), out totalVolume, out totalCenter))
                return Failed("Non riesco a misurare il pezzo prima di spostarlo.");
            if (!MassOf(bodies, out movedVolume, out movedCenter))
                return Failed("Non riesco a misurare i corpi da spostare.");
            if (totalVolume <= 0 || movedVolume <= 0)
                return Failed("I corpi indicati non hanno volume: non c'e' niente da spostare.");
            int bodiesBefore = CountSolidBodies(model);
            if (bodiesBefore < 0) return Failed("Non riesco a contare i corpi del pezzo prima di spostarli.");

            // I corpi da muovere vanno selezionati con MARCA 1: e' la marca che
            // la feature legge. Con la marca a zero la selezione c'e' ma la
            // feature non la vede, e non lo dice.
            model.ClearSelection2(true);
            var handler = model.SelectionManager as ISelectionMgr;
            var mark = handler?.CreateSelectData();
            if (mark == null) return Failed("SolidWorks non mi da' come marcare la selezione.");
            mark.Mark = SelectionMarks.MoveCopyBodyBody;
            // UN CORPO NON E' UNA IEntity, E LA DIFFERENZA NON SI VEDE FINCHE'
            // NON SI PROVA. Misurato il 10 settembre 2026 dentro SolidWorks:
            // `corpo as IEntity` torna null nell'interop, il contatore restava a
            // zero e tutte e cinque le prove di spostamento dicevano «i corpi
            // indicati non si sono lasciati selezionare». Le facce e gli spigoli
            // sono IEntity; i corpi hanno il loro `IBody2.Select2`.
            int selected = 0;
            foreach (var c in bodies)
            {
                var body = c as IBody2;
                if (body != null && body.Select2(true, mark)) { selected++; continue; }
                var ent = c as IEntity;
                if (ent != null && ent.Select4(true, mark)) selected++;
            }
            if (selected == 0)
                return Failed("I corpi indicati non si sono lasciati selezionare.");

            IFeature feature;
            try
            {
                // MISURATO DENTRO SOLIDWORKS IL 10 SETTEMBRE 2026, E IL DUBBIO
                // E' SCIOLTO: comandano TransX/Y/Z, che sono lo spostamento
                // ASSOLUTO in metri; TransDist viene IGNORATO.
                //
                // Prima qui passavo un versore unitario piu' la distanza in
                // TransDist — la lettura che sembrava piu' probabile. Il
                // risultato: il versore (1,0,0 o simili) veniva preso per
                // metri, e QUALUNQUE spostamento chiesto diventava 1000 mm.
                // Chiedendo 50 mm il pezzo ne faceva mille, senza nessun
                // errore. L'ha detto il baricentro: scarto di 475 mm su un
                // atteso di 25. Era esattamente la misura scritta per poter
                // fallire, e ha fallito nel punto giusto.
                //
                // TransDist resta valorizzato con la distanza vera: se una
                // versione futura decidesse di leggerlo, leggerebbe il numero
                // giusto invece di uno zero.
                feature = model.FeatureManager.InsertMoveCopyBody2(
                    TransX: Units.M(dx),
                    TransY: Units.M(dy),
                    TransZ: Units.M(dz),
                    TransDist: Units.M(distance),
                    RotPointX: 0, RotPointY: 0, RotPointZ: 0,
                    RotAngleX: 0, RotAngleY: 0, RotAngleZ: 0,
                    BCopy: copy,
                    NumCopies: copies);
            }
            catch (Exception ex)
            {
                return Failed("Lo spostamento non e' riuscito: " + ex.Message);
            }
            if (feature == null)
                return Failed("Lo spostamento non e' stato creato.");

            // ── E ADESSO SI MISURA ───────────────────────────────────────────
            int bodiesAfter = CountSolidBodies(model);
            int expectedBodies = bodiesBefore + copies * selected;
            if (bodiesAfter >= 0 && bodiesAfter != expectedBodies)
                return Failed(
                    "Dopo lo spostamento il pezzo ha " + bodiesAfter + " corpi invece di " +
                    expectedBodies + ". Non ti confermo un pezzo diverso da quello " +
                    "che ti ho mostrato: ritiralo — " + Sentences.HowToWithdrawIt + ".");

            double volumeAfter;
            double[] centerAfter;
            if (!MassOf(AllSolidBodies(model), out volumeAfter, out centerAfter))
                return Failed("Non riesco a rimisurare il pezzo dopo lo spostamento.");

            double expectedVolume = totalVolume + copies * movedVolume;
            if (Math.Abs(volumeAfter - expectedVolume) > Math.Max(1.0, expectedVolume * 0.005))
                return Failed(
                    "Dopo lo spostamento il pezzo ha un volume di " +
                    volumeAfter.ToString("0.#") + " mm3 invece di " +
                    expectedVolume.ToString("0.#") + ": qualcosa si e' unito o " +
                    "tagliato. Ritiralo — " + Sentences.HowToWithdrawIt + ".");

            // Il baricentro atteso. Senza copia si sposta la frazione di volume
            // mossa; con le copie, la copia k-esima sta a k*d, e la somma dei k
            // da 1 a n fa n(n+1)/2.
            var d = new[] { dx, dy, dz };
            var expected = new double[3];
            if (copies == 0)
            {
                for (int i = 0; i < 3; i++)
                    expected[i] = totalCenter[i] + (movedVolume / totalVolume) * d[i];
            }
            else
            {
                double sumK = copies * (copies + 1) / 2.0;
                for (int i = 0; i < 3; i++)
                    expected[i] =
                        (totalVolume * totalCenter[i]
                         + movedVolume * (copies * movedCenter[i] + sumK * d[i]))
                        / expectedVolume;
            }

            double deviation = 0;
            for (int i = 0; i < 3; i++) deviation = Math.Max(deviation, Math.Abs(centerAfter[i] - expected[i]));
            double tolerance = Math.Max(0.05, distance * 0.002);
            if (deviation > tolerance)
                return Failed(
                    "I corpi non sono finiti dove dovevano: il baricentro del " +
                    "pezzo e' fuori di " + deviation.ToString("0.###") + " mm da dove " +
                    "lo mette uno spostamento di " + distance.ToString("0.##") +
                    " mm. Quasi sempre vuol dire che la distanza applicata non e' " +
                    "quella chiesta. Ritiralo — " + Sentences.HowToWithdrawIt + ".");

            return Succeeded(feature, new Dictionary<string, object>
            {
                ["bodies_moved"] = selected,
                ["dx_mm"] = dx,
                ["dy_mm"] = dy,
                ["dz_mm"] = dz,
                ["distance_mm"] = Math.Round(distance, 3),
                ["copy"] = copy,
                ["copy_count"] = copies,
                ["bodies_after"] = bodiesAfter,
                ["centroid_deviation_mm"] = Math.Round(deviation, 4),
            }, null);
        }

        /// <summary>
        /// I quattro segmenti dello stadio, con l'inferenza automatica spenta.
        ///
        /// PERCHE' SPEGNERLA. I quattro pezzi si toccano per costruzione: il
        /// capo di ogni linea e' lo STESSO vettore che fa da capo all'arco, non
        /// un numero ricalcolato. Ma se SolidWorks aggancia ogni segmento per
        /// conto suo — che e' quello che fa quando `AddToDB` e' falso — li
        /// sposta per far tornare le relazioni che inferisce, e il contorno si
        /// apre di frazioni di millimetro. Un contorno aperto non si taglia.
        ///
        /// Torna quanti segmenti sono nati davvero: e' il numero che il
        /// messaggio di errore usa per dire SE il contorno era completo, invece
        /// di lasciare indovinare.
        /// </summary>
        private static bool DrawStadium(
            ISketchManager sketches,
            double[] topA, double[] topB, double[] bottomA, double[] bottomB,
            double[] crestA, double[] crestB,
            out int segments, out string error, out ISketchSegment[] created)
        {
            segments = 0;
            error = null;
            created = new ISketchSegment[4];
            bool addToDbBefore = false, displayBefore = false;
            try { addToDbBefore = sketches.AddToDB; displayBefore = sketches.DisplayWhenAdded; } catch { }
            try
            {
                try { sketches.AddToDB = true; sketches.DisplayWhenAdded = false; } catch { }

                created[0] = sketches.CreateLine(topA[0], topA[1], topA[2],
                                               topB[0], topB[1], topB[2]);
                if (created[0] != null) segments++;
                created[1] = sketches.CreateLine(bottomB[0], bottomB[1], bottomB[2],
                                               bottomA[0], bottomA[1], bottomA[2]);
                if (created[1] != null) segments++;

                // GLI ARCHI SI DANNO PER TRE PUNTI, NON PER VERSO.
                //
                // Prima erano `CreateArc(centro, capo1, capo2, Direction)` con
                // `Direction = -1` scritto a mano. Un verso pero' vive nel
                // piano dello SCHIZZO, mentre i punti sono costruiti nel
                // sistema del MODELLO, e la trasformazione fra i due non
                // conserva la mano. Misurato il 10 settembre 2026 su una
                // piastra: funzionava su «front», «down» e «right» e si
                // ribaltava su «back», «up» e «left» — una faccia per
                // ogni coppia, cioe' meta' del parco facce.
                //
                // Ribaltato, il semicerchio RIENTRA invece di sporgere:
                // l'asola esce lunga L−W con le estremita' morse. Sulla prova
                // cieca il conto era 1724,8 mm3 invece di 2675,2, cioe'
                // esattamente (L−W)·W − pi·r^2 al posto di + pi·r^2.
                //
                // `Create3PointArc(capo1, capo2, puntoSull'arco)` toglie di
                // mezzo la domanda: il terzo punto e' il colmo del semicerchio,
                // passa per `PointInSketch` come tutti gli altri, e
                // l'arco resta determinato dalla GEOMETRIA invece che da una
                // convenzione. Non c'e' piu' niente da indovinare.
                created[2] = sketches.Create3PointArc(topB[0], topB[1], topB[2],
                                                    bottomB[0], bottomB[1], bottomB[2],
                                                    crestB[0], crestB[1], crestB[2]);
                if (created[2] != null) segments++;
                created[3] = sketches.Create3PointArc(bottomA[0], bottomA[1], bottomA[2],
                                                    topA[0], topA[1], topA[2],
                                                    crestA[0], crestA[1], crestA[2]);
                if (created[3] != null) segments++;
            }
            catch (Exception ex)
            {
                error = "Lo schizzo dell'asola non e' riuscito: " + ex.Message;
                return false;
            }
            finally
            {
                try { sketches.AddToDB = addToDbBefore; sketches.DisplayWhenAdded = displayBefore; } catch { }
            }

            if (segments < 4)
            {
                error =
                    "Lo schizzo dell'asola ha solo " + segments + " segmenti sui " +
                    "quattro che servono: SolidWorks non ha costruito " +
                    (segments < 2 ? "le linee" : "gli archi") + ".";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Quante pareti ha attraversato un taglio cilindrico: si contano le
        /// facce cilindriche del diametro chiesto che la feature ha lasciato.
        ///
        /// Una parete forata lascia una faccia cilindrica. Due pareti, due
        /// facce. E' un conteggio, non una stima, e non dipende da quanto e'
        /// spesso il pezzo — che in generale non si sa.
        ///
        /// La ricetta per riconoscere una faccia cilindrica di un certo
        /// diametro e' quella gia' in produzione in `OperazioniFori`:
        /// `IFace2.IGetSurface().IsCylinder()` e `CylinderParams[6] * 2000`.
        ///
        /// **-1 quando non si e' potuto leggere** (voce Z-C8): `walls_drilled: 0`
        /// su un foro riuscito e' un'affermazione falsa. ZERO-E-ZERO: una
        /// feature viva senza facce (`GetFaces` null) non ha forato niente.
        /// </summary>
        private static int DrilledWalls(IFeature feature, double diameterMm)
        {
            try
            {
                var faces = feature.GetFaces() as object[];
                if (faces == null) return 0;
                int howMany = 0;
                foreach (var o in faces)
                {
                    var surface = (o as IFace2)?.IGetSurface();
                    if (surface == null || !surface.IsCylinder()) continue;
                    var par = surface.CylinderParams as double[];
                    if (par == null || par.Length < 7) continue;
                    if (Math.Abs(par[6] * 2000.0 - diameterMm) > 0.05) continue;
                    howMany++;
                }
                return howMany;
            }
            catch { return -1; }
        }

        /// <summary>Tutti i corpi solidi del pezzo, come li vuole MassOf.</summary>
        private static object[] AllSolidBodies(IModelDoc2 model)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return new object[0];
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            return bodies ?? new object[0];
        }

        /// <summary>
        /// Volume in mm3 e baricentro in mm di un gruppo di corpi.
        ///
        /// `IBody2.GetMassProperties(densita)` torna un vettore in cui i primi
        /// tre numeri sono il centro di massa in METRI e il quarto e' il volume
        /// in metri cubi. Il baricentro di piu' corpi e' la media dei loro
        /// baricentri PESATA SUL VOLUME: farne la media semplice darebbe un
        /// numero plausibile e sbagliato, e nessuno se ne accorgerebbe.
        /// </summary>
        private static bool MassOf(object[] bodies, out double volumeMm3, out double[] centerMm)
        {
            volumeMm3 = 0;
            centerMm = new double[3];
            if (bodies == null || bodies.Length == 0) return false;
            var readings = new List<double[]>();
            try
            {
                foreach (var c in bodies)
                    readings.Add((c as IBody2)?.GetMassProperties(1000.0) as double[]);
            }
            catch { return false; }
            return MassOfBodies(readings, out volumeMm3, out centerMm);
        }

        /// <summary>
        /// The volume (mm3) and the volume-weighted centre (mm) of the bodies
        /// whose IBody2.GetMassProperties readings are given, one per body; false
        /// when ANY of them cannot be used (Z-C21.8, finding M43).
        ///
        /// A BODY THAT CANNOT BE MEASURED IS NOT SKIPPED. Until library 0.144.0
        /// a missing, short or non-positive reading was skipped with a
        /// `continue`, and the sum of the others was given as the whole: a
        /// partial volume and a centre of part of the bodies, from which
        /// move_bodies then drew its diagnosis of the move. VolumeMm3 already
        /// had the rule (a body that gives no mass properties makes the whole
        /// null); this is the same rule. Kept apart from the document so that
        /// Oce.Solid.Agent.ContractTests can call it without SOLIDWORKS.
        /// </summary>
        internal static bool MassOfBodies(IList<double[]> readings, out double volumeMm3, out double[] centreMm)
        {
            volumeMm3 = 0;
            centreMm = new double[3];
            if (readings == null || readings.Count == 0) return false;
            var weighted = new double[3];
            double total = 0;
            foreach (var mp in readings)
            {
                if (mp == null || mp.Length < 4) return false;
                double v = mp[3] * 1e9;
                if (!(v > 0)) return false;
                total += v;
                for (int i = 0; i < 3; i++) weighted[i] += Units.Mm(mp[i]) * v;
            }
            volumeMm3 = total;
            for (int i = 0; i < 3; i++) centreMm[i] = weighted[i] / total;
            return true;
        }

        /// <summary>
        /// ESTRUDE UN PROFILO SU UNA FACCIA CHE C'E' GIA'.
        ///
        /// PERCHE' MANCAVA, E PERCHE' E' IL BUCO PIU' GROSSO DEL CATALOGO.
        /// Misurato il 10 settembre 2026, elencando le precondizioni di tutte
        /// e ventisette le operazioni: su un pezzo normale — non una
        /// carpenteria — si poteva soltanto TOGLIERE. Taglio cilindrico,
        /// taglio rettangolare, asola, foro guidato: tolgono. Guscio: svuota.
        /// Specchia e sposta: muovono. Nessuna aggiungeva niente.
        ///
        /// Cioe': fai un corpo, e da li' in poi puoi solo scavarlo.
        ///
        /// E' la spiegazione migliore che abbiamo trovato della lamentela
        /// dell'officina — «una volta che disegna una cosa non riesce
        /// successivamente a modificarla». Senza questa operazione non si
        /// possono fare: un labbro di tenuta, un distanziale su cui appoggiare
        /// qualcosa, un'aletta di fissaggio, una torretta per una vite, un
        /// rialzo. Cioe' meta' di quello che c'e' dentro un contenitore.
        ///
        /// LA STRADA E' GIA' PROVATA, ED E' PER QUESTO CHE COSTA POCO.
        /// Disegnare uno schizzo su una faccia scelta e' esattamente cio' che
        /// fanno `rectangular_cut` e `slot`, misurate. Disegnare un
        /// contorno qualunque e' cio' che fa `extrude_profile`, misurato.
        /// Qui si mettono insieme i due pezzi e si chiama `FeatureExtrusion3`
        /// invece di `FeatureCut4`.
        ///
        /// IL DUBBIO: `Merge`. Con `merge` vero il materiale nuovo diventa
        /// parte del corpo che c'era; con falso nasce un CORPO SEPARATO, che
        /// e' un pezzo diverso e non lo dice nessuno — l'albero cresce
        /// uguale, la forma sembra la stessa, e la differenza si vede solo
        /// contando i corpi. Percio' qui si contano.
        /// </summary>
        private Outcome ExtrudeOnFace(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var contours = Contours(a, out string reason, out string _);
            if (contours == null) return Failed(reason);

            var face = ResolveFace(docState, a, Str(a, "face_uid", ""), out reason);
            if (face == null) return Failed(reason);

            double[] axes = FaceAxes(face, out double offsetM, out string error);
            if (axes == null) return Failed(error);

            double height = Num(a, "height_mm");
            if (height <= 0)
                return Failed("Un rialzo di altezza nulla o negativa non e' un rialzo.");
            bool merge = Bool(a, "merge", true);

            // L'ANCORA (voce E18). Qui non ci sono x_mm/y_mm: il punto e' il
            // CENTRO del contorno, e l'ancora vale per un contorno solo, tondo
            // o rettangolare allineato alla faccia.
            var anchor = new Anchoring();
            double centerX = 0, centerY = 0;
            object rawAnchor;
            if (a.TryGetValue("anchor", out rawAnchor) && rawAnchor != null)
            {
                double halfU, halfV;
                if (!ContourCenterOnly(contours, out centerX, out centerY, out halfU, out halfV, out reason))
                    return Failed(reason);
                anchor = ReadAnchor(a, docState, face, axes, offsetM, centerX, centerY, halfU, halfV, out reason);
                if (anchor == null) return Failed(reason);
            }
            var anchorMeasurements = new Dictionary<string, object>();

            int bodiesBefore = CountSolidBodies(model);
            double? volumeBefore = VolumeMm3(model);
            if (bodiesBefore < 0 || volumeBefore == null)
                return Failed(VolumeNotReadBefore("fare il rialzo"));

            // The names before the sketch (scheme S7): see ExtrudeRectangle.
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectFace(face)) return Failed("Non sono riuscito a selezionare la faccia.");

            var sketches = model.SketchManager;
            bool addToDbBefore = false, displayBefore = false;
            try { addToDbBefore = sketches.AddToDB; displayBefore = sketches.DisplayWhenAdded; } catch { }

            int segments = 0;
            bool drawn = false;
            try
            {
                sketches.InsertSketch(true);
                try { sketches.AddToDB = true; sketches.DisplayWhenAdded = false; } catch { }
                // I contorni arrivano in coordinate della FACCIA, non del
                // piano: e' la stessa convenzione di `rectangular_cut`, e
                // il modello non deve sapere dove sta la faccia nello spazio.
                var created = new List<ISketchSegment>();
                segments = DrawContoursOnFace(
                    model, sketches, contours, axes, offsetM, out error, created);
                drawn = segments > 0;
                if (drawn && !anchor.Empty &&
                    !AnchorContour(model, contours[0], created, axes, offsetM, centerX, centerY,
                                      anchor, anchorMeasurements, out error))
                    drawn = false;
            }
            catch (Exception ex) { error = ex.Message; }
            finally
            {
                try { sketches.AddToDB = addToDbBefore; sketches.DisplayWhenAdded = displayBefore; } catch { }
                try { sketches.InsertSketch(false); } catch { }
            }
            if (!drawn) return Failed((error ?? "schizzo non riuscito") + WithoutSketch(model));

            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            var feature = Extrude(model, height, merge: merge);
            if (feature == null)
                return Failed(
                    "Il rialzo non e' stato creato. Lo schizzo ha " + segments +
                    " segmenti: se il contorno non e' chiuso, l'estrusione non parte." +
                    WithoutSketch(model));

            // ── e adesso si conta ────────────────────────────────────────
            string doesNotHold = MovementTest(model, anchor, feature);
            if (doesNotHold != null) return Failed(doesNotHold + RemoveCut(model, feature, volumeBefore));

            int bodiesAfter = CountSolidBodies(model);
            double? volumeAfter = VolumeMm3(model);
            if (bodiesAfter < 0 || volumeAfter == null)
                return Succeeded(feature, new Dictionary<string, object>
                {
                    ["height_mm"] = height,
                    ["merge"] = merge,
                    ["contours"] = contours.Count,
                    ["segments"] = segments,
                    ["bodies"] = NumberOrNull(bodiesAfter),
                    ["material_added_mm3"] = null,
                    ["volume_not_reread"] = NoteVolumeNotReread,
                }, FaceNormal(face));
            double added = volumeAfter.Value - volumeBefore.Value;

            if (merge && bodiesAfter != bodiesBefore)
                return Failed(
                    "Il rialzo doveva unirsi al pezzo e invece i corpi sono " +
                    "passati da " + bodiesBefore + " a " + bodiesAfter + ": quasi " +
                    "sempre vuol dire che il profilo non tocca il corpo. " +
                    "Ritiralo — " + Sentences.HowToWithdrawIt + ".");

            if (added <= 0)
                return Failed(
                    "Il rialzo non ha aggiunto materiale: il volume e' " +
                    (added < 0 ? "calato" : "rimasto uguale") + ". Di solito " +
                    "vuol dire che l'estrusione e' andata DENTRO il pezzo " +
                    "invece che in fuori. Ritiralo — " + Sentences.HowToWithdrawIt + ".");

            var bossMeasurements = new Dictionary<string, object>
            {
                ["height_mm"] = height,
                ["merge"] = merge,
                ["contours"] = contours.Count,
                ["segments"] = segments,
                ["bodies"] = bodiesAfter,
                ["material_added_mm3"] = Math.Round(added, 2),
            };
            foreach (var entry in anchorMeasurements) bossMeasurements[entry.Key] = entry.Value;
            return Succeeded(feature, bossMeasurements, FaceNormal(face));
        }

        /// <summary>
        /// Disegna dei contorni sul piano di una FACCIA, non su un piano
        /// dell'albero: ogni punto passa per `PointInSketch`, che e' la
        /// stessa strada dei tagli su faccia gia' misurati.
        /// </summary>
        private int DrawContoursOnFace(
            IModelDoc2 model, ISketchManager sketches, List<Contour> contours,
            double[] axes, double offsetM, out string error, List<ISketchSegment> created = null)
        {
            error = null;
            int done = 0;
            foreach (var contour in contours)
            {
                if (contour.Circle)
                {
                    var c = PointInSketch(model, axes, offsetM,
                                              contour.Center[0], contour.Center[1], out error);
                    var border = PointInSketch(model, axes, offsetM,
                                                  contour.Center[0] + contour.Diameter / 2.0,
                                                  contour.Center[1], out error);
                    if (c == null || border == null) return 0;
                    // CreateCircle vuole un punto sul bordo, non un raggio:
                    // cosi' il cerchio resta tondo anche se il piano dello
                    // schizzo e' girato rispetto al modello.
                    var circle = sketches.CreateCircle(c[0], c[1], c[2], border[0], border[1], border[2]);
                    if (circle != null) { done++; if (created != null) created.Add(circle); }
                    continue;
                }
                foreach (var stroke in contour.Strokes)
                {
                    for (int i = 0; i < stroke.Points.Count - 1; i++)
                    {
                        var p1 = PointInSketch(model, axes, offsetM,
                                                   stroke.Points[i][0], stroke.Points[i][1], out error);
                        var p2 = PointInSketch(model, axes, offsetM,
                                                   stroke.Points[i + 1][0], stroke.Points[i + 1][1], out error);
                        if (p1 == null || p2 == null) return 0;
                        var line = sketches.CreateLine(p1[0], p1[1], p1[2], p2[0], p2[1], p2[2]);
                        if (line != null) { done++; if (created != null) created.Add(line); }
                    }
                }
            }
            return done;
        }

        /// <summary>
        /// CAMBIA UNA QUOTA DI UNA LAVORAZIONE CHE C'E' GIA'.
        ///
        /// E' la richiesta piu' frequente in officina — «quel foro portalo da
        /// M8 a M10», «lo spessore fallo 3» — e fino al 10 settembre 2026 era
        /// impossibile: tutte e ventotto le operazioni aggiungevano o
        /// toglievano, nessuna MODIFICAVA. L'unico modo era cancellare e
        /// rifare, cioe' perdere tutto quello che veniva dopo: fori,
        /// raccordi, nervature costruite su quella geometria.
        ///
        /// Firme verificate per riflessione sull'interop 29.5.1.1:
        ///   IFeature.GetFirstDisplayDimension() / GetNextDisplayDimension(Object)
        ///   IDisplayDimension.GetDimension2(Int32 Index) -> Dimension
        ///   IDimension.SystemValue (in METRI) e
        ///   IDimension.SetSystemValue3(Double, Int32 WhichConfigurations, Object Config_names) -> Int32
        ///   IModelDoc2.EditRebuild3() -> Boolean
        ///
        /// LE CONFIGURAZIONI SONO UNA TRAPPOLA APERTA.
        /// `SetSystemValue3` vuole sapere SU QUALI configurazioni scrivere.
        /// Scegliere «questa» su un pezzo che ne ha tre vuol dire cambiarne
        /// una e lasciare le altre com'erano — e il pezzo sembra a posto
        /// finche' qualcuno non apre l'altra configurazione. Qui si scrive su
        /// TUTTE, che e' l'unica scelta che non lascia il pezzo per meta'.
        ///
        /// E SI RICOSTRUISCE, PERCHE' SENZA NON CAMBIA NIENTE.
        /// Cambiare il valore non muove la geometria: la muove la
        /// ricostruzione. Senza `EditRebuild3` il numero e' nuovo e il pezzo
        /// e' vecchio — che e' il modo perfetto di far confermare a qualcuno
        /// una modifica che non c'e'.
        /// </summary>
        private Outcome SetDimension(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string dimensionName = Str(a, "dimension", "");
            if (string.IsNullOrWhiteSpace(dimensionName))
                return Failed(
                    "Non hai detto quale quota cambiare. I nomi delle quote " +
                    "stanno nello stato del documento, dentro ogni lavorazione " +
                    "(per esempio «D1@Schizzo1»).");

            double val;
            string error;
            if (!RequiredNum(a, "value_mm", out val, out error)) return Failed(error);

            var dimension = FindDimension(model, dimensionName, out string location);
            if (dimension == null)
                return Failed(
                    "Non trovo la quota «" + dimensionName + "» in questo documento." +
                    (location == null ? "" : " Ci sono: " + location + "."));

            // ── LA QUOTA DI SOLA LETTURA NON SI SCAVALCA (voce Z-C21.2, M8) ──
            //
            // IDimension~SetSystemValue3.html, Remarks: «This method allows you
            // to change the value of a read-only dimension. You can use
            // IDimension::ReadOnly to determine if a dimension is read-only.»
            // Chi ha messo la quota in sola lettura l'ha fatto apposta, e
            // SetSystemValue3 la cambiava lo stesso senza dirlo. Lo stesso per
            // una quota GUIDATA (IDimension.DrivenState, swDimensionDriven = 1,
            // inventario swconst): e' una misura di riferimento, non comanda
            // niente, e scriverci sopra non cambierebbe il pezzo.
            bool isReadOnly = false;
            try { isReadOnly = dimension.ReadOnly; } catch { }
            if (isReadOnly)
                return Failed(
                    "La quota «" + dimensionName + "» e' di sola lettura: qualcuno l'ha bloccata " +
                    "apposta, e io non la scavalco anche se SOLIDWORKS me lo permetterebbe. " +
                    "Se va cambiata, togli prima la sola lettura dalla quota in SOLIDWORKS. " +
                    "Non ho toccato niente.");
            const int Guided = 1;   // swDimensionDrivenState_e.swDimensionDriven
            int comeGuida = 0;
            try { comeGuida = dimension.DrivenState; } catch { }
            if (comeGuida == Guided)
                return Failed(
                    "La quota «" + dimensionName + "» e' guidata (di riferimento): misura il " +
                    "pezzo, non lo comanda. Cambia invece la quota che la guida. Non ho " +
                    "toccato niente.");

            // ── GRADI O MILLIMETRI: LO DICE LA QUOTA (voce Z-C21.2, M3) ─────
            //
            // IDimension~GetType.html: «Type of dimension as defined by
            // swDimensionParamType_e» — DoubleLinear = 0, DoubleAngular = 1,
            // Integer = 2 (inventario swconst). Fino alla libreria 0.136.0 ogni
            // quota si convertiva da mm: «30» su un angolo diventava 0,03 rad
            // (1,7 gradi), e la rilettura, sbagliata allo stesso modo, diceva che
            // aveva preso. Lo stato del documento le quote angolari le scrive in
            // `deg` (DocumentState.cs, stessa GetType): qui il valore si legge
            // nella stessa unita' in cui il modello l'ha letto.
            const int AngularDimension = 1, WholeDimension = 2;
            int dimensionType = 0;
            try { dimensionType = dimension.GetType(); } catch { }
            if (dimensionType == WholeDimension)
                return Failed(
                    "La quota «" + dimensionName + "» e' un numero intero (per esempio le copie " +
                    "di una ripetizione), non una misura: con «value_mm» non la cambio. " +
                    "Non ho toccato niente.");
            bool angular = dimensionType == AngularDimension;
            Func<double, double> fromSystem = angular ? (Func<double, double>)Units.Degrees : Units.Mm;
            Func<double, double> toSystem = angular ? (Func<double, double>)Units.Rad : Units.M;
            string units = angular ? "gradi" : "mm";

            double before = fromSystem(ApiHelpers.ReadDimension(dimension));
            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            if (bodiesBefore < 0)
                return Failed(
                    "Non riesco a contare i corpi del pezzo prima di cambiare la quota: " +
                    "senza, non saprei dirti se la modifica lo spezza. Non ho toccato niente.");

            // swSetValueInConfiguration_e.swSetValue_InAllConfigurations = 2
            const int inAllConfigurations = 2;
            int outcome;
            var featuresBefore = FacesPerFeature(model);
            int openBefore = ClosedOpenings(model);

            try
            {
                outcome = dimension.SetSystemValue3(toSystem(val), inAllConfigurations, null);
            }
            catch (Exception ex)
            {
                return Failed("La quota non si e' lasciata cambiare: " + ex.Message);
            }
            if (outcome != 0)
                return Failed(
                    "SolidWorks ha rifiutato il valore " + val + " " + units + " per «" +
                    dimensionName + "» (codice " + outcome + ": " + ErrorCodeDecoder.SetValueStatus(outcome) + "). " +
                    SetValueRefusalCause(outcome));

            bool rebuilt;
            try { rebuilt = model.EditRebuild3(); }
            catch (Exception ex) { return Failed("La ricostruzione e' fallita: " + ex.Message); }

            // ── e adesso si guarda se e' successo davvero ────────────────
            double after = fromSystem(ApiHelpers.ReadDimension(dimension));
            if (Math.Abs(after - val) > 0.001)
                return Failed(
                    "La quota «" + dimensionName + "» dice ancora " +
                    after.ToString("0.###") + " " + units + " invece di " + val + ": il " +
                    "valore non ha preso. Il pezzo non e' cambiato.");

            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);

            // UNA MODIFICA CHE NON MUOVE NIENTE E' UN SOSPETTO, NON UN
            // SUCCESSO. Se la quota e' cambiata e il volume no, quasi sempre
            // vuol dire che quella quota non comanda la geometria che chi
            // chiede aveva in mente — e confermarla come «fatto» sarebbe una
            // bugia comoda.
            //
            // Fermo si puo' dire solo di due volumi LETTI (voce Z-C8): con due
            // letture fallite lo zero di prima e lo zero di dopo erano uguali,
            // e la nota diceva «il volume no».
            bool unchangedVolume = volumeBefore.HasValue && volumeAfter.HasValue &&
                               Math.Abs(volumeAfter.Value - volumeBefore.Value) < 0.01;
            if (!rebuilt)
                return Failed(
                    "La quota e' cambiata ma la ricostruzione non e' riuscita: " +
                    "il pezzo e' in errore. Guardalo prima di andare avanti." +
                    RemainsInPart);

            if (bodiesAfter >= 0 && bodiesAfter != bodiesBefore)
                return Failed(
                    "Dopo la modifica il pezzo ha " + bodiesAfter + " corpi invece " +
                    "di " + bodiesBefore + ": la quota nuova ha spezzato o unito " +
                    "qualcosa." + RemainsInPart);

            // UN'APERTURA CHE ESCE DAL MATERIALE (voce E18, pezzo c).
            //
            // Misurato nella regressione, caso r16: una piastra centrata da
            // 200 portata a 150 cala DA UNA PARTE SOLA, e l'asola lunga 120
            // al centro esce dal fianco. Ricostruzione pulita, un corpo,
            // nessun errore, il volume cambia di poco — e un'asola chiusa e'
            // diventata una tacca aperta sul bordo: un altro pezzo.
            //
            // Si guarda la TOPOLOGIA, non la geometria: le aperture chiuse
            // delle facce piane (gli anelli interni) erano N e adesso sono di
            // meno. Vuol dire che un foro o un'asola che stava dentro una
            // faccia adesso ne tocca il bordo — o che due aperture si sono
            // fuse in una. Tutte e due le cose cambiano il pezzo, e nessuna
            // delle due la voleva chi ha chiesto di cambiare una misura.
            //
            // IL RIFIUTO RIMETTE IL VALORE DI PRIMA. La quota a questo punto
            // e' gia' scritta: dire «no» e lasciarla sarebbe dire la verita' e
            // tenersi il danno. Si riscrive `before`, si ricostruisce, e si
            // RILEGGE il volume: se non torna, lo si dice.
            int openAfter = ClosedOpenings(model);
            if (openBefore >= 0 && openAfter >= 0 && openAfter < openBefore)
            {
                string restored = PutBackDimension(model, dimension, before, volumeBefore, toSystem, units);
                return Failed(
                    "Con «" + dimensionName + "» a " + val.ToString("0.###") + " " + units + " un'apertura " +
                    "esce dal materiale: le aperture chiuse sulle facce piane passano da " +
                    openBefore + " a " + openAfter + ". Un foro o un'asola che stava dentro " +
                    "il pezzo adesso ne apre il bordo, oppure due aperture si sono fuse in una." +
                    restored + " Se e' proprio quello che vuoi, sposta prima l'apertura, poi " +
                    "cambia la quota.");
            }

            // UNO SMUSSO O UN RACCORDO CHE NON CI STA PIU' (26/9/2026, caso r05
            // della regressione — l'ultimo dei silenzi).
            //
            // Misurato: piastra spessa 20 con uno smusso da 2 su uno spigolo
            // lungo, spessore portato a 1. Lo smusso non ci sta, e SOLIDWORKS NON
            // FALLISCE: ricostruisce pulito, nessuna lavorazione in errore, e si
            // mangia un millimetro di larghezza. Lo stesso col banchetto
            // (`prova0089 storia`): due raccordi da 8 in cima a una piastra, poi
            // larghezza a 10 o spessore a 6 — riuscita, zero errori, un pezzo che
            // non e' quello che i raccordi descrivono.
            //
            // Il segno c'era gia', e lo si buttava: la lavorazione DI BASE perde
            // una faccia intera — quella che smusso o raccordo adesso si prendono
            // tutta — e `ChangedFeatures` lo scriveva in una nota, con la
            // spiegazione sbagliata («una lavorazione cieca diventata passante»).
            // Qui si guarda la stessa cosa con la domanda giusta: una lavorazione
            // che NON e' un taglio ha perso facce, e piu' avanti nell'albero c'e'
            // uno smusso o un raccordo? Allora e' lui che non ci sta, e si
            // rifiuta come per le aperture: valore di prima, e volume riletto.
            //
            // I TAGLI RESTANO FUORI apposta: una tasca che perde il fondo e'
            // diventata passante, e puo' essere proprio cio' che si voleva (la
            // nota sotto lo dice e non rifiuta).
            string whoDoesNotFit = ChamferThatDoesNotFit(model, featuresBefore, FacesPerFeature(model));
            if (whoDoesNotFit != null)
            {
                string restored = PutBackDimension(model, dimension, before, volumeBefore, toSystem, units);
                return Failed(
                    "Con «" + dimensionName + "» a " + val.ToString("0.###") + " " + units + " " + whoDoesNotFit +
                    ": non ci sta piu'. SOLIDWORKS non lo segnala come errore — ricostruisce e " +
                    "cambia la forma da solo — ma il pezzo che esce non e' quello che la lavorazione " +
                    "descrive." + restored + " Se vuoi quella misura, cambia o togli prima lo smusso " +
                    "o il raccordo, poi la quota.");
            }

            var measurements = new Dictionary<string, object>
            {
                ["dimension"] = dimensionName,
                ["volume_mm3"] = NumberOrNull(volumeAfter, 2),
                ["volume_changed_mm3"] = NumberOrNull(volumeAfter - volumeBefore, 2),
            };
            // UNA QUOTA ANGOLARE NON SI RIPORTA IN «_mm» (M3): un campo che si
            // chiama mm e porta gradi e' lo stesso difetto detto in un altro
            // posto. I nomi nuovi sono in inglese, come i prossimi del ponte.
            if (angular)
            {
                measurements["before_deg"] = Math.Round(before, 3);
                measurements["after_deg"] = Math.Round(after, 3);
            }
            else
            {
                measurements["before_mm"] = Math.Round(before, 3);
                measurements["now_mm"] = Math.Round(after, 3);
            }
            if (volumeAfter == null || bodiesAfter < 0) measurements["volume_not_reread"] = NoteVolumeNotReread;

            // L'ALBERO PUO' ESSERE CAMBIATO, NON SOLO LE MISURE.
            //
            // Misurato: piastra 200x100x20 con una tasca cieca profonda 5,
            // portato lo spessore a 3. La tasca diventa PASSANTE — la
            // lavorazione di taglio passa da 2 facce a 1, perche' il fondo non
            // c'e' piu' — e nessuno lo dice: GetErrorCode2 risponde 0,
            // IsWarning e' falso, il volume torna, i corpi sono sempre uno.
            //
            // Una sede cieca e un passaggio sono due pezzi diversi. Chi ha
            // chiesto di assottigliare la piastra magari lo voleva, quindi non
            // si rifiuta; ma deve poterlo leggere qui invece di scoprirlo in
            // officina.
            var changed = ChangedFeatures(
                featuresBefore, FacesPerFeature(model), measurements);

            measurements["note"] = unchangedVolume
                ? "la quota e' cambiata ma il volume no: controlla che sia quella giusta"
                : changed;

            return Succeeded(null, measurements);
        }

        /// <summary>
        /// Quante aperture CHIUSE ha il pezzo: gli anelli interni delle facce
        /// PIANE, contati su tutti i corpi solidi. -1 se non si riesce a
        /// contare, e allora la guardia di E18 tace invece di inventare.
        ///
        /// SOLO LE FACCE PIANE. Una faccia cilindrica di un foro passante ha
        /// due anelli, uno per bocca, e quale dei due SOLIDWORKS chiami
        /// «outer» su una superficie periodica non e' una cosa su cui
        /// appoggiare una guardia. Su una faccia piana un anello interno e' un
        /// buco in quella faccia, e basta.
        /// </summary>
        private static int ClosedOpenings(IModelDoc2 model)
        {
            int n = 0;
            try
            {
                var bodies = (model as IPartDoc)?.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (bodies == null) return -1;
                foreach (var c in bodies)
                {
                    var face = (c as IBody2)?.GetFirstFace() as IFace2;
                    while (face != null)
                    {
                        bool planar = false;
                        try { planar = (face.GetSurface() as ISurface)?.IsPlane() == true; } catch { }
                        if (planar)
                        {
                            var rings = face.GetLoops() as object[];
                            if (rings != null)
                                foreach (var o in rings)
                                {
                                    var ring = o as ILoop2;
                                    if (ring != null && !ring.IsOuter()) n++;
                                }
                        }
                        face = face.GetNextFace() as IFace2;
                    }
                }
            }
            catch { return -1; }
            return n;
        }

        /// <summary>
        /// Cerca una quota per nome in tutto l'albero.
        ///
        /// Il nome puo' essere quello pieno («D1@Schizzo1») o quello corto
        /// («D1»): il modello legge il pieno nello stato, ma una persona che
        /// scrive a mano usa il corto, e rifiutare per quello sarebbe pedante.
        /// Se non lo trova, elenca i nomi che ci sono: un «non trovato» senza
        /// l'elenco costringe a indovinare.
        /// </summary>
        private static IDimension FindDimension(IModelDoc2 model, string itemName, out string available)
        {
            available = null;
            var partDoc = model as IPartDoc;
            if (partDoc == null) return null;
            // UNA VOLTA SOLA PER QUOTA. Uno schizzo assorbito compare due
            // volte nell'albero — come schizzo e dentro la lavorazione che
            // se l'e' preso — e la sua quota si incontra due volte.
            var names = new List<string>();
            var already = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IFeature f = partDoc.IFirstFeature();
            while (f != null)
            {
                object display = null;
                try { display = f.GetFirstDisplayDimension(); } catch { }
                while (display != null)
                {
                    IDimension d = null;
                    try { d = (display as IDisplayDimension)?.GetDimension2(0); } catch { }
                    if (d != null)
                    {
                        string full = null, shorter = null;
                        try { full = d.FullName; shorter = d.Name; } catch { }
                        if (!string.IsNullOrEmpty(full))
                        {
                            if (names.Count < 12 && already.Add(full)) names.Add(full);

                            // TRE FORME, E TUTTE E TRE DEVONO ANDARE.
                            //
                            //   D1                          il nome corto
                            //   D1@Schizzo1                 nome e lavorazione
                            //   D1@Schizzo1@Parte286.Part   come lo scrive lo stato
                            //
                            // La seconda mancava, e il messaggio d'errore la
                            // dava per buona («per esempio «D1@Schizzo1»»):
                            // il messaggio diceva il falso.
                            //
                            // E non e' un dettaglio di comodita': la forma con
                            // il documento in coda contiene il TITOLO del
                            // documento, quindi un caso di banco scritto con
                            // quella non si puo' rilanciare su un altro pezzo.
                            // Senza la forma di mezzo, le prove che cambiano
                            // una quota non sono ripetibili.
                            string withoutDocument = full;
                            int lastAtSign = full.LastIndexOf('@');
                            if (lastAtSign > 0)
                                withoutDocument = full.Substring(0, lastAtSign);

                            if (string.Equals(full, itemName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(shorter, itemName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(withoutDocument, itemName, StringComparison.OrdinalIgnoreCase))
                            {
                                return d;
                            }
                        }
                    }
                    object next = null;
                    try { next = f.GetNextDisplayDimension(display); } catch { }
                    display = next;
                }
                try { f = (IFeature)f.GetNextFeature(); } catch { break; }
            }
            if (names.Count > 0) available = string.Join(", ", names);
            return null;
        }

        private Outcome FilletEdges(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double radius = Num(a, "radius_mm");
            int howMany;
            string why;
            // The volume before, for the check of M37 in RunFillet; read
            // before the edges are selected (GetMassProperties on the bodies,
            // which does not touch the selection).
            double? volumeBefore = VolumeMm3(model);
            if (!SelectEdges(model, a, docState, radius, out howMany, out why))
                return Failed(why);
            return RunFillet(model, radius, howMany, volumeBefore);
        }

        /// <summary>
        /// What to do about a value that IDimension.SetSystemValue3 refused, by
        /// its status (Z-C21.8, finding L5-6). IDimension~SetSystemValue3.html:
        /// «Success indicator value as defined in swSetValueReturnStatus_e».
        /// Until the review of this lot every refusal said «usually a dimension
        /// governed by an equation or a relation», also for a value out of range,
        /// a frozen feature or a model not loaded. The status name is already in
        /// the message (ErrorCodeDecoder.SetValueStatus); this is the remedy that
        /// goes with it. Only what the enum name says is said: none of these
        /// statuses was provoked on purpose by a probe. No interop type in the
        /// signature: Oce.Solid.Agent.ContractTests calls it by reflection.
        /// </summary>
        internal static string SetValueRefusalCause(int status)
        {
            switch (status)
            {
                case (int)swSetValueReturnStatus_e.swSetValue_InvalidValue:
                    return "SOLIDWORKS does not take this value for this dimension: ask for a value the " +
                           "geometry allows (not zero or negative for a length).";
                case (int)swSetValueReturnStatus_e.swSetValue_DrivenDimension:
                    return "It is a DRIVEN dimension: its value follows the geometry, it is not set. Change the " +
                           "dimension that drives it.";
                case (int)swSetValueReturnStatus_e.swSetValue_ModelNotLoaded:
                    return "The model that owns the dimension is not loaded: open it, then ask again.";
                case (int)swSetValueReturnStatus_e.swSetValue_FrozenFeatureOwner:
                    return "The feature that owns the dimension is FROZEN: move the freeze bar below it, then " +
                           "ask again.";
                case (int)swSetValueReturnStatus_e.swSetValue_Failure:
                    return "SOLIDWORKS gives no reason: check whether an equation or a relation drives the " +
                           "dimension, then ask again.";
                default:
                    return "Read the status above, then ask again.";
            }
        }

        /// <summary>
        /// A FILLET OR A CHAMFER MUST MOVE THE VOLUME OF THE PART (Z-C21.8,
        /// finding M37): the fault in words, or null. fillet_edges and
        /// chamfer_edges had no check on the volume at all, while
        /// variable_fillet had one: a feature that leaves the volume where it
        /// was has worked nothing, and the tree says otherwise. The SIGN is not
        /// judged: on a concave edge a fillet or a chamfer ADDS material. A
        /// volume that could not be read, before or after, is not a fault here:
        /// the answer says «not read» instead. Kept apart from the document so
        /// that Oce.Solid.Agent.ContractTests can call it without SOLIDWORKS.
        /// </summary>
        internal static string EdgeFeatureVolumeFault(double? volumeBefore, double? volumeAfter)
        {
            if (volumeBefore == null || volumeAfter == null) return null;
            if (Math.Abs(volumeAfter.GetValueOrDefault() - volumeBefore.GetValueOrDefault()) >= 0.01) return null;
            return "the volume of the part did not change (" +
                   volumeAfter.GetValueOrDefault().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " mm3 before and after)";
        }

        /// <summary>
        /// The volume check of <see cref="EdgeFeatureVolumeFault"/> applied to a
        /// fillet or a chamfer just born: null when it passes (the change, or
        /// the note that the volume was not read, goes into
        /// <paramref name="measures"/>), the refusal otherwise.
        /// </summary>
        private string EdgeFeatureVolumeCheck(
            IModelDoc2 model, string what, double? volumeBefore, Dictionary<string, object> measures)
        {
            try { model.EditRebuild3(); } catch { }
            double? volumeAfter = VolumeMm3(model);
            string unchanged = EdgeFeatureVolumeFault(volumeBefore, volumeAfter);
            if (unchanged != null)
                return "The " + what + " was created, but " + unchanged + ": it worked nothing, and the tree says " +
                       "otherwise. It is still in the part: undo it (" + Sentences.HowToWithdrawIt + ").";
            if (volumeBefore == null || volumeAfter == null)
                measures["volume_not_read"] = "The volume of the part could not be read " +
                                              (volumeBefore == null ? "before" : "after") +
                                              " the " + what + ", so the " + what + " was not checked against it.";
            else
                measures["material_change_mm3"] = Math.Round(volumeAfter.GetValueOrDefault() - volumeBefore.GetValueOrDefault(), 2);
            return null;
        }

        /// <summary>
        /// Lo smusso: il gemello del raccordo, e la ragione per cui la
        /// selezione degli spigoli e' stata estratta in un metodo solo.
        ///
        /// In carpenteria lo smusso e' una voce di capitolato: si smussa per
        /// togliere la bava, per far entrare un pezzo, per preparare un
        /// cianfrino da saldare. Fino alla 0.30.2 l'assistente sapeva
        /// arrotondare e non sapeva smussare, e chi lo chiedeva si sentiva
        /// rispondere di no.
        ///
        /// FIRMA VERIFICATA per riflessione sull'interop 29.5.1.1:
        ///   IFeatureManager.InsertFeatureChamfer(Int32 Options, Int32
        ///       ChamferType, Double Width, Double Angle, Double OtherDist,
        ///       Double VertexChamDist1, Double VertexChamDist2,
        ///       Double VertexChamDist3) -> Feature
        ///   swChamferType_e: AngleDistance=1, DistanceDistance=2, Vertex=3,
        ///       EqualDistance=16
        ///   swFeatureChamferOption_e: FlipDirection=1, KeepFeature=2,
        ///       TangentPropagation=4, PropagateFeatToParts=8
        ///
        /// ATTENZIONE, ed e' scritto nel RAG fra «le sette API che sembravano
        /// giuste»: gli enum dello smusso sono DUE e hanno gli stessi numeri
        /// con significati diversi — `swSketchChamferType_e` vale per lo
        /// schizzo, `swChamferType_e` per la feature. Qui serve il secondo.
        ///
        /// THE TWO SIDES GO IN Width AND OtherDist, NOT WHERE THE GUIDE PUTS
        /// THEM. IFeatureManager~InsertFeatureChamfer.html gives Width only for
        /// swChamferAngleDistance and OtherDist only for swChamferEqualDistance,
        /// and says «VertexChamDist1 — If ChamferType is swChamferDistanceDistance
        /// or swChamferVertex, then specify a value for the distance on first
        /// side». Measured otherwise: Bench/cases/chamfer/chamfer_3x6_bevel
        /// asks 3 and 6 on a 100 mm edge and gets 119100 mm3 exactly (120000
        /// minus 3 x 6 / 2 x 100); an ignored second side would leave 119550,
        /// and a SOLIDWORKS reading VertexChamDist1/2 = 0 would make no chamfer
        /// at all (audit finding L7-2, not real). Moving to VertexChamDist1/2
        /// means running that case before and after.
        /// </summary>
        private Outcome ChamferEdges(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double side = Num(a, "leg_mm");
            double angle = Num(a, "angle_deg", 45);
            double secondSide = Num(a, "second_leg_mm", 0);
            bool twoSides = secondSide > 0;

            int howMany;
            string why;
            // The volume before, for the check of M37 below (EdgeFeatureVolumeCheck).
            double? volumeBefore = VolumeMm3(model);
            // Il limite «meta' dello spigolo» vale anche per lo smusso: il lato
            // piu' lungo dei due e' quello che non ci puo' stare.
            if (!SelectEdges(model, a, docState,
                                  Math.Max(side, secondSide), out howMany, out why))
                return Failed(why);

            int typ = twoSides
                ? (int)swChamferType_e.swChamferDistanceDistance
                : (int)swChamferType_e.swChamferAngleDistance;

            IFeature feature;
            try
            {
                feature = model.FeatureManager.InsertFeatureChamfer(
                    Options: (int)swFeatureChamferOption_e.swFeatureChamferTangentPropagation,
                    ChamferType: typ,
                    Width: Units.M(side),
                    Angle: Units.Rad(angle),
                    OtherDist: twoSides ? Units.M(secondSide) : 0,
                    VertexChamDist1: 0,
                    VertexChamDist2: 0,
                    VertexChamDist3: 0) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("Lo smusso non e' riuscito: " + ex.Message);
            }
            if (feature == null)
                return Failed(
                    "Lo smusso non e' stato creato. Di solito e' la misura: un " +
                    "lato piu' grande del materiale che c'e' li' intorno non si " +
                    "puo' togliere.");

            var measurements = new Dictionary<string, object>
            {
                ["leg_mm"] = side,
                ["edges"] = howMany,
            };
            SelectedEdgesAndFaces(feature, howMany, measurements);
            if (twoSides) measurements["second_leg_mm"] = secondSide;
            else measurements["angle_deg"] = angle;
            // After the counts, which SelectedEdgesAndFaces reads right after
            // creation as the probe did: the check rebuilds before measuring.
            string chamferUnchanged = EdgeFeatureVolumeCheck(model, "chamfer", volumeBefore, measurements);
            if (chamferUnchanged != null) return Failed(chamferUnchanged);
            return Succeeded(feature, measurements);
        }

        /// <summary>
        /// Un piano di riferimento nuovo, parallelo a uno dei tre piani base.
        ///
        /// SOLO PIANI BASE, E IL MOTIVO STA NEL VALIDATORE DEL SERVER.
        /// `valida()` impone SEMPRE uno fra `face_uid` e `face_ref` appena
        /// quella proprieta' compare nello schema di un'operazione. Se qui si
        /// dichiarasse anche una faccia come riferimento alternativo, «piano
        /// superiore, 600 mm» da solo non sarebbe piu' accettato: servirebbe
        /// comunque un identificativo di faccia. La versione 1 fa il caso che
        /// il committente ha chiesto per nome.
        ///
        /// THE CALL (Z-C21.7, L8-1): IFeatureManager.InsertRefPlane with the
        /// base plane selected with mark 0 (the first reference entity), the
        /// Distance constraint, OptionFlip when the distance is negative, and
        /// the ABSOLUTE distance: ApiHelpers.SelectOffsetPlaneBase and
        /// ApiHelpers.InsertOffsetPlane, where the guide pages and the probe
        /// numbers are. Until Z-C21.7 this was the obsolete
        /// IModelDoc2.CreatePlaneAtOffset3, because InsertRefPlane had been
        /// measured to make no plane on 10/9/2026; the InsertRefPlane probe of
        /// Z-C21.3 (28/9/2026) showed that measure was
        /// made with mark 1 (H1: no plane), while mark 0 makes the plane where
        /// it belongs (G1 top +100, G4 top -100, G6 front -250). The sign of
        /// the distance is measured too: a signed distance puts the plane on
        /// the base plane (G5: (0, 0, 0) for -100), hence OptionFlip.
        /// </summary>
        private Outcome CreatePlane(IModelDoc2 model, Dictionary<string, object> a)
        {
            string plane = Str(a, "plane", "front");
            double distance;
            string error;
            if (!RequiredNum(a, "distance_mm", out distance, out error))
                return Failed(error);

            model.ClearSelection2(true);
            IFeature reference = FindBasePlane(model, plane);
            if (reference == null)
                return Failed($"Non ho trovato il piano «{plane}» nell'albero.");
            // The PLANE is selected by name, with mark 0; the tree entry through
            // IFeature.Select2 is only the fallback (the probe's G3 measured it
            // working with InsertRefPlane too).
            if (!ApiHelpers.SelectOffsetPlaneBase(model, reference))
                return Failed($"Non sono riuscito a selezionare il piano «{plane}».");

            // WHAT SOLIDWORKS REALLY HOLDS WHEN WE CALL. InsertRefPlane works on
            // what is selected WITH THE RIGHT MARK, and with the wrong mark it
            // returns null and says nothing (H1). These lines cost nothing and
            // turn a "not created" into a diagnosis.
            string comEra = "";
            try
            {
                var sel = model.SelectionManager as ISelectionMgr;
                int howMany = sel == null ? -1 : sel.GetSelectedObjectCount2(-1);
                int mark = (sel != null && howMany > 0) ? sel.GetSelectedObjectMark(1) : -1;
                string itemName = "";
                try { itemName = reference.Name; } catch { }
                int typ = (sel != null && howMany > 0) ? sel.GetSelectedObjectType3(1, -1) : -1;
                comEra = " [selezionati " + howMany + ", marca " + mark +
                         ", tipo " + typ + ", piano «" + itemName + "»]";
            }
            catch { }

            // THE SIDE IS A BIT, NOT A SIGN (probe G4/G5): the absolute
            // distance, with OptionFlip when the distance asked is negative.
            int planesBefore = CountPlanes(model);
            var namesBefore = FeatureNamesNow(model);
            IFeature feature = null;
            int planesBorn = -1;
            try
            {
                object raw = ApiHelpers.InsertOffsetPlane(model, distance);
                feature = raw as IFeature;
                // The return value is a RefPlane; the probe found it to be an
                // IFeature as well. When the cast fails the plane is the ONE
                // RefPlane born in the tree since the call (FeatureBorn, scheme
                // S7, Z-C21.5), never the last feature of the tree: on a
                // sheet-metal part that is the flat pattern (finding M9).
                if (feature == null)
                    feature = FeatureBorn(model, namesBefore, "RefPlane", out planesBorn);
            }
            catch (Exception ex)
            {
                return Failed("Il piano non e' stato creato: " + ex.Message + comEra);
            }
            if (feature == null && planesBorn > 1)
                return Failed(
                    "InsertRefPlane returned no plane and " + planesBorn + " reference planes were born in the " +
                    "tree: I do not pick one of them for you. Look at the part before going on." + comEra);
            if (feature == null && namesBefore == null)
                return Failed(
                    "InsertRefPlane returned no plane, and the feature tree could not be read to find one it " +
                    "may have made. Look at the part before going on." + comEra);
            if (feature == null)
                return Failed(
                    "Il piano non e' stato creato, e nell'albero non ne e' " +
                    "comparso nessuno." + comEra);

            return Succeeded(feature, new Dictionary<string, object>
            {
                ["starting_plane"] = plane,
                ["planes_before"] = planesBefore,
                ["distance_mm"] = distance,
            });
        }

        /// <summary>
        /// Un asse nuovo, lungo il centro di una faccia cilindrica o conica.
        ///
        /// E' l'operazione che manca perche' «circular_pattern» smetta di
        /// essere un vicolo cieco: pretende un asse che esista gia', e fino
        /// alla 0.30.2 nessuna operazione del catalogo ne creava uno.
        ///
        /// SIGNATURES CHECKED on the interop 29.5.1.1:
        ///   IModelDoc2.InsertAxis2(Boolean AutoSize) -> Boolean
        ///   ISurface.IsCylinder(), ISurface.IsCone()
        ///
        /// IT RETURNS A BOOLEAN, NOT A FEATURE, like the shell. Until Z-C21.5
        /// the axis was taken as the last feature of the tree
        /// (IFeatureByPositionReverse(0)) and refused when that was not a
        /// «RefAxis»: on a sheet-metal part the last feature is the flat
        /// pattern, and the operation refused an axis it had made and left it
        /// in the tree (finding L8-6). The axis is now the ONE RefAxis born
        /// since the call (FeatureBorn, scheme S7), wherever SOLIDWORKS put it.
        /// «RefAxis» is the string DocumentState recognises axes by, so an axis
        /// that passes here shows in the state at the next step.
        /// </summary>
        private Outcome CreateAxis(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var face = ResolveFace(docState, a, Str(a, "face_uid", ""), out string reason);
            if (face == null) return Failed(reason);

            bool round;
            try
            {
                var surface = face.IGetSurface();
                round = surface != null && (surface.IsCylinder() || surface.IsCone());
            }
            catch (Exception ex)
            {
                return Failed("Non riesco a leggere la faccia: " + ex.Message);
            }
            if (!round)
                return Failed(
                    "Questa faccia non e' cilindrica ne' conica: l'asse si fa " +
                    "dal centro di un foro o di un albero. Per l'asse " +
                    "all'intersezione di due piani questa versione non ha " +
                    "un'operazione.");

            model.ClearSelection2(true);
            var entity = face as IEntity;
            if (entity == null || !entity.SelectByMark(false, SelectionMarks.AxisFace))
                return Failed("Non sono riuscito a selezionare la faccia.");

            var namesBefore = FeatureNamesNow(model);
            if (namesBefore == null)
                return Failed(
                    "I cannot read the feature tree before making the axis, and without it I could not tell " +
                    "the new axis from the ones already there. Nothing was changed.");

            bool created;
            try { created = model.InsertAxis2(true); }
            catch (Exception ex) { return Failed("L'asse non e' stato creato: " + ex.Message); }
            if (!created)
                return Failed(
                    "L'asse non e' stato creato. Di solito e' la faccia: non e' " +
                    "abbastanza cilindrica, o e' quella sbagliata.");

            // The ONE axis born since the call, not the last feature of the tree
            // (finding L8-6, scheme S7).
            int axesBorn;
            IFeature feature = FeatureBorn(model, namesBefore, "RefAxis", out axesBorn);
            if (feature == null)
                return Failed(
                    (axesBorn < 0
                        ? "SOLIDWORKS says it made the axis, but I cannot read the feature tree to find it"
                        : axesBorn == 0
                            ? "SOLIDWORKS says it made the axis, but no new RefAxis is in the feature tree"
                            : "SOLIDWORKS says it made the axis, and " + axesBorn + " new RefAxis features are in the tree") +
                    ": I do not confirm an axis I cannot point to. Look at the part before trusting the rest of the plan.");

            return Succeeded(feature, new Dictionary<string, object>
            {
                ["axis"] = feature.Name,
            });
        }

        /// <summary>
        /// Ripete una lavorazione in fila, o in griglia su due direzioni.
        ///
        /// PERCHE' CONTA. Otto fori in fila, senza questa, vogliono otto
        /// chiamate con otto coordinate calcolate a mano: lento, e ogni
        /// coordinata e' un'occasione di sbagliare. Peggio: il pezzo finito
        /// non DICHIARA che quei fori sono una serie, quindi cambiarne il
        /// passo vuol dire rifarli tutti.
        ///
        /// FIRMA VERIFICATA sull'interop 29.5.1.1 — e' gia' la piu' alta:
        ///   IFeatureManager.FeatureLinearPattern5(Num1, Spacing1, Num2,
        ///       Spacing2, FlipDir1, FlipDir2, DName1, DName2,
        ///       GeometryPattern, VaryInstance, HasOffset1, HasOffset2,
        ///       CtrlByNum1, CtrlByNum2, FromCentroid1, FromCentroid2,
        ///       RevOffset1, RevOffset2, Offset1, Offset2, D2PatternSeedOnly,
        ///       SyncSubAssemblies) -> Feature
        ///
        /// THE MARKS ARE MEASURED, NOT GUESSED (Z-C21.9, finding L7-5, not
        /// real). Mark 1 for the first direction, mark 2 for the second and
        /// mark 4 for the feature (SelectionMarks.LinearPatternDirection1/2,
        /// LinearPatternFeatures; IFeatureManager~FeatureLinearPattern5.html,
        /// «1 to mark Direction 1», «2 to mark Direction 2»). Reflection cannot
        /// check them, they are behaviour; the Z-C21.3 probe of L7-5 and M16
        /// (28/9/2026) did: a row along an edge and along an axis of create_axis
        /// (L0-L5, six ways of selecting), and a grid on two edges and on two
        /// axes (G0-G3), each with the expected removed volume and the new hole
        /// centres where they should be. Until Z-C21.9 this note called mark 2
        /// an analogy still to be measured on a grid.
        ///
        /// GeometryPattern resta FALSE. Copiare solo la forma e' piu' veloce
        /// e piu' fragile: un foro filettato ripetuto cosi' perde il filetto
        /// sulle copie, e nessun errore lo dice.
        /// </summary>
        private Outcome LinearPattern(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double? volumeBefore; int bodiesBefore, facesBefore;
            Snapshot(model, out volumeBefore, out bodiesBefore, out facesBefore);
            if (volumeBefore == null || bodiesBefore < 0 || facesBefore < 0)
                return Failed(VolumeNotReadBefore("fare la copia"));

            double pitch1, instances1Raw;
            string error;
            if (!RequiredNum(a, "pitch1_mm", out pitch1, out error)) return Failed(error);
            if (!RequiredNum(a, "instance_count1", out instances1Raw, out error))
                return Failed(error);
            int instances1 = (int)instances1Raw;


            string axis2 = Str(a, "axis2_uid", "");
            bool grid = !string.IsNullOrEmpty(axis2);
            int instances2 = 1;
            double pitch2 = 0;
            if (grid)
            {
                double n2;
                if (!RequiredNum(a, "instance_count2", out n2, out error))
                    return Failed("Hai dato «axis2_uid» ma non «instance_count2»: " + error);
                instances2 = (int)n2;
                if (instances2 < 2)
                    return Failed(
                        "«instance_count2» deve essere almeno 2. Per una fila sola " +
                        "ometti «axis2_uid», invece di mettere 1.");
                if (!RequiredNum(a, "pitch2_mm", out pitch2, out error))
                    return Failed("Hai dato «axis2_uid» ma non «pitch2_mm»: " + error);
            }

            model.ClearSelection2(true);
            if (!SelectAxis(model, a, "axis1", docState, SelectionMarks.LinearPatternDirection1, append: false, out error)) return Failed(error);
            // La seconda direzione passa dallo STESSO aiutante, con Mark 2:
            // prima aveva una riga sua che accettava solo un asse, e da quando
            // la direzione puo' essere uno spigolo le due strade divergevano —
            // una griglia con la direzione 1 su uno spigolo e la 2 no.
            if (grid && !SelectAxis(model, a, "axis2", docState, SelectionMarks.LinearPatternDirection2, append: true, out error))
                return Failed("Direzione 2: " + error);

            if (!SelectFeature(model, a, out error)) return Failed(error);
            // Prima di costruire: creare la ripetizione azzera la selezione.
            var seed = SelectedSeed(model, SelectionMarks.LinearPatternFeatures);

            IFeature feature;
            try
            {
                feature = model.FeatureManager.FeatureLinearPattern5(
                    Num1: instances1,
                    Spacing1: Units.M(pitch1),
                    Num2: instances2,
                    Spacing2: Units.M(pitch2),
                    FlipDir1: Bool(a, "flip1", false),
                    FlipDir2: Bool(a, "flip2", false),
                    DName1: "",
                    DName2: "",
                    GeometryPattern: false,
                    VaryInstance: false,
                    HasOffset1: false,
                    HasOffset2: false,
                    CtrlByNum1: true,
                    CtrlByNum2: true,
                    FromCentroid1: false,
                    FromCentroid2: false,
                    RevOffset1: false,
                    RevOffset2: false,
                    Offset1: 0,
                    Offset2: 0,
                    // false = griglia PIENA: ogni istanza della direzione 1 si
                    // ripete anche sulla 2. true farebbe una croce, che ripete
                    // solo l'originale. Per una piastra forata a matrice serve
                    // la griglia.
                    D2PatternSeedOnly: false,
                    SyncSubAssemblies: false) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("La ripetizione lineare non e' riuscita: " + ex.Message);
            }
            if (feature == null)
                return Failed(
                    "La ripetizione lineare non e' stata creata. Di solito e' il " +
                    "passo o il numero: una serie che esce dal materiale, o che " +
                    "si sovrappone a se stessa, non si costruisce.");

            var measurements = new Dictionary<string, object>
            {
                ["instance_count1"] = instances1,
                ["pitch1_mm"] = pitch1,
            };
            if (grid)
            {
                measurements["instance_count2"] = instances2;
                measurements["pitch2_mm"] = pitch2;
            }

            var stopped = CompareCopy(
                model, volumeBefore, bodiesBefore, facesBefore, measurements,
                "La ripetizione lineare");
            if (stopped != null) return Failed(stopped);

            var missingItems = MissingCopies(
                measurements, feature, seed, instances1 * instances2 - 1,
                "La ripetizione lineare",
                "Se la fila esce dal pezzo da una parte sola, inverti il verso con " +
                "«flip1» (o «flip2» per la seconda direzione); " +
                "altrimenti riduci il passo o il numero di istanze.");
            if (missingItems != null) return Failed(missingItems);

            return Succeeded(feature, measurements);
        }

        /// <summary>
        /// Specchia una lavorazione rispetto a uno dei tre piani base.
        ///
        /// FIRMA VERIFICATA sull'interop 29.5.1.1 — gia' la piu' alta:
        ///   IFeatureManager.InsertMirrorFeature2(Boolean BMirrorBody, Boolean
        ///       BGeometryPattern, Boolean BMerge, Boolean BKnit, Int32
        ///       ScopeOptions) -> Feature
        ///   swFeatureScope_e: AllBodies = 0
        ///
        /// THE MARKS ARE THE GUIDE'S, AND MEASURED: the plane with mark 2 and
        /// the features with mark 1 (SelectionMarks.MirrorFeaturePlane and
        /// MirrorFeatureFeatures). IFeatureManager~InsertMirrorFeature2.html,
        /// Remarks table: «Features to be mirrored 1», «Plane or planar face 2».
        /// Until 0.35.0 this used the pattern's 1 and 4, and no mirror was
        /// created. The off-centre hole the old note asked for is the bench
        /// case mirror/hole_mirrored_on_right: a hole at (60, 25) mirrored
        /// on the right plane gives holes at x -60 and 60, y 25 and 25,
        /// 475476.11 mm3, one body.
        ///
        /// Si specchia la LAVORAZIONE, non il corpo: `BMirrorBody` resta
        /// false. Specchiare un corpo intero e' il caso piu' comune in
        /// officina per un pezzo simmetrico, ma vuole capire prima come si
        /// seleziona «il corpo» — nessun aiutante di questo file lo fa — e
        /// riverificare gli stessi Mark su quel ramo.
        /// </summary>
        private Outcome Mirror(IModelDoc2 model, Dictionary<string, object> a)
        {
            string plane = Str(a, "plane", "");
            if (string.IsNullOrEmpty(plane)) return Failed("Manca il piano di simmetria.");

            double? volumeBefore; int bodiesBefore, facesBefore;
            Snapshot(model, out volumeBefore, out bodiesBefore, out facesBefore);
            if (volumeBefore == null || bodiesBefore < 0 || facesBefore < 0)
                return Failed(VolumeNotReadBefore("fare la copia"));

            model.ClearSelection2(true);
            var planeFeature = FindBasePlane(model, plane);
            // `InsertMirrorFeature2` vuole il PIANO con la marca 2 e le
            // lavorazioni con la 1 — non le stesse marche della ripetizione.
            // Fino alla 0.35.0 qui c'erano 1 e 4, cioe' quelle del pattern, e
            // lo specchio non veniva creato.
            if (planeFeature == null || !planeFeature.Select2(false, SelectionMarks.MirrorFeaturePlane))
                return Failed($"Non ho trovato il piano «{plane}» nell'albero.");

            string error;
            if (!SelectFeature(model, a, out error, mark: SelectionMarks.MirrorFeatureFeatures)) return Failed(error);
            var seed = SelectedSeed(model, SelectionMarks.MirrorFeatureFeatures);

            IFeature feature;
            try
            {
                feature = model.FeatureManager.InsertMirrorFeature2(
                    BMirrorBody: false,
                    BGeometryPattern: false,
                    BMerge: true,
                    BKnit: false,
                    ScopeOptions: (int)swFeatureScope_e.swFeatureScope_AllBodies) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("Lo specchio non e' riuscito: " + ex.Message);
            }
            if (feature == null)
                return Failed(
                    "Lo specchio non e' stato creato. Se il piano scelto taglia la " +
                    "lavorazione invece di lasciarla tutta da un lato, o se la " +
                    "copia uscirebbe fuori dal materiale, SolidWorks rifiuta.");

            var measurements = new Dictionary<string, object> { ["plane"] = plane };
            var stopped = CompareCopy(
                model, volumeBefore, bodiesBefore, facesBefore, measurements, "Lo specchio");
            if (stopped != null) return Failed(stopped);

            // QUI IL RIFIUTO NON PUO' SCATTARE, ED E' GIUSTO COSI'.
            // Con una copia sola servirebbe «zero copie fatte», e zero non
            // esce mai: MissingCopies si ferma prima se la feature non ha
            // facce. Quindi sullo specchio questa chiamata solo RIPORTA
            // «copies_done», e lo specchio che non ha specchiato niente resta
            // in carico a CompareCopy, qui sopra, che lo prende dal pezzo
            // rimasto identico.
            //
            // Sta scritto perche' una guardia che sembra proteggere e non puo'
            // scattare e' peggio di una guardia assente: la prima volta si
            // legge come una difesa, la seconda ci si conta sopra.
            MissingCopies(measurements, feature, seed, 1, "Lo specchio", "");

            return Succeeded(feature, measurements);
        }

        /// <summary>
        /// L'asse attorno a cui — o lungo cui — si ripete, con Mark 1.
        ///
        /// PER RIFERIMENTO O PER IDENTIFICATIVO, ED E' UNA CORREZIONE.
        /// Fino alla 0.30.2 `circular_pattern` accettava SOLO `axis_uid`, cioe'
        /// un asse che esistesse gia' nel documento quando il piano nasceva. E
        /// nessuna operazione del catalogo creava assi. Il risultato: la
        /// ripetizione circolare era un vicolo cieco, offerta e mai
        /// utilizzabile.
        ///
        /// Adesso «create_axis» ne fa uno, e questo aiutante permette al passo
        /// dopo di citarlo — «fai l'asse del foro, poi ripeti la lavorazione
        /// sei volte attorno» in un piano solo. L'oggetto si seleziona
        /// direttamente: il nome che SolidWorks gli ha dato lo sapremmo solo
        /// rileggendo l'albero, e sarebbe un giro in piu' per arrivare alla
        /// stessa feature che gia' abbiamo in mano.
        /// </summary>
        /// <param name="prefix">«axis», «axis1» oppure «axis2»: i campi si
        /// chiamano diversamente ma si risolvono identici.</param>
        /// <param name="mark">
        /// The selection mark, from <see cref="SelectionMarks"/>: the main
        /// direction (which all three patterns read), or the second direction
        /// of a grid.
        /// </param>
        /// <param name="append">
        /// True to ADD to the selection already made (the second direction of a
        /// grid), otherwise the second direction would clear the first. Explicit
        /// since Z-C21.6: it used to be inferred from `mark != 1`, which tied
        /// the selection behaviour to the numeric value of a mark.
        /// </param>
        private bool SelectAxis(
            IModelDoc2 model, Dictionary<string, object> a, string prefix,
            DocumentState docState, int mark, bool append, out string reason)
        {
            reason = null;
            string refField = prefix + "_ref";
            if (HasReference(a, refField))
            {
                int number;
                var planStep = CitedStep(a, refField, out number, out reason);
                if (planStep == null)
                {
                    reason = reason ?? "Riferimento all'asse non risolto.";
                    return false;
                }
                if (planStep.Feature == null)
                {
                    reason = "Il passo " + number + " non ha lasciato nessun asse.";
                    return false;
                }
                if (!planStep.Feature.Select2(append, mark))
                {
                    reason = "Non sono riuscito a selezionare l'asse del passo " + number + ".";
                    return false;
                }
                _log("[agente] asse preso dal passo " + number);
                return true;
            }

            string uid = Str(a, prefix + "_uid", "");
            if (string.IsNullOrEmpty(uid))
            {
                reason =
                    "Non hai detto lungo o attorno a quale direzione. Indica «" +
                    prefix + "_uid» con un asse OPPURE con uno spigolo dritto " +
                    "dello stato del documento, oppure «" + refField + "» se " +
                    "l'asse lo crea un passo precedente di questo piano con " +
                    "«create_axis».";
                return false;
            }

            // ── UNO SPIGOLO DRITTO VALE COME DIREZIONE, ED E' UNA CORREZIONE ──
            //
            // Fino alla 0.30.2 qui c'era solo `SelectByID2(uid, "AXIS", ...)`,
            // cioe' un asse di riferimento. Ma l'unica operazione del catalogo
            // che crea assi e' «create_axis», che li prende dal centro di una
            // faccia CILINDRICA: l'asse che ne esce e' sempre quello di un
            // foro o di un albero, cioe' PERPENDICOLARE alla faccia su cui la
            // lavorazione sta. Ripetere lungo quello vuol dire ripetere dentro
            // lo spessore.
            //
            // Risultato: «otto fori in fila sulla piastra» — il caso per cui
            // esiste la ripetizione lineare — non aveva NESSUNA forma valida.
            // Era lo stesso vicolo cieco che la 0.30.2 ha chiuso per la
            // ripetizione circolare, un piano piu' in la'.
            //
            // Uno spigolo del pezzo la direzione ce l'ha, e lo stato del
            // documento gli spigoli li elenca gia' (E1, E2, ...). SolidWorks
            // accetta uno spigolo lineare come direzione di una ripetizione
            // lineare e come asse di una circolare.
            //
            // The circular half is measured, not only accepted (Z-C21.9,
            // finding M16, not real): the Z-C21.3 probe of L7-5 and M16 (28/9/2026)
            // turned four copies exactly around a straight edge selected with
            // SelectByMark and mark 1, the call below, although the marks table
            // of FeatureCircularPattern5.html names only features and axes
            // (SelectionMarks.CircularPatternAxis); bench case
            // the_circular_pattern_turns_around_a_straight_edge.
            //
            // DEVE ESSERE DRITTO, E SI CONTROLLA. Uno spigolo curvo non da'
            // errore: da' la direzione della tangente in un punto che nessuno
            // ha scelto, cioe' un pezzo che sembra giusto.
            // UNO SPIGOLO ANCHE SE ADESSO QUELL'UID NON LO E' PIU' (voce E19): se
            // il piano porta una firma con la lunghezza, quando l'ha letto era
            // uno spigolo, e si cerca come spigolo — non si ripiega sugli assi.
            DocumentState.EntitySignature signature;
            var signatureInPlane = ExpectedSignature(a, uid);
            bool wasAnEdge = signatureInPlane != null && signatureInPlane.ContainsKey("length_mm");
            if ((docState != null && docState.Signatures.TryGetValue(uid, out signature) && signature.Type == "EDGE") ||
                wasAnEdge)
            {
                signature = EdgeSignature(
                    a, docState, uid, "Lo spigolo «" + uid + "» non c'e' piu' in questo documento.",
                    out reason);
                if (signature == null) return false;

                var edge = signature.Entity as IEdge;
                bool straight = false;
                try
                {
                    var curva = edge == null ? null : edge.IGetCurve();
                    straight = curva != null && curva.IsLine();
                }
                catch { }
                if (!straight)
                {
                    reason =
                        "Lo spigolo «" + uid + "» non e' dritto: non da' una " +
                        "direzione. Per la ripetizione serve uno spigolo " +
                        "rettilineo, oppure un asse fatto con «create_axis».";
                    return false;
                }

                var entity = edge as IEntity;
                if (entity == null || !entity.SelectByMark(append, mark))
                {
                    reason = "Lo spigolo «" + uid + "» non si e' lasciato selezionare.";
                    return false;
                }
                _log("[agente] direzione presa dallo spigolo " + uid);
                return true;
            }

            // L'ASSE PER IDENTITA', NON SOLO PER NOME (voce E4). Se lo stato lo
            // conosce, la firma che il piano porta deve combaciare con la retta
            // di adesso, e lo si seleziona per oggetto invece che per nome.
            DocumentState.EntitySignature axisSignature;
            if (docState != null && docState.Signatures.TryGetValue(uid, out axisSignature) && axisSignature.Type == "AXIS")
            {
                string offsetAxis;
                if (!SignatureMatches(a, uid, axisSignature, out offsetAxis)) { reason = offsetAxis; return false; }
                var featAxis = axisSignature.Entity as IFeature;
                if (featAxis != null && featAxis.Select2(append, mark)) return true;
            }

            if (!model.Extension.SelectByID2(uid, "AXIS", 0, 0, 0, append, mark, null, 0))
            {
                reason =
                    "Non ho trovato l'asse «" + uid + "». Gli assi si fanno con " +
                    "«create_axis»; in alternativa indica uno spigolo dritto del " +
                    "pezzo, che nello stato ha un identificativo come «E1».";
                return false;
            }
            return true;
        }

        /// <summary>
        /// The feature to pattern or mirror, selected with the mark the caller
        /// passes (4 for the patterns, 1 for the mirror: see the parameter),
        /// by reference to a previous step or by identifier.
        ///
        /// In un metodo solo perche' ripetizione lineare, ripetizione
        /// circolare e specchio la nominano nello stesso identico modo, e tre
        /// copie della stessa logica divergono alla prima correzione.
        /// </summary>
        /// <param name="mark">
        /// LA MARCA NON E' LA STESSA PER TUTTE LE FEATURE, ed e' il genere di
        /// dettaglio che non da' errore: con la marca sbagliata la selezione
        /// c'e', la feature non la vede, e torna null senza dire niente.
        /// Le ripetizioni vogliono le lavorazioni con la marca 4 e l'asse con
        /// la 1; lo specchio le vuole con la 1 e il piano con la 2.
        /// </param>
        private bool SelectFeature(
            IModelDoc2 model, Dictionary<string, object> a, out string reason,
            int mark = SelectionMarks.LinearPatternFeatures)
        {
            reason = null;
            if (HasReference(a, "feature_ref"))
            {
                int number;
                var planStep = CitedStep(a, "feature_ref", out number, out reason);
                if (planStep == null)
                {
                    reason = reason ?? "Riferimento non risolto.";
                    return false;
                }
                if (planStep.Feature == null)
                {
                    reason = "Il passo " + number + " non ha lasciato una lavorazione.";
                    return false;
                }
                if (!planStep.Feature.Select2(true, mark))
                {
                    reason = "Non sono riuscito a selezionare la lavorazione del passo " +
                             number + ".";
                    return false;
                }
                _log("[agente] lavorazione presa dal passo " + number);
                return true;
            }

            string uid = Str(a, "feature_uid", "");
            if (string.IsNullOrEmpty(uid))
            {
                reason = "Non hai detto quale lavorazione ripetere o specchiare.";
                return false;
            }
            // E' ANCORA QUELLA? Vedi FeatureIsThatOne.
            if (!FeatureIsThatOne(model, uid, a, out reason)) return false;

            if (!model.Extension.SelectByID2(uid, "BODYFEATURE", 0, 0, 0, true, mark, null, 0))
            {
                reason = "Non ho trovato la lavorazione «" + uid + "».";
                return false;
            }
            return true;
        }

        /// <summary>
        /// La lavorazione che porta quel nome e' ancora quella che il piano
        /// aveva guardato?
        ///
        /// L'UID DI UNA LAVORAZIONE E' IL SUO NOME — non c'e' altro
        /// nell'albero — e un nome si rinomina. Chi rinomina «Taglio1» e poi
        /// chiama «Taglio1» un'altra lavorazione ha trasferito
        /// l'identificativo da una cosa a un'altra, e `SelectByID2` selezionera'
        /// obbedientemente quella nuova.
        ///
        /// `IFeature.GetID()` non si rinomina, e il piano se lo porta dietro
        /// nella firma. Qui si confrontano.
        ///
        /// SI CONTROLLA SOLO SE SI PUO'. Niente firma, o firma senza
        /// `feature_id`, vuol dire niente da confrontare: non si inventa un
        /// rifiuto. E' la stessa regola di `SignatureMatches`.
        /// </summary>
        private static bool FeatureIsThatOne(
            IModelDoc2 model, string uid,
            Dictionary<string, object> arguments, out string reason)
        {
            reason = null;
            var expectation = ExpectedSignature(arguments, uid);
            if (expectation == null) return true;
            object raw;
            if (!expectation.TryGetValue("feature_id", out raw) || raw == null) return true;
            int expected;
            try { expected = Convert.ToInt32(raw); } catch { return true; }

            var partDoc = model as IPartDoc;
            if (partDoc == null) return true;
            IFeature f = partDoc.IFirstFeature();
            while (f != null)
            {
                string itemName = null;
                try { itemName = f.Name; } catch { }
                if (string.Equals(itemName, uid, StringComparison.OrdinalIgnoreCase))
                {
                    int now;
                    try { now = f.GetID(); } catch { return true; }
                    if (now == expected) return true;
                    reason =
                        "La lavorazione «" + uid + "» non e' piu' quella che avevi " +
                        "guardato: qualcuno ha rinominato l'albero, e adesso quel nome " +
                        "sta su un'altra lavorazione. Rileggi lo stato del documento e " +
                        "nominala di nuovo.";
                    return false;
                }
                f = f.GetNextFeature() as IFeature;
            }
            // Non l'ho trovata scorrendo l'albero: lo dice SelectByID2 con il
            // suo messaggio, che e' piu' preciso di uno inventato qui.
            return true;
        }

        /// <summary>
        /// Il piano base indicato, SENZA selezionarlo.
        ///
        /// Non e' un doppione pigro di `SelectPlane`: quella seleziona
        /// sempre con Mark 0 — che va bene alle estrusioni, che i Mark non li
        /// leggono — e restituisce un bool. Qui serve la Feature, per poterla
        /// selezionare con il Mark che l'operazione vuole.
        /// </summary>
        private static IFeature FindBasePlane(IModelDoc2 model, string which)
        {
            if (!(model is IPartDoc)) return null;
            // THREE NAMES AND NOTHING ELSE: an unknown name is null, never the
            // front plane (a final `: 1` did that until the measure of 10
            // September 2026, «mirror on a plane that does not exist»).
            // BY GEOMETRY, NOT BY POSITION (Z-C21.5, scheme S19): the plane with
            // the canonical normal through the origin, References.BasePlane.
            // The callers keep their own refusal, which names the plane.
            return References.BasePlane(model, which, out _);
        }

        /// <summary>
        /// Rivoluzione: ruota un profilo attorno a un asse. E' il modo generale
        /// di fare un corpo a simmetria di rotazione — boccole, distanziali,
        /// alberi a gradini, flange, pulegge, volantini — quando la forma non
        /// e' un cilindro costante (quello e' «extrude_circle», piu' semplice).
        ///
        /// L'ASSE E' IL PUNTO DIFFICILE, E VALE LA PENA SPIEGARLO.
        /// `FeatureRevolve2` vuole, al momento della chiamata, sia il profilo
        /// sia l'asse. In un documento vuoto — il caso normale, perche' questa
        /// operazione fa il PRIMO corpo — non c'e' nessun asse di riferimento
        /// da citare e nessuno spigolo da usare come tale.
        ///
        /// La strada scelta: l'asse si disegna come CENTERLINE dentro lo stesso
        /// schizzo del profilo (`ISketchManager.CreateCenterLine`, verificata
        /// per riflessione: stessa firma di CreateLine), e poi si seleziona lo
        /// schizzo intero, come gia' si fa per estrusioni e tagli.
        ///
        /// THE AXIS IS THE ONLY CENTERLINE OF THE SKETCH, AND THAT IS A MEASURE,
        /// NOT UseAutoSelect. The guide asks for two selections
        /// (IFeatureManager~FeatureRevolve2.html, Remarks: «the sketch to
        /// revolve, using Mark = 0» and «the axis of revolution, using Mark = 4
        /// or 16»); only the sketch is selected here, and SOLIDWORKS takes its
        /// single centerline as the axis. Measured by the revolve cases of the
        /// bench (Bench/cases/revolve): four with their volume to half a
        /// cubic millimetre and one body (cylinder_d40_long_50 62831.853 mm3,
        /// half_cylinder_180, hollow_bushing, stepped_shaft) and the refusal
        /// limit_profile_straddling_the_axis; a wrong axis would not give those
        /// volumes (audit finding L2-1, not real). UseAutoSelect is about
        /// BODIES, not the axis: «True to automatically select all bodies and
        /// have the feature affect those bodies» (same page). Adding the
        /// centerline with mark 4 would follow the guide to the letter, and
        /// needs those cases run before and after.
        ///
        /// FIRMA VERIFICATA sull'interop 29.5.1.1, usata COSI' e non
        /// «aggiornata» a un numero piu' alto:
        ///   IFeatureManager.FeatureRevolve2(SingleDir, IsSolid, IsThin,
        ///       IsCut, ReverseDir, BothDirectionUpToSameEntity, Dir1Type,
        ///       Dir2Type, Dir1Angle, Dir2Angle, OffsetReverse1,
        ///       OffsetReverse2, OffsetDistance1, OffsetDistance2, ThinType,
        ///       ThinThickness1, ThinThickness2, Merge, UseFeatScope,
        ///       UseAutoSelect) -> Feature
        ///
        /// SOLO IL CORPO PIENO: IsThin resta false. Per una parete sottile si
        /// disegna il vuoto come secondo contorno del profilo.
        ///
        /// E SI CONTA IL RISULTATO. Un profilo che ATTRAVERSA l'asse non da'
        /// errore: da' un solido che si autointerseca. E' la stessa famiglia
        /// del foro da 50,8 mm che sembrava giusto. Partendo da un documento
        /// senza corpi — che e' la precondizione — una rivoluzione riuscita ne
        /// lascia esattamente UNO: zero o piu' di uno vuol dire che il profilo
        /// e l'asse non erano quello che sembravano.
        /// </summary>
        private Outcome Revolve(IModelDoc2 model, Dictionary<string, object> a)
        {
            var contours = Contours(a, out string reason, out string _);
            if (contours == null) return Failed(reason);

            object rawAxis;
            if (!a.TryGetValue("axis", out rawAxis) || rawAxis == null)
                return Failed("Manca l'asse di rivoluzione.");
            var axisPoints = Points(rawAxis, out reason);
            if (axisPoints == null) return Failed("Asse di rivoluzione: " + reason);
            if (axisPoints.Count != 2)
                return Failed(
                    "L'asse di rivoluzione va dato come due punti [x_mm, y_mm], non " +
                    axisPoints.Count + ".");
            double dx = axisPoints[1][0] - axisPoints[0][0];
            double dy = axisPoints[1][1] - axisPoints[0][1];
            if (Math.Sqrt(dx * dx + dy * dy) < 1e-6)
                return Failed("I due punti dell'asse coincidono: non definiscono una retta.");

            double angle = Num(a, "angle_deg", 360);
            string plane = Str(a, "plane", "front");
            // Before the sketch: it is selected, and locked after the
            // revolve, as the one sketch born since (scheme S7).
            var sketchNamesBefore = FeatureNamesNow(model);
            if (!SelectPlane(model, plane))
                return Failed($"Non ho trovato il piano «{plane}» nell'albero.");

            var sketches = model.SketchManager;
            bool addToDbBefore = false, displayBefore = false;
            try { addToDbBefore = sketches.AddToDB; displayBefore = sketches.DisplayWhenAdded; } catch { }

            int segments = 0;
            bool createdAxis = false;
            try
            {
                sketches.InsertSketch(true);
                try { sketches.AddToDB = true; sketches.DisplayWhenAdded = false; } catch { }

                foreach (var contour in contours) segments += DrawContour(sketches, contour);

                var axisSegment = sketches.CreateCenterLine(
                    Units.M(axisPoints[0][0]), Units.M(axisPoints[0][1]), 0,
                    Units.M(axisPoints[1][0]), Units.M(axisPoints[1][1]), 0) as ISketchSegment;
                // Ridondante se, come atteso, CreateCenterLine nasce gia' di
                // costruzione: si scrive lo stesso perche' non costa niente ed
                // e' la differenza fra un asse riconosciuto e una linea
                // qualunque, se quell'attesa fosse sbagliata.
                try { if (axisSegment != null) axisSegment.ConstructionGeometry = true; } catch { }
                createdAxis = axisSegment != null;
            }
            finally
            {
                try { sketches.AddToDB = addToDbBefore; sketches.DisplayWhenAdded = displayBefore; } catch { }
            }
            sketches.InsertSketch(false);

            if (!createdAxis)
                return Failed("Non sono riuscito a disegnare l'asse di rivoluzione." + WithoutSketch(model));
            if (!SelectJustCreatedSketch(model, sketchNamesBefore, out string selError))
                return Failed(selError + WithoutSketch(model));

            IFeature feature;
            try
            {
                feature = model.FeatureManager.FeatureRevolve2(
                    SingleDir: true,
                    IsSolid: true,
                    IsThin: false,
                    IsCut: false,
                    ReverseDir: false,
                    BothDirectionUpToSameEntity: false,
                    Dir1Type: (int)swEndConditions_e.swEndCondBlind,
                    Dir2Type: (int)swEndConditions_e.swEndCondBlind,
                    Dir1Angle: Units.Rad(angle),
                    Dir2Angle: 0,
                    OffsetReverse1: false,
                    OffsetReverse2: false,
                    OffsetDistance1: 0,
                    OffsetDistance2: 0,
                    ThinType: 0,
                    ThinThickness1: 0,
                    ThinThickness2: 0,
                    Merge: true,
                    UseFeatScope: true,
                    UseAutoSelect: true) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("La rivoluzione non e' riuscita: " + ex.Message + WithoutSketch(model));
            }
            if (feature == null)
                return Failed(
                    "La rivoluzione non e' stata creata. Di solito e' l'asse: deve " +
                    "stare sul piano del profilo, e il profilo non deve " +
                    "attraversarlo." + WithoutSketch(model));

            int nBodies = CountSolidBodies(model);
            if (nBodies >= 0 && nBodies != 1)
                return Failed(
                    "La rivoluzione ha creato la feature ma il documento ha " +
                    nBodies + " corpi solidi invece di uno: molto probabilmente " +
                    "il profilo attraversa l'asse. Non ti confermo un pezzo " +
                    "diverso da quello che ti ho mostrato: ritiralo — " +
                    Sentences.HowToWithdrawIt + ".");

            string noteBlock;
            int fixedRelations;
            bool locked = LockSketch(model, sketchNamesBefore, out noteBlock, out fixedRelations);
            var revolveMeasures = new Dictionary<string, object>
            {
                ["sketch_locked"] = locked,
                ["fixed_relations"] = fixedRelations,
                ["contours"] = contours.Count,
                ["segments"] = segments,
                ["angle_deg"] = angle,
            };
            if (noteBlock != null) revolveMeasures["sketch_lock_note"] = noteBlock;
            return Succeeded(feature, revolveMeasures, References.PlaneNormal(plane));
        }

        /// <summary>
        /// Il guscio: svuota un corpo pieno lasciando una parete di spessore
        /// costante. Le facce indicate vengono TOLTE, cioe' restano aperte.
        ///
        /// DUE COSE DA SAPERE PRIMA DI TOCCARE QUESTO METODO, tutte e due
        /// verificate per riflessione sull'interop 29.5.1.1:
        ///
        ///   IModelDoc2.InsertFeatureShell(Double Thickness, Boolean Outward)
        ///
        ///   1. sta su IModelDoc2, NON su IFeatureManager dove sta tutto il
        ///      resto di questo file. E' una delle sette API che nel RAG
        ///      «sembravano giuste e non lo erano»;
        ///   2. ritorna VOID. Non c'e' nessun InsertFeatureShell2 che
        ///      restituisca una Feature.
        ///
        /// Non prende le facce come parametro: lavora su quello che e' GIA'
        /// SELEZIONATO, come il raccordo e lo smusso fanno con gli spigoli.
        ///
        /// SICCOME NON TORNA UNA FEATURE, la si va a cercare in coda
        /// all'albero — e siccome «l'albero e' cresciuto» non basta a dire che
        /// e' cresciuto BENE, si misura il volume: un guscio TOGLIE materiale,
        /// e ne toglie meno del corpo intero. Il 9 settembre 2026 abbiamo
        /// misurato che in questo stesso agente una chiamata puo' riuscire e
        /// produrre geometria sbagliata senza dare errore (HoleWizard5, foro
        /// da due pollici). Qui il controllo costa due letture.
        /// </summary>
        private Outcome Shell(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double thickness = Num(a, "thickness_mm");
            bool outwardFacing = Bool(a, "outward", false);
            bool allClosed = Bool(a, "fully_closed", false);
            var uid = StringList(a, "faces_uid");

            // LE FACCE POSSONO VENIRE DA UN PASSO PRECEDENTE (voce E15).
            //
            // Fino alla 0.30.0 `shell` era l'UNICA operazione su faccia che
            // non accettava un riferimento a un passo: voleva `faces_uid`, che
            // nascono solo da una lettura. Quindi «fai un blocco e poi svuotalo»
            // non si poteva scrivere in un piano solo, mentre «fai un blocco e
            // poi foralo» si' — ed e' proprio la sequenza del contenitore per
            // l'antenna: blocco, guscio, alette.
            //
            // UN ELENCO DI RIFERIMENTI SINGOLI, non un genere plurale. Il
            // vocabolario di `References` risolve una faccia per volta, e
            // nessuna operazione del catalogo usa oggi un genere che ne dia
            // piu' d'una: inventarne uno qui vorrebbe dire aprire superficie
            // nuova per un problema che si risolve ripetendo l'elemento.
            List<IFace2> fromSteps = null;
            if (uid.Count == 0 && HasList(a, "faces_ref"))
            {
                if (!FacesFromReferences(a, "faces_ref", out fromSteps, out string why))
                    return Failed(why);
            }
            else if (uid.Count > 0 && HasList(a, "faces_ref"))
            {
                return Failed(
                    "Il guscio ha sia «faces_uid» sia «faces_ref»: uno nomina facce " +
                    "che ci sono gia', l'altro facce che le crea un passo di questo " +
                    "piano. Scegline uno.");
            }

            if (uid.Count == 0 && fromSteps == null && !allClosed)
                return Failed(
                    "Nessuna faccia indicata per il guscio. Se la faccia la crea un " +
                    "passo precedente di questo piano, usa «faces_ref»: " +
                    "[{\"step\": 1, \"kind\": \"top_face\"}]. Se serve davvero " +
                    "un corpo cavo completamente chiuso, senza nessuna apertura, metti " +
                    "«fully_closed: true» — di solito pero' e' una faccia dimenticata " +
                    "nella richiesta.");

            model.ClearSelection2(true);

            if (fromSteps != null)
            {
                // Le facce arrivano FRESCHE dal passo appena eseguito, non da
                // una lettura precedente: non c'e' nessuna firma da
                // confrontare, ed e' lo stesso motivo per cui il ramo
                // `face_ref` di `ResolveFace` salta quel controllo.
                // TUTTE, NON ALMENO UNA. La prima scrittura contava quante si
                // erano lasciate selezionare e si fermava solo a zero: con tre
                // riferimenti risolti e due facce selezionate andava avanti, e
                // il guscio usciva aperto su DUE facce invece che su tre —
                // «succeeded: true», volume calato, tutti i controlli a valle
                // soddisfatti, e un contenitore chiuso su un lato che doveva
                // essere aperto.
                //
                // La regola di questa voce era gia' scritta e valeva sulla
                // RISOLUZIONE: «se uno solo non si risolve, fallisce tutto».
                // Valeva su meta' del percorso. Il ramo degli uid, quindici
                // righe piu' giu', rifiuta alla PRIMA faccia che non si
                // seleziona: una riparazione che copre un ramo e ne lascia
                // scoperto il gemello.
                int takenOnes = 0;
                foreach (var f in fromSteps)
                {
                    if ((f as IEntity)?.Select4(true, null) == true) { takenOnes++; continue; }
                    return Failed(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "La faccia n. {0} di «faces_ref» non si e' lasciata selezionare: " +
                        "{1} su {2} erano pronte. Un guscio aperto su meno facce di quelle " +
                        "chieste non e' quasi giusto, e' un pezzo diverso.{3}",
                        takenOnes + 1, takenOnes, fromSteps.Count, RemainsInPart));
                }
                _log("[agente] " + takenOnes + " facce prese dai passi precedenti");
            }
            else if (uid.Count == 0)
            {
                // Guscio chiuso: si seleziona il corpo. Con piu' corpi non si
                // sceglie: si chiede. Un guscio applicato al corpo sbagliato
                // e' esattamente il pezzo che sembra giusto.
                var partDoc = model as IPartDoc;
                var bodies = partDoc?.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null || bodies.Length == 0)
                    return Failed("Non ho trovato nessun corpo solido da rendere cavo.");
                if (bodies.Length > 1)
                    return Failed(
                        "In questo documento ci sono " + bodies.Length + " corpi " +
                        "solidi: un guscio chiuso senza facce indicate lavorerebbe " +
                        "su uno scelto a caso. Indica le facce da togliere.");
                // UN CORPO NON E' UNA IEntity, e questo ramo non l'ha mai
                // saputo. `corpi[0] as IEntity` torna **sempre** null
                // nell'interop, quindi «fully_closed: true» rispondeva «Non
                // sono riuscito a selezionare il corpo solido» su QUALUNQUE
                // pezzo — non era un caso limite, era rotto sempre.
                //
                // La cosa che fa male: la lezione era gia' scritta, in questo
                // stesso file, quattromila righe piu' su. Il commento di
                // `MoveBodies` la racconta per esteso — misurata dentro
                // SOLIDWORKS il 10 settembre 2026, con cinque prove che
                // dicevano «i corpi indicati non si sono lasciati selezionare»
                // — e li' e' gia' riparata con `IBody2.Select2`. Qui no.
                //
                // Una lezione scritta e non applicata e' una lezione che il
                // codice non ha imparato: il commento la ricorda a chi legge
                // QUELLE righe, e chi scrive altrove non passa di li'.
                var body = bodies[0] as IBody2;
                bool taken = body != null && body.Select2(false, null);
                if (!taken)
                {
                    // La vecchia strada resta come ripiego, non come prima
                    // scelta: se un domani l'interop cambiasse idea, questo
                    // ramo continua a funzionare invece di rompersi.
                    var asEntity = bodies[0] as IEntity;
                    taken = asEntity != null && asEntity.Select4(false, null);
                }
                if (!taken)
                    return Failed("Non sono riuscito a selezionare il corpo solido.");
            }
            else
            {
                // Le facce si selezionano ACCUMULANDO, come fa il fazzoletto:
                // `SelectFace` seleziona con append=false ed e' pensato
                // per una faccia sola prima di uno schizzo. Qui servono tutte
                // insieme, e usare quell'aiutante lascerebbe selezionata solo
                // l'ultima — un guscio aperto da un lato invece che da tre,
                // senza nessun errore.
                foreach (var u in uid)
                {
                    DocumentState.EntitySignature signature;
                    if (docState == null || !docState.Signatures.TryGetValue(u, out signature) ||
                        signature.Type != "FACE")
                        return Failed("La faccia «" + u + "» non c'e' piu' in questo documento.");
                    string offset;
                    if (!SignatureMatches(a, u, signature, out offset)) return Failed(offset);
                    var ent = signature.Entity as IEntity;
                    if (ent == null || !ent.Select4(true, null))
                        return Failed("La faccia «" + u + "» non si e' lasciata selezionare.");
                }
            }

            int featureBefore = CountFeature(model);
            int bodiesBefore = CountSolidBodies(model);
            double? volumeBefore = VolumeMm3(model);
            var namesBefore = FeatureNamesNow(model);
            if (bodiesBefore < 0 || featureBefore < 0 || namesBefore == null) return Failed(VolumeNotReadBefore("fare il guscio").Replace("il volume del pezzo", "i corpi del pezzo"));

            try
            {
                model.InsertFeatureShell(Units.M(thickness), outwardFacing);
            }
            catch (Exception ex)
            {
                return Failed("Il guscio non e' riuscito: " + ex.Message);
            }

            int featureAfter = CountFeature(model);
            if (featureAfter < 0)
                return Failed(
                    "Ho chiesto il guscio, ma poi non riesco a ricontare l'albero: non so dirti " +
                    "se e' nato. Guarda il pezzo prima di andare avanti.");
            if (featureAfter <= featureBefore)
                return Failed(
                    "Il guscio non e' stato creato. Di solito e' lo spessore: " +
                    "piu' grande di meta' del corpo non ci sta, e vicino a uno " +
                    "spigolo lascerebbe una parete di spessore negativo.");

            int bodiesAfter = CountSolidBodies(model);
            if (bodiesAfter >= 0 && bodiesAfter != bodiesBefore)
                return Failed(
                    "Il guscio ha cambiato il numero di corpi solidi (da " +
                    bodiesBefore + " a " + bodiesAfter + "): non e' il corpo svuotato " +
                    "che ci si aspettava. Non te lo lascio passare per buono.");

            // IL GUSCIO TOGLIE MATERIALE. Se il volume non e' calato, quella
            // feature non e' un guscio, qualunque cosa dica l'albero.
            double? volumeAfter = VolumeMm3(model);
            if (volumeBefore > 0 && volumeAfter > 0 && volumeAfter >= volumeBefore - 0.001)
                return Failed(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Il guscio non ha tolto niente: il volume era {0:0.#} mm3 ed " +
                    "e' {1:0.#} mm3. Non e' il pezzo che ti ho promesso.",
                    volumeBefore, volumeAfter));

            // THE SHELL IS THE ONE FEATURE BORN SINCE THE CALL (FeatureBorn,
            // scheme S7, Z-C21.5), not the last feature of the tree: on a
            // sheet-metal part that is the flat pattern (finding M9).
            int featuresBorn;
            var feature = FeatureBorn(model, namesBefore, null, out featuresBorn);
            if (feature == null && featuresBorn > 1)
                return Failed(
                    "The shell seems made, but " + featuresBorn + " features were born in the tree and I do not " +
                    "pick one of them for you. Look at the part before going on.");
            if (feature == null)
                return Failed("Il guscio sembra creato ma non ritrovo la feature nell'albero.");

            var measurements = new Dictionary<string, object>
            {
                ["thickness_mm"] = thickness,
                ["facing"] = outwardFacing ? "outer" : "inner",
                // NON `uid.Count`: le facce possono arrivare anche da
                // `faces_ref`, e contare solo gli uid faceva dire «faces_removed:
                // 0» a un guscio che una faccia l'aveva tolta davvero. Una
                // misura che dice zero quando il numero e' uno e' il difetto
                // che questo progetto insegue, in miniatura.
                ["faces_removed"] = fromSteps != null ? fromSteps.Count : uid.Count,
            };
            if (volumeBefore > 0 && volumeAfter > 0)
                measurements["material_removed_mm3"] = Math.Round(volumeBefore.Value - volumeAfter.Value, 1);
            else
                measurements["volume_not_reread"] = NoteVolumeNotReread;
            return Succeeded(feature, measurements);
        }

        /// <summary>
        /// Quante feature ci sono nell'albero.
        ///
        /// Serve alle operazioni la cui API ritorna void — il guscio, per
        /// adesso — per sapere se hanno creato qualcosa: l'unico segno di vita
        /// e' che l'albero e' cresciuto di una posizione.
        ///
        /// **-1 quando non si e' potuto leggere, o se non e' una parte** (voce
        /// Z-C8): con la forma `int n = 0; try { ... } catch { }` un errore a
        /// meta' albero dava un numero piu' basso, e «l'albero non e'
        /// cresciuto» diventava un rifiuto falso.
        /// </summary>
        private static int CountFeature(IModelDoc2 model)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return -1;
            int n = 0;
            try
            {
                var f = partDoc.IFirstFeature();
                while (f != null) { n++; f = f.IGetNextFeature(); }
            }
            catch { return -1; }
            return n;
        }

        /// <summary>
        /// Quanti piani di riferimento ha il pezzo, adesso, o **-1 se non si e'
        /// potuto leggere** (voce Z-C8): una feature che non dice il suo tipo,
        /// o un albero che si interrompe, lasciavano un numero piu' basso.
        /// </summary>
        private static int CountPlanes(IModelDoc2 model)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return -1;
            int n = 0;
            try
            {
                IFeature f = partDoc.IFirstFeature();
                while (f != null)
                {
                    if (f.GetTypeName2() == "RefPlane") n++;
                    f = (IFeature)f.GetNextFeature();
                }
            }
            catch { return -1; }
            return n;
        }

        /// <summary>Quanti piani ha aggiunto il passo, o null se un conteggio non si legge.</summary>
        private static object AddedPlanes(IModelDoc2 model, int before)
        {
            int now = CountPlanes(model);
            return before >= 0 && now >= 0 ? (object)(now - before) : null;
        }

        // ─────────────────────────────────────────────────────────────────
        // The feature a call has just added (scheme S7, Z-C21.5)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The names of the top-level features of a part, now: the «before» of
        /// <see cref="FeatureBorn"/>, taken right before the call that adds a
        /// feature. Null when the document is not a part or the tree cannot be
        /// read to the end (a partial set would make an old feature look born).
        /// </summary>
        private static HashSet<string> FeatureNamesNow(IModelDoc2 model)
        {
            List<IFeature> features;
            List<string> names, types;
            if (!ReadFeatureTree(model, out features, out names, out types)) return null;
            return new HashSet<string>(names, StringComparer.Ordinal);
        }

        /// <summary>
        /// THE FEATURE A CALL HAS JUST ADDED, FOUND BY THE DIFFERENCE OF THE
        /// TREE (scheme S7: findings L8-6, M9 and N13; Z-C21.5). The one feature
        /// of type <paramref name="type"/> (any type when null) whose name was
        /// not in <paramref name="before"/>. Null when none was born, when more
        /// than one was, or when the tree cannot be read; <paramref name="born"/>
        /// says how many of that type were born (-1: not read).
        ///
        /// WHY NOT THE LAST FEATURE OF THE TREE. On a sheet-metal part the tail
        /// of the tree is the flat pattern (the FlatPattern feature at the bottom
        /// of the tree, OperazioniLamiera), and SOLIDWORKS inserts a new feature
        /// before it. The last feature is then the flat pattern: create_axis
        /// refused an axis it had made and left it in the tree,
        /// plane_three_points reported the flat pattern as the plane it had
        /// made, and the orphan sketch of a failed step was never taken away.
        /// IModelDoc2~IFeatureByPositionReverse.html: «SOLIDWORKS does not
        /// guarantee the name or positioning of default features». A feature
        /// name is unique in a part, so the names that were not there before are
        /// what the call added, wherever in the tree it went.
        /// </summary>
        private static IFeature FeatureBorn(IModelDoc2 model, HashSet<string> before, string type, out int born)
        {
            born = -1;
            List<IFeature> features;
            List<string> names, types;
            if (before == null || !ReadFeatureTree(model, out features, out names, out types)) return null;
            int index = BornFeatureIndex(names, types, before, type, out born);
            return index >= 0 ? features[index] : null;
        }

        /// <summary>
        /// The top-level features of a part in tree order, with their names and
        /// types (an unreadable type is ""). False when the document is not a
        /// part or the walk breaks: then nothing is returned rather than half a
        /// tree.
        /// </summary>
        private static bool ReadFeatureTree(
            IModelDoc2 model, out List<IFeature> features, out List<string> names, out List<string> types)
        {
            var inTree = new List<IFeature>();
            var theirNames = new List<string>();
            var theirTypes = new List<string>();
            features = inTree;
            names = theirNames;
            types = theirTypes;
            var partDoc = model as IPartDoc;
            if (partDoc == null) return false;
            try
            {
                IFeature f = partDoc.IFirstFeature();
                while (f != null)
                {
                    string kind = "";
                    try { kind = f.GetTypeName2() ?? ""; } catch { }
                    inTree.Add(f);
                    theirNames.Add(f.Name);
                    theirTypes.Add(kind);
                    f = f.IGetNextFeature();
                }
            }
            catch { return false; }
            return true;
        }

        /// <summary>
        /// The positions, in tree order, of the features whose name is not in
        /// <paramref name="before"/>: what was born since. Pure, like
        /// <see cref="BornFeatureIndex"/>.
        /// </summary>
        internal static List<int> BornFeatureIndices(IList<string> names, ICollection<string> before)
        {
            var born = new List<int>();
            if (names == null || before == null) return born;
            for (int i = 0; i < names.Count; i++)
                if (names[i] != null && !before.Contains(names[i])) born.Add(i);
            return born;
        }

        /// <summary>
        /// THE DECISION OF <see cref="FeatureBorn"/>, WITH NO INTEROP: the
        /// position of the ONE born feature of that type (any type when null),
        /// or -1 when there is none or more than one. Two born features of the
        /// same type are two candidates, and taking the later would be «the last
        /// in the tree» again. Pure, so that Oce.Solid.Agent.ContractTests checks it without
        /// SOLIDWORKS (Zc215IdentityContract).
        /// </summary>
        internal static int BornFeatureIndex(
            IList<string> names, IList<string> types, ICollection<string> before, string type, out int born)
        {
            born = -1;
            if (names == null || before == null) return -1;
            int found = -1;
            born = 0;
            foreach (int i in BornFeatureIndices(names, before))
            {
                if (type != null && (types == null || i >= types.Count || types[i] != type)) continue;
                born++;
                found = i;
            }
            return born == 1 ? found : -1;
        }

        /// <summary>
        /// Quante facce hanno in tutto i corpi solidi.
        ///
        /// Serve alle operazioni che COPIANO — specchio e ripetizioni —
        /// perche' il solo volume non basta a dire che e' successo
        /// qualcosa: specchiando un taglio simmetrico rispetto al piano
        /// scelto il volume cambia, ma ci sono casi in cui cambia poco e
        /// casi in cui la copia si fonde con cio' che c'era. Le facce
        /// invece si contano, e una copia in piu' ne porta sempre.
        ///
        /// **-1 quando non si e' potuto leggere** (voce Z-C8): aveva la forma
        /// che lo scanner non vedeva, `int n = 0; try { ... } catch { }`, e due
        /// letture fallite davano «stesse 0 facce» — cioe' il pezzo identico.
        /// </summary>
        private static int CountFaces(IModelDoc2 model)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return -1;
            int n = 0;
            try
            {
                var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null) return 0;
                foreach (var c in bodies)
                {
                    var faces = (c as IBody2)?.GetFaces() as object[];
                    if (faces != null) n += faces.Length;
                }
            }
            catch { return -1; }
            return n;
        }

        /// <summary>
        /// Com'e' il pezzo adesso: volume, corpi, facce. E' la fotografia
        /// da confrontare prima e dopo un'operazione che copia.
        /// </summary>
        private static void Snapshot(
            IModelDoc2 model, out double? volume, out int bodies, out int faces)
        {
            volume = VolumeMm3(model);
            bodies = CountSolidBodies(model);
            faces = CountFaces(model);
        }

        /// <summary>
        /// Confronta la fotografia prima e dopo, e riempie le misure.
        /// Ritorna il motivo del rifiuto se NON E' SUCCESSO NIENTE.
        ///
        /// PERCHE' IL CRITERIO E' «NIENTE E' CAMBIATO» E NON QUALCOSA DI
        /// PIU' PRECISO. Di una copia non si sa in anticipo quanto volume
        /// aggiunga o tolga: dipende da quanto la copia si sovrappone a cio'
        /// che c'era. Pretendere un numero esatto vorrebbe dire rifiutare
        /// casi legittimi — e rifiutare cio' che e' soltanto insolito e' un
        /// difetto peggiore di non controllare.
        ///
        /// «Niente e' cambiato», invece, e' inequivocabile: SolidWorks ha
        /// detto di aver creato la feature e il pezzo e' identico. Quello e'
        /// il falso successo, ed e' l'unica cosa che questo controllo
        /// rifiuta.
        /// </summary>
        private static string CompareCopy(
            IModelDoc2 model,
            double? volumeBefore, int bodiesBefore, int facesBefore,
            Dictionary<string, object> measurements,
            string whatItWas)
        {
            double? volumeAfter; int bodiesAfter, facesAfter;
            Snapshot(model, out volumeAfter, out bodiesAfter, out facesAfter);

            measurements["volume_before_mm3"] = NumberOrNull(volumeBefore, 2);
            measurements["volume_after_mm3"] = NumberOrNull(volumeAfter, 2);
            measurements["volume_changed_mm3"] = NumberOrNull(volumeAfter - volumeBefore, 2);
            measurements["faces_before"] = NumberOrNull(facesBefore);
            measurements["faces_after"] = NumberOrNull(facesAfter);
            measurements["bodies"] = NumberOrNull(bodiesAfter);

            // «IDENTICO» SI DICE SOLO DI COSE LETTE (voce Z-C8): con due letture
            // fallite, zero prima e zero dopo erano «stesso volume, stesse 0
            // facce», e una copia riuscita veniva rifiutata come inesistente.
            bool everythingRead = volumeBefore.HasValue && volumeAfter.HasValue &&
                              facesBefore >= 0 && facesAfter >= 0 && bodiesBefore >= 0 && bodiesAfter >= 0;
            if (!everythingRead)
            {
                measurements["volume_not_reread"] = NoteVolumeNotReread;
                return null;
            }
            bool unchangedVolume = Math.Abs(volumeAfter.Value - volumeBefore.Value) < 0.01;
            if (unchangedVolume && facesAfter == facesBefore && bodiesAfter == bodiesBefore)
                return
                    whatItWas + " dice di essere riuscito, ma il pezzo e' identico a prima: " +
                    "stesso volume (" + Math.Round(volumeAfter.Value, 2) + " mm3), stesse " +
                    facesAfter + " facce, stessi " + bodiesAfter + " corpi. Non e' stato copiato niente. " +
                    "Di solito vuol dire che la lavorazione indicata non e' quella che credi, " +
                    "oppure che la copia sarebbe caduta esattamente dove c'e' gia' l'originale.";

            return null;
        }

        /// <summary>
        /// Quante facce POSSIEDE una lavorazione.
        ///
        /// Non e' il conto delle facce del pezzo: GetFaces() su una feature
        /// da' solo quelle che quella feature ha creato. E' la differenza
        /// che permette di contare le copie senza che il materiale gia'
        /// presente c'entri niente.
        ///
        /// **-1 quando non si e' potuto leggere** (voce Z-C8). Con lo zero, una
        /// lettura fallita diventava «la lavorazione non ha piu' nessuna
        /// faccia», e `SuppressInstance` rimetteva a posto una modifica giusta.
        /// ZERO-E-ZERO: `GetFaces` null su una feature viva vuol dire che non
        /// ne possiede (un piano, uno schizzo): quello zero e' vero.
        /// </summary>
        private static int FeatureFaces(IFeature f)
        {
            if (f == null) return -1;
            try
            {
                var faces = f.GetFaces() as object[];
                return faces == null ? 0 : faces.Length;
            }
            catch { return -1; }
        }

        /// <summary>
        /// La lavorazione appena selezionata con quel contrassegno.
        ///
        /// Serve PRIMA di creare la ripetizione, perche' costruirla
        /// azzera la selezione. Chi la chiama deve accettare un null: se
        /// il seme non si ritrova non si conta niente, e non si rifiuta.
        /// </summary>
        private static IFeature SelectedSeed(IModelDoc2 model, int mark)
        {
            try
            {
                var sel = model.SelectionManager as ISelectionMgr;
                if (sel == null) return null;
                int howMany = sel.GetSelectedObjectCount2(-1);
                for (int i = 1; i <= howMany; i++)
                    if (sel.GetSelectedObjectMark(i) == mark)
                        return sel.GetSelectedObject6(i, -1) as IFeature;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// LE COPIE CHE NON CI SONO.
        ///
        /// MISURATO il 14 settembre 2026 su due pezzi gemelli: blocco
        /// 200x100x20, foro Ø10 a x=-60, ripetizione di 4 lungo lo spigolo E1.
        ///
        ///   verso preso dallo spigolo -> la ripetizione possiede 1 faccia
        ///   con «flip1»         -> la ripetizione possiede 3 facce
        ///
        /// Il seme ne possiede 1. Quindi UNA copia contro TRE, a parita' di
        /// richiesta. E IN TUTT'E DUE I CASI SOLIDWORKS HA TACIUTO:
        /// GetErrorCode2 ha dato 0, IsWarning false, SkippedItemArray null.
        /// Due istanze cadevano fuori dal materiale, lui ha costruito quello
        /// che poteva e ha risposto «riuscito». Chi chiedeva quattro fori ne
        /// otteneva due, e nessuno glielo diceva: e' esattamente il difetto
        /// che non da' errore.
        ///
        /// QUANDO NON SI SA, NON SI RIFIUTA. Se il seme non si ritrova, se
        /// una delle due non ha facce, o se il conto non e' divisibile, il
        /// numero non si scrive e l'operazione passa. Copie che si fondono
        /// fra loro lasciano un resto, ed e' un caso legittimo: rifiutare
        /// cio' che e' soltanto insolito e' un difetto peggiore che non
        /// controllare affatto.
        ///
        /// Non si rifiuta nemmeno se le copie sono PIU' di quelle chieste:
        /// vorrebbe dire che questo modo di contare e' sbagliato, e su un
        /// conto che si e' appena rivelato sbagliato non si nega niente.
        /// </summary>
        private static string MissingCopies(
            Dictionary<string, object> measurements, IFeature repetition, IFeature seed,
            int requestedCopies, string whatItWas, string howToRepair)
        {
            if (requestedCopies < 1) return null;

            int patternFaces = FeatureFaces(repetition);
            int seedFaceList = FeatureFaces(seed);
            if (patternFaces <= 0 || seedFaceList <= 0) return null;
            if (patternFaces % seedFaceList != 0) return null;

            int done = patternFaces / seedFaceList;
            measurements["copies_requested"] = requestedCopies;
            measurements["copies_done"] = done;
            if (done >= requestedCopies) return null;

            return
                whatItWas + " dice di essere riuscita, ma le copie non ci sono tutte: " +
                "ne ho contate " + done + " delle " + requestedCopies + " chieste. " +
                "Le conto dalle facce che la ripetizione possiede (" + patternFaces +
                ") divise per quelle della lavorazione ripetuta (" + seedFaceList + "). " +
                "SOLIDWORKS qui non segnala niente: costruisce quello che puo' e " +
                "dice riuscito. Di solito le copie mancanti cadono fuori dal " +
                "materiale" + (done > 1 ? ", oppure si sono fuse fra loro" : "") + ". " +
                howToRepair +
                " La ripetizione e' rimasta nel pezzo: ritirala — " +
                Sentences.HowToWithdrawIt + ".";
        }

        /// <summary>
        /// Quante facce possiede ogni lavorazione dell'albero, per nome.
        ///
        /// Serve a vedere se una modifica di quota ha cambiato la NATURA di
        /// una lavorazione, non solo le sue misure. Il conto delle facce del
        /// pezzo intero non basta: una tasca che diventa passante toglie una
        /// faccia al pezzo, ma cosi' fanno anche altre dieci cose.
        ///
        /// C'e' un tetto, e non e' prudenza generica: l'albero costa circa
        /// 200 ms a feature (misurato), quindi su un pezzo con centinaia di
        /// lavorazioni questa lettura da sola durerebbe piu' dell'operazione.
        /// Oltre il tetto si smette e si riporta quello che si e' visto.
        /// </summary>
        private static Dictionary<string, int> FacesPerFeature(IModelDoc2 model)
        {
            var outside = new Dictionary<string, int>();
            try
            {
                var f = model.FirstFeature() as IFeature;
                int views = 0;
                while (f != null && views < 200)
                {
                    // -1 SI TIENE (voce Z-C8): una lavorazione che non si e'
                    // lasciata contare non e' una lavorazione «senza facce», e
                    // toglierla dall'elenco la farebbe risultare sparita.
                    int howMany = FeatureFaces(f);
                    if (howMany != 0)
                    {
                        views++;
                        try { outside[f.Name] = howMany; } catch { }
                    }
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// Cosa e' cambiato nell'albero, in una frase per chi legge.
        /// Null se non e' cambiato niente di topologico.
        ///
        /// SI RIPORTA, NON SI RIFIUTA. Assottigliare una piastra finche' una
        /// tasca diventa passante puo' essere esattamente cio' che si vuole:
        /// rifiutarlo sarebbe rifiutare cio' che e' soltanto insolito. Ma una
        /// sede cieca e un passaggio sono due pezzi diversi, e chi ha chiesto
        /// la modifica deve poterlo leggere invece di scoprirlo in officina.
        /// </summary>
        /// <summary>
        /// Rimette una quota al valore di prima dopo un rifiuto, ricostruisce e
        /// RILEGGE il volume: la coda della frase di rifiuto. Sta in un metodo
        /// solo perche' la usano due guardie di `SetDimension` (aperture che
        /// escono dal materiale, smussi che non ci stanno), e due copie dello
        /// stesso «ho rimesso» divergono alla prima correzione.
        /// </summary>
        private static string PutBackDimension(
            IModelDoc2 model, IDimension dimension, double before, double? volumeBefore,
            Func<double, double> toSystem, string units)
        {
            // swSetValueInConfiguration_e.swSetValue_InAllConfigurations = 2
            const int inAllConfigurations = 2;
            // Nella stessa unita' in cui `before` e' stata letta: gradi per una
            // quota angolare, mm per le altre (voce Z-C21.2, M3).
            try
            {
                dimension.SetSystemValue3(toSystem(before), inAllConfigurations, null);
                model.EditRebuild3();
                double? returned = VolumeMm3(model);
                return returned == null || volumeBefore == null
                    ? " Ho rimesso " + before.ToString("0.###") + " " + units + ", ma non ho potuto " +
                      "rimisurare il volume per controllare che il pezzo sia com'era: guardalo."
                    : Math.Abs(returned.Value - volumeBefore.Value) < 0.01
                    ? " Non l'ho lasciato cosi': ho rimesso " + before.ToString("0.###") +
                      " " + units + " e il pezzo e' com'era."
                    : " Ho provato a rimettere " + before.ToString("0.###") + " " + units + ", ma il " +
                      "volume adesso e' " + returned.Value.ToString("0.##") + " mm3 invece di " +
                      volumeBefore.Value.ToString("0.##") + ": guarda il pezzo prima di andare avanti.";
            }
            catch (Exception ex)
            {
                return " E non sono riuscito a rimettere " + before.ToString("0.###") +
                       " " + units + " (" + ex.Message + "): il pezzo e' rimasto col valore nuovo.";
            }
        }

        /// <summary>
        /// Lo smusso o il raccordo che una quota nuova ha fatto uscire di misura,
        /// detto a parole («lo smusso «Smusso1» si prende una faccia intera di
        /// «Estrusione1» (6 -> 5 facce)»); null se non ce n'e'.
        ///
        /// Due condizioni, tutte e due lette: una lavorazione che non e' un
        /// taglio (`Cut`, `ICE`, `HoleWzd`, e i tagli di rivoluzione) ha MENO
        /// facce di prima, e dopo di lei nell'albero c'e' uno smusso o un
        /// raccordo. Un conteggio -1 non entra: «non lo so» non e' «perso».
        /// </summary>
        private static string ChamferThatDoesNotFit(
            IModelDoc2 model, Dictionary<string, int> before, Dictionary<string, int> after)
        {
            if (model == null || before == null || after == null) return null;
            string loser = null, howMuch = null;
            try
            {
                var f = model.FirstFeature() as IFeature;
                int views = 0;
                while (f != null && views < 400)
                {
                    views++;
                    string itemName = null, typ = null;
                    try { itemName = f.Name; typ = f.GetTypeName2(); } catch { }
                    if (itemName != null && typ != null)
                    {
                        bool isCut = typ == "Cut" || typ == "ICE" || typ == "HoleWzd" ||
                                         typ.IndexOf("Cut", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool isChamferOrFillet = typ == "Chamfer" ||
                                                typ.IndexOf("Fillet", StringComparison.OrdinalIgnoreCase) >= 0;
                        int p, d;
                        if (loser == null && !isCut && !isChamferOrFillet &&
                            before.TryGetValue(itemName, out p) && after.TryGetValue(itemName, out d) &&
                            p >= 0 && d >= 0 && d < p)
                        {
                            loser = itemName;
                            howMuch = p + " -> " + d;
                        }
                        else if (loser != null && isChamferOrFillet)
                        {
                            return (typ == "Chamfer" ? "lo smusso «" : "il raccordo «") + itemName +
                                   "» si prende una faccia intera di «" + loser + "» (" + howMuch +
                                   " facce)";
                        }
                    }
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch { }
            return null;
        }

        private static string ChangedFeatures(
            Dictionary<string, int> before, Dictionary<string, int> after,
            Dictionary<string, object> measurements)
        {
            var lost = new List<string>();
            var taken = new List<string>();
            var disappeared = new List<string>();

            foreach (var entry in before)
            {
                if (entry.Value < 0) continue;
                int now;
                if (!after.TryGetValue(entry.Key, out now)) { disappeared.Add(entry.Key); continue; }
                if (now < 0) continue;
                if (now < entry.Value) lost.Add(entry.Key + " " + entry.Value + " -> " + now);
                else if (now > entry.Value) taken.Add(entry.Key + " " + entry.Value + " -> " + now);
            }

            if (lost.Count == 0 && taken.Count == 0 && disappeared.Count == 0) return null;

            if (lost.Count > 0) measurements["features_with_fewer_faces"] = lost.ToArray();
            if (taken.Count > 0) measurements["features_with_more_faces"] = taken.ToArray();
            if (disappeared.Count > 0) measurements["features_gone"] = disappeared.ToArray();

            var lines = new List<string>();
            if (disappeared.Count > 0)
                lines.Add("non hanno piu' nessuna faccia: " + string.Join(", ", disappeared));
            if (lost.Count > 0)
                lines.Add("hanno perso facce: " + string.Join(", ", lost) +
                          ". Un taglio cieco che perde una faccia di solito e' " +
                          "diventato PASSANTE: il fondo non c'e' piu'");
            if (taken.Count > 0)
                lines.Add("hanno preso facce: " + string.Join(", ", taken));

            return "La quota nuova ha cambiato l'albero, non solo le misure: " +
                   string.Join("; ", lines) + ".";
        }

        /// <summary>
        /// Quanti corpi solidi ha la parte, o **-1 se non si e' potuto leggere**
        /// (voce Z-C8): anche un documento che non e' una parte, perche' «zero
        /// corpi» detto di un assieme e' una frase falsa.
        ///
        /// ZERO-E-ZERO: `GetBodies2` null su una parte viva vuol dire che non ha
        /// corpi solidi (una parte appena aperta, o fatta solo di superfici);
        /// una maniglia morta solleva, e finisce nel `catch`.
        ///
        /// Chi confronta due conteggi deve guardare prima che siano tutti e due
        /// >= 0: un -1 dentro una differenza e' un numero inventato.
        /// </summary>
        private static int CountSolidBodies(IModelDoc2 model)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return -1;
            try
            {
                var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                return bodies == null ? 0 : bodies.Length;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Un conteggio per una risposta: il numero, o null quando e' -1 («non
        /// lo so»). Un campo null si legge come «non misurato»; un -1 si
        /// leggerebbe come un numero.
        /// </summary>
        private static object NumberOrNull(int n)
        {
            return n >= 0 ? (object)n : null;
        }

        /// <summary>Un volume per il diario: il numero, o «?» se non si e' letto.</summary>
        private static string ReadValue(double? v)
        {
            return v.HasValue ? v.Value.ToString("0.#") : "?";
        }

        /// <summary>Un volume per una risposta: arrotondato, o null se non si e' letto.</summary>
        private static object NumberOrNull(double? v, int digits)
        {
            return v.HasValue ? (object)Math.Round(v.Value, digits) : null;
        }

        /// <summary>
        /// Il rifiuto quando il volume non si legge PRIMA di fare qualcosa
        /// (voce Z-C8). Non costa niente: non si e' ancora toccato il pezzo, e
        /// senza quel numero ogni controllo dopo sarebbe un confronto con
        /// l'aria. E' la stessa strada di `MoveBodies` con `MassOf`.
        /// </summary>
        private static string VolumeNotReadBefore(string what)
        {
            return "Non riesco a misurare il volume del pezzo prima di " + what + ": senza quel " +
                   "numero non saprei dirti cosa e' cambiato. Non ho toccato niente.";
        }

        /// <summary>
        /// La nota quando il volume non si rilegge DOPO (voce Z-C8). Qui non si
        /// rifiuta: la lavorazione c'e', e buttarla per una lettura fallita
        /// sarebbe peggio. Non si scrive nemmeno un numero: si dice che i
        /// controlli sul materiale non sono stati fatti. E' la regola di
        /// `NoteOnVolume` e del guscio, «la guardia tace invece di inventare»,
        /// detta a voce alta.
        /// </summary>
        private const string NoteVolumeNotReread =
            "Dopo la lavorazione non sono riuscito a rimisurare il volume del pezzo: la " +
            "lavorazione c'e', ma i controlli sul materiale tolto o aggiunto non li ho potuti " +
            "fare. Guarda il pezzo prima di andare avanti.";

        /// <summary>
        /// Il volume di tutti i corpi solidi, in mm3, o **null se non si e'
        /// potuto leggere** (voce Z-C8).
        ///
        /// Diceva «zero se non si legge», e duemila righe piu' su
        /// `NoteOnVolume` spiegava perche' quello zero era un'affermazione
        /// falsa — difendendosi da sola, e lasciando il buco a tutti gli altri
        /// chiamanti. Adesso il tipo lo dice: `double?`, e il compilatore
        /// impedisce di sommarlo o arrotondarlo senza guardare.
        ///
        /// ZERO-E-ZERO: `GetBodies2` null su una parte viva vuol dire nessun
        /// corpo solido, e il volume e' davvero zero. Un corpo che non da' le
        /// sue proprieta' di massa fa null TUTTO: saltarlo darebbe una somma
        /// parziale detta come intera.
        ///
        /// L'INDICE 3 E' MISURATO, NON RICORDATO. `IBody2.GetVolume` non
        /// esiste (e' fra le sette API che sembravano giuste); l'alternativa
        /// vera e' `GetMassProperties(densita)`, che restituisce un vettore.
        /// Il 9 settembre 2026, misurando i fori guidati, l'elemento 3 e'
        /// risultato il volume in metri cubi: un foro M8 su una piastra da 20
        /// toglieva 726,3 mm3, cioe' pi*3,4^2*20 al decimo di millimetro cubo,
        /// e cosi' per tutte e undici le taglie da M3 a M24. Su un vettore
        /// letto male quei numeri non tornerebbero.
        /// </summary>
        private static double? VolumeMm3(IModelDoc2 model)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return null;
            double v = 0;
            try
            {
                var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null) return 0;
                foreach (var c in bodies)
                {
                    var mp = (c as IBody2)?.GetMassProperties(1000.0) as double[];
                    if (mp == null || mp.Length <= 4) return null;
                    v += mp[3] * 1e9;
                }
            }
            catch { return null; }
            return v;
        }

        /// <summary>
        /// Seleziona gli spigoli indicati dal piano, per riferimento a un passo
        /// oppure per identificativo dello stato.
        ///
        /// STA IN UN METODO SOLO PERCHE' DUE COPIE DIVERGONO.
        /// Raccordo e smusso nominano gli spigoli nello stesso identico modo, e
        /// tutti i controlli che contano stanno qui: che il passo citato esista,
        /// che l'identificativo sia ancora quello (gli uid sono posizionali e si
        /// riassegnano a ogni lettura), e che la misura chiesta ci stia sulla
        /// lunghezza dello spigolo. Scritti due volte, il giorno in cui si
        /// corregge uno l'altro resta indietro.
        ///
        /// E LE DUE STRADE DENTRO QUESTO METODO ERANO GIA' DIVERGENTI.
        /// Il metodo era uno, ma aveva due rami — per uid e per riferimento a
        /// un passo — e il secondo usciva con `return true` prima di arrivare
        /// al controllo sulla misura. Bastava un metodo solo per credersi al
        /// sicuro: il commento qui sopra prometteva un controllo che meta' del
        /// metodo non faceva. Misurato il 14/9/2026, riparato nella 0.25.2.
        /// Mettere due strade sotto lo stesso commento non e' meno pericoloso
        /// che mettere due copie sotto due nomi.
        /// </summary>
        /// <param name="maximumMeasure">
        /// Raggio o lato: non puo' superare meta' dello spigolo. Zero = non
        /// controllare.
        /// </param>
        private bool SelectEdges(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState,
            double maximumMeasure, out int howMany, out string reason)
        {
            howMany = 0;
            reason = null;
            var uid = StringList(a, "edges_uid");

            // ── Spigoli nominati per riferimento a un passo precedente ──
            //
            // «Arrotonda i quattro angoli della piastra che fai al passo 1»:
            // quando il piano e' stato scritto quegli spigoli non esistevano.
            // Adesso esistono, e il selettore li trova.
            if (HasReference(a, "edges_ref"))
            {
                int number;
                var planStep = CitedStep(a, "edges_ref", out number, out reason);
                if (planStep == null)
                {
                    reason = reason ?? "Riferimento non risolto.";
                    return false;
                }

                List<IEntity> foundItems;
                if (!References.Edges(planStep, KindOf(a, "edges_ref"), out foundItems, out reason))
                    return false;

                // LA MISURA SI CONTROLLA ANCHE QUI, e fino alla 0.25.2 no.
                //
                // Il commento di questo metodo prometteva che «tutti i
                // controlli che contano stanno qui», e fra quelli elencava che
                // la misura chiesta ci stia sulla lunghezza dello spigolo. Ma
                // questo ramo usciva con `return true` PRIMA di arrivarci: lo
                // stesso raccordo da 30 mm su uno spigolo da 10 veniva
                // rifiutato con `edges_uid` e lasciato passare con
                // `edges_ref`, fino a FeatureFillet3 che fallisce dicendo
                // soltanto che non e' stato creato.
                //
                // Qui non c'e' una firma da cui leggere la misura — gli spigoli
                // arrivano dal passo, non dallo stato — quindi si misurano
                // adesso, che e' anche il momento giusto: fra la proposta e
                // l'esecuzione il pezzo puo' essere cambiato.
                if (maximumMeasure > 0)
                {
                    double shorter = 0;
                    foreach (var e in foundItems)
                    {
                        double len = DocumentState.LengthMm(e as IEdge);
                        if (len <= 0) continue;          // non misurabile: non si inventa un verdetto
                        if (maximumMeasure <= len / 2) continue;
                        if (shorter == 0 || len < shorter) shorter = len;
                    }
                    if (shorter > 0)
                    {
                        reason = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0:0.##} mm e' troppo per gli spigoli del passo {1}: il piu' corto " +
                            "e' lungo {2:0.##} mm, e non ci sta neanche meta'. " +
                            "Il massimo che ci sta e' {3:0.##} mm.",
                            maximumMeasure, number, shorter, shorter / 2);
                        return false;
                    }
                }

                model.ClearSelection2(true);
                foreach (var e in foundItems) e.Select4(true, null);

                // IL NUMERO VERO LO DICE SOLIDWORKS, NON IL CONTEGGIO NOSTRO.
                // La stessa entita' incontrata su due facce si seleziona una
                // volta sola: `found.Count` sarebbe piu' alto del reale, e
                // finirebbe nel messaggio che l'utente legge dopo.
                try
                {
                    var selection = model.SelectionManager as ISelectionMgr;
                    if (selection != null) howMany = selection.GetSelectedObjectCount2(-1);
                }
                catch { }
                if (howMany == 0)
                {
                    reason = "Gli spigoli del passo " + number +
                             " non si sono lasciati selezionare.";
                    return false;
                }
                _log("[agente] " + howMany + " spigoli presi dal passo " + number);
                return true;
            }

            if (uid.Count == 0)
            {
                reason = "Nessuno spigolo indicato.";
                return false;
            }

            model.ClearSelection2(true);
            int selected = 0;
            foreach (var u in uid)
            {
                // Anche gli spigoli sono numerati per posizione, ordinati per
                // lunghezza: dopo un passo che cambia il corpo, «E7» e' un
                // altro spigolo. Stesso controllo delle facce, e dalla voce E19
                // la stessa ricerca per firma.
                var signature = EdgeSignature(
                    a, docState, u, "Lo spigolo «" + u + "» non c'e' piu' in questo documento.",
                    out reason);
                if (signature == null) return false;
                var edge = signature.Entity as IEntity;
                if (edge == null)
                {
                    reason = "Lo spigolo «" + u + "» non e' piu' selezionabile.";
                    return false;
                }

                // La misura non puo' superare meta' dello spigolo: il controllo
                // si rifa' QUI, sulla lunghezza misurata adesso, perche' fra la
                // proposta e l'esecuzione il pezzo puo' essere cambiato.
                if (maximumMeasure > 0 && signature.Measure > 0 && maximumMeasure > signature.Measure / 2)
                {
                    reason = string.Format(
                        "{0:0.##} mm e' troppo per lo spigolo «{1}», lungo {2:0.##} mm: " +
                        "non ci sta neanche meta'.",
                        maximumMeasure, u, signature.Measure);
                    return false;
                }

                if (!edge.Select4(true, null))
                {
                    reason = "Non sono riuscito a selezionare lo spigolo «" + u + "».";
                    return false;
                }
                selected++;
            }

            howMany = selected;
            return true;
        }

        /// <summary>
        /// The fillet itself, on what is already selected. <paramref name="volumeBefore"/>
        /// is the volume read before the selection, for EdgeFeatureVolumeCheck
        /// (Z-C21.8, finding M37).
        /// </summary>
        private Outcome RunFillet(IModelDoc2 model, double radius, int howManyEdges, double? volumeBefore)
        {
            var options = (int)(swFeatureFilletOptions_e.swFeatureFilletUniformRadius |
                                swFeatureFilletOptions_e.swFeatureFilletPropagate);
            var feature = model.FeatureManager.FeatureFillet3(
                Options: options,
                R1: Units.M(radius),
                R2: 0,
                Rho: 0,
                Ftyp: (int)swFeatureFilletType_e.swFeatureFilletType_Simple,
                OverflowType: (int)swFilletOverFlowType_e.swFilletOverFlowType_Default,
                ConicRhoType: (int)swFeatureFilletProfileType_e.swFeatureFilletCircular,
                Radii: null,
                Dist2Arr: null,
                RhoArr: null,
                SetBackDistances: null,
                PointRadiusArray: null,
                PointDist2Array: null,
                PointRhoArray: null) as IFeature;

            if (feature == null) return Failed("Il raccordo non e' stato creato.");
            var measures = new Dictionary<string, object>
            {
                ["radius_mm"] = radius,
                ["edges"] = howManyEdges,
            };
            SelectedEdgesAndFaces(feature, howManyEdges, measures);
            // After the counts, which SelectedEdgesAndFaces reads right after
            // creation as the probe did: the check rebuilds before measuring.
            string filletUnchanged = EdgeFeatureVolumeCheck(model, "fillet", volumeBefore, measures);
            if (filletUnchanged != null) return Failed(filletUnchanged);
            return Succeeded(feature, measures);
        }

        /// <summary>
        /// THE REPLY OF A FILLET OR A CHAMFER SAYS WHAT WAS SELECTED AND WHAT WAS
        /// MADE (finding N14, Z-C21.3). «edges» is the number of edges the
        /// caller passed, kept under its old name for the callers that read it;
        /// but with tangent propagation one selected edge can work a whole loop.
        /// Measured on 28/9/2026 (Z-C21.3 edge-count probe, a block with its four
        /// vertical edges rounded r 5): a fillet r 2 on ONE top edge, with these
        /// options, removed 110.399 mm3 and owned 8 faces; a chamfer 2 x 45 on one
        /// bottom edge, 254.454 mm3 and 8 faces; the reply said «edges: 1» both
        /// times. So the reply also carries «selected_edges» (the same count,
        /// under the name of what it is) and «faces», IFeature.GetFaceCount of the
        /// new feature (IFeature~GetFaceCount.html: «Gets the number of faces in
        /// this feature»), read right after creation as the probe read it. The
        /// document state carries the same two fields for every fillet and
        /// chamfer (DocumentState.EdgeAndFaceCounts).
        /// </summary>
        private static void SelectedEdgesAndFaces(IFeature feature, int selected, Dictionary<string, object> measures)
        {
            measures["selected_edges"] = selected;
            try
            {
                int faces = feature.GetFaceCount();
                if (faces > 0) measures["faces"] = faces;
            }
            catch { }
        }

        private Outcome CircularPattern(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            double? volumeBefore; int bodiesBefore, facesBefore;
            Snapshot(model, out volumeBefore, out bodiesBefore, out facesBefore);
            if (volumeBefore == null || bodiesBefore < 0 || facesBefore < 0)
                return Failed(VolumeNotReadBefore("fare la copia"));

            int instances = (int)Num(a, "instance_count");
            double angle = Num(a, "total_angle_deg", 360);
            string featureUid = Str(a, "feature_uid", "");

            model.ClearSelection2(true);
            // L'asse va selezionato per PRIMO con Mark = 1: la ripetizione
            // circolare prende la direzione dalla selezione, non dai parametri.
            string why;
            if (!SelectAxis(model, a, "axis", docState, SelectionMarks.CircularPatternAxis, append: false, out why)) return Failed(why);

            // La lavorazione da ripetere puo' essere quella fatta da un passo
            // precedente di questo stesso piano — «fai un foro e ripetilo 6
            // volte». In quel caso si seleziona l'OGGETTO, non il nome: il
            // nome che SolidWorks le ha dato lo sapremmo solo rileggendolo, e
            // sarebbe un giro in piu' per arrivare alla stessa feature che
            // gia' abbiamo in mano.
            if (HasReference(a, "feature_ref"))
            {
                int number;
                var previous = CitedStep(a, "feature_ref", out number, out why);
                if (previous == null) return Failed(why ?? "Riferimento non risolto.");
                if (previous.Feature == null)
                    return Failed($"Il passo {number} non ha lasciato una lavorazione da ripetere.");
                if (!previous.Feature.Select2(true, SelectionMarks.CircularPatternFeatures))
                    return Failed($"Non sono riuscito a selezionare la lavorazione del passo {number}.");
                _log($"[agente] ripeto la lavorazione del passo {number}");
            }
            else if (!model.Extension.SelectByID2(featureUid, "BODYFEATURE", 0, 0, 0, true, SelectionMarks.CircularPatternFeatures, null, 0))
            {
                return Failed($"Non ho trovato la lavorazione «{featureUid}» da ripetere.");
            }

            var seed = SelectedSeed(model, SelectionMarks.CircularPatternFeatures);

            var feature = model.FeatureManager.FeatureCircularPattern5(
                Number: instances,
                Spacing: Units.Rad(angle),
                FlipDirection: false,
                DName: "",
                GeometryPattern: false,
                EqualSpacing: true,
                VaryInstance: false,
                SyncSubAssemblies: false,
                BDir2: false,
                BSymmetric: false,
                Number2: 1,
                Spacing2: 0,
                DName2: "",
                EqualSpacing2: false) as IFeature;

            if (feature == null) return Failed("La ripetizione non e' stata creata.");
            var measurements = new Dictionary<string, object>
            {
                ["instances"] = instances,
                ["angle_deg"] = angle,
            };
            var stopped = CompareCopy(
                model, volumeBefore, bodiesBefore, facesBefore, measurements,
                "La ripetizione circolare");
            if (stopped != null) return Failed(stopped);

            var missingItems = MissingCopies(
                measurements, feature, seed, instances - 1, "La ripetizione circolare",
                "Con un angolo minore del giro intero le copie si fermano dove " +
                "finisce l'arco: controlla «total_angle_deg», oppure porta " +
                "le istanze a quante ce ne stanno.");
            if (missingItems != null) return Failed(missingItems);

            return Succeeded(feature, measurements);
        }

        // ─────────────────────────────────────────────────────────────────
        // Mattoni comuni
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// THE SKETCH A CALLER HAS JUST DRAWN AND CLOSED, BY THE DIFFERENCE OF
        /// THE TREE (scheme S7, Z-C21.5): the one ProfileFeature whose name was
        /// not in <paramref name="namesBeforeSketch"/>, the names the caller
        /// took with <see cref="FeatureNamesNow"/> BEFORE it selected the plane
        /// or face and opened the sketch. Null when none was born, when more
        /// than one was, when the names were not taken or the tree cannot be
        /// read, and on anything but a part; <paramref name="born"/> says how
        /// many sketches were born (-1: not read). The decision is
        /// <see cref="BornFeatureIndex"/>, the same as for the planes and axes.
        ///
        /// UNTIL LIBRARY 0.146.0 it was the LAST ProfileFeature of the tree,
        /// which is the new sketch only when SOLIDWORKS puts it at the bottom
        /// and when it is born at all: a sketch that was not born left the
        /// previous one, a sketch an earlier step had already used, to be
        /// selected in its place. IPartDoc~IFirstFeature.html
        /// (Remarks): «this order changes when the part is edited» and «you
        /// should not assume that Sketch1 always appears before Sketch2».
        ///
        /// THE NAMES ARE READ, NEVER WRITTEN. Feature names are localized (an
        /// Italian installation says «Schizzo1» where an English one says
        /// «Sketch1») and a SelectByID2 on a name we made up answers false in
        /// silence. Here a name is only compared with the names read from the
        /// same tree a moment before, and the feature is then selected as an
        /// object. IFeature~Name.html (Remarks): a new name «must be unique in
        /// the FeatureManager design tree», so a name that was not there before
        /// is a feature that was not there before. The type only narrows:
        /// IFeature~GetTypeName2.html lists ProfileFeature under Sketch, with
        /// ISketch as its specific feature (3DProfileFeature is the 3D sketch).
        ///
        /// Not ISketchManager.ActiveSketch taken as a feature while the sketch
        /// is still open: only a guide example does that cast
        /// (Rename_Active_Sketch_Example_VB.htm, «Set swFeat = swSketch» after
        /// GetActiveSketch2), no Remarks says it, and nothing here can measure
        /// it today; the names before and after are what FeatureBorn already
        /// uses for every other feature a call makes.
        /// </summary>
        private static IFeature JustCreatedSketch(IModelDoc2 model, HashSet<string> namesBeforeSketch, out int born)
        {
            return FeatureBorn(model, namesBeforeSketch, "ProfileFeature", out born);
        }

        /// <summary>
        /// Why the sketch just drawn could not be identified. With none born the
        /// caller's own sentence, the one it has always said; with an unreadable
        /// tree the same sentence and why; with two or more born, how many:
        /// two candidates, and taking the later would be «the last sketch of the
        /// tree» again. Pure, so that Oce.Solid.Agent.ContractTests checks it without
        /// SOLIDWORKS (Zc215SketchIdentityContract).
        /// </summary>
        internal static string SketchNotIdentified(int born, string notFound)
        {
            if (born > 1)
                return born + " sketches were born since this sketch was opened, and nothing tells which " +
                       "one was just drawn: none is taken.";
            if (born < 0)
                return notFound + " The feature tree could not be read before or after the sketch, so the " +
                       "new sketch cannot be told from the others.";
            return notFound;
        }

        /// <summary>
        /// Selects the sketch just closed, ready to be extruded or cut. False
        /// when it cannot be identified or selected.
        ///
        /// WHY IT IS NEEDED. `InsertSketch(false)` leaves the sketch but does
        /// NOT leave it selected, and FeatureExtrusion3 works on what is
        /// selected: without this step the feature is simply not born. It
        /// happened on 7 September 2026 at the first real extrusion: the sketch
        /// stayed in the tree, unused, and the plan stopped saying that the
        /// extrusion was not created, with no other explanation. The
        /// parameters were right: the selection was missing.
        ///
        /// WHICH SKETCH: the one born since <paramref name="namesBeforeSketch"/>
        /// (<see cref="JustCreatedSketch"/>), never the last of the tree.
        /// </summary>
        private bool SelectJustCreatedSketch(IModelDoc2 model, HashSet<string> namesBeforeSketch, out string error)
        {
            error = null;
            int born;
            var sketch = JustCreatedSketch(model, namesBeforeSketch, out born);
            if (sketch == null)
            {
                error = SketchNotIdentified(born, "Non ritrovo lo schizzo che ho appena creato.");
                return false;
            }
            try
            {
                model.ClearSelection2(true);
                if (!sketch.Select2(false, 0))
                {
                    error = "Non sono riuscito a selezionare lo schizzo appena creato.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = "Selezione dello schizzo non riuscita: " + ex.Message;
                return false;
            }
            return true;
        }

        /// <summary>
        /// TOGLIE LO SCHIZZO QUANDO LA FEATURE NON E' NATA.
        ///
        /// IL DIFETTO DA CUI NASCE. L'11 settembre 2026, otto richieste oltre i
        /// limiti su una scatola rettangolare: sette rifiuti giusti, zero
        /// fallimenti silenziosi — e sette schizzi orfani nel pezzo. Ogni rifiuto diceva
        /// «puoi toglierlo con Ctrl+Z», che e' onesto e non e' una riparazione:
        /// e' un compito dato a chi legge, e si moltiplica per il numero di
        /// tentativi.
        ///
        /// PERCHE' ADESSO SI PUO' FARE, E PRIMA NO. La regola in testa a questo
        /// file — non prendere iniziative dentro il documento di qualcuno —
        /// resta valida per le feature che hanno TOLTO o AGGIUNTO materiale:
        /// quelle si annullano con Ctrl+Z e basta (vedi
        /// <see cref="RemainsInPart"/>). Uno schizzo che non ha prodotto niente
        /// e' un caso diverso: non e' una modifica del pezzo, e' un rifiuto
        /// nostro rimasto per terra. Toglierlo non e' iniziativa, e' pulizia.
        ///
        /// QUATTRO CONTROLLI PRIMA DI CANCELLARE, PERCHE' CANCELLARE LA FEATURE
        /// SBAGLIATA E' MOLTO PEGGIO CHE LASCIARE UNO SCHIZZO.
        ///
        ///   1. E' UNO SCHIZZO? Non si guarda il nome del tipo — i nomi sono
        ///      tradotti, ed e' l'errore che questo file evita da sempre. Si
        ///      chiede l'interfaccia specifica: `IFeature.GetSpecificFeature2`
        ///      torna ISketch per uno schizzo 2D e per uno 3D, e torna NULL per
        ///      estrusioni, tagli, raccordi, smussi e lofts. E' scritto nella
        ///      guida API 2021, voce GetSpecificFeature2: «For some feature
        ///      types, this method returns Nothing or null because there is no
        ///      specific object for that type of feature (e.g., features such
        ///      as extrusions, lofts, fillets, chamfers, etc.)». Quindi: se la
        ///      feature in fondo all'albero e' quella che volevamo creare, qui
        ///      si esce.
        ///
        ///      E POI SI ESCLUDE L'ORIGINE, che e' la trappola misurata il 14
        ///      settembre 2026 camminando l'albero di una vasca: l'origine del
        ///      pezzo e' di tipo `OriginProfileFeature` e a
        ///      GetSpecificFeature2 risponde **ISketch anche lei**. Su un
        ///      documento vuoto e' l'ultima feature dell'albero. Il tipo si
        ///      guarda per RIFIUTARE, mai per accettare: se il nome del tipo
        ///      fosse diverso da quello che ci aspettiamo restano in piedi
        ///      tutti gli altri controlli.
        ///
        ///   2. STA IN FONDO ALL'ALBERO? Si guarda l'ULTIMA feature, non
        ///      «l'ultimo schizzo». E qui c'e' il secondo fatto misurato il 14
        ///      settembre 2026, che e' il contrario di quello che sembra: uno
        ///      schizzo ASSORBITO resta comunque nel giro di primo livello di
        ///      `IFirstFeature`/`GetNextFeature`. Su una vasca fatta con una
        ///      estrusione sola l'albero e': ... `#18 ProfileFeature Schizzo1`,
        ///      `#19 Extrusion Estrusione1`. Cioe' un'estrusione riuscita
        ///      aggiunge DUE feature al conto, non una — e lo schizzo e'
        ///      penultimo, non ultimo. E' esattamente il motivo per cui il
        ///      controllo e' «l'ULTIMA e' uno schizzo» e non «esiste uno
        ///      schizzo in coda»: quando la feature nasce, dopo lo schizzo c'e'
        ///      lei.
        ///
        ///   3. DID THIS STEP MAKE IT? It must be born in this step: its name
        ///      was not in the tree when the step began
        ///      (<see cref="_featureNamesAtStepStart"/>). A sketch that was
        ///      already there belongs to someone else and is not touched.
        ///
        ///   2 AND 3 SINCE Z-C21.5 (scheme S7): «the last feature» of 2 is the
        ///   newest of the features BORN IN THIS STEP, not the last feature of
        ///   the tree. On a sheet-metal part the tail of the tree is the flat
        ///   pattern and a new feature goes before it: the old check found the
        ///   flat pattern, which is no sketch, and left every orphan sketch of a
        ///   failed step in the part. Among the features born in this step the
        ///   newest is the last in tree order, because each was inserted after
        ///   the one before it; a feature born after the sketch (the one the
        ///   step wanted) still makes the check give up.
        ///
        ///   4. DIPENDE QUALCOSA DA LUI? `IGetChildCount` dev'essere zero, ed
        ///      e' il controllo che pesa di piu' dei quattro. Sempre il 14
        ///      settembre: lo schizzo assorbito da un'estrusione riuscita ha
        ///      **1 figlio**, che e' l'estrusione. Un orfano di un fallimento
        ///      non ha nessuno che lo usa. Quindi questo numero non e' una
        ///      cautela generica: e' la differenza fra «consumato» e
        ///      «abbandonato», chiesta a SolidWorks invece che dedotta.
        ///
        /// DeleteSelection2 E NON EditDelete, E IL MOTIVO E' UNA TRAPPOLA NOTA.
        /// La guida API 2021, nelle Remarks di IModelDoc2::EditDelete, dice:
        /// «Use IModelDocExtension::DeleteSelection2 to turn off prompting the
        /// user to confirm the deletion». EditDelete apre una finestra di
        /// conferma: dentro l'add-in, che gira NEL processo di SolidWorks, la
        /// chiamata COM non tornerebbe mai e SolidWorks resterebbe piantato —
        /// e' esattamente quello che ha fatto AddDimension con
        /// swInputDimValOnCreate acceso, dieci minuti fermo, misurati l'11
        /// settembre 2026. DeleteSelection2 ritorna un bool e non chiede
        /// niente a nessuno. L'opzione e' swDelete_Absorbed: le quote dello
        /// schizzo sono assorbite da lui e devono andarsene con lui.
        /// swDelete_Children NON si passa: se un figlio ci fosse, il controllo
        /// 4 ha gia' rinunciato, e passare il flag vorrebbe dire cancellare di
        /// piu' proprio nel caso che non abbiamo capito.
        ///
        /// E DOPO SI MISURA. Volume, numero di corpi e numero di feature: il
        /// primo e il secondo devono essere identici, il terzo dev'essere
        /// calato di UNO. Se non torna, si dice <see cref="SketchRemovedButNotOnlyIt"/>,
        /// che e' la sola risposta onesta: <see cref="LeftSketchBehind"/> a quel
        /// punto sarebbe una bugia che copre un danno.
        /// </summary>
        /// <returns>
        /// Stringa vuota se lo schizzo e' stato tolto e il pezzo e' quello di
        /// prima — non c'e' niente da aggiungere al motivo del fallimento;
        /// <see cref="LeftSketchBehind"/> se non si e' potuto toglierlo;
        /// <see cref="SketchRemovedButNotOnlyIt"/> se toglierlo ha cambiato il pezzo.
        /// </returns>
        // ══════════════════════════════════════════════════════════════════
        // R2 — TOGLIERE una lavorazione dall'albero
        //
        // Fino alla 0.33.0 le trentasei operazioni erano tutte additive o
        // parametriche: l'albero poteva solo CRESCERE. L'unica correzione era
        // `set_dimension` — si cambia un numero, non si ritira una scelta — e
        // `undo` (R1), che pero' disfa l'ULTIMO piano intero e non una
        // lavorazione in mezzo.
        //
        // Queste due tolgono una lavorazione qualunque: `suppress_feature`
        // la spegne (si riaccende), `delete_feature` la cancella.
        //
        // ── LA PARTE DIFFICILE E' IL RIFIUTO, come per `undo` ───────────
        //
        // **Le lavorazioni hanno figli.** Togliere l'estrusione di base fa
        // crollare tutto quello che ci sta sopra, e SOLIDWORKS lo fa **senza
        // chiedere**. La guardia `IGetChildCount()` esisteva gia' per lo
        // schizzo orfano (`WithoutSketch`, punto 4); qui e' estesa, e quando
        // ci sono figli **si rifiuta dicendo quali** — non si sceglie per conto
        // di chi disegna.
        // ══════════════════════════════════════════════════════════════════

        /// <summary>La lavorazione che porta quel nome, o null.</summary>
        private static IFeature FindFeature(IModelDoc2 model, string uid)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null) return null;
            try
            {
                var f = partDoc.IFirstFeature();
                while (f != null)
                {
                    string itemName = null;
                    try { itemName = f.Name; } catch { }
                    if (string.Equals(itemName, uid, StringComparison.OrdinalIgnoreCase)) return f;
                    f = f.IGetNextFeature();
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// I nomi di cio' che dipende da questa lavorazione.
        ///
        /// Servono a SCRIVERLI nel rifiuto. «Non posso, ha dei figli» manda chi
        /// legge a cercarli a mano; «non posso, ci stanno sopra Taglio1 e
        /// Raccordo2» gli dice cosa deve togliere prima, o gli fa capire che ha
        /// sbagliato lavorazione.
        /// </summary>
        private static List<string> ChildrenOf(IFeature f)
        {
            var names = new List<string>();
            try
            {
                var children = f.GetChildren() as object[];
                if (children == null) return names;
                foreach (var g in children)
                {
                    var child = g as IFeature;
                    if (child == null) continue;
                    try { names.Add(child.Name); } catch { }
                }
            }
            catch { }
            return names;
        }

        /// <summary>
        /// I nomi delle lavorazioni che ci sono, per un rifiuto che aiuta
        /// invece di constatare. Corto: dodici bastano a far capire la forma
        /// dei nomi.
        /// </summary>
        private static string FeaturesThatExist(IModelDoc2 model)
        {
            var names = new List<string>();
            var partDoc = model as IPartDoc;
            if (partDoc == null) return "";
            try
            {
                var f = partDoc.IFirstFeature();
                while (f != null && names.Count < 12)
                {
                    string itemName = null;
                    try { itemName = f.Name; } catch { }
                    if (!string.IsNullOrEmpty(itemName)) names.Add(itemName);
                    f = f.IGetNextFeature();
                }
            }
            catch { }
            return string.Join(", ", names);
        }

        /// <summary>
        /// La lavorazione nominata, con tutti i controlli che valgono per
        /// tutte e due le operazioni. Null col motivo se non si puo'.
        /// </summary>
        private IFeature FeatureToTouch(
            IModelDoc2 model, Dictionary<string, object> a, out string reason)
        {
            reason = null;
            string uid = Str(a, "feature_uid", "");
            if (string.IsNullOrWhiteSpace(uid))
            {
                reason = "Non hai detto quale lavorazione. I nomi stanno nello " +
                         "stato del documento, dentro «feature» (per esempio " +
                         "«Taglio-Estrusione2»).";
                return null;
            }

            // E' ANCORA QUELLA? Un nome si rinomina, `GetID()` no. Stessa
            // difesa di `linear_pattern` e `mirror`, e qui pesa di piu':
            // la' si copia una lavorazione sbagliata, qui la si cancella.
            if (!FeatureIsThatOne(model, uid, a, out reason)) return null;

            var f = FindFeature(model, uid);
            if (f == null)
            {
                string ci = FeaturesThatExist(model);
                reason = "Non trovo la lavorazione «" + uid + "» in questo documento." +
                         (ci.Length > 0 ? " Ci sono: " + ci + "." : "");
                return null;
            }

            // L'ORIGINE NON SI TOCCA. E' uno schizzo speciale da cui puo'
            // dipendere tutto, e `GetChildren` non sempre lo racconta.
            string typ = "";
            try { typ = f.GetTypeName2(); } catch { }
            if (typ == PartOrigin)
            {
                reason = "«" + uid + "» e' l'origine del pezzo: non e' una " +
                         "lavorazione e togliendola non resta niente.";
                return null;
            }
            return f;
        }

        /// <summary>
        /// SPEGNE una lavorazione, o la riaccende.
        ///
        /// ── PERCHE' SOPPRIMERE E NON CANCELLARE ───────────────────────────
        ///
        /// La soppressione **si disfa**: e' la strada da prendere quando si
        /// vuole vedere il pezzo senza quella lavorazione senza impegnarsi.
        /// Lo stato la mostra gia' — `suppressed: true` — dalla 0.25.2, e quel
        /// campo esisteva prima che ci fosse un modo di metterlo: era un
        /// display senza interruttore.
        ///
        /// ── GLI IDENTIFICATIVI CAMBIANO, E VA DETTO ───────────────────────
        ///
        /// Sopprimere una lavorazione fa sparire le sue facce, e gli uid sono
        /// **posizionali**: si rinumerano per area a ogni lettura. Misurato
        /// nella misura 0.1: sopprimendo la lavorazione generatrice sono
        /// sparite due facce su otto. Chi ha in mano un piano scritto su «F3»
        /// dopo questo non ha piu' «F3»: la risposta lo dice, e non lo lascia
        /// scoprire al passo dopo.
        ///
        /// **I figli non fermano la soppressione, ma si dicono.** Sopprimere un
        /// padre spegne anche i figli, ed e' cio' che di solito si vuole («togli
        /// il foro e i suoi raccordi»). Diverso da `delete_feature`, dove
        /// i figli fermano tutto: li' non si torna indietro.
        /// </summary>
        private Outcome SuppressFeature(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string reason;
            var f = FeatureToTouch(model, a, out reason);
            if (f == null) return Failed(reason);

            string uid = Str(a, "feature_uid", "");
            bool reactivate = Bool(a, "reactivate", false);

            // Z-C21.7 (S3): IsSuppressed2 in the active configuration, not the
            // obsolete IsSuppressed; guide page and probe numbers in
            // ApiHelpers.IsSuppressedHere.
            bool wasSuppressed = false;
            try { wasSuppressed = ApiHelpers.IsSuppressedHere(f); } catch { }
            if (wasSuppressed == !reactivate)
            {
                // GIA' COM'E' CHIESTA: non e' un errore, ed e' importante non
                // trattarlo come tale — un piano che la chiede due volte non ha
                // rotto niente. Ma non si dice «fatto» e basta, perche' allora
                // «fatto» non distinguerebbe piu' un cambiamento da un nulla.
                return Failed(
                    "«" + uid + "» e' gia' " +
                    (wasSuppressed ? "soppressa" : "attiva") +
                    ": non ho cambiato niente. Se volevi il contrario, " +
                    (wasSuppressed
                        ? "passa «reactivate»: true."
                        : "togli «reactivate»."));
            }

            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            var children = ChildrenOf(f);

            // IN TUTTE LE CONFIGURAZIONI, per la stessa ragione di
            // `set_dimension`: sopprimere in una sola lascerebbe il pezzo
            // diverso a seconda di come lo si guarda.
            const int inAllConfigurations = 2;
            int action = reactivate
                ? (int)swFeatureSuppressionAction_e.swUnSuppressFeature
                : (int)swFeatureSuppressionAction_e.swSuppressFeature;

            bool done;
            try { done = f.SetSuppression2(action, inAllConfigurations, null); }
            catch (Exception ex)
            {
                return Failed("SOLIDWORKS non ha " +
                               (reactivate ? "riattivato" : "soppresso") +
                               " «" + uid + "»: " + ex.Message);
            }
            if (!done)
                return Failed("SOLIDWORKS non ha " +
                               (reactivate ? "riattivato" : "soppresso") +
                               " «" + uid + "», e non ha detto perche'.");

            try { model.EditRebuild3(); } catch { }

            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);

            var measurements = new Dictionary<string, object>
            {
                ["feature"] = uid,
                ["suppressed"] = !reactivate,
                ["volume_before_mm3"] = NumberOrNull(volumeBefore, 2),
                ["volume_after_mm3"] = NumberOrNull(volumeAfter, 2),
                ["bodies"] = NumberOrNull(bodiesAfter),
                // GLI IDENTIFICATIVI NON VALGONO PIU', e si dice sempre: e' il
                // difetto piu' caro del progetto — un'operazione giusta fatta
                // alla cieca — e qui la cecita' e' garantita.
                ["note_identifiers"] =
                    "Le facce sono cambiate: gli identificativi (F1, E4) di " +
                    "prima non valgono piu'. Rileggi il pezzo prima del passo " +
                    "seguente.",
            };
            if (bodiesBefore >= 0 && bodiesAfter >= 0 && bodiesAfter != bodiesBefore)
                measurements["note_bodies"] =
                    "I corpi sono passati da " + bodiesBefore + " a " + bodiesAfter + ".";
            if (children.Count > 0)
                measurements["note_children"] =
                    "Da «" + uid + "» dipendono " + string.Join(", ", children) +
                    ": " + (reactivate ? "si sono riaccese con lei." : "si sono spente con lei.");

            _log("[agente] " + (reactivate ? "riattivata" : "soppressa") + " " + uid +
                 ", volume " + ReadValue(volumeBefore) + " -> " + ReadValue(volumeAfter));
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// CANCELLA una lavorazione. Non si torna indietro con un'altra
        /// operazione: solo con `undo`.
        ///
        /// ── I FIGLI FERMANO TUTTO, E QUI SI' ──────────────────────────────
        ///
        /// In `suppress_feature` i figli si spengono insieme al padre e si
        /// riaccendono insieme. Qui sparirebbero, e **SOLIDWORKS lo fa senza
        /// chiedere**. Quindi si rifiuta, dicendo quali sono: chi legge decide
        /// se toglierli prima o se aveva in mente un'altra lavorazione.
        ///
        /// E' la stessa guardia che `WithoutSketch` usa al punto 4, estesa da
        /// «lo schizzo che ho appena fatto io» a «una lavorazione qualunque».
        ///
        /// ── `DeleteSelection2` E NON `EditDelete` ─────────────────────────
        ///
        /// La documentazione lo dice, e il motivo e' una trappola nota:
        /// `EditDelete` apre la finestra di conferma, e una chiamata COM che
        /// apre un modale **non torna** — e' la stessa trappola di
        /// `AddDimension`. `DeleteSelection2` risponde un bool e non chiede
        /// niente a nessuno.
        ///
        /// `swDelete_Absorbed` toglie anche lo schizzo assorbito: senza, resta
        /// nell'albero uno schizzo orfano che non fa niente e confonde chi
        /// legge lo stato.
        ///
        /// A SHARED SKETCH IS SPARED, MEASURED (Z-C21.9, finding M33, not real).
        /// DeleteSelection2.html says nothing about a sketch that two features
        /// use, and the audit called the effect undocumented. The Z-C21.3 probe
        /// probe_shared_sketch_delete (SOLIDWORKS 29.5.1) measured it: with two
        /// extrusions on the same sketch (A 16000 mm3, B on top, 24000 in all),
        /// deleting B with swDelete_Absorbed left the sketch alive, with A as its
        /// only child and no error, and the volume went from 24000 to 16000, A
        /// intact (option 0, the guide's, gave the same). The control, one
        /// extrusion on its own sketch, took the sketch with it. Deleting A
        /// first is refused here anyway: the probe read IGetChildCount 1 on A.
        /// </summary>
        private Outcome DeleteFeature(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string reason;
            var f = FeatureToTouch(model, a, out reason);
            if (f == null) return Failed(reason);

            string uid = Str(a, "feature_uid", "");

            var children = ChildrenOf(f);
            int howManyChildren = 0;
            try { howManyChildren = f.IGetChildCount(); } catch { howManyChildren = children.Count; }
            if (howManyChildren > 0)
                return Failed(
                    "Non elimino «" + uid + "»: da lei " +
                    (howManyChildren == 1 ? "dipende" : "dipendono") + " " +
                    (children.Count > 0
                        ? string.Join(", ", children)
                        : howManyChildren + " lavorazion" + (howManyChildren == 1 ? "e" : "i")) +
                    ". Cancellandola sparirebbe" + (howManyChildren == 1 ? "" : "ro") +
                    " anche quell" + (howManyChildren == 1 ? "a" : "e") +
                    ", e SOLIDWORKS lo farebbe senza chiedere.\n\n" +
                    "Se e' proprio lei che vuoi togliere, togli prima cio' che " +
                    "ci sta sopra. Se ti serve solo vedere il pezzo senza, usa " +
                    "«suppress_feature»: spegne anche i figli e si disfa.");

            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            int featureBefore = CountFeature(model);

            bool removed;
            try
            {
                model.ClearSelection2(true);
                if (!f.Select2(false, 0))
                    return Failed("Non sono riuscito a selezionare «" + uid + "».");
                removed = model.Extension.DeleteSelection2(
                    (int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch (Exception ex)
            {
                return Failed("SOLIDWORKS non ha cancellato «" + uid + "»: " + ex.Message);
            }
            finally
            {
                try { model.ClearSelection2(true); } catch { }
            }

            if (!removed)
                return Failed("SOLIDWORKS non ha cancellato «" + uid + "», e non ha " +
                               "detto perche'. Il pezzo e' rimasto com'era.");

            try { model.EditRebuild3(); } catch { }

            // ── E ADESSO SI CONTROLLA CHE SIA DAVVERO SPARITA ────────────
            //
            // `DeleteSelection2` ha risposto true. Non basta: la domanda e' se
            // quel nome nell'albero non c'e' piu'. E' la differenza fra «ha
            // detto di averlo fatto» e «l'ha fatto» — la stessa distanza che
            // questo progetto insegue da cinquanta voci.
            if (FindFeature(model, uid) != null)
                return Failed(
                    "Ho chiesto di cancellare «" + uid + "», SOLIDWORKS ha " +
                    "risposto di si', e la lavorazione e' ancora nell'albero. " +
                    "Non so dirti cos'e' successo: guarda il pezzo prima di " +
                    "fare altro.");

            int featureAfter = CountFeature(model);
            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);

            var measurements = new Dictionary<string, object>
            {
                ["feature_removed"] = uid,
                ["volume_before_mm3"] = NumberOrNull(volumeBefore, 2),
                ["volume_after_mm3"] = NumberOrNull(volumeAfter, 2),
                ["features_before"] = NumberOrNull(featureBefore),
                ["features_after"] = NumberOrNull(featureAfter),
                ["bodies"] = NumberOrNull(bodiesAfter),
                ["note_identifiers"] =
                    "Le facce sono cambiate: gli identificativi (F1, E4) di " +
                    "prima non valgono piu'. Rileggi il pezzo prima del passo " +
                    "seguente.",
            };
            if (bodiesBefore >= 0 && bodiesAfter >= 0 && bodiesAfter != bodiesBefore)
                measurements["note_bodies"] =
                    "I corpi sono passati da " + bodiesBefore + " a " + bodiesAfter +
                    ": togliere questa lavorazione ha spezzato o unito il pezzo.";

            _log("[agente] eliminata " + uid + ", lavorazioni " + featureBefore +
                 " -> " + featureAfter + ", volume " + ReadValue(volumeBefore) +
                 " -> " + ReadValue(volumeAfter));
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// Un successo che NON ha creato niente.
        ///
        /// `Succeeded` scrive nel log «creata feature X» e porta la feature
        /// nuova nell'esito, perche' le trentasei operazioni che c'erano prima
        /// creavano tutte qualcosa. Togliere no — e un'operazione che dice
        /// «creata feature» mentre ne cancella una lascia nel registro una
        /// frase falsa.
        ///
        /// **Nome nuovo invece di un parametro in piu' su `Succeeded`:**
        /// cambiare l'insieme delle firme rompe chi chiama per riflessione
        /// anche quando il compilatore tace. E' la lezione della 0.25.1, e
        /// `Succeeded` e' il punto da cui passa OGNI successo del progetto —
        /// il posto peggiore in cui provarla.
        ///
        /// I riancoraggi si drenano lo stesso: se nel trovare la lavorazione
        /// una faccia e' stata ritrovata altrove, chi legge deve saperlo anche
        /// qui.
        /// </summary>
        private Outcome SucceededWithoutCreating(Dictionary<string, object> measurements)
        {
            if (_reanchors.Count > 0)
            {
                if (measurements == null) measurements = new Dictionary<string, object>();
                measurements["reanchors"] = _reanchors.ToArray();
            }
            return new Outcome
            {
                Succeeded = true,
                CreatedFeature = null,
                Feature = null,
                Measurements = measurements,
            };
        }

        private string WithoutSketch(IModelDoc2 model)
        {
            if (model == null) return LeftSketchBehind;

            // ── 2 and 3. the newest feature BORN IN THIS STEP ───────────
            // (Z-C21.5, scheme S7): not the last feature of the tree, which on
            // a sheet-metal part is the flat pattern. Without the names of the
            // step start, or with a tree that cannot be read, nobody knows what
            // this step made, and nothing is deleted (Z-C8).
            List<IFeature> features;
            List<string> names, types;
            if (_featureNamesAtStepStart == null ||
                !ReadFeatureTree(model, out features, out names, out types)) return LeftSketchBehind;
            var born = BornFeatureIndices(names, _featureNamesAtStepStart);
            if (born.Count == 0) return LeftSketchBehind;
            IFeature last = features[born[born.Count - 1]];
            int featureNow = features.Count;

            // ── 1. e dev'essere uno schizzo, e NON l'origine ────────────
            ISketch sketch = null;
            try { sketch = last.GetSpecificFeature2() as ISketch; } catch { }
            if (sketch == null) return LeftSketchBehind;
            string typ = "";
            try { typ = last.GetTypeName2(); } catch { }
            if (typ == PartOrigin) return LeftSketchBehind;

            // ── 4. e non deve avere figli ───────────────────────────────
            try { if (last.IGetChildCount() > 0) return LeftSketchBehind; }
            catch { return LeftSketchBehind; }

            // Cio' che NON deve cambiare, letto prima di toccare niente. Se non
            // si legge (voce Z-C8), non si cancella: senza il prima non si
            // potrebbe dire che togliere lo schizzo non ha cambiato il pezzo.
            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            if (volumeBefore == null || bodiesBefore < 0) return LeftSketchBehind;

            string itemName = "";
            try { itemName = last.Name; } catch { }

            bool removed;
            try
            {
                model.ClearSelection2(true);
                if (!last.Select2(false, 0)) return LeftSketchBehind;
                removed = model.Extension.DeleteSelection2(
                    (int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }
            catch (Exception ex)
            {
                _log("[agente] non sono riuscito a togliere lo schizzo: " + ex.Message);
                return LeftSketchBehind;
            }
            finally
            {
                try { model.ClearSelection2(true); } catch { }
            }

            if (!removed) return LeftSketchBehind;

            // ── e adesso si conta ───────────────────────────────────────
            int featureAfter = CountFeature(model);
            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);
            if (volumeAfter == null || bodiesAfter < 0 || featureAfter < 0)
                return " Lo schizzo che avevo disegnato l'ho tolto, ma non ho potuto rimisurare " +
                       "il pezzo per controllare che sia com'era: guardalo.";
            if (featureAfter != featureNow - 1
                || bodiesAfter != bodiesBefore
                || Math.Abs(volumeAfter.Value - volumeBefore.Value) > 1e-6)
            {
                _log(string.Format(
                    "[agente] tolto lo schizzo «{0}» ma il pezzo e' cambiato: " +
                    "feature {1}->{2}, corpi {3}->{4}, volume {5:0.###}->{6:0.###} mm3",
                    itemName, featureNow, featureAfter, bodiesBefore, bodiesAfter,
                    volumeBefore, volumeAfter));
                return SketchRemovedButNotOnlyIt;
            }

            _log("[agente] tolto lo schizzo «" + itemName + "», che la feature non ha usato");
            return "";
        }

        private IFeature Extrude(IModelDoc2 model, double thicknessMm, bool merge, bool symmetric = false)
        {
            // Simmetrico = meta' per parte del piano. Va detto nel riassunto
            // perche' cambia dove finisce il pezzo: «100 simmetrica» sono 50 da
            // una parte e 50 dall'altra, e chi legge «100» puo' intendere
            // tutt'altro. swEndCondMidPlane = 6, verificato nell'enum.
            return model.FeatureManager.FeatureExtrusion3(
                Sd: true,                       // una direzione sola
                Flip: false,
                Dir: false,
                T1: symmetric
                    ? (int)swEndConditions_e.swEndCondMidPlane
                    : (int)swEndConditions_e.swEndCondBlind,
                T2: (int)swEndConditions_e.swEndCondBlind,
                D1: Units.M(thicknessMm),
                D2: 0,
                Dchk1: false, Dchk2: false,
                Ddir1: false, Ddir2: false,
                Dang1: 0, Dang2: 0,
                OffsetReverse1: false, OffsetReverse2: false,
                TranslateSurface1: false, TranslateSurface2: false,
                Merge: merge,
                UseFeatScope: true,
                UseAutoSelect: true,
                T0: (int)swStartConditions_e.swStartSketchPlane,
                StartOffset: 0,
                FlipStartOffset: false) as IFeature;
        }

        private IFeature Cut(IModelDoc2 model, bool throughAll, double depthMm)
        {
            return model.FeatureManager.FeatureCut4(
                Sd: true,
                Flip: false,
                Dir: false,
                T1: throughAll
                    ? (int)swEndConditions_e.swEndCondThroughAll
                    : (int)swEndConditions_e.swEndCondBlind,
                T2: (int)swEndConditions_e.swEndCondBlind,
                D1: throughAll ? 0 : Units.M(depthMm),
                D2: 0,
                Dchk1: false, Dchk2: false,
                Ddir1: false, Ddir2: false,
                Dang1: 0, Dang2: 0,
                OffsetReverse1: false, OffsetReverse2: false,
                TranslateSurface1: false, TranslateSurface2: false,
                NormalCut: false,
                UseFeatScope: true,
                UseAutoSelect: true,
                AssemblyFeatureScope: false,
                AutoSelectComponents: false,
                PropagateFeatureToParts: false,
                T0: (int)swStartConditions_e.swStartSketchPlane,
                StartOffset: 0,
                FlipStartOffset: false,
                OptimizeGeometry: true) as IFeature;
        }

        /// <summary>
        /// Selects one of the three base planes WITHOUT using its name, with
        /// mark 0.
        ///
        /// The names are localized (Front/Top/Right in an English installation):
        /// a SelectByID2 on the wrong name answers false in silence and the
        /// feature downstream is built on an empty selection. Until Z-C21.5 the
        /// base planes were taken as the first three RefPlane features of the
        /// tree; since then they are recognised by their geometry, the canonical
        /// normal through the origin (References.BasePlane, scheme S19), which
        /// no plane added before them can take.
        /// </summary>
        private bool SelectPlane(IModelDoc2 model, string which)
        {
            if (!(model is IPartDoc)) return false;
            // THREE NAMES AND NOTHING ELSE (Z-C21.10, finding N33). Here too a
            // final `: 1` sent ANY unknown name to the front plane: the same
            // fallback FindBasePlane dropped after the measure of 10
            // September 2026. «Superiore» (accepted by the Forma ignoring
            // case, and until 0.140.0 passed on untouched) built the box on
            // the front plane with riuscita:true. An unknown name is now a
            // false, which every caller already turns into a refusal naming
            // the plane, exactly as for a Select2 that fails
            // (IFeature~Select2.html: «True if the feature was selected,
            // false if not»). The comparison stays Ordinal on purpose: the
            // Forma now rewrites every enum value in the catalogue's spelling
            // (Bridge/Shape.cs, pattern S20); References.BasePlane compares
            // Ordinal too.
            IFeature f = References.BasePlane(model, which, out _);
            if (f == null) return false;
            model.ClearSelection2(true);
            return f.Select2(false, 0);
        }

        private bool SelectFace(IFace2 face)
        {
            var entity = face as IEntity;
            if (entity == null) return false;
            return entity.Select4(false, null);
        }

        /// <summary>
        /// Quanto puo' essersi spostato il punto di riferimento di un'entita'
        /// fra la proposta e l'esecuzione. Deve combaciare con
        /// TOLLERANZA_PUNTO_MM del server: sono lo stesso controllo fatto due
        /// volte, e due tolleranze diverse vorrebbero dire che una delle due
        /// parti accetta cio' che l'altra rifiuta.
        /// </summary>
        private const double PointToleranceMm = 0.05;

        /// <summary>
        /// Le lunghezze degli spigoli, nello stato e nelle firme, sono
        /// arrotondate al centesimo: cinque centesimi coprono due arrotondamenti
        /// e restano mille volte sotto la lunghezza di qualunque spigolo vero.
        /// </summary>
        private const double LengthToleranceMm = 0.05;
        private const double NormalTolerance = 0.01;

        /// <summary>
        /// Trova la faccia indicata dal passo, E VERIFICA CHE SIA ANCORA QUELLA.
        ///
        /// PERCHE' NON BASTA CERCARE L'UID
        /// Gli uid sono POSIZIONALI: DocumentState li riassegna a ogni lettura
        /// enumerando le facce per area decrescente, e l'esecutore rilegge il
        /// documento prima di ogni passo. Il piano invece e' congelato al
        /// momento della conferma. Se il passo 1 aggiunge materiale, al passo 2
        /// «F1» e' un'altra faccia: la lavorazione riesce, il rebuild e'
        /// pulito, l'esito dice «riuscito» e il pezzo e' sbagliato.
        ///
        /// Per questo il piano porta con se' la FIRMA che la faccia aveva
        /// quando il modello l'ha scelta, e qui si riconfronta. Il commento in
        /// testa a DocumentState prometteva questo controllo da sempre; fino
        /// alla 0.24.0 non esisteva in nessun punto del codice.
        ///
        /// Si confronta il punto e la normale, non l'area: l'area cambia in
        /// modo legittimo quando sulla faccia si fa un foro.
        /// </summary>
        private IFace2 ResolveFace(
            DocumentState docState,
            Dictionary<string, object> arguments,
            string uid,
            out string reason)
        {
            reason = null;

            // ── La faccia puo' venire da un passo precedente del piano ──
            //
            // Sta qui, e non nelle due operazioni di taglio, perche' qui e'
            // l'unico posto da cui passano tutte e due: un foro e una tasca
            // devono poter nominare la stessa faccia nello stesso modo, e due
            // copie di questa logica sarebbero divergenti alla prima modifica.
            //
            // Il riferimento ha la precedenza sull'uid: il server garantisce
            // che non arrivino tutti e due, ma se arrivassero comunque e'
            // meglio fallire su un uid che non c'e' che lavorare in silenzio
            // sulla faccia sbagliata.
            if (HasReference(arguments, "face_ref"))
            {
                int number;
                var planStep = CitedStep(arguments, "face_ref", out number, out reason);
                if (planStep == null) return null;
                IFace2 fromStep;
                if (!References.Face(
                        planStep, KindOf(arguments, "face_ref"), out fromStep, out reason))
                    return null;
                _log($"[agente] faccia presa dal passo {number}");
                return fromStep;
            }

            if (docState == null || string.IsNullOrEmpty(uid))
            {
                reason =
                    "Nessuna faccia indicata. Se la faccia la crea un passo " +
                    "precedente di questo piano, va nominata con face_ref.";
                return null;
            }
            if (!docState.Signatures.TryGetValue(uid, out var signature) || signature.Type != "FACE")
            {
                reason = $"La faccia «{uid}» non c'e' piu' in questo documento.";
                return null;
            }
            var face = signature.Entity as IFace2;
            if (face == null)
            {
                reason = $"La faccia «{uid}» non e' piu' utilizzabile.";
                return null;
            }
            if (SignatureMatches(arguments, uid, signature, out reason)) return face;

            // ── IL PERSIST ID PROPONE, LA MISURA GIUDICA (voce E6) ──────────
            //
            // La firma non combacia. Prima di cercare alla cieca si chiede a
            // SOLIDWORKS: il riferimento persistente e' l'unica ancora che non
            // sia una nostra congettura sulla geometria.
            //
            // MAI DA SOLO. `GetObjectByPersistReference3` puo' restituire SENZA
            // ERRORE una faccia diversa dopo un rebuild che cambia la topologia:
            // sarebbe il difetto peggiore di tutta la sezione, perche' non
            // darebbe errore. Quello che torna e' un CANDIDATO, e passa dallo
            // stesso giudice di tutti gli altri — `Corroborates`.
            //
            // MISURATO (0.1, 14/9/2026): 8 facce su 8 ritrovate in tutte e
            // cinque le modifiche riuscite, comprese le tre in cui l'ancora sul
            // punto si perde. Dove ha ceduto — la soppressione della
            // lavorazione generatrice — ha ceduto in modo DISTINGUIBILE: le due
            // facce che avevano davvero smesso di esistere non tornano
            // (`ErrorCode = 1`) invece di tornare sbagliate.
            // E `IsSamePersistentID` non si usa: ha risposto vero 46 volte su
            // 46, anche dove l'area era raddoppiata perche' due facce si erano
            // fuse. Attesta l'identificativo, non la faccia.
            var expectedPersist = ExpectedSignature(arguments, uid);
            if (expectedPersist != null)
            {
                string move;
                var fromPersist = FaceFromPersistId(docState, expectedPersist, uid, out move);
                if (fromPersist != null)
                {
                    // SI SPIEGA DI QUANTO, INVECE DI RIFIUTARE. E' il motivo per
                    // cui questa voce esiste: la faccia e' quella giusta, si e'
                    // solo mossa, e chi legge deve sapere che si e' mossa.
                    ReAnchored(move);
                    reason = null;
                    return fromPersist;
                }
            }

            // LA FIRMA NON COMBACIA: PRIMA DI RIFIUTARE, SI CERCA.
            //
            // Gli uid sono POSIZIONALI — le facce si rinumerano per area a
            // ogni lettura — quindi basta una lavorazione perche' «F13» non
            // sia piu' la faccia di prima. Fin qui il sistema se ne
            // accorgeva (la firma) e si fermava.
            //
            // Fermarsi era giusto e non bastava. Misurato il 10 settembre
            // 2026 disegnando il contenitore per l'antenna: «taglia quattro
            // asole uguali sulle quattro alette» — una richiesta
            // normalissima — non si poteva fare in un piano solo. La prima
            // asola cambiava le aree, le facce si rinumeravano, e dalla
            // seconda in poi il piano nominava facce sbagliate.
            //
            // La firma pero' non serve solo ad accorgersene: serve a
            // RITROVARE. Se nel documento c'e' UNA sola faccia che combacia
            // con quella che il piano descrive, e' quella — e usarla e'
            // piu' giusto che rifiutare. Se ce ne sono due, non si sceglie:
            // li' l'ambiguita' e' vera e va detta.
            var expectation = ExpectedSignature(arguments, uid);
            if (expectation == null) return null;   // niente firma: resta il rifiuto di prima

            IFace2 foundAgain = null;
            DocumentState.EntitySignature refoundSignature = null;
            int howMany = 0;
            string discardedBecause = null;
            foreach (var entry in docState.Signatures)
            {
                if (entry.Value.Type != "FACE") continue;
                var candidate = entry.Value.Entity as IFace2;
                if (candidate == null) continue;
                string why;
                if (!SignatureMatches(arguments, uid, entry.Value, out why)) continue;

                // COMBACIARE PER POSIZIONE NON BASTA. Vedi Corrobora.
                string grown;
                string doesNotHold = Corroborates(expectation, uid, entry.Value, out grown);
                ReAnchored(grown);
                if (doesNotHold != null)
                {
                    // Si tiene il PRIMO motivo di scarto per il messaggio: se
                    // alla fine non si ritrova niente, chi legge deve sapere
                    // che una candidata c'era e perche' non e' stata presa.
                    if (discardedBecause == null) discardedBecause = doesNotHold;
                    continue;
                }

                howMany++;
                foundAgain = candidate;
                refoundSignature = entry.Value;
            }

            if (howMany == 1)
            {
                // SI DICE DI QUANTO, non solo CHE. Fino alla 0.29.0 questa
                // strada diceva «si era spostata: ritrovata dalla firma» e
                // basta — un fatto senza una misura, e per giunta solo su
                // stderr. La firma misurata e' gia' in mano (`refoundSignature`),
                // e la frase la scrive la stessa funzione che usa il persist
                // ID: due strade diverse per ritrovare, una sola per dirlo.
                ReAnchored(
                    MoveSentence(expectedPersist, uid, refoundSignature, null)
                    ?? "la faccia «" + uid + "» si era spostata ed e' stata ritrovata " +
                       "dalla firma misurata.");
                reason = null;
                return foundAgain;
            }
            if (howMany > 1)
            {
                reason =
                    "La faccia «" + uid + "» si e' spostata e ce ne sono " + howMany +
                    " che le somigliano tutte uguali: non scelgo io. Rileggi lo " +
                    "stato del documento e nominala di nuovo.";
            }
            else if (discardedBecause != null)
            {
                // Il caso che conta: UNA candidata c'era, combaciava per
                // posizione, e la corroborazione l'ha smentita. Prima veniva
                // presa e il pezzo usciva sbagliato senza che nessuno dicesse
                // niente.
                reason = discardedBecause;
            }
            return null;
        }

        /// <summary>
        /// LA SECONDA PROVA, prima di dire «ritrovata».
        ///
        /// Il ritrovamento per firma esisteva gia' e serviva: gli uid sono
        /// posizionali, e senza di lui «taglia quattro asole uguali» non si
        /// poteva scrivere in un piano solo. Ma accettava la candidata
        /// confrontando **solo punto e normale**, e quello non basta.
        ///
        /// MISURATO (voce E2, 14/9/2026). Piastra 100x60x20 con una tasca 40x40
        /// profonda 8: il fondo sta a Z=12 con area 1600, la faccia in alto a
        /// Z=20 con area 4400. Si porta lo spessore a 12; il fondo scende a Z=4
        /// e **la faccia in alto scende a Z=12**, cioe' esattamente dove il
        /// piano cerca il fondo.
        ///
        /// Il punto combaciava. L'area era 4400 contro 1600 attesi — quasi il
        /// triplo — e veniva presa lo stesso: `succeeded: true`, ricostruzione
        /// pulita, e un mezzo foro profondo 2 al posto di un foro intero
        /// profondo 4. **Il punto aveva vinto sull'area.**
        ///
        /// DUE PROVE, E NESSUNA DELLE DUE E' «L'AREA DEVE COMBACIARE».
        ///
        /// 1. **La lavorazione dev'essere la stessa**, per identificativo e non
        ///    per nome (`IFeature.GetID()`). Una faccia che viene da un'altra
        ///    lavorazione non e' la stessa faccia, comunque sia messa.
        ///
        /// 2. **L'area puo' solo CALARE**, ma SOLO quando la prima prova non ha
        ///    gia' risposto. E' un vincolo monotono, non un confronto: ci si fa
        ///    un foro e la faccia perde area, ed e' legittimo — e' proprio il
        ///    motivo per cui l'area era stata esclusa dal confronto. Ma non ne
        ///    GUADAGNA: una faccia cresciuta e' un altro pezzo di pezzo.
        ///
        /// IL CONTROLLO SULL'AREA SI MORDEVA LA CODA, e riparato il 14/9/2026.
        ///
        /// Gli uid si assegnano per area decrescente. Una faccia che si allarga
        /// abbastanza da scavalcarne un'altra in classifica CAMBIA UID per
        /// forza: `SignatureMatches` sull'uid vecchio fallisce, si passa alla
        /// ricerca, la faccia vera viene ritrovata — e veniva scartata proprio
        /// perche' era cresciuta. **La crescita dell'area era insieme la causa
        /// di questa ricerca e il motivo del suo fallimento.**
        ///
        /// Misurato: staffa a elle, aletta orizzontale 100x20 = 2000 mm² (F2) e
        /// verticale 80x20 = 1600 mm² (F3). Piano di due passi: `set_dimension`
        /// porta la verticale da 80 a 120, poi `cylindrical_cut` su «F3». Dopo
        /// il primo passo la verticale vale 2400 e diventa F2; la ricerca
        /// ritrova la faccia giusta — stesso piano, stesso punto, stessa
        /// normale, **stessa lavorazione** — e il controllo sull'area la
        /// buttava. Un rifiuto di lavoro legittimo.
        ///
        /// LA RIPARAZIONE non e' togliere il controllo: e' ordinarlo. Quando la
        /// prima prova ha ACCERTATO che la lavorazione e' la stessa — non
        /// «non lo so», proprio accertato — l'identita' e' gia' stabilita da un
        /// segnale piu' forte, e la crescita dell'area diventa una cosa da
        /// DIRE, non da rifiutare. Il vincolo monotono resta intero dove serve
        /// davvero: quando la lavorazione non si sa, che e' il caso E2.
        ///
        /// In E2 le due facce venivano da lavorazioni diverse (l'estrusione e
        /// il taglio), quindi la prima prova le separa e il difetto resta
        /// chiuso: si controlla in `Bench/cases/E2-the-two-cases.md`.
        ///
        /// PERCHE' QUI E NON DENTRO `SignatureMatches`. Perche' quel confronto
        /// serve anche a decidere se la faccia di partenza va ancora bene, e
        /// li' l'area che cresce e' normale — si allarga il pezzo e la faccia
        /// in alto diventa piu' grande, senza che il punto si muova. La
        /// corroborazione vale **solo quando si sta gia' cercando un
        /// sostituto**, cioe' quando qualcosa si e' gia' mosso.
        ///
        /// Ritorna null se la candidata regge; il motivo del rifiuto se no —
        /// e il motivo da' **il criterio**, non il verdetto.
        /// </summary>
        /// <summary>
        /// La faccia che SOLIDWORKS ritrova dal riferimento persistente, se la
        /// ritrova E se regge al giudizio. Null in ogni altro caso — e null
        /// qui vuol dire «prova un'altra strada», non «rifiuta».
        ///
        /// TRE PORTE, E TUTTE E TRE DEVONO APRIRSI.
        ///
        ///  1. **Il piano deve portare un persist ID.** Le firme di prima della
        ///     0.26.0 non ce l'hanno, e un piano scritto ieri deve continuare a
        ///     funzionare: senza, si torna alla ricerca di sempre.
        ///  2. **SOLIDWORKS deve ritrovare qualcosa.** `ErrorCode` diverso da
        ///     zero, oppure un oggetto che non e' una faccia, vogliono dire che
        ///     quella faccia non esiste piu': e' la risposta giusta al caso
        ///     della lavorazione soppressa, e va lasciata passare alla ricerca
        ///     che sapra' dirlo meglio.
        ///  3. **Il giudizio deve reggere.** Il candidato passa da `Corroborates`
        ///     come qualunque altro: stessa lavorazione, e l'area che cresce
        ///     solo se la lavorazione e' accertata. Un persist ID che torna una
        ///     faccia di un'altra lavorazione e' esattamente il difetto
        ///     silenzioso che questa voce temeva, e qui si ferma.
        ///
        /// <param name="move">
        /// Di quanto si e' mossa, in parole, quando si e' mossa. Va a chi legge:
        /// la faccia e' quella giusta e il pezzo NON e' piu' quello di prima,
        /// e tacerlo sarebbe lavorare bene su un pezzo cambiato senza dirlo.
        /// </param>
        /// </summary>
        private IFace2 FaceFromPersistId(
            DocumentState docState, Dictionary<string, object> expectation, string uid,
            out string move)
        {
            move = null;
            if (docState == null || docState.Model == null || expectation == null) return null;

            object raw;
            if (!expectation.TryGetValue("persist_id", out raw) || raw == null) return null;
            var base64 = Convert.ToString(raw);
            if (string.IsNullOrEmpty(base64)) return null;

            byte[] blob;
            try { blob = Convert.FromBase64String(base64); }
            catch { return null; }
            if (blob.Length == 0) return null;

            IFace2 candidate;
            try
            {
                var ext = docState.Model.Extension;
                if (ext == null) return null;
                int error;
                var obj = ext.GetObjectByPersistReference3(blob, out error);
                if (error != 0 || obj == null) return null;
                candidate = obj as IFace2;
                if (candidate == null) return null;
            }
            catch { return null; }

            // IL GIUDIZIO. La firma si rimisura ADESSO sulla faccia ritrovata:
            // non si usa quella che il piano porta, che e' cio' che si sta
            // verificando.
            var signatureNow = DocumentState.FaceSignature(candidate);
            if (signatureNow == null) return null;
            // `FaceSignature` misura UNA faccia e non rilegge il documento:
            // la scatola del pezzo non ce l'ha, e senza quella la frase non si
            // puo' scrivere. Gliela passa chi il documento l'ha letto.
            signatureNow.BoxPartMm = docState?.BoxPartMm;

            string note;
            if (Corroborates(expectation, uid, signatureNow, out note) != null) return null;

            move = MoveSentence(expectation, uid, signatureNow, note);
            return candidate;
        }

        /// <summary>
        /// Che cosa e' cambiato fra la firma che il piano portava e quella
        /// misurata adesso, in una frase. Null se non e' cambiato niente che
        /// valga la pena dire.
        ///
        /// SI DICE IL CRITERIO, NON IL VERDETTO: quanto si e' spostata e di
        /// quanto e' cambiata l'area, cosi' chi legge puo' decidere se il piano
        /// contava su quei numeri.
        /// </summary>
        private static string MoveSentence(
            Dictionary<string, object> expectation, string uid,
            DocumentState.EntitySignature now, string corroboratingNote)
        {
            var parts = new List<string>();

            var pointBefore = Triplet(expectation, "point_mm");
            if (pointBefore != null && now.PointMm != null && pointBefore.Length >= 3 &&
                now.PointMm.Length >= 3)
            {
                double dx = now.PointMm[0] - pointBefore[0];
                double dy = now.PointMm[1] - pointBefore[1];
                double dz = now.PointMm[2] - pointBefore[2];
                double howMuch = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (howMuch > 0.05)
                    parts.Add(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "si e' spostata di {0:0.##} mm, da [{1:0.##}, {2:0.##}, {3:0.##}] " +
                        "a [{4:0.##}, {5:0.##}, {6:0.##}]",
                        howMuch, pointBefore[0], pointBefore[1], pointBefore[2],
                        now.PointMm[0], now.PointMm[1], now.PointMm[2]));
            }

            object rawArea;
            if (expectation.TryGetValue("area_mm2", out rawArea) && rawArea != null)
            {
                double beforeArea;
                try
                {
                    beforeArea = Convert.ToDouble(
                        rawArea, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch { beforeArea = 0; }
                if (beforeArea > 0 && Math.Abs(now.Measure - beforeArea) > beforeArea * 0.001)
                    parts.Add(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "l'area e' passata da {0:0.##} a {1:0.##} mm²", beforeArea, now.Measure));
            }

            if (parts.Count == 0) return null;

            // LA SPIEGAZIONE, che qui e' il caso per cui la voce E10 esisteva:
            // la faccia si e' mossa di sessanta millimetri E sta dove stava
            // rispetto al pezzo. Dirlo aiuta a capire; deciderlo no — e infatti
            // qui non si decide niente: la faccia l'ha gia' scelta il
            // riferimento persistente, e il giudizio l'ha gia' dato Corrobora.
            return "la faccia «" + uid + "» l'ha ritrovata SOLIDWORKS dal suo " +
                   "riferimento persistente: " + string.Join(", ", parts) +
                   ". Il pezzo e' cambiato da quando l'hai guardato, ma la faccia e' " +
                   "quella giusta — se il piano contava sulle misure di prima, " +
                   "ricontrolla il risultato." +
                   Sentences.WhereItWasAndWhereItIs(
                       uid, pointBefore, now.PointMm,
                       Sextuple(expectation, "box_part_mm"), now.BoxPartMm) +
                   (corroboratingNote == null ? "" : " " + corroboratingNote);
        }

        private static string Corroborates(
            Dictionary<string, object> expectation, string uid,
            DocumentState.EntitySignature candidate, out string note)
        {
            note = null;
            if (expectation == null) return null;

            // 1. LA STESSA LAVORAZIONE.
            // Solo se li conosciamo tutti e due: due «-1» non sono una
            // corrispondenza, sono due volte «non lo so».
            bool sameFeatureConfirmed = false;
            object rawId;
            if (expectation.TryGetValue("feature_id", out rawId) && rawId != null &&
                candidate.FeatureId >= 0)
            {
                int expected;
                try { expected = Convert.ToInt32(rawId); } catch { expected = -1; }
                if (expected >= 0 && expected == candidate.FeatureId)
                    sameFeatureConfirmed = true;
                if (expected >= 0 && expected != candidate.FeatureId)
                {
                    object expectedName;
                    string fromWhom = expectation.TryGetValue("feature", out expectedName) &&
                                   expectedName != null
                        ? "«" + Convert.ToString(expectedName) + "»"
                        : "un'altra lavorazione";
                    return
                        "La faccia «" + uid + "» si e' spostata, e l'unica che combacia " +
                        "per posizione viene da «" + (candidate.Feature ?? "?") +
                        "» invece che da " + fromWhom + ": non e' la stessa faccia, e' " +
                        "un'altra finita nello stesso posto. Rileggi lo stato del " +
                        "documento e dimmi tu quale.";
                }
            }

            // 2. L'AREA PUO' SOLO CALARE.
            object rawArea;
            if (expectation.TryGetValue("area_mm2", out rawArea) && rawArea != null)
            {
                double expectedArea;
                try { expectedArea = Convert.ToDouble(rawArea, System.Globalization.CultureInfo.InvariantCulture); }
                catch { return null; }
                // Un per mille di margine: le aree si arrotondano al
                // centesimo e un confronto secco accuserebbe l'arrotondamento.
                if (expectedArea > 0 && candidate.Measure > expectedArea * 1.001)
                {
                    // LA LAVORAZIONE ACCERTATA VINCE SULL'AREA.
                    // Se si sa con certezza che viene dalla stessa lavorazione,
                    // l'identita' e' gia' stabilita: una faccia puo' essere
                    // cresciuta perche' una quota e' cambiata, ed e' legittimo.
                    // Si DICE di quanto, e si va avanti.
                    if (sameFeatureConfirmed)
                    {
                        note = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "La faccia «{0}» si era spostata ed e' anche CRESCIUTA: " +
                            "{1:0.##} mm² contro {2:0.##} che il piano si aspettava. " +
                            "L'ho presa lo stesso perche' viene dalla stessa lavorazione " +
                            "«{3}», ma se il piano contava sulla misura di prima, " +
                            "ricontrolla il risultato.",
                            uid, candidate.Measure, expectedArea, candidate.Feature ?? "?");
                        return null;
                    }

                    return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "La faccia «{0}» si e' spostata, e l'unica che combacia per " +
                        "posizione e' piu' GRANDE di quella che cercavo: {1:0.##} mm² " +
                        "contro {2:0.##}. Una lavorazione toglie area a una faccia, non " +
                        "gliene aggiunge: quella non e' la stessa faccia. Rileggi lo " +
                        "stato del documento e dimmi tu quale.",
                        uid, candidate.Measure, expectedArea);
                }
            }

            return null;
        }

        /// <summary>
        /// Lo spigolo che il piano chiama «uid»: quello dello stato se la firma
        /// combacia, altrimenti RITROVATO PER FIRMA — e solo se e' uno (voce E19).
        ///
        /// Gli uid degli spigoli sono posizionali, in ordine di lunghezza: un
        /// foro aggiunge due circonferenze e «E9» diventa un altro spigolo. Per
        /// le facce la ricerca c'e' dal 10/9/2026 (piu' su, con il persist ID
        /// della voce E6); per gli spigoli fino alla libreria 0.119.0 si
        /// rifiutava e basta, e un piano di due passi — fora, poi raccorda lo
        /// spigolo che hai visto — si fermava al secondo.
        ///
        /// LA STESSA LUNGHEZZA NELLO STESSO PUNTO, E NIENT'ALTRO. Il punto e' il
        /// medio degli estremi: uno spigolo accorciato (uno smusso sulla sua
        /// cima) ha un altro punto e un'altra lunghezza, e NON e' lo stesso
        /// spigolo anche se sta nello stesso angolo. Il piu' vicino non si
        /// prende mai: sarebbe il successo sul pezzo sbagliato, il caso B di E2.
        /// Due candidati uguali — due corpi che si toccano lungo uno spigolo —
        /// sono un'ambiguita' vera, e si dice.
        ///
        /// Senza firma nel piano resta tutto com'era: si usa l'uid, o si
        /// rifiuta con `sparito` se lo stato non ce l'ha.
        /// </summary>
        private DocumentState.EntitySignature EdgeSignature(
            Dictionary<string, object> a, DocumentState docState, string uid,
            string disappeared, out string reason)
        {
            reason = null;
            DocumentState.EntitySignature signature = null;
            bool present = docState != null && docState.Signatures.TryGetValue(uid, out signature) &&
                            signature.Type == "EDGE";
            if (present && SignatureMatches(a, uid, signature, out reason)) return signature;
            string refusal = present ? reason : disappeared;
            reason = refusal;

            var expectation = ExpectedSignature(a, uid);
            var point = Triplet(expectation, "point_mm");
            double length = Num(expectation, "length_mm");
            if (docState == null || point == null || length <= 0) return null;

            string found = null;
            DocumentState.EntitySignature foundAgain = null;
            int howMany = 0;
            foreach (var entry in docState.Signatures)
            {
                var candidate = entry.Value;
                if (candidate.Type != "EDGE" || candidate.Entity == null) continue;
                if (Math.Abs(candidate.Measure - length) > LengthToleranceMm) continue;
                string why;
                if (!SignatureMatches(a, uid, candidate, out why)) continue;
                howMany++;
                found = entry.Key;
                foundAgain = candidate;
            }

            if (howMany == 1)
            {
                // SI DICE, come per le facce: il piano ha nominato «E9» e il
                // raccordo e' finito su «E11». Chi legge la risposta deve poter
                // controllare che sia lo stesso spigolo, e i numeri per farlo
                // sono questi.
                ReAnchored(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "lo spigolo «{0}» adesso si chiama «{1}»: stessa lunghezza ({2:0.##} mm), " +
                    "stesso punto [{3:0.##}, {4:0.##}, {5:0.##}]. Gli spigoli si rinumerano per " +
                    "lunghezza quando il pezzo cambia: ho lavorato su quello che avevi visto.",
                    uid, found, length, point[0], point[1], point[2]));
                reason = null;
                return foundAgain;
            }
            if (howMany > 1)
            {
                reason = "Lo spigolo «" + uid + "» ha cambiato numero, e ce ne sono " + howMany +
                         " con la sua lunghezza nel suo punto: non scelgo io. Rileggi lo stato " +
                         "del documento e nominalo di nuovo.";
                return null;
            }
            reason = refusal + string.Format(System.Globalization.CultureInfo.InvariantCulture,
                " E fra gli spigoli di adesso nessuno e' lungo {0:0.##} mm in [{1:0.##}, " +
                "{2:0.##}, {3:0.##}] come quello che avevi visto: non scelgo il piu' vicino.",
                length, point[0], point[1], point[2]);
            return null;
        }

        /// <summary>
        /// La firma portata dal piano combacia con quella letta adesso?
        /// True anche quando il piano non porta nessuna firma: in quel caso non
        /// c'e' niente da confrontare e non si puo' inventare un rifiuto.
        /// </summary>
        private static bool SignatureMatches(
            Dictionary<string, object> arguments,
            string uid,
            DocumentState.EntitySignature now,
            out string reason)
        {
            reason = null;
            var expectation = ExpectedSignature(arguments, uid);
            if (expectation == null) return true;

            // L'ASSE E' UNA RETTA, non un punto (voce E4). Il punto che lo stato
            // pubblica e' l'inizio dell'asse, e l'inizio si sposta se il foro da
            // cui nasce si allunga: la stessa retta con un altro inizio e' lo
            // stesso asse. Quindi la direzione dev'essere parallela, e il punto
            // atteso deve stare sulla retta di adesso.
            if (now.Type == "AXIS")
            {
                var q = Triplet(expectation, "point_mm");
                var e = Triplet(expectation, "direction");
                var p = now.PointMm; var d = now.Normal;
                if (q == null || p == null || d == null || p.Length != 3 || d.Length != 3) return true;
                if (e != null)
                {
                    double dot = Math.Abs(d[0] * e[0] + d[1] * e[1] + d[2] * e[2]);
                    if (dot < Math.Cos(Math.PI / 180.0))
                    {
                        reason = "L'asse «" + uid + "» adesso e' orientato in un altro modo. Il pezzo " +
                                 "e' cambiato da quando hai confermato: " + IDoNotWorkOn(now);
                        return false;
                    }
                }
                double vx = q[0] - p[0], vy = q[1] - p[1], vz = q[2] - p[2];
                double cx = vy * d[2] - vz * d[1], cy = vz * d[0] - vx * d[2], cz = vx * d[1] - vy * d[0];
                double dist = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                if (dist > PointToleranceMm)
                {
                    reason = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "L'asse «{0}» si e' spostato di {1:0.##} mm. Il pezzo e' cambiato da quando " +
                        "hai confermato: {2}", uid, dist, IDoNotWorkOn(now));
                    return false;
                }
                return true;
            }

            // DUE NOMI PER LA STESSA COSA, e non e' pigrizia.
            // Una faccia pubblica `point_mm` — un punto SU di lei; un corpo
            // pubblica `center_mm` — il centro della sua scatola. Sono due
            // misure diverse con lo stesso mestiere: dire DOVE sta l'entita'.
            // Chiamarle allo stesso modo nello stato sarebbe stato peggio (il
            // centro di un corpo non sta sul corpo, se il corpo e' a ferro di
            // cavallo), quindi qui si accettano tutti e due.
            // UNO SPIGOLO DEV'ESSERE ANCHE LUNGO QUANTO ERA (voce E19). Il punto
            // medio da solo non basta: uno spigolo accorciato alle DUE estremita'
            // — due smussi uguali — ha lo stesso punto medio e un'altra
            // lunghezza. Si confronta solo con due lunghezze lette.
            if (now.Type == "EDGE")
            {
                double expectedLength = Num(expectation, "length_mm");
                if (expectedLength > 0 && now.Measure > 0 &&
                    Math.Abs(now.Measure - expectedLength) > LengthToleranceMm)
                {
                    reason = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "«{0}» adesso e' lungo {1:0.##} mm invece di {2:0.##}. Il pezzo e' " +
                        "cambiato da quando hai confermato: {3}",
                        uid, now.Measure, expectedLength, IDoNotWorkOn(now));
                    return false;
                }
            }

            var expectedPoint = Triplet(expectation, "point_mm") ?? Triplet(expectation, "center_mm");
            if (expectedPoint != null && now.PointMm != null && now.PointMm.Length == 3)
            {
                double deviation = 0;
                for (int i = 0; i < 3; i++)
                    deviation = Math.Max(deviation, Math.Abs(expectedPoint[i] - now.PointMm[i]));
                if (deviation > PointToleranceMm)
                {
                    // LA PAROLA GIUSTA PER LA COSA GIUSTA. Lo stesso confronto
                    // serve a facce, spigoli e corpi, e dire «non lavoro sulla
                    // faccia sbagliata» di un corpo manda a cercare una faccia
                    // che non c'entra niente.
                    // LA SPIEGAZIONE SI ATTACCA DOPO IL VERDETTO, e il verdetto
                    // e' gia' preso: sopra, sui millimetri. Quello che segue e'
                    // una frase — non un numero, non un `bool` — e non puo'
                    // cambiare l'esito nemmeno se qualcuno lo volesse. Vedi
                    // Sentences.WhereItWasAndWhereItIs (voce E10).
                    reason =
                        $"«{uid}» adesso indica un'altra parte del pezzo (spostato di " +
                        $"{deviation:0.##} mm). Il pezzo e' cambiato da quando hai " +
                        $"confermato: {IDoNotWorkOn(now)}" +
                        Sentences.WhereItWasAndWhereItIs(
                            uid, expectedPoint, now.PointMm,
                            Sextuple(expectation, "box_part_mm"), now.BoxPartMm);
                    return false;
                }
            }

            var expectedNormal = Triplet(expectation, "normal");
            if (expectedNormal != null && now.Normal != null && now.Normal.Length == 3)
            {
                double deviation = 0;
                for (int i = 0; i < 3; i++)
                    deviation = Math.Max(deviation, Math.Abs(expectedNormal[i] - now.Normal[i]));
                if (deviation > NormalTolerance)
                {
                    reason =
                        $"«{uid}» adesso e' orientata in un altro modo. Il pezzo e' " +
                        "cambiato da quando hai confermato.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// La frase di chiusura del rifiuto, con la parola giusta per il tipo
        /// di entita'. Tre parole invece di una, ed e' la differenza fra un
        /// messaggio che si capisce e uno che manda a cercare la cosa
        /// sbagliata.
        /// </summary>
        private static string IDoNotWorkOn(DocumentState.EntitySignature entity)
        {
            switch (entity?.Type)
            {
                case "BODY": return "non lavoro sul corpo sbagliato.";
                case "AXIS": return "non lavoro sull'asse sbagliato.";
                case "EDGE": return "non lavoro sullo spigolo sbagliato.";
                default: return "non lavoro sulla faccia sbagliata.";
            }
        }

        private static Dictionary<string, object> ExpectedSignature(
            Dictionary<string, object> arguments, string uid)
        {
            if (arguments == null) return null;
            if (!arguments.TryGetValue("_signatures", out var all)) return null;
            var map = all as Dictionary<string, object>;
            if (map == null) return null;
            foreach (var entry in map)
            {
                if (string.Equals(entry.Key, uid, StringComparison.OrdinalIgnoreCase))
                    return entry.Value as Dictionary<string, object>;
            }
            return null;
        }

        /// <summary>Tre numeri da un campo del JSON, o null se non ci sono.</summary>
        private static double[] Triplet(Dictionary<string, object> map, string key)
        {
            if (map == null || !map.TryGetValue(key, out var v) || v == null) return null;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return null;
            var outside = new List<double>();
            foreach (var x in list)
            {
                try { outside.Add(Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture)); }
                catch { return null; }
            }
            return outside.Count == 3 ? outside.ToArray() : null;
        }

        /// <summary>
        /// I sei numeri della scatola del pezzo da un campo del JSON
        /// (`{minX,minY,minZ,maxX,maxY,maxZ}`), o null se non ci sono.
        ///
        /// Serve solo a scrivere una frase: vedi
        /// <see cref="Sentences.WhereItWasAndWhereItIs"/>, dove c'e' anche il motivo per
        /// cui questi numeri non entrano in nessuna decisione.
        /// </summary>
        private static double[] Sextuple(Dictionary<string, object> map, string key)
        {
            if (map == null || !map.TryGetValue(key, out var v) || v == null) return null;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return null;
            var outside = new List<double>();
            foreach (var x in list)
            {
                try { outside.Add(Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture)); }
                catch { return null; }
            }
            return outside.Count == 6 ? outside.ToArray() : null;
        }

        /// <summary>
        /// Quali assi del modello corrispondono a X e Y dello schizzo su questa
        /// faccia. Ritorna null se la faccia non e' piana e perpendicolare a un
        /// asse principale — caso in cui rifiutiamo invece di indovinare.
        /// </summary>
        private double[] FaceAxes(IFace2 face, out double offsetM, out string error)
        {
            error = null;
            offsetM = 0;
            try
            {
                var surface = face.IGetSurface();
                if (surface == null || !surface.IsPlane())
                {
                    error = "La faccia non e' piana: questa versione non ci lavora sopra.";
                    return null;
                }
                var plane = surface.PlaneParams as double[];
                if (plane == null || plane.Length < 6)
                {
                    // Servono sei numeri: la normale e un punto sul piano. Con
                    // i soli tre della normale non si sa DOVE sta il piano, e
                    // la terza coordinata del punto finirebbe a zero — cioe'
                    // sul piano che passa per l'origine, non su questa faccia.
                    error = "Non riesco a leggere la posizione della faccia.";
                    return null;
                }
                double ax = Math.Abs(plane[0]), ay = Math.Abs(plane[1]), az = Math.Abs(plane[2]);
                const double almostOne = 0.999;
                // L'ultimo indice e' l'asse della NORMALE: da li' si prende
                // l'offset del piano, cioe' la coordinata che il modello non
                // deve dire perche' e' la faccia stessa a fissarla.
                if (az > almostOne) { offsetM = plane[5]; return new double[] { 0, 1, 2 }; } // normale Z
                if (ay > almostOne) { offsetM = plane[4]; return new double[] { 0, 2, 1 }; } // normale Y
                if (ax > almostOne) { offsetM = plane[3]; return new double[] { 1, 2, 0 }; } // normale X
                error =
                    "La faccia e' inclinata rispetto agli assi principali: in questa " +
                    "versione non ci faccio lavorazioni, perche' le coordinate " +
                    "finirebbero nel punto sbagliato senza dare errore.";
                return null;
            }
            catch (Exception ex)
            {
                error = "Non riesco a leggere la faccia: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Da (x, y) del MODELLO a coordinate di schizzo, in metri.
        ///
        /// PERCHE' SERVE UNA CONVERSIONE VERA
        /// Fino alla 0.24.0 questo metodo riceveva la mappatura degli assi e la
        /// IGNORAVA: restituiva (x, y, 0) come se le coordinate del modello
        /// fossero gia' coordinate di schizzo. Non lo sono. Quando SolidWorks
        /// apre uno schizzo su una faccia sceglie da solo origine e verso degli
        /// assi in base alla normale: per due facce opposte l'asse Y risulta
        /// invertito, e un foro chiesto a (30, 20) usciva a (30, -20) — 40 mm
        /// piu' in la', con la feature verde e nessun avviso.
        ///
        /// La conversione non la indoviniamo noi: la chiediamo a SolidWorks.
        /// ISketch.ModelToSketchTransform E' la trasformazione che lui stesso
        /// ha usato per costruire lo schizzo. (Esiste nella 2021: verificato
        /// per riflessione sull'interop 29.5.1.1, insieme a
        /// IMathUtility.CreatePoint e IMathPoint.MultiplyTransform.)
        ///
        /// Va chiamato con lo schizzo GIA' APERTO: prima di InsertSketch(true)
        /// non c'e' nessuno schizzo attivo da cui leggere la trasformazione.
        /// </summary>
        private double[] PointInSketch(
            IModelDoc2 model,
            double[] axes,
            double offsetM,
            double xMm,
            double yMm,
            out string error)
        {
            error = null;

            // 1. Il punto nel sistema del MODELLO, in metri. Le due coordinate
            //    che il modello linguistico ha dato vanno sui due assi liberi;
            //    la terza e' il piano della faccia, che lui non deve indovinare.
            var inModel = new double[3];
            inModel[(int)axes[0]] = Units.M(xMm);
            inModel[(int)axes[1]] = Units.M(yMm);
            inModel[(int)axes[2]] = offsetM;

            try
            {
                var sketch = model.SketchManager?.ActiveSketch;
                if (sketch == null)
                {
                    error = "Nessuno schizzo aperto: non posso posizionare la lavorazione.";
                    return null;
                }
                var transformation = sketch.ModelToSketchTransform;
                if (transformation == null)
                {
                    error = "SolidWorks non mi da' l'orientamento dello schizzo.";
                    return null;
                }
                var math = _sw.GetMathUtility() as IMathUtility;
                var point = math?.CreatePoint(inModel) as IMathPoint;
                var inSketch = point?.MultiplyTransform(transformation) as IMathPoint;
                var outside = inSketch?.ArrayData as double[];
                if (outside == null || outside.Length < 3)
                {
                    error = "Non sono riuscito a convertire le coordinate nello schizzo.";
                    return null;
                }
                return new[] { outside[0], outside[1], outside[2] };
            }
            catch (Exception ex)
            {
                error = "Conversione delle coordinate non riuscita: " + ex.Message;
                return null;
            }
        }

        private Outcome Succeeded(
            IFeature feature,
            Dictionary<string, object> measurements,
            double[] direction = null)
        {
            string itemName = "";
            try { itemName = feature.Name; } catch { }
            _log($"[agente] creata feature {itemName}");

            // CIO' CHE SI E' MOSSO ARRIVA A CHI LEGGE (voce E8).
            //
            // Qui e' l'unico punto da cui passa OGNI successo di tutte e
            // trentasei le operazioni, comprese quelle nei file `partial`
            // (Fori, Saldatura, Tavola, Materiale). Drenare qui vuol dire che
            // nessuna operazione puo' dimenticarsene.
            //
            // IL DIFETTO CHE QUESTO CHIUDE, ed e' il piu' beffardo di tutti:
            // la 0.26.0 ha costruito il ritrovamento per persist ID e con esso
            // la frase che dice DI QUANTO la faccia si e' spostata — «si e'
            // spostata di 60 mm, da [50, 0, 0] a [110, 0, 0]». Poi l'ha
            // consegnata a `_log`, che in questo ponte finisce su
            // `Console.Error`. E in un server MCP su stdio **stderr non e' la
            // risposta**: il modello che ha scritto il piano e la persona che
            // l'ha confermato non la vedono mai.
            //
            // Risultato misurato: `e2_case_a` esegue il foro su un pezzo lungo
            // il 60% in piu' di quello confermato, sulla faccia giusta, e nella
            // risposta c'e' `succeeded: true` e nient'altro. Il meccanismo
            // scritto apposta per non avere difetti silenziosi ne aveva uno
            // addosso: **aveva misurato lo spostamento e buttato via la
            // misura**.
            //
            // Il canale giusto esisteva gia' ed era vuoto: `Outcome.Measurements`
            // arriva dritto nel JSON della risposta, come fa `note_volume`.
            if (_reanchors.Count > 0)
            {
                if (measurements == null) measurements = new Dictionary<string, object>();
                measurements["reanchors"] = _reanchors.ToArray();
            }

            return new Outcome
            {
                Succeeded = true,
                CreatedFeature = itemName,
                Feature = feature,
                Direction = direction,
                Measurements = measurements,
            };
        }

        /// <summary>
        /// Le facce che sono state ritrovate altrove rispetto a dove il piano
        /// se le aspettava, con scritto di quanto si sono mosse.
        ///
        /// E' un CAMPO e non un parametro: nessuna firma di metodo cambia,
        /// quindi chi chiama per riflessione non se ne accorge. E' la regola
        /// che la 0.25.1 ha imparato a caro prezzo.
        ///
        /// Si svuota all'inizio di ogni operazione, non alla fine: un elenco
        /// che si svuota alla fine resta pieno se l'operazione fallisce, e la
        /// successiva erediterebbe gli spostamenti di quella prima.
        /// </summary>
        private readonly List<string> _reanchors = new List<string>();

        /// <summary>
        /// Registra che una faccia e' stata ritrovata altrove, e lo dice anche
        /// a stderr per chi guarda il ponte lavorare.
        /// </summary>
        private void ReAnchored(string sentence)
        {
            if (string.IsNullOrEmpty(sentence)) return;
            _log("[agente] " + sentence);
            _reanchors.Add(sentence);
        }

        // ─────────────────────────────────────────────────────────────────
        // Riferimenti a passi precedenti
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Il passo citato da un riferimento, oppure null con il motivo.
        ///
        /// Il numero del passo lo ha gia' controllato il server mentre
        /// scriveva il piano; qui si ricontrolla lo stesso, perche' fra quel
        /// controllo e questa riga c'e' tutto il giro di rete, la conferma
        /// dell'utente, e i passi precedenti che possono essere falliti.
        /// </summary>
        /// <summary>
        /// C'e' davvero un riferimento in quel campo?
        ///
        /// GUARDA IL VALORE, NON LA CHIAVE, e la differenza e' costata un
        /// difetto. Un modello che vede due campi alternativi ne emette spesso
        /// tutti e due, riempiendo quello che non usa con `null`. Con
        /// `ContainsKey` un `face_ref: null` accanto a un `face_uid`
        /// perfettamente valido faceva prendere il ramo del riferimento,
        /// buttava via l'identificativo buono, e il passo moriva. Il server
        /// adesso quelle chiavi le toglie dal piano; questo controllo e' la
        /// seconda serratura, per i piani gia' salvati e per il giorno in cui
        /// qualcuno cambia il server.
        /// </summary>
        private static bool HasReference(Dictionary<string, object> a, string theField)
        {
            object raw;
            return a != null && a.TryGetValue(theField, out raw) && raw != null;
        }

        private References.Step CitedStep(
            Dictionary<string, object> a, string theField, out int number, out string reason)
        {
            number = 0;
            // Mai null: un motivo nullo diventa `Failed(null)`, e chi legge
            // trova «Mi sono fermato dopo 1 operazioni: » con il vuoto dopo i
            // due punti. Un fallimento senza ragione scritta e' un fallimento
            // che non si puo' correggere.
            reason = $"Il riferimento «{theField}» non si e' potuto risolvere.";
            object raw;
            if (a == null || !a.TryGetValue(theField, out raw) || raw == null)
            {
                reason =
                    $"Il piano dice di usare «{theField}», ma quel riferimento e' " +
                    "vuoto. Il passo non si puo' eseguire.";
                return null;
            }

            var map = raw as Dictionary<string, object>;
            if (map == null)
            {
                reason = $"Il riferimento «{theField}» non e' leggibile.";
                return null;
            }
            object val;
            if (!map.TryGetValue("step", out val))
            {
                reason = $"Il riferimento «{theField}» non dice a quale passo.";
                return null;
            }
            try { number = Convert.ToInt32(val); }
            catch
            {
                reason = $"Il riferimento «{theField}» ha un numero di passo illeggibile.";
                return null;
            }

            References.Step planStep;
            if (!_stepsDone.TryGetValue(number, out planStep) || planStep == null)
            {
                reason =
                    $"Il riferimento «{theField}» cita il passo {number}, ma di quel " +
                    "passo non ho la geometria: o non e' ancora stato eseguito, " +
                    "o non ha creato niente.";
                return null;
            }
            return planStep;
        }

        /// <summary>
        /// La normale della faccia su cui si e' tagliato: e' la direzione del
        /// taglio, e serve a un passo successivo per sapere quale faccia della
        /// tasca e' il fondo. Null se la faccia non e' piana — in quel caso il
        /// passo dopo non potra' nominarne il fondo, e lo dira'.
        /// </summary>
        private static double[] FaceNormal(IFace2 face)
        {
            try
            {
                return ApiHelpers.NormalIfPlanar(face);
            }
            catch { return null; }
        }

        private static string KindOf(Dictionary<string, object> a, string theField)
        {
            object raw;
            if (a == null || !a.TryGetValue(theField, out raw)) return "";
            var map = raw as Dictionary<string, object>;
            if (map == null) return "";
            object g;
            return map.TryGetValue("kind", out g) ? Convert.ToString(g) ?? "" : "";
        }

        private Outcome Failed(string reason)
        {
            // Un fallimento senza ragione scritta non si puo' correggere.
            // Succedeva davvero: un percorso ritornava il motivo a null, e chi
            // leggeva trovava «Mi sono fermato dopo 1 operazioni: » con il
            // vuoto dopo i due punti. Meglio una frase generica che il nulla.
            if (string.IsNullOrWhiteSpace(reason))
                reason =
                    "L'operazione non e' riuscita, e non ho una ragione da " +
                    "darti: e' un difetto mio. Segnalalo insieme al log.";
            _log("[agente] " + reason);
            return new Outcome { Succeeded = false, Error = reason };
        }

        // ─── lettura dei parametri, senza sorprese ───────────────────────
        private static double Num(Dictionary<string, object> a, string key, double se = 0)
        {
            if (a == null || !a.TryGetValue(key, out var v) || v == null) return se;
            try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return se; }
        }

        /// <summary>
        /// Un numero che DEVE esserci. Fallisce invece di restituire zero.
        ///
        /// Num() sopra inghiotte ogni eccezione e restituisce il valore
        /// predefinito: comodo per «centered» o «fillet», rovinoso per una
        /// coordinata. Uno zero al posto di un valore illeggibile non da'
        /// nessun errore: sposta il foro nell'origine e basta.
        /// </summary>
        private static bool RequiredNum(
            Dictionary<string, object> a, string key, out double val, out string error)
        {
            val = 0;
            error = null;
            object v = null;
            if (a == null || !a.TryGetValue(key, out v) || v == null)
            {
                error = $"Manca il parametro «{key}».";
                return false;
            }
            try
            {
                val = Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                error = $"«{key}» non e' un numero: {v}.";
                return false;
            }
            if (double.IsNaN(val) || double.IsInfinity(val))
            {
                error = $"«{key}» non e' un numero utilizzabile: {v}.";
                return false;
            }
            return true;
        }

        private static bool Bool(Dictionary<string, object> a, string key, bool se)
        {
            if (a == null || !a.TryGetValue(key, out var v) || v == null) return se;
            try { return Convert.ToBoolean(v); }
            catch { return se; }
        }

        private static string Str(Dictionary<string, object> a, string key, string se)
        {
            if (a == null || !a.TryGetValue(key, out var v) || v == null) return se;
            return Convert.ToString(v) ?? se;
        }

        /// <summary>C'e' un elenco non vuoto sotto questa chiave?</summary>
        private static bool HasList(Dictionary<string, object> a, string key)
        {
            if (a == null || !a.TryGetValue(key, out var v) || v == null) return false;
            if (v is string) return false;
            var e = v as System.Collections.IEnumerable;
            if (e == null) return false;
            foreach (var x in e) if (x != null) return true;
            return false;
        }

        /// <summary>
        /// Le facce che un elenco di riferimenti a passi precedenti risolve.
        ///
        /// UN ELENCO DI RIFERIMENTI SINGOLI, e non un genere plurale. Il
        /// vocabolario di <see cref="References"/> risolve una faccia per
        /// volta e rifiuta quando il ruolo non e' univoco — che e' la regola
        /// giusta e non va toccata. Per averne piu' d'una si ripete l'elemento:
        /// e' cio' che fa gia' chi scrive `faces_uid` con tre uid dentro.
        ///
        /// SE UNO SOLO NON SI RISOLVE, FALLISCE TUTTO. Un guscio aperto su due
        /// facce invece che su tre non e' «quasi giusto»: e' un pezzo diverso,
        /// e sarebbe riuscito in silenzio.
        /// </summary>
        private bool FacesFromReferences(
            Dictionary<string, object> a, string theField,
            out List<IFace2> faces, out string reason)
        {
            faces = new List<IFace2>();
            reason = null;

            object raw;
            if (a == null || !a.TryGetValue(theField, out raw) || raw == null)
            {
                reason = "«" + theField + "» e' vuoto.";
                return false;
            }
            var list = raw as System.Collections.IEnumerable;
            if (list == null || raw is string)
            {
                reason = "«" + theField + "» deve essere un elenco di riferimenti a passi.";
                return false;
            }

            int which = 0;
            foreach (var v in list)
            {
                which++;
                var entry = v as Dictionary<string, object>;
                if (entry == null)
                {
                    reason = "Il riferimento n. " + which + " di «" + theField +
                             "» non e' un oggetto {passo, genere}.";
                    return false;
                }

                // Si riusa `CitedStep` costruendo un dizionario di un campo
                // solo: cosi' i controlli sul passo — che esista, che sia
                // precedente, che abbia creato qualcosa — restano quelli, e
                // non se ne scrive una seconda copia che divergera'.
                var one = new Dictionary<string, object> { [theField] = entry };
                int number;
                var planStep = CitedStep(one, theField, out number, out reason);
                if (planStep == null)
                {
                    reason = "Riferimento n. " + which + ": " +
                             (reason ?? "non risolto.");
                    return false;
                }

                IFace2 face;
                if (!References.Face(planStep, KindOf(one, theField), out face, out reason))
                {
                    reason = "Riferimento n. " + which + ": " +
                             (reason ?? "faccia non trovata.");
                    return false;
                }
                faces.Add(face);
            }

            if (faces.Count == 0)
            {
                reason = "«" + theField + "» non ha nessun riferimento.";
                return false;
            }
            return true;
        }

        private static List<string> StringList(Dictionary<string, object> a, string key)
        {
            var outside = new List<string>();
            if (a == null || !a.TryGetValue(key, out var v) || v == null) return outside;
            if (v is System.Collections.IEnumerable e && !(v is string))
            {
                foreach (var x in e) if (x != null) outside.Add(Convert.ToString(x));
            }
            return outside;
        }
    }
}
