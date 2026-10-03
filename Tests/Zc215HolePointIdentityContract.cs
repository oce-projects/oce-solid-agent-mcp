using System;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Programma;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// N23 — A HOLE'S ANCHOR POINT IS NEVER THE LAST ONE FOUND BY GUESS
    /// (Z-C21.5).
    ///
    /// `AncoraIFori` (AnchorOperations.cs) walks a positioning sketch's points
    /// and keeps every one within 1e-7 m of the hole's expected position — a
    /// window loose on purpose, since `NelloSchizzo`'s own transform carries
    /// some numerical slack. Up to library 0.146.0 a second (or third) point
    /// inside that window silently REPLACED the first: whichever the loop over
    /// `GetSketchPoints2()` visited last won, with nothing said. The fix,
    /// `OperazioniSw.SameHolePoint`, refuses instead of guessing UNLESS every
    /// candidate is provably the same point: the same sketch point ID
    /// (ISketchPoint~GetID.html, Remarks: «Each point within a specific sketch
    /// has a unique ID»). Two points at the very same coordinates with two IDs
    /// are two points, and are refused; so is a candidate whose ID was not read.
    ///
    /// NO BENCH CASE CAN BUILD THE AMBIGUOUS CASE ON PURPOSE: it needs two
    /// sketch points inside the same 1e-7 m window of one positioning sketch,
    /// which no JSON case can ask SOLIDWORKS to stage. `SameHolePoint` takes
    /// plain numbers, no interop, exactly so this group can drive it directly,
    /// as Zc215IdentityContract does for the other identity rules of this lot.
    /// </summary>
    internal static class Zc215HolePointIdentityContract
    {
        internal static void RunAll()
        {
            Gruppo("N23: a hole's anchor point is proved, never picked last by guess");

            var library = Path.Combine(Radice(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
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
                owner = asm.GetType("Oce.Solid.Agent.Core.OperazioniSw", false);
            }
            catch (Exception ex) { Segna("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (owner == null) { Segna("Oce.Solid.Agent.Core.OperazioniSw is in the library", false, ""); return; }

            var method = owner.GetMethod("SameHolePoint",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) { Segna("OperazioniSw.SameHolePoint is there", false, ""); return; }

            Func<double[][], long[][], object[]> call = (candidates, ids) =>
            {
                var args = new object[] { candidates, ids, null };
                bool same = (bool)method.Invoke(null, args);
                return new object[] { same, args[2] as string };
            };

            var noCandidates = call(new double[0][], new long[0][]);
            Segna("no candidate: false, and nothing to say (the caller's own refusal covers it)",
                  (bool)noCandidates[0] == false && noCandidates[1] == null,
                  Show(noCandidates));

            var one = call(new[] { new[] { 0.05, 0.03 } }, new[] { new long[] { 1, 7 } });
            Segna("one candidate: it is the point, no ambiguity",
                  (bool)one[0] && one[1] == null, Show(one));

            // THE SAME POINT MET TWICE: one ID, so one point.
            var sameId = call(new[] { new[] { 0.05, 0.03 }, new[] { 0.05 + 4e-10, 0.03 } },
                              new[] { new long[] { 1, 7 }, new long[] { 1, 7 } });
            Segna("two candidates with the same ID: the same point",
                  (bool)sameId[0] && sameId[1] == null, Show(sameId));

            // THE SAME PLACE IS NOT THE SAME POINT: bit-equal coordinates, two
            // IDs. The first draft of this lot (1e-9 m apart = the same point)
            // said yes here, and anchored one of two coincident holes by guess.
            var twinPlace = call(new[] { new[] { 0.05, 0.03 }, new[] { 0.05, 0.03 } },
                                 new[] { new long[] { 1, 7 }, new long[] { 1, 8 } });
            Segna("two candidates at the exact same coordinates with two IDs: refused, two points",
                  (bool)twinPlace[0] == false && (string)twinPlace[1] != null &&
                  ((string)twinPlace[1]).Contains("IDs differ"),
                  Show(twinPlace));

            // INSIDE THE COARSE 1e-7 m SEARCH WINDOW, TWO POINTS: the case
            // 0.146.0 got wrong by silently keeping whichever came last.
            var ambiguousPair = call(new[] { new[] { 0.05, 0.03 }, new[] { 0.05 + 5e-8, 0.03 } },
                                     new[] { new long[] { 1, 7 }, new long[] { 1, 9 } });
            Segna("two candidates 5e-8 m apart with two IDs: refused, naming both",
                  (bool)ambiguousPair[0] == false && (string)ambiguousPair[1] != null &&
                  ((string)ambiguousPair[1]).StartsWith("2 ") &&
                  ((string)ambiguousPair[1]).Contains("ID 1,7") && ((string)ambiguousPair[1]).Contains("ID 1,9"),
                  Show(ambiguousPair));

            // AN ID THAT WAS NOT READ PROVES NOTHING, even at the same place.
            var unread = call(new[] { new[] { 0.05, 0.03 }, new[] { 0.05, 0.03 } },
                              new[] { new long[] { 1, 7 }, null });
            Segna("two candidates, one ID not read: refused, and the refusal says so",
                  (bool)unread[0] == false && (string)unread[1] != null &&
                  ((string)unread[1]).Contains("could not be read") && ((string)unread[1]).Contains("unread"),
                  Show(unread));

            // THE ODD ONE OUT IS CAUGHT EVEN WHEN THE OTHER TWO AGREE.
            var oneOfThreeDiffers = call(new[]
            {
                new[] { 0.05, 0.03 },
                new[] { 0.05, 0.03 },
                new[] { 0.05 + 5e-8, 0.03 },
            }, new[] { new long[] { 1, 7 }, new long[] { 1, 7 }, new long[] { 2, 7 } });
            Segna("three candidates, two with one ID and one with another: refused, naming all three",
                  (bool)oneOfThreeDiffers[0] == false && (string)oneOfThreeDiffers[1] != null &&
                  ((string)oneOfThreeDiffers[1]).StartsWith("3 ") &&
                  ((string)oneOfThreeDiffers[1]).Contains("ID 2,7"),
                  Show(oneOfThreeDiffers));

            // MILLIMETRES IN THE MESSAGE, NOT METRES: what a person reads.
            Segna("the refusal says the coordinates in millimetres",
                  (string)ambiguousPair[1] != null && ((string)ambiguousPair[1]).Contains("(mm)") &&
                  ((string)ambiguousPair[1]).Contains("(50.00005, 30)"),
                  (string)ambiguousPair[1]);
        }

        private static string Show(object[] result)
        {
            return "same=" + result[0] + ", ambiguity=" + (result[1] == null ? "null" : "\"" + result[1] + "\"");
        }
    }
}
