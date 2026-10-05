using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
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
    /// `Bridge/Documents.cs::BringToFront` read the activation Errors bitmask
    /// into a local and never looked at it again: a plan whose first step is
    /// read-only (`mass_properties`, `overall`) would measure stale geometry,
    /// since the executor's own rebuild only follows a SUCCESSFUL step
    /// (PlanExecutor.cs) — too late for the very first one.
    ///
    /// NO BENCH CASE CAN BUILD THE swDocNeedsRebuildWarning BIT ON PURPOSE: it
    /// needs a document already loaded, out of date, that a SEPARATE
    /// ActivateDoc3 call (not the bench's own) brings to the front while
    /// swDontRebuildActiveDoc is in effect — nothing the bench's one bridge
    /// process can stage. So this checks the two things a bench case cannot:
    ///   - on the SOURCE, that `BringToFront` calls IModelDoc2.EditRebuild3
    ///     exactly when its own bit test says to;
    ///   - the bit test itself (`Session.NeedsRebuildAfterActivation`), by
    ///     reflection on the built bridge, against every shape ActivateAndReadBack
    ///     can hand it: -1 (ActivateDoc3 was never called — already in front),
    ///     0 (no warning), swGenericActivateError alone, swDocNeedsRebuildWarning
    ///     alone, and both bits together.
    /// </summary>
    internal static class Zc215ActivationRebuildContract
    {
        private const string SourceFile = "Documents.cs";
        private const int SwGenericActivateError = 1;
        private const int SwDocNeedsRebuildWarning = 2;

        internal static void RunAll()
        {
            Group("C4-3: a pending rebuild left by ActivateDoc3 is not lost");

            SourceWiring();
            LibraryWiring();

            var bridge = BridgeOrNotHere("Sessione.NeedsRebuildAfterActivation, in full");
            if (bridge == null) return;

            MethodInfo needsRebuild;
            try
            {
                var type = Load.Class(Load.Bridge(bridge), "Session");
                if (type == null) { Check("Sessione.NeedsRebuildAfterActivation is there", false, "no such type"); return; }
                needsRebuild = type.GetMethod("NeedsRebuildAfterActivation",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (needsRebuild == null) { Check("Sessione.NeedsRebuildAfterActivation is there", false, "no such method"); return; }
            }
            catch (Exception ex) { Check("the bridge loads", false, ex.ToString()); return; }

            Func<int, bool> needs = errors => (bool)needsRebuild.Invoke(null, new object[] { errors });

            Check("ActivateDoc3 not called (-1, already in front): nothing to act on",
                  !needs(-1));
            Check("Errors = 0 (no warning): no rebuild is asked",
                  !needs(0));
            Check("swGenericActivateError alone (1) is not swDocNeedsRebuildWarning",
                  !needs(SwGenericActivateError));
            Check("swDocNeedsRebuildWarning alone (2): a rebuild is asked",
                  needs(SwDocNeedsRebuildWarning));
            Check("both bits together (3): the warning bit still fires",
                  needs(SwGenericActivateError | SwDocNeedsRebuildWarning));
        }

        /// <summary>
        /// THE LIBRARY'S OWN ACTIVATIONS (review of the Z-C21.5 leftovers): every
        /// DocumentByPath.ActivateAndReadBack outside DocumentByPath.cs acts on the
        /// bitmask within the next lines: RebuildIfActivationAsked when OUR
        /// document was brought forward to be exported, PutBackNeedsRebuild when
        /// the user's document was put back. Then, by reflection on the built
        /// library, the bit test DocumentByPath.NeedsRebuildAfterActivation on
        /// the same shapes as the bridge's.
        /// </summary>
        private static void LibraryWiring()
        {
            var dir = Path.Combine(Root(), "Oce.Solid.Agent.Core");
            var loose = new System.Collections.Generic.List<string>();
            int calls = 0;
            try
            {
                foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly))
                {
                    if (string.Equals(Path.GetFileName(file), "DocumentByPath.cs", StringComparison.OrdinalIgnoreCase)) continue;
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (!lines[i].Contains("DocumentByPath.ActivateAndReadBack(")) continue;
                        calls++;
                        bool acts = false;
                        for (int k = i; k < Math.Min(lines.Length, i + 14) && !acts; k++)
                            acts = lines[k].Contains("RebuildIfActivationAsked(") || lines[k].Contains("PutBackNeedsRebuild(");
                        if (!acts) loose.Add(Path.GetFileName(file) + ":" + (i + 1));
                    }
                }
            }
            catch (Exception ex) { Check("the library sources read", false, ex.Message); return; }
            Check("every library ActivateAndReadBack acts on swDocNeedsRebuildWarning (rebuild ours, say it on the user's)",
                  calls > 0 && loose.Count == 0,
                  calls == 0 ? "no call found: moved or renamed" : loose.Count == 0 ? calls + " calls" : "not acting: " + string.Join(", ", loose));

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library)) { NotHere("DocumentByPath.NeedsRebuildAfterActivation", library + " is missing: build the library first"); return; }
            try
            {
                var redist = WhereTheInteropIs();
                if (redist != null)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += (sender, a) =>
                    {
                        var name = new AssemblyName(a.Name).Name;
                        if (!name.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase)) return null;
                        var f = Path.Combine(redist, name + ".dll");
                        return File.Exists(f) ? Assembly.LoadFrom(f) : null;
                    };
                }
                var type = Assembly.Load(File.ReadAllBytes(library)).GetType("Oce.Solid.Agent.Core.DocumentByPath", false);
                var method = type == null ? null : type.GetMethod("NeedsRebuildAfterActivation", BindingFlags.Public | BindingFlags.Static);
                if (method == null) { Check("DocumentByPath.NeedsRebuildAfterActivation is there", false, "not found"); return; }
                Func<int, bool> needs = e => (bool)method.Invoke(null, new object[] { e });
                Check("library bit test: -1, 0 and swGenericActivateError alone ask nothing; the warning bit, alone or with 1, asks a rebuild",
                      !needs(-1) && !needs(0) && !needs(SwGenericActivateError) && needs(SwDocNeedsRebuildWarning) &&
                      needs(SwGenericActivateError | SwDocNeedsRebuildWarning));
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>
        /// ON THE SOURCE: `BringToFront` (Bridge/Documents.cs) calls
        /// IModelDoc2.EditRebuild3() inside the same `if` that tests
        /// `NeedsRebuildAfterActivation`. A bench case cannot force
        /// swDocNeedsRebuildWarning, so nothing else here would ever turn red if
        /// the call were quietly dropped.
        /// </summary>
        private static void SourceWiring()
        {
            var path = Path.Combine(Root(), "Bridge", SourceFile);
            if (!File.Exists(path))
            {
                Check("Bridge/" + SourceFile + " reads", false, "not found: " + path);
                return;
            }
            string source;
            try { source = File.ReadAllText(path); }
            catch (Exception ex) { Check("Bridge/" + SourceFile + " reads", false, ex.Message); return; }

            var method = Regex.Match(source,
                @"private\s+Dictionary<string,\s*object>\s+BringToFront\s*\([^)]*\)\s*\{(.*?)\n        \}",
                RegexOptions.Singleline);
            Check("PortaDavanti is found in the source", method.Success,
                  method.Success ? "" : "the method does not match the pattern: renamed or moved");
            if (!method.Success) return;
            string body = method.Groups[1].Value;

            bool testsTheBit = Regex.IsMatch(body, @"NeedsRebuildAfterActivation\s*\(\s*activationErrors\s*\)");
            Check("PortaDavanti tests NeedsRebuildAfterActivation(activationErrors)", testsTheBit);

            bool callsRebuild = Regex.IsMatch(body, @"doc\.EditRebuild3\s*\(\s*\)");
            Check("PortaDavanti calls doc.EditRebuild3() (not a different document reference)", callsRebuild);

            // THE CALL IS GUARDED BY THE TEST, not unconditional: an
            // unconditional EditRebuild3 on every activation would rebuild a
            // document nobody asked to touch, on every call that brings a
            // document to the front (OpenDocument, ActivateDocument).
            var guarded = Regex.Match(body,
                @"if\s*\(\s*needsRebuild\s*\)\s*\{[^}]*EditRebuild3", RegexOptions.Singleline);
            var bitRead = Regex.Match(body,
                @"(?:bool\s+needsRebuild\s*=\s*NeedsRebuildAfterActivation\s*\(\s*activationErrors\s*\)\s*;)",
                RegexOptions.Singleline);
            Check("the EditRebuild3 call sits behind the bit test, not unconditionally",
                  bitRead.Success && guarded.Success,
                  bitRead.Success ? "" : "no local named needsRebuild computed from the bit test right after activation");

            bool saysSo = Regex.IsMatch(body, "rebuilt_on_activation");
            Check("the answer says a rebuild happened (rebuilt_on_activation)", saysSo);
        }
    }
}
