using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// Every lookup by name that goes through a reflection helper of this suite,
    /// and the ones that came back null (entry W3.1-bis.2, item 3).
    ///
    /// The helpers (`Carica.Classe`, the `Call` and `Invoke` of the contracts,
    /// `Metti`, the lambdas of `ProveIlRegistro` and `ProveLAvanzamento`) take
    /// the name from a parameter, so no text search over the sources can tell
    /// whether the name still exists: only running them can. Most of them
    /// already turn red on a null, a few fall back to null in silence (a field
    /// read as null is a «false» that nobody asked for). Here every one of
    /// them is written down, and `ReflectionTests` turns red at the end of the
    /// run if any came back null.
    /// </summary>
    internal sealed class ReflectionLog
    {
        internal static readonly ReflectionLog Global = new ReflectionLog();

        private readonly List<string> _misses = new List<string>();

        internal int Lookups { get; private set; }

        internal IList<string> Misses { get { return _misses.AsReadOnly(); } }

        internal MethodInfo Method(Type owner, string name, BindingFlags flags)
        {
            return Note(owner == null ? null : owner.GetMethod(name, flags), "method", owner, name);
        }

        internal PropertyInfo Property(Type owner, string name, BindingFlags flags)
        {
            return Note(owner == null ? null : owner.GetProperty(name, flags), "property", owner, name);
        }

        internal FieldInfo Field(Type owner, string name, BindingFlags flags)
        {
            return Note(owner == null ? null : owner.GetField(name, flags), "field", owner, name);
        }

        internal Type Nested(Type owner, string name, BindingFlags flags)
        {
            return Note(owner == null ? null : owner.GetNestedType(name, flags), "nested type", owner, name);
        }

        /// <summary>A lookup that a helper made by itself, and that found what it looked for.</summary>
        internal void Found()
        {
            Lookups++;
        }

        /// <summary>A lookup that a helper made by itself, and that came back empty.</summary>
        internal void Missing(string kind, Type owner, string name)
        {
            Lookups++;
            _misses.Add(kind + " " + (owner == null ? "(no type)" : owner.FullName) + "." + name);
        }

        /// <summary>A type looked up by its simple name in an assembly, and not found.</summary>
        internal void MissingType(Assembly assembly, string name)
        {
            Lookups++;
            _misses.Add("type " + name + " in " + (assembly == null ? "(no assembly)" : assembly.GetName().Name));
        }

        private T Note<T>(T found, string kind, Type owner, string name) where T : class
        {
            if (found == null) Missing(kind, owner, name);
            else Found();
            return found;
        }
    }

    /// <summary>
    /// THE NAMES THIS SUITE AND THE BENCH LOOK UP BY REFLECTION STILL EXIST
    /// (entry W3.1-bis.2, item 3).
    ///
    /// ── WHY ─────────────────────────────────────────────────────────────
    ///
    /// The translation renames classes, methods and fields of the library and
    /// of the bridge (W3.1-bis.6). Roslyn renames the code that calls them,
    /// and no string: a `GetMethod("Leggi")` keeps asking for the old name,
    /// gets null, and depending on the helper turns red, throws, or skips a
    /// check in silence. The compiler cannot see it, and the IL comparison
    /// sees a literal that did not change.
    ///
    /// ── TWO NETS ────────────────────────────────────────────────────────
    ///
    ///   * STATIC: every literal name handed to reflection in `Prove/`,
    ///     `Banco/` and the probes of `Controlli/` is extracted from the
    ///     sources (comments taken out, a constant prefix such as
    ///     `NS + "OperazioniSw"` resolved) and must resolve on the assemblies
    ///     in `bin\`. The literal arguments of the helpers of `Prove/` count
    ///     too.
    ///   * AT RUN TIME: the helpers write every lookup by name in
    ///     `ReflectionLog.Global`, so a name that arrives through a parameter
    ///     is checked on the path the run actually took. This group runs last
    ///     and turns red if one of them came back null.
    ///
    /// ── SEEN RED ────────────────────────────────────────────────────────
    ///
    /// Three self-tests run everywhere, also where `bin\` cannot exist: the
    /// extractor on a fake source (a commented lookup must not count, a
    /// constant prefix must be resolved), the resolver on fake names against
    /// this very assembly, and the log with a fake name passed as a parameter.
    /// </summary>
    internal static class ReflectionTests
    {
        /// <summary>
        /// Below this many literal names the extractor has gone blind (a
        /// pattern that stopped matching reads as «nothing to check»).
        /// Measured on 30/9/2026: 331 names in 27 files, of which 124 handed
        /// straight to GetType, GetMethod, GetField, GetProperty,
        /// GetNestedType or Carica.Classe, and 207 handed to a helper.
        /// </summary>
        private const int LiteralFloor = 250;

        /// <summary>
        /// Below this many logged lookups, where `bin\` exists, the helpers
        /// have stopped writing to the log. Measured on 30/9/2026: 399 in a
        /// whole run with the bridge and the library built.
        /// </summary>
        private const int LookupFloor = 300;

        private const BindingFlags Everything = BindingFlags.Public | BindingFlags.NonPublic |
                                                BindingFlags.Instance | BindingFlags.Static |
                                                BindingFlags.DeclaredOnly;

        internal sealed class LiteralName
        {
            public string Kind;   // type (full name), class (simple name), nested, member
            public string Name;
            public string File;
            public int Line;
            public bool ByHelper;

            public override string ToString()
            {
                return Kind + " " + Name + " (" + File + ":" + Line + ")";
            }
        }

        /// <summary>
        /// Runs last, right before the check on `Prove/LEGGIMI.md`: the log is
        /// only complete when every other group has run.
        /// </summary>
        internal static void All()
        {
            Gruppo("the names looked up by reflection still exist (W3.1-bis.2)");

            ExtractorSelfTest();
            ResolverSelfTest();
            LogSelfTest();
            BenchLogSelfTest();

            var names = ExtractFromTree();
            BenchReadsNoLookupInSilence();
            BenchChecksItsLogOnAFullRun();
            bool built = ResolveOnBuiltAssemblies(names);
            RuntimeLog(built);
        }

        /// <summary>
        /// A lookup of the bench read with `?.` goes on with null when its target
        /// is renamed, and the bench never says so: such a read goes through
        /// Banco/ReflectionLog.cs, which the run checks at its end. Seen red on a
        /// fake source with one silent read.
        /// </summary>
        private static readonly Regex SilentLookup = new Regex(
            @"\.(?:GetField|GetProperty|GetMethod|GetNestedType|GetEvent)\((?:[^()]|\([^()]*\))*\)\s*\?\.");

        /// <summary>
        /// THE BENCH'S OWN LOG CHECK RUNS ON A FULL BENCH (1/10/2026). banco.ps1
        /// always passes --libreria with the default path, and the bench took any
        /// --libreria for «another library»: the check was skipped on every full
        /// run. Now only a different file counts; this reads the source, and is
        /// seen red on a copy where every --libreria is «another» again.
        /// </summary>
        private static void BenchChecksItsLogOnAFullRun()
        {
            const string Label = "on a full bench (banco.ps1 passes --libreria with the default path) the bench's lookup log is checked, not skipped";
            var file = Path.Combine(Radice(), "Banco", "Program.cs");
            if (!File.Exists(file)) { Segna(Label, false, "Banco/Program.cs not found"); return; }
            var code = WithoutComments(File.ReadAllText(file));
            Func<string, bool> ok = c =>
                Regex.IsMatch(c, @"_anotherLibrary\s*=\s*!\s*SameLibraryPath\s*\(\s*\w+\s*,\s*\w+\s*\(\s*\)\s*\)\s*;") &&
                !Regex.IsMatch(c, @"_anotherLibrary\s*=\s*true\s*;");
            var mutant = Regex.Replace(code, @"_anotherLibrary\s*=\s*!\s*SameLibraryPath\s*\([^;]*;", "_anotherLibrary = true;");
            bool good = ok(code), seenRed = !ok(mutant);
            Segna(Label, good && seenRed,
                  !good ? "Banco/Program.cs does not compare the --libreria path with the default one before skipping"
                        : !seenRed ? "the check did not turn red on the copy where every --libreria is another library"
                                   : "only a different library file skips the check (seen red on the old line)");
        }

        private static void BenchReadsNoLookupInSilence()
        {
            const char Q = '"';
            var fake = "var n = o.GetType().GetField(" + Q + "Nome" + Q + ")?.GetValue(o);\n" +
                       "var m = t.GetMethod(" + Q + "Json" + Q + ", BindingFlags.Public | BindingFlags.Static)\n    ?.Invoke(null, a);";
            var logged = "var n = ReflectionLog.Global.Field(o.GetType(), " + Q + "Nome" + Q + ")?.GetValue(o);";
            bool seen = SilentLookup.Matches(WithoutComments(fake)).Count == 2 &&
                        SilentLookup.Matches(WithoutComments(logged)).Count == 0;

            var found = new List<string>();
            var folder = Path.Combine(Radice(), "Banco");
            if (Directory.Exists(folder))
                foreach (var f in Directory.GetFiles(folder, "*.cs").OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (Path.GetFileName(f) == "ReflectionLog.cs") continue;
                    var code = WithoutComments(File.ReadAllText(f));
                    foreach (Match m in SilentLookup.Matches(code))
                        found.Add(Path.GetFileName(f) + ":" + LineOf(code, m.Index));
                }
            Segna("the bench reads no reflection lookup with ?. outside its log (a fake silent read is seen)",
                  seen && found.Count == 0,
                  !seen ? "the pattern did not see the two silent reads of the fake source"
                        : found.Count == 0 ? "none in Banco/*.cs"
                                           : "read with ?., route them through ReflectionLog.Global: " + string.Join(", ", found));
        }

        // ================================================================
        // THE SELF-TESTS: every net seen red on a fake
        // ================================================================

        private static void ExtractorSelfTest()
        {
            const char Q = '"';
            var fake = new StringBuilder();
            fake.AppendLine("class Fake {");
            fake.AppendLine("  private const string NS = " + Q + "Some.Space." + Q + ";");
            fake.AppendLine("  static object Call(Type t, string method, params object[] args) {");
            fake.AppendLine("    var m = ReflectionLog.Global.Method(t, method, Any); return m; }");
            fake.AppendLine("  void M(Type t, Assembly asm) {");
            fake.AppendLine("    var a = t.GetMethod(" + Q + "FakeLiteralMethod" + Q + ", Any);");
            fake.AppendLine("    // var b = t.GetMethod(" + Q + "CommentedOut" + Q + ");");
            fake.AppendLine("    /* t.GetField(" + Q + "BlockCommented" + Q + ") */");
            fake.AppendLine("    var c = asm.GetType(NS + " + Q + "FakeType" + Q + ", true);");
            fake.AppendLine("    var d = Loader.Find(asm, " + Q + "FakeClass" + Q + ");");
            fake.AppendLine("    var e = Call(t, " + Q + "FakeHelperArgument" + Q + ", 1, 2);");
            fake.AppendLine("    var e2 = Call(t, " + Q + "Glued" + Q + " + suffix, 1);");
            fake.AppendLine("    var f = asm.GetType(" + Q + "Prefix.Only." + Q + " + name);");
            fake.AppendLine("    var g = t.GetNestedType(" + Q + "FakeNested" + Q + ", Any);");
            fake.AppendLine("    var g2 = ReflectionLog.Global.TypeIn(asm, NS + " + Q + "FakeLoggedType" + Q + ");");
            fake.AppendLine("    var g3 = ReflectionLog.Global.Field(t.GetType(), " + Q + "FakeLoggedField" + Q + ");");
            fake.AppendLine("    Func<object, string, object> read = (o, prop) =>");
            fake.AppendLine("        ReflectionLog.Global.Property(o.GetType(), prop, Any).GetValue(o);");
            fake.AppendLine("    var h = read(x, " + Q + "FakeLambdaArgument" + Q + ");");
            fake.AppendLine("    var i = " + Q + "t.GetMethod(" + Q + " + x;");
            fake.AppendLine("    if (a == null) { return; }");
            fake.AppendLine("  }");
            fake.AppendLine("}");
            fake.AppendLine("static class Loader {");
            fake.AppendLine("  internal static Type Find(Assembly asm, string simple) {");
            fake.AppendLine("    var t = asm.GetType(simple, false);");
            fake.AppendLine("    if (t == null) ReflectionLog.Global.MissingType(asm, simple);");
            fake.AppendLine("    return t; }");
            fake.AppendLine("}");

            var got = ExtractAll(new[] { new KeyValuePair<string, string>("Fake.cs", fake.ToString()) }, f => true)
                .Select(n => n.Kind + ":" + n.Name).OrderBy(s => s, StringComparer.Ordinal).ToList();
            var want = new[]
            {
                "class:FakeClass", "member:FakeHelperArgument", "member:FakeLambdaArgument", "member:FakeLiteralMethod",
                "member:FakeLoggedField", "nested:FakeNested", "type:Some.Space.FakeLoggedType", "type:Some.Space.FakeType",
            };
            Segna("the extractor finds literal names, helpers and their arguments, and skips comments (fake source)",
                  got.SequenceEqual(want, StringComparer.Ordinal),
                  string.Join(", ", got));
        }

        private static void ResolverSelfTest()
        {
            var mine = new[] { typeof(ReflectionTests).Assembly };
            Func<string, string, bool> resolves = (kind, name) =>
            {
                int unreadable;
                return Unresolved(new[] { new LiteralName { Kind = kind, Name = name, File = "fake", Line = 1 } },
                                  mine, out unreadable).Count == 0;
            };
            var wrong = new List<string>();
            if (!resolves("member", "All")) wrong.Add("the real method «All» did not resolve");
            if (resolves("member", "NoSuchMemberAnywhereInThisSuite")) wrong.Add("a fake member resolved");
            if (!resolves("type", "Prove.Contratti.ReflectionTests")) wrong.Add("the real type did not resolve");
            if (resolves("type", "Prove.Contratti.NoSuchTypeInThisSuite")) wrong.Add("a fake type resolved");
            if (!resolves("class", "ReflectionLog")) wrong.Add("the real class did not resolve by simple name");
            if (resolves("class", "NoSuchClassInThisSuite")) wrong.Add("a fake class resolved");
            if (!resolves("nested", "LiteralName")) wrong.Add("the real nested type did not resolve");
            if (resolves("nested", "ReflectionLog")) wrong.Add("a top-level type resolved as nested");
            Segna("a literal name that does not exist is red, one that exists is green (fake names on this assembly)",
                  wrong.Count == 0, wrong.Count == 0 ? "8 of 8 as expected" : string.Join("; ", wrong));
        }

        private static void LogSelfTest()
        {
            var log = new ReflectionLog();
            var passed = NameFromAParameter();
            var none = log.Method(typeof(ReflectionTests), passed, BindingFlags.NonPublic | BindingFlags.Static);
            var real = log.Method(typeof(ReflectionTests), "All", BindingFlags.NonPublic | BindingFlags.Static);
            bool ok = none == null && real != null && log.Lookups == 2 && log.Misses.Count == 1 &&
                      log.Misses[0].IndexOf(passed, StringComparison.Ordinal) >= 0 && !Verdict(log, true).Item1;
            Segna("a lookup by a name passed as a parameter that comes back null is logged, and the log is red (fake)",
                  ok, log.Lookups + " lookups, misses: " + string.Join("; ", log.Misses));
        }

        /// <summary>
        /// The log of the bench (Banco/ReflectionLog.cs, linked here): a field and
        /// a type asked for by names passed as parameters come back null and are
        /// logged, a real member is found, and the verdict is red; a clean log has
        /// no verdict. The bench runs this log at the end of every run, where
        /// SOLIDWORKS is; here it is seen red without it.
        /// </summary>
        private static void BenchLogSelfTest()
        {
            var clean = new ProvaSaldatura.ReflectionLog();
            var found = clean.Method(typeof(ReflectionTests), "All", BindingFlags.NonPublic | BindingFlags.Static);
            var foundType = clean.TypeIn(typeof(ReflectionTests).Assembly, typeof(ReflectionTests).FullName);
            var log = new ProvaSaldatura.ReflectionLog();
            var passed = NameFromAParameter();
            var noField = log.Field(typeof(ReflectionTests), passed);
            var noType = log.TypeIn(typeof(ReflectionTests).Assembly, "Prove.Contratti." + passed);
            var real = log.Method(typeof(ReflectionTests), "All", BindingFlags.NonPublic | BindingFlags.Static);
            var verdict = log.Verdict();
            bool ok = found != null && foundType != null && clean.Verdict() == null && clean.Lookups == 2 &&
                      noField == null && noType == null && real != null && log.Lookups == 3 && log.Misses.Count == 2 &&
                      verdict != null && verdict.IndexOf(passed, StringComparison.Ordinal) >= 0;
            Segna("the bench's own lookup log records a field and a type that came back null, and is red (fake)",
                  ok, log.Lookups + " lookups; " + (verdict ?? "no verdict") + "; clean log: " + (clean.Verdict() ?? "no verdict"));
        }

        private static string NameFromAParameter()
        {
            return "NoSuchMethod" + "PassedAsAParameter";
        }

        // ================================================================
        // THE STATIC NET
        //
        // The patterns name only the reflection API and ReflectionLog: the
        // helpers of this repository are FOUND, not listed. A helper is a
        // method or a lambda of Prove/ that hands one of its parameters to a
        // lookup by name, and at its call sites the literal in that position
        // is a name to resolve. A helper written tomorrow is covered the day
        // it is written.
        // ================================================================

        private sealed class Helper
        {
            public string File, Class, Name, Kind;
            public int Index;
        }

        private static readonly Regex TypeByFullName = new Regex(
            @"\.GetType\(\s*(?:(?<prefix>[A-Za-z_]\w*)\s*\+\s*)?""(?<name>[^""\\]+)""\s*[,)]");
        private static readonly Regex NestedByName = new Regex(
            @"\.GetNestedType\(\s*""(?<name>[A-Za-z_]\w*)""\s*[,)]");
        private static readonly Regex MemberByName = new Regex(
            @"\.(?:GetMethod|GetProperty|GetField|GetEvent|GetMember|InvokeMember)\(\s*""(?<name>[A-Za-z_]\w*)""\s*[,)]");
        private static readonly Regex LogByName = new Regex(
            @"\bReflectionLog\.Global\.(?:Method|Property|Field|Nested)\(\s*[^,;]+,\s*""(?<name>[A-Za-z_]\w*)""\s*[,)]");
        // the bench log asks for a type by its full name (Banco/ReflectionLog.cs)
        private static readonly Regex LoggedTypeByFullName = new Regex(
            @"\bReflectionLog\.Global\.TypeIn\(\s*[^,;]+,\s*(?:(?<prefix>[A-Za-z_]\w*)\s*\+\s*)?""(?<name>[^""\\]+)""\s*[,)]");
        private static readonly Regex ConstantString = new Regex(
            @"\bconst\s+string\s+(?<id>[A-Za-z_]\w*)\s*=\s*""(?<value>[^""\\]*)""\s*;");

        // a method declaration (its body follows) and a lambda assigned to a name
        private static readonly Regex MethodDeclaration = new Regex(
            @"(?<![\w.])(?<name>[A-Za-z_]\w*)(?:<[^<>()]*>)?\s*\((?<params>[^()]*)\)\s*\{");
        private static readonly Regex LambdaDeclaration = new Regex(
            @"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*=\s*\((?<params>[^()]*)\)\s*=>");
        private static readonly Regex ClassDeclaration = new Regex(@"\bclass\s+(?<name>[A-Za-z_]\w*)");
        private const string Argument = @"(?:[^,()]|\((?:[^()]|\([^()]*\))*\))+";
        // a lookup whose name is a parameter, inside a body
        private static readonly Regex[] ByParameter =
        {
            new Regex(@"\bReflectionLog\.Global\.(?<kind>Method|Property|Field|Nested)\(\s*" + Argument + @",\s*(?<p>[A-Za-z_]\w*)\s*,"),
            new Regex(@"\bReflectionLog\.Global\.(?<kind>Missing)\(\s*""[^""]*""\s*,\s*" + Argument + @",\s*(?<p>[A-Za-z_]\w*)\s*\)"),
            new Regex(@"\bReflectionLog\.Global\.(?<kind>MissingType)\(\s*" + Argument + @",\s*(?<p>[A-Za-z_]\w*)\s*\)"),
            new Regex(@"\.(?<kind>GetMethod|GetProperty|GetField|GetNestedType)\(\s*(?<p>[A-Za-z_]\w*)\s*[,)]"),
        };
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "return", "new",
            "typeof", "sizeof", "nameof", "checked", "unchecked", "default", "delegate", "base", "this", "else",
        };

        private static List<LiteralName> ExtractFromTree()
        {
            var root = Radice();
            var files = new List<string>();
            foreach (var folder in new[] { "Prove", "Banco" })
            {
                var d = Path.Combine(root, folder);
                if (Directory.Exists(d)) files.AddRange(Directory.GetFiles(d, "*.cs", SearchOption.TopDirectoryOnly));
            }
            var probes = Path.Combine(root, "Controlli");
            if (Directory.Exists(probes))
                foreach (var d in Directory.GetDirectories(probes))
                    files.AddRange(Directory.GetFiles(d, "*.cs", SearchOption.TopDirectoryOnly));

            var sources = new List<KeyValuePair<string, string>>();
            var errors = new List<string>();
            foreach (var f in files.OrderBy(x => x, StringComparer.Ordinal))
            {
                var relative = f.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                // this file looks names up in its own assembly, on purpose
                if (relative == "Prove/ReflectionTests.cs") continue;
                try { sources.Add(new KeyValuePair<string, string>(relative, File.ReadAllText(f))); }
                catch (Exception ex) { errors.Add(relative + ": " + ex.Message); }
            }
            List<string> helperNames;
            var all = ExtractAll(sources, file => file.StartsWith("Prove/", StringComparison.Ordinal), out helperNames);
            int inFiles = all.Select(n => n.File).Distinct().Count();
            Segna("the literal names handed to reflection are extracted from the sources",
                  errors.Count == 0 && all.Count >= LiteralFloor,
                  errors.Count > 0 ? "unreadable: " + string.Join("; ", errors)
                  : all.Count + " names in " + inFiles + " files: " + all.Count(n => !n.ByHelper) +
                    " handed straight to reflection, " + all.Count(n => n.ByHelper) + " to a helper (" +
                    string.Join(", ", all.GroupBy(n => n.Kind).OrderBy(g => g.Key, StringComparer.Ordinal)
                                         .Select(g => g.Count() + " " + g.Key)) +
                    "), floor " + LiteralFloor + "; helpers found: " + string.Join(", ", helperNames));
            return all;
        }

        /// <summary>
        /// The literal names of a set of sources. `withHelpers` says in which
        /// files helpers are looked for, and their call sites read.
        /// </summary>
        internal static List<LiteralName> ExtractAll(IList<KeyValuePair<string, string>> sources, Func<string, bool> withHelpers)
        {
            List<string> unused;
            return ExtractAll(sources, withHelpers, out unused);
        }

        internal static List<LiteralName> ExtractAll(IList<KeyValuePair<string, string>> sources, Func<string, bool> withHelpers,
                                                     out List<string> helperNames)
        {
            var codes = sources.Select(s => new KeyValuePair<string, string>(s.Key, WithoutComments(s.Value))).ToList();
            var helpers = new List<Helper>();
            foreach (var c in codes) if (withHelpers(c.Key)) helpers.AddRange(FindHelpers(c.Value, c.Key));
            helperNames = helpers.Select(h => (h.Class == null ? "" : h.Class + ".") + h.Name + "#" + h.Index)
                                 .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

            var found = new List<LiteralName>();
            foreach (var c in codes)
            {
                found.AddRange(DirectLiterals(c.Value, c.Key));
                if (!withHelpers(c.Key)) continue;
                foreach (var h in helpers)
                {
                    if (h.File == c.Key)
                        found.AddRange(CallSites(c.Value, c.Key, new Regex(@"(?<![\w.])" + h.Name + @"\s*\("), h));
                    if (h.Class != null)
                        found.AddRange(CallSites(c.Value, c.Key, new Regex(@"\b" + h.Class + @"\." + h.Name + @"\s*\("), h));
                }
            }
            return found;
        }

        private static List<LiteralName> DirectLiterals(string code, string file)
        {
            var constants = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match m in ConstantString.Matches(code))
                constants[m.Groups["id"].Value] = m.Groups["value"].Value;

            var found = new List<LiteralName>();
            Action<Regex, string> scan = (re, kind) =>
            {
                foreach (Match m in re.Matches(code))
                {
                    var name = m.Groups["name"].Value;
                    if (kind == "type" && m.Groups["prefix"].Success)
                    {
                        string prefix;
                        if (!constants.TryGetValue(m.Groups["prefix"].Value, out prefix)) continue;
                        name = prefix + name;
                    }
                    found.Add(new LiteralName { Kind = kind, Name = name, File = file, Line = LineOf(code, m.Index) });
                }
            };
            scan(TypeByFullName, "type");
            scan(LoggedTypeByFullName, "type");
            scan(NestedByName, "nested");
            scan(MemberByName, "member");
            scan(LogByName, "member");
            return found;
        }

        private static IEnumerable<Helper> FindHelpers(string code, string file)
        {
            var classes = ClassDeclaration.Matches(code).Cast<Match>().ToList();
            Func<int, string> classAt = offset =>
            {
                var c = classes.LastOrDefault(m => m.Index < offset);
                return c == null ? null : c.Groups["name"].Value;
            };
            var declarations = MethodDeclaration.Matches(code).Cast<Match>().Select(m => Tuple.Create(m, true))
                .Concat(LambdaDeclaration.Matches(code).Cast<Match>().Select(m => Tuple.Create(m, false)));
            foreach (var d in declarations)
            {
                var m = d.Item1;
                var name = m.Groups["name"].Value;
                if (Keywords.Contains(name)) continue;
                if (d.Item2 && Regex.IsMatch(code.Substring(Math.Max(0, m.Index - 5), Math.Min(5, m.Index)), @"\bnew\s*$")) continue;
                var parameters = ParameterNames(m.Groups["params"].Value, d.Item2);
                if (parameters == null || parameters.Count == 0) continue;
                var body = Body(code, m.Index + m.Length, d.Item2);
                foreach (var re in ByParameter)
                {
                    var hit = re.Matches(body).Cast<Match>().FirstOrDefault(x => parameters.Contains(x.Groups["p"].Value));
                    if (hit == null) continue;
                    var kind = hit.Groups["kind"].Value;
                    yield return new Helper
                    {
                        File = file,
                        Class = classAt(m.Index),
                        Name = name,
                        Index = parameters.IndexOf(hit.Groups["p"].Value),
                        Kind = kind == "MissingType" ? "class" : kind == "Nested" || kind == "GetNestedType" ? "nested" : "member",
                    };
                    break;
                }
            }
        }

        /// <summary>
        /// The names of a parameter list, or null when it is not one: in a
        /// method each parameter has a type and a name («if (a == b)» has not).
        /// </summary>
        private static List<string> ParameterNames(string list, bool typed)
        {
            var names = new List<string>();
            if (list.Trim().Length == 0) return names;
            foreach (var raw in SplitArguments(list))
            {
                var p = raw.Split('=')[0].Trim();
                var m = Regex.Match(p, typed ? @"^(?:(?:params|ref|out|in|this)\s+)?[\w.<>\[\],?\s]+?\s+(?<n>[A-Za-z_]\w*)$"
                                             : @"^(?:[\w.<>\[\],?]+\s+)?(?<n>[A-Za-z_]\w*)$");
                if (!m.Success) return null;
                names.Add(m.Groups["n"].Value);
            }
            return names;
        }

        /// <summary>The body after a declaration: a block, or a lambda expression up to its end.</summary>
        private static string Body(string code, int from, bool block)
        {
            int i = from;
            if (block) i--; // the match ends right after the opening brace
            else
            {
                while (i < code.Length && char.IsWhiteSpace(code[i])) i++;
                if (i >= code.Length) return "";
                if (code[i] != '{')
                {
                    int depth = 0, j = i;
                    for (; j < code.Length; j++)
                    {
                        char c = code[j];
                        if (c == '"') { j = StringEnd(code, j + 1, false, false); continue; }
                        if (c == '(' || c == '{' || c == '[') depth++;
                        else if (c == ')' || c == '}' || c == ']') { if (depth == 0) break; depth--; }
                        else if ((c == ';' || c == ',') && depth == 0) break;
                    }
                    return code.Substring(i, j - i);
                }
            }
            int level = 0, k = i;
            for (; k < code.Length; k++)
            {
                char c = code[k];
                if (c == '"') { k = StringEnd(code, k + 1, false, false); continue; }
                if ((c == '@' || c == '$') && k + 1 < code.Length && code[k + 1] == '"')
                { k = StringEnd(code, k + 2, c == '@', c == '$'); continue; }
                if (c == '\'') { k++; while (k < code.Length && code[k] != '\'') { if (code[k] == '\\') k++; k++; } continue; }
                if (c == '{') level++;
                else if (c == '}') { level--; if (level == 0) break; }
            }
            return code.Substring(i, Math.Min(code.Length, k + 1) - i);
        }

        private static IEnumerable<LiteralName> CallSites(string code, string file, Regex call, Helper helper)
        {
            foreach (Match m in call.Matches(code))
            {
                int open = m.Index + m.Length - 1;
                int close = Closing(code, open);
                if (close < 0) continue;
                var args = SplitArguments(code.Substring(open + 1, close - open - 1));
                if (helper.Index >= args.Count) continue;
                var literal = Regex.Match(args[helper.Index].Trim(), @"^""(?<name>[A-Za-z_]\w*)""$");
                if (!literal.Success) continue;
                yield return new LiteralName
                {
                    Kind = helper.Kind, Name = literal.Groups["name"].Value, File = file,
                    Line = LineOf(code, m.Index), ByHelper = true,
                };
            }
        }

        /// <summary>The offset of the parenthesis that closes the one at `open`, or -1.</summary>
        private static int Closing(string code, int open)
        {
            int depth = 0;
            for (int k = open; k < code.Length; k++)
            {
                char c = code[k];
                if (c == '"') { k = StringEnd(code, k + 1, false, false); continue; }
                if ((c == '@' || c == '$') && k + 1 < code.Length && code[k + 1] == '"')
                { k = StringEnd(code, k + 2, c == '@', c == '$'); continue; }
                if (c == '\'') { k++; while (k < code.Length && code[k] != '\'') { if (code[k] == '\\') k++; k++; } continue; }
                if (c == '(') depth++;
                else if (c == ')') { depth--; if (depth == 0) return k; }
            }
            return -1;
        }

        /// <summary>
        /// The arguments of a call (or the parameters of a declaration), split
        /// at the commas that are not nested. A «&lt;» counts as an opening
        /// only between two name characters, as in «List&lt;string&gt;»; a
        /// comparison does not.
        /// </summary>
        private static List<string> SplitArguments(string s)
        {
            var parts = new List<string>();
            int depth = 0, angle = 0, from = 0;
            for (int k = 0; k < s.Length; k++)
            {
                char c = s[k];
                if (c == '"') { k = StringEnd(s, k + 1, false, false); continue; }
                if ((c == '@' || c == '$') && k + 1 < s.Length && s[k + 1] == '"')
                { k = StringEnd(s, k + 2, c == '@', c == '$'); continue; }
                if (c == '\'') { k++; while (k < s.Length && s[k] != '\'') { if (s[k] == '\\') k++; k++; } continue; }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == '<' && k > 0 && k + 1 < s.Length && char.IsLetterOrDigit(s[k - 1]) && char.IsLetter(s[k + 1])) angle++;
                else if (c == '>' && angle > 0) angle--;
                else if (c == ',' && depth == 0 && angle == 0) { parts.Add(s.Substring(from, k - from)); from = k + 1; }
            }
            parts.Add(s.Substring(from));
            return parts;
        }

        private static int LineOf(string text, int offset)
        {
            int line = 1;
            for (int i = 0; i < offset && i < text.Length; i++) if (text[i] == '\n') line++;
            return line;
        }

        /// <summary>
        /// The source with every comment turned into spaces (new lines kept, so
        /// line numbers do not move). Strings are kept as they are: the names
        /// are in them.
        /// </summary>
        internal static string WithoutComments(string s)
        {
            var o = s.ToCharArray();
            int n = s.Length, i = 0;
            while (i < n)
            {
                char c = s[i], d = i + 1 < n ? s[i + 1] : '\0';
                if (c == '/' && d == '/')
                {
                    while (i < n && s[i] != '\n') { o[i] = ' '; i++; }
                    continue;
                }
                if (c == '/' && d == '*')
                {
                    int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    end = end < 0 ? n : end + 2;
                    for (int k = i; k < end; k++) if (o[k] != '\n') o[k] = ' ';
                    i = end;
                    continue;
                }
                if (c == '\'')
                {
                    i++;
                    while (i < n && s[i] != '\'' && s[i] != '\n') { if (s[i] == '\\') i++; i++; }
                    i++;
                    continue;
                }
                if ((c == '@' || c == '$') && (d == '"' || ((d == '@' || d == '$') && i + 2 < n && s[i + 2] == '"')))
                {
                    bool verbatim = c == '@' || d == '@', interpolated = c == '$' || d == '$';
                    int q = d == '"' ? i + 1 : i + 2;
                    i = StringEnd(s, q + 1, verbatim, interpolated) + 1;
                    continue;
                }
                if (c == '"') { i = StringEnd(s, i + 1, false, false) + 1; continue; }
                i++;
            }
            return new string(o);
        }

        /// <summary>The offset of the closing quote of a C# string literal.</summary>
        private static int StringEnd(string s, int i, bool verbatim, bool interpolated)
        {
            int n = s.Length;
            for (int j = i; j < n; j++)
            {
                char c = s[j];
                if (interpolated && c == '{')
                {
                    if (j + 1 < n && s[j + 1] == '{') { j++; continue; }
                    int depth = 1, k = j + 1;
                    for (; k < n && depth > 0; k++)
                    {
                        char e = s[k];
                        if (e == '{') depth++;
                        else if (e == '}') { depth--; if (depth == 0) break; }
                        else if (e == '"') k = StringEnd(s, k + 1, false, false);
                        else if ((e == '@' || e == '$') && k + 1 < n && s[k + 1] == '"')
                            k = StringEnd(s, k + 2, e == '@', e == '$');
                    }
                    j = k;
                    continue;
                }
                if (verbatim)
                {
                    if (c == '"') { if (j + 1 < n && s[j + 1] == '"') { j++; continue; } return j; }
                    continue;
                }
                if (c == '\\') { j++; continue; }
                if (c == '"' || c == '\n') return j;
            }
            return n;
        }

        /// <summary>
        /// The names that resolve on none of the assemblies. A member resolves
        /// when some type of the assemblies declares a member with that name;
        /// a type by its full name; a class by its simple name; a nested type
        /// by the simple name of a nested type.
        /// </summary>
        internal static List<LiteralName> Unresolved(IEnumerable<LiteralName> names, IList<Assembly> assemblies,
                                                     out int unreadableTypes)
        {
            var types = new List<Type>();
            foreach (var a in assemblies)
            {
                try { types.AddRange(a.GetTypes()); }
                catch (ReflectionTypeLoadException ex) { types.AddRange(ex.Types.Where(t => t != null)); }
            }
            var simple = new HashSet<string>(types.Where(t => !t.IsNested).Select(t => t.Name), StringComparer.Ordinal);
            var nested = new HashSet<string>(types.Where(t => t.IsNested).Select(t => t.Name), StringComparer.Ordinal);
            var members = new HashSet<string>(StringComparer.Ordinal);
            int unreadable = 0;
            foreach (var t in types)
            {
                try { foreach (var m in t.GetMembers(Everything)) members.Add(m.Name); }
                catch (Exception) { unreadable++; }
            }
            unreadableTypes = unreadable;

            var missing = new List<LiteralName>();
            foreach (var n in names)
            {
                bool ok;
                switch (n.Kind)
                {
                    case "type": ok = assemblies.Any(a => a.GetType(n.Name, false) != null); break;
                    case "class": ok = simple.Contains(n.Name) || nested.Contains(n.Name); break;
                    case "nested": ok = nested.Contains(n.Name); break;
                    default: ok = members.Contains(n.Name); break;
                }
                if (!ok) missing.Add(n);
            }
            return missing;
        }

        /// <summary>True when the assemblies were there to resolve on.</summary>
        private static bool ResolveOnBuiltAssemblies(List<LiteralName> names)
        {
            const string Rule = "every literal name resolves on the built bridge, library and catalogue exporter";
            var bin = Path.Combine(Radice(), "bin");
            var files = new[] { "oce-solid-agent-mcp.exe", "Riscontro.Disegno.dll", "Riscontro.Catalogo.exe" }
                .Select(f => Path.Combine(bin, f)).ToList();
            var absent = files.Where(f => !ProdottoDaBuild(f)).ToList();
            if (absent.Count > 0)
            {
                if (!IlPonteSiPotevaCompilare())
                {
                    NonQui(Rule, "SOLIDWORKS is not on this machine, so bin\\ cannot be built: the names are " +
                                 "extracted and counted above, and resolved where the assemblies exist.");
                    return false;
                }
                Segna(Rule, false, "missing " + string.Join(", ", absent.Select(Path.GetFileName)) +
                                   ": SOLIDWORKS is on this machine, so only .\\build.ps1 is missing");
                return false;
            }

            List<LiteralName> missing;
            int unreadable;
            try
            {
                var assemblies = new List<Assembly> { Carica.Ponte(files[0]) };
                assemblies.Add(Assembly.Load(File.ReadAllBytes(files[1])));
                assemblies.Add(Assembly.Load(File.ReadAllBytes(files[2])));
                // THE INSTALLER TOO, WHEN IT IS BUILT (H19 review): the
                // contracts look up its rules by name (H19InstallerContract).
                // build.ps1 writes it beside the bridge; without it those names
                // stay unresolved here, as they should.
                var installer = Path.Combine(bin, "Installa.exe");
                if (ProdottoDaBuild(installer)) assemblies.Add(Assembly.Load(File.ReadAllBytes(installer)));
                missing = Unresolved(names, assemblies, out unreadable);
            }
            catch (Exception ex)
            {
                Segna(Rule, false, ex.GetType().Name + ": " + ex.Message);
                return true;
            }
            Segna(Rule, missing.Count == 0,
                  missing.Count == 0
                      ? names.Count + " names resolved" + (unreadable > 0 ? " (" + unreadable + " types whose members could not be read)" : "")
                      : missing.Count + " do not resolve: " + string.Join("; ", missing.Take(12)) +
                        (unreadable > 0 ? " (and " + unreadable + " types whose members could not be read)" : ""));
            return true;
        }

        // ================================================================
        // THE NET AT RUN TIME
        // ================================================================

        private static void RuntimeLog(bool built)
        {
            const string Rule = "no lookup by name through the reflection helpers came back null in this run";
            var log = ReflectionLog.Global;
            if (log.Lookups == 0 && !built)
            {
                NonQui(Rule, "no lookup ran here: the groups that look up names need bin\\, which cannot be " +
                             "built without SOLIDWORKS. The log is checked where they run.");
                return;
            }
            var verdict = Verdict(log, built);
            Segna(Rule, verdict.Item1, verdict.Item2);
        }

        private static Tuple<bool, string> Verdict(ReflectionLog log, bool built)
        {
            if (log.Misses.Count > 0)
                return Tuple.Create(false, log.Misses.Count + " of " + log.Lookups + " came back null: " +
                                           string.Join("; ", log.Misses.Distinct().Take(12)));
            if (built && log.Lookups < LookupFloor)
                return Tuple.Create(false, "only " + log.Lookups + " lookups were logged, floor " + LookupFloor +
                                           ": a helper stopped writing to the log, or a group stopped running");
            return Tuple.Create(true, log.Lookups + " lookups logged, none null");
        }
    }
}
