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
    /// THE SKETCH JUST DRAWN IS THE ONE BORN SINCE ITS CALLER OPENED IT
    /// (Z-C21.5, scheme S7: the two sketch lookups library 0.146.0 left).
    ///
    /// Until library 0.146.0 eight operations of OperazioniSw and incidi_testo
    /// selected «the sketch just made» as the LAST ProfileFeature of the tree
    /// (SchizzoAppenaCreato), `estrudi_profilo` and `rivoluzione` found the
    /// sketch to lock after the feature the same way (BloccaSchizzo), and
    /// sweep_circolare took its path as the last sketch of the tree, 2D or 3D
    /// (UltimoSchizzoFeature). Since then each caller takes the names of the
    /// tree (FeatureNamesNow) before it selects the plane or face and opens the
    /// sketch, and the sketch is the one of its type born since (FeatureBorn),
    /// as 0.146.0 already did for the planes, the axes and the loft.
    ///
    /// WHY NOT A BENCH CASE. To be red on 0.146.0 a case needs a part where the
    /// last sketch of the tree is not the one just drawn: a sketch that is not
    /// born (the old rule then took a sketch an earlier step had used), or an
    /// older sketch after the new one. No operation of the catalogue moves the
    /// rollback bar (the library has no EditRollback call), and on the plane or
    /// face a caller has just selected InsertSketch(true) opens a sketch
    /// (ISketchManager~InsertSketch.html: «Inserts a new sketch in the current
    /// part or assembly document»), so the bench cannot build that part.
    /// This group therefore pins the rule in the source, where it could go back
    /// without a sound, and checks the pure decision on the trees that tell the
    /// two rules apart:
    ///  * the source: SchizzoAppenaCreato answers FeatureBorn of a
    ///    ProfileFeature on its own names parameter and walks nothing itself;
    ///    UltimoSchizzoFeature is gone; in the three sources a sketch type is
    ///    written only as the type of a FeatureBorn call; every call of
    ///    SelezionaSchizzoAppenaCreato, BloccaSchizzo and SchizzoAppenaCreato in
    ///    the library, and every FeatureBorn of a sketch, gets names assigned
    ///    from FeatureNamesNow in the same member, after the previous sketch
    ///    was opened and before the one it looks for. Each rule turns red on a
    ///    changed copy of the real source;
    ///  * the built library, by reflection: SketchNotIdentified (none born, an
    ///    unreadable tree, two born) and BornFeatureIndex on sketch trees.
    ///
    /// On library 0.146.0 three of the four checks are red (run on a copy of
    /// that tree, built apart): the source check (the calls without names, the
    /// two walks, UltimoSchizzoFeature), its seen-red companion (the untouched
    /// source is already red) and SketchNotIdentified, which is not there. The
    /// sketch trees are green there by design: BornFeatureIndex came with
    /// 0.146.0, and the check guards the decision the callers now rely on.
    /// </summary>
    internal static class Zc215SketchIdentityContract
    {
        private const string Library = "Riscontro.Disegno";

        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        /// <summary>The sources whose sketch lookups this group pins.</summary>
        private static readonly string[] ThreeSources = { "OperazioniSw.cs", "OperazioniTesto.cs", "OperazioniPercorso.cs" };

        /// <summary>
        /// The library methods this group reads, written once in one prose
        /// string. For the language gate a string literal that is one bare
        /// name is a protocol element, and these are identifiers the library
        /// already has, not new names.
        /// </summary>
        private static readonly string[] LibraryNames =
            "SelezionaSchizzoAppenaCreato BloccaSchizzo SchizzoAppenaCreato UltimoSchizzoFeature".Split(' ');

        /// <summary>Selects the sketch just drawn (the eight operations and the engraving).</summary>
        private static readonly string SelectLookup = LibraryNames[0];

        /// <summary>Finds the sketch to lock after the feature.</summary>
        private static readonly string LockLookup = LibraryNames[1];

        /// <summary>The lookup itself, which the two above call.</summary>
        private static readonly string SketchLookup = LibraryNames[2];

        /// <summary>The sweep's last-sketch lookup, gone since this lot.</summary>
        private static readonly string OldSweepLookup = LibraryNames[3];

        private static readonly Regex LookupCallRx = new Regex(
            @"(?<![\w.])(?<name>" + SelectLookup + "|" + LockLookup + "|" + SketchLookup +
            @")\(\s*(?<model>[^,()]+?)\s*,\s*(?<names>[^,()]+?)\s*,",
            RegexOptions.Compiled);

        private static readonly Regex LookupNameRx = new Regex(
            @"(?<![\w.])(?:" + SelectLookup + "|" + LockLookup + "|" + SketchLookup + @")\(", RegexOptions.Compiled);

        private static readonly Regex BornSketchRx = new Regex(
            @"(?<![\w.])FeatureBorn\(\s*(?<model>[^,()]+?)\s*,\s*(?<names>[^,()]+?)\s*,\s*""(?<type>(?:3D)?ProfileFeature)""",
            RegexOptions.Compiled);

        private static readonly Regex OpeningRx = new Regex(@"\.Insert(?:3D)?Sketch\(\s*true\s*\)", RegexOptions.Compiled);

        private static readonly Regex LastNewOfType = new Regex(
            @"GetTypeName2\s*\(\s*\)\s*==\s*[^&;)]+&&\s*!\s*\w+\s*\.\s*Contains\s*\(\s*\w+\s*\.\s*Name\s*\)\s*\)\s*\w+\s*=\s*\w+\s*;",
            RegexOptions.Compiled);

        /// <summary>Every «if (type matches and the name is new) keep it» loop body, as file:line.</summary>
        private static List<string> LastNewOfTypeLoops(Dictionary<string, string> sources)
        {
            var found = new List<string>();
            foreach (var kv in sources)
                foreach (Match m in LastNewOfType.Matches(kv.Value))
                    found.Add(kv.Key + ":" + (kv.Value.Substring(0, m.Index).Split('\n').Length));
            return found;
        }

        internal static void RunAll()
        {
            Gruppo("the sketch just drawn is the one born since its caller opened it (Z-C21.5 S7, sketch lookups)");

            // ── THE SOURCE ───────────────────────────────────────────────
            var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string root = Path.Combine(Radice(), Library);
                foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                    if (relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
                        relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)) continue;
                    sources[relative] = File.ReadAllText(file);
                }
            }
            catch (Exception ex)
            {
                Segna("the library sources can be read", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            var problems = SourceProblems(sources);
            Segna("S7: every sketch lookup gets the names its caller took before opening that sketch",
                  problems.Count == 0,
                  problems.Count == 0
                      ? "SchizzoAppenaCreato is FeatureBorn of a ProfileFeature; no UltimoSchizzoFeature; sketch types only as " +
                        "FeatureBorn types; " + Summary(sources)
                      : string.Join(" | ", problems));

            // Each change hits the first occurrence of its anchor in its file. The
            // anchors are the English tails of the lines, so that this file
            // carries no prose of the sources it quotes.
            var changes = new[]
            {
                Change("OperazioniSw.cs", "the last sketch of the tree again",
                       "namesBeforeSketch, \"ProfileFeature\", out born);",
                       "namesBeforeSketch, \"Extrusion\", out born) ?? LastOfType(((IPartDoc)model).IFirstFeature(), \"ProfileFeature\");"),
                Change("OperazioniPercorso.cs", "the sweep path falling back on the last sketch of the tree",
                       "pathNamesBefore, \"3DProfileFeature\", out pathsBorn);",
                       "pathNamesBefore, \"3DProfileFeature\", out pathsBorn) ?? UltimoSchizzoFeature(null);"),
                Change("OperazioniPercorso.cs", "the sweep path looked for as a 2D sketch",
                       "\"3DProfileFeature\", out pathsBorn);",
                       "\"ProfileFeature\", out pathsBorn);"),
                Change("OperazioniSw.cs", "a caller that passes no names",
                       "sketchNamesBefore, out string erroreSel",
                       "null, out string erroreSel"),
                Change("OperazioniSw.cs", "names taken again after the sketch was opened",
                       ".InsertSketch(true);",
                       ".InsertSketch(true); sketchNamesBefore = FeatureNamesNow(null);"),
                Change("OperazioniTesto.cs", "names that are not read from the tree",
                       "sketchNamesBefore = FeatureNamesNow(",
                       "sketchNamesBefore = new HashSet<string>("),
                Change("OperazioniSw.cs", "the sketch to lock found with no names",
                       "sketchNamesBefore, out notaBlocco",
                       "null, out notaBlocco"),
                Change("OperazioniPercorso.cs", "the second loft sketch with the names taken before the first",
                       "beforeSecondSketch, \"ProfileFeature\"",
                       "beforeFirstSketch, \"ProfileFeature\""),
            };
            var red = SeenRed(sources, changes);
            Segna("the sketch lookup check turns red on each changed copy", red.Count == 0,
                  red.Count == 0 ? changes.Length + " changes, each one red" : string.Join(" | ", red));

            // ── NO LOOP KEEPS THE LAST NEW FEATURE OF A TYPE ─────────────
            // Review of the Z-C21.5 leftovers: the sketched bend (OperazioniLamiera)
            // and the rib (OperazioniNervatura) walked the tree and kept the LAST
            // feature of a type whose name was new, so two born gave one of them
            // without a word. Both now go through FeatureBorn.
            var loops = LastNewOfTypeLoops(sources);
            Segna("no library loop keeps the last new feature of a type (the sketched bend, the rib)", loops.Count == 0,
                  loops.Count == 0 ? "none in " + sources.Count + " files" : string.Join(" | ", loops));
            var oldLoop = new Dictionary<string, string>(sources, StringComparer.OrdinalIgnoreCase);
            oldLoop["OperazioniLamiera.cs"] = (oldLoop.ContainsKey("OperazioniLamiera.cs") ? oldLoop["OperazioniLamiera.cs"] : "") +
                "\n// seeded: if (f.GetTypeName2() == \"ProfileFeature\" && !before.Contains(f.Name)) sketch = f;\n";
            Segna("that check turns red on a copy with the old loop of the sketched bend", LastNewOfTypeLoops(oldLoop).Count == 1);

            // ── THE BUILT LIBRARY ────────────────────────────────────────
            var library = Path.Combine(Radice(), Library, "bin", "Release", "Riscontro.Disegno.dll");
            if (!File.Exists(library))
            {
                string why = library + " is missing: build the library first (build.ps1)";
                NonQui("S7: the refusal names none, many or an unreadable tree", why);
                NonQui("S7: on sketch trees the sketch born is taken, not the last sketch", why);
                return;
            }

            Type sw;
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
                // From the bytes, as Zc215IdentityContract: the repository may sit
                // on a network drive, where LoadFrom is refused.
                sw = Assembly.Load(File.ReadAllBytes(library)).GetType("Riscontro.Disegno.OperazioniSw", false);
            }
            catch (Exception ex)
            {
                Segna("S7: the refusal names none, many or an unreadable tree", false,
                      "the library does not load: " + ex.GetType().Name + ": " + ex.Message);
                Segna("S7: on sketch trees the sketch born is taken, not the last sketch", false,
                      "the library does not load: " + ex.GetType().Name + ": " + ex.Message);
                return;
            }
            if (sw == null)
            {
                Segna("S7: the refusal names none, many or an unreadable tree", false, "OperazioniSw is not in the library");
                Segna("S7: on sketch trees the sketch born is taken, not the last sketch", false, "OperazioniSw is not in the library");
                return;
            }

            Refusal(sw);
            SketchTrees(sw);
        }

        // ── the built library ────────────────────────────────────────────────

        private static object Invoke(Type t, string method, object[] args)
        {
            var m = t.GetMethod(method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method);
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        private static void Refusal(Type sw)
        {
            try
            {
                const string mine = "The caller's own sentence.";
                var none = Invoke(sw, "SketchNotIdentified", new object[] { 0, mine }) as string;
                var unread = Invoke(sw, "SketchNotIdentified", new object[] { -1, mine }) as string;
                var two = Invoke(sw, "SketchNotIdentified", new object[] { 2, mine }) as string;
                bool ok = none == mine
                          && unread != null && unread.StartsWith(mine + " ", StringComparison.Ordinal)
                          && unread.IndexOf("could not be read", StringComparison.Ordinal) >= 0
                          && two != null && two.StartsWith("2 sketches", StringComparison.Ordinal)
                          && two.IndexOf(mine, StringComparison.Ordinal) < 0;
                Segna("S7: the refusal names none, many or an unreadable tree", ok,
                      "none: «" + none + "» | unreadable: «" + unread + "» | two: «" + two + "»");
            }
            catch (Exception ex)
            {
                Segna("S7: the refusal names none, many or an unreadable tree", false,
                      "OperazioniSw.SketchNotIdentified: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static int Born(Type sw, List<string> names, List<string> types, HashSet<string> before, string type, out int born)
        {
            var args = new object[] { names, types, before, type, 0 };
            int index = (int)Invoke(sw, "BornFeatureIndex", args);
            born = (int)args[4];
            return index;
        }

        private static void SketchTrees(Type sw)
        {
            try
            {
                var misses = new List<string>();
                int born;

                // An older sketch AFTER the new one (a tree edited before the
                // call): the last ProfileFeature is Sketch2, the one born Sketch3.
                var names = new List<string> { "Origin", "Sketch1", "Extrude1", "Sketch3", "Sketch2" };
                var types = new List<string> { "OriginProfileFeature", "ProfileFeature", "Extrusion", "ProfileFeature", "ProfileFeature" };
                int i = Born(sw, names, types, new HashSet<string> { "Origin", "Sketch1", "Extrude1", "Sketch2" }, "ProfileFeature", out born);
                if (i != 3 || born != 1) misses.Add("older sketch after the new one: " + i + " born " + born + " (expected 3 born 1; the old rule took 4)");

                // No sketch born: the old rule took Sketch1, the sketch Extrude1 had used.
                names = new List<string> { "Origin", "Sketch1", "Extrude1" };
                types = new List<string> { "OriginProfileFeature", "ProfileFeature", "Extrusion" };
                i = Born(sw, names, types, new HashSet<string>(names), "ProfileFeature", out born);
                if (i != -1 || born != 0) misses.Add("no sketch born: " + i + " born " + born + " (expected -1 born 0; the old rule took 1)");

                // After the feature (BloccaSchizzo): the sketch and the extrusion
                // are both born, and the sketch is the one ProfileFeature.
                names = new List<string> { "Origin", "Sketch1", "Extrude1", "Sketch2", "Extrude2" };
                types = new List<string> { "OriginProfileFeature", "ProfileFeature", "Extrusion", "ProfileFeature", "Extrusion" };
                i = Born(sw, names, types, new HashSet<string> { "Origin", "Sketch1", "Extrude1" }, "ProfileFeature", out born);
                if (i != 3 || born != 1) misses.Add("after the extrusion: " + i + " born " + born + " (expected 3 born 1)");

                // The sweep path: the 3D sketch born, not a 2D sketch drawn before it.
                names = new List<string> { "Origin", "Sketch1", "Boss-Extrude1", "3DSketch1" };
                types = new List<string> { "OriginProfileFeature", "ProfileFeature", "Extrusion", "3DProfileFeature" };
                var before = new HashSet<string> { "Origin", "Sketch1", "Boss-Extrude1" };
                i = Born(sw, names, types, before, "3DProfileFeature", out born);
                int flat = Born(sw, names, types, before, "ProfileFeature", out int flatBorn);
                if (i != 3 || born != 1 || flat != -1 || flatBorn != 0)
                    misses.Add("sweep path: " + i + " born " + born + ", as a 2D sketch " + flat + " born " + flatBorn +
                               " (expected 3 born 1, -1 born 0)");

                Segna("S7: on sketch trees the sketch born is taken, not the last sketch", misses.Count == 0,
                      misses.Count == 0
                          ? "an older sketch after the new one, no sketch born, the sketch after its extrusion, the 3D path"
                          : string.Join(" | ", misses));
            }
            catch (Exception ex)
            {
                Segna("S7: on sketch trees the sketch born is taken, not the last sketch", false,
                      "OperazioniSw.BornFeatureIndex: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // ── the check, on source text ────────────────────────────────────────

        /// <summary>What is wrong with the sketch lookups of these sources; empty when nothing.</summary>
        internal static List<string> SourceProblems(IDictionary<string, string> sources)
        {
            var problems = new List<string>();
            var code = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in sources) code[kv.Key] = CodeOnly(kv.Value);

            // 1. SchizzoAppenaCreato is FeatureBorn of a ProfileFeature on its own names.
            string sw;
            if (!code.TryGetValue("OperazioniSw.cs", out sw)) { problems.Add("OperazioniSw.cs is not among the sources"); return problems; }
            var header = Regex.Match(sw,
                @"\bIFeature\s+" + SketchLookup + @"\(\s*IModelDoc2\s+(\w+)\s*,\s*HashSet<string>\s+(\w+)\s*,\s*out\s+int\s+(\w+)\s*\)");
            if (!header.Success)
                problems.Add("SchizzoAppenaCreato(IModelDoc2, HashSet<string> names before the sketch, out int born) is not in OperazioniSw.cs");
            else
            {
                int start, end;
                MemberSpan(sw, header.Index, out start, out end);
                string body = sw.Substring(start, end - start);
                string answer = @"\bFeatureBorn\(\s*" + header.Groups[1].Value + @"\s*,\s*" + header.Groups[2].Value +
                                @"\s*,\s*""ProfileFeature""\s*,\s*out\s+" + header.Groups[3].Value + @"\s*\)";
                if (!Regex.IsMatch(body, answer))
                    problems.Add("SchizzoAppenaCreato does not answer FeatureBorn(model, its names, \"ProfileFeature\", out born)");
                if (Regex.IsMatch(body, @"\bI?FirstFeature\(|\bI?GetNextFeature\(|\bFeatureByPosition"))
                    problems.Add("SchizzoAppenaCreato walks the tree itself: the last sketch of the tree again");
            }

            // 2. The old sweep lookup is gone, from every source.
            foreach (var kv in code)
                if (Regex.IsMatch(kv.Value, @"\b" + OldSweepLookup + @"\b"))
                    problems.Add(kv.Key + ": UltimoSchizzoFeature is back (the last sketch of the tree)");

            // 3. In the three sources a sketch type is written only as the type of a FeatureBorn call.
            foreach (var file in ThreeSources)
            {
                string text;
                if (!code.TryGetValue(file, out text)) { problems.Add(file + " is not among the sources"); continue; }
                foreach (Match m in Regex.Matches(text, @"""(?:3D)?ProfileFeature"""))
                {
                    int from = Math.Max(0, m.Index - 300);
                    if (!Regex.IsMatch(text.Substring(from, m.Index - from), @"\bFeatureBorn\(\s*\w+\s*,\s*\w+\s*,\s*$"))
                        problems.Add(file + ":" + LineOf(text, m.Index) + ": " + m.Value +
                                     " is not the type of a FeatureBorn call (a walk that keeps the last sketch?)");
                }
            }

            // 4. Every lookup gets the names its caller took before opening that sketch.
            int wrapperCalls = 0, sweepPaths = 0;
            foreach (var kv in code)
            {
                string text = kv.Value;
                int named = 0;
                foreach (Match m in LookupNameRx.Matches(text)) if (!IsDeclaration(text, m.Index)) named++;
                int read = 0;
                foreach (Match m in LookupCallRx.Matches(text))
                {
                    if (IsDeclaration(text, m.Index)) continue;
                    read++;
                    string name = m.Groups["name"].Value;
                    string arg = m.Groups["names"].Value.Trim();
                    int start, end;
                    MemberSpan(text, m.Index, out start, out end);
                    string member = text.Substring(start, end - start);
                    string where = kv.Key + ":" + LineOf(text, m.Index) + ": " + name;
                    if (name == SketchLookup && (IsMember(member, SelectLookup) || IsMember(member, LockLookup)))
                    {
                        // The two wrappers pass on their own parameter; their callers are checked.
                        if (!Regex.IsMatch(Header(member), @"HashSet<string>\s+" + Regex.Escape(arg) + @"\b"))
                            problems.Add(where + " gets «" + arg + "», not the names parameter of its wrapper");
                        continue;
                    }
                    if (name != SketchLookup) wrapperCalls++;
                    string why = NamesTakenBefore(member, m.Index - start, arg);
                    if (why != null) problems.Add(where + " " + why);
                }
                if (read != named)
                    problems.Add(kv.Key + ": " + named + " sketch lookup call(s), " + read +
                                 " readable by this check (write the model and the names as plain arguments)");

                if (!ThreeSources.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)) continue;
                foreach (Match m in BornSketchRx.Matches(text))
                {
                    int start, end;
                    MemberSpan(text, m.Index, out start, out end);
                    string member = text.Substring(start, end - start);
                    if (IsMember(member, SketchLookup)) continue;   // rule 1
                    if (m.Groups["type"].Value == "3DProfileFeature" &&
                        kv.Key.Equals("OperazioniPercorso.cs", StringComparison.OrdinalIgnoreCase)) sweepPaths++;
                    string why = NamesTakenBefore(member, m.Index - start, m.Groups["names"].Value.Trim());
                    if (why != null) problems.Add(kv.Key + ":" + LineOf(text, m.Index) + ": FeatureBorn of a sketch " + why);
                }
            }
            if (wrapperCalls == 0)
                problems.Add("no call of SelezionaSchizzoAppenaCreato or BloccaSchizzo found: the check would prove nothing");
            if (sweepPaths == 0)
                problems.Add("OperazioniPercorso.cs: the sweep path is not FeatureBorn of a 3DProfileFeature");
            return problems;
        }

        /// <summary>
        /// Null when <paramref name="arg"/> is assigned in <paramref name="member"/>
        /// only from FeatureNamesNow, and its last assignment before the call at
        /// <paramref name="at"/> lies after the sketch opened before the one the
        /// call looks for and before that one; otherwise what is wrong.
        /// </summary>
        private static string NamesTakenBefore(string member, int at, string arg)
        {
            if (!Regex.IsMatch(arg, @"^[A-Za-z_]\w*$") || arg == "null" || arg == "default")
                return "gets «" + arg + "», not the names its caller took before the sketch";
            var openings = OpeningRx.Matches(member).Cast<Match>().Select(x => x.Index).ToList();
            int open = openings.Where(i => i < at).DefaultIfEmpty(-1).Max();
            if (open < 0) return "is called where no sketch was opened before it";
            int previous = openings.Where(i => i < open).DefaultIfEmpty(-1).Max();
            var assignments = Regex.Matches(member, @"(?<![\w.])" + Regex.Escape(arg) + @"\s*=(?![=>])").Cast<Match>().ToList();
            if (assignments.Count == 0)
                return "gets «" + arg + "», which its member does not assign (the names are taken there, before the sketch)";
            foreach (var a in assignments)
                if (!Regex.IsMatch(member.Substring(a.Index), "^" + Regex.Escape(arg) + @"\s*=\s*FeatureNamesNow\("))
                    return "gets «" + arg + "», which is assigned from something other than FeatureNamesNow";
            int last = assignments.Select(x => x.Index).Where(i => i < at).DefaultIfEmpty(-1).Max();
            if (last < 0) return "gets «" + arg + "», assigned only after the call";
            if (last > open) return "gets names taken after its sketch was opened: the new sketch is already among them";
            if (last < previous) return "gets names taken before an earlier sketch was opened: two sketches are born since";
            return null;
        }

        private static string Summary(IDictionary<string, string> sources)
        {
            int calls = 0, born = 0;
            foreach (var kv in sources)
            {
                var text = CodeOnly(kv.Value);
                foreach (Match m in LookupCallRx.Matches(text)) if (!IsDeclaration(text, m.Index)) calls++;
                if (ThreeSources.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)) born += BornSketchRx.Matches(text).Count;
            }
            return calls + " lookup calls and " + born + " FeatureBorn of a sketch, each with names taken before its sketch";
        }

        // ── seen red ─────────────────────────────────────────────────────────

        private sealed class SourceChange
        {
            public string File, Name, From, To;
        }

        private static SourceChange Change(string file, string name, string from, string to)
        {
            return new SourceChange { File = file, Name = name, From = from, To = to };
        }

        /// <summary>
        /// Each change applied alone to its file must leave sources the check
        /// calls wrong. Returns what did not: a change whose anchor is no longer
        /// in the file (the check would then prove nothing), or a changed copy
        /// the check still calls right.
        /// </summary>
        private static List<string> SeenRed(IDictionary<string, string> sources, SourceChange[] changes)
        {
            var misses = new List<string>();
            if (SourceProblems(sources).Count != 0) { misses.Add("the untouched source is already red, the changes prove nothing"); return misses; }
            foreach (var c in changes)
            {
                string source;
                if (!sources.TryGetValue(c.File, out source)) { misses.Add(c.Name + ": " + c.File + " is not among the sources"); continue; }
                int at = source.IndexOf(c.From, StringComparison.Ordinal);
                if (at < 0) { misses.Add(c.Name + ": anchor not found in " + c.File + " (update the change)"); continue; }
                var changed = new Dictionary<string, string>(sources, StringComparer.OrdinalIgnoreCase);
                changed[c.File] = source.Substring(0, at) + c.To + source.Substring(at + c.From.Length);
                if (SourceProblems(changed).Count == 0) misses.Add(c.Name + ": the changed copy is still green");
            }
            return misses;
        }

        // ── source helpers ───────────────────────────────────────────────────

        /// <summary>
        /// The source without its comment lines and trailing line comments, so
        /// that a call named in a comment is not counted; the lines stay where
        /// they were. String literals are kept: the sketch types are in them.
        /// </summary>
        private static string CodeOnly(string source)
        {
            var lines = source.Replace("\r\n", "\n").Split('\n');
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
        /// The member around a position: from the last line at member
        /// indentation (eight spaces) that opens a member before it, to the
        /// closing brace at member indentation after it.
        /// </summary>
        private static void MemberSpan(string code, int at, out int start, out int end)
        {
            start = 0;
            foreach (var opener in new[] { "\n        private ", "\n        internal ", "\n        public ", "\n        protected " })
                start = Math.Max(start, code.LastIndexOf(opener, at, StringComparison.Ordinal));
            end = code.IndexOf("\n        }", at, StringComparison.Ordinal);
            if (end < 0) end = code.Length;
        }

        /// <summary>The header of a member: its text up to the first brace.</summary>
        private static string Header(string member)
        {
            int brace = member.IndexOf('{');
            return brace < 0 ? member : member.Substring(0, brace);
        }

        /// <summary>Whether the member declares a method of that name.</summary>
        private static bool IsMember(string member, string name)
        {
            return Regex.IsMatch(Header(member), @"(?<![\w.])" + Regex.Escape(name) + @"\(");
        }

        /// <summary>Whether the name at that position is declared there, not called.</summary>
        private static bool IsDeclaration(string code, int at)
        {
            int lineStart = code.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
            string before = code.Substring(lineStart, at - lineStart);
            return Regex.IsMatch(before, @"^\s*(?:(?:private|internal|public|protected|static)\s+)+[\w<>\[\],]+\s+$");
        }

        private static int LineOf(string code, int at)
        {
            int line = 1;
            for (int i = 0; i < at && i < code.Length; i++) if (code[i] == '\n') line++;
            return line;
        }
    }
}
