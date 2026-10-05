using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LE BOOLEANE FRA CORPI: unisci, sottrai, interseca. (voce GP8)
    ///
    /// Servono agli stampi e alle dime, ai pezzi compositi disegnati come piu'
    /// corpi e poi uniti, e ai pezzi da dividere per la stampa 3D quando non
    /// stanno nel piatto.
    ///
    /// ── DUE `InsertCombineFeature`, E UNA SOLA E' QUELLA GIUSTA ─────────
    ///
    /// Lette per riflessione dall'interop 29.5.1.1:
    ///
    ///   IFeatureManager.InsertCombineFeature(int, Body2, object) -> Feature
    ///   IPartDoc       .InsertCombineFeature(int, object, object) -> Boolean
    ///
    /// Stessa arita', stesso nome, tipi e ritorno diversi, e **non sono l'una
    /// l'evoluzione dell'altra**. Quella su `IPartDoc` torna un booleano: la
    /// feature, se nasce, non e' raggiungibile — e un passo successivo che
    /// volesse citarla non avrebbe niente da citare. Qui si usa quella del
    /// FeatureManager.
    ///
    /// ── L'ENUM COL NOME GIUSTO E' QUELLO SBAGLIATO ──────────────────────
    ///
    /// In `swconst` c'e' `swCombineBodiesOperationType_e`, e i suoi tre valori
    /// si chiamano **Add = 0, Subtract = 1, Common = 2**: i nomi esatti delle
    /// tre booleane. Passati a `InsertCombineFeature` **tornano `null` tutti e
    /// tre**, senza un'eccezione e senza un codice.
    ///
    /// Quello che il metodo vuole e' `swBodyOperationType_e`:
    ///
    ///     SWBODYADD       = 15903     unisci
    ///     SWBODYCUT       = 15902     sottrai
    ///     SWBODYINTERSECT = 15901     interseca
    ///
    /// **Un nome dice come si chiama una costante, non a quale funzione
    /// appartiene.** Era il rischio scritto nella voce — «un valore sbagliato
    /// consuma il corpo sbagliato senza errore» — e la realta' e' stata piu'
    /// mite di cosi': non consuma niente, non fa niente, e non lo dice.
    ///
    /// ── E L'ARRAY VA AVVOLTO, E CONTIENE ANCHE IL PRINCIPALE ────────────
    ///
    /// Misurato su pezzi nuovi, una variante per pezzo (provarle di fila sullo
    /// stesso documento dava risultati incoerenti):
    ///
    ///                              ADD 15903   CUT 15902   INTERSECT 15901
    ///   object[] col solo utensile    NULL        NULL          NULL
    ///   Body2[] tipizzato             NULL      «Abbina1»       NULL
    ///   DispatchWrapper[] utensile    NULL      «Abbina1»       NULL
    ///   DispatchWrapper[] TUTTI E DUE «Abbina1» «Abbina1»     «Abbina1»
    ///
    /// Due cose insieme, e nessuna delle due protesta quando manca:
    ///
    /// 1. **`object[]` di RCW non arriva mai.** Il parametro e' un `VARIANT`
    ///    che vuole un SAFEARRAY di `IDispatch*`, e un `object[]` di oggetti
    ///    COM non lo diventa da solo: ci vuole `DispatchWrapper`.
    /// 2. **L'array deve contenere anche il corpo principale**, non solo gli
    ///    utensili — per `union` e `intersect` senza di lui non succede
    ///    niente. Per `subtract` basterebbe l'utensile, ma metterlo dentro non
    ///    cambia il risultato (600 mm³ in tutti e due i modi), e una regola
    ///    sola vale piu' di tre.
    ///
    /// ── I TRE NUMERI, SU UN CASO CHE LI DISTINGUE ───────────────────────
    ///
    /// Due cubi da 10 sfalsati di 2 in X e 5 in Z, sovrapposti per
    /// 8 x 10 x 5 = 400 mm³:
    ///
    ///     unisci      1000 + 1000 - 400 = 1600 mm³
    ///     sottrai     1000 - 400        =  600 mm³
    ///     interseca                        400 mm³
    ///
    /// Con due cubi uguali e centrati i tre numeri sarebbero stati 1500, 500 e
    /// 500 — e «subtract» e «intersect» non si sarebbero distinti. Un caso in
    /// cui due valori sbagliati danno lo stesso risultato non tabella niente.
    ///
    /// ── E SI CONTANO I CORPI ────────────────────────────────────────────
    ///
    /// Un'unione che fallisce in silenzio lascia **due corpi** dove ne serviva
    /// uno: l'albero cresce uguale, la forma sembra la stessa, e la differenza
    /// si vede solo contando. E' lo stesso controllo che `extrude_on_face`
    /// fa gia' sul suo `Merge`.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// La booleana che si fa salvo avviso contrario (voce Z-G3).
        /// Stesso ragionamento di `DefaultMaterial`: lo schema lo
        /// annuncia, questo lo applica, e il valore sta in un posto solo.
        /// </summary>
        private const string DefaultOperation = "union";

        /// <summary>
        /// Coda del messaggio quando il principale e' il predefinito (il primo
        /// corpo, per posizione) e coincide con un utensile nominato: senza,
        /// «e' anche il corpo principale» suonerebbe come una scelta di chi chiede.
        /// </summary>
        private const string DefaultMain =
            ", il predefinito: il primo corpo, preso per posizione perche' non l'hai " +
            "nominato. Nominalo con «main_body_uid» o «main_body_ref»";

        private Outcome CombineBodies(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var partDoc = model as IPartDoc;
            if (partDoc == null)
                return Failed("Questo documento non e' un pezzo: le booleane si fanno fra corpi.");

            // ── IL PREDEFINITO (voce Z-G3) ───────────────────────────────
            //
            // «union» e' di gran lunga il caso piu' comune: «subtract» e
            // «intersect» si chiedono apposta. Qui il predefinito e' piu'
            // pericoloso che altrove — sbagliare verso non lascia un pezzo
            // del materiale sbagliato, lascia **un pezzo con dentro un buco
            // che nessuno ha chiesto** — e per questo si dichiara, e per
            // questo il caso di banco misura il VOLUME invece di guardare
            // che sia riuscito: fra le tre booleane i tre volumi sono
            // inconfondibili, e «succeeded: true» li darebbe tutti e tre.
            string asRequested = (Str(a, "operation", "") ?? "").Trim().ToLowerInvariant();
            bool assumed = asRequested.Length == 0;
            if (assumed) asRequested = DefaultOperation;
            int typ;
            // `swBodyOperationType_e`, NON `swCombineBodiesOperationType_e`:
            // vedi il commento in testa al file.
            if (asRequested == "union") typ = (int)swBodyOperationType_e.SWBODYADD;
            else if (asRequested == "subtract") typ = (int)swBodyOperationType_e.SWBODYCUT;
            else if (asRequested == "intersect") typ = (int)swBodyOperationType_e.SWBODYINTERSECT;
            else return Failed(
                "Non hai detto che booleana fare, o non la conosco: «" + asRequested +
                "». Ci sono «union», «subtract» e «intersect».");

            var rawItems = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            // ZERO-E-ZERO: `GetBodies2` qui sopra non e' in un try — una maniglia
            // morta solleva e non arriva fin qui — quindi null e' «nessun corpo».
            if (rawItems == null || rawItems.Length < 2)
                return Failed(
                    "Per una booleana ci vogliono almeno due corpi, e questo pezzo ne ha " +
                    (rawItems == null ? 0 : rawItems.Length) + ". I corpi separati nascono con " +
                    "«move_bodies» e la copia, o con un'estrusione che non si fonde.");

            // ── CHI E' IL PRINCIPALE, E CHI GLI VA ADDOSSO ───────────────
            //
            // Nella sottrazione l'ordine CAMBIA IL PEZZO: A meno B non e' B
            // meno A. Il principale si dice, e se non si dice e' il primo —
            // ma allora si scrive nella risposta, perche' chi legge deve
            // sapere quale dei due e' rimasto.
            // ── GLI IDENTIFICATIVI SI RISOLVONO PER FIRMA, NON PER POSIZIONE ──
            //
            // (voce Z-C21.2, rilievo L6-1.) Fino alla libreria 0.136.0 «C2»
            // voleva dire «il secondo di un GetBodies2 fatto adesso», e il
            // parametro `state` non era nemmeno letto.
            // IPartDoc~GetBodies2.html, Remarks: «May vary the order in which
            // bodies are returned». Un taglio fra la lettura dello stato e la
            // booleana rinumera i corpi, e si combinava il corpo sbagliato con
            // `succeeded: true`: e' la voce E4, misurata il 14/9/2026 sulla
            // gemella `move_bodies` e corretta li' con `BodiesFromUid`, che
            // confronta la firma (centro e scatola) letta dal piano con quella di
            // adesso. Qui adesso passa dallo stesso punto.
            //
            // E I CORPI TROVATI SI PASSANO COSI' COME SONO, come fa move_bodies
            // con `firma.Entity`. La prima stesura li ricercava dentro il
            // GetBodies2 qui sopra con ReferenceEquals/Equals: regge solo se
            // SOLIDWORKS da' lo stesso oggetto COM per lo stesso corpo in due
            // chiamate, e non e' misurato (revisione di Z-C21.2). Dove serve
            // sapere se due corpi sono lo stesso — il principale fra gli
            // utensili — si chiede a SOLIDWORKS: ISldWorks~IsSame.html, «Gets
            // whether the two specified objects are the same object», «Return
            // Value Object equality as defined by swObjectEquality», come in
            // StatoTavola.
            //
            // I `_ref` (tabella L6-1): senza, un piano «taglio, poi booleana»
            // non ha nessun modo valido di nominare i corpi — gli uid letti
            // prima del taglio ora si rifiutano per firma, e quelli nuovi il
            // piano non li conosce. Stessa strada di move_bodies:
            // BodiesFromReference.
            string bodyReason;
            Func<object, object, bool> same = (x, y) =>
            {
                if (x == null || y == null) return false;
                if (ReferenceEquals(x, y)) return true;
                try { return _sw.IsSame(x, y) == (int)swObjectEquality.swObjectSame; }
                catch { return false; }
            };
            // Il nome da riportare: l'uid dello STATO se il corpo c'e', altrimenti
            // la posizione, detta come tale — i numeri del GetBodies2 di adesso
            // non sono quelli dello stato (GetBodies2.html, stessa frase).
            Func<object, int, string> nameOf = (body, position) =>
            {
                if (docState != null && docState.Signatures != null)
                    foreach (var kv in docState.Signatures)
                        if (kv.Value != null && kv.Value.Type == "BODY" && same(kv.Value.Entity, body))
                            return kv.Key;
                return position < 0 ? "(corpo senza nome nello stato)"
                                     : "C" + (position + 1) + " (per posizione)";
            };

            bool hasMainRef = HasReference(a, "main_body_ref");
            bool hasToolsRef = HasReference(a, "tool_bodies_ref");
            string mainUid = Str(a, "main_body_uid", null);
            var requested = StringList(a, "tool_bodies_uid");
            if (hasMainRef && !string.IsNullOrWhiteSpace(mainUid))
                return Failed("Hai dato sia «main_body_uid» sia «main_body_ref»: uno nomina " +
                               "un corpo dello stato, l'altro il corpo di un passo. Togli uno dei due.");
            if (hasToolsRef && requested != null && requested.Count > 0)
                return Failed("Hai dato sia «tool_bodies_uid» sia «tool_bodies_ref»: uno nomina " +
                               "corpi dello stato, l'altro quelli di un passo. Togli uno dei due.");

            object main;
            string mainName;
            bool mainByPosition = false;
            if (hasMainRef)
            {
                int howMany;
                var fromStep = BodiesFromReference(a, "main_body_ref", docState, out howMany, out bodyReason);
                if (fromStep == null) return Failed(bodyReason);
                if (fromStep.Length != 1)
                    return Failed("Il passo di «main_body_ref» ha lasciato " + fromStep.Length +
                                   " corpi, e il principale e' uno solo: nominalo con «main_body_uid» " +
                                   "dopo aver riletto il pezzo.");
                main = fromStep[0];
                mainName = nameOf(main, Array.FindIndex(rawItems, g => same(g, main)));
            }
            else if (!string.IsNullOrWhiteSpace(mainUid))
            {
                mainUid = mainUid.Trim();
                var foundItems = BodiesFromUid(docState, new List<string> { mainUid }, a, out bodyReason);
                if (foundItems == null) return Failed(bodyReason);
                main = foundItems[0];
                mainName = mainUid;
            }
            else
            {
                // Il PREDEFINITO resta «il primo», ed e' per posizione: si dice.
                main = rawItems[0];
                mainName = nameOf(rawItems[0], 0);
                mainByPosition = true;
            }

            var toolBodies = new List<object>();
            var toolNames = new List<string>();
            if (hasToolsRef)
            {
                int howMany;
                var fromStep = BodiesFromReference(a, "tool_bodies_ref", docState, out howMany, out bodyReason);
                if (fromStep == null) return Failed(bodyReason);
                foreach (var c in fromStep)
                {
                    if (same(c, main))
                        return Failed("Fra i corpi di «tool_bodies_ref» c'e' anche il corpo principale (" +
                                       mainName + (mainByPosition ? DefaultMain : "") +
                                       "): un corpo non si combina con se stesso.");
                    toolBodies.Add(c);
                    toolNames.Add(nameOf(c, Array.FindIndex(rawItems, g => same(g, c))));
                }
            }
            else if (requested != null && requested.Count > 0)
            {
                foreach (var raw in requested)
                {
                    string u = Convert.ToString(raw).Trim();
                    var foundItems = BodiesFromUid(docState, new List<string> { u }, a, out bodyReason);
                    if (foundItems == null) return Failed(bodyReason);
                    if (same(foundItems[0], main))
                        return Failed(
                            "«" + u + "» e' anche il corpo principale (" + mainName +
                            (mainByPosition ? DefaultMain : "") +
                            "): un corpo non si combina con se stesso.");
                    toolBodies.Add(foundItems[0]);
                    toolNames.Add(u);
                }
            }
            else
            {
                for (int i = 0; i < rawItems.Length; i++)
                {
                    if (same(rawItems[i], main)) continue;
                    toolBodies.Add(rawItems[i]);
                    toolNames.Add(nameOf(rawItems[i], i));
                }
            }
            if (toolBodies.Count == 0)
                return Failed("Non e' rimasto nessun corpo da combinare col principale.");

            double? volumeBefore = VolumeMm3(model);
            int bodiesBefore = CountSolidBodies(model);
            if (volumeBefore == null || bodiesBefore < 0)
                return Failed(VolumeNotReadBefore("combinare i corpi"));

            IFeature feature;
            try
            {
                model.ClearSelection2(true);
                // AVVOLTI, E COL PRINCIPALE DENTRO: vedi il commento in testa.
                // Un `object[]` di oggetti COM non diventa un SAFEARRAY di
                // IDispatch*, e senza il principale nell'elenco «union» e
                // «intersect» non fanno niente — senza dirlo.
                var toPass = new List<DispatchWrapper>();
                toPass.Add(new DispatchWrapper(main));
                foreach (var u in toolBodies) toPass.Add(new DispatchWrapper(u));

                feature = model.FeatureManager.InsertCombineFeature(
                    typ, main as Body2, toPass.ToArray()) as IFeature;
            }
            catch (Exception ex)
            {
                return Failed("La booleana non e' riuscita: " + ex.Message);
            }

            // NULL SENZA UN'ECCEZIONE E SENZA UN CODICE: e' tutto quello che
            // questa API dice quando non le va bene qualcosa. Il motivo piu'
            // frequente e' geometrico — i corpi non si toccano davvero — ma
            // non e' l'unico, e la frase lo dice invece di far credere che sia
            // certa.
            if (feature == null)
                return Failed(
                    "La booleana «" + asRequested + "» non ha creato niente, e SOLIDWORKS " +
                    "non dice perche': questa chiamata risponde «niente» e basta. Di solito " +
                    "i corpi non si toccano davvero: «union» vuole corpi che si " +
                    "compenetrano o si toccano, «subtract» e «intersect» vogliono che si " +
                    "sovrappongano. Guarda gli ingombri con «body_bounding_box».");

            try { model.EditRebuild3(); } catch { }

            double? volumeAfter = VolumeMm3(model);
            int bodiesAfter = CountSolidBodies(model);
            bool reread = volumeAfter.HasValue && bodiesAfter >= 0;

            // UN'UNIONE CHE LASCIA DUE CORPI E' FALLITA IN SILENZIO. La
            // feature c'e', l'albero e' cresciuto, e il pezzo e' un altro.
            //
            // `SWBODYADD`, E FINO ALLA 0.116.0 NO (voce Z-C18). Il confronto era
            // con `swCombineBodiesOperationAdd` (= 0), mentre `tipo` vale 15903:
            // due enum diversi — proprio quelli che il commento in testa a
            // questo file distingue — e la guardia non poteva scattare.
            // Misurato il 25/9/2026: con due corpi lontani SOLIDWORKS non crea
            // la feature affatto (il rifiuto qui sopra), quindi dal banco questa
            // guardia non si fa scattare; resta per la feature che nasce e non
            // fonde, e adesso guarda la cosa giusta.
            if (reread && typ == (int)swBodyOperationType_e.SWBODYADD &&
                bodiesAfter >= bodiesBefore)
                return Failed(
                    "L'unione ha creato la feature ma i corpi sono passati da " + bodiesBefore +
                    " a " + bodiesAfter + ": non si sono fusi. Di solito e' che non si toccano " +
                    "davvero. Non ti confermo un pezzo diverso da quello che ti ho mostrato: " +
                    "ritiralo — " + Sentences.HowToWithdrawIt + ".");

            if (reread && volumeAfter <= 0)
                return Failed(
                    "Dopo la booleana il pezzo non ha piu' volume: «" + asRequested +
                    "» ha consumato tutto. Di solito e' l'ordine — nella sottrazione il " +
                    "corpo principale e' quello che RESTA.");

            var measurements = new Dictionary<string, object>
            {
                ["operation"] = asRequested,
                ["note_assumption"] = assumed
                    ? "operazione: «" + DefaultOperation + "» — PREDEFINITO, " +
                      "non l'hai chiesto tu. «subtract» e «intersect» si chiedono " +
                      "per nome."
                    : "operazione: «" + asRequested + "» — l'hai chiesto tu.",
                ["main_body"] = mainName,
                ["tool_bodies"] = toolNames.ToArray(),
                ["bodies_before"] = bodiesBefore,
                ["bodies"] = NumberOrNull(bodiesAfter),
                ["volume_before_mm3"] = NumberOrNull(volumeBefore, 2),
                ["volume_mm3"] = NumberOrNull(volumeAfter, 2),
                ["volume_changed_mm3"] = NumberOrNull(volumeAfter - volumeBefore, 2),
                // GLI IDENTIFICATIVI NON VALGONO PIU', e si dice sempre: una
                // booleana rifa' le facce da capo.
                ["note_identifiers"] =
                    "I corpi e le facce sono cambiati: gli identificativi (C2, F1, E4) di " +
                    "prima non valgono piu'. Rileggi il pezzo prima del passo seguente.",
            };
            if (!reread) measurements["volume_not_reread"] = NoteVolumeNotReread;
            _log("[agente] " + asRequested + ": corpi " + bodiesBefore + " -> " + bodiesAfter +
                 ", volume " + ReadValue(volumeBefore) + " -> " + ReadValue(volumeAfter));
            return Succeeded(feature, measurements);
        }

        // =================================================================
        // IL RACCORDO A RAGGIO VARIABILE  (voce GP7)
        // =================================================================
        //
        // Serve a smussare gradualmente uno spigolo lungo: grande dove il
        // pezzo deve incassare un carico, piccolo dove non c'e' posto.
        //
        // ── LA RICETTA, MISURATA E NON DEDOTTA ──────────────────────────
        //
        // La voce avvisava che «il popolamento di Radii / PointRadiusArray /
        // PointRhoArray (ordine, quanti valori per vertice) non e' verificato:
        // un passo sbagliato da' raggi plausibili e sbagliati». Provato su un
        // cubo 40x40x40, spigolo verticale, raggi 3 e 8:
        //
        //   spigolo + i suoi 2 vertici selezionati, Radii = {3, 8}   277,7 mm³
        //   SOLO i due vertici (senza lo spigolo)                      NULL
        //   con le marche 1/1/1 invece che senza marca               277,7 mm³
        //   aggiungendo PointRadiusArray = {3, 8}                    277,7 mm³
        //
        // Quindi: **lo spigolo va selezionato**, i vertici pure, le marche non
        // contano e `PointRadiusArray` non serve — quello e' per i punti
        // INTERMEDI, che qui non ci sono.
        //
        // ── E IL NUMERO CHE DISTINGUE IL VARIABILE DAL COSTANTE ─────────
        //
        // Il volume tolto da un raccordo di raggio r su uno spigolo lungo L e'
        // L·r²·(1 − π/4). Per un raggio che va da 3 a 8, la media di r² e'
        // (9 + 24 + 64)/3 = 32,33, e il conto da' **277,5 mm³** — misurato
        // 277,7, cioe' il blend non e' esattamente una spazzata lineare del
        // raggio ma ci sta dentro per lo 0,07%.
        //
        // I due numeri sbagliati sono lontani: un raccordo costante da 3 ne
        // toglierebbe **77,3**, uno costante da 8 **549,4**. E' cosi' che si
        // vede se ha fatto il variabile o se ha preso solo il primo raggio.
        // =================================================================

        private Outcome VariableFillet(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string uid = Str(a, "edge_uid", null);
            if (string.IsNullOrWhiteSpace(uid))
                return Failed(
                    "Non hai detto su quale spigolo. Gli identificativi degli spigoli " +
                    "stanno nello stato del documento, e somigliano a «E7».");

            double r1 = Num(a, "start_radius_mm");
            double r2 = Num(a, "end_radius_mm");
            if (r1 <= 0 || r2 <= 0)
                return Failed(
                    "I due raggi devono essere tutti e due maggiori di zero: hai chiesto " +
                    r1.ToString("0.##") + " e " + r2.ToString("0.##") + ". Per un raccordo " +
                    "uguale da capo a fondo c'e' «fillet_edges».");

            string offset;
            var signature = EdgeSignature(
                a, docState, uid, "Lo spigolo «" + uid + "» non c'e' piu' in questo documento.",
                out offset);
            if (signature == null) return Failed(offset);

            var edge = signature.Entity as IEdge;
            if (edge == null)
                return Failed("Lo spigolo «" + uid + "» non e' piu' utilizzabile.");

            // IL RAGGIO PIU' GRANDE DEVE STARCI, e si misura adesso: fra la
            // proposta e l'esecuzione il pezzo puo' essere cambiato. E' lo
            // stesso controllo del raccordo costante, sul piu' grande dei due.
            double maxValue = Math.Max(r1, r2);
            if (signature.Measure > 0 && maxValue > signature.Measure / 2)
                return Failed(string.Format(
                    "{0:0.##} mm e' troppo per lo spigolo «{1}», lungo {2:0.##} mm: non ci " +
                    "sta neanche meta'.", maxValue, uid, signature.Measure));

            var v1 = edge.GetStartVertex() as IEntity;
            var v2 = edge.GetEndVertex() as IEntity;
            if (v1 == null || v2 == null)
                return Failed(
                    "Lo spigolo «" + uid + "» non ha due vertici: e' chiuso su se stesso " +
                    "(un cerchio), e un raccordo variabile ha bisogno di due estremi fra " +
                    "cui variare.");

            double? volumeBefore = VolumeMm3(model);
            if (volumeBefore == null) return Failed(VolumeNotReadBefore("fare il raccordo"));

            IFeature feature;
            try
            {
                model.ClearSelection2(true);
                // LO SPIGOLO PRIMA, POI I DUE VERTICI. Senza lo spigolo la
                // chiamata torna null: misurato.
                if (!(edge as IEntity).Select4(true, null))
                    return Failed("Non sono riuscito a selezionare lo spigolo «" + uid + "».");
                if (!v1.Select4(true, null) || !v2.Select4(true, null))
                    return Failed(
                        "Non sono riuscito a selezionare i due vertici dello spigolo «" +
                        uid + "»: senza di loro il raggio non ha dove variare.");

                feature = model.FeatureManager.FeatureFillet3(
                    Options: (int)(swFeatureFilletOptions_e.swFeatureFilletVarRadiusType |
                                   swFeatureFilletOptions_e.swFeatureFilletPropagate),
                    // R1 IS NOT READ FOR A VARIABLE RADIUS (Z-C21.8, finding M37):
                    // IFeatureManager~FeatureFillet3.html, «R1 Uniform radius of
                    // the symmetric fillet; valid only if: Ftyp !=
                    // swFeatureFilletType_e.swFeatureFilletType_VariableRadius».
                    // The radii are the two of `Radii` below. The Z-C21.3 probe of
                    // L7-7 measured 277.694 mm3 with R1 = the first radius; the
                    // bench case fillet_that_changes_radius (277.7) is the check
                    // that R1 = 0, as the page asks, changes nothing.
                    R1: 0,
                    R2: 0,
                    Rho: 0,
                    Ftyp: (int)swFeatureFilletType_e.swFeatureFilletType_VariableRadius,
                    OverflowType: (int)swFilletOverFlowType_e.swFilletOverFlowType_Default,
                    ConicRhoType: (int)swFeatureFilletProfileType_e.swFeatureFilletCircular,
                    // UNO PER VERTICE, nell'ordine in cui sono stati
                    // selezionati: inizio e fine.
                    Radii: new double[] { Units.M(r1), Units.M(r2) },
                    Dist2Arr: null,
                    RhoArr: null,
                    SetBackDistances: null,
                    // I PUNTI INTERMEDI NON CI SONO, e `PointRadiusArray` e'
                    // per quelli: passarlo non cambia niente (misurato), e
                    // passarlo per abitudine sarebbe un argomento che non si
                    // sa cosa fa.
                    PointRadiusArray: null,
                    PointDist2Array: null,
                    PointRhoArray: null) as IFeature;
            }
            catch (Exception ex) { return Failed("Il raccordo variabile non e' riuscito: " + ex.Message); }

            if (feature == null)
                return Failed(
                    "Il raccordo variabile non e' stato creato, e SOLIDWORKS non dice " +
                    "perche'. Di solito e' il raggio: anche se ci sta sullo spigolo, puo' " +
                    "non starci sulle facce che gli stanno intorno.");

            try { model.EditRebuild3(); } catch { }
            double? volumeAfter = VolumeMm3(model);
            double? removed = volumeBefore - volumeAfter;

            // UN RACCORDO CHE NON TOGLIE NIENTE NON E' UN RACCORDO. La feature
            // c'e', l'albero e' cresciuto, e lo spigolo e' ancora vivo. Si dice
            // solo di un volume riletto (voce Z-C8).
            if (removed <= 0)
                return Failed(
                    "Il raccordo e' stato creato ma non ha tolto niente (volume " +
                    volumeBefore.Value.ToString("0.##") + " mm3 prima e dopo). Lo spigolo e' " +
                    "ancora vivo: non ti confermo un pezzo diverso da quello che ti ho " +
                    "mostrato — " + Sentences.HowToWithdrawIt + ".");

            return Succeeded(feature, new Dictionary<string, object>
            {
                ["edge"] = uid,
                ["radius_start_mm"] = r1,
                ["radius_end_mm"] = r2,
                ["material_removed_mm3"] = NumberOrNull(removed, 2),
                ["volume_not_reread"] = removed.HasValue ? null : NoteVolumeNotReread,
                // IL CONTO A MANO ACCANTO, e non e' ridondanza: distingue «ha
                // fatto il variabile» da «ha preso solo il primo raggio».
                // Il blend non e' esattamente una spazzata lineare, quindi e'
                // un ORDINE DI GRANDEZZA, ed e' scritto che lo e'.
                ["expected_removed_mm3"] = signature.Measure > 0
                    ? (object)Math.Round(
                        signature.Measure * ((r1 * r1 + r1 * r2 + r2 * r2) / 3.0) * (1 - Math.PI / 4), 2)
                    : null,
                ["how_it_is_counted"] =
                    "lunghezza per la media di r^2 fra i due raggi, per (1 - pi/4). E' un " +
                    "ordine di grandezza: il raccordo vero non e' esattamente una spazzata " +
                    "lineare del raggio.",
            });
        }

        // =================================================================
        // LA FILETTATURA COSMETICA  (voce GP11)
        // =================================================================
        //
        // NON TAGLIA NIENTE: e' un'annotazione. Dice «qui c'e' un filetto» a
        // chi legge il pezzo e alla tavola, e il volume resta quello di prima.
        // Se il volume cambia si e' chiamata la cosa sbagliata, e questa
        // operazione lo controlla.
        //
        // ── SI ATTACCA A UNO SPIGOLO, NON A UNA FACCIA ──────────────────
        //
        // Sulla faccia cilindrica `InsertCosmeticThread3` torna **null** in
        // tutte le combinazioni provate. A dirlo e' stato il modello dei dati:
        // `ICosmeticThreadFeatureData` ha una proprieta' **`Edge`**. Con lo
        // spigolo CIRCOLARE selezionato funziona al primo colpo.
        //
        // ── E IL CALLOUT NON E' GENERATO: E' RICOPIATO ──────────────────
        //
        // La voce GP11 diceva: «la nota generata, riletta, dice M6x1 — e' il
        // solo modo di sapere se `Standard` era il numero giusto». Misurato il
        // 16/9/2026, la risposta e' che **`Standard` non e' nessun numero**:
        //
        //   Standard = 0, 1, 2, 3, 8 (ISO), con StandardType «Tapped hole»,
        //   «Machine Threads», «Maschiatura», «Filettature macchina», e Size
        //   «M6x1.0», «M6», «M6x1»:
        //       la feature nasce sempre, e rileggendola
        //       Standard = -2,  StandardType = «»,  Size = «»,  callout = «»
        //
        //   passando invece la NOTA:
        //       callout = «M6x1»   — anche con Standard = -2 e i due nomi vuoti
        //
        // Cioe': lo standard e la taglia **vengono ignorati**, e il callout e'
        // esattamente il testo che gli si da'. Quindi l'operazione chiede una
        // denominazione e la scrive, e **non finge** che venga da una tabella:
        // «generata» e «ricopiata» non sono la stessa cosa, e chi legge una
        // tavola ha diritto di sapere quale delle due sta guardando.
        // =================================================================

        private Outcome CosmeticThread(
            IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            string uid = Str(a, "edge_uid", null);
            if (string.IsNullOrWhiteSpace(uid))
                return Failed(
                    "Non hai detto su quale spigolo. Il filetto cosmetico si attacca allo " +
                    "spigolo CIRCOLARE di un cilindro o di un foro — non alla faccia — e " +
                    "gli identificativi degli spigoli stanno nello stato del documento.");

            string designation = Str(a, "designation", null);
            if (string.IsNullOrWhiteSpace(designation))
                return Failed(
                    "Manca la «designation», per esempio «M6x1». Un filetto cosmetico " +
                    "senza denominazione e' un'annotazione che non dice niente: sulla " +
                    "tavola comparirebbe un filetto senza misura, e chi lo legge dovrebbe " +
                    "indovinarla. Il testo lo scrivo come me lo dai: SOLIDWORKS non lo " +
                    "ricava da una tabella.");

            string offset;
            var signature = EdgeSignature(
                a, docState, uid, "Lo spigolo «" + uid + "» non c'e' piu' in questo documento.",
                out offset);
            if (signature == null) return Failed(offset);

            var edge = signature.Entity as IEdge;
            if (edge == null)
                return Failed("Lo spigolo «" + uid + "» non e' piu' utilizzabile.");

            double diameter = 0;
            bool round = false;
            try
            {
                var curva = edge.IGetCurve();
                round = curva != null && curva.IsCircle();
                if (round)
                {
                    var p = curva.CircleParams as double[];
                    if (p != null && p.Length >= 7) diameter = p[6] * 2000.0;
                }
            }
            catch { }

            if (!round)
                return Failed(
                    "Lo spigolo «" + uid + "» non e' un cerchio: un filetto cosmetico si " +
                    "attacca al bordo tondo di un cilindro o di un foro. Su uno spigolo " +
                    "dritto non c'e' niente da filettare.");

            // IL DIAMETRO LO DICE LO SPIGOLO, non chi chiama: e' il bordo su
            // cui il filetto si attacca, e un diametro diverso da quello
            // disegnerebbe un filetto che non sta sul pezzo.
            double requestedDiameter = Num(a, "diameter_mm", 0);
            if (requestedDiameter > 0 && diameter > 0 &&
                Math.Abs(requestedDiameter - diameter) > 0.01)
                return Failed(string.Format(
                    "Hai chiesto un filetto da {0:0.##} mm, ma lo spigolo «{1}» e' un " +
                    "cerchio da {2:0.##}. Il filetto si attacca a quel bordo: o e' quello " +
                    "giusto, o e' l'altro spigolo.", requestedDiameter, uid, diameter));
            if (diameter <= 0) diameter = requestedDiameter;
            if (diameter <= 0)
                return Failed("Non riesco a misurare il diametro dello spigolo «" + uid + "».");

            double depth = Num(a, "depth_mm", 0);
            bool throughAll = Bool(a, "through_all", false);
            if (!throughAll && depth <= 0)
                return Failed(
                    "Dimmi quanto e' profondo il filetto («depth_mm») oppure che e' " +
                    "«through_all».");

            double? volumeBefore = VolumeMm3(model);
            if (volumeBefore == null) return Failed(VolumeNotReadBefore("mettere il filetto"));

            IFeature feature;
            try
            {
                model.ClearSelection2(true);
                if (!(edge as IEntity).Select4(false, null))
                    return Failed("Non sono riuscito a selezionare lo spigolo «" + uid + "».");

                feature = model.FeatureManager.InsertCosmeticThread3(
                    // Standard e taglia SONO IGNORATI da questa chiamata
                    // (misurato): si passa «nessuno standard» invece di
                    // fingere che un numero serva a qualcosa.
                    Standard: -2,
                    StandardType: "",
                    Size: "",
                    Diameter: Units.M(diameter),
                    EndType: throughAll
                        ? (int)swCosmeticThreadType_e.swApplyCosmeticThread_ThroughFeature
                        : (int)swCosmeticThreadType_e.swApplyCosmeticThread_Blind,
                    Depth: throughAll ? 0 : Units.M(depth),
                    Note: designation) as IFeature;
            }
            catch (Exception ex) { return Failed("Il filetto cosmetico non e' riuscito: " + ex.Message); }

            if (feature == null)
                return Failed(
                    "Il filetto cosmetico non e' stato creato, e SOLIDWORKS non dice " +
                    "perche'. Controlla che lo spigolo «" + uid + "» sia il bordo di una " +
                    "faccia cilindrica.");

            try { model.EditRebuild3(); } catch { }

            // E' COSMETICA: se il volume e' cambiato, si e' tagliato davvero.
            double? volumeAfter = VolumeMm3(model);
            if (volumeAfter.HasValue && Math.Abs(volumeAfter.Value - volumeBefore.Value) > 0.01)
                return Failed(string.Format(
                    "Il filetto doveva essere COSMETICO e il volume e' cambiato: {0:0.##} " +
                    "mm3 prima, {1:0.##} dopo. Qualcosa ha tagliato davvero, e non e' " +
                    "quello che hai chiesto — ritiralo: " + Sentences.HowToWithdrawIt + ".",
                    volumeBefore, volumeAfter));

            string featureType = "";
            try { featureType = feature.GetTypeName2(); } catch { }
            string callout = "";
            try
            {
                var data = feature.GetDefinition() as ICosmeticThreadFeatureData;
                if (data != null) callout = data.ThreadCallout ?? "";
            }
            catch { }

            // UN'ANNOTAZIONE CHE NON DICE NIENTE non e' un'annotazione: la
            // feature ci sarebbe, la tavola mostrerebbe un filetto muto.
            if (string.IsNullOrEmpty(callout))
                return Failed(
                    "Il filetto e' stato creato ma la sua denominazione e' rimasta vuota: " +
                    "sulla tavola comparirebbe un filetto senza misura. Ritiralo — " +
                    Sentences.HowToWithdrawIt + ".");

            return Succeeded(feature, new Dictionary<string, object>
            {
                ["edge"] = uid,
                ["designation"] = callout,
                ["diameter_mm"] = Math.Round(diameter, 3),
                ["depth_mm"] = throughAll ? (object)null : depth,
                ["through_all"] = throughAll,
                ["feature_kind"] = featureType,
                ["volume_mm3"] = NumberOrNull(volumeAfter, 2),
                ["cuts_nothing"] = volumeAfter.HasValue
                    ? "E' un'annotazione: il volume del pezzo e' lo stesso di prima. Se ti " +
                      "serve il filetto VERO, tagliato, questa non e' l'operazione."
                    : NoteVolumeNotReread,
                // SI DICHIARA DA DOVE VIENE IL TESTO. Vedi il commento in testa.
                ["designation_is_yours"] =
                    "«" + callout + "» e' il testo che mi hai dato, non uno ricavato da una " +
                    "tabella di filettature: questa chiamata ignora lo standard e la taglia. " +
                    "Se e' sbagliato, sulla tavola finisce sbagliato.",
            });
        }

        // `IndiceDelCorpo` («C2» -> indice 1 di un GetBodies2 nuovo) e' stato
        // tolto con la voce Z-C21.2: era la risoluzione per posizione che il
        // rilievo L6-1 ha trovato sbagliata. Gli uid dei corpi passano da
        // `BodiesFromUid` (WeldmentOperations.cs), che guarda la firma.
    }
}
