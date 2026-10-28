using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 12 OF REPORT 8CD843 (27/10/2026), WHERE THE BENCH
    /// CANNOT REACH.
    ///
    /// N43: three details came out on top of other views (two with the same
    /// centre, one on the section A-A), with no operation that moves a view. N44:
    /// the drawing read «to be saved» after a plan and «document close» refused it;
    /// measured on a copy (t35s), it was export_pdf that raised the flag, not
    /// title_block. N45: title_block read the general note of the sheet as the
    /// first field of the title block (67 -> 68); a probe on a copy told them apart
    /// with IAnnotation.OwnerType. N46: place_component said «mates» 3 beside the
    /// state's 2 with one suppressed. And three observations: the sentence on the
    /// degrees of freedom «after the first plan», the refusal that said «any part»
    /// with a drawing in front, the sheet of two that read as one with no views.
    ///
    /// Two halves, as Zc2194: the decisions by reflection, the wiring on the
    /// sources, every rule first red on a changed copy. On library 0.174.2 and
    /// bridge 0.152.2 every check is red.
    /// </summary>
    internal static class Zc2195DrawingRound12Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 12 of report 8cd843: the detail in a free place, the PDF that marks the drawing, the title block without the sheet notes, the mates of place_component (N43-N46)");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 12 fixes",
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

            PdfMarksTheDrawing(sw);
            LabelGoesWithTheOutline(sw);
            WhatTheStateSays(state);

            var bridge = BridgeOrNotHere("the refusal before a document was looked at");
            if (bridge == null) return;
            Type tools;
            try { tools = Load.Class(Load.Bridge(bridge), "McpTools"); }
            catch (Exception ex) { Check("the bridge loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (tools == null) { Check("McpTools is in the bridge", false, "type missing"); return; }
            NotLookedAt(tools);
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

        private static void PdfMarksTheDrawing(Type sw)
        {
            const string label = "N44: export_pdf says when it raised the save flag of a saved drawing, and how to close it";
            try
            {
                var wrong = new List<string>();
                Func<bool, bool, bool> marked = (before, after) => (bool)Call(sw, "MarkedUnsavedByThePdf", before, after);
                if (!marked(false, true)) wrong.Add("saved before, unsaved after: not said");
                if (marked(true, true)) wrong.Add("unsaved before and after: said");
                if (marked(false, false)) wrong.Add("saved before and after: said");
                if (marked(true, false)) wrong.Add("unsaved before, saved after: said");
                var two = (string)Call(sw, "PdfMarkedUnsavedWarning", 2);
                foreach (var part in new[] { "2 sheets", "refuses it as unsaved", "«save_document» after «export_pdf»" })
                    if (two == null || !two.Contains(part)) wrong.Add("the warning lacks «" + part + "»: " + (two ?? "null"));
                var one = (string)Call(sw, "PdfMarkedUnsavedWarning", 1);
                if (one == null || !one.Contains("1 sheet ")) wrong.Add("one sheet: " + (one ?? "null"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "only saved before and unsaved after; the warning with the sheets and the order of the plan" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void LabelGoesWithTheOutline(Type sw)
        {
            const string label = "N43: the place where a detail landed is its rectangle with the label, moved as far as the outline moved";
            try
            {
                var wrong = new List<string>();
                var landed = Call(sw, "LandedWithTheOutline", new[] { 0.1751, 0.191, 0.2957, 0.2495 },
                                  new[] { 0.1751, 0.2096, 0.2957, 0.2495 }, new[] { 0.0251, 0.0596, 0.1457, 0.0995 }) as double[];
                var expected = new[] { 0.0251, 0.041, 0.1457, 0.0995 };
                if (landed == null || landed.Length != 4) wrong.Add("no rectangle");
                else for (int i = 0; i < 4; i++)
                        if (Math.Abs(landed[i] - expected[i]) > 1e-9) wrong.Add("corner " + i + ": " + landed[i] + " instead of " + expected[i]);
                if (Call(sw, "LandedWithTheOutline", new[] { 0.0, 0.0, 1.0, 1.0 }, new[] { 0.0, 0.0, 1.0, 1.0 }, null) != null)
                    wrong.Add("an outline not read back gave a rectangle");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "detail B of the probe with its label, moved (-150, -150) mm, lands at 25.1/41 mm; no outline, no rectangle" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void WhatTheStateSays(Type state)
        {
            const string label = "round 12: a sheet that is not active says it was not read, and the degrees of freedom wait for a changed mate, not for any plan";
            try
            {
                var wrong = new List<string>();
                var sheet = (string)Call(state, "NotActiveSheetNote", "Sheet2");
                foreach (var part in new[] { "only the active sheet is read", "«views_total» are those of the active sheet", "«activate_sheet» with «Sheet2»" })
                    if (sheet == null || !sheet.Contains(part)) wrong.Add("the sheet note lacks «" + part + "»: " + (sheet ?? "null"));
                var text = (string)Call(state, "DegreesNotGivenText", new List<string> { "C8" }, 8);
                foreach (var part in new[] { "until a plan has changed a mate", "after «interference_check»", "after «suppress_mate»" })
                    if (text == null || !text.Contains(part)) wrong.Add("the degrees lack «" + part + "»: " + (text ?? "null"));
                if (text != null && text.Contains("after the first plan")) wrong.Add("«after the first plan» is still said");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "the sheet note names the sheet and the remedy; the degrees name interference_check and suppress_mate" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void NotLookedAt(Type tools)
        {
            const string label = "round 12: the refusal before a document was looked at says «document», not «part», and that the one looked at may have been closed";
            try
            {
                var front = new Dictionary<string, object> { ["document_title"] = "Clip - Sheet1", ["kind"] = "drawing" };
                var text = (string)Call(tools, "NotLookedAtYet", front);
                var wrong = new List<string>();
                foreach (var part in new[] { "not looked at any document yet", "the one I looked at was closed", "or name it in «document»" })
                    if (text == null || !text.Contains(part)) wrong.Add("lacks «" + part + "»: " + (text ?? "null"));
                if (text != null && text.Contains("any part")) wrong.Add("«any part» is still said");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "document, closed, the name as a way out" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING, ON THE SOURCES
        // =================================================================

        private const string PlacedActive = "int mates = ActiveMates(MatesOf(placedNow), matesOff);";
        private const string PlacedSuppressed = "if (matesOff >= 0) measurements[\"mates_suppressed\"] = matesOff;";
        private const string OldPlaced = "int mates = MatesOf(ComponentByName(assemblyDoc, name));";
        private const string FixActive = "int acc = ActiveMates(MatesOf(c), off);";
        private const string OldFix = "int acc = MatesOf(c);";
        private const string OwnerSheet = "if (note != null && owner == (int)swAnnotationOwner_e.swAnnotationOwner_DrawingSheet) onTheSheet++;";
        // «notes», not «outside», since library 0.174.7: the loop moved into FormatNotes,
        // which reads the format of the other sheets too (report 8cd843 N59).
        private const string OwnerElse = "else if (note != null) notes.Add(note);";
        private const string LeftOutSaid = "[\"notes_on_the_sheet_left_out\"] = notesOnTheSheet,";
        private const string FlagBefore = "try { unsavedBeforePdf = model.GetSaveFlag(); } catch { }";
        private const string FlagAfter = "try { unsavedAfterPdf = model.GetSaveFlag(); } catch { }";
        private const string PdfWrite = "pdf, ref errors, ref warnings);";
        private const string MarkedSaid = "if (MarkedUnsavedByThePdf(unsavedBeforePdf, unsavedAfterPdf))";
        private const string FreePlaceCall = "if (!DetailInAFreePlace(drawing, detailView, out placedWhere, out placeProblem))";
        private const string DetailMoved = "try { view.Position = new[] { p[0] + down[0] - o[0], p[1] + down[1] - o[1] }; } catch { }";
        private const string PlacedSaid = "[\"placed\"] = placedWhere,";
        private const string CirclesProof = "» the detail circles are still ";
        private const string SheetNotRead = "entry[\"not_read\"] = NotActiveSheetNote(names[i]);";
        private const string WithLabel = "double[] o = ViewWithItsNotes(view), p = null, outline = null;";
        private const string WithLabelAfter = "var o2 = LandedWithTheOutline(o, outline, moved);";
        private const string RedrawFirst = "try { moved = view.GetOutline() as double[]; } catch { }";
        private const string LabelReadBack = "var o2 = ViewWithItsNotes(view);";
        private const string OwnNotesSkipped = "var note = (excludedName != null && viewName == excludedName) ||";

        private static readonly string[][] Files =
        {
            new[] { "placement", "Oce.Solid.Agent.Core", "PlacementOperations.cs" },
            new[] { "fix", "Oce.Solid.Agent.Core", "FixComponentOperations.cs" },
            new[] { "title", "Oce.Solid.Agent.Core", "TitleBlockOperations.cs" },
            new[] { "pdf", "Oce.Solid.Agent.Core", "DrawingOperations.cs" },
            new[] { "detail", "Oce.Solid.Agent.Core", "DrawingDetailOperations.cs" },
            new[] { "drawing", "Oce.Solid.Agent.Core", "DrawingState.cs" },
            new[] { "sheet", "Oce.Solid.Agent.Core", "SheetPlacementOperations.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "placement", PlacedActive, "place_component counts the suppressed mates among those that hold (N46)" },
            new[] { "placement", PlacedSuppressed, "place_component does not say the suppressed mates (N46)" },
            new[] { "fix", FixActive, "fix_component counts the suppressed mates among those that hold (N46)" },
            new[] { "title", OwnerSheet, "the notes put on the sheet are read as fields of the title block (N45)" },
            new[] { "title", OwnerElse, "the notes of the format are no longer read (N45)" },
            new[] { "title", LeftOutSaid, "the notes left out of the title block are not counted in the answer (N45)" },
            new[] { "pdf", FlagBefore, "the save flag before the PDF is not read (N44)" },
            new[] { "pdf", FlagAfter, "the save flag after the PDF is not read (N44)" },
            new[] { "pdf", MarkedSaid, "a drawing marked unsaved by the PDF is not said (N44)" },
            new[] { "detail", FreePlaceCall, "the detail stays where it was born, on top of other views (N43)" },
            new[] { "detail", DetailMoved, "the detail is never moved to the free place (N43)" },
            new[] { "detail", PlacedSaid, "the answer does not say where the detail went (N43)" },
            new[] { "drawing", SheetNotRead, "a sheet that is not active reads as a sheet with no views (round 12)" },
            new[] { "detail", WithLabel, "the label of the detail is left out of the place it needs, and goes below the frame (N43)" },
            new[] { "detail", WithLabelAfter, "the place where the detail landed is judged without its label (N43)" },
            new[] { "detail", RedrawFirst, "the outline is not read again after the move (N43)" },
            new[] { "sheet", OwnNotesSkipped, "the view being moved is in the way of itself through its own label (N43)" },
        };

        /// <summary>The lines that must not come back, with what they bring back.</summary>
        private static readonly string[][] Gone =
        {
            new[] { "placement", OldPlaced, "place_component counts every mate again (N46)" },
            new[] { "fix", OldFix, "fix_component counts every mate again (N46)" },
            new[] { "detail", LabelReadBack, "the label is read back right after the move, where it was before (N43)" },
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
            string pdf;
            if (sources.TryGetValue("pdf", out pdf) && pdf != null)
            {
                int before = pdf.IndexOf(FlagBefore, StringComparison.Ordinal);
                int write = pdf.IndexOf(PdfWrite, StringComparison.Ordinal);
                int after = pdf.IndexOf(FlagAfter, StringComparison.Ordinal);
                if (before < 0 || write < 0 || after < 0 || before > write || after < write)
                    wrong.Add("the save flag is not read before and after the PDF is written (N44)");
            }
            string detail;
            if (sources.TryGetValue("detail", out detail) && detail != null)
            {
                int proof = detail.IndexOf(CirclesProof, StringComparison.Ordinal);
                int place = detail.IndexOf(FreePlaceCall, StringComparison.Ordinal);
                int said = detail.IndexOf(PlacedSaid, StringComparison.Ordinal);
                if (proof < 0 || place < 0 || said < 0 || place < proof || place > said)
                    wrong.Add("the detail is placed before it is proved a detail, or after the answer is built (N43)");
            }
            if (detail != null)
            {
                int moved = detail.IndexOf(DetailMoved, StringComparison.Ordinal);
                int redraw = detail.IndexOf(RedrawFirst, StringComparison.Ordinal);
                int reread = detail.IndexOf(WithLabelAfter, StringComparison.Ordinal);
                if (moved < 0 || redraw < 0 || reread < 0 || redraw < moved || reread < redraw)
                    wrong.Add("the place where the detail landed is not taken from the outline read after the move (N43)");
            }
            string title;
            if (sources.TryGetValue("title", out title) && title != null)
            {
                int owner = title.IndexOf(OwnerSheet, StringComparison.Ordinal);
                int kept = title.IndexOf(OwnerElse, StringComparison.Ordinal);
                if (owner < 0 || kept < 0 || kept < owner)
                    wrong.Add("a note is kept before its owner is looked at (N45)");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: the mates that hold in place_component and fix_component, the title block without the sheet notes, the save flag around the PDF, the detail in a free place, the sheet not read said";
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
            mutate("the flag after read before the PDF", "pdf",
                   t => t.Replace(FlagAfter, "").Replace(FlagBefore, FlagBefore + "\n" + FlagAfter));
            mutate("the detail placed after the answer", "detail",
                   t => t.Replace(FreePlaceCall, "if (false)").Replace(PlacedSaid, PlacedSaid + "\n" + FreePlaceCall));
            mutate("the outline read after the place is judged", "detail",
                   t => t.Replace(RedrawFirst, "").Replace(WithLabelAfter, WithLabelAfter + "\n" + RedrawFirst));
            mutate("the note kept before its owner is looked at", "title",
                   t => t.Replace(OwnerSheet, "").Replace(OwnerElse, OwnerElse + "\n" + OwnerSheet));

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : Lines.Length + " lines, " + Gone.Length + " old lines kept out, four orders; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
