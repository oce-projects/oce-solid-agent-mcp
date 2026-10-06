using System;
using System.Collections.Generic;
using Oce.Solid.Agent.Bench;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE EVALUATOR OF THE EXPECTATIONS, against fake states written by hand.
    ///
    /// **If it is broken, the whole suite with SOLIDWORKS becomes green for free**:
    /// a comparison that always answers «yes» is not seen anywhere, except
    /// from here. It is the reason this group exists, and for which the
    /// evaluator was moved into a file that compiles even where the CAD
    /// is not there.
    /// </summary>
    internal static class ExpectationsTests
    {
        internal static void All()
        {
            Group("the evaluator of the expectations");

            var measurements = new Dictionary<string, object>
            {
                ["removed_mm3"] = 1234.5,
                ["facing"] = "inner",
                ["faces_removed"] = 1,
            };
            var docState = new Dictionary<string, object>
            {
                ["bounding_box_mm"] = new List<object> { 100.0, 60.0, 40.0 },
                ["material"] = null,
                ["faces"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "F1", ["area_mm2"] = 6000.0, ["facing"] = "front" },
                    new Dictionary<string, object> { ["uid"] = "F2", ["area_mm2"] = 2000.0, ["facing"] = "back" },
                },
            };

            string d;

            // ── THE RIGHT NUMBER PASSES, THE WRONG ONE DOES NOT ───────────
            Check("right number: green",
                  Expectations.Matches(Expectation("measurements.removed_mm3", 1234.5), null, docState, measurements, out d), d);
            Check("wrong number: red",
                  !Expectations.Matches(Expectation("measurements.removed_mm3", 1234.6), null, docState, measurements, out d), d);

            // ── THE TOLERANCE IS ABSOLUTE, AND THE WHY IS NEEDED ──────────
            var withTolerance = Expectation("measurements.removed_mm3", 1234.0);
            withTolerance["tolerance"] = 1.0;
            withTolerance["tolerance_why"] = "it is a check, and the number is written by hand";
            Check("tolerance respected: green",
                  Expectations.Matches(withTolerance, null, docState, measurements, out d), d);

            withTolerance["equal_to"] = 1200.0;
            Check("tolerance exceeded: red",
                  !Expectations.Matches(withTolerance, null, docState, measurements, out d), d);

            var withoutWhy = Expectation("measurements.removed_mm3", 1234.0);
            withoutWhy["tolerance"] = 1.0;
            Check("tolerance without the why: not evaluated",
                  Raise(() => { string x; Expectations.Matches(withoutWhy, null, docState, measurements, out x); },
                       "tolerance_why"));

            // ── THE ABSENT FIELD IS NOT A ZERO (entry F4) ─────────────────
            //
            // It is the trap in its literal form: a dictionary that does not
            // find the key returns null, and null compared with null passes.
            Check("missing field: not evaluated, and it says which ones there were",
                  Raise(() => { string x; Expectations.Matches(Expectation("measurements.does_not_exist", 0.0), null, docState, measurements, out x); },
                       "missing field: measurements.does_not_exist"));
            Check("missing field: it lists the fields that were there",
                  Raise(() => { string x; Expectations.Matches(Expectation("measurements.does_not_exist", 0.0), null, docState, measurements, out x); },
                       "faces_removed, facing, removed_mm3"));

            // ── A DECLARED NULL IS MEASURED ───────────────────────────────
            Check("«material: null» is measured, it is not missing",
                  Expectations.Matches(Expectation("state.material", ""), null, docState, measurements, out d), d);

            // ── THE LISTS OF NUMBERS ──────────────────────────────────────
            Check("list of numbers right: green",
                  Expectations.Matches(Expectation("state.bounding_box_mm", new List<object> { 100.0, 60.0, 40.0 }),
                                  null, docState, measurements, out d), d);
            Check("list of numbers with a different value: red",
                  !Expectations.Matches(Expectation("state.bounding_box_mm", new List<object> { 100.0, 60.0, 41.0 }),
                                   null, docState, measurements, out d), d);
            Check("list of a different length: red",
                  !Expectations.Matches(Expectation("state.bounding_box_mm", new List<object> { 100.0, 60.0 }),
                                   null, docState, measurements, out d), d);

            // ── INSIDE A LIST, BY INDEX ───────────────────────────────────
            Check("state.faces[1].area_mm2 can be read",
                  Expectations.Matches(Expectation("state.faces[1].area_mm2", 2000.0), null, docState, measurements, out d), d);
            Check("an index beyond the end: not evaluated",
                  Raise(() => { string x; Expectations.Matches(Expectation("state.faces[9].area_mm2", 0.0), null, docState, measurements, out x); },
                       "missing field"));

            // ── THE MEASUREMENTS OF THE DOCUMENT, WITHOUT A DOCUMENT ──────
            //
            // Here SOLIDWORKS is not there: `volume_mm3` cannot be measured, and the
            // evaluator SAYS so instead of answering zero.
            Check("«volume_mm3» without a document: it says so",
                  Raise(() => { string x; Expectations.Matches(Expectation("volume_mm3", 0.0), null, docState, measurements, out x); },
                       "measured on the document"));

            // And with a fake document — a function that returns numbers — it
            // is measured all right: it is the bridge that the real bench passes to it.
            Func<string, object> fake = c => c == "volume_mm3" ? (object)240000.0 : null;
            Check("«volume_mm3» with something that can measure it: green",
                  Expectations.Matches(Expectation("volume_mm3", 240000.0), fake, docState, measurements, out d), d);

            // ── A NAME THAT DOES NOT EXIST ────────────────────────────────
            //
            // THE LIST OF NAMES IS NO LONGER IN THE EVALUATOR, and this is the
            // proof that it does not go back. It was there, and it came unstuck: two new measurements
            // added to whoever measures and forgotten here came out as «I do not
            // know how to measure «last_plane_mm»: the names are …» with the old
            // list next to it — a message that said what was false about itself,
            // and that gave BENCH ERROR to a check that the bridge passed.
            //
            // Now the evaluator does not judge the names: it passes them to whoever has the
            // document, and it is that one who says it does not know them. The passing is
            // verified (the function is really called) and that
            // the sentence of the refusal reaches the reader whole.
            bool arrived = false;
            Func<string, object> strict = c =>
            {
                arrived = true;
                throw new Expectations.CouldNotMeasure(
                    "I cannot measure «" + c + "»: the names are volume_mm3");
            };
            Check("an unknown «what»: the refusal of whoever measures arrives whole",
                  Raise(() => { string x; Expectations.Matches(Expectation("foo", 1.0), strict, docState, measurements, out x); },
                       "I cannot measure «foo»"));
            Check("an unknown «what» is really asked of whoever measures", arrived);

            // ── TWO DIFFERENT PROFILES ARE NOT WRITTEN THE SAME ───────────
            //
            // IT IS THE RED OF A DEFECT THAT REALLY TRIGGERED. The comparison
            // between a case that must fail and its twin used
            // `Convert.ToString`, which on an `object[]` returns «System.Object[]»:
            // the refusal of the revolve and its twin differ ONLY
            // in the contours, and the bench called them «a copy».
            //
            // The two lines below are the two faces of the same blindness:
            // different lists must be written different, equal lists equal —
            // because a function that always returns different strings would pass
            // the first check and break the comparison all the same.
            Func<double[], object> profile = p =>
            {
                var points = new List<object>();
                for (int i = 0; i + 1 < p.Length; i += 2)
                    points.Add(new List<object> { p[i], p[i + 1] });
                return new List<object> { points };
            };
            object cylinder = profile(new double[] { 0, 0, 0, 20, 50, 20, 50, 0 });
            object straddling = profile(new double[] { 0, -20, 0, 20, 50, 20, 50, -20 });
            Check("two different outlines are written differently",
                  Expectations.HowItIsWritten(cylinder) != Expectations.HowItIsWritten(straddling),
                  Expectations.HowItIsWritten(straddling));
            Check("two equal outlines are written equally",
                  Expectations.HowItIsWritten(cylinder) ==
                  Expectations.HowItIsWritten(profile(new double[] { 0, 0, 0, 20, 50, 20, 50, 0 })));
            Check("a list is not written «System.Object[]»",
                  Expectations.HowItIsWritten(cylinder).IndexOf("System.Object", StringComparison.Ordinal) < 0,
                  Expectations.HowItIsWritten(cylinder));
            Check("a number is written with the dot, not with the comma",
                  Expectations.HowItIsWritten(1.5) == "1.5", Expectations.HowItIsWritten(1.5));
            Check("nothing has a name", Expectations.HowItIsWritten(null) == "(nothing)");

            // ── A LIST THAT HAS NO NUMBERS ────────────────────────────────
            //
            // `["C2"]` — the bodies a boolean worked on. Until
            // 0.81.0 it came out as «is not a list of numbers: System.String[]»: a
            // true and useless message, because the case asked something
            // sensible and the evaluator did not know how to compare it.
            var withLists = new Dictionary<string, object>
            {
                ["tools"] = new List<object> { "C2", "C3" },
                ["one"] = new List<object> { "C2" },
                ["mixed"] = new List<object> { "C2", 7.0 },
            };
            Func<string, object, Dictionary<string, object>> att = (c, u) =>
                new Dictionary<string, object> { ["what"] = c, ["equal_to"] = u };

            Check("list of text: equal passes",
                  Expectations.Matches(att("state.tools", new List<object> { "C2", "C3" }),
                                  null, withLists, measurements, out d), d);
            Check("list of text RED: a different element",
                  !Expectations.Matches(att("state.tools", new List<object> { "C2", "C9" }),
                                   null, withLists, measurements, out d), d);
            Check("list of text RED: the ORDER matters",
                  !Expectations.Matches(att("state.tools", new List<object> { "C3", "C2" }),
                                   null, withLists, measurements, out d), d);
            Check("list of text RED: one fewer",
                  !Expectations.Matches(att("state.tools", new List<object> { "C2" }),
                                   null, withLists, measurements, out d), d);
            Check("list of text: even a single one",
                  Expectations.Matches(att("state.one", new List<object> { "C2" }),
                                  null, withLists, measurements, out d), d);
            Check("mixed list of text and numbers: compared as text",
                  Expectations.Matches(att("state.mixed", new List<object> { "C2", 7.0 }),
                                  null, withLists, measurements, out d), d);
            Check("list of text RED: if the measurement is NOT a list, it says so",
                  !Expectations.Matches(att("state.material", new List<object> { "C2" }),
                                   null, docState, measurements, out d) &&
                  d.IndexOf("is not a list", StringComparison.Ordinal) >= 0, d);

            // ── HAS THE TWIN ANYTHING TO DO WITH THE REFUSAL? (F5) ────────
            //
            // The rule lives here and not next to the bench because it is arithmetic
            // on two dictionaries: its reds are tested in a thousandth of a
            // second, and they are four cases that nobody ever looks at by hand.
            Func<string, string, Dictionary<string, object>> arg2 = (a, b) =>
                new Dictionary<string, object> { ["face_uid"] = a, ["x_mm"] = b };
            string d2;

            Check("identical twin: refused",
                  !Expectations.Resemble(arg2("F1", "10"), arg2("F1", "10"), out d2) &&
                  d2.IndexOf("copy", StringComparison.Ordinal) >= 0, d2);

            Check("twin that differs by one argument: accepted",
                  Expectations.Resemble(arg2("F1", "10"), arg2("F1", "40"), out d2), d2);

            Check("twin with nothing in common, with two arguments: refused",
                  !Expectations.Resemble(arg2("F1", "10"), arg2("F5", "40"), out d2) &&
                  d2.IndexOf("do not talk to each other", StringComparison.Ordinal) >= 0, d2);

            // THE EXCEPTION OF THE ARITY. `create_axis` takes `face_uid` and nothing else:
            // two cases that differ in that cannot have one in
            // common, and refusing them would forbid the pair for half the catalogue.
            Func<string, Dictionary<string, object>> arg1 = a =>
                new Dictionary<string, object> { ["face_uid"] = a };
            Check("twin of an operation with ONE argument only: accepted",
                  Expectations.Resemble(arg1("@face(planar=false)"),
                                      arg1("@face(normal=front)"), out d2), d2);
            Check("...and it says so, instead of pretending it is a normal pair",
                  d2.IndexOf("the only one this operation takes",
                             StringComparison.Ordinal) >= 0, d2);
            Check("one argument only, but equal: it stays a copy",
                  !Expectations.Resemble(arg1("F1"), arg1("F1"), out d2), d2);

            // THE DIFFERENCE DECLARED IN THE PART (28/9/2026, rib W1.2):
            // same arguments, different steps before. In both directions, because a
            // declaration that always passes is the fake argument from before
            // under another name.
            Func<string, double, object> partStep = (plane, thickness) => new Dictionary<string, object>
            {
                ["operation"] = "extrude_rectangle",
                ["arguments"] = new Dictionary<string, object> { ["plane"] = plane, ["thickness_mm"] = thickness },
            };
            var box = new List<object> { partStep("top", 20.0),
                new Dictionary<string, object> { ["operation"] = "shell" } };
            var plate = new List<object> { partStep("top", 2.0) };
            const string partSentence = "the plate has no walls, the box does";
            Check("same arguments on another part, declared: accepted",
                  Expectations.ResembleInPart(arg2("F1", "10"), arg2("F1", "10"), plate, box, partSentence, out d2) &&
                  d2.IndexOf("part different from step 1", StringComparison.Ordinal) >= 0, d2);
            Check("same arguments and same part, even if declared: it stays a copy",
                  !Expectations.ResembleInPart(arg2("F1", "10"), arg2("F1", "10"), box, box, partSentence, out d2) &&
                  d2.IndexOf("copy", StringComparison.Ordinal) >= 0, d2);
            Check("different arguments and the difference declared in the part: the declaration is superfluous",
                  !Expectations.ResembleInPart(arg2("F1", "10"), arg2("F1", "40"), plate, box, partSentence, out d2) &&
                  d2.IndexOf("remove", StringComparison.Ordinal) >= 0, d2);
            Check("the difference in the part declared without saying which: refused",
                  !Expectations.ResembleInPart(arg2("F1", "10"), arg2("F1", "10"), plate, box, " ", out d2), d2);

            // The contours are lists: it is the case that broke the comparison.
            Check("twins that differ only in a list: accepted",
                  Expectations.Resemble(
                      new Dictionary<string, object> { ["contours"] = straddling, ["angle_deg"] = 360.0 },
                      new Dictionary<string, object> { ["contours"] = cylinder, ["angle_deg"] = 360.0 },
                      out d2), d2);

            // ── AN EXPECTATION ON A SENTENCE ──────────────────────────────
            //
            // `does_not_include` is the half that counts: a sentence that declares «the
            // Ø30 by 40 profile» on a part that no longer has that profile
            // is the defect that gives no error.
            var withShape = new Dictionary<string, object>
            {
                ["shape"] = "a part of revolution with the profile D30 for 40, D20 for 30",
            };
            Func<object, object, Dictionary<string, object>> sentence = (c, n) =>
            {
                var a = new Dictionary<string, object> { ["what"] = "state.shape" };
                if (c != null) a["includes"] = c;
                if (n != null) a["does_not_include"] = n;
                return a;
            };

            Check("sentence: the part that is there is found",
                  Expectations.Matches(sentence(new List<object> { "profile", "D30 for 40" }, null),
                                  null, withShape, measurements, out d), d);
            Check("sentence RED: a part that is not there fails",
                  !Expectations.Matches(sentence(new List<object> { "D99 for 1" }, null),
                                   null, withShape, measurements, out d) &&
                  d.IndexOf("does not say", StringComparison.Ordinal) >= 0, d);
            Check("sentence RED: «does_not_include» fails if the word is still there",
                  !Expectations.Matches(sentence(null, new List<object> { "profile" }),
                                   null, withShape, measurements, out d) &&
                  d.IndexOf("still says", StringComparison.Ordinal) >= 0, d);
            Check("sentence: «does_not_include» passes when the word is gone",
                  Expectations.Matches(sentence(null, new List<object> { "shell" }),
                                  null, withShape, measurements, out d), d);
            Check("sentence: both can be asked together",
                  Expectations.Matches(sentence(new List<object> { "revolution" },
                                        new List<object> { "shell" }),
                                  null, withShape, measurements, out d), d);
            Check("sentence: a single string counts as a list of one",
                  Expectations.Matches(sentence("revolution", null), null, withShape, measurements, out d), d);

            // ── AND A LIST IS NOT «System.String[]» ───────────────────────
            //
            // `measurements.profiles[0].measurements` is a list: asking that it CONTAIN
            // an entry is the most natural thing in the world, and until 0.91.0
            // it failed with «does not say "80X80X5": System.String[]» — a true
            // and useless message. For «equals» the lists of text had already
            // been taught; for «contains» they had not, and nobody had noticed
            // because no case asked for it.
            var withList = new Dictionary<string, object>
            {
                ["measurements"] = new[] { "80X80X5", "100X100X6", "40X40X3" },
                ["empty"] = new string[0],
            };
            Func<string, object, object, Dictionary<string, object>> list = (theField, c, n) =>
            {
                var a = new Dictionary<string, object> { ["what"] = "measurements." + theField };
                if (c != null) a["includes"] = c;
                if (n != null) a["does_not_include"] = n;
                return a;
            };

            Check("list: «includes» finds a whole entry",
                  Expectations.Matches(list("measurements", new List<object> { "80X80X5" }, null),
                                  null, docState, withList, out d), d);
            Check("list: «includes» also finds part of an entry",
                  Expectations.Matches(list("measurements", new List<object> { "100X100" }, null),
                                  null, docState, withList, out d), d);
            Check("list RED: an entry that is not there fails",
                  !Expectations.Matches(list("measurements", new List<object> { "999X999X9" }, null),
                                   null, docState, withList, out d) &&
                  d.IndexOf("does not say", StringComparison.Ordinal) >= 0, d);
            // THE MESSAGE MUST NO LONGER SAY THE NAME OF THE TYPE. It is the half
            // that counts: the red was there before too, but it said something from which
            // one could not start again.
            Check("list RED: the message shows the entries, not «System.String[]»",
                  !Expectations.Matches(list("measurements", new List<object> { "999X999X9" }, null),
                                   null, docState, withList, out d) &&
                  d.IndexOf("System.String[]", StringComparison.Ordinal) < 0 &&
                  d.IndexOf("80X80X5", StringComparison.Ordinal) >= 0, d);
            Check("list: «does_not_include» passes if no entry says it",
                  Expectations.Matches(list("measurements", null, new List<object> { "999X999X9" }),
                                  null, docState, withList, out d), d);
            Check("list RED: «does_not_include» fails if an entry says it",
                  !Expectations.Matches(list("measurements", null, new List<object> { "40X40X3" }),
                                   null, docState, withList, out d) &&
                  d.IndexOf("still says", StringComparison.Ordinal) >= 0, d);
            // AN EMPTY LIST CONTAINS NOTHING, and it is not an absent field:
            // it is a list that is there and is empty. The two things are told apart.
            Check("list RED: an empty list contains nothing",
                  !Expectations.Matches(list("empty", new List<object> { "any" }, null),
                                   null, docState, withList, out d), d);
            // AN ABSENT FIELD IS NOT AN EMPTY SENTENCE, and it is not even a
            // red: it is a bench error, as for every other measurement. The
            // case asks for something that is not in the state, and it must be said.
            Check("sentence RED: a field that is not there is not evaluated",
                  Raise(() => { string x;
                               Expectations.Matches(sentence(new List<object> { "x" }, null), null,
                                               new Dictionary<string, object>(), measurements, out x); },
                       "missing field"));
            // And if instead whoever measures answers «nothing», that yes: it is a
            // red, because the sentence was due and is not there.
            Check("sentence RED: whoever measures answers nothing, and the sentence is missing",
                  !Expectations.Matches(
                      new Dictionary<string, object>
                      { ["what"] = "shape_of_the_part", ["includes"] = new List<object> { "x" } },
                      c => null, docState, measurements, out d) &&
                  d.IndexOf("is missing", StringComparison.Ordinal) >= 0, d);
            Check("sentence RED: an empty list asks for nothing, and is refused",
                  Raise(() => { string x;
                               Expectations.Matches(sentence(new List<object>(), null), null, withShape, measurements, out x); },
                       "asks for nothing"));
            Check("sentence RED: «equal» together with «includes» is refused",
                  Raise(() =>
                  {
                      var a = sentence(new List<object> { "profile" }, null);
                      a["equal_to"] = "something";
                      string x; Expectations.Matches(a, null, withShape, measurements, out x);
                  }, "decide which of the two"));

            // ── THE SELECTORS: ZERO OR MORE THAN ONE ARE REFUSED ──────────
            Check("selector: a single face is taken",
                  Expectations.Select("@face(normal=front)", docState) == "F1");
            Check("selector: no face, it is refused",
                  Raise(() => Expectations.Select("@face(normal=up)", docState), "I did not find it"));

            var due = new Dictionary<string, object>
            {
                ["faces"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "F1", ["facing"] = "front" },
                    new Dictionary<string, object> { ["uid"] = "F5", ["facing"] = "front" },
                },
            };
            Check("selector: two equal faces, it is refused instead of guessing",
                  Raise(() => Expectations.Select("@face(normal=front)", due), "I found 2"));

            // THE BODY BY NAME (25/9/2026): the numbers C1, C2... do not follow
            // the order of creation and change between one run and the next. The state
            // here is written backwards on purpose, and the name has the brackets.
            var bodies = new Dictionary<string, object>
            {
                ["bodies"] = new List<object>
                {
                    new Dictionary<string, object> { ["uid"] = "C1", ["name"] = "TUBE 80X80X5(2)" },
                    new Dictionary<string, object> { ["uid"] = "C2", ["name"] = "TUBE 80X80X5(1)" },
                },
            };
            Check("selector: @body takes by name, parentheses included",
                  Expectations.Select("@body(name=TUBE 80X80X5(1))", bodies) == "C2");
            Check("selector: @body with a name that is not there, it is refused",
                  Raise(() => Expectations.Select("@body(name=TUBE 80X80X5(3))", bodies), "I did not find it"));

            // THE EDGE BY WHERE IT IS (entry E18): x, y, z read point_mm. The
            // four edges 120 long of a plate are told apart only like this.
            Func<string, double, double, Dictionary<string, object>> edge = (uid, x, z) =>
                new Dictionary<string, object>
                {
                    ["uid"] = uid, ["along"] = "Y", ["length_mm"] = 120.0,
                    ["point_mm"] = new double[] { x, 0, z },
                };
            var four = new Dictionary<string, object>
            {
                ["edges"] = new List<object>
                {
                    edge("E1", -100, 0), edge("E2", 100, 0), edge("E3", -100, 20), edge("E4", 100, 20),
                },
            };
            Check("selector: the edge is taken by coordinates (x, z)",
                  Expectations.Select("@edge(axis=Y,x~=-100,z~=20)", four) == "E3");
            Check("selector: with one coordinate only the sides stay two, and it is refused",
                  Raise(() => Expectations.Select("@edge(axis=Y,x~=-100)", four), "I found 2"));
        }

        private static Dictionary<string, object> Expectation(string what, object equal)
        {
            return new Dictionary<string, object> { ["what"] = what, ["equal_to"] = equal };
        }

        /// <summary>
        /// Does it raise, and does the message contain that piece? A check that
        /// was satisfied with «it raised» would pass for the wrong
        /// exception too — and it has happened often enough in this project that
        /// we do not want it any more.
        /// </summary>
        private static bool Raise(Action f, string part)
        {
            try { f(); return false; }
            catch (Exception ex) { return ex.Message.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0; }
        }

        private static bool Raise(Func<string> f, string part)
        {
            return Raise(() => { f(); }, part);
        }
    }
}
