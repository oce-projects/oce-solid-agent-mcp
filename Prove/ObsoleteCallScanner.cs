using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Prove.Contratti
{
    /// <summary>
    /// The refined list of obsolete SOLIDWORKS API members
    /// (Prove/data/obsolete-api-members.tsv, built by
    /// Controlli/obsolete-api-list.js from the API help).
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
    /// FINDS CALLS TO OBSOLETE MEMBERS IN C# SOURCE, WITHOUT COMPILING IT.
    ///
    /// The difficulty is that a member NAME can be obsolete on one interface
    /// and current on another: `Select2` is obsolete on IEntity and current on
    /// IFeature, `GetBodies2` obsolete on IComponent2 and current on IPartDoc,
    /// `SaveAs3` obsolete on IModelDoc2 and current on IModelDocExtension. A
    /// check by name alone would either shout at every `feature.Select2` (and
    /// be switched off the same day) or miss the obsolete ones. So:
    ///
    ///   1. the RECEIVER TYPE is inferred when the source says it: a declared
    ///      variable or field (`IDimension quota`, also in another file of the
    ///      same partial class), a cast (`(IEntity)x`, `x as IFace2`), a well
    ///      known accessor (`.Extension`, `.FeatureManager`, `.SketchManager`,
    ///      `.SelectionManager`, `.ConfigurationManager`). With a known
    ///      SOLIDWORKS type the verdict is exact: the member is obsolete on that
    ///      interface or it is not;
    ///   2. with an unknown receiver, the name decides only when it is obsolete
    ///      on EVERY interface that declares it. Legacy interfaces that the code
    ///      never names are left out (IModelDoc next to IModelDoc2, IFace next to
    ///      IFace2...): otherwise every `EditRebuild3` would look ambiguous
    ///      because the ancient IModelDoc had an obsolete one;
    ///   3. everything else is UNDECIDED and counted, not flagged: `list.Add`,
    ///      `x.Value`, `feat.Select2` on an untyped receiver. The count is
    ///      printed so that nobody mistakes "not flagged" for "checked".
    ///
    /// Comments and string literals are blanked before looking, so that
    /// explaining a trap in a comment does not become a violation.
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
            @"\b(I?[A-Z][A-Za-z0-9_]*)(?:\[\])?\s+(@?[A-Za-z_]\w*)\s*(?==|;|,|\)|&|\||\bin\b)", RegexOptions.Compiled);
        private static readonly Regex CastAssignRx = new Regex(
            @"\b([A-Za-z_]\w*)\s*=\s*\(\s*(I?[A-Z]\w*)\s*\)", RegexOptions.Compiled);
        private static readonly Regex AsAssignRx = new Regex(
            @"\b([A-Za-z_]\w*)\s*=[^;=]*?\bas\s+(I?[A-Z]\w*)\s*(?=;|\?\?|\))", RegexOptions.Compiled);
        private static readonly Regex AccessorAssignRx = new Regex(
            @"\b([A-Za-z_]\w*)\s*=\s*[\w.()?\[\]]*?\.(Extension|FeatureManager|SketchManager|SelectionManager|ConfigurationManager)\s*(?:as\s+\w+\s*)?;", RegexOptions.Compiled);
        private static readonly Regex TypeTokenRx = new Regex(
            @"(?:\bnew\s+|\bas\s+|\bis\s+|\btypeof\s*\(\s*|<\s*)(?<t>I?[A-Z]\w*)|\(\s*(?<t>I?[A-Z]\w*)\s*\)", RegexOptions.Compiled);
        private static readonly Regex MemberRx = new Regex(@"\.\s*([A-Za-z_]\w*)\b", RegexOptions.Compiled);
        private static readonly Regex TrailingIdentRx = new Regex(@"([A-Za-z_]\w*)\s*$", RegexOptions.Compiled);
        private static readonly Regex TrailingMemberRx = new Regex(@"\.\s*([A-Za-z_]\w*)\s*$", RegexOptions.Compiled);
        private static readonly Regex CastInnerRx = new Regex(@"^\(\s*(I?[A-Z]\w*)\s*\)", RegexOptions.Compiled);
        private static readonly Regex AsInnerRx = new Regex(@"\bas\s+(I?[A-Z]\w*)\s*$", RegexOptions.Compiled);

        public const string MeasuredMarker = "OBSOLETE-MEASURED:";
        private static readonly Regex MeasuredRx = new Regex(@"OBSOLETE-MEASURED:\s*([A-Za-z0-9_\-]+)", RegexOptions.Compiled);

        private sealed class SourceFile
        {
            public string Path;
            public string[] Raw;
            public string[] Code;
            public string[] Comment;
            public List<Tuple<int, string, string>> Declarations = new List<Tuple<int, string, string>>(); // line, name, type
        }

        private readonly ObsoleteApiList _list;
        private readonly List<SourceFile> _files = new List<SourceFile>();
        private readonly HashSet<string> _typesNamed = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _globalTypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private Dictionary<string, List<string>> _byBase;

        /// <summary>Calls that could not be decided (ambiguous name, unknown receiver).</summary>
        public int Undecided;
        public readonly Dictionary<string, int> UndecidedByName = new Dictionary<string, int>(StringComparer.Ordinal);

        public ObsoleteCallScanner(ObsoleteApiList list) { _list = list; }

        /// <summary>Adds a file. Call <see cref="Scan"/> after every file is added.</summary>
        public void Add(string relativePath, string[] lines)
        {
            var f = new SourceFile { Path = relativePath.Replace('\\', '/'), Raw = lines,
                                     Code = new string[lines.Length], Comment = new string[lines.Length] };
            bool block = false, verbatim = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string comment;
                f.Code[i] = Strip(lines[i], ref block, ref verbatim, out comment);
                f.Comment[i] = comment;
            }
            for (int i = 0; i < f.Code.Length; i++)
            {
                var c = f.Code[i];
                foreach (Match m in DeclarationRx.Matches(c)) AddDeclaration(f, i, m.Groups[2].Value, m.Groups[1].Value);
                foreach (Match m in CastAssignRx.Matches(c)) AddDeclaration(f, i, m.Groups[1].Value, m.Groups[2].Value);
                foreach (Match m in AsAssignRx.Matches(c)) AddDeclaration(f, i, m.Groups[1].Value, m.Groups[2].Value);
                foreach (Match m in AccessorAssignRx.Matches(c)) AddDeclaration(f, i, m.Groups[1].Value, Accessors[m.Groups[2].Value]);
                foreach (Match m in TypeTokenRx.Matches(c)) _typesNamed.Add(m.Groups["t"].Value);
            }
            _files.Add(f);
        }

        private void AddDeclaration(SourceFile f, int line, string name, string type)
        {
            name = name.TrimStart('@');
            f.Declarations.Add(Tuple.Create(line, name, type));
            _typesNamed.Add(type);
            HashSet<string> types;
            if (!_globalTypes.TryGetValue(name, out types)) _globalTypes[name] = types = new HashSet<string>(StringComparer.Ordinal);
            types.Add(type);
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
            Undecided = 0;
            UndecidedByName.Clear();
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
                    string receiver = ReceiverType(f, i, code.Substring(0, m.Index));
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
                            Undecided++;
                            int n;
                            UndecidedByName.TryGetValue(name, out n);
                            UndecidedByName[name] = n + 1;
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

        private string ReceiverType(SourceFile f, int line, string before)
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
            if (ident.Success) return TypeOf(f, line, ident.Groups[1].Value);
            if (before[before.Length - 1] == ')')
            {
                int depth = 0, j = before.Length - 1;
                for (; j >= 0; j--)
                {
                    if (before[j] == ')') depth++;
                    else if (before[j] == '(') { depth--; if (depth == 0) break; }
                }
                if (j < 0) return null;
                var inner = before.Substring(j + 1, before.Length - j - 2).Trim();
                var cast = CastInnerRx.Match(inner);
                if (cast.Success) return cast.Groups[1].Value;
                var asCast = AsInnerRx.Match(inner);
                if (asCast.Success) return asCast.Groups[1].Value;
            }
            return null;
        }

        private string TypeOf(SourceFile f, int line, string name)
        {
            Tuple<int, string, string> best = null, first = null;
            foreach (var d in f.Declarations)
            {
                if (d.Item2 != name) continue;
                if (first == null) first = d;
                if (d.Item1 <= line) best = d;
            }
            if (best != null) return best.Item3;
            if (first != null) return first.Item3;
            HashSet<string> types;
            if (_globalTypes.TryGetValue(name, out types) && types.Count == 1) return types.First();
            return null;
        }

        private string Normalise(string type)
        {
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
        /// and the text of its // comment in <paramref name="comment"/>.
        /// </summary>
        internal static string Strip(string line, ref bool inBlock, ref bool inVerbatim, out string comment)
        {
            comment = null;
            var sb = new StringBuilder(line.Length);
            int i = 0;
            char str = '\0';   // '"' normal string, '@' verbatim string
            if (inVerbatim) str = '@';
            while (i < line.Length)
            {
                char c = line[i];
                char next = i + 1 < line.Length ? line[i + 1] : '\0';
                if (inBlock)
                {
                    if (c == '*' && next == '/') { inBlock = false; sb.Append("  "); i += 2; continue; }
                    sb.Append(' '); i++; continue;
                }
                if (str == '\0')
                {
                    if (c == '/' && next == '/') { comment = line.Substring(i); break; }
                    if (c == '/' && next == '*') { inBlock = true; sb.Append("  "); i += 2; continue; }
                    if (c == '"')
                    {
                        bool verbatim = (i > 0 && line[i - 1] == '@') || (i > 1 && line[i - 1] == '$' && line[i - 2] == '@');
                        str = verbatim ? '@' : '"';
                        sb.Append(' '); i++; continue;
                    }
                    if (c == '\'')
                    {
                        int end = line.IndexOf('\'', i + (next == '\\' ? 3 : 2));
                        if (end > i && end - i <= 7) { sb.Append(' ', end - i + 1); i = end + 1; continue; }
                    }
                    sb.Append(c); i++; continue;
                }
                if (str == '"' && c == '\\') { sb.Append("  "); i += 2; continue; }
                if (c == '"')
                {
                    if (str == '@' && next == '"') { sb.Append("  "); i += 2; continue; }
                    str = '\0'; sb.Append(' '); i++; continue;
                }
                sb.Append(' '); i++;
            }
            inVerbatim = str == '@';
            while (sb.Length < line.Length) sb.Append(' ');
            return sb.ToString();
        }
    }
}
