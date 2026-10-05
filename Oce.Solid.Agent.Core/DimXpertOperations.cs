using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swdimxpert;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// LE QUOTE NEL MODELLO, DA DIMXPERT (voce Z-H3).
    ///
    /// ── LA PREMESSA CHE ERA FALSA ─────────────────────────────────────────
    ///
    /// La voce diceva «serve la stessa spunta di Utilities». Non serve niente:
    /// `IModelDocExtension.get_DimXpertManager` sta nell'interop del NUCLEO, e
    /// `Checks/DimXpert` il 24/9/2026 ha quotato una piastra con un foro
    /// senza nessun add-in — Ø20, 10, 60, 100, e il foro a 50 e 30.
    ///
    /// ── DUE COSE CHE SI SANNO SOLO MISURANDO ──────────────────────────────
    ///
    ///  1. **I riferimenti si SELEZIONANO**, con un marchio: 1 il primario, 2 il
    ///     secondario, 4 il terziario. Le opzioni dello schema non hanno campi
    ///     per loro. Senza selezione lo schema mette UNA quota su tre facce.
    ///  2. **`AutoDimensionScheme` risponde False anche quando ha quotato.**
    ///     Si giudica dalle annotazioni, prima e dopo — mai dalla risposta.
    ///
    /// Le quote nascono nel MODELLO (MBD), non in una tavola: una vista di
    /// tavola le puo' poi mostrare, ma quella e' un'altra operazione.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome DimensionScheme(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            var uid = StringList(a, "references_uid");
            if (uid.Count != 3)
                return Failed(
                    "«references_uid» vuole TRE facce piane, in ordine: il riferimento primario " +
                    "(di solito la faccia d'appoggio), il secondario e il terziario (due fianchi " +
                    "fra loro perpendicolari). Ne ho ricevute " + uid.Count + ".");

            var faces = new List<IFace2>();
            foreach (var u in uid)
            {
                string reason;
                var f = ResolveFace(docState, a, u, out reason);
                if (f == null) return Failed("Riferimento «" + u + "»: " + reason);
                faces.Add(f);
            }

            string config = "";
            try { config = model.ConfigurationManager.ActiveConfiguration.Name; } catch { }
            IDimXpertPart partDoc;
            try
            {
                var handler = model.Extension.get_DimXpertManager(config, true) as IDimXpertManager;
                partDoc = handler?.DimXpertPart as IDimXpertPart;
            }
            catch (Exception ex) { return Failed("DimXpert non risponde: " + ex.Message); }
            if (partDoc == null) return Failed("DimXpert non ha dato la parte da quotare.");

            int before = partDoc.GetAnnotationCount();

            model.ClearSelection2(true);
            var selection = model.SelectionManager as ISelectionMgr;
            int[] marks = { 1, 2, 4 };
            for (int i = 0; i < 3; i++)
                if (!Select(selection, faces[i], marks[i]))
                    return Failed("Non sono riuscito a selezionare «" + uid[i] + "».");

            var options = partDoc.GetAutoDimSchemeOption();
            options.PartType = (int)swDimXpertAutoDimSchemePartType_e.swDimXpertAutoDimSchemePartType_Prismatic;
            options.ToleranceType = Str(a, "tolerance", "plus_minus") == "geometric"
                ? (int)swDimXpertAutoDimSchemeToleranceType_e.swDimXpertAutoDimSchemeToleranceType_Geometric
                : (int)swDimXpertAutoDimSchemeToleranceType_e.swDimXpertAutoDimSchemeToleranceType_PlusMinus;
            options.ScopeAllFeature = true;
            options.FeatureFilters = 0xFFFF;

            bool response;
            try { response = partDoc.AutoDimensionScheme(options); }
            catch (Exception ex) { return Failed("Lo schema di quote non e' partito: " + ex.Message); }
            finally { model.ClearSelection2(true); }

            int after = partDoc.GetAnnotationCount();
            if (after <= before)
                return Failed(
                    "Lo schema non ha messo nessuna quota (" + before + " -> " + after + "). Di " +
                    "solito i tre riferimenti non sono tre facce piane fra loro perpendicolari.");

            var quote = new List<object>();
            var values = new List<object>();
            var ann = partDoc.GetAnnotations() as object[];
            foreach (var o in ann ?? new object[0])
            {
                var an = o as IDimXpertAnnotation;
                if (an == null) continue;
                quote.Add(an.Name);
                int ap = an.Name.LastIndexOf('('), ch = an.Name.LastIndexOf(')');
                double v;
                if (ap > 0 && ch > ap && double.TryParse(an.Name.Substring(ap + 1, ch - ap - 1),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    values.Add(v);
            }
            values.Sort((x, y) => ((double)x).CompareTo((double)y));

            return WithoutFeature(new Dictionary<string, object>
            {
                ["annotations_before"] = before,
                ["annotations_after"] = after,
                ["dimensions"] = quote.Count <= 40 ? quote.ToArray() : quote.GetRange(0, 40).ToArray(),
                ["values_mm"] = values.ToArray(),
                ["characteristics"] = partDoc.GetFeatureCount(),
                // LA RISPOSTA DELL'API, accanto alla misura e col suo nome: False
                // anche quando ha quotato. Non si nasconde, si nomina.
                ["auto_dimension_scheme_response"] = response,
            });
        }
    }
}
