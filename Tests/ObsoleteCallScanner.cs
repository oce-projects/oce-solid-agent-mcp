using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// The refined list of obsolete SOLIDWORKS API members
    /// (Tests/data/obsolete-api-members.tsv, built by
    /// Checks/obsolete-api-list.js from the API help).
    /// </summary>
    internal sealed class ObsoleteApiList
    {
        /// <summary>"IInterface.Member" of every obsolete member.</summary>
        public readonly HashSet<string> Members = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Member name -> interfaces where it is obsolete.</summary>
        public readonly Dictionary<string, HashSet<string>> ObsoleteOn = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        /// <summary>Member name -> interfaces where the same name is current.</summary>
        public readonly Dictionary<string, HashSet<string>> CurrentOn = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        /// <summary>Every interface the list mentions, obsolete or current.</summary>
        public readonly HashSet<string> Universe = new HashSet<string>(StringComparer.Ordinal);
        public int Partial;

        public static ObsoleteApiList Load(string path)
        {
            var list = new ObsoleteApiList();
            foreach (var raw in File.ReadAllLines(path))
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                var cols = raw.Split('\t');
                if (cols.Length < 3) continue;
                var member = cols[0];
                int dot = member.IndexOf('.');
                if (dot <= 0) continue;
                string iface = member.Substring(0, dot), name = member.Substring(dot + 1);
                list.Members.Add(member);
                list.Universe.Add(iface);
                if (cols[1] == "partial") list.Partial++;
                HashSet<string> on;
                if (!list.ObsoleteOn.TryGetValue(name, out on)) list.ObsoleteOn[name] = on = new HashSet<string>(StringComparer.Ordinal);
                on.Add(iface);
                if (!list.CurrentOn.TryGetValue(name, out on)) list.CurrentOn[name] = on = new HashSet<string>(StringComparer.Ordinal);
                if (cols.Length > 3)
                    foreach (var c in cols[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        on.Add(c);
                        list.Universe.Add(c);
                    }
            }
            return list;
        }
    }

    /// <summary>One call to an obsolete member found in the source.</summary>
    internal sealed class ObsoleteHit
    {
        public string File;       // relative to the repository root, forward slashes
        public int Line;          // 1-based
        public string Member;     // "IInterface.Member"
        public string How;        // "typed" (receiver type known) or "name" (the name is obsolete everywhere)
        public string Code;       // the source line, trimmed
        public string MeasuredCase; // the bank case cited by an OBSOLETE-MEASURED: comment, or null
        public bool MeasuredMarker; // an OBSOLETE-MEASURED: comment covers this line
    }

    /// <summary>
    /// A call on a name that is obsolete on some interface and current on
    /// another, whose receiver type the source does not say.
    /// </summary>
    internal sealed class UndecidedCall
    {
        public string File;
        public int Line;
        public string Name;
        public string Code;
    }

    /// <summary>
    /// The state of <see cref="ObsoleteCallScanner.Strip(string, StripState, out string)"/>
    /// from one line to the next: a block comment, a verbatim string, or an
    /// interpolated string whose holes hold code, can all span lines.
    /// </summary>
    internal sealed class StripState
    {
        internal sealed class Frame
        {
            public bool Hole;          // an interpolation hole {...}: code
            public bool Verbatim;      // @"..." (for a string frame)
            public bool Interpolated;  // $"..." (for a string frame)
            public int Braces;         // nested { } inside a hole
            public int Parens;         // nested ( ) [ ] inside a hole
            public bool Format;        // after the ':' of a hole: literal format text
        }

        public bool Block;
        public readonly List<Frame> Stack = new List<Frame>();
    }

    /// <summary>
    /// FINDS CALLS TO OBSOLETE MEMBERS IN C# SOURCE, WITHOUT COMPILING IT.
    ///
    /// The difficulty is that a member NAME can be obsolete on one interface
    /// and current on another: `Select2` is obsolete on IEntity and current on
    /// IFeature, `GetBodies2` obsolete on IComponent2 and current on IPartDoc,
    /// `SaveAs3` obsolete on IModelDoc2 and current on IModelDocExtension. A
    /// check by name alone would either shout at every `feature.Select2` (and
    /// be switched off the same day) or miss the obsolete ones. So:
    ///
    ///   1. the RECEIVER TYPE is inferred when the source says it, WITHIN THE
    ///      SCOPE C# gives the name: a local or parameter visible at the call
    ///      (declared in an enclosing block of the same method, not in another
    ///      method that happens to use the same name), a field of the enclosing
    ///      class (also declared in another file of the same partial class), a
    ///      cast (`(IEntity)x`, `x as IFace2`), a well-known accessor
    ///      (`.Extension`, `.FeatureManager`...), `new T(...)`, an element of a
    ///      `T[]`, or the return type of a method declared in the scanned
    ///      source (`var view = ResolveView(...)` is an IView because
    ///      ResolveView is declared `IView ResolveView(`). A `var` whose type
    ///      cannot be inferred HIDES any outer name, as it does in C#. With a
    ///      known SOLIDWORKS type the verdict is exact;
    ///   2. with an unknown receiver, the name decides only when it is obsolete
    ///      on EVERY interface that declares it. Legacy interfaces that the code
    ///      never names are left out (IModelDoc next to IModelDoc2, IFace next to
    ///      IFace2...): otherwise every `EditRebuild3` would look ambiguous
    ///      because the ancient IModelDoc had an obsolete one;
    ///   3. everything else is UNDECIDED and returned as such (`x.Value`,
    ///      `feat.Select2` on an untyped receiver): the contract freezes those
    ///      too, so that a new one is seen, and prints the count so that nobody
    ///      mistakes "not flagged" for "checked".
    ///
    /// A type found OUTSIDE the scope of the call is never used: it made
    /// `done.Add` (a HashSet) look like an IModelDoc2 call and let an obsolete
    /// `IView.Name` pass as `IConfigurationManager.Name` (review of Z-C21.6).
    ///
    /// Comments and string literals are blanked before looking, so that
    /// explaining a trap in a comment does not become a violation; the code in
    /// the holes of an interpolated string is kept.
    /// </summary>
    internal sealed class ObsoleteCallScanner
    {
        private static readonly Dictionary<string, string> Accessors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Extension"] = "IModelDocExtension",
            ["FeatureManager"] = "IFeatureManager",
            ["SketchManager"] = "ISketchManager",
            ["SelectionManager"] = "ISelectionMgr",
            ["ConfigurationManager"] = "IConfigurationManager",
        };

        private static readonly Regex DeclarationRx = new Regex(
            @"\b(I?[A-Z][A-Za-z0-9_]*)(\[\])?\s+(@?[A-Za-z_]\w*)\s*(?==|;|,|\)|&|\||\{|\bin\b)", RegexOptions.Compiled);
        private static readonly Regex VarRx = new Regex(@"\bvar\s+(@?[A-Za-z_]\w*)\s*(=(?!=))?", RegexOptions.Compiled);
        private static readonly Regex CastAssignRx = new Regex(
            @"\b([A-Za-z_]\w*)\s*=\s*\(\s*(I?[A-Z]\w*)\s*\)", RegexOptions.Compiled);
        private static readonly Regex AsAssignRx = new Regex(
            @"\b([A-Za-z_]\w*)\s*=[^;=]*?\bas\s+(I?[A-Z]\w*)\s*(?=;|\?\?|\))", RegexOptions.Compiled);
        private static readonly Regex AccessorAssignRx = new Regex(
            @"\b([A-Za-z_]\w*)\s*=\s*[\w.()?\[\]]*?\.(Extension|FeatureManager|SketchManager|SelectionManager|ConfigurationManager)\s*(?:as\s+\w+\s*)?;", RegexOptions.Compiled);
        private static readonly Regex MethodRx = new Regex(
            @"\b(I?[A-Z]\w*)(\[\])?\s+([A-Za-z_]\w*)\s*(?:<[^()<>]*>)?\s*\(", RegexOptions.Compiled);
        private static readonly Regex TypeHeaderRx = new Regex(@"\b(class|struct|interface|record|enum)\s+([A-Za-z_]\w*)", RegexOptions.Compiled);
        private static readonly Regex NamespaceRx = new Regex(@"\bnamespace\b", RegexOptions.Compiled);
        private static readonly Regex TypeTokenRx = new Regex(
            @"(?:\bnew\s+|\bas\s+|\bis\s+|\btypeof\s*\(\s*|<\s*)(?<t>I?[A-Z]\w*)|\(\s*(?<t>I?[A-Z]\w*)\s*\)", RegexOptions.Compiled);
        private static readonly Regex MemberRx = new Regex(@"\.\s*([A-Za-z_]\w*)\b", RegexOptions.Compiled);
        private static readonly Regex TrailingIdentRx = new Regex(@"(?<![\w.])([A-Za-z_]\w*)\s*$", RegexOptions.Compiled);
        private static readonly Regex TrailingMemberRx = new Regex(@"\.\s*([A-Za-z_]\w*)\s*$", RegexOptions.Compiled);
        private static readonly Regex CastInnerRx = new Regex(@"^\(\s*(I?[A-Z]\w*)\s*\)", RegexOptions.Compiled);
        private static readonly Regex AsInnerRx = new Regex(@"\bas\s+(I?[A-Z]\w*)\s*$", RegexOptions.Compiled);
        private static readonly Regex NewRx = new Regex(@"^new\s+(?:[A-Za-z_]\w*\.)*([A-Za-z_]\w*)\s*(<|\(|\[|\{)", RegexOptions.Compiled);
        private static readonly Regex CallRx = new Regex(@"^(?:([A-Za-z_]\w*)\s*\.\s*)?([A-Za-z_]\w*)\s*\(", RegexOptions.Compiled);
        private static readonly Regex AccessorTailRx = new Regex(@"\.\s*(Extension|FeatureManager|SketchManager|SelectionManager|ConfigurationManager)\s*$", RegexOptions.Compiled);

        public const string MeasuredMarker = "OBSOLETE-MEASURED:";
        private static readonly Regex MeasuredRx = new Regex(@"OBSOLETE-MEASURED:\s*([A-Za-z0-9_\-]+)", RegexOptions.Compiled);

        private enum ScopeKind { Namespace, Type, Code }

        private sealed class Scope
        {
            public ScopeKind Kind;
            public string ClassName;   // for a Type scope
            public Scope Parent;
            public int Depth;
        }

        private sealed class Declaration
        {
            public int Line, Col;
            public string Name;
            public string Type;        // null: declared, type unknown (a var that hides)
            public string Method;      // "var x = Method(...)": resolved at Scan, when every method is known
            public Scope Scope;        // a local: the block it lives in
            public Scope Body;         // a parameter: the body of its method
            public int EndLine = -1;   // a parameter of an expression-bodied member: its last line
        }

        private sealed class SourceFile
        {
            public string Path;
            public string[] Raw;
            public string[] Code;
            public string[] Comment;
            public readonly Dictionary<string, List<Declaration>> Locals = new Dictionary<string, List<Declaration>>(StringComparer.Ordinal);
            public Scope[] LineStart;                      // the scope at the start of each line
            public List<Tuple<int, Scope>>[] Transitions;  // (column, scope from that column on)
        }

        private readonly ObsoleteApiList _list;
        private readonly List<SourceFile> _files = new List<SourceFile>();
        private readonly HashSet<string> _typesNamed = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _classNames = new HashSet<string>(StringComparer.Ordinal);
        // class name -> field or property name -> the types it is declared with
        private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _fields =
            new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.Ordinal);
        // method name -> the return types it is declared with, in any scanned file
        private readonly Dictionary<string, HashSet<string>> _methods = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private Dictionary<string, List<string>> _byBase;

        /// <summary>Calls that could not be decided (ambiguous name, unknown receiver).</summary>
        public int Undecided => UndecidedCalls.Count;
        public readonly List<UndecidedCall> UndecidedCalls = new List<UndecidedCall>();

        public ObsoleteCallScanner(ObsoleteApiList list) { _list = list; }

        /// <summary>Adds a file. Call <see cref="Scan"/> after every file is added.</summary>
        public void Add(string relativePath, string[] lines)
        {
            var f = new SourceFile { Path = relativePath.Replace('\\', '/'), Raw = lines,
                                     Code = new string[lines.Length], Comment = new string[lines.Length],
                                     LineStart = new Scope[lines.Length],
                                     Transitions = new List<Tuple<int, Scope>>[lines.Length] };
            var state = new StripState();
            for (int i = 0; i < lines.Length; i++)
            {
                string comment;
                f.Code[i] = Strip(lines[i], state, out comment);
                f.Comment[i] = comment;
                foreach (Match m in TypeTokenRx.Matches(f.Code[i])) _typesNamed.Add(m.Groups["t"].Value);
            }
            Walk(f);
            _files.Add(f);
        }

        /// <summary>The candidate declarations of a line, by column: (col, name, type, method).</summary>
        private List<Tuple<int, string, string, string>> Candidates(SourceFile f, int i)
        {
            var c = f.Code[i];
            var result = new List<Tuple<int, string, string, string>>();
            foreach (Match m in DeclarationRx.Matches(c))
            {
                var t = m.Groups[1].Value;
                if (t == "I") continue;
                result.Add(Tuple.Create(m.Groups[3].Index, m.Groups[3].Value, t + m.Groups[2].Value, (string)null));
            }
            foreach (Match m in VarRx.Matches(c))
            {
                string type = null, method = null;
                if (m.Groups[2].Success) RhsType(Statement(f, i, m.Index + m.Length), out type, out method);
                result.Add(Tuple.Create(m.Groups[1].Index, m.Groups[1].Value, type, method));
            }
            Func<int, bool> afterVar = pos => Regex.IsMatch(c.Substring(0, pos), @"\bvar\s+$");
            foreach (Match m in CastAssignRx.Matches(c))
                if (!afterVar(m.Index)) result.Add(Tuple.Create(m.Index, m.Groups[1].Value, m.Groups[2].Value, (string)null));
            foreach (Match m in AsAssignRx.Matches(c))
                if (!afterVar(m.Index)) result.Add(Tuple.Create(m.Index, m.Groups[1].Value, m.Groups[2].Value, (string)null));
            foreach (Match m in AccessorAssignRx.Matches(c))
                if (!afterVar(m.Index)) result.Add(Tuple.Create(m.Index, m.Groups[1].Value, Accessors[m.Groups[2].Value], (string)null));
            return result.OrderBy(x => x.Item1).ToList();
        }

        /// <summary>The code from a position to the end of its statement (';' at depth 0), up to ten lines.</summary>
        private static string Statement(SourceFile f, int line, int col)
        {
            var sb = new StringBuilder();
            int depth = 0;
            for (int i = line; i < f.Code.Length && i < line + 10; i++)
            {
                var c = f.Code[i];
                for (int j = i == line ? col : 0; j < c.Length; j++)
                {
                    char ch = c[j];
                    if (ch == '(' || ch == '[' || ch == '{') depth++;
                    else if (ch == ')' || ch == ']' || ch == '}') { if (depth == 0) return sb.ToString(); depth--; }
                    else if ((ch == ';' || ch == ',') && depth == 0) return sb.ToString();
                    sb.Append(ch);
                }
                sb.Append(' ');
            }
            return sb.ToString();
        }

        /// <summary>The type of the right-hand side of a `var x = ...`, when the source says it.</summary>
        private void RhsType(string rhs, out string type, out string method)
        {
            type = null;
            method = null;
            rhs = rhs.Trim();
            if (rhs.Length == 0) return;
            var m = NewRx.Match(rhs);
            if (m.Success) { type = m.Groups[1].Value + (m.Groups[2].Value == "[" ? "[]" : ""); return; }
            m = CastInnerRx.Match(rhs);
            if (m.Success) { type = m.Groups[1].Value; return; }
            m = AsInnerRx.Match(rhs);
            if (m.Success) { type = m.Groups[1].Value; return; }
            m = AccessorTailRx.Match(rhs);
            if (m.Success) { type = Accessors[m.Groups[1].Value]; return; }
            m = CallRx.Match(rhs);
            if (m.Success)
            {
                // The call must be the WHOLE right-hand side: `Method(a).Other` is not the type of Method.
                int open = m.Index + m.Length - 1, depth = 0, j = open;
                for (; j < rhs.Length; j++)
                {
                    if (rhs[j] == '(') depth++;
                    else if (rhs[j] == ')') { depth--; if (depth == 0) break; }
                }
                if (j >= rhs.Length || rhs.Substring(j + 1).Trim().Length != 0) return;
                var qualifier = m.Groups[1].Success ? m.Groups[1].Value : null;
                if (qualifier == null || qualifier == "this" || char.IsUpper(qualifier[0]))
                    method = (qualifier != null && qualifier != "this" ? qualifier + "|" : "|") + m.Groups[2].Value;
            }
        }

        /// <summary>
        /// One pass over the blanked code: the braces give the scopes (namespace,
        /// type, code block), and every declaration gets the scope where C#
        /// makes it visible.
        /// </summary>
        private void Walk(SourceFile f)
        {
            var stack = new List<Scope>();
            var parens = new List<int> { 0 };      // paren depth, one counter per brace level
            var header = new StringBuilder();      // code since the last ; { or }
            var pending = new List<Declaration>(); // parameters waiting for their body
            Scope pendingOwner = null;
            Func<Scope> top = () => stack.Count == 0 ? null : stack[stack.Count - 1];

            for (int i = 0; i < f.Code.Length; i++)
            {
                f.LineStart[i] = top();
                f.Transitions[i] = new List<Tuple<int, Scope>>();
                var code = f.Code[i];
                var candidates = Candidates(f, i);
                int next = 0;

                // Methods declared at type level: their return type.
                if (top() != null && top().Kind == ScopeKind.Type && parens[parens.Count - 1] == 0)
                    foreach (Match m in MethodRx.Matches(code))
                        if (!Regex.IsMatch(code.Substring(0, m.Index), @"\bnew\s*$"))
                            AddMethod(top().ClassName, m.Groups[3].Value, m.Groups[1].Value + m.Groups[2].Value);

                for (int col = 0; col <= code.Length; col++)
                {
                    while (next < candidates.Count && candidates[next].Item1 <= col)
                    {
                        var cand = candidates[next++];
                        var d = new Declaration { Line = i, Col = cand.Item1, Name = cand.Item2.TrimStart('@'),
                                                  Type = cand.Item3, Method = cand.Item4 };
                        var s = top();
                        if (s == null || s.Kind == ScopeKind.Namespace) continue;
                        if (s.Kind == ScopeKind.Code) { d.Scope = s; AddLocal(f, d); }
                        else if (parens[parens.Count - 1] > 0)
                        {
                            if (pendingOwner != s) { pending.Clear(); pendingOwner = s; }
                            pending.Add(d);
                            AddLocal(f, d);
                        }
                        else AddField(s.ClassName, d.Name, d.Type);
                        if (d.Type != null) _typesNamed.Add(d.Type.TrimEnd('[', ']'));
                    }
                    if (col == code.Length) break;
                    char ch = code[col];
                    if (ch == '(' || ch == '[') parens[parens.Count - 1]++;
                    else if (ch == ')' || ch == ']') parens[parens.Count - 1] = Math.Max(0, parens[parens.Count - 1] - 1);
                    else if (ch == '{')
                    {
                        var h = header.ToString();
                        var s = new Scope { Parent = top(), Depth = stack.Count };
                        var tm = TypeHeaderRx.Match(h);
                        if (tm.Success) { s.Kind = ScopeKind.Type; s.ClassName = tm.Groups[2].Value; _classNames.Add(s.ClassName); }
                        else if (NamespaceRx.IsMatch(h)) s.Kind = ScopeKind.Namespace;
                        else s.Kind = ScopeKind.Code;
                        if (s.Kind == ScopeKind.Code && pending.Count > 0 && pendingOwner == top() && parens[parens.Count - 1] == 0)
                        {
                            foreach (var p in pending) p.Body = s;
                            pending.Clear();
                        }
                        stack.Add(s);
                        parens.Add(0);
                        header.Clear();
                        f.Transitions[i].Add(Tuple.Create(col + 1, s));
                        continue;
                    }
                    else if (ch == '}')
                    {
                        if (stack.Count > 0) { stack.RemoveAt(stack.Count - 1); parens.RemoveAt(parens.Count - 1); }
                        header.Clear();
                        f.Transitions[i].Add(Tuple.Create(col + 1, top()));
                        continue;
                    }
                    else if (ch == ';')
                    {
                        if (pending.Count > 0 && pendingOwner == top() && parens[parens.Count - 1] == 0)
                        {
                            foreach (var p in pending) p.EndLine = i;   // expression-bodied or abstract member
                            pending.Clear();
                        }
                        header.Clear();
                        continue;
                    }
                    header.Append(ch);
                }
                header.Append(' ');
            }
        }

        private static void AddLocal(SourceFile f, Declaration d)
        {
            List<Declaration> l;
            if (!f.Locals.TryGetValue(d.Name, out l)) f.Locals[d.Name] = l = new List<Declaration>();
            l.Add(d);
        }

        private void AddField(string className, string name, string type)
        {
            Dictionary<string, HashSet<string>> byName;
            if (!_fields.TryGetValue(className ?? "", out byName)) _fields[className ?? ""] = byName = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            HashSet<string> types;
            if (!byName.TryGetValue(name, out types)) byName[name] = types = new HashSet<string>(StringComparer.Ordinal);
            types.Add(type ?? "?");
        }

        private void AddMethod(string className, string name, string type)
        {
            foreach (var key in new[] { "|" + name, (className ?? "") + "|" + name })
            {
                HashSet<string> types;
                if (!_methods.TryGetValue(key, out types)) _methods[key] = types = new HashSet<string>(StringComparer.Ordinal);
                types.Add(type);
            }
        }

        /// <summary>Every call to an obsolete member in the files added.</summary>
        public List<ObsoleteHit> Scan()
        {
            _byBase = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var i in _list.Universe)
            {
                List<string> l;
                var b = Base(i);
                if (!_byBase.TryGetValue(b, out l)) _byBase[b] = l = new List<string>();
                l.Add(i);
            }
            var hits = new List<ObsoleteHit>();
            UndecidedCalls.Clear();
            foreach (var f in _files) ScanFile(f, hits);
            return hits;
        }

        private void ScanFile(SourceFile f, List<ObsoleteHit> hits)
        {
            for (int i = 0; i < f.Code.Length; i++)
            {
                var code = f.Code[i];
                foreach (Match m in MemberRx.Matches(code))
                {
                    var name = m.Groups[1].Value;
                    if (!_list.ObsoleteOn.ContainsKey(name)) continue;
                    string receiver = ReceiverType(f, i, m.Index, code.Substring(0, m.Index));
                    string member = null, how = null;
                    if (receiver != null)
                    {
                        var sw = Normalise(receiver);
                        if (sw == null) continue;                       // not a SOLIDWORKS type
                        if (_list.Members.Contains(sw + "." + name)) { member = sw + "." + name; how = "typed"; }
                        else continue;                                  // current (or undocumented) on that type
                    }
                    else
                    {
                        var obsolete = _list.ObsoleteOn[name].Where(x => !Legacy(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
                        var current = _list.CurrentOn[name].Where(x => !Legacy(x)).ToList();
                        if (obsolete.Count == 0) continue;
                        if (current.Count > 0)
                        {
                            UndecidedCalls.Add(new UndecidedCall { File = f.Path, Line = i + 1, Name = name, Code = f.Raw[i].Trim() });
                            continue;
                        }
                        member = obsolete[0] + "." + name;
                        how = "name";
                    }
                    var hit = new ObsoleteHit { File = f.Path, Line = i + 1, Member = member, How = how, Code = f.Raw[i].Trim() };
                    string cited;
                    hit.MeasuredMarker = FindMeasured(f, i, out cited);
                    hit.MeasuredCase = cited;
                    hits.Add(hit);
                }
            }
        }

        /// <summary>
        /// An OBSOLETE-MEASURED: comment on the same line, or in the block of
        /// comment-only lines right above it.
        /// </summary>
        private static bool FindMeasured(SourceFile f, int line, out string cited)
        {
            cited = null;
            for (int k = line; k >= 0 && line - k <= 15; k--)
            {
                bool commentOnly = f.Code[k].Trim().Length == 0 && !string.IsNullOrEmpty(f.Comment[k]);
                if (k != line && !commentOnly) break;
                var c = f.Comment[k];
                if (string.IsNullOrEmpty(c)) continue;
                var m = MeasuredRx.Match(c);
                if (m.Success) { cited = m.Groups[1].Value; return true; }
                if (c.Contains(MeasuredMarker)) return true;   // marker without a case: caught by the evaluation
            }
            return false;
        }

        private string ReceiverType(SourceFile f, int line, int col, string before)
        {
            before = before.TrimEnd();
            if (before.EndsWith("?", StringComparison.Ordinal)) before = before.Substring(0, before.Length - 1).TrimEnd();
            if (before.Length == 0) return null;
            var member = TrailingMemberRx.Match(before);
            if (member.Success)
            {
                string t;
                return Accessors.TryGetValue(member.Groups[1].Value, out t) ? t : null;
            }
            var ident = TrailingIdentRx.Match(before);
            if (ident.Success) return TypeOf(f, line, col, ident.Groups[1].Value);
            char last = before[before.Length - 1];
            if (last == ')' || last == ']')
            {
                char open = last == ')' ? '(' : '[';
                int depth = 0, j = before.Length - 1;
                for (; j >= 0; j--)
                {
                    if (before[j] == last) depth++;
                    else if (before[j] == open) { depth--; if (depth == 0) break; }
                }
                if (j < 0) return null;
                var head = before.Substring(0, j).TrimEnd();
                if (last == ']')
                {
                    // An element of an array: `faces[i].Normal` with `IFace2[] faces`.
                    var arr = TrailingIdentRx.Match(head);
                    if (!arr.Success) return null;
                    var t = TypeOf(f, line, col, arr.Groups[1].Value);
                    return t != null && t.EndsWith("[]", StringComparison.Ordinal) ? t.Substring(0, t.Length - 2) : null;
                }
                var inner = before.Substring(j + 1, before.Length - j - 2).Trim();
                var cast = CastInnerRx.Match(inner);
                if (cast.Success) return cast.Groups[1].Value;
                var asCast = AsInnerRx.Match(inner);
                if (asCast.Success) return asCast.Groups[1].Value;
                // A direct call: `ResolveView(...).Name`, `new T(...).X`.
                var callee = Regex.Match(head, @"(?:(?<![\w.])new\s+(?<new>[A-Za-z_]\w*)|(?<![\w.])(?<m>[A-Za-z_]\w*))\s*$");
                if (callee.Success && callee.Groups["new"].Success) return callee.Groups["new"].Value;
                if (callee.Success && callee.Groups["m"].Success) return MethodType("|" + callee.Groups["m"].Value);
            }
            return null;
        }

        private string MethodType(string key)
        {
            HashSet<string> types;
            return _methods.TryGetValue(key, out types) && types.Count == 1 ? types.First() : null;
        }

        private static Scope ScopeAt(SourceFile f, int line, int col)
        {
            var s = f.LineStart[line];
            foreach (var t in f.Transitions[line])
            {
                if (t.Item1 > col) break;
                s = t.Item2;
            }
            return s;
        }

        private static bool Encloses(Scope outer, Scope inner)
        {
            for (var s = inner; s != null; s = s.Parent) if (s == outer) return true;
            return false;
        }

        /// <summary>
        /// The declared type of a name at a point of the file: the nearest local
        /// or parameter visible there, else a field of an enclosing class. Null
        /// when nothing in scope says it (a type seen elsewhere is not used).
        /// </summary>
        private string TypeOf(SourceFile f, int line, int col, string name)
        {
            var use = ScopeAt(f, line, col);
            List<Declaration> locals;
            if (f.Locals.TryGetValue(name, out locals))
            {
                Declaration best = null;
                int bestDepth = -1;
                foreach (var d in locals)
                {
                    int depth;
                    if (d.Scope != null)
                    {
                        if (!Encloses(d.Scope, use)) continue;
                        if (d.Line > line || (d.Line == line && d.Col >= col)) continue;
                        depth = d.Scope.Depth;
                    }
                    else if (d.Body != null)
                    {
                        if (!Encloses(d.Body, use)) continue;
                        depth = d.Body.Depth;
                    }
                    else if (d.EndLine >= 0)
                    {
                        if (line < d.Line || line > d.EndLine) continue;
                        depth = 0;
                    }
                    else continue;
                    if (best == null || depth > bestDepth || (depth == bestDepth && (d.Line > best.Line || (d.Line == best.Line && d.Col > best.Col))))
                    {
                        best = d;
                        bestDepth = depth;
                    }
                }
                if (best != null)
                    return best.Method != null ? ResolveMethod(best.Method) : best.Type;
            }
            for (var s = use; s != null; s = s.Parent)
            {
                if (s.Kind != ScopeKind.Type) continue;
                Dictionary<string, HashSet<string>> byName;
                HashSet<string> types;
                if (_fields.TryGetValue(s.ClassName ?? "", out byName) && byName.TryGetValue(name, out types))
                    return types.Count == 1 && !types.Contains("?") ? types.First() : null;
            }
            return null;
        }

        private string ResolveMethod(string key)
        {
            // "Class|Method" for a qualified static call, "|Method" otherwise.
            if (!key.StartsWith("|", StringComparison.Ordinal) && !_classNames.Contains(key.Substring(0, key.IndexOf('|'))))
                return null;
            return MethodType(key);
        }

        private string Normalise(string type)
        {
            if (type.EndsWith("[]", StringComparison.Ordinal)) return null;
            if (_list.Universe.Contains(type)) return type;
            if (_list.Universe.Contains("I" + type)) return "I" + type;
            return null;
        }

        private static string Base(string iface) { return iface.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'); }

        private static int Number(string iface)
        {
            var b = Base(iface);
            int n;
            return b.Length < iface.Length && int.TryParse(iface.Substring(b.Length), out n) ? n : 1;
        }

        private bool Legacy(string iface)
        {
            if (_typesNamed.Contains(iface) || _typesNamed.Contains(iface.Substring(1))) return false;
            List<string> siblings;
            if (!_byBase.TryGetValue(Base(iface), out siblings)) return false;
            int n = Number(iface);
            return siblings.Any(s => Number(s) > n);
        }

        /// <summary>
        /// The line with comments and string/char literals blanked (same length),
        /// and the text of its // comment in <paramref name="comment"/>. The code
        /// inside the holes of an interpolated string is kept: `$"{m.GetX()}"`
        /// is a call like any other.
        /// </summary>
        internal static string Strip(string line, StripState state, out string comment)
        {
            comment = null;
            var sb = new StringBuilder(line.Length);
            var st = state.Stack;
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                char next = i + 1 < line.Length ? line[i + 1] : '\0';
                if (state.Block)
                {
                    if (c == '*' && next == '/') { state.Block = false; sb.Append("  "); i += 2; continue; }
                    sb.Append(' '); i++; continue;
                }
                var top = st.Count == 0 ? null : st[st.Count - 1];
                if (top != null && !top.Hole)
                {
                    // Inside a string literal.
                    if (!top.Verbatim && c == '\\') { sb.Append(next == '\0' ? " " : "  "); i += 2; continue; }
                    if (c == '"')
                    {
                        if (top.Verbatim && next == '"') { sb.Append("  "); i += 2; continue; }
                        st.RemoveAt(st.Count - 1); sb.Append(' '); i++; continue;
                    }
                    if (top.Interpolated && (c == '{' || c == '}'))
                    {
                        if (next == c) { sb.Append("  "); i += 2; continue; }
                        if (c == '{') { st.Add(new StripState.Frame { Hole = true }); sb.Append(' '); i++; continue; }
                    }
                    sb.Append(' '); i++; continue;
                }
                if (top != null && top.Format)
                {
                    if (c == '}') st.RemoveAt(st.Count - 1);
                    sb.Append(' '); i++; continue;
                }
                // Code: the top level, or an interpolation hole.
                if (c == '/' && next == '/') { if (top == null) comment = line.Substring(i); break; }
                if (c == '/' && next == '*') { state.Block = true; sb.Append("  "); i += 2; continue; }
                if (c == '"')
                {
                    bool verbatim = false, interpolated = false;
                    for (int k = sb.Length - 1; k >= 0 && (sb[k] == '@' || sb[k] == '$'); k--)
                    {
                        if (sb[k] == '@') verbatim = true; else interpolated = true;
                        sb[k] = ' ';
                    }
                    st.Add(new StripState.Frame { Verbatim = verbatim, Interpolated = interpolated });
                    sb.Append(' '); i++; continue;
                }
                if (c == '\'')
                {
                    int end = line.IndexOf('\'', i + (next == '\\' ? 3 : 2));
                    if (end > i && end - i <= 7) { sb.Append(' ', end - i + 1); i = end + 1; continue; }
                }
                if (top != null)
                {
                    if (c == '{') top.Braces++;
                    else if (c == '}')
                    {
                        if (top.Braces == 0) { st.RemoveAt(st.Count - 1); sb.Append(' '); i++; continue; }
                        top.Braces--;
                    }
                    else if (c == '(' || c == '[') top.Parens++;
                    else if (c == ')' || c == ']') top.Parens--;
                    else if (c == ':' && next != ':' && top.Braces == 0 && top.Parens == 0)
                    {
                        top.Format = true; sb.Append(' '); i++; continue;
                    }
                }
                sb.Append(c); i++;
            }
            // A regular (not verbatim) string cannot go on to the next line.
            int cut = st.FindIndex(fr => !fr.Hole && !fr.Verbatim);
            if (cut >= 0) st.RemoveRange(cut, st.Count - cut);
            while (sb.Length < line.Length) sb.Append(' ');
            return sb.ToString();
        }
    }
}
