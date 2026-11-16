using System;
using System.Collections.Generic;
using System.IO;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE LOCAL COPY OF A TEMPLATE OUTLIVES A CLEANER OF %TEMP% (bridge 0.170.14, 3/11/2026).
    ///
    /// The bench copies the templates of the share to %TEMP%\bench-models (Program.LocalModel).
    /// On 3/11/2026 a scheduled cleaner of %TEMP%, which deletes what has not been modified for 3
    /// days, deleted the copy of the assembly template in the middle of a full bench: File.Copy keeps the time of the
    /// original, and every new assembly after it was null (26 bench errors). No SOLIDWORKS needed:
    /// the check reads LocalModel. The copy must take the time of now, and a cached copy that is
    /// gone must be copied again; each is taken away on a changed copy, which must turn it red.
    /// </summary>
    internal static class BenchLocalModelsContract
    {
        internal static void RunAll()
        {
            Group("the local copy of a template outlives a cleaner of %TEMP% (bridge 0.170.14)");
            Wiring();
        }

        private const string Head = "internal static string LocalModel(string its)";
        private const string Copied = "File.Copy(its, copy, true);";
        private const string Touched = "File.SetLastWriteTime(copy, DateTime.Now);";
        private const string Cached = "if (_localModels.TryGetValue(its, out already))";
        private const string StillThere = "File.Exists(already)";

        /// <summary>The body of LocalModel, or null.</summary>
        private static string Body(string text)
        {
            int at = text.IndexOf(Head, StringComparison.Ordinal);
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

        internal static List<string> Problems(string program)
        {
            var wrong = new List<string>();
            if (program == null) { wrong.Add("Bench/Program.cs not found"); return wrong; }
            string body = Body(program);
            if (body == null) { wrong.Add("no LocalModel in Bench/Program.cs"); return wrong; }

            int copied = body.IndexOf(Copied, StringComparison.Ordinal);
            int touched = body.IndexOf(Touched, StringComparison.Ordinal);
            if (copied < 0) wrong.Add("LocalModel does not copy the template with File.Copy(its, copy, true)");
            else if (touched < copied)
                wrong.Add("the copy keeps the time of the original: a cleaner of %TEMP% takes it for an abandoned file");

            int cached = body.IndexOf(Cached, StringComparison.Ordinal);
            if (cached < 0) wrong.Add("LocalModel has no cache of the copies");
            else
            {
                int exists = body.IndexOf(StillThere, cached, StringComparison.Ordinal);
                int next = body.IndexOf("string arrival", cached, StringComparison.Ordinal);
                if (exists < 0 || (next >= 0 && exists > next))
                    wrong.Add("a cached copy is handed back without looking whether it is still there");
            }
            return wrong;
        }

        private static void Wiring()
        {
            const string label = "LocalModel gives the copy the time of now, and copies again a copy that is gone";
            var path = Path.Combine(Root(), "Bench", "Program.cs");
            var program = File.Exists(path) ? File.ReadAllText(path) : null;
            var real = Problems(program);

            // Teeth: each change below must turn the check red.
            var blind = new List<string>();
            int copies = 0;
            Action<string, Func<string, string>> mutate = (what, change) =>
            {
                if (program == null) return;
                var changed = change(program);
                if (changed == program) { blind.Add(what + " (the copy did not change)"); return; }
                copies++;
                if (Problems(changed).Count == 0) blind.Add(what);
            };
            mutate("the copy keeps the time of the original", t => t.Replace(Touched, ""));
            mutate("the cached copy handed back as it is", t => t.Replace(StillThere, "true"));

            bool ok = real.Count == 0 && blind.Count == 0;
            string detailText = real.Count > 0 ? string.Join("; ", real)
                              : blind.Count > 0 ? "the rule does not see: " + string.Join("; ", blind)
                              : "the copy touched after File.Copy, the cache looks at the file; " + copies + " changed copies all red";
            Check(label, ok, detailText);
        }
    }
}
