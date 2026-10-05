using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE ASSEMBLY FIXES OF Z-C21.2-TER, WHERE THE BENCH CANNOT BUILD THE CASE.
    ///
    /// Four findings of the audit were measured REAL by the Z-C21.3 probes on
    /// 28/9/2026 (the TODO entry of the Z-C21.3 outcome). The bench can build only one of
    /// their situations (N26, case assiemi/the_mirrored_instance_turns_as_asked);
    /// the others need what a bench case cannot write:
    ///
    ///   C4-2  an assembly cut on one instance (user bodies): the bench has no
    ///         assembly feature;
    ///   N27   a sub-assembly whose configuration promotes or hides its
    ///         children in the BOM: the bench builds parts only;
    ///   L12-8 a component file renamed away and a same-name file in the
    ///         current SOLIDWORKS directory: the bench renames no file.
    ///
    /// Each fix keeps its decision in a static method with no interop in the
    /// signature, and this group calls it on the BUILT library by reflection,
    /// with the numbers the probe read. N26's axis rule is checked here too,
    /// on the four orientations the probe measured. L12-8 is a flag in one
    /// call, so it is checked on the source: every GetDependencies call of the
    /// library looks where the files were saved (Searchflag false).
    ///
    /// On library 0.141.0 every check but L12-8 is red because the method is
    /// missing, and L12-8 is red because the call passes Searchflag true. The
    /// indented check of N27 follows the M23 measurement since Z-C21.11 and is
    /// red on library 0.146.0, which still counted every part there.
    /// </summary>
    internal static class AssembliesZc212terContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the assembly fixes of Z-C21.2-ter the bench cannot reach (N26, C4-2, N27, L12-8)");

            SearchFlagInTheSource();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the Z-C21.2-ter assembly fixes",
                       library + " is missing: build the library first (build.ps1)");
                return;
            }

            Type sw;
            try
            {
                var redist = WhereTheInteropIs();
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
                sw = asm.GetType("Oce.Solid.Agent.Core.SwOperations", false);
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (sw == null) { Check("SwOperations is in the library", false, "type not found"); return; }

            MirrorAxes(sw);
            StepSolids(sw);
            BomItems(sw);
        }

        private static object Call(Type t, string method, params object[] args)
        {
            var m = ReflectionLog.Global.Method(t, method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method);
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        // ── N26: the axes of a mirrored instance ────────────────────────────
        //
        // The Z-C21.3 probe of the mirrored instance's axes, Right plane, the original unrotated:
        //   0 X'(-1,0,0) Y'(0,1,0) Z'(0,0,-1)    1 X'(1,0,0) Y'(0,1,0) Z'(0,0,1)
        //   2 X'(-1,0,0) Y'(0,-1,0) Z'(0,0,1)    3 X'(1,0,0) Y'(0,-1,0) Z'(0,0,-1)
        // and the centre check passed on all four.
        private static void MirrorAxes(Type sw)
        {
            try
            {
                var identity = new[] { new double[] { 1, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 0, 1 } };
                var measured = new[]
                {
                    new[] { new double[] { -1, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 0, -1 } },
                    new[] { new double[] { 1, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 0, 1 } },
                    new[] { new double[] { -1, 0, 0 }, new double[] { 0, -1, 0 }, new double[] { 0, 0, 1 } },
                    new[] { new double[] { 1, 0, 0 }, new double[] { 0, -1, 0 }, new double[] { 0, 0, -1 } },
                };
                var own = new List<string>();
                var others = new List<string>();
                for (int k = 0; k < 4; k++)
                    for (int asked = 0; asked < 4; asked++)
                    {
                        var problem = (string)Call(sw, "MirrorAxesProblem", identity, measured[k], 0, asked);
                        if (k == asked && problem != null) own.Add(k + ": " + problem);
                        if (k != asked && problem == null) others.Add("measured " + k + " accepted as " + asked);
                    }
                Check("N26: each measured orientation matches the one that was set (Right plane)",
                      own.Count == 0, own.Count == 0 ? "4 of 4" : string.Join(" | ", own));
                Check("N26: and none passes for another one, which the centre check could not see",
                      others.Count == 0, others.Count == 0 ? "12 wrong pairs refused" : string.Join(" | ", others));

                var names = sw.GetField("MirrorOrientationNames", Any)?.GetValue(null) as string[];
                Check("N26: the four «orientation» names follow swMirrorComponentOrientation2_e 0..3",
                      names != null && names.Length == 4 && names[0] == "mirrored_x_mirrored_y" &&
                      names[1] == "mirrored_and_flipped_x_mirrored_y" && names[2] == "mirrored_x_mirrored_and_flipped_y" &&
                      names[3] == "mirrored_and_flipped_x_mirrored_and_flipped_y",
                      names == null ? "field missing" : string.Join(", ", names));

                // The extrapolation to the other planes, written down so that the
                // bench case (steps 2 and 3) and this rule cannot drift apart.
                var front = (double[][])Call(sw, "ExpectedMirrorAxes", identity, 2, 0);
                var top = (double[][])Call(sw, "ExpectedMirrorAxes", identity, 1, 2);
                Check("N26: Front plane, default orientation: the axes stay as they are; Top plane, orientation 2: the same",
                      SameAxes(front, identity) && SameAxes(top, identity),
                      "Front " + Axes(front) + ", Top " + Axes(top));
            }
            catch (Exception ex) { Check("N26: MirrorAxesProblem can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        private static bool SameAxes(double[][] a, double[][] b)
        {
            if (a == null || b == null || a.Length != 3 || b.Length != 3) return false;
            for (int k = 0; k < 3; k++)
                for (int j = 0; j < 3; j++)
                    if (Math.Abs(a[k][j] - b[k][j]) > 1e-9) return false;
            return true;
        }

        private static string Axes(double[][] a)
        {
            if (a == null) return "null";
            return string.Join(" ", a.Select(v => "(" + string.Join(",", v.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))) + ")"));
        }

        // ── C4-2: the solids the STEP of an assembly must hold ──────────────
        //
        // The Z-C21.3 probe of the user bodies: P 40 x 40 x 10 twice; K0 no cut (STEP 1
        // solid), K1 a 10 x 10 hole through instance 1 (user body 15000 mm3,
        // STEP 2), K2 a slot splitting instance 1 (two user bodies, STEP 3),
        // K3 Q in two configurations of identical geometry (STEP 2). The old
        // count said 1, 1, 2, 2.
        private static void StepSolids(Type sw)
        {
            try
            {
                const string p = "c:\\w\\P.SLDPRT|Default", q1 = "c:\\w\\Q.SLDPRT|Default", q2 = "c:\\w\\Q.SLDPRT|Twin";
                int k0 = (int)Call(sw, "ExpectedStepSolids", new[] { p, p }, new[] { 1, 1 }, new[] { 0, 0 });
                int k1 = (int)Call(sw, "ExpectedStepSolids", new[] { p, p }, new[] { 1, 1 }, new[] { 1, 0 });
                int k1b = (int)Call(sw, "ExpectedStepSolids", new[] { p, p }, new[] { 1, 1 }, new[] { 0, 1 });
                int k2 = (int)Call(sw, "ExpectedStepSolids", new[] { p, p }, new[] { 2, 1 }, new[] { 2, 0 });
                int k3 = (int)Call(sw, "ExpectedStepSolids", new[] { q1, q2 }, new[] { 1, 1 }, new[] { 0, 0 });
                int bad = (int)Call(sw, "ExpectedStepSolids", new[] { p, p }, new[] { 1 }, new[] { 0, 0 });
                Check("C4-2: the counts the STEP files had: 1 uncut, 2 with the hole, 3 with the slot, 2 for two configurations",
                      k0 == 1 && k1 == 2 && k2 == 3 && k3 == 2,
                      "K0 " + k0 + ", K1 " + k1 + ", K2 " + k2 + ", K3 " + k3 + " (expected 1, 2, 3, 2)");
                Check("C4-2: whichever instance comes first (GetComponents «can be in any order»)",
                      k1b == 2, "the cut instance second: " + k1b);
                Check("C4-2: arrays that do not line up are no count", bad == -1, bad.ToString());
            }
            catch (Exception ex) { Check("C4-2: ExpectedStepSolids can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── N27: the BOM items of an assembly with sub-assemblies ───────────
        //
        // The Z-C21.3 probe of envelopes and BOM children, the tables SOLIDWORKS wrote (item: qty):
        //   T_prom (A + S_prom = 2 B, Promote)  top level: A 1, B 2   parts only: A 1, B 2
        //   T_hide (A + S_hide = 2 B, Hide)     top level: A 1, S_hide 1   parts only: A 1, S_hide 1
        //   T_show (A + S_show = 2 B, Show)     top level: A 1, S_show 1   parts only: A 1, B 2
        //   T_env  (A + envelopes E, F)         both: A 1 (the envelopes read ExcludeFromBOM true)
        // The old count refused T_prom top level (2 instances against 3 pieces)
        // and T_hide parts only (3 instances against 2 pieces).
        // The Z-C21.11 probe of the indented table (M23, 30/9/2026), Detailed numbering:
        //   T_hide indented: A 1, S_hide 1 (the old count, every part: A 1, B 2, refused it)
        private const int PartsOnly = 1, TopLevelOnly = 2, Indented = 3;
        private const int Hide = 1, Show = 2, Promote = 3;

        private static void BomItems(Type sw)
        {
            try
            {
                var lines = new List<string>();
                bool all = true;
                Action<string, int, object[], string> check = (label, bomType, tree, expected) =>
                {
                    var got = Items(sw, tree, bomType);
                    bool ok = got == expected;
                    all &= ok;
                    lines.Add(label + " " + (ok ? got : got + " instead of " + expected));
                };
                // (key, parent index, assembly, child display, out of the BOM)
                var prom = Tree("A", -1, false, -1, false, "S_prom", -1, true, Promote, false, "B", 1, false, -1, false, "B", 1, false, -1, false);
                var hide = Tree("A", -1, false, -1, false, "S_hide", -1, true, Hide, false, "B", 1, false, -1, false, "B", 1, false, -1, false);
                var show = Tree("A", -1, false, -1, false, "S_show", -1, true, Show, false, "B", 1, false, -1, false, "B", 1, false, -1, false);
                var env = Tree("A", -1, false, -1, false, "E", -1, false, -1, true, "F", -1, false, -1, true);
                check("T_prom top level", TopLevelOnly, prom, "A 1, B 2");
                check("T_hide parts only", PartsOnly, hide, "A 1, S_hide 1");
                Check("N27: the two tables the old count refused: promoted children at the top, a hiding sub-assembly among the parts",
                      all, string.Join("; ", lines));

                lines.Clear();
                all = true;
                check("T_prom parts only", PartsOnly, prom, "A 1, B 2");
                check("T_hide top level", TopLevelOnly, hide, "A 1, S_hide 1");
                check("T_show top level", TopLevelOnly, show, "A 1, S_show 1");
                check("T_show parts only", PartsOnly, show, "A 1, B 2");
                check("T_env top level", TopLevelOnly, env, "A 1");
                check("T_env parts only", PartsOnly, env, "A 1");
                Check("N27: and the six tables it already read right are still read the same",
                      all, string.Join("; ", lines));

                // Measured on 30/9/2026 (M23, the Z-C21.11 probe of the indented BOM):
                // the indented table of T_hide is A 1, S_hide 1, the hiding
                // sub-assembly one row with nothing under it. Up to library
                // 0.146.0 this check pinned the old count (A 1, B 2), written when
                // the indented table had not been measured; Zc2111ComponentsContract
                // checks the other indented trees.
                lines.Clear();
                all = true;
                check("T_hide indented", Indented, hide, "A 1, S_hide 1");
                Check("N27/M23: the indented table of T_hide, as measured: the hiding sub-assembly is one row with nothing under it",
                      all, string.Join("; ", lines));
            }
            catch (Exception ex) { Check("N27: ExpectedBomItems can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        private static object[] Tree(params object[] flat) { return flat; }

        /// <summary>The items as «key qty», sorted by key, or «null».</summary>
        private static string Items(Type sw, object[] flat, int bomType)
        {
            int n = flat.Length / 5;
            var keys = new string[n];
            var parents = new int[n];
            var isAssembly = new bool[n];
            var display = new int[n];
            var skipped = new bool[n];
            for (int i = 0; i < n; i++)
            {
                keys[i] = (string)flat[5 * i];
                parents[i] = (int)flat[5 * i + 1];
                isAssembly[i] = (bool)flat[5 * i + 2];
                display[i] = (int)flat[5 * i + 3];
                skipped[i] = (bool)flat[5 * i + 4];
            }
            var got = Call(sw, "ExpectedBomItems", keys, parents, isAssembly, display, skipped, bomType)
                      as Dictionary<string, int>;
            if (got == null) return "null";
            return string.Join(", ", got.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => k + " " + got[k]));
        }

        // ── L12-8: GetDependencies looks where the files were saved ─────────
        //
        // The Z-C21.3 probe of the same-name file: with a component not loaded and a same-name
        // file in the current SOLIDWORKS directory, Searchflag=true answered
        // that file (it exists, so nothing was «missing»), Searchflag=false the
        // saved path (missing). After a true call had found it, false answered
        // it too. So no call of the library may pass Searchflag true.
        private static void SearchFlagInTheSource()
        {
            var folder = Path.Combine(Root(), "Oce.Solid.Agent.Core");
            if (!Directory.Exists(folder)) { Check("L12-8: the library source is there", false, "missing " + folder); return; }
            var call = new Regex(@"\bGetDependencies\s*\(\s*(\w+)\s*,\s*(\w+)");
            var calls = new List<string>();
            var searching = new List<string>();
            foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                if (file.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    file.IndexOf(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var state = new StripState();
                int number = 0;
                foreach (var raw in File.ReadAllLines(file))
                {
                    number++;
                    string comment;
                    var code = ObsoleteCallScanner.Strip(raw, state, out comment);
                    foreach (Match m in call.Matches(code))
                    {
                        string where = Path.GetFileName(file) + ":" + number;
                        calls.Add(where);
                        if (m.Groups[2].Value != "false") searching.Add(where + " Searchflag «" + m.Groups[2].Value + "»");
                    }
                }
            }
            Check("L12-8: every GetDependencies call of the library passes Searchflag false (the saved paths)",
                  calls.Count > 0 && searching.Count == 0,
                  calls.Count == 0 ? "no call found: the check would pass on nothing"
                  : searching.Count == 0 ? calls.Count + " call(s): " + string.Join(", ", calls)
                  : string.Join(" | ", searching));
        }
    }
}
