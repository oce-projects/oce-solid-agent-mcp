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
    ///     INote.GetText()                 ->  «Coperchio»            WHAT IT SHOWS
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

            var note = TitleBlockNotes(drawing, sheet, out string fromWhere);
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
                bool showNothing = string.IsNullOrWhiteSpace(show);
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
                fields.Add(entry);
            }

            var measurements = new Dictionary<string, object>
            {
                ["sheet"] = SheetName(sheet) ?? "",
                ["sheet_format"] = TestText(() => sheet.GetSheetFormatName()),
                ["notes_in_title_block"] = note.Count,
                ["notes_linked"] = connected,
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
            // With the earlier fields that red said only «non dice
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
            IDrawingDoc drawing, ISheet sheet, out string fromWhere)
        {
            fromWhere = null;
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
                            if (note != null) outside.Add(note);
                        }
                        catch { }
                        ann = ann.GetNext3() as IAnnotation;
                    }
                    if (outside.Count > 0) fromWhere = "among the notes of the sheet format";
                }
            }
            catch { }

            if (fromWhere == null) fromWhere = "in the title block group and among the notes of the format";
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
        /// on to say WHERE it comes from: «Descrizione  (dal pezzo, non dal
        /// disegno)» returns «Descrizione».
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
                if (m.Groups[1].Success) itemName = itemName + "  (from the part, not from the drawing)";
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
