using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// UNA VISTA DI DETTAGLIO SU UNA TAVOLA (voce GT4, seconda meta').
    ///
    /// ── PERCHE' ESISTE ──────────────────────────────────────────────────
    ///
    /// L'altra meta' di cio' che manca a una tavola d'officina vera. La
    /// sezione fa vedere cosa c'e' dentro; il dettaglio fa vedere cio' che e'
    /// **troppo piccolo per leggersi alla scala del disegno** — una gola, uno
    /// smusso, un raccordo con la sua quota.
    ///
    /// ── «PER ANALOGIA» NON E' BASTATO, E ERA SCRITTO ────────────────────
    ///
    /// La misura 0.6 aveva chiuso la sezione e lasciato fuori il dettaglio con
    /// una nota onesta: *«per analogia dovrebbe funzionare uguale — ma "per
    /// analogia" e' proprio il salto che qui e' gia' costato una correzione»*.
    ///
    /// Quella prudenza si e' guadagnata la paga due volte nello stesso
    /// pomeriggio. Sulla sezione, l'analogia col contatore del tratteggio era
    /// **sbagliata**. E qui, delle tre cose che si sarebbero date per scontate,
    /// due erano diverse:
    ///
    ///   - `Showtype` non vuole l'enum delle viste di dettaglio: vuole
    ///     `swDetCircleShowType_e`. L'altro nome non esiste in questa interop.
    ///   - `GetDetailCircleCount2(out int Size)` ha due uscite, e **quella
    ///     giusta e' il valore di ritorno**. Misurato il 23/9/2026:
    ///
    ///         circonferenze PRIMA: ritorno=0  out=1
    ///         circonferenze DOPO : ritorno=1  out=35
    ///
    ///     L'`out` e' la misura di un array, e passa da 1 a 35 per UNA
    ///     circonferenza. Chi lo leggesse come un conto direbbe che la vista
    ///     ne aveva gia' una prima di averne.
    ///
    /// Una cosa invece era davvero uguale: il cerchio e' un `ISketchSegment`
    /// come la linea, e `Select4(false, null)` basta.
    ///
    /// ── IL CONTROLLO NON E' IL TRATTEGGIO ───────────────────────────────
    ///
    /// Sulla sezione la campitura dice che il taglio ha incontrato materiale.
    /// Un dettaglio non taglia niente: mostra la stessa geometria ingrandita, e
    /// infatti `GetFaceHatchCount` sul dettaglio risponde **0**. Copiare il
    /// controllo della sezione avrebbe prodotto un'operazione che rifiuta
    /// sempre — che e' esattamente il difetto che la sezione ha avuto al primo
    /// uso, ripetuto per analogia.
    ///
    /// Qui la prova che il cerchio e' **diventato** un dettaglio e' che la
    /// vista di partenza ha una circonferenza di dettaglio in piu': 0 -> 1.
    /// Senza, sul foglio resterebbe un cerchio disegnato a mano.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome DetailView(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "Il documento attivo non e' una tavola: una vista di dettaglio sta " +
                    "su un foglio di tavola. Crea prima la tavola con «create_drawing».");

            // ── LA VISTA DA DETTAGLIARE ─────────────────────────────────
            //
            // La PRIMA «view» del foglio e' il foglio stesso, e si salta.
            string requested = ViewRequestedByName(drawing, Str(a, "view", null));
            IView target = null;
            var viewNames = new List<string>();
            int viewsBefore = 0;
            try
            {
                var v = drawing.GetFirstView() as IView;
                while (v != null && viewsBefore < 200)
                {
                    viewsBefore++;
                    if (v.Type != (int)swDrawingViewTypes_e.swDrawingSheet)
                    {
                        string itemName = "";
                        try { itemName = v.GetName2() ?? ""; } catch { }
                        viewNames.Add(itemName);
                        if (target == null &&
                            (requested == null ||
                             string.Equals(itemName, requested, StringComparison.OrdinalIgnoreCase)))
                            target = v;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch (Exception ex)
            {
                return Failed("Non riesco a scorrere le viste del foglio: " + ex.Message);
            }

            if (target == null)
                return Failed(requested == null
                    ? "Sul foglio non c'e' nessuna vista da dettagliare: mettici prima " +
                      "le viste con «standard_views»."
                    : "Non trovo la vista «" + requested + "». Sul foglio ci sono: " +
                      (viewNames.Count == 0 ? "nessuna vista" : string.Join(", ", viewNames)) + ".");

            string targetName = "";
            try { targetName = target.GetName2() ?? ""; } catch { }

            var outline = target.GetOutline() as double[];
            if (outline == null || outline.Length < 4)
                return Failed(
                    "Non riesco a leggere l'ingombro della vista «" + targetName +
                    "», e senza quello non so dove mettere il cerchio del dettaglio.");

            double xMin = outline[0], yMin = outline[1], xMax = outline[2], yMax = outline[3];
            double wide = xMax - xMin, up = yMax - yMin;

            // ── DOVE, E QUANTO GRANDE ───────────────────────────────────
            //
            // In millimetri di FOGLIO, come per la sezione: sul foglio la vista
            // e' in scala, e mescolare le due misure e' il modo piu' rapido di
            // ingrandire il posto sbagliato. Di serie al centro, raggio un
            // quarto del lato minore.
            double xM = Num(a, "x_mm", double.NaN);
            double yM = Num(a, "y_mm", double.NaN);
            double cx = double.IsNaN(xM) ? (xMin + xMax) / 2.0 : (xMin + xMax) / 2.0 + xM / 1000.0;
            double cy = double.IsNaN(yM) ? (yMin + yMax) / 2.0 : (yMin + yMax) / 2.0 + yM / 1000.0;

            double radiusMm = Num(a, "radius_mm", Math.Min(wide, up) / 4.0 * 1000.0);
            if (radiusMm <= 0)
                return Failed("«radius_mm» dev'essere maggiore di zero: ho letto " + radiusMm + ".");
            double radius = radiusMm / 1000.0;

            // IL CERCHIO DEVE STARE DENTRO LA VISTA. Uno che sborda ingrandisce
            // del vuoto, e il vuoto ingrandito e' comunque una vista valida.
            if (cx - radius < xMin || cx + radius > xMax ||
                cy - radius < yMin || cy + radius > yMax)
                return Failed(
                    "Il cerchio del dettaglio non sta dentro la vista «" + targetName +
                    "»: centro (" + Math.Round((cx - (xMin + xMax) / 2.0) * 1000, 1) + ", " +
                    Math.Round((cy - (yMin + yMax) / 2.0) * 1000, 1) + ") mm dal centro, raggio " +
                    Math.Round(radiusMm, 1) + " mm, su una vista " +
                    Math.Round(wide * 1000, 1) + " x " + Math.Round(up * 1000, 1) +
                    " mm di foglio. Un cerchio che sborda ingrandisce del vuoto, e il " +
                    "vuoto ingrandito e' comunque una vista che sembra buona.");

            double scale1 = Num(a, "scale", 2);
            if (scale1 <= 0)
                return Failed("«scale» dev'essere maggiore di zero: ho letto " + scale1 + ".");
            string label = Str(a, "label", "B");
            if (string.IsNullOrWhiteSpace(label)) label = "B";
            // UN DETTAGLIO «B» ACCANTO A UNA SEZIONE «B-B» (voce Z-C13): si rifiuta prima.
            string doubled = LetterAlreadyUsed(drawing, label);
            if (doubled != null) return Failed(doubled);

            // Quante circonferenze aveva PRIMA. Vedi il docstring: il conto e'
            // il valore di RITORNO, non l'`out`.
            int circlesBefore = -1;
            try { int howMany; circlesBefore = target.GetDetailCircleCount2(out howMany); }
            catch { }

            // ── IL CERCHIO, SULLO SCHIZZO DEL FOGLIO ────────────────────
            try { drawing.EditSheet(); } catch { }

            // As for the section: the sheet sketch counted before the circle, so
            // that a circle left behind by a refusal can be removed and the
            // removal checked (Z-C21.8, finding M41).
            int segmentsBefore = SheetSketchSegmentCount(drawing);
            ISketchSegment circle = null;
            try
            {
                model.ClearSelection2(true);
                circle = model.SketchManager.CreateCircleByRadius(cx, cy, 0.0, radius)
                          as ISketchSegment;
            }
            catch (Exception ex)
            {
                return Failed("Non sono riuscito a tracciare il cerchio del dettaglio: " + ex.Message);
            }
            if (circle == null)
                return Failed(
                    "«CreateCircleByRadius» non ha prodotto nessun cerchio sul foglio, e " +
                    "senza il cerchio non c'e' niente da selezionare: " +
                    "«CreateDetailViewAt4» non prende la zona in firma, la prende dalla " +
                    "selection.");

            // Lo schizzo si chiude prima, come per la sezione.
            try { model.SketchManager.InsertSketch(true); } catch { }

            bool taken = false;
            string selectError = null;
            try
            {
                model.ClearSelection2(true);
                taken = circle.Select4(false, null);
            }
            catch (Exception ex) { selectError = ex.Message; }
            if (!taken)
                return Failed(
                    (selectError != null
                        ? "I could not select the detail circle (" + selectError + ")"
                        : "The detail circle is there but does not let itself be selected") +
                    ", and CreateDetailViewAt4 takes the zone from the selection: without it, it would do " +
                    "something other than what you asked" +
                    SheetLineRemovedOrNot(RemoveSheetSketchSegment(model, drawing, circle, segmentsBefore), "circle"));

            // ── IL DETTAGLIO, A FIANCO ──────────────────────────────────
            double xDetail = xMax + wide * 0.8;
            double yDetail = (yMin + yMax) / 2.0;

            object born = null;
            string exploded = null;
            try
            {
                born = drawing.CreateDetailViewAt4(
                    xDetail, yDetail, 0.0,
                    (int)swDetViewStyle_e.swDetViewSTANDARD, scale1, 1.0, label,
                    (int)swDetCircleShowType_e.swDetCircleCIRCLE,
                    false, false, false, 0);
            }
            catch (Exception ex) { exploded = ex.Message; }

            int viewsAfter = 0;
            IView last = null;
            try
            {
                var w = drawing.GetFirstView() as IView;
                while (w != null && viewsAfter < 200)
                {
                    viewsAfter++;
                    last = w;
                    w = w.GetNextView() as IView;
                }
            }
            catch { }

            if (viewsAfter <= viewsBefore)
                return Failed(
                    "No view more: the detail was NOT born (there were " + viewsBefore +
                    ", there are " + viewsAfter + "). CreateDetailViewAt4 " +
                    (exploded != null ? "failed: " + exploded
                                     : born == null ? "returned no object"
                                                    : "returned an object") +
                    SheetLineRemovedOrNot(RemoveSheetSketchSegment(model, drawing, circle, segmentsBefore), "circle"));

            // THE DETAIL IS THE VIEW CreateDetailViewAt4 RETURNS (Z-C21.8, finding
            // M41), as for the section: the last view of the chain only when the
            // call returned no view, and the answer says which one was read.
            IView detailView = born as IView;
            string viewReadFrom = "returned by CreateDetailViewAt4";
            if (detailView == null)
            {
                detailView = last;
                viewReadFrom = "last view of the sheet (CreateDetailViewAt4 returned no view)";
            }

            string detailName = "";
            int detailType = -1;
            try { detailName = detailView.GetName2() ?? ""; } catch { }
            try { detailType = detailView.Type; } catch { }

            int circlesAfter = -1;
            try { int howMany; circlesAfter = target.GetDetailCircleCount2(out howMany); }
            catch { }

            // ── LA PROVA CHE IL CERCHIO E' DIVENTATO UN DETTAGLIO ───────
            //
            // Non il tratteggio: un dettaglio non taglia niente e la campitura
            // li' e' zero (misurato). La prova e' che la vista di PARTENZA ha
            // una circonferenza di dettaglio in piu'. Senza, sul foglio
            // resterebbe un cerchio disegnato a mano accanto a una vista in
            // piu' che non gli e' legata.
            if (circlesBefore >= 0 && circlesAfter >= 0 && circlesAfter <= circlesBefore)
            {
                // NEITHER IS LEFT ON THE SHEET (Z-C21.8, finding M41): until library
                // 0.144.0 this refusal described the loose circle and the unlinked
                // view, and left both. The view goes first, then the circle; each
                // is said removed only after its reread (RemoveNewView reads the
                // views again, RemoveSheetSketchSegment counts the sheet sketch).
                // ONLY A VIEW THAT WAS NOT THERE BEFORE IS DELETED: with no view
                // returned, the name comes from the last view of the chain, and a
                // name read before the call is a view of the user.
                bool viewIsNew = !viewNames.Contains(detailName);
                bool viewRemoved = viewIsNew && RemoveNewView(model, detailName);
                bool circleRemoved = RemoveSheetSketchSegment(model, drawing, circle, segmentsBefore);
                return Failed(
                    "The view «" + detailName + "» was born, but on «" + targetName +
                    "» the detail circles are still " + circlesAfter + ": the circle did not become a detail. " +
                    (viewRemoved ? "I removed the view"
                     : !viewIsNew ? "The view I read, «" + detailName + "», was on the sheet before this step, so I " +
                                    "did not delete it: find the new view and remove it — " + Sentences.HowToWithdrawIt
                     : "I could not remove the view «" + detailName + "» (remove it — " + Sentences.HowToWithdrawIt + ")") +
                    SheetLineRemovedOrNot(circleRemoved, "circle"));
            }

            string readLetter = ViewLetter(detailView);
            var outside = new Dictionary<string, object>
            {
                ["label_reread"] = readLetter,
                ["view_read_from"] = viewReadFrom,
                ["view_detailed"] = targetName,
                ["views_before"] = viewsBefore,
                ["views_after"] = viewsAfter,
                ["view_detail"] = detailName,
                ["view_kind"] = detailType,
                ["is_detail"] =
                    detailType == (int)swDrawingViewTypes_e.swDrawingDetailView,
                ["label"] = label,
                ["scale"] = scale1,
                ["circle_mm"] = new object[]
                {
                    Math.Round((cx - (xMin + xMax) / 2.0) * 1000, 2),
                    Math.Round((cy - (yMin + yMax) / 2.0) * 1000, 2),
                    Math.Round(radiusMm, 2),
                },
                ["circles_before"] = circlesBefore,
                ["circles_after"] = circlesAfter,
                ["what_it_does_not_check"] =
                    "SE QUELLO E' IL PARTICOLARE CHE SERVIVA. So dirti che il cerchio e' " +
                    "diventato un dettaglio e che sta dentro la vista, non che dentro ci " +
                    "sia la gola che volevi mostrare: un cerchio spostato di dieci " +
                    "millimetri ingrandisce un pezzo di piano liscio, ed e' altrettanto " +
                    "valido.\n" +
                    "Non guardo se la scala scelta rende il particolare leggibile, ne' se " +
                    "il dettaglio si accavalla con le altre viste o col cartiglio. E non " +
                    "decido se un dettaglio serva: quello dipende da quanto e' piccolo " +
                    "cio' che va quotato.",
            };

            _log("[agente] dettaglio «" + detailName + "» da «" + targetName +
                 "»: circonferenze " + circlesBefore + " -> " + circlesAfter +
                 ", viste " + viewsBefore + " -> " + viewsAfter);
            return WithoutFeature(outside);
        }
    }
}
