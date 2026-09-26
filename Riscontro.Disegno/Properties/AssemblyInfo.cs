using System.Reflection;
using System.Runtime.InteropServices;

// Riscontro.Disegno — le operazioni di disegno per SOLIDWORKS, senza il PDM.
//
// LA VERSIONE SI ALZA A OGNI MODIFICA, come per l'add-in e per il ponte, e per
// la stessa ragione: questa libreria serve a MISURARE, e una misura senza il
// numero di versione di cio' che l'ha prodotta non e' una misura, e' un
// aneddoto.
//
// Parte da 0.52.0 e non da 0.1.0: e' lo stesso codice dell'add-in OpenPDM alla
// 0.52.0, spostato. Ricominciare da capo la numerazione farebbe sembrare nuovo
// cio' che ha gia' un anno di misure alle spalle, e renderebbe impossibile dire
// «questa prova gira sulla stessa versione di allora».
[assembly: AssemblyTitle("Riscontro.Disegno")]
[assembly: AssemblyDescription(
    "Operazioni di disegno per SOLIDWORKS che riportano cos'e' venuto, non cos'e' stato chiesto.")]
[assembly: AssemblyProduct("Riscontro")]
[assembly: AssemblyCulture("")]

// Non e' visibile a COM: la usa il ponte, non SOLIDWORKS.
[assembly: ComVisible(false)]

