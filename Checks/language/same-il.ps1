# =============================================================================
#  same-il.ps1 - the same assemblies after a translation? (W3.1-bis.2, item 8)
# =============================================================================
#
#  WHY IT EXISTS
#
#  The comment batches of W3.1-bis (.7) touch every .cs file of the
#  repository and must not change one instruction; the message batches (.8,
#  .9) must change only string literals; the rename windows (.6) only names,
#  and only as the name map says. The bench is the proof that behaviour did
#  not change, but it costs a night of SOLIDWORKS. This compares what the
#  compiler produced, before and after, in a minute.
#
#  HOW
#
#  Each assembly is copied alone into a temporary folder (so that ildasm reads
#  no .pdb: line numbers and local names stay out) and disassembled with
#  `ildasm /caverbal /utf8 /out=` (the .NET Framework 4.8 tools of the Windows
#  SDK; without /linenum). Into a file and not with /text: on the console
#  ildasm writes in the OEM code page, and a character outside ASCII in the
#  string argument of an attribute came out as a replacement character
#  (measured on 30/9/2026), so two different strings would have compared
#  equal. The text is then normalized:
#    - the MVID, the image base, ildasm's banner and the comment lines that
#      announce the resource files it wrote are dropped: they change from one
#      build to the next, or with the tool and the language of Windows, not
#      with the source;
#    - a statement that ildasm wrapped on several lines becomes one line, and
#      the byte comments of hex dumps go away;
#    - a string literal is one quoted string: the `+` continuations of a long
#      literal are joined, and a `bytearray (...)` (how ildasm writes a string
#      with a character outside ASCII) is decoded;
#    - every managed resource that ildasm writes next to the text becomes one
#      line with its SHA-256; the Win32 version block becomes one line of its
#      strings (it names the file, so a renamed assembly can be explained by
#      the map), any other Win32 resource one line with its SHA-256.
#  An output without an `.assembly` line stops the comparison: two
#  disassemblies that read nothing would compare equal.
#  The two texts are compared with `git diff --no-index`, and each changed
#  line is judged by the mode.
#
#  THREE MODES
#
#    Identical   the texts are the same: 0 differing lines. A translation of
#                COMMENTS must give this (.7).
#    Ldstr       every differing line differs only inside string literals
#                (ldstr, const string fields, string arguments of custom
#                attributes), line for line. Messages and descriptions (.8,
#                .9). A `switch` on strings with seven cases or more is
#                compiled to a hash table: a changed case label changes its
#                hash constants, and this mode reports them. It is right to:
#                a case label is protocol, not a message.
#    Renames     every differing line is explained by the name map: whole
#                words (or dotted runs of words) of the old line replaced
#                with pairs of the map give the new line. The rename windows
#                (.6). The same limit on string switches applies when a case
#                label is renamed.
#
#  The name map is a TSV. With a header naming its columns (old/new,
#  from/to or it/en) those columns are used; otherwise the first two. Lines
#  that start with # are comments. Same rules as token-stream.js.
#
#  USAGE
#    .\Checks\language\same-il.ps1 -Before <dll|exe|folder> -After <dll|exe|folder> [-Mode Identical|Ldstr|Renames] [-Map <tsv>]
#        two folders: every .dll and .exe at the top of them, paired by name
#        (in Renames mode, a name the map explains pairs too)
#    .\Checks\language\same-il.ps1 -SelfTest
#        compiles a probe with csc and sees every mode fail and pass:
#        two builds of the same source give 0 lines, a changed literal gives
#        exactly 1, a changed instruction is not a literal, a rename outside
#        the map is not explained. About 20 seconds.
#    .\Checks\language\same-il.ps1 -OnProjects [-Projects <csproj>...] [-Source <folder>]
#        the same proof on the real projects: a copy of the tracked files
#        (or of every file under -Source, for instance a `git archive` of a
#        commit) in a temporary folder, each project rebuilt twice and once
#        more with one string literal changed. The working tree is never
#        built. Needs MSBuild and, for the projects that reference it,
#        SOLIDWORKS's interop assemblies on disk (not SOLIDWORKS running).
#        Some minutes.
#    -Show <n>   differing lines printed per assembly (default 10)
#    -Json       also print the result as one JSON object on the last line
#
#  EXIT CODES
#    0  no difference outside the mode (or -SelfTest / -OnProjects as expected)
#    1  a difference outside the mode, or a self-test not as expected
#    2  cannot run: no ildasm, no git, a missing file, a bad map
# =============================================================================

