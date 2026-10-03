using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Programma;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// UN «NO» SENZA UNA STRADA COSTA UN TURNO A CHI LO LEGGE (voce Z-G4).
    ///
    /// ── IL FATTO ─────────────────────────────────────────────────────────
    ///
    /// «Il profilo non ha la misura 40X40X3. Nel file quella misura si scrive
    /// 40x40x3» ha corretto il modello **in un turno**. «Il profilo non ha
    /// quella misura» lo avrebbe mandato a indovinare.
    ///
    /// La differenza non e' cortesia: e' il numero di giri che serve per
    /// arrivare al pezzo. Un rifiuto che nomina l'alternativa e' una
    /// correzione; uno che dice solo no e' un ostacolo.
    ///
    /// ── LA MISURA, E PERCHE' NON E' QUELLA DELLA VOCE ────────────────────
    ///
    /// La voce Z-G4 diceva «~162 punti di rifiuto, ~39 (24%) dicono anche cosa
    /// fare». Contati il 23/9/2026 su tutti i `.cs` della libreria: i punti
    /// sono **576** — `grep -c "Fallito("` ne trova 577, meno la definizione —
    /// e quelli che offrono una strada sono **66**, cioe' l'**11%**.
    ///
    /// Il numero della voce era una stima su un sottoinsieme; questo e' il
    /// conto intero. *Piu' punti e percentuale piu' bassa: la voce sottostimava
    /// il lavoro, non lo sopravvalutava.*
    ///
    /// ── PERCHE' UNA CRICCA E NON UNA CORREZIONE ──────────────────────────
    ///
    /// Cinquecentodieci messaggi da riscrivere sono le due giornate che la voce
    /// stima, e non si fanno in un pomeriggio. Ma **si puo' impedire che
    /// peggiori**: il numero qui sotto e' un pavimento, e ogni messaggio che
    /// impara a dire cosa fare lo alza. Un rifiuto nuovo scritto muto fa
    /// scendere la percentuale e il controllo diventa rosso.
    ///
    /// ── E IL RILEVATORE E' GENEROSO APPOSTA ──────────────────────────────
    ///
    /// Le parole che contano come «strada» sono un elenco largo — «usa»,
    /// «metti», «oppure», «si scrive»… Largo vuol dire che **sovrastima** i
    /// messaggi buoni: il vero 11% e' probabilmente piu' basso. Un rilevatore
    /// severo avrebbe prodotto un numero piu' lusinghiero da migliorare e meno
    /// vero, e *si puo' barare aggiungendo la parola «oppure»* — ma barare
    /// qui vorrebbe dire scrivere un messaggio peggiore apposta, e a quel
    /// punto il problema non e' il controllo.
    /// </summary>
    internal static class ProveIRifiuti
    {
        /// <summary>
        /// Quanti rifiuti, sui **537 che hanno un messaggio proprio**, offrono
        /// una strada. **Ottantatre il 23/9/2026, cioe' il 15%.** Puo' solo
        /// salire.
        ///
        /// ── DUE NUMERI NELLO STESSO GIORNO, E IL SECONDO NON E' UN PROGRESSO ──
        ///
        /// La prima misura diceva **66**. Poi l'elenco dei rimedi e' stato
        /// guardato accanto ai messaggi veri, e contava muto
        /// «So fare: filettato, passaggio, lamato, svasato» — che **nomina le
        /// alternative**, cioe' fa esattamente quello che la voce chiede. Con
        /// le forme che il codice usa davvero il conto e' 83.
        ///
        /// *Fra 66 e 83 non ha lavorato nessuno: e' migliorato il rilevatore.*
        /// Scriverlo qui e' il punto — un pavimento che sale senza che nessuno
        /// abbia scritto un messaggio migliore insegna a leggere quel numero
        /// come un risultato, e la volta dopo nessuno controlla.
        ///
        /// **84 dal 24/9/2026, e stavolta e' lavoro vero.** Togliendo il
        /// ripiego cablato alla libreria dei profili, `weldment_profiles` ha
        /// guadagnato un rifiuto che distingue «non so dove sia» da «non c'e'»
        /// e dice quale variabile impostare. Prima i due casi davano lo stesso
        /// messaggio, con un percorso vuoto fra virgolette.
        ///
        /// **85 dal 24/9/2026 sera (libreria 0.113.0).** `set_dimension` ha un
        /// rifiuto nuovo — l'apertura che esce dal materiale (voce E18 c) — e
        /// dice la strada: «sposta prima l'apertura, poi cambia la quota».
        ///
        /// **87 con le operazioni nuove della stessa versione**: `base_flange`
        /// («allunga il tratto o stringi il raggio») e `flat_pattern_dxf` («salvalo
        /// prima»).
        ///
        /// **88 dal 25/9/2026 (libreria 0.114.0)**: `weld_bead`
        /// riscritto su due facce, coi rifiuti nuovi che dicono quale faccia
        /// indicare invece.
        ///
        /// **90 dal 25/9/2026 (libreria 0.116.0)**: l'ancora (voce E18), coi
        /// rifiuti che dicono come ancorare l'asse mancante e a quale spigolo
        /// vivo attaccarsi invece che al bordo di uno smusso.
        ///
        /// **75 dal 25/9/2026 (libreria 0.117.0), e SCENDE senza che nessun
        /// messaggio sia peggiorato.** I rifiuti nuovi di Z-C8 avevano fatto
        /// salire il conto a 92, e guardandoli uno per uno nessuno dei due
        /// scatti era una strada: «prima di scrivere» contiene «scriv», e un
        /// rifiuto chiuso sulla sua riga si prendeva il «metti» del rifiuto
        /// SUCCESSIVO, perche' il rilevatore leggeva sempre la riga dopo.
        /// Corretto il rilevatore (si ferma a fine messaggio), il conto vero e'
        /// 75: quindici «strade» erano in prestito dal vicino. Misurato con
        /// una replica del rilevatore su HEAD e sull'albero: 75 e 75.
        /// *E' la storia del 66 e dell'83 al contrario: un rilevatore piu'
        /// onesto abbassa il numero, e va scritto per la stessa ragione.*
        ///
        /// **76 dal 26/9/2026 (libreria 0.121.0):** il rifiuto nuovo di
        /// `set_dimension` sullo smusso o il raccordo che non ci sta piu' dice
        /// la strada («cambia o togli prima lo smusso o il raccordo, poi la
        /// quota»).
        ///
        /// **77 dal 26/9/2026 (libreria 0.122.0):** `edge_flange` rifiuta
        /// una flangia senza niente di dritto dicendo fin dove si puo' scendere.
        /// **78, stessa libreria:** `hem` rifiuta un aperto senza gioco dicendo
        /// che senza gioco si chiama «closed».
        /// **79 dalla libreria 0.123.0:** `sketched_bend` rifiuta i 180 gradi
        /// nominando «hem».
        /// **81 dalla libreria 0.124.0:** `hole_table` dice su quale
        /// vista va (V2) e cosa fare della doppia quotatura; `drawing_dimension`
        /// nomina la tabella fori fra le cose gia' quotate.
        /// **89, stessa libreria:** le tre annotazioni (rugosita', datum,
        /// tolleranza geometrica) dicono cosa fare per ogni rifiuto — la lettera
        /// libera, togliere il riferimento, mettere il datum prima.
        /// **90, libreria 0.125.0:** `flat_pattern_view` su un pezzo che non e' di
        /// lamiera dice che non c'e' uno sviluppo da mettere in tavola. **93:**
        /// `drawing_weld_symbol` dice su quale vista provare quando il giunto non
        /// si vede, e toglie da se' il simbolo nato sbagliato (o dice di toglierlo).
        /// **94:** la distinta in tavola senza posto dice di nascondere colonne o
        /// di togliere x/y e lasciar scegliere il posto. **98:** le lettere di
        /// sezioni e dettagli («Usa un'altra lettera»), le croci (i raccordi:
        /// «Chiedi fori»; un foro senza croce: l'opzione del documento) e
        /// `dimension_tolerance` («Scegli un grado fra 5 e 11», l'esempio della
        /// bilaterale). **100:** dalla revisione avversariale — la quota angolare
        /// («Scegli una quota lineare») e la quota non mostrata («mostra la quota»).
        /// **106:** GT9 — la distinta base e la bollatura dicono cosa mettere prima
        /// (la vista di un assieme, la distinta prima della bollatura) e cosa usare
        /// per un saldato. **108:** Z-D1/Z-D6 — un documento che non e' ne' pezzo
        /// ne' assieme («Aprimi il pezzo o l'assieme»), lo STL di un assieme («usa
        /// export_step»), la rilettura dello STEP d'assieme («toglilo e richiedimelo»).
        /// **109:** Z-D5 — `fix_component` fuori da un assieme («Aprimi l'assieme»).
        /// **116:** Z-F1 — `global_variable` e `link_dimension`: il nome fatto male (con
        /// gli esempi), valore o espressione, il nome gia' preso («usa parete»), la
        /// variabile che non esiste («creala con global_variable»), il pezzo da aprire.
        /// **119:** Z-F3 — `reorder_feature`: lo spostamento impossibile («spostala
        /// rispetto a una lavorazione che viene dopo»), i nomi mancanti, il pezzo.
        /// **122:** Z-F5 — `sketch_picture`: il PDF («convertilo in PNG»),
        /// il file che non e' un'immagine («PNG, JPG…»), l'immagine da risalvare.
        /// **125:** GA11 — `mirror_component`: il modo che manca (spiega i tre), fuori
        /// da un assieme («c'e' specchia»), il file che c'e' gia' («cambia suffisso»).
        /// **128:** Z-F4 — `edit_extrusion`: la lavorazione che non e' un'estrusione
        /// («c'e' set_dimension»), «through_all» su un'estrusione («dimmi depth_mm»),
        /// passante o profondita'.
        /// **145 dalla voce Z-C21.2 (28/9/2026, ramo z-c21-2-difetti-silenziosi):**
        /// i difetti silenziosi diventati rifiuti dicono la strada — la quota di
        /// sola lettura («togli prima la sola lettura»), la guidata («cambia
        /// invece la quota che la guida»), edit_extrusion a due versi
        /// («cambiala a mano, oppure… set_dimension»), concentrico e tangente
        /// (coincidente, parallelo… con le facce piane), la griglia in
        /// suppress_instance («fai la ripetizione con una direzione sola»),
        /// l'ancora su una faccia curva («togli ancora e dai x_mm e y_mm») e
        /// sullo spigolo che non si lascia guardare («ancoralo a un altro
        /// spigolo»), il foro fuori dal pezzo. Nove in piu' secondo il
        /// rilevatore: il numero l'ha dato il controllo, l'elenco qui sopra e'
        /// quello dei messaggi nuovi e non una corrispondenza uno a uno.
        /// **146, stessa voce, dopo la revisione:** meno due — i rifiuti intermedi di
        /// `hole_wizard` (prima del posizionamento) non ci sono piu', e contavano
        /// solo perche' «TogliIlTaglio» contiene «togli» — e piu' tre: in
        /// `combine_bodies` uid e `_ref` dati insieme («togli uno dei due»), e il
        /// passo di `main_body_ref` che lascia piu' corpi («nominalo con
        /// «main_body_uid»»).
        /// **213 dalle quattro lavorazioni della guida (28/9/2026, ramo
        /// quattro-lavorazioni):** +67, quanto ha contato il controllo sull'albero
        /// unito, e torna con la somma delle repliche dei quattro scrittori file per
        /// file. **+15, voce GP7 (`fillet_face`):** la stessa faccia due volte e
        /// le facce parallele indicano «fillet_edges»; il raccordo che non nasce,
        /// che nasce di un altro tipo, che rompe qualcosa o che non cambia il volume
        /// dice di provare un raggio piu' piccolo o «fillet_edges». **+18, voce
        /// W1.1 (`fill_pattern`):** la direzione che manca («indica uno spigolo
        /// dritto… oppure… create_axis»), la faccia non piana, la direzione fuori dalla
        /// faccia, il passo e le copie per giro, e il NULL che nomina linear_pattern /
        /// circular_pattern. **+10, voce W1.2 (`rib`):** l'estremo fuori dal
        /// piano, la linea troppo corta, la nervatura che non nasce in nessun verso
        /// («extrude_on_face»), il piano da dire. **+24, voce GP20 (`gusset`):**
        /// i rifiuti nuovi nominano l'alternativa — indica le facce, usa il
        /// triangolare, togli la posizione, prova lati piu' corti,
        /// «weld_bead».
        /// **216 dalla correzione della revisione (stesso ramo):** +3. Il raccordo
        /// di faccia che toglie un volume diverso dal conto a mano nomina
        /// «fillet_edges»; la nervatura che, fallendo, lascia nell'albero una
        /// lavorazione che non e' una «Rib» nomina «extrude_on_face»; il
        /// fazzoletto nato di un tipo diverso da «Gusset» nomina lati piu' corti e
        /// «weld_bead».
        /// **217, Z-C21.2-ter:** +1, and it is the detector, not a better message:
        /// the new refusal of «mirror_component» for a mirror born with the wrong
        /// orientation ends with the call that removes the mirror, and the name of
        /// that helper holds a remedy word, like the other refusals of that file
        /// that remove it.
        /// **230, Z-C21.8 and Z-C21.9 (30/9/2026, branch z-c21-8-9): +13, and most
        /// of it is the detector.** The refusals are being rewritten in English
        /// (W3.1-bis) and the list of remedy words below was Italian only: a
        /// refusal that named its remedy in Italian counted, the same refusal
        /// saying «remove it» did not. On this branch that took the count DOWN to 212
        /// with no message made worse. The English forms added below are the
        /// imperatives the new messages actually use, picked by reading them one
        /// by one; «check that» was tried and left out because it caught «I
        /// cannot check that», which is not a remedy. Measured with a replica of
        /// this detector: with the English words, HEAD (0.144.0) counts 223 and
        /// this branch 230. So +6 is the detector learning the English that
        /// earlier lots had already written, and +7 is this branch: the refusals
        /// that now say «ask again», «remove it», «try again», «give in
        /// «axis_uid»», «write one of the four», «undo the step».
        /// **232, Z-C21.4 and Z-C21.5 (branch z-c21-4-5): +2.** New refusals of
        /// this branch that say what to do, such as the configuration check of
        /// «insert_component» («insert it without a configuration name, or
        /// open the file again and retry»).
        /// **234, Z-C21.11 (branch z-c21-11): +2.** New refusals of lightweight
        /// components that say what to do (the mass: «Resolve them in SOLIDWORKS,
        /// or ask me again with ...»; the BOM's lightweight sub-assembly and the
        /// box's lightweight part without solid bodies: «Resolve it and ask
        /// again»), less the old mass refusal «nothing to weigh», whose remedy
        /// moved to the first of them.
        /// **236, W3.1-bis.2 (30/9/2026, branch w31bis-2): +2, and it is the
        /// detector.** The list is bilingual now: every Italian remedy word has
        /// the English forms that translate it, so that a refusal keeps counting
        /// when it is translated (W3.1-bis.8). The two refusals it adds are
        /// English and do name their remedy: «ask for the mate once more»
        /// (MateOperations.cs) and «Pass ... to import them all, or ...»
        /// (DrawingOperations.cs). On the Italian refusals nothing moved: 209
        /// with the Italian words alone and 209 with both lists, and on the tree
        /// of 25/9/2026 (library 0.117.0, still Italian) 75 and 75 of 578, the
        /// same percentage. A check below keeps it so.
        /// **238, merge of W3.1-bis.2 with Z-C14 (1/10/2026): +2.** Z-C14 added two
        /// refusals of `mate` (a face not found again after the test of the
        /// directions) that named no remedy: the share net of W3.1-bis.2 caught
        /// them on the merge (236 of 937 under 236 of 935), and both now end with
        /// «ask for the mate once more».
        /// </summary>
        private const int Pavimento = 238;

        /// <summary>
        /// THE SHARE OF REFUSALS THAT SAY WHAT TO DO may not go down: today's
        /// share, kept as the fraction it is (W3.1-bis.2, 30/9/2026: 236 of
        /// 935, that is 25.2%) and compared exactly, so that ONE new mute
        /// refusal is enough to turn it red (in thousandths it was not).
        ///
        /// The header of this file always said that a new refusal written mute
        /// lowers the percentage and turns the check red. The code checked only
        /// the absolute count, which a new mute refusal does not lower: the
        /// promise was a sentence, not a check. This floor is the check. It is
        /// raised by hand, like the one above, when the share goes up.
        /// </summary>
        private const int ShareFloorWithRemedy = 238;   // 1/10/2026, merge with Z-C14: 238 of 937
        private const int ShareFloorWithText = 937;

        /// <summary>
        /// Le aperture con cui questo codice dice no.
        /// </summary>
        private static readonly string[] Aperture = { "Fallito(", "Rifiuta(", "NonPosso(" };

        /// <summary>
        /// One remedy word in Italian, the English forms that translate it, and
        /// an English refusal that says the same thing (its own forms must
        /// catch it).
        /// </summary>
        private sealed class Remedy
        {
            internal readonly string Italian;
            internal readonly string[] English;
            internal readonly string Sample;

            internal Remedy(string italian, string[] english, string sample)
            {
                Italian = italian;
                English = english;
                Sample = sample;
            }
        }

        /// <summary>
        /// Le parole con cui un messaggio indica una strada. Elenco volutamente
        /// largo: vedi il commento in testa.
        ///
        /// BILINGUAL SINCE W3.1-bis.2 (30/9/2026). The refusals are being
        /// translated message by message (W3.1-bis.8), and a refusal that loses
        /// its remedy word in translation drops out of the count although it
        /// still says what to do: the floor would go red for the wrong reason,
        /// or be lowered and stop protecting. So every Italian word has its
        /// English partners, picked as the translation of that word in the
        /// messages that use it. The Italian words match anywhere, as they
        /// always did (the count on the Italian messages must not move); the
        /// English ones only at the start of a word, because «use » is inside
        /// «because », and inside Italian words too. The Italian column goes
        /// away at the tag, with the rest of the transition.
        /// </summary>
        private static readonly Remedy[] Remedies =
        {
            new Remedy("usa ", new[] { "use «", "use the ", "use a ", "use an ", "use it", "use them", "use one " },
                       "Use the hole wizard for a threaded hole."),
            new Remedy("usa«", new[] { "use «" }, "Use «slot» instead."),
            new Remedy("metti ", new[] { "put the ", "put it", "put a ", "set it", "set the ", "set «" },
                       "Put the sketch on a plane first."),
            new Remedy("chiedi ", new[] { "ask for ", "ask me ", "ask again", "ask me again" },
                       "Read the assembly again and ask for the mate once more."),
            new Remedy("aggiungi ", new[] { "add a ", "add the ", "add it", "add «", "add one" },
                       "Add a sketch before the cut."),
            new Remedy("prova ", new[] { "try again", "try a ", "try the ", "try it", "try with ", "try «" },
                       "Try a smaller radius."),
            new Remedy("togli", new[] { "remove it", "remove the ", "remove «", "remove them", "remove one", "delete it", "delete the " },
                       "Remove «position» and the middle is used."),
            new Remedy("salvalo", new[] { "save it", "save the " }, "The part was never saved: save it first."),
            new Remedy("scriv", new[] { "write one of", "write it", "write the ", "write «", "is written", "are written" },
                       "Write the size as 40x40x3."),
            new Remedy("riattiva", new[] { "reactivate", "activate it", "turn it back on" },
                       "Reactivate the configuration and call me again."),
            new Remedy("si fa con", new[] { "is done with", "is made with", "can be done with" },
                       "A threaded hole is done with the hole wizard."),
            new Remedy("passa ", new[] { "pass «", "pass the ", "switch to " },
                       "Pass «export_anyway»: true if you know what you are doing."),
            new Remedy("indica ", new[] { "point to ", "point at ", "give «", "give in «", "name the " },
                       "Point to an edge of the part."),
            new Remedy("per farlo", new[] { "to do it", "to do that", "to do so" },
                       "To do that, the drawing must be open."),
            new Remedy("se vuoi", new[] { "if you want" }, "If you want a deeper cut, give the depth."),
            new Remedy("chiamala", new[] { "call it" }, "Call it again with the name of the view."),
            new Remedy("rifai", new[] { "redo ", "do it again", "run it again" }, "Redo the sketch on the other face."),
            new Remedy("apri ", new[] { "open the ", "open it", "open «", "open a " }, "Open the assembly first."),
            new Remedy("fai il check-out", new[] { "check it out", "check out " }, "Check it out of the vault first."),
            new Remedy("con «", new[] { "with «" }, "Round it with «fillet_edges»."),
            new Remedy("oppure", new[] { ", or " }, "Tell me the path, or save the part first."),

            // AGGIUNTE IL 23/9/2026, GUARDANDO I MESSAGGI VERI. La prima
            // stesura dell'elenco era scritta a tavolino e contava muto
            // «So fare: filettato, passaggio, lamato, svasato», che **nomina
            // le alternative** ed e' esattamente cio' che la voce chiede.
            //
            // Il numero che si alza qui si alza perche' il RILEVATORE e'
            // migliorato, non i messaggi: va detto, altrimenti il prossimo
            // che legge il pavimento crede che qualcuno abbia lavorato.
            new Remedy("so fare", new[] { "i can do", "i can make", "i know how" },
                       "I can make: tapped, clearance, counterbore, countersink."),
            new Remedy("per esempio", new[] { "for example", "for instance", "e.g." }, "Give a size, for example 40x40x3."),
            new Remedy("dimmi", new[] { "tell me" }, "Tell me how deep the thread is."),
            new Remedy("dimmela", new[] { "tell me" }, "A blind hole wants a depth: tell me."),
            new Remedy("puoi ", new[] { "you can " }, "You can pass the flag that imports them all."),
            new Remedy("ti serve", new[] { "you need" }, "You need a saved part for this."),
            new Remedy("va scritt", new[] { "must be written", "has to be written", "is written" },
                       "The size must be written as 40x40x3."),
            new Remedy("si indica", new[] { "is given", "is indicated" }, "The depth is given in millimetres."),
        };

        /// <summary>
        /// ADDED ON 30/9/2026 (Z-C21.8 and Z-C21.9): English forms that the
        /// refusals rewritten in English actually use and that no Italian word
        /// of the list translates. Imperatives only, with the word that
        /// follows, so that «I removed the view» or «does not give access» do
        /// not count as a remedy.
        /// </summary>
        private static readonly string[] EnglishOnly =
        {
            "show the ", "undo the step", "leave «", "take them back",
        };

        private static readonly string[] ItalianForms = Remedies.Select(r => r.Italian).ToArray();

        private static readonly string[] EnglishForms =
            Remedies.SelectMany(r => r.English).Concat(EnglishOnly).Distinct(StringComparer.Ordinal).ToArray();

        /// <summary>A refusal found in the sources, with the text it can be judged on.</summary>
        private sealed class Refusal
        {
            internal string File;
            internal int Line;
            internal string Text;       // the statement, lower case
            internal string Prose;      // its string literals with a space in them
            internal bool Italian;      // an Italian remedy word is in it
            internal bool English;      // an English remedy form starts a word in it
        }

        /// <summary>What a set of sources says about its refusals.</summary>
        private sealed class Tally
        {
            internal int Points, WithText, Forwarded;
            internal readonly List<Refusal> Refusals = new List<Refusal>();
            internal int WithRemedy { get { return Refusals.Count(r => r.Italian || r.English); } }
            internal int PerMille { get { return WithText == 0 ? 0 : WithRemedy * 1000 / WithText; } }

            internal bool AtLeast(int floorWithRemedy, int floorWithText)
            {
                return ShareAtLeast(WithRemedy, WithText, floorWithRemedy, floorWithText);
            }
        }

        internal static void Tutte()
        {
            Gruppo("i rifiuti che dicono anche cosa fare (Z-G4)");

            var cartella = Path.Combine(Radice(), "Oce.Solid.Agent.Core");
            if (!Directory.Exists(cartella))
            {
                Segna("la cartella della libreria c'e'", false, "manca " + cartella);
                return;
            }

            var sorgenti = Directory.GetFiles(cartella, "*.cs", SearchOption.AllDirectories)
                .Select(f => new KeyValuePair<string, string[]>(Path.GetFileName(f), File.ReadAllLines(f)));
            var tally = Count(sorgenti, ItalianForms, EnglishForms);
            int totale = tally.Points, conTesto = tally.WithText, inoltrano = tally.Forwarded, conStrada = tally.WithRemedy;

            Segna("i punti di rifiuto si contano", totale > 0,
                  totale + " punti: " + conTesto + " con un messaggio proprio, " +
                  inoltrano + " che ne inoltrano uno scritto altrove");

            // IL PAVIMENTO SALE E NON SCENDE.
            Segna("i rifiuti con una strada non sono diminuiti",
                  conStrada >= Pavimento,
                  conStrada + " su " + conTesto + " (" + (conTesto == 0 ? 0 : conStrada * 100 / conTesto) +
                  "%), pavimento " + Pavimento);

            Segna("e il pavimento e' quello vero", conStrada == Pavimento,
                  conStrada == Pavimento
                      ? conStrada + ", as measured"
                      : "sono " + conStrada + ": alza il pavimento a questo numero");

            // DOVE STANNO I MUTI, perche' un numero senza un indirizzo non fa
            // cominciare nessuno.
            var perFile = tally.Refusals.Where(r => !r.Italian && !r.English)
                .GroupBy(r => r.File).ToDictionary(g => g.Key, g => g.Count());
            var peggiori = perFile.OrderByDescending(x => x.Value).Take(5)
                .Select(x => x.Key + " (" + x.Value + ")");
            Segna("si sa da dove cominciare", perFile.Count > 0 || conStrada == totale,
                  perFile.Count == 0 ? "nessun rifiuto muto"
                                     : "i piu' muti: " + string.Join(", ", peggiori));

            Bilingual(tally);
        }

        /// <summary>
        /// The checks of W3.1-bis.2: the share floor, and the two halves of the
        /// word list watching each other through the translation.
        /// </summary>
        private static void Bilingual(Tally tally)
        {
            // THE SHARE, which a new mute refusal lowers.
            Segna("the share of refusals that say what to do has not gone down",
                  tally.AtLeast(ShareFloorWithRemedy, ShareFloorWithText),
                  Share(tally.PerMille) + " (" + tally.WithRemedy + " of " + tally.WithText + "), floor " +
                  ShareFloorWithRemedy + " of " + ShareFloorWithText +
                  (tally.AtLeast(ShareFloorWithRemedy + 1, ShareFloorWithText) ? ": raise the floor to this share" : ""));

            // EVERY ITALIAN WORD HAS ITS ENGLISH, AND THE ENGLISH CATCHES WHAT IT
            // TRANSLATES. Remove an English form and its sample goes mute here.
            var holes = new List<string>();
            foreach (var r in Remedies)
            {
                if (r.English.Length == 0) { holes.Add("«" + r.Italian.Trim() + "» has no English partner"); continue; }
                if (!StartsAWord(r.Sample.ToLowerInvariant(), r.English))
                    holes.Add("the sample of «" + r.Italian.Trim() + "» («" + r.Sample + "») is caught by none of " +
                              string.Join(", ", r.English.Select(e => "«" + e + "»")));
            }
            Segna("every Italian remedy word has English partners, and they catch an English refusal that says the same",
                  holes.Count == 0, holes.Count == 0 ? Remedies.Length + " words, " + EnglishForms.Length + " English forms"
                                                     : string.Join("; ", holes));

            // ON THE ITALIAN MESSAGES THE ENGLISH ADDS NOTHING: the bilingual list,
            // on a tree that is still Italian, gives the same count (TODO,
            // W3.1-bis.2). An English form that is also an Italian word
            // («indicate » is one, measured) would count a mute refusal as a good one.
            var words = ItalianFunctionWords();
            if (words == null)
            {
                Segna("on the Italian refusals the English forms add nothing", false,
                      "Checks/language/lexicon.json has no function_words to tell Italian from English");
                return;
            }
            var italian = tally.Refusals.Where(r => LooksItalian(r.Prose, words)).ToList();
            var borrowed = italian.Where(r => r.English && !r.Italian).ToList();
            Segna("on the Italian refusals the English forms add nothing (the same count as the Italian words alone)",
                  borrowed.Count == 0,
                  borrowed.Count == 0
                      ? italian.Count(r => r.Italian) + " of " + italian.Count + " Italian refusals, with or without the English forms"
                      : borrowed.Count + " Italian refusals counted only by an English form: " +
                        string.Join("; ", borrowed.Take(4).Select(r => r.File + ":" + r.Line + " «" + FirstEnglish(r.Text) + "»")));

            // AND THESE CHECKS, SEEN RED ON FAKE SOURCES.
            SeenRed(words);
        }

        private static void SeenRed(HashSet<string> words)
        {
            const char Q = '"';
            var wrong = new List<string>();

            // a new mute refusal lowers the share under the floor
            var two = new[]
            {
                "return Fallito(" + Q + "The face is not planar. Use the top face instead." + Q + ");",
                "return Fallito(" + Q + "The face is not planar." + Q + ");",
                "return Fallito(errore);",
            };
            var t2 = Count(new[] { new KeyValuePair<string, string[]>("Fake.cs", two) }, ItalianForms, EnglishForms);
            if (!(t2.Points == 3 && t2.Forwarded == 1 && t2.WithText == 2 && t2.WithRemedy == 1 && t2.PerMille == 500))
                wrong.Add("the fake source was counted " + t2.WithRemedy + "/" + t2.WithText + ", " + t2.Forwarded + " forwarded");
            var three = two.Concat(new[] { "return Fallito(" + Q + "The edge is not straight." + Q + ");" }).ToArray();
            var t3 = Count(new[] { new KeyValuePair<string, string[]>("Fake.cs", three) }, ItalianForms, EnglishForms);
            if (!(t3.WithRemedy == t2.WithRemedy && t2.AtLeast(t2.WithRemedy, t2.WithText) &&
                  !t3.AtLeast(t2.WithRemedy, t2.WithText)))
                wrong.Add("a new mute refusal did not take the share under the floor (" + t2.PerMille + " -> " + t3.PerMille + " per mille)");
            // on today's numbers one mute refusal more is enough, one with a remedy more is not
            if (ShareAtLeast(236, 936, 236, 935) || !ShareAtLeast(237, 936, 236, 935) || !ShareAtLeast(236, 935, 236, 935))
                wrong.Add("the exact comparison of the share does not see a single refusal");

            // an English form that is a piece of Italian is caught on an Italian refusal
            var sentence = ItalianSentinel();
            if (sentence == null) wrong.Add("lexicon.json has no exception_sentinels to build an Italian refusal from");
            else
            {
                var piece = sentence.Split(' ').First(w => w.Length >= 5 && w.All(char.IsLetter)) + " ";
                var fake = new[] { "return Fallito(" + Q + sentence + Q + ");" };
                var tf = Count(new[] { new KeyValuePair<string, string[]>("Fake.cs", fake) }, new string[0], new[] { piece });
                bool caught = tf.Refusals.Count == 1 && LooksItalian(tf.Refusals[0].Prose, words) &&
                              tf.Refusals[0].English && !tf.Refusals[0].Italian;
                if (!caught) wrong.Add("an English form «" + piece + "» inside an Italian refusal was not caught");
            }

            // an English form only counts at the start of a word
            if (StartsAWord("the parts that i could not close because ", new[] { "use " }))
                wrong.Add("«use » was taken inside «because »");

            Segna("the counter, the share floor and the language guard turn red on fake sources",
                  wrong.Count == 0, wrong.Count == 0 ? "5 fakes ended as expected" : string.Join("; ", wrong));
        }

        /// <summary>
        /// The refusals of a set of sources. The message goes on over the next
        /// lines, but not past its own end.
        /// </summary>
        private static Tally Count(IEnumerable<KeyValuePair<string, string[]>> files, string[] italian, string[] english)
        {
            var tally = new Tally();
            foreach (var file in files)
            {
                var righe = file.Value;
                for (int i = 0; i < righe.Length; i++)
                {
                    var r = righe[i];
                    var nudo = r.TrimStart();
                    if (nudo.StartsWith("//", StringComparison.Ordinal)) continue;
                    if (!Aperture.Any(a => r.Contains(a))) continue;

                    // IL MESSAGGIO CONTINUA SULLE RIGHE DOPO. Fermarsi alla
                    // prima vorrebbe dire giudicare mute quasi tutte: qui i
                    // messaggi lunghi sono la norma, e sono proprio quelli che
                    // la strada ce l'hanno.
                    //
                    // MA NON OLTRE LA FINE DEL SUO. Fino al 25/9/2026 il ciclo
                    // leggeva sempre almeno la riga dopo, anche quando il
                    // messaggio si chiudeva sulla sua: un rifiuto breve si
                    // prendeva il «metti» del rifiuto successivo, e il
                    // pavimento e' salito di uno senza che nessuno scrivesse un
                    // messaggio migliore.
                    var testo = r;
                    if (!r.Contains(");"))
                    for (int j = i + 1; j < righe.Length && j < i + 12; j++)
                    {
                        if (righe[j].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                        testo += " " + righe[j];
                        if (righe[j].Contains(");")) break;
                    }

                    tally.Points++;

                    // ── TRE SPECIE, NON DUE ──────────────────────────────
                    //
                    // `return Fallito(errore)` non e' un rifiuto muto: e' un
                    // rifiuto che **inoltra** un messaggio scritto altrove.
                    // Giudicarlo qui vorrebbe dire contarlo due volte — una
                    // dove il testo c'e' e una dove passa — e gonfiare il
                    // problema. Un punto senza nemmeno una virgoletta non ha
                    // un messaggio da giudicare.
                    if (testo.IndexOf('"') < 0) { tally.Forwarded++; continue; }

                    tally.WithText++;
                    var basso = testo.ToLowerInvariant();
                    tally.Refusals.Add(new Refusal
                    {
                        File = file.Key,
                        Line = i + 1,
                        Text = basso,
                        Prose = string.Join(" ", Regex.Matches(testo, @"""((?:[^""\\]|\\.)*)""").Cast<Match>()
                                                      .Select(m => m.Groups[1].Value).Where(s => s.IndexOf(' ') >= 0)),
                        Italian = italian.Any(x => basso.Contains(x)),
                        English = StartsAWord(basso, english),
                    });
                }
            }
            return tally;
        }

        /// <summary>True when one of the forms starts a word of the text (the text in lower case).</summary>
        private static bool StartsAWord(string text, IEnumerable<string> forms)
        {
            return forms.Any(f => FirstAt(text, f) >= 0);
        }

        // A form that begins with a letter must begin a word; one that begins
        // with punctuation («, or ») carries its own boundary.
        private static int FirstAt(string text, string form)
        {
            bool word = form.Length > 0 && char.IsLetter(form[0]);
            for (int i = text.IndexOf(form, StringComparison.Ordinal); i >= 0;
                 i = text.IndexOf(form, i + 1, StringComparison.Ordinal))
                if (!word || i == 0 || !char.IsLetter(text[i - 1])) return i;
            return -1;
        }

        private static string FirstEnglish(string text)
        {
            var f = EnglishForms.FirstOrDefault(e => FirstAt(text, e) >= 0);
            return f ?? "?";
        }

        /// <summary>withRemedy / withText is at least floorWithRemedy / floorWithText, compared exactly.</summary>
        private static bool ShareAtLeast(int withRemedy, int withText, int floorWithRemedy, int floorWithText)
        {
            return withText > 0 && (long)withRemedy * floorWithText >= (long)floorWithRemedy * withText;
        }

        private static string Share(int perMille)
        {
            return (perMille / 10) + "." + (perMille % 10) + "%";
        }

        // ── TELLING AN ITALIAN REFUSAL FROM AN ENGLISH ONE ───────────────
        //
        // The Italian function words come from the lexicon of the language
        // gate, so this file carries none, less the words the lexicon itself
        // calls weak because English writes them too. A hyphenated word stays
        // whole («non-planar» is English), and what stands between
        // guillemets, in backticks or in a snake_case or camelCase name is a
        // protocol name, not the language of the sentence. Measured on
        // 30/9/2026: 817 of the 935 refusals with a message read as Italian,
        // and the ones that read so by a single word were checked by eye.

        private static HashSet<string> ItalianFunctionWords()
        {
            var list = LexiconList("function_words");
            if (list == null || list.Count == 0) return null;
            var weak = new HashSet<string>(LexiconList("weak_words") ?? new List<string>(), StringComparer.Ordinal);
            return new HashSet<string>(list.Where(w => !weak.Contains(w)), StringComparer.Ordinal);
        }

        private static string ItalianSentinel()
        {
            var list = LexiconList("exception_sentinels");
            return list == null ? null : list.FirstOrDefault(s => s.IndexOf(' ') > 0);
        }

        private static List<string> LexiconList(string key)
        {
            try
            {
                var file = Path.Combine(Radice(), "Checks", "language", "lexicon.json");
                var json = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                    .DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
                object value;
                if (json == null || !json.TryGetValue(key, out value) || !(value is object[])) return null;
                return ((object[])value).Select(Convert.ToString).ToList();
            }
            catch (Exception) { return null; }
        }

        private static bool LooksItalian(string prose, HashSet<string> functionWords)
        {
            var s = Regex.Replace(prose ?? "", @"«[^»]*»|`[^`]*`", " ");
            s = Regex.Replace(s, @"\b[A-Za-z0-9]+(?:_[A-Za-z0-9]+)+\b|\b[a-z]+[A-Z][A-Za-z0-9]*\b", " ");
            if (Regex.IsMatch(s, "[\u00e0\u00e8\u00e9\u00ec\u00f2\u00f9\u00c0\u00c8\u00c9\u00cc\u00d2\u00d9]")) return true;
            if (Regex.IsMatch(s, @"[A-Za-z][aeiouAEIOU]'(?=\s|$|[.,;:!?)»])")) return true;
            foreach (Match m in Regex.Matches(s, @"[\p{L}'’]+(?:-[\p{L}'’]+)*"))
                if (functionWords.Contains(m.Value.ToLowerInvariant())) return true;
            return false;
        }
    }
}
