using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Oce.Solid.Agent.Mcp;
using Oce.Solid.Agent.Ponte;

namespace Oce.Solid.Agent.Strumenti
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
    /// `guarda_il_pezzo` non e' un accessorio: e' il primo, e senza di lui gli
    /// identificativi che gli altri vogliono (`F1`, `E4`, `Corpo1`) non
    /// esistono.
    ///
    /// **IL QUINTO E' `annulla`, ed e' arrivato dopo (voce R1).** Non e'
    /// un'operazione di disegno, e sta fuori dal catalogo per una ragione
    /// precisa: le operazioni si eseguono dentro `disegna`, cioe' dentro un
    /// piano, e annullare un piano da dentro il piano stesso e' un nodo. Il
    /// motivo per cui gli strumenti non sono trentotto — non esporre le
    /// operazioni una per una — non riguarda un gesto che operazione non e'.
    ///
    /// **Togliere una lavorazione invece SI'**, ed e' nel catalogo
    /// (`sopprimi_lavorazione`, `elimina_lavorazione`, voce R2): quelle sono
    /// operazioni sul pezzo come tutte le altre, si scrivono dentro un piano e
    /// hanno senso in mezzo a una sequenza. La differenza con `annulla` non e'
    /// «toglie» contro «aggiunge»: e' che `annulla` disfa **un piano**, e un
    /// piano non e' geometria.
    ///
    /// Fino alla 0.31.0 questo ponte diceva **ventuno volte** «annullalo con
    /// Ctrl+Z» a un destinatario che non ha una tastiera.
    /// </summary>
    internal sealed class Attrezzi
    {
        private readonly Sessione _sessione;
        private readonly Catalogo _catalogo;

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
        private readonly RegistroDelleDomande _registro;

        public Attrezzi(Sessione sessione, Catalogo catalogo)
            : this(sessione, catalogo, new RegistroDelleDomande()) { }

        /// <summary>Con un registro dato: lo usano le prove, che non
        /// vogliono scrivere accanto all'eseguibile.</summary>
        public Attrezzi(Sessione sessione, Catalogo catalogo,
                        RegistroDelleDomande registro)
        {
            _sessione = sessione;
            _catalogo = catalogo;
            _registro = registro ?? new RegistroDelleDomande(null);
        }

        public object Elenco()
        {
            return new object[]
            {
                Strumento(
                    "guarda_il_pezzo",
                    "GUARDA il documento aperto in SolidWorks prima di toccarlo: " +
                    "ingombro, volume, corpi, lavorazioni con le loro quote, facce " +
                    "e spigoli con il loro identificativo. Gli identificativi (F1, " +
                    "E4, Corpo1) che le altre operazioni vogliono nascono QUI e " +
                    "valgono per questa lettura: dopo ogni modifica si rilegge.\n\n" +
                    "Con «viste» torna anche una FOTOGRAFIA del pezzo, che e' " +
                    "l'unico modo di sapere che FORMA ha: l'elenco delle facce " +
                    "dice quante sono e quanto misurano, non com'e' fatto. " +
                    "Costa: ogni vista pesa piu' dell'intero stato scritto, " +
                    "quindi si chiede quando la forma e' in dubbio — dopo un " +
                    "guscio, dopo uno smusso, prima di dire che il pezzo e' " +
                    "finito — non a ogni lettura.",
                    Oggetto(
                        new Dictionary<string, object>
                        {
                            ["viste"] = new Dictionary<string, object>
                            {
                                ["type"] = "array",
                                ["description"] =
                                    "quali viste fotografare, al massimo " +
                                    MassimoDiViste + ". Se non sai quale, chiedi " +
                                    "«isometrica»: e' la sola che da sola mostra una " +
                                    "forma. Ci sono: " +
                                    string.Join(", ", Sessione.NomiDelleViste) + ".",
                                ["items"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string",
                                    ["enum"] = Nomi(Sessione.NomiDelleViste),
                                },
                            },
                            ["sezione"] = new Dictionary<string, object>
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
                                    ["piano"] = new Dictionary<string, object>
                                    {
                                        ["type"] = "string",
                                        ["enum"] = Nomi(Sessione.NomiDeiPiani),
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
                                ["required"] = new[] { "piano" },
                            },
                        },
                        new string[0]),
                    soloLettura: true, distruttivo: false, idempotente: true),

                Strumento(
                    "elenco_operazioni",
                    "L'elenco corto di cosa si puo' chiedere, per famiglia: un " +
                    "nome e una riga ciascuna. Per lo schema completo di una, usa " +
                    "«descrivi_operazione».",
                    Oggetto(
                        new Dictionary<string, object>
                        {
                            ["famiglia"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "solo una famiglia: estrusioni, lavorazioni, " +
                                    "riferimenti, corpi, saldature, proprieta', tavola",
                            },
                        },
                        new string[0]),
                    soloLettura: true, distruttivo: false, idempotente: true),

                Strumento(
                    "descrivi_operazione",
                    "Lo schema completo di una operazione: cosa vuole, in che " +
                    "unita', cosa deve esserci gia' nel documento.",
                    Oggetto(
                        new Dictionary<string, object>
                        {
                            ["nome"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "il nome esatto, da «elenco_operazioni»",
                            },
                        },
                        new[] { "nome" }),
                    soloLettura: true, distruttivo: false, idempotente: true),

                Strumento(
                    "disegna",
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
                    "stato costruito resta, e «annulla» lo toglie tutto in una " +
                    "volta.\n\n" +
                    "Con «prova: true» il piano viene SOLO controllato: non " +
                    "viene toccato niente, e la risposta dice cosa e' stato " +
                    "guardato e cosa no. Serve a sbagliare prima, invece che " +
                    "a meta' pezzo.\n\n" +
                    "Un passo puo' nominare la geometria creata da un passo " +
                    "PRECEDENTE dello stesso piano (per esempio faccia_rif: " +
                    "{passo: 1, genere: \"faccia_superiore\"}): e' l'unico modo, " +
                    "perche' quella faccia non ha ancora nessun identificativo.",
                    Oggetto(
                        new Dictionary<string, object>
                        {
                            ["passi"] = new Dictionary<string, object>
                            {
                                ["type"] = "array",
                                ["description"] =
                                    "le operazioni da fare, in ordine",
                                ["items"] = Oggetto(
                                    new Dictionary<string, object>
                                    {
                                        ["operazione"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "string",
                                            ["description"] = "il nome dell'operazione",
                                        },
                                        ["argomenti"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "object",
                                            ["description"] =
                                                "gli argomenti, come da «descrivi_operazione»",
                                        },
                                    },
                                    new[] { "operazione" }),
                            },
                            ["documento"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "Su quale documento disegnare: il titolo o il " +
                                    "percorso, copiato da «guarda_il_pezzo». Serve " +
                                    "SOLO se il documento davanti e' cambiato da " +
                                    "quando l'hai guardato: normalmente si omette. " +
                                    "Nominarlo vuol dire «so quale pezzo sto " +
                                    "toccando», e un nome che non corrisponde viene " +
                                    "rifiutato.",
                            },
                            ["prova"] = new Dictionary<string, object>
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
                        new[] { "passi" }),
                    // Rieseguire lo stesso piano NON rimette il pezzo
                    // com'era: fa un secondo foro. Dichiararlo idempotente
                    // inviterebbe un client a riprovare da solo dopo un
                    // errore di rete.
                    soloLettura: false, distruttivo: true, idempotente: false),

                // MISURA E' DISEGNA SENZA IL PERMESSO DI TOCCARE.
                //
                // Non e' una comodita' ed e' l'unico modo di dire la verita' nel
                // protocollo: le annotazioni MCP viaggiano su `tools/list`,
                // cioe' stanno sullo STRUMENTO e partono prima che esista una
                // chiamata. Finche' c'era solo `disegna`, il client leggeva
                // «distruttivo» e chiedeva conferma anche per
                // `proprieta_di_massa`, che non muove un millimetro cubo — e chi
                // conferma dieci volte di fila smette di leggere cosa conferma.
                // Non si poteva riparare dentro `disegna`: al momento
                // dell'annotazione non si sa ancora quale operazione arrivera'.
                // Serviva uno strumento in piu', e questo e' quello.
                Strumento(
                    "misura",
                    "MISURA il pezzo senza toccarlo. Prende le stesse " +
                    "operazioni di «disegna», ma accetta SOLO quelle della " +
                    "famiglia «verifiche» — quelle che leggono e basta.\n\n" +
                    "Se hai solo bisogno di sapere com'e' fatto il pezzo, usa " +
                    "questo: il tuo client sa che non cambia niente e non ti " +
                    "chiede conferma.\n\n" +
                    "Un'operazione che tocca il pezzo qui viene RIFIUTATA, e la " +
                    "risposta dice con quale strumento si fa. Il piano non " +
                    "parte: non si misura meta' pezzo e poi ci si ferma.",
                    Oggetto(
                        new Dictionary<string, object>
                        {
                            ["passi"] = new Dictionary<string, object>
                            {
                                ["type"] = "array",
                                ["description"] =
                                    "le misure da prendere, in ordine — solo " +
                                    "operazioni della famiglia «verifiche»",
                                ["items"] = Oggetto(
                                    new Dictionary<string, object>
                                    {
                                        ["operazione"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "string",
                                            ["description"] =
                                                "il nome dell'operazione di verifica",
                                        },
                                        ["argomenti"] = new Dictionary<string, object>
                                        {
                                            ["type"] = "object",
                                            ["description"] =
                                                "gli argomenti, come da «descrivi_operazione»",
                                        },
                                    },
                                    new[] { "operazione" }),
                            },
                            ["documento"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "Su quale documento misurare: il titolo o il " +
                                    "percorso, copiato da «guarda_il_pezzo». " +
                                    "Normalmente si omette.",
                            },
                        },
                        new[] { "passi" }),
                    // Idempotente sul serio, e non per cortesia: misurare due
                    // volte da' due volte la stessa risposta perche' non
                    // cambia niente in mezzo. E' la differenza con `disegna`,
                    // che al secondo giro fa un SECONDO foro.
                    soloLettura: true, distruttivo: false, idempotente: true),

                Strumento(
                    "annulla",
                    "RITIRA l'ultimo piano eseguito da «disegna», tutto intero. " +
                    "E' il gesto che questo ponte chiedeva di fare con Ctrl+Z, " +
                    "che tu non hai.\n\n" +
                    "Si annulla SOLO se il pezzo e' ancora come «disegna» lo " +
                    "aveva lasciato: se dopo il piano qualcuno ha lavorato a " +
                    "mano, mi fermo e dico cosa e' cambiato — annullare " +
                    "allora toglierebbe il lavoro di qualcun altro.\n\n" +
                    "Dopo, il pezzo torna al volume che aveva prima del piano, e " +
                    "gli identificativi delle facce NON sono piu' quelli: la " +
                    "risposta porta lo stato nuovo, ed e' quello che vale.",
                    Oggetto(new Dictionary<string, object>(), new string[0]),
                    // NON e' idempotente: chiamarlo due volte non annulla due
                    // volte lo stesso piano — la seconda toglierebbe quello
                    // che c'era PRIMA, cioe' lavoro che questo ponte non ha
                    // fatto. Il ricordo si cancella dopo il primo, ma
                    // dichiararlo idempotente inviterebbe un client a
                    // riprovare da solo dopo un errore di rete.
                    soloLettura: false, distruttivo: true, idempotente: false),

                Strumento(
                    "documento",
                    "Il DOCUMENTO su cui si lavora: crearne uno, aprirne uno dal " +
                    "disco, sapere quali sono aperti, portarne uno davanti.\n\n" +
                    "Senza documenti aperti gli altri strumenti non hanno niente " +
                    "da guardare: si comincia con «nuovo» (una parte vuota dal " +
                    "modello predefinito) o con «apri».\n\n" +
                    "Ogni volta che il documento davanti cambia, quello che avevi " +
                    "guardato non vale piu': prima di disegnare, «guarda_il_pezzo». " +
                    "Il ponte lo pretende.\n\n" +
                    "Un file aperto in SOLA LETTURA lo dico sempre: di solito vuol " +
                    "dire che un collega lo ha aperto, e «disegna» non ci scrive.\n\n" +
                    "«chiudi» RIFIUTA un documento con modifiche non salvate: non " +
                    "butto il lavoro di nessuno. Salvalo prima, o chiudilo a mano.",
                    Oggetto(
                        new Dictionary<string, object>
                        {
                            ["azione"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = new[] { "nuovo", "apri", "elenca", "attiva", "chiudi" },
                                ["description"] =
                                    "«nuovo»: crea un documento vuoto e lo porta davanti. " +
                                    "«apri»: apre un file dal disco. «elenca»: i documenti " +
                                    "aperti, e quale e' davanti. «attiva»: porta davanti uno " +
                                    "gia' aperto. «chiudi»: chiude uno aperto, solo se non ha " +
                                    "modifiche da salvare.",
                            },
                            ["tipo"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = new[] { "parte", "assieme" },
                                ["description"] =
                                    "solo con «nuovo». Predefinito «parte». Una tavola " +
                                    "nasce da un pezzo: «crea_disegno», dentro un piano.",
                            },
                            ["percorso"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] =
                                    "con «apri»: il file .sldprt, .sldasm o .slddrw, col " +
                                    "percorso intero. Con «attiva» e «chiudi»: il percorso " +
                                    "o, per un documento mai salvato, il titolo che " +
                                    "«elenca» ha dato.",
                            },
                        },
                        new[] { "azione" }),
                    // NON e' di sola lettura: «nuovo», «apri», «attiva» e
                    // «chiudi» cambiano il documento davanti. NON e' distruttivo:
                    // «chiudi» rifiuta un documento con modifiche non salvate,
                    // quindi chiude solo cio' che e' gia' tutto su disco, e non
                    // butta niente. NON e' idempotente: «nuovo» due volte fa due
                    // documenti.
                    soloLettura: false, distruttivo: false, idempotente: false),

                // THE WHOLE API, READ BY REFLECTION (entry U1.1).
                //
                // Two tools that never touch SOLIDWORKS: they read the interop
                // assembly loaded in this process and api_knowledge.json. They
                // work with SOLIDWORKS closed, and they are the first half of
                // Layer 1 (api_objects and api_call come with U1.2-U1.3).
                Strumento(
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
                    Oggetto(
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
                    soloLettura: true, distruttivo: false, idempotente: true),

                Strumento(
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
                    Oggetto(
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
                    soloLettura: true, distruttivo: false, idempotente: true),
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
                var asm = typeof(Attrezzi).Assembly;
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
                Protocollo.Nota("[bridge] api_knowledge.json not readable: " + ex.Message);
            }
            return null;
        });

        private static Dictionary<string, object> ApiAnswerContent(Func<Riscontro.Disegno.Api.ApiAnswer> ask, string tool)
        {
            try
            {
                if (ApiKnowledgeJson.Value == null)
                    return Protocollo.Contenuto(
                        tool + ": api_knowledge.json is missing from this build, and without it " +
                        "every member would look never probed — including the ones measured " +
                        "to crash SOLIDWORKS. Rebuild the bridge.", true);
                var answer = ask();
                return Protocollo.Contenuto(answer.Text, answer.IsError);
            }
            catch (Exception ex)
            {
                // The interop is missing (SOLIDWORKS not installed where the
                // bridge looks): say where to point it, not a stack trace.
                var inner = ex;
                while (inner.InnerException != null) inner = inner.InnerException;
                return Protocollo.Contenuto(
                    tool + " could not read the SOLIDWORKS interop: " + inner.Message +
                    " Set SW_INSTALL_PATH to the SOLIDWORKS installation folder.", true);
            }
        }

        private Dictionary<string, object> ApiSearch(Dictionary<string, object> argomenti)
        {
            var query = TestoDi(argomenti, "query");
            int limit;
            int.TryParse(TestoDi(argomenti, "limit") ?? "", NumberStyles.Integer,
                         CultureInfo.InvariantCulture, out limit);
            return ApiAnswerContent(
                () => Riscontro.Disegno.Api.ApiCatalog.Search(query, limit, ApiKnowledgeJson.Value),
                "api_search");
        }

        private Dictionary<string, object> ApiSignature(Dictionary<string, object> argomenti)
        {
            var name = TestoDi(argomenti, "name");
            return ApiAnswerContent(
                () => Riscontro.Disegno.Api.ApiCatalog.Signature(name, ApiKnowledgeJson.Value),
                "api_signature");
        }

        /// <summary>
        /// Lo strumento «documento» (voci Z-B1, Z-B2, Z-B5, Z-B6).
        ///
        /// Tutto il lavoro sta in `Ponte/Documenti.cs`; qui c'e' la parte che
        /// solo il ponte sa fare: quando il documento davanti cambia, quello
        /// che era stato guardato non vale piu', e le firme con lui. E' la
        /// regola di Z-A1 — i tre campi che parlano dello stesso pezzo si
        /// buttano insieme — applicata al punto in cui il ponte stesso cambia
        /// documento. La voce Z-B6 lo aveva scritto prima che esistesse.
        /// </summary>
        private Dictionary<string, object> Documento(Dictionary<string, object> argomenti)
        {
            if (!_sessione.Agganciata)
                return Protocollo.Contenuto("Il ponte non e' agganciato a SolidWorks.", true);

            string azione = TestoDi(argomenti, "azione");
            string errore;
            Dictionary<string, object> scheda;
            switch (azione)
            {
                case "elenca":
                {
                    var elenco = _sessione.Documenti(out int nascosti, out errore);
                    if (elenco == null) return Protocollo.Contenuto(errore, true);
                    var fuori = new Dictionary<string, object>
                    {
                        ["documenti"] = elenco,
                        ["quanti"] = elenco.Count,
                        ["caricati_dentro_un_assieme"] = nascosti,
                    };
                    if (elenco.Count == 0)
                        fuori["nota"] = "Nessun documento aperto: comincia con «nuovo» o «apri».";
                    return Protocollo.ContenutoConDati(Protocollo.InJson(fuori), fuori);
                }
                case "nuovo":
                    string tipo = TestoDi(argomenti, "tipo");
                    scheda = _sessione.NuovoDocumento(string.IsNullOrWhiteSpace(tipo) ? "parte" : tipo, out errore);
                    break;
                case "apri":
                    scheda = _sessione.ApriDocumento(TestoDi(argomenti, "percorso"), out errore);
                    break;
                case "attiva":
                    scheda = _sessione.AttivaDocumento(TestoDi(argomenti, "percorso"), out errore);
                    break;
                case "chiudi":
                {
                    var chiuso = _sessione.ChiudiDocumento(TestoDi(argomenti, "percorso"), out errore);
                    if (chiuso == null) return Protocollo.Contenuto(errore, true);
                    // Il documento guardato potrebbe essere proprio quello chiuso:
                    // si butta lo sguardo come per ogni cambio di documento.
                    _guardato = null;
                    _firmaGuardata = null;
                    _firmeViste = null;
                    chiuso["prossimo_passo"] = chiuso["davanti_adesso"] == null
                        ? "Non c'e' piu' nessun documento aperto: «nuovo» o «apri» per continuare."
                        : "Davanti adesso c'e' «" + Convert.ToString(chiuso["davanti_adesso"]) + "». " +
                          "Prima di disegnare, «guarda_il_pezzo».";
                    return Protocollo.ContenutoConDati(Protocollo.InJson(chiuso), chiuso);
                }
                default:
                    return Protocollo.Contenuto(
                        "«azione» vale «nuovo», «apri», «elenca», «attiva» o «chiudi». Ho ricevuto «" +
                        (azione ?? "") + "».", true);
            }
            if (scheda == null) return Protocollo.Contenuto(errore, true);

            // IL DOCUMENTO DAVANTI E' CAMBIATO (o potrebbe esserlo): quello che
            // era stato guardato e le sue firme si buttano INSIEME. «disegna» a
            // questo punto risponde «non ho ancora guardato nessun pezzo», che
            // e' la verita'.
            _guardato = null;
            _firmaGuardata = null;
            _firmeViste = null;

            scheda["prossimo_passo"] =
                "Davanti adesso c'e' «" + Convert.ToString(scheda["titolo"]) + "». " +
                "Prima di disegnare, «guarda_il_pezzo»: quello che avevi guardato " +
                "prima non vale piu'.";
            return Protocollo.ContenutoConDati(Protocollo.InJson(scheda), scheda);
        }

        private static string TestoDi(Dictionary<string, object> argomenti, string chiave)
        {
            if (argomenti == null || !argomenti.TryGetValue(chiave, out var v) || v == null) return null;
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)?.Trim();
        }

        /// <summary>
        /// Il filo dell'avanzamento della chiamata in corso (voce W2.2).
        ///
        /// Non e' mai null: spento quando il client non ha messo un gettone.
        /// Cosi' i punti che lo usano non hanno un `if` ciascuno.
        /// </summary>
        private Mcp.Avanzamento.Filo _filo = new Mcp.Avanzamento.Filo(null);

        public Dictionary<string, object> Chiama(
            string nome, Dictionary<string, object> argomenti, object gettone = null)
        {
            // UN FILO NUOVO A OGNI CHIAMATA. Il contatore di `progress` deve
            // crescere DENTRO una richiesta; fra richieste diverse ricomincia,
            // e tenerne uno solo vorrebbe dire che la seconda chiamata parte
            // da dove era arrivata la prima.
            _filo = new Mcp.Avanzamento.Filo(gettone);

            switch (nome)
            {
                case "guarda_il_pezzo": return GuardaIlPezzo(argomenti);
                case "elenco_operazioni": return ElencoOperazioni(argomenti);
                case "descrivi_operazione": return DescriviOperazione(argomenti);
                case "disegna": return Disegna(argomenti);
                case "misura": return Misura(argomenti);
                case "annulla": return Annulla();
                case "documento": return Documento(argomenti);
                case "api_search": return ApiSearch(argomenti);
                case "api_signature": return ApiSignature(argomenti);
                default:
                    // E PRIMA DI RISPONDERE, SI SCRIVE (voce Z-G7): questo
                    // e' il punto che sapeva e taceva.
                    _registro.StrumentoCheNonCe(nome);

                    // SI DICE QUALI CI SONO, non solo che questo non c'e'.
                    //
                    // Un modello che ha sbagliato nome non ha modo di
                    // correggersi leggendo «non conosco x»: puo' solo
                    // riprovare a indovinare, ed e' il modo in cui si finisce
                    // per chiamare tre volte la stessa cosa sbagliata. I nomi
                    // sono sette: stanno in una riga, e costano meno di un giro
                    // in piu'. E' la stessa regola dei rifiuti delle
                    // operazioni — il criterio, non il verdetto.
                    return Protocollo.Contenuto(
                        "Non conosco lo strumento «" + nome + "». Ci sono " +
                        "questi: " + string.Join(", ", NomiDegliStrumenti) + ". " +
                        "Le sessanta OPERAZIONI di disegno non sono strumenti: " +
                        "si chiedono a «disegna» (o a «misura», se leggono e " +
                        "basta) e si elencano con «elenco_operazioni».", true);
            }
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// L'ultimo documento che «guarda_il_pezzo» ha descritto.
        ///
        /// Non e' un ricordo di comodo: e' il contratto fra i due strumenti.
        /// Un piano nasce guardando UN documento, e va eseguito su QUELLO.
        /// </summary>
        private Dictionary<string, object> _guardato;

        /// <summary>
        /// COM'ERA FATTO IL PEZZO quando «guarda_il_pezzo» l'ha guardato
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
        /// «riuscita: true».
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
        private Dictionary<string, object> _firmaGuardata;

        /// <summary>
        /// Com'erano fatte le entita' quando «guarda_il_pezzo» le ha
        /// descritte: uid -> punto, normale, area, lunghezza.
        ///
        /// GLI UID SONO POSIZIONALI. L'add-in li riassegna a ogni lettura
        /// ordinando le facce per area, quindi basta un passo che cambia
        /// il pezzo perche' «F1» indichi un'altra faccia. Un piano pero'
        /// nasce da UNA lettura: i suoi uid parlano di quel momento li'.
        ///
        /// MISURATO il 14 settembre 2026, tre passi in una sola chiamata a
        /// «disegna» su un blocco 200x100x20:
        ///   1. estrudi il blocco          -> F1 = faccia di sopra, 20.000 mm2
        ///   2. tasca Ø60 profonda 5 su F1 -> la faccia di sopra scende a
        ///      17.172,57 mm2 e passa DIETRO a quella di sotto nell'ordine
        ///   3. tasca Ø10 profonda 5 su F1 -> «F1» adesso e' quella di SOTTO
        /// Tutti e tre i passi hanno risposto «riuscita: true». La tasca
        /// piccola e' finita sulla faccia sbagliata, le profondita' erano
        /// quelle chieste, e il volume tornava: 385.470,1 mm3, esatto. Non
        /// c'era niente, in quella risposta, che facesse sospettare.
        ///
        /// L'add-in sa gia' difendersi: legge «_firme» dagli argomenti e
        /// confronta. Ma quella chiave la scriveva solo il server di
        /// OpenPDM, e qui non la scriveva nessuno — quindi il confronto
        /// non aveva termini e passava sempre. Questo campo e' il termine
        /// che mancava.
        /// </summary>
        private Dictionary<string, object> _firmeViste;

        /// <summary>
        /// L'ultimo piano eseguito, il documento su cui e' andato, e com'era il
        /// documento subito dopo (voce W2.7).
        ///
        /// Servono tutti e tre INSIEME: il piano da solo non distingue un
        /// rinvio da una ripetizione voluta su un documento diverso, e lo stato
        /// da solo non dice niente su cosa si stia richiedendo.
        /// </summary>
        private string _ultimoPianoFatto;
        private Dictionary<string, object> _documentoDelPiano;
        private string _riepilogoDopoIlPiano;

        /// <summary>
        /// Quante viste al massimo in una sola risposta.
        ///
        /// Tre bastano a definire una forma — isometrica piu' due — e sono
        /// gia' tre volte il costo dello stato scritto. Nove (tutte quelle
        /// che esistono) sarebbero un ordine di grandezza in piu' per un
        /// pezzo, e nessuno le guarderebbe tutte.
        /// </summary>
        private const int MassimoDiViste = 3;

        /// <summary>Larghezza e altezza del fotogramma.</summary>
        private const int LarghezzaVista = 1024;
        private const int AltezzaVista = 768;

        private Dictionary<string, object> GuardaIlPezzo(
            Dictionary<string, object> argomenti)
        {
            var stato = _sessione.Stato(out var errore);
            if (stato == null) return Protocollo.Contenuto(errore, true);
            _guardato = IdentitaDelloStato(stato);
            _firmaGuardata = FirmaLeggera(stato);
            _firmeViste = FirmeDelloStato(stato);
            TogliLeAncore(stato);

            var chieste = VisteChieste(argomenti);
            var sezione = Prendi(argomenti, "sezione") as Dictionary<string, object>;
            if (chieste.Count == 0 && sezione == null)
                // Il testo per chi legge, i dati per chi li deve usare (W2.4).
                return Protocollo.ContenutoConDati(Protocollo.InJson(stato), stato);

            // LO STATO ESCE COMUNQUE, anche se nessuna fotografia riesce.
            // Un `guarda_il_pezzo` che fallisce perche' non si e' potuto
            // scattare toglierebbe a chi legge anche l'elenco delle facce —
            // cioe' l'unica cosa che gli serviva davvero. I motivi delle
            // fotografie mancate si dicono nel testo, accanto allo stato.
            var immagini = new List<KeyValuePair<string, byte[]>>();
            var mancate = new List<string>();
            var riuscite = new List<string>();

            foreach (var quale in chieste)
            {
                // LO STESSO DOCUMENTO DELLO STATO, PRIMA DI OGNI SCATTO.
                //
                // `Stato` e `Fotografia` chiedono a SOLIDWORKS qual e' il
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
                var adesso = _sessione.Identita(_sessione.DocumentoAttivo());
                if (!Sessione.StessoDocumento(_guardato, adesso))
                {
                    mancate.Add(quale + ": davanti adesso c'e' «" +
                                NomeLeggibile(adesso) + "», non «" +
                                NomeLeggibile(_guardato) + "». Non fotografo " +
                                "un pezzo diverso da quello che ho appena " +
                                "misurato.");
                    continue;
                }

                var png = _sessione.Fotografia(
                    quale, LarghezzaVista, AltezzaVista, out var perche);
                if (png != null && png.Length > 0)
                {
                    immagini.Add(new KeyValuePair<string, byte[]>("image/png", png));
                    riuscite.Add(quale);
                }
                else
                {
                    mancate.Add(quale + ": " + (perche ?? "non so perche'"));
                }
            }

            // ── LO SPACCATO, se lo hanno chiesto (voce V4) ───────────────
            //
            // Viene DOPO le viste esterne e prima del testo: nell'ordine in cui
            // si guarda un pezzo, prima da fuori e poi dentro.
            if (sezione != null)
            {
                var adessoSez = _sessione.Identita(_sessione.DocumentoAttivo());
                if (!Sessione.StessoDocumento(_guardato, adessoSez))
                {
                    mancate.Add("sezione: davanti adesso c'e' «" +
                                NomeLeggibile(adessoSez) + "», non «" +
                                NomeLeggibile(_guardato) + "».");
                }
                else
                {
                    string piano = Testo(sezione, "piano");
                    double offset = 0;
                    object grezzo = Prendi(sezione, "offset_mm");
                    if (grezzo != null)
                        try { offset = Convert.ToDouble(grezzo,
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
                    var png = _sessione.Spaccato(
                        VistaDiFronteAl(piano), piano, offset,
                        LarghezzaVista, AltezzaVista, out var perche);
                    if (png != null && png.Length > 0)
                    {
                        immagini.Add(new KeyValuePair<string, byte[]>("image/png", png));
                        riuscite.Add("sezione sul piano " + piano +
                                     (Math.Abs(offset) > 1e-9
                                         ? " a " + offset.ToString("0.##",
                                             System.Globalization.CultureInfo.InvariantCulture) + " mm"
                                         : ""));
                    }
                    else mancate.Add("sezione: " + (perche ?? "non so perche'"));
                }
            }

            var testo = new StringBuilder();
            if (riuscite.Count > 0)
            {
                // LA DIDASCALIA PRIMA DELLE IMMAGINI, E NELL'ORDINE.
                // Le immagini arrivano senza nome: sapere che la prima e'
                // l'isometrica e la seconda la frontale e' la differenza fra
                // una vista e un indovinello.
                testo.Append(riuscite.Count == 1
                    ? "Qui sotto la vista " + riuscite[0] + " del pezzo."
                    : "Qui sotto " + riuscite.Count + " viste del pezzo, in " +
                      "quest'ordine: " + string.Join(", ", riuscite) + ".");
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
                testo.Append(" Le fotografie mostrano la FORMA. **I numeri " +
                             "letti da un'immagine non sono autorevoli**: su " +
                             "queste stesse viste un diametro di 90 e' stato " +
                             "letto 79, e una parete di 12,5 e' stata letta 6 " +
                             "— in isometrica gli spessori sono scorciati e " +
                             "sembrano piu' grossi. Prima di usare una misura " +
                             "in un passo, prendila dallo stato qui di seguito " +
                             "oppure chiedila con «misura_fra_entita», " +
                             "«distanza_minima» o «ingombro_corpo». Nello stato " +
                             "stanno anche gli identificativi.\n\n");
            }
            if (mancate.Count > 0)
                testo.Append("Viste non riuscite — " +
                             string.Join("; ", mancate) + "\n\n");

            testo.Append(Protocollo.InJson(stato));

            return Protocollo.ContenutoConImmagini(testo.ToString(), immagini);
        }

        /// <summary>
        /// Quali viste ha chiesto chi legge: nomi veri, senza doppioni, al
        /// massimo <see cref="MassimoDiViste"/>.
        ///
        /// UN NOME SBAGLIATO NON SI SCARTA IN SILENZIO: arriva a
        /// `VistaDelPezzo.Scatta`, che risponde con l'elenco di quelli veri.
        /// Scartarlo qui vorrebbe dire rispondere «ecco il pezzo» a chi ha
        /// chiesto una vista che non ha mai avuto — e non capirebbe perche'
        /// manca.
        /// </summary>
        private static List<string> VisteChieste(Dictionary<string, object> argomenti)
        {
            // `Stringhe` C'ERA GIA', e fa esattamente la cosa difficile: una
            // stringa e' un `IEnumerable` **di caratteri**, quindi
            // `viste: "isometrica"` — l'errore piu' naturale del mondo, e lo
            // schema chiede un elenco — scorso come sequenza darebbe tre viste
            // che si chiamano «i», «s», «o». Tre rifiuti incomprensibili al
            // posto della fotografia chiesta.
            //
            // Scriverne una seconda copia qui sarebbe stato il difetto che
            // questo progetto ha visto tre volte oggi: la regola giusta gia'
            // scritta da un'altra parte, e una riga nuova che non la fa.
            var fuori = new List<string>();
            foreach (var x in Stringhe(Prendi(argomenti, "viste")))
            {
                var nome = x.Trim();
                if (nome.Length == 0) continue;
                if (fuori.Contains(nome, StringComparer.OrdinalIgnoreCase)) continue;
                fuori.Add(nome);
                if (fuori.Count >= MassimoDiViste) break;
            }
            return fuori;
        }

        /// <summary>
        /// La vista da cui si guarda uno spaccato: quella perpendicolare al
        /// piano di taglio.
        ///
        /// Null per un piano che non si conosce — e allora si scatta
        /// nell'orientamento corrente, che e' meglio di niente: il rifiuto per
        /// il nome sbagliato lo fa `ScattaInSezione`, che conosce i piani veri,
        /// e farlo anche qui vorrebbe dire scrivere due volte lo stesso elenco.
        /// </summary>
        private static string VistaDiFronteAl(string piano)
        {
            switch (piano)
            {
                case "frontale": return "frontale";
                case "superiore": return "superiore";
                case "destro": return "destra";
                default: return null;
            }
        }

        /// <summary>
        /// I nomi come vettore, che e' la forma che JSON Schema vuole per
        /// `enum`: un `IEnumerable&lt;string&gt;` serializzato diventa
        /// un oggetto, non un elenco.
        /// </summary>
        private static string[] Nomi(IEnumerable<string> nomi)
        {
            return new List<string>(nomi).ToArray();
        }

        /// <summary>
        /// L'ANCORA NON SI MANDA A CHI LEGGE, e non e' per risparmiare posto —
        /// anche se ne risparmia.
        ///
        /// `punto_mm` e `ancora` fanno due mestieri diversi e hanno due
        /// destinatari diversi:
        ///
        ///   * `punto_mm` serve a **selezionare**: e' un punto garantito sulla
        ///     faccia, e chi legge lo usa per dire dove lavorare;
        ///   * `ancora` serve a **riconoscere**: e' il baricentro d'area, che
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
        private static void TogliLeAncore(Dictionary<string, object> stato)
        {
            if (stato == null) return;
            var facce = (stato.TryGetValue("facce", out var g) ? g : null)
                        as System.Collections.IEnumerable;
            if (facce == null) return;
            foreach (var v in facce)
            {
                var voce = v as Dictionary<string, object>;
                if (voce == null) continue;
                voce.Remove("ancora");
                // IL PERSIST ID SI TOGLIE PER DUE RAGIONI, e la seconda basta
                // da sola. Non e' leggibile — e' un blob opaco, e chi legge non
                // ci puo' fare niente. E pesa: 375-423 byte per faccia, cioe'
                // ~530 caratteri in base64, che su sessanta facce sono ~32 KB,
                // il DOPPIO dell'intero tetto dello stato.
                voce.Remove("persist_id");
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
        private static Dictionary<string, object> FirmeDelloStato(
            Dictionary<string, object> stato)
        {
            if (stato == null) return null;
            var fuori = new Dictionary<string, object>();

            // LA SCATOLA DEL PEZZO, la stessa per tutte le entita'.
            //
            // Sta nella firma di ognuna e non una volta sola in fondo perche'
            // `_firme` e' una mappa uid -> firma, e chi la legge cerca per uid:
            // una chiave che uid non e' sarebbe un caso speciale da ricordarsi
            // in tre posti. Sei numeri per entita' in un allegato che porta gia'
            // ~530 caratteri di persist ID a faccia non si notano.
            //
            // SERVE SOLO A SCRIVERE UNA FRASE (voce E10): dove stava la faccia
            // rispetto al pezzo, quando il pezzo e' cambiato. Il motivo per cui
            // non puo' diventare un criterio di identita' sta per esteso in
            // `Riscontro.Disegno/Frasi.cs`.
            double[] scatolaPezzo = ScatolaDelPezzo(stato);

            // I CORPI SONO ENTITA' COME LE ALTRE: per un saldato sono LE
            // entita', e «tappa le estremita' di C1» nomina corpi.
            foreach (var famiglia in new[] { "facce", "spigoli", "feature", "assi", "corpi" })
            {
                // SI SCORRE COME IEnumerable, NON SI CASTA A object[].
                // Lo stato arriva dall'add-in con i suoi tipi veri, e la' le
                // famiglie sono List<Dictionary<string, object>>: un
                // «as object[]» torna null in silenzio, e il 14 settembre 2026
                // questo controllo e' rimasto spento per quello. Passando da
                // IEnumerable va bene tutte e due le forme, anche quella che
                // arriva da un JSON.
                var voci = (stato.TryGetValue(famiglia, out var g) ? g : null)
                           as System.Collections.IEnumerable;
                if (voci == null) continue;
                foreach (var v in voci)
                {
                    var voce = v as Dictionary<string, object>;
                    if (voce == null || !voce.TryGetValue("uid", out var uid) || uid == null)
                        continue;
                    var firma = new Dictionary<string, object>();
                    // `lavorazione` e `lavorazione_id` servono alla
                    // corroborazione del ritrovamento (voce E3): senza, una
                    // faccia di un'altra lavorazione finita nello stesso posto
                    // e' indistinguibile da quella che si cercava.
                    // `centro_mm` e `ingombro_mm` sono la firma dei CORPI, e
                    // fino alla 0.24.0 non venivano copiati: per «C1» la firma
                    // usciva vuota, `FirmaCombacia` non aveva niente da
                    // confrontare e rispondeva sempre di si'. I corpi erano
                    // controllati per ESISTENZA e non per IDENTITA'.
                    foreach (var campo in new[]
                             {
                                 "punto_mm", "normale", "area_mm2", "lunghezza_mm",
                                 "lavorazione_id", "centro_mm", "ingombro_mm", "ancora",
                                 "feature_id",
                                 // LA DIREZIONE DI UN ASSE (voce E4): senza, la
                                 // firma dell'asse avrebbe solo un punto, e un asse
                                 // girato attorno a quel punto passerebbe.
                                 "direzione",
                                 // IL PERSIST ID VIAGGIA QUI E SOLO QUI.
                                 // E' il CANDIDATO del ritrovamento (voce E6):
                                 // ~530 caratteri in base64 per faccia, che
                                 // nello stato sarebbero ~32 KB su sessanta
                                 // facce — il doppio del tetto. La firma non va
                                 // a nessun modello, e' un allegato interno al
                                 // passo, e li' il posto non e' un problema.
                                 "persist_id",
                             })
                        if (voce.TryGetValue(campo, out var x) && x != null)
                            firma[campo] = x;

                    // IL NOME DELLA LAVORAZIONE SI RISOLVE QUI.
                    //
                    // Lo stato lo scrive una volta sola, in una mappa in fondo,
                    // perche' ripeterlo su sessanta facce faceva sforare il
                    // tetto dei 16 KB. La firma invece se lo porta esteso: non
                    // va a nessun modello — e' un allegato interno al passo — e
                    // serve a scrivere un rifiuto che nomina la lavorazione
                    // attesa invece di darne il numero.
                    if (firma.TryGetValue("lavorazione_id", out var idLav) && idLav != null)
                    {
                        var nome = NomeLavorazione(stato, idLav);
                        if (nome != null) firma["lavorazione"] = nome;
                    }
                    // DOPO il conteggio di sopra, mai prima: una firma che
                    // avesse SOLO la scatola del pezzo non direbbe niente di
                    // quell'entita', e finirebbe lo stesso in `_firme` facendo
                    // credere che una firma ci sia.
                    if (firma.Count > 0 && scatolaPezzo != null)
                        firma["scatola_pezzo_mm"] = scatolaPezzo;
                    if (firma.Count > 0) fuori[Convert.ToString(uid)] = firma;
                }
            }
            return fuori.Count > 0 ? fuori : null;
        }

        /// <summary>Titolo e percorso presi dallo stato, senza ripassare dal COM.</summary>
        private static Dictionary<string, object> IdentitaDelloStato(
            Dictionary<string, object> stato)
        {
            if (stato == null) return null;
            return new Dictionary<string, object>
            {
                ["titolo"] = stato.TryGetValue("titolo", out var t) ? t : "",
                ["percorso"] = stato.TryGetValue("percorso", out var p) ? p : "",
            };
        }

        /// <summary>
        /// La firma leggera di uno stato: volume, quante lavorazioni, quando
        /// il file e' stato scritto (voce X2). Vedi <see cref="_firmaGuardata"/>.
        ///
        /// Le chiavi che mancano NON diventano zero. Un pezzo senza volume —
        /// un documento vuoto — ha una firma senza `volume_mm3`, e un
        /// confronto fra «non c'era» e «adesso c'e'» e' una differenza vera
        /// che va detta; metterci uno zero la trasformerebbe in «il volume e'
        /// passato da 0 a 96000», che e' un'altra frase e racconta un'altra
        /// cosa.
        /// </summary>
        private static Dictionary<string, object> FirmaLeggera(
            Dictionary<string, object> stato)
        {
            if (stato == null) return null;
            var f = new Dictionary<string, object>(StringComparer.Ordinal);

            if (stato.TryGetValue("volume_mm3", out var v) && v != null)
                f["volume_mm3"] = v;

            if (stato.TryGetValue("feature", out var e) &&
                e is System.Collections.IList elenco)
                f["lavorazioni"] = elenco.Count;

            // LA DATA DEL FILE PRENDE CIO' CHE IL VOLUME NON VEDE: un salvataggio
            // fatto da fuori. Un pezzo mai salvato non ha un file, e non avere la
            // chiave e' la risposta giusta — non «data zero».
            var percorso = Convert.ToString(
                stato.TryGetValue("percorso", out var p) ? p : "");
            if (!string.IsNullOrWhiteSpace(percorso))
            {
                try
                {
                    if (File.Exists(percorso))
                        f["file_scritto"] = File.GetLastWriteTimeUtc(percorso)
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
        internal static string CosaECambiato(
            Dictionary<string, object> prima, Dictionary<string, object> adesso)
        {
            if (prima == null || adesso == null) return null;

            var detti = new List<string>();
            foreach (var chiave in new[] { "volume_mm3", "lavorazioni", "file_scritto" })
            {
                bool cePrima = prima.TryGetValue(chiave, out var a);
                bool ceAdesso = adesso.TryGetValue(chiave, out var b);
                if (!cePrima && !ceAdesso) continue;

                string testoA = cePrima ? Convert.ToString(a, CultureInfo.InvariantCulture) : null;
                string testoB = ceAdesso ? Convert.ToString(b, CultureInfo.InvariantCulture) : null;
                if (string.Equals(testoA, testoB, StringComparison.Ordinal)) continue;

                if (chiave == "volume_mm3")
                    detti.Add("il volume e' passato da " + (testoA ?? "(nessuno)") +
                              " a " + (testoB ?? "(nessuno)") + " mm3");
                else if (chiave == "lavorazioni")
                    detti.Add("le lavorazioni sono passate da " + (testoA ?? "(nessuna)") +
                              " a " + (testoB ?? "(nessuna)"));
                else
                    detti.Add("il file e' stato riscritto (" + (testoA ?? "(mai)") +
                              " -> " + (testoB ?? "(mai)") + ")");
            }

            return detti.Count == 0 ? null : string.Join("; ", detti);
        }

        private static string NomeLeggibile(Dictionary<string, object> identita)
        {
            if (identita == null) return "(nessuno)";
            var p = Convert.ToString(identita.TryGetValue("percorso", out var x) ? x : "");
            if (!string.IsNullOrWhiteSpace(p)) return p;
            var t = Convert.ToString(identita.TryGetValue("titolo", out var y) ? y : "");
            return string.IsNullOrWhiteSpace(t) ? "(senza nome)" : t;
        }

        private Dictionary<string, object> ElencoOperazioni(
            Dictionary<string, object> argomenti)
        {
            if (_catalogo.ErroreDiCaricamento != null)
                return Protocollo.Contenuto(_catalogo.ErroreDiCaricamento, true);
            var famiglia = Testo(argomenti, "famiglia");
            var elenco = _catalogo.Elenco(famiglia);
            return Protocollo.ContenutoConDati(Protocollo.InJson(elenco), elenco);
        }

        private Dictionary<string, object> DescriviOperazione(
            Dictionary<string, object> argomenti)
        {
            if (_catalogo.ErroreDiCaricamento != null)
                return Protocollo.Contenuto(_catalogo.ErroreDiCaricamento, true);

            var nome = Testo(argomenti, "nome");
            var descrizione = _catalogo.Descrizione(nome);
            if (descrizione == null)
            {
                // UN ERRORE CHE AIUTA invece di uno che constata: chi ha
                // sbagliato nome quasi sempre ne ha in mente uno vicino.
                var simili = _catalogo.Simili(nome);
                _registro.OperazioneCheNonCe(nome, simili);
                return Protocollo.Contenuto(
                    "Non c'e' nessuna operazione che si chiama «" + nome + "»." +
                    (simili.Count > 0
                        ? " Forse cercavi: " + string.Join(", ", simili) + "."
                        : " Chiedi «elenco_operazioni» per vedere quali ci sono."),
                    true);
            }
            return Protocollo.ContenutoConDati(Protocollo.InJson(descrizione), descrizione);
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
        private const string NienteToccato =
            " Il piano non e' stato eseguito per niente: il documento e' rimasto " +
            "com'era. Correggi questo passo e rimanda il piano intero.";

        /// <summary>
        /// `misura` — `disegna` senza il permesso di toccare.
        ///
        /// IL CANCELLO STA QUI E NON DENTRO `Disegna`, e il motivo e' che deve
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
        private static string ImprontaDelPiano(object[] passi)
        {
            try
            {
                var testo = Protocollo.InJson(passi);
                using (var sha = System.Security.Cryptography.SHA1.Create())
                {
                    var b = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(testo));
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
        private static string RiepilogoDelloStato(Dictionary<string, object> stato)
        {
            if (stato == null) return null;
            Func<string, string> campo = k =>
            {
                object v;
                return stato.TryGetValue(k, out v) && v != null
                    ? Convert.ToString(v, CultureInfo.InvariantCulture)
                    : "-";
            };
            return campo("volume_mm3") + "|" + campo("corpi") + "|" +
                   campo("quante_lavorazioni") + "|" + campo("titolo");
        }

        private Dictionary<string, object> Misura(Dictionary<string, object> argomenti)
        {
            var passi = Prendi(argomenti, "passi") as object[];
            if (passi == null || passi.Length == 0)
                return Protocollo.Contenuto(
                    "Non c'e' niente da misurare: «passi» deve essere un elenco " +
                    "di {operazione, argomenti}.", true);

            // UN CANCELLO CHE NON SA NIENTE DEVE CHIUDERE, NON APRIRE.
            //
            // Il cancello qui sotto decide guardando la FAMIGLIA, e la famiglia
            // la sa solo il catalogo. Se il catalogo non si e' caricato,
            // `Famiglia()` risponde null per tutto, ogni passo passa il
            // controllo, e `misura` — che si e' dichiarata di sola lettura nel
            // protocollo, e per cui il client ha gia' deciso di NON chiedere
            // conferma — eseguirebbe un'estrusione.
            //
            // Sarebbe un difetto che non da' errore nella sua forma peggiore:
            // la difesa sparisce esattamente quando qualcosa e' gia' andato
            // storto, e il sintomo e' che tutto funziona.
            if (_catalogo.ErroreDiCaricamento != null)
                return Protocollo.Contenuto(
                    "Non posso misurare: il catalogo non si e' caricato (" +
                    _catalogo.ErroreDiCaricamento + "). Senza catalogo non so " +
                    "quali operazioni leggono e quali toccano il pezzo, e " +
                    "«misura» non esegue niente di cui non sappia rispondere. " +
                    "Con «disegna» lo stesso piano parte, ma il tuo client sa " +
                    "che quello puo' modificare.", true);

            var fuoriPosto = new List<string>();
            for (int i = 0; i < passi.Length; i++)
            {
                var passo = passi[i] as Dictionary<string, object>;
                string nome = passo == null ? null : Testo(passo, "operazione");
                if (string.IsNullOrWhiteSpace(nome)) continue;   // lo dira' Disegna

                string famiglia = _catalogo.Famiglia(nome);
                // Un'operazione che il catalogo non conosce NON e' un problema
                // di questo cancello: lo dice `Disegna`, con i suggerimenti di
                // `Simili`. Qui si tace, altrimenti lo stesso sbaglio darebbe
                // due messaggi diversi a seconda dello strumento.
                if (famiglia == null) continue;
                if (!string.Equals(famiglia, FamigliaDiSolaLettura, StringComparison.OrdinalIgnoreCase))
                    fuoriPosto.Add("il passo " + (i + 1) + ", «" + nome +
                                   "» (famiglia «" + famiglia + "»)");
            }

            if (fuoriPosto.Count > 0)
                return Protocollo.Contenuto(
                    "«misura» legge e basta, e " +
                    (fuoriPosto.Count == 1 ? "questo passo tocca" : "questi passi toccano") +
                    " il pezzo: " + string.Join("; ", fuoriPosto) + ". " +
                    "Non ho eseguito niente — nemmeno le misure che venivano " +
                    "prima, perche' meta' piano e' un risultato che non si sa " +
                    "leggere. Per fare queste operazioni usa «disegna», che il " +
                    "tuo client sa essere distruttivo. Per sapere cosa sa " +
                    "leggere «misura»: «elenco_operazioni» con famiglia «" +
                    FamigliaDiSolaLettura + "».", true);

            // SENZA la guardia sulla ripetizione: misurare due volte la stessa
            // cosa e' legittimo, e `misura` si dichiara idempotente.
            return EseguiIlPiano(argomenti, guardaSeERipetuto: false);
        }

        /// <summary>
        /// L'unica famiglia che non tocca il pezzo.
        ///
        /// NON c'e' anche «identita» qui, e va detto perche' e' il verso in cui
        /// sbagliare costa di piu': `scrivi_proprieta`, `crea_configurazione` e
        /// `attiva_configurazione` SCRIVONO nel documento. Dichiararle di sola
        /// lettura vorrebbe dire un client che non chiede conferma mentre
        /// qualcuno cambia la configurazione attiva di un pezzo confermato.
        /// </summary>
        private const string FamigliaDiSolaLettura = "verifiche";

        /// <summary>
        /// LA PROVA A VUOTO: sbagliare senza toccare il pezzo (voce Z-G6).
        ///
        /// ── PERCHE' SERVE ────────────────────────────────────────────
        ///
        /// `disegna` controlla gia' tutto il piano prima di cominciare, ed
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
        private Dictionary<string, object> ProvaIlPiano(Dictionary<string, object> argomenti)
        {
            if (_catalogo.ErroreDiCaricamento != null)
                return Protocollo.Contenuto(_catalogo.ErroreDiCaricamento, true);

            var passi = Prendi(argomenti, "passi") as object[];
            if (passi == null || passi.Length == 0)
                return Protocollo.Contenuto(
                    "Il piano e' vuoto: «passi» deve essere un elenco di " +
                    "{operazione, argomenti}.", true);

            var guai = new List<string>();

            // L'AVANZAMENTO DELLA PROVA A VUOTO (voce W2.2). Qui non si
            // esegue niente, quindi il totale e' il numero dei passi e basta:
            // non c'e' nessuna seconda meta' da aspettare.
            _filo.Totale = passi.Length;

            for (int i = 0; i < passi.Length; i++)
            {
                var passo = passi[i] as Dictionary<string, object>;
                string dove = "passo " + (i + 1);
                _filo.Passo(i + 1, "controllo il " + dove + " di " + passi.Length);
                if (passo == null) { guai.Add(dove + ": non e' un oggetto"); continue; }

                var nome = Testo(passo, "operazione");
                if (string.IsNullOrWhiteSpace(nome))
                { guai.Add(dove + ": manca «operazione»"); continue; }

                if (!_catalogo.Conosce(nome))
                {
                    var simili = _catalogo.Simili(nome);
                    _registro.OperazioneCheNonCe(nome, simili);
                    guai.Add(dove + ": «" + nome + "» non esiste" +
                             (simili != null && simili.Count > 0
                                 ? ". Forse: " + string.Join(", ", simili)
                                 : ""));
                    continue;
                }

                var schema = _catalogo.Schema(nome);
                var dati = Prendi(passo, "argomenti") as Dictionary<string, object>
                           ?? new Dictionary<string, object>();
                if (schema == null) continue;

                var proprieta = Prendi(schema, "properties") as Dictionary<string, object>;
                var richiesti = Prendi(schema, "required") as object[];

                if (richiesti != null)
                    foreach (var r in richiesti)
                    {
                        var campo = Convert.ToString(r);
                        if (string.IsNullOrEmpty(campo)) continue;
                        // `x_rif` vale per `x_uid` e viceversa: il catalogo
                        // dichiara obbligatorio uno dei due, e chiederli
                        // tutti e due sarebbe un rifiuto sbagliato.
                        var gemello = campo.EndsWith("_uid", StringComparison.Ordinal)
                            ? campo.Substring(0, campo.Length - 4) + "_rif"
                            : campo.EndsWith("_rif", StringComparison.Ordinal)
                                ? campo.Substring(0, campo.Length - 4) + "_uid"
                                : null;
                        if (dati.ContainsKey(campo)) continue;
                        if (gemello != null && dati.ContainsKey(gemello)) continue;
                        guai.Add(dove + " («" + nome + "»): manca «" + campo + "»");
                    }

                if (proprieta != null)
                    foreach (var campo in dati.Keys)
                    {
                        if (proprieta.ContainsKey(campo)) continue;
                        // UN ARGOMENTO SCONOSCIUTO NON E' UN DETTAGLIO: viene
                        // ignorato in silenzio, e il pezzo esce diverso da
                        // come lo si e' chiesto senza nessun errore.
                        guai.Add(dove + " («" + nome + "»): «" + campo +
                                 "» non e' un argomento di questa operazione, " +
                                 "e verrebbe IGNORATO");
                    }

                // I riferimenti a un passo precedente: `{passo: N, genere: ...}`.
                foreach (var coppia in dati)
                {
                    if (!coppia.Key.EndsWith("_rif", StringComparison.Ordinal)) continue;
                    var rif = coppia.Value as Dictionary<string, object>;
                    if (rif == null) continue;
                    var n = Prendi(rif, "passo");
                    int quale;
                    if (n == null || !int.TryParse(Convert.ToString(n), out quale)) continue;
                    if (quale >= 1 && quale <= i) continue;
                    guai.Add(dove + " («" + nome + "»): «" + coppia.Key +
                             "» rimanda al passo " + quale + ", che " +
                             (quale > i ? "non e' ancora stato fatto" : "non esiste"));
                }
            }

            var testo = new StringBuilder();
            testo.Append(guai.Count == 0
                ? "PROVA A VUOTO: il piano regge, per quello che si puo' " +
                  "sapere senza guardare il documento.\n\n"
                : "PROVA A VUOTO: " + guai.Count + " cose da sistemare.\n\n");
            foreach (var g in guai) testo.Append("  - " + g + "\n");
            if (guai.Count > 0) testo.Append("\n");

            // COSA NON E' STATO GUARDATO. Questa parte c'e' SEMPRE, anche
            // quando il piano regge -- soprattutto quando regge.
            testo.Append("Controllati: i nomi delle operazioni, gli argomenti " +
                         "obbligatori, quelli sconosciuti (che verrebbero " +
                         "ignorati in silenzio) e i rimandi a passi " +
                         "precedenti.\n\n");
            testo.Append("NON controllati, e per questi serve leggere il " +
                         "documento: che gli «uid» siano ancora validi, che il " +
                         "documento sia del tipo giusto per queste operazioni, " +
                         "che le facce e gli spigoli nominati esistano ancora, " +
                         "e se la geometria che chiedi sia possibile. " +
                         (_sessione.Agganciata
                             ? "SOLIDWORKS e' agganciato: «disegna» senza «prova» " +
                               "li controlla tutti prima di toccare qualunque cosa."
                             : "SOLIDWORKS NON e' agganciato adesso.") + "\n");

            return Protocollo.Contenuto(testo.ToString(), guai.Count > 0);
        }
        private Dictionary<string, object> Disegna(Dictionary<string, object> argomenti)
        {
            // LA PROVA A VUOTO NON TOCCA NIENTE, e non vuole nemmeno che
            // SOLIDWORKS sia agganciato: serve proprio a sbagliare prima.
            var chiesta = Prendi(argomenti, "prova");
            bool prova = false;
            if (chiesta != null) { try { prova = Convert.ToBoolean(chiesta); } catch { } }
            if (prova) return ProvaIlPiano(argomenti);
            return EseguiIlPiano(argomenti, guardaSeERipetuto: true);
        }

        /// <summary>
        /// Il corpo di `disegna`, con o senza la guardia sulla ripetizione.
        ///
        /// ── PERCHE' `misura` NON LA VUOLE, ED E' UN BUG TROVATO SCRIVENDOLA ──
        ///
        /// `misura` passa di qui: sono le stesse operazioni, con un cancello
        /// davanti. Ma **misurare due volte la stessa cosa e' legittimo** — anzi,
        /// e' il caso normale: si guarda, si pensa, si riguarda. E siccome una
        /// misura non cambia il documento, lo stato dopo e' identico allo stato
        /// prima: la guardia sulla ripetizione avrebbe visto un piano identico
        /// su un documento identico e **avrebbe rifiutato la seconda misura.**
        ///
        /// Non sarebbe stato solo scomodo: `misura` si dichiara
        /// `idempotentHint: true` nelle annotazioni, e un client legge quella
        /// riga per decidere se puo' riprovare da solo. Rifiutare il secondo
        /// tentativo avrebbe reso quella dichiarazione **una bugia nel
        /// protocollo** — la stessa categoria di difetto che `misura` esiste per
        /// non avere.
        ///
        /// Un parametro con valore predefinito su `Disegna` sarebbe bastato, ma
        /// qui si tiene la regola di casa: metodo nuovo, nome diverso.
        /// </summary>
        /// <summary>
        /// LE OPERAZIONI CHE NON SI ANNULLANO (voce W2.5).
        ///
        /// **Una sola, e il criterio conta piu' dell'elenco**: ci finisce
        /// cio' che toglie qualcosa che non si puo' rimettere con
        /// un'operazione del catalogo. `sopprimi_lavorazione` non c'e',
        /// perche' esiste `riattiva`; `elimina_lavorazione` si', perche' una
        /// lavorazione eliminata non torna.
        ///
        /// Un elenco lungo sarebbe peggio: chi conferma dieci volte di fila
        /// smette di leggere cosa conferma, ed e' la stessa ragione per cui
        /// `misura` esiste separata da `disegna`.
        /// </summary>
        private static readonly string[] NonSiAnnullano = { "elimina_lavorazione" };

        private Dictionary<string, object> EseguiIlPiano(
            Dictionary<string, object> argomenti, bool guardaSeERipetuto)
        {
            var passi = Prendi(argomenti, "passi") as object[];
            if (passi == null || passi.Length == 0)
                return Protocollo.Contenuto(
                    "Il piano e' vuoto: «passi» deve essere un elenco di " +
                    "{operazione, argomenti}.", true);

            // ── SI CHIEDE PRIMA DI TOCCARE QUALUNQUE COSA (voce W2.5) ──
            //
            // Prima anche di guardare se SOLIDWORKS e' agganciato, e non e'
            // un dettaglio d'ordine: leggere lo stato del documento e' gia'
            // una conversazione con SOLIDWORKS, e una conversazione con
            // SOLIDWORKS puo' non tornare (voce Z-C4). La domanda riguarda
            // l'intenzione, e l'intenzione si puo' chiedere a costo zero.
            //
            // `misura` non passa di qui con `guardaSeERipetuto` falso? Passa,
            // ma non puo' eseguire operazioni distruttive: il cancello e'
            // prima. Chiedere conferma li' sarebbe chiedere il permesso di
            // fare una cosa che verra' rifiutata comunque.
            if (guardaSeERipetuto)
            {
                var daConfermare = new List<string>();
                foreach (var x in passi)
                {
                    var passo = x as Dictionary<string, object>;
                    if (passo == null) continue;
                    var nome = Testo(passo, "operazione");
                    if (string.IsNullOrEmpty(nome)) continue;
                    foreach (var brutta in NonSiAnnullano)
                        if (string.Equals(nome, brutta, StringComparison.OrdinalIgnoreCase) &&
                            !daConfermare.Contains(nome))
                            daConfermare.Add(nome);
                }

                if (daConfermare.Count > 0)
                {
                    var domanda =
                        "Questo piano contiene " +
                        (daConfermare.Count == 1 ? "un'operazione che NON si annulla"
                                                 : "operazioni che NON si annullano") +
                        ": " + string.Join(", ", daConfermare) +
                        ". Quello che toglie non si rimette con nessuna " +
                        "operazione del catalogo. Vuoi che proceda?";

                    if (Oce.Solid.Agent.Mcp.Elicitazione.IlClientSaChiedere)
                    {
                        if (!Oce.Solid.Agent.Mcp.Elicitazione.Conferma(Console.Out, Console.In, domanda))
                            return Protocollo.Contenuto(
                                "Fermato: la conferma non e' arrivata, e non ho " +
                                "toccato niente. " + domanda, true);
                    }
                    else
                    {
                        // IL RIPIEGO SI DICHIARA (voce W2.5). Andare avanti
                        // in silenzio sarebbe il modo in cui una difesa
                        // sparisce senza che nessuno se ne accorga.
                        Protocollo.Nota(
                            "[ponte] " + string.Join(", ", daConfermare) +
                            ": non ho potuto chiedere conferma all'utente — " +
                            "questo client non dichiara «elicitation». Vado " +
                            "avanti come prima.");
                    }
                }
            }

            if (!_sessione.Agganciata)
                return Protocollo.Contenuto("Il ponte non e' agganciato a SolidWorks.", true);

            // SENZA ANCORA: questa lettura serve a sapere su QUALE documento si
            // lavora, quanto volume c'era e quante lavorazioni erano gia' in
            // errore. Non se ne costruiscono firme (quelle si fanno su «dopo»,
            // piu' sotto) e non torna a chi legge. L'ancora costa un
            // GetTessTriangles per faccia, e qui si pagava per buttarla.
            var prima = _sessione.StatoSenzaAncora(out var erroreLettura);
            if (prima == null)
                return Protocollo.Contenuto(erroreLettura, true);

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
            // CONFRONTA con quello che «guarda_il_pezzo» aveva descritto, e se
            // non combacia ci si ferma. Chi sa cosa sta facendo puo' dire
            // esplicitamente «si', quello davanti» con `qualunque_documento`.
            var adesso = IdentitaDelloStato(prima);
            var nominato = Testo(argomenti, "documento");

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
            bool loHaNominato =
                !string.IsNullOrWhiteSpace(nominato) &&
                Ponte.Sessione.StessoDocumento(
                    adesso,
                    new Dictionary<string, object>
                    {
                        [nominato.IndexOfAny(new[] { '\\', '/' }) >= 0
                            ? "percorso" : "titolo"] = nominato,
                    });

            if (!loHaNominato)
            {
                if (_guardato == null)
                    return Protocollo.Contenuto(
                        "Non ho ancora guardato nessun pezzo, quindi non so su quale " +
                        "documento vuoi che disegni: adesso davanti c'e' «" +
                        NomeLeggibile(adesso) + "». Chiama prima «guarda_il_pezzo» — " +
                        "serve comunque, perche' gli identificativi delle facce " +
                        "nascono li'.", true);

                if (!Ponte.Sessione.StessoDocumento(_guardato, adesso))
                    return Protocollo.Contenuto(
                        "Mi fermo: il documento e' cambiato. Avevi guardato «" +
                        NomeLeggibile(_guardato) + "», adesso davanti c'e' «" +
                        NomeLeggibile(adesso) + "». Disegnare qui vorrebbe dire " +
                        "scrivere nel pezzo sbagliato, e gli identificativi del tuo " +
                        "piano non valgono su questo.\n\n" +
                        "Se e' cambiato per una ragione che conosci, rileggilo con " +
                        "«guarda_il_pezzo». Se sai gia' che vuoi lavorare proprio su " +
                        "quello che c'e' adesso, ripeti passando «documento»: \"" +
                        NomeLeggibile(adesso) + "\".", true);
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
            bool quelloGuardato =
                _guardato != null && Ponte.Sessione.StessoDocumento(_guardato, adesso);

            if (quelloGuardato)
            {
                var cambiato = CosaECambiato(_firmaGuardata, FirmaLeggera(prima));
                if (cambiato != null)
                    return Protocollo.Contenuto(
                        "Mi fermo: il pezzo e' cambiato da quando l'hai guardato — " +
                        cambiato + ".\n\n" +
                        "Il documento e' giusto, il contenuto no. Gli identificativi " +
                        "delle facce («F1», «F2», ...) sono POSIZIONALI: nascono da una " +
                        "lettura e valgono per quella. Su una geometria cambiata «F1» " +
                        "puo' indicare un'altra faccia, e l'operazione riuscirebbe nel " +
                        "posto sbagliato senza dare nessun errore.\n\n" +
                        "Rileggi con «guarda_il_pezzo» e rifa' il piano sugli " +
                        "identificativi nuovi.", true);
            }

            // LE FIRME VALGONO SOLO SUL DOCUMENTO CHE LE HA PRODOTTE.
            // Se il piano nomina un altro documento, gli uid del piano non
            // parlano di questo e allegare le firme vecchie produrrebbe
            // rifiuti che non c'entrano niente. Meglio nessun confronto che
            // un confronto fra due pezzi diversi.
            var firme = quelloGuardato ? _firmeViste : null;

            // ── TRE CAMPI CHE DEVONO PARLARE DELLO STESSO PEZZO (Z-A1) ──
            //
            // `_guardato` dice QUALE documento, `_firmaGuardata` e
            // `_firmeViste` dicono COM'ERA. Finche' questa riga spostava
            // solo il primo, bastava un piano che nominasse un altro
            // documento — `documento: "..."`, la strada legittima — perche'
            // da quel momento `_guardato` indicasse il pezzo nuovo e le
            // firme il pezzo vecchio.
            //
            // Il guaio non si vede subito: si vede alla chiamata DOPO, dove
            // `quelloGuardato` torna vero e il confronto gira contro le
            // firme di un altro pezzo. O rifiuta un piano buono dicendo «il
            // pezzo e' cambiato», o — peggio — lascia passare un piano su
            // uid che non valgono piu'.
            //
            // Il principio giusto e' scritto nel commento qui sopra, per la
            // variabile locale: **meglio nessun confronto che un confronto
            // fra due pezzi diversi.** Qui lo si applica anche ai campi che
            // restano: se il documento cambia, le firme vecchie si buttano.
            // Le rifara' `guarda_il_pezzo`, oppure la fine di questo piano.
            if (!quelloGuardato)
            {
                _firmeViste = null;
                _firmaGuardata = null;
            }
            _guardato = adesso;

            // ── LO STESSO PIANO, DUE VOLTE (voce W2.7) ──────────────────
            //
            // Un client che non riceve la risposta — rete, timeout, riavvio —
            // rimanda la stessa richiesta. E' il comportamento normale di
            // qualunque client, ed e' documentato nelle annotazioni di questo
            // ponte: `disegna` **non** e' idempotente, rieseguire lo stesso
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
            string improntaPiano = ImprontaDelPiano(passi);
            if (guardaSeERipetuto &&
                _ultimoPianoFatto != null &&
                _ultimoPianoFatto == improntaPiano &&
                Ponte.Sessione.StessoDocumento(_documentoDelPiano, adesso) &&
                _riepilogoDopoIlPiano != null &&
                _riepilogoDopoIlPiano == RiepilogoDelloStato(prima))
            {
                return Protocollo.Contenuto(
                    "Questo piano l'ho gia' eseguito, e il documento e' ancora " +
                    "esattamente come l'ho lasciato. Non lo rifaccio: «disegna» " +
                    "non e' ripetibile — rieseguirlo non rimette il pezzo com'era, " +
                    "farebbe un SECONDO foro.\n\n" +
                    "Se la risposta di prima non ti e' arrivata, chiama " +
                    "«guarda_il_pezzo»: lo stato di adesso e' il risultato di " +
                    "quel piano.\n\n" +
                    "Se invece vuoi davvero rifarlo, cambia qualcosa nel piano " +
                    "oppure usa «annulla» e poi rilancialo: cosi' e' una scelta, " +
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
            var daFare = new List<Riscontro.Disegno.PassoDelPiano>();
            var esiti = new List<object>();
            bool tuttoBene = true;
            int ordine = 0;

            // L'AVANZAMENTO DI UN PIANO VERO SI RACCONTA IN DUE META'
            // (voce W2.2): prima lo si controlla tutto a documento intatto,
            // poi lo si esegue. Il totale le tiene insieme — la seconda meta'
            // la manda `PianoInMemoria`, che riprende il conto da dove questo
            // giro l'ha lasciato invece di ricalcolarselo.
            //
            // Dire «passo 3 di 5» durante il controllo e poi ricominciare da
            // «1 di 5» all'esecuzione farebbe tornare indietro la barra di chi
            // legge, e la specifica pretende che `progress` cresca.
            _filo.Totale = passi.Length * 2;

            foreach (var p in passi)
            {
                var passo = p as Dictionary<string, object>;
                if (passo == null) continue;
                ordine++;

                var nome = Testo(passo, "operazione");
                _filo.Passo(ordine, "controllo il passo " + ordine + " di " +
                                    passi.Length + ": " + nome);
                var suoi = Prendi(passo, "argomenti") as Dictionary<string, object>
                           ?? new Dictionary<string, object>();

                // SI CONTROLLA IL NOME PRIMA DI ESEGUIRE. L'add-in, davanti a
                // un nome che non conosce ma con dei «contorni» dentro, ricade
                // sull'estrusione di profilo: comodo per le ricette del server,
                // pericoloso qui, dove un nome storto diventerebbe un pezzo
                // storto senza che nessuno dica niente.
                if (!_catalogo.Conosce(nome))
                {
                    var simili = _catalogo.Simili(nome);
                    _registro.OperazioneCheNonCe(nome, simili);
                    esiti.Add(new Dictionary<string, object>
                    {
                        ["operazione"] = nome,
                        ["riuscita"] = false,
                        ["errore"] =
                            "Non esiste nessuna operazione «" + nome + "»." +
                            (simili.Count > 0
                                ? " Forse cercavi: " + string.Join(", ", simili) + "."
                                : "") + NienteToccato,
                    });
                    tuttoBene = false;
                    break;
                }

                // THE STEP GOES ON UNDER THE CATALOGUE'S NAME (Z-C21.10,
                // pattern S20). `Conosce` finds the operation ignoring case,
                // and until 0.140.0 the step went on under the name as it was
                // written. The library dispatches Ordinal: «Estrudi_Rettangolo»
                // came back «Operazione sconosciuta» right after this check had
                // said it exists, and «Rivoluzione», which carries «contorni»,
                // fell into the profile extrusion the comment above warns
                // about, with no «spessore_mm». Same rule as the enum values
                // in Ponte/Forma.cs: what passes here goes on spelled as the
                // catalogue spells it.
                nome = Testo(_catalogo.Trova(nome), "nome") ?? nome;

                // I PERCORSI SI CONTROLLANO PRIMA DI ESEGUIRE.
                // Sono l'unica cosa che questo ponte scrive fuori dal
                // documento aperto, e arrivano da un testo generato.
                var percorsoStorto = Ponte.Percorsi.Controlla(suoi);
                if (percorsoStorto != null)
                {
                    esiti.Add(new Dictionary<string, object>
                    {
                        ["operazione"] = nome,
                        ["riuscita"] = false,
                        ["errore"] = percorsoStorto + NienteToccato,
                    });
                    tuttoBene = false;
                    break;
                }

                // GLI ARGOMENTI RISPETTANO LO SCHEMA CHE ABBIAMO
                // DICHIARATO NOI? Il ponte offre trentasei operazioni con
                // schemi dettagliati: non guardarli vuol dire che lo
                // schema serve solo a suggerire, e che uno «spessore_mm»
                // dimenticato arriva a valle come zero — un'estrusione di
                // spessore nullo, rifiutata da SOLIDWORKS con un messaggio
                // suo, nel migliore dei casi.
                //
                // SOLO LA FORMA, MAI IL MERITO: vedi Ponte/Forma.cs.
                //
                // AND THE FORMA REWRITES `suoi` IN PLACE (Z-C21.10, S20): an
                // enum value accepted with a different case («Superiore»)
                // comes out spelled as the catalogue spells it («superiore»).
                // `suoi` is the very dictionary the step below hands to the
                // library, which compares Ordinal.
                var formaStorta = Ponte.Forma.Controlla(
                    nome, _catalogo.Schema(nome), suoi);
                if (formaStorta != null)
                {
                    esiti.Add(new Dictionary<string, object>
                    {
                        ["operazione"] = nome,
                        ["riuscita"] = false,
                        ["errore"] = formaStorta + NienteToccato,
                    });
                    tuttoBene = false;
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
                // Misurato il 14/9/2026: `[estrudi_rettangolo, taglio_cilindrico
                // su F1]` su un documento vuoto riusciva, volume 117.738,1. Il
                // foro e' finito sulla faccia piu' grande — che in quel caso era
                // anche quella giusta, ma **non l'aveva scelta nessuno**: «F1»
                // e' posizionale e si rinumera per area a ogni lettura.
                var uidStorto = UidCheNonEsistono(
                    _catalogo.Schema(nome), suoi, prima);
                if (uidStorto != null)
                {
                    esiti.Add(new Dictionary<string, object>
                    {
                        ["operazione"] = nome,
                        ["riuscita"] = false,
                        ["errore"] = uidStorto + NienteToccato,
                    });
                    tuttoBene = false;
                    break;
                }

                // LE FIRME DI QUANDO IL PIANO E' NATO, ALLEGATE AL PASSO.
                // Non si sovrascrive quello che il chiamante ha gia' messo:
                // un piano che porta le sue firme sa meglio di noi di cosa
                // sta parlando.
                if (firme != null && !suoi.ContainsKey("_firme"))
                    suoi["_firme"] = firme;

                daFare.Add(new Riscontro.Disegno.PassoDelPiano
                {
                    Ordine = ordine,
                    Operazione = nome,
                    Argomenti = suoi,
                    Riassunto = nome,
                });
            }

            // IL GIRO E' QUELLO DELLA LIBRERIA, NON UNO NOSTRO.
            // Da qui arrivano i riferimenti fra passi («la faccia in cima a
            // quello che hai fatto al passo 1») e la voce di annullamento
            // unica: un solo Ctrl+Z disfa il piano intero.
            if (tuttoBene)
            {
                var fatti = _sessione.EseguiPiano(daFare, out var erroreGiro, _filo);
                if (fatti != null) foreach (var e in fatti) esiti.Add(e);
                if (!string.IsNullOrEmpty(erroreGiro))
                {
                    tuttoBene = false;
                    // Un piano fermato PRIMA del primo passo non lascia nessun
                    // esito: senza questo, la risposta sarebbe un elenco vuoto
                    // e un «riuscita» implicito che nessuno ha detto.
                    if (fatti == null || fatti.Count == 0)
                        esiti.Add(new Dictionary<string, object>
                        {
                            ["operazione"] = daFare.Count > 0 ? daFare[0].Operazione : null,
                            ["riuscita"] = false,
                            ["errore"] = erroreGiro,
                        });
                }
            }

            var dopo = _sessione.Stato(out var erroreStato);

            // «stato_dopo» E' LA LETTURA SUCCESSIVA, E VA FIRMATA COME TALE.
            //
            // Questa risposta porta con se' lo stato del documento dopo i
            // passi, con gli identificativi nuovi: chi legge scrivera' il
            // piano seguente su QUESTI, non su quelli di «guarda_il_pezzo».
            // Se le firme restassero quelle di prima, il piano seguente —
            // scritto giusto — verrebbe rifiutato perche' confrontato con una
            // lettura che il suo autore non ha mai visto. Sarebbe un rifiuto
            // di cio' che e' corretto, che e' il difetto peggiore dei due.
            // E LA FIRMA SEGUE LA STESSA REGOLA (voce X2). Il pezzo e'
            // cambiato, ma l'ha cambiato il ponte facendo quello che gli e'
            // stato chiesto: se la firma restasse quella di
            // «guarda_il_pezzo», il `disegna` seguente si fermerebbe
            // dicendo «qualcuno ha toccato il pezzo a mano». Sarebbe il
            // falso positivo che la voce X2 metteva fra i rischi, e sarebbe
            // fastidioso al punto giusto per insegnare a ignorare l'avviso.
            if (dopo != null)
            {
                _firmeViste = FirmeDelloStato(dopo);
                _firmaGuardata = FirmaLeggera(dopo);

                // ── E ANCHE CHI E', NON SOLO COM'E' FATTO ────────────────
                //
                // `_guardato` veniva riportato all'identita' letta PRIMA di
                // eseguire, e un piano che contiene `salva_documento` cambia
                // l'identita' del documento mentre gira: prima del salvataggio
                // e' «Parte1597» senza percorso, dopo e' un percorso su disco.
                //
                // **Il `disegna` seguente veniva rifiutato**, con un messaggio
                // giusto e una causa sbagliata: «il documento e' cambiato,
                // avevi guardato Parte1597». Era cambiato, si' — l'aveva
                // cambiato il ponte facendo quello che gli era stato chiesto.
                //
                // Trovato il 23/9/2026 con una sonda che preparava una tavola:
                // estrudi -> taglia -> salva -> crea_disegno, che e' la
                // sequenza piu' normale che esista, e il quarto passo non
                // partiva. Il banco non poteva vederlo: chiama la libreria,
                // non il ponte. L'ha visto il gruppo che passa dalla porta del
                // cliente (voce F14), il giorno dopo essere nato.
                //
                // `annulla` faceva gia' cosi' tre metodi piu' sotto. Qui no.
                _guardato = IdentitaDelloStato(dopo);

                TogliLeAncore(dopo);
            }

            var risultato = new Dictionary<string, object>
            {
                // SI DICE SEMPRE SU COSA SI E' LAVORATO. Anche quando e' ovvio:
                // e' l'unico modo perche' chi legge se ne accorga quando ovvio
                // non era.
                ["documento"] = NomeLeggibile(adesso),
                ["esiti"] = esiti,
                ["stato_dopo"] = dopo,
                ["nota_stato"] = dopo == null ? erroreStato : null,
                ["volume_prima_mm3"] = Volume(prima),
                ["volume_dopo_mm3"] = Volume(dopo),
            };

            // IL CONTEGGIO DEGLI ERRORI SI DA' COME DELTA, NON COME NUMERO.
            //
            // «lavorazioni_in_errore: 1» dopo un piano sembra dire «il piano ha
            // rotto qualcosa». Puo' voler dire l'opposto: il pezzo era gia'
            // rotto quando e' arrivato, e il piano non c'entra. Il numero
            // assoluto mente su chi sia stato, ed e' il rischio scritto nella
            // voce D5.
            //
            // Si dice quanti ce n'erano prima solo quando cambia qualcosa: su
            // un pezzo sano che resta sano non c'e' niente da dire, e ripeterlo
            // a ogni risposta e' posto tolto a cio' che conta.
            int erroriPrima = LavorazioniInErrore(prima);
            int erroriDopo = LavorazioniInErrore(dopo);
            if (erroriPrima > 0 || erroriDopo > 0)
            {
                risultato["lavorazioni_in_errore_prima"] = erroriPrima;
                risultato["nota_errori"] =
                    erroriDopo > erroriPrima
                        ? "Il pezzo ha " + erroriDopo + " lavorazioni in errore, e prima " +
                          "ne aveva " + erroriPrima + ": questo piano ne ha rotte " +
                          (erroriDopo - erroriPrima) + ". Guarda «stato_dopo» per sapere quali."
                        : erroriDopo < erroriPrima
                            ? "Le lavorazioni in errore sono passate da " + erroriPrima +
                              " a " + erroriDopo + ": questo piano ne ha riparate " +
                              (erroriPrima - erroriDopo) + "."
                            : "Il pezzo aveva gia' " + erroriPrima + " lavorazioni in errore " +
                              "prima di questo piano, e ne ha ancora " + erroriDopo + ": " +
                              "non le ha rotte questo piano.";
            }

            // ── IL RICORDO PER «annulla» (voce R1) ───────────────────────
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
            // SI CANCELLA quando il pezzo non e' cambiato, e cancella anche il
            // ricordo di un piano PRECEDENTE che sarebbe ancora ritirabile.
            //
            // E' una perdita, ed e' voluta. `EsecutorePiano` apre la voce di
            // annullamento PRIMA del primo passo e la chiude comunque: un piano
            // fallito subito ne lascia una vuota, e se SOLIDWORKS la mette in
            // pila lo stesso, un passo indietro toglie quella — non il piano di
            // prima. Non c'e' nessuna API per guardare la pila e togliersi il
            // dubbio.
            //
            // Di solito, in questo progetto, **rifiutare cio' che e' corretto e'
            // il peggiore dei due modi di sbagliare**. Qui no, e vale la pena
            // dire perche': un rifiuto di troppo costa a chi legge una
            // rilettura e un piano nuovo; un annullamento di troppo cancella il
            // lavoro di una persona, e nessun messaggio lo rimette. Quando i due
            // errori non costano uguale, il criterio si rovescia.
            double? volPrima = NumeroDi(prima, "volume_mm3");
            double? volDopo = NumeroDi(dopo, "volume_mm3");
            int featPrima = QuanteFeature(prima);
            int featDopo = QuanteFeature(dopo);

            // Un volume che non si e' letto non conta come «cambiato» (voce
            // Z-C8): qui si sbaglia nel verso di NON ricordare, per la ragione
            // scritta sopra — un annullamento di troppo cancella il lavoro di
            // una persona.
            bool haCostruito =
                dopo != null &&
                (featDopo != featPrima ||
                 (volPrima.HasValue && volDopo.HasValue &&
                  Math.Abs(volDopo.Value - volPrima.Value) > ScartoVolumeMm3));

            _ultimoPiano = haCostruito
                ? new UltimoPiano
                {
                    Documento = adesso,
                    VolumePrima = volPrima,
                    VolumeDopo = volDopo,
                    FeaturePrima = featPrima,
                    FeatureDopo = featDopo,
                }
                : null;

            // SI RICORDA COSA SI E' FATTO, e solo se qualcosa si e' fatto
            // davvero (voce W2.7). Un piano rifiutato in blocco non ha toccato
            // niente: ricordarlo farebbe rifiutare il secondo tentativo di chi
            // ha appena corretto l'errore — cioe' la difesa scatterebbe contro
            // il caso che deve lasciar passare.
            // E non si ricorda niente di una MISURA: non ha cambiato il pezzo,
            // e ricordarla farebbe rifiutare un `disegna` successivo che per
            // caso avesse la stessa impronta.
            if (tuttoBene && guardaSeERipetuto)
            {
                _ultimoPianoFatto = improntaPiano;
                _documentoDelPiano = adesso;
                _riepilogoDopoIlPiano = RiepilogoDelloStato(dopo);
            }

            // I dati escono anche quando il piano e' andato male (W2.4): e'
            // proprio allora che un programma ha piu' bisogno di leggerli senza
            // ri-analizzare una stringa — quale passo, quale misura, quale
            // rifiuto.
            return Protocollo.ContenutoConDati(
                Protocollo.InJson(risultato), risultato, !tuttoBene);
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
        private sealed class UltimoPiano
        {
            public Dictionary<string, object> Documento;
            public double? VolumePrima;
            public double? VolumeDopo;
            public int FeaturePrima;
            public int FeatureDopo;
        }

        private UltimoPiano _ultimoPiano;

        /// <summary>
        /// Quanto si puo' sbagliare un volume e considerarlo lo stesso.
        ///
        /// Un millesimo di mm³: SOLIDWORKS ricalcola il volume a ogni rebuild e
        /// l'ultima cifra balla. Piu' stretto di cosi' rifiuterebbe pezzi
        /// identici; piu' largo lascerebbe passare una modifica vera — un foro
        /// Ø1 profondo 1 sono 0,79 mm³, quasi mille volte questa soglia.
        /// </summary>
        private const double ScartoVolumeMm3 = 0.001;

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
        /// LA MACCHINA C'ERA GIA'. `EsecutorePiano` apre e chiude una voce di
        /// annullamento intorno a ogni piano (voce B8, misurata: «un solo
        /// Ctrl+Z riporta il documento al valore di volume_prima_mm3»). Si
        /// azionava solo con una tastiera umana. E' la stessa forma della
        /// sezione V — la capacita' c'e', e non arriva a chi legge.
        ///
        /// ── IL RIFIUTO VIENE PRIMA ────────────────────────────────────────
        ///
        /// Fra il piano e questa chiamata il progettista puo' aver lavorato a
        /// mano. Premere annulla allora gli toglie il SUO lavoro, ed e' un
        /// danno che nessun messaggio ripara. Quindi si confrontano DUE
        /// numeri che la risposta di `disegna` gia' conteneva — il volume e
        /// quante lavorazioni ci sono — e se non tornano **ci si ferma e si
        /// dice cosa e' cambiato**.
        ///
        /// Sono due numeri, non una prova: un gesto che non cambia ne' il
        /// volume ne' il conteggio passa inosservato. Per quello si controlla
        /// anche DOPO, e se l'annullamento ha disfatto una cosa diversa da
        /// quella attesa, si RIMETTE (`UnPassoAvanti`) e si dice cos'e'
        /// successo. Non c'e' nessuna API per guardare la pila degli
        /// annullamenti prima di premere: provare, controllare, e disfare la
        /// prova sbagliata e' l'unica strada onesta.
        /// </summary>
        private Dictionary<string, object> Annulla()
        {
            if (!_sessione.Agganciata)
                return Protocollo.Contenuto("Il ponte non e' agganciato a SolidWorks.", true);

            var memoria = _ultimoPiano;
            if (memoria == null)
                return Protocollo.Contenuto(
                    "Non ho nessun piano da ritirare: «annulla» disfa l'ultimo " +
                    "«disegna» di questa conversazione, e qui non ne ho ancora " +
                    "eseguito nessuno che abbia costruito qualcosa.\n\n" +
                    "Non premo annulla alla cieca: toglierei qualcosa che non ho " +
                    "fatto io, e non saprei nemmeno dire cosa.", true);

            var prima = _sessione.StatoSenzaAncora(out var erroreLettura);
            if (prima == null) return Protocollo.Contenuto(erroreLettura, true);

            var adesso = IdentitaDelloStato(prima);
            if (!Ponte.Sessione.StessoDocumento(memoria.Documento, adesso))
                return Protocollo.Contenuto(
                    "Mi fermo: il documento e' cambiato. Il piano l'avevo eseguito " +
                    "su «" + NomeLeggibile(memoria.Documento) + "», adesso davanti " +
                    "c'e' «" + NomeLeggibile(adesso) + "». Annullare qui " +
                    "toglierebbe l'ultima cosa fatta su un pezzo che non c'entra.\n\n" +
                    "Porta davanti il pezzo giusto e richiamami.", true);

            double? volumeOra = NumeroDi(prima, "volume_mm3");
            int featureOra = QuanteFeature(prima);

            if (volumeOra == null || memoria.VolumeDopo == null)
                return Protocollo.Contenuto(
                    "Mi fermo: non riesco a misurare il volume del pezzo, e senza non so " +
                    "dirti se dopo il piano l'ha cambiato qualcun altro. Un passo indietro " +
                    "alla cieca potrebbe disfare il lavoro di chi sta davanti allo schermo: " +
                    "se vuoi comunque tornare indietro, e' un gesto da fare a mano.", true);

            if (Math.Abs(volumeOra.Value - memoria.VolumeDopo.Value) > ScartoVolumeMm3 ||
                featureOra != memoria.FeatureDopo)
                return Protocollo.Contenuto(
                    "Mi fermo: dopo il piano il pezzo e' cambiato, e non l'ho " +
                    "cambiato io.\n\n" +
                    Confronto("volume", memoria.VolumeDopo.Value, volumeOra.Value, "mm³") +
                    Confronto("lavorazioni", memoria.FeatureDopo, featureOra, "") +
                    "\nUn passo indietro adesso non disferebbe il mio piano: " +
                    "disferebbe l'ultima cosa fatta, che e' di qualcun altro. " +
                    "Se vuoi comunque tornare indietro, e' un gesto che deve fare " +
                    "chi sta davanti allo schermo, perche' solo lui sa cosa sta " +
                    "buttando via.", true);

            // ── SI PREME ─────────────────────────────────────────────────
            var erroreAnnulla = _sessione.UnPassoIndietro();
            if (erroreAnnulla != null)
                return Protocollo.Contenuto(erroreAnnulla, true);

            var dopo = _sessione.Stato(out var erroreStato);
            if (dopo == null)
                return Protocollo.Contenuto(
                    "Ho premuto annulla e poi non sono riuscito a rileggere il " +
                    "documento: " + erroreStato + "\n\n" +
                    "Non posso dirti se ha funzionato. Guarda il pezzo prima di " +
                    "fare altro.", true);

            double? volumeDopo = NumeroDi(dopo, "volume_mm3");
            int featureDopo = QuanteFeature(dopo);
            // SE IL VOLUME NON SI LEGGE, NON SI RIFA' ALLA CIECA (voce Z-C8):
            // «non e' tornato» porterebbe a un passo avanti che potrebbe
            // rimettere proprio il piano appena tolto.
            if (volumeDopo == null || memoria.VolumePrima == null)
            {
                _ultimoPiano = null;
                return Protocollo.Contenuto(
                    "Ho premuto annulla, ma poi non riesco a misurare il volume del pezzo: " +
                    "non posso dirti se e' tornato com'era. Non ho fatto altro. Guarda il " +
                    "pezzo prima di andare avanti.", true);
            }
            bool tornato =
                Math.Abs(volumeDopo.Value - memoria.VolumePrima.Value) <= ScartoVolumeMm3 &&
                featureDopo == memoria.FeaturePrima;

            if (!tornato)
            {
                // HA DISFATTO UN'ALTRA COSA: SI RIMETTE.
                // Il volume e il conteggio non erano cambiati, quindi il gesto
                // che stava in cima alla pila non era geometrico — un nome
                // cambiato, una configurazione, una vista. L'ho tolto io senza
                // volerlo, e lo rimetto io.
                var erroreRedo = _sessione.UnPassoAvanti();
                var riletto = _sessione.Stato(out _);

                _guardato = IdentitaDelloStato(riletto);
                _firmeViste = FirmeDelloStato(riletto);
                _firmaGuardata = FirmaLeggera(riletto);
                if (riletto != null) TogliLeAncore(riletto);

                return Protocollo.Contenuto(Protocollo.InJson(
                    new Dictionary<string, object>
                    {
                        ["annullato"] = false,
                        ["documento"] = NomeLeggibile(adesso),
                        ["motivo"] =
                            "Ho premuto annulla e il pezzo non e' tornato dov'era: " +
                            "il volume doveva essere " + Numero(memoria.VolumePrima.Value) +
                            " mm³ e le lavorazioni " + memoria.FeaturePrima +
                            ", invece sono " + Numero(volumeDopo.Value) + " mm³ e " +
                            featureDopo + ". Vuol dire che in cima alla pila degli " +
                            "annullamenti non c'era il mio piano, ma un gesto fatto " +
                            "dopo che non cambiava ne' il volume ne' il conteggio — " +
                            "quindi non potevo vederlo prima di premere." +
                            (erroreRedo == null
                                ? " L'ho rimesso a posto subito (un passo avanti)."
                                : " E NON sono riuscito a rimetterlo: " + erroreRedo +
                                  " Guarda il documento prima di fare altro."),
                        ["stato_dopo"] = riletto,
                    }), true);
            }

            // RIUSCITO: IL RICORDO SI CANCELLA.
            // Un secondo «annulla» non deve disfare quello che c'era PRIMA del
            // piano: quello non l'ha fatto questo ponte, e la pila degli
            // annullamenti non sa distinguere.
            _ultimoPiano = null;

            // GLI IDENTIFICATIVI NON SONO PIU' QUELLI, e chi legge deve
            // saperlo dalla risposta invece di scoprirlo al piano seguente:
            // gli uid sono posizionali e si rinumerano per area a ogni
            // lettura. Si firma questa lettura, esattamente come fa `disegna`.
            _guardato = IdentitaDelloStato(dopo);
            _firmeViste = FirmeDelloStato(dopo);
            _firmaGuardata = FirmaLeggera(dopo);
            TogliLeAncore(dopo);

            return Protocollo.Contenuto(Protocollo.InJson(
                new Dictionary<string, object>
                {
                    ["annullato"] = true,
                    ["documento"] = NomeLeggibile(adesso),
                    ["volume_prima_mm3"] = Math.Round(volumeOra.Value, 2),
                    ["volume_dopo_mm3"] = Math.Round(volumeDopo.Value, 2),
                    ["lavorazioni_prima"] = featureOra,
                    ["lavorazioni_dopo"] = featureDopo,
                    ["nota"] =
                        "Il piano e' stato ritirato tutto intero e il pezzo e' " +
                        "tornato com'era. Gli identificativi di «stato_dopo» sono " +
                        "quelli validi adesso: quelli di prima non valgono piu'.",
                    ["stato_dopo"] = dopo,
                }));
        }

        /// <summary>Una riga di confronto, solo se i due numeri sono diversi.</summary>
        private static string Confronto(string cosa, double atteso, double trovato, string unita)
        {
            if (Math.Abs(atteso - trovato) <= ScartoVolumeMm3) return "";
            return "  " + cosa + ": era " + Numero(atteso) +
                   (unita.Length > 0 ? " " + unita : "") + " quando ho finito, " +
                   "adesso e' " + Numero(trovato) +
                   (unita.Length > 0 ? " " + unita : "") + "\n";
        }

        private static string Numero(double v)
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
        private static double? NumeroDi(Dictionary<string, object> stato, string chiave)
        {
            if (stato == null) return null;
            object v;
            if (!stato.TryGetValue(chiave, out v)) return 0;
            if (v == null) return null;
            try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        /// <summary>
        /// Quante lavorazioni ha il pezzo, comprese quelle che il tetto ha
        /// lasciato fuori.
        ///
        /// SENZA `feature_non_elencate` QUESTO CONTEGGIO MENTIREBBE su ogni
        /// pezzo con piu' di ottanta lavorazioni: si fermerebbe a ottanta e
        /// direbbe «non e' cambiato niente» anche dopo dieci fori nuovi. E'
        /// esattamente il difetto della voce E12, ripetuto da questa parte.
        /// </summary>
        private static int QuanteFeature(Dictionary<string, object> stato)
        {
            if (stato == null) return 0;
            int quante = 0;
            foreach (var v in VociDi(stato, "feature")) { if (v != null) quante++; }
            object fuori;
            if (stato.TryGetValue("feature_non_elencate", out fuori) && fuori != null)
                try { quante += Convert.ToInt32(fuori); } catch { }
            return quante;
        }

        // ─────────────────────────────────────────────────────────────────

        private static object Volume(Dictionary<string, object> stato)
        {
            if (stato == null) return null;
            return stato.TryGetValue("volume_mm3", out var v) ? v : null;
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
        private static string UidCheNonEsistono(
            Dictionary<string, object> schema,
            Dictionary<string, object> argomenti,
            Dictionary<string, object> stato)
        {
            if (schema == null || argomenti == null || stato == null) return null;
            object grezzoProp;
            if (!schema.TryGetValue("properties", out grezzoProp)) return null;
            var proprieta = grezzoProp as Dictionary<string, object>;
            if (proprieta == null) return null;

            var esistenti = UidDelloStato(stato);

            foreach (var campo in proprieta.Keys)
            {
                if (!campo.EndsWith("_uid", StringComparison.Ordinal)) continue;
                object valore;
                if (!argomenti.TryGetValue(campo, out valore) || valore == null) continue;

                foreach (var uid in Stringhe(valore))
                {
                    if (esistenti.Contains(uid)) continue;

                    // SI DICE COSA C'E', non solo cosa manca. Su un documento
                    // vuoto l'elenco e' vuoto, ed e' proprio quello il caso in
                    // cui chi legge deve capire che deve cambiare strada.
                    string cosaCe = esistenti.Count == 0
                        ? "Nel pezzo che ho guardato non c'era nessun identificativo: " +
                          "era vuoto."
                        : "Quelli che c'erano sono: " + Elenca(esistenti) + ".";

                    return
                        "«" + campo + "» cita «" + uid + "», che nel pezzo che ho " +
                        "guardato non c'era. " + cosaCe + "\n\n" +
                        "Gli identificativi nascono da «guarda_il_pezzo» e valgono per " +
                        "QUELLA lettura. Se la geometria la crea un passo precedente di " +
                        "questo stesso piano, non ha ancora nessun identificativo e va " +
                        "nominata con il riferimento al passo — per esempio " +
                        "\"" + campo.Replace("_uid", "_rif") + "\": {\"passo\": 1, " +
                        "\"genere\": \"faccia_superiore\"}. Se invece c'era gia', " +
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
        ///   * `da_lavorazione` su ogni faccia — ed e' messo li' apposta per
        ///     «quel foro portalo a M10»;
        ///   * `fatto_da` su ogni corpo;
        ///   * la mappa `lavorazioni` in fondo.
        ///
        /// E l'elenco `feature` invece e' FILTRATO per tipo e tagliato a
        /// `MaxFeature`. Quindi bastava un pezzo con piu' di ottanta
        /// lavorazioni, o una lavorazione di un tipo che non entra nell'elenco,
        /// perche' lo stato mostrasse un nome e il controllo lo dichiarasse
        /// inesistente.
        ///
        /// SI ALLARGA E NON SI STRINGE. L'insieme e' unico e non distingue le
        /// famiglie: `faccia_uid` accetta anche un nome di lavorazione. E'
        /// voluto — questo e' un controllo di ESISTENZA, non di tipo, e chi
        /// sbaglia famiglia viene fermato dopo con un messaggio che sa dire di
        /// che tipo e' quell'entita' (`NonLavoroSu`). Stringere qui vorrebbe
        /// dire rischiare un rifiuto sbagliato per guadagnare un messaggio
        /// leggermente migliore.
        /// </summary>
        private static HashSet<string> UidDelloStato(Dictionary<string, object> stato)
        {
            var fuori = new HashSet<string>(StringComparer.Ordinal);

            // 1. Gli uid veri e propri.
            foreach (var famiglia in new[] { "facce", "spigoli", "corpi", "assi", "feature" })
                foreach (var voce in VociDi(stato, famiglia))
                {
                    object uid;
                    if (voce.TryGetValue("uid", out uid) && uid != null)
                        fuori.Add(Convert.ToString(uid));
                }

            // 2. I nomi di lavorazione scritti sulle facce e sui corpi.
            foreach (var voce in VociDi(stato, "facce"))
            {
                object nome;
                if (voce.TryGetValue("da_lavorazione", out nome) && nome != null)
                    fuori.Add(Convert.ToString(nome));
            }
            foreach (var voce in VociDi(stato, "corpi"))
            {
                object nome;
                if (voce.TryGetValue("fatto_da", out nome) && nome != null)
                    fuori.Add(Convert.ToString(nome));
            }

            // 3. La mappa delle lavorazioni: le CHIAVI sono i numeri, i VALORI
            //    sono i nomi. Chi scrive un piano copia il nome, non il numero.
            object grezzo;
            if (stato.TryGetValue("lavorazioni", out grezzo))
            {
                var mappa = grezzo as Dictionary<string, object>;
                if (mappa != null)
                    foreach (var v in mappa.Values)
                        if (v != null) fuori.Add(Convert.ToString(v));
            }

            fuori.Remove("");
            return fuori;
        }

        /// <summary>
        /// Le voci di una famiglia dello stato, come dizionari.
        ///
        /// Le famiglie sono `List&lt;Dictionary&lt;...&gt;&gt;`, non `object[]`: un
        /// `as object[]` qui tornerebbe null in silenzio e il controllo direbbe
        /// «non esiste» su tutto. E' gia' successo una volta, con le firme.
        /// </summary>
        private static IEnumerable<Dictionary<string, object>> VociDi(
            Dictionary<string, object> stato, string famiglia)
        {
            object grezzo;
            if (!stato.TryGetValue(famiglia, out grezzo) || grezzo == null) yield break;
            var elenco = grezzo as System.Collections.IEnumerable;
            if (elenco == null) yield break;
            foreach (var v in elenco)
            {
                var voce = v as Dictionary<string, object>;
                if (voce != null) yield return voce;
            }
        }

        /// <summary>Un valore che puo' essere una stringa o un elenco di stringhe.</summary>
        private static IEnumerable<string> Stringhe(object valore)
        {
            if (valore is string s) { if (s.Length > 0) yield return s; yield break; }
            if (valore is System.Collections.IEnumerable elenco)
            {
                foreach (var v in elenco)
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
        private static string Elenca(HashSet<string> uid)
        {
            var ordinati = uid.ToList();
            ordinati.Sort(StringComparer.Ordinal);
            if (ordinati.Count <= 12) return string.Join(", ", ordinati);
            return string.Join(", ", ordinati.Take(12)) +
                   " e altri " + (ordinati.Count - 12);
        }

        /// <summary>
        /// Il nome di una lavorazione, dalla mappa che lo stato scrive in
        /// fondo. Null se non c'e': meglio una firma senza nome che una firma
        /// con un nome inventato.
        /// </summary>
        private static string NomeLavorazione(Dictionary<string, object> stato, object id)
        {
            if (stato == null || id == null) return null;
            if (!stato.TryGetValue("lavorazioni", out var g) || g == null) return null;
            var mappa = g as Dictionary<string, object>;
            if (mappa == null) return null;
            var chiave = Convert.ToString(id);
            return mappa.TryGetValue(chiave, out var nome) && nome != null
                ? Convert.ToString(nome)
                : null;
        }

        /// <summary>
        /// La scatola del pezzo — {minX,minY,minZ,maxX,maxY,maxZ} in mm —
        /// ricavata dai due campi che lo stato pubblica: l'angolo
        /// (`ingombro_da_mm`) e i lati (`ingombro_mm`).
        ///
        /// Null se uno dei due manca. NON si inventa l'angolo mettendolo a
        /// zero: uno stato che dichiarasse una scatola appoggiata all'origine
        /// quando il pezzo e' centrato su di essa farebbe scrivere «stava a
        /// meta' del lato» di una faccia che stava all'estremo, e una
        /// spiegazione sbagliata e' peggio di nessuna spiegazione.
        /// </summary>
        private static double[] ScatolaDelPezzo(Dictionary<string, object> stato)
        {
            double[] da = TreNumeri(stato, "ingombro_da_mm");
            double[] lati = TreNumeri(stato, "ingombro_mm");
            if (da == null || lati == null) return null;
            return new[]
            {
                da[0], da[1], da[2],
                da[0] + lati[0], da[1] + lati[1], da[2] + lati[2],
            };
        }

        /// <summary>Tre numeri da un campo dello stato, o null.</summary>
        private static double[] TreNumeri(Dictionary<string, object> stato, string chiave)
        {
            if (stato == null || !stato.TryGetValue(chiave, out var v) || v == null) return null;
            var elenco = v as System.Collections.IEnumerable;
            if (elenco == null) return null;
            var fuori = new List<double>();
            foreach (var x in elenco)
            {
                try
                {
                    fuori.Add(Convert.ToDouble(
                        x, System.Globalization.CultureInfo.InvariantCulture));
                }
                catch { return null; }
            }
            return fuori.Count == 3 ? fuori.ToArray() : null;
        }

        /// <summary>
        /// Quante lavorazioni in errore aveva quello stato. Zero anche quando
        /// il campo non c'e' — ed e' giusto: lo stato lo scrive solo quando ce
        /// ne sono.
        /// </summary>
        private static int LavorazioniInErrore(Dictionary<string, object> stato)
        {
            if (stato == null) return 0;
            object v;
            if (!stato.TryGetValue("lavorazioni_in_errore", out v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        private static object Prendi(Dictionary<string, object> d, string chiave)
        {
            if (d == null) return null;
            return d.TryGetValue(chiave, out var v) ? v : null;
        }

        private static string Testo(Dictionary<string, object> d, string chiave)
        {
            var v = Prendi(d, chiave);
            return v == null ? null : Convert.ToString(v);
        }


        private static Dictionary<string, object> Oggetto(
            Dictionary<string, object> proprieta, string[] obbligatori)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = proprieta,
                ["required"] = obbligatori,
            };
        }

        /// <summary>
        /// Uno strumento, con le sue ANNOTAZIONI.
        ///
        /// PERCHE' LE ANNOTAZIONI NON SONO UN DETTAGLIO DI PROTOCOLLO.
        /// Il client MCP le usa per decidere se fermarsi a chiedere conferma a
        /// chi sta lavorando. Senza, `guarda_il_pezzo` — che non tocca niente —
        /// e `disegna` — che modifica il pezzo in modo che nessuno annulla per
        /// lui — arrivano al client identici, e il client tratta identiche due
        /// cose che non lo sono.
        ///
        /// Per un progetto la cui tesi e' «nessuna geometria esiste senza che
        /// qualcuno l'abbia confermata», dichiararle e' il minimo.
        ///
        /// `idempotente` merita una riga: `disegna` NON lo e'. Rieseguire lo
        /// stesso piano non rimette il pezzo com'era — fa un SECONDO foro.
        /// Dichiararlo idempotente inviterebbe un client a riprovare da solo
        /// dopo un errore di rete, e il pezzo uscirebbe con il doppio delle
        /// lavorazioni senza che nessuno l'abbia chiesto.
        /// </summary>
        private static Dictionary<string, object> Strumento(
            string nome,
            string descrizione,
            Dictionary<string, object> schema,
            bool soloLettura,
            bool distruttivo,
            bool idempotente)
        {
            return new Dictionary<string, object>
            {
                ["name"] = nome,
                // IL TITOLO STA IN DUE POSTI, E NON E' UNA RIPETIZIONE.
                //
                // Nelle revisioni recenti dello spec MCP `title` e' un campo di
                // PRIMO LIVELLO dello strumento; prima stava solo dentro
                // `annotations`, dove resta per compatibilita'. Un client nuovo
                // legge quello di sopra, uno vecchio quello di sotto, e
                // scriverne uno solo vuol dire che meta' dei client mostra il
                // nome tecnico `guarda_il_pezzo` a chi sta lavorando.
                // Scriverli tutti e due costa due righe e nessuna scelta.
                ["title"] = TitoloUmano(nome),
                ["description"] = descrizione,
                ["inputSchema"] = schema,
                ["annotations"] = new Dictionary<string, object>
                {
                    ["title"] = TitoloUmano(nome),
                    ["readOnlyHint"] = soloLettura,
                    ["destructiveHint"] = distruttivo,
                    ["idempotentHint"] = idempotente,
                    // Il mondo esterno qui e' SolidWorks: un programma che non
                    // controlliamo, con documenti che cambiano sotto i piedi.
                    ["openWorldHint"] = true,
                },
            };
        }

        /// <summary>
        /// I nomi degli strumenti, per il messaggio di «non lo conosco».
        ///
        /// Scritti a mano e non ricavati da `Elenco()`: costruire l'elenco vero
        /// per rispondere a un errore vorrebbe dire ricostruire sessanta schemi
        /// JSON per stampare sei parole. Il prezzo di averli scritti due volte
        /// e' che possono divergere — e infatti c'e' un controllo di contratto
        /// che confronta questo elenco con quello che esce da `tools/list`.
        /// </summary>
        internal static readonly string[] NomiDegliStrumenti =
        {
            "guarda_il_pezzo", "elenco_operazioni", "descrivi_operazione",
            "disegna", "misura", "annulla", "documento",
            "api_search", "api_signature",
        };

        private static string TitoloUmano(string nome)
        {
            switch (nome)
            {
                case "guarda_il_pezzo": return "Guarda il pezzo aperto";
                case "elenco_operazioni": return "Elenco delle operazioni";
                case "descrivi_operazione": return "Descrivi una operazione";
                case "disegna": return "Disegna nel pezzo";
                case "misura": return "Misura il pezzo, senza toccarlo";
                case "annulla": return "Ritira l'ultimo piano";
                case "documento": return "Apri, crea o scegli il documento";
                case "api_search": return "Search the SOLIDWORKS API";
                case "api_signature": return "Signature and knowledge of an API member";
                default: return nome;
            }
        }
    }
}