[CmdletBinding()]
param(
    [string] $Before,
    [string] $After,
    [ValidateSet('Identical', 'Ldstr', 'Renames')]
    [string] $Mode = 'Identical',
    [string] $Map,
    [int] $Show = 10,
    [switch] $Json,
    [switch] $SelfTest,
    [switch] $OnProjects,
    [string[]] $Projects,
    [string] $Source,
    [string] $Ildasm
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
# the samples print literals as they are, accented letters included
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

class CannotRun : System.Exception {
    CannotRun([string] $m) : base($m) {}
}
function Stop-Cannot([string] $m) { throw [CannotRun]::new($m) }

# -----------------------------------------------------------------------------
#  The work that has to be fast is C#: Oce.Solid.Agent.Core.dll disassembles to
#  about 240,000 lines (measured on 30/9/2026), and a PowerShell loop over
#  them takes minutes.
#  C# 5 on purpose: Add-Type in Windows PowerShell compiles with that.
# -----------------------------------------------------------------------------
Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

public class SameIlResult
{
    public int Differing;
    public int Explained;
    public int Unexplained;
    public int Rewritten;
    public List<string> Samples = new List<string>();
    // the lines not explained, by the method they sit in
    public SortedDictionary<string, int> ByMethod = new SortedDictionary<string, int>(StringComparer.Ordinal);
}

public static class SameIlCore
{
    static readonly Regex Drop = new Regex(@"^(// MVID:|// Image base:|//\s+Microsoft \(R\)|//\s+Copyright|// WARNING: Created Win32 resource file|// Time-date stamp:|// Checksum:|// Offset: 0x[0-9A-F]+ Length: 0x[0-9A-F]+$)");
    static readonly Regex HexContinuation = new Regex(@"^((?:[0-9A-F]{2} )+\)?)\s*(//.*)?$");
    static readonly Regex HexFirst = new Regex(@"^(.*(?:bytearray|=)\s*\((?:\s*[0-9A-F]{2})*\s*\)?)\s*//.*$");
    static readonly Regex Starts = new Regex(@"^(\.|IL_[0-9a-f]{4,}:|\{|\}|//|catch\b|finally\b|fault\b|filter\b)");
    static readonly Regex LdstrLine = new Regex(@"^(IL_[0-9a-f]+:\s+ldstr\s+)(.*)$");
    static readonly Regex ConstLine = new Regex(@"^(\.field\b.*\bliteral\s+string\s+.*?=\s*)(.*)$");
    static readonly Regex ByteArray = new Regex(@"^bytearray\s*\(([0-9A-F\s]*)\)\s*$");

    // `written` are the names of the files ildasm wrote next to the text: the
    // comment lines that announce them carry a temporary path and are worded
    // in the language of the installation, so they go by what they name.
    public static string[] Normalize(string[] raw, string[] written)
    {
        var lines = new List<string>();
        StringBuilder cur = null;
        bool banner = true;
        foreach (var r in raw)
        {
            var t = r.Trim();
            if (t.Length == 0) { Flush(ref cur, lines); continue; }
            // ildasm's banner and the metadata version: comment lines before
            // the first directive
            if (banner && t.StartsWith("//")) continue;
            banner = false;
            if (Drop.IsMatch(t) || MvidLike.IsMatch(t)) continue;
            if (t.StartsWith("//") && Announces(t, written)) continue;
            var hex = HexContinuation.Match(t);
            bool continuation = hex.Success && cur != null;
            if (continuation) t = hex.Groups[1].Value.TrimEnd();
            else
            {
                var first = HexFirst.Match(t);
                if (first.Success) t = first.Groups[1].Value.TrimEnd();
            }
            if (!continuation && (cur == null || Starts.IsMatch(t))) { Flush(ref cur, lines); cur = new StringBuilder(t); }
            else cur.Append(' ').Append(t);
        }
        Flush(ref cur, lines);
        for (int i = 0; i < lines.Count; i++) lines[i] = DataLabel.Replace(Literal(lines[i]), "I_<data>");
        return lines.ToArray();
    }

    // The address of a static data blob (`at I_000C4760`, `.data cil
    // I_000C4760 = bytearray (...)`) moves whenever the metadata before it
    // changes size, as a rename does; the bytes of the blob stay compared.
    // The offset and length comment of a managed resource go too: its sha256
    // line says whether its bytes changed.
    static readonly Regex DataLabel = new Regex(@"(?<=\bat |\.data (?:cil |tls )?)I_[0-9A-F]{8}\b");

    static readonly Regex MvidLike = new Regex(@"^//.*\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}\s*$");

    static bool Announces(string line, string[] written)
    {
        foreach (var w in written) if (w.Length > 0 && line.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    static void Flush(ref StringBuilder cur, List<string> lines)
    {
        if (cur != null) lines.Add(cur.ToString());
        cur = null;
    }

    // ldstr and const string: one quoted string, whatever ildasm did with it
    static string Literal(string line)
    {
        var m = LdstrLine.Match(line);
        if (!m.Success) m = ConstLine.Match(line);
        if (!m.Success) return line;
        var arg = m.Groups[2].Value.Trim();
        var ba = ByteArray.Match(arg);
        if (ba.Success)
        {
            var parts = ba.Groups[1].Value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++) bytes[i] = Convert.ToByte(parts[i], 16);
            return m.Groups[1].Value + Quote(Encoding.Unicode.GetString(bytes));
        }
        // "a" + "b" + "c": the raw contents joined, escapes kept as they are
        var sb = new StringBuilder();
        int p = 0;
        bool any = false;
        while (p < arg.Length)
        {
            while (p < arg.Length && (arg[p] == ' ' || arg[p] == '+')) p++;
            if (p >= arg.Length) break;
            if (arg[p] != '"') return line;
            int q = p + 1;
            while (q < arg.Length && arg[q] != '"') { if (arg[q] == '\\') q++; q++; }
            if (q >= arg.Length) return line;
            sb.Append(arg, p + 1, q - p - 1);
            any = true;
            p = q + 1;
        }
        return any ? m.Groups[1].Value + "\"" + sb.ToString() + "\"" : line;
    }

    static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            if (c == '"') sb.Append("\\\"");
            else if (c == '\\') sb.Append("\\\\");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    // Every string literal of an IL line replaced with one mark: quoted
    // strings, and the string('...') of a custom attribute written verbally.
    // A single-quoted IL NAME ('<>c') is not a literal and stays.
    public static string WithoutLiterals(string line)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            bool attr = c == '\'' && i >= 7 && string.CompareOrdinal(line, i - 7, "string(", 0, 7) == 0;
            if (c == '"' || attr)
            {
                char close = c;
                int q = i + 1;
                while (q < line.Length && line[q] != close) { if (line[q] == '\\') q++; q++; }
                sb.Append(close).Append('\u0001').Append(close);
                i = q + 1;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    public static List<string> Words(string s)
    {
        var list = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            if (IsWord(s[i]))
            {
                int j = i;
                while (j < s.Length && IsWord(s[j])) j++;
                // an accessor is its prefix and the name of the member: get_Name, set_Name, add_Name, remove_Name
                int cut = 0;
                foreach (var pre in new[] { "get_", "set_", "add_", "remove_" })
                    if (j - i > pre.Length && string.CompareOrdinal(s, i, pre, 0, pre.Length) == 0 && char.IsLetter(s[i + pre.Length])) { cut = pre.Length; break; }
                if (cut > 0) { list.Add(s.Substring(i, cut)); i += cut; }
                // `<>3__name`: the number of the generated field and the name it carries
                var gen = Regex.Match(s.Substring(i, j - i), @"^[0-9]+__(?=[A-Za-z])");
                if (gen.Success) { list.Add(gen.Value); i += gen.Length; }
                list.Add(s.Substring(i, j - i));
                i = j;
            }
            else { list.Add(s.Substring(i, 1)); i++; }
        }
        return list;
    }

    static bool IsWord(char c) { return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'; }

    // The map: first word of the old name -> pairs (old words, new words).
    public static Dictionary<string, List<string[][]>> MapOf(string[] olds, string[] news)
    {
        var map = new Dictionary<string, List<string[][]>>(StringComparer.Ordinal);
        for (int i = 0; i < olds.Length; i++)
        {
            var o = Words(olds[i]).ToArray();
            var n = Words(news[i]).ToArray();
            List<string[][]> list;
            if (!map.TryGetValue(o[0], out list)) { list = new List<string[][]>(); map[o[0]] = list; }
            list.Add(new[] { o, n });
        }
        return map;
    }

    static bool At(List<string> x, int i, string[] w)
    {
        if (i + w.Length > x.Count) return false;
        for (int k = 0; k < w.Length; k++) if (x[i + k] != w[k]) return false;
        return true;
    }

    // b is a with whole words (or runs of words) replaced by pairs of the map;
    // a search over positions, so a word with two rows is tried both ways.
    public static bool ExplainedByMap(string a, string b, Dictionary<string, List<string[][]>> map)
    {
        // ildasm quotes a name that is an IL keyword (error, strict, value...): a rename that makes a name
        // a keyword adds quotes, and they mean nothing
        var A = Words(a.Replace("'", ""));
        var B = Words(b.Replace("'", ""));
        var seen = new HashSet<long>();
        var stack = new Stack<long>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            long s = stack.Pop();
            int i = (int)(s >> 32), j = (int)(s & 0xffffffff);
            if (i == A.Count && j == B.Count) return true;
            if (!seen.Add(s)) continue;
            if (i < A.Count && j < B.Count && A[i] == B[j]) stack.Push(((long)(i + 1) << 32) | (uint)(j + 1));
            List<string[][]> pairs;
            if (i < A.Count && map.TryGetValue(A[i], out pairs))
                foreach (var p in pairs)
                    if (At(A, i, p[0]) && At(B, j, p[1])) stack.Push(((long)(i + p[0].Length) << 32) | (uint)(j + p[1].Length));
        }
        return false;
    }

    // Renames mode: the old text rewritten first with the pairs that have
    // one row only, so that git's diff lines up the rest; what is left is
    // judged by ExplainedByMap, which knows the words with two rows.
    public static string[] Substitute(string[] lines, Dictionary<string, List<string[][]>> map)
    {
        var single = new Dictionary<string, List<string[][]>>(StringComparer.Ordinal);
        var rowsPerOld = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in map) foreach (var p in kv.Value)
        {
            var k = string.Join("\u0001", p[0]);
            int n; rowsPerOld.TryGetValue(k, out n); rowsPerOld[k] = n + 1;
        }
        foreach (var kv in map)
        {
            var list = new List<string[][]>();
            foreach (var p in kv.Value) if (rowsPerOld[string.Join("\u0001", p[0])] == 1) list.Add(p);
            list.Sort((x, y) => y[0].Length.CompareTo(x[0].Length));
            if (list.Count > 0) single[kv.Key] = list;
        }
        var result = new string[lines.Length];
        for (int li = 0; li < lines.Length; li++)
        {
            var w = Words(lines[li]);
            var sb = new StringBuilder();
            int i = 0;
            while (i < w.Count)
            {
                List<string[][]> cands;
                string[][] hit = null;
                if (single.TryGetValue(w[i], out cands)) foreach (var p in cands) if (At(w, i, p[0])) { hit = p; break; }
                if (hit != null) { foreach (var x in hit[1]) sb.Append(x); i += hit[0].Length; }
                else { sb.Append(w[i]); i++; }
            }
            result[li] = sb.ToString();
        }
        return result;
    }

    static readonly Regex NestedStart = new Regex(@"^(\s*)\.class\b.*\bnested\b");

    // The order of the lines of a disassembly in which every run of nested classes is sorted by the name
    // of the class. Roslyn writes the synthesized nested types (the iterator of a method, a closure) in
    // the order of their names, so a renamed method moves its iterator class among its brothers; the
    // same text with the runs sorted is the same text whatever the names were.
    static readonly Regex NestedNameRx = new Regex(@"\.class\b.*?\bnested\s+(?:[a-z]+\s+)*?('(?:[^']|'')+'|[^\s']+)(?=\s+(?:extends|implements)\b|\s*$)");

    // the name of the nested class a `.class ... nested ... name extends ...` line declares
    static string NestedName(string line)
    {
        var m = NestedNameRx.Match(line);
        return m.Success ? m.Groups[1].Value : line.Trim();
    }

    public static string[] Permute(string[] lines, int[] order)
    {
        var r = new string[order.Length];
        for (int i = 0; i < order.Length; i++) r[i] = lines[order[i]];
        return r;
    }

    public static int[] NestedOrder(string[] lines, Dictionary<string, List<string[][]>> map)
    {
        // every word of a name as its Italian word: the old words are themselves, the new ones go back to their row
        var back = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in map) foreach (var p in kv.Value)
            if (p[0].Length == 1 && p[1].Length == 1 && !back.ContainsKey(p[1][0])) back[p[1][0]] = p[0][0];
        return SortNested(lines, 0, lines.Length, back).ToArray();
    }

    static List<int> SortNested(string[] key, int from, int to, Dictionary<string, string> back)
    {
        var res = new List<int>();
        int i = from;
        while (i < to)
        {
            var m = NestedStart.Match(key[i]);
            if (!m.Success) { res.Add(i); i++; continue; }
            string indent = m.Groups[1].Value;
            var blocks = new List<KeyValuePair<string, List<int>>>();
            while (i < to)
            {
                var mm = NestedStart.Match(key[i]);
                if (!mm.Success || mm.Groups[1].Value != indent) break;
                string name = NestedName(key[i]);
                int j = i + 1;
                while (j < to && !(key[j].TrimStart().StartsWith("}", StringComparison.Ordinal) && key[j].Contains("// end of class " + name))) j++;
                var list = new List<int> { i };
                list.AddRange(SortNested(key, i + 1, Math.Min(j, to), back));
                if (j < to) list.Add(j);
                // the blank lines after the class go with it
                int k = j + 1;
                while (k < to && key[k].Trim().Length == 0) { list.Add(k); k++; }
                var canon = new StringBuilder();
                foreach (var w in Words(name)) { string old; canon.Append(back.TryGetValue(w, out old) ? old : w); }
                blocks.Add(new KeyValuePair<string, List<int>>(canon.ToString(), list));
                i = k;
            }
            // a stable sort: the brothers with one name keep their order
            var ordered = new List<KeyValuePair<string, List<int>>>(blocks);
            for (int a = 1; a < ordered.Count; a++)
            {
                var x = ordered[a]; int b = a - 1;
                while (b >= 0 && string.CompareOrdinal(ordered[b].Key, x.Key) > 0) { ordered[b + 1] = ordered[b]; b--; }
                ordered[b + 1] = x;
            }
            foreach (var blk in ordered) res.AddRange(blk.Value);
        }
        return res;
    }

    // git diff --no-index -U0, hunk by hunk: a hunk with as many old lines as
    // new ones is judged line by line, any other is unexplained as a whole.
    public static SameIlResult Compare(string beforeFile, string afterFile, string mode,
                                       Dictionary<string, List<string[][]>> map, int show, string[] originalBefore)
    {
        var psi = new ProcessStartInfo("git", "diff --no-index --no-color --text -U0 -- \"" + beforeFile + "\" \"" + afterFile + "\"");
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = new UTF8Encoding(false);
        psi.CreateNoWindow = true;
        var proc = Process.Start(psi);
        var errTask = proc.StandardError.ReadToEndAsync();
        var text = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode > 1) throw new Exception("git diff --no-index failed: " + errTask.Result);
        var res = new SameIlResult();
        var removed = new List<string>();
        var added = new List<string>();
        int hunkStart = 0;
        foreach (var raw in text.Split('\n'))
        {
            var l = raw.TrimEnd('\r');
            if (l.StartsWith("@@"))
            {
                Judge(removed, added, originalBefore, hunkStart, mode, map, show, res);
                var hm = Regex.Match(l, @"^@@ -(\d+)");
                hunkStart = hm.Success ? int.Parse(hm.Groups[1].Value) - 1 : 0;
                continue;
            }
            if (l.StartsWith("---") || l.StartsWith("+++")) continue;
            if (l.StartsWith("-")) removed.Add(l.Substring(1));
            else if (l.StartsWith("+")) added.Add(l.Substring(1));
        }
        Judge(removed, added, originalBefore, hunkStart, mode, map, show, res);
        return res;
    }

    static readonly Regex MethodLine = new Regex(@"^\s*\.method\b.*?([^\s(]+)\(");
    static readonly Regex ClassLine = new Regex(@"^\s*\.class\b.*?(?:nested\s+)?(?:[a-z]+\s+)*?('(?:[^']|'')+'|[^\s']+)(?=\s+(?:extends|implements)\b|\s*$)");

    // the method (or, before any, the class) the line before `at` belongs to
    static string Where(SameIlResult res, string[] before, int at, int lines)
    {
        string where = "(the top)";
        if (before != null)
            for (int k = Math.Min(at, before.Length - 1); k >= 0; k--)
            {
                var m = MethodLine.Match(before[k]);
                if (m.Success) { where = m.Groups[1].Value; break; }
                var c = ClassLine.Match(before[k]);
                if (c.Success && before[k].TrimStart().StartsWith(".class", StringComparison.Ordinal)) { where = "class " + c.Groups[1].Value; break; }
            }
        int n; res.ByMethod.TryGetValue(where, out n); res.ByMethod[where] = n + lines;
        return where;
    }

    static void Judge(List<string> removed, List<string> added, string[] originalBefore, int hunkStart, string mode,
                      Dictionary<string, List<string[][]>> map, int show, SameIlResult res)
    {
        if (removed.Count == 0 && added.Count == 0) return;
        res.Differing += Math.Max(removed.Count, added.Count);
        if (removed.Count != added.Count || mode == "Identical")
        {
            res.Unexplained += Math.Max(removed.Count, added.Count);
            var where = Where(res, originalBefore, hunkStart, Math.Max(removed.Count, added.Count));
            if (res.Samples.Count < show)
                res.Samples.Add((removed.Count != added.Count ? removed.Count + " line(s) became " + added.Count + " (in " + where + "):" : "differs (in " + where + "):") +
                                "\n        before: " + Cut(removed.Count > 0 ? removed[0] : "(nothing)") +
                                "\n        after:  " + Cut(added.Count > 0 ? added[0] : "(nothing)"));
        }
        else
        {
            for (int i = 0; i < removed.Count; i++)
            {
                bool ok = mode == "Ldstr"
                    ? WithoutLiterals(removed[i]) == WithoutLiterals(added[i])
                    : ExplainedByMap(originalBefore != null && hunkStart + i < originalBefore.Length ? originalBefore[hunkStart + i] : removed[i], added[i], map);
                if (ok) { res.Explained++; continue; }
                res.Unexplained++;
                var where = Where(res, originalBefore, hunkStart + i, 1);
                if (res.Samples.Count < show)
                    res.Samples.Add((mode == "Ldstr" ? "not only a literal (in " + where + "):" : "not explained by the map (in " + where + "):") +
                                    "\n        before: " + Cut(removed[i]) + "\n        after:  " + Cut(added[i]));
            }
        }
        removed.Clear();
        added.Clear();
    }

    // The Win32 resources ildasm writes (.res): the version block becomes its
    // strings, one line (so that a renamed assembly, whose file name sits in
    // it, can be explained by the map); any other resource is a type, a name
    // and a SHA-256. Null when the file is not a .res this can read.
    public static List<string> Win32ResourceLines(byte[] b)
    {
        var lines = new List<string>();
        int p = 0;
        var sha = System.Security.Cryptography.SHA256.Create();
        while (p + 8 <= b.Length)
        {
            int dataSize = BitConverter.ToInt32(b, p);
            int headerSize = BitConverter.ToInt32(b, p + 4);
            if (headerSize < 16 || p + headerSize + dataSize > b.Length) return null;
            int q = p + 8;
            string type = ResName(b, ref q);
            string name = ResName(b, ref q);
            if (type == null || name == null) return null;
            int data = p + headerSize;
            if (dataSize > 0)
            {
                if (type == "#16")
                {
                    var entries = new List<string>();
                    if (!VersionBlock(b, data, data + dataSize, entries)) return null;
                    lines.Add(".win32-version " + string.Join(" | ", entries.ToArray()));
                }
                else
                {
                    var h = sha.ComputeHash(b, data, dataSize);
                    lines.Add(".win32-resource type " + type + " name " + name + " sha256 " + BitConverter.ToString(h).Replace("-", "").ToLowerInvariant());
                }
            }
            p = data + dataSize;
            p = (p + 3) & ~3;
        }
        return lines;
    }

    // VS_VERSIONINFO: blocks of { length, value length, type, key, value,
    // children }; a text value becomes key=value, a binary one key:hex.
    static bool VersionBlock(byte[] b, int start, int end, List<string> entries)
    {
        int p = start;
        while (p + 6 <= end)
        {
            int len = b[p] | (b[p + 1] << 8);
            int valueLength = b[p + 2] | (b[p + 3] << 8);
            int type = b[p + 4] | (b[p + 5] << 8);
            if (len == 0) break;
            int blockEnd = p + len;
            if (blockEnd > end || len < 6) return false;
            int k = p + 6;
            var key = new StringBuilder();
            while (k + 1 < blockEnd)
            {
                char c = (char)(b[k] | (b[k + 1] << 8));
                k += 2;
                if (c == 0) break;
                key.Append(c);
            }
            k = (k + 3) & ~3;
            int valueBytes = type == 1 ? valueLength * 2 : valueLength;
            if (valueBytes > 0 && k + valueBytes <= blockEnd)
            {
                if (type == 1)
                {
                    var v = Encoding.Unicode.GetString(b, k, valueBytes).TrimEnd('\0');
                    entries.Add(key + "=" + v);
                }
                else entries.Add(key + ":" + BitConverter.ToString(b, k, valueBytes).Replace("-", ""));
            }
            else entries.Add(key.ToString());
            k = (k + valueBytes + 3) & ~3;
            if (k < blockEnd && !VersionBlock(b, k, blockEnd, entries)) return false;
            p = (blockEnd + 3) & ~3;
        }
        return true;
    }

    static string ResName(byte[] b, ref int q)
    {
        if (q + 2 > b.Length) return null;
        if (b[q] == 0xFF && b[q + 1] == 0xFF)
        {
            if (q + 4 > b.Length) return null;
            int id = b[q + 2] | (b[q + 3] << 8);
            q += 4;
            return "#" + id;
        }
        var sb = new StringBuilder();
        while (q + 1 < b.Length)
        {
            char c = (char)(b[q] | (b[q + 1] << 8));
            q += 2;
            if (c == 0) break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static int CountDifferent(string[] a, string[] b)
    {
        int n = 0;
        for (int i = 0; i < a.Length && i < b.Length; i++) if (a[i] != b[i]) n++;
        return n + Math.Abs(a.Length - b.Length);
    }

    static string Cut(string s) { return s.Length > 220 ? s.Substring(0, 217) + "..." : s; }
}
'@

# -----------------------------------------------------------------------------
#  Tools
# -----------------------------------------------------------------------------
function Find-Ildasm {
    if ($Ildasm) { if (Test-Path -LiteralPath $Ildasm) { return $Ildasm }; Stop-Cannot "no ildasm at $Ildasm" }
    if ($env:ILDASM -and (Test-Path -LiteralPath $env:ILDASM)) { return $env:ILDASM }
    $sdk = Join-Path ${env:ProgramFiles(x86)} 'Microsoft SDKs\Windows'
    $found = @(Get-ChildItem -Path $sdk -Filter ildasm.exe -Recurse -ErrorAction SilentlyContinue |
               Where-Object { $_.FullName -match 'NETFX 4\.\d+(\.\d+)? Tools\\ildasm\.exe$' } |
               Sort-Object FullName -Descending)
    if ($found.Count -eq 0) { Stop-Cannot "ildasm not found under $sdk (Windows SDK, NETFX 4.8 Tools); pass -Ildasm" }
    return $found[0].FullName
}

function Find-Csc {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $hit = @(& $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' 2>$null)
        if ($hit.Count -gt 0 -and $hit[0]) { return $hit[0] }
    }
    foreach ($ed in 'BuildTools', 'Community', 'Professional', 'Enterprise') {
        foreach ($base in ${env:ProgramFiles(x86)}, $env:ProgramFiles) {
            $c = Join-Path $base "Microsoft Visual Studio\2022\$ed\MSBuild\Current\Bin\Roslyn\csc.exe"
            if (Test-Path $c) { return $c }
        }
    }
    $fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (Test-Path $fw) { return $fw }
    Stop-Cannot 'no C# compiler found (MSBuild Roslyn or .NET Framework csc)'
}

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $hit = @(& $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null)
        if ($hit.Count -gt 0 -and $hit[0]) { return $hit[0] }
    }
    foreach ($ed in 'BuildTools', 'Community', 'Professional', 'Enterprise') {
        foreach ($base in ${env:ProgramFiles(x86)}, $env:ProgramFiles) {
            $c = Join-Path $base "Microsoft Visual Studio\2022\$ed\MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path $c) { return $c }
        }
    }
    Stop-Cannot 'MSBuild not found'
}

