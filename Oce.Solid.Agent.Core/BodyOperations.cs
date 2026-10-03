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
    public partial class OperazioniSw
    {
        /// <summary>
        /// La booleana che si fa salvo avviso contrario (voce Z-G3).
        /// Stesso ragionamento di `MaterialePredefinito`: lo schema lo
        /// annuncia, questo lo applica, e il valore sta in un posto solo.
        /// </summary>
        private const string OperazionePredefinita = "union";

        /// <summary>
        /// Coda del messaggio quando il principale e' il predefinito (il primo
        /// corpo, per posizione) e coincide con un utensile nominato: senza,
        /// «e' anche il corpo principale» suonerebbe come una scelta di chi chiede.
        /// </summary>
        private const string PrincipalePredefinito =
            ", il predefinito: il primo corpo, preso per posizione perche' non l'hai " +
            "nominato. Nominalo con «main_body_uid» o «main_body_ref»";

        private Esito CombinaCorpi(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var parte = modello as IPartDoc;
            if (parte == null)
                return Fallito("Questo documento non e' un pezzo: le booleane si fanno fra corpi.");

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
            string comeChiesta = (Str(a, "operation", "") ?? "").Trim().ToLowerInvariant();
            bool assunta = comeChiesta.Length == 0;
            if (assunta) comeChiesta = OperazionePredefinita;
            int tipo;
            // `swBodyOperationType_e`, NON `swCombineBodiesOperationType_e`:
            // vedi il commento in testa al file.
            if (comeChiesta == "union") tipo = (int)swBodyOperationType_e.SWBODYADD;
            else if (comeChiesta == "subtract") tipo = (int)swBodyOperationType_e.SWBODYCUT;
            else if (comeChiesta == "intersect") tipo = (int)swBodyOperationType_e.SWBODYINTERSECT;
            else return Fallito(
                "Non hai detto che booleana fare, o non la conosco: «" + comeChiesta +
                "». Ci sono «union», «subtract» e «intersect».");

            var grezzi = parte.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            // ZERO-E-ZERO: `GetBodies2` qui sopra non e' in un try — una maniglia
            // morta solleva e non arriva fin qui — quindi null e' «nessun corpo».
            if (grezzi == null || grezzi.Length < 2)
                return Fallito(
                    "Per una booleana ci vogliono almeno due corpi, e questo pezzo ne ha " +
                    (grezzi == null ? 0 : grezzi.Length) + ". I corpi separati nascono con " +
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
            // gemella `move_bodies` e corretta li' con `CorpiDaUid`, che
            // confronta la firma (centro e scatola) letta dal piano con quella di
            // adesso. Qui adesso passa dallo stesso punto.
            //
            // E I CORPI TROVATI SI PASSANO COSI' COME SONO, come fa move_bodies
            // con `firma.Entita`. La prima stesura li ricercava dentro il
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
            // CorpiDalRiferimento.
            string motivoCorpo;
            Func<object, object, bool> stesso = (x, y) =>
            {
                if (x == null || y == null) return false;
                if (ReferenceEquals(x, y)) return true;
                try { return _sw.IsSame(x, y) == (int)swObjectEquality.swObjectSame; }
                catch { return false; }
            };
            // Il nome da riportare: l'uid dello STATO se il corpo c'e', altrimenti
            // la posizione, detta come tale — i numeri del GetBodies2 di adesso
            // non sono quelli dello stato (GetBodies2.html, stessa frase).
            Func<object, int, string> nomeDi = (corpo, posizione) =>
            {
                if (stato != null && stato.Firme != null)
                    foreach (var kv in stato.Firme)
                        if (kv.Value != null && kv.Value.Tipo == "BODY" && stesso(kv.Value.Entita, corpo))
                            return kv.Key;
                return posizione < 0 ? "(corpo senza nome nello stato)"
                                     : "C" + (posizione + 1) + " (per posizione)";
            };

            bool haPrincipaleRif = HaRiferimento(a, "main_body_ref");
            bool haUtensiliRif = HaRiferimento(a, "tool_bodies_ref");
            string uidPrincipale = Str(a, "main_body_uid", null);
            var chiesti = Lista(a, "tool_bodies_uid");
            if (haPrincipaleRif && !string.IsNullOrWhiteSpace(uidPrincipale))
                return Fallito("Hai dato sia «main_body_uid» sia «main_body_ref»: uno nomina " +
                               "un corpo dello stato, l'altro il corpo di un passo. Togli uno dei due.");
            if (haUtensiliRif && chiesti != null && chiesti.Count > 0)
                return Fallito("Hai dato sia «tool_bodies_uid» sia «tool_bodies_ref»: uno nomina " +
                               "corpi dello stato, l'altro quelli di un passo. Togli uno dei due.");

            object principale;
            string nomePrincipale;
            bool principalePerPosizione = false;
            if (haPrincipaleRif)
            {
                int quanti;
                var daPasso = CorpiDalRiferimento(a, "main_body_ref", stato, out quanti, out motivoCorpo);
                if (daPasso == null) return Fallito(motivoCorpo);
                if (daPasso.Length != 1)
                    return Fallito("Il passo di «main_body_ref» ha lasciato " + daPasso.Length +
                                   " corpi, e il principale e' uno solo: nominalo con «main_body_uid» " +
                                   "dopo aver riletto il pezzo.");
                principale = daPasso[0];
                nomePrincipale = nomeDi(principale, Array.FindIndex(grezzi, g => stesso(g, principale)));
            }
            else if (!string.IsNullOrWhiteSpace(uidPrincipale))
            {
                uidPrincipale = uidPrincipale.Trim();
                var trovati = CorpiDaUid(stato, new List<string> { uidPrincipale }, a, out motivoCorpo);
                if (trovati == null) return Fallito(motivoCorpo);
                principale = trovati[0];
                nomePrincipale = uidPrincipale;
            }
            else
            {
                // Il PREDEFINITO resta «il primo», ed e' per posizione: si dice.
                principale = grezzi[0];
                nomePrincipale = nomeDi(grezzi[0], 0);
                principalePerPosizione = true;
            }

            var utensili = new List<object>();
            var nomiUtensili = new List<string>();
            if (haUtensiliRif)
            {
                int quanti;
                var daPasso = CorpiDalRiferimento(a, "tool_bodies_ref", stato, out quanti, out motivoCorpo);
                if (daPasso == null) return Fallito(motivoCorpo);
                foreach (var c in daPasso)
                {
                    if (stesso(c, principale))
                        return Fallito("Fra i corpi di «tool_bodies_ref» c'e' anche il corpo principale (" +
                                       nomePrincipale + (principalePerPosizione ? PrincipalePredefinito : "") +
                                       "): un corpo non si combina con se stesso.");
                    utensili.Add(c);
                    nomiUtensili.Add(nomeDi(c, Array.FindIndex(grezzi, g => stesso(g, c))));
                }
            }
            else if (chiesti != null && chiesti.Count > 0)
            {
                foreach (var grezzo in chiesti)
                {
                    string u = Convert.ToString(grezzo).Trim();
                    var trovati = CorpiDaUid(stato, new List<string> { u }, a, out motivoCorpo);
                    if (trovati == null) return Fallito(motivoCorpo);
                    if (stesso(trovati[0], principale))
                        return Fallito(
                            "«" + u + "» e' anche il corpo principale (" + nomePrincipale +
                            (principalePerPosizione ? PrincipalePredefinito : "") +
                            "): un corpo non si combina con se stesso.");
                    utensili.Add(trovati[0]);
                    nomiUtensili.Add(u);
                }
            }
            else
            {
                for (int i = 0; i < grezzi.Length; i++)
                {
                    if (stesso(grezzi[i], principale)) continue;
                    utensili.Add(grezzi[i]);
                    nomiUtensili.Add(nomeDi(grezzi[i], i));
                }
            }
            if (utensili.Count == 0)
                return Fallito("Non e' rimasto nessun corpo da combinare col principale.");

            double? volumePrima = VolumeMm3(modello);
            int corpiPrima = ContaCorpiSolidi(modello);
            if (volumePrima == null || corpiPrima < 0)
                return Fallito(VolumeNonLettoPrima("combinare i corpi"));

            IFeature feature;
            try
            {
                modello.ClearSelection2(true);
                // AVVOLTI, E COL PRINCIPALE DENTRO: vedi il commento in testa.
                // Un `object[]` di oggetti COM non diventa un SAFEARRAY di
                // IDispatch*, e senza il principale nell'elenco «union» e
                // «intersect» non fanno niente — senza dirlo.
                var daPassare = new List<DispatchWrapper>();
                daPassare.Add(new DispatchWrapper(principale));
                foreach (var u in utensili) daPassare.Add(new DispatchWrapper(u));

                feature = modello.FeatureManager.InsertCombineFeature(
                    tipo, principale as Body2, daPassare.ToArray()) as IFeature;
            }
            catch (Exception ex)
            {
                return Fallito("La booleana non e' riuscita: " + ex.Message);
            }

            // NULL SENZA UN'ECCEZIONE E SENZA UN CODICE: e' tutto quello che
            // questa API dice quando non le va bene qualcosa. Il motivo piu'
            // frequente e' geometrico — i corpi non si toccano davvero — ma
            // non e' l'unico, e la frase lo dice invece di far credere che sia
            // certa.
            if (feature == null)
                return Fallito(
                    "La booleana «" + comeChiesta + "» non ha creato niente, e SOLIDWORKS " +
                    "non dice perche': questa chiamata risponde «niente» e basta. Di solito " +
                    "i corpi non si toccano davvero: «union» vuole corpi che si " +
                    "compenetrano o si toccano, «subtract» e «intersect» vogliono che si " +
                    "sovrappongano. Guarda gli ingombri con «body_bounding_box».");

            try { modello.EditRebuild3(); } catch { }

            double? volumeDopo = VolumeMm3(modello);
            int corpiDopo = ContaCorpiSolidi(modello);
            bool riletto = volumeDopo.HasValue && corpiDopo >= 0;

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
            if (riletto && tipo == (int)swBodyOperationType_e.SWBODYADD &&
                corpiDopo >= corpiPrima)
                return Fallito(
                    "L'unione ha creato la feature ma i corpi sono passati da " + corpiPrima +
                    " a " + corpiDopo + ": non si sono fusi. Di solito e' che non si toccano " +
                    "davvero. Non ti confermo un pezzo diverso da quello che ti ho mostrato: " +
                    "ritiralo — " + Frasi.CosiSiRitira + ".");

            if (riletto && volumeDopo <= 0)
                return Fallito(
                    "Dopo la booleana il pezzo non ha piu' volume: «" + comeChiesta +
                    "» ha consumato tutto. Di solito e' l'ordine — nella sottrazione il " +
                    "corpo principale e' quello che RESTA.");

            var misure = new Dictionary<string, object>
            {
                ["operation"] = comeChiesta,
                ["note_assumption"] = assunta
                    ? "operazione: «" + OperazionePredefinita + "» — PREDEFINITO, " +
                      "non l'hai chiesto tu. «subtract» e «intersect» si chiedono " +
                      "per nome."
                    : "operazione: «" + comeChiesta + "» — l'hai chiesto tu.",
                ["main_body"] = nomePrincipale,
                ["tool_bodies"] = nomiUtensili.ToArray(),
                ["bodies_before"] = corpiPrima,
                ["bodies"] = NumeroOppureNull(corpiDopo),
                ["volume_before_mm3"] = NumeroOppureNull(volumePrima, 2),
                ["volume_mm3"] = NumeroOppureNull(volumeDopo, 2),
                ["volume_changed_mm3"] = NumeroOppureNull(volumeDopo - volumePrima, 2),
                // GLI IDENTIFICATIVI NON VALGONO PIU', e si dice sempre: una
                // booleana rifa' le facce da capo.
                ["note_identifiers"] =
                    "I corpi e le facce sono cambiati: gli identificativi (C2, F1, E4) di " +
                    "prima non valgono piu'. Rileggi il pezzo prima del passo seguente.",
            };
            if (!riletto) misure["volume_not_reread"] = NotaVolumeNonRiletto;
            _log("[agente] " + comeChiesta + ": corpi " + corpiPrima + " -> " + corpiDopo +
                 ", volume " + Letto(volumePrima) + " -> " + Letto(volumeDopo));
            return Riuscito(feature, misure);
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

        private Esito RaccordoVariabile(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string uid = Str(a, "edge_uid", null);
            if (string.IsNullOrWhiteSpace(uid))
                return Fallito(
                    "Non hai detto su quale spigolo. Gli identificativi degli spigoli " +
                    "stanno nello stato del documento, e somigliano a «E7».");

            double r1 = Num(a, "start_radius_mm");
            double r2 = Num(a, "end_radius_mm");
            if (r1 <= 0 || r2 <= 0)
                return Fallito(
                    "I due raggi devono essere tutti e due maggiori di zero: hai chiesto " +
                    r1.ToString("0.##") + " e " + r2.ToString("0.##") + ". Per un raccordo " +
                    "uguale da capo a fondo c'e' «fillet_edges».");

            string scostamento;
            var firma = FirmaDelloSpigolo(
                a, stato, uid, "Lo spigolo «" + uid + "» non c'e' piu' in questo documento.",
                out scostamento);
            if (firma == null) return Fallito(scostamento);

            var spigolo = firma.Entita as IEdge;
            if (spigolo == null)
                return Fallito("Lo spigolo «" + uid + "» non e' piu' utilizzabile.");

            // IL RAGGIO PIU' GRANDE DEVE STARCI, e si misura adesso: fra la
            // proposta e l'esecuzione il pezzo puo' essere cambiato. E' lo
            // stesso controllo del raccordo costante, sul piu' grande dei due.
            double massimo = Math.Max(r1, r2);
            if (firma.Misura > 0 && massimo > firma.Misura / 2)
                return Fallito(string.Format(
                    "{0:0.##} mm e' troppo per lo spigolo «{1}», lungo {2:0.##} mm: non ci " +
                    "sta neanche meta'.", massimo, uid, firma.Misura));

            var v1 = spigolo.GetStartVertex() as IEntity;
            var v2 = spigolo.GetEndVertex() as IEntity;
            if (v1 == null || v2 == null)
                return Fallito(
                    "Lo spigolo «" + uid + "» non ha due vertici: e' chiuso su se stesso " +
                    "(un cerchio), e un raccordo variabile ha bisogno di due estremi fra " +
                    "cui variare.");

            double? volumePrima = VolumeMm3(modello);
            if (volumePrima == null) return Fallito(VolumeNonLettoPrima("fare il raccordo"));

            IFeature feature;
            try
            {
                modello.ClearSelection2(true);
                // LO SPIGOLO PRIMA, POI I DUE VERTICI. Senza lo spigolo la
                // chiamata torna null: misurato.
                if (!(spigolo as IEntity).Select4(true, null))
                    return Fallito("Non sono riuscito a selezionare lo spigolo «" + uid + "».");
                if (!v1.Select4(true, null) || !v2.Select4(true, null))
                    return Fallito(
                        "Non sono riuscito a selezionare i due vertici dello spigolo «" +
                        uid + "»: senza di loro il raggio non ha dove variare.");

                feature = modello.FeatureManager.FeatureFillet3(
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
                    Radii: new double[] { Unita.M(r1), Unita.M(r2) },
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
            catch (Exception ex) { return Fallito("Il raccordo variabile non e' riuscito: " + ex.Message); }

            if (feature == null)
                return Fallito(
                    "Il raccordo variabile non e' stato creato, e SOLIDWORKS non dice " +
                    "perche'. Di solito e' il raggio: anche se ci sta sullo spigolo, puo' " +
                    "non starci sulle facce che gli stanno intorno.");

            try { modello.EditRebuild3(); } catch { }
            double? volumeDopo = VolumeMm3(modello);
            double? tolto = volumePrima - volumeDopo;

            // UN RACCORDO CHE NON TOGLIE NIENTE NON E' UN RACCORDO. La feature
            // c'e', l'albero e' cresciuto, e lo spigolo e' ancora vivo. Si dice
            // solo di un volume riletto (voce Z-C8).
            if (tolto <= 0)
                return Fallito(
                    "Il raccordo e' stato creato ma non ha tolto niente (volume " +
                    volumePrima.Value.ToString("0.##") + " mm3 prima e dopo). Lo spigolo e' " +
                    "ancora vivo: non ti confermo un pezzo diverso da quello che ti ho " +
                    "mostrato — " + Frasi.CosiSiRitira + ".");

            return Riuscito(feature, new Dictionary<string, object>
            {
                ["edge"] = uid,
                ["radius_start_mm"] = r1,
                ["radius_end_mm"] = r2,
                ["material_removed_mm3"] = NumeroOppureNull(tolto, 2),
                ["volume_not_reread"] = tolto.HasValue ? null : NotaVolumeNonRiletto,
                // IL CONTO A MANO ACCANTO, e non e' ridondanza: distingue «ha
                // fatto il variabile» da «ha preso solo il primo raggio».
                // Il blend non e' esattamente una spazzata lineare, quindi e'
                // un ORDINE DI GRANDEZZA, ed e' scritto che lo e'.
                ["expected_removed_mm3"] = firma.Misura > 0
                    ? (object)Math.Round(
                        firma.Misura * ((r1 * r1 + r1 * r2 + r2 * r2) / 3.0) * (1 - Math.PI / 4), 2)
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

        private Esito FilettaturaCosmetica(
            IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            string uid = Str(a, "edge_uid", null);
            if (string.IsNullOrWhiteSpace(uid))
                return Fallito(
                    "Non hai detto su quale spigolo. Il filetto cosmetico si attacca allo " +
                    "spigolo CIRCOLARE di un cilindro o di un foro — non alla faccia — e " +
                    "gli identificativi degli spigoli stanno nello stato del documento.");

            string denominazione = Str(a, "designation", null);
            if (string.IsNullOrWhiteSpace(denominazione))
                return Fallito(
                    "Manca la «designation», per esempio «M6x1». Un filetto cosmetico " +
                    "senza denominazione e' un'annotazione che non dice niente: sulla " +
                    "tavola comparirebbe un filetto senza misura, e chi lo legge dovrebbe " +
                    "indovinarla. Il testo lo scrivo come me lo dai: SOLIDWORKS non lo " +
                    "ricava da una tabella.");

            string scostamento;
            var firma = FirmaDelloSpigolo(
                a, stato, uid, "Lo spigolo «" + uid + "» non c'e' piu' in questo documento.",
                out scostamento);
            if (firma == null) return Fallito(scostamento);

            var spigolo = firma.Entita as IEdge;
            if (spigolo == null)
                return Fallito("Lo spigolo «" + uid + "» non e' piu' utilizzabile.");

            double diametro = 0;
            bool tondo = false;
            try
            {
                var curva = spigolo.IGetCurve();
                tondo = curva != null && curva.IsCircle();
                if (tondo)
                {
                    var p = curva.CircleParams as double[];
                    if (p != null && p.Length >= 7) diametro = p[6] * 2000.0;
                }
            }
            catch { }

            if (!tondo)
                return Fallito(
                    "Lo spigolo «" + uid + "» non e' un cerchio: un filetto cosmetico si " +
                    "attacca al bordo tondo di un cilindro o di un foro. Su uno spigolo " +
                    "dritto non c'e' niente da filettare.");

            // IL DIAMETRO LO DICE LO SPIGOLO, non chi chiama: e' il bordo su
            // cui il filetto si attacca, e un diametro diverso da quello
            // disegnerebbe un filetto che non sta sul pezzo.
            double diametroChiesto = Num(a, "diameter_mm", 0);
            if (diametroChiesto > 0 && diametro > 0 &&
                Math.Abs(diametroChiesto - diametro) > 0.01)
                return Fallito(string.Format(
                    "Hai chiesto un filetto da {0:0.##} mm, ma lo spigolo «{1}» e' un " +
                    "cerchio da {2:0.##}. Il filetto si attacca a quel bordo: o e' quello " +
                    "giusto, o e' l'altro spigolo.", diametroChiesto, uid, diametro));
            if (diametro <= 0) diametro = diametroChiesto;
            if (diametro <= 0)
                return Fallito("Non riesco a misurare il diametro dello spigolo «" + uid + "».");

            double profondita = Num(a, "depth_mm", 0);
            bool passante = Bool(a, "through_all", false);
            if (!passante && profondita <= 0)
                return Fallito(
                    "Dimmi quanto e' profondo il filetto («depth_mm») oppure che e' " +
                    "«through_all».");

            double? volumePrima = VolumeMm3(modello);
            if (volumePrima == null) return Fallito(VolumeNonLettoPrima("mettere il filetto"));

            IFeature feature;
            try
            {
                modello.ClearSelection2(true);
                if (!(spigolo as IEntity).Select4(false, null))
                    return Fallito("Non sono riuscito a selezionare lo spigolo «" + uid + "».");

                feature = modello.FeatureManager.InsertCosmeticThread3(
                    // Standard e taglia SONO IGNORATI da questa chiamata
                    // (misurato): si passa «nessuno standard» invece di
                    // fingere che un numero serva a qualcosa.
                    Standard: -2,
                    StandardType: "",
                    Size: "",
                    Diameter: Unita.M(diametro),
                    EndType: passante
                        ? (int)swCosmeticThreadType_e.swApplyCosmeticThread_ThroughFeature
                        : (int)swCosmeticThreadType_e.swApplyCosmeticThread_Blind,
                    Depth: passante ? 0 : Unita.M(profondita),
                    Note: denominazione) as IFeature;
            }
            catch (Exception ex) { return Fallito("Il filetto cosmetico non e' riuscito: " + ex.Message); }

            if (feature == null)
                return Fallito(
                    "Il filetto cosmetico non e' stato creato, e SOLIDWORKS non dice " +
                    "perche'. Controlla che lo spigolo «" + uid + "» sia il bordo di una " +
                    "faccia cilindrica.");

            try { modello.EditRebuild3(); } catch { }

            // E' COSMETICA: se il volume e' cambiato, si e' tagliato davvero.
            double? volumeDopo = VolumeMm3(modello);
            if (volumeDopo.HasValue && Math.Abs(volumeDopo.Value - volumePrima.Value) > 0.01)
                return Fallito(string.Format(
                    "Il filetto doveva essere COSMETICO e il volume e' cambiato: {0:0.##} " +
                    "mm3 prima, {1:0.##} dopo. Qualcosa ha tagliato davvero, e non e' " +
                    "quello che hai chiesto — ritiralo: " + Frasi.CosiSiRitira + ".",
                    volumePrima, volumeDopo));

            string tipoFeature = "";
            try { tipoFeature = feature.GetTypeName2(); } catch { }
            string callout = "";
            try
            {
                var dati = feature.GetDefinition() as ICosmeticThreadFeatureData;
                if (dati != null) callout = dati.ThreadCallout ?? "";
            }
            catch { }

            // UN'ANNOTAZIONE CHE NON DICE NIENTE non e' un'annotazione: la
            // feature ci sarebbe, la tavola mostrerebbe un filetto muto.
            if (string.IsNullOrEmpty(callout))
                return Fallito(
                    "Il filetto e' stato creato ma la sua denominazione e' rimasta vuota: " +
                    "sulla tavola comparirebbe un filetto senza misura. Ritiralo — " +
                    Frasi.CosiSiRitira + ".");

            return Riuscito(feature, new Dictionary<string, object>
            {
                ["edge"] = uid,
                ["designation"] = callout,
                ["diameter_mm"] = Math.Round(diametro, 3),
                ["depth_mm"] = passante ? (object)null : profondita,
                ["through_all"] = passante,
                ["feature_kind"] = tipoFeature,
                ["volume_mm3"] = NumeroOppureNull(volumeDopo, 2),
                ["cuts_nothing"] = volumeDopo.HasValue
                    ? "E' un'annotazione: il volume del pezzo e' lo stesso di prima. Se ti " +
                      "serve il filetto VERO, tagliato, questa non e' l'operazione."
                    : NotaVolumeNonRiletto,
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
        // `CorpiDaUid` (WeldmentOperations.cs), che guarda la firma.
    }
}
