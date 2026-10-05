using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oce.Solid.Agent.Mcp;
using Oce.Solid.Agent.Bridge;

namespace Oce.Solid.Agent.Tools
{
    /// <summary>
    /// I cinque strumenti che il ponte espone.
    ///
    /// PERCHE' CINQUE E NON TRENTOTTO.
    /// Le operazioni di disegno sono trentotto, e la tentazione e' di esporne
    /// una per strumento. Sarebbe sbagliato per due ragioni misurate nel
    /// progetto:
    ///
    ///   1. **il costo.** Gli schemi interi delle trentotto operazioni sono
    ///      decine di migliaia di caratteri in ogni singola richiesta. Il
    ///      catalogo a due livelli del server esiste esattamente per questo.
    ///   2. **l'ordine giusto.** Il difetto piu' caro di questo progetto non e'
    ///      un'operazione sbagliata: e' un'operazione giusta fatta ALLA CIECA.
    ///      Gli strumenti in fila — guarda, cerca, approfondisci, fai —
    ///      rendono difficile saltare il primo.
    ///
    /// `inspect_part` non e' un accessorio: e' il primo, e senza di lui gli
    /// identificativi che gli altri vogliono (`F1`, `E4`, `Corpo1`) non
    /// esistono.
    ///
    /// **IL QUINTO E' `undo`, ed e' arrivato dopo (voce R1).** Non e'
    /// un'operazione di disegno, e sta fuori dal catalogo per una ragione
    /// precisa: le operazioni si eseguono dentro `build`, cioe' dentro un
    /// piano, e annullare un piano da dentro il piano stesso e' un nodo. Il
    /// motivo per cui gli strumenti non sono trentotto — non esporre le
    /// operazioni una per una — non riguarda un gesto che operazione non e'.
    ///
    /// **Togliere una lavorazione invece SI'**, ed e' nel catalogo
    /// (`suppress_feature`, `delete_feature`, voce R2): quelle sono
    /// operazioni sul pezzo come tutte le altre, si scrivono dentro un piano e
    /// hanno senso in mezzo a una sequenza. La differenza con `undo` non e'
    /// «toglie» contro «aggiunge»: e' che `undo` disfa **un piano**, e un
    /// piano non e' geometria.
    ///
    /// Fino alla 0.31.0 questo ponte diceva **ventuno volte** «annullalo con
    /// Ctrl+Z» a un destinatario che non ha una tastiera.
    /// </summary>
    internal sealed class McpTools
    {
        private readonly Session _session;
        private readonly Catalog _catalog;

        /// <summary>
        /// Cosa e' stato chiesto e non c'era (voce Z-G7). Non e' un log:
        /// e' l'unico posto da cui si puo' sapere quale operazione
        /// costruire dopo, invece di sceglierla come abbiamo scelto le
        /// cinque senza casi di banco — a intuito.
        ///
        /// Non e' mai null: se non c'e' dove scrivere, tace da solo. Un
        /// `if (_registro != null)` sparso in quattro punti e' il modo in
        /// cui il quinto punto se lo dimentica.
        /// </summary>
        private readonly QuestionRegistry _registry;

        public McpTools(Session session, Catalog catalog)
            : this(session, catalog, new QuestionRegistry()) { }

        /// <summary>Con un registro dato: lo usano le prove, che non
        /// vogliono scrivere accanto all'eseguibile.</summary>
        public McpTools(Session session, Catalog catalog,
                        QuestionRegistry registry)
        {
            _session = session;
            _catalog = catalog;
            _registry = registry ?? new QuestionRegistry(null);
        }

        public object Items()
        {
            return new object[]
            {
                Tool(
                    "inspect_part",
                    "GUARDA il documento aperto in SolidWorks prima di toccarlo: " +
                    "ingombro, volume, corpi, lavorazioni con le loro quote, facce " +
                    "e spigoli con il loro identificativo. Gli identificativi (F1, " +
                    "E4, Corpo1) che le altre operazioni vogliono nascono QUI e " +
                    "valgono per questa lettura: dopo ogni modifica si rilegge.\n\n" +
                    "Con «views» torna anche una FOTOGRAFIA del pezzo, che e' " +
                    "l'unico modo di sapere che FORMA ha: l'elenco delle facce " +
                    "dice quante sono e quanto misurano, non com'e' fatto. " +
                    "Costa: ogni vista pesa piu' dell'intero stato scritto, " +
                    "quindi si chiede quando la forma e' in dubbio — dopo un " +
                    "guscio, dopo uno smusso, prima di dire che il pezzo e' " +
                    "finito — non a ogni lettura.",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["views"] = new Dictionary<string, object>
                            {
                                ["type"] = "array",
                                ["description"] =
                                    "quali viste fotografare, al massimo " +
                                    MaximumViews + ". Se non sai quale, chiedi " +
                                    "«isometric»: e' la sola che da sola mostra una " +
                                    "forma. Ci sono: " +
                                    string.Join(", ", Session.ViewNames) + ".",
                                ["items"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string",
                                    ["enum"] = Names(Session.ViewNames),
                                },
                            },
                            ["section"] = new Dictionary<string, object>
                            {
                                ["type"] = "object",
                                ["description"] =
                                    "UNO SPACCATO: il pezzo tagliato da un piano. Dice di un " +
                                    "guscio piu' di dieci viste esterne — dall'esterno una " +
                                    "scatola svuotata e una piena sono lo stesso " +
                                    "parallelepipedo. Taglia solo il DISEGNO, non il pezzo, e " +
                                    "si toglie da sola appena scattata.",
                                ["properties"] = new Dictionary<string, object>
                                {
                                    ["plane"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string",
                                        ["enum"] = Names(Session.PlaneNames),
                                        ["description"] = "su quale piano tagliare",
                                    },
                                    ["offset_mm"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "number",
                                        ["description"] =
                                            "di quanto spostare il piano di taglio, in mm. " +
                                            "Predefinito 0, cioe' il piano stesso — che passa " +
                                            "per l'origine, non per il centro del pezzo.",
                                    },
                                },
                                ["required"] = new[] { "plane" },
                            },
                        },
                        new string[0]),
                    readOnlyMode: true, destructive: false, idempotent: true),