# A native tool with its stderr folded into the output: under
# $ErrorActionPreference = Stop, Windows PowerShell turns every stderr line of
# a native command into a terminating error, and the exit code is what counts.
function Invoke-Native([scriptblock] $command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { return @(& $command 2>&1 | ForEach-Object { "$_" }) } finally { $ErrorActionPreference = $previous }
}

function New-TempFolder([string] $name) {
    $d = Join-Path ([IO.Path]::GetTempPath()) ($name + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $d -Force | Out-Null
    return $d
}

# The name map, as token-stream.js reads it.
function Read-NameMap([string] $file) {
    $olds = New-Object System.Collections.Generic.List[string]
    $news = New-Object System.Collections.Generic.List[string]
    if (-not $file) { return [SameIlCore]::MapOf(@(), @()) }
    if (-not (Test-Path -LiteralPath $file)) { Stop-Cannot "no map at $file" }
    $rows = @([IO.File]::ReadAllLines((Resolve-Path -LiteralPath $file).Path, [Text.Encoding]::UTF8) |
              Where-Object { $_.Trim() -and -not $_.StartsWith('#') } | ForEach-Object { , ($_.TrimStart([char]0xFEFF) -split "`t") })
    if ($rows.Count -eq 0) { Stop-Cannot "the map $file is empty" }
    $from = 0; $to = 1
    $head = @($rows[0] | ForEach-Object { $_.Trim().ToLowerInvariant() })
    foreach ($pair in @(@('old', 'new'), @('from', 'to'), @('it', 'en'), @('italian', 'english'))) {
        if ($head -contains $pair[0] -and $head -contains $pair[1]) {
            $from = [array]::IndexOf($head, $pair[0]); $to = [array]::IndexOf($head, $pair[1])
            $rows = @($rows | Select-Object -Skip 1)
            break
        }
    }
    foreach ($r in $rows) {
        $a = if ($r.Count -gt $from) { $r[$from].Trim() } else { '' }
        $b = if ($r.Count -gt $to) { $r[$to].Trim() } else { '' }
        if (-not $a -or -not $b) { continue }
        # A row that keeps the name (old = new) is kept too: next to another
        # row for the same word it says "renamed here, left as it is there"
        # (a folder name that is also a word of a message), so the word is
        # not substituted blindly and each line is judged both ways.
        $olds.Add($a); $news.Add($b)
    }
    return [SameIlCore]::MapOf($olds.ToArray(), $news.ToArray())
}

# ildasm text of one assembly, normalized, plus one line per resource.
function Get-TheText([string] $assembly, [string] $ildasmExe) {
    if (-not (Test-Path -LiteralPath $assembly)) { Stop-Cannot "no assembly at $assembly" }
    $work = New-TempFolder 'same-il'
    try {
        $copy = Join-Path $work ([IO.Path]::GetFileName($assembly))
        Copy-Item -LiteralPath $assembly -Destination $copy
        $il = Join-Path $work 'disassembly.il'
        $out = Invoke-Native { & $ildasmExe /caverbal /utf8 /nobar "/out=$il" $copy }
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $il)) {
            Stop-Cannot ("ildasm failed on $assembly (exit $LASTEXITCODE): " + (($out | Out-String).Trim()))
        }
        $written = @(Get-ChildItem -LiteralPath $work -File | Where-Object { $_.FullName -ne $copy -and $_.FullName -ne $il } | Sort-Object Name)
        $lines = [SameIlCore]::Normalize([IO.File]::ReadAllLines($il, [Text.Encoding]::UTF8), [string[]] @($written | ForEach-Object { $_.Name }))
        # a disassembly without its manifest is not an empty assembly: it is
        # ildasm that read nothing, and two of those would compare equal
        if (-not ($lines | Where-Object { $_.StartsWith('.assembly ') -and -not $_.StartsWith('.assembly extern ') } | Select-Object -First 1)) {
            Stop-Cannot ("ildasm gave no .assembly line for ${assembly}: " + (($out | Out-String).Trim()))
        }
        $list = New-Object System.Collections.Generic.List[string]
        $list.AddRange([string[]] $lines)
        $sha = [Security.Cryptography.SHA256]::Create()
        foreach ($f in $written) {
            $bytes = [IO.File]::ReadAllBytes($f.FullName)
            if ($f.Name -eq 'disassembly.res') {
                $res = [SameIlCore]::Win32ResourceLines($bytes)
                if ($null -ne $res) { $list.AddRange([string[]] $res.ToArray()); continue }
            }
            $hash = -join ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') })
            $name = if ($f.Name -eq 'disassembly.res') { 'win32 resources' } else { $f.Name }
            $list.Add(".resource-file '$name' sha256 $hash")
        }
        return $list.ToArray()
    } finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Compares two assemblies; returns a SameIlResult.
