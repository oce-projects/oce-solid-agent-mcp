using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swdimxpert;

namespace Riscontro.Disegno
{
    /// <summary>
    /// LE QUOTE NEL MODELLO, DA DIMXPERT (voce Z-H3).
    ///
    /// ── LA PREMESSA CHE ERA FALSA ─────────────────────────────────────────
    ///
    /// La voce diceva «serve la stessa spunta di Utilities». Non serve niente:
    /// `IModelDocExtension.get_DimXpertManager` sta nell'interop del NUCLEO, e
    /// `Controlli/DimXpert` il 24/9/2026 ha quotato una piastra con un foro
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
    public partial class OperazioniSw
    {
        private Esito SchemaDiQuote(IModelDoc2 modello, Dictionary<string, object> a, StatoDocumento stato)
        {
            var uid = Lista(a, "references_uid");
            if (uid.Count != 3)
                return Fallito(
                    "«references_uid» vuole TRE facce piane, in ordine: il riferimento primario " +
                    "(di solito la faccia d'appoggio), il secondario e il terziario (due fianchi " +
                    "fra loro perpendicolari). Ne ho ricevute " + uid.Count + ".");

            var facce = new List<IFace2>();
            foreach (var u in uid)
            {
                string motivo;
                var f = RisolviFaccia(stato, a, u, out motivo);
                if (f == null) return Fallito("Riferimento «" + u + "»: " + motivo);
                facce.Add(f);
            }

            string config = "";
            try { config = modello.ConfigurationManager.ActiveConfiguration.Name; } catch { }
            IDimXpertPart parte;
            try
            {
                var gestore = modello.Extension.get_DimXpertManager(config, true) as IDimXpertManager;
                parte = gestore?.DimXpertPart as IDimXpertPart;
            }
            catch (Exception ex) { return Fallito("DimXpert non risponde: " + ex.Message); }
            if (parte == null) return Fallito("DimXpert non ha dato la parte da quotare.");

            int prima = parte.GetAnnotationCount();

            modello.ClearSelection2(true);
            var selezione = modello.SelectionManager as ISelectionMgr;
            int[] marchi = { 1, 2, 4 };
            for (int i = 0; i < 3; i++)
                if (!Seleziona(selezione, facce[i], marchi[i]))
                    return Fallito("Non sono riuscito a selezionare «" + uid[i] + "».");

            var opzioni = parte.GetAutoDimSchemeOption();
            opzioni.PartType = (int)swDimXpertAutoDimSchemePartType_e.swDimXpertAutoDimSchemePartType_Prismatic;
            opzioni.ToleranceType = Str(a, "tolerance", "plus_minus") == "geometric"
                ? (int)swDimXpertAutoDimSchemeToleranceType_e.swDimXpertAutoDimSchemeToleranceType_Geometric
                : (int)swDimXpertAutoDimSchemeToleranceType_e.swDimXpertAutoDimSchemeToleranceType_PlusMinus;
            opzioni.ScopeAllFeature = true;
            opzioni.FeatureFilters = 0xFFFF;

            bool risposta;
            try { risposta = parte.AutoDimensionScheme(opzioni); }
            catch (Exception ex) { return Fallito("Lo schema di quote non e' partito: " + ex.Message); }
            finally { modello.ClearSelection2(true); }

            int dopo = parte.GetAnnotationCount();
            if (dopo <= prima)
                return Fallito(
                    "Lo schema non ha messo nessuna quota (" + prima + " -> " + dopo + "). Di " +
                    "solito i tre riferimenti non sono tre facce piane fra loro perpendicolari.");

            var quote = new List<object>();
            var valori = new List<object>();
            var ann = parte.GetAnnotations() as object[];
            foreach (var o in ann ?? new object[0])
            {
                var an = o as IDimXpertAnnotation;
                if (an == null) continue;
                quote.Add(an.Name);
                int ap = an.Name.LastIndexOf('('), ch = an.Name.LastIndexOf(')');
                double v;
                if (ap > 0 && ch > ap && double.TryParse(an.Name.Substring(ap + 1, ch - ap - 1),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    valori.Add(v);
            }
            valori.Sort((x, y) => ((double)x).CompareTo((double)y));

            return SenzaFeature(new Dictionary<string, object>
            {
                ["annotations_before"] = prima,
                ["annotations_after"] = dopo,
                ["dimensions"] = quote.Count <= 40 ? quote.ToArray() : quote.GetRange(0, 40).ToArray(),
                ["values_mm"] = valori.ToArray(),
                ["characteristics"] = parte.GetFeatureCount(),
                // LA RISPOSTA DELL'API, accanto alla misura e col suo nome: False
                // anche quando ha quotato. Non si nasconde, si nomina.
                ["auto_dimension_scheme_response"] = risposta,
            });
        }
    }
}
