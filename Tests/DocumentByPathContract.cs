using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Programma;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// A DOCUMENT IS CLOSED OR ACTIVATED BY PATH OR BY REFERENCE, NEVER BY TITLE
    /// (Z-C21.13, findings N11 and M4, pattern S18 of the Z-C21.12 audit).
    ///
    /// Up to library 0.143.0 the bridge and the library passed
    /// IModelDoc2.GetTitle() to ISldWorks.CloseDoc and ActivateDoc3: in the
    /// «close» action of the document tool, for a wrong-type new document, for
    /// the weldment profile library, for the trial frame of the profile section,
    /// for the STEP/IGES reopened to compare volumes (M4), for the flat pattern
    /// and the AP242 export. With the extensions hidden a part and an assembly
    /// share the title, a document never saved has only its stem, and no source
    /// says which document CloseDoc closes when the name fits two: the N11 probe
    /// (29/9/2026) saw CloseDoc(stem) close the wrong one, dirty, edit lost.
    ///
    /// No bench case can build it: the equal titles need Windows to hide the
    /// extensions, a setting of the station, and this station shows them (the
    /// probe itself left the tie undecided for that reason). So this group checks
    ///   - on the SOURCE, that no CloseDoc, QuitDoc or ActivateDoc call is left
    ///     outside Oce.Solid.Agent.Core/DocumentByPath.cs, and that inside it the
    ///     calls take only the name the rule computed;
    ///   - on the BUILT library, by reflection, the pure rules of DocumentByPath
    ///     with the situations of the probe.
    /// On library 0.143.0 all of it is red: ten direct calls (Documents.cs 3,
    /// ExportOperations.cs 2, SheetMetalOperations.cs 2, WeldmentOperations.cs 1,
    /// SectionProfile.cs 2), and no DocumentByPath.
    /// </summary>
    internal static class DocumentByPathContract
    {
        private const BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private const string HelperFile = "DocumentByPath.cs";

        internal static void RunAll()
        {
            Gruppo("documents closed and activated by path or reference, never by title (Z-C21.13 N11, M4, S18)");

            DirectCallsInTheSource();

            var library = Path.Combine(Radice(), "Oce.Solid.Agent.Core", "bin", "Release", "Oce.Solid.Agent.Core.dll");
            if (!File.Exists(library))
            {
                NonQui("the pure rules of DocumentByPath",
                       library + " is missing: build the library first (build.ps1)");
                return;
            }

            Type rules;
            try
            {
                var redist = DoveStaLInterop();
                if (redist != null)
                {
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
                var asm = Assembly.Load(File.ReadAllBytes(library));
                rules = asm.GetType("Oce.Solid.Agent.Core.DocumentByPath", false);
            }
            catch (Exception ex) { Segna("the library loads", false, ex.GetType().Name + ": " + ex.Message); return; }
            if (rules == null)
            {
                Segna("DocumentByPath is in the library", false, "type Oce.Solid.Agent.Core.DocumentByPath not found");
                return;
            }

            CloseRules(rules);
            ActivateRules(rules);
        }

        // ── the source ──────────────────────────────────────────────────────

        private static readonly Regex DirectCall = new Regex(
            @"\.\s*(CloseDoc|QuitDoc|CloseAllDocuments|I?ActivateDoc\d*)\s*\(", RegexOptions.Compiled);
        private static readonly Regex HelperCloseDoc = new Regex(@"\.\s*CloseDoc\s*\(([^)]*)\)", RegexOptions.Compiled);
        private static readonly Regex HelperActivateDoc = new Regex(@"\.\s*ActivateDoc3\s*\(([^,)]*)[,)]", RegexOptions.Compiled);

        private static void DirectCallsInTheSource()
        {
            var files = new List<string>();
            foreach (var folder in new[] { "Oce.Solid.Agent.Core", "Bridge", "Tools", "Mcp", "Shared" })
            {
                var dir = Path.Combine(Radice(), folder);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var rel = f.Substring(Radice().Length).TrimStart('\\', '/').Replace('\\', '/');
                    if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
                    files.Add(f);
                }
            }
            var main = Path.Combine(Radice(), "Program.cs");
            if (File.Exists(main)) files.Add(main);

            var outside = new List<string>();
            var helperCalls = new List<string>();
            var helperBad = new List<string>();
            bool helperSeen = false;
            foreach (var f in files)
            {
                bool isHelper = string.Equals(Path.GetFileName(f), HelperFile, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "Oce.Solid.Agent.Core",
                                              StringComparison.OrdinalIgnoreCase);
                if (isHelper) helperSeen = true;
                var state = new StripState();
                int number = 0;
                foreach (var raw in File.ReadAllLines(f))
                {
                    number++;
                    string comment;
                    var code = ObsoleteCallScanner.Strip(raw, state, out comment);
                    string where = Path.GetFileName(f) + ":" + number;
                    if (!isHelper)
                    {
                        foreach (Match m in DirectCall.Matches(code)) outside.Add(where + " " + m.Groups[1].Value);
                        continue;
                    }
                    foreach (Match m in HelperCloseDoc.Matches(code))
                    {
                        helperCalls.Add(where + " CloseDoc");
                        if (m.Groups[1].Value.Trim() != "name") helperBad.Add(where + " CloseDoc(" + m.Groups[1].Value.Trim() + ")");
                    }
                    foreach (Match m in HelperActivateDoc.Matches(code))
                    {
                        helperCalls.Add(where + " ActivateDoc3");
                        if (m.Groups[1].Value.Trim() != "name") helperBad.Add(where + " ActivateDoc3(" + m.Groups[1].Value.Trim() + ", ...)");
                    }
                    foreach (Match m in DirectCall.Matches(code))
                        if (m.Groups[1].Value != "CloseDoc" && m.Groups[1].Value != "ActivateDoc3")
                            helperBad.Add(where + " " + m.Groups[1].Value);
                }
            }

            Segna("no CloseDoc, QuitDoc or ActivateDoc call outside DocumentByPath (library, bridge, tools)",
                  files.Count > 10 && outside.Count == 0,
                  files.Count <= 10 ? "only " + files.Count + " source files found: the check would pass on nothing"
                  : outside.Count == 0 ? files.Count + " files, none"
                  : outside.Count + " direct call(s), each one a name that may be a title: " + string.Join(" | ", outside));

            bool closeThere = helperCalls.Any(c => c.EndsWith("CloseDoc", StringComparison.Ordinal));
            bool activateThere = helperCalls.Any(c => c.EndsWith("ActivateDoc3", StringComparison.Ordinal));
            Segna("inside DocumentByPath, CloseDoc and ActivateDoc3 take only the name the rule computed",
                  helperSeen && closeThere && activateThere && helperBad.Count == 0,
                  !helperSeen ? "Oce.Solid.Agent.Core/" + HelperFile + " is missing"
                  : !closeThere || !activateThere ? "a CloseDoc or an ActivateDoc3 call is missing: " + string.Join(", ", helperCalls)
                  : helperBad.Count == 0 ? string.Join(", ", helperCalls)
                  : string.Join(" | ", helperBad));
        }

        // ── the pure rules ──────────────────────────────────────────────────

        private static string[] Names(Type t, string method, object[] args, out string refusal)
        {
            var m = ReflectionLog.Global.Method(t, method, Any);
            if (m == null) throw new MissingMethodException(t.Name + "." + method);
            object result;
            try { result = m.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
            refusal = args[args.Length - 1] as string;
            return result as string[];
        }

        private static string[] CloseNames(Type t, string path, string title, bool active,
                                           string[] otherPaths, string[] otherTitles, out string refusal)
        {
            return Names(t, "CloseNames", new object[] { path, title, active, otherPaths, otherTitles, null }, out refusal);
        }

        private static string[] ActivateNames(Type t, string path, string title,
                                              string[] otherPaths, string[] otherTitles, out string refusal)
        {
            return Names(t, "ActivateNames", new object[] { path, title, otherPaths, otherTitles, null }, out refusal);
        }

        private static string Show(string[] names)
        {
            return names == null ? "null" : "[" + string.Join(", ", names.Select(n => "«" + n + "»")) + "]";
        }

        private static bool Same(string[] names, params string[] expected)
        {
            return names != null && names.SequenceEqual(expected, StringComparer.Ordinal);
        }

        private static void CloseRules(Type t)
        {
            const string asm = @"C:\w\N11AB.SLDASM", prt = @"C:\w\N11AB.SLDPRT";
            string refusal;

            try
            {
                // A-G1, A-G3: CloseDoc(GetPathName()) and CloseDoc(file name with
                // extension) closed exactly the target.
                var names = CloseNames(t, asm, "N11AB.SLDASM", false, new[] { prt }, new[] { "N11AB.SLDPRT" }, out refusal);
                Segna("a saved document is closed by its full path, then its file name (probe A-G1, A-G3)",
                      Same(names, asm, "N11AB.SLDASM"), Show(names));
            }
            catch (Exception ex) { Segna("a saved document is closed by its full path, then its file name (probe A-G1, A-G3)", false, ex.GetType().Name + ": " + ex.Message); }

            try
            {
                // Extensions hidden: Base.SLDASM and Base.SLDPRT both show «Base».
                var close = CloseNames(t, @"C:\w\Base.SLDASM", "Base", false, new[] { @"C:\w\Base.SLDPRT" }, new[] { "Base" }, out refusal);
                Segna("with the extensions hidden the shared title «Base» is never the name given to CloseDoc",
                      Same(close, @"C:\w\Base.SLDASM", "Base.SLDASM"), Show(close));
            }
            catch (Exception ex) { Segna("with the extensions hidden the shared title «Base» is never the name given to CloseDoc", false, ex.GetType().Name + ": " + ex.Message); }

            try
            {
                // NEVER CloseDoc(""): on the first full bench of 0.144.0 it closed
                // the original part of an export instead of the reopened one that
                // answered as active. In front or not, a document never saved is
                // closed by its title, and refused when another document answers
                // to that title.
                string refusedWhy;
                var names = CloseNames(t, "", "Part7", true, new[] { @"C:\w\Part7.SLDPRT" }, new[] { "Part7" }, out refusedWhy);
                var alone = CloseNames(t, "", "Part7", true, new[] { @"C:\w\Base.SLDPRT" }, new[] { "Base.SLDPRT" }, out refusal);
                Segna("a document never saved and in front is closed by its title, never with CloseDoc(\"\")",
                      names == null && !string.IsNullOrEmpty(refusedWhy) && Same(alone, "Part7"),
                      "title taken by another document " + Show(names) + ", title free " + Show(alone));
            }
            catch (Exception ex) { Segna("a document never saved and in front is closed by its title, never with CloseDoc(\"\")", false, ex.GetType().Name + ": " + ex.Message); }

            try
            {
                var names = CloseNames(t, "", "Part7", false, new[] { "", @"C:\w\Base.SLDPRT" }, new[] { "Part8", "Base.SLDPRT" }, out refusal);
                Segna("a document never saved, not in front, with a title nobody else answers to: that title",
                      Same(names, "Part7"), Show(names) + (refusal == null ? "" : " refusal: " + refusal));
            }
            catch (Exception ex) { Segna("a document never saved, not in front, with a title nobody else answers to: that title", false, ex.GetType().Name + ": " + ex.Message); }

            try
            {
                var hidden = CloseNames(t, "", "Part7", false, new[] { @"C:\w\Part7.SLDPRT" }, new[] { "Part7" }, out refusal);
                string hiddenRefusal = refusal;
                // A-S: CloseDoc(stem) closed the document whose title carried the
                // extension, so the stem collides with the extensions shown too.
                var shown = CloseNames(t, "", "Part7", false, new[] { @"C:\w\Part7.SLDPRT" }, new[] { "Part7.SLDPRT" }, out refusal);
                Segna("never saved, not in front, and a saved file answers to its title: refused, naming it (probe A-S)",
                      hidden == null && shown == null && hiddenRefusal != null && hiddenRefusal.Contains(@"C:\w\Part7.SLDPRT") &&
                      refusal != null,
                      "extensions hidden " + Show(hidden) + ", shown " + Show(shown) + "; refusal: " + (hiddenRefusal ?? "none"));
            }
            catch (Exception ex) { Segna("never saved, not in front, and a saved file answers to its title: refused, naming it (probe A-S)", false, ex.GetType().Name + ": " + ex.Message); }

            try
            {
                // M4: the STEP export, asked to reopen and compare, reopens
                // gear.step next to gear.SLDPRT; the imported document, never
                // saved, is titled «gear».
                var notFront = CloseNames(t, "", "gear", false, new[] { @"C:\w\gear.SLDPRT" }, new[] { "gear.SLDPRT" }, out refusal);
                var front = CloseNames(t, "", "gear", true, new[] { @"C:\w\gear.SLDPRT" }, new[] { "gear.SLDPRT" }, out refusal);
                // In front or not, refused: CloseDoc("") is never used (it closed the
                // original part on the first full bench of 0.144.0), and the title
                // is shared. The reopened document stays open and the answer says so.
                Segna("M4: the reopened STEP titled like its part is never closed by that title",
                      notFront == null && front == null,
                      "not in front " + Show(notFront) + ", in front " + Show(front));
            }
            catch (Exception ex) { Segna("M4: the reopened STEP titled like its part is never closed by that title", false, ex.GetType().Name + ": " + ex.Message); }

            try
            {
                var names = CloseNames(t, "", "Draw3 - Sheet1", false, new[] { @"C:\w\Draw3.SLDDRW" }, new[] { "Draw3 - Sheet1" }, out refusal);
                Segna("a drawing never saved is refused when a saved drawing shows the same title",
                      names == null, Show(names));
            }
            catch (Exception ex) { Segna("a drawing never saved is refused when a saved drawing shows the same title", false, ex.GetType().Name + ": " + ex.Message); }

            try
            {
                var m = t.GetMethod("TitleCollision", Any);
                if (m == null) throw new MissingMethodException(t.Name + ".TitleCollision");
                var upper = m.Invoke(null, new object[] { "PART7", new[] { @"C:\w\part7.sldprt" }, new[] { "part7.sldprt" } }) as string;
                var prefix = m.Invoke(null, new object[] { "Part7", new[] { @"C:\w\Part70.SLDPRT" }, new[] { "Part70.SLDPRT" } }) as string;
                Segna("the collision ignores case, as Windows file names do, and a longer name is no collision",
                      upper != null && prefix == null, "PART7 vs part7.sldprt: " + (upper ?? "null") + "; Part7 vs Part70.SLDPRT: " + (prefix ?? "null"));
            }
            catch (Exception ex) { Segna("the collision ignores case, as Windows file names do, and a longer name is no collision", false, ex.GetType().Name + ": " + ex.Message); }

            try
            {
                string closeRefusal, activateRefusal;
                var close = CloseNames(t, "", "", false, new string[0], new string[0], out closeRefusal);
                var activate = ActivateNames(t, "", "", new string[0], new string[0], out activateRefusal);
                Segna("no path and no title: nothing to name, refused with a reason",
                      close == null && activate == null && closeRefusal != null && activateRefusal != null,
                      "close " + Show(close) + ", activate " + Show(activate));
            }
            catch (Exception ex) { Segna("no path and no title: nothing to name, refused with a reason", false, ex.GetType().Name + ": " + ex.Message); }
        }

        private static void ActivateRules(Type t)
        {
            string refusal;
            try
            {
                // ActivateDoc3.html: «specify the file extension in Name». The
                // title «N11AB» (extensions hidden) must not appear.
                var names = ActivateNames(t, @"C:\w\N11AB.SLDASM", "N11AB", new[] { @"C:\w\N11AB.SLDPRT" }, new[] { "N11AB" }, out refusal);
                Segna("a saved document is activated by its file name with extension, then its path, never its title",
                      Same(names, "N11AB.SLDASM", @"C:\w\N11AB.SLDASM"), Show(names));
            }
            catch (Exception ex) { Segna("a saved document is activated by its file name with extension, then its path, never its title", false, ex.GetType().Name + ": " + ex.Message); }

            try
            {
                var unique = ActivateNames(t, "", "Part7", new[] { @"C:\w\Base.SLDPRT" }, new[] { "Base.SLDPRT" }, out refusal);
                var clash = ActivateNames(t, "", "Part7", new[] { @"C:\w\Part7.SLDPRT" }, new[] { "Part7.SLDPRT" }, out refusal);
                Segna("a document never saved is activated by its title only when nobody else answers to it",
                      Same(unique, "Part7") && clash == null && refusal != null,
                      "unique " + Show(unique) + ", clash " + Show(clash));
            }
            catch (Exception ex) { Segna("a document never saved is activated by its title only when nobody else answers to it", false, ex.GetType().Name + ": " + ex.Message); }
        }
    }
}
