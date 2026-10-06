using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
// THE EXPECTATION EVALUATOR AND THE SELECTORS ARE IN `Expectations.cs`, which is
// also compiled by `Oce.Solid.Agent.ContractTests` — the project that runs on a machine
// WITHOUT SOLIDWORKS (entry F7). `using static` lets them be written as before:
// moving them was not supposed to cost re-reading two hundred lines of calls.
using static Oce.Solid.Agent.Bench.Expectations;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// THE CASES THAT ARE A FILE, NOT A LINE OF C# (entry F3).
    ///
    /// ── WHY IT EXISTS ────────────────────────────────────────────────────
    ///
    /// It is the project's literal request — «the expected numbers next to every
    /// step» — and it is what makes a check readable to those who do not write C#. A case today
    /// is a method: to know what it verifies one has to
    /// read the code that verifies it, and whoever draws for a living does not do that.
    ///
    /// ── THE TOLERANCES ARE ABSOLUTE, AND THEY CARRY THE WHY ───────────────
    ///
    /// In mm or mm³, never in percent, and every tolerance other than zero must
    /// say **why that one**. `Shell()` used `expectedVolume * 0.02`: on the
    /// 240,000 mm³ of the plate that is **4,800 mm³, more than a through
    /// M12 hole**. A check that accepts a hole of slack does not prove the hole.
    ///
    /// A tolerance without its `tolerance_why` **is a broken
    /// case**, not a permissive case: the file comes out red and says which field
    /// is missing. A missing field never becomes a green.
    ///
    /// ── THE SELECTORS, AND WHY THEY MUST REFUSE ──────────────────────────
    ///
    /// The identifiers of the state are POSITIONAL — faces are renumbered
    /// by area at every reading — so a case cannot write «F3». It writes
    /// what it wants (`@face(normal=front)`) and it is resolved against the state
    /// of the moment.
    ///
    /// **A selector that finds ZERO or MORE THAN ONE thing fails, and does not pick
    /// the first.** It is the silent defect that this entry names: a case that
    /// runs on a face different from the one the file name promises, and
    /// that perhaps returns the same volume anyway. The only exception is `n=` of
    /// `@feature`, where the ambiguity is resolved by whoever writes the case.
    /// </summary>
    internal static partial class Program
    {
        /// <summary>
        /// THE FOLDERS OF CASES ARE NAMED, not discovered.
        ///
        /// In `cases/` there also live the plans of the old format, which
        /// `run_plans.js` executes and which here would all be «the file is not a
        /// JSON object»: discovering the folders by themselves would mean
        /// turning somebody else's file into a bench error at every run.
        ///
        /// A new family is added here, and whoever forgets it finds out
        /// right away: its cases do not run and the count of expectations does not go up.
        /// </summary>
        private static readonly string[] Families =
        {
            "features", "pairs", "slot", "chamfer", "revolve", "plane",
            "mirror", "patterns", "axis", "removal", "sentence", "check",
            "export", "measurement", "bodies", "document_properties", "text", "frames",
            "assemblies", "drawing",
            // «weldments» (then «welds») came in on 23/9/2026 with entry Z-C6: five
            // catalog operations were exercised by NOTHING — not by a
            // case, not by C#, not by a plan — and four of the five are
            // of this family.
            //
            // THIS LIST IS EXPLICIT, and it is a lurking defect: a
            // folder of cases that is not in it is a set of files that
            // look like checks and never run. Measured the same day: the
            // other folders outside the list (`bridge`, `limits`, `views`, `parts`,
            // `shape`) contain only PLANS, which is right — but it is a fact
            // measured, not a guarantee of the mechanism.
            "weldments",
            // «extrusions» (then «extrusions») came in on 24/9/2026, still Z-C6: `extrude_profile`
            // and `extrude_section` had only hand-written plans. The contract
            // check that looks at this list called it MUTE before the
            // bench ran: it is the lurking defect of the comment above,
            // caught this time by a check and not by a reading.
            "extrusions",
            // «sheet_metal» (entry GP18) and «dimensions» (entry Z-H3), the first two families
            // born from a probe that ran the same evening: the measurement first,
            // the operation after.
            "sheet_metal", "dimensions",
            // «reanchor» (entry E19, 25/9/2026): entities that the plan names
            // with a uid read BEFORE a step that renumbers them. The cases use
            // «uid_from_step», the only way to make the bench take the
            // bridge's road.
            "reanchor",
        };

        /// <summary>
        /// The keys a case may carry (Bench/case-keys.json, W3.1-bis.2 item 4),
        /// loaded by the three runs that read cases: declared, mutations and
        /// regression.
        /// </summary>
        private static CaseKeys _caseKeys;

        /// <summary>
        /// Loads the list of case keys, or says why it cannot as a bench error
        /// and answers false: without the list no case is run, because a case
        /// that cannot be checked is not a case that passed the check.
        /// </summary>
        private static bool LoadCaseKeys()
        {
            if (_caseKeys != null) return true;
            var folder = BenchFolder();
            var path = folder == null ? CaseKeys.FileName : Path.Combine(folder, CaseKeys.FileName);
            try { _caseKeys = CaseKeys.Load(path); return true; }
            catch (Exception ex)
            {
                BenchError("case keys: the list is readable", Why(ex));
                return false;
            }
        }

        private static void TestDeclaredCases()
        {
            Console.WriteLine();
            Console.WriteLine("=== the declared cases, which are files (F3) ===");

            string casesRoot;
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var bench = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                casesRoot = Path.Combine(bench.FullName, "cases");
            }
            catch (Exception ex)
            {
                BenchError("declared cases: find the folder", Why(ex));
                return;
            }

            var file = new List<string>();
            foreach (var family in Families)
            {
                if (_familiesOnly != null && !_familiesOnly.Contains(family)) continue;
                var folder = Path.Combine(casesRoot, family);
                if (!Directory.Exists(folder))
                {
                    BenchError("declared cases: the folder «" + family + "» exists", folder);
                    return;
                }
                file.AddRange(Directory.GetFiles(folder, "*.json"));
            }
            file = file.OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal).ToList();
            if (file.Count == 0)
            {
                BenchError("declared cases: there is at least one", "no file in " + casesRoot);
                return;
            }
            if (!LoadCaseKeys()) return;

            // ── TWO RUNS, AND THE SECOND IS ENTRY F5 ──────────────────────
            //
            // First all the cases are executed and how it went is kept aside;
            // then the twins are looked at. It cannot be done in a single run,
            // because «the twin is green IN THIS RUN» is a question that
            // can be answered only when the run is over.
            var howItWent = new Dictionary<string, bool>(StringComparer.Ordinal);
            var toCheck = new List<Twinning>();
            var whereItIs = new Dictionary<string, string>(StringComparer.Ordinal);

            // ── TWO FILE SHAPES IN THE SAME FOLDER, AND THEY ARE TOLD APART ──
            //
            // `cases/drawing/` already contained the PLANS of the hand runs — the ones
            // launched with `run_plans.js` — and they are files of a different
            // shape: an ARRAY of steps, without a name, without «why» and without
            // expectations. A declared case is an OBJECT.
            //
            // The difference is STRUCTURAL, not a hint: one looks at whether the
            // root of the JSON is an array. A badly written case stays an
            // object and keeps giving a bench error as before — **the mesh is not
            // being widened**, a distinction is being drawn between two things that do not
            // look alike.
            //
            // AND HOW MANY IT SKIPS IS SAID. A file skipped in silence is like a
            // check that does not fire: the day a real case is
            // written as an array, it vanishes without anybody noticing, and
            // «all green» counts one case fewer.
            var planes = new List<string>();
            foreach (var f in file)
            {
                if (IsAPlanNotACase(f)) { planes.Add(Path.GetFileName(f)); continue; }

                // One case, one entry of the test ledger: its tests carry its
                // name, and its expectations are counted on it (W3.1-bis.2, item 7).
                using (var scope = Ledger.OpenCase(Path.GetFileNameWithoutExtension(f),
                                                   Path.GetFileName(Path.GetDirectoryName(f)), "declared"))
                {
                    // SOLIDWORKS WENT AWAY: from here on we say what
                    // was not measured, we do not pretend to have measured it
                    // (entry K17). `Skipped` counts neither as green nor as
                    // red, and it is exactly what these cases are.
                    if (Sentinel.HasGone)
                    {
                        Skipped(Path.GetFileNameWithoutExtension(f), Sentinel.Report());
                        scope.Outcome = "skipped";
                        continue;
                    }

                    whereItIs[Path.GetFileNameWithoutExtension(f)] = f;
                    bool green = RunCase(f, toCheck);
                    howItWent[Path.GetFileNameWithoutExtension(f)] = green;
                    // A bench error is not a red case (F4): nothing was measured.
                    scope.Outcome = green ? "green" : scope.HasBenchErrors ? "bench_error" : "red";
                }
            }
            Check("hand-written plans are recognised and skipped", true,
                  planes.Count == 0
                      ? "none: all the files are declared cases"
                      : planes.Count + " skipped (arrays, not cases): " +
                        string.Join(", ", planes.Take(4)) + (planes.Count > 4 ? " ..." : ""));

            LookAtTwins(toCheck, howItWent, whereItIs);

            // WHICH CASES ARE GREEN, BY NAME (entry Z-C6, the second half).
            //
            // `last.json` carried the totals and nothing else: «972 tests, 0 failed» does not
            // say WHICH cases ran, and so nobody could check
            // that every `Tested = true` pointed to a case that really runs. On
            // 25/9/2026, while writing the missing cases, two catalog operations
            // turned out never to have worked. This line is collected by `bench.ps1`
            // into `green_cases`, and the contract suite crosses it with the catalog.
            //
            // It comes out BEFORE the ==== line, and does not start with a space: it is not
            // an outcome line, and must not be counted as one.
            Console.WriteLine("green cases: " + string.Join(" ",
                howItWent.Where(kv => kv.Value).Select(kv => kv.Key)
                           .OrderBy(k => k, StringComparer.Ordinal)));
        }

        /// <summary>
        /// A refusal and the check that accompanies it (entry F5).
        /// </summary>
        private sealed class Twinning
        {
            public string Label;     // «lim_… · step 2»
            public string Case;          // whoever names it
            public string Twin;       // who must succeed
            public string Operation;    // the step that refused
            public Dictionary<string, object> Arguments;   // as they were written in the file
            public List<object> PartBefore;               // the steps before the refusal: the part
            public string DifferenceInPart;             // «difference_is_the_part», o null
        }

        /// <summary>
        /// CHECKS IN PAIRS, IMPOSED BY THE BENCH (entry F5).
        ///
        /// ── WHY ───────────────────────────────────────────────────────────
        ///
        /// **A check that verifies only the refusal does not tell «the guard
        /// works» from «the guard refuses everything».** Case 1 of 80u — the
        /// shell of 30 on a box 52 tall, which seemed impossible and was
        /// only ugly — says what happens without it: an
        /// oversight is turned into a rule of the product.
        ///
        /// Three things, and all three are bench errors when they do not hold,
        /// because they concern the case and not the part:
        ///
        ///   1. the twin **is named**;
        ///   2. the twin **is green in this run** — that it exists is not enough:
        ///      a red twin is a pair that proves nothing;
        ///   3. the twin **has something to do** with the refusal: same
        ///      operation, arguments not identical, and at least one argument in
        ///      common with the same value.
        /// </summary>
        private static void LookAtTwins(
            List<Twinning> pairs, Dictionary<string, bool> howItWent,
            Dictionary<string, string> whereItIs)
        {
            Console.WriteLine();
            Console.WriteLine("=== the twins: a refusal is never alone (F5) ===");

            if (pairs.Count == 0)
            {
                Check("twins: there is at least one to check", false,
                      "no case with «must_fail»");
                return;
            }

            int green = 0;
            foreach (var c in pairs)
            using (Ledger.About(c.Case))
            {
                bool wentWell;
                if (!howItWent.TryGetValue(c.Twin, out wentWell))
                {
                    BenchError(c.Label, "the twin «" + c.Twin + "» is not among the cases run");
                    continue;
                }
                if (!wentWell)
                {
                    // A RED TWIN IS A PAIR THAT PROVES NOTHING: the
                    // refusal is left without its counterpart, and nobody knows any more
                    // whether the guard works or refuses everything.
                    BenchError(c.Label, "the twin «" + c.Twin + "» is not green in this run");
                    continue;
                }

                string why;
                if (!Resemble(c, whereItIs, out why))
                {
                    BenchError(c.Label, "fake twin: " + why);
                    continue;
                }

                green++;
                Check("twin of «" + c.Case + "»", true, c.Twin + " — " + why);
            }

            Check("twins " + green + "/" + pairs.Count + " green",
                  green == pairs.Count, "");
        }

        /// <summary>
        /// Does the twin have to do with the refusal?
        ///
        /// ── THE RULE WRITTEN IN THE ENTRY DOES NOT HOLD UP AGAINST ITS OWN PAIRS ──
        ///
        /// Entry F5 proposed «the twin must differ by ONE argument
        /// only». Measured on the four canonical pairs of 80v, that
        /// rule **refuses three out of four**: the hole outside the material
        /// differs by two coordinates, the slot by length and width. It is
        /// a rule that would have thrown away the very examples that the entry
        /// itself gives as canonical.
        ///
        /// The one that runs says the same thing without the fixed number: **same
        /// operation, arguments not identical, and at least one argument in common
        /// with the same value.** How many differ is SAID, so whoever
        /// reads sees a loose pair without the bench having to decide in
        /// their place.
        /// </summary>
        private static bool Resemble(
            Twinning c, Dictionary<string, string> whereItIs, out string why)
        {
            why = "";
            string filePath;
            if (!whereItIs.TryGetValue(c.Twin, out filePath))
            { why = "cannot find the twin's file"; return false; }

            Dictionary<string, object> twin;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                twin = s.DeserializeObject(File.ReadAllText(filePath)) as Dictionary<string, object>;
            }
            catch (Exception ex) { why = "the twin's JSON is unreadable: " + ex.Message; return false; }

            var steps = Items(twin, "steps");
            if (steps == null) { why = "the twin has no steps"; return false; }

            Dictionary<string, object> itsItems = null;
            int whereItsOwn = -1;
            for (int k = 0; k < steps.Count; k++)
            {
                var planStep = steps[k] as Dictionary<string, object>;
                if (planStep == null) continue;
                if (!string.Equals(Text(planStep, "operation"), c.Operation, StringComparison.Ordinal)) continue;
                if (planStep.ContainsKey("must_fail")) continue;   // two refusals are not a pair
                itsItems = (planStep.ContainsKey("arguments") ? planStep["arguments"] : null)
                       as Dictionary<string, object>;
                whereItsOwn = k;
                break;
            }
            if (itsItems == null)
            {
                why = "the twin has no step «" + c.Operation + "» that succeeds";
                return false;
            }

            // THE REAL COMPARISON IS IN `Expectations`, which is a pure function on
            // two dictionaries and the check `Oce.Solid.Agent.ContractTests` without CAD. Here
            // only the work that touches the CAD remains: finding the file and reading it.
            // `Expectations.` BY FORCE: the method next to it has the same name, and
            // without the prefix the compiler chooses that one — that is, itself,
            // forever.
            //
            // THE DIFFERENCE DECLARED IN THE PART (28/9/2026): same arguments,
            // different steps before. It is checked in `Expectations.ResembleInPart`,
            // and a fake argument is not written to make the rule pass.
            if (c.DifferenceInPart != null)
                return Expectations.ResembleInPart(c.Arguments, itsItems, c.PartBefore,
                                                   steps.Take(whereItsOwn).ToList(), c.DifferenceInPart, out why);
            return Expectations.Resemble(c.Arguments, itsItems, out why);
        }


        // ==================================================================
        // THE RUNNER'S REDS
        //
        // THE CASES ARE FILES, BUT WHOEVER READS THEM IS C#, and its refusals are checked
        // here: inside a case they cannot be checked, because a selector that
        // refuses turns the case red — and that is right.
        //
        // Everything that follows is a pure function on a dictionary: it does not
        // open SOLIDWORKS, does not create documents, takes a thousandth of a second.
        // ==================================================================
        private static void TestThatSelectorRefuses()
        {
            Console.WriteLine();
            Console.WriteLine("=== the case selectors: they refuse instead of guessing (F3) ===");

            Func<string, string, Dictionary<string, object>> face =
                (uid, facing) => new Dictionary<string, object>
                    { ["uid"] = uid, ["facing"] = facing, ["area_mm2"] = 6000.0 };

            // ── ONLY ONE: it is taken ──────────────────────────────────────
            var onlyOne = new Dictionary<string, object>
            {
                ["faces"] = new List<object> { face("F1", "front"), face("F2", "back") },
            };
            Check("F3: a single face, it is taken",
                  Test(() => Select("@face(normal=front)", onlyOne)) == "F1", "");

            // ── ZERO: it is refused ────────────────────────────────────────
            Check("F3 RED: no face «up», it refuses",
                  Why(() => Select("@face(normal=up)", onlyOne)).Contains("I did not find it"),
                  Why(() => Select("@face(normal=up)", onlyOne)));

            // ── TWO: it is refused, the first is NOT taken ─────────────────
            //
            // It is the silent defect that entry F3 names: a case that runs
            // on a face different from the one the file name promises, and
            // that perhaps returns the same volume. Without this red, «the case
            // is green» would not mean «it worked where it said».
            var twoEqual = new Dictionary<string, object>
            {
                ["faces"] = new List<object> { face("F1", "front"), face("F7", "front") },
            };
            Check("F3 RED: two faces «front», it refuses instead of guessing",
                  Why(() => Select("@face(normal=front)", twoEqual)).Contains("I found 2"),
                  Why(() => Select("@face(normal=front)", twoEqual)));

            // ── THE EDGE, by axis and length ───────────────────────────────
            var edges = new Dictionary<string, object>
            {
                ["edges"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "E1", ["along"] = "Z", ["length_mm"] = 40.0 },
                    new Dictionary<string, object> { ["uid"] = "E2", ["along"] = "Z", ["length_mm"] = 100.0 },
                    new Dictionary<string, object> { ["uid"] = "E3", ["along"] = "X", ["length_mm"] = 100.0 },
                },
            };
            Check("F3: the edge is chosen by axis and length",
                  Test(() => Select("@edge(axis=Z,length~=100)", edges)) == "E2", "");
            Check("F3 RED: without the length, two edges on Z are ambiguous",
                  Why(() => Select("@edge(axis=Z)", edges)).Contains("I found 2"),
                  "");

            // ── THE NTH FEATURE: the order is decided by whoever writes the case ──
            var feature = new Dictionary<string, object>
            {
                ["feature"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "Hole1", ["kind"] = "HoleWzd" },
                    new Dictionary<string, object> { ["uid"] = "Hole2", ["kind"] = "HoleWzd" },
                    new Dictionary<string, object> { ["uid"] = "Extrusion1", ["kind"] = "Extrusion" },
                },
            };
            Check("F3: @feature(n=2) takes the second",
                  Test(() => Select("@feature(kind=HoleWzd,n=2)", feature)) == "Hole2", "");
            Check("F3 RED: @feature(n=3) out of two, it refuses",
                  Why(() => Select("@feature(kind=HoleWzd,n=3)", feature)).Contains("I found 2"),
                  "");

            // ── THE DIMENSION: the document title is put in by the bench ────
            //
            // `plans/change_diameter.json` writes «D1@Sketch1@Part76.Part», and
            // that «Part76» changes at every run: that plan cannot be
            // re-run. The case names the dimension, the title is resolved here.
            var conQuote = new Dictionary<string, object>
            {
                ["feature"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["uid"] = "Extrusion1",
                        ["dimensions"] = new List<object>
                        {
                            new Dictionary<string, object> { ["name"] = "D1@Sketch1@Part99.Part" },
                            new Dictionary<string, object> { ["name"] = "D2@Sketch1@Part99.Part" },
                        },
                    },
                },
            };
            Check("F3: @dimension returns the name with the document of NOW",
                  Test(() => Select("@dimension(feature=Sketch1,name=D1)", conQuote))
                      == "D1@Sketch1@Part99.Part", "");

            // ── THE BODY, BY NAME: the parentheses of the name break nothing ─
            //
            // The numbers C1, C2... of the state do not follow the order of creation
            // and change from one run to the next (measured on 25/9/2026); the name
            // does not. Here the state is written backwards on purpose: C1 is the bar
            // (2). A selector that returned «the second body» would get it wrong.
            var bodies = new Dictionary<string, object>
            {
                ["bodies"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "C1", ["name"] = "TUBE 80X80X5(2)" },
                    new Dictionary<string, object> { ["uid"] = "C2", ["name"] = "TUBE 80X80X5(1)" },
                },
            };
            Check("F3: @body(name=...) takes the body by name, parentheses included",
                  Test(() => Select("@body(name=TUBE 80X80X5(2))", bodies)) == "C1",
                  Test(() => Select("@body(name=TUBE 80X80X5(2))", bodies)));
            Check("F3 RED: @body with a name that does not exist, it refuses",
                  Why(() => Select("@body(name=TUBE 80X80X5(3))", bodies)).Contains("I did not find it"),
                  "");

            // ── A TOLERANCE WITHOUT THE WHY IS A BROKEN CASE ────────────────
            //
            // Not a permissive case: broken. A missing field never becomes
            // a green.
            var withoutWhy = new Dictionary<string, object>
            {
                ["what"] = "volume_mm3", ["equal_to"] = 100.0, ["tolerance"] = 5.0,
            };
            string said;
            string reason = Why(() => { string d; Matches(withoutWhy, null, null, null, out d); return d; });
            Check("F3 RED: tolerance without «tolerance_why»",
                  reason.Contains("tolerance_why"), Short(reason));

            // And with the why, the same expectation is evaluated (here it fails on the
            // value, not on the format: the point is that it GETS there).
            var withWhy = new Dictionary<string, object>
            {
                ["what"] = "measurements.x", ["equal_to"] = 100.0, ["tolerance"] = 5.0,
                ["tolerance_why"] = "measured with a ruler",
            };
            var fakeMeasurements = new Dictionary<string, object> { ["x"] = 102.0 };
            bool evaluated = false;
            try { evaluated = Matches(withWhy, null, null, fakeMeasurements, out said); }
            catch (Exception ex) { said = ex.Message; }
            Check("F3: with the why, the tolerance applies", evaluated, Short(said));
        }

        // ==================================================================
        // F4. a field that is not there is not a zero, and it is exit 2
        // ==================================================================
        /// <summary>
        /// THE TRAP, IN ITS LITERAL FORM.
        ///
        /// A check that verified a defect stops verifying it when the
        /// defect is closed elsewhere, and **stays green**. In ninety percent
        /// of the cases the way it stops is that the field it read was
        /// renamed: a dictionary that does not find the key returns
        /// `null`, and `null` compared with `null` passes.
        ///
        /// Here a missing field raises, the message names it **and names the fields
        /// that were there** — the same form as the message on nonexistent
        /// dimensions — and whoever calls turns it into a BENCH ERROR, which
        /// is not a red check and takes the run to **exit 2**.
        /// </summary>
        private static void TestThatAnAbsentFieldIsNotAZero()
        {
            Console.WriteLine();
            Console.WriteLine("=== a field that does not exist is not a zero (F4) ===");

            // The measurements that `apply_material` really reports. There is no
            // «material_removed_mm3»: that operation removes nothing.
            var measurements = new Dictionary<string, object>
            {
                ["material"] = "1.0037 (S235JR)",
                ["library"] = "solidworks materials",
                ["class_name"] = "Acciaio",
            };

            string said = Why(() =>
            {
                string d;
                Matches(new Dictionary<string, object>
                { ["what"] = "measurements.material_removed_mm3", ["equal_to"] = 0.0 },
                         null, null, measurements, out d);
                return d;
            });
            Check("F4 RED: missing field, and it says which ones there were",
                  said.Contains("missing field: measurements.material_removed_mm3") &&
                  said.Contains("there are: class_name, library, material"),
                  Short(said));

            // ── AND A DECLARED NULL IS NOT A MISSING FIELD ─────────────────
            //
            // The state writes `material: null` ON PURPOSE, to say «that place
            // exists and is empty»: it is the only way for the model to notice
            // that it has to fill it. Confusing it with a field that is missing
            // would remove that very distinction.
            var stateWithoutMaterial = new Dictionary<string, object> { ["material"] = null };
            string d2;
            bool ok = false;
            try
            {
                ok = Matches(new Dictionary<string, object>
                { ["what"] = "state.material", ["equal_to"] = "" },
                              null, stateWithoutMaterial, null, out d2);
            }
            catch (Exception ex) { d2 = ex.Message; }
            Check("F4: «material: null» is measured, it is not a missing field", ok, Short(d2));

            // ── THE COUNT OF THE EXPECTATIONS ──────────────────────────────
            //
            // Attempted and verified: if they diverge, something is measuring
            // nothing. Here we only look that the count is there and is not stuck at
            // zero — the true number is written by the run's summary.
            Check("F4: the expectations are counted", _expectations > 0 && _verifiedExpectations > 0,
                  _verifiedExpectations + " of " + _expectations);
        }

        /// <summary>The result, or null if it raised.</summary>
        private static string Test(Func<string> f)
        {
            try { return f(); } catch { return null; }
        }

        /// <summary>The exception message, or «did not refuse».</summary>
        private static string Why(Func<string> f)
        {
            try { f(); return "DID NOT REFUSE"; }
            catch (Exception ex) { return ex.Message; }
        }

        // ==================================================================
        // --mutate: EVERY CASE MUST BE ABLE TO TURN RED (entry F6)
        // ==================================================================
        /// <summary>
        /// PERTURBS THE PLAN, NOT THE CODE.
        ///
        /// For each case, one numeric argument at a time is multiplied by
        /// 1.5 (booleans inverted), the case is re-run and it is **expected to
        /// turn RED**. A case that stays green under every perturbation is
        /// **blind**: its expectations do not constrain its arguments.
        ///
        /// ── WHY THIS AND NOT A HAND-WRITTEN FIELD ─────────────────────────
        ///
        /// It is **the only mechanical way** of imposing that a check has already
        /// failed at least once. A hand-written «proven to fail» flag is
        /// a declaration, and a declaration is copied from the case next to it;
        /// a mutation is not. And it costs little precisely here, because the plan is
        /// given: mutating a JSON is trivial, mutating the C# of SOLIDWORKS is not.
        ///
        /// ── THE FALSE BLIND, AND HOW IT IS AVOIDED ────────────────────────
        ///
        /// Mutating an argument that the operation **ignores** — `depth_mm`
        /// on a through cut — rightly stays green and is not blindness.
        /// Those mutations are skipped by **reading the catalog**: the schema of
        /// `depth_mm` says «only if not through», and that line is read
        /// instead of being copied here.
        ///
        /// ── AND ONLY THE STEPS WITH EXPECTATIONS ARE MUTATED ──────────────
        ///
        /// A step with `must_fail` has no expectations to constrain: a hole of
        /// 500 mutated to 750 covers the part all the same, and the case would stay
        /// green without this saying anything about its expectations.
        /// </summary>
        /// <remarks>
        /// IT IS NOT CALLED `Test…`, AND IT IS NOT A WHIM. `TestThatTheTableCoversEverything`
        /// (entry F13) demands that every `Test…()` method be in the table of the
        /// groups, and this is not a group: it is **another run**, which replaces
        /// the groups. Calling it `TestMutations` the check came out
        /// red — rightly — on the first full run after it was written.
        /// </remarks>
        private static void MutationsRun()
        {
            Console.WriteLine();
            Console.WriteLine("=== --mutate: every case must be able to turn red (F6) ===");

            var file = CasesFile();
            if (file == null) return;
            if (!LoadCaseKeys()) return;
            var schemas = CatalogSchemas();

            var lines = new List<string>();
            int blindOnes = 0;

            foreach (var filePath in file)
            {
                string itemName = Path.GetFileNameWithoutExtension(filePath);
                Dictionary<string, object> testCase;
                try
                {
                    var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                    testCase = s.DeserializeObject(File.ReadAllText(filePath)) as Dictionary<string, object>;
                }
                catch (Exception ex) { BenchError(itemName, "unreadable JSON: " + Why(ex)); continue; }
                // The same refusal as the normal run: a mutation of a case the
                // bench would not run proves nothing (W3.1-bis.2, item 4).
                var unknown = testCase == null ? null : _caseKeys.Unknown(testCase, _caseKeys.DeclaredRoot);
                if (unknown != null && unknown.Count > 0) { BenchError(itemName, CaseKeys.Message(unknown)); continue; }

                var mutations = Mutations(testCase, schemas);
                if (mutations.Count == 0)
                {
                    blindOnes++;
                    lines.Add("| `" + itemName + "` | 0 | 0 | **NO MUTATION POSSIBLE**: " +
                              "no numeric or boolean argument in a step with expectations |");
                    Check("mutate: " + itemName, false, "no mutation possible");
                    continue;
                }

                int red = 0;
                var survived = new List<string>();
                foreach (var m in mutations)
                {
                    bool turnedRed = TestMutation(m.Case);
                    if (turnedRed) red++;
                    else survived.Add(m.How);
                }

                bool blind = red == 0;
                if (blind) blindOnes++;
                lines.Add("| `" + itemName + "` | " + mutations.Count + " | " + red + " | " +
                          (blind ? "**BLIND**: the expectations do not constrain the arguments"
                                 : (survived.Count == 0
                                        ? "all red"
                                        : "still green: " + string.Join(", ", survived))) +
                          " |");

                // A CASE WITH SOME SURVIVING MUTATION IS NOT RED: it is
                // a case whose expectations leave some arguments free, and the
                // report line says so by name. Only the BLIND one is red.
                Check("mutate: " + itemName, !blind,
                      mutations.Count + " mutations, " + red + " red" +
                      (survived.Count > 0 ? " — still green: " + string.Join(", ", survived) : ""));
            }

            WriteMutations(lines, blindOnes);
            Check("mutate: blind cases", blindOnes == 0, blindOnes + " blind");
        }

        private sealed class Mutation
        {
            public string How;                            // «step 1 · thickness_mm 40 -> 60»
            public Dictionary<string, object> Case;       // the whole case, already mutated
        }

        /// <summary>
        /// All the mutations of a case: one argument at a time, and the whole
        /// case rebuilt around it. It works on a deep COPY, because
        /// two mutations that add up no longer prove anything precise.
        /// </summary>
        private static List<Mutation> Mutations(
            Dictionary<string, object> testCase, Dictionary<string, Dictionary<string, string>> schemas)
        {
            var outside = new List<Mutation>();
            var steps = Items(testCase, "steps");
            if (steps == null) return outside;

            for (int i = 0; i < steps.Count; i++)
            {
                var planStep = steps[i] as Dictionary<string, object>;
                if (planStep == null) continue;
                if (planStep.ContainsKey("must_fail")) continue;
                var expectations = Items(planStep, "expect");
                if (expectations == null || expectations.Count == 0) continue;

                var arguments = (planStep.ContainsKey("arguments") ? planStep["arguments"] : null)
                                as Dictionary<string, object>;
                if (arguments == null) continue;
                string operation = Text(planStep, "operation") ?? "";

                foreach (var key in arguments.Keys.ToList())
                {
                    object val = arguments[key];
                    if (val == null) continue;
                    if (val is string) continue;          // selectors and plan names
                    if (Ignored(operation, key, arguments, schemas)) continue;

                    object changed;
                    string how;
                    if (val is bool)
                    {
                        bool b = (bool)val;
                        changed = !b;
                        how = "step " + (i + 1) + " · " + key + " " + b + "->" + (!b);
                    }
                    else
                    {
                        double d;
                        if (!double.TryParse(Convert.ToString(val, CultureInfo.InvariantCulture),
                                             NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                            continue;
                        // ZERO TIMES 1.5 IS ZERO: a mutation that mutates
                        // nothing would stay green and would be read as blindness.
                        // It is moved by one millimeter, which on a coordinate is
                        // a real mutation.
                        double n = Math.Abs(d) < 1e-9 ? 1.0 : d * 1.5;
                        changed = n;
                        how = "step " + (i + 1) + " · " + key + " " +
                               d.ToString("0.##", CultureInfo.InvariantCulture) + "->" +
                               n.ToString("0.##", CultureInfo.InvariantCulture);
                    }

                    var copy = Copy(testCase) as Dictionary<string, object>;
                    var itsSteps = Items(copy, "steps");
                    var itsStep = itsSteps[i] as Dictionary<string, object>;
                    ((Dictionary<string, object>)itsStep["arguments"])[key] = changed;
                    outside.Add(new Mutation { How = how, Case = copy });
                }
            }
            return outside;
        }

        /// <summary>
        /// Does the operation ignore that argument, here and now? It is read from the
        /// CATALOG: a description that begins with «only if not X» says that
        /// the field counts only when `X` is false, and «only if X» the opposite.
        /// Mutating an ignored field leaves the case green, and that green
        /// would look like blindness.
        /// </summary>
        private static bool Ignored(
            string operation, string key, Dictionary<string, object> arguments,
            Dictionary<string, Dictionary<string, string>> schemas)
        {
            Dictionary<string, string> itsItems;
            if (schemas == null || !schemas.TryGetValue(operation, out itsItems)) return false;
            string descriptionText;
            if (!itsItems.TryGetValue(key, out descriptionText) || descriptionText == null) return false;

            const string prefix = "only if ";
            if (!descriptionText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string rest = descriptionText.Substring(prefix.Length).Trim();
            bool denied = rest.StartsWith("not ", StringComparison.OrdinalIgnoreCase);
            if (denied) rest = rest.Substring(4).Trim();
            // Only a field name: «only if not through». If the condition is
            // a sentence, it is not interpreted — better one mutation too many than
            // one skipped because of an assumption about the language.
            if (rest.Contains(" ")) return false;

            object v;
            bool isOn = arguments.TryGetValue(rest, out v) && v != null &&
                          Convert.ToString(v, CultureInfo.InvariantCulture)
                                 .Equals("True", StringComparison.OrdinalIgnoreCase);
            return denied ? isOn : !isOn;
        }

        /// <summary>Runs a mutated case in silence. True if it turned red.</summary>
        private static bool TestMutation(Dictionary<string, object> testCase)
        {
            // THE MUTATION TOO GOES THROUGH `PrepareDocument`: if the case is
            // an assembly, mutating it on an empty part would make it fail for the
            // wrong reason — and a mutation that turns red because the
            // document was not the right one **does not prove that the case guards
            // its defect**. It would say yes while lying, which is worse than
            // silence.
            Preparation prepared = null;
            _quiet = true;
            _quietRed = false;
            try
            {
                prepared = PrepareDocument(Text(testCase, "name") ?? "?", testCase);
                if (prepared == null || prepared.Document == null) return false;
                RunSteps(Text(testCase, "name") ?? "?", testCase, prepared.Document, null, null);
            }
            catch { _quietRed = true; }
            finally { ThrowAwayPreparation(prepared); _quiet = false; }
            return _quietRed;
        }

        private static object Copy(object x)
        {
            var map = x as Dictionary<string, object>;
            if (map != null)
            {
                var outside = new Dictionary<string, object>();
                foreach (var v in map) outside[v.Key] = Copy(v.Value);
                return outside;
            }
            var itemList = x as System.Collections.IList;
            if (itemList != null)
            {
                var outside = new List<object>();
                foreach (var v in itemList) outside.Add(Copy(v));
                return outside;
            }
            return x;
        }

        /// <summary>
        /// The catalog schemas: operation -> field -> description.
        ///
        /// They are read from the LOADED LIBRARY, not from `bin/catalog.json`: that
        /// file is written by `build.ps1`, and reading it would mean describing a
        /// library with the words of another build.
        /// </summary>
        private static Dictionary<string, Dictionary<string, string>> CatalogSchemas()
        {
            try
            {
                // Every lookup below goes through the reflection log: they fall
                // back to null, and a renamed target would give fewer mutations
                // in silence (W3.1-bis.2, item 3).
                var tOperations = ReflectionLog.Global.TypeIn(_addin, "Oce.Solid.Agent.Core.Catalog.Operations");
                var tNode = ReflectionLog.Global.TypeIn(_addin, "Oce.Solid.Agent.Core.Catalog.Node");
                if (tOperations == null || tNode == null) return null;
                var all = ReflectionLog.Global.Field(tOperations, "All",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    as System.Collections.IEnumerable;
                var json = ReflectionLog.Global.Method(tNode, "Json", BindingFlags.Public | BindingFlags.Static);
                if (all == null || json == null) return null;

                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                var outside = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                foreach (var o in all)
                {
                    var typ = o.GetType();
                    string itemName = Convert.ToString(ReflectionLog.Global.Field(typ, "Name")?.GetValue(o));
                    var schema = ReflectionLog.Global.Field(typ, "Schema")?.GetValue(o);
                    if (itemName == null || schema == null) continue;
                    var text = json.Invoke(null, new[] { schema }) as string;
                    if (text == null) continue;
                    var tree = s.DeserializeObject(text) as Dictionary<string, object>;
                    var property = (tree != null && tree.ContainsKey("properties")
                        ? tree["properties"] : null) as Dictionary<string, object>;
                    if (property == null) continue;

                    var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var p in property)
                    {
                        var rule = p.Value as Dictionary<string, object>;
                        if (rule != null && rule.ContainsKey("description"))
                            fields[p.Key] = Convert.ToString(rule["description"]);
                    }
                    outside[itemName] = fields;
                }
                return outside;
            }
            catch { return null; }
        }

        private static void WriteMutations(List<string> lines, int blindOnes)
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var bench = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                var folder = Path.Combine(bench.FullName, "report");
                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, "mutations.md");

                var text = new List<string>
                {
                    "# Mutations — every case must be able to turn red (F6)",
                    "",
                    "One argument at a time multiplied by 1.5 (booleans inverted),",
                    "the case run again, and it is EXPECTED to turn red. A case that stays",
                    "green under every perturbation is **blind**: its expectations do not constrain",
                    "its arguments.",
                    "",
                    "Mutations on arguments the operation ignores — `depth_mm` on a",
                    "through-all cut — are skipped by reading the catalog, not guessed.",
                    "",
                    "| case | mutations | red | |",
                    "|---|---|---|---|",
                };
                text.AddRange(lines);
                text.Add("");
                text.Add(blindOnes == 0
                    ? "**No blind case.**"
                    : "**" + blindOnes + " blind cases.** A blind case is green and proves nothing.");
                File.WriteAllText(file, string.Join("\r\n", text) + "\r\n",
                                  new System.Text.UTF8Encoding(false));
                Console.WriteLine("mutations: " + file);
            }
            catch (Exception ex) { Console.WriteLine("could not write mutations.md: " + ex.Message); }
        }

        /// <summary>
        /// THE MEASUREMENTS TAKEN FROM THE DOCUMENT AND NOT FROM THE STATE, in one
        /// place only. It is the bridge between the evaluator — which knows nothing
        /// about SOLIDWORKS — and the open document.
        ///
        /// A list and not a chain of `if` for a measured reason: the list
        /// of names lived in `Expectations.Measure`, far from whoever really measures, and
        /// two new measurements added here and forgotten there came out as
        /// «I do not know how to measure «last_plane_mm»: the names are …» with the old
        /// list next to it — a message that said something false about itself.
        /// Now the names are the keys, and the message reads them.
        ///
        /// ── WHY IT IS LAZY AND NOT A FIELD WITH THE INITIALIZER ───────────
        ///
        /// Because written as `static readonly ... = new Dictionary...` THE BENCH
        /// NO LONGER STARTS:
        ///
        ///     Unhandled exception: System.TypeInitializationException
        ///     ---> System.IO.FileNotFoundException: SolidWorks.Interop.sldworks
        ///
        /// A static field initializer ends up in the class constructor of
        /// `Program`, which runs BEFORE the first line of `Main` — and
        /// therefore before `Main` installs the `AssemblyResolve` that knows where
        /// the interops are. The lambdas name `IModelDoc2`, the loading
        /// starts too early, and the thing does not look at all like its
        /// cause: the executable dies before printing a line, and the bench
        /// launched from `bench.ps1` exits with a `NativeCommandError` that
        /// looks like a PowerShell mishap.
        ///
        /// The field without an initializer does not enter the class constructor:
        /// the table is born at the first measurement, with the resolver already up.
        /// </summary>
        private static Dictionary<string,
            Func<IModelDoc2, Dictionary<string, object>, object>> _documentMeasurements;

        private static Dictionary<string,
            Func<IModelDoc2, Dictionary<string, object>, object>> DocumentMeasurements
        {
            get { return _documentMeasurements ?? (_documentMeasurements = BuildDocumentMeasurements()); }
        }

        private static Dictionary<string,
            Func<IModelDoc2, Dictionary<string, object>, object>> BuildDocumentMeasurements()
        {
            return new Dictionary<string, Func<IModelDoc2, Dictionary<string, object>, object>>
        {
            { "volume_mm3",  (doc, docState) => DocumentVolume(doc) },
            { "bodies",       (doc, docState) => CountBodies(doc) },
            { "bounding_box_mm", (doc, docState) => DocumentBoundingBox(doc) },

            // ── WHERE THE HOLES ARE, NOT HOW MUCH MATERIAL IS MISSING ────
            //
            // It is the invariant of entry F8: **the position of the cylinder, never
            // the volume alone**. A case that «holds» because the volume adds up,
            // while the hole ended up on the other side, is the defect that
            // that suite exists to measure.
            //
            // Sorted, because the order in which SOLIDWORKS lists the faces is
            // not something a case can count on.
            { "centers_x_mm", (doc, docState) => CylinderCenters(doc, 0) },
            { "centers_y_mm", (doc, docState) => CylinderCenters(doc, 1) },
            { "centers_z_mm", (doc, docState) => CylinderCenters(doc, 2) },
            { "diameters_mm", (doc, docState) => CylindersDiameters(doc) },

            // ── AND FOR WHAT LEAVES NO CYLINDERS ─────────────────────────
            //
            // A rectangular pocket has no center that can be read
            // from the faces, but it **moves the part's center of mass**, and by how much
            // is computed by hand. It is the position invariant for everything
            // that is not a hole: the volume alone does not tell a pocket that stays put
            // from a pocket that followed the edge.
            { "center_of_mass_mm", (doc, docState) => CenterOfMass(doc) },

            // ── WHERE THE LAST PLANE CREATED ENDED UP ────────────────────
            //
            // `create_plane` answers with the distance ASKED, not with the one
            // obtained: an expectation on `measurements.distance_mm` would verify that the
            // bridge can copy a number. The sign is seen only by measuring
            // the plane, and it is the defect that «looks right» — the add-in passes
            // the absolute value plus `OptionFlip`, but macros circulate that
            // pass the sign, and with those the plane at -100 ends up at +100
            // without anything failing.
            { "last_plane_mm", (doc, docState) => LastPlaneTranslation(doc) },

            // How many curved edges the part has. It serves the expectation «no
            // curved edge is offered to the model»: the edge of a hole is a
            // circle, and as the direction of a pattern it is refused.
            { "edges_curved", (doc, docState) => CurvedEdges(docState) },

            // How many reference axes there are. `create_axis` does not return a
            // feature — it returns a boolean — and the only proof that the axis really
            // exists is that the state lists it at the next step.
            { "axes_created", (doc, docState) => HowManyInList(docState, "axes") },

            // How many features the tree has. It serves `delete_feature`:
            // the volume says the material is back, this says the
            // feature is no longer there. They are two different things — a
            // suppressed feature leaves the volume as it is and stays in the list.
            { "how_many_features", (doc, docState) => HowManyFeatures(docState) },

            // HOW MANY CONFIGURATION NAMES THE PART DECLARES.
            //
            // THE NAMES CANNOT BE WRITTEN IN A CASE. The default one is
            // called «Default<A macchina>»: the part between angle brackets is
            // the display state and it is localized, so a case
            // that wrote it would be green on this installation and red
            // on the next. The COUNT instead is a fact: if
            // `create_configuration` created nothing it would stay what it was
            // before while the operation answers that it went well.
            //
            // «NAMES» AND NOT «CONFIGURATIONS», and the distinction cost a
            // red: a part just born from this template declares TWO names
            // -- «Default<A macchina>» and «Default<Saldato>» -- and a case that
            // took for granted «a new part, one configuration» found
            // three where it expected two. The true count, the one that
            // `GetConfigurationCount()` answers, is the measurement below.
            { "how_many_configuration_names", (doc, docState) => HowManyInList(docState, "configurations") },

            // HOW MANY SOLIDWORKS COUNTS, which is another call and may
            // answer another number. It is in the state, read from
            // `GetConfigurationCount()`.
            { "how_many_configurations", (doc, docState) => FromState(docState, "how_many_configurations") },
        };
        }

        /// <summary>
        /// A field of the state taken as it is. Null when it is not there — and whoever
        /// calls turns it into a red, because a missing field is not
        /// a zero.
        /// </summary>
        private static object FromState(Dictionary<string, object> docState, string theField)
        {
            return docState != null && docState.ContainsKey(theField) ? docState[theField] : null;
        }

        /// <summary>How many entries a family of the state has.</summary>
        private static int HowManyInList(Dictionary<string, object> docState, string family)
        {
            var entries = (docState != null && docState.ContainsKey(family)
                ? docState[family] : null) as System.Collections.IList;
            return entries == null ? 0 : entries.Count;
        }

        /// <summary>
        /// A measurement of the document, or a refusal that says which ones exist.
        ///
        /// It used to return `null` for an unknown name, and a `null` compared with
        /// a number is a RED: the check failed saying «expected 100, found
        /// (nothing)», which looks like a defect of the bridge and is not. A name
        /// that does not exist is a bench error, and must be said as such.
        /// </summary>
        private static object FromDocument(string what, IModelDoc2 doc,
                                           Dictionary<string, object> docState)
        {
            Func<IModelDoc2, Dictionary<string, object>, object> measure;
            if (!DocumentMeasurements.TryGetValue(what, out measure))
                throw new CouldNotMeasure(
                    "cannot measure «" + what + "»: the names are " +
                    string.Join(", ", DocumentMeasurements.Keys.ToArray()) +
                    ", state.… and measurements.…");
            return measure(doc, docState);
        }

        /// <summary>How many edges the state declares as curved.</summary>
        private static int CurvedEdges(Dictionary<string, object> docState)
        {
            var edges = (docState != null && docState.ContainsKey("edges")
                ? docState["edges"] : null) as System.Collections.IList;
            if (edges == null) return 0;
            int n = 0;
            foreach (var s in edges)
            {
                var entry = s as Dictionary<string, object>;
                if (entry != null && entry.ContainsKey("curved") &&
                    Convert.ToString(entry["curved"]).Equals("True", StringComparison.OrdinalIgnoreCase))
                    n++;
            }
            return n;
        }

        private static double[] CenterOfMass(IModelDoc2 doc)
        {
            var partDoc = doc as IPartDoc;
            if (partDoc == null) return null;
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null || bodies.Length == 0) return null;
            double vx = 0, vy = 0, vz = 0, volume = 0;
            foreach (var c in bodies)
            {
                var mp = (c as IBody2)?.GetMassProperties(1000.0) as double[];
                if (mp == null || mp.Length < 4) continue;
                double v = mp[3] * 1e9;
                if (v <= 0) continue;
                volume += v;
                vx += mp[0] * 1000.0 * v;
                vy += mp[1] * 1000.0 * v;
                vz += mp[2] * 1000.0 * v;
            }
            if (volume <= 0) return null;
            return new[]
            {
                Math.Round(vx / volume, 3), Math.Round(vy / volume, 3), Math.Round(vz / volume, 3),
            };
        }

        /// <summary>
        /// The coordinates of the centers of the cylindrical faces along an axis, in
        /// mm, sorted. A through hole leaves several faces on the same axis:
        /// the HOLE is counted, not the face.
        /// </summary>
        private static double[] CylinderCenters(IModelDoc2 doc, int axis)
        {
            return Cylinders(doc).Select(c => Math.Round(c[axis], 2)).OrderBy(x => x).ToArray();
        }

        private static double[] CylindersDiameters(IModelDoc2 doc)
        {
            return Cylinders(doc).Select(c => Math.Round(c[3] * 2, 2)).OrderBy(x => x).ToArray();
        }

        /// <summary>{x, y, z, radius} of every distinct cylinder, in mm.</summary>
        private static List<double[]> Cylinders(IModelDoc2 doc)
        {
            var outside = new List<double[]>();
            var partDoc = doc as IPartDoc;
            if (partDoc == null) return outside;
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null) return outside;
            foreach (var c in bodies)
            {
                var faces = (c as IBody2)?.GetFaces() as object[];
                if (faces == null) continue;
                foreach (var o in faces)
                {
                    double[] par;
                    try
                    {
                        var su = (o as IFace2)?.IGetSurface();
                        if (su == null || !su.IsCylinder()) continue;
                        par = su.CylinderParams as double[];
                    }
                    catch { continue; }
                    if (par == null || par.Length < 7) continue;
                    var q = new[] { par[0] * 1000, par[1] * 1000, par[2] * 1000, par[6] * 1000 };
                    bool already = outside.Any(p =>
                        Math.Abs(p[0] - q[0]) < 0.05 && Math.Abs(p[1] - q[1]) < 0.05 &&
                        Math.Abs(p[2] - q[2]) < 0.05 && Math.Abs(p[3] - q[3]) < 0.05);
                    if (!already) outside.Add(q);
                }
            }
            return outside;
        }

        /// <summary>The case files, or null (after having already said why).</summary>
        private static List<string> CasesFile()
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var bench = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                var casesRoot = Path.Combine(bench.FullName, "cases");
                var file = new List<string>();
                foreach (var family in Families)
                {
                    var folder = Path.Combine(casesRoot, family);
                    if (!Directory.Exists(folder))
                    {
                        BenchError("cases: the folder «" + family + "» exists", folder);
                        return null;
                    }
                    file.AddRange(Directory.GetFiles(folder, "*.json"));
                }
                return file.OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal).ToList();
            }
            catch (Exception ex) { BenchError("cases: find the folder", Why(ex)); return null; }
        }

        // ==================================================================
        // A CASE
        // ==================================================================
        /// <summary>
        /// `true` if the file is a hand-written PLAN and not a declared case.
        ///
        /// The root of a plan is an array of steps; that of a case is an
        /// object with `name`, `why` and `steps`. **An unreadable file is NOT
        /// a plan**: `false` is returned and it is let through to `RunCase`,
        /// which will declare it a bench error with the right reason. *A
        /// recognition that swallows the failures too stops recognizing.*
        /// </summary>
        private static bool IsAPlanNotACase(string filePath)
        {
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                s.MaxJsonLength = int.MaxValue;
                return s.DeserializeObject(File.ReadAllText(filePath)) is object[];
            }
            catch { return false; }
        }

        private static bool RunCase(string filePath, List<Twinning> twins)
        {
            string itemName = Path.GetFileNameWithoutExtension(filePath);
            Dictionary<string, object> testCase;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                testCase = s.DeserializeObject(File.ReadAllText(filePath)) as Dictionary<string, object>;
            }
            // A BADLY WRITTEN CASE IS A BENCH ERROR, NOT A RED CHECK
            // (entry F4): «a check failed» says that something was
            // measured, and here nothing was measured.
            catch (Exception ex) { BenchError(itemName, "unreadable JSON: " + Why(ex)); return false; }
            if (testCase == null) { BenchError(itemName, "the file is not a JSON object"); return false; }

            // AN UNKNOWN KEY IS A CASE WRITTEN WRONG (W3.1-bis.2, item 4): the
            // reader would skip it in silence, and whatever it asked for would
            // never be checked. Refused before anything is built.
            var unknown = _caseKeys.Unknown(testCase, _caseKeys.DeclaredRoot);
            if (unknown.Count > 0) { BenchError(itemName, CaseKeys.Message(unknown)); return false; }

            // The name of the case is the one INSIDE the file, and it must match the
            // file name: two different names for the same thing make one look
            // in the wrong place when a check turns red.
            string declared = Text(testCase, "name");
            if (declared == null) { BenchError(itemName, "«name» is missing"); return false; }
            if (declared != itemName)
            {
                BenchError(itemName, "the file is called «" + itemName + "» and inside says «" +
                                    declared + "»");
                return false;
            }
            if (Text(testCase, "why") == null)
            {
                BenchError(itemName, "«why» is missing: a case without the doubt it settles, " +
                                    "no one can tell why it exists any more");
                return false;
            }

            // THE PREPARATION IS NO LONGER JUST «A NEW PART» (entry GA0).
            // A case can ask for an ASSEMBLY, and then preparing it means
            // building the components, saving them to disk, reopening them and inserting them.
            // `PrepareDocument` returns null when it did not succeed, and has
            // already said why with `BenchError`: here a second message is not added,
            // which would say the same thing worse.
            Preparation prepared = null;
            bool green;
            try
            {
                prepared = PrepareDocument(itemName, testCase);
                if (prepared == null || prepared.Document == null) return false;
                green = RunSteps(itemName, testCase, prepared.Document, twins, filePath, prepared);
            }
            // I COULD NOT MEASURE IS NOT A RED CHECK (entry K17).
            //
            // Before, this `catch` marked red whatever had
            // happened, and on 23/9/2026 it painted red nine well-written
            // cases while SOLIDWORKS no longer opened documents.
            // Now the preparation has a type of its own: if it failed, we
            // say what was NOT measured.
            catch (Shared.PreparationFailed ex)
            {
                if (Sentinel.HasGone) Skipped(itemName, Why(ex));
                else BenchError(itemName, Why(ex));
                return false;
            }
            catch (Exception ex) { Check(itemName, false, Why(ex)); return false; }
            finally
            {
                ThrowAwayPreparation(prepared);
            }

            return OnThisLibrary(itemName, testCase, green);
        }

        /// <summary>
        /// `red_before` — THE STRONGEST PROOF THAT A CHECK REALLY
        /// GUARDS ITS DEFECT (entry F10).
        ///
        /// A case can declare the version of the library **before which
        /// it had to be red**. Launching the bench with `--library` on
        /// an older version, that case **must** fail: if it passes, it
        /// was guarding nothing.
        ///
        /// A hand-written «proven to fail» flag is a declaration, and a
        /// declaration is copied from the case next to it. This one is not: it is decided by the
        /// library that runs.
        /// </summary>
        private static bool OnThisLibrary(string itemName, Dictionary<string, object> testCase, bool green)
        {
            string before = Text(testCase, "red_before");
            if (before == null) return green;

            Version threshold, now;
            if (!Version.TryParse(before, out threshold))
            {
                BenchError(itemName, "«red_before» is not a version: «" + before + "»");
                return false;
            }
            now = _addin.GetName().Version;
            if (now >= threshold) return green;   // repaired library: the true outcome counts

            // Library older than the repair: the case MUST be red.
            Check(itemName + " · red before " + before, !green,
                  green
                      ? "on " + now + " it passed, and it should not have: this check does not " +
                        "guard the defect it says it guards"
                      : "on " + now + " it is red, as it should be");
            return !green;
        }

        /// <summary>
        /// The steps of a case, on an already open document.
        ///
        /// It is in a method of its own because `--mutate` (entry F6) re-runs the SAME
        /// case with an argument changed, and that case does not come from a file:
        /// it comes from an in-memory copy. `twins` and `path` may be
        /// null — there is no pair to record there and no file
        /// next to which to look.
        /// </summary>
        private static bool RunSteps(
            string itemName, Dictionary<string, object> testCase, IModelDoc2 doc,
            List<Twinning> twins, string filePath,
            Preparation toClean = null)
        {
            var steps = Items(testCase, "steps");
            if (steps == null || steps.Count == 0) { BenchError(itemName, "«steps» is missing"); return false; }

            {
                // THE DOCUMENT CAN CHANGE MID-CASE (entry GT0).
                //
                // `create_drawing` opens a drawing, and from there on the steps
                // work on that one. As long as this was `doc` and nothing else, a case
                // that created a drawing put the views inside the part — or more
                // often stopped on «the active document is not a
                // drawing». It is the reason why **none of the ten GT
                // entries ever had a bench case**: it was not lack of will, it is
                // that the bench did not know how to follow the second document.
                //
                // Only what the operation **declares** is followed
                // (`Outcome.NewDocument`), never SOLIDWORKS's active document:
                // on this machine SOLIDWORKS is shared, and
                // «active» is what anybody has in front of them.
                var current = doc;
                var docState = ReadState(current);
                // The state as it was BEFORE every step: it serves «uid_from_step».
                var statesBefore = new List<Dictionary<string, object>>();

                for (int i = 0; i < steps.Count; i++)
                {
                    statesBefore.Add(docState);
                    var planStep = steps[i] as Dictionary<string, object>;
                    if (planStep == null) { BenchError(itemName, "step " + (i + 1) + " is not an object"); return false; }
                    string label = itemName + " · step " + (i + 1);

                    // THE UIDs READ BEFORE, AS THE BRIDGE DOES (entry E19).
                    //
                    // A normal case resolves the selectors against the state of NOW,
                    // that is with the uids already updated: it is convenient, and it is a
                    // road that in production nobody travels. Whoever writes
                    // a plan reads the state ONCE and then writes three steps;
                    // from the second on the uids they carry are the old ones,
                    // and the bridge attaches the old signatures to them. «uid_from_step: 2»
                    // does the same: selectors and signatures from the state BEFORE
                    // step 2. Without this the search for edges by signature
                    // could not even be written as a check.
                    var uidState = docState;
                    object fromStep;
                    if (planStep.TryGetValue("uid_from_step", out fromStep) && fromStep != null)
                    {
                        int k;
                        if (!int.TryParse(Convert.ToString(fromStep, CultureInfo.InvariantCulture),
                                          NumberStyles.Integer, CultureInfo.InvariantCulture, out k) ||
                            k < 1 || k > i + 1)
                        {
                            BenchError(label, "«uid_from_step» must be the number of a step " +
                                          "up to this one (1.." + (i + 1) + "), not «" + fromStep + "»");
                            return false;
                        }
                        uidState = statesBefore[k - 1];
                    }

                    string operation = Text(planStep, "operation");
                    if (operation == null) { BenchError(label, "«operation» is missing"); return false; }

                    object rawArg;
                    planStep.TryGetValue("arguments", out rawArg);
                    Dictionary<string, object> arguments;
                    // A SELECTOR THAT DOES NOT RESOLVE IS NOT A RED CHECK:
                    // the case could not be RUN, and calling it «failed»
                    // would make one believe that something was measured (entry F4).
                    try { arguments = Resolve(rawArg, uidState) as Dictionary<string, object>; }
                    catch (CouldNotMeasure ex) { BenchError(label, ex.Message); return false; }
                    catch (Exception ex) { Check(label, false, ex.Message); return false; }
                    if (arguments == null) arguments = new Dictionary<string, object>();
                    if (!ReferenceEquals(uidState, docState)) AttachSignatures(arguments, uidState);

                    // «select_first» (entry Z-C21.2, finding L13-1): the entities
                    // that stay SELECTED when the step starts, as a previous
                    // step or whoever is in front of SOLIDWORKS would leave
                    // them. It serves the defects that depend on the selection
                    // and not on the arguments: `SaveAs3` exports only the selected
                    // faces (IModelDocExtension~SaveAs3.html, Remarks), and a
                    // case without this field could not even write it.
                    object toSelect;
                    if (planStep.TryGetValue("select_first", out toSelect) && toSelect != null)
                    {
                        string why;
                        if (!SelectBefore(current, toSelect, uidState, out why))
                        {
                            BenchError(label, "«select_first»: " + why);
                            return false;
                        }
                    }
                    // «hide_first» (entry Z-C21.2, finding L12-1): the bodies
                    // hidden when the step starts, as whoever wants to
                    // look inside the part hides them. No catalog operation
                    // hides a body, and without this field the defect «a
                    // hidden body leaves the check» could not be written.
                    object toHide;
                    if (planStep.TryGetValue("hide_first", out toHide) && toHide != null)
                    {
                        string why;
                        if (!HideBefore(current, toHide, uidState, out why))
                        {
                            BenchError(label, "«hide_first»: " + why);
                            return false;
                        }
                        // The state is re-read: the step receives it, and a hidden
                        // body is a fact that the state must see.
                        docState = ReadState(current);
                    }
                    // «activate_before» (Z-C21.4, finding L16-2): a configuration of
                    // ANOTHER open document made active when the step starts, as the
                    // user leaves it by working on the model while the drawing is
                    // open. activate_configuration acts on the current document only,
                    // so without this field a drawing step could never find the
                    // model on a configuration other than the one its views show.
                    object toActivate;
                    if (planStep.TryGetValue("activate_before", out toActivate) && toActivate != null)
                    {
                        string why;
                        if (!ActivateBefore(toActivate, uidState, out why))
                        {
                            BenchError(label, "«activate_before»: " + why);
                            return false;
                        }
                    }

                    double volumeBefore = DocumentVolume(current);
                    int featureBefore = HowManyFeatures(docState);

                    string error;
                    IModelDoc2 born;
                    var measurements = Run(current, operation, arguments, out error, out born, docState);
                    // IT CHANGES ONLY IF THE STEP SUCCEEDED. A `create_drawing`
                    // that failed midway may have left a document on the monitor
                    // and it is not the one to continue on: following a document
                    // born from a red step would mean measuring the wreck.
                    if (measurements != null && born != null && !ReferenceEquals(born, current))
                    {
                        current = born;
                        string whatItIsCalled = null;
                        try { whatItIsCalled = current.GetTitle(); } catch { }
                        // It is SAID. A silent change of document is the
                        // thing that makes a red case unreadable three steps
                        // further on: one reads «the view is not there» and goes
                        // to look for the defect in the wrong operation.
                        Console.WriteLine("       | from here on it works on «" +
                                          (whatItIsCalled ?? "(untitled)") + "»");

                        // AND IT IS MARKED TO CLOSE IT AFTERWARDS.
                        //
                        // «The bench knows how to follow the second document» is not
                        // finished until it also knows how to **close it again**. A
                        // drawing left open stays there for all the following
                        // cases, and it is not an aesthetic nuisance: six
                        // documents too many are ~650 more characters in the
                        // state, and the check «the state is under 16 KB» goes
                        // from 15.6k to 16.2k and **turns red without the
                        // code having changed**. Half an hour hunting for a
                        // regression that is not there.
                        // THE REFERENCE IS MARKED, NOT THE NAME (entry K12-bis).
                        //
                        // Here the drawing is still called `Disegno38 -
                        // Foglio1`; two steps after the case saves it and it becomes
                        // `drawing_dimensions_itself - Sheet1`. By marking the name of
                        // now, at the end of the case SOLIDWORKS was asked to
                        // close a document that no longer existed under that
                        // name — and `CloseDoc` on an unknown name is not an
                        // error, so nobody said so.
                        if (toClean != null)
                            toClean.DocumentsToClose.Add(current);
                    }

                    docState = ReadState(current);

                    var mustFail = planStep.ContainsKey("must_fail")
                        ? planStep["must_fail"] as Dictionary<string, object> : null;

                    if (mustFail != null)
                    {
                        if (!LookAtRefusal(label, mustFail, measurements, error,
                                             current, docState, volumeBefore, featureBefore, filePath))
                            return false;
                        // THE PAIR IS RECORDED HERE AND LOOKED AT THE END:
                        // «the twin is green in this run» is known only after
                        // the run is over (entry F5).
                        if (twins != null)
                            twins.Add(new Twinning
                            {
                                Label = label,
                                Case = itemName,
                                Twin = Text(mustFail, "twin"),
                                Operation = operation,
                                Arguments = (planStep.ContainsKey("arguments") ? planStep["arguments"] : null)
                                            as Dictionary<string, object>,
                                PartBefore = steps.Take(i).ToList(),
                                DifferenceInPart = mustFail.ContainsKey(DifferenceInPartField)
                                    ? Convert.ToString(mustFail[DifferenceInPartField], CultureInfo.InvariantCulture)
                                    : null,
                            });
                        continue;
                    }

                    if (measurements == null) { Check(label, false, "failed: " + Short(error)); return false; }

                    var expectations = Items(planStep, "expect");
                    if (expectations == null || expectations.Count == 0)
                    {
                        // A STEP WITHOUT EXPECTATIONS IS A BLIND STEP. It passes and
                        // measures nothing, and it is the most comfortable way of having a
                        // green check that proves nothing. It is said, and it is said that the
                        // case is badly written, not that the part is wrong.
                        BenchError(label,
                              "no expectation: a step that measures nothing is not a check");
                        return false;
                    }

                    foreach (var a in expectations)
                    {
                        _expectations++;
                        Ledger.ExpectationAttempted();
                        var expectation = a as Dictionary<string, object>;
                        if (expectation == null)
                        { BenchError(label, "an expectation is not an object"); return false; }
                        string detail;
                        bool ok;
                        // THE THREE MEASUREMENTS OF THE DOCUMENT are handed over by whoever has the
                        // document: the evaluator does not know what an
                        // `IModelDoc2` is, and that is what makes it testable
                        // without SOLIDWORKS (entry F7).
                        var here = current;
                        try { ok = Matches(expectation, c => FromDocument(c, here, docState), docState, measurements, out detail); }
                        catch (CouldNotMeasure ex)
                        { BenchError(label + " · " + Text(expectation, "what"), ex.Message); return false; }
                        catch (Exception ex) { Check(label, false, ex.Message); return false; }
                        // VERIFIED means «I measured and I compared»,
                        // not «it went well»: a red one is verified all right.
                        _verifiedExpectations++;
                        Ledger.ExpectationChecked();
                        CheckExpectation(label + " · " + Text(expectation, "what"), ok, detail);
                        if (!ok) return false;
                    }
                }
            }

            // ALL THE STEPS, ALL THE EXPECTATIONS. One gets here only if nothing
            // came out along the way: this is what entry F5 calls «green
            // in this run».
            return true;
        }

        /// <summary>
        /// Leaves selected the entities of «select_first» (entry Z-C21.2).
        ///
        /// The field is a list of uids or selectors, resolved against the SAME
        /// state as the step's arguments. The entity is taken from the signatures
        /// of the library's state — the same ones the operations use to
        /// find «F3» — and is selected with `IEntity.Select4(true, null)`.
        /// Then it is counted: a selection that did not succeed would make a case
        /// that proved nothing green, so it is a bench error.
        /// </summary>
        private static bool SelectBefore(
            IModelDoc2 doc, object raw, Dictionary<string, object> uidState,
            out string why)
        {
            List<string> uid;
            List<object> entity;
            if (!StateEntity(doc, raw, uidState, out uid, out entity, out why))
                return false;

            doc.ClearSelection2(true);
            for (int i = 0; i < uid.Count; i++)
            {
                var e = entity[i] as IEntity;
                if (e == null) { why = "«" + uid[i] + "» is not an entity I can select"; return false; }
                if (!e.Select4(true, null)) { why = "Select4 answered no on «" + uid[i] + "»"; return false; }
            }
            int howMany = ((ISelectionMgr)doc.SelectionManager).GetSelectedObjectCount2(-1);
            if (howMany != uid.Count)
            {
                why = "asked to select " + uid.Count + " entities, selected " + howMany;
                return false;
            }
            Console.WriteLine("       | before the step these remain selected: " + string.Join(", ", uid));
            return true;
        }

        /// <summary>
        /// «activate_before»: { "document": path of an open document, "configuration":
        /// its name } (Z-C21.4, L16-2). The document is found among the open ones
        /// by its full path, the configuration activated with ShowConfiguration2,
        /// and the active configuration READ BACK: ShowConfiguration2 answers
        /// false when the configuration is already the active one, so its answer
        /// decides nothing.
        /// </summary>
        private static bool ActivateBefore(object raw, Dictionary<string, object> uidState, out string why)
        {
            why = null;
            Dictionary<string, object> request;
            try { request = Resolve(raw, uidState) as Dictionary<string, object>; }
            catch (Exception ex) { why = ex.Message; return false; }
            string path = request == null ? null : Text(request, "document");
            string configuration = request == null ? null : Text(request, "configuration");
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(configuration))
            {
                why = "it needs «document» (a path) and «configuration» (a name)";
                return false;
            }
            IModelDoc2 target = null;
            try
            {
                var d = _sw.GetFirstDocument() as IModelDoc2;
                for (int guard = 0; d != null && guard < 500; guard++)
                {
                    string own = null;
                    try { own = d.GetPathName(); } catch { }
                    if (!string.IsNullOrWhiteSpace(own) &&
                        string.Equals(Path.GetFullPath(own), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    { target = d; break; }
                    d = d.GetNext() as IModelDoc2;
                }
            }
            catch (Exception ex) { why = "walking the open documents: " + Why(ex); return false; }
            if (target == null) { why = "«" + path + "» is not open"; return false; }
            bool answer;
            try { answer = target.ShowConfiguration2(configuration); }
            catch (Exception ex) { why = "ShowConfiguration2(«" + configuration + "»): " + Why(ex); return false; }
            string active = null;
            try
            {
                IConfigurationManager targetManager = target.ConfigurationManager;
                IConfiguration activeNow = targetManager == null ? null : targetManager.ActiveConfiguration;
                active = activeNow == null ? null : activeNow.Name;
            }
            catch { }
            if (!string.Equals(active, configuration, StringComparison.Ordinal))
            {
                why = "asked for «" + configuration + "» on «" + Path.GetFileName(path) + "» (ShowConfiguration2 " +
                         "answered " + answer + "), and reading it back the active configuration is «" + (active ?? "?") + "»";
                return false;
            }
            Console.WriteLine("       | before the step, «" + Path.GetFileName(path) + "» is on «" + active + "»");
            return true;
        }

        /// <summary>
        /// Hides the bodies of «hide_first» (entry Z-C21.2) with
        /// `IBody2.HideBody(true)` (IBody2~HideBody.html: «True to hide the
        /// body, false to show the body») and re-reads `IBody2.Visible`: a body
        /// that stays visible would make a case that proved nothing green.
        /// </summary>
        private static bool HideBefore(
            IModelDoc2 doc, object raw, Dictionary<string, object> uidState,
            out string why)
        {
            List<string> uid;
            List<object> entity;
            if (!StateEntity(doc, raw, uidState, out uid, out entity, out why))
                return false;
            for (int i = 0; i < uid.Count; i++)
            {
                // A BODY, OR THE BODY OF A FACE. The names of the bodies after a
                // copy or a feature are not predictable, and the numbers C1, C2
                // change from one run to the next (entry Z-C16); a face instead is
                // named by WHERE it is (`@face(normal=front,x~=150)`).
                var body = entity[i] as IBody2;
                var face = entity[i] as IFace2;
                if (body == null && face != null) body = face.GetBody() as IBody2;
                if (body == null) { why = "«" + uid[i] + "» is neither a body nor a face"; return false; }
                body.HideBody(true);
                if (body.Visible) { why = "«" + uid[i] + "» is still visible after HideBody(true)"; return false; }
            }
            try { doc.GraphicsRedraw2(); } catch { }
            Console.WriteLine("       | before the step these remain hidden: " + string.Join(", ", uid));
            return true;
        }

        /// <summary>
        /// The COM objects behind uids or selectors, from the signatures of the
        /// library's state: the same ones the operations use to find «F3» or
        /// «C2».
        /// </summary>
        private static bool StateEntity(
            IModelDoc2 doc, object raw, Dictionary<string, object> uidState,
            out List<string> uid, out List<object> entity, out string why)
        {
            why = null;
            uid = new List<string>();
            entity = new List<object>();
            object resolved;
            try { resolved = Resolve(raw, uidState); }
            catch (Exception ex) { why = ex.Message; return false; }
            var list = resolved as System.Collections.IList;
            if (list != null) { foreach (var u in list) uid.Add(Convert.ToString(u)); }
            else if (resolved != null) uid.Add(Convert.ToString(resolved));
            if (uid.Count == 0) { why = "the list is empty"; return false; }

            object objectState;
            try { objectState = ReadObjectState(doc); }
            catch (Exception ex) { why = "cannot read the state: " + Why(ex); return false; }
            var signatures = ReflectionLog.Global.Property(_tState, "Signatures",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(objectState) as System.Collections.IDictionary;
            if (signatures == null) { why = "the library's state has no «Signatures»"; return false; }
            foreach (var u in uid)
            {
                var signature = signatures.Contains(u) ? signatures[u] : null;
                var e = signature == null ? null : ReflectionLog.Global.Field(signature.GetType(), "Entity")?.GetValue(signature);
                if (e == null) { why = "the state has no «" + u + "»"; return false; }
                entity.Add(e);
            }
            return true;
        }

        // ==================================================================
        // THE STEP THAT MUST FAIL
        // ==================================================================
        /// <summary>
        /// A refusal is not «nothing happened»: one looks at **what it
        /// said** and **whether the part stayed as it was**. An operation that
        /// refuses after having already removed material is worse than one that succeeds.
        /// </summary>
        private static bool LookAtRefusal(
            string label, Dictionary<string, object> rules,
            Dictionary<string, object> measurements, string error,
            IModelDoc2 doc, Dictionary<string, object> docState,
            double volumeBefore, int featureBefore, string casePath)
        {
            if (measurements != null)
            {
                Check(label, false, "it should have failed but it succeeded");
                return false;
            }

            var parts = Items(rules, "message_includes");
            if (parts == null || parts.Count == 0)
            {
                BenchError(label,
                      "«must_fail» without «message_includes»: any error " +
                      "would do, even the wrong one");
                return false;
            }
            foreach (var p in parts)
            {
                string lookFor = Convert.ToString(p);
                if ((error ?? "").IndexOf(lookFor, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Check(label, false, "it refused but does not say «" + lookFor + "»: " + Short(error));
                    return false;
                }
            }

            if (True(rules, "volume_unchanged"))
            {
                double now = DocumentVolume(doc);
                if (Math.Abs(now - volumeBefore) > 0.001)
                {
                    Check(label, false, string.Format(CultureInfo.InvariantCulture,
                        "it refused but the volume changed: {0:0.###} -> {1:0.###}",
                        volumeBefore, now));
                    return false;
                }
            }
            if (True(rules, "no_extra_feature"))
            {
                int now = HowManyFeatures(docState);
                if (now > featureBefore)
                {
                    Check(label, false, "it refused but left " +
                                            (now - featureBefore) + " extra features");
                    return false;
                }
            }

            // THE TWIN IS MANDATORY (entry F5).
            //
            // A check that verifies only the refusal does not tell «the
            // guard works» from «the guard refuses everything». Case 1 of
            // 80u — the shell of 30 on a box 52 tall, which seemed
            // impossible and was only ugly — says what happens without it.
            string twin = Text(rules, "twin");
            if (twin == null)
            {
                BenchError(label,
                    "case not allowed: a «must_fail» must name in «twin» " +
                    "the case that SUCCEEDS with almost the same arguments. Without it, this " +
                    "refusal does not tell a check that works from one that " +
                    "refuses everything.");
                return false;
            }
            // Without the path — that is under `--mutate`, where the case comes from
            // an in-memory copy — the file next to it cannot be looked at, and
            // it is not what is being tested.
            if (casePath == null) { Check(label, true, "refused: " + Short(error)); return true; }
            var nextTo = Path.Combine(Path.GetDirectoryName(casePath), twin + ".json");
            if (!File.Exists(nextTo))
            {
                // «twin missing» is one of the three bench errors that
                // entry F2 named by name, together with «case not allowed» and
                // «field absent».
                BenchError(label, "the twin «" + twin + "» does not exist");
                return false;
            }

            Check(label, true, "refused: " + Short(error));
            return true;
        }

    }
}
