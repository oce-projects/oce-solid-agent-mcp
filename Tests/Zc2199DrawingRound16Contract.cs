using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 16 OF REPORT 8CD843 (28/10/2026), WHERE THE BENCH
    /// CANNOT REACH.
    ///
    /// N58: a detail at 2:1 of a view at 5:1 was accepted, and it shrank the piece.
    /// N59: title_block read only the active sheet; the third sheet showed another part
    /// whose title block came out with two empty boxes, and the verdict said «no empty
    /// box». Observation: the sheets of the state carried «active» only when true.
    ///
    /// Two halves, as Zc2198: the decisions by reflection, and the wiring on the
    /// sources, every rule first red on a changed copy. On library 0.174.6 every check
    /// is red.
    /// </summary>
    internal static class Zc2199DrawingRound16Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 16 of report 8cd843: no detail smaller than its view (N58), the title block of the other sheets (N59), «active» false on the other sheets");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 16 fixes",
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

            DetailNotLarger(sw);
            OtherSheets(sw);
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

        private static void DetailNotLarger(Type sw)
        {
            const string label = "N58: a detail at a scale not larger than its view is refused with both scales and a larger one; a larger scale, or a view whose scale cannot be read, goes";
            try
            {
                var wrong = new List<string>();
                // Round 16: detail E at 2 from «Drawing View10» at 5:1.
                var refused = (string)Call(sw, "DetailNotLarger", 2.0, new[] { 0.1, 0.1, 5.0 }, "Drawing View10");
                foreach (var part in new[] { "«scale» 2 (2:1)", "the scale of the view «Drawing View10», 5:1", "smaller", "larger than 5", "for example 10", "I touched nothing" })
                    if (refused == null || !refused.Contains(part)) wrong.Add("the refusal lacks «" + part + "»: " + (refused ?? "null"));
                var same = (string)Call(sw, "DetailNotLarger", 5.0, new[] { 0.1, 0.1, 5.0 }, "V");
                if (same == null || !same.Contains("at the same size")) wrong.Add("a detail at the scale of its view: " + (same ?? "null"));
                var half = (string)Call(sw, "DetailNotLarger", 0.5, new[] { 0.1, 0.1, 1.0 }, "V");
                if (half == null || !half.Contains("(1:2)") || !half.Contains(", 1:1:")) wrong.Add("0.5 from a view at 1:1: " + (half ?? "null"));
                if (Call(sw, "DetailNotLarger", 10.0, new[] { 0.1, 0.1, 5.0 }, "V") != null) wrong.Add("10 from a view at 5:1 was refused");
                if (Call(sw, "DetailNotLarger", 2.0, new[] { 0.1, 0.1, 1.0 }, "V") != null) wrong.Add("2 from a view at 1:1 was refused (the bench case detail_enlarges)");
                if (Call(sw, "DetailNotLarger", 2.0, null, "V") != null) wrong.Add("a view with no transform was refused here");
                if (Call(sw, "DetailNotLarger", 2.0, new[] { 0.1, 0.1, 0.0 }, "V") != null) wrong.Add("a view of scale 0 was refused here");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "2 from 5:1 and 5 from 5:1 refused, 0.5 from 1:1 refused with 1:2; 10 from 5:1, 2 from 1:1 and an unread view go" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void OtherSheets(Type sw)
        {
            const string label = "N59: the names asked from the part are taken bare; another sheet whose model lacks names not empty here, or gives them no value, is named with that model, and the ones empty here too are only counted; another format is said; nothing is said when nothing new lacks";
            try
            {
                var wrong = new List<string>();
                const string mark = "  (from the part, not from the drawing)";
                var bare = (List<string>)Call(sw, "NamesFromThePart",
                    new List<string> { "Description" + mark, "REVISION", "Material" + mark, "Description" + mark });
                if (bare == null || string.Join("|", bare) != "Description|Material")
                    wrong.Add("names from the part: " + (bare == null ? "null" : string.Join("|", bare)));

                var lacks = (string)Call(sw, "OtherSheetWarning", "Sheet3", @"C:\copies\coupler.SLDPRT",
                    new List<string> { "Material", "State" }, new List<string> { "Weight" }, false, 20);
                foreach (var part in new[] { "Sheet «Sheet3» shows «C:\\copies\\coupler.SLDPRT»", "Material, State are not on it", "Weight is there with no value", "none of them is empty on this sheet", "(20 more it lacks are empty on this sheet too", "set_property" })
                    if (lacks == null || !lacks.Contains(part)) wrong.Add("the warning lacks «" + part + "»: " + (lacks ?? "null"));
                if (lacks != null && lacks.Contains("one of the models")) wrong.Add("one model said as one of several");
                var several = (string)Call(sw, "OtherSheetWarning", "Sheet2", "a.SLDPRT", new List<string> { "Material" }, new List<string>(), true, 1);
                if (several == null || !several.Contains("(one of the models of its views)") || !several.Contains("Material is not on it") || !several.Contains("(one more it lacks is empty on this sheet too"))
                    wrong.Add("several models: " + (several ?? "null"));
                if (Call(sw, "OtherSheetWarning", "Sheet2", "a.SLDPRT", new List<string>(), new List<string>(), false, 20) != null) wrong.Add("a model that lacks only what is empty here too was warned about");
                if (Call(sw, "OtherSheetWarning", "Sheet2", "a.SLDPRT", null, null, false, 0) != null) wrong.Add("no lists was warned about");
                var plain = (string)Call(sw, "OtherSheetWarning", "Sheet2", "a.SLDPRT", new List<string> { "Material" }, null, false, 0);
                if (plain == null || plain.Contains("more it lacks")) wrong.Add("nothing empty here too, said all the same: " + (plain ?? "null"));
                // Round 16, scenario t60: the coupler lacked 22 names, 20 of them empty on the first sheet too.
                var fresh = (List<string>)Call(sw, "NotEmptyHere", new List<string> { "TREATMENT", "Material", "STATE", "Weight" }, new List<string> { "Weight", "treatment" });
                if (fresh == null || string.Join("|", fresh) != "Material|STATE") wrong.Add("not empty here: " + (fresh == null ? "null" : string.Join("|", fresh)));
                var none = (List<string>)Call(sw, "NotEmptyHere", null, new List<string> { "Weight" });
                if (none == null || none.Count != 0) wrong.Add("no names gave " + (none == null ? "null" : none.Count.ToString()));

                var format = (string)Call(sw, "OtherFormatWarning", "Sheet3", "A4 - landscape", "A3 - landscape");
                if (format == null || !format.Contains("«A4 - landscape», not «A3 - landscape»") || !format.Contains("call «title_block»"))
                    wrong.Add("another format: " + (format ?? "null"));
                if (Call(sw, "OtherFormatWarning", "Sheet3", "a3 - LANDSCAPE", "A3 - landscape") != null) wrong.Add("the same format in other capitals was said different");
                if (Call(sw, "OtherFormatWarning", "Sheet3", "", "A3") != null) wrong.Add("a format not read was said different");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "two names from the part of four, the coupler sheet named with two missing and one blank and 20 counted, several models said, two of four names not empty here, another format said; nothing when nothing new lacks" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING
        // =================================================================

        private const string DetailCall = "string notLarger = DetailNotLarger(scale1, xform, targetName);";
        private const string DetailRefusal = "if (notLarger != null) return Failed(notLarger);";
        private const string Activation = "try { inView = drawing.ActivateView(targetName); } catch { }";
        private const string OtherSheetsSaid = "measurements[\"other_sheets\"] = OtherSheets(";
        private const string VerdictBut = "\" BUT ON THE OTHER SHEETS: \"";
        private const string LastVerdict = "\"names above are the right ones, copy them literally.\";";
        private const string SheetViews = "try { views = other.GetViews() as object[]; } catch { }";
        private const string MarkShared = "itemName = itemName + FromThePart;";
        private const string ActiveAlways = "entry[\"active\"] = isActive;";
        private const string OldActive = "if (entry.ContainsKey(\"active\"))";
        private const string ItsTitleBlock = "var sheetViews = SheetViews(drawing);";
        private const string ItsNames = "model[\"values\"] = PropertyValues(m, configuration, askedHere, missing, blank);";
        private const string OldNames = "PropertyValues(m, configuration, wanted, missing, blank)";
        private const string OneReading = "onTheSheet = FormatNotes(sheetView, outside);";
        private const string ByList = "var all = drawing.GetViews() as object[];";
        private const string EmptyPassed = "requestedItems, empty, otherWarnings).ToArray();";
        private const string OnlyFresh = "var freshMissing = NotEmptyHere(missingOf[i], emptyBare);";

        private static readonly string[][] Files =
        {
            new[] { "detail", "Oce.Solid.Agent.Core", "DrawingDetailOperations.cs" },
            new[] { "title", "Oce.Solid.Agent.Core", "TitleBlockOperations.cs" },
            new[] { "state", "Oce.Solid.Agent.Core", "DrawingState.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "detail", DetailCall, "the scale of the detail is not compared with the one of its view (N58)" },
            new[] { "detail", DetailRefusal, "a detail smaller than its view is drawn all the same (N58)" },
            new[] { "title", OtherSheetsSaid, "the other sheets are not read (N59)" },
            new[] { "title", VerdictBut, "the verdict says «no empty box» of a drawing with one on another sheet (N59)" },
            new[] { "title", SheetViews, "the views of the other sheet are not read without activating it (N59)" },
            new[] { "title", ItsTitleBlock, "the other sheets are checked with the names of the active sheet, not those of their own title block (N59)" },
            new[] { "title", ItsNames, "the model of another sheet is checked against the names of the active sheet (N59)" },
            new[] { "title", OneReading, "the notes of a format are read in two ways, one for the active sheet and one for the others (N59)" },
            new[] { "title", ByList, "the format of another sheet is not reached without activating it (N59)" },
            new[] { "title", EmptyPassed, "the other sheets do not know what is empty on the active sheet, and repeat it (N59, scenario t60)" },
            new[] { "title", OnlyFresh, "the warning lists the names empty on the active sheet too, and buries the new ones (N59, scenario t60)" },
            new[] { "title", MarkShared, "the mark of the names from the part is written in two places, and NamesFromThePart can stop finding it (N59)" },
            new[] { "state", ActiveAlways, "the other sheets do not say «active» false (observation)" },
        };

        /// <summary>The lines that must not come back, with what they bring back.</summary>
        private static readonly string[][] Gone =
        {
            new[] { "state", OldActive, "the sheet properties read by a key that is now always there (observation)" },
            new[] { "title", OldNames, "the model of another sheet checked against the names of the active sheet (N59)" },
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
                int call = detail.IndexOf(DetailCall, StringComparison.Ordinal);
                int refusal = detail.IndexOf(DetailRefusal, StringComparison.Ordinal);
                int activation = detail.IndexOf(Activation, StringComparison.Ordinal);
                if (call < 0 || refusal < call || activation < 0 || activation < refusal)
                    wrong.Add("the scale of the detail is not refused before the view is activated for the circle (N58)");
            }
            string title;
            if (sources.TryGetValue("title", out title) && title != null)
            {
                int last = title.IndexOf(LastVerdict, StringComparison.Ordinal);
                int but = title.IndexOf(VerdictBut, StringComparison.Ordinal);
                if (last < 0 || but < last)
                    wrong.Add("the other sheets are put in the verdict before its last version is written (N59)");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: the scale of the detail refused before the circle, the other sheets read through their views and their own title block and said in the verdict, one reading of the notes of a format, one mark for the names from the part, «active» on every sheet";
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Files)
            {
                var path = Path.Combine(Root(), f[1], f[2]);
                sources[f[0]] = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            var real = WiringProblems(sources);

            // Teeth: each line taken away, the old line put back, each order swapped.
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
            mutate("the scale refused after the activation", "detail",
                   t => t.Replace(DetailCall + "\n", "").Replace(DetailCall + "\r\n", "").Replace(DetailRefusal, "")
                         .Replace(Activation, Activation + "\n" + DetailCall + "\n" + DetailRefusal));
            mutate("the other sheets put in the verdict before its last version", "title",
                   t => t.Replace(VerdictBut, "\" ... \"").Replace(OtherSheetsSaid, "var x = " + VerdictBut + ";\n" + OtherSheetsSaid));

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : Lines.Length + " lines, " + Gone.Length + " old lines kept out, two orders; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
