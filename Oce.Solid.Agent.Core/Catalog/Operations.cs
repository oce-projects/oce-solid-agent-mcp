using System.Collections.Generic;

using static Oce.Solid.Agent.Core.Catalog.Node;

namespace Oce.Solid.Agent.Core.Catalog
{
    /// <summary>
    /// THE CATALOG. What the model can ask for, and how it is written.
    ///
    /// IT LIVES HERE AND NOT IN A HAND-WRITTEN JSON, and not in the server either, for
    /// one reason only: **the layer that DESCRIBES an operation must be
    /// the same one that EXECUTES it**. It is the only arrangement in which
    /// an operation that is described and not executable cannot enter the
    /// catalog without somebody noticing: the check becomes
    /// an assertion inside a project, instead of a comparison between two
    /// repositories that nobody redoes.
    ///
    /// It used to live in `ai_disegno.py`, and whoever cloned this repository COULD NOT
    /// regenerate `catalog.json`: the server, docker and a
    /// disposable container were needed. Now `Oce.Solid.Agent.Catalog.exe` is needed.
    ///
    /// `Tested` does not end up in the JSON and this is intended: it is the promise that
    /// the operation was built and MEASURED inside SOLIDWORKS, and
    /// as long as it is false the operation is not exported — so the model does not
    /// see it. It sits next to the code that executes, not in a distant file.
    /// </summary>
    public static class Operations
    {
        /// <summary>The families, with the line that explains them.</summary>
        public static readonly List<KeyValuePair<string, string>> Families =
            new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(
                    "extrusions",
                    "fare il primo corpo del pezzo"),
                new KeyValuePair<string, string>(
                    "features",
                    "togliere o modificare materiale su un corpo che c'e' gia'"),
                new KeyValuePair<string, string>(
                    "weldments",
                    "carpenteria: barre, telai, tagli fra barre, distinta"),
                new KeyValuePair<string, string>(
                    "references",
                    "piani e assi su cui costruire"),
                // THE FAMILY THAT IS NOT A PART. All the others work
                // inside a part document; this one puts together parts that
                // already exist, and what distinguishes it is not what it does but what
                // it is NOT enough: inserting is not mating, and an assembly of
                // free components looks assembled without being so. The line
                // below tells the model before it asks for the list.
                new KeyValuePair<string, string>(
                    "assemblies",
                    "mettere insieme pezzi che esistono gia'. Inserire NON e' " +
                    "accoppiare: guarda sempre «resting»"),
                new KeyValuePair<string, string>(
                    "physical_properties",
                    "cio' che il pezzo E', non la sua forma"),
                new KeyValuePair<string, string>(
                    "drawing",
                    "mettere il pezzo in tavola e consegnarlo: fogli, viste, quote, PDF"),
                // THE FAMILY THAT DOES NOT DRAW. Up to 0.76.0 no
                // operation of the catalog could answer «how did it turn out?»:
                // the measurements were there but they were private inside the operations, and the
                // model could not call any of them.
                new KeyValuePair<string, string>(
                    "checks",
                    "dire com'e' venuto, senza toccarlo"),
                new KeyValuePair<string, string>(
                    "exports",
                    "far uscire il pezzo da SOLIDWORKS: STL per la stampa 3D, "
                    + "STEP e IGES per chi lo deve lavorare"),
                // WHAT THE PART SAYS ABOUT ITSELF. The drawing number and the
                // revision are not geometry, but they are what reaches the
                // title block and the PDM card: a right part with the wrong
                // revision is a wrong part.
                new KeyValuePair<string, string>(
                    "identity",
                    "quello che il pezzo dice di se': numero disegno, revisione, "
                    + "cliente, e le sue varianti"),
                // SHEET METAL (entry GP18). The family that produces the file that goes
                // to the cutting machine: a silent defect here costs real
                // sheet, and the flat pattern is always compared with the hand calculation.
                new KeyValuePair<string, string>(
                    "sheet_metal",
                    "lamiera piegata e il suo sviluppo in DXF per il taglio"),
                // THE DIMENSIONS IN THE MODEL (entry Z-H3): DimXpert from the core. It is not
                // the drawing: the dimensions are on the part.
                new KeyValuePair<string, string>(
                    "dimensions",
                    "quote e tolleranze messe sul PEZZO (DimXpert), non su una tavola"),
            };

