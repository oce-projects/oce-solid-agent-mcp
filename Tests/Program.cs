using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// THE CHECKS THAT RUN ON ANY MACHINE (entry F7).
    ///
    /// ── WHY THEY EXIST ──────────────────────────────────────────────────────
    ///
    /// They are the only checks that run **without a licence, without CAD, in
    /// seconds**. The real bench opens SOLIDWORKS and lasts a quarter of an hour: it is
    /// fine the evening before committing, and it is of no use in a CI
    /// or on the machine of whoever opens this repository for the first time.
    ///
    /// And the evaluator of the expectations **must** live here: if it is broken, the whole
    /// suite with SOLIDWORKS becomes green for free.
    ///
    /// ── WHAT IT TESTS ────────────────────────────────────────────────────────
    ///
    ///   * the **catalogue**: fields, families, no path of this machine
    ///     inside the descriptions, and `Similar()` which on a name wrong by one letter finds «slot»;
    ///   * `Session.SameDocument()` — the guard against the extruded block
    ///     in somebody else's part, which until today had no check;
    ///   * the **MCP protocol**, talking to the bridge on stdin/stdout as
    ///     a client would;
    ///   * the **evaluator of the expectations**, against fake states written by hand.
    ///
    /// ── AND IT WRITES `results.xml` ────────────────────────────────────────
    ///
    /// In JUnit format, which is what every CI knows how to read without
    /// anybody teaching it anything.
    /// </summary>
    internal static class Program
    {
        internal static int FailedOnes;

        /// <summary>
        /// How many checks could NOT run on this machine.
        /// They are neither green nor red: see <see cref="NotHere"/>.
        /// </summary>
        internal static int NotRunnableHere;

        internal static readonly List<Outcome> Outcomes = new List<Outcome>();

        internal sealed class Outcome
        {
            public string Group;
            public string Name;
            public bool Ok;
            public bool NotHere;
            public string Detail;
        }

        private static string _group = "general";

        internal static void Group(string itemName)
        {
            _group = itemName;
            Console.WriteLine();
            Console.WriteLine("=== " + itemName + " ===");
        }

        internal static void Check(string itemName, bool ok, string detail = "")
        {
            if (!ok) FailedOnes++;
            Outcomes.Add(new Outcome { Group = _group, Name = itemName, Ok = ok, Detail = detail });
            // A GREEN IS SHORTENED, A RED IS NOT. The detail of a red
            // check is the only thing that is really read, and truncating it to
            // a hundred and ten characters means running again to see the rest.
            Console.WriteLine((ok ? "  OK   " : "  NO   ") + itemName.PadRight(58) +
                              (ok ? Truncated(detail) : detail));
        }

        /// <summary>
        /// A CHECK THAT **COULD NOT** RUN HERE IS NEITHER GREEN NOR RED.
        ///
        /// ── THE DEFECT THAT GAVE BIRTH TO THIS THIRD ENTRY ────────────────
        ///
        /// 23/9/2026, entry F9-bis. The CI of this repository NEVER
        /// turned green, and the reason was not a defect of the code: its
        /// first step was `msbuild Oce.Solid.Agent.csproj`, and that project
        /// references `SolidWorks.Interop.*` with a `HintPath` on the SOLIDWORKS
        /// installation. On a GitHub runner that folder is not there and will not
        /// be. **Measured, not deduced**: building with
        /// `/p:SwInstallPath=Z:\does-not-exist` two hundred errors come out, and among them
        /// `CS0246: the type name IModelDoc2 could not be found`.
        ///
        /// The README meanwhile said «the CI builds, runs the contract
        /// checks and refuses a delivery in which nobody ran the
        /// bench». The last two were true in theory and unreachable in
        /// practice, because the first never got to the end. **A CI that
        /// has never run is a badge, not a proof.**
        ///
        /// ── WHY SKIPPING IS NOT ENOUGH ───────────────────────────────────
        ///
        /// The temptation is `if (!File.Exists(bridge)) return;`: the suite
        /// becomes green and nobody knows any more that half of the checks was not
        /// done. It is the defect that this repository reproaches
        /// elsewhere, applied to its own CI.
        ///
        /// So: the entry is there all the same, `--` is printed, it is counted separately, and
        /// it ends up in `results.xml` as `<skipped>` — which every CI knows how to show.
        ///
        /// ── AND «COULD NOT» IS PROVED, NOT DECLARED ────────────────────────
        ///
        /// The distinction that counts is between **cannot be done here** and **you
        /// forgot to build**. They give the same symptom — the executable
        /// is not there — and they are two opposite defects. They are separated by looking at
        /// the interop: if it is not on the machine, the bridge COULD NOT exist;
        /// if it is, the executable is missing because somebody did not run
        /// `build.ps1`, and that **stays red**.
        /// </summary>
        internal static void NotHere(string itemName, string why)
        {
            NotRunnableHere++;
            Outcomes.Add(new Outcome { Group = _group, Name = itemName, Ok = true,
                                  NotHere = true, Detail = why });
            Console.WriteLine("  --   " + itemName.PadRight(58) + why);
        }

        /// <summary>
        /// Where the Dassault interop is on THIS machine, or `null`.
        ///
        /// Same order as the `.csproj` files — `SW_INSTALL_PATH`, then `SW_REDIST`,
        /// then the default location — because the question is exactly «would msbuild
        /// have found it?», and looking for it in an order different from that
        /// of the build would answer another question.
        /// </summary>
        /// <summary>
        /// «Behave like a runner without SOLIDWORKS», here and now.
        ///
        /// The «not runnable here» entries would be seen only by a bare machine, and
        /// this one is not: without this switch the code that writes them
        /// would stay untested until the first CI run — that is, one would
        /// have to believe it works. **A check never seen triggering is not
        /// a check.**
        ///
        /// It is not a shortcut to make something pass: under simulation the
        /// summary shouts it and the skipped ones are counted all the same, so using it
        /// to «become green» would make the hole more visible, not less.
        /// </summary>
        internal static bool PretendWithoutInterop
        {
            get { return Environment.GetEnvironmentVariable("OCE_PRETEND_NO_INTEROP") == "1"; }
        }

        /// <summary>
        /// A file that `build.ps1` produces, and which therefore cannot exist on a machine
        /// without the interop. Under simulation it never exists.
        /// </summary>
        internal static bool ProductFromBuild(string filePath)
        {
            return !PretendWithoutInterop && File.Exists(filePath);
        }

        internal static string WhereTheInteropIs()
        {
            if (PretendWithoutInterop) return null;
            var candidates = new List<string>();
            var swRoot = Environment.GetEnvironmentVariable("SW_INSTALL_PATH");
            if (!string.IsNullOrEmpty(swRoot))
                candidates.Add(Path.Combine(swRoot, "api", "redist"));
            var redist = Environment.GetEnvironmentVariable("SW_REDIST");
            if (!string.IsNullOrEmpty(redist)) candidates.Add(redist);
            candidates.Add(@"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist");
            foreach (var c in candidates)
            {
                try
                {
                    if (File.Exists(Path.Combine(c, "SolidWorks.Interop.sldworks.dll")))
                        return c;
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// True if on this machine the bridge COULD HAVE been built.
        /// It does not say that it was built: it says that, if it is missing, the fault is
        /// of whoever did not run `build.ps1` and not of the machine.
        /// </summary>
        internal static bool TheBridgeCouldBeBuilt()
        {
            return WhereTheInteropIs() != null;
        }

        /// <summary>
        /// The executable of the bridge, or a «not here» entry and `null`.
        ///
        /// Three groups did the same thing in three slightly different ways
        /// (`ProtocolTests`, `SessionTests`, `CatalogTests`): the rule on
        /// when a missing bridge is the machine's fault was written three
        /// times, and it is the way in which two of the three fall behind.
        /// </summary>
        internal static string BridgeOrNotHere(string whatDoesNotRun)
        {
            var bridge = Path.Combine(Root(), "bin", "oce-solid-agent-mcp.exe");
            if (ProductFromBuild(bridge)) return bridge;
            if (!TheBridgeCouldBeBuilt())
            {
                NotHere(whatDoesNotRun,
                       "SOLIDWORKS is not here: without api\\redist\\SolidWorks.Interop.* the " +
                       "bridge cannot be compiled, so there is no executable to " +
                       "talk to. It is not a defect of the bridge, and pretending to have proven it " +
                       "would be worse than not having proven it.");
                return null;
            }
            Check("the bridge is there", false,
                  "cannot find " + bridge + " — but SOLIDWORKS is on this machine (" +
                  WhereTheInteropIs() + "), so only .\\build.ps1 is missing");
            return null;
        }

        /// <summary>
        /// The catalogue that the bridge serves: `bin\catalog.json`, and if it is not there the
        /// versioned copy `Tools\catalog.json` — which is exactly the
        /// file that `build.ps1` regenerates and copies into `bin\`.
        ///
        /// FOUR CHECKS HAD KEPT THE CI RED FOR FIVE PUSHES (measured on
        /// 25/9/2026, from 0.85.0 to 0.88.0): they looked only in `bin\`, which in
        /// a clean clone does not exist, and said «the served catalogue is
        /// not there». It was not a defect of the catalogue, nor something the CI
        /// could not do: the file to read was there, in `Tools\`.
        /// `DescriptionsTests` already did it right. *An always-red CI gets learned
        /// not to be looked at, and it is the way in which a real red goes through.*
        /// </summary>
        internal static string ServedCatalog()
        {
            var inBin = Path.Combine(Root(), "bin", "catalog.json");
            if (File.Exists(inBin)) return inBin;
            return Path.Combine(Root(), "Tools", "catalog.json");
        }

        internal static string Truncated(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 110 ? s.Substring(0, 110) + "..." : s;
        }

        /// <summary>The root of the repository, two levels above `Tests\bin\`.</summary>
        internal static string Root()
        {
            var bin = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            return Directory.GetParent(Directory.GetParent(bin).FullName).FullName;
        }

        /// <summary>
        /// The version of the product, read from `VERSION.txt` in the repository.
        ///
        /// Null if it cannot be read: whoever uses it must be able to say «I do not know»
        /// instead of comparing with an empty string and finding it different
        /// from everything.
        /// </summary>
        internal static string RepositoryVersion()
        {
            try
            {
                var f = Path.Combine(Root(), "VERSION.txt");
                if (!File.Exists(f)) return null;
                var s = File.ReadAllText(f).Trim();
                return s.Length == 0 ? null : s;
            }
            catch { return null; }
        }

        /// <summary>
        /// `Tests/README.md` says how many checks there are. It must be true.
        ///
        /// On 16/9/2026 it said **54** while **99** were running: a number
        /// right when it was written, aged by itself while the suite
        /// grew. Whoever reads that file has no way of noticing — it is their
        /// only source. It is the same defect as the README that promised
        /// thirty-eight operations out of sixty, and the defense is the same: make
        /// a check read the document.
        ///
        /// THIS CHECK IS COUNTED TOO. It runs last, so when it
        /// looks at `Outcomes.Count` it itself is not there yet: the number written
        /// in the document must equal `Outcomes.Count + 1`. It is not a trick,
        /// it is the only count that adds up — and the document must say the total that
        /// the user reads on the screen, not that minus one.
        /// </summary>
        private static void TheReadmeSaysHowManyThereAre()
        {
            Group("the README of the tests");

            // THE COUNT HOLDS FOR A WHOLE RUN (entry F9-bis). Where SOLIDWORKS
            // is not there, a group of forty checks becomes ONE «not runnable
            // here» line: the total collapses, the README is still right, and
            // this check would turn red accusing the right document.
            // A check that knows it cannot answer says so.
            if (NotRunnableHere > 0)
            {
                NotHere("the README says how many checks there are, and they are the real ones",
                       "on this machine " + NotRunnableHere + " checks could not " +
                       "run: the number written in the README is that of a whole " +
                       "run and here it is not comparable.");
                return;
            }

            int real = Outcomes.Count + 1;

            var readme = Path.Combine(Root(), "Tests", "README.md");
            if (!File.Exists(readme))
            {
                Check("the README is there", false, readme);
                return;
            }

            string text;
            try { text = File.ReadAllText(readme); }
            catch (Exception ex) { Check("the README can be read", false, ex.Message); return; }

            // ONLY the bold form «**<number> checks» (the headline) is looked for: the
            // file also speaks of seconds and licences, and the rows of the table
            // say «4 checks» for the groups they describe, so a broad rule would
            // shout at a correct sentence.
            var foundItems = System.Text.RegularExpressions.Regex.Matches(
                text, @"\*\*(\d+)\s+checks");
            if (foundItems.Count == 0)
            {
                Check("the README says how many checks there are", false,
                      "I find no «N checks»: if the number disappears, " +
                      "this check disappears too");
                return;
            }

            var wrong = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in foundItems)
                if (Convert.ToInt32(m.Groups[1].Value) != real)
                    wrong.Add("«" + m.Value + "»");

            Check("the README says how many checks there are, and they are the real ones",
                  wrong.Count == 0,
                  wrong.Count == 0
                      ? real + " checks"
                      : "running: " + real + ", the README says " +
                        string.Join(" and ", wrong));
        }

        private static int Main(string[] arguments)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = Encoding.UTF8;

            Console.WriteLine("Oce.Solid.Agent.ContractTests — without SOLIDWORKS, on any machine");
            Console.WriteLine("root: " + Root());

            if (PretendWithoutInterop)
            {
                Console.WriteLine();
                Console.WriteLine("### OCE_PRETEND_NO_INTEROP=1 — SIMULATED RUN ###");
                Console.WriteLine("This machine pretends not to have SOLIDWORKS, to");
                Console.WriteLine("show how the suite behaves on a bare runner. The");
                Console.WriteLine("results of this run do NOT count as an acceptance test.");
            }

            NoInterop();
            EverySourceIsInItsOwnProject();
            EveryEnvironmentVariableIsInTheReadme();
            TheOperationsWithoutStateMatchTheSwitch();
            TheBenchReadmeCountsTheRightFiles();
            EveryCatalogFamilyHasAnAllowedDocument();
            EveryCasesFolderIsInTheList();
            NoTodoEntryIsNamedLikeAnother();
            TheDimensionsRefusalStillSaysWhatIsNeeded();
            TheBridgeLibraryIsTheSAMEAsTheBench();
            ExpectationsTests.All();
            CatalogTests.All();
            SessionTests.All();
            H19InstallerContract.RunAll();
            ProtocolTests.All();
            BenchTests.All();
            SentinelTests.All();
            DocumentTests.All();
            ErrorEnumTests.All();
            LimitsTests.All();
            DescriptionsTests.All();
            RefusalsTests.All();
            RequestedDocumentTests.All();
            RequiredFieldsTests.All();
            ExamplesTests.All();
            ImagesTests.All();
            LeashTests.All();
            DryRunTests.All();
            ConfirmationTests.All();
            ForbiddenSignaturesTests.All();
            DocumentSignaturesTests.All();
            RegistryTests.All();
            ProgressTests.All();
            ApiCatalogContracts.Run();
            ObsoleteCallsContract.RunAll();
            SelectionMarksContract.RunAll();
            ShapeSpellingContract.RunAll();
            Zc2110HelpersContract.RunAll();
            StateChamferContract.RunAll();
            Zc212DrawingContract.RunAll();
            AssembliesZc212terContract.RunAll();
            WeldmentConfigurationZc212terContract.RunAll();
            Zc2113ExportUnitsContract.RunAll();
            Zc2113SplineCentreMarksContract.RunAll();
            DocumentByPathContract.RunAll();
            NoAliasContract.RunAll();
            DescriptionValuesContract.RunAll();
            RefusalKeywordsContract.RunAll();
            Zc2189WeldAssemblyContract.RunAll();
            Zc2189DrawingContract.RunAll();
            Zc2189MachiningContract.RunAll();
            Zc215IdentityContract.RunAll();
            SolidWorksNamesContract.RunAll();
            Zc215SketchIdentityContract.RunAll();
            Zc215ActivationRebuildContract.RunAll();
            Zc215HolePointIdentityContract.RunAll();
            Zc2113PartDimensionContract.RunAll();
            FacesBeyondSixtyContract.RunAll();
            Zc2111ComponentsContract.RunAll();
            Zc14MateDirectionsContract.RunAll();
            R16StepExpectationsContract.RunAll();
            R6AssemblyStateTextContract.RunAll();
            R8PropertyScopeContract.RunAll();
            R15MeshContract.RunAll();
            R17EdgesFilterContract.RunAll();
            Zc2114UndoHonestyContract.RunAll();
            ReadmeBadgesContract.RunAll();
            LanguageGateTests.All();
            CaseKeysTests.All();
            BenchReportTests.All();
            ConsumedStringsContract.RunAll();
            SafetyNetSelfTests.All();
            // Last group: it reads the lookup log that every group above fills.
            ReflectionTests.All();
            TheReadmeSaysHowManyThereAre();

            Console.WriteLine();
            Console.WriteLine(new string('=', 76));
            int done = Outcomes.Count - NotRunnableHere;
            var summary = Outcomes.Count + " checks, " + FailedOnes + " failed";
            if (NotRunnableHere > 0)
                summary += ", " + NotRunnableHere + " not runnable here (" +
                             done + " really done)";
            Console.WriteLine(summary);

            // THE NUMBER ALONE WOULD READ AS A GREEN. Whoever looks at a CI
            // reads «0 failed» and moves on: if half of the checks did not
            // run, that number is true and misleading at once, and it is
            // exactly the defect that this repository reproaches elsewhere.
            if (NotRunnableHere > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  «NOT RUNNABLE HERE» does not mean passed.");
                Console.WriteLine("  Some checks need something this machine does not have: the");
                Console.WriteLine("  bridge (without SOLIDWORKS it cannot even BUILD) or the private");
                Console.WriteLine("  TODO (not published). Each skipped check printed its own reason");
                Console.WriteLine("  above. This number is the only place where the difference between");
                Console.WriteLine("  «proven and passes» and «not proven» stays visible.");
            }

            // THE MINIMUM IS DECLARED AND IS CHECKED. A suite that empties itself
            // — a check file removed, a group that exits early — would stay
            // green with three checks, and «0 failed» would sound the same.
            //
            // THE FACTS ARE COUNTED, NOT THE ENTRIES (entry F9-bis). By counting
            // `Outcomes.Count` the threshold would have been passed even by twenty-five
            // «not runnable here» lines: a minimum that lets itself be satisfied
            // by what did not run is not a minimum.
            const int DeclaredMinimum = 25;
            if (done < DeclaredMinimum)
            {
                Console.WriteLine("TOO FEW: at least " + DeclaredMinimum +
                                  " were declared to be really run, and what shows up is " + done +
                                  ". A suite that empties out stays green, and that is the most " +
                                  "silent way to stop checking.");
                FailedOnes++;
            }

            WriteJUnit();
            return FailedOnes == 0 ? 0 : 1;
        }

        /// <summary>
        /// THIS PROJECT DOES NOT REFERENCE THE INTEROP, AND CHECKS IT BY ITSELF.
        ///
        /// It is the reason it exists: to run where SOLIDWORKS is not there. If
        /// somebody adds a reference to the csproj — because «a type
        /// was needed» — the promise falls, and on this machine nobody would notice:
        /// here SOLIDWORKS is there, and it builds all the same.
        ///
        /// The references of the assembly are looked at, not the text of the csproj: the
        /// csproj can be read in ten ways, the real references are one.
        /// </summary>
        private static void NoInterop()
        {
            Group("this project runs where SOLIDWORKS is not");
            var myOwn = typeof(Program).Assembly;
            var dassault = myOwn.GetReferencedAssemblies()
                .Where(a => a.Name.StartsWith("SolidWorks", StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Name)
                .ToList();
            Check("no reference to SolidWorks.Interop",
                  dassault.Count == 0,
                  dassault.Count == 0
                      ? myOwn.GetReferencedAssemblies().Length + " references, none of them from Dassault"
                      : "found: " + string.Join(", ", dassault));
        }

        /// <summary>
        /// EVERY SOURCE IS IN ITS `.csproj`.
        ///
        /// ── THE DEFECT THIS CHECK CATCHES ────────────────────────────────
        ///
        /// A `.cs` file that does not appear among the `<Compile Include>` **is not
        /// compiled**. If somebody references it, MSBuild says so at once, and it
        /// really happened on 16/9/2026 with `AssemblyOperations.cs`: two errors,
        /// fixed in thirty seconds.
        ///
        /// **The dangerous case is the other one.** If nobody references it yet —
        /// a new class, an operation just written and not yet
        /// connected to the dispatcher — **the build stays green and the code does not
        /// exist**. A file is written, it is built, everything passes, and that file is not
        /// in the program. No error, anywhere.
        ///
        /// It is the defect that gives no error applied to the PROJECT instead of to the
        /// part, and there was no check that caught it.
        ///
        /// ── WHY IT IS HERE AND NOT IN A SCRIPT ────────────────────────────
        ///
        /// A script has to be remembered to be launched, and it is exactly the
        /// way the `pre-push` hook was born (entry W2.10): the check
        /// was there, what was missing was somebody running it. Here it runs on every run.
        /// </summary>
        private static void EverySourceIsInItsOwnProject()
        {
            Group("the sources are in the projects");

            // folder (relative to the root) -> csproj that must list it.
            // The `Checks\<tool>\` have one csproj per tool and stand
            // outside the solution on purpose: they are not checked here.
            var map = new[]
            {
                new { Folders = new[] { @"Oce.Solid.Agent.Core", @"Oce.Solid.Agent.Core\Catalog",
                                     @"Oce.Solid.Agent.Core\Properties", @"Oce.Solid.Agent.Core\Api" },
                      Project = @"Oce.Solid.Agent.Core\Oce.Solid.Agent.Core.csproj" },
                new { Folders = new[] { @"Bridge", @"Tools", @"Mcp" },
                      Project = @"Oce.Solid.Agent.csproj" },
                new { Folders = new[] { @"Bench" }, Project = @"Bench\Oce.Solid.Agent.Bench.csproj" },
                new { Folders = new[] { @"Tests" }, Project = @"Tests\Oce.Solid.Agent.ContractTests.csproj" },

                // `Shared\` IS BUILT BY TWO, and here naming one is enough.
                //
                // The check is one-way: «every source under this
                // folder appears in that project». That the bench builds the
                // same files does not make it false, and asking that a folder
                // be in ONE project only would turn red precisely the way in
                // which `Expectations.cs` has been shared for days.
                //
                // Without this line `Shared\` would be the only folder of
                // sources that nobody looks at: a file put there and forgotten
                // outside the csproj files would not be built and nobody would say
                // so — the defect this check exists for.
                new { Folders = new[] { @"Shared" },
                      Project = @"Tests\Oce.Solid.Agent.ContractTests.csproj" },
                new { Folders = new[] { @"Installer" }, Project = @"Installer\Installer.csproj" },
            };

            int inspected = 0;
            var outside = new List<string>();

            foreach (var m in map)
            {
                var csproj = Path.Combine(Root(), m.Project);
                if (!File.Exists(csproj))
                {
                    Check("the project «" + m.Project + "» is there", false, csproj);
                    continue;
                }
                string text;
                try { text = File.ReadAllText(csproj); }
                catch (Exception ex) { Check("«" + m.Project + "» can be read", false, ex.Message); continue; }

                foreach (var folder in m.Folders)
                {
                    var full = Path.Combine(Root(), folder);
                    if (!Directory.Exists(full)) continue;
                    // ONLY the first level: `obj\` and `bin\` are full of
                    // generated sources that no csproj lists, and that must
                    // not enter the count.
                    foreach (var f in Directory.GetFiles(full, "*.cs", SearchOption.TopDirectoryOnly))
                    {
                        var itemName = Path.GetFileName(f);
                        // Generated by the compiler, never listed by hand.
                        if (itemName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                            itemName.IndexOf("AssemblyAttributes", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        inspected++;
                        // The NAME is looked for between quotes: it covers both
                        // `Include="Foo.cs"` and `Include="Bridge\Bar.cs"`.
                        if (text.IndexOf(itemName + "\"", StringComparison.OrdinalIgnoreCase) < 0)
                            outside.Add(folder + "\\" + itemName + " → " + m.Project);
                    }
                }
            }

            Check("there are sources to check", inspected > 10, inspected + " file .cs");
            Check("every source is listed in its .csproj",
                  outside.Count == 0,
                  outside.Count == 0
                      ? "all " + inspected
                      : "OUTSIDE THE PROJECT (they are not compiled): " +
                        string.Join("; ", outside));
        }

        /// <summary>
        /// EVERY ENVIRONMENT VARIABLE THAT THE BRIDGE READS IS IN THE README.
        ///
        /// ── WHY THIS CHECK EXISTS ────────────────────────────────────────
        ///
        /// Found on 17/9/2026 reading the README for another reason:
        /// **three variables out of eight were not there**, and two of the three were
        /// `OCE_WRITE_ROOT` and `OCE_READ_ONLY_FOLDERS` — that is, the only two
        /// with which the bridge is told where it must NOT write. Whoever installs reads
        /// the table, not the sources: a defense that is in the code and not in the
        /// README **is a defense that nobody switches on**, and the part finds it out
        /// by rewriting a shared library.
        ///
        /// It is not documentary pedantry: it is the same defect as the other
        /// contract checks in here — two lists written in two places
        /// diverge, and the moment they diverge nobody sees it.
        ///
        /// ── WHAT IT LOOKS AT, AND WHAT IT DOES NOT ────────────────────────
        ///
        /// Only the sources of the BRIDGE and of the LIBRARY: `Bench\` and `Tests\`
        /// read variables that are for whoever develops, not for whoever installs, and
        /// demanding them in the README would mean filling it with stuff that is of no use
        /// to whoever reads it.
        ///
        /// The name is looked for **in the whole README**, not only in the table: a
        /// variable explained at length in a paragraph is documented. The
        /// check does not demand a form, it demands that the name be there.
        /// </summary>
        /// <summary>
        /// THE OPERATIONS THAT SKIP THE READING ARE THE ONES THAT NEVER RECEIVE IT
        /// (brief R2, 26/10/2026).
        ///
        /// `SwOperations.OperationsWithoutState` tells the executor which steps
        /// need no reading of the document. It is a list written by hand next to
        /// the switch of `SwOperations.Run`, and a list next to a switch goes
        /// stale: an operation that starts to look at `docState` and stays in
        /// the list would get an empty reading, find no identifier, and fail
        /// on a plan that was right. Here the switch is read as text, case by
        /// case, and the two must say the same thing.
        /// </summary>
        private static void TheOperationsWithoutStateMatchTheSwitch()
        {
            Group("the operations read without state are the ones the switch does not pass it to");

            var file = Path.Combine(Root(), "Oce.Solid.Agent.Core", "SwOperations.cs");
            string source;
            try { source = File.ReadAllText(file); }
            catch (Exception ex) { Check("SwOperations.cs can be read", false, ex.Message); return; }

            // The list, from its literal.
            var listStart = source.IndexOf("OperationsWithoutState =", StringComparison.Ordinal);
            var listEnd = listStart < 0 ? -1 : source.IndexOf("};", listStart, StringComparison.Ordinal);
            if (listStart < 0 || listEnd < 0) { Check("the list is there", false, file); return; }
            var listed = new SortedSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         source.Substring(listStart, listEnd - listStart), "\"([a-z_0-9]+)\""))
                listed.Add(m.Groups[1].Value);

            // The switch: every group of `case` labels takes the first `return`
            // after it; the group uses the state when that return passes `docState`.
            var lines = source.Replace("\r\n", "\n").Split('\n');
            int start = Array.FindIndex(lines, l => l.Contains("switch (operation)"));
            if (start < 0) { Check("the switch of Run is there", false, file); return; }
            var withoutState = new SortedSet<string>(StringComparer.Ordinal);
            var withState = new SortedSet<string>(StringComparer.Ordinal);
            var pending = new List<string>();
            var caseShape = new System.Text.RegularExpressions.Regex("^case \"([a-z_0-9]+)\":\\s*(.*)$");
            for (int i = start + 1; i < lines.Length; i++)
            {
                var l = lines[i].Trim();
                if (l.StartsWith("default:", StringComparison.Ordinal)) break;
                var m = caseShape.Match(l);
                string rest = null;
                if (m.Success) { pending.Add(m.Groups[1].Value); rest = m.Groups[2].Value; }
                else if (pending.Count > 0 && l.StartsWith("return", StringComparison.Ordinal)) rest = l;
                if (rest == null || !rest.StartsWith("return", StringComparison.Ordinal)) continue;
                foreach (var p in pending) (rest.Contains("docState") ? withState : withoutState).Add(p);
                pending.Clear();
            }

            Check("the switch has operations to compare", withoutState.Count + withState.Count >= 60,
                  (withoutState.Count + withState.Count) + " operations");
            var wrong = listed.Where(withState.Contains).ToList();
            Check("no operation in the list passes the state on", wrong.Count == 0,
                  wrong.Count == 0 ? listed.Count + " listed" : "THEY USE IT: " + string.Join(", ", wrong));
            var missing = withoutState.Where(x => !listed.Contains(x)).ToList();
            Check("every operation that does not pass the state on is in the list", missing.Count == 0,
                  missing.Count == 0 ? withoutState.Count + " without state" : "MISSING: " + string.Join(", ", missing));
            var unknown = listed.Where(x => !withoutState.Contains(x) && !withState.Contains(x)).ToList();
            Check("every operation in the list is in the switch", unknown.Count == 0,
                  unknown.Count == 0 ? "all known" : "NOT IN THE SWITCH: " + string.Join(", ", unknown));
        }

        private static void EveryEnvironmentVariableIsInTheReadme()
        {
            Group("the environment variables are in the README");

            var readme = Path.Combine(Root(), "README.md");
            if (!File.Exists(readme)) { Check("the README is there", false, readme); return; }
            string text;
            try { text = File.ReadAllText(readme); }
            catch (Exception ex) { Check("the README can be read", false, ex.Message); return; }

            // `GetEnvironmentVariable("X")` and the shortcut `Variable("X")`
            // of `Bridge\Paths.cs`. The second is exactly the one that hid
            // `OCE_WRITE_ROOT` and `OCE_READ_ONLY_FOLDERS` from a grep done on the
            // long name: **looking for one form only means finding only one.**
            var shape = new System.Text.RegularExpressions.Regex(
                @"(?:GetEnvironmentVariable|Variable)\(""([A-Z][A-Z_0-9]+)""\)");

            var foundOnes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var folder in new[] { "Bridge", "Tools", "Mcp", "Oce.Solid.Agent.Core" })
            {
                var full = Path.Combine(Root(), folder);
                if (!Directory.Exists(full)) continue;
                foreach (var f in Directory.GetFiles(full, "*.cs", SearchOption.AllDirectories))
                {
                    if (f.IndexOf(@"\obj\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        f.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string source;
                    try { source = File.ReadAllText(f); } catch { continue; }
                    foreach (System.Text.RegularExpressions.Match m in shape.Matches(source))
                        foundOnes.Add(m.Groups[1].Value);
                }
            }

            // A CHECK THAT FINDS NOTHING ALWAYS PASSES, and proves nothing.
            // If one day the folders change name, this line turns red
            // instead of letting the check switch itself off silently.
            Check("there are variables to check", foundOnes.Count >= 6,
                  foundOnes.Count + ": " + string.Join(", ", foundOnes));

            var absent = foundOnes.Where(v => text.IndexOf(v, StringComparison.Ordinal) < 0).ToList();
            Check("every variable read by the bridge is named in the README",
                  absent.Count == 0,
                  absent.Count == 0
                      ? "all " + foundOnes.Count
                      : "IN THE CODE AND NOT IN THE README: " + string.Join(", ", absent));
        }

        /// <summary>
        /// THE NUMBERS IN `Bench\README.md` ARE HOW MANY FILES THERE REALLY ARE.
        ///
        /// ── WHY IT EXISTS ─────────────────────────────────────────────────
        ///
        /// The README of the bench has a table «folder / how many cases / what they
        /// test». Looked at on 17/9/2026: **four numbers out of twenty were
        /// wrong** — `features` said 2 and had 7, `repeat` 3
        /// and had 6. Nobody had noticed, because a number in a document does not
        /// fail: it ages.
        ///
        /// It is the same defect that the README had on the count of operations
        /// («thirty-eight» when there were sixty), repaired in the same way: not
        /// by recounting by hand — *a count done by hand ages while it is
        /// being written* — but by having it recounted on every run.
        ///
        /// ── THE SUM, AND NOT THE LINE ─────────────────────────────────────
        ///
        /// A folder can appear **twice** in the table, and now there is
        /// one: `cases/drawing/` contains both the hand-written plans and the declared
        /// cases, and it is listed in both blocks with two different
        /// numbers. The lines of the same folder are added up and the
        /// sum is compared: 7 + 1 = 8 files. Demanding that every line on its own match
        /// would turn a correct table red.
        /// </summary>
        private static void TheBenchReadmeCountsTheRightFiles()
        {
            Group("the README of the bench counts the right files");

            var readme = Path.Combine(Root(), "Bench", "README.md");
            if (!File.Exists(readme)) { Check("the README of the bench is there", false, readme); return; }
            string text;
            try { text = File.ReadAllText(readme); }
            catch (Exception ex) { Check("the README of the bench can be read", false, ex.Message); return; }

            // `cases/<name>/` followed by spaces and a number. The
            // continuation lines (the description that wraps) do not have the number
            // and do not match: it is intended.
            //
            // AT THE START OF THE LINE, and the reason is measured (25/9/2026): without `^` the
            // count also took the sentence that TELLS the old line
            // («This line used to count it ... «cases/shape/ 5»»), and
            // `cases/shape/` is not in the repository by choice. Locally it passed,
            // because the five files are there; in a clean clone — the CI — it was
            // «named and not existing: shape», red on every push since 0.85.0.
            var shape = new System.Text.RegularExpressions.Regex(
                @"^cases/([a-z_]+)/\s+(\d+)", System.Text.RegularExpressions.RegexOptions.Multiline);

            var declared = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in shape.Matches(text))
            {
                var itemName = m.Groups[1].Value;
                int n = Convert.ToInt32(m.Groups[2].Value);
                declared[itemName] = (declared.ContainsKey(itemName) ? declared[itemName] : 0) + n;
            }

            // A CHECK THAT FINDS NOTHING ALWAYS PASSES. If the table changes
            // shape, this line turns red instead of switching itself off silently.
            Check("there are folders to check", declared.Count >= 15,
                  declared.Count + " rows «cases/<name>/ N»");

            var wrong = new List<string>();
            var absent = new List<string>();
            foreach (var v in declared)
            {
                var folder = Path.Combine(Root(), "Bench", "cases", v.Key);
                if (!Directory.Exists(folder)) { absent.Add(v.Key); continue; }
                int real = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly).Length;
                if (real != v.Value)
                    wrong.Add(v.Key + ": says " + v.Value + ", has " + real);
            }

            Check("every folder named in the README exists",
                  absent.Count == 0,
                  absent.Count == 0 ? declared.Count + " folders"
                                     : "named and not existing: " + string.Join(", ", absent));
            Check("the numbers in the README are how many files there are",
                  wrong.Count == 0,
                  wrong.Count == 0
                      ? "all " + declared.Count
                      : string.Join("; ", wrong));
        }

        /// <summary>
        /// EVERY FAMILY OF THE CATALOGUE HAS A DOCUMENT TYPE IT CAN RUN ON.
        ///
        /// ── WHY THIS CHECK EXISTS ─────────────────────────────────────────
        ///
        /// Found on 18/9/2026 reading `PlanExecutor.cs` for another
        /// reason: `PreliminaryChecks()` — which runs **before the first
        /// step of every plan**, and for all four tools of the bridge
        /// — admits only `swDocPART` and, if `AlsoOnDrawings`, `swDocDRAWING`.
        /// Not assemblies. Meanwhile the catalogue served to the clients listed three
        /// operations of the `assemblies` family.
        ///
        /// **The bridge promised operations that it would have refused before
        /// starting, and none of the 710 checks of the bench could see it:
        /// the bench calls the LIBRARY, not the bridge.** Two true measurements
        /// of two different things, and whoever reads the big number believes
        /// it covers the other too.
        ///
        /// ── HOW IT LOOKS AT IT ────────────────────────────────────────────
        ///
        /// From the source, not from a hand-written list: the admitted types are
        /// read from `PreliminaryChecks` with a regex on the `swDoc...`
        /// tokens, so the day somebody adds or removes one
        /// this check notices instead of staying in agreement with itself.
        ///
        /// ── WHAT IT DOES **NOT** CATCH, and it must be said ───────────────
        ///
        /// It looks at the FAMILIES, not at the single operations. `interference_check`
        /// is in the `checks` family and wants an assembly: this check
        /// classifies it as «part» and lets it pass. The real remedy is a
        /// `RequestedDocument` field on `OperationSpec` — an entry of its own,
        /// because it touches the library. Until it is there, **this check covers the
        /// family and not the single case**, and it is written here so that whoever
        /// sees it green does not believe it covers more.
        /// </summary>
        private static void EveryCatalogFamilyHasAnAllowedDocument()
        {
            Group("the catalog promises only documents that the executor accepts");

            // ── 1. the types the executor admits, read from its source ──
            var source = Path.Combine(Root(), "Oce.Solid.Agent.Core", "PlanExecutor.cs");
            if (!File.Exists(source))
            {
                Check("the executor source is there", false, source);
                return;
            }
            string code;
            try { code = File.ReadAllText(source); }
            catch (Exception ex) { Check("the source can be read", false, ex.Message); return; }

            int da = code.IndexOf("bool allowed", StringComparison.Ordinal);
            if (da < 0)
            {
                // If the line changes shape this check MUST NOT pass in
                // silence: a check that does not find what it looks for and passes is
                // worse than no check.
                Check("I find the line that decides the accepted types", false,
                      "«bool allowed» is no longer in PlanExecutor.cs: " +
                      "the check must be rewritten, not removed");
                return;
            }
            int a = code.IndexOf(';', da);
            var expression = a > da ? code.Substring(da, a - da) : code.Substring(da);

            var allowed = new SortedSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(expression, @"swDoc[A-Z]+"))
                allowed.Add(m.Value);

            Check("the executor allows at least one document type", allowed.Count >= 1,
                  string.Join(", ", allowed));

            // ── 2. the families the catalogue SERVES to the clients ─────────
            var json = Path.Combine(Root(), "bin", "catalog.json");
            // As in the group «the catalogue» (entry F9-bis): where the bridge could not
            // be built, `bin\\` is empty and the good file is the one in the
            // repository. This check reads a JSON and a C# source: it has
            // nothing to ask SOLIDWORKS, and stopping here would mean not
            // doing a verification that could be done.
            if (!ProductFromBuild(json) && !TheBridgeCouldBeBuilt())
            {
                var inRepository = Path.Combine(Root(), "Tools", "catalog.json");
                if (File.Exists(inRepository)) json = inRepository;
            }
            if (!File.Exists(json))
            {
                Check("the catalog is there", false, "cannot find " + json + " — produce it with .\\build.ps1");
                return;
            }
            Dictionary<string, object> root;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                s.MaxJsonLength = 40 * 1024 * 1024;
                root = s.DeserializeObject(File.ReadAllText(json)) as Dictionary<string, object>;
            }
            catch (Exception ex) { Check("the catalog can be read", false, ex.Message); return; }

            var list = (root != null && root.ContainsKey("operations")
                ? root["operations"] : null) as object[];
            if (list == null) { Check("the catalog has «operations»", false, ""); return; }

            var families = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var o in list)
            {
                var v = o as Dictionary<string, object>;
                if (v != null && v.ContainsKey("family") && v["family"] != null)
                    families.Add(Convert.ToString(v["family"]));
            }
            Check("there are families to check", families.Count >= 5,
                  families.Count + ": " + string.Join(", ", families));

            // ── 3. on which type of document each one makes sense ────────
            // Three hand-written lines, and their job is precisely NOT to
            // know everything: a family they do not know is RED, not green.
            // This way the list cannot age in silence — whoever adds
            // one must say what it runs on.
            var runsOn = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "assemblies",      "swDocASSEMBLY" },
                { "drawing",       "swDocDRAWING"  },
                { "extrusions",   "swDocPART"     },
                { "features",  "swDocPART"     },
                { "weldments",    "swDocPART"     },
                { "references",  "swDocPART"     },
                { "physical_properties",    "swDocPART"     },
                { "checks",    "swDocPART"     },
                { "exports", "swDocPART"     },
                { "identity",     "swDocPART"     },
                { "document",    "swDocPART"     },
                { "sheet_metal",      "swDocPART"     },
                { "dimensions",        "swDocPART"     },
            };

            var unknown = families.Where(f => !runsOn.ContainsKey(f)).ToList();
            Check("every family of the catalog is classified in here",
                  unknown.Count == 0,
                  unknown.Count == 0
                      ? "all " + families.Count
                      : "NEW AND UNCLASSIFIED FAMILY: " + string.Join(", ", unknown) +
                        " — say what type of document it runs on, in Tests\\Program.cs");

            var promises = families
                .Where(f => runsOn.ContainsKey(f) && !allowed.Contains(runsOn[f]))
                .Select(f => f + " (needs " + runsOn[f] + ")")
                .ToList();

            Check("no served family is refused before the first step",
                  promises.Count == 0,
                  promises.Count == 0
                      ? families.Count + " families, all on an accepted type (" +
                        string.Join(", ", allowed) + ")"
                      : "PROMISED AND UNREACHABLE: " + string.Join("; ", promises) +
                        " — the executor accepts only " + string.Join(", ", allowed));
        }

        /// <summary>
        /// TWO ENTRIES OF THE TODO ARE NOT CALLED THE SAME WAY.
        ///
        /// ── WHY THIS CHECK EXISTS ─────────────────────────────────────────
        ///
        /// On 17/9/2026 65 new entries were grafted in (the «Z section»),
        /// called `A1`, `B3`, `C3`… **Thirty-seven of those names were already
        /// used, in the same file, by different entries — and thirty-three of
        /// those were closed.** «Do C3» led to a green entry of 14/9 that had
        /// nothing to do with it: whoever got there concluded that the work was
        /// done, and the real entry stayed invisible.
        ///
        /// No error anywhere, which is the signature of this family
        /// of defects — the same as `40x40x3` against `40X40X3` and `.sldprt`
        /// against `.SLDPRT`, applied to the index of the work instead of to the data.
        ///
        /// ── THE EXCEPTION, AND IT IS A REAL CONVENTION ────────────────────
        ///
        /// When an entry is redone, the original is kept below with the previous name
        /// and the wording the TODO's marker of an original trace kept for reference:
        /// there the double name is intended and says something. So the duplicate is
        /// accepted **only** if one of the two is a trace.
        ///
        /// The TODO is not required to be in a format: what is required is that two
        /// different things not be called the same.
        /// </summary>
        /// <summary>
        /// EVERY CASE FOLDER IS IN THE LIST THAT THE BENCH SCANS.
        ///
        /// ── THE DEFECT THIS CHECK CATCHES ─────────────────────────────────
        ///
        /// `Bench\DeclaredCases.cs` has an EXPLICIT list of families, and
        /// scans only those. A new folder under `Bench\cases\` that does not
        /// end up in that list contains files that **look like checks and never
        /// run**: no error, no warning, and the count of the checks
        /// does not notice because those cases never existed for it.
        ///
        /// It happened on 23/9/2026 writing entry Z-C6: the folder
        /// `weldments\` was born with its first case inside, and would have stayed
        /// mute. *A hand-written list is a thing that one day somebody
        /// forgets to lengthen, and the way they forget it is by not
        /// noticing.*
        ///
        /// ── WHY THE SHAPE OF THE FILES IS LOOKED AT, NOT THE NAME ─────────
        ///
        /// Under `cases\` there are two different things: the CASES (objects with
        /// steps and expectations, which the bench executes) and the PLANS (arrays, which
        /// `run_plans.js` launches by hand). The folders of plans only —
        /// `bridge\`, `limits\`, `views\`, `parts\`, `shape\` — stand outside
        /// the list and rightly so.
        ///
        /// So the check does not ask «every folder is in the list»:
        /// it asks «every folder that contains at least one CASE is in the list».
        /// The difference is between a check that describes reality and one that
        /// demands an order that is not there.
        /// </summary>
        private static void EveryCasesFolderIsInTheList()
        {
            Group("the case folders are in the list the bench goes through");

            var source = Path.Combine(Root(), "Bench", "DeclaredCases.cs");
            var casesRoot = Path.Combine(Root(), "Bench", "cases");
            if (!File.Exists(source) || !Directory.Exists(casesRoot))
            {
                Check("the bench and its cases are there", false,
                      source + " / " + casesRoot);
                return;
            }

            var text = File.ReadAllText(source);
            var block = System.Text.RegularExpressions.Regex.Match(text, @"Families\s*=\s*\{([\s\S]*?)\};");
            if (!block.Success)
            {
                // IF THE LIST DISAPPEARS, THIS CHECK DISAPPEARS TOO: and
                // a check that disappears in silence is worse than one that
                // was never there.
                Check("I find the list of the families", false,
                      "there is no «Families = { ... }» in DeclaredCases.cs");
                return;
            }

            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(block.Groups[1].Value, "\"([a-z0-9_-]+)\""))
                listed.Add(m.Groups[1].Value);

            Check("the list of the families can be read", listed.Count >= 10,
                  listed.Count + " families");
            if (listed.Count < 10) return;

            // `regression` is NOT in the list, and it is not a defect: it has a
            // run of its own (`--regression`), with an evaluator of its own. It is written
            // here and not in a generic list of exceptions, because it is
            // only one and it has a reason.
            var outsideByDesign = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "regression" };

            var mute = new List<string>();
            int inspected = 0, withCases = 0;
            foreach (var folder in Directory.GetDirectories(casesRoot))
            {
                inspected++;
                var itemName = Path.GetFileName(folder);
                bool hasCases = false;
                foreach (var f in Directory.GetFiles(folder, "*.json"))
                {
                    string raw;
                    try { raw = File.ReadAllText(f); } catch { continue; }
                    // A CASE IS AN OBJECT, A PLAN IS AN ARRAY. The same
                    // structural distinction that the bench makes.
                    var t = raw.TrimStart();
                    if (t.StartsWith("{") && t.IndexOf("\"steps\"", StringComparison.Ordinal) >= 0)
                    { hasCases = true; break; }
                }
                if (!hasCases) continue;
                withCases++;
                if (!listed.Contains(itemName) && !outsideByDesign.Contains(itemName))
                    mute.Add(itemName);
            }

            Check("every folder with cases in it is in the list",
                  mute.Count == 0,
                  mute.Count == 0
                      ? withCases + " folders with cases out of " + inspected + ", all scanned"
                      : "MUTE, their cases never run: " + string.Join(", ", mute) +
                        " — add them to «Families» in Bench\\DeclaredCases.cs");
        }

        /// <summary>
        /// Where the TODO is, or `null` if in this clone it is not there.
        ///
        /// Same order as `Checks\where-is-the-todo.js`, and for the same
        /// reason: the `TODO_MD` variable for whoever keeps it elsewhere,
        /// `note-interne\TODO.md` where it is today, and the root where it was before —
        /// so an old clone keeps working without noticing.
        /// </summary>
        private static string WhereTheTodoIs()
        {
            var its = Environment.GetEnvironmentVariable("TODO_MD");
            if (!string.IsNullOrEmpty(its) && File.Exists(its)) return its;

            foreach (var c in new[] {
                Path.Combine(Root(), "note-interne", "TODO.md"),
                Path.Combine(Root(), "TODO.md") })
            {
                if (File.Exists(c)) return c;
            }
            return null;
        }

        private static void NoTodoEntryIsNamedLikeAnother()
        {
            Group("in the TODO no two entries have the same name");

            // THE TODO IS NO LONGER IN THE PUBLIC REPOSITORY: it is a work
            // journal and lives in `note-interne\`, which the .gitignore excludes.
            //
            // HERE THERE USED TO BE `Check(..., false, ...)`, that is a RED. On
            // this machine it would never have triggered; on every clone it would
            // have been red always, for a file that whoever clones does not have and must not
            // have - a check that fails for everybody except whoever wrote it
            // does not measure the code, it measures whoever runs it.
            //
            // Now the check is SKIPPED, and says so. The count of the checks
            // goes down by one instead of going up by a red, which is the difference
            // between «I did not check it» and «I checked it and it goes badly».
            var todo = WhereTheTodoIs();
            if (todo == null)
            {
                // Counted as «not runnable here», not only printed: a bare
                // Console.WriteLine leaves no outcome behind, so the total of a
                // clone (861) differed from the README's (863) and the check
                // «the README says how many checks there are» turned the suite
                // RED on every fresh clone. Seen on a clone of v0.130.1, built
                // with build.ps1, no TODO. NotHere makes that check step aside
                // and says why.
                NotHere("in the TODO no two entries have the same name",
                        "the TODO is not in this clone: it is internal and is not " +
                        "published. THIS CHECK WAS NOT DONE. With TODO_MD you point to a copy.");
                return;
            }
            string[] lines;
            try { lines = File.ReadAllLines(todo); }
            catch (Exception ex) { Check("the TODO can be read", false, ex.Message); return; }

            var header = new System.Text.RegularExpressions.Regex(
                @"^#### \[(.)\] (\S+)\s*(.*)$");
            var trace = new System.Text.RegularExpressions.Regex(
                @"traccia originale|progetto originale|tenut[ao] per|per riferimento|com'era scritta",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // name -> how many times, and how many of those were traces
            var howMany = new Dictionary<string, int>(StringComparer.Ordinal);
            var traces = new Dictionary<string, int>(StringComparer.Ordinal);
            int headingCount = 0;

            foreach (var r in lines)
            {
                var m = header.Match(r);
                if (!m.Success) continue;
                headingCount++;
                var itemName = m.Groups[2].Value;
                howMany[itemName] = (howMany.ContainsKey(itemName) ? howMany[itemName] : 0) + 1;
                if (trace.IsMatch(m.Groups[3].Value))
                    traces[itemName] = (traces.ContainsKey(itemName) ? traces[itemName] : 0) + 1;
            }

            // A CHECK THAT FINDS NOTHING ALWAYS PASSES. If one day the
            // headings change shape, this line turns red instead of
            // letting the check switch itself off silently.
            Check("there are entries to check", headingCount >= 100, headingCount + " headings");

            var doubled = new List<string>();
            foreach (var v in howMany)
            {
                int t = traces.ContainsKey(v.Key) ? traces[v.Key] : 0;
                // one live entry plus its traces: fine. Two live ones, no.
                if (v.Value - t > 1)
                    doubled.Add(v.Key + " (×" + v.Value + ", of which traces: " + t + ")");
            }
            doubled.Sort(StringComparer.Ordinal);

            Check("two different entries do not have the same name",
                  doubled.Count == 0,
                  doubled.Count == 0
                      ? headingCount + " headings, no name used twice for two things"
                      : "SAME NAME, DIFFERENT ENTRIES: " + string.Join("; ", doubled));
        }

        /// <summary>
        /// THE REFUSAL OF `model_dimensions` STILL SAYS WHAT IS NEEDED.
        ///
        /// ── WHY IT EXISTS, AND WHAT IT IS NOT ─────────────────────────────
        ///
        /// Measured on 18/9/2026: on a part built by the bridge, with **seven
        /// dimensions all marked for the drawing**, five import routes
        /// brought **zero**. **On 26/9/2026 the import works out of process**
        /// (7 dimensions on the same part, two probes) and the behaviour check
        /// is there: `dimensions/dimensions_come_from_the_model`, with the refusal without views and
        /// the green twin (Z-C1-bis, Z-E7 closed). This check stays for the
        /// day the import goes back to bringing zero dimensions: then the message
        /// below is once again all that a model receives.
        ///
        /// In that case **that message is all that a model receives
        /// when it asks for the dimensions**, and it is the part that works: it says how many
        /// dimensions the part has, **how many are marked** — the datum that splits the
        /// possible causes in two — and the list of the routes tried. If somebody
        /// shortens it «because it was long», nobody notices.
        ///
        /// ── WHAT IT LOOKS AT, AND WHAT IT CANNOT LOOK AT ──────────────────
        ///
        /// It looks at the **source**, not at the execution: here SOLIDWORKS is not there.
        /// So it proves that the sentences are still there, **not** that the refusal
        /// triggers nor that it says the truth: that is proved by the bench case
        /// of the pair (since 26/9/2026).
        /// </summary>
        private static void TheDimensionsRefusalStillSaysWhatIsNeeded()
        {
            Group("the dimensions refusal still says what is needed");

            var file = Path.Combine(Root(), "Oce.Solid.Agent.Core", "DrawingOperations.cs");
            if (!File.Exists(file)) { Check("the source is there", false, file); return; }
            string code;
            try { code = File.ReadAllText(file); }
            catch (Exception ex) { Check("the source can be read", false, ex.Message); return; }

            // THE ANCHOR. If it disappears, this check no longer has a subject and
            // must turn RED: a check that does not find what it looks for and
            // passes is worse than no check.
            const string anchor = "No dimension arrived";
            if (code.IndexOf(anchor, StringComparison.Ordinal) < 0)
            {
                Check("I find the refusal of model_dimensions", false,
                      "«" + anchor + "» is no longer in DrawingOperations.cs: " +
                      "if the import has been repaired this check must be REWRITTEN, not removed");
                return;
            }
            Check("I find the refusal of model_dimensions", true, "");

            // Every piece, and next to it WHY that piece: a list of strings
            // without the reason is the first thing somebody cuts.
            var parts = new[]
            {
                new[] { "marked for the drawing",
                        "it is the datum that splits the causes in two: marked and not imported " +
                        "(fault in the import) versus not marked (fault upstream)" },
                new[] { "[marked]",
                        "the qualifier next to EVERY name, not only the total" },
                new[] { "[NOT marked]",
                        "without the opposite, «marked» is not information" },
                new[] { "I tried: ",
                        "the list of the ways tried: it is what makes the " +
                        "failure a fact instead of a mystery" },
            };

            var missingItems = new List<string>();
            foreach (var p in parts)
                if (code.IndexOf(p[0], StringComparison.Ordinal) < 0)
                    missingItems.Add("«" + p[0] + "» (" + p[1] + ")");

            Check("the refusal still names how many dimensions are marked and what it tried",
                  missingItems.Count == 0,
                  missingItems.Count == 0
                      ? "all " + parts.Length + " pieces are there"
                      : "REMOVED FROM THE MESSAGE: " + string.Join("; ", missingItems));
        }

        /// <summary>
        /// THE LIBRARY THAT THE BRIDGE LOADS IS THE SAME ONE THAT THE BENCH MEASURES.
        ///
        /// ── WHY IT EXISTS, AND WHAT IT COST ───────────────────────────────
        ///
        /// There are **two copies**, and they live in two places:
        ///
        ///   Oce.Solid.Agent.Core\bin\Release\Oce.Solid.Agent.Core.dll   <- the bench
        ///   bin\Oce.Solid.Agent.Core.dll                             <- the bridge
        ///
        /// The second is put there by `build.ps1`. Whoever builds **the library project
        /// alone** — which is the most natural thing in the world while
        /// iterating — updates the first and **not** the second.
        ///
        /// On 18/9/2026 they diverged by four versions for nine hours.
        /// The bench measured 0.98.4 and the bridge was running 0.98.1: a repair
        /// was tested three times by the bridge and **was never tested**,
        /// because it was not in that binary. The symptom was perfect —
        /// the operation «did not refuse» — and the cause was not in the code.
        ///
        /// *Two copies of the same thing in two places diverge, and the day
        /// they diverge nobody sees it: the symptom is seen, months later,
        /// disguised as a defect of the code.*
        ///
        /// ── WHAT IT LOOKS AT ──────────────────────────────────────────────
        ///
        /// The two versions, and that is all. Not the bytes: a rebuild with no changes
        /// changes the bytes and changes nothing of what counts.
        /// If one of the two is missing it says so and **does not pass**: a check that does not
        /// find what it looks for and passes is worse than no check.
        /// </summary>
        private static void TheBridgeLibraryIsTheSAMEAsTheBench()
        {
            Group("the THREE copies of the library are the same");

            var bench = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release",
                                        "Oce.Solid.Agent.Core.dll");
            var bridge = Path.Combine(Root(), "bin", "Oce.Solid.Agent.Core.dll");

            // ── THE THIRD, WHICH THIS CHECK DID NOT LOOK AT ──────────────
            //
            // `Bench\bin\Oce.Solid.Agent.Core.dll` is the one that
            // `Oce.Solid.Agent.Bench.exe` REALLY loads, and for two days this
            // group called itself «the bridge and the bench load the same
            // library» while comparing two copies of which **neither
            // was the one of the bench in execution**. The first is the source
            // from which `bench.ps1` copies it while building.
            //
            // On 24/9/2026 it cost a run: `Oce.Solid.Agent.Bench.exe` launched
            // by hand to iterate quickly loaded the copy from the day
            // before and said that two defaults just implemented **did not
            // exist**. They were in the source and in the other two DLLs.
            //
            // This check does not prevent launching the executable by hand
            // — nothing prevents it — but it says **at once** that the copies have
            // diverged, instead of leaving it to be discovered by a report that
            // accuses the wrong code.
            var nextToBench = Path.Combine(Root(), "Bench", "bin",
                                              "Oce.Solid.Agent.Core.dll");

            bool c1 = ProductFromBuild(bench), c2 = ProductFromBuild(bridge);

            // Without the interop the library could not be built: both are missing
            // because the machine cannot do it, not because somebody
            // made a mistake. See `NotHere`.
            if (!c1 && !c2 && !TheBridgeCouldBeBuilt())
            {
                NotHere("the bridge and the bench load the same library",
                       "SOLIDWORKS is not here: Oce.Solid.Agent.Core.dll cannot be built, " +
                       "and two files that do not exist cannot be compared.");
                return;
            }

            Check("the library that the bench measures is there", c1, bench);
            Check("the library that the bridge loads is there", c2,
                  c2 ? bridge : bridge + " — produce it with .\\build.ps1");
            if (!c1 || !c2) return;

            string v1 = Version(bench), v2 = Version(bridge);
            Check("the two versions can be read", v1 != null && v2 != null,
                  (v1 ?? "?") + " / " + (v2 ?? "?"));
            if (v1 == null || v2 == null) return;

            Check("the source and the bridge have the SAME version of the library",
                  string.Equals(v1, v2, StringComparison.Ordinal),
                  string.Equals(v1, v2, StringComparison.Ordinal)
                      ? "both " + v1
                      : "SOURCE " + v1 + " against BRIDGE " + v2 +
                        " — whoever compiled only the project updated the first and " +
                        "not the second: run .\\build.ps1 again, which aligns them");

            // ── AND THE THIRD COPY: WE LOOK AT THE ROOT, NOT THE SYMPTOM ──
            //
            // `Bench\bin\Oce.Solid.Agent.Core.dll` is a LEFTOVER: `bench.ps1`
            // passes `--library` pointing at the source, and nobody updates
            // that copy. Demanding that it match would be a perennial red
            // on a file that the real run never looks at.
            //
            // What counts is the DEFAULT: what `Oce.Solid.Agent.Bench.exe`
            // loads if somebody launches it by hand without `--library`. Until 24/9/2026 it was
            // the copy next to itself, and it
            // made me measure for a run the code of six versions before
            // saying that two defaults just written «did not
            // exist». Now it is the same one that `bench.ps1` uses.
            //
            // It is read from the SOURCE, because that is where the choice lives, and
            // because a check that read the leftover would go back to
            // watching the symptom.
            var benchSource = Path.Combine(Root(), "Bench", "Program.cs");
            if (File.Exists(benchSource))
            {
                var text = File.ReadAllText(benchSource);
                bool findTheGoodOne =
                    text.IndexOf("DefaultLibrary", StringComparison.Ordinal) >= 0 &&
                    text.IndexOf("\"Oce.Solid.Agent.Core\", \"bin\", \"Release\"",
                                  StringComparison.Ordinal) >= 0;
                Check("without «--library» the bench looks FIRST for the library just compiled",
                      findTheGoodOne,
                      findTheGoodOne
                          ? "the fallback next to the executable stays, but comes after"
                          : "the default points next to the executable: whoever runs " +
                            "`Oce.Solid.Agent.Bench.exe` by hand measures a copy that nobody " +
                            "updates");

                // And if the leftover is there, it is SAID — without accusing: knowing
                // that it is old explains a confusing log from six months ago.
                var leftoverCheck = "and it is known whether next to the executable there is an old leftover";
                if (ProductFromBuild(nextToBench))
                {
                    string v3 = Version(nextToBench);
                    Check(leftoverCheck, true,
                          string.Equals(v1, v3, StringComparison.Ordinal)
                              ? "there is one, and it is aligned (" + v3 + ")"
                              : "there is one STUCK AT " + v3 + " against " + v1 +
                                ": nobody uses it any more, but if an old log " +
                                "does not add up, it is that one");
                }
                // Said when there is none as well: a check that only exists
                // on the machines with a leftover makes the count of a full
                // run depend on the machine, and the count check on the readme
                // of the contracts then accuses a right document (1/10/2026:
                // 765 on the main checkout, 764 in a fresh worktree).
                else
                    Check(leftoverCheck, true, "there is none: nothing stale next to the executable");
            }

            // ── AND THE THIRD NUMBER, THE ONE NOBODY LOOKS AT ─────────────
            //
            // `AssemblyInformationalVersion` is the version that Windows shows
            // in the properties of the file. No code reads it, and that is why it
            // ages by itself: on 18/9/2026 it said 0.61.0 with the assembly at
            // 0.66.0, it was corrected, and on 22/9/2026 it said **0.94.0 with
            // the assembly at 0.99.0** — five versions behind, again.
            //
            // Above that line there was already a comment telling the
            // defect at length. It did not help, and it could not help: **a
            // comment is not a check**. Whoever aligns the number by hand
            // realigns it as long as they remember, and then stops.
            //
            // The binary is looked at and not the source: what ends up in the
            // package is the binary.
            string notice = VersionInformation(bench);
            Check("the version shown by Windows is the real one",
                  notice != null &&
                  v1.StartsWith(notice, StringComparison.Ordinal),
                  notice == null
                      ? "cannot be read"
                      : "assembly " + v1 + ", file properties " + notice +
                        (v1.StartsWith(notice, StringComparison.Ordinal)
                            ? ""
                            : " — align AssemblyInformationalVersion in " +
                              "Oce.Solid.Agent.Core\\Properties\\AssemblyInfo.cs"));
        }

        /// <summary>
        /// `AssemblyInformationalVersion` of the file, that is the one read
        /// in Properties → Details → Product version. `null` if it is not there.
        /// </summary>
        private static string VersionInformation(string filePath)
        {
            try
            {
                var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(filePath)
                                                          .ProductVersion;
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }
            catch { return null; }
        }

        /// <summary>
        /// The version of an assembly, read without loading it into the process:
        /// loading it would mean keeping it, and this suite runs also where the
        /// Dassault interop is not there.
        /// </summary>
        private static string Version(string dll)
        {
            try { return System.Reflection.AssemblyName.GetAssemblyName(dll).Version.ToString(); }
            catch { return null; }
        }

        /// <summary>
        /// `results.xml` in JUnit format: it is what every CI knows how to read without
        /// anybody teaching it anything.
        /// </summary>
        private static void WriteJUnit()
        {
            try
            {
                var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "results.xml");
                var x = new StringBuilder();
                x.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                x.AppendLine("<testsuites name=\"Oce.Solid.Agent.ContractTests\" tests=\"" + Outcomes.Count +
                             "\" failures=\"" + FailedOnes +
                             "\" skipped=\"" + NotRunnableHere + "\">");
                foreach (var g in Outcomes.GroupBy(e => e.Group))
                {
                    x.AppendLine("  <testsuite name=\"" + Xml(g.Key) + "\" tests=\"" + g.Count() +
                                 "\" failures=\"" + g.Count(e => !e.Ok) +
                                 "\" skipped=\"" + g.Count(e => e.NotHere) + "\">");
                    foreach (var e in g)
                    {
                        x.Append("    <testcase classname=\"" + Xml(g.Key) +
                                 "\" name=\"" + Xml(e.Name) + "\"");
                        // A SKIPPED IS NOT A PASSED, not even in the XML: a bare
                        // `<testcase />` is a green for whoever reads the
                        // file, and the file is read by a CI that then shows a tick
                        // mark. `<skipped>` is the entry that JUnit has precisely
                        // for this, and every interface knows how to draw it differently.
                        if (e.NotHere)
                        {
                            x.AppendLine(">");
                            x.AppendLine("      <skipped message=\"" + Xml(e.Detail) + "\" />");
                            x.AppendLine("    </testcase>");
                        }
                        else if (e.Ok) x.AppendLine(" />");
                        else
                        {
                            x.AppendLine(">");
                            x.AppendLine("      <failure message=\"" + Xml(e.Detail) + "\" />");
                            x.AppendLine("    </testcase>");
                        }
                    }
                    x.AppendLine("  </testsuite>");
                }
                x.AppendLine("</testsuites>");
                File.WriteAllText(file, x.ToString(), new UTF8Encoding(false));
                Console.WriteLine("results.xml: " + file);
            }
            catch (Exception ex)
            {
                Console.WriteLine("could not write results.xml: " + ex.Message);
            }
        }

        private static string Xml(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;")
                            .Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
