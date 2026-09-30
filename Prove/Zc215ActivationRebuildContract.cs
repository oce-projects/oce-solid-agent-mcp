using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// C4-3 — A DOCUMENT ACTIVATED WITH A PENDING REBUILD IS REBUILT BEFORE
    /// ANSWERING (Z-C21.5, schema S18).
    ///
    /// `DocumentByPath.ActivateAndReadBack` always asks ActivateDoc3 with
    /// swRebuildOnActivation_e.swDontRebuildActiveDoc, to avoid the dialog that
    /// swUserDecision opens and waits on a person (finding N8). The guide pays
    /// for that choice: ISldWorks~ActivateDoc3.html, Remarks — «If the document
    /// needs a rebuild upon activation, and you have specified Option with
    /// swRebuildOnActivation_e.swDontRebuildActiveDoc, then the activated
    /// document is not rebuilt, and Errors contains
    /// swActivateDocError_e.swDocNeedsRebuildWarning. You can then rebuild the
    /// document using IModelDoc2::EditRebuild3.» Up to library 0.146.0
    /// `Ponte/Documenti.cs::PortaDavanti` read the activation Errors bitmask
    /// into a local and never looked at it again: a plan whose first step is
    /// read-only (`proprieta_di_massa`, `ingombro`) would measure stale geometry,
    /// since the executor's own rebuild only follows a SUCCESSFUL step
    /// (EsecutorePiano.cs) — too late for the very first one.
    ///
    /// NO BENCH CASE CAN BUILD THE swDocNeedsRebuildWarning BIT ON PURPOSE: it
    /// needs a document already loaded, out of date, that a SEPARATE
    /// ActivateDoc3 call (not the bench's own) brings to the front while
    /// swDontRebuildActiveDoc is in effect — nothing the bench's one bridge
    /// process can stage. So this checks the two things a bench case cannot:
    ///   - on the SOURCE, that `PortaDavanti` calls IModelDoc2.EditRebuild3
    ///     exactly when its own bit test says to;
    ///   - the bit test itself (`Sessione.NeedsRebuildAfterActivation`), by
    ///     reflection on the built bridge, against every shape ActivateAndReadBack
    ///     can hand it: -1 (ActivateDoc3 was never called — already in front),
    ///     0 (no warning), swGenericActivateError alone, swDocNeedsRebuildWarning
    ///     alone, and both bits together.
    /// </summary>
    internal static class Zc215ActivationRebuildContract
    {
        private const string SourceFile = "Documenti.cs";
        private const int SwGenericActivateError = 1;
        private const int SwDocNeedsRebuildWarning = 2;

        internal static void RunAll()
        {
            Gruppo("C4-3: a pending rebuild left by ActivateDoc3 is not lost");

            SourceWiring();

            var bridge = PonteOppureNonQui("Sessione.NeedsRebuildAfterActivation, in full");
            if (bridge == null) return;

            MethodInfo needsRebuild;
            try
            {
                var type = Carica.Classe(Carica.Ponte(bridge), "Sessione");
                if (type == null) { Segna("Sessione.NeedsRebuildAfterActivation is there", false, "no such type"); return; }
                needsRebuild = type.GetMethod("NeedsRebuildAfterActivation",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (needsRebuild == null) { Segna("Sessione.NeedsRebuildAfterActivation is there", false, "no such method"); return; }
            }
            catch (Exception ex) { Segna("the bridge loads", false, ex.ToString()); return; }

            Func<int, bool> needs = errors => (bool)needsRebuild.Invoke(null, new object[] { errors });

            Segna("ActivateDoc3 not called (-1, already in front): nothing to act on",
                  !needs(-1));
            Segna("Errors = 0 (no warning): no rebuild is asked",
                  !needs(0));
            Segna("swGenericActivateError alone (1) is not swDocNeedsRebuildWarning",
                  !needs(SwGenericActivateError));
            Segna("swDocNeedsRebuildWarning alone (2): a rebuild is asked",
                  needs(SwDocNeedsRebuildWarning));
            Segna("both bits together (3): the warning bit still fires",
                  needs(SwGenericActivateError | SwDocNeedsRebuildWarning));
        }

        /// <summary>
        /// ON THE SOURCE: `PortaDavanti` (Ponte/Documenti.cs) calls
        /// IModelDoc2.EditRebuild3() inside the same `if` that tests
        /// `NeedsRebuildAfterActivation`. A bench case cannot force
        /// swDocNeedsRebuildWarning, so nothing else here would ever turn red if
        /// the call were quietly dropped.
        /// </summary>
        private static void SourceWiring()
        {
            var path = Path.Combine(Radice(), "Ponte", SourceFile);
            if (!File.Exists(path))
            {
                Segna("Ponte/" + SourceFile + " reads", false, "not found: " + path);
                return;
            }
            string source;
            try { source = File.ReadAllText(path); }
            catch (Exception ex) { Segna("Ponte/" + SourceFile + " reads", false, ex.Message); return; }

            var method = Regex.Match(source,
                @"private\s+Dictionary<string,\s*object>\s+PortaDavanti\s*\([^)]*\)\s*\{(.*?)\n        \}",
                RegexOptions.Singleline);
            Segna("PortaDavanti is found in the source", method.Success,
                  method.Success ? "" : "the method does not match the pattern: renamed or moved");
            if (!method.Success) return;
            string body = method.Groups[1].Value;

            bool testsTheBit = Regex.IsMatch(body, @"NeedsRebuildAfterActivation\s*\(\s*activationErrors\s*\)");
            Segna("PortaDavanti tests NeedsRebuildAfterActivation(activationErrors)", testsTheBit);

            bool callsRebuild = Regex.IsMatch(body, @"doc\.EditRebuild3\s*\(\s*\)");
            Segna("PortaDavanti calls doc.EditRebuild3() (not a different document reference)", callsRebuild);

            // THE CALL IS GUARDED BY THE TEST, not unconditional: an
            // unconditional EditRebuild3 on every activation would rebuild a
            // document nobody asked to touch, on every call that brings a
            // document to the front (ApriDocumento, AttivaDocumento).
            var guarded = Regex.Match(body,
                @"if\s*\(\s*needsRebuild\s*\)\s*\{[^}]*EditRebuild3", RegexOptions.Singleline);
            var bitRead = Regex.Match(body,
                @"(?:bool\s+needsRebuild\s*=\s*NeedsRebuildAfterActivation\s*\(\s*activationErrors\s*\)\s*;)",
                RegexOptions.Singleline);
            Segna("the EditRebuild3 call sits behind the bit test, not unconditionally",
                  bitRead.Success && guarded.Success,
                  bitRead.Success ? "" : "no local named needsRebuild computed from the bit test right after activation");

            bool saysSo = Regex.IsMatch(body, "rebuilt_on_activation");
            Segna("the answer says a rebuild happened (rebuilt_on_activation)", saysSo);
        }
    }
}