function Compare-Assembly([string] $a, [string] $b, [string] $mode, $nameMap, [string] $ildasmExe, [int] $show) {
    $ta = Get-TheText $a $ildasmExe
    $tb = Get-TheText $b $ildasmExe
    $rewritten = 0
    $original = $ta
    if ($mode -eq 'Renames') {
        $sub = [SameIlCore]::Substitute($ta, $nameMap)
        $rewritten = [SameIlCore]::CountDifferent($ta, $sub)
        # the runs of nested classes in the order of their names, on both sides (the original lines of the
        # before side follow the rewritten ones, so that a line is judged on its own text)
        $orderBefore = [SameIlCore]::NestedOrder($original, $nameMap)
        $orderAfter = [SameIlCore]::NestedOrder($tb, $nameMap)
        $ta = [SameIlCore]::Permute($sub, $orderBefore)
        $original = [SameIlCore]::Permute($original, $orderBefore)
        $tb = [SameIlCore]::Permute($tb, $orderAfter)
    }
    $work = New-TempFolder 'same-il-diff'
    try {
        $fa = Join-Path $work 'before.il'
        $fb = Join-Path $work 'after.il'
        $enc = New-Object System.Text.UTF8Encoding($false)
        [IO.File]::WriteAllText($fa, (($ta -join "`n") + "`n"), $enc)
        [IO.File]::WriteAllText($fb, (($tb -join "`n") + "`n"), $enc)
        $r = [SameIlCore]::Compare($fa, $fb, $mode, $nameMap, $show, $original)
        $r.Rewritten = $rewritten
        return $r
    } finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# The assembly pairs of two paths (files or folders).
function Get-Pairs([string] $a, [string] $b, [string] $mode, $nameMap) {
    if (-not (Test-Path -LiteralPath $a)) { Stop-Cannot "no such path: $a" }
    if (-not (Test-Path -LiteralPath $b)) { Stop-Cannot "no such path: $b" }
    $da = (Get-Item -LiteralPath $a).PSIsContainer
    $db = (Get-Item -LiteralPath $b).PSIsContainer
    if ($da -ne $db) { Stop-Cannot '-Before and -After must be two files or two folders' }
    if (-not $da) { return , @(, @((Resolve-Path -LiteralPath $a).Path, (Resolve-Path -LiteralPath $b).Path)) }
    $left = @(Get-ChildItem -LiteralPath $a -File | Where-Object { $_.Extension -in '.dll', '.exe' })
    $right = @(Get-ChildItem -LiteralPath $b -File | Where-Object { $_.Extension -in '.dll', '.exe' })
    $pairs = New-Object System.Collections.Generic.List[object]
    $used = @{}
    foreach ($l in $left) {
        $r = $right | Where-Object { $_.Name -eq $l.Name } | Select-Object -First 1
        if (-not $r -and $mode -eq 'Renames') {
            $r = $right | Where-Object { -not $used[$_.Name] -and [SameIlCore]::ExplainedByMap($l.Name, $_.Name, $nameMap) } | Select-Object -First 1
        }
        if (-not $r) { $script:oneSide.Add("only before: " + $l.Name); continue }
        $used[$r.Name] = $true
        $pairs.Add(@($l.FullName, $r.FullName))
    }
    foreach ($r in $right) { if (-not $used[$r.Name]) { $script:oneSide.Add("only after: " + $r.Name) } }
    # comparing nothing is not "the same"
    if ($left.Count -eq 0 -and $right.Count -eq 0) { Stop-Cannot "no .dll or .exe to compare in $a and $b" }
    return , $pairs.ToArray()
}

# -----------------------------------------------------------------------------
#  The self-test: a probe compiled several ways
# -----------------------------------------------------------------------------
function Invoke-SelfTest {
    $ildasmExe = Find-Ildasm
    $csc = Find-Csc
    Write-Output "same-il self-test: ildasm $ildasmExe"
    Write-Output "                   csc    $csc"
    $work = New-TempFolder 'same-il-selftest'
    $long = 'L' * 150
    $source = @'
using System;
using System.ComponentModel;
namespace Probe.Legacy
{
    [Description("attribute text")]
    public class Sample
    {
        public const string Constant = "constant text";
        public static string Short() { return "short text"; }
        // a message that keeps two words of the map, a property (its accessor is get_Label) and two iterators
        // (their classes are written in the order of the names of the methods)
        public static string Say() { return "Short and Alpha stay words of a message"; }
        public static string Label { get { return "label text"; } }
        public static System.Collections.Generic.IEnumerable<int> Alpha() { yield return 1; }
        public static System.Collections.Generic.IEnumerable<int> Middle() { yield return 2; }
        public static string Degree() { return "ten \u00b0C"; }
        public static string Long() { return "LONGTEXT"; }
        public static int Pick(string s)
        {
            switch (s)
            {
                case "alpha": return 1; case "beta": return 2; case "gamma": return 3; case "delta": return 4;
                case "epsilon": return 5; case "zeta": return 6; case "eta": return 7; case "theta": return 8;
                default: return 0;
            }
        }
        public static int Next(int x) { return x + 1; }
        public static Func<int, int> Adder() { return y => y + 1; }
    }
}
'@
    $source = $source.Replace('LONGTEXT', $long)
    $mapFile = Join-Path $work 'map.tsv'
    [IO.File]::WriteAllText($mapFile, "old`tnew`nProbe.Legacy`tProbe.Modern`nSample`tExample`nShort`tBrief`nAlpha`tZulu`nLabel`tTag`n", (New-Object System.Text.UTF8Encoding($false)))
    $partialMap = Join-Path $work 'partial-map.tsv'
    [IO.File]::WriteAllText($partialMap, "old`tnew`nProbe.Legacy`tProbe.Modern`nSample`tExample`n", (New-Object System.Text.UTF8Encoding($false)))

    function Build([string] $name, [string] $text, [string] $resourceText = 'resource one', [switch] $Deterministic) {
        # the resource is named after the namespace, as MSBuild names it
        $resourceName = $(if ($text.Contains('namespace Probe.Modern')) { 'Probe.Modern.probe.txt' } else { 'Probe.Legacy.probe.txt' })
        $dir = Join-Path $work $name
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $cs = Join-Path $dir 'Probe.cs'
        $res = Join-Path $dir 'probe.txt'
        [IO.File]::WriteAllText($cs, $text, (New-Object System.Text.UTF8Encoding($true)))
        [IO.File]::WriteAllText($res, $resourceText, (New-Object System.Text.UTF8Encoding($false)))
        $dll = Join-Path $dir 'Probe.dll'
        $cscArgs = @('/nologo', '/target:library', '/optimize-', "/out:$dll", "/resource:$res,$resourceName")
        if ($Deterministic) { $cscArgs += '/deterministic' }
        $cscOut = Invoke-Native { & $csc @cscArgs $cs }
        if ($LASTEXITCODE -ne 0) { Stop-Cannot ("the probe does not compile ($name): " + ($cscOut | Out-String)) }
        return $dll
    }

    try {
        $base = Build 'base' $source
        Start-Sleep -Milliseconds 1100
        $again = Build 'again' $source
        $detA = Build 'det-a' $source -Deterministic
        $detB = Build 'det-b' $source -Deterministic
        $variants = [ordered]@{
            'comment'   = ('// a comment added on top' + [char]10 + '// and one more line' + [char]10 + $source).Replace('public static int Next(', '/* inline */ public static int Next(')
            'short'     = $source.Replace('"short text"', '"brief text"')
            'long'      = $source.Replace($long, ('M' + $long.Substring(1)))
            'accent'    = $source.Replace('"ten \u00b0C"', '"ten degrees"')
            'attribute' = $source.Replace('"attribute text"', '"another attribute text"')
            'constant'  = $source.Replace('"constant text"', '"other constant"')
            'code'      = $source.Replace('return x + 1;', 'return x + 2;')
            'case'      = $source.Replace('case "alpha"', 'case "omega"')
            'renamed'   = $source.Replace('Probe.Legacy', 'Probe.Modern').Replace('class Sample', 'class Example').Replace('string Short()', 'string Brief()').Replace('IEnumerable<int> Alpha()', 'IEnumerable<int> Zulu()').Replace('string Label {', 'string Tag {')
        }
        $built = @{}
        foreach ($k in $variants.Keys) { $built[$k] = Build $k $variants[$k] }
        $built['resource'] = Build 'resource' $source 'resource two'

        $map = Read-NameMap $mapFile
        $partial = Read-NameMap $partialMap
        $cases = @(
            @{ Name = 'two builds of the same source'; A = $base; B = $again; Mode = 'Identical'; Map = $null; Differing = 0; Pass = $true },
            @{ Name = 'two deterministic builds'; A = $detA; B = $detB; Mode = 'Identical'; Map = $null; Differing = 0; Pass = $true },
            @{ Name = 'comments added, every line moved'; A = $base; B = $built['comment']; Mode = 'Identical'; Map = $null; Differing = 0; Pass = $true },
            @{ Name = 'a short literal changed'; A = $base; B = $built['short']; Mode = 'Identical'; Map = $null; Differing = 1; Pass = $false },
            @{ Name = 'a short literal changed'; A = $base; B = $built['short']; Mode = 'Ldstr'; Map = $null; Differing = 1; Pass = $true },
            @{ Name = 'a literal ildasm wraps on several lines changed'; A = $base; B = $built['long']; Mode = 'Identical'; Map = $null; Differing = 1; Pass = $false },
            @{ Name = 'a literal with a character outside ASCII changed'; A = $base; B = $built['accent']; Mode = 'Identical'; Map = $null; Differing = 1; Pass = $false },
            @{ Name = 'a literal with a character outside ASCII changed'; A = $base; B = $built['accent']; Mode = 'Ldstr'; Map = $null; Differing = 1; Pass = $true },
            @{ Name = 'a string argument of an attribute changed'; A = $base; B = $built['attribute']; Mode = 'Ldstr'; Map = $null; Differing = 1; Pass = $true },
            @{ Name = 'a const string changed'; A = $base; B = $built['constant']; Mode = 'Ldstr'; Map = $null; Differing = 1; Pass = $true },
            @{ Name = 'an instruction changed'; A = $base; B = $built['code']; Mode = 'Ldstr'; Map = $null; Differing = -1; Pass = $false },
            @{ Name = 'a case label of a string switch changed'; A = $base; B = $built['case']; Mode = 'Ldstr'; Map = $null; Differing = -1; Pass = $false },
            @{ Name = 'an embedded resource changed'; A = $base; B = $built['resource']; Mode = 'Identical'; Map = $null; Differing = 1; Pass = $false },
            @{ Name = 'an embedded resource changed'; A = $base; B = $built['resource']; Mode = 'Ldstr'; Map = $null; Differing = 1; Pass = $false },
            @{ Name = 'renamed as the map says'; A = $base; B = $built['renamed']; Mode = 'Identical'; Map = $null; Differing = -1; Pass = $false },
            @{ Name = 'renamed as the map says'; A = $base; B = $built['renamed']; Mode = 'Renames'; Map = $map; Differing = -1; Pass = $true },
            @{ Name = 'renamed, one pair missing from the map'; A = $base; B = $built['renamed']; Mode = 'Renames'; Map = $partial; Differing = -1; Pass = $false }
        )
        $good = 0
        $bad = 0
        foreach ($c in $cases) {
            $m = if ($c.Map) { $c.Map } else { [SameIlCore]::MapOf(@(), @()) }
            $r = Compare-Assembly $c.A $c.B $c.Mode $m $ildasmExe 3
            $passed = if ($c.Mode -eq 'Identical') { $r.Differing -eq 0 } else { $r.Unexplained -eq 0 }
            $countOk = $c.Differing -lt 0 -or $r.Differing -eq $c.Differing
            $ok = ($passed -eq $c.Pass) -and $countOk
            if ($ok) { $good++ } else { $bad++ }
            $mark = if ($ok) { 'ok' } else { 'XX' }
            $expect = $(if ($c.Pass) { 'pass' } else { 'fail' }) + $(if ($c.Differing -ge 0) { ", $($c.Differing) differing" } else { '' })
            Write-Output ("  {0}  {1} [{2}]: expected {3}; got {4}, {5} differing, {6} unexplained" -f $mark, $c.Name, $c.Mode, $expect,
                          $(if ($passed) { 'pass' } else { 'fail' }), $r.Differing, $r.Unexplained)
            if (-not $ok) { $r.Samples | ForEach-Object { Write-Output ("      " + $_) } }
        }
        # two folders without assemblies: nothing to compare is not a pass
        $emptyA = Join-Path $work 'empty-a'
        $emptyB = Join-Path $work 'empty-b'
        New-Item -ItemType Directory -Path $emptyA, $emptyB -Force | Out-Null
        $refused = $false
        try { Get-Pairs $emptyA $emptyB 'Identical' ([SameIlCore]::MapOf(@(), @())) | Out-Null } catch [CannotRun] { $refused = $true }
        $mark = if ($refused) { 'ok' } else { $bad++; 'XX' }
        Write-Output "  $mark  two folders without assemblies are refused, not compared as equal"
        # the normalization must have had something to take away
        $bytesDiffer = -not ([IO.File]::ReadAllBytes($base).Length -eq [IO.File]::ReadAllBytes($again).Length -and
                             (Compare-Object ([IO.File]::ReadAllBytes($base)) ([IO.File]::ReadAllBytes($again)) -SyncWindow 0 | Select-Object -First 1) -eq $null)
        $detSame = $null -eq (Compare-Object ([IO.File]::ReadAllBytes($detA)) ([IO.File]::ReadAllBytes($detB)) -SyncWindow 0 | Select-Object -First 1)
        $mark = if ($bytesDiffer) { 'ok' } else { $bad++; 'XX' }
        Write-Output "  $mark  the two plain builds differ in their bytes (the MVID), so the 0 above is the normalization at work"
        $mark = if ($detSame) { 'ok' } else { $bad++; 'XX' }
        Write-Output "  $mark  the two deterministic builds are the same bytes (what <Deterministic> buys)"
        Write-Output ''
        Write-Output "$good of $($cases.Count) comparisons as expected."
        $script:exitCode = $(if ($bad -eq 0) { 0 } else { 1 })
    } finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# -----------------------------------------------------------------------------
#  The proof on the real projects, in a copy
# -----------------------------------------------------------------------------
function Invoke-ProjectProof {
    $ildasmExe = Find-Ildasm
    $msbuild = Find-MSBuild
    if (-not $Projects -or $Projects.Count -eq 0) {
        $Projects = @('Oce.Solid.Agent.Core\Oce.Solid.Agent.Core.csproj', 'Oce.Solid.Agent.Catalog\Oce.Solid.Agent.Catalog.csproj',
                      'Oce.Solid.Agent.csproj', 'Tests\Oce.Solid.Agent.ContractTests.csproj', 'Bench\Oce.Solid.Agent.Bench.csproj')
    }
    $work = New-TempFolder 'same-il-projects'
    $from = $root
    if ($Source) {
        if (-not (Test-Path -LiteralPath $Source)) { Stop-Cannot "no folder $Source" }
        $from = (Resolve-Path -LiteralPath $Source).Path
        $files = @(Get-ChildItem -LiteralPath $from -Recurse -File | ForEach-Object { $_.FullName.Substring($from.TrimEnd('\').Length + 1) })
        Write-Output "same-il on the projects: a copy of $from in $work"
    } else {
        $files = @(& git -C $root ls-files)
        if ($LASTEXITCODE -ne 0) { Stop-Cannot 'git ls-files failed' }
        Write-Output "same-il on the projects: a copy of the tracked files in $work"
    }
    try {
        if ($files.Count -eq 0) { Stop-Cannot "no file to copy from $from" }
        foreach ($rel in $files) {
            $src = Join-Path $from $rel
            if (-not (Test-Path -LiteralPath $src)) { continue }
            $dst = Join-Path $work $rel
            $parent = Split-Path -Parent $dst
            if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
            Copy-Item -LiteralPath $src -Destination $dst
        }
        $ns = @{ m = 'http://schemas.microsoft.com/developer/msbuild/2003' }
        $rows = @()
        $allOk = $true
        foreach ($p in $Projects) {
            $proj = Join-Path $work $p
            if (-not (Test-Path -LiteralPath $proj)) { Stop-Cannot "no project $p in the copy" }
            $projDir = Split-Path -Parent $proj
            [xml] $x = Get-Content -LiteralPath $proj -Raw -Encoding UTF8
            $nameNode = Select-Xml -Xml $x -XPath '//m:AssemblyName' -Namespace $ns | Select-Object -First 1
            $typeNode = Select-Xml -Xml $x -XPath '//m:OutputType' -Namespace $ns | Select-Object -First 1
            if (-not $nameNode -or -not $typeNode) { Stop-Cannot "$p has no AssemblyName or OutputType" }
            $file = $nameNode.Node.InnerText + $(if ($typeNode.Node.InnerText -eq 'Library') { '.dll' } else { '.exe' })
            $sources = @(Select-Xml -Xml $x -XPath '//m:Compile/@Include' -Namespace $ns | ForEach-Object { Join-Path $projDir $_.Node.Value })
            if ($sources.Count -eq 0) { Stop-Cannot "$p lists no Compile item" }

            # one plain string literal in a method body, changed by one letter
            # (a message first; else any plain literal passed to a call, never
            # to an attribute, whose argument is not an instruction)
            $literal = $null
            $messageCalls = @('WriteLine', 'Fallito', 'Exception') -join '|'
            foreach ($pattern in ('\b(?:' + $messageCalls + ')\s*\(\s*"([A-Za-z][A-Za-z0-9 ,.:;()-]{5,})"'),
                                 '(?<![\[\w])[A-Za-z_]\w*\s*\(\s*"([A-Za-z][A-Za-z0-9 ,.:;()-]{5,})"') {
                foreach ($s in $sources) {
                    if (-not (Test-Path -LiteralPath $s)) { continue }
                    $text = [IO.File]::ReadAllText($s)
                    $m = [regex]::Match($text, $pattern)
                    if ($m.Success) { $literal = @{ File = $s; Text = $text; Index = $m.Groups[1].Index; Value = $m.Groups[1].Value }; break }
                }
                if ($literal) { break }
            }
            if (-not $literal) { Stop-Cannot "$p has no plain string literal to change" }

            $outs = @()
            foreach ($i in 1, 2, 3) {
                if ($i -eq 3) {
                    $changed = $literal.Text.Substring(0, $literal.Index) + 'Q' + $literal.Text.Substring($literal.Index + 1)
                    [IO.File]::WriteAllText($literal.File, $changed, (New-Object System.Text.UTF8Encoding($true)))
                }
                $out = Join-Path $work ("out$i\" + [IO.Path]::GetFileNameWithoutExtension($p))
                $log = Invoke-Native { & $msbuild $proj /t:Rebuild /p:Configuration=Release "/p:OutDir=$out" /v:q /nologo /nr:false }
                if ($LASTEXITCODE -ne 0) { Stop-Cannot ("MSBuild failed on $p (build $i):`n" + (($log | Select-Object -Last 30) -join "`n")) }
                $outs += (Join-Path $out $file)
            }
            [IO.File]::WriteAllText($literal.File, $literal.Text, (New-Object System.Text.UTF8Encoding($true)))

            $none = [SameIlCore]::MapOf(@(), @())
            $same = Compare-Assembly $outs[0] $outs[1] 'Identical' $none $ildasmExe 3
            $lit = Compare-Assembly $outs[0] $outs[2] 'Identical' $none $ildasmExe 3
            $litLdstr = Compare-Assembly $outs[0] $outs[2] 'Ldstr' $none $ildasmExe 3
            $bytesSame = $null -eq (Compare-Object ([IO.File]::ReadAllBytes($outs[0])) ([IO.File]::ReadAllBytes($outs[1])) -SyncWindow 0 | Select-Object -First 1)
            $ok = $same.Differing -eq 0 -and $lit.Differing -eq 1 -and $litLdstr.Unexplained -eq 0
            if (-not $ok) { $allOk = $false }
            $rows += [pscustomobject]@{
                Project = $p; Assembly = $file; SameSource = $same.Differing; SameBytes = $bytesSame
                LiteralChanged = $lit.Differing; LdstrUnexplained = $litLdstr.Unexplained
                Literal = ([IO.Path]::GetFileName($literal.File) + ': "' + $literal.Value + '"'); Ok = $ok
            }
            $mark = if ($ok) { 'ok' } else { 'XX' }
            Write-Output ("  {0}  {1}: two builds {2} lines (bytes {3}), one literal changed {4} line(s), Ldstr mode {5} unexplained   [{6}]" -f `
                          $mark, $file, $same.Differing, $(if ($bytesSame) { 'identical' } else { 'differ' }), $lit.Differing, $litLdstr.Unexplained, $rows[-1].Literal)
            if (-not $ok) { ($same.Samples + $lit.Samples + $litLdstr.Samples) | ForEach-Object { Write-Output ("      " + $_) } }
        }
        Write-Output ''
        if ($allOk) { Write-Output "same-il: in every project two builds give 0 lines and one changed literal gives exactly 1."; $script:exitCode = 0; return }
        Write-Output 'same-il: NOT AS EXPECTED on the projects above.'
        $script:exitCode = 1
    } finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# -----------------------------------------------------------------------------
#  Main
# -----------------------------------------------------------------------------
$script:oneSide = New-Object System.Collections.Generic.List[string]
$script:exitCode = 2
try {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Stop-Cannot 'git is not on the PATH (same-il uses git diff --no-index)' }
    if ($SelfTest) { Invoke-SelfTest; exit $script:exitCode }
    if ($OnProjects) { Invoke-ProjectProof; exit $script:exitCode }
    if (-not $Before -or -not $After) { Stop-Cannot 'give -Before and -After (or -SelfTest, -OnProjects)' }
    if ($Mode -eq 'Renames' -and -not $Map) { Stop-Cannot '-Mode Renames needs -Map <tsv>' }
    $ildasmExe = Find-Ildasm
    $nameMap = Read-NameMap $Map
    Write-Output "same-il, mode ${Mode}:"
    $pairs = Get-Pairs $Before $After $Mode $nameMap
    $summary = @()
    foreach ($o in $script:oneSide) { Write-Output ("  " + $o) }
    $failed = $script:oneSide.Count
    foreach ($pair in $pairs) {
        $r = Compare-Assembly $pair[0] $pair[1] $Mode $nameMap $ildasmExe $Show
        $name = [IO.Path]::GetFileName($pair[0])
        $bad = if ($Mode -eq 'Identical') { $r.Differing -gt 0 } else { $r.Unexplained -gt 0 }
        if ($bad) { $failed++ }
        $what = if ($Mode -eq 'Identical') { '' } else { ", $($r.Explained) explained by the mode, $($r.Unexplained) not" }
        if ($Mode -eq 'Renames') { $what += " ($($r.Rewritten) lines rewritten by the map before the comparison)" }
        Write-Output ("  {0}: {1} differing lines{2}" -f $name, $r.Differing, $what)
        foreach ($s in $r.Samples) { Write-Output ("      " + $s) }
        if ($Mode -eq 'Renames' -and $r.ByMethod.Count) {
            Write-Output ("      not explained, by method (" + $r.ByMethod.Count + "): " + (($r.ByMethod.GetEnumerator() | ForEach-Object { $_.Key + ' x' + $_.Value }) -join '; '))
        }
        $summary += [pscustomobject]@{ assembly = $name; differing = $r.Differing; explained = $r.Explained; unexplained = $r.Unexplained }
    }
    Write-Output ''
    Write-Output ("same-il: {0} assemblies compared, {1} with differences outside the mode." -f $pairs.Count, $failed)
    if ($Json) { Write-Output (ConvertTo-Json -Compress -InputObject @{ mode = $Mode; assemblies = $summary; failed = $failed }) }
    if ($failed -gt 0) { exit 1 }
    exit 0
} catch [CannotRun] {
    Write-Output ("same-il: " + $_.Exception.Message)
    exit 2
} catch {
    Write-Output ("same-il: cannot run: " + $_.Exception.Message + " (" + $_.InvocationInfo.PositionMessage + ")")
    exit 2
}
