using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// NO NEW CALL TO AN OBSOLETE SOLIDWORKS API (Z-C21.6).
    ///
    /// ── WHY ───────────────────────────────────────────────────────────────
    ///
    /// Obsolete does not mean broken: most of the obsolete calls in this code
    /// work on SOLIDWORKS 2021, and some were measured to be the only ones that
    /// work. But the lesson of FeatureFillet3 is that the day an obsolete call
    /// stops, a whole operation stops with it, and nobody had written down that
    /// it was obsolete. The audit Z-C21 counted the obsolete calls by hand; this
    /// check keeps the count from going back up.
    ///
    /// ── THE RULE ──────────────────────────────────────────────────────────
    ///
    /// Every call to a member of the refined obsolete list
    /// (Tests/data/obsolete-api-members.tsv, see ObsoleteCallScanner for how a
    /// call is recognised) must be one of:
    ///
    ///   1. MEASURED: covered by an `OBSOLETE-MEASURED: <bank case>` comment, on
    ///      the same line or in the comment block right above it, AND listed in
    ///      <see cref="Measured"/> with the exact number of calls per file,
    ///      member and case. It means: that bank case exercises this very call
    ///      and proves the effect the code relies on. It does NOT mean the
    ///      replacement fails: the entry says what is known of the replacement
    ///      (tried and failed, or not measured yet), and a "not measured" one is
    ///      still a migration candidate for Z-C21.7. The comment alone is not
    ///      enough: citing any existing case over a new call would otherwise
    ///      turn it green without touching this file (review of Z-C21.6);
    ///   2. FROZEN: in <see cref="Frozen"/>, "to migrate in Z-C21.7", counted per
    ///      file and member.
    ///
    /// Both lists are RATCHETS with exact counts: more calls than listed is
    /// red (a new obsolete call), fewer is red too (lower the number, so that
    /// the room left by a migration is not reused). Raising a number is a
    /// visible edit to this file, with a reason per member.
    ///
    /// ── WHAT IT CANNOT DECIDE, AND FREEZES ANYWAY ────────────────────────
    ///
    /// Calls whose receiver type the source does not say, on a name that is
    /// obsolete on some interface and current on another (`x.Select2`,
    /// `v.Name`), are "undecided". They are frozen too, per file and name, in
    /// Tests/data/obsolete-undecided-frozen.tsv, with the same exact ratchet: a
    /// new undecided call is red until its receiver is typed in the source
    /// (then the scanner judges it) or the count is raised with a reason.
    /// Checks/ (the probes, which call obsolete APIs on purpose to measure
    /// them) and Tests/ are not scanned.
    /// </summary>
    internal static class ObsoleteCallsContract
    {
        internal static readonly string[] Folders = { "Oce.Solid.Agent.Core", "Bridge", "Mcp", "Tools", "Shared", "Bench" };

        internal const string UndecidedFile = "obsolete-undecided-frozen.tsv";

        /// <summary>What a MEASURED entry knows about the obsolete call.</summary>
        internal sealed class MeasuredCall
        {
            public int Count;
            /// <summary>What is known of the replacement: "tried, fails: ..." or "not measured: ...".</summary>
            public string Replacement;
            public MeasuredCall(int count, string replacement) { Count = count; Replacement = replacement; }
        }

        /// <summary>
        /// The OBSOLETE-MEASURED calls, per "file|member|case", with how many
        /// calls the file has under that case. Exact, like <see cref="Frozen"/>.
        /// </summary>
        internal static readonly Dictionary<string, MeasuredCall> Measured = new Dictionary<string, MeasuredCall>(StringComparer.Ordinal)
        {
            ["Oce.Solid.Agent.Core/CenterMarkOperations.cs|IDrawingDoc.AddCenterMark|center_marks_on_the_holes"] =
                new MeasuredCall(1, "tried, fails: InsertCenterMark2/3 answer True and add nothing (probes of 17/9 and 26/9/2026)"),
            ["Oce.Solid.Agent.Core/DrawingRoundOperations.cs|IModelDoc2.GraphicsRedraw2|stepped_shaft_dimensioned_from_the_side"] =
                new MeasuredCall(2, "not measured: IModelView::GraphicsRedraw never tried here; the case proves the redraw makes " +
                                    "ISilhouetteEdge.GetFace answer (N20, remeasure in Z-C21.7)"),
        };

        /// <summary>
        /// The obsolete calls of 28/9/2026 that wait for Z-C21.7, per
        /// "file|member", with how many calls the file has. Exact.
        /// </summary>
        internal static readonly Dictionary<string, int> Frozen = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Bench/TestAssembly.cs|IModelDocExtension.SaveAs"] = 1,
            ["Bench/DeclaredCases.cs|IModelDoc2.GraphicsRedraw2"] = 1,
            ["Bench/DimensionNumbers.cs|IDimension.GetSystemValue2"] = 1,
            ["Bench/DimensionNumbers.cs|IModelDocExtension.SaveAs"] = 1,
            ["Bench/Orphans.cs|IModelDocExtension.SaveAs"] = 1,
            ["Bench/ClientPort.cs|IModelDocExtension.SaveAs"] = 1,
            ["Bench/NewOperationsTests.cs|IModelDoc2.GraphicsRedraw2"] = 2,
            ["Oce.Solid.Agent.Core/ApiHelpers.Geometry.cs|IEdge.GetCurveParams2"] = 1,
            ["Oce.Solid.Agent.Core/ApiHelpers.cs|IDimension.GetSystemValue2"] = 1,
            ["Oce.Solid.Agent.Core/ApiHelpers.cs|IModelDocExtension.CreateMassProperty"] = 1,
            ["Oce.Solid.Agent.Core/PlanExecutor.cs|IModelDocExtension.FinishRecordingUndoObject"] = 1,
            ["Oce.Solid.Agent.Core/MateOperations.cs|IAssemblyDoc.AddMate5"] = 1,
            ["Oce.Solid.Agent.Core/DrawingAnnotationOperations.cs|IGtol.GetFrameSymbols"] = 1,
            ["Oce.Solid.Agent.Core/DrawingAnnotationOperations.cs|IGtol.SetFrameSymbols"] = 1,
            ["Oce.Solid.Agent.Core/DrawingAnnotationOperations.cs|IGtol.SetFrameValues"] = 1,
            ["Oce.Solid.Agent.Core/CenterMarkOperations.cs|IView.Name"] = 1,
            ["Oce.Solid.Agent.Core/BodyOperations.cs|IFeatureManager.FeatureFillet3"] = 1,
            ["Oce.Solid.Agent.Core/BomOperations.cs|ITableAnnotation.GetColumnType2"] = 2,
            ["Oce.Solid.Agent.Core/DrawingCutListOperations.cs|ITableAnnotation.GetColumnType2"] = 2,
            ["Oce.Solid.Agent.Core/DrawingCutListOperations.cs|ITableAnnotation.Text"] = 1,
            ["Oce.Solid.Agent.Core/DrawingCutListOperations.cs|IView.Name"] = 1,
            ["Oce.Solid.Agent.Core/HoleOperations.cs|ISketchManager.AutoInference"] = 3,
            ["Oce.Solid.Agent.Core/SheetMetalOperations.cs|ISheetMetalFeatureData.KFactor"] = 3,
            ["Oce.Solid.Agent.Core/AssemblyMeasureOperations.cs|IBody2.Copy"] = 1,
            ["Oce.Solid.Agent.Core/AssemblyMeasureOperations.cs|IModelDoc2.GetActiveConfiguration"] = 1,
            ["Oce.Solid.Agent.Core/DrawingNoteOperations.cs|INote.GetTextPoint"] = 1,
            ["Oce.Solid.Agent.Core/PathOperations.cs|IFeatureManager.InsertProtrusionSwept4"] = 1,
            ["Oce.Solid.Agent.Core/PlaneOperations.cs|IModelDoc2.CreatePlaneFixed2"] = 1,
            ["Oce.Solid.Agent.Core/DrawingDimensionOperations.cs|IModelDoc2.GraphicsRedraw2"] = 1,
            ["Oce.Solid.Agent.Core/DraftOperations.cs|IDraftFeatureData.Angle"] = 1,
            ["Oce.Solid.Agent.Core/MirrorComponentOperations.cs|IBody2.Copy"] = 1,
            ["Oce.Solid.Agent.Core/DrawingFlatPatternOperations.cs|IModelDoc2.GetActiveConfiguration"] = 1,
            ["Oce.Solid.Agent.Core/SwOperations.cs|IFeatureManager.FeatureCircularPattern5"] = 1,
            ["Oce.Solid.Agent.Core/SwOperations.cs|IFeatureManager.FeatureFillet3"] = 1,
            ["Oce.Solid.Agent.Core/SwOperations.cs|IFeatureManager.FeatureLinearPattern5"] = 1,
            ["Oce.Solid.Agent.Core/DrawingOperations.cs|IDrawingDoc.InsertModelAnnotations2"] = 1,
            ["Oce.Solid.Agent.Core/DrawingOperations.cs|IModelDocExtension.SaveAs"] = 2,
            ["Oce.Solid.Agent.Core/DrawingOperations.cs|IView.GetDimensionCount"] = 1,
            ["Oce.Solid.Agent.Core/DrawingOperations.cs|IView.Name"] = 3,
            ["Oce.Solid.Agent.Core/TextOperations.cs|ICurve.Evaluate"] = 1,
            ["Oce.Solid.Agent.Core/TextOperations.cs|ISketchText.GetEdges"] = 1,
            ["Oce.Solid.Agent.Core/DimensionToleranceOperations.cs|IDimensionTolerance.GetMaxValue"] = 1,
            ["Oce.Solid.Agent.Core/DimensionToleranceOperations.cs|IDimensionTolerance.GetMinValue"] = 1,
            ["Oce.Solid.Agent.Core/CheckOperations.cs|IBody2.Check"] = 1,
            ["Oce.Solid.Agent.Core/CheckOperations.cs|IBody2.Check2"] = 1,
            ["Oce.Solid.Agent.Core/AssemblyState.cs|IConfiguration.ShowChildComponentsInBOM"] = 1,
            ["Oce.Solid.Agent.Core/AssemblyState.cs|IModelDoc2.GetActiveConfiguration"] = 1,
            ["Oce.Solid.Agent.Core/DocumentState.cs|IWizardHoleFeatureData2.FastenerType"] = 1,
            ["Oce.Solid.Agent.Core/DrawingState.cs|IView.GetDimensionCount"] = 1,
            ["Oce.Solid.Agent.Core/DrawingState.cs|IView.Name"] = 2,
            ["Oce.Solid.Agent.Core/PartView.cs|IModelDoc2.GraphicsRedraw2"] = 5,
        };

        /// <summary>
        /// Why each frozen member is still called, with the audit finding. A
        /// frozen member without a reason is red.
        /// </summary>
        internal static readonly Dictionary<string, string> WhyFrozen = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IAssemblyDoc.AddMate5"] = "L11-2: superseded by IAssemblyDoc::CreateMate; measured working (swAddMateError_NoError, three coincident cases), CreateMate never tried",
            ["IBody2.Check"] = "L12-4: superseded by IBody2::Check3, which Z-C21.7 makes the single judge of the geometry check",
            ["IBody2.Check2"] = "L12-4: superseded by IBody2::Check3, as Check",
            ["IBody2.Copy"] = "L11-6: superseded by IBody2::Copy2; the temporary copies of the mirror and of the assembly mass",
            ["IConfiguration.ShowChildComponentsInBOM"] = "superseded by IConfiguration::ChildComponentDisplayInBOM; read-only use in the assembly state",
            ["ICurve.Evaluate"] = "M30: superseded by ICurve::Evaluate2 (sketch-text outline)",
            ["IDimension.GetSystemValue2"] = "S1: the drawing readers (ApiHelpers.ReadDimensionInConfiguration) stay here, MEASURED: GetSystemValue3 answers null in every form on the dimensions a drawing owns (probe_dimension_drawing_owned, 30/9/2026, 21 of 21), and the migration of Z-C21.7 that moved them was withdrawn after 12 failures of the drawing cases on 0.143.0. The part reader (ApiHelpers.ReadDimension) moved from SystemValue to GetSystemValue3 after Z-C21.13 (probe_dimension_value3). Also one bench reader.",
            ["IDimensionTolerance.GetMaxValue"] = "L5-3: superseded by IDimensionTolerance::GetMaxValue2",
            ["IDimensionTolerance.GetMinValue"] = "L5-3: superseded by IDimensionTolerance::GetMinValue2",
            ["IDraftFeatureData.Angle"] = "L4-6: IDraftFeatureData2::Angle is current",
            ["IDrawingDoc.InsertModelAnnotations2"] = "superseded by IDrawingDoc::InsertModelAnnotations3 (model dimensions into the drawing)",
            ["IEdge.GetCurveParams2"] = "S12/L3-2: only in ApiHelpers.EdgeParameterArray, for the positional array (with its sense flag) of the sketch-text outline",
            ["IFeatureManager.FeatureCircularPattern5"] = "L7-6: CreateDefinition(swFmCirPattern) through ApiHelpers.CreateFeatureFromDefinition",
            ["IFeatureManager.FeatureFillet3"] = "L7-1: obsolete for constant-radius fillets since SOLIDWORKS 2020; CreateDefinition route, behind the fillet cases",
            ["IFeatureManager.FeatureLinearPattern5"] = "L7-4: CreateDefinition(swFmLPattern) through ApiHelpers.CreateFeatureFromDefinition",
            ["IFeatureManager.InsertProtrusionSwept4"] = "L2-3: the 2018 sweep architecture, CreateDefinition(swFmSweep)",
            ["IGtol.GetFrameSymbols"] = "L16-5/6/7: the IGtol 2/2/3 versions",
            ["IGtol.SetFrameSymbols"] = "L16-5/6/7: the IGtol 2/2/3 versions",
            ["IGtol.SetFrameValues"] = "L16-5/6/7: superseded by IGtol::SetFrameValues2",
            ["IModelDoc2.CreatePlaneFixed2"] = "superseded by IFeatureManager::InsertRefPlane (plane through three points); the Z-C21.3 probe measured only the " +
                                               "offset plane (mark 0, Distance constraint, migrated in Z-C21.7), not the three-point plane: not measured yet",
            ["IModelDoc2.GetActiveConfiguration"] = "M44/C4-1: IConfigurationManager::ActiveConfiguration",
            ["IModelDoc2.GraphicsRedraw2"] = "N20/M44: IModelView::GraphicsRedraw; the two in OperazioniTondoTavola are OBSOLETE-MEASURED",
            ["IModelDocExtension.CreateMassProperty"] = "S2/L17-3: only in ApiHelpers.NewMassProperty, for two callers the Z-C21.3 probe did not cover. The ASSEMBLY " +
                                                        "(OperazioniMisuraAssieme): probe_mass_property2 measured CreateMassProperty2 on a part only, and on an " +
                                                        "assembly it works on components (hidden ones: IncludeHiddenBodiesOrComponents, not measured). The DOCUMENT " +
                                                        "STATE (DocumentState.Mass): CreateMassProperty2 measures what is selected, the probe cleared the selection " +
                                                        "first, and the state reads the designer's selection afterwards; left in place, a pre-selected body is " +
                                                        "measured alone (N2/N3: 16000 mm3 instead of 21000), and whether SelectedItems replaces a pre-selection " +
                                                        "was not probed. The other part readings moved to CreateMassProperty2 in Z-C21.7 (N1 = M0, N5 = M1, N8 = M3)",
            ["IModelDocExtension.FinishRecordingUndoObject"] = "L17-4: superseded by FinishRecordingUndoObject2",
            ["IModelDocExtension.SaveAs"] = "L13-4/L13-8: superseded by SaveAs2/SaveAs3 (drawing export and bank harness)",
            ["INote.GetTextPoint"] = "superseded by INote::GetTextPoint2",
            ["ISketchManager.AutoInference"] = "superseded by the user preference swSketchInference; read and restored around the hole sketches",
            ["ISheetMetalFeatureData.KFactor"] = "superseded by ISheetMetalFeatureData::GetCustomBendAllowance/SetCustomBendAllowance (ICustomBendAllowance); read-only, the K-factor of the flat length. Found once the scanner followed the return type of SheetMetalData",
            ["ISketchText.GetEdges"] = "superseded by ISketchText::GetEdges2 (stick-font text)",
            ["ITableAnnotation.GetColumnType2"] = "superseded by ITableAnnotation::GetColumnType3 (bill of materials)",
            ["ITableAnnotation.Text"] = "superseded by ITableAnnotation::Text2 (bill of materials)",
            ["IView.GetDimensionCount"] = "C3-3/N20: superseded by IView::GetDimensionCount4",
            ["IView.Name"] = "superseded by IView::GetName2/SetName2",
            ["IWizardHoleFeatureData2.FastenerType"] = "superseded by IWizardHoleFeatureData2::FastenerType2 (hole description in the state)",
        };

        internal static void RunAll()
        {
            Group("no new call to an obsolete SOLIDWORKS API (Z-C21.6)");

            var listPath = Path.Combine(Root(), "Tests", "data", "obsolete-api-members.tsv");
            if (!File.Exists(listPath))
            {
                Check("the refined obsolete list is there", false, listPath + " is missing: regenerate it with Checks/obsolete-api-list.js");
                return;
            }
            var list = ObsoleteApiList.Load(listPath);

            // ── THE LIST IS THE REFINED ONE ──────────────────────────────
            //
            // GetSystemValue2 was MISSED by the raw list (the marker sits after
            // the parameter table), FeatureExtrusion3 was a FALSE positive in it
            // (its page mentions "the now obsolete FeatureExtrusion2"), and
            // ICurve.Length2 has no page at all, only such a mention.
            bool refined = list.Members.Count >= 3000 &&
                           list.Members.Contains("IDimension.GetSystemValue2") &&
                           list.Members.Contains("IModelDoc2.GraphicsRedraw2") &&
                           list.Members.Contains("IView.GetDimensionCount") &&
                           list.Members.Contains("IModelDoc2.GetActiveConfiguration") &&
                           list.Members.Contains("ICurve.Length2") &&
                           !list.Members.Contains("IFeatureManager.FeatureExtrusion3") &&
                           !list.Members.Contains("IModelDocExtension.SaveAs3");
            Check("the obsolete list is the refined one", refined,
                  list.Members.Count + " members (" + list.Partial + " obsolete only for some uses); " +
                  "GetSystemValue2, GraphicsRedraw2, GetDimensionCount, GetActiveConfiguration, ICurve.Length2 in; " +
                  "FeatureExtrusion3 and IModelDocExtension.SaveAs3 (current) out");

            // ── THE SCANNER HAS TEETH, AND DOES NOT BITE THE WRONG THING ─
            //
            // Snippets are whole files; "\n" separates lines.
            var mustFlag = new[]
            {
                "IDimension dimension = null; double v = dimension.SystemValue;",
                "IModelDoc2 model = null; var mp = model.Extension.CreateMassProperty();",
                "IModelDoc2 d = null; var c = d.GetActiveConfiguration();",
                "object o = null; var t = (o as IView).GetDimensionCount();",
                "IFeatureManager fm = null; fm.FeatureFillet3(0, 0, 0, 0, 0, 0, 0, null, null, null, null, null, null, null);",
                // the code in the holes of an interpolated string is code
                "IModelDoc2 model = null; var s = $\"{model.GetActiveConfiguration()}\";",
                "IModelDoc2 model = null; var s = $@\"a {{b}} {model.GetActiveConfiguration():X} c\";",
                // the type of a var from the return type of a method in the source
                "class C {\n IView ResolveView(int a) { return null; }\n void M() {\n var view = ResolveView(1);\n var n = view.Name;\n }\n}",
                // an element of a typed array
                "class C { void M() { IView[] views = null; var n = views[0].Name; } }",
                // a field of the same partial class declared in another part
                "partial class C { IView _view; }\npartial class D { }\npartial class C { void M() { var n = _view.Name; } }",
            };
            var mustPass = new[]
            {
                "IFeatureManager fm = null; fm.FeatureExtrusion3(true);",
                "IModelDoc2 model = null; model.Extension.SaveAs3(p, 0, 1, null, null, ref e, ref w);",
                "IFeature f = null; f.Select2(false, 0);",
                "var list = new List<int>(); list.Add(1);",
                "// quota.SystemValue is obsolete, see the guide",
                "var s = \"dimension.SystemValue\";",
                "var s = $\"dimension.SystemValue {x}\";",
                "IPartDoc partDoc = null; var b = partDoc.GetBodies2(0, false);",
            };
            // A type declared OUTSIDE the scope of the call must not be used: the
            // call is undecided (counted and frozen), not current, not typed.
            var mustBeUndecided = new[]
            {
                "class C {\n void A() { IConfigurationManager g = null; }\n void B() { var g = Find(); var n = g.Name; }\n}",
                "class C {\n void A(IFeature f) { }\n void B() { var n = f.Select2(false, 0); }\n}",
            };
            var missed = mustFlag.Where(l => ScanSnippet(list, l).Count != 1).ToList();
            var wrong = mustPass.Where(l => ScanSnippet(list, l).Count != 0).ToList();
            var notUndecided = mustBeUndecided.Where(l =>
            {
                var s = new ObsoleteCallScanner(list);
                s.Add("snippet.cs", l.Split('\n'));
                return s.Scan().Count != 0 || s.Undecided != 1;
            }).ToList();
            Check("the scanner catches obsolete calls on typed receivers", missed.Count == 0,
                  (mustFlag.Length - missed.Count) + " of " + mustFlag.Length + " (declared, accessor, cast, interpolated string, " +
                  "method return type, array element, field of a partial class)" +
                  (missed.Count == 0 ? "" : "; missed: " + string.Join(" | ", missed)));
            Check("and does not flag current members, comments or strings", wrong.Count == 0,
                  wrong.Count == 0 ? mustPass.Length + " lines, no false alarm" : string.Join(" | ", wrong));
            Check("a type declared out of scope leaves the call undecided", notUndecided.Count == 0,
                  notUndecided.Count == 0 ? mustBeUndecided.Length + " snippets: the other method's variable is not used"
                                          : string.Join(" | ", notUndecided));

            // ── THE REAL SOURCE ──────────────────────────────────────────
            var cases = BankCases();
            var scanner = new ObsoleteCallScanner(list);
            int files = 0;
            var sources = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var folder in Folders)
            {
                var dir = Path.Combine(Root(), folder);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var rel = f.Substring(Root().Length).TrimStart('\\', '/').Replace('\\', '/');
                    if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
                    var lines = File.ReadAllLines(f);
                    sources[rel] = lines;
                    scanner.Add(rel, lines);
                    files++;
                }
            }
            var hits = scanner.Scan();
            var undecidedPath = Path.Combine(Root(), "Tests", "data", UndecidedFile);
            var frozenUndecided = LoadUndecided(undecidedPath);
            var v = Evaluate(hits, scanner.UndecidedCalls, cases, frozenUndecided, null);
            Check("every obsolete call is measured or frozen", v.Violations.Count == 0,
                  v.Violations.Count == 0
                      ? hits.Count + " obsolete calls in " + files + " files: " + v.Measured +
                        " OBSOLETE-MEASURED, " + v.FrozenUsed + " frozen for Z-C21.7"
                      : v.Violations.Count + " not allowed: " + string.Join(" | ", v.Violations));

            Check("the measured and frozen lists only go down (no stale room left)", v.Stale.Count == 0,
                  v.Stale.Count == 0 ? Measured.Count + " measured and " + Frozen.Count + " frozen entries, each one exact"
                                     : "lower these counts: " + string.Join(" | ", v.Stale));

            var noReason = Frozen.Keys.Select(k => k.Substring(k.IndexOf('|') + 1)).Distinct()
                                 .Where(m => !WhyFrozen.ContainsKey(m) || WhyFrozen[m].Trim().Length < 10).ToList();
            var badMeasured = Measured.Where(kv => kv.Value.Replacement == null ||
                                                   !(kv.Value.Replacement.StartsWith("tried", StringComparison.Ordinal) ||
                                                     kv.Value.Replacement.StartsWith("not measured", StringComparison.Ordinal)) ||
                                                   !cases.Contains(kv.Key.Substring(kv.Key.LastIndexOf('|') + 1)))
                                      .Select(kv => kv.Key).ToList();
            Check("every frozen member says why, every measured one what is known of its replacement",
                  noReason.Count == 0 && badMeasured.Count == 0,
                  noReason.Count == 0 && badMeasured.Count == 0
                      ? WhyFrozen.Count + " reasons, " + Measured.Count + " measured entries with an existing case"
                      : "no reason for " + string.Join(", ", noReason) + "; measured entries without \"tried\"/\"not measured\" " +
                        "or with a case not in Bench/cases: " + string.Join(", ", badMeasured));

            // The undecided calls: counted, and frozen with the same ratchet.
            bool undecidedOk = frozenUndecided != null && v.UndecidedRaised.Count == 0 && v.UndecidedStale.Count == 0;
            string dump = undecidedOk ? null : DumpUndecided(scanner.UndecidedCalls);
            Check("undecided calls (receiver type not in the source) do not grow", undecidedOk,
                  undecidedOk
                      ? scanner.Undecided + " undecided calls in " + frozenUndecided.Count + " file|name entries, each one exact " +
                        "(most frequent: " + string.Join(", ", scanner.UndecidedCalls.GroupBy(u => u.Name)
                                                                  .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                                                                  .Take(5).Select(g => g.Key + " " + g.Count())) + ")"
                      : (frozenUndecided == null ? undecidedPath + " is missing. " : "") +
                        (v.UndecidedRaised.Count > 0
                            ? "NEW undecided calls (type the receiver in the source, or raise the count with a reason): " +
                              string.Join(" | ", v.UndecidedRaised) + ". " : "") +
                        (v.UndecidedStale.Count > 0 ? "Lower these counts: " + string.Join(" | ", v.UndecidedStale) + ". " : "") +
                        "The file as it would be today: " + dump);

            // ── SEEN RED ─────────────────────────────────────────────────
            //
            // The same scan, on temporary copies of real source files, each with
            // one change. Every change must turn the file red, and say what it
            // changed, while the untouched file is green.
            SeenRed(list, sources, cases, frozenUndecided, hits, scanner.UndecidedCalls);
        }

        /// <summary>The obsolete calls in one snippet (a single "file"; "\n" separates lines).</summary>
        private static List<ObsoleteHit> ScanSnippet(ObsoleteApiList list, string text)
        {
            var s = new ObsoleteCallScanner(list);
            s.Add("snippet.cs", text.Split('\n'));
            return s.Scan();
        }

        private static void SeenRed(ObsoleteApiList list, Dictionary<string, string[]> sources, HashSet<string> cases,
                                    Dictionary<string, int> frozenUndecided, List<ObsoleteHit> baseHits,
                                    List<UndecidedCall> baseUndecided)
        {
            const string equations = "Oce.Solid.Agent.Core/EquationOperations.cs";
            const string assemblyMass = "Oce.Solid.Agent.Core/AssemblyMeasureOperations.cs";

            // 1. A new obsolete call on a typed receiver, in a file with no entry for it.
            Inject(list, sources, cases, frozenUndecided, baseHits, baseUndecided, equations, "a new obsolete call",
                   "IModelDoc2 model", true, "            var injected = model.GetActiveConfiguration();",
                   (before, after, line) => after.Violations.Count == 1 && Names(after.Violations[0], "GetActiveConfiguration", equations, line));

            // 2. The ratchet up: a SECOND GetActiveConfiguration where one is frozen.
            Inject(list, sources, cases, frozenUndecided, baseHits, baseUndecided, assemblyMass, "a second call where one is frozen",
                   "GetActiveConfiguration()", false, "            var injected = model.GetActiveConfiguration();",
                   (before, after, line) => after.Violations.Count == 1 && after.Violations[0].Contains("2 calls, frozen allows 1") &&
                                            after.Violations[0].Contains(assemblyMass + ":" + line));

            // 3. The ratchet down: the frozen call removed, the entry must be lowered.
            Inject(list, sources, cases, frozenUndecided, baseHits, baseUndecided, assemblyMass, "the frozen call removed, its entry left",
                   "GetActiveConfiguration()", false, null,
                   (before, after, line) => after.Stale.Count == 1 &&
                                            after.Stale[0].Contains(assemblyMass + "|IModelDoc2.GetActiveConfiguration\" 1 -> 0"));

            // 4. A new call under an OBSOLETE-MEASURED comment citing an existing case.
            Inject(list, sources, cases, frozenUndecided, baseHits, baseUndecided, equations, "a new call under a marker citing a real case",
                   "IModelDoc2 model", true,
                   "            // OBSOLETE-MEASURED: three_holes_pitch_40\n            var injected = model.GetActiveConfiguration();",
                   (before, after, line) => after.Violations.Count == 1 && after.Violations[0].Contains("not in the Measured list") &&
                                            Names(after.Violations[0], "GetActiveConfiguration", equations, line));

            // 5. A new undecided call (receiver type not in the source).
            Inject(list, sources, cases, frozenUndecided, baseHits, baseUndecided, equations, "a new undecided call",
                   "IModelDoc2 model", true, "            var injected = unknown.Select2(false, 0);",
                   (before, after, line) => after.UndecidedRaised.Count == 1 && after.UndecidedRaised[0].Contains("|Select2") &&
                                            after.UndecidedRaised[0].Contains(equations + ":" + line));
        }

        private static bool Names(string violation, string member, string file, int line)
        {
            return violation.Contains(member) && violation.Contains(file + ":" + line);
        }

        /// <summary>
        /// Scans the tree with a changed copy of one real file, and checks the
        /// verdict on that file against the verdict on the untouched tree
        /// (<paramref name="baseHits"/>). The change goes after the first line
        /// containing <paramref name="anchorText"/> (after its opening brace when
        /// <paramref name="afterBrace"/>), or at it; a null
        /// <paramref name="insert"/> REMOVES the anchor line instead.
        /// </summary>
        private static void Inject(ObsoleteApiList list, Dictionary<string, string[]> sources, HashSet<string> cases,
                                   Dictionary<string, int> frozenUndecided, List<ObsoleteHit> baseHits,
                                   List<UndecidedCall> baseUndecided, string target, string what,
                                   string anchorText, bool afterBrace, string insert, Func<Verdict, Verdict, int, bool> expected)
        {
            var name = "seen red: " + what + " (" + Path.GetFileName(target) + ")";
            string[] original;
            if (!sources.TryGetValue(target, out original)) { Check(name, false, target + " not found"); return; }
            int anchor = Array.FindIndex(original, l => l.Contains(anchorText) && !l.TrimStart().StartsWith("//", StringComparison.Ordinal));
            if (anchor < 0) { Check(name, false, "no «" + anchorText + "» in " + target); return; }
            string temp = Path.Combine(Path.GetTempPath(), "obsolete-seen-red-" + Guid.NewGuid().ToString("N") + ".cs");
            try
            {
                var copy = original.ToList();
                int line;   // 1-based line of the change in the copy
                if (insert == null) { copy.RemoveAt(anchor); line = anchor + 1; }
                else
                {
                    int at = anchor;
                    if (afterBrace)
                    {
                        while (at < copy.Count && !copy[at].Contains("{")) at++;
                        at++;
                    }
                    var added = insert.Split('\n');
                    copy.InsertRange(at, added);
                    line = at + added.Length;   // the last inserted line carries the call
                }
                File.WriteAllLines(temp, copy);

                // The whole tree is scanned (the types and methods of the other
                // files decide receivers here too), but only the entries of the
                // changed file are judged: whatever else is wrong in the tree is
                // the business of the checks above, not of this one.
                var changed = new ObsoleteCallScanner(list);
                foreach (var kv in sources) changed.Add(kv.Key, kv.Key == target ? File.ReadAllLines(temp) : kv.Value);
                var before = Evaluate(baseHits, baseUndecided, cases, frozenUndecided, target);
                var after = Evaluate(changed.Scan(), changed.UndecidedCalls, cases, frozenUndecided, target);
                bool clean = before.Violations.Count == 0 && before.Stale.Count == 0 &&
                             before.UndecidedRaised.Count == 0 && before.UndecidedStale.Count == 0;
                bool red = clean && expected(before, after, line);
                var all = after.Violations.Concat(after.Stale).Concat(after.UndecidedRaised).Concat(after.UndecidedStale).ToList();
                Check(name, red,
                      red ? "red: " + Short(all[0])
                          : (clean ? "" : "the untouched file is not green; ") + "got: " + string.Join(" | ", all));
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }

        private static string Short(string s) { return s.Length <= 170 ? s : s.Substring(0, 167) + "..."; }

        private static HashSet<string> BankCases()
        {
            var cases = new HashSet<string>(StringComparer.Ordinal);
            var dir = Path.Combine(Root(), "Bench", "cases");
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                    cases.Add(Path.GetFileNameWithoutExtension(f));
            return cases;
        }

        /// <summary>"file|name" -> count, or null when the file is missing.</summary>
        internal static Dictionary<string, int> LoadUndecided(string path)
        {
            if (!File.Exists(path)) return null;
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var raw in File.ReadAllLines(path))
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                var cols = raw.Split('\t');
                int n;
                if (cols.Length >= 2 && int.TryParse(cols[1], out n)) d[cols[0]] = n;
            }
            return d;
        }

        /// <summary>Writes the undecided file as it would be today to a temporary path, and returns the path.</summary>
        private static string DumpUndecided(List<UndecidedCall> calls)
        {
            var lines = new List<string>
            {
                "# Calls to names that are obsolete on some SOLIDWORKS interface and current on another, whose",
                "# receiver type the source does not say (Tests/ObsoleteCallsContract.cs). Exact counts per",
                "# file|name: a new one is red. Type the receiver in the source rather than raising a count;",
                "# a raised count needs a reason in the commit.",
            };
            lines.AddRange(calls.GroupBy(u => u.File + "|" + u.Name).OrderBy(g => g.Key, StringComparer.Ordinal)
                                .Select(g => g.Key + "\t" + g.Count()));
            var path = Path.Combine(Path.GetTempPath(), UndecidedFile);
            try { File.WriteAllLines(path, lines); } catch { return "(could not write " + path + ")"; }
            return path;
        }

        /// <summary>The verdict on a set of hits.</summary>
        internal sealed class Verdict
        {
            public readonly List<string> Violations = new List<string>();
            public readonly List<string> Stale = new List<string>();
            public readonly List<string> UndecidedRaised = new List<string>();
            public readonly List<string> UndecidedStale = new List<string>();
            public int Measured, FrozenUsed;
        }

        /// <summary>
        /// Which hits are not allowed, which listed counts are larger than
        /// reality. With <paramref name="onlyFile"/>, only the entries of that
        /// file are compared (a scan of one file).
        /// </summary>
        internal static Verdict Evaluate(List<ObsoleteHit> hits, List<UndecidedCall> undecided, HashSet<string> cases,
                                         Dictionary<string, int> frozenUndecided, string onlyFile)
        {
            var v = new Verdict();
            Func<string, bool> inScope = key => onlyFile == null || key.StartsWith(onlyFile + "|", StringComparison.Ordinal);
            if (onlyFile != null)
            {
                hits = hits.Where(h => h.File == onlyFile).ToList();
                undecided = undecided.Where(u => u.File == onlyFile).ToList();
            }
            var rest = new List<ObsoleteHit>();
            var measuredHits = new List<ObsoleteHit>();
            foreach (var h in hits)
            {
                if (!h.MeasuredMarker) { rest.Add(h); continue; }
                if (h.MeasuredCase == null || !cases.Contains(h.MeasuredCase))
                {
                    v.Violations.Add(h.File + ":" + h.Line + " " + h.Member + " — " + ObsoleteCallScanner.MeasuredMarker +
                                     " cites " + (h.MeasuredCase == null ? "no case" : "«" + h.MeasuredCase + "», which is not in Bench/cases"));
                    continue;
                }
                measuredHits.Add(h);
            }
            foreach (var g in measuredHits.GroupBy(h => h.File + "|" + h.Member + "|" + h.MeasuredCase).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                MeasuredCall entry;
                Measured.TryGetValue(g.Key, out entry);
                int allowed = entry == null ? 0 : entry.Count, count = g.Count();
                v.Measured += Math.Min(count, allowed);
                if (count > allowed)
                    v.Violations.Add(string.Join(", ", g.Select(h => h.File + ":" + h.Line)) + " " + g.First().Member + " — " +
                                     ObsoleteCallScanner.MeasuredMarker + " " + g.First().MeasuredCase + ": " + count + " calls, " +
                                     (entry == null ? "not in the Measured list" : "Measured allows " + allowed) +
                                     " [measured key \"" + g.Key + "\"]");
            }
            var actualMeasured = measuredHits.GroupBy(h => h.File + "|" + h.Member + "|" + h.MeasuredCase)
                                             .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var kv in Measured.Where(k => inScope(k.Key)).OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                int n;
                actualMeasured.TryGetValue(kv.Key, out n);
                if (n < kv.Value.Count) v.Stale.Add("measured \"" + kv.Key + "\" " + kv.Value.Count + " -> " + n);
            }

            foreach (var g in rest.GroupBy(h => h.File + "|" + h.Member).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                int allowed;
                Frozen.TryGetValue(g.Key, out allowed);
                int count = g.Count();
                v.FrozenUsed += Math.Min(count, allowed);
                if (count > allowed)
                    v.Violations.Add(string.Join(", ", g.Select(h => h.File + ":" + h.Line)) + " " + g.First().Member +
                                     " (" + g.First().How + ") — " + count + " calls, frozen allows " + allowed +
                                     " [frozen key \"" + g.Key + "\"]");
            }
            var actual = rest.GroupBy(h => h.File + "|" + h.Member).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var kv in Frozen.Where(k => inScope(k.Key)).OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                int n;
                actual.TryGetValue(kv.Key, out n);
                if (n < kv.Value) v.Stale.Add("frozen \"" + kv.Key + "\" " + kv.Value + " -> " + n);
            }

            if (frozenUndecided != null)
            {
                var byKey = undecided.GroupBy(u => u.File + "|" + u.Name).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
                foreach (var kv in byKey.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    int allowed;
                    frozenUndecided.TryGetValue(kv.Key, out allowed);
                    if (kv.Value.Count > allowed)
                        v.UndecidedRaised.Add(kv.Key + " " + allowed + " -> " + kv.Value.Count + " (" +
                                              string.Join(", ", kv.Value.Select(u => u.File + ":" + u.Line)) + ")");
                }
                foreach (var kv in frozenUndecided.Where(k => inScope(k.Key)).OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    List<UndecidedCall> l;
                    int n = byKey.TryGetValue(kv.Key, out l) ? l.Count : 0;
                    if (n < kv.Value) v.UndecidedStale.Add(kv.Key + " " + kv.Value + " -> " + n);
                }
            }
            return v;
        }
    }
}
