using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE RETURNS AND MESSAGES OF Z-C21.8 IN THE WELDMENT, ASSEMBLY AND EXPORT
    /// OPERATIONS, WHERE THE BENCH CANNOT REACH (TODO entries Z-C21.8 and
    /// Z-C21.9, the weldment and assembly group).
    ///
    /// Two halves.
    ///
    /// 1. ON THE SOURCE, no library needed: the answers SOLIDWORKS gives are
    ///    not dropped. A call written as a whole statement (`bf.UpdateCutList();`,
    ///    `(face as IEntity).Select4(true, null);`, or the body of an `if`)
    ///    throws its bool away. The bench cannot make these calls answer false:
    ///    a Select4 that fails, a cut list that does not update, a view that
    ///    does not select. So the rule is checked where it lives, on the text,
    ///    for the calls the lot named in the files it touched: Select4 in the
    ///    mate operation, SetAutomaticCutList, SetAutomaticUpdate, UpdateCutList,
    ///    Select2 and DeleteSelection2 in the weldments (N15, N30, N41),
    ///    SelectByID2 and AutoBalloon5 in the ballooning (M39). The rule shows
    ///    its teeth first on lines written to be caught, and to be let through.
    ///
    /// 2. ON THE BUILT LIBRARY, by reflection: the decisions of the fixes are
    ///    small static methods with no interop in their signatures (N15, N17,
    ///    N18, N22, N30, N38, M39, L13-7), called here with the cases the
    ///    bench cannot build: an item of a cut list that lumps bars of two
    ///    lengths, a preference that could not be read, a possible interference,
    ///    a suppressed component, a weldment feature that would not go away.
    ///
    /// On library 0.144.0 every check is red: the calls are discarded
    /// statements, and the methods are missing.
    /// </summary>
    internal static class Zc2189WeldAssemblyContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags AnyInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>The calls whose answer must be read, file by file.</summary>
        private static readonly Dictionary<string, string[]> ReadAnswers =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "MateOperations.cs", new[] { "Select4" } },
                { "WeldmentOperations.cs", new[] { "SetAutomaticCutList", "SetAutomaticUpdate", "UpdateCutList", "Select2", "DeleteSelection2", "Select4" } },
                { "BomOperations.cs", new[] { "SelectByID2", "AutoBalloon5" } },
                { "MirrorComponentOperations.cs", new[] { "Select2", "DeleteSelection2" } },
            };

        internal static void RunAll()
        {
            Group("the answers of SOLIDWORKS are read in the weldment, assembly and export operations (Z-C21.8)");

            DiscardedAnswersOnTheSource();

            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere("the pure rules of the Z-C21.8 weldment and assembly fixes",
                       library + " is missing: build the library first (build.ps1)");
                return;
            }

            Assembly asm;
            bool interop = false;
            try
            {
                var redist = WhereTheInteropIs();
                if (redist != null)
                {
                    interop = true;
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
                asm = Assembly.Load(File.ReadAllBytes(library));
            }
            catch (Exception ex) { Check("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }

            Type sw = asm.GetType("Oce.Solid.Agent.Core.SwOperations", false);
            if (sw == null) { Check("SwOperations is in the library", false, "type missing"); return; }

            TrimEndCondition(sw);
            CutListItems(sw);
            WeldmentRemoval(sw);
            MirrorRemoval(sw);
            AutomaticCutListRefusal(sw);
            InterferenceVerdict(sw);
            ContactsAndPairs(sw);
            ExchangeAndTables(sw);
            InterferenceSettings(sw);
            ComponentsLookedAt(sw);
            Balloons(sw);
            PreferenceSnapshot(asm);
            SaveWarnings(sw, interop);
        }

        private static object Call(Type t, string method, params object[] args)
        {
            var m = ReflectionLog.Global.Method(t, method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method);
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        // =================================================================
        //  1. ON THE SOURCE
        // =================================================================

        private static void DiscardedAnswersOnTheSource()
        {
            // ── TEETH FIRST ──────────────────────────────────────────────
            var mustCatch = new[]
            {
                "bf.UpdateCutList();",
                "{ (faceA as IEntity).Select4(true, null); }",
                "if (ok) model.Extension.DeleteSelection2(0);",
                "try { c.UpdateCutList(); } catch { }",
                "else x?.Select4(true, null);",
                "foreach (var c in cs)\n    c.UpdateCutList();",
                "model.Extension\n    .SelectByID2(n, \"DRAWINGVIEW\", 0, 0, 0, false, 0, null, 0);",
            };
            var mustPass = new[]
            {
                "bool s = (f as IEntity).Select4(true, null);",
                "if (!c.UpdateCutList()) n++;",
                "return a.Select2(false, 0);",
                "x = y && z.Select4(true, null);",
                "Foo(bf.UpdateCutList());",
                "// bf.UpdateCutList();",
                "var t = \"a.UpdateCutList();\";",
                "if (f.Select2(false, 0))\n    removed = model.Extension.DeleteSelection2(1);",
                "/* c.UpdateCutList(); */ int k = 0;",
            };
            var names = new[] { "UpdateCutList", "Select4", "Select2", "DeleteSelection2", "SelectByID2" };
            int caught = mustCatch.Count(s => Discarded(s, names).Count > 0);
            var falseAlarms = mustPass.Where(s => Discarded(s, names).Count > 0).ToList();
            Check("the rule catches an answer thrown away", caught == mustCatch.Length,
                  caught + " of " + mustCatch.Length + " discarded calls caught");
            Check("and lets an answer that is read go through", falseAlarms.Count == 0,
                  falseAlarms.Count == 0 ? mustPass.Length + " lines that read the answer, no false alarm"
                                         : string.Join(" | ", falseAlarms));

            // ── THEN THE REAL SOURCE ─────────────────────────────────────
            var folder = Path.Combine(Root(), "Oce.Solid.Agent.Core");
            var found = new List<string>();
            int calls = 0;
            foreach (var entry in ReadAnswers)
            {
                var path = Path.Combine(folder, entry.Key);
                if (!File.Exists(path)) { found.Add(entry.Key + " is missing"); continue; }
                var text = File.ReadAllText(path);
                calls += CountCalls(text, entry.Value);
                foreach (var d in Discarded(text, entry.Value)) found.Add(entry.Key + ":" + d);
            }
            Check("no answer of these calls is thrown away (Select4, cut list, Select2, DeleteSelection2, SelectByID2, AutoBalloon5)",
                  found.Count == 0 && calls > 0,
                  found.Count == 0 ? calls + " calls in " + ReadAnswers.Count + " files, every answer read"
                                   : string.Join(" | ", found));

            // L11-7: the mirror of components reads the error code of what
            // was born, after the rebuild, before its component checks.
            var mirror = Path.Combine(folder, "MirrorComponentOperations.cs");
            string mirrorSource = File.Exists(mirror) ? File.ReadAllText(mirror) : "";
            bool reads = Regex.IsMatch(mirrorSource, @"ApiHelpers\.RebuildError\(\s*\w+\s*,\s*out\s+bool\s+mirrorErrorKnown\s*\)");
            Check("L11-7: the component mirror reads GetErrorCode2 of the mirror it made", reads,
                  "MirrorComponentOperations.cs: ApiHelpers.RebuildError(..., out bool mirrorErrorKnown)");
        }

        /// <summary>How many calls to the names the (stripped) text holds.</summary>
        private static int CountCalls(string text, string[] names)
        {
            var code = StripCommentsAndStrings(text);
            int n = 0;
            foreach (var name in names)
                n += Regex.Matches(code, @"\.\s*" + Regex.Escape(name) + @"\s*\(").Count;
            return n;
        }

        /// <summary>
        /// The lines (1-based, with the call) where a call to one of the names is
        /// a whole statement, or the body of an if/while/for/foreach/using/lock:
        /// its answer is thrown away. Comments and string literals do not count.
        /// </summary>
        internal static List<string> Discarded(string text, string[] names)
        {
            var result = new List<string>();
            var code = StripCommentsAndStrings(text);
            foreach (var name in names)
            {
                foreach (Match m in Regex.Matches(code, @"\.\s*" + Regex.Escape(name) + @"\s*\("))
                {
                    int start = ReceiverStart(code, m.Index);
                    int p = start - 1;
                    while (p >= 0 && char.IsWhiteSpace(code[p])) p--;
                    if (IsStatementContext(code, p))
                    {
                        int line = 1;
                        for (int i = 0; i < m.Index; i++) if (code[i] == '\n') line++;
                        result.Add(line + " " + name);
                    }
                }
            }
            return result;
        }

        /// <summary>Where the receiver of the member access at <paramref name="dot"/> begins.</summary>
        private static int ReceiverStart(string code, int dot)
        {
            int j = dot - 1;
            if (j >= 0 && code[j] == '?') j--;              // x?.Call(
            while (true)
            {
                while (j >= 0 && char.IsWhiteSpace(code[j])) j--;
                if (j < 0) return 0;
                int segStart;
                if (code[j] == ')' || code[j] == ']')
                {
                    int open = MatchBackward(code, j);
                    if (open < 0) return j + 1;
                    segStart = open;
                    int k = open - 1;
                    if (k >= 0 && IsIdent(code[k]))          // foo(x) or a[i]
                    {
                        while (k >= 0 && IsIdent(code[k])) k--;
                        segStart = k + 1;
                    }
                }
                else if (IsIdent(code[j]))
                {
                    int k = j;
                    while (k >= 0 && IsIdent(code[k])) k--;
                    segStart = k + 1;
                }
                else return j + 1;

                int q = segStart - 1;
                while (q >= 0 && char.IsWhiteSpace(code[q])) q--;
                if (q >= 0 && code[q] == '.')
                {
                    j = q - 1;
                    if (j >= 0 && code[j] == '?') j--;
                    continue;
                }
                return segStart;
            }
        }

        /// <summary>Whether the character at <paramref name="p"/>, just before a receiver, starts a statement.</summary>
        private static bool IsStatementContext(string code, int p)
        {
            if (p < 0) return true;
            char c = code[p];
            if (c == ';' || c == '{' || c == '}') return true;
            if (c == ')')
            {
                int open = MatchBackward(code, p);
                if (open < 0) return false;
                string word = WordBefore(code, open);
                return word == "if" || word == "while" || word == "for" || word == "foreach" ||
                       word == "using" || word == "lock" || word == "fixed";
            }
            if (IsIdent(c))
            {
                string word = WordBefore(code, p + 1);
                return word == "else" || word == "do";
            }
            return false;
        }

        private static string WordBefore(string code, int end)
        {
            int k = end - 1;
            while (k >= 0 && char.IsWhiteSpace(code[k])) k--;
            int e = k;
            while (k >= 0 && IsIdent(code[k])) k--;
            return e > k ? code.Substring(k + 1, e - k) : "";
        }

        private static bool IsIdent(char c) { return char.IsLetterOrDigit(c) || c == '_'; }

        private static int MatchBackward(string code, int close)
        {
            char closing = code[close];
            char opening = closing == ')' ? '(' : '[';
            int depth = 0;
            for (int i = close; i >= 0; i--)
            {
                if (code[i] == closing) depth++;
                else if (code[i] == opening) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        /// <summary>
        /// The text with comments and the contents of string and char literals
        /// turned into spaces, newlines kept (so that line numbers stay right).
        /// </summary>
        internal static string StripCommentsAndStrings(string s)
        {
            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                char next = i + 1 < s.Length ? s[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < s.Length && s[i] != '\n') { sb.Append(' '); i++; }
                }
                else if (c == '/' && next == '*')
                {
                    sb.Append("  "); i += 2;
                    while (i < s.Length && !(s[i] == '*' && i + 1 < s.Length && s[i + 1] == '/'))
                    { sb.Append(s[i] == '\n' ? '\n' : ' '); i++; }
                    if (i < s.Length) { sb.Append("  "); i += 2; }
                }
                else if (c == '@' && next == '"')
                {
                    sb.Append("@\""); i += 2;
                    while (i < s.Length)
                    {
                        if (s[i] == '"' && i + 1 < s.Length && s[i + 1] == '"') { sb.Append("  "); i += 2; continue; }
                        if (s[i] == '"') { sb.Append('"'); i++; break; }
                        sb.Append(s[i] == '\n' ? '\n' : ' '); i++;
                    }
                }
                else if (c == '"')
                {
                    sb.Append('"'); i++;
                    while (i < s.Length && s[i] != '"' && s[i] != '\n')
                    {
                        if (s[i] == '\\' && i + 1 < s.Length) { sb.Append("  "); i += 2; continue; }
                        sb.Append(' '); i++;
                    }
                    if (i < s.Length) { sb.Append(s[i]); i++; }
                }
                else if (c == '\'')
                {
                    sb.Append('\''); i++;
                    while (i < s.Length && s[i] != '\'' && s[i] != '\n')
                    {
                        if (s[i] == '\\' && i + 1 < s.Length) { sb.Append("  "); i += 2; continue; }
                        sb.Append(' '); i++;
                    }
                    if (i < s.Length) { sb.Append(s[i]); i++; }
                }
                else { sb.Append(c); i++; }
            }
            return sb.ToString();
        }

        // =================================================================
        //  2. ON THE BUILT LIBRARY
        // =================================================================

        // ── N22: the four end conditions of trim_members, nothing else ──────
        private static void TrimEndCondition(Type sw)
        {
            try
            {
                // swSolidworksWeldmentEndCondOptions_e (swconst page): Miter 1,
                // Butt1 2, Butt2 3, Trim 4, None 0.
                // The case is changed at run time, so that no spelling of the
                // catalogue words is written here that the catalogue does not use.
                int trim = (int)Call(sw, "TrimEndCondition", "cut");
                int miter = (int)Call(sw, "TrimEndCondition", " " + "miter".ToUpperInvariant() + " ");
                int butt1 = (int)Call(sw, "TrimEndCondition", "butt1");
                int butt2 = (int)Call(sw, "TrimEndCondition", "butt2".ToUpperInvariant());
                int none = (int)Call(sw, "TrimEndCondition", "none");
                int unknown = (int)Call(sw, "TrimEndCondition", "unknown");
                int missing = (int)Call(sw, "TrimEndCondition", (object)null);
                Check("N22: the four catalogue words give Trim 4, Miter 1, Butt1 2, Butt2 3, whatever their case",
                      trim == 4 && miter == 1 && butt1 == 2 && butt2 == 3,
                      trim + ", " + miter + ", " + butt1 + ", " + butt2);
                Check("N22: the word for none, an unknown word and nothing are refused (-1), not Trim",
                      none == -1 && unknown == -1 && missing == -1,
                      none + ", " + unknown + ", " + missing);
            }
            catch (Exception ex) { Check("N22: TrimEndCondition can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── N15: an item of the cut list, body by body ───────────────────────
        private static void CutListItems(Type sw)
        {
            try
            {
                // GT2: four bars of 1000 and 800 mm of the 80X80X5 tube in one
                // item «QUANTITY 4»: the quantity is right, the lengths are not.
                var gt2 = (string)Call(sw, "CutListItemProblem", "TUBE 80X80X5", 4, (int?)4,
                                       new[] { 1469956.0, 1469956.0, 1175964.8, 1175964.8 });
                var quantity = (string)Call(sw, "CutListItemProblem", "TUBE 80X80X5", 2, (int?)3,
                                            new[] { 1469956.0, 1469956.0 });
                var sound = (string)Call(sw, "CutListItemProblem", "TUBE 80X80X5", 2, (int?)2,
                                         new[] { 1469956.0, 1469956.1 });
                var unread = (string)Call(sw, "CutListItemProblem", null, -1, null, null);
                Check("N15: one item holding bars of two volumes is a problem (the defect of GT2)",
                      gt2 != null && gt2.Contains("different volume"), gt2 ?? "null");
                Check("N15: a QUANTITY that is not the bodies of its folder is a problem",
                      quantity != null && quantity.Contains("QUANTITY 3") && quantity.Contains("2 bodies"),
                      quantity ?? "null");
                Check("N15: equal bodies are sound, and what was not read does not accuse",
                      sound == null && unread == null, (sound ?? "null") + " | " + (unread ?? "null"));

                var equalNull = (bool?)Call(sw, "BodiesAreEqual", (object)null);
                var equalOne = (bool?)Call(sw, "BodiesAreEqual", new[] { 5.0 });
                var equalNot = (bool?)Call(sw, "BodiesAreEqual", new[] { 5.0, 900.0 });
                Check("N15: bodies_equal is null when unknown, true for one body, false for two volumes",
                      equalNull == null && equalOne == true && equalNot == false,
                      (equalNull.HasValue ? equalNull.ToString() : "null") + ", " + equalOne + ", " + equalNot);
            }
            catch (Exception ex) { Check("N15: CutListItemProblem and BodiesAreEqual can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── N30: the weldment feature that would not go away ─────────────────
        private static void WeldmentRemoval(Type sw)
        {
            try
            {
                Func<bool, bool, bool?, int, int, string> note = (sel, del, after, before, now) =>
                    (string)Call(sw, "WeldmentRemovalNote", "Weldment1", sel, del, after, before, now);
                var notSelected = note(false, false, null, 4, 4);
                var notDeleted = note(true, false, true, 4, 4);
                var stillWeldment = note(true, true, true, 4, 4);
                var unread = note(true, true, null, 4, 4);
                var gone = note(true, true, false, 4, 4);
                var bodies = note(true, true, false, 4, 3);
                Check("N30: a Select2 or a DeleteSelection2 that answers false says the part stayed a weldment",
                      notSelected.Contains("Select2 answered false") && notSelected.Contains("stayed a weldment") &&
                      notDeleted.Contains("DeleteSelection2 answered false") && notDeleted.Contains("stayed a weldment"),
                      notSelected + " | " + notDeleted);
                Check("N30: after the deletion IsWeldment decides: true stays a weldment, unread says so",
                      stillWeldment.Contains("still reads true") && unread.Contains("could not read IsWeldment"),
                      stillWeldment + " | " + unread);
                Check("N30: «as it was» only when IsWeldment reads false, and bodies that changed are said",
                      gone.Contains("reads false again") && !gone.Contains("stayed") &&
                      bodies.Contains("3 bodies where it had 4"),
                      gone + " | " + bodies);
            }
            catch (Exception ex) { Check("N30: WeldmentRemovalNote can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── The component mirror a refusal removes: said removed after a reread ──
        // RemoveMirror said «the assembly is as it was» on the answer of
        // DeleteSelection2 alone, and L11-7 gave it one more caller. Now the tree
        // is read again: removed only when the mirror is no longer in it.
        private static void MirrorRemoval(Type sw)
        {
            try
            {
                Func<bool, bool?, string, string> note = (del, still, file) =>
                    (string)Call(sw, "MirrorRemovalNote", "MirrorComponent1", del, still, file);
                var gone = note(true, false, null);
                var stillThere = note(true, true, null);
                var unread = note(true, null, null);
                var refused = note(false, null, "Bracket_left.SLDPRT");
                Check("L11-7/M42: the component mirror is said removed only when the tree read again lacks it",
                      gone.Contains("I removed") && gone.Contains("it is gone") &&
                      !stillThere.Contains("I removed") && stillThere.Contains("still in the tree") &&
                      !unread.Contains("I removed") && unread.Contains("could not read the tree") &&
                      !refused.Contains("I removed") && refused.Contains("did not remove") &&
                      refused.Contains("Bracket_left.SLDPRT"),
                      gone + " | " + stillThere + " | " + unread + " | " + refused);
            }
            catch (Exception ex) { Check("L11-7/M42: MirrorRemovalNote can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── N15: the refusal when the automatic cut list does not turn on ────
        private static void AutomaticCutListRefusal(Type sw)
        {
            try
            {
                var oldList = (string)Call(sw, "AutomaticCutListRefusal", (bool?)false, true);
                var refused = (string)Call(sw, "AutomaticCutListRefusal", (bool?)false, false);
                var off = (string)Call(sw, "AutomaticCutListRefusal", null, false);
                Check("N15: an old cut list gets its own message, and SetAutomaticCutList's false is said",
                      oldList.Contains("Delete that cut list") && oldList.Contains("0.124.0") &&
                      refused.Contains("SetAutomaticCutList answered false") &&
                      off.Contains("GetAutomaticCutList reads false"),
                      oldList + " | " + refused + " | " + off);
            }
            catch (Exception ex) { Check("N15: AutomaticCutListRefusal can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── N38: certain and possible interferences apart ────────────────────
        private static void InterferenceVerdict(Type sw)
        {
            try
            {
                // WHAT THE BENCH READS in the verdict of a certain interference
                // comes from the bench case itself, not from a copy of its words
                // here: the words a case of the lid in the base expects, and the
                // first of them is the alarm that opens the verdict.
                var expected = BenchVerdictWords("assemblies", "cover_inside_base");
                if (expected == null || expected.Count == 0)
                {
                    Check("N38: the bench case of the lid in the base says what its verdict holds", false,
                          "no expected words for the verdict in that bench case");
                    return;
                }
                // The alarm that opens the verdict is fixed here: the case now asks for the sentence that explains it
                // (the words «The parts occupy the same space»), not for the bare word, which many other messages share.
                string alarm = "WARNING";
                var two = new List<string> { "base-1", "lid-1" };
                var certain = (string)Call(sw, "InterferenceVerdict", 1, (int?)0, true, 19200.0, 0.0, two);
                var onlyPossible = (string)Call(sw, "InterferenceVerdict", 1, (int?)1, true, 5.0, 5.0, two);
                var mixed = (string)Call(sw, "InterferenceVerdict", 3, (int?)1, true, 30.0, 10.0, two);
                var unknown = (string)Call(sw, "InterferenceVerdict", 2, null, false, 0.0, 0.0, null);
                Check("N38: a certain interference keeps the verdict the bench reads, count and volume",
                      expected.All(w => certain.Contains(w)) &&
                      certain.StartsWith(alarm + ": 1 ", StringComparison.Ordinal) &&
                      certain.Contains(" 19200 mm3") && !certain.Contains("POSSIBLE"),
                      Truncated(certain));
                Check("N38: one POSSIBLE interference does not raise the alarm of a certain one",
                      !onlyPossible.Contains(alarm) && onlyPossible.Contains("1 POSSIBLE interference") &&
                      onlyPossible.Contains("no certain one"),
                      onlyPossible);
                Check("N38: three with one possible: two certain in the head, the possible one apart",
                      mixed.StartsWith(alarm + ": 2 ", StringComparison.Ordinal) && mixed.Contains(" 20 mm3") &&
                      mixed.Contains("Besides these, 1 POSSIBLE interference, 10 mm3"),
                      mixed);
                Check("N38: when the flag was not read, all are counted and the verdict says it",
                      unknown.StartsWith(alarm + ": 2 ", StringComparison.Ordinal) &&
                      unknown.Contains("could not read"),
                      unknown);
            }
            catch (Exception ex) { Check("N38: InterferenceVerdict can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── report 8cd843 N14 and N15: contacts apart, the pair and the box ──
        private static void ContactsAndPairs(Type sw)
        {
            try
            {
                var pairs = new List<string> { "26P000007-1 and 26P000008-1" };
                var alone = (string)Call(sw, "ContactSentence", 1, pairs, true);
                var after = (string)Call(sw, "ContactSentence", 1, pairs, false);
                Check("N15: faces that touch alone are no alarm, and say which pair",
                      !alone.Contains("WARNING") && alone.Contains("1 CONTACT") &&
                      alone.Contains("26P000007-1 and 26P000008-1") && alone.Contains("touching_counts"),
                      alone);
                Check("N15: next to interferences with a volume they are counted apart, not in the volume",
                      after.StartsWith("Besides these, 1 CONTACT", StringComparison.Ordinal) && after.Contains("not in the volume"),
                      after);
            }
            catch (Exception ex) { Check("N15: ContactSentence can be called", false, ex.GetType().Name + ": " + ex.Message); }

            string source;
            try { source = File.ReadAllText(Path.Combine(Root(), "Oce.Solid.Agent.Core", "InterferenceOperations.cs")).Replace("\r\n", "\n"); }
            catch (Exception ex) { Check("N15: InterferenceOperations.cs can be read", false, ex.Message); return; }
            var problems = ContactSourceProblems(source);
            Check("N14/N15/N16: a sheet body is a contact before any volume is read, each interference names its pair and box, the unsaved mark of Done is said",
                  problems.Count == 0, problems.Count == 0 ? "swSheetBody before GetMassProperties; Components; GetBodyBox; GetSaveFlag around Done" : string.Join(" | ", problems));
            var changed = new[]
            {
                Tuple.Create("the sheet read as a volume (N15)", "if (type == (int)swBodyType_e.swSheetBody) { contact = true; return true; }", ""),
                Tuple.Create("no pair per interference (N14)", "entry[\"components\"] = pair.ToArray();", "entry[\"pair\"] = null;"),
                Tuple.Create("no box per interference (N14)", "entry[\"box_from_mm\"] =", "entry[\"from\"] ="),
                Tuple.Create("the unsaved mark not said (N16)", "measurements[\"marked_unsaved\"] = true;", "measurements[\"quiet\"] = true;"),
            };
            var notRed = changed.Where(c => !source.Contains(c.Item2) || ContactSourceProblems(source.Replace(c.Item2, c.Item3)).Count == 0)
                                .Select(c => c.Item1).ToList();
            Check("N14/N15: the check turns red on each changed copy", notRed.Count == 0,
                  notRed.Count == 0 ? changed.Length + " changes, each one red" : "NOT red: " + string.Join(", ", notRed));
        }

        // ── report 8cd843 round 6: N18 to N22 ───────────────────────────────
        private static void ExchangeAndTables(Type sw)
        {
            try
            {
                var iges = (string)Call(sw, "AssemblyExchangeRefusal", "export_iges", false);
                var reopened = (string)Call(sw, "AssemblyExchangeRefusal", "export_step", true);
                var step = (string)Call(sw, "AssemblyExchangeRefusal", "export_step", false);
                var other = (string)Call(sw, "AssemblyExchangeRefusal", "export_stl", true);
                Check("N18: one text for the IGES and the reopened STEP of an assembly, for the pre-check and the operation",
                      iges != null && iges.Contains("IGES of an assembly") && reopened != null && reopened.Contains("reopen_and_compare") &&
                      step == null && other == null,
                      (iges ?? "null") + " | " + (reopened ?? "null") + " | " + (step ?? "null") + " | " + (other ?? "null"));
            }
            catch (Exception ex) { Check("N18: AssemblyExchangeRefusal can be called", false, ex.GetType().Name + ": " + ex.Message); }

            try
            {
                var headers = new List<string> { "ITEM NO.", "PART NUMBER", "DESCRIPTION", "QTY." };
                var rows = new List<object>
                {
                    new[] { "1", "26P000007", "", "2" },
                    new[] { "2", "26P000008", " ", "1" },
                };
                var oneEmpty = (string)Call(sw, "EmptyColumnsWarning", headers, rows);
                rows.Add(new[] { "3", "26P000009", "Clip", "4" });
                var filled = (string)Call(sw, "EmptyColumnsWarning", headers, rows);
                var noRows = (string)Call(sw, "EmptyColumnsWarning", headers, new List<object>());
                Check("N20: a column empty on every row is said, with its name and the rows",
                      oneEmpty != null && oneEmpty.Contains("«DESCRIPTION»") && oneEmpty.Contains("all 2 rows") &&
                      !oneEmpty.Contains("PART NUMBER"),
                      oneEmpty ?? "null");
                Check("N20: a column with one cell filled, or a table with no rows, is no warning",
                      filled == null && noRows == null, (filled ?? "null") + " | " + (noRows ?? "null"));
            }
            catch (Exception ex) { Check("N20: EmptyColumnsWarning can be called", false, ex.GetType().Name + ": " + ex.Message); }

            try
            {
                var font = (string)Call(sw, "WithoutFormatCodes", "<FONT size=3.5>PART NUMBER");
                var closed = (string)Call(sw, "WithoutFormatCodes", "<font size=3.5 style=B>QTY.</FONT>");
                var two = (string)Call(sw, "WithoutFormatCodes", "<FONT size=2>A<FONT size=3>B");
                var plain = (string)Call(sw, "WithoutFormatCodes", "M8 <FONTANA> 2 > 1");
                var empty = (string)Call(sw, "WithoutFormatCodes", (object)null);
                Check("N21: the FONT codes of a note are taken out of a cell",
                      font == "PART NUMBER" && closed == "QTY." && two == "AB",
                      font + " | " + closed + " | " + two);
                Check("N21: any other text, and an empty cell, stay as they are",
                      plain == "M8 <FONTANA> 2 > 1" && empty == "", plain + " | «" + empty + "»");
            }
            catch (Exception ex) { Check("N21: WithoutFormatCodes can be called", false, ex.GetType().Name + ": " + ex.Message); }

            try
            {
                var note = (string)Call(sw, "SetSheetsFalseNote", 3, 3);
                var unread = (string)Call(sw, "SetSheetsFalseNote", -1, 2);
                Check("N22: a false of SetSheets is explained next to it, under 100 characters so the summary keeps it",
                      note != null && note.Contains("page count") && note.EndsWith("3 of 3", StringComparison.Ordinal) &&
                      note.Length <= 100 && unread != null && unread.EndsWith("? of 2", StringComparison.Ordinal) && unread.Length <= 100,
                      (note ?? "null") + " (" + (note == null ? 0 : note.Length) + ") | " + (unread ?? "null"));
            }
            catch (Exception ex) { Check("N22: SetSheetsFalseNote can be called", false, ex.GetType().Name + ": " + ex.Message); }

            string export, drawing, bom, igesText, reopenText;
            try
            {
                export = File.ReadAllText(Path.Combine(Root(), "Oce.Solid.Agent.Core", "ExportOperations.cs")).Replace("\r\n", "\n");
                drawing = File.ReadAllText(Path.Combine(Root(), "Oce.Solid.Agent.Core", "DrawingOperations.cs")).Replace("\r\n", "\n");
                bom = File.ReadAllText(Path.Combine(Root(), "Oce.Solid.Agent.Core", "BomOperations.cs")).Replace("\r\n", "\n");
                igesText = (string)Call(sw, "AssemblyExchangeRefusal", "export_iges", false);
                reopenText = (string)Call(sw, "AssemblyExchangeRefusal", "export_step", true);
            }
            catch (Exception ex) { Check("N18-N22: the sources and the shared refusal can be read", false, ex.GetType().Name + ": " + ex.Message); return; }
            var sources = new Dictionary<string, string> { { "export", export }, { "drawing", drawing }, { "bom", bom } };
            var problems = ExchangeAndTablesProblems(sources, igesText, reopenText);
            Check("N18/N19/N20/N22: the refusals of the pre-check are the ones of the operation, the basis of the solid count, the empty columns and the false of SetSheets are said",
                  problems.Count == 0, problems.Count == 0 ? "AssemblyExchangeRefusal equal to the two Failed of ExportExchange; solids_expected_basis; EmptyColumnsWarning; SetSheetsFalseNote" : string.Join(" | ", problems));
            var changed = new[]
            {
                Tuple.Create("export", "the IGES refusal of the operation parted from the pre-check (N18)",
                             "\"I do not write the IGES of an assembly: I cannot verify", "\"I do not write IGES of an assembly: I cannot verify"),
                Tuple.Create("export", "the reopen refusal of the operation parted from the pre-check (N18)",
                             "of the solids in the file against those of the parts: remove it and ask me again.\");",
                             "of the solids in the file against those of the parts: remove it.\");"),
                Tuple.Create("export", "the basis of the count not said (N19)", "measurements[\"solids_expected_basis\"] =", "measurements[\"basis\"] ="),
                Tuple.Create("bom", "the empty columns not said (N20)", "measurements[\"warning_empty_columns\"] = emptyColumns;", ""),
                Tuple.Create("drawing", "the false not explained (N22)", "measurements[\"set_sheets_note\"] = SetSheetsFalseNote(pages, sheets);", ""),
            };
            var notRed = changed.Where(c =>
            {
                if (!sources[c.Item1].Contains(c.Item3)) return true;
                var copy = new Dictionary<string, string>(sources);
                copy[c.Item1] = copy[c.Item1].Replace(c.Item3, c.Item4);
                return ExchangeAndTablesProblems(copy, igesText, reopenText).Count == 0;
            }).Select(c => c.Item2).ToList();
            Check("N18-N22: the check turns red on each changed copy", notRed.Count == 0,
                  notRed.Count == 0 ? changed.Length + " changes, each one red" : "NOT red: " + string.Join(", ", notRed));
        }

        /// <summary>
        /// The text of the Failed statement that holds a literal starting with
        /// `start`, its literals joined; null when no Failed holds it.
        /// </summary>
        internal static string FailedTextWith(string source, string start)
        {
            string needle = "\"" + start;
            for (int at = source.IndexOf(needle, StringComparison.Ordinal); at >= 0;
                 at = source.IndexOf(needle, at + 1, StringComparison.Ordinal))
            {
                int open = source.LastIndexOf("Failed(", at, StringComparison.Ordinal);
                int close = source.IndexOf(");", at, StringComparison.Ordinal);
                if (open < 0 || close < 0) continue;
                string statement = source.Substring(open, close - open);
                const string literal = @"""((?:[^""\\]|\\.)*)""";
                // A «;» outside the literals: the text is not inside that Failed.
                if (Regex.Replace(statement, literal, "").IndexOf(';') >= 0) continue;
                return string.Concat(Regex.Matches(statement, literal).Cast<Match>().Select(m => m.Groups[1].Value));
            }
            return null;
        }

        /// <summary>What is missing of the round 6 fixes in the operations; empty when nothing.</summary>
        internal static List<string> ExchangeAndTablesProblems(Dictionary<string, string> sources, string igesText, string reopenText)
        {
            var problems = new List<string>();
            // N18: the pre-check of the plan and the operation give the same refusal.
            string igesInOperation = FailedTextWith(sources["export"], "I do not write ");
            string reopenInOperation = FailedTextWith(sources["export"], "On an assembly I do not do ");
            if (igesText == null || igesInOperation != igesText)
                problems.Add("the IGES refusal of ExportExchange is not the one the plan pre-check gives (report 8cd843 N18)");
            if (reopenText == null || reopenInOperation != reopenText)
                problems.Add("the reopen_and_compare refusal of ExportExchange is not the one the plan pre-check gives (report 8cd843 N18)");
            if (sources["export"].IndexOf("measurements[\"solids_expected_basis\"] =", StringComparison.Ordinal) < 0)
                problems.Add("the STEP of an assembly does not say what the expected solid count counts (report 8cd843 N19)");
            if (sources["bom"].IndexOf("measurements[\"warning_empty_columns\"] = emptyColumns;", StringComparison.Ordinal) < 0)
                problems.Add("a bill of materials with a column empty on every row does not say it (report 8cd843 N20)");
            if (sources["drawing"].IndexOf("measurements[\"set_sheets_note\"] = SetSheetsFalseNote(pages, sheets);", StringComparison.Ordinal) < 0)
                problems.Add("a false of SetSheets is not explained in the answer (report 8cd843 N22)");
            return problems;
        }

        /// <summary>What is wrong with the reading of one interference; empty when nothing.</summary>
        internal static List<string> ContactSourceProblems(string source)
        {
            var problems = new List<string>();
            int sheet = source.IndexOf("if (type == (int)swBodyType_e.swSheetBody) { contact = true; return true; }", StringComparison.Ordinal);
            int mass = source.IndexOf("body.GetMassProperties(1000.0)", StringComparison.Ordinal);
            if (sheet < 0 || mass < 0 || sheet > mass)
                problems.Add("a sheet body (faces that touch) is not taken as a contact before its volume is read (report 8cd843 N15)");
            if (source.IndexOf("entry[\"components\"] = pair.ToArray();", StringComparison.Ordinal) < 0 ||
                source.IndexOf("interference.Components as object[]", StringComparison.Ordinal) < 0)
                problems.Add("an interference does not name its own components (report 8cd843 N14)");
            if (source.IndexOf("entry[\"box_from_mm\"] =", StringComparison.Ordinal) < 0 ||
                source.IndexOf("body.GetBodyBox()", StringComparison.Ordinal) < 0)
                problems.Add("an interference does not say where it is (report 8cd843 N14)");
            // N16: Done marks a saved assembly changed; the answer must say it.
            int flagBefore = source.IndexOf("saveFlagBefore = model.GetSaveFlag();", StringComparison.Ordinal);
            int done = source.IndexOf("try { handler.Done(); } catch { }", StringComparison.Ordinal);
            int flagAfter = source.IndexOf("saveFlagAfter = model.GetSaveFlag();", StringComparison.Ordinal);
            if (flagBefore < 0 || done < flagBefore || flagAfter < done ||
                source.IndexOf("measurements[\"marked_unsaved\"] = true;", StringComparison.Ordinal) < 0)
                problems.Add("the save flag is not read around Done, or a check that marked the assembly unsaved does not say it (N16)");
            return problems;
        }

        /// <summary>
        /// The words a bench case expects in its verdict (the list of words its
        /// expectation on the verdict field must contain), or null.
        /// </summary>
        private static List<string> BenchVerdictWords(string folder, string caseName)
        {
            var path = Path.Combine(Root(), "Bench", "cases", folder, caseName + ".json");
            if (!File.Exists(path)) return null;
            var json = new System.Web.Script.Serialization.JavaScriptSerializer()
                .DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
            var steps = json != null && json.ContainsKey("steps") ? json["steps"] as object[] : null;
            if (steps == null) return null;
            foreach (var step in steps.OfType<Dictionary<string, object>>())
            {
                var expectations = step.ContainsKey("expect") ? step["expect"] as object[] : null;
                if (expectations == null) continue;
                foreach (var e in expectations.OfType<Dictionary<string, object>>())
                {
                    object what, words;
                    if (e.TryGetValue("what", out what) && Convert.ToString(what) == "measurements.verdict" &&
                        e.TryGetValue("includes", out words) && words is object[])
                        return ((object[])words).Select(w => Convert.ToString(w)).ToList();
                }
            }
            return null;
        }

        // ── N18: the settings, as SOLIDWORKS reads them back ─────────────────
        private static void InterferenceSettings(Type sw)
        {
            try
            {
                var fine = (string)Call(sw, "InterferenceSettingsProblem", false, (bool?)false, false, (bool?)false, (bool?)false);
                var coincidence = (string)Call(sw, "InterferenceSettingsProblem", true, (bool?)false, false, (bool?)false, (bool?)false);
                var multibody = (string)Call(sw, "InterferenceSettingsProblem", false, (bool?)false, true, (bool?)false, (bool?)false);
                var ignored = (string)Call(sw, "InterferenceSettingsProblem", false, (bool?)false, false, (bool?)false, (bool?)true);
                var unread = (string)Call(sw, "InterferenceSettingsProblem", true, null, true, null, null);
                Check("N18: a setting that reads back different from the request is a problem",
                      coincidence != null && coincidence.Contains("TreatCoincidenceAsInterference reads false") &&
                      multibody != null && multibody.Contains("IncludeMultibodyPartInterferences") &&
                      ignored != null && ignored.Contains("ShowIgnoredInterferences"),
                      (coincidence ?? "null") + " | " + (multibody ?? "null") + " | " + (ignored ?? "null"));
                Check("N18: settings read back as asked, or not read back, are no problem",
                      fine == null && unread == null, (fine ?? "null") + " | " + (unread ?? "null"));
            }
            catch (Exception ex) { Check("N18: InterferenceSettingsProblem can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── N18: the components looked at are the ones not suppressed ────────
        private static void ComponentsLookedAt(Type sw)
        {
            try
            {
                var m = sw.GetMethod("ComponentsLookedAt", Any);
                if (m == null) { Check("N18: ComponentsLookedAt is there", false, "missing on this library"); return; }
                // swComponentSuppressionState_e: Suppressed 0, FullyResolved 2, Resolved 3.
                var args = new object[] { new int?[] { 2, 0, 3 }, 0 };
                int looked = (int)m.Invoke(null, args);
                int suppressed = (int)args[1];
                var argsUnread = new object[] { new int?[] { 2, null }, 0 };
                int lookedUnread = (int)m.Invoke(null, argsUnread);
                int suppressedUnread = (int)argsUnread[1];
                Check("N18: three components, one suppressed: two looked at, one suppressed",
                      looked == 2 && suppressed == 1, looked + " looked at, " + suppressed + " suppressed");
                Check("N18: a state that cannot be read makes both counts unknown (-1)",
                      lookedUnread == -1 && suppressedUnread == -1, lookedUnread + ", " + suppressedUnread);
            }
            catch (Exception ex) { Check("N18: ComponentsLookedAt can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── M39: the array AutoBalloon5 returns, counted ─────────────────────
        private static void Balloons(Type sw)
        {
            try
            {
                int none = (int)Call(sw, "BalloonNotesReturned", (object)null);
                int two = (int)Call(sw, "BalloonNotesReturned", (object)new object[] { new object(), null, new object() });
                var agree = (string)Call(sw, "BalloonCountNote", 3, 2, 5);
                var disagree = (string)Call(sw, "BalloonCountNote", 2, 2, 5);
                var noArray = (string)Call(sw, "BalloonCountNote", -1, 2, 5);
                var uncounted = (string)Call(sw, "BalloonCountNote", 3, -1, 5);
                Check("M39: no array is -1, not zero; the notes of the array are counted",
                      none == -1 && two == 2, none + ", " + two);
                Check("M39: the returned notes and the balloons counted in the view are compared",
                      agree == null && disagree != null && disagree.Contains("returned 2 notes") &&
                      noArray != null && noArray.Contains("no array") && uncounted == null,
                      (agree ?? "null") + " | " + (disagree ?? "null") + " | " + (noArray ?? "null"));
            }
            catch (Exception ex) { Check("M39: BalloonNotesReturned and BalloonCountNote can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }

        // ── N17: only the preferences that were read go back ─────────────────
        private static void PreferenceSnapshot(Assembly asm)
        {
            try
            {
                var t = asm.GetType("Oce.Solid.Agent.Core.SwOperations+UserPreferenceSnapshot", false);
                if (t == null) { Check("N17: UserPreferenceSnapshot is there", false, "missing on this library"); return; }
                var readInteger = t.GetMethod("ReadInteger", AnyInstance);
                var readToggle = t.GetMethod("ReadToggle", AnyInstance);
                var restore = t.GetMethod("Restore", AnyInstance);
                var count = t.GetProperty("ReadCount", AnyInstance);
                Func<object> fresh = () =>
                {
                    var s = Activator.CreateInstance(t, true);
                    // Two integers, one read (quality 2) and one whose read throws;
                    // two toggles, one read (on) and one whose read throws.
                    readInteger.Invoke(s, new object[] { 1, "quality", (Func<int, int>)(p => 2) });
                    readInteger.Invoke(s, new object[] { 2, "units", (Func<int, int>)(p => { throw new InvalidOperationException("read failed"); }) });
                    readToggle.Invoke(s, new object[] { 3, "binary", (Func<int, bool>)(p => true) });
                    readToggle.Invoke(s, new object[] { 4, "info", (Func<int, bool>)(p => { throw new InvalidOperationException("read failed"); }) });
                    return s;
                };

                var snapshot = fresh();
                int read = (int)count.GetValue(snapshot);
                var written = new List<string>();
                var toggles = new Dictionary<int, bool>();
                var notBack = (List<string>)restore.Invoke(snapshot, new object[]
                {
                    (Func<int, int, bool>)((p, v) => { written.Add(p + "=" + v); return true; }),
                    (Action<int, bool>)((p, v) => { written.Add(p + "=" + v); toggles[p] = v; }),
                    (Func<int, bool>)(p => toggles.ContainsKey(p) && toggles[p]),
                });
                Check("N17: only the preferences that were read are written back",
                      read == 2 && written.Count == 2 && written.Contains("1=2") && written.Contains("3=True") &&
                      notBack.Count == 0,
                      read + " read; written: " + string.Join(", ", written) +
                      (notBack.Count > 0 ? "; not back: " + string.Join(", ", notBack) : ""));

                var failing = (List<string>)restore.Invoke(fresh(), new object[]
                {
                    (Func<int, int, bool>)((p, v) => false),
                    (Action<int, bool>)((p, v) => { }),
                    (Func<int, bool>)(p => false),
                });
                Check("N17: a setter that answers false and a toggle that does not read back are named",
                      failing.Contains("quality") && failing.Contains("binary") && failing.Count == 2,
                      string.Join(", ", failing));
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Check("N17: UserPreferenceSnapshot can be used", false, inner.GetType().Name + ": " + inner.Message);
            }
        }

        // ── L13-7: the warnings of a save, by name ───────────────────────────
        private static void SaveWarnings(Type sw, bool interop)
        {
            try
            {
                // swFileSaveWarning_e: RebuildError 1, NeedsRebuild 2,
                // ViewsNeedUpdate 4, AnimatorNeedToSolve 8.
                var zero = (string)Call(sw, "SaveWarningsAdvice", 0);
                var rebuild = (string)Call(sw, "SaveWarningsAdvice", 2);
                var errors = (string)Call(sw, "SaveWarningsAdvice", 1);
                var views = (string)Call(sw, "SaveWarningsAdvice", 4);
                var animator = (string)Call(sw, "SaveWarningsAdvice", 8);
                Check("L13-7: NeedsRebuild is 2, RebuildError 1, ViewsNeedUpdate 4, and each has its advice",
                      zero == null && animator == null &&
                      rebuild != null && rebuild.Contains("needed a rebuild") && !rebuild.Contains("rebuild errors") &&
                      errors != null && errors.Contains("rebuild errors") &&
                      views != null && views.Contains("drawing views"),
                      (rebuild ?? "null") + " | " + (errors ?? "null") + " | " + (views ?? "null"));
            }
            catch (Exception ex) { Check("L13-7: SaveWarningsAdvice can be called", false, ex.GetType().Name + ": " + ex.Message); }

            if (!interop)
            {
                NotHere("L13-7: the Warnings mask decoded by name",
                       "the names come from the interop enum, and the interop of SOLIDWORKS is not on this machine");
                return;
            }
            try
            {
                var none = (string)Call(sw, "SaveWarningsForAnswer", 0);
                var one = (string)Call(sw, "SaveWarningsForAnswer", 2);
                var two = (string)Call(sw, "SaveWarningsForAnswer", 5);
                Check("L13-7: the Warnings mask goes in the answer by name, and 0 says «none»",
                      none.StartsWith("none", StringComparison.Ordinal) &&
                      one == "swFileSaveWarning_NeedsRebuild" &&
                      two.Contains("swFileSaveWarning_RebuildError") && two.Contains("swFileSaveWarning_ViewsNeedUpdate"),
                      none + " | " + one + " | " + two);
            }
            catch (Exception ex) { Check("L13-7: SaveWarningsForAnswer can be called", false, ex.GetType().Name + ": " + ex.Message); }
        }
    }
}
