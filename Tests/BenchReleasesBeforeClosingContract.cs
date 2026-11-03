using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE BENCH RELEASES BEFORE IT CLOSES A DOCUMENT (bridge 0.170.13, 3/11/2026).
    ///
    /// SOLIDWORKS faults in slduiu.dll at offset 0x94d513 when the last reference to a co-edge
    /// read from a drawing view is released after the drawing is closed (a probe outside the
    /// repository, 3/11/2026: released after the close, the violation; released before, none).
    /// The bench closes its documents through `CloseTitle`, which collects first
    /// (`ReleaseBeforeClosing`, with the finalizer gate open). No SOLIDWORKS needed: the check
    /// reads the sources of the bench. A `CloseDoc(` of the bench outside `CloseTitle`, a
    /// `CloseTitle` that does not release first, or a release that waits for the finalizers
    /// with the gate still closed, turn it red; each is tried on a changed copy.
    /// </summary>
    internal static class BenchReleasesBeforeClosingContract
    {
        internal static void RunAll()
        {
            Group("the bench releases before it closes a document (bridge 0.170.13)");
            Wiring();
        }

        private const string CloseTitleHead = "private static void CloseTitle(string title)";
        private const string ReleaseHead = "private static void ReleaseBeforeClosing()";
        private const string Released = "ReleaseBeforeClosing();";
        private const string TheClose = "_sw.CloseDoc(title);";
        private const string Opened = "Shared.FinalizerGate.Open();";
        private const string Waits = "GC.WaitForPendingFinalizers();";

        /// <summary>The body of the method whose declaration is <paramref name="head"/>, or null.</summary>
        private static string Body(string text, string head)
        {
            int at = text.IndexOf(head, StringComparison.Ordinal);
            if (at < 0) return null;
            int open = text.IndexOf('{', at);
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) return text.Substring(open, i - open + 1);
            }
            return null;
        }

        /// <summary>The lines of code of a file, without the lines that are comments.</summary>
        private static IEnumerable<string> CodeLines(string text)
        {
            return text.Replace("\r\n", "\n").Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal));
        }

        internal static List<string> Problems(Dictionary<string, string> sources)
        {
            var wrong = new List<string>();
            if (sources == null || sources.Count == 0) { wrong.Add("no source of the bench found"); return wrong; }
            string program;
            if (!sources.TryGetValue("Program.cs", out program)) { wrong.Add("Bench/Program.cs not found"); return wrong; }

            string closeTitle = Body(program, CloseTitleHead);
            string release = Body(program, ReleaseHead);
            if (closeTitle == null) { wrong.Add("no CloseTitle in Bench/Program.cs"); return wrong; }
            if (release == null) { wrong.Add("no ReleaseBeforeClosing in Bench/Program.cs"); return wrong; }

            int released = closeTitle.IndexOf(Released, StringComparison.Ordinal);
            int closed = closeTitle.IndexOf(TheClose, StringComparison.Ordinal);
            if (closed < 0) wrong.Add("CloseTitle does not call _sw.CloseDoc(title)");
            else if (released < 0 || released > closed)
                wrong.Add("CloseTitle closes before it releases: a co-edge of a view is released after its drawing is closed");

            int opened = release.IndexOf(Opened, StringComparison.Ordinal);
            int waits = release.IndexOf(Waits, StringComparison.Ordinal);
            if (waits < 0) wrong.Add("ReleaseBeforeClosing does not wait for the finalizers: the wrappers are still there at the close");
            else if (opened < 0 || opened > waits)
                wrong.Add("ReleaseBeforeClosing waits for the finalizers with the gate closed: it waits for ever");

            foreach (var pair in sources)
            {
                int n = 0;
                foreach (var line in CodeLines(pair.Value))
                {
                    if (!line.Contains("CloseDoc(")) continue;
                    if (pair.Key == "Program.cs" && line.Trim() == TheClose) continue;
                    n++;
                }
                if (n > 0) wrong.Add("Bench/" + pair.Key + ": " + n + " CloseDoc outside CloseTitle, closed with nothing released first");
            }
            int inProgram = CodeLines(program).Count(l => l.Trim() == TheClose);
            if (inProgram != 1) wrong.Add("Bench/Program.cs: " + inProgram + " lines «" + TheClose + "», 1 expected (the one of CloseTitle)");
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "every CloseDoc of the bench goes through CloseTitle, which collects first with the gate open";
            var dir = Path.Combine(Root(), "Bench");
            var sources = new Dictionary<string, string>();
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly))
                    sources[Path.GetFileName(f)] = File.ReadAllText(f);
            var real = Problems(sources);

            // Teeth: each change below must turn the check red.
            var blind = new List<string>();
            int copies = 0;
            Action<string, string, Func<string, string>> mutate = (what, file, change) =>
            {
                string text;
                if (!sources.TryGetValue(file, out text)) { blind.Add(what + " (Bench/" + file + " not found)"); return; }
                var changed = change(text);
                if (changed == text) { blind.Add(what + " (the copy did not change)"); return; }
                copies++;
                var copy = new Dictionary<string, string>(sources);
                copy[file] = changed;
                if (Problems(copy).Count == 0) blind.Add(what);
            };
            mutate("a group that calls _sw.CloseDoc itself", "BareViews.cs",
                   t => t.Replace("CloseTitle(drawing.GetTitle())", "_sw.CloseDoc(drawing.GetTitle())"));
            mutate("Close that calls _sw.CloseDoc itself", "Program.cs",
                   t => t.Replace("try { CloseTitle(doc.GetTitle()); } catch { }", "try { _sw.CloseDoc(doc.GetTitle()); } catch { }"));
            mutate("CloseTitle that does not release", "Program.cs", t =>
            {
                string body = Body(t, CloseTitleHead);
                return body == null ? t : t.Replace(body, body.Replace(Released, ""));
            });
            mutate("CloseTitle that releases after the close", "Program.cs", t =>
            {
                string body = Body(t, CloseTitleHead);
                return body == null ? t : t.Replace(body, body.Replace(Released, "").Replace(TheClose, TheClose + " " + Released));
            });
            mutate("a release with the gate never opened", "Program.cs", t =>
            {
                string body = Body(t, ReleaseHead);
                return body == null ? t : t.Replace(body, body.Replace(Opened, ""));
            });

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : sources.Count + " files of the bench, one CloseDoc, after the release; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
