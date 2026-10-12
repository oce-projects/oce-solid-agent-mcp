using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE CATALOGUE, which is the real interface to the model (entry C1/F7).
    ///
    /// It is read by REFLECTION from the already compiled bridge — `Catalog` and its
    /// `Similar()` live there — and the `catalog.json` file is pointed out to it with
    /// `OCE_CATALOG`, which is the same variable that whoever installs
    /// the package elsewhere would use.
    ///
    /// None of this touches SOLIDWORKS: `Catalog` reads a JSON file and
    /// compares strings.
    /// </summary>
    internal static class CatalogTests
    {
        internal static void All()
        {
            Group("the catalog");

            var json = Path.Combine(Root(), "bin", "catalog.json");

            // THE CONTENT OF THE CATALOGUE CAN BE READ HERE TOO (entry F9-bis).
            //
            // `bin\\catalog.json` is produced by `build.ps1`, which without interop does not
            // run: on a runner without SOLIDWORKS this group stopped at the
            // first line and thirty-two checks on the CONTENT — families, required
            // fields, duplicate names — were not done although they have
            // nothing to do with SOLIDWORKS. They read a JSON.
            //
            // `Tools\\catalog.json` is the same file, it is in the repository and
            // it is the one that `build.ps1` COPIES into `bin\\`. We fall back there, and we say so.
            // The comparison between the two stays impossible, and further down it says so.
            bool fromRepository = false;
            if (!ProductFromBuild(json))
            {
                var inRepository = Path.Combine(Root(), "Tools", "catalog.json");
                if (!TheBridgeCouldBeBuilt() && File.Exists(inRepository))
                {
                    json = inRepository;
                    fromRepository = true;
                }
                else
                {
                    Check("the catalog is there", false,
                          "cannot find " + json + " — produce it with .\\build.ps1");
                    return;
                }
            }
            Check("the catalog is there", true,
                  fromRepository
                      ? json + " (the one in the repository: SOLIDWORKS is not here, " +
                        "so bin\\catalog.json could not be produced)"
                      : json);

            Dictionary<string, object> root;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                s.MaxJsonLength = 40 * 1024 * 1024;
                root = s.DeserializeObject(File.ReadAllText(json)) as Dictionary<string, object>;
            }
            catch (Exception ex) { Check("the catalog can be read", false, ex.Message); return; }

            var operations = (root != null && root.ContainsKey("operations")
                ? root["operations"] : null) as object[];
            if (operations == null) { Check("the catalog has «operations»", false, ""); return; }
            Check("the catalog has at least thirty operations", operations.Length >= 30,
                  operations.Length + " operations");

            TheReadmeStatesTheRightNumber(operations.Length);

            // ── EVERY OPERATION HAS THE FIELDS THAT ARE NEEDED ────────────
            var without = new List<string>();
            var families = new HashSet<string>(StringComparer.Ordinal);
            var catalogNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in operations)
            {
                var v = o as Dictionary<string, object>;
                if (v == null) { without.Add("(an entry is not an object)"); continue; }
                string itemName = v.ContainsKey("name") ? Convert.ToString(v["name"]) : null;
                if (itemName != null) catalogNames.Add(itemName);
                foreach (var theField in new[] { "name", "family", "summary", "details", "schema" })
                    if (!v.ContainsKey(theField) || v[theField] == null)
                        without.Add((itemName ?? "?") + " without «" + theField + "»");
                if (v.ContainsKey("family")) families.Add(Convert.ToString(v["family"]));
            }
            Check("every operation has name, family, summary, description, schema",
                  without.Count == 0, string.Join("; ", without.Take(4)));

            // ── THE DECLARED FAMILIES ARE THE USED ONES ───────────────────
            //
            // A family that is used and not declared is an operation that does not
            // appear in the menu: the model does not see it, and nobody notices
            // until they look for it by name.
            var declared = (root.ContainsKey("families") ? root["families"] : null)
                             as Dictionary<string, object>;
            if (declared == null) Check("the catalog declares the families", false, "");
            else
            {
                var orfane = families.Where(f => !declared.ContainsKey(f)).ToList();
                Check("every family in use is declared", orfane.Count == 0,
                      orfane.Count == 0 ? declared.Count + " families"
                                        : "used and not declared: " + string.Join(", ", orfane));
            }

            // ── NO PATH OF THIS MACHINE ───────────────────────────────────
            //
            // The catalogue is read by a model, and will end up in a package that
            // goes elsewhere. A drive path from the maintainer's workstation inside a description is an
            // address that does not exist from that machine, and whoever reads has no
            // way of knowing it.
            // ── AND THE CHECK HAS ALREADY TRIGGERED FOR THE WRONG REASON ──
            //
            // The first version looked for «O:\» without distinguishing upper and
            // lower case, and found **«type:\n»**: two letters, the colon and
            // the backslash of a newline. A red that triggers for another reason
            // proves nothing and looks identical to one that works.
            //
            // A drive letter is a letter **not preceded by another
            // letter**: so «NO:\n» and «type:\n» are not one, and «S:\a» is.
            var text = File.ReadAllText(json);
            var bad = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(text, @"(?<![A-Za-z])[A-Za-z]:\\"))
                bad.Add(text.Substring(m.Index, Math.Min(40, text.Length - m.Index)));
            foreach (var p in new[] { "scratchpad", "\\\\\\\\" })
                if (text.IndexOf(p, StringComparison.Ordinal) >= 0) bad.Add(p);
            Check("no path of this machine in the catalog",
                  bad.Count == 0,
                  bad.Count == 0 ? "" : "found: " + string.Join(" | ", bad.Take(3)));

            EveryExecutableIsDeclared(catalogNames);
            TheCatalogHasNotFallenBehind();

            Similar();
        }

        // ================================================================
        // THE JSON IS A PRODUCT, AND A PRODUCT CAN BE OLD
        // ================================================================

        /// <summary>
        /// Does `catalog.json` still match the C# catalogue that generates it?
        ///
        /// IT COST A RUN ON 22/9/2026. The operation `note` was written,
        /// compiled, and the bridge refused it with «no
        /// operation «note» exists. Did you mean:
        /// drawing_dimension» — while it existed, it was in the dispatcher and it was
        /// inside the DLL the bridge had just loaded. It was missing only from the
        /// JSON, which is the only thing the bridge looks at to know what
        /// exists.
        ///
        /// And `Tools/update.md` said: *«`build.ps1` regenerates the file at
        /// every package, so it cannot fall behind unless somebody
        /// deletes that step»*. **It is not true with `-BridgeOnly`**, which is the
        /// way one builds while iterating. A guarantee written in a
        /// document is not a guarantee: it is a hope with a source.
        ///
        /// The tool to notice it was already there — `--check` exits 1 if
        /// it does not match, and says WHERE — and nobody called it.
        ///
        /// TWO PAIRS ARE LOOKED AT, and they are different defects:
        /// the C# against `Tools/catalog.json` (somebody touched the
        /// operations and did not export again), and `Tools/` against `bin/`
        /// (somebody exported again and did not rebuild the package, so the
        /// bridge still loads the old one — and it is exactly the case of
        /// today).
        /// </summary>
        private static void TheCatalogHasNotFallenBehind()
        {
            var tool = Path.Combine(Root(), "bin", "Oce.Solid.Agent.Catalog.exe");
            var source = Path.Combine(Root(), "Tools", "catalog.json");
            var package = Path.Combine(Root(), "bin", "catalog.json");

            if (!ProductFromBuild(tool))
            {
                if (!TheBridgeCouldBeBuilt())
                {
                    NotHere("the exported catalog matches the C#",
                           "SOLIDWORKS is not here: Oce.Solid.Agent.Catalog.exe depends on " +
                           "Oce.Solid.Agent.Core, which cannot be compiled without the interop.");
                    return;
                }
                Check("the catalog exporter is there", false,
                      "cannot find " + tool + " — produce it with .\\build.ps1");
                return;
            }

            // 1. THE C# AGAINST THE EXPORTED JSON.
            string said = "";
            int output = -1;
            try
            {
                var p = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo(tool,
                        "--check \"" + source + "\"")
                    {
                        WorkingDirectory = Root(),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };
                p.Start();
                said = (p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()).Trim();
                p.WaitForExit(30000);
                output = p.ExitCode;
            }
            catch (Exception ex)
            {
                Check("the exported catalog matches the C#", false, ex.Message);
                return;
            }

            // The useful line is the one that says WHERE they differ: a «they
            // are different» on seventy thousand characters sends you searching by eye.
            string line = said.Replace("\r", " ").Replace("\n", " ");
            if (line.Length > 160) line = line.Substring(0, 160) + "...";

            Check("the exported catalog matches the C#", output == 0,
                  output == 0 ? "Tools\\catalog.json is up to date"
                              : "does NOT match: regenerate it with "
                                + ".\\bin\\Oce.Solid.Agent.Catalog.exe Tools\\catalog.json — " + line);

            // 2. AND IS WHAT THE BRIDGE LOADS THE SAME?
            if (!File.Exists(package))
            {
                if (!TheBridgeCouldBeBuilt())
                {
                    NotHere("the bridge loads the current catalog",
                           "SOLIDWORKS is not here: bin\\catalog.json does not exist " +
                           "because build.ps1 could not run, and a file is compared " +
                           "with another file, not with its absence.");
                    return;
                }
                Check("the bridge loads the current catalog", false,
                      "cannot find " + package + " — produce it with .\\build.ps1");
                return;
            }

            string a = null, b = null;
            try { a = Fingerprint(source); b = Fingerprint(package); }
            catch (Exception ex)
            {
                Check("the bridge loads the current catalog", false, ex.Message);
                return;
            }

            Check("the bridge loads the current catalog",
                  string.Equals(a, b, StringComparison.Ordinal),
                  string.Equals(a, b, StringComparison.Ordinal)
                      ? "Tools\\ and bin\\ are the same file"
                      : "bin\\catalog.json has fallen behind Tools\\: "
                        + "run .\\build.ps1 again, which copies it over again. Until it is run, "
                        + "the bridge REFUSES the new operations saying that they do not exist");
        }

        private static string Fingerprint(string filePath)
        {
            using (var h = System.Security.Cryptography.MD5.Create())
            using (var f = File.OpenRead(filePath))
                return BitConverter.ToString(h.ComputeHash(f));
        }

        // ================================================================
        // WHAT IS EXECUTED AND WHAT IS SEEN
        // ================================================================

        /// <summary>
        /// Every operation the dispatcher can execute must be either in the
        /// catalogue, or in the list of the untested ones. In no other place.
        ///
        /// THE HOLE THIS CHECK CATCHES, measured on 22/9/2026.
        /// `ExecutableNames` counted **70** of them, `catalog.json` declared
        /// **68**, and the two that were missing — `center_marks` and
        /// `drawing_cut_list` — were out **for a good reason**
        /// (`Tested=false`: the APIs say yes and create nothing, 0 -> 0).
        ///
        /// But until here nobody compared the two lists, and the same
        /// difference would have been produced by a **forgotten** operation:
        /// written, working, never added to `Operations.cs`. That one would
        /// give no error anywhere — the bench would test it
        /// (it reads `ExecutableNames`), the catalogue would not mention it, and no
        /// model could call it. **The defect that gives no error**,
        /// applied to the only thing the model uses to know what exists.
        ///
        /// `ExecutableNames` IS READ FROM THE SOURCE, and not by reflection:
        /// that constant lives in `SwOperations`, which touches the Dassault
        /// interop — loading its type in here would mean demanding
        /// SOLIDWORKS in a run that must run without it. The untested ones instead
        /// are asked of `Export.NotTested()`, which is exactly what
        /// the exporter uses to decide: not a second copy of the rule.
        ///
        /// Reading ZERO of them is a failure, not a success: an empty list
        /// would make every comparison pass.
        /// </summary>
        private static void EveryExecutableIsDeclared(HashSet<string> inCatalog)
        {
            var source = Path.Combine(Root(), "Oce.Solid.Agent.Core", "SwOperations.cs");
            if (!File.Exists(source))
            {
                Check("the dispatcher source can be read", false, "cannot find " + source);
                return;
            }

            var text = File.ReadAllText(source);
            var block = System.Text.RegularExpressions.Regex.Match(
                text, @"ExecutableNames\s*=\s*\{(.*?)\};",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var executables = new HashSet<string>(StringComparer.Ordinal);
            if (block.Success)
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(block.Groups[1].Value, "\"([a-z0-9_]+)\""))
                    executables.Add(m.Groups[1].Value);

            Check("the list of executable operations can be read",
                  executables.Count >= 30,
                  executables.Count + " names in ExecutableNames");
            if (executables.Count < 30) return;

            // The untested ones, asked of whoever decides who stays out of the file.
            //
            // They are asked OF THE COMPILED LIBRARY, and there the machine counts: without
            // interop there is no DLL to interrogate. The count
            // «executable = in the catalogue + untested» cannot be done, and a
            // count that cannot be done is not pretended to have succeeded.
            if (!ProductFromBuild(Path.Combine(Root(), "bin", "Oce.Solid.Agent.Core.dll")) &&
                !TheBridgeCouldBeBuilt())
            {
                NotHere("every executable operation is in the catalog or declared untested",
                       "SOLIDWORKS is not here: the list of the untested ones is asked of " +
                       "Oce.Solid.Agent.Core.dll, which cannot be compiled without the interop.");
                return;
            }

            List<string> notTested;
            try
            {
                var bridge = Path.Combine(Root(), "bin", "oce-solid-agent-mcp.exe");
                Load.Bridge(bridge);   // installs the solver for bin\
                var dll = Path.Combine(Root(), "bin", "Oce.Solid.Agent.Core.dll");
                var library = Assembly.Load(File.ReadAllBytes(dll));
                var export = Load.Class(library, "Export");
                if (export == null)
                {
                    Check("the list of the untested ones is asked of the exporter", false,
                          "cannot find the Export class");
                    return;
                }
                notTested = (export.GetMethod("NotTested",
                                  BindingFlags.Public | BindingFlags.Static)
                                  ?.Invoke(null, null) as System.Collections.IEnumerable)
                             ?.Cast<object>().Select(Convert.ToString).ToList();
                if (notTested == null)
                {
                    Check("the list of the untested ones is asked of the exporter", false,
                          "NotTested() did not answer");
                    return;
                }
            }
            catch (Exception ex)
            {
                Check("the list of the untested ones is asked of the exporter", false, ex.Message);
                return;
            }

            var outside = new HashSet<string>(notTested, StringComparer.Ordinal);

            // 1. Executable and invisible: the defect this check exists to catch.
            var invisible = executables.Where(n => !inCatalog.Contains(n) && !outside.Contains(n))
                                       .OrderBy(n => n, StringComparer.Ordinal).ToList();
            Check("every executable operation is in the catalog or declared untested",
                  invisible.Count == 0,
                  invisible.Count == 0
                      ? executables.Count + " executable = " + inCatalog.Count +
                        " in the catalog + " + outside.Count + " untested"
                      : "executable and invisible to the model: " + string.Join(", ", invisible));

            // 2. The opposite direction: a catalogue entry that the dispatcher cannot execute
            //    is a promise that is found false only by calling it.
            var promises = inCatalog.Where(n => !executables.Contains(n))
                                      .OrderBy(n => n, StringComparer.Ordinal).ToList();
            Check("every operation of the catalog is really executable",
                  promises.Count == 0,
                  promises.Count == 0 ? "" : "in the catalog and not in the dispatcher: " +
                                             string.Join(", ", promises));

            // 3. And an untested one must not have ended up in the file all the same.
            var escaped = outside.Where(inCatalog.Contains)
                                .OrderBy(n => n, StringComparer.Ordinal).ToList();
            Check("no untested operation ended up in the catalog",
                  escaped.Count == 0,
                  escaped.Count == 0 ? outside.Count + " outside: " + string.Join(", ", outside)
                                      : "untested but exposed: " + string.Join(", ", escaped));
        }

        /// <summary>
        /// `Catalog.Similar()`: the case for which the function was rewritten.
        ///
        /// The first attempt looked for the whole word inside the name, and on
        /// «slotz» **it did not find «slot»** — that is, it failed exactly where
        /// it was needed: whoever gets a name wrong almost always has a close one in mind,
        /// and gets it wrong by one letter.
        /// </summary>
        private static void Similar()
        {
            // «NOT HERE» IF THE MACHINE CANNOT BUILD THE BRIDGE, RED IF IT CAN
            // AND NOBODY RAN build.ps1: the rule is in one place only.
            // Before there was a flat red here, and in CI (where the bridge is never
            // built) it kept the CI red on every push.
            var bridge = BridgeOrNotHere("Catalog.Similar: the names close to the wrong one");
            if (bridge == null) return;

            object catalog;
            MethodInfo similar;
            try
            {
                Environment.SetEnvironmentVariable(
                    "OCE_CATALOG", Path.Combine(Root(), "bin", "catalog.json"));
                // IT IS LOADED FROM THE BYTES: `LoadFrom` on a network drive answers
                // NotSupportedException, and this repository lives on S:.
                var asm = Load.Bridge(bridge);
                var typ = Load.Class(asm, "Catalog");
                if (typ == null) { Check("Catalog.Similar: the class is there", false, "Catalog missing"); return; }
                catalog = Activator.CreateInstance(typ, true);
                similar = typ.GetMethod("Similar");
                if (similar == null) { Check("Catalog.Similar: the method is there", false, ""); return; }
            }
            catch (Exception ex) { Check("Catalog.Similar: it loads", false, ex.ToString()); return; }

            Func<string, List<string>> di = n =>
                (similar.Invoke(catalog, new object[] { n }) as System.Collections.IEnumerable)
                    ?.Cast<object>().Select(Convert.ToString).ToList() ?? new List<string>();

            Check("«slots» finds «slot»", di("slots").Contains("slot"),
                  string.Join(", ", di("slots")));
            Check("«hole» finds «hole_wizard»", di("hole").Contains("hole_wizard"),
                  string.Join(", ", di("hole")));
            Check("an empty name does not blow up and does not invent", di("").Count == 0);
            Check("a name that resembles nothing does not invent",
                  di("qwertyuiop").Count == 0, string.Join(", ", di("qwertyuiop")));
            Check("it offers no more than five", di("a_slot_hole_cut_plane").Count <= 5,
                  di("a_slot_hole_cut_plane").Count + " offered");
        }

        // ================================================================
        // THE NUMBER WRITTEN IN THE README IS A MEASUREMENT, AND IT AGES LIKE ALL OF THEM
        //
        // On 16/9/2026 the README said «thirty-eight operations» in two places
        // while the catalogue had sixty. No check could
        // notice it: documents are not compiled, not executed, and
        // nobody rereads them until they are needed by someone who was not there.
        //
        // IT IS EXACTLY THE DEFECT THAT GIVES NO ERROR, applied to
        // documents: the README looks right, reads well, and says what is false.
        // Whoever reads it has no way of noticing — it is their only source.
        //
        // The check looks for only TWO forms, not all the numbers that appear:
        // «<number> operations» and «drawing operations are <number>». This way
        // «Five, not sixty» (which is about the TOOLS) does not make it
        // trigger: a check that shouts at a correct sentence gets learned to be
        // ignored, and that is worse than no check.
        // ================================================================
        private static void TheReadmeStatesTheRightNumber(int howMany)
        {
            var readme = Path.Combine(Root(), "README.md");
            if (!File.Exists(readme))
            {
                Check("the README exists", false, readme);
                return;
            }

            string text;
            try { text = File.ReadAllText(readme); }
            catch (Exception ex) { Check("the README can be read", false, ex.Message); return; }

            // THE READER READS ENGLISH, since W3.1-bis.11 (before it read Italian
            // number words too, while the README was being translated). Two
            // silences are red:
            //   * a word that LOOKS like a number and cannot be read (it used
            //     to be skipped by «if (n < 0) continue»);
            //   * a README in which no count can be read at all.
            // The forms stay closed: «N operations», «N drawing operations»
            // and «drawing operations are N». «Six assembly operations» still
            // does not count: the one qualifier allowed between number and
            // noun is «drawing».
            NumberReaderReadsEnglish();
            CountCheckSeenRed();

            int claims;
            var findings = ReadmeCountFindings(text, howMany, out claims);
            Check("the README says how many operations there are, and they are the real ones",
                  findings.Count == 0,
                  findings.Count == 0
                      ? "the catalogue has " + howMany + ", the README says so " + claims + " times"
                      : "the catalogue has " + howMany + "; " + string.Join("; ", findings));
            Check("the README states the number of operations in a form this check reads",
                  claims > 0,
                  claims > 0 ? claims + " counts read"
                             : "no «N operations» and no «drawing operations are N» in README.md: if the " +
                               "README stops saying how many there are, this check must be changed on purpose");
        }

        private const string EnglishNoun = "operations";

        // A word of the window: letters, digits and apostrophes, with inner
        // hyphens («ninety-nine»).
        private const string Word = @"[\p{L}\p{Nd}'’]+(?:-[\p{L}\p{Nd}'’]+)*";

        // Up to six words right before the noun: an English number takes up to
        // four («one hundred and sixty-four») and the qualifier one more. A
        // Markdown asterisk counts as a space.
        private static readonly Regex CountBefore = new Regex(
            @"(?<![\p{L}\p{Nd}'’-])(?<words>(?:" + Word + @"[\s*]+){1,6}?)(?<noun>" + EnglishNoun + @")\b",
            RegexOptions.IgnoreCase);

        private static readonly Regex CountAfter = new Regex(
            @"(?<noun>drawing\s+operations\s+are)[\s*]+(?<words>" + Word +
            @"(?:[\s*]+" + Word + @"){0,5})",
            RegexOptions.IgnoreCase);

        /// <summary>
        /// What the README says about the number of operations and is wrong
        /// or unreadable; `claims` is how many counts it could read.
        /// </summary>
        private static List<string> ReadmeCountFindings(string text, int actual, out int claims)
        {
            var findings = new List<string>();
            int read = 0;
            Action<Match, List<string>, bool> judge = (m, words, before) =>
            {
                // the longest run of words next to the noun that is a number
                for (int k = words.Count; k >= 1; k--)
                {
                    var candidate = before ? words.Skip(words.Count - k) : words.Take(k);
                    int n = Number(string.Join(" ", candidate));
                    if (n < 0) continue;
                    read++;
                    if (n != actual) findings.Add("«" + Flat(m.Value) + "» says " + n);
                    return;
                }
                var next = before ? words[words.Count - 1] : words[0];
                if (LooksLikeNumber(next))
                    findings.Add("«" + Flat(m.Value) + "»: «" + next + "» looks like a number and cannot be read");
            };

            foreach (Match m in CountBefore.Matches(text))
            {
                var words = Words(m.Groups["words"].Value);
                if (m.Groups["noun"].Value.Equals(EnglishNoun, StringComparison.OrdinalIgnoreCase) && words.Count > 1 &&
                    words[words.Count - 1].Equals("drawing", StringComparison.OrdinalIgnoreCase))
                    words.RemoveAt(words.Count - 1);
                if (words.Count > 0) judge(m, words, true);
            }
            foreach (Match m in CountAfter.Matches(text))
            {
                var words = Words(m.Groups["words"].Value);
                if (words.Count > 0) judge(m, words, false);
            }
            claims = read;
            return findings;
        }

        private static List<string> Words(string s)
        {
            return Regex.Split(s, @"[\s*]+").Where(w => w.Length > 0).ToList();
        }

        private static string Flat(string s)
        {
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        // ── THE SELF-TESTS ───────────────────────────────────────────────

        private static void NumberReaderReadsEnglish()
        {
            var cases = new[]
            {
                Tuple.Create("ninety-six", 96), Tuple.Create("ninety four", 94), Tuple.Create("twenty-one", 21),
                Tuple.Create("thirty-eight", 38), Tuple.Create("seventy-three", 73), Tuple.Create("thirteen", 13),
                Tuple.Create("one hundred and sixty-four", 164), Tuple.Create("a hundred", 100),
                Tuple.Create("two hundred", 200), Tuple.Create("99", 99),
                // not numbers: -1, and never a guess
                Tuple.Create("ninety-fuor", -1), Tuple.Create("thirty-eigt", -1),
                Tuple.Create("the", -1), Tuple.Create("drawing", -1), Tuple.Create("two ninety-nine", -1),
                Tuple.Create("hundred and", -1), Tuple.Create("zero hundred", -1),
            };
            var wrong = cases.Where(c => Number(c.Item1) != c.Item2)
                             .Select(c => c.Item1 + "=" + Number(c.Item1) + " (expected " + c.Item2 + ")").ToList();
            Check("the number reader reads English number words, and nothing else",
                  wrong.Count == 0,
                  wrong.Count == 0 ? cases.Length + " of " + cases.Length + " words read as expected" : string.Join("; ", wrong));
        }

        private static void CountCheckSeenRed()
        {
            var cases = new[]
            {
                // text, actual count, red expected
                Tuple.Create("It exposes ninety-six drawing operations as tools.", 94, true),
                Tuple.Create("It exposes ninety-four drawing operations as tools.", 94, false),
                Tuple.Create("Nine, not ninety-nine. The drawing operations are ninety-six, but", 94, true),
                Tuple.Create("The drawing operations are **ninety-four**, but", 94, false),
                Tuple.Create("There are one hundred and sixty-four operations.", 164, false),
                Tuple.Create("There are one hundred and sixty-four operations.", 64, true),
                Tuple.Create("The bench prints 99 operations in its header.", 94, true),
                Tuple.Create("exposes ninety-six\noperations as tools", 94, true),
                Tuple.Create("exposes ninety-four operations as tools", 94, false),
                // the silence that used to be green: a number word that cannot be read
                Tuple.Create("It exposes ninety-fuor drawing operations.", 94, true),
                Tuple.Create("It exposes fourty drawing operations.", 40, true),
                Tuple.Create("exposes two hundred and ninetty operations", 290, true),
                Tuple.Create("exposes sixtyteen operations", 160, true),
                // words that are not numbers stay silent
                Tuple.Create("the other operations, all operations, the drawing operations", 94, false),
                Tuple.Create("many operations and six assembly operations", 94, false),
            };
            var wrong = new List<string>();
            foreach (var c in cases)
            {
                int claims;
                bool red = ReadmeCountFindings(c.Item1, c.Item2, out claims).Count > 0;
                if (red != c.Item3) wrong.Add("«" + Flat(c.Item1) + "» against " + c.Item2 + ": " + (red ? "red" : "green"));
            }
            int none;
            bool blind = ReadmeCountFindings("A README that never says how many there are.", 94, out none).Count == 0 && none == 0;
            if (!blind) wrong.Add("a README without any count was not seen as one");
            Check("the README count check turns red on fakes («ninety-six operations» against 94, a number it cannot read)",
                  wrong.Count == 0,
                  wrong.Count == 0 ? (cases.Length + 1) + " fakes ended as expected" : string.Join("; ", wrong));
        }

        // ── THE NUMBER READER ────────────────────────────────────────────

        private static readonly string[] EnglishUnits =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
            "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen",
            "seventeen", "eighteen", "nineteen",
        };

        private static readonly string[] EnglishTens =
        {
            "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
        };

        /// <summary>
        /// A number written in words, in English from 0 to 999, or in digits.
        /// `-1` when the word is not a number, which is the normal case and not
        /// an error.
        /// </summary>
        private static int Number(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return -1;
            var p = word.Trim().ToLowerInvariant();
            if (Regex.IsMatch(p, @"^\d{1,4}$")) return int.Parse(p, CultureInfo.InvariantCulture);
            return EnglishNumber(p);
        }

        private static int EnglishNumber(string p)
        {
            var t = p.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            int i = 0, total = 0;
            bool hundreds = false;
            if (t.Length >= 2 && t[1] == "hundred")
            {
                int h = t[0] == "a" ? 1 : Array.IndexOf(EnglishUnits, t[0]);
                if (h < 1 || h > 9) return -1;
                total = h * 100;
                hundreds = true;
                i = 2;
                if (i < t.Length && t[i] == "and") { i++; if (i == t.Length) return -1; }
            }
            int left = t.Length - i;
            if (left == 0) return hundreds ? total : -1;
            if (left == 1)
            {
                int u = Array.IndexOf(EnglishUnits, t[i]);
                int d = Array.IndexOf(EnglishTens, t[i]);
                int v = u >= 0 ? u : d >= 0 ? (d + 2) * 10 : -1;
                if (v < 0 || (hundreds && v == 0)) return -1;
                return total + v;
            }
            if (left == 2)
            {
                int d = Array.IndexOf(EnglishTens, t[i]);
                int u = Array.IndexOf(EnglishUnits, t[i + 1]);
                if (d < 0 || u < 1 || u > 9) return -1;
                return total + (d + 2) * 10 + u;
            }
            return -1;
        }

        /// <summary>
        /// A word that is meant as a number even if the reader cannot read it:
        /// a digit, the root of a ten (at the start), an English «-teen», a
        /// hyphen joined to a number word, or one letter away from a number
        /// word (six letters or more, so that short words such as «the» are
        /// never taken for one). A count written in another way is the job of
        /// the second check above (no count at all is red).
        /// </summary>
        private static bool LooksLikeNumber(string word)
        {
            var w = word.ToLowerInvariant().Trim('\'');
            if (w.Any(char.IsDigit)) return true;
            var parts = w.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (EnglishTens.Any(r => part.StartsWith(r, StringComparison.Ordinal))) return true;
                if (part.EndsWith("teen", StringComparison.Ordinal) || part == "hundred" || part == "thousand") return true;
                if (parts.Length > 1 && (EnglishUnits.Contains(part) || EnglishTens.Contains(part))) return true;
                if (part.Length >= 6 && NumberWords.Any(n => NearlyEqual(part, n))) return true;
            }
            return false;
        }

        private static readonly string[] NumberWords = EnglishUnits.Concat(EnglishTens).Distinct().ToArray();

        /// <summary>One edit apart: a letter changed, added, dropped, or two next ones swapped.</summary>
        private static bool NearlyEqual(string a, string b)
        {
            if (a == b) return false;
            if (Math.Abs(a.Length - b.Length) > 1) return false;
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            return d[a.Length, b.Length] == 1;
        }
    }
}
