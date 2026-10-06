// =============================================================================
//  The checks that do not open SOLIDWORKS  (entries E10 and F12)
// =============================================================================
//
//  WHY THEY ARE IN A FILE OF THEIR OWN. `NewOperationsTests.cs` is the file of the checks on the
//  PART: it opens a document, builds, measures, closes. These do not. They are
//  pure functions on strings - `Sentences.WhereItWasAndWhereItIs` - and a sieve that
//  reads the repo's sources. They take a thousandth of a second and need
//  no license.
//
//  Keeping them among the others made them look like drawing checks, and made
//  `NewOperationsTests.cs` look fuller of expected numbers than it is: entry
//  F11 counts exactly that.
//
//  THEY ARE NOT IN `Oce.Solid.Agent.ContractTests` for one reason only: they ask the REAL
//  LIBRARY, loaded by reflection, whether that method is there and what it answers.
//  `Oce.Solid.Agent.ContractTests` does not load the library - it is the project that runs where
//  SOLIDWORKS is not, and the library has the interop inside it.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
// A SINGLE `IModelDoc2` IN THE WHOLE FILE, and none is created: the
// null check (F12) passes `null` to `OrSayIt<IModelDoc2>` to look at
// what the bench says when the part is not there. The type is needed, not the CAD.
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Bench
{
    internal static partial class Program
    {
        // ==================================================================
        // F12. when the bench cannot prepare the part, it SAYS so
        // ==================================================================
        /// <summary>
        /// The null has a name.
        ///
        /// On 15/9/2026 a check came out red with «Object reference not set
        /// to an instance of an object.» and nothing else: in eighteen places
        /// of the bench `NewPart()` was used without looking at whether it had
        /// produced anything, and a `NullReferenceException` does not say which of the
        /// eighteen. Now `NewPart()` does not return null: it raises an exception
        /// that names the step.
        ///
        /// This check looks that the name is really there, and that `Why()` —
        /// the function the bench uses to tell an exception — carries it
        /// all the way to the outcome line. Without it, «the message names the step»
        /// would remain an intention written in a comment.
        /// </summary>
        private static void TestThatNullHasAName()
        {
            Console.WriteLine();
            Console.WriteLine("=== when the bench cannot prepare the part (F12) ===");

            // ── THE RED: the null raises, and the message names the step ──
            string said = null;
            string told = null;
            try
            {
                OrSayIt<IModelDoc2>(null, "the new part to try on");
            }
            catch (Exception ex)
            {
                said = ex.Message;
                told = Why(ex);
            }
            Check("F12 RED: a null raises, and says WHICH step",
                  said != null && said.Contains("the new part to try on"),
                  Short(said));

            // `Why` truncates at 110 characters: one looks at the beginning, which is where
            // the name of the step is, and the type of the exception.
            // THE TYPE HAS CHANGED, AND THE CHANGE IS THE POINT (entry K17):
            // `PreparationFailed` is what lets the run of the cases
            // not call red a check that did not take place. It remains an
            // `InvalidOperationException`, so whoever catches it that way
            // keeps catching it.
            Check("F12: the exception's account carries type and step",
                  told != null &&
                  told.StartsWith("PreparationFailed:", StringComparison.Ordinal) &&
                  told.Contains("the new part"),
                  Short(told));

            // ── THE GREEN: what is there passes, and the same object comes back ──
            var something = new object();
            Check("F12: what is there passes, and comes back as it was",
                  ReferenceEquals(OrSayIt(something, "any object"), something),
                  "");
        }

        // ==================================================================
        // E10. the fraction of the bounding box: as an EXPLANATION, never as identity
        // ==================================================================
        /// <summary>
        /// The sentence that says where a face was with respect to the part.
        ///
        /// Pure function on four triples of numbers: it does not need SOLIDWORKS, does not
        /// need a document, and runs in a thousandth of a second. It is in the bench
        /// all the same because the bench is the place where one looks whether something
        /// broke, and a function that does not need the CAD is exactly
        /// the one that nobody will remember to re-test by hand.
        /// </summary>
        private static void TestWhereItWas()
        {
            Console.WriteLine();
            Console.WriteLine("=== where it was, as a fraction of the bounding box (E10) ===");

            var typ = _addin.GetType("Oce.Solid.Agent.Core.Sentences", false);
            if (typ == null) { Check("E10: the class exists", false, "Sentences missing"); return; }
            var method = typ.GetMethod("WhereItWasAndWhereItIs");
            if (method == null)
            {
                Check("E10: the method exists", false, "WhereItWasAndWhereItIs missing");
                return;
            }

            Func<double[], double[], double[], double[], string> di =
                (before, now, sBefore, sNow) =>
                    Convert.ToString(method.Invoke(
                        null, new object[] { "F6", before, now, sBefore, sNow }));

            // ── CASE A OF ENTRY E2, with the numbers measured on 14/9 ──────
            // Block 100 x 60 x 20 centered: the box goes from -50 to +50 in X.
            // The dimension goes from 100 to 160 and the part grows ON ONE SIDE ONLY:
            // -50 .. +110. The right face goes from 50 to 110 — sixty
            // millimeters — and sits at 100% of the side both times.
            string testCase = di(
                new[] { 50.0, 0.0, 0.0 }, new[] { 110.0, 0.0, 0.0 },
                new[] { -50.0, -30.0, 0.0, 50.0, 30.0, 20.0 },
                new[] { -50.0, -30.0, 0.0, 110.0, 30.0, 20.0 });
            Check("E10: at 100% of side X, and it is still there",
                  testCase != null && testCase.Contains("100%") && testCase.Contains("is still there") &&
                  testCase.Contains("from 100 to 160"),
                  Short(testCase));

            // ── THE RED: a face that moved WITH RESPECT TO the part ──
            //
            // Without this, «the sentence comes out» would pass even if it always said
            // «it still fits» — that is even if the comparison between the two fractions
            // were not there. It is the case that counts: a hole 20 mm from the edge on
            // a plate that goes from 100 to 300 STAYS 20 mm from the edge, and the
            // fraction says it went to 60. Saying it is useful; deciding it
            // would be the defect that entry E10 exists in order not to introduce.
            string moved = di(
                new[] { 20.0, 0.0, 0.0 }, new[] { 20.0, 0.0, 0.0 },
                new[] { 0.0, 0.0, 0.0, 100.0, 60.0, 20.0 },
                new[] { 0.0, 0.0, 0.0, 300.0, 60.0, 20.0 });
            Check("E10 RED: the hole standing still at 20 mm did not move, and it keeps quiet",
                  string.IsNullOrEmpty(moved),
                  moved == null ? "(nothing, correct)" : Short(moved));

            // The same part that grows, but the face that moves by HALF
            // instead of staying where it was in proportion: the sentence must say
            // two different percentages and must NOT say «it still fits».
            string different = di(
                new[] { 100.0, 0.0, 0.0 }, new[] { 150.0, 0.0, 0.0 },
                new[] { 0.0, 0.0, 0.0, 100.0, 60.0, 20.0 },
                new[] { 0.0, 0.0, 0.0, 300.0, 60.0, 20.0 });
            Check("E10 RED: different fractions, «is still there» is not said",
                  different != null && different.Contains("100%") && different.Contains("50%") &&
                  !different.Contains("is still there"),
                  Short(different));

            // Without the box nothing is invented.
            Check("E10: without the box, no sentence",
                  string.IsNullOrEmpty(di(
                      new[] { 50.0, 0.0, 0.0 }, new[] { 110.0, 0.0, 0.0 }, null, null)),
                  "");
        }

        /// <summary>
        /// THE REAL CRITERION OF ENTRY E10: a search for «fraction» or
        /// «proportional» inside `SignatureMatches` or inside the
        /// re-finding loop must return **empty**.
        ///
        /// ── WHY A CHECK THAT READS THE SOURCE ────────────────────────────
        ///
        /// Because what must be defended is not a behavior: it is the ABSENCE
        /// of a behavior. The fraction of the bounding box is simple, the
        /// number is already there now, and promoting it from explanation to criterion
        /// costs ten minutes to anyone who passes by two months from now — whoever
        /// wrote it included. No check on a part can notice until the two
        /// criteria give the same answer, and when they give different answers
        /// it will be because a part has already come out wrong.
        ///
        /// ── AND ITS RED ──────────────────────────────────────────────────
        ///
        /// A check that looks for a word and never finds it is identical to
        /// a broken check that looks for nothing. The same sieve is passed
        /// over `Sentences.cs`, where those words really are there: if it finds
        /// nothing there, the sieve is broken and says so.
        /// </summary>
        private static void TestThatTheFractionDoesNotDecide()
        {
            Console.WriteLine();
            Console.WriteLine("=== the fraction does not decide: the grep that must come back empty (E10) ===");

            string root;
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var bench = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                root = Directory.GetParent(bench.FullName).FullName;
            }
            catch (Exception ex)
            {
                Check("E10: find the repo root", false, Why(ex));
                return;
            }

            string operations = Path.Combine(root, "Oce.Solid.Agent.Core", "SwOperations.cs");
            string sentences = Path.Combine(root, "Oce.Solid.Agent.Core", "Sentences.cs");
            if (!File.Exists(operations) || !File.Exists(sentences))
            {
                Skipped("E10: the grep that must come back empty",
                        "the sources are not next to the executable");
                return;
            }

            // DOES THE SIEVE WORK? In `Sentences.cs` those words are there.
            string sentencesText = File.ReadAllText(sentences);
            Check("E10 RED: the sieve finds the words where they are",
                  Pick(sentencesText, 0, sentencesText.Length),
                  "Sentences.cs");

            string text = File.ReadAllText(operations);

            int a = text.IndexOf("private static bool SignatureMatches(", StringComparison.Ordinal);
            Check("E10: SignatureMatches does not talk about fractions",
                  a >= 0 && !Pick(text, a, MethodEnd(text, a)),
                  a < 0 ? "SignatureMatches not found" : "");

            int b = text.IndexOf("IFace2 foundAgain = null;", StringComparison.Ordinal);
            Check("E10: the finding loop does not talk about fractions",
                  b >= 0 && !Pick(text, b, MethodEnd(text, b)),
                  b < 0 ? "the loop not found" : "");
        }

        /// <summary>The forbidden words, in a piece of source.</summary>
        private static bool Pick(string text, int da, int a)
        {
            if (da < 0 || a <= da || a > text.Length) return false;
            string part = text.Substring(da, a - da);
            return part.IndexOf("fraction", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   part.IndexOf("proportional", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Where the method that begins at `da` ends: the first open brace,
        /// and then one counts until it returns to zero. Crude — a brace inside
        /// a string would confuse it — and sufficient: here it serves to delimit
        /// a piece of text on which to look for two words, not to compile.
        /// </summary>
        private static int MethodEnd(string text, int da)
        {
            int i = text.IndexOf('{', da);
            if (i < 0) return text.Length;
            int open = 0;
            for (; i < text.Length; i++)
            {
                if (text[i] == '{') open++;
                else if (text[i] == '}' && --open == 0) return i + 1;
            }
            return text.Length;
        }
    }
}
