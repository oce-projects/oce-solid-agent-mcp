using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// `undo` TELLS THE TRUTH, Z-C21.14 (with the review of the lot).
    ///
    /// Two probes of 1/10/2026 found what `undo` got wrong:
    ///  * the live probe M19 (line BRIDGE-R1 (X2)): the state after a plan was
    ///    read on the window in front, so with another document in front the
    ///    memory of `undo` held that document's numbers and a right undo
    ///    was refused;
    ///  * the probe `redo`: SOLIDWORKS never redoes an undo entry recorded
    ///    through the API, and every plan of the bridge is one, while the
    ///    answer after a wrong press claimed the part had been put back
    ///    whenever EditRedo2 did not throw.
    /// The review of the lot added: the last plan is the memory even when it
    /// only reordered (F1); a plan that wrote nothing keeps the memory (F3);
    /// the names and the order are compared before pressing (F1, F14); the
    /// executor runs on the document the bridge checked (F7); the window in
    /// front is adopted only when the plan ended on it (F5); the resent-plan
    /// guard reads one document (F6); the document is held by reference (F11).
    ///
    /// The bench has the four cases that need SOLIDWORKS (Bench/ClientPort.cs,
    /// UndoHonesty). This group pins the rest where no CAD is needed:
    ///  * by reflection on the built bridge, the pure decisions:
    ///    MemoryAfterPlan, WhatDiffers, AnswerAfterWrongPress,
    ///    RefusalCoveredText, RefusalChangedSinceThePlanText,
    ///    NoteDocumentInFront and IdentityForTheExecutor, and on an assembly
    ///    (brief R23) WhatDiffers on components and mates,
    ///    AnswerAfterWrongPressOnAssembly and
    ///    RefusalChangedSinceThePlanOnAssemblyText;
    ///  * on the source, the rules of the plan, of `undo`, of the executor
    ///    and of the session, each seen red on changed copies of the real
    ///    source.
    /// </summary>
    internal static class Zc2114UndoHonestyContract
    {
        internal static void RunAll()
        {
            Group("`undo` tells the truth: the plan's document, the last plan, no blind press or redo (Z-C21.14)");
            Decisions();
            Sources();
        }

        // ── the pure decisions, by reflection ────────────────────────────────

        private static void Decisions()
        {
            var bridge = BridgeOrNotHere("the pure decisions of `undo` (Z-C21.14)");
            if (bridge == null) return;

            MethodInfo memory, differs, answer, refusal, changedText, note, identity, answerOnAssembly, changedOnAssembly;
            Type type;
            try
            {
                var asm = Load.Bridge(bridge);
                type = Load.Class(asm, "McpTools");
                if (type == null) { Check("the tools class is in the bridge", false, ""); return; }
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                memory = type.GetMethod("MemoryAfterPlan", any);
                differs = type.GetMethod("WhatDiffers", any);
                answer = type.GetMethod("AnswerAfterWrongPress", any);
                refusal = type.GetMethod("RefusalCoveredText", any);
                changedText = type.GetMethod("RefusalChangedSinceThePlanText", any);
                note = type.GetMethod("NoteDocumentInFront", any);
                identity = type.GetMethod("IdentityForTheExecutor", any);
                answerOnAssembly = type.GetMethod("AnswerAfterWrongPressOnAssembly", any);
                changedOnAssembly = type.GetMethod("RefusalChangedSinceThePlanOnAssemblyText", any);
                var missingMethods = new List<string>();
                if (answerOnAssembly == null) missingMethods.Add("AnswerAfterWrongPressOnAssembly");
                if (changedOnAssembly == null) missingMethods.Add("RefusalChangedSinceThePlanOnAssemblyText");
                if (memory == null) missingMethods.Add("MemoryAfterPlan");
                if (differs == null) missingMethods.Add("WhatDiffers");
                if (answer == null) missingMethods.Add("AnswerAfterWrongPress");
                if (refusal == null) missingMethods.Add("RefusalCoveredText");
                if (changedText == null) missingMethods.Add("RefusalChangedSinceThePlanText");
                if (note == null) missingMethods.Add("NoteDocumentInFront");
                if (identity == null) missingMethods.Add("IdentityForTheExecutor");
                if (missingMethods.Count > 0)
                {
                    Check("the pure methods of `undo` are in the bridge", false, "missing: " + string.Join(", ", missingMethods));
                    return;
                }
            }
            catch (Exception ex) { Check("the bridge loads", false, ex.ToString()); return; }

            Memory(memory);
            Differences(differs);
            Answers(answer);
            Refusals(refusal, changedText);
            NoteAndIdentity(note, identity);
            AssemblyDifferences(differs);
            AssemblyAnswers(answerOnAssembly, changedOnAssembly);
        }

        // ── the assembly (brief R23) ─────────────────────────────────────────
        // On an assembly the state writes neither a volume nor features, so
        // both read 0 before and after every plan: up to bridge 0.141.2 an
        // undo there pressed without seeing a component moved by hand. These
        // readings are shaped as AssemblyState writes them.

        private static Dictionary<string, object> Component(string uid, string name, double y, string axes)
        {
            var c = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["uid"] = uid, ["name"] = name, ["path"] = "C:\\x\\" + name + ".SLDPRT", ["state"] = "resolved",
                ["grounded"] = false, ["visible"] = true, ["transparent"] = false, ["axes"] = axes,
            };
            if (!double.IsNaN(y)) c["origin_mm"] = new object[] { 0.0, y, 0.0 };
            return c;
        }

        private static Dictionary<string, object> Mate(string uid, string name, bool suppressed, params string[] held)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["uid"] = uid, ["name"] = name, ["kind"] = "distance", ["suppressed"] = suppressed,
                ["components"] = new List<string>(held),
            };
        }

        private static Dictionary<string, object> AssemblyReading(object[] components, object[] mates)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["kind"] = "assembly", ["solid_body"] = false,
            };
            if (components != null) { d["components"] = components; d["how_many_components"] = components.Length; }
            if (mates != null) { d["mates_list"] = mates; d["mates_total"] = mates.Length; }
            return d;
        }

        private static void AssemblyDifferences(MethodInfo differs)
        {
            Func<Dictionary<string, object>, Dictionary<string, object>, List<string>> diff =
                (a, b) => (List<string>)differs.Invoke(null, new object[] { a, b });
            var a1 = Component("C1", "A-1", -5, "+X +Y +Z");
            var b1 = Component("C2", "B-1", 15, "+X -Y -Z");
            var m1 = Mate("M1", "Distance1", false, "C1", "C2");
            var plan = AssemblyReading(new object[] { a1, b1 }, new object[] { m1 });
            var missing = new List<string>();

            if (diff(plan, AssemblyReading(new object[] { a1, b1 }, new object[] { m1 })).Count != 0)
                missing.Add("two equal readings differ");
            var moved = diff(plan, AssemblyReading(new object[] { a1, Component("C2", "B-1", 40, "+X -Y -Z") }, new object[] { m1 }));
            if (moved.Count == 0 || moved.TrueForAll(s => s.IndexOf("moved", StringComparison.Ordinal) < 0))
                missing.Add("a component moved by 25 mm is not seen as moved");
            if (diff(plan, AssemblyReading(new object[] { a1, Component("C2", "B-1", 15.001, "+X -Y -Z") }, new object[] { m1 })).Count != 0)
                missing.Add("a last digit of the rounded origin reads as a move");
            if (diff(plan, AssemblyReading(new object[] { a1, Component("C2", "B-1", 15.002, "+X -Y -Z") }, new object[] { m1 })).Count == 0)
                missing.Add("a move of two thousandths of a mm is not seen");
            var turned = diff(plan, AssemblyReading(new object[] { a1, Component("C2", "B-1", 15, "+X +Y +Z") }, new object[] { m1 }));
            if (turned.Count == 0 || turned.TrueForAll(s => s.IndexOf("turned", StringComparison.Ordinal) < 0))
                missing.Add("a component turned over is not seen as turned");
            // A component inserted before the others renumbers the uids: the mate
            // still holds A-1 and B-1, and must not read as changed.
            var added = diff(plan, AssemblyReading(
                new object[] { Component("C1", "0-1", 0, "+X +Y +Z"), Component("C2", "A-1", -5, "+X +Y +Z"),
                               Component("C3", "B-1", 15, "+X -Y -Z") },
                new object[] { Mate("M1", "Distance1", false, "C2", "C3") }));
            if (added.TrueForAll(s => s.IndexOf("that were not there: «0-1»", StringComparison.Ordinal) < 0))
                missing.Add("a component added is not named");
            if (added.Exists(s => s.StartsWith("mate «", StringComparison.Ordinal)))
                missing.Add("renumbered uids read as a changed mate");
            if (diff(plan, AssemblyReading(new object[] { a1 }, new object[0])).Count == 0)
                missing.Add("a component and a mate taken away are not seen");
            if (diff(plan, AssemblyReading(new object[] { a1, b1 }, new object[] { m1, Mate("M2", "Coincident1", false, "C1", "C2") })).Count == 0)
                missing.Add("a mate added is not seen");
            if (diff(plan, AssemblyReading(new object[] { a1, b1 }, new object[] { Mate("M1", "Distance1", true, "C1", "C2") })).Count == 0)
                missing.Add("a mate suppressed is not seen");
            if (diff(plan, AssemblyReading(new object[] { a1, Component("C2", "B-1", double.NaN, "+X -Y -Z") }, new object[] { m1 })).Count == 0)
                missing.Add("an origin that cannot be read counts as the same place");
            if (diff(plan, AssemblyReading(null, new object[] { m1 })).Count == 0)
                missing.Add("components that cannot be read count as the same");
            if (diff(plan, AssemblyReading(new object[] { a1, b1 }, null)).Count == 0)
                missing.Add("mates that cannot be read count as the same");
            Check("WhatDiffers on an assembly sees components and mates come, go, move, turn and change, and renumbered uids as nothing (R23)",
                  missing.Count == 0, missing.Count == 0 ? "13 pairs of readings" : string.Join(" | ", missing));
        }

        private static void AssemblyAnswers(MethodInfo answer, MethodInfo changedText)
        {
            Func<List<string>, bool, string, List<string>, bool, Tuple<string, bool>> say = (taken, tried, error, still, onlyListed) =>
            {
                var args = new object[]
                {
                    2, 1, 2, 1, new List<string> { "«B-1» moved: origin [0, 15, 0] mm before, [0, 40, 0] mm now" },
                    taken, tried, error, still, onlyListed, false,
                };
                var text = (string)answer.Invoke(null, args);
                return Tuple.Create(text, (bool)args[10]);
            };
            var some = new List<string> { "«B-1» state: «resolved» before, «suppressed» now" };
            var none = new List<string>();
            var lies = new List<string>();
            var back = say(some, true, null, none, false);
            if (!back.Item2 || back.Item1.IndexOf("came back", StringComparison.Ordinal) < 0)
                lies.Add("a redo that brought the assembly back as before pressing is not said restored");
            var notBack = say(some, true, null, some, false);
            if (notBack.Item2 || notBack.Item1.IndexOf("NOT as it was", StringComparison.Ordinal) < 0)
                lies.Add("a redo after which the assembly still differs is said restored, or not said NOT");
            var threw = say(some, true, "SOLIDWORKS threw: x", none, false);
            if (threw.Item2 || threw.Item1.IndexOf("came back", StringComparison.Ordinal) >= 0)
                lies.Add("a redo that threw is said restored");
            var invisible = say(none, false, null, none, false);
            if (invisible.Item2 || invisible.Item1.IndexOf("did not press redo", StringComparison.Ordinal) < 0)
                lies.Add("a press that changed nothing visible is said restored, or the answer hides that no redo was pressed");
            var beyondList = say(none, false, null, none, true);
            if (beyondList.Item1.IndexOf("only by count", StringComparison.Ordinal) < 0)
                lies.Add("beyond the listed components and mates the answer still claims they were all compared");
            foreach (var t in new[] { back.Item1, notBack.Item1, invisible.Item1 })
                if (t.IndexOf("volume", StringComparison.Ordinal) >= 0 || t.IndexOf("the part", StringComparison.Ordinal) >= 0)
                {
                    lies.Add("the answer on an assembly speaks of a volume or of the part");
                    break;
                }
            if (back.Item1.IndexOf("«B-1» moved", StringComparison.Ordinal) < 0)
                lies.Add("the answer does not say how the assembly differs from before the plan");

            var changed = (string)changedText.Invoke(null, new object[] { new List<string> { "«B-1» moved" }, true });
            if (changed.IndexOf("«B-1» moved", StringComparison.Ordinal) < 0 ||
                changed.IndexOf("nothing was touched", StringComparison.Ordinal) < 0 ||
                changed.IndexOf("assembly changed", StringComparison.Ordinal) < 0 ||
                changed.IndexOf("only by count", StringComparison.Ordinal) < 0)
                lies.Add("the refusal on an assembly changed since the plan does not say what changed or what it compared");
            if (changed.IndexOf("delete_mate", StringComparison.Ordinal) < 0 ||
                changed.IndexOf("Edit > Undo", StringComparison.Ordinal) < 0 ||
                changed.IndexOf("delete_feature", StringComparison.Ordinal) >= 0)
                lies.Add("the refusal on an assembly offers the remedy of a part, or none");
            Check("on an assembly the answers after a wrong press and the refusal speak of components and mates, restored only from the comparison (R23)",
                  lies.Count == 0, lies.Count == 0 ? "5 outcomes and 1 refusal, each told as it is" : string.Join(" | ", lies));
        }

        /// <summary>One situation for MemoryAfterPlan, with what it must answer.</summary>
        private sealed class Situation
        {
            public string Label;
            public bool Built, Visible, Readable = true, HasMemory = true, Covered;
            public bool Reached = true, Stopped, Threw, TurnRefused;
            public bool? Undo;
            public bool Same = true, Measure, Equations;
            public string Expected;
            public bool ReasonExpected;
            public string ReasonMustSay;
        }

        private static void Memory(MethodInfo memory)
        {
            var situations = new List<Situation>
            {
                new Situation { Label = "a plan that never reached the executor", Reached = false, Expected = "keep" },
                new Situation { Label = "a plan stopped before writing", Stopped = true, Expected = "keep" },
                new Situation { Label = "another bridge held the turn on the memory's document (F4)", Reached = false,
                                TurnRefused = true, Expected = "cover", ReasonExpected = true, ReasonMustSay = "Another bridge" },
                new Situation { Label = "an executor that threw (F9)", Threw = true, Expected = "drop", ReasonExpected = true,
                                ReasonMustSay = "exception" },
                new Situation { Label = "a document not read after the plan", Readable = false, Undo = true, Expected = "drop",
                                ReasonExpected = true },
                new Situation { Label = "a plan that built", Built = true, Visible = true, Undo = true, Expected = "new" },
                new Situation { Label = "a plan that changed the equations (t14, UndoGaps)", Visible = true, Undo = false,
                                Equations = true, Expected = "drop", ReasonExpected = true, ReasonMustSay = "no equation in an undo entry" },
                new Situation { Label = "a plan that built and changed the equations", Built = true, Visible = true, Undo = true,
                                Equations = true, Expected = "drop", ReasonExpected = true, ReasonMustSay = "equation_before" },
                new Situation { Label = "no memory, a plan that changed the equations", HasMemory = false, Visible = true,
                                Undo = false, Equations = true, Expected = "drop", ReasonExpected = true,
                                ReasonMustSay = "parameters" },
                new Situation { Label = "a plan that built while its own entry closed empty (t13)", Built = true, Visible = true,
                                Undo = false, Expected = "drop", ReasonExpected = true, ReasonMustSay = "closed with nothing" },
                new Situation { Label = "no memory, a plan that built while its own entry closed empty", HasMemory = false,
                                Built = true, Visible = true, Undo = false, Expected = "drop", ReasonExpected = true,
                                ReasonMustSay = "closed with nothing" },
                new Situation { Label = "a reorder with its own non-empty entry (F1)", Visible = true, Undo = true, Expected = "new" },
                new Situation { Label = "an invisible plan with its own non-empty entry (F1)", Undo = true, Expected = "new" },
                new Situation { Label = "a measure with a non-empty entry on the same document", Undo = true, Measure = true,
                                Expected = "cover", ReasonExpected = true, ReasonMustSay = "undo entry" },
                new Situation { Label = "a measure that wrote nothing (F3)", Undo = false, Measure = true, Expected = "keep" },
                new Situation { Label = "a plan that wrote nothing (F3)", Undo = false, Expected = "keep" },
                new Situation { Label = "an answer false with a visible change", Undo = false, Visible = true, Expected = "drop",
                                ReasonExpected = true },
                new Situation { Label = "an unknown answer", Undo = null, Expected = "drop", ReasonExpected = true },
                new Situation { Label = "a plan on another document", Undo = false, Same = false, Expected = "drop",
                                ReasonExpected = true, ReasonMustSay = "another document" },
                new Situation { Label = "a covered memory under a plan that wrote nothing", Covered = true, Undo = false,
                                Expected = "keep" },
                new Situation { Label = "a covered memory under a measure with an entry", Covered = true, Undo = true,
                                Measure = true, Expected = "keep" },
                new Situation { Label = "no memory, a plan that wrote nothing (the reason stays)", HasMemory = false,
                                Undo = false, Expected = "keep" },
                new Situation { Label = "no memory, an unknown answer (F12: the reason is rewritten)", HasMemory = false,
                                Undo = null, Expected = "drop", ReasonExpected = true },
            };
            var wrong = new List<string>();
            foreach (var s in situations)
            {
                var args = new object[]
                {
                    s.Built, s.Visible, s.Readable, s.HasMemory, s.Covered, s.Reached, s.Stopped, s.Threw,
                    s.TurnRefused, s.Undo, s.Same, s.Measure, s.Equations, "Part1", null,
                };
                string action;
                try { action = (string)memory.Invoke(null, args); }
                catch (Exception ex) { wrong.Add(s.Label + ": threw " + (ex.InnerException ?? ex).Message); continue; }
                var reason = (string)args[14];
                if (action != s.Expected) { wrong.Add(s.Label + ": " + action + ", expected " + s.Expected); continue; }
                if (s.ReasonExpected && string.IsNullOrEmpty(reason)) wrong.Add(s.Label + ": no reason");
                if (!s.ReasonExpected && reason != null) wrong.Add(s.Label + ": a reason where none belongs");
                if (s.ReasonMustSay != null && (reason ?? "").IndexOf(s.ReasonMustSay, StringComparison.Ordinal) < 0)
                    wrong.Add(s.Label + ": the reason does not say «" + s.ReasonMustSay + "»");
            }
            Check("what a plan does to the memory: the last plan, kept when nothing was written, covered or dropped with a reason",
                  wrong.Count == 0, wrong.Count == 0 ? situations.Count + " situations, each as the rules say" : string.Join(" | ", wrong));
        }

        private static void Differences(MethodInfo differs)
        {
            Func<object, object[], Dictionary<string, object>> reading = (volume, features) =>
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                if (volume != null) d["volume_mm3"] = volume;
                d["feature"] = features;
                return d;
            };
            Func<string, int, Dictionary<string, object>> feature = (name, id) =>
                new Dictionary<string, object> { ["uid"] = name, ["feature_id"] = id };
            Func<Dictionary<string, object>, Dictionary<string, object>, List<string>> diff =
                (a, b) => (List<string>)differs.Invoke(null, new object[] { a, b });
            var ab = new object[] { feature("A", 1), feature("B", 2) };
            var ba = new object[] { feature("B", 2), feature("A", 1) };
            var renamed = new object[] { feature("A", 1), feature("C", 2) };
            var missing = new List<string>();
            if (diff(reading(1000.0, ab), reading(1000.0, ab)).Count != 0) missing.Add("two equal readings differ");
            if (diff(reading(1000.0, ab), reading(900.0, ab)).Count == 0) missing.Add("a changed volume is not seen");
            if (diff(reading(1000.0, ab), reading(null, ab)).Count == 0) missing.Add("an unreadable volume counts as the same");
            if (diff(reading(1000.0, ab), reading(1000.0, new object[] { feature("A", 1) })).Count == 0) missing.Add("a feature less is not seen");
            var order = diff(reading(1000.0, ab), reading(1000.0, ba));
            if (order.Count == 0 || order.TrueForAll(s => s.IndexOf("order", StringComparison.Ordinal) < 0)) missing.Add("a reorder is not seen as one");
            if (diff(reading(1000.0, ab), reading(1000.0, renamed)).Count == 0) missing.Add("a rename is not seen");
            var withEquation = reading(1000.0, ab);
            withEquation["equations"] = new object[] { new Dictionary<string, object> { ["equation"] = "\"wall\" = 12mm" } };
            var equationChanged = reading(1000.0, ab);
            equationChanged["equations"] = new object[] { new Dictionary<string, object> { ["equation"] = "\"wall\" = 15mm" } };
            if (diff(reading(1000.0, ab), withEquation).Count == 0) missing.Add("an equation added is not seen");
            if (diff(withEquation, equationChanged).Count == 0) missing.Add("an equation rewritten is not seen");
            if (diff(withEquation, withEquation).Count != 0) missing.Add("the same equations differ");
            Check("WhatDiffers sees volume, count, names, order and equations, and an unreadable volume as a difference",
                  missing.Count == 0, missing.Count == 0 ? "9 pairs of readings" : string.Join(" | ", missing));
        }

        private static void Answers(MethodInfo answer)
        {
            Func<List<string>, bool, string, List<string>, bool, Tuple<string, bool>> say = (taken, tried, error, still, onlyListed) =>
            {
                var args = new object[]
                {
                    1000.0, 3, 1000.0, 3, new List<string> { "the order of the features changed" },
                    taken, tried, error, still, onlyListed, false,
                };
                var text = (string)answer.Invoke(null, args);
                return Tuple.Create(text, (bool)args[10]);
            };
            var some = new List<string> { "the order of the features changed" };
            var none = new List<string>();
            var lies = new List<string>();
            var back = say(some, true, null, none, false);
            if (!back.Item2 || back.Item1.IndexOf("came back", StringComparison.Ordinal) < 0)
                lies.Add("a redo that brought the part back as before pressing is not said restored");
            var notBack = say(some, true, null, new List<string> { "the order of the features changed" }, false);
            if (notBack.Item2 || notBack.Item1.IndexOf("NOT as it was", StringComparison.Ordinal) < 0 ||
                notBack.Item1.IndexOf("came back", StringComparison.Ordinal) >= 0)
                lies.Add("a redo after which the part still differs is said restored, or not said NOT");
            var threw = say(some, true, "SOLIDWORKS threw: x", none, false);
            if (threw.Item2 || threw.Item1.IndexOf("came back", StringComparison.Ordinal) >= 0)
                lies.Add("a redo that threw is said restored");
            var invisible = say(none, false, null, none, false);
            if (invisible.Item2 || invisible.Item1.IndexOf("did not press redo", StringComparison.Ordinal) < 0)
                lies.Add("a press that changed nothing visible is said restored, or the answer hides that no redo was pressed");
            var nothingTaken = say(none, true, null, none, false);
            if (nothingTaken.Item2 || nothingTaken.Item1.IndexOf("came back", StringComparison.Ordinal) >= 0)
                lies.Add("restored claimed although the press took away nothing visible");
            var beyondCap = say(none, false, null, none, true);
            if (beyondCap.Item1.IndexOf("only by count", StringComparison.Ordinal) < 0)
                lies.Add("beyond the feature list the answer still claims names and order were compared (F16)");
            if (back.Item1.IndexOf("the order of the features changed", StringComparison.Ordinal) < 0)
                lies.Add("the answer does not say how the part differs from before the plan");
            Check("after a wrong press the answer says restored only from the comparison, NOT otherwise, and what it compared",
                  lies.Count == 0, lies.Count == 0 ? "7 outcomes, each told as it is" : string.Join(" | ", lies));
        }

        private static void Refusals(MethodInfo refusal, MethodInfo changedText)
        {
            var problems = new List<string>();
            var recorded = (string)refusal.Invoke(null, new object[] { "The last measure left its undo entry on top.", true });
            var unrecorded = (string)refusal.Invoke(null, new object[] { "An action is on top.", false });
            foreach (var t in new[] { recorded, unrecorded })
            {
                if (t.IndexOf("nothing was touched", StringComparison.Ordinal) < 0) problems.Add("a refusal does not say nothing was touched");
                if (t.IndexOf("delete_feature", StringComparison.Ordinal) < 0 ||
                    t.IndexOf("Edit > Undo", StringComparison.Ordinal) < 0)
                    problems.Add("a refusal has no remedy for a model and for the person at the screen");
            }
            if (recorded.IndexOf("left its undo entry on top", StringComparison.Ordinal) < 0) problems.Add("the reason is not in the text");
            if (recorded.IndexOf("lost for good", StringComparison.Ordinal) < 0) problems.Add("a recorded entry is not said lost to a press");
            if (unrecorded.IndexOf("lost for good", StringComparison.Ordinal) >= 0 ||
                unrecorded.IndexOf("can put back", StringComparison.Ordinal) < 0)
                problems.Add("an action that was not recorded is said lost for good (F10)");
            var changed = (string)changedText.Invoke(null, new object[] { new List<string> { "the order of the features changed" }, true });
            if (changed.IndexOf("the order of the features changed", StringComparison.Ordinal) < 0 ||
                changed.IndexOf("nothing was touched", StringComparison.Ordinal) < 0 ||
                changed.IndexOf("delete_feature", StringComparison.Ordinal) < 0 ||
                changed.IndexOf("only by count", StringComparison.Ordinal) < 0)
                problems.Add("the refusal on a part changed since the plan does not say what changed, what it compared, or what to do");
            Check("the refusals before pressing say why, what a press would do, and what to do instead",
                  problems.Count == 0, problems.Count == 0 ? Truncated(recorded) : string.Join(" | ", problems));
        }

        private static void NoteAndIdentity(MethodInfo note, MethodInfo identity)
        {
            var problems = new List<string>();
            var ours = (string)note.Invoke(null, new object[] { "Part1", "Drawing1 - Sheet1", true });
            var foreign = (string)note.Invoke(null, new object[] { "Part1", "Part2", false });
            if (ours.IndexOf("works on", StringComparison.Ordinal) < 0 ||
                ours.IndexOf("«activate»", StringComparison.Ordinal) < 0)
                problems.Add("the note after create_drawing does not say the drawing is next and how to come back");
            if (foreign.IndexOf("works on", StringComparison.Ordinal) >= 0 ||
                foreign.IndexOf("inspect_part", StringComparison.Ordinal) < 0)
                problems.Add("the note for a window that came from outside offers it to the next plan (F5)");
            Func<Dictionary<string, object>, string> id = d => (string)identity.Invoke(null, new object[] { d });
            if (id(new Dictionary<string, object> { ["document_title"] = "Part1", ["path"] = "C:\\x\\Part1.SLDPRT" }) != "C:\\x\\Part1.SLDPRT")
                problems.Add("the path does not win in the identity for the executor");
            if (id(new Dictionary<string, object> { ["document_title"] = "Part1", ["path"] = "" }) != "title:Part1")
                problems.Add("a document never saved is not its title with the executor's prefix");
            if (id(new Dictionary<string, object> { ["document_title"] = "", ["path"] = "" }) != null)
                problems.Add("an empty identity is not null");
            Check("the note on the document in front and the identity handed to the executor (F5, F7)",
                  problems.Count == 0, problems.Count == 0 ? "2 notes, 3 identities" : string.Join(" | ", problems));
        }

        // ── the sources ──────────────────────────────────────────────────────

        private static void Sources()
        {
            string tools, executor, session;
            try
            {
                tools = File.ReadAllText(Path.Combine(Root(), "Tools", "McpTools.cs")).Replace("\r\n", "\n");
                executor = File.ReadAllText(Path.Combine(Root(), "Oce.Solid.Agent.Core", "PlanExecutor.cs")).Replace("\r\n", "\n");
                session = File.ReadAllText(Path.Combine(Root(), "Bridge", "Session.cs")).Replace("\r\n", "\n");
            }
            catch (Exception ex)
            {
                Check("the tools, executor and session sources can be read", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            var afterPlan = AfterPlanProblems(tools);
            Check("after a plan: the plan's document read, the window in front adopted only when the plan ended on it, one document for the resent-plan guard",
                  afterPlan.Count == 0,
                  afterPlan.Count == 0
                      ? "PlanDocument kept; StateOfDocument when it is not in front; memory and volume_after_mm3 on planStateAfter; " +
                        "state_after the document in front; document_in_front answered; the executor given the checked identity; " +
                        "signatures only when frontIsThePlans; the memory through MemoryAfterPlan with the reference; " +
                        "the resent-plan guard on planIdentity and planStateAfter"
                      : string.Join(" | ", afterPlan));
            var afterPlanRed = SeenRed("after plan", tools, AfterPlanProblems, new[]
            {
                Change("the memory's volume read elsewhere", "NumberOf(planStateAfter, \"volume_mm3\")", "NumberOf(frontState, \"volume_mm3\")"),
                Change("the memory's count read elsewhere", "= HowManyFeatures(planStateAfter);", "= HowManyFeatures(frontState);"),
                Change("the plan's document never read", "StateOfDocument(planDocumentHeld", "StateOfDocument(frontDocument"),
                Change("volume_after_mm3 of the window in front", "[\"volume_after_mm3\"] = Volume(planStateAfter)", "[\"volume_after_mm3\"] = Volume(frontState)"),
                Change("the document in front not said", "[\"document_in_front\"] = ", "[\"elsewhere\"] = "),
                Change("state_after turned into the plan's document", "[\"state_after\"] = ", "[\"state_after\"] = planStateAfter ?? "),
                Change("the executor left to pick the document (F7)", "IdentityForTheExecutor(now));", "null);"),
                Change("a foreign window adopted (F5)", " != null && frontIsThePlans)", " != null)"),
                Change("the end document not asked (F5)", "frontIsThePlans = _session.IsInFront(finalDocument)", "frontIsThePlans = true"),
                Change("the resent-plan summary of the window in front (F6)", "_summaryAfterPlan = StateSummary(planStateAfter);",
                       "_summaryAfterPlan = StateSummary(frontState);"),
                Change("the resent-plan document read before the plan (F6)", "_planDocument = planIdentity;", "_planDocument = frontIdentity;"),
                Change("the memory without its reference (F11)", "DocumentRef = planDocumentHeld,", "DocumentRef = null,"),
                Change("the memory decided by hand", "= MemoryAfterPlan(", "= MemoryAfterPlanByHand("),
                Change("the window of a refused plan taken for its document", " && !planRun.StoppedBeforeWriting;", ";"),
                Change("an assembly never counted as built (R23)", "AssemblyBuilt(snapshotBefore, snapshotAfter)", "false"),
                Change("N11: the refusal of undo said only by the undo", "if (memoryAction == MemoryDrop && (somethingToWithdraw || drawingReason))", "if (false)"),
                Change("N12: a stopped plan silent about what stays", "result[\"note_stopped\"] =", "result[\"note_quiet\"] ="),
                Change("N24: a plan on a drawing gives the undo no reason", "memoryReason = onDrawing;", "memoryReason = null;"),
                Change("N24: the drawing remedy never chosen", "_noMemoryOnDrawing = onDrawing != null && memoryReason == onDrawing;", "_noMemoryOnDrawing = false;"),
            });
            Check("the after-plan check turns red on each changed copy", afterPlanRed.Count == 0,
                  afterPlanRed.Count == 0 ? "19 changes, each one red" : string.Join(" | ", afterPlanRed));

            var front = PlanFrontProblems(tools);
            Check("before a plan: no part looked at is refused before the reading (N17), an exchange the assembly cannot have is refused before any step (N18)",
                  front.Count == 0,
                  front.Count == 0
                      ? "NotLookedAtYet(frontNow) before StateWithoutAnchor; AssemblyExchangeRefusal in the pre-check of the steps, " +
                        "only while the plan is still on the assembly"
                      : string.Join(" | ", front));
            var frontRed = SeenRed("before a plan", tools, PlanFrontProblems, new[]
            {
                Change("N17: the early refusal taken out", "return Protocol.Content(NotLookedAtYet(frontNow), true);", "frontNow = null;"),
                Change("N18: the exchange refusal not asked in the pre-check", "SwOperations.AssemblyExchangeRefusal(", "SwOperations.AssemblyExchangeIgnored("),
                Change("N18: a drawing ahead not noted", "if (itemName == \"create_drawing\") anotherDocumentAhead = true;", ""),
            });
            Check("the before-a-plan check turns red on each changed copy", frontRed.Count == 0,
                  frontRed.Count == 0 ? "3 changes, each one red" : string.Join(" | ", frontRed));

            var undo = UndoProblems(tools);
            Check("`undo` refuses before pressing when covered or changed since the plan, judges the press by the part before the plan, redoes only after a visible change",
                  undo.Count == 0,
                  undo.Count == 0
                      ? "the document by reference; RefusalCovered and RefusalChangedSinceThePlan before the press; " +
                        "withdrawn only if SnapshotDiffers(before, back) is empty; one redo inside if (takenAway.Count > 0), " +
                        "followed by a new reading and a second WhatDiffers; the answer from AnswerAfterWrongPress"
                      : string.Join(" | ", undo));
            var undoRed = SeenRed("undo", tools, UndoProblems, new[]
            {
                Change("the covered memory pressed", ".CoveredBecause != null)", ".CoveredBecause == \"never\")"),
                Change("a blind redo", "if (takenAway.Count > 0)", "if (takenAway != null)"),
                Change("no new reading after the redo", "= _session.State(out _);\n                }", ";\n                }"),
                Change("the answer not from the comparison", "= answerText,", "= \"put back\","),
                Change("names and order not compared before pressing (F1, F14)", "var changedSince = SnapshotDiffers(after, now);",
                       "var changedSince = new List<string>();"),
                Change("withdrawn judged by two numbers only", "var notAsBeforeThePlan = SnapshotDiffers(before, back);",
                       "var notAsBeforeThePlan = new List<string>();"),
                Change("the document compared by name (F11)", "? _session.IsInFront(memory.DocumentRef)", "? true"),
                Change("an assembly pressed without its lists (R23)",
                       "if (after.Assembly && !(now.AssemblyRead && after.AssemblyRead))", "if (false)"),
                Change("an assembly judged without its lists after the press (R23)",
                       "if (before.Assembly && !(back.AssemblyRead && before.AssemblyRead))", "if (false)"),
                Change("a refused plan said withdrawn whole (report 8cd843 N10)",
                       "outside[\"note\"] = memory.Refused", "outside[\"note\"] = false"),
                Change("delete_feature offered after a plan on a drawing (report 8cd843 N24)",
                       "(_noMemoryOnDrawing && _noMemoryBecause != null ? DrawingNoMemoryRemedy : NoMemoryRemedy)", "NoMemoryRemedy"),
            });
            Check("the `undo` check turns red on each changed copy", undoRed.Count == 0,
                  undoRed.Count == 0 ? "11 changes, each one red" : string.Join(" | ", undoRed));

            var finish = ExecutorProblems(executor);
            Check("the executor hands on what FinishRecordingUndoObject answered for the plan's document",
                  finish.Count == 0,
                  finish.Count == 0
                      ? "FinishUndoRecording returns bool?; both closings go through NoteUndoAnswer; PlanDocument set at the start"
                      : string.Join(" | ", finish));
            var finishRed = SeenRed("executor", executor, ExecutorProblems, new[]
            {
                Change("the answer thrown away", "private bool? FinishUndoRecording(", "private void FinishUndoRecording("),
                Change("the plan document not handed on", ".PlanDocument = planDocument;", ".FinalDocument = planDocument;"),
                Change("a closing not noted", "FinishUndoRecording(undoRecordedOn));\n                    if (planDocumentReopened)",
                       "FinishUndoRecording(undoRecordedOn));\n                    FinishUndoRecording(undoRecordedOn);\n                    if (planDocumentReopened)"),
                Change("N12: «I touched nothing» after steps that ran", "in front. \" + (result.ExecutedSteps == 0", "in front. \" + (true"),
                Change("N13: the identity key in the message", "\"the plan was for «\" + ReadableIdentity(expectedPath)", "\"the plan was for «\" + expectedPath"),
            });
            Check("the executor check turns red on each changed copy", finishRed.Count == 0,
                  finishRed.Count == 0 ? "5 changes, each one red" : string.Join(" | ", finishRed));

            var sess = SessionProblems(session);
            Check("the session hands the checked identity to the executor, says when it threw and when another bridge held the turn",
                  sess.Count == 0,
                  sess.Count == 0 ? "RunAsync(expectedDocument); LastPlanExecutorThrew set around it; LastTurnRefusedDocument on a refused turn"
                                  : string.Join(" | ", sess));
            var sessRed = SeenRed("session", session, SessionProblems, new[]
            {
                Change("the executor picks the document (F7)", "(string.IsNullOrEmpty(expectedDocument) ? null : expectedDocument)", "(null)"),
                Change("a throw not said (F9)", "LastPlanExecutorThrew = true;\n", "\n"),
                Change("another bridge's turn not said (F4)", "LastTurnRefusedDocument = doc;", "LastTurnRefusedDocument = null;"),
            });
            Check("the session check turns red on each changed copy", sessRed.Count == 0,
                  sessRed.Count == 0 ? "3 changes, each one red" : string.Join(" | ", sessRed));
        }

        /// <summary>
        /// Report 8cd843, round 6, in the plan tool; empty when nothing is wrong.
        /// N17: a plan sent before any «inspect_part» is refused before the long
        /// reading of the document, not after it. N18: an IGES of an assembly, or
        /// a STEP with reopen_and_compare, is refused before the steps run, not
        /// after the steps before it changed the assembly.
        /// </summary>
        internal static List<string> PlanFrontProblems(string source)
        {
            var problems = new List<string>();
            var code = CodeOnly(source);
            string plan = MemberBody(code, "private Dictionary<string, object> RunPlan(");
            if (plan == null) { problems.Add("the body of the plan is not in the source"); return problems; }
            int early = plan.IndexOf("if (_seen == null && string.IsNullOrWhiteSpace(Text(arguments, \"document\")))", StringComparison.Ordinal);
            int earlyRefusal = early < 0 ? -1 : plan.IndexOf("return Protocol.Content(NotLookedAtYet(frontNow), true);", early, StringComparison.Ordinal);
            int reading = plan.IndexOf("var before = _session.StateWithoutAnchor(", StringComparison.Ordinal);
            if (earlyRefusal < 0 || reading < 0 || earlyRefusal > reading)
                problems.Add("a plan with no part looked at is refused only after the reading of the document (report 8cd843 N17)");
            int guard = plan.IndexOf("if (startKind == \"assembly\" && !anotherDocumentAhead)", StringComparison.Ordinal);
            int exchange = guard < 0 ? -1 : plan.IndexOf("SwOperations.AssemblyExchangeRefusal(", guard, StringComparison.Ordinal);
            int drawing = plan.IndexOf("if (itemName == \"create_drawing\") anotherDocumentAhead = true;", StringComparison.Ordinal);
            if (exchange < 0 || drawing < 0 || drawing < exchange)
                problems.Add("an IGES or a reopened STEP of an assembly is refused only after the steps before it ran (report 8cd843 N18)");
            return problems;
        }

        /// <summary>What is wrong with the reading after a plan; empty when nothing.</summary>
        internal static List<string> AfterPlanProblems(string source)
        {
            var problems = new List<string>();
            var code = CodeOnly(source);
            string plan = MemberBody(code, "private Dictionary<string, object> RunPlan(");
            if (plan == null) { problems.Add("the body of the plan is not in the source"); return problems; }
            if (plan.IndexOf("LastPlanResult;", StringComparison.Ordinal) < 0)
                problems.Add("the result of the executor is not read after the plan");
            if (plan.IndexOf("? planRun.PlanDocument : null;", StringComparison.Ordinal) < 0)
                problems.Add("the plan's document (PlanDocument) is not used");
            if (plan.IndexOf("wroteOnADocument = planRun != null && !planRun.StoppedBeforeWriting;", StringComparison.Ordinal) < 0)
                problems.Add("a plan stopped before writing can make the window in front the plan's document (F5, F7)");
            if (plan.IndexOf("StateOfDocument(planDocumentHeld", StringComparison.Ordinal) < 0)
                problems.Add("the plan's document is not read when another one is in front");
            if (plan.IndexOf("NumberOf(planStateAfter, \"volume_mm3\")", StringComparison.Ordinal) < 0)
                problems.Add("the memory's volume after the plan is not read on the plan's document");
            if (plan.IndexOf("= HowManyFeatures(planStateAfter);", StringComparison.Ordinal) < 0)
                problems.Add("the memory's feature count after the plan is not read on the plan's document");
            if (plan.IndexOf("[\"volume_after_mm3\"] = Volume(planStateAfter)", StringComparison.Ordinal) < 0)
                problems.Add("volume_after_mm3 is not the plan document's");
            if (plan.IndexOf("[\"document_in_front\"] = ", StringComparison.Ordinal) < 0)
                problems.Add("the answer does not say which document is in front");
            if (plan.IndexOf("[\"state_after\"] = planStateAfter", StringComparison.Ordinal) >= 0)
                problems.Add("state_after describes the plan's document, not the one the next plan works on");
            if (plan.IndexOf("IdentityForTheExecutor(now));", StringComparison.Ordinal) < 0)
                problems.Add("the executor is not given the identity checked above (F7)");
            if (plan.IndexOf(" != null && frontIsThePlans)", StringComparison.Ordinal) < 0)
                problems.Add("the window in front is adopted whether or not the plan ended on it (F5)");
            if (plan.IndexOf("frontIsThePlans = _session.IsInFront(finalDocument)", StringComparison.Ordinal) < 0)
                problems.Add("whether the plan ended on the window in front is not asked of its end document (F5)");
            if (plan.IndexOf("_summaryAfterPlan = StateSummary(planStateAfter);", StringComparison.Ordinal) < 0 ||
                plan.IndexOf("_planDocument = planIdentity;", StringComparison.Ordinal) < 0)
                problems.Add("the resent-plan guard does not read one document, the plan's (F6)");
            if (plan.IndexOf("DocumentRef = planDocumentHeld,", StringComparison.Ordinal) < 0)
                problems.Add("the memory does not hold the plan's document by reference (F11)");
            if (plan.IndexOf("= MemoryAfterPlan(", StringComparison.Ordinal) < 0)
                problems.Add("the memory is not decided by MemoryAfterPlan");
            if (plan.IndexOf("AssemblyBuilt(snapshotBefore, snapshotAfter)", StringComparison.Ordinal) < 0)
                problems.Add("a plan that changed the components or mates of an assembly is never counted as built (R23)");
            // Report 8cd843 N11 and N12 (27/10/2026): «undo_entry_recorded» true
            // and then an undo that refused; a stop that said «I touched nothing»
            // after two cuts.
            if (plan.IndexOf("if (memoryAction == MemoryDrop && (somethingToWithdraw || drawingReason))", StringComparison.Ordinal) < 0 ||
                plan.IndexOf("result[\"note_undo\"] =", StringComparison.Ordinal) < 0)
                problems.Add("a plan that «undo» will refuse does not say so in its own answer (report 8cd843 N11)");
            // Report 8cd843 N24 (round 7): three dimensions on a drawing, then «undo»
            // said «I have not yet run any that built something».
            int drawingReason = plan.IndexOf("DrawingPlanMemoryReason(", StringComparison.Ordinal);
            int drawingDrop = plan.IndexOf("memoryReason = onDrawing;", StringComparison.Ordinal);
            int drawingSwitch = plan.IndexOf("switch (memoryAction)", StringComparison.Ordinal);
            if (drawingReason < 0 || drawingDrop < drawingReason || drawingSwitch < drawingDrop ||
                plan.IndexOf("_noMemoryOnDrawing = onDrawing != null && memoryReason == onDrawing;", StringComparison.Ordinal) < 0)
                problems.Add("a plan on a drawing leaves the undo without its reason, or the reason is set after the memory is decided (report 8cd843 N24)");
            if (plan.IndexOf("planRun.ExecutedSteps > 0", StringComparison.Ordinal) < 0 ||
                plan.IndexOf("result[\"note_stopped\"] =", StringComparison.Ordinal) < 0)
                problems.Add("a plan stopped halfway does not say which steps stay (report 8cd843 N12)");
            return problems;
        }

        /// <summary>What is wrong with the undo tool; empty when nothing.</summary>
        internal static List<string> UndoProblems(string source)
        {
            var problems = new List<string>();
            var code = CodeOnly(source);
            string undo = MemberAround(code, "return RefusalCovered(");
            if (undo == null) { problems.Add("the member that refuses a covered memory is not in the source"); return problems; }

            int covered = undo.IndexOf(".CoveredBecause != null)", StringComparison.Ordinal);
            int refusal = undo.IndexOf("return RefusalCovered(", StringComparison.Ordinal);
            int press = undo.IndexOf("= _session.StepBack();", StringComparison.Ordinal);
            if (covered < 0 || refusal < covered || press < 0 || refusal > press)
                problems.Add("a covered memory is not refused before the press");

            if (undo.IndexOf("? _session.IsInFront(memory.DocumentRef)", StringComparison.Ordinal) < 0)
                problems.Add("the document is not compared by its reference (F11)");
            if (undo.IndexOf("(_noMemoryOnDrawing && _noMemoryBecause != null ? DrawingNoMemoryRemedy : NoMemoryRemedy)", StringComparison.Ordinal) < 0)
                problems.Add("after a plan on a drawing the undo offers delete_feature, which does not take away a dimension (report 8cd843 N24)");

            int changedSince = undo.IndexOf("var changedSince = SnapshotDiffers(after, now);", StringComparison.Ordinal);
            int changedRefusal = undo.IndexOf("return RefusalChangedSinceThePlan(", StringComparison.Ordinal);
            if (changedSince < 0 || changedRefusal < changedSince || changedRefusal > press)
                problems.Add("names and order are not compared with how the plan left the part before the press (F1, F14)");

            int judged = undo.IndexOf("var notAsBeforeThePlan = SnapshotDiffers(before, back);", StringComparison.Ordinal);
            int branch = undo.IndexOf("if (notAsBeforeThePlan.Count > 0)", StringComparison.Ordinal);
            if (judged < press || branch < judged)
                problems.Add("«withdrawn» is not judged by the whole snapshot before the plan, after the press");

            // R23: on an assembly the volume reads 0 on both sides, so the lists
            // of components and mates take its place in both guards.
            int assemblyBefore = undo.IndexOf("if (after.Assembly && !(now.AssemblyRead && after.AssemblyRead))", StringComparison.Ordinal);
            if (assemblyBefore < 0 || assemblyBefore > press)
                problems.Add("an assembly whose components and mates cannot be read is pressed (R23)");
            int assemblyAfter = undo.IndexOf("if (before.Assembly && !(back.AssemblyRead && before.AssemblyRead))", StringComparison.Ordinal);
            if (assemblyAfter < press || assemblyAfter > judged)
                problems.Add("after the press an assembly whose lists cannot be read is judged, and maybe redone (R23)");

            int redos = Count(undo, "= _session.StepForward();");
            if (redos != 1) problems.Add(redos + " redo calls, expected 1");
            int guard = undo.IndexOf("if (takenAway.Count > 0)", StringComparison.Ordinal);
            int close = guard < 0 ? -1 : undo.IndexOf("\n                }", guard, StringComparison.Ordinal);
            string block = close < 0 ? "" : undo.Substring(guard, close - guard + 1);
            int redo = block.IndexOf("= _session.StepForward();", StringComparison.Ordinal);
            int reread = block.IndexOf("= _session.State(out _);", StringComparison.Ordinal);
            if (redo < 0 || reread < redo)
                problems.Add("the redo is not inside if (takenAway.Count > 0) with a new reading right after it");
            int firstCompare = undo.IndexOf("var takenAway = WhatDiffers(", StringComparison.Ordinal);
            if (firstCompare < 0 || guard < 0 || firstCompare > guard)
                problems.Add("what the press took away is not compared with the reading before it, ahead of the redo");
            if (Count(undo, "WhatDiffers(") != 2 || undo.LastIndexOf("WhatDiffers(", StringComparison.Ordinal) < close)
                problems.Add("the part after the redo is not compared again");
            if (undo.IndexOf("= AnswerAfterWrongPress(", StringComparison.Ordinal) < 0 ||
                undo.IndexOf("= answerText,", StringComparison.Ordinal) < 0)
                problems.Add("the answer after a wrong press does not come from AnswerAfterWrongPress");
            // Report 8cd843 N10 (27/10/2026): the undo of a refused plan said
            // «withdrawn whole ... went back» next to «took away only its undo entry».
            int note = undo.IndexOf("outside[\"note\"] = memory.Refused", StringComparison.Ordinal);
            int whole = undo.IndexOf("The plan was withdrawn whole", StringComparison.Ordinal);
            if (note < 0 || whole < note)
                problems.Add("the note of a successful undo does not depend on whether the plan was refused (report 8cd843 N10)");
            return problems;
        }

        /// <summary>What is wrong with the executor's undo answer; empty when nothing.</summary>
        internal static List<string> ExecutorProblems(string source)
        {
            var problems = new List<string>();
            var code = CodeOnly(source);
            if (code.IndexOf("private bool? FinishUndoRecording(", StringComparison.Ordinal) < 0)
                problems.Add("FinishUndoRecording does not return the answer (bool?)");
            int finishes = Count(code, "FinishUndoRecording(undoRecordedOn)");
            int noted = Regex.Matches(code, @"NoteUndoAnswer\(\s*\w+,\s*undoRecordedOn,\s*planDocument,\s*FinishUndoRecording\(undoRecordedOn\)\)").Count;
            if (finishes != 2 || noted != 2)
                problems.Add(finishes + " closings of the recording, " + noted + " of them noted: expected 2 and 2");
            if (code.IndexOf(".PlanDocument = planDocument;", StringComparison.Ordinal) < 0)
                problems.Add("the plan's document is not handed on in the result");
            // Report 8cd843 N12: «I touched nothing» only when no step ran yet.
            int changed = code.IndexOf("\"The active document changed while I was running: \"", StringComparison.Ordinal);
            int guard = changed < 0 ? -1 : code.IndexOf("result.ExecutedSteps == 0", changed, StringComparison.Ordinal);
            if (changed < 0 || guard < 0 || guard - changed > 600)
                problems.Add("the stop for a window that changed says «I touched nothing» after steps that ran (report 8cd843 N12)");
            // Report 8cd843 N13: «title:Parte354» in that message, the key of the comparisons.
            int readable = changed < 0 ? -1 : code.IndexOf("ReadableIdentity(expectedPath)", changed, StringComparison.Ordinal);
            if (changed < 0 || readable < 0 || readable - changed > 300)
                problems.Add("the stop for a window that changed prints the identity key («title:...»), not the name (report 8cd843 N13)");
            return problems;
        }

        /// <summary>What is wrong with the session's plan call; empty when nothing.</summary>
        internal static List<string> SessionProblems(string source)
        {
            var problems = new List<string>();
            var code = CodeOnly(source);
            const string handedOn = ".RunAsync(string.IsNullOrEmpty(expectedDocument) ? null : expectedDocument)";
            if (code.IndexOf(handedOn, StringComparison.Ordinal) < 0 || code.IndexOf("Async(null)", StringComparison.Ordinal) >= 0)
                problems.Add("the executor is not handed the identity the caller checked (F7)");
            int threwTrue = code.IndexOf("LastPlanExecutorThrew = true;", StringComparison.Ordinal);
            int call = code.IndexOf(handedOn, StringComparison.Ordinal);
            int threwFalse = call < 0 ? -1 : code.IndexOf("LastPlanExecutorThrew = false;", call, StringComparison.Ordinal);
            if (threwTrue < 0 || call < threwTrue || threwFalse < 0)
                problems.Add("a throw of the executor is not told apart from a result (F9)");
            int turn = code.IndexOf("DocumentTurn.Take(", StringComparison.Ordinal);
            int said = code.IndexOf("LastTurnRefusedDocument = doc;", StringComparison.Ordinal);
            if (turn < 0 || said < turn)
                problems.Add("a turn refused because another bridge holds the document is not said (F4)");
            return problems;
        }

        // ── seen red ─────────────────────────────────────────────────────────

        private sealed class SourceChange
        {
            public string Label, From, To;
        }

        private static SourceChange Change(string label, string from, string to)
        {
            return new SourceChange { Label = label, From = from, To = to };
        }

        /// <summary>
        /// Each change applied alone to the source must leave text that the check
        /// calls wrong. Returns what did not: a change whose anchor is no longer
        /// in the source, or a changed copy the check still calls right. The
        /// source arrives with LF line ends, so an anchor written with "\n"
        /// matches.
        /// </summary>
        private static List<string> SeenRed(string what, string source, Func<string, List<string>> check, SourceChange[] changes)
        {
            var misses = new List<string>();
            if (check(source).Count != 0) { misses.Add(what + ": the untouched source is already red, the changes prove nothing"); return misses; }
            foreach (var c in changes)
            {
                int at = source.IndexOf(c.From, StringComparison.Ordinal);
                if (at < 0) { misses.Add(what + ", " + c.Label + ": anchor not found in the source (update the change)"); continue; }
                string changed = source.Substring(0, at) + c.To + source.Substring(at + c.From.Length);
                if (check(changed).Count == 0) misses.Add(what + ", " + c.Label + ": the changed copy is still green");
            }
            return misses;
        }

        // ── source helpers ───────────────────────────────────────────────────

        /// <summary>
        /// The source without its comment lines and trailing line comments, so
        /// that a call named in a comment is not counted. String literals are
        /// kept.
        /// </summary>
        private static string CodeOnly(string source)
        {
            var lines = source.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) { lines[i] = ""; continue; }
                int cut = TrailingComment(lines[i]);
                if (cut >= 0) lines[i] = lines[i].Substring(0, cut);
            }
            return string.Join("\n", lines);
        }

        /// <summary>Where a "//" line comment starts outside string literals, or -1.</summary>
        private static int TrailingComment(string line)
        {
            bool inString = false, verbatim = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inString)
                {
                    if (verbatim) { if (ch == '"') { if (i + 1 < line.Length && line[i + 1] == '"') i++; else inString = false; } }
                    else if (ch == '\\') i++;
                    else if (ch == '"') inString = false;
                    continue;
                }
                if (ch == '\'' && i + 2 < line.Length) { i += line[i + 1] == '\\' ? 3 : 2; continue; }
                if (ch == '"') { inString = true; verbatim = i > 0 && line[i - 1] == '@'; continue; }
                if (ch == '/' && i + 1 < line.Length && line[i + 1] == '/') return i;
            }
            return -1;
        }

        /// <summary>
        /// The text of the member that starts at <paramref name="header"/>, up
        /// to its closing brace at the indentation of a class member (eight
        /// spaces); null when the header is not there.
        /// </summary>
        private static string MemberBody(string code, string header)
        {
            int start = code.IndexOf(header, StringComparison.Ordinal);
            if (start < 0) return null;
            int end = code.IndexOf("\n        }", start, StringComparison.Ordinal);
            return end < 0 ? code.Substring(start) : code.Substring(start, end - start);
        }

        /// <summary>
        /// The text of the member that contains <paramref name="inside"/>: from
        /// the last line at member indentation that opens a private member
        /// before it, to the closing brace at member indentation after it.
        /// </summary>
        private static string MemberAround(string code, string inside)
        {
            int at = code.IndexOf(inside, StringComparison.Ordinal);
            if (at < 0) return null;
            int start = code.LastIndexOf("\n        private ", at, StringComparison.Ordinal);
            if (start < 0) return null;
            int end = code.IndexOf("\n        }", at, StringComparison.Ordinal);
            return end < 0 ? code.Substring(start) : code.Substring(start, end - start);
        }

        private static int Count(string text, string what)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
            return n;
        }
    }
}
