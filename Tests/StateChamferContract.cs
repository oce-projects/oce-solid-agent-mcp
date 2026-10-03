using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Programma;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE STATE READS A CHAMFER BY ITS TYPE (N29, Z-C21.3).
    ///
    /// Up to library 0.141.0 the state read every chamfer as an edge chamfer
    /// (side 0, side 1 when different, the angle when not zero), so a VERTEX
    /// chamfer went into the state with no size at all. The probe of
    /// 28/9/2026 (Z-C21.3, chamfer probe, V1 and V2) measured the remedy:
    /// Type 3, the three vertex distances 2/3/4 and 3/3/3 read back as asked,
    /// the edge distances and the angle 0.
    ///
    /// No bench case can build this: no operation of the bridge makes a
    /// vertex chamfer (chamfer_edges takes edges). So the decision is kept in
    /// StatoDocumento.ChamferSizes, which takes the reads as functions and no
    /// interop type, and this group calls it on the BUILT library by
    /// reflection, with the numbers of the probe: no SOLIDWORKS. The
    /// functions also record which reads were asked for, because the defect
    /// was reading a vertex chamfer with the edge distances.
    /// </summary>
    internal static class StateChamferContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Gruppo("the state reads a chamfer by its type (N29)");

            var library = Path.Combine(Radice(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NonQui("the state reads a chamfer by its type",
                       library + " is missing: build the library first (build.ps1)");
                return;
            }

            Assembly asm;
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
                // From the bytes, as the other library contracts: the repository
                // may sit on a network drive, where LoadFrom is refused.
                asm = Assembly.Load(File.ReadAllBytes(library));
            }
            catch (Exception ex) { Segna("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }

            Check(asm);
        }

        /// <summary>The checks, on a library already loaded.</summary>
        internal static void Check(Assembly asm)
        {
            var state = asm.GetType("Oce.Solid.Agent.Core.StatoDocumento", false);
            var method = state == null ? null : state.GetMethod("ChamferSizes", Any);
            if (method == null)
            {
                Segna("N29: StatoDocumento.ChamferSizes is there", false,
                      state == null ? "no Oce.Solid.Agent.Core.StatoDocumento in the library"
                                    : "missing on this library: it is older than the fix");
                return;
            }

            try
            {
                // V1 of the probe: Type 3, vertex distances 2/3/4 mm, edge distances and angle 0.
                var v1 = Read(method, 3, new[] { 0.0, 0.0 }, 0.0, new[] { 0.002, 0.003, 0.004 });
                // V2: the same corner, 3/3/3.
                var v2 = Read(method, 3, new[] { 0.0, 0.0 }, 0.0, new[] { 0.003, 0.003, 0.003 });
                Segna("N29: a vertex chamfer is read with its three distances (probe V1 2/3/4, V2 3/3/3)",
                      v1.Written == "{vertex_distances_mm=[2, 3, 4]}" && v2.Written == "{vertex_distances_mm=[3, 3, 3]}",
                      "V1 " + v1.Written + ", V2 " + v2.Written + " (expected {vertex_distances_mm=[2, 3, 4]} and [3, 3, 3])");
                Segna("N29: and never with an edge distance or an angle",
                      v1.Asked == "vertex 0, vertex 1, vertex 2" && v2.Asked == v1.Asked,
                      "asked: " + v1.Asked + " (the old reading asked edge 0, edge 1 and the angle, and wrote {})");

                // C9 of the probe: angle-distance 5 at 30 degrees, Type 1, side 1 reads 0.
                var ad = Read(method, 1, new[] { 0.005, 0.0 }, 30 * Math.PI / 180, new[] { 0.0, 0.0, 0.0 });
                // C1/C7 of the probe: distance-distance 3 x 6, Type 2. SOLIDWORKS
                // read the angle as 0 there; 45 is offered here on purpose (the
                // bridge passes 45 for DD too, C7-1), to see that the rule does
                // not ask for it: «relevant only for an angle-distance type».
                var dd = Read(method, 2, new[] { 0.003, 0.006 }, 45 * Math.PI / 180, new[] { 0.0, 0.0, 0.0 });
                Segna("N29: the edge chamfers keep their sizes, read by type (probe C9 5 x 30, C1 3 x 6)",
                      ad.Written == "{angle_deg=30, leg1_mm=5}" && ad.Asked == "edge 0, angle" &&
                      dd.Written == "{leg1_mm=3, leg2_mm=6}" && dd.Asked == "edge 0, edge 1",
                      "angle-distance " + ad.Written + " asking " + ad.Asked +
                      "; distance-distance " + dd.Written + " asking " + dd.Asked);

                // swChamferEqualDistance (16), not measured: the reading of before.
                var eq = Read(method, 16, new[] { 0.002, 0.002 }, 0.0, new[] { 0.0, 0.0, 0.0 });
                Segna("N29: a type the rule does not know keeps the reading of before",
                      eq.Written == "{leg1_mm=2}" && eq.Asked == "edge 0, edge 1, angle",
                      "equal distance 2: " + eq.Written + " asking " + eq.Asked);
            }
            catch (Exception ex) { Segna("N29: ChamferSizes can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        private sealed class Reading
        {
            public string Written, Asked;
        }

        /// <summary>
        /// Calls ChamferSizes with reads in metres and radians, as SOLIDWORKS
        /// gives them, and returns what it wrote and what it asked for.
        /// </summary>
        private static Reading Read(MethodInfo method, int type, double[] edge, double angleRad, double[] vertex)
        {
            var asked = new List<string>();
            Func<int, double> edgeDistance = side => { asked.Add("edge " + side); return edge[side]; };
            Func<double> angle = () => { asked.Add("angle"); return angleRad; };
            Func<int, double> vertexDistance = side => { asked.Add("vertex " + side); return vertex[side]; };
            var entry = new Dictionary<string, object>();
            try { method.Invoke(null, new object[] { type, edgeDistance, angle, vertexDistance, entry }); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
            return new Reading { Written = Write(entry), Asked = string.Join(", ", asked) };
        }

        private static string Write(Dictionary<string, object> entry)
        {
            return "{" + string.Join(", ", entry.Keys.OrderBy(k => k, StringComparer.Ordinal)
                                                .Select(k => k + "=" + Value(entry[k]))) + "}";
        }

        private static string Value(object v)
        {
            var list = v as System.Collections.IEnumerable;
            if (list != null && !(v is string))
                return "[" + string.Join(", ", list.Cast<object>().Select(Value)) + "]";
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
