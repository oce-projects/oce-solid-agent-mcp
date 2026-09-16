using System.Collections.Generic;

using static Riscontro.Disegno.Catalogo.Nodo;

namespace Riscontro.Disegno.Catalogo
{
    /// <summary>
    /// IL CATALOGO. Cosa il modello puo' chiedere, e come si scrive.
    ///
    /// STA QUI E NON IN UN JSON SCRITTO A MANO, e nemmeno nel server, per
    /// una ragione sola: **lo strato che DESCRIVE un'operazione deve essere
    /// lo stesso che la ESEGUE**. E' l'unica disposizione in cui
    /// un'operazione descritta e non eseguibile non puo' entrare in
    /// catalogo senza che qualcuno se ne accorga: il controllo diventa
    /// un'asserzione dentro un progetto, invece di un confronto fra due
    /// repository che nessuno rifa'.
    ///
    /// Prima viveva in `ai_disegno.py`, e chi clonava questo repository NON
    /// POTEVA rigenerare `catalogo.json`: servivano il server, docker e un
    /// container usa-e-getta. Adesso serve `Riscontro.Catalogo.exe`.
    ///
    /// `Provata` non finisce nel JSON ed e' voluto: e' la promessa che
    /// l'operazione e' stata costruita e MISURATA dentro SOLIDWORKS, e
    /// finche' e' falsa l'operazione non si esporta — quindi il modello non
    /// la vede. Sta accanto al codice che esegue, non in un file lontano.
    /// </summary>
    public static class Operazioni
    {
        /// <summary>Le famiglie, con la riga che le spiega.</summary>
        public static readonly List<KeyValuePair<string, string>> Famiglie =
            new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(
                    "estrusioni",
                    "fare il primo corpo del pezzo"),
                new KeyValuePair<string, string>(
                    "lavorazioni",
                    "togliere o modificare materiale su un corpo che c'e' gia'"),
                new KeyValuePair<string, string>(
                    "saldature",
                    "carpenteria: barre, telai, tagli fra barre, distinta"),
                new KeyValuePair<string, string>(
                    "riferimenti",
                    "piani e assi su cui costruire"),
                new KeyValuePair<string, string>(
                    "proprieta",
                    "cio' che il pezzo E', non la sua forma"),
                new KeyValuePair<string, string>(
                    "tavola",
                    "mettere il pezzo in tavola e consegnarlo: fogli, viste, quote, PDF"),
                // LA FAMIGLIA CHE NON DISEGNA. Fino alla 0.76.0 nessuna
                // operazione del catalogo sapeva rispondere a «com'e' venuto?»:
                // le misure c'erano ma erano private dentro le operazioni, e il
                // modello non poteva chiamarne nessuna.
                new KeyValuePair<string, string>(
                    "verifiche",
                    "dire com'e' venuto, senza toccarlo"),
                new KeyValuePair<string, string>(
                    "esportazioni",
                    "far uscire il pezzo da SOLIDWORKS: STL per la stampa 3D, "
                    + "STEP e IGES per chi lo deve lavorare"),
                // QUELLO CHE IL PEZZO DICE DI SE'. Il numero di disegno e la
                // revisione non sono geometria, ma sono cio' che arriva al
                // cartiglio e alla scheda del PDM: un pezzo giusto con la
                // revisione sbagliata e' un pezzo sbagliato.
                new KeyValuePair<string, string>(
                    "identita",
                    "quello che il pezzo dice di se': numero disegno, revisione, "
                    + "cliente, e le sue varianti"),
            };

