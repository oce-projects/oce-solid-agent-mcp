using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// I FOGLI DI UNA TAVOLA (voce GT1).
    ///
    /// ── PERCHE' E' LA VOCE PIU' SEMPLICE E NON LA MENO UTILE ────────────
    ///
    /// `export_pdf` esporta gia' tutti i fogli e **conta gia' le pagine del
    /// PDF**: e' la guardia scritta perche' un PDF di un foglio solo, su una
    /// tavola di tre, esce senza nessun errore e arriva in officina incompleto.
    /// Quel conteggio pero' oggi vale sempre 1, **perche' non c'e' modo di fare
    /// un secondo foglio**. Una guardia che non puo' mai scattare non e' una
    /// guardia: e' una riga di codice che consola.
    ///
    /// ── IL DIEDRO, CHE E' TUTTA LA DIFFICOLTA' ──────────────────────────
    ///
    ///     Boolean NewSheet4(String Name, Int32 PaperSize, Int32 TemplateIn,
    ///                       Double Scale1, Double Scale2, Boolean FirstAngle,
    ///                       String TemplateName, Double Width, Double Height,
    ///                       String PropertyViewName, ...)
    ///
    /// **`FirstAngle` e' un parametro obbligatorio, in sesta posizione: non c'e'
    /// niente da ereditare.** Scrivere `false` perche' e' il valore di serie
    /// darebbe **la stessa tavola con due fogli specchiati in modo diverso**, e
    /// nessuno dei due darebbe errore: il foglio 1 in primo diedro, il foglio 2
    /// in terzo, e chi taglia il pezzo del foglio 2 lo taglia rovesciato.
    ///
    /// Si fa quello che `set_sheet` fa gia': **si legge il diedro del
    /// foglio corrente e lo si ripassa**. Il modello di tavola dell'officina
    /// l'ha gia' deciso una volta, e questa e' l'unica risposta che non inventa
    /// niente.
    ///
    /// ── E NON SI CREDE AL `Boolean` ─────────────────────────────────────
    ///
    /// *Il valore che torna da chi ha fatto la cosa non e' la misura della
    /// cosa.* Il foglio si conta da `GetSheetNames().Length` prima e dopo, e il
    /// diedro del foglio nuovo si **rilegge** dal foglio nuovo.
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

            // ── IL DIEDRO SI LEGGE DAL FOGLIO CORRENTE ──────────────────
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

            // La scala: se non me la dicono, quella del foglio corrente. Due
            // fogli della stessa tavola a scale diverse si possono volere, ma
            // non per distrazione.
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
                    firstAngle,               // ← RIPASSATO, non deciso qui
                    "",                        // TemplateName: nessuno
                    0, 0,                      // Width/Height: contano solo col formato «custom»
                    "",                        // PropertyViewName
                    0, 0, 0, 0, 0, 0);         // zone: non si usano
            }
            catch (Exception ex) { return Failed("Foglio non creato: " + ex.Message); }

            // ── SI CONTA, NON SI CREDE ──────────────────────────────────
            var namesAfter = SheetNames(drawing);
            if (namesAfter.Count <= namesBefore.Count)
                return Failed(
                    "Mi e' stato risposto «" + (response ? "si'" : "no") + "» e i fogli sono " +
                    "sempre " + namesBefore.Count + ". Non lo chiamo fatto.");

            // E IL DIEDRO DEL FOGLIO NUOVO SI RILEGGE DAL FOGLIO NUOVO.
            // E' la difesa vera di questa operazione: passarlo non e' averlo.
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
        /// `ActivateSheet` prende **il NOME**, non un indice — letto nella
        /// firma, non ricordato. Un indice sarebbe stato la solita occasione
        /// per attivare il foglio sbagliato senza nessun errore.
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

            // NON SI CREDE ALLA RISPOSTA: si richiede al documento quale foglio
            // ha davanti adesso.
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
        /// I nomi dei fogli. `GetSheetNames()` torna `Object`: va castato, e un
        /// cast fallito e' una lista vuota, **non** zero fogli — chi chiama deve
        /// distinguere «non ce ne sono» da «non li ho potuti leggere», ed e' per
        /// questo che qui si rifiuta quando la lista e' vuota invece di andare
        /// avanti.
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
        /// Il formato del foglio corrente, per darlo uguale al foglio nuovo.
        /// `GetProperties2()` mette il formato in posizione 0.
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
        /// Un nome che non c'e' gia'. «Foglio2», «Foglio3»... e se per caso
        /// esistono, si va avanti a contare invece di rifiutare: chi chiede un
        /// foglio nuovo senza dargli un nome non vuole sapere che il nome di
        /// serie era occupato.
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