                Tool(
                    "list_operations",
                    "L'elenco corto di cosa si puo' chiedere, per famiglia: un " +
                    "nome e una riga ciascuna. Per lo schema completo di una, usa " +
                    "«describe_operation».",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["family"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "solo una famiglia: extrusions, features, weldments, " +
                                    "references, assemblies, physical_properties, drawing, " +
                                    "checks, exports, identity, sheet_metal, dimensions",
                            },
                        },
                        new string[0]),
                    readOnlyMode: true, destructive: false, idempotent: true),

                Tool(
                    "describe_operation",
                    "Lo schema completo di una operazione: cosa vuole, in che " +
                    "unita', cosa deve esserci gia' nel documento.",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["name"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "il nome esatto, da «list_operations»",
                            },
                        },
                        new[] { "name" }),
                    readOnlyMode: true, destructive: false, idempotent: true),

                Tool(
                    "build",
                    "Esegue in SolidWorks una sequenza di operazioni e RIPORTA " +
                    "COS'E' VENUTO: le misure di ogni passo e lo stato del " +
                    "documento dopo.\n\n" +
                    "Il piano viene controllato TUTTO prima di cominciare: se un " +
                    "passo qualunque ha un nome sbagliato o argomenti che non " +
                    "rispettano lo schema, non viene eseguito niente e il " +
                    "documento resta com'era.\n\n" +
                    "Se invece un passo fallisce mentre viene eseguito, ci si " +
                    "ferma li': i passi seguenti quasi sempre nominano cio' che " +
                    "il passo fallito avrebbe dovuto creare. Quello che era gia' " +
                    "stato costruito resta, e «undo» lo toglie tutto in una " +
                    "volta.\n\n" +
                    "Con «dry_run: true» il piano viene SOLO controllato: non " +
                    "viene toccato niente, e la risposta dice cosa e' stato " +
                    "guardato e cosa no. Serve a sbagliare prima, invece che " +
                    "a meta' pezzo.\n\n" +
                    "Un passo puo' nominare la geometria creata da un passo " +
                    "PRECEDENTE dello stesso piano (per esempio face_ref: " +
                    "{step: 1, kind: \"top_face\"}): e' l'unico modo, " +
                    "perche' quella faccia non ha ancora nessun identificativo.\n\n" +
                    "The plan runs on the document you looked at; if another window " +
                    "came to the front in between, it stops before writing. " +
                    "«volume_after_mm3» is the plan's document; «state_after» is the " +
                    "document in front, and when that is another one the answer says " +
                    "so in «document_in_front» and «note_document_in_front» (after " +
                    "a drawing step it is the new drawing, and the next plan works " +
                    "on it). «undo_entry_recorded» says whether the plan left its " +
                    "own undo entry, the one an undo takes away.",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["steps"] = new Dictionary<string, object>
                            {
                                ["type"] = "array",
                                ["description"] =
                                    "le operazioni da fare, in ordine",
                                ["items"] = Object(
                                    new Dictionary<string, object>
                                    {
                                        ["operation"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "string",
                                            ["description"] = "il nome dell'operazione",
                                        },
                                        ["arguments"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "object",
                                            ["description"] =
                                                "gli argomenti, come da «describe_operation»",
                                        },
                                    },
                                    new[] { "operation" }),
                            },
                            ["document"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "Su quale documento disegnare: il titolo o il " +
                                    "percorso, copiato da «inspect_part». Serve " +
                                    "SOLO se il documento davanti e' cambiato da " +
                                    "quando l'hai guardato: normalmente si omette. " +
                                    "Nominarlo vuol dire «so quale pezzo sto " +
                                    "toccando», e un nome che non corrisponde viene " +
                                    "rifiutato.",
                            },
                            ["dry_run"] = new Dictionary<string, object>
                            {
                                ["type"] = "boolean",
                                ["description"] =
                                    "PROVA A VUOTO: controlla il piano e NON " +
                                    "tocca niente. Dice quali operazioni non " +
                                    "esistono, quali argomenti obbligatori " +
                                    "mancano, quali argomenti verrebbero " +
                                    "IGNORATI perche' scritti male, e quali " +
                                    "rimandi puntano a un passo che non c'e'. " +
                                    "Dice anche cosa NON ha potuto guardare: " +
                                    "se gli «uid» sono ancora validi e se il " +
                                    "documento e' quello giusto lo sa solo " +
                                    "leggendo il documento. Funziona anche con " +
                                    "SOLIDWORKS chiuso.",
                            },
                        },
                        new[] { "steps" }),
                    // Rieseguire lo stesso piano NON rimette il pezzo
                    // com'era: fa un secondo foro. Dichiararlo idempotente
                    // inviterebbe un client a riprovare da solo dopo un
                    // errore di rete.
                    readOnlyMode: false, destructive: true, idempotent: false),

                // MISURA E' DISEGNA SENZA IL PERMESSO DI TOCCARE.
                //
                // Non e' una comodita' ed e' l'unico modo di dire la verita' nel
                // protocollo: le annotazioni MCP viaggiano su `tools/list`,
                // cioe' stanno sullo STRUMENTO e partono prima che esista una
                // chiamata. Finche' c'era solo `build`, il client leggeva
                // «distruttivo» e chiedeva conferma anche per
                // `mass_properties`, che non muove un millimetro cubo — e chi
                // conferma dieci volte di fila smette di leggere cosa conferma.
                // Non si poteva riparare dentro `build`: al momento
                // dell'annotazione non si sa ancora quale operazione arrivera'.
                // Serviva uno strumento in piu', e questo e' quello.
                Tool(
                    "measure",
                    "MISURA il pezzo senza toccarlo. Prende le stesse " +
                    "operazioni di «build», ma accetta SOLO quelle della " +
                    "famiglia «checks» — quelle che leggono e basta.\n\n" +
                    "Se hai solo bisogno di sapere com'e' fatto il pezzo, usa " +
                    "questo: il tuo client sa che non cambia niente e non ti " +
                    "chiede conferma.\n\n" +
                    "Un'operazione che tocca il pezzo qui viene RIFIUTATA, e la " +
                    "risposta dice con quale strumento si fa. Il piano non " +
                    "parte: non si misura meta' pezzo e poi ci si ferma.",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["steps"] = new Dictionary<string, object>
                            {
                                ["type"] = "array",
                                ["description"] =
                                    "le misure da prendere, in ordine — solo " +
                                    "operazioni della famiglia «checks»",
                                ["items"] = Object(
                                    new Dictionary<string, object>
                                    {
                                        ["operation"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "string",
                                            ["description"] =
                                                "il nome dell'operazione di verifica",
                                        },
                                        ["arguments"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "object",
                                            ["description"] =
                                                "gli argomenti, come da «describe_operation»",
                                        },
                                    },
                                    new[] { "operation" }),
                            },
                            ["document"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "Su quale documento misurare: il titolo o il " +
                                    "percorso, copiato da «inspect_part». " +
                                    "Normalmente si omette.",
                            },
                        },
                        new[] { "steps" }),
                    // Idempotente sul serio, e non per cortesia: misurare due
                    // volte da' due volte la stessa risposta perche' non
                    // cambia niente in mezzo. E' la differenza con `build`,
                    // che al secondo giro fa un SECONDO foro.
                    readOnlyMode: true, destructive: false, idempotent: true),

                Tool(
                    "undo",
                    "RITIRA l'ultimo piano eseguito da «build», tutto intero. " +
                    "E' il gesto che questo ponte chiedeva di fare con Ctrl+Z, " +
                    "che tu non hai.\n\n" +
                    "Si annulla SOLO se il pezzo e' ancora come «build» lo " +
                    "aveva lasciato: se dopo il piano qualcuno ha lavorato a " +
                    "mano, mi fermo e dico cosa e' cambiato — annullare " +
                    "allora toglierebbe il lavoro di qualcun altro.\n\n" +
                    "Dopo, il pezzo torna al volume che aveva prima del piano, e " +
                    "gli identificativi delle facce NON sono piu' quelli: la " +
                    "risposta porta lo stato nuovo, ed e' quello che vale.\n\n" +
                    "The last plan is the last one that built something or left its " +
                    "own undo entry (its answer said «undo_entry_recorded»: true), " +
                    "for example a reorder; a measure, or a plan that wrote nothing, " +
                    "does not replace it. The part must be in front: after a plan " +
                    "that ended on another window (a drawing it made stays in front, " +
                    "and its answer names it in «document_in_front»), bring the part " +
                    "back as «note_document_in_front» says, then call again. I compare the part with " +
                    "how the plan left it (volume, feature count, feature names and " +
                    "order) and I REFUSE BEFORE PRESSING («refused_before_pressing»: " +
                    "true) when it differs, or when something is known to be on top " +
                    "of the plan's undo entry: SOLIDWORKS cannot redo an undo entry " +
                    "recorded through the API, so a wrong press could not be taken " +
                    "back. After pressing I report it withdrawn only if the part " +
                    "reads as before the plan; otherwise I say what the press took " +
                    "away and whether it came back.",
                    Object(new Dictionary<string, object>(), new string[0]),
                    // NON e' idempotente: chiamarlo due volte non annulla due
                    // volte lo stesso piano — la seconda toglierebbe quello
                    // che c'era PRIMA, cioe' lavoro che questo ponte non ha
                    // fatto. Il ricordo si cancella dopo il primo, ma
                    // dichiararlo idempotente inviterebbe un client a
                    // riprovare da solo dopo un errore di rete.
                    readOnlyMode: false, destructive: true, idempotent: false),

                Tool(
                    "document",
                    "Il DOCUMENTO su cui si lavora: crearne uno, aprirne uno dal " +
                    "disco, sapere quali sono aperti, portarne uno davanti.\n\n" +
                    "Senza documenti aperti gli altri strumenti non hanno niente " +
                    "da guardare: si comincia con «create» (una parte vuota dal " +
                    "modello predefinito) o con «open».\n\n" +
                    "Ogni volta che il documento davanti cambia, quello che avevi " +
                    "guardato non vale piu': prima di disegnare, «inspect_part». " +
                    "Il ponte lo pretende.\n\n" +
                    "Un file aperto in SOLA LETTURA lo dico sempre: di solito vuol " +
                    "dire che un collega lo ha aperto, e «build» non ci scrive.\n\n" +
                    "«close» RIFIUTA un documento con modifiche non salvate: non " +
                    "butto il lavoro di nessuno. Salvalo prima, o chiudilo a mano.\n\n" +
                    "Closing: a saved document is closed by its full path, and one never " +
                    "saved as the active document or by a title that no other open " +
                    "document answers to. It REFUSES when another open document answers " +
                    "to an unsaved document's title, and when hidden documents with " +
                    "unsaved changes that no other open document uses would go with it " +
                    "(CloseDoc closes non-active hidden documents without saving). The " +
                    "answer adds closed_by, still_loaded_hidden + still_loaded_reason " +
                    "(window closed, model kept because another open document references " +
                    "it), also_closed, also_closed_unsaved. Bringing forward: a saved " +
                    "document comes forward by file name with extension or path, never " +
                    "by title, and the active document is read back.",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["action"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = new[] { "create", "open", "list", "activate", "close" },
                                ["description"] =
                                    "«create»: crea un documento vuoto e lo porta davanti. " +
                                    "«open»: apre un file dal disco. «list»: i documenti " +
                                    "aperti, e quale e' davanti. «activate»: porta davanti uno " +
                                    "gia' aperto. «close»: chiude uno aperto, solo se non ha " +
                                    "modifiche da salvare.",
                            },
                            ["variant"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = new[] { "part", "assembly" },
                                ["description"] =
                                    "solo con «create». Predefinito «part». Una tavola " +
                                    "nasce da un pezzo: «create_drawing», dentro un piano.",
                            },
                            ["path"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "con «open»: il file .sldprt, .sldasm o .slddrw, col " +
                                    "percorso intero. Con «activate» e «close»: il percorso " +
                                    "o, per un documento mai salvato, il titolo che " +
                                    "«list» ha dato.",
                            },
                        },
                        new[] { "action" }),
                    // NON e' di sola lettura: «create», «open», «activate» e
                    // «close» cambiano il documento davanti. NON e' distruttivo:
                    // «close» rifiuta un documento con modifiche non salvate,
                    // quindi chiude solo cio' che e' gia' tutto su disco, e non
                    // butta niente. NON e' idempotente: «create» due volte fa due
                    // documenti. Still true for hidden documents (Z-C21.13 N11):
                    // a close that would take hidden documents with unsaved
                    // changes is refused.
                    readOnlyMode: false, destructive: false, idempotent: false),

                // THE WHOLE API, READ BY REFLECTION (entry U1.1).
                //
                // Two tools that never touch SOLIDWORKS: they read the interop
                // assembly loaded in this process and api_knowledge.json. They
                // work with SOLIDWORKS closed, and they are the first half of
                // Layer 1 (api_objects and api_call come with U1.2-U1.3).
                Tool(
                    "api_search",
                    "SEARCH the whole SOLIDWORKS API (the sldworks interop and the " +
                    "swconst enums) by a piece of a name. Answers with the REAL " +
                    "signatures, read from the interop on this machine: " +
                    "«SewRefSurface» finds IFeatureManager.InsertSewRefSurface(5 " +
                    "arguments) -> Feature and IModelDoc2.InsertSewRefSurface() -> Void.\n\n" +
                    "«Interface.piece» searches one interface («IEdge.Adjacent»). " +
                    "Enum names and enum values are searched too («swMateAlign»).\n\n" +
                    "The four old interfaces (IModelDoc, IBody, IFace, IComponent) are " +
                    "not exposed: a hit there names the new sister to use instead. " +
                    "Members of swmotionstudy are listed read only.\n\n" +
                    "Never touches SOLIDWORKS; works with it closed. For the full card " +
                    "of one member (what is known of it, enums, indices), use " +
                    "«api_signature».",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["query"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "a piece of the name, at least two characters; " +
                                    "«Interface.piece» to search one interface",
                            },
                            ["limit"] = new Dictionary<string, object>
                            {
                                ["type"] = "integer",
                                ["description"] =
                                    "how many lines to show, default 40, at most 200",
                            },
                        },
                        new[] { "query" }),
                    readOnlyMode: true, destructive: false, idempotent: true),

                Tool(
                    "api_signature",
                    "The CARD of one SOLIDWORKS API member, e.g. «IBody2.Check3» or " +
                    "«IAssemblyDoc.AddMate5»: every overload with its real types, " +
                    "property or method, the out (&) parameters by name, the typed " +
                    "I… variant next to it, the numbered family (X, X2, X3) with the " +
                    "latest marked «latest by number, not verified», and the flag " +
                    "when it returns Void (then a new feature is only found from the " +
                    "before/after snapshot).\n\n" +
                    "It also says what is KNOWN of the member here: its state " +
                    "(forbidden, confirm, gray, never_probed, probed_read, " +
                    "probed_modifies), which enum an Int32 parameter takes — ONLY " +
                    "where a hand-written map says so, otherwise «enum not " +
                    "tabulated» — the meaning of array indices ONLY when measured, " +
                    "otherwise «index not tabulated», the measured type of an Object " +
                    "parameter, and the source of each fact. Do not guess what it " +
                    "says is not tabulated.\n\n" +
                    "Also takes an interface («IEdge»: its member names) or an enum " +
                    "(«swFeatureNameID_e»: every value, names that share a value " +
                    "marked AMBIGUOUS). Never touches SOLIDWORKS.",
                    Object(
                        new Dictionary<string, object>
                        {
                            ["name"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "«Interface.Member», an interface, an enum of " +
                                    "swconst, or a member name that exists on one " +
                                    "interface only",
                            },
                        },
                        new[] { "name" }),
                    readOnlyMode: true, destructive: false, idempotent: true),
            };
        }

        /// <summary>
        /// api_knowledge.json, embedded in the executable at build time.
        ///
        /// Embedded and not copied next to the executable: the installer
        /// carries one file per payload entry, and a knowledge file that can
        /// be missing would make api_signature answer "never_probed" for a
        /// member measured to crash SOLIDWORKS — the one wrong answer that
        /// looks like a right one. Read once.
        /// </summary>
        private static readonly Lazy<string> ApiKnowledgeJson = new Lazy<string>(() =>
        {
            try
            {
                var asm = typeof(McpTools).Assembly;
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith("api_knowledge.json", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var stream = asm.GetManifestResourceStream(name))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                Protocol.Note("[bridge] api_knowledge.json not readable: " + ex.Message);
            }
            return null;
        });

        private static Dictionary<string, object> ApiAnswerContent(Func<Oce.Solid.Agent.Core.Api.ApiAnswer> ask, string tool)
        {
            try
            {
                if (ApiKnowledgeJson.Value == null)
                    return Protocol.Content(
                        tool + ": api_knowledge.json is missing from this build, and without it " +
                        "every member would look never probed — including the ones measured " +
                        "to crash SOLIDWORKS. Rebuild the bridge.", true);
                var answer = ask();
                return Protocol.Content(answer.Text, answer.IsError);
            }
            catch (Exception ex)
            {
                // The interop is missing (SOLIDWORKS not installed where the
                // bridge looks): say where to point it, not a stack trace.
                var inner = ex;
                while (inner.InnerException != null) inner = inner.InnerException;
                return Protocol.Content(
                    tool + " could not read the SOLIDWORKS interop: " + inner.Message +
                    " Set SW_INSTALL_PATH to the SOLIDWORKS installation folder.", true);
            }
        }

        private Dictionary<string, object> ApiSearch(Dictionary<string, object> arguments)
        {
            var query = TextOf(arguments, "query");
            int limit;
            int.TryParse(TextOf(arguments, "limit") ?? "", NumberStyles.Integer,
                         CultureInfo.InvariantCulture, out limit);
            return ApiAnswerContent(
                () => Oce.Solid.Agent.Core.Api.ApiCatalog.Search(query, limit, ApiKnowledgeJson.Value),
                "api_search");
        }

        private Dictionary<string, object> ApiSignature(Dictionary<string, object> arguments)
        {
            var name = TextOf(arguments, "name");
            return ApiAnswerContent(
                () => Oce.Solid.Agent.Core.Api.ApiCatalog.Signature(name, ApiKnowledgeJson.Value),
                "api_signature");
        }

        /// <summary>
        /// Lo strumento «document» (voci Z-B1, Z-B2, Z-B5, Z-B6).
        ///
        /// Tutto il lavoro sta in `Bridge/Documents.cs`; qui c'e' la parte che
        /// solo il ponte sa fare: quando il documento davanti cambia, quello
        /// che era stato guardato non vale piu', e le firme con lui. E' la
        /// regola di Z-A1 — i tre campi che parlano dello stesso pezzo si
        /// buttano insieme — applicata al punto in cui il ponte stesso cambia
        /// documento. La voce Z-B6 lo aveva scritto prima che esistesse.
        /// </summary>
        private Dictionary<string, object> Document(Dictionary<string, object> arguments)
        {
            if (!_session.Attached)
                return Protocol.Content("Il ponte non e' agganciato a SolidWorks.", true);

            string action = TextOf(arguments, "action");
            string error;
            Dictionary<string, object> card;
            switch (action)
            {
                case "list":
                {
                    var list = _session.Documents(out int hidden, out error);
                    if (list == null) return Protocol.Content(error, true);
                    var outside = new Dictionary<string, object>
                    {
                        ["documents"] = list,
                        ["how_many"] = list.Count,
                        ["loaded_inside_assembly"] = hidden,
                    };
                    if (list.Count == 0)
                        outside["note"] = "Nessun documento aperto: comincia con «create» o «open».";
                    return Protocol.ContentWithData(Protocol.InJson(outside), outside);
                }
                case "create":
                    string typ = TextOf(arguments, "variant");
                    card = _session.CreateDocument(string.IsNullOrWhiteSpace(typ) ? "part" : typ, out error);
                    break;
                case "open":
                    card = _session.OpenDocument(TextOf(arguments, "path"), out error);
                    break;
                case "activate":
                    card = _session.ActivateDocument(TextOf(arguments, "path"), out error);
                    break;
                case "close":
                {
                    var closed = _session.CloseDocument(TextOf(arguments, "path"), out error);
                    if (closed == null) return Protocol.Content(error, true);
                    // Il documento guardato potrebbe essere proprio quello chiuso:
                    // si butta lo sguardo come per ogni cambio di documento.
                    _seen = null;
                    _signatureSeen = null;
                    _seenSignatures = null;
                    closed["next_step"] = closed["in_front_now"] == null
                        ? "Non c'e' piu' nessun documento aperto: «create» o «open» per continuare."
                        : "Davanti adesso c'e' «" + Convert.ToString(closed["in_front_now"]) + "». " +
                          "Prima di disegnare, «inspect_part».";
                    return Protocol.ContentWithData(Protocol.InJson(closed), closed);
                }
                default:
                    return Protocol.Content(
                        "«action» vale «create», «open», «list», «activate» o «close». Ho ricevuto «" +
                        (action ?? "") + "».", true);
            }
            if (card == null) return Protocol.Content(error, true);

            // IL DOCUMENTO DAVANTI E' CAMBIATO (o potrebbe esserlo): quello che
            // era stato guardato e le sue firme si buttano INSIEME. «build» a
            // questo punto risponde «non ho ancora guardato nessun pezzo», che
            // e' la verita'.
            _seen = null;
            _signatureSeen = null;
            _seenSignatures = null;

            card["next_step"] =
                "Davanti adesso c'e' «" + Convert.ToString(card["document_title"]) + "». " +
                "Prima di disegnare, «inspect_part»: quello che avevi guardato " +
                "prima non vale piu'.";
            return Protocol.ContentWithData(Protocol.InJson(card), card);
        }

        private static string TextOf(Dictionary<string, object> arguments, string key)
        {
            if (arguments == null || !arguments.TryGetValue(key, out var v) || v == null) return null;
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)?.Trim();
        }

        /// <summary>
        /// Il filo dell'avanzamento della chiamata in corso (voce W2.2).
        ///
        /// Non e' mai null: spento quando il client non ha messo un gettone.
        /// Cosi' i punti che lo usano non hanno un `if` ciascuno.
        /// </summary>
        private Mcp.Progress.Wire _wire = new Mcp.Progress.Wire(null);

        public Dictionary<string, object> Invoke(
            string itemName, Dictionary<string, object> arguments, object token = null)
        {
            // UN FILO NUOVO A OGNI CHIAMATA. Il contatore di `progress` deve
            // crescere DENTRO una richiesta; fra richieste diverse ricomincia,
            // e tenerne uno solo vorrebbe dire che la seconda chiamata parte
            // da dove era arrivata la prima.
            _wire = new Mcp.Progress.Wire(token);

            // THE NAMES OF BEFORE W3.1-bis.5 (Oce.Solid.Agent.Core/Alias.cs,
            // decision D4): a tool, an action, a family, an operation, an
            // argument or a value written with its Italian name is translated
            // BEFORE any check of this bridge reads it, and the answer lists
            // each one in `deprecated`. Input only: the answers, the catalogue
            // and describe_operation speak English. Removed in W3.1-bis.11.
            List<string> deprecated = new List<string>();
            itemName = Oce.Solid.Agent.Core.Alias.Tool(itemName, deprecated);
            if (arguments != null)
                Oce.Solid.Agent.Core.Alias.ToolArguments(itemName, arguments, ToolSchema(itemName), deprecated);
            return WithDeprecated(Dispatch(itemName, arguments), deprecated);
        }

        /// <summary>The input schema of a tool, as tools/list gives it, or null.</summary>
        private Dictionary<string, object> ToolSchema(string itemName)
        {
            var tools = Items() as object[];
            if (tools == null) return null;
            foreach (var x in tools)
            {
                var s = x as Dictionary<string, object>;
                object n, schema;
                if (s != null && s.TryGetValue("name", out n) && string.Equals(n as string, itemName, StringComparison.Ordinal) &&
                    s.TryGetValue("inputSchema", out schema))
                    return schema as Dictionary<string, object>;
            }
            return null;
        }

        /// <summary>
        /// The answer of a tool, with the Italian names it accepted: in
        /// `structuredContent.deprecated` when there is structured content, and
        /// always as a last text item, so that a client reading only the text
        /// sees it too.
        /// </summary>
        internal static Dictionary<string, object> WithDeprecated(Dictionary<string, object> answer, List<string> deprecated)
        {
            if (answer == null || deprecated == null || deprecated.Count == 0) return answer;
            object data;
            var structured = answer.TryGetValue("structuredContent", out data) ? data as Dictionary<string, object> : null;
            if (structured != null) structured["deprecated"] = deprecated.ToArray();
            object c;
            var content = answer.TryGetValue("content", out c) ? c as object[] : null;
            var line = new Dictionary<string, object>
            {
                ["type"] = "text",
                ["text"] = "deprecated: " + string.Join("; ", deprecated),
            };
            answer["content"] = (content ?? new object[0]).Concat(new object[] { line }).ToArray();
            return answer;
        }

        private Dictionary<string, object> Dispatch(string itemName, Dictionary<string, object> arguments)
        {
            switch (itemName)
            {
                case "inspect_part": return InspectPart(arguments);
                case "list_operations": return ListOperations(arguments);
                case "describe_operation": return DescribeOperation(arguments);
                case "build": return Build(arguments);
                case "measure": return Measure(arguments);
                case "undo": return Undo();
                case "document": return Document(arguments);
                case "api_search": return ApiSearch(arguments);
                case "api_signature": return ApiSignature(arguments);
                default:
                    // E PRIMA DI RISPONDERE, SI SCRIVE (voce Z-G7): questo
                    // e' il punto che sapeva e taceva.
                    _registry.ToolThatDoesNotExist(itemName);

                    // SI DICE QUALI CI SONO, non solo che questo non c'e'.
                    //
                    // Un modello che ha sbagliato nome non ha modo di
                    // correggersi leggendo «non conosco x»: puo' solo
                    // riprovare a indovinare, ed e' il modo in cui si finisce
                    // per chiamare tre volte la stessa cosa sbagliata. I nomi
                    // sono sette: stanno in una riga, e costano meno di un giro
                    // in piu'. E' la stessa regola dei rifiuti delle
                    // operazioni — il criterio, non il verdetto.
                    return Protocol.Content(
                        "Non conosco lo strumento «" + itemName + "». Ci sono " +
                        "questi: " + string.Join(", ", ToolNames) + ". " +
                        "Le sessanta OPERAZIONI di disegno non sono strumenti: " +
                        "si chiedono a «build» (o a «measure», se leggono e " +
                        "basta) e si elencano con «list_operations».", true);
            }
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// L'ultimo documento che «inspect_part» ha descritto.
        ///
        /// Non e' un ricordo di comodo: e' il contratto fra i due strumenti.
        /// Un piano nasce guardando UN documento, e va eseguito su QUELLO.
        /// </summary>
        private Dictionary<string, object> _seen;

        /// <summary>
        /// COM'ERA FATTO IL PEZZO quando «inspect_part» l'ha guardato
        /// (voce X2) — non QUALE pezzo era, che e' `_guardato`.
        ///
        /// ── DUE DOMANDE CHE SEMBRANO UNA ─────────────────────────────
        ///
        /// «E' lo stesso documento?» e «e' ancora lo stesso pezzo?» hanno
        /// risposte diverse, e finora se ne guardava una sola. Titolo e
        /// percorso combaciano benissimo mentre qualcuno, nel frattempo, ha
        /// tirato una quota a mano nella finestra di SOLIDWORKS: il
        /// documento e' quello, il pezzo no.
        ///
        /// Conta perche' **gli uid delle facce sono posizionali**. Un piano
        /// nasce da una lettura e i suoi `F1`, `F2` parlano di quel momento;
        /// se la geometria e' cambiata, «F1» indica un'altra faccia e
        /// l'operazione riesce sul posto sbagliato — che e' il difetto gia'
        /// misurato il 14 settembre con le due tasche, tutte e due
        /// «succeeded: true».
        ///
        /// ── COSA CI SI METTE, E PERCHE' SOLO QUESTO ──────────────────
        ///
        /// Volume, numero di lavorazioni, data di scrittura del file. Tre
        /// cose **gia' tutte in mano**: non costano una lettura in piu' e
        /// non chiedono niente a SOLIDWORKS che non sia gia' stato chiesto.
        /// Non e' un'impronta forte e non vuole esserlo — *un confronto che
        /// costa quanto la cosa che protegge non lo fa nessuno.*
        ///
        /// **Cosa NON prende, detto qui invece che scoperto:** una modifica
        /// che lascia il volume identico e non tocca il conto delle
        /// lavorazioni — spostare un foro senza cambiarne il diametro, per
        /// dire. Quella passa, e per prenderla servirebbe un'impronta della
        /// geometria che questa non e'.
        /// </summary>
        private Dictionary<string, object> _signatureSeen;

        /// <summary>
        /// Com'erano fatte le entita' quando «inspect_part» le ha
        /// descritte: uid -> punto, normale, area, lunghezza.
        ///
        /// GLI UID SONO POSIZIONALI. L'add-in li riassegna a ogni lettura
        /// ordinando le facce per area, quindi basta un passo che cambia
        /// il pezzo perche' «F1» indichi un'altra faccia. Un piano pero'
        /// nasce da UNA lettura: i suoi uid parlano di quel momento li'.
        ///
        /// MISURATO il 14 settembre 2026, tre passi in una sola chiamata a
        /// «build» su un blocco 200x100x20:
        ///   1. estrudi il blocco          -> F1 = faccia di sopra, 20.000 mm2
        ///   2. tasca Ø60 profonda 5 su F1 -> la faccia di sopra scende a
        ///      17.172,57 mm2 e passa DIETRO a quella di sotto nell'ordine
        ///   3. tasca Ø10 profonda 5 su F1 -> «F1» adesso e' quella di SOTTO
        /// Tutti e tre i passi hanno risposto «succeeded: true». La tasca
        /// piccola e' finita sulla faccia sbagliata, le profondita' erano
        /// quelle chieste, e il volume tornava: 385.470,1 mm3, esatto. Non
        /// c'era niente, in quella risposta, che facesse sospettare.
        ///
        /// L'add-in sa gia' difendersi: legge «_signatures» dagli argomenti e
        /// confronta. Ma quella chiave la scriveva solo il server di
        /// OpenPDM, e qui non la scriveva nessuno — quindi il confronto
        /// non aveva termini e passava sempre. Questo campo e' il termine
        /// che mancava.
        /// </summary>
        private Dictionary<string, object> _seenSignatures;

        /// <summary>
        /// L'ultimo piano eseguito, il documento su cui e' andato, e com'era il
        /// documento subito dopo (voce W2.7).
        ///
        /// Servono tutti e tre INSIEME: il piano da solo non distingue un
        /// rinvio da una ripetizione voluta su un documento diverso, e lo stato
        /// da solo non dice niente su cosa si stia richiedendo.
        /// </summary>
        private string _lastPlanDone;
        private Dictionary<string, object> _planDocument;
        private string _summaryAfterPlan;

        /// <summary>
        /// Quante viste al massimo in una sola risposta.
        ///
        /// Tre bastano a definire una forma — isometrica piu' due — e sono
        /// gia' tre volte il costo dello stato scritto. Nove (tutte quelle
        /// che esistono) sarebbero un ordine di grandezza in piu' per un
        /// pezzo, e nessuno le guarderebbe tutte.
        /// </summary>
        private const int MaximumViews = 3;

        /// <summary>Larghezza e altezza del fotogramma.</summary>
        private const int ViewWidth = 1024;
        private const int ViewHeight = 768;

        private Dictionary<string, object> InspectPart(
            Dictionary<string, object> arguments)
        {
            var docState = _session.State(out var error);
            if (docState == null) return Protocol.Content(error, true);
            _seen = StateIdentity(docState);
            _signatureSeen = LightSignature(docState);
            _seenSignatures = StateSignatures(docState);
            RemoveAnchors(docState);

            var requestedItems = RequestedViews(arguments);
            var section = Take(arguments, "section") as Dictionary<string, object>;
            if (requestedItems.Count == 0 && section == null)
                // Il testo per chi legge, i dati per chi li deve usare (W2.4).
                return Protocol.ContentWithData(Protocol.InJson(docState), docState);

            // LO STATO ESCE COMUNQUE, anche se nessuna fotografia riesce.
            // Un `inspect_part` che fallisce perche' non si e' potuto
            // scattare toglierebbe a chi legge anche l'elenco delle facce —
            // cioe' l'unica cosa che gli serviva davvero. I motivi delle
            // fotografie mancate si dicono nel testo, accanto allo stato.
            var images = new List<KeyValuePair<string, byte[]>>();
            var missed = new List<string>();
            var succeeded = new List<string>();

            foreach (var which in requestedItems)
            {
                // LO STESSO DOCUMENTO DELLO STATO, PRIMA DI OGNI SCATTO.
                //
                // `State` e `Snapshot` chiedono a SOLIDWORKS qual e' il
                // documento davanti, ognuna per conto suo: fra le due
                // chiamate basta che chi lavora porti avanti un'altra
                // finestra e il ponte risponde con la fotografia di un pezzo
                // e le misure di un altro. Nessun errore, nessun avviso,
                // un'immagine che contraddice l'elenco accanto a cui sta.
                //
                // E' esattamente il difetto costato «Parte4» l'11 settembre,
                // rifatto nella forma piu' pura: due letture di `ActiveDoc`
                // trattate come se fossero una sola.
                //
                // Si controlla A OGNI GIRO e non una volta sola: tre viste
                // sono tre ridisegni e un paio di secondi, e la finestra
                // puo' cambiare durante.
                var now = _session.Identity(_session.ActiveDocument());
                if (!Session.SameDocument(_seen, now))
                {
                    missed.Add(which + ": davanti adesso c'e' «" +
                                ReadableName(now) + "», non «" +
                                ReadableName(_seen) + "». Non fotografo " +
                                "un pezzo diverso da quello che ho appena " +
                                "misurato.");
                    continue;
                }

                var png = _session.Snapshot(
                    which, ViewWidth, ViewHeight, out var why);
                if (png != null && png.Length > 0)
                {
                    images.Add(new KeyValuePair<string, byte[]>("image/png", png));
                    succeeded.Add(which);
                }
                else
                {
                    missed.Add(which + ": " + (why ?? "non so perche'"));
                }
            }

            // ── LO SPACCATO, se lo hanno chiesto (voce V4) ───────────────
            //
            // Viene DOPO le viste esterne e prima del testo: nell'ordine in cui
            // si guarda un pezzo, prima da fuori e poi dentro.
            if (section != null)
            {
                var nowSec = _session.Identity(_session.ActiveDocument());
                if (!Session.SameDocument(_seen, nowSec))
                {
                    missed.Add("sezione: davanti adesso c'e' «" +
                                ReadableName(nowSec) + "», non «" +
                                ReadableName(_seen) + "».");
                }
                else
                {
                    string plane = Text(section, "plane");
                    double offset = 0;
                    object raw = Take(section, "offset_mm");
                    if (raw != null)
                        try { offset = Convert.ToDouble(raw,
                                  System.Globalization.CultureInfo.InvariantCulture); }
                        catch { }

                    // UNA SEZIONE SI GUARDA DI FRONTE, e non e' una preferenza.
                    //
                    // Il primo giro la fotografava nell'orientamento corrente,
                    // cioe' in isometrica. Misurato: un modello a cui e' stata
                    // data quella immagine ha letto la parete «6 volte
                    // nell'altezza» e il fondo «8», quando il vero e' **12,5**
                    // per tutti e due — sbagliato del 40-50%.
                    //
                    // Non era colpa sua: in isometrica uno spessore sottile e'
                    // scorciato, e sembra piu' grosso di quello che e'. Guardata
                    // perpendicolarmente al piano di taglio, la sezione e' in
                    // scala vera nelle due direzioni che contano.
                    //
                    // La vista si ricava dal piano, e non si chiede: chi chiede
                    // uno spaccato sul piano destro vuole guardarlo da destra.
                    // Poterla scegliere sarebbe un modo in piu' di sbagliare.
                    var png = _session.Cutaway(
                        ViewFacing(plane), plane, offset,
                        ViewWidth, ViewHeight, out var why);
                    if (png != null && png.Length > 0)
                    {
                        images.Add(new KeyValuePair<string, byte[]>("image/png", png));
                        succeeded.Add("sezione sul piano " + plane +
                                     (Math.Abs(offset) > 1e-9
                                         ? " a " + offset.ToString("0.##",
                                             System.Globalization.CultureInfo.InvariantCulture) + " mm"
                                         : ""));
                    }
                    else missed.Add("sezione: " + (why ?? "non so perche'"));
                }
            }

            var text = new StringBuilder();
            if (succeeded.Count > 0)
            {
                // LA DIDASCALIA PRIMA DELLE IMMAGINI, E NELL'ORDINE.
                // Le immagini arrivano senza nome: sapere che la prima e'
                // l'isometrica e la seconda la frontale e' la differenza fra
                // una vista e un indovinello.
                text.Append(succeeded.Count == 1
                    ? "Qui sotto la vista " + succeeded[0] + " del pezzo."
                    : "Qui sotto " + succeeded.Count + " viste del pezzo, in " +
                      "quest'ordine: " + string.Join(", ", succeeded) + ".");
                // ── UN NUMERO LETTO DA UN'IMMAGINE NON E' AUTOREVOLE ──────
                //
                // La didascalia diceva gia' dove stanno le misure. Non
                // diceva la cosa che conta: che leggerle DALLA FOTOGRAFIA
                // sbaglia, e di quanto. Su questo progetto e' successo due
                // volte, misurato tutt'e due:
                //
                //   - una corona di fori **90 letta 79** dalla sola
                //     isometrica, con confidenza piena;
                //   - una parete di **12,5 letta «6»** e un fondo di 12,5
                //     letto «8», sbagliati del 40-50%, perche' in
                //     isometrica uno spessore sottile e' scorciato.
                //
                // Nessuno dei due errori si accorge di se': il numero che
                // esce e' plausibile, e viene usato per il passo dopo.
                // **Dire "le misure sono nello stato" non basta** — va
                // detto che quelle dell'immagine sono SBAGLIATE, e come si
                // prende quella vera (voce Z-C10).
                text.Append(" Le fotografie mostrano la FORMA. **I numeri " +
                             "letti da un'immagine non sono autorevoli**: su " +
                             "queste stesse viste un diametro di 90 e' stato " +
                             "letto 79, e una parete di 12,5 e' stata letta 6 " +
                             "— in isometrica gli spessori sono scorciati e " +
                             "sembrano piu' grossi. Prima di usare una misura " +
                             "in un passo, prendila dallo stato qui di seguito " +
                             "oppure chiedila con «measure_between», " +
                             "«minimum_distance» o «body_bounding_box». Nello stato " +
                             "stanno anche gli identificativi.\n\n");
            }
            if (missed.Count > 0)
                text.Append("Viste non riuscite — " +
                             string.Join("; ", missed) + "\n\n");

            text.Append(Protocol.InJson(docState));

            return Protocol.ContentWithImages(text.ToString(), images);
        }

        /// <summary>
        /// Quali viste ha chiesto chi legge: nomi veri, senza doppioni, al
        /// massimo <see cref="MaximumViews"/>.
        ///
        /// UN NOME SBAGLIATO NON SI SCARTA IN SILENZIO: arriva a
        /// `PartView.Fire`, che risponde con l'elenco di quelli veri.
        /// Scartarlo qui vorrebbe dire rispondere «ecco il pezzo» a chi ha
        /// chiesto una vista che non ha mai avuto — e non capirebbe perche'
        /// manca.
        /// </summary>
        private static List<string> RequestedViews(Dictionary<string, object> arguments)
        {
            // `Strings` C'ERA GIA', e fa esattamente la cosa difficile: una
            // stringa e' un `IEnumerable` **di caratteri**, quindi
            // `views: "isometric"` — l'errore piu' naturale del mondo, e lo
            // schema chiede un elenco — scorso come sequenza darebbe tre viste
            // che si chiamano «i», «s», «o». Tre rifiuti incomprensibili al
            // posto della fotografia chiesta.
            //
            // Scriverne una seconda copia qui sarebbe stato il difetto che
            // questo progetto ha visto tre volte oggi: la regola giusta gia'
            // scritta da un'altra parte, e una riga nuova che non la fa.
            var outside = new List<string>();
            foreach (var x in Strings(Take(arguments, "views")))
            {
                var itemName = x.Trim();
                if (itemName.Length == 0) continue;
                if (outside.Contains(itemName, StringComparer.OrdinalIgnoreCase)) continue;
                outside.Add(itemName);
                if (outside.Count >= MaximumViews) break;
            }
            return outside;
        }

        /// <summary>
        /// La vista da cui si guarda uno spaccato: quella perpendicolare al
        /// piano di taglio.
        ///
        /// Null per un piano che non si conosce — e allora si scatta
        /// nell'orientamento corrente, che e' meglio di niente: il rifiuto per
        /// il nome sbagliato lo fa `CaptureInSection`, che conosce i piani veri,
        /// e farlo anche qui vorrebbe dire scrivere due volte lo stesso elenco.
        /// </summary>
        private static string ViewFacing(string plane)
        {
            switch (plane)
            {
                case "front": return "front";
                case "top": return "top";
                case "right": return "right";
                default: return null;
            }
        }

        /// <summary>
        /// I nomi come vettore, che e' la forma che JSON Schema vuole per
        /// `enum`: un `IEnumerable&lt;string&gt;` serializzato diventa
        /// un oggetto, non un elenco.
        /// </summary>
        private static string[] Names(IEnumerable<string> names)
        {
            return new List<string>(names).ToArray();
        }

        /// <summary>
        /// L'ANCORA NON SI MANDA A CHI LEGGE, e non e' per risparmiare posto —
        /// anche se ne risparmia.
        ///
        /// `point_mm` e `anchor` fanno due mestieri diversi e hanno due
        /// destinatari diversi:
        ///
        ///   * `point_mm` serve a **selezionare**: e' un punto garantito sulla
        ///     faccia, e chi legge lo usa per dire dove lavorare;
        ///   * `anchor` serve a **riconoscere**: e' il baricentro d'area, che
        ///     non sta necessariamente sulla faccia (quello di una faccia a
        ///     ferro di cavallo cade nel vuoto) e non si puo' usare per
        ///     selezionare niente.
        ///
        /// Mandarli tutti e due vorrebbe dire offrire due punti a chi ne deve
        /// usare uno, e la scelta sbagliata non darebbe errore: darebbe un foro
        /// fuori dal materiale.
        ///
        /// E il posto conta lo stesso: su un telaio da quattro barre l'ancora
        /// su sessanta facce sono **1.400 caratteri**, e lo stato passava da
        /// 15.700 a 17.171 — oltre il tetto dei 16 KB che il banco sorveglia.
        ///
        /// Si toglie DOPO aver costruito le firme, che se la portano dietro.
        /// </summary>
        private static void RemoveAnchors(Dictionary<string, object> docState)
        {
            if (docState == null) return;
            var faces = (docState.TryGetValue("faces", out var g) ? g : null)
                        as System.Collections.IEnumerable;
            if (faces == null) return;
            foreach (var v in faces)
            {
                var entry = v as Dictionary<string, object>;
                if (entry == null) continue;
                entry.Remove("anchor");
                // IL PERSIST ID SI TOGLIE PER DUE RAGIONI, e la seconda basta
                // da sola. Non e' leggibile — e' un blob opaco, e chi legge non
                // ci puo' fare niente. E pesa: 375-423 byte per faccia, cioe'
                // ~530 caratteri in base64, che su sessanta facce sono ~32 KB,
                // il DOPPIO dell'intero tetto dello stato.
                entry.Remove("persist_id");
            }
        }

        /// <summary>
        /// Cosa basta per riconoscere la stessa entita' fra due letture.
        ///
        /// Gli stessi quattro campi che usa il server di OpenPDM, cosi' le
        /// due strade dicono la stessa cosa all'add-in. Chi confronta
        /// davvero e' lui, e guarda punto e normale: l'area di una faccia
        /// cambia in modo legittimo — ci si fa un foro — mentre
        /// l'orientamento no.
        /// </summary>
        private static Dictionary<string, object> StateSignatures(
            Dictionary<string, object> docState)
        {
            if (docState == null) return null;
            var outside = new Dictionary<string, object>();

            // LA SCATOLA DEL PEZZO, la stessa per tutte le entita'.
            //
            // Sta nella firma di ognuna e non una volta sola in fondo perche'
            // `_signatures` e' una mappa uid -> firma, e chi la legge cerca per uid:
            // una chiave che uid non e' sarebbe un caso speciale da ricordarsi
            // in tre posti. Sei numeri per entita' in un allegato che porta gia'
            // ~530 caratteri di persist ID a faccia non si notano.
            //
            // SERVE SOLO A SCRIVERE UNA FRASE (voce E10): dove stava la faccia
            // rispetto al pezzo, quando il pezzo e' cambiato. Il motivo per cui
            // non puo' diventare un criterio di identita' sta per esteso in
            // `Oce.Solid.Agent.Core/Sentences.cs`.
            double[] partBoxValue = PartBox(docState);

            // I CORPI SONO ENTITA' COME LE ALTRE: per un saldato sono LE
            // entita', e «tappa le estremita' di C1» nomina corpi.
            foreach (var family in new[] { "faces", "edges", "feature", "axes", "bodies" })
            {
                // SI SCORRE COME IEnumerable, NON SI CASTA A object[].
                // Lo stato arriva dall'add-in con i suoi tipi veri, e la' le
                // famiglie sono List<Dictionary<string, object>>: un
                // «as object[]» torna null in silenzio, e il 14 settembre 2026
                // questo controllo e' rimasto spento per quello. Passando da
                // IEnumerable va bene tutte e due le forme, anche quella che
                // arriva da un JSON.
                var entries = (docState.TryGetValue(family, out var g) ? g : null)
                           as System.Collections.IEnumerable;
                if (entries == null) continue;
                foreach (var v in entries)
                {
                    var entry = v as Dictionary<string, object>;
                    if (entry == null || !entry.TryGetValue("uid", out var uid) || uid == null)
                        continue;
                    var signature = new Dictionary<string, object>();
                    // `feature` e `feature_id` servono alla
                    // corroborazione del ritrovamento (voce E3): senza, una
                    // faccia di un'altra lavorazione finita nello stesso posto
                    // e' indistinguibile da quella che si cercava.
                    // `center_mm` e `bounding_box_mm` sono la firma dei CORPI, e
                    // fino alla 0.24.0 non venivano copiati: per «C1» la firma
                    // usciva vuota, `SignatureMatches` non aveva niente da
                    // confrontare e rispondeva sempre di si'. I corpi erano
                    // controllati per ESISTENZA e non per IDENTITA'.
                    foreach (var theField in new[]
                             {
                                 "point_mm", "normal", "area_mm2", "length_mm",
                                 "feature_id", "center_mm", "bounding_box_mm", "anchor",
                                 "feature_id",
                                 // LA DIREZIONE DI UN ASSE (voce E4): senza, la
                                 // firma dell'asse avrebbe solo un punto, e un asse
                                 // girato attorno a quel punto passerebbe.
                                 "direction",
                                 // IL PERSIST ID VIAGGIA QUI E SOLO QUI.
                                 // E' il CANDIDATO del ritrovamento (voce E6):
                                 // ~530 caratteri in base64 per faccia, che
                                 // nello stato sarebbero ~32 KB su sessanta
                                 // facce — il doppio del tetto. La firma non va
                                 // a nessun modello, e' un allegato interno al
                                 // passo, e li' il posto non e' un problema.
                                 "persist_id",
                             })
                        if (entry.TryGetValue(theField, out var x) && x != null)
                            signature[theField] = x;

                    // IL NOME DELLA LAVORAZIONE SI RISOLVE QUI.
                    //
                    // Lo stato lo scrive una volta sola, in una mappa in fondo,
                    // perche' ripeterlo su sessanta facce faceva sforare il
                    // tetto dei 16 KB. La firma invece se lo porta esteso: non
                    // va a nessun modello — e' un allegato interno al passo — e
                    // serve a scrivere un rifiuto che nomina la lavorazione
                    // attesa invece di darne il numero.
                    if (signature.TryGetValue("feature_id", out var featId) && featId != null)
                    {
                        var itemName = FeatureName(docState, featId);
                        if (itemName != null) signature["feature"] = itemName;
                    }
                    // DOPO il conteggio di sopra, mai prima: una firma che
                    // avesse SOLO la scatola del pezzo non direbbe niente di
                    // quell'entita', e finirebbe lo stesso in `_signatures` facendo
                    // credere che una firma ci sia.
                    if (signature.Count > 0 && partBoxValue != null)
                        signature["box_part_mm"] = partBoxValue;
                    if (signature.Count > 0) outside[Convert.ToString(uid)] = signature;
                }
            }
            return outside.Count > 0 ? outside : null;
        }

        /// <summary>Titolo e percorso presi dallo stato, senza ripassare dal COM.</summary>
        private static Dictionary<string, object> StateIdentity(
            Dictionary<string, object> docState)
        {
            if (docState == null) return null;
            return new Dictionary<string, object>
            {
                ["document_title"] = docState.TryGetValue("document_title", out var t) ? t : "",
                ["path"] = docState.TryGetValue("path", out var p) ? p : "",
            };
        }

        /// <summary>
        /// La firma leggera di uno stato: volume, quante lavorazioni, quando
        /// il file e' stato scritto (voce X2). Vedi <see cref="_signatureSeen"/>.
        ///
        /// Le chiavi che mancano NON diventano zero. Un pezzo senza volume —
        /// un documento vuoto — ha una firma senza `volume_mm3`, e un
        /// confronto fra «non c'era» e «adesso c'e'» e' una differenza vera
        /// che va detta; metterci uno zero la trasformerebbe in «il volume e'
        /// passato da 0 a 96000», che e' un'altra frase e racconta un'altra
        /// cosa.
        /// </summary>
        private static Dictionary<string, object> LightSignature(
            Dictionary<string, object> docState)
        {
            if (docState == null) return null;
            var f = new Dictionary<string, object>(StringComparer.Ordinal);

            if (docState.TryGetValue("volume_mm3", out var v) && v != null)
                f["volume_mm3"] = v;

            if (docState.TryGetValue("feature", out var e) &&
                e is System.Collections.IList list)
                f["features"] = list.Count;

            // LA DATA DEL FILE PRENDE CIO' CHE IL VOLUME NON VEDE: un salvataggio
            // fatto da fuori. Un pezzo mai salvato non ha un file, e non avere la
            // chiave e' la risposta giusta — non «data zero».
            var filePath = Convert.ToString(
                docState.TryGetValue("path", out var p) ? p : "");
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                try
                {
                    if (File.Exists(filePath))
                        f["file_written"] = File.GetLastWriteTimeUtc(filePath)
                            .ToString("o", CultureInfo.InvariantCulture);
                }
                catch { }
            }
            return f;
        }

        /// <summary>
        /// Cosa e' cambiato fra due firme, in italiano, oppure `null` se non
        /// e' cambiato niente (voce X2).
        ///
        /// **SI FERMA COL CRITERIO, NON CON UN VERDETTO.** «Il volume e'
        /// passato da 96.000 a 94.500 da quando l'hai guardato» si puo'
        /// controllare; «lo stato e' cambiato» obbliga a fidarsi. La
        /// differenza conta perche' un avviso che non si puo' verificare si
        /// impara a ignorare, ed e' il rischio scritto nella voce.
        /// </summary>
        internal static string WhatChanged(
            Dictionary<string, object> before, Dictionary<string, object> now)
        {
            if (before == null || now == null) return null;

            var called = new List<string>();
            foreach (var key in new[] { "volume_mm3", "features", "file_written" })
            {
                bool existedBefore = before.TryGetValue(key, out var a);
                bool existsNow = now.TryGetValue(key, out var b);
                if (!existedBefore && !existsNow) continue;

                string textA = existedBefore ? Convert.ToString(a, CultureInfo.InvariantCulture) : null;
                string textB = existsNow ? Convert.ToString(b, CultureInfo.InvariantCulture) : null;
                if (string.Equals(textA, textB, StringComparison.Ordinal)) continue;

                if (key == "volume_mm3")
                    called.Add("il volume e' passato da " + (textA ?? "(nessuno)") +
                              " a " + (textB ?? "(nessuno)") + " mm3");
                else if (key == "features")
                    called.Add("le lavorazioni sono passate da " + (textA ?? "(nessuna)") +
                              " a " + (textB ?? "(nessuna)"));
                else
                    called.Add("il file e' stato riscritto (" + (textA ?? "(mai)") +
                              " -> " + (textB ?? "(mai)") + ")");
            }

            return called.Count == 0 ? null : string.Join("; ", called);
        }

        private static string ReadableName(Dictionary<string, object> identity)
        {
            if (identity == null) return "(nessuno)";
            var p = Convert.ToString(identity.TryGetValue("path", out var x) ? x : "");
            if (!string.IsNullOrWhiteSpace(p)) return p;
            var t = Convert.ToString(identity.TryGetValue("document_title", out var y) ? y : "");
            return string.IsNullOrWhiteSpace(t) ? "(senza nome)" : t;
        }

        private Dictionary<string, object> ListOperations(
            Dictionary<string, object> arguments)
        {
            if (_catalog.LoadingError != null)
                return Protocol.Content(_catalog.LoadingError, true);
            var family = Text(arguments, "family");
            var list = _catalog.Items(family);
            return Protocol.ContentWithData(Protocol.InJson(list), list);
        }

        private Dictionary<string, object> DescribeOperation(
            Dictionary<string, object> arguments)
        {
            if (_catalog.LoadingError != null)
                return Protocol.Content(_catalog.LoadingError, true);

            var itemName = Text(arguments, "name");
            var descriptionText = _catalog.Description(itemName);
            if (descriptionText == null)
            {
                // UN ERRORE CHE AIUTA invece di uno che constata: chi ha
                // sbagliato nome quasi sempre ne ha in mente uno vicino.
                var similar = _catalog.Similar(itemName);
                _registry.OperationThatDoesNotExist(itemName, similar);
                return Protocol.Content(
                    "Non c'e' nessuna operazione che si chiama «" + itemName + "»." +
                    (similar.Count > 0
                        ? " Forse cercavi: " + string.Join(", ", similar) + "."
                        : " Chiedi «list_operations» per vedere quali ci sono."),
                    true);
            }
            return Protocol.ContentWithData(Protocol.InJson(descriptionText), descriptionText);
        }

        /// <summary>
        /// Il giro vero: si esegue, e SI GUARDA COS'E' VENUTO.
        ///
        /// Il risultato non e' «riuscito». Sono le misure di ogni passo — il
        /// volume tolto, il diametro vero del foro, quante pareti ha forato —
        /// e lo stato del documento DOPO. E' l'unica forma che permette a chi
        /// ha chiesto di accorgersi che il pezzo non e' quello che voleva.
        /// </summary>
        /// <summary>
        /// La frase che si aggiunge a ogni rifiuto di FORMA.
        ///
        /// Il piano si controlla tutto prima di cominciare, quindi un errore al
        /// passo 3 ferma anche i primi due. La risposta pero' elenca solo il
        /// passo che ha sbagliato, e senza questa frase chi legge puo' credere
        /// che i precedenti siano stati eseguiti — e mettersi a cercare nel
        /// documento una piastra che non c'e'.
        /// </summary>
        private const string NothingTouched =
            " Il piano non e' stato eseguito per niente: il documento e' rimasto " +
            "com'era. Correggi questo passo e rimanda il piano intero.";

        /// <summary>
        /// `measure` — `build` senza il permesso di toccare.
        ///
        /// IL CANCELLO STA QUI E NON DENTRO `Build`, e il motivo e' che deve
        /// scattare PRIMA che il piano cominci. Un piano di tre misure e
        /// un'estrusione, fermato al quarto passo, avrebbe gia' misurato tre
        /// volte e poi rifiutato: chi legge la risposta vede tre misure buone e
        /// un errore, e non ha modo di sapere che il pezzo e' ancora intatto
        /// solo per fortuna. Qui si guardano TUTTI i passi, e se uno solo non e'
        /// una verifica non parte niente.
        ///
        /// SI DICE CON QUALE STRUMENTO SI FA, non solo che qui non si fa. Un
        /// rifiuto che non porta da nessuna parte costringe chi legge a
        /// indovinare, e indovinare e' il modo in cui si sbaglia in silenzio.
        /// </summary>
        /// <summary>
        /// L'impronta di un piano: le operazioni e i loro argomenti, in ordine.
        ///
        /// SI SERIALIZZA IN JSON e si passa per uno SHA-1 invece di confrontare
        /// le strutture: due dizionari con le stesse chiavi in ordine diverso
        /// sono lo stesso piano, e `InJson` li scrive nello stesso modo. Un
        /// confronto fatto a mano sui dizionari avrebbe dovuto decidere cosa
        /// conta e cosa no, e ogni decisione di quel genere e' un posto dove
        /// due piani diversi diventano uguali.
        /// </summary>
        private static string PlanFingerprint(object[] steps)
        {
            try
            {
                var text = Protocol.InJson(steps);
                using (var sha = System.Security.Cryptography.SHA1.Create())
                {
                    var b = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
                    var s = new System.Text.StringBuilder(b.Length * 2);
                    foreach (var x in b) s.Append(x.ToString("x2", CultureInfo.InvariantCulture));
                    return s.ToString();
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// Com'e' messo il documento, in poche misure: volume, corpi, quante
        /// lavorazioni.
        ///
        /// NON e' una firma crittografica dello stato intero, ed e' voluto. Lo
        /// stato completo cambia anche quando il pezzo non cambia — un uid che
        /// si rinumera, una faccia che entra o esce dal tetto — e un confronto
        /// su tutto direbbe «diverso» ogni volta, spegnendo il controllo senza
        /// che nessuno se ne accorga. **Un controllo che non scatta mai e' un
        /// controllo spento**, e questo e' il modo piu' comune di spegnerne uno:
        /// renderlo troppo sensibile.
        ///
        /// Le tre misure qui prese cambiano quando il PEZZO cambia, e non
        /// cambiano quando cambia solo come lo si e' guardato.
        /// </summary>
        private static string StateSummary(Dictionary<string, object> docState)
        {
            if (docState == null) return null;
            Func<string, string> theField = k =>
            {
                object v;
                return docState.TryGetValue(k, out v) && v != null
                    ? Convert.ToString(v, CultureInfo.InvariantCulture)
                    : "-";
            };
            return theField("volume_mm3") + "|" + theField("bodies") + "|" +
                   theField("how_many_features") + "|" + theField("document_title");
        }

        private Dictionary<string, object> Measure(Dictionary<string, object> arguments)
        {
            var steps = Take(arguments, "steps") as object[];
            if (steps == null || steps.Length == 0)
                return Protocol.Content(
                    "Non c'e' niente da misurare: «steps» deve essere un elenco " +
                    "di {operazione, argomenti}.", true);

            // UN CANCELLO CHE NON SA NIENTE DEVE CHIUDERE, NON APRIRE.
            //
            // Il cancello qui sotto decide guardando la FAMIGLIA, e la famiglia
            // la sa solo il catalogo. Se il catalogo non si e' caricato,
            // `Family()` risponde null per tutto, ogni passo passa il
            // controllo, e `measure` — che si e' dichiarata di sola lettura nel
            // protocollo, e per cui il client ha gia' deciso di NON chiedere
            // conferma — eseguirebbe un'estrusione.
            //
            // Sarebbe un difetto che non da' errore nella sua forma peggiore:
            // la difesa sparisce esattamente quando qualcosa e' gia' andato
            // storto, e il sintomo e' che tutto funziona.
            if (_catalog.LoadingError != null)
                return Protocol.Content(
                    "Non posso misurare: il catalogo non si e' caricato (" +
                    _catalog.LoadingError + "). Senza catalogo non so " +
                    "quali operazioni leggono e quali toccano il pezzo, e " +
                    "«measure» non esegue niente di cui non sappia rispondere. " +
                    "Con «build» lo stesso piano parte, ma il tuo client sa " +
                    "che quello puo' modificare.", true);

            var outOfPlace = new List<string>();
            for (int i = 0; i < steps.Length; i++)
            {
                var planStep = steps[i] as Dictionary<string, object>;
                string itemName = planStep == null ? null : Text(planStep, "operation");
                if (string.IsNullOrWhiteSpace(itemName)) continue;   // lo dira' Disegna

                string family = _catalog.Family(itemName);
                // Un'operazione che il catalogo non conosce NON e' un problema
                // di questo cancello: lo dice `Build`, con i suggerimenti di
                // `Similar`. Qui si tace, altrimenti lo stesso sbaglio darebbe
                // due messaggi diversi a seconda dello strumento.
                if (family == null) continue;
                if (!string.Equals(family, ReadOnlyFamily, StringComparison.OrdinalIgnoreCase))
                    outOfPlace.Add("il passo " + (i + 1) + ", «" + itemName +
                                   "» (famiglia «" + family + "»)");
            }

            if (outOfPlace.Count > 0)
                return Protocol.Content(
                    "«measure» legge e basta, e " +
                    (outOfPlace.Count == 1 ? "questo passo tocca" : "questi passi toccano") +
                    " il pezzo: " + string.Join("; ", outOfPlace) + ". " +
                    "Non ho eseguito niente — nemmeno le misure che venivano " +
                    "prima, perche' meta' piano e' un risultato che non si sa " +
                    "leggere. Per fare queste operazioni usa «build», che il " +
                    "tuo client sa essere distruttivo. Per sapere cosa sa " +
                    "leggere «measure»: «list_operations» con famiglia «" +
                    ReadOnlyFamily + "».", true);

            // SENZA la guardia sulla ripetizione: misurare due volte la stessa
            // cosa e' legittimo, e `measure` si dichiara idempotente.
            return RunPlan(arguments, checkIfRepeated: false);
        }

        /// <summary>
        /// L'unica famiglia che non tocca il pezzo.
        ///
        /// NON c'e' anche «identity» qui, e va detto perche' e' il verso in cui
        /// sbagliare costa di piu': `set_property`, `create_configuration` e
        /// `activate_configuration` SCRIVONO nel documento. Dichiararle di sola
        /// lettura vorrebbe dire un client che non chiede conferma mentre
        /// qualcuno cambia la configurazione attiva di un pezzo confermato.
        /// </summary>
        private const string ReadOnlyFamily = "checks";

        /// <summary>
        /// LA PROVA A VUOTO: sbagliare senza toccare il pezzo (voce Z-G6).
        ///
        /// ── PERCHE' SERVE ────────────────────────────────────────────
        ///
        /// `build` controlla gia' tutto il piano prima di cominciare, ed
        /// e' il pezzo piu' prezioso del ponte: un piano sbagliato non tocca
        /// niente. Ma quel controllo **non era raggiungibile senza eseguire**:
        /// per sapere se un piano regge bisognava lanciarlo, e un piano che
        /// regge a meta' lascia meta' pezzo fatto.
        ///
        /// ── COSA GUARDA, E SOPRATTUTTO COSA NO ───────────────────────
        ///
        /// Senza SOLIDWORKS si puo' dire molto: che l'operazione esista, che
        /// gli argomenti obbligatori ci siano, che non ce ne siano di
        /// **sconosciuti** — un argomento scritto male viene ignorato in
        /// silenzio, ed e' il difetto che non da' errore — e che un
        /// riferimento a un passo precedente punti a un passo che esiste.
        ///
        /// Non si puo' dire se un `uid` e' ancora valido, se il documento e'
        /// del tipo giusto, se la faccia c'e' ancora: per quello va letto il
        /// documento. **La risposta lo dice a chiare lettere**, perche' una
        /// prova a vuoto che tace su cio' che non ha guardato e' peggio di
        /// nessuna prova: fa credere che il piano sia a posto.
        /// </summary>
        private Dictionary<string, object> TestThePlan(Dictionary<string, object> arguments)
        {
            if (_catalog.LoadingError != null)
                return Protocol.Content(_catalog.LoadingError, true);

            var steps = Take(arguments, "steps") as object[];
            if (steps == null || steps.Length == 0)
                return Protocol.Content(
                    "Il piano e' vuoto: «steps» deve essere un elenco di " +
                    "{operazione, argomenti}.", true);

            var trouble = new List<string>();

            // L'AVANZAMENTO DELLA PROVA A VUOTO (voce W2.2). Qui non si
            // esegue niente, quindi il totale e' il numero dei passi e basta:
            // non c'e' nessuna seconda meta' da aspettare.
            _wire.Total = steps.Length;

            for (int i = 0; i < steps.Length; i++)
            {
                var planStep = steps[i] as Dictionary<string, object>;
                string location = "passo " + (i + 1);
                _wire.Step(i + 1, "controllo il " + location + " di " + steps.Length);
                if (planStep == null) { trouble.Add(location + ": non e' un oggetto"); continue; }

                var itemName = Text(planStep, "operation");
                if (string.IsNullOrWhiteSpace(itemName))
                { trouble.Add(location + ": manca «operation»"); continue; }

                if (!_catalog.Knows(itemName))
                {
                    var similar = _catalog.Similar(itemName);
                    _registry.OperationThatDoesNotExist(itemName, similar);
                    trouble.Add(location + ": «" + itemName + "» non esiste" +
                             (similar != null && similar.Count > 0
                                 ? ". Forse: " + string.Join(", ", similar)
                                 : ""));
                    continue;
                }

                var schema = _catalog.Schema(itemName);
                var data = Take(planStep, "arguments") as Dictionary<string, object>
                           ?? new Dictionary<string, object>();
                if (schema == null) continue;

                var property = Take(schema, "properties") as Dictionary<string, object>;
                var requested = Take(schema, "required") as object[];

                if (requested != null)
                    foreach (var r in requested)
                    {
                        var theField = Convert.ToString(r);
                        if (string.IsNullOrEmpty(theField)) continue;
                        // `x_rif` vale per `x_uid` e viceversa: il catalogo
                        // dichiara obbligatorio uno dei due, e chiederli
                        // tutti e due sarebbe un rifiuto sbagliato.
                        var twin = theField.EndsWith("_uid", StringComparison.Ordinal)
                            ? theField.Substring(0, theField.Length - 4) + "_ref"
                            : theField.EndsWith("_ref", StringComparison.Ordinal)
                                ? theField.Substring(0, theField.Length - 4) + "_uid"
                                : null;
                        if (data.ContainsKey(theField)) continue;
                        if (twin != null && data.ContainsKey(twin)) continue;
                        trouble.Add(location + " («" + itemName + "»): manca «" + theField + "»");
                    }

                if (property != null)
                    foreach (var theField in data.Keys)
                    {
                        if (property.ContainsKey(theField)) continue;
                        // UN ARGOMENTO SCONOSCIUTO NON E' UN DETTAGLIO: viene
                        // ignorato in silenzio, e il pezzo esce diverso da
                        // come lo si e' chiesto senza nessun errore.
                        trouble.Add(location + " («" + itemName + "»): «" + theField +
                                 "» non e' un argomento di questa operazione, " +
                                 "e verrebbe IGNORATO");
                    }

                // I riferimenti a un passo precedente: `{step: N, kind: ...}`.
                foreach (var pair in data)
                {
                    if (!pair.Key.EndsWith("_ref", StringComparison.Ordinal)) continue;
                    var refObj = pair.Value as Dictionary<string, object>;
                    if (refObj == null) continue;
                    var n = Take(refObj, "step");
                    int which;
                    if (n == null || !int.TryParse(Convert.ToString(n), out which)) continue;
                    if (which >= 1 && which <= i) continue;
                    trouble.Add(location + " («" + itemName + "»): «" + pair.Key +
                             "» rimanda al passo " + which + ", che " +
                             (which > i ? "non e' ancora stato fatto" : "non esiste"));
                }
            }

            var text = new StringBuilder();
            text.Append(trouble.Count == 0
                ? "PROVA A VUOTO: il piano regge, per quello che si puo' " +
                  "sapere senza guardare il documento.\n\n"
                : "PROVA A VUOTO: " + trouble.Count + " cose da sistemare.\n\n");
            foreach (var g in trouble) text.Append("  - " + g + "\n");
            if (trouble.Count > 0) text.Append("\n");

            // COSA NON E' STATO GUARDATO. Questa parte c'e' SEMPRE, anche
            // quando il piano regge -- soprattutto quando regge.
            text.Append("Controllati: i nomi delle operazioni, gli argomenti " +
                         "obbligatori, quelli sconosciuti (che verrebbero " +
                         "ignorati in silenzio) e i rimandi a passi " +
                         "precedenti.\n\n");
            text.Append("NON controllati, e per questi serve leggere il " +
                         "documento: che gli «uid» siano ancora validi, che il " +
                         "documento sia del tipo giusto per queste operazioni, " +
                         "che le facce e gli spigoli nominati esistano ancora, " +
                         "e se la geometria che chiedi sia possibile. " +
                         (_session.Attached
                             ? "SOLIDWORKS e' agganciato: «build» senza «dry_run» " +
                               "li controlla tutti prima di toccare qualunque cosa."
                             : "SOLIDWORKS NON e' agganciato adesso.") + "\n");

            return Protocol.Content(text.ToString(), trouble.Count > 0);
        }
        private Dictionary<string, object> Build(Dictionary<string, object> arguments)
        {
            // LA PROVA A VUOTO NON TOCCA NIENTE, e non vuole nemmeno che
            // SOLIDWORKS sia agganciato: serve proprio a sbagliare prima.
            var requested = Take(arguments, "dry_run");
            bool test = false;
            if (requested != null) { try { test = Convert.ToBoolean(requested); } catch { } }
            if (test) return TestThePlan(arguments);
            return RunPlan(arguments, checkIfRepeated: true);
        }

        /// <summary>
        /// Il corpo di `build`, con o senza la guardia sulla ripetizione.
        ///
        /// ── PERCHE' `measure` NON LA VUOLE, ED E' UN BUG TROVATO SCRIVENDOLA ──
        ///
        /// `measure` passa di qui: sono le stesse operazioni, con un cancello
        /// davanti. Ma **misurare due volte la stessa cosa e' legittimo** — anzi,
        /// e' il caso normale: si guarda, si pensa, si riguarda. E siccome una
        /// misura non cambia il documento, lo stato dopo e' identico allo stato
        /// prima: la guardia sulla ripetizione avrebbe visto un piano identico
        /// su un documento identico e **avrebbe rifiutato la seconda misura.**
        ///
        /// Non sarebbe stato solo scomodo: `measure` si dichiara
        /// `idempotentHint: true` nelle annotazioni, e un client legge quella
        /// riga per decidere se puo' riprovare da solo. Rifiutare il secondo
        /// tentativo avrebbe reso quella dichiarazione **una bugia nel
        /// protocollo** — la stessa categoria di difetto che `measure` esiste per
        /// non avere.
        ///
        /// Un parametro con valore predefinito su `Build` sarebbe bastato, ma
        /// qui si tiene la regola di casa: metodo nuovo, nome diverso.
        /// </summary>
        /// <summary>
        /// LE OPERAZIONI CHE NON SI ANNULLANO (voce W2.5).
        ///
        /// **Una sola, e il criterio conta piu' dell'elenco**: ci finisce
        /// cio' che toglie qualcosa che non si puo' rimettere con
        /// un'operazione del catalogo. `suppress_feature` non c'e',
        /// perche' esiste `reactivate`; `delete_feature` si', perche' una
        /// lavorazione eliminata non torna.
        ///
        /// Un elenco lungo sarebbe peggio: chi conferma dieci volte di fila
        /// smette di leggere cosa conferma, ed e' la stessa ragione per cui
        /// `measure` esiste separata da `build`.
        /// </summary>
        private static readonly string[] CannotBeUndone = { "delete_feature" };

        private Dictionary<string, object> RunPlan(
            Dictionary<string, object> arguments, bool checkIfRepeated)
        {
            var steps = Take(arguments, "steps") as object[];
            if (steps == null || steps.Length == 0)
                return Protocol.Content(
                    "Il piano e' vuoto: «steps» deve essere un elenco di " +
                    "{operazione, argomenti}.", true);

            // ── SI CHIEDE PRIMA DI TOCCARE QUALUNQUE COSA (voce W2.5) ──
            //
            // Prima anche di guardare se SOLIDWORKS e' agganciato, e non e'
            // un dettaglio d'ordine: leggere lo stato del documento e' gia'
            // una conversazione con SOLIDWORKS, e una conversazione con
            // SOLIDWORKS puo' non tornare (voce Z-C4). La domanda riguarda
            // l'intenzione, e l'intenzione si puo' chiedere a costo zero.
            //
            // `measure` non passa di qui con `checkIfRepeated` falso? Passa,
            // ma non puo' eseguire operazioni distruttive: il cancello e'
            // prima. Chiedere conferma li' sarebbe chiedere il permesso di
            // fare una cosa che verra' rifiutata comunque.
            if (checkIfRepeated)
            {
                var toConfirm = new List<string>();
                foreach (var x in steps)
                {
                    var planStep = x as Dictionary<string, object>;
                    if (planStep == null) continue;
                    var itemName = Text(planStep, "operation");
                    if (string.IsNullOrEmpty(itemName)) continue;
                    foreach (var bad in CannotBeUndone)
                        if (string.Equals(itemName, bad, StringComparison.OrdinalIgnoreCase) &&
                            !toConfirm.Contains(itemName))
                            toConfirm.Add(itemName);
                }

                if (toConfirm.Count > 0)
                {
                    var question =
                        "Questo piano contiene " +
                        (toConfirm.Count == 1 ? "un'operazione che NON si annulla"
                                                 : "operazioni che NON si annullano") +
                        ": " + string.Join(", ", toConfirm) +
                        ". Quello che toglie non si rimette con nessuna " +
                        "operazione del catalogo. Vuoi che proceda?";

                    if (Oce.Solid.Agent.Mcp.Elicitation.ClientCanAsk)
                    {
                        if (!Oce.Solid.Agent.Mcp.Elicitation.Confirm(Console.Out, Console.In, question))
                            return Protocol.Content(
                                "Fermato: la conferma non e' arrivata, e non ho " +
                                "toccato niente. " + question, true);
                    }
                    else
                    {
                        // IL RIPIEGO SI DICHIARA (voce W2.5). Andare avanti
                        // in silenzio sarebbe il modo in cui una difesa
                        // sparisce senza che nessuno se ne accorga.
                        Protocol.Note(
                            "[ponte] " + string.Join(", ", toConfirm) +
                            ": non ho potuto chiedere conferma all'utente — " +
                            "questo client non dichiara «elicitation». Vado " +
                            "avanti come prima.");
                    }
                }
            }

            if (!_session.Attached)
                return Protocol.Content("Il ponte non e' agganciato a SolidWorks.", true);

            // SENZA ANCORA: questa lettura serve a sapere su QUALE documento si
            // lavora, quanto volume c'era e quante lavorazioni erano gia' in
            // errore. Non se ne costruiscono firme (quelle si fanno su «after»,
            // piu' sotto) e non torna a chi legge. L'ancora costa un
            // GetTessTriangles per faccia, e qui si pagava per buttarla.
            var before = _session.StateWithoutAnchor(out var readError);
            if (before == null)
                return Protocol.Content(readError, true);

            // SI DISEGNA SUL DOCUMENTO CHE SI E' GUARDATO, NON SU QUELLO DAVANTI.
            //
            // Il ponte lavora sul documento ATTIVO, e il documento attivo e'
            // quello la cui finestra sta davanti: cambia se qualcuno clicca,
            // se un'altra operazione ne apre uno, se SolidWorks ne porta uno in
            // primo piano da solo. Il 14 settembre 2026 e' successo davvero —
            // un blocco 314x274x52 estruso dentro il pezzo di qualcun altro,
            // senza un errore, senza un avviso.
            //
            // Qui non si indovina e non si cambia finestra da soli: si
            // CONFRONTA con quello che «inspect_part» aveva descritto, e se
            // non combacia ci si ferma. Chi sa cosa sta facendo puo' dire
            // esplicitamente «si', quello davanti» con `qualunque_documento`.
            var now = StateIdentity(before);
            var named = Text(arguments, "document");

            // SI NOMINA IL DOCUMENTO, NON SI SPEGNE IL CONTROLLO.
            //
            // La prima versione aveva un `qualunque_documento: true` per
            // scavalcare il rifiuto. Era un difetto: un argomento che
            // disattiva una difesa e' un invito a disattivarla, e chi lo
            // incontra ha tutti gli incentivi per riprovare con quello
            // acceso — cioe' la difesa sparisce proprio nel caso in cui
            // serviva.
            //
            // Nominare il documento chiede la stessa cosa in una forma che
            // non si puo' dare a caso: per scriverne il nome bisogna averlo
            // guardato. Un booleano lo mette chiunque; un nome sbagliato
            // viene rifiutato come tutto il resto.
            bool namedIt =
                !string.IsNullOrWhiteSpace(named) &&
                Bridge.Session.SameDocument(
                    now,
                    new Dictionary<string, object>
                    {
                        [named.IndexOfAny(new[] { '\\', '/' }) >= 0
                            ? "path" : "document_title"] = named,
                    });

            if (!namedIt)
            {
                if (_seen == null)
                    return Protocol.Content(
                        "Non ho ancora guardato nessun pezzo, quindi non so su quale " +
                        "documento vuoi che disegni: adesso davanti c'e' «" +
                        ReadableName(now) + "». Chiama prima «inspect_part» — " +
                        "serve comunque, perche' gli identificativi delle facce " +
                        "nascono li'.", true);

                if (!Bridge.Session.SameDocument(_seen, now))
                    return Protocol.Content(
                        "Mi fermo: il documento e' cambiato. Avevi guardato «" +
                        ReadableName(_seen) + "», adesso davanti c'e' «" +
                        ReadableName(now) + "». Disegnare qui vorrebbe dire " +
                        "scrivere nel pezzo sbagliato, e gli identificativi del tuo " +
                        "piano non valgono su questo.\n\n" +
                        "Se e' cambiato per una ragione che conosci, rileggilo con " +
                        "«inspect_part». Se sai gia' che vuoi lavorare proprio su " +
                        "quello che c'e' adesso, ripeti passando «document»: \"" +
                        ReadableName(now) + "\".", true);
            }
            // ── LO STESSO DOCUMENTO, CON DENTRO UN'ALTRA COSA (voce X2) ──
            //
            // Il controllo qui sopra chiede «e' lo stesso documento?». Questo
            // chiede «e' ancora lo stesso pezzo?», e sono due domande diverse:
            // titolo e percorso combaciano anche mentre qualcuno tira una
            // quota a mano nella finestra di SOLIDWORKS.
            //
            // Si guarda SOLO se il documento davanti e' quello guardato. Se il
            // piano ne nomina un altro, la firma vecchia parla di un pezzo
            // diverso e confrontarla produrrebbe una frase vera su due cose
            // che non c'entrano niente.
            bool theCheckedOne =
                _seen != null && Bridge.Session.SameDocument(_seen, now);

            if (theCheckedOne)
            {
                var changed = WhatChanged(_signatureSeen, LightSignature(before));
                if (changed != null)
                    return Protocol.Content(
                        "Mi fermo: il pezzo e' cambiato da quando l'hai guardato — " +
                        changed + ".\n\n" +
                        "Il documento e' giusto, il contenuto no. Gli identificativi " +
                        "delle facce («F1», «F2», ...) sono POSIZIONALI: nascono da una " +
                        "lettura e valgono per quella. Su una geometria cambiata «F1» " +
                        "puo' indicare un'altra faccia, e l'operazione riuscirebbe nel " +
                        "posto sbagliato senza dare nessun errore.\n\n" +
                        "Rileggi con «inspect_part» e rifa' il piano sugli " +
                        "identificativi nuovi.", true);
            }

            // LE FIRME VALGONO SOLO SUL DOCUMENTO CHE LE HA PRODOTTE.
            // Se il piano nomina un altro documento, gli uid del piano non
            // parlano di questo e allegare le firme vecchie produrrebbe
            // rifiuti che non c'entrano niente. Meglio nessun confronto che
            // un confronto fra due pezzi diversi.
            var signatures = theCheckedOne ? _seenSignatures : null;

            // ── TRE CAMPI CHE DEVONO PARLARE DELLO STESSO PEZZO (Z-A1) ──
            //
            // `_guardato` dice QUALE documento, `_firmaGuardata` e
            // `_firmeViste` dicono COM'ERA. Finche' questa riga spostava
            // solo il primo, bastava un piano che nominasse un altro
            // documento — `document: "..."`, la strada legittima — perche'
            // da quel momento `_guardato` indicasse il pezzo nuovo e le
            // firme il pezzo vecchio.
            //
            // Il guaio non si vede subito: si vede alla chiamata DOPO, dove
            // `theCheckedOne` torna vero e il confronto gira contro le
            // firme di un altro pezzo. O rifiuta un piano buono dicendo «il
            // pezzo e' cambiato», o — peggio — lascia passare un piano su
            // uid che non valgono piu'.
            //
            // Il principio giusto e' scritto nel commento qui sopra, per la
            // variabile locale: **meglio nessun confronto che un confronto
            // fra due pezzi diversi.** Qui lo si applica anche ai campi che
            // restano: se il documento cambia, le firme vecchie si buttano.
            // Le rifara' `inspect_part`, oppure la fine di questo piano.
            if (!theCheckedOne)
            {
                _seenSignatures = null;
                _signatureSeen = null;
            }
            _seen = now;

            // ── LO STESSO PIANO, DUE VOLTE (voce W2.7) ──────────────────
            //
            // Un client che non riceve la risposta — rete, timeout, riavvio —
            // rimanda la stessa richiesta. E' il comportamento normale di
            // qualunque client, ed e' documentato nelle annotazioni di questo
            // ponte: `build` **non** e' idempotente, rieseguire lo stesso
            // piano non rimette il pezzo com'era, **fa un secondo foro**.
            //
            // Finora niente lo fermava. Adesso si', e il criterio e' stretto
            // apposta: si rifiuta **solo** se il piano e' identico a quello di
            // prima **e** il documento e' ancora esattamente come quel piano
            // l'ha lasciato. Le due condizioni insieme descrivono un caso solo:
            // il rinvio.
            //
            // Non basta la prima. Due piani identici su documenti in stati
            // diversi fanno cose diverse e possono essere tutti e due voluti.
            // E non basta la seconda: un documento che non e' cambiato non e'
            // di per se' un motivo per rifiutare.
            string planFingerprint = PlanFingerprint(steps);
            if (checkIfRepeated &&
                _lastPlanDone != null &&
                _lastPlanDone == planFingerprint &&
                Bridge.Session.SameDocument(_planDocument, now) &&
                _summaryAfterPlan != null &&
                _summaryAfterPlan == StateSummary(before))
            {
                return Protocol.Content(
                    "Questo piano l'ho gia' eseguito, e il documento e' ancora " +
                    "esattamente come l'ho lasciato. Non lo rifaccio: «build» " +
                    "non e' ripetibile — rieseguirlo non rimette il pezzo com'era, " +
                    "farebbe un SECONDO foro.\n\n" +
                    "Se la risposta di prima non ti e' arrivata, chiama " +
                    "«inspect_part»: lo stato di adesso e' il risultato di " +
                    "quel piano.\n\n" +
                    "Se invece vuoi davvero rifarlo, cambia qualcosa nel piano " +
                    "oppure usa «undo» e poi rilancialo: cosi' e' una scelta, " +
                    "non un rinvio.", true);
            }

            // IL PIANO SI CONTROLLA TUTTO PRIMA DI TOCCARE IL DOCUMENTO.
            //
            // Nome dell'operazione, percorsi e forma degli argomenti si vedono
            // leggendo, senza aprire niente. Prima si controllavano a mano a
            // mano: un errore di forma al passo 3 fermava il piano con i primi
            // due GIA' COSTRUITI, e nel documento restava mezzo pezzo per una
            // cosa che si poteva dire prima di cominciare.
            //
            // Adesso o il piano e' scrivibile tutto, o non si tocca niente.
            var toDo = new List<Oce.Solid.Agent.Core.PlanStep>();
            var outcomes = new List<object>();
            bool everythingFine = true;
            int order = 0;

            // L'AVANZAMENTO DI UN PIANO VERO SI RACCONTA IN DUE META'
            // (voce W2.2): prima lo si controlla tutto a documento intatto,
            // poi lo si esegue. Il totale le tiene insieme — la seconda meta'
            // la manda `PlanInMemory`, che riprende il conto da dove questo
            // giro l'ha lasciato invece di ricalcolarselo.
            //
            // Dire «passo 3 di 5» durante il controllo e poi ricominciare da
            // «1 di 5» all'esecuzione farebbe tornare indietro la barra di chi
            // legge, e la specifica pretende che `progress` cresca.
            _wire.Total = steps.Length * 2;

            foreach (var p in steps)
            {
                var planStep = p as Dictionary<string, object>;
                if (planStep == null) continue;
                order++;

                var itemName = Text(planStep, "operation");
                _wire.Step(order, "controllo il passo " + order + " di " +
                                    steps.Length + ": " + itemName);
                var itsItems = Take(planStep, "arguments") as Dictionary<string, object>
                           ?? new Dictionary<string, object>();

                // SI CONTROLLA IL NOME PRIMA DI ESEGUIRE. L'add-in, davanti a
                // un nome che non conosce ma con dei «contours» dentro, ricade
                // sull'estrusione di profilo: comodo per le ricette del server,
                // pericoloso qui, dove un nome storto diventerebbe un pezzo
                // storto senza che nessuno dica niente.
                if (!_catalog.Knows(itemName))
                {
                    var similar = _catalog.Similar(itemName);
                    _registry.OperationThatDoesNotExist(itemName, similar);
                    outcomes.Add(new Dictionary<string, object>
                    {
                        ["operation"] = itemName,
                        ["succeeded"] = false,
                        ["error"] =
                            "Non esiste nessuna operazione «" + itemName + "»." +
                            (similar.Count > 0
                                ? " Forse cercavi: " + string.Join(", ", similar) + "."
                                : "") + NothingTouched,
                    });
                    everythingFine = false;
                    break;
                }

                // THE STEP GOES ON UNDER THE CATALOGUE'S NAME (Z-C21.10,
                // pattern S20). `Knows` finds the operation ignoring case,
                // and until 0.140.0 the step went on under the name as it was
                // written. The library dispatches Ordinal: «Estrudi_Rettangolo»
                // came back «Operazione sconosciuta» right after this check had
                // said it exists, and «Rivoluzione», which carries «contours»,
                // fell into the profile extrusion the comment above warns
                // about, with no «thickness_mm». Same rule as the enum values
                // in Bridge/Shape.cs: what passes here goes on spelled as the
                // catalogue spells it.
                itemName = Text(_catalog.Find(itemName), "name") ?? itemName;

                // I PERCORSI SI CONTROLLANO PRIMA DI ESEGUIRE.
                // Sono l'unica cosa che questo ponte scrive fuori dal
                // documento aperto, e arrivano da un testo generato.
                var crookedPath = Bridge.Paths.Check(itsItems);
                if (crookedPath != null)
                {
                    outcomes.Add(new Dictionary<string, object>
                    {
                        ["operation"] = itemName,
                        ["succeeded"] = false,
                        ["error"] = crookedPath + NothingTouched,
                    });
                    everythingFine = false;
                    break;
                }

                // GLI ARGOMENTI RISPETTANO LO SCHEMA CHE ABBIAMO
                // DICHIARATO NOI? Il ponte offre trentasei operazioni con
                // schemi dettagliati: non guardarli vuol dire che lo
                // schema serve solo a suggerire, e che uno «thickness_mm»
                // dimenticato arriva a valle come zero — un'estrusione di
                // spessore nullo, rifiutata da SOLIDWORKS con un messaggio
                // suo, nel migliore dei casi.
                //
                // SOLO LA FORMA, MAI IL MERITO: vedi Bridge/Shape.cs.
                //
                // AND THE FORMA REWRITES `suoi` IN PLACE (Z-C21.10, S20): an
                // enum value accepted with a different case («Superiore»)
                // comes out spelled as the catalogue spells it («top»).
                // `suoi` is the very dictionary the step below hands to the
                // library, which compares Ordinal.
                var crookedShape = Bridge.Shape.Check(
                    itemName, _catalog.Schema(itemName), itsItems);
                if (crookedShape != null)
                {
                    outcomes.Add(new Dictionary<string, object>
                    {
                        ["operation"] = itemName,
                        ["succeeded"] = false,
                        ["error"] = crookedShape + NothingTouched,
                    });
                    everythingFine = false;
                    break;
                }

                // GLI IDENTIFICATIVI CITATI ESISTONO DAVVERO?
                //
                // E' l'altra meta' della difesa anti-deriva, e prende un caso
                // che le FIRME non possono prendere: un piano che crea il pezzo
                // da zero e poi cita «F1» non ha nessuna firma da confrontare,
                // perche' quando lo stato e' stato letto quella faccia non
                // c'era. Nessuna firma, nessun confronto, e il piano passa.
                //
                // Misurato il 14/9/2026: `[extrude_rectangle, cylindrical_cut
                // su F1]` su un documento vuoto riusciva, volume 117.738,1. Il
                // foro e' finito sulla faccia piu' grande — che in quel caso era
                // anche quella giusta, ma **non l'aveva scelta nessuno**: «F1»
                // e' posizionale e si rinumera per area a ogni lettura.
                var crookedUid = UidsThatDoNotExist(
                    _catalog.Schema(itemName), itsItems, before);
                if (crookedUid != null)
                {
                    outcomes.Add(new Dictionary<string, object>
                    {
                        ["operation"] = itemName,
                        ["succeeded"] = false,
                        ["error"] = crookedUid + NothingTouched,
                    });
                    everythingFine = false;
                    break;
                }

                // LE FIRME DI QUANDO IL PIANO E' NATO, ALLEGATE AL PASSO.
                // Non si sovrascrive quello che il chiamante ha gia' messo:
                // un piano che porta le sue firme sa meglio di noi di cosa
                // sta parlando.
                if (signatures != null && !itsItems.ContainsKey("_signatures"))
                    itsItems["_signatures"] = signatures;

                toDo.Add(new Oce.Solid.Agent.Core.PlanStep
                {
                    Order = order,
                    Operation = itemName,
                    Arguments = itsItems,
                    Summary = itemName,
                });
            }

            // IL GIRO E' QUELLO DELLA LIBRERIA, NON UNO NOSTRO.
            // Da qui arrivano i riferimenti fra passi («la faccia in cima a
            // quello che hai fatto al passo 1») e la voce di annullamento
            // unica: un solo Ctrl+Z disfa il piano intero.
            // What the executor said about its document and its undo entry
            // (Z-C21.14). Stays null when the plan never reached it: refused
            // above, before anything was written.
            Oce.Solid.Agent.Core.PlanExecutor.Result planRun = null;
            bool executorThrew = false;
            object turnRefusedOn = null;
            if (everythingFine)
            {
                // THE EXECUTOR RUNS ON THE DOCUMENT CHECKED ABOVE (review F7).
                // Up to bridge 0.125.0 it took the active document by itself,
                // so a window brought to the front between the reading of
                // `before` and this call got the plan, and the memory mixed the
                // numbers of two documents. It now stops before writing when
                // the active document is not `adesso`.
                var done = _session.ExecutePlan(toDo, out var runError, _wire,
                                                  IdentityForTheExecutor(now));
                planRun = _session.LastPlanResult;
                executorThrew = _session.LastPlanExecutorThrew;
                turnRefusedOn = _session.LastTurnRefusedDocument;
                if (done != null) foreach (var e in done) outcomes.Add(e);
                if (!string.IsNullOrEmpty(runError))
                {
                    everythingFine = false;
                    // Un piano fermato PRIMA del primo passo non lascia nessun
                    // esito: senza questo, la risposta sarebbe un elenco vuoto
                    // e un «succeeded» implicito che nessuno ha detto.
                    if (done == null || done.Count == 0)
                        outcomes.Add(new Dictionary<string, object>
                        {
                            ["operation"] = toDo.Count > 0 ? toDo[0].Operation : null,
                            ["succeeded"] = false,
                            ["error"] = runError,
                        });
                }
            }

            var after = _session.State(out var stateError);

            // ── TWO READINGS WHEN THE PLAN ENDS ON ANOTHER WINDOW (Z-C21.14) ──
            //
            // `after` above is the document IN FRONT. Up to bridge 0.124.1 it
            // was also the only reading after the plan, and when the window
            // changed during the plan (a `create_drawing`, another session, the
            // person) the undo memory got the numbers of the other
            // document: the live probe M19 (1/10/2026, line BRIDGE-R1 (X2))
            // saw the next undo refuse a right undo («the part changed
            // after the plan»). The plan's own numbers are now read on the
            // plan's document, by the reference the executor kept
            // (`PlanDocument`, the expectedDocument it read once at the start).
            //
            // «state_after» KEEPS DESCRIBING THE DOCUMENT IN FRONT, on purpose:
            // the next plan works on the document in front, and after
            // [..., create_drawing] the next plan is `standard_views` on the
            // drawing, the most normal sequence there is. What describes the
            // plan's document is `volume_after_mm3` (paired with
            // `volume_before_mm3`, read on the same document before), the error
            // counts and the undo memory; and when the two documents
            // differ the answer says which one is in front.
            var frontIdentity = StateIdentity(after);
            var planStateAfter = after;
            var planIdentity = now;
            string planStateError = null;
            bool anotherInFront = false;
            // A plan the executor stopped before writing (its checks, or the
            // active document not the one checked here, review F7) worked on
            // nothing: its PlanDocument is only the window that was in front,
            // possibly a foreign one, and must not become the plan's document.
            bool wroteOnADocument = planRun != null && !planRun.StoppedBeforeWriting;
            object planDocumentHeld = wroteOnADocument ? planRun.PlanDocument : null;
            if (planDocumentHeld != null)
            {
                planIdentity = _session.Identity(planDocumentHeld) ?? now;
                if (!_session.IsInFront(planDocumentHeld))
                {
                    anotherInFront = true;
                    planStateAfter = _session.StateOfDocument(planDocumentHeld, out planStateError);
                }
            }
            else if (frontIdentity != null && !Bridge.Session.SameDocument(now, frontIdentity))
            {
                anotherInFront = true;
                // The executor held no document. When it was never reached, or
                // stopped before writing, nothing was written and the document
                // of this call is as it was read before. When it threw (review
                // F9), what it wrote is not known and the document is not held:
                // no reading after.
                planStateAfter = planRun == null || planRun.StoppedBeforeWriting ? before : null;
            }

            // ── IS THE WINDOW IN FRONT THE PLAN'S? (review F5) ──────────────
            //
            // It is when the plan ended on it: the plan document, or the new
            // document a step declared (the drawing of `create_drawing`). When a
            // window came to the front from outside during the plan (scenario
            // X2 of the probe M19: another session, the person), it is not, and
            // up to bridge 0.125.0 its identity and signatures were adopted
            // anyway: a retry of the refused step, written for the plan's part,
            // passed the «document changed» check and wrote into the other
            // document, the class of the 14 September 2026. Now nothing is
            // adopted from a window the plan did not end on: the identity and
            // the signatures are cleared, and the next plan asks for
            // «inspect_part» first.
            bool frontIsThePlans;
            object finalDocument = wroteOnADocument ? planRun.FinalDocument : null;
            if (finalDocument != null)
                frontIsThePlans = _session.IsInFront(finalDocument) ||
                                  (planDocumentHeld != null && !anotherInFront);
            else
                frontIsThePlans = frontIdentity != null &&
                                  Bridge.Session.SameDocument(planIdentity, frontIdentity);

            // «state_after» E' LA LETTURA SUCCESSIVA, E VA FIRMATA COME TALE.
            //
            // Questa risposta porta con se' lo stato del documento dopo i
            // passi, con gli identificativi nuovi: chi legge scrivera' il
            // piano seguente su QUESTI, non su quelli di «inspect_part».
            // Se le firme restassero quelle di prima, il piano seguente —
            // scritto giusto — verrebbe rifiutato perche' confrontato con una
            // lettura che il suo autore non ha mai visto. Sarebbe un rifiuto
            // di cio' che e' corretto, che e' il difetto peggiore dei due.
            // E LA FIRMA SEGUE LA STESSA REGOLA (voce X2). Il pezzo e'
            // cambiato, ma l'ha cambiato il ponte facendo quello che gli e'
            // stato chiesto: se la firma restasse quella di
            // «inspect_part», il `build` seguente si fermerebbe
            // dicendo «qualcuno ha toccato il pezzo a mano». Sarebbe il
            // falso positivo che la voce X2 metteva fra i rischi, e sarebbe
            // fastidioso al punto giusto per insegnare a ignorare l'avviso.
            if (after != null && frontIsThePlans)
            {
                _seenSignatures = StateSignatures(after);
                _signatureSeen = LightSignature(after);

                // ── E ANCHE CHI E', NON SOLO COM'E' FATTO ────────────────
                //
                // `_guardato` veniva riportato all'identita' letta PRIMA di
                // eseguire, e un piano che contiene `save_document` cambia
                // l'identita' del documento mentre gira: prima del salvataggio
                // e' «Parte1597» senza percorso, dopo e' un percorso su disco.
                //
                // **Il `build` seguente veniva rifiutato**, con un messaggio
                // giusto e una causa sbagliata: «il documento e' cambiato,
                // avevi guardato Parte1597». Era cambiato, si' — l'aveva
                // cambiato il ponte facendo quello che gli era stato chiesto.
                //
                // Trovato il 23/9/2026 con una sonda che preparava una tavola:
                // estrudi -> taglia -> salva -> create_drawing, che e' la
                // sequenza piu' normale che esista, e il quarto passo non
                // partiva. Il banco non poteva vederlo: chiama la libreria,
                // non il ponte. L'ha visto il gruppo che passa dalla porta del
                // cliente (voce F14), il giorno dopo essere nato.
                //
                // `undo` faceva gia' cosi' tre metodi piu' sotto. Qui no.
                _seen = StateIdentity(after);
            }
            else
            {
                // A window the plan did not end on, or no reading (review F5).
                _seen = null;
                _seenSignatures = null;
                _signatureSeen = null;
            }
            if (after != null) RemoveAnchors(after);

            var result = new Dictionary<string, object>
            {
                // SI DICE SEMPRE SU COSA SI E' LAVORATO. Anche quando e' ovvio:
                // e' l'unico modo perche' chi legge se ne accorga quando ovvio
                // non era.
                ["document"] = ReadableName(now),
                ["outcomes"] = outcomes,
                ["state_after"] = after,
                ["note_state"] = after == null ? stateError : null,
                ["volume_before_mm3"] = Volume(before),
                // On the plan's document, like the line above (Z-C21.14).
                ["volume_after_mm3"] = Volume(planStateAfter),
            };

            // WHICH DOCUMENT IS IN FRONT, when it is not the plan's (Z-C21.14),
            // and whether the next plan may work on it (review F5).
            if (anotherInFront)
            {
                result["document_in_front"] = ReadableName(frontIdentity);
                result["note_document_in_front"] =
                    NoteDocumentInFront(ReadableName(planIdentity), ReadableName(frontIdentity), frontIsThePlans) +
                    (planStateAfter == null && planStateError != null ? " " + planStateError : "");
            }

            // WHETHER THE PLAN LEFT AN UNDO ENTRY (Z-C21.14): true, one entry
            // with the plan in it sits on top of that document's undo stack;
            // false, SOLIDWORKS answered false (measured for an empty
            // recording; whether that leaves an entry on the stack is
            // undecided, probe M19 phase H4); null, not known.
            if (planRun != null)
                result["undo_entry_recorded"] = planRun.UndoEntryOnPlanDocument;

            // IL CONTEGGIO DEGLI ERRORI SI DA' COME DELTA, NON COME NUMERO.
            //
            // «features_in_error: 1» dopo un piano sembra dire «il piano ha
            // rotto qualcosa». Puo' voler dire l'opposto: il pezzo era gia'
            // rotto quando e' arrivato, e il piano non c'entra. Il numero
            // assoluto mente su chi sia stato, ed e' il rischio scritto nella
            // voce D5.
            //
            // Si dice quanti ce n'erano prima solo quando cambia qualcosa: su
            // un pezzo sano che resta sano non c'e' niente da dire, e ripeterlo
            // a ogni risposta e' posto tolto a cio' che conta.
            int errorsBefore = FeaturesInError(before);
            // The same document as `before` (Z-C21.14), not the window in front.
            int errorsAfter = FeaturesInError(planStateAfter);
            if (errorsBefore > 0 || errorsAfter > 0)
            {
                result["features_in_error_before"] = errorsBefore;
                result["note_errors"] =
                    errorsAfter > errorsBefore
                        ? "Il pezzo ha " + errorsAfter + " lavorazioni in errore, e prima " +
                          "ne aveva " + errorsBefore + ": questo piano ne ha rotte " +
                          (errorsAfter - errorsBefore) + ". Guarda «state_after» per sapere quali."
                        : errorsAfter < errorsBefore
                            ? "Le lavorazioni in errore sono passate da " + errorsBefore +
                              " a " + errorsAfter + ": questo piano ne ha riparate " +
                              (errorsBefore - errorsAfter) + "."
                            : "Il pezzo aveva gia' " + errorsBefore + " lavorazioni in errore " +
                              "prima di questo piano, e ne ha ancora " + errorsAfter + ": " +
                              "non le ha rotte questo piano.";
            }

            // ── IL RICORDO PER «undo» (voce R1) ───────────────────────
            //
            // Si tiene com'era il pezzo quando questo piano ha finito: e'
            // l'unica cosa che permettera' di ritirarlo senza rischiare di
            // togliere il lavoro di qualcun altro.
            //
            // SI SCRIVE ANCHE QUANDO IL PIANO E' FALLITO A META', ed e' il caso
            // che conta di piu': un piano fermato al passo 3 lascia i primi due
            // costruiti, e sono proprio quelli che chi legge vuole ritirare.
            // Il messaggio di errore glielo dice gia' («quello che era gia'
            // stato costruito resta»).
            //
            // WHAT IS KEPT (Z-C21.14, review F1): the plan's document by its
            // COM reference (review F11: a save changes the name, not the
            // object), and a snapshot of it before and after the plan: volume,
            // feature count, and the feature names with their IDs in tree order.
            // The undo presses only if the part still reads as the snapshot
            // after, and says «withdrawn» only if it then reads as the snapshot
            // before.
            //
            // WHICH PLAN IS KEPT, AND WHEN (`MemoryAfterPlan`, pure, with the
            // rules written there). The short form:
            //  * a plan that built, or a plan whose own undo entry closed
            //    with something in it (FinishRecordingUndoObject answered true:
            //    a reorder, a rename, a property) becomes the memory: it is the
            //    last plan, and its entry is on top. Up to the first version of
            //    this lot such a plan only covered the plan before it, and
            //    the undo refused to withdraw the very plan the description
            //    promises to withdraw (review F1);
            //  * a plan that wrote nothing (it never reached the executor,
            //    stopped before opening its recording, or closed an empty one
            //    on the same document with nothing visibly changed) leaves the
            //    memory as it is (review F3: a plan, a measure and an undo, or
            //    a plan, a drawing with its views, the part brought back and
            //    an undo, were false refusals). The probe measured that an empty
            //    recording answers false; whether it leaves an entry on the
            //    stack is undecided, and the bench case
            //    `undo case c` decides it. If that entry were on top, a press
            //    would take away nothing visible, `undo` would see the part
            //    unchanged and say so, without a redo;
            //  * a measure that recorded a non-empty entry covers the memory
            //    (it is not a plan to withdraw, and its entry sits above);
            //    so does another bridge process refused the turn on the
            //    memory's document (review F4: another writer is on it);
            //  * anything else (an executor that threw, an answer not known, an
            //    entry on another document) drops the memory, and the reason is
            //    kept for the refusal, with what to do (review F12).
            //
            // Di solito, in questo progetto, **rifiutare cio' che e' corretto e'
            // il peggiore dei due modi di sbagliare**. Qui no, e vale la pena
            // dire perche': un rifiuto di troppo costa a chi legge una
            // rilettura e un piano nuovo; un annullamento di troppo cancella il
            // lavoro di una persona, e nessun messaggio lo rimette. Quando i due
            // errori non costano uguale, il criterio si rovescia.
            // On the plan's document (Z-C21.14): `before` was read on it before
            // the plan, `planStateAfter` after, whatever window is in front now.
            double? volBefore = NumberOf(before, "volume_mm3");
            double? volAfter = NumberOf(planStateAfter, "volume_mm3");
            int featBefore = HowManyFeatures(before);
            int featAfter = HowManyFeatures(planStateAfter);
            var snapshotBefore = PartSnapshot.Of(before);
            var snapshotAfter = PartSnapshot.Of(planStateAfter);

            // Un volume che non si e' letto non conta come «changed» (voce
            // Z-C8): qui si sbaglia nel verso di NON ricordare, per la ragione
            // scritta sopra — un annullamento di troppo cancella il lavoro di
            // una persona.
            bool hasBuilt =
                planStateAfter != null &&
                (featAfter != featBefore ||
                 (volBefore.HasValue && volAfter.HasValue &&
                  Math.Abs(volAfter.Value - volBefore.Value) > VolumeDeviationMm3));
            bool visibleChange = SnapshotDiffers(snapshotBefore, snapshotAfter).Count > 0;

            var memoryBefore = _lastPlan;
            bool sameDocument = memoryBefore != null &&
                                SameDocumentAs(memoryBefore, planDocumentHeld, planIdentity);
            bool turnRefusedOnMemoryDocument = memoryBefore != null && turnRefusedOn != null &&
                                               SameDocumentAs(memoryBefore, turnRefusedOn,
                                                              _session.Identity(turnRefusedOn));
            string memoryReason;
            string memoryAction = MemoryAfterPlan(
                hasBuilt, visibleChange, planStateAfter != null,
                memoryBefore != null, memoryBefore != null && memoryBefore.CoveredBecause != null,
                planRun != null, planRun != null && planRun.StoppedBeforeWriting, executorThrew,
                turnRefusedOnMemoryDocument, planRun?.UndoEntryOnPlanDocument, sameDocument,
                !checkIfRepeated, ReadableName(planIdentity), out memoryReason);

            switch (memoryAction)
            {
                case MemoryNew:
                    _lastPlan = new LastPlan
                    {
                        // The plan document as it is called NOW (Z-C21.14): a
                        // plan that saves its part changes its title and path.
                        Document = planIdentity,
                        DocumentRef = planDocumentHeld,
                        Before = snapshotBefore,
                        After = snapshotAfter,
                    };
                    _noMemoryBecause = null;
                    break;
                case MemoryCover:
                    memoryBefore.CoveredBecause = memoryReason;
                    memoryBefore.CoveredByRecordedEntry = true;
                    break;
                case MemoryDrop:
                    _lastPlan = null;
                    _noMemoryBecause = memoryReason;
                    break;
                default:
                    // Kept: the name follows a save of the same document.
                    if (memoryBefore != null && sameDocument) memoryBefore.Document = planIdentity;
                    break;
            }

            // SI RICORDA COSA SI E' FATTO, e solo se qualcosa si e' fatto
            // davvero (voce W2.7). Un piano rifiutato in blocco non ha toccato
            // niente: ricordarlo farebbe rifiutare il secondo tentativo di chi
            // ha appena corretto l'errore — cioe' la difesa scatterebbe contro
            // il caso che deve lasciar passare.
            // E non si ricorda niente di una MISURA: non ha cambiato il pezzo,
            // e ricordarla farebbe rifiutare un `build` successivo che per
            // caso avesse la stessa impronta.
            // The plan's document and its own summary (review F6): up to bridge
            // 0.125.0 the document was the plan's and the summary the window
            // in front's, so after [..., create_drawing] a resent plan on the part
            // was not recognised and built a second time.
            if (everythingFine && checkIfRepeated)
            {
                _lastPlanDone = planFingerprint;
                _planDocument = planIdentity;
                _summaryAfterPlan = StateSummary(planStateAfter);
            }

            // I dati escono anche quando il piano e' andato male (W2.4): e'
            // proprio allora che un programma ha piu' bisogno di leggerli senza
            // ri-analizzare una stringa — quale passo, quale misura, quale
            // rifiuto.
            return Protocol.ContentWithData(
                Protocol.InJson(result), result, !everythingFine);
        }

        // ─────────────────────────────────────────────────────────────────
        // R1 — ritirare quello che si e' fatto
        // ─────────────────────────────────────────────────────────────────
        /// <summary>
        /// Com'era il pezzo quando l'ultimo piano ha finito.
        ///
        /// E' l'unica cosa che permette di annullare senza rischiare di
        /// togliere il lavoro di qualcun altro: se il pezzo non e' piu' cosi',
        /// fra il piano e adesso e' successo qualcosa, e quel qualcosa non
        /// l'ha fatto questo ponte.
        /// </summary>
        private sealed class LastPlan
        {
            /// <summary>The plan document's identity, for the messages (and the
            /// comparison when no reference is held).</summary>
            public Dictionary<string, object> Document;

            /// <summary>
            /// Z-C21.14, review F11: the plan document by its COM reference
            /// (`PlanExecutor.Result.PlanDocument`). A save changes the
            /// name of the same document; a document closed and opened again
            /// has the same name and an undo stack without the plan. Null when
            /// the executor held none.
            /// </summary>
            public object DocumentRef;

            /// <summary>Z-C21.14, review F1: the plan's document before the plan.</summary>
            public PartSnapshot Before;

            /// <summary>Z-C21.14, review F1: the plan's document after the plan.</summary>
            public PartSnapshot After;

            /// <summary>
            /// Z-C21.14: why this plan is KNOWN not to be on top of the undo
            /// stack any more, in English, for the refusal; null while nothing
            /// says so. A covered memory is never pressed.
            /// </summary>
            public string CoveredBecause;

            /// <summary>
            /// Z-C21.14, review F10: what covers it. True: an undo entry
            /// recorded through the API (a measure that recorded one, another
            /// bridge's plan), which SOLIDWORKS cannot redo. False: an action
            /// that was not recorded, put back by a redo, which a redo can put
            /// back again.
            /// </summary>
            public bool CoveredByRecordedEntry;
        }

        /// <summary>
        /// WHAT THE UNDO COMPARES OF A READING (Z-C21.14): the volume (null
        /// when the state could not read it, Z-C8), the feature count with the
        /// features beyond the list, the listed features as «name#id» in tree
        /// order, and how many features are beyond the list (review F16: those
        /// are compared only by count).
        /// </summary>
        private sealed class PartSnapshot
        {
            public double? Volume;
            public int Count;
            public List<string> Names;
            public int Unlisted;

            public static PartSnapshot Of(Dictionary<string, object> docState)
            {
                if (docState == null) return null;
                int unlisted = 0;
                if (docState.TryGetValue("feature_not_listed", out var u) && u != null)
                    try { unlisted = Convert.ToInt32(u, CultureInfo.InvariantCulture); } catch { }
                return new PartSnapshot
                {
                    Volume = NumberOf(docState, "volume_mm3"),
                    Count = HowManyFeatures(docState),
                    Names = FeatureNamesInOrder(docState),
                    Unlisted = unlisted,
                };
            }
        }

        private LastPlan _lastPlan;

        /// <summary>
        /// Z-C21.14: why there is no memory, when a later plan or an
        /// undo dropped it, in English, for the refusal; null otherwise.
        /// </summary>
        private string _noMemoryBecause;

        /// <summary>The four answers of <see cref="MemoryAfterPlan"/>.</summary>
        internal const string MemoryNew = "new", MemoryKeep = "keep", MemoryCover = "cover", MemoryDrop = "drop";

        /// <summary>
        /// WHAT A PLAN DOES TO THE UNDO MEMORY (Z-C21.14, reviewed).
        /// Pure, so that a contract can call it. Returns
        /// <see cref="MemoryNew"/> (this plan becomes the memory),
        /// <see cref="MemoryKeep"/> (the memory, or its absence and the reason
        /// for it, stays as it is), <see cref="MemoryCover"/> (the memory is
        /// kept and marked covered by a recorded entry, with
        /// <paramref name="reason"/>) or <see cref="MemoryDrop"/> (the memory
        /// goes, if there was one, and <paramref name="reason"/> says why).
        ///
        /// The rules, in this order:
        ///  0. another bridge process held the turn on the memory's document
        ///     (review F4): cover;
        ///  1. the plan never reached the executor, or stopped before opening
        ///     its recording: nothing was written, keep;
        ///  2. the executor threw (review F9): what it wrote and recorded is
        ///     not known, drop;
        ///  3. the plan's document could not be read after it: drop;
        ///  4. the plan built (volume or feature count changed): new;
        ///  5. a plan whose own undo entry closed with something in it
        ///     (FinishRecordingUndoObject answered true: a reorder, a rename, a
        ///     property): new, it is the last plan and its entry is on top
        ///     (review F1);
        ///  6. no memory: keep when the plan wrote nothing (answer false and
        ///     nothing visibly changed), otherwise drop with the reason (review
        ///     F12: the reason is rewritten after every plan that wrote);
        ///  7. a covered memory stays covered;
        ///  8. a plan on another document: drop;
        ///  9. a measure that recorded a non-empty entry: cover;
        /// 10. answer false and nothing visibly changed: keep (review F3; the
        ///     bench case `undo case c` decides that an empty recording does not
        ///     stand between the memory and the press);
        /// 11. anything else (answer unknown, or false with a visible change):
        ///     drop.
        /// </summary>
        internal static string MemoryAfterPlan(
            bool built, bool visibleChange, bool afterReadable,
            bool hasMemory, bool alreadyCovered,
            bool executorReached, bool stoppedBeforeWriting, bool executorThrew,
            bool turnRefusedOnMemoryDocument, bool? undoEntryOnPlanDocument, bool sameDocument,
            bool measure, string planDocumentName, out string reason)
        {
            reason = null;
            string what = measure ? "The last «measure»" : "The last «build»";
            string on = " «" + (planDocumentName ?? "") + "»";

            if (turnRefusedOnMemoryDocument && hasMemory && !alreadyCovered)
            {
                reason = "Another bridge process was working on the document of the plan I remember when my last " +
                         "plan was refused its turn there: whatever it recorded is now on top of the undo stack, " +
                         "above my plan.";
                return MemoryCover;
            }
            if (!executorReached || stoppedBeforeWriting) return MemoryKeep;
            if (executorThrew)
            {
                reason = what + " stopped with an exception in the plan executor, so what it wrote on" + on +
                         " and what it left on top of its undo stack are not known.";
                return MemoryDrop;
            }
            if (!afterReadable)
            {
                reason = what + " ran, and then I could not read" + on + " again: what is on top of its undo stack " +
                         "is not known.";
                return MemoryDrop;
            }
            if (built) return MemoryNew;
            if (!measure && undoEntryOnPlanDocument == true) return MemoryNew;

            bool wroteNothing = undoEntryOnPlanDocument == false && !visibleChange;
            if (!hasMemory)
            {
                if (wroteNothing) return MemoryKeep;
                reason = undoEntryOnPlanDocument == true
                    ? what + " put its own undo entry on top of the undo stack of" + on +
                      " (FinishRecordingUndoObject answered true), and a measure is not a plan to withdraw."
                    : UnknownTop(what, on, undoEntryOnPlanDocument);
                return MemoryDrop;
            }
            if (alreadyCovered) return MemoryKeep;
            if (!sameDocument)
            {
                reason = what + " ran on another document," + on + ", so I no longer keep the memory of the plan " +
                         "before it: I do not press undo on a stack I cannot vouch for.";
                return MemoryDrop;
            }
            if (undoEntryOnPlanDocument == true)
            {
                reason = what + " put its own undo entry («Ponte MCP»: FinishRecordingUndoObject answered true) on " +
                         "top of the undo stack of" + on + ", above the plan I remember.";
                return MemoryCover;
            }
            if (wroteNothing) return MemoryKeep;
            reason = UnknownTop(what, on, undoEntryOnPlanDocument);
            return MemoryDrop;
        }

        private static string UnknownTop(string what, string on, bool? undoEntry)
        {
            return undoEntry == false
                ? what + " changed something I can see on" + on + " (the feature names or their order) while " +
                  "SOLIDWORKS answered that its undo entry closed with nothing in it: I cannot tell what is on top " +
                  "of the undo stack."
                : what + " ran on" + on + ", and I do not know whether it left an undo entry on top of its undo stack.";
        }

        /// <summary>
        /// Is <paramref name="document"/> (identity <paramref name="identity"/>)
        /// the document of <paramref name="memory"/>? By the COM reference when
        /// both are held (review F11), by name otherwise.
        /// </summary>
        private static bool SameDocumentAs(LastPlan memory, object document, Dictionary<string, object> identity)
        {
            if (memory == null) return false;
            if (memory.DocumentRef != null && document != null)
                return Bridge.Session.SameComObject(memory.DocumentRef, document);
            return Bridge.Session.SameDocument(memory.Document, identity);
        }

        /// <summary>
        /// An identity in the form the plan executor compares (review F7): the
        /// path, or the title with its prefix when the document has never been
        /// saved; null when neither is known.
        /// </summary>
        internal static string IdentityForTheExecutor(Dictionary<string, object> identity)
        {
            if (identity == null) return null;
            string path = Convert.ToString(identity.TryGetValue("path", out var p) ? p : null) ?? "";
            if (path.Trim().Length > 0) return path;
            string title = Convert.ToString(identity.TryGetValue("document_title", out var t) ? t : null) ?? "";
            return title.Trim().Length > 0 ? "titolo:" + title : null;
        }

        /// <summary>
        /// The note of `build` when another document is in front (Z-C21.14,
        /// review F5). Pure, so that a contract can call it.
        /// </summary>
        internal static string NoteDocumentInFront(string planDocument, string front, bool frontIsThePlans)
        {
            const string bringBack = "(«document» with «action»: «activate»)";
            string head = "The plan worked on «" + planDocument + "», and in front now is «" + front + "»";
            if (frontIsThePlans)
                return head + ", the document the plan ended on. «state_after» describes «" + front + "», the document " +
                       "the next plan works on; «volume_after_mm3» is of «" + planDocument + "». To withdraw this " +
                       "plan with an undo, bring «" + planDocument + "» to the front first " + bringBack + ".";
            return head + ", a window the plan did not end on: it came to the front from outside while the plan ran. " +
                   "«state_after» describes «" + front + "» for information only: I keep neither its identity nor its " +
                   "signatures, so the next plan asks for «inspect_part» first. To go on with «" + planDocument +
                   "», bring it back " + bringBack + " and call «inspect_part»; to work on «" +
                   front + "», call «inspect_part» now.";
        }

        /// <summary>
        /// What to do when the undo has no plan to withdraw (review F12): the
        /// reader is a model, so the way that does not need a keyboard comes
        /// first.
        /// </summary>
        internal const string NoMemoryRemedy =
            "If something must go, take it away with a new plan («delete_feature» or «suppress_feature» " +
            "on the feature, by the name «inspect_part» gives it); the person at the SOLIDWORKS screen can also " +
            "undo by hand (Edit > Undo), seeing what each step takes away.";

        /// <summary>
        /// Quanto si puo' sbagliare un volume e considerarlo lo stesso.
        ///
        /// Un millesimo di mm³: SOLIDWORKS ricalcola il volume a ogni rebuild e
        /// l'ultima cifra balla. Piu' stretto di cosi' rifiuterebbe pezzi
        /// identici; piu' largo lascerebbe passare una modifica vera — un foro
        /// Ø1 profondo 1 sono 0,79 mm³, quasi mille volte questa soglia.
        /// </summary>
        private const double VolumeDeviationMm3 = 0.001;

        /// <summary>
        /// RITIRA L'ULTIMO PIANO.
        ///
        /// PERCHE' ESISTE. Fino alla 0.31.0 questo ponte diceva **ventuno
        /// volte** al modello «annullalo con Ctrl+Z». Il destinatario di quei
        /// messaggi e' un modello linguistico, che non ha una tastiera: era una
        /// riparazione offerta a qualcuno che non poteva prenderla. E le
        /// trentasei operazioni sono tutte additive o parametriche, quindi
        /// l'albero poteva solo crescere: un modello che si accorgeva di aver
        /// sbagliato aveva due strade, tutte e due sbagliate — costruirci sopra,
        /// o rifare il pezzo da capo.
        ///
        /// LA MACCHINA C'ERA GIA'. `PlanExecutor` apre e chiude una voce di
        /// annullamento intorno a ogni piano (voce B8, misurata: «un solo
        /// Ctrl+Z riporta il documento al valore di volume_before_mm3»). Si
        /// azionava solo con una tastiera umana. E' la stessa forma della
        /// sezione V — la capacita' c'e', e non arriva a chi legge.
        ///
        /// ── IL RIFIUTO VIENE PRIMA ────────────────────────────────────────
        ///
        /// Fra il piano e questa chiamata il progettista puo' aver lavorato a
        /// mano. Premere annulla allora gli toglie il SUO lavoro, ed e' un
        /// danno che nessun messaggio ripara. Quindi si confrontano DUE
        /// numeri che la risposta di `build` gia' conteneva — il volume e
        /// quante lavorazioni ci sono — e se non tornano **ci si ferma e si
        /// dice cosa e' cambiato**.
        ///
        /// Since Z-C21.14 (review F1) the feature names, their IDs and their
        /// order are compared too: a reorder or a rename done after the plan by
        /// another session, another bridge process or the person is seen before
        /// pressing. What changes none of these (a property, a configuration)
        /// still goes unseen.
        ///
        /// ── A WRONG PRESS CANNOT BE TAKEN BACK (Z-C21.14) ─────────────────
        ///
        /// Up to bridge 0.124.1 this summary said that the way out was to try,
        /// check, and undo the wrong try with a redo. Measured false on
        /// 1/10/2026 (probe `redo`; RAG note 82zz_duodecies, section «La sonda
        /// viva di M19»): SOLIDWORKS never redoes an undo entry recorded
        /// through the API, and every plan of this bridge is one; only an
        /// action that was not recorded comes back. And the answer said «L'ho
        /// rimesso a posto subito» whenever EditRedo2 did not throw, without
        /// looking. So now:
        ///  * when the memory is covered (something is KNOWN to be on top of
        ///    the plan's entry), or the part reads differently from how the
        ///    plan left it, it refuses BEFORE pressing, says why and what to do;
        ///  * after pressing it says «withdrawn» only if the part reads as it
        ///    was before the plan (volume, feature count, feature names and
        ///    order); otherwise it compares the part with how it was before
        ///    pressing, tries one redo only when the press took away something
        ///    visible, and says «restored» only if the part then reads as before
        ///    pressing, otherwise what is still different. With a volume it
        ///    cannot read it does not redo at all (Z-C8).
        /// There is still no API to look at the undo stack before pressing.
        /// </summary>
        private Dictionary<string, object> Undo()
        {
            if (!_session.Attached)
                return Protocol.Content("Il ponte non e' agganciato a SolidWorks.", true);

            var memory = _lastPlan;
            if (memory == null)
                return Protocol.Content(
                    "Non ho nessun piano da ritirare: «undo» disfa l'ultimo " +
                    "«build» di questa conversazione, e qui non ne ho ancora " +
                    "eseguito nessuno che abbia costruito qualcosa.\n\n" +
                    "Non premo annulla alla cieca: toglierei qualcosa che non ho " +
                    "fatto io, e non saprei nemmeno dire cosa." +
                    (_noMemoryBecause != null ? "\n\n" + _noMemoryBecause : "") +
                    "\n\n" + NoMemoryRemedy, true);

            // ── SOMETHING IS KNOWN TO BE ON TOP: REFUSE BEFORE PRESSING ──
            // (Z-C21.14). No reading, no press: the memory says the remembered
            // plan is not on top of the undo stack, and a press would take away
            // what is.
            if (memory.CoveredBecause != null)
                return RefusalCovered(memory);

            var stateBefore = _session.StateWithoutAnchor(out var readError);
            if (stateBefore == null) return Protocol.Content(readError, true);

            var identityNow = StateIdentity(stateBefore);
            // THE SAME OPEN DOCUMENT, by its COM reference when it is held
            // (review F11): a save renames it, and a document closed and opened
            // again keeps its name and loses the undo stack with the plan in it.
            bool samePart = memory.DocumentRef != null
                ? _session.IsInFront(memory.DocumentRef)
                : Bridge.Session.SameDocument(memory.Document, identityNow);
            if (!samePart)
                return Protocol.Content(
                    "Mi fermo: il documento e' cambiato. Il piano l'avevo eseguito " +
                    "su «" + ReadableName(memory.Document) + "», adesso davanti " +
                    "c'e' «" + ReadableName(identityNow) + "». Annullare qui " +
                    "toglierebbe l'ultima cosa fatta su un pezzo che non c'entra.\n\n" +
                    (Bridge.Session.SameDocument(memory.Document, identityNow)
                        ? "It has the same name, but it is not the same open document (it was closed and opened " +
                          "again, or it is another copy): its undo stack does not hold my plan.\n\n"
                        : "") +
                    "Porta davanti il pezzo giusto e richiamami.", true);

            var now = PartSnapshot.Of(stateBefore);
            var after = memory.After;
            if (now.Volume == null || after == null || after.Volume == null)
                return Protocol.Content(
                    "Mi fermo: non riesco a misurare il volume del pezzo, e senza non so " +
                    "dirti se dopo il piano l'ha cambiato qualcun altro. Un passo indietro " +
                    "alla cieca potrebbe disfare il lavoro di chi sta davanti allo schermo: " +
                    "se vuoi comunque tornare indietro, e' un gesto da fare a mano.", true);

            if (Math.Abs(now.Volume.Value - after.Volume.Value) > VolumeDeviationMm3 ||
                now.Count != after.Count)
                return Protocol.Content(
                    "Mi fermo: dopo il piano il pezzo e' cambiato, e non l'ho " +
                    "cambiato io.\n\n" +
                    Comparison("volume", after.Volume.Value, now.Volume.Value, "mm³") +
                    Comparison("features", after.Count, now.Count, "") +
                    "\nUn passo indietro adesso non disferebbe il mio piano: " +
                    "disferebbe l'ultima cosa fatta, che e' di qualcun altro. " +
                    "Se vuoi comunque tornare indietro, e' un gesto che deve fare " +
                    "chi sta davanti allo schermo, perche' solo lui sa cosa sta " +
                    "buttando via.", true);

            // THE NAMES AND THE ORDER TOO (review F1, F14): a reorder or a
            // rename done after the plan leaves volume and count as they were,
            // and a press would take it away for good.
            var changedSince = SnapshotDiffers(after, now);
            if (changedSince.Count > 0)
                return RefusalChangedSinceThePlan(memory, changedSince, NamesComparedOnlyInPart(after, now));

            // ── SI PREME ─────────────────────────────────────────────────
            var undoError = _session.StepBack();
            if (undoError != null)
                return Protocol.Content(undoError, true);

            var stateAfter = _session.State(out var stateError);
            if (stateAfter == null)
                return Protocol.Content(
                    "Ho premuto annulla e poi non sono riuscito a rileggere il " +
                    "documento: " + stateError + "\n\n" +
                    "Non posso dirti se ha funzionato. Guarda il pezzo prima di " +
                    "fare altro.", true);

            var back = PartSnapshot.Of(stateAfter);
            var before = memory.Before;
            // SE IL VOLUME NON SI LEGGE, NON SI RIFA' ALLA CIECA (voce Z-C8):
            // «non e' tornato» porterebbe a un passo avanti che potrebbe
            // rimettere proprio il piano appena tolto.
            if (back.Volume == null || before == null || before.Volume == null)
            {
                _lastPlan = null;
                _noMemoryBecause =
                    "The last «undo» pressed undo and then could not read the volume: what is on " +
                    "top of the undo stack now is not known.";
                return Protocol.Content(
                    "Ho premuto annulla, ma poi non riesco a misurare il volume del pezzo: " +
                    "non posso dirti se e' tornato com'era. Non ho fatto altro. Guarda il " +
                    "pezzo prima di andare avanti.", true);
            }
            // BACK AS IT WAS BEFORE THE PLAN: volume, count, names and order
            // (review F1), not only the two numbers.
            var notAsBeforeThePlan = SnapshotDiffers(before, back);
            bool namesPartial = NamesComparedOnlyInPart(before, back);

            if (notAsBeforeThePlan.Count > 0)
            {
                // IT TOOK AWAY SOMETHING ELSE (Z-C21.14). The part read as the
                // plan left it, so what was on top changed nothing that is
                // compared: a property, a configuration.
                //
                // Up to bridge 0.124.1 a redo followed here, blind, and the
                // answer said «L'ho rimesso a posto subito» whenever EditRedo2
                // did not throw. Measured on 1/10/2026 (probe `redo`): an undo
                // entry recorded through the API is never redone, so that
                // answer lied for every plan of this bridge. Now the part is
                // compared with how it was before pressing (`before`): only when
                // the press took away something visible is ONE redo tried (it
                // can bring back an action that was not recorded, measured),
                // and then the part is read again and compared once more. The
                // answer says «restored» only from that comparison.
                var takenAway = WhatDiffers(stateBefore, stateAfter);
                bool redoTried = false;
                string redoError = null;
                var readAgain = stateAfter;
                if (takenAway.Count > 0)
                {
                    redoTried = true;
                    redoError = _session.StepForward();
                    readAgain = _session.State(out _);
                }
                var stillDifferent = readAgain == null
                    ? new List<string> { "the part could not be read again" }
                    : WhatDiffers(stateBefore, readAgain);

                bool restored;
                var answerText = AnswerAfterWrongPress(
                    before.Volume.Value, before.Count, back.Volume.Value, back.Count, notAsBeforeThePlan,
                    takenAway, redoTried, redoError, stillDifferent, namesPartial, out restored);

                // THE MEMORY: restored, the foreign action is on top again and
                // KNOWN to be (covered by an action that was not recorded: the
                // next undo refuses before pressing); otherwise what is on
                // top is not known any more, and the memory goes.
                if (restored)
                {
                    memory.CoveredBecause =
                        "An action done after the plan, which changes neither the volume, nor the feature count, " +
                        "nor the feature names and order, is on top of the undo stack: the last undo took it " +
                        "away and the redo put it back.";
                    memory.CoveredByRecordedEntry = false;
                }
                else
                {
                    _lastPlan = null;
                    _noMemoryBecause =
                        "The last «undo» took away something that was not the plan, and it did not come " +
                        "back: what is on top of the undo stack now is not known.";
                }

                if (readAgain != null)
                {
                    _seen = StateIdentity(readAgain);
                    _seenSignatures = StateSignatures(readAgain);
                    _signatureSeen = LightSignature(readAgain);
                    RemoveAnchors(readAgain);
                }
                else
                {
                    _seen = null;
                    _seenSignatures = null;
                    _signatureSeen = null;
                }

                return Protocol.Content(Protocol.InJson(
                    new Dictionary<string, object>
                    {
                        ["undone"] = false,
                        ["document"] = ReadableName(identityNow),
                        ["reason"] = answerText,
                        ["not_as_before_the_plan"] = notAsBeforeThePlan,
                        ["what_the_undo_took_away"] = takenAway,
                        ["redo_tried"] = redoTried,
                        ["restored"] = restored,
                        ["still_different"] = stillDifferent,
                        ["state_after"] = readAgain,
                    }), true);
            }

            // RIUSCITO: IL RICORDO SI CANCELLA.
            // Un secondo «undo» non deve disfare quello che c'era PRIMA del
            // piano: quello non l'ha fatto questo ponte, e la pila degli
            // annullamenti non sa distinguere.
            _lastPlan = null;
            _noMemoryBecause =
                "The last «undo» already withdrew the plan; one more step back would take away what " +
                "was there before it, which this bridge did not make.";

            // GLI IDENTIFICATIVI NON SONO PIU' QUELLI, e chi legge deve
            // saperlo dalla risposta invece di scoprirlo al piano seguente:
            // gli uid sono posizionali e si rinumerano per area a ogni
            // lettura. Si firma questa lettura, esattamente come fa `build`.
            _seen = StateIdentity(stateAfter);
            _seenSignatures = StateSignatures(stateAfter);
            _signatureSeen = LightSignature(stateAfter);
            RemoveAnchors(stateAfter);

            // A PLAN THAT CHANGED NOTHING THAT IS COMPARED (a property, a
            // sketch that is not a counted feature) cannot be seen to go by
            // reading: «withdrawn» is then what SOLIDWORKS did with the entry
            // on top, and the answer says it was not checked (Z-C21.14).
            bool checkedByReading = SnapshotDiffers(before, after).Count > 0;
            var outside = new Dictionary<string, object>
            {
                ["undone"] = true,
                ["document"] = ReadableName(identityNow),
                ["volume_before_mm3"] = Math.Round(now.Volume.Value, 2),
                ["volume_after_mm3"] = Math.Round(back.Volume.Value, 2),
                ["features_before"] = now.Count,
                ["features_after"] = back.Count,
                ["checked_by_reading"] = checkedByReading,
                ["note"] =
                    "Il piano e' stato ritirato tutto intero e il pezzo e' " +
                    "tornato com'era. Gli identificativi di «state_after» sono " +
                    "quelli validi adesso: quelli di prima non valgono piu'.",
                ["state_after"] = stateAfter,
            };
            if (!checkedByReading)
                outside["note_checked"] =
                    "This plan changed nothing I compare (volume, feature count, feature names and order), so reading " +
                    "the part cannot show that it is gone: SOLIDWORKS took away the entry on top, and the last entry I " +
                    "know of on this document was this plan's (its «undo_entry_recorded» was true, and nothing I " +
                    "compare changed since).";
            else if (namesPartial)
                outside["note_checked"] =
                    "The part reads as before the plan by volume and feature count, and by " + NamesScope(true) + ".";
            return Protocol.Content(Protocol.InJson(outside));
        }

        /// <summary>
        /// THE REFUSAL BEFORE PRESSING (Z-C21.14): the remembered plan is known
        /// not to be on top of the undo stack. Nothing was read or touched.
        /// </summary>
        private Dictionary<string, object> RefusalCovered(LastPlan memory)
        {
            var outside = new Dictionary<string, object>
            {
                ["undone"] = false,
                ["refused_before_pressing"] = true,
                ["document"] = ReadableName(memory.Document),
                ["covered_by_recorded_entry"] = memory.CoveredByRecordedEntry,
                ["reason"] = RefusalCoveredText(memory.CoveredBecause, memory.CoveredByRecordedEntry),
            };
            return Protocol.ContentWithData(Protocol.InJson(outside), outside, true);
        }

        /// <summary>
        /// What to do instead of a press that would take away what is not the
        /// plan (Z-C21.14): the reader is a model, without a keyboard, so the
        /// way through a new plan comes first and the person at the screen
        /// second (review F1).
        /// </summary>
        internal const string WithdrawInsteadRemedy =
            "If the plan must go: take its features away with a new plan («delete_feature» or " +
            "«suppress_feature», by the names «inspect_part» gives), or ask the person at the SOLIDWORKS " +
            "screen, who can see what each step removes, to undo by hand (Edit > Undo).";

        /// <summary>
        /// The text of <see cref="RefusalCovered"/>, pure so that a contract can
        /// call it: why, what a press would do (which depends on what covers the
        /// plan, review F10), and what to do instead.
        /// </summary>
        internal static string RefusalCoveredText(string coveredBecause, bool recordedEntryOnTop)
        {
            string press = recordedEntryOnTop
                ? " One step back now would take that away, not my plan, and SOLIDWORKS cannot redo an undo entry " +
                  "recorded through the API (measured on 1/10/2026): it would be lost for good."
                : " One step back now would take that away again, not my plan. It is an action that was not recorded " +
                  "through the API, which a redo can put back (measured on 1/10/2026), but taking away what is not " +
                  "mine and putting it back does not withdraw my plan.";
            return "I am not pressing undo, and nothing was touched. " + coveredBecause + press + "\n\n" +
                   WithdrawInsteadRemedy;
        }

        /// <summary>
        /// THE REFUSAL BEFORE PRESSING when the part reads differently from how
        /// the plan left it in something other than volume and count: the
        /// feature names, their IDs or their order (Z-C21.14, review F1 and
        /// F14). Nothing was touched.
        /// </summary>
        private Dictionary<string, object> RefusalChangedSinceThePlan(
            LastPlan memory, List<string> changed, bool namesPartial)
        {
            var outside = new Dictionary<string, object>
            {
                ["undone"] = false,
                ["refused_before_pressing"] = true,
                ["document"] = ReadableName(memory.Document),
                ["what_changed_since_the_plan"] = changed,
                ["reason"] = RefusalChangedSinceThePlanText(changed, namesPartial),
            };
            return Protocol.ContentWithData(Protocol.InJson(outside), outside, true);
        }

        /// <summary>The text of <see cref="RefusalChangedSinceThePlan"/>, pure.</summary>
        internal static string RefusalChangedSinceThePlanText(List<string> changed, bool namesPartial)
        {
            return "I am not pressing undo, and nothing was touched. After my plan the part changed, and I did not " +
                   "change it: " + string.Join("; ", changed ?? new List<string>()) + "." +
                   (namesPartial ? " (I compared " + NamesScope(true) + ".)" : "") +
                   " One step back now would not withdraw my plan: it would take away the last thing done, which is " +
                   "someone else's (another session, another bridge process or the person at the screen), and " +
                   "SOLIDWORKS cannot redo an undo entry recorded through the API (measured on 1/10/2026).\n\n" +
                   WithdrawInsteadRemedy;
        }

        /// <summary>
        /// WHAT IS VISIBLY DIFFERENT between two readings of the same part
        /// (Z-C21.14): the volume, the feature count, and the feature names and
        /// their order (with the ID SOLIDWORKS gives each feature, which a
        /// rename does not change). Empty when nothing visible differs; a volume
        /// that cannot be read on either side counts as a difference, because
        /// sameness cannot be seen (Z-C8). The names are those the state lists:
        /// beyond <c>DocumentState.MaxFeature</c> the older features are
        /// compared only by count (review F16, <see cref="NamesComparedOnlyInPart"/>).
        /// Pure, so that a contract can call it.
        /// </summary>
        internal static List<string> WhatDiffers(
            Dictionary<string, object> before, Dictionary<string, object> after)
        {
            return SnapshotDiffers(PartSnapshot.Of(before), PartSnapshot.Of(after));
        }

        /// <summary><see cref="WhatDiffers"/> on two snapshots.</summary>
        private static List<string> SnapshotDiffers(PartSnapshot before, PartSnapshot after)
        {
            var differences = new List<string>();
            if (before == null || after == null)
            {
                differences.Add("one of the two readings is missing");
                return differences;
            }

            if (before.Volume == null || after.Volume == null)
                differences.Add("the volume cannot be read" +
                                (before.Volume == null ? " before" : "") +
                                (after.Volume == null ? " after" : ""));
            else
            {
                double was = before.Volume.GetValueOrDefault(), now = after.Volume.GetValueOrDefault();
                if (Math.Abs(was - now) > VolumeDeviationMm3)
                    differences.Add("volume " + Number(was) + " mm³ before, " + Number(now) + " mm³ now");
            }

            if (before.Count != after.Count)
                differences.Add("features " + before.Count + " before, " + after.Count + " now");

            var namesBefore = before.Names ?? new List<string>();
            var namesAfter = after.Names ?? new List<string>();
            var gone = namesBefore.Where(n => !namesAfter.Contains(n)).ToList();
            var come = namesAfter.Where(n => !namesBefore.Contains(n)).ToList();
            if (gone.Count > 0) differences.Add("features no longer there: «" + string.Join("», «", gone) + "»");
            if (come.Count > 0) differences.Add("features that were not there: «" + string.Join("», «", come) + "»");
            if (gone.Count == 0 && come.Count == 0 && !namesBefore.SequenceEqual(namesAfter))
                differences.Add("the order of the features changed");
            return differences;
        }

        /// <summary>
        /// True when either reading has features beyond the list of the state
        /// (`feature_not_listed`): the names and the order are then compared
        /// only on the listed ones (review F16).
        /// </summary>
        private static bool NamesComparedOnlyInPart(PartSnapshot a, PartSnapshot b)
        {
            return (a != null && a.Unlisted > 0) || (b != null && b.Unlisted > 0);
        }

        /// <summary>What «names and order» covers, for the answers (review F16).</summary>
        internal static string NamesScope(bool partial)
        {
            return partial
                ? "the names and order of the last " + Oce.Solid.Agent.Core.DocumentState.MaxFeature +
                  " features, the ones the state lists (the older ones only by count)"
                : "the feature names and order";
        }

        /// <summary>
        /// The features of a reading as «name#id», in tree order (Z-C21.14).
        /// </summary>
        private static List<string> FeatureNamesInOrder(Dictionary<string, object> docState)
        {
            var names = new List<string>();
            foreach (var v in EntriesOf(docState, "feature"))
            {
                var entry = v as Dictionary<string, object>;
                if (entry == null) continue;
                string name = Convert.ToString(entry.TryGetValue("uid", out var u) ? u : null) ?? "";
                string id = Convert.ToString(entry.TryGetValue("feature_id", out var i) ? i : null,
                                             CultureInfo.InvariantCulture) ?? "";
                names.Add(id.Length > 0 ? name + "#" + id : name);
            }
            return names;
        }

        /// <summary>
        /// THE ANSWER AFTER A PRESS THAT DID NOT TAKE THE PLAN AWAY (Z-C21.14).
        /// Pure, so that a contract can call it: <paramref name="restored"/> is
        /// true only when a redo was tried, did not throw, the press had taken
        /// away something visible and the part now reads as before pressing.
        /// Never «put back» without that comparison.
        /// </summary>
        internal static string AnswerAfterWrongPress(
            double expectedVolume, int expectedFeatures, double volumeNow, int featuresNow,
            List<string> notAsBeforeThePlan, List<string> takenAway, bool redoTried, string redoError,
            List<string> stillDifferent, bool namesPartial, out bool restored)
        {
            notAsBeforeThePlan = notAsBeforeThePlan ?? new List<string>();
            takenAway = takenAway ?? new List<string>();
            stillDifferent = stillDifferent ?? new List<string> { "the part could not be read again" };
            restored = redoTried && redoError == null && takenAway.Count > 0 && stillDifferent.Count == 0;
            string scope = NamesScope(namesPartial);

            string head =
                "I pressed undo and the part did not go back to how it was before the plan (the volume should be " +
                Number(expectedVolume) + " mm³ and the features " + expectedFeatures + "; they are " +
                Number(volumeNow) + " mm³ and " + featuresNow +
                (notAsBeforeThePlan.Count > 0 ? "; " + string.Join("; ", notAsBeforeThePlan) : "") +
                "). So the entry on top of the undo stack was not my plan but something done after it that changed " +
                "neither the volume, nor the feature count, nor " + scope + ", which I could not see before pressing.";

            if (!redoTried && takenAway.Count > 0)
                return head + " It took away: " + string.Join("; ", takenAway) + ". I did not press redo, " +
                       "so it is NOT back. Look at the part in SOLIDWORKS before going on.";
            if (!redoTried)
                return head + " Reading the part again, the undo changed nothing I can see (volume, feature " +
                       "count and " + scope + " are as before pressing), so I did not press redo: I could not check " +
                       "what it would bring back. Look at the part in SOLIDWORKS before going on; Edit > Redo puts " +
                       "back only an action that was not recorded through the API.";

            if (restored)
                return head + " It took away: " + string.Join("; ", takenAway) + ". I pressed redo and read " +
                       "the part again: volume, feature count and " + scope + " are as before pressing, so it came " +
                       "back (only an action not recorded through the API can come back this way). The plan is " +
                       "still there.";

            return head + " It took away: " + string.Join("; ", takenAway) + ". I pressed redo" +
                   (redoError != null ? " and SOLIDWORKS refused: " + redoError : "") +
                   ", and the part is NOT as it was before pressing: " + string.Join("; ", stillDifferent) +
                   ". SOLIDWORKS cannot redo an undo entry recorded through the API (measured on " +
                   "1/10/2026), so what the undo took away is lost. Look at the part in SOLIDWORKS before " +
                   "going on, and make it again with «build» if you need it.";
        }

        /// <summary>Una riga di confronto, solo se i due numeri sono diversi.</summary>
        private static string Comparison(string what, double expected, double found, string units)
        {
            if (Math.Abs(expected - found) <= VolumeDeviationMm3) return "";
            return "  " + what + ": era " + Number(expected) +
                   (units.Length > 0 ? " " + units : "") + " quando ho finito, " +
                   "adesso e' " + Number(found) +
                   (units.Length > 0 ? " " + units : "") + "\n";
        }

        private static string Number(double v)
        {
            return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Un numero dello stato, o **null se lo stato dice di non saperlo**
        /// (voce Z-C8): la chiave c'e' e vale null, cioe' la lettura e' fallita.
        /// ZERO-E-ZERO: una chiave ASSENTE vale zero — lo stato non scrive il
        /// volume di una parte senza corpi solidi, ne' di una tavola o di un
        /// assieme, e li' zero e' vero. Fino al 25/9/2026 assente voleva dire
        /// anche «non letto», e un annullamento giusto poteva essere rifatto.
        /// </summary>
        private static double? NumberOf(Dictionary<string, object> docState, string key)
        {
            if (docState == null) return null;
            object v;
            if (!docState.TryGetValue(key, out v)) return 0;
            if (v == null) return null;
            try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        /// <summary>
        /// Quante lavorazioni ha il pezzo, comprese quelle che il tetto ha
        /// lasciato fuori.
        ///
        /// SENZA `feature_not_listed` QUESTO CONTEGGIO MENTIREBBE su ogni
        /// pezzo con piu' di ottanta lavorazioni: si fermerebbe a ottanta e
        /// direbbe «non e' cambiato niente» anche dopo dieci fori nuovi. E'
        /// esattamente il difetto della voce E12, ripetuto da questa parte.
        /// </summary>
        private static int HowManyFeatures(Dictionary<string, object> docState)
        {
            if (docState == null) return 0;
            int howMany = 0;
            foreach (var v in EntriesOf(docState, "feature")) { if (v != null) howMany++; }
            object outside;
            if (docState.TryGetValue("feature_not_listed", out outside) && outside != null)
                try { howMany += Convert.ToInt32(outside); } catch { }
            return howMany;
        }

        // ─────────────────────────────────────────────────────────────────

        private static object Volume(Dictionary<string, object> docState)
        {
            if (docState == null) return null;
            return docState.TryGetValue("volume_mm3", out var v) ? v : null;
        }

        /// <summary>
        /// GLI IDENTIFICATIVI CHE IL PASSO CITA E CHE NELLO STATO NON C'ERANO.
        /// Null se e' tutto a posto.
        ///
        /// Si guardano solo i campi che lo schema chiama `*_uid`: sono quelli
        /// che nominano geometria **che c'era gia'**. La geometria creata dal
        /// piano stesso si nomina con `*_rif`, che cita il PASSO e non
        /// l'identificativo — ed e' l'unico modo, perche' al momento in cui il
        /// piano si scrive quella faccia non esiste e nessuno le puo' dare un
        /// nome.
        ///
        /// PERCHE' NON BASTAVANO LE FIRME. La firma confronta un identificativo
        /// con la geometria che descriveva quando il piano e' nato. Se la
        /// faccia **non esisteva affatto**, non c'e' nessuna firma: niente da
        /// confrontare, nessun rifiuto, e il piano passa. La firma protegge
        /// dallo spostamento; questo protegge dall'invenzione.
        ///
        /// NON SI RIFIUTA IL LEGITTIMO. Un piano che crea geometria e poi ci
        /// lavora sopra e' normale, e passa: usa `*_rif`. Il messaggio lo dice,
        /// perche' un rifiuto che non indica la strada buona insegna soltanto a
        /// riprovare a caso.
        /// </summary>
        private static string UidsThatDoNotExist(
            Dictionary<string, object> schema,
            Dictionary<string, object> arguments,
            Dictionary<string, object> docState)
        {
            if (schema == null || arguments == null || docState == null) return null;
            object rawProp;
            if (!schema.TryGetValue("properties", out rawProp)) return null;
            var property = rawProp as Dictionary<string, object>;
            if (property == null) return null;

            var existing = StateUid(docState);

            foreach (var theField in property.Keys)
            {
                if (!theField.EndsWith("_uid", StringComparison.Ordinal)) continue;
                object val;
                if (!arguments.TryGetValue(theField, out val) || val == null) continue;

                foreach (var uid in Strings(val))
                {
                    if (existing.Contains(uid)) continue;

                    // SI DICE COSA C'E', non solo cosa manca. Su un documento
                    // vuoto l'elenco e' vuoto, ed e' proprio quello il caso in
                    // cui chi legge deve capire che deve cambiare strada.
                    string whatIsThere = existing.Count == 0
                        ? "Nel pezzo che ho guardato non c'era nessun identificativo: " +
                          "era vuoto."
                        : "Quelli che c'erano sono: " + List(existing) + ".";

                    return
                        "«" + theField + "» cita «" + uid + "», che nel pezzo che ho " +
                        "guardato non c'era. " + whatIsThere + "\n\n" +
                        "Gli identificativi nascono da «inspect_part» e valgono per " +
                        "QUELLA lettura. Se la geometria la crea un passo precedente di " +
                        "questo stesso piano, non ha ancora nessun identificativo e va " +
                        "nominata con il riferimento al passo — per esempio " +
                        "\"" + theField.Replace("_uid", "_ref") + "\": {\"step\": 1, " +
                        "\"kind\": \"top_face\"}. Se invece c'era gia', " +
                        "rileggi il pezzo e copia l'identificativo da li'.";
                }
            }
            return null;
        }

        /// <summary>
        /// Tutti i nomi che quello stato ha davvero MOSTRATO: gli uid di facce,
        /// spigoli, corpi, assi e lavorazioni, piu' i nomi di lavorazione che lo
        /// stato scrive in giro per altre vie.
        ///
        /// LA REGOLA: SI RACCOGLIE CIO' CHE LO STATO HA SCRITTO, NON CIO' CHE
        /// AVEVA IN MENTE DI SCRIVERE.
        ///
        /// Questo controllo blocca un piano PRIMA di eseguirlo quando cita un
        /// nome che non c'era. Se raccoglie meno nomi di quanti lo stato ne ha
        /// mostrati, rifiuta lavoro legittimo — e lo rifiuta nel modo peggiore,
        /// perche' chi ha scritto il piano quel nome l'ha letto nella risposta
        /// precedente e ha ragione a fidarsene.
        ///
        /// Fino alla 0.25.2 guardava solo il campo `uid` delle cinque famiglie.
        /// Ma lo stato pubblica nomi di lavorazione anche in tre altri posti:
        ///
        ///   * `from_feature` su ogni faccia — ed e' messo li' apposta per
        ///     «quel foro portalo a M10»;
        ///   * `created_by` su ogni corpo;
        ///   * la mappa `features` in fondo.
        ///
        /// E l'elenco `feature` invece e' FILTRATO per tipo e tagliato a
        /// `MaxFeature`. Quindi bastava un pezzo con piu' di ottanta
        /// lavorazioni, o una lavorazione di un tipo che non entra nell'elenco,
        /// perche' lo stato mostrasse un nome e il controllo lo dichiarasse
        /// inesistente.
        ///
        /// SI ALLARGA E NON SI STRINGE. L'insieme e' unico e non distingue le
        /// famiglie: `face_uid` accetta anche un nome di lavorazione. E'
        /// voluto — questo e' un controllo di ESISTENZA, non di tipo, e chi
        /// sbaglia famiglia viene fermato dopo con un messaggio che sa dire di
        /// che tipo e' quell'entita' (`IDoNotWorkOn`). Stringere qui vorrebbe
        /// dire rischiare un rifiuto sbagliato per guadagnare un messaggio
        /// leggermente migliore.
        /// </summary>
        private static HashSet<string> StateUid(Dictionary<string, object> docState)
        {
            var outside = new HashSet<string>(StringComparer.Ordinal);

            // 1. Gli uid veri e propri.
            foreach (var family in new[] { "faces", "edges", "bodies", "axes", "feature" })
                foreach (var entry in EntriesOf(docState, family))
                {
                    object uid;
                    if (entry.TryGetValue("uid", out uid) && uid != null)
                        outside.Add(Convert.ToString(uid));
                }

            // 2. I nomi di lavorazione scritti sulle facce e sui corpi.
            foreach (var entry in EntriesOf(docState, "faces"))
            {
                object itemName;
                if (entry.TryGetValue("from_feature", out itemName) && itemName != null)
                    outside.Add(Convert.ToString(itemName));
            }
            foreach (var entry in EntriesOf(docState, "bodies"))
            {
                object itemName;
                if (entry.TryGetValue("created_by", out itemName) && itemName != null)
                    outside.Add(Convert.ToString(itemName));
            }

            // 3. La mappa delle lavorazioni: le CHIAVI sono i numeri, i VALORI
            //    sono i nomi. Chi scrive un piano copia il nome, non il numero.
            object raw;
            if (docState.TryGetValue("features", out raw))
            {
                var map = raw as Dictionary<string, object>;
                if (map != null)
                    foreach (var v in map.Values)
                        if (v != null) outside.Add(Convert.ToString(v));
            }

            outside.Remove("");
            return outside;
        }

        /// <summary>
        /// Le voci di una famiglia dello stato, come dizionari.
        ///
        /// Le famiglie sono `List&lt;Dictionary&lt;...&gt;&gt;`, non `object[]`: un
        /// `as object[]` qui tornerebbe null in silenzio e il controllo direbbe
        /// «non esiste» su tutto. E' gia' successo una volta, con le firme.
        /// </summary>
        private static IEnumerable<Dictionary<string, object>> EntriesOf(
            Dictionary<string, object> docState, string family)
        {
            object raw;
            if (!docState.TryGetValue(family, out raw) || raw == null) yield break;
            var list = raw as System.Collections.IEnumerable;
            if (list == null) yield break;
            foreach (var v in list)
            {
                var entry = v as Dictionary<string, object>;
                if (entry != null) yield return entry;
            }
        }

        /// <summary>Un valore che puo' essere una stringa o un elenco di stringhe.</summary>
        private static IEnumerable<string> Strings(object val)
        {
            if (val is string s) { if (s.Length > 0) yield return s; yield break; }
            if (val is System.Collections.IEnumerable list)
            {
                foreach (var v in list)
                {
                    var t = Convert.ToString(v);
                    if (!string.IsNullOrEmpty(t)) yield return t;
                }
            }
        }

        /// <summary>
        /// L'elenco, ma corto. Su un pezzo con quattrocento facce scriverli
        /// tutti non aiuta nessuno: i primi dodici bastano a far capire che
        /// forma hanno i nomi.
        /// </summary>
        private static string List(HashSet<string> uid)
        {
            var sortedItems = uid.ToList();
            sortedItems.Sort(StringComparer.Ordinal);
            if (sortedItems.Count <= 12) return string.Join(", ", sortedItems);
            return string.Join(", ", sortedItems.Take(12)) +
                   " e altri " + (sortedItems.Count - 12);
        }

        /// <summary>
        /// Il nome di una lavorazione, dalla mappa che lo stato scrive in
        /// fondo. Null se non c'e': meglio una firma senza nome che una firma
        /// con un nome inventato.
        /// </summary>
        private static string FeatureName(Dictionary<string, object> docState, object id)
        {
            if (docState == null || id == null) return null;
            if (!docState.TryGetValue("features", out var g) || g == null) return null;
            var map = g as Dictionary<string, object>;
            if (map == null) return null;
            var key = Convert.ToString(id);
            return map.TryGetValue(key, out var itemName) && itemName != null
                ? Convert.ToString(itemName)
                : null;
        }

        /// <summary>
        /// La scatola del pezzo — {minX,minY,minZ,maxX,maxY,maxZ} in mm —
        /// ricavata dai due campi che lo stato pubblica: l'angolo
        /// (`bounding_box_from_mm`) e i lati (`bounding_box_mm`).
        ///
        /// Null se uno dei due manca. NON si inventa l'angolo mettendolo a
        /// zero: uno stato che dichiarasse una scatola appoggiata all'origine
        /// quando il pezzo e' centrato su di essa farebbe scrivere «stava a
        /// meta' del lato» di una faccia che stava all'estremo, e una
        /// spiegazione sbagliata e' peggio di nessuna spiegazione.
        /// </summary>
        private static double[] PartBox(Dictionary<string, object> docState)
        {
            double[] da = ThreeNumbers(docState, "bounding_box_from_mm");
            double[] sides = ThreeNumbers(docState, "bounding_box_mm");
            if (da == null || sides == null) return null;
            return new[]
            {
                da[0], da[1], da[2],
                da[0] + sides[0], da[1] + sides[1], da[2] + sides[2],
            };
        }

        /// <summary>Tre numeri da un campo dello stato, o null.</summary>
        private static double[] ThreeNumbers(Dictionary<string, object> docState, string key)
        {
            if (docState == null || !docState.TryGetValue(key, out var v) || v == null) return null;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return null;
            var outside = new List<double>();
            foreach (var x in list)
            {
                try
                {
                    outside.Add(Convert.ToDouble(
                        x, System.Globalization.CultureInfo.InvariantCulture));
                }
                catch { return null; }
            }
            return outside.Count == 3 ? outside.ToArray() : null;
        }

        /// <summary>
        /// Quante lavorazioni in errore aveva quello stato. Zero anche quando
        /// il campo non c'e' — ed e' giusto: lo stato lo scrive solo quando ce
        /// ne sono.
        /// </summary>
        private static int FeaturesInError(Dictionary<string, object> docState)
        {
            if (docState == null) return 0;
            object v;
            if (!docState.TryGetValue("features_in_error", out v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        private static object Take(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            return d.TryGetValue(key, out var v) ? v : null;
        }

        private static string Text(Dictionary<string, object> d, string key)
        {
            var v = Take(d, key);
            return v == null ? null : Convert.ToString(v);
        }


        private static Dictionary<string, object> Object(
            Dictionary<string, object> property, string[] mandatory)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = property,
                ["required"] = mandatory,
            };
        }

        /// <summary>
        /// Uno strumento, con le sue ANNOTAZIONI.
        ///
        /// PERCHE' LE ANNOTAZIONI NON SONO UN DETTAGLIO DI PROTOCOLLO.
        /// Il client MCP le usa per decidere se fermarsi a chiedere conferma a
        /// chi sta lavorando. Senza, `inspect_part` — che non tocca niente —
        /// e `build` — che modifica il pezzo in modo che nessuno annulla per
        /// lui — arrivano al client identici, e il client tratta identiche due
        /// cose che non lo sono.
        ///
        /// Per un progetto la cui tesi e' «nessuna geometria esiste senza che
        /// qualcuno l'abbia confermata», dichiararle e' il minimo.
        ///
        /// `idempotente` merita una riga: `build` NON lo e'. Rieseguire lo
        /// stesso piano non rimette il pezzo com'era — fa un SECONDO foro.
        /// Dichiararlo idempotente inviterebbe un client a riprovare da solo
        /// dopo un errore di rete, e il pezzo uscirebbe con il doppio delle
        /// lavorazioni senza che nessuno l'abbia chiesto.
        /// </summary>
        private static Dictionary<string, object> Tool(
            string itemName,
            string descriptionText,
            Dictionary<string, object> schema,
            bool readOnlyMode,
            bool destructive,
            bool idempotent)
        {
            return new Dictionary<string, object>
            {
                ["name"] = itemName,
                // IL TITOLO STA IN DUE POSTI, E NON E' UNA RIPETIZIONE.
                //
                // Nelle revisioni recenti dello spec MCP `title` e' un campo di
                // PRIMO LIVELLO dello strumento; prima stava solo dentro
                // `annotations`, dove resta per compatibilita'. Un client nuovo
                // legge quello di sopra, uno vecchio quello di sotto, e
                // scriverne uno solo vuol dire che meta' dei client mostra il
                // nome tecnico `inspect_part` a chi sta lavorando.
                // Scriverli tutti e due costa due righe e nessuna scelta.
                ["title"] = HumanTitle(itemName),
                ["description"] = descriptionText,
                ["inputSchema"] = schema,
                ["annotations"] = new Dictionary<string, object>
                {
                    ["title"] = HumanTitle(itemName),
                    ["readOnlyHint"] = readOnlyMode,
                    ["destructiveHint"] = destructive,
                    ["idempotentHint"] = idempotent,
                    // Il mondo esterno qui e' SolidWorks: un programma che non
                    // controlliamo, con documenti che cambiano sotto i piedi.
                    ["openWorldHint"] = true,
                },
            };
        }

        /// <summary>
        /// I nomi degli strumenti, per il messaggio di «non lo conosco».
        ///
        /// Scritti a mano e non ricavati da `Items()`: costruire l'elenco vero
        /// per rispondere a un errore vorrebbe dire ricostruire sessanta schemi
        /// JSON per stampare sei parole. Il prezzo di averli scritti due volte
        /// e' che possono divergere — e infatti c'e' un controllo di contratto
        /// che confronta questo elenco con quello che esce da `tools/list`.
        /// </summary>
        internal static readonly string[] ToolNames =
        {
            "inspect_part", "list_operations", "describe_operation",
            "build", "measure", "undo", "document",
            "api_search", "api_signature",
        };

        private static string HumanTitle(string itemName)
        {
            switch (itemName)
            {
                case "inspect_part": return "Inspect the open part";
                case "list_operations": return "List the operations";
                case "describe_operation": return "Describe an operation";
                case "build": return "Build in the part";
                case "measure": return "Measure the part, without touching it";
                case "undo": return "Withdraw the last plan";
                case "document": return "Open, create or pick the document";
                case "api_search": return "Search the SOLIDWORKS API";
                case "api_signature": return "Signature and knowledge of an API member";
                default: return itemName;
            }
        }
    }
}
