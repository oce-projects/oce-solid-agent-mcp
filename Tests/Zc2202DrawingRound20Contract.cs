using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 20 OF REPORT 8CD843 (28/10/2026), WHERE THE BENCH CANNOT
    /// REACH.
    ///
    /// N62: the outline of a view keeps a margin around what it draws, and a circle of a
    /// detail in that margin passed the check and enlarged nothing. N64: the verdict on
    /// the mates counted the suppressed components and one placed by a local pattern as
    /// laid down with no mate. N65: the state did not say in which folder of the tree a
    /// component is. N64 and N65 have no bench case: no operation of a plan suppresses a
    /// component, makes a pattern of components or a folder; scenario t62 read them on a
    /// copy of the assembly of round 20. N63: what the sketches of a part are made of
    /// (`sketch_details`) and how each mate is set (alignment, value, entities).
    ///
    /// Two halves, as Zc2201: the decisions by reflection, and the wiring on the
    /// sources, every rule first red on a changed copy. On library 0.174.9 every check
    /// is red.
    /// </summary>
    internal static class Zc2202DrawingRound20Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 20 of report 8cd843: a circle of a detail in the margin of a view refused with where the view draws (N62), suppressed and patterned components left out of the verdict on the mates (N64), the folders of a component (N65)");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 20 fixes",
                       library + " is missing: build the library first (build.ps1)");
                return;
            }
            Type sw, state;
            try
            {
                var redist = WhereTheInteropIs();
                if (redist != null)
                    AppDomain.CurrentDomain.AssemblyResolve += (s, a) =>
                    {
                        var name = new AssemblyName(a.Name).Name;
                        if (!name.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase)) return null;
                        var file = Path.Combine(redist, name + ".dll");
                        return File.Exists(file) ? Assembly.LoadFrom(file) : null;
                    };
                // From the bytes: the repository may sit on a network drive,
                // where LoadFrom is refused.
                var core = Assembly.Load(File.ReadAllBytes(library));
                sw = core.GetType("Oce.Solid.Agent.Core.SwOperations", false);
                state = core.GetType("Oce.Solid.Agent.Core.DocumentState", false);
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (sw == null || state == null) { Check("SwOperations and DocumentState are in the library", false, "type missing"); return; }

            Margin(sw);
            NotJudged(state);
            Details(sw, state);
        }

        private static object Call(Type t, string method, params object[] args)
        {
            var m = ReflectionLog.Global.Method(t, method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method);
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        private static string Why(Exception ex)
        {
            var inner = ex.InnerException ?? ex;
            return inner.GetType().Name + ": " + inner.Message;
        }

        // =================================================================
        // THE PURE RULES
        // =================================================================

        private static void Margin(Type sw)
        {
            const string label = "N62: the box of the lines of a view; a circle out of it and meeting none of them lies in the margin, one across a line or wholly inside the drawing does not, nor any when the lines are unknown; the box from the centre of the outline and the text of the refusal";
            try
            {
                var wrong = new List<string>();
                // The front view of the plate of the bench case, in metres of sheet: a rectangle
                // 120 x 20 mm centred at (0.2, 0.15), its outline 5.9 mm wider on each side.
                var pieces = new List<double[]>
                {
                    new[] { 0.14, 0.14, 0.26, 0.14 }, new[] { 0.26, 0.14, 0.26, 0.16 },
                    new[] { 0.26, 0.16, 0.14, 0.16 }, new[] { 0.14, 0.16, 0.14, 0.14 },
                };
                var outline = new[] { 0.1341, 0.1341, 0.2659, 0.1659 };
                var box = Call(sw, "PiecesBox", pieces) as double[];
                if (box == null || Math.Abs(box[0] - 0.14) > 1e-9 || Math.Abs(box[1] - 0.14) > 1e-9 || Math.Abs(box[2] - 0.26) > 1e-9 || Math.Abs(box[3] - 0.16) > 1e-9)
                    wrong.Add("the box of the plate: " + (box == null ? "null" : string.Join(", ", box)));
                if (Call(sw, "PiecesBox", new List<double[]>()) != null) wrong.Add("a box of no piece");
                if (Call(sw, "PiecesBox", new object[] { null }) != null) wrong.Add("a box of no list");

                Func<double, double, double, bool> margin = (x, y, r) => (bool)Call(sw, "InTheMarginOnly", x, y, r, box, pieces);
                if (!margin(0.263, 0.15, 0.002)) wrong.Add("radius 2 at x 63 mm, in the margin, not refused");
                if (margin(0.255, 0.15, 0.008)) wrong.Add("radius 8 at x 55 mm, across the end of the plate, refused");
                if (margin(0.2, 0.15, 0.008)) wrong.Add("radius 8 at the centre, inside the drawing, refused here (it is found empty after it is made)");
                if (margin(0.2605, 0.15, 0.001)) wrong.Add("radius 1 at 0.5 mm from the end, across it, refused");
                if ((bool)Call(sw, "InTheMarginOnly", 0.263, 0.15, 0.002, null, pieces)) wrong.Add("refused with no box");
                if ((bool)Call(sw, "InTheMarginOnly", 0.263, 0.15, 0.002, box, null)) wrong.Add("refused with no lines");
                if ((bool)Call(sw, "CircleInsideBox", 0.255, 0.15, 0.008, box)) wrong.Add("the circle across the end said inside the box");
                if (!(bool)Call(sw, "CircleInsideBox", 0.2, 0.15, 0.008, box)) wrong.Add("the circle at the centre said outside the box");

                var fromCentre = Call(sw, "BoxFromCentre", box, outline) as object[];
                if (fromCentre == null || Convert.ToDouble(fromCentre[0]) != -60 || Convert.ToDouble(fromCentre[1]) != -10 ||
                    Convert.ToDouble(fromCentre[2]) != 60 || Convert.ToDouble(fromCentre[3]) != 10)
                    wrong.Add("the box from the centre of the outline: " + (fromCentre == null ? "null" : string.Join(", ", fromCentre)));
                var text = Call(sw, "DrawnBoxText", box, outline) as string;
                foreach (var part in new[] { "The view draws from x -60 to 60 and from y -10 to 10 mm from that center", "131.8 x 31.8 mm", "keeps a margin" })
                    if (text == null || !text.Contains(part)) wrong.Add("the text lacks «" + part + "»: " + (text ?? "null"));
                var unknown = Call(sw, "DrawnBoxText", null, outline) as string;
                if (unknown == null || !unknown.Contains("I could not read its lines")) wrong.Add("the text with no lines: " + (unknown ?? "null"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "the plate of the bench case: its box, four circles, two inputs missing, the box from the centre, the two texts" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void NotJudged(Type state)
        {
            const string label = "N64: the verdict on the mates says the suppressed components and those a pattern places, apart and counted, and says nothing when there are none";
            try
            {
                var wrong = new List<string>();
                var both = Call(state, "NotJudgedText", 3, 1) as string;
                foreach (var part in new[] { "3 suppressed components are not in this count", "1 component placed by a pattern is not in this count", "turned on again", "not a mate of its own" })
                    if (both == null || !both.Contains(part)) wrong.Add("the text lacks «" + part + "»: " + (both ?? "null"));
                var one = Call(state, "NotJudgedText", 1, 0) as string;
                if (one == null || !one.StartsWith("1 suppressed component is") || one.Contains("pattern")) wrong.Add("one suppressed: " + (one ?? "null"));
                var two = Call(state, "NotJudgedText", 0, 2) as string;
                if (two == null || !two.StartsWith("2 components placed by a pattern are") || two.Contains("suppressed")) wrong.Add("two by a pattern: " + (two ?? "null"));
                if (Call(state, "NotJudgedText", 0, 0) != null) wrong.Add("a text with none left out");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "3 and 1 (round 20), 1 and 0, 0 and 2, 0 and 0" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void Details(Type sw, Type state)
        {
            const string label = "N63: the kinds of the segments and of the relations of a sketch by their names, the alignment of a mate in the words «mate» takes (1 is «opposite», not swAlignSAME), the kinds of the mate entities";
            try
            {
                var wrong = new List<string>();
                Action<Type, string, int, string> same = (t, method, value, expected) =>
                {
                    var got = Call(t, method, value) as string;
                    if (got != expected) wrong.Add(method + "(" + value + ") = " + (got ?? "null") + ", not " + expected);
                };
                // swSketchSegments_e: LINE 0, ARC 1, SPLINE 3; swConstraintType_e: COINCIDENT 9.
                same(sw, "SegmentKindName", 0, "line");
                same(sw, "SegmentKindName", 1, "arc");
                same(sw, "SegmentKindName", 3, "spline");
                same(sw, "SegmentKindName", 99, "kind 99");
                same(sw, "RelationKindName", 9, "coincident");
                same(sw, "RelationKindName", -777, "kind -777");
                // swMateAlign_e: 0 aligned, 1 anti-aligned (also swAlignSAME), 2 closest.
                same(state, "AlignmentName", 0, "aligned");
                same(state, "AlignmentName", 1, "opposite");
                same(state, "AlignmentName", 2, "closest");
                same(state, "AlignmentName", 7, "alignment 7");
                // swSelectType_e: FACES 2, DATUMPLANES 4.
                same(state, "SelectKindName", 2, "face");
                same(state, "SelectKindName", 4, "plane");
                same(state, "SelectKindName", -5, "kind -5");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "13 values: line, arc, spline, coincident, aligned, opposite, closest, face, plane and four unknown" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING
        // =================================================================

        private const string LinesRead = "List<double[]> drawnPieces = DrawnPieces(target);";
        private const string MarginJudged = "if (InTheMarginOnly(cx, cy, radius, drawn, drawnPieces))";
        private const string OutlineCheck = "if (cx - radius < xMin || cx + radius > xMax ||";
        private const string DrawnSaid = "[\"drawing_of_the_view_mm\"] = BoxFromCentre(drawn, outline),";
        private const string StateDrawn = "var d = SwOperations.DrawnBox(v);";
        private const string StateDrawnSaid = "entry[\"drawn_mm\"] = ";
        private const string SuppressedLeft = "if (!isFixed && topLevel != false && isSuppressed) suppressedNotJudged++;";
        private const string PatternLeft = "else if (!isFixed && topLevel != false && byPattern && itsMates <= 0)";
        private const string PatternRead = "try { byPattern = c.IsPatternInstance(); } catch { }";
        private const string LeftOutSaid = "string leftOut = NotJudgedText(suppressedNotJudged, placedByPattern);";
        private const string FoldersRead = "var folders = ComponentFolders(assemblyDoc as IModelDoc2);";
        private const string FoldersSaid = "entry[\"in_folders\"] = itsFolders.Split(FolderSeparator);";
        private const string EndTagLeft = "folderName.IndexOf(\"___EndTag___\", StringComparison.Ordinal) < 0";
        private const string CatalogMargin = "I REFUSE IF THE CIRCLE LIES IN THE MARGIN OF THE VIEW";
        private const string SketchCase = "case \"sketch_details\": return SketchDetails(model, arguments);";
        private const string AlignmentRead = "try { entry[\"alignment\"] = AlignmentName(mate.Alignment); }";
        private const string ValueRead = "entry[\"value\"] = MateValue(mate);";
        private const string KindsSaid = "entry[\"entity_kinds\"] = kinds;";

        private static readonly string[][] Files =
        {
            new[] { "detail", "Oce.Solid.Agent.Core", "DrawingDetailOperations.cs" },
            new[] { "drawingstate", "Oce.Solid.Agent.Core", "DrawingState.cs" },
            new[] { "assembly", "Oce.Solid.Agent.Core", "AssemblyState.cs" },
            new[] { "catalog", Path.Combine("Oce.Solid.Agent.Core", "Catalog"), "Operations.cs" },
            new[] { "operations", "Oce.Solid.Agent.Core", "SwOperations.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "detail", LinesRead, "the lines of the view are not read, and the margin cannot be told from the drawing (N62)" },
            new[] { "detail", MarginJudged, "a circle in the margin is drawn and found empty after (N62)" },
            new[] { "detail", OutlineCheck, "a circle out of the outline is no longer refused (N62 must not lose the old check)" },
            new[] { "detail", DrawnSaid, "the answer does not say where the view draws (N62)" },
            new[] { "drawingstate", StateDrawn, "the state does not read what a view draws (N62)" },
            new[] { "drawingstate", StateDrawnSaid, "the state does not say «drawn_mm» (N62)" },
            new[] { "assembly", SuppressedLeft, "a suppressed component is judged as laid down with no mate (N64)" },
            new[] { "assembly", PatternLeft, "a component placed by a pattern is judged as laid down with no mate (N64)" },
            new[] { "assembly", PatternRead, "whether a component is a pattern instance is not read (N64)" },
            new[] { "assembly", LeftOutSaid, "the verdict does not say what it left out (N64)" },
            new[] { "assembly", FoldersRead, "the folders of the tree are not read (N65)" },
            new[] { "assembly", FoldersSaid, "the state does not say the folders of a component (N65)" },
            new[] { "assembly", EndTagLeft, "the end of a folder is listed as a folder (N65, scenario t62)" },
            new[] { "catalog", CatalogMargin, "the description of detail_view does not say the refusal of the margin (N62)" },
            new[] { "operations", SketchCase, "a plan cannot run «sketch_details» (N63)" },
            new[] { "assembly", AlignmentRead, "the state does not say the alignment of a mate (N63)" },
            new[] { "assembly", ValueRead, "the state does not say the value of a mate (N63)" },
            new[] { "assembly", KindsSaid, "the state does not say the kinds of the mate entities (N63)" },
        };

        private static List<string> WiringProblems(Dictionary<string, string> sources)
        {
            var wrong = new List<string>();
            foreach (var line in Lines)
            {
                string text;
                if (!sources.TryGetValue(line[0], out text) || text == null) { wrong.Add(line[0] + " not found"); continue; }
                if (!text.Contains(line[1])) wrong.Add(line[2]);
            }
            string detail;
            if (sources.TryGetValue("detail", out detail) && detail != null)
            {
                int read = detail.IndexOf(LinesRead, StringComparison.Ordinal);
                int judged = detail.IndexOf(MarginJudged, StringComparison.Ordinal);
                if (read < 0 || judged < read) wrong.Add("the margin is judged before the lines are read (N62)");
            }
            string assembly;
            if (sources.TryGetValue("assembly", out assembly) && assembly != null)
            {
                int read = assembly.IndexOf(PatternRead, StringComparison.Ordinal);
                int left = assembly.IndexOf(PatternLeft, StringComparison.Ordinal);
                if (read < 0 || left < read) wrong.Add("the pattern is judged before it is read (N64)");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: the lines of a view read before the circle is judged, the old check on the outline kept, the drawing said in the answer and in the state; the suppressed and patterned components left out after they are read and said in the verdict; the folders read, said, their end left out; sketch_details runnable, the alignment, value and entity kinds of a mate said";
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Files)
            {
                var path = Path.Combine(Root(), f[1], f[2]);
                sources[f[0]] = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            var real = WiringProblems(sources);

            // Teeth: each line taken away, each order swapped.
            var blind = new List<string>();
            int copies = 0;
            Action<string, string, Func<string, string>> mutate = (what, key, change) =>
            {
                string text;
                if (!sources.TryGetValue(key, out text) || text == null) return;
                var changed = change(text);
                if (changed == text) { blind.Add(what + " (the copy did not change)"); return; }
                var copy = new Dictionary<string, string>(sources, StringComparer.Ordinal) { [key] = changed };
                copies++;
                if (WiringProblems(copy).Count == 0) blind.Add(what);
            };
            foreach (var line in Lines)
            {
                var anchor = line[1];
                mutate("without «" + anchor + "» in " + line[0], line[0], t => t.Replace(anchor, "/* gone */"));
            }
            mutate("the margin judged before the lines are read", "detail",
                   t => t.Replace(MarginJudged, "/* moved */").Replace(LinesRead, MarginJudged + "\n" + LinesRead));
            mutate("the pattern judged before it is read", "assembly",
                   t => t.Replace(PatternLeft, "/* moved */").Replace(PatternRead, PatternLeft + "\n" + PatternRead));

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : Lines.Length + " lines, two orders; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
