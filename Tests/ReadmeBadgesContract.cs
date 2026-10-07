using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE BADGES AT THE TOP OF THE README ARE FACTS, AND A FACT AGES (W3.1-bis.11).
    ///
    /// The README opens with two support buttons and a "Project information"
    /// section of badges. Two of those badges carry a version number, and a
    /// version number written by hand in a document is exactly the measurement that
    /// ages by itself: `Tests/README.md` said 54 checks while 99 were running, the
    /// main README said thirty-eight operations while the catalogue had sixty.
    /// The next one would have been «library 0.152.0» on a library that
    /// moved to 0.153.0 and nobody rereading the badge.
    ///
    /// So the badges are read like the other numbers of the documents:
    ///
    ///   * the bridge badge must equal `VERSION.txt` (what the build stamps into
    ///     the executable);
    ///   * the library badge must equal the `AssemblyVersion` of
    ///     `Oce.Solid.Agent.Core\Properties\AssemblyInfo.cs` (what the build
    ///     stamps into the library). Both are read from the SOURCE, not from a
    ///     DLL in `bin`, which could be from yesterday;
    ///   * the badges that state a fact are there: platform, SOLIDWORKS version,
    ///     .NET Framework, license, MCP, and the status badge of a workflow that
    ///     really exists in `.github\workflows`;
    ///   * and ONLY true badges: shields.io static badges and the status badge
    ///     of that workflow. A badge of a service that is not configured here
    ///     (code quality, lint, coverage) is a statement nobody can back up, so
    ///     it is red.
    ///
    /// THE CHECK IS SEEN RED ON FAKES, in the run itself: a bridge badge one
    /// patch behind, a library badge one minor behind, a missing badge, a badge
    /// of a coverage service, a status badge of a workflow that does not exist,
    /// each against a copy of the text with exactly that one change. And it was
    /// seen red once on a mutated copy of the whole README, run by the
    /// executable from a folder of its own (see the CHANGELOG entry).
    ///
    /// The two support buttons are checked for what they are (an image with its
    /// label inside a link to the https address of the service), not for the
    /// address: the addresses are the author's, and the publishability check
    /// exempts exactly those two URLs and nothing else.
    /// </summary>
    internal static class ReadmeBadgesContract
    {
        // ![alt](url) and [![alt](url)](link)
        private static readonly Regex Image = new Regex(@"!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)\)");
        private static readonly Regex Linked = new Regex(@"\[!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)\)\]\((?<link>[^)\s]+)\)");

        // https://img.shields.io/badge/<label>-<message>-<color> : a literal
        // hyphen in label or message is written as two (`Apache--2.0`).
        private static readonly Regex StaticBadge = new Regex(
            @"^https://img\.shields\.io/badge/(?<body>(?:[^/?\-]|--)+-(?:[^/?\-]|--)+)(?:-[A-Za-z0-9]+)?(?:\?.*)?$");

        private static readonly Regex Workflow = new Regex(
            @"^https://github\.com/[^/]+/[^/]+/actions/workflows/(?<file>[A-Za-z0-9._-]+\.yml)/badge\.svg$");

        internal static void RunAll()
        {
            Group("the badges of the README are true");

            FakesAreSeenRed();

            var root = Root();
            var readme = Path.Combine(root, "README.md");
            if (!File.Exists(readme))
            {
                Check("the README is there to read its badges", false, readme);
                return;
            }

            string text;
            try { text = File.ReadAllText(readme); }
            catch (Exception ex) { Check("the README can be read for its badges", false, ex.Message); return; }

            string bridge = RepositoryVersion();
            string library = LibraryVersion(root);
            Check("the version files can be read (VERSION.txt and the AssemblyVersion of the library)",
                  bridge != null && library != null,
                  "VERSION.txt: " + (bridge ?? "not readable") + "; AssemblyVersion: " + (library ?? "not readable"));
            if (bridge == null || library == null) return;

            var workflows = new HashSet<string>(
                Directory.Exists(Path.Combine(root, ".github", "workflows"))
                    ? Directory.GetFiles(Path.Combine(root, ".github", "workflows"), "*.yml").Select(Path.GetFileName)
                    : new string[0], StringComparer.Ordinal);

            var findings = Findings(text, bridge, library, workflows);
            Check("the badges of the README say the version of the bridge and of the library, and nothing untrue",
                  findings.Count == 0,
                  findings.Count == 0
                      ? "bridge " + bridge + ", library " + library + ", " + workflows.Count + " workflows on disk"
                      : string.Join("; ", findings));
        }

        /// <summary>The `AssemblyVersion` of the library, as major.minor.patch, from its source.</summary>
        private static string LibraryVersion(string root)
        {
            try
            {
                var f = Path.Combine(root, "Oce.Solid.Agent.Core", "Properties", "AssemblyInfo.cs");
                if (!File.Exists(f)) return null;
                var m = Regex.Match(File.ReadAllText(f), @"^\[assembly: AssemblyVersion\(""([0-9]+\.[0-9]+\.[0-9]+)\.[0-9]+""\)\]",
                                    RegexOptions.Multiline);
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// What is wrong with the badges of this README text; empty if nothing.
        /// Pure: the text, the two versions and the workflow files that exist.
        /// </summary>
        internal static List<string> Findings(string text, string bridge, string library, ISet<string> workflows)
        {
            var found = new List<string>();

            // only what stands before the first "## " that is not the information
            // section counts as the top of the README
            int start = text.IndexOf("## Project information", StringComparison.Ordinal);
            if (start < 0)
            {
                found.Add("no «Project information» section");
                return found;
            }
            int end = text.IndexOf("\n## ", start + 5, StringComparison.Ordinal);
            string section = end < 0 ? text.Substring(start) : text.Substring(start, end - start);

            var images = Image.Matches(section).Cast<Match>()
                              .Select(m => new { Alt = m.Groups["alt"].Value, Url = m.Groups["url"].Value }).ToList();
            if (images.Count == 0) found.Add("the section has no badge");

            // 1. every image is a static shields.io badge or the status of a workflow on disk
            var bodies = new List<string>();
            bool ci = false;
            foreach (var i in images)
            {
                var wf = Workflow.Match(i.Url);
                if (wf.Success)
                {
                    if (!workflows.Contains(wf.Groups["file"].Value))
                        found.Add("the status badge points to .github/workflows/" + wf.Groups["file"].Value + ", which does not exist");
                    else ci = true;
                    continue;
                }
                var st = StaticBadge.Match(i.Url);
                if (!st.Success)
                {
                    found.Add("«" + i.Alt + "» is neither a static shields.io badge nor the status of a workflow: " + i.Url);
                    continue;
                }
                string body = st.Groups["body"].Value.Replace("--", "-");
                bodies.Add(Uri.UnescapeDataString(body));
            }

            // 2. no service that is not configured here
            foreach (var b in bodies)
                if (Regex.IsMatch(b, @"coverage|coverall|codecov|codacy|codeclimate|sonar|quality|lint|build\s*passing", RegexOptions.IgnoreCase))
                    found.Add("«" + b + "» is the badge of a service that is not configured here");

            // 3. the facts that must be stated
            Func<string, bool> has = prefix => bodies.Any(b => b.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            foreach (var needed in new[] { "platform-", "SOLIDWORKS-", ".NET Framework-", "license-", "MCP-" })
                if (!has(needed)) found.Add("no «" + needed.TrimEnd('-') + "» badge");
            if (!ci) found.Add("no status badge of a workflow that exists");

            // 4. the versions
            CheckVersion(found, bodies, "bridge-", bridge, "VERSION.txt");
            CheckVersion(found, bodies, "library-", library, "the AssemblyVersion of the library");

            // 5. the two support buttons: an image with its label, linked to https
            var buttons = Linked.Matches(text.Substring(0, start)).Cast<Match>()
                                .Select(m => new { Alt = m.Groups["alt"].Value, Link = m.Groups["link"].Value }).ToList();
            foreach (var label in new[] { "Buy me a coffee", "Donate via PayPal" })
            {
                var b = buttons.FirstOrDefault(x => x.Alt == label);
                if (b == null) found.Add("the button «" + label + "» is not above the information section");
                else if (!b.Link.StartsWith("https://", StringComparison.Ordinal)) found.Add("the button «" + label + "» does not link to an https address");
            }

            return found;
        }

        private static void CheckVersion(List<string> found, List<string> bodies, string prefix, string expected, string source)
        {
            var mine = bodies.Where(b => b.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (mine.Count == 0) { found.Add("no «" + prefix.TrimEnd('-') + "» version badge"); return; }
            foreach (var b in mine)
            {
                string said = b.Substring(prefix.Length);
                if (said != expected)
                    found.Add("the «" + prefix.TrimEnd('-') + "» badge says " + said + " and " + source + " says " + expected);
            }
        }

        // ── THE SELF-TESTS: each fake is the good text with ONE change ───────

        private const string Good =
            "# T\n\n" +
            "[![Buy me a coffee](https://img.shields.io/badge/Buy%20me%20a%20coffee-d9342b)](https://example.org/a)\n" +
            "[![Donate via PayPal](https://img.shields.io/badge/Donate%20via%20PayPal-0070ba)](https://example.org/b)\n\n" +
            "## Project information\n\n" +
            "![Bridge version](https://img.shields.io/badge/bridge-0.129.0-blue)\n" +
            "![Library version](https://img.shields.io/badge/library-0.152.0-blue)\n" +
            "![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey)\n" +
            "![SOLIDWORKS](https://img.shields.io/badge/SOLIDWORKS-2021%20SP5.1-red)\n" +
            "![.NET](https://img.shields.io/badge/.NET%20Framework-4.8-512bd4)\n" +
            "![License](https://img.shields.io/badge/license-Apache--2.0-green)\n" +
            "![MCP](https://img.shields.io/badge/MCP-stdio-black)\n" +
            "[![Tests](https://github.com/o/r/actions/workflows/tests.yml/badge.svg)](https://github.com/o/r/actions/workflows/tests.yml)\n\n" +
            "## Why it exists\n\n" +
            "![Not a badge of the section](https://example.org/x.png)\n";

        private static void FakesAreSeenRed()
        {
            var workflows = new HashSet<string> { "tests.yml", "language-gate.yml" };
            const string NoSuchService = "![Coverage](https://img.shields.io/badge/coverage-91%25-green)\n";
            var cases = new[]
            {
                // name, text, red expected
                Tuple.Create("the good text", Good, false),
                Tuple.Create("bridge badge one patch behind", Good.Replace("bridge-0.129.0", "bridge-0.128.9"), true),
                Tuple.Create("library badge one minor behind", Good.Replace("library-0.152.0", "library-0.151.0"), true),
                Tuple.Create("library badge written as four numbers", Good.Replace("library-0.152.0", "library-0.152.0.0"), true),
                Tuple.Create("no library badge", Good.Replace("![Library version](https://img.shields.io/badge/library-0.152.0-blue)\n", ""), true),
                Tuple.Create("no license badge", Good.Replace("![License](https://img.shields.io/badge/license-Apache--2.0-green)\n", ""), true),
                Tuple.Create("no SOLIDWORKS badge", Good.Replace("![SOLIDWORKS](https://img.shields.io/badge/SOLIDWORKS-2021%20SP5.1-red)\n", ""), true),
                Tuple.Create("a coverage badge", Good.Replace("![MCP]", NoSuchService + "![MCP]"), true),
                Tuple.Create("a badge of a lint service", Good.Replace("![MCP]", "![Lint](https://img.shields.io/badge/lint-passing-green)\n![MCP]"), true),
                Tuple.Create("a badge from another host", Good.Replace("![MCP]", "![Quality](https://codacy.example/badge.svg)\n![MCP]"), true),
                Tuple.Create("a dynamic shields badge (reads a service)", Good.Replace("![MCP]", "![Stars](https://img.shields.io/github/stars/o/r)\n![MCP]"), true),
                Tuple.Create("a status badge of a workflow that is not there", Good.Replace("workflows/tests.yml/badge.svg", "workflows/ci.yml/badge.svg"), true),
                Tuple.Create("no status badge at all", Good.Replace("[![Tests](https://github.com/o/r/actions/workflows/tests.yml/badge.svg)](https://github.com/o/r/actions/workflows/tests.yml)\n", ""), true),
                Tuple.Create("no information section", Good.Replace("## Project information", "## Something else"), true),
                Tuple.Create("a support button gone", Good.Replace("[![Buy me a coffee](https://img.shields.io/badge/Buy%20me%20a%20coffee-d9342b)](https://example.org/a)\n", ""), true),
                Tuple.Create("a support button that is not https", Good.Replace("(https://example.org/b)", "(http://example.org/b)"), true),
            };

            var wrong = new List<string>();
            foreach (var c in cases)
            {
                bool red = Findings(c.Item2, "0.129.0", "0.152.0", workflows).Count > 0;
                if (red != c.Item3) wrong.Add("«" + c.Item1 + "»: " + (red ? "red" : "green") + " instead of " + (c.Item3 ? "red" : "green"));
            }
            Check("the badge check turns red on fakes (a version one step behind, a missing badge, a service that is not configured, a workflow that does not exist)",
                  wrong.Count == 0,
                  wrong.Count == 0 ? cases.Length + " fakes ended as expected" : string.Join("; ", wrong));
        }
    }
}
