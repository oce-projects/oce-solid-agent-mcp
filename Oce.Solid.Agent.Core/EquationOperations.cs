using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// GLOBAL VARIABLES AND EQUATIONS (entry Z-F1): not repeating a number,
    /// giving it a name. `parete = 3mm`, and the dimension driven by `parete` survives
    /// an upstream change by construction — it is the identity system that
    /// SOLIDWORKS already has, and a parametric model is the only kind that someone who does not
    /// design can change without breaking anything.
    ///
    /// Measured on 27/9/2026 (`prova0089 zf1`, `zf1b`, plate 120x80x10):
    ///
    ///  * `IEquationMgr.Add3(-1, "\"parete\" = 12mm", true, swAllConfiguration,
    ///    null)` returns the index (0); `"\"D1@Estrusione-Estrusione1\" = \"parete\""`
    ///    links the thickness: volume 96000 -> 115200. `set_Equation` to 15mm, then
    ///    `EvaluateAll` and rebuild: 144000, and the dimension says 15.
    ///  * Expressions work: `"doppio" = 2 * "parete" + 1mm` is worth 31.
    ///  * **«spessore» and «Spessore» are REFUSED** (Add3 -1) even with the part
    ///    without properties: for the Italian SOLIDWORKS it is a name already taken, and
    ///    an equation that uses it is worth 3. A refused name is stated, and
    ///    another name is proposed.
    ///  * `get_Status` is NOT reliable: -1 even after a successful EvaluateAll,
    ///    1 after a successful set_Equation. The outcome is read from the VALUES: that
    ///    of the equation and that of the dimension, read back.
    ///  * The name of a dimension for the equation is «D1@Estrusione-Estrusione1»:
    ///    the `FullName` without the part at the tail (`@Parte1089.Part`).
    /// </summary>
    public partial class SwOperations
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The index of the equation that assigns «name», or -1.</summary>
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

        /// <summary>Writes (or rewrites) the equation «name» = right-hand side; the index, or -1 if SOLIDWORKS refuses it.</summary>
        private static int WriteEquation(IEquationMgr em, string itemName, string right)
        {
            string eq = "\"" + itemName + "\" = " + right;
            int i = EquationOf(em, itemName);
            if (i >= 0)
            {
                string old = em.get_Equation(i);
                try { em.set_Equation(i, eq); } catch { return -1; }
                // IT IS READ BACK: set_Equation does not answer, and Status cannot be read.
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

        /// <summary>Z-F1: creates or changes a global variable, and reads back what it drives.</summary>
        private Outcome GlobalVariable(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (!(model is IPartDoc))
                return Failed("I put global variables on a part (parts dimension them). Open the part for me.");
            string itemName = (Str(a, "name", "") ?? "").Trim();
            if (!GoodVariableName(itemName))
                return Failed("«name» is the name of the variable: letters, digits and «_», it starts with a letter " +
                               "(for example «wall», «clearance», «raggio_angolo»), not «" + itemName + "».");
            double val; string error;
            string expression = Str(a, "expression", null);
            bool number = a != null && a.ContainsKey("value_mm");
            if (number == !string.IsNullOrWhiteSpace(expression))
                return Failed("Tell me EITHER «value_mm» (a number in millimeters) OR «expression» " +
                               "(for example «2 * \"wall\" + 1mm»), one of the two.");
            string right;
            if (number)
            {
                if (!RequiredNum(a, "value_mm", out val, out error)) return Failed(error);
                right = val.ToString("0.######", Inv) + "mm";
            }
            else { val = double.NaN; right = expression.Trim(); }

            var em = model.GetEquationMgr() as IEquationMgr;
            if (em == null) return Failed("I cannot read the equations of this part (GetEquationMgr does not answer).");
            // THE GUARDS OF set_dimension (adversarial review of 27/9/2026):
            // a variable that drives dimensions changes the geometry like a
            // dimension, and a linked dimension set_dimension no longer changes.
            var photo = SnapshotPart(model);
            if (photo.Bodies < 0)
                return Failed("I cannot count the bodies of the part before changing the variable: without that, I " +
                               "could not tell you whether the change breaks it. I touched nothing.");
            double? volumeBefore = photo.Volume;
            int existing = EquationOf(em, itemName);
            bool newOne = existing < 0;
            string old = null;
            if (!newOne) { try { old = em.get_Equation(existing); } catch { } }
            int i = WriteEquation(em, itemName, right);
            if (i < 0)
                return Failed(
                    "SOLIDWORKS does not accept «\"" + itemName + "\" = " + right + "». If the name is already taken — in " +
                    "an Italian SOLIDWORKS «spessore» is taken (measured) — choose another one, for example «wall»; if " +
                    "it is an expression, check that the names between quotes exist.");
            try { em.EvaluateAll(); } catch { }
            bool rebuilt = false;
            try { rebuilt = model.ForceRebuild3(false); } catch { }
            i = EquationOf(em, itemName);
            double read = double.NaN;
            try { if (i >= 0) read = em.get_Value(i); } catch { }
            string what = newOne ? "the equations without «" + itemName + "»" : "«" + old + "»";
            if (i < 0 || double.IsNaN(read))
                return Failed("The variable «" + itemName + "» cannot be read back after writing it: I do not know what value it has." +
                               IsBackAsItWas(model, photo, PutBackEquation(model, em, itemName, old, null), what));
            // THE VALUE IS IN THE UNITS OF THE DOCUMENT: it is compared in millimetres.
            double factor = MmPerDocumentUnit(model);
            double readMm = double.IsNaN(factor) ? double.NaN : read * factor;
            if (number && !double.IsNaN(readMm) && Math.Abs(readMm - val) > 1e-6)
                return Failed("I wrote «" + itemName + "» = " + val.ToString("0.###", Inv) + " mm and reading it back it is " +
                               readMm.ToString("0.###", Inv) + " mm: it is not the variable you wanted." +
                               IsBackAsItWas(model, photo, PutBackEquation(model, em, itemName, old, null), what));
            string broken = WhatBroke(model, photo, rebuilt, true);
            if (broken != null)
                return Failed("With «" + itemName + "» = " + right + " " + broken + "." +
                               IsBackAsItWas(model, photo, PutBackEquation(model, em, itemName, old, null), what) +
                               " If you really want that value, first fix the feature that does not fit, then change the variable.");
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
                ["units"] = "millimeters (the value of a length variable, read back in the units of the document and brought to mm)",
            };
            if (double.IsNaN(readMm))
                measurements["warning"] = "I do not know what units the document is in: the variable is " + read.ToString("0.######", Inv) +
                                       " in its units, and I did not compare it with the requested value.";
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>Z-F1: links a dimension to an expression (a variable, a calculation), and reads it back.</summary>
        private Outcome LinkDimension(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (!(model is IPartDoc))
                return Failed("I link the dimensions of a part. Open the part for me.");
            string dimensionName = (Str(a, "dimension", "") ?? "").Trim();
            string expression = (Str(a, "expression", "") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(dimensionName))
                return Failed("Tell me «dimension»: the name of the dimension from the state of the document (for example «D1@Schizzo1»).");
            if (string.IsNullOrWhiteSpace(expression))
                return Failed("Tell me «expression»: what to link it to, for example «\"wall\"» or «2 * \"wall\"».");
            var dimension = FindDimension(model, dimensionName, out string location);
            if (dimension == null)
                return Failed("I cannot find the dimension «" + dimensionName + "» in this document." + (location == null ? "" : " There are: " + location + "."));
            // THE NAME FOR THE EQUATION: the FullName without the part at the tail.
            string complete = "";
            try { complete = dimension.FullName ?? ""; } catch { }
            var parts = complete.Split('@');
            if (parts.Length < 2)
                return Failed("I cannot read the full name of the dimension «" + dimensionName + "» («" + complete + "»): without it, " +
                               "I would not know how to write the equation.");
            string eqName = parts[0] + "@" + parts[1];
            var em = model.GetEquationMgr() as IEquationMgr;
            if (em == null) return Failed("I cannot read the equations of this part (GetEquationMgr does not answer).");
            // AN ANGULAR DIMENSION is read in degrees, a linear one in mm: SystemValue is
            // in radians or metres, and the comparison with the equation (in the units
            // of the document) is made with GetValue3, which is in the same units.
            int dimensionType = -1;
            try { dimensionType = dimension.GetType(); } catch { }
            bool angular = dimensionType == (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular;
            double systemBefore = ApiHelpers.ReadDimension(dimension);
            var photo = SnapshotPart(model);
            if (photo.Bodies < 0)
                return Failed("I cannot count the bodies of the part before linking the dimension: without that, I could " +
                               "not tell you whether the link breaks it. I touched nothing.");
            double? volumeBefore = photo.Volume;
            int existing = EquationOf(em, eqName);
            string old = null;
            if (existing >= 0) { try { old = em.get_Equation(existing); } catch { } }
            int i = WriteEquation(em, eqName, expression);
            if (i < 0)
                return Failed("SOLIDWORKS does not accept «\"" + eqName + "\" = " + expression + "». Check that the " +
                               "variables between quotes exist (you create them with «global_variable»).");
            try { em.EvaluateAll(); } catch { }
            bool rebuilt = false;
            try { rebuilt = model.ForceRebuild3(false); } catch { }
            i = EquationOf(em, eqName);
            double expected = double.NaN;
            try { if (i >= 0) expected = em.get_Value(i); } catch { }
            double afterDoc = double.NaN;
            try { afterDoc = ((double[])dimension.GetValue3((int)swInConfigurationOpts_e.swThisConfiguration, null))[0]; } catch { }
            // Putting back: the previous equation, or none — and then the previous value too,
            // because an unlinked dimension keeps the last value the equation gave it.
            Func<bool> putBack = () => PutBackEquation(model, em, eqName, old,
                old != null ? null : new Action(() => dimension.SetSystemValue3(systemBefore, 2, null)));
            string what = old != null ? "«" + old + "»" : "the dimension unlinked, with its previous value";
            if (double.IsNaN(expected) || double.IsNaN(afterDoc) || Math.Abs(afterDoc - expected) > 0.001)
                return Failed("I linked «" + eqName + "» to «" + expression + "», but the dimension says " +
                               (double.IsNaN(afterDoc) ? "?" : afterDoc.ToString("0.###", Inv)) + " and the equation " +
                               (double.IsNaN(expected) ? "cannot be read back" : expected.ToString("0.###", Inv)) +
                               " (in the units of the document): the link did not take." +
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
                ["verdict"] = "The dimension «" + eqName + "» is now driven by «" + expression + "»: changing the variable changes " +
                               "it. With «set_dimension» it cannot be changed any more (it is driven).",
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
            // A LINK THAT REPLACES ANOTHER IS STATED (review of 27/9/2026):
            // before it was rewritten silently.
            if (old != null) measurements["link_replaced"] = old;
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// Puts the equations back as they were: the old one rewritten, or the new one removed
        /// (with «after», to put back the value of an unlinked dimension); then recalculates
        /// and rebuilds. True if, READING the equations back, they are those from before —
        /// the number that Delete answers is not believed.
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

        /// <summary>The equations that use «name» on the right, with the dimension they drive and its value.</summary>
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
        /// For the state: the equations of the document (the first twenty), or null if there
        /// are none; in «total» how many there really are, so that whoever reads knows that
        /// the list is cut (review of 27/9/2026: the last driven dimensions
        /// vanished without saying so). The «value_text» is in the units of the document.
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