        public static readonly List<OperazioneDisegno> Tutte =
            new List<OperazioneDisegno>
            {
                new OperazioneDisegno
                {
                    Nome = "estrudi_cerchio",
                    Famiglia = "estrusioni",
                    Provata = true,
                    Sommario =
                        "Un cilindro pieno centrato sull'origine.",
                    Descrizione =
                        "Crea un cilindro pieno: cerchio su un piano, estruso. E' "
                        + "l'operazione per perni, spine, boccole, dischi.",
                    Precondizione = "documento_senza_corpo",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "piano", O(
                                "type", "string",
                                "enum", A(
                                    "frontale",
                                    "superiore",
                                    "destro"),
                                "description", "piano di costruzione: frontale, superiore o destro"),
                            "diametro_mm", O(
                                "type", "number"),
                            "altezza_mm", O(
                                "type", "number",
                                "description", "lunghezza del cilindro, in mm")),
                        "required", A(
                            "diametro_mm",
                            "altezza_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "estrudi_profilo",
                    Famiglia = "estrusioni",
                    Provata = true,
                    Sommario =
                        "Una sagoma qualunque, data per contorni, estrusa: il modo "
                        + "generale di fare il primo corpo.",
                    Descrizione =
                        "Estrude una SAGOMA QUALSIASI, data come elenco di vertici. E' "
                        + "l'operazione con cui si fa tutto quello che non e' un rettangolo "
                        + "o un cerchio: staffe sagomate, squadrette, fazzoletti, angolari, "
                        + "e i profili CAVI — tubolari, tubi, sezioni a C — che si fanno con "
                        + "DUE contorni, uno esterno e uno interno.\n\nIl primo contorno e' il "
                        + "bordo esterno; quelli dopo sono vuoti dentro di esso. Ogni "
                        + "contorno si chiude da solo: l'ultimo vertice si unisce al primo, "
                        + "non ripeterlo.\n\nSe la sagoma che ti serve ha un nome (una ruota "
                        + "dentata, un tubolare) guarda se esiste un'operazione dedicata: e' "
                        + "piu' facile da controllare per chi conferma, perche' mostra le "
                        + "quote invece di un elenco di punti.",
                    Precondizione = "documento_senza_corpo",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "piano", O(
                                "type", "string",
                                "enum", A(
                                    "frontale",
                                    "superiore",
                                    "destro"),
                                "description", "piano di costruzione: frontale, superiore o destro"),
                            "contorni", O(
                                "type", "array",
                                "description", "Il PRIMO contorno e' il bordo esterno, i successivi sono i vuoti "
                                    + "dentro di esso.\n\nOgni contorno puo' avere DUE forme:\n1) una "
                                    + "spezzata chiusa: un elenco di vertici, per esempio "
                                    + "[[0,0],[100,0],[100,60],[0,60]]. L'ultimo vertice si unisce al "
                                    + "primo da solo: NON ripeterlo.\n2) un CERCHIO, e per un foro o un "
                                    + "tondo va usato questo e non una spezzata con tanti lati: "
                                    + "{\"cerchio\": [x_centro, y_centro], \"diametro_mm\": 20}. Un cerchio "
                                    + "approssimato a segmenti ha il materiale dentro la circonferenza, "
                                    + "e un albero non ci entra.\n\nEsempio, TUBO diametro esterno 60 "
                                    + "spessore 5:\n[{\"cerchio\": [0, 0], \"diametro_mm\": 60}, {\"cerchio\": "
                                    + "[0, 0], \"diametro_mm\": 50}]\nEsempio, TUBOLARE quadro 80x80 parete "
                                    + "5:\n[[[-40,-40],[40,-40],[40,40],[-40,40]], "
                                    + "[[-35,-35],[35,-35],[35,35],[-35,35]]]",
                                "items", O(
                                    "description", "una spezzata (elenco di [x_mm, y_mm]) oppure un cerchio "
                                        + "({\"cerchio\": [x, y], \"diametro_mm\": d})")),
                            "spessore_mm", O(
                                "type", "number",
                                "description", "quanto si estrude, in mm"),
                            "simmetrico", O(
                                "type", "boolean",
                                "description", "estrusione meta' da una parte e meta' dall'altra del piano (predefinito: no)")),
                        "required", A(
                            "contorni",
                            "spessore_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "estrudi_rettangolo",
                    Famiglia = "estrusioni",
                    Provata = true,
                    Sommario =
                        "Una piastra rettangolare piena, il primo corpo del pezzo.",
                    Descrizione =
                        "Crea una piastra rettangolare: disegna un rettangolo su un piano "
                        + "e lo estrude. E' l'operazione per piastre, staffe, basi, flange "
                        + "squadrate.",
                    Precondizione = "documento_senza_corpo",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "piano", O(
                                "type", "string",
                                "enum", A(
                                    "frontale",
                                    "superiore",
                                    "destro"),
                                "description", "piano di costruzione: frontale, superiore o destro"),
                            "lunghezza_mm", O(
                                "type", "number",
                                "description", "primo lato, in mm"),
                            "larghezza_mm", O(
                                "type", "number",
                                "description", "secondo lato, in mm"),
                            "spessore_mm", O(
                                "type", "number",
                                "description", "QUANTO SI ESTRUDE, in mm. Per una piastra e' lo spessore; per una "
                                    + "barra o un profilo e' la LUNGHEZZA. Il rettangolo e' la sezione, "
                                    + "questo e' quanto va in profondita': una barra 80x80 lunga 1000 si "
                                    + "fa con lunghezza_mm 80, larghezza_mm 80, spessore_mm 1000."),
                            "centrato", O(
                                "type", "boolean",
                                "description", "rettangolo centrato sull'origine (predefinito: si')")),
                        "required", A(
                            "lunghezza_mm",
                            "larghezza_mm",
                            "spessore_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "estrudi_sezione",
                    Famiglia = "estrusioni",
                    Provata = true,
                    Sommario =
                        "Una barra da sezione normalizzata (tubolare, tondo, angolare, U, "
                        + "T, piatto, esagono, asola) NON di carpenteria.",
                    Descrizione =
                        "Estrude una sezione normalizzata — tubolare, tondo, tubo, "
                        + "angolare a L, profilato a U o a T, piatto, esagono, asola — e "
                        + "ottiene un CORPO SOLIDO con quella forma.\n\nNON E' L'OPERAZIONE "
                        + "DELLA CARPENTERIA. Per un elemento di telaio usa "
                        + "«membro_strutturale»: un corpo estruso ha la stessa forma di una "
                        + "barra, ma non e' un membro strutturale, quindi NON genera la "
                        + "distinta di taglio, non porta il nome del profilo, e non si "
                        + "lascia tagliare a 45 gradi agli incroci. Chi va in officina con "
                        + "un pezzo cosi' non ha l'elenco delle barre da tagliare.\n\nQuesta "
                        + "operazione serve quando la sezione NON e' un elemento di telaio: "
                        + "un distanziale ricavato da barra, una piastrina sagomata, un "
                        + "pezzo tornito da tondo, o un profilo che nella libreria aziendale "
                        + "non esiste.\n\nLe quote da dare cambiano con il tipo:\n• tubolare  "
                        + "larghezza, altezza, parete\n• tubo      diametro, parete\n• tondo   "
                        + "  diametro\n• angolare / u / t   larghezza, altezza, spessore\n• "
                        + "piatto / asola     larghezza, altezza\n• esagono   chiave "
                        + "(l'apertura in chiave, non il diametro "
                        + "circoscritto)\n\n«lunghezza_mm» e' quanto e' lunga la barra: per un "
                        + "tubolare 80x80x5 lungo 1000 sono larghezza 80, altezza 80, parete "
                        + "5, lunghezza 1000.\n\nGLI SPIGOLI SONO VIVI, e sul peso si "
                        + "vede. Un profilo commerciale ha gli angoli raggiati, quindi "
                        + "questa sezione pesa di piu': un tubolare 80x80x5 lungo 1000 "
                        + "viene 1.500.000 mm3 contro i circa 1.443.000 del profilo vero, "
                        + "cioe' il 4% in piu'. Per la forma non cambia niente; se il "
                        + "numero che ti serve e' il PESO, prendi il profilo dalla "
                        + "libreria con «membro_strutturale».",
                    Precondizione = "documento_senza_corpo",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "piano", O(
                                "type", "string",
                                "enum", A(
                                    "frontale",
                                    "superiore",
                                    "destro"),
                                "description", "piano di costruzione: frontale, superiore o destro"),
                            "tipo", O(
                                "type", "string",
                                "enum", A(
                                    "tubolare",
                                    "tondo",
                                    "tubo",
                                    "angolare",
                                    "u",
                                    "t",
                                    "piatto",
                                    "esagono",
                                    "asola"),
                                "description", "quale sezione"),
                            "lunghezza_mm", O(
                                "type", "number",
                                "description", "quanto e' lunga la barra, in mm"),
                            "larghezza_mm", O(
                                "type", "number"),
                            "altezza_mm", O(
                                "type", "number"),
                            "parete_mm", O(
                                "type", "number",
                                "description", "spessore della parete, per tubolare e tubo"),
                            "spessore_mm", O(
                                "type", "number",
                                "description", "spessore del profilo, per angolare, U e T"),
                            "diametro_mm", O(
                                "type", "number"),
                            "chiave_mm", O(
                                "type", "number",
                                "description", "apertura in chiave dell'esagono"),
                            "simmetrico", O(
                                "type", "boolean",
                                "description", "meta' per parte del piano (predefinito: no)")),
                        "required", A(
                            "tipo",
                            "lunghezza_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "rivoluzione",
                    Famiglia = "estrusioni",
                    Provata = true,
                    Sommario =
                        "Un solido a simmetria di rotazione: boccole, distanziali, alberi "
                        + "a gradini, flange, pulegge, volantini.",
                    Descrizione =
                        "Ruota un profilo attorno a un asse e ottiene un corpo a simmetria "
                        + "di rotazione. E' l'operazione per tutto cio' che si fa al tornio "
                        + "quando la forma VARIA lungo l'asse: alberi a gradini, boccole con "
                        + "gole, flange, pulegge. Per un cilindro costante bastano "
                        + "«estrudi_cerchio» o «estrudi_sezione», piu' semplici.\n\nIl profilo "
                        + "si da' con gli stessi CONTORNI di «estrudi_profilo»: il primo e' "
                        + "il bordo, i successivi sono vuoti.\n\nLA DIFFERENZA E' L'ASSE. Il "
                        + "profilo e' una MEZZA SEZIONE, vista di fianco come al tornio: sta "
                        + "tutto da un lato dell'asse, al piu' toccandolo. NON deve "
                        + "attraversarlo — un profilo che attraversa l'asse produce un "
                        + "solido che si autointerseca.\n\nEsempio, albero a gradini: diametro "
                        + "30 per 40 mm, poi diametro 20 per altri 30, asse lungo y=0:\n  "
                        + "contorni: [[[0,0],[0,15],[40,15],[40,10],[70,10],[70,0]]]\n  asse: "
                        + "[[0,0],[70,0]]\nIl lato da (70,0) a (0,0), che chiude la spezzata, "
                        + "giace SULL'asse: e' il caso normale, non un errore.\n\nSolo il "
                        + "corpo pieno: per una parete sottile disegna il vuoto interno come "
                        + "secondo contorno.",
                    Precondizione = "documento_senza_corpo",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "piano", O(
                                "type", "string",
                                "enum", A(
                                    "frontale",
                                    "superiore",
                                    "destro"),
                                "description", "piano di costruzione: frontale, superiore o destro"),
                            "contorni", O(
                                "type", "array",
                                "description", "il profilo da ruotare, stessa sintassi di «estrudi_profilo»: il "
                                    + "primo contorno e' il bordo, i successivi sono vuoti. Ogni "
                                    + "contorno e' una spezzata chiusa di vertici [x_mm, y_mm] (l'ultimo "
                                    + "si chiude da solo, non ripeterlo) oppure {\"cerchio\": [x, y], "
                                    + "\"diametro_mm\": d}. Sta tutto da un lato dell'asse.",
                                "items", O(
                                    "description", "una spezzata (elenco di [x_mm, y_mm]) oppure un cerchio "
                                        + "({\"cerchio\": [x, y], \"diametro_mm\": d})")),
                            "asse", O(
                                "type", "array",
                                "description", "la retta di rivoluzione: DUE punti [x_mm, y_mm] nello stesso "
                                    + "sistema dei contorni. Per un pezzo tornito e' quasi sempre l'asse "
                                    + "orizzontale del disegno, per esempio [[0,0],[70,0]]. Non e' un "
                                    + "dettaglio: decide l'ingombro finale quanto i vertici del profilo.",
                                "items", O(
                                    "type", "array",
                                    "items", O(
                                        "type", "number"))),
                            "angolo_gradi", O(
                                "type", "number",
                                "description", "predefinito 360, un giro completo")),
                        "required", A(
                            "contorni",
                            "asse")),
                },
                new OperazioneDisegno
                {
                    Nome = "ruota_dentata",
                    Famiglia = "estrusioni",
                    Provata = true,
                    Sommario =
                        "Una ruota dentata a evolvente, con foro e cava per linguetta.",
                    Descrizione =
                        "Crea una RUOTA DENTATA cilindrica a denti dritti, dal numero di "
                        + "denti e dal modulo. Il profilo del dente e' a evolvente e lo "
                        + "calcola il server: tu dai solo i dati di progetto.\n\nCon foro_mm "
                        + "si ottiene anche il foro centrale.",
                    Precondizione = "documento_senza_corpo",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "piano", O(
                                "type", "string",
                                "enum", A(
                                    "frontale",
                                    "superiore",
                                    "destro"),
                                "description", "piano di costruzione: frontale, superiore o destro"),
                            "denti", O(
                                "type", "integer",
                                "description", "numero di denti (z)"),
                            "modulo_mm", O(
                                "type", "number",
                                "description", "il modulo m in mm. Diametro primitivo = m x z, esterno = m x (z+2)."),
                            "spessore_mm", O(
                                "type", "number",
                                "description", "larghezza di fascia"),
                            "angolo_pressione_gradi", O(
                                "type", "number",
                                "description", "predefinito 20, che e' lo standard"),
                            "foro_mm", O(
                                "type", "number",
                                "description", "diametro del foro centrale; omettilo per una ruota piena"),
                            "cava_linguetta", O(
                                "type", "boolean",
                                "description", "cava per linguetta nel foro, con larghezza e profondita' "
                                    + "normalizzate (ISO/R 773, DIN 6885-1) ricavate dal diametro del "
                                    + "foro. Non chiedere le quote della cava: le sceglie la norma.")),
                        "required", A(
                            "denti",
                            "modulo_mm",
                            "spessore_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "asola",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Un foro allungato: il fissaggio che si registra in montaggio.",
                    Descrizione =
                        "Taglia un'ASOLA su una faccia: un foro allungato, con le "
                        + "estremita' tonde. E' quello che permette di REGISTRARE in opera — "
                        + "una staffa con due asole si allinea, una con due fori tondi o "
                        + "entra o si rifa'.\n\nLA LUNGHEZZA E' DA FUORI A FUORI, come la si "
                        + "misura sul pezzo e come si scrive a disegno; la LARGHEZZA e' il "
                        + "diametro delle estremita', cioe' il diametro della vite piu' il "
                        + "gioco. Per una M10 che deve scorrere 40 mm: larghezza 11, "
                        + "lunghezza 51.\n\n«angolo_gradi» ruota l'asola nel piano della "
                        + "faccia: 0 e' orizzontale, 90 verticale. Serve quando la "
                        + "registrazione va in una direzione precisa.\n\nNon e' una tasca "
                        + "rettangolare. «taglio_rettangolare» ci somiglia ma ha gli spigoli "
                        + "vivi, e la vite ci balla dentro invece di scorrere.",
                    Precondizione = "corpo_solido_e_faccia",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "faccia_uid", O(
                                "type", "string",
                                "description", "una faccia che c'e' gia', presa dallo stato"),
                            "faccia_rif", O(
                                "type", "object",
                                "description", "la faccia creata da un passo precedente di questo stesso piano. "
                                    + "Metti questo OPPURE faccia_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- faccia_di_fondo: il fondo della tasca (c'e' solo se la "
                                    + "tasca e' cieca)\n- faccia_inferiore: la faccia sul piano di "
                                    + "schizzo, il fondo dell'estrusione\n- faccia_superiore: la faccia "
                                    + "in cima all'estrusione, quella opposta al piano di schizzo",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "faccia_di_fondo",
                                            "faccia_inferiore",
                                            "faccia_superiore"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "x_mm", O(
                                "type", "number",
                                "description", "COORDINATE DEL MODELLO in mm, non coordinate della faccia. Prendi "
                                    + "i due assi che restano togliendo quello della normale della "
                                    + "faccia, in ordine X, Y, Z: normale lungo Z -> x_mm e' la X del "
                                    + "modello e y_mm la Y; normale lungo Y -> x_mm e' la X e y_mm la Z; "
                                    + "normale lungo X -> x_mm e' la Y e y_mm la Z. Sono gli stessi "
                                    + "numeri di «punto_mm» che leggi nello stato del documento, quindi "
                                    + "puoi partire da quelli. La terza coordinata non serve: e' il "
                                    + "piano della faccia."),
                            "y_mm", O(
                                "type", "number",
                                "description", "vedi x_mm"),
                            "lunghezza_mm", O(
                                "type", "number",
                                "description", "lunghezza TOTALE da fuori a fuori, non la corsa. Dev'essere "
                                    + "maggiore della larghezza."),
                            "larghezza_mm", O(
                                "type", "number",
                                "description", "larghezza dell'asola, cioe' il diametro delle estremita' tonde"),
                            "angolo_gradi", O(
                                "type", "number",
                                "description", "rotazione nel piano della faccia, predefinito 0"),
                            "passante", O(
                                "type", "boolean",
                                "description", "predefinito: si'"),
                            "profondita_mm", O(
                                "type", "number",
                                "description", "solo se non passante")),
                        "required", A(
                            "x_mm",
                            "y_mm",
                            "lunghezza_mm",
                            "larghezza_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "estrudi_su_faccia",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Fa CRESCERE materiale da una faccia: un rialzo, un labbro, un "
                        + "distanziale, un'aletta.",
                    Descrizione =
                        "Disegna un contorno su una faccia che c'e' gia' e lo estrude in "
                        + "FUORI, aggiungendo materiale al pezzo.\n\nE' L'UNICA OPERAZIONE CHE "
                        + "AGGIUNGE. Tutte le altre su un pezzo esistente tolgono (fori, "
                        + "tagli, asole), svuotano (guscio) o spostano. Senza questa non si "
                        + "possono fare: il labbro di tenuta di un coperchio, i distanziali "
                        + "su cui appoggia qualcosa, un'aletta di fissaggio, una torretta "
                        + "per una vite, un rialzo attorno a un foro.\n\nI CONTORNI SONO NELLE "
                        + "COORDINATE DELLA FACCIA, come per «taglio_rettangolare»: (0, 0) "
                        + "e' il centro della faccia. Piu' contorni annidati fanno una "
                        + "cornice — quello fuori e quello dentro — ed e' cosi' che si fa un "
                        + "labbro attorno a un bordo invece di un blocco pieno.\n\n«unisci» a "
                        + "vero (predefinito) fonde il rialzo con il pezzo. A falso nasce un "
                        + "CORPO SEPARATO: quasi mai quello che si vuole, e l'albero cresce "
                        + "uguale, quindi lo si nota solo contando i corpi.",
                    Precondizione = "corpo_solido_e_faccia",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "faccia_uid", O(
                                "type", "string",
                                "description", "la faccia da cui far crescere il materiale"),
                            "faccia_rif", O(
                                "type", "object",
                                "description", "la faccia creata da un passo precedente di questo stesso piano. "
                                    + "Metti questo OPPURE faccia_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- faccia_di_fondo: il fondo della tasca (c'e' solo se la "
                                    + "tasca e' cieca)\n- faccia_inferiore: la faccia sul piano di "
                                    + "schizzo, il fondo dell'estrusione\n- faccia_superiore: la faccia "
                                    + "in cima all'estrusione, quella opposta al piano di schizzo",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "faccia_di_fondo",
                                            "faccia_inferiore",
                                            "faccia_superiore"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "contorni", O(
                                "type", "array",
                                "description", "la sagoma da estrudere, nelle coordinate della faccia. Ogni "
                                    + "contorno e' {\"vertici\": [[x, y], ...]} — spezzata chiusa, "
                                    + "l'ultimo punto si chiude da solo — oppure {\"cerchio\": [x, y], "
                                    + "\"diametro_mm\": d}. Due contorni annidati fanno una cornice.",
                                "items", O(
                                    "type", "object")),
                            "altezza_mm", O(
                                "type", "number",
                                "description", "quanto sporge dalla faccia"),
                            "unisci", O(
                                "type", "boolean",
                                "description", "predefinito: si'. A falso fa un corpo separato.")),
                        "required", A(
                            "contorni",
                            "altezza_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "foro_guidato",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Un foro come si fa un foro: creazione guidata, norma ISO, "
                        + "filettato o di passaggio, lamato o svasato, anche piu' fori in "
                        + "una volta.",
                    Descrizione =
                        "IL MODO GIUSTO DI FARE UN FORO: la creazione guidata fori di "
                        + "SolidWorks, norma ISO. Da preferire SEMPRE a «taglio_cilindrico» "
                        + "quando il foro alloggia una vite, un perno o una filettatura.\n\nLe "
                        + "misure le prende dalla norma: tu dai la taglia della vite («M8») "
                        + "e il tipo, non i diametri. Un foro filettato M8 esce con il "
                        + "preforo 6.8 e il filetto; uno di passaggio M8 esce 9.\n\nPiu' fori "
                        + "uguali si fanno in UNA SOLA chiamata mettendo piu' «posizioni»: "
                        + "e' cosi' che si disegna una serie di fori, e in officina si legge "
                        + "come una lavorazione sola.",
                    Precondizione = "corpo_solido_e_faccia",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "faccia_uid", O(
                                "type", "string",
                                "description", "una faccia che c'e' gia', presa dallo stato"),
                            "faccia_rif", O(
                                "type", "object",
                                "description", "la faccia creata da un passo precedente di questo stesso piano. "
                                    + "Metti questo OPPURE faccia_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- faccia_di_fondo: il fondo della tasca (c'e' solo se la "
                                    + "tasca e' cieca)\n- faccia_inferiore: la faccia sul piano di "
                                    + "schizzo, il fondo dell'estrusione\n- faccia_superiore: la faccia "
                                    + "in cima all'estrusione, quella opposta al piano di schizzo",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "faccia_di_fondo",
                                            "faccia_inferiore",
                                            "faccia_superiore"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "tipo", O(
                                "type", "string",
                                "enum", A(
                                    "passaggio",
                                    "filettato",
                                    "lamato",
                                    "svasato"),
                                "description", "passaggio = foro liscio per far passare la vite; filettato = "
                                    + "maschiato, la vite ci si avvita; lamato = con la sede per la "
                                    + "testa cilindrica (ISO 4762); svasato = con la sede conica per la "
                                    + "testa svasata (ISO 10642). Predefinito: passaggio."),
                            "taglia", O(
                                "type", "string",
                                "enum", A(
                                    "M3",
                                    "M4",
                                    "M5",
                                    "M6",
                                    "M8",
                                    "M10",
                                    "M12",
                                    "M14",
                                    "M16",
                                    "M20",
                                    "M24"),
                                "description", "la taglia della VITE, non il diametro del foro. Copiala da questo "
                                    + "elenco: e' quello che la tabella ISO di SolidWorks conosce."),
                            "posizioni", O(
                                "type", "array",
                                "description", "dove vanno i fori, sul piano della faccia. Piu' fori uguali "
                                    + "stanno in una chiamata sola.",
                                "items", O(
                                    "type", "object",
                                    "properties", O(
                                        "x_mm", O(
                                            "type", "number",
                                            "description", "COORDINATE DEL MODELLO in mm, non coordinate della faccia. Prendi "
                                                + "i due assi che restano togliendo quello della normale della "
                                                + "faccia, in ordine X, Y, Z: normale lungo Z -> x_mm e' la X del "
                                                + "modello e y_mm la Y; normale lungo Y -> x_mm e' la X e y_mm la Z; "
                                                + "normale lungo X -> x_mm e' la Y e y_mm la Z. Sono gli stessi "
                                                + "numeri di «punto_mm» che leggi nello stato del documento, quindi "
                                                + "puoi partire da quelli. La terza coordinata non serve: e' il "
                                                + "piano della faccia."),
                                        "y_mm", O(
                                            "type", "number",
                                            "description", "vedi x_mm")),
                                    "required", A(
                                        "x_mm",
                                        "y_mm"))),
                            "passante", O(
                                "type", "boolean",
                                "description", "predefinito: si'"),
                            "profondita_mm", O(
                                "type", "number",
                                "description", "obbligatoria se non passante")),
                        "required", A(
                            "taglia",
                            "posizioni")),
                },
                new OperazioneDisegno
                {
                    Nome = "guscio",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Svuota un corpo pieno lasciando una parete di spessore costante: "
                        + "scatole, carter, coperchi.",
                    Descrizione =
                        "Il guscio: prende un corpo pieno e lo svuota, lasciando una "
                        + "parete di spessore costante. Le facce indicate in «facce_uid» "
                        + "vengono TOLTE, cioe' restano aperte — una faccia sola apre una "
                        + "scatola da un lato, due facce opposte fanno un tubo passante.\n\nLo "
                        + "spessore si toglie verso l'INTERNO: l'ingombro esterno non "
                        + "cambia, come aprendo una scatola. Con «verso_esterno» lo spessore "
                        + "si aggiunge da fuori, e serve quando e' l'ingombro INTERNO la "
                        + "quota fissa — un vano che deve restare della misura data.\n\nUn "
                        + "guscio senza nessuna faccia tolta e' un corpo cavo chiuso: "
                        + "legittimo ma raro, e quasi sempre il segno che manca una faccia "
                        + "nella richiesta. Per questo va confermato con «tutto_chiuso».",
                    Precondizione = "corpo_solido_e_faccia",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "facce_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "le facce da TOGLIERE (restano aperte), prese dallo stato del documento"),
                            "facce_rif", O(
                                "type", "array",
                                "description", "le facce da TOGLIERE, quando le crea un passo PRECEDENTE di questo "
                                    + "stesso piano e quindi non hanno ancora un identificativo. Metti "
                                    + "questo OPPURE «facce_uid», non tutti e due.\n\nUn elemento per "
                                    + "ogni faccia da aprire: «fai un blocco e poi svuotalo dall'alto» "
                                    + "e' un piano solo.\n\nGeneri ammessi:\n- faccia_superiore: la "
                                    + "faccia in cima all'estrusione, quella opposta al piano di "
                                    + "schizzo\n- faccia_inferiore: la faccia sul piano di schizzo\n"
                                    + "- faccia_di_fondo: il fondo di una tasca cieca\n"
                                    + "- faccia_del_foro: la parete cilindrica di un foro",
                                "items", O(
                                    "type", "object",
                                    "properties", O(
                                        "passo", O(
                                            "type", "integer",
                                            "description", "il numero d'ordine del passo che crea la geometria, "
                                                + "contando da 1. Deve essere un passo PRECEDENTE a questo."),
                                        "genere", O(
                                            "type", "string",
                                            "enum", A(
                                                "faccia_superiore", "faccia_inferiore",
                                                "faccia_di_fondo", "faccia_del_foro"))),
                                    "required", A("passo", "genere"))),
                            "spessore_mm", O(
                                "type", "number",
                                "description", "spessore della parete che resta"),
                            "verso_esterno", O(
                                "type", "boolean",
                                "description", "predefinito no: lo spessore si toglie verso l'interno e "
                                    + "l'ingombro esterno non cambia"),
                            "tutto_chiuso", O(
                                "type", "boolean",
                                "description", "conferma esplicita per un guscio senza nessuna faccia tolta. Di "
                                    + "solito manca una faccia: usalo solo se e' davvero questo che "
                                    + "serve.")),
                        "required", A(
                            "spessore_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "modifica_quota",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Cambia una quota di una lavorazione che c'e' gia', senza rifare niente.",
                    Descrizione =
                        "Cambia il VALORE di una quota — uno spessore, una profondita', un "
                        + "diametro, un raggio — su una lavorazione che il pezzo ha "
                        + "gia'.\n\nE' LA RICHIESTA PIU' FREQUENTE IN OFFICINA: «quel foro "
                        + "portalo a M10», «lo spessore fallo 3». Prima l'unico modo era "
                        + "cancellare e rifare, cioe' perdere tutto quello che veniva dopo — "
                        + "fori, raccordi, nervature costruite su quella geometria.\n\nIL NOME "
                        + "DELLA QUOTA LO TROVI NELLO STATO DEL DOCUMENTO: ogni lavorazione "
                        + "porta le sue, con nome e valore in mm (per esempio "
                        + "«D1@Svuota1@Parte3.Part» = 6). Copia il nome cosi' com'e'.\n\nIl "
                        + "valore cambia in TUTTE le configurazioni: cambiarne una sola "
                        + "lascerebbe il pezzo diverso a seconda di come lo si guarda.\n\nSe "
                        + "la quota cambia ma il volume del pezzo no, viene detto: quasi "
                        + "sempre vuol dire che quella quota non comanda la geometria che "
                        + "avevi in mente.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "quota", O(
                                "type", "string",
                                "description", "il nome della quota, copiato dallo stato del documento"),
                            "valore_mm", O(
                                "type", "number",
                                "description", "il valore nuovo, in millimetri")),
                        "required", A(
                            "quota",
                            "valore_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "sopprimi_lavorazione",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "SPEGNE una lavorazione dell'albero, senza cancellarla. Si riaccende.",
                    Descrizione =
                        "Spegne una lavorazione: il pezzo torna com'era senza di lei, ma lei "
                        + "resta nell'albero e si puo' riaccendere con «riattiva».\n\nE' la "
                        + "strada da prendere quando vuoi VEDERE il pezzo senza una "
                        + "lavorazione senza impegnarti. Per toglierla davvero c'e' "
                        + "«elimina_lavorazione», che non si disfa.\n\nIL NOME DELLA "
                        + "LAVORAZIONE STA NELLO STATO DEL DOCUMENTO, dentro «feature» (per "
                        + "esempio «Taglio-Estrusione2»). Copialo cosi' com'e'.\n\nI FIGLI SI "
                        + "SPENGONO CON LEI, ed e' quasi sempre cio' che si vuole: sopprimere "
                        + "un foro spegne anche i suoi raccordi. La risposta dice quali sono "
                        + "stati.\n\nDOPO, GLI IDENTIFICATIVI NON VALGONO PIU'. Sopprimere fa "
                        + "sparire delle facce, e gli identificativi (F1, E4) si rinumerano "
                        + "per area a ogni lettura: rileggi il pezzo prima del passo seguente.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "il nome della lavorazione, copiato dallo stato del documento"),
                            "riattiva", O(
                                "type", "boolean",
                                "description", "a vero RIACCENDE una lavorazione soppressa, invece di spegnerla. "
                                    + "Predefinito: falso.")),
                        "required", A(
                            "feature_uid")),
                },
                new OperazioneDisegno
                {
                    Nome = "elimina_lavorazione",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "CANCELLA una lavorazione dall'albero. Non si disfa con un'altra operazione.",
                    Descrizione =
                        "Toglie una lavorazione dall'albero per sempre. Lo schizzo che aveva "
                        + "assorbito se ne va con lei, cosi' nell'albero non resta niente di "
                        + "orfano.\n\nNON SI DISFA con un'altra operazione: l'unica strada "
                        + "indietro e' lo strumento «annulla», e solo se non hai fatto "
                        + "altro dopo. Se vuoi solo vedere il pezzo senza quella lavorazione, "
                        + "usa «sopprimi_lavorazione», che si riaccende.\n\nSE QUALCOSA "
                        + "DIPENDE DA LEI, MI RIFIUTO e ti dico cosa. Cancellare una "
                        + "lavorazione da cui ne dipendono altre le farebbe sparire tutte, e "
                        + "SOLIDWORKS lo fa senza chiedere: se e' proprio lei che vuoi "
                        + "togliere, togli prima cio' che ci sta sopra.\n\nIL NOME DELLA "
                        + "LAVORAZIONE STA NELLO STATO DEL DOCUMENTO, dentro «feature». "
                        + "Copialo cosi' com'e'.\n\nDOPO, GLI IDENTIFICATIVI NON VALGONO "
                        + "PIU': rileggi il pezzo prima del passo seguente.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "il nome della lavorazione, copiato dallo stato del documento")),
                        "required", A(
                            "feature_uid")),
                },
                new OperazioneDisegno
                {
                    Nome = "raccordo_spigoli",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Raccordo a raggio costante su uno o piu' spigoli.",
                    Descrizione =
                        "Arrotonda uno o piu' spigoli con lo stesso raggio. Gli spigoli si "
                        + "indicano con gli identificativi presi dallo stato del documento.",
                    Precondizione = "corpo_solido_e_spigolo",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "spigoli_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "identificativi di spigoli che ci sono GIA', presi dallo stato del documento"),
                            "spigoli_rif", O(
                                "type", "object",
                                "description", "gli spigoli creati da un passo precedente di questo stesso piano "
                                    + "— per esempio «arrotonda i quattro angoli della piastra che fai "
                                    + "al passo 1». Quanti spigoli siano si sa solo dentro SolidWorks. "
                                    + "Metti questo OPPURE spigoli_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- spigoli_inferiori: gli spigoli del bordo sul fondo "
                                    + "dell'estrusione\n- spigoli_laterali: gli spigoli lungo la "
                                    + "direzione di estrusione, i verticali\n- spigoli_superiori: gli "
                                    + "spigoli del bordo in cima all'estrusione\n- tutti_gli_spigoli: "
                                    + "tutti gli spigoli creati da quel passo",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "spigoli_inferiori",
                                            "spigoli_laterali",
                                            "spigoli_superiori",
                                            "tutti_gli_spigoli"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "raggio_mm", O(
                                "type", "number")),
                        "required", A(
                            "raggio_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "ripeti_circolare",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Ripete una lavorazione in circolo attorno a un asse o a uno spigolo dritto.",
                    Descrizione =
                        "Ripete una lavorazione gia' fatta, in circolo attorno a un asse. "
                        + "La lavorazione e l'asse si indicano con gli identificativi presi "
                        + "dallo stato del documento.\n\nL'ASSE PUO' ESSERE UNO SPIGOLO DRITTO "
                        + "del pezzo — quelli che lo stato elenca come «E1», «E2» — oppure "
                        + "un asse di riferimento gia' nel documento.\n\nPer ripetere attorno "
                        + "a un FORO serve un asse che passi per il suo centro: il bordo del "
                        + "foro e' un cerchio, non una retta, e come direzione viene "
                        + "rifiutato. Se quell'asse non c'e' ancora, fallo prima con "
                        + "«crea_asse».",
                    Precondizione = "feature_e_direzione",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "cosa ripetere, se c'e' gia' nell'albero"),
                            "feature_rif", O(
                                "type", "object",
                                "description", "la lavorazione fatta da un passo precedente di questo stesso "
                                    + "piano — «fai un foro e poi ripetilo 6 volte». Metti questo OPPURE "
                                    + "feature_uid, non tutti e due.\n\nGeneri ammessi:\n- feature: la "
                                    + "lavorazione fatta da quel passo, per ripeterla",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "feature"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "asse_uid", O(
                                "type", "string",
                                "description", "attorno a cosa: un asse gia' nel documento, oppure uno spigolo "
                                    + "DRITTO del pezzo («E1», «E2»...) preso dallo stato"),
                            "asse_rif", O(
                                "type", "object",
                                "description", "l'asse creato da un passo precedente di questo stesso piano — "
                                    + "«fai l'asse del foro e poi ripeti la lavorazione 6 volte "
                                    + "attorno». Metti questo OPPURE asse_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- asse: l'asse creato dal passo, dal centro di un foro o "
                                    + "di un albero",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "asse"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "numero_istanze", O(
                                "type", "integer"),
                            "angolo_totale_gradi", O(
                                "type", "number",
                                "description", "predefinito: 360")),
                        "required", A(
                            "numero_istanze")),
                },
                new OperazioneDisegno
                {
                    Nome = "ripeti_lineare",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Ripete una lavorazione in fila, o in griglia su due direzioni: "
                        + "otto fori in fila si fanno cosi'.",
                    Descrizione =
                        "Ripete una lavorazione gia' fatta, in fila lungo un asse — o in "
                        + "griglia, lungo due assi insieme. E' il gemello di "
                        + "«ripeti_circolare» e si indica allo stesso modo.\n\nOtto fori in "
                        + "fila si fanno COSI', non chiamando otto volte il foro con otto "
                        + "coordinate: il pezzo finito dichiara che quei fori sono una "
                        + "serie, e cambiarne il passo diventa una quota sola.\n\nLA DIREZIONE "
                        + "SI PRENDE DA UNO SPIGOLO DRITTO del pezzo: quelli che lo stato "
                        + "del documento elenca come «E1», «E2»... Per una fila lungo la "
                        + "piastra si indica lo spigolo lungo; per una griglia, due spigoli "
                        + "perpendicolari fra loro.\n\nVa bene anche un asse fatto con "
                        + "«crea_asse», ma quello nasce dal centro di un foro ed e' "
                        + "perpendicolare alla faccia forata: serve per ripetere NELLO "
                        + "SPESSORE, quasi mai per una fila.\n\n«asse1_uid» e' obbligatorio. "
                        + "«asse2_uid» serve solo per una griglia — una piastra forata a "
                        + "matrice; per una fila sola omettilo insieme a passo2 e "
                        + "numero_istanze2.\n\nIl verso lo decide l'asse, non un segno nei "
                        + "parametri: se la ripetizione esce dalla parte sbagliata usa "
                        + "«capovolgi_1».",
                    Precondizione = "feature_e_direzione",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "cosa ripetere, se c'e' gia' nell'albero"),
                            "feature_rif", O(
                                "type", "object",
                                "description", "la lavorazione fatta da un passo precedente di questo piano. "
                                    + "Metti questo OPPURE feature_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- feature: la lavorazione fatta da quel passo, per "
                                    + "ripeterla",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "feature"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "asse1_uid", O(
                                "type", "string",
                                "description", "direzione 1: uno spigolo DRITTO del pezzo («E1», «E2»...) preso "
                                    + "dallo stato, oppure un asse gia' nel documento"),
                            "asse1_rif", O(
                                "type", "object",
                                "description", "la direzione 1, se l'asse lo crea un passo precedente di questo "
                                    + "piano con «crea_asse». Uno OPPURE l'altro.\n\nGeneri ammessi:\n- "
                                    + "asse: l'asse creato dal passo, dal centro di un foro o di un "
                                    + "albero",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "asse"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "passo1_mm", O(
                                "type", "number",
                                "description", "distanza fra le istanze"),
                            "numero_istanze1", O(
                                "type", "integer",
                                "description", "quante copie in tutto, l'originale compreso"),
                            "capovolgi_1", O(
                                "type", "boolean",
                                "description", "inverte il verso"),
                            "asse2_uid", O(
                                "type", "string",
                                "description", "direzione 2, solo per una griglia"),
                            "passo2_mm", O(
                                "type", "number",
                                "description", "solo con asse2_uid"),
                            "numero_istanze2", O(
                                "type", "integer",
                                "description", "solo con asse2_uid"),
                            "capovolgi_2", O(
                                "type", "boolean",
                                "description", "inverte il verso 2")),
                        "required", A(
                            "passo1_mm",
                            "numero_istanze1")),
                },
                new OperazioneDisegno
                {
                    Nome = "smusso_spigoli",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Smussa uno o piu' spigoli: bava, invito di montaggio, cianfrino da saldare.",
                    Descrizione =
                        "Smusso su uno o piu' spigoli. E' il gemello di «raccordo_spigoli» "
                        + "e si indica allo stesso modo.\n\nPredefinito 45 gradi, che e' lo "
                        + "smusso di gran lunga piu' comune. Per uno smusso non simmetrico — "
                        + "un cianfrino di saldatura, un invito che deve entrare in una sede "
                        + "— metti «secondo_lato_mm» e l'angolo viene ignorato.\n\nSmusso o "
                        + "raccordo non e' una preferenza estetica: uno smusso si fa con una "
                        + "fresa o una lima, un raccordo vuole un utensile di raggio. Se "
                        + "l'utente dice «smussa», non arrotondare.",
                    Precondizione = "corpo_solido_e_spigolo",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "spigoli_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "spigoli che ci sono GIA', presi dallo stato"),
                            "spigoli_rif", O(
                                "type", "object",
                                "description", "gli spigoli creati da un passo precedente di questo piano. Metti "
                                    + "questo OPPURE spigoli_uid, non tutti e due.\n\nGeneri ammessi:\n- "
                                    + "spigoli_inferiori: gli spigoli del bordo sul fondo "
                                    + "dell'estrusione\n- spigoli_laterali: gli spigoli lungo la "
                                    + "direzione di estrusione, i verticali\n- spigoli_superiori: gli "
                                    + "spigoli del bordo in cima all'estrusione\n- tutti_gli_spigoli: "
                                    + "tutti gli spigoli creati da quel passo",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "spigoli_inferiori",
                                            "spigoli_laterali",
                                            "spigoli_superiori",
                                            "tutti_gli_spigoli"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "lato_mm", O(
                                "type", "number",
                                "description", "quanto materiale si toglie, misurato su una faccia"),
                            "angolo_gradi", O(
                                "type", "number",
                                "description", "predefinito 45; ignorato se dai «secondo_lato_mm»"),
                            "secondo_lato_mm", O(
                                "type", "number",
                                "description", "solo per uno smusso non simmetrico: il lato sull'altra faccia")),
                        "required", A(
                            "lato_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "specchia",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Specchia una lavorazione rispetto a uno dei tre piani base.",
                    Descrizione =
                        "Specchia una lavorazione gia' fatta rispetto a un piano di "
                        + "simmetria. La lavorazione si indica come per "
                        + "«ripeti_circolare».\n\nIl piano e' sempre uno dei tre base, e va "
                        + "detto: sbagliarlo non sposta la geometria di poco, la mette da "
                        + "un'altra parte del pezzo, e la costruzione riesce lo stesso.\n\nSi "
                        + "specchia la LAVORAZIONE, non il corpo intero: per un pezzo "
                        + "simmetrico specchia il foro o la tasca che manca da un lato.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "cosa specchiare, se c'e' gia' nell'albero"),
                            "feature_rif", O(
                                "type", "object",
                                "description", "la lavorazione fatta da un passo precedente di questo piano. "
                                    + "Metti questo OPPURE feature_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- feature: la lavorazione fatta da quel passo, per "
                                    + "ripeterla",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "feature"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "piano", O(
                                "type", "string",
                                "enum", A(
                                    "frontale",
                                    "superiore",
                                    "destro"),
                                "description", "piano di costruzione: frontale, superiore o destro")),
                        "required", A(
                            "piano")),
                },
                new OperazioneDisegno
                {
                    Nome = "sposta_corpi",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Sposta o COPIA dei corpi gia' fatti, di una distanza data.",
                    Descrizione =
                        "Sposta i corpi indicati di «dx_mm», «dy_mm», «dz_mm». Con «copia» "
                        + "a vero li LASCIA DOVE SONO e ne mette delle copie piu' in la': e' "
                        + "cosi' che si fanno due telai uguali a una certa distanza.\n\nSERVE "
                        + "QUANDO IL PEZZO HA DUE PARTI UGUALI IN POSTI DIVERSI. Un castello "
                        + "fatto di due telai distanti 600 mm non si chiede facendo due "
                        + "volte il telaio: la seconda volta verrebbe rifiutata, perche' due "
                        + "volte la stessa operazione con gli stessi numeri fa due corpi "
                        + "sovrapposti. Si fa UN telaio, e poi lo si copia spostato di 600 "
                        + "in Z.\n\nQuali corpi: «corpi_uid» per quelli che ci sono GIA' nel "
                        + "documento (C1, C2...), «corpi_rif» per quelli che li crea un "
                        + "passo precedente di questo stesso piano. Uno o l'altro, mai tutti "
                        + "e due.\n\nSolo spostamento in linea retta: questa operazione non "
                        + "ruota niente. Se serve ruotare, dillo invece di far finta.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "corpi_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "i corpi da spostare, per identificativo dello stato del documento "
                                    + "(C1, C2...)"),
                            "corpi_rif", O(
                                "type", "object",
                                "description", "i corpi creati da un passo precedente di questo stesso piano. "
                                    + "Metti questo OPPURE corpi_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- corpi: le barre create da quel passo, tutte insieme",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "corpi"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "dx_mm", O(
                                "type", "number",
                                "description", "spostamento lungo X, in mm"),
                            "dy_mm", O(
                                "type", "number",
                                "description", "spostamento lungo Y"),
                            "dz_mm", O(
                                "type", "number",
                                "description", "spostamento lungo Z"),
                            "copia", O(
                                "type", "boolean",
                                "description", "vero: lascia gli originali e aggiunge delle copie spostate. "
                                    + "Predefinito: falso, cioe' li sposta e basta."),
                            "quante_copie", O(
                                "type", "integer",
                                "description", "quante copie, solo se «copia» e' vero. Predefinito 1. La copia "
                                    + "numero k finisce a k volte lo spostamento.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "taglio_cilindrico",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Un taglio tondo su una faccia: per un passaggio che NON e' un "
                        + "foro da viteria.",
                    Descrizione =
                        "Fa un foro su una faccia gia' esistente, alle coordinate indicate "
                        + "nel piano di quella faccia. Passante o cieco.",
                    Precondizione = "corpo_solido_e_faccia",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "faccia_uid", O(
                                "type", "string",
                                "description", "identificativo della faccia, preso dallo stato del documento. "
                                    + "Usalo per una faccia che c'e' GIA'."),
                            "faccia_rif", O(
                                "type", "object",
                                "description", "la faccia creata da un passo precedente di questo stesso piano, "
                                    + "quando il foro va su geometria che stai creando adesso e che "
                                    + "quindi non ha ancora nessun identificativo. Metti questo OPPURE "
                                    + "faccia_uid, non tutti e due.\n\nGeneri ammessi:\n- faccia_di_fondo: "
                                    + "il fondo della tasca (c'e' solo se la tasca e' cieca)\n- "
                                    + "faccia_inferiore: la faccia sul piano di schizzo, il fondo "
                                    + "dell'estrusione\n- faccia_superiore: la faccia in cima "
                                    + "all'estrusione, quella opposta al piano di schizzo",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "faccia_di_fondo",
                                            "faccia_inferiore",
                                            "faccia_superiore"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "x_mm", O(
                                "type", "number",
                                "description", "COORDINATE DEL MODELLO in mm, non coordinate della faccia. Prendi "
                                    + "i due assi che restano togliendo quello della normale della "
                                    + "faccia, in ordine X, Y, Z: normale lungo Z -> x_mm e' la X del "
                                    + "modello e y_mm la Y; normale lungo Y -> x_mm e' la X e y_mm la Z; "
                                    + "normale lungo X -> x_mm e' la Y e y_mm la Z. Sono gli stessi "
                                    + "numeri di «punto_mm» che leggi nello stato del documento, quindi "
                                    + "puoi partire da quelli. La terza coordinata non serve: e' il "
                                    + "piano della faccia."),
                            "y_mm", O(
                                "type", "number",
                                "description", "vedi x_mm"),
                            "diametro_mm", O(
                                "type", "number"),
                            "passante", O(
                                "type", "boolean",
                                "description", "predefinito: si'"),
                            "profondita_mm", O(
                                "type", "number",
                                "description", "solo se non passante")),
                        "required", A(
                            "x_mm",
                            "y_mm",
                            "diametro_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "taglio_rettangolare",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Una tasca rettangolare su una faccia.",
                    Descrizione =
                        "Scava una tasca rettangolare su una faccia esistente.",
                    Precondizione = "corpo_solido_e_faccia",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "faccia_uid", O(
                                "type", "string",
                                "description", "una faccia che c'e' gia', presa dallo stato"),
                            "faccia_rif", O(
                                "type", "object",
                                "description", "la faccia creata da un passo precedente di questo stesso piano. "
                                    + "Metti questo OPPURE faccia_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- faccia_di_fondo: il fondo della tasca (c'e' solo se la "
                                    + "tasca e' cieca)\n- faccia_inferiore: la faccia sul piano di "
                                    + "schizzo, il fondo dell'estrusione\n- faccia_superiore: la faccia "
                                    + "in cima all'estrusione, quella opposta al piano di schizzo",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "faccia_di_fondo",
                                            "faccia_inferiore",
                                            "faccia_superiore"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "x_mm", O(
                                "type", "number",
                                "description", "COORDINATE DEL MODELLO in mm, non coordinate della faccia. Prendi "
                                    + "i due assi che restano togliendo quello della normale della "
                                    + "faccia, in ordine X, Y, Z: normale lungo Z -> x_mm e' la X del "
                                    + "modello e y_mm la Y; normale lungo Y -> x_mm e' la X e y_mm la Z; "
                                    + "normale lungo X -> x_mm e' la Y e y_mm la Z. Sono gli stessi "
                                    + "numeri di «punto_mm» che leggi nello stato del documento, quindi "
                                    + "puoi partire da quelli. La terza coordinata non serve: e' il "
                                    + "piano della faccia."),
                            "y_mm", O(
                                "type", "number",
                                "description", "vedi x_mm"),
                            "lunghezza_mm", O(
                                "type", "number"),
                            "larghezza_mm", O(
                                "type", "number"),
                            "passante", O(
                                "type", "boolean",
                                "description", "predefinito: no"),
                            "profondita_mm", O(
                                "type", "number")),
                        "required", A(
                            "x_mm",
                            "y_mm",
                            "lunghezza_mm",
                            "larghezza_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "applica_materiale",
                    Famiglia = "proprieta",
                    Provata = true,
                    Sommario =
                        "Assegna il materiale al pezzo: senza, non c'e' peso ne' distinta utile.",
                    Descrizione =
                        "Assegna il materiale al pezzo. VA FATTO SEMPRE, su ogni pezzo che "
                        + "disegni: senza materiale non c'e' densita', senza densita' non "
                        + "c'e' peso, e la distinta di taglio esce con la colonna del "
                        + "materiale vuota — cioe' non serve a comprare l'acciaio.\n\nScrivi "
                        + "il nome come lo direbbe un progettista («S235JR», «AISI 304», "
                        + "«Lega 6061»): la grafia esatta la trova la postazione, che ha le "
                        + "librerie vere. Se il nome corrisponde a piu' materiali o a "
                        + "nessuno, l'operazione fallisce elencandoti quelli che ci sono, e "
                        + "allora si chiede all'utente: fra 1.0037 e 1.0116 non si sceglie a "
                        + "caso.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "materiale", O(
                                "type", "string",
                                "description", "per esempio «S235JR», «AISI 304», «Lega 6061»"),
                            "archivio", O(
                                "type", "string",
                                "description", "opzionale: il nome dell'archivio, se sai in quale cercare. "
                                    + "Lascialo stare se non lo sai.")),
                        "required", A(
                            "materiale")),
                },
                new OperazioneDisegno
                {
                    Nome = "crea_asse",
                    Famiglia = "riferimenti",
                    Provata = true,
                    Sommario =
                        "Un asse lungo il centro di un foro o di un albero: serve per "
                        + "ripetere ATTORNO a quel foro.",
                    Descrizione =
                        "Crea un asse di riferimento lungo il centro di una faccia "
                        + "cilindrica o conica gia' esistente — il foro o l'albero attorno a "
                        + "cui poi ripetere qualcosa con «ripeti_circolare».\n\nQUANDO SERVE "
                        + "DAVVERO: per ripetere attorno a un FORO. Il bordo di un foro e' "
                        + "un cerchio, non una retta, e come direzione viene rifiutato; "
                        + "l'asse invece passa per il suo centro.\n\nQUANDO NON SERVE: per una "
                        + "fila o una griglia. Li' la direzione si prende da uno spigolo "
                        + "dritto del pezzo, che nello stato c'e' gia' («E1», «E2»...). "
                        + "L'asse di un foro punta NELLO SPESSORE della piastra: ripetere "
                        + "lungo quello non fa una fila.\n\nSolo facce cilindriche o coniche: "
                        + "una faccia piana non ha un asse. Nello stato del documento sono "
                        + "quelle con «piana: false».",
                    Precondizione = "corpo_solido_e_faccia",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "faccia_uid", O(
                                "type", "string",
                                "description", "la faccia cilindrica o conica, presa dallo stato del documento"),
                            "faccia_rif", O(
                                "type", "object",
                                "description", "la faccia creata da un passo precedente di questo piano. Metti "
                                    + "questo OPPURE faccia_uid, non tutti e due.\n\nGeneri ammessi:\n- "
                                    + "faccia_di_fondo: il fondo della tasca (c'e' solo se la tasca e' "
                                    + "cieca)\n- faccia_inferiore: la faccia sul piano di schizzo, il "
                                    + "fondo dell'estrusione\n- faccia_superiore: la faccia in cima "
                                    + "all'estrusione, quella opposta al piano di schizzo",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "faccia_di_fondo",
                                            "faccia_inferiore",
                                            "faccia_superiore"))),
                                "required", A(
                                    "passo",
                                    "genere"))),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "crea_piano",
                    Famiglia = "riferimenti",
                    Provata = true,
                    Sommario =
                        "Un piano di riferimento nuovo, parallelo a un piano base e "
                        + "spostato di una quota.",
                    Descrizione =
                        "Crea un piano di riferimento parallelo a un piano base (frontale, "
                        + "superiore o destro) e spostato di una quota lungo la sua normale. "
                        + "Serve per costruire uno schizzo a una certa altezza o profondita' "
                        + "senza toccare il piano base — «un piano parallelo al superiore, a "
                        + "600 mm» per lavorare a meta' altezza di un montante.\n\nDistanza "
                        + "negativa: dalla parte opposta. Distanza zero: un piano "
                        + "coincidente col base, come oggetto distinto.\n\nSolo piani base: "
                        + "questa versione non sa appoggiare il piano nuovo su una faccia "
                        + "del pezzo.",
                    Precondizione = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "piano", O(
                                "type", "string",
                                "enum", A(
                                    "frontale",
                                    "superiore",
                                    "destro"),
                                "description", "piano di costruzione: frontale, superiore o destro"),
                            "distanza_mm", O(
                                "type", "number",
                                "description", "quanto si sposta dal piano base. Negativo per l'altro verso.")),
                        "required", A(
                            "distanza_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "cordone_saldatura",
                    Famiglia = "saldature",
                    Provata = true,
                    Sommario =
                        "Cordone di saldatura cosmetico fra due gruppi di corpi.",
                    Descrizione =
                        "Mette un cordone di saldatura cosmetico fra due insiemi di facce. "
                        + "Non toglie ne' aggiunge materiale al modello: documenta la "
                        + "saldatura, e finisce nelle tavole e nella distinta.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "da_corpi_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "i corpi da cui parte il cordone, per identificativo preso dallo "
                                    + "stato del documento (C1, C2...). Usa questo per corpi che ci sono "
                                    + "GIA'; l'altro campo per corpi che li crea un passo precedente di "
                                    + "questo piano. Uno o l'altro, mai tutti e due."),
                            "da_corpi_rif", O(
                                "type", "object",
                                "description", "il primo lato della saldatura\n\nGeneri ammessi:\n- corpi: le barre "
                                    + "create da quel passo, tutte insieme",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "corpi"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "a_corpi_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "i corpi su cui arriva il cordone, per identificativo preso dallo "
                                    + "stato del documento (C1, C2...). Usa questo per corpi che ci sono "
                                    + "GIA'; l'altro campo per corpi che li crea un passo precedente di "
                                    + "questo piano. Uno o l'altro, mai tutti e due."),
                            "a_corpi_rif", O(
                                "type", "object",
                                "description", "il secondo lato della saldatura\n\nGeneri ammessi:\n- corpi: le "
                                    + "barre create da quel passo, tutte insieme",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "corpi"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "lato_mm", O(
                                "type", "number",
                                "description", "lato del cordone (a)"),
                            "tipo", O(
                                "type", "string",
                                "enum", A(
                                    "continuo",
                                    "intermittente",
                                    "sfalsato"),
                                "description", "predefinito: continuo")),
                        "required", A(
                            "lato_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "distinta_taglio",
                    Famiglia = "saldature",
                    Provata = true,
                    Sommario =
                        "Crea o aggiorna la distinta di taglio del pezzo saldato. Va per ultima.",
                    Descrizione =
                        "Crea o aggiorna la DISTINTA DI TAGLIO del pezzo saldato: l'elenco "
                        + "delle barre con profilo, misura, lunghezza e quantita'.\n\nE' il "
                        + "documento con cui si va in officina, ed e' il motivo per cui un "
                        + "telaio si fa con i membri strutturali invece che con le "
                        + "estrusioni. Chiamala DOPO aver messo tutte le barre e fatto tutti "
                        + "i tagli: una distinta calcolata prima riporta lunghezze che poi "
                        + "cambiano.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "aggiorna_se_esiste", O(
                                "type", "boolean",
                                "description", "se la distinta c'e' gia', la ricalcola invece di crearne una "
                                    + "seconda (predefinito: si')")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "fazzoletto",
                    Famiglia = "saldature",
                    Provata = true,
                    Sommario =
                        "Fazzoletto di irrigidimento fra due facce di un nodo.",
                    Descrizione =
                        "Mette un FAZZOLETTO di rinforzo nell'angolo fra due barre: una "
                        + "piastra triangolare saldata che irrigidisce il nodo.\n\nLe due "
                        + "facce su cui si appoggia si indicano per identificativo, prese "
                        + "dallo stato del documento: e' un'operazione che si chiede DOPO "
                        + "aver fatto il telaio, guardandolo. In un angolo di tubolari le "
                        + "facce candidate sono quattro, e sceglierne due «probabili» "
                        + "vorrebbe dire mettere il rinforzo dalla parte sbagliata.",
                    Precondizione = "corpo_solido_e_faccia",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "facce_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "esattamente DUE facce, una per barra, prese dallo stato del documento"),
                            "spessore_mm", O(
                                "type", "number",
                                "description", "spessore della piastra"),
                            "lato1_mm", O(
                                "type", "number",
                                "description", "lato sulla prima faccia"),
                            "lato2_mm", O(
                                "type", "number",
                                "description", "lato sulla seconda faccia"),
                            "posizione", O(
                                "type", "string",
                                "enum", A(
                                    "inizio",
                                    "centro",
                                    "fine"),
                                "description", "dove sta lungo il nodo (predefinito: centro)")),
                        "required", A(
                            "facce_uid",
                            "spessore_mm",
                            "lato1_mm",
                            "lato2_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "membro_strutturale",
                    Famiglia = "saldature",
                    Provata = true,
                    Sommario =
                        "Barre vere di carpenteria lungo i segmenti che dai: e' cio' che "
                        + "genera la distinta di taglio.",
                    Descrizione =
                        "Crea una o piu' BARRE di un telaio saldato: membri strutturali "
                        + "veri, con il profilo preso dalla libreria dell'azienda.\n\nE' "
                        + "L'OPERAZIONE DA USARE PER TUTTO CIO' CHE E' CARPENTERIA: "
                        + "tubolari, angolari, piatti, IPE, HEA, HEB, UPN, UPE, profilati a "
                        + "U e a T — sia una barra sola sia un telaio intero.\n\nPerche' non "
                        + "basta estrudere una sezione: solo il membro strutturale genera la "
                        + "DISTINTA DI TAGLIO, porta il nome del profilo, e si lascia "
                        + "tagliare a 45 gradi agli incroci. Una barra estrusa ha la stessa "
                        + "forma e non serve a fare il pezzo.\n\nPRIMA CHIAMA "
                        + "«profili_saldatura» e copia da li' «profilo_percorso» e "
                        + "«configurazione» alla lettera. Non comporre il nome della misura: "
                        + "la libreria non e' uniforme e un nome inventato fa usare a "
                        + "SolidWorks un'altra misura, senza errore.\n\nI segmenti sono le "
                        + "linee lungo cui corrono le barre, in coordinate del modello in "
                        + "mm. Una barra dritta lunga 200 sull'asse X e' un segmento solo: "
                        + "da [0,0,0] a [200,0,0]. Un telaio rettangolare sono quattro "
                        + "segmenti che si chiudono.",
                    Precondizione = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "profilo_percorso", O(
                                "type", "string",
                                "description", "percorso completo del .sldlfp, copiato da profili_saldatura"),
                            "configurazione", O(
                                "type", "string",
                                "description", "la misura, copiata ALLA LETTERA da profili_saldatura (es. "
                                    + "«80X80X5»: maiuscole e virgole comprese)"),
                            "segmenti", O(
                                "type", "array",
                                "description", "le linee lungo cui corrono le barre, in mm nelle coordinate del modello",
                                "items", O(
                                    "type", "object",
                                    "properties", O(
                                        "da", O(
                                            "type", "array",
                                            "items", O(
                                                "type", "number"),
                                            "description", "[x, y, z] di partenza, in mm"),
                                        "a", O(
                                            "type", "array",
                                            "items", O(
                                                "type", "number"),
                                            "description", "[x, y, z] di arrivo, in mm")),
                                    "required", A(
                                        "da",
                                        "a"))),
                            "rotazione_gradi", O(
                                "type", "number",
                                "description", "rotazione del profilo attorno al proprio asse (predefinito: 0). "
                                    + "Serve quando un angolare o una U vanno girati."),
                            "trattamento_angolo", O(
                                "type", "string",
                                "enum", A(
                                    "nessuno",
                                    "quartabuono",
                                    "testa1",
                                    "testa2"),
                                "description", "come si incontrano fra loro le barre di QUESTO gruppo, DOVE SI "
                                    + "TOCCANO. «quartabuono» e' il taglio a 45 gradi negli angoli "
                                    + "(predefinito su un percorso chiuso); «testa1»/«testa2» e' una "
                                    + "barra che va in battuta sull'altra; «nessuno» le lascia "
                                    + "compenetrate e va detto solo se lo si vuole davvero.\n\nNON taglia "
                                    + "le estremita' LIBERE. Su una barra sola, o sui due capi di un "
                                    + "telaio aperto, non c'e' niente contro cui tagliare e questo "
                                    + "parametro non ha effetto."),
                            "gioco_mm", O(
                                "type", "number",
                                "description", "gioco fra le barre agli incroci, per il cordone di saldatura "
                                    + "(predefinito: 0)")),
                        "required", A(
                            "profilo_percorso",
                            "configurazione",
                            "segmenti")),
                },
                new OperazioneDisegno
                {
                    Nome = "taglia_membri",
                    Famiglia = "saldature",
                    Provata = true,
                    Sommario =
                        "Taglia o estende le barre dove si incontrano.",
                    Descrizione =
                        "Taglia le barre dove si incontrano, o le estende fino a toccarsi. "
                        + "E' il «taglia/estendi» della barra di saldature.\n\nServe quando "
                        + "due gruppi di barre si incrociano e non sono stati trattati agli "
                        + "angoli: senza, i corpi restano compenetrati e la distinta di "
                        + "taglio riporta lunghezze che in officina non tornano.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "corpi_da_tagliare_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "le barre da accorciare, per identificativo preso dallo stato del "
                                    + "documento (C1, C2...). Usa questo per corpi che ci sono GIA'; "
                                    + "l'altro campo per corpi che li crea un passo precedente di questo "
                                    + "piano. Uno o l'altro, mai tutti e due."),
                            "corpi_da_tagliare_rif", O(
                                "type", "object",
                                "description", "le barre da accorciare, create da un passo precedente\n\nGeneri "
                                    + "ammessi:\n- corpi: le barre create da quel passo, tutte insieme",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "corpi"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "corpi_di_taglio_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "le barre contro cui tagliare, per identificativo preso dallo "
                                    + "stato del documento (C1, C2...). Usa questo per corpi che ci sono "
                                    + "GIA'; l'altro campo per corpi che li crea un passo precedente di "
                                    + "questo piano. Uno o l'altro, mai tutti e due."),
                            "corpi_di_taglio_rif", O(
                                "type", "object",
                                "description", "le barre contro cui tagliare, da un passo precedente\n\nGeneri "
                                    + "ammessi:\n- corpi: le barre create da quel passo, tutte insieme",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "corpi"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "condizione", O(
                                "type", "string",
                                "enum", A(
                                    "quartabuono",
                                    "testa1",
                                    "testa2",
                                    "taglio"),
                                "description", "come si chiude il taglio (predefinito: taglio)"),
                            "gioco_mm", O(
                                "type", "number",
                                "description", "gioco lasciato (predefinito: 0)")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "tappo_estremita",
                    Famiglia = "saldature",
                    Provata = true,
                    Sommario =
                        "Chiude con un tappo le estremita' aperte delle barre cave.",
                    Descrizione =
                        "Chiude con un tappo le estremita' aperte di una barra cava. Si "
                        + "mette sulle facce di estremita' create da un passo precedente, "
                        + "oppure sulle barre che ci sono gia' nel documento («C1», "
                        + "«C2»...).\n\nL'UNICA COSA CHE PUO' MANCARE E' LO SPESSORE DEL "
                        + "TAPPO. Non ha un valore predefinito, perche' e' una scelta di "
                        + "progetto: chiedilo, e chiedi SOLO quello. Il materiale non serve "
                        + "— lo si applica dopo con la sua operazione, e non cambia la forma "
                        + "del tappo.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "corpi_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "le barre di cui tappare le estremita' aperte, per identificativo "
                                    + "preso dallo stato del documento (C1, C2...). Usa questo per corpi "
                                    + "che ci sono GIA'; l'altro campo per corpi che li crea un passo "
                                    + "precedente di questo piano. Uno o l'altro, mai tutti e due."),
                            "estremita_rif", O(
                                "type", "object",
                                "description", "le facce di estremita' da tappare, da un passo precedente\n\nGeneri "
                                    + "ammessi:\n- estremita_libere: le facce di estremita' ancora aperte "
                                    + "delle barre di quel passo, quelle che si tappano",
                                "properties", O(
                                    "passo", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "genere", O(
                                        "type", "string",
                                        "enum", A(
                                            "estremita_libere"))),
                                "required", A(
                                    "passo",
                                    "genere")),
                            "spessore_mm", O(
                                "type", "number",
                                "description", "spessore del tappo"),
                            "rientro_mm", O(
                                "type", "number",
                                "description", "quanto rientra dal filo (predefinito: 0)"),
                            "smusso_mm", O(
                                "type", "number",
                                "description", "smusso del bordo (predefinito: nessuno)")),
                        "required", A(
                            "spessore_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "telaio_rettangolare",
                    Famiglia = "saldature",
                    Provata = true,
                    Sommario =
                        "Un telaio rettangolare di quattro barre, dato con due quote "
                        + "invece che con otto coordinate.",
                    Descrizione =
                        "RICETTA: un telaio rettangolare piano, quattro barre saldate agli "
                        + "angoli. E' il caso piu' comune della carpenteria, e si chiede con "
                        + "due quote invece che con otto coordinate.\n\nLe quote sono "
                        + "l'INGOMBRO ESTERNO del telaio finito. Il piano dice su quale "
                        + "piano principale sta.\n\nCome per «membro_strutturale», profilo e "
                        + "misura si copiano da «profili_saldatura».",
                    Precondizione = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "profilo_percorso", O(
                                "type", "string"),
                            "configurazione", O(
                                "type", "string"),
                            "lunghezza_mm", O(
                                "type", "number",
                                "description", "ingombro esterno"),
                            "larghezza_mm", O(
                                "type", "number",
                                "description", "ingombro esterno"),
                            "piano", O(
                                "type", "string",
                                "enum", A(
                                    "frontale",
                                    "superiore",
                                    "destro"),
                                "description", "piano di costruzione: frontale, superiore o destro"),
                            "trattamento_angolo", O(
                                "type", "string",
                                "enum", A(
                                    "quartabuono",
                                    "testa1",
                                    "testa2",
                                    "nessuno"),
                                "description", "predefinito: quartabuono, cioe' 45 gradi"),
                            "rotazione_gradi", O(
                                "type", "number"),
                            "gioco_mm", O(
                                "type", "number"),
                            "misure", O(
                                "type", "string",
                                "enum", A(
                                    "ingombro",
                                    "interasse"),
                                "description", "cosa vogliono dire le due quote: «ingombro» e' la misura del "
                                    + "pezzo finito da fuori a fuori (e' quello che intende un "
                                    + "progettista quando dice «telaio 1000x800»); «interasse» e' la "
                                    + "distanza fra gli assi delle barre. Predefinito: ingombro."),
                            "centrato", O(
                                "type", "boolean",
                                "description", "telaio centrato sull'origine (predefinito: si')")),
                        "required", A(
                            "profilo_percorso",
                            "configurazione",
                            "lunghezza_mm",
                            "larghezza_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "crea_disegno",
                    Famiglia = "tavola",
                    Provata = true,
                    Sommario =
                        "Apre la tavola del pezzo che stai guardando.",
                    Descrizione =
                        "Crea il documento di DISEGNO del pezzo aperto e lo mette davanti. "
                        + "Il pezzo resta aperto: una vista di tavola si aggancia al "
                        + "PERCORSO di un documento aperto, non a un file.\n\nIL PEZZO DEVE "
                        + "ESSERE GIA' SALVATO. Se non lo e', salvalo prima con "
                        + "«salva_documento».\n\nIl modello di tavola NON si sceglie: si usa "
                        + "quello di questa installazione, che porta il cartiglio "
                        + "dell'officina e il diedro giusto. Da qui in poi il documento "
                        + "attivo e' la TAVOLA, e le operazioni di disegno del pezzo non "
                        + "valgono piu' finche' non torni sul pezzo.",
                    Precondizione = "un pezzo o un assieme SALVATO su disco",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "formato", O(
                                "type", "string",
                                "enum", A(
                                    "A4",
                                    "A4V",
                                    "A3",
                                    "A2",
                                    "A1",
                                    "A0"),
                                "description", "formato del foglio; A3 se non dici niente")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "esporta_pdf",
                    Famiglia = "tavola",
                    Provata = true,
                    Sommario =
                        "Il PDF di TUTTI i fogli, con le pagine contate.",
                    Descrizione =
                        "Scrive il PDF della tavola.\n\nTUTTI i fogli, non solo quello che "
                        + "stai guardando: l'esportazione di serie prende il foglio "
                        + "corrente, e una tavola da tre fogli esce in officina come una "
                        + "pagina sola senza dare nessun errore. Dopo aver scritto il file "
                        + "si CONTANO le pagine e si confrontano con i fogli: se non "
                        + "tornano, te lo dico.",
                    Precondizione = "una tavola aperta",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "percorso", O(
                                "type", "string",
                                "description", "dove scrivere il PDF. Se non lo dici, accanto alla tavola con lo "
                                    + "stesso nome.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "imposta_foglio",
                    Famiglia = "tavola",
                    Provata = true,
                    Sommario =
                        "Formato e scala del foglio.",
                    Descrizione =
                        "Cambia FORMATO e SCALA del foglio attivo, e rilegge per "
                        + "controllare.\n\nIL DIEDRO NON SI TOCCA e non e' un parametro. Una "
                        + "tavola in terzo diedro ha la vista destra e la sinistra "
                        + "scambiate, non da' nessun errore, e il pezzo che ne esce e' "
                        + "specchiato: si legge quello che il modello di tavola "
                        + "dell'officina ha gia' deciso e si riscrive uguale. Nelle misure "
                        + "trovi qual e'.",
                    Precondizione = "una tavola aperta",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "formato", O(
                                "type", "string",
                                "enum", A(
                                    "A4",
                                    "A4V",
                                    "A3",
                                    "A2",
                                    "A1",
                                    "A0"),
                                "description", "formato del foglio"),
                            "scala_numeratore", O(
                                "type", "number",
                                "description", "il primo numero della scala: 1 in «1:2»"),
                            "scala_denominatore", O(
                                "type", "number",
                                "description", "il secondo numero della scala: 2 in «1:2»")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "quote_dal_modello",
                    Famiglia = "tavola",
                    Provata = true,
                    Sommario =
                        "Porta in tavola le quote che il pezzo ha gia'.",
                    Descrizione =
                        "Prende le quote dal MODELLO e le mette sulle viste.\n\nE' l'unico "
                        + "modo di quotare che questo catalogo offre, e la ragione e' di "
                        + "sicurezza: l'API di SolidWorks permette di scrivere in tavola un "
                        + "numero diverso dalla geometria, e una tavola con una quota falsa "
                        + "passa ogni controllo e manda in officina un pezzo sbagliato. Una "
                        + "quota che viene dal modello non puo' mentire.\n\nSE NON ARRIVA "
                        + "NIENTE, quasi sempre il pezzo non ha quote: succede quando gli "
                        + "schizzi sono stati fatti per coordinate. Non si ripara sulla "
                        + "tavola — si ripara sul pezzo.\n\nDi serie arrivano solo le quote "
                        + "MARCATE per il disegno, cioe' quelle scelte per essere mostrate: "
                        + "con «tutte_le_quote» arrivano anche le altre, e la tavola diventa "
                        + "parecchio piu' fitta.",
                    Precondizione = "una tavola con almeno una vista",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "tutte_le_quote", O(
                                "type", "boolean",
                                "description", "anche le quote non marcate per il disegno. Rende la tavola piu' "
                                    + "fitta: usalo quando quelle marcate non bastano."),
                            "callout_fori", O(
                                "type", "boolean",
                                "description", "anche le indicazioni dei fori guidati (M8, profondita', filetto). "
                                    + "Attive se non dici niente.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "salva_documento",
                    Famiglia = "tavola",
                    Provata = true,
                    Sommario =
                        "Salva il documento aperto.",
                    Descrizione =
                        "Salva su disco il documento attivo — pezzo o tavola.\n\nServe anche "
                        + "come PRIMO PASSO di una tavola: una vista si aggancia al percorso "
                        + "del pezzo, quindi un pezzo mai salvato non si puo' mettere in "
                        + "tavola.\n\nIl file scritto si rilegge: percorso e dimensione stanno "
                        + "nelle misure.",
                    Precondizione = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "percorso", O(
                                "type", "string",
                                "description", "dove salvare. Se non lo dici si salva dov'era gia'; un documento "
                                    + "mai salvato senza percorso e' un errore.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "vista_modello",
                    Famiglia = "tavola",
                    Provata = true,
                    Sommario =
                        "Una vista con nome: isometrica, fronte, sopra...",
                    Descrizione =
                        "Aggiunge al foglio una VISTA CON NOME del pezzo, nel punto che "
                        + "dici.\n\nLa piu' utile e' l'ISOMETRICA: da sola fa capire il pezzo "
                        + "a chi guarda la tavola, e le tre viste standard da sole spesso "
                        + "no.\n\nx_mm e y_mm sono la posizione SUL FOGLIO, con l'origine in "
                        + "basso a sinistra: su un A3 (420x297) l'angolo in alto a destra e' "
                        + "intorno a 320, 220.",
                    Precondizione = "una tavola aperta e il pezzo aperto accanto",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "vista", O(
                                "type", "string",
                                "enum", A(
                                    "isometrica",
                                    "dimetrica",
                                    "trimetrica",
                                    "fronte",
                                    "dietro",
                                    "sopra",
                                    "sotto",
                                    "destra",
                                    "sinistra"),
                                "description", "quale vista; isometrica se non dici niente"),
                            "x_mm", O(
                                "type", "number",
                                "description", "posizione sul foglio da sinistra, in mm"),
                            "y_mm", O(
                                "type", "number",
                                "description", "posizione sul foglio dal basso, in mm"),
                            "modello", O(
                                "type", "string",
                                "description", "il percorso del pezzo. Se non lo dici si usa il primo pezzo aperto.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "viste_standard",
                    Famiglia = "tavola",
                    Provata = true,
                    Sommario =
                        "Le tre viste standard, in primo diedro.",
                    Descrizione =
                        "Mette sul foglio le TRE VISTE STANDARD del pezzo, in PRIMO diedro "
                        + "— quello che si usa in Europa.\n\nIl terzo diedro non esiste in "
                        + "questo catalogo, di proposito: e' una chiamata gemella che non "
                        + "fallisce mai e produce una tavola con destra e sinistra "
                        + "scambiate. Nessuna misura se ne accorge, e il pezzo si taglia "
                        + "specchiato.\n\nLe viste si contano prima e dopo: se non ne nasce "
                        + "nessuna l'operazione fallisce, invece di lasciarti una tavola "
                        + "vuota che sembra riuscita.",
                    Precondizione = "una tavola aperta e il pezzo aperto accanto",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "modello", O(
                                "type", "string",
                                "description", "il percorso del pezzo da mettere in vista. Se non lo dici si usa "
                                    + "il primo pezzo aperto. Lo trovi in «documenti_aperti» nello stato.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "verifica_geometria_corpo",
                    Famiglia = "verifiche",
                    Provata = true,
                    Sommario =
                        "Dice se i corpi sono solidi chiusi e sani. Non tocca il pezzo.",
                    Descrizione =
                        "Guarda i CORPI del pezzo e dice se sono solidi chiusi e senza "
                        + "difetti.\n\nNon disegna e non modifica niente: e' una domanda, non "
                        + "un'operazione. Chiamala prima di consegnare un pezzo che verra' "
                        + "stampato o esportato.\n\nPer ogni corpo risponde: il tipo (solido, "
                        + "foglio, filo), quante facce ha, se e' chiuso, quante fessure e "
                        + "quanti difetti topologici, col NOME del difetto e non col numero. "
                        + "Poi un giudizio in una frase: «stampabile» vuol dire UN SOLO corpo "
                        + "solido, chiuso, senza difetti.\n\nUn corpo APERTO -- una superficie "
                        + "rimasta li' per sbaglio -- e' il caso che conta e quello che "
                        + "nessuno vede: non da' errore da nessuna parte, sembra un pezzo, e "
                        + "lo STL che ne esce non racchiude niente.\n\nATTENZIONE a cosa NON "
                        + "guarda: lo spessore delle pareti e le sovrasporgenze. Un guscio da "
                        + "tre decimi passa di qui senza una parola.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "corpo_uid", O(
                                "type", "string",
                                "description", "un corpo solo, per identificativo (per esempio «C2»). Se non lo "
                                    + "dici li guarda tutti. Gli identificativi stanno nello stato del "
                                    + "documento, dentro «corpi».")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "proprieta_di_massa",
                    Famiglia = "verifiche",
                    Provata = true,
                    Sommario =
                        "Volume, area, massa e baricentro del pezzo. Non tocca niente.",
                    Descrizione =
                        "Risponde a «quanto pesa questo pezzo»: volume, area della "
                        + "superficie, massa, densita' e baricentro.\n\nLA MASSA SENZA "
                        + "MATERIALE NON E' ZERO, E' «NON SI SA»: se al pezzo non e' stato "
                        + "dato un materiale ti rispondo null e ti dico perche'. SOLIDWORKS "
                        + "risponderebbe lo stesso usando la densita' predefinita del modello "
                        + "-- di solito 1000 kg/m3, cioe' acqua -- e un pezzo d'acciaio che "
                        + "pesa come l'acqua e' un numero credibile e sbagliato di otto "
                        + "volte. Usa prima «applica_materiale».\n\nLa massa arriva anche "
                        + "RICALCOLATA a mano (volume per densita') accanto a quella di "
                        + "SOLIDWORKS: se le due divergono, una delle misure non e' nelle "
                        + "unita' che dichiara, ed e' il modo in cui un fattore mille si fa "
                        + "vedere invece di nascondersi.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "corpo_uid", O(
                                "type", "string",
                                "description", "un corpo solo, per identificativo (per esempio «C2»). Se non lo "
                                    + "dici li pesa tutti insieme.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "ingombro_corpo",
                    Famiglia = "verifiche",
                    Provata = true,
                    Sommario =
                        "La scatola del pezzo in mm, corpo per corpo e tutta insieme.",
                    Descrizione =
                        "Risponde a «entra nella scatola 300x260x40»: da dove a dove arriva "
                        + "il pezzo su ognuno dei tre assi, e quanto misura.\n\nI punti sono "
                        + "quelli VERI del corpo, presi in sei direzioni, non la scatola "
                        + "approssimata: un ingombro dichiarato piu' grande del vero fa "
                        + "dimensionare male tutto quello che ci sta intorno.\n\nE' la "
                        + "scatola ALLINEATA AGLI ASSI del modello, non la piu' piccola "
                        + "possibile: un pezzo lungo messo in diagonale ha un ingombro molto "
                        + "piu' grande di quello che gli servirebbe girato bene, e questa "
                        + "misura non dice come girarlo.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "corpo_uid", O(
                                "type", "string",
                                "description", "un corpo solo, per identificativo (per esempio «C2»). Se non lo "
                                    + "dici misura tutti i corpi e la scatola che li contiene.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "filettatura_cosmetica",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Dice «qui c'e' un filetto» su un perno o un foro gia' fatto. Non "
                        + "taglia niente.",
                    Descrizione =
                        "Mette una FILETTATURA COSMETICA sul bordo tondo di un perno o di un "
                        + "foro: e' un'annotazione, non un taglio. Il volume del pezzo resta "
                        + "quello di prima, e se cambia fallisco -- vorrebbe dire che ha "
                        + "tagliato davvero.\n\nServe quando il filetto e' su un perno o su un "
                        + "foro fatto a mano: per un foro filettato guidato c'e' gia' "
                        + "«foro_guidato», che le misure le prende dalla norma.\n\nSI ATTACCA "
                        + "ALLO SPIGOLO, non alla faccia: il bordo circolare del cilindro o "
                        + "del foro.\n\nATTENZIONE ALLA DENOMINAZIONE: il testo che scrivi -- "
                        + "«M6x1» -- e' quello che finisce sulla tavola, PAROLA PER PAROLA. "
                        + "Questa chiamata di SOLIDWORKS ignora lo standard e la taglia: non "
                        + "c'e' nessuna tabella che controlli se «M6x1» e' giusto per un "
                        + "perno da 6. Se lo scrivi sbagliato, in officina arriva sbagliato.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "spigolo_uid", O(
                                "type", "string",
                                "description", "il bordo TONDO su cui attaccare il filetto, preso dallo stato "
                                    + "del documento (per esempio «E3»)."),
                            "denominazione", O(
                                "type", "string",
                                "description", "il testo del filetto, per esempio «M6x1». Finisce in tavola "
                                    + "com'e' scritto qui."),
                            "profondita_mm", O(
                                "type", "number",
                                "description", "quanto e' profondo il filetto, in mm. Non serve se e' passante."),
                            "passante", O(
                                "type", "boolean",
                                "description", "vero: il filetto va da parte a parte, e la profondita' non serve."),
                            "diametro_mm", O(
                                "type", "number",
                                "description", "il diametro, se vuoi che lo controlli: se non combacia con quello "
                                    + "dello spigolo te lo dico invece di filettare il bordo sbagliato. Se non "
                                    + "lo dici, lo misuro dallo spigolo.")),
                        "required", A("spigolo_uid", "denominazione")),
                },
                new OperazioneDisegno
                {
                    Nome = "raccordo_variabile",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Un raccordo che cambia raggio da un capo all'altro dello spigolo.",
                    Descrizione =
                        "Arrotonda uno spigolo con un raggio che VARIA: grande da una parte, "
                        + "piccolo dall'altra.\n\nServe dove il pezzo deve incassare un carico "
                        + "in un punto e non ha posto nell'altro -- una staffa che si allarga "
                        + "verso l'attacco, il bordo di un coperchio che si assottiglia. Per "
                        + "un raccordo uguale da capo a fondo c'e' «raccordo_spigoli».\n\nLo "
                        + "spigolo dev'essere aperto, con due estremi: su un cerchio -- il "
                        + "bordo di un foro -- non c'e' un «da» e un «a» fra cui variare."
                        + "\n\nIl raggio piu' grande deve starci sullo spigolo: se supera la "
                        + "meta' della sua lunghezza lo dico prima di provare. E dopo "
                        + "controllo che abbia tolto davvero del materiale: una feature che "
                        + "nasce senza togliere niente lascia lo spigolo vivo, e l'albero "
                        + "cresciuto per finta.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "spigolo_uid", O(
                                "type", "string",
                                "description", "lo spigolo da raccordare, preso dallo stato del documento "
                                    + "(per esempio «E7»)."),
                            "raggio_inizio_mm", O(
                                "type", "number",
                                "description", "il raggio a un capo dello spigolo, in mm."),
                            "raggio_fine_mm", O(
                                "type", "number",
                                "description", "il raggio all'altro capo, in mm.")),
                        "required", A("spigolo_uid", "raggio_inizio_mm", "raggio_fine_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "combina_corpi",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Unisce, sottrae o interseca due o piu' corpi dello stesso pezzo.",
                    Descrizione =
                        "La booleana fra CORPI dello stesso pezzo: «unisci» li fonde in uno, "
                        + "«sottrai» toglie gli altri dal principale, «interseca» tiene solo "
                        + "quello che hanno in comune.\n\nServe agli stampi e alle dime, e ai "
                        + "pezzi disegnati come piu' corpi e poi messi insieme. I corpi "
                        + "separati nascono con «sposta_corpi» e la copia.\n\nNELLA "
                        + "SOTTRAZIONE L'ORDINE CAMBIA IL PEZZO: il corpo principale e' "
                        + "quello che RESTA, gli altri sono quelli che gli vanno addosso. Se "
                        + "non dici quale, e' il primo, e te lo scrivo nella risposta.\n\nDopo "
                        + "l'unione conto i corpi: se sono ancora due, non si sono fusi -- di "
                        + "solito non si toccano davvero -- e fallisco invece di confermarti "
                        + "un pezzo diverso da quello che credi.\n\nDopo una booleana gli "
                        + "identificativi di corpi e facce NON valgono piu': rileggi il pezzo.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "operazione", O(
                                "type", "string",
                                "enum", A("unisci", "sottrai", "interseca"),
                                "description", "che booleana fare."),
                            "corpo_principale_uid", O(
                                "type", "string",
                                "description", "il corpo che RESTA (per esempio «C1»). Se non lo dici e' il "
                                    + "primo. Conta solo per «sottrai», ma scriverlo non fa mai male."),
                            "corpi_utensile_uid", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "i corpi da unire, sottrarre o intersecare col principale. Se "
                                    + "non li dici, tutti gli altri.")),
                        "required", A("operazione")),
                },
                new OperazioneDisegno
                {
                    Nome = "distanza_minima",
                    Famiglia = "verifiche",
                    Provata = true,
                    Sommario =
                        "Quanto distano due facce o due spigoli, col controllo che il "
                        + "numero non sia smentito dai suoi stessi punti.",
                    Descrizione =
                        "Misura la distanza PIU' CORTA fra due facce, due spigoli, o una "
                        + "faccia e uno spigolo. E' il modo di rispondere a «quanto materiale "
                        + "resta fra la cava della guarnizione e la faccia esterna»: due cose "
                        + "nominate e un numero, invece di un conto a mano sull'ingombro."
                        + "\n\nRisponde anche con i DUE PUNTI fra cui ha misurato, e con la "
                        + "loro distanza calcolata a parte: se il numero e i punti non "
                        + "tornano, la chiamata e' fallita e ha risposto lo stesso, e in quel "
                        + "caso fallisco invece di darti il numero.\n\nZero vuol dire che le "
                        + "due cose si toccano, e te lo dico.",
                    Precondizione = "corpo_solido_e_faccia",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "da_uid", O(
                                "type", "string",
                                "description", "identificativo della prima faccia o del primo spigolo, preso "
                                    + "dallo stato del documento (per esempio «F3» o «E7»)."),
                            "a_uid", O(
                                "type", "string",
                                "description", "identificativo della seconda.")),
                        "required", A("da_uid", "a_uid")),
                },
                new OperazioneDisegno
                {
                    Nome = "misura_fra_entita",
                    Famiglia = "verifiche",
                    Provata = true,
                    Sommario =
                        "Lo strumento di misura: area, perimetro, distanza, angolo, "
                        + "parallelismo fra una o due entita'.",
                    Descrizione =
                        "Misura UNA faccia o uno spigolo -- area, perimetro, lunghezza, "
                        + "raggio -- oppure DUE, e allora dice anche distanza, differenze sui "
                        + "tre assi, angolo, se sono parallele o perpendicolari.\n\nE' lo "
                        + "stesso strumento di misura che si usa a mano in SOLIDWORKS. Per la "
                        + "sola distanza minima fra due cose, «distanza_minima» e' piu' "
                        + "robusta: controlla il risultato coi punti.\n\nQuello che non si "
                        + "applica non torna: il perimetro di una faccia che non ne ha, il "
                        + "raggio di un piano. SOLIDWORKS li darebbe come -1, che e' un "
                        + "numero e non lo e'.",
                    Precondizione = "corpo_solido_e_faccia",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "uid", O(
                                "type", "string",
                                "description", "la faccia o lo spigolo da misurare, preso dallo stato del "
                                    + "documento."),
                            "uid2", O(
                                "type", "string",
                                "description", "la seconda, se vuoi misurare FRA due cose.")),
                        "required", A("uid")),
                },
                new OperazioneDisegno
                {
                    Nome = "esporta_stl",
                    Famiglia = "esportazioni",
                    Provata = true,
                    Sommario =
                        "Scrive lo STL per la stampa 3D, in millimetri. Verifica il pezzo "
                        + "prima, e rifiuta se non e' un solido chiuso.",
                    Descrizione =
                        "Scrive il pezzo come STL, il formato che gli slicer delle stampanti "
                        + "3D leggono.\n\nPRIMA DI SCRIVERE chiama «verifica_geometria_corpo» e "
                        + "SI RIFIUTA se il pezzo non e' un solo corpo solido chiuso senza "
                        + "difetti. Non e' prudenza: un STL non porta con se' niente che dica "
                        + "che il pezzo era a posto -- si apre, si affetta e si stampa lo "
                        + "stesso, e il buco si vede quando il pezzo e' in mano. Se sai quello "
                        + "che fai, «esporta_comunque»: true lo scrive lo stesso e te lo dice "
                        + "nella risposta.\n\nLE UNITA' SONO SEMPRE MILLIMETRI, imposte a mano "
                        + "prima di scrivere e rimesse com'erano dopo. Il file non le porta "
                        + "con se' e lo slicer assume i millimetri: esportato in pollici, il "
                        + "pezzo esce 25,4 volte piu' piccolo e nessun errore lo dice.\n\nDopo "
                        + "aver scritto RILEGGE IL FILE: quanti triangoli dice di avere e se "
                        + "la sua dimensione torna con quel numero. Un STL che non torna e' un "
                        + "file che si apre e non e' il pezzo.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "percorso", O(
                                "type", "string",
                                "description", "dove scrivere il file, estensione .stl compresa. Se non lo dici "
                                    + "si usa il percorso del pezzo con l'estensione cambiata, e allora il "
                                    + "pezzo dev'essere gia' stato salvato."),
                            "qualita", O(
                                "type", "string",
                                "enum", A("fine", "grossolana"),
                                "description", "«fine» (predefinito) fa piu' triangoli e segue meglio le "
                                    + "curve; «grossolana» fa un file piu' piccolo e sfaccetta i tondi."),
                            "binario", O(
                                "type", "boolean",
                                "description", "vero (predefinito): file binario, molto piu' piccolo. Falso: "
                                    + "ASCII, leggibile ma dieci volte piu' grosso."),
                            "esporta_comunque", O(
                                "type", "boolean",
                                "description", "scrive il file anche se la verifica dice di no. Predefinito: "
                                    + "falso. Usalo solo sapendo che cosa non andava.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "esporta_step",
                    Famiglia = "esportazioni",
                    Provata = true,
                    Sommario =
                        "Scrive lo STEP per chi deve lavorare il pezzo, e rilegge dal file "
                        + "quale protocollo e' uscito davvero.",
                    Descrizione =
                        "Scrive il pezzo come STEP: e' il formato con cui si manda un pezzo a "
                        + "un fornitore, a un cliente, o lo si porta in un altro CAD.\n\nA "
                        + "differenza dello STL NON SI RIFIUTA se il pezzo ha piu' corpi o "
                        + "delle superfici: uno STEP li porta, e un saldato si manda al "
                        + "fornitore proprio cosi'. La verifica gira lo stesso e il suo "
                        + "giudizio torna nella risposta, cosi' sai com'era il pezzo che hai "
                        + "mandato.\n\nDOPO AVER SCRITTO RILEGGE IL FILE e dice quale "
                        + "protocollo e' uscito DAVVERO, non quale hai chiesto: il valore che "
                        + "governa la cosa e' un numero senza nome nell'API, e l'unica prova "
                        + "e' il FILE_SCHEMA scritto dentro il file. Se non combacia, "
                        + "fallisce -- certi fornitori accettano solo l'AP203.\n\nCon "
                        + "«riapri_e_confronta» il file viene riaperto in SOLIDWORKS e il suo "
                        + "volume confrontato col pezzo: e' l'unica misura che dice che la "
                        + "GEOMETRIA e' sopravvissuta alla traduzione. Costa un'apertura di "
                        + "documento, quindi non e' il predefinito.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "percorso", O(
                                "type", "string",
                                "description", "dove scrivere il file, estensione .step o .stp compresa. Se non "
                                    + "lo dici si usa il percorso del pezzo con l'estensione cambiata."),
                            "protocollo", O(
                                "type", "string",
                                "enum", A("AP214", "AP203", "AP242"),
                                "description", "AP214 (predefinito) porta anche i colori; AP203 e' il piu' "
                                    + "vecchio e il piu' accettato; AP242 e' il piu' nuovo e non tutti lo "
                                    + "leggono."),
                            "riapri_e_confronta", O(
                                "type", "boolean",
                                "description", "riapre il file appena scritto e confronta il volume col pezzo, "
                                    + "fallendo oltre un millesimo di scarto. Predefinito: falso, perche' "
                                    + "costa un'apertura di documento.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "esporta_iges",
                    Famiglia = "esportazioni",
                    Provata = true,
                    Sommario =
                        "Scrive l'IGES, per i CAD e le macchine che lo STEP non lo leggono.",
                    Descrizione =
                        "Scrive il pezzo come IGES. Serve quando dall'altra parte c'e' un CAD "
                        + "o una macchina che lo STEP non lo legge: e' un formato piu' vecchio "
                        + "e meno preciso, e fra i due lo STEP e' sempre la prima scelta."
                        + "\n\nCome lo STEP, non si rifiuta per piu' corpi o superfici, e la "
                        + "verifica del pezzo torna nella risposta.\n\nDopo aver scritto "
                        + "conta i record di parametri del file: un IGES con le sole "
                        + "intestazioni e niente dentro esiste, pesa e si apre.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "percorso", O(
                                "type", "string",
                                "description", "dove scrivere il file, estensione .iges o .igs compresa. Se non "
                                    + "lo dici si usa il percorso del pezzo con l'estensione cambiata."),
                            "rappresentazione", O(
                                "type", "string",
                                "enum", A("solidi", "superfici"),
                                "description", "«solidi» (predefinito) scrive i corpi come BREP, ed e' quello "
                                    + "che serve a chi deve lavorare il pezzo; «superfici» scrive facce "
                                    + "tagliate."),
                            "riapri_e_confronta", O(
                                "type", "boolean",
                                "description", "riapre il file appena scritto e confronta il volume col pezzo. "
                                    + "Predefinito: falso.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "sopprimi_istanza",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Toglie una o piu' copie da una ripetizione gia' fatta, e si "
                        + "rifiuta se la copia non c'era.",
                    Descrizione =
                        "Una griglia di fori che ne salta uno dove passa una nervatura, una "
                        + "fila di asole che si ferma prima della piega. La ripetizione si fa "
                        + "prima, intera, con «ripeti_lineare» o «ripeti_circolare»; poi si "
                        + "toglie quello che non deve esserci.\n\nLE COPIE SI CONTANO DA 1, E "
                        + "L'ORIGINALE NON E' UNA COPIA. In una fila di quattro, la 1 e' la "
                        + "prima dopo l'originale e la 3 e' l'ultima.\n\nMI RIFIUTO SE LA "
                        + "COPIA NON C'ERA, ed e' la ragione per cui questa operazione "
                        + "controlla invece di limitarsi a scrivere. Una ripetizione "
                        + "costruisce solo le copie che stanno nel materiale e risponde "
                        + "«riuscito» anche quando ne ha fatte meno di quelle chieste: "
                        + "chiedere di togliere la quarta di quattro, quando la quarta e' "
                        + "caduta fuori dal pezzo, RIESCE e non cambia niente. Io conto le "
                        + "facce prima e dopo, e se non sono calate ti dico che quella copia "
                        + "non c'era e rimetto tutto com'era.\n\nSi puo' rimettere una copia "
                        + "tolta con «riattiva».\n\nNON TUTTE LE RIPETIZIONI SANNO FARLO: "
                        + "quelle guidate da uno schizzo, da una catena o da una quota non "
                        + "hanno le copie numerate. Uno specchio ha una copia sola: si toglie "
                        + "cancellandolo.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "la RIPETIZIONE da cui togliere le copie (per esempio «L3»), non "
                                    + "la lavorazione ripetuta. Gli identificativi stanno nello stato "
                                    + "del documento, dentro «feature»."),
                            "copie", O(
                                "type", "array",
                                "items", O("type", "integer"),
                                "description", "i numeri delle copie da togliere, contando da 1 dopo "
                                    + "l'originale. Per esempio [2] toglie la seconda copia."),
                            "riattiva", O(
                                "type", "boolean",
                                "description", "vero per RIMETTERE copie tolte prima, invece di toglierne. "
                                    + "Predefinito falso.")),
                        "required", A("feature_uid", "copie")),
                },
                new OperazioneDisegno
                {
                    Nome = "incidi_testo",
                    Famiglia = "lavorazioni",
                    Provata = true,
                    Sommario =
                        "Incide (o fa in rilievo) una scritta su una faccia piana, e si "
                        + "rifiuta se la scritta non ci sta.",
                    Descrizione =
                        "Scrive delle PAROLE nel pezzo: numero di serie, verso di montaggio, "
                        + "nome del cliente, «ALTO». Incisa (predefinito) o in rilievo."
                        + "\n\nCONTROLLO PRIMA CHE CI STIA, ED E' LA META' DEL LAVORO. La "
                        + "larghezza di una scritta non si calcola dalla stringa: dipende dal "
                        + "font, e «PROVA» alta 10 mm in Century Gothic e' larga 47 mm. Una "
                        + "scritta che esce dalla faccia viene tagliata via IN SILENZIO -- la "
                        + "lavorazione nasce verde, e nel pezzo c'e' «PRO». Io leggo dove "
                        + "finiscono davvero le lettere e mi rifiuto, dicendoti di quanto "
                        + "sbordava.\n\nTI RISPONDO QUANTO E' LARGA E ALTA DAVVERO, cosi' la "
                        + "seconda volta sai gia' che numeri passare.\n\nL'ancora e' "
                        + "l'inizio della scritta in basso a sinistra: da li' va a destra. "
                        + "L'area delle lettere che ti rispondo va col quadrato "
                        + "dell'altezza -- raddoppiando «altezza_mm» l'area quadruplica -- ed "
                        + "e' il modo di controllare due marcature l'una con l'altra."
                        + "\n\nNON SU UNA FACCIA CURVA: il testo avvolto e' un'altra "
                        + "lavorazione, che non ho.",
                    Precondizione = "corpo_solido_e_faccia",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "testo", O(
                                "type", "string",
                                "description", "la scritta, per esempio «SN-0042» o «ALTO»."),
                            "faccia_uid", O(
                                "type", "string",
                                "description", "la faccia piana su cui scrivere (per esempio «F1»). Gli "
                                    + "identificativi stanno nello stato del documento."),
                            "x_mm", O(
                                "type", "number",
                                "description", "dove comincia la scritta sulla faccia, in mm, nelle "
                                    + "coordinate della faccia. Predefinito 0."),
                            "y_mm", O(
                                "type", "number",
                                "description", "l'altra coordinata dell'ancora. Predefinito 0. L'ancora e' "
                                    + "l'ANGOLO IN BASSO A SINISTRA della scritta, non il suo centro."),
                            "altezza_mm", O(
                                "type", "number",
                                "description", "l'altezza delle lettere in mm. Predefinito 5."),
                            "profondita_mm", O(
                                "type", "number",
                                "description", "quanto in profondita' incidere, o quanto alzare se «in_rilievo». "
                                    + "Predefinito 0,5 mm, che e' una marcatura da leggere. Per una "
                                    + "che si senta al tatto servono 0,3-0,8 mm."),
                            "in_rilievo", O(
                                "type", "boolean",
                                "description", "vero per una scritta in rilievo invece che incisa. Predefinito "
                                    + "falso. In rilievo si stampa meglio in 3D, incisa si legge "
                                    + "meglio su un pezzo lavorato."),
                            "font", O(
                                "type", "string",
                                "description", "il nome del carattere, per esempio «Arial». Se non lo dici uso "
                                    + "quello del documento. Un font che non c'e' su questo computer "
                                    + "cambia la larghezza della scritta."),
                            "grassetto", O(
                                "type", "boolean",
                                "description", "vero per il grassetto: lettere piu' spesse, che su una stampa "
                                    + "3D o una fusione si leggono meglio. Predefinito falso.")),
                        "required", A("testo", "faccia_uid")),
                },
                new OperazioneDisegno
                {
                    Nome = "scrivi_proprieta",
                    Famiglia = "identita",
                    Provata = true,
                    Sommario =
                        "Scrive una proprieta' personalizzata -- numero disegno, revisione, "
                        + "cliente -- sul documento oppure su una configurazione.",
                    Descrizione =
                        "Scrive una proprieta' personalizzata: e' cio' che finisce nel "
                        + "cartiglio della tavola e nella scheda del PDM. «Numero disegno», "
                        + "«Revisione», «Cliente», «Descrizione».\n\nIL DOCUMENTO E UNA "
                        + "CONFIGURAZIONE SONO DUE CASSETTI SEPARATI. Una proprieta' scritta "
                        + "sul documento da una configurazione NON SI VEDE, e viceversa: non "
                        + "si ereditano. Se dici «configurazione» la scrivo li' e solo li'; "
                        + "se non la dici la scrivo sul documento e solo sul documento. Te lo "
                        + "ripeto nella risposta ogni volta, perche' e' l'errore che non da' "
                        + "errore: si scrive il numero di disegno sul documento, lo si "
                        + "rilegge dalla configurazione, non c'e', e nessuno dice perche'."
                        + "\n\nSE C'E' GIA', LA SOSTITUISCO. Non ti lascio credere di aver "
                        + "cambiato una revisione che invece e' rimasta quella di prima."
                        + "\n\nDOPO AVER SCRITTO RILEGGO. Il comando risponde un codice, non "
                        + "il valore: se rileggendo non trovo quello che ho scritto fallisco "
                        + "invece di confermarti una scheda dati che non e' quella che credi.",
                    Precondizione = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "nome", O(
                                "type", "string",
                                "description", "il nome della proprieta', per esempio «Numero disegno» o "
                                    + "«Revisione». Scrivilo come lo vuoi vedere nel cartiglio: il nome "
                                    + "e' quello che vedra' chi legge la scheda."),
                            "valore", O(
                                "type", "string",
                                "description", "il valore da scrivere. Stringa vuota per svuotarla."),
                            "configurazione", O(
                                "type", "string",
                                "description", "il nome della configurazione su cui scriverla. Se non lo dici "
                                    + "la scrivo sul DOCUMENTO, ed e' un posto diverso. I nomi stanno "
                                    + "nello stato del documento, dentro «configurazioni».")),
                        "required", A("nome", "valore")),
                },
                new OperazioneDisegno
                {
                    Nome = "crea_configurazione",
                    Famiglia = "identita",
                    Provata = true,
                    Sommario =
                        "Fa una variante del pezzo -- sinistra e destra, con e senza foro -- "
                        + "e la attiva subito.",
                    Descrizione =
                        "Crea una configurazione: la stessa staffa sinistra e destra, con e "
                        + "senza foro, dentro un file solo. E' come un'officina a disegno "
                        + "chiede una famiglia di pezzi.\n\nLA CREA E LA ATTIVA. SOLIDWORKS "
                        + "sposta subito il documento sulla configurazione nuova, senza che "
                        + "glielo si chieda: tutto quello che disegni dopo, e tutto quello "
                        + "che lo stato ti racconta dopo, riguarda QUELLA. Te lo dico nella "
                        + "risposta. Per tornare a un'altra c'e' «attiva_configurazione»."
                        + "\n\nIL NOME CHE TI RISPONDO E' QUELLO DA USARE, e non e' detto che "
                        + "sia quello che mi hai chiesto: i nomi di SOLIDWORKS possono avere "
                        + "delle parentesi angolari dentro, e quelle fanno parte del nome. "
                        + "Riportalo com'e'.",
                    Precondizione = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "nome", O(
                                "type", "string",
                                "description", "il nome della configurazione, per esempio «Sinistra» o "
                                    + "«Senza foro». Se ce n'e' gia' una cosi' mi rifiuto."),
                            "descrizione", O(
                                "type", "string",
                                "description", "la descrizione che comparira' nell'elenco delle "
                                    + "configurazioni."),
                            "commento", O(
                                "type", "string",
                                "description", "un commento libero.")),
                        "required", A("nome")),
                },
                new OperazioneDisegno
                {
                    Nome = "attiva_configurazione",
                    Famiglia = "identita",
                    Provata = true,
                    Sommario =
                        "Passa a una configurazione gia' esistente, e verifica che ci sia "
                        + "davvero passato.",
                    Descrizione =
                        "Sposta il documento su un'altra configurazione. Da qui in poi quello "
                        + "che disegni e quello che lo stato ti racconta riguardano quella."
                        + "\n\nIL NOME NON E' QUELLO CHE SEMBRA. La configurazione predefinita "
                        + "si chiama spesso «Default<A macchina>» e NON «Default»: quello fra "
                        + "parentesi angolari fa parte del nome. Chiedendo «Default» "
                        + "SOLIDWORKS risponde di no, in silenzio -- e spesso di "
                        + "configurazioni che cominciano per «Default» ce n'e' piu' d'una (la "
                        + "coppia dei saldati: «<A macchina>» e «<Saldato>»), per cui non c'e' "
                        + "nemmeno un modo giusto di indovinare. NON INDOVINO: copia il nome "
                        + "intero da «configurazioni» nello stato del documento.\n\nDOPO AVER "
                        + "CHIESTO CONTROLLO QUALE E' ATTIVA "
                        + "ADESSO: non mi basta che il comando abbia detto di si', perche' "
                        + "lavorare per mezz'ora sulla variante sbagliata non da' nessun "
                        + "errore.",
                    Precondizione = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "nome", O(
                                "type", "string",
                                "description", "il nome INTERO della configurazione da attivare, copiato da "
                                    + "«configurazioni» nello stato del documento. Suffisso fra "
                                    + "parentesi angolari compreso, se c'e'.")),
                        "required", A("nome")),
                },
            };
    }
}
