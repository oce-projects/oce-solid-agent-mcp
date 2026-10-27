using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 7 OF REPORT 8CD843 (27/10/2026), WHERE THE BENCH
    /// CANNOT REACH.
    ///
    /// N23: on a view of an assembly the edges come in the coordinates of their
    /// component; three dimensions came out at 1.725, 15.23° and 159.25° and
    /// were called placed. N24: «undo» after a plan on a drawing proposed
    /// delete_feature, which does not take a dimension away. N25: a title block
    /// field showing only « Kg» was counted as shown. N26: «unsaved_changes»
    /// stayed false after set_property. N27: a new drawing took the title of an
    /// open one without a word. N28: inspect_part of an assembly gave no
    /// «property».
    ///
    /// The replay of these fixes on SOLIDWORKS (t26n, bridge 0.150.0) found two
    /// more: the N27 warning sat only in create_drawing, while it is the first
    /// model view that renames a drawing without a file; and a note asked on the
    /// sheet went into the view the dimensions had left active. Both are here.
    ///
    /// Two halves, as Z-C21.8. The decisions are small static methods, called
    /// here by reflection with the cases a bench cannot build (a chamfer angle
    /// of 159.25°, a field that shows « Kg»). The wiring is read on the sources,
    /// and every wiring rule first turns red on a copy where its line is gone.
    ///
    /// On library 0.149.0 every check is red: the methods and the lines are
    /// missing.
    /// </summary>
    internal static class Zc2190DrawingRound7Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 7 of report 8cd843: assembly views, undo on a drawing, title block, unsaved, twin titles (N23-N28), and the note on the sheet");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 7 fixes",
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
                sw = Assembly.Load(File.ReadAllBytes(library)).GetType("Oce.Solid.Agent.Core.SwOperations", false);
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (sw == null) { Check("SwOperations is in the library", false, "type missing"); return; }

            ChamferValues(sw);
            OverallRules(sw);
            AnnotationNames(sw);
            FixedText(sw);
            TwinTitle(sw);
            NoteWhere(sw);

            var bridge = BridgeOrNotHere("the rules of «view_edges» and of the undo after a plan on a drawing");
            if (bridge == null) return;
            Type tools;
            try { tools = Load.Class(Load.Bridge(bridge), "McpTools"); }
            catch (Exception ex) { Check("the bridge loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (tools == null) { Check("McpTools is in the bridge", false, "type missing"); return; }
            ViewEdgesArgument(tools);
            UndoOnDrawing(tools);
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

        private static void ChamferValues(Type sw)
        {
            const string label = "N23: a chamfer dimension read back with an angle outside (0°, 90°) or a leg of zero or less is judged impossible (159.25° and -0.4 mm of the report)";
            try
            {
                var wrong = new List<string>();
                Func<double?, double?, string> problem = (angle, leg) => (string)Call(sw, "ChamferValuesProblem", angle, leg);
                if (problem(45, 1.5) != null) wrong.Add("45° 1.5 mm refused: " + problem(45, 1.5));
                if (problem(15.23, 0.4) != null) wrong.Add("15.23° 0.4 mm refused");
                if (problem(null, null) != null) wrong.Add("unknown values judged");
                var obtuse = problem(159.25, 0.4);
                if (obtuse == null || !obtuse.Contains("159.25") || !obtuse.Contains("between 0 and 90")) wrong.Add("159.25°: " + (obtuse ?? "taken"));
                if (problem(0, 1) == null) wrong.Add("0° taken");
                if (problem(90, 1) == null) wrong.Add("90° taken");
                var negative = problem(45, -0.4);
                if (negative == null || !negative.Contains("-0.4") || !negative.Contains("zero or less")) wrong.Add("-0.4 mm: " + (negative ?? "taken"));
                if (problem(45, 0) == null) wrong.Add("a leg of 0 taken");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "3 taken, 5 refused" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void OverallRules(Type sw)
        {
            const string label = "N23: «overall» read (both by default, none when edges are named, bool, any case), the key of an overall size, and the outermost straight edges judged against what the view shows";
            try
            {
                var wrong = new List<string>();
                Func<object, bool, Tuple<string, string>> mode = (raw, named) =>
                {
                    var args = new object[] { raw, named, null };
                    var m = ReflectionLog.Global.Method(sw, "OverallMode", Any);
                    if (m == null) throw new MissingMethodException("SwOperations.OverallMode");
                    var r = (string)m.Invoke(null, args);
                    return Tuple.Create(r, (string)args[2]);
                };
                if (mode(null, false).Item1 != "both") wrong.Add("left out, no edges: " + mode(null, false).Item1);
                if (mode(null, true).Item1 != "none") wrong.Add("left out, edges named: " + mode(null, true).Item1);
                if (mode(true, true).Item1 != "both" || mode(false, false).Item1 != "none") wrong.Add("a bool is not read");
                if (mode(" Horizontal ", false).Item1 != "horizontal") wrong.Add("« Horizontal » not read");
                var diagonal = mode("diagonal", false);
                if (diagonal.Item1 != null || diagonal.Item2 == null || !diagonal.Item2.Contains("«both», «horizontal», «vertical» or «none»"))
                    wrong.Add("«diagonal»: " + (diagonal.Item1 ?? diagonal.Item2));
                if (mode(5, false).Item1 != null) wrong.Add("5 taken");

                Func<int, double, string> key = (axis, mm) => (string)Call(sw, "OverallKey", axis, mm);
                if (key(0, 175) != "X|175.00" || key(2, 49.996) != "Z|50.00" || key(3, 1) != "?|1.00")
                    wrong.Add("keys " + key(0, 175) + ", " + key(2, 49.996) + ", " + key(3, 1));

                Func<bool, double, double, double, double, double, string> edges = (horizontal, lowEdge, highEdge, lowAll, highAll, scale) =>
                    (string)Call(sw, "OverallEdgesProblem", horizontal, lowEdge, highEdge, lowAll, highAll, scale);
                if (edges(true, 0, 0.1, 0, 0.1, 1) != null) wrong.Add("edges at the extremes refused");
                if (edges(false, 0, 0.1, 0, 0.1000001, 1) != null) wrong.Add("0.0001 mm beyond refused (tolerance 0.01 mm)");
                var single = edges(true, 0.05, 0.05, 0, 0.1, 1);
                if (single == null || !single.Contains("not two different straight edges")) wrong.Add("one edge: " + (single ?? "taken"));
                var left = edges(true, 0.01, 0.1, 0, 0.1, 0.5);
                if (left == null || !left.Contains("on the left") || !left.Contains("20 mm")) wrong.Add("10 mm on the sheet at 1:2: " + (left ?? "taken"));
                var top = edges(false, 0, 0.1, 0, 0.102, 1);
                if (top == null || !top.Contains("on the top") || !top.Contains("2 mm")) wrong.Add("2 mm on top: " + (top ?? "taken"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "6 forms of «overall», 3 keys, 5 edge cases" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void AnnotationNames(Type sw)
        {
            const string label = "N24: «delete_annotation» finds a dimension by its full name or by what comes before an «@», and RD1 does not answer for RD10";
            try
            {
                var wrong = new List<string>();
                Func<string, string, bool> matches = (whole, asked) => (bool)Call(sw, "DimensionNameMatches", whole, asked);
                const string full = "RD1@Drawing View1@Part.Drawing";
                if (!matches(full, full)) wrong.Add("the full name");
                if (!matches(full, "RD1")) wrong.Add("RD1");
                if (!matches(full, "RD1@Drawing View1")) wrong.Add("RD1@Drawing View1");
                if (matches("RD10@Drawing View1@Part.Drawing", "RD1")) wrong.Add("RD1 answers for RD10");
                if (matches(full, "rd1")) wrong.Add("rd1 answers (the names are case-sensitive)");
                if (matches(null, "RD1") || matches(full, "") || matches(full, null)) wrong.Add("an empty name answers");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "3 forms answer, 5 do not" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void FixedText(Type sw)
        {
            const string label = "N25: a title block field that shows only the fixed text around its links (« Kg») is told apart from one that shows a value";
            try
            {
                var wrong = new List<string>();
                Func<string, string, bool> only = (asks, show) => (bool)Call(sw, "ShowsOnlyFixedText", asks, show);
                const string weight = "$PRPSHEET:\"Weight\" $PRPSHEET:\"Mass\"Kg";
                if (!only(weight, " Kg")) wrong.Add("« Kg» counted as a value");
                if (!only(weight, "Kg")) wrong.Add("«Kg» counted as a value");
                if (!only(weight, "")) wrong.Add("an empty field counted as a value");
                if (only(weight, "12.5 Kg")) wrong.Add("«12.5 Kg» counted as fixed text");
                const string titled = "TITLE $PRP:\"Description\"";
                if (!only(titled, "TITLE ")) wrong.Add("«TITLE » counted as a value");
                if (only(titled, "TITLE Bracket")) wrong.Add("«TITLE Bracket» counted as fixed text");
                if (only("", "x")) wrong.Add("a field with no text asked for counted as fixed text");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "4 fields with fixed text only, 3 with a value" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void TwinTitle(Type sw)
        {
            const string label = "N27: a drawing with the title of an open document is told so, with both names and the remedy (save_document)";
            try
            {
                var text = (string)Call(sw, "SameTitleWarning", "Draw1", @"C:\Parts\Draw1.SLDDRW");
                bool ok = text != null && text.Contains("«Draw1»") && text.Contains(@"C:\Parts\Draw1.SLDDRW") && text.Contains("save_document");
                Check(label, ok, ok ? "both names and the remedy" : text ?? "null");
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void NoteWhere(Type sw)
        {
            const string label = "the replay of round 7: a note says where it went, read from the two counters, and a note inside a view is warned of with its name, the counts and delete_annotation";
            try
            {
                var wrong = new List<string>();
                Func<int, int, int, int, string> where = (sheetBefore, sheetAfter, viewsBefore, viewsAfter) =>
                    (string)Call(sw, "NoteAttachedTo", sheetBefore, sheetAfter, viewsBefore, viewsAfter);
                if (where(67, 68, 7, 7) != "sheet") wrong.Add("sheet 67 -> 68: " + where(67, 68, 7, 7));
                if (where(67, 67, 7, 8) != "view") wrong.Add("views 7 -> 8 (the replay): " + where(67, 67, 7, 8));
                if (where(0, 1, 0, 1) != "both") wrong.Add("both up: " + where(0, 1, 0, 1));
                var text = (string)Call(sw, "NoteInViewWarning", "DetailItem399", 67, 67, 7, 8);
                if (text == null || !text.Contains("«DetailItem399»") || !text.Contains("67 -> 67") || !text.Contains("7 -> 8") || !text.Contains("delete_annotation"))
                    wrong.Add("the warning: " + (text ?? "null"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "3 places, the warning with name, counts and remedy" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void ViewEdgesArgument(Type tools)
        {
            const string label = "N23: «view_edges» of inspect_part read as the view alone or {view, component, limit}, and 6 forms refused";
            try
            {
                var wrong = new List<string>();
                var m = ReflectionLog.Global.Method(tools, "ViewEdgesRequest", Any);
                if (m == null) throw new MissingMethodException("McpTools.ViewEdgesRequest");
                Func<object, object[]> read = raw =>
                {
                    var args = new object[] { raw, null, null, 0 };
                    var problem = (string)m.Invoke(null, args);
                    return new object[] { problem, args[1], args[2], args[3] };
                };
                var alone = read(" V1 ");
                if (alone[0] != null || (string)alone[1] != "V1" || alone[2] != null || (int)alone[3] != 200)
                    wrong.Add("« V1 »: " + alone[0] + " view " + alone[1] + " limit " + alone[3]);
                var full = read(new Dictionary<string, object> { { "view", "V2" }, { "component", "26P000011-1" }, { "limit", 50 } });
                if (full[0] != null || (string)full[1] != "V2" || (string)full[2] != "26P000011-1" || (int)full[3] != 50)
                    wrong.Add("the full object: " + full[0] + " " + full[1] + " " + full[2] + " " + full[3]);
                var bad = new[]
                {
                    Tuple.Create((object)5, "the view alone"),
                    Tuple.Create((object)new Dictionary<string, object> { { "colour", "red" } }, "has no «colour»"),
                    Tuple.Create((object)new Dictionary<string, object> { { "view", 3 } }, "identifier of the view"),
                    Tuple.Create((object)new Dictionary<string, object> { { "component", " " } }, "name of a component"),
                    Tuple.Create((object)new Dictionary<string, object> { { "limit", 0 } }, "from 1 to 500"),
                    Tuple.Create((object)new Dictionary<string, object> { { "limit", 2.5 } }, "whole number"),
                };
                foreach (var b in bad)
                {
                    var r = read(b.Item1);
                    var problem = (string)r[0];
                    if (problem == null || !problem.Contains(b.Item2)) wrong.Add("«" + b.Item2 + "»: " + (problem ?? "taken"));
                }
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "2 forms taken, " + bad.Length + " refused" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void UndoOnDrawing(Type tools)
        {
            const string label = "N24: after a plan that worked on a drawing the undo says why it keeps no plan, names each operation once, and its remedy is delete_annotation, not delete_feature";
            try
            {
                var wrong = new List<string>();
                Func<string, List<string>, string> reason = (kind, ok) => (string)Call(tools, "DrawingPlanMemoryReason", kind, ok, "Bracket - Sheet1");
                var said = reason("drawing", new List<string> { "drawing_dimension", "drawing_dimension", "note" });
                if (said == null || !said.Contains("«Bracket - Sheet1» (drawing_dimension, note)") || !said.Contains("undo_entry_recorded"))
                    wrong.Add("a plan on a drawing: " + (said ?? "no reason"));
                if (reason("part", new List<string> { "extrude" }) != null) wrong.Add("a plan on a part given the drawing reason");
                if (reason("drawing", new List<string>()) != null) wrong.Add("a plan on a drawing where nothing worked given the reason");
                if (reason("drawing", null) != null) wrong.Add("no list given the reason");
                var field = tools.GetField("DrawingNoMemoryRemedy", Any);
                var remedy = field == null ? null : field.GetValue(null) as string;
                if (remedy == null || !remedy.Contains("delete_annotation") || remedy.Contains("delete_feature"))
                    wrong.Add("the remedy: " + (remedy ?? "missing"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "1 reason, 3 cases without it, the remedy names delete_annotation" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING, ON THE SOURCES
        // =================================================================

        private const string ReadEdgeLine = "var toSheet = ToSheetOf(entity, tr, out component, out placement);";
        private const string CircleLine = "tr = ToSheetOf(e, tr, out component, out placement);";
        private const string TextLine = "ToSheetOf(o, tr, out component, out placement)";
        private const string MarksLine = "MathTransform edgeToSheet = ToSheetOf(e, toSheet, out _, out _);";
        private const string MarksUse = "IsAVisibleHole(e, c, mu, edgeToSheet)";
        private const string SideLine = "var edgeToSheet = ToSheetOf(e, tr, out _, out _);";
        private const string AssemblyBranch = "if (ViewOfAnAssembly(view, all, circles))";
        private const string AlreadyLine = "var already = KeysAlreadyDimensioned(drawing, view";
        private const string AssemblyProperty = "data[\"property\"] = ReadProperty(model, \"\");";
        private const string TwinLine = "string twin = OtherDocumentWithTitle(drawing, title);";
        private const string TwinWarning = "measurements[\"warning_same_title\"] = SameTitleWarning(title, twin);";
        private const string TitleOnCreate = "TellTitle(drawing, measurements);";
        private const string TitleOnView = "TellTitle(model, measurements);";
        private const string SheetFirst = "try { drawing.EditSheet(); } catch { }";
        private const string NoteInsert = "born = model.InsertNote(text);";
        private const string NoteAttachedLine = "[\"attached_to\"] = attachedTo,";
        private const string NoteWarning = "outside[\"warning_in_view\"] = NoteInViewWarning(";
        private const string FixedLine = "ShowsOnlyFixedText(asks, show)";
        private const string FixedFlag = "entry[\"only_fixed_text\"] = true";
        private const string NoteName = "[\"name\"] = noteName,";
        private const string DeleteCase = "case \"delete_annotation\": return DeleteAnnotation(model, arguments);";
        private const string EdgesRequest = "ViewEdgesRequest(viewEdgesRaw,";
        private const string EdgesListing = "_session.ViewEdges(viewEdgesView, viewEdgesComponent, viewEdgesLimit";
        private const string UndoRemedy = "(_noMemoryOnDrawing && _noMemoryBecause != null ? DrawingNoMemoryRemedy : NoMemoryRemedy)";
        private const string UnsavedCall = "MarkUnsavedAfterProperty(model, ";

        private static readonly string[][] Files =
        {
            new[] { "dimension", "Oce.Solid.Agent.Core", "DrawingDimensionOperations.cs" },
            new[] { "text", "Oce.Solid.Agent.Core", "DimensionTextOperations.cs" },
            new[] { "marks", "Oce.Solid.Agent.Core", "CenterMarkOperations.cs" },
            new[] { "annotation", "Oce.Solid.Agent.Core", "DrawingAnnotationOperations.cs" },
            new[] { "property", "Oce.Solid.Agent.Core", "PropertyOperations.cs" },
            new[] { "state", "Oce.Solid.Agent.Core", "DocumentState.cs" },
            new[] { "drawing", "Oce.Solid.Agent.Core", "DrawingOperations.cs" },
            new[] { "flat", "Oce.Solid.Agent.Core", "DrawingFlatPatternOperations.cs" },
            new[] { "title","Oce.Solid.Agent.Core", "TitleBlockOperations.cs" },
            new[] { "note", "Oce.Solid.Agent.Core", "DrawingNoteOperations.cs" },
            new[] { "operations", "Oce.Solid.Agent.Core", "SwOperations.cs" },
            new[] { "tools", "Tools", "McpTools.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "dimension", ReadEdgeLine, "an edge of an assembly view is projected without its component (N23)" },
            new[] { "dimension", CircleLine, "a circle of an assembly view is projected without its component (N23)" },
            new[] { "text", TextLine, "the overlap of the texts reads the segments of an assembly view without their component (N23)" },
            new[] { "marks", MarksLine, "center_marks takes the circles of an assembly view without their component (N23)" },
            new[] { "marks", MarksUse, "center_marks judges a hole of an assembly view without its component (N23)" },
            new[] { "annotation", SideLine, "an annotation picks the side of an assembly view among edges without their component (N23)" },
            new[] { "state", AssemblyProperty, "inspect_part of an assembly gives no «property» (N28)" },
            new[] { "drawing", TwinLine, "the title of a drawing is not compared with the open documents (N27)" },
            new[] { "drawing", TwinWarning, "the twin title is not warned of (N27)" },
            new[] { "drawing", TitleOnCreate, "create_drawing does not tell its title (N27)" },
            new[] { "flat", TitleOnView, "the flat pattern view, which can rename the drawing, does not tell the title (N27)" },
            new[] { "note", NoteAttachedLine, "«note» does not say whether it went on the sheet or into a view" },
            new[] { "note", NoteWarning, "a note inside a view is not warned of" },
            new[] { "title", FixedLine, "the title block does not tell fixed text from a value (N25)" },
            new[] { "title", FixedFlag, "the title block does not mark a field showing only fixed text (N25)" },
            new[] { "note", NoteName, "«note» does not give the name «delete_annotation» needs (N24)" },
            new[] { "operations", DeleteCase, "«delete_annotation» is not dispatched (N24)" },
            new[] { "tools", EdgesRequest, "inspect_part does not read «view_edges» (N23)" },
            new[] { "tools", EdgesListing, "inspect_part does not list the edges of a view (N23)" },
            new[] { "tools", UndoRemedy, "the undo after a plan on a drawing still proposes delete_feature (N24)" },
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
            string dimension;
            if (sources.TryGetValue("dimension", out dimension) && dimension != null)
            {
                int branch = dimension.IndexOf(AssemblyBranch, StringComparison.Ordinal);
                int already = dimension.IndexOf(AlreadyLine, StringComparison.Ordinal);
                if (branch < 0 || already < 0 || branch > already)
                    wrong.Add("the assembly view does not leave for its own path before the dimensions of the part are read (N23)");
            }
            string property;
            if (sources.TryGetValue("property", out property) && property != null)
            {
                int calls = Regex.Matches(property, Regex.Escape(UnsavedCall)).Count;
                if (calls != 2) wrong.Add("set_property marks the document unsaved in " + calls + " places of 2 (N26)");
            }
            string drawingOps;
            if (sources.TryGetValue("drawing", out drawingOps) && drawingOps != null)
            {
                // standard_views and model_view: the first model view renames a drawing
                // without a file, and it is there that report 8cd843 N27 happened.
                int calls = Regex.Matches(drawingOps, Regex.Escape(TitleOnView)).Count;
                if (calls != 2) wrong.Add("the views that rename the drawing tell its title in " + calls + " places of 2 (N27)");
            }
            string note;
            if (sources.TryGetValue("note", out note) && note != null)
            {
                int sheet = note.IndexOf(SheetFirst, StringComparison.Ordinal);
                int insert = note.IndexOf(NoteInsert, StringComparison.Ordinal);
                if (sheet < 0 || insert < 0 || sheet > insert)
                    wrong.Add("«note» does not leave the active view for the sheet before InsertNote");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: the component before the view in the five projections (edges, circles, text overlap, centre marks, annotation side), the assembly path first, view_edges, delete_annotation and the note name, the undo remedy, unsaved after set_property in 2 places, property of an assembly, twin title on create and on the 3 views that rename, fixed text, the note on the sheet";
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Files)
            {
                var path = Path.Combine(Root(), f[1], f[2]);
                sources[f[0]] = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            var real = WiringProblems(sources);

            // Teeth: each line taken away, the order swapped, one call dropped.
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
                mutate("without «" + anchor + "»", line[0], t => t.Replace(anchor, "/* gone */"));
            }
            mutate("the assembly path after the dimensions of the part", "dimension",
                   t => t.Replace(AssemblyBranch, "if (false)").Replace(AlreadyLine, AlreadyLine + "\n" + AssemblyBranch));
            mutate("one MarkUnsavedAfterProperty dropped", "property",
                   t => t.Replace("MarkUnsavedAfterProperty(model, both);", "/* gone */"));
            mutate("one TellTitle of a view dropped", "drawing", t =>
            {
                int i = t.IndexOf(TitleOnView, StringComparison.Ordinal);
                return i < 0 ? t : t.Remove(i, TitleOnView.Length).Insert(i, "/* gone */");
            });
            mutate("the sheet made active after InsertNote", "note",
                   t => t.Replace(SheetFirst, "").Replace(NoteInsert, NoteInsert + "\n" + SheetFirst));

            bool ok = real.Count == 0 && blind.Count == 0;
            string detail = real.Count > 0 ? string.Join("; ", real)
                          : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                          : Lines.Length + " lines, 2 orders, 2 counts; " + copies + " changed copies all red";
            Check(label, ok, detail);
        }
    }
}
