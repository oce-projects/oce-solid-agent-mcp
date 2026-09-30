using System;
using System.IO;
using System.Reflection;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// N23 — A HOLE'S ANCHOR POINT IS NEVER THE LAST ONE FOUND BY GUESS
    /// (Z-C21.5).
    ///
    /// `AncoraIFori` (OperazioniAncora.cs) walks a positioning sketch's points
    /// and keeps every one within 1e-7 m of the hole's expected position — a
    /// window loose on purpose, since `NelloSchizzo`'s own transform carries
    /// some numerical slack. Up to library 0.146.0 a second (or third) point
    /// inside that window silently REPLACED the first: whichever the loop over
    /// `GetSketchPoints2()` visited last won, with nothing said. The fix,
    /// `OperazioniSw.SameHolePoint`, refuses instead of guessing UNLESS every
    /// candidate is provably the same point — within this file's own point-
    /// identity tolerance (1e-9 m, a hundred times tighter, already used
    /// elsewhere in the same file to match a sketch point to a computed one).
    ///
    /// NO BENCH CASE CAN BUILD THE AMBIGUOUS CASE ON PURPOSE: it needs two real
    /// sketch points 1e-7 m apart inside the same positioning sketch, which is
    /// not a shape a JSON bench case can ask SOLIDWORKS to build (and even if
    /// it could, floating-point placement that exact is not something the bench
    /// controls). `SameHolePoint` takes plain coordinates, no interop, exactly
    /// so this group can drive it directly, as Zc215IdentityContract does for
    /// the other identity rules of this lot.
    /// </summary>
    internal static class Zc215HolePointIdentityContract
    {
        internal static void RunAll()
        {
            Gruppo("N23: a hole's anchor point is proved, never picked last by guess");

            var library = Path.Combine(Radice(), "Riscontro.Disegno", "bin", "Release", "Riscontro.Disegno.dll");
            if (!File.Exists(library))
            {
                NonQui("OperazioniSw.SameHolePoint", library + " is missing: build the library first (build.ps1)");
                return;
            }

            Type owner;
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
                // From the bytes, as Zc215IdentityContract: the repository may
                // sit on a network drive, where LoadFrom is refused.
                var asm = Assembly.Load(File.ReadAllBytes(library));
                owner = asm.GetType("Riscontro.Disegno.OperazioniSw", false);
            }
            catch (Exception ex) { Segna("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (owner == null) { Segna("Riscontro.Disegno.OperazioniSw is in the library", false, ""); return; }

            var method = owner.GetMethod("SameHolePoint",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) { Segna("OperazioniSw.SameHolePoint is there", false, ""); return; }

            Func<double[][], object[]> call = candidates =>
            {
                var args = new object[] { candidates, null };
                bool same = (bool)method.Invoke(null, args);
                return new object[] { same, args[1] as string };
            };

            var noCandidates = call(new double[0][]);
            Segna("no candidate: false, and nothing to say (the caller's own refusal covers it)",
                  (bool)noCandidates[0] == false && noCandidates[1] == null,
                  Show(noCandidates));

            var one = call(new[] { new[] { 0.05, 0.03 } });
            Segna("one candidate: it is the point, no ambiguity",
                  (bool)one[0] && one[1] == null, Show(one));

            var exactTwin = call(new[] { new[] { 0.05, 0.03 }, new[] { 0.05, 0.03 } });
            Segna("two candidates at the exact same coordinates: the same point",
                  (bool)exactTwin[0] && exactTwin[1] == null, Show(exactTwin));

            // WITHIN THE POINT-IDENTITY TOLERANCE (1e-9 m) BUT NOT BIT-EQUAL:
            // still proved the same point. A coincident relation can leave two
            // entities a hair apart in floating point without them being two
            // different positions.
            var withinTolerance = call(new[] { new[] { 0.05, 0.03 }, new[] { 0.05 + 4e-10, 0.03 - 4e-10 } });
            Segna("two candidates 4e-10 m apart (inside the point-identity tolerance): still the same point",
                  (bool)withinTolerance[0] && withinTolerance[1] == null, Show(withinTolerance));

            // INSIDE THE COARSE 1e-7 m SEARCH WINDOW, BUT NOT THE SAME POINT:
            // the case the bench cannot build, and the one 0.146.0 got wrong by
            // silently keeping whichever the loop visited last.
            var ambiguousPair = call(new[] { new[] { 0.05, 0.03 }, new[] { 0.05 + 5e-8, 0.03 } });
            Segna("two candidates 5e-8 m apart (inside the 1e-7 search window, not the point-identity one): refused",
                  (bool)ambiguousPair[0] == false && (string)ambiguousPair[1] != null &&
                  ((string)ambiguousPair[1]).Contains("2"),
                  Show(ambiguousPair));

            // THE ODD ONE OUT IS CAUGHT EVEN WHEN THE OTHER TWO AGREE.
            var oneOfThreeDiffers = call(new[]
            {
                new[] { 0.05, 0.03 },
                new[] { 0.05 + 5e-8, 0.03 },
                new[] { 0.05 + 5e-8, 0.03 },
            });
            Segna("three candidates, two of them agreeing and one not: still refused, naming all three",
                  (bool)oneOfThreeDiffers[0] == false && (string)oneOfThreeDiffers[1] != null &&
                  ((string)oneOfThreeDiffers[1]).Contains("3"),
                  Show(oneOfThreeDiffers));

            // MILLIMETRES IN THE MESSAGE, NOT METRES: what a person reads.
            Segna("the refusal says the coordinates in millimetres",
                  ((string)ambiguousPair[1]).Contains("(mm)"), (string)ambiguousPair[1]);
        }

        private static string Show(object[] result)
        {
            return "same=" + result[0] + ", ambiguity=" + (result[1] == null ? "null" : "\"" + result[1] + "\"");
        }
    }
}
