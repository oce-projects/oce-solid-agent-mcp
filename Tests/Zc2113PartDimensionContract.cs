using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Programma;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// S1 IN PARTS ONLY (TODO entry Z-C21.13, the follow-up it left: «in the
    /// part the migration to GetSystemValue3(This, null)[0] can be done again»).
    ///
    /// ApiHelpers.ReadDimension, the reader of every PART dimension
    /// (set_dimension, link_dimension, dimension_tolerance, the dimensions of the part
    /// state), moved from the obsolete IDimension.SystemValue to
    /// GetSystemValue3(swThisConfiguration, null), which the probe
    /// probe_dimension_value3 measured equal to SystemValue on a part (linear and
    /// angular, either configuration active). The drawing readers stay on
    /// GetSystemValue2(""): on the dimensions a drawing owns GetSystemValue3
    /// answers null in every form (probe_dimension_drawing_owned, 30/9/2026),
    /// and the first migration, which moved them too, failed 12 drawing cases
    /// and was withdrawn.
    ///
    /// The bench sees the part readers work (case
    /// the_dimension_is_read_in_the_active_configuration, and the cases of the
    /// quote family), but it cannot make SOLIDWORKS answer another shape: those
    /// are checked here. The shape rule is a static method with no interop in
    /// its signature, called on the BUILT library by reflection (no SOLIDWORKS);
    /// the rest is read in the source. On library 0.145.0 the first, third and
    /// fourth checks are red (SystemValue, the angle of the state at 0 and the
    /// dimensions of a feature dropped, no shape rule); the second is the guard
    /// of the measured decision and is green on both.
    /// </summary>
    internal static class Zc2113PartDimensionContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        /// <summary>
        /// The files that call ApiHelpers.ReadDimension, each of them a PART
        /// reader: TrovaQuota answers null on anything but a part (set_dimension,
        /// link_dimension, dimension_tolerance), and the state reads the dimensions of a
        /// feature only after its part check. A new caller is added here on
        /// purpose, after checking that it reads a part.
        /// </summary>
        private static readonly string[] PartReaders =
        {
            "EquationOperations.cs", "SwOperations.cs", "DimensionToleranceOperations.cs", "DocumentState.cs",
        };

        internal static void RunAll()
        {
            Gruppo("S1 in parts only: the part dimensions through GetSystemValue3, the drawing readers left alone (Z-C21.13 follow-up)");

            string dir = Path.Combine(Radice(), "Oce.Solid.Agent.Core");
            string helpers = Read(dir, "ApiHelpers.cs");
            if (helpers != null)
            {
                string reader = Body(helpers, "internal static double ReadDimension(IDimension dimension)");
                bool migrated = reader != null &&
                                reader.Contains("GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null)") &&
                                !reader.Contains(".SystemValue");
                Segna("S1 in parts: ReadDimension reads GetSystemValue3(swThisConfiguration, null), and SystemValue is gone",
                      migrated,
                      reader == null ? "ReadDimension not found in ApiHelpers.cs" : "body: " + OneLine(reader));

                string inConfiguration = Body(helpers, "internal static double ReadDimensionInConfiguration(IDimension dimension, string configurationName)");
                bool drawingsKept = inConfiguration != null && inConfiguration.Contains(".GetSystemValue2(") &&
                                    !inConfiguration.Contains("GetSystemValue3(");
                var callers = Callers(dir, "ApiHelpers.ReadDimension(");
                var foreign = callers.Where(f => !PartReaders.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
                Segna("S1 stays out of drawings: ReadDimensionInConfiguration keeps GetSystemValue2, and only the part readers call ReadDimension",
                      drawingsKept && foreign.Count == 0 && callers.Count > 0,
                      "ReadDimensionInConfiguration on GetSystemValue2 " + drawingsKept + "; callers of ReadDimension: " +
                      (callers.Count == 0 ? "none found" : string.Join(", ", callers)) +
                      (foreign.Count == 0 ? "" : "; NOT known part readers: " + string.Join(", ", foreign) +
                                                 " (a drawing reader must stay on GetSystemValue2, probe_dimension_drawing_owned)"));
            }

            // The state reads a dimension in one method only, the row of a
            // dimension (an angle in degrees, anything else in millimetres):
            // both reads sit in their own try and land in a value that starts
            // as null. Until library 0.145.0 the angle started at 0 and the
            // linear read had no try of its own.
            string state = Read(dir, "DocumentState.cs");
            if (state != null)
            {
                var reads = state.Split('\n').Where(l => l.Contains("ApiHelpers.ReadDimension(")).Select(l => l.Trim()).ToList();
                bool guarded = reads.Count == 2 &&
                               reads.All(l => l.StartsWith("try { readValue = ", StringComparison.Ordinal) &&
                                              l.EndsWith("} catch { }", StringComparison.Ordinal));
                bool startsNull = state.Contains("object readValue = null;");
                Segna("the part state writes null, not 0, for a dimension it cannot read, and keeps the other dimensions of the feature",
                      guarded && startsNull,
                      reads.Count + " reads in DocumentState.cs (2 expected), each inside its own try " + guarded +
                      ", the value starts as null " + startsNull);
            }

            ShapeRule();
        }

        // ── the shape of a GetSystemValue3 answer ─────────────────────────────
        //
        // What the probe measured is a Double[1]. The positional form with every
        // configuration (Double[3] {0.01, 0, 0.025} on the probe part) is the
        // trap: its first element is the FIRST configuration, not the active
        // one, a plausible wrong number. It must throw, as must null (what a
        // drawing-owned dimension answers) and anything that is not an array.
        private static void ShapeRule()
        {
            const string rule = "S1: OneDimensionValue takes one number (Double[1], or Object[1] holding a double) and throws on every other shape";
            var library = Path.Combine(Radice(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NonQui(rule, library + " is missing: build the library first (build.ps1)");
                return;
            }
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
                var helpers = asm.GetType("Oce.Solid.Agent.Core.ApiHelpers", false);
                var method = helpers == null ? null : helpers.GetMethod("OneDimensionValue", Any);
                if (method == null) { Segna(rule, false, "ApiHelpers.OneDimensionValue is missing on this library"); return; }

                var wrong = new List<string>();
                // The answer of the rule: the value, or "refused" for the
                // InvalidOperationException that names the shape.
                Func<object, object> call = answer =>
                {
                    try { return (double)method.Invoke(null, new[] { answer }); }
                    catch (TargetInvocationException ex)
                    {
                        var inner = ex.InnerException;
                        return inner is InvalidOperationException && (inner.Message ?? "").Contains("did not answer one value")
                            ? "refused" : "threw " + (inner == null ? "?" : inner.GetType().Name);
                    }
                };
                const double fiveDegrees = 0.087266462599716474;
                var expected = new[]
                {
                    Tuple.Create((object)new[] { 0.01 }, (object)0.01, "Double[1] {0.01}"),
                    Tuple.Create((object)new object[] { fiveDegrees }, (object)fiveDegrees, "Object[1] {5 deg}"),
                    Tuple.Create((object)new[] { 0.01, 0.0, 0.025 }, (object)"refused", "Double[3], every configuration"),
                    Tuple.Create((object)new double[0], (object)"refused", "Double[0]"),
                    Tuple.Create((object)null, (object)"refused", "null"),
                    Tuple.Create((object)new object[] { "0.01" }, (object)"refused", "Object[1] {\"0.01\"}"),
                    Tuple.Create((object)0.01, (object)"refused", "a bare double"),
                };
                foreach (var e in expected)
                {
                    object got = call(e.Item1);
                    if (!Equals(got, e.Item2)) wrong.Add(e.Item3 + ": " + got + " instead of " + e.Item2);
                }
                Segna(rule, wrong.Count == 0,
                      wrong.Count == 0 ? expected.Length + " shapes as expected" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Segna(rule, false, ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>The body of the member declared by <paramref name="signature"/>, up to its closing brace at 8 spaces.</summary>
        private static string Body(string text, string signature)
        {
            int start = text.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0) return null;
            int end = text.IndexOf("\n        }", start, StringComparison.Ordinal);
            return end < 0 ? null : text.Substring(start, end - start);
        }

        /// <summary>The files of the library with a code line (not a comment) containing <paramref name="what"/>.</summary>
        private static List<string> Callers(string dir, string what)
        {
            var found = new List<string>();
            foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var rel = f.Substring(dir.Length).TrimStart('\\', '/').Replace('\\', '/');
                if (rel.StartsWith("bin/", StringComparison.Ordinal) || rel.StartsWith("obj/", StringComparison.Ordinal)) continue;
                if (File.ReadAllLines(f).Any(l => l.Contains(what) && !l.TrimStart().StartsWith("//", StringComparison.Ordinal)))
                    found.Add(Path.GetFileName(f));
            }
            found.Sort(StringComparer.Ordinal);
            return found;
        }

        private static string OneLine(string s)
        {
            var flat = string.Join(" ", s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
            return flat.Length <= 200 ? flat : flat.Substring(0, 197) + "...";
        }

        private static string Read(string dir, string file)
        {
            try { return File.ReadAllText(Path.Combine(dir, file)); }
            catch (Exception ex) { Segna(file + " can be read", false, ex.Message); return null; }
        }
    }
}
