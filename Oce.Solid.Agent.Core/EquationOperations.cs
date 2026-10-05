using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// VARIABILI GLOBALI ED EQUAZIONI (voce Z-F1): non ripetere un numero,
    /// dargli un nome. `parete = 3mm`, e la quota guidata da `parete` sopravvive a
    /// una modifica a monte per costruzione — e' il sistema di identita' che
    /// SOLIDWORKS ha gia', e un modello parametrico e' l'unico che chi non
    /// progetta puo' cambiare senza rompere niente.
    ///
    /// Misurato il 27/9/2026 (`prova0089 zf1`, `zf1b`, piastra 120x80x10):
    ///
    ///  * `IEquationMgr.Add3(-1, "\"parete\" = 12mm", true, swAllConfiguration,
    ///    null)` torna l'indice (0); `"\"D1@Estrusione-Estrusione1\" = \"parete\""`
    ///    lega lo spessore: volume 96000 -> 115200. `set_Equation` a 15mm, poi
    ///    `EvaluateAll` e ricostruzione: 144000, e la quota dice 15.
    ///  * Le espressioni vanno: `"doppio" = 2 * "parete" + 1mm` vale 31.
    ///  * **«spessore» e «Spessore» sono RIFIUTATI** (Add3 -1) anche col pezzo
    ///    senza proprieta': per il SOLIDWORKS in italiano e' un nome gia' preso, e
    ///    un'equazione che lo usa vale 3. Un nome rifiutato si dice, e si propone
    ///    un altro nome.
    ///  * `get_Status` NON e' affidabile: -1 anche dopo un EvaluateAll riuscito,
    ///    1 dopo un set_Equation riuscito. L'esito si legge dai VALORI: quello
    ///    dell'equazione e quello della quota, riletti.
    ///  * Il nome di una quota per l'equazione e' «D1@Estrusione-Estrusione1»:
    ///    il `FullName` senza il pezzo in coda (`@Parte1089.Part`).
    /// </summary>
    public partial class SwOperations
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>L'indice dell'equazione che assegna «name», o -1.</summary>
        private static int EquationOf(IEquationMgr em, string itemName)
        {
            string head = "\"" + itemName + "\"";
            for (int i = 0; i < em.GetCount(); i++)
            {
                string eq = "";
                try { eq = em.get_Equation(i) ?? ""; } catch { }
                int equal = eq.IndexOf('=');
                if (equal > 0 && string.Equals(eq.Substring(0, equal).Trim(), head, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>Scrive (o riscrive) l'equazione «name» = destra; l'indice, o -1 se SOLIDWORKS la rifiuta.</summary>
        private static int WriteEquation(IEquationMgr em, string itemName, string right)
        {
            string eq = "\"" + itemName + "\" = " + right;
            int i = EquationOf(em, itemName);
            if (i >= 0)
            {
                string old = em.get_Equation(i);
                try { em.set_Equation(i, eq); } catch { return -1; }
                // SI RILEGGE: set_Equation non risponde, e Status non si legge.
                if (!string.Equals(em.get_Equation(i), eq, StringComparison.Ordinal))
                {
                    try { em.set_Equation(i, old); } catch { }
                    return -1;
                }
                return i;
            }
            try { return em.Add3(-1, eq, true, (int)swInConfigurationOpts_e.swAllConfiguration, null); }
            catch { return -1; }
        }

        private static bool GoodVariableName(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName) || itemName.Length > 40) return false;
            if (!char.IsLetter(itemName[0])) return false;
            foreach (var ch in itemName) if (!(char.IsLetterOrDigit(ch) || ch == '_')) return false;
            return true;
        }

        /// <summary>Z-F1: crea o cambia una variabile globale, e rilegge cosa guida.</summary>
        private Outcome GlobalVariable(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (!(model is IPartDoc))
                return Failed("Le variabili globali le metto su un pezzo (le parti le quotano). Aprimi il pezzo.");
            string itemName = (Str(a, "name", "") ?? "").Trim();
            if (!GoodVariableName(itemName))
                return Failed("«name» e' il nome della variabile: lettere, cifre e «_», comincia con una lettera " +
                               "(per esempio «parete», «gioco», «raggio_angolo»), non «" + itemName + "».");
            double val; string error;
            string expression = Str(a, "expression", null);
            bool number = a != null && a.ContainsKey("value_mm");
            if (number == !string.IsNullOrWhiteSpace(expression))
                return Failed("Dimmi O «value_mm» (un numero in millimetri) O «expression» (per esempio " +
                               "«2 * \"parete\" + 1mm»), uno dei due.");
            string right;
            if (number)
            {
                if (!RequiredNum(a, "value_mm", out val, out error)) return Failed(error);
                right = val.ToString("0.######", Inv) + "mm";
            }
            else { val = double.NaN; right = expression.Trim(); }

            var em = model.GetEquationMgr() as IEquationMgr;
            if (em == null) return Failed("Non leggo le equazioni di questo pezzo (GetEquationMgr non risponde).");
            // LE GUARDIE DI set_dimension (revisione avversariale del 27/9/2026):
            // una variabile che guida delle quote cambia la geometria come una
            // quota, e una quota legata set_dimension non la cambia piu'.
            var photo = SnapshotPart(model);
            if (photo.Bodies < 0)
                return Failed("Non riesco a contare i corpi del pezzo prima di cambiare la variabile: senza, non " +
                               "saprei dirti se la modifica lo spezza. Non ho toccato niente.");
            double? volumeBefore = photo.Volume;
            int existing = EquationOf(em, itemName);
            bool newOne = existing < 0;
            string old = null;
            if (!newOne) { try { old = em.get_Equation(existing); } catch { } }
            int i = WriteEquation(em, itemName, right);
            if (i < 0)
                return Failed(
                    "SOLIDWORKS non accetta «\"" + itemName + "\" = " + right + "». Se il nome e' gia' preso — per il " +
                    "SOLIDWORKS in italiano «spessore» lo e' (misurato) — scegline un altro, per esempio «parete»; " +
                    "se e' un'espressione, controlla che i nomi fra virgolette esistano.");
            try { em.EvaluateAll(); } catch { }
            bool rebuilt = false;
            try { rebuilt = model.ForceRebuild3(false); } catch { }
            i = EquationOf(em, itemName);
            double read = double.NaN;
            try { if (i >= 0) read = em.get_Value(i); } catch { }
            string what = newOne ? "le equazioni senza «" + itemName + "»" : "«" + old + "»";
            if (i < 0 || double.IsNaN(read))
                return Failed("La variabile «" + itemName + "» non si rilegge dopo averla scritta: non so che valore ha." +
                               IsBackAsItWas(model, photo, PutBackEquation(model, em, itemName, old, null), what));
            // IL VALORE E' NELLE UNITA' DEL DOCUMENTO: in millimetri si confronta.
            double factor = MmPerDocumentUnit(model);
            double readMm = double.IsNaN(factor) ? double.NaN : read * factor;
            if (number && !double.IsNaN(readMm) && Math.Abs(readMm - val) > 1e-6)
                return Failed("Ho scritto «" + itemName + "» = " + val.ToString("0.###", Inv) + " mm e rileggendola vale " +
                               readMm.ToString("0.###", Inv) + " mm: non e' la variabile che volevi." +
                               IsBackAsItWas(model, photo, PutBackEquation(model, em, itemName, old, null), what));
            string broken = WhatBroke(model, photo, rebuilt, true);
            if (broken != null)
                return Failed("Con «" + itemName + "» = " + right + " " + broken + "." +
                               IsBackAsItWas(model, photo, PutBackEquation(model, em, itemName, old, null), what) +
                               " Se vuoi proprio quel valore, sistema prima la lavorazione che non ci sta, poi cambia la variabile.");
            double? volumeAfter = VolumeMm3(model);
            var measurements = new Dictionary<string, object>
            {
                ["name"] = itemName,
                ["equation"] = em.get_Equation(i),
                ["value_mm"] = double.IsNaN(readMm) ? null : (object)Math.Round(readMm, 6),
                ["is_new"] = newOne,
                ["driven_dimensions"] = DimensionsDrivenBy(em, itemName),
                ["volume_before_mm3"] = NumberOrNull(volumeBefore, 2),
                ["volume_after_mm3"] = NumberOrNull(volumeAfter, 2),
                ["units"] = "millimetri (il valore di una variabile di lunghezza, riletto nelle unita' del documento e portato in mm)",
            };
            if (double.IsNaN(readMm))
                measurements["warning"] = "Non so in che unita' e' il documento: la variabile vale " + read.ToString("0.######", Inv) +
                                       " nelle sue unita', e non l'ho confrontata col valore chiesto.";
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>Z-F1: lega una quota a un'espressione (una variabile, un conto), e la rilegge.</summary>
        private Outcome LinkDimension(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (!(model is IPartDoc))
                return Failed("Lego le quote di un pezzo. Aprimi il pezzo.");
            string dimensionName = (Str(a, "dimension", "") ?? "").Trim();
            string expression = (Str(a, "expression", "") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(dimensionName))
                return Failed("Dimmi «dimension»: il nome della quota dallo stato del documento (per esempio «D1@Schizzo1»).");
            if (string.IsNullOrWhiteSpace(expression))
                return Failed("Dimmi «expression»: a cosa legarla, per esempio «\"parete\"» o «2 * \"parete\"».");
            var dimension = FindDimension(model, dimensionName, out string location);
            if (dimension == null)
                return Failed("Non trovo la quota «" + dimensionName + "» in questo documento." + (location == null ? "" : " Ci sono: " + location + "."));
            // IL NOME PER L'EQUAZIONE: il FullName senza il pezzo in coda.
            string complete = "";
            try { complete = dimension.FullName ?? ""; } catch { }
            var parts = complete.Split('@');
            if (parts.Length < 2)
                return Failed("Non leggo il nome completo della quota «" + dimensionName + "» («" + complete + "»): senza, " +
                               "l'equazione non saprei scriverla.");
            string eqName = parts[0] + "@" + parts[1];
            var em = model.GetEquationMgr() as IEquationMgr;
            if (em == null) return Failed("Non leggo le equazioni di questo pezzo (GetEquationMgr non risponde).");
            // UNA QUOTA ANGOLARE si legge in gradi, una lineare in mm: SystemValue e'
            // in radianti o in metri, e il confronto con l'equazione (nelle unita'
            // del documento) si fa con GetValue3, che e' nelle stesse unita'.
            int dimensionType = -1;
            try { dimensionType = dimension.GetType(); } catch { }
            bool angular = dimensionType == (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular;
            double systemBefore = ApiHelpers.ReadDimension(dimension);
            var photo = SnapshotPart(model);
            if (photo.Bodies < 0)
                return Failed("Non riesco a contare i corpi del pezzo prima di legare la quota: senza, non saprei " +
                               "dirti se il legame lo spezza. Non ho toccato niente.");
            double? volumeBefore = photo.Volume;
            int existing = EquationOf(em, eqName);
            string old = null;
            if (existing >= 0) { try { old = em.get_Equation(existing); } catch { } }
            int i = WriteEquation(em, eqName, expression);
            if (i < 0)
                return Failed("SOLIDWORKS non accetta «\"" + eqName + "\" = " + expression + "». Controlla che le " +
                               "variabili fra virgolette esistano (le crei con «global_variable»).");
            try { em.EvaluateAll(); } catch { }
            bool rebuilt = false;
            try { rebuilt = model.ForceRebuild3(false); } catch { }
            i = EquationOf(em, eqName);
            double expected = double.NaN;
            try { if (i >= 0) expected = em.get_Value(i); } catch { }
            double afterDoc = double.NaN;
            try { afterDoc = ((double[])dimension.GetValue3((int)swInConfigurationOpts_e.swThisConfiguration, null))[0]; } catch { }
            // Rimettere: l'equazione di prima, o nessuna — e allora anche il valore di
            // prima, perche' una quota slegata tiene l'ultimo valore che l'equazione le ha dato.
            Func<bool> putBack = () => PutBackEquation(model, em, eqName, old,
                old != null ? null : new Action(() => dimension.SetSystemValue3(systemBefore, 2, null)));
            string what = old != null ? "«" + old + "»" : "la quota slegata, col suo valore di prima";
            if (double.IsNaN(expected) || double.IsNaN(afterDoc) || Math.Abs(afterDoc - expected) > 0.001)
                return Failed("Ho legato «" + eqName + "» a «" + expression + "», ma la quota dice " +
                               (double.IsNaN(afterDoc) ? "?" : afterDoc.ToString("0.###", Inv)) + " e l'equazione " +
                               (double.IsNaN(expected) ? "non si rilegge" : expected.ToString("0.###", Inv)) +
                               " (nelle unita' del documento): il legame non ha preso." +
                               IsBackAsItWas(model, photo, putBack(), what));
            string broken = WhatBroke(model, photo, rebuilt, true);
            if (broken != null)
                return Failed("Legando «" + eqName + "» a «" + expression + "» " + broken + "." +
                               IsBackAsItWas(model, photo, putBack(), what));
            double? volumeAfter = VolumeMm3(model);
            double afterSystem = ApiHelpers.ReadDimension(dimension);
            var measurements = new Dictionary<string, object>
            {
                ["dimension"] = eqName,
                ["equation"] = em.get_Equation(i),
                ["volume_before_mm3"] = NumberOrNull(volumeBefore, 2),
                ["volume_after_mm3"] = NumberOrNull(volumeAfter, 2),
                ["verdict"] = "La quota «" + eqName + "» adesso la guida «" + expression + "»: cambiando la variabile " +
                               "cambia lei. Con «set_dimension» non si cambia piu' (e' guidata).",
            };
            if (angular)
            {
                measurements["value_before_deg"] = Math.Round(systemBefore * 180.0 / Math.PI, 4);
                measurements["value_after_deg"] = Math.Round(afterSystem * 180.0 / Math.PI, 4);
            }
            else
            {
                measurements["value_before_mm"] = Math.Round(Units.Mm(systemBefore), 4);
                measurements["value_after_mm"] = Math.Round(Units.Mm(afterSystem), 4);
            }
            // UN LEGAME CHE NE SOSTITUISCE UN ALTRO SI DICE (revisione del 27/9/2026):
            // prima si riscriveva in silenzio.
            if (old != null) measurements["link_replaced"] = old;
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// Rimette le equazioni com'erano: la vecchia riscritta, o la nuova tolta
        /// (con «after», per rimettere il valore di una quota slegata); poi ricalcola
        /// e ricostruisce. Vero se RILEGGENDO le equazioni sono quelle di prima —
        /// il numero che Delete risponde non si crede.
        /// </summary>
        private static bool PutBackEquation(IModelDoc2 model, IEquationMgr em, string itemName, string old, Action after)
        {
            bool done = false;
            try
            {
                int i = EquationOf(em, itemName);
                if (old != null)
                {
                    if (i >= 0)
                    {
                        em.set_Equation(i, old);
                        done = string.Equals(em.get_Equation(i), old, StringComparison.Ordinal);
                    }
                }
                else
                {
                    if (i >= 0) em.Delete(i);
                    done = EquationOf(em, itemName) < 0;
                }
                try { em.EvaluateAll(); } catch { }
                if (after != null) after();
                model.ForceRebuild3(false);
            }
            catch { }
            return done;
        }

        /// <summary>Le equazioni che usano «name» a destra, con la quota che guidano e il suo valore.</summary>
        private static object[] DimensionsDrivenBy(IEquationMgr em, string itemName)
        {
            var outside = new List<object>();
            string searched = "\"" + itemName + "\"";
            for (int i = 0; i < em.GetCount(); i++)
            {
                string eq = "";
                try { eq = em.get_Equation(i) ?? ""; } catch { }
                int equal = eq.IndexOf('=');
                if (equal < 0) continue;
                string left = eq.Substring(0, equal).Trim().Trim('"');
                if (eq.Substring(equal + 1).IndexOf(searched, StringComparison.OrdinalIgnoreCase) < 0) continue;
                double v = double.NaN;
                try { v = em.get_Value(i); } catch { }
                outside.Add(new Dictionary<string, object>
                {
                    ["driven"] = left,
                    ["equation"] = eq,
                    ["value_text"] = double.IsNaN(v) ? null : (object)Math.Round(v, 6),
                });
            }
            return outside.ToArray();
        }

        /// <summary>
        /// Per lo stato: le equazioni del documento (le prime venti), o null se non ce
        /// ne sono; in «total» quante sono davvero, perche' chi legge sappia che
        /// l'elenco e' tagliato (revisione del 27/9/2026: le ultime quote guidate
        /// sparivano senza dirlo). Il «value_text» e' nelle unita' del documento.
        /// </summary>
        internal static object[] DocumentEquations(IModelDoc2 model, out int total)
        {
            total = 0;
            try
            {
                var em = model.GetEquationMgr() as IEquationMgr;
                if (em == null) return null;
                int n = em.GetCount();
                total = n;
                if (n <= 0) return null;
                var outside = new List<object>();
                for (int i = 0; i < n && i < 20; i++)
                {
                    double v = double.NaN; bool g = false; string eq = "";
                    try { eq = em.get_Equation(i) ?? ""; } catch { }
                    try { v = em.get_Value(i); } catch { }
                    try { g = em.get_GlobalVariable(i); } catch { }
                    outside.Add(new Dictionary<string, object>
                    {
                        ["equation"] = eq,
                        ["value_text"] = double.IsNaN(v) ? null : (object)Math.Round(v, 6),
                        ["global_variable"] = g,
                    });
                }
                return outside.ToArray();
            }
            catch { return null; }
        }
    }
}
