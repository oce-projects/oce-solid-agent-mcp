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
    /// `OPENPDM_CATALOGO`, which is the same variable that whoever installs
    /// the package elsewhere would use.
    ///
    /// None of this touches SOLIDWORKS: `Catalog` reads a JSON file and
    /// compares strings.
    /// </summary>
    internal static class CatalogTests
    {
        internal static void All()
        {
            Group("il catalogo");

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
                    Check("il catalogo c'e'", false,
                          "non trovo " + json + " — si produce con .\\build.ps1");
                    return;
                }
            }
            Check("il catalogo c'e'", true,
                  fromRepository
                      ? json + " (quello in repository: qui SOLIDWORKS non c'e', " +
                        "quindi bin\\catalog.json non si poteva produrre)"
                      : json);

            Dictionary<string, object> root;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                s.MaxJsonLength = 40 * 1024 * 1024;
                root = s.DeserializeObject(File.ReadAllText(json)) as Dictionary<string, object>;
            }
            catch (Exception ex) { Check("il catalogo si legge", false, ex.Message); return; }

            var operations = (root != null && root.ContainsKey("operations")
                ? root["operations"] : null) as object[];
            if (operations == null) { Check("il catalogo ha «operations»", false, ""); return; }
            Check("il catalogo ha almeno trenta operazioni", operations.Length >= 30,
                  operations.Length + " operazioni");

            TheReadmeStatesTheRightNumber(operations.Length);

            // ── EVERY OPERATION HAS THE FIELDS THAT ARE NEEDED ────────────
            var without = new List<string>();
            var families = new HashSet<string>(StringComparer.Ordinal);
            var catalogNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in operations)
            {
                var v = o as Dictionary<string, object>;
                if (v == null) { without.Add("(una voce non e' un oggetto)"); continue; }
                string itemName = v.ContainsKey("name") ? Convert.ToString(v["name"]) : null;
                if (itemName != null) catalogNames.Add(itemName);
                foreach (var theField in new[] { "name", "family", "summary", "details", "schema" })
                    if (!v.ContainsKey(theField) || v[theField] == null)
                        without.Add((itemName ?? "?") + " senza «" + theField + "»");
                if (v.ContainsKey("family")) families.Add(Convert.ToString(v["family"]));
            }
            Check("ogni operazione ha nome, famiglia, sommario, descrizione, schema",
                  without.Count == 0, string.Join("; ", without.Take(4)));

            // ── THE DECLARED FAMILIES ARE THE USED ONES ───────────────────
            //
            // A family that is used and not declared is an operation that does not
            // appear in the menu: the model does not see it, and nobody notices
            // until they look for it by name.
            var declared = (root.ContainsKey("families") ? root["families"] : null)
                             as Dictionary<string, object>;
            if (declared == null) Check("il catalogo dichiara le famiglie", false, "");
            else
            {
                var orfane = families.Where(f => !declared.ContainsKey(f)).ToList();
                Check("ogni famiglia usata e' dichiarata", orfane.Count == 0,
                      orfane.Count == 0 ? declared.Count + " famiglie"
                                        : "usate e non dichiarate: " + string.Join(", ", orfane));
            }

            // ── NO PATH OF THIS MACHINE ───────────────────────────────────
            //
            // The catalogue is read by a model, and will end up in a package that
            // goes elsewhere. An «S:\PDM\...» inside a description is an
            // address that does not exist from that machine, and whoever reads has no
            // way of knowing it.
            // ── AND THE CHECK HAS ALREADY TRIGGERED FOR THE WRONG REASON ──
            //
            // The first version looked for «O:\» without distinguishing upper and
            // lower case, and found **«tipo:\n»**: two letters, the colon and
            // the backslash of a newline. A red that triggers for another reason
            // proves nothing and looks identical to one that works.
            //
            // A drive letter is a letter **not preceded by another
            // letter**: so «NO:\n» and «tipo:\n» are not one, and «S:\PDM» is.
            var text = File.ReadAllText(json);
            var bad = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(text, @"(?<![A-Za-z])[A-Za-z]:\\"))
                bad.Add(text.Substring(m.Index, Math.Min(40, text.Length - m.Index)));
            foreach (var p in new[] { "scratchpad", "\\\\\\\\" })
                if (text.IndexOf(p, StringComparison.Ordinal) >= 0) bad.Add(p);
            Check("nessun percorso di questa macchina nel catalogo",
                  bad.Count == 0,
                  bad.Count == 0 ? "" : "trovati: " + string.Join(" | ", bad.Take(3)));

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
        /// compiled, and the bridge refused it with «non
        /// esiste nessuna operazione «note». Forse cercavi:
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
        /// The tool to notice it was already there — `--controlla` exits 1 if
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
                    NotHere("il catalogo esportato combacia col C#",
                           "qui SOLIDWORKS non c'e': Oce.Solid.Agent.Catalog.exe dipende da " +
                           "Oce.Solid.Agent.Core, che senza interop non si compila.");
                    return;
                }
                Check("l'esportatore del catalogo c'e'", false,
                      "non trovo " + tool + " — si produce con .\\build.ps1");
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
                        "--controlla \"" + source + "\"")
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
                Check("il catalogo esportato combacia col C#", false, ex.Message);
                return;
            }

            // The useful line is the one that says WHERE they differ: a «they
            // are different» on seventy thousand characters sends you searching by eye.
            string line = said.Replace("\r", " ").Replace("\n", " ");
            if (line.Length > 160) line = line.Substring(0, 160) + "...";

            Check("il catalogo esportato combacia col C#", output == 0,
                  output == 0 ? "Tools\\catalog.json e' aggiornato"
                              : "NON combacia: si rigenera con "
                                + ".\\bin\\Oce.Solid.Agent.Catalog.exe Tools\\catalog.json — " + line);

            // 2. AND IS WHAT THE BRIDGE LOADS THE SAME?
            if (!File.Exists(package))
            {
                if (!TheBridgeCouldBeBuilt())
                {
                    NotHere("il ponte carica il catalogo di adesso",
                           "qui SOLIDWORKS non c'e': bin\\catalog.json non esiste " +
                           "perche' build.ps1 non poteva girare, e un file si confronta " +
                           "con un altro file, non con la sua assenza.");
                    return;
                }
                Check("il ponte carica il catalogo di adesso", false,
                      "non trovo " + package + " — si produce con .\\build.ps1");
                return;
            }

            string a = null, b = null;
            try { a = Fingerprint(source); b = Fingerprint(package); }
            catch (Exception ex)
            {
                Check("il ponte carica il catalogo di adesso", false, ex.Message);
                return;
            }

            Check("il ponte carica il catalogo di adesso",
                  string.Equals(a, b, StringComparison.Ordinal),
                  string.Equals(a, b, StringComparison.Ordinal)
                      ? "Tools\\ e bin\\ sono lo stesso file"
                      : "bin\\catalog.json e' rimasto indietro rispetto a Tools\\: "
                        + "si rilancia .\\build.ps1, che lo ricopia. Finche' non lo si fa, "
                        + "il ponte RIFIUTA le operazioni nuove dicendo che non esistono");
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
        /// (`Provata=false`: the APIs say yes and create nothing, 0 -> 0).
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
                Check("il sorgente del dispatcher si legge", false, "non trovo " + source);
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

            Check("l'elenco delle operazioni eseguibili si legge",
                  executables.Count >= 30,
                  executables.Count + " nomi in ExecutableNames");
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
                NotHere("ogni operazione eseguibile e' nel catalogo o dichiarata non provata",
                       "qui SOLIDWORKS non c'e': l'elenco delle non provate si chiede a " +
                       "Oce.Solid.Agent.Core.dll, che senza interop non si compila.");
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
                    Check("l'elenco delle non provate si chiede all'esportatore", false,
                          "non trovo la classe Export");
                    return;
                }
                notTested = (export.GetMethod("NotTested",
                                  BindingFlags.Public | BindingFlags.Static)
                                  ?.Invoke(null, null) as System.Collections.IEnumerable)
                             ?.Cast<object>().Select(Convert.ToString).ToList();
                if (notTested == null)
                {
                    Check("l'elenco delle non provate si chiede all'esportatore", false,
                          "NotTested() non ha risposto");
                    return;
                }
            }
            catch (Exception ex)
            {
                Check("l'elenco delle non provate si chiede all'esportatore", false, ex.Message);
                return;
            }

            var outside = new HashSet<string>(notTested, StringComparer.Ordinal);

            // 1. Executable and invisible: the defect this check exists to catch.
            var invisible = executables.Where(n => !inCatalog.Contains(n) && !outside.Contains(n))
                                       .OrderBy(n => n, StringComparer.Ordinal).ToList();
            Check("ogni operazione eseguibile e' nel catalogo o dichiarata non provata",
                  invisible.Count == 0,
                  invisible.Count == 0
                      ? executables.Count + " eseguibili = " + inCatalog.Count +
                        " nel catalogo + " + outside.Count + " non provate"
                      : "eseguibili e invisibili al modello: " + string.Join(", ", invisible));

            // 2. The opposite direction: a catalogue entry that the dispatcher cannot execute
            //    is a promise that is found false only by calling it.
            var promises = inCatalog.Where(n => !executables.Contains(n))
                                      .OrderBy(n => n, StringComparer.Ordinal).ToList();
            Check("ogni operazione del catalogo e' davvero eseguibile",
                  promises.Count == 0,
                  promises.Count == 0 ? "" : "nel catalogo e non nel dispatcher: " +
                                             string.Join(", ", promises));

            // 3. And an untested one must not have ended up in the file all the same.
            var escaped = outside.Where(inCatalog.Contains)
                                .OrderBy(n => n, StringComparer.Ordinal).ToList();
            Check("nessuna operazione non provata e' finita nel catalogo",
                  escaped.Count == 0,
                  escaped.Count == 0 ? outside.Count + " fuori: " + string.Join(", ", outside)
                                      : "non provate ma esposte: " + string.Join(", ", escaped));
        }

        /// <summary>
        /// `Catalog.Similar()`: the case for which the function was rewritten.
        ///
        /// The first attempt looked for the whole word inside the name, and on
        /// «asole» **it did not find «slot»** — that is, it failed exactly where
        /// it was needed: whoever gets a name wrong almost always has a close one in mind,
        /// and gets it wrong by one letter.
        /// </summary>
        private static void Similar()
        {
            // «NOT HERE» IF THE MACHINE CANNOT BUILD THE BRIDGE, RED IF IT CAN
            // AND NOBODY RAN build.ps1: the rule is in one place only.
            // Before there was a flat red here, and in CI (where the bridge is never
            // built) it kept the CI red on every push.
            var bridge = BridgeOrNotHere("Catalog.Similar: i nomi vicini a quello sbagliato");
            if (bridge == null) return;

            object catalog;
            MethodInfo similar;
            try
            {
                Environment.SetEnvironmentVariable(
                    "OPENPDM_CATALOGO", Path.Combine(Root(), "bin", "catalog.json"));
                // IT IS LOADED FROM THE BYTES: `LoadFrom` on a network drive answers
                // NotSupportedException, and this repository lives on S:.
                var asm = Load.Bridge(bridge);
                var typ = Load.Class(asm, "Catalog");
                if (typ == null) { Check("Catalog.Similar: la classe c'e'", false, "Catalog assente"); return; }
                catalog = Activator.CreateInstance(typ, true);
                similar = typ.GetMethod("Similar");
                if (similar == null) { Check("Catalog.Similar: il metodo c'e'", false, ""); return; }
            }
            catch (Exception ex) { Check("Catalog.Similar: si carica", false, ex.ToString()); return; }

            Func<string, List<string>> di = n =>
                (similar.Invoke(catalog, new object[] { n }) as System.Collections.IEnumerable)
                    ?.Cast<object>().Select(Convert.ToString).ToList() ?? new List<string>();

            Check("«slots» trova «slot»", di("slots").Contains("slot"),
                  string.Join(", ", di("slots")));
            Check("«hole» trova «hole_wizard»", di("hole").Contains("hole_wizard"),
                  string.Join(", ", di("hole")));
            Check("un nome vuoto non esplode e non inventa", di("").Count == 0);
            Check("un nome che non somiglia a niente non inventa",
                  di("qwertyuiop").Count == 0, string.Join(", ", di("qwertyuiop")));
            Check("non ne offre piu' di cinque", di("a_asola_foro_taglio_piano").Count <= 5,
                  di("a_asola_foro_taglio_piano").Count + " offerti");
        }

        // ================================================================
        // THE NUMBER WRITTEN IN THE README IS A MEASUREMENT, AND IT AGES LIKE ALL OF THEM
        //
        // On 16/9/2026 the README said «trentotto operazioni» in two places
        // while the catalogue had sixty. No check could
        // notice it: documents are not compiled, not executed, and
        // nobody rereads them until they are needed by someone who was not there.
        //
        // IT IS EXACTLY THE DEFECT THAT GIVES NO ERROR, applied to
        // documents: the README looks right, reads well, and says what is false.
        // Whoever reads it has no way of noticing — it is their only source.
        //
        // The check looks for only TWO forms, not all the numbers that appear:
        // «<numero> operazioni» and «operazioni di disegno sono <numero>». This way
        // «Cinque, non sessanta» (which is about the TOOLS) does not make it
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

            // BILINGUAL SINCE W3.1-bis.2 (30/9/2026). The README is going to be
            // translated, and a reader that knew only Italian number words
            // would read «ninety-nine drawing operations» as no number at all:
            // the check would have stopped protecting without failing. So the
            // reader knows English too, and two silences are red now:
            //   * a word that LOOKS like a number and cannot be read (it used
            //     to be skipped by «if (n < 0) continue»);
            //   * a README in which no count can be read at all.
            // The forms stay closed: «N operations», «N drawing operations»
            // and «drawing operations are N» in English, and the two Italian
            // ones above. «Six assembly operations» still does not count: the
            // one English qualifier allowed between number and noun is
            // «drawing».
            NumberReaderReadsBothLanguages();
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

        private const string ItalianNoun = "operazioni";
        private const string EnglishNoun = "operations";

        // A word of the window: letters, digits and apostrophes, with inner
        // hyphens («ninety-nine»).
        private const string Word = @"[\p{L}\p{Nd}'’]+(?:-[\p{L}\p{Nd}'’]+)*";

        // Up to six words right before the noun: an English number takes up to
        // four («one hundred and sixty-four») and the qualifier one more. A
        // Markdown asterisk counts as a space.
        private static readonly Regex CountBefore = new Regex(
            @"(?<![\p{L}\p{Nd}'’-])(?<words>(?:" + Word + @"[\s*]+){1,6}?)(?<noun>" + ItalianNoun + "|" + EnglishNoun + @")\b",
            RegexOptions.IgnoreCase);

        private static readonly Regex CountAfter = new Regex(
            @"(?<noun>" + ItalianNoun + @"\s+di\s+disegno\s+sono|drawing\s+operations\s+are)[\s*]+(?<words>" + Word +
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

        private static void NumberReaderReadsBothLanguages()
        {
            // The Italian words are built from the reader's own lists: the
            // Italian branch is transitional, and its tests add no Italian to
            // the repository.
            string it73accent = "settantatré";
            string it73apostrophe = ItalianTens[5] + ItalianUnits[3] + "'";
            string it38 = ItalianTens[1].Substring(0, ItalianTens[1].Length - 1) + ItalianUnits[8];
            string it21 = ItalianTens[0].Substring(0, ItalianTens[0].Length - 1) + ItalianUnits[1];
            string it94 = ItalianTens[7] + ItalianUnits[4];
            string glued = ItalianUnits[2] + ItalianTens[4]; // a unit glued to a ten: not a number
            var cases = new[]
            {
                Tuple.Create(it73accent, 73), Tuple.Create(it73apostrophe, 73), Tuple.Create(it38, 38),
                Tuple.Create(it21, 21), Tuple.Create(it94, 94), Tuple.Create(glued, -1),
                Tuple.Create("ninety-six", 96), Tuple.Create("ninety four", 94), Tuple.Create("twenty-one", 21),
                Tuple.Create("thirty-eight", 38), Tuple.Create("seventy-three", 73), Tuple.Create("thirteen", 13),
                Tuple.Create("one hundred and sixty-four", 164), Tuple.Create("a hundred", 100),
                Tuple.Create("two hundred", 200), Tuple.Create("99", 99),
                // not numbers: -1, and never a guess
                Tuple.Create("ninety-fuor", -1), Tuple.Create(it38.Remove(it38.Length - 2, 1), -1),
                Tuple.Create("the", -1), Tuple.Create("drawing", -1), Tuple.Create("two ninety-nine", -1),
                Tuple.Create("hundred and", -1), Tuple.Create("zero hundred", -1),
            };
            var wrong = cases.Where(c => Number(c.Item1) != c.Item2)
                             .Select(c => c.Item1 + "=" + Number(c.Item1) + " (expected " + c.Item2 + ")").ToList();
            Check("the number reader reads Italian and English number words, and nothing else",
                  wrong.Count == 0,
                  wrong.Count == 0 ? cases.Length + " of " + cases.Length + " words read as expected" : string.Join("; ", wrong));
        }

        private static void CountCheckSeenRed()
        {
            string it94 = ItalianTens[7] + ItalianUnits[4];
            string it96 = ItalianTens[7] + ItalianUnits[6];
            string it94misspelled = it94.Remove(it94.Length - 3, 1);
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
                Tuple.Create("exposes " + it96 + "\n" + ItalianNoun + " as tools", 94, true),
                Tuple.Create("exposes " + it94 + " " + ItalianNoun + " as tools", 94, false),
                // the silence that used to be green: a number word that cannot be read
                Tuple.Create("exposes " + it94misspelled + " " + ItalianNoun + " as tools", 94, true),
                Tuple.Create("It exposes ninety-fuor drawing operations.", 94, true),
                Tuple.Create("It exposes fourty drawing operations.", 40, true),
                Tuple.Create("exposes " + ItalianUnits[2] + ItalianTens[4] + " " + ItalianNoun, 260, true),
                Tuple.Create("exposes ultra" + ItalianTens[4] + " " + ItalianNoun, 160, true),
                // words that are not numbers stay silent
                Tuple.Create("the other operations, all operations, the drawing operations", 94, false),
                Tuple.Create("many " + ItalianNoun + " and six assembly operations", 94, false),
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

        private static readonly string[] ItalianUnits =
        {
            "zero", "uno", "due", "tre", "quattro", "cinque", "sei", "sette",
            "otto", "nove", "dieci", "undici", "dodici", "tredici",
            "quattordici", "quindici", "sedici", "diciassette", "diciotto",
            "diciannove",
        };

        private static readonly string[] ItalianTens =
        {
            "venti", "trenta", "quaranta", "cinquanta", "sessanta",
            "settanta", "ottanta", "novanta",
        };

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
        /// A number written in words, in Italian from 0 to 99 as before and in
        /// English from 0 to 999, or in digits. `-1` when the word is not a
        /// number, which is the normal case and not an error. The Italian
        /// hundreds are left out on purpose: the Italian branch only has to
        /// last until the README is translated, and a hundred written in
        /// Italian words is red below (it looks like a number and cannot be
        /// read), not silent.
        /// </summary>
        private static int Number(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return -1;
            var p = word.Trim().ToLowerInvariant().Replace('’', '\'');
            if (Regex.IsMatch(p, @"^\d{1,4}$")) return int.Parse(p, CultureInfo.InvariantCulture);
            int n = ItalianNumber(p);
            return n >= 0 ? n : EnglishNumber(p);
        }

        private static int ItalianNumber(string p)
        {
            if (p.IndexOf(' ') >= 0 || p.IndexOf('-') >= 0) return -1;

            // A COMPOUND «TRE» IS WRITTEN WITH AN ACCENT: without this the
            // number written properly came back as «not a number», and the
            // README check kept QUIET. On 25/9/2026 the README was about to go
            // from seventy-four to seventy-three, and a wrong seventy-three
            // would have gone through. This repository also writes the accent
            // as a vowel and an apostrophe, so that form is read too.
            if (p.Length > 4 && p.EndsWith(ItalianUnits[3] + "'", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 1);
            else if (p.Length > 3 && p.EndsWith("tré", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 1) + "e";
            if (p.IndexOf('\'') >= 0) return -1;

            return ItalianBelowHundred(p);
        }

        private static int ItalianBelowHundred(string p)
        {
            for (int i = 0; i < ItalianUnits.Length; i++)
                if (p == ItalianUnits[i]) return i;
            for (int d = 0; d < ItalianTens.Length; d++)
            {
                int value = (d + 2) * 10;
                if (p == ItalianTens[d]) return value;

                // THE ELISION. Before «uno» and «otto» the tens lose their last
                // vowel: without this the thirty-eight that gave birth to the
                // check would not even be read.
                var root = ItalianTens[d].Substring(0, ItalianTens[d].Length - 1);
                for (int u = 1; u <= 9; u++)
                {
                    bool elides = u == 1 || u == 8;
                    if (p == (elides ? root : ItalianTens[d]) + ItalianUnits[u]) return value + u;
                }
            }
            return -1;
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
        /// a digit, the root of a ten (at the start, or inside a longer Italian
        /// word), an Italian unit glued to more letters, an English «-teen», a
        /// hyphen joined to a number word, or one letter away from a number
        /// word (six letters or more, so that short words such as «the» are
        /// never taken for one). What it misses, measured: an Italian hundred
        /// with nothing of a ten in it («a hundred and five» in Italian words);
        /// the README says those in English or in digits.
        /// </summary>
        private static bool LooksLikeNumber(string word)
        {
            var w = word.ToLowerInvariant().Replace('’', '\'').Trim('\'');
            if (w.Any(char.IsDigit)) return true;
            var parts = w.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (NumberRoots.Any(r => part.StartsWith(r, StringComparison.Ordinal))) return true;
                // inside a longer Italian word, the tens that English never
                // writes («vent» is in «event», «quarant» in «quarantine»)
                if (InnerRoots.Any(r => part.IndexOf(r, 1, StringComparison.Ordinal) > 0)) return true;
                // a unit glued to more letters: the Italian hundreds
                if (part.Length >= 6 && ItalianUnits.Skip(2).Take(8).Any(u => part.StartsWith(u, StringComparison.Ordinal))) return true;
                if (part.EndsWith("teen", StringComparison.Ordinal) || part == "hundred" || part == "thousand") return true;
                if (parts.Length > 1 && (EnglishUnits.Contains(part) || EnglishTens.Contains(part))) return true;
                if (part.Length >= 6 && NumberWords.Any(n => NearlyEqual(part, n))) return true;
            }
            return false;
        }

        // The roots of the tens and of the teens, in both languages.
        private static readonly string[] NumberRoots =
            ItalianTens.Select(t => t.Substring(0, t.Length - 1))
                       .Concat(ItalianUnits.Skip(11).Select(u => u.Substring(0, 5)))
                       .Concat(EnglishTens)
                       .Distinct().ToArray();

        // thirty, fifty, sixty, seventy, eighty, ninety: twenty and forty are
        // inside English words
        private static readonly string[] InnerRoots =
            new[] { 1, 3, 4, 5, 6, 7 }.Select(i => ItalianTens[i].Substring(0, ItalianTens[i].Length - 1)).ToArray();

        private static readonly string[] NumberWords =
            Enumerable.Range(0, 100).Select(ItalianWord).Concat(EnglishUnits).Concat(EnglishTens).Distinct().ToArray();

        private static string ItalianWord(int n)
        {
            if (n < 20) return ItalianUnits[n];
            var tens = ItalianTens[n / 10 - 2];
            int u = n % 10;
            if (u == 0) return tens;
            return (u == 1 || u == 8 ? tens.Substring(0, tens.Length - 1) : tens) + ItalianUnits[u];
        }

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
