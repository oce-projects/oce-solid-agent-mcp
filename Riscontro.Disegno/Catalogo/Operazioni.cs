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
                // LA FAMIGLIA CHE NON E' UN PEZZO. Tutte le altre lavorano
                // dentro un documento di parte; questa mette insieme parti che
                // esistono gia', e la cosa che la distingue non e' cosa fa ma
                // cosa NON basta: inserire non e' accoppiare, e un assieme di
                // componenti liberi sembra montato senza esserlo. La riga qui
                // sotto lo dice al modello prima che chieda l'elenco.
                new KeyValuePair<string, string>(
                    "assiemi",
                    "mettere insieme pezzi che esistono gia'. Inserire NON e' " +
                    "accoppiare: guarda sempre «appoggiato»"),
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
                // LA LAMIERA (voce GP18). La famiglia che produce il file che va
                // alla macchina di taglio: un difetto silenzioso qui costa
                // lamiera vera, e lo sviluppo si confronta sempre col conto a mano.
                new KeyValuePair<string, string>(
                    "lamiera",
                    "lamiera piegata e il suo sviluppo in DXF per il taglio"),
                // LE QUOTE NEL MODELLO (voce Z-H3): DimXpert dal nucleo. Non e'
                // la tavola: le quote stanno sul pezzo.
                new KeyValuePair<string, string>(
                    "quote",
                    "quote e tolleranze messe sul PEZZO (DimXpert), non su una tavola"),
            };

        public static readonly List<OperazioneDisegno> Tutte =
            new List<OperazioneDisegno>
            {
                new OperazioneDisegno
                {
                    Nome = "estrudi_cerchio",
                    Famiglia = "estrusioni",
                    Esempio = "{\"piano\":\"frontale\",\"diametro_mm\":6,\"altezza_mm\":10}",   // da `filetto_su_un_perno`
                    DocumentoRichiesto = "parte",
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
                    DocumentoRichiesto = "parte",
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
                    Esempio = "{\"piano\":\"frontale\",\"lunghezza_mm\":200,\"larghezza_mm\":120,\"spessore_mm\":20,\"centrato\":true}",   // da `asola_cieca`
                    DocumentoRichiesto = "parte",
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
                    DocumentoRichiesto = "parte",
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
                        + "libreria con «membro_strutturale».\n\nL'ASOLA E' UN POLIGONO: le "
                        + "due estremita' sono 24 lati ciascuna, coi vertici sul cerchio "
                        + "circoscritto, cosi' che l'asola non sia mai piu' stretta del "
                        + "nominale. Esce larga lo 0,2% in piu' (20,043 per 20) e con lo 0,2% "
                        + "di area in piu'. Tondo e tubo sono cerchi veri.",
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
                    Esempio = "{\"piano\":\"frontale\",\"angolo_gradi\":360,\"asse\":[[0,0],[90,0]],\"contorni\":[[[0,0],[0,15],[40,15],[40,10],[70,10],[70,17],[90,17],[90,0]]]}",   // da `albero_forato_non_dichiara_piu_il_profilo`
                    DocumentoRichiesto = "parte",
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
                        + "secondo contorno."
                        + "\n\nMI RIFIUTO SE L'ASSE NON STA SUL PIANO DEL PROFILO, e se il profilo "
                        + "ATTRAVERSA l'asse. Un profilo a cavallo dell'asse genera un solido che "
                        + "si compenetra con se stesso: SOLIDWORKS a volte lo fa lo stesso, e il "
                        + "pezzo che ne esce non e' quello che volevi.",
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
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Una ruota dentata a evolvente, con foro e cava per linguetta.",
                    Descrizione =
                        "Crea una RUOTA DENTATA cilindrica a denti dritti, dal numero di "
                        + "denti e dal modulo. Il profilo del dente e' a evolvente e lo "
                        + "calcolo io: tu dai solo i dati di progetto.\n\nCon foro_mm "
                        + "si ottiene anche il foro centrale.\n\nIL LIMITE: sotto 17 denti "
                        + "(con l'angolo di pressione standard di 20 gradi) rifiuto, perche' "
                        + "servirebbe la correzione di profilo contro l'intaglio del piede e "
                        + "non la so fare. E' un limite mio, non della meccanica: con un "
                        + "angolo di pressione maggiore il minimo scende (25 gradi: 11 denti).",
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
                    DocumentoRichiesto = "parte",
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
                        + "vivi, e la vite ci balla dentro invece di scorrere."
                        + "\n\nMI RIFIUTO IN QUATTRO CASI, e li dico prima perche' provarli costa un giro: "
                        + "se l'asola non e' passante e non mi dai la profondita'; se cade tutta "
                        + "fuori dal materiale (ti dico fra quali coordinate sta la faccia); se e' "
                        + "piu' grande della faccia e la coprirebbe tutta; e se lunghezza e "
                        + "larghezza sono uguali -- quella e' una circonferenza, e la lunghezza qui "
                        + "si misura da fuori a fuori.",
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
                            "ancora", SchemaAncora("l'asola"),
                            "lunghezza_mm", O(
                                "type", "number",
                                "description", "lunghezza TOTALE da fuori a fuori, non la corsa. Dev'essere "
                                    + "maggiore della larghezza."),
                            "larghezza_mm", O(
                                "type", "number",
                                "description", "larghezza dell'asola, cioe' il diametro delle estremita' tonde"),
                            "angolo_gradi", O(
                                "type", "number",
                                "description", "rotazione nel piano della faccia, predefinito 0. Lo schizzo "
                                    + "nasce quotato (raggio e distanza fra i centri): a 0 e 90 gradi anche "
                                    + "l'orientamento e' fissato; obliqua, l'angolo si quota contro il primo "
                                    + "spigolo dell'«ancora», e senza ancora resta libero (te lo dico)."),
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
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Fa CRESCERE materiale da una faccia: un rialzo, un labbro, un "
                        + "distanziale, un'aletta.",
                    Descrizione =
                        "Disegna un contorno su una faccia che c'e' gia' e lo estrude in "
                        + "FUORI, aggiungendo materiale al pezzo.\n\nE' L'OPERAZIONE CHE FA "
                        + "CRESCERE UN RIALZO. Le altre su un pezzo esistente tolgono (fori, "
                        + "tagli, asole), svuotano (guscio) o spostano; aggiungono materiale "
                        + "anche «incidi_testo» in rilievo, ma solo una scritta, e "
                        + "«combina_corpi», che unisce corpi che ci sono gia'. Senza questa non si "
                        + "possono fare: il labbro di tenuta di un coperchio, i distanziali "
                        + "su cui appoggia qualcosa, un'aletta di fissaggio, una torretta "
                        + "per una vite, un rialzo attorno a un foro.\n\nI CONTORNI SONO NELLE "
                        + "COORDINATE DELLA FACCIA, come per «taglio_rettangolare»: (0, 0) "
                        + "e' il centro della faccia. Piu' contorni annidati fanno una "
                        + "cornice — quello fuori e quello dentro — ed e' cosi' che si fa un "
                        + "labbro attorno a un bordo invece di un blocco pieno.\n\n«unisci» a "
                        + "vero (predefinito) fonde il rialzo con il pezzo. A falso nasce un "
                        + "CORPO SEPARATO: quasi mai quello che si vuole, e l'albero cresce "
                        + "uguale, quindi lo si nota solo contando i corpi."
                        + "\n\nMI RIFIUTO se il rialzo non aggiunge materiale (e' cresciuto DENTRO "
                        + "il pezzo), se con «unisci» i corpi non restano quelli, e se chiedi "
                        + "l'«ancora» su piu' di un contorno o su un contorno che non e' ne' tondo "
                        + "ne' un rettangolo allineato: il punto che l'ancora tiene e' il centro, "
                        + "e il centro di un poligono qualunque non lo indovino.",
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
                            "ancora", SchemaAncora("il rialzo", true),
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
                    DocumentoRichiesto = "parte",
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
                        + "come una lavorazione sola."
                        + "\n\nOgni posizione puo' portare un'«ancora» (una quota da uno spigolo, o "
                        + "il centro fra due spigoli, per asse): allora il foro segue il bordo "
                        + "quando il pezzo cambia. MI RIFIUTO se l'ancora ce l'hanno solo alcune "
                        + "posizioni (tutte o nessuna: i fori stanno in uno schizzo solo) e se una "
                        + "distanza e' meno del raggio del foro — quello lo so solo a foro fatto, "
                        + "e allora il foro lo tolgo.",
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
                                    + "stanno in una chiamata sola. L'«ancora» si da' a TUTTE le posizioni o a "
                                    + "nessuna: i fori stanno in uno schizzo solo, e un punto senza ancora lo "
                                    + "lascerebbe libero di muoversi. Mi rifiuto se una distanza e' meno del "
                                    + "raggio del foro (lo decide la tabella): il foro l'ho gia' fatto, e lo tolgo.",
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
                                            "description", "vedi x_mm"),
                                        "ancora", SchemaAncora("il foro")),
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
                    DocumentoRichiesto = "parte",
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
                    Nome = "cambia_estrusione",
                    Famiglia = "lavorazioni",
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Esempio = "{\"feature_uid\":\"Taglio-Estrusione1\",\"passante\":true}",   // da `il_cieco_diventa_passante`
                    Sommario = "Cambia un'estrusione o un taglio gia' fatti: cieco -> passante, o un'altra profondita'.",
                    Descrizione =
                        "Cambia la definizione di un'estrusione o di un taglio estruso che c'e' gia': un "
                        + "foro cieco che deve diventare passante («passante»: true), o una profondita' "
                        + "diversa («profondita_mm»). Non si rifa' il pezzo: la lavorazione cambia al suo "
                        + "posto nell'albero, e quelle a valle la seguono.\n\nDopo rileggo la definizione "
                        + "(condizione e profondita'), il volume prima e dopo e le lavorazioni in errore: se "
                        + "la modifica ne rompe una a valle, rimetto com'era e mi rifiuto. Se il volume non "
                        + "cambia te lo dico (un cieco piu' profondo del materiale e' gia' passante).\n\nMI "
                        + "RIFIUTO se la lavorazione non e' un'estrusione o un taglio estruso (per una quota "
                        + "qualunque c'e' «modifica_quota»), se chiedi «passante» per un'estrusione, e se non "
                        + "mi dai uno solo fra «passante» e «profondita_mm».",
                    Precondizione = "un pezzo con un'estrusione o un taglio estruso",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "la lavorazione da cambiare, col nome dallo stato («feature»)."),
                            "passante", O(
                                "type", "boolean",
                                "description", "true: il taglio attraversa tutto. Oppure «profondita_mm»."),
                            "profondita_mm", O(
                                "type", "number",
                                "description", "la profondita' nuova, in mm (cieca). Oppure «passante».")),
                        "required", A("feature_uid")),
                },
                new OperazioneDisegno
                {
                    Nome = "riordina_lavorazione",
                    Famiglia = "lavorazioni",
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Esempio = "{\"feature_uid\":\"Svuota1\",\"rispetto_a_uid\":\"Taglio-Estrusione1\",\"dove\":\"prima\"}",   // da `l_ordine_cambia_il_pezzo`
                    Sommario = "Sposta una lavorazione prima o dopo un'altra nell'albero, e dice come e' cambiato il pezzo.",
                    Descrizione =
                        "Sposta una lavorazione nell'albero, prima o dopo un'altra: uno svuotamento "
                        + "messo prima di un foro da' un pezzo diverso da uno messo dopo (misurato: "
                        + "56485,7 contro 51952,38 mm3 sulla stessa scatola). Ti do l'ordine prima e "
                        + "dopo, il volume e i corpi prima e dopo; se il pezzo non cambia te lo dico."
                        + "\n\nSE LO SPOSTAMENTO ROMPE UNA LAVORAZIONE A VALLE la rimetto dov'era e "
                        + "mi rifiuto.\n\nMI RIFIUTO se una delle due lavorazioni non c'e' o non e' "
                        + "piu' quella dello stato (il nome si rinomina, l'ID no), se «dove» non e' "
                        + "«prima» o «dopo», e se SOLIDWORKS non la sposta: succede quando dipende da "
                        + "una lavorazione che verrebbe dopo (un foro prima dell'estrusione su cui "
                        + "sta).",
                    Precondizione = "un pezzo con almeno due lavorazioni",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "la lavorazione da spostare, col nome dallo stato («feature»)."),
                            "rispetto_a_uid", O(
                                "type", "string",
                                "description", "la lavorazione accanto a cui metterla."),
                            "dove", O(
                                "type", "string",
                                "enum", A("prima", "dopo"),
                                "description", "prima o dopo di «rispetto_a_uid».")),
                        "required", A("feature_uid", "rispetto_a_uid", "dove")),
                },
                new OperazioneDisegno
                {
                    Nome = "variabile_globale",
                    Famiglia = "lavorazioni",
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Esempio = "{\"nome\":\"parete\",\"valore_mm\":12}",   // da `la_parete_ha_un_nome`
                    Sommario = "Dai un NOME a un numero (parete = 3 mm): le quote legate cambiano con lui.",
                    Descrizione =
                        "Crea o cambia una variabile globale del pezzo: «parete» = 12 mm, oppure "
                        + "un'espressione («2 * \"parete\" + 1mm»). Le quote legate con "
                        + "«lega_quota» cambiano quando la cambi: e' il modo di fare un pezzo che "
                        + "si modifica senza rompersi, senza ripetere lo stesso numero in dieci "
                        + "posti.\n\nDopo averla scritta la rileggo, e ti dico quali quote guida e "
                        + "il volume prima e dopo.\n\nMI RIFIUTO se il nome non e' fatto di lettere, "
                        + "cifre e «_», se mi dai insieme (o nessuno dei due) «valore_mm» ed "
                        + "«espressione», e se SOLIDWORKS non accetta il nome: nel SOLIDWORKS in "
                        + "italiano «spessore» e' gia' preso (misurato) — usa per esempio «parete».",
                    Precondizione = "un pezzo aperto",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "nome", O(
                                "type", "string",
                                "description", "il nome della variabile: lettere, cifre e «_», comincia con una lettera."),
                            "valore_mm", O(
                                "type", "number",
                                "description", "il valore, in millimetri. Oppure «espressione»."),
                            "espressione", O(
                                "type", "string",
                                "description", "un'espressione di SOLIDWORKS, coi nomi fra virgolette doppie: "
                                    + "«2 * \"parete\" + 1mm». Oppure «valore_mm».")),
                        "required", A("nome")),
                },
                new OperazioneDisegno
                {
                    Nome = "lega_quota",
                    Famiglia = "lavorazioni",
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario = "Lega una quota a una variabile o a un conto: la guida l'equazione.",
                    Descrizione =
                        "Lega una quota del pezzo (il suo nome dallo stato, «D1@Schizzo1») a "
                        + "un'espressione: una variabile globale («\"parete\"») o un conto "
                        + "(«2 * \"parete\" + 1mm»). Da li' la quota la guida l'equazione, e "
                        + "cambia quando cambia la variabile; «modifica_quota» non la cambia piu'."
                        + "\n\nDopo il legame rileggo la quota: deve valere quanto dice "
                        + "l'equazione, e ti do il volume prima e dopo.\n\nMI RIFIUTO se la quota "
                        + "non c'e' (ti dico quali ci sono) o se SOLIDWORKS non accetta "
                        + "l'espressione (una variabile che non esiste: creala prima con "
                        + "«variabile_globale»).",
                    Precondizione = "un pezzo aperto con almeno una quota",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "quota", O(
                                "type", "string",
                                "description", "il nome della quota, copiato dallo stato del documento."),
                            "espressione", O(
                                "type", "string",
                                "description", "a cosa legarla, coi nomi fra virgolette doppie: «\"parete\"», "
                                    + "«2 * \"parete\" + 1mm».")),
                        "required", A("quota", "espressione")),
                },
                new OperazioneDisegno
                {
                    Nome = "modifica_quota",
                    Famiglia = "lavorazioni",
                    DocumentoRichiesto = "parte",
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
                        + "avevi in mente.\n\nRIFIUTA se la quota nuova fa uscire un'apertura "
                        + "dal materiale — un foro o un'asola che stava dentro il pezzo e ne "
                        + "aprirebbe il bordo, o due aperture che si fonderebbero in una: "
                        + "rimette il valore di prima, e lo dice. Succede piu' spesso di quanto "
                        + "sembri, perche' una piastra disegnata centrata cresce e cala DA UNA "
                        + "PARTE SOLA: accorciarla di 50 sposta un fianco di 50, non due di 25."
                        + "\n\nRIFIUTA anche se con la quota nuova uno smusso o un raccordo non "
                        + "ci sta piu' — per esempio lo spessore sotto il lato dello smusso: "
                        + "SOLIDWORKS non da' errore, cambia la forma da solo, e la lavorazione "
                        + "di base perde una faccia intera. Rimette il valore di prima e dice "
                        + "quale smusso o raccordo.",
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
                    Nome = "tolleranza_quota",
                    Famiglia = "lavorazioni",
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "La tolleranza su una quota del modello (±, bilaterale, accoppiamento ISO), riscontrata e con i decimali giusti.",
                    Descrizione =
                        "Mette la TOLLERANZA su una quota del modello: simmetrica (±valore), bilaterale "
                        + "(scostamenti superiore e inferiore) o un accoppiamento ISO (classe, es. H7 per un "
                        + "foro, g6 per un albero). Sulla quota del modello arriva in tavola da sola con "
                        + "«quote_dal_modello».\n\nL'ACCOPPIAMENTO SI RISCONTRA: gli scostamenti li calcola "
                        + "SOLIDWORKS, e io controllo che l'ampiezza sia il grado IT della tabella ISO 286-1 per "
                        + "quel diametro (IT5..IT11, fino a 500 mm) e che il segno sia quello della lettera "
                        + "(H da zero in su, h da zero in giu'). Se non torna la rimetto com'era e te lo dico."
                        + "\n\nI DECIMALI: imposto quelli della tolleranza sulla quota. Misurato: senza, un H7 "
                        + "su 10 usciva in tavola «10 H7 +0 / 0» — in officina vuol dire «esatta».\n\nLa "
                        + "risposta porta scostamenti e limiti (10,000 / 10,015 per un Ø10 H7).\n\nMI RIFIUTO "
                        + "per un grado IT fuori da 5..11 o una quota oltre 500 mm (non li so riscontrare), per "
                        + "una bilaterale con l'inferiore sopra il superiore, e quando il riscontro non torna.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "quota", O(
                                "type", "string",
                                "description", "il nome della quota, copiato dallo stato del documento (es. «D1@Schizzo2»)"),
                            "tipo", O(
                                "type", "string",
                                "enum", A("simmetrica", "bilaterale", "accoppiamento", "nessuna"),
                                "description", "che tolleranza. «nessuna» la toglie."),
                            "valore_mm", O("type", "number", "description", "per «simmetrica»: il ± (0,05 = ±0,05)."),
                            "superiore_mm", O("type", "number", "description", "per «bilaterale»: lo scostamento superiore (es. 0,1)."),
                            "inferiore_mm", O("type", "number", "description", "per «bilaterale»: lo scostamento inferiore (es. -0,05)."),
                            "classe", O("type", "string", "description", "per «accoppiamento»: la classe ISO, maiuscola per un foro (H7), minuscola per un albero (g6).")),
                        "required", A("quota", "tipo")),
                },
                new OperazioneDisegno
                {
                    Nome = "sopprimi_lavorazione",
                    Famiglia = "lavorazioni",
                    Esempio = "{\"feature_uid\":\"Taglio-Estrusione2\"}",   // da `lim_sopprimere_due_volte`
                    DocumentoRichiesto = "parte",
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
                        + "per area a ogni lettura: rileggi il pezzo prima del passo seguente."
                        + "\n\nMI RIFIUTO SE LA LAVORAZIONE E' GIA' SOPPRESSA, e ti dico di usare "
                        + "l'operazione che la riattiva. Sopprimere due volte non fa niente e non "
                        + "da' errore: e' il modo piu' facile di credere di aver cambiato il pezzo "
                        + "senza averlo toccato.",
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
                    Esempio = "{\"feature_uid\":\"Taglio-Estrusione1\"}",   // da `elimina_il_primo_foro`
                    DocumentoRichiesto = "parte",
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
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Raccordo a raggio costante su uno o piu' spigoli.",
                    Descrizione =
                        "Arrotonda uno o piu' spigoli con lo stesso raggio. Gli spigoli si "
                        + "indicano con gli identificativi presi dallo stato del documento."
                        + "\n\nSe fra la lettura dello stato e adesso gli spigoli hanno cambiato "
                        + "numero (un foro fatto prima li rinumera), li ritrovo per firma: stessa "
                        + "lunghezza nello stesso punto, e lo dico nella risposta. MI RIFIUTO se "
                        + "lo spigolo che avevi visto non c'e' piu' com'era — accorciato, spezzato "
                        + "— invece di prendere il piu' vicino, e se il raggio non ci sta in meta' "
                        + "dello spigolo.",
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
                    DocumentoRichiesto = "parte",
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
                    DocumentoRichiesto = "parte",
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
                        + "«capovolgi_1»."
                        + "\n\nMI RIFIUTO SE LA DIREZIONE ARRIVA DA UNO SPIGOLO CURVO: serve uno "
                        + "spigolo rettilineo. Un arco non ha una direzione sola, e prenderne una "
                        + "a caso vorrebbe dire una ripetizione storta che sembra giusta.",
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
                    DocumentoRichiesto = "parte",
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
                        + "l'utente dice «smussa», non arrotondare."
                        + "\n\nMI RIFIUTO SE LO SMUSSO E' TROPPO GRANDE PER LO SPIGOLO -- non ci sta "
                        + "nemmeno meta'. Un valore oltre il limite non da' sempre errore: a volte "
                        + "esce una geometria degenere che passa il ricalcolo.",
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
                    DocumentoRichiesto = "parte",
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
                        + "simmetrico specchia il foro o la tasca che manca da un lato."
                        + "\n\nMI RIFIUTO SE IL PIANO NON ESISTE, e ti dico quali ci sono. I nomi "
                        + "inventati -- diagonale, obliquo -- non li interpreto: specchiare sul "
                        + "piano sbagliato produce un pezzo plausibile e sbagliato.",
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
                    DocumentoRichiesto = "parte",
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
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Un taglio tondo su una faccia: per un passaggio che NON e' un "
                        + "foro da viteria.",
                    Descrizione =
                        "Fa un foro su una faccia gia' esistente, alle coordinate indicate "
                        + "nel piano di quella faccia. Passante o cieco."
                        + "\n\nMI RIFIUTO SE IL FORO CADE TUTTO FUORI DAL MATERIALE (ti dico fra quali "
                        + "coordinate sta la faccia) e se e' cosi' largo da coprire tutta la "
                        + "faccia: quello non e' un foro, e' un pezzo che sparisce.",
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
                            "ancora", SchemaAncora("il foro"),
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
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Una tasca rettangolare su una faccia.",
                    Descrizione =
                        "Scava una tasca rettangolare su una faccia esistente."
                        + "\n\nMI RIFIUTO SE LA TASCA COPRE TUTTA LA FACCIA, e ti dico quanto misura. "
                        + "Una tasca grande quanto il pezzo non e' una tasca: e' il pezzo che non "
                        + "c'e' piu'.",
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
                            "ancora", SchemaAncora("la tasca"),
                            "lunghezza_mm", O(
                                "type", "number",
                                "description", "lungo x_mm"),
                            "larghezza_mm", O(
                                "type", "number",
                                "description", "lungo y_mm"),
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
                    Esempio = "{\"materiale\":\"AISI 304\"}",   // da `massa_di_un_cubo_di_acciaio`
                    DocumentoRichiesto = "parte",
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
                        + "caso.\n\nSE NON DICI IL MATERIALE uso «S235JR», che e' "
                        + "quello che questa officina lavora salvo avviso contrario, "
                        + "e te lo scrivo nella risposta: cosi' sai che l'ho scelto io "
                        + "e non tu.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "materiale", O(
                                "type", "string",
                                "default", "S235JR",
                                "description", "per esempio «S235JR», «AISI 304», «Lega 6061». "
                                    + "Se non lo dici uso S235JR, e te lo scrivo nella risposta."),
                            "archivio", O(
                                "type", "string",
                                "description", "opzionale: il nome dell'archivio, se sai in quale cercare. "
                                    + "Lascialo stare se non lo sai.")),
                        // Z-G3: `materiale` NON e' piu' obbligatorio — c'e' un
                        // predefinito d'officina, e la risposta dichiara di averlo
                        // usato. Un campo che si chiede sempre e la cui risposta e'
                        // sempre la stessa fa scrivere al modello una riga che non
                        // e' una scelta.
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "crea_asse",
                    Famiglia = "riferimenti",
                    DocumentoRichiesto = "parte",
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
                    Esempio = "{\"piano\":\"frontale\",\"distanza_mm\":-250}",   // da `piano_frontale_meno_250`
                    DocumentoRichiesto = "parte",
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
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Cordone di saldatura cosmetico fra due facce di due barre che si toccano ad angolo.",
                    Descrizione =
                        "Mette un cordone di saldatura d'angolo, cosmetico, fra DUE facce "
                        + "di due corpi che si toccano lungo uno spigolo e formano un angolo: "
                        + "per una barra in battuta su un'altra, la faccia LATERALE della "
                        + "barra e la faccia su cui arriva. Non toglie ne' aggiunge "
                        + "materiale: documenta la saldatura, e finisce nelle tavole e nella "
                        + "distinta.\n\nUNA COPPIA DI FACCE PER CHIAMATA. Un cordone che gira "
                        + "attorno a un tubolare sono piu' chiamate, una per lato: con piu' "
                        + "facce per parte SOLIDWORKS non crea niente. Le facce si indicano "
                        + "per identificativo, prese dallo stato del documento: e' "
                        + "un'operazione che si chiede DOPO aver fatto le barre e i tagli, "
                        + "guardandole.\n\nSe le due facce non si toccano, o sono parallele, "
                        + "rifiuto: SOLIDWORKS il cordone lo creerebbe lo stesso, e in "
                        + "distinta finirebbe una saldatura che non esiste. La lunghezza che "
                        + "ti torno e' quella dello spigolo in comune.",
                    Precondizione = "corpo_solido_e_faccia",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "da_faccia_uid", O(
                                "type", "string",
                                "description", "una faccia del primo corpo, presa dallo stato del documento "
                                    + "(F1, F2...)"),
                            "a_faccia_uid", O(
                                "type", "string",
                                "description", "una faccia dell'ALTRO corpo, che tocca la prima lungo uno "
                                    + "spigolo e fa angolo con lei"),
                            "lato_mm", O(
                                "type", "number",
                                "description", "lato del cordone (a)")),
                        "required", A(
                            "da_faccia_uid",
                            "a_faccia_uid",
                            "lato_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "distinta_taglio",
                    Famiglia = "saldature",
                    DocumentoRichiesto = "parte",
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
                        + "cambiano.\n\nUNA VOCE PER OGNI GRUPPO DI BARRE UGUALI: il pezzo diventa "
                        + "un saldato (la lavorazione «Saldatura», se manca) e la distinta e' quella "
                        + "automatica di SOLIDWORKS, che separa le barre per lunghezza. La risposta "
                        + "porta le voci — descrizione, lunghezza, quantita' — e la somma delle "
                        + "quantita' deve essere il numero delle barre, se no lo dice.",
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
                    DocumentoRichiesto = "parte",
                    // ── NON PROVATA: il fazzoletto non nasce ────────────
                    //
                    // Era `Provata = true` senza nessun caso che la
                    // esercitasse (voce Z-C6), e quando il caso si e' provato
                    // a scriverlo, il 25/9/2026, **non e' nato nessun
                    // fazzoletto**. Su una T di tubolari 80x80x5 tagliata per
                    // davvero, con la faccia alta della barra lunga e la
                    // faccia laterale della corta (si toccano lungo uno
                    // spigolo di 68 mm, ad angolo retto — le stesse su cui
                    // `cordone_saldatura` nasce):
                    //
                    //   - `InsertGussetFeature3` con le due facce selezionate,
                    //     senza marca, marche 1/1 e 1/2, ordine invertito;
                    //     spessore interno, esterno e da tutte e due le parti;
                    //     profilo triangolare e poligonale; lati o angolo;
                    //     posizione inizio e centro;
                    //   - `InsertGussetFeature2` con le facce passate come
                    //     `Face2[]`, come `DispatchWrapper[]`, e per selezione;
                    //   - `InsertGussetFeature`, la prima, per selezione;
                    //   - su un telaio 1000x800 a quartabuono, le due facce
                    //     interne di un angolo.
                    //
                    // **null ogni volta, albero e volume invariati.** Lo stesso
                    // difetto di `taglia_membri` (array non tipizzati) e' stato
                    // escluso passando gli array tipizzati. Voce GP20 del TODO
                    // per dove ripartire.
                    Provata = false,
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
                    DocumentoRichiesto = "parte",
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
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Taglia o estende le barre dove si incontrano.",
                    Descrizione =
                        "Taglia le barre dove si incontrano, o le estende fino a toccarsi. "
                        + "E' il «taglia/estendi» della barra di saldature.\n\nServe quando "
                        + "due gruppi di barre si incrociano e non sono stati trattati agli "
                        + "angoli: senza, i corpi restano compenetrati e la distinta di "
                        + "taglio riporta lunghezze che in officina non tornano.\n\nTi torno "
                        + "di quanto e' cambiato il volume (negativo = tolto). Se il pezzo "
                        + "non cambia, rifiuto: vuol dire che le barre non si incontrano, "
                        + "e un taglio che non taglia niente non te lo confermo.",
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
                    DocumentoRichiesto = "parte",
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
                    DocumentoRichiesto = "parte",
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
                    Esempio = "{\"formato\":\"A3\"}",   // da `due_fogli_e_lo_stesso_diedro`
                    DocumentoRichiesto = "parte/assieme",
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
                    DocumentoRichiesto = "tavola",
                    Provata = true,
                    Sommario =
                        "Il PDF di TUTTI i fogli, con le pagine contate.",
                    Descrizione =
                        "Scrive il PDF della tavola.\n\nTUTTI i fogli, non solo quello che "
                        + "stai guardando: l'esportazione di serie prende il foglio "
                        + "corrente, e una tavola da tre fogli esce in officina come una "
                        + "pagina sola senza dare nessun errore. Dopo aver scritto il file "
                        + "si CONTANO le pagine e si confrontano con i fogli: se non "
                        + "tornano, te lo dico.\n\nCon «cerca» il PDF si RILEGGE: dimmi le "
                        + "scritte che devono esserci (il numero di disegno, il nome del "
                        + "pezzo) e ti dico quali ci sono («trovati») e quali no "
                        + "(«non_trovati»). Il testo nel PDF e' scritto come indici di "
                        + "glifo, e una ricerca nei byte non lo trova: qui si decodifica.",
                    Precondizione = "una tavola aperta",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "percorso", O(
                                "type", "string",
                                "description", "dove scrivere il PDF. Se non lo dici, accanto alla tavola con lo "
                                    + "stesso nome."),
                            "cerca", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "scritte che il PDF deve contenere: si rilegge il file e si dice quali ci sono")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "imposta_foglio",
                    Famiglia = "tavola",
                    DocumentoRichiesto = "tavola",
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
                    DocumentoRichiesto = "tavola",
                    Provata = true,
                    Sommario =
                        "Porta in tavola le quote che il pezzo ha gia'.",
                    Descrizione =
                        "Prende le quote dal MODELLO e le mette sulle viste.\n\nFUORI "
                        + "PROCESSO, SU QUESTA INSTALLAZIONE, NON PORTA NIENTE: misurato il "
                        + "18/9/2026 su un pezzo con sette quote tutte marcate per il disegno, "
                        + "cinque strade di import, zero quote in tavola. In processo, dentro "
                        + "un add-in, l'11/9/2026 ne erano arrivate undici — mai rimisurato. "
                        + "Per mettere quote in tavola da qui c'e' «quota_in_tavola», che le "
                        + "CREA sugli spigoli che la vista mostra.\n\nSE NON ARRIVA NIENTE NON "
                        + "VUOL DIRE che il pezzo non ha quote: fuori processo e' il risultato "
                        + "normale. Prima di concludere che il pezzo va riquotato, guarda "
                        + "nello stato del pezzo se le sue lavorazioni hanno quote.\n\nPerche' "
                        + "esiste lo stesso: una quota che viene dal modello non puo' portare "
                        + "un numero diverso dalla geometria, mentre l'API di SolidWorks "
                        + "permette di scrivere in tavola un testo che la geometria non ha."
                        + "\n\nDi serie arrivano solo le quote "
                        + "MARCATE per il disegno, cioe' quelle scelte per essere mostrate: "
                        + "con «tutte_le_quote» arrivano anche le altre, e la tavola diventa "
                        + "parecchio piu' fitta.\n\nMI RIFIUTO se la tavola non ha viste (le quote "
                        + "non avrebbero dove attaccarsi) e se non arriva nessuna quota: te lo dico "
                        + "con quante il pezzo ne ha marcate, invece di rispondere «fatto».",
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
                    Nome = "croci_di_centro",
                    Famiglia = "tavola",
                    DocumentoRichiesto = "tavola",
                    // ── PROVATA DAL 26/9/2026 (libreria 0.125.0) ──────────
                    //
                    // Dal 17/9 era `Provata = false`: `AutoInsertCenterMarks2`
                    // rispondeva True e metteva zero croci in 25 combinazioni, e
                    // `InsertCenterMark2/3` sugli spigoli selezionati zero. Il
                    // 26/9 (`prova0089 croci2`, col controllo positivo):
                    // le croci sui fori C'ERANO (le mette SOLIDWORKS creando la
                    // vista) e `GetCenterMarkCount` non le contava; da codice una
                    // croce nuova non nasce. L'operazione e' un controllo: conta
                    // enumerando e rifiuta i fori senza croce. Vedi la voce W2.11.
                    Provata = true,
                    Sommario =
                        "Controlla che ogni foro delle viste abbia la croce di centro, contandole davvero, e rifiuta se ne manca.",
                    Descrizione =
                        "CONTROLLA che ogni foro visibile nelle viste abbia la croce di centro, e "
                        + "prova a metterla dove manca. SOLIDWORKS le mette da se' creando la vista, "
                        + "se l'opzione del documento «Croci di centro sui fori» e' accesa: quei fori "
                        + "li riconosco («cerchi_che_avevano_gia_la_croce»). Da codice una croce "
                        + "nuova non nasce (misurato): un foro senza croce e' un rifiuto, con la "
                        + "strada — l'opzione accesa e la vista rifatta.\n\nPERCHE' SERVE. Una tavola con dodici fori e "
                        + "nessuna croce di centro si stampa e sembra completa. In officina manca "
                        + "la cosa che serve per forare: **dove sta il centro.**\n\nTI DICO "
                        + "QUANTE NE SONO NATE, contate sulla vista: ogni croce deve far salire il "
                        + "conto di uno, e un cerchio rimasto senza croce e' un rifiuto. Se il "
                        + "numero e' **zero** te lo dico invece di dire «fatto» — quasi sempre la "
                        + "vista e' quella sbagliata (i fori come cerchi si vedono dall'alto, di "
                        + "solito V2).\n\nLA DIMENSIONE, se non me la dici, e' quella del "
                        + "documento: e' una convenzione di studio.\n\nSenza «vista» le guardo "
                        + "tutte. Raccordi e asole no: misurato, sugli archi la croce da codice "
                        + "non nasce.\n\nLE CROCI SI CONTANO ENUMERANDOLE: il contatore di "
                        + "SOLIDWORKS (GetCenterMarkCount) non vede quelle messe alla creazione "
                        + "della vista, e diceva zero su un foglio che ne aveva tre.",
                    Precondizione = "una tavola con almeno una vista",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "vista", O(
                                "type", "string",
                                "description", "UNA vista, come la chiama lo stato della tavola (V1, V2...) "
                                    + "o per nome. Vuoto = tutte le viste."),
                            "su", O(
                                "type", "string",
                                "enum", A("fori"),
                                "description", "«fori», i cerchi interi (l'unico: sui raccordi la croce da codice non nasce, misurato)."),
                            "dimensione_mm", O(
                                "type", "number",
                                "description", "quanto e' grande la croce. Se non lo dici uso le "
                                    + "impostazioni del documento, che e' quasi sempre la cosa giusta."),
                            "linee_estese", O(
                                "type", "boolean",
                                "description", "le linee d'asse che escono dalla croce fino al bordo "
                                    + "del cerchio. Spente se non dici niente.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "quota_in_tavola",
                    Famiglia = "tavola",
                    Esempio = "{\"spigoli\":[0,1,2,3]}",   // da `la_tavola_si_quota`
                    DocumentoRichiesto = "tavola",
                    // ── PROVATA, E COSA VUOL DIRE QUI ──────────────────
                    //
                    // Misurato il 18/9/2026 fuori processo: quattro quote su
                    // quattro spigoli, e i VALORI RILETTI con
                    // `GetSystemValue2` — 120 mm la piastra, 5 mm lo smusso, su
                    // un pezzo 120 x 80 x 10 con smusso 5.
                    //
                    // Non basta che compaiano: una quota che misura un'altra
                    // cosa e' peggio di una quota che manca, perche' sulla
                    // carta si legge uguale. Per questo l'operazione rilegge
                    // ogni quota e restituisce il millimetro, e per questo il
                    // primo giro della sonda — che rispondeva `?` al valore —
                    // NON sarebbe bastato a marcarla provata.
                    Provata = true,
                    Sommario =
                        "Mette le quote sugli spigoli di una vista, e RILEGGE quanto misurano.",
                    Descrizione =
                        "Mette quote di riferimento sugli spigoli di una vista di tavola, e "
                        + "per ognuna ti dice **quanti millimetri misura**, riletti da "
                        + "SOLIDWORKS dopo averla messa.\n\nQUESTA E' L'OPERAZIONE CHE "
                        + "QUOTA. «quote_dal_modello» fa un'altra cosa: IMPORTA le quote "
                        + "che il pezzo ha gia', e su questa installazione non ne porta "
                        + "nessuna (misurato su otto modelli di tavola diversi). Se ti "
                        + "serve una tavola quotata, usa questa.\n\nGLI SPIGOLI HANNO UN "
                        + "NUMERO, e te lo dico io: la vista espone il suo elenco, e senza "
                        + "«spigoli» quoto i primi «quante». Chiedimi uno spigolo che non "
                        + "c'e' e ti rispondo quanti ne ha la vista, invece di quotarne un "
                        + "altro.\n\nTI DICO QUANTE NE SONO NATE, non che ho chiamato "
                        + "l'API: le quote si contano prima e dopo, una per una. Se non ne "
                        + "nasce nessuna te lo dico, e ti dico quali spigoli ho provato."
                        + "\n\nDOVE FINISCONO: fuori dalla vista — le orizzontali sotto, le "
                        + "verticali di lato, dentro la cornice del foglio — dalle piu' piccole "
                        + "alle piu' grandi. Non so se si accavallano col disegno: quotare BENE "
                        + "e' un mestiere, e questa operazione mette quote giuste, non una "
                        + "tavola d'autore."
                        + "\n\nFORI E SMUSSI LI QUOTO DA SOLO, in ogni vista che li mostra, anche "
                        + "se non li chiedi: i fori col RICHIAMO DEL FORO («4× Ø8 PASSANTE», uno "
                        + "per diametro) e la posizione dai due bordi di riferimento, quello a "
                        + "sinistra e quello in basso; gli smussi con la QUOTA DA SMUSSO («5 x "
                        + "45°»), non coi loro spigoli — l'ipotenusa e i tratti accorciati "
                        + "dallo smusso non si quotano. Un foro cieco porta la profondita' "
                        + "(«Ø8 ↧5»); se il richiamo non nasce, la scrivo io dalla geometria."
                        + "\n\nUN CERCHIO NON E' SEMPRE UN FORO: il contorno di un pezzo tondo "
                        + "visto di fronte ha una quota di diametro (diametri_esterni)."
                        + "\n\nIL PEZZO TONDO VISTO DI FIANCO (albero, boccola, perno) lo quoto "
                        + "cosi': le LUNGHEZZE fra le facce in parallelo dalla faccia di "
                        + "un'estremita' (tondo_lunghezze_mm) e i DIAMETRI dalle due generatrici "
                        + "di ogni cilindro, col Ø (tondo_diametri_mm), ognuno riletto. SOLIDWORKS "
                        + "non sempre espone le generatrici di un cilindro: quei diametri te li "
                        + "dico in tondo_diametri_non_raggiunti, e vanno quotati a mano (un foro "
                        + "interno si quota dalla vista in cui e' un cerchio)."
                        + "\n\nUNA QUOTA SOLA PER ELEMENTO IN TUTTA LA TAVOLA: se la stessa "
                        + "estensione del pezzo e' gia' quotata in un'altra vista, qui la salto "
                        + "e te lo dico (UNI ISO 129-1). Quindi quota le viste una alla volta, e "
                        + "non aspettarti in ogni vista le misure che ha gia' un'altra."
                        + "\n\nMI RIFIUTO SE LO SPIGOLO CHIESTO NON ESISTE nella vista, e ti dico "
                        + "quanti ne mostra. Un indice che non c'e' e' l'errore piu' facile da "
                        + "fare quotando alla cieca. E rifiuto una vista in cui non resta niente "
                        + "da quotare perche' e' tutto gia' quotato altrove: zero quote non sono "
                        + "una risposta «fatto».",
                    Precondizione = "una tavola con almeno una vista che mostri spigoli",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "vista", O(
                                "type", "string",
                                "description", "UNA vista: il suo identificativo nello stato della "
                                    + "tavola (V1, V2...) oppure il suo nome. Vuoto = la prima vista vera."),
                            "spigoli", O(
                                "type", "array",
                                "items", O("type", "integer"),
                                "description", "i numeri degli spigoli da quotare, dall'elenco che la "
                                    + "vista espone (il primo e' 0). Vuoto = i primi «quante»."),
                            "quante", O(
                                "type", "integer",
                                "description", "quante quote mettere quando non dici quali spigoli. "
                                    + "Predefinito 4."),
                            "distacco_mm", O(
                                "type", "number",
                                "description", "quanto sotto l'ingombro della vista mettere la prima "
                                    + "quota. Predefinito 12."),
                            "passo_mm", O(
                                "type", "number",
                                "description", "la distanza fra una quota e la successiva. "
                                    + "Predefinito 10.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "tabella_fori_in_tavola",
                    Famiglia = "tavola",
                    DocumentoRichiesto = "tavola",
                    Provata = true,
                    Sommario =
                        "La tabella fori su una vista: un tag per foro, la posizione dall'origine e la misura.",
                    Descrizione =
                        "Mette in tavola la TABELLA FORI della faccia forata che si vede in una vista: "
                        + "per ogni foro un tag (A1, A2...), la posizione X e Y da un vertice d'angolo "
                        + "del pezzo, e la misura («Ø8 PASSANTE»). E' la tabella che serve a chi fora "
                        + "una piastra con tanti fori, al posto di una quota per foro.\n\nLa vista e' "
                        + "quella in cui i fori si vedono come cerchi (di solito V2, dall'alto): la "
                        + "faccia la sceglie l'operazione fra quelle che guardano verso chi legge, la "
                        + "piu' forata. «origine» dice da quale angolo di quella faccia si misura "
                        + "(predefinito basso_sinistra).\n\nLa tabella si RILEGGE: le righe devono "
                        + "essere i fori piu' l'intestazione, e ogni posizione scritta deve "
                        + "corrispondere a un foro del pezzo misurato sulla vista. Misurato: con "
                        + "l'origine e la faccia selezionate male SOLIDWORKS fa nascere una tabella "
                        + "VUOTA senza errore.\n\nMI RIFIUTO se nessuna faccia con dei fori guarda "
                        + "verso chi legge in quella vista, e se la tabella nasce vuota o dice "
                        + "posizioni che il pezzo non ha: in quei casi la tolgo.",
                    Precondizione = "una tavola con una vista del pezzo in cui i fori si vedono come cerchi",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "vista", O(
                                "type", "string",
                                "description", "la vista, come la chiama lo stato della tavola (V1, V2...) o per nome. Predefinita la prima."),
                            "origine", O(
                                "type", "string",
                                "enum", A("basso_sinistra", "basso_destra", "alto_sinistra", "alto_destra"),
                                "description", "da quale angolo della faccia forata si misurano le posizioni. Predefinito basso_sinistra."),
                            "x_mm", O("type", "number", "description", "dove va l'angolo in alto a sinistra della tabella sul foglio, in mm. Predefinito 15."),
                            "y_mm", O("type", "number", "description", "idem in verticale. Predefinito 15 sotto il bordo alto del foglio."),
                            "inizio_tag", O("type", "string", "description", "la lettera dei tag. Predefinito «A».")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "vista_sviluppo",
                    Famiglia = "tavola",
                    DocumentoRichiesto = "tavola",
                    Provata = true,
                    Sommario = "La vista dello SVILUPPO di una lamiera in tavola, misurata contro il pezzo.",
                    Descrizione =
                        "Mette in tavola la vista dello SVILUPPO di un pezzo di lamiera: la lamiera "
                        + "distesa, con le linee di piega, com'e' prima di andare in pressa. E' la "
                        + "vista che serve a chi taglia il lamierino.\n\nLa vista si MISURA: "
                        + "l'ingombro si legge dagli spigoli visibili (non dal contorno della vista, "
                        + "che comprende la nota di piega e un margine — misurato: 92,6 mm per uno "
                        + "sviluppo di 80,7) e si confronta con lo sviluppo del pezzo misurato prima "
                        + "di mettere la vista. La risposta porta l'ingombro, le linee di piega e la "
                        + "lunghezza dello sviluppo. La vista va in un posto libero del foglio (misurata "
                        + "a scala 2:1 a 300/90 mm cadeva nel cartiglio).\n\nMI RIFIUTO se il pezzo non e' di lamiera, se "
                        + "la vista nasce ma non e' uno sviluppo, o se misura un'altra cosa dallo "
                        + "sviluppo del pezzo: in quei casi la tolgo.",
                    Precondizione = "una tavola e, aperto, un pezzo di lamiera salvato su disco",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "modello", O("type", "string", "description", "il percorso del pezzo di lamiera. Vuoto = quello della tavola."),
                            "configurazione", O("type", "string", "description", "la configurazione del pezzo. Vuota = quella attiva."),
                            "x_mm", O("type", "number", "description", "dove va il centro della vista sul foglio, in mm. Vuoto = un posto libero fuori dalle viste e dal cartiglio."),
                            "y_mm", O("type", "number", "description", "idem in verticale."),
                            "nascondi_pieghe", O("type", "boolean", "description", "senza le linee di piega. Predefinito falso: chi piega le vuole.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "saldatura_in_tavola",
                    Famiglia = "tavola",
                    DocumentoRichiesto = "tavola",
                    Provata = true,
                    Sommario = "Il simbolo di saldatura ISO sul giunto fra due corpi, in una vista.",
                    Descrizione =
                        "Mette in tavola il SIMBOLO DI SALDATURA (ISO 2553) sul giunto fra due corpi "
                        + "di un saldato: la freccia sulla linea del giunto, il simbolo del cordone "
                        + "(angolo, V, mezza V, testa), la gola «a» a sinistra e, se la dai, la "
                        + "lunghezza a destra.\n\nIL GIUNTO LO TROVO IO: fra gli spigoli che la vista "
                        + "mostra, quello in cui un corpo tocca l'altro (il piu' lungo). Il simbolo "
                        + "si sposta fuori dal pezzo, dalla parte di «lato», oltre le quote gia' "
                        + "messe.\n\nSI RILEGGE: simbolo, gola, lunghezza, «tutto attorno». Il "
                        + "codice del simbolo e' quello della libreria di SOLIDWORKS («<WELD-FILL>» "
                        + "per l'angolo): misurato, con un codice sbagliato il simbolo nasce lo "
                        + "stesso, si rilegge uguale, e non disegna il triangolo.\n\nMI RIFIUTO se "
                        + "il pezzo ha un corpo solo, se nella vista non si vede nessun giunto, e se "
                        + "il simbolo nato non e' quello chiesto (in quel caso lo tolgo).",
                    Precondizione = "una tavola con una vista di un saldato in cui si vede il giunto fra due corpi",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "vista", O("type", "string", "description", "la vista, come la chiama lo stato della tavola (V1, V2...) o per nome. Predefinita la prima."),
                            "tipo", O("type", "string", "enum", A("angolo", "v", "mezza_v", "testa"), "description", "il cordone. Predefinito angolo."),
                            "a_mm", O("type", "number", "description", "la gola del cordone in mm (ISO 2553, «a»). Da 0 a 50."),
                            "lunghezza_mm", O("type", "number", "description", "la lunghezza del cordone, a destra del simbolo. Vuota = tutto il giunto."),
                            "tutto_attorno", O("type", "boolean", "description", "il cerchio del cordone tutto attorno. Predefinito falso."),
                            "in_opera", O("type", "boolean", "description", "la bandierina della saldatura in opera (in cantiere). Predefinito falso."),
                            "entrambi_i_lati", O("type", "boolean", "description", "il cordone dai due lati del giunto. Predefinito falso."),
                            "lato", O("type", "string", "enum", A("alto", "basso", "sinistra", "destra"), "description", "da che parte della vista va il simbolo. Predefinito alto.")),
                        "required", A("a_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "distinta_base_in_tavola",
                    Famiglia = "tavola",
                    DocumentoRichiesto = "tavola",
                    Provata = true,
                    Sommario = "La distinta base di un assieme in tavola, confrontata coi componenti dell'assieme.",
                    Descrizione =
                        "Mette in tavola la DISTINTA BASE dell'assieme che sta dietro una vista: numero di "
                        + "voce, componente, quantita'. Di serie il primo livello; «solo_parti» o "
                        + "«indentata» per le altre.\n\nLA CONFRONTO: l'assieme sa per conto suo quali "
                        + "componenti ha e quanti; le voci e la somma delle quantita' devono combaciare. Se non "
                        + "combaciano la tolgo e te lo dico.\n\nPoi la misuro e la metto in un posto libero "
                        + "del foglio, fuori dalle viste e dal cartiglio.\n\nMI RIFIUTO se dietro la vista non "
                        + "c'e' un assieme (per un saldato c'e' «distinta_taglio_in_tavola») o se sul foglio non "
                        + "c'e' posto.",
                    Precondizione = "una tavola con una vista di un assieme salvato",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "vista", O("type", "string", "description", "la vista dell'assieme (V1, V2...) o per nome. Predefinita la prima."),
                            "tipo", O("type", "string", "enum", A("primo_livello", "solo_parti", "indentata"), "description", "che distinta. Predefinito primo_livello."),
                            "template", O("type", "string", "description", "il modello di tabella. Vuoto = il primo della cartella dei modelli di distinta.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "bollatura",
                    Famiglia = "tavola",
                    DocumentoRichiesto = "tavola",
                    Provata = true,
                    Sommario = "La bollatura: i palloncini coi numeri di voce della distinta sui componenti di una vista, contati.",
                    Descrizione =
                        "Fa la BOLLATURA: mette i palloncini sui componenti dell'assieme in una vista, col numero di voce della "
                        + "distinta base (AutoBalloon5). Li conta sulla vista prima e dopo, e ti dice i numeri "
                        + "scritti. Poi confronta le voci della distinta coi palloncini di TUTTA la tavola e ti "
                        + "dice quali voci restano senza (quel componente in questa vista non si vede: bolla "
                        + "un'altra vista). Se ogni voce ha gia' il suo palloncino altrove, non ne mette e "
                        + "risponde «niente da aggiungere».\n\nMI RIFIUTO se in tavola non c'e' ancora una distinta base (la bollatura "
                        + "porta il suo numero di voce: mettila prima con «distinta_base_in_tavola») o se "
                        + "dietro la vista non c'e' un assieme.",
                    Precondizione = "una tavola con la distinta base e una vista dell'assieme",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "vista", O("type", "string", "description", "la vista (V1, V2...) o per nome. Predefinita la prima."),
                            "disposizione", O("type", "string", "enum", A("quadrato", "cerchio", "sopra", "sotto"), "description", "come disporre i palloncini attorno alla vista. Predefinito quadrato.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "rugosita_in_tavola",
                    Famiglia = "tavola",
                    DocumentoRichiesto = "tavola",
                    Provata = true,
                    Sommario = "Il simbolo di rugosita' (Ra in micrometri) su uno spigolo di bordo di una vista.",
                    Descrizione =
                        "Mette il simbolo di RUGOSITA' (UNI EN ISO 1302) con la linea di richiamo sullo spigolo "
                        + "di bordo di una vista, dal «lato» chiesto, e lo porta fuori dal pezzo. «lavorazione»: "
                        + "asportazione (la barretta, predefinito), qualsiasi, senza_asportazione (il cerchietto)."
                        + "\n\nIl Ra si RILEGGE dal simbolo. Misurato: senza spostarlo, SOLIDWORKS mette il simbolo "
                        + "fuori cornice ignorando le coordinate; per questo la posizione si imposta e si rilegge."
                        + "\n\nMI RIFIUTO se Ra non sta fra 0 e 100 micrometri, se la vista non ha uno spigolo dritto "
                        + "su quel lato, e se il simbolo nato dice un Ra diverso.",
                    Precondizione = "una tavola con una vista",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "vista", O("type", "string", "description", "la vista, V1, V2... o per nome. Predefinita la prima."),
                            "lato", O(
                                "type", "string",
                                "enum", A("alto", "basso", "sinistra", "destra"),
                                "description", "su quale lato della vista sta lo spigolo a cui si attacca. Predefinito «alto»."),
                            "ra_um", O("type", "number", "description", "la rugosita' Ra in micrometri, per esempio 3.2"),
                            "lavorazione", O(
                                "type", "string",
                                "enum", A("asportazione", "qualsiasi", "senza_asportazione"),
                                "description", "come si ottiene la superficie. Predefinito «asportazione».")),
                        "required", A("ra_um")),
                },
                new OperazioneDisegno
                {
                    Nome = "datum_in_tavola",
                    Famiglia = "tavola",
                    DocumentoRichiesto = "tavola",
                    Provata = true,
                    Sommario = "Un riferimento (datum A, B...) su uno spigolo di bordo di una vista.",
                    Descrizione =
                        "Mette un DATUM (UNI EN ISO 5459): il triangolo sullo spigolo di bordo della vista dal "
                        + "«lato» chiesto, e la bandierina con la «lettera» fuori dal pezzo, dalla stessa parte. "
                        + "E' il riferimento che le tolleranze geometriche citano.\n\nLa lettera si RILEGGE. "
                        + "Misurato: lasciato dove nasce, SOLIDWORKS mette la bandierina dall'altra parte dello "
                        + "spigolo e la linea attraversa il pezzo; per questo si sposta.\n\nMI RIFIUTO se la "
                        + "lettera c'e' gia' in tavola — due datum con la stessa lettera non danno errore e rendono "
                        + "ambigua ogni tolleranza — e se la vista non ha uno spigolo dritto su quel lato.",
                    Precondizione = "una tavola con una vista",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "vista", O("type", "string", "description", "la vista, V1, V2... o per nome. Predefinita la prima."),
                            "lato", O(
                                "type", "string",
                                "enum", A("alto", "basso", "sinistra", "destra"),
                                "description", "su quale lato della vista sta lo spigolo a cui si attacca. Predefinito «basso»."),
                            "lettera", O("type", "string", "description", "la lettera del riferimento, dalla A alla Z. Predefinita «A».")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "tolleranza_geometrica_in_tavola",
                    Famiglia = "tavola",
                    DocumentoRichiesto = "tavola",
                    Provata = true,
                    Sommario = "Una cornice di tolleranza geometrica (planarita', perpendicolarita', posizione...) su uno spigolo.",
                    Descrizione =
                        "Mette una CORNICE DI TOLLERANZA GEOMETRICA (UNI EN ISO 1101) con la freccia sullo spigolo "
                        + "di bordo della vista dal «lato» chiesto: la «caratteristica», l'ampiezza della zona in mm "
                        + "e i «riferimenti» (fino a tre lettere di datum, nell'ordine primario, secondario, "
                        + "terziario). «diametro» mette il Ø davanti al valore (una zona cilindrica, per la "
                        + "posizione di un asse).\n\nSimbolo, valore e riferimenti si RILEGGONO dalla cornice."
                        + "\n\nMI RIFIUTO se una tolleranza di forma (rettilineita', planarita', circolarita', "
                        + "cilindricita') cita un datum, se una di orientamento o di posizione non ne cita nessuno, "
                        + "se un datum citato non c'e' in tavola (mettilo prima con «datum_in_tavola»), e se la "
                        + "cornice nata non dice cio' che si e' chiesto.",
                    Precondizione = "una tavola con una vista, e i datum che si citano gia' messi",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "vista", O("type", "string", "description", "la vista, V1, V2... o per nome. Predefinita la prima."),
                            "lato", O(
                                "type", "string",
                                "enum", A("alto", "basso", "sinistra", "destra"),
                                "description", "su quale lato della vista sta lo spigolo a cui si attacca. Predefinito «destra»."),
                            "caratteristica", O(
                                "type", "string",
                                "enum", A("rettilineita", "planarita", "circolarita", "cilindricita", "profilo_linea",
                                          "profilo_superficie", "inclinazione", "perpendicolarita", "parallelismo",
                                          "posizione", "concentricita", "simmetria", "oscillazione_circolare",
                                          "oscillazione_totale"),
                                "description", "la caratteristica geometrica"),
                            "valore_mm", O("type", "number", "description", "l'ampiezza della zona di tolleranza, in mm"),
                            "riferimenti", O(
                                "type", "array", "items", O("type", "string"),
                                "description", "le lettere dei datum, fino a tre, nell'ordine primario, secondario, terziario"),
                            "diametro", O("type", "boolean", "description", "zona cilindrica: il Ø davanti al valore")),
                        "required", A("caratteristica", "valore_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "nota_in_tavola",
                    Famiglia = "tavola",
                    Esempio = "{\"testo\":\"TOLLERANZE NON INDICATE ISO 2768-mK\",\"x_mm\":20,\"y_mm\":250}",   // da `la_nota_finisce_sul_foglio`
                    DocumentoRichiesto = "tavola",
                    // ── PROVATA, E SU COSA ─────────────────────────────
                    //
                    // Che la nota NASCA, che finisca sul FOGLIO e non dentro
                    // una vista, e che il testo riletto sia quello chiesto.
                    //
                    // Non e' provata — e non puo' esserlo — sul fatto che
                    // quello che c'e' scritto sia giusto: una nota e' testo
                    // libero, e «materiale S355» su un pezzo S275 passa ogni
                    // conteggio e ogni rilettura.
                    Provata = true,
                    Sommario =
                        "Scrive una nota di testo sul foglio della tavola, e RILEGGE "
                        + "quello che c'e' scritto.",
                    Descrizione =
                        "Mette una nota di testo sul foglio di una tavola — le note "
                        + "generali: «smussare tutti gli spigoli vivi», «zincare dopo "
                        + "saldatura», «tolleranze non indicate ISO 2768-mK».\n\n"
                        + "TI DICO SE E' NATA, non che ho chiamato l'API: le note si "
                        + "contano prima e dopo. E te le conto in DUE posti separati — "
                        + "sul foglio e dentro le viste — perche' sono due cose diverse "
                        + "e sommarle farebbe sembrare piena una tavola vuota.\n\n"
                        + "IL TESTO TORNA RILETTO da SOLIDWORKS, non ricopiato da quello "
                        + "che hai chiesto: una nota che compare con un testo diverso si "
                        + "legge come una nota giusta finche' nessuno la confronta.\n\n"
                        + "DOVE FINISCE: coordinate del FOGLIO, in millimetri, di serie "
                        + "in alto a sinistra. Se chiedi un punto fuori dal foglio mi "
                        + "rifiuto — fuori dal foglio la nota c'e' davvero e si conta, ma "
                        + "stampando non compare, e nessun errore lo direbbe.\n\n"
                        + "QUELLO CHE NON POSSO SAPERE e' se quello che scrivi e' VERO. "
                        + "Garantisco che la nota c'e' e dice quello che hai scritto.",
                    Precondizione = "una tavola aperta, con un foglio attivo",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "testo", O(
                                "type", "string",
                                "description", "il testo della nota. Obbligatorio: una nota vuota "
                                    + "occupa un posto sul foglio e non dice niente."),
                            "x_mm", O(
                                "type", "number",
                                "description", "distanza dal bordo sinistro del foglio, in "
                                    + "millimetri. Predefinito 20."),
                            "y_mm", O(
                                "type", "number",
                                "description", "altezza dal bordo inferiore del foglio, in "
                                    + "millimetri. Predefinito 20 mm sotto il bordo alto.")),
                        "required", A("testo")),
                },
                new OperazioneDisegno
                {
                    Nome = "sezione_in_tavola",
                    // Z-C13 (26/9/2026): una lettera gia' usata da una sezione o da un
                    // dettaglio si rifiuta; la lettera effettiva si rilegge.
                    Famiglia = "tavola",
                    Esempio = "{\"etichetta\":\"A\",\"a_mm\":0}",   // da `la_sezione_taglia_il_pezzo`
                    DocumentoRichiesto = "tavola",
                    // ── PROVATA, E SU COSA ─────────────────────────────
                    //
                    // Che la vista di sezione NASCA (il conto delle viste
                    // sale), che sia del tipo giusto, e che abbia del
                    // TRATTEGGIO — che e' l'unica cosa che distingue una
                    // sezione dentro il materiale da una linea di taglio
                    // caduta fuori, che produce una vista valida e vuota.
                    //
                    // Non e' provata — e non puo' esserlo — sul fatto che
                    // il taglio passi dove serviva: dieci millimetri piu' in
                    // la' e' altrettanto valido e non fa vedere il foro.
                    Provata = true,
                    Sommario =
                        "Crea una vista di sezione su una tavola, e controlla che "
                        + "il taglio abbia incontrato materiale.",
                    Descrizione =
                        "Taglia una vista della tavola e mette la sezione a fianco. "
                        + "E' la prima cosa che manca per una tavola d'officina vera: "
                        + "un pezzo con una cavita' interna, senza sezione, non si "
                        + "legge — le linee nascoste dicono che c'e' un vuoto, non che "
                        + "forma ha.\n\n"
                        + "LA LINEA DI TAGLIO LA TRACCIO IO, da parte a parte della "
                        + "vista: una linea che la sfiora da' una sezione vuota senza "
                        + "dire niente. Di serie taglio in mezzo; con «a_mm» sposti il "
                        + "taglio, in millimetri **di foglio** — non del modello, "
                        + "perche' sul foglio la vista e' in scala.\n\n"
                        + "TI DICO SE E' NATA contando le viste prima e dopo, non "
                        + "perche' l'API ha risposto un oggetto: puo' rispondere senza "
                        + "aver fatto niente.\n\n"
                        + "E MI RIFIUTO SE NON C'E' TRATTEGGIO. Una sezione senza "
                        + "tratteggio e' una linea di taglio caduta fuori dal "
                        + "materiale: sul foglio resta una vista che sembra buona e non "
                        + "mostra niente, e nessun errore lo direbbe. Preferisco "
                        + "fallire e dirti dove ho tagliato.\n\n"
                        + "LA LETTERA E' UNA SOLA PER TAVOLA: se «etichetta» e' gia' di "
                        + "un'altra sezione o di un dettaglio mi rifiuto e ti dico quelle "
                        + "usate — due «A-A» sembrano lo stesso taglio. La lettera che la "
                        + "vista ha davvero te la rileggo («etichetta_riletta»).\n\n"
                        + "QUELLO CHE NON POSSO SAPERE e' se quella e' la sezione che "
                        + "serviva. So dirti che il taglio ha incontrato materiale, non "
                        + "che passa per la cavita' che volevi mostrare.",
                    Precondizione = "una tavola aperta con almeno una vista di modello",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "vista", O(
                                "type", "string",
                                "description", "quale vista sezionare, per nome. Senza, "
                                    + "la prima vista di modello del foglio."),
                            "verso", O(
                                "type", "string",
                                "enum", A("verticale", "orizzontale"),
                                "description", "come corre la linea di taglio. "
                                    + "Predefinito «verticale»."),
                            "a_mm", O(
                                "type", "number",
                                "description", "di quanto spostare il taglio dal centro "
                                    + "della vista, in millimetri DI FOGLIO. Positivo "
                                    + "verso destra (o verso l'alto). Predefinito 0, "
                                    + "cioe' in mezzo."),
                            "etichetta", O(
                                "type", "string",
                                "description", "la lettera della sezione: «A» da' "
                                    + "«Vista in sezione A-A». Predefinito «A»."),
                            "profondita_mm", O(
                                "type", "number",
                                "description", "profondita' del taglio per una sezione "
                                    + "parziale. Predefinito 0, che taglia da parte a "
                                    + "parte.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "dettaglio_in_tavola",
                    Famiglia = "tavola",
                    Esempio = "{\"etichetta\":\"B\",\"scala\":2,\"raggio_mm\":8}",   // da `il_dettaglio_ingrandisce`
                    DocumentoRichiesto = "tavola",
                    // ── PROVATA, E SU COSA ─────────────────────────────
                    //
                    // Che la vista di dettaglio NASCA, che sia del tipo
                    // giusto, e che il cerchio sia DIVENTATO una
                    // circonferenza di dettaglio sulla vista di partenza —
                    // che e' cio' che distingue un dettaglio vero da un
                    // cerchio disegnato a mano accanto a una vista in piu'.
                    //
                    // NON si guarda il tratteggio: un dettaglio non taglia
                    // niente, e la campitura li' e' zero (misurato). Copiare
                    // il controllo della sezione darebbe un'operazione che
                    // rifiuta sempre.
                    Provata = true,
                    Sommario =
                        "Crea una vista di dettaglio ingrandita su una tavola, e "
                        + "controlla che il cerchio sia diventato un dettaglio.",
                    Descrizione =
                        "Ingrandisce un pezzo di una vista. E' l'altra meta' di cio' "
                        + "che manca a una tavola d'officina: la sezione fa vedere cosa "
                        + "c'e' dentro, il dettaglio fa vedere cio' che e' troppo "
                        + "piccolo per leggersi alla scala del disegno — una gola, uno "
                        + "smusso, un raccordo con la sua quota.\n\n"
                        + "IL CERCHIO LO TRACCIO IO, dentro la vista. Di serie al centro "
                        + "con raggio un quarto del lato minore; con «x_mm» e «y_mm» lo "
                        + "sposti e con «raggio_mm» lo cambi, in millimetri **di foglio** "
                        + "— non del modello, perche' sul foglio la vista e' in scala.\n\n"
                        + "MI RIFIUTO SE IL CERCHIO SBORDA dalla vista: un cerchio fuori "
                        + "ingrandisce del vuoto, e il vuoto ingrandito e' comunque una "
                        + "vista che sembra buona.\n\n"
                        + "E TI DICO SE IL CERCHIO E' DIVENTATO UN DETTAGLIO, contando le "
                        + "circonferenze di dettaglio sulla vista di partenza prima e "
                        + "dopo. Se non sale, sul foglio resta un cerchio disegnato a mano "
                        + "accanto a una vista che non gli e' legata — e nessun errore lo "
                        + "direbbe.\n\n"
                        + "LA LETTERA E' UNA SOLA PER TAVOLA: se «etichetta» e' gia' di una "
                        + "sezione o di un altro dettaglio mi rifiuto e ti dico quelle usate. "
                        + "La lettera vera te la rileggo («etichetta_riletta»).\n\n"
                        + "QUELLO CHE NON POSSO SAPERE e' se quello e' il particolare che "
                        + "serviva: un cerchio spostato di dieci millimetri ingrandisce un "
                        + "pezzo di piano liscio, ed e' altrettanto valido.",
                    Precondizione = "una tavola aperta con almeno una vista di modello",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "vista", O(
                                "type", "string",
                                "description", "quale vista dettagliare, per nome. Senza, "
                                    + "la prima vista di modello del foglio."),
                            "x_mm", O(
                                "type", "number",
                                "description", "di quanto spostare il centro del cerchio "
                                    + "dal centro della vista, in millimetri DI FOGLIO. "
                                    + "Positivo verso destra. Predefinito 0."),
                            "y_mm", O(
                                "type", "number",
                                "description", "di quanto spostarlo in verticale, in "
                                    + "millimetri DI FOGLIO. Positivo verso l'alto. "
                                    + "Predefinito 0."),
                            "raggio_mm", O(
                                "type", "number",
                                "description", "il raggio del cerchio, in millimetri di "
                                    + "foglio. Predefinito: un quarto del lato minore "
                                    + "della vista."),
                            "scala", O(
                                "type", "number",
                                "description", "quante volte ingrandire. Predefinito 2, "
                                    + "cioe' 2:1."),
                            "etichetta", O(
                                "type", "string",
                                "description", "la lettera del dettaglio. Predefinito «B», "
                                    + "perche' «A» di solito se la prende la sezione.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "cartiglio",
                    Famiglia = "verifiche",
                    DocumentoRichiesto = "tavola",
                    Provata = true,
                    Sommario =
                        "Dice quali proprieta' il cartiglio CHIEDE e quali MOSTRA. "
                        + "Non tocca niente.",
                    Descrizione =
                        "Legge il CARTIGLIO del foglio attivo e ti dice due cose "
                        + "diverse: **quali proprieta' personalizzate chiede** e **quali "
                        + "sta mostrando**.\n\nPERCHE' SONO DUE COSE. Un cartiglio non "
                        + "contiene testo: contiene collegamenti, scritti "
                        + "`$PRP:\"Descrizione\"`. Riempirlo vuol dire scrivere quelle "
                        + "proprieta' con «scrivi_proprieta».\n\n**IL NOME VA COPIATO "
                        + "ALLA LETTERA.** Se lo scrivi con una maiuscola o uno spazio "
                        + "diversi da quelli che il cartiglio referenzia, **non succede "
                        + "niente di visibile**: SOLIDWORKS scrive la proprieta', il "
                        + "cartiglio non la mostra, e il PDF esce con la casella vuota. "
                        + "Nessun errore, da nessuna parte. Per questo ti do i nomi "
                        + "esatti invece di lasciarteli indovinare.\n\nSe una proprieta' "
                        + "e' chiesta e non mostrata te lo dico: o non e' ancora scritta, "
                        + "o e' scritta con un'altra grafia.\n\nAttento a "
                        + "`$PRPSHEET`: quella prende la proprieta' dal PEZZO in vista, "
                        + "non dal disegno. Sono due cassetti diversi, e sbagliarli vuol "
                        + "dire scrivere sul documento sbagliato.\n\nSe il cartiglio non "
                        + "ha nessuna nota collegata te lo dico invece di lasciarti "
                        + "riempire proprieta' che non comparirebbero: quel testo e' "
                        + "disegnato dentro il formato, non alimentato.",
                    Precondizione = "una tavola aperta",
                    Schema = O(
                        "type", "object",
                        "properties", O(),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "verifica_interferenze",
                    Famiglia = "verifiche",
                    Esempio = "{\"toccarsi_conta\":true}",   // da `toccarsi_non_e_compenetrarsi`
                    DocumentoRichiesto = "assieme",
                    Provata = true,
                    Sommario =
                        "Dice se i componenti dell'assieme si compenetrano, e di quanto "
                        + "volume. Non tocca niente.",
                    Descrizione =
                        "Cerca le INTERFERENZE fra i componenti dell'assieme aperto: i "
                        + "punti in cui due pezzi occupano lo stesso spazio.\n\n"
                        + "PERCHE' CONTA. Due pezzi compenetrati si aprono, si "
                        + "ricostruiscono, si esportano e si stampano: **non c'e' nessun "
                        + "errore da nessuna parte**, e in officina non entrano. E' il "
                        + "difetto che si scopre a pezzo fatto.\n\nTI DICO IL VOLUME, non "
                        + "solo quante sono. Due interferenze possono essere due decimi di "
                        + "millimetro cubo — due facce che si sfiorano — oppure migliaia, "
                        + "cioe' mezzo coperchio dentro la base. **Il conteggio da solo non "
                        + "distingue un problema da un dettaglio.**\n\nLE FACCE CHE SI "
                        + "TOCCANO, di serie, NON contano come interferenza: altrimenti "
                        + "ogni assieme montato bene risulterebbe rotto, perche' un "
                        + "coperchio appoggiato condivide una faccia con la base. Ma un "
                        + "albero in un foro dello stesso diametro condivide la superficie "
                        + "cilindrica, e li' e' proprio il problema: quel pezzo non entra. "
                        + "Se e' quello che ti interessa, chiedimelo con «toccarsi_conta», "
                        + "e ti dico sempre quale delle due ho usato.\n\nNon modifica "
                        + "niente: si puo' chiedere anche con «misura».",
                    Precondizione = "un assieme aperto con almeno due componenti",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "toccarsi_conta", O(
                                "type", "boolean",
                                "description", "vero: due facce che si toccano esattamente contano come "
                                    + "interferenza. Serve per «questo albero entra in questo foro?». "
                                    + "Falso (predefinito): un coperchio appoggiato non e' un errore."),
                            "dentro_le_parti", O(
                                "type", "boolean",
                                "description", "vero: cerca anche fra i corpi DENTRO lo stesso "
                                    + "componente multicorpo. Falso (predefinito): solo fra componenti "
                                    + "diversi.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "distinta_taglio_in_tavola",
                    Famiglia = "tavola",
                    DocumentoRichiesto = "tavola",
                    // ── PROVATA DAL 26/9/2026 (libreria 0.125.0) ──────────
                    //
                    // La tabella nasce quando il pezzo e' un SALDATO con la
                    // distinta automatica — che `distinta_taglio` adesso fa. Il
                    // testo qui sotto e' del 17/9: la causa non era la tavola,
                    // era il pezzo (`IsWeldment` falso). Misurato: telaio
                    // 1000 x 800, tabella con 2 voci, 2 x 1000 e 2 x 800.
                    //
                    // ── (17/9) NON PROVATA: la tabella non nasce ───────────
                    //
                    // `IView.InsertWeldmentTable` torna `null`, e non e' un
                    // problema di questa operazione: **su questa installazione
                    // non nasce NESSUNA tabella in tavola.** Misurato il
                    // 17/9/2026 — anche `IDrawingDoc.InsertTableAnnotation2`
                    // con tre righe, tre colonne e nessun modello torna null.
                    //
                    // Provati e tutti negativi: modello di tabella vuoto e
                    // modello vero (`cut list.sldwldtbt`); ancoraggio 0 e
                    // TopLeft; `UseAnchorPoint` vero e falso; configurazione
                    // vuota e quella esatta della vista; tutte e tre le viste;
                    // vista attivata e selezionata; **quattro modelli di tavola
                    // diversi, tre dei quali di serie** (iso, din, ansi);
                    // documento gia' attivo e riattivato con `ActivateDoc3`.
                    //
                    // E' lo stesso blocco di `croci_di_centro` (voce W2.11):
                    // due API di annotazione che non c'entrano niente fra loro,
                    // tutte e due mute sulla stessa tavola. Vedi la voce GT2
                    // del TODO per dove ripartire.
                    Provata = true,
                    Sommario =
                        "Mette in tavola la distinta di taglio del saldato, e la confronta "
                        + "con quella del pezzo.",
                    Descrizione =
                        "Inserisce nella tavola la DISTINTA DI TAGLIO del saldato che sta "
                        + "dietro una vista: l'elenco delle barre con lunghezze e "
                        + "quantita'.\n\nE LA CONFRONTO. Il pezzo sa gia', per conto suo, "
                        + "quanti corpi ha in distinta; la tabella in tavola dice la sua. "
                        + "Sono **due letture indipendenti dello stesso fatto**: se non "
                        + "combaciano te lo dico e non scelgo a quale credere. Una tavola "
                        + "che dichiara una quantita' diversa dal modello manda in officina "
                        + "il numero sbagliato di barre, e nessuno dei due lati da' "
                        + "errore.\n\nDi solito vuol dire che il pezzo e' cambiato dopo "
                        + "l'ultimo «distinta_taglio»: si rifa' quello e si rimette la "
                        + "tabella.\n\nLa colonna delle quantita' non la indovino per "
                        + "posizione: la chiedo al modello di tabella.\n\nLE COLONNE E IL POSTO: "
                        + "tengo quelle che in officina servono per tagliare (numero, quantita', "
                        + "lunghezza, i due angoli, descrizione) e nascondo le altre — il modello "
                        + "di tabella di casa ne ha 17, e la tabella usciva dal foglio. «colonne» "
                        + "ne sceglie altre per intestazione, o «tutte». Poi misuro la tabella e "
                        + "la metto in un posto libero dentro la cornice, fuori dalle viste e dal "
                        + "cartiglio; se non c'e' posto la tolgo e lo dico.",
                    Precondizione = "una tavola con una vista di un saldato che ha gia' la distinta di taglio",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "vista", O(
                                "type", "string",
                                "description", "quale vista. Vuoto = la prima della tavola."),
                            "x_mm", O(
                                "type", "number",
                                "description", "dove mettere l'angolo in alto a sinistra della tabella. Vuoto = un posto libero che trovo io."),
                            "y_mm", O(
                                "type", "number",
                                "description", "idem, in verticale."),
                            "colonne", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "le intestazioni delle colonne da tenere, o [\"tutte\"]. Vuoto = numero, quantita', lunghezza, angoli, descrizione."),
                            "configurazione", O(
                                "type", "string",
                                "description", "quale configurazione del pezzo. Vuota = quella della vista."),
                            "template", O(
                                "type", "string",
                                "description", "il modello di tabella. Vuoto = quello dell'installazione.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "nuovo_foglio",
                    Famiglia = "tavola",
                    Esempio = "{\"nome\":\"Montaggio\"}",   // da `due_fogli_e_lo_stesso_diedro`
                    DocumentoRichiesto = "tavola",
                    Provata = true,
                    Sommario =
                        "Aggiunge un foglio alla tavola, con lo stesso diedro del primo.",
                    Descrizione =
                        "Aggiunge un FOGLIO alla tavola aperta e lo attiva.\n\n"
                        + "IL DIEDRO NON TE LO CHIEDO, e non e' una semplificazione: lo "
                        + "leggo dal foglio corrente e lo ripasso uguale. Se il foglio 2 "
                        + "nascesse in un diedro diverso dal foglio 1, la stessa tavola "
                        + "avrebbe due fogli specchiati in modo diverso -- **nessuno dei "
                        + "due da' errore**, e chi taglia il pezzo del foglio 2 lo taglia "
                        + "rovesciato.\n\nE lo rileggo dal foglio nuovo dopo averlo "
                        + "creato: passarlo non e' averlo. Se non combacia mi fermo.\n\n"
                        + "Anche formato e scala, se non li dici, sono quelli del foglio "
                        + "corrente: due fogli a scale diverse si possono volere, ma non "
                        + "per distrazione.\n\nIl nome, se non lo dici, e' il primo "
                        + "«FoglioN» libero. Un nome gia' usato lo rifiuto: due fogli con "
                        + "lo stesso nome e non si sa piu' quale si sta guardando.",
                    Precondizione = "una tavola aperta",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "nome", O(
                                "type", "string",
                                "description", "come chiamarlo. Vuoto = il primo «FoglioN» libero."),
                            "formato", O(
                                "type", "string",
                                "description", "A0, A1, A2, A3, A4, A4V. Vuoto = quello del foglio corrente."),
                            "scala_numeratore", O(
                                "type", "number",
                                "description", "il primo numero della scala. Vuoto = quella del foglio corrente."),
                            "scala_denominatore", O(
                                "type", "number",
                                "description", "il secondo numero della scala. 1:2 si scrive 1 e 2.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "attiva_foglio",
                    Famiglia = "tavola",
                    Esempio = "{\"nome\":\"Foglio1\"}",   // da `due_fogli_e_lo_stesso_diedro`
                    DocumentoRichiesto = "tavola",
                    Provata = true,
                    Sommario =
                        "Mette davanti un foglio della tavola, per nome.",
                    Descrizione =
                        "Attiva un FOGLIO della tavola: da li' in poi le viste e le "
                        + "annotazioni vanno su quello.\n\nSI CHIEDE PER NOME, non per "
                        + "numero. Un indice sarebbe l'occasione perfetta per lavorare sul "
                        + "foglio sbagliato senza nessun errore.\n\nNon credo alla "
                        + "risposta: dopo averlo attivato richiedo alla tavola quale "
                        + "foglio ha davanti, e se non e' quello che hai chiesto mi fermo "
                        + "-- altrimenti i passi dopo lavorerebbero altrove.\n\nI nomi "
                        + "dei fogli stanno in «guarda_il_pezzo», sotto «fogli»."
                        + "\n\nMI RIFIUTO SE IL FOGLIO NON C'E', e ti elenco quelli che ci sono col "
                        + "loro nome. Su una tavola a piu' fogli sbagliare foglio non da' nessun "
                        + "errore: le viste finiscono su quello attivo, e in officina arriva un "
                        + "foglio con sopra roba di un altro.",
                    Precondizione = "una tavola aperta con piu' di un foglio",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "nome", O(
                                "type", "string",
                                "description", "il nome esatto del foglio, come lo riporta lo stato")),
                        "required", A("nome")),
                },
                new OperazioneDisegno
                {
                    Nome = "salva_documento",
                    Famiglia = "tavola",
                    DocumentoRichiesto = "qualunque",
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
                    DocumentoRichiesto = "tavola",
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
                    DocumentoRichiesto = "tavola",
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
                    DocumentoRichiesto = "parte",
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
                    Nome = "verifica_sovrasporgenze",
                    Famiglia = "verifiche",
                    Esempio = "{\"soglia_gradi\":70}",   // da `pareti_a_sessanta_gradi_non_si_stampano`
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Dice cosa non si stampa senza supporti, e di quanto. Non tocca il "
                        + "pezzo.",
                    Descrizione =
                        "Una parete che pende nel vuoto oltre un certo angolo si stampa a "
                        + "filamenti persi. SOLIDWORKS non dice niente -- il pezzo e' sano, "
                        + "il volume e' giusto, la geometria e' chiusa -- e il difetto si "
                        + "vede solo sulla macchina, a stampa finita.\n\nGUARDO OGNI "
                        + "SUPERFICIE, anche quelle curve: il sotto di un cilindro "
                        + "orizzontale pende nel vuoto nella sua parte bassa e non in quella "
                        + "alta, quindi non basta una normale per faccia. Tassello e guardo "
                        + "triangolo per triangolo, pesando per area.\n\nL'ANGOLO SI CONTA "
                        + "DALLA VERTICALE, come negli slicer: una parete dritta e' 0, un "
                        + "soffitto orizzontale e' 90, e «soglia 45» vuol dire che oltre i 45 "
                        + "serve il supporto. Non e' l'angolo col piano, che e' il "
                        + "complementare: due numeri che si somigliano e dicono il "
                        + "contrario.\n\nQUELLO CHE APPOGGIA SUL PIANO NON LO CONTO. La "
                        + "faccia di sotto guarda dritta in giu' -- 90 gradi, il caso "
                        + "peggiore -- ma appoggia sul piano della macchina e si stampa "
                        + "benissimo.\n\nTi rispondo la sporgenza peggiore, quanta area "
                        + "pende, e le facce una per una con dove stanno, cosi' sai se girare "
                        + "il pezzo o smussare.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "soglia_gradi", O(
                                "type", "number",
                                "description", "oltre quanti gradi dalla verticale serve il supporto. "
                                    + "Predefinito 45, che e' il valore normale di una stampante a "
                                    + "filamento; 30-35 per stare larghi, 60 se la macchina raffredda "
                                    + "bene."),
                            "direzione_di_stampa", O(
                                "type", "array",
                                "items", O("type", "number"),
                                "description", "il verso in cui cresce la stampa, tre numeri. Predefinito "
                                    + "[0, 0, 1]. Cambiarlo e' il modo di chiedere «e se lo stampassi "
                                    + "girato?» senza girare il pezzo."),
                            "corpo_uid", O(
                                "type", "string",
                                "description", "un corpo solo (per esempio «C2»). Se non lo dici li guardo "
                                    + "tutti.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "dipendenze",
                    Famiglia = "verifiche",
                    DocumentoRichiesto = "qualunque",
                    Provata = true,
                    Sommario = "Da quali file dipende il documento, e quali mancano sul disco. Non tocca niente.",
                    Descrizione =
                        "Un assieme (o una tavola) e' un grafo di file sul disco: ti dico da quali file "
                        + "dipende, a tutti i livelli, e per ognuno se c'e'. Un file che manca e' un "
                        + "componente che l'assieme mostra SOPPRESSO: prima di misurare o esportare va "
                        + "ritrovato o sostituito.\n\nSe manca lo dice il disco, non l'API: il parametro "
                        + "che promette di segnare i riferimenti rotti non li segna (misurato).",
                    Precondizione = "un documento salvato",
                    Schema = O(
                        "type", "object",
                        "properties", O(),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "proprieta_di_massa",
                    Famiglia = "verifiche",
                    DocumentoRichiesto = "parte/assieme",
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
                        + "vedere invece di nascondersi."
                        + "\n\nSU UN ASSIEME peso l'assieme intero (i componenti soppressi no, e "
                        + "te li elenco) e controllo la massa contro la somma dei pezzi letti uno "
                        + "per uno: se non combaciano mi fermo. Se un pezzo non ha materiale, la "
                        + "massa e' null e ti dico quale.",
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
                    DocumentoRichiesto = "parte/assieme",
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
                        + "misura non dice come girarlo."
                        + "\n\nSU UN ASSIEME misuro ogni pezzo nel posto in cui sta nell'assieme "
                        + "e la scatola che li contiene tutti; i componenti soppressi no, e te "
                        + "li elenco.",
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
                    DocumentoRichiesto = "parte",
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
                    DocumentoRichiesto = "parte",
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
                    Esempio = "{\"operazione\":\"interseca\"}",   // da `interseca_due_cubi`
                    DocumentoRichiesto = "parte",
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
                        + "identificativi di corpi e facce NON valgono piu': rileggi il pezzo."
                        + "\n\nSE NON DICI QUALE BOOLEANA faccio «unisci», ed e' scritto "
                        + "nella risposta. «sottrai» e «interseca» si chiedono per nome, "
                        + "perche' cambiano il pezzo in modo opposto.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "operazione", O(
                                "type", "string",
                                "enum", A("unisci", "sottrai", "interseca"),
                                "default", "unisci",
                                "description", "che booleana fare. Se non lo dici e' «unisci», "
                                    + "e te lo scrivo nella risposta."),
                            "corpo_principale_uid", O(
                                "type", "string",
                                "description", "il corpo che RESTA (per esempio «C1»). Se non lo dici e' il "
                                    + "primo. Conta solo per «sottrai», ma scriverlo non fa mai male."),
                            "corpi_utensile_uid", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "i corpi da unire, sottrarre o intersecare col principale. Se "
                                    + "non li dici, tutti gli altri.")),
                        // Z-G3: predefinito «unisci», dichiarato nella risposta.
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "distanza_minima",
                    Famiglia = "verifiche",
                    DocumentoRichiesto = "parte",
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
                    DocumentoRichiesto = "parte",
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
                    DocumentoRichiesto = "parte",
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
                    DocumentoRichiesto = "parte/assieme",
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
                        + "documento, quindi non e' il predefinito."
                        + "\n\nANCHE UN ASSIEME: lo scrivo intero e conto i solidi nel file contro "
                        + "quelli dei pezzi non soppressi. «riapri_e_confronta» su un assieme lo "
                        + "rifiuto: rileggere lo STEP di un assieme apre una finestra e la chiamata "
                        + "non torna.",
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
                    DocumentoRichiesto = "parte/assieme",
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
                    Nome = "profili_saldatura",
                    Famiglia = "saldature",
                    Esempio = "{\"cerca\":\"TUBO STRUTTURALE QUADRO\"}",   // da `i_profili_si_leggono_prima`
                    DocumentoRichiesto = "nessuno",
                    Provata = true,
                    Sommario =
                        "Elenca i profili della libreria e le loro misure, scritte come vanno "
                        + "copiate. Non tocca niente.",
                    Descrizione =
                        "Dice quali profili ci sono -- tubolari, IPE, angolari, piatti -- e "
                        + "per ognuno quali misure, scritte esattamente come vanno copiate."
                        + "\n\nCHIAMAMI PRIMA di «membro_strutturale» e «telaio_rettangolare». "
                        + "Loro vogliono «profilo_percorso» e «configurazione» ALLA LETTERA, e "
                        + "alla lettera vuol dire davvero: maiuscole, spazi e virgole contano. "
                        + "Una misura scritta quasi giusta NON DA' ERRORE -- SOLIDWORKS usa la "
                        + "configurazione attiva del profilo, il pezzo esce con una parete "
                        + "diversa da quella che credi, la ricostruzione e' pulita, e te ne "
                        + "accorgi in officina.\n\nLeggo i file senza aprirli: non apro "
                        + "documenti e non lascio niente in giro.\n\nSe la libreria e' grande "
                        + "te ne do i primi quaranta e te lo dico: con «cerca» e un pezzo del "
                        + "nome li restringi.",
                    Precondizione = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "cerca", O(
                                "type", "string",
                                "description", "un pezzo del nome del profilo, per restringere: per esempio "
                                    + "«tubo», «IPE», «angolare». Senza, li elenca tutti."),
                            "con_misure", O(
                                "type", "boolean",
                                "description", "vero (predefinito) per avere anche le misure di ogni profilo. "
                                    + "Falso se ti basta sapere quali profili esistono: costa meno.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "accoppia",
                    Famiglia = "assiemi",
                    DocumentoRichiesto = "assieme",
                    Provata = true,
                    Sommario =
                        "Vincola due componenti fra loro prendendo una faccia per uno. "
                        + "E' la differenza fra un assieme e un mucchio ordinato.",
                    Descrizione =
                        "Mette un accoppiamento fra due componenti dell'assieme aperto.\n\n"
                        + "LE FACCE SI NOMINANO PER VERSO, non per identificativo: «la "
                        + "faccia in basso di C2 sulla faccia in alto di C1». I componenti "
                        + "sono quelli che «guarda_il_pezzo» elenca come C1, C2, ... e quegli "
                        + "identificativi sono STABILI (al contrario di quelli delle facce di "
                        + "una parte, che si rinumerano a ogni lettura).\n\nI versi sono "
                        + "quelli dell'ASSIEME, non del pezzo: se un componente e' ruotato, "
                        + "la sua faccia «in alto» e' quella che guarda in alto **nel "
                        + "montaggio**. Se non trovo nessuna faccia piana in quel verso te lo "
                        + "dico invece di prenderne una a caso.\n\nFra piu' facce nello stesso "
                        + "verso prendo LA PIU' GRANDE: su un coperchio o una piastra e' "
                        + "quella che vuoi.\n\nQUELLO CHE NON TI SO DIRE, e te lo dico: **di "
                        + "quanto i componenti restino ancora liberi**. SOLIDWORKS non mi da' "
                        + "i gradi di liberta' residui da qui, e un accoppiamento solo ne "
                        + "lascia liberi cinque su sei: per vincolare davvero un pezzo ne "
                        + "servono di solito tre. Ti dico quanti ne ha adesso, non quanto "
                        + "basti.\n\nNon conto sulla risposta: conto gli accoppiamenti dei due "
                        + "componenti prima e dopo, e se non sono aumentati te lo dico invece "
                        + "di dichiarare fatto.",
                    Precondizione = "un assieme aperto con almeno due componenti",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "componente_a", O(
                                "type", "string",
                                "description", "l'identificativo del primo componente, da "
                                    + "«guarda_il_pezzo»: «C1», «C2», ..."),
                            "faccia_a", O(
                                "type", "string",
                                "description", "quale sua faccia, per verso: alto, basso, destra, "
                                    + "sinistra, davanti, dietro. Versi dell'ASSIEME."),
                            "componente_b", O(
                                "type", "string",
                                "description", "l'identificativo del secondo componente."),
                            "faccia_b", O(
                                "type", "string",
                                "description", "quale sua faccia, per verso."),
                            "tipo", O(
                                "type", "string",
                                "description", "coincidente (predefinito), concentrico, parallelo, "
                                    + "perpendicolare, distanza, tangente."),
                            "distanza_mm", O(
                                "type", "number",
                                "description", "solo per «distanza»: quanto stanno lontane. Senza, "
                                    + "mi rifiuto -- non c'e' nessun valore predefinito ragionevole."),
                            "allineamento", O(
                                "type", "string",
                                "description", "«allineato» (predefinito) o «opposto». Se SOLIDWORKS "
                                    + "rifiuta per allineamento, prova l'altro: due facce che si "
                                    + "guardano vogliono «opposto».")),
                        "required", A("componente_a", "faccia_a", "componente_b", "faccia_b")),
                },
                new OperazioneDisegno
                {
                    Nome = "specchia_componente",
                    Famiglia = "assiemi",
                    DocumentoRichiesto = "assieme",
                    Provata = true,
                    Esempio = "{\"componente\":\"C2\",\"piano\":\"destro\",\"modo\":\"istanza\"}",   // da `la_staffa_destra_e_la_sinistra`
                    Sommario = "Specchia un componente su un piano dell'assieme: la staffa destra e la sinistra.",
                    Descrizione =
                        "Specchia un componente su un piano dell'assieme (frontale, superiore, destro). "
                        + "DEVI SCEGLIERE COME, perche' sono tre pezzi diversi:\n- «istanza»: lo stesso "
                        + "file, specchiato. Giusto per un pezzo simmetrico.\n- «opposto_legato»: un file "
                        + "nuovo che SEGUE l'originale. Cambi la destra e cambia anche la sinistra.\n- "
                        + "«opposto_slegato»: un file nuovo INDIPENDENTE. Correggi la destra e la sinistra "
                        + "resta com'era, senza che niente te lo dica.\n\nDopo lo verifico: il centro del "
                        + "pezzo nuovo contro lo specchio del centro dell'originale, e per gli opposti "
                        + "l'albero del file nuovo (legato o no). Il file nuovo prende il nome "
                        + "dell'originale col «suffisso» (predefinito «_spec»), nella stessa cartella."
                        + "\n\nMI RIFIUTO se non mi dici «modo» (ti spiego i tre), se il piano non e' uno "
                        + "dei tre dell'assieme o se il componente non c'e' (ti dico quali ci sono).",
                    Precondizione = "un assieme aperto con almeno un componente",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "componente", O(
                                "type", "string",
                                "description", "l'identificativo del componente, dallo stato: «C1», «C2», ..."),
                            "piano", O(
                                "type", "string",
                                "enum", A("frontale", "superiore", "destro"),
                                "description", "il piano dell'assieme su cui specchiare."),
                            "modo", O(
                                "type", "string",
                                "enum", A("istanza", "opposto_legato", "opposto_slegato"),
                                "description", "istanza (stesso file), opposto_legato (file nuovo che segue "
                                    + "l'originale), opposto_slegato (file nuovo indipendente)."),
                            "suffisso", O(
                                "type", "string",
                                "description", "per gli opposti: cosa aggiungere al nome del file. Predefinito «_spec».")),
                        "required", A("componente", "piano", "modo")),
                },
                new OperazioneDisegno
                {
                    Nome = "fissa_componente",
                    Famiglia = "assiemi",
                    DocumentoRichiesto = "assieme",
                    Provata = true,
                    Esempio = "{\"componente\":\"C2\",\"fisso\":true}",   // da `fisso_non_e_accoppiato`
                    Sommario = "Fissa o libera un componente dell'assieme, e lo rilegge.",
                    Descrizione =
                        "Fissa un componente (non si muove piu', qualunque accoppiamento abbia) o "
                        + "lo libera (lo tengono solo i suoi accoppiamenti). E' l'unico stato di "
                        + "un componente che cambia come l'assieme si ricostruisce.\n\nFISSO NON "
                        + "VUOL DIRE ACCOPPIATO: il primo componente inserito SOLIDWORKS lo fissa "
                        + "da solo, e un assieme che sta fermo solo per quello non e' montato. "
                        + "Dopo averlo cambiato lo rileggo, e ti dico quanti accoppiamenti ha: un "
                        + "componente libero con zero accoppiamenti e' appoggiato, si muove."
                        + "\n\nMI RIFIUTO se non mi dici «fisso» (true o false: sono due cose "
                        + "opposte, non c'e' un verso d'ufficio) o se il componente non c'e' "
                        + "(ti dico quali ci sono).",
                    Precondizione = "un assieme aperto con almeno un componente",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "componente", O(
                                "type", "string",
                                "description", "l'identificativo del componente, dallo stato: «C1», «C2», ..."),
                            "fisso", O(
                                "type", "boolean",
                                "description", "true per fissarlo, false per liberarlo.")),
                        "required", A("componente", "fisso")),
                },
                new OperazioneDisegno
                {
                    Nome = "sostituisci_componente",
                    Famiglia = "assiemi",
                    DocumentoRichiesto = "assieme",
                    Provata = true,
                    Sommario =
                        "Cambia il file di un componente gia' montato, e ti dice se i "
                        + "vincoli sono sopravvissuti SANI o solo sopravvissuti.",
                    Descrizione =
                        "«Il coperchio va rifatto»: cambia il file di un componente gia' "
                        + "montato.\n\nGUARDA SEMPRE COSA TI RISPONDO SUGLI ACCOPPIAMENTI. "
                        + "Misurato: i vincoli del pezzo vecchio **restano** -- non si "
                        + "perdono -- ma restare non vuol dire funzionare. Un vincolo che "
                        + "puntava a una faccia del pezzo vecchio sopravvive nell'albero e "
                        + "puo' andare in ERRORE, e allora il conteggio lo vede ancora e "
                        + "sembra tutto a posto.\n\n**Un vincolo rotto e' peggio di un "
                        + "vincolo assente**: assente si vede, rotto sembra che tenga. "
                        + "Percio' ti dico due cose, non una: quanti accoppiamenti ci sono "
                        + "prima e dopo, E quanti problemi ha il documento prima e dopo. Se i "
                        + "problemi sono aumentati te lo grido, e la cosa da fare e' togliere "
                        + "quei vincoli e rimetterli con «accoppia».\n\nIL FILE NUOVO "
                        + "DEV'ESSERE GIA' APERTO in SOLIDWORKS, come per "
                        + "«inserisci_componente»: copia il percorso da «documenti_aperti».\n\n"
                        + "Se gli dai lo stesso file che c'e' gia' mi rifiuto: riuscirebbe e "
                        + "non cambierebbe niente, e tu crederesti di aver cambiato il "
                        + "pezzo.\n\nLa configurazione la scelgo per NOME corrispondente: non "
                        + "apro finestre per farti scegliere, perche' una finestra aperta da "
                        + "qui non la chiuderebbe nessuno.",
                    Precondizione = "un assieme aperto, e il file nuovo gia' aperto",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "componente", O(
                                "type", "string",
                                "description", "l'identificativo del componente da sostituire, da "
                                    + "«guarda_il_pezzo»: «C1», «C2», ..."),
                            "percorso", O(
                                "type", "string",
                                "description", "percorso completo del file NUOVO, gia' aperto in "
                                    + "SOLIDWORKS."),
                            "configurazione", O(
                                "type", "string",
                                "description", "quale configurazione del pezzo nuovo. Vuota = quella "
                                    + "col nome corrispondente."),
                            "tutte_le_istanze", O(
                                "type", "boolean",
                                "description", "vero (predefinito): se lo stesso pezzo e' montato piu' "
                                    + "volte, le sostituisce tutte. Falso: solo quella.")),
                        "required", A("componente", "percorso")),
                },
                new OperazioneDisegno
                {
                    Nome = "inserisci_componente",
                    Famiglia = "assiemi",
                    DocumentoRichiesto = "assieme",
                    Provata = true,
                    Sommario =
                        "Mette un pezzo gia' aperto dentro l'assieme aperto, e ti dice "
                        + "se e' vincolato o solo appoggiato li'.",
                    Descrizione =
                        "Inserisce un componente in un assieme.\n\nQUELLO CHE DEVI SAPERE "
                        + "PRIMA DI TUTTO IL RESTO: inserire NON e' accoppiare. Il pezzo "
                        + "appare, l'albero si riempie, la ricostruzione e' pulita -- e il "
                        + "componente e' appoggiato li', libero. Quattro componenti liberi "
                        + "sembrano un assieme, si aprono, si ruotano, si esportano, e si "
                        + "montano sbagliati.\n\nPercio' ti rispondo con «fisso», "
                        + "«accoppiamenti» e «appoggiato». Non fisso piu' zero "
                        + "accoppiamenti vuol dire APPOGGIATO: il pezzo sta li' e basta, e "
                        + "finche' e' cosi' l'assieme non e' montato, e' un mucchio "
                        + "ordinato.\n\nQUELLO CHE NON TI SO DIRE, e te lo dico: **di "
                        + "quanto** un componente sia ancora libero. SOLIDWORKS non mi da' i "
                        + "gradi di liberta' residui da qui -- risponde «non disponibile» su "
                        + "ogni componente, fisso o libero che sia -- e un numero inventato "
                        + "sarebbe peggio di nessun numero. Quindi «appoggiato: false» vuol "
                        + "dire «ha almeno un accoppiamento», NON «non si muove piu'»: un "
                        + "accoppiamento solo ne lascia liberi cinque su sei.\n\nIL "
                        + "COMPONENTE DEV'ESSERE GIA' APERTO in SOLIDWORKS: io lo cerco per "
                        + "percorso, e su un file chiuso l'inserimento risponde «non fatto» "
                        + "senza dire perche'. I percorsi che puoi usare sono quelli in "
                        + "«documenti_aperti» dello stato: copiali da li'.\n\nE «configurazione» "
                        + "va scritta esatta o lasciata vuota: una configurazione che non "
                        + "esiste NON DA' ERRORE, prende quella attiva del componente, e ti "
                        + "ritrovi montato un pezzo di un'altra misura.\n\nATTENZIONE SULLE "
                        + "COORDINATE, perche' i due numeri non tornano e non e' un errore: "
                        + "«x_mm, y_mm, z_mm» posizionano il **centro dell'ingombro**; "
                        + "«origine_mm», che ti rispondo io e che rileggerai nello stato, e' "
                        + "dove e' finita l'**origine** del pezzo. Se il pezzo ha l'origine su "
                        + "una faccia -- cioe' quasi sempre -- i due punti distano meta' "
                        + "ingombro. Chiedi z=30 su un pezzo spesso 4 e l'origine esce a "
                        + "z=30 ma y=-2: nessuno dei due numeri e' sbagliato, sono due cose "
                        + "diverse.\n\nNon conto sulla "
                        + "risposta dell'inserimento: conto i componenti prima e dopo, e se "
                        + "non sono aumentati di uno te lo dico invece di dichiarare fatto.",
                    Precondizione = "un assieme aperto, e il componente da inserire gia' aperto",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "percorso", O(
                                "type", "string",
                                "description", "percorso completo del file da inserire, copiato da "
                                    + "«documenti_aperti». Dev'essere gia' aperto in SOLIDWORKS."),
                            "x_mm", O(
                                "type", "number",
                                "description", "dove va il CENTRO dell'ingombro del componente, in mm. "
                                    + "Predefinito 0. Non e' un accoppiamento: e' solo dove "
                                    + "appoggiarlo. E NON e' dove finisce la sua origine: se il "
                                    + "pezzo ha l'origine su una faccia, l'origine si sposta di "
                                    + "meta' ingombro. Ti rispondo con «origine_mm» per questo."),
                            "y_mm", O(
                                "type", "number",
                                "description", "dove va il CENTRO dell'ingombro, in mm. Predefinito 0."),
                            "z_mm", O(
                                "type", "number",
                                "description", "dove va il CENTRO dell'ingombro, in mm. Predefinito 0."),
                            "configurazione", O(
                                "type", "string",
                                "description", "quale configurazione del componente usare, scritta "
                                    + "ESATTA. Vuota o assente = quella attiva del componente. Un "
                                    + "nome sbagliato non da' errore: prende l'attiva.")),
                        "required", A("percorso")),
                },
                new OperazioneDisegno
                {
                    Nome = "sweep_circolare",
                    Famiglia = "estrusioni",
                    Esempio = "{\"percorso_mm\":[[0,0,0],[100,0,0]],\"diametro_mm\":20}",   // da `tubo_dritto_area_per_lunghezza`
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Un tubo pieno di diametro dato lungo un percorso di punti. Su un "
                        + "percorso dritto controllo che il volume sia area per lunghezza.",
                    Descrizione =
                        "Fa una forma che segue una strada: un bocchettone passacavo, un "
                        + "manico, un tubo piegato. E' la prima operazione del catalogo che "
                        + "fa qualcosa che non e' dritto.\n\nIL PROFILO E' UN NUMERO, non uno "
                        + "schizzo: dai il diametro e basta. Il percorso e' un elenco di "
                        + "punti nello spazio, e lo disegno io: non ti chiedo uno schizzo "
                        + "perche' nello stato del documento gli schizzi non hanno un "
                        + "identificativo, e non avresti modo di indicarmelo.\n\nSU UN "
                        + "PERCORSO DRITTO IL CONTO SI FA A MANO, e lo faccio: il volume "
                        + "dev'essere l'area del cerchio per la lunghezza, e se non lo e' mi "
                        + "rifiuto. Su un percorso con dei gomiti no -- nei gomiti il "
                        + "materiale si sovrappone e il volume vero e' minore -- e allora ti "
                        + "do il teorico dicendoti che e' un limite superiore, non la "
                        + "risposta.\n\nUNA CURVA PIU' STRETTA DEL RAGGIO DEL TUBO non si "
                        + "puo' fare: la superficie si attraverserebbe. SOLIDWORKS in quel "
                        + "caso non crea niente e non dice perche'; io te lo dico.",
                    Precondizione = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "percorso_mm", O(
                                "type", "array",
                                "items", O("type", "array", "items", O("type", "number")),
                                "description", "i punti della strada, almeno due, ognuno [x, y, z] in mm. "
                                    + "Per esempio [[0,0,0], [100,0,0]] fa un tubo dritto lungo 100."),
                            "diametro_mm", O(
                                "type", "number",
                                "description", "il diametro del tubo, in mm.")),
                        "required", A("percorso_mm", "diametro_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "loft_fra_due_cerchi",
                    Famiglia = "estrusioni",
                    Esempio = "{\"piano\":\"frontale\",\"diametro1_mm\":40,\"diametro2_mm\":20,\"distanza_mm\":50}",   // da `il_loft_non_e_un_cono`
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Una transizione fra due cerchi di diametro diverso, e ti dico di "
                        + "quanto si scosta dal tronco di cono.",
                    Descrizione =
                        "Raccorda due sezioni tonde diverse: un riduttore, un imbocco, una "
                        + "transizione da un tubo grosso a uno sottile.\n\nUN LOFT FRA DUE "
                        + "CERCHI NON E' UN CONO, ed e' la cosa che vale la pena sapere. La "
                        + "sua superficie e' un'approssimazione a spline della conica: il "
                        + "volume si scosta dalla formula del tronco di cono di qualche "
                        + "millesimo di per cento, sempre in difetto. Ti do tutti e due i "
                        + "numeri e lo scarto, cosi' non insegui un difetto che non c'e'. Se "
                        + "ti serve il cono esatto, «rivoluzione» su un trapezio lo fa."
                        + "\n\nI due cerchi stanno su piani paralleli a quello che scegli, "
                        + "alla distanza che dici. Puoi sfalsarli uno rispetto all'altro per "
                        + "fare una transizione obliqua.",
                    Precondizione = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "piano", O(
                                "type", "string",
                                "enum", A("frontale", "superiore", "destro"),
                                "description", "il piano del primo cerchio. Predefinito «frontale»."),
                            "diametro1_mm", O(
                                "type", "number",
                                "description", "il diametro del primo cerchio, sul piano scelto."),
                            "diametro2_mm", O(
                                "type", "number",
                                "description", "il diametro del secondo."),
                            "distanza_mm", O(
                                "type", "number",
                                "description", "quanto dista il secondo cerchio dal primo. Negativo per "
                                    + "andare dall'altra parte."),
                            "sfalsa1_mm", O(
                                "type", "number",
                                "description", "di quanto spostare di lato il primo cerchio. Predefinito 0."),
                            "sfalsa2_mm", O(
                                "type", "number",
                                "description", "di quanto spostare di lato il secondo: serve per una "
                                    + "transizione obliqua. Predefinito 0.")),
                        "required", A("diametro1_mm", "diametro2_mm", "distanza_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "immagine_nello_schizzo",
                    Famiglia = "riferimenti",
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario = "Mette una foto o uno schizzo a mano, in scala, su un piano: da ricalcare.",
                    Descrizione =
                        "Mette un'immagine (una foto del pezzo, uno schizzo con le quote a penna) in uno "
                        + "schizzo su un piano, IN SCALA: tu mi dici quanto e' larga davvero tutta "
                        + "l'immagine («larghezza_mm» — una misura nota divisa per i suoi pixel, per la "
                        + "larghezza dell'immagine) e dove va l'angolo in basso a sinistra. Poi ricalchi "
                        + "sullo stesso piano con le operazioni di schizzo.\n\nLA SCALA LA VERIFICO: il "
                        + "primo e l'ultimo pixel della prima riga, nello schizzo, devono distare quanto "
                        + "dicono i millimetri per pixel, entro l'1%. Ti rispondo la regola per passare "
                        + "da un pixel ai millimetri.\n\nMI RIFIUTO per un PDF (non entra: convertilo in "
                        + "PNG), per un file che non c'e' o non e' un'immagine, e per una larghezza non "
                        + "positiva.",
                    Precondizione = "un pezzo aperto (anche vuoto)",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "percorso", O(
                                "type", "string",
                                "description", "il file dell'immagine: PNG, JPG, BMP, GIF o TIF."),
                            "piano", O(
                                "type", "string",
                                "enum", A("frontale", "superiore", "destro"),
                                "description", "il piano su cui metterla."),
                            "larghezza_mm", O(
                                "type", "number",
                                "description", "quanto e' larga davvero TUTTA l'immagine, in mm: la scala."),
                            "x_mm", O("type", "number", "description", "dove va l'angolo in basso a sinistra, x. Predefinito 0."),
                            "y_mm", O("type", "number", "description", "dove va l'angolo in basso a sinistra, y. Predefinito 0.")),
                        "required", A("percorso", "piano", "larghezza_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "piano_per_tre_punti",
                    Famiglia = "riferimenti",
                    Esempio = "{\"punto1_mm\":[0,0,0],\"punto2_mm\":[50,0,0],\"punto3_mm\":[100,10,0]}",   // da `lim_tre_punti_in_fila`
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Un piano obliquo, dato per tre punti, e rileggo la sua normale per "
                        + "dirti che e' quello.",
                    Descrizione =
                        "«crea_piano» sa fare solo il piano SFALSATO da uno dei tre standard: "
                        + "serve a tutto quello che e' allineato agli assi e a niente altro. "
                        + "Per un foro inclinato, una faccia obliqua o una staffa a 30 gradi "
                        + "non c'era nessun piano su cui disegnare.\n\nI PUNTI SONO "
                        + "COORDINATE, non vertici del pezzo: tre terne di numeri in "
                        + "millimetri, nel sistema del modello. Non ti chiedo di indicarmi dei "
                        + "vertici perche' nello stato del documento i vertici non hanno un "
                        + "identificativo -- facce e spigoli si', i vertici no.\n\nCONTROLLO "
                        + "CHE IL PIANO SIA QUELLO. Dai tre punti calcolo la normale, poi "
                        + "rileggo quella del piano appena nato e le confronto: se non "
                        + "coincidono mi rifiuto. «Una feature in piu' nell'albero» lo direbbe "
                        + "anche di un piano storto, e su un piano storto ci disegneresti "
                        + "sopra senza accorgertene.\n\nTRE PUNTI IN FILA NON FANNO UN PIANO, "
                        + "e te lo dico prima di provare: per una retta ne passano infiniti.",
                    Precondizione = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "punto1_mm", O(
                                "type", "array",
                                "items", O("type", "number"),
                                "description", "il primo punto, tre numeri in mm: [x, y, z]."),
                            "punto2_mm", O(
                                "type", "array",
                                "items", O("type", "number"),
                                "description", "il secondo punto."),
                            "punto3_mm", O(
                                "type", "array",
                                "items", O("type", "number"),
                                "description", "il terzo punto. Non dev'essere sulla retta degli altri due.")),
                        "required", A("punto1_mm", "punto2_mm", "punto3_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "flangia_base",
                    Famiglia = "lamiera",
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Una lamiera piegata da un profilo aperto: spessore, raggio di piega, profondita'.",
                    Descrizione =
                        "Fa una LAMIERA da un profilo aperto disegnato su un piano base: una L, "
                        + "una U, uno zeta. Ogni angolo del profilo diventa una piega col raggio "
                        + "chiesto.\n\nI PUNTI SONO LE FACCE INTERNE quando il profilo va nel verso "
                        + "in cui lo spessore resta a DESTRA: misurato su una L da 50 + 30 data "
                        + "dall'alto verso destra, lo sviluppo esce 80,712 mm, uguale al conto a "
                        + "mano sulle facce interne. Dato al contrario, lo spessore va dall'altra "
                        + "parte e i punti diventano le facce ESTERNE: la risposta dice sempre "
                        + "quale delle due («i_punti_sono_le_facce»), perche' fra le due passano "
                        + "2 volte lo spessore per piega.\n\nLa risposta porta anche lo SVILUPPO: "
                        + "la lunghezza del pezzo spianato misurata da SOLIDWORKS, accanto a "
                        + "quella calcolata a mano col fattore K riletto dal pezzo. Se non "
                        + "combaciano lo dice, prima che il DXF vada al taglio.\n\nRifiuta un "
                        + "tratto piu' corto delle pieghe che lo mangiano (un lato da 1 mm con un "
                        + "raggio da 2): SOLIDWORKS darebbe una lavorazione rossa senza dire quale "
                        + "lato.",
                    Precondizione = "una parte senza corpi, o a cui aggiungere una lamiera",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "piano", O(
                                "type", "string",
                                "enum", A("frontale", "superiore", "destro"),
                                "description", "il piano base su cui sta il profilo. Predefinito «frontale»."),
                            "punti", O(
                                "type", "array",
                                "items", O("type", "array", "items", O("type", "number")),
                                "description", "il profilo APERTO, almeno due punti [x, y] in mm sul piano. "
                                    + "Una L: [[0,30],[0,0],[50,0]]."),
                            "spessore_mm", O("type", "number", "description", "lo spessore della lamiera"),
                            "raggio_mm", O("type", "number", "description", "il raggio INTERNO delle pieghe"),
                            "profondita_mm", O("type", "number", "description", "quanto e' profonda la lamiera, perpendicolare al piano")),
                        "required", A("punti", "spessore_mm", "raggio_mm", "profondita_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "flangia_spigolo",
                    Famiglia = "lamiera",
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Una piega nuova su uno spigolo di bordo di una lamiera che c'e' gia'.",
                    Descrizione =
                        "Aggiunge una FLANGIA a 90 gradi su uno spigolo di bordo di una lamiera "
                        + "(fatta con «flangia_base»): e' cosi' che si chiude una scatola, con pieghe "
                        + "su spigoli di direzioni diverse.\n\nLo spigolo e' quello fra la faccia "
                        + "larga della lamiera e una faccia di TESTA, larga quanto lo spessore; la "
                        + "flangia si alza dalla parte verso cui guarda la faccia larga. "
                        + "«lunghezza_mm» e' misurata allo SPIGOLO VIRTUALE ESTERNO, e il materiale "
                        + "resta dentro l'ingombro di prima: il lato su cui pieghi si accorcia di "
                        + "raggio + spessore. Spessore, raggio e fattore K sono quelli della "
                        + "lamiera, riletti.\n\nLa risposta porta il volume aggiunto accanto al "
                        + "conto a mano (lo sviluppo in piu', (L - R - T) + BA - (R + T), per lo "
                        + "spessore e la lunghezza dello spigolo), e la lunghezza riletta dalla "
                        + "flangia.\n\nMI RIFIUTO se la lunghezza non lascia niente di dritto "
                        + "(non oltre raggio + spessore), se lo spigolo non e' dritto o non sta sul "
                        + "bordo della lamiera, e se la flangia nasce senza aggiungere materiale.",
                    Precondizione = "una parte di lamiera",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "spigolo_uid", O(
                                "type", "string",
                                "description", "lo spigolo di bordo su cui piegare, dallo stato del documento (E1, E2...)"),
                            "lunghezza_mm", O(
                                "type", "number",
                                "description", "quanto e' lunga la flangia, misurata allo spigolo virtuale esterno")),
                        "required", A("spigolo_uid", "lunghezza_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "orlo",
                    Famiglia = "lamiera",
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Il bordo di una lamiera ripiegato su se stesso, chiuso o aperto.",
                    Descrizione =
                        "Ripiega su se stesso il bordo di una lamiera (fatta con «flangia_base»): "
                        + "l'orlo che toglie il filo tagliente e irrigidisce il bordo. «chiuso» e' "
                        + "schiacciato sulla lamiera; «aperto» lascia «gioco_mm» fra il bordo e la "
                        + "lamiera. Resta dentro l'ingombro di prima.\n\nLo spigolo e' quello fra la "
                        + "faccia larga e una faccia di testa, larga quanto lo spessore. La risposta "
                        + "porta il volume aggiunto accanto al conto a mano: per l'aperto e' la piega "
                        + "a 180 gradi col raggio interno pari a meta' del gioco, misurata tre volte "
                        + "al millesimo; per il chiuso il conto usa un raggio di 0,05 mm, misurato "
                        + "con spessore 2, e si dice ma non decide.\n\nMI RIFIUTO se l'orlo non "
                        + "lascia niente di dritto, se «aperto» non ha gioco, se lo spigolo non e' "
                        + "dritto o non sta sul bordo, e se l'orlo nasce senza aggiungere materiale.",
                    Precondizione = "una parte di lamiera",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "spigolo_uid", O(
                                "type", "string",
                                "description", "lo spigolo di bordo da ripiegare, dallo stato del documento (E1, E2...)"),
                            "tipo", O(
                                "type", "string",
                                "enum", A("chiuso", "aperto"),
                                "description", "chiuso (schiacciato) o aperto (con gioco). Predefinito «chiuso»."),
                            "lunghezza_mm", O(
                                "type", "number",
                                "description", "quanto e' lungo il bordo ripiegato"),
                            "gioco_mm", O(
                                "type", "number",
                                "description", "solo per «aperto»: lo spazio fra il bordo ripiegato e la lamiera")),
                        "required", A("spigolo_uid", "lunghezza_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "piega_da_schizzo",
                    Famiglia = "lamiera",
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Piega una lamiera lungo una linea tracciata su una sua faccia.",
                    Descrizione =
                        "Piega una lamiera che c'e' gia' lungo una LINEA tracciata su una sua "
                        + "faccia piana, dall'estremo «da_mm» a «a_mm» (punti [x, y, z] in mm sul "
                        + "piano della faccia). La linea attraversa la faccia da bordo a bordo.\n\n"
                        + "La lamiera si piega verso la parte a cui GUARDA la faccia: per piegare "
                        + "dall'altra parte, traccia la linea sull'altra faccia. Il raggio e' quello "
                        + "della lamiera; il lato che resta fermo lo sceglie SOLIDWORKS, e la "
                        + "risposta porta l'ingombro prima e dopo.\n\nUna piega non aggiunge "
                        + "lamiera: volume e sviluppo restano quelli di prima, e se cambiano la "
                        + "risposta lo dice.\n\nMI RIFIUTO se l'angolo non sta fra 0 e 180 gradi "
                        + "esclusi, se la faccia non e' piana, se un estremo della linea non sta "
                        + "sulla faccia, e se la piega nasce senza piegare niente.",
                    Precondizione = "una parte di lamiera",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "faccia_uid", O(
                                "type", "string",
                                "description", "la faccia piana su cui si traccia la linea, dallo stato del documento (F1, F2...)"),
                            "da_mm", O(
                                "type", "array", "items", O("type", "number"),
                                "description", "un estremo della linea di piega, [x, y, z] in mm sulla faccia"),
                            "a_mm", O(
                                "type", "array", "items", O("type", "number"),
                                "description", "l'altro estremo, [x, y, z] in mm sulla faccia"),
                            "angolo_gradi", O(
                                "type", "number",
                                "description", "l'angolo di piega. Predefinito 90.")),
                        "required", A("faccia_uid", "da_mm", "a_mm")),
                },
                new OperazioneDisegno
                {
                    Nome = "sviluppo_dxf",
                    Famiglia = "lamiera",
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Il DXF dello sviluppo di una lamiera, per il taglio.",
                    Descrizione =
                        "Scrive il pezzo di lamiera SPIANATO in un DXF: il disegno che va alla "
                        + "macchina di taglio.\n\nIL PEZZO DEV'ESSERE SALVATO: su un pezzo senza "
                        + "file SOLIDWORKS apre «Salva con nome» e resta ad aspettare che qualcuno "
                        + "clicchi — misurato. Si rifiuta prima di chiamare.\n\nIl DXF si RILEGGE: "
                        + "il suo ingombro deve avere per lato lungo lo sviluppo misurato sul "
                        + "pezzo, se no si rifiuta — un DXF di un'altra cosa si apre uguale. Senza "
                        + "«percorso» va accanto al pezzo, col suo nome.",
                    Precondizione = "una parte di lamiera, salvata",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "percorso", O(
                                "type", "string",
                                "description", "dove scrivere il .dxf. Predefinito: accanto al pezzo, col suo nome.")),
                        "required", A()),
                },
                new OperazioneDisegno
                {
                    Nome = "schema_di_quote",
                    Famiglia = "quote",
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Quota il pezzo nel modello con DimXpert, da tre facce di riferimento.",
                    Descrizione =
                        "Mette sul PEZZO (non su una tavola) le quote che un disegnatore "
                        + "metterebbe: spessori, lati, diametri dei fori e la loro posizione "
                        + "dai riferimenti. E' DimXpert di SOLIDWORKS, dal nucleo, senza "
                        + "componenti aggiuntivi.\n\nI RIFERIMENTI SONO TRE FACCE PIANE, in "
                        + "ordine: la primaria (di solito quella d'appoggio), la secondaria e la "
                        + "terziaria (due fianchi perpendicolari). Da loro partono le quote di "
                        + "posizione. Con meno di tre rifiuta: DimXpert non rifiuterebbe, "
                        + "metterebbe una quota sola.\n\nLa risposta porta i VALORI delle quote "
                        + "(«valori_mm»), letti dalle quote nate: e' il modo di vedere se ha "
                        + "quotato quello che serviva. La risposta di SOLIDWORKS alla chiamata "
                        + "(«risposta_di_auto_dimension_scheme») vale False anche quando ha "
                        + "quotato — misurato: non la si guarda.",
                    Precondizione = "corpo_solido",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "riferimenti_uid", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "TRE facce piane dallo stato del documento: primaria, secondaria, terziaria"),
                            "tolleranza", O(
                                "type", "string",
                                "enum", A("piu_meno", "geometrica"),
                                "description", "piu'/meno sulle quote (predefinito) o tolleranze geometriche")),
                        "required", A("riferimenti_uid")),
                },
                new OperazioneDisegno
                {
                    Nome = "sformo",
                    Famiglia = "lavorazioni",
                    DocumentoRichiesto = "parte",
                    Provata = true,
                    Sommario =
                        "Inclina delle facce di un angolo, rispetto a una faccia che resta "
                        + "ferma. Misura l'angolo sul pezzo, non sulla lavorazione.",
                    Descrizione =
                        "L'angolo di spoglia: le pareti di un pezzo stampato in stampo devono "
                        + "pendere di un grado o due, o il pezzo non esce dalla forma. Serve "
                        + "anche alle fusioni e alle lavorazioni con angolo di spoglia."
                        + "\n\nDUE FACCE DIVERSE, E NON VANNO CONFUSE. La faccia NEUTRA e' "
                        + "quella che resta ferma: e' il piano da cui si misura l'angolo, e "
                        + "va in «faccia_neutra_uid». Le facce da inclinare vanno in "
                        + "«facce_uid». Se le scambi, SOLIDWORKS fa una lavorazione lo "
                        + "stesso, la chiama con lo stesso nome e non da' nessun errore -- "
                        + "ma ha sformato altre facce, di un'altra quantita'. Io me ne "
                        + "accorgo e ti fermo.\n\nCOME ME NE ACCORGO. La lavorazione, "
                        + "richiesta l'angolo, dichiara sempre quello che le hai chiesto -- "
                        + "anche quando non ha inclinato niente. Percio' conto le facce del "
                        + "pezzo che stanno davvero all'angolo giusto rispetto alla neutra, "
                        + "prima e dopo, e se non ne sono comparse abbastanza mi rifiuto."
                        + "\n\nIL VERSO. Con «capovolgi» la parete pende dall'altra parte: il "
                        + "pezzo cresce invece di calare. Ti rispondo quanto materiale si e' "
                        + "mosso e da che parte, cosi' lo vedi senza guardare il pezzo di "
                        + "lato.\n\nSOLO FACCE PIANE: una faccia cilindrica sformata diventa "
                        + "conica e non ha un'inclinazione sola, quindi non la conto.",
                    Precondizione = "corpo_solido_e_faccia",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "faccia_neutra_uid", O(
                                "type", "string",
                                "description", "la faccia che RESTA FERMA, da cui si misura l'angolo (per "
                                    + "esempio «F1»). Di solito e' il fondo o la bocca dello stampo."),
                            "facce_uid", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "le facce da inclinare. Devono toccare la neutra."),
                            "angolo_gradi", O(
                                "type", "number",
                                "description", "di quanto inclinarle, in gradi. Per uno stampo 1-3 gradi; "
                                    + "per una fusione in sabbia anche 5."),
                            "capovolgi", O(
                                "type", "boolean",
                                "description", "vero per far pendere le pareti dall'altra parte -- il pezzo "
                                    + "cresce invece di calare. Predefinito falso.")),
                        "required", A("faccia_neutra_uid", "facce_uid", "angolo_gradi")),
                },
                new OperazioneDisegno
                {
                    Nome = "sopprimi_istanza",
                    Famiglia = "lavorazioni",
                    DocumentoRichiesto = "parte",
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
                    DocumentoRichiesto = "parte",
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
                    Esempio = "{\"nome\":\"NumeroDisegno\",\"valore\":\"DIS-1234\"}",   // da `i_due_cassetti_non_si_vedono`
                    DocumentoRichiesto = "qualunque",
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
                    Esempio = "{\"nome\":\"Sinistra\",\"descrizione\":\"staffa sinistra\"}",   // da `i_due_cassetti_non_si_vedono`
                    DocumentoRichiesto = "parte/assieme",
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
                        + "Riportalo com'e'."
                        + "\n\nMI RIFIUTO SE UNA CONFIGURAZIONE CON QUEL NOME C'E' GIA'. Due "
                        + "configurazioni omonime non sono un doppione innocuo: le lavorazioni "
                        + "soppresse in una e attive nell'altra fanno due pezzi diversi con lo "
                        + "stesso nome, e la distinta ne conta uno.",
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
                    Esempio = "{\"nome\":\"Sinistra\"}",   // da `lim_configurazione_gia_esistente`
                    DocumentoRichiesto = "parte/assieme",
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
                        + "errore."
                        + "\n\nMI RIFIUTO SE LA CONFIGURAZIONE NON C'E', e ti elenco quelle che ci "
                        + "sono. E il nome dev'essere INTERO: Default non basta se si chiama "
                        + "Default<Come lavorata>. Un nome parziale non lo indovino, perche' "
                        + "indovinare vorrebbe dire attivare la configurazione sbagliata in "
                        + "silenzio.",
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

        /// <summary>
        /// L'argomento `ancora` (voce E18), lo stesso per il foro e la tasca. E'
        /// un metodo e non un campo: la lista qui sopra si inizializza prima dei
        /// campi dichiarati dopo di lei, e un campo varrebbe ancora null.
        /// </summary>
        private static Nodo SchemaAncora(string cosa, bool dalContorno = false)
        {
            string punto = dalContorno
                ? "Il punto e' il CENTRO del contorno, e l'ancora vale per UN contorno solo, " +
                  "tondo o rettangolare allineato agli assi della faccia."
                : "x_mm/y_mm restano obbligatori e devono dire lo stesso punto.";
            return O(
                "type", "object",
                "description", "COSA TIENE " + cosa.ToUpperInvariant() + " DOV'E' QUANDO IL PEZZO CAMBIA. Senza, "
                    + "x_mm/y_mm sono coordinate assolute: se la piastra si allunga, " + cosa + " resta "
                    + "dov'era. Per OGNI asse, «x» e «y», tutti e due obbligatori, una di due cose: "
                    + "{\"spigolo_uid\": \"E7\", \"distanza_mm\": 20}, la distanza dal CENTRO a uno "
                    + "spigolo dritto della stessa faccia che corre lungo l'altro asse; oppure "
                    + "{\"centrata_fra\": [\"E3\", \"E5\"]}, a meta' fra due spigoli opposti. " + punto
                    + "\n\nMI RIFIUTO SE manca un asse; se x_mm/y_mm e l'ancora non dicono lo stesso "
                    + "punto; se lo spigolo non e' dritto, non sta sulla faccia, non corre lungo l'altro "
                    + "asse o sta sul bordo di uno smusso o di un raccordo; se la distanza e' meno di "
                    + "meta' della larghezza (il profilo scavalcherebbe lo spigolo); se lo schizzo non "
                    + "risulta completamente definito, o non regge una modifica di prova — e allora il "
                    + "taglio lo tolgo.",
                "properties", O(
                    "x", AsseDellAncora("x"),
                    "y", AsseDellAncora("y")),
                "required", A("x", "y"));
        }

        private static Nodo AsseDellAncora(string asse)
        {
            string altro = asse == "x" ? "y" : "x";
            return O(
                "type", "object",
                "description", "l'ancora lungo " + asse + "_mm: spigolo_uid + distanza_mm, oppure centrata_fra",
                "properties", O(
                    "spigolo_uid", O(
                        "type", "string",
                        "description", "uno spigolo dritto della faccia, dallo stato, che corre lungo " + altro + "_mm"),
                    "distanza_mm", O(
                        "type", "number",
                        "description", "dal CENTRO del profilo allo spigolo, misurata lungo " + asse + "_mm"),
                    "centrata_fra", O(
                        "type", "array",
                        "items", O("type", "string"),
                        "description", "due spigoli opposti della faccia, dallo stato, che corrono lungo "
                            + altro + "_mm: il centro resta a meta' fra loro")));
        }
    }
}
