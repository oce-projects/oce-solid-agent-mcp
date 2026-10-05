using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// A SECTION VIEW ON A DRAWING (entry GT4).
    ///
    /// ── WHY IT EXISTS ───────────────────────────────────────────────────
    ///
    /// It is the first thing missing for a real mechanical drawing. A part with
    /// an internal cavity, without a section, **cannot be read in the workshop**: the
    /// hidden lines say that there is a void, not what shape it has.
    ///
    /// ── THE API TAKES NO ENTITY IN ITS SIGNATURE, AND THAT CHANGES EVERYTHING ──
    ///
    ///     IDrawingDoc :: CreateSectionViewAt5(Double X, Double Y, Double Z,
    ///                        String SectionLabel, Int32 Options,
    ///                        Object ExcludedComponents, Double SectionDepth)
    ///
    /// There is no argument to say WHERE to cut. The cut line must be
    /// **drawn on the sheet and left selected**: the API takes that one.
    /// This means the operation has five steps that must all
    /// succeed, and each can fail silently.
    ///
    /// Measured on 14/9/2026 (entry 0.6, tool `Checks\SectionView`):
    ///
    ///     line selected: True, objects: 1, type 10 (swSelSKETCHSEGS)
    ///     views on the sheet: 4 -> 5
    ///       -> THE SECTION IS THERE. Last view: «Vista in sezione A-A»
    ///
    /// ── THREE THINGS THAT CANNOT BE DEDUCED, AND THEY COST US ───────────
    ///
    /// 1. **The line must cross the view from side to side.** One that just
    ///    grazes it gives an **empty section without saying anything**.
    /// 2. **The sketch is closed first** (`InsertSketch(true)`). With an
    ///    open sketch SOLIDWORKS is inside a command, and the APIs that create
    ///    views behave differently.
    /// 3. **The sketch of the sheet is not `GetTemplateSketch()`** — that one is the
    ///    title block. «Template» there means *format*. The line ends up in the
    ///    sketch of the first «view», which IS the sheet.
    ///
    /// ── THE HATCHING IS THE CHECK THAT COUNTS ───────────────────────────
    ///
    /// The real risk of this operation is not that it fails: it is that it
    /// **succeeds in the wrong place**. A cut line that fell outside the
    /// material produces a formally valid view, that passes every
    /// count, and shows nothing. It is the same kind as the projection angle: no geometric
    /// measurement reveals it.
    ///
    /// So afterwards the hatching is counted, and **if it is zero the operation
    /// fails** instead of delivering an empty view that looks successful.
    /// *A section without hatching is a cut line that fell outside the
    /// part.*
    ///
    /// **AND THE COUNTER IS NOT THE ONE THE ENTRY PRESCRIBED.** GT4 said
    /// `GetSolidHatchCount() > 0`; measured, that number is **zero on a
    /// real section**, before and after the rebuild. `GetFaceHatchCount()` is
    /// read, which on a good section is 1. The other one is reported all the
    /// same in the answer, so that it serves as a warning to whoever
    /// retraced the same route.
    ///
    /// ── AND THE COUNT OF THE VIEWS, NOT THE RETURN VALUE ────────────────
    ///
    /// `CreateSectionViewAt5` may answer an object without having done
    /// anything. The views are counted before and after: that is the cross-check, the
    /// return value is a promise.
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

            // ── THE VIEW TO SECTION ─────────────────────────────────────
            //
            // The FIRST «view» of the sheet is the sheet itself (type 1), and it is
            // skipped. Whoever took it for a view would section the sheet.
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

            // ── WHERE TO CUT ────────────────────────────────────────────
            //
            // By default: in the middle of the view. `offset_mm` moves the line, in
            // SHEET millimetres — not of the model: on the sheet the view is
            // scaled, and mixing the two measures is the quickest way to
            // cut where one did not want to.
            string facing = (Str(a, "orientation", "vertical") ?? "vertical").Trim().ToLowerInvariant();
            if (facing != "vertical" && facing != "horizontal")
                return Failed(
                    "«orientation» dice come corre la linea di taglio e vale «vertical» o " +
                    "«horizontal»: ho letto «" + facing + "».");

            double offsetM = Num(a, "offset_mm", 0) / 1000.0;
            string label = Str(a, "label", "A");
            if (string.IsNullOrWhiteSpace(label)) label = "A";
            double depthM = Num(a, "depth_mm", 0) / 1000.0;
            // TWO «A-A» ON THE SAME DRAWING (entry Z-C13): it is refused first.
            string doubled = LetterAlreadyUsed(drawing, label);
            if (doubled != null) return Failed(doubled);

            double xMin = outline[0], yMin = outline[1], xMax = outline[2], yMax = outline[3];
            double wide = xMax - xMin, up = yMax - yMin;

            // WE GO OUT BY A TENTH BEYOND THE EDGES. A line that ends
            // exactly on the edge is a free edge case, and the
            // edge case of this API is an empty section that does not say so.
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

            // ── THE LINE, ON THE SKETCH OF THE SHEET ────────────────────
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

            // THE SKETCH IS CLOSED FIRST. See the docstring: with an open sketch
            // the APIs that create views behave differently.
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

            // ── THE SECTION, ALONGSIDE ──────────────────────────────────
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

            // ── THE CROSS-CHECK: HOW MANY VIEWS THERE ARE NOW ───────────
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
            // ── THE RIGHT COUNTER IS `GetFaceHatchCount` ────────────────
            //
            // Entry GT4 prescribed `GetSolidHatchCount() > 0`, and the
            // criterion was right: a section without hatching is a cut
            // that fell outside the material. **The counter was not.**
            //
            // Measured on 23/9/2026 on a section that crosses a solid
            // 120x80x20 plate, with the probe `Checks\SectionView`:
            //
            //     hatching BEFORE the rebuild: return=0  out=0
            //     hatching AFTER  the rebuild: return=0  out=0
            //     FACE hatching:               1
            //     view type: 2 (swDrawingSectionView), GetSection(): an object
            //
            // `GetSolidHatchCount` answers **zero on a real section**,
            // before and after the rebuild. `GetFaceHatchCount` is read.
            //
            // HOW IT WAS DISCOVERED, and it is worth keeping: the operation
            // refused at the first real use, in the first bench case written for
            // it. The guard was right to exist and wrong about the number, and
            // the two things are told apart only by measuring. *A right criterion
            // resting on the wrong counter is a guard that always
            // says no, and it is indistinguishable from one that works until
            // it is used.*
            try { hatching = sectionView.GetFaceHatchCount(); } catch { }

            // The other one is read all the same and reported: it is zero on a
            // good section, and having it alongside is what stops
            // someone from trying again in six months.
            int solids = -1;
            try { int q; sectionView.GetSolidHatchCount(out q); solids = q; } catch { }

            // ── THE HATCHING, AND HERE IT FAILS ─────────────────────────
            //
            // See the docstring. A section view without hatching is a
            // cut line that fell outside the material: formally valid,
            // counts as a view, and shows nothing.
            // THE EMPTY VIEW IS REMOVED (review of 26/9/2026): if it stayed, its
            // letter «A» made the remedy the message itself indicates be refused.
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

            // THE ACTUAL LETTER, read back from the view (Z-C13).
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
                // THE RAW NUMBER IS NOT LEFT ALONE: whoever reads does not have the
                // table of `swDrawingViewTypes_e` at hand.
                ["is_section"] = sectionType == (int)swDrawingViewTypes_e.swDrawingSectionView,
                ["label"] = label,
                ["facing"] = facing,
                ["cut_line_mm"] = new object[]
                {
                    Math.Round(x1 * 1000, 2), Math.Round(y1 * 1000, 2),
                    Math.Round(x2 * 1000, 2), Math.Round(y2 * 1000, 2),
                },
                // TWO NUMBERS, AND THE SECOND IS ZERO BY ITS NATURE. `hatches` is the
                // count of face hatchings, which is what says whether
                // the cut met material; `hatches_solids` is
                // the other counter, which on a section is zero — it is here
                // so that nobody tries the already measured route again.
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
