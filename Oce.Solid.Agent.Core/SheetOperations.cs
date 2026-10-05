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
                    "Il documento attivo non e' una tavola: i fogli stanno in una " +
                    "tavola. Creala prima con «create_drawing».");

            var namesBefore = SheetNames(drawing);
            if (namesBefore.Count == 0)
                return Failed("Questa tavola non dichiara nessun foglio: non so da cosa partire.");

            string itemName = Str(a, "name", null);
            if (string.IsNullOrWhiteSpace(itemName)) itemName = FreeName(namesBefore);
            if (namesBefore.Any(n => string.Equals(n, itemName, StringComparison.OrdinalIgnoreCase)))
                return Failed(
                    "C'e' gia' un foglio che si chiama «" + itemName + "». I nomi dei fogli " +
                    "sono come si chiamano le pagine di una tavola: due uguali e non si " +
                    "sa piu' quale si sta guardando. Ci sono: " +
                    string.Join(", ", namesBefore) + ".");

            // ── THE DIHEDRAL IS READ FROM THE CURRENT SHEET ──────────────
            ISheet current;
            try { current = drawing.GetCurrentSheet() as ISheet; }
            catch (Exception ex) { return Failed("Non riesco a leggere il foglio corrente: " + ex.Message); }
            if (current == null) return Failed("La tavola non ha un foglio attivo.");

            var property = SheetProperty(current);
            if (property == null || property.Length <= 4)
                return Failed(
                    "Non riesco a leggere il diedro del foglio corrente, e senza quello " +
                    "il foglio nuovo nascerebbe con un diedro deciso da me. Un secondo " +
                    "foglio specchiato rispetto al primo non da' nessun errore e manda " +
                    "in officina un pezzo rovesciato: preferisco fermarmi.");
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
                return Failed("La scala deve avere due numeri positivi, per esempio 1:2.");

            string formatName = Str(a, "paper_size", null);
            int sheetFormat = string.IsNullOrWhiteSpace(formatName)
                ? SheetFormat(current, property)
                : SheetFormatCode(formatName);

            bool response;
            try
            {
                response = drawing.NewSheet4(
                    itemName,
                    sheetFormat,
                    (int)swDwgTemplates_e.swDwgTemplateNone,
                    scaleNum, scaleDen,
                    firstAngle,               // ← PASSED ON AGAIN, not decided here
                    "",                        // TemplateName: none
                    0, 0,                      // Width/Height: count only with the «custom» format
                    "",                        // PropertyViewName
                    0, 0, 0, 0, 0, 0);         // zones: not used
            }
            catch (Exception ex) { return Failed("Foglio non creato: " + ex.Message); }

            // ── IT IS COUNTED, NOT BELIEVED ─────────────────────────────
            var namesAfter = SheetNames(drawing);
            if (namesAfter.Count <= namesBefore.Count)
                return Failed(
                    "Mi e' stato risposto «" + (response ? "si'" : "no") + "» e i fogli sono " +
                    "sempre " + namesBefore.Count + ". Non lo chiamo fatto.");

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
                    "Il foglio e' nato ma col diedro sbagliato: ho chiesto il " +
                    (firstAngle ? "PRIMO" : "TERZO") + " e il foglio dice il " +
                    (firstAngleAfter ? "PRIMO" : "TERZO") + ". Due fogli con diedri diversi " +
                    "nella stessa tavola sono un pezzo rovesciato che non da' nessun " +
                    "errore: non dico che e' fatto.");

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
                ["projection_angle_source"] = "riletto dal foglio nuovo, dopo averlo copiato dal foglio " +
                                     "corrente: non e' un valore scelto qui",
                ["scale"] = scaleAfter != null && scaleAfter.Length > 1
                    ? scaleAfter[0] + ":" + scaleAfter[1] : null,
                ["width_mm"] = sheetMeasurements != null ? (object)Math.Round(sheetMeasurements[0], 1) : null,
                ["height_mm"] = sheetMeasurements != null ? (object)Math.Round(sheetMeasurements[1], 1) : null,
            };

            if (!firstAngleAfter)
                _log("[agente] ATTENZIONE: questa tavola e' in TERZO diedro, e anche il foglio nuovo");
            _log("[agente] foglio «" + itemName + "»: i fogli sono " + namesAfter.Count);
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
            if (drawing == null) return Failed("Il documento attivo non e' una tavola.");

            var names = SheetNames(drawing);
            string wanted = Str(a, "name", null);
            if (string.IsNullOrWhiteSpace(wanted))
                return Failed(
                    "Dimmi quale foglio: «name», come lo riporta lo stato. Ci sono: " +
                    string.Join(", ", names) + ".");

            var its = names.FirstOrDefault(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase));
            if (its == null)
                return Failed(
                    "Non c'e' nessun foglio che si chiami «" + wanted + "». Ci sono: " +
                    string.Join(", ", names) + ".");

            string before = SheetName(CurrentSheet(drawing));

            bool response;
            try { response = drawing.ActivateSheet(its); }
            catch (Exception ex) { return Failed("Foglio non attivato: " + ex.Message); }

            // THE ANSWER IS NOT BELIEVED: the document is asked which sheet
            // it has in front of it now.
            string after = SheetName(CurrentSheet(drawing));
            if (!string.Equals(after, its, StringComparison.OrdinalIgnoreCase))
                return Failed(
                    "Mi e' stato risposto «" + (response ? "si'" : "no") + "» ma il foglio " +
                    "attivo e' «" + (after ?? "(non leggibile)") + "», non «" + its + "». " +
                    "Non lo chiamo fatto: i passi dopo lavorerebbero sul foglio sbagliato.");

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
        /// A name that is not already there. «Foglio2», «Foglio3»... and if by chance
        /// they exist, it goes on counting instead of refusing: whoever asks for a
        /// new sheet without giving it a name does not want to know that the default
        /// name was taken.
        /// </summary>
        private static string FreeName(List<string> existing)
        {
            for (int i = existing.Count + 1; i < existing.Count + 100; i++)
            {
                var attempt = "Foglio" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!existing.Any(n => string.Equals(n, attempt, StringComparison.OrdinalIgnoreCase)))
                    return attempt;
            }
            return "Foglio" + Guid.NewGuid().ToString("N").Substring(0, 4);
        }
    }
}
