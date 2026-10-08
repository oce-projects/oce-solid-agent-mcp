using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// SPLINES THROUGH CreateSpline3 WITH NATURAL ENDS (L1-1), AND THE CENTRE
    /// MARKS THE BRIDGE PUTS ON THE HOLES OF BARE VIEWS WHEN IT MAKES THE
    /// STANDARD VIEWS, Z-C21.13.
    ///
    /// Neither can be a red bench case:
    ///  * the spline migration draws the same gear as the obsolete call, which
    ///    is its point: the 17-tooth gear case is green before and after, and
    ///    it is the judge of SimulateNaturalEnds (probe_spline3: true 8808.586
    ///    mm3, false 8806.568, tolerance 0.5). What can go back without a
    ///    sound is the flag, the AddToDB switch around the call and the count
    ///    on a returned segment, so this group pins them in the source. The
    ///    obsolete call itself is kept out by ObsoleteCallsContract (its
    ///    frozen key is gone);
    ///  * the views left with zero centre marks were seen in long bench runs
    ///    and no operation of the bridge turns off the document option that
    ///    makes SOLIDWORKS put them (the bench group «bare-views» does it by
    ///    COM, and is the red case since library 0.152.1). So this group
    ///    checks that the standard views walk every view with zero marks
    ///    through the SAME method the centre-mark operation uses
    ///    (MarkHolesOfView, not a copy of it: one AutoInsertCenterMarks2 call
    ///    in the library, inside that method, never on a view where a hole is
    ///    already marked because it doubles the marks, and no AddCenterMark,
    ///    which adds nothing on a bare view: probe of 8/10/2026) and answer how
    ///    many marks the bridge added next to the count after the rebuild.
    ///
    /// Every check also runs on changed copies of the real source, each of
    /// which must turn it red: a check never seen red proves nothing. No
    /// SOLIDWORKS, no build: the source is read as text.
    /// </summary>
    internal static class Zc2113SplineCentreMarksContract
    {
        private const string Library = "Oce.Solid.Agent.Core";

        internal static void RunAll()
        {
            Group("splines through CreateSpline3 with natural ends, centre marks on bare views (Z-C21.13)");

            string sw, drawing, marks;
            try
            {
                sw = File.ReadAllText(Path.Combine(Root(), Library, "SwOperations.cs"));
                drawing = File.ReadAllText(Path.Combine(Root(), Library, "DrawingOperations.cs"));
                marks = File.ReadAllText(Path.Combine(Root(), Library, "CenterMarkOperations.cs"));
            }
            catch (Exception ex)
            {
                Check("the three library sources can be read", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            // ── L1-1: THE SPLINE CALL ────────────────────────────────────
            var spline = SplineProblems(sw);
            Check("L1-1: splines through CreateSpline3, natural ends true, AddToDB off around the call, counted on a returned segment",
                  spline.Count == 0,
                  spline.Count == 0
                      ? "one CreateSpline3 call, inside CreateSplineThroughPoints; SplineSimulateNaturalEnds = true; AddToDB and " +
                        "DisplayWhenAdded switched and put back in a finally; the caller counts a spline only when a segment came back"
                      : string.Join(" | ", spline));

            // The call site, as the source writes it (its names are the caller's).
            var callSite = Regex.Match(sw, @"CreateSplineThroughPoints\([^()]*\)\s*!=\s*null");
            string countAnchor = callSite.Success ? callSite.Value : "CreateSplineThroughPoints(";
            var splineChanges = new[]
            {
                Change(0, "natural ends false", "SplineSimulateNaturalEnds = true", "SplineSimulateNaturalEnds = false"),
                Change(0, "AddToDB left on", "sketches.AddToDB = false;", ""),
                Change(0, "the state not put back", "                    try { sketches.AddToDB = addToDbBefore;", "                    try {"),
                Change(0, "a spline counted without its return", countAnchor, countAnchor + " || true"),
                Change(0, "a second CreateSpline3 call outside the helper", "private static ISketchSegment CreateSplineThroughPoints(",
                       "private static object SecondSpline(ISketchManager s, double[] d) { return s.CreateSpline3(d, null, null, true, out _); }\n" +
                       "        private static ISketchSegment CreateSplineThroughPoints("),
            };
            var splineRed = SeenRed("spline", new[] { sw }, s => SplineProblems(s[0]), splineChanges);
            Check("the spline check turns red on each changed copy", splineRed.Count == 0,
                  splineRed.Count == 0 ? splineChanges.Length + " changes, each one red" : string.Join(" | ", splineRed));

            // ── THE CENTRE MARKS OF THE STANDARD VIEWS ───────────────────
            var centre = CentreMarkProblems(drawing, marks);
            Check("the standard views mark the holes of views with zero marks through the shared walk, and say how many",
                  centre.Count == 0,
                  centre.Count == 0
                      ? "MarkHolesOfView called for the views with zero marks, no insertion of their own; the answer carries " +
                        "centre_marks_after_rebuild and centre_marks_added_by_bridge; one AutoInsertCenterMarks2 in the marks source, " +
                        "inside MarkHolesOfView after the guard on holes already marked, no AddCenterMark; the centre-mark operation calls it too"
                      : string.Join(" | ", centre));

            // Source 0 is the drawing source, source 1 the marks source; each
            // change hits the first occurrence of its anchor in its source.
            var centreChanges = new[]
            {
                Change(0, "the shared walk not called", "MarkHolesOfView(", "NoWalk("),
                Change(0, "every view walked, not only the bare ones", "if (n == 0) viewsWithoutMarks.Add(view);", "viewsWithoutMarks.Add(view);"),
                Change(0, "the views already on the sheet walked too", "if (viewName == null || viewsBefore == null || viewsBefore.Contains(viewName)) continue;", ""),
                Change(0, "the count of the bridge not answered", "[\"centre_marks_added_by_bridge\"] = addedByBridge,", ""),
                Change(0, "a walk of its own", "if (viewsWithoutMarks.Count > 0)",
                       "if (viewsWithoutMarks.Count > 0) other.AutoInsertCenterMarks2(1, 0, true, true, true, 0.0025, 0.0025, false, false, 0);\n            if (viewsWithoutMarks.Count > 0)"),
                Change(0, "the dead AddCenterMark back in the standard views", "if (viewsWithoutMarks.Count > 0)",
                       "if (viewsWithoutMarks.Count > 0) other.AddCenterMark(0.0025, false);\n            if (viewsWithoutMarks.Count > 0)"),
                Change(1, "the centre-mark operation with a copy of the walk", "MarkHolesOfView(", "CopiedWalk("),
                Change(1, "a second AutoInsertCenterMarks2 in the marks source", "private static HoleCentreMarks MarkHolesOfView(",
                       "private static void Other(IView v) { v.AutoInsertCenterMarks2(1, 0, true, true, true, 0.0025, 0.0025, false, false, 0); }\n" +
                       "        private static HoleCentreMarks MarkHolesOfView("),
                Change(1, "the dead AddCenterMark back in the marks source", "private static HoleCentreMarks MarkHolesOfView(",
                       "private static void Other(IDrawingDoc d) { d.AddCenterMark(0.0025, false); }\n" +
                       "        private static HoleCentreMarks MarkHolesOfView("),
                Change(1, "the guard on holes already marked removed (the marks double)", "if (marks.AlreadyMarked > 0)", "if (false)"),
            };
            var centreRed = SeenRed("centre marks", new[] { drawing, marks }, s => CentreMarkProblems(s[0], s[1]), centreChanges);
            Check("the centre-mark check turns red on each changed copy", centreRed.Count == 0,
                  centreRed.Count == 0 ? centreChanges.Length + " changes, each one red" : string.Join(" | ", centreRed));
        }

        // ── the checks, on source text ───────────────────────────────────────

        /// <summary>What is wrong with the spline call in this source; empty when nothing.</summary>
        internal static List<string> SplineProblems(string source)
        {
            var problems = new List<string>();
            var code = CodeOnly(source);

            if (!Regex.IsMatch(code, @"\bconst\s+bool\s+SplineSimulateNaturalEnds\s*=\s*true\s*;"))
                problems.Add("SplineSimulateNaturalEnds is not a constant true (probe_spline3 S5: true reproduces the gear, false gives 8806.568 mm3)");

            int calls = Count(code, ".CreateSpline3(");
            if (calls != 1) problems.Add(calls + " CreateSpline3 calls in the source, expected exactly 1 (inside CreateSplineThroughPoints)");

            string helper = MemberBody(code, "private static ISketchSegment CreateSplineThroughPoints(");
            if (helper == null) problems.Add("CreateSplineThroughPoints is not in the source");
            else
            {
                if (Count(helper, ".CreateSpline3(") != 1) problems.Add("the CreateSpline3 call is not inside CreateSplineThroughPoints");
                if (!Regex.IsMatch(helper, @"\.CreateSpline3\(\s*\w+\s*,\s*null\s*,\s*null\s*,\s*SplineSimulateNaturalEnds\s*,"))
                    problems.Add("CreateSpline3 does not get (points, null, null, SplineSimulateNaturalEnds, ...)");
                int off = helper.IndexOf("AddToDB = false", StringComparison.Ordinal);
                int display = helper.IndexOf("DisplayWhenAdded = true", StringComparison.Ordinal);
                int call = helper.IndexOf(".CreateSpline3(", StringComparison.Ordinal);
                if (off < 0 || display < 0 || call < 0 || off > call || display > call)
                    problems.Add("AddToDB is not switched off and DisplayWhenAdded on before the call (the guide: it \"Does not work with\" them)");
                int fin = helper.IndexOf("finally", StringComparison.Ordinal);
                int back = helper.IndexOf("AddToDB = addToDbBefore", StringComparison.Ordinal);
                int backDisplay = helper.IndexOf("DisplayWhenAdded = displayBefore", StringComparison.Ordinal);
                if (fin < 0 || back < fin || backDisplay < fin)
                    problems.Add("AddToDB and DisplayWhenAdded are not put back in a finally after the call");
            }

            if (!Regex.IsMatch(code, @"if\s*\(\s*CreateSplineThroughPoints\([^()]*\)\s*!=\s*null\s*\)\s*\w+\+\+\s*;"))
                problems.Add("the caller does not count a spline only when CreateSplineThroughPoints returned a segment");
            return problems;
        }

        /// <summary>What is wrong with the centre marks of the standard views; empty when nothing.</summary>
        internal static List<string> CentreMarkProblems(string drawingSource, string marksSource)
        {
            var problems = new List<string>();
            var drawing = CodeOnly(drawingSource);
            var marks = CodeOnly(marksSource);

            // The standard views: the member that calls Create1stAngleViews2.
            string views = MemberAround(drawing, ".Create1stAngleViews2(");
            if (views == null) problems.Add("the member that makes the standard views (Create1stAngleViews2) is not in the source");
            else
            {
                if (Count(views, "MarkHolesOfView(") != 1)
                    problems.Add("the standard views do not call MarkHolesOfView once (found " + Count(views, "MarkHolesOfView(") + ")");
                if (!Regex.IsMatch(views, @"==\s*0\s*\)\s*viewsWithoutMarks\.Add\("))
                    problems.Add("the standard views do not keep only the views with zero marks for the walk");
                if (views.IndexOf(".AddCenterMark(", StringComparison.Ordinal) >= 0)
                    problems.Add("the standard views call AddCenterMark themselves: a copy of the walk");
                if (views.IndexOf(".AutoInsertCenterMarks2(", StringComparison.Ordinal) >= 0)
                    problems.Add("the standard views call AutoInsertCenterMarks2 themselves: a copy of the walk");
                if (!Regex.IsMatch(views, @"viewsBefore\.Contains\(\w+\)\)\s*continue\s*;"))
                    problems.Add("the standard views also walk the views that were on the sheet before the call");
                foreach (var key in new[] { "\"centre_marks_after_rebuild\"", "\"centre_marks_added_by_bridge\"" })
                    if (views.IndexOf(key, StringComparison.Ordinal) < 0) problems.Add("the answer of the standard views has no " + key);
            }

            // The walk: one AutoInsertCenterMarks2 in the marks source, inside
            // MarkHolesOfView and after the return on a view whose holes are
            // partly marked (it doubles existing marks: 3 -> 6, probe of
            // 8/10/2026); no AddCenterMark anywhere (it adds nothing on a bare
            // view, same probe); and the centre-mark operation calls the walk.
            string walk = MemberBody(marks, "private static HoleCentreMarks MarkHolesOfView(");
            if (walk == null) problems.Add("MarkHolesOfView is not in the marks source");
            else
            {
                int all = Count(marks, ".AutoInsertCenterMarks2("), inside = Count(walk, ".AutoInsertCenterMarks2(");
                if (all != 1 || inside != 1)
                    problems.Add(all + " AutoInsertCenterMarks2 call(s) in the marks source, " + inside + " inside MarkHolesOfView: expected 1 and 1");
                var guard = Regex.Match(walk, @"if\s*\(\s*marks\.AlreadyMarked\s*>\s*0\s*\)\s*\{[^{}]*return\s+marks\s*;\s*\}");
                int call = walk.IndexOf(".AutoInsertCenterMarks2(", StringComparison.Ordinal);
                if (!guard.Success || call < 0 || guard.Index > call)
                    problems.Add("no return before AutoInsertCenterMarks2 on a view where some hole already has its mark: the marks would double");
            }
            int addCalls = Count(marks, ".AddCenterMark(") + Count(drawing, ".AddCenterMark(");
            if (addCalls != 0)
                problems.Add(addCalls + " AddCenterMark call(s): it adds nothing on a bare view (probe of 8/10/2026), expected none");
            int callers = Count(marks, "MarkHolesOfView(") - (walk == null ? 0 : 1);
            if (callers != 1)
                problems.Add("the centre-mark operation calls MarkHolesOfView " + callers + " time(s), expected 1");
            return problems;
        }

        // ── seen red ─────────────────────────────────────────────────────────

        private sealed class SourceChange
        {
            public int Source;
            public string Name, From, To;
        }

        private static SourceChange Change(int source, string name, string from, string to)
        {
            return new SourceChange { Source = source, Name = name, From = from, To = to };
        }

        /// <summary>
        /// Each change applied alone to its source must leave text that the check
        /// calls wrong. Returns what did not: a change whose anchor is no longer
        /// in the source (the check would then prove nothing), or a changed copy
        /// the check still calls right.
        /// </summary>
        private static List<string> SeenRed(string what, string[] sources, Func<string[], List<string>> check, SourceChange[] changes)
        {
            var misses = new List<string>();
            if (check(sources).Count != 0) { misses.Add(what + ": the untouched source is already red, the changes prove nothing"); return misses; }
            foreach (var c in changes)
            {
                string source = sources[c.Source];
                int at = source.IndexOf(c.From, StringComparison.Ordinal);
                if (at < 0) { misses.Add(what + ", " + c.Name + ": anchor not found in the source (update the change)"); continue; }
                var changed = (string[])sources.Clone();
                changed[c.Source] = source.Substring(0, at) + c.To + source.Substring(at + c.From.Length);
                if (check(changed).Count == 0) misses.Add(what + ", " + c.Name + ": the changed copy is still green");
            }
            return misses;
        }

        // ── source helpers ───────────────────────────────────────────────────

        /// <summary>
        /// The source without its comment lines and trailing line comments, so
        /// that a call named in a comment is not counted. String literals are
        /// kept: the answer keys are looked for in them.
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
        /// the last line at member indentation that opens a member before it, to
        /// the closing brace at member indentation after it.
        /// </summary>
        private static string MemberAround(string code, string inside)
        {
            int at = code.IndexOf(inside, StringComparison.Ordinal);
            if (at < 0) return null;
            int start = Math.Max(code.LastIndexOf("\n        private ", at, StringComparison.Ordinal),
                                 code.LastIndexOf("\n        internal ", at, StringComparison.Ordinal));
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
