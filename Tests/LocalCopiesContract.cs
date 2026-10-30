using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE TEMPLATES FROM A LOCAL COPY (library 0.179.1, `OCE_LOCAL_COPIES`, 29/10/2026).
    /// The pure halves of `LocalCopies`, on a temporary folder: a path under the share is
    /// opened from the copy only when the copy exists; a path elsewhere, a folder whose name
    /// only begins like the share, an empty or malformed variable change nothing; what is
    /// reported goes back to the share. The environment of the process is not touched: the
    /// value of the variable is passed.
    /// </summary>
    internal static class LocalCopiesContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void RunAll()
        {
            Group("the templates the operations open, from a local copy when OCE_LOCAL_COPIES asks for it (library 0.179.1)");
            const string label = "a path under the share is opened from the copy when it exists, and reported with the share";
            var library = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NotHere(label, library + " is missing: build the library first (build.ps1)");
                return;
            }
            string temp = Path.Combine(Path.GetTempPath(), "oce-local-copies-" + System.Diagnostics.Process.GetCurrentProcess().Id);
            try
            {
                // From the bytes: the repository may sit on a network drive, where LoadFrom is refused.
                var core = Assembly.Load(File.ReadAllBytes(library));
                var t = core.GetType("Oce.Solid.Agent.Core.LocalCopies", false);
                if (t == null) { Check(label, false, "LocalCopies is not in this library (0.179.0 or older)"); return; }

                string share = Path.Combine(temp, "share");
                string local = Path.Combine(temp, "local");
                Directory.CreateDirectory(Path.Combine(local, "Formats"));
                File.WriteAllText(Path.Combine(local, "Formats", "A3.slddrt"), "x");
                string setting = share + "|" + local + "\\";
                string onShare = Path.Combine(share, "Formats", "A3.slddrt");
                string inCopy = Path.Combine(local, "Formats", "A3.slddrt");

                var wrong = new List<string>();
                if ((string)Call(t, "Map", onShare, setting) != inCopy) wrong.Add("the format on the share is not opened from its copy");
                // Windows paths ignore case: the rest of the path keeps the capitals it came with.
                if (!string.Equals((string)Call(t, "Map", onShare.ToUpperInvariant(), setting), inCopy, StringComparison.OrdinalIgnoreCase)) wrong.Add("the share written in capitals");
                if ((string)Call(t, "Map", Path.Combine(share, "Formats"), setting) != Path.Combine(local, "Formats")) wrong.Add("a folder of the share is not opened from its copy");
                string missing = Path.Combine(share, "Formats", "A4.slddrt");
                if ((string)Call(t, "Map", missing, setting) != missing) wrong.Add("a file with no copy is not left on the share");
                string elsewhere = @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\lang\english\bom-standard.sldbomtbt";
                if ((string)Call(t, "Map", elsewhere, setting) != elsewhere) wrong.Add("a path outside the share moved");
                string lookalike = Path.Combine(temp, "shareX", "Formats", "A3.slddrt");
                if ((string)Call(t, "Map", lookalike, setting) != lookalike) wrong.Add("a folder that only begins like the share moved");
                if ((string)Call(t, "Map", onShare, (object)null) != onShare) wrong.Add("an unset variable moved the path");
                if ((string)Call(t, "Map", onShare, share) != onShare) wrong.Add("a variable without «|» moved the path");
                if ((string)Call(t, "Map", onShare, "|" + local) != onShare) wrong.Add("a variable with no share moved the path");
                if (Call(t, "Map", null, setting) != null) wrong.Add("a null path became something");
                if ((string)Call(t, "Unmap", inCopy, setting) != onShare) wrong.Add("the copy is not reported with the share");
                if ((string)Call(t, "Unmap", onShare, setting) != onShare) wrong.Add("a path on the share changed when reported");
                if ((string)Call(t, "Unmap", elsewhere, setting) != elsewhere) wrong.Add("a path elsewhere changed when reported");
                if ((string)Call(t, "Unmap", inCopy, (object)null) != inCopy) wrong.Add("an unset variable changed what is reported");
                Check(label, wrong.Count == 0, wrong.Count == 0
                    ? "opened from the copy only when it exists, reported with the share; elsewhere, look-alike, unset and malformed change nothing"
                    : string.Join("; ", wrong));
            }
            catch (Exception ex) { Check(label, false, Why(ex)); }
            finally { try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { } }
        }

        private static object Call(Type t, string method, params object[] args)
        {
            var m = ReflectionLog.Global.Method(t, method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method);
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        private static string Why(Exception ex)
        {
            var inner = ex.InnerException ?? ex;
            return inner.GetType().Name + ": " + inner.Message;
        }
    }
}
