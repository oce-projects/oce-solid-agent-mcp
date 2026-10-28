using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 13 OF REPORT 8CD843 (27/10/2026), WHERE THE BENCH
    /// CANNOT REACH.
    ///
    /// N47: the three details of the drawing were empty in the PDF; the circle
    /// went in the sketch of the view with sheet coordinates, and a probe on a copy
    /// measured 0 visible edges that way and 11 with the coordinates of the view
    /// sketch. N48: the cutting line of A-A, drawn on the sheet sketch, was given to
    /// the view below, and «the hatching is there» held for the wrong view. N49: the
    /// new sheet had no format (no frame, no title block). N50: the balloons
    /// renumbered the bill of materials, and the printed table was not «rows_read».
    /// N51: a note after a section went into the view activated for the line. And
    /// the observation on «first free place (147/144 mm)».
    ///
    /// Two halves, as Zc2195: the decisions by reflection, the wiring on the
    /// sources, every rule first red on a changed copy. On library 0.174.3 and
    /// bridge 0.152.3 every check is red.
    /// </summary>
    internal static class Zc2196DrawingRound13Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 13 of report 8cd843: the circle and the cutting line in the coordinates of the view, the format of a new sheet, the bill of materials after the balloons, the note after a section (N47-N51)");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 13 fixes",
                       library + " is missing: build the library first (build.ps1)");
                return;
            }
            Type sw;
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
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (sw == null) { Check("SwOperations is in the library", false, "type missing"); return; }

            IntoTheViewSketch(sw);
            WhenTheViewSketchCannotBeUsed(sw);
            TheViewAsked(sw);
            FormatOfTheNewSheet(sw);
            RowsAfterTheBalloons(sw);
            FreePlaceSaid(sw);
            SettledWhenTheRoundTripChangesNothing(sw);
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

        private static void IntoTheViewSketch(Type sw)
        {
            const string label = "N47, N48: a point of the sheet goes into the sketch of a view as (sheet - centre) / scale";
            try
            {
                var wrong = new List<string>();
                // The probe: GetXform of the view below [94.5 mm, 110.69 mm, 0.5], the
                // circle wanted at (134.5, 110.69) mm of sheet, drawn at (80, 0) mm.
                var xform = new[] { 0.0945, 0.11069, 0.5 };
                var p = Call(sw, "SheetToViewSketch", xform, 0.1345, 0.11069) as double[];
                if (p == null || p.Length != 2 || Math.Abs(p[0] - 0.08) > 1e-9 || Math.Abs(p[1]) > 1e-9)
                    wrong.Add("the circle of the probe: " + (p == null ? "null" : p[0] + ", " + p[1]) + " instead of 0.08, 0");
                // The line of the view above: x 94.5 mm, y 212.69 mm, centre 229.49 mm.
                var q = Call(sw, "SheetToViewSketch", new[] { 0.0945, 0.22949, 0.5 }, 0.0945, 0.21269) as double[];
                if (q == null || Math.Abs(q[0]) > 1e-9 || Math.Abs(q[1] + 0.0336) > 1e-9)
                    wrong.Add("the line of the probe: " + (q == null ? "null" : q[0] + ", " + q[1]) + " instead of 0, -0.0336");
                if (Call(sw, "SheetToViewSketch", null, 0.1, 0.1) != null) wrong.Add("no transform gave a point");
                if (Call(sw, "SheetToViewSketch", new[] { 0.1, 0.1, 0.0 }, 0.1, 0.1) != null) wrong.Add("a scale of 0 gave a point");
                if (Call(sw, "SheetToViewSketch", new[] { 0.1, 0.1 }, 0.1, 0.1) != null) wrong.Add("a transform of two numbers gave a point");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "the circle and the line of the probe land where they were drawn; no transform, no point" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void WhenTheViewSketchCannotBeUsed(Type sw)
        {
            const string label = "N47, N48: a view that does not activate, has no transform or is turned is refused, never drawn on the sheet sketch";
            try
            {
                var wrong = new List<string>();
                var xform = new[] { 0.0945, 0.11069, 0.5 };
                var off = (string)Call(sw, "ViewSketchProblem", false, xform, 0.0, "V1", "cutting line");
                foreach (var part in new[] { "could not activate the view «V1»", "cutting line", "another view" })
                    if (off == null || !off.Contains(part)) wrong.Add("not activated lacks «" + part + "»: " + (off ?? "null"));
                var none = (string)Call(sw, "ViewSketchProblem", true, null, 0.0, "V1", "circle of the detail");
                if (none == null || !none.Contains("IView.GetXform")) wrong.Add("no transform: " + (none ?? "null"));
                var turned = (string)Call(sw, "ViewSketchProblem", true, xform, Math.PI / 2, "V1", "circle of the detail");
                if (turned == null || !turned.Contains("turned by 90 degrees")) wrong.Add("turned: " + (turned ?? "null"));
                if (Call(sw, "ViewSketchProblem", true, xform, 0.0, "V1", "cutting line") != null) wrong.Add("a good view was refused");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "three refusals with the view and the reason; a view at 0 degrees with its transform passes" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void TheViewAsked(Type sw)
        {
            const string label = "N47, N48: a detail or a section made from another view, or a detail with no edge, is refused; what cannot be read is said, not refused";
            try
            {
                var wrong = new List<string>();
                var other = (string)Call(sw, "NotTheViewAsked", "section", "Drawing View1", "Drawing View2", -1);
                if (other == null || !other.Contains("made from the view «Drawing View2», not from «Drawing View1»"))
                    wrong.Add("the section of the view below: " + (other ?? "null"));
                var empty = (string)Call(sw, "NotTheViewAsked", "detail", "V2", "V2", 0);
                if (empty == null || !empty.Contains("visible edges 0")) wrong.Add("the empty detail: " + (empty ?? "null"));
                if (Call(sw, "NotTheViewAsked", "detail", "V2", "V2", 11) != null) wrong.Add("the detail of the probe with 11 edges was refused");
                if (Call(sw, "NotTheViewAsked", "detail", "V2", "v2", 11) != null) wrong.Add("the same name in another case was refused");
                if (Call(sw, "NotTheViewAsked", "section", "V1", null, -1) != null) wrong.Add("a base that cannot be read was refused");
                if (Call(sw, "NotTheViewAsked", "detail", "V1", "V1", -1) != null) wrong.Add("edges that cannot be read were refused");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "the section of round 13 and an empty detail refused; the detail of the probe passes" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void FormatOfTheNewSheet(Type sw)
        {
            const string label = "N49: the new sheet takes the format file of the current sheet, and a format that did not come is refused";
            try
            {
                var wrong = new List<string>();
                const string a3 = @"S:\Settings\Sheet formats\A3.slddrt";
                if ((string)Call(sw, "SheetFormatFile", a3) != a3) wrong.Add("the format of the first sheet of round 13 is not read");
                if ((string)Call(sw, "SheetFormatFile", " x.SLDDRT ") != "x.SLDDRT") wrong.Add("a format with spaces and capitals");
                if (Call(sw, "SheetFormatFile", "") != null) wrong.Add("the empty name of the second sheet of round 13 is a format");
                if (Call(sw, "SheetFormatFile", "*.drt") != null) wrong.Add("«*.drt» is a format");
                if (Call(sw, "SheetFormatFile", (object)null) != null) wrong.Add("null is a format");
                if (Call(sw, "NewSheetFormatProblem", null, null) != null) wrong.Add("no format asked, none read: refused");
                var lost = (string)Call(sw, "NewSheetFormatProblem", a3, null);
                if (lost == null || !lost.Contains("has no format")) wrong.Add("the lost format: " + (lost ?? "null"));
                var other = (string)Call(sw, "NewSheetFormatProblem", a3, "B.slddrt");
                if (other == null || !other.Contains("reads «B.slddrt»")) wrong.Add("another format: " + (other ?? "null"));
                if (Call(sw, "NewSheetFormatProblem", a3, a3.ToLowerInvariant()) != null) wrong.Add("the same file in other case was refused");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "the .slddrt read, «» and «*.drt» are none; a format lost or changed refused" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void RowsAfterTheBalloons(Type sw)
        {
            const string label = "N50: the rows of the bill of materials before and after the balloons are compared in order";
            try
            {
                var wrong = new List<string>();
                Func<string[], List<string[]>> rows = parts =>
                {
                    var r = new List<string[]>();
                    for (int i = 0; i < parts.Length; i++) r.Add(new[] { (i + 1).ToString(), parts[i], "", "1" });
                    return r;
                };
                var before = rows(new[] { "26P000007", "26P000008", "26P000009", "26P000010", "26P000011", "26P000012", "26P000013" });
                var after = rows(new[] { "26P000010", "26P000011", "26P000009", "26P000008", "26P000007", "26P000013", "26P000012" });
                if ((bool)Call(sw, "SameRows", before, after)) wrong.Add("the renumbering of round 13 reads as the same rows");
                if (!(bool)Call(sw, "SameRows", before, rows(new[] { "26P000007", "26P000008", "26P000009", "26P000010", "26P000011", "26P000012", "26P000013" })))
                    wrong.Add("the same rows read as different");
                if ((bool)Call(sw, "SameRows", before, before.GetRange(0, 6))) wrong.Add("a row less reads as the same");
                if ((bool)Call(sw, "SameRows", null, before)) wrong.Add("no reading before reads as the same");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "the renumbering of round 13 is seen; the same rows are the same" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void FreePlaceSaid(Type sw)
        {
            const string label = "round 13: «first free place» says that the numbers are the bottom-left corner";
            try
            {
                var text = (string)Call(sw, "FirstFreePlaceText", 0.147, 0.144);
                bool ok = text != null && text.Contains("bottom-left corner at x 147 mm, y 144 mm");
                Check(label, ok, ok ? text : (text ?? "null"));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void SettledWhenTheRoundTripChangesNothing(Type sw)
        {
            const string label = "scenario t39: a detail view is settled only when a round trip of 1 mm leaves its outline where and as big as it was";
            try
            {
                var wrong = new List<string>();
                // Detail B of the probe: 56.1 x 31.2 mm at birth, 100.3 x 50.5 mm after the round trip.
                var birth = new[] { 0.2074, 0.2139, 0.2635, 0.2451 };
                var built = new[] { 0.1853, 0.2043, 0.2856, 0.2548 };
                if ((bool)Call(sw, "SameRectangle", birth, built, 0.0001)) wrong.Add("the outline of birth and the built one read as the same");
                if (!(bool)Call(sw, "SameRectangle", built, new[] { 0.18535, 0.20435, 0.28565, 0.25485 }, 0.0001))
                    wrong.Add("0.05 mm apart read as different");
                if ((bool)Call(sw, "SameRectangle", built, new[] { 0.1863, 0.2043, 0.2866, 0.2548 }, 0.0001))
                    wrong.Add("a view moved by 1 mm reads as the same");
                if ((bool)Call(sw, "SameRectangle", null, built, 0.0001)) wrong.Add("an outline not read reads as the same");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "birth and built told apart, 0.05 mm the same, 1 mm and an unread outline not" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING, ON THE SOURCES
        // =================================================================

        private const string XformRead = "try { xform = target.GetXform() as double[]; } catch { }";
        private const string Activate = "try { inView = drawing.ActivateView(targetName); } catch { }";
        private const string CircleInView = "circle = model.SketchManager.CreateCircleByRadius(centre[0], centre[1], 0.0, radius / xform[2])";
        private const string OldCircle = "circle = model.SketchManager.CreateCircleByRadius(cx, cy, 0.0, radius)";
        private const string LineInView = "line = model.SketchManager.CreateLine(from[0], from[1], 0.0, to[0], to[1], 0.0)";
        private const string OldLine = "line = model.SketchManager.CreateLine(x1, y1, 0.0, x2, y2, 0.0)";
        private const string DetailBase = "string notAsked = NotTheViewAsked(\"detail\", targetName, detailBase, detailEdges);";
        private const string SectionBase = "string notAsked = NotTheViewAsked(\"section\", targetName, sectionBase, -1);";
        private const string DetailProblem = "string noSketch = ViewSketchProblem(inView, xform, angle, targetName, \"circle of the detail\");";
        private const string SectionProblem = "string noSketch = ViewSketchProblem(inView, xform, angle, targetName, \"cutting line\");";
        private const string SectionBorn = "born = drawing.CreateSectionViewAt5(";
        private const string BackToSheet = "try { drawing.EditSheet(); } catch { }";
        private const string SectionViewsAfter = "int viewsAfter = 0, walkedAfter = 0;";
        private const string HatchRead = "try { hatching = sectionView.GetFaceHatchCount(); } catch { }";
        private const string FormatPassed = "format != null ? (int)swDwgTemplates_e.swDwgTemplateCustom : (int)swDwgTemplates_e.swDwgTemplateNone,";
        private const string OldNoFormat = "\"\",                        // TemplateName: none";
        private const string FormatReadBack = "string formatWrong = NewSheetFormatProblem(format, formatAfter);";
        // The order chosen by «order» since library 0.174.8 (report 8cd843 N60), still set
        // before AutoBalloon5 and never left to the default.
        private const string OrderSet = "opt.ItemOrder = (int)itemOrder;";
        private const string RowsBefore = "var rowsBefore = BomRowsOfDrawing(drawing);";
        private const string RowsAfter = "var rowsAfter = BomRowsOfDrawing(drawing);";
        private const string BalloonCall = "born = drawing.AutoBalloon5(opt);";
        private const string FreeText = "location = FirstFreePlaceText(x, y);";
        private const string Settle = "if (!SettleDetailView(view, out readings))";
        private const string ReadWithLabel = "double[] o = ViewWithItsNotes(view), p = null, outline = null;";

        private static readonly string[][] Files =
        {
            new[] { "detail", "Oce.Solid.Agent.Core", "DrawingDetailOperations.cs" },
            new[] { "section", "Oce.Solid.Agent.Core", "DrawingSectionOperations.cs" },
            new[] { "sheet", "Oce.Solid.Agent.Core", "SheetOperations.cs" },
            new[] { "bom", "Oce.Solid.Agent.Core", "BomOperations.cs" },
            new[] { "place", "Oce.Solid.Agent.Core", "SheetPlacementOperations.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "detail", XformRead, "the detail does not read where its view is and at what scale (N47)" },
            new[] { "detail", CircleInView, "the circle is drawn with sheet coordinates in the sketch of the view, and the detail is empty (N47)" },
            new[] { "detail", DetailProblem, "a view that does not activate sends the circle to the sheet sketch (N47)" },
            new[] { "detail", DetailBase, "an empty detail, or one of another view, is delivered (N47)" },
            new[] { "section", XformRead, "the section does not read where its view is and at what scale (N48)" },
            new[] { "section", Activate, "the cutting line goes on the sheet sketch, and to another view (N48)" },
            new[] { "section", LineInView, "the cutting line is drawn with sheet coordinates (N48)" },
            new[] { "section", SectionProblem, "a view that does not activate sends the line to the sheet sketch (N48)" },
            new[] { "section", SectionBase, "a section of another view is delivered with its hatching (N48)" },
            new[] { "sheet", FormatPassed, "the new sheet is born with no format (N49)" },
            new[] { "sheet", FormatReadBack, "a new sheet whose format did not come is called done (N49)" },
            new[] { "bom", OrderSet, "the order of the balloons is left to a default that renumbers the table (N50)" },
            new[] { "bom", RowsBefore, "the rows before the balloons are not read (N50)" },
            new[] { "bom", RowsAfter, "the renumbered rows are not given after the balloons (N50)" },
            new[] { "place", FreeText, "«first free place» gives two numbers without saying what they are (round 13)" },
            new[] { "detail", Settle, "a detail is judged and moved on the outline it has at birth, not the one it gets (scenario t39)" },
        };

        /// <summary>The lines that must not come back, with what they bring back.</summary>
        private static readonly string[][] Gone =
        {
            new[] { "detail", OldCircle, "the circle with sheet coordinates in the sketch of the view (N47)" },
            new[] { "section", OldLine, "the cutting line with sheet coordinates (N48)" },
            new[] { "sheet", OldNoFormat, "a new sheet with no format whatever the current sheet has (N49)" },
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
            foreach (var line in Gone)
            {
                string text;
                if (sources.TryGetValue(line[0], out text) && text != null && text.Contains(line[1])) wrong.Add(line[2]);
            }
            string detail;
            if (sources.TryGetValue("detail", out detail) && detail != null)
            {
                int problem = detail.IndexOf(DetailProblem, StringComparison.Ordinal);
                int circle = detail.IndexOf(CircleInView, StringComparison.Ordinal);
                if (problem < 0 || circle < 0 || problem > circle)
                    wrong.Add("the circle is drawn before the view is judged (N47)");
                int settle = detail.IndexOf(Settle, StringComparison.Ordinal);
                int read = detail.IndexOf(ReadWithLabel, StringComparison.Ordinal);
                if (settle < 0 || read < 0 || settle > read)
                    wrong.Add("the outline of the detail is read before the view is settled (scenario t39)");
            }
            string section;
            if (sources.TryGetValue("section", out section) && section != null)
            {
                int problem = section.IndexOf(SectionProblem, StringComparison.Ordinal);
                int line = section.IndexOf(LineInView, StringComparison.Ordinal);
                if (problem < 0 || line < 0 || problem > line)
                    wrong.Add("the cutting line is drawn before the view is judged (N48)");
                // N51: back to the sheet between the birth of the section and the count of the views.
                int born = section.IndexOf(SectionBorn, StringComparison.Ordinal);
                int views = section.IndexOf(SectionViewsAfter, StringComparison.Ordinal);
                int back = born < 0 ? -1 : section.IndexOf(BackToSheet, born, StringComparison.Ordinal);
                if (born < 0 || views < 0 || back < 0 || back > views)
                    wrong.Add("the section leaves the view activated for the line, and the next note goes in it (N51)");
                int hatch = section.IndexOf(HatchRead, StringComparison.Ordinal);
                int baseCheck = section.IndexOf(SectionBase, StringComparison.Ordinal);
                if (hatch < 0 || baseCheck < 0 || baseCheck < born)
                    wrong.Add("the base view is judged before the section is born (N48)");
            }
            string bom;
            if (sources.TryGetValue("bom", out bom) && bom != null)
            {
                int before = bom.IndexOf(RowsBefore, StringComparison.Ordinal);
                int call = bom.IndexOf(BalloonCall, StringComparison.Ordinal);
                int order = bom.IndexOf(OrderSet, StringComparison.Ordinal);
                int after = bom.IndexOf(RowsAfter, StringComparison.Ordinal);
                if (before < 0 || call < 0 || order < 0 || after < 0 || before > call || order > call || after < call)
                    wrong.Add("the rows are not read before and after the balloons, or the order is set after them (N50)");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: the circle and the cutting line in the sketch of the view, the base view and the edges judged, back to the sheet after a section, the format of a new sheet, the rows around the balloons, the free place said";
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Files)
            {
                var path = Path.Combine(Root(), f[1], f[2]);
                sources[f[0]] = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            var real = WiringProblems(sources);

            // Teeth: each line taken away, each old line put back, the orders swapped.
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
            foreach (var line in Gone)
            {
                var back = line[1];
                mutate("with «" + back + "» back", line[0], t => t + "\n// " + back);
            }
            mutate("the circle drawn before the view is judged", "detail",
                   t => t.Replace(DetailProblem, "").Replace(CircleInView, CircleInView + ";\n" + DetailProblem));
            mutate("the line drawn before the view is judged", "section",
                   t => t.Replace(SectionProblem, "").Replace(LineInView, LineInView + ";\n" + SectionProblem));
            mutate("no way back to the sheet after the section", "section",
                   t =>
                   {
                       int born = t.IndexOf(SectionBorn, StringComparison.Ordinal);
                       int back = born < 0 ? -1 : t.IndexOf(BackToSheet, born, StringComparison.Ordinal);
                       return back < 0 ? t : t.Remove(back, BackToSheet.Length).Insert(back, "/* gone */");
                   });
            mutate("the outline read before the view is settled", "detail",
                   t => t.Replace(Settle, "if (false)").Replace(ReadWithLabel, ReadWithLabel + "\n" + Settle + " { }"));
            mutate("the base view judged before the section is born", "section",
                   t => t.Replace(SectionBase, "").Replace(SectionBorn, SectionBase + "\n" + SectionBorn));
            mutate("the rows read after the balloons only", "bom",
                   t => t.Replace(RowsBefore, "").Replace(RowsAfter, RowsAfter + "\n" + RowsBefore));
            mutate("the order set after the balloons", "bom",
                   t => t.Replace(OrderSet, "").Replace(BalloonCall, BalloonCall + "\n" + OrderSet));

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : Lines.Length + " lines, " + Gone.Length + " old lines kept out, seven orders; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
