using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// THE EXPECTATION EVALUATOR AND THE SELECTORS, WITHOUT SOLIDWORKS (entry F7).
    ///
    /// ── WHY IT IS IN A CLASS OF ITS OWN ──────────────────────────────────
    ///
    /// This file is compiled by TWO projects: the bench, which opens the CAD, and
    /// `Oce.Solid.Agent.ContractTests`, which runs on any machine in a few seconds and
    /// **does not reference the SOLIDWORKS interop**. As long as the evaluator was
    /// in a partial class that touches `IModelDoc2`, the second project could
    /// not even compile.
    ///
    /// And it must be able to run there: **if the evaluator is broken, the whole suite with the
    /// CAD turns green for free.** It is the last place where one can afford
    /// not to test.
    ///
    /// The measurements that come from the DOCUMENT — volume, bodies, bounding box — arrive
    /// from a `Func&lt;string, object&gt;`: whoever has the document in hand passes it, whoever
    /// does not have it passes null and tests everything else.
    /// </summary>
    internal static class Expectations
    {
        /// <summary>
        /// What a check could not measure: a missing field, a
        /// selector that does not resolve, a badly written case (entry F4).
        /// </summary>
        internal sealed class CouldNotMeasure : Exception
        {
            public CouldNotMeasure(string message) : base(message) { }
        }

        /// <summary>
        /// Where the library of the welded profiles is. It is stated with
        /// `OCE_WELDMENT_PROFILES`, as in the bench and as in the library.
        ///
        /// HERE THERE USED TO BE THE PATH OF ONE WORKSTATION ONLY, as a fallback. It never
        /// helped anybody else - on every other machine that folder does not
        /// exist - and meanwhile it told how that disk is laid out. Now
        /// empty is empty, and whoever calls must say so.
        /// </summary>
        internal static string Profiles
        {
            get
            {
                var its = Environment.GetEnvironmentVariable("OCE_WELDMENT_PROFILES");
                return string.IsNullOrWhiteSpace(its) ? null : its;
            }
        }

        /// <summary>
        /// WHERE A CASE CAN WRITE. `Bench\work\`, the same folder
        /// into which `CloseLeftovers` throws the leftover documents: it is ignored by git and
        /// can be emptied at any moment.
        ///
        /// It has been needed since there are operations that produce FILES —
        /// `export_stl`, `export_pdf`, `save_document`. A case that writes
        /// in a folder of its own would fill it with stuff that nobody cleans; a
        /// case that writes next to the repository would dirty `git status`.
        /// The folder is created if it is not there: a case must not fail because
        /// somebody emptied the recycle bin.
        /// </summary>
        internal static string Work
        {
            get
            {
                // A LOCAL folder, not `Bench\work` next to the repository: the
                // main checkout sits on a network drive, and on 3/10/2026 a
                // SaveAs3 there answered e=1 and left no file, a bench error
                // that cost a whole run. OCE_BENCH_WORK names another folder.
                var its = Environment.GetEnvironmentVariable("OCE_BENCH_WORK");
                var location = Path.GetFullPath(!string.IsNullOrWhiteSpace(its)
                    ? its
                    : Path.Combine(Path.GetTempPath(), "oce-bench-work"));
                try { if (!Directory.Exists(location)) Directory.CreateDirectory(location); } catch { }
                return location;
            }
        }

        /// <summary>
        /// THE SOLIDWORKS INSTALLATION FOLDER, for the stock files that
        /// a case must be able to name without copying them into the repository (entry
        /// Z-C21.2, revision: the cut list template «cut
        /// list.sldwldtbt», which belongs to Dassault and is not versioned). It is stated with
        /// `SW_INSTALL_PATH`, the same as in build.ps1; without it, it is looked for where the default
        /// installation puts it, recognizing it from SLDWORKS.exe. If it is not found, the case
        /// cannot be measured — it is not red.
        /// </summary>
        internal static string SolidWorksFolder
        {
            get
            {
                var its = Environment.GetEnvironmentVariable("SW_INSTALL_PATH");
                if (!string.IsNullOrWhiteSpace(its)) return its;
                foreach (var pf in new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetEnvironmentVariable("ProgramW6432"),
                })
                {
                    if (string.IsNullOrWhiteSpace(pf)) continue;
                    var d = Path.Combine(pf, "SOLIDWORKS Corp", "SOLIDWORKS");
                    try { if (File.Exists(Path.Combine(d, "SLDWORKS.exe"))) return d; } catch { }
                }
                return null;
            }
        }

        /// <summary>
        /// THE FILES THAT A CASE READS AND DOES NOT WRITE: `Bench\data\`, versioned (entry
        /// Z-F5, 27/9/2026: the image to trace). `{WORK}` is emptied at every
        /// run and is not in the repository, so an input file cannot live there.
        /// </summary>
        internal static string Data
        {
            get
            {
                var here = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetEntryAssembly() != null
                        ? System.Reflection.Assembly.GetEntryAssembly().Location
                        : System.Reflection.Assembly.GetExecutingAssembly().Location);
                return Path.GetFullPath(Path.Combine(here ?? ".", "..", "data"));
            }
        }

        /// <summary>A long text, shortened to fit on one outcome line.</summary>
        internal static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(no reason)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 110 ? s.Substring(0, 110) + "..." : s;
        }

        // ==================================================================
        // THE EXPECTATIONS
        // ==================================================================
        internal static bool Matches(
            Dictionary<string, object> expectation, Func<string, object> fromDocument,
            Dictionary<string, object> docState, Dictionary<string, object> measurements,
            out string detail)
        {
            detail = "";
            string what = Text(expectation, "what");
            if (what == null) throw new CouldNotMeasure("an expectation without «what»");

            // ── WHEN THE MEASUREMENT IS A SENTENCE ────────────────────────
            //
            // `state.shape` is not a number: it is the description that the part
            // gives of itself — «a revolution of the profile Ø30 for 40, Ø20
            // for 30, Ø34 for 20». Demanding it EQUAL would mean rewriting
            // the case at every comma fixed; what matters is that the
            // pieces that matter are there, and that those that must NOT be there
            // are not — the second half is the more important one, because
            // it is how one catches a sentence that declares a profile that the part
            // no longer has.
            bool includes = expectation.ContainsKey("includes");
            bool doesNotContain = expectation.ContainsKey("does_not_include");
            if (includes || doesNotContain)
            {
                if (expectation.ContainsKey("equal_to"))
                    throw new CouldNotMeasure("«" + what + "» has «equal_to» together with " +
                                        "«includes»/«does_not_include»: decide which of the two");
                return Sentence(what, expectation, fromDocument, docState, measurements, out detail);
            }

            if (!expectation.ContainsKey("equal_to")) throw new CouldNotMeasure("«" + what + "» without «equal_to»");

            double tolerance = 0;
            if (expectation.ContainsKey("tolerance") && expectation["tolerance"] != null)
            {
                tolerance = Convert.ToDouble(expectation["tolerance"], CultureInfo.InvariantCulture);
                // THE WHY IS MANDATORY, and it is not bureaucracy: a
                // tolerance without a reason is the place where one day somebody
                // will widen the number instead of looking at the part.
                if (Math.Abs(tolerance) > 1e-12 && Text(expectation, "tolerance_why") == null)
                    throw new CouldNotMeasure("«" + what + "» has tolerance " +
                                        tolerance.ToString(CultureInfo.InvariantCulture) +
                                        " and does not say «tolerance_why»");
            }

            object measured = Measure(what, fromDocument, docState, measurements);
            object expected = expectation["equal_to"];

            // A list of numbers: the bounding box, a point, a triple.
            var expectedList = expected as System.Collections.IEnumerable;
            if (expectedList != null && !(expected is string))
            {
                var expectedValues = Numbers(expectedList);

                // ── A LIST THAT HAS NO NUMBERS ───────────────────────────
                //
                // `["C2"]`, the bodies a boolean worked on. Up to
                // 0.81.0 it ended in «is not a list of numbers:
                // System.String[]» — a message true and useless: the case
                // asked for a sensible thing and the evaluator did not know how to
                // compare it. A list of TEXT is compared one by one,
                // with the same writing that serves the twins.
                if (expectedValues == null)
                {
                    var measuredList = measured as System.Collections.IEnumerable;
                    if (measuredList == null || measured is string)
                    {
                        detail = what + " is not a list: " + WhatWas(measured);
                        return false;
                    }
                    var q = new List<string>();
                    foreach (var x in expectedList) q.Add(HowItIsWritten(x));
                    var r = new List<string>();
                    foreach (var x in measuredList) r.Add(HowItIsWritten(x));
                    detail = what + " [" + string.Join(", ", r) + "] (expected [" +
                                string.Join(", ", q) + "])";
                    if (q.Count != r.Count) return false;
                    for (int i = 0; i < q.Count; i++)
                        if (!string.Equals(q[i], r[i], StringComparison.Ordinal)) return false;
                    return true;
                }

                var measuredValues = Numbers(measured as System.Collections.IEnumerable);
                if (measuredValues == null)
                {
                    detail = what + " is not a list of numbers: " + WhatWas(measured);
                    return false;
                }
                detail = what + " " + Set(measuredValues) + " (expected " + Set(expectedValues) +
                            (tolerance > 0 ? " ±" + tolerance.ToString(CultureInfo.InvariantCulture) : "") + ")";
                if (expectedValues.Length != measuredValues.Length) return false;
                for (int i = 0; i < expectedValues.Length; i++)
                    if (Math.Abs(expectedValues[i] - measuredValues[i]) > tolerance) return false;
                return true;
            }

            // A number.
            double expectedNumber;
            if (expected != null && !(expected is string) && !(expected is bool) &&
                double.TryParse(Convert.ToString(expected, CultureInfo.InvariantCulture),
                                NumberStyles.Any, CultureInfo.InvariantCulture, out expectedNumber))
            {
                double measuredNumber;
                if (measured == null ||
                    !double.TryParse(Convert.ToString(measured, CultureInfo.InvariantCulture),
                                     NumberStyles.Any, CultureInfo.InvariantCulture, out measuredNumber))
                {
                    detail = what + " is not a number: " + WhatWas(measured);
                    return false;
                }
                detail = string.Format(CultureInfo.InvariantCulture,
                    "{0} {1:0.###} (expected {2:0.###}{3})", what, measuredNumber, expectedNumber,
                    tolerance > 0 ? " ±" + tolerance.ToString(CultureInfo.InvariantCulture) : "");
                return Math.Abs(measuredNumber - expectedNumber) <= tolerance;
            }

            // Everything else is compared as text: «material», a boolean.
            string a = Convert.ToString(expected, CultureInfo.InvariantCulture);
            string m = Convert.ToString(measured, CultureInfo.InvariantCulture);
            detail = what + " «" + m + "» (expected «" + a + "»)";
            return string.Equals(a, m, StringComparison.Ordinal);
        }

        /// <summary>
        /// AN EXPECTATION ON A SENTENCE: which pieces must be there, and which not.
        ///
        /// `does_not_include` is not the symmetric convenience of `includes`: it is the
        /// reason this exists. A sentence that declares «the profile
        /// Ø30 for 40» on a part that no longer has that profile is the
        /// defect that gives no error, and it is caught only by demanding that a
        /// word DISAPPEAR.
        ///
        /// The comparison is exact — capitals and accents included — because a
        /// sentence that the model reads changes meaning with one letter.
        /// </summary>
        private static bool Sentence(
            string what, Dictionary<string, object> expectation, Func<string, object> fromDocument,
            Dictionary<string, object> docState, Dictionary<string, object> measurements,
            out string detail)
        {
            object measured = Measure(what, fromDocument, docState, measurements);
            if (measured == null)
            {
                detail = what + " is missing: an absent phrase is not an empty phrase";
                return false;
            }

            // ── A LIST IS NOT «System.String[]» ──────────────────────────
            //
            // `measurements.profiles[0].measurements` is a list of measurements: «80X80X5»,
            // «100X100X6». Asking that it CONTAIN an entry is the most
            // natural thing there is, and up to 0.91.0 it failed with
            //
            //     does not say «80X80X5»: System.String[]
            //
            // — a message true and useless, because `Convert.ToString` on an
            // array gives the name of the type. For `equal_to` text lists
            // had already been taught (entry 0.81.0); for `includes` not, and the
            // defect remained until a case picked it up again.
            //
            // On a list «includes» means **one of the entries contains it**,
            // not «the writing of the list contains it»: this way looking for
            // «80X80X5» finds the entry, and looking for «80X80» finds the same entry
            // by prefix, which is what whoever writes the case expects.
            var entries = Items(measured);
            bool isList = entries != null;
            string sentence = isList
                ? string.Join(", ", entries)
                : Convert.ToString(measured, CultureInfo.InvariantCulture);

            var areMissing = new List<string>();
            foreach (var p in Parts(expectation, "includes"))
                if (!Inside(entries, sentence, p)) areMissing.Add("«" + p + "»");

            var tooMuch = new List<string>();
            foreach (var p in Parts(expectation, "does_not_include"))
                if (Inside(entries, sentence, p)) tooMuch.Add("«" + p + "»");

            if (areMissing.Count == 0 && tooMuch.Count == 0)
            {
                detail = what + (isList ? " [" + Short(sentence) + "]" : ": " + Short(sentence));
                return true;
            }
            detail = what +
                (areMissing.Count > 0 ? " does not say " + string.Join(", ", areMissing) : "") +
                (areMissing.Count > 0 && tooMuch.Count > 0 ? ", e" : "") +
                (tooMuch.Count > 0 ? " still says " + string.Join(", ", tooMuch) : "") +
                ": " + Short(sentence);
            return false;
        }

        /// <summary>
        /// The entries of a list, already written out. Null when what was
        /// measured is not a list — and a string is NOT a list, even
        /// if in C# it is enumerable character by character.
        /// </summary>
        private static List<string> Items(object measured)
        {
            if (measured == null || measured is string) return null;
            var e = measured as System.Collections.IEnumerable;
            if (e == null) return null;
            var outside = new List<string>();
            foreach (var x in e) outside.Add(HowItIsWritten(x));
            return outside;
        }

        /// <summary>
        /// Is the piece searched for there? On a list it is enough that ONE entry contains it;
        /// on a sentence, the sentence.
        /// </summary>
        /// <summary>
        /// WITHOUT LOOKING AT CAPITALS, and the why cost three times.
        ///
        /// ── THE THREE INCIDENTS ──────────────────────────────────────────
        ///
        ///   21/9  an expectation looked for «true», the text said «TRUE»
        ///   23/9  it looked for «overlap», the text says «overlaps»
        ///   23/9  it looked for «that was needed», the text says «THAT WAS NEEDED»
        ///
        /// The second was an ending, and it was fixed by looking for the root.
        /// The first and the third are the same thing, and they were not defects of the
        /// cases: `includes` looks at PROSE, written for a person, and in this
        /// repository emphasis is made by raising the voice — that is by changing
        /// the capitals. An expectation that breaks when a sentence is rewritten
        /// with more emphasis is not measuring what it says: it is measuring how
        /// it is written.
        ///
        /// *Three incidents from the same cause are not three cases to fix: it is
        /// one operator to fix.*
        ///
        /// ── AND `does_not_include` BECOMES MORE SEVERE ───────────────────────
        ///
        /// They change together, and it is the uncomfortable direction: a «does not say X» that
        /// passed only because the text wrote «x» now turns red. It is
        /// right that it does — that green was a lucky case, not a
        /// measurement — and it is the reason why this change was made with a
        /// whole bench run on top of it instead of by eye.
        ///
        /// ── WHO REALLY WANTS THE CAPITALS ────────────────────────────────
        ///
        /// It has no operator, and that must be said instead of leaving it to be discovered.
        /// No case today needs one: it was looked at before removing the
        /// exact comparison. The day it was needed, `contains_exact` is added
        /// — a different name, not one more boolean on
        /// this one, because a switch that changes the meaning of an operator
        /// is an operator in disguise.
        /// </summary>
        private static bool Inside(List<string> entries, string sentence, string part)
        {
            if (entries != null)
                return entries.Any(v => v.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
            return sentence.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The sentence pieces of a field: a single string or a list.</summary>
        private static List<string> Parts(Dictionary<string, object> expectation, string theField)
        {
            var outside = new List<string>();
            object v;
            if (!expectation.TryGetValue(theField, out v) || v == null) return outside;
            if (v is string) { outside.Add((string)v); return outside; }
            var list = v as System.Collections.IEnumerable;
            if (list == null)
            { outside.Add(Convert.ToString(v, CultureInfo.InvariantCulture)); return outside; }
            foreach (var x in list)
                outside.Add(Convert.ToString(x, CultureInfo.InvariantCulture));
            if (outside.Count == 0)
                throw new CouldNotMeasure("«" + theField + "» is an empty list: " +
                                    "it asks for nothing, and would always pass");
            return outside;
        }

        /// <summary>
        /// Where a `what` is read from. Three sources, and the prefix says it:
        /// nothing = what the bench measures on its own, `state.` = the
        /// add-in's state, `measurements.` = what the operation reported.
        /// </summary>
        internal static object Measure(
            string what, Func<string, object> fromDocument,
            Dictionary<string, object> docState, Dictionary<string, object> measurements)
        {
            // THE THREE MEASUREMENTS OF THE DOCUMENT COME FROM OUTSIDE, and whoever does not have a
            // document in hand passes null: it is what makes this file
            // compilable where SOLIDWORKS is not there (entry F7).
            // THERE IS NO LONGER A LIST OF NAMES HERE.
            //
            // There was one, and it came unglued: a new measurement added to whoever
            // measures, and forgotten here, came out «I do not know how to measure» with the old list
            // next to it — a message that says something false about itself.
            // Everything that has no prefix is asked of whoever has the document,
            // and it is that one that says whether it does not know that name.
            if (!what.StartsWith("state.", StringComparison.Ordinal) &&
                !what.StartsWith("measurements.", StringComparison.Ordinal))
            {
                if (fromDocument == null)
                    throw new CouldNotMeasure(
                        "«" + what + "» is measured on the document, and there is none here");
                return fromDocument(what);
            }
            if (what.StartsWith("state.", StringComparison.Ordinal))
                return Inside(docState, what.Substring("state.".Length), what);
            return Inside(measurements, what.Substring("measurements.".Length), what);
        }

        /// <summary>
        /// A VALUE AS A STRING, EVEN WHEN IT IS A LIST.
        ///
        /// `Convert.ToString` on an `object[]` returns «System.Object[]»: TWO
        /// DIFFERENT PROFILES ARE WRITTEN THE SAME. The comparison between a case that
        /// must fail and its twin was made this way, and it became blind
        /// precisely on the argument that tells them apart. The blindness goes in both
        /// directions: here it gave an honest bench error — «it is identical to the
        /// refusal» on a pair that differed ONLY in the contours —
        /// but the same line would have said «it differs by 1 argument»
        /// looking at two lists it had not read.
        ///
        /// It is here, and not next to whoever uses it, because it is a pure function
        /// on a dictionary: `Oce.Solid.Agent.ContractTests` tests it in a thousandth of a
        /// second, without CAD (entry F7).
        /// </summary>
        internal static string HowItIsWritten(object v)
        {
            if (v == null) return "(nothing)";
            if (v is string) return (string)v;

            var dictionary = v as Dictionary<string, object>;
            if (dictionary != null)
            {
                var entries = new List<string>();
                foreach (var k in dictionary.Keys.OrderBy(x => x, StringComparer.Ordinal))
                    entries.Add(k + "=" + HowItIsWritten(dictionary[k]));
                return "{" + string.Join(", ", entries) + "}";
            }

            var list = v as System.Collections.IEnumerable;
            if (list != null)
            {
                var parts = new List<string>();
                foreach (var x in list) parts.Add(HowItIsWritten(x));
                return "[" + string.Join(", ", parts) + "]";
            }

            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// DOES THE TWIN HAVE TO DO WITH THE REFUSAL? (entry F5)
        ///
        /// The rule: **arguments not identical, and at least one argument in common
        /// with the same value.** The entry proposed «differs by ONE argument
        /// only»; measured on the canonical pairs it refused three out of
        /// four, and whoever calls says how many differ instead of
        /// deciding in place of whoever reads.
        ///
        /// ── THE EXCEPTION OF THE SINGLE ARGUMENT, WHICH IS ARITHMETIC ─────
        ///
        /// `create_axis` takes `face_uid` and nothing else. Two cases that differ
        /// in that single argument CANNOT have one in common: the
        /// rule would always refuse them, and not because they are unconnected but
        /// because the operation has only one argument. Refusing them would mean
        /// forbidding the pair for half of the catalog. When the keys in play
        /// are only one, «nothing in common» is not a hint: it is the arity.
        /// </summary>
        internal static bool Resemble(
            Dictionary<string, object> mine, Dictionary<string, object> itsItems,
            out string why)
        {
            mine = mine ?? new Dictionary<string, object>();
            itsItems = itsItems ?? new Dictionary<string, object>();

            var keys = new HashSet<string>(mine.Keys, StringComparer.Ordinal);
            foreach (var k in itsItems.Keys) keys.Add(k);

            int different = 0, equal = 0;
            var whichOnes = new List<string>();
            foreach (var k in keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                string a = mine.ContainsKey(k) ? HowItIsWritten(mine[k]) : null;
                string b = itsItems.ContainsKey(k) ? HowItIsWritten(itsItems[k]) : null;
                if (string.Equals(a, b, StringComparison.Ordinal)) equal++;
                else { different++; whichOnes.Add(k); }
            }

            if (different == 0)
            { why = "is identical to the refusal: it is not a twin, it is a copy"; return false; }

            if (equal == 0 && keys.Count > 1)
            {
                why = "does not even have one argument in common with the refusal: " +
                         "they are two operations that do not talk to each other";
                return false;
            }

            why = "same operation, differs by " + different +
                     (different == 1 ? " argument (" : " arguments (") +
                     string.Join(", ", whichOnes) + ")";
            if (equal == 0)
                why += ", which is the only one this operation takes";
            return true;
        }

        /// <summary>
        /// The field of «must_fail» that declares a twin with the SAME
        /// arguments on ANOTHER part. The value is the sentence that says what
        /// changes in the part: empty is not valid.
        /// </summary>
        internal const string DifferenceInPartField = "difference_is_the_part";

        /// <summary>
        /// THE TWIN WITH THE SAME ARGUMENTS, ON ANOTHER PART (entry F5,
        /// 28/9/2026, rib W1.2).
        ///
        /// There are refusals that do not depend on an argument but on the PART: the
        /// same rib (same line, same plane, same thickness) is born
        /// in the hollowed box and is not born on the plate without walls. The
        /// rule of `Resemble` demands a different argument, and before this
        /// function the only way to respect it was to write one that does
        /// nothing (a `distance_mm: 0` which is the default): F5 green without
        /// proving anything.
        ///
        /// Here the difference is DECLARED (<see cref="DifferenceInPartField"/>)
        /// and CHECKED, in both directions:
        ///   1. the sentence is there;
        ///   2. the arguments are IDENTICAL — if they differ, the normal rule
        ///      is enough and the declaration is superfluous (or hides a loose
        ///      pair);
        ///   3. the steps BEFORE (the part) differ — if they are equal, same
        ///      part and same arguments: it is a copy, as before.
        ///
        /// The steps are compared as they are written (operation and arguments),
        /// without CAD: it is arithmetic on the JSON, and the check is Oce.Solid.Agent.ContractTests.
        /// </summary>
        internal static bool ResembleInPart(
            Dictionary<string, object> mine, Dictionary<string, object> itsItems,
            IList<object> myPart, IList<object> itsPart, string declaration,
            out string why)
        {
            if (string.IsNullOrWhiteSpace(declaration))
            {
                why = "«" + DifferenceInPartField + "» is empty: it must say what changes in the part " +
                         "between the refusal and the twin";
                return false;
            }

            string onArguments;
            if (Resemble(mine, itsItems, out onArguments))
            {
                why = "declares that the difference is the part, but the arguments already differ (" +
                         onArguments + "): the normal rule is enough, remove «" + DifferenceInPartField + "»";
                return false;
            }
            if (onArguments.IndexOf("copy", StringComparison.Ordinal) < 0)
            {
                why = "declares that the difference is the part, but the arguments are not the same: " +
                         onArguments;
                return false;
            }

            var myOwn = (myPart ?? new List<object>()).Select(HowAStepIsWritten).ToList();
            var its = (itsPart ?? new List<object>()).Select(HowAStepIsWritten).ToList();
            int firstDifferent = -1;
            for (int i = 0; i < Math.Max(myOwn.Count, its.Count); i++)
            {
                string a = i < myOwn.Count ? myOwn[i] : null, b = i < its.Count ? its[i] : null;
                if (!string.Equals(a, b, StringComparison.Ordinal)) { firstDifferent = i; break; }
            }
            if (firstDifferent < 0)
            {
                why = "same arguments AND same part (the " + myOwn.Count + " steps before are equal): " +
                         "it is not a twin, it is a copy";
                return false;
            }

            why = "same arguments, part different from step " + (firstDifferent + 1) + " (" +
                     declaration.Trim() + ")";
            return true;
        }

        /// <summary>A step of a case as it is written: operation and arguments.</summary>
        private static string HowAStepIsWritten(object planStep)
        {
            var p = planStep as Dictionary<string, object>;
            if (p == null) return HowItIsWritten(planStep);
            object op, arg;
            p.TryGetValue("operation", out op);
            p.TryGetValue("arguments", out arg);
            return HowItIsWritten(op) + " " + HowItIsWritten(arg);
        }

        /// <summary>
        /// A path inside a dictionary: `faces[0].area_mm2`.
        /// Null when the path leads nowhere — and whoever calls turns it
        /// into a red, because a missing field is not a zero.
        /// </summary>
        internal static object Inside(object root, string filePath, string fullName)
        {
            object here = root;
            foreach (var part in filePath.Split('.'))
            {
                string key = part;
                int index = -1;
                int bracket = part.IndexOf('[');
                if (bracket >= 0 && part.EndsWith("]", StringComparison.Ordinal))
                {
                    key = part.Substring(0, bracket);
                    int.TryParse(part.Substring(bracket + 1, part.Length - bracket - 2), out index);
                }

                var map = here as Dictionary<string, object>;
                if (map == null)
                    throw new CouldNotMeasure(
                        "missing field: " + fullName + "; «" + key +
                        "» would have to be looked for inside something that is not an object");

                // THE KEY THAT IS NOT THERE AND A NULL VALUE ARE NOT THE SAME
                // THING, and it is the whole of entry F4. `material: null` means
                // «that place exists and is empty»; a key that is missing means
                // that the case is reading a field that no longer exists —
                // and a `null` compared with a `null` would pass.
                if (!map.ContainsKey(key))
                    throw new CouldNotMeasure(
                        "missing field: " + fullName + "; there are: " +
                        string.Join(", ", map.Keys.OrderBy(k => k, StringComparer.Ordinal)));
                here = map[key];

                if (index >= 0)
                {
                    var itemList = here as System.Collections.IList;
                    if (itemList == null)
                        throw new CouldNotMeasure(
                            "missing field: " + fullName + "; «" + key + "» is not a list");
                    if (index >= itemList.Count)
                        throw new CouldNotMeasure(
                            "missing field: " + fullName + "; «" + key + "» has " +
                            itemList.Count + " entries and the case asks for number " + index);
                    here = itemList[index];
                }
            }
            return here;
        }

        // ==================================================================
        // THE SELECTORS
        // ==================================================================
        internal static object Resolve(object raw, Dictionary<string, object> docState)
        {
            var map = raw as Dictionary<string, object>;
            if (map != null)
            {
                var outside = new Dictionary<string, object>();
                foreach (var v in map) outside[v.Key] = Resolve(v.Value, docState);
                return outside;
            }
            var itemList = raw as System.Collections.IList;
            if (itemList != null)
            {
                var outside = new List<object>();
                foreach (var v in itemList) outside.Add(Resolve(v, docState));
                return outside;
            }
            var text = raw as string;
            if (text != null && text.StartsWith("@", StringComparison.Ordinal))
                return Select(text, docState);

            // THE PLACEHOLDERS: a case that names a path of THIS machine
            // is not a check, it is a memento. The library of the welded profiles
            // is where every installation puts it, and the case says «{PROFILES}»
            // instead of knowing where it is.
            if (text != null && text.Contains("{PROFILES}"))
            {
                // `String.Replace(a, null)` raises `ArgumentNullException`, and
                // from here it would come out as a bench fault instead of as the
                // thing it is: a missing precondition. It is said.
                if (Profiles == null)
                    throw new CouldNotMeasure(
                        "This case names {PROFILES}, but OCE_WELDMENT_PROFILES is not " +
                        "set and there is no default path. The profile library " +
                        "belongs to the person working, not to the repository: set the variable to its folder.");
                return text.Replace("{PROFILES}", Profiles);
            }
            // `{WORK}`: where a case can write a file without dirtying
            // either the repository or the disk of whoever works.
            if (text != null && text.Contains("{WORK}"))
                return text.Replace("{WORK}", Work);
            // `{DATA}`: the versioned input files (Bench\data\).
            if (text != null && text.Contains("{DATA}"))
                return text.Replace("{DATA}", Data);
            // `{SOLIDWORKS}`: the STOCK files of the installation (the
            // table templates), which belong neither to the repository nor to whoever works.
            if (text != null && text.Contains("{SOLIDWORKS}"))
            {
                var sw = SolidWorksFolder;
                if (sw == null)
                    throw new CouldNotMeasure(
                        "This case names {SOLIDWORKS}, but I cannot find the SOLIDWORKS " +
                        "installation folder: give it with SW_INSTALL_PATH.");
                return text.Replace("{SOLIDWORKS}", sw);
            }
            return raw;
        }

        internal static string Select(string expression, Dictionary<string, object> docState)
        {
            int opens = expression.IndexOf('(');
            if (opens < 0 || !expression.EndsWith(")", StringComparison.Ordinal))
                throw new CouldNotMeasure("malformed selector: «" + expression + "»");
            string typ = expression.Substring(1, opens - 1);
            var conditions = Conditions(expression.Substring(opens + 1, expression.Length - opens - 2));

            switch (typ)
            {
                // `n=` APPLIES TO ALL, not only to features: among four
                // equal long edges there is no criterion at all, and whoever
                // writes the case can decide the order — provided they SAY so.
                // Without `n=` the ambiguity remains a refusal.
                case "face":  return OnlyOne(expression, "faces", docState, conditions, "uid", Nth(expression, conditions));
                case "edge": return OnlyOne(expression, "edges", docState, conditions, "uid", Nth(expression, conditions));
                case "feature": return AFeature(expression, docState, conditions);
                case "dimension":   return ADimension(expression, docState, conditions);
                // BODIES ARE CALLED BY NAME, NOT BY NUMBER. Measured on
                // 25/9/2026 on three structural members created in the same
                // order in two runs: once the state says C1 = the short bar,
                // the other time C1 = the long one. «C1» written in a case means
                // a different body depending on the run; the name («... (2)»)
                // follows instead the order of creation, and it is what the case
                // means. The name has the parentheses inside, and the selector
                // copes with them: it opens at the FIRST and closes at the LAST.
                case "body":   return OnlyOne(expression, "bodies", docState, conditions, "uid", Nth(expression, conditions));
                default:
                    throw new CouldNotMeasure("unknown selector «@" + typ + "»: there are " +
                                        "@face, @edge, @feature, @dimension, @body");
            }
        }

        /// <summary>
        /// `normal=front, length~=100`: the `~` means «about», and it is for
        /// lengths, which are never whole when the part has been rebuilt.
        /// </summary>
        internal sealed class Condition
        {
            public string Field;
            public string Value;
            public bool Circa;
        }

        internal static List<Condition> Conditions(string inside)
        {
            var outside = new List<Condition>();
            foreach (var part in inside.Split(','))
            {
                var t = part.Trim();
                if (t.Length == 0) continue;
                bool circa = t.Contains("~=");
                int equal = circa ? t.IndexOf("~=", StringComparison.Ordinal) : t.IndexOf('=');
                if (equal < 0) throw new CouldNotMeasure("condition without «=»: «" + t + "»");
                outside.Add(new Condition
                {
                    Field = t.Substring(0, equal).Trim(),
                    Value = t.Substring(equal + (circa ? 2 : 1)).Trim(),
                    Circa = circa,
                });
            }
            return outside;
        }

        // THE NAMES OF THE CASE AND THE NAMES OF THE STATE ARE NOT THE SAME, and the
        // translation is here and in one place only. `@edge(axis=Z)` reads
        // well; the field of the state is called `along`, which by itself would mean
        // something else.
        internal static string StateField(string nameInCase)
        {
            switch (nameInCase)
            {
                case "axis":   return "along";
                case "length": return "length_mm";
                case "area":   return "area_mm2";
                case "normal": return "facing";
                default:      return nameInCase;
            }
        }

        /// <summary>
        /// The value that a selector condition compares.
        ///
        /// `x`, `y`, `z` ARE THE COORDINATES OF `point_mm` (25/9/2026, entry E18).
        /// Among the four long edges of 120 of a plate no field of the
        /// state told the left side in front from the one behind, and
        /// `n=` chooses by ORDER — the same order that for the bodies was
        /// seen to change from one run to the next (entry Z-C16). With the
        /// coordinates the edge is named by WHERE it is:
        /// `@edge(axis=Y,x~=-100,z~=20)`.
        /// </summary>
        internal static bool FieldValue(Dictionary<string, object> entry, string theField, out object val)
        {
            int index = theField == "x" ? 0 : theField == "y" ? 1 : theField == "z" ? 2 : -1;
            if (index >= 0)
            {
                val = null;
                object point;
                if (!entry.TryGetValue("point_mm", out point)) return false;
                var list = point as System.Collections.IList;
                if (list == null || list.Count < 3) return false;
                val = list[index];
                return true;
            }
            return entry.TryGetValue(StateField(theField), out val);
        }

        internal static string OnlyOne(
            string expression, string family, Dictionary<string, object> docState,
            List<Condition> conditions, string fieldToReturn, int? nth)
        {
            var entries = (docState != null && docState.ContainsKey(family)
                ? docState[family] : null) as System.Collections.IList;
            if (entries == null)
                throw new CouldNotMeasure("the state has no «" + family + "»: " + expression);

            var foundOnes = new List<Dictionary<string, object>>();
            foreach (var v in entries)
            {
                var entry = v as Dictionary<string, object>;
                if (entry == null) continue;
                bool goes = true;
                foreach (var c in conditions)
                {
                    if (c.Field == "n") continue;
                    object val;
                    if (!FieldValue(entry, c.Field, out val) || val == null)
                    { goes = false; break; }
                    if (c.Circa)
                    {
                        double a, b;
                        if (!double.TryParse(Convert.ToString(val, CultureInfo.InvariantCulture),
                                             NumberStyles.Any, CultureInfo.InvariantCulture, out a) ||
                            !double.TryParse(c.Value, NumberStyles.Any,
                                             CultureInfo.InvariantCulture, out b) ||
                            Math.Abs(a - b) > 0.5)
                        { goes = false; break; }
                    }
                    else if (!string.Equals(
                        Convert.ToString(val, CultureInfo.InvariantCulture), c.Value,
                        StringComparison.OrdinalIgnoreCase))
                    { goes = false; break; }
                }
                if (goes) foundOnes.Add(entry);
            }

            if (nth.HasValue)
            {
                if (nth.Value < 1 || nth.Value > foundOnes.Count)
                    throw new CouldNotMeasure(expression + ": I found " + foundOnes.Count +
                                        ", and the case asks for number " + nth.Value);
                return Convert.ToString(foundOnes[nth.Value - 1][fieldToReturn]);
            }

            // A READING OF EVERY FACE THAT WAS CUT ALL THE SAME (W3.1-bis.12): the
            // part has more faces than one reading lists (FaceFilter.MaxLimit).
            // One face found on that list does not prove it is the only one —
            // its twin may be beyond the cut, and that is the silent defect
            // below. `n=` above is safe: the listing keeps the largest first, so
            // the n-th found is the n-th of the part.
            string cutNote = family == "faces" ? FacesCutByFilter(docState) : null;
            if (cutNote != null && foundOnes.Count <= 1)
                throw new CouldNotMeasure(expression + ": " + (foundOnes.Count == 0 ? "I did not find it" : "I found 1") +
                                    " among the faces listed, and " + cutNote +
                                    ": the face may be beyond them, or have a twin there. Add a condition, or «n=»");

            // ZERO OR MORE THAN ONE: IT IS REFUSED, the first is not chosen.
            // It is the silent defect that this entry names: a case that runs
            // on a face different from the one the file name promises, and
            // that perhaps returns the same volume.
            if (foundOnes.Count == 0)
                throw new CouldNotMeasure(expression + ": I did not find it");
            if (foundOnes.Count > 1)
                throw new CouldNotMeasure(expression + ": I found " + foundOnes.Count +
                                    ". Picking one would be guessing: add a " +
                                    "condition, or «n=» if you decide the order yourself");
            return Convert.ToString(foundOnes[0][fieldToReturn]);
        }

        /// <summary>
        /// Why a state read with a face filter (`faces_filter`) did not list every
        /// face that matched it, or null when it did (or when it is an ordinary
        /// reading: its cut at 60 is what the bench reads again, see
        /// StateForSelectors in Program.cs).
        /// </summary>
        internal static string FacesCutByFilter(Dictionary<string, object> docState)
        {
            if (docState == null || !docState.ContainsKey("faces_filter")) return null;
            object notListed;
            if (!docState.TryGetValue("faces_not_listed", out notListed) || notListed == null) return null;
            int n;
            try { n = Convert.ToInt32(notListed, CultureInfo.InvariantCulture); }
            catch (Exception) { return null; }
            if (n <= 0) return null;
            object total;
            docState.TryGetValue("faces_total", out total);
            return n + " of the " + Convert.ToString(total, CultureInfo.InvariantCulture) +
                   " faces of the part were not listed (one reading lists at most 500)";
        }

        internal static string AFeature(
            string expression, Dictionary<string, object> docState, List<Condition> conditions)
        {
            return OnlyOne(expression, "feature", docState, conditions, "uid",
                           Nth(expression, conditions));
        }

        /// <summary>The `n=` of a selector, if it is there.</summary>
        internal static int? Nth(string expression, List<Condition> conditions)
        {
            var n = conditions.FirstOrDefault(c => c.Field == "n");
            if (n == null) return null;
            int k;
            if (!int.TryParse(n.Value, out k))
                throw new CouldNotMeasure(expression + ": «n» is not a number");
            return k;
        }

        /// <summary>
        /// `@dimension(feature=Sketch1,name=D1)` -> the EXTENDED name of the dimension, as
        /// the state writes it: `D1@Sketch1@Part286.Part`.
        ///
        /// It is the selector that the entry called «not a luxury»:
        /// `plans/change_diameter.json` writes the name with the document title
        /// inside, and that `Part76` changes at every run — **that plan today cannot
        /// be re-run.** By resolving it against the state, the case names the
        /// dimension and the title is put in by the bench.
        ///
        /// `angular=true` (entry Z-C21.2, finding M3) keeps only the dimensions that the
        /// state writes in `deg`, and then «name» can be omitted: the names that
        /// SOLIDWORKS gives to the dimensions of a chamfer (D1, D2) and the order in which it
        /// lists them are not measured, while «the only angular dimension of the part» is
        /// a fact that the state tells.
        /// </summary>
        internal static string ADimension(
            string expression, Dictionary<string, object> docState, List<Condition> conditions)
        {
            string insideA = conditions.FirstOrDefault(c => c.Field == "feature")?.Value;
            string call = conditions.FirstOrDefault(c => c.Field == "name")?.Value;
            string angularText = conditions.FirstOrDefault(c => c.Field == "angular")?.Value;
            bool? angular = null;
            if (angularText != null)
            {
                bool a;
                if (!bool.TryParse(angularText, out a))
                    throw new CouldNotMeasure(expression + ": «angular» is true or false");
                angular = a;
            }
            if (call == null && angular == null)
                throw new CouldNotMeasure(expression + ": «name» (or «angular») is needed");

            var feature = (docState != null && docState.ContainsKey("feature")
                ? docState["feature"] : null) as System.Collections.IList;
            if (feature == null) throw new CouldNotMeasure("the state has no «feature»: " + expression);

            var foundOnes = new List<string>();
            foreach (var f in feature)
            {
                var entry = f as Dictionary<string, object>;
                if (entry == null || !entry.ContainsKey("dimensions")) continue;
                var dims = entry["dimensions"] as System.Collections.IList;
                if (dims == null) continue;
                foreach (var q in dims)
                {
                    var dimension = q as Dictionary<string, object>;
                    if (dimension == null || !dimension.ContainsKey("name")) continue;
                    string extended = Convert.ToString(dimension["name"]);
                    // «D1@Sketch1@Part286.Part» -> short «D1», inside «Sketch1»
                    var parts = extended.Split('@');
                    if (parts.Length < 2) continue;
                    if (call != null &&
                        !string.Equals(parts[0], call, StringComparison.OrdinalIgnoreCase)) continue;
                    if (angular.HasValue && dimension.ContainsKey("deg") != angular.Value) continue;
                    if (insideA != null &&
                        !string.Equals(parts[1], insideA, StringComparison.OrdinalIgnoreCase)) continue;
                    foundOnes.Add(extended);
                }
            }

            if (foundOnes.Count == 0) throw new CouldNotMeasure(expression + ": I did not find it");
            if (foundOnes.Count > 1)
                throw new CouldNotMeasure(expression + ": I found " + foundOnes.Count +
                                    " (" + string.Join(", ", foundOnes) + ")");
            return foundOnes[0];
        }

        // ==================================================================
        // MINUTERIA
        // ==================================================================
        internal static string Text(Dictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v) || v == null) return null;
            var s = Convert.ToString(v);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        internal static bool True(Dictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v) || v == null) return false;
            try { return Convert.ToBoolean(v); } catch { return false; }
        }

        internal static List<object> Items(Dictionary<string, object> map, string key)
        {
            object v;
            if (map == null || !map.TryGetValue(key, out v) || v == null) return null;
            var itemList = v as System.Collections.IList;
            if (itemList == null) return null;
            var outside = new List<object>();
            foreach (var x in itemList) outside.Add(x);
            return outside;
        }

        internal static double[] Numbers(System.Collections.IEnumerable itemList)
        {
            if (itemList == null) return null;
            var outside = new List<double>();
            foreach (var x in itemList)
            {
                double d;
                if (!double.TryParse(Convert.ToString(x, CultureInfo.InvariantCulture),
                                     NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                    return null;
                outside.Add(d);
            }
            return outside.ToArray();
        }

        internal static string Set(double[] n)
        {
            return "[" + string.Join(", ", n.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture))) + "]";
        }

        /// <summary>
        /// What that value is, for a message. It is called this and not
        /// `Describe` because in `NewOperationsTests.cs` a `Describe` already exists: two
        /// methods with the same name in the same partial class do not
        /// compile, which is the best possible end for a repeated name.
        /// </summary>
        internal static string WhatWas(object x)
        {
            if (x == null) return "(not there)";
            return Short(Convert.ToString(x, CultureInfo.InvariantCulture));
        }

        internal static int HowManyFeatures(Dictionary<string, object> docState)
        {
            var f = (docState != null && docState.ContainsKey("feature")
                ? docState["feature"] : null) as System.Collections.IList;
            return f == null ? 0 : f.Count;
        }
    }
}
