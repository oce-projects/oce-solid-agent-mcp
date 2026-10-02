using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LA TAVOLA.
    ///
    /// Fino alla 0.39.0 il catalogo sapeva fare solo pezzi: ventinove
    /// operazioni, tutte su un `.SLDPRT`. Le stringhe «Drawing», «IView» e
    /// «SaveAs» non comparivano in nessun file dell'add-in. Cioe' l'assistente
    /// poteva disegnare un contenitore stagno e non poteva consegnarlo: in
    /// officina non arriva un modello, arriva una tavola quotata.
    ///
    /// Il censimento (RAG 80r) ha contato 278 metodi su `IDrawingDoc` e 481 su
    /// `IView`. Qui ce ne sono sette. La scelta e' voluta: sette operazioni
    /// misurate valgono piu' di settanta dichiarate, ed e' la regola del
    /// catalogo a due livelli (RAG 80j).
    ///
    /// LE TRE TRAPPOLE DI QUESTA FAMIGLIA, tutte della specie che questo
    /// progetto esiste per prendere — nessun errore, risultato sbagliato:
    ///
    /// 1. IL DIEDRO. `Create3rdAngleViews2` e `Create1stAngleViews2` sono
    ///    gemelle, nessuna delle due fallisce, e producono tavole con la vista
    ///    destra e la sinistra SCAMBIATE. L'Italia disegna in primo diedro; una
    ///    tavola in terzo e' leggibile, plausibile, e il pezzo che ne esce e'
    ///    specchiato. Nessuna misura geometrica lo rivela. Qui la terza non e'
    ///    esposta affatto e il primo diedro non e' un parametro: e' una
    ///    costante.
    ///
    /// 2. LA QUOTA CHE MENTE. `CreateLinearDim4` prende un parametro `Val`, e
    ///    `IDisplayDimension.SetText` sovrascrive il numero mostrato: l'API
    ///    permette di scrivere in tavola un numero che NON e' la geometria.
    ///    Qui non c'e' nessuna operazione che quoti a mano: le quote si
    ///    prendono dal modello (`InsertModelAnnotations3`), dove non possono
    ///    mentire.
    ///
    /// 3. IL PDF DI UN FOGLIO SOLO. L'esportazione di serie prende il foglio
    ///    corrente: una tavola da tre fogli esce in officina come una pagina,
    ///    senza errori. `IExportPdfData` ha sette metodi in tutto e due
    ///    servono solo a evitare questo. Qui si contano i fogli prima e le
    ///    PAGINE del PDF dopo.
    /// </summary>
    public partial class OperazioniSw
    {
        /// <summary>
        /// Il numero di preferenza del template di disegno predefinito.
        /// Non si scrive mai un percorso a mano: e' la lezione dei profili di
        /// saldatura. L'officina ha il suo template, con il suo cartiglio e il
        /// suo diedro, e un percorso scritto a mano funziona sulla macchina di
        /// chi l'ha scritto e su nessun'altra.
        /// </summary>
        private const int TemplateDisegnoPredefinito =
            (int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing;

        // ─────────────────────────────────────────────────────────────────
        // create_drawing
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Crea la tavola del modello aperto, e la lascia attiva.
        ///
        /// DUE DOCUMENTI APERTI INSIEME: e' la novita' architetturale, non
        /// l'API. `CreateDrawViewFromModelView3(ModelName, ...)` vuole il
        /// percorso di un documento GIA' APERTO — quindi il modello deve
        /// restare aperto mentre il disegno diventa attivo. Fino a qui
        /// l'agente lavorava su un documento solo e lo stato lo dava per
        /// sottinteso.
        /// </summary>
        private Esito CreaDisegno(IModelDoc2 modello, Dictionary<string, object> a)
        {
            if (modello == null)
                return Fallito("Non c'e' nessun documento aperto da mettere in tavola.");

            string percorsoModello = "";
            try { percorsoModello = modello.GetPathName() ?? ""; } catch { }
            if (string.IsNullOrWhiteSpace(percorsoModello))
                return Fallito(
                    "Il pezzo non e' mai stato salvato, quindi non ha un percorso, e " +
                    "una vista di tavola si aggancia al PERCORSO del modello. " +
                    "Salvalo una prima volta e poi rifammi la tavola.");

            int tipoModello = 0;
            try { tipoModello = modello.GetType(); } catch { }
            if (tipoModello != (int)swDocumentTypes_e.swDocPART &&
                tipoModello != (int)swDocumentTypes_e.swDocASSEMBLY)
                return Fallito(
                    "Il documento attivo non e' ne' un pezzo ne' un assieme: " +
                    "non so di cosa fare la tavola.");

            string template = Str(a, "template", null);
            if (string.IsNullOrWhiteSpace(template))
            {
                try { template = _sw.GetUserPreferenceStringValue(TemplateDisegnoPredefinito); }
                catch { template = null; }
            }
            if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
                return Fallito(
                    "Non trovo il modello di tavola di questa installazione " +
                    (string.IsNullOrWhiteSpace(template)
                        ? "(la preferenza e' vuota)."
                        : "(«" + template + "» non c'e')") +
                    " Senza, la tavola nascerebbe senza cartiglio e con il " +
                    "diedro sbagliato: preferisco fermarmi.");

            // Formato e scala si passano gia' qui: `NewDocument` li accetta, e
            // farlo in due tempi vorrebbe dire un foglio buttato.
            int formato = FormatoFoglio(Str(a, "paper_size", "A3"));
            object nuovo;
            try { nuovo = _sw.NewDocument(template, formato, 0, 0); }
            catch (Exception ex) { return Fallito("Non riesco a creare la tavola: " + ex.Message); }

            var disegno = nuovo as IModelDoc2;
            if (disegno == null)
                return Fallito(
                    "SolidWorks non ha creato nessuna tavola e non ha detto perche'.");

            int tipoNuovo = 0;
            try { tipoNuovo = disegno.GetType(); } catch { }
            if (tipoNuovo != (int)swDocumentTypes_e.swDocDRAWING)
                return Fallito(
                    "Il documento creato non e' una tavola: il modello «" + template +
                    "» non e' un modello di disegno.");

            // IL MODELLO DEVE ESSERE ANCORA APERTO.
            //
            // E' la precondizione di tutto quello che viene dopo, e non e'
            // scontata: creare un documento cambia il documento attivo, e
            // basta una preferenza («chiudi i documenti non usati») perche' il
            // pezzo sparisca sotto i piedi. Se sparisce, le viste nascerebbero
            // vuote senza dare errore.
            if (!DocumentoAperto(percorsoModello))
                return Fallito(
                    "La tavola e' nata ma il pezzo non risulta piu' aperto: le viste " +
                    "verrebbero vuote. Riapri il pezzo e riprova.");

            var misure = new Dictionary<string, object>
            {
                ["template"] = template,
                ["paper_size"] = Str(a, "paper_size", "A3"),
                ["model_in_drawing"] = percorsoModello,
                ["sheets"] = NumeroOppureNull(ContaFogli(disegno)),
                ["documents_open"] = DocumentiAperti().Count,
            };
            _log("[agente] tavola creata da " + template);
            var esito = SenzaFeature(misure);
            // DA QUI IN POI SI LAVORA SULLA TAVOLA, e lo si dice.
            // Chi esegue il piano sorveglia che il documento non cambi sotto i
            // piedi; questo e' l'unico cambio legittimo, quindi va dichiarato
            // invece di subito.
            esito.DocumentoNuovo = disegno;
            return esito;
        }

        // ─────────────────────────────────────────────────────────────────
        // set_sheet
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Formato e scala del foglio attivo. Il diedro NON e' un parametro.
        /// </summary>
        private Esito ImpostaFoglio(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito("Il documento attivo non e' una tavola.");

            ISheet foglio;
            try { foglio = disegno.GetCurrentSheet() as ISheet; }
            catch (Exception ex) { return Fallito("Non riesco a leggere il foglio: " + ex.Message); }
            if (foglio == null) return Fallito("La tavola non ha un foglio attivo.");

            string nomeFormato = Str(a, "paper_size", "A3");
            int formato = FormatoFoglio(nomeFormato);
            double scalaNum = Num(a, "scale_numerator", 1);
            double scalaDen = Num(a, "scale_denominator", 1);
            if (scalaNum <= 0 || scalaDen <= 0)
                return Fallito("La scala deve avere due numeri positivi, per esempio 1:2.");

            // FORMATO E SCALA SI SCRIVONO INSIEME, E IL DIEDRO SI RILEGGE.
            //
            // Il primo tentativo faceva `SetSize` e poi `SetScale`, e la
            // seconda tornava `false` — misurato l'11 settembre 2026 sulla
            // prima tavola vera. La ragione e' la stessa gia' pagata con le
            // facce: cambiare il foglio invalida il puntatore al foglio, e
            // la seconda chiamata lavorava su un oggetto vecchio.
            //
            // `SetProperties2` fa tutto in un colpo, quindi il problema non
            // si pone. Porta pero' un parametro che NON deve essere deciso
            // qui: `FirstAngle`. E' il diedro, cioe' la trappola piu' cara
            // di questa famiglia (una tavola in terzo diedro ha destra e
            // sinistra scambiate, non da' nessun errore, e il pezzo esce
            // specchiato). Si LEGGE quello che il modello di tavola
            // dell'officina ha gia' deciso e si riscrive uguale.
            var prima = ProprietaDelFoglio(foglio);
            bool primoDiedro = prima != null && prima.Length > 4 && prima[4] != 0;

            try
            {
                foglio.SetProperties2(
                    formato,
                    (int)swDwgTemplates_e.swDwgTemplateNone,
                    scalaNum, scalaDen,
                    primoDiedro,
                    0, 0,
                    true);
            }
            catch (Exception ex) { return Fallito("Foglio non impostato: " + ex.Message); }

            // Il puntatore al foglio e' vecchio da questa riga in poi.
            try { foglio = disegno.GetCurrentSheet() as ISheet; }
            catch { foglio = null; }
            if (foglio == null) return Fallito("Ho perso il foglio dopo averlo impostato.");

            // SI RILEGGE. Le due chiamate tornano `true` anche quando il
            // foglio e' rimasto com'era — e' successo con `SetMaterialPropertyName2`,
            // e la difesa e' sempre la stessa: chiedere al documento, non
            // fidarsi della chiamata.
            double[] misureFoglio = MisureFoglio(foglio);
            var scalaDopo = LeggiScala(foglio);
            if (scalaDopo != null && scalaDen != 0 &&
                Math.Abs(scalaDopo[0] / scalaDopo[1] - scalaNum / scalaDen) > 1e-9)
                return Fallito(
                    "Ho chiesto la scala " + scalaNum + ":" + scalaDen +
                    " e il foglio dice " + scalaDopo[0] + ":" + scalaDopo[1] +
                    ". Non dico che e' fatto quando non lo e'.");

            var misure = new Dictionary<string, object>
            {
                ["paper_size"] = nomeFormato,
                ["width_mm"] = misureFoglio != null ? (object)Math.Round(misureFoglio[0], 1) : null,
                ["height_mm"] = misureFoglio != null ? (object)Math.Round(misureFoglio[1], 1) : null,
                ["scale"] = scalaDopo != null ? scalaDopo[0] + ":" + scalaDopo[1] : null,
                ["projection_angle"] = primoDiedro ? "first" : "third",
            };
            // IL DIEDRO SI DICE SEMPRE, anche quando e' giusto. Non e' un
            // avviso: e' l'unico modo perche' qualcuno se ne accorga prima
            // che il pezzo sia tagliato.
            if (!primoDiedro)
                _log("[agente] ATTENZIONE: questa tavola e' in TERZO diedro");
            return SenzaFeature(misure);
        }

        // ─────────────────────────────────────────────────────────────────
        // standard_views
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Le tre viste standard, in PRIMO diedro.
        ///
        /// `Create1stAngleViews2` torna `bool`, non le viste: per sapere cosa
        /// ha fatto bisogna contarle prima e dopo. E' la stessa forma gia'
        /// vista con `InsertFeatureShell` — una chiamata che dice «si'» e non
        /// dice cosa.
        /// </summary>
        private Esito VisteStandard(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null) return Fallito("Il documento attivo non e' una tavola.");

            string percorsoModello = Str(a, "model", null);
            if (string.IsNullOrWhiteSpace(percorsoModello))
            {
                string perche;
                percorsoModello = ModelloDaMettereInVista(out perche);
                if (percorsoModello == null) return Fallito(perche);
            }
            var percorsoSw = PercorsoCanonico(percorsoModello);
            if (percorsoSw == null)
                return Fallito(
                    "«" + percorsoModello + "» non risulta aperto. Le viste " +
                    "nascerebbero vuote senza dare errore.");

            int prima = ContaViste(disegno);
            if (prima < 0)
                return Fallito("Non riesco a contare le viste della tavola prima di cominciare. Non ho toccato niente.");
            // The views that were already on the sheet: the centre marks the bridge
            // adds below go only on the views this call makes (Z-C21.13 review).
            // Null when one name cannot be read: then no view is known to be new,
            // and the bridge marks none.
            var viewsBefore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var sheetBefore = disegno.GetFirstView() as IView;   // the sheet itself
                var old = sheetBefore == null ? null : sheetBefore.GetNextView() as IView;
                while (old != null)
                {
                    string oldName = old.GetName2();
                    if (oldName == null) { viewsBefore = null; break; }
                    viewsBefore.Add(oldName);
                    old = old.GetNextView() as IView;
                }
            }
            catch { viewsBefore = null; }
            bool ok;
            try { ok = disegno.Create1stAngleViews2(percorsoSw); }
            catch (Exception ex) { return Fallito("Viste non create: " + ex.Message); }

            int dopo = ContaViste(disegno);
            if (dopo < 0)
                return Fallito(
                    "Ho chiesto le viste, ma poi non riesco a ricontarle: non so dirti se sono " +
                    "nate. Guarda la tavola prima di andare avanti.");
            if (!ok && dopo == prima)
                return Fallito(
                    "SolidWorks non ha creato nessuna vista e non ha detto perche'. " +
                    "Di solito vuol dire che il modello non e' ricostruito.");
            if (dopo <= prima)
                return Fallito(
                    "La chiamata e' andata a buon fine ma le viste sono sempre " + prima +
                    ": la tavola e' vuota. Non lo chiamo riuscito.");

            // THE DRAWING IS REBUILT BEFORE ANSWERING (29/9/2026). The centre marks
            // SOLIDWORKS puts on holes when it creates a view (document option
            // swDetailingAutoInsertCenterMarksForHoles) were counted 3 in three full
            // bench runs and alone, and 0 once in the full run on 0.142.0
            // (center_marks_on_the_holes, step 7), with no change to this code or
            // to the counter. The state read right after the views were made may
            // come before the views are complete; a rebuild settles them, and the
            // answer now carries the count, so a second zero comes with its number.
            try { ((IModelDoc2)disegno).EditRebuild3(); } catch { }
            // Counted view by view. A view whose marks cannot be counted makes the
            // total unknown (null), not smaller: up to library 0.143.0 such a view
            // was skipped, and the total still looked like a count.
            var views = new List<IView>();
            try
            {
                var sheet = disegno.GetFirstView() as IView;          // the sheet itself
                var next = sheet == null ? null : sheet.GetNextView() as IView;
                while (next != null)
                {
                    views.Add(next);
                    next = next.GetNextView() as IView;
                }
            }
            catch { views = null; }
            // ZERO-E-ZERO: the sum starts at zero and every counted view adds its
            // own count; views that cannot be walked, or one view that cannot be
            // counted, turn it to -1, which the answer gives as null.
            int centreMarks = views == null ? -1 : 0;
            var viewsWithoutMarks = new List<IView>();
            if (views != null)
            {
                foreach (IView view in views)
                {
                    int n = ContaCroci.DellaVista(view);
                    if (n < 0) { centreMarks = -1; continue; }
                    if (centreMarks >= 0) centreMarks += n;
                    if (n == 0) viewsWithoutMarks.Add(view);
                }
            }

            // THE BRIDGE MARKS THE HOLES SOLIDWORKS LEFT BARE (Z-C21.13). A view
            // that shows holes end-on and has no centre mark at all after the
            // rebuild gets them from the walk the centre-mark operation uses
            // (MarkHolesOfView, OperazioniCentrini.cs), and the answer says how
            // many the bridge added. The online search of 29/9/2026 (RAG note
            // 82zz-decies) found no documented fix, only conditions that suppress
            // the automatic marks in silence: draft quality views ("Some edges may
            // be missing, since not all model information is loaded in Draft
            // quality"), large assembly mode. Only views with ZERO marks: where
            // SOLIDWORKS put some, the view is left as it is. What the operation
            // measured on 26/9/2026 still holds: once the automatic marks were
            // deleted, AddCenterMark put none back; so a hole may stay bare, and
            // then the answer says it. A view with no hole seen end-on (the front
            // and side views of a plate) is walked and left untouched.
            // ZERO-E-ZERO: the three counters below count what the walk found and
            // did; with no view without marks, or no hole in them, zero is real.
            int addedByBridge = 0;
            int holesInViewsWithoutMarks = 0;
            int holesLeftWithoutMark = 0;
            object autoMarksOption = null;
            if (viewsWithoutMarks.Count > 0)
            {
                IMathUtility mu = null;
                try { mu = _sw.GetMathUtility() as IMathUtility; } catch { }
                var selMgr = modello.SelectionManager as ISelectionMgr;
                double size = DocumentCentreMarkSize(modello, Unita.M(DefaultCentreMarkSizeMm));
                foreach (IView view in viewsWithoutMarks)
                {
                    // ONLY THE VIEWS THIS CALL MADE (Z-C21.13 review): a view that was
                    // on the sheet before may be bare on purpose, and it is not ours
                    // to mark. A view whose name cannot be read is not taken for new.
                    string viewName = null;
                    try { viewName = view.GetName2(); } catch { }
                    if (viewName == null || viewsBefore == null || viewsBefore.Contains(viewName)) continue;
                    var marks = MarkHolesOfView(modello, disegno, view, size, false, mu, selMgr);
                    if (marks.Before < 0) continue;   // not counted this time: nothing was touched
                    holesInViewsWithoutMarks += marks.Holes;
                    addedByBridge += marks.Added;
                    holesLeftWithoutMark += marks.LeftWithoutMark.Count;
                }
                if (addedByBridge > 0) { try { modello.EditRebuild3(); } catch { } }
                // The document option that makes SOLIDWORKS put the marks, read only
                // when it did not (DP_Detailing.htm of the swconst guide: the toggle
                // with swDetailingNoOptionSpecified, one for parts and one for
                // assemblies): a template with the option off explains a zero that
                // no suppression does.
                if (holesInViewsWithoutMarks > 0)
                {
                    bool assembly = percorsoSw.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase);
                    try
                    {
                        autoMarksOption = modello.Extension.GetUserPreferenceToggle(
                            (int)(assembly ? swUserPreferenceToggle_e.swDetailingAutoInsertCenterMarksForHolesAsm
                                           : swUserPreferenceToggle_e.swDetailingAutoInsertCenterMarksForHoles),
                            (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified);
                    }
                    catch { }
                }
            }

            var misure = new Dictionary<string, object>
            {
                ["projection_angle"] = "first",
                ["model"] = percorsoSw,
                ["views_before"] = prima,
                ["views_after"] = dopo,
                ["views_created"] = dopo - prima,
                ["names"] = NomiDelleViste(disegno),
                // The marks SOLIDWORKS put, after the rebuild and before the bridge.
                ["centre_marks_after_rebuild"] = centreMarks >= 0 ? (object)centreMarks : null,
                ["centre_marks_added_by_bridge"] = addedByBridge,
            };
            if (holesInViewsWithoutMarks > 0)
            {
                misure["holes_in_views_without_centre_marks"] = holesInViewsWithoutMarks;
                misure["auto_centre_marks_option"] = autoMarksOption;
            }
            if (holesLeftWithoutMark > 0)
            {
                misure["holes_left_without_centre_mark"] = holesLeftWithoutMark;
                string option = "the document option that inserts centre marks on holes when a view is created " +
                                "(Document Properties, Detailing)";
                misure["centre_marks_note"] =
                    "SOLIDWORKS put no centre mark on the " + holesInViewsWithoutMarks +
                    (holesInViewsWithoutMarks == 1 ? " hole" : " holes") + " seen end-on in views without marks, and the bridge " +
                    "added " + addedByBridge + ": on the other " + holesLeftWithoutMark + " AddCenterMark added nothing (measured " +
                    "on 26/9/2026: once the automatic marks are deleted it puts none back). " +
                    (autoMarksOption is bool optionOn
                        ? (optionOn ? "The option that inserts them is on, so something suppressed them in silence (the documented " +
                                      "cases: a draft quality view, large assembly mode): make the views again."
                              : "Turn on " + option + " and make the views again.")
                        : "Check " + option + ", which could not be read, and make the views again.");
            }
            return SenzaFeature(misure);
        }

        // ─────────────────────────────────────────────────────────────────
        // model_view (isometrica e le altre viste con nome)
        // ─────────────────────────────────────────────────────────────────

        private Esito VistaModello(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null) return Fallito("Il documento attivo non e' una tavola.");

            string percorsoModello = Str(a, "model", null);
            if (string.IsNullOrWhiteSpace(percorsoModello))
            {
                string perche;
                percorsoModello = ModelloDaMettereInVista(out perche);
                if (percorsoModello == null) return Fallito(perche);
            }
            var percorsoSw = PercorsoCanonico(percorsoModello);
            if (percorsoSw == null)
                return Fallito("«" + percorsoModello + "» non risulta aperto.");

            // I NOMI DELLE VISTE SONO QUELLI DI SOLIDWORKS, NON QUELLI DEL
            // PROGETTISTA. Si accetta l'italiano e si traduce: chiedere a chi
            // scrive il piano di ricordarsi «*Isometric» con l'asterisco e'
            // un modo di far fallire il piano per una stella.
            string chiesta = (Str(a, "view", "isometric") ?? "").Trim().ToLowerInvariant();
            var nomiSw = NomiVistaSw(chiesta);
            if (nomiSw == null)
                return Fallito(
                    "Non conosco la vista «" + chiesta + "». Ne conosco nove: " +
                    "isometrica, dimetrica, trimetrica, fronte, dietro, sopra, " +
                    "sotto, destra, sinistra.");

            double x = Unita.M(Num(a, "x_mm", 0));
            double y = Unita.M(Num(a, "y_mm", 0));

            // IL NOME DELLA VISTA NON SI INDOVINA: SI PROVA.
            //
            // Una vista con nome si chiede per NOME, e il nome dipende
            // dalla lingua dell'installazione: su SolidWorks italiano la
            // vista isometrica si chiama «*Isometrica», su quello inglese
            // «*Isometric». La chiamata non fallisce con un messaggio: torna
            // `null` e basta, e da fuori sembra che la tavola non voglia
            // quella vista.
            //
            // E' la stessa cosa gia' pagata con `ShowNamedView2`, dove ne'
            // l'italiano ne' l'inglese funzionavano e serviva il numero. Qui
            // il numero non c'e', quindi si provano le scritture e si
            // DICHIARA quale ha funzionato: cosi' la prossima volta il
            // fatto e' scritto invece che ricordato.
            // PRIMA SI CHIEDE AL MODELLO COME LE CHIAMA LUI.
            //
            // `IModelDoc2.GetModelViewNames()` restituisce i nomi veri delle
            // viste di QUESTO documento su QUESTA installazione. Provare una
            // lista scritta a mano funziona finche' qualcuno non apre il
            // programma in un'altra lingua; chiedere funziona sempre, e
            // quando non trova niente puo' dire cosa c'era davvero — che e'
            // la differenza fra un errore utile e «non ha funzionato».
            var suoi = NomiDelleVisteDelModello(percorsoSw);
            var daProvare = new List<string>();
            // Si cerca la RADICE dentro il nome vero, non il nome intero:
            // «isometri» prende «*Isometrico», «*Isometrica» e «*Isometric»
            // senza doverli conoscere tutti e tre.
            foreach (var radice in nomiSw)
            {
                foreach (var n in suoi)
                {
                    if (n.IndexOf(radice, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!daProvare.Contains(n)) daProvare.Add(n);
                }
            }
            // Se il modello non dichiara niente — puo' succedere su un
            // documento appena aperto — restano le scritture note.
            foreach (var noto in NomiNotiPerRadice(nomiSw))
                if (!daProvare.Contains(noto)) daProvare.Add(noto);

            int prima = ContaViste(disegno);
            if (prima < 0)
                return Fallito("Non riesco a contare le viste della tavola prima di cominciare. Non ho toccato niente.");
            IView vista = null;
            string nomeSw = null;
            var rifiutati = new List<string>();
            foreach (var candidato in daProvare)
            {
                try { vista = disegno.CreateDrawViewFromModelView3(percorsoSw, candidato, x, y, 0); }
                catch { vista = null; }
                if (vista != null) { nomeSw = candidato; break; }
                rifiutati.Add(candidato);
            }

            int dopo = ContaViste(disegno);
            // Con un conteggio illeggibile resta la vista restituita (voce Z-C8).
            if (vista == null || (dopo >= 0 && dopo <= prima))
                return Fallito(
                    "SolidWorks non ha creato la vista «" + chiesta + "». Ho provato " +
                    "a chiamarla: " + string.Join(", ", rifiutati) + ". " +
                    (suoi.Count > 0
                        ? "Il pezzo dice di avere queste viste: " + string.Join(", ", suoi) + "."
                        : "Il pezzo non dichiara nessuna vista con nome."));

            string nome = "";
            double scala = 0;
            try { nome = vista.Name; } catch { }
            try { scala = vista.ScaleDecimal; } catch { }

            var misure = new Dictionary<string, object>
            {
                ["view"] = chiesta,
                ["sw_name"] = nomeSw,
                ["names_rejected"] = rifiutati.Count > 0 ? (object)string.Join(",", rifiutati) : null,
                ["name"] = nome,
                ["scale"] = scala > 0 ? (object)Math.Round(scala, 4) : null,
                ["views_after"] = dopo,
            };
            return SenzaFeature(misure);
        }

        // ─────────────────────────────────────────────────────────────────
        // model_dimensions
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Porta in tavola le quote che il modello ha gia'.
        ///
        /// E' l'unica via di quotatura esposta, e la ragione e' di sicurezza,
        /// non di comodita': una quota inserita a mano puo' portare un numero
        /// diverso dalla geometria (`Val`, e `SetText` che sovrascrive il testo
        /// mostrato). Una quota che viene dal modello no.
        ///
        /// PERCHE' QUI C'E' UNA TABELLA DI TENTATIVI E NON UNA CHIAMATA.
        /// Misurato l'11 settembre 2026 sulla prima tavola vera: un pezzo con
        /// cinque quote, tre viste standard, e `InsertModelAnnotations3` che
        /// non porta niente — senza errore, restituendo un vettore vuoto, cioe'
        /// esattamente cio' che restituisce quando ha funzionato e non c'era
        /// nulla da portare.
        ///
        /// Le cose che possono fare la differenza sono tre, e la
        /// documentazione le tratta tutte da dettaglio:
        ///
        ///   - QUALI BIT. `swInsertDimensions` (8) e' il bit generico; le quote
        ///     sono pero' divise in MARCATE per il disegno (32768) e NON
        ///     marcate (524288), e una lavorazione fatta via API non e'
        ///     marcata. Chiederli tutti insieme non e' la stessa cosa che
        ///     chiederli uno per volta.
        ///   - DOVE PIAZZARLE. `UsePlacementInSketch` prende la posizione
        ///     dallo schizzo del modello; se lo schizzo non e' quotato non c'e'
        ///     nessuna posizione da prendere.
        ///   - QUALE VERSIONE. La 2 ha un parametro che la 3 ha perso —
        ///     `AllTypes`, «portale tutte, non guardare la maschera» — e la
        ///     regola «la piu' alta e' la migliore» in questo interop e' gia'
        ///     dimostrata falsa (`InsertEndCapFeature3` aveva TOLTO le facce
        ///     rispetto alla 2).
        ///
        /// Quindi si prova, in ordine dal piu' preciso al piu' grossolano, e
        /// si scrive nelle misure quale strada ha funzionato. Non e' eleganza:
        /// e' l'unico modo di trasformare «non ha funzionato» in un fatto.
        /// </summary>
        private Esito QuoteDalModello(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null) return Fallito("Il documento attivo non e' una tavola.");

            int viste = ContaViste(disegno);
            if (viste < 0)
                return Fallito("Non riesco a contare le viste della tavola. Non ho toccato niente.");
            if (viste == 0)
                return Fallito(
                    "La tavola non ha viste: le quote non avrebbero dove attaccarsi. " +
                    "Metti prima le viste.");

            bool tutte = Bool(a, "all_dimensions", false);
            int extra = Bool(a, "hole_callouts", true)
                ? (int)swInsertAnnotation_e.swInsertholeCallout
                : 0;

            // Di serie si prendono solo le quote MARCATE per il disegno: sono
            // quelle che un progettista ha scelto di mostrare, e una tavola con
            // tutte le quote del modello e' illeggibile.
            // The masks, and every fallback below, come from the ModelImport*
            // members after this method (Z-C21.3, findings M2 and N9): with
            // all_dimensions false no step may carry the NOT-MARKED bit or
            // ignore the mask. Prove.Contratti (Zc212DrawingContract) reads the
            // method from the first line of this comment to its closing brace.
            var maschere = ModelImportMasks(tutte);

            int prima = ContaQuote(disegno);
            if (prima < 0)
                return Fallito(
                    "Non riesco a contare le quote della tavola prima di cominciare: senza quel " +
                    "numero non saprei dirti quali sono arrivate. Non ho toccato niente.");
            int dopo = prima;
            string comeFatto = null;
            int restituite = 0;
            var provati = new List<string>();

            foreach (var maschera in maschere)
            {
                int tipi = maschera | extra;

                foreach (var dalloSchizzo in new[] { false, true })
                {
                    try
                    {
                        var tornate = disegno.InsertModelAnnotations3(
                            (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel,
                            tipi,
                            true,    // tutte le viste
                            ModelImportV3DuplicateDims, // TRUE eliminates duplicates (M2, see the constant)
                            false,   // HiddenFeatureDims
                            dalloSchizzo) as object[];
                        // ZERO-E-ZERO: e' solo quante ne ha restituite l'API; la misura
                        // vera e' `ContaQuote` prima e dopo, e l'eccezione ha il suo ramo.
                        restituite = tornate == null ? 0 : tornate.Length;
                    }
                    catch (Exception ex) { return Fallito("Quote non inserite: " + ex.Message); }

                    provati.Add("v3/" + maschera + (dalloSchizzo ? "/schizzo" : ""));
                    dopo = ContaQuote(disegno);
                    if (dopo < 0) return Fallito("Ho chiesto le quote del modello, ma poi non riesco a ricontare le quote della tavola: non so dirti se sono arrivate. Guarda la tavola prima di andare avanti.");
                    if (dopo > prima)
                    {
                        comeFatto = "InsertModelAnnotations3, bit " + maschera +
                                    (dalloSchizzo ? ", posizione dallo schizzo" : "");
                        break;
                    }
                }
                if (dopo > prima) break;

                // La versione 2, con `AllTypes`: il parametro che la 3 non ha.
                // AllTypes TRUE ignores the mask, so it is TRUE only when every
                // dimension was asked for (N9, see ModelImportAllTypes).
                // duplicateDims stays FALSE here on purpose: the page of THIS
                // version says the opposite of the page of the 3
                // (estratto_obsoleteapi\DrawingDoc\DrawingDoc__InsertModelAnnotations2.htm:
                // «duplicateDims TRUE to insert duplicate dimensions, FALSE to
                // eliminate duplicate dimensions»), and the probe of M2 measured
                // the 3 only.
                bool allTypes = ModelImportAllTypes(tutte);
                try
                {
                    if (disegno.InsertModelAnnotations2(
                            (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel,
                            allTypes, tipi, true, false, false))
                    {
                        provati.Add("v2/" + maschera);
                        dopo = ContaQuote(disegno);
                        if (dopo < 0) return Fallito("Ho chiesto le quote del modello, ma poi non riesco a ricontare le quote della tavola: non so dirti se sono arrivate. Guarda la tavola prima di andare avanti.");
                        if (dopo > prima)
                        {
                            comeFatto = "InsertModelAnnotations2" +
                                        (allTypes ? " with AllTypes" : " with the types mask") +
                                        ", bit " + maschera;
                            break;
                        }
                    }
                }
                catch { }
            }

            // InsertModelDimensions has no mask at all
            // (IDrawingDoc~InsertModelDimensions.html: «0 - All dimensions in
            // the view»): it runs only when every dimension was asked for (N9).
            if (dopo <= prima && ModelImportMayTakeEveryViewDimension(tutte))
            {
                // La piu' vecchia di tutte, un parametro solo. Torna `void`:
                // l'unico modo di sapere se ha fatto qualcosa e' ricontare.
                try
                {
                    disegno.InsertModelDimensions(
                        (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel);
                    provati.Add("InsertModelDimensions");
                    dopo = ContaQuote(disegno);
                    if (dopo < 0) return Fallito("Ho chiesto le quote del modello, ma poi non riesco a ricontare le quote della tavola: non so dirti se sono arrivate. Guarda la tavola prima di andare avanti.");
                    if (dopo > prima) comeFatto = "InsertModelDimensions";
                }
                catch { }
            }

            if (dopo <= prima)
            {
                // Una vista alla volta, selezionandola prima: alcune
                // installazioni ignorano «tutte le viste».
                int quante = 0;
                try
                {
                    var doc = (IModelDoc2)disegno;
                    var v = (disegno.GetFirstView() as IView)?.GetNextView() as IView;
                    while (v != null)
                    {
                        try
                        {
                            doc.ClearSelection2(true);
                            if (doc.Extension.SelectByID2(
                                    v.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0))
                            {
                                // The types follow all_dimensions (N9: the
                                // NOT-MARKED bit here brought 7 unmarked
                                // dimensions with nothing marked), and
                                // DuplicateDims is the same TRUE as above (M2).
                                disegno.InsertModelAnnotations3(
                                    (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel,
                                    ModelImportViewByViewTypes(tutte, extra),
                                    false, ModelImportV3DuplicateDims, false, false);
                                quante++;
                            }
                        }
                        catch { }
                        v = v.GetNextView() as IView;
                    }
                    doc.ClearSelection2(true);
                }
                catch { }
                provati.Add("vista per vista");
                dopo = ContaQuote(disegno);
                if (dopo < 0) return Fallito("Ho chiesto le quote del modello, ma poi non riesco a ricontare le quote della tavola: non so dirti se sono arrivate. Guarda la tavola prima di andare avanti.");
                if (dopo > prima) comeFatto = "una vista alla volta (" + quante + ")";
            }

            if (dopo <= prima)
            {
                // QUANTE NE HA IL PEZZO, E QUALI. Senza questa risposta un
                // «non e' arrivata nessuna quota» non distingue fra «la
                // chiamata non funziona» e «non c'era niente da portare»:
                // sono due difetti diversi e si riparano in due posti diversi.
                // QUI IL PEZZO SI CHIEDE ALLA VISTA, non all'elenco dei
                // documenti aperti. E' un messaggio d'errore: deve dire quante
                // quote ha **il pezzo di QUESTA tavola**, e prendere il primo
                // aperto vorrebbe dire contare le quote di un altro pezzo e
                // stamparle dentro una spiegazione. Un messaggio d'errore che
                // cita il documento sbagliato manda a cercare piu' lontano di
                // dove si era partiti.
                string _ignora;
                var nomiQuote = NomiDelleQuoteDelModello(
                    PercorsoDelModelloInVista(disegno) ?? ModelloDaMettereInVista(out _ignora));
                int marked = nomiQuote.Count(n => n.EndsWith("[marcata]"));

                // NOTHING MARKED, AND ONLY THE MARKED ONES WERE ASKED FOR (N9).
                // Until library 0.141.0 the fallbacks brought the unmarked
                // dimensions anyway: the N9 probe of Z-C21.3, 28/9/2026,
                // on a part with nothing marked the chain imported all 7 of
                // them, view by view, and answered «done». Now every step stops
                // at the marked masks, and the refusal says how to get the
                // others instead of bringing them unasked.
                if (!tutte && nomiQuote.Count > 0 && marked == 0)
                    return Fallito(
                        "No dimension came in (there were " + prima + ", there are " + dopo +
                        "): the part has " + nomiQuote.Count + " dimensions and none is marked " +
                        "for drawing (" + string.Join(", ", nomiQuote) + "). By default only the " +
                        "MARKED dimensions are imported, and no fallback brings the others " +
                        "unasked. Pass all_dimensions=true to import them all, or mark on the " +
                        "model the ones the drawing must show. I tried: " +
                        string.Join(", ", provati) + ".");

                return Fallito(
                    "Non e' arrivata nessuna quota (erano " + prima + ", sono " + dopo +
                    "; l'ultimo tentativo ne ha restituite " + restituite + "). " +
                    (nomiQuote.Count == 0
                        ? "Il pezzo non ha nessuna quota da portare in tavola: e' stato " +
                          "costruito senza quotare gli schizzi. Le quote vanno messe sul " +
                          "MODELLO, non sulla tavola."
                        : "Il pezzo ha " + nomiQuote.Count + " quote, di cui " +
                          marked +
                          " marcate per il disegno (" +
                          string.Join(", ", nomiQuote) + "), e nessuna arriva in tavola. " +
                          "Ho provato: " + string.Join(", ", provati) + ". " +
                          // Up to library 0.141.0 the tail said the import never works out
                          // of process (18/9/2026). It does since 26/9/2026 (the bench case
                          // dimensions_come_from_the_model), and with the fallbacks now held
                          // to the MARKED masks (N9) this refusal is what a part gets when
                          // its marks do not reach the import: the N9 probe of Z-C21.3,
                          // 28/9/2026, marked two sketch dimensions and the MARKED mask
                          // brought 0 of them.
                          "The import works out of process (measured on 26/9/2026 and " +
                          "28/9/2026). A dimension marked on the sketch may still not be " +
                          "one the MARKED mask brings (measured on 28/9/2026: two sketch " +
                          "dimensions marked, 0 imported). Pass all_dimensions=true to import " +
                          "every dimension of the model, or create the ones you need with " +
                          "quota_in_tavola."));
            }

            var misure = new Dictionary<string, object>
            {
                ["how"] = comeFatto,
                ["dimensions_before"] = prima,
                ["dimensions_after"] = dopo,
                ["dimensions_inserted"] = dopo - prima,
                ["views"] = viste,
                ["only_marked_for_drawing"] = !tutte,
            };
            return SenzaFeature(misure);
        }

        // ── THE IMPORT CHAIN OF model_dimensions, AS VALUES (Z-C21.3: M2, N9) ──
        //
        // Small static members with no interop in the signature, so that
        // Prove.Contratti (Zc212DrawingContract) reads them by reflection. The
        // situation of N9, a part with NOTHING marked for drawing, cannot be
        // built by a bench case: every extrusion depth is born marked, and the
        // bridge marks the sketch dimensions it places (OperazioniSw.cs,
        // QuotaSegmenti).
        //
        // The bits (swInsertAnnotation_e): swInsertDimensions = 8,
        // swInsertDimensionsMarkedForDrawing = 32768,
        // swInsertDimensionsNotMarkedForDrawing = 524288.

        private const int ImportMarked = (int)swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing;
        private const int ImportNotMarked = (int)swInsertAnnotation_e.swInsertDimensionsNotMarkedForDrawing;
        private const int ImportGeneric = (int)swInsertAnnotation_e.swInsertDimensions;

        /// <summary>
        /// DuplicateDims of InsertModelAnnotations3: TRUE (finding M2).
        ///
        /// IDrawingDoc~InsertModelAnnotations3.html: «DuplicateDims True to
        /// eliminate duplicate dimensions, false to allow duplicate
        /// dimensions». Up to library 0.141.0 the bridge passed false with the
        /// comment «never duplicates», that is the opposite. Measured by
        /// Z-C21.3 probe_duplicate_dims (28/9/2026) on the plate of the bench
        /// case dimensions_come_from_the_model (120 x 80 x 10, hole Ø10 through,
        /// three standard views): false gave 7 dimensions with 3 of them twice
        /// (the 120 in the front and top views, the depth 10 in the front and
        /// side views, the 80 in the top and side views); true gave 4, none
        /// twice (front: the 10; top: 120, 80 and Ø10; side: none). A second
        /// call added 0 either way.
        /// </summary>
        internal const bool ModelImportV3DuplicateDims = true;

        /// <summary>
        /// The masks tried with InsertModelAnnotations3 (and 2), in order.
        ///
        /// With <paramref name="allDimensions"/> false every mask carries the
        /// MARKED bit and none the NOT-MARKED bit (finding N9). Measured by
        /// the N9 probe of Z-C21.3 (28/9/2026) on a part with NOTHING
        /// marked: MARCATE and MARCATE|GENERICO, both placements, brought 0 of
        /// its 7 unmarked dimensions; NON_MARCATE alone brought all 7.
        /// </summary>
        internal static int[] ModelImportMasks(bool allDimensions)
        {
            return allDimensions
                ? new[] { ImportMarked | ImportNotMarked | ImportGeneric, ImportNotMarked, ImportGeneric, ImportMarked }
                : new[] { ImportMarked, ImportMarked | ImportGeneric };
        }

        /// <summary>
        /// AllTypes of InsertModelAnnotations2: TRUE only when every dimension
        /// was asked for (finding N9). The current page of the 2 says only
        /// «Obsolete»; the old one,
        /// estratto_obsoleteapi\DrawingDoc\DrawingDoc__InsertModelAnnotations2.htm,
        /// says «allTypes TRUE to insert all types of annotations, FALSE uses
        /// the types argument»: TRUE ignores the mask. Measured by
        /// the N9 probe: FALSE with the MARKED mask brought no
        /// unmarked dimension.
        /// </summary>
        internal static bool ModelImportAllTypes(bool allDimensions)
        {
            return allDimensions;
        }

        /// <summary>
        /// Whether InsertModelDimensions(0) may run: only when every dimension
        /// was asked for (finding N9). IDrawingDoc~InsertModelDimensions.html:
        /// «Inserts model dimensions into the selected drawing view», «0 - All
        /// dimensions in the view»: there is no mask. Measured by
        /// the N9 probe: with nothing selected it brought 0, with the
        /// first view selected it brought 2 unmarked dimensions — and nothing
        /// in model_dimensions decides what is selected when it gets there.
        /// </summary>
        internal static bool ModelImportMayTakeEveryViewDimension(bool allDimensions)
        {
            return allDimensions;
        }

        /// <summary>
        /// The types of the last fallback, one view at a time (finding N9).
        /// Measured by the N9 probe on the part with nothing marked:
        /// MARCATE|NON_MARCATE|GENERICO brought all 7 unmarked dimensions (and
        /// the bridge's whole chain reached them through this step), MARCATE
        /// alone brought 0.
        /// </summary>
        internal static int ModelImportViewByViewTypes(bool allDimensions, int extra)
        {
            return (allDimensions ? ImportMarked | ImportNotMarked | ImportGeneric : ImportMarked) | extra;
        }

        // ─────────────────────────────────────────────────────────────────
        // export_pdf
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Il PDF di TUTTI i fogli, con il conto delle pagine.
        ///
        /// Il difetto da prendere e' noto: l'esportazione di serie prende il
        /// foglio corrente, e una tavola da tre fogli esce come una pagina
        /// sola senza errori. Quindi si chiedono tutti i fogli, e poi si
        /// CONTANO le pagine del file scritto. Contare le pagine di un PDF e'
        /// grezzo, ed e' l'unica verifica che possa davvero fallire.
        /// </summary>
        private Esito EsportaPdf(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null) return Fallito("Il documento attivo non e' una tavola.");

            string dove = Str(a, "path", null);
            if (string.IsNullOrWhiteSpace(dove))
            {
                string suo = "";
                try { suo = modello.GetPathName() ?? ""; } catch { }
                if (string.IsNullOrWhiteSpace(suo))
                    return Fallito(
                        "La tavola non e' mai stata salvata e non mi hai detto dove " +
                        "mettere il PDF.");
                dove = Path.ChangeExtension(suo, ".pdf");
            }

            int fogli = ContaFogli(modello);
            if (fogli < 0)
                return Fallito(
                    "Non riesco a contare i fogli della tavola: senza quel numero non " +
                    "potrei controllare che il PDF li abbia tutti, che e' il motivo per cui " +
                    "questa operazione esiste. Non ho scritto niente.");
            if (fogli == 0) return Fallito("La tavola non ha fogli.");

            object datiPdf;
            try { datiPdf = _sw.GetExportFileData((int)swExportDataFileType_e.swExportPdfData); }
            catch (Exception ex) { return Fallito("Non riesco a preparare il PDF: " + ex.Message); }

            var pdf = datiPdf as IExportPdfData;
            if (pdf == null)
                return Fallito("Questa installazione non espone le opzioni di esportazione PDF.");

            bool everySheetAsked;
            try
            {
                pdf.ViewPdfAfterSaving = false;
                // TUTTI i fogli, esplicitamente. E' il difetto che questa
                // operazione esiste per non fare.
                everySheetAsked = pdf.SetSheets((int)swExportDataSheetsToExport_e.swExportData_ExportAllSheets, null);
            }
            catch (Exception ex) { return Fallito("Non riesco a chiedere tutti i fogli: " + ex.Message); }
            // THE ANSWER OF SetSheets IS READ (Z-C21.8, finding L13-6).
            // IExportPdfData~SetSheets.html, Return Value: «True if the drawings
            // sheets are set to export to PDF, false if not». Until library
            // 0.144.0 the answer was dropped, and a false went on to the export
            // with whatever sheets SOLIDWORKS kept: the one-page PDF of a
            // three-sheet drawing that this operation exists to prevent.
            //
            // BUT THE ANSWER LIES (full bench on 0.145.0, 30/9/2026): SetSheets
            // answered false in drawing_exports_to_pdf and dimensions_arrive_in_the_pdf,
            // whose PDFs came out right in every run up to 0.144.0, when the answer
            // was dropped. So a false is not a refusal: it is said in the answer
            // («set_sheets_answer»), and the page count read from the written file
            // against the sheets of the drawing (PdfPageCountRefusal below) decides.
            if (!everySheetAsked)
                _log("[agent] IExportPdfData.SetSheets answered false; the page count of the written PDF decides");

            if (File.Exists(dove))
            {
                try { File.Delete(dove); }
                catch (Exception ex)
                {
                    return Fallito(
                        "C'e' gia' un PDF li' e non riesco a sostituirlo (" + ex.Message +
                        "): probabilmente e' aperto.");
                }
            }

            int errori = 0, avvisi = 0;
            bool ok;
            try
            {
                ok = modello.Extension.SaveAs(
                    dove, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    pdf, ref errori, ref avvisi);
            }
            catch (Exception ex) { return Fallito("PDF non scritto: " + ex.Message); }

            if (!ok || !File.Exists(dove))
                // L13-7: the codes by their swFileSaveError_e / swFileSaveWarning_e
                // names (ErrorCodeDecoder), the raw number kept for a search.
                return Fallito("Il PDF non e' stato scritto (codice " + errori + SaveErrorNames(errori) + ")." +
                               SaveWarningsTail(avvisi));

            int pagine = CountPdfPages(dove);
            string pagesRefusal = PdfPageCountRefusal(pagine, fogli, dove);
            if (pagesRefusal != null) return Fallito(pagesRefusal);

            var misure = new Dictionary<string, object>
            {
                ["path"] = dove,
                ["sheets"] = fogli,
                ["pages"] = pagine > 0 ? (object)pagine : null,
                ["byte"] = new FileInfo(dove).Length,
                // L13-6: what SetSheets answered; false has been measured on PDFs
                // that came out complete, so the page count above is the check.
                ["set_sheets_answer"] = everySheetAsked,
                // L13-7: the Warnings of the save, decoded, also on a success.
                ["save_warnings"] = SaveWarningsForAnswer(avvisi),
            };
            string pdfWarningAdvice = SaveWarningsAdvice(avvisi);
            if (pdfWarningAdvice != null) misure["save_warnings_note"] = pdfWarningAdvice;

            // IL PDF SI RILEGGE, SE SI CHIEDE COSA DEVE CONTENERE (voci GT3-bis,
            // Z-C9). Misurato il 24/9/2026: il testo della tavola esce come
            // testo, ma scritto come indici di glifo dietro le tabelle
            // /ToUnicode — una ricerca nei byte non lo trova. `TestoDelPdf` lo
            // decodifica. Si dice cosa c'e' e cosa no, senza rifiutare: un
            // numero di disegno mancante nel PDF e' una cosa da sapere, non un
            // PDF da buttare.
            var cercati = Lista(a, "search");
            if (cercati.Count > 0)
            {
                string testo = "";
                try { testo = TestoDelPdf.Leggi(dove); } catch { }
                var trovati = new List<object>();
                var mancanti = new List<object>();
                foreach (var c in cercati)
                    (testo.IndexOf(c, StringComparison.Ordinal) >= 0 ? trovati : mancanti).Add(c);
                misure["found"] = trovati.ToArray();
                misure["not_found"] = mancanti.ToArray();
                if (mancanti.Count > 0)
                    misure["warning"] = "Nel PDF scritto non trovo: «" + string.Join("», «", mancanti) +
                        "». Se dovevano esserci, controlla la tavola prima di mandarla.";
            }
            // ── LE QUOTE ARRIVANO NEL PDF? (voce Z-C9, la meta' «oggetti») ──
            //
            // I contatori contano nel documento: una quota su un livello che non
            // si stampa, o in una vista nascosta, conta e in officina non arriva.
            // Ogni numero stampato dalle quote deve comparire nel testo del PDF
            // almeno tante volte quante lo stampano (OperazioniQuoteNelPdf).
            int quoteInTavola, quoteSenzaTesto;
            var numeriAttesi = NumeriDelleQuote(disegno, out quoteInTavola, out quoteSenzaTesto);
            misure["dimensions_in_drawing"] = quoteInTavola;
            misure["dimensions_not_checked"] = quoteSenzaTesto;   // il loro testo non si legge
            if (quoteSenzaTesto > 0)
                misure["warning"] = (misure.ContainsKey("warning") ? misure["warning"] + " " : "") +
                    quoteSenzaTesto + (quoteSenzaTesto == 1 ? " quota non ha" : " quote non hanno") +
                    " un testo che io sappia leggere: se arrivano nel PDF non l'ho controllato.";
            if (quoteInTavola > 0)
            {
                string testoPdf = null;
                try { testoPdf = TestoDelPdf.Leggi(dove); } catch { }
                if (string.IsNullOrWhiteSpace(testoPdf))
                    misure["dimensions_missing_in_pdf"] = null;   // il testo non si legge: non lo so
                else
                {
                    var mancano = QuoteCheMancanoNelPdf(numeriAttesi, testoPdf);
                    misure["dimensions_missing_in_pdf"] = mancano.ToArray();
                    misure["how_many_dimension_numbers_missing"] = mancano.Count;
                    if (mancano.Count > 0)
                        misure["warning"] = (misure.ContainsKey("warning") ? misure["warning"] + " " : "") +
                            "Nel PDF mancano numeri che le quote della tavola stampano: " + string.Join(", ", mancano) +
                            ". Di solito e' una quota su un livello che non si stampa o in una vista nascosta: " +
                            "guarda la tavola prima di mandarla in officina.";
                }
            }
            _log("[agente] PDF: " + fogli + " fogli, " + pagine + " pagine, " + dove);
            return SenzaFeature(misure);
        }

        // ─────────────────────────────────────────────────────────────────
        // save_document
        // ─────────────────────────────────────────────────────────────────

        private Esito SalvaDocumento(IModelDoc2 modello, Dictionary<string, object> a)
        {
            if (modello == null) return Fallito("Non c'e' nessun documento aperto.");

            string dove = Str(a, "path", null);
            int errori = 0, avvisi = 0;
            bool ok;

            if (string.IsNullOrWhiteSpace(dove))
            {
                string suo = "";
                try { suo = modello.GetPathName() ?? ""; } catch { }
                if (string.IsNullOrWhiteSpace(suo))
                    return Fallito(
                        "Il documento non e' mai stato salvato: dimmi dove metterlo.");
                dove = suo;
            }

            try
            {
                ok = modello.Extension.SaveAs(
                    dove, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    null, ref errori, ref avvisi);
            }
            catch (Exception ex) { return Fallito("Non salvato: " + ex.Message); }

            if (!ok || !File.Exists(dove))
            {
                // IL CODICE DA SOLO NON SI PUO' USARE. `SaveAs` risponde un
                // numero, e il numero non dice cosa fare: la causa piu' comune
                // — misurata il 18/9/2026 mentre si provava un'altra cosa — e'
                // che **la cartella non esiste**, e quella si guarda da qui in
                // un millisecondo invece di farla indovinare a chi legge.
                //
                // *Un messaggio d'errore che riporta un codice e si ferma
                // sposta il lavoro su chi lo riceve.*
                string cartella = null;
                try { cartella = Path.GetDirectoryName(dove); } catch { }
                if (!string.IsNullOrWhiteSpace(cartella) && !Directory.Exists(cartella))
                    return Fallito(
                        "Il documento non e' stato salvato: **la cartella non esiste** — «" +
                        cartella + "». SOLIDWORKS non la crea, e il suo codice di errore " +
                        "(" + errori + SaveErrorNames(errori) + ") non lo dice. Creala, o dammi un percorso dentro " +
                        "una cartella che c'e'.");

                bool solaLettura = false;
                try
                {
                    solaLettura = File.Exists(dove) &&
                                  new FileInfo(dove).IsReadOnly;
                }
                catch { }
                if (solaLettura)
                    return Fallito(
                        "Il documento non e' stato salvato: il file esiste gia' ed e' in " +
                        "SOLA LETTURA — «" + dove + "». Toglila, o fai il check-out se " +
                        "sta in un PDM.");

                // L13-7: the codes by their enum names (ErrorCodeDecoder).
                return Fallito("Il documento non e' stato salvato (codice " + errori + SaveErrorNames(errori) + ")." +
                               SaveWarningsTail(avvisi));
            }

            var misure = new Dictionary<string, object>
            {
                ["path"] = dove,
                ["byte"] = new FileInfo(dove).Length,
                // The raw mask stays: bench cases read it (misure.avvisi = 0).
                ["warnings"] = avvisi,
                // L13-7: and its swFileSaveWarning_e names.
                ["save_warnings"] = SaveWarningsForAnswer(avvisi),
            };
            string saveWarningAdvice = SaveWarningsAdvice(avvisi);
            if (saveWarningAdvice != null) misure["save_warnings_note"] = saveWarningAdvice;
            return SenzaFeature(misure);
        }

        // ─────────────────────────────────────────────────────────────────
        // Attrezzi
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Un esito riuscito per un'operazione che non crea feature.</summary>
        private Esito SenzaFeature(Dictionary<string, object> misure)
        {
            return new Esito { Riuscito = true, Misure = misure };
        }

        private static int FormatoFoglio(string nome)
        {
            switch ((nome ?? "").Trim().ToUpperInvariant())
            {
                case "A0": return (int)swDwgPaperSizes_e.swDwgPaperA0size;
                case "A1": return (int)swDwgPaperSizes_e.swDwgPaperA1size;
                case "A2": return (int)swDwgPaperSizes_e.swDwgPaperA2size;
                case "A4": return (int)swDwgPaperSizes_e.swDwgPaperA4size;
                case "A4V":
                case "A4 VERTICALE": return (int)swDwgPaperSizes_e.swDwgPaperA4sizeVertical;
                default: return (int)swDwgPaperSizes_e.swDwgPaperA3size;
            }
        }

        /// <summary>
        /// Il nome che SolidWorks usa per una vista con nome.
        ///
        /// L'asterisco davanti NON e' un refuso: e' il modo in cui SolidWorks
        /// distingue le viste di sistema da quelle salvate dall'utente.
        /// </summary>
        /// <summary>
        /// Le RADICI con cui riconoscere una vista fra quelle che il
        /// documento dichiara. Ce ne sono due per voce, una per la lingua
        /// dell'interfaccia e una per l'inglese, e sono radici e non parole
        /// intere proprio perche' la desinenza cambia: questa installazione
        /// dice «*Isometrico», un'altra «*Isometrica».
        /// </summary>
        private static string[] NomiVistaSw(string chiesta)
        {
            switch (chiesta)
            {
                case "isometric": return new[] { "isometri", "isometric" };
                case "dimetric": return new[] { "dimetri", "dimetric" };
                case "trimetric": return new[] { "trimetri", "trimetric" };
                case "front": return new[] { "anterior", "front" };
                case "back": return new[] { "posterior", "back" };
                case "top": return new[] { "superior", "top" };
                case "bottom": return new[] { "inferior", "bottom" };
                case "right": return new[] { "destra", "right" };
                case "left": return new[] { "sinistr", "left" };
                default: return null;
            }
        }

        /// <summary>Le scritture note, per quando il documento non dichiara nulla.</summary>
        private static List<string> NomiNotiPerRadice(string[] radici)
        {
            var fuori = new List<string>();
            foreach (var r in radici)
            {
                // Si ricostruisce «*Radice» con l'iniziale maiuscola: e' la
                // forma che SolidWorks usa per le viste di sistema, e vale
                // come ultimo tentativo, non come regola.
                if (r.Length == 0) continue;
                fuori.Add("*" + char.ToUpperInvariant(r[0]) + r.Substring(1));
            }
            return fuori;
        }

        /// <summary>
        /// Quanti fogli ha la tavola, o **-1 se non si e' potuto leggere**
        /// (voce Z-C8). Una tavola ha sempre almeno un foglio: un elenco che
        /// non arriva non e' uno zero, e' una lettura andata male — e con lo
        /// zero `export_pdf` rispondeva «la tavola non ha fogli».
        /// </summary>
        private int ContaFogli(IModelDoc2 disegno)
        {
            try
            {
                var d = disegno as IDrawingDoc;
                if (d == null) return -1;
                var nomi = d.GetSheetNames() as string[];
                return nomi == null ? -1 : nomi.Length;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Le viste vere della tavola (il foglio escluso), o **-1 se non si e'
        /// potuto leggere** (voce Z-C8). Aveva la forma che lo scanner non
        /// vedeva: `int n = 0; try { ... } catch { }`, e un errore a meta'
        /// giro dava un numero piu' basso del vero, detto come vero.
        /// </summary>
        private int ContaViste(IDrawingDoc disegno)
        {
            int n = 0;
            try
            {
                var foglio = disegno.GetFirstView() as IView;   // il primo e' il FOGLIO
                if (foglio == null) return -1;
                var v = foglio.GetNextView() as IView;
                while (v != null) { n++; v = v.GetNextView() as IView; }
            }
            catch { return -1; }
            return n;
        }

        private List<string> NomiDelleViste(IDrawingDoc disegno)
        {
            var fuori = new List<string>();
            try
            {
                var foglio = disegno.GetFirstView() as IView;
                if (foglio == null) return fuori;
                var v = foglio.GetNextView() as IView;
                while (v != null)
                {
                    try { fuori.Add(v.Name); } catch { }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return fuori;
        }

        /// <summary>
        /// Le quote di tutte le viste, o **-1 se non si e' potuto leggere**
        /// (voce Z-C8): una vista che non risponde non conta zero, perche' la
        /// somma delle altre sarebbe un numero piu' basso del vero. Chi
        /// confronta prima e dopo usa `NonCresciuta`.
        /// </summary>
        private int ContaQuote(IDrawingDoc disegno)
        {
            int n = 0;
            try
            {
                var foglio = disegno.GetFirstView() as IView;
                if (foglio == null) return -1;
                var v = foglio.GetNextView() as IView;
                while (v != null)
                {
                    n += v.GetDimensionCount();
                    v = v.GetNextView() as IView;
                }
            }
            catch { return -1; }
            return n;
        }

        /// <summary>
        /// Il conteggio delle quote NON e' salito da `before`? Si dice solo di
        /// due conteggi letti. Se uno dei due non si legge, resta l'unica
        /// misura che c'e': l'oggetto che la chiamata ha restituito (null =
        /// non nata). E' meno di quanto vorrebbe la regola di casa — «il
        /// valore che torna da chi ha fatto la cosa non e' la misura della
        /// cosa» — ma e' piu' vero di uno zero inventato.
        /// </summary>
        private bool NonCresciuta(IDrawingDoc disegno, int prima, object restituita)
        {
            int ora = ContaQuote(disegno);
            if (prima >= 0 && ora >= 0) return ora <= prima;
            return restituita == null;
        }

        private static double[] ProprietaDelFoglio(ISheet foglio)
        {
            try { return foglio.GetProperties2() as double[]; }
            catch { return null; }
        }

        private static double[] MisureFoglio(ISheet foglio)
        {
            var p = ProprietaDelFoglio(foglio);
            if (p == null || p.Length < 7) return null;
            // 5 = larghezza, 6 = altezza, in metri.
            return new[] { Unita.Mm(p[5]), Unita.Mm(p[6]) };
        }

        private static double[] LeggiScala(ISheet foglio)
        {
            var p = ProprietaDelFoglio(foglio);
            if (p == null || p.Length < 4) return null;
            // 2 = numeratore, 3 = denominatore.
            if (p[3] == 0) return null;
            return new[] { p[2], p[3] };
        }

        /// <summary>
        /// Quante quote ha il MODELLO, cioe' quante ce ne sarebbero da
        /// portare in tavola. Si contano scorrendo l'albero e chiedendo a
        /// ogni lavorazione le sue quote visualizzate — la stessa strada
        /// che lo stato del documento usa per elencarle.
        /// </summary>
        private List<string> NomiDelleQuoteDelModello(string percorso)
        {
            var fuori = new List<string>();
            try
            {
                var d = DocumentoPerPercorso(percorso);
                var parte = d as IPartDoc;
                if (parte == null) return fuori;
                var f = parte.IFirstFeature();
                int guardia = 0;
                while (f != null && guardia++ < 2000)
                {
                    try
                    {
                        var q = f.GetFirstDisplayDimension() as IDisplayDimension;
                        while (q != null)
                        {
                            try
                            {
                                var vera = q.GetDimension2(0) as IDimension;
                                string nome = vera != null ? vera.FullName : "?";

                                // MARCATA O NO, ACCANTO AL NOME, ed e' la
                                // differenza che divide la diagnosi in due.
                                // `model_dimensions` di serie importa SOLO le
                                // marcate: senza questo dato, «il pezzo ha 7
                                // quote e nessuna arriva» lascia aperte due
                                // spiegazioni opposte — che nessuna sia
                                // marcata (guasto a monte, in chi doveva
                                // marcarle) o che siano marcate e l'import non
                                // le prenda (guasto nell'import).
                                // Il 18/9/2026 ci e' voluta una sonda apposta
                                // per sapere quale delle due: il dato c'era,
                                // e la risposta non lo diceva.
                                try
                                {
                                    nome += q.MarkedForDrawing ? " [marcata]" : " [NON marcata]";
                                }
                                catch { nome += " [marcatura non leggibile]"; }

                                fuori.Add(nome);
                            }
                            catch { fuori.Add("?"); }
                            q = f.GetNextDisplayDimension(q) as IDisplayDimension;
                        }
                    }
                    catch { }
                    f = f.IGetNextFeature();
                }
            }
            catch { }
            return fuori;
        }

        /// <summary>Il documento aperto a quel percorso, o null.</summary>
        private IModelDoc2 DocumentoPerPercorso(string percorso)
        {
            if (string.IsNullOrWhiteSpace(percorso)) return null;
            try
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                int guardia = 0;
                while (d != null && guardia++ < 200)
                {
                    string suo = "";
                    try { suo = d.GetPathName() ?? ""; } catch { }
                    if (StessoFile(suo, percorso)) return d;
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Come questo documento chiama le sue viste con nome.</summary>
        private List<string> NomiDelleVisteDelModello(string percorso)
        {
            var fuori = new List<string>();
            try
            {
                var d = DocumentoPerPercorso(percorso);
                if (d == null) return fuori;
                var nomi = d.GetModelViewNames() as string[];
                if (nomi == null) return fuori;
                foreach (var n in nomi)
                    if (!string.IsNullOrWhiteSpace(n)) fuori.Add(n);
            }
            catch { }
            return fuori;
        }

        /// <summary>I documenti aperti adesso, con percorso e tipo.</summary>
        private List<Dictionary<string, object>> DocumentiAperti()
        {
            var fuori = new List<Dictionary<string, object>>();
            try
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                while (d != null)
                {
                    string percorso = "", titolo = "";
                    int tipo = 0;
                    try { percorso = d.GetPathName() ?? ""; } catch { }
                    try { titolo = d.GetTitle() ?? ""; } catch { }
                    try { tipo = d.GetType(); } catch { }
                    fuori.Add(new Dictionary<string, object>
                    {
                        ["path"] = percorso,
                        ["document_title"] = titolo,
                        ["kind"] = NomeTipoDocumento(tipo),
                    });
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch { }
            return fuori;
        }

        internal static string NomeTipoDocumento(int tipo)
        {
            switch (tipo)
            {
                case (int)swDocumentTypes_e.swDocPART: return "part";
                case (int)swDocumentTypes_e.swDocASSEMBLY: return "assembly";
                case (int)swDocumentTypes_e.swDocDRAWING: return "drawing";
                default: return "altro";
            }
        }

        /// <summary>
        /// Due percorsi sono lo stesso file?
        ///
        /// SEMBRA UNA DOMANDA BANALE E NON LO E'. Misurato alla prima prova
        /// dentro SolidWorks, l'11 settembre 2026: il piano diceva
        /// `C:/Users/.../pezzo.SLDPRT` e SolidWorks rispondeva
        /// `C:\Users\...\pezzo.SLDPRT`. Stesso file, due scritture, e un
        /// confronto fra stringhe che diceva di no. Il risultato era il
        /// peggiore possibile per un utente: «il pezzo non risulta aperto»
        /// mentre il pezzo era li', aperto, davanti a tutti.
        ///
        /// Non si usa `Path.GetFullPath` perche' normalizza anche i
        /// percorsi relativi e puo' sollevare su una stringa storta: qui
        /// serve solo mettere d'accordo le barre e le maiuscole.
        /// </summary>
        private static bool StessoFile(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            return string.Equals(
                a.Trim().Replace('/', '\\'),
                b.Trim().Replace('/', '\\'),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Il percorso di questo documento COME LO SCRIVE SOLIDWORKS,
        /// oppure null se non e' aperto.
        ///
        /// Non basta sapere che il file e' aperto: il percorso va poi
        /// passato a `Create1stAngleViews2` e a
        /// `CreateDrawViewFromModelView3`, e li' una barra in avanti al
        /// posto di una all'indietro fa tornare `false` senza spiegazioni
        /// e senza creare niente.
        /// </summary>
        private string PercorsoCanonico(string percorso)
        {
            if (string.IsNullOrWhiteSpace(percorso)) return null;
            foreach (var d in DocumentiAperti())
            {
                object p;
                if (!d.TryGetValue("path", out p)) continue;
                var suo = Convert.ToString(p);
                if (StessoFile(suo, percorso)) return suo;
            }
            return null;
        }

        private bool DocumentoAperto(string percorso)
        {
            return PercorsoCanonico(percorso) != null;
        }

        /// <summary>Il primo pezzo o assieme aperto: quello che va in tavola.</summary>
        /// <summary>
        /// Il modello da mettere in vista quando il piano non lo dice.
        ///
        /// ── PERCHE' NON SI CHIAMA PIU' «IL PRIMO» ────────────────────────
        ///
        /// Fino al 18/9/2026 tornava **il primo** documento aperto di tipo
        /// parte o assieme, nell'ordine in cui SOLIDWORKS li elenca — un
        /// ordine che **nessuno dichiara** e che cambia con quello che e'
        /// rimasto aperto da prima.
        ///
        /// Il difetto che ne e' uscito, misurato dal banco:
        /// `create_drawing` faceva la tavola del pezzo GIUSTO — quello che gli
        /// viene passato — e subito dopo `standard_views`, che gira **sulla
        /// tavola** e il pezzo non ce l'ha piu' in mano, ci metteva dentro le
        /// viste di **un altro pezzo**, rimasto aperto dal caso precedente.
        ///
        /// **Tre viste, nessun errore, e la tavola di un pezzo che non
        /// c'entra.** Il cartiglio leggeva i `$PRPSHEET` da quel pezzo li' e
        /// mostrava caselle vuote; il PDF sarebbe uscito e sarebbe andato in
        /// officina. E' esattamente il difetto che questo progetto esiste per
        /// non avere, applicato alla strada di consegna.
        ///
        /// ── COSA FA ADESSO ───────────────────────────────────────────────
        ///
        /// Se ne e' aperto **uno solo**, quello e' — non c'e' niente da
        /// indovinare. Se ce ne sono **due o piu'**, si RIFIUTA e si dice
        /// quali sono, perche' scegliere fra loro vorrebbe dire decidere al
        /// posto di chi disegna su una cosa che poi non da' nessun errore.
        ///
        /// *Un ordine che nessuno dichiara non e' una scelta: e' un sorteggio
        /// che sembra una scelta.*
        /// </summary>
        /// <summary>
        /// Il percorso del modello che questa tavola mostra davvero, chiesto
        /// alla prima vista vera. E' l'unica risposta che non dipende da cosa
        /// c'e' aperto attorno: la vista sa a quale documento e' agganciata.
        /// Torna `null` se la tavola non ha ancora viste.
        /// </summary>
        private static string PercorsoDelModelloInVista(IDrawingDoc disegno)
        {
            try
            {
                // La prima «view» e' il FOGLIO: le viste vere cominciano dopo.
                var v = (disegno.GetFirstView() as IView)?.GetNextView() as IView;
                while (v != null)
                {
                    try
                    {
                        var rif = v.ReferencedDocument as IModelDoc2;
                        var p = rif?.GetPathName();
                        if (!string.IsNullOrWhiteSpace(p)) return p;
                    }
                    catch { }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return null;
        }

        private string ModelloDaMettereInVista(out string perche)
        {
            perche = null;
            var candidati = new List<string>();
            foreach (var d in DocumentiAperti())
            {
                var tipo = Convert.ToString(d["kind"]);
                if (tipo != "part" && tipo != "assembly") continue;
                var p = Convert.ToString(d["path"]);
                if (!string.IsNullOrWhiteSpace(p) &&
                    !candidati.Contains(p, StringComparer.OrdinalIgnoreCase))
                    candidati.Add(p);
            }

            if (candidati.Count == 1) return candidati[0];

            if (candidati.Count == 0)
            {
                perche =
                    "Non c'e' nessun pezzo aperto da mettere in vista: una vista di " +
                    "tavola si aggancia a un documento APERTO, non a un file su disco.";
                return null;
            }

            perche =
                "Ci sono " + candidati.Count + " documenti aperti che potrei mettere in " +
                "vista, e sceglierne uno sarebbe un sorteggio: " +
                string.Join(", ", candidati) +
                ". Dimmi quale con l'argomento «model», scrivendo il percorso. " +
                "NON e' pignoleria: fino al 18/9/2026 qui si prendeva il primo " +
                "dell'elenco, e una tavola e' uscita con le viste del pezzo sbagliato " +
                "senza che niente desse errore.";
            return null;
        }

        /// <summary>
        /// How many pages the PDF just written really has, or -1 when they
        /// cannot be counted.
        ///
        /// The file is read and the «/Type /Page» objects that are not
        /// «/Type /Pages» (the node of the page tree) are counted. It is crude:
        /// a PDF with compressed object streams can hide them. A PDF has at
        /// least one page, so finding none means «I could not count», not
        /// «zero pages»: ZERO-E-ZERO, the unknown is -1 (Z-C21.8, finding
        /// L13-6; until library 0.144.0 it was 0, and the caller skipped the
        /// check on it without a word).
        /// </summary>
        private static int CountPdfPages(string percorso)
        {
            try
            {
                var testo = File.ReadAllText(percorso, System.Text.Encoding.ASCII);
                int n = 0, i = 0;
                while (true)
                {
                    i = testo.IndexOf("/Type", i, StringComparison.Ordinal);
                    if (i < 0) break;
                    int j = i + 5;
                    while (j < testo.Length && (testo[j] == ' ' || testo[j] == '\r' ||
                                                testo[j] == '\n' || testo[j] == '\t')) j++;
                    if (j + 5 <= testo.Length && testo.Substring(j, 5) == "/Page")
                    {
                        char dopo = j + 5 < testo.Length ? testo[j + 5] : ' ';
                        if (dopo != 's') n++;   // «/Pages» e' il nodo, non una pagina
                    }
                    i = j;
                }
                return n > 0 ? n : -1;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Why the PDF just written cannot be handed over, or null when its
        /// pages match the sheets (Z-C21.8, finding L13-6). Counting the pages
        /// is the one check of export_pdf that can fail, so a count that could
        /// not be taken is a refusal, not a pass: the file stays where it was
        /// written, and the message says so.
        /// </summary>
        internal static string PdfPageCountRefusal(int pages, int sheets, string path)
        {
            if (pages < 0)
                return "The PDF is written (" + path + "), but I cannot count its pages, so I cannot say " +
                       "that all " + sheets + (sheets == 1 ? " sheet is" : " sheets are") + " in it: that " +
                       "check is the reason this operation exists. Open the file and look before sending it.";
            if (pages != sheets)
                return "The PDF came out with " + pages + (pages == 1 ? " page" : " pages") + " and the drawing has " +
                       sheets + (sheets == 1 ? " sheet" : " sheets") + ". The file is there and it is not the one " +
                       "you need: look at it before sending it to the shop. (" + path + ")";
            return null;
        }
    }
}
