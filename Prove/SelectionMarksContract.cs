using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
{
    /// <summary>
    /// THE TABLE OF SELECTION MARKS AGREES WITH THE GUIDE, AND IS THE ONLY
    /// PLACE MARKS ARE WRITTEN (Z-C21.6, pattern S9).
    ///
    /// Riscontro.Disegno/SelectionMarks.cs names every non-zero selection mark
    /// the code uses, and quotes the words of the guide page that give it. This
    /// check reads that file as TEXT (no SOLIDWORKS, no interop) and asks:
    ///
    ///   1. every constant cites a page ("Guide: X~Y.html") and either quotes,
    ///      between « », words of the page that contain its value, or is
    ///      UNDOCUMENTED with the bank case that measured it ("MEASURED:");
    ///   2. no SOLIDWORKS selection call in the library carries a non-zero
    ///      literal mark: `Select2(append, N)`, `SelectByMark(append, N)`,
    ///      `SelectByID2(..., append, N, callout, option)`, `.Mark = N`. A new
    ///      call must take its mark from the table, where the page is quoted.
    ///
    /// The quote is checked against the NUMBER, not against the page (the guide
    /// is Dassault's and is not in this repository): what it prevents is a
    /// constant whose value and whose quoted evidence disagree, and a mark typed
    /// at a call site without anyone having read the page.
    /// </summary>
    internal static class SelectionMarksContract
    {
        private static readonly Regex ConstRx = new Regex(@"public\s+const\s+int\s+(\w+)\s*=\s*(\d+)\s*;", RegexOptions.Compiled);
        private static readonly Regex GuideRx = new Regex(@"Guide:\s*(\w+~\w+\.html)", RegexOptions.Compiled);
        private static readonly Regex QuoteRx = new Regex(@"«([^»]*)»", RegexOptions.Compiled);
        private static readonly Regex MeasuredRx = new Regex(@"MEASURED:\s*([A-Za-z0-9_\-]+)", RegexOptions.Compiled);
        private static readonly Regex MarkAssignRx = new Regex(@"\.\s*Mark\s*=\s*([1-9]\d*)\b", RegexOptions.Compiled);

        internal static void RunAll()
        {
            Gruppo("the selection marks come from the table (Z-C21.6, S9)");

            var tablePath = Path.Combine(Radice(), "Riscontro.Disegno", "SelectionMarks.cs");
            if (!File.Exists(tablePath))
            {
                Segna("the table of marks is there", false, tablePath);
                return;
            }
            var cases = new HashSet<string>(StringComparer.Ordinal);
            var casesDir = Path.Combine(Radice(), "Banco", "casi");
            if (Directory.Exists(casesDir))
                foreach (var f in Directory.GetFiles(casesDir, "*.json", SearchOption.AllDirectories))
                    cases.Add(Path.GetFileNameWithoutExtension(f));

            // ── TEETH FIRST, ON A TABLE WRITTEN TO BE WRONG ──────────────
            var bad = string.Join("\n", new[]
            {
                "/// Guide: IFoo~Bar.html, «Mark = 4 to select the path».",
                "public const int WrongNumber = 2;",
                "/// no guide here",
                "public const int NoGuide = 1;",
                "/// Guide: IFoo~Baz.html, which says nothing.",
                "/// UNDOCUMENTED: measured once.",
                "/// MEASURED: a_case_that_does_not_exist",
                "public const int UnknownCase = 1;",
                "/// Guide: IFoo~Ok.html, «Features to be mirrored 1».",
                "public const int Right = 1;",
            });
            var badProblems = TableProblems(bad, cases, out int badCount);
            bool teeth = badCount == 4 && badProblems.Count == 3 &&
                         badProblems.Any(p => p.StartsWith("WrongNumber")) &&
                         badProblems.Any(p => p.StartsWith("NoGuide")) &&
                         badProblems.Any(p => p.StartsWith("UnknownCase"));
            Segna("the table rule catches a wrong number, a missing page, an unknown case", teeth,
                  teeth ? "3 of 3 wrong constants caught, the right one left alone" : string.Join(" | ", badProblems));

            var literalSamples = new[]
            {
                "if (!feat.Select2(true, 4)) return;",
                "sd.Mark = 2;",
                "modello.Extension.SelectByID2(n, \"PLANE\", 0, 0, 0, false,\n    1, null, 0);",
                "e.SelectByMark(false, 1);",
            };
            var fineSamples = new[]
            {
                "if (!feat.Select2(true, SelectionMarks.SweepPath)) return;",
                "feat.Select2(false, 0);",
                "modello.Extension.SelectByID2(n, \"PLANE\", 0, 0, 0, false, 0, null, 0);",
                "sd.Mark = SelectionMarks.HoleTableHoles;",
                "// Select2(true, 4) is what the old code did",
            };
            int caught = literalSamples.Count(s => LiteralMarks("t.cs", s).Count == 1);
            var falseAlarms = fineSamples.Where(s => LiteralMarks("t.cs", s).Count != 0).ToList();
            Segna("the literal-mark rule catches the four call shapes", caught == literalSamples.Length,
                  caught + " of " + literalSamples.Length + " (Select2, .Mark =, SelectByID2 over two lines, SelectByMark)");
            Segna("and leaves marks from the table, mark 0 and comments alone", falseAlarms.Count == 0,
                  falseAlarms.Count == 0 ? fineSamples.Length + " lines, no false alarm" : string.Join(" | ", falseAlarms));

            // ── THE REAL TABLE ───────────────────────────────────────────
            var problems = TableProblems(File.ReadAllText(tablePath), cases, out int constants);
            Segna("every mark in the table cites its page and agrees with it", problems.Count == 0 && constants >= 10,
                  problems.Count == 0 ? constants + " marks, each with its page and quote (or its measuring case)"
                                      : string.Join(" | ", problems));

            // ── THE REAL SOURCE ──────────────────────────────────────────
            var found = new List<string>();
            int used = 0;
            foreach (var folder in new[] { "Riscontro.Disegno", "Ponte" })
            {
                var dir = Path.Combine(Radice(), folder);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    if (f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                        f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                    if (Path.GetFileName(f) == "SelectionMarks.cs") continue;
                    var text = File.ReadAllText(f);
                    used += Regex.Matches(text, @"\bSelectionMarks\.\w+").Count;
                    found.AddRange(LiteralMarks(Path.GetFileName(f), text));
                }
            }
            Segna("no selection call carries a non-zero literal mark", found.Count == 0,
                  found.Count == 0 ? used + " marks taken from the table" : string.Join(" | ", found));
        }

        /// <summary>The constants of a table text that break the rules.</summary>
        internal static List<string> TableProblems(string text, HashSet<string> cases, out int constants)
        {
            var problems = new List<string>();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            constants = 0;
            var doc = new List<string>();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                var m = ConstRx.Match(line);
                if (m.Success)
                {
                    constants++;
                    string name = m.Groups[1].Value, value = m.Groups[2].Value;
                    var block = string.Join(" ", doc);
                    doc.Clear();
                    if (!GuideRx.IsMatch(block)) { problems.Add(name + ": no \"Guide: Interface~Member.html\""); continue; }
                    if (block.Contains("UNDOCUMENTED:"))
                    {
                        var measured = MeasuredRx.Match(block);
                        if (!measured.Success || !cases.Contains(measured.Groups[1].Value))
                            problems.Add(name + ": UNDOCUMENTED needs \"MEASURED: <bank case>\" with a case in Banco/casi" +
                                         (measured.Success ? " («" + measured.Groups[1].Value + "» is not)" : ""));
                        continue;
                    }
                    var quote = QuoteRx.Match(block);
                    if (!quote.Success) { problems.Add(name + ": no quote of the page between « »"); continue; }
                    if (!Regex.IsMatch(quote.Groups[1].Value, @"(?<![\d.])" + value + @"(?![\d.])"))
                        problems.Add(name + " = " + value + ": the quote «" + quote.Groups[1].Value + "» does not give " + value);
                    continue;
                }
                if (line.StartsWith("///", StringComparison.Ordinal)) doc.Add(line.Substring(3));
                else if (line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal)) doc.Clear();
            }
            return problems;
        }

        /// <summary>Selection calls with a non-zero literal mark in a source text.</summary>
        internal static List<string> LiteralMarks(string file, string text)
        {
            var found = new List<string>();
            // Comments and strings blanked, lines kept, so that positions map back.
            var raw = text.Replace("\r\n", "\n").Split('\n');
            bool block = false, verbatim = false;
            var sb = new StringBuilder();
            var lineStart = new List<int>();
            foreach (var r in raw)
            {
                string comment;
                lineStart.Add(sb.Length);
                sb.Append(ObsoleteCallScanner.Strip(r, ref block, ref verbatim, out comment)).Append('\n');
            }
            var code = sb.ToString();
            Func<int, int> lineOf = pos => { int i = lineStart.BinarySearch(pos); return (i >= 0 ? i : ~i - 1) + 1; };

            foreach (Match m in MarkAssignRx.Matches(code))
                found.Add(file + ":" + lineOf(m.Index) + " .Mark = " + m.Groups[1].Value);
            foreach (var call in new[] { "Select2", "SelectByMark", "SelectByID2" })
            {
                foreach (Match m in Regex.Matches(code, @"\.\s*" + call + @"\s*\("))
                {
                    var args = Arguments(code, m.Index + m.Length);
                    if (args == null) continue;
                    int markAt = call == "SelectByID2" ? 6 : 1;
                    if (args.Count <= markAt) continue;
                    var mark = args[markAt].Trim();
                    if (Regex.IsMatch(mark, @"^[1-9]\d*$"))
                        found.Add(file + ":" + lineOf(m.Index) + " " + call + " with literal mark " + mark);
                }
            }
            return found;
        }

        /// <summary>The top-level arguments of a call whose "(" ends at <paramref name="start"/>.</summary>
        private static List<string> Arguments(string code, int start)
        {
            var args = new List<string>();
            int depth = 0, from = start;
            for (int i = start; i < code.Length; i++)
            {
                char c = code[i];
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}')
                {
                    if (depth == 0) { args.Add(code.Substring(from, i - from)); return args; }
                    depth--;
                }
                else if (c == ',' && depth == 0) { args.Add(code.Substring(from, i - from)); from = i + 1; }
            }
            return null;
        }
    }
}
