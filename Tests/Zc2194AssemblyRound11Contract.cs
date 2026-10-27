using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE FIXES OF ROUND 11 OF REPORT 8CD843 (27/10/2026), WHERE THE BENCH
    /// CANNOT REACH.
    ///
    /// N39: a coupler with one of its mates suppressed read «constrained», 0
    /// degrees of freedom and «mates» 3, and moved 6.5 mm. N40: a plan on a
    /// drawing that put a section and stopped said «refused and changed nothing I
    /// compare» beside «the 1 step(s) before it stay», and the undo after it said
    /// «as it was» while the section stayed. N41: a detail on a view of an assembly
    /// was refused three times out of three: drawn on the sketch of the sheet, its
    /// circle went to another view (a probe on two copies of the drawing). N42: section
    /// and detail counted the sheet as a view.
    ///
    /// Two halves, as Zc2193: the decisions by reflection, the wiring on the
    /// sources, every rule first red on a changed copy. On library 0.174.1 and
    /// bridge 0.152.1 every check is red.
    /// </summary>
    internal static class Zc2194AssemblyRound11Contract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the fixes of round 11 of report 8cd843: suppressed mates, a stopped plan on a drawing, the detail of a view of an assembly, the views counted (N39-N42)");

            Wiring();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the round 11 fixes",
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

            MatesThatHold(sw);
            DegreesLeftOut(sw, state);

            var bridge = BridgeOrNotHere("the memory of a plan on a drawing");
            if (bridge == null) return;
            Type tools;
            try { tools = Load.Class(Load.Bridge(bridge), "McpTools"); }
            catch (Exception ex) { Check("the bridge loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (tools == null) { Check("McpTools is in the bridge", false, "type missing"); return; }
            DrawingMemory(tools);
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

        private static void MatesThatHold(Type sw)
        {
            const string label = "N39: the mates that hold a component are all of them less the suppressed ones, and an unreadable count stays unknown";
            try
            {
                var wrong = new List<string>();
                Func<int, int, int> active = (all, off) => (int)Call(sw, "ActiveMates", all, off);
                if (active(3, 1) != 2) wrong.Add("3 with 1 suppressed: " + active(3, 1));
                if (active(1, 1) != 0) wrong.Add("1 suppressed of 1: " + active(1, 1));
                if (active(2, 0) != 2) wrong.Add("none suppressed: " + active(2, 0));
                if (active(-1, 0) != -1) wrong.Add("mates unreadable: " + active(-1, 0));
                if (active(2, -1) != -1) wrong.Add("suppression unreadable: " + active(2, -1));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "3-1 = 2, 1-1 = 0, 2-0 = 2, two unknowns" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void DegreesLeftOut(Type sw, Type state)
        {
            const string label = "N39: the degrees of freedom of a component with a suppressed mate are left out with the reason, and the state says when SOLIDWORKS gives no number";
            try
            {
                var wrong = new List<string>();
                var note = (string)Call(sw, "DofWithSuppressedMateNote", "constrained", 0, 1);
                foreach (var part in new[] { "SOLIDWORKS answered «constrained» with 0 free direction(s)", "1 mate of this component is suppressed",
                                             "moved 6.5 mm", "the number is left out", "«mate» measures" })
                    if (note == null || !note.Contains(part)) wrong.Add("the note lacks «" + part + "»: " + (note ?? "null"));
                var two = (string)Call(sw, "DofWithSuppressedMateNote", "constrained", 0, 2);
                if (two == null || !two.Contains("2 mates of this component are suppressed")) wrong.Add("two suppressed: " + (two ?? "null"));
                var text = (string)Call(state, "DegreesNotGivenText", new List<string> { "C8 (a mate suppressed)" }, 8);
                foreach (var part in new[] { "just opened", "8 of 8 «unavailable» on opening", "suppressed mate is left out", "three of the six", "at least one mate" })
                    if (text == null || !text.Contains(part)) wrong.Add("the state lacks «" + part + "»: " + (text ?? "null"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "the note with the state, the count and the remedy; the state with the opening and the suppressed mate" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        private static void DrawingMemory(Type tools)
        {
            const string label = "N40: after a plan on a drawing the undo says a view stays too, and the remedy says who can remove a view";
            try
            {
                var wrong = new List<string>();
                var reason = (string)Call(tools, "DrawingPlanMemoryReason", "drawing", new List<string> { "section_view" }, "Clip - Sheet1");
                if (reason == null || !reason.Contains("left the view on the sheet") || !reason.Contains("undo_entry_recorded"))
                    wrong.Add("the reason: " + (reason ?? "null"));
                var field = tools.GetField("DrawingNoMemoryRemedy", Any);
                var remedy = field == null ? null : field.GetValue(null) as string;
                if (remedy == null || !remedy.Contains("A view (a section, a detail, a model view) has no operation that removes it yet"))
                    wrong.Add("the remedy: " + (remedy ?? "missing"));
                Check(label, wrong.Count == 0, wrong.Count == 0 ? "the reason names the view that stayed, the remedy the views" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
        }

        // =================================================================
        // THE WIRING, ON THE SOURCES
        // =================================================================

        private const string ActiveCount = "int itsAcc = SwOperations.ActiveMates(SwOperations.MatesOf(c), itsOff);";
        private const string OldCount = "int itsAcc = SwOperations.MatesOf(c);";
        private const string SuppressedSaid = "if (itsOff >= 0) entry[\"mates_suppressed\"] = itsOff;";
        private const string DegreesLeft = "if (deg.Available && itsOff > 0)";
        private const string KindsSkip = "if (feature != null && ApiHelpers.IsSuppressedHere(feature)) continue;";
        private const string FreeActive = "return ActiveMatesOf(c) == 0 ? name : null;";
        private const string OldFree = "return MatesOf(c) == 0 ? name : null;";
        private const string MemoryRule = "((memoryBefore == null && memoryAction == MemoryKeep) || memoryAction == MemoryNew))";
        private const string OldMemoryRule = "if (onDrawing != null && memoryBefore == null && memoryAction == MemoryKeep && !hasBuilt)";
        private const string Activate = "try { inView = drawing.ActivateView(targetName); } catch { }";
        // Since library 0.174.4 (N47) the circle is drawn in the coordinates of the
        // sketch of the view, and the view is always left for the sheet: Zc2196 reads
        // where.
        private const string CircleDrawn = "circle = model.SketchManager.CreateCircleByRadius(centre[0], centre[1], 0.0, radius / xform[2])";
        private const string BackToSheet = "try { drawing.EditSheet(); } catch { }";
        private const string ModelViewsOnly = "if (w.Type != (int)swDrawingViewTypes_e.swDrawingSheet) viewsAfter++;";
        private const string OldViewsCount = "while (w != null && viewsAfter < 200)";
        private const string OtherSheet = "or another sheet of the drawing («activate_sheet», then this step again)";
        private const string Phases = "PhaseClock.Time(\"drawing_dimension.visible_edges\"";

        private static readonly string[][] Files =
        {
            new[] { "state", "Oce.Solid.Agent.Core", "AssemblyState.cs" },
            new[] { "assembly", "Oce.Solid.Agent.Core", "DrawingAssemblyDimensions.cs" },
            new[] { "detail", "Oce.Solid.Agent.Core", "DrawingDetailOperations.cs" },
            new[] { "section", "Oce.Solid.Agent.Core", "DrawingSectionOperations.cs" },
            new[] { "bom", "Oce.Solid.Agent.Core", "BomOperations.cs" },
            new[] { "dimension", "Oce.Solid.Agent.Core", "DrawingDimensionOperations.cs" },
            new[] { "tools", "Tools", "McpTools.cs" },
        };

        /// <summary>The lines each file must hold, with what is lost without them.</summary>
        private static readonly string[][] Lines =
        {
            new[] { "state", ActiveCount, "a suppressed mate still counts among the mates that hold a component (N39)" },
            new[] { "state", SuppressedSaid, "the suppressed mates of a component are not said (N39)" },
            new[] { "state", DegreesLeft, "the degrees of freedom of a component with a suppressed mate are given as true (N39)" },
            new[] { "state", KindsSkip, "the kinds of the mates count a suppressed one (N39)" },
            new[] { "assembly", FreeActive, "a component held only by suppressed mates is not called loose on a drawing (N39)" },
            new[] { "tools", MemoryRule, "a plan on a drawing that kept an undo entry leaves a memory the comparison cannot check (N40)" },
            new[] { "detail", Activate, "the circle of a detail goes on the sketch of the sheet, and to another view (N41)" },
            new[] { "detail", BackToSheet, "the view activated for the circle stays active for the next step (N41)" },
            new[] { "detail", ModelViewsOnly, "a detail counts the sheet as a view (N42)" },
            new[] { "section", ModelViewsOnly, "a section counts the sheet as a view (N42)" },
            new[] { "bom", OtherSheet, "a bill of materials with no room names only a bigger sheet (round 11)" },
            new[] { "dimension", Phases, "the phases of drawing_dimension are not timed (round 11)" },
        };

        /// <summary>The lines that must not come back, with what they bring back.</summary>
        private static readonly string[][] Gone =
        {
            new[] { "state", OldCount, "«mates» counts the suppressed mates again (N39)" },
            new[] { "assembly", OldFree, "a loose component is judged on every mate again (N39)" },
            new[] { "tools", OldMemoryRule, "the memory of a plan on a drawing is decided only when nothing was recorded (N40)" },
            new[] { "detail", OldViewsCount, "the detail counts every view of the list again (N42)" },
            new[] { "section", OldViewsCount, "the section counts every view of the list again (N42)" },
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
                int activate = detail.IndexOf(Activate, StringComparison.Ordinal);
                int circle = detail.IndexOf(CircleDrawn, StringComparison.Ordinal);
                if (activate < 0 || circle < 0 || activate > circle)
                    wrong.Add("the view is activated after the circle is drawn, which then lands on the sheet (N41)");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "wired: the active mates and the suppressed ones in the state, no memory of a plan on a drawing, the circle of a detail in its view, the views of the model counted, the other sheet for a bill of materials, the phases timed";
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in Files)
            {
                var path = Path.Combine(Root(), f[1], f[2]);
                sources[f[0]] = File.Exists(path) ? File.ReadAllText(path) : null;
            }
            var real = WiringProblems(sources);

            // Teeth: each line taken away, each old line put back, the order swapped.
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
            mutate("the view activated after the circle", "detail",
                   t => t.Replace(Activate, "").Replace(CircleDrawn, CircleDrawn + ";\n" + Activate));

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : Lines.Length + " lines, " + Gone.Length + " old lines kept out, the circle drawn in the view activated first; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
