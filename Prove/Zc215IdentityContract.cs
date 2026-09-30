using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// THE TWO IDENTITY RULES OF Z-C21.5 THAT THE BENCH CANNOT REACH (S19, S7).
    ///
    /// S19, the base planes. Until Z-C21.5 `frontale`, `superiore` and `destro` were
    /// the first / second / third RefPlane of the tree (TrovaPianoBase,
    /// SelezionaPiano, the section of VistaDelPezzo, specchia_componente). The
    /// Z-C21.3 probe of C3-6 found every ReorderFeature that tried to put a plane
    /// before the three default ones refused, so on the bench template the old
    /// rule and the new one pick the same plane: only a part TEMPLATE made by hand
    /// with an extra plane first tells them apart, and the bench has none. The
    /// decision is therefore kept in Riferimenti.BasePlaneIndex, with no interop
    /// in its signature, and checked here on the plane lists such a template
    /// would give, with the normals and origins the probe read on the real one.
    ///
    /// S7, the feature just created. The decision of OperazioniSw.FeatureBorn
    /// (the one feature of a type whose name was not there before the call) is
    /// OperazioniSw.BornFeatureIndex, checked here on the tree of a sheet-metal
    /// part, where the last feature is the flat pattern (the bench covers the
    /// same rule end to end with the_axis_and_the_planes_of_a_sheet).
    ///
    /// Both are called on the BUILT library by reflection, as
    /// Zc2110HelpersContract does: no SOLIDWORKS. On library 0.145.0 the methods
    /// are not there and every check of this group is red.
    /// </summary>
    internal static class Zc215IdentityContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly double[] Front = { 0, 0, 1 };
        private static readonly double[] Top = { 0, 1, 0 };
        private static readonly double[] Right = { 1, 0, 0 };
        private static readonly double[] Origin = { 0, 0, 0 };

        internal static void RunAll()
        {
            Gruppo("the identity rules of Z-C21.5 the bench cannot reach (S19 base planes, S7 feature born)");

            var library = Path.Combine(Radice(), "Riscontro.Disegno", "bin", "Release", "Riscontro.Disegno.dll");
            if (!File.Exists(library))
            {
                NonQui("the identity rules of Z-C21.5",
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
                // From the bytes, as Zc2110HelpersContract: the repository may sit
                // on a network drive, where LoadFrom is refused.
                asm = Assembly.Load(File.ReadAllBytes(library));
            }
            catch (Exception ex) { Segna("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }

            Type references = asm.GetType("Riscontro.Disegno.Riferimenti", false);
            Type sw = asm.GetType("Riscontro.Disegno.OperazioniSw", false);
            if (references == null || sw == null)
            {
                Segna("the two library types are there", false,
                      "Riferimenti " + (references != null) + ", OperazioniSw " + (sw != null));
                return;
            }

            BasePlanes(references);
            FeatureBorn(sw);
        }

        private static object Invoke(Type t, string method, object[] args)
        {
            var m = t.GetMethod(method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method);
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        private static int PlaneIndex(Type references, List<double[]> normals, List<double[]> origins, string name)
        {
            return (int)Invoke(references, "BasePlaneIndex", new object[] { normals, origins, name });
        }

        private static double[] Mm(double x, double y, double z)
        {
            return new[] { x / 1000, y / 1000, z / 1000 };
        }

        // ── S19: the base plane by its normal through the origin ──────────────
        private static void BasePlanes(Type references)
        {
            try
            {
                // The template as it comes, as the probe of C3-6 read it (P0).
                var n = new List<double[]> { Front, Top, Right };
                var o = new List<double[]> { Origin, Origin, Origin };
                int f = PlaneIndex(references, n, o, "frontale");
                int t = PlaneIndex(references, n, o, "superiore");
                int r = PlaneIndex(references, n, o, "destro");
                Segna("S19: on the template as it comes, front, top and right are the 1st, 2nd and 3rd plane",
                      f == 0 && t == 1 && r == 2, f + ", " + t + ", " + r + " (expected 0, 1, 2)");

                // One plane more BEFORE the three: the front plane offset by 20 mm,
                // as the probe read it (normal (0, 0, 1) through (0, 0, 20)). The
                // old rule took it as the front plane, and the top plane as the right.
                n = new List<double[]> { Front, Front, Top, Right };
                o = new List<double[]> { Mm(0, 0, 20), Origin, Origin, Origin };
                f = PlaneIndex(references, n, o, "frontale");
                t = PlaneIndex(references, n, o, "superiore");
                r = PlaneIndex(references, n, o, "destro");
                Segna("S19: an offset plane before the three does not move them (the old rule said 0, 1, 2)",
                      f == 1 && t == 2 && r == 3, f + ", " + t + ", " + r + " (expected 1, 2, 3)");

                // A plane through the origin facing -Y before the top plane, as the
                // probe's plane through three points read (0, -1, 0): the sign
                // decides the side of an offset and of an extrusion, so it is not
                // the top plane.
                n = new List<double[]> { Front, new[] { 0.0, -1.0, 0.0 }, Top, Right };
                o = new List<double[]> { Origin, Origin, Origin, Origin };
                t = PlaneIndex(references, n, o, "superiore");
                Segna("S19: a plane through the origin facing -Y is not the top plane", t == 2,
                      t + " (expected 2)");

                // A plane through the origin turned by one degree about Y, before the
                // right plane: nearly the right plane is not the right plane.
                double c = Math.Cos(Math.PI / 180), s = Math.Sin(Math.PI / 180);
                n = new List<double[]> { Front, Top, new[] { c, 0, s }, Right };
                o = new List<double[]> { Origin, Origin, Origin, Origin };
                r = PlaneIndex(references, n, o, "destro");
                Segna("S19: a plane turned by one degree is not a base plane", r == 3, r + " (expected 3)");

                // An unreadable plane first (a null frame) is skipped, not guessed.
                n = new List<double[]> { null, Front, Top, Right };
                o = new List<double[]> { null, Origin, Origin, Origin };
                f = PlaneIndex(references, n, o, "frontale");
                Segna("S19: a plane whose transform was not read is skipped", f == 1, f + " (expected 1)");

                // Two planes identical in normal, sign and origin: the first, which
                // is the template's own (the probe found no way to put one before).
                n = new List<double[]> { Front, Front, Top, Right };
                o = new List<double[]> { Origin, Origin, Origin, Origin };
                f = PlaneIndex(references, n, o, "frontale");
                Segna("S19: between two planes the geometry cannot tell apart, the first", f == 0,
                      f + " (expected 0)");

                // No plane with the right normal: -1, never another plane.
                n = new List<double[]> { Front, Top };
                o = new List<double[]> { Origin, Origin };
                r = PlaneIndex(references, n, o, "destro");
                int wrongCase = PlaneIndex(references, new List<double[]> { Front, Top, Right },
                                           new List<double[]> { Origin, Origin, Origin }, "Superiore");
                int unknown = PlaneIndex(references, new List<double[]> { Front, Top, Right },
                                         new List<double[]> { Origin, Origin, Origin }, "middle");
                Segna("S19: no plane with the normal, a name in another case, an unknown name: -1",
                      r == -1 && wrongCase == -1 && unknown == -1,
                      r + ", " + wrongCase + ", " + unknown + " (expected -1, -1, -1)");
            }
            catch (Exception ex) { Segna("S19: Riferimenti.BasePlaneIndex can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── S7: the feature born, not the last feature of the tree ────────────
        private static void FeatureBorn(Type sw)
        {
            try
            {
                // A sheet-metal part: the flat pattern stays at the bottom, and the
                // new axis goes before it.
                var before = new HashSet<string>(StringComparer.Ordinal) { "Origin", "SheetMetal1", "BaseFlange1", "FlatPattern1" };
                var names = new List<string> { "Origin", "SheetMetal1", "BaseFlange1", "Axis1", "FlatPattern1" };
                var types = new List<string> { "OriginProfileFeature", "SheetMetal", "SMBaseFlange", "RefAxis", "FlatPattern" };
                var args = new object[] { names, types, before, "RefAxis", 0 };
                int index = (int)Invoke(sw, "BornFeatureIndex", args);
                Segna("S7: on a sheet-metal part the axis born is the 4th feature, not the flat pattern at the bottom",
                      index == 3 && (int)args[4] == 1, index + ", born " + args[4] + " (expected 3, born 1)");

                // The same list is what SenzaLoSchizzo takes the newest born from:
                // on a sheet it is the feature before the flat pattern.
                var indices = Invoke(sw, "BornFeatureIndices", new object[] { names, before }) as List<int>;
                Segna("S7: what was born is the axis alone, and its newest is not the flat pattern",
                      indices != null && indices.Count == 1 && indices[0] == 3,
                      indices == null ? "null" : "[" + string.Join(", ", indices) + "] (expected [3])");

                // Nothing born: -1, born 0.
                args = new object[] { new List<string>(before), new List<string>(types.Take(4)), before, "RefAxis", 0 };
                index = (int)Invoke(sw, "BornFeatureIndex", args);
                Segna("S7: nothing born is -1, not the last feature", index == -1 && (int)args[4] == 0,
                      index + ", born " + args[4] + " (expected -1, born 0)");

                // Two planes born: -1, the choice is not made.
                var twoNames = new List<string> { "Origin", "Plane4", "Plane5", "FlatPattern1" };
                var twoTypes = new List<string> { "OriginProfileFeature", "RefPlane", "RefPlane", "FlatPattern" };
                args = new object[] { twoNames, twoTypes, new HashSet<string> { "Origin", "FlatPattern1" }, "RefPlane", 0 };
                index = (int)Invoke(sw, "BornFeatureIndex", args);
                Segna("S7: two planes born are two candidates, not «the later one»", index == -1 && (int)args[4] == 2,
                      index + ", born " + args[4] + " (expected -1, born 2)");

                // Any type: one born is taken, a sketch and an extrusion are two.
                var shellNames = new List<string> { "Origin", "Extrude1", "Shell1", "FlatPattern1" };
                var shellTypes = new List<string> { "OriginProfileFeature", "Extrusion", "Shell", "FlatPattern" };
                args = new object[] { shellNames, shellTypes, new HashSet<string> { "Origin", "Extrude1", "FlatPattern1" }, null, 0 };
                int shell = (int)Invoke(sw, "BornFeatureIndex", args);
                var bothNames = new List<string> { "Origin", "Sketch1", "Extrude1" };
                var bothTypes = new List<string> { "OriginProfileFeature", "ProfileFeature", "Extrusion" };
                var args2 = new object[] { bothNames, bothTypes, new HashSet<string> { "Origin" }, null, 0 };
                int both = (int)Invoke(sw, "BornFeatureIndex", args2);
                Segna("S7: with no type, the one feature born is taken and two born are refused",
                      shell == 2 && both == -1 && (int)args2[4] == 2,
                      shell + ", " + both + " born " + args2[4] + " (expected 2, -1 born 2)");
            }
            catch (Exception ex) { Segna("S7: OperazioniSw.BornFeatureIndex can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }
    }
}
