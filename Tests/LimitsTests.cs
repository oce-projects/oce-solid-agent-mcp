using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// A MEASURED LIMIT THAT THE DESCRIPTION DOES NOT SAY COSTS A WASTED TURN
    /// (entries Z-G5 and Z-C11).
    ///
    /// ── THE TWO HALVES OF THE SAME THING ─────────────────────────────────
    ///
    /// When an operation refuses, somebody has already measured that limit and
    /// written a bench case for it. But **the model does not read the bench
    /// cases**: it reads the catalogue. If the description is silent, the only way it has
    /// to discover the limit is to bump into it — and a refusal costs a run,
    /// while a declared limit costs zero.
    ///
    /// *Declaring a limit before trying is worth more than a refusal.*
    ///
    /// And the other half (Z-C11): **every refusal is born with its green
    /// twin**. The mirror risk — refusing a legitimate part — is not
    /// reviewed by anybody, because a wrong refusal *looks* like prudence. The
    /// twin is what tells a guard from an operation that always
    /// says no, and it really happened: the first version of
    /// `section_view` refused everything, and the defect came out at the first
    /// real use.
    ///
    /// ── WHY HERE AND NOT IN THE BENCH ─────────────────────────────────────
    ///
    /// The bench already checks the twins, **but after an hour of running and with
    /// SOLIDWORKS on**. A refusal case written without a twin is a
    /// writing error, not a measurement error: it is seen by reading the JSON, in a
    /// tenth of a second, and whoever writes it must know before launching the
    /// bench, not after.
    /// </summary>
    internal static class LimitsTests
    {
        /// <summary>
        /// How many operations have a refusal tested by the bench and **do not
        /// say it** in their description. **Zero, since 23/9/2026.**
        ///
        /// There were twelve that morning — slot, activate_configuration,
        /// activate_sheet, create_configuration, drawing_dimension, linear_pattern,
        /// revolve, chamfer_edges, suppress_feature, mirror,
        /// cylindrical_cut, rectangular_cut — and each had a limit
        /// already measured, with a bench case that demands it, which the
        /// model could discover **only by bumping into it**.
        ///
        /// The new sentences are not invented: they are the message that the case
        /// demands, turned to the future. *A declared limit costs zero turns;
        /// the same limit discovered by a refusal costs one.*
        ///
        /// It is a CEILING, and from zero it can only go up — that is, it cannot. A
        /// new guard without the sentence that declares it turns this check red
        /// **on the day it is written**, which is the only day
        /// on which somebody is looking at it.
        /// </summary>
        private const int Mute = 0;

        /// <summary>
        /// The words with which a description declares a limit. They are the ones
        /// the catalogue really uses, not the ones it «should» use: a
        /// list invented at a desk would say that all of them are silent.
        /// </summary>
        private static readonly string[] Telltales =
        {
            "rifiut", "non posso", "limite", "non guarda", "non sa",
            "non copre", "fallisc", "sbord", "piu' grande",
            // The catalogue is moving to English (Z-C21.10): these are the
            // English stems of the same declaration.
            "refus", "reject",
        };

        /// <summary>
        /// How many declared cases cite a body BY NUMBER («C1», «C2») in a
        /// `_uid` field. Measured on 25/9/2026: the order of the bodies in the state
        /// comes from `GetBodies2`, **does not follow creation and changes between one run
        /// and the next** (three structural members: C1 = the short one in one run, the
        /// long one in the other). Many of these cases have a single body when
        /// they cite it, and are harmless; the others are the list of entry Z-C16.
        /// The bench has `@body(name=...)`, which follows the creation order.
        ///
        /// **12 at 25/9/2026**, seen red with a zero ceiling before fixing it (the two
        /// refusals on `C9`, which exists in no order, stay out).
        /// </summary>
        private const int BodiesByNumberCap = 12;

        /// <summary>The «C\d+» written by hand in the `_uid` fields of the arguments.</summary>
        private static IEnumerable<string> BodiesByNumber(object arguments)
        {
            var d = arguments as Dictionary<string, object>;
            if (d == null) yield break;
            foreach (var kv in d)
            {
                if (!kv.Key.EndsWith("_uid", StringComparison.Ordinal)) continue;
                var values = kv.Value as object[] ?? new[] { kv.Value };
                foreach (var v in values)
                {
                    var s = v as string;
                    if (s != null && System.Text.RegularExpressions.Regex.IsMatch(s, @"^C\d+$"))
                        yield return s;
                }
            }
        }

        internal static void All()
        {
            Group("the measured limits and the descriptions that declare them (Z-G5, Z-C11)");

            var folder = Path.Combine(Root(), "Bench", "cases");
            if (!Directory.Exists(folder))
            {
                Check("the cases folder is there", false, "missing " + folder);
                return;
            }

            var serializer = new JavaScriptSerializer();
            int declared = 0, planes = 0;
            var unreadable = new List<string>();
            var withoutTwin = new List<string>();
            var absentTwins = new List<string>();
            var withoutMessage = new List<string>();
            var refusedOperations = new Dictionary<string, int>(StringComparer.Ordinal);

            var file = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories);
            var names = new HashSet<string>(
                file.Select(f => Path.GetFileNameWithoutExtension(f)), StringComparer.Ordinal);
            // THE TOLERANCE WITHOUT THE WHY. The bench stops it as a bench
            // error — but after an hour of running. On the evening of 24/9/2026 a new case
            // did it: a whole run lost for a line that is seen by reading
            // the JSON. Here it is seen before launching.
            var withoutWhy = new List<string>();
            var muteSteps = new List<string>();
            // THE FAKE TWIN: same operation and arguments IDENTICAL to the refusal.
            // The bench stops it as a bench error (entry F5) — after an hour. On
            // 25/9/2026 it happened with `flat_pattern_dxf`, where the refusal depends
            // on the state and not on an argument. Here it is compared beforehand.
            var refusalsToCompare = new List<Tuple<string, string, string, string>>(); // case, operation, arguments, twin
            var argumentsPerCase = new Dictionary<string, List<Tuple<string, string>>>(StringComparer.Ordinal);
            // THE TWIN AS THE BENCH SEES IT (25/9/2026): the first SUCCEEDED
            // step of the same operation, and at least one argument in common.
            var caseSteps = new Dictionary<string, List<Tuple<string, Dictionary<string, object>, bool>>>(StringComparer.Ordinal);
            var refusalsWithTwin = new List<Tuple<string, string, Dictionary<string, object>, string>>();
            // THE TWIN WITH THE SAME ARGUMENTS ON ANOTHER PART (28/9/2026):
            // case, operation, arguments, twin, index of the step, declared
            // sentence. It is looked at with `Expectations.ResembleInPart`, the same
            // function as the bench, and not with the two rules above.
            var refusalsInPart = new List<Tuple<string, string, Dictionary<string, object>, string, int, string>>();
            var rawSteps = new Dictionary<string, object[]>(StringComparer.Ordinal);
            var js = new JavaScriptSerializer();
            var casesWithNumbers = new SortedSet<string>(StringComparer.Ordinal);
            // Z-C6: operation -> declared cases that use it in a step.
            var casesPerOp = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var f in file)
            {
                string itemName = Path.GetFileNameWithoutExtension(f);
                object root;
                try { root = serializer.DeserializeObject(File.ReadAllText(f)); }
                catch (Exception ex) { unreadable.Add(itemName + ": " + ex.Message); continue; }

                var testCase = root as Dictionary<string, object>;
                if (testCase == null) { planes++; continue; }
                declared++;

                // The expectations are in the steps («expect») and, for the regression,
                // in «after». Both are looked at.
                Action<object, string> checkExpectations = (list, location) =>
                {
                    foreach (var at in (list as object[]) ?? new object[0])
                    {
                        var expectation = at as Dictionary<string, object>;
                        if (expectation == null || !expectation.ContainsKey("tolerance")) continue;
                        if (!expectation.ContainsKey("tolerance_why"))
                            withoutWhy.Add(itemName + " / " + location + " / " +
                                            (expectation.ContainsKey("what") ? Convert.ToString(expectation["what"]) : "?"));
                    }
                };
                object rawAfter;
                if (testCase.TryGetValue("after", out rawAfter)) checkExpectations(rawAfter, "after");

                object raw;
                if (!testCase.TryGetValue("steps", out raw)) continue;
                var steps = raw as object[];
                if (steps == null) continue;
                rawSteps[itemName] = steps;

                int number = 0;
                foreach (var p in steps)
                {
                    var planStep = p as Dictionary<string, object>;
                    number++;
                    if (planStep == null) continue;
                    object at0;
                    if (planStep.TryGetValue("expect", out at0)) checkExpectations(at0, "passo " + number);
                    // A STEP THAT MEASURES NOTHING (25/9/2026): the bench stops it
                    // as a bench error — after an hour and a half of running, and
                    // stopping the whole case there. It happened with the case of the
                    // drawing that was looked at: three holes without expectations, and the expectations of the
                    // drawing never ran. It is seen by reading the JSON.
                    else if (!planStep.ContainsKey("must_fail")) muteSteps.Add(itemName + " / step " + number);
                    object opN, argN;
                    string opName = planStep.TryGetValue("operation", out opN) ? Convert.ToString(opN) : "";
                    string argText = planStep.TryGetValue("arguments", out argN) ? js.Serialize(argN) : "{}";
                    List<Tuple<string, string>> itsItems;
                    if (!argumentsPerCase.TryGetValue(itemName, out itsItems)) argumentsPerCase[itemName] = itsItems = new List<Tuple<string, string>>();
                    itsItems.Add(Tuple.Create(opName, argText));
                    List<Tuple<string, Dictionary<string, object>, bool>> stepsHere;
                    if (!caseSteps.TryGetValue(itemName, out stepsHere))
                        caseSteps[itemName] = stepsHere = new List<Tuple<string, Dictionary<string, object>, bool>>();
                    stepsHere.Add(Tuple.Create(opName,
                        argN as Dictionary<string, object> ?? new Dictionary<string, object>(),
                        planStep.ContainsKey("must_fail")));
                    HashSet<string> opCases;
                    if (!casesPerOp.TryGetValue(opName, out opCases))
                        casesPerOp[opName] = opCases = new HashSet<string>(StringComparer.Ordinal);
                    opCases.Add(itemName);
                    // THE BODY BY NUMBER (Z-C16). A hand-written «C2» in a
                    // `_uid` field: only the number that the refusal
                    // itself names in the expected message is excluded (`C9`, which does not exist,
                    // is the point of the case, and does not depend on the order).
                    foreach (var cited in BodiesByNumber(argN))
                    {
                        object dvC; string expectedInMessage = "";
                        if (planStep.TryGetValue("must_fail", out dvC) && dvC is Dictionary<string, object>)
                            expectedInMessage = js.Serialize(dvC);
                        if (expectedInMessage.Contains("\"" + cited + "\"")) continue;
                        casesWithNumbers.Add(itemName);
                    }
                    object dv0;
                    if (planStep.TryGetValue("must_fail", out dv0) && dv0 is Dictionary<string, object>)
                    {
                        object g0;
                        var gem = ((Dictionary<string, object>)dv0).TryGetValue("twin", out g0) ? Convert.ToString(g0) : null;
                        object dich;
                        bool inPart = ((Dictionary<string, object>)dv0).TryGetValue(
                            Oce.Solid.Agent.Bench.Expectations.DifferenceInPartField, out dich);
                        if (!string.IsNullOrEmpty(gem) && inPart)
                            refusalsInPart.Add(Tuple.Create(itemName, opName,
                                argN as Dictionary<string, object> ?? new Dictionary<string, object>(), gem,
                                number - 1, Convert.ToString(dich)));
                        else if (!string.IsNullOrEmpty(gem))
                        {
                            refusalsToCompare.Add(Tuple.Create(itemName, opName, argText, gem));
                            refusalsWithTwin.Add(Tuple.Create(itemName, opName,
                                argN as Dictionary<string, object> ?? new Dictionary<string, object>(), gem));
                        }
                    }
                    object dv;
                    if (!planStep.TryGetValue("must_fail", out dv)) continue;
                    var refusal = dv as Dictionary<string, object>;
                    if (refusal == null) continue;

                    object op;
                    string operation = planStep.TryGetValue("operation", out op)
                        ? Convert.ToString(op) : "(no name)";
                    int howMany;
                    refusedOperations.TryGetValue(operation, out howMany);
                    refusedOperations[operation] = howMany + 1;

                    // ── THE TWIN (Z-C11) ─────────────────────────────────────
                    object g;
                    string twin = refusal.TryGetValue("twin", out g)
                        ? Convert.ToString(g) : null;
                    if (string.IsNullOrEmpty(twin)) withoutTwin.Add(itemName + " / " + operation);
                    else if (!names.Contains(twin))
                        absentTwins.Add(itemName + " -> «" + twin + "» does not exist");

                    // ── AND THE MESSAGE MUST BE DEMANDED ─────────────────────
                    //
                    // «must fail» without saying with which message is almost
                    // nothing: any error satisfies it, including the
                    // wrong one. The bench already demands it; here it is seen at once.
                    object m;
                    var messages = refusal.TryGetValue("message_includes", out m)
                        ? m as object[] : null;
                    if (messages == null || messages.Length == 0)
                        withoutMessage.Add(itemName + " / " + operation);
                }
            }

            // THE CEILING GOES DOWN AND DOES NOT GO UP (Z-C16). The cases that still cite them
            // are the list to look at one by one; a NEW case that cites a
            // body by number turns this check red on the day
            // it is written: `@body(name=...)` is used.
            Check("the cases that cite a body by number do not increase (Z-C16)",
                  casesWithNumbers.Count <= BodiesByNumberCap,
                  casesWithNumbers.Count + " cases (ceiling " + BodiesByNumberCap + "): " +
                  string.Join(", ", casesWithNumbers));
            Check("and the ceiling of bodies by number is the real one",
                  casesWithNumbers.Count == BodiesByNumberCap,
                  casesWithNumbers.Count == BodiesByNumberCap
                      ? "tetto " + BodiesByNumberCap
                      : "there are " + casesWithNumbers.Count + ": lower the ceiling to this number");

            Check("every step of a case measures something (as the bench demands)", muteSteps.Count == 0,
                  muteSteps.Count == 0 ? "no step without expectations" : string.Join(" | ", muteSteps));

            Check("all the case files can be read", unreadable.Count == 0,
                  unreadable.Count == 0
                      ? file.Length + " file (" + declared + " casi, " + planes + " piani)"
                      : string.Join(" | ", unreadable));

            var fake = new List<string>();
            foreach (var r in refusalsToCompare)
            {
                List<Tuple<string, string>> twinSteps;
                if (!argumentsPerCase.TryGetValue(r.Item4, out twinSteps)) continue;
                // A CASE CAN BE THE TWIN OF ITSELF (the green step and the
                // refusal in the same file): there the refusal finds itself among
                // the steps, and must be counted once more. Seen on the first run of
                // this check, on `two_sheets_and_the_same_projection_angle`.
                int equal = 0;
                foreach (var p in twinSteps)
                    if (p.Item1 == r.Item2 && p.Item2 == r.Item3) equal++;
                if (equal > (r.Item4 == r.Item1 ? 1 : 0))
                    fake.Add(r.Item1 + " -> " + r.Item4 + " (" + r.Item2 + " " + r.Item3 + ")");
            }
            // AND IT IS NOT ENOUGH THAT IT IS NOT IDENTICAL: the bench compares it with the FIRST
            // succeeded step of the same operation and demands at least one
            // argument in common with the same value (`Expectations.Resemble`,
            // the same function it uses). On 25/9/2026 the refusal on «V9»
            // had «count» in common with the SECOND step of the twin and nothing
            // with the first: bench error after a hundred minutes of running.
            var far = new List<string>();
            foreach (var r in refusalsWithTwin)
            {
                List<Tuple<string, Dictionary<string, object>, bool>> stepsG;
                if (!caseSteps.TryGetValue(r.Item4, out stepsG)) continue;   // twin absent: said below
                Dictionary<string, object> itsItems = null;
                foreach (var p in stepsG)
                    if (p.Item1 == r.Item2 && !p.Item3) { itsItems = p.Item2; break; }
                if (itsItems == null)
                {
                    far.Add(r.Item1 + " -> " + r.Item4 + ": no «" + r.Item2 + "» step that succeeds");
                    continue;
                }
                string why;
                if (!Oce.Solid.Agent.Bench.Expectations.Resemble(r.Item3, itsItems, out why))
                    far.Add(r.Item1 + " -> " + r.Item4 + ": " + why);
            }
            // The twins declared on the part: the first succeeded step of the
            // same operation in the twin, and the steps before it.
            foreach (var r in refusalsInPart)
            {
                object[] stepsG, mySteps;
                if (!rawSteps.TryGetValue(r.Item4, out stepsG) || !rawSteps.TryGetValue(r.Item1, out mySteps)) continue;
                int location = -1;
                for (int k = 0; k < stepsG.Length; k++)
                {
                    var pg = stepsG[k] as Dictionary<string, object>;
                    object opG;
                    if (pg == null || pg.ContainsKey("must_fail") || !pg.TryGetValue("operation", out opG) ||
                        Convert.ToString(opG) != r.Item2) continue;
                    location = k;
                    break;
                }
                if (location < 0)
                {
                    far.Add(r.Item1 + " -> " + r.Item4 + ": no «" + r.Item2 + "» step that succeeds");
                    continue;
                }
                object argG;
                ((Dictionary<string, object>)stepsG[location]).TryGetValue("arguments", out argG);
                string why;
                if (!Oce.Solid.Agent.Bench.Expectations.ResembleInPart(r.Item3,
                        argG as Dictionary<string, object> ?? new Dictionary<string, object>(),
                        mySteps.Take(r.Item5).ToList(), stepsG.Take(location).ToList(), r.Item6, out why))
                    far.Add(r.Item1 + " -> " + r.Item4 + ": " + why);
            }

            Check("every twin resembles its refusal as the bench wants it (F5, seen before the run)",
                  far.Count == 0,
                  far.Count == 0
                      ? refusalsWithTwin.Count + " pairs looked at with the criterion of the bench, " +
                        refusalsInPart.Count + " with the difference declared in the part"
                      : string.Join(" | ", far));

            Check("no twin is identical to its refusal (entry F5, seen before the run)",
                  fake.Count == 0,
                  fake.Count == 0 ? refusalsToCompare.Count + " refusals with a twin compared" : string.Join(" | ", fake));

            Check("every tolerance says why (the rule of the bench, seen before the run)",
                  withoutWhy.Count == 0,
                  withoutWhy.Count == 0
                      ? "no mute tolerance"
                      : string.Join(" | ", withoutWhy));

            Check("every refusal declares its twin", withoutTwin.Count == 0,
                  withoutTwin.Count == 0
                      ? refusedOperations.Values.Sum() + " refusals, all with a twin"
                      : string.Join(" | ", withoutTwin));

            Check("and the twin really exists", absentTwins.Count == 0,
                  absentTwins.Count == 0
                      ? "no cross-reference to a case that is not there"
                      : string.Join(" | ", absentTwins));

            Check("every refusal demands a message", withoutMessage.Count == 0,
                  withoutMessage.Count == 0
                      ? "no «must_fail» that accepts any error"
                      : string.Join(" | ", withoutMessage));

            // ── AND THE DESCRIPTIONS? (Z-G5) ─────────────────────────────
            var catalog = ServedCatalog();
            if (!File.Exists(catalog))
            {
                Check("the served catalog is there", false, "missing " + catalog);
                return;
            }

            var catRoot = serializer.DeserializeObject(File.ReadAllText(catalog))
                            as Dictionary<string, object>;
            object rawEntries;
            var entries = catRoot != null && catRoot.TryGetValue("operations", out rawEntries)
                ? rawEntries as object[] : null;
            if (entries == null)
            {
                Check("the catalog has the operations", false, "«operations» is not a list");
                return;
            }

            var textOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var v in entries)
            {
                var o = v as Dictionary<string, object>;
                if (o == null) continue;
                object n, d, s;
                o.TryGetValue("name", out n);
                o.TryGetValue("details", out d);
                o.TryGetValue("summary", out s);
                if (n == null) continue;
                textOf[Convert.ToString(n)] =
                    (Convert.ToString(d) + " " + Convert.ToString(s)).ToLowerInvariant();
            }

            var mute = new List<string>();
            var outsideCatalog = new List<string>();
            foreach (var op in refusedOperations.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                string text;
                if (!textOf.TryGetValue(op, out text)) { outsideCatalog.Add(op); continue; }
                bool speaks = false;
                foreach (var indicator in Telltales) if (text.Contains(indicator)) { speaks = true; break; }
                if (!speaks) mute.Add(op + " (" + refusedOperations[op] + ")");
            }

            Check("every operation with a proven refusal is in the catalog",
                  outsideCatalog.Count == 0,
                  outsideCatalog.Count == 0
                      ? refusedOperations.Count + " operations with at least one proven refusal"
                      : "outside the catalog: " + string.Join(", ", outsideCatalog));

            // THE CEILING GOES DOWN AND DOES NOT GO UP. A new guard without the sentence that
            // declares it turns this check red, and it is the moment in
            // which it is needed: afterwards, nobody comes back to it.
            Check("the mute descriptions have not increased", mute.Count <= Mute,
                  mute.Count + " mute su " + refusedOperations.Count +
                  " (tetto " + Mute + "): " + string.Join(", ", mute));

            Check("and the ceiling is the real one", mute.Count == Mute,
                  mute.Count == Mute
                      ? "zero: every operation with a proven refusal declares it"
                      : "there are " + mute.Count + ": lower the ceiling to this number");

            EveryOperationHasAGreenCase(
                new HashSet<string>(textOf.Keys, StringComparer.Ordinal), casesPerOp,
                Path.Combine(Root(), "Bench", "report", "last.json"));
        }

        /// <summary>
        /// The operations in the catalogue that today can NOT have a green case, and
        /// why. Each is rechecked: if it leaves the catalogue or finds a case
        /// that uses it, the permission must be removed — as in Z-C7, where an exception for
        /// a line that is no longer there is a hole that stays open.
        /// </summary>
        private static readonly Dictionary<string, string> WithoutGreenCase =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // (26/9/2026) `model_dimensions` is no longer here: out of process
                // it carries the dimensions (7 on the same part as Z-C1, two probes), and has its
                // true pair: `dimensions/dimensions_come_from_the_model`.
            };

        /// <summary>
        /// THE SECOND HALF OF Z-C6: `Provata = true` must point to a case that
        /// RAN, green, in the last run — not to a case that exists.
        ///
        /// On 25/9/2026, writing the cases that were missing, two «tested» operations
        /// turned out never to have worked (`trim_members`, `weld_bead`)
        /// and one left the catalogue (`gusset`). «Provata» meant that
        /// somebody had written true there. Now the bench says it: it writes the names
        /// of the green cases in `last.json` («green_cases»), and here they are crossed
        /// with the catalogue served.
        /// </summary>
        private static void EveryOperationHasAGreenCase(
            HashSet<string> inCatalog, Dictionary<string, HashSet<string>> casesPerOp, string lastJson)
        {
            if (!File.Exists(lastJson))
            {
                Check("the bench says which cases are green (Z-C6)", false, "missing " + lastJson);
                return;
            }
            var last = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(lastJson))
                         as Dictionary<string, object>;
            object rawItems;
            var list = last != null && last.TryGetValue("green_cases", out rawItems) ? rawItems as object[] : null;
            if (list == null)
            {
                Check("the bench says which cases are green (Z-C6)", false,
                      "`last.json` has no «green_cases»: the last run is from a bench that did not " +
                      "write them yet. Run the bench again.");
                return;
            }
            var green = new HashSet<string>(list.Select(x => Convert.ToString(x)), StringComparer.Ordinal);
            Check("the bench says which cases are green (Z-C6)", green.Count > 0,
                  green.Count + " green cases in the last run");

            var without = new List<string>();
            foreach (var op in inCatalog.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (WithoutGreenCase.ContainsKey(op)) continue;
                HashSet<string> itsItems;
                if (!casesPerOp.TryGetValue(op, out itsItems)) { without.Add(op + " (no case)"); continue; }
                if (!itsItems.Any(green.Contains))
                    without.Add(op + " (" + string.Join(", ", itsItems.OrderBy(x => x, StringComparer.Ordinal)) +
                              ": none green in the last run)");
            }
            Check("every operation of the catalog has a GREEN case in the last run (Z-C6)",
                  without.Count == 0,
                  without.Count == 0
                      ? (inCatalog.Count - WithoutGreenCase.Count) + " out of " + inCatalog.Count + ", plus " +
                        WithoutGreenCase.Count + " exceptions declared with the why"
                      : string.Join(" | ", without));

            var expired = new List<string>();
            foreach (var kv in WithoutGreenCase)
            {
                if (!inCatalog.Contains(kv.Key)) expired.Add(kv.Key + ": is no longer in the catalog, remove the exception");
                else if (casesPerOp.ContainsKey(kv.Key)) expired.Add(kv.Key + ": now has a case, remove the exception");
            }
            Check("and the exceptions still hold (Z-C6)", expired.Count == 0,
                  expired.Count == 0 ? string.Join(", ", WithoutGreenCase.Keys) : string.Join(" | ", expired));
        }
    }
}
