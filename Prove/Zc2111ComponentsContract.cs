using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// WHICH COMPONENTS OF AN ASSEMBLY COUNT: LIGHTWEIGHT, ENVELOPE AND HIDDEN
    /// COMPONENTS (TODO entry Z-C21.11, findings N5, N27, M23).
    ///
    /// Measured on 30/9/2026 by the two Z-C21.11 probes, of lightweight, envelope
    /// and hidden components and of the indented BOM (SOLIDWORKS 2021 SP5.1):
    ///  - a LIGHTWEIGHT component answers IComponent2.IsSuppressed() TRUE and
    ///    GetModelDoc2 null; GetSuppression2 answers 1 (lightweight), GetBodies3
    ///    gives its bodies with the volume of its file, Transform2 is there, and
    ///    IsHidden(false) is false (IsHidden(true) is true). Up to library 0.146.0
    ///    the mass, the box and the STEP count took such a part for suppressed
    ///    and dropped it: no mass and no box on an assembly opened lightweight,
    ///    a partial box answered as a success with one part lightweight, and the
    ///    BOM left a lightweight sub-assembly out with all its children;
    ///  - an ENVELOPE is left out of the assembly mass by CreateMassProperty (A +
    ///    envelope E weighed A only), and the STEP writes it;
    ///  - a HIDDEN component is in the assembly mass, and the STEP does NOT write
    ///    it (A + hidden B: 1 solid, the old count asked 2);
    ///  - the INDENTED BOM writes one row per group of siblings (same file and
    ///    configuration), and under a sub-assembly row the children of ONE
    ///    instance; the old count compared it as the flat list of parts and
    ///    refused every indented table of an assembly with a sub-assembly.
    ///
    /// The bench cannot open an assembly lightweight, make an envelope or hide a
    /// component (a case builds parts and plain assemblies only), so what those
    /// states change is checked here:
    ///  * the pure rules (the load state from GetSuppression2, part or
    ///    sub-assembly from the file extension, the indented BOM rows) are static
    ///    methods with no interop in their signature, called on the BUILT library
    ///    by reflection with the probe's numbers and trees;
    ///  * the rest is read in the source: no component state read through
    ///    IsSuppressed, the old reader gone, the four readers (mass, box, STEP
    ///    count, BOM) going through AssemblyComponents, and what each of them
    ///    does with lightweight, envelope and hidden components. Every source
    ///    check also runs on changed copies of the real source, each of which
    ///    must turn it red: a check never seen red proves nothing.
    ///
    /// On library 0.146.0 every check is red but two, which guard what it already
    /// read right (the indented T_flat and T_prom; T_ind parts only and top level
    /// only): AssemblyComponents and its rules do not exist, the component state
    /// is read with IsSuppressed, and the indented rows are the old flat count
    /// (T_ind: A 1, B 7). Seen on the sources of main (d45f320) and on a library
    /// built before Z-C21.11; all green on the Z-C21.11 sources and a library
    /// built from them.
    /// </summary>
    internal static class Zc2111ComponentsContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private const string Library = "Riscontro.Disegno";
        private const string Shared = "AssemblyComponents.cs";
        private const string Measures = "OperazioniMisuraAssieme.cs";
        private const string Export = "OperazioniEsporta.cs";
        private const string Bom = "OperazioniDistintaBase.cs";
        private const string Catalogue = "Catalogo/Operazioni.cs";

        /// <summary>
        /// The library's own member names this group reads, in one literal as the
        /// other contracts write them: the assembly mass, the assembly box, the
        /// STEP count, the BOM reader, and the old reader that must stay gone.
        /// </summary>
        private static readonly string[] OwnNames =
            "ProprietaDiMassaAssieme IngombroAssieme SolidiAttesiNellAssieme ComponentiPerVoce PezziDellAssieme".Split(' ');
        private static readonly string MassMember = OwnNames[0];
        private static readonly string BoxMember = OwnNames[1];
        private static readonly string StepCountMember = OwnNames[2];
        private static readonly string BomMember = OwnNames[3];
        private static readonly string OldReader = OwnNames[4];

        /// <summary>The operation of the catalogue whose schema must declare the mass argument.</summary>
        private const string MassOperation = "proprieta_di_massa";

        internal static void RunAll()
        {
            Gruppo("which components count: lightweight, envelope and hidden components of an assembly (Z-C21.11)");

            var sources = ReadLibrary();
            if (sources != null) SourceChecks(sources);
            PureRules();
        }

        // ══ THE SOURCE ═════════════════════════════════════════════════════

        private static void SourceChecks(Dictionary<string, string> sources)
        {
            Checked(sources, "IsSuppressed",
                "no component state is read with IsSuppressed() in the library: it answers true on a lightweight component",
                NoIsSuppressed,
                "no .IsSuppressed() call in the code of the library; the state is read with GetSuppression2",
                Change(Bom, "the BOM back on the component getter", "= item.Load == ComponentLoad.Suppressed;", "= c.IsSuppressed();"),
                Change(Shared, "the shared reader on the component getter", "state = c.GetSuppression2();", "state = c.IsSuppressed() ? 0 : 2;"));

            Checked(sources, "old reader",
                "the old reader of the assembly parts is gone (it took lightweight parts for suppressed)",
                OldReaderGone,
                OldReader + " is declared and called nowhere in the library",
                Change(Measures, "the old reader declared again", "private static List<AssemblyComponent> ComponentsOrRefusal(",
                       "private static List<IComponent2> " + OldReader + "(IAssemblyDoc doc) { return null; }\n" +
                       "        private static List<AssemblyComponent> ComponentsOrRefusal("));

            Checked(sources, "shared reader",
                "AssemblyComponents reads the state with GetSuppression2, hidden with IsHidden(false), the kind from the extension",
                SharedReaderProblems,
                "Describe: GetSuppression2, IsHidden(false) only, GetModelDoc2 once and only when resolved, IsPart and " +
                "IsSubAssembly from IsPartFile and IsAssemblyFile of the saved path",
                Change(Shared, "a lightweight component taken for hidden", "current.IsHidden(false)", "current.IsHidden(true)"),
                Change(Shared, "a lightweight part not a part", "item.IsPart = IsPartFile(item.Path);", "item.IsPart = item.Document is IPartDoc;"),
                Change(Shared, "the document read on a lightweight component", "if (item.Load == ComponentLoad.Resolved)",
                       "if (item.Load != ComponentLoad.Suppressed)"),
                Change(Shared, "the state not read with GetSuppression2", "state = c.GetSuppression2();", "state = c.IsSuppressed() ? 0 : 2;"));

            // Review of Z-C21.11: with the kind read from the extension alone, a
            // resolved component whose saved path ends otherwise was neither a
            // part nor a sub-assembly, and left the mass, the box and the STEP
            // count in silence (0.146.0 read the kind from the document).
            Checked(sources, "kind",
                "a component whose extension does not say its kind takes it from its document, and one without either is not skipped",
                KindProblems,
                "Describe falls back to «Document is IPartDoc / IAssemblyDoc»; ComponentsOrRefusal refuses on " +
                "AssemblyComponents.KindUnknown; the STEP count gives -1 on a component that is neither",
                Change(Shared, "no fallback to the document", "item.IsSubAssembly = item.Document is IAssemblyDoc;", ""),
                Change(Measures, "a component of unknown kind skipped by the mass and the box", ".Where(AssemblyComponents.KindUnknown)", ".Where(c => false)"),
                Change(Measures, "a component of unknown kind skipped by the STEP count", "if (item.IsSubAssembly) continue; return -1; }", "continue; }"));

            Checked(sources, "four readers",
                "the assembly mass, box, STEP count and BOM read their components through AssemblyComponents",
                ReadersProblems,
                "each of the four goes through AssemblyComponents (Read, Describe or ComponentsOrRefusal) and calls neither " +
                "GetModelDoc2 nor IsSuppressed itself; ComponentsOrRefusal reads AssemblyComponents.Read",
                RegexChange(Measures, "the box on a reader of its own", @"ComponentsOrRefusal\((\w+), ""box"", out (\w+)\)", "OldPieces($1, out $2)"),
                Change(Measures, "the mass reading the document itself", "var doc = item.Document;", "var doc = c.GetModelDoc2() as IModelDoc2;"),
                Change(Bom, "the BOM reading the document itself", "IModelDoc2 doc = item.Document;", "IModelDoc2 doc = c.GetModelDoc2() as IModelDoc2;"),
                RegexChange(Measures, "the STEP count on a list of its own", @"AssemblyComponents\.Read\((\w+)\);(\s*if \(all == null\) return -1;)", "OldList($1);$2"),
                Change(Measures, "the refusal helper not on the shared list", "AssemblyComponents.Read(", "OldRead("));

            Checked(sources, "mass",
                "the assembly mass leaves envelopes out, weighs hidden components, refuses lightweight parts unless asked",
                MassProblems,
                "the weighed selection is resolved parts without envelopes, hidden ones kept; envelopes and hidden named; a " +
                "Bool argument, false by default and declared so in the catalogue, resolves lightweight parts " +
                "(ResolveAllLightWeightComponents(false)), reads the list again and says so",
                Change(Measures, "envelopes weighed by the sum", "ComponentLoad.Resolved && !c.Envelope)", "ComponentLoad.Resolved)"),
                Change(Measures, "hidden components left out of the sum", "ComponentLoad.Resolved && !c.Envelope)",
                       "ComponentLoad.Resolved && !c.Envelope && !c.Hidden)"),
                RegexChange(Measures, "lightweight parts resolved without being asked", @"(if\s*\(\s*!\s*Bool\([^)]*,\s*)false(\s*\)\s*\))", "${1}true$2"),
                // A regex, not a text: the member's own comment names the call first.
                RegexChange(Measures, "no resolution when asked", @"(\w+)\.ResolveAllLightWeightComponents\(false\);", "$1.GetLightWeightComponentCount();"),
                RegexChange(Measures, "the list not read again after resolving", @"(ResolveAllLightWeightComponents\(false\);[\s\S]*?)ComponentsOrRefusal\(", "${1}OldList("),
                Change(Measures, "envelopes not named", "\"envelope_components\"", "\"envelopes\""),
                RegexChange(Measures, "a refusal after resolving that does not say it", @"return RefuseAfterResolving\(resolvedOnRequest,", "return Fallito("),
                Change(Measures, "the refusal helper that drops what was resolved", "message + \" \" + resolvedOnRequest", "message"),
                Change(Catalogue, "the argument not false by default in the catalogue", "\"default\", false", "\"default\", true"));

            Checked(sources, "box",
                "the assembly box measures lightweight parts through GetBodies3 and names lightweight, envelope and hidden ones",
                BoxProblems,
                "the measured selection is every part that is not suppressed (lightweight, envelopes and hidden in); " +
                "GetBodies3 inside its own try, a throw or a lightweight part without solid bodies refuses; " +
                "lightweight_components_measured, envelope_components and hidden_components answered",
                Change(Measures, "lightweight parts dropped again", "c.IsPart && c.Load != ComponentLoad.Suppressed)",
                       "c.IsPart && c.Load == ComponentLoad.Resolved)"),
                Change(Measures, "envelopes dropped from the box", "c.IsPart && c.Load != ComponentLoad.Suppressed)",
                       "c.IsPart && c.Load != ComponentLoad.Suppressed && !c.Envelope)"),
                Change(Measures, "lightweight parts not named", "\"lightweight_components_measured\"", "\"measured\""),
                Change(Measures, "a throw of GetBodies3 swallowed", "catch { bodiesUnread = true; }", "catch { }"),
                RegexChange(Measures, "a lightweight part without bodies skipped",
                            @"if \(item\.Load == ComponentLoad\.Lightweight\)\s*return Fallito\(", "if (false) return Fallito("));

            Checked(sources, "STEP count",
                "the STEP count leaves hidden components out and names them, and counts lightweight parts",
                StepCountProblems,
                "a hidden component is added to the list of the left out and skipped; only suppressed components are skipped " +
                "by their load state, and an unreadable state gives -1",
                Change(Measures, "hidden components counted again", "hiddenLeftOut.Add(item.Name); continue; }", "hiddenLeftOut.Add(item.Name); }"),
                Change(Measures, "lightweight parts left out", "item.Load == ComponentLoad.Suppressed) continue;",
                       "item.Load != ComponentLoad.Resolved) continue;"),
                Change(Measures, "an unreadable state counted", "if (item.Load == ComponentLoad.Unknown) return -1;", ""));

            Checked(sources, "STEP answer",
                "the STEP of an assembly names the hidden components and says the lightweight ones it resolved",
                StepAnswerProblems,
                "hidden_components_not_exported from the list the count fills; GetLightWeightComponentCount read before SaveAs3 " +
                "and after it, lightweight_resolved_by_the_export when it went down",
                Change(Export, "the count before the export not read", "lightweightBeforeExport = LightweightCount(", "lightweightBeforeExport = ("),
                Change(Export, "hidden components not named", "\"hidden_components_not_exported\"", "\"hidden\""),
                RegexChange(Export, "the list of the left out not passed", Regex.Escape(StepCountMember) + @"\((\w+), (\w+)\)", StepCountMember + "($1)"));

            Checked(sources, "BOM reader",
                "the BOM reads suppressed by GetSuppression2, sub-assemblies by extension, and refuses a lightweight sub-assembly",
                BomProblems,
                "suppressed = Load == Suppressed; a sub-assembly is IsSubAssembly (no «is IAssemblyDoc»); a sub-assembly without " +
                "its document and an unreadable state refuse with a reason, which reaches the caller at every call; counted_as answered",
                Change(Bom, "a lightweight sub-assembly counted as a part", "bool assembly = item.IsSubAssembly;", "bool assembly = doc is IAssemblyDoc;"),
                Change(Bom, "a lightweight component taken for suppressed", "= item.Load == ComponentLoad.Suppressed;", "= item.Load != ComponentLoad.Resolved;"),
                RegexChange(Bom, "a lightweight sub-assembly read as Show", @"(if\s*\(\s*doc\s*==\s*null)(\s*\)\s*\{\s*readProblem)", "$1 && false$2"),
                Change(Bom, "the reason lost on the way", "if (asIs == null && readProblem != null) problem = readProblem + \" No table was inserted.\";", ""),
                Change(Bom, "the answer does not say how it counted", "[\"counted_as\"]", "[\"counted\"]"));
        }

        // ── the checks, on source text (file name -> text) ───────────────────

        /// <summary>No «.IsSuppressed()» in the code of any library file (comments and strings blanked).</summary>
        internal static List<string> NoIsSuppressed(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var call = new Regex(@"\.\s*IsSuppressed\s*\(\s*\)");
            int stateReads = 0;
            foreach (var file in sources.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var state = new StripState();
                int number = 0;
                foreach (var raw in sources[file].Split('\n'))
                {
                    number++;
                    string comment;
                    var code = ObsoleteCallScanner.Strip(raw, state, out comment);
                    if (call.IsMatch(code)) problems.Add(file + ":" + number + " " + raw.Trim());
                    if (code.Contains(".GetSuppression2(")) stateReads++;
                }
            }
            if (stateReads == 0) problems.Add("no GetSuppression2 call in the library: the component state is read nowhere");
            return problems;
        }

        /// <summary>The old reader named nowhere in the code of the library.</summary>
        internal static List<string> OldReaderGone(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var name = new Regex(@"\b" + Regex.Escape(OldReader) + @"\b");
            foreach (var file in sources.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var state = new StripState();
                int number = 0;
                foreach (var raw in sources[file].Split('\n'))
                {
                    number++;
                    string comment;
                    if (name.IsMatch(ObsoleteCallScanner.Strip(raw, state, out comment)))
                        problems.Add(file + ":" + number + " " + raw.Trim());
                }
            }
            return problems;
        }

        internal static List<string> SharedReaderProblems(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var code = CodeOnly(sources[Shared]);
            var describe = MemberBody(code, "Describe");
            if (describe == null) { problems.Add("AssemblyComponents.Describe is not in " + Shared); return problems; }
            if (!describe.Contains(".GetSuppression2()")) problems.Add("Describe does not read GetSuppression2");
            if (!code.Contains(".IsHidden(false)")) problems.Add("hidden is not read with IsHidden(false)");
            if (code.Contains(".IsHidden(true)"))
                problems.Add("IsHidden(true) is read: it answers true on a lightweight component (measured)");
            int documents = Count(code, ".GetModelDoc2(");
            if (documents != 1) problems.Add(documents + " GetModelDoc2 calls in " + Shared + ", expected 1 (in Describe)");
            int at = describe.IndexOf(".GetModelDoc2(", StringComparison.Ordinal);
            if (at < 0) problems.Add("Describe does not read the document");
            else
            {
                var guard = Regex.Matches(describe.Substring(0, at), @"\bif\s*\(([^()]*)\)").Cast<Match>().LastOrDefault();
                if (guard == null || !Regex.IsMatch(guard.Groups[1].Value, @"\.Load\s*==\s*ComponentLoad\.Resolved\b"))
                    problems.Add("GetModelDoc2 is not read only for a resolved component (the last if before it: " +
                                 (guard == null ? "none" : guard.Value) + ")");
            }
            if (!Regex.IsMatch(describe, @"\.IsPart\s*=\s*IsPartFile\(\s*\w+\.Path\s*\)\s*;"))
                problems.Add("IsPart does not come from IsPartFile of the saved path");
            if (!Regex.IsMatch(describe, @"\.IsSubAssembly\s*=\s*IsAssemblyFile\(\s*\w+\.Path\s*\)\s*;"))
                problems.Add("IsSubAssembly does not come from IsAssemblyFile of the saved path");
            return problems;
        }

        internal static List<string> KindProblems(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var shared = CodeOnly(sources[Shared]);
            var describe = MemberBody(shared, "Describe");
            if (describe == null) problems.Add("AssemblyComponents.Describe is not in " + Shared);
            else
            {
                if (!Regex.IsMatch(describe, @"if\s*\(\s*!\s*\w+\.IsPart\s*&&\s*!\s*\w+\.IsSubAssembly\s*&&\s*\w+\.Document\s*!=\s*null\s*\)"))
                    problems.Add("Describe does not look at the document when the extension says neither part nor sub-assembly");
                if (!Regex.IsMatch(describe, @"\.IsPart\s*=\s*\w+\.Document\s+is\s+IPartDoc\s*;"))
                    problems.Add("Describe does not take a part from its document");
                if (!Regex.IsMatch(describe, @"\.IsSubAssembly\s*=\s*\w+\.Document\s+is\s+IAssemblyDoc\s*;"))
                    problems.Add("Describe does not take a sub-assembly from its document");
            }
            var unknown = MemberBody(shared, "KindUnknown");
            if (unknown == null || !Regex.IsMatch(unknown, @"!\s*\w+\.IsPart\s*&&\s*!\s*\w+\.IsSubAssembly"))
                problems.Add("AssemblyComponents.KindUnknown does not say «neither a part nor a sub-assembly»");
            var measures = CodeOnly(sources[Measures]);
            var helper = MemberBody(measures, "ComponentsOrRefusal");
            if (helper == null || !Regex.IsMatch(helper, @"\.Where\(\s*AssemblyComponents\.KindUnknown\s*\)[\s\S]*?return\s+null\s*;"))
                problems.Add("ComponentsOrRefusal does not refuse on a component of unknown kind");
            var count = MemberBody(measures, StepCountMember);
            if (count == null || !Regex.IsMatch(count, @"if\s*\(\s*!\s*\w+\.IsPart\s*\)\s*\{\s*if\s*\(\s*\w+\.IsSubAssembly\s*\)\s*continue\s*;\s*return\s+-1\s*;\s*\}"))
                problems.Add(StepCountMember + " does not give -1 on a component that is neither a part nor a sub-assembly");
            return problems;
        }

        internal static List<string> ReadersProblems(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var measures = CodeOnly(sources[Measures]);
            var bom = CodeOnly(sources[Bom]);
            var readers = new[]
            {
                Tuple.Create(MassMember, measures), Tuple.Create(BoxMember, measures),
                Tuple.Create(StepCountMember, measures), Tuple.Create(BomMember, bom),
            };
            foreach (var r in readers)
            {
                var body = MemberBody(r.Item2, r.Item1);
                if (body == null) { problems.Add(r.Item1 + " is not in the source"); continue; }
                if (!body.Contains("AssemblyComponents.Read(") && !body.Contains("AssemblyComponents.Describe(") &&
                    !body.Contains("ComponentsOrRefusal("))
                    problems.Add(r.Item1 + " does not read its components through AssemblyComponents");
                if (body.Contains(".GetModelDoc2(")) problems.Add(r.Item1 + " calls GetModelDoc2 itself (null on a lightweight component)");
                if (body.Contains(".IsSuppressed(")) problems.Add(r.Item1 + " calls IsSuppressed itself (true on a lightweight component)");
            }
            var helper = MemberBody(measures, "ComponentsOrRefusal");
            if (helper == null) problems.Add("ComponentsOrRefusal is not in " + Measures);
            else if (!helper.Contains("AssemblyComponents.Read(")) problems.Add("ComponentsOrRefusal does not read AssemblyComponents.Read");
            return problems;
        }

        internal static List<string> MassProblems(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var body = MemberBody(CodeOnly(sources[Measures]), MassMember);
            if (body == null) { problems.Add(MassMember + " is not in " + Measures); return problems; }

            string selection = LoopSelection(body);
            if (selection == null) problems.Add("the selection the mass loop walks is not found");
            else
            {
                if (!Regex.IsMatch(selection, @"!\s*\w+\.Envelope\b"))
                    problems.Add("the weighed selection keeps envelopes (SOLIDWORKS leaves them out of the assembly mass): " + OneLine(selection));
                if (!selection.Contains("ComponentLoad.Resolved"))
                    problems.Add("the weighed selection is not the resolved parts (the sum reads each part's document): " + OneLine(selection));
                if (selection.Contains(".Hidden"))
                    problems.Add("the weighed selection leaves hidden components out (SOLIDWORKS weighs them): " + OneLine(selection));
            }
            foreach (var key in new[] { "\"envelope_components\"", "\"hidden_components_weighed\"", "\"lightweight_resolved_on_request\"" })
                if (!body.Contains(key)) problems.Add("the answer has no " + key);

            var gate = Regex.Match(body, @"if\s*\(\s*!\s*Bool\(\s*\w+\s*,\s*""(\w+)""\s*,\s*false\s*\)\s*\)\s*return\s+\w+\(");
            if (!body.Contains("ComponentLoad.Lightweight")) problems.Add("the mass does not look for lightweight parts");
            if (!gate.Success) problems.Add("no refusal on lightweight parts behind a Bool argument that is false by default");
            int resolve = body.IndexOf(".ResolveAllLightWeightComponents(false)", StringComparison.Ordinal);
            if (resolve < 0) problems.Add("the lightweight parts are not resolved when asked (ResolveAllLightWeightComponents(false))");
            else if (gate.Success && resolve < gate.Index) problems.Add("the resolution comes before the refusal");
            else if (body.IndexOf("ComponentsOrRefusal(", resolve, StringComparison.Ordinal) < 0)
                problems.Add("the list is not read again after the resolution");
            // Review of Z-C21.11: a refusal after the resolution says it too (the
            // parts stay resolved), directly or through RefuseAfterResolving.
            if (resolve >= 0)
            {
                foreach (Match refusal in Regex.Matches(body.Substring(resolve), @"return\s+Fallito\(([\s\S]*?)\);"))
                    if (refusal.Groups[1].Value.IndexOf("resolv", StringComparison.OrdinalIgnoreCase) < 0)
                        problems.Add("a refusal after the resolution does not say the parts were resolved: " + OneLine(refusal.Value));
                var helper = MemberBody(CodeOnly(sources[Measures]), "RefuseAfterResolving");
                if (helper == null || !Regex.IsMatch(helper, @"\+\s*resolvedOnRequest\b"))
                    problems.Add("RefuseAfterResolving does not add what was resolved to the refusal");
            }

            if (gate.Success)
            {
                string argument = gate.Groups[1].Value;
                var catalogue = sources[Catalogue];
                int entry = catalogue.IndexOf("\"" + MassOperation + "\"", StringComparison.Ordinal);
                int next = entry < 0 ? -1 : catalogue.IndexOf("new OperazioneDisegno", entry, StringComparison.Ordinal);
                string block = entry < 0 ? null : (next < 0 ? catalogue.Substring(entry) : catalogue.Substring(entry, next - entry));
                int property = block == null ? -1 : block.IndexOf("\"" + argument + "\", O(", StringComparison.Ordinal);
                int description = property < 0 ? -1 : block.IndexOf("\"description\"", property, StringComparison.Ordinal);
                string declared = description < 0 ? null : block.Substring(property, description - property);
                if (block == null) problems.Add("the catalogue has no entry " + MassOperation);
                else if (declared == null) problems.Add("the catalogue entry " + MassOperation + " does not declare the argument the code reads");
                else if (!declared.Contains("\"type\", \"boolean\"") || !declared.Contains("\"default\", false"))
                    problems.Add("the catalogue does not declare the argument a boolean, false by default: " + OneLine(declared));
            }
            return problems;
        }

        internal static List<string> BoxProblems(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var body = MemberBody(CodeOnly(sources[Measures]), BoxMember);
            if (body == null) { problems.Add(BoxMember + " is not in " + Measures); return problems; }
            string selection = LoopSelection(body);
            if (selection == null) problems.Add("the selection the box loop walks is not found");
            else
            {
                if (!selection.Contains("ComponentLoad.Suppressed"))
                    problems.Add("the measured selection does not leave the suppressed out by their load state: " + OneLine(selection));
                if (selection.Contains("ComponentLoad.Resolved") || selection.Contains("ComponentLoad.Lightweight"))
                    problems.Add("the measured selection filters on resolved or lightweight (GetBodies3 reads a lightweight part): " + OneLine(selection));
                if (selection.Contains(".Envelope") || selection.Contains(".Hidden"))
                    problems.Add("the measured selection leaves envelopes or hidden components out (they are in the box): " + OneLine(selection));
            }
            if (!Regex.IsMatch(body, @"try\s*\{[^{}]*\.GetBodies3\("))
                problems.Add("GetBodies3 is not inside its own try");
            // N5 again: a part dropped in silence is a partial box answered as a success.
            var bodiesTry = Regex.Match(body, @"try\s*\{[^{}]*\.GetBodies3\([^{}]*\}\s*catch\s*(?:\([^)]*\))?\s*\{\s*(\w+)\s*=\s*true\s*;\s*\}");
            if (!bodiesTry.Success)
                problems.Add("a throw of GetBodies3 is swallowed (its catch does not set a flag)");
            else if (!Regex.IsMatch(body, @"if\s*\(\s*" + bodiesTry.Groups[1].Value + @"\s*\)\s*return\s+Fallito\("))
                problems.Add("a throw of GetBodies3 does not refuse the box");
            if (!Regex.IsMatch(body, @"if\s*\(\s*\w+\.Load\s*==\s*ComponentLoad\.Lightweight\s*\)\s*return\s+Fallito\("))
                problems.Add("a lightweight part that answers no solid body is skipped instead of refusing");
            foreach (var key in new[] { "\"lightweight_components_measured\"", "\"envelope_components\"", "\"hidden_components\"" })
                if (!body.Contains(key)) problems.Add("the answer has no " + key);
            return problems;
        }

        internal static List<string> StepCountProblems(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var body = MemberBody(CodeOnly(sources[Measures]), StepCountMember);
            if (body == null) { problems.Add(StepCountMember + " is not in " + Measures); return problems; }
            if (!Regex.IsMatch(body, @"if\s*\(\s*\w+\.Hidden\s*\)\s*\{[^{}]*\.Add\(\s*\w+\.Name\s*\)\s*;[^{}]*\bcontinue\s*;\s*\}"))
                problems.Add("a hidden component is not both named and skipped (the STEP does not write it, measured)");
            if (!Regex.IsMatch(body, @"\w+\.Load\s*==\s*ComponentLoad\.Suppressed\s*\)\s*continue\s*;"))
                problems.Add("the suppressed components are not skipped by their load state");
            if (body.Contains("ComponentLoad.Resolved") || body.Contains("ComponentLoad.Lightweight"))
                problems.Add("the count filters on resolved or lightweight (the export writes lightweight parts, measured)");
            if (!Regex.IsMatch(body, @"\w+\.Load\s*==\s*ComponentLoad\.Unknown\s*\)\s*return\s+-1\s*;"))
                problems.Add("an unreadable load state does not give -1");
            return problems;
        }

        internal static List<string> StepAnswerProblems(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var code = CodeOnly(sources[Export]);
            foreach (var key in new[] { "\"hidden_components_not_exported\"", "\"lightweight_resolved_by_the_export\"" })
                if (!code.Contains(key)) problems.Add("the answer has no " + key);
            if (!Regex.IsMatch(code, Regex.Escape(StepCountMember) + @"\(\s*\w+\s*,\s*\w+\s*\)"))
                problems.Add(StepCountMember + " is not called with the list of the components it leaves out");
            int before = code.IndexOf("LightweightCount(", StringComparison.Ordinal);
            int save = before < 0 ? -1 : code.IndexOf(".SaveAs3(", before, StringComparison.Ordinal);
            int after = save < 0 ? -1 : code.IndexOf("LightweightCount(", save, StringComparison.Ordinal);
            if (before < 0 || save < 0 || after < 0)
                problems.Add("the lightweight count is not read both before SaveAs3 and after it (before " + (before >= 0) +
                             ", SaveAs3 after it " + (save >= 0) + ", read again after the save " + (after >= 0) + ")");
            return problems;
        }

        internal static List<string> BomProblems(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var code = CodeOnly(sources[Bom]);
            var body = MemberBody(code, BomMember);
            if (body == null) { problems.Add(BomMember + " is not in " + Bom); return problems; }
            if (!Regex.IsMatch(body, @"=\s*\w+\.Load\s*==\s*ComponentLoad\.Suppressed\s*;"))
                problems.Add("suppressed is not read as the load state Suppressed");
            if (!Regex.IsMatch(body, @"\bbool\s+\w+\s*=\s*\w+\.IsSubAssembly\s*;"))
                problems.Add("a sub-assembly is not recognised by IsSubAssembly (its file extension)");
            if (body.Contains("is IAssemblyDoc"))
                problems.Add("a sub-assembly is still recognised by its document (null on a lightweight component)");
            if (!Regex.IsMatch(body, @"if\s*\(\s*\w+\s*==\s*null\s*\)\s*\{\s*\w+\s*=[^;]*lightweight[^;]*;\s*return\s+null\s*;\s*\}"))
                problems.Add("a sub-assembly without its document (lightweight) does not refuse with a reason");
            if (!Regex.IsMatch(body, @"ComponentLoad\.Unknown\s*\)\s*\{\s*\w+\s*=[^;]*;\s*return\s+null\s*;"))
                problems.Add("an unreadable load state does not refuse with a reason");

            var calls = Regex.Matches(code, Regex.Escape(BomMember) + @"\([^;()]*\bout\s+\w+\s*\)\s*;").Count;
            var passed = Regex.Matches(code, Regex.Escape(BomMember) + @"\([^;()]*\bout\s+(\w+)\s*\)\s*;\s*if\s*\([^\n]*\)\s*\w+\s*=\s*\1\b").Count;
            if (calls == 0) problems.Add("no call of " + BomMember + " takes the reason out");
            else if (passed != calls) problems.Add(passed + " of " + calls + " calls of " + BomMember + " pass the reason on to the caller's problem");
            if (!code.Contains("[\"counted_as\"]")) problems.Add("the answer has no counted_as");
            return problems;
        }

        /// <summary>The declaration of the list the first foreach of the member walks, or null.</summary>
        private static string LoopSelection(string body)
        {
            var loop = Regex.Match(body, @"foreach\s*\(\s*(?:var|AssemblyComponent)\s+\w+\s+in\s+(\w+)\s*\)");
            if (!loop.Success) return null;
            var declaration = Regex.Match(body, @"\bvar\s+" + Regex.Escape(loop.Groups[1].Value) + @"\s*=\s*([^;]*);");
            return declaration.Success ? declaration.Groups[1].Value : null;
        }

        // ── seen red ─────────────────────────────────────────────────────────

        private sealed class SourceChange
        {
            public string File, Name, From, To;
            public bool IsRegex;
        }

        private static SourceChange Change(string file, string name, string from, string to)
        {
            return new SourceChange { File = file, Name = name, From = from, To = to };
        }

        private static SourceChange RegexChange(string file, string name, string pattern, string replacement)
        {
            return new SourceChange { File = file, Name = name, From = pattern, To = replacement, IsRegex = true };
        }

        /// <summary>
        /// One source check and its seen-red companion: the check on the real
        /// source, then each change applied alone to a copy, each of which must
        /// turn it red.
        /// </summary>
        private static void Checked(Dictionary<string, string> sources, string tag, string label,
                                    Func<IDictionary<string, string>, List<string>> check, string green,
                                    params SourceChange[] changes)
        {
            List<string> problems;
            try { problems = check(sources); }
            catch (Exception ex) { problems = new List<string> { ex.GetType().Name + ": " + ex.Message }; }
            Segna(label, problems.Count == 0, problems.Count == 0 ? green : string.Join(" | ", problems));

            var misses = new List<string>();
            if (problems.Count != 0) misses.Add("the untouched source is already red, the changes prove nothing");
            else
            {
                foreach (var c in changes)
                {
                    string text;
                    if (!sources.TryGetValue(c.File, out text)) { misses.Add(c.Name + ": " + c.File + " not read"); continue; }
                    string changed = null;
                    if (c.IsRegex)
                    {
                        var m = Regex.Match(text, c.From);
                        if (m.Success) changed = text.Substring(0, m.Index) + m.Result(c.To) + text.Substring(m.Index + m.Length);
                    }
                    else
                    {
                        int at = text.IndexOf(c.From, StringComparison.Ordinal);
                        if (at >= 0) changed = text.Substring(0, at) + c.To + text.Substring(at + c.From.Length);
                    }
                    if (changed == null) { misses.Add(c.Name + ": anchor not found in " + c.File + " (update the change)"); continue; }
                    var copy = new Dictionary<string, string>(sources, StringComparer.OrdinalIgnoreCase) { [c.File] = changed };
                    List<string> red;
                    try { red = check(copy); }
                    catch (Exception ex) { red = new List<string> { ex.GetType().Name }; }
                    if (red.Count == 0) misses.Add(c.Name + ": the changed copy is still green");
                }
            }
            Segna("seen red: " + tag + ", on each changed copy", misses.Count == 0,
                  misses.Count == 0 ? changes.Length + " change(s), each one red: " + string.Join("; ", changes.Select(c => c.Name))
                                    : string.Join(" | ", misses));
        }

        // ── source helpers ───────────────────────────────────────────────────

        /// <summary>Every .cs file of the library (bin and obj left out), by path relative to it, with \n line ends.</summary>
        private static Dictionary<string, string> ReadLibrary()
        {
            var dir = Path.Combine(Radice(), Library);
            var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var rel = f.Substring(dir.Length).TrimStart('\\', '/').Replace('\\', '/');
                    if (rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)) continue;
                    sources[rel] = File.ReadAllText(f).Replace("\r\n", "\n");
                }
            }
            catch (Exception ex)
            {
                Segna("the library sources can be read", false, ex.GetType().Name + ": " + ex.Message);
                return null;
            }
            var missing = new[] { Shared, Measures, Export, Bom, Catalogue }.Where(n => !sources.ContainsKey(n)).ToList();
            if (missing.Count > 0)
            {
                Segna("the library sources can be read", false, "missing in " + dir + ": " + string.Join(", ", missing));
                return null;
            }
            return sources;
        }

        /// <summary>
        /// The source without its comment lines and trailing line comments, so
        /// that a call named in a comment is not counted. String literals are
        /// kept: the answer keys are looked for in them.
        /// </summary>
        private static string CodeOnly(string source)
        {
            var lines = source.Split('\n');
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
        /// The text of the member declared with <paramref name="name"/> (a line at
        /// class-member indentation, the name before the first parenthesis), up
        /// to its closing brace at that indentation; null when it is not there.
        /// </summary>
        private static string MemberBody(string code, string name)
        {
            var header = Regex.Match(code, @"\n {8}(?:private|internal|public|protected)\b[^\n(;=]*\b" + Regex.Escape(name) + @"\s*\(");
            if (!header.Success) return null;
            int end = code.IndexOf("\n        }", header.Index + 1, StringComparison.Ordinal);
            return end < 0 ? code.Substring(header.Index) : code.Substring(header.Index, end - header.Index);
        }

        private static int Count(string text, string what)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; }
            return n;
        }

        private static string OneLine(string s)
        {
            var flat = string.Join(" ", s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
            return flat.Length <= 200 ? flat : flat.Substring(0, 197) + "...";
        }

        // ══ THE PURE RULES, ON THE BUILT LIBRARY ═══════════════════════════

        private static void PureRules()
        {
            var library = Path.Combine(Radice(), Library, "bin", "Release", "Riscontro.Disegno.dll");
            if (!File.Exists(library))
            {
                NonQui("the pure rules of Z-C21.11 (load state, part or sub-assembly by extension, indented BOM rows)",
                       library + " is missing: build the library first (build.ps1)");
                return;
            }

            Type components, sw;
            try
            {
                var redist = DoveStaLInterop();
                if (redist != null)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += (s, a) =>
                    {
                        var name = new AssemblyName(a.Name).Name;
                        if (!name.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase)) return null;
                        var file = Path.Combine(redist, name + ".dll");
                        return File.Exists(file) ? Assembly.LoadFrom(file) : null;
                    };
                }
                // From the bytes, as ApiCatalogContracts: the repository may sit
                // on a network drive, where LoadFrom is refused.
                var asm = Assembly.Load(File.ReadAllBytes(library));
                components = asm.GetType("Riscontro.Disegno.AssemblyComponents", false);
                sw = asm.GetType("Riscontro.Disegno.OperazioniSw", false);
            }
            catch (Exception ex) { Segna("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }

            LoadStates(components);
            FileKinds(components);
            IndentedRows(sw);
        }

        private static object Call(Type t, string method, params object[] args)
        {
            if (t == null) throw new MissingMethodException("the type is not in this library (0.146.0 or older)");
            var m = ReflectionLog.Global.Method(t, method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method + " is not in this library");
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        // ── the load state, from GetSuppression2 ─────────────────────────────
        //
        // swComponentSuppressionState_e: Suppressed 0, Lightweight 1,
        // FullyResolved 2, Resolved 3, FullyLightweight 4, InternalIdMismatch 5.
        // The probe read 1 on every lightweight component and 2 on the resolved
        // ones; -1 stands for an answer outside the enumeration.
        private static void LoadStates(Type components)
        {
            const string label = "the load state from GetSuppression2: 0 suppressed, 1 and 4 lightweight, 2 and 3 resolved, anything else unknown";
            try
            {
                var expected = new[]
                {
                    Tuple.Create(0, "Suppressed"), Tuple.Create(1, "Lightweight"), Tuple.Create(2, "Resolved"),
                    Tuple.Create(3, "Resolved"), Tuple.Create(4, "Lightweight"), Tuple.Create(5, "Unknown"),
                    Tuple.Create(-1, "Unknown"),
                };
                var wrong = new List<string>();
                foreach (var e in expected)
                {
                    var got = Convert.ToString(Call(components, "LoadFromSuppression2", e.Item1));
                    if (got != e.Item2) wrong.Add(e.Item1 + ": " + got + " instead of " + e.Item2);
                }
                Segna(label, wrong.Count == 0, wrong.Count == 0 ? expected.Length + " states as expected" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Segna(label, false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── part or sub-assembly, by the file extension ──────────────────────
        //
        // GetModelDoc2 is null on a lightweight component (measured), so the
        // kind comes from GetPathName, the saved path.
        private static void FileKinds(Type components)
        {
            const string label = "part or sub-assembly by the extension of the saved path, whatever its case";
            try
            {
                var expected = new[]
                {
                    Tuple.Create(@"C:\w\A.SLDPRT", true, false), Tuple.Create(@"C:\w\A.sldprt", true, false),
                    Tuple.Create(@"C:\w\S.sldasm", false, true), Tuple.Create(@"C:\w\S.SLDASM", false, true),
                    Tuple.Create(@"C:\w.sldasm\A.SLDPRT", true, false),
                };
                var wrong = Kinds(components, expected);
                Segna(label, wrong.Count == 0, wrong.Count == 0 ? expected.Length + " paths as expected" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Segna(label, false, ex.GetType().Name + ": " + ex.Message); }

            const string odd = "an empty, missing or other path is neither, and an illegal character does not throw";
            try
            {
                var expected = new[]
                {
                    Tuple.Create("", false, false), Tuple.Create((string)null, false, false),
                    Tuple.Create(@"C:\w\D.SLDDRW", false, false), Tuple.Create(@"C:\w\A", false, false),
                    Tuple.Create(@"C:\w\A.SLDPRT.bak", false, false),
                };
                var wrong = Kinds(components, expected);
                // An illegal path character makes Path.GetExtension throw on .NET
                // Framework: the reader must answer, not throw. Which answer is not
                // pinned (no component path can hold it).
                string illegal = @"C:\w\A|B.SLDPRT";
                try { Call(components, "IsPartFile", illegal); Call(components, "IsAssemblyFile", illegal); }
                catch (Exception ex) { wrong.Add(illegal + " threw " + ex.GetType().Name); }
                Segna(odd, wrong.Count == 0,
                      wrong.Count == 0 ? expected.Length + " paths neither part nor sub-assembly, the illegal one answered" : string.Join("; ", wrong));
            }
            catch (Exception ex) { Segna(odd, false, ex.GetType().Name + ": " + ex.Message); }
        }

        private static List<string> Kinds(Type components, Tuple<string, bool, bool>[] expected)
        {
            var wrong = new List<string>();
            foreach (var e in expected)
            {
                bool part = (bool)Call(components, "IsPartFile", e.Item1);
                bool assembly = (bool)Call(components, "IsAssemblyFile", e.Item1);
                if (part != e.Item2 || assembly != e.Item3)
                    wrong.Add((e.Item1 == null ? "null" : "«" + e.Item1 + "»") + ": part " + part + ", sub-assembly " + assembly +
                              " instead of " + e.Item2 + ", " + e.Item3);
            }
            return wrong;
        }

        // ── M23: the indented BOM rows ───────────────────────────────────────
        //
        // The probe's trees and the indented tables SOLIDWORKS wrote (Detailed
        // numbering; row: quantity, « > » between a row and the rows above it):
        //   T_flat  A + B + B                      A 1, B 2                      (2 rows, qty 3)
        //   T_ind   A + 3 x S_show(2 B) + B        A 1, S_show 3, S_show > B 2, B 1   (4, 7)
        //   T_hide  A + S_hide(2 B), Hide          A 1, S_hide 1                 (2, 2)
        //   T_prom  A + S_prom(2 B), Promote       A 1, B 2                      (2, 3)
        //   T_nest  A + 2 x N, N = S_show(2 B) + B A 1, N 2, N > S_show 1, N > S_show > B 2, N > B 1   (5, 7)
        // The old count (every part, all levels) refused T_ind, T_hide and T_nest.
        // The same day, T_ind parts only: A 1, B 7; top level only: A 1, S_show 3, B 1.
        private const int PartsOnly = 1, TopLevelOnly = 2, Indented = 3;
        private const int NotRead = -1, Hide = 1, Show = 2, Promote = 3;

        /// <summary>A component tree as the BOM reader builds it: one entry per component, children after their parents.</summary>
        private sealed class Tree
        {
            public readonly List<string> Keys = new List<string>();
            public readonly List<int> Parents = new List<int>();
            public readonly List<bool> IsAssembly = new List<bool>();
            public readonly List<int> Display = new List<int>();
            public readonly List<bool> Skipped = new List<bool>();

            public int Part(string key, int parent = -1) { return Add(key, parent, false, NotRead); }
            public int Sub(string key, int display, int parent = -1) { return Add(key, parent, true, display); }

            private int Add(string key, int parent, bool assembly, int display)
            {
                Keys.Add(key); Parents.Add(parent); IsAssembly.Add(assembly); Display.Add(display); Skipped.Add(false);
                return Keys.Count - 1;
            }
        }

        private static Tree Flat()
        {
            var t = new Tree();
            t.Part("A"); t.Part("B"); t.Part("B");
            return t;
        }

        /// <summary>T_ind; <paramref name="probeOrder"/>: the top level as the probe read it (B, S_show x3, A).</summary>
        private static Tree Ind(bool probeOrder)
        {
            var t = new Tree();
            var subs = new List<int>();
            if (probeOrder)
            {
                t.Part("B");
                for (int k = 0; k < 3; k++) subs.Add(t.Sub("S_show", Show));
                t.Part("A");
            }
            else
            {
                t.Part("A");
                for (int k = 0; k < 3; k++) subs.Add(t.Sub("S_show", Show));
                t.Part("B");
            }
            foreach (var s in subs) { t.Part("B", s); t.Part("B", s); }
            return t;
        }

        private static Tree OneSub(string name, int display)
        {
            var t = new Tree();
            t.Part("A");
            int s = t.Sub(name, display);
            t.Part("B", s); t.Part("B", s);
            return t;
        }

        /// <summary>T_nest; <paramref name="reversed"/>: every level listed the other way round.</summary>
        private static Tree Nest(bool reversed)
        {
            var t = new Tree();
            var ns = new List<int>();
            if (!reversed) { t.Part("A"); ns.Add(t.Sub("N", Show)); ns.Add(t.Sub("N", Show)); }
            else { ns.Add(t.Sub("N", Show)); ns.Add(t.Sub("N", Show)); t.Part("A"); }
            var shows = new List<int>();
            foreach (var n in ns)
            {
                if (!reversed) { shows.Add(t.Sub("S_show", Show, n)); t.Part("B", n); }
                else { t.Part("B", n); shows.Add(t.Sub("S_show", Show, n)); }
            }
            foreach (var s in shows) { t.Part("B", s); t.Part("B", s); }
            return t;
        }

        /// <summary>The expected items of a table of that type, or null.</summary>
        private static Dictionary<string, int> Items(Type sw, Tree t, int bomType)
        {
            return Call(sw, "ExpectedBomItems", t.Keys.ToArray(), t.Parents.ToArray(), t.IsAssembly.ToArray(),
                        t.Display.ToArray(), t.Skipped.ToArray(), bomType) as Dictionary<string, int>;
        }

        /// <summary>The items as «row qty», sorted by row, or «null».</summary>
        private static string Rows(Dictionary<string, int> items)
        {
            if (items == null) return "null";
            return string.Join(", ", items.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => k + " " + items[k]));
        }

        private static void IndentedRows(Type sw)
        {
            var lines = new List<string>();
            bool all = true;
            Action<string, Tree, int, string> check = (name, tree, bomType, expected) =>
            {
                string got;
                try { got = Rows(Items(sw, tree, bomType)); }
                catch (Exception ex) { got = ex.GetType().Name + ": " + ex.Message; }
                bool ok = got == expected;
                all &= ok;
                lines.Add(name + " " + (ok ? got : got + " instead of " + expected));
            };
            Action<string> close = label =>
            {
                Segna(label, all, string.Join("; ", lines));
                lines.Clear();
                all = true;
            };

            check("T_ind", Ind(false), Indented, "A 1, B 1, S_show 3, S_show > B 2");
            check("T_hide", OneSub("S_hide", Hide), Indented, "A 1, S_hide 1");
            check("T_nest", Nest(false), Indented, "A 1, N 2, N > B 1, N > S_show 1, N > S_show > B 2");
            close("M23: the indented rows of the three trees the old count refused, as SOLIDWORKS wrote them");

            check("T_flat", Flat(), Indented, "A 1, B 2");
            check("T_prom", OneSub("S_prom", Promote), Indented, "A 1, B 2");
            close("M23: and the two indented tables it read right keep their rows (no sub-assembly, a promoting one)");

            check("T_ind in the probe's order", Ind(true), Indented, "A 1, B 1, S_show 3, S_show > B 2");
            check("T_nest every level reversed", Nest(true), Indented, "A 1, N 2, N > B 1, N > S_show 1, N > S_show > B 2");
            close("M23: the same rows whichever order GetComponents and GetChildren answer");

            // What the caller compares: the table's item count (rows) and the sum
            // of its quantities. A child row counts ONE instance of its parent:
            // T_ind's B under S_show is 2, not 6, so the sum is 7 as in the table.
            var tables = new[]
            {
                Tuple.Create("T_flat", Flat(), 2, 3), Tuple.Create("T_ind", Ind(false), 4, 7),
                Tuple.Create("T_hide", OneSub("S_hide", Hide), 2, 2), Tuple.Create("T_prom", OneSub("S_prom", Promote), 2, 3),
                Tuple.Create("T_nest", Nest(false), 5, 7),
            };
            const string sums = "M23: rows and quantity sum are the table's, a child row counting one instance of its parent";
            try
            {
                var wrong = new List<string>();
                var read = new List<string>();
                foreach (var e in tables)
                {
                    var items = Items(sw, e.Item2, Indented);
                    int rows = items == null ? -1 : items.Count, quantity = items == null ? -1 : items.Values.Sum();
                    read.Add(e.Item1 + " " + rows + "/" + quantity);
                    if (rows != e.Item3 || quantity != e.Item4)
                        wrong.Add(e.Item1 + ": " + rows + " rows, quantity " + quantity + " instead of " + e.Item3 + ", " + e.Item4);
                }
                Segna(sums, wrong.Count == 0, wrong.Count == 0 ? "rows/quantity " + string.Join(", ", read) : string.Join("; ", wrong));
            }
            catch (Exception ex) { Segna(sums, false, ex.GetType().Name + ": " + ex.Message); }

            check("T_ind parts only", Ind(false), PartsOnly, "A 1, B 7");
            check("T_ind top level only", Ind(false), TopLevelOnly, "A 1, B 1, S_show 3");
            close("N27: the parts-only and top-level tables of T_ind, measured the same day, read as before");
        }
    }
}
