using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE LANGUAGE GATE CATCHES WHAT IT CLAIMS TO CATCH (entry W3.1-bis.1, rewritten in W3.1-bis.11).
    ///
    /// ── WHY A CONTRACT AND NOT ONLY THE CI STEP ──────────────────────────
    ///
    /// `Checks/language/detect-italian.js` is a gate of ZERO: no Italian in any
    /// of eight categories outside the exceptions of `exceptions.json` that the
    /// owner has SIGNED, and an exception counts only when it is signed. A gate
    /// that never fires looks exactly like a gate that works, and the tree it
    /// runs on is English, so it has nothing to catch. This contract builds
    /// small trees and gives it something to catch. It does not use the real
    /// tree: the real tree is judged by the gate itself, in the CI, and whether
    /// its exceptions are signed yet is the owner's business, not a contract's.
    ///
    /// ── 1. THE DETECTOR, ON A FIXTURE REPOSITORY ───────────────────────
    ///
    /// A throw-away repository holds the real detector and lexicon, a clean
    /// English tree and the two entries that cover the gate's own lexicon, signed.
    /// It must hold. Then Italian is injected
    /// and the gate must say what it found, by name, in the file or with the
    /// element where it was injected:
    ///
    ///   * one line for each of the eight categories;
    ///   * a tracked file with a text extension and a NUL byte, which git
    ///     calls binary: read anyway, because binaries go by extension only;
    ///   * the holes the review of W3.1-bis.1 found: a string inside the hole
    ///     of an interpolated string (C# and JavaScript), a protocol name
    ///     joined by hyphens, prose after a placeholder, a JSON key with
    ///     spaces, workshop vocabulary the repository does not use yet, a line
    ///     made of content words only, the words of an `echo` and of a
    ///     here-document, Italian appended to a line that is already Italian,
    ///     the typographic apostrophe;
    ///   * and English lines with one Italian homograph each ("plane",
    ///     "dice"): they must NOT count.
    ///
    /// ── 2. THE EXCEPTIONS ────────────────────────────────────────────────
    ///
    /// An entry that is not signed suppresses nothing and is listed as waiting;
    /// the same entry signed makes the gate hold; a signature with no date is
    /// refused; an entry that covers a whole file, directly or by a side door,
    /// or of a class that is not one of the six, is refused; the two classes of
    /// the changelog cover only what their shape says (the text between
    /// guillemets; the entries from a heading found in the text to the end).
    ///
    /// ── 3. THE CI STEP, ON A THROW-AWAY REPOSITORY WITH COMMITS ────────
    ///
    /// `.github/steps/language-gate.ps1` judges the tip. Each case is a branch
    /// from the same base commit: a clean commit, Italian, Italian covered by
    /// an unsigned entry, by a signed one, a change to the gate with and
    /// without its trailer, the trailer words in the body, and the gate script
    /// switched off. Each must end as expected.
    ///
    /// The Italian samples come from `injection_samples` and
    /// `holdout_workshop_terms` in lexicon.json, so this source file carries
    /// none.
    /// </summary>
    internal static class LanguageGateTests
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        // The two entries of the real exceptions.json that cover the gate's own files
        // (the Italian words of the lexicon, the patterns of the exceptions), signed:
        // every fixture carries them, or the lexicon it copies would be Italian text.
        private static string[] _base = new string[0];

        private static string[] BaseEntries(string root)
        {
            var real = Json.Deserialize<List<Dictionary<string, object>>>(File.ReadAllText(Path.Combine(root, "Checks", "language", "exceptions.json")));
            var entries = new List<string>();
            foreach (var e in real.Where(x => S(x, "class") == "detector-lexicon"))
            {
                e["signed_by"] = "LanguageGateTests";
                e["signed_on"] = "2026-10-06";
                entries.Add(Json.Serialize(e));
            }
            return entries.ToArray();
        }

        internal static void All()
        {
            Group("the language gate catches every category and counts only signed exceptions (W3.1-bis.1, W3.1-bis.11)");

            var root = Root();
            var detector = Path.Combine(root, "Checks", "language", "detect-italian.js");
            if (!File.Exists(detector)) { Check("the detector exists", false, detector); return; }

            string version;
            if (Run("node", "--version", root, out version) != 0)
            {
                NotHere("the language gate on a fixture repository",
                       "node is not on this machine: the gate is a Node script and the CI runner has Node. " +
                       "Not running it here is not the same as passing.");
                return;
            }

            var work = Path.Combine(Path.GetTempPath(), "language-gate-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                _base = BaseEntries(root);
                Directory.CreateDirectory(work);
                Probe(root, Path.Combine(work, "tree"));
                Exceptions(root, Path.Combine(work, "exceptions"));
                GateScript(root, Path.Combine(work, "gate"));
            }
            catch (Exception ex)
            {
                Check("the gate contract runs to the end", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }
            }
        }

        // A fixture repository: the real detector and lexicon, a clean English tree,
        // the signed entries for the gate's own lexicon. Everything is tracked.
        private static void Fixture(string root, string repo)
        {
            Directory.CreateDirectory(repo);
            foreach (var f in new[] { "Checks/language/detect-italian.js", "Checks/language/lexicon.json" })
            {
                var to = Path.Combine(repo, f.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(Path.Combine(root, f.Replace('/', Path.DirectorySeparatorChar)), to);
            }
            WriteNew(repo, "Checks/language/exceptions.json", "[\n " + string.Join(",\n ", _base) + "\n]\n");
            WriteNew(repo, "src/app.js", "// an English comment\nvar x = 1;\n");
            WriteNew(repo, "src/Load.cs", "// an English comment\nclass Load { int x = 1; }\n");
            WriteNew(repo, "README.md", "# The project\n\nAn English document.\n");
            WriteNew(repo, "data/values.json", "{\"name\": \"value\", \"note\": \"an English note\"}\n");
            WriteNew(repo, "RELEASE.md", "# Release\n");
            WriteNew(repo, "QUICKSTART.md", "# Quickstart\n");
            WriteNew(repo, "Checks/hooks/pre-push", "#!/bin/sh\necho done\n");
            WriteNew(repo, "CHANGELOG.md", "# Changelog\n\n## Unreleased\n\nAn English entry.\n");
            string ignored;
            Run("git", "init -q", repo, out ignored);
            Run("git", "config core.autocrlf false", repo, out ignored);
            Run("git", "config user.email gate@example.invalid", repo, out ignored);
            Run("git", "config user.name LanguageGateTests", repo, out ignored);
            Run("git", "config commit.gpgsign false", repo, out ignored);
            Run("git", "add -A", repo, out ignored);
        }

        private static string Detect(string repo, string extra, out int code)
        {
            string text;
            code = Run("node", Q(Path.Combine(repo, "Checks", "language", "detect-italian.js")) + " --root " + Q(repo) + " " + extra, repo, out text);
            return text;
        }

        // ── 1. the detector ──────────────────────────────────────────────────

        private static void Probe(string root, string repo)
        {
            Fixture(root, repo);
            int code;
            string text = Detect(repo, "", out code);
            Check("the gate holds on a clean English tree, with only the signed entries of its own lexicon", code == 0 && text.Contains("language gate: held"), "exit " + code + "; " + Tail(text));

            var lexicon = ReadJson(Path.Combine(repo, "Checks", "language", "lexicon.json"));
            var samples = lexicon.ContainsKey("injection_samples") ? lexicon["injection_samples"] as Dictionary<string, object> : null;
            if (samples == null) { Check("lexicon.json has injection_samples", false, ""); return; }
            string prose = S(samples, "prose"), identifier = S(samples, "identifier"),
                   jsonKey = S(samples, "json_key"), protocol = S(samples, "protocol"), segment = S(samples, "path_segment"),
                   compound = S(samples, "compound"), contentOnly = S(samples, "content_only"), appended = S(samples, "appended"),
                   typographic = S(samples, "typographic");
            var englishHomographs = Items(samples.ContainsKey("weak_alone_english") ? samples["weak_alone_english"] : null).Select(o => o.ToString()).ToList();
            // a workshop word the repository does not use, caught by its ending only
            var holdout = Items(lexicon.ContainsKey("holdout_workshop_terms") ? lexicon["holdout_workshop_terms"] : null)
                          .Select(o => o.ToString()).FirstOrDefault(t => t.EndsWith("atura", StringComparison.Ordinal));
            if (new[] { compound, contentOnly, appended, typographic, holdout }.Any(string.IsNullOrEmpty) || englishHomographs.Count == 0)
            {
                Check("lexicon.json has every injection sample", false, "compound, content_only, appended, typographic, weak_alone_english, holdout_workshop_terms");
                return;
            }
            var newWord = "probe" + char.ToUpperInvariant(holdout[0]) + holdout.Substring(1);

            // the injections, one per category
            Append(repo, "src/app.js", "// " + prose);
            Append(repo, "src/Load.cs", "class Strings { string s = \"" + prose + "\"; }");
            Append(repo, "README.md", prose);
            Append(repo, "src/app.js", "var " + identifier + " = 0;");
            var jsonFile = Path.Combine(repo, "data", "values.json");
            var jsonText = File.ReadAllText(jsonFile);
            int brace = jsonText.IndexOf('{');
            jsonText = jsonText.Substring(0, brace + 1) +
                       "\n  \"probe_note\": \"" + prose + "\",\n  \"" + jsonKey + "\": 1,\n  \"probe_value\": \"" + protocol + "\"," +
                       "\n  \"probe_compound\": \"" + compound + "\"," +
                       jsonText.Substring(brace + 1);
            File.WriteAllText(jsonFile, jsonText, new UTF8Encoding(false));
            WriteNew(repo, "Bench/" + segment + "/notes.md", "Gate probe.\n");
            // a text extension with a NUL byte: git calls it binary
            var nulBytes = new List<byte>(Encoding.UTF8.GetBytes("// " + prose + "\n"));
            nulBytes.Add(0);
            nulBytes.AddRange(Encoding.UTF8.GetBytes("\n"));
            File.WriteAllBytes(Path.Combine(repo, "Checks", "nul-probe.js"), nulBytes.ToArray());

            // the holes of the review, each in a file of its own
            Append(repo, "src/Hole.cs", "class GateProbeHole { string s = $\"R {(true ? \"" + prose + "\" : \"x\")}\"; }");
            Append(repo, "src/template.js", "var gateProbeTemplate = `${1 ? \"" + prose + "\" : \"\"}`;");
            WriteNew(repo, "data/probe-placeholder.json", "{\"note\": \"{NAME} " + prose + "\"}\n");
            WriteNew(repo, "data/probe-key-with-spaces.json", "{\"" + prose + "\": 1}\n");
            Append(repo, "src/words.js", "var " + newWord + " = 0;");
            WriteNew(repo, "Bench/" + holdout + "/notes.md", "Gate probe.\n");
            Append(repo, "RELEASE.md", contentOnly);
            Append(repo, "Checks/hooks/pre-push", "echo " + prose);
            WriteNew(repo, "Checks/hooks/probe-heredoc", "#!/bin/sh\ncat <<EOF\n" + prose + "\nEOF\n");
            Append(repo, "QUICKSTART.md", typographic);
            WriteNew(repo, "src/appended.js", "// an English line and " + prose + " " + appended + "\n");
            WriteNew(repo, "docs/homographs.md", string.Join("\n", englishHomographs) + "\n");

            string ignored;
            Run("git", "add -A", repo, out ignored);
            string numstat;
            Run("git", "diff --cached --numstat -- Checks/nul-probe.js", repo, out numstat);
            Check("git calls the probe with a NUL byte binary", numstat.StartsWith("-\t-\t", StringComparison.Ordinal), numstat.Trim());

            var report = Path.Combine(Path.GetDirectoryName(repo), "report.json");
            text = Detect(repo, "--report " + Q(report), out code);
            Check("with the injections the gate exits 1 and says why",
                  code == 1 && text.Contains("LANGUAGE GATE:") && text.Contains("ITALIAN OUTSIDE EVERY EXCEPTION"), "exit " + code + "; " + Tail(text));
            string jsonOut;
            code = Run("node", Q(Path.Combine(repo, "Checks", "language", "detect-italian.js")) + " --root " + Q(repo) + " --json", repo, out jsonOut);
            if (code != 0) { Check("the gate gives its report as JSON", false, "exit " + code + "; " + Tail(jsonOut)); return; }
            var doc = Json.Deserialize<Dictionary<string, object>>(jsonOut);
            var outside = Items(doc["outside_every_entry"]).Cast<Dictionary<string, object>>().ToList();

            Counted(outside, "comments", "src/app.js", "an Italian comment");
            Counted(outside, "strings", "src/Load.cs", "an Italian string");
            Counted(outside, "markdown", "README.md", "an Italian line of a document");
            Counted(outside, "json_values", "data/values.json", "an Italian JSON value");
            Element(outside, "identifiers", identifier, "an Italian identifier is found");
            Element(outside, "json_keys", jsonKey, "an Italian JSON key is found");
            Element(outside, "protocol", protocol, "an Italian name as a value is found");
            Element(outside, "paths", segment, "a file in an Italian folder is found");
            var read = Strings(doc, "files_read");
            Check("the probe git calls binary is read, and its Italian found",
                  read.Contains("Checks/nul-probe.js") && outside.Any(h => S(h, "file") == "Checks/nul-probe.js"),
                  read.Contains("Checks/nul-probe.js") ? "read" : "NOT READ");

            Named(outside, "strings", "src/Hole.cs", "a string inside the hole of a C# interpolated string is found");
            Named(outside, "strings", "src/template.js", "a string inside the hole of a JavaScript template is found");
            Element(outside, "protocol", compound, "a protocol name joined by hyphens, found as a whole");
            Named(outside, "json_values", "data/probe-placeholder.json", "prose after a placeholder is found as prose");
            Element(outside, "json_keys", prose, "a JSON key with spaces is found as one key");
            Element(outside, "identifiers", newWord, "an identifier with a workshop word the repository does not use is found");
            Element(outside, "paths", holdout, "a folder named with a workshop word the repository does not use is found");
            Named(outside, "markdown", "RELEASE.md", "a line of Italian content words only is found");
            Named(outside, "strings", "Checks/hooks/pre-push", "the words of an echo in a shell script are found");
            Named(outside, "strings", "Checks/hooks/probe-heredoc", "the body of a here-document is found");
            Named(outside, "comments", "src/appended.js", "Italian appended to an English line is found");
            Named(outside, "markdown", "QUICKSTART.md", "the typographic apostrophe counts as the accent");
            Check("English lines with one Italian homograph each do not count",
                  !outside.Any(h => S(h, "file") == "docs/homographs.md"), string.Join(" | ", englishHomographs));
            Check("the totals of the report carry no category that is gone (transition)",
                  doc["totals"] is Dictionary<string, object> && !((Dictionary<string, object>)doc["totals"]).ContainsKey("transition"), "");
        }

        // ── 2. the exceptions ────────────────────────────────────────────────

        private static void Exceptions(string root, string repo)
        {
            Fixture(root, repo);
            var lexicon = ReadJson(Path.Combine(repo, "Checks", "language", "lexicon.json"));
            var samples = lexicon["injection_samples"] as Dictionary<string, object>;
            string prose = S(samples, "prose");
            var excFile = Path.Combine(repo, "Checks", "language", "exceptions.json");
            string text;
            int code;
            string ignored;

            Append(repo, "src/app.js", "// " + prose + " (again)");
            Run("git", "add -A", repo, out ignored);

            // an unsigned entry suppresses nothing, and is listed as waiting
            WriteEntries(excFile, Entry("solidworks-localized", "^src/app\\\\.js$", prose + " \\\\(again\\\\)", "null", "null"));
            text = Detect(repo, "", out code);
            Check("an UNSIGNED entry suppresses nothing: the gate fails and says the entry waits for the signature",
                  code == 1 && text.Contains("wait") && text.Contains("for the owner's signature") && text.Contains("[2] solidworks-localized") &&
                  !text.Contains("ITALIAN OUTSIDE EVERY EXCEPTION"),
                  "exit " + code + "; " + Tail(text));
            Check("and it says what the entry would cover", text.Contains("covers 1 occurrence(s) in 1 file(s)"), Tail(text));

            // the same entry, signed
            WriteEntries(excFile, Entry("solidworks-localized", "^src/app\\\\.js$", prose + " \\\\(again\\\\)", "\"the owner\"", "\"2026-10-06\""));
            text = Detect(repo, "", out code);
            Check("the same entry SIGNED makes the gate hold", code == 0 && text.Contains("language gate: held"), "exit " + code + "; " + Tail(text));

            // the other lines of the file stay judged
            Append(repo, "src/app.js", "// " + prose + " (and this one)");
            text = Detect(repo, "", out code);
            Check("a signed entry covers its pattern only: another Italian line of the file is found",
                  code == 1 && text.Contains("ITALIAN OUTSIDE EVERY EXCEPTION") && text.Contains("src/app.js:"), "exit " + code + "; " + Tail(text));
            File.WriteAllText(Path.Combine(repo, "src", "app.js"), "// an English comment\nvar x = 1;\n", new UTF8Encoding(false));

            // refused entries
            Refused(repo, excFile, Entry("solidworks-localized", "^src/app\\\\.js$", prose + " \\\\(again\\\\)", "\"the owner\"", "null"),
                "both null", "a signature without its date is refused");
            Refused(repo, excFile, Entry("solidworks-localized", "^src/app\\\\.js$", prose + " \\\\(again\\\\)", "\"the owner\"", "\"yesterday\""),
                "both null", "a date that is not a date is refused");
            Refused(repo, excFile, "{\"class\": \"solidworks-localized\", \"file\": \"^README\\\\.md$\"" + Rest("null", "null"), "never a whole file",
                "an exception that covers a whole file is refused");
            Refused(repo, excFile, "{\"class\": \"detector-lexicon\", \"file\": \"^Bench/cases/\", \"json_path\": \"^(name|steps|expect)\"" + Rest("null", "null"),
                "exactly one file of Checks/language", "a json_path over a folder of cases is refused");
            Refused(repo, excFile, "{\"class\": \"solidworks-localized\", \"file\": \"^Bench/cases/x\\\\.json$\", \"json_path\": \"^name$\"" + Rest("null", "null"),
                "only for the class detector-lexicon", "a json_path outside the class detector-lexicon is refused");
            var functionWords = Items(lexicon["function_words"]).Select(o => o.ToString()).Take(3).ToList();
            Refused(repo, excFile, "{\"class\": \"todo-reader\", \"pattern\": \"\\\\b(?:" + string.Join("|", functionWords) + ")\\\\b\"" + Rest("null", "null"),
                "function word", "a pattern made of Italian function words is refused");
            Refused(repo, excFile, Entry("transition", "^src/app\\\\.js$", prose, "null", "null"),
                "not in the closed list", "a class that is not one of the six (transition) is refused");
            Refused(repo, excFile, Entry("quote", "^src/app\\\\.js$", prose, "null", "null"),
                "not in the closed list", "a class that is not one of the six (quote) is refused");
            Refused(repo, excFile, "{\"class\": \"changelog-history\", \"file\": \"^CHANGELOG\\\\.md$\", \"pattern\": \"[^\\\\n]+\"" + Rest("null", "null"),
                "needs \"scope\"", "the history of the changelog without its scope is refused");
            Refused(repo, excFile, "{\"class\": \"changelog-quote\", \"file\": \"^src/app\\\\.js$\", \"pattern\": \"«[^»]*»\"" + Rest("null", "null"),
                "exactly the file ^CHANGELOG", "a class of the changelog on another file is refused");
            Refused(repo, excFile, "{\"class\": \"solidworks-localized\", \"file\": \"^src/app\\\\.js$\", \"pattern\": \"[^\\\\n]+\", \"scope\": { \"from\": \"^x\" }" + Rest("null", "null"),
                "only changelog-history", "a scope on another class is refused");

            // the two classes of the changelog
            WriteNew(repo, "CHANGELOG.md",
                "# Changelog\n\n## Unreleased — a new entry\n\nIt quotes «" + prose + "» and nothing else.\n\n## Unreleased — the history starts here\n\nOld text: " + prose + "\n");
            WriteEntries(excFile,
                "{\"class\": \"changelog-quote\", \"file\": \"^CHANGELOG\\\\.md$\", \"pattern\": \"«[^»]*»\"" + Rest("\"the owner\"", "\"2026-10-06\""),
                "{\"class\": \"changelog-history\", \"file\": \"^CHANGELOG\\\\.md$\", \"pattern\": \"[^\\\\n]+\", \"scope\": { \"from\": \"^## Unreleased — the history\" }" + Rest("\"the owner\"", "\"2026-10-06\""));
            Run("git", "add -A", repo, out ignored);
            text = Detect(repo, "", out code);
            Check("the changelog: the text between guillemets and the entries from the scope on are covered, once signed",
                  code == 0 && text.Contains("language gate: held"), "exit " + code + "; " + Tail(text));
            WriteNew(repo, "CHANGELOG.md",
                "# Changelog\n\n## Unreleased — a new entry\n\nA line that is not a quote: " + prose + "\n\n## Unreleased — the history starts here\n\nOld text: " + prose + "\n");
            text = Detect(repo, "", out code);
            Check("the changelog: Italian above the history and outside guillemets is found",
                  code == 1 && text.Contains("CHANGELOG.md:5"), "exit " + code + "; " + Tail(text));
            WriteNew(repo, "CHANGELOG.md",
                "# Changelog\n\n## Unreleased — a new entry\n\nAn English line.\n\n## Unreleased — the history starts here\n\nOld text: " + prose + "\n");
            WriteEntries(excFile,
                "{\"class\": \"changelog-history\", \"file\": \"^CHANGELOG\\\\.md$\", \"pattern\": \"[^\\\\n]+\", \"scope\": { \"from\": \"^## Unreleased — the history\" }" + Rest("null", "null"));
            text = Detect(repo, "", out code);
            Check("the changelog: the history is covered only when the entry is signed",
                  code == 1 && text.Contains("[2] changelog-history"), "exit " + code + "; " + Tail(text));
        }

        // One entry, as the owner would write it.
        private static string Entry(string cls, string file, string pattern, string by, string on)
        {
            return "{\"class\": \"" + cls + "\", \"file\": \"" + file + "\", \"pattern\": \"" + pattern + "\"" + Rest(by, on);
        }

        // The rest of an entry: applies_to, reason and the signature.
        private static string Rest(string by, string on)
        {
            return ", \"applies_to\": [\"detector\"], \"reason\": \"contract probe\", \"signed_by\": " + by + ", \"signed_on\": " + on + "}";
        }

        private static void WriteEntries(string file, params string[] entries)
        {
            File.WriteAllText(file, "[\n " + string.Join(",\n ", _base.Concat(entries)) + "\n]\n", new UTF8Encoding(false));
        }

        private static void Refused(string repo, string excFile, string entry, string expected, string name)
        {
            WriteEntries(excFile, entry);
            string text;
            int code = Run("node", Q(Path.Combine(repo, "Checks", "language", "detect-italian.js")) + " --root " + Q(repo), repo, out text);
            Check(name, code == 2 && text.Contains(expected), "exit " + code + "; " + Tail(text));
            WriteEntries(excFile);
        }

        // ── 3. the CI step on a throw-away repository with commits ───────────

        private static void GateScript(string root, string repo)
        {
            string text;
            if (Run("powershell.exe", "-NoProfile -Command \"exit 0\"", root, out text) != 0)
            {
                NotHere("the CI step of the language gate on a repository with commits",
                       "powershell.exe is not on this machine: the step is a PowerShell script and the CI runner has it.");
                return;
            }
            Fixture(root, repo);
            var step = Path.Combine(repo, ".github", "steps", "language-gate.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(step));
            File.Copy(Path.Combine(root, ".github", "steps", "language-gate.ps1"), step);
            var lexicon = ReadJson(Path.Combine(repo, "Checks", "language", "lexicon.json"));
            var samples = lexicon["injection_samples"] as Dictionary<string, object>;
            string prose = S(samples, "prose");
            var italianLine = "// " + prose + " (again)";

            Git(repo, "add -A");
            Git(repo, "commit -q -m " + Q("A: the repository with the gate"));
            var baseSha = GitOut(repo, "rev-parse HEAD");

            Func<string> gateRun = () =>
            {
                string o;
                int c = Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File " + Q(step) + " -Base " + baseSha, repo, out o);
                return c + "|" + o;
            };
            // each refusal must be refused FOR ITS REASON: the output has to
            // carry the line of the rule that is meant to fire
            Action<string, int, string> expect = (name, want, because) =>
            {
                var r = gateRun();
                int got = int.Parse(r.Substring(0, r.IndexOf('|')));
                var outText = r.Substring(r.IndexOf('|') + 1);
                bool reason = because == null || outText.Contains(because);
                Check("CI step: " + name, got == want && reason,
                      "expected exit " + want + (because == null ? "" : " with \"" + because + "\"") + ", got " + got + "; " + Tail(outText));
            };
            Action<string> branch = (name) => Git(repo, "checkout -q -B " + name + " " + baseSha);

            branch("clean");
            AppendLine(repo, "src/app.js", "// one more English line");
            Commit(repo, "clean English");
            expect("a clean English commit holds", 0, "language gate: held.");

            branch("italian");
            AppendLine(repo, "src/app.js", italianLine);
            Commit(repo, "Italian");
            expect("an Italian line fails and is listed outside every exception", 1, "ITALIAN OUTSIDE EVERY EXCEPTION");

            var excFile = Path.Combine(repo, "Checks", "language", "exceptions.json");
            branch("unsigned-entry");
            WriteEntries(excFile, Entry("solidworks-localized", "^src/app\\\\.js$", prose + " \\\\(again\\\\)", "null", "null"));
            AppendLine(repo, "src/app.js", italianLine);
            Commit(repo, "an entry for the exact sentence, not signed", "Language-Gate-Config-Change: the entry is waiting for the owner");
            expect("Italian covered by an entry that is not signed fails, and the step says the entry waits", 1, "for the owner's signature");

            branch("signed-entry");
            WriteEntries(excFile, Entry("solidworks-localized", "^src/app\\\\.js$", prose + " \\\\(again\\\\)", "\"the owner\"", "\"2026-10-06\""));
            AppendLine(repo, "src/app.js", italianLine);
            Commit(repo, "the entry, signed", "Language-Gate-Config-Change: the owner signed the entry");
            expect("Italian covered by a signed entry, and the change to the gate signed in the commit: holds", 0, "language gate: held.");

            branch("signed-entry-no-trailer");
            WriteEntries(excFile, Entry("solidworks-localized", "^src/app\\\\.js$", prose + " \\\\(again\\\\)", "\"the owner\"", "\"2026-10-06\""));
            AppendLine(repo, "src/app.js", italianLine);
            Commit(repo, "the entry, signed, without the trailer");
            expect("a change to the exceptions without its trailer is refused", 1, "without the trailer 'Language-Gate-Config-Change");

            branch("body");
            AppendLine(repo, "src/app.js", "// English");
            WriteEntries(excFile, Entry("solidworks-localized", "^src/app\\\\.js$", "x", "null", "null"));
            Commit(repo, "the words in the body", "Language-Gate-Config-Change: in the body", "and a paragraph after it, so it is not a trailer");
            expect("the signing words in the body, not as a trailer: refused", 1, "without the trailer");

            branch("lexicon");
            var lexFile = Path.Combine(repo, "Checks", "language", "lexicon.json");
            var lex = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(lexFile));
            var proseWords = prose.Split(' ');
            foreach (var k in new[] { "function_words", "content_words", "weak_words", "workshop_words" })
                if (lex.ContainsKey(k)) lex[k] = Items(lex[k]).Select(o => o.ToString()).Where(w => !proseWords.Contains(w)).ToArray();
            File.WriteAllText(lexFile, Json.Serialize(lex), new UTF8Encoding(false));
            AppendLine(repo, "src/app.js", italianLine);
            Commit(repo, "lexicon loosened", "Language-Gate-Config-Change: the words of the line taken out");
            expect("a lexicon loosened in a commit that signs the change to the gate: accepted, the change is visible", 0, null);

            branch("switched-off");
            File.WriteAllText(step, "Write-Output 'held'\nexit 0\n", new UTF8Encoding(false));
            AppendLine(repo, "src/app.js", italianLine);
            Commit(repo, "gate switched off");
            // on a pull request the workflow language-gate.yml runs the BASE's copy
            var baseCopy = Path.Combine(Path.GetDirectoryName(repo), "gate-of-the-base.ps1");
            File.WriteAllText(baseCopy, GitOut(repo, "show " + baseSha + ":.github/steps/language-gate.ps1") + "\n", new UTF8Encoding(false));
            string o2;
            int c2 = Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File " + Q(baseCopy) + " -Root " + Q(repo) + " -Base " + baseSha, repo, out o2);
            Check("CI step: the gate script switched off, judged by the base's copy: refused",
                  c2 == 1 && o2.Contains("changes the gate itself") && o2.Contains("ITALIAN OUTSIDE EVERY EXCEPTION"), "exit " + c2 + "; " + Tail(o2));
            Git(repo, "checkout -q -- .");
        }

        private static void Commit(string repo, string subject, params string[] paragraphs)
        {
            Git(repo, "add -A");
            Git(repo, "commit -q -m " + Q(subject) + string.Concat(paragraphs.Select(p => " -m " + Q(p))));
        }

        private static void Git(string repo, string arguments)
        {
            string o;
            if (Run("git", arguments, repo, out o) != 0) throw new InvalidOperationException("git " + arguments + ": " + Tail(o));
        }

        private static string GitOut(string repo, string arguments)
        {
            string o;
            if (Run("git", arguments, repo, out o) != 0) throw new InvalidOperationException("git " + arguments + ": " + Tail(o));
            return o.TrimEnd('\r', '\n');
        }

        private static void AppendLine(string repo, string file, string line)
        {
            File.AppendAllText(Path.Combine(repo, file.Replace('/', Path.DirectorySeparatorChar)), line + "\n", new UTF8Encoding(false));
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static void Counted(List<Dictionary<string, object>> outside, string category, string file, string what)
        {
            Check(what + " is found (" + category + ")", Has(outside, category, file), category + " in " + file);
        }

        private static void Named(List<Dictionary<string, object>> outside, string category, string file, string name)
        {
            Check(name, Has(outside, category, file), category + " in " + file);
        }

        private static bool Has(List<Dictionary<string, object>> outside, string category, string file)
        {
            return outside.Any(h => S(h, "category") == category && S(h, "file") == file);
        }

        private static void Element(List<Dictionary<string, object>> outside, string category, string element, string name)
        {
            bool found = outside.Any(h => S(h, "category") == category && S(h, "element") == element);
            Check(name, found, category + ": " + element);
        }

        private static void Append(string repo, string file, string line)
        {
            var full = Path.Combine(repo, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.AppendAllText(full, "\n" + line + "\n", new UTF8Encoding(false));
        }

        private static void WriteNew(string repo, string file, string content)
        {
            var full = Path.Combine(repo, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content, new UTF8Encoding(false));
        }

        private static Dictionary<string, object> ReadJson(string file)
        {
            return Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(file).TrimStart((char)0xFEFF));
        }

        private static HashSet<string> Strings(Dictionary<string, object> doc, string key)
        {
            var arr = doc.ContainsKey(key) ? Items(doc[key]) : null;
            return new HashSet<string>((arr ?? new object[0]).Select(o => o.ToString()), StringComparer.Ordinal);
        }

        // JavaScriptSerializer gives arrays as object[] or ArrayList depending
        // on how it was called: both are read the same way here.
        private static IEnumerable<object> Items(object value)
        {
            var list = value as System.Collections.IEnumerable;
            if (list == null || value is string) return new object[0];
            return list.Cast<object>().ToList();
        }

        private static string S(Dictionary<string, object> d, string key)
        {
            return d != null && d.ContainsKey(key) && d[key] != null ? d[key].ToString() : "";
        }

        private static string Q(string s) { return "\"" + s + "\""; }

        private static string Tail(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var lines = s.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToList();
            return string.Join(" | ", lines.Skip(Math.Max(0, lines.Count - 3)));
        }

        private static int Run(string exe, string arguments, string cwd, out string output)
        {
            var psi = new ProcessStartInfo(exe, arguments)
            {
                WorkingDirectory = cwd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEndAsync();
                    var e = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(180000)) { try { p.Kill(); } catch { } output = "timed out"; return -1; }
                    output = o.Result + e.Result;
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }
    }
}
