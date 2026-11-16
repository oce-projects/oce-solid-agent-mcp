using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 17 OF REPORT 8CD843 (28/10/2026), WHERE THE BENCH
    /// CANNOT REACH.
    ///
    /// N60: rounds 16 and 17 ran the same plan on the same assembly, and the balloons 4
    /// and 6, whose arrows ended 0.73 mm apart, swapped components, the bill of materials
    /// with them. Observation: title_block said «different_format» of three sheets of the
    /// same A3, because it compared the names of the format instances.
    ///
    /// Two halves, as Zc2199: the decisions by reflection, and the wiring on the
    /// sources, every rule first red on a changed copy. On library 0.174.7 every check
    /// is red.
    /// </summary>
    internal static class Zc2200DrawingRound17Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 17 of report 8cd843: the order of the balloon numbers chosen and the ties said (N60), the format of a sheet compared by its file");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 17 fixes",
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

            BalloonOrder(sw);
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

        private static void BalloonOrder(Type sw)
        {
            const string label = "N60: around_the_view is OrderSequentially (4), bill_of_materials is DoNotChangeItemNumbers (1), any other order is refused; the tie note names the swap and the way out, and is not said with no crowded arrow";
            try
            {
                var wrong = new List<string>();
                // swBalloonItemNumbersOrder_e: 1 DoNotChangeItemNumbers, 2 FollowAssemblyOrder, 4 OrderSequentially.
                var around = Call(sw, "BalloonItemOrder", "around_the_view") as int?;
                if (around != 4) wrong.Add("around_the_view gave " + (around == null ? "null" : around.ToString()));
                var table = Call(sw, "BalloonItemOrder", " Bill_Of_Materials ") as int?;
                if (table != 1) wrong.Add("bill_of_materials gave " + (table == null ? "null" : table.ToString()));
                foreach (var other in new[] { "assembly", "", null, "sequential" })
                    if (Call(sw, "BalloonItemOrder", other) != null) wrong.Add("«" + (other ?? "null") + "» was taken");

                var two = (string)Call(sw, "NumbersByTieNote", 2);
                foreach (var part in new[] { "2 balloons", "within 5 mm", "AutoBalloon5", "swapped", "«order» «bill_of_materials»" })
                    if (two == null || !two.Contains(part)) wrong.Add("the note lacks «" + part + "»: " + (two ?? "null"));
                var one = (string)Call(sw, "NumbersByTieNote", 1);
                if (one == null || !one.StartsWith("One balloon")) wrong.Add("one crowded arrow: " + (one ?? "null"));
                if (Call(sw, "NumbersByTieNote", 0) != null) wrong.Add("a note with no crowded arrow");
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "4 and 1, four other orders refused; the note with 2 and 1 balloons, none with 0" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING
        // =================================================================

        private const string OrderRead = "int? itemOrder = BalloonItemOrder(order);";
        private const string OrderRefused = "if (itemOrder == null)";
        private const string OrderUsed = "opt.ItemOrder = (int)itemOrder;";
        private const string OldOrder = "opt.ItemOrder = (int)swBalloonItemNumbersOrder_e.swBalloonItemNumbers_OrderSequentially;";
        private const string OrderSaid = "[\"order\"] = order,";
        private const string TieSaid = "measurements[\"numbers_decided_by_a_tie\"] = tied.ToArray();";
        private const string Spread = "var moves = SpreadCrowdedBalloons(model, view, out spreadNote);";
        // Since library 0.179.1 the file is named as the installation names it, also when a
        // local copy was opened (LocalCopies.cs).
        private const string FileOfActive = "[\"sheet_format_file\"] = SheetFormatFile(LocalCopies.Original(TestText(() => sheet.GetTemplateName()))),";
        private const string FileOfOther = "string formatFile = SheetFormatFile(LocalCopies.Original(TestText(() => other.GetTemplateName())));";
        private const string ComparedByFile = "string formatWarning = OtherFormatWarning(name, formatFile, activeFormat);";
        private const string OldCompared = "OtherFormatWarning(name, format, activeFormat)";

        private static readonly string[][] Files =
        {
            new[] { "bom", "Oce.Solid.Agent.Core", "BomOperations.cs" },
            new[] { "title", "Oce.Solid.Agent.Core", "TitleBlockOperations.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "bom", OrderRead, "«order» is not read, and the numbers are always around the view (N60)" },
            new[] { "bom", OrderRefused, "an order that is not one of the two goes to AutoBalloon5 (N60)" },
            new[] { "bom", OrderUsed, "AutoBalloon5 does not get the order asked (N60)" },
            new[] { "bom", OrderSaid, "the answer does not say the order of its numbers (N60)" },
            new[] { "bom", TieSaid, "the numbers a tie decided are not said (N60)" },
            new[] { "title", FileOfActive, "the format of the active sheet is known by the name of its instance only (observation)" },
            new[] { "title", FileOfOther, "the format of another sheet is known by the name of its instance only (observation)" },
            new[] { "title", ComparedByFile, "three sheets of the same A3 are said of different formats (observation)" },
        };

        /// <summary>The lines that must not come back, with what they bring back.</summary>
        private static readonly string[][] Gone =
        {
            new[] { "bom", OldOrder, "the numbers always around the view, whatever «order» says (N60)" },
            new[] { "title", OldCompared, "the formats compared by the names «...foglio1», «...foglio2» (observation)" },
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
            string bom;
            if (sources.TryGetValue("bom", out bom) && bom != null)
            {
                int read = bom.IndexOf(OrderRead, StringComparison.Ordinal);
                int used = bom.IndexOf(OrderUsed, StringComparison.Ordinal);
                if (read < 0 || used < read)
                    wrong.Add("the order is used before it is read and checked (N60)");
                int spread = bom.IndexOf(Spread, StringComparison.Ordinal);
                int tie = bom.IndexOf(TieSaid, StringComparison.Ordinal);
                if (spread < 0 || tie < spread)
                    wrong.Add("the ties are said before the crowded arrows are known (N60)");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: «order» read, refused when unknown, given to AutoBalloon5 and said; the ties said after the crowded arrows are known; the formats of the sheets compared by their file";
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Files)
            {
                var path = Path.Combine(Root(), f[1], f[2]);
                sources[f[0]] = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            var real = WiringProblems(sources);

            // Teeth: each line taken away, each old line put back, each order swapped.
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
            mutate("the order used before it is read", "bom",
                   t => t.Replace(OrderUsed, "/* moved */").Replace(OrderRead, OrderUsed + "\n" + OrderRead));
            mutate("the ties said before the spread", "bom",
                   t => t.Replace(TieSaid, "/* moved */").Replace(Spread, TieSaid + "\n" + Spread));

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : Lines.Length + " lines, " + Gone.Length + " old lines kept out, two orders; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
