using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 14 OF REPORT 8CD843 (27/10/2026), WHERE THE BENCH
    /// CANNOT REACH.
    ///
    /// N52: the circle of a detail landed 1.09 mm lower than asked; a probe on a copy
    /// measured that the sketch snapped it onto the geometry around it, and that with
    /// AddToDB on it stayed within 0.02 mm. N53: the section was born on top of the
    /// side view; a section is aligned to its view and moves only along its line.
    /// N54: no operation moved or took away a view. N55: a save onto another existing
    /// file said nothing. N56: three balloon arrows ended within 0.725 mm and
    /// 1.549 mm of each other; the arrow is not moved (the PDF did not follow
    /// SetLeaderAttachmentPointAtIndex), the balloon is put again.
    ///
    /// Two halves, as Zc2196: the decisions by reflection, with the numbers of the
    /// probes of round 14, and the wiring on the sources, every rule first red on a
    /// changed copy. On library 0.174.4 and bridge 0.152.4 every check is red.
    /// </summary>
    internal static class Zc2197DrawingRound14Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 14 of report 8cd843: no snapping for the circle and the line, the section in a free place, move_view and delete_view, the save that says what it replaced, the crowded balloons (N52-N56)");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 14 fixes",
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

            WhatSnappedIsSeen(sw);
            TheSectionSlidesToAFreePlace(sw);
            TheMoveAsked(sw);
            TheViewsThatGoWithIt(sw);
            TheFileReplaced(sw);
            TheCrowdedArrows(sw);
            ThePiecesOfTheView(sw);
            WhoseThePiecesAre(sw);
            TheTransformOfTheComponent(sw);
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

        private static bool Near(double a, double b) { return Math.Abs(a - b) <= 1e-9; }

        // =================================================================
        // THE PURE RULES
        // =================================================================

        private static void WhatSnappedIsSeen(Type sw)
        {
            const string label = "N52: a circle or a line read back away from where it was drawn is refused with both numbers; within the tolerance, or not read, it passes";
            try
            {
                var wrong = new List<string>();
                // The probe of round 14, in metres of the view sketch at scale 0.5: asked
                // centre (0, 0) radius 10 mm; read back without AddToDB (0, -2.175) mm and
                // 10.234 mm, that is 1.088 mm lower and radius 5.117 mm on the sheet, as the PDF.
                var asked = new[] { 0.0, 0.0, 0.01 };
                var snapped = new[] { 0.0, -0.002175, 0.010234 };
                var moved = (string)Call(sw, "LandedElsewhere", "circle of the detail", asked, snapped, 1e-6);
                foreach (var part in new[] { "circle of the detail", "asked [0, 0, 10] mm", "read back [0, -2.175, 10.234] mm", "SOLIDWORKS moved it" })
                    if (moved == null || !moved.Contains(part)) wrong.Add("the snapped circle lacks «" + part + "»: " + (moved ?? "null"));
                // With AddToDB on: the numbers come back as they went.
                if (Call(sw, "LandedElsewhere", "circle of the detail", asked, new[] { 0.0, 0.0, 0.01 }, 1e-6) != null)
                    wrong.Add("the circle that stayed was refused");
                if (Call(sw, "LandedElsewhere", "cutting line", new[] { 0.0, -0.0336, 0.0, 0.0336 }, new[] { 0.0000005, -0.0336, 0.0, 0.0336 }, 1e-6) != null)
                    wrong.Add("half a micrometre was refused");
                var line = (string)Call(sw, "LandedElsewhere", "cutting line", new[] { 0.0, -0.0336, 0.0, 0.0336 }, new[] { 0.002, -0.0336, 0.002, 0.0336 }, 1e-6);
                if (line == null || !line.Contains("cutting line")) wrong.Add("the line moved by 2 mm: " + (line ?? "null"));
                if (Call(sw, "LandedElsewhere", "circle of the detail", asked, null, 1e-6) != null) wrong.Add("a circle not read was refused");
                if (Call(sw, "LandedElsewhere", "circle of the detail", asked, new[] { 0.0, 0.0 }, 1e-6) != null) wrong.Add("a reading too short was refused");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "the snapped circle of the probe refused with asked and read; the one drawn with AddToDB passes" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void TheSectionSlidesToAFreePlace(Type sw)
        {
            const string label = "N53: the section of round 14 slides left along its line to the first place clear of the views by 4 mm, and none is refused";
            try
            {
                var wrong = new List<string>();
                // The outlines read by the probe on the copy of the drawing of round 14 (metres).
                var section = new[] { 0.214997, 0.215712, 0.256701, 0.235642 };
                var views = new List<double[]>
                {
                    new[] { 0.040135, 0.215488, 0.148865, 0.243492 },   // the front view
                    new[] { 0.220648, 0.215488, 0.262352, 0.243492 },   // the side view, under the section
                    new[] { 0.279274, 0.161622, 0.380726, 0.238378 },   // the isometric view
                };
                // To the right the isometric view and the frame leave no room; to the left the
                // section must end 4 mm before the side view: 41 mm.
                var shift = Call(sw, "NearestFreeAlong", section, true, views, 0.42, 0.297) as double[];
                if (shift == null || shift.Length != 2 || !Near(shift[0], -0.041) || !Near(shift[1], 0))
                    wrong.Add("the section of round 14: " + (shift == null ? "null" : shift[0] + ", " + shift[1]) + " instead of -0.041, 0");
                // The gap between the front and the side view filled: nowhere along the line.
                var full = new List<double[]>(views) { new[] { 0.148, 0.2, 0.221, 0.25 } };
                if (Call(sw, "NearestFreeAlong", section, true, full, 0.42, 0.297) != null) wrong.Add("a full line gave a place");
                // A section below its view slides vertically; free 19 mm up and 19 mm down
                // (no bound falls on a step of 1 mm), the positive side first at equal distance.
                var below = new[] { 0.1, 0.1, 0.14, 0.12 };
                var vertical = Call(sw, "NearestFreeAlong", below, false, new List<double[]> { new[] { 0.09, 0.1055, 0.15, 0.1145 } }, 0.42, 0.297) as double[];
                if (vertical == null || !Near(vertical[0], 0) || !Near(vertical[1], 0.019))
                    wrong.Add("the vertical slide: " + (vertical == null ? "null" : vertical[0] + ", " + vertical[1]));
                if (Call(sw, "NearestFreeAlong", null, true, views, 0.42, 0.297) != null) wrong.Add("no outline gave a place");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "-41 mm for the section of round 14; a full line none; the vertical slide upwards" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void TheMoveAsked(Type sw)
        {
            const string label = "N54: move_view takes a shift or a place, not both and not neither, and a move done only in part is said with both numbers";
            try
            {
                var wrong = new List<string>();
                double n = double.NaN;
                var both = (string)Call(sw, "MoveArgumentsProblem", 10.0, n, 100.0, n);
                if (both == null || !both.Contains("not both")) wrong.Add("a shift and a place: " + (both ?? "null"));
                var none = (string)Call(sw, "MoveArgumentsProblem", n, n, n, n);
                if (none == null || !none.Contains("Say where the view goes")) wrong.Add("no argument: " + (none ?? "null"));
                if (Call(sw, "MoveArgumentsProblem", n, 30.0, n, n) != null) wrong.Add("«dy_mm» alone was refused");
                if (Call(sw, "MoveArgumentsProblem", n, n, 120.0, n) != null) wrong.Add("«x_mm» alone was refused");
                // The section of the probe: centre at 235.849 mm, asked -150 mm, then x 120 mm.
                var by = Call(sw, "MoveAsked", -150.0, n, n, n, 0.235849, 0.225678) as double[];
                if (by == null || !Near(by[0], -0.15) || !Near(by[1], 0)) wrong.Add("the shift of the probe: " + (by == null ? "null" : by[0] + ", " + by[1]));
                var to = Call(sw, "MoveAsked", n, n, 120.0, n, 0.235849, 0.225678) as double[];
                if (to == null || !Near(to[0], 0.12 - 0.235849) || !Near(to[1], 0)) wrong.Add("the place: " + (to == null ? "null" : to[0] + ", " + to[1]));
                // The probe asked +30 mm across the line: moved 0.
                var across = (string)Call(sw, "MoveNotDone", 0.0, 0.03, 0.0, 0.0, 0.0001);
                if (across == null || !across.Contains("moved by (0, 0) mm instead of the (0, 30) mm asked")) wrong.Add("the move across the line: " + (across ?? "null"));
                if (Call(sw, "MoveNotDone", -0.15, 0.0, -0.15, 0.0, 0.0001) != null) wrong.Add("the exact move of the probe was refused");
                var unread = (string)Call(sw, "MoveNotDone", 0.03, 0.0, n, 0.0, 0.0001);
                if (unread == null || !unread.Contains("cannot read")) wrong.Add("a move not read: " + (unread ?? "null"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "both or neither refused; the shift and the place of the probe; the move across the line said" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void TheViewsThatGoWithIt(Type sw)
        {
            const string label = "N54: a view with sections or details is not taken away unless «with_derived», and the refusal names them";
            try
            {
                var wrong = new List<string>();
                var derived = new List<string> { "Section View A-A", "Detail View B (2 : 1)" };
                var held = (string)Call(sw, "DerivedViewsProblem", "Drawing View1", derived, false);
                foreach (var part in new[] { "«Drawing View1»", "Section View A-A", "Detail View B (2 : 1)", "with_derived", "I touched nothing" })
                    if (held == null || !held.Contains(part)) wrong.Add("the refusal lacks «" + part + "»: " + (held ?? "null"));
                if (Call(sw, "DerivedViewsProblem", "Drawing View1", derived, true) != null) wrong.Add("«with_derived» true was refused");
                if (Call(sw, "DerivedViewsProblem", "Drawing View3", new List<string>(), false) != null) wrong.Add("a view with nothing made from it was refused");
                if (Call(sw, "DerivedViewsProblem", "Drawing View3", null, false) != null) wrong.Add("no list was refused");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "the front view of the probe refused with its section and detail named; «with_derived» passes" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void TheFileReplaced(Type sw)
        {
            const string label = "N55: «overwrite» false refuses a save onto another existing file and says its bytes and date; true, or no file, goes on";
            try
            {
                var wrong = new List<string>();
                var written = new DateTime(2026, 10, 27, 21, 9, 0);
                // The file of the report: 363440 bytes, written at 21:09.
                var refused = (string)Call(sw, "ReplaceRefused", @"C:\work\copy.SLDDRW", 363440L, written, false);
                foreach (var part in new[] { "already exists", "363440 bytes", "2026-10-27 21:09:00", "I saved nothing", "«overwrite»: true" })
                    if (refused == null || !refused.Contains(part)) wrong.Add("the refusal lacks «" + part + "»: " + (refused ?? "null"));
                if (Call(sw, "ReplaceRefused", @"C:\work\copy.SLDDRW", 363440L, written, true) != null) wrong.Add("«overwrite» true was refused");
                if (Call(sw, "ReplaceRefused", @"C:\work\copy.SLDDRW", -1L, DateTime.MinValue, false) != null) wrong.Add("no file there was refused");
                if (Call(sw, "ReplaceRefused", @"C:\work\copy.SLDDRW", 0L, written, false) == null) wrong.Add("an empty file there was not refused");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "the file of the report refused with bytes and date; overwrite or no file goes on" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void TheCrowdedArrows(Type sw)
        {
            const string label = "N56: the arrows of the balloons 6, 4 and 5 of round 14 are crowded within 5 mm, the others not, and a tip not read is skipped";
            try
            {
                var wrong = new List<string>();
                // The arrow tips read on sheet 2 of the copy (metres): 2, 3, 6, 4, 5, 7, and one not read.
                var tips = new List<double[]>
                {
                    new[] { 0.186263, 0.183495 }, new[] { 0.239618, 0.154089 }, new[] { 0.253363, 0.143498 },
                    new[] { 0.253581, 0.142806 }, new[] { 0.253363, 0.141273 }, new[] { 0.212668, 0.141049 }, null,
                };
                var crowded = Call(sw, "CrowdedIndices", tips, 0.005) as List<int>;
                if (crowded == null || string.Join(",", crowded) != "2,3,4")
                    wrong.Add("crowded: " + (crowded == null ? "null" : string.Join(",", crowded)) + " instead of 2,3,4");
                var tight = Call(sw, "CrowdedIndices", tips, 0.001) as List<int>;
                if (tight == null || string.Join(",", tight) != "2,3") wrong.Add("within 1 mm: " + (tight == null ? "null" : string.Join(",", tight)) + " instead of 2,3");
                var field = ReflectionLog.Global.Field(sw, "CrowdedArrows", Any);
                if (field == null || !Near((double)field.GetValue(null), 0.005)) wrong.Add("the limit is not 5 mm");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "6, 4, 5 crowded, 2, 3, 7 not; within 1 mm only 6 and 4; the limit 5 mm" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void ThePiecesOfTheView(Type sw)
        {
            const string label = "N56: the records of GetPolylines7 become pieces on the sheet through GetXform, and an array that does not hold the records is none";
            try
            {
                var wrong = new List<string>();
                // A line of two points, then an arc (two numbers of geometry) of three points.
                var data = new List<double> { 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0.01, 0, 0 };
                data.AddRange(new double[] { 1, 2, 7, 7, 0, 0, 0, 0, 0, 0, 3, 0, 0.02, 0, 0.01, 0.02, 0, 0.02, 0.02, 0 });
                var xform = new[] { 0.1, 0.2, 0.5 };
                var pieces = Call(sw, "PolylinePieces", data.ToArray(), 2, xform) as List<double[]>;
                if (pieces == null || pieces.Count != 3) wrong.Add("pieces: " + (pieces == null ? "null" : pieces.Count.ToString()) + " instead of 3");
                else
                {
                    if (!Near(pieces[0][0], 0.1) || !Near(pieces[0][1], 0.2) || !Near(pieces[0][2], 0.105) || !Near(pieces[0][3], 0.2))
                        wrong.Add("the line: " + string.Join(", ", pieces[0]));
                    if (!Near(pieces[2][0], 0.105) || !Near(pieces[2][1], 0.21) || !Near(pieces[2][2], 0.11) || !Near(pieces[2][3], 0.21))
                        wrong.Add("the last piece of the arc: " + string.Join(", ", pieces[2]));
                }
                if (Call(sw, "PolylinePieces", data.ToArray(), 1, xform) != null) wrong.Add("one record of two read as all");
                if (Call(sw, "PolylinePieces", data.ToArray(), 3, xform) != null) wrong.Add("three records out of two read");
                if (Call(sw, "PolylinePieces", data.ToArray(), 2, null) != null) wrong.Add("no transform gave pieces");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "a line and an arc give 1 + 2 pieces at the centre plus scale times the point; a wrong count gives none" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void WhoseThePiecesAre(Type sw)
        {
            const string label = "N56: a piece belongs to the part whose edges pass by both its ends, and the new arrow goes on its own line, away from a shared one and from the other arrows";
            try
            {
                var wrong = new List<string>();
                // B first: its short edge lies within 0.2 mm of A, and the first component that
                // passes by both ends of a piece owns it.
                var segments = new Dictionary<string, List<double[]>>
                {
                    { "B", new List<double[]> { new[] { 0.0, 0.0002, 0.002, 0.0002 }, new[] { 0.0, 0.0, 0.0, 0.01 } } },
                    { "A", new List<double[]> { new[] { 0.0, 0.0, 0.01, 0.0 } } },
                };
                var pieces = new List<double[]>
                {
                    new[] { 0.0, 0.0, 0.01, 0.0 },          // on A
                    new[] { 0.0, 0.0002, 0.002, 0.0002 },   // on B, within 0.2 mm of A
                    new[] { 0.01, 0.0, 0.0, 0.01 },         // from A to B: nobody's
                };
                var owners = Call(sw, "PieceOwners", pieces, segments, 0.0005) as string[];
                if (owners == null || owners.Length != 3 || owners[0] != "A" || owners[1] != "B" || owners[2] != null)
                    wrong.Add("owners: " + (owners == null ? "null" : string.Join(",", owners)) + " instead of A,B,(none)");
                else
                {
                    // The other arrow at the start of A: the point of A farthest from it is its far end.
                    // The piece of nobody is left out here: it ends on the far end of A.
                    var others = new List<double[]> { new[] { 0.0, 0.0 } };
                    var best = Call(sw, "FarthestOwnPoint", new List<double[]> { pieces[0], pieces[1] }, new[] { "A", "B" }, "A", others, 0.001, 0.0005) as double[];
                    if (best == null || !Near(best[0], 0.01) || !Near(best[1], 0) || !Near(best[2], 0.01))
                        wrong.Add("the farthest point of A: " + (best == null ? "null" : string.Join(", ", best)));
                    var near = Call(sw, "FarthestOwnPoint", new List<double[]> { pieces[0], pieces[1] }, new[] { "A", "B" }, "A", new List<double[]> { new[] { 0.01, 0.0 } }, 0.001, 0.0005) as double[];
                    if (near == null || near[0] < 0.0025 - 1e-9) wrong.Add("a point shared with B was picked: " + (near == null ? "null" : string.Join(", ", near)));
                    if (Call(sw, "FarthestOwnPoint", pieces, owners, "C", others, 0.001, 0.0005) != null) wrong.Add("a part with no piece gave a point");
                }
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "A, B and nobody's; the far end of A, never the line shared with B" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void TheTransformOfTheComponent(Type sw)
        {
            const string label = "N56: the 13 numbers of ArrayData are read by rows or by columns, decided by the point IMathPoint gave";
            try
            {
                var wrong = new List<string>();
                // A quarter turn about z, moved by (0.1, 0.2).
                var a = new[] { 0.0, 1, 0, -1, 0, 0, 0, 0, 1, 0.1, 0.2, 0, 1 };
                var p = new[] { 0.01, 0.0, 0.0 };
                var rows = Call(sw, "Transformed", a, p, true) as double[];
                if (rows == null || !Near(rows[0], 0.1) || !Near(rows[1], 0.21)) wrong.Add("by rows: " + (rows == null ? "null" : rows[0] + ", " + rows[1]));
                var columns = Call(sw, "Transformed", a, p, false) as double[];
                if (columns == null || !Near(columns[0], 0.1) || !Near(columns[1], 0.19)) wrong.Add("by columns: " + (columns == null ? "null" : columns[0] + ", " + columns[1]));
                if ((int)Call(sw, "TransformOrder", a, p, new[] { 0.1, 0.21 }, 1e-9) != 1) wrong.Add("the point by rows not read as rows");
                if ((int)Call(sw, "TransformOrder", a, p, new[] { 0.1, 0.19 }, 1e-9) != 2) wrong.Add("the point by columns not read as columns");
                if ((int)Call(sw, "TransformOrder", a, p, new[] { 0.5, 0.5 }, 1e-9) != 3) wrong.Add("a point of neither read as one");
                if ((int)Call(sw, "TransformOrder", a, p, null, 1e-9) != 3) wrong.Add("no point read as one");
                if (Call(sw, "Transformed", new double[12], p, true) != null) wrong.Add("12 numbers gave a point");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "the quarter turn by rows and by columns; neither and nothing are 3" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING
        // =================================================================

        private const string AddToDb = "model.SketchManager.AddToDB = true;";
        private const string AddBack = "finally { try { model.SketchManager.AddToDB = addBefore; } catch { } }";
        private const string CircleRead = "double[] circleRead = CircleInSketch(circle);";
        private const string CentreAsPoint = "var c = arc?.GetCenterPoint2() as ISketchPoint;";
        private const string CentreAsArray = "GetCenterPoint2() as double[]";
        private const string CircleJudged = "string circleMoved = LandedElsewhere(\"circle of the detail\",";
        private const string LineJudged = "string lineMoved = LandedElsewhere(\"cutting line\", new[] { from[0], from[1], to[0], to[1] }, LineInSketch(line), 1e-6);";
        private const string FreePlace = "if (!SectionInAFreePlace(drawing, sectionView, facing, out sectionWhere, out sectionPlaceProblem))";
        private const string Spread = "var moves = SpreadCrowdedBalloons(model, view, out spreadNote);";
        private const string Crowded = "var crowded = CrowdedIndices(tips, CrowdedArrows);";
        private const string KeepNumbers = "options.ItemOrder = (int)swBalloonItemNumbersOrder_e.swBalloonItemNumbers_DoNotChangeItemNumbers;";
        private const string Born = "born = model.Extension.InsertBOMBalloon2(options) as INote;";
        private const string NumberChecked = "if (annotation == null || number != b.Number)";
        private const string OldGone = "if (!DeleteAnnotationSelected(model, b.Annotation))";
        private const string Kept = "string kept = ReplaceRefused(location, replacedBytes, replacedWritten, Bool(a, \"overwrite\", true));";
        private const string SaveCall = "ok = model.Extension.SaveAs(";
        private const string MoveArgs = "string arguments = MoveArgumentsProblem(dx, dy, x, y);";
        private const string Partly = "string partly = MoveNotDone(want[0], want[1], gotX, gotY, 0.0001);";
        private const string EachMoved = "var sheet = ReadSheet(drawing, mv, null, moved);";
        private const string Held = "string held = DerivedViewsProblem(viewName, derived, withDerived);";
        private const string RemoveCall = "bool removed = RemoveNewView(model, viewName);";
        private const string MoveCase = "case \"move_view\": return MoveView(model, arguments);";
        private const string DeleteCase = "case \"delete_view\": return DeleteView(model, arguments);";
        private const string OutlineSaid = "entry[\"outline_mm\"] = new object[]";
        private const string OldCaveat = "whether the section view overlaps the others or the title block";
        private const string Answered = "if (response != null && !isNotification) Protocol.Send(output, response);";
        // Bridge 0.160.1 put ForgetLastPlan before the collection, in a block: the call alone is
        // the anchor, and the order check looks for it after the answer.
        private const string Released = "ReleaseSolidWorksObjects();";
        private const string Finalizers = "GC.WaitForPendingFinalizers();";

        private static readonly string[][] Files =
        {
            new[] { "detail", "Oce.Solid.Agent.Core", "DrawingDetailOperations.cs" },
            new[] { "section", "Oce.Solid.Agent.Core", "DrawingSectionOperations.cs" },
            new[] { "bom", "Oce.Solid.Agent.Core", "BomOperations.cs" },
            new[] { "spread", "Oce.Solid.Agent.Core", "BalloonSpreadOperations.cs" },
            new[] { "save", "Oce.Solid.Agent.Core", "DrawingOperations.cs" },
            new[] { "view", "Oce.Solid.Agent.Core", "DrawingViewOperations.cs" },
            new[] { "ops", "Oce.Solid.Agent.Core", "SwOperations.cs" },
            new[] { "state", "Oce.Solid.Agent.Core", "DrawingState.cs" },
            new[] { "program", "", "Program.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "detail", AddToDb, "the circle of a detail snaps onto the geometry around it (N52)" },
            new[] { "detail", AddBack, "AddToDB stays on after the detail, for whoever draws next (N52)" },
            new[] { "detail", CircleRead, "the circle is not read back from the sketch (N52)" },
            new[] { "detail", CentreAsPoint, "the centre of the circle is not read as the ISketchPoint it is (N52)" },
            new[] { "detail", CircleJudged, "a circle that moved is delivered as drawn (N52)" },
            new[] { "section", AddToDb, "the cutting line snaps onto the geometry around it (N52)" },
            new[] { "section", AddBack, "AddToDB stays on after the section (N52)" },
            new[] { "section", LineJudged, "a cutting line that moved is delivered as drawn (N52)" },
            new[] { "section", FreePlace, "the section stays where it is born, on top of another view (N53)" },
            new[] { "bom", Spread, "the crowded balloons are not looked at (N56)" },
            new[] { "spread", Crowded, "the balloons are not judged by their arrows (N56)" },
            new[] { "spread", KeepNumbers, "the balloon put again renumbers the table (N56)" },
            new[] { "spread", Born, "no balloon is put again (N56)" },
            new[] { "spread", NumberChecked, "a balloon put again with another number is kept (N56)" },
            new[] { "spread", OldGone, "the old balloon stays beside the new one (N56)" },
            new[] { "save", Kept, "«overwrite» false does not refuse (N55)" },
            new[] { "view", MoveArgs, "move_view moves on arguments that say no move, or two (N54)" },
            new[] { "view", Partly, "a move done in part is called done (N54)" },
            new[] { "view", EachMoved, "the views moved together are not checked against the sheet (N54)" },
            new[] { "view", Held, "a view is taken away with its sections and details, unasked (N54)" },
            new[] { "ops", MoveCase, "move_view is in the catalog and not run (N54)" },
            new[] { "ops", DeleteCase, "delete_view is in the catalog and not run (N54)" },
            new[] { "state", OutlineSaid, "inspect_part does not say where a view is seen (observation 2)" },
            new[] { "program", Released, "the SOLIDWORKS objects of a call stay alive, and SOLIDWORKS falls after some drawings (scenarios t45, t46)" },
            new[] { "program", Finalizers, "the wrappers are collected but not finalized, and SOLIDWORKS keeps the objects (t48)" },
        };

        /// <summary>The lines that must not come back, with what they bring back.</summary>
        private static readonly string[][] Gone =
        {
            new[] { "section", OldCaveat, "the section says it does not look at the other views, which it now does (N53)" },
            new[] { "detail", CentreAsArray, "the centre read as an array is always null, and a snapped circle passes unseen (measured, N52)" },
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
                int on = detail.IndexOf(AddToDb, StringComparison.Ordinal);
                int read = detail.IndexOf(CircleRead, StringComparison.Ordinal);
                int judged = detail.IndexOf(CircleJudged, StringComparison.Ordinal);
                if (on < 0 || read < 0 || judged < 0 || on > read || read > judged)
                    wrong.Add("the circle is drawn before AddToDB is on, or judged before it is read (N52)");
            }
            string section;
            if (sources.TryGetValue("section", out section) && section != null)
            {
                int on = section.IndexOf(AddToDb, StringComparison.Ordinal);
                int judged = section.IndexOf(LineJudged, StringComparison.Ordinal);
                if (on < 0 || judged < 0 || on > judged)
                    wrong.Add("the cutting line is drawn before AddToDB is on (N52)");
            }
            string spread;
            if (sources.TryGetValue("spread", out spread) && spread != null)
            {
                int born = spread.IndexOf(Born, StringComparison.Ordinal);
                int number = spread.IndexOf(NumberChecked, StringComparison.Ordinal);
                int old = spread.IndexOf(OldGone, StringComparison.Ordinal);
                if (born < 0 || number < 0 || old < 0 || born > number || number > old)
                    wrong.Add("the old balloon is taken away before the new one is born and its number checked (N56)");
            }
            string save;
            if (sources.TryGetValue("save", out save) && save != null)
            {
                int kept = save.IndexOf(Kept, StringComparison.Ordinal);
                int call = kept < 0 ? -1 : save.IndexOf(SaveCall, kept, StringComparison.Ordinal);
                if (kept < 0 || call < 0)
                    wrong.Add("the refusal of «overwrite» false comes after the save (N55)");
            }
            string program;
            if (sources.TryGetValue("program", out program) && program != null)
            {
                int answered = program.IndexOf(Answered, StringComparison.Ordinal);
                int released = answered < 0 ? -1 : program.IndexOf(Released, answered, StringComparison.Ordinal);
                if (answered < 0 || released < 0)
                    wrong.Add("the objects are released before the answer is sent, and the client waits for it (t48)");
            }
            string view;
            if (sources.TryGetValue("view", out view) && view != null)
            {
                int held = view.IndexOf(Held, StringComparison.Ordinal);
                int removed = view.IndexOf(RemoveCall, StringComparison.Ordinal);
                if (held < 0 || removed < 0 || held > removed)
                    wrong.Add("the view is taken away before its sections and details are looked at (N54)");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: AddToDB around the circle and the line and their reading back, the section in a free place, the crowded balloons put again, the refusal before the save, move_view and delete_view run and checked, the outline in the state";
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
            mutate("the circle drawn before AddToDB is on", "detail",
                   t => t.Replace(AddToDb, "").Replace(CircleJudged, CircleJudged + "\n" + AddToDb));
            mutate("the circle judged before it is read", "detail",
                   t => t.Replace(CircleRead, "").Replace(CircleJudged, CircleJudged + "\n" + CircleRead));
            mutate("the line drawn before AddToDB is on", "section",
                   t => t.Replace(AddToDb, "").Replace(LineJudged, LineJudged + "\n" + AddToDb));
            mutate("the old balloon taken away before the new one is born", "spread",
                   t => t.Replace(OldGone, "if (false)").Replace(Born, OldGone + " { }\n" + Born));
            mutate("the refusal after the save", "save",
                   t => t.Replace(Kept, "") + "\n// " + Kept);
            mutate("the view taken away before its sections are looked at", "view",
                   t => t.Replace(Held, "").Replace(RemoveCall, RemoveCall + "\n" + Held));
            mutate("the objects released before the answer is sent", "program",
                   t => t.Replace(Released, "").Replace(Answered, Released + "\n" + Answered));

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : Lines.Length + " lines, " + Gone.Length + " old lines kept out, seven orders; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
