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
        /// On 15/9/2026 a check came out red with «Riferimento a un oggetto
        /// non impostato su un'istanza di oggetto.» and nothing else: in eighteen places
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
            Console.WriteLine("=== quando il banco non riesce a preparare il pezzo (F12) ===");

            // ── THE RED: the null raises, and the message names the step ──
            string said = null;
            string told = null;
            try
            {
                OrSayIt<IModelDoc2>(null, "la parte nuova su cui provare");
            }
            catch (Exception ex)
            {
                said = ex.Message;
                told = Why(ex);
            }
            Check("F12 ROSSO: un null alza, e dice QUALE passo",
                  said != null && said.Contains("la parte nuova su cui provare"),
                  Short(said));

            // `Why` truncates at 110 characters: one looks at the beginning, which is where
            // the name of the step is, and the type of the exception.
            // THE TYPE HAS CHANGED, AND THE CHANGE IS THE POINT (entry K17):
            // `PreparationFailed` is what lets the run of the cases
            // not call red a check that did not take place. It remains an
            // `InvalidOperationException`, so whoever catches it that way
            // keeps catching it.
            Check("F12: il racconto dell'eccezione porta tipo e passo",
                  told != null &&
                  told.StartsWith("PreparationFailed:", StringComparison.Ordinal) &&
                  told.Contains("la parte nuova"),
                  Short(told));

            // ── THE GREEN: what is there passes, and the same object comes back ──
            var something = new object();
            Check("F12: quello che c'e' passa, e torna com'era",
                  ReferenceEquals(OrSayIt(something, "un oggetto qualunque"), something),
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
            Console.WriteLine("=== dove stava, in frazione dell'ingombro (E10) ===");

            var typ = _addin.GetType("Oce.Solid.Agent.Core.Sentences", false);
            if (typ == null) { Check("E10: la classe c'e'", false, "Sentences assente"); return; }
            var method = typ.GetMethod("WhereItWasAndWhereItIs");
            if (method == null)
            {
                Check("E10: il metodo c'e'", false, "WhereItWasAndWhereItIs assente");
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
            Check("E10: al 100% del lato X, e ci sta ancora",
                  testCase != null && testCase.Contains("100%") && testCase.Contains("ci sta ancora") &&
                  testCase.Contains("da 100 a 160"),
                  Short(testCase));

            // ── THE RED: a face that moved WITH RESPECT TO the part ──
            //
            // Without this, «the sentence comes out» would pass even if it always said
            // «ci sta ancora» — that is even if the comparison between the two fractions
            // were not there. It is the case that counts: a hole 20 mm from the edge on
            // a plate that goes from 100 to 300 STAYS 20 mm from the edge, and the
            // fraction says it went to 60. Saying it is useful; deciding it
            // would be the defect that entry E10 exists in order not to introduce.
            string moved = di(
                new[] { 20.0, 0.0, 0.0 }, new[] { 20.0, 0.0, 0.0 },
                new[] { 0.0, 0.0, 0.0, 100.0, 60.0, 20.0 },
                new[] { 0.0, 0.0, 0.0, 300.0, 60.0, 20.0 });
            Check("E10 ROSSO: il foro fermo a 20 mm non si e' mosso, e si tace",
                  string.IsNullOrEmpty(moved),
                  moved == null ? "(niente, giusto)" : Short(moved));

            // The same part that grows, but the face that moves by HALF
            // instead of staying where it was in proportion: the sentence must say
            // two different percentages and must NOT say «ci sta ancora».
            string different = di(
                new[] { 100.0, 0.0, 0.0 }, new[] { 150.0, 0.0, 0.0 },
                new[] { 0.0, 0.0, 0.0, 100.0, 60.0, 20.0 },
                new[] { 0.0, 0.0, 0.0, 300.0, 60.0, 20.0 });
            Check("E10 ROSSO: frazioni diverse, «ci sta ancora» non si dice",
                  different != null && different.Contains("100%") && different.Contains("50%") &&
                  !different.Contains("ci sta ancora"),
                  Short(different));

            // Without the box nothing is invented.
            Check("E10: senza la scatola, nessuna frase",
                  string.IsNullOrEmpty(di(
                      new[] { 50.0, 0.0, 0.0 }, new[] { 110.0, 0.0, 0.0 }, null, null)),
                  "");
        }

        /// <summary>
        /// THE REAL CRITERION OF ENTRY E10: a search for «frazione» or
        /// «proporzional» inside `SignatureMatches` or inside the
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
            Console.WriteLine("=== la frazione non decide: il grep che deve tornare vuoto (E10) ===");

            string root;
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var bench = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                root = Directory.GetParent(bench.FullName).FullName;
            }
            catch (Exception ex)
            {
                Check("E10: trovo la radice del repo", false, Why(ex));
                return;
            }

            string operations = Path.Combine(root, "Oce.Solid.Agent.Core", "SwOperations.cs");
            string sentences = Path.Combine(root, "Oce.Solid.Agent.Core", "Sentences.cs");
            if (!File.Exists(operations) || !File.Exists(sentences))
            {
                Skipped("E10: il grep che deve tornare vuoto",
                        "i sorgenti non sono accanto all'eseguibile");
                return;
            }

            // DOES THE SIEVE WORK? In `Sentences.cs` those words are there.
            string sentencesText = File.ReadAllText(sentences);
            Check("E10 ROSSO: il setaccio trova le parole dove ci sono",
                  Pick(sentencesText, 0, sentencesText.Length),
                  "Sentences.cs");

            string text = File.ReadAllText(operations);

            int a = text.IndexOf("private static bool SignatureMatches(", StringComparison.Ordinal);
            Check("E10: SignatureMatches non parla di frazioni",
                  a >= 0 && !Pick(text, a, MethodEnd(text, a)),
                  a < 0 ? "SignatureMatches non trovata" : "");

            int b = text.IndexOf("IFace2 foundAgain = null;", StringComparison.Ordinal);
            Check("E10: il ciclo di ritrovamento non parla di frazioni",
                  b >= 0 && !Pick(text, b, MethodEnd(text, b)),
                  b < 0 ? "il ciclo non trovato" : "");
        }

        /// <summary>The forbidden words, in a piece of source.</summary>
        private static bool Pick(string text, int da, int a)
        {
            if (da < 0 || a <= da || a > text.Length) return false;
            string part = text.Substring(da, a - da);
            return part.IndexOf("frazione", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   part.IndexOf("proporzional", StringComparison.OrdinalIgnoreCase) >= 0;
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
