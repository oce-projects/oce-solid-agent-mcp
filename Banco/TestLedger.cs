using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ProvaSaldatura
{
    /// <summary>
    /// EVERY TEST OF A BENCH RUN, BY IDENTITY, AND THE EXPECTATIONS CHECKED PER
    /// CASE (entry W3.1-bis.2, item 7).
    ///
    /// ── WHY AN IDENTITY THAT IS NOT THE LABEL ───────────────────────────────
    ///
    /// The translation of the repository changes the label of almost every
    /// test (W3.1-bis.8), and the comparison "same outcome, test by test" with
    /// the reference runs of W3.1-bis.2 must survive it. A test is therefore
    /// named by WHERE it is recorded: `File.cs:Method:ordinal`, the ordinal
    /// being the position of the recording call among the recording calls of
    /// that method in that file, in source order (methods of the same name in
    /// one file share the count, so the identity stays unique). Translating a
    /// comment moves lines and keeps ordinals; renaming a method or a file goes
    /// through the name map.
    ///
    /// One call site records many tests when it runs in a loop (every case
    /// runs through the same lines), so the identity also carries the SUBJECT
    /// when one is open (`@case_name`) and, from the second time the same site
    /// records for the same subject, the occurrence (`#2`).
    ///
    /// ── WHERE THE ORDINAL COMES FROM ────────────────────────────────────────
    ///
    /// The compiler gives each recording call its file, member and line
    /// (caller information attributes). The ordinal needs the other calls of
    /// the same member, so the source is read and scanned (<see cref="SourceScan"/>),
    /// and the answer is CROSS-CHECKED: the scan must find a recording call
    /// starting on that line, inside a member with the name the compiler gave.
    /// When it does not (a source edited after the build, a declaration the
    /// scan does not understand), the test stays in the ledger as UNRESOLVED
    /// with the reason, and the bench says so in a test of its own. An
    /// identity that is guessed would be worse than one that is missing.
    ///
    /// This file is compiled by the bench and by Prove.Contratti, whose
    /// BenchReportTests see every refusal fire on sources written on purpose.
    ///
    /// ── NAMED LIST TYPES, AND WHY ───────────────────────────────────────────
    ///
    /// The obsolete-call scanner of Prove.Contratti knows a receiver by a
    /// simple type name: an `Add` on a generic list it cannot type is an
    /// undecided call, frozen by count. The lists here have a name of their
    /// own (TestList, CaseList, MemberList, CallList) so that every call on
    /// them is decided, and the members avoid the names that are obsolete on
    /// some SOLIDWORKS interface (Name, Text, Label, Value...).
    /// </summary>
    internal sealed class TestLedger
    {
        internal const string Ok = "ok";
        internal const string Failed = "failed";
        internal const string Skipped = "skipped";
        internal const string BenchError = "bench_error";

        /// <summary>One recorded test.</summary>
        internal sealed class Test
        {
            public string Id;
            public string File;
            public string Method;
            public int? Ordinal;
            public int Line;
            public string Subject;
            public int Occurrence;
            public string Outcome;
            public string Group;
            /// <summary>The label the test printed: kept for the reader, never part of the identity.</summary>
            public string Caption;
            /// <summary>Null when the identity is resolved; the reason otherwise.</summary>
            public string Unresolved;
        }

        /// <summary>One case of the run, and what it checked.</summary>
        internal sealed class CaseRecord
        {
            public string CaseName;
            public string Folder;
            public string Kind;
            public string Outcome;
            public int ExpectationsAttempted;
            public int ExpectationsChecked;
            public readonly Dictionary<string, int> Tests = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { Ok, 0 }, { Failed, 0 }, { Skipped, 0 }, { BenchError, 0 },
            };
        }

        internal sealed class TestList : List<Test> { }

        internal sealed class CaseList : List<CaseRecord> { }

        private readonly HashSet<string> _recorders;
        private readonly Func<string, string> _readSource;
        private readonly Dictionary<string, SourceScan> _scans =
            new Dictionary<string, SourceScan>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _unreadable =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _seen = new Dictionary<string, int>(StringComparer.Ordinal);

        internal readonly TestList Tests = new TestList();
        internal readonly CaseList Cases = new CaseList();

        private CaseRecord _case;
        private string _subject;

        /// <param name="recorders">The names of the methods that record a test.</param>
        /// <param name="readSource">From the caller file path the compiler gave, the
        /// source text, or null when it cannot be read.</param>
        internal TestLedger(IEnumerable<string> recorders, Func<string, string> readSource)
        {
            _recorders = new HashSet<string>(recorders, StringComparer.Ordinal);
            _readSource = readSource;
        }

        internal int UnresolvedCount
        {
            get { return Tests.Count(t => t.Unresolved != null); }
        }

        /// <summary>
        /// Scans a source now instead of at its first test: the scan must see the
        /// source the binary was built from, and the earlier it is read, the
        /// smaller the window for an edit made while the bench runs.
        /// </summary>
        internal void ScanSource(string path)
        {
            ScanOf(path);
        }

        /// <summary>Records one test and resolves its identity.</summary>
        internal Test Record(string callerFile, string member, int line, string outcome, string caption, string group)
        {
            var t = new Test
            {
                File = Path.GetFileName(callerFile ?? ""),
                Method = member ?? "",
                Line = line,
                Subject = _subject,
                Outcome = outcome,
                Group = group,
                Caption = caption,
            };

            var scan = ScanOf(callerFile);
            if (scan == null)
            {
                string why;
                _unreadable.TryGetValue(t.File, out why);
                t.Unresolved = "the source " + t.File + " cannot be read" + (why == null ? "" : ": " + why);
            }
            else
            {
                var call = scan.CallAt(line);
                if (call == null)
                    t.Unresolved = "no recording call starts on line " + line + " of " + t.File +
                                   " (a source edited after the build?)";
                else if (call.In == null)
                    t.Unresolved = "line " + line + " of " + t.File + " is inside no member the scan understands";
                else if (!string.Equals(call.In.Identifier, t.Method, StringComparison.Ordinal))
                    t.Unresolved = "the scan puts line " + line + " of " + t.File + " in «" + call.In.Identifier +
                                   "», the compiler says «" + t.Method + "»";
                else
                    t.Ordinal = call.Ordinal;
            }

            var site = t.File + ":" + t.Method + ":" +
                       (t.Ordinal.HasValue ? t.Ordinal.GetValueOrDefault().ToString(CultureInfo.InvariantCulture)
                                           : "L" + line.ToString(CultureInfo.InvariantCulture)) +
                       (t.Subject == null ? "" : "@" + t.Subject);
            int before;
            _seen.TryGetValue(site, out before);
            t.Occurrence = before + 1;
            _seen[site] = t.Occurrence;
            t.Id = site + (t.Occurrence > 1 ? "#" + t.Occurrence.ToString(CultureInfo.InvariantCulture) : "");

            Tests.Add(t);
            if (_case != null)
            {
                int n;
                _case.Tests.TryGetValue(outcome, out n);
                _case.Tests[outcome] = n + 1;
            }
            return t;
        }

        /// <summary>
        /// Opens a case: the tests recorded until the scope closes carry its
        /// name, and its expectations are counted on it. The caller sets
        /// <see cref="CaseScope.Outcome"/> before the scope closes.
        /// </summary>
        internal CaseScope OpenCase(string caseName, string folder, string kind)
        {
            var record = new CaseRecord { CaseName = caseName, Folder = folder, Kind = kind };
            Cases.Add(record);
            return new CaseScope(this, record);
        }

        /// <summary>A subject without a case: the tests recorded until it closes carry its name.</summary>
        internal IDisposable About(string subject)
        {
            return new SubjectScope(this, subject);
        }

        /// <summary>An expectation of the open case was attempted. Outside a case it counts nowhere.</summary>
        internal void ExpectationAttempted()
        {
            if (_case != null) _case.ExpectationsAttempted++;
        }

        /// <summary>An expectation of the open case was measured and compared (green or red).</summary>
        internal void ExpectationChecked()
        {
            if (_case != null) _case.ExpectationsChecked++;
        }

        internal sealed class CaseScope : IDisposable
        {
            private readonly TestLedger _ledger;
            private readonly CaseRecord _record;
            private readonly CaseRecord _previousCase;
            private readonly string _previousSubject;

            /// <summary>green, red, skipped, bench_error, or the outcome of a regression case.</summary>
            public string Outcome;

            /// <summary>Whether a bench error was recorded inside this case.</summary>
            public bool HasBenchErrors
            {
                get { return _record.Tests[BenchError] > 0; }
            }

            internal CaseScope(TestLedger ledger, CaseRecord record)
            {
                _ledger = ledger;
                _record = record;
                _previousCase = ledger._case;
                _previousSubject = ledger._subject;
                ledger._case = record;
                ledger._subject = record.CaseName;
            }

            public void Dispose()
            {
                // AN OUTCOME NOBODY SET IS SAID, NOT GUESSED: a case whose scope
                // closed on an exception has no green or red to report.
                _record.Outcome = Outcome ?? "not_reported";
                _ledger._case = _previousCase;
                _ledger._subject = _previousSubject;
            }
        }

        private sealed class SubjectScope : IDisposable
        {
            private readonly TestLedger _ledger;
            private readonly string _previous;

            internal SubjectScope(TestLedger ledger, string subject)
            {
                _ledger = ledger;
                _previous = ledger._subject;
                ledger._subject = subject;
            }

            public void Dispose() { _ledger._subject = _previous; }
        }

        private SourceScan ScanOf(string callerFile)
        {
            var file = Path.GetFileName(callerFile ?? "");
            if (file.Length == 0) return null;
            SourceScan scan;
            if (_scans.TryGetValue(file, out scan)) return scan;
            if (_unreadable.ContainsKey(file)) return null;
            string source;
            try { source = _readSource(callerFile); }
            catch (Exception ex) { _unreadable[file] = ex.Message; return null; }
            if (source == null) { _unreadable[file] = "not found"; return null; }
            scan = SourceScan.Of(source, _recorders);
            _scans[file] = scan;
            return scan;
        }

        /// <summary>
        /// The ledger as JSON: one test per line, one case per line, so that two
        /// runs can be compared with a plain diff as well as by identity.
        /// </summary>
        internal string Json(string mode, string library, DateTime when)
        {
            var s = new System.Web.Script.Serialization.JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            var lines = new List<string>
            {
                "{",
                "  \"about\": " + s.Serialize(
                    "Every test of one bench run (entry W3.1-bis.2, item 7). A test is named by where it is recorded: " +
                    "File.cs:Method:ordinal, the ordinal counting the recording calls of that method in source order; " +
                    "@subject when a case or a subject is open, #n from the second time the same site records for the " +
                    "same subject. A test whose identity could not be resolved says why in unresolved.") + ",",
                "  \"format\": 1,",
                "  \"mode\": " + s.Serialize(mode ?? "") + ",",
                "  \"library\": " + s.Serialize(library ?? "") + ",",
                "  \"written\": " + s.Serialize(when.ToString("s", CultureInfo.InvariantCulture)) + ",",
                "  \"identities_unresolved\": " + UnresolvedCount.ToString(CultureInfo.InvariantCulture) + ",",
                "  \"tests\": [",
            };
            for (int i = 0; i < Tests.Count; i++)
            {
                var t = Tests[i];
                var row = new Dictionary<string, object>
                {
                    { "id", t.Id },
                    { "file", t.File },
                    { "method", t.Method },
                    { "ordinal", t.Ordinal },
                    { "line", t.Line },
                    { "subject", t.Subject },
                    { "occurrence", t.Occurrence },
                    { "outcome", t.Outcome },
                    { "group", t.Group },
                    { "label", t.Caption },
                };
                if (t.Unresolved != null) row["unresolved"] = t.Unresolved;
                lines.Add("    " + s.Serialize(row) + (i + 1 < Tests.Count ? "," : ""));
            }
            lines.Add("  ],");
            lines.Add("  \"cases\": [");
            for (int i = 0; i < Cases.Count; i++)
            {
                var c = Cases[i];
                var row = new Dictionary<string, object>
                {
                    { "name", c.CaseName },
                    { "folder", c.Folder },
                    { "kind", c.Kind },
                    { "outcome", c.Outcome },
                    { "expectations_attempted", c.ExpectationsAttempted },
                    { "expectations_checked", c.ExpectationsChecked },
                    { "tests", c.Tests },
                };
                lines.Add("    " + s.Serialize(row) + (i + 1 < Cases.Count ? "," : ""));
            }
            lines.Add("  ]");
            lines.Add("}");
            return string.Join("\n", lines) + "\n";
        }
    }

    /// <summary>
    /// WHERE THE RECORDING CALLS OF A C# SOURCE ARE, AND IN WHICH MEMBER.
    ///
    /// Comments, string and character literals and preprocessor lines are
    /// blanked first (keeping every line where it was), so a call quoted in a
    /// comment or in a message is not a call. Then the declarations are found
    /// by position: a statement that starts directly inside the body of a type
    /// is a member (method, constructor, property, field, event), whatever its
    /// modifiers. A call belongs to the nearest member declared before it,
    /// which is also what the compiler calls it: the enclosing member for a
    /// lambda or a local function, the property for a getter, «.ctor» for a
    /// constructor (checked in BenchReportTests against the compiler itself).
    ///
    /// What it does not understand is said, never guessed: an indexer, an
    /// operator or a tuple return type gives no member name, and a call after
    /// it is a call inside no member. A call inside the hole of an
    /// interpolated string is not seen at all, and a test recorded from it is
    /// unresolved.
    /// </summary>
    internal sealed class SourceScan
    {
        internal sealed class Member
        {
            /// <summary>The name the compiler gives: the method or property, «.ctor», «.cctor»; null for a type or an unknown shape.</summary>
            public string Identifier;
            public int Line;
            public int Token;
            /// <summary>True when its parameters carry [CallerMemberName]: a recording method.</summary>
            public bool Records;
        }

        internal sealed class Call
        {
            public string Recorder;
            public int Line;
            public Member In;
            public int Ordinal;
        }

        internal sealed class MemberList : List<Member> { }

        internal sealed class CallList : List<Call> { }

        private sealed class Token
        {
            public string Spelled;
            public int Line;
            public bool Word;
        }

        private sealed class TokenList : List<Token> { }

        internal MemberList Members = new MemberList();
        internal CallList Calls = new CallList();

        private static readonly HashSet<string> Modifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "public", "private", "protected", "internal", "static", "override", "virtual", "sealed",
            "abstract", "extern", "partial", "readonly", "unsafe", "volatile", "new", "const", "async",
        };

        private static readonly HashSet<string> TypeKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "class", "struct", "interface", "enum",
        };

        /// <summary>The first recording call that starts on this line, or null.</summary>
        internal Call CallAt(int line)
        {
            foreach (var c in Calls) if (c.Line == line) return c;
            return null;
        }

        /// <summary>The lines that start two recording calls: one line, one identity.</summary>
        internal List<int> LinesWithTwoCalls()
        {
            var seen = new HashSet<int>();
            var twice = new List<int>();
            foreach (var c in Calls)
                if (!seen.Add(c.Line) && !twice.Contains(c.Line)) twice.Add(c.Line);
            return twice;
        }

        /// <summary>The names of the members whose parameters carry [CallerMemberName].</summary>
        internal List<string> RecordersDeclared()
        {
            var found = new List<string>();
            foreach (var m in Members)
                if (m.Records && m.Identifier != null && !found.Contains(m.Identifier)) found.Add(m.Identifier);
            return found;
        }

        internal static SourceScan Of(string source, ICollection<string> recorders)
        {
            var scan = new SourceScan();
            var tokens = Tokens(Blank(source ?? ""));
            var members = new MemberList();
            var declarationNames = new HashSet<int>();

            // blocks: what the innermost brace opened ("file", "namespace", "type", "enum", "other")
            var blocks = new Stack<string>();
            blocks.Push("file");
            string pending = null;
            bool atStart = true;

            for (int i = 0; i < tokens.Count; i++)
            {
                var tk = tokens[i];
                var inside = blocks.Peek();

                if (atStart && (inside == "file" || inside == "namespace" || inside == "type"))
                {
                    int stop, nameIndex;
                    var kind = Declaration(tokens, i, inside == "type", members, out stop, out nameIndex);
                    if (nameIndex >= 0) declarationNames.Add(nameIndex);
                    if (kind != null)
                    {
                        pending = kind;
                        atStart = false;
                        // the terminator itself is processed below, on the next turn
                        i = stop - 1;
                        continue;
                    }
                }

                if (tk.Spelled == "{")
                {
                    blocks.Push(pending ?? "other");
                    pending = null;
                    atStart = true;
                }
                else if (tk.Spelled == "}")
                {
                    if (blocks.Count > 1) blocks.Pop();
                    pending = null;
                    atStart = true;
                }
                else if (tk.Spelled == ";")
                {
                    pending = null;
                    atStart = true;
                }
                else atStart = false;
            }

            // the calls, each in the nearest member declared before it. The
            // ordinal counts by NAME, not by declaration: two methods of one file
            // with the same name (overloads, or one per nested class) share the
            // count, so File.cs:Method:ordinal still names one call only.
            var calls = new CallList();
            var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
            int m = -1;
            for (int i = 0; i < tokens.Count; i++)
            {
                while (m + 1 < members.Count && members[m + 1].Token <= i) m++;
                var tk = tokens[i];
                if (!tk.Word || recorders == null || !recorders.Contains(tk.Spelled)) continue;
                if (i + 1 >= tokens.Count || tokens[i + 1].Spelled != "(") continue;
                if (declarationNames.Contains(i)) continue;
                var member = m >= 0 ? members[m] : null;
                if (member != null && member.Identifier == null) member = null;
                int n = 0;
                if (member != null) { ordinals.TryGetValue(member.Identifier, out n); n++; ordinals[member.Identifier] = n; }
                calls.Add(new Call { Recorder = tk.Spelled, Line = tk.Line, In = member, Ordinal = n });
            }
            scan.Members = members;
            scan.Calls = calls;
            return scan;
        }

        /// <summary>
        /// A declaration starting at token i, or null. Returns what the next
        /// brace opens, in stop the index of the token that ends the declaration
        /// head («(», «{», «=>», «=» or «;»), and in nameIndex the token of the
        /// declared name (-1 when there is none): a recorder named there is its
        /// declaration, not a call.
        /// </summary>
        private static string Declaration(TokenList t, int i, bool inType, MemberList members,
                                          out int stop, out int nameIndex)
        {
            stop = i;
            nameIndex = -1;
            int j = i;
            // attributes
            while (j < t.Count && t[j].Spelled == "[")
            {
                int depth = 0;
                for (; j < t.Count; j++)
                {
                    if (t[j].Spelled == "[") depth++;
                    else if (t[j].Spelled == "]") { depth--; if (depth == 0) { j++; break; } }
                }
            }
            if (j >= t.Count) return null;
            if (t[j].Spelled == "namespace")
            {
                int k = j;
                while (k < t.Count && t[k].Spelled != "{") k++;
                stop = k;
                return "namespace";
            }
            bool isStatic = false;
            int first = j;
            while (first < t.Count && Modifiers.Contains(t[first].Spelled))
            {
                if (t[first].Spelled == "static") isStatic = true;
                first++;
            }
            if (first >= t.Count) return null;
            var head = t[first].Spelled;

            if (TypeKeywords.Contains(head))
            {
                int k = first + 1;
                if (k < t.Count && t[k].Word)
                    members.Add(new Member { Identifier = null, Line = t[k].Line, Token = k });
                while (k < t.Count && t[k].Spelled != "{" && t[k].Spelled != ";") k++;
                stop = k;
                return head == "enum" ? "enum" : "type";
            }
            if (!inType) return null;
            if (head == "delegate" || head == "using" || head == "event")
            {
                int k = first;
                while (k < t.Count && t[k].Spelled != ";" && t[k].Spelled != "{") k++;
                stop = k;
                members.Add(new Member { Identifier = null, Line = t[first].Line, Token = first });
                return "other";
            }
            if (!t[first].Word) return null;

            int angle = 0, bracket = 0;
            int end = first;
            bool isOperator = false;
            for (; end < t.Count; end++)
            {
                var x = t[end].Spelled;
                if (x == "operator") isOperator = true;
                if (x == "<") angle++;
                else if (x == ">") angle = Math.Max(0, angle - 1);
                else if (x == "[") bracket++;
                else if (x == "]") bracket = Math.Max(0, bracket - 1);
                else if (angle == 0 && bracket == 0 &&
                         (x == "(" || x == "{" || x == "=>" || x == "=" || x == ";"))
                    break;
            }
            if (end >= t.Count) return null;
            stop = end;

            // the name: the word before the terminator, or before its generic arguments
            int nameAt = end - 1;
            if (nameAt >= first && t[nameAt].Spelled == ">")
            {
                int depth = 0;
                for (; nameAt >= first; nameAt--)
                {
                    if (t[nameAt].Spelled == ">") depth++;
                    else if (t[nameAt].Spelled == "<") { depth--; if (depth == 0) { nameAt--; break; } }
                }
            }
            string declared = null;
            if (nameAt >= first && t[nameAt].Word && t[nameAt].Spelled != "this" && !isOperator)
            {
                declared = t[nameAt].Spelled;
                // A CONSTRUCTOR has no type before its name: the compiler calls it «.ctor».
                if (t[end].Spelled == "(" && nameAt == first) declared = isStatic ? ".cctor" : ".ctor";
            }

            bool records = false;
            if (t[end].Spelled == "(")
            {
                int depth = 0;
                for (int k = end; k < t.Count; k++)
                {
                    if (t[k].Spelled == "(") depth++;
                    else if (t[k].Spelled == ")") { depth--; if (depth == 0) break; }
                    else if (t[k].Spelled == "CallerMemberName") records = true;
                }
            }
            int at = nameAt >= first ? nameAt : end;
            members.Add(new Member { Identifier = declared, Line = t[at].Line, Token = at, Records = records });
            if (nameAt >= first) nameIndex = nameAt;
            return "other";
        }

        private static TokenList Tokens(string code)
        {
            var list = new TokenList();
            int line = 1;
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (c == '\n') { line++; continue; }
                if (char.IsWhiteSpace(c)) continue;
                if (char.IsLetter(c) || c == '_' || (c == '@' && i + 1 < code.Length && (char.IsLetter(code[i + 1]) || code[i + 1] == '_')))
                {
                    int from = c == '@' ? i + 1 : i;
                    int k = from;
                    while (k < code.Length && (char.IsLetterOrDigit(code[k]) || code[k] == '_')) k++;
                    list.Add(new Token { Spelled = code.Substring(from, k - from), Line = line, Word = true });
                    i = k - 1;
                    continue;
                }
                if (char.IsDigit(c))
                {
                    int k = i;
                    while (k < code.Length && (char.IsLetterOrDigit(code[k]) || code[k] == '.' || code[k] == '_')) k++;
                    i = k - 1;
                    continue;
                }
                if (c == '=' && i + 1 < code.Length && code[i + 1] == '>') { list.Add(new Token { Spelled = "=>", Line = line }); i++; continue; }
                if ((c == '=' || c == '!' || c == '<' || c == '>') && i + 1 < code.Length && code[i + 1] == '=')
                {
                    list.Add(new Token { Spelled = code.Substring(i, 2), Line = line });
                    i++;
                    continue;
                }
                list.Add(new Token { Spelled = c.ToString(), Line = line });
            }
            return list;
        }

        /// <summary>
        /// The source with comments, string and character literals and
        /// preprocessor lines turned into spaces. Line breaks stay, so every
        /// token keeps its line.
        /// </summary>
        internal static string Blank(string s)
        {
            var o = s.ToCharArray();
            int i = 0;
            int n = s.Length;
            bool lineStart = true;
            while (i < n)
            {
                char c = s[i];
                char d = i + 1 < n ? s[i + 1] : '\0';
                if (c == '\n') { lineStart = true; i++; continue; }
                if (lineStart && c == '#')
                {
                    while (i < n && s[i] != '\n') { o[i] = ' '; i++; }
                    continue;
                }
                if (!char.IsWhiteSpace(c)) lineStart = false;
                if (c == '/' && d == '/')
                {
                    while (i < n && s[i] != '\n') { o[i] = ' '; i++; }
                    continue;
                }
                if (c == '/' && d == '*')
                {
                    int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    end = end < 0 ? n : end + 2;
                    Clear(o, s, i, end);
                    i = end;
                    continue;
                }
                if (c == '\'')
                {
                    int k = i + 1;
                    while (k < n && s[k] != '\'' && s[k] != '\n') k += s[k] == '\\' ? 2 : 1;
                    int end = Math.Min(n, k + 1);
                    Clear(o, s, i, end);
                    i = end;
                    continue;
                }
                bool verbatim = false, interpolated = false;
                int q = i;
                while (q < n && (s[q] == '@' || s[q] == '$') && q - i < 2)
                {
                    if (s[q] == '@') verbatim = true; else interpolated = true;
                    q++;
                }
                if (q < n && s[q] == '"' && (q == i || verbatim || interpolated))
                {
                    int end = StringEnd(s, q, verbatim, interpolated);
                    Clear(o, s, i, end);
                    i = end;
                    continue;
                }
                i++;
            }
            return new string(o);
        }

        /// <summary>The index just after the string literal whose opening quote is at q.</summary>
        private static int StringEnd(string s, int q, bool verbatim, bool interpolated)
        {
            int n = s.Length;
            int k = q + 1;
            while (k < n)
            {
                char c = s[k];
                if (verbatim && c == '"') { if (k + 1 < n && s[k + 1] == '"') { k += 2; continue; } return k + 1; }
                if (!verbatim && c == '\\') { k += 2; continue; }
                if (!verbatim && c == '"') return k + 1;
                if (!verbatim && c == '\n') return k;
                if (interpolated && c == '{')
                {
                    if (k + 1 < n && s[k + 1] == '{') { k += 2; continue; }
                    k = HoleEnd(s, k + 1);
                    continue;
                }
                k++;
            }
            return n;
        }

        /// <summary>The index just after the «}» that closes an interpolation hole opened before k.</summary>
        private static int HoleEnd(string s, int k)
        {
            int n = s.Length;
            int depth = 1;
            while (k < n)
            {
                char c = s[k];
                if (c == '"' || ((c == '@' || c == '$') && k + 1 < n && (s[k + 1] == '"' || s[k + 1] == '@' || s[k + 1] == '$')))
                {
                    bool verbatim = false, interpolated = false;
                    int q = k;
                    while (q < n && (s[q] == '@' || s[q] == '$') && q - k < 2)
                    {
                        if (s[q] == '@') verbatim = true; else interpolated = true;
                        q++;
                    }
                    if (q < n && s[q] == '"') { k = StringEnd(s, q, verbatim, interpolated); continue; }
                }
                if (c == '\'')
                {
                    int j = k + 1;
                    while (j < n && s[j] != '\'' && s[j] != '\n') j += s[j] == '\\' ? 2 : 1;
                    k = Math.Min(n, j + 1);
                    continue;
                }
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return k + 1; }
                k++;
            }
            return n;
        }

        private static void Clear(char[] o, string s, int from, int to)
        {
            for (int k = from; k < to && k < s.Length; k++) if (s[k] != '\n' && s[k] != '\r') o[k] = ' ';
        }
    }
}