        public static readonly List<OperationSpec> All =
            new List<OperationSpec>
            {
                new OperationSpec
                {
                    Name = "extrude_circle",
                    Family = "extrusions",
                    Example = "{\"plane\":\"front\",\"diameter_mm\":6,\"height_mm\":10}",   // from `thread_on_pin`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Un cilindro pieno centrato sull'origine.",
                    Description =
                        "Crea un cilindro pieno: cerchio su un piano, estruso. E' "
                        + "l'operazione per perni, spine, boccole, dischi."
                        + "\n\nOn a part that already has a body, the new cylinder merges with the "
                        + "bodies it touches (merge and automatic body selection are on, as the guide "
                        + "describes them); it cannot merge with a body it does not touch, and the "
                        + "document state counts the bodies afterwards.",
                    Precondition = "document_without_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "piano di costruzione: front, top o right"),
                            "diameter_mm", O(
                                "type", "number"),
                            "height_mm", O(
                                "type", "number",
                                "description", "lunghezza del cilindro, in mm")),
                        "required", A(
                            "diameter_mm",
                            "height_mm")),
                },
                new OperationSpec
                {
                    Name = "extrude_profile",
                    Family = "extrusions",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Una sagoma qualunque, data per contorni, estrusa: il modo "
                        + "generale di fare il primo corpo.",
                    Description =
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
                        + "quote invece di un elenco di punti."
                        + "\n\nThe result says whether the sketch is locked: true only when every segment "
                        + "of the sketch got its FIXED relation, counted again after the rebuild. It "
                        + "also carries «fixed_relations» (how many were added) and, when the sketch is not "
                        + "locked, «sketch_lock_note» with the reason."
                        + "\n\nOn a part that already has a body, the new shape merges with the bodies "
                        + "it touches (merge and automatic body selection are on, as the guide describes "
                        + "them); it cannot merge with a body it does not touch, and the document state "
                        + "counts the bodies afterwards.",
                    Precondition = "document_without_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "piano di costruzione: front, top o right"),
                            "contours", O(
                                "type", "array",
                                "description", "Il PRIMO contorno e' il bordo esterno, i successivi sono i vuoti "
                                    + "dentro di esso.\n\nOgni contorno puo' avere DUE forme:\n1) una "
                                    + "spezzata chiusa: un elenco di vertici, per esempio "
                                    + "[[0,0],[100,0],[100,60],[0,60]]. L'ultimo vertice si unisce al "
                                    + "primo da solo: NON ripeterlo.\n2) un CERCHIO, e per un foro o un "
                                    + "tondo va usato questo e non una spezzata con tanti lati: "
                                    + "{\"circle\": [x_centro, y_centro], \"diameter_mm\": 20}. Un cerchio "
                                    + "approssimato a segmenti ha il materiale dentro la circonferenza, "
                                    + "e un albero non ci entra.\n\nEsempio, TUBO diametro esterno 60 "
                                    + "spessore 5:\n[{\"circle\": [0, 0], \"diameter_mm\": 60}, {\"circle\": "
                                    + "[0, 0], \"diameter_mm\": 50}]\nEsempio, TUBOLARE quadro 80x80 parete "
                                    + "5:\n[[[-40,-40],[40,-40],[40,40],[-40,40]], "
                                    + "[[-35,-35],[35,-35],[35,35],[-35,35]]]",
                                "items", O(
                                    "description", "una spezzata (elenco di [x_mm, y_mm]) oppure un cerchio "
                                        + "({\"circle\": [x, y], \"diameter_mm\": d})")),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "quanto si estrude, in mm"),
                            "symmetric", O(
                                "type", "boolean",
                                "description", "estrusione meta' da una parte e meta' dall'altra del piano (predefinito: no)")),
                        "required", A(
                            "contours",
                            "thickness_mm")),
                },
                new OperationSpec
                {
                    Name = "extrude_rectangle",
                    Family = "extrusions",
                    Example = "{\"plane\":\"front\",\"length_mm\":200,\"width_mm\":120,\"thickness_mm\":20,\"centered\":true}",   // from `blind_slot`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Una piastra rettangolare piena, il primo corpo del pezzo.",
                    Description =
                        "Crea una piastra rettangolare: disegna un rettangolo su un piano "
                        + "e lo estrude. E' l'operazione per piastre, staffe, basi, flange "
                        + "squadrate."
                        + "\n\n«plane» takes «front», «top» or «right» (the case of "
                        + "the letters does not matter); any other name is refused, it never "
                        + "falls back to the front plane."
                        + "\n\nOn a part that already has a body, the new plate merges with the bodies "
                        + "it touches (merge and automatic body selection are on, as the guide describes "
                        + "them); it cannot merge with a body it does not touch, and the document state "
                        + "counts the bodies afterwards.",
                    Precondition = "document_without_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "piano di costruzione: front, top o right"),
                            "length_mm", O(
                                "type", "number",
                                "description", "primo lato, in mm"),
                            "width_mm", O(
                                "type", "number",
                                "description", "secondo lato, in mm"),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "QUANTO SI ESTRUDE, in mm. Per una piastra e' lo spessore; per una "
                                    + "barra o un profilo e' la LUNGHEZZA. Il rettangolo e' la sezione, "
                                    + "questo e' quanto va in profondita': una barra 80x80 lunga 1000 si "
                                    + "fa con length_mm 80, width_mm 80, thickness_mm 1000."),
                            "centered", O(
                                "type", "boolean",
                                "description", "rettangolo centrato sull'origine (predefinito: si')")),
                        "required", A(
                            "length_mm",
                            "width_mm",
                            "thickness_mm")),
                },
                new OperationSpec
                {
                    Name = "extrude_section",
                    Family = "extrusions",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Una barra da sezione normalizzata (tubolare, tondo, angolare, U, "
                        + "T, piatto, esagono, asola) NON di carpenteria.",
                    Description =
                        "Estrude una sezione normalizzata — tubolare, tondo, tubo, "
                        + "angolare a L, profilato a U o a T, piatto, esagono, asola — e "
                        + "ottiene un CORPO SOLIDO con quella forma.\n\nNON E' L'OPERAZIONE "
                        + "DELLA CARPENTERIA. Per un elemento di telaio usa "
                        + "«structural_member»: un corpo estruso ha la stessa forma di una "
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
                        + "circoscritto)\n\n«length_mm» e' quanto e' lunga la barra: per un "
                        + "tubolare 80x80x5 lungo 1000 sono larghezza 80, altezza 80, parete "
                        + "5, lunghezza 1000.\n\nGLI SPIGOLI SONO VIVI, e sul peso si "
                        + "vede. Un profilo commerciale ha gli angoli raggiati, quindi "
                        + "questa sezione pesa di piu': un tubolare 80x80x5 lungo 1000 "
                        + "viene 1.500.000 mm3 contro i circa 1.443.000 del profilo vero, "
                        + "cioe' il 4% in piu'. Per la forma non cambia niente; se il "
                        + "numero che ti serve e' il PESO, prendi il profilo dalla "
                        + "libreria con «structural_member».\n\nL'ASOLA E' UN POLIGONO: le "
                        + "due estremita' sono 24 lati ciascuna, coi vertici sul cerchio "
                        + "circoscritto, cosi' che l'asola non sia mai piu' stretta del "
                        + "nominale. Esce larga lo 0,2% in piu' (20,043 per 20) e con lo 0,2% "
                        + "di area in piu'. Tondo e tubo sono cerchi veri."
                        + "\n\nThe result says whether the sketch is locked: true only when every segment "
                        + "of the sketch got its FIXED relation, counted again after the rebuild. It "
                        + "also carries «fixed_relations» (how many were added) and, when the sketch is not "
                        + "locked, «sketch_lock_note» with the reason."
                        + "\n\nOn a part that already has a body, the new bar merges with the bodies it "
                        + "touches (merge and automatic body selection are on, as the guide describes "
                        + "them); it cannot merge with a body it does not touch, and the document state "
                        + "counts the bodies afterwards.",
                    Precondition = "document_without_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "piano di costruzione: front, top o right"),
                            "variant", O(
                                "type", "string",
                                "enum", A(
                                    "tubular",
                                    "round",
                                    "tube",
                                    "angle",
                                    "u",
                                    "t",
                                    "flat",
                                    "hexagon",
                                    "slot"),
                                "description", "quale sezione"),
                            "length_mm", O(
                                "type", "number",
                                "description", "quanto e' lunga la barra, in mm"),
                            "width_mm", O(
                                "type", "number"),
                            "height_mm", O(
                                "type", "number"),
                            "wall_mm", O(
                                "type", "number",
                                "description", "spessore della parete, per tubolare e tubo"),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "spessore del profilo, per angolare, U e T"),
                            "diameter_mm", O(
                                "type", "number"),
                            "across_flats_mm", O(
                                "type", "number",
                                "description", "apertura in chiave dell'esagono"),
                            "symmetric", O(
                                "type", "boolean",
                                "description", "meta' per parte del piano (predefinito: no)")),
                        "required", A(
                            "variant",
                            "length_mm")),
                },
                new OperationSpec
                {
                    Name = "revolve",
                    Family = "extrusions",
                    Example = "{\"plane\":\"front\",\"angle_deg\":360,\"axis\":[[0,0],[90,0]],\"contours\":[[[0,0],[0,15],[40,15],[40,10],[70,10],[70,17],[90,17],[90,0]]]}",   // from `drilled_shaft_no_longer_declares_profile`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Un solido a simmetria di rotazione: boccole, distanziali, alberi "
                        + "a gradini, flange, pulegge, volantini.",
                    Description =
                        "Ruota un profilo attorno a un asse e ottiene un corpo a simmetria "
                        + "di rotazione. E' l'operazione per tutto cio' che si fa al tornio "
                        + "quando la forma VARIA lungo l'asse: alberi a gradini, boccole con "
                        + "gole, flange, pulegge. Per un cilindro costante bastano "
                        + "«extrude_circle» o «extrude_section», piu' semplici.\n\nIl profilo "
                        + "si da' con gli stessi CONTORNI di «extrude_profile»: il primo e' "
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
                        + "pezzo che ne esce non e' quello che volevi."
                        + "\n\nThe result says whether the sketch is locked: true only when every segment "
                        + "of the sketch got its FIXED relation, counted again after the rebuild. It "
                        + "also carries «fixed_relations» (how many were added) and, when the sketch is not "
                        + "locked, «sketch_lock_note» with the reason.",
                    Precondition = "document_without_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "piano di costruzione: front, top o right"),
                            "contours", O(
                                "type", "array",
                                "description", "il profilo da ruotare, stessa sintassi di «extrude_profile»: il "
                                    + "primo contorno e' il bordo, i successivi sono vuoti. Ogni "
                                    + "contorno e' una spezzata chiusa di vertici [x_mm, y_mm] (l'ultimo "
                                    + "si chiude da solo, non ripeterlo) oppure {\"circle\": [x, y], "
                                    + "\"diameter_mm\": d}. Sta tutto da un lato dell'asse.",
                                "items", O(
                                    "description", "una spezzata (elenco di [x_mm, y_mm]) oppure un cerchio "
                                        + "({\"circle\": [x, y], \"diameter_mm\": d})")),
                            "axis", O(
                                "type", "array",
                                "description", "la retta di rivoluzione: DUE punti [x_mm, y_mm] nello stesso "
                                    + "sistema dei contorni. Per un pezzo tornito e' quasi sempre l'asse "
                                    + "orizzontale del disegno, per esempio [[0,0],[70,0]]. Non e' un "
                                    + "dettaglio: decide l'ingombro finale quanto i vertici del profilo.",
                                "items", O(
                                    "type", "array",
                                    "items", O(
                                        "type", "number"))),
                            "angle_deg", O(
                                "type", "number",
                                "description", "predefinito 360, un giro completo")),
                        "required", A(
                            "contours",
                            "axis")),
                },
                new OperationSpec
                {
                    Name = "spur_gear",
                    Family = "extrusions",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Una ruota dentata a evolvente, con foro e cava per linguetta.",
                    Description =
                        "Crea una RUOTA DENTATA cilindrica a denti dritti, dal numero di "
                        + "denti e dal modulo. Il profilo del dente e' a evolvente e lo "
                        + "calcolo io: tu dai solo i dati di progetto.\n\nCon bore_mm "
                        + "si ottiene anche il foro centrale.\n\nIL LIMITE: sotto 17 denti "
                        + "(con l'angolo di pressione standard di 20 gradi) rifiuto, perche' "
                        + "servirebbe la correzione di profilo contro l'intaglio del piede e "
                        + "non la so fare. E' un limite mio, non della meccanica: con un "
                        + "angolo di pressione maggiore il minimo scende (25 gradi: 11 denti)."
                        + "\n\nThe result says whether the sketch is locked: true only when every segment "
                        + "of the sketch got its FIXED relation, counted again after the rebuild. It "
                        + "also carries «fixed_relations» (how many were added) and, when the sketch is not "
                        + "locked, «sketch_lock_note» with the reason.",
                    Precondition = "document_without_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "piano di costruzione: front, top o right"),
                            "teeth", O(
                                "type", "integer",
                                "description", "numero di denti (z)"),
                            "module_mm", O(
                                "type", "number",
                                "description", "il modulo m in mm. Diametro primitivo = m x z, esterno = m x (z+2)."),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "larghezza di fascia"),
                            "pressure_angle_deg", O(
                                "type", "number",
                                "description", "predefinito 20, che e' lo standard"),
                            "bore_mm", O(
                                "type", "number",
                                "description", "diametro del foro centrale; omettilo per una ruota piena"),
                            "keyway", O(
                                "type", "boolean",
                                "description", "cava per linguetta nel foro, con larghezza e profondita' "
                                    + "normalizzate (ISO/R 773, DIN 6885-1) ricavate dal diametro del "
                                    + "foro. Non chiedere le quote della cava: le sceglie la norma.")),
                        "required", A(
                            "teeth",
                            "module_mm",
                            "thickness_mm")),
                },
                new OperationSpec
                {
                    Name = "slot",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Un foro allungato: il fissaggio che si registra in montaggio.",
                    Description =
                        "Taglia un'ASOLA su una faccia: un foro allungato, con le "
                        + "estremita' tonde. E' quello che permette di REGISTRARE in opera — "
                        + "una staffa con due asole si allinea, una con due fori tondi o "
                        + "entra o si rifa'.\n\nLA LUNGHEZZA E' DA FUORI A FUORI, come la si "
                        + "misura sul pezzo e come si scrive a disegno; la LARGHEZZA e' il "
                        + "diametro delle estremita', cioe' il diametro della vite piu' il "
                        + "gioco. Per una M10 che deve scorrere 40 mm: larghezza 11, "
                        + "lunghezza 51.\n\n«angle_deg» ruota l'asola nel piano della "
                        + "faccia: 0 e' orizzontale, 90 verticale. Serve quando la "
                        + "registrazione va in una direzione precisa.\n\nNon e' una tasca "
                        + "rettangolare. «rectangular_cut» ci somiglia ma ha gli spigoli "
                        + "vivi, e la vite ci balla dentro invece di scorrere."
                        + "\n\nMI RIFIUTO IN QUATTRO CASI, e li dico prima perche' provarli costa un giro: "
                        + "se l'asola non e' passante e non mi dai la profondita'; se cade tutta "
                        + "fuori dal materiale (ti dico fra quali coordinate sta la faccia); se e' "
                        + "piu' grande della faccia e la coprirebbe tutta; e se lunghezza e "
                        + "larghezza sono uguali -- quella e' una circonferenza, e la lunghezza qui "
                        + "si misura da fuori a fuori.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_uid", O(
                                "type", "string",
                                "description", "una faccia che c'e' gia', presa dallo stato"),
                            "face_ref", O(
                                "type", "object",
                                "description", "la faccia creata da un passo precedente di questo stesso piano. "
                                    + "Metti questo OPPURE face_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- pocket_floor: il fondo della tasca (c'e' solo se la "
                                    + "tasca e' cieca)\n- bottom_face: la faccia sul piano di "
                                    + "schizzo, il fondo dell'estrusione\n- top_face: la faccia "
                                    + "in cima all'estrusione, quella opposta al piano di schizzo",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "pocket_floor",
                                            "bottom_face",
                                            "top_face"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "x_mm", O(
                                "type", "number",
                                "description", "COORDINATE DEL MODELLO in mm, non coordinate della faccia. Prendi "
                                    + "i due assi che restano togliendo quello della normale della "
                                    + "faccia, in ordine X, Y, Z: normale lungo Z -> x_mm e' la X del "
                                    + "modello e y_mm la Y; normale lungo Y -> x_mm e' la X e y_mm la Z; "
                                    + "normale lungo X -> x_mm e' la Y e y_mm la Z. Sono gli stessi "
                                    + "numeri di «point_mm» che leggi nello stato del documento, quindi "
                                    + "puoi partire da quelli. La terza coordinata non serve: e' il "
                                    + "piano della faccia."),
                            "y_mm", O(
                                "type", "number",
                                "description", "vedi x_mm"),
                            "anchor", AnchorSchema("l'asola"),
                            "length_mm", O(
                                "type", "number",
                                "description", "lunghezza TOTALE da fuori a fuori, non la corsa. Dev'essere "
                                    + "maggiore della larghezza."),
                            "width_mm", O(
                                "type", "number",
                                "description", "larghezza dell'asola, cioe' il diametro delle estremita' tonde"),
                            "angle_deg", O(
                                "type", "number",
                                "description", "rotazione nel piano della faccia, predefinito 0. Lo schizzo "
                                    + "nasce quotato (raggio e distanza fra i centri): a 0 e 90 gradi anche "
                                    + "l'orientamento e' fissato; obliqua, l'angolo si quota contro il primo "
                                    + "spigolo dell'«anchor», e senza ancora resta libero (te lo dico)."
                                    + " The angle dimension is read back: it must measure the angle "
                                    + "between the slot and that edge (or its supplement), or I refuse. "
                                    + "If the four tangencies and the equal radii of the slot cannot be "
                                    + "counted, or not all of them were born, the response says so in "
                                    + "«slot_relations»."),
                            "through_all", O(
                                "type", "boolean",
                                "description", "predefinito: si'"),
                            "depth_mm", O(
                                "type", "number",
                                "description", "solo se non passante")),
                        "required", A(
                            "x_mm",
                            "y_mm",
                            "length_mm",
                            "width_mm")),
                },
                new OperationSpec
                {
                    Name = "extrude_on_face",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Fa CRESCERE materiale da una faccia: un rialzo, un labbro, un "
                        + "distanziale, un'aletta.",
                    Description =
                        "Disegna un contorno su una faccia che c'e' gia' e lo estrude in "
                        + "FUORI, aggiungendo materiale al pezzo.\n\nE' L'OPERAZIONE CHE FA "
                        + "CRESCERE UN RIALZO. Le altre su un pezzo esistente tolgono (fori, "
                        + "tagli, asole), svuotano (guscio) o spostano; aggiungono materiale "
                        + "anche «engrave_text» in rilievo, ma solo una scritta, e "
                        + "«combine_bodies», che unisce corpi che ci sono gia'. Senza questa non si "
                        + "possono fare: il labbro di tenuta di un coperchio, i distanziali "
                        + "su cui appoggia qualcosa, un'aletta di fissaggio, una torretta "
                        + "per una vite, un rialzo attorno a un foro.\n\nI CONTORNI SONO NELLE "
                        + "COORDINATE DELLA FACCIA, come per «rectangular_cut»: (0, 0) "
                        + "e' il centro della faccia. Piu' contorni annidati fanno una "
                        + "cornice — quello fuori e quello dentro — ed e' cosi' che si fa un "
                        + "labbro attorno a un bordo invece di un blocco pieno.\n\n«merge» a "
                        + "vero (predefinito) fonde il rialzo con il pezzo. A falso nasce un "
                        + "CORPO SEPARATO: quasi mai quello che si vuole, e l'albero cresce "
                        + "uguale, quindi lo si nota solo contando i corpi."
                        + "\n\nMI RIFIUTO se il rialzo non aggiunge materiale (e' cresciuto DENTRO "
                        + "il pezzo), se con «merge» i corpi non restano quelli, e se chiedi "
                        + "l'«anchor» su piu' di un contorno o su un contorno che non e' ne' tondo "
                        + "ne' un rettangolo allineato: il punto che l'ancora tiene e' il centro, "
                        + "e il centro di un poligono qualunque non lo indovino.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_uid", O(
                                "type", "string",
                                "description", "la faccia da cui far crescere il materiale"),
                            "face_ref", O(
                                "type", "object",
                                "description", "la faccia creata da un passo precedente di questo stesso piano. "
                                    + "Metti questo OPPURE face_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- pocket_floor: il fondo della tasca (c'e' solo se la "
                                    + "tasca e' cieca)\n- bottom_face: la faccia sul piano di "
                                    + "schizzo, il fondo dell'estrusione\n- top_face: la faccia "
                                    + "in cima all'estrusione, quella opposta al piano di schizzo",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "pocket_floor",
                                            "bottom_face",
                                            "top_face"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "contours", O(
                                "type", "array",
                                "description", "la sagoma da estrudere, nelle coordinate della faccia. Ogni "
                                    + "contorno e' {\"vertices\": [[x, y], ...]} — spezzata chiusa, "
                                    + "l'ultimo punto si chiude da solo — oppure {\"circle\": [x, y], "
                                    + "\"diameter_mm\": d}. Due contorni annidati fanno una cornice.",
                                "items", O(
                                    "type", "object")),
                            "height_mm", O(
                                "type", "number",
                                "description", "quanto sporge dalla faccia"),
                            "anchor", AnchorSchema("il rialzo", true),
                            "merge", O(
                                "type", "boolean",
                                "description", "predefinito: si'. A falso fa un corpo separato.")),
                        "required", A(
                            "contours",
                            "height_mm")),
                },
                new OperationSpec
                {
                    Name = "hole_wizard",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Un foro come si fa un foro: creazione guidata, norma ISO, "
                        + "filettato o di passaggio, lamato o svasato, anche piu' fori in "
                        + "una volta.",
                    Description =
                        "IL MODO GIUSTO DI FARE UN FORO: la creazione guidata fori di "
                        + "SolidWorks, norma ISO. Da preferire SEMPRE a «cylindrical_cut» "
                        + "quando il foro alloggia una vite, un perno o una filettatura.\n\nLe "
                        + "misure le prende dalla norma: tu dai la taglia della vite («M8») "
                        + "e il tipo, non i diametri. Un foro filettato M8 esce con il "
                        + "preforo 6.8 e il filetto; uno di passaggio M8 esce 9.\n\nPiu' fori "
                        + "uguali si fanno in UNA SOLA chiamata mettendo piu' «positions»: "
                        + "e' cosi' che si disegna una serie di fori, e in officina si legge "
                        + "come una lavorazione sola."
                        + "\n\nOgni posizione puo' portare un'«anchor» (una quota da uno spigolo, o "
                        + "il centro fra due spigoli, per asse): allora il foro segue il bordo "
                        + "quando il pezzo cambia. I REFUSE if only some positions carry an "
                        + "anchor (all or none: the holes are in one sketch), if a distance is "
                        + "less than the radius of the hole, or if, on a centred axis, the gap "
                        + "between the two edges is narrower than the hole. The radius is the "
                        + "widest cut: the counterbore for «counterbore», the countersink for "
                        + "«countersink». I know it only once the hole is made, and then I remove "
                        + "the hole. If SOLIDWORKS does not declare the diameter, the response "
                        + "says so in «anchor_fit_not_checked».\n\nMI RIFIUTO, e tolgo il foro, anche se "
                        + "ricostruisce in errore o non toglie materiale: per esempio una "
                        + "posizione che sta fuori dalla faccia.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_uid", O(
                                "type", "string",
                                "description", "una faccia che c'e' gia', presa dallo stato"),
                            "face_ref", O(
                                "type", "object",
                                "description", "la faccia creata da un passo precedente di questo stesso piano. "
                                    + "Metti questo OPPURE face_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- pocket_floor: il fondo della tasca (c'e' solo se la "
                                    + "tasca e' cieca)\n- bottom_face: la faccia sul piano di "
                                    + "schizzo, il fondo dell'estrusione\n- top_face: la faccia "
                                    + "in cima all'estrusione, quella opposta al piano di schizzo",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "pocket_floor",
                                            "bottom_face",
                                            "top_face"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "variant", O(
                                "type", "string",
                                "enum", A(
                                    "clearance",
                                    "threaded",
                                    "counterbore",
                                    "countersink"),
                                "description", "clearance = foro liscio per far passare la vite; threaded = "
                                    + "maschiato, la vite ci si avvita; counterbore = con la sede per la "
                                    + "testa cilindrica (ISO 4762); countersink = con la sede conica per la "
                                    + "testa svasata (ISO 10642). Predefinito: clearance."),
                            "size", O(
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
                            "positions", O(
                                "type", "array",
                                "description", "dove vanno i fori, sul piano della faccia. Piu' fori uguali "
                                    + "stanno in una chiamata sola. L'«anchor» si da' a TUTTE le posizioni o a "
                                    + "nessuna: i fori stanno in uno schizzo solo, e un punto senza ancora lo "
                                    + "lascerebbe libero di muoversi. I refuse if a distance is less than "
                                    + "the radius of the hole, or a centred gap is narrower than the hole, "
                                    + "counterbore or countersink included (the ISO table decides it): the "
                                    + "hole is already made, and I remove it.",
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
                                                + "numeri di «point_mm» che leggi nello stato del documento, quindi "
                                                + "puoi partire da quelli. La terza coordinata non serve: e' il "
                                                + "piano della faccia."),
                                        "y_mm", O(
                                            "type", "number",
                                            "description", "vedi x_mm"),
                                        "anchor", AnchorSchema("il foro")),
                                    "required", A(
                                        "x_mm",
                                        "y_mm"))),
                            "through_all", O(
                                "type", "boolean",
                                "description", "predefinito: si'"),
                            "depth_mm", O(
                                "type", "number",
                                "description", "obbligatoria se non passante")),
                        "required", A(
                            "size",
                            "positions")),
                },
                new OperationSpec
                {
                    Name = "shell",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Svuota un corpo pieno lasciando una parete di spessore costante: "
                        + "scatole, carter, coperchi.",
                    Description =
                        "Il guscio: prende un corpo pieno e lo svuota, lasciando una "
                        + "parete di spessore costante. Le facce indicate in «faces_uid» "
                        + "vengono TOLTE, cioe' restano aperte — una faccia sola apre una "
                        + "scatola da un lato, due facce opposte fanno un tubo passante.\n\nLo "
                        + "spessore si toglie verso l'INTERNO: l'ingombro esterno non "
                        + "cambia, come aprendo una scatola. Con «outward» lo spessore "
                        + "si aggiunge da fuori, e serve quando e' l'ingombro INTERNO la "
                        + "quota fissa — un vano che deve restare della misura data.\n\nUn "
                        + "guscio senza nessuna faccia tolta e' un corpo cavo chiuso: "
                        + "legittimo ma raro, e quasi sempre il segno che manca una faccia "
                        + "nella richiesta. Per questo va confermato con «fully_closed».",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "faces_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "le facce da TOGLIERE (restano aperte), prese dallo stato del documento"),
                            "faces_ref", O(
                                "type", "array",
                                "description", "le facce da TOGLIERE, quando le crea un passo PRECEDENTE di questo "
                                    + "stesso piano e quindi non hanno ancora un identificativo. Metti "
                                    + "questo OPPURE «faces_uid», non tutti e due.\n\nUn elemento per "
                                    + "ogni faccia da aprire: «fai un blocco e poi svuotalo dall'alto» "
                                    + "e' un piano solo.\n\nGeneri ammessi:\n- top_face: la "
                                    + "faccia in cima all'estrusione, quella opposta al piano di "
                                    + "schizzo\n- bottom_face: la faccia sul piano di schizzo\n"
                                    + "- pocket_floor: il fondo di una tasca cieca\n"
                                    + "- hole_face: la parete cilindrica di un foro",
                                "items", O(
                                    "type", "object",
                                    "properties", O(
                                        "step", O(
                                            "type", "integer",
                                            "description", "il numero d'ordine del passo che crea la geometria, "
                                                + "contando da 1. Deve essere un passo PRECEDENTE a questo."),
                                        "kind", O(
                                            "type", "string",
                                            "enum", A(
                                                "top_face", "bottom_face",
                                                "pocket_floor", "hole_face"))),
                                    "required", A("step", "kind"))),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "spessore della parete che resta"),
                            "outward", O(
                                "type", "boolean",
                                "description", "predefinito no: lo spessore si toglie verso l'interno e "
                                    + "l'ingombro esterno non cambia"),
                            "fully_closed", O(
                                "type", "boolean",
                                "description", "conferma esplicita per un guscio senza nessuna faccia tolta. Di "
                                    + "solito manca una faccia: usalo solo se e' davvero questo che "
                                    + "serve.")),
                        "required", A(
                            "thickness_mm")),
                },
                new OperationSpec
                {
                    Name = "edit_extrusion",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Example = "{\"feature_uid\":\"Cut-Extrude1\",\"through_all\":true}",   // from `blind_becomes_through`
                    Summary = "Cambia un'estrusione o un taglio gia' fatti: cieco -> passante, o un'altra profondita'.",
                    Description =
                        "Cambia la definizione di un'estrusione o di un taglio estruso che c'e' gia': un "
                        + "foro cieco che deve diventare passante («through_all»: true), o una profondita' "
                        + "diversa («depth_mm»). Non si rifa' il pezzo: la lavorazione cambia al suo "
                        + "posto nell'albero, e quelle a valle la seguono.\n\nDopo rileggo la definizione "
                        + "(condizione e profondita'), il volume prima e dopo e le lavorazioni in errore: se "
                        + "la modifica ne rompe una a valle, rimetto com'era e mi rifiuto. Se il volume non "
                        + "cambia te lo dico (un cieco piu' profondo del materiale e' gia' passante).\n\nMI "
                        + "RIFIUTO se la lavorazione non e' un'estrusione o un taglio estruso (per una quota "
                        + "qualunque c'e' «set_dimension»), se chiedi «through_all» per un'estrusione — anche "
                        + "per un rialzo fatto su una faccia con «extrude_on_face», che nello stato ha tipo "
                        + "«ICE» come i tagli ma e' un'estrusione —, se l'estrusione va in DUE VERSI "
                        + "indipendenti (cambio e rileggo solo il primo, e il secondo resterebbe com'era), e "
                        + "se non mi dai uno solo fra «through_all» e «depth_mm».",
                    Precondition = "un pezzo con un'estrusione o un taglio estruso",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "la lavorazione da cambiare, col nome dallo stato («feature»). The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is."),
                            "through_all", O(
                                "type", "boolean",
                                "description", "true: il taglio attraversa tutto. Oppure «depth_mm»."),
                            "depth_mm", O(
                                "type", "number",
                                "description", "la profondita' nuova, in mm (cieca). Oppure «through_all».")),
                        "required", A("feature_uid")),
                },
                new OperationSpec
                {
                    Name = "reorder_feature",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Example = "{\"feature_uid\":\"Shell1\",\"relative_to_uid\":\"Cut-Extrude1\",\"placement\":\"before\"}",   // from `order_changes_the_part`
                    Summary = "Sposta una lavorazione prima o dopo un'altra nell'albero, e dice come e' cambiato il pezzo.",
                    Description =
                        "Sposta una lavorazione nell'albero, prima o dopo un'altra: uno svuotamento "
                        + "messo prima di un foro da' un pezzo diverso da uno messo dopo (misurato: "
                        + "56485,7 contro 51952,38 mm3 sulla stessa scatola). Ti do l'ordine prima e "
                        + "dopo, il volume e i corpi prima e dopo; se il pezzo non cambia te lo dico."
                        + "\n\nSE LO SPOSTAMENTO ROMPE UNA LAVORAZIONE A VALLE la rimetto dov'era e "
                        + "mi rifiuto.\n\nMI RIFIUTO se una delle due lavorazioni non c'e' o non e' "
                        + "piu' quella dello stato (il nome si rinomina, l'ID no), se «placement» non e' "
                        + "«before» o «after», e se SOLIDWORKS non la sposta: succede quando dipende da "
                        + "una lavorazione che verrebbe dopo (un foro prima dell'estrusione su cui "
                        + "sta).",
                    Precondition = "un pezzo con almeno due lavorazioni",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "la lavorazione da spostare, col nome dallo stato («feature»). The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is."),
                            "relative_to_uid", O(
                                "type", "string",
                                "description", "la lavorazione accanto a cui metterla."),
                            "placement", O(
                                "type", "string",
                                "enum", A("before", "after"),
                                "description", "before o after di «relative_to_uid».")),
                        "required", A("feature_uid", "relative_to_uid", "placement")),
                },
                new OperationSpec
                {
                    Name = "global_variable",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Example = "{\"name\":\"parete\",\"value_mm\":12}",   // from `wall_has_a_name`
                    Summary = "Dai un NOME a un numero (parete = 3 mm): le quote legate cambiano con lui.",
                    Description =
                        "Crea o cambia una variabile globale del pezzo: «parete» = 12 mm, oppure "
                        + "un'espressione («2 * \"parete\" + 1mm»). Le quote legate con "
                        + "«link_dimension» cambiano quando la cambi: e' il modo di fare un pezzo che "
                        + "si modifica senza rompersi, senza ripetere lo stesso numero in dieci "
                        + "posti.\n\nDopo averla scritta la rileggo, e ti dico quali quote guida e "
                        + "il volume prima e dopo.\n\nMI RIFIUTO se il nome non e' fatto di lettere, "
                        + "cifre e «_», se mi dai insieme (o nessuno dei due) «value_mm» ed "
                        + "«expression», e se SOLIDWORKS non accetta il nome: nel SOLIDWORKS in "
                        + "italiano «spessore» e' gia' preso (misurato) — usa per esempio «parete».",
                    Precondition = "un pezzo aperto",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "name", O(
                                "type", "string",
                                "description", "il nome della variabile: lettere, cifre e «_», comincia con una lettera."),
                            "value_mm", O(
                                "type", "number",
                                "description", "il valore, in millimetri. Oppure «expression»."),
                            "expression", O(
                                "type", "string",
                                "description", "un'espressione di SOLIDWORKS, coi nomi fra virgolette doppie: "
                                    + "«2 * \"parete\" + 1mm». Oppure «value_mm».")),
                        "required", A("name")),
                },
                new OperationSpec
                {
                    Name = "link_dimension",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary = "Lega una quota a una variabile o a un conto: la guida l'equazione.",
                    Description =
                        "Lega una quota del pezzo (il suo nome dallo stato, «D1@Sketch1») a "
                        + "un'espressione: una variabile globale («\"parete\"») o un conto "
                        + "(«2 * \"parete\" + 1mm»). Da li' la quota la guida l'equazione, e "
                        + "cambia quando cambia la variabile; «set_dimension» non la cambia piu'."
                        + "\n\nDopo il legame rileggo la quota: deve valere quanto dice "
                        + "l'equazione, e ti do il volume prima e dopo.\n\nMI RIFIUTO se la quota "
                        + "non c'e' (ti dico quali ci sono) o se SOLIDWORKS non accetta "
                        + "l'espressione (una variabile che non esiste: creala prima con "
                        + "«global_variable»).",
                    Precondition = "un pezzo aperto con almeno una quota",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "dimension", O(
                                "type", "string",
                                "description", "il nome della quota, copiato dallo stato del documento. The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is."),
                            "expression", O(
                                "type", "string",
                                "description", "a cosa legarla, coi nomi fra virgolette doppie: «\"parete\"», "
                                    + "«2 * \"parete\" + 1mm».")),
                        "required", A("dimension", "expression")),
                },
                new OperationSpec
                {
                    Name = "set_dimension",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Cambia una quota di una lavorazione che c'e' gia', senza rifare niente.",
                    Description =
                        "Cambia il VALORE di una quota — uno spessore, una profondita', un "
                        + "diametro, un raggio — su una lavorazione che il pezzo ha "
                        + "gia'.\n\nE' LA RICHIESTA PIU' FREQUENTE IN OFFICINA: «quel foro "
                        + "portalo a M10», «lo spessore fallo 3». Prima l'unico modo era "
                        + "cancellare e rifare, cioe' perdere tutto quello che veniva dopo — "
                        + "fori, raccordi, nervature costruite su quella geometria.\n\nIL NOME "
                        + "DELLA QUOTA LO TROVI NELLO STATO DEL DOCUMENTO: ogni lavorazione "
                        + "porta le sue, con nome e valore in mm (per esempio "
                        + "«D1@Shell1@Part3.Part» = 6). Copia il nome cosi' com'e'.\n\nIl "
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
                        + "quale smusso o raccordo.\n\nUNA QUOTA ANGOLARE (lo stato la scrive in "
                        + "«deg») si cambia in GRADI: «value_mm» porta i gradi, e la risposta "
                        + "dice «before_deg» e «after_deg» invece di «before_mm» e «now_mm». "
                        + "A dimension the document state lists with its value null (not 0) could "
                        + "not be read from SOLIDWORKS; set_dimension refuses it before changing "
                        + "anything.\n\n"
                        + "RIFIUTA le quote di sola lettura (non le scavalco, anche se SOLIDWORKS "
                        + "lo permetterebbe), quelle guidate, che misurano e non comandano, e "
                        + "quelle intere, come le copie di una ripetizione.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "dimension", O(
                                "type", "string",
                                "description", "il nome della quota, copiato dallo stato del documento The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is."),
                            "value_mm", O(
                                "type", "number",
                                "description", "il valore nuovo, in millimetri; in gradi se la quota e' "
                                    + "angolare")),
                        "required", A(
                            "dimension",
                            "value_mm")),
                },
                new OperationSpec
                {
                    Name = "dimension_tolerance",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "La tolleranza su una quota del modello (±, bilaterale, accoppiamento ISO), riscontrata e con i decimali giusti.",
                    Description =
                        "Mette la TOLLERANZA su una quota del modello: simmetrica (±valore), bilaterale "
                        + "(scostamenti superiore e inferiore) o un accoppiamento ISO (classe, es. H7 per un "
                        + "foro, g6 per un albero). Sulla quota del modello arriva in tavola da sola con "
                        + "«model_dimensions».\n\nL'ACCOPPIAMENTO SI RISCONTRA: gli scostamenti li calcola "
                        + "SOLIDWORKS, e io controllo che l'ampiezza sia il grado IT della tabella ISO 286-1 per "
                        + "quel diametro (IT5..IT11, fino a 500 mm) e che il segno sia quello della lettera "
                        + "(H da zero in su, h da zero in giu'). Se non torna la rimetto com'era e te lo dico."
                        + "\n\nI DECIMALI: imposto quelli della tolleranza sulla quota. Misurato: senza, un H7 "
                        + "su 10 usciva in tavola «10 H7 +0 / 0» — in officina vuol dire «esatta». The "
                        + "tolerance decimals are set and read back on EVERY displayed copy of the "
                        + "dimension; one that does not take them is a refusal, and the tolerance is put "
                        + "back and read back. precision_status reports a SetPrecision3 answer of 1, 2 "
                        + "or 4 (about values I leave unchanged).\n\nLa "
                        + "risposta porta scostamenti e limiti (10,000 / 10,015 per un Ø10 H7).\n\nMI RIFIUTO "
                        + "per un grado IT fuori da 5..11 o una quota oltre 500 mm (non li so riscontrare), per "
                        + "una bilaterale con l'inferiore sopra il superiore, e quando il riscontro non torna.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "dimension", O(
                                "type", "string",
                                "description", "il nome della quota, copiato dallo stato del documento (es. «D1@Sketch2») The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is."),
                            "variant", O(
                                "type", "string",
                                "enum", A("symmetric", "bilateral", "fit", "none"),
                                "description", "che tolleranza. «none» la toglie."),
                            "value_mm", O("type", "number", "description", "per «symmetric»: il ± (0,05 = ±0,05)."),
                            "upper_mm", O("type", "number", "description", "per «bilateral»: lo scostamento superiore (es. 0,1)."),
                            "lower_mm", O("type", "number", "description", "per «bilateral»: lo scostamento inferiore (es. -0,05)."),
                            "fit_class", O("type", "string", "description", "per «fit»: la classe ISO, maiuscola per un foro (H7), minuscola per un albero (g6).")),
                        "required", A("dimension", "variant")),
                },
                new OperationSpec
                {
                    Name = "suppress_feature",
                    Family = "features",
                    Example = "{\"feature_uid\":\"Cut-Extrude2\"}",   // from `limit_suppress_twice`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "SPEGNE una lavorazione dell'albero, senza cancellarla. Si riaccende.",
                    Description =
                        "Spegne una lavorazione: il pezzo torna com'era senza di lei, ma lei "
                        + "resta nell'albero e si puo' riaccendere con «reactivate».\n\nE' la "
                        + "strada da prendere quando vuoi VEDERE il pezzo senza una "
                        + "lavorazione senza impegnarti. Per toglierla davvero c'e' "
                        + "«delete_feature», che non si disfa.\n\nIL NOME DELLA "
                        + "LAVORAZIONE STA NELLO STATO DEL DOCUMENTO, dentro «feature» (per "
                        + "esempio «Cut-Extrude2»). Copialo cosi' com'e'.\n\nI FIGLI SI "
                        + "SPENGONO CON LEI, ed e' quasi sempre cio' che si vuole: sopprimere "
                        + "un foro spegne anche i suoi raccordi. La risposta dice quali sono "
                        + "stati.\n\nDOPO, GLI IDENTIFICATIVI NON VALGONO PIU'. Sopprimere fa "
                        + "sparire delle facce, e gli identificativi (F1, E4) si rinumerano "
                        + "per area a ogni lettura: rileggi il pezzo prima del passo seguente."
                        + "\n\nMI RIFIUTO SE LA LAVORAZIONE E' GIA' SOPPRESSA, e ti dico di usare "
                        + "l'operazione che la riattiva. Sopprimere due volte non fa niente e non "
                        + "da' errore: e' il modo piu' facile di credere di aver cambiato il pezzo "
                        + "senza averlo toccato.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "il nome della lavorazione, copiato dallo stato del documento The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is."),
                            "reactivate", O(
                                "type", "boolean",
                                "description", "a vero RIACCENDE una lavorazione soppressa, invece di spegnerla. "
                                    + "Predefinito: falso.")),
                        "required", A(
                            "feature_uid")),
                },
                new OperationSpec
                {
                    Name = "delete_feature",
                    Family = "features",
                    Example = "{\"feature_uid\":\"Cut-Extrude1\"}",   // from `delete_first_hole`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "CANCELLA una lavorazione dall'albero. Non si disfa con un'altra operazione.",
                    Description =
                        "Toglie una lavorazione dall'albero per sempre. Lo schizzo che aveva "
                        + "assorbito se ne va con lei, cosi' nell'albero non resta niente di "
                        + "orfano.\n\nNON SI DISFA con un'altra operazione: l'unica strada "
                        + "indietro e' lo strumento «undo», e solo se non hai fatto "
                        + "altro dopo. Se vuoi solo vedere il pezzo senza quella lavorazione, "
                        + "usa «suppress_feature», che si riaccende.\n\nSE QUALCOSA "
                        + "DIPENDE DA LEI, MI RIFIUTO e ti dico cosa. Cancellare una "
                        + "lavorazione da cui ne dipendono altre le farebbe sparire tutte, e "
                        + "SOLIDWORKS lo fa senza chiedere: se e' proprio lei che vuoi "
                        + "togliere, togli prima cio' che ci sta sopra.\n\nIL NOME DELLA "
                        + "LAVORAZIONE STA NELLO STATO DEL DOCUMENTO, dentro «feature». "
                        + "Copialo cosi' com'e'.\n\nDOPO, GLI IDENTIFICATIVI NON VALGONO "
                        + "PIU': rileggi il pezzo prima del passo seguente.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "il nome della lavorazione, copiato dallo stato del documento The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is.")),
                        "required", A(
                            "feature_uid")),
                },
                new OperationSpec
                {
                    Name = "fillet_edges",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Raccordo a raggio costante su uno o piu' spigoli.",
                    Description =
                        "Arrotonda uno o piu' spigoli con lo stesso raggio. Gli spigoli si "
                        + "indicano con gli identificativi presi dallo stato del documento."
                        + "\n\nSe fra la lettura dello stato e adesso gli spigoli hanno cambiato "
                        + "numero (un foro fatto prima li rinumera), li ritrovo per firma: stessa "
                        + "lunghezza nello stesso punto, e lo dico nella risposta. MI RIFIUTO se "
                        + "lo spigolo che avevi visto non c'e' piu' com'era — accorciato, spezzato "
                        + "— invece di prendere il piu' vicino, e se il raggio non ci sta in meta' "
                        + "dello spigolo."
                        + "\n\nThe fillet follows tangent edges: one selected edge can round a whole "
                        + "loop (a rounded block: 1 edge selected, 8 faces made). The document state "
                        + "says so for every fillet: «selected_edges» is how many edges were selected, "
                        + "«faces» how many faces the fillet made. The answer carries «selected_edges» "
                        + "and «faces» too, read the same way."
                        + "\n\nI refuse when the volume of the part does not change after the fillet is "
                        + "made: the feature is in the tree and worked nothing, and I say it is still "
                        + "there to undo. On a concave edge the volume grows, and that is fine. "
                        + "material_change_mm3 gives the change (after minus before); volume_not_read "
                        + "says when the volume could not be read.",
                    Precondition = "solid_body_and_edge",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "edges_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "identificativi di spigoli che ci sono GIA', presi dallo stato del documento"),
                            "edges_ref", O(
                                "type", "object",
                                "description", "gli spigoli creati da un passo precedente di questo stesso piano "
                                    + "— per esempio «arrotonda i quattro angoli della piastra che fai "
                                    + "al passo 1». Quanti spigoli siano si sa solo dentro SolidWorks. "
                                    + "Metti questo OPPURE edges_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- bottom_edges: gli spigoli del bordo sul fondo "
                                    + "dell'estrusione\n- side_edges: gli spigoli lungo la "
                                    + "direzione di estrusione, i verticali\n- top_edges: gli "
                                    + "spigoli del bordo in cima all'estrusione\n- all_edges: "
                                    + "tutti gli spigoli creati da quel passo",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "bottom_edges",
                                            "side_edges",
                                            "top_edges",
                                            "all_edges"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "radius_mm", O(
                                "type", "number")),
                        "required", A(
                            "radius_mm")),
                },
                new OperationSpec
                {
                    Name = "circular_pattern",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Ripete una lavorazione in circolo attorno a un asse o a uno spigolo dritto.",
                    Description =
                        "Ripete una lavorazione gia' fatta, in circolo attorno a un asse. "
                        + "La lavorazione e l'asse si indicano con gli identificativi presi "
                        + "dallo stato del documento.\n\nL'ASSE PUO' ESSERE UNO SPIGOLO DRITTO "
                        + "del pezzo — quelli che lo stato elenca come «E1», «E2» — oppure "
                        + "un asse di riferimento gia' nel documento.\n\nPer ripetere attorno "
                        + "a un FORO serve un asse che passi per il suo centro: il bordo del "
                        + "foro e' un cerchio, non una retta, e come direzione viene "
                        + "rifiutato. Se quell'asse non c'e' ancora, fallo prima con "
                        + "«create_axis».",
                    Precondition = "feature_and_direction",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "cosa ripetere, se c'e' gia' nell'albero"),
                            "feature_ref", O(
                                "type", "object",
                                "description", "la lavorazione fatta da un passo precedente di questo stesso "
                                    + "piano — «fai un foro e poi ripetilo 6 volte». Metti questo OPPURE "
                                    + "feature_uid, non tutti e due.\n\nGeneri ammessi:\n- feature: la "
                                    + "lavorazione fatta da quel passo, per ripeterla",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "feature"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "axis_uid", O(
                                "type", "string",
                                "description", "attorno a cosa: un asse gia' nel documento, oppure uno spigolo "
                                    + "DRITTO del pezzo («E1», «E2»...) preso dallo stato"),
                            "axis_ref", O(
                                "type", "object",
                                "description", "l'asse creato da un passo precedente di questo stesso piano — "
                                    + "«fai l'asse del foro e poi ripeti la lavorazione 6 volte "
                                    + "attorno». Metti questo OPPURE axis_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- axis: l'asse creato dal passo, dal centro di un foro o "
                                    + "di un albero",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "axis"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "instance_count", O(
                                "type", "integer"),
                            "total_angle_deg", O(
                                "type", "number",
                                "description", "predefinito: 360")),
                        "required", A(
                            "instance_count")),
                },
                new OperationSpec
                {
                    Name = "linear_pattern",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Ripete una lavorazione in fila, o in griglia su due direzioni: "
                        + "otto fori in fila si fanno cosi'.",
                    Description =
                        "Ripete una lavorazione gia' fatta, in fila lungo un asse — o in "
                        + "griglia, lungo due assi insieme. E' il gemello di "
                        + "«circular_pattern» e si indica allo stesso modo.\n\nOtto fori in "
                        + "fila si fanno COSI', non chiamando otto volte il foro con otto "
                        + "coordinate: il pezzo finito dichiara che quei fori sono una "
                        + "serie, e cambiarne il passo diventa una quota sola.\n\nLA DIREZIONE "
                        + "SI PRENDE DA UNO SPIGOLO DRITTO del pezzo: quelli che lo stato "
                        + "del documento elenca come «E1», «E2»... Per una fila lungo la "
                        + "piastra si indica lo spigolo lungo; per una griglia, due spigoli "
                        + "perpendicolari fra loro.\n\nVa bene anche un asse fatto con "
                        + "«create_axis», ma quello nasce dal centro di un foro ed e' "
                        + "perpendicolare alla faccia forata: serve per ripetere NELLO "
                        + "SPESSORE, quasi mai per una fila.\n\n«axis1_uid» e' obbligatorio. "
                        + "«axis2_uid» serve solo per una griglia — una piastra forata a "
                        + "matrice; per una fila sola omettilo insieme a passo2 e "
                        + "instance_count2.\n\nIl verso lo decide l'asse, non un segno nei "
                        + "parametri: se la ripetizione esce dalla parte sbagliata usa "
                        + "«flip1»."
                        + "\n\nMI RIFIUTO SE LA DIREZIONE ARRIVA DA UNO SPIGOLO CURVO: serve uno "
                        + "spigolo rettilineo. Un arco non ha una direzione sola, e prenderne una "
                        + "a caso vorrebbe dire una ripetizione storta che sembra giusta.",
                    Precondition = "feature_and_direction",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "cosa ripetere, se c'e' gia' nell'albero"),
                            "feature_ref", O(
                                "type", "object",
                                "description", "la lavorazione fatta da un passo precedente di questo piano. "
                                    + "Metti questo OPPURE feature_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- feature: la lavorazione fatta da quel passo, per "
                                    + "ripeterla",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "feature"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "axis1_uid", O(
                                "type", "string",
                                "description", "direzione 1: uno spigolo DRITTO del pezzo («E1», «E2»...) preso "
                                    + "dallo stato, oppure un asse gia' nel documento"),
                            "axis1_ref", O(
                                "type", "object",
                                "description", "la direzione 1, se l'asse lo crea un passo precedente di questo "
                                    + "piano con «create_axis». Uno OPPURE l'altro.\n\nGeneri ammessi:\n- "
                                    + "axis: l'asse creato dal passo, dal centro di un foro o di un "
                                    + "albero",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "axis"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "pitch1_mm", O(
                                "type", "number",
                                "description", "distanza fra le istanze"),
                            "instance_count1", O(
                                "type", "integer",
                                "description", "quante copie in tutto, l'originale compreso"),
                            "flip1", O(
                                "type", "boolean",
                                "description", "inverte il verso"),
                            "axis2_uid", O(
                                "type", "string",
                                "description", "direzione 2, solo per una griglia"),
                            "pitch2_mm", O(
                                "type", "number",
                                "description", "solo con axis2_uid"),
                            "instance_count2", O(
                                "type", "integer",
                                "description", "solo con axis2_uid"),
                            "flip2", O(
                                "type", "boolean",
                                "description", "inverte il verso 2")),
                        "required", A(
                            "pitch1_mm",
                            "instance_count1")),
                },
                new OperationSpec
                {
                    Name = "fill_pattern",
                    Family = "features",
                    RequestedDocument = "part",
                    // true ONLY after the green bench on cases/patterns (the green and the three refusals).
                    Tested = true,
                    Summary =
                        "Riempie una faccia piana di copie di una lavorazione: una piastra forata "
                        + "per alleggerire, una griglia di sfiato.",
                    Description =
                        "Riempie una faccia piana di copie di una lavorazione gia' fatta: una "
                        + "piastra forata per alleggerire, una griglia di sfiato, un crivello. "
                        + "SOLIDWORKS mette la prima copia al CENTRO della faccia e aggiunge giri "
                        + "concentrici distanti «pitch_mm», con «copies_per_turn» copie su ogni giro, "
                        + "finche' la faccia le contiene; quelle che uscirebbero dal contorno (meno "
                        + "«margin_mm») non si fanno. L'originale resta dov'era.\n\nQUANTE COPIE "
                        + "ENTRANO LO DECIDE LA FACCIA, non un numero che dai tu: la risposta le conta "
                        + "in «copies_made». Su una piastra 100 x 60 con un foro Ø8, passo circa 17 "
                        + "e 10 copie per giro, sono 14.\n\nServono tre cose: la lavorazione "
                        + "(«feature_uid» o «feature_ref»), la faccia di contorno su cui sta "
                        + "(«face_uid» o «face_ref») e una DIREZIONE, cioe' uno spigolo dritto "
                        + "che corre lungo la faccia («axis_uid», per esempio il suo bordo lungo) "
                        + "or an axis that lies along that face («axis_ref»). The axis of a hole "
                        + "drilled into the face, as «create_axis» makes it, runs across the face and is "
                        + "refused. The direction orients the loops. Measured with an edge of the "
                        + "boundary face; an axis lying along the face is accepted, but no test has "
                        + "used one yet.\n\nPer copie in fila o in griglia usa «linear_pattern»; per un "
                        + "cerchio di copie attorno a un asse, «circular_pattern». La disposizione e' "
                        + "solo circolare: quadrata, poligonale e a perforazione non sono provate."
                        + "\n\nMI RIFIUTO SE MANCA LA DIREZIONE: senza, SOLIDWORKS non crea niente e "
                        + "non dice perche' (misurato, anche se la guida la dice facoltativa). MI "
                        + "RIFIUTO SE LA FACCIA DI CONTORNO NON E' PIANA, e SE LA DIREZIONE NON STA "
                        + "NEL PIANO DELLA FACCIA (uno spigolo dello spessore). E mi rifiuto, togliendo "
                        + "la ripetizione, se nasce con lavorazioni in errore, con i corpi cambiati o "
                        + "con copie che mordono il bordo o si fondono fra loro.",
                    Precondition = "feature_and_direction",
                    // No Example: the green case uses bench selectors
                    // (@feature, @face, @edge), and an example that the client cannot
                    // copy is a trap (ExamplesTests, entry Z-G1).
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "la lavorazione da ripetere, se c'e' gia' nell'albero"),
                            "feature_ref", O(
                                "type", "object",
                                "description", "la lavorazione fatta da un passo precedente di questo piano. "
                                    + "Metti questo OPPURE feature_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- feature: la lavorazione fatta da quel passo, per "
                                    + "ripeterla",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "feature"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "face_uid", O(
                                "type", "string",
                                "description", "la faccia PIANA dentro cui disporre le copie, presa dallo stato "
                                    + "(«F1»...): di solito quella da cui parte il foro"),
                            "face_ref", O(
                                "type", "object",
                                "description", "la faccia creata da un passo precedente di questo stesso piano. "
                                    + "Metti questo OPPURE face_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- pocket_floor: il fondo della tasca (c'e' solo se la "
                                    + "tasca e' cieca)\n- bottom_face: la faccia sul piano di "
                                    + "schizzo, il fondo dell'estrusione\n- top_face: la faccia "
                                    + "in cima all'estrusione, quella opposta al piano di schizzo",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "pocket_floor",
                                            "bottom_face",
                                            "top_face"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "axis_uid", O(
                                "type", "string",
                                "description", "the direction of the loops: a STRAIGHT edge that runs along the "
                                    + "boundary face («E1», «E2»...), for example its long edge, or an axis "
                                    + "already in the document that lies along the face. Always needed: without "
                                    + "it the pattern is not born"),
                            "axis_ref", O(
                                "type", "object",
                                "description", "the direction, when a previous step of this plan makes the axis "
                                    + "with «create_axis»: only an axis that lies along the boundary face works "
                                    + "(for example the axis of a shaft lying on it); the axis of a hole "
                                    + "drilled into the face runs across it and is refused. One OR the "
                                    + "other.\n\nAllowed kinds:\n- axis: the axis made by that step",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "axis"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "pitch_mm", O(
                                "type", "number",
                                "description", "distanza fra un giro di copie e il successivo, da centro a centro"),
                            "copies_per_turn", O(
                                "type", "integer",
                                "description", "quante copie su ogni giro attorno al centro della faccia"),
                            "margin_mm", O(
                                "type", "number",
                                "description", "quanto le copie restano lontane dal bordo della faccia; predefinito 0")),
                        "required", A(
                            "pitch_mm",
                            "copies_per_turn")),
                },
                new OperationSpec
                {
                    Name = "chamfer_edges",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Smussa uno o piu' spigoli: bava, invito di montaggio, cianfrino da saldare.",
                    Description =
                        "Smusso su uno o piu' spigoli. E' il gemello di «fillet_edges» "
                        + "e si indica allo stesso modo.\n\nPredefinito 45 gradi, che e' lo "
                        + "smusso di gran lunga piu' comune. Per uno smusso non simmetrico — "
                        + "un cianfrino di saldatura, un invito che deve entrare in una sede "
                        + "— metti «second_leg_mm» e l'angolo viene ignorato.\n\nSmusso o "
                        + "raccordo non e' una preferenza estetica: uno smusso si fa con una "
                        + "fresa o una lima, un raccordo vuole un utensile di raggio. Se "
                        + "l'utente dice «smussa», non arrotondare."
                        + "\n\nMI RIFIUTO SE LO SMUSSO E' TROPPO GRANDE PER LO SPIGOLO -- non ci sta "
                        + "nemmeno meta'. Un valore oltre il limite non da' sempre errore: a volte "
                        + "esce una geometria degenere che passa il ricalcolo."
                        + "\n\nThe chamfer follows tangent edges like the fillet. The document state "
                        + "lists each chamfer with «selected_edges» and «faces», and its sizes by type: "
                        + "angle-distance «leg1_mm» + «angle_deg», distance-distance «leg1_mm» + "
                        + "«leg2_mm», and a vertex chamfer (made by hand) «vertex_distances_mm», its "
                        + "three distances along the corner's edges. The answer carries «selected_edges» "
                        + "and «faces» too, read the same way."
                        + "\n\nI refuse when the volume of the part does not change after the chamfer is "
                        + "made: the feature is in the tree and worked nothing, and I say it is still "
                        + "there to undo. On a concave edge the volume grows, and that is fine. "
                        + "material_change_mm3 gives the change (after minus before); volume_not_read "
                        + "says when the volume could not be read.",
                    Precondition = "solid_body_and_edge",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "edges_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "spigoli che ci sono GIA', presi dallo stato"),
                            "edges_ref", O(
                                "type", "object",
                                "description", "gli spigoli creati da un passo precedente di questo piano. Metti "
                                    + "questo OPPURE edges_uid, non tutti e due.\n\nGeneri ammessi:\n- "
                                    + "bottom_edges: gli spigoli del bordo sul fondo "
                                    + "dell'estrusione\n- side_edges: gli spigoli lungo la "
                                    + "direzione di estrusione, i verticali\n- top_edges: gli "
                                    + "spigoli del bordo in cima all'estrusione\n- all_edges: "
                                    + "tutti gli spigoli creati da quel passo",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "bottom_edges",
                                            "side_edges",
                                            "top_edges",
                                            "all_edges"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "leg_mm", O(
                                "type", "number",
                                "description", "quanto materiale si toglie, misurato su una faccia"),
                            "angle_deg", O(
                                "type", "number",
                                "description", "predefinito 45; ignorato se dai «second_leg_mm»"),
                            "second_leg_mm", O(
                                "type", "number",
                                "description", "solo per uno smusso non simmetrico: il lato sull'altra faccia")),
                        "required", A(
                            "leg_mm")),
                },
                new OperationSpec
                {
                    Name = "mirror",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Specchia una lavorazione rispetto a uno dei tre piani base.",
                    Description =
                        "Specchia una lavorazione gia' fatta rispetto a un piano di "
                        + "simmetria. La lavorazione si indica come per "
                        + "«circular_pattern».\n\nIl piano e' sempre uno dei tre base, e va "
                        + "detto: sbagliarlo non sposta la geometria di poco, la mette da "
                        + "un'altra parte del pezzo, e la costruzione riesce lo stesso.\n\nSi "
                        + "specchia la LAVORAZIONE, non il corpo intero: per un pezzo "
                        + "simmetrico specchia il foro o la tasca che manca da un lato."
                        + "\n\nMI RIFIUTO SE IL PIANO NON ESISTE, e ti dico quali ci sono. I nomi "
                        + "inventati -- diagonale, obliquo -- non li interpreto: specchiare sul "
                        + "piano sbagliato produce un pezzo plausibile e sbagliato.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "cosa specchiare, se c'e' gia' nell'albero"),
                            "feature_ref", O(
                                "type", "object",
                                "description", "la lavorazione fatta da un passo precedente di questo piano. "
                                    + "Metti questo OPPURE feature_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- feature: la lavorazione fatta da quel passo, per "
                                    + "ripeterla",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "feature"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "piano di costruzione: front, top o right")),
                        "required", A(
                            "plane")),
                },
                new OperationSpec
                {
                    Name = "move_bodies",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Sposta o COPIA dei corpi gia' fatti, di una distanza data.",
                    Description =
                        "Sposta i corpi indicati di «dx_mm», «dy_mm», «dz_mm». Con «copy» "
                        + "a vero li LASCIA DOVE SONO e ne mette delle copie piu' in la': e' "
                        + "cosi' che si fanno due telai uguali a una certa distanza.\n\nSERVE "
                        + "QUANDO IL PEZZO HA DUE PARTI UGUALI IN POSTI DIVERSI. Un castello "
                        + "fatto di due telai distanti 600 mm non si chiede facendo due "
                        + "volte il telaio: la seconda volta verrebbe rifiutata, perche' due "
                        + "volte la stessa operazione con gli stessi numeri fa due corpi "
                        + "sovrapposti. Si fa UN telaio, e poi lo si copia spostato di 600 "
                        + "in Z.\n\nQuali corpi: «bodies_uid» per quelli che ci sono GIA' nel "
                        + "documento (C1, C2...), «bodies_ref» per quelli che li crea un "
                        + "passo precedente di questo stesso piano. Uno o l'altro, mai tutti "
                        + "e due.\n\nSolo spostamento in linea retta: questa operazione non "
                        + "ruota niente. Se serve ruotare, dillo invece di far finta.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "bodies_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "i corpi da spostare, per identificativo dello stato del documento "
                                    + "(C1, C2...)"),
                            "bodies_ref", O(
                                "type", "object",
                                "description", "i corpi creati da un passo precedente di questo stesso piano. "
                                    + "Metti questo OPPURE bodies_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- bodies: le barre create da quel passo, tutte insieme",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "bodies"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "dx_mm", O(
                                "type", "number",
                                "description", "spostamento lungo X, in mm"),
                            "dy_mm", O(
                                "type", "number",
                                "description", "spostamento lungo Y"),
                            "dz_mm", O(
                                "type", "number",
                                "description", "spostamento lungo Z"),
                            "copy", O(
                                "type", "boolean",
                                "description", "vero: lascia gli originali e aggiunge delle copie spostate. "
                                    + "Predefinito: falso, cioe' li sposta e basta."),
                            "copy_count", O(
                                "type", "integer",
                                "description", "quante copie, solo se «copy» e' vero. Predefinito 1. La copia "
                                    + "numero k finisce a k volte lo spostamento.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "cylindrical_cut",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Un taglio tondo su una faccia: per un passaggio che NON e' un "
                        + "foro da viteria.",
                    Description =
                        "Fa un foro su una faccia gia' esistente, alle coordinate indicate "
                        + "nel piano di quella faccia. Passante o cieco."
                        + "\n\nMI RIFIUTO SE IL FORO CADE TUTTO FUORI DAL MATERIALE (ti dico fra quali "
                        + "coordinate sta la faccia) e se e' cosi' largo da coprire tutta la "
                        + "faccia: quello non e' un foro, e' un pezzo che sparisce.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_uid", O(
                                "type", "string",
                                "description", "identificativo della faccia, preso dallo stato del documento. "
                                    + "Usalo per una faccia che c'e' GIA'."),
                            "face_ref", O(
                                "type", "object",
                                "description", "la faccia creata da un passo precedente di questo stesso piano, "
                                    + "quando il foro va su geometria che stai creando adesso e che "
                                    + "quindi non ha ancora nessun identificativo. Metti questo OPPURE "
                                    + "face_uid, non tutti e due.\n\nGeneri ammessi:\n- pocket_floor: "
                                    + "il fondo della tasca (c'e' solo se la tasca e' cieca)\n- "
                                    + "bottom_face: la faccia sul piano di schizzo, il fondo "
                                    + "dell'estrusione\n- top_face: la faccia in cima "
                                    + "all'estrusione, quella opposta al piano di schizzo",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "pocket_floor",
                                            "bottom_face",
                                            "top_face"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "x_mm", O(
                                "type", "number",
                                "description", "COORDINATE DEL MODELLO in mm, non coordinate della faccia. Prendi "
                                    + "i due assi che restano togliendo quello della normale della "
                                    + "faccia, in ordine X, Y, Z: normale lungo Z -> x_mm e' la X del "
                                    + "modello e y_mm la Y; normale lungo Y -> x_mm e' la X e y_mm la Z; "
                                    + "normale lungo X -> x_mm e' la Y e y_mm la Z. Sono gli stessi "
                                    + "numeri di «point_mm» che leggi nello stato del documento, quindi "
                                    + "puoi partire da quelli. La terza coordinata non serve: e' il "
                                    + "piano della faccia."),
                            "y_mm", O(
                                "type", "number",
                                "description", "vedi x_mm"),
                            "anchor", AnchorSchema("il foro"),
                            "diameter_mm", O(
                                "type", "number"),
                            "through_all", O(
                                "type", "boolean",
                                "description", "predefinito: si'"),
                            "depth_mm", O(
                                "type", "number",
                                "description", "solo se non passante")),
                        "required", A(
                            "x_mm",
                            "y_mm",
                            "diameter_mm")),
                },
                new OperationSpec
                {
                    Name = "rectangular_cut",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Una tasca rettangolare su una faccia.",
                    Description =
                        "Scava una tasca rettangolare su una faccia esistente."
                        + "\n\nMI RIFIUTO SE LA TASCA COPRE TUTTA LA FACCIA, e ti dico quanto misura. "
                        + "Una tasca grande quanto il pezzo non e' una tasca: e' il pezzo che non "
                        + "c'e' piu'.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_uid", O(
                                "type", "string",
                                "description", "una faccia che c'e' gia', presa dallo stato"),
                            "face_ref", O(
                                "type", "object",
                                "description", "la faccia creata da un passo precedente di questo stesso piano. "
                                    + "Metti questo OPPURE face_uid, non tutti e due.\n\nGeneri "
                                    + "ammessi:\n- pocket_floor: il fondo della tasca (c'e' solo se la "
                                    + "tasca e' cieca)\n- bottom_face: la faccia sul piano di "
                                    + "schizzo, il fondo dell'estrusione\n- top_face: la faccia "
                                    + "in cima all'estrusione, quella opposta al piano di schizzo",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "pocket_floor",
                                            "bottom_face",
                                            "top_face"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "x_mm", O(
                                "type", "number",
                                "description", "COORDINATE DEL MODELLO in mm, non coordinate della faccia. Prendi "
                                    + "i due assi che restano togliendo quello della normale della "
                                    + "faccia, in ordine X, Y, Z: normale lungo Z -> x_mm e' la X del "
                                    + "modello e y_mm la Y; normale lungo Y -> x_mm e' la X e y_mm la Z; "
                                    + "normale lungo X -> x_mm e' la Y e y_mm la Z. Sono gli stessi "
                                    + "numeri di «point_mm» che leggi nello stato del documento, quindi "
                                    + "puoi partire da quelli. La terza coordinata non serve: e' il "
                                    + "piano della faccia."),
                            "y_mm", O(
                                "type", "number",
                                "description", "vedi x_mm"),
                            "anchor", AnchorSchema("la tasca"),
                            "length_mm", O(
                                "type", "number",
                                "description", "lungo x_mm"),
                            "width_mm", O(
                                "type", "number",
                                "description", "lungo y_mm"),
                            "through_all", O(
                                "type", "boolean",
                                "description", "predefinito: no"),
                            "depth_mm", O(
                                "type", "number")),
                        "required", A(
                            "x_mm",
                            "y_mm",
                            "length_mm",
                            "width_mm")),
                },
                new OperationSpec
                {
                    Name = "apply_material",
                    Family = "physical_properties",
                    Example = "{\"material\":\"AISI 304\"}",   // from `mass_of_a_steel_cube`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Assegna il materiale al pezzo: senza, non c'e' peso ne' distinta utile.",
                    Description =
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
                        + "e non tu."
                        + "\n\nThe material goes on EVERY configuration of the part, each one set "
                        + "and read back by its own name (measured: the empty configuration name "
                        + "reaches only the active configuration and the ones derived from it). The "
                        + "answer lists them in «configurations»; I refuse the step, naming them, if "
                        + "any configuration does not take the material.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "material", O(
                                "type", "string",
                                "default", "S235JR",
                                "description", "per esempio «S235JR», «AISI 304», «Lega 6061». "
                                    + "Se non lo dici uso S235JR, e te lo scrivo nella risposta."),
                            "library", O(
                                "type", "string",
                                "description", "opzionale: il nome dell'archivio, se sai in quale cercare. "
                                    + "Lascialo stare se non lo sai.")),
                        // Z-G3: `material` is NO longer mandatory — there is a
                        // workshop default, and the answer declares that it used
                        // it. A field that is always asked and whose answer is
                        // always the same makes the model write a line that
                        // is not a choice.
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "create_axis",
                    Family = "references",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Un asse lungo il centro di un foro o di un albero: serve per "
                        + "ripetere ATTORNO a quel foro.",
                    Description =
                        "Crea un asse di riferimento lungo il centro di una faccia "
                        + "cilindrica o conica gia' esistente — il foro o l'albero attorno a "
                        + "cui poi ripetere qualcosa con «circular_pattern».\n\nQUANDO SERVE "
                        + "DAVVERO: per ripetere attorno a un FORO. Il bordo di un foro e' "
                        + "un cerchio, non una retta, e come direzione viene rifiutato; "
                        + "l'asse invece passa per il suo centro.\n\nQUANDO NON SERVE: per una "
                        + "fila o una griglia. Li' la direzione si prende da uno spigolo "
                        + "dritto del pezzo, che nello stato c'e' gia' («E1», «E2»...). "
                        + "L'asse di un foro punta NELLO SPESSORE della piastra: ripetere "
                        + "lungo quello non fa una fila.\n\nSolo facce cilindriche o coniche: "
                        + "una faccia piana non ha un asse. Nello stato del documento sono "
                        + "quelle con «planar: false»."
                        + "\n\nOn a sheet-metal part too: the axis is the one the call made, wherever "
                        + "SOLIDWORKS puts it in the tree (before the flat pattern).",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_uid", O(
                                "type", "string",
                                "description", "la faccia cilindrica o conica, presa dallo stato del documento"),
                            "face_ref", O(
                                "type", "object",
                                "description", "la faccia creata da un passo precedente di questo piano. Metti "
                                    + "questo OPPURE face_uid, non tutti e due.\n\nGeneri ammessi:\n- "
                                    + "pocket_floor: il fondo della tasca (c'e' solo se la tasca e' "
                                    + "cieca)\n- bottom_face: la faccia sul piano di schizzo, il "
                                    + "fondo dell'estrusione\n- top_face: la faccia in cima "
                                    + "all'estrusione, quella opposta al piano di schizzo",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "pocket_floor",
                                            "bottom_face",
                                            "top_face"))),
                                "required", A(
                                    "step",
                                    "kind"))),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "create_plane",
                    Family = "references",
                    Example = "{\"plane\":\"front\",\"distance_mm\":-250}",   // from `front_plane_minus_250`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Un piano di riferimento nuovo, parallelo a un piano base e "
                        + "spostato di una quota.",
                    Description =
                        "Crea un piano di riferimento parallelo a un piano base (frontale, "
                        + "superiore o destro) e spostato di una quota lungo la sua normale. "
                        + "The base plane is found by its geometry (its normal through the "
                        + "origin), not by its place in the tree, so a template with an extra plane "
                        + "before the three default ones does not move it. "
                        + "Serve per costruire uno schizzo a una certa altezza o profondita' "
                        + "senza toccare il piano base — «un piano parallelo al superiore, a "
                        + "600 mm» per lavorare a meta' altezza di un montante.\n\nDistanza "
                        + "negativa: dalla parte opposta. Distanza zero: un piano "
                        + "coincidente col base, come oggetto distinto.\n\nSolo piani base: "
                        + "questa versione non sa appoggiare il piano nuovo su una faccia "
                        + "del pezzo.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "piano di costruzione: front, top o right"),
                            "distance_mm", O(
                                "type", "number",
                                "description", "quanto si sposta dal piano base. Negativo per l'altro verso.")),
                        "required", A(
                            "distance_mm")),
                },
                new OperationSpec
                {
                    Name = "weld_bead",
                    Family = "weldments",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Cordone di saldatura cosmetico fra due facce di due barre che si toccano ad angolo.",
                    Description =
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
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "start_face_uid", O(
                                "type", "string",
                                "description", "una faccia del primo corpo, presa dallo stato del documento "
                                    + "(F1, F2...)"),
                            "end_face_uid", O(
                                "type", "string",
                                "description", "una faccia dell'ALTRO corpo, che tocca la prima lungo uno "
                                    + "spigolo e fa angolo con lei"),
                            "leg_mm", O(
                                "type", "number",
                                "description", "lato del cordone (a)")),
                        "required", A(
                            "start_face_uid",
                            "end_face_uid",
                            "leg_mm")),
                },
                new OperationSpec
                {
                    Name = "cut_list",
                    Family = "weldments",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Crea o aggiorna la distinta di taglio del pezzo saldato. Va per ultima.",
                    Description =
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
                        + "quantita' deve essere il numero delle barre, se no lo dice."
                        + "\n\nThe answer says what SOLIDWORKS answered: automatic_cut_list_set (null "
                        + "when the cut list was automatic already), automatic_update_set and "
                        + "cut_list_updated. When the cut list did not update, the warning field of the "
                        + "answer says the numbers may be stale. Each item gets bodies_equal, and the "
                        + "same warning field speaks when "
                        + "an item QUANTITY differs from the bodies of its folder, when one item holds "
                        + "bodies of different volume, or when the folders hold a different number of "
                        + "bodies than the part. A cut list that is not automatic (made by hand or "
                        + "before library 0.124.0) is refused: delete it first. When a refusal removes "
                        + "the weldment feature this step added, the message says whether IsWeldment "
                        + "reads false again.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "update_if_exists", O(
                                "type", "boolean",
                                "description", "se la distinta c'e' gia', la ricalcola invece di crearne una "
                                    + "seconda (predefinito: si')")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "gusset",
                    Family = "weldments",
                    RequestedDocument = "part",
                    // ── PROVEN SINCE 28/9/2026 (entry GP20) ─────────────
                    //
                    // Up to library 0.139.0 a gusset was never born:
                    // the method passed a polygon with the short sides at zero
                    // (ProfileD3 = ProfileD4 = 0) and no mark, and
                    // InsertGussetFeature3 returned null. The API guide (Remarks
                    // of InsertGussetFeature3, Insert_Gusset_Feature_Example_
                    // CSharp.htm) wants the two faces with mark 1 AND a valid
                    // profile. Measured with the probe Checks\Gusset\
                    // Program.cs on the T 80x80x5: triangle 60/60
                    // thickness 5 +9000 mm3, polygon 60/60/15/15 +12937.5, bodies
                    // 2 -> 3. Cases: weldments/gusset_sits_in_the_corner and
                    // weldments/gusset_polygon_is_not_degenerate.
                    Tested = true,
                    Summary =
                        "Fazzoletto di rinforzo, triangolare o poligonale, nell'angolo fra due facce "
                        + "piane di due barre che si toccano.",
                    Description =
                        "Mette un FAZZOLETTO di rinforzo nell'angolo fra due barre di un telaio "
                        + "saldato: una piastra, corpo a se', che irrigidisce il nodo e finisce in "
                        + "distinta.\n\nLE DUE FACCE si indicano per identificativo, prese dallo "
                        + "stato del documento: e' un'operazione che si chiede DOPO aver fatto le "
                        + "barre e i tagli, guardandole. Devono essere PIANE, fare un angolo e "
                        + "toccarsi lungo uno spigolo: per una barra in battuta su un'altra, la "
                        + "faccia LATERALE della barra e la faccia su cui arriva (le stesse di "
                        + "«weld_bead»). In un angolo di tubolari le facce candidate sono "
                        + "quattro, e sceglierne due «probabili» vorrebbe dire mettere il rinforzo "
                        + "dalla parte sbagliata. Facce parallele, lontane o curve: rifiuto, prima "
                        + "di toccare il pezzo.\n\nIL PROFILO: «triangular» (il predefinito) coi "
                        + "due lati «leg1_mm» e «leg2_mm» lungo le due facce; oppure «polygonal», "
                        + "che taglia la punta e vuole anche i due lati corti «side3_mm» e "
                        + "«side4_mm», piu' corti dei due lati. Un poligono senza lati corti e' "
                        + "degenere e lo rifiuto: e' il difetto per cui questa operazione non ha "
                        + "mai creato niente fino alla libreria 0.139.0. Lo spessore sta da tutte e "
                        + "due le parti del piano del fazzoletto; «position» e' «start», «center» "
                        + "(il predefinito) o «end» lungo lo spigolo comune, e un altro valore lo "
                        + "rifiuto invece di metterlo al centro di nascosto.\n\nLA RISPOSTA E' "
                        + "RILETTA, non ripetuta: il volume aggiunto contro il conto a mano "
                        + "(«volume_added_mm3», «volume_expected_mm3»), i corpi prima e dopo, lo "
                        + "spessore, il profilo, la posizione e le due facce d'appoggio riletti "
                        + "dalla feature («thickness_read_mm», «profile_read», «location_read», "
                        + "«supporting_faces_area_mm2»), il corpo nuovo e la sua scatola "
                        + "(«new_body», «new_body_box_mm»). Se una di queste non torna, il "
                        + "fazzoletto lo tolgo e te lo dico. Se ti serve solo documentare la "
                        + "saldatura, usa «weld_bead».",
                    // No example: the two green cases name the faces with the
                    // bench selectors (@face(area~=...,normal=...)), which the
                    // client cannot copy (see OperationSpec.Esempio).
                    Example = null,
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "faces_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "esattamente DUE facce piane, una per barra, che si toccano lungo "
                                    + "uno spigolo e fanno angolo, prese dallo stato del documento (F1, F2...)"),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "spessore della piastra, da tutte e due le parti del suo piano"),
                            "leg1_mm", O(
                                "type", "number",
                                "description", "uno dei due lati del fazzoletto, quelli che corrono lungo le due "
                                    + "facce. Quale lato vada su quale faccia la guida non lo dice e nessuna "
                                    + "prova lo ha distinto (misurato solo con lati uguali): se ti importa, "
                                    + "guarda il pezzo dopo"),
                            "leg2_mm", O(
                                "type", "number",
                                "description", "l'altro lato, lungo l'altra faccia (vedi leg1_mm)"),
                            "profile", O(
                                "type", "string",
                                "enum", A(
                                    "triangular",
                                    "polygonal"),
                                "description", "forma della piastra (predefinito: triangular). Il poligonale "
                                    + "taglia la punta e vuole «side3_mm» e «side4_mm»"),
                            "side3_mm", O(
                                "type", "number",
                                "description", "solo col profilo poligonale: primo lato corto, piu' corto dei due lati"),
                            "side4_mm", O(
                                "type", "number",
                                "description", "solo col profilo poligonale: secondo lato corto, piu' corto dei due lati"),
                            "position", O(
                                "type", "string",
                                "enum", A(
                                    "start",
                                    "center",
                                    "end"),
                                "description", "dove sta lungo lo spigolo fra le due facce (predefinito: center)")),
                        "required", A(
                            "faces_uid",
                            "thickness_mm",
                            "leg1_mm",
                            "leg2_mm")),
                },
                new OperationSpec
                {
                    Name = "structural_member",
                    Family = "weldments",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Barre vere di carpenteria lungo i segmenti che dai: e' cio' che "
                        + "genera la distinta di taglio.",
                    Description =
                        "Crea una o piu' BARRE di un telaio saldato: membri strutturali "
                        + "veri, con il profilo preso dalla libreria dell'azienda.\n\nE' "
                        + "L'OPERAZIONE DA USARE PER TUTTO CIO' CHE E' CARPENTERIA: "
                        + "tubolari, angolari, piatti, IPE, HEA, HEB, UPN, UPE, profilati a "
                        + "U e a T — sia una barra sola sia un telaio intero.\n\nPerche' non "
                        + "basta estrudere una sezione: solo il membro strutturale genera la "
                        + "DISTINTA DI TAGLIO, porta il nome del profilo, e si lascia "
                        + "tagliare a 45 gradi agli incroci. Una barra estrusa ha la stessa "
                        + "forma e non serve a fare il pezzo.\n\nPRIMA CHIAMA "
                        + "«weldment_profiles» e copia da li' «profile_path» e "
                        + "«configuration» alla lettera. Non comporre il nome della misura: "
                        + "la libreria non e' uniforme e un nome inventato fa usare a "
                        + "SolidWorks un'altra misura, senza errore.\n\nI segmenti sono le "
                        + "linee lungo cui corrono le barre, in coordinate del modello in "
                        + "mm. Una barra dritta lunga 200 sull'asse X e' un segmento solo: "
                        + "da [0,0,0] a [200,0,0]. Un telaio rettangolare sono quattro "
                        + "segmenti che si chiudono."
                        + "\n\nIf the weldment profile library is still open and modified in "
                        + "SOLIDWORKS after the insertion, the answer carries "
                        + "«profile_library_warning»: close that library without saving, or a "
                        + "Ctrl+S would rewrite the shared library. The warning can carry a second "
                        + "sentence: the library is still LOADED, hidden (an open document references it, or the close did nothing), "
                        + "with the insertion's changes in memory even if its save "
                        + "flag reads clean."
                        + "\n\nI refuse an empty «configuration» when the profile file has several "
                        + "sizes, and list them: with an empty size SOLIDWORKS builds the size the file "
                        + "was saved on (measured: the 20 x 20 of the four in iso\\square tube.sldlfp). "
                        + "An empty size is accepted only on a file with one size, and the answer says so "
                        + "in «configuration_note».",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "profile_path", O(
                                "type", "string",
                                "description", "percorso completo del .sldlfp, copiato da weldment_profiles"),
                            "configuration", O(
                                "type", "string",
                                "description", "la misura, copiata ALLA LETTERA da weldment_profiles (es. "
                                    + "«80X80X5»: maiuscole e virgole comprese). Empty only for a profile "
                                    + "file with one size."),
                            "segments", O(
                                "type", "array",
                                "description", "le linee lungo cui corrono le barre, in mm nelle coordinate del modello",
                                "items", O(
                                    "type", "object",
                                    "properties", O(
                                        "start", O(
                                            "type", "array",
                                            "items", O(
                                                "type", "number"),
                                            "description", "[x, y, z] di partenza, in mm"),
                                        "end", O(
                                            "type", "array",
                                            "items", O(
                                                "type", "number"),
                                            "description", "[x, y, z] di arrivo, in mm")),
                                    "required", A(
                                        "start",
                                        "end"))),
                            "rotation_deg", O(
                                "type", "number",
                                "description", "rotazione del profilo attorno al proprio asse (predefinito: 0). "
                                    + "Serve quando un angolare o una U vanno girati."),
                            "corner_treatment", O(
                                "type", "string",
                                "enum", A(
                                    "none",
                                    "miter",
                                    "butt1",
                                    "butt2"),
                                "description", "come si incontrano fra loro le barre di QUESTO gruppo, DOVE SI "
                                    + "TOCCANO. «miter» e' il taglio a 45 gradi negli angoli "
                                    + "(default: miter, the 45 degree miter, wherever the bars of "
                                    + "this group touch, closed or open path; the answer says it was the "
                                    + "default in «corner_treatment_note»); «butt1»/«butt2» e' una "
                                    + "barra che va in battuta sull'altra; «none» le lascia "
                                    + "compenetrate e va detto solo se lo si vuole davvero.\n\nNON taglia "
                                    + "le estremita' LIBERE. Su una barra sola, o sui due capi di un "
                                    + "telaio aperto, non c'e' niente contro cui tagliare e questo "
                                    + "parametro non ha effetto."),
                            "gap_mm", O(
                                "type", "number",
                                "description", "gioco fra le barre agli incroci, per il cordone di saldatura "
                                    + "(predefinito: 0)")),
                        "required", A(
                            "profile_path",
                            "configuration",
                            "segments")),
                },
                new OperationSpec
                {
                    Name = "trim_members",
                    Family = "weldments",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Taglia o estende le barre dove si incontrano.",
                    Description =
                        "Taglia le barre dove si incontrano, o le estende fino a toccarsi. "
                        + "E' il «taglia/estendi» della barra di saldature.\n\nServe quando "
                        + "due gruppi di barre si incrociano e non sono stati trattati agli "
                        + "angoli: senza, i corpi restano compenetrati e la distinta di "
                        + "taglio riporta lunghezze che in officina non tornano.\n\nTi torno "
                        + "di quanto e' cambiato il volume (negativo = tolto). Se il pezzo "
                        + "non cambia, rifiuto: vuol dire che le barre non si incontrano, "
                        + "e un taglio che non taglia niente non te lo confermo."
                        + "\n\nA bar that stops short of the other one is extended to its face "
                        + "(measured: a bar 20 mm short grows by 20 mm, Options 1|2|8); a bar that "
                        + "enters the other is cut at its face as before. The coped cut is not used.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "bodies_to_trim_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "the bars to trim or extend (a bar that stops short grows to "
                                    + "the other one), by the identifier of the document state (C1, "
                                    + "C2...). Use this for bodies that exist ALREADY; the other field for "
                                    + "bodies a previous step of this plan creates. One or the other, never "
                                    + "both."),
                            "bodies_to_trim_ref", O(
                                "type", "object",
                                "description", "the bars to trim or extend, made by a previous step\n\nGeneri "
                                    + "ammessi:\n- bodies: le barre create da quel passo, tutte insieme",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "bodies"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "trim_boundary_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "le barre contro cui tagliare, per identificativo preso dallo "
                                    + "stato del documento (C1, C2...). Usa questo per corpi che ci sono "
                                    + "GIA'; l'altro campo per corpi che li crea un passo precedente di "
                                    + "questo piano. Uno o l'altro, mai tutti e due."),
                            "trim_boundary_ref", O(
                                "type", "object",
                                "description", "le barre contro cui tagliare, da un passo precedente\n\nGeneri "
                                    + "ammessi:\n- bodies: le barre create da quel passo, tutte insieme",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "bodies"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "end_condition", O(
                                "type", "string",
                                "enum", A(
                                    "miter",
                                    "butt1",
                                    "butt2",
                                    "cut"),
                                "description", "come si chiude il taglio (predefinito: cut). Any other "
                                    + "word is refused before the part is touched, a word meaning no "
                                    + "corner included: the trim/extend has no such corner type, and none "
                                    + "was measured. The answer names the condition used in end_condition."),
                            "gap_mm", O(
                                "type", "number",
                                "description", "gioco lasciato (predefinito: 0). Il gioco che passi e' "
                                    + "sempre quello applicato, anche 0: l'ultimo gioco dato a mano "
                                    + "in SOLIDWORKS non viene ereditato")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "end_cap",
                    Family = "weldments",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Chiude con un tappo le estremita' aperte delle barre cave.",
                    Description =
                        "Chiude con un tappo le estremita' aperte di una barra cava. Si "
                        + "mette sulle facce di estremita' create da un passo precedente, "
                        + "oppure sulle barre che ci sono gia' nel documento («C1», "
                        + "«C2»...).\n\nL'UNICA COSA CHE PUO' MANCARE E' LO SPESSORE DEL "
                        + "TAPPO. Non ha un valore predefinito, perche' e' una scelta di "
                        + "progetto: chiedilo, e chiedi SOLO quello. Il materiale non serve "
                        + "— lo si applica dopo con la sua operazione, e non cambia la forma "
                        + "del tappo."
                        + "\n\nA PLANAR FACE WITH A HOLE MACHINED INTO IT IS NOT AN OPEN END. "
                        + "A drilled side wall of a tube (its outer and its inner face) is "
                        + "looked at and left out, and the answer counts it in "
                        + "machined_faces_skipped. An open end is a face whose void was made "
                        + "by the bar itself, so an end whose inner edges were chamfered or "
                        + "counterbored is left out the same way. When a face cannot be told "
                        + "apart, nothing is capped and the answer says why.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "bodies_uid", O(
                                "type", "array",
                                "items", O(
                                    "type", "string"),
                                "description", "le barre di cui tappare le estremita' aperte, per identificativo "
                                    + "preso dallo stato del documento (C1, C2...). Usa questo per corpi "
                                    + "che ci sono GIA'; l'altro campo per corpi che li crea un passo "
                                    + "precedente di questo piano. Uno o l'altro, mai tutti e due."),
                            "ends_ref", O(
                                "type", "object",
                                "description", "le facce di estremita' da tappare, da un passo precedente\n\nGeneri "
                                    + "ammessi:\n- free_ends: le facce di estremita' ancora aperte "
                                    + "delle barre di quel passo, quelle che si tappano"
                                    + " (a wall with a hole machined into it is not an end: it is left out)",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "free_ends"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "spessore del tappo"),
                            "inset_mm", O(
                                "type", "number",
                                "description", "NOT a depth: the side gap between the cap and the inside of "
                                    + "the tube wall, on every side. The cap always sits flush with the end "
                                    + "of the bar. 0 or absent: half the wall thickness per side (measured on "
                                    + "an 80 x 80 x 5 tube, 70 x 70 inside: 65 x 65 caps); a value above 0 is "
                                    + "that gap. A cap that fills the whole inside with no gap cannot be asked "
                                    + "for yet."),
                            "chamfer_mm", O(
                                "type", "number",
                                "description", "IGNORED today: the corner treatment of the cap is off, and "
                                    + "without it SOLIDWORKS drops the chamfer (measured: 2 mm gives the same "
                                    + "volume as none). The cap corners stay sharp whatever the value.")),
                        "required", A(
                            "thickness_mm")),
                },
                new OperationSpec
                {
                    Name = "rectangular_frame",
                    Family = "weldments",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Un telaio rettangolare di quattro barre, dato con due quote "
                        + "invece che con otto coordinate.",
                    Description =
                        "RICETTA: un telaio rettangolare piano, quattro barre saldate agli "
                        + "angoli. E' il caso piu' comune della carpenteria, e si chiede con "
                        + "due quote invece che con otto coordinate.\n\nLe quote sono "
                        + "l'INGOMBRO ESTERNO del telaio finito. Il piano dice su quale "
                        + "piano principale sta.\n\nCome per «structural_member», profilo e "
                        + "misura si copiano da «weldment_profiles»."
                        + "\n\nI refuse the frame, and draw nothing, when the profile does not "
                        + "fit in the two sizes (its overhang, measured on a trial frame, is as "
                        + "large as a side), and when «measured_as» is neither «overall» nor "
                        + "«center_distance» (upper or lower case does not matter). The answer says "
                        + "which reading it used («measured_as»), whether the profile's overhang was "
                        + "measured now or remembered from an earlier frame («overhang_source»: "
                        + "«measured» or «archive»), and, if the weldment profile library is "
                        + "still open and modified in SOLIDWORKS afterwards, "
                        + "«profile_library_warning»: close that library without saving. The warning "
                        + "or the refusal can also say that the library is still LOADED, hidden, "
                        + "because an open document references it or the close did nothing, with the changes in memory even "
                        + "if its save flag reads clean.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "profile_path", O(
                                "type", "string"),
                            "configuration", O(
                                "type", "string",
                                "description", "the profile size, copied from weldment_profiles. Empty is "
                                    + "accepted only on a profile file with one size; with several sizes it is "
                                    + "refused, and the sizes are listed."),
                            "length_mm", O(
                                "type", "number",
                                "description", "ingombro esterno"),
                            "width_mm", O(
                                "type", "number",
                                "description", "ingombro esterno"),
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "piano di costruzione: front, top o right"),
                            "corner_treatment", O(
                                "type", "string",
                                "enum", A(
                                    "miter",
                                    "butt1",
                                    "butt2",
                                    "none"),
                                "description", "default: miter (45 degree miter); the answer says when "
                                    + "it was the default («corner_treatment_note»)"),
                            "rotation_deg", O(
                                "type", "number"),
                            "gap_mm", O(
                                "type", "number"),
                            "measured_as", O(
                                "type", "string",
                                "enum", A(
                                    "overall",
                                    "center_distance"),
                                "description", "cosa vogliono dire le due quote: «overall» e' la misura del "
                                    + "pezzo finito da fuori a fuori (e' quello che intende un "
                                    + "progettista quando dice «telaio 1000x800»); «center_distance» e' la "
                                    + "distanza fra gli assi delle barre. Predefinito: overall."
                                    + " Upper or lower case does not matter («Ingombro» is "
                                    + "«overall»); any other word is refused."),
                            "centered", O(
                                "type", "boolean",
                                "description", "telaio centrato sull'origine (predefinito: si')")),
                        "required", A(
                            "profile_path",
                            "configuration",
                            "length_mm",
                            "width_mm")),
                },
                new OperationSpec
                {
                    Name = "create_drawing",
                    Family = "drawing",
                    Example = "{\"paper_size\":\"A3\"}",   // from `two_sheets_and_the_same_projection_angle`
                    RequestedDocument = "part/assembly",
                    Tested = true,
                    Summary =
                        "Apre la tavola del pezzo che stai guardando.",
                    Description =
                        "Crea il documento di DISEGNO del pezzo aperto e lo mette davanti. "
                        + "Il pezzo resta aperto: una vista di tavola si aggancia al "
                        + "PERCORSO di un documento aperto, non a un file.\n\nIL PEZZO DEVE "
                        + "ESSERE GIA' SALVATO. Se non lo e', salvalo prima con "
                        + "«save_document».\n\nIl modello di tavola NON si sceglie: si usa "
                        + "quello di questa installazione, che porta il cartiglio "
                        + "dell'officina e il diedro giusto. Da qui in poi il documento "
                        + "attivo e' la TAVOLA, e le operazioni di disegno del pezzo non "
                        + "valgono piu' finche' non torni sul pezzo.",
                    Precondition = "un pezzo o un assieme SALVATO su disco",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "paper_size", O(
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
                new OperationSpec
                {
                    Name = "export_pdf",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "Il PDF di TUTTI i fogli, con le pagine contate.",
                    Description =
                        "Scrive il PDF della tavola.\n\nTUTTI i fogli, non solo quello che "
                        + "stai guardando: l'esportazione di serie prende il foglio "
                        + "corrente, e una tavola da tre fogli esce in officina come una "
                        + "pagina sola senza dare nessun errore. Dopo aver scritto il file "
                        + "si CONTANO le pagine e si confrontano con i fogli: se non "
                        + "tornano, te lo dico.\n\nI REFUSE when SOLIDWORKS does not accept "
                        + "the request for every sheet (IExportPdfData.SetSheets answers false), "
                        + "before writing anything; and when the pages of the written file "
                        + "cannot be counted, saying where the file is: without that count, the "
                        + "check this operation exists for did not run.\n\nThe answer carries "
                        + "save_warnings (the swFileSaveWarning_e names, or «none») and "
                        + "save_warnings_note when the drawing needed a rebuild, had rebuild "
                        + "errors, or had views needing an update. A failed save gives the error "
                        + "and warning names next to the raw codes.\n\nCon «search» il PDF si RILEGGE: dimmi le "
                        + "scritte che devono esserci (il numero di disegno, il nome del "
                        + "pezzo) e ti dico quali ci sono («found») e quali no "
                        + "(«not_found»). Il testo nel PDF e' scritto come indici di "
                        + "glifo, e una ricerca nei byte non lo trova: qui si decodifica.",
                    Precondition = "una tavola aperta",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "dove scrivere il PDF. Se non lo dici, accanto alla tavola con lo "
                                    + "stesso nome."),
                            "search", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "scritte che il PDF deve contenere: si rilegge il file e si dice quali ci sono")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "set_sheet",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "Formato e scala del foglio.",
                    Description =
                        "Cambia FORMATO e SCALA del foglio attivo, e rilegge per "
                        + "controllare.\n\nIL DIEDRO NON SI TOCCA e non e' un parametro. Una "
                        + "tavola in terzo diedro ha la vista destra e la sinistra "
                        + "scambiate, non da' nessun errore, e il pezzo che ne esce e' "
                        + "specchiato: si legge quello che il modello di tavola "
                        + "dell'officina ha gia' deciso e si riscrive uguale. Nelle misure "
                        + "trovi qual e'.",
                    Precondition = "una tavola aperta",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "paper_size", O(
                                "type", "string",
                                "enum", A(
                                    "A4",
                                    "A4V",
                                    "A3",
                                    "A2",
                                    "A1",
                                    "A0"),
                                "description", "formato del foglio"),
                            "scale_numerator", O(
                                "type", "number",
                                "description", "il primo numero della scala: 1 in «1:2»"),
                            "scale_denominator", O(
                                "type", "number",
                                "description", "il secondo numero della scala: 2 in «1:2»")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "model_dimensions",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "Porta in tavola le quote che il pezzo ha gia'.",
                    Description =
                        "Prende le quote dal MODELLO e le mette sulle viste.\n\nThe import "
                        + "works from this bridge, out of process: measured on 26/9/2026 (7 "
                        + "dimensions on a plate) and again on 28/9/2026. To put on the drawing "
                        + "a dimension the model does not have, «drawing_dimension» CREATES it on "
                        + "the edges the view shows.\n\nEach model dimension comes in once: I "
                        + "ask SOLIDWORKS to eliminate duplicates (DuplicateDims true), so a "
                        + "dimension already shown in one view is not repeated in another "
                        + "(measured: 4 dimensions on a plate that got 7 before).\n\nPerche' "
                        + "esiste lo stesso: una quota che viene dal modello non puo' portare "
                        + "un numero diverso dalla geometria, mentre l'API di SolidWorks "
                        + "permette di scrivere in tavola un testo che la geometria non ha."
                        + "\n\nDi serie arrivano solo le quote "
                        + "MARCATE per il disegno, cioe' quelle scelte per essere mostrate: "
                        + "con «all_dimensions» arrivano anche le altre, e la tavola diventa "
                        + "parecchio piu' fitta.\n\nBy default only the dimensions MARKED for "
                        + "drawing come in, fallbacks included. If the part has dimensions and "
                        + "none is marked, I refuse and say so: pass all_dimensions=true to "
                        + "import them all, or mark on the model the ones the drawing must show."
                        + "\n\nMI RIFIUTO se la tavola non ha viste (le quote "
                        + "non avrebbero dove attaccarsi) e se non arriva nessuna quota: te lo dico "
                        + "con quante il pezzo ne ha marcate, invece di rispondere «fatto».",
                    Precondition = "una tavola con almeno una vista",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "all_dimensions", O(
                                "type", "boolean",
                                "description", "anche le quote non marcate per il disegno. Rende la tavola piu' "
                                    + "fitta: usalo quando quelle marcate non bastano. Without it, "
                                    + "a part where no dimension is marked for drawing is refused, not "
                                    + "imported."),
                            "hole_callouts", O(
                                "type", "boolean",
                                "description", "anche le indicazioni dei fori guidati (M8, profondita', filetto). "
                                    + "Attive se non dici niente.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "center_marks",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    // ── PROVEN SINCE 26/9/2026 (library 0.125.0) ──────────
                    //
                    // Since 17/9 it was `Provata = false`: `AutoInsertCenterMarks2`
                    // answered True and put zero marks in 25 combinations, and
                    // `InsertCenterMark2/3` on the selected edges zero. On
                    // 26/9 (`prova0089 croci2`, with the positive check):
                    // the marks on the holes WERE THERE (SOLIDWORKS puts them when creating the
                    // view) and `GetCenterMarkCount` did not count them; from code a new
                    // mark is not born. The operation is a check: it counts
                    // by enumerating and refuses the holes without a mark. See entry W2.11.
                    Tested = true,
                    Summary =
                        "Controlla che ogni foro delle viste abbia la croce di centro, contandole davvero, e rifiuta se ne manca.",
                    Description =
                        "CONTROLLA che ogni foro visibile nelle viste abbia la croce di centro, e "
                        + "prova a metterla dove manca. SOLIDWORKS le mette da se' creando la vista, "
                        + "se l'opzione del documento «Croci di centro sui fori» e' accesa: quei fori "
                        + "li riconosco («circles_already_marked»). Da codice una croce "
                        + "nuova non nasce (misurato): un foro senza croce e' un rifiuto, con la "
                        + "strada — l'opzione accesa e la vista rifatta.\n\nPERCHE' SERVE. Una tavola con dodici fori e "
                        + "nessuna croce di centro si stampa e sembra completa. In officina manca "
                        + "la cosa che serve per forare: **dove sta il centro.**\n\nTI DICO "
                        + "QUANTE NE SONO NATE, contate sulla vista: ogni croce deve far salire il "
                        + "conto di uno, e un cerchio rimasto senza croce e' un rifiuto. Se il "
                        + "numero e' **zero** te lo dico invece di dire «fatto» — quasi sempre la "
                        + "vista e' quella sbagliata (i fori come cerchi si vedono dall'alto, di "
                        + "solito V2).\n\nLA DIMENSIONE, se non me la dici, e' quella del "
                        + "documento: e' una convenzione di studio.\n\nSenza «view» le guardo "
                        + "tutte. Raccordi e asole no: misurato, sugli archi la croce da codice "
                        + "non nasce.\n\nLE CROCI SI CONTANO ENUMERANDOLE: il contatore di "
                        + "SOLIDWORKS (GetCenterMarkCount) non vede quelle messe alla creazione "
                        + "della vista, e diceva zero su un foglio che ne aveva tre."
                        + "\n\nThe walk over the holes is the one standard_views also uses on views "
                        + "left with no mark.",
                    Precondition = "una tavola con almeno una vista",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O(
                                "type", "string",
                                "description", "UNA vista, come la chiama lo stato della tavola (V1, V2...) "
                                    + "o per nome. Vuoto = tutte le viste."),
                            "applies_to", O(
                                "type", "string",
                                "enum", A("holes"),
                                "description", "«holes», i cerchi interi (l'unico: sui raccordi la croce da codice non nasce, misurato)."),
                            "size_mm", O(
                                "type", "number",
                                "description", "quanto e' grande la croce. Se non lo dici uso le "
                                    + "impostazioni del documento, che e' quasi sempre la cosa giusta."),
                            "extended_lines", O(
                                "type", "boolean",
                                "description", "le linee d'asse che escono dalla croce fino al bordo "
                                    + "del cerchio. Spente se non dici niente.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "drawing_dimension",
                    Family = "drawing",
                    Example = "{\"edges\":[0,1,2,3]}",   // from `drawing_dimensions_itself`
                    RequestedDocument = "drawing",
                    // ── PROVEN, AND WHAT THAT MEANS HERE ───────────────
                    //
                    // Measured on 18/9/2026 out of process: four dimensions on
                    // four edges, and the VALUES READ BACK with
                    // `GetSystemValue2` — 120 mm the plate, 5 mm the chamfer, on
                    // a 120 x 80 x 10 part with chamfer 5.
                    //
                    // It is not enough that they appear: a dimension that measures something else
                    // is worse than a dimension that is missing, because on
                    // paper it reads the same. For this the operation reads back
                    // every dimension and returns the millimetre, and for this the
                    // first run of the probe — which answered `?` to the value —
                    // would NOT have been enough to mark it proven.
                    Tested = true,
                    Summary =
                        "Mette le quote sugli spigoli di una vista, e RILEGGE quanto misurano.",
                    Description =
                        "Mette quote di riferimento sugli spigoli di una vista di tavola, e "
                        + "per ognuna ti dice **quanti millimetri misura**, riletti da "
                        + "SOLIDWORKS dopo averla messa.\n\nQUESTA E' L'OPERAZIONE CHE "
                        + "QUOTA. «model_dimensions» fa un'altra cosa: IMPORTA le quote "
                        + "che il pezzo ha gia', e su questa installazione non ne porta "
                        + "nessuna (misurato su otto modelli di tavola diversi). Se ti "
                        + "serve una tavola quotata, usa questa.\n\nGLI SPIGOLI HANNO UN "
                        + "NUMERO, e te lo dico io: la vista espone il suo elenco, e senza "
                        + "«edges» quoto i primi «count». Chiedimi uno spigolo che non "
                        + "c'e' e ti rispondo quanti ne ha la vista, invece di quotarne un "
                        + "altro.\n\nTI DICO QUANTE NE SONO NATE, non che ho chiamato "
                        + "l'API: le quote si contano prima e dopo, una per una. Se non ne "
                        + "nasce nessuna te lo dico, e ti dico quali spigoli ho provato."
                        + "\n\nDOVE FINISCONO: fuori dalla vista — le orizzontali sotto, le "
                        + "verticali di lato, dentro la cornice del foglio — dalle piu' piccole "
                        + "alle piu' grandi."
                        + "\n\nOVERLAPS ARE CHECKED AND REPORTED, not avoided: the true rectangle of "
                        + "every dimension text, read back from SOLIDWORKS, against the other texts of "
                        + "the whole drawing and against the lines of the part in this view. "
                        + "«overlapping_texts» lists each overlap, and the answer also gives "
                        + "their count and how many texts were read; null, not 0, means the texts "
                        + "could not be read, or the walk over the dimensions of the drawing broke off "
                        + "before the end, so overlaps are unknown. Dimension lines, "
                        + "extension lines and the title block are not checked. The dimensions are "
                        + "correct; a well laid out drawing is still a trade."
                        + "\n\nFORI E SMUSSI LI QUOTO DA SOLO, in ogni vista che li mostra, anche "
                        + "se non li chiedi: i fori col RICHIAMO DEL FORO («4× Ø8 PASSANTE», one "
                        + "callout per diameter AND depth: two through Ø8 and two blind Ø8 get two "
                        + "callouts, never one «4×») e la posizione dai due bordi di riferimento, quello a "
                        + "sinistra e quello in basso; gli smussi con la QUOTA DA SMUSSO («5 x "
                        + "45°»), non coi loro spigoli — l'ipotenusa e i tratti accorciati "
                        + "dallo smusso non si quotano. Un foro cieco porta la profondita' "
                        + "(«Ø8 ↧5»); se il richiamo non nasce, la scrivo io dalla geometria."
                        + "\n\nTHE «4×» IS READ BACK: after writing the count in front of the "
                        + "callout I read its prefix again (fori[].prefix_read). If the count did "
                        + "not stick, the holes are listed in edges_not_dimensioned and the count "
                        + "must be written by hand; «how_many» is the number of holes, not the text."
                        + "\n\nUN CERCHIO NON E' SEMPRE UN FORO: il contorno di un pezzo tondo "
                        + "visto di fronte ha una quota di diametro (outer_diameters)."
                        + "\n\nIL PEZZO TONDO VISTO DI FIANCO (albero, boccola, perno) lo quoto "
                        + "cosi': le LUNGHEZZE fra le facce in parallelo dalla faccia di "
                        + "un'estremita' (round_lengths_mm) e i DIAMETRI dalle due generatrici "
                        + "di ogni cilindro, col Ø (round_diameters_mm), ognuno riletto. SOLIDWORKS "
                        + "non sempre espone le generatrici di un cilindro: quei diametri te li "
                        + "dico in round_diameters_not_reached, e vanno quotati a mano (un foro "
                        + "interno si quota dalla vista in cui e' un cerchio)."
                        + "\n\nUNA QUOTA SOLA PER ELEMENTO IN TUTTA LA TAVOLA: se la stessa "
                        + "estensione del pezzo e' gia' quotata in un'altra vista, qui la salto "
                        + "e te lo dico (UNI ISO 129-1). Quindi quota le viste una alla volta, e "
                        + "non aspettarti in ogni vista le misure che ha gia' un'altra."
                        + "\n\nON EVERY SHEET: «already dimensioned» is read from the views of "
                        + "all the sheets, only from views of the same part and configuration, "
                        + "and a dimension hidden by hand does not count (one on a hidden layer, "
                        + "or of a suppressed feature, still does: I cannot see those). The "
                        + "answer says what was read: already_dimensioned_read_from («every "
                        + "sheet», or «current sheet only» when the drawing did not hand over "
                        + "its sheets), hidden_dimensions_not_counted, "
                        + "views_of_other_models_not_counted."
                        + "\n\nA DIMENSION BORN WITHOUT ITS OBJECT (the count of dimensions went up, "
                        + "the call returned nothing) stays: it is listed with its values null and "
                        + "named in edges_not_dimensioned, since its value was not read back. For a hole "
                        + "it does not trigger the fallback diameter dimension, which would be a second "
                        + "annotation on the same hole. The same holds in the turned view: a length or "
                        + "a diameter born without its object is not in round_lengths_mm or "
                        + "round_diameters_mm (they list what was read back), is not dimensioned again in "
                        + "another view and is not reported as not reached; one born between two "
                        + "silhouette edges without a face cannot be matched to a cylinder, so no further "
                        + "pair is tried, and its line in edges_not_dimensioned says so."
                        + "\n\nMI RIFIUTO SE LO SPIGOLO CHIESTO NON ESISTE nella vista, e ti dico "
                        + "quanti ne mostra. Un indice che non c'e' e' l'errore piu' facile da "
                        + "fare quotando alla cieca. E rifiuto una vista in cui non resta niente "
                        + "da quotare perche' e' tutto gia' quotato altrove: zero quote non sono "
                        + "una risposta «fatto».",
                    Precondition = "una tavola con almeno una vista che mostri spigoli",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O(
                                "type", "string",
                                "description", "UNA vista: il suo identificativo nello stato della "
                                    + "tavola (V1, V2...) oppure il suo nome. Vuoto = la prima vista vera."),
                            "edges", O(
                                "type", "array",
                                "items", O("type", "integer"),
                                "description", "i numeri degli spigoli da quotare, dall'elenco che la "
                                    + "vista espone (il primo e' 0). Vuoto = i primi «count»."),
                            "count", O(
                                "type", "integer",
                                "description", "quante quote mettere quando non dici quali spigoli. "
                                    + "Predefinito 4."),
                            "offset_mm", O(
                                "type", "number",
                                "description", "quanto sotto l'ingombro della vista mettere la prima "
                                    + "quota. Predefinito 12."),
                            "pitch_mm", O(
                                "type", "number",
                                "description", "la distanza fra una quota e la successiva. "
                                    + "Predefinito 10.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "hole_table",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "La tabella fori su una vista: un tag per foro, la posizione dall'origine e la misura.",
                    Description =
                        "Mette in tavola la TABELLA FORI della faccia forata che si vede in una vista: "
                        + "per ogni foro un tag (A1, A2...), la posizione X e Y da un vertice d'angolo "
                        + "del pezzo, e la misura («Ø8 PASSANTE»). E' la tabella che serve a chi fora "
                        + "una piastra con tanti fori, al posto di una quota per foro.\n\nLa vista e' "
                        + "quella in cui i fori si vedono come cerchi (di solito V2, dall'alto): la "
                        + "faccia la sceglie l'operazione fra quelle che guardano verso chi legge, la "
                        + "piu' forata. «origin» dice da quale angolo di quella faccia si misura "
                        + "(predefinito bottom_left).\n\nLa tabella si RILEGGE: le righe devono "
                        + "essere i fori piu' l'intestazione, e ogni posizione scritta deve "
                        + "corrispondere a un foro del pezzo misurato sulla vista. Misurato: con "
                        + "l'origine e la faccia selezionate male SOLIDWORKS fa nascere una tabella "
                        + "VUOTA senza errore.\n\nMI RIFIUTO se nessuna faccia con dei fori guarda "
                        + "verso chi legge in quella vista, e se la tabella nasce vuota o dice "
                        + "posizioni che il pezzo non ha: in quei casi la tolgo.",
                    Precondition = "una tavola con una vista del pezzo in cui i fori si vedono come cerchi",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O(
                                "type", "string",
                                "description", "la vista, come la chiama lo stato della tavola (V1, V2...) o per nome. Predefinita la prima."),
                            "origin", O(
                                "type", "string",
                                "enum", A("bottom_left", "bottom_right", "top_left", "top_right"),
                                "description", "da quale angolo della faccia forata si misurano le posizioni. Predefinito bottom_left."),
                            "x_mm", O("type", "number", "description", "dove va l'angolo in alto a sinistra della tabella sul foglio, in mm. Predefinito 15."),
                            "y_mm", O("type", "number", "description", "idem in verticale. Predefinito 15 sotto il bordo alto del foglio."),
                            "tag_start", O("type", "string", "description", "la lettera dei tag. Predefinito «A».")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "flat_pattern_view",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary = "La vista dello SVILUPPO di una lamiera in tavola, misurata contro il pezzo.",
                    Description =
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
                        + "sviluppo del pezzo: in those cases I remove it, and say so only after "
                        + "reading the views of the drawing again; if it is still there, I ask you to "
                        + "remove it. I also refuse, before placing any view, when the flat pattern of the "
                        + "part does not unsuppress (or suppress again) while I measure its flat length: "
                        + "the part may then be left flattened, and the message says so.",
                    Precondition = "una tavola e, aperto, un pezzo di lamiera salvato su disco",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "model", O("type", "string", "description", "il percorso del pezzo di lamiera. Vuoto = quello della tavola."),
                            "configuration", O("type", "string", "description", "la configurazione del pezzo. Vuota = quella attiva."),
                            "x_mm", O("type", "number", "description", "dove va il centro della vista sul foglio, in mm. Vuoto = un posto libero fuori dalle viste e dal cartiglio."),
                            "y_mm", O("type", "number", "description", "idem in verticale."),
                            "hide_bends", O("type", "boolean", "description", "senza le linee di piega. Predefinito falso: chi piega le vuole.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "drawing_weld_symbol",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary = "Il simbolo di saldatura ISO sul giunto fra due corpi, in una vista.",
                    Description =
                        "Mette in tavola il SIMBOLO DI SALDATURA (ISO 2553) sul giunto fra due corpi "
                        + "di un saldato: la freccia sulla linea del giunto, il simbolo del cordone "
                        + "(angolo, V, mezza V, testa), la gola «a» a sinistra e, se la dai, la "
                        + "lunghezza a destra.\n\nIL GIUNTO LO TROVO IO: fra gli spigoli che la vista "
                        + "mostra, quello in cui un corpo tocca l'altro (il piu' lungo). Il simbolo "
                        + "si sposta fuori dal pezzo, dalla parte di «side», oltre le quote gia' "
                        + "messe.\n\nSI RILEGGE: simbolo, gola, lunghezza, «tutto attorno». The "
                        + "count of its texts is checked too: a code the library does not draw leaves "
                        + "one text (the throat) instead of two, and that symbol is refused. Il "
                        + "codice del simbolo e' quello della libreria di SOLIDWORKS («<WELD-FILL>» "
                        + "per l'angolo): misurato, con un codice sbagliato il simbolo nasce lo "
                        + "stesso, si rilegge uguale, e non disegna il triangolo.\n\nMI RIFIUTO se "
                        + "il pezzo ha un corpo solo, se nella vista non si vede nessun giunto, e se "
                        + "il simbolo nato non e' quello chiesto (in that case I remove it, and say "
                        + "so only after counting the weld symbols of the drawing again).",
                    Precondition = "una tavola con una vista di un saldato in cui si vede il giunto fra due corpi",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O("type", "string", "description", "la vista, come la chiama lo stato della tavola (V1, V2...) o per nome. Predefinita la prima."),
                            "variant", O("type", "string", "enum", A("fillet", "v", "half_v", "butt"), "description", "il cordone. Predefinito fillet."),
                            "throat_mm", O("type", "number", "description", "la gola del cordone in mm (ISO 2553, «a»). Da 0 a 50."),
                            "length_mm", O("type", "number", "description", "la lunghezza del cordone, a destra del simbolo. Vuota = tutto il giunto."),
                            "all_around", O("type", "boolean", "description", "il cerchio del cordone tutto attorno. Predefinito falso."),
                            "field_weld", O("type", "boolean", "description", "la bandierina della saldatura in opera (in cantiere). Predefinito falso."),
                            "both_sides", O("type", "boolean", "description", "il cordone dai due lati del giunto. Predefinito falso."),
                            "side", O("type", "string", "enum", A("top", "bottom", "left", "right"), "description", "da che parte della vista va il simbolo. Predefinito top.")),
                        "required", A("throat_mm")),
                },
                new OperationSpec
                {
                    Name = "drawing_bom",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary = "La distinta base di un assieme in tavola, confrontata coi componenti dell'assieme.",
                    Description =
                        "Mette in tavola la DISTINTA BASE dell'assieme che sta dietro una vista: numero di "
                        + "voce, componente, quantita'. Di serie il primo livello; «parts_only» o "
                        + "«indented» per le altre.\n\nLA CONFRONTO: l'assieme sa per conto suo quali "
                        + "componenti ha e quanti; le voci e la somma delle quantita' devono combaciare. Se non "
                        + "combaciano la tolgo e te lo dico.\n\nPoi la misuro e la metto in un posto libero "
                        + "del foglio, fuori dalle viste e dal cartiglio.\n\nMI RIFIUTO se dietro la vista non "
                        + "c'e' un assieme (per un saldato c'e' «drawing_cut_list») o se sul foglio non "
                        + "c'e' posto."
                        + "\n\nThe count on the assembly side follows how each sub-assembly shows its "
                        + "children in the BOM (ChildComponentDisplayInBOM): in a top-level table a "
                        + "sub-assembly that promotes its children is replaced by them; in a parts-only "
                        + "table a sub-assembly that hides its children is one item. The indented table "
                        + "is compared with the rows of the assembly tree, as SOLIDWORKS writes them "
                        + "(measured): siblings with the same file and configuration make one row with their "
                        + "count; under a sub-assembly row stand the children of ONE instance, one level "
                        + "deeper (a sub-assembly used 3 times with 2 B inside: its row says 3, the B row "
                        + "under it says 2); a sub-assembly that hides its children is one row with nothing "
                        + "under it, and one that promotes them has no row and its children stand at its "
                        + "level. «counted_as» in the answer says how the expected items were counted."
                        + "\n\nA LIGHTWEIGHT SUB-ASSEMBLY REFUSES: how it shows its children in a BOM is in "
                        + "its document, which is not loaded; resolve it and ask again. A component is "
                        + "suppressed by its load state (GetSuppression2: IsSuppressed answers true on a "
                        + "lightweight component, measured), and a sub-assembly is recognised by its file "
                        + "extension, so a lightweight one is not taken for a part. A component whose load "
                        + "state cannot be read refuses the step."
                        + "\n\nIN WHICH CONFIGURATION: the table is made for the configuration the view "
                        + "shows, and the assembly is read in that same one. When the assembly has another "
                        + "configuration active, I activate the view's for the reading and put the active "
                        + "one back, reading it again: the answer gives the view's configuration and "
                        + "«assembly_active_configuration», the active one after the reading. If the view's "
                        + "configuration cannot be activated, or the active one cannot be put back, I refuse "
                        + "before inserting the table and say where the assembly stayed. The exclusion from "
                        + "the BOM is read in the configuration of the assembly that holds each component.",
                    Precondition = "una tavola con una vista di un assieme salvato",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O("type", "string", "description", "la vista dell'assieme (V1, V2...) o per nome. Predefinita la prima."),
                            "variant", O("type", "string", "enum", A("top_level", "parts_only", "indented"), "description", "che distinta. Predefinito top_level."),
                            "template", O("type", "string", "description", "il modello di tabella. Vuoto = il primo della cartella dei modelli di distinta.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "balloons",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary = "La bollatura: i palloncini coi numeri di voce della distinta sui componenti di una vista, contati.",
                    Description =
                        "Fa la BOLLATURA: mette i palloncini sui componenti dell'assieme in una vista, col numero di voce della "
                        + "distinta base (AutoBalloon5). Li conta sulla vista prima e dopo, e ti dice i numeri "
                        + "scritti. Poi confronta le voci della distinta coi palloncini di TUTTA la tavola e ti "
                        + "dice quali voci restano senza (quel componente in questa vista non si vede: bolla "
                        + "un'altra vista). Se ogni voce ha gia' il suo palloncino altrove, non ne mette e "
                        + "risponde «niente da aggiungere».\n\nMI RIFIUTO se in tavola non c'e' ancora una distinta base (la bollatura "
                        + "porta il suo numero di voce: mettila prima con «drawing_bom») o se "
                        + "dietro la vista non c'e' un assieme."
                        + "\n\nIf the view cannot be selected, the step is refused before AutoBalloon5 "
                        + "runs. balloons_returned is the number of notes AutoBalloon5 returned (null "
                        + "when it returned no array), and balloons_count_note appears when it disagrees "
                        + "with the count in the view.",
                    Precondition = "una tavola con la distinta base e una vista dell'assieme",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O("type", "string", "description", "la vista (V1, V2...) o per nome. Predefinita la prima."),
                            "layout", O("type", "string", "enum", A("square", "circle", "above", "below"), "description", "come disporre i palloncini attorno alla vista. Predefinito square.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "surface_finish",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary = "Il simbolo di rugosita' (Ra in micrometri) su uno spigolo di bordo di una vista.",
                    Description =
                        "Mette il simbolo di RUGOSITA' (UNI EN ISO 1302) con la linea di richiamo sullo spigolo "
                        + "di bordo di una vista, dal «side» chiesto, e lo porta fuori dal pezzo. «process»: "
                        + "asportazione (la barretta, predefinito), qualsiasi, no_removal (il cerchietto)."
                        + "\n\nIl Ra si RILEGGE dal simbolo. Misurato: senza spostarlo, SOLIDWORKS mette il simbolo "
                        + "fuori cornice ignorando le coordinate; per questo la posizione si imposta e si rilegge."
                        + "\n\nMI RIFIUTO se Ra non sta fra 0 e 100 micrometri, se la vista non ha uno spigolo dritto "
                        + "su quel lato, e se il simbolo nato dice un Ra diverso.",
                    Precondition = "una tavola con una vista",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O("type", "string", "description", "la vista, V1, V2... o per nome. Predefinita la prima."),
                            "side", O(
                                "type", "string",
                                "enum", A("top", "bottom", "left", "right"),
                                "description", "su quale lato della vista sta lo spigolo a cui si attacca. Predefinito «top»."),
                            "ra_um", O("type", "number", "description", "la rugosita' Ra in micrometri, per esempio 3.2"),
                            "process", O(
                                "type", "string",
                                "enum", A("material_removal", "any", "no_removal"),
                                "description", "come si ottiene la superficie. Predefinito «material_removal».")),
                        "required", A("ra_um")),
                },
                new OperationSpec
                {
                    Name = "datum",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary = "Un riferimento (datum A, B...) su uno spigolo di bordo di una vista.",
                    Description =
                        "Mette un DATUM (UNI EN ISO 5459): il triangolo sullo spigolo di bordo della vista dal "
                        + "«side» chiesto, e la bandierina con la «letter» fuori dal pezzo, dalla stessa parte. "
                        + "E' il riferimento che le tolleranze geometriche citano.\n\nLa lettera si RILEGGE. "
                        + "Misurato: lasciato dove nasce, SOLIDWORKS mette la bandierina dall'altra parte dello "
                        + "spigolo e la linea attraversa il pezzo; per questo si sposta.\n\nMI RIFIUTO se la "
                        + "lettera c'e' gia' in tavola — due datum con la stessa lettera non danno errore e rendono "
                        + "ambigua ogni tolleranza — e se la vista non ha uno spigolo dritto su quel lato.",
                    Precondition = "una tavola con una vista",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O("type", "string", "description", "la vista, V1, V2... o per nome. Predefinita la prima."),
                            "side", O(
                                "type", "string",
                                "enum", A("top", "bottom", "left", "right"),
                                "description", "su quale lato della vista sta lo spigolo a cui si attacca. Predefinito «bottom»."),
                            "letter", O("type", "string", "description", "la lettera del riferimento, dalla A alla Z. Predefinita «A».")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "geometric_tolerance",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary = "Una cornice di tolleranza geometrica (planarita', perpendicolarita', posizione...) su uno spigolo.",
                    Description =
                        "Mette una CORNICE DI TOLLERANZA GEOMETRICA (UNI EN ISO 1101) con la freccia sullo spigolo "
                        + "di bordo della vista dal «side» chiesto: la «characteristic», l'ampiezza della zona in mm "
                        + "e i «references» (fino a tre lettere di datum, nell'ordine primario, secondario, "
                        + "terziario). «diameter» mette il Ø davanti al valore (una zona cilindrica, per la "
                        + "posizione di un asse).\n\nSimbolo, valore e riferimenti si RILEGGONO dalla cornice, "
                        + "and so does the Ø (diameter_read, from IGtol.GetFrameDiameterSymbols)."
                        + "\n\nMI RIFIUTO se una tolleranza di forma (rettilineita', planarita', circolarita', "
                        + "cilindricita') cita un datum, se una di orientamento o di posizione non ne cita nessuno, "
                        + "se un datum citato non c'e' in tavola (mettilo prima con «datum»), e se la "
                        + "cornice nata non dice cio' che si e' chiesto, Ø included: such a frame, or one "
                        + "that cannot be filled, is removed from the sheet, and said removed only after "
                        + "the frames of the drawing are counted again.",
                    Precondition = "una tavola con una vista, e i datum che si citano gia' messi",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O("type", "string", "description", "la vista, V1, V2... o per nome. Predefinita la prima."),
                            "side", O(
                                "type", "string",
                                "enum", A("top", "bottom", "left", "right"),
                                "description", "su quale lato della vista sta lo spigolo a cui si attacca. Predefinito «right»."),
                            "characteristic", O(
                                "type", "string",
                                "enum", A("straightness", "flatness", "circularity", "cylindricity", "profile_of_line",
                                          "profile_of_surface", "angularity", "perpendicularity", "parallelism",
                                          "position", "concentricity", "symmetry", "circular_runout",
                                          "total_runout"),
                                "description", "la caratteristica geometrica"),
                            "value_mm", O("type", "number", "description", "l'ampiezza della zona di tolleranza, in mm"),
                            "references", O(
                                "type", "array", "items", O("type", "string"),
                                "description", "le lettere dei datum, fino a tre, nell'ordine primario, secondario, terziario"),
                            "diameter", O("type", "boolean", "description", "zona cilindrica: il Ø davanti al valore")),
                        "required", A("characteristic", "value_mm")),
                },
                new OperationSpec
                {
                    Name = "note",
                    Family = "drawing",
                    Example = "{\"text\":\"TOLLERANZE NON INDICATE ISO 2768-mK\",\"x_mm\":20,\"y_mm\":250}",   // from `note_ends_up_on_the_sheet`
                    RequestedDocument = "drawing",
                    // ── PROVEN, AND ON WHAT ────────────────────────────
                    //
                    // That the note IS BORN, that it ends up on the SHEET and not inside
                    // a view, and that the text read back is the one asked for.
                    //
                    // It is not proven — and cannot be — on whether
                    // what is written is right: a note is free text,
                    // and «materiale S355» on an S275 part passes every
                    // count and every re-reading.
                    Tested = true,
                    Summary =
                        "Scrive una nota di testo sul foglio della tavola, e RILEGGE "
                        + "quello che c'e' scritto.",
                    Description =
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
                    Precondition = "una tavola aperta, con un foglio attivo",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "text", O(
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
                        "required", A("text")),
                },
                new OperationSpec
                {
                    Name = "section_view",
                    // Z-C13 (26/9/2026): a letter already used by a section or a
                    // detail is refused; the effective letter is read back.
                    Family = "drawing",
                    Example = "{\"label\":\"A\",\"offset_mm\":0}",   // from `section_cuts_the_part`
                    RequestedDocument = "drawing",
                    // ── PROVEN, AND ON WHAT ────────────────────────────
                    //
                    // That the section view IS BORN (the count of the views
                    // goes up), that it is of the right type, and that it has
                    // HATCHING — which is the only thing that tells a
                    // section inside the material from a cutting line
                    // fallen outside, which produces a valid and empty view.
                    //
                    // It is not proven — and cannot be — on whether
                    // the cut passes where it was needed: ten millimetres further
                    // on is just as valid and does not show the hole.
                    Tested = true,
                    Summary =
                        "Crea una vista di sezione su una tavola, e controlla che "
                        + "il taglio abbia incontrato materiale.",
                    Description =
                        "Taglia una vista della tavola e mette la sezione a fianco. "
                        + "E' la prima cosa che manca per una tavola d'officina vera: "
                        + "un pezzo con una cavita' interna, senza sezione, non si "
                        + "legge — le linee nascoste dicono che c'e' un vuoto, non che "
                        + "forma ha.\n\n"
                        + "LA LINEA DI TAGLIO LA TRACCIO IO, da parte a parte della "
                        + "vista: una linea che la sfiora da' una sezione vuota senza "
                        + "dire niente. Di serie taglio in mezzo; con «offset_mm» sposti il "
                        + "taglio, in millimetri **di foglio** — non del modello, "
                        + "perche' sul foglio la vista e' in scala.\n\n"
                        + "TI DICO SE E' NATA contando le viste prima e dopo, non "
                        + "perche' l'API ha risposto un oggetto: puo' rispondere senza "
                        + "aver fatto niente. The view I then read (hatch, letter) is the "
                        + "one the call returned; the last view of the sheet only when it "
                        + "returned none, and view_read_from says which. When the section "
                        + "is not born, or the cutting line cannot be selected, the line I "
                        + "drew is removed from the sheet, said removed only after the "
                        + "sheet sketch is counted again.\n\n"
                        + "E MI RIFIUTO SE NON C'E' TRATTEGGIO. Una sezione senza "
                        + "tratteggio e' una linea di taglio caduta fuori dal "
                        + "materiale: sul foglio resta una vista che sembra buona e non "
                        + "mostra niente, e nessun errore lo direbbe. Preferisco "
                        + "fallire e dirti dove ho tagliato.\n\n"
                        + "LA LETTERA E' UNA SOLA PER TAVOLA: se «label» e' gia' di "
                        + "un'altra sezione o di un dettaglio mi rifiuto e ti dico quelle "
                        + "usate — due «A-A» sembrano lo stesso taglio. La lettera che la "
                        + "vista ha davvero te la rileggo («label_reread»).\n\n"
                        + "QUELLO CHE NON POSSO SAPERE e' se quella e' la sezione che "
                        + "serviva. So dirti che il taglio ha incontrato materiale, non "
                        + "che passa per la cavita' che volevi mostrare.",
                    Precondition = "una tavola aperta con almeno una vista di modello",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O(
                                "type", "string",
                                "description", "quale vista sezionare, per nome. Senza, "
                                    + "la prima vista di modello del foglio."),
                            "orientation", O(
                                "type", "string",
                                "enum", A("vertical", "horizontal"),
                                "description", "come corre la linea di taglio. "
                                    + "Predefinito «vertical»."),
                            "offset_mm", O(
                                "type", "number",
                                "description", "di quanto spostare il taglio dal centro "
                                    + "della vista, in millimetri DI FOGLIO. Positivo "
                                    + "verso destra (o verso l'alto). Predefinito 0, "
                                    + "cioe' in mezzo."),
                            "label", O(
                                "type", "string",
                                "description", "la lettera della sezione: «A» da' "
                                    + "«Vista in sezione A-A». Predefinito «A»."),
                            "depth_mm", O(
                                "type", "number",
                                "description", "profondita' del taglio per una sezione "
                                    + "parziale. Predefinito 0, che taglia da parte a "
                                    + "parte.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "detail_view",
                    Family = "drawing",
                    Example = "{\"label\":\"B\",\"scale\":2,\"radius_mm\":8}",   // from `detail_enlarges`
                    RequestedDocument = "drawing",
                    // ── PROVEN, AND ON WHAT ────────────────────────────
                    //
                    // That the detail view IS BORN, that it is of the right
                    // type, and that the circle BECAME a
                    // detail circle on the starting view —
                    // which is what tells a true detail from a
                    // circle drawn by hand next to one more view.
                    //
                    // The hatching is NOT looked at: a detail cuts
                    // nothing, and the hatch there is zero (measured). Copying
                    // the check of the section would give an operation that
                    // always refuses.
                    Tested = true,
                    Summary =
                        "Crea una vista di dettaglio ingrandita su una tavola, e "
                        + "controlla che il cerchio sia diventato un dettaglio.",
                    Description =
                        "Ingrandisce un pezzo di una vista. E' l'altra meta' di cio' "
                        + "che manca a una tavola d'officina: la sezione fa vedere cosa "
                        + "c'e' dentro, il dettaglio fa vedere cio' che e' troppo "
                        + "piccolo per leggersi alla scala del disegno — una gola, uno "
                        + "smusso, un raccordo con la sua quota.\n\n"
                        + "IL CERCHIO LO TRACCIO IO, dentro la vista. Di serie al centro "
                        + "con raggio un quarto del lato minore; con «x_mm» e «y_mm» lo "
                        + "sposti e con «radius_mm» lo cambi, in millimetri **di foglio** "
                        + "— non del modello, perche' sul foglio la vista e' in scala.\n\n"
                        + "MI RIFIUTO SE IL CERCHIO SBORDA dalla vista: un cerchio fuori "
                        + "ingrandisce del vuoto, e il vuoto ingrandito e' comunque una "
                        + "vista che sembra buona.\n\n"
                        + "E TI DICO SE IL CERCHIO E' DIVENTATO UN DETTAGLIO, contando le "
                        + "circonferenze di dettaglio sulla vista di partenza prima e "
                        + "dopo. If it does not go up I refuse, and remove both the new view "
                        + "and the circle, each said removed only after a reread: left there, "
                        + "they would be a hand-drawn circle next to a view tied to nothing, "
                        + "and no error would say so. The view I read is the one the call "
                        + "returned (view_read_from); when the detail is not born, the "
                        + "circle I drew is removed.\n\n"
                        + "LA LETTERA E' UNA SOLA PER TAVOLA: se «label» e' gia' di una "
                        + "sezione o di un altro dettaglio mi rifiuto e ti dico quelle usate. "
                        + "La lettera vera te la rileggo («label_reread»).\n\n"
                        + "QUELLO CHE NON POSSO SAPERE e' se quello e' il particolare che "
                        + "serviva: un cerchio spostato di dieci millimetri ingrandisce un "
                        + "pezzo di piano liscio, ed e' altrettanto valido.",
                    Precondition = "una tavola aperta con almeno una vista di modello",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O(
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
                            "radius_mm", O(
                                "type", "number",
                                "description", "il raggio del cerchio, in millimetri di "
                                    + "foglio. Predefinito: un quarto del lato minore "
                                    + "della vista."),
                            "scale", O(
                                "type", "number",
                                "description", "quante volte ingrandire. Predefinito 2, "
                                    + "cioe' 2:1."),
                            "label", O(
                                "type", "string",
                                "description", "la lettera del dettaglio. Predefinito «B», "
                                    + "perche' «A» di solito se la prende la sezione.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "title_block",
                    Family = "checks",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "Dice quali proprieta' il cartiglio CHIEDE e quali MOSTRA. "
                        + "Non tocca niente.",
                    Description =
                        "Legge il CARTIGLIO del foglio attivo e ti dice due cose "
                        + "diverse: **quali proprieta' personalizzate chiede** e **quali "
                        + "sta mostrando**.\n\nPERCHE' SONO DUE COSE. Un cartiglio non "
                        + "contiene testo: contiene collegamenti, scritti "
                        + "`$PRP:\"Descrizione\"`. Riempirlo vuol dire scrivere quelle "
                        + "proprieta' con «set_property».\n\n**IL NOME VA COPIATO "
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
                    Precondition = "una tavola aperta",
                    Schema = O(
                        "type", "object",
                        "properties", O(),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "interference_check",
                    Family = "checks",
                    Example = "{\"touching_counts\":true}",   // from `touching_is_not_interpenetrating`
                    RequestedDocument = "assembly",
                    Tested = true,
                    Summary =
                        "Dice se i componenti dell'assieme si compenetrano, e di quanto "
                        + "volume. Non tocca niente.",
                    Description =
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
                        + "Se e' quello che ti interessa, chiedimelo con «touching_counts», "
                        + "e ti dico sempre quale delle due ho usato."
                        + "\n\nTHE COUNT AND THE LIST MUST AGREE. «how_many» is the count "
                        + "SOLIDWORKS gives; «interferences_read» is how many interferences its "
                        + "list actually held. When the two differ, «volume_total_mm3» is left "
                        + "out (a sum over part of the list would be smaller than the truth) and "
                        + "«what_is_not_known» says what was missing; a count of zero with "
                        + "interferences in the list is refused instead of answered «none»."
                        + "\n\nPOSSIBLE interferences (IsPossibleInterference) are counted apart: "
                        + "certain_interferences and possible_interferences, and the verdict counts "
                        + "only the certain ones in its alarm (both null when a flag could not be read). "
                        + "The count of components looked at excludes suppressed components "
                        + "(suppressed_components). The two settings echoed in the answer (touching "
                        + "faces, and interferences inside multibody parts) are the values SOLIDWORKS "
                        + "reads back; if one reads back different from the request, the check is "
                        + "refused."
                        + "\n\nNon modifica "
                        + "niente: si puo' chiedere anche con «measure».",
                    Precondition = "un assieme aperto con almeno due componenti",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "touching_counts", O(
                                "type", "boolean",
                                "description", "vero: due facce che si toccano esattamente contano come "
                                    + "interferenza. Serve per «questo albero entra in questo foro?». "
                                    + "Falso (predefinito): un coperchio appoggiato non e' un errore."),
                            "inside_parts", O(
                                "type", "boolean",
                                "description", "vero: cerca anche fra i corpi DENTRO lo stesso "
                                    + "componente multicorpo. Falso (predefinito): solo fra componenti "
                                    + "diversi.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "drawing_cut_list",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    // ── PROVEN SINCE 26/9/2026 (library 0.125.0) ──────────
                    //
                    // The table is born when the part is a WELDMENT with the
                    // automatic cut list — which `cut_list` now does. The
                    // text below is from 17/9: the cause was not the drawing,
                    // it was the part (`IsWeldment` false). Measured: frame
                    // 1000 x 800, table with 2 entries, 2 x 1000 and 2 x 800.
                    //
                    // ── (17/9) NOT PROVEN: the table is not born ───────────
                    //
                    // `IView.InsertWeldmentTable` returns `null`, and it is not a
                    // problem of this operation: **on this installation
                    // NO table is born in the drawing.** Measured on
                    // 17/9/2026 — even `IDrawingDoc.InsertTableAnnotation2`
                    // with three rows, three columns and no template returns null.
                    //
                    // Tried and all negative: empty table template and
                    // real template (`cut list.sldwldtbt`); anchor 0 and
                    // TopLeft; `UseAnchorPoint` true and false; empty
                    // configuration and the exact one of the view; all three views;
                    // view activated and selected; **four different drawing
                    // templates, three of which standard** (iso, din, ansi);
                    // document already active and reactivated with `ActivateDoc3`.
                    //
                    // It is the same block as `center_marks` (entry W2.11):
                    // two annotation APIs that have nothing to do with each other,
                    // both mute on the same drawing. See entry GT2
                    // of the TODO for where to restart.
                    Tested = true,
                    Summary =
                        "Mette in tavola la distinta di taglio del saldato, e la confronta "
                        + "con quella del pezzo.",
                    Description =
                        "Inserisce nella tavola la DISTINTA DI TAGLIO del saldato che sta "
                        + "dietro una vista: l'elenco delle barre con lunghezze e "
                        + "quantita'.\n\nE LA CONFRONTO. Il pezzo sa gia', per conto suo, "
                        + "quanti corpi ha in distinta; la tabella in tavola dice la sua. "
                        + "Sono **due letture indipendenti dello stesso fatto**: se non "
                        + "combaciano te lo dico e non scelgo a quale credere. Una tavola "
                        + "che dichiara una quantita' diversa dal modello manda in officina "
                        + "il numero sbagliato di barre, e nessuno dei due lati da' "
                        + "errore.\n\nDi solito vuol dire che il pezzo e' cambiato dopo "
                        + "l'ultimo «cut_list»: si rifa' quello e si rimette la "
                        + "tabella.\n\nLa colonna delle quantita' non la indovino per "
                        + "posizione: la chiedo al modello di tabella, prima per tipo e poi per "
                        + "nome della proprieta'. Se non la trovo il confronto non si fa, e la "
                        + "risposta lo dice in «comparison_done» (false) invece che in una frase "
                        + "in fondo.\n\nLE COLONNE E IL POSTO: "
                        + "tengo quelle che in officina servono per tagliare (numero, quantita', "
                        + "lunghezza, i due angoli, descrizione) e nascondo le altre — il modello "
                        + "di tabella di casa ne ha 17, e la tabella usciva dal foglio. «columns» "
                        + "ne sceglie altre per intestazione, o «all». Poi misuro la tabella e "
                        + "la metto in un posto libero dentro la cornice, fuori dalle viste e dal "
                        + "cartiglio; se non c'e' posto la tolgo e lo dico."
                        + "\n\nWHAT THE COMPARISON COUNTS: the bodies of the cut-list folders at the top "
                        + "of the part's tree, in the part's active configuration. The folders of a "
                        + "sub-weldment are not descended into, and the view's configuration is not "
                        + "activated for the count (finding M32, open): on a part with sub-weldments, or "
                        + "with the view on another configuration, a mismatch can be the count's and not "
                        + "the table's.",
                    Precondition = "una tavola con una vista di un saldato che ha gia' la distinta di taglio",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O(
                                "type", "string",
                                "description", "quale vista. Vuoto = la prima della tavola."),
                            "x_mm", O(
                                "type", "number",
                                "description", "dove mettere l'angolo in alto a sinistra della tabella. Vuoto = un posto libero che trovo io."),
                            "y_mm", O(
                                "type", "number",
                                "description", "idem, in verticale."),
                            "columns", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "le intestazioni delle colonne da tenere, o [\"all\"]. Vuoto = numero, quantita', lunghezza, angoli, descrizione."),
                            "configuration", O(
                                "type", "string",
                                "description", "which configuration of the part: the guide asks for the "
                                    + "name of the «As Welded» configuration. Passed as given. Empty: "
                                    + "passed empty and SOLIDWORKS picks it; which one has not been "
                                    + "measured, and it is not necessarily the view's (finding M32, open)."),
                            "template", O(
                                "type", "string",
                                "description", "il modello di tabella. Vuoto = quello dell'installazione.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "new_sheet",
                    Family = "drawing",
                    Example = "{\"name\":\"Assembly\"}",   // from `two_sheets_and_the_same_projection_angle`
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "Aggiunge un foglio alla tavola, con lo stesso diedro del primo.",
                    Description =
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
                    Precondition = "una tavola aperta",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "name", O(
                                "type", "string",
                                "description", "come chiamarlo. Vuoto = il primo «FoglioN» libero."),
                            "paper_size", O(
                                "type", "string",
                                "description", "A0, A1, A2, A3, A4, A4V. Vuoto = quello del foglio corrente."),
                            "scale_numerator", O(
                                "type", "number",
                                "description", "il primo numero della scala. Vuoto = quella del foglio corrente."),
                            "scale_denominator", O(
                                "type", "number",
                                "description", "il secondo numero della scala. 1:2 si scrive 1 e 2.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "activate_sheet",
                    Family = "drawing",
                    Example = "{\"name\":\"Sheet1\"}",   // from `two_sheets_and_the_same_projection_angle`
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "Mette davanti un foglio della tavola, per nome.",
                    Description =
                        "Attiva un FOGLIO della tavola: da li' in poi le viste e le "
                        + "annotazioni vanno su quello.\n\nSI CHIEDE PER NOME, non per "
                        + "numero. Un indice sarebbe l'occasione perfetta per lavorare sul "
                        + "foglio sbagliato senza nessun errore.\n\nNon credo alla "
                        + "risposta: dopo averlo attivato richiedo alla tavola quale "
                        + "foglio ha davanti, e se non e' quello che hai chiesto mi fermo "
                        + "-- altrimenti i passi dopo lavorerebbero altrove.\n\nI nomi "
                        + "dei fogli stanno in «inspect_part», sotto «sheets»."
                        + "\n\nMI RIFIUTO SE IL FOGLIO NON C'E', e ti elenco quelli che ci sono col "
                        + "loro nome. Su una tavola a piu' fogli sbagliare foglio non da' nessun "
                        + "errore: le viste finiscono su quello attivo, e in officina arriva un "
                        + "foglio con sopra roba di un altro.",
                    Precondition = "una tavola aperta con piu' di un foglio",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "name", O(
                                "type", "string",
                                "description", "il nome esatto del foglio, come lo riporta lo stato The name is the one SOLIDWORKS gives, in the language of its installation: the example is from an English one; copy the name from the state as it is.")),
                        "required", A("name")),
                },
                new OperationSpec
                {
                    Name = "save_document",
                    Family = "drawing",
                    RequestedDocument = "any",
                    Tested = true,
                    Summary =
                        "Salva il documento aperto.",
                    Description =
                        "Salva su disco il documento attivo — pezzo o tavola.\n\nServe anche "
                        + "come PRIMO PASSO di una tavola: una vista si aggancia al percorso "
                        + "del pezzo, quindi un pezzo mai salvato non si puo' mettere in "
                        + "tavola.\n\nIl file scritto si rilegge: percorso e dimensione stanno "
                        + "nelle misure."
                        + "\n\nThe answer carries save_warnings (the swFileSaveWarning_e names, or "
                        + "«none») and save_warnings_note when the model needed a rebuild, had "
                        + "rebuild errors, or had views needing an update. A failed save gives the "
                        + "error and warning names next to the raw codes.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "dove salvare. Se non lo dici si salva dov'era gia'; un documento "
                                    + "mai salvato senza percorso e' un errore.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "model_view",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "Una vista con nome: isometrica, fronte, sopra...",
                    Description =
                        "Aggiunge al foglio una VISTA CON NOME del pezzo, nel punto che "
                        + "dici.\n\nLa piu' utile e' l'ISOMETRICA: da sola fa capire il pezzo "
                        + "a chi guarda la tavola, e le tre viste standard da sole spesso "
                        + "no.\n\nx_mm e y_mm sono la posizione SUL FOGLIO, con l'origine in "
                        + "basso a sinistra: su un A3 (420x297) l'angolo in alto a destra e' "
                        + "intorno a 320, 220.",
                    Precondition = "una tavola aperta e il pezzo aperto accanto",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "view", O(
                                "type", "string",
                                "enum", A(
                                    "isometric",
                                    "dimetric",
                                    "trimetric",
                                    "front",
                                    "back",
                                    "top",
                                    "bottom",
                                    "right",
                                    "left"),
                                "description", "quale vista; isometric se non dici niente"),
                            "x_mm", O(
                                "type", "number",
                                "description", "posizione sul foglio da sinistra, in mm"),
                            "y_mm", O(
                                "type", "number",
                                "description", "posizione sul foglio dal basso, in mm"),
                            "model", O(
                                "type", "string",
                                "description", "il percorso del pezzo. Se non lo dici si usa il primo pezzo aperto.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "standard_views",
                    Family = "drawing",
                    RequestedDocument = "drawing",
                    Tested = true,
                    Summary =
                        "Le tre viste standard, in primo diedro.",
                    Description =
                        "Mette sul foglio le TRE VISTE STANDARD del pezzo, in PRIMO diedro "
                        + "— quello che si usa in Europa.\n\nIl terzo diedro non esiste in "
                        + "questo catalogo, di proposito: e' una chiamata gemella che non "
                        + "fallisce mai e produce una tavola con destra e sinistra "
                        + "scambiate. Nessuna misura se ne accorge, e il pezzo si taglia "
                        + "specchiato.\n\nLe viste si contano prima e dopo: se non ne nasce "
                        + "nessuna l'operazione fallisce, invece di lasciarti una tavola "
                        + "vuota che sembra riuscita."
                        + "\n\nCENTRE MARKS ON HOLES. After the views are made the drawing is rebuilt, "
                        + "and the answer gives centre_marks_after_rebuild: the marks SOLIDWORKS put "
                        + "(null when a view cannot be counted). A view made by this call that shows holes "
                        + "end-on and has no centre mark at all gets them from the bridge, through the same "
                        + "walk as the centre-mark check: one mark per hole, each counted on the sheet. "
                        + "centre_marks_added_by_bridge says how many; a view where SOLIDWORKS put some "
                        + "marks, and a view that was on the sheet before, are left as they are. When that happens the answer also gives "
                        + "holes_in_views_without_centre_marks and auto_centre_marks_option (the document "
                        + "option that inserts them, null when it cannot be read). A hole the bridge could "
                        + "not mark is counted in holes_left_without_centre_mark, with centre_marks_note "
                        + "saying what to do.",
                    Precondition = "una tavola aperta e il pezzo aperto accanto",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "model", O(
                                "type", "string",
                                "description", "il percorso del pezzo da mettere in vista. Se non lo dici si usa "
                                    + "il primo pezzo aperto. Lo trovi in «documents_open» nello stato.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "check_body_geometry",
                    Family = "checks",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Dice se i corpi sono solidi chiusi e sani. Non tocca il pezzo.",
                    Description =
                        "Guarda i CORPI del pezzo e dice se sono solidi chiusi e senza "
                        + "difetti.\n\nNon disegna e non modifica niente: e' una domanda, non "
                        + "un'operazione. Chiamala prima di consegnare un pezzo che verra' "
                        + "stampato o esportato.\n\nPer ogni corpo risponde: il tipo (solido, "
                        + "foglio, filo), quante facce ha, se e' chiuso, quante fessure e "
                        + "quanti difetti topologici, col NOME del difetto e non col numero. "
                        + "Poi un giudizio in una frase: «printable» vuol dire UN SOLO corpo "
                        + "solido, chiuso, senza difetti.\n\nUn corpo APERTO -- una superficie "
                        + "rimasta li' per sbaglio -- e' il caso che conta e quello che "
                        + "nessuno vede: non da' errore da nessuna parte, sembra un pezzo, e "
                        + "lo STL che ne esce non racchiude niente."
                        + "\n\nWITH «body_uid» THE VERDICT IS ABOUT THAT BODY ONLY: "
                        + "«printable» and «verdict» speak for it, and «verdict_about» says so "
                        + "(«body C2»; «part» when you ask for the whole part). «bodies_in_part» "
                        + "always counts every body of the part: a sound body of a part of two "
                        + "is still a part whose STL is two separate shells."
                        + "\n\nA body whose defect check (Check3) or gap count (Diagnose) cannot be read "
                        + "is not called sound: its defect list is null with defects_not_read, and the "
                        + "printable verdict is false with the reason."
                        + "\n\nATTENZIONE a cosa NON "
                        + "guarda: lo spessore delle pareti e le sovrasporgenze. Un guscio da "
                        + "tre decimi passa di qui senza una parola.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "body_uid", O(
                                "type", "string",
                                "description", "un corpo solo, per identificativo (per esempio «C2»). Se non lo "
                                    + "dici li guarda tutti. Gli identificativi stanno nello stato del "
                                    + "documento, dentro «bodies». With it the verdict speaks for that "
                                    + "body only; «bodies_in_part» still says how many bodies the part has. "
                                    + "The identifier is resolved through the document state and checked "
                                    + "against its signature (centre and box): if the part changed since "
                                    + "and it now names another body, the step is refused. Only the solid "
                                    + "bodies of the state have an identifier; to check sheet bodies too, "
                                    + "leave body_uid out.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "check_overhangs",
                    Family = "checks",
                    Example = "{\"threshold_deg\":70}",   // from `walls_at_sixty_degrees_do_not_print`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Dice cosa non si stampa senza supporti, e di quanto. Non tocca il "
                        + "pezzo.",
                    Description =
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
                        + "il pezzo o smussare.\n\nGUARDO ANCHE I CORPI NASCOSTI (quanti sono "
                        + "te lo dico in «hidden_bodies»). Una faccia curva che non si lascia "
                        + "tassellare non la posso guardare: la conto in «faces_not_analyzed», "
                        + "e se non ho trovato sporgenze altrove «prints_without_supports» "
                        + "e' null, non true.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "threshold_deg", O(
                                "type", "number",
                                "description", "oltre quanti gradi dalla verticale serve il supporto. "
                                    + "Predefinito 45, che e' il valore normale di una stampante a "
                                    + "filamento; 30-35 per stare larghi, 60 se la macchina raffredda "
                                    + "bene."),
                            "print_direction", O(
                                "type", "array",
                                "items", O("type", "number"),
                                "description", "il verso in cui cresce la stampa, tre numeri. Predefinito "
                                    + "[0, 0, 1]. Cambiarlo e' il modo di chiedere «e se lo stampassi "
                                    + "girato?» senza girare il pezzo."),
                            "body_uid", O(
                                "type", "string",
                                "description", "un corpo solo (per esempio «C2»). Se non lo dici li guardo "
                                    + "tutti.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "file_dependencies",
                    Family = "checks",
                    RequestedDocument = "any",
                    Tested = true,
                    Summary = "Da quali file dipende il documento, e quali mancano sul disco. Non tocca niente.",
                    Description =
                        "Un assieme (o una tavola) e' un grafo di file sul disco: ti dico da quali file "
                        + "dipende, a tutti i livelli, e per ognuno se c'e'. Un file che manca e' un "
                        + "componente che l'assieme mostra SOPPRESSO: prima di misurare o esportare va "
                        + "ritrovato o sostituito.\n\nSe manca lo dice il disco, non l'API: il parametro "
                        + "che promette di segnare i riferimenti rotti non li segna (misurato)."
                        + "\n\nThe paths are where each file was last saved (GetDependencies with "
                        + "Searchflag false): with the search rules, a component that is not loaded "
                        + "could come back as another file with the same name found in another folder, "
                        + "which exists, and a missing file would not be reported (measured, L12-8).",
                    Precondition = "un documento salvato",
                    Schema = O(
                        "type", "object",
                        "properties", O(),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "mass_properties",
                    Family = "checks",
                    RequestedDocument = "part/assembly",
                    Tested = true,
                    Summary =
                        "Volume, area, massa e baricentro del pezzo. Non tocca niente.",
                    Description =
                        "Risponde a «quanto pesa questo pezzo»: volume, area della "
                        + "superficie, massa, densita' e baricentro.\n\nLA MASSA SENZA "
                        + "MATERIALE NON E' ZERO, E' «NON SI SA»: se al pezzo non e' stato "
                        + "dato un materiale ti rispondo null e ti dico perche'. SOLIDWORKS "
                        + "risponderebbe lo stesso usando la densita' predefinita del modello "
                        + "-- di solito 1000 kg/m3, cioe' acqua -- e un pezzo d'acciaio che "
                        + "pesa come l'acqua e' un numero credibile e sbagliato di otto "
                        + "volte. Usa prima «apply_material».\n\nLa massa arriva anche "
                        + "RICALCOLATA a mano (volume per densita') accanto a quella di "
                        + "SOLIDWORKS: se le due divergono, una delle misure non e' nelle "
                        + "unita' che dichiara, ed e' il modo in cui un fattore mille si fa "
                        + "vedere invece di nascondersi."
                        + "\n\nSU UN ASSIEME peso l'assieme intero (i componenti soppressi no, e "
                        + "te li elenco) e controllo la massa contro la somma dei pezzi letti uno "
                        + "per uno: se non combaciano mi fermo. Se un pezzo non ha materiale, la "
                        + "massa e' null e ti dico quale."
                        + "\n\nWHICH COMPONENTS WEIGH ON AN ASSEMBLY (measured). Suppressed components do "
                        + "not weigh, and they are listed. ENVELOPES do not weigh: SOLIDWORKS leaves them out "
                        + "of the assembly mass, the sum of the parts leaves them out too, and the answer names "
                        + "them in «envelope_components». HIDDEN components weigh, as SOLIDWORKS weighs them, "
                        + "and are named in «hidden_components_weighed». LIGHTWEIGHT parts refuse the step: "
                        + "SOLIDWORKS would weigh them, but their material cannot be read while they are "
                        + "lightweight, so a part without material would weigh as water unseen, and the sum of "
                        + "the parts cannot read them. Resolve them in SOLIDWORKS, or pass "
                        + "«resolve_lightweight»: true and I resolve them first; they stay resolved, and "
                        + "«lightweight_resolved_on_request» says so. If one is still lightweight after that, I "
                        + "refuse. A component whose load state cannot be read refuses the step too, and so "
                        + "does one that is neither a part nor a sub-assembly by its file extension or its "
                        + "document."
                        + "\n\nWITH «body_uid», a body named from an earlier reading of the state is "
                        + "checked against its signature (centre and box): if the part changed since and "
                        + "the identifier now names another body, the step is refused instead of weighing "
                        + "the other one.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "body_uid", O(
                                "type", "string",
                                "description", "un corpo solo, per identificativo (per esempio «C2»). Se non lo "
                                    + "dici li pesa tutti insieme. A body named from an earlier reading of "
                                    + "the state is checked against its signature (centre and box): if the "
                                    + "part changed since and the identifier now names another body, the "
                                    + "step is refused instead of weighing the other one."),
                            "resolve_lightweight", O(
                                "type", "boolean",
                                "default", false,
                                "description", "assembly only: when some parts are lightweight, resolve them "
                                    + "first (ResolveAllLightWeightComponents) instead of refusing. Default false. "
                                    + "They stay resolved, and the answer says so in "
                                    + "«lightweight_resolved_on_request».")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "body_bounding_box",
                    Family = "checks",
                    RequestedDocument = "part/assembly",
                    Tested = true,
                    Summary =
                        "La scatola del pezzo in mm, corpo per corpo e tutta insieme.",
                    Description =
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
                        + "li elenco."
                        + "\n\nON AN ASSEMBLY, LIGHTWEIGHT PARTS ARE MEASURED without resolving them: "
                        + "GetBodies3 reads the bodies of a lightweight part (measured), and the answer names "
                        + "them in «lightweight_components_measured». Envelopes and hidden components are in "
                        + "the box too, named in «envelope_components» and «hidden_components». A component "
                        + "whose load state cannot be read refuses the step, and so does one that is neither a "
                        + "part nor a sub-assembly by its file extension or its document.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "body_uid", O(
                                "type", "string",
                                "description", "un corpo solo, per identificativo (per esempio «C2»). Se non lo "
                                    + "dici misura tutti i corpi e la scatola che li contiene. A body named "
                                    + "from an earlier reading of the state is checked against its "
                                    + "signature (centre and box) before it is measured: if the part "
                                    + "changed since and the identifier now names another body, the step "
                                    + "is refused instead of measuring the other one. The answer names the "
                                    + "body by the identifier asked.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "cosmetic_thread",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Dice «qui c'e' un filetto» su un perno o un foro gia' fatto. Non "
                        + "taglia niente.",
                    Description =
                        "Mette una FILETTATURA COSMETICA sul bordo tondo di un perno o di un "
                        + "foro: e' un'annotazione, non un taglio. Il volume del pezzo resta "
                        + "quello di prima, e se cambia fallisco -- vorrebbe dire che ha "
                        + "tagliato davvero.\n\nServe quando il filetto e' su un perno o su un "
                        + "foro fatto a mano: per un foro filettato guidato c'e' gia' "
                        + "«hole_wizard», che le misure le prende dalla norma.\n\nSI ATTACCA "
                        + "ALLO SPIGOLO, non alla faccia: il bordo circolare del cilindro o "
                        + "del foro.\n\nATTENZIONE ALLA DENOMINAZIONE: il testo che scrivi -- "
                        + "«M6x1» -- e' quello che finisce sulla tavola, PAROLA PER PAROLA. "
                        + "Questa chiamata di SOLIDWORKS ignora lo standard e la taglia: non "
                        + "c'e' nessuna tabella che controlli se «M6x1» e' giusto per un "
                        + "perno da 6. Se lo scrivi sbagliato, in officina arriva sbagliato.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "edge_uid", O(
                                "type", "string",
                                "description", "il bordo TONDO su cui attaccare il filetto, preso dallo stato "
                                    + "del documento (per esempio «E3»)."),
                            "designation", O(
                                "type", "string",
                                "description", "il testo del filetto, per esempio «M6x1». Finisce in tavola "
                                    + "com'e' scritto qui."),
                            "depth_mm", O(
                                "type", "number",
                                "description", "quanto e' profondo il filetto, in mm. Non serve se e' passante."),
                            "through_all", O(
                                "type", "boolean",
                                "description", "vero: il filetto va da parte a parte, e la profondita' non serve."),
                            "diameter_mm", O(
                                "type", "number",
                                "description", "il diametro, se vuoi che lo controlli: se non combacia con quello "
                                    + "dello spigolo te lo dico invece di filettare il bordo sbagliato. Se non "
                                    + "lo dici, lo misuro dallo spigolo.")),
                        "required", A("edge_uid", "designation")),
                },
                new OperationSpec
                {
                    Name = "variable_fillet",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Un raccordo che cambia raggio da un capo all'altro dello spigolo.",
                    Description =
                        "Arrotonda uno spigolo con un raggio che VARIA: grande da una parte, "
                        + "piccolo dall'altra.\n\nServe dove il pezzo deve incassare un carico "
                        + "in un punto e non ha posto nell'altro -- una staffa che si allarga "
                        + "verso l'attacco, il bordo di un coperchio che si assottiglia. Per "
                        + "un raccordo uguale da capo a fondo c'e' «fillet_edges».\n\nLo "
                        + "spigolo dev'essere aperto, con due estremi: su un cerchio -- il "
                        + "bordo di un foro -- non c'e' un «da» e un «a» fra cui variare."
                        + "\n\nIl raggio piu' grande deve starci sullo spigolo: se supera la "
                        + "meta' della sua lunghezza lo dico prima di provare. E dopo "
                        + "controllo che abbia tolto davvero del materiale: una feature che "
                        + "nasce senza togliere niente lascia lo spigolo vivo, e l'albero "
                        + "cresciuto per finta.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "edge_uid", O(
                                "type", "string",
                                "description", "lo spigolo da raccordare, preso dallo stato del documento "
                                    + "(per esempio «E7»)."),
                            "start_radius_mm", O(
                                "type", "number",
                                "description", "il raggio a un capo dello spigolo, in mm."),
                            "end_radius_mm", O(
                                "type", "number",
                                "description", "il raggio all'altro capo, in mm.")),
                        "required", A("edge_uid", "start_radius_mm", "end_radius_mm")),
                },
                new OperationSpec
                {
                    Name = "fillet_face",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Un raccordo appoggiato a due facce invece che a uno spigolo: la palla "
                        + "del raggio rotola toccandole tutte e due.",
                    Description =
                        "Arrotonda il passaggio fra DUE FACCE: la superficie che nasce e' "
                        + "quella lasciata da una palla del raggio chiesto che rotola "
                        + "appoggiata a tutte e due. Sullo spigolo fra due facce che si "
                        + "toccano da' lo stesso pezzo di «fillet_edges»; la differenza "
                        + "e' che non serve nominare uno spigolo, bastano le due facce."
                        + "\n\nSu uno spigolo ESTERNO toglie materiale, in un angolo interno "
                        + "ne aggiunge: nella risposta c'e' di quanto e' cambiato il volume "
                        + "e da che parte."
                        + "\n\nTHE HAND CALCULATION IS A CHECK, NOT A SIDE NOTE. When the two faces are "
                        + "flat, at 90 degrees and touch along an edge, the expected change is edge "
                        + "length x r^2 x (1 - pi/4), read before the fillet («expected_change_abs_mm3»). "
                        + "A volume change further from it than 0.05 mm3 (or one ten-thousandth of it, "
                        + "whichever is larger) is refused: the fillet is removed, the part is checked "
                        + "back, and the answer says so. On any other pair of faces the expected change "
                        + "is null and nothing is compared.\n\nMI RIFIUTO PRIMA DI "
                        + "PROVARE quando la stessa faccia e' in tutti e due gli insiemi "
                        + "(per i bordi di una faccia sola c'e' «fillet_edges»), e "
                        + "quando le due facce sono piane e PARALLELE: se guardano dalla "
                        + "stessa parte, o si danno le spalle come il davanti e il dietro di "
                        + "un blocco, nessuna palla le tocca tutte e due. Due pareti che si "
                        + "guardano (una cava) le lascio provare solo col raggio pari a meta' "
                        + "della distanza, e te lo dico.\n\nDOPO CONTROLLO: che la lavorazione "
                        + "nuova sia un raccordo, che il volume sia cambiato, che i corpi "
                        + "siano quelli di prima e che nessuna lavorazione sia andata in "
                        + "errore. Se una di queste non torna tolgo il raccordo, ricontrollo "
                        + "che il pezzo sia com'era e te lo dico. Se non nasce -- di solito e' "
                        + "il raggio, che non ci sta sulle facce -- lo dico e ti indico "
                        + "«fillet_edges».\n\nUna faccia per insieme; le facce si nominano "
                        + "con gli identificativi dello stato, non con un riferimento a un "
                        + "passo.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_1_uid", O(
                                "type", "string",
                                "description", "la prima faccia, presa dallo stato del documento "
                                    + "(per esempio «F1»)."),
                            "face_2_uid", O(
                                "type", "string",
                                "description", "la seconda faccia, diversa dalla prima (per esempio "
                                    + "«F3»). Due facce piane parallele non si raccordano."),
                            "radius_mm", O(
                                "type", "number",
                                "description", "il raggio del raccordo, in mm. Deve starci su tutte e "
                                    + "due le facce.")),
                        "required", A("face_1_uid", "face_2_uid", "radius_mm")),
                },
                new OperationSpec
                {
                    Name = "combine_bodies",
                    Family = "features",
                    Example = "{\"operation\":\"intersect\"}",   // from `intersect_two_cubes`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Unisce, sottrae o interseca due o piu' corpi dello stesso pezzo.",
                    Description =
                        "La booleana fra CORPI dello stesso pezzo: «union» li fonde in uno, "
                        + "«subtract» toglie gli altri dal principale, «intersect» tiene solo "
                        + "quello che hanno in comune.\n\nServe agli stampi e alle dime, e ai "
                        + "pezzi disegnati come piu' corpi e poi messi insieme. I corpi "
                        + "separati nascono con «move_bodies» e la copia.\n\nNELLA "
                        + "SOTTRAZIONE L'ORDINE CAMBIA IL PEZZO: il corpo principale e' "
                        + "quello che RESTA, gli altri sono quelli che gli vanno addosso. Se "
                        + "non dici quale, e' il primo, e te lo scrivo nella risposta.\n\nQuali "
                        + "corpi: gli identificativi dello stato (C1, C2...) con «_uid», oppure i "
                        + "corpi di un passo precedente di questo stesso piano con «_ref». Dopo un "
                        + "taglio gli identificativi letti prima non valgono piu', e un uid vecchio "
                        + "lo rifiuto invece di combinare un altro corpo.\n\nDopo "
                        + "l'unione conto i corpi: se sono ancora due, non si sono fusi -- di "
                        + "solito non si toccano davvero -- e fallisco invece di confermarti "
                        + "un pezzo diverso da quello che credi.\n\nDopo una booleana gli "
                        + "identificativi di corpi e facce NON valgono piu': rileggi il pezzo."
                        + "\n\nSE NON DICI QUALE BOOLEANA faccio «union», ed e' scritto "
                        + "nella risposta. «subtract» e «intersect» si chiedono per nome, "
                        + "perche' cambiano il pezzo in modo opposto.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "operation", O(
                                "type", "string",
                                "enum", A("union", "subtract", "intersect"),
                                "default", "union",
                                "description", "che booleana fare. Se non lo dici e' «union», "
                                    + "e te lo scrivo nella risposta."),
                            "main_body_uid", O(
                                "type", "string",
                                "description", "il corpo che RESTA (per esempio «C1»). Se non lo dici e' il "
                                    + "primo. Conta solo per «subtract», ma scriverlo non fa mai male."),
                            "main_body_ref", O(
                                "type", "object",
                                "description", "il corpo che RESTA, se lo crea un passo precedente di questo "
                                    + "stesso piano (per esempio dopo un taglio, che cambia gli "
                                    + "identificativi). Metti questo OPPURE main_body_uid, non tutti e "
                                    + "due. Il passo deve lasciare UN corpo solo.\n\nGeneri ammessi:\n- bodies: "
                                    + "i corpi toccati da quel passo",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "bodies"))),
                                "required", A(
                                    "step",
                                    "kind")),
                            "tool_bodies_uid", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "i corpi da unire, sottrarre o intersecare col principale. Se "
                                    + "non li dici, tutti gli altri."),
                            "tool_bodies_ref", O(
                                "type", "object",
                                "description", "i corpi da unire, sottrarre o intersecare, se li crea un passo "
                                    + "precedente di questo stesso piano. Metti questo OPPURE "
                                    + "tool_bodies_uid, non tutti e due.\n\nGeneri ammessi:\n- bodies: i corpi "
                                    + "toccati da quel passo, tutti insieme",
                                "properties", O(
                                    "step", O(
                                        "type", "integer",
                                        "description", "il numero d'ordine del passo che crea la geometria, contando da "
                                            + "1. Deve essere un passo PRECEDENTE a questo."),
                                    "kind", O(
                                        "type", "string",
                                        "enum", A(
                                            "bodies"))),
                                "required", A(
                                    "step",
                                    "kind"))),
                        // Z-G3: default «union», declared in the answer.
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "minimum_distance",
                    Family = "checks",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Quanto distano due facce o due spigoli, col controllo che il "
                        + "numero non sia smentito dai suoi stessi punti.",
                    Description =
                        "Misura la distanza PIU' CORTA fra due facce, due spigoli, o una "
                        + "faccia e uno spigolo. E' il modo di rispondere a «quanto materiale "
                        + "resta fra la cava della guarnizione e la faccia esterna»: due cose "
                        + "nominate e un numero, invece di un conto a mano sull'ingombro."
                        + "\n\nRisponde anche con i DUE PUNTI fra cui ha misurato, e con la "
                        + "loro distanza calcolata a parte: se il numero e i punti non "
                        + "tornano, la chiamata e' fallita e ha risposto lo stesso, e in quel "
                        + "caso fallisco invece di darti il numero.\n\nZero vuol dire che le "
                        + "due cose si toccano, e te lo dico.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "start_uid", O(
                                "type", "string",
                                "description", "identificativo della prima faccia o del primo spigolo, preso "
                                    + "dallo stato del documento (per esempio «F3» o «E7»)."),
                            "end_uid", O(
                                "type", "string",
                                "description", "identificativo della seconda.")),
                        "required", A("start_uid", "end_uid")),
                },
                new OperationSpec
                {
                    Name = "measure_between",
                    Family = "checks",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Lo strumento di misura: area, perimetro, distanza, angolo, "
                        + "parallelismo fra una o due entita'.",
                    Description =
                        "Misura UNA faccia o uno spigolo -- area, perimetro, lunghezza, "
                        + "raggio -- oppure DUE, e allora dice anche distanza, differenze sui "
                        + "tre assi, angolo, se sono parallele o perpendicolari.\n\nE' lo "
                        + "stesso strumento di misura che si usa a mano in SOLIDWORKS. Per la "
                        + "sola distanza minima fra due cose, «minimum_distance» e' piu' "
                        + "robusta: controlla il risultato coi punti.\n\nQuello che non si "
                        + "applica non torna: il perimetro di una faccia che non ne ha, il "
                        + "raggio di un piano. SOLIDWORKS li darebbe come -1, che e' un "
                        + "numero e non lo e'."
                        + "\n\nA distance to an arc or a circle is taken as «arc_measure» says: "
                        + "centre to centre (the default), between the nearest points or between "
                        + "the farthest points. The result says which one was used («arc_measure», "
                        + "read back from SOLIDWORKS) and «projection»: none. Measured on two holes "
                        + "D10 and D20 at 50 mm centres: centre 50, minimum 35 (the wall between "
                        + "them), maximum 65. I refuse any other value of «arc_measure».",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "uid", O(
                                "type", "string",
                                "description", "la faccia o lo spigolo da misurare, preso dallo stato del "
                                    + "documento."),
                            "uid2", O(
                                "type", "string",
                                "description", "la seconda, se vuoi misurare FRA due cose."),
                            "arc_measure", O(
                                "type", "string",
                                "enum", A("centre", "minimum", "maximum"),
                                "description", "how a distance to an arc or a circle is taken: centre = centre "
                                    + "to centre (the default), minimum = between the nearest points, maximum = "
                                    + "between the farthest points. The distance between the centres is always "
                                    + "given too, centre to centre.")),
                        "required", A("uid")),
                },
                new OperationSpec
                {
                    Name = "export_stl",
                    Family = "exports",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Scrive lo STL per la stampa 3D, in millimetri. Verifica il pezzo "
                        + "prima, e rifiuta se non e' un solido chiuso.",
                    Description =
                        "Scrive il pezzo come STL, il formato che gli slicer delle stampanti "
                        + "3D leggono.\n\nPRIMA DI SCRIVERE chiama «check_body_geometry» e "
                        + "SI RIFIUTA se il pezzo non e' un solo corpo solido chiuso senza "
                        + "difetti. Non e' prudenza: un STL non porta con se' niente che dica "
                        + "che il pezzo era a posto -- si apre, si affetta e si stampa lo "
                        + "stesso, e il buco si vede quando il pezzo e' in mano. Se sai quello "
                        + "che fai, «export_anyway»: true lo scrive lo stesso e te lo dice "
                        + "nella risposta.\n\nLE UNITA' SONO SEMPRE MILLIMETRI, imposte a mano "
                        + "prima di scrivere e rimesse com'erano dopo. Il file non le porta "
                        + "con se' e lo slicer assume i millimetri: esportato in pollici, il "
                        + "pezzo esce 25,4 volte piu' piccolo e nessun errore lo dice.\n\nDopo "
                        + "aver scritto RILEGGE IL FILE: quanti triangoli dice di avere e se "
                        + "la sua dimensione torna con quel numero. Un STL che non torna e' un "
                        + "file che si apre e non e' il pezzo."
                        + "\n\nThe answer carries save_warnings (the swFileSaveWarning_e names, or "
                        + "«none») and save_warnings_note when the model needed a rebuild or had "
                        + "rebuild errors; preferences_not_restored names any user preference that "
                        + "could not be put back. A failed save gives the error and warning names "
                        + "next to the raw codes.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "dove scrivere il file, estensione .stl compresa. Se non lo dici "
                                    + "si usa il percorso del pezzo con l'estensione cambiata, e allora il "
                                    + "pezzo dev'essere gia' stato salvato."),
                            "quality", O(
                                "type", "string",
                                "enum", A("fine", "coarse"),
                                "description", "«fine» (predefinito) fa piu' triangoli e segue meglio le "
                                    + "curve; «coarse» fa un file piu' piccolo e sfaccetta i tondi."),
                            "binary", O(
                                "type", "boolean",
                                "description", "vero (predefinito): file binario, molto piu' piccolo. Falso: "
                                    + "ASCII, leggibile ma dieci volte piu' grosso."),
                            "export_anyway", O(
                                "type", "boolean",
                                "description", "scrive il file anche se la verifica dice di no. Predefinito: "
                                    + "falso. Usalo solo sapendo che cosa non andava.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "export_step",
                    Family = "exports",
                    RequestedDocument = "part/assembly",
                    Tested = true,
                    Summary =
                        "Scrive lo STEP per chi deve lavorare il pezzo, e rilegge dal file "
                        + "quale protocollo e' uscito davvero.",
                    Description =
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
                        + "«reopen_and_compare» il file viene riaperto in SOLIDWORKS e il suo "
                        + "volume confrontato col pezzo: e' l'unica misura che dice che la "
                        + "GEOMETRIA e' sopravvissuta alla traduzione. Costa un'apertura di "
                        + "documento, quindi non e' il predefinito."
                        + "\n\nANCHE UN ASSIEME: lo scrivo intero e conto i solidi nel file contro "
                        + "quelli dei pezzi non soppressi. «reopen_and_compare» su un assieme lo "
                        + "rifiuto: rileggere lo STEP di un assieme apre una finestra e la chiamata "
                        + "non torna. An instance changed in the assembly (a cut: user bodies in "
                        + "GetBodies3's BodiesInfo) is a shape of its own and counts all its bodies; "
                        + "the other instances count once per file and configuration."
                        + " WHICH COMPONENTS THE FILE HOLDS (measured): HIDDEN components are not written "
                        + "by SOLIDWORKS, so they are left out of the expected count and named in "
                        + "«hidden_components_not_exported». LIGHTWEIGHT components are resolved by "
                        + "SOLIDWORKS to write the file and stay resolved; the count is made after the "
                        + "export, and «lightweight_resolved_by_the_export» says how many were resolved. "
                        + "Envelopes are written and counted."
                        + "\n\nAP242 IS WRITTEN BY SOLIDWORKS MBD, not by a protocol number: in "
                        + "SOLIDWORKS 2021 the protocol preference takes only 203 and 214, and 242 "
                        + "written there gives an AP203 file (measured). I write AP242 through "
                        + "PublishSTEP242File, with the part brought to the front. Without the MBD "
                        + "licence (the bench machine has none) I refuse, saying so; AP214 and AP203 "
                        + "are still available. With AP242 the path must end in .stp. When the file "
                        + "is written, its FILE_SCHEMA is read back as for the other two."
                        + "\n\nThe answer carries save_warnings (the swFileSaveWarning_e names, or "
                        + "«none»; null for AP242, whose call has no warnings mask) and "
                        + "save_warnings_note when the model needed a rebuild or had rebuild errors; "
                        + "preferences_not_restored names any user preference that could not be put "
                        + "back. A failed save gives the error and warning names next to the raw codes.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "where to write the file, extension included: .step or .stp for AP214 "
                                    + "and AP203, only .stp for AP242 (a .step path is refused before writing). "
                                    + "If you leave it out, the part's own path is used with the extension changed."),
                            "protocol", O(
                                "type", "string",
                                "enum", A("AP214", "AP203", "AP242"),
                                "description", "AP214 (the default) also carries the colours; AP203 is the oldest and "
                                    + "the most widely read. AP242 is not a protocol number: it is written only by "
                                    + "SOLIDWORKS MBD (PublishSTEP242File), needs the SOLIDWORKS MBD licence and a "
                                    + "path ending in .stp. Without the licence I refuse and say so, and a file "
                                    + "already at the path stays as it was."),
                            "reopen_and_compare", O(
                                "type", "boolean",
                                "description", "riapre il file appena scritto e confronta il volume col pezzo, "
                                    + "fallendo oltre un millesimo di scarto. Predefinito: falso, perche' "
                                    + "costa un'apertura di documento."
                                    + " The reopened file is closed by "
                                    + "reference, never by its title (with the default path its title is the part's own "
                                    + "stem); if it cannot be closed safely it stays open and the answer "
                                    + "says so. The document that was in front is brought back by its path.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "export_iges",
                    Family = "exports",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Scrive l'IGES, per i CAD e le macchine che lo STEP non lo leggono.",
                    Description =
                        "Scrive il pezzo come IGES. Serve quando dall'altra parte c'e' un CAD "
                        + "o una macchina che lo STEP non lo legge: e' un formato piu' vecchio "
                        + "e meno preciso, e fra i due lo STEP e' sempre la prima scelta."
                        + "\n\nCome lo STEP, non si rifiuta per piu' corpi o superfici, e la "
                        + "verifica del pezzo torna nella risposta.\n\nDopo aver scritto "
                        + "conta i record di parametri del file: un IGES con le sole "
                        + "intestazioni e niente dentro esiste, pesa e si apre."
                        + "\n\nThree IGES options of the SOLIDWORKS profile would add curves to "
                        + "the file or turn the solid into curves: «IGES wireframe (3D curves)», "
                        + "«Export 3D Curve features» and «Export sketch entities». I turn them "
                        + "off for the export and put them back as they were. After writing I "
                        + "also read the file's directory and count its solids (type 186) and "
                        + "surfaces: «solid_entities», «surface_entities», «entities». A file "
                        + "with neither, curves only, is refused: it opens, and it is not the part. "
                        + "After turning the three options off I read them again: "
                        + "«iges_options_not_off» lists any that stayed on or could not be read "
                        + "(empty is the proof they were off), and «iges_options_warning» says "
                        + "what to do."
                        + "\n\nThe answer carries save_warnings (the swFileSaveWarning_e names, or "
                        + "«none») and save_warnings_note when the model needed a rebuild or had "
                        + "rebuild errors; preferences_not_restored names any user preference that "
                        + "could not be put back. A failed save gives the error and warning names "
                        + "next to the raw codes.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "dove scrivere il file, estensione .iges o .igs compresa. Se non "
                                    + "lo dici si usa il percorso del pezzo con l'estensione cambiata."),
                            "representation", O(
                                "type", "string",
                                "enum", A("solids", "surfaces"),
                                "description", "«solids» (predefinito) scrive i corpi come BREP, ed e' quello "
                                    + "che serve a chi deve lavorare il pezzo; «surfaces» scrive facce "
                                    + "tagliate."),
                            "reopen_and_compare", O(
                                "type", "boolean",
                                "description", "riapre il file appena scritto e confronta il volume col pezzo. "
                                    + "Predefinito: falso."
                                    + " The reopened file is closed by "
                                    + "reference, never by its title (with the default path its title is the part's own "
                                    + "stem); if it cannot be closed safely it stays open and the answer "
                                    + "says so. The document that was in front is brought back by its path.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "weldment_profiles",
                    Family = "weldments",
                    Example = "{\"search\":\"TUBO STRUTTURALE QUADRO\"}",   // from `profiles_are_read_first`
                    RequestedDocument = "none",
                    Tested = true,
                    Summary =
                        "Elenca i profili della libreria e le loro misure, scritte come vanno "
                        + "copiate. Non tocca niente.",
                    Description =
                        "Dice quali profili ci sono -- tubolari, IPE, angolari, piatti -- e "
                        + "per ognuno quali misure, scritte esattamente come vanno copiate."
                        + "\n\nCHIAMAMI PRIMA di «structural_member» e «rectangular_frame». "
                        + "Loro vogliono «profile_path» e «configuration» ALLA LETTERA, e "
                        + "alla lettera vuol dire davvero: maiuscole, spazi e virgole contano. "
                        + "Una misura scritta quasi giusta NON DA' ERRORE -- SOLIDWORKS usa la "
                        + "configurazione attiva del profilo, il pezzo esce con una parete "
                        + "diversa da quella che credi, la ricostruzione e' pulita, e te ne "
                        + "accorgi in officina.\n\nLeggo i file senza aprirli: non apro "
                        + "documenti e non lascio niente in giro.\n\nSe la libreria e' grande "
                        + "te ne do i primi quaranta e te lo dico: con «search» e un pezzo del "
                        + "nome li restringi.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "search", O(
                                "type", "string",
                                "description", "un pezzo del nome del profilo, per restringere: per esempio "
                                    + "«tubo», «IPE», «angolare». Senza, li elenca tutti."),
                            "with_measurements", O(
                                "type", "boolean",
                                "description", "vero (predefinito) per avere anche le misure di ogni profilo. "
                                    + "Falso se ti basta sapere quali profili esistono: costa meno.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "mate",
                    Family = "assemblies",
                    RequestedDocument = "assembly",
                    Tested = true,
                    Summary =
                        "Vincola due componenti fra loro prendendo una faccia per uno. "
                        + "E' la differenza fra un assieme e un mucchio ordinato.",
                    Description =
                        "Mette un accoppiamento fra due componenti dell'assieme aperto.\n\n"
                        + "LE FACCE SI NOMINANO PER VERSO, non per identificativo: «la "
                        + "faccia in basso di C2 sulla faccia in alto di C1». I componenti "
                        + "sono quelli che «inspect_part» elenca come C1, C2, ... e quegli "
                        + "identificativi sono STABILI (al contrario di quelli delle facce di "
                        + "una parte, che si rinumerano a ogni lettura)."
                        + "\n\nTHE DIRECTIONS ARE THE ASSEMBLY'S: the faces come from the component's "
                        + "bodies in the part's own coordinates, and each normal is turned with the "
                        + "component's placement in the assembly (Transform2) before it is compared, so "
                        + "on a turned or mirrored component «up» is the face that looks up in the "
                        + "assembly. If no flat face looks that way I say so instead of taking one at "
                        + "random; if the placement of the component cannot be read, I refuse without "
                        + "touching anything.\n\nFra piu' facce nello stesso "
                        + "verso prendo LA PIU' GRANDE: su un coperchio o una piastra e' "
                        + "quella che vuoi.\n\nWHAT THE MATE BLOCKS IS MEASURED (Z-C14). SOLIDWORKS gives no "
                        + "remaining degrees of freedom from here, so before the mate and after it each "
                        + "non-fixed component of the pair is moved 5 mm along X, Y and Z and turned 5 degrees "
                        + "about X, Y and Z of the assembly, one move at a time with a rebuild after each: a "
                        + "direction the mates put back is blocked, one where the component stays is free. The "
                        + "answer gives, per component in the order of the two ids, blocked_directions_before and "
                        + "blocked_directions_after (TX, TY, TZ, RX, RY, RZ), degrees_removed and direction_test; a "
                        + "fixed component is not moved (fully blocked by definition), and neither is one inside a "
                        + "sub-assembly (unknown). After the test each component is set back where it started, "
                        + "and so is any other component the rebuilds carried along; a slide the rebuild leaves is "
                        + "reported in residual_drift_mm and in the answer, not hidden.\n\n"
                        + "A REDUNDANT MATE IS REFUSED: when the mate blocks nothing that was not already blocked, "
                        + "on a pair where one component is fixed or already fully blocked, I take out the mate I "
                        + "have just made (the object AddMate5 returned, never a name) and refuse, saying which "
                        + "directions were already blocked. SOLIDWORKS itself reports nothing wrong on such a mate "
                        + "(AddMate5 answers NoError, GetWhatsWrongCount stays 0): this test is the only place it "
                        + "shows. When a position cannot be set or read, a move comes back only in part (neither "
                        + "blocked nor free), the assembly does not rebuild cleanly (a "
                        + "feature or mate in error makes every rebuild answer false), or neither component is fixed "
                        + "or fully blocked, I say so and keep the mate: a mate is never called redundant from a "
                        + "reading that could not be made.\n\nTHE COST: 13 rebuilds per non-fixed component for each "
                        + "reading (two per direction and one to put it back), and two readings per mate: 26 for "
                        + "the usual pair of a fixed and a free component, 52 when both move. On a large assembly "
                        + "this takes a while.\n\nNon conto sulla risposta: conto gli accoppiamenti dei due "
                        + "componenti prima e dopo, e se non sono aumentati te lo dico invece "
                        + "di dichiarare fatto.\n\nIf one of the two faces cannot be selected, the "
                        + "step is refused and AddMate5 is not called.",
                    Precondition = "un assieme aperto con almeno due componenti",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "component_a", O(
                                "type", "string",
                                "description", "l'identificativo del primo componente, da "
                                    + "«inspect_part»: «C1», «C2», ..."),
                            "face_a", O(
                                "type", "string",
                                "description", "quale sua faccia, per verso: up, down, right, "
                                    + "left, front, back. Directions of the assembly: on a turned "
                                    + "component, the face that looks that way after the rotation."),
                            "component_b", O(
                                "type", "string",
                                "description", "l'identificativo del secondo componente."),
                            "face_b", O(
                                "type", "string",
                                "description", "quale sua faccia, per verso."),
                            "variant", O(
                                "type", "string",
                                "description", "coincidente (predefinito), parallelo, perpendicolare, "
                                    + "distanza. Concentrico e tangente li rifiuto: vogliono una "
                                    + "faccia curva, e le facce qui si scelgono per verso, che vale "
                                    + "solo per quelle piane."),
                            "distance_mm", O(
                                "type", "number",
                                "description", "solo per «distanza»: quanto stanno lontane. Senza, "
                                    + "mi rifiuto -- non c'e' nessun valore predefinito ragionevole."),
                            "alignment", O(
                                "type", "string",
                                "description", "«allineato» (predefinito) o «opposto». Se SOLIDWORKS "
                                    + "rifiuta per allineamento, prova l'altro: due facce che si "
                                    + "guardano vogliono «opposto».")),
                        "required", A("component_a", "face_a", "component_b", "face_b")),
                },
                new OperationSpec
                {
                    Name = "mirror_component",
                    Family = "assemblies",
                    RequestedDocument = "assembly",
                    Tested = true,
                    Example = "{\"component\":\"C2\",\"plane\":\"right\",\"mode\":\"instance\"}",   // from `right_bracket_and_left_bracket`
                    Summary = "Specchia un componente su un piano dell'assieme: la staffa destra e la sinistra.",
                    Description =
                        "Specchia un componente su un piano dell'assieme (frontale, superiore, destro). "
                        + "DEVI SCEGLIERE COME, perche' sono tre pezzi diversi:\n- «instance»: lo stesso "
                        + "file, specchiato. Giusto per un pezzo simmetrico.\n- «opposite_linked»: un file "
                        + "nuovo che SEGUE l'originale. Cambi la destra e cambia anche la sinistra.\n- "
                        + "«opposite_unlinked»: un file nuovo INDIPENDENTE. Correggi la destra e la sinistra "
                        + "resta com'era, senza che niente te lo dica.\n\nDopo lo verifico: il centro del "
                        + "pezzo nuovo contro lo specchio del centro dell'originale, e per gli opposti "
                        + "l'albero del file nuovo (legato o no). Il file nuovo prende il nome "
                        + "dell'originale col «suffix» (predefinito «_spec»), nella stessa cartella."
                        + "\n\nMI RIFIUTO se non mi dici «mode» (ti spiego i tre), se il piano non e' uno "
                        + "dei tre dell'assieme o se il componente non c'e' (ti dico quali ci sono)."
                        + "\n\nThe three planes are recognised by their geometry (the plane's normal "
                        + "through the origin), not by their place in the tree nor by their name: if no "
                        + "plane of the assembly has that normal through the origin, I refuse and say "
                        + "what I read."
                        + "\n\nFor an instance of the same file the orientation of the new instance is "
                        + "SET («orientation», default mirrored_x_mirrored_y) and then CHECKED on the "
                        + "axes of the instance (Transform2): the centre is the same in all four "
                        + "orientations and cannot tell them apart. The answer carries «orientation» "
                        + "and «new_axes» (x, y, z unit vectors). «orientation» with an opposite-hand "
                        + "mode is refused, and so is a value that is not one of the four."
                        + "\n\nA mirror born with a rebuild error (GetErrorCode2) is refused and removed. "
                        + "Whenever a refusal removes the mirror it made, it says so only after reading the "
                        + "tree again and not finding it there; otherwise it asks you to remove it.",
                    Precondition = "un assieme aperto con almeno un componente",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "component", O(
                                "type", "string",
                                "description", "l'identificativo del componente, dallo stato: «C1», «C2», ..."),
                            "orientation", O(
                                "type", "string",
                                "enum", A("mirrored_x_mirrored_y", "mirrored_and_flipped_x_mirrored_y",
                                    "mirrored_x_mirrored_and_flipped_y", "mirrored_and_flipped_x_mirrored_and_flipped_y"),
                                "description", "only for an instance of the same file: how the new instance is "
                                    + "turned, as SOLIDWORKS names it (swMirrorComponentOrientation2_e). Default "
                                    + "mirrored_x_mirrored_y, the SOLIDWORKS default. Refused with an opposite-hand "
                                    + "mode."),
                            "plane", O(
                                "type", "string",
                                "enum", A("front", "top", "right"),
                                "description", "il piano dell'assieme su cui specchiare."),
                            "mode", O(
                                "type", "string",
                                "enum", A("instance", "opposite_linked", "opposite_unlinked"),
                                "description", "instance (stesso file), opposite_linked (file nuovo che segue "
                                    + "l'originale), opposite_unlinked (file nuovo indipendente)."),
                            "suffix", O(
                                "type", "string",
                                "description", "per gli opposti: cosa aggiungere al nome del file. Predefinito «_spec».")),
                        "required", A("component", "plane", "mode")),
                },
                new OperationSpec
                {
                    Name = "fix_component",
                    Family = "assemblies",
                    RequestedDocument = "assembly",
                    Tested = true,
                    Example = "{\"component\":\"C2\",\"grounded\":true}",   // from `fixed_is_not_mated`
                    Summary = "Fissa o libera un componente dell'assieme, e lo rilegge.",
                    Description =
                        "Fissa un componente (non si muove piu', qualunque accoppiamento abbia) o "
                        + "lo libera (lo tengono solo i suoi accoppiamenti). E' l'unico stato di "
                        + "un componente che cambia come l'assieme si ricostruisce.\n\nFISSO NON "
                        + "VUOL DIRE ACCOPPIATO: il primo componente inserito SOLIDWORKS lo fissa "
                        + "da solo, e un assieme che sta fermo solo per quello non e' montato. "
                        + "Dopo averlo cambiato lo rileggo, e ti dico quanti accoppiamenti ha: un "
                        + "componente libero con zero accoppiamenti e' appoggiato, si muove."
                        + "\n\nMI RIFIUTO se non mi dici «grounded» (true o false: sono due cose "
                        + "opposte, non c'e' un verso d'ufficio) o se il componente non c'e' "
                        + "(ti dico quali ci sono)."
                        + "\n\nTOP-LEVEL COMPONENTS ONLY: a component inside a sub-assembly "
                        + "(«top_level»: false in the state) is refused, and the refusal names the "
                        + "sub-assembly that holds it and its uid; IsFixed and FixComponent work on the "
                        + "top level only. Open the sub-assembly to fix one of its parts, or fix the "
                        + "sub-assembly itself.",
                    Precondition = "un assieme aperto con almeno un componente",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "component", O(
                                "type", "string",
                                "description", "l'identificativo del componente, dallo stato: «C1», «C2», ..."),
                            "grounded", O(
                                "type", "boolean",
                                "description", "true per fissarlo, false per liberarlo.")),
                        "required", A("component", "grounded")),
                },
                new OperationSpec
                {
                    Name = "replace_component",
                    Family = "assemblies",
                    RequestedDocument = "assembly",
                    Tested = true,
                    Summary =
                        "Cambia il file di un componente gia' montato, e ti dice se i "
                        + "vincoli sono sopravvissuti SANI o solo sopravvissuti.",
                    Description =
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
                        + "quei vincoli e rimetterli con «mate».\n\nIL FILE NUOVO "
                        + "DEV'ESSERE GIA' APERTO in SOLIDWORKS, come per "
                        + "«insert_component»: copia il percorso da «documents_open».\n\n"
                        + "Se gli dai lo stesso file che c'e' gia' mi rifiuto: riuscirebbe e "
                        + "non cambierebbe niente, e tu crederesti di aver cambiato il "
                        + "pezzo."
                        + "\n\nTHE CONFIGURATION IS CHECKED, NOT YET CHOSEN. A «configuration» the new "
                        + "file does not have is refused before the swap, and nothing changes. The swap "
                        + "asks SOLIDWORKS to match the configuration of the old component with one of "
                        + "the new file (its «match name» choice); the choice that uses the named "
                        + "configuration waits for a probe that shows it opens no window. So after the "
                        + "swap I read back the configuration the new component references and give it "
                        + "in the answer («configuration»), and, when you asked for one, "
                        + "«configuration_as_asked»: false means SOLIDWORKS took another one, the verdict "
                        + "opens with a WARNING, and the swap stays (undo it if the size matters)."
                        + "\n\nTOP-LEVEL COMPONENTS ONLY: a component inside a sub-assembly is refused "
                        + "before the call (ReplaceComponents2 wants a top-level component). The swap "
                        + "counts as done only when a component took up the new file that was not on it "
                        + "before AND the chosen component no longer uses its old file: another component "
                        + "already mounted on the new file does not count. «took_its_place» names the "
                        + "component(s) that took it; the C1..Cn numbers follow the names, so read the "
                        + "state again before naming one by uid.",
                    Precondition = "un assieme aperto, e il file nuovo gia' aperto",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "component", O(
                                "type", "string",
                                "description", "l'identificativo del componente da sostituire, da "
                                    + "«inspect_part»: «C1», «C2», ..."),
                            "path", O(
                                "type", "string",
                                "description", "percorso completo del file NUOVO, gia' aperto in "
                                    + "SOLIDWORKS."),
                            "configuration", O(
                                "type", "string",
                                "description", "a configuration of the new file, whole name. Refused "
                                    + "before the swap if the file does not have it. SOLIDWORKS matches "
                                    + "the old component's configuration by name, so the one you get may "
                                    + "differ: the answer reads it back («configuration_as_asked»). "
                                    + "Empty: not checked; the guide says an empty name means the default "
                                    + "configuration of the new file."),
                            "all_instances", O(
                                "type", "boolean",
                                "description", "vero (predefinito): se lo stesso pezzo e' montato piu' "
                                    + "volte, le sostituisce tutte. Falso: solo quella.")),
                        "required", A("component", "path")),
                },
                new OperationSpec
                {
                    Name = "insert_component",
                    Family = "assemblies",
                    RequestedDocument = "assembly",
                    Tested = true,
                    Summary =
                        "Mette un pezzo gia' aperto dentro l'assieme aperto, e ti dice "
                        + "se e' vincolato o solo appoggiato li'.",
                    Description =
                        "Inserisce un componente in un assieme.\n\nQUELLO CHE DEVI SAPERE "
                        + "PRIMA DI TUTTO IL RESTO: inserire NON e' accoppiare. Il pezzo "
                        + "appare, l'albero si riempie, la ricostruzione e' pulita -- e il "
                        + "componente e' appoggiato li', libero. Quattro componenti liberi "
                        + "sembrano un assieme, si aprono, si ruotano, si esportano, e si "
                        + "montano sbagliati.\n\nPercio' ti rispondo con «grounded», "
                        + "«mates» e «resting». Non fisso piu' zero "
                        + "accoppiamenti vuol dire APPOGGIATO: il pezzo sta li' e basta, e "
                        + "finche' e' cosi' l'assieme non e' montato, e' un mucchio "
                        + "ordinato.\n\nQUELLO CHE NON TI SO DIRE, e te lo dico: **di "
                        + "quanto** un componente sia ancora libero. SOLIDWORKS non mi da' i "
                        + "gradi di liberta' residui da qui -- risponde «non disponibile» su "
                        + "ogni componente, fisso o libero che sia -- e un numero inventato "
                        + "sarebbe peggio di nessun numero. Quindi «resting: false» vuol "
                        + "dire «ha almeno un accoppiamento», NON «non si muove piu'». One "
                        + "coincident mate leaves three of the six directions free (measured), "
                        + "and «mate» says which directions each mate blocks.\n\nIL "
                        + "COMPONENTE DEV'ESSERE GIA' APERTO in SOLIDWORKS: io lo cerco per "
                        + "percorso, e su un file chiuso l'inserimento risponde «non fatto» "
                        + "senza dire perche'. I percorsi che puoi usare sono quelli in "
                        + "«documents_open» dello stato: copiali da li'."
                        + "\n\n«configuration» chooses the configuration of the part to mount. The name "
                        + "is checked against the configurations of the open file BEFORE inserting, "
                        + "compared whole (the part in angle brackets included): a name the file does "
                        + "not have is refused and nothing is inserted. Then SOLIDWORKS is asked for it "
                        + "(UseConfigForPartReferences true) and the configuration the new component "
                        + "really references is read back: if it is another one, the component is taken "
                        + "out again and the step is refused, and the refusal says whether the removal "
                        + "was confirmed by counting the components. Without a name the component comes "
                        + "in with the file's last saved configuration "
                        + "(swAddComponentConfigOptions_CurrentSelectedConfig), and the answer's "
                        + "«configuration» says which one.\n\nATTENZIONE SULLE "
                        + "COORDINATE, perche' i due numeri non tornano e non e' un errore: "
                        + "«x_mm, y_mm, z_mm» posizionano il **centro dell'ingombro**; "
                        + "«origin_mm», che ti rispondo io e che rileggerai nello stato, e' "
                        + "dove e' finita l'**origine** del pezzo. Se il pezzo ha l'origine su "
                        + "una faccia -- cioe' quasi sempre -- i due punti distano meta' "
                        + "ingombro. Chiedi z=30 su un pezzo spesso 4 e l'origine esce a "
                        + "z=30 ma y=-2: nessuno dei due numeri e' sbagliato, sono due cose "
                        + "diverse.\n\nNon conto sulla "
                        + "risposta dell'inserimento: conto i componenti prima e dopo, e se "
                        + "non sono aumentati di uno te lo dico invece di dichiarare fatto.",
                    Precondition = "un assieme aperto, e il componente da inserire gia' aperto",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "percorso completo del file da inserire, copiato da "
                                    + "«documents_open». Dev'essere gia' aperto in SOLIDWORKS."),
                            "x_mm", O(
                                "type", "number",
                                "description", "dove va il CENTRO dell'ingombro del componente, in mm. "
                                    + "Predefinito 0. Non e' un accoppiamento: e' solo dove "
                                    + "appoggiarlo. E NON e' dove finisce la sua origine: se il "
                                    + "pezzo ha l'origine su una faccia, l'origine si sposta di "
                                    + "meta' ingombro. Ti rispondo con «origin_mm» per questo."),
                            "y_mm", O(
                                "type", "number",
                                "description", "dove va il CENTRO dell'ingombro, in mm. Predefinito 0."),
                            "z_mm", O(
                                "type", "number",
                                "description", "dove va il CENTRO dell'ingombro, in mm. Predefinito 0."),
                            "configuration", O(
                                "type", "string",
                                "description", "the configuration of the part to mount, by its whole "
                                    + "name (the part in angle brackets included). A name the file does "
                                    + "not have is refused before inserting; the component comes in with "
                                    + "it, read back, or is taken out again. Empty: the file's last saved "
                                    + "configuration.")),
                        "required", A("path")),
                },
                new OperationSpec
                {
                    Name = "circular_sweep",
                    Family = "extrusions",
                    Example = "{\"path_mm\":[[0,0,0],[100,0,0]],\"diameter_mm\":20}",   // from `straight_pipe_area_per_length`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Un tubo pieno di diametro dato lungo un percorso di punti. Su un "
                        + "percorso dritto controllo che il volume sia area per lunghezza.",
                    Description =
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
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path_mm", O(
                                "type", "array",
                                "items", O("type", "array", "items", O("type", "number")),
                                "description", "i punti della strada, almeno due, ognuno [x, y, z] in mm. "
                                    + "Per esempio [[0,0,0], [100,0,0]] fa un tubo dritto lungo 100."),
                            "diameter_mm", O(
                                "type", "number",
                                "description", "il diametro del tubo, in mm.")),
                        "required", A("path_mm", "diameter_mm")),
                },
                new OperationSpec
                {
                    Name = "loft_two_circles",
                    Family = "extrusions",
                    Example = "{\"plane\":\"front\",\"diameter1_mm\":40,\"diameter2_mm\":20,\"distance_mm\":50}",   // from `loft_is_not_a_cone`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Una transizione fra due cerchi di diametro diverso, e ti dico di "
                        + "quanto si scosta dal tronco di cono.",
                    Description =
                        "Raccorda due sezioni tonde diverse: un riduttore, un imbocco, una "
                        + "transizione da un tubo grosso a uno sottile.\n\nUN LOFT FRA DUE "
                        + "CERCHI NON E' UN CONO, ed e' la cosa che vale la pena sapere. La "
                        + "sua superficie e' un'approssimazione a spline della conica: il "
                        + "volume si scosta dalla formula del tronco di cono di qualche "
                        + "millesimo di per cento, sempre in difetto. Ti do tutti e due i "
                        + "numeri e lo scarto, cosi' non insegui un difetto che non c'e'. Se "
                        + "ti serve il cono esatto, «revolve» su un trapezio lo fa."
                        + "\n\nI due cerchi stanno su piani paralleli a quello che scegli, "
                        + "alla distanza che dici. Puoi sfalsarli uno rispetto all'altro per "
                        + "fare una transizione obliqua.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A("front", "top", "right"),
                                "description", "il piano del primo cerchio. Predefinito «front»."),
                            "diameter1_mm", O(
                                "type", "number",
                                "description", "il diametro del primo cerchio, sul piano scelto."),
                            "diameter2_mm", O(
                                "type", "number",
                                "description", "il diametro del secondo."),
                            "distance_mm", O(
                                "type", "number",
                                "description", "how far the second circle is from the first, in mm. "
                                    + "Greater than zero: the second circle goes on the side the chosen "
                                    + "plane faces (+Z for frontale, +Y for superiore, +X for destro). A "
                                    + "negative or zero distance is refused."),
                            "offset1_mm", O(
                                "type", "number",
                                "description", "di quanto spostare di lato il primo cerchio. Predefinito 0."),
                            "offset2_mm", O(
                                "type", "number",
                                "description", "di quanto spostare di lato il secondo: serve per una "
                                    + "transizione obliqua. Predefinito 0.")),
                        "required", A("diameter1_mm", "diameter2_mm", "distance_mm")),
                },
                new OperationSpec
                {
                    Name = "sketch_picture",
                    Family = "references",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary = "Mette una foto o uno schizzo a mano, in scala, su un piano: da ricalcare.",
                    Description =
                        "Mette un'immagine (una foto del pezzo, uno schizzo con le quote a penna) in uno "
                        + "schizzo su un piano, IN SCALA: tu mi dici quanto e' larga davvero tutta "
                        + "l'immagine («width_mm» — una misura nota divisa per i suoi pixel, per la "
                        + "larghezza dell'immagine) e dove va l'angolo in basso a sinistra. Poi ricalchi "
                        + "sullo stesso piano con le operazioni di schizzo.\n\nLA SCALA LA VERIFICO: il "
                        + "primo e l'ultimo pixel della prima riga, nello schizzo, devono distare quanto "
                        + "dicono i millimetri per pixel, entro l'1%. Ti rispondo la regola per passare "
                        + "da un pixel ai millimetri.\n\nMI RIFIUTO per un PDF (non entra: convertilo in "
                        + "PNG), per un file che non c'e' o non e' un'immagine, e per una larghezza non "
                        + "positiva."
                        + "\n\nOver 2048 px SOLIDWORKS compresses the picture in a part, even with "
                        + "HighestResolution (measured: 4096 x 2048 is held as 2048 x 1024). «pixel» and "
                        + "«mm_per_pixel» are the FILE's, the pixels you count in the image: the scale is "
                        + "«width_mm» divided by the FILE's pixel width. «solidworks_pixels» is the map "
                        + "SOLIDWORKS holds, and «compression» says when the two differ. I also refuse when "
                        + "the file's pixel size cannot be read and SOLIDWORKS holds 2048 px or more on a "
                        + "side, and when the picture SOLIDWORKS holds does not have the file's shape. "
                        + "I also refuse when SetSize or SetOrigin answers false, or when the width, "
                        + "height or lower left corner read back is more than 0.01 mm from what was "
                        + "asked; the picture then stays in the sketch, to be removed.",
                    Precondition = "un pezzo aperto (anche vuoto)",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "il file dell'immagine: PNG, JPG, BMP, GIF o TIF."),
                            "plane", O(
                                "type", "string",
                                "enum", A("front", "top", "right"),
                                "description", "il piano su cui metterla."),
                            "width_mm", O(
                                "type", "number",
                                "description", "quanto e' larga davvero TUTTA l'immagine, in mm: la scala."),
                            "x_mm", O("type", "number", "description", "dove va l'angolo in basso a sinistra, x. Predefinito 0."),
                            "y_mm", O("type", "number", "description", "dove va l'angolo in basso a sinistra, y. Predefinito 0.")),
                        "required", A("path", "plane", "width_mm")),
                },
                new OperationSpec
                {
                    Name = "plane_three_points",
                    Family = "references",
                    Example = "{\"point1_mm\":[0,0,0],\"point2_mm\":[50,0,0],\"point3_mm\":[100,10,0]}",   // from `limit_three_points_in_a_row`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Un piano obliquo, dato per tre punti, e rileggo la sua normale per "
                        + "dirti che e' quello.",
                    Description =
                        "«create_plane» sa fare solo il piano SFALSATO da uno dei tre standard: "
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
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "point1_mm", O(
                                "type", "array",
                                "items", O("type", "number"),
                                "description", "il primo punto, tre numeri in mm: [x, y, z]."),
                            "point2_mm", O(
                                "type", "array",
                                "items", O("type", "number"),
                                "description", "il secondo punto."),
                            "point3_mm", O(
                                "type", "array",
                                "items", O("type", "number"),
                                "description", "il terzo punto. Non dev'essere sulla retta degli altri due.")),
                        "required", A("point1_mm", "point2_mm", "point3_mm")),
                },
                new OperationSpec
                {
                    Name = "base_flange",
                    Family = "sheet_metal",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Una lamiera piegata da un profilo aperto: spessore, raggio di piega, profondita'.",
                    Description =
                        "Fa una LAMIERA da un profilo aperto disegnato su un piano base: una L, "
                        + "una U, uno zeta. Ogni angolo del profilo diventa una piega col raggio "
                        + "chiesto.\n\nI PUNTI SONO LE FACCE INTERNE quando il profilo va nel verso "
                        + "in cui lo spessore resta a DESTRA: misurato su una L da 50 + 30 data "
                        + "dall'alto verso destra, lo sviluppo esce 80,712 mm, uguale al conto a "
                        + "mano sulle facce interne. Dato al contrario, lo spessore va dall'altra "
                        + "parte e i punti diventano le facce ESTERNE: la risposta dice sempre "
                        + "quale delle due («points_are_faces»), perche' fra le due passano "
                        + "2 volte lo spessore per piega.\n\nLa risposta porta anche lo SVILUPPO: "
                        + "la lunghezza del pezzo spianato misurata da SOLIDWORKS, accanto a "
                        + "quella calcolata a mano col fattore K riletto dal pezzo. Se non "
                        + "combaciano lo dice, prima che il DXF vada al taglio.\n\nRifiuta un "
                        + "tratto piu' corto delle pieghe che lo mangiano (un lato da 1 mm con un "
                        + "raggio da 2): SOLIDWORKS darebbe una lavorazione rossa senza dire quale "
                        + "lato. E rifiuta una flangia che nasce in errore o che aggiunge meno "
                        + "della meta' del materiale del conto a mano."
                        + "\n\nIf the flat pattern does not unsuppress (or suppress again) when I "
                        + "measure the flat length, the answer says so in flat_pattern_problem (the "
                        + "part may be left flattened).",
                    Precondition = "una parte senza corpi, o a cui aggiungere una lamiera",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A("front", "top", "right"),
                                "description", "il piano base su cui sta il profilo. Predefinito «front»."),
                            "points", O(
                                "type", "array",
                                "items", O("type", "array", "items", O("type", "number")),
                                "description", "il profilo APERTO, almeno due punti [x, y] in mm sul piano. "
                                    + "Una L: [[0,30],[0,0],[50,0]]."),
                            "thickness_mm", O("type", "number", "description", "lo spessore della lamiera"),
                            "radius_mm", O("type", "number", "description", "il raggio INTERNO delle pieghe"),
                            "depth_mm", O("type", "number", "description", "quanto e' profonda la lamiera, perpendicolare al piano")),
                        "required", A("points", "thickness_mm", "radius_mm", "depth_mm")),
                },
                new OperationSpec
                {
                    Name = "edge_flange",
                    Family = "sheet_metal",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Una piega nuova su uno spigolo di bordo di una lamiera che c'e' gia'.",
                    Description =
                        "Aggiunge una FLANGIA a 90 gradi su uno spigolo di bordo di una lamiera "
                        + "(fatta con «base_flange»): e' cosi' che si chiude una scatola, con pieghe "
                        + "su spigoli di direzioni diverse.\n\nLo spigolo e' quello fra la faccia "
                        + "larga della lamiera e una faccia di TESTA, larga quanto lo spessore; la "
                        + "flangia si alza dalla parte verso cui guarda la faccia larga. "
                        + "«length_mm» e' misurata allo SPIGOLO VIRTUALE ESTERNO, e il materiale "
                        + "resta dentro l'ingombro di prima: il lato su cui pieghi si accorcia di "
                        + "raggio + spessore. Spessore, raggio e fattore K sono quelli della "
                        + "lamiera, riletti.\n\nLa risposta porta il volume aggiunto accanto al "
                        + "conto a mano (lo sviluppo in piu', (L - R - T) + BA - (R + T), per lo "
                        + "spessore e la lunghezza dello spigolo), e la lunghezza riletta dalla "
                        + "flangia.\n\nMI RIFIUTO se la lunghezza non lascia niente di dritto "
                        + "(non oltre raggio + spessore), se lo spigolo non e' dritto o non sta sul "
                        + "bordo della lamiera, e se la flangia nasce senza aggiungere materiale."
                        + "\n\nIf the flat pattern does not unsuppress (or suppress again) when I "
                        + "measure the flat length, the answer says so in flat_pattern_problem (the "
                        + "part may be left flattened).",
                    Precondition = "una parte di lamiera",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "edge_uid", O(
                                "type", "string",
                                "description", "lo spigolo di bordo su cui piegare, dallo stato del documento (E1, E2...)"),
                            "length_mm", O(
                                "type", "number",
                                "description", "quanto e' lunga la flangia, misurata allo spigolo virtuale esterno")),
                        "required", A("edge_uid", "length_mm")),
                },
                new OperationSpec
                {
                    Name = "hem",
                    Family = "sheet_metal",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Il bordo di una lamiera ripiegato su se stesso, chiuso o aperto.",
                    Description =
                        "Ripiega su se stesso il bordo di una lamiera (fatta con «base_flange»): "
                        + "l'orlo che toglie il filo tagliente e irrigidisce il bordo. «closed» e' "
                        + "schiacciato sulla lamiera; «open» lascia «gap_mm» fra il bordo e la "
                        + "lamiera. Resta dentro l'ingombro di prima.\n\nLo spigolo e' quello fra la "
                        + "faccia larga e una faccia di testa, larga quanto lo spessore. La risposta "
                        + "porta il volume aggiunto accanto al conto a mano: per l'aperto e' la piega "
                        + "a 180 gradi col raggio interno pari a meta' del gioco, misurata tre volte "
                        + "al millesimo; per il chiuso il conto usa un raggio di 0,05 mm, misurato "
                        + "con spessore 2, e si dice ma non decide.\n\nMI RIFIUTO se l'orlo non "
                        + "lascia niente di dritto, se «open» non ha gioco, se lo spigolo non e' "
                        + "dritto o non sta sul bordo, e se l'orlo nasce senza aggiungere materiale."
                        + "\n\nIf the flat pattern does not unsuppress (or suppress again) when I "
                        + "measure the flat length, the answer says so in flat_pattern_problem (the "
                        + "part may be left flattened).",
                    Precondition = "una parte di lamiera",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "edge_uid", O(
                                "type", "string",
                                "description", "lo spigolo di bordo da ripiegare, dallo stato del documento (E1, E2...)"),
                            "variant", O(
                                "type", "string",
                                "enum", A("closed", "open"),
                                "description", "closed (schiacciato) o open (con gioco). Predefinito «closed»."),
                            "length_mm", O(
                                "type", "number",
                                "description", "quanto e' lungo il bordo ripiegato"),
                            "gap_mm", O(
                                "type", "number",
                                "description", "solo per «open»: lo spazio fra il bordo ripiegato e la lamiera")),
                        "required", A("edge_uid", "length_mm")),
                },
                new OperationSpec
                {
                    Name = "sketched_bend",
                    Family = "sheet_metal",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Piega una lamiera lungo una linea tracciata su una sua faccia.",
                    Description =
                        "Piega una lamiera che c'e' gia' lungo una LINEA tracciata su una sua "
                        + "faccia piana, dall'estremo «start_mm» a «end_mm» (punti [x, y, z] in mm sul "
                        + "piano della faccia). La linea attraversa la faccia da bordo a bordo.\n\n"
                        + "La lamiera si piega verso la parte a cui GUARDA la faccia: per piegare "
                        + "dall'altra parte, traccia la linea sull'altra faccia. Il raggio e' quello "
                        + "della lamiera; il lato che resta fermo lo sceglie SOLIDWORKS, e la "
                        + "risposta porta l'ingombro prima e dopo.\n\nUna piega non aggiunge "
                        + "lamiera: volume e sviluppo restano quelli di prima, e se cambiano la "
                        + "risposta lo dice.\n\nMI RIFIUTO se l'angolo non sta fra 0 e 180 gradi "
                        + "esclusi, se la faccia non e' piana, se un estremo della linea non sta "
                        + "sulla faccia, e se la piega nasce senza piegare niente."
                        + "\n\nIf the flat pattern does not unsuppress (or suppress again) when I "
                        + "measure the flat length, the answer says so in flat_pattern_problem (the "
                        + "part may be left flattened), and before bending I refuse if that happens.",
                    Precondition = "una parte di lamiera",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "face_uid", O(
                                "type", "string",
                                "description", "la faccia piana su cui si traccia la linea, dallo stato del documento (F1, F2...)"),
                            "start_mm", O(
                                "type", "array", "items", O("type", "number"),
                                "description", "un estremo della linea di piega, [x, y, z] in mm sulla faccia"),
                            "end_mm", O(
                                "type", "array", "items", O("type", "number"),
                                "description", "l'altro estremo, [x, y, z] in mm sulla faccia"),
                            "angle_deg", O(
                                "type", "number",
                                "description", "l'angolo di piega. Predefinito 90.")),
                        "required", A("face_uid", "start_mm", "end_mm")),
                },
                new OperationSpec
                {
                    Name = "flat_pattern_dxf",
                    Family = "sheet_metal",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Il DXF dello sviluppo di una lamiera, per il taglio.",
                    Description =
                        "Scrive il pezzo di lamiera SPIANATO in un DXF: il disegno che va alla "
                        + "macchina di taglio.\n\nIL PEZZO DEV'ESSERE SALVATO: su un pezzo senza "
                        + "file SOLIDWORKS apre «Salva con nome» e resta ad aspettare che qualcuno "
                        + "clicchi — misurato. Si rifiuta prima di chiamare.\n\nIl DXF si RILEGGE: "
                        + "il suo ingombro deve avere per lato lungo lo sviluppo misurato sul "
                        + "pezzo, se no si rifiuta — un DXF di un'altra cosa si apre uguale. Senza "
                        + "«path» va accanto al pezzo, col suo nome."
                        + "\n\nIf the flat pattern does not unsuppress (or suppress again) when I "
                        + "measure the flat length, the answer says so in flat_pattern_problem (the "
                        + "part may be left flattened).",
                    Precondition = "una parte di lamiera, salvata",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "path", O(
                                "type", "string",
                                "description", "dove scrivere il .dxf. Predefinito: accanto al pezzo, col suo nome.")),
                        "required", A()),
                },
                new OperationSpec
                {
                    Name = "dimension_scheme",
                    Family = "dimensions",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Quota il pezzo nel modello con DimXpert, da tre facce di riferimento.",
                    Description =
                        "Mette sul PEZZO (non su una tavola) le quote che un disegnatore "
                        + "metterebbe: spessori, lati, diametri dei fori e la loro posizione "
                        + "dai riferimenti. E' DimXpert di SOLIDWORKS, dal nucleo, senza "
                        + "componenti aggiuntivi.\n\nI RIFERIMENTI SONO TRE FACCE PIANE, in "
                        + "ordine: la primaria (di solito quella d'appoggio), la secondaria e la "
                        + "terziaria (due fianchi perpendicolari). Da loro partono le quote di "
                        + "posizione. Con meno di tre rifiuta: DimXpert non rifiuterebbe, "
                        + "metterebbe una quota sola.\n\nLa risposta porta i VALORI delle quote "
                        + "(«values_mm»), letti dalle quote nate: e' il modo di vedere se ha "
                        + "quotato quello che serviva. La risposta di SOLIDWORKS alla chiamata "
                        + "(«auto_dimension_scheme_response») vale False anche quando ha "
                        + "quotato — misurato: non la si guarda.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "references_uid", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "TRE facce piane dallo stato del documento: primaria, secondaria, terziaria"),
                            "tolerance", O(
                                "type", "string",
                                "enum", A("plus_minus", "geometric"),
                                "description", "piu'/meno sulle quote (predefinito) o tolleranze geometriche")),
                        "required", A("references_uid")),
                },
                new OperationSpec
                {
                    Name = "draft",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Inclina delle facce di un angolo, rispetto a una faccia che resta "
                        + "ferma. Misura l'angolo sul pezzo, non sulla lavorazione.",
                    Description =
                        "L'angolo di spoglia: le pareti di un pezzo stampato in stampo devono "
                        + "pendere di un grado o due, o il pezzo non esce dalla forma. Serve "
                        + "anche alle fusioni e alle lavorazioni con angolo di spoglia."
                        + "\n\nDUE FACCE DIVERSE, E NON VANNO CONFUSE. La faccia NEUTRA e' "
                        + "quella che resta ferma: e' il piano da cui si misura l'angolo, e "
                        + "va in «neutral_face_uid». Le facce da inclinare vanno in "
                        + "«faces_uid». Se le scambi, SOLIDWORKS fa una lavorazione lo "
                        + "stesso, la chiama con lo stesso nome e non da' nessun errore -- "
                        + "ma ha sformato altre facce, di un'altra quantita'. Io me ne "
                        + "accorgo e ti fermo.\n\nCOME ME NE ACCORGO. La lavorazione, "
                        + "richiesta l'angolo, dichiara sempre quello che le hai chiesto -- "
                        + "anche quando non ha inclinato niente. Percio' conto le facce del "
                        + "pezzo che stanno davvero all'angolo giusto rispetto alla neutra, "
                        + "prima e dopo, e se non ne sono comparse abbastanza mi rifiuto."
                        + "\n\nIL VERSO. Con «flip» la parete pende dall'altra parte: il "
                        + "pezzo cresce invece di calare. Ti rispondo quanto materiale si e' "
                        + "mosso e da che parte, cosi' lo vedi senza guardare il pezzo di "
                        + "lato.\n\nSOLO FACCE PIANE: una faccia cilindrica sformata diventa "
                        + "conica e non ha un'inclinazione sola, quindi non la conto.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "neutral_face_uid", O(
                                "type", "string",
                                "description", "la faccia che RESTA FERMA, da cui si misura l'angolo (per "
                                    + "esempio «F1»). Di solito e' il fondo o la bocca dello stampo."),
                            "faces_uid", O(
                                "type", "array",
                                "items", O("type", "string"),
                                "description", "le facce da inclinare. Devono toccare la neutra."),
                            "angle_deg", O(
                                "type", "number",
                                "description", "di quanto inclinarle, in gradi. Per uno stampo 1-3 gradi; "
                                    + "per una fusione in sabbia anche 5."),
                            "flip", O(
                                "type", "boolean",
                                "description", "vero per far pendere le pareti dall'altra parte -- il pezzo "
                                    + "cresce invece di calare. Predefinito falso.")),
                        "required", A("neutral_face_uid", "faces_uid", "angle_deg")),
                },
                new OperationSpec
                {
                    Name = "rib",
                    Family = "features",
                    Example = "{\"plane\":\"front\",\"distance_mm\":25,\"start_mm\":[10,40,25],\"end_mm\":[40,10,25],\"thickness_mm\":6}",   // from `rib_in_the_corner_of_the_l`
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Una nervatura di irrigidimento: riempie, nel piano di una linea, lo spazio "
                        + "fra la linea e le pareti del pezzo. Misura il materiale aggiunto.",
                    Description =
                        "Irrigidisce una parete sottile: l'angolo di una staffa a L, il fondo di una "
                        + "scatola svuotata. Dai una LINEA con due estremi «start_mm» e «end_mm» (punti "
                        + "[x, y, z] in mm nel sistema del modello) che sta su un piano: uno dei tre "
                        + "base («plane»), eventualmente spostato parallelo a se stesso di «distance_mm» "
                        + "(il piano sfalsato lo creo io). La nervatura cresce NEL piano della linea, "
                        + "dalla linea fino alle pareti che la linea -- prolungata -- incontra, ed e' "
                        + "spessa «thickness_mm», divisi a meta' dalle due parti del piano.\n\n"
                        + "LA LINEA STA NEL VUOTO, con gli estremi (o il loro prolungamento) sulle pareti "
                        + "da unire. Esempio: L 60x60 spessa 10 e profonda 50, piano frontale a distanza "
                        + "25, linea da [10, 40, 25] a [40, 10, 25]: la nervatura e' il triangolo "
                        + "nell'angolo, 450 mm2 x 6 = 2700 mm3.\n\n"
                        + "IL VERSO DEL MATERIALE lo provo io: prima dritto, poi girato, e tengo il primo "
                        + "in cui la nervatura nasce e il pezzo cresce. Dipende dalla forma (nella L e' "
                        + "il dritto, nella scatola svuotata il girato) e la risposta dice quale ho "
                        + "usato e quanti tentativi. Se la linea sta fra pareti da tutte e due le parti "
                        + "vince il dritto.\n\n"
                        + "MI RIFIUTO se un estremo della linea non sta sul piano indicato (non la "
                        + "proietto in silenzio), se la linea e' piu' corta di un millimetro, e se la "
                        + "nervatura non nasce in nessuno dei due versi: di solito vuol dire che dalla "
                        + "linea non c'e' niente da incontrare, come su una piastra senza pareti (per un "
                        + "rilievo su una faccia piana c'e' «extrude_on_face»). Quando rifiuto tolgo "
                        + "lo schizzo e il piano che avevo fatto: il pezzo resta com'era.\n\n"
                        + "Non faccio nervature sformate e non guardo QUALI pareti la nervatura ha "
                        + "unito: misuro che e' nata, il materiale aggiunto e che il pezzo non si e' "
                        + "rotto.",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "plane", O(
                                "type", "string",
                                "enum", A(
                                    "front",
                                    "top",
                                    "right"),
                                "description", "il piano base parallelo a quello della nervatura: front "
                                    + "(XY), top (XZ) o right (YZ)"),
                            "distance_mm", O(
                                "type", "number",
                                "description", "di quanto il piano della nervatura e' spostato dal piano base, "
                                    + "lungo la sua normale (+Z, +Y, +X). Negativo per l'altro verso. "
                                    + "Predefinito 0: la nervatura sta sul piano base."),
                            "start_mm", O(
                                "type", "array", "items", O("type", "number"),
                                "description", "un estremo della linea, [x, y, z] in mm, sul piano della nervatura"),
                            "end_mm", O(
                                "type", "array", "items", O("type", "number"),
                                "description", "l'altro estremo, [x, y, z] in mm, sul piano della nervatura"),
                            "thickness_mm", O(
                                "type", "number",
                                "description", "lo spessore della nervatura, in mm, diviso a meta' dalle due "
                                    + "parti del piano. Per una staffa da 10 di solito 4-6.")),
                        "required", A("plane", "start_mm", "end_mm", "thickness_mm")),
                },
                new OperationSpec
                {
                    Name = "suppress_instance",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Toglie una o piu' copie da una ripetizione gia' fatta, e si "
                        + "rifiuta se la copia non c'era.",
                    Description =
                        "Una griglia di fori che ne salta uno dove passa una nervatura, una "
                        + "fila di asole che si ferma prima della piega. La ripetizione si fa "
                        + "prima, intera, con «linear_pattern» o «circular_pattern»; poi si "
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
                        + "tolta con «reactivate».\n\nNON TUTTE LE RIPETIZIONI SANNO FARLO: "
                        + "quelle guidate da uno schizzo, da una catena o da una quota non "
                        + "hanno le copie numerate. Uno specchio ha una copia sola: si toglie "
                        + "cancellandolo. A FILL pattern and a TABLE-driven pattern do skip "
                        + "instances, but a fill pattern skips them by (row, column) and a table "
                        + "pattern by the rows of its table: I do not translate a copy number into "
                        + "those, and I refuse them by name.\n\nDA UNA GRIGLIA A DUE DIREZIONI NON TOLGO COPIE "
                        + "(lineare, circolare o guidata da curva): SOLIDWORKS le numera riga per "
                        + "riga, e un numero solo non dice quale posizione. Mi rifiuto anche se "
                        + "dopo la modifica la ripetizione va in errore, resta senza facce, o "
                        + "perde facce che non valgono le copie tolte — e rimetto com'era. Per "
                        + "questo rifiuto anche le copie FUSE fra loro o col pezzo in modo da "
                        + "cambiare il numero di facce (per esempio due asole che si toccano): "
                        + "il conto delle facce non torna e non posso dire cosa ho tolto. Nella "
                        + "risposta «faces_per_copy» dice quante facce vale una copia (null se "
                        + "non l'ho potuto leggere: allora questo controllo non si e' fatto).",
                    Precondition = "solid_body",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "feature_uid", O(
                                "type", "string",
                                "description", "la RIPETIZIONE da cui togliere le copie (per esempio «L3»), non "
                                    + "la lavorazione ripetuta. Gli identificativi stanno nello stato "
                                    + "del documento, dentro «feature»."),
                            "copies", O(
                                "type", "array",
                                "items", O("type", "integer"),
                                "description", "i numeri delle copie da togliere, contando da 1 dopo "
                                    + "l'originale. Per esempio [2] toglie la seconda copia."),
                            "reactivate", O(
                                "type", "boolean",
                                "description", "vero per RIMETTERE copie tolte prima, invece di toglierne. "
                                    + "Predefinito falso.")),
                        "required", A("feature_uid", "copies")),
                },
                new OperationSpec
                {
                    Name = "engrave_text",
                    Family = "features",
                    RequestedDocument = "part",
                    Tested = true,
                    Summary =
                        "Incide (o fa in rilievo) una scritta su una faccia piana, e si "
                        + "rifiuta se la scritta non ci sta.",
                    Description =
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
                        + "dell'altezza -- raddoppiando «height_mm» l'area quadruplica -- ed "
                        + "e' il modo di controllare due marcature l'una con l'altra."
                        + "\n\nNON SU UNA FACCIA CURVA: il testo avvolto e' un'altra "
                        + "lavorazione, che non ho.",
                    Precondition = "solid_body_and_face",
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "text", O(
                                "type", "string",
                                "description", "la scritta, per esempio «SN-0042» o «ALTO»."),
                            "face_uid", O(
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
                            "height_mm", O(
                                "type", "number",
                                "description", "l'altezza delle lettere in mm. Predefinito 5."),
                            "depth_mm", O(
                                "type", "number",
                                "description", "quanto in profondita' incidere, o quanto alzare se «raised». "
                                    + "Predefinito 0,5 mm, che e' una marcatura da leggere. Per una "
                                    + "che si senta al tatto servono 0,3-0,8 mm."),
                            "raised", O(
                                "type", "boolean",
                                "description", "vero per una scritta in rilievo invece che incisa. Predefinito "
                                    + "falso. In rilievo si stampa meglio in 3D, incisa si legge "
                                    + "meglio su un pezzo lavorato."),
                            "font", O(
                                "type", "string",
                                "description", "il nome del carattere, per esempio «Arial». Se non lo dici uso "
                                    + "quello del documento. Un font che non c'e' su questo computer "
                                    + "cambia la larghezza della scritta."),
                            "bold", O(
                                "type", "boolean",
                                "description", "vero per il grassetto: lettere piu' spesse, che su una stampa "
                                    + "3D o una fusione si leggono meglio. Predefinito falso.")),
                        "required", A("text", "face_uid")),
                },
                new OperationSpec
                {
                    Name = "set_property",
                    Family = "identity",
                    Example = "{\"name\":\"NumeroDisegno\",\"value_text\":\"DIS-1234\"}",   // from `the_two_lists_do_not_see_each_other`
                    RequestedDocument = "any",
                    Tested = true,
                    Summary =
                        "Scrive una proprieta' personalizzata -- numero disegno, revisione, "
                        + "cliente -- sul documento oppure su una configurazione.",
                    Description =
                        "Scrive una proprieta' personalizzata: e' cio' che finisce nel "
                        + "cartiglio della tavola e nella scheda del PDM. «Numero disegno», "
                        + "«Revision», «Cliente», «Descrizione».\n\nIL DOCUMENTO E UNA "
                        + "CONFIGURAZIONE SONO DUE CASSETTI SEPARATI. Una proprieta' scritta "
                        + "sul documento da una configurazione NON SI VEDE, e viceversa: non "
                        + "si ereditano. Se dici «configuration» la scrivo li' e solo li'; "
                        + "se non la dici la scrivo sul documento e solo sul documento. Te lo "
                        + "ripeto nella risposta ogni volta, perche' e' l'errore che non da' "
                        + "errore: si scrive il numero di disegno sul documento, lo si "
                        + "rilegge dalla configurazione, non c'e', e nessuno dice perche'."
                        + "\n\nSE C'E' GIA', LA SOSTITUISCO. Non ti lascio credere di aver "
                        + "cambiato una revisione che invece e' rimasta quella di prima."
                        + "\n\nDOPO AVER SCRITTO RILEGGO. Il comando risponde un codice, non "
                        + "il valore: se rileggendo non trovo quello che ho scritto fallisco "
                        + "invece di confermarti una scheda dati che non e' quella che credi."
                        + "\n\nA value written as a formula (for example \"SW-Mass@part.SLDPRT\") is "
                        + "checked as the TEXT written, and resolved_value gives what SOLIDWORKS "
                        + "evaluates it to, when the two differ.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "name", O(
                                "type", "string",
                                "description", "il nome della proprieta', per esempio «Numero disegno» o "
                                    + "«Revision». Scrivilo come lo vuoi vedere nel cartiglio: il nome "
                                    + "e' quello che vedra' chi legge la scheda."),
                            "value_text", O(
                                "type", "string",
                                "description", "il valore da scrivere. Stringa vuota per svuotarla."),
                            "configuration", O(
                                "type", "string",
                                "description", "il nome della configurazione su cui scriverla. Se non lo dici "
                                    + "la scrivo sul DOCUMENTO, ed e' un posto diverso. I nomi stanno "
                                    + "nello stato del documento, dentro «configurations».")),
                        "required", A("name", "value_text")),
                },
                new OperationSpec
                {
                    Name = "create_configuration",
                    Family = "identity",
                    Example = "{\"name\":\"Left\",\"details\":\"staffa sinistra\"}",   // from `the_two_lists_do_not_see_each_other`
                    RequestedDocument = "part/assembly",
                    Tested = true,
                    Summary =
                        "Fa una variante del pezzo -- sinistra e destra, con e senza foro -- "
                        + "e la attiva subito.",
                    Description =
                        "Crea una configurazione: la stessa staffa sinistra e destra, con e "
                        + "senza foro, dentro un file solo. E' come un'officina a disegno "
                        + "chiede una famiglia di pezzi.\n\nLA CREA E LA ATTIVA. SOLIDWORKS "
                        + "sposta subito il documento sulla configurazione nuova, senza che "
                        + "glielo si chieda: tutto quello che disegni dopo, e tutto quello "
                        + "che lo stato ti racconta dopo, riguarda QUELLA. Te lo dico nella "
                        + "risposta. Per tornare a un'altra c'e' «activate_configuration»."
                        + "\n\nIL NOME CHE TI RISPONDO E' QUELLO DA USARE, e non e' detto che "
                        + "sia quello che mi hai chiesto: i nomi di SOLIDWORKS possono avere "
                        + "delle parentesi angolari dentro, e quelle fanno parte del nome. "
                        + "Riportalo com'e'."
                        + "\n\nMI RIFIUTO SE UNA CONFIGURAZIONE CON QUEL NOME C'E' GIA'. Due "
                        + "configurazioni omonime non sono un doppione innocuo: le lavorazioni "
                        + "soppresse in una e attive nell'altra fanno due pezzi diversi con lo "
                        + "stesso nome, e la distinta ne conta uno.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "name", O(
                                "type", "string",
                                "description", "il nome della configurazione, per esempio «Left» o "
                                    + "«Senza foro». Se ce n'e' gia' una cosi' mi rifiuto."),
                            "details", O(
                                "type", "string",
                                "description", "la descrizione che comparira' nell'elenco delle "
                                    + "configurazioni."),
                            "comment", O(
                                "type", "string",
                                "description", "un commento libero.")),
                        "required", A("name")),
                },
                new OperationSpec
                {
                    Name = "activate_configuration",
                    Family = "identity",
                    Example = "{\"name\":\"Left\"}",   // from `limit_configuration_that_already_exists`
                    RequestedDocument = "part/assembly",
                    Tested = true,
                    Summary =
                        "Passa a una configurazione gia' esistente, e verifica che ci sia "
                        + "davvero passato.",
                    Description =
                        "Sposta il documento su un'altra configurazione. Da qui in poi quello "
                        + "che disegni e quello che lo stato ti racconta riguardano quella."
                        + "\n\nIL NOME NON E' QUELLO CHE SEMBRA. La configurazione predefinita "
                        + "si chiama spesso «Default<A macchina>» e NON «Default»: quello fra "
                        + "parentesi angolari fa parte del nome. Chiedendo «Default» "
                        + "SOLIDWORKS risponde di no, in silenzio -- e spesso di "
                        + "configurazioni che cominciano per «Default» ce n'e' piu' d'una (la "
                        + "coppia dei saldati: «<A macchina>» e «<Saldato>»), per cui non c'e' "
                        + "nemmeno un modo giusto di indovinare. NON INDOVINO: copia il nome "
                        + "intero da «configurations» nello stato del documento.\n\nDOPO AVER "
                        + "CHIESTO CONTROLLO QUALE E' ATTIVA "
                        + "ADESSO: non mi basta che il comando abbia detto di si', perche' "
                        + "lavorare per mezz'ora sulla variante sbagliata non da' nessun "
                        + "errore."
                        + "\n\nMI RIFIUTO SE LA CONFIGURAZIONE NON C'E', e ti elenco quelle che ci "
                        + "sono. E il nome dev'essere INTERO: Default non basta se si chiama "
                        + "Default<Come lavorata>. Un nome parziale non lo indovino, perche' "
                        + "indovinare vorrebbe dire attivare la configurazione sbagliata in "
                        + "silenzio.",
                    Precondition = null,
                    Schema = O(
                        "type", "object",
                        "properties", O(
                            "name", O(
                                "type", "string",
                                "description", "il nome INTERO della configurazione da attivare, copiato da "
                                    + "«configurations» nello stato del documento. Suffisso fra "
                                    + "parentesi angolari compreso, se c'e'.")),
                        "required", A("name")),
                },
            };

        /// <summary>
        /// The `anchor` argument (entry E18), the same for the hole and the pocket. It is
        /// a method and not a field: the list above is initialised before the
        /// fields declared after it, and a field would still be null.
        /// </summary>
        private static Node AnchorSchema(string what, bool fromContour = false)
        {
            string point = fromContour
                ? "Il punto e' il CENTRO del contorno, e l'ancora vale per UN contorno solo, " +
                  "tondo o rettangolare allineato agli assi della faccia."
                : "x_mm/y_mm restano obbligatori e devono dire lo stesso punto.";
            return O(
                "type", "object",
                "description", "COSA TIENE " + what.ToUpperInvariant() + " DOV'E' QUANDO IL PEZZO CAMBIA. Senza, "
                    + "x_mm/y_mm sono coordinate assolute: se la piastra si allunga, " + what + " resta "
                    + "dov'era. Per OGNI asse, «x» e «y», tutti e due obbligatori, una di due cose: "
                    + "{\"edge_uid\": \"E7\", \"distance_mm\": 20}, la distanza dal CENTRO a uno "
                    + "spigolo dritto della stessa faccia che corre lungo l'altro asse; oppure "
                    + "{\"centered_between\": [\"E3\", \"E5\"]}, a meta' fra due spigoli opposti. " + point
                    + "\n\nMI RIFIUTO SE manca un asse; se x_mm/y_mm e l'ancora non dicono lo stesso "
                    + "punto; se lo spigolo non e' dritto, non sta sulla faccia, non corre lungo l'altro "
                    + "asse o sta sul bordo di uno smusso o di un raccordo (l'altra faccia dello "
                    + "spigolo dev'essere piana e perpendicolare, e se non riesco a guardarla "
                    + "rifiuto); se la faccia su cui lavori non e' piana; se la distanza e' meno di "
                    + "meta' della larghezza (il profilo scavalcherebbe lo spigolo); "
                    + "if, on a centred axis, the profile is wider than the gap between the two edges; "
                    + "se lo schizzo non "
                    + "risulta completamente definito, o non regge una modifica di prova — e allora il "
                    + "taglio lo tolgo. I also refuse when I cannot do those checks: relations of the "
                    + "sketch I cannot read, or an anchor dimension I cannot read or move (the test "
                    + "would pass without testing anything).",
                "properties", O(
                    "x", AxisOfAnchor("x"),
                    "y", AxisOfAnchor("y")),
                "required", A("x", "y"));
        }

        private static Node AxisOfAnchor(string axis)
        {
            string other = axis == "x" ? "y" : "x";
            return O(
                "type", "object",
                "description", "l'ancora lungo " + axis + "_mm: edge_uid + distance_mm, oppure centered_between",
                "properties", O(
                    "edge_uid", O(
                        "type", "string",
                        "description", "uno spigolo dritto della faccia, dallo stato, che corre lungo " + other + "_mm"),
                    "distance_mm", O(
                        "type", "number",
                        "description", "dal CENTRO del profilo allo spigolo, misurata lungo " + axis + "_mm"),
                    "centered_between", O(
                        "type", "array",
                        "items", O("type", "string"),
                        "description", "due spigoli opposti della faccia, dallo stato, che corrono lungo "
                            + other + "_mm: il centro resta a meta' fra loro")));
        }
    }
}
