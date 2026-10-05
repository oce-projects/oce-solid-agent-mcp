using System;
using System.IO;
using System.Linq;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// AN EMPTY PROFILE SIZE IS ACCEPTED ONLY ON A ONE-SIZE FILE (N25, Z-C21.3).
    ///
    /// Up to library 0.141.0 the structural member refused an empty size
    /// outright, though the guide (IFeatureManager~InsertStructuralWeldment5.html,
    /// Remarks) gives the empty string as THE configuration name for a
    /// SOLIDWORKS-supplied profile. The Z-C21.3 probe of the member group
    /// (28/9/2026) measured that the empty string works, and what it does: on
    /// the 2021 «iso\square tube.sldlfp», four sizes (20 x 20 x 2, 30 x 30 x
    /// 2.6, 40 x 40 x 4, 80 x 80 x 5), it built the 20 x 20 without an error
    /// (N7). So the library accepts the empty size on a file with one size and
    /// refuses it, naming the sizes, on a file with several.
    ///
    /// The refusal on the four-size file is a bench case
    /// (an_empty_profile_size_names_the_sizes). The ACCEPTANCE is not: no
    /// one-size profile is known to exist on every station (the per-size file
    /// under the Italian iso folder was never measured for its configurations),
    /// and a case written against a guess is a memory, not a test. So the
    /// decision is kept in SwOperations.EmptyProfileSizeRefusal, which takes
    /// the sizes as strings and no interop type, and this group calls it on
    /// the BUILT library by reflection: no SOLIDWORKS.
    /// </summary>
    internal static class WeldmentConfigurationZc212terContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("an empty profile size is accepted only on a one-size file (N25)");

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("an empty profile size is accepted only on a one-size file",
                       library + " is missing: build the library first (build.ps1)");
                return;
            }

            Assembly asm;
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
                // From the bytes, as the other library contracts: the repository
                // may sit on a network drive, where LoadFrom is refused.
                asm = Assembly.Load(File.ReadAllBytes(library));
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }

            CheckLibrary(asm);
        }

        /// <summary>The checks, on a library already loaded.</summary>
        internal static void CheckLibrary(Assembly asm)
        {
            var sw = asm.GetType("Oce.Solid.Agent.Core.SwOperations", false);
            var method = sw == null ? null : sw.GetMethod("EmptyProfileSizeRefusal", Any);
            if (method == null)
            {
                Check("N25: OperazioniSw.EmptyProfileSizeRefusal is there", false,
                      sw == null ? "no Oce.Solid.Agent.Core.OperazioniSw in the library"
                                 : "missing on this library: it is older than the fix");
                return;
            }

            try
            {
                // The guide's case: one file, one size. Nothing to choose.
                string one = Call(method, "120 x 40 x 5", new[] { "Default" }, null);
                Check("N25: an empty size on a one-size file is accepted",
                      one == null,
                      one == null ? "accepted" : "refused: " + one);

                // The probe's file (N7): four sizes, the empty name built the 20 x 20.
                var probeSizes = new[] { "20 x 20 x 2", "30 x 30 x 2.6", "40 x 40 x 4", "80 x 80 x 5" };
                string four = Call(method, "square tube", probeSizes, null);
                Check("N25: an empty size on the probe's four-size file is refused, naming every size",
                      four != null && four.Contains("is empty") && four.Contains("has 4 sizes") &&
                      probeSizes.All(s => four.Contains(s)),
                      four ?? "(accepted: the 20 x 20 of N7 would be built without an error)");

                // Sizes that cannot be read: the file may have several, so no guess.
                string unread = Call(method, "square tube", null, "the file is locked");
                string none = Call(method, "square tube", new string[0], null);
                Check("N25: an empty size is refused when the sizes cannot be read",
                      unread != null && unread.Contains("cannot read") && unread.Contains("the file is locked") &&
                      none != null && none.Contains("cannot read"),
                      "unread: " + (unread ?? "(accepted)") + " | none: " + (none ?? "(accepted)"));

                // A long list is cut at twelve and says how many are left out,
                // like the refusal of a size that is written wrong.
                var thirteen = Enumerable.Range(1, 13).Select(i => "S" + i).ToArray();
                string many = Call(method, "big library", thirteen, null);
                Check("N25: a long list of sizes is cut at twelve, and says how many more",
                      many != null && many.Contains("has 13 sizes") && many.Contains("S12 and 1 more") &&
                      !many.Contains("S13"),
                      many ?? "(accepted)");
            }
            catch (Exception ex) { Check("N25: EmptyProfileSizeRefusal can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        private static string Call(MethodInfo method, string fileName, string[] sizes, string whyUnread)
        {
            try { return (string)method.Invoke(null, new object[] { fileName, sizes, whyUnread }); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }
    }
}