// 0.94.1: **solo commenti.** La regola dice che ogni modifica alla libreria
// alza la versione, e non fa eccezioni per i commenti — perche' «sono solo
// commenti» e' esattamente quello che si dice un attimo prima che non lo
// fossero. Qui e' vero (due blocchi di documentazione in
// `OperazioniSostituisci.cs`, riscritti perche' dicevano una cosa che il banco
// ha poi smentito), e il numero si muove lo stesso.
// 0.95.0: `croci_di_centro` (operazione 64) e le croci nello stato della tavola.
// 0.96.0: l'esploso nello stato dell'assieme, col millimetro accanto al booleano.
// 0.97.0: `nuovo_foglio` e `attiva_foglio` (voce GT1), col diedro riletto.
// 0.98.0: `cartiglio` (voce GT3) e `verifica_interferenze` (GA10); e
//         `AncheSuAssiemi`, perche' l'esecutore rifiutava ogni assieme mentre
//         il catalogo ne prometteva quattro operazioni.
// 0.98.1: il rifiuto di `quote_dal_modello` dice quante quote sono MARCATE,
//         non solo quante ce ne sono: senza quel dato la diagnosi resta divisa
//         in due, e il 18/9 c'e' voluta una sonda apposta per dividerla.
// 0.98.2: i sei contatori di annotazione per vista (voce Z-E1), piu' i sei del
//         FOGLIO a parte — perche' `InsertNote` scrive li', e contando solo le
//         viste `note` sarebbe un campo che non puo' diventare vero.
// 0.98.3: `cartiglio` distingue «la proprieta' non c'e'» da «c'e' sul modello e
//         il cartiglio la mostra vuota» — due diagnosi opposte che prima
//         finivano nello stesso elenco, e mandavano a riparare la cosa
//         sbagliata.
// 0.98.4: `viste_standard` non sorteggia piu' il pezzo. Metteva in tavola le
//         viste del PRIMO documento aperto — un ordine che nessuno dichiara — e
//         con piu' di un pezzo aperto faceva la tavola di un altro pezzo senza
//         dare nessun errore. Adesso: uno solo, quello; due o piu', rifiuta.
// 0.98.5: `salva_documento` guarda PERCHE' non ha salvato invece di riportare
//         un codice: la cartella che non esiste e il file in sola lettura si
//         vedono da qui, e il codice di `SaveAs` non li dice.
// 0.99.0: `quota_in_tavola` (voce K10) — LE QUOTE IN TAVOLA SI CREANO.
//         `InsertModelAnnotations*` non ne porta nessuna fuori processo, ma
//         `AddDimension` sugli spigoli della vista si', e coi valori giusti.
//         Ogni quota viene RILETTA: risponde quanto misura, non «fatto».
// 0.100.0: `nota_in_tavola` (voce E2-bis) — una nota di testo sul foglio.
//          `InsertNote` risponde, `InsertNewNote3` e' `Void` e non fa niente.
//          Le note si contano SUL FOGLIO e non nelle viste: contarle nel posto
//          sbagliato avrebbe letto 0 con la nota al suo posto, e quel falso
//          negativo avrebbe chiuso per sbaglio quattro voci del TODO.
// 0.101.0: le quote non si lasciano piu' ammucchiate sulla vista. Il punto in
//          cui una quota finisce si calcola dallo spigolo: `GetCurveParams3`
//          da' i due estremi nel modello, `ModelToViewTransform` li porta sul
//          foglio, e SOLO ALLORA si somma `IView.Position` -- senza quella
//          somma le quote cadevano tutte a x=0. Orizzontali sopra, verticali
//          di lato (UNI ISO 129-1: linee di misura fuori dalle figure), in
//          ordine di lunghezza crescente allontanandosi dal pezzo, distacco
//          12 mm e passo 10 mm. Doppioni tolti per GEOMETRIA e non per
//          valore: due gradini da 10 mm sono due elementi, e vanno quotati
//          tutti e due.
// 0.102.0: il contatore della fascia orizzontale si chiamava `soprattutto`
//          e le quote le metteva SOTTO (voce K15). Nient'altro cambia nel
//          comportamento: e' un nome che diceva il contrario di quello che
//          faceva, ed e' una trappola per chi legge fra sei mesi.
//          E LA RIGA DELLA 0.101.0 QUI SOPRA DICE «orizzontali sopra»: e'
//          sbagliata, e il nome del contatore e' il motivo per cui e' stata
//          scritta cosi'. Non la si corregge in silenzio — una nota di
//          versione riscritta all'indietro e' una nota che mente — la si
//          smentisce qui: **sotto**, come dice il codice e come ha sempre
//          fatto.
// 0.103.0: `MaxSpigoli` da 60 a 30 (voce K13). Il tetto dei 16 KB sullo stato
//          era gia' superato nella configurazione normale di chi lavora —
//          16.089 caratteri con due documenti aperti — e la prova non lo
//          vedeva perche' il banco gira su una scrivania pulita. Facce e
//          spigoli sono l'89% del carico e le voci sono gia' asciutte:
//          l'unica leva e' QUANTE. Adesso ~13.300, il 17% di margine.
// 0.104.0: `sezione_in_tavola` (voce GT4) — la prima vista di sezione. La
//          linea di taglio non si passa in firma: si disegna sul foglio e si
//          lascia selezionata, e `CreateSectionViewAt5` prende quella.
//          IL CONTATORE DEL TRATTEGGIO NON E' QUELLO CHE LA VOCE
//          PRESCRIVEVA: `GetSolidHatchCount` risponde ZERO su una sezione
//          vera, prima e dopo il ricalcolo. Si legge `GetFaceHatchCount`, e
//          l'altro si riporta lo stesso nella risposta perche' nessuno
//          ripercorra la strada gia' misurata.
// 0.105.0: `dettaglio_in_tavola` (voce GT4, l'altra meta'). E qui 'per
//          analogia' sarebbe stato sbagliato due volte su tre: 'Showtype'
//          vuole swDetCircleShowType_e, e il conto delle circonferenze di
//          dettaglio e' il VALORE DI RITORNO di GetDetailCircleCount2, non
//          il suo 'out' -- che passa da 1 a 35 per UNA circonferenza.
//          E il controllo non e' il tratteggio: un dettaglio non taglia
//          niente e la campitura li' e' zero. La prova che il cerchio e'
//          diventato un dettaglio e' la circonferenza in piu' sulla vista
//          di partenza: 0 -> 1.
// 0.106.0: i controlli preliminari RICEVONO il documento invece di
//          ripescarsi `_sw.IActiveDoc2` (voce Z-C5). Erano due letture
//          dello stesso globale a poche righe di distanza, con una
//          finestra in mezzo: potevano approvare il documento A -- e' una
//          parte, e' salvato, non e' in sola lettura -- e il piano
//          prendere il B. Nessun errore lo avrebbe detto, perche'
//          tutt'e due le letture erano legittime: sbagliato era che
//          fossero DUE. Restano cinque letture in EsecutorePiano, e adesso
//          un controllo di contratto le conta a ogni giro.
// 0.107.0: dodici operazioni dichiarano il limite che gia' rifiutavano
//          (voce Z-G5). Ognuna aveva un caso di banco che pretende un
//          messaggio di rifiuto, e una descrizione che non ne diceva
//          niente: il modello poteva scoprire quel limite solo
//          sbattendoci contro, e un rifiuto costa un giro mentre un
//          limite dichiarato costa zero. Le frasi non sono inventate:
//          sono il messaggio del caso, girato al futuro. Il conto delle
//          descrizioni mute e' un tetto in Prove/ProveILimiti.cs, ed e'
//          passato da 12 a ZERO.
// 0.108.0: `DocumentoRichiesto` su ogni operazione del catalogo (voce
//          Z-D0-ter): «parte», «assieme», «tavola», o piu' d'uno. La
//          famiglia era un'approssimazione, e `verifiche` lo dimostrava
//          da sola -- `cartiglio` vuole una tavola, `verifica_interferenze`
//          un assieme, le altre sei una parte. Quel fatto viveva nella
//          testa di chi aveva scritto l'operazione e in una frase di prosa
//          che nessun controllo legge. Adesso sta nel JSON, e un modello
//          che lo legge NON prova un'operazione sul documento sbagliato.
// 0.109.0: `Esempio` su 24 operazioni (voce Z-G1): una chiamata vera,
//          copiata da un passo che il banco ha ESEGUITO e misurato. Uno
//          schema dice quali campi sono leciti, non quale combinazione
//          costruisce un pezzo.
//          Sono 24 e non 71 apposta: gli altri casi usano selettori che
//          vivono solo nel banco (@faccia, @spigolo) o i segnaposto
//          {LAVORO}/{PROFILI}. Copiarli avrebbe dato 24 esempi buoni e 47
//          TRAPPOLE -- un modello che copia @faccia(verso=alto) riceve un
//          rifiuto che non capisce, e poi non si fida piu' del catalogo.
// 0.110.0: il guinzaglio anche nella libreria (voce Z-C4), la STESSA
//          classe che usa il banco -- due meccanismi di scadenza che si
//          comportano diversamente sono peggio di uno solo.
//          Due differenze volute rispetto al banco: il referto va su
//          **stderr**, perche' su stdio stdout e' il canale JSON-RPC e una
//          riga non-JSON lo rompe; e **non uccide il processo**, perche'
//          il ponte e' un servizio di qualcun altro mentre il banco e' un
//          giro che o finisce o non serve.
// 0.111.0: `applica_materiale.materiale` vale S235JR se non detto e
//          `combina_corpi.operazione` vale unisci (voce Z-G3), e la
//          risposta DICE che l'ha scelto il ponte. Questa nota mancava:
//          la versione era stata alzata e raccontata solo nel CHANGELOG
//          del ponte (0.84.0). Scritta il 24/9/2026 con la 0.112.0.
// 0.112.0: la cartella dei profili di saldatura non ha piu' un percorso
//          predefinito. `CartellaDeiProfili()` torna null senza
//          PROFILI_SALDATURA, e `profili_saldatura` distingue «non so dove
//          sia» da «li' non c'e'»: prima i due casi davano lo stesso
//          messaggio, con un percorso vuoto fra virgolette. Il ripiego era
//          il disco di una postazione sola: non aiutava nessun altro e
//          pubblicava com'e' fatto quel disco.
//          Il resto sono commenti riscritti senza nomi di pezzi veri.
//          ALZATA APPOSTA ANCHE SE IL CAMBIO E' PICCOLO: i sorgenti erano
//          cambiati sotto la 0.111.0, e un banco verde su quei byte avrebbe
//          descritto una libreria diversa con lo stesso numero addosso --
//          il difetto della voce K14. Se n'e' accorto il critico della
//          rilettura, non una prova.
// 0.113.0: tre cose misurate dalle sonde del 24/9/2026, tutte e tre senza
//          errore da nessuna parte prima di oggi.
//          - `modifica_quota` RIFIUTA se la quota nuova fa uscire
//            un'apertura dal materiale (voce E18, pezzo c): contate le
//            aperture chiuse delle facce piane prima e dopo, se calano
//            rimette il valore di prima e rilegge il volume. Era il caso
//            r16 della regressione, un'asola che sbordava in silenzio.
//          - La guardia contro il modale guarda se la finestra principale
//            e' ABILITATA (voce Z-C3): `CommandInProgress` resta falso con
//            un modale aperto, misurato con `Controlli/Modale`.
//          - Due descrizioni del catalogo che si proclamavano sole
//            (`quote_dal_modello`, `estrudi_su_faccia`) dicono il vero
//            (voce Z-C15): le ha trovate il controllo di contratto nuovo.
//          E prima del primo banco verde, sulla stessa versione mai
//          committata (il giro del 24/9 sera e' morto per un singhiozzo di
//          S:, e un giro nuovo serviva comunque):
//          - la famiglia LAMIERA: `flangia_base`, `sviluppo_dxf` (voce GP18),
//            dalla sonda che ha misurato lo sviluppo al centesimo;
//          - `schema_di_quote`, DimXpert dal nucleo (voce Z-H3);
//          - `esporta_pdf` con «cerca»: il PDF si rilegge, decodificando le
//            /ToUnicode (voci GT3-bis, Z-C9);
//          - lo stato della tavola conta le quote senza spigolo (voce Z-A2);
//          - la descrizione di `estrudi_sezione` dice che l'asola e' un
//            poligono circoscritto: il primo giro lo ha scoperto col caso nuovo.
// 0.114.0: gli ASSI si controllano per identita' e due operazioni di
//          saldatura che non avevano mai fatto niente.
//          - Lo stato di un asse porta un punto sulla retta e la direzione, e
//            `asse_uid` si confronta con la firma del piano (voce E4, la parte
//            degli assi): un asse rifatto altrove col nome di prima si
//            rifiuta, invece di ripetere attorno a quello nuovo.
//          - `taglia_membri` passava i corpi come `object[]`, e SOLIDWORKS
//            rispondeva con una feature che nell'albero non entrava: la T
//            restava identica e l'operazione diceva `corpi_tagliati: 1`. Con
//            `Body2[]` il taglio nasce, e una guardia pretende che la feature
//            sia nell'albero e che il volume cambi (voce Z-C6).
//          - `cordone_saldatura` non aveva mai creato un cordone: prende DUE
//            facce invece di due insiemi di corpi, usa la chiamata per
//            selezione (l'unica che lo crea), controlla prima che le facce si
//            tocchino ad angolo e rilegge il lato dalla feature.
//          - `fazzoletto` esce dal catalogo (`Provata = false`): non nasce in
//            nessuna delle sedici varianti provate.
// 0.115.0: LA TAVOLA QUOTATA, GUARDATA (voce GT11). Chiesta una tavola da
//          vedere, la tavola — col banco verde — aveva quattro difetti:
//          - i numeri delle quote verticali SOPRA le linee di richiamo: la
//            trasformazione modello-foglio di `quota_in_tavola` usava la matrice
//            trasposta (Y specchiata nelle viste ruotate) e sommava la posizione
//            della vista al posto della traslazione (5 mm di errore). Adesso
//            `MathPoint.MultiplyTransform`;
//          - i fori non quotati: richiamo del foro («4× Ø8 PASSANTE») e
//            posizioni in parallelo dai bordi di riferimento;
//          - lo smusso quotato 7,071 in obliquo con la catena 5/75: adesso la
//            quota da smusso «5 x 45°», e gambe e ipotenusa si saltano;
//          - il 10 e il 120 in due viste: si salta cio' che e' gia' quotato in
//            un'altra vista, leggendolo dagli spigoli delle quote che ci sono.
//          E le quote restano dentro la cornice del foglio, e la vista si puo'
//          dire con l'identificativo dello stato («V2»).
// 0.116.0: L'ANCORA (voce E18, pezzi a e b). `taglio_cilindrico` e
//          `taglio_rettangolare` prendono `ancora`: per ogni asse della faccia
//          una quota dal centro a uno spigolo, oppure il centro a meta' fra due
//          spigoli opposti. Senza, x_mm/y_mm restano assoluti e un foro a 20
//          dal bordo resta dov'era quando la piastra si allunga. Le regole
//          vengono dalla misura e da un critico avversariale: tutti e due gli
//          assi, spigolo con la firma, dritto, sulla faccia e non sul bordo di
//          uno smusso; coerente con x_mm/y_mm; `AddToDB` solo attorno alle
//          creazioni; schizzo completamente definito e nessuna relazione
//          malata; una prova di movimento dopo il taglio, e se non regge il
//          taglio si toglie. La centratura e' PER ASSE: fra i punti medi dei
//          fianchi faceva saltare il foro in y gia' alla creazione.
// 0.117.0: ZERO NON E' «NON LO SO» (voce Z-C8), e due guardie che guardavano
//          altrove. Ogni conteggio letto dal CAD che rispondeva 0 da un catch
//          ora risponde -1 o null: VolumeMm3 e' double?, ContaCorpiSolidi,
//          FacceDellaFeature, ContaFogli, ContaViste, ContaQuote, ContaFeature,
//          ContaPiani e gli altri tornano -1, e i chiamanti confrontano solo
//          numeri letti. Prima della modifica un volume illeggibile e' un
//          rifiuto (non si e' toccato niente); dopo, niente rifiuto e niente
//          numero: il campo e' null e c'e' la nota. Lo stato scrive null per un
//          volume o un'area che non si leggono. La guardia dell'unione
//          confrontava due enum diversi e non scattava mai (voce Z-C18).
// 0.118.0: IL NUMERO STAMPATO E' QUELLO DEL MODELLO? (voce Z-C2) Lo stato della
//          tavola conta le quote che stampano un numero non del modello: numero
//          sovrascritto (`GetOverride`) o valore spento con del testo al suo
//          posto (`ShowDimensionValue`), misurati il 25/9/2026 sulle tre strade
//          dell'interop. Prende anche il numero sovrascritto che combacia per
//          caso col modello, perche' guarda la sovrascrittura e non i valori.
//          E L'ASOLA (voce E18): lo schizzo nasce definito — tangenze, raggi
//          uguali, mezzeria fra i centri quotata, raggio quotato, orizzontale o
//          verticale — e si corregge con modifica_quota; prende `ancora`, e
//          obliqua quota l'angolo contro lo spigolo d'ancora con
//          AddSpecificDimension (Extension.AddDimension torna null li').
// 0.119.0: IL RIALZO ANCORATO (voce E18). `estrudi_su_faccia` prende `ancora`:
//          il punto e' il centro del contorno, e vale per un contorno solo,
//          tondo (diametro quotato, ancora sul centro) o rettangolare
//          allineato (orizzontali e verticali, due quote, centro costruito con
//          la diagonale). Per un contorno qualunque un rifiuto che dice perche'.
//          E `foro_guidato`, l'ultimo pezzo: un'ancora per posizione (tutte o
//          nessuna), messa nello schizzo dei punti di HoleWizard dopo il
//          posizionamento; definito si guarda dopo l'ultimo punto, lo
//          scavalcamento col raggio vero a foro nato.
// 0.120.0: GLI SPIGOLI SI RITROVANO PER FIRMA (voce E19). Raccordo, smusso,
//          raccordo variabile, filetto cosmetico, direzione di una ripetizione
//          e ancora: se l'uid e' cambiato fra la lettura e adesso, si cerca lo
//          spigolo con la stessa lunghezza nello stesso punto — uno solo, mai
//          il piu' vicino — e si dice in `riancoraggi`. `LunghezzaMm` da' la
//          lunghezza VERA (GetLength3), non la corda: il bordo di un foro era
//          «lungo 0» nello stato. FirmaCombacia confronta anche la lunghezza.
// 0.121.0: LO SMUSSO CHE NON CI STA (caso r05, l'ultimo silenzio della
//          regressione). modifica_quota rifiuta, e rimette il valore, se una
//          lavorazione che non e' un taglio perde facce e dopo di lei c'e' uno
//          smusso o un raccordo: e' lui che si prende una faccia intera, e
//          SOLIDWORKS non lo segnala. RimettiLaQuota estratto (due guardie).
// 0.122.0: `flangia_spigolo` (voce GP18, terza su sette). La flangia a spigolo
//          nasce solo con un RETTANGOLO di profilo sul piano della flangia (la
//          faccia di testa): dieci tentativi della sonda, i primi otto null, il
//          nono una flangia vuota. Lunghezza allo spigolo virtuale esterno =
//          profilo + T; volume in piu' confrontato col conto a mano. E `orlo`
//          (quarta su sette): sullo spigolo selezionato, nessuno schizzo;
//          l'aperto e' la piega a 180 gradi col raggio = meta' del gioco.
//          `SpigoloDiBordo` estratto, per flangia e orlo.
// 0.123.0: `piega_da_schizzo` (voce GP18, quinta su sette): la lamiera si
//          piega lungo una linea tracciata su una faccia (InsertSheetMetal3dBend
//          con lo schizzo selezionato, al primo colpo). Volume e sviluppo
//          restano; l'ingombro prima e dopo dice quale lato e' rimasto fermo.
//          Il jog no: sette varianti di selezione, niente.
[assembly: AssemblyVersion("0.123.0.0")]
[assembly: AssemblyFileVersion("0.123.0.0")]
// Era rimasta a 0.61.0 mentre l'assembly andava a 0.66.0: un numero che
// nessuno legge e che dice il falso a chi apra le proprieta' del file. Non
// serviva a niente ed era sbagliato, che e' la combinazione peggiore.
//
// ED E' RISUCCESSO: il 22/9/2026 diceva 0.94.0 con l'assembly a 0.99.0, cioe'
// cinque versioni indietro. La riga sopra racconta il difetto e non lo ha
// impedito — perche' un commento non e' un controllo.
//
// ADESSO IL CONTROLLO C'E', e il 23/9/2026 ha parlato: «assembly 0.101.0.0,
// proprieta' del file 0.100.0». Il difetto era ricapitato per la terza volta,
// e stavolta non l'ha trovato nessuno leggendo — l'ha detto la suite di
// contratto, prima del commit. *Fra un commento che avvisa e una prova che
// si fa rossa non c'e' una differenza di grado: la prima si legge quando si
// e' gia' sbagliato, la seconda impedisce di sbagliare.*
[assembly: AssemblyInformationalVersion("0.123.0")]
