using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE SHEETS OF A DRAWING (entry GT1).
    ///
    /// ── WHY IT IS THE SIMPLEST ENTRY AND NOT THE LEAST USEFUL ────────────
    ///
    /// `export_pdf` already exports all the sheets and **already counts the pages of the
    /// PDF**: it is the guard written because a one-sheet PDF, on a
    /// drawing of three, comes out with no error and arrives in the workshop incomplete.
    /// That count however today is always 1, **because there is no way to make
    /// a second sheet**. A guard that can never fire is not a
    /// guard: it is a line of code that consoles.
    ///
    /// ── THE DIHEDRAL, WHICH IS THE WHOLE DIFFICULTY ─────────────────────
    ///
    ///     Boolean NewSheet4(String Name, Int32 PaperSize, Int32 TemplateIn,
    ///                       Double Scale1, Double Scale2, Boolean FirstAngle,
    ///                       String TemplateName, Double Width, Double Height,
    ///                       String PropertyViewName, ...)
    ///
    /// **`FirstAngle` is a mandatory parameter, in sixth position: there is
    /// nothing to inherit.** Writing `false` because it is the default value
    /// would give **the same drawing with two sheets mirrored differently**, and
    /// neither would give an error: sheet 1 in first angle, sheet 2
    /// in third, and whoever cuts the part from sheet 2 cuts it upside down.
    ///
    /// What `set_sheet` already does is done: **the dihedral of the
    /// current sheet is read and passed on again**. The workshop's drawing template
    /// has already decided it once, and this is the only answer that invents
    /// nothing.
    ///
    /// ── AND THE `Boolean` IS NOT BELIEVED ───────────────────────────────
    ///
    /// *The value that comes back from whoever did the thing is not the measure of the
    /// thing.* The sheets are counted from `GetSheetNames().Length` before and after, and the
    /// dihedral of the new sheet is **re-read** from the new sheet.
    /// </summary>
    public partial class SwOperations
    {
        // ─────────────────────────────────────────────────────────────────
        // new_sheet
        // ─────────────────────────────────────────────────────────────────
        private Outcome NewSheet(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "The active document is not a drawing: the sheets are in a " +
                    "drawing. First create it with «create_drawing».");

            var namesBefore = SheetNames(drawing);
            if (namesBefore.Count == 0)
                return Failed("This drawing does not declare any sheet: I do not know what to start from.");

            string itemName = Str(a, "name", null);
            if (string.IsNullOrWhiteSpace(itemName)) itemName = FreeName(namesBefore);
            if (namesBefore.Any(n => string.Equals(n, itemName, StringComparison.OrdinalIgnoreCase)))
                return Failed(
                    "There is already a sheet called «" + itemName + "». The sheet names are " +
                    "what the pages of a drawing are called: two the same and you no longer " +
                    "know which one you are looking at. There are: " +
                    string.Join(", ", namesBefore) + ".");

            // ── THE DIHEDRAL IS READ FROM THE CURRENT SHEET ──────────────
            ISheet current;
            try { current = drawing.GetCurrentSheet() as ISheet; }
            catch (Exception ex) { return Failed("I cannot read the current sheet: " + ex.Message); }
            if (current == null) return Failed("The drawing has no active sheet.");

            var property = SheetProperty(current);
            if (property == null || property.Length <= 4)
                return Failed(
                    "I cannot read the projection angle of the current sheet, and without it " +
                    "the new sheet would be created with a projection angle decided by me. " +
                    "A second sheet mirrored with respect to the first gives no error and " +
                    "sends a reversed part to the workshop: I prefer to stop.");
            bool firstAngle = property[4] != 0;

            // The scale: if I am not told, that of the current sheet. Two
            // sheets of the same drawing at different scales may be wanted, but
            // not by distraction.
            var currentScale = ReadScale(current);
            double scaleNum = Num(a, "scale_numerator",
                currentScale != null && currentScale.Length > 0 ? currentScale[0] : 1);
            double scaleDen = Num(a, "scale_denominator",
                currentScale != null && currentScale.Length > 1 ? currentScale[1] : 1);
            if (scaleNum <= 0 || scaleDen <= 0)
                return Failed("The scale must have two positive numbers, for example 1:2.");

            string formatName = Str(a, "paper_size", null);
            int sheetFormat = string.IsNullOrWhiteSpace(formatName)
                ? SheetFormat(current, property)
                : SheetFormatCode(formatName);

            // ── THE FORMAT, FROM THE CURRENT SHEET (report 8cd843 N49, round 13) ──
            //
            // Until library 0.174.3 the new sheet had no format: swDwgTemplateNone, so no
            // frame and no title block (the second page of the PDF of round 13). Now, as
            // the projection angle, the format of the current sheet is passed on.
            // IDrawingDoc~NewSheet4.html: TemplateName is the «Name of custom template
            // with full directory path; valid only if TemplateIn is set to
            // swDwgTemplates_e.swDwgTemplateCustom», and PaperSize is «valid only if
            // TemplateIn is swDwgTemplates_e.swDwgTemplateNone». ISheet~GetTemplateName.html:
            // «If the sheet does not use a template ... this method returns "*.drt"».
            // Measured on a copy of the drawing of round 13 (27/10/2026): the first sheet
            // read its .slddrt (template-in 12), the second, made by the bridge, read ""
            // (template-in 13, none); NewSheet4 with swDwgTemplateCustom and the first
            // sheet's .slddrt gave a sheet that read the same .slddrt, with the 67 fields
            // of the title block among its notes. A «paper_size» asked keeps the sheet
            // without a format: the format of the current sheet is drawn for its own size.
            string currentTemplate = null;
            // The format is named as the installation names it, and opened from its local
            // copy when OCE_LOCAL_COPIES asks for it (LocalCopies.cs).
            try { currentTemplate = LocalCopies.Original(current.GetTemplateName()); } catch { }
            string format = string.IsNullOrWhiteSpace(formatName) ? SheetFormatFile(currentTemplate) : null;
            if (format != null && !System.IO.File.Exists(LocalCopies.Of(format)))
                return Failed(
                    "The current sheet uses the format «" + format + "», and I cannot find that file: the new sheet would " +
                    "have no frame and no title block. Make the file reachable and ask again, or give «paper_size» " +
                    "to create a sheet with no format on purpose. No sheet was created.");

            bool response;
            try
            {
                response = drawing.NewSheet4(
                    itemName,
                    sheetFormat,
                    format != null ? (int)swDwgTemplates_e.swDwgTemplateCustom : (int)swDwgTemplates_e.swDwgTemplateNone,
                    scaleNum, scaleDen,
                    firstAngle,               // ← PASSED ON AGAIN, not decided here
                    format != null ? LocalCopies.Of(format) : "",  // TemplateName: the format of the current sheet, or none
                    0, 0,                      // Width/Height: count only with the «custom» format
                    "",                        // PropertyViewName
                    0, 0, 0, 0, 0, 0);         // zones: not used
            }
            catch (Exception ex) { return Failed("Sheet not created: " + ex.Message); }

            // ── IT IS COUNTED, NOT BELIEVED ─────────────────────────────
            var namesAfter = SheetNames(drawing);
            if (namesAfter.Count <= namesBefore.Count)
                return Failed(
                    "It answered «" + (response ? "yes" : "no") + "» and the sheets are " +
                    "still " + namesBefore.Count + ". I do not call it done.");

            // AND THE DIHEDRAL OF THE NEW SHEET IS RE-READ FROM THE NEW SHEET.
            // It is the true defense of this operation: passing it is not having it.
            ISheet newItem = null;
            try { newItem = drawing.GetCurrentSheet() as ISheet; }
            catch { }
            bool firstAngleAfter = firstAngle;
            var propAfter = newItem == null ? null : SheetProperty(newItem);
            if (propAfter != null && propAfter.Length > 4) firstAngleAfter = propAfter[4] != 0;

            if (firstAngleAfter != firstAngle)
                return Failed(
                    "The sheet was created but with the wrong projection angle: I asked for the " +
                    (firstAngle ? "FIRST" : "THIRD") + " projection and the sheet says the " +
                    (firstAngleAfter ? "FIRST" : "THIRD") + ". Two sheets with different " +
                    "projection angles in the same drawing are a reversed part that gives " +
                    "no error: I do not say it is done.");

            // AND THE FORMAT OF THE NEW SHEET IS RE-READ FROM THE NEW SHEET (N49).
            string formatAfter = null;
            try { formatAfter = newItem == null ? null : SheetFormatFile(LocalCopies.Original(newItem.GetTemplateName())); } catch { }
            string formatWrong = NewSheetFormatProblem(format, formatAfter);
            if (formatWrong != null)
                return Failed(
                    "The sheet «" + itemName + "» was created, but " + formatWrong + ": a sheet with no frame and no " +
                    "title block goes to the workshop with no code, scale or sheet number. Remove it — " +
                    Sentences.HowToWithdrawIt + " — or put the format on it by hand.");

            var scaleAfter = newItem == null ? null : ReadScale(newItem);
            var sheetMeasurements = newItem == null ? null : SheetMeasurements(newItem);

            var measurements = new Dictionary<string, object>
            {
                ["name"] = itemName,
                ["sheets_before"] = namesBefore.Count,
                ["sheets_after"] = namesAfter.Count,
                ["sheets"] = namesAfter.ToArray(),
                ["active"] = SheetName(newItem) ?? itemName,
                ["projection_angle"] = firstAngleAfter ? "first" : "third",
                ["projection_angle_source"] = "read from the new sheet, after copying it from the current " +
                                     "sheet: it is not a value chosen here",
                ["scale"] = scaleAfter != null && scaleAfter.Length > 1
                    ? scaleAfter[0] + ":" + scaleAfter[1] : null,
                ["width_mm"] = sheetMeasurements != null ? (object)Math.Round(sheetMeasurements[0], 1) : null,
                ["height_mm"] = sheetMeasurements != null ? (object)Math.Round(sheetMeasurements[1], 1) : null,
                // The format (frame and title block), read from the new sheet (N49).
                ["format"] = formatAfter ?? "none",
                ["format_source"] = format != null
                    ? "read from the new sheet, after copying it from the current sheet"
                    : !string.IsNullOrWhiteSpace(formatName)
                        ? "none: «paper_size» was given, and the format of the current sheet is drawn for its own size"
                        : "none, as the current sheet",
            };

            if (!firstAngleAfter)
                _log("[agent] WARNING: this drawing is in THIRD angle projection, and so is the new sheet");
            _log("[agent] sheet «" + itemName + "»: the sheets are " + namesAfter.Count);
            return WithoutFeature(measurements);
        }

        // ─────────────────────────────────────────────────────────────────
        // activate_sheet
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// `ActivateSheet` takes **the NAME**, not an index — read in the
        /// signature, not remembered. An index would have been the usual chance
        /// to activate the wrong sheet with no error.
        /// </summary>
        private Outcome ActivateSheet(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null) return Failed("The active document is not a drawing.");

            var names = SheetNames(drawing);
            string wanted = Str(a, "name", null);
            if (string.IsNullOrWhiteSpace(wanted))
                return Failed(
                    "Tell me which sheet: «name», as the state reports it. There are: " +
                    string.Join(", ", names) + ".");

            var its = names.FirstOrDefault(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase));
            if (its == null)
                return Failed(
                    "There is no sheet called «" + wanted + "». There are: " +
                    string.Join(", ", names) + ".");

            string before = SheetName(CurrentSheet(drawing));

            bool response;
            try { response = drawing.ActivateSheet(its); }
            catch (Exception ex) { return Failed("Sheet not activated: " + ex.Message); }

            // THE ANSWER IS NOT BELIEVED: the document is asked which sheet
            // it has in front of it now.
            string after = SheetName(CurrentSheet(drawing));
            if (!string.Equals(after, its, StringComparison.OrdinalIgnoreCase))
                return Failed(
                    "It answered «" + (response ? "yes" : "no") + "» but the active " +
                    "sheet is «" + (after ?? "(not readable)") + "», not «" + its + "». " +
                    "I do not call it done: the steps after would work on the wrong sheet.");

            return WithoutFeature(new Dictionary<string, object>
            {
                ["active"] = after,
                ["previous"] = before,
                ["sheets"] = names.ToArray(),
                ["changed"] = !string.Equals(before, after, StringComparison.OrdinalIgnoreCase),
            });
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// The names of the sheets. `GetSheetNames()` returns `Object`: it must be cast, and a
        /// failed cast is an empty list, **not** zero sheets — whoever calls must
        /// tell «there are none» from «I could not read them», and that is why
        /// here it refuses when the list is empty instead of going
        /// on.
        /// </summary>
        private static List<string> SheetNames(IDrawingDoc drawing)
        {
            try
            {
                var names = drawing.GetSheetNames() as string[];
                return names == null ? new List<string>() : names.ToList();
            }
            catch { return new List<string>(); }
        }

        private static ISheet CurrentSheet(IDrawingDoc drawing)
        {
            try { return drawing.GetCurrentSheet() as ISheet; }
            catch { return null; }
        }

        private static string SheetName(ISheet sheet)
        {
            if (sheet == null) return null;
            try { return sheet.GetName(); } catch { return null; }
        }

        /// <summary>
        /// The format file (.slddrt) a sheet uses, from ISheet.GetTemplateName, or null
        /// when it uses none (N49): ISheet~GetTemplateName.html says «"*.drt"» for a sheet
        /// with no template, and a sheet made with swDwgTemplateNone read "" (measured).
        /// Pure, so the contract suite reads it.
        /// </summary>
        internal static string SheetFormatFile(string templateName)
        {
            if (string.IsNullOrWhiteSpace(templateName)) return null;
            string t = templateName.Trim();
            if (t == "*.drt" || !t.EndsWith(".slddrt", StringComparison.OrdinalIgnoreCase)) return null;
            return t;
        }

        /// <summary>
        /// What is wrong with the format the new sheet read back (N49), null when it is the
        /// one asked: <paramref name="asked"/> is the format passed (null = none asked),
        /// <paramref name="read"/> the one read from the new sheet. Pure, so the contract
        /// suite reads it.
        /// </summary>
        internal static string NewSheetFormatProblem(string asked, string read)
        {
            if (asked == null) return null;
            if (read == null) return "it has no format, while I passed «" + asked + "», the format of the current sheet";
            if (!string.Equals(asked, read, StringComparison.OrdinalIgnoreCase))
                return "its format reads «" + read + "», while I passed «" + asked + "», the format of the current sheet";
            return null;
        }

        /// <summary>
        /// The format of the current sheet, to give it the same to the new sheet.
        /// `GetProperties2()` puts the format in position 0.
        /// </summary>
        private static int SheetFormat(ISheet sheet, double[] property)
        {
            try
            {
                if (property != null && property.Length > 0)
                    return (int)Math.Round(property[0]);
            }
            catch { }
            return (int)swDwgPaperSizes_e.swDwgPaperA3size;
        }

        /// <summary>
        /// A name that is not already there, in the series SOLIDWORKS started: the
        /// word of the first existing sheet that ends in a number («Sheet1» on an
        /// English installation, its own word on another), then «Sheet» if no sheet
        /// has that shape. «Sheet2», «Sheet3»... and if by chance they exist, it goes
        /// on counting instead of refusing: whoever asks for a new sheet without
        /// giving it a name does not want to know that the default name was taken.
        /// Until W3.1-bis.11 the word was written here in Italian, so an English
        /// installation got a sheet named in another language.
        /// </summary>
        private static string FreeName(List<string> existing)
        {
            string word = "Sheet";
            foreach (var n in existing)
            {
                string s = n ?? "";
                int k = s.Length;
                while (k > 0 && char.IsDigit(s[k - 1])) k--;
                if (k >= 3 && k < s.Length && s.Substring(0, k).All(char.IsLetter)) { word = s.Substring(0, k); break; }
            }
            for (int i = existing.Count + 1; i < existing.Count + 100; i++)
            {
                var attempt = word + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!existing.Any(n => string.Equals(n, attempt, StringComparison.OrdinalIgnoreCase)))
                    return attempt;
            }
            return word + Guid.NewGuid().ToString("N").Substring(0, 4);
        }
    }
}
