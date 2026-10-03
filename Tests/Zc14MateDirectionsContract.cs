using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Programma;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE SECOND NUMBER FOR A MATE: THE DIRECTIONS IT BLOCKS, AND THE REDUNDANT
    /// MATE REFUSED (TODO entry Z-C14, RAG note 82d).
    ///
    /// Measured on 30/9/2026 by the probe zc14/probe_mate_dof (SOLIDWORKS 2021
    /// SP5.1, decisive run probe_mate_dof_2.out): for a non-fixed component,
    /// Transform2 read (T0), then for each of six moves -- 5 mm along X, Y, Z of
    /// the assembly and 5 degrees about X, Y, Z through the component's origin --
    /// Transform2 set to the moved transform, EditRebuild3, Transform2 read
    /// again. A translation is BLOCKED when the displacement ALONG its axis is
    /// back under 0.01 mm, a rotation when the angle between the rotation parts
    /// is back under 0.01 degree; otherwise free. Two blocks with the first
    /// fixed read 0 blocked with no mate, 3 (TY, RX, RZ) with a coincident, 3
    /// with the SAME coincident again, 4 with a parallel added (S3), 6 fully
    /// mated. The first run judged the whole transform and read S3's rotations
    /// free: the solver put each rotation back by turning about another point
    /// and slid the block 1.7-2.4 mm along a free direction. AddMate5 NoError,
    /// GetWhatsWrongCount 0, the mate's error 0 and GetConstrainedStatus 2 do not
    /// see the redundant mate.
    ///
    /// The bench case `the_same_mate_twice_is_refused_as_redundant` runs the test
    /// in SOLIDWORKS. What a case cannot reach is checked here:
    ///  * the pure rules (the moved transform, the rule per direction on
    ///    synthetic transforms -- the S3 slide among them -- the redundant
    ///    decision, the words of the removal) are static methods of OperazioniSw
    ///    with no interop in their signature, called by reflection on the BUILT
    ///    library; the S3 data is also checked to tell the rule of the moved
    ///    direction from the rule of the whole transform, so that it cannot pass
    ///    under both;
    ///  * the rest is read in the source of MateOperations.cs and of the
    ///    catalogue: `mate` reads the directions before AddMate5 and after it,
    ///    takes out the mate it made by the object AddMate5 returned and refuses
    ///    when the verdict is redundant, the test follows the probe (fixed
    ///    component not moved, set checked, rebuild answer used, put back where
    ///    it started at the end), and the catalogue says what is measured, the
    ///    refusal and the cost. Every source check also runs on changed copies
    ///    of the real source, each of which must turn it red.
    ///
    /// Seen on 30/9/2026 in a scratch copy of the Z-C14 tree: all 17 green on
    /// the Z-C14 sources and a library built from them; 16 of 17 red on the
    /// sources and a library of 0.147.0 (2df71c4): none of the members exists,
    /// the catalogue still says the remaining freedom cannot be told, and
    /// `mate` neither measures nor refuses (the one green is the check of the
    /// S3 data, which needs no library). And 4 red on a library built from the
    /// Z-C14 sources with MovementOf on the whole displacement and the whole
    /// transform (the probe's first rule) and the «not_judged» line of
    /// MateVerdict taken out: the rule per direction (the TY slide and both S3
    /// rows read free), the source check of the rule with its seen-red
    /// companion, and the redundant decision (two free components read
    /// «unknown»).
    ///
    /// Added by the review of the branch (30/9/2026), one more check (18): the
    /// probe named THREE outcomes of a move and the first code kept two, so a
    /// move the mates put back only in part read «free» -- and a direction read
    /// free before and after a mate that holds it is a mate called redundant
    /// and taken out. Now it is unknown, checked by reflection (the two rules
    /// together tell the third reading) and in the source; and the other
    /// components a rebuild carried along are set back and reported, in the
    /// source, each seen red on its changed copy.
    /// </summary>
    internal static class Zc14MateDirectionsContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private const string Library = "Oce.Solid.Agent.Core";
        private const string Mates = "MateOperations.cs";
        private const string Catalogue = "Catalog/Operations.cs";

        /// <summary>
        /// The library's own member names this group reads, in one literal as
        /// the other contracts write them. The operation's own method is found
        /// as the member that calls AddMate5 (<see cref="MateStep"/>).
        /// </summary>
        private static readonly string[] OwnNames =
            "ReadBlockedDirections RemoveTheMateJustMade MovementOf DirectionBlocked MateVerdict".Split(' ');
        private static readonly string TestMember = OwnNames[0];
        private static readonly string RemovalMember = OwnNames[1];
        private static readonly string MovementMember = OwnNames[2];
        private static readonly string RuleMember = OwnNames[3];
        private static readonly string VerdictMember = OwnNames[4];

        /// <summary>The operation's name in the catalogue, as the bench cases write it.</summary>
        private const string MateOperation = "mate";

        internal static void RunAll()
        {
            Gruppo("the second number for a mate: the directions it blocks, and the redundant mate refused (Z-C14)");

            SlideDataTellsTheRules();
            var sources = ReadLibrary();
            if (sources != null) SourceChecks(sources);
            PureRules();
        }

        // ══ SYNTHETIC TRANSFORMS ═══════════════════════════════════════════
        //
        // ArrayData as SOLIDWORKS gives it: [0..8] the rotation by rows (the
        // component's axes in the assembly), [9..11] the translation in metres,
        // [12] the scale, [13..15] unused. The start is the probe's S1: the
        // block at (100, 10, 100) mm, not turned.

        private static double[] Transform(double[] rotation, double xMm, double yMm, double zMm)
        {
            var t = new double[16];
            Array.Copy(rotation, t, 9);
            t[9] = xMm / 1000.0; t[10] = yMm / 1000.0; t[11] = zMm / 1000.0; t[12] = 1.0;
            return t;
        }

        private static readonly double[] NotTurned = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };

        /// <summary>The rows of a turn of <paramref name="degrees"/> about the assembly axis 0, 1 or 2 applied to a not-turned block.</summary>
        private static double[] TurnedAbout(int axis, double degrees)
        {
            double a = degrees * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
            // Rows = the images of the block's axes: R = Q^T for a turn Q.
            if (axis == 0) return new[] { 1, 0, 0, 0, c, s, 0, -s, c };
            if (axis == 1) return new[] { c, 0, -s, 0, 1, 0, s, 0, c };
            return new[] { c, s, 0, -s, c, 0, 0, 0, 1 };
        }

        private static double[] Start() { return Transform(NotTurned, 100, 10, 100); }

        /// <summary>
        /// The rows the rule is checked on: name, transform after the rebuild,
        /// direction moved (0..5 = TX..RZ), and whether it must read blocked.
        /// The two S3 rows are the probe's: the turn back at 0 degrees, the
        /// block slid 1.667 mm (RX) and 2.435 mm (RY) along a free direction.
        /// </summary>
        private static List<Tuple<string, double[], int, bool>> RuleRows()
        {
            return new List<Tuple<string, double[], int, bool>>
            {
                Tuple.Create("TY put back exactly", Start(), 1, true),
                Tuple.Create("TX stays 5 mm away", Transform(NotTurned, 105, 10, 100), 0, false),
                Tuple.Create("TY put back, slid 2 mm along X (a free direction)", Transform(NotTurned, 102, 10, 100), 1, true),
                Tuple.Create("TY back within 0.009 mm", Transform(NotTurned, 100, 10.009, 100), 1, true),
                Tuple.Create("TY back only to 0.011 mm", Transform(NotTurned, 100, 10.011, 100), 1, false),
                Tuple.Create("S3: RX put back, slid 1.667 mm along Z", Transform(NotTurned, 100, 10, 101.667), 3, true),
                Tuple.Create("S3: RY put back, slid 2.435 mm along X", Transform(NotTurned, 102.435, 10, 100), 4, true),
                Tuple.Create("RY stays turned 5 degrees", Transform(TurnedAbout(1, 5), 100, 10, 100), 4, false),
                Tuple.Create("RZ half way back (2.5 degrees)", Transform(TurnedAbout(2, 2.5), 100, 10, 100), 5, false),
                Tuple.Create("RX back within 0.009 degree", Transform(TurnedAbout(0, 0.009), 100, 10, 100), 3, true),
            };
        }

        /// <summary>
        /// THE S3 DATA TELLS THE TWO RULES APART. A row that reads the same under
        /// the rule of the moved direction and under the rule of the whole
        /// transform (the probe's first run) cannot catch the old rule: this
        /// check needs no library, and proves the rows of <see cref="RuleRows"/>
        /// are able to fail.
        /// </summary>
        private static void SlideDataTellsTheRules()
        {
            const string label = "the S3 rows read blocked under the rule of the moved direction and free under the whole-transform rule";
            var start = Start();
            var wrong = new List<string>();
            foreach (var row in RuleRows().Where(r => r.Item1.StartsWith("S3", StringComparison.Ordinal)))
            {
                bool whole = LocalDistanceMm(start, row.Item2) < 0.01 && LocalAngleDeg(start, row.Item2) < 0.01;
                bool moved = row.Item3 < 3
                    ? Math.Abs(row.Item2[9 + row.Item3] - start[9 + row.Item3]) * 1000.0 < 0.01
                    : LocalAngleDeg(start, row.Item2) < 0.01;
                if (!moved || whole) wrong.Add(row.Item1 + ": moved-direction rule " + moved + ", whole-transform rule " + whole);
            }
            Segna(label, wrong.Count == 0, wrong.Count == 0 ? "2 rows, each blocked by one rule and free by the other" : string.Join("; ", wrong));
        }

        private static double LocalDistanceMm(double[] a, double[] b)
        {
            double dx = a[9] - b[9], dy = a[10] - b[10], dz = a[11] - b[11];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) * 1000.0;
        }

        private static double LocalAngleDeg(double[] a, double[] b)
        {
            double trace = 0;
            for (int i = 0; i < 9; i++) trace += a[i] * b[i];
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, (trace - 1.0) / 2.0))) * 180.0 / Math.PI;
        }

        // ══ THE SOURCE ═════════════════════════════════════════════════════

        private static void SourceChecks(Dictionary<string, string> sources)
        {
            Checked(sources, "readings around the mate",
                "the mate step reads the directions of both components before AddMate5 and after it, and finds the faces again after the first test",
                ReadingsAroundTheMate,
                "two ReadBlockedDirections before AddMate5 and two after it, both faces found again on the tested components " +
                "between the first test and AddMate5, the verdict from MateVerdict after the mate",
                Change(Mates, "no reading after the mate", "beforeA.Status == ReadingMeasured ? ReadBlockedDirections(",
                       "beforeA.Status == ReadingMeasured ? NotRead("),
                Change(Mates, "no reading before the mate", "var beforeB = ReadBlockedDirections(", "var beforeB = NotRead("),
                Change(Mates, "a face not found again after the test", "ComponentAxes(testedB)", "axesFromBefore"),
                Change(Mates, "no verdict", "string verdict = MateVerdict(", "string verdict = MateVerdictOld("));

            Checked(sources, "limit mates",
                "a redundant verdict stands only when every mate of the assembly is standard and without limits (the test moves one way)",
                LimitMatesGuard,
                "if (verdict == MateRedundant && !OnlyStandardMates(...)) verdict = MateNotJudged, " +
                "before the redundant branch",
                Change(Mates, "no guard for limit mates", "if (verdict == MateRedundant && !OnlyStandardMates(",
                       "if (false && !OnlyStandardMates("));

            Checked(sources, "redundant refused",
                "a redundant verdict takes out the mate by the object AddMate5 returned and refuses, quoting SOLIDWORKS's silence",
                RedundantRefused,
                "if (verdict == MateRedundant) before the success answer: RemoveTheMateJustMade with the object AddMate5 " +
                "returned, a refusal, REDUNDANT, «AddMate5 answered NoError» and «GetWhatsWrongCount is 0»",
                Change(Mates, "the redundant branch never taken", "if (verdict == MateRedundant)", "if (verdict == MateNotJudged && false)"),
                Change(Mates, "the mate not taken out", ", mate, nameA, nameB, uidA", ", null, nameA, nameB, uidA"),
                Change(Mates, "not refused", "return Fallito(refusal);", "_log(refusal);"),
                Change(Mates, "the refusal does not quote SOLIDWORKS", "GetWhatsWrongCount is 0", "no problem"),
                Change(Mates, "not the word", "is REDUNDANT:", "adds nothing:"));

            Checked(sources, "removal",
                "the mate is taken out by the object AddMate5 returned, never by a name, and the counts are read again",
                RemovalProblems,
                "mate as IFeature, Select2(false, 0), DeleteSelection2 (not EditDelete), both mate counts read again after it " +
                "and passed to MateRemovalNote; no SelectByID2, no walk of the tree, no name compared",
                Change(Mates, "the mate looked for in the tree", "var feature = mate as IFeature;", "var feature = FirstMateInTheTree() as IFeature;"),
                Change(Mates, "the counts not read again", "int nowA = AccoppiamentiDi(ComponentByName(", "int nowA = 1 + 0 * Unused("),
                Change(Mates, "EditDelete instead of DeleteSelection2",
                       ".Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);", ".EditDelete();"),
                Change(Mates, "selected by name", "if (feature.Select2(false, 0))", "if (SelectByName(featureName))"));

            Checked(sources, "test as the probe",
                "the test of one component follows the probe: fixed not moved, set checked, rebuild answer used, «elsewhere» unknown, " +
                "put back at the end with any other component it carried along",
                TestProblems,
                "a fixed component returns before any move; MoveTookEffect after each set; every RebuildForTest answer tested; " +
                "the direction judged by DirectionBlocked, and a move neither back nor stayed (MoveTookEffect on the same " +
                "reading) made unknown; the other non-fixed components read before the loop, set back before the final " +
                "rebuild and what is left of them reported; at the end Transform2 set to the original, rebuilt and the drift read",
                Change(Mates, "no restore to where it started", "name, math, original))", "name, math, reference))"),
                Change(Mates, "the rebuild's answer not tested", "if (!RebuildForTest(", "if (!Ignored() && RebuildForTest("),
                Change(Mates, "the set not checked", "if (!MoveTookEffect(reference, afterSet, k))", "if (afterSet == null)"),
                Change(Mates, "a fixed component moved", "reading.Status = ReadingFixed;", "reading.Status = ReadingMeasured;"),
                Change(Mates, "another rule", "bool? isBlocked = DirectionBlocked(reference,", "bool? isBlocked = WholeTransformBack(reference,"),
                Change(Mates, "the drift not read", "reading.DriftMm = DistanceMm(final, original);", ""),
                Change(Mates, "a move neither back nor stayed read free", "if (isBlocked == false && !MoveTookEffect(reference, afterRebuild, k))",
                       "if (isBlocked == false && false)"),
                Change(Mates, "the ELSEWHERE check on another reading than the rule's",
                       "bool? isBlocked = DirectionBlocked(reference, afterRebuild, k);",
                       "bool? isBlocked = DirectionBlocked(reference, afterSet, k);"),
                Change(Mates, "the other components not read before the test", "var others = OtherPositions(", "var others = NoPositions("),
                Change(Mates, "the other components not set back", "var setBack = SetOthersBack(", "var setBack = NothingSetBack("),
                Change(Mates, "what is left of the others not reported", "reading.Others.AddRange(OthersLeftMoved(", "Unused(("),
                Change(Mates, "the others not in the answer", "string others = reading.Others.Count == 0 ? \"\"", "string others = true ? \"\""));

            Checked(sources, "moved direction only",
                "the rule judges only the moved direction: the displacement along the axis, the angle of the turn, 0.01 mm and 0.01 degree",
                RuleProblems,
                "MovementOf: |to[9 + direction] - from[9 + direction]| * 1000 for a translation, AngleBetweenDeg for a turn, no " +
                "DistanceMm; DirectionBlocked on MovementOf with BackWithinMm and BackWithinDeg; 5 mm, 5 degrees, 0.01, 0.01",
                Change(Mates, "the whole displacement for a translation",
                       "if (direction < 3) return Math.Abs(to[9 + direction] - from[9 + direction]) * 1000.0;",
                       "if (direction < 3) return DistanceMm(from, to);"),
                Change(Mates, "the whole transform for a turn", "return AngleBetweenDeg(from, to);", "return AngleBetweenDeg(from, to) + DistanceMm(from, to);"),
                Change(Mates, "a looser threshold", "BackWithinMm = 0.01", "BackWithinMm = 0.5"),
                Change(Mates, "a smaller move", "TestMoveMm = 5.0", "TestMoveMm = 0.5"));

            Checked(sources, "catalogue",
                "the catalogue says what the mate step measures, that a redundant mate is refused, and the cost",
                CatalogueProblems,
                "the catalogue entry names blocked_directions_before, blocked_directions_after, degrees_removed, the refusal of a " +
                "redundant mate with NoError and GetWhatsWrongCount, and 13 rebuilds per component per reading",
                Change(Catalogue, "the cost not said", "THE COST: 13 rebuilds", "THE COST: some rebuilds"),
                Change(Catalogue, "the refusal not said", "A REDUNDANT MATE IS REFUSED", "A SECOND MATE IS FINE"),
                Change(Catalogue, "the answer keys not named", "degrees_removed and direction_test", "and the rest"));
        }

        // ── the checks, on source text (file name -> text) ───────────────────

        internal static List<string> ReadingsAroundTheMate(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var body = MateStep(CodeOnly(sources[Mates]));
            if (body == null) { problems.Add("no member of " + Mates + " calls AddMate5"); return problems; }
            int mate = body.IndexOf(".AddMate5(", StringComparison.Ordinal);
            string before = body.Substring(0, mate), after = body.Substring(mate);
            int readsBefore = Count(before, TestMember + "("), readsAfter = Count(after, TestMember + "(");
            if (readsBefore < 2) problems.Add(readsBefore + " " + TestMember + " before AddMate5, expected 2 (one per component)");
            if (readsAfter < 2) problems.Add(readsAfter + " " + TestMember + " after AddMate5, expected 2 (one per component)");
            int lastRead = before.LastIndexOf(TestMember + "(", StringComparison.Ordinal);
            int facesAgain = lastRead < 0 ? 0 : Count(before.Substring(lastRead), "ComponentAxes(tested");
            if (facesAgain < 2)
                problems.Add(facesAgain + " faces found again on the tested components between the test and AddMate5, expected 2: " +
                             "the rebuilds of the test can leave the first faces stale");
            if (!Regex.IsMatch(after, @"\bverdict\s*=\s*" + VerdictMember + @"\("))
                problems.Add("the verdict is not taken from " + VerdictMember + " after the mate");
            return problems;
        }

        internal static List<string> LimitMatesGuard(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            string text;
            if (!sources.TryGetValue(Mates, out text)) { problems.Add(Mates + " not read"); return problems; }
            int guard = text.IndexOf("if (verdict == MateRedundant && !OnlyStandardMates(", StringComparison.Ordinal);
            int branch = text.IndexOf("if (verdict == MateRedundant)", StringComparison.Ordinal);
            if (guard < 0) problems.Add("no OnlyStandardMates guard on the redundant verdict");
            else if (branch >= 0 && branch < guard) problems.Add("the redundant branch comes before the guard");
            if (text.IndexOf("verdict = MateNotJudged;", StringComparison.Ordinal) < 0) problems.Add("the guard does not turn the verdict into not_judged");
            return problems;
        }

        internal static List<string> RedundantRefused(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var body = MateStep(CodeOnly(sources[Mates]));
            if (body == null) { problems.Add("no member of " + Mates + " calls AddMate5"); return problems; }
            if (!Regex.IsMatch(body, @"\bmate\s*=\s*\w+\.AddMate5\("))
                problems.Add("«mate» is not the object AddMate5 returned");
            var branch = Regex.Match(body, @"if\s*\(\s*verdict\s*==\s*MateRedundant\s*\)");
            if (!branch.Success) { problems.Add("no branch on the redundant verdict"); return problems; }
            string block = BlockAfter(body, branch.Index + branch.Length);
            if (block == null) { problems.Add("the redundant branch has no block"); return problems; }
            if (!Regex.IsMatch(block, @"RemoveTheMateJustMade\([^;,]*,[^;,]*,\s*mate\s*,"))
                problems.Add("the redundant branch does not take out the mate by the object AddMate5 returned");
            if (!Regex.IsMatch(block, @"return\s+Fallito\("))
                problems.Add("the redundant branch does not refuse");
            foreach (var words in new[] { "is REDUNDANT:", "AddMate5 answered NoError", "GetWhatsWrongCount is 0" })
                if (!block.Contains(words)) problems.Add("the refusal does not say «" + words + "»");
            int success = body.IndexOf("\"blocked_directions_before\"", StringComparison.Ordinal);
            if (success < 0) problems.Add("the success answer has no blocked_directions_before");
            else if (success < branch.Index) problems.Add("the success answer comes before the redundant branch");
            return problems;
        }

        internal static List<string> RemovalProblems(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var body = MemberBody(CodeOnly(sources[Mates]), RemovalMember);
            if (body == null) { problems.Add(RemovalMember + " is not in " + Mates); return problems; }
            var cast = Regex.Match(body, @"\bvar\s+(\w+)\s*=\s*mate\s+as\s+IFeature\s*;");
            if (!cast.Success) { problems.Add("the feature is not the object AddMate5 returned (mate as IFeature)"); return problems; }
            string feature = Regex.Escape(cast.Groups[1].Value);
            var select = Regex.Match(body, feature + @"\.Select2\(\s*false\s*,\s*0\s*\)");
            int delete = body.IndexOf(".DeleteSelection2(", StringComparison.Ordinal);
            if (!select.Success) problems.Add("the mate is not selected through its own Select2");
            if (delete < 0) problems.Add("DeleteSelection2 is not called");
            else if (select.Success && delete < select.Index) problems.Add("DeleteSelection2 comes before the selection");
            int recounts = delete < 0 ? 0 : Count(body.Substring(delete), "ComponentByName(");
            if (recounts < 2) problems.Add(recounts + " mate counts read after DeleteSelection2, expected 2");
            if (!Regex.IsMatch(body, @"MateRemovalNote\([^;]*\bnowA\b[^;]*\bnowB\b[^;]*\)\s*;"))
                problems.Add("the counts read again are not passed to MateRemovalNote");
            foreach (var forbidden in new[] { "SelectByID2(", "FirstFeature(", "GetTypeName2(", ".EditDelete(", ".Name ==", "GetNextFeature(" })
                if (body.Contains(forbidden)) problems.Add("the removal uses " + forbidden + ": a mate is never looked for by name or in the tree");
            return problems;
        }

        internal static List<string> TestProblems(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var body = MemberBody(CodeOnly(sources[Mates]), TestMember);
            if (body == null) { problems.Add(TestMember + " is not in " + Mates); return problems; }
            int firstSet = body.IndexOf("SetTransform(", StringComparison.Ordinal);
            var fixedGuard = Regex.Match(body, @"if\s*\(\s*\w+\(\s*component\s*\)\s*\)\s*\{[^{}]*\.Status\s*=\s*ReadingFixed\s*;[^{}]*return\s+\w+\s*;\s*\}");
            if (!fixedGuard.Success) problems.Add("a fixed component does not return before the test");
            else if (firstSet >= 0 && fixedGuard.Index > firstSet) problems.Add("the fixed check comes after a move");
            int took = body.IndexOf("MoveTookEffect(", StringComparison.Ordinal);
            int firstRebuild = body.IndexOf("RebuildForTest(", StringComparison.Ordinal);
            if (took < 0 || firstSet < 0 || firstRebuild < 0 || took < firstSet || took > firstRebuild)
                problems.Add("MoveTookEffect is not checked between the first set and the first rebuild");
            int rebuilds = Count(body, "RebuildForTest(");
            int tested = Regex.Matches(body, @"if\s*\(\s*!?\s*RebuildForTest\(").Count;
            if (rebuilds == 0 || rebuilds != tested) problems.Add(tested + " of " + rebuilds + " RebuildForTest answers are tested");
            var rule = Regex.Match(body, @"bool\?\s*(\w+)\s*=\s*" + RuleMember + @"\(\s*reference\s*,\s*(\w+)\s*,\s*k\s*\)\s*;");
            if (!rule.Success) problems.Add("the direction is not judged by " + RuleMember + " on one reading taken after the rebuild");
            else
            {
                // THE PROBE'S THIRD READING: neither back (the rule) nor where it
                // was moved (MoveTookEffect on the SAME reading) is unknown.
                string verdict = Regex.Escape(rule.Groups[1].Value), reading = Regex.Escape(rule.Groups[2].Value);
                if (!Regex.IsMatch(body.Substring(rule.Index), @"if\s*\(\s*" + verdict + @"\s*==\s*false\s*&&\s*!\s*MoveTookEffect\(\s*reference\s*,\s*" +
                                   reading + @"\s*,\s*k\s*\)\s*\)\s*\{[^{}]*\bwhy\s*="))
                    problems.Add("a move that is neither back nor where it was moved (the probe's ELSEWHERE) is not made unknown");
            }
            int loop = body.IndexOf("for (int k = 0;", StringComparison.Ordinal);
            var snapshot = Regex.Match(body, @"var\s+(\w+)\s*=\s*OtherPositions\(");
            if (!snapshot.Success || loop < 0 || snapshot.Index > loop)
                problems.Add("the other components are not read before the loop of the six moves");
            var restore = Regex.Match(body, @"SetTransform\([^;()]*,\s*math\s*,\s*original\s*\)");
            if (!restore.Success) problems.Add("the component is not set back to its original transform at the end");
            else
            {
                string tail = body.Substring(restore.Index);
                if (!tail.Contains("RebuildForTest(")) problems.Add("no rebuild after setting it back");
                if (!Regex.IsMatch(tail, @"\.DriftMm\s*=\s*DistanceMm\(\s*\w+\s*,\s*original\s*\)"))
                    problems.Add("the drift from where it started is not read after setting it back");
                if (loop < 0 || restore.Index < loop) problems.Add("the final restore is not after the loop of the six moves");
                if (snapshot.Success)
                {
                    string others = Regex.Escape(snapshot.Groups[1].Value);
                    var setBack = Regex.Match(body, @"SetOthersBack\([^;]*\b" + others + @"\s*\)");
                    if (!setBack.Success || setBack.Index < loop || setBack.Index > restore.Index)
                        problems.Add("the other components are not set back between the loop and the final rebuild");
                    if (!Regex.IsMatch(tail, @"\.Others\.AddRange\(\s*OthersLeftMoved\([^;]*\b" + others + @"\b"))
                        problems.Add("what the final rebuild left of the other components is not kept");
                }
            }
            var drift = MemberBody(CodeOnly(sources[Mates]), "DriftAfterTest");
            if (drift == null || !Regex.IsMatch(drift, @"\.Others\.Count\s*==\s*0\s*\?\s*""""\s*:"))
                problems.Add("the answer does not say which other components the test left moved (DriftAfterTest)");
            return problems;
        }

        internal static List<string> RuleProblems(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var code = CodeOnly(sources[Mates]);
            var movement = MemberBody(code, MovementMember);
            if (movement == null) problems.Add(MovementMember + " is not in " + Mates);
            else
            {
                if (!Regex.IsMatch(movement, @"if\s*\(\s*direction\s*<\s*3\s*\)\s*return\s+Math\.Abs\(\s*to\[\s*9\s*\+\s*direction\s*\]\s*-\s*from\[\s*9\s*\+\s*direction\s*\]\s*\)\s*\*\s*1000(\.0)?\s*;"))
                    problems.Add("a translation is not judged by the displacement along its own axis");
                if (!Regex.IsMatch(movement, @"return\s+AngleBetweenDeg\(\s*from\s*,\s*to\s*\)\s*;"))
                    problems.Add("a turn is not judged by the angle between the rotation parts alone");
                if (movement.Contains("DistanceMm(")) problems.Add(MovementMember + " uses the whole displacement");
            }
            var rule = MemberBody(code, RuleMember);
            if (rule == null || !rule.Contains(MovementMember + "(") || !rule.Contains("BackWithinMm") || !rule.Contains("BackWithinDeg"))
                problems.Add(RuleMember + " does not judge MovementOf against BackWithinMm and BackWithinDeg");
            foreach (var constant in new[] { @"TestMoveMm\s*=\s*5(\.0)?\b", @"TestMoveDeg\s*=\s*5(\.0)?\b",
                                             @"BackWithinMm\s*=\s*0\.01\b", @"BackWithinDeg\s*=\s*0\.01\b" })
                if (!Regex.IsMatch(code, constant)) problems.Add("the probe's constant " + constant + " is not in " + Mates);
            return problems;
        }

        internal static List<string> CatalogueProblems(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var catalogue = sources[Catalogue];
            int entry = catalogue.IndexOf("\"" + MateOperation + "\"", StringComparison.Ordinal);
            if (entry < 0) { problems.Add("the catalogue has no entry " + MateOperation); return problems; }
            int next = catalogue.IndexOf("new OperazioneDisegno", entry, StringComparison.Ordinal);
            string block = next < 0 ? catalogue.Substring(entry) : catalogue.Substring(entry, next - entry);
            foreach (var words in new[] { "blocked_directions_before", "blocked_directions_after", "degrees_removed and direction_test",
                                          "A REDUNDANT MATE IS REFUSED", "NoError", "GetWhatsWrongCount", "THE COST: 13 rebuilds" })
                if (!block.Contains(words)) problems.Add("the entry " + MateOperation + " does not say «" + words + "»");
            return problems;
        }

        /// <summary>
        /// The text of the member that calls AddMate5: the operation's own
        /// method, found by what it does rather than by its name. Null when no
        /// member calls it.
        /// </summary>
        private static string MateStep(string code)
        {
            int call = code.IndexOf(".AddMate5(", StringComparison.Ordinal);
            if (call < 0) return null;
            var header = Regex.Matches(code.Substring(0, call), @"\n {8}(?:private|internal|public|protected)\b[^\n(;=]*\(")
                              .Cast<Match>().LastOrDefault();
            if (header == null) return null;
            int end = code.IndexOf("\n        }", header.Index + 1, StringComparison.Ordinal);
            return end < 0 ? code.Substring(header.Index) : code.Substring(header.Index, end - header.Index);
        }

        /// <summary>The text of the block that opens at the first «{» after <paramref name="from"/>, braces counted; null when there is none.</summary>
        private static string BlockAfter(string code, int from)
        {
            int open = code.IndexOf('{', from);
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}' && --depth == 0) return code.Substring(open, i - open + 1);
            }
            return null;
        }

        // ── seen red ─────────────────────────────────────────────────────────

        private sealed class SourceChange
        {
            public string File, Name, From, To;
        }

        private static SourceChange Change(string file, string name, string from, string to)
        {
            return new SourceChange { File = file, Name = name, From = from, To = to };
        }

        /// <summary>
        /// One source check and its seen-red companion: the check on the real
        /// source, then each change applied alone to a copy, each of which must
        /// turn it red.
        /// </summary>
        private static void Checked(Dictionary<string, string> sources, string tag, string label,
                                    Func<IDictionary<string, string>, List<string>> check, string green,
                                    params SourceChange[] changes)
        {
            List<string> problems;
            try { problems = check(sources); }
            catch (Exception ex) { problems = new List<string> { ex.GetType().Name + ": " + ex.Message }; }
            Segna(label, problems.Count == 0, problems.Count == 0 ? green : string.Join(" | ", problems));

            var misses = new List<string>();
            if (problems.Count != 0) misses.Add("the untouched source is already red, the changes prove nothing");
            else
            {
                foreach (var c in changes)
                {
                    string text;
                    if (!sources.TryGetValue(c.File, out text)) { misses.Add(c.Name + ": " + c.File + " not read"); continue; }
                    int at = text.IndexOf(c.From, StringComparison.Ordinal);
                    if (at < 0) { misses.Add(c.Name + ": anchor not found in " + c.File + " (update the change)"); continue; }
                    string changed = text.Substring(0, at) + c.To + text.Substring(at + c.From.Length);
                    var copy = new Dictionary<string, string>(sources, StringComparer.OrdinalIgnoreCase) { [c.File] = changed };
                    List<string> red;
                    try { red = check(copy); }
                    catch (Exception ex) { red = new List<string> { ex.GetType().Name }; }
                    if (red.Count == 0) misses.Add(c.Name + ": the changed copy is still green");
                }
            }
            Segna("seen red: " + tag + ", on each changed copy", misses.Count == 0,
                  misses.Count == 0 ? changes.Length + " change(s), each one red: " + string.Join("; ", changes.Select(c => c.Name))
                                    : string.Join(" | ", misses));
        }

        // ── source helpers ───────────────────────────────────────────────────

        /// <summary>The two files this group reads, by path relative to the library, with \n line ends; null when one is missing.</summary>
        private static Dictionary<string, string> ReadLibrary()
        {
            var dir = Path.Combine(Radice(), Library);
            var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var rel in new[] { Mates, Catalogue })
                {
                    var f = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(f)) sources[rel] = File.ReadAllText(f).Replace("\r\n", "\n");
                }
            }
            catch (Exception ex)
            {
                Segna("the library sources of Z-C14 can be read", false, ex.GetType().Name + ": " + ex.Message);
                return null;
            }
            var missing = new[] { Mates, Catalogue }.Where(n => !sources.ContainsKey(n)).ToList();
            if (missing.Count > 0)
            {
                Segna("the library sources of Z-C14 can be read", false, "missing in " + dir + ": " + string.Join(", ", missing));
                return null;
            }
            return sources;
        }

        /// <summary>
        /// The source without its comment lines and trailing line comments, so
        /// that a call named in a comment is not counted. String literals are
        /// kept: the words of the refusal are looked for in them.
        /// </summary>
        private static string CodeOnly(string source)
        {
            var lines = source.Split('\n');
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
        /// The text of the member declared with <paramref name="name"/> (a line at
        /// class-member indentation, the name before the first parenthesis), up
        /// to its closing brace at that indentation; null when it is not there.
        /// </summary>
        private static string MemberBody(string code, string name)
        {
            var header = Regex.Match(code, @"\n {8}(?:private|internal|public|protected)\b[^\n(;=]*\b" + Regex.Escape(name) + @"\s*\(");
            if (!header.Success) return null;
            int end = code.IndexOf("\n        }", header.Index + 1, StringComparison.Ordinal);
            return end < 0 ? code.Substring(header.Index) : code.Substring(header.Index, end - header.Index);
        }

        private static int Count(string text, string what)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
            return n;
        }

        // ══ THE PURE RULES, ON THE BUILT LIBRARY ═══════════════════════════

        private static void PureRules()
        {
            var library = Path.Combine(Radice(), Library, "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NonQui("the pure rules of Z-C14 (the moves, the rule per direction, the redundant decision, the removal)",
                       library + " is missing: build the library first (build.ps1)");
                return;
            }

            Type sw;
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
                // From the bytes, as ApiCatalogContracts: the repository may sit
                // on a network drive, where LoadFrom is refused.
                var asm = Assembly.Load(File.ReadAllBytes(library));
                sw = asm.GetType("Oce.Solid.Agent.Core.OperazioniSw", false);
            }
            catch (Exception ex) { Segna("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }

            Moves(sw);
            RulePerDirection(sw);
            Verdicts(sw);
            RemovalNotes(sw);
            StandardMates(sw);
        }

        // ── which mates allow a «redundant» verdict (review of Z-C14) ─────────
        private static void StandardMates(Type sw)
        {
            const string label = "only standard mates without limits allow «redundant»: coincident, concentric, perpendicular, parallel, tangent, lock; distance and angle when not limit";
            try
            {
                var rows = new[]
                {
                    Tuple.Create(0, (bool?)null, true), Tuple.Create(1, (bool?)null, true), Tuple.Create(2, (bool?)null, true),
                    Tuple.Create(3, (bool?)null, true), Tuple.Create(4, (bool?)null, true), Tuple.Create(16, (bool?)null, true),
                    Tuple.Create(5, (bool?)false, true), Tuple.Create(6, (bool?)false, true),
                    Tuple.Create(5, (bool?)true, false), Tuple.Create(6, (bool?)true, false), Tuple.Create(5, (bool?)null, false),
                    Tuple.Create(10, (bool?)null, false), Tuple.Create(21, (bool?)null, false), Tuple.Create(7, (bool?)null, false),
                };
                var wrong = new List<string>();
                foreach (var r in rows)
                {
                    bool got = (bool)Call(sw, "StandardMate", r.Item1, r.Item2);
                    if (got != r.Item3) wrong.Add("type " + r.Item1 + " limit " + (r.Item2 == null ? "unread" : r.Item2.ToString()) + ": " + got);
                }
                Segna(label, wrong.Count == 0, wrong.Count == 0 ? rows.Length + " types as expected" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Segna(label, false, ex.GetType().Name + ": " + ex.Message); }
        }

        private static object Call(Type t, string method, params object[] args)
        {
            if (t == null) throw new MissingMethodException("OperazioniSw is not in this library");
            var m = t.GetMethod(method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method + " is not in this library (0.147.0 or older)");
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        private static bool Near(double a, double b, double within) { return Math.Abs(a - b) <= within; }

        // ── the moves of the test ────────────────────────────────────────────
        private static void Moves(Type sw)
        {
            const string label = "the six moves: 5 mm along X, Y, Z and 5 degrees about X, Y, Z of the ASSEMBLY, through the origin";
            try
            {
                var wrong = new List<string>();
                var start = Start();
                var field = sw == null ? null : sw.GetField("DirectionNames", Any);
                if (field == null) throw new MissingFieldException("OperazioniSw.DirectionNames is not in this library (0.147.0 or older)");
                var names = (string[])field.GetValue(null);
                if (string.Join(" ", names) != "TX TY TZ RX RY RZ") wrong.Add("the directions are " + string.Join(" ", names));
                for (int k = 0; k < 6; k++)
                {
                    var moved = (double[])Call(sw, "MovedForTest", start, k);
                    if (moved == null || moved.Length != 16) { wrong.Add(names[k] + ": no 16-number transform"); continue; }
                    double along = k < 3 ? (moved[9 + k] - start[9 + k]) * 1000.0 : 0;
                    double moveMm = LocalDistanceMm(start, moved), turn = LocalAngleDeg(start, moved);
                    if (k < 3 && (!Near(along, 5, 1e-9) || !Near(moveMm, 5, 1e-9) || !Near(turn, 0, 1e-6)))
                        wrong.Add(names[k] + ": " + along + " mm along, " + moveMm + " mm, " + turn + " degrees");
                    if (k >= 3 && (!Near(turn, 5, 1e-6) || !Near(moveMm, 0, 1e-9)))
                        wrong.Add(names[k] + ": " + turn + " degrees, " + moveMm + " mm");
                    if (!(bool)Call(sw, "MoveTookEffect", start, moved, k)) wrong.Add(names[k] + ": the move does not read as taken");
                    if ((bool)Call(sw, "MoveTookEffect", start, start, k)) wrong.Add(names[k] + ": no move reads as taken");
                }
                // A block already turned 90 degrees about Z (its X axis along the
                // assembly's Y): RX must turn it about the ASSEMBLY's X, so its X
                // axis (0, 1, 0) goes to (0, cos 5, sin 5) and its Y axis (-1, 0, 0)
                // does not move.
                var turned = Transform(TurnedAbout(2, 90), 100, 10, 100);
                var rx = (double[])Call(sw, "MovedForTest", turned, 3);
                double c = Math.Cos(5 * Math.PI / 180), s = Math.Sin(5 * Math.PI / 180);
                var expected = new[] { 0, c, s, -1, 0, 0, 0, -s, c };
                for (int i = 0; i < 9; i++)
                    if (!Near(rx[i], expected[i], 1e-9))
                    { wrong.Add("RX of a block turned about Z: row value " + i + " is " + rx[i] + ", expected " + expected[i]); break; }
                if (Call(sw, "MovedForTest", new double[11], 0) != null) wrong.Add("11 numbers give a moved transform");
                Segna(label, wrong.Count == 0, wrong.Count == 0 ? "six moves, the turn about the assembly's axes, a short array refused" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Segna(label, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── the rule, one direction at a time ────────────────────────────────
        private static void RulePerDirection(Type sw)
        {
            const string label = "the rule per direction: back within 0.01 mm along the axis or 0.01 degree is blocked, a slide along a free direction does not count";
            try
            {
                var wrong = new List<string>();
                var start = Start();
                foreach (var row in RuleRows())
                {
                    var got = Call(sw, "DirectionBlocked", start, row.Item2, row.Item3);
                    if (!(got is bool) || (bool)got != row.Item4)
                        wrong.Add(row.Item1 + ": " + (got == null ? "unknown" : got.ToString()) + " instead of " + (row.Item4 ? "blocked" : "free"));
                }
                foreach (var unreadable in new[] { null, new double[11], Transform(new double[] { 1, 0, 0, 0, double.NaN, 0, 0, 0, 1 }, 100, 10, 100) })
                    if (Call(sw, "DirectionBlocked", start, unreadable, 1) != null)
                        wrong.Add("an unreadable transform (" + (unreadable == null ? "null" : unreadable.Length + " numbers") + ") is not «unknown»");
                Segna(label, wrong.Count == 0, wrong.Count == 0 ? RuleRows().Count + " rows, the two S3 slides blocked, 3 unreadable transforms unknown" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Segna(label, false, ex.GetType().Name + ": " + ex.Message); }

            // THE THIRD READING. The probe named three outcomes of a move --
            // BLOCKED, free, ELSEWHERE -- and ReadBlockedDirections makes the
            // third unknown. It is told by the two rules together: not
            // DirectionBlocked and not MoveTookEffect on the reading after the
            // rebuild. A translation put back only to 0.011 mm, or a turn put
            // back half way, is neither; a slide along another axis is not a
            // third reading.
            const string third = "a move neither back nor where it was moved is the probe's «elsewhere», told by DirectionBlocked and MoveTookEffect together";
            try
            {
                var wrong = new List<string>();
                var start = Start();
                var elsewhere = new HashSet<string> { "TY back only to 0.011 mm", "RZ half way back (2.5 degrees)" };
                foreach (var row in RuleRows())
                {
                    bool blocked = (bool)Call(sw, "DirectionBlocked", start, row.Item2, row.Item3);
                    bool stayed = (bool)Call(sw, "MoveTookEffect", start, row.Item2, row.Item3);
                    string got = blocked ? (stayed ? "both" : "blocked") : stayed ? "free" : "elsewhere";
                    string expected = elsewhere.Contains(row.Item1) ? "elsewhere" : row.Item4 ? "blocked" : "free";
                    if (got != expected) wrong.Add(row.Item1 + ": " + got + " instead of " + expected);
                }
                Segna(third, wrong.Count == 0, wrong.Count == 0 ? RuleRows().Count + " rows: 2 elsewhere, the S3 slides blocked, the moves that stay free" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Segna(third, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── the redundant decision ───────────────────────────────────────────
        private static void Verdicts(Type sw)
        {
            const string label = "the redundant decision: never from an unknown or empty reading, never on two free components";
            try
            {
                string[] none = new string[0], s1 = { "TY", "RX", "RZ" }, s3 = { "TY", "RX", "RY", "RZ" };
                string[] all = { "TX", "TY", "TZ", "RX", "RY", "RZ" };
                var rows = new[]
                {
                    Tuple.Create("the first coincident (S0 -> S1)", "fixed", none, none, "measured", none, s1, "removes"),
                    Tuple.Create("the same coincident again (S1 -> S2)", "fixed", none, none, "measured", s1, s1, "redundant"),
                    Tuple.Create("a parallel after it (S2 -> S3)", "fixed", none, none, "measured", s1, s3, "removes"),
                    Tuple.Create("a component read unknown", "fixed", none, none, "unknown", s1, s1, "unknown"),
                    Tuple.Create("nothing blocked after a mate to a fixed part", "fixed", none, none, "measured", none, none, "unknown"),
                    Tuple.Create("a direction blocked before and free after", "fixed", none, none, "measured", s1, new[] { "TY", "RX" }, "unknown"),
                    Tuple.Create("two free components, nothing removed", "measured", s1, s1, "measured", none, none, "not_judged"),
                    Tuple.Create("two free components, something removed", "measured", none, none, "measured", none, s1, "removes"),
                    Tuple.Create("a partner already fully blocked", "measured", all, all, "measured", s1, s1, "redundant"),
                    Tuple.Create("two fixed components", "fixed", none, none, "fixed", none, none, "redundant"),
                };
                var wrong = new List<string>();
                foreach (var r in rows)
                {
                    var got = Convert.ToString(Call(sw, "MateVerdict", r.Item2, r.Item3, r.Item4, r.Item5, r.Item6, r.Item7));
                    if (got != r.Rest.Item1) wrong.Add(r.Item1 + ": " + got + " instead of " + r.Rest.Item1);
                }
                var added = (string[])Call(sw, "NewlyBlocked", new[] { "RZ" }, new[] { "RZ", "RY", "TX" });
                if (string.Join(" ", added) != "TX RY") wrong.Add("NewlyBlocked gives «" + string.Join(" ", added) + "» instead of «TX RY», the order of the test");
                Segna(label, wrong.Count == 0, wrong.Count == 0 ? rows.Length + " pairs of readings as expected, the new directions in the order of the test" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Segna(label, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── the words of the removal ─────────────────────────────────────────
        private static void RemovalNotes(Type sw)
        {
            const string label = "the removal says «taken out» only when DeleteSelection2 answered true and both counts are back";
            try
            {
                var rows = new[]
                {
                    Tuple.Create("taken out, counts back", true, true, 1, 1, 1, 1, "I took out the mate I had just made"),
                    Tuple.Create("taken out, a count still up", true, true, 1, 1, 1, 2, "look at the assembly"),
                    Tuple.Create("DeleteSelection2 false", true, false, 1, 1, 2, 2, "did not take out"),
                    Tuple.Create("not a feature", false, false, 1, 1, 2, 2, "I do not look for a mate by its name"),
                    Tuple.Create("counts unreadable", true, true, 1, 1, -1, 1, "could not count the mates again"),
                };
                var wrong = new List<string>();
                foreach (var r in rows)
                {
                    var note = Convert.ToString(Call(sw, "MateRemovalNote", r.Item2, "Coincident2", r.Item3, "C1", "C2", r.Item4, r.Item5, r.Item6, r.Item7));
                    if (note.IndexOf(r.Rest.Item1, StringComparison.Ordinal) < 0) wrong.Add(r.Item1 + ": «" + note.Trim() + "»");
                    if (r.Item1 != "taken out, counts back" && note.Contains("I took out")) wrong.Add(r.Item1 + ": says «I took out»");
                }
                Segna(label, wrong.Count == 0, wrong.Count == 0 ? rows.Length + " outcomes, each with its words" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Segna(label, false, ex.GetType().Name + ": " + ex.Message); }
        }
    }
}
