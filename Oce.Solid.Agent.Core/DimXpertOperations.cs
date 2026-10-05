using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swdimxpert;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// THE DIMENSIONS IN THE MODEL, FROM DIMXPERT (entry Z-H3).
    ///
    /// ── THE PREMISE THAT WAS FALSE ────────────────────────────────────────
    ///
    /// The entry said «the same tick as Utilities is needed». Nothing is needed:
    /// `IModelDocExtension.get_DimXpertManager` is in the interop of the CORE, and
    /// `Checks/DimXpert` on 24/9/2026 dimensioned a plate with a hole
    /// without any add-in — Ø20, 10, 60, 100, and the hole at 50 and 30.
    ///
    /// ── TWO THINGS THAT ARE KNOWN ONLY BY MEASURING ───────────────────────
    ///
    ///  1. **The references are SELECTED**, with a mark: 1 the primary, 2 the
    ///     secondary, 4 the tertiary. The options of the scheme have no fields
    ///     for them. Without a selection the scheme puts ONE dimension on three faces.
    ///  2. **`AutoDimensionScheme` answers False even when it has dimensioned.**
    ///     It is judged from the annotations, before and after — never from the answer.
    ///
    /// The dimensions are born in the MODEL (MBD), not in a drawing: a drawing
    /// view can then show them, but that is another operation.
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
                // THE ANSWER OF THE API, next to the measurement and with its name: False
                // even when it has dimensioned. It is not hidden, it is named.
                ["auto_dimension_scheme_response"] = response,
            });
        }
    }
}
