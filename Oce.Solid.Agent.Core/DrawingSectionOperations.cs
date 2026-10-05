using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// UNA VISTA DI SEZIONE SU UNA TAVOLA (voce GT4).
    ///
    /// ── PERCHE' ESISTE ──────────────────────────────────────────────────
    ///
    /// E' la prima cosa che manca per una tavola meccanica vera. Un pezzo con
    /// una cavita' interna, senza sezione, **non si legge in officina**: le
    /// linee nascoste dicono che c'e' un vuoto, non che forma ha.
    ///
    /// ── L'API NON PRENDE UN'ENTITA' IN FIRMA, E CAMBIA TUTTO ────────────
    ///
    ///     IDrawingDoc :: CreateSectionViewAt5(Double X, Double Y, Double Z,
    ///                        String SectionLabel, Int32 Options,
    ///                        Object ExcludedComponents, Double SectionDepth)
    ///
    /// Non c'e' nessun argomento per dire DOVE tagliare. La linea di taglio va
    /// **disegnata sul foglio e lasciata selezionata**: l'API prende quella.
    /// Questo vuol dire che l'operazione ha cinque passi che devono riuscire
    /// tutti, e ognuno puo' fallire in silenzio.
    ///
    /// Misurato il 14/9/2026 (voce 0.6, attrezzo `Checks\SectionView`):
    ///
    ///     linea selezionata: True, oggetti: 1, tipo 10 (swSelSKETCHSEGS)
    ///     viste sul foglio: 4 -> 5
    ///       -> LA SEZIONE C'E'. Ultima vista: «Vista in sezione A-A»
    ///
    /// ── TRE COSE CHE NON SI DEDUCONO, E SONO COSTATE ────────────────────
    ///
    /// 1. **La linea deve attraversare la vista da parte a parte.** Una che la
    ///    sfiora da' una sezione **vuota senza dire niente**.
    /// 2. **Lo schizzo si chiude prima** (`InsertSketch(true)`). Con uno
    ///    schizzo aperto SOLIDWORKS e' dentro un comando, e le API che creano
    ///    viste si comportano diversamente.
    /// 3. **Lo schizzo del foglio non e' `GetTemplateSketch()`** — quello e' il
    ///    cartiglio. «Template» li' vuol dire *formato*. La linea finisce nello
    ///    schizzo della prima «view», che E' il foglio.
    ///
    /// ── IL TRATTEGGIO E' IL CONTROLLO CHE CONTA ─────────────────────────
    ///
    /// Il rischio vero di questa operazione non e' che fallisca: e' che
    /// **riesca nel posto sbagliato**. Una linea di taglio caduta fuori dal
    /// materiale produce una vista formalmente valida, che passa ogni
    /// conteggio, e non mostra niente. E' la stessa specie del diedro: nessuna
    /// misura geometrica la rivela.
    ///
    /// Quindi dopo si conta la campitura, e **se e' zero l'operazione
    /// fallisce** invece di consegnare una vista vuota che sembra riuscita.
    /// *Una sezione senza tratteggio e' una linea di taglio caduta fuori dal
    /// pezzo.*
    ///
    /// **E IL CONTATORE NON E' QUELLO CHE LA VOCE PRESCRIVEVA.** GT4 diceva
    /// `GetSolidHatchCount() > 0`; misurato, quel numero e' **zero su una
    /// sezione vera**, prima e dopo il ricalcolo. Si legge
    /// `GetFaceHatchCount()`, che su una sezione buona vale 1. L'altro si
    /// riporta lo stesso nella risposta, perche' valga da avviso a chi
    /// ripercorresse la stessa strada.
    ///
    /// ── E IL CONTO DELLE VISTE, NON IL VALORE DI RITORNO ────────────────
    ///
    /// `CreateSectionViewAt5` puo' rispondere un oggetto senza aver fatto
    /// niente. Si contano le viste prima e dopo: quello e' il riscontro, il
    /// valore di ritorno e' una promessa.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome SectionView(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "Il documento attivo non e' una tavola: una vista di sezione sta " +
                    "su un foglio di tavola. Crea prima la tavola con «create_drawing».");

            // ── LA VISTA DA SEZIONARE ───────────────────────────────────
            //
            // La PRIMA «view» del foglio e' il foglio stesso (tipo 1), e si
            // salta. Chi la prendesse per una vista sezionerebbe il foglio.
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
                    ? "Sul foglio non c'e' nessuna vista da sezionare: mettici prima " +
                      "le viste con «standard_views»."
                    : "Non trovo la vista «" + requested + "». Sul foglio ci sono: " +
                      (viewNames.Count == 0 ? "nessuna vista" : string.Join(", ", viewNames)) + ".");

            string targetName = "";
            try { targetName = target.GetName2() ?? ""; } catch { }

            var outline = target.GetOutline() as double[];
            if (outline == null || outline.Length < 4)
                return Failed(
                    "Non riesco a leggere l'ingombro della vista «" + targetName +
                    "», e senza quello non so dove tracciare la linea di taglio: una " +
                    "linea che sfiora la vista da' una sezione vuota senza dire niente.");

            // ── DOVE TAGLIARE ───────────────────────────────────────────
            //
            // Di serie: in mezzo alla vista. `offset_mm` sposta la linea, in
            // millimetri di FOGLIO — non del modello: sul foglio la vista e'
            // in scala, e mescolare le due misure e' il modo piu' rapido di
            // tagliare dove non si voleva.
            string facing = (Str(a, "orientation", "vertical") ?? "vertical").Trim().ToLowerInvariant();
            if (facing != "vertical" && facing != "horizontal")
                return Failed(
                    "«orientation» dice come corre la linea di taglio e vale «vertical» o " +
                    "«horizontal»: ho letto «" + facing + "».");

            double offsetM = Num(a, "offset_mm", 0) / 1000.0;
            string label = Str(a, "label", "A");
            if (string.IsNullOrWhiteSpace(label)) label = "A";
            double depthM = Num(a, "depth_mm", 0) / 1000.0;
            // DUE «A-A» SULLA STESSA TAVOLA (voce Z-C13): si rifiuta prima.
            string doubled = LetterAlreadyUsed(drawing, label);
            if (doubled != null) return Failed(doubled);

            double xMin = outline[0], yMin = outline[1], xMax = outline[2], yMax = outline[3];
            double wide = xMax - xMin, up = yMax - yMin;

            // SI ESCE DI UN DECIMO OLTRE I BORDI. Una linea che finisce
            // esattamente sul bordo e' un caso limite gratuito, e il caso
            // limite di questa API e' una sezione vuota che non lo dice.
            double x1, y1, x2, y2;
            if (facing == "vertical")
            {
                double x = (xMin + xMax) / 2.0 + offsetM;
                if (x <= xMin || x >= xMax)
                    return Failed(
                        "Con «offset_mm» = " + Num(a, "offset_mm", 0) + " la linea di taglio cade " +
                        "FUORI dalla vista, e una sezione tagliata fuori dal materiale " +
                        "esce vuota senza dare nessun errore. La vista e' larga " +
                        Math.Round(wide * 1000, 1) + " mm sul foglio, quindi «offset_mm» sta " +
                        "fra " + Math.Round(-wide * 500, 1) + " e " +
                        Math.Round(wide * 500, 1) + ".");
                x1 = x; y1 = yMin - up * 0.1;
                x2 = x; y2 = yMax + up * 0.1;
            }
            else
            {
                double y = (yMin + yMax) / 2.0 + offsetM;
                if (y <= yMin || y >= yMax)
                    return Failed(
                        "Con «offset_mm» = " + Num(a, "offset_mm", 0) + " la linea di taglio cade " +
                        "FUORI dalla vista, e una sezione tagliata fuori dal materiale " +
                        "esce vuota senza dare nessun errore. La vista e' alta " +
                        Math.Round(up * 1000, 1) + " mm sul foglio, quindi «offset_mm» sta " +
                        "fra " + Math.Round(-up * 500, 1) + " e " +
                        Math.Round(up * 500, 1) + ".");
                x1 = xMin - wide * 0.1; y1 = y;
                x2 = xMax + wide * 0.1; y2 = y;
            }

            // ── LA LINEA, SULLO SCHIZZO DEL FOGLIO ──────────────────────
            try { drawing.EditSheet(); } catch { }

            // The segments of the sheet sketch before the line, so that a line
            // left behind by a refusal can be removed and the removal checked
            // (Z-C21.8, finding M41).
            int segmentsBefore = SheetSketchSegmentCount(drawing);
            ISketchSegment line = null;
            try
            {
                model.ClearSelection2(true);
                line = model.SketchManager.CreateLine(x1, y1, 0.0, x2, y2, 0.0) as ISketchSegment;
            }
            catch (Exception ex)
            {
                return Failed("Non sono riuscito a tracciare la linea di taglio: " + ex.Message);
            }
            if (line == null)
                return Failed(
                    "«CreateLine» non ha prodotto nessun segmento sul foglio, e senza " +
                    "la linea non c'e' niente da selezionare: «CreateSectionViewAt5» " +
                    "non prende il taglio in firma, lo prende dalla selezione.");

            // LO SCHIZZO SI CHIUDE PRIMA. Vedi il docstring: con uno schizzo
            // aperto le API che creano viste si comportano diversamente.
            try { model.SketchManager.InsertSketch(true); } catch { }

            bool taken = false;
            string selectError = null;
            try
            {
                model.ClearSelection2(true);
                taken = line.Select4(false, null);
            }
            catch (Exception ex) { selectError = ex.Message; }
            if (!taken)
                return Failed(
                    (selectError != null
                        ? "I could not select the cutting line (" + selectError + ")"
                        : "The cutting line is there but does not let itself be selected") +
                    ", and CreateSectionViewAt5 takes the cut from the selection: without it, it would do " +
                    "something other than what you asked" +
                    SheetLineRemovedOrNot(RemoveSheetSketchSegment(model, drawing, line, segmentsBefore), "cutting line"));

            // ── LA SEZIONE, A FIANCO ────────────────────────────────────
            double xSection, ySection;
            if (facing == "vertical")
            {
                xSection = xMax + wide * 0.8;
                ySection = (yMin + yMax) / 2.0;
            }
            else
            {
                xSection = (xMin + xMax) / 2.0;
                ySection = yMin - up * 0.8;
            }

            object born = null;
            string exploded = null;
            try
            {
                born = drawing.CreateSectionViewAt5(
                    xSection, ySection, 0.0, label, 0, null, depthM);
            }
            catch (Exception ex) { exploded = ex.Message; }

            // ── IL RISCONTRO: QUANTE VISTE CI SONO ADESSO ───────────────
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
                    "No view more: the section was NOT born (there were " + viewsBefore +
                    ", there are " + viewsAfter + "). CreateSectionViewAt5 " +
                    (exploded != null ? "failed: " + exploded
                                     : born == null ? "returned no object"
                                                    : "returned an object") +
                    SheetLineRemovedOrNot(RemoveSheetSketchSegment(model, drawing, line, segmentsBefore), "cutting line"));

            // THE SECTION IS THE VIEW CreateSectionViewAt5 RETURNS (Z-C21.8, finding
            // M41). CreateSectionViewAt5.html, Return Value: «Section view». Until
            // library 0.144.0 the bridge read the LAST view of the chain instead,
            // which is the new one only as long as SOLIDWORKS appends it there. The
            // count of views stays the check that something was born; the last
            // view is only the fallback when the call returned no view, and the
            // answer says which one was read.
            IView sectionView = born as IView;
            string viewReadFrom = "returned by CreateSectionViewAt5";
            if (sectionView == null)
            {
                sectionView = last;
                viewReadFrom = "last view of the sheet (CreateSectionViewAt5 returned no view)";
            }

            string sectionName = "";
            int sectionType = -1;
            int hatching = -1;
            try { sectionName = sectionView.GetName2() ?? ""; } catch { }
            try { sectionType = sectionView.Type; } catch { }
            // ── IL CONTATORE GIUSTO E' `GetFaceHatchCount` ──────────────
            //
            // La voce GT4 prescriveva `GetSolidHatchCount() > 0`, e il
            // criterio era giusto: una sezione senza campitura e' un taglio
            // caduto fuori dal materiale. **Il contatore no.**
            //
            // Misurato il 23/9/2026 su una sezione che attraversa una piastra
            // 120x80x20 piena, con la sonda `Checks\SectionView`:
            //
            //     tratteggio PRIMA del ricalcolo: ritorno=0  out=0
            //     tratteggio DOPO  il ricalcolo: ritorno=0  out=0
            //     tratteggio di FACCIA:          1
            //     tipo vista: 2 (swDrawingSectionView), GetSection(): un oggetto
            //
            // `GetSolidHatchCount` risponde **zero su una sezione vera**,
            // prima e dopo il ricalcolo. Si legge `GetFaceHatchCount`.
            //
            // COME SI E' SCOPERTO, e vale la pena tenerlo: l'operazione si e'
            // rifiutata al primo uso vero, nel primo caso di banco scritto per
            // lei. La guardia aveva ragione a esistere e torto sul numero, e
            // le due cose si distinguono solo misurando. *Un criterio giusto
            // appoggiato al contatore sbagliato e' una guardia che dice
            // sempre di no, ed e' indistinguibile da una che funziona finche'
            // non la si usa.*
            try { hatching = sectionView.GetFaceHatchCount(); } catch { }

            // L'altro si legge lo stesso e si riporta: vale zero su una
            // sezione buona, e averlo accanto e' cio' che impedisce a
            // qualcuno di riprovarci fra sei mesi.
            int solids = -1;
            try { int q; sectionView.GetSolidHatchCount(out q); solids = q; } catch { }

            // ── IL TRATTEGGIO, E QUI SI FALLISCE ────────────────────────
            //
            // Vedi il docstring. Una vista di sezione senza tratteggio e' una
            // linea di taglio caduta fuori dal materiale: formalmente valida,
            // conta come vista, e non mostra niente.
            // LA VISTA VUOTA SI TOGLIE (revisione del 26/9/2026): restando, la sua
            // lettera «A» faceva rifiutare il rimedio che il messaggio stesso indica.
            // Only a view that was not on the sheet before is deleted: with no
            // view returned, the name comes from the last view of the chain, and
            // a name read before the call is a view of the user (Z-C21.8, M41).
            bool emptyOneRemoved = hatching == 0 && !viewNames.Contains(sectionName) && RemoveNewView(model, sectionName);
            if (hatching == 0)
                return Failed(
                    (emptyOneRemoved ? "Ho tolto la vista «" + sectionName + "»: " : "") +
                    "La vista «" + sectionName + "» e' nata ma NON HA TRATTEGGIO " +
                    "(campiture di faccia: 0), e " +
                    "una sezione senza tratteggio e' una linea di taglio caduta fuori " +
                    "dal materiale: sul foglio c'e' una vista che sembra buona e non " +
                    "mostra niente. La linea l'ho tracciata " +
                    (facing == "vertical" ? "in verticale a x=" : "in orizzontale a y=") +
                    Math.Round((facing == "vertical" ? x1 : y1) * 1000, 2) + " mm di foglio. " +
                    "Sposta il taglio con «offset_mm».");

            // LA LETTERA EFFETTIVA, riletta dalla vista (Z-C13).
            string readLetter = ViewLetter(sectionView);
            var outside = new Dictionary<string, object>
            {
                ["label_reread"] = readLetter,
                ["view_read_from"] = viewReadFrom,
                ["view_sectioned"] = targetName,
                ["views_before"] = viewsBefore,
                ["views_after"] = viewsAfter,
                ["view_section"] = sectionName,
                ["view_kind"] = sectionType,
                // IL NUMERO CRUDO NON SI LASCIA SOLO: chi legge non ha la
                // tabella di `swDrawingViewTypes_e` sottomano.
                ["is_section"] = sectionType == (int)swDrawingViewTypes_e.swDrawingSectionView,
                ["label"] = label,
                ["facing"] = facing,
                ["cut_line_mm"] = new object[]
                {
                    Math.Round(x1 * 1000, 2), Math.Round(y1 * 1000, 2),
                    Math.Round(x2 * 1000, 2), Math.Round(y2 * 1000, 2),
                },
                // DUE NUMERI, E IL SECONDO E' ZERO DI SUO. `hatches` e' il
                // conto delle campiture di faccia, che e' quello che dice se
                // il taglio ha incontrato materiale; `hatches_solids` e'
                // l'altro contatore, che su una sezione vale zero — sta qui
                // per non far riprovare a nessuno la strada gia' misurata.
                ["hatches"] = hatching,
                ["hatches_solids"] = solids,
                ["depth_mm"] = Math.Round(depthM * 1000, 2),
                ["what_it_does_not_check"] =
                    "SE QUELLA E' LA SEZIONE CHE SERVIVA. So dirti che il taglio ha " +
                    "incontrato materiale (il tratteggio c'e'), non che passa per la " +
                    "cavita' che volevi mostrare: una sezione tagliata dieci millimetri " +
                    "piu' in la' e' altrettanto valida e non fa vedere il foro. " +
                    "Scegli «offset_mm» guardando il pezzo, non a caso.\n" +
                    "Non guardo se la vista di sezione si accavalla con le altre o col " +
                    "cartiglio, ne' se la scala del foglio la fa uscire dal formato. " +
                    "Non guardo le frecce e l'etichetta sulla vista di partenza: " +
                    "SOLIDWORKS le mette, io non le rileggo. E non decido se una " +
                    "sezione serva: quello dipende da cosa il pezzo ha dentro.",
            };

            _log("[agente] sezione «" + sectionName + "» da «" + targetName + "»: " +
                 hatching + " tratteggi, viste " + viewsBefore + " -> " + viewsAfter);
            return WithoutFeature(outside);
        }

        /// <summary>
        /// How many segments the sketch of the sheet has (the first «view» of
        /// the drawing IS the sheet, see the header), or -1 when it cannot be
        /// read. The cutting line of section_view and the circle of
        /// detail_view are drawn there.
        /// </summary>
        private static int SheetSketchSegmentCount(IDrawingDoc drawing)
        {
            try
            {
                var sketch = (drawing.GetFirstView() as IView)?.GetSketch() as ISketch;
                if (sketch == null) return -1;
                var segments = sketch.GetSketchSegments() as object[];
                return segments == null ? 0 : segments.Length;   // ZERO-E-ZERO: null is a sheet sketch with no segment
            }
            catch { return -1; }
        }

        /// <summary>
        /// Removes a segment this operation drew on the sheet sketch and then did
        /// not use (Z-C21.8, finding M41: the cutting line or the detail circle
        /// was left on the sheet by every refusal after it was drawn). True only
        /// when the sheet sketch, counted again, is back to
        /// <paramref name="segmentsBefore"/>; DeleteSelection2 and not
        /// EditDelete, which can open a confirmation window.
        /// </summary>
        private static bool RemoveSheetSketchSegment(IModelDoc2 model, IDrawingDoc drawing, ISketchSegment segment, int segmentsBefore)
        {
            if (segment == null || segmentsBefore < 0) return false;
            // Already gone (taken along by a view removed first): the count says so.
            if (SheetSketchSegmentCount(drawing) == segmentsBefore) return true;
            bool deleted = false;
            try
            {
                model.ClearSelection2(true);
                if (segment.Select4(false, null))
                    deleted = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
            }
            catch { }
            if (!deleted) return false;
            int now = SheetSketchSegmentCount(drawing);
            return now >= 0 && now == segmentsBefore;
        }

        /// <summary>The end of a refusal that removed the segment it drew, or tried to.</summary>
        private static string SheetLineRemovedOrNot(bool removed, string what)
        {
            return removed
                ? ". I removed the " + what + " I had drawn."
                : ". I could not confirm that the " + what + " I drew is gone: look at the sheet and, if it is " +
                  "there, remove it — " + Sentences.HowToWithdrawIt + ".";
        }
    }
}
