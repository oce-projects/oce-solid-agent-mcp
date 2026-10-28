using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE TITLE BLOCK: WHAT IT ASKS FOR, AND WHAT IT SHOWS (entry GT3).
    ///
    /// ── THE TITLE BLOCK IS NOT WRITTEN: IT IS FED ─────────────────────
    ///
    /// A template title block holds no text: it holds **links** to
    /// custom properties, written `$PRP:"Descrizione"`. Filling it means
    /// writing those properties — and `set_property` already knows how.
    ///
    /// What was missing is the hard half, and entry GT3 named it:
    /// **«read which variables its title block really references (they cannot
    /// be guessed)»**, two hours of work by hand for every drawing template.
    ///
    /// ── THE DEFECT THIS OPERATION EXISTS TO CATCH ─────────────────────
    ///
    /// The property name written with a different spelling from the one the
    /// title block references — a capital letter, a space, the language — **gives
    /// no error**: SOLIDWORKS writes the property, the title block does not
    /// show it, and the PDF comes out with an empty field that nobody looks at until it
    /// is in the shop.
    ///
    /// It is the same defect already paid for twice today: `40x40x3` against
    /// `40X40X3` on profiles, and `.SLDPRT` against `.sldprt` on paths.
    /// **The same name, two spellings, no error.**
    ///
    /// ── THE PAIR THAT SOLVES IT, AND THEY ARE TWO DIFFERENT CALLS ─────────
    ///
    ///     INote.get_PropertyLinkedText()  ->  «$PRP:"Descrizione"»   WHAT IT ASKS FOR
    ///     INote.GetText()                 ->  «Lid»            WHAT IT SHOWS
    ///
    /// Whoever reads only `GetText()` sees an empty box and does not know why. Whoever
    /// reads both knows **the exact name to write**, with its spelling,
    /// copyable to the letter. *A name that can be asked for is not guessed.*
    ///
    /// ── WHY IT IS NOT VERIFIED IN THE PDF ─────────────────────────────
    ///
    /// Entry GT3 proposed searching for the string inside the exported PDF. It is
    /// a real check and it remains good, but it is **downstream of everything**: it says that
    /// something did not work, not which field nor why. This reading
    /// is upstream, per field, and costs one call.
    /// </summary>
    public partial class SwOperations
    {
        /// <summary>
        /// The links a title block can carry. `$PRPSHEET` takes the
        /// property from the MODEL shown in the view, not from the drawing: it is the
        /// distinction that makes people write the property on the wrong document.
        /// </summary>
        private static readonly Regex Connection =
            new Regex("\\$PRP(SHEET)?:\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);

        /// <summary>
        /// Does the note show only the fixed text written around its links (report
        /// 8cd843 N25)? The text the note asks for, with every link taken out,
        /// leaves its fixed pieces (« » and «Kg» of `$PRPSHEET:"Peso"
        /// $PRPSHEET:"MASSA"Kg`); each piece is taken out of what the note shows,
        /// once, and if nothing but blanks is left no link brought a value. An
        /// empty show counts as only fixed text too. Pure, so the contract suite
        /// reads it.
        /// </summary>
        internal static bool ShowsOnlyFixedText(string asks, string show)
        {
            if (string.IsNullOrWhiteSpace(show)) return true;
            if (string.IsNullOrEmpty(asks)) return false;
            string rest = show;
            foreach (var piece in Connection.Replace(asks, "\u0001").Split('\u0001'))
            {
                var fixedText = piece.Trim();
                if (fixedText.Length == 0) continue;
                int at = rest.IndexOf(fixedText, StringComparison.Ordinal);
                if (at >= 0) rest = rest.Remove(at, fixedText.Length);
            }
            return rest.Trim().Length == 0;
        }

        private Outcome TitleBlock(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "The active document is not a drawing: the title block is on " +
                    "a drawing sheet.");

            ISheet sheet;
            try { sheet = drawing.GetCurrentSheet() as ISheet; }
            catch (Exception ex) { return Failed("I cannot read the sheet: " + ex.Message); }
            if (sheet == null) return Failed("The drawing has no active sheet.");

            var note = TitleBlockNotes(drawing, sheet, out string fromWhere, out int notesOnTheSheet);
            if (note.Count == 0)
                return Failed(
                    "I cannot find any note in the title block of this sheet" +
                    (fromWhere == null ? "" : " (searched " + fromWhere + ")") +
                    ". Either the drawing template has no title block, or it is " +
                    "made of drawing instead of notes: in the second case there is " +
                    "nothing to feed, the text is painted inside the format.");

            // ── WHAT IT ASKS FOR AND WHAT IT SHOWS, field by field ──────
            var fields = new List<object>();
            var requestedItems = new List<string>();
            var empty = new List<string>();
            var shown = new List<string>();
            int connected = 0;

            foreach (var n in note)
            {
                string asks = null, show = null;
                try { asks = n.PropertyLinkedText; } catch { }
                try { show = n.GetText(); } catch { }

                var names = ConnectedNames(asks);
                if (names.Count > 0) connected++;

                foreach (var x in names)
                    if (!requestedItems.Contains(x, StringComparer.Ordinal)) requestedItems.Add(x);

                // A LINKED FIELD THAT SHOWS NOTHING IS THE DEFECT.
                // It is not «empty on purpose»: it is a link that did not
                // find its property.
                //
                // AND THE FIXED TEXT AROUND THE LINKS IS NOT A VALUE (report 8cd843
                // N25, round 7): `$PRPSHEET:"Peso" $PRPSHEET:"MASSA"Kg` showed « Kg»,
                // and «Peso» and «MASSA» were counted as shown with both empty.
                bool onlyFixed = names.Count > 0 && ShowsOnlyFixedText(asks, show);
                bool showNothing = string.IsNullOrWhiteSpace(show) || onlyFixed;
                if (names.Count > 0 && showNothing)
                    foreach (var x in names)
                        if (!empty.Contains(x, StringComparer.Ordinal)) empty.Add(x);

                // AND THOSE THAT REALLY ARRIVE AT THE END.
                //
                // It is not the complement of «empty»: it is the positive statement,
                // and it is needed so that **it can be proved that something works**.
                // A field that lists only what is missing cannot be used to
                // show that a write arrived: one could only say
                // that it is not in the list of failures, and «it is not among the
                // broken ones» is not «it works».
                if (names.Count > 0 && !showNothing)
                    foreach (var x in names)
                        if (!shown.Contains(x, StringComparer.Ordinal)) shown.Add(x);

                var entry = new Dictionary<string, object>
                {
                    ["asks"] = string.IsNullOrWhiteSpace(asks) ? null : asks,
                    ["shows"] = show ?? "",
                };
                if (names.Count > 0) entry["property"] = names.ToArray();
                if (onlyFixed && !string.IsNullOrWhiteSpace(show)) entry["only_fixed_text"] = true;
                fields.Add(entry);
            }

            var measurements = new Dictionary<string, object>
            {
                ["sheet"] = SheetName(sheet) ?? "",
                ["sheet_format"] = TestText(() => sheet.GetSheetFormatName()),
                ["notes_in_title_block"] = note.Count,
                ["notes_linked"] = connected,
                // The notes put on the sheet, left out of the title block (N45).
                ["notes_on_the_sheet_left_out"] = notesOnTheSheet,
                ["searched_in"] = fromWhere,
                ["fields"] = fields.ToArray(),
            };

            // ── THE LIST THAT REMOVES THE GUESSWORK ─────────────────────
            //
            // It is the piece that is worth having: the EXACT names this title block wants,
            // copyable to the letter into `set_property`.
            if (requestedItems.Count > 0)
            {
                measurements["properties_wanted_by_title_block"] = requestedItems.ToArray();
                measurements["how_to_use_it"] =
                    "Copy these names LITERALLY into «set_property». A " +
                    "different capital or space gives no error: the property " +
                    "is written, the title block does not show it, and the PDF " +
                    "comes out with the empty box.";
            }
            // This one too always, for the same reason: it is the positive
            // statement, and a statement that disappears when it is false is not
            // a statement.
            measurements["requested_and_shown"] = shown.ToArray();

            // ── EMPTY BECAUSE NOT WRITTEN, OR EMPTY BECAUSE NOT RESOLVED? ──
            //
            // ── WHY THIS DIVISION EXISTS ─────────────────────────────────
            //
            // On 18/9/2026 the case `title_block_says_what_it_wants` was
            // **green twice and red the third time**, on the same library:
            // `Descrizione`, written on the part and reread, no longer appeared
            // among the shown ones. Retried from the bridge, **it did not reproduce**.
            //
            // With the earlier fields that red said only «does not say
            // Descrizione», and did not tell apart two opposite things:
            //
            //   * the property is NOT on the model   -> whoever draws must
            //     write it, and it is the defect the operation exists to
            //     catch;
            //   * the property IS on the model and the title block shows it
            //     empty -> the link did not resolve: either not yet, or
            //     it is broken. It is not the fault of whoever draws, and writing the
            //     property again would change nothing.
            //
            // They are two diagnoses and two remedies. **A list that puts them
            // together sends people to repair the wrong thing**, and it is the same
            // form of the defect the operation denounces: an answer that
            // looks complete and is not.
            //
            // We look in the model referenced by the first real view —
            // the one `$PRPSHEET` takes from — in both drawers,
            // because document and configuration are separate lists.
            // AND FIRST OF ALL: WAS I ABLE TO LOOK?
            //
            // `ViewModelProperty` returns `null` when it cannot get there.
            // Without saying so, the list below would stay empty **out of ignorance**
            // and not by measurement — and a list empty out of ignorance passes
            // any check that asks «X is not in it».
            //
            // On 18/9/2026 exactly this happened, and it cost one
            // bench run: the distinction was written in the comment of that
            // function and **was not visible to whoever reads the answer**.
            // *A distinction that lives only in a comment is not a
            // distinction: it is an intention.*
            string whoWasChecked;
            var onDisk = ViewModelProperty(drawing, out whoWasChecked);
            measurements["model_checked_for_properties"] = whoWasChecked ??
                "I DID NOT REACH THE MODEL: the two lists below are empty " +
                "because I could not look, not because there was nothing.";

            var presentButMute = new List<string>();
            if (onDisk != null)
                foreach (var x in empty)
                    if (onDisk.Contains(BareName(x), StringComparer.OrdinalIgnoreCase))
                        presentButMute.Add(x);

            measurements["requested_but_empty"] = empty.ToArray();

            // ALWAYS, EVEN EMPTY, and it is not pedantry: the bench treats an
            // ABSENT field as red proof (entry F4), so a field that
            // appears only when it has something inside **cannot even
            // be demanded empty**. And whoever reads the answer, not finding it,
            // concludes that the operation cannot answer instead of
            // the list being empty.
            measurements["requested_present_on_model_but_shown_empty"] = presentButMute.ToArray();
            if (presentButMute.Count > 0)
                measurements["not_the_drafters_fault"] =
                    "These properties ARE on the model shown in the view, and the title " +
                    "block shows them empty all the same: the link did not resolve. " +
                    "Writing them again changes nothing. Try rebuilding the drawing " +
                    "and reading again; if they stay empty, the link is broken.";

            string viewByProperty = TestText(() => sheet.CustomPropertyView);
            if (!string.IsNullOrWhiteSpace(viewByProperty))
                measurements["sheet_properties_source_view"] = viewByProperty;

            // ── THE OTHER SHEETS (report 8cd843 N59, round 16) ──────────
            //
            // Only the active sheet was read. The third sheet of round 16 showed another
            // part, and its title block took two `$PRPSHEET` properties from that part:
            // both empty in the PDF, and the answer said «no empty box». The other sheets are
            // not activated (that would change the drawing): for each one its own title
            // block is read from the «view» of its format, the models of its views are
            // read, and the names it asks with `$PRPSHEET` are looked for on them. Only the
            // names this sheet shows are warned about: those empty here too are already in
            // «requested_but_empty» (scenario t60 on a copy of the drawing of round 16: 20 of
            // the 22 names the coupler lacked were empty on the first sheet too, and they
            // buried the two that were not). ALWAYS, EVEN EMPTY, for the reason given above
            // `requested_present_on_model_but_shown_empty`.
            var otherWarnings = new List<string>();
            measurements["other_sheets"] = OtherSheets(
                drawing, SheetName(sheet), measurements["sheet_format"] as string,
                requestedItems, empty, otherWarnings).ToArray();
            if (otherWarnings.Count > 0)
                measurements["other_sheets_warning"] = string.Join(" ", otherWarnings);

            if (requestedItems.Count == 0)
                measurements["verdict"] =
                    "The title block has " + note.Count + (note.Count == 1 ? " note" : " notes") +
                    " but **none linked to a property**: the text is written fixed " +
                    "inside the format. Filling it with «set_property» would " +
                    "change nothing, and I do not let you believe it.";
            else if (empty.Count == 0)
                measurements["verdict"] =
                    "The title block wants " + requestedItems.Count +
                    (requestedItems.Count == 1 ? " property" : " properties") + " (" +
                    string.Join(", ", requestedItems) + ") and shows all of them: no empty " +
                    "box.";
            else
                measurements["verdict"] =
                    "WARNING: the title block asks for " + requestedItems.Count + " properties and " +
                    empty.Count + " show NOTHING (" + string.Join(", ", empty) +
                    "). Either they have not been written yet, or they are written with a " +
                    "spelling different from the one the title block references — and that " +
                    "case gives no error: the drawing comes out with the empty boxes. The " +
                    "names above are the right ones, copy them literally.";
            // The verdict speaks for the active sheet only; said in it, not only in a field
            // below, so that «no empty box» is not read as true of the whole drawing (N59).
            if (otherWarnings.Count > 0)
                measurements["verdict"] = measurements["verdict"] + " BUT ON THE OTHER SHEETS: " +
                                          measurements["other_sheets_warning"];

            _log("[agent] " + measurements["verdict"]);
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// The title block's notes. Two roads, and it says which one answered:
        /// the real `TitleBlock` (which not all drawing templates have) and, in
        /// its absence, the annotations of the SHEET — which in the API is the first
        /// «view», the one that carries the format.
        /// </summary>
        private static List<INote> TitleBlockNotes(
            IDrawingDoc drawing, ISheet sheet, out string fromWhere, out int onTheSheet)
        {
            fromWhere = null;
            onTheSheet = 0;
            var outside = new List<INote>();

            try
            {
                var block = sheet.TitleBlock as ITitleBlock;
                if (block != null)
                {
                    var n = block.GetNotes() as object[];
                    if (n != null)
                        foreach (var o in n) { var note = o as INote; if (note != null) outside.Add(note); }
                    if (outside.Count > 0) { fromWhere = "in the title block group of the sheet"; return outside; }
                }
            }
            catch { }

            // THE FIRST «VIEW» OF A DRAWING IS THE SHEET, not a view of the
            // part: it is the same thing that `CountViews` skips to count the
            // real views, and here instead it is exactly the one that is needed.
            try
            {
                var sheetView = drawing.GetFirstView() as IView;
                if (sheetView != null)
                {
                    onTheSheet = FormatNotes(sheetView, outside);
                    if (outside.Count > 0) fromWhere = "among the notes of the sheet format";
                }
            }
            catch { }

            if (fromWhere == null) fromWhere = "in the title block group and among the notes of the format";
            return outside;
        }

        /// <summary>
        /// The notes of the format carried by a sheet «view», added to «notes»; the
        /// number of notes put on the sheet, left out, is returned. The view is the
        /// first one of the active sheet (TitleBlockNotes) or the first one of the list
        /// of another sheet (SheetViews, report 8cd843 N59).
        /// </summary>
        private static int FormatNotes(IView sheetView, List<INote> notes)
        {
            int onTheSheet = 0;
            try
            {
                var ann = sheetView.GetFirstAnnotation3() as IAnnotation;
                while (ann != null)
                {
                    // WE DO NOT ASK THE TYPE, WE TRY THE CAST.
                    //
                    // `IAnnotation` has a `GetType()` that returns `Int32`, and in
                    // C# that name collides with `object.GetType()`: the
                    // compiler does not know which you mean. `GetSpecificAnnotation`
                    // already returns the specific object, and a cast to `INote` that
                    // fails gives `null` — which is the same information
                    // without the collision.
                    try
                    {
                        var note = ann.GetSpecificAnnotation() as INote;
                        // ONLY THE NOTES OF THE FORMAT (report 8cd843 N45, round 12):
                        // the annotations of the sheet hold the notes put on the sheet
                        // too, and the general note of the drawing was read as the first
                        // field of the title block (67 -> 68). IAnnotation.OwnerType
                        // (IAnnotation~OwnerType.html: «drawing view, drawing sheet,
                        // drawing template») tells them apart; probe on a copy of the
                        // drawing of the report (27/10/2026): the general note 1
                        // (swAnnotationOwner_DrawingSheet), the 67 fields 2
                        // (swAnnotationOwner_DrawingTemplate). A note whose owner
                        // cannot be read is kept, as before.
                        int owner = -1;
                        try { owner = ann.OwnerType; } catch { }
                        if (note != null && owner == (int)swAnnotationOwner_e.swAnnotationOwner_DrawingSheet) onTheSheet++;
                        else if (note != null) notes.Add(note);
                    }
                    catch { }
                    ann = ann.GetNext3() as IAnnotation;
                }
            }
            catch { }
            return onTheSheet;
        }

        /// <summary>
        /// The «view» that carries the format of each sheet, by sheet name, read without
        /// activating any sheet (report 8cd843 N59). IDrawingDoc~GetViews.html: «an array
        /// of arrays with a length equal to the number of sheets», each «a list of views
        /// with the first view in the list being the sheet itself», the order of the sheets
        /// «undetermined» — so they are matched by name, not by position. Empty when the
        /// list cannot be read.
        /// </summary>
        private static Dictionary<string, IView> SheetViews(IDrawingDoc drawing)
        {
            var outside = new Dictionary<string, IView>(StringComparer.Ordinal);
            try
            {
                var all = drawing.GetViews() as object[];
                foreach (var o in all ?? new object[0])
                {
                    var list = o as object[];
                    var first = list != null && list.Length > 0 ? list[0] as IView : null;
                    if (first == null) continue;
                    string name = null;
                    try { name = first.GetName2(); } catch { }
                    if (!string.IsNullOrEmpty(name) && !outside.ContainsKey(name)) outside[name] = first;
                }
            }
            catch { }
            return outside;
        }

        /// <summary>
        /// The property names inside a linked text. The name is kept
        /// **as written**, normalizing nothing: normalizing it here
        /// would mean returning a spelling the title block does not use, that is
        /// exactly the error this operation exists to prevent.
        /// </summary>
        /// <summary>
        /// The property name without the suffix the operation sticks
        /// on to say WHERE it comes from: «Descrizione  (from the part, not from the
        /// drawing)» returns «Descrizione».
        ///
        /// It lives here and is not written twice because the suffix is a presentation
        /// choice, and whoever compares two lists must compare the
        /// true names — not the ones dressed up to be read.
        /// </summary>
        private static string BareName(string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return itemName;
            int i = itemName.IndexOf("  (", StringComparison.Ordinal);
            return i < 0 ? itemName.Trim() : itemName.Substring(0, i).Trim();
        }

        /// <summary>
        /// The names of the properties that exist on the MODEL shown in the
        /// first real view — which is the one `$PRPSHEET` takes from.
        ///
        /// BOTH DRAWERS are looked at: the document's properties and
        /// those of the active configuration are separate lists, they are not
        /// inherited and do not see each other. Measured on 18/9/2026: a
        /// property written on the document is **not** seen from the
        /// configuration, and the title block shows it all the same — so
        /// looking at only one would say the wrong thing one way or the other.
        ///
        /// It returns `null` — not an empty list — when it could not
        /// look: «I found nothing» and «I could not look» are
        /// two different answers, and whoever reads must be able to tell them apart.
        /// </summary>
        private static List<string> ViewModelProperty(
            IDrawingDoc drawing, out string whomIChecked)
        {
            whomIChecked = null;
            try
            {
                // The first «view» is the SHEET: the real views begin after.
                var v = (drawing.GetFirstView() as IView)?.GetNextView() as IView;
                IModelDoc2 refObj = null;
                while (v != null && refObj == null)
                {
                    try { refObj = v.ReferencedDocument as IModelDoc2; } catch { }
                    v = v.GetNextView() as IView;
                }
                if (refObj == null) return null;

                // THE PATH, not the title: it is the only address that does not
                // lie when two documents have the same name.
                try { whomIChecked = refObj.GetPathName(); } catch { }
                if (string.IsNullOrWhiteSpace(whomIChecked))
                    try { whomIChecked = "no path, title «" + refObj.GetTitle() + "»"; } catch { }

                var outside = new List<string>();
                var location = new List<string> { "" };
                try
                {
                    var g = refObj.ConfigurationManager as IConfigurationManager;
                    var att = g?.ActiveConfiguration?.Name;
                    if (!string.IsNullOrWhiteSpace(att)) location.Add(att);
                }
                catch { }

                foreach (var propertyManager in location)
                {
                    try
                    {
                        var gp = refObj.Extension.get_CustomPropertyManager(propertyManager)
                                 as ICustomPropertyManager;
                        var names = gp?.GetNames() as string[];
                        if (names != null)
                            foreach (var n in names)
                                if (!outside.Contains(n, StringComparer.OrdinalIgnoreCase))
                                    outside.Add(n);
                    }
                    catch { }
                }
                return outside;
            }
            catch { return null; }
        }

        /// <summary>
        /// The sheets other than the active one (report 8cd843 N59, round 16), each with
        /// the models of its views and, on each model, which names asked with `$PRPSHEET`
        /// it gives no value to. Read without activating the sheet: its views through
        /// ISheet.GetViews («Not active, this method returns only the drawing views»,
        /// ISheet~GetViews.html), its own title block through SheetViews. A view whose
        /// model is not loaded is named and not checked. When its title block cannot be
        /// reached the names of the active sheet are used, and said so, with its format
        /// when it is another one.
        /// </summary>
        private static List<Dictionary<string, object>> OtherSheets(
            IDrawingDoc drawing, string activeSheet, string activeFormat,
            List<string> requested, List<string> emptyHere, List<string> warnings)
        {
            var outside = new List<Dictionary<string, object>>();
            string[] names = null;
            try { names = drawing.GetSheetNames() as string[]; } catch { }
            if (names == null) return outside;
            var wanted = NamesFromThePart(requested);
            var sheetViews = SheetViews(drawing);
            var emptyBare = new List<string>();
            foreach (var x in emptyHere ?? new List<string>()) emptyBare.Add(BareName(x));

            foreach (var name in names)
            {
                if (string.Equals(name, activeSheet, StringComparison.Ordinal)) continue;
                var entry = new Dictionary<string, object> { ["sheet"] = name };
                outside.Add(entry);
                ISheet other = null;
                try { other = drawing.get_Sheet(name) as ISheet; } catch { }
                if (other == null)
                {
                    entry["not_read"] = "I could not reach this sheet (IDrawingDoc.get_Sheet).";
                    warnings.Add("Sheet «" + name + "»: I could not reach it, so I do not know what its title block shows.");
                    continue;
                }

                string format = TestText(() => other.GetSheetFormatName());
                entry["sheet_format"] = format;
                string formatWarning = OtherFormatWarning(name, format, activeFormat);
                if (formatWarning != null) entry["different_format"] = true;

                // ITS OWN TITLE BLOCK: the names it asks, not those of the active sheet.
                // A sheet whose format has no linked note asks for nothing, and nothing of
                // it can show empty (a sheet made with «paper_size» has no format notes:
                // measured on the bench, 28/10/2026).
                var askedHere = wanted;
                IView itsSheetView;
                if (sheetViews.TryGetValue(name, out itsSheetView))
                {
                    var itsNotes = new List<INote>();
                    FormatNotes(itsSheetView, itsNotes);
                    var itsRequested = new List<string>();
                    foreach (var n in itsNotes)
                    {
                        string asks = null;
                        try { asks = n.PropertyLinkedText; } catch { }
                        foreach (var x in ConnectedNames(asks))
                            if (!itsRequested.Contains(x, StringComparer.Ordinal)) itsRequested.Add(x);
                    }
                    askedHere = NamesFromThePart(itsRequested);
                    entry["title_block_notes"] = itsNotes.Count;
                    entry["asked_from_the_part"] = askedHere.ToArray();
                    if (itsNotes.Count == 0)
                        entry["no_title_block"] = "The format of this sheet has no note: nothing it shows can be empty.";
                }
                else
                {
                    entry["title_block_not_read"] = "I could not reach the format of this sheet (IDrawingDoc.GetViews): " +
                        "the names looked for are those of the title block of the active sheet.";
                    if (formatWarning != null) warnings.Add(formatWarning);
                }
                string chosen = TestText(() => other.CustomPropertyView);
                if (!string.IsNullOrWhiteSpace(chosen)) entry["properties_source_view"] = chosen;

                object[] views = null;
                try { views = other.GetViews() as object[]; } catch { }
                var models = new List<Dictionary<string, object>>();
                var viewsOfModel = new List<List<string>>();
                var missingOf = new List<List<string>>();
                var blankOf = new List<List<string>>();
                var viewNames = new List<string>();
                foreach (var o in views ?? new object[0])
                {
                    var v = o as IView;
                    if (v == null) continue;
                    string viewName = null;
                    try { viewName = v.GetName2(); } catch { }
                    IModelDoc2 m = null;
                    try { m = v.ReferencedDocument as IModelDoc2; } catch { }
                    string path = null;
                    try { path = m != null ? m.GetPathName() : v.GetReferencedModelName(); } catch { }
                    // The sheet itself, or a view of nothing: no model to take properties from.
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (viewName != null) viewNames.Add(viewName);
                    string configuration = "";
                    try { configuration = v.ReferencedConfiguration ?? ""; } catch { }

                    int known = models.FindIndex(x =>
                        string.Equals((string)x["path"], path, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals((string)x["configuration"], configuration, StringComparison.Ordinal));
                    if (known >= 0) { if (viewName != null) viewsOfModel[known].Add(viewName); continue; }

                    var model = new Dictionary<string, object> { ["path"] = path, ["configuration"] = configuration };
                    var missing = new List<string>();
                    var blank = new List<string>();
                    if (m == null) model["not_checked"] = "the model is not loaded";
                    else
                    {
                        model["values"] = PropertyValues(m, configuration, askedHere, missing, blank);
                        model["asked_but_not_on_model"] = missing.ToArray();
                        model["asked_but_blank_on_model"] = blank.ToArray();
                    }
                    models.Add(model);
                    viewsOfModel.Add(viewName != null ? new List<string> { viewName } : new List<string>());
                    missingOf.Add(missing);
                    blankOf.Add(blank);
                }

                entry["views"] = viewNames.ToArray();
                if (views == null) entry["not_read"] = "ISheet.GetViews gave no list: the models of this sheet are not known.";
                // The view named in the sheet properties, when one is: the title block
                // takes from its model, and only that one is warned about.
                int source = string.IsNullOrWhiteSpace(chosen) ? -1 :
                    viewsOfModel.FindIndex(l => l.Contains(chosen, StringComparer.OrdinalIgnoreCase));
                for (int i = 0; i < models.Count; i++)
                {
                    models[i]["views"] = viewsOfModel[i].ToArray();
                    if (source >= 0 && i != source) continue;
                    var freshMissing = NotEmptyHere(missingOf[i], emptyBare);
                    var freshBlank = NotEmptyHere(blankOf[i], emptyBare);
                    models[i]["empty_there_shown_here"] = freshMissing.Concat(freshBlank).ToArray();
                    int alsoHere = missingOf[i].Count + blankOf[i].Count - freshMissing.Count - freshBlank.Count;
                    string w = OtherSheetWarning(name, (string)models[i]["path"], freshMissing, freshBlank,
                                                 source < 0 && models.Count > 1, alsoHere);
                    if (w != null) warnings.Add(w);
                }
                entry["models"] = models.ToArray();
            }
            return outside;
        }

        /// <summary>
        /// The value a model gives to each name in «wanted», looked for in its document
        /// properties and in those of «configuration» (the two drawers do not see each
        /// other, see ViewModelProperty). Names on neither go to «missing», names present
        /// with a blank resolved value to «blank». A value in either drawer counts as
        /// given: which of the two wins when both are set was not measured.
        /// </summary>
        private static Dictionary<string, object> PropertyValues(
            IModelDoc2 m, string configuration, List<string> wanted, List<string> missing, List<string> blank)
        {
            var values = new Dictionary<string, object>();
            var drawers = new List<string> { "" };
            if (!string.IsNullOrWhiteSpace(configuration)) drawers.Add(configuration);
            foreach (var w in wanted)
            {
                bool present = false;
                string given = null;
                foreach (var d in drawers)
                {
                    try
                    {
                        var gp = m.Extension.get_CustomPropertyManager(d) as ICustomPropertyManager;
                        var names = gp?.GetNames() as string[];
                        var real = names?.FirstOrDefault(n => string.Equals(n, w, StringComparison.OrdinalIgnoreCase));
                        if (real == null) continue;
                        present = true;
                        string raw, resolved;
                        bool wasResolved, link;
                        gp.Get6(real, false, out raw, out resolved, out wasResolved, out link);
                        if (string.IsNullOrWhiteSpace(given) && !string.IsNullOrWhiteSpace(resolved)) given = resolved;
                    }
                    catch { }
                }
                if (!present) missing.Add(w);
                else if (string.IsNullOrWhiteSpace(given)) blank.Add(w);
                else values[w] = given;
            }
            return values;
        }

        /// <summary>
        /// The bare names the title block asks with `$PRPSHEET`, the ones that come from
        /// the model in view: those marked by ConnectedNames. Pure, so the contract suite
        /// reads it.
        /// </summary>
        internal static List<string> NamesFromThePart(IEnumerable<string> requested)
        {
            var outside = new List<string>();
            foreach (var r in requested ?? new string[0])
                if (r != null && r.EndsWith(FromThePart, StringComparison.Ordinal))
                {
                    var bare = BareName(r);
                    if (!outside.Contains(bare, StringComparer.Ordinal)) outside.Add(bare);
                }
            return outside;
        }

        /// <summary>
        /// The names of «names» that are not among «emptyHere», compared without regard to
        /// case as the properties are (see ViewModelProperty). Pure, so the contract suite
        /// reads it.
        /// </summary>
        internal static List<string> NotEmptyHere(List<string> names, List<string> emptyHere)
        {
            var outside = new List<string>();
            foreach (var n in names ?? new List<string>())
                if (emptyHere == null || !emptyHere.Contains(n, StringComparer.OrdinalIgnoreCase)) outside.Add(n);
            return outside;
        }

        /// <summary>
        /// What is said of another sheet whose model gives no value to some of the names
        /// the active sheet shows (N59), null when there are none. «alsoEmptyHere» counts
        /// the names it lacks that are empty on the active sheet too. Pure, so the contract
        /// suite reads it.
        /// </summary>
        internal static string OtherSheetWarning(
            string sheetName, string modelPath, List<string> missing, List<string> blank, bool oneOfSeveral,
            int alsoEmptyHere)
        {
            int m = missing == null ? 0 : missing.Count, b = blank == null ? 0 : blank.Count;
            if (m + b == 0) return null;
            var parts = new List<string>();
            if (m > 0) parts.Add(string.Join(", ", missing) + (m == 1 ? " is" : " are") + " not on it");
            if (b > 0) parts.Add(string.Join(", ", blank) + (b == 1 ? " is" : " are") + " there with no value");
            string alsoHere = alsoEmptyHere <= 0 ? ""
                : alsoEmptyHere == 1 ? " (one more it lacks is empty on this sheet too, see «requested_but_empty»)"
                : " (" + alsoEmptyHere + " more it lacks are empty on this sheet too, see «requested_but_empty»)";
            return "Sheet «" + sheetName + "» shows «" + modelPath + "»" +
                   (oneOfSeveral ? " (one of the models of its views)" : "") + ": " + string.Join(", and ", parts) +
                   ", and none of them is empty on this sheet" + alsoHere + ". With `$PRPSHEET` its title block " +
                   "takes them from the model in its views, so it shows them empty. Write them on that model with " +
                   "«set_property», or activate the sheet and call «title_block».";
        }

        /// <summary>
        /// What is said of another sheet with a different format (N59): its title block
        /// may ask for other names than those of the active sheet. Null when the formats
        /// are the same or one cannot be read. Pure, so the contract suite reads it.
        /// </summary>
        internal static string OtherFormatWarning(string sheetName, string format, string activeFormat)
        {
            if (string.IsNullOrWhiteSpace(format) || string.IsNullOrWhiteSpace(activeFormat)) return null;
            if (string.Equals(format.Trim(), activeFormat.Trim(), StringComparison.OrdinalIgnoreCase)) return null;
            return "Sheet «" + sheetName + "» has the format «" + format + "», not «" + activeFormat +
                   "» of the active sheet: its title block may ask for other names than those above. " +
                   "Activate it and call «title_block» to read them.";
        }

        /// <summary>The mark ConnectedNames puts on a name asked with `$PRPSHEET`.</summary>
        private const string FromThePart = "  (from the part, not from the drawing)";

        private static List<string> ConnectedNames(string text)
        {
            var outside = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return outside;
            foreach (Match m in Connection.Matches(text))
            {
                var itemName = m.Groups[2].Value;
                // `$PRPSHEET` takes the property from the MODEL in view, not
                // from the drawing: it is a different drawer, and confusing them makes
                // people write on the wrong document.
                if (m.Groups[1].Success) itemName = itemName + FromThePart;
                if (!outside.Contains(itemName, StringComparer.Ordinal)) outside.Add(itemName);
            }
            return outside;
        }

        private static string TestText(Func<string> f)
        {
            try { return f() ?? ""; } catch { return ""; }
        }
    }
}
