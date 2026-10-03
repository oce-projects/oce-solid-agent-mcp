using System;
using System.IO;
using System.Linq;
using System.Reflection;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// THE IMPORT CHAIN OF model_dimensions STOPS AT THE MARKED MASKS (N9)
    /// AND ASKS SOLIDWORKS TO REMOVE DUPLICATES (M2), Z-C21.2.
    ///
    /// Finding N9, measured by its Z-C21.3 probe (28/9/2026): on a part with NOTHING marked for drawing,
    /// model_dimensions with all_dimensions = false imported all 7 unmarked
    /// dimensions through its last fallback (one view at a time, with the
    /// NOT-MARKED bit), and InsertModelDimensions(0) with a view selected
    /// brought 2 more. No bench case can build that part: every extrusion
    /// depth is born marked, and the bridge marks the sketch dimensions it
    /// places (OperazioniSw.cs, QuotaSegmenti). So the chain keeps its
    /// decisions in the ModelImport* members of OperazioniSw
    /// (OperazioniTavola.cs), and this group reads them from the BUILT
    /// library by reflection: no SOLIDWORKS. It also reads the source of
    /// QuoteDalModello, because members that nobody calls would keep this
    /// group green after a revert of the call sites to literals.
    ///
    /// Finding M2 has its bench case (dimensions/dimensions_come_from_the_model:
    /// 4 dimensions, not 7); here only the constant is pinned.
    /// </summary>
    internal static class Zc212DrawingContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        // swInsertAnnotation_e (estratto_swconst, swInsertAnnotation_e.html).
        private const int Generic = 8, Marked = 32768, NotMarked = 524288, HoleCallout = 1048576;

        internal static void RunAll()
        {
            Gruppo("model_dimensions: the import chain stops at the marked masks (N9) and removes duplicates (M2)");

            TheMethodCallsTheMembers();

            var library = Path.Combine(Radice(), "Riscontro.Disegno", "bin", "Release", "Riscontro.Disegno.dll");
            if (!File.Exists(library))
            {
                NonQui("the import chain of model_dimensions, from the library",
                       library + " is missing: build the library first (build.ps1)");
                return;
            }

            Type sw;
            try
            {
                var redist = DoveStaLInterop();
                if (redist != null)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += (s, a) =>
                    {
                        var name = new AssemblyName(a.Name).Name;
                        if (!name.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase)) return null;
                        var file = Path.Combine(redist, name + ".dll");
                        return File.Exists(file) ? Assembly.LoadFrom(file) : null;
                    };
                }
                // From the bytes, as ApiCatalogContracts: the repository may sit
                // on a network drive, where LoadFrom is refused.
                var asm = Assembly.Load(File.ReadAllBytes(library));
                sw = asm.GetType("Riscontro.Disegno.OperazioniSw", false);
            }
            catch (Exception ex) { Segna("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (sw == null) { Segna("OperazioniSw is in the library", false, "type not found"); return; }

            try
            {
                var duplicates = sw.GetField("ModelImportV3DuplicateDims", Any);
                object value = duplicates?.GetValue(null);
                Segna("M2: InsertModelAnnotations3 gets DuplicateDims = true (true eliminates, the guide)",
                      value is bool && (bool)value,
                      duplicates == null ? "ModelImportV3DuplicateDims is missing" : "value " + value);
            }
            catch (Exception ex) { Segna("M2: ModelImportV3DuplicateDims can be read", false, ex.GetType().Name + ": " + ex.Message); }

            try
            {
                var masks = (int[])Call(sw, "ModelImportMasks", false);
                bool onlyMarked = masks != null && masks.Length > 0 &&
                                  masks.All(m => (m & Marked) != 0 && (m & NotMarked) == 0);
                Segna("N9: with all_dimensions false every mask has the MARKED bit and none the NOT-MARKED bit",
                      onlyMarked, masks == null ? "null" : string.Join(", ", masks));

                bool allTypes = (bool)Call(sw, "ModelImportAllTypes", false);
                bool everyViewDimension = (bool)Call(sw, "ModelImportMayTakeEveryViewDimension", false);
                Segna("N9: with all_dimensions false AllTypes is false and InsertModelDimensions(0) does not run",
                      !allTypes && !everyViewDimension,
                      "AllTypes " + allTypes + ", InsertModelDimensions " + everyViewDimension);

                int viewByView = (int)Call(sw, "ModelImportViewByViewTypes", false, HoleCallout);
                Segna("N9: with all_dimensions false the view-by-view fallback asks MARKED only (plus the callouts)",
                      viewByView == (Marked | HoleCallout),
                      viewByView + " (expected " + (Marked | HoleCallout) + ")");

                // The other side: all_dimensions true must still reach the
                // unmarked dimensions, or the fix would have cut the option.
                var allMasks = (int[])Call(sw, "ModelImportMasks", true);
                bool allOn = (bool)Call(sw, "ModelImportAllTypes", true)
                             && (bool)Call(sw, "ModelImportMayTakeEveryViewDimension", true);
                int allViewByView = (int)Call(sw, "ModelImportViewByViewTypes", true, 0);
                Segna("N9: with all_dimensions true the chain still reaches the unmarked dimensions",
                      allMasks != null && allMasks.Any(m => (m & NotMarked) != 0) && allOn &&
                      (allViewByView & NotMarked) != 0 && (allViewByView & Marked) != 0 && (allViewByView & Generic) != 0,
                      "masks " + (allMasks == null ? "null" : string.Join(", ", allMasks)) +
                      ", AllTypes and InsertModelDimensions " + allOn + ", view by view " + allViewByView);
            }
            catch (Exception ex) { Segna("N9: the ModelImport members can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        private static object Call(Type t, string method, params object[] args)
        {
            var m = ReflectionLog.Global.Method(t, method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method);
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        // ── the call sites, in the source ────────────────────────────────────
        private static void TheMethodCallsTheMembers()
        {
            var file = Path.Combine(Radice(), "Riscontro.Disegno", "OperazioniTavola.cs");
            string text;
            try { text = File.ReadAllText(file); }
            catch (Exception ex) { Segna("OperazioniTavola.cs can be read", false, ex.Message); return; }

            // The anchor is the comment that opens the chain inside the method,
            // right before the masks: every call checked below comes after it.
            const string anchor = "The masks, and every fallback below, come from the ModelImport";
            int start = text.IndexOf(anchor, StringComparison.Ordinal);
            if (start < 0) { Segna("the import chain of model_dimensions is in OperazioniTavola.cs", false, "anchor comment «" + anchor + "» not found"); return; }
            // Up to the closing brace of the method, the first one at the
            // indentation of a class member (eight spaces).
            int end = text.IndexOf("\n        }", start, StringComparison.Ordinal);
            if (end < 0) { Segna("QuoteDalModello ends in OperazioniTavola.cs", false, "closing brace not found"); return; }
            string body = text.Substring(start, end - start);

            var missing = new[]
            {
                "ModelImportMasks(",
                "ModelImportAllTypes(",
                "ModelImportMayTakeEveryViewDimension(",
                "ModelImportViewByViewTypes(",
                // The other half of the remedy: with nothing marked, a refusal
                // that says how to get the unmarked dimensions.
                "Pass all_dimensions=true",
            }.Where(c => body.IndexOf(c, StringComparison.Ordinal) < 0).ToList();
            int v3Calls = Count(body, ".InsertModelAnnotations3(");
            int duplicates = Count(body, "ModelImportV3DuplicateDims");
            // The NOT-MARKED bit may appear only inside the ModelImport members.
            bool notMarkedLiteral = body.IndexOf("NotMarkedForDrawing", StringComparison.Ordinal) >= 0
                                    || body.IndexOf("524288", StringComparison.Ordinal) >= 0;
            Segna("N9, M2: QuoteDalModello takes masks, AllTypes, fallbacks and DuplicateDims from the ModelImport members, and refuses with the way out",
                  missing.Count == 0 && v3Calls > 0 && duplicates == v3Calls && !notMarkedLiteral,
                  (missing.Count == 0 ? "every member called, the refusal there" : "missing: " + string.Join(", ", missing)) +
                  "; InsertModelAnnotations3 calls " + v3Calls + ", with ModelImportV3DuplicateDims " + duplicates +
                  (notMarkedLiteral ? "; a NOT-MARKED literal is back in the method" : ""));
        }

        private static int Count(string text, string what)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
            return n;
        }
    }
}
