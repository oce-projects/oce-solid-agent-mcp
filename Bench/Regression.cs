using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using static Oce.Solid.Agent.Bench.Expectations;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// THE REGRESSION ON THE REFERENCES (entry F8).
    ///
    /// ── THE RESULT IS ONE OF THREE THINGS, NOT TWO ────────────────────────
    ///
    ///   * **HOLDS** — the rebuild goes through, no feature in error, and the
    ///     downstream geometry stays where the intent declares;
    ///   * **COMES OFF AND SAYS SO** — the rebuild fails, or a
    ///     feature carries an error code. **The designer sees it**, and
    ///     a defect that is seen is a problem, not a trap;
    ///   * **COMES OFF IN SILENCE** — rebuild fine, no error,
    ///     and the downstream geometry **ended up somewhere else**.
    ///
    /// The third row is the only number that counts, and it is the one that nobody
    /// measures. The most advanced project in the field compares two whole
    /// documents at the end of the work with a tolerance of **1%**: on a part of 200 mm that is
    /// 2 mm, that is it does not see a hole that moved by a wall
    /// thickness. Here the criterion is **per feature and per position**.
    ///
    /// ── THE INTENT, because without it there is no right answer ───────────
    ///
    /// A hole 20 mm from the edge, on a plate that widens, must stay
    /// 20 mm from the edge or stay where it was? **It depends on what whoever
    /// drew it wanted**, and until it is declared there is nothing to measure.
    ///
    ///   * `ancorato_a_assoluto` — «the hole stays at x = 80 in the model»;
    ///   * `ancorato_a_bordo` — «the hole stays 20 mm from the right edge».
    ///
    /// **Up to library 0.115.0 the system could express only the
    /// first:** `x_mm: 80` is absolute by construction, and the cases
    /// `ancorato_a_bordo` all failed — the starting measurement of E6/E7.
    /// From 0.116.0 the face operations take `anchor` (entry E18:
    /// `cylindrical_cut` and `rectangular_cut`; from 0.118.0 `slot`;
    /// from 0.119.0 `extrude_on_face` and `hole_wizard`): the cases whose
    /// plan uses it can hold. Declaring them expected-green would be cheating;
    /// declaring them expected-red would be making a limit permanent. They are
    /// counted and that is all.
    ///
    /// ── AND THE LINE OF THE SILENT ONES IS WRITTEN EVEN IF IT IS ZERO ─────
    ///
    /// A count that disappears when it is zero is a count that nobody
    /// checks.
    /// </summary>
    internal static partial class Program
    {
        internal enum Outcome { Holds, DetachesAndSaysSo, DetachesInSilence }

        internal sealed class CaseOutcome
        {
            public string Name;
            public string Intent;
            public Outcome Come;
            public string Why;
        }

        internal static void RegressionRun()
        {
            Console.WriteLine();
            Console.WriteLine("=== the regression on references (F8) ===");

            var folder = RegressionFolder();
            if (folder == null) return;
            var file = Directory.GetFiles(folder, "*.json")
                                .OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal).ToList();
            if (file.Count == 0)
            {
                BenchError("regression: there is at least one case", "empty folder: " + folder);
                return;
            }

            if (!LoadCaseKeys()) return;

            // One regression case, one entry of the test ledger, with its
            // outcome in English words (W3.1-bis.2, item 7).
            var outcomes = new List<CaseOutcome>();
            foreach (var f in file)
                using (var scope = Ledger.OpenCase(Path.GetFileNameWithoutExtension(f),
                                                   Path.GetFileName(Path.GetDirectoryName(f)), "regression"))
                {
                    int aliasUsesAtStart = AliasUsesNow();
                    var e = ACase(f);
                    CountAliasUses("regression/" + Path.GetFileNameWithoutExtension(f), f, aliasUsesAtStart);
                    scope.Outcome = scope.HasBenchErrors ? "bench_error"
                                  : e.Come == Outcome.Holds ? "holds"
                                  : e.Come == Outcome.DetachesAndSaysSo ? "breaks_and_says_so"
                                  : "breaks_silently";
                    outcomes.Add(e);
                }

            int holds = outcomes.Count(e => e.Come == Outcome.Holds);
            int noisy = outcomes.Count(e => e.Come == Outcome.DetachesAndSaysSo);
            int silent = outcomes.Count(e => e.Come == Outcome.DetachesInSilence);

            Console.WriteLine();
            foreach (var e in outcomes)
                Console.WriteLine("  " + Symbol(e.Come) + "  " + e.Name.PadRight(34) +
                                  e.Intent.PadRight(22) + Short(e.Why));

            WriteRegression(outcomes, holds, noisy, silent);

            // THE THREE NUMBERS, and they add up to the number of cases. They are written as
            // bench outcomes so that the report takes them from there.
            Check("regression: they hold", true, holds + " su " + outcomes.Count);
            Check("regression: they come loose and say so", true, noisy + " su " + outcomes.Count);
            Check("regression: THEY COME LOOSE IN SILENCE", true, silent + " su " + outcomes.Count);
            Check("regression: the three numbers add up to the cases",
                  holds + noisy + silent == outcomes.Count,
                  holds + " + " + noisy + " + " + silent + " = " + outcomes.Count);
        }

        private static string Symbol(Outcome e)
        {
            switch (e)
            {
                case Outcome.Holds: return "REGGE   ";
                case Outcome.DetachesAndSaysSo: return "LO DICE ";
                default: return "SILENZIO";
            }
        }

        private static string RegressionFolder()
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var bench = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                var c = Path.Combine(bench.FullName, "cases", "regression");
                if (!Directory.Exists(c)) { BenchError("regression: the folder exists", c); return null; }
                return c;
            }
            catch (Exception ex) { BenchError("regression: find the folder", Why(ex)); return null; }
        }

        // ==================================================================
        // A CASE: build, modify, look
        // ==================================================================
        private static CaseOutcome ACase(string filePath)
        {
            string itemName = Path.GetFileNameWithoutExtension(filePath);
            var outside = new CaseOutcome { Name = itemName, Intent = "?", Come = Outcome.DetachesInSilence };

            Dictionary<string, object> testCase;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                testCase = s.DeserializeObject(File.ReadAllText(filePath)) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                BenchError(itemName, "unreadable JSON: " + Why(ex));
                outside.Why = "unreadable JSON"; return outside;
            }

            // An unknown key is a case written wrong (W3.1-bis.2, item 4).
            var unknown = testCase == null ? null : _caseKeys.Unknown(testCase, _caseKeys.RegressionRoot);
            if (unknown != null && unknown.Count > 0)
            {
                BenchError(itemName, CaseKeys.Message(unknown));
                outside.Why = "unknown keys"; return outside;
            }

            outside.Intent = Text(testCase, "intent") ?? "?";
            if (outside.Intent != "ancorato_a_assoluto" && outside.Intent != "ancorato_a_bordo")
            {
                // WITHOUT THE INTENT THERE IS NO RIGHT ANSWER, and a case that does not
                // declare it is not a case: it is a bench error.
                BenchError(itemName, "«intent» must be «ancorato_a_assoluto» or " +
                                    "«ancorato_a_bordo»: without it, there is no right answer");
                outside.Why = "intent not declared"; return outside;
            }
            if (Text(testCase, "intent_why") == null)
            {
                BenchError(itemName, "«intent_why» is missing: an intent without the reason " +
                                    "gets copied from the case next to it");
                outside.Why = "perche_intento missing"; return outside;
            }

            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();

                // ── BUILD ────────────────────────────────────────────────
                var docState = ReadState(doc);
                foreach (var p in Items(testCase, "build") ?? new List<object>())
                {
                    var planStep = p as Dictionary<string, object>;
                    if (planStep == null) { BenchError(itemName, "a step is not an object"); outside.Why = "malformed step"; return outside; }
                    var arguments = Resolve(
                        planStep.ContainsKey("arguments") ? planStep["arguments"] : null, docState)
                        as Dictionary<string, object> ?? new Dictionary<string, object>();
                    string error;
                    var measurements = Run(doc, Text(planStep, "operation"), arguments, out error, docState);
                    docState = ReadState(doc);
                    if (measurements == null)
                    {
                        // THE STARTING PART MUST COME OUT. If it does not, one is not
                        // measuring the modification: one is measuring a badly
                        // written case.
                        BenchError(itemName, "the build failed: " + Short(error));
                        outside.Why = "build failed"; return outside;
                    }
                }

                // ── WHAT MUST NOT CHANGE IS MEASURED BEFORE ──────────────
                //
                // Many invariants of this suite are «as it was»: the pitch of
                // a pattern, the symmetry of a mirror, the radius of
                // four fillets. Writing them as numbers would mean
                // computing by hand what SOLIDWORKS has already decided — and
                // getting that computation wrong would turn a case into a false
                // silence. An expectation with `same_as_before` is compared with the
                // measurement from BEFORE the modification.
                var before = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var a in Items(testCase, "after") ?? new List<object>())
                {
                    var expectation = a as Dictionary<string, object>;
                    if (expectation == null || !True(expectation, "same_as_before")) continue;
                    string which = Text(expectation, "what");
                    if (which == null) continue;
                    var here0 = doc;
                    try { before[which] = Measure(which, c => FromDocument(c, here0, docState), docState, null); }
                    catch (CouldNotMeasure ex)
                    {
                        BenchError(itemName, "«" + which + "» cannot be measured before the change: " + ex.Message);
                        outside.Why = ex.Message; return outside;
                    }
                }

                // ── MODIFY: ONE DIMENSION ONLY, UPSTREAM ─────────────────
                var change = (testCase.ContainsKey("modify") ? testCase["modify"] : null)
                               as Dictionary<string, object>;
                if (change == null)
                {
                    BenchError(itemName, "«modify» is missing"); outside.Why = "modification missing"; return outside;
                }
                var itsItems = Resolve(change, docState) as Dictionary<string, object>;
                string modError;
                var done = Run(doc, "set_dimension", itsItems, out modError, docState);

                // ── THE THREE ROADS ──────────────────────────────────────
                //
                // First one looks whether the part SAYS so: a refused modification,
                // a rebuild that fails, a feature in error.
                bool rebuilt = false;
                try { rebuilt = doc.EditRebuild3(); } catch { }
                docState = ReadState(doc);
                int inError = StateFeaturesInError(docState);

                if (done == null)
                {
                    outside.Come = Outcome.DetachesAndSaysSo;
                    outside.Why = "the modification was refused: " + Short(modError);
                    return outside;
                }
                if (!rebuilt || inError > 0)
                {
                    outside.Come = Outcome.DetachesAndSaysSo;
                    outside.Why = !rebuilt
                        ? "the rebuild did not succeed"
                        : inError + " features in error";
                    return outside;
                }

                // Clean rebuild: now the geometry tells the truth.
                var broken = new List<string>();
                foreach (var a in Items(testCase, "after") ?? new List<object>())
                {
                    var expectation = a as Dictionary<string, object>;
                    if (expectation == null) continue;
                    _expectations++;
                    Ledger.ExpectationAttempted();
                    string detail;
                    bool ok;
                    var here = doc;

                    // `same_as_before`: the expected value is the measurement from before, and it is put
                    // in the expectation now. This way the comparison is made by the same
                    // code as always, and the message comes out in the same
                    // format: «centers_x_mm [...] (atteso [...])».
                    if (True(expectation, "same_as_before"))
                    {
                        object era;
                        if (!before.TryGetValue(Text(expectation, "what") ?? "", out era) || era == null)
                        {
                            BenchError(itemName, "«same_as_before» but the earlier measurement is not there");
                            outside.Why = "come_prima without the earlier measurement"; return outside;
                        }
                        expectation["equal_to"] = era;
                    }

                    try { ok = Matches(expectation, c => FromDocument(c, here, docState), docState, done, out detail); }
                    catch (CouldNotMeasure ex)
                    {
                        BenchError(itemName, ex.Message);
                        outside.Why = ex.Message; return outside;
                    }
                    _verifiedExpectations++;
                    Ledger.ExpectationChecked();
                    if (!ok) broken.Add(detail);
                }

                if (broken.Count == 0)
                {
                    outside.Come = Outcome.Holds;
                    outside.Why = "rebuilt clean, the geometry is where the intent says";
                }
                else
                {
                    // REBUILD FINE, NO ERROR, AND THE GEOMETRY IS
                    // ELSEWHERE. It is the number that counts.
                    outside.Come = Outcome.DetachesInSilence;
                    outside.Why = string.Join(" · ", broken.Take(2));
                }
                return outside;
            }
            catch (Exception ex)
            {
                BenchError(itemName, Why(ex));
                outside.Why = Why(ex);
                return outside;
            }
            finally { Close(doc); }
        }

        private static int StateFeaturesInError(Dictionary<string, object> docState)
        {
            object v;
            if (docState == null || !docState.TryGetValue("features_in_error", out v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        // ==================================================================
        // THE REPORT
        // ==================================================================
        private static void WriteRegression(
            List<CaseOutcome> outcomes, int holds, int noisy, int silent)
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var bench = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                var folder = Path.Combine(bench.FullName, "report");
                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, "regressione.md");

                var t = new List<string>
                {
                    "# The regression on references (F8)",
                    "",
                    "Each case builds a part, changes **a single upstream dimension**, and",
                    "looks at where the downstream geometry ended up. The result is one of THREE",
                    "things, not two.",
                    "",
                    "| | cases out of " + outcomes.Count + " | |",
                    "|---|---|---|",
                    "| **hold** | " + holds + " | clean rebuild, geometry where the intent says |",
                    "| **come loose and say so** | " + noisy + " | the designer sees it |",
                    "| **COME LOOSE IN SILENCE** | " + silent + " | rebuild fine, no error, and the geometry is elsewhere |",
                    "",
                    "> The third row is written **even when it is zero**: a count that",
                    "> disappears when it is zero is a count that nobody checks.",
                    "",
                    "Without `anchor`, `x_mm: 80` is absolute by construction. With `anchor`",
                    "(entry E18: holes, pockets, slots, bosses and hole-wizard holes) the profile follows",
                    "the edge. An `ancorato_a_bordo` case that stays silent is an intent",
                    "that the system cannot yet express: the measure of what is missing.",
                    "",
                    "| case | intent | outcome | |",
                    "|---|---|---|---|",
                };
                foreach (var e in outcomes)
                    t.Add("| `" + e.Name + "` | " + e.Intent + " | " +
                          (e.Come == Outcome.Holds ? "regge"
                           : e.Come == Outcome.DetachesAndSaysSo ? "says so" : "**silence**") +
                          " | " + (e.Why ?? "").Replace("|", "/") + " |");

                t.Add("");
                t.Add("By intent:");
                t.Add("");
                t.Add("| intent | cases | hold |");
                t.Add("|---|---|---|");
                foreach (var g in outcomes.GroupBy(e => e.Intent))
                    t.Add("| " + g.Key + " | " + g.Count() + " | " +
                          g.Count(e => e.Come == Outcome.Holds) + " |");

                File.WriteAllText(file, string.Join("\r\n", t) + "\r\n",
                                  new System.Text.UTF8Encoding(false));
                Console.WriteLine("regressione: " + file);
            }
            catch (Exception ex)
            {
                Console.WriteLine("could not write regressione.md: " + ex.Message);
            }
        }
    }
}
