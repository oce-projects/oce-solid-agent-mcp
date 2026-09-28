using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
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
    /// (Prove/data/obsolete-api-members.tsv, see ObsoleteCallScanner for how a
    /// call is recognised) must be one of:
    ///
    ///   1. covered by an `OBSOLETE-MEASURED: <bank case>` comment, on the same
    ///      line or in the comment block right above it, citing a case that
    ///      exists in Banco/casi. It means: the replacement was tried and does
    ///      not work, and this case is the one that proves the obsolete call
    ///      does;
    ///   2. in the FROZEN list below, "to migrate in Z-C21.7", counted per file
    ///      and member. The list is a RATCHET: a file may have fewer calls than
    ///      frozen (and then this check asks to lower the number, so that the
    ///      room left by a migration is not reused), never more.
    ///
    /// A new obsolete call is therefore red until someone either measures it
    /// against its replacement or migrates it. Raising a frozen number is a
    /// visible edit to this file, with a reason per member.
    ///
    /// ── WHAT IT CANNOT SEE ───────────────────────────────────────────────
    ///
    /// Calls whose receiver type the source does not say, on a name that is
    /// current on some other interface, are "undecided" and counted in the
    /// output, not judged. Controlli/ (the probes, which call obsolete APIs on
    /// purpose to measure them) and Prove/ are not scanned.
    /// </summary>
    internal static class ObsoleteCallsContract
    {
        internal static readonly string[] Folders = { "Riscontro.Disegno", "Ponte", "Mcp", "Strumenti", "Condiviso", "Banco" };

        /// <summary>
        /// The obsolete calls of 28/9/2026 that wait for Z-C21.7, per
        /// "file|member", with how many calls the file has. It may only go down.
        /// </summary>
        internal static readonly Dictionary<string, int> Frozen = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Banco/AssiemeDiProva.cs|IModelDocExtension.SaveAs"] = 1,
            ["Banco/CasiDichiarati.cs|IModelDoc2.GraphicsRedraw2"] = 1,
            ["Banco/NumeriDelleQuote.cs|IDimension.GetSystemValue2"] = 1,
            ["Banco/NumeriDelleQuote.cs|IModelDocExtension.SaveAs"] = 1,
            ["Banco/Orfane.cs|IModelDocExtension.SaveAs"] = 1,
            ["Banco/PortaDelCliente.cs|IModelDocExtension.CreateMassProperty"] = 1,
            ["Banco/PortaDelCliente.cs|IModelDocExtension.SaveAs"] = 1,
            ["Banco/Program.cs|IBody2.Select"] = 1,
            ["Banco/Program.cs|IModelDoc2.Select"] = 1,
            ["Banco/ProvaNuove.cs|IModelDoc2.GraphicsRedraw2"] = 2,
            ["Riscontro.Disegno/ApiHelpers.Geometry.cs|IEdge.GetCurveParams2"] = 1,
            ["Riscontro.Disegno/ApiHelpers.cs|IDimension.GetSystemValue2"] = 1,
            ["Riscontro.Disegno/ApiHelpers.cs|IDimension.SystemValue"] = 1,
            ["Riscontro.Disegno/ApiHelpers.cs|IModelDocExtension.CreateMassProperty"] = 1,
            ["Riscontro.Disegno/EsecutorePiano.cs|IModelDocExtension.FinishRecordingUndoObject"] = 1,
            ["Riscontro.Disegno/OperazioniAccoppia.cs|IAssemblyDoc.AddMate5"] = 1,
            ["Riscontro.Disegno/OperazioniAnnotazioniTavola.cs|IGtol.GetFrameSymbols"] = 1,
            ["Riscontro.Disegno/OperazioniAnnotazioniTavola.cs|IGtol.SetFrameSymbols"] = 1,
            ["Riscontro.Disegno/OperazioniAnnotazioniTavola.cs|IGtol.SetFrameValues"] = 1,
            ["Riscontro.Disegno/OperazioniCentrini.cs|IView.Name"] = 1,
            ["Riscontro.Disegno/OperazioniCorpi.cs|IFeatureManager.FeatureFillet3"] = 1,
            ["Riscontro.Disegno/OperazioniDistintaBase.cs|IComponent2.ExcludeFromBOM"] = 1,
            ["Riscontro.Disegno/OperazioniDistintaBase.cs|ITableAnnotation.GetColumnType2"] = 2,
            ["Riscontro.Disegno/OperazioniDistintaTavola.cs|ITableAnnotation.GetColumnType2"] = 2,
            ["Riscontro.Disegno/OperazioniDistintaTavola.cs|ITableAnnotation.Text"] = 1,
            ["Riscontro.Disegno/OperazioniFori.cs|ISketchManager.AutoInference"] = 3,
            ["Riscontro.Disegno/OperazioniLamiera.cs|IFeatureManager.InsertSheetMetalEdgeFlange2"] = 1,
            ["Riscontro.Disegno/OperazioniLamiera.cs|IPartDoc.ExportFlatPatternView"] = 1,
            ["Riscontro.Disegno/OperazioniMisuraAssieme.cs|IBody2.Copy"] = 1,
            ["Riscontro.Disegno/OperazioniMisuraAssieme.cs|IModelDoc2.GetActiveConfiguration"] = 1,
            ["Riscontro.Disegno/OperazioniNotaTavola.cs|INote.GetTextPoint"] = 1,
            ["Riscontro.Disegno/OperazioniPercorso.cs|IFeatureManager.InsertProtrusionSwept4"] = 1,
            ["Riscontro.Disegno/OperazioniPercorso.cs|IModelDoc2.CreatePlaneAtOffset3"] = 1,
            ["Riscontro.Disegno/OperazioniPiani.cs|IModelDoc2.CreatePlaneFixed2"] = 1,
            ["Riscontro.Disegno/OperazioniQuoteTavola.cs|IModelDoc2.GraphicsRedraw2"] = 1,
            ["Riscontro.Disegno/OperazioniRicalco.cs|ISketchManager.InsertSketchPicture"] = 1,
            ["Riscontro.Disegno/OperazioniSformo.cs|IDraftFeatureData.Angle"] = 1,
            ["Riscontro.Disegno/OperazioniSpecchiaComponente.cs|IBody2.Copy"] = 1,
            ["Riscontro.Disegno/OperazioniSviluppoTavola.cs|IModelDoc2.GetActiveConfiguration"] = 1,
            ["Riscontro.Disegno/OperazioniSw.cs|IFeature.IsSuppressed"] = 1,
            ["Riscontro.Disegno/OperazioniSw.cs|IFeatureManager.FeatureCircularPattern5"] = 1,
            ["Riscontro.Disegno/OperazioniSw.cs|IFeatureManager.FeatureFillet3"] = 1,
            ["Riscontro.Disegno/OperazioniSw.cs|IFeatureManager.FeatureLinearPattern5"] = 1,
            ["Riscontro.Disegno/OperazioniSw.cs|IModelDoc2.CreatePlaneAtOffset3"] = 1,
            ["Riscontro.Disegno/OperazioniSw.cs|ISketchManager.CreateSpline"] = 1,
            ["Riscontro.Disegno/OperazioniTabellaFori.cs|IView.InsertHoleTable2"] = 1,
            ["Riscontro.Disegno/OperazioniTavola.cs|IDrawingDoc.InsertModelAnnotations2"] = 1,
            ["Riscontro.Disegno/OperazioniTavola.cs|IModelDocExtension.SaveAs"] = 2,
            ["Riscontro.Disegno/OperazioniTavola.cs|IView.GetDimensionCount"] = 1,
            ["Riscontro.Disegno/OperazioniTavola.cs|IView.Name"] = 3,
            ["Riscontro.Disegno/OperazioniTesto.cs|ICurve.Evaluate"] = 1,
            ["Riscontro.Disegno/OperazioniTesto.cs|ISketchText.GetEdges"] = 1,
            ["Riscontro.Disegno/OperazioniTolleranzaQuota.cs|IDimensionTolerance.GetMaxValue"] = 2,
            ["Riscontro.Disegno/OperazioniTolleranzaQuota.cs|IDimensionTolerance.GetMinValue"] = 2,
            ["Riscontro.Disegno/OperazioniVerifica.cs|IBody2.Check"] = 1,
            ["Riscontro.Disegno/OperazioniVerifica.cs|IBody2.Check2"] = 1,
            ["Riscontro.Disegno/StatoAssieme.cs|IConfiguration.ShowChildComponentsInBOM"] = 1,
            ["Riscontro.Disegno/StatoAssieme.cs|IModelDoc2.GetActiveConfiguration"] = 1,
            ["Riscontro.Disegno/StatoDocumento.cs|IChamferFeatureData.EdgeChamferAngle"] = 2,
            ["Riscontro.Disegno/StatoDocumento.cs|IChamferFeatureData.GetEdgeChamferDistance"] = 2,
            ["Riscontro.Disegno/StatoDocumento.cs|IFeature.IsSuppressed"] = 1,
            ["Riscontro.Disegno/StatoDocumento.cs|IWizardHoleFeatureData2.FastenerType"] = 1,
            ["Riscontro.Disegno/StatoTavola.cs|IView.GetDimensionCount"] = 1,
            ["Riscontro.Disegno/StatoTavola.cs|IView.Name"] = 2,
            ["Riscontro.Disegno/VistaDelPezzo.cs|IModelDoc2.GraphicsRedraw2"] = 5,
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
            ["IBody2.Select"] = "bank harness (Banco/Program.cs) selecting a body for a probe; superseded by IBody2::Select2",
            ["IChamferFeatureData.EdgeChamferAngle"] = "the state reads chamfers through the old IChamferFeatureData; IChamferFeatureData2::EdgeChamferAngle is current",
            ["IChamferFeatureData.GetEdgeChamferDistance"] = "as EdgeChamferAngle: IChamferFeatureData2::GetEdgeChamferDistance is current",
            ["IComponent2.ExcludeFromBOM"] = "L16-3: superseded by GetExcludeFromBOM2/SetExcludeFromBOM2, per configuration (pattern S8)",
            ["IConfiguration.ShowChildComponentsInBOM"] = "superseded by IConfiguration::ChildComponentDisplayInBOM; read-only use in the assembly state",
            ["ICurve.Evaluate"] = "M30: superseded by ICurve::Evaluate2 (sketch-text outline)",
            ["IDimension.GetSystemValue2"] = "S1: only in ApiHelpers.ReadDimensionInConfiguration and one bank reader; GetSystemValue3 returns an object whose shape waits for the Z-C21.3 probe",
            ["IDimension.SystemValue"] = "S1: only in ApiHelpers.ReadDimension; GetSystemValue3 waits for the Z-C21.3 probe",
            ["IDimensionTolerance.GetMaxValue"] = "L5-3: superseded by IDimensionTolerance::GetMaxValue2",
            ["IDimensionTolerance.GetMinValue"] = "L5-3: superseded by IDimensionTolerance::GetMinValue2",
            ["IDraftFeatureData.Angle"] = "L4-6: IDraftFeatureData2::Angle is current",
            ["IDrawingDoc.InsertModelAnnotations2"] = "superseded by IDrawingDoc::InsertModelAnnotations3 (model dimensions into the drawing)",
            ["IEdge.GetCurveParams2"] = "S12/L3-2: only in ApiHelpers.EdgeParameterArray, for the positional array (with its sense flag) of the sketch-text outline",
            ["IFeature.IsSuppressed"] = "S3: superseded by IFeature::IsSuppressed2 per configuration (Config_names null is a Z-C21.3 probe)",
            ["IFeatureManager.FeatureCircularPattern5"] = "L7-6: CreateDefinition(swFmCirPattern) through ApiHelpers.CreateFeatureFromDefinition",
            ["IFeatureManager.FeatureFillet3"] = "L7-1: obsolete for constant-radius fillets since SOLIDWORKS 2020; CreateDefinition route, behind the fillet cases",
            ["IFeatureManager.FeatureLinearPattern5"] = "L7-4: CreateDefinition(swFmLPattern) through ApiHelpers.CreateFeatureFromDefinition",
            ["IFeatureManager.InsertProtrusionSwept4"] = "L2-3: the 2018 sweep architecture, CreateDefinition(swFmSweep)",
            ["IFeatureManager.InsertSheetMetalEdgeFlange2"] = "L10-1: CreateDefinition/CreateFeature, if the Z-C21.3 probe succeeds",
            ["IGtol.GetFrameSymbols"] = "L16-5/6/7: the IGtol 2/2/3 versions",
            ["IGtol.SetFrameSymbols"] = "L16-5/6/7: the IGtol 2/2/3 versions",
            ["IGtol.SetFrameValues"] = "L16-5/6/7: superseded by IGtol::SetFrameValues2",
            ["IModelDoc2.CreatePlaneAtOffset3"] = "L8-1 = L2-4: InsertRefPlane to be probed again with mark 0 (Z-C21.3); offset plane and loft",
            ["IModelDoc2.CreatePlaneFixed2"] = "superseded by IFeatureManager::InsertRefPlane (plane through three points), with the same probe",
            ["IModelDoc2.GetActiveConfiguration"] = "M44/C4-1: IConfigurationManager::ActiveConfiguration",
            ["IModelDoc2.GraphicsRedraw2"] = "N20/M44: IModelView::GraphicsRedraw; the two in OperazioniTondoTavola are OBSOLETE-MEASURED",
            ["IModelDoc2.Select"] = "bank harness (Banco/Program.cs); superseded by IModelDocExtension::SelectByID2",
            ["IModelDocExtension.CreateMassProperty"] = "S2: only in ApiHelpers.NewMassProperty and one bank reader; CreateMassProperty2 works by selection, probe in Z-C21.3",
            ["IModelDocExtension.FinishRecordingUndoObject"] = "L17-4: superseded by FinishRecordingUndoObject2",
            ["IModelDocExtension.SaveAs"] = "L13-4/L13-8: superseded by SaveAs2/SaveAs3 (drawing export and bank harness)",
            ["INote.GetTextPoint"] = "superseded by INote::GetTextPoint2",
            ["IPartDoc.ExportFlatPatternView"] = "L10-2: superseded by IPartDoc::ExportToDWG2",
            ["ISketchManager.AutoInference"] = "superseded by the user preference swSketchInference; read and restored around the hole sketches",
            ["ISketchManager.CreateSpline"] = "L1-1: superseded by CreateSpline2/CreateSpline3 (triples/pairs, probe in Z-C21.3)",
            ["ISketchManager.InsertSketchPicture"] = "L8-4: superseded by InsertSketchPicture2 (same behaviour on parts per the guide)",
            ["ISketchText.GetEdges"] = "superseded by ISketchText::GetEdges2 (stick-font text)",
            ["ITableAnnotation.GetColumnType2"] = "superseded by ITableAnnotation::GetColumnType3 (bill of materials)",
            ["ITableAnnotation.Text"] = "superseded by ITableAnnotation::Text2 (bill of materials)",
            ["IView.GetDimensionCount"] = "C3-3/N20: superseded by IView::GetDimensionCount4",
            ["IView.InsertHoleTable2"] = "L15-1: InsertHoleTable3 never tried; the marks 1/2 of the 2 are measured",
            ["IView.Name"] = "superseded by IView::GetName2/SetName2",
            ["IWizardHoleFeatureData2.FastenerType"] = "superseded by IWizardHoleFeatureData2::FastenerType2 (hole description in the state)",
        };

        internal static void RunAll()
        {
            Gruppo("no new call to an obsolete SOLIDWORKS API (Z-C21.6)");

            var listPath = Path.Combine(Radice(), "Prove", "data", "obsolete-api-members.tsv");
            if (!File.Exists(listPath))
            {
                Segna("the refined obsolete list is there", false, listPath + " is missing: regenerate it with Controlli/obsolete-api-list.js");
                return;
            }
            var list = ObsoleteApiList.Load(listPath);

            // ── THE LIST IS THE REFINED ONE ──────────────────────────────
            //
            // Two members prove it: GetSystemValue2 was MISSED by the raw list
            // (the marker sits after the parameter table), FeatureExtrusion3 was
            // a FALSE positive in it (its page mentions "the now obsolete
            // FeatureExtrusion2").
            bool refined = list.Members.Count >= 3000 &&
                           list.Members.Contains("IDimension.GetSystemValue2") &&
                           list.Members.Contains("IModelDoc2.GraphicsRedraw2") &&
                           list.Members.Contains("IView.GetDimensionCount") &&
                           list.Members.Contains("IModelDoc2.GetActiveConfiguration") &&
                           !list.Members.Contains("IFeatureManager.FeatureExtrusion3") &&
                           !list.Members.Contains("IModelDocExtension.SaveAs3");
            Segna("the obsolete list is the refined one", refined,
                  list.Members.Count + " members (" + list.Partial + " obsolete only for some uses); " +
                  "GetSystemValue2, GraphicsRedraw2, GetDimensionCount, GetActiveConfiguration in; " +
                  "FeatureExtrusion3 and IModelDocExtension.SaveAs3 (current) out");

            // ── THE SCANNER HAS TEETH, AND DOES NOT BITE THE WRONG THING ─
            var mustFlag = new[]
            {
                "IDimension quota = null; double v = quota.SystemValue;",
                "IModelDoc2 modello = null; var mp = modello.Extension.CreateMassProperty();",
                "IModelDoc2 d = null; var c = d.GetActiveConfiguration();",
                "object o = null; var t = (o as IView).GetDimensionCount();",
                "IFeatureManager fm = null; fm.FeatureFillet3(0, 0, 0, 0, 0, 0, 0, null, null, null, null, null, null, null);",
            };
            var mustPass = new[]
            {
                "IFeatureManager fm = null; fm.FeatureExtrusion3(true);",
                "IModelDoc2 modello = null; modello.Extension.SaveAs3(p, 0, 1, null, null, ref e, ref w);",
                "IFeature f = null; f.Select2(false, 0);",
                "var list = new List<int>(); list.Add(1);",
                "// quota.SystemValue is obsolete, see the guide",
                "var s = \"quota.SystemValue\";",
                "IPartDoc parte = null; var b = parte.GetBodies2(0, false);",
            };
            int caught = mustFlag.Count(l => ScanSnippet(list, l).Count == 1);
            var wrong = mustPass.Where(l => ScanSnippet(list, l).Count != 0).ToList();
            Segna("the scanner catches obsolete calls on typed receivers", caught == mustFlag.Length,
                  caught + " of " + mustFlag.Length);
            Segna("and does not flag current members, comments or strings", wrong.Count == 0,
                  wrong.Count == 0 ? mustPass.Length + " lines, no false alarm" : string.Join(" | ", wrong));

            // ── THE REAL SOURCE ──────────────────────────────────────────
            var cases = BankCases();
            var scanner = new ObsoleteCallScanner(list);
            int files = 0;
            var sources = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var folder in Folders)
            {
                var dir = Path.Combine(Radice(), folder);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var rel = f.Substring(Radice().Length).TrimStart('\\', '/').Replace('\\', '/');
                    if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
                    var lines = File.ReadAllLines(f);
                    sources[rel] = lines;
                    scanner.Add(rel, lines);
                    files++;
                }
            }
            var hits = scanner.Scan();
            List<string> violations, stale;
            int measured, frozenUsed;
            Evaluate(hits, cases, out violations, out stale, out measured, out frozenUsed);
            Segna("every obsolete call is measured or frozen", violations.Count == 0,
                  violations.Count == 0
                      ? hits.Count + " obsolete calls in " + files + " files: " + measured +
                        " OBSOLETE-MEASURED, " + frozenUsed + " frozen for Z-C21.7; " +
                        scanner.Undecided + " calls undecided (ambiguous name, receiver type not in the source)"
                      : violations.Count + " not allowed: " + string.Join(" | ", violations));

            Segna("the frozen list only goes down (no stale room left)", stale.Count == 0,
                  stale.Count == 0 ? Frozen.Count + " frozen entries, each one exact"
                                   : "lower these frozen counts: " + string.Join(" | ", stale));

            var noReason = Frozen.Keys.Select(k => k.Substring(k.IndexOf('|') + 1)).Distinct()
                                 .Where(m => !WhyFrozen.ContainsKey(m) || WhyFrozen[m].Trim().Length < 10).ToList();
            Segna("every frozen member says why it is still called", noReason.Count == 0,
                  noReason.Count == 0 ? WhyFrozen.Count + " reasons" : "no reason for " + string.Join(", ", noReason));

            // ── SEEN RED: A NEW OBSOLETE CALL IN A COPY OF A REAL FILE ───
            //
            // The same scan, on a temporary copy of a real source file with one
            // obsolete call injected. It must be red, and name the injected line,
            // while the untouched file stays green.
            SeenRed(list, sources, cases);
        }

        /// <summary>The obsolete calls in one snippet (a single "file").</summary>
        private static List<ObsoleteHit> ScanSnippet(ObsoleteApiList list, string line)
        {
            var s = new ObsoleteCallScanner(list);
            s.Add("snippet.cs", new[] { line });
            return s.Scan();
        }

        private static void SeenRed(ObsoleteApiList list, Dictionary<string, string[]> sources, HashSet<string> cases)
        {
            const string target = "Riscontro.Disegno/OperazioniEquazioni.cs";
            string[] original;
            if (!sources.TryGetValue(target, out original))
            {
                Segna("seen red on an injected obsolete call", false, target + " not found");
                return;
            }
            int anchor = Array.FindIndex(original, l => l.Contains("IModelDoc2 modello"));
            if (anchor < 0)
            {
                Segna("seen red on an injected obsolete call", false, "no `IModelDoc2 modello` in " + target);
                return;
            }
            string temp = Path.Combine(Path.GetTempPath(), "obsolete-seen-red-" + Guid.NewGuid().ToString("N") + ".cs");
            try
            {
                var copy = original.ToList();
                int brace = anchor;
                while (brace < copy.Count && !copy[brace].Contains("{")) brace++;
                copy.Insert(brace + 1, "            var injected = modello.GetActiveConfiguration();");
                File.WriteAllLines(temp, copy);

                // The file alone, before and after: whatever else is wrong in the
                // tree is the business of the check above, not of this one.
                List<string> before, after, stale;
                int measured, frozenUsed;
                var untouched = new ObsoleteCallScanner(list);
                untouched.Add(target, original);
                Evaluate(untouched.Scan(), cases, out before, out stale, out measured, out frozenUsed);
                var injected = new ObsoleteCallScanner(list);
                injected.Add(target, File.ReadAllLines(temp));
                Evaluate(injected.Scan(), cases, out after, out stale, out measured, out frozenUsed);
                var named = after.Where(v => v.Contains("GetActiveConfiguration") && v.Contains(target + ":" + (brace + 2))).ToList();
                bool red = after.Count == before.Count + 1 && named.Count == 1;
                Segna("seen red on an obsolete call injected in a copy of " + Path.GetFileName(target), red,
                      red ? "red, and it names the injected line: " + named[0]
                          : "expected one more violation naming the injected line, got " + before.Count + " -> " +
                            after.Count + ": " + string.Join(" | ", after));
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }

        private static HashSet<string> BankCases()
        {
            var cases = new HashSet<string>(StringComparer.Ordinal);
            var dir = Path.Combine(Radice(), "Banco", "casi");
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                    cases.Add(Path.GetFileNameWithoutExtension(f));
            return cases;
        }

        /// <summary>
        /// The verdict on a set of hits: which ones are not allowed, which frozen
        /// counts are larger than reality.
        /// </summary>
        internal static void Evaluate(
            List<ObsoleteHit> hits, HashSet<string> cases,
            out List<string> violations, out List<string> stale, out int measured, out int frozenUsed)
        {
            violations = new List<string>();
            stale = new List<string>();
            measured = 0;
            frozenUsed = 0;
            var rest = new List<ObsoleteHit>();
            foreach (var h in hits)
            {
                if (!h.MeasuredMarker) { rest.Add(h); continue; }
                if (h.MeasuredCase != null && cases.Contains(h.MeasuredCase)) { measured++; continue; }
                violations.Add(h.File + ":" + h.Line + " " + h.Member + " — " + ObsoleteCallScanner.MeasuredMarker +
                               " cites " + (h.MeasuredCase == null ? "no case" : "«" + h.MeasuredCase + "», which is not in Banco/casi"));
            }
            foreach (var g in rest.GroupBy(h => h.File + "|" + h.Member).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                int allowed;
                Frozen.TryGetValue(g.Key, out allowed);
                int count = g.Count();
                frozenUsed += Math.Min(count, allowed);
                if (count > allowed)
                    violations.Add(string.Join(", ", g.Select(h => h.File + ":" + h.Line)) + " " + g.First().Member +
                                   " (" + g.First().How + ") — " + count + " calls, frozen allows " + allowed +
                                   " [frozen key \"" + g.Key + "\"]");
            }
            var actual = rest.GroupBy(h => h.File + "|" + h.Member).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var kv in Frozen.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                int n;
                actual.TryGetValue(kv.Key, out n);
                if (n < kv.Value) stale.Add("\"" + kv.Key + "\" " + kv.Value + " -> " + n);
            }
        }
    }
}
