using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// GLOBAL VARIABLES AND EQUATIONS (entry Z-F1): not repeating a number,
    /// giving it a name. `wall = 3mm`, and the dimension driven by `wall` survives
    /// an upstream change by construction — it is the identity system that
    /// SOLIDWORKS already has, and a parametric model is the only kind that someone who does not
    /// design can change without breaking anything.
    ///
    /// Measured on 27/9/2026 (`probe0089 zf1`, `zf1b`, plate 120x80x10):
    ///
    ///  * `IEquationMgr.Add3(-1, "\"wall\" = 12mm", true, swAllConfiguration,
    ///    null)` returns the index (0); `"\"D1@Boss-Extrude1\" = \"wall\""`
    ///    links the thickness: volume 96000 -> 115200. `set_Equation` to 15mm, then
    ///    `EvaluateAll` and rebuild: 144000, and the dimension says 15.
    ///  * Expressions work: `"double" = 2 * "wall" + 1mm` is worth 31.
    ///  * **The word an Italian SOLIDWORKS reserves for the thickness is REFUSED**,
    ///    in both capitalisations (Add3 -1), even with the part without properties:
    ///    it is a name already taken, and an equation that uses it is worth 3. The
    ///    English «Thickness» is accepted there (measured 7/10/2026, with the toggle
    ///    «Use English feature and file names» on or off: the reserved words follow the
    ///    language of the interface). A refused name is stated, with the words of
    ///    solidworks-names.json (equation.reserved.thickness), and another name is proposed.
    ///  * `get_Status` is NOT reliable: -1 even after a successful EvaluateAll,
    ///    1 after a successful set_Equation. The outcome is read from the VALUES: that
    ///    of the equation and that of the dimension, read back.
    ///  * The name of a dimension for the equation is «D1@Boss-Extrude1»:
    ///    the `FullName` without the part at the tail (`@Part1089.Part`).
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

        /// <summary>
        /// A NEW EQUATION IS ADDED WITH ITS DOCUMENT IN FRONT (brief R1, measured on
        /// 26/10/2026 with SOLIDWORKS 2021 SP5.1). IEquationMgr.Add3 on a document
        /// that is not the active one leaves it UNSAVABLE: every later SaveAs answers
        /// false with swGenericSaveError and writes a file of 0 bytes. Probe
        /// of equations behind: in front 45,040 bytes; behind 0 bytes, with or
        /// without ForceRebuild3 and with or without CommandInProgress; a rebuild
        /// alone behind does no harm. The probe of writes behind: set_Equation,
        /// Delete and a custom property written behind do no harm either: only Add3.
        /// Found because a plan whose window changed during a global_variable could
        /// not save its part any more, not even with a save of its own afterwards.
        /// Null when the document is in front (<paramref name="broughtBack"/> says
        /// whether it had to be brought there); otherwise the refusal.
        /// </summary>
        private string EquationDocumentInFront(IModelDoc2 model, out bool broughtBack)
        {
            broughtBack = false;
            if (DocumentByPath.IsFrontDocument(_sw, model)) return null;
            if (!MayBringDocumentBack)
                return "Another window is in front, and this plan stops on a window change instead of bringing " +
                       "its document back: I do not add an equation to a document behind, because SOLIDWORKS then " +
                       "no longer saves it (measured). I touched nothing. Bring the part to the front and try again.";
            int errors;
            string problem;
            if (DocumentByPath.ActivateAndReadBack(_sw, model, out errors, out problem))
            {
                broughtBack = true;
                string rebuilt = DocumentByPath.RebuildIfActivationAsked(model, errors);
                _log("[agent] the part was not in front: brought back before adding the equation" +
                     (rebuilt == null ? "" : "; " + rebuilt));
                return null;
            }
            return "I add a new equation only with its document in front: written on a document behind, " +
                   "SOLIDWORKS no longer saves it (measured). I could not bring it to the front: " +
                   (problem ?? "SOLIDWORKS did not say why") + ". I touched nothing. Bring the part to the front " +
                   "and try again.";
        }

        /// <summary>
        /// THE REPAIR, MEASURED (probe of the repair, 26/10/2026): writing each
        /// equation again with its own text, WITH THE DOCUMENT IN FRONT, makes a
        /// document broken by an Add3 behind savable again. Written behind it does
        /// not, and neither do EditUndo2, EvaluateAll or a SaveAs as a copy. Called
        /// right after the Add3: when the document is still in front nothing is
        /// done and null comes back. Otherwise the document is brought back, its
        /// equations are written again, and the sentence for the answer says so;
        /// up to three tries if the window keeps changing. It is done even when
        /// <see cref="MayBringDocumentBack"/> is false: a part that can no longer be
        /// saved costs more than a window brought back.
        /// </summary>
        private string RepairIfAnotherCameToFront(IModelDoc2 model, IEquationMgr em)
        {
            if (DocumentByPath.IsFrontDocument(_sw, model)) return null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int errors;
                string problem;
                if (!DocumentByPath.ActivateAndReadBack(_sw, model, out errors, out problem)) continue;
                string rebuilt = DocumentByPath.RebuildIfActivationAsked(model, errors);
                if (rebuilt != null) _log("[agent] " + rebuilt);
                int n = 0;
                try { n = em.GetCount(); } catch { }
                for (int k = 0; k < n; k++)
                {
                    try { em.set_Equation(k, em.get_Equation(k)); } catch { }
                }
                if (DocumentByPath.IsFrontDocument(_sw, model))
                {
                    _log("[agent] another window came to the front during an equation: " + n +
                         " equation(s) written again in front");
                    return "Another window came to the front while the equation was being written, and " +
                           "SOLIDWORKS then refuses to save the part (measured). I brought the part back and wrote " +
                           "its " + n + " equation(s) again with the same text, which makes it savable again.";
                }
            }
            return "Another window came to the front while the equation was being written, and I could not " +
                   "keep the part in front to repair it: SOLIDWORKS may refuse to save it (swGenericSaveError, " +
                   "a file of 0 bytes). Bring the part to the front and write any of its equations again with " +
                   "the same text before saving.";
        }

        private static string BroughtBackNote(bool broughtBack)
        {
            return broughtBack
                ? "Another window was in front when the equation was about to be written: I brought the part " +
                  "back first, because an equation added to a document behind leaves it unsavable (measured)."
                : null;
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
                               "(for example «wall», «clearance», «corner_radius»), not «" + itemName + "».");
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
            bool broughtBack = false;
            if (newOne)
            {
                string notInFront = EquationDocumentInFront(model, out broughtBack);
                if (notInFront != null) return Failed(notInFront);
            }
            int i = WriteEquation(em, itemName, right);
            if (i < 0)
                return Failed(
                    "SOLIDWORKS does not accept «\"" + itemName + "\" = " + right + "». If the name is already taken — " +
                    ReservedNames() + " — choose another one, for example «wall»; if " +
                    "it is an expression, check that the names between quotes exist.");
            try { em.EvaluateAll(); } catch { }
            bool rebuilt = false;
            try { rebuilt = model.ForceRebuild3(false); } catch { }
            string windowNote = newOne ? RepairIfAnotherCameToFront(model, em) ?? BroughtBackNote(broughtBack) : null;
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
            if (windowNote != null) measurements["warning_window"] = windowNote;
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
                return Failed("Tell me «dimension»: the name of the dimension from the state of the document (for example «D1@Sketch1»).");
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
            bool broughtBack = false;
            if (existing < 0)
            {
                string notInFront = EquationDocumentInFront(model, out broughtBack);
                if (notInFront != null) return Failed(notInFront);
            }
            int i = WriteEquation(em, eqName, expression);
            if (i < 0)
                return Failed("SOLIDWORKS does not accept «\"" + eqName + "\" = " + expression + "». Check that the " +
                               "variables between quotes exist (you create them with «global_variable»).");
            try { em.EvaluateAll(); } catch { }
            bool rebuilt = false;
            try { rebuilt = model.ForceRebuild3(false); } catch { }
            string windowNote = existing < 0 ? RepairIfAnotherCameToFront(model, em) ?? BroughtBackNote(broughtBack) : null;
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
                return Failed("Linking «" + eqName + "» to «" + expression + "» " + broken + "." +
                               IsBackAsItWas(model, photo, putBack(), what));
            double? volumeAfter = VolumeMm3(model);
            double afterSystem = ApiHelpers.ReadDimension(dimension);
            var measurements = new Dictionary<string, object>
            {
                ["dimension"] = eqName,
                ["equation"] = em.get_Equation(i),
                ["volume_before_mm3"] = NumberOrNull(volumeBefore, 2),
                ["volume_after_mm3"] = NumberOrNull(volumeAfter, 2),
                // «meaning», not «verdict» (26/10/2026): the same sentence after
                // every link, an explanation of what a link is. A «verdict» stays
                // in a short answer whatever its length; this one cost 190
                // characters a step in a plan of 21.
                ["meaning"] = "The dimension «" + eqName + "» is now driven by «" + expression + "»: changing the variable changes " +
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
            if (windowNote != null) measurements["warning_window"] = windowNote;
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>One equation that «parameters» writes: a variable or a link.</summary>
        private sealed class EquationWrite
        {
            /// <summary>The name on the left, without quotes: «wall», «D1@Boss-Extrude1».</summary>
            public string Left;
            /// <summary>The right-hand side as written: «12mm», «2 * "wall"».</summary>
            public string Right;
            /// <summary>The value asked in mm, for a variable given as a number; NaN otherwise.</summary>
            public double AskedMm = double.NaN;
            /// <summary>A link (a dimension on the left), not a variable.</summary>
            public bool IsLink;
            /// <summary>The name the caller used for the dimension of a link.</summary>
            public string Asked;
            public IDimension Dimension;
            public double SystemBefore = double.NaN;
            public bool Angular;
            /// <summary>The equation that was there before, or null: put back on a failure.</summary>
            public string Old;
        }

        /// <summary>
        /// BRIEF R2 AND §5.1: VARIABLES AND LINKS IN ONE STEP. On 24/10/2026 a part
        /// took five «global_variable» and six «link_dimension», eleven steps, each
        /// with its own reading, evaluation and rebuild. Here every equation is
        /// checked before anything is written (names, dimensions that exist),
        /// written in one go (variables first, so that the links can use them),
        /// evaluated and rebuilt once, and then every value is read back: a
        /// variable given as a number must read the same in mm, a linked dimension
        /// must equal its equation. Anything that does not hold, or a part that
        /// breaks, puts EVERY equation back as it was (the old text, or none and
        /// the old value of the dimension), in the reverse order, and says whether
        /// the part is back. The guards are those of the two single operations.
        /// </summary>
        private Outcome Parameters(IModelDoc2 model, Dictionary<string, object> a, DocumentState docState)
        {
            if (!(model is IPartDoc))
                return Failed("I put variables and links on a part (parts dimension them). Open the part for me.");
            object rawVariables = a != null && a.TryGetValue("variables", out var rv) ? rv : null;
            object rawLinks = a != null && a.TryGetValue("links", out var rl) ? rl : null;
            if ((rawVariables != null && !(rawVariables is Dictionary<string, object>)) ||
                (rawLinks != null && !(rawLinks is Dictionary<string, object>)))
                return Failed("«variables» and «links» are objects: «variables» {name: value in mm, or an expression}, " +
                               "«links» {dimension: expression}. For example {\"variables\": {\"wall\": 3}, " +
                               "\"links\": {\"D1@Boss-Extrude1\": \"\\\"wall\\\"\"}}.");
            var variables = rawVariables as Dictionary<string, object> ?? new Dictionary<string, object>();
            var links = rawLinks as Dictionary<string, object> ?? new Dictionary<string, object>();
            if (variables.Count == 0 && links.Count == 0)
                return Failed("Tell me «variables» (name: value in mm, or an expression) or «links» (dimension: " +
                               "expression), at least one of the two, with at least one entry.");

            // ── EVERYTHING IS CHECKED BEFORE ANYTHING IS WRITTEN ──
            var writes = new List<EquationWrite>();
            foreach (string itemName in variables.Keys)
            {
                string name = (itemName ?? "").Trim();
                if (!GoodVariableName(name))
                    return Failed("A variable is named with letters, digits and «_», starting with a letter (for " +
                                   "example «wall», «clearance»), not «" + name + "». I touched nothing.");
                object value = variables[itemName];
                var write = new EquationWrite { Left = name };
                if (value is string text)
                {
                    if (string.IsNullOrWhiteSpace(text))
                        return Failed("The variable «" + name + "» has an empty expression. Give a number in mm or an " +
                                       "expression such as «2 * \"wall\" + 1mm». I touched nothing.");
                    write.Right = text.Trim();
                }
                else
                {
                    double mm;
                    try { mm = Convert.ToDouble(value, Inv); }
                    catch
                    {
                        return Failed("The variable «" + name + "» is neither a number in mm nor an expression. " +
                                       "I touched nothing.");
                    }
                    if (double.IsNaN(mm) || double.IsInfinity(mm))
                        return Failed("The variable «" + name + "» is not a finite number. I touched nothing.");
                    write.AskedMm = mm;
                    write.Right = mm.ToString("0.######", Inv) + "mm";
                }
                writes.Add(write);
            }
            foreach (string dimensionName in links.Keys)
            {
                string asked = (dimensionName ?? "").Trim();
                string expression = (Convert.ToString(links[dimensionName], Inv) ?? "").Trim();
                if (expression.Length == 0)
                    return Failed("The link of «" + asked + "» has no expression: what drives it, for example " +
                                   "«\"wall\"» or «2 * \"wall\"». I touched nothing.");
                string location;
                IDimension dimension = FindDimension(model, asked, out location);
                if (dimension == null)
                    return Failed("I cannot find the dimension «" + asked + "» in this document." +
                                   (location == null ? "" : " There are: " + location + ".") + " I touched nothing.");
                string complete = "";
                try { complete = dimension.FullName ?? ""; } catch { }
                var parts = complete.Split('@');
                if (parts.Length < 2)
                    return Failed("I cannot read the full name of the dimension «" + asked + "» («" + complete + "»): " +
                                   "without it I would not know how to write the equation. I touched nothing.");
                int dimensionType = -1;
                try { dimensionType = dimension.GetType(); } catch { }
                writes.Add(new EquationWrite
                {
                    Left = parts[0] + "@" + parts[1],
                    Right = expression,
                    IsLink = true,
                    Asked = asked,
                    Dimension = dimension,
                    SystemBefore = ApiHelpers.ReadDimension(dimension),
                    Angular = dimensionType == (int)swDimensionParamType_e.swDimensionParamTypeDoubleAngular,
                });
            }

            var em = model.GetEquationMgr() as IEquationMgr;
            if (em == null) return Failed("I cannot read the equations of this part (GetEquationMgr does not answer).");
            var photo = SnapshotPart(model);
            if (photo.Bodies < 0)
                return Failed("I cannot count the bodies of the part before writing the equations: without that, I " +
                               "could not tell you whether they break it. I touched nothing.");
            double? volumeBefore = photo.Volume;
            bool anyNew = false;
            foreach (EquationWrite w in writes)
            {
                int existing = EquationOf(em, w.Left);
                if (existing >= 0) { try { w.Old = em.get_Equation(existing); } catch { } }
                else anyNew = true;
            }
            bool broughtBack = false;
            if (anyNew)
            {
                string notInFront = EquationDocumentInFront(model, out broughtBack);
                if (notInFront != null) return Failed(notInFront);
            }

            // ── WRITTEN IN ONE GO, EVALUATED AND REBUILT ONCE ──
            var written = new List<EquationWrite>();
            foreach (EquationWrite w in writes)
            {
                if (WriteEquation(em, w.Left, w.Right) < 0)
                    return Failed("SOLIDWORKS does not accept «\"" + w.Left + "\" = " + w.Right + "». " +
                                   (w.IsLink
                                       ? "Check that the variables between quotes exist (they can be in «variables» of this step)."
                                       : "If the name is already taken — " + ReservedNames() + " — choose another one; if it " +
                                         "is an expression, check that the names between quotes exist.") +
                                   IsBackAsItWas(model, photo, PutBackAll(model, em, written), "the equations as they were"));
                written.Add(w);
            }
            try { em.EvaluateAll(); } catch { }
            bool rebuilt = false;
            try { rebuilt = model.ForceRebuild3(false); } catch { }
            string windowNote = anyNew ? RepairIfAnotherCameToFront(model, em) ?? BroughtBackNote(broughtBack) : null;

            // ── EVERY VALUE READ BACK ──
            double factor = MmPerDocumentUnit(model);
            var variableRows = new List<object>();
            var linkRows = new List<object>();
            foreach (EquationWrite w in writes)
            {
                int i = EquationOf(em, w.Left);
                double read = double.NaN;
                try { if (i >= 0) read = em.get_Value(i); } catch { }
                string equation = null;
                try { if (i >= 0) equation = em.get_Equation(i); } catch { }
                if (i < 0 || double.IsNaN(read))
                    return Failed("«" + w.Left + "» cannot be read back after writing it: I do not know what value it has." +
                                   IsBackAsItWas(model, photo, PutBackAll(model, em, written), "the equations as they were"));
                if (!w.IsLink)
                {
                    double readMm = double.IsNaN(factor) ? double.NaN : read * factor;
                    if (!double.IsNaN(w.AskedMm) && !double.IsNaN(readMm) && Math.Abs(readMm - w.AskedMm) > 1e-6)
                        return Failed("I wrote «" + w.Left + "» = " + w.AskedMm.ToString("0.###", Inv) + " mm and reading it " +
                                       "back it is " + readMm.ToString("0.###", Inv) + " mm: it is not the variable you wanted." +
                                       IsBackAsItWas(model, photo, PutBackAll(model, em, written), "the equations as they were"));
                    var variableRow = new Dictionary<string, object>
                    {
                        ["name"] = w.Left,
                        ["equation"] = equation,
                        ["value_mm"] = double.IsNaN(readMm) ? null : (object)Math.Round(readMm, 6),
                        ["is_new"] = w.Old == null,
                    };
                    // WHAT IT WAS, for whoever must put it back: SOLIDWORKS records
                    // no equation in an undo entry (Checks\UndoGaps, 27/10/2026),
                    // so «undo» does not withdraw this step and «parameters» with the
                    // old value is the way back.
                    if (w.Old != null) variableRow["equation_before"] = w.Old;
                    variableRows.Add(variableRow);
                    continue;
                }
                IDimension dimension = w.Dimension;
                double afterDoc = double.NaN;
                try { afterDoc = ((double[])dimension.GetValue3((int)swInConfigurationOpts_e.swThisConfiguration, null))[0]; } catch { }
                if (double.IsNaN(afterDoc) || Math.Abs(afterDoc - read) > 0.001)
                    return Failed("I linked «" + w.Left + "» to «" + w.Right + "», but the dimension says " +
                                   (double.IsNaN(afterDoc) ? "?" : afterDoc.ToString("0.###", Inv)) + " and the equation " +
                                   read.ToString("0.###", Inv) + " (in the units of the document): the link did not take." +
                                   IsBackAsItWas(model, photo, PutBackAll(model, em, written), "the equations as they were"));
                double afterSystem = ApiHelpers.ReadDimension(dimension);
                var row = new Dictionary<string, object>
                {
                    ["dimension"] = w.Left,
                    ["equation"] = equation,
                };
                if (w.Angular)
                {
                    row["value_before_deg"] = Math.Round(w.SystemBefore * 180.0 / Math.PI, 4);
                    row["value_after_deg"] = Math.Round(afterSystem * 180.0 / Math.PI, 4);
                }
                else
                {
                    row["value_before_mm"] = Math.Round(Units.Mm(w.SystemBefore), 4);
                    row["value_after_mm"] = Math.Round(Units.Mm(afterSystem), 4);
                }
                if (w.Old != null) row["link_replaced"] = w.Old;
                linkRows.Add(row);
            }
            string broken = WhatBroke(model, photo, rebuilt, true);
            if (broken != null)
                return Failed("With these equations " + broken + "." +
                               IsBackAsItWas(model, photo, PutBackAll(model, em, written), "the equations as they were") +
                               " If you really want those values, first fix the feature that does not fit, then change them.");
            double? volumeAfter = VolumeMm3(model);
            var measurements = new Dictionary<string, object>
            {
                ["variables"] = variableRows.ToArray(),
                ["links"] = linkRows.ToArray(),
                ["equations_written"] = writes.Count,
                ["volume_before_mm3"] = NumberOrNull(volumeBefore, 2),
                ["volume_after_mm3"] = NumberOrNull(volumeAfter, 2),
                ["units"] = "millimeters for lengths, degrees for angles (read back in the units of the document and brought to mm)",
            };
            if (double.IsNaN(factor))
                measurements["warning"] = "I do not know what units the document is in: the variables given as numbers " +
                                          "were not compared with the values asked.";
            if (windowNote != null) measurements["warning_window"] = windowNote;
            return SucceededWithoutCreating(measurements);
        }

        /// <summary>
        /// Puts back every equation «parameters» wrote, the last first: the old
        /// text, or none and, for a link, the value the dimension had. True only if
        /// each one reads back as it was.
        /// </summary>
        private static bool PutBackAll(IModelDoc2 model, IEquationMgr em, List<EquationWrite> written)
        {
            bool all = true;
            for (int k = written.Count - 1; k >= 0; k--)
            {
                EquationWrite w = written[k];
                IDimension dimension = w.Dimension;
                double before = w.SystemBefore;
                Action after = w.IsLink && w.Old == null && dimension != null
                    ? new Action(() => dimension.SetSystemValue3(before, 2, null))
                    : null;
                if (!PutBackEquation(model, em, w.Left, w.Old, after)) all = false;
            }
            return all;
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

        /// <summary>
        /// The reserved variable name that SOLIDWORKS refuses, said in the words of
        /// the language it runs in (solidworks-names.json, equation.reserved.thickness):
        /// a name taken by the program is the one reason a good equation is refused
        /// without a message, and the word depends on the language of the installation.
        /// </summary>
        private string ReservedNames()
        {
            var quoted = new List<string>();
            foreach (var spelling in SolidWorksLanguage.Read(_sw).Spellings("equation.reserved.thickness"))
                quoted.Add("«" + spelling + "»");
            return "SOLIDWORKS reserves some names in its own language, for the thickness " +
                   string.Join(" or ", quoted) + " (measured)";
        }
    }
}
