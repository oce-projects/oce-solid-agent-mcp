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
    /// THE LANGUAGE GATE CATCHES WHAT IT CLAIMS TO CATCH (entry W3.1-bis.1).
    ///
    /// ── WHY A CONTRACT AND NOT ONLY THE CI STEP ──────────────────────────
    ///
    /// `Checks/language/detect-italian.js` guards a ratchet: the Italian in
    /// the repository may go down, never up. A ratchet that never fires looks
    /// exactly like a ratchet that works, and the tree it runs on today has
    /// nothing new to catch. So this contract does two things.
    ///
    /// ── 1. THE DETECTOR, ON A COPY OF THE TRACKED TREE ─────────────────
    ///
    /// A baseline is written on the untouched copy, then Italian is injected
    /// and the detector must say what grew, by name, in the file or with the
    /// element where it was injected:
    ///
    ///   * one line for each of the nine categories;
    ///   * a new identifier glued together from Italian words that are already
    ///     in the repository one by one: new as a whole, so blocked;
    ///   * a new file whose folder is an Italian word: blocked;
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
    /// ── 2. THE CI STEP, ON A THROW-AWAY REPOSITORY WITH COMMITS ────────
    ///
    /// `.github/steps/language-gate.ps1` judges every commit of a range
    /// against its parent. A small repository with the gate is built, and
    /// each case is a branch from the same base commit: a growth signed on a
    /// later commit, a signature on the wrong commit, a loosened lexicon, an
    /// exception for the exact sentence, a rename, a JSON key with spaces, a
    /// trailer word in the body, a merge, the gate script switched off, a
    /// release tag with transition left. Each must end as expected.
    ///
    /// The Italian samples come from `injection_samples` and
    /// `holdout_workshop_terms` in lexicon.json, so this source file carries
    /// none.
    /// </summary>
    internal static class LanguageGateTests
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        internal static void All()
        {
            Group("the language gate catches every category (W3.1-bis.1)");

            var root = Root();
            var detector = Path.Combine(root, "Checks", "language", "detect-italian.js");
            if (!File.Exists(detector)) { Check("the detector exists", false, detector); return; }

            string version;
            if (Run("node", "--version", root, out version) != 0)
            {
                NotHere("the language gate on a copy of the tree",
                       "node is not on this machine: the gate is a Node script and the CI runner has Node. " +
                       "Not running it here is not the same as passing.");
                return;
            }

            var work = Path.Combine(Path.GetTempPath(), "language-gate-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var copy = Path.Combine(work, "tree");
            var output = Path.Combine(work, "out");
            try
            {
                Directory.CreateDirectory(output);
                var tracked = CopyTrackedTree(root, copy);
                if (tracked == null) return;

                Probe(copy, output, tracked);
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

        private static void Probe(string copy, string output, List<string> tracked)
        {
            var detector = Path.Combine(copy, "Checks", "language", "detect-italian.js");
            var baseline = Path.Combine(output, "base.json");
            string text;

            // 1. a baseline on the untouched copy
            int code = Run("node", Q(detector) + " --root " + Q(copy) + " --write-baseline " + Q(baseline), copy, out text);
            Check("the gate runs on a copy of the tracked tree", code == 0 && File.Exists(baseline), Tail(text));
            if (code != 0) return;

            // 2. nothing grew on the untouched copy, and every text file was read
            var report0 = Path.Combine(output, "report0.json");
            code = Run("node", Q(detector) + " --root " + Q(copy) + " --baseline " + Q(baseline) + " --report " + Q(report0), copy, out text);
            Check("on the untouched copy nothing grew", code == 0, Tail(text));
            var read0 = Strings(ReadJson(report0), "files_read");
            var textFiles = tracked.Where(f => !HasBinaryExtension(f)).ToList();
            var unread = textFiles.Where(f => !read0.Contains(f)).ToList();
            Check("every tracked file with a text extension is read",
                  textFiles.Count > 400 && unread.Count == 0,
                  read0.Count + " read of " + textFiles.Count + (unread.Count == 0 ? "" : "; not read: " + string.Join(", ", unread.Take(5))));
            Check("the file that used to carry a NUL byte is read",
                  read0.Contains("Checks/questions-without-answer.js"), "Checks/questions-without-answer.js");

            // 3. the samples, and the proof that the glued identifier is new
            var lexicon = ReadJson(Path.Combine(copy, "Checks", "language", "lexicon.json"));
            var samples = lexicon.ContainsKey("injection_samples") ? lexicon["injection_samples"] as Dictionary<string, object> : null;
            if (samples == null) { Check("lexicon.json has injection_samples", false, ""); return; }
            string prose = S(samples, "prose"), transition = S(samples, "transition"), identifier = S(samples, "identifier"),
                   jsonKey = S(samples, "json_key"), protocol = S(samples, "protocol"), segment = S(samples, "path_segment"),
                   compound = S(samples, "compound"), contentOnly = S(samples, "content_only"), appended = S(samples, "appended"),
                   typographic = S(samples, "typographic");
            var englishHomographs = Items(samples.ContainsKey("weak_alone_english") ? samples["weak_alone_english"] : null).Select(o => o.ToString()).ToList();
            var knownParts = Items(samples["identifier_known_parts"]).Select(o => o.ToString()).ToList();
            // a workshop word the repository does not use, caught by its ending only
            var holdout = Items(lexicon.ContainsKey("holdout_workshop_terms") ? lexicon["holdout_workshop_terms"] : null)
                          .Select(o => o.ToString()).FirstOrDefault(t => t.EndsWith("atura", StringComparison.Ordinal));
            if (new[] { compound, contentOnly, appended, typographic, holdout }.Any(string.IsNullOrEmpty) || englishHomographs.Count == 0)
            {
                Check("lexicon.json has every injection sample", false, "compound, content_only, appended, typographic, weak_alone_english, holdout_workshop_terms");
                return;
            }
            var newWord = "probe" + char.ToUpperInvariant(holdout[0]) + holdout.Substring(1);

            var baseDoc = ReadJson(baseline);
            var baseSets = baseDoc["sets"] as Dictionary<string, object>;
            var baseIdentifiers = new HashSet<string>(Items(baseSets["identifiers"]).Select(o => o.ToString()), StringComparer.Ordinal);
            Check("the glued identifier is new, and its words are already there one by one",
                  !baseIdentifiers.Contains(identifier) && knownParts.Count >= 2 && knownParts.All(baseIdentifiers.Contains),
                  identifier + " made of " + string.Join(" + ", knownParts));

            // 4. the injections, one per category
            Append(copy, "Checks/language/measure.js", "// " + prose);
            Append(copy, "Checks/zero-or-unknown.js", "var probeText = \"" + prose + "\";");
            Append(copy, "README.md", prose);
            Append(copy, "Checks/count-tested.js", "var " + identifier + " = 0;");
            Append(copy, "Checks/rename-product.js", "// " + transition);
            var jsonFile = Path.Combine(copy, "Checks", "config-with-other-servers.json");
            var jsonText = File.ReadAllText(jsonFile);
            int brace = jsonText.IndexOf('{');
            jsonText = jsonText.Substring(0, brace + 1) +
                       "\n  \"probe_note\": \"" + prose + "\",\n  \"" + jsonKey + "\": 1,\n  \"probe_value\": \"" + protocol + "\"," +
                       "\n  \"probe_compound\": \"" + compound + "\"," +
                       jsonText.Substring(brace + 1);
            File.WriteAllText(jsonFile, jsonText, new UTF8Encoding(false));
            var segmentDir = Path.Combine(copy, "Bench", segment);
            Directory.CreateDirectory(segmentDir);
            File.WriteAllText(Path.Combine(segmentDir, "notes.md"), "Gate probe.\n", new UTF8Encoding(false));
            // a text extension with a NUL byte: git calls it binary
            var nulBytes = new List<byte>(Encoding.UTF8.GetBytes("// " + prose + "\n"));
            nulBytes.Add(0);
            nulBytes.AddRange(Encoding.UTF8.GetBytes("\n"));
            File.WriteAllBytes(Path.Combine(copy, "Checks", "nul-probe.js"), nulBytes.ToArray());
            // the transition exception, as the rename windows will write them
            var exceptionsFile = Path.Combine(copy, "Checks", "language", "exceptions.json");
            var exceptionsText = File.ReadAllText(exceptionsFile).TrimEnd();
            var transitionEntry = ",\n {\"class\": \"transition\", \"file\": \"^Checks/rename-product\\\\.js$\", \"pattern\": \"" +
                                  transition + "\", \"applies_to\": [\"detector\"], \"reason\": \"contract probe of the transition category\", " +
                                  "\"signed_by\": \"LanguageGateTests\", \"date\": \"2026-09-28\"}\n]";
            File.WriteAllText(exceptionsFile, exceptionsText.Substring(0, exceptionsText.LastIndexOf(']')).TrimEnd() + transitionEntry + "\n",
                              new UTF8Encoding(false));

            // the holes of the review, each in a file of its own
            Append(copy, "Tests/Load.cs", "class GateProbeHole { string s = $\"R {(true ? \"" + prose + "\" : \"x\")}\"; }");
            Append(copy, "Bench/test_undo.js", "var gateProbeTemplate = `${1 ? \"" + prose + "\" : \"\"}`;");
            WriteNew(copy, "Checks/probe-placeholder.json", "{\"note\": \"{NAME} " + prose + "\"}\n");
            WriteNew(copy, "Checks/probe-key-with-spaces.json", "{\"" + prose + "\": 1}\n");
            Append(copy, "Bench/test_section.js", "var " + newWord + " = 0;");
            WriteNew(copy, "Bench/" + holdout + "/notes.md", "Gate probe.\n");
            Append(copy, "Bench/README.md", contentOnly);
            Append(copy, "Checks/hooks/pre-push", "echo " + prose);
            WriteNew(copy, "Checks/hooks/probe-heredoc", "#!/bin/sh\ncat <<EOF\n" + prose + "\nEOF\n");
            Append(copy, "QUICKSTART.md", typographic);
            foreach (var e in englishHomographs) Append(copy, "RELEASE.md", e);
            // Italian appended to a comment line that is already Italian (the
            // first line of this PowerShell file that opens with "# that is read is":
            // the one comment whose Italian quotes what git prints, so it stays Italian)
            var workflowCheck = Path.Combine(copy, "Checks", "check-publishability.ps1");
            var wcLines = File.ReadAllLines(workflowCheck).ToList();
            int target = wcLines.FindIndex(l => l.StartsWith("# that is read is", StringComparison.Ordinal));
            Check("the Italian comment line to append to is there", target >= 0, "Checks/check-publishability.ps1");
            if (target >= 0) { wcLines[target] = wcLines[target] + " " + appended; File.WriteAllLines(workflowCheck, wcLines, new UTF8Encoding(false)); }

            Run("git", "add -A", copy, out text);

            string numstat;
            Run("git", "diff --cached --numstat -- Checks/nul-probe.js", copy, out numstat);
            Check("git calls the probe with a NUL byte binary", numstat.StartsWith("-\t-\t", StringComparison.Ordinal), numstat.Trim());

            // 5. what grew
            var report1 = Path.Combine(output, "report1.json");
            code = Run("node", Q(detector) + " --root " + Q(copy) + " --baseline " + Q(baseline) + " --report " + Q(report1), copy, out text);
            Check("with the injections the gate exits 1 and says why",
                  code == 1 && text.Contains("LANGUAGE GATE:"), "exit " + code + "; " + Tail(text));
            if (!File.Exists(report1)) return;
            var report = ReadJson(report1);
            var growth = Items(report["growth"]).Cast<Dictionary<string, object>>().ToList();

            Counted(growth, "comments", "Checks/language/measure.js");
            Counted(growth, "strings", "Checks/zero-or-unknown.js");
            Counted(growth, "markdown", "README.md");
            Counted(growth, "json_values", "Checks/config-with-other-servers.json");
            Counted(growth, "transition", "Checks/rename-product.js");
            Element(growth, "identifiers", identifier, "a new identifier glued from Italian words already present is blocked");
            Element(growth, "json_keys", jsonKey, "an Italian line in json_keys is counted");
            Element(growth, "protocol", protocol, "an Italian line in protocol is counted");
            Element(growth, "paths", segment, "a new file with an Italian folder is blocked");
            var read1 = Strings(report, "files_read");
            Check("the probe git calls binary is read, and its Italian counted",
                  read1.Contains("Checks/nul-probe.js") && HasGrowth(growth, "comments", "Checks/nul-probe.js"),
                  read1.Contains("Checks/nul-probe.js") ? "read" : "NOT READ");

            Named(growth, "strings", "Tests/Load.cs", "a string inside the hole of a C# interpolated string is counted");
            Named(growth, "strings", "Bench/test_undo.js", "a string inside the hole of a JavaScript template is counted");
            Element(growth, "protocol", compound, "a protocol name joined by hyphens, new as a whole, is blocked");
            Named(growth, "json_values", "Checks/probe-placeholder.json", "prose after a placeholder is counted as prose");
            Element(growth, "json_keys", prose, "a JSON key with spaces is one new key");
            Element(growth, "identifiers", newWord, "an identifier with a workshop word the repository does not use is blocked");
            Element(growth, "paths", holdout, "a folder named with a workshop word the repository does not use is blocked");
            Named(growth, "markdown", "Bench/README.md", "a line of Italian content words only is counted");
            Named(growth, "strings", "Checks/hooks/pre-push", "the words of an echo in a shell script are counted");
            Named(growth, "strings", "Checks/hooks/probe-heredoc", "the body of a here-document is counted");
            Named(growth, "comments", "Checks/check-publishability.ps1", "Italian appended to a line that is already Italian makes the file grow");
            Named(growth, "markdown", "QUICKSTART.md", "the typographic apostrophe counts as the accent");
            Check("English lines with one Italian homograph each do not count",
                  !growth.Any(g => S(g, "file") == "RELEASE.md"), string.Join(" | ", englishHomographs));

            // 6. a baseline raised in place is caught, a lowered one is not
            var raised = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(baseline));
            var files = raised["files"] as Dictionary<string, object>;
            var anyFile = files.Keys.First();
            var counts = files[anyFile] as Dictionary<string, object>;
            var firstCategory = counts.Keys.First();
            counts[firstCategory] = Convert.ToInt32(counts[firstCategory]) + 1;
            var raisedFile = Path.Combine(output, "raised.json");
            File.WriteAllText(raisedFile, Json.Serialize(raised), new UTF8Encoding(false));
            int up = Run("node", Q(detector) + " --compare-baselines " + Q(baseline) + " " + Q(raisedFile), copy, out text);
            int down = Run("node", Q(detector) + " --compare-baselines " + Q(raisedFile) + " " + Q(baseline), copy, out text);
            Check("a baseline raised in place is caught, a lowered one passes", up == 1 && down == 0,
                  "raised: exit " + up + ", lowered: exit " + down);

            // 7. the JSON key with spaces does not jam the gate: once the
            // baseline is rewritten, the same tree holds
            var rewritten = Path.Combine(output, "rewritten.json");
            Run("node", Q(detector) + " --root " + Q(copy) + " --write-baseline " + Q(rewritten), copy, out text);
            code = Run("node", Q(detector) + " --root " + Q(copy) + " --baseline " + Q(rewritten), copy, out text);
            Check("after a rewritten baseline the injected tree holds (no element that never matches)", code == 0, "exit " + code + "; " + Tail(text));

            // 8. exceptions that cover a whole file, directly or by a side door, are refused
            var original = File.ReadAllText(exceptionsFile);
            Refused(copy, detector, baseline, exceptionsFile, original,
                "{\"class\": \"quote\", \"file\": \"^README\\\\.md$\"}", "never a whole file",
                "an exception that covers a whole file is refused");
            Refused(copy, detector, baseline, exceptionsFile, original,
                "{\"class\": \"detector-lexicon\", \"file\": \"^Bench/cases/\", \"json_path\": \"^(nome|passi|attese)\"}", "exactly one file of Checks/language",
                "a json_path over a folder of cases is refused");
            Refused(copy, detector, baseline, exceptionsFile, original,
                "{\"class\": \"quote\", \"file\": \"^Bench/cases/x\\\\.json$\", \"json_path\": \"^nome$\"}", "only for the class detector-lexicon",
                "a json_path outside the class detector-lexicon is refused");
            var functionWords = Items(lexicon["function_words"]).Select(o => o.ToString()).Take(3).ToList();
            Refused(copy, detector, baseline, exceptionsFile, original,
                "{\"class\": \"quote\", \"pattern\": \"\\\\b(?:" + string.Join("|", functionWords) + ")\\\\b\"}", "function word",
                "a pattern made of Italian function words is refused");
            File.WriteAllText(exceptionsFile, original, new UTF8Encoding(false));
        }

        private static void Refused(string copy, string detector, string baseline, string exceptionsFile, string original,
                                    string entry, string expected, string name)
        {
            var full = entry.Substring(0, entry.Length - 1) +
                       ", \"applies_to\": [\"detector\"], \"reason\": \"contract probe\", \"signed_by\": \"LanguageGateTests\", \"date\": \"2026-09-28\"}";
            var t = original.TrimEnd();
            File.WriteAllText(exceptionsFile, t.Substring(0, t.LastIndexOf(']')).TrimEnd() + ",\n " + full + "\n]\n", new UTF8Encoding(false));
            string text;
            int code = Run("node", Q(detector) + " --root " + Q(copy) + " --baseline " + Q(baseline), copy, out text);
            Check(name, code == 2 && text.Contains(expected), "exit " + code + "; " + Tail(text));
            File.WriteAllText(exceptionsFile, original, new UTF8Encoding(false));
        }

        // ── 2. the CI step on a throw-away repository with commits ───────────

        private static void GateScript(string root, string repo)
        {
            string text;
            if (Run("powershell.exe", "-NoProfile -Command \"exit 0\"", root, out text) != 0)
            {
                NotHere("the CI step of the language gate on a repository with commits",
                       "powershell.exe is not on this machine: the step is a PowerShell script and the CI runner has it.");
                return;
            }
            var lexicon = ReadJson(Path.Combine(root, "Checks", "language", "lexicon.json"));
            var samples = lexicon["injection_samples"] as Dictionary<string, object>;
            string prose = S(samples, "prose"), transition = S(samples, "transition");
            var proseWords = prose.Split(' ');

            // the repository: the gate, a JavaScript file with one Italian line, a JSON file
            Directory.CreateDirectory(repo);
            foreach (var f in new[] { "Checks/language/detect-italian.js", "Checks/language/lexicon.json", "Checks/language/exceptions.json",
                                      ".github/steps/language-gate.ps1" })
            {
                var to = Path.Combine(repo, f.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(Path.Combine(root, f.Replace('/', Path.DirectorySeparatorChar)), to);
            }
            WriteNew(repo, "src/app.js", "// an English comment\n// " + prose + "\nvar x = 1;\n");
            WriteNew(repo, "data/values.json", "{\"name\": \"value\"}\n");
            Git(repo, "init -q");
            Git(repo, "config core.autocrlf false");
            Git(repo, "config user.email gate@example.invalid");
            Git(repo, "config user.name LanguageGateTests");
            Git(repo, "config commit.gpgsign false");
            WriteBaseline(repo);
            Git(repo, "add -A");
            Git(repo, "commit -q -m " + Q("A: the repository with the gate"));
            var baseSha = GitOut(repo, "rev-parse HEAD");
            var gate = Path.Combine(repo, ".github", "steps", "language-gate.ps1");

            Func<string> gateRun = () =>
            {
                string o;
                int c = Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File " + Q(gate) + " -Base " + baseSha, repo, out o);
                return c + "|" + o;
            };
            // each refusal must be refused FOR ITS REASON: the output has to
            // carry the line of the check that is meant to fire
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
            var italianLine = "// " + prose + " (again)";

            branch("clean");
            AppendLine(repo, "src/app.js", "// one more English line");
            Commit(repo, "clean English");
            expect("a clean English commit holds", 0, "language gate: held.");

            branch("signed-later");
            AppendLine(repo, "src/app.js", italianLine);
            Commit(repo, "unsigned Italian");
            WriteBaseline(repo);
            Commit(repo, "the raise", "Language-Baseline-Raise: signed on the next commit");
            expect("growth in an earlier commit, signature on a later one: refused", 1, "is ABOVE the baseline of its parent");

            branch("wrong-commit");
            AppendLine(repo, "src/app.js", italianLine);
            WriteBaseline(repo);
            Commit(repo, "raise, unsigned");
            AppendLine(repo, "src/app.js", "// English");
            Commit(repo, "another commit", "Language-Baseline-Raise: on the wrong commit");
            expect("the signature on a different commit than the raise: refused", 1, "RAISES Checks/language/baseline.json without the trailer");

            branch("signed");
            AppendLine(repo, "src/app.js", italianLine);
            WriteBaseline(repo);
            Commit(repo, "raise", "Language-Baseline-Raise: an approved window");
            expect("a raise signed on the commit that raises: accepted", 0, "baseline raised, signed");

            branch("lexicon");
            var lexFile = Path.Combine(repo, "Checks", "language", "lexicon.json");
            var lex = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(lexFile));
            foreach (var k in new[] { "function_words", "content_words", "weak_words", "workshop_words" })
                if (lex.ContainsKey(k)) lex[k] = Items(lex[k]).Select(o => o.ToString()).Where(w => !proseWords.Contains(w)).ToArray();
            File.WriteAllText(lexFile, Json.Serialize(lex), new UTF8Encoding(false));
            AppendLine(repo, "src/app.js", italianLine);
            WriteBaseline(repo);
            Commit(repo, "lexicon loosened", "Language-Gate-Config-Change: the words of the line taken out");
            expect("a loosened lexicon does not judge its own commit: refused", 1, "(rules of the parent)");

            branch("exception");
            var excFile = Path.Combine(repo, "Checks", "language", "exceptions.json");
            var exc = File.ReadAllText(excFile).TrimEnd();
            File.WriteAllText(excFile, exc.Substring(0, exc.LastIndexOf(']')).TrimEnd() +
                ",\n {\"class\": \"quote\", \"pattern\": \"" + prose + " \\\\(again\\\\)\", \"applies_to\": [\"detector\"], " +
                "\"reason\": \"contract probe\", \"signed_by\": \"LanguageGateTests\", \"date\": \"2026-09-28\"}\n]\n", new UTF8Encoding(false));
            AppendLine(repo, "src/app.js", italianLine);
            Commit(repo, "an exception for the exact sentence");
            expect("an exception for the exact sentence, unsigned: refused", 1, "changes the gate itself");

            branch("rename");
            Git(repo, "mv src/app.js src/renamed.js");
            WriteBaseline(repo);
            Commit(repo, "rename");
            expect("a file renamed by hand, baseline rewritten, unsigned: accepted", 0, "compared with their old paths");

            branch("key-with-spaces");
            WriteNew(repo, "data/keys.json", "{\"" + prose + "\": 1}\n");
            WriteBaseline(repo);
            Commit(repo, "a key with spaces", "Language-Baseline-Raise: probe");
            AppendLine(repo, "src/app.js", "// English");
            Commit(repo, "the next commit");
            expect("a JSON key with spaces, raised and signed, then a clean commit: accepted", 0, "language gate: held.");

            branch("body");
            AppendLine(repo, "src/app.js", italianLine);
            WriteBaseline(repo);
            Commit(repo, "the words in the body", "Language-Baseline-Raise: in the body", "and a paragraph after it, so it is not a trailer");
            expect("the signing words in the body, not as a trailer: refused", 1, "without the trailer");

            branch("merge-main");
            Git(repo, "checkout -q -B side " + baseSha);
            AppendLine(repo, "src/app.js", italianLine);
            WriteBaseline(repo);
            Commit(repo, "side raise", "Language-Baseline-Raise: side branch");
            Git(repo, "checkout -q merge-main");
            WriteNew(repo, "data/more.json", "{\"other\": \"value\"}\n");
            Commit(repo, "main moves on");
            Git(repo, "merge -q --no-ff side -m " + Q("merge side"));
            expect("a merge of a branch whose raise is signed: accepted", 0, "language gate: held.");

            branch("switched-off");
            File.WriteAllText(gate, "Write-Output 'held'\nexit 0\n", new UTF8Encoding(false));
            AppendLine(repo, "src/app.js", italianLine);
            Commit(repo, "gate switched off");
            // on a pull request the workflow language-gate.yml runs the BASE's copy
            var baseCopy = Path.Combine(Path.GetDirectoryName(repo), "gate-of-the-base.ps1");
            File.WriteAllText(baseCopy, GitOut(repo, "show " + baseSha + ":.github/steps/language-gate.ps1") + "\n", new UTF8Encoding(false));
            string o2;
            int c2 = Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File " + Q(baseCopy) + " -Root " + Q(repo) + " -Base " + baseSha, repo, out o2);
            Check("CI step: the gate script switched off, judged by the base's copy: refused", c2 == 1 && o2.Contains("changes the gate itself"), "exit " + c2 + "; " + Tail(o2));
            Git(repo, "checkout -q -- .");

            branch("release");
            exc = File.ReadAllText(excFile).TrimEnd();
            File.WriteAllText(excFile, exc.Substring(0, exc.LastIndexOf(']')).TrimEnd() +
                ",\n {\"class\": \"transition\", \"file\": \"^src/app\\\\.js$\", \"pattern\": \"" + transition + "\", \"applies_to\": [\"detector\"], " +
                "\"reason\": \"contract probe\", \"signed_by\": \"LanguageGateTests\", \"date\": \"2026-09-28\"}\n]\n", new UTF8Encoding(false));
            AppendLine(repo, "src/app.js", "// " + transition);
            WriteBaseline(repo);
            // two trailers go together, in the LAST paragraph of the message
            Commit(repo, "a transition alias", "Language-Baseline-Raise: an alias until the tag" + "\n" +
                                               "Language-Gate-Config-Change: the transition exception");
            expect("transition allowed before the tag", 0, "language gate: held.");
            Git(repo, "tag v0.1.0");
            expect("a release tag with transition left: refused", 1, "with Italian still in transition");
        }

        // the detector lists the TRACKED files: new ones are added first
        private static void WriteBaseline(string repo)
        {
            Git(repo, "add -A");
            string o;
            int c = Run("node", Q(Path.Combine(repo, "Checks", "language", "detect-italian.js")) + " --root " + Q(repo) + " --write-baseline", repo, out o);
            if (c != 0) throw new InvalidOperationException("write-baseline failed: " + Tail(o));
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

        private static void Counted(List<Dictionary<string, object>> growth, string category, string file)
        {
            Check("an Italian line in " + category + " is counted", HasGrowth(growth, category, file), category + " in " + file);
        }

        private static void Named(List<Dictionary<string, object>> growth, string category, string file, string name)
        {
            Check(name, HasGrowth(growth, category, file), category + " in " + file);
        }

        private static bool HasGrowth(List<Dictionary<string, object>> growth, string category, string file)
        {
            return growth.Any(g => S(g, "category") == category && S(g, "file") == file);
        }

        private static void Element(List<Dictionary<string, object>> growth, string category, string element, string name)
        {
            bool found = growth.Any(g => S(g, "category") == category &&
                                         g.ContainsKey("elements") &&
                                         Items(g["elements"]).Any(e => e != null && e.ToString() == element));
            Check(name, found, category + ": " + element);
        }

        // The tracked files, copied as they are on disk, in a fresh repository.
        private static List<string> CopyTrackedTree(string root, string copy)
        {
            string list;
            if (Run("git", "-C " + Q(root) + " ls-files -z", root, out list) != 0)
            {
                NotHere("the language gate on a copy of the tree", "git ls-files did not answer here: " + Tail(list));
                return null;
            }
            var tracked = list.Split('\0').Where(s => s.Length > 0).ToList();
            foreach (var f in tracked)
            {
                var from = Path.Combine(root, f.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(from)) continue;
                var to = Path.Combine(copy, f.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(from, to);
            }
            string ignored;
            Run("git", "init -q", copy, out ignored);
            Run("git", "config core.autocrlf false", copy, out ignored);
            Run("git", "add -A", copy, out ignored);
            return tracked;
        }

        private static readonly HashSet<string> BinaryExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tif", ".tiff", ".webp",
            ".sldprt", ".sldasm", ".slddrw", ".sldlfp", ".sldmat", ".sldblk", ".slddrt", ".prtdot", ".asmdot", ".drwdot",
            ".step", ".stp", ".igs", ".iges", ".stl", ".x_t", ".x_b", ".dxf", ".dwg",
            ".dll", ".exe", ".pdb", ".lib", ".obj", ".snk", ".pfx",
            ".pdf", ".zip", ".7z", ".gz", ".tar", ".rar", ".nupkg",
            ".ttf", ".otf", ".woff", ".woff2", ".eot",
        };

        private static bool HasBinaryExtension(string file)
        {
            return BinaryExtensions.Contains(Path.GetExtension(file));
        }

        private static void Append(string copy, string file, string line)
        {
            var full = Path.Combine(copy, file.Replace('/', Path.DirectorySeparatorChar));
            File.AppendAllText(full, "\n" + line + "\n", new UTF8Encoding(false));
        }

        private static void WriteNew(string copy, string file, string content)
        {
            var full = Path.Combine(copy, file.Replace('/', Path.DirectorySeparatorChar));
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
