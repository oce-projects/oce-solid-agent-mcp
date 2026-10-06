using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Oce.Solid.Agent.Bench
{
    /// <summary>
    /// Tests the REAL DLL of the add-in against SolidWorks, not a copy.
    ///
    /// WHY THIS WAY
    /// The previous rounds measured the SolidWorks APIs by rewriting
    /// the calls here. It served to discover the recipe, but says nothing about
    /// what will be installed: between the test program and the add-in there
    /// are the reading of the parameters, the unit conversions, the checks and
    /// the error messages — that is half of the code that can go wrong.
    ///
    /// Here the freshly built `OpenPDM.SwAddin.dll` is loaded and its internal
    /// types are called by reflection, with the same arguments that
    /// would arrive from the server's plan. What passes through here is what
    /// the designer will have in hand.
    ///
    /// Every check works on a disposable part, which is closed.
    /// </summary>
    internal static partial class Program
    {
        /// <summary>
        /// Where the drawing library is. Default: next to
        /// the executable. It is changed with `--libreria &lt;percorso&gt;`.
        ///
        /// BEFORE IT WAS A CONSTANT that pointed to a temporary folder of
        /// a session, and that path no longer exists today. A bench that can
        /// be launched only from the machine and the week in which it was
        /// written is not a repeatable measurement: it is an anecdote.
        /// </summary>
        /// <remarks>
        /// AND THE DEFAULT WAS CHANGED on 24/9/2026, because it was a
        /// trap.
        ///
        /// It pointed to the DLL **next to the executable**. But the real bench is
        /// `bench.ps1`, which builds and then passes `--libreria` pointing to
        /// `Oce.Solid.Agent.Core\bin\Release\` — so the copy next to
        /// the executable is updated by nobody, and stays stuck
        /// at the last time that something put it there.
        ///
        /// On 24/9/2026 it was at **0.105.0.0**, six versions behind. Launching
        /// the executable by hand to iterate fast on two new cases, it
        /// said that two defaults just implemented **did not exist**:
        /// «Manca il nome del materiale», «campo assente: misure.nota_assunto».
        /// They were in the source and in the other two copies. *The symptom was
        /// perfect and the cause was not in the code.*
        ///
        /// Now the default is the SAME path that `bench.ps1` uses,
        /// and the copy next to the executable stays only as a fallback — if
        /// the good one is not there. This way launching the executable by hand measures
        /// the code of now, which is what whoever launches it believes they are
        /// measuring.
        ///
        /// **In both cases the line «libreria X.Y.Z (percorso)» comes out
        /// at the head of the run**, and it is what made it possible to understand.
        /// </remarks>
        private static string _library = DefaultLibrary();

        /// <summary>
        /// True when `--libreria` named another library: an older one may lack
        /// names the bench looks up, by design (W3.1-bis.2, item 3).
        /// </summary>
        private static bool _anotherLibrary;

        /// <summary>
        /// The same library file? Full paths compared without case (Windows
        /// paths); a path that cannot be read is «not the same», so an odd
        /// --libreria still counts as another library and skips what an older
        /// library could not pass.
        /// </summary>
        internal static bool SameLibraryPath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a ?? ""), Path.GetFullPath(b ?? ""),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private static string DefaultLibrary()
        {
            var nextTo = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? ".";
            // `Bench\bin\` -> two up -> the root of the repository.
            try
            {
                var root = Path.GetFullPath(Path.Combine(nextTo, "..", ".."));
                var good = Path.Combine(root, "Oce.Solid.Agent.Core", "bin", "Release",
                                         "Oce.Solid.Agent.Core.dll");
                if (File.Exists(good)) return good;
            }
            catch { }
            return Path.Combine(nextTo, "Oce.Solid.Agent.Core.dll");
        }

        /// <summary>The names of the types, after the move into the library.</summary>
        /// <remarks>
        /// They were `OpenPDM.SwAddin.Agente.*`. Entries B4 and B5 moved the
        /// operations into `Oce.Solid.Agent.Core`, and since then this bench
        /// **no longer started**: `GetType(..., true)` raises on a type that
        /// is not there. Nobody had noticed because the bench lived
        /// outside the repository and nothing launched it any more — which is
        /// exactly the reason entry F1 exists.
        /// </remarks>
        private const string NS = "Oce.Solid.Agent.Core.";
        /// <summary>
        /// THE STANDARD PROFILES, which belong to whoever works and not to the repository.
        ///
        /// The checks on weldments need the real library files:
        /// an invented `.SLDLFP` measures nothing. Where it is is stated with
        /// `PROFILI_SALDATURA`.
        ///
        /// It used to be a constant. Whoever clones the repository does not have that disk, and with
        /// a constant found out that they did not have it when the bench failed without
        /// saying why. Then it became a variable WITH A FALLBACK, which is the
        /// same thing written better: the fallback was the path of a single
        /// workstation, it helped nobody else, and it published how
        /// that disk is laid out.
        ///
        /// Now the fallback is not there: empty is empty, and `Path.Combine` never
        /// sees it because the bench stops before, saying so.
        /// </summary>
        private static readonly string WeldmentProfiles =
            System.Environment.GetEnvironmentVariable("PROFILI_SALDATURA");

        private static readonly string SQUARE_TUBE = string.IsNullOrWhiteSpace(WeldmentProfiles)
            ? null : System.IO.Path.Combine(WeldmentProfiles, "TUBO STRUTTURALE QUADRO.SLDLFP");
        private static readonly string RECT = string.IsNullOrWhiteSpace(WeldmentProfiles)
            ? null : System.IO.Path.Combine(WeldmentProfiles, "TUBO STRUTTURALE RETTANGOLARE.SLDLFP");

        private static ISldWorks _sw;
        private static Assembly _addin;
        private static Type _tOperations, _tState, _tOutcome;
        private static readonly List<string> _outcomes = new List<string>();
        private static int _failed;

        /// <summary>
        /// DASSAULT'S LIBRARIES ARE NOT COPIED INTO THE REPOSITORY, nor
        /// next to the bench's executable.
        ///
        /// The csproj references them with `Private=False`, so they do not end up
        /// in `bin`: they cannot be redistributed by mistake, and one is not
        /// tied to a version. In their place the one INSTALLED on
        /// this machine is taken, which is by definition the same one that is running.
        ///
        /// WHY MAIN IS SPLIT IN TWO. The handler must be installed BEFORE
        /// the JIT compiles a method that names an interop type, and the JIT
        /// compiles the whole method before executing its first line. With the
        /// run inside `Main` the handler was installed too late and
        /// the exception came out "in Program.Main", that is before any line
        /// written here. `Startup` is kept apart and is not allowed to be inlined.
        /// </summary>
        private static int Main(string[] lineArguments)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, argument) =>
            {
                var itemName = new AssemblyName(argument.Name).Name;
                if (!itemName.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase))
                    return null;
                var places = new List<string>();
                // ENTRY H10: one variable only, and its meaning is THE
                // ROOT. `SW_REDIST` is still read afterwards, for whoever already has it.
                // The bench and the bridge must look IN THE SAME PLACES and
                // in the same order: two different searches for the same file
                // are two programs that one day use two different interops.
                var root = System.Environment.GetEnvironmentVariable("SW_INSTALL_PATH");
                if (!string.IsNullOrWhiteSpace(root))
                    places.Add(Path.Combine(root.Trim(), "api", "redist"));
                var its = System.Environment.GetEnvironmentVariable("SW_REDIST");
                if (!string.IsNullOrWhiteSpace(its)) places.Add(its);
                places.Add(@"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist");
                places.Add(@"C:\Program Files\SOLIDWORKS\api\redist");
                foreach (var folder in places)
                {
                    var file = Path.Combine(folder, itemName + ".dll");
                    if (File.Exists(file))
                    {
                        try { return Assembly.LoadFrom(file); } catch { }
                    }
                }
                Console.WriteLine("cannot find " + itemName + ": say where " +
                                  "SOLIDWORKS is installed, in the variable SW_INSTALL_PATH (the root, " +
                                  "the one that ends with \\SOLIDWORKS).");
                return null;
            };
            return Startup(lineArguments);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int Startup(string[] lineArguments)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            // ── THE PRECONDITION IS SAID FIRST, NOT MID-RUN ─────────────────
            //
            // The bench wants the library of the standard profiles, which belongs to whoever
            // works and not to the repository. As long as there was a hard-wired fallback -
            // the path of a single workstation - whoever cloned found out
            // after three hundred checks, with a refusal that named a disk that
            // they had never seen.
            //
            // Now the fallback is not there, and this check exists because
            // without it the symptom would have GOT WORSE: a `null` in
            // `Path.Combine` mid-run, that is an exception in place of a
            // sentence. *Removing a fallback without putting the sentence that explains
            // what is missing is not a repair, it is moving the damage.*
            if (string.IsNullOrWhiteSpace(WeldmentProfiles))
            {
                Console.WriteLine();
                Console.WriteLine("PROFILI_SALDATURA is not set, and the bench cannot run.");
                Console.WriteLine();
                Console.WriteLine("  The weldment checks open the real `.SLDLFP` files of the");
                Console.WriteLine("  standard library: an invented profile measures nothing, and");
                Console.WriteLine("  there is no default path because every workshop keeps");
                Console.WriteLine("  its own library where it wants.");
                Console.WriteLine();
                Console.WriteLine("      setx PROFILI_SALDATURA \"<la cartella dei .SLDLFP>\"");
                Console.WriteLine();
                Console.WriteLine("  Then reopen the terminal: `setx` does not touch the one already open.");
                return 2;   // bench error, not a red check
            }

            // `--libreria <percorso>` before everything else: if the library
            // is another one, the measurement is another one too, and it must be said at the head.
            var arguments = new List<string>(lineArguments ?? new string[0]);
            int location = arguments.IndexOf("--libreria");
            if (location >= 0 && location + 1 < arguments.Count)
            {
                _library = arguments[location + 1];
                // ONLY A DIFFERENT FILE IS «ANOTHER LIBRARY» (1/10/2026). bench.ps1
                // always passes --libreria, with the same path as
                // DefaultLibrary, so taking every --libreria for another
                // library skipped the reflection-log test on every full bench
                // (seen on the bench of 0.148.0, where the test was reported as
                // skipped). A net that never runs is not a net.
                _anotherLibrary = !SameLibraryPath(_library, DefaultLibrary());
                arguments.RemoveRange(location, 2);
            }
            // `--lento`: the checks that cost MINUTES, not seconds.
            //
            // The ceiling on features (E12) must be tested on a part with more
            // than eighty features, and eighty-five cuts cost ~26 seconds
            // each: half an hour for a single check, against the thirteen minutes of
            // all the others together. On every run it would be a price that nobody
            // pays gladly, and a check that weighs too much is a check that
            // sooner or later somebody skips.
            //
            // BUT A SKIPPED CHECK MUST BE SEEN. When it is not launched, the
            // bench SAYS so and counts it among the skipped: a check that disappears
            // in silence is like a check that never fires.
            _slow = arguments.Remove("--lento");

            // `--muta`: not a run with something extra, ANOTHER RUN.
            // It perturbs the cases and expects them to turn red (entry F6). It costs
            // one SOLIDWORKS document per mutation, so it is launched before
            // a delivery and not every evening.
            _mutate = arguments.Remove("--muta");

            // `--regressione`: the suite of the references (entry F8). Like
            // `--muta`, it is another run: it produces three numbers, not green
            // and red checks, and mixing them would tell nobody anything.
            _regression = arguments.Remove("--regressione");

            // `--fingi-senza-solidworks`: make SOLIDWORKS DIE on command.
            //
            // The sentinel (entry K17) lights up when three new parts
            // in a row are not born — a condition that, in real life,
            // happens after hours and cannot be reproduced. Without a way of
            // provoking it, it would be a guard written and never seen
            // to fire: it is simply believed.
            //
            // With this flag `NewDocument` answers `null` regardless,
            // the bench must notice within three attempts, skip
            // everything else SAYING so, and exit 4. It is launched by hand,
            // takes a few seconds, and serves to see with one's own eyes what
            // the contract checks verify in the abstract.
            _pretendWithoutSolidWorks = arguments.Remove("--fingi-senza-solidworks");

            // `--gruppo <nomi>`: a single family of checks, while working.
            //
            // ── WHY IT EXISTS (entry F13) ─────────────────────────────────
            //
            // A whole run takes 13-17 minutes, and up to 0.40.0 there was
            // no way of launching a piece of it. On 15/9/2026 the bench was
            // launched a dozen times — about three and a half hours of
            // SOLIDWORKS — almost all to re-test a hundred things not touched.
            //
            // ── AND WHY THE WHOLE RUN REMAINS THE DEFAULT ─────────────────
            //
            // Because «I only test the one I changed» does NOT work, and the
            // same day it was shown twice: `center_mm` on the faces
            // of the holes made the 16 KB ceiling be exceeded on a frame of
            // four bars — which has no holes, but has the corner fillets of the
            // profiles — and the cropping of the images changed an assertion
            // on the weight of the PNG. **The bench measures what you broke, not what you
            // did**: that you already know.
            //
            // So: `--gruppo` to get things wrong quickly, the whole run before
            // committing, and a partial run that CANNOT pass for a whole
            // one — the summary says so in plain letters.
            List<string> chosen = null;
            int whereG = arguments.IndexOf("--gruppo");
            if (whereG >= 0)
            {
                if (whereG + 1 >= arguments.Count)
                {
                    Console.WriteLine("«--gruppo» needs a name. " + WhatTheyAreCalled());
                    return 2;
                }
                chosen = new List<string>(
                    arguments[whereG + 1].Split(new[] { ',' },
                                               StringSplitOptions.RemoveEmptyEntries));
                for (int i = 0; i < chosen.Count; i++) chosen[i] = chosen[i].Trim().ToLowerInvariant();
                arguments.RemoveRange(whereG, 2);
            }
            if (arguments.Remove("--gruppi"))
            {
                Console.WriteLine(WhatTheyAreCalled());
                return 0;
            }

            // `--famiglia <nomi>`: inside the «casi» group, only those
            // folders (25/9/2026, entry E19). «casi» alone is the thousand declared
            // cases, three quarters of an hour: to see TWO new cases fail
            // before writing the code — the rule of every new check —
            // it was the whole run or nothing. It implies `--gruppo casi`.
            int whereF = arguments.IndexOf("--famiglia");
            if (whereF >= 0)
            {
                if (whereF + 1 >= arguments.Count)
                {
                    Console.WriteLine("«--famiglia» needs a name: " + string.Join(", ", Families));
                    return 2;
                }
                _familiesOnly = new List<string>(
                    arguments[whereF + 1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(n => n.Trim().ToLowerInvariant()));
                arguments.RemoveRange(whereF, 2);
                var unknownOnes = _familiesOnly.Where(n => !Families.Contains(n)).ToList();
                if (unknownOnes.Count > 0)
                {
                    Console.WriteLine("I do not know the family " +
                                      string.Join(", ", unknownOnes.Select(n => "«" + n + "»")) +
                                      ". The families are: " + string.Join(", ", Families));
                    return 2;
                }
                if (chosen == null) chosen = new List<string> { "casi" };
            }

            lineArguments = arguments.ToArray();

            // A WRONG NAME IS SAID AT ONCE, before attaching to SOLIDWORKS.
            // A typo that is discovered after thirty seconds of startup, and that leaves
            // one believing «that group has no checks», is worse than no group.
            if (chosen != null)
            {
                var known = new HashSet<string>(
                    Groups().Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
                var unknown = chosen.Where(n => !known.Contains(n)).ToList();
                if (unknown.Count > 0)
                {
                    Console.WriteLine("I do not know " +
                                      string.Join(", ", unknown.Select(n => "«" + n + "»")) +
                                      ". " + WhatTheyAreCalled());
                    return 2;
                }
            }

            if (!File.Exists(_library))
            {
                Console.WriteLine("cannot find the library: " + _library);
                Console.WriteLine("indicala con  --libreria <percorso di Oce.Solid.Agent.Core.dll>");
                return 1;
            }

            try { _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (Exception ex) { Console.WriteLine("cannot attach: " + ex.Message); return 1; }

            // THE BYTES ARE READ AND LOADED FROM THOSE.
            // `Assembly.LoadFrom` on a network path (S:) answers
            // NotSupportedException, and the library is right there. Reading the
            // file and passing the bytes goes around the problem without asking
            // anybody to copy things by hand.
            _addin = Assembly.Load(File.ReadAllBytes(_library));
            Console.WriteLine("SolidWorks " + _sw.RevisionNumber());
            Console.WriteLine("libreria " + _addin.GetName().Version + "  (" + _library + ")");
            // ── THE LIBRARY DOES NOT LOAD IS NOT A RED RUN (entry F10) ─
            //
            // Pointing `--libreria` to a historical version — what is needed to
            // show that a check really guards its defect — the
            // types may not be there: before 0.52.0 the operations
            // were in `OpenPDM.SwAddin`, with another namespace.
            //
            // Without this net the bench exited with an unhandled exception and
            // the code -532462766, which in a log is indistinguishable from a
            // stuck process. A historical comparison that gives «red everywhere» means
            // nothing, and this is its limit case.
            try
            {
                _tOperations = _addin.GetType(NS + "SwOperations", true);
                _tState = _addin.GetType(NS + "DocumentState", true);
                _tOutcome = _addin.GetType(NS + "SwOperations+Outcome", true);
                // The Italian selectors of before W3.1-bis.5, read through the
                // dictionary of the library (Alias.cs); a library from before
                // it has none, and the selectors are read as written.
                var tAlias = _addin.GetType(NS + "Alias", false);
                var selectorMethod = tAlias == null ? null
                    : tAlias.GetMethod("Selector", new[] { typeof(string), typeof(List<string>) });
                if (selectorMethod != null)
                    Expectations.SelectorAlias = (s, notes) => (string)selectorMethod.Invoke(null, new object[] { s, notes });
                var usesProperty = tAlias == null ? null
                    : tAlias.GetProperty("Uses", BindingFlags.Public | BindingFlags.Static);
                if (usesProperty != null)
                {
                    Expectations.AliasUsesNow = () => (int)usesProperty.GetValue(null, null);
                    AliasUsesNow();   // the start of the count of the run
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("THE LIBRARY DOES NOT EXPOSE THE TYPES THE BENCH LOOKS FOR.");
                Console.WriteLine("  libreria: " + _addin.GetName().Version + "  (" + _library + ")");
                Console.WriteLine("  cercavo:  " + NS + "SwOperations");
                Console.WriteLine("  " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine();
                Console.WriteLine("It is not a red run: it is a library the bench cannot read.");
                Console.WriteLine("The historical comparison starts at library 0.150.1 (rename window 3, decision D11): before it " +
                                  "the library had Italian names, and before 0.52.0 the operations lived in «OpenPDM.SwAddin».");
                return 2;
            }

            // HOW MANY OPERATIONS, said here and not elsewhere: the report (entry F2)
            // wants it in the heading, and this is the only point at which the
            // library to measure is already in hand.
            //
            // AND THEY ARE TWO NUMBERS (entry F15). `ExecutableNames` are all those
            // that the library knows how to execute, including the `Provata = false` ones that
            // `build.ps1` keeps out of `catalog.json`. Up to 0.112.0 it
            // printed «catalogo: 73» and the served catalog had 71: on 24/9
            // an agent explained the difference with «the two copies of the
            // library diverge» — a true theme of the project, applied in the
            // wrong place. A label that says something the number is not
            // produces plausible and false explanations. Now both
            // come out with their name, both counted from the loaded library.
            try
            {
                // Through the reflection log: these fall back to null, and the
                // header would print a count of zero in silence (W3.1-bis.2, item 3).
                var names = ReflectionLog.Global.Field(_tOperations, "ExecutableNames",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string[];
                int inCatalog = -1;
                var catalogueType = ReflectionLog.Global.TypeIn(_addin, "Oce.Solid.Agent.Core.Catalog.Operations");
                var all = ReflectionLog.Global.Field(catalogueType, "All", BindingFlags.Public | BindingFlags.Static)?
                    .GetValue(null) as System.Collections.IEnumerable;
                if (all != null)
                {
                    inCatalog = 0;
                    foreach (var o in all)
                        if (ReflectionLog.Global.Field(o.GetType(), "Tested")?.GetValue(o) as bool? == true) inCatalog++;
                }
                if (names != null)
                    Console.WriteLine("runnable: " + names.Length + " operations (in catalog: " +
                                      (inCatalog < 0 ? "?" : inCatalog.ToString()) + ")");
            }
            catch { }

            // SESSION MODE: instead of the checks, the complete run — a
            // document kept open while somebody else plans.
            if (lineArguments != null && lineArguments.Length >= 2 &&
                lineArguments[0] == "--sessione")
            {
                // Optional third argument: an already made part to
                // pick up, instead of an empty part.
                string toOpen = lineArguments.Length >= 3 ? lineArguments[2] : null;
                return Session(lineArguments[1], toOpen);
            }

            // ── SOLIDWORKS MUST BE CLEAN BEFORE, NOT AFTER ───────────────
            if (!TheDeskIsClean(lineArguments)) return 3;

            // The journal is opened HERE and not before: attaching to SOLIDWORKS and
            // loading the library can make the program exit, and a
            // journal opened for a run that never began would say
            // «stuck at the first step» when in fact it did not start at all.
            OpenLog();

            // THE GUARDIAN IS LIT HERE, and not before: before this line
            // the bench can still exit on its own (library missing, SOLIDWORKS
            // cannot be attached to, dirty desk) and a guardian lit on a
            // run that never began watches nothing.
            //
            // And it is lit ALWAYS, even with `--gruppo`: the call that does not
            // return does not look at which subset was chosen.
            TurnOnGuardian();
            Log("SolidWorks " + _sw.RevisionNumber() +
                   ", library " + _addin.GetName().Version +
                   (_slow ? ", with --lento" : "") +
                   (chosen == null ? "" : ", ONLY the groups " + string.Join(", ", chosen)));

            // ── `--muta` IS A RUN OF ITS OWN ─────────────────────────────
            //
            // It is not added to the checks: it replaces them. Mixing it would mean
            // adding hundreds of SOUGHT reds to a count that serves
            // to read the unsought reds.
            if (_mutate || _regression)
            {
                if (_mutate) MutationsRun();
                else RegressionRun();
                ReflectionLogAsBenchError();
                Console.WriteLine();
                Console.WriteLine(new string('=', 76));
                foreach (var r in _outcomes) Console.WriteLine(r);
                Console.WriteLine();
                var mutateOnly = _mutate
                    ? "MUTATIONS — " + (_outcomes.Count - _skipped - _benchErrors) +
                      " cases, " + _failed + " blind" +
                      (_benchErrors > 0 ? ", " + _benchErrors + " BENCH ERRORS" : "")
                    : "REGRESSION — " + (_outcomes.Count - _skipped - _benchErrors) +
                      " rows, " + _failed + " failed" +
                      (_benchErrors > 0 ? ", " + _benchErrors + " BENCH ERRORS" : "");
                Console.WriteLine(mutateOnly);
                Console.WriteLine("expectations verified " + _verifiedExpectations + " of " + _expectations);
                // No identity test here: it would count as one more case in
                // the line above. The ledger still says how many are unresolved.
                WriteTheTestLedger(_mutate ? "mutations" : "regression");
                CloseLog(mutateOnly);
                if (_benchErrors > 0) return 2;
                return _failed == 0 ? 0 : 1;
            }

            var notLaunched = new List<string>();
            var groupsDone = new List<string>();
            bool stoppedBySentinel = false;
            foreach (var theGroup in Groups())
            {
                if (chosen != null &&
                    !chosen.Contains(theGroup.Name, StringComparer.OrdinalIgnoreCase))
                {
                    notLaunched.Add(theGroup.Name);
                    continue;
                }
                if (theGroup.SlowOnly && !_slow)
                {
                    Skipped(theGroup.What, theGroup.WhyItWeighs ?? "run it with --lento");
                    continue;
                }
                // FROM HERE ON THE CHECKS BELONG TO THIS GROUP (entry K2).
                // It is raised here and not inside `Run()` because the groups are
                // eighteen different methods and asking each of them to remember it
                // means that sooner or later one forgets — and a group
                // that does not declare itself ends up in «(fuori gruppo)», which is
                // visible, but later.
                _groupInProgress = theGroup.Name;
                try { theGroup.Run(); }
                catch (Shared.PreparationFailed ex)
                {
                    // ── THE HOLE IN K17, FOUND ON 24/9/2026 ─────────────
                    //
                    // Here there was a `try/finally` **without `catch`**. Most
                    // groups catch `PreparationFailed`
                    // per single check and mark it red; but a group that
                    // raises it WHILE PREPARING — `PartView`, which makes itself the
                    // part before measuring — let it pass through here,
                    // and `Main` died.
                    //
                    // The result was the exact opposite of what K17
                    // promised: instead of «I stop, here is why, I exit 4»,
                    // a **stack trace** and a dead process. No
                    // report on disk, hence no proof of what
                    // had happened — and the exit code was 0xE0434352, which means
                    // nothing to anybody.
                    //
                    // Found by launching `--fingi-senza-solidworks`, that is
                    // by exercising the guard on purpose. *A guard that has never been
                    // seen to fire ALL THE WAY is half tested,
                    // and the half that is missing is always the one that is needed.*
                    Skipped(theGroup.What, "PreparationFailed: " + ex.Message);
                    _groupInProgress = null;

                    // The rest is not even tested: SOLIDWORKS no longer produces
                    // documents, and every group that follows would ask for a
                    // new part. They are named by name, like the ones not launched.
                    foreach (var g in Groups())
                        if (!ReferenceEquals(g, theGroup) &&
                            !notLaunched.Contains(g.Name) &&
                            Array.IndexOf(groupsDone.ToArray(), g.Name) < 0)
                            Skipped(g.What, "SOLIDWORKS went away before this group");

                    stoppedBySentinel = true;
                    break;
                }
                finally { _groupInProgress = null; }
                groupsDone.Add(theGroup.Name);
            }

            Console.WriteLine();
            Console.WriteLine(new string('=', 76));
            foreach (var r in _outcomes) Console.WriteLine(r);
            Console.WriteLine();

            // THE GROUPS NOT LAUNCHED ARE NAMED, like the skipped ones.
            //
            // «8 prove, 0 fallite» on a partial run is true and makes one believe a
            // false thing, exactly as «88 prove, 0 fallite» was with a
            // check not launched. The defense is in the summary line, not
            // in the discipline of whoever launches: late in the evening nobody remembers
            // having limited themselves to one group.
            if (notLaunched.Count > 0)
            {
                Console.WriteLine("NOT LAUNCHED — " + notLaunched.Count + " groups out of " +
                                  Groups().Count + ": " + string.Join(", ", notLaunched));
                Console.WriteLine("the whole run is launched without «--gruppo», " +
                                  "and that is the one that counts before committing.");
            }

            // THE SKIPPED ARE COUNTED SEPARATELY AND SAID. «88 prove, 0 fallite»
            // on a run in which a check was not launched is a true
            // sentence that makes one believe a false thing.
            //
            // THE COUNT PER GROUP IS CHECKED BEFORE THE SUMMARY, and not
            // after: that check can raise a red, and a red raised
            // after the summary has already counted the reds ends up neither in the
            // summary nor in the exit code. The first draft called it
            // at the end, and it would have been a check that could not make anything
            // fail: the worst form, because it reads like a
            // check.
            CheckCountPerGroup();
            CheckTheTestIdentities();
            CheckTheReflectionLog();
            WriteAliasUses();

            var summary =
                (notLaunched.Count > 0
                    ? "PARTIAL RUN (only " + string.Join(", ", chosen) + ", " +
                      notLaunched.Count + " groups not launched) — "
                    : "") +
                (_outcomes.Count - _skipped - _benchErrors) + " prove, " + _failed + " fallite" +
                (_skipped > 0 ? ", " + _skipped + " SKIPPED (--lento runs them)" : "") +
                (_benchErrors > 0 ? ", " + _benchErrors + " BENCH ERRORS" : "");
            Console.WriteLine(summary);

            // EXPECTATIONS VERIFIED N OUT OF M. It is ALWAYS written, even when the two
            // numbers match: it is the number that says whether the suite is
            // measuring, and a number that appears only when things go wrong
            // is looked at by nobody until it is too late.
            var count = "expectations verified " + _verifiedExpectations + " of " + _expectations;
            Console.WriteLine(count);

            // ── AND HOW MANY CHECKS HAVE NO EXPECTATION AT ALL (entry K3) ────────
            //
            // As long as the two numbers were in different columns — 831 passed,
            // 659 expectations verified — the difference was readable only by
            // subtracting by hand, and nobody subtracted it. Written here, with the
            // NAMES, it becomes a list on which one can decide.
            //
            // IT IS NOT A LIST OF CULPRITS. Severe checks end up in it too,
            // which compare in the code instead of in a case file; and the
            // bench's service lines end up in it, which do not test the product
            // but themselves. The point is to be able to look at them one by one
            // instead of believing them all equivalent to a measured expectation.
            Console.WriteLine("checks with a declared expectation: " + _withExpectation +
                              " — without: " + _withoutExpectation.Count);

            // Entry K2-bis. It sits next to the total like the count per group,
            // because it is the same question: of that big number, how much
            // really involves SOLIDWORKS?
            // THE POSTSCRIPT SAID «as long as K12-bis is open», and K12-bis
            // has been closed since 23/9/2026: it was a line that told the world of
            // yesterday, that is exactly what this bench reproaches
            // hand-written numbers with. The count is now a measurement —
            // `OpenDocuments()` asks SOLIDWORKS how many documents there are
            // during each check — and no longer needs excuses.
            Console.WriteLine("checks with a CAD document open: " + _withDocument +
                              " — without: " + _withoutDocument);
            WriteWithoutWaiting();
            WriteCountPerGroup();
            WriteTheTestLedger(notLaunched.Count > 0 || _familiesOnly != null ? "partial" : "full");

            CloseLog(summary + " · " + count);

            // ── FOUR EXITS, AND 4 WINS OVER ALL OF THEM (entry K17) ─────
            //
            // If SOLIDWORKS went away mid-run, «how many checks
            // failed» and «how many I could not measure» are both
            // the wrong question: the run WAS NOT DONE. Saying it
            // with a code of its own prevents whoever reads a 1 from starting to
            // look for what broke.
            if (Sentinel.HasGone)
            {
                Console.WriteLine();
                Console.WriteLine(new string('!', 76));
                if (stoppedBySentinel)
                    Console.WriteLine(
                        "THE RUN WAS INTERRUPTED HALFWAY: a group could not " +
                        "even get ready, and the following ones were not " +
                        "tried.");
                Console.WriteLine(Sentinel.Report());
                Console.WriteLine("INVALID RUN: the " + _skipped +
                                  " skipped cases are neither green nor red.");
                Console.WriteLine(new string('!', 76));
                return 4;
            }
            // THREE EXITS, AND 2 WINS OVER 1. If the bench could not
            // measure, how many checks failed is a question that has not been
            // answered.
            if (_benchErrors > 0) return 2;
            return _failed == 0 ? 0 : 1;
        }

        // ══════════════════════════════════════════════════════════════════
        // THE GROUPS (entry F13)
        //
        // THE ORDER IS THE SAME AS BEFORE, and it is not a detail: some checks
        // leave the document in a state that the next one expects, and
        // reordering them to tidy up would be the most silent way of
        // breaking the bench. This table is the list of before, with a name
        // in front of every line.
        // ══════════════════════════════════════════════════════════════════
        private sealed class Group
        {
            public string Name;      // how it is written after --gruppo
            public string What;      // what it is called when it is said that it was skipped
            public bool SlowOnly;
            public string WhyItWeighs;   // what is said when --lento is not there

            /// <summary>
            /// THE CHECKS OF THE GROUP, ONE BY ONE and not a lambda that
            /// calls them. A lambda would hide the real methods behind its own, and
            /// `TestThatTheTableCoversEverything` — the check that no
            /// family is left out — would no longer see them: it would look among the
            /// methods generated by the compiler and say yes to everything.
            /// </summary>
            public Action[] Actions;

            public void Run() { foreach (var a in Actions) a(); }
        }

        private static List<Group> Groups()
        {
            Func<Action, Action[]> one = a => new[] { a };
            return new List<Group>
            {
                new Group { Name = "sezioni",   What = "Services.SectionProfile",      Actions = one(Sections) },
                new Group { Name = "archivio",  What = "Services.SolidWorksMaterials", Actions = one(Materials) },
                new Group { Name = "telai",     What = "rectangular_frame",          Actions = one(Frames) },
                new Group { Name = "fori",      What = "hole_wizard",                 Actions = one(Holes) },
                new Group { Name = "materiale", What = "apply_material",            Actions = one(Material) },
                new Group { Name = "stato",     What = "DocumentState",               Actions = one(State) },
                new Group { Name = "viste",     What = "PartView (V1)",           Actions = one(TestSnapshot) },
                new Group { Name = "tetto",     What = "the ceiling on features (E12)",
                             Actions = one(TestFeatureCap), SlowOnly = true,
                             WhyItWeighs = "85 features, about half an hour: run with --lento" },

                // Those that were inside `NuoveOperazioni()`.
                new Group { Name = "scatto",       What = "take the view shot",        Actions = one(CaptureView) },
                new Group { Name = "quanto-vede",  What = "what the model sees", Actions = one(HowMuchTheModelSees) },
                new Group { Name = "facce",        What = "the faces of the plate", Actions = one(PrintPlateFaces) },
                new Group { Name = "sposta-corpi", What = "move_bodies",           Actions = one(TestMoveBodies) },
                // «slot» is no longer there: it became fifteen files in
                // `cases/slot/`, and it is launched with `--gruppo casi` (entry F11).
                // «smusso» is no longer there: it became three files in
                // `cases/chamfer/`, and it is launched with `--gruppo casi` (entry F11).
                // «revolve» is no longer there: it became five files in
                // `cases/revolve/`. The tolerance is the difference: the C#
                // accepted `atteso * 0.02`, which on the cylinder of 62,831 mm³
                // is 1,256 mm³ of allowed slack.
                // «shell» is no longer there: it became two files in
                // `cases/features/`, and it is launched with `--gruppo casi`.
                // «crea-piano» is no longer there: it became three files in
                // `cases/plane/`, and the expectation is on WHERE THE PLANE ENDED UP
                // (`last_plane_mm`), not on the distance that the bridge
                // copies.
                // «crea-asse» stayed in C# for ONE line only: that the axis
                // re-attaches with SelectByID2("AXIS"), which is a COM
                // call and not a catalog operation. All the rest of the
                // family is in `cases/axis/`.
                new Group { Name = "crea-asse",    What = "the axis is found again by name", Actions = one(TestCreateAxis) },
                // THE DIMENSION WITHOUT AN EDGE (entry Z-A2): the red that a declared
                // case does not know how to build, because after `create_drawing` it does not
                // go back to the part to suppress.
                new Group { Name = "orfane",       What = "the dimension left without an edge (Z-A2)", Actions = one(TestOrphanDimensions) },
                // THE PRINTED NUMBER (entry Z-C2): no catalog operation
                // overwrites a dimension, so the falsification is done here.
                new Group { Name = "numeri",       What = "the printed number is the model's (Z-C2)", Actions = one(TestDimensionNumbers) },
                // THE CLIENT'S DOOR (entry F14): it does not call the library,
                // it talks to the bridge over stdio as an MCP client would.
                new Group { Name = "porta-del-cliente",
                             What = "the cases that go through the bridge (F14)",
                             Actions = one(TheClientPort) },
                // «ripeti», «mirror» and «togliere» are no longer there: they
                // became `cases/patterns/`, `cases/mirror/` and
                // `cases/removal/`, and they are launched with `--gruppo casi`.
                // «shape» is no longer there: it became `cases/sentence/`, since
                // the expectations know how to ask «includes» and «does_not_include».
                new Group { Name = "dove-stava",   What = "where it was, and the empty grep (E10)",
                             Actions = new Action[] { TestWhereItWas, TestThatTheFractionDoesNotDecide } },
                new Group { Name = "casi",         What = "the declared cases, which are files (F3)",
                             Actions = new Action[]
                             {
                                 TestThatSelectorRefuses,
                                 TestDeclaredCases,
                                 // AFTER the cases: it also looks at the count of the
                                 // expectations, which before running them is zero.
                                 TestThatAnAbsentFieldIsNotAZero,
                             } },
                new Group { Name = "il-null-ha-un-nome", What = "the bench says where it failed (F12)",
                             Actions = one(TestThatNullHasAName) },
                // The capability that opens the whole G-A section: without it, no
                // assembly operation could have a case.
                new Group { Name = "assieme-di-prova", What = "the bench can prepare an assembly (GA0)",
                             Actions = one(TestTheTestAssembly) },
                new Group { Name = "tabella",      What = "the table of groups covers everything (F13)",
                             Actions = one(TestThatTheTableCoversEverything) },
            };
        }

        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// THE DEFECT THAT THE GROUPS INTRODUCE, and that gives no error.
        ///
        /// Before entry F13, forgetting to call a new check meant
        /// not adding it to `NuoveOperazioni()`. Now it means not
        /// adding it to the table of the groups — same oversight, same
        /// silence: the bench keeps saying «all adds up» on one check fewer,
        /// and the total count is looked at by nobody from one run to the next.
        ///
        /// Here .NET is asked which check methods really exist and the answer is
        /// compared with the table. The count is made on the `MethodInfo`, not on
        /// hand-written names: a hand-written list would have the same
        /// defect that it is checking.
        ///
        /// ── AND ITS RED ──────────────────────────────────────────────────
        ///
        /// A sieve that finds nothing and a broken one look the same. If the
        /// question to reflection came back empty, «no family outside
        /// the table» would be true for the wrong reason: it is checked
        /// first that it sees the families.
        ///
        /// ── AND THE RED IS NO LONGER A NUMBER ────────────────────────────
        ///
        /// It was `famiglie.Count >= 15`, and on 15/9/2026 it turned red without
        /// anything having broken: entry F11 turned six families
        /// of checks into JSON files, and the methods went down to twelve. **A numeric
        /// floor on something that must shrink is a red with an
        /// expiry date**: whoever meets it raises it or lowers it, and in both
        /// cases has looked at the number instead of the part.
        ///
        /// Now the sieve must find ITSELF. `TestThatTheTableCoversEverything`
        /// is a `private static void` method without parameters that begins with
        /// «Prova»: if reflection does not list it, it does not work — and the thing
        /// does not depend on how many families there are today. The count stays
        /// written next to it as data, not as a threshold.
        /// </summary>
        private static void TestThatTheTableCoversEverything()
        {
            Console.WriteLine();
            Console.WriteLine("=== the table of groups covers everything (F13) ===");

            var families = typeof(Program)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Where(m => m.Name.StartsWith("Test", StringComparison.Ordinal) &&
                            m.ReturnType == typeof(void) &&
                            m.GetParameters().Length == 0)
                .ToList();

            // THE SIEVE FINDS ITSELF, or it does not work.
            bool seesMe = families.Any(m => m.Name == "TestThatTheTableCoversEverything");
            Check("F13 RED: the sieve sees the families of checks", seesMe,
                  families.Count + " «Test…()» methods" +
                  (seesMe ? ", this one included" : ", and this one is NOT there: the reflection does not list it"));

            var groups = Groups();
            var inGroups = new HashSet<MethodInfo>(groups.SelectMany(g => g.Actions).Select(a => a.Method));
            var outside = families.Where(m => !inGroups.Contains(m)).Select(m => m.Name).ToList();
            Check("F13: no family of checks outside the table",
                  outside.Count == 0,
                  outside.Count == 0 ? groups.Count + " gruppi" : string.Join(", ", outside));

            // TWO GROUPS WITH THE SAME NAME: `--gruppo x` would launch two and
            // the count of the not-launched would say one thing and the run another.
            var doubled = groups.GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                              .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Check("F13: the group names are all different",
                  doubled.Count == 0,
                  doubled.Count == 0 ? "" : string.Join(", ", doubled));
        }

        /// <summary>
        /// The names of the groups, for whoever wrote a wrong one or does not know
        /// which ones there are. It is built from the same table that launches them: a
        /// hand-written list next to it ages at the first new group.
        /// </summary>
        private static string WhatTheyAreCalled()
        {
            return "The groups are: " +
                   string.Join(", ", Groups().Select(g => g.Name)) +
                   ". Without «--gruppo» all are run, and that is what counts " +
                   "before committing.";
        }

        /// <summary>
        /// The name, in columns, **and always at least one space after**.
        ///
        /// `PadRight(52)` alone adds nothing when the name is already
        /// 52 long, and the journal came out like this:
        ///
        ///     OK   ...misure.copie_fattemisure.copie_fatte 3 (atteso 3)
        ///
        /// Two words stuck together that look like a single word. It is not an
        /// aesthetic detail: the journal is what one reads when the bench
        /// is red, and a line that reads badly is read wrong — that
        /// doubled name looks like a defect of the bench, and one goes to look for it.
        /// </summary>
        private static string InColumn(string itemName)
        {
            if (itemName == null) itemName = "";
            return itemName.Length >= 52 ? itemName + "  " : itemName.PadRight(52);
        }

        // THE CALLER INFORMATION IS THE IDENTITY OF THE TEST (entry W3.1-bis.2,
        // item 7): the compiler fills file, member and line at every call, and
        // the test ledger turns them into File.cs:Method:ordinal. The four
        // recording methods (this one, `CheckExpectation`, `Skipped`, `BenchError`)
        // carry the attributes; nobody passes these arguments by hand.
        private static void Check(string itemName, bool ok, string detail = "",
                                  [CallerFilePath] string callerFile = "",
                                  [CallerMemberName] string callerMember = "",
                                  [CallerLineNumber] int callerLine = 0)
        {
            RecordOutcome(itemName, ok, detail, callerFile, callerMember, callerLine);
        }

        /// <summary>
        /// The body of `Check`, with the caller information passed on:
        /// `CheckExpectation` records the test of ITS caller, and going through
        /// `Check` would name every expectation after `CheckExpectation` itself.
        /// </summary>
        private static void RecordOutcome(string itemName, bool ok, string detail,
                                          string callerFile, string callerMember, int callerLine)
        {
            // QUIET: the `--muta` mode re-runs the same case dozens of times and
            // EXPECTS it to turn red (entry F6). Those reds are not
            // regressions — they are the result that was sought — and ending up
            // in the list of outcomes would make them be read as such.
            if (_quiet) { if (!ok) _quietRed = true; return; }

            if (!ok) _failed++;

            // ── WITH OR WITHOUT A DECLARED EXPECTATION (entry K3) ────────
            //
            // `_waitInProgress` is raised by whoever has just MEASURED AND COMPARED
            // a quantity declared in a case: the expectation evaluator
            // and the regression one. All the other checks — those
            // written in C# — pass through here with the flag lowered.
            //
            // THE DIFFERENCE IS NOT «GOOD AND BAD». Many checks in C#
            // compare all right, and some are the most severe of the suite.
            // But a part compares nothing: it calls, does not blow up, and
            // ends green. *A check that passes without an assertion does not
            // demonstrate a behavior: it demonstrates that it did not blow up*, and
            // as long as the two things add up into a single number — 831 passed
            // against 659 expectations verified — that number does not say how much the
            // suite is measuring.
            //
            // Here they are separated and the NAMES of those without are written. Then
            // it is decided, entry by entry, whether to close them or declare them: it is a
            // decision that can be taken only by having the list in front.
            if (_waitInProgress) _withExpectation++;
            else _withoutExpectation.Add(itemName);

            // ── WHICH GROUP THIS CHECK BELONGS TO (entry K2) ─────────────
            //
            // «831 checks on real SOLIDWORKS» is a number that covers less
            // ground than it seems, and contrasting it with somebody puts in the
            // same count checks that do not even open a CAD document.
            // It is not a lie: it is a big number that reassures in
            // proportion to how big it is, not to how pertinent it is.
            //
            // The count per group is written next to the total. The total
            // stays — it serves to see whether the suite grows — but it stops being
            // the only number that is read.
            var key = _groupInProgress ?? "(no group)";
            int howMany;
            _perGroup.TryGetValue(key, out howMany);
            _perGroup[key] = howMany + 1;

            // ── AND HOW MANY HAVE A CAD DOCUMENT UNDERNEATH (entry K2-bis) ──
            //
            // The count per group said WHERE the checks are; this says
            // how many of them really have SOLIDWORKS in the middle. Inside the
            // `casi` group — which alone gathers the great majority of them —
            // there are both the declared cases, which do open a document, and the
            // checks on the expectation evaluator, which compare dictionaries
            // and open nothing.
            //
            // SOLIDWORKS IS ASKED, NOT THE BENCH. The first draft counted
            // openings and closings with an internal counter, and would have been
            // wrong: documents are opened in at least four places
            // (`NewPart`, `NewAssembly`, `Open`, the session) and closed
            // in three, one of which tries TWICE on purpose. A counter with
            // seven guardians is a counter that one day will say -1.
            // `GetDocumentCount()` is the same question asked of whoever has the
            // answer.
            //
            // WHAT IT REALLY MEASURES, said here and not discovered afterwards: «there was a
            // document open when this check was recorded». Since
            // the bench starts only on a clean desk (entry K12)
            // that document is necessarily one that it opened. But if a case
            // forgets to close (entry K12-bis), the following checks
            // show up as «with document» even though they did not touch the CAD.
            // **It is a limit, and it must be read together with K12-bis**: as long as
            // that one is open, this number is a ceiling, not a measurement.
            if (OpenDocuments() > 0) _withDocument++; else _withoutDocument++;

            var line = (ok ? "  OK   " : "  NO   ") + InColumn(itemName) + detail;
            _outcomes.Add(line);
            Console.WriteLine(line);
            Log(line);
            Ledger.Record(callerFile, callerMember, callerLine,
                          ok ? TestLedger.Ok : TestLedger.Failed, itemName, _groupInProgress);
        }

        /// <summary>
        /// Raised by whoever is recording the outcome of a DECLARED expectation, for
        /// the time of that single call to `Check`. See entry K3.
        /// </summary>
        internal static bool _waitInProgress;

        internal static int _withExpectation;
        internal static readonly List<string> _withoutExpectation = new List<string>();

        /// <summary>The group that is running now (entry K2).</summary>
        internal static string _groupInProgress;

        /// <summary>Checks recorded with a CAD document open (entry K2-bis).</summary>
        internal static int _withDocument;
        internal static int _withoutDocument;

        /// <summary>
        /// How many documents SOLIDWORKS has open now, or -1 if it is not known.
        ///
        /// The -1 is not a zero in disguise: «I could not ask» and «there
        /// are none» are two different things, and confusing them here would mean
        /// counting as «without document» a run in which the question did not
        /// get through. Whoever reads the number treats the -1 as «I do not know».
        /// </summary>
        internal static int OpenDocuments()
        {
            try { return _sw.GetDocumentCount(); }
            catch { return -1; }
        }

        /// <summary>
        /// How many checks per group. THE SUM MUST MAKE THE TOTAL, and if it
        /// does not the report says so: a count per group that does not match the
        /// total is worse than no count per group, because it reads like
        /// a partition while it is an incomplete partition.
        /// </summary>
        internal static readonly Dictionary<string, int> _perGroup =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// Records the outcome of a declared expectation: like `Check`, but it counts
        /// in the right column. It is here and not in the callers because a flag
        /// raised by hand and forgotten raised falsifies the whole count from
        /// there on — and it would falsify it in silence.
        /// </summary>
        internal static void CheckExpectation(string itemName, bool ok, string detail = "",
                                         [CallerFilePath] string callerFile = "",
                                         [CallerMemberName] string callerMember = "",
                                         [CallerLineNumber] int callerLine = 0)
        {
            _waitInProgress = true;
            try { RecordOutcome(itemName, ok, detail, callerFile, callerMember, callerLine); }
            finally { _waitInProgress = false; }
        }

        /// <summary>
        /// Writes to a file the list of the checks without a declared expectation, and
        /// on screen the first twenty.
        ///
        /// IN A FILE AND NOT ONLY ON SCREEN: they are a hundred and seventy lines at the bottom
        /// of a run of eight hundred, that is exactly the place where nobody
        /// looks. The file instead is opened when one feels like deciding, and
        /// can be compared with the one of the previous run to see whether
        /// the list is getting shorter.
        /// </summary>
        /// <summary>
        /// The count per group, and the check that the sum makes the total.
        ///
        /// WHY THE TOTAL ALONE SAYS LESS THAN IT SEEMS (entry K2).
        /// «831 checks on real SOLIDWORKS» puts in the same number checks that
        /// do not even open a CAD document — the MCP protocol, the
        /// expectation evaluator, the checks on the bench itself. It is not a
        /// lie: it is a number that covers less ground than it seems, and it is
        /// the form of defect this project chases.
        ///
        /// *A big number reassures in proportion to how big it is, not
        /// to how pertinent it is.*
        ///
        /// AND IF THE SUM DOES NOT ADD UP, IT IS SAID. A partition that does not make the
        /// total reads like a partition and it is not: it would lead one to
        /// believe that the rest does not exist, instead of making one look for where it
        /// went.
        /// </summary>
        private static void CheckCountPerGroup()
        {
            if (_perGroup.Count == 0) return;
            int sum = 0;
            foreach (var v in _perGroup.Values) sum += v;
            int total = _withExpectation + _withoutExpectation.Count;
            Check("the count per group adds up to the total", sum == total,
                  sum == total
                      ? sum + " prove in " + _perGroup.Count + " gruppi"
                      : "the sum per group is " + sum + ", the checks are " + total +
                        ": " + Math.Abs(total - sum) + " are not attributed to any " +
                        "group, and a breakdown that does not add up to the total reads as " +
                        "a complete breakdown");
        }

        /// <summary>The lines «group: how many», in order of quantity.</summary>
        private static void WriteCountPerGroup()
        {
            if (_perGroup.Count == 0) return;
            var lines = new List<KeyValuePair<string, int>>(_perGroup);
            lines.Sort((a, b) => b.Value != a.Value
                ? b.Value.CompareTo(a.Value)
                : string.CompareOrdinal(a.Key, b.Key));
            var parts = new List<string>();
            foreach (var r in lines) parts.Add(r.Key + " " + r.Value);
            Console.WriteLine("per group: " + string.Join(", ", parts));
        }

        private static void WriteWithoutWaiting()
        {
            if (_withoutExpectation.Count == 0) return;
            try
            {
                // The bench's folder is derived as for the journal: the
                // binary is in `Bench/bin/`, so the parent is `Bench/`.
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var bench = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                var location = Path.Combine(bench != null ? bench.FullName : bin,
                                        "report", "senza-attesa.txt");
                var lines = new List<string>
                {
                    "# The checks that pass without a DECLARED expectation (entry K3).",
                    "#",
                    "# It is not a list of culprits: many compare in the code instead",
                    "# of in a case file, and some are among the strictest in the suite.",
                    "# Others, however, call, do not blow up, and end up green — and a check",
                    "# that passes without an assertion does not prove a behavior: it proves",
                    "# that it did not blow up.",
                    "#",
                    "# " + _withoutExpectation.Count + " rows, out of " + (_withExpectation + _withoutExpectation.Count) +
                    " checks in all. The run is from " + DateTime.Now.ToString("s") + ".",
                    "",
                };
                lines.AddRange(_withoutExpectation);
                File.WriteAllLines(location, lines);
                Console.WriteLine("   l'elenco: " + location);
            }
            catch (Exception ex)
            {
                Console.WriteLine("   (the list could not be written: " + ex.Message + ")");
            }
        }

        private static bool _quiet;
        private static bool _quietRed;

        // ══════════════════════════════════════════════════════════════════
        // THE JOURNAL: what the bench is doing, while it does it
        //
        // ── THE PROBLEM, AND WHAT IT COST ─────────────────────────────────
        //
        // A run takes from thirteen to seventeen minutes, and from outside **a slow
        // run and a stuck run look the same**. On 14/9/2026 a run
        // blocked on the eighty-fourth cut of `TestFeatureCap` seemed
        // for an hour and a half a run that was working: the only way
        // to tell them apart was to sample the CPU counter of
        // SLDWORKS.exe and look at the modification time of a file.
        //
        // ── AND `ultimo-giro.txt` IS NOT WRITTEN BY THE BENCH ─────────────
        //
        // It must be said because it changes the remedy: the bench prints everything to stdout,
        // and `ultimo-giro.txt` is a REDIRECTION by whoever launches it. So
        // how up to date it is does not depend on the bench but on the shell: with
        // `Tee-Object` it grows line by line, with `Out-String` it appears all
        // together at the end. Adding a `Flush` somewhere inside the
        // bench would have changed nothing — there is no «somewhere»:
        // there was no file.
        //
        // Now there is one **of the bench**, `Bench/in-corso.txt`, written and
        // flushed line by line. `ultimo-giro.txt` stays what it was, and the
        // end-of-run summary does not change by a comma: it is what
        // LEGGIMI documents and what is looked at when the run is over.
        //
        // ── WHY THERE IS A TIME ON EVERY LINE ─────────────────────────────
        //
        // «Where it is at» is seen from the last line. «It is stuck» is not: for
        // that one needs to know **how long** that line has been there. With the time
        // next to it two readings a minute apart are enough, and there is no need
        // any more to look for the modification time of the file — which is the datum
        // that fooled us the other time.
        //
        // ── IT CANNOT MAKE THE RUN FAIL ───────────────────────────────────
        //
        // It is a tool for looking at the checks: if it breaks, the checks
        // must go on all the same. Every write sits in an empty
        // `try`. **But the fact that it could not be opened is said once
        // on stdout**: a diagnostic aid that disappears in silence is itself
        // the defect that this project chases.
        // ══════════════════════════════════════════════════════════════════

        private static StreamWriter _log;
        private static readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        /// <summary>
        /// Opens `Bench/in-corso.txt` next to the executable, one level above
        /// `bin\`. If it does not succeed it says so and goes on without it.
        /// </summary>
        private static void OpenLog()
        {
            string filePath = null;
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var bench = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                filePath = Path.Combine(
                    bench != null ? bench.FullName : bin, "in-corso.txt");

                // IT IS TRUNCATED: inside is the run of NOW. A file that
                // accumulates runs forces whoever reads to ask which line
                // belongs to which run, and it is the question that must not have to be
                // asked by whoever is trying to understand whether it is stuck.
                _log = new StreamWriter(filePath, false) { AutoFlush = true };
                // THE HEADER DOES NOT NAME THE WORD THAT CLOSES THE RUN.
                //
                // The first writing said «if the last line is not FINE...»,
                // and so the first `grep FINE` on this file found
                // the HEADER and answered «run finished» after three seconds.
                // It happened at once, at the first try, to whoever had just
                // written it. Now that word appears in one place only of
                // the whole file, and the most naive check one can make
                // is the right one.
                _log.WriteLine("# the current run, line by line. " +
                                  "The number in parentheses is how many seconds " +
                                  "have passed since the start.");
                _log.WriteLine("# the last line of a run that reached the end " +
                                  "carries the summary; if it is not there, the run " +
                                  "stopped earlier.");
                _log.WriteLine("# if the time of the last line does not grow between " +
                                  "two readings, it is stuck.");
            }
            catch (Exception ex)
            {
                _log = null;
                Console.WriteLine("(no journal" +
                                  (filePath == null ? "" : " in " + filePath) +
                                  ": " + ex.Message + ". The checks go on.)");
            }
        }

        private static void Log(string line)
        {
            if (_log == null) return;
            try
            {
                _log.WriteLine(
                    "[" + _stopwatch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)
                        .PadLeft(7) + "s] " + line);
            }
            catch { }
        }

        /// <summary>
        /// The last line, the one that says that the run reached the bottom.
        ///
        /// It is the half that counts: without it, a file that stops means «it is
        /// working» or «it is dead», and they are indistinguishable again.
        /// </summary>
        private static void CloseLog(string summary)
        {
            if (_log == null) return;
            try
            {
                Log("FINE — " + summary);
                _log.Dispose();
            }
            catch { }
            _log = null;
            WriteToHistory(summary);
        }

        /// <summary>
        /// ONE LINE PER RUN, at the tail of `Bench/storico.txt` (entry F12).
        ///
        /// ── WHY IT IS NEEDED ─────────────────────────────────────────────
        ///
        /// `in-corso.txt` is rewritten by the next run, and `ultimo-giro.txt` is a
        /// redirection by whoever launches: **of the past runs nothing was left.**
        /// So «that check failed just once» could be neither
        /// proved nor disproved — and a check that fails at random and that
        /// nobody accounts for stops being a check: the time it
        /// fails for a real reason, it will be filed away as «the one that every
        /// now and then jumps».
        ///
        /// The failed checks are written BY NAME, because the question asked
        /// of this file is always the same: «how many times has it happened to
        /// THAT one?».
        ///
        /// It is not versioned: it is the register of this machine, not a measurement
        /// to carry around. The count, when it serves to decide, is copied
        /// into the TODO with the date.
        /// </summary>
        private static void WriteToHistory(string summary)
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var bench = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                var file = Path.Combine(bench != null ? bench.FullName : bin, "storico.txt");

                var broken = _outcomes.Where(r => r.StartsWith("  NO   ", StringComparison.Ordinal))
                                  .Select(r => r.Substring(7).Trim())
                                  .Select(r => r.Length > 52 ? r.Substring(0, 52).Trim() : r)
                                  .ToList();

                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                           "  libreria " + (_addin != null ? _addin.GetName().Version.ToString() : "?") +
                           "  " + summary +
                           (broken.Count > 0 ? "  ->  " + string.Join(" | ", broken) : "");

                bool newItem = !File.Exists(file);
                using (var s = new StreamWriter(file, true))
                {
                    if (newItem)
                        s.WriteLine("# one line per bench run. It is used to count the checks " +
                                    "that fail at random: «how many times did it happen to that one?».");
                    s.WriteLine(line);
                }
            }
            catch { }
        }

        /// <summary>
        /// A check that was NOT launched, and why.
        ///
        /// It does not count as failed and does not count as succeeded: it counts as
        /// skipped, and it shows. A check that disappears from the report when it is not
        /// launched is worse than one that does not exist — whoever reads «88 prove, 0
        /// fallite» believes everything has been tested.
        /// </summary>
        private static void Skipped(string itemName, string why,
                                    [CallerFilePath] string callerFile = "",
                                    [CallerMemberName] string callerMember = "",
                                    [CallerLineNumber] int callerLine = 0)
        {
            _skipped++;
            var line = "  --   " + InColumn(itemName) + "SALTATA: " + why;
            _outcomes.Add(line);
            Console.WriteLine(line);
            Log(line);
            Ledger.Record(callerFile, callerMember, callerLine, TestLedger.Skipped, itemName, _groupInProgress);
        }

        /// <summary>
        /// A BENCH ERROR IS NOT A RED CHECK (entry F4).
        ///
        /// A red check says «the part came out different from how it should»:
        /// it is a measurement, and it is information. A bench error says «I
        /// could not measure» — a field that is not in the state, a
        /// selector that does not resolve, a badly written case. Counting them together
        /// means reading «a check failed» when in fact **no check was
        /// done**, and it is the way in which a suite empties itself without
        /// the total count changing.
        ///
        /// For this reason it has a symbol of its own, a count of its own, and **its own exit
        /// code: 2**.
        /// </summary>
        private static void BenchError(string itemName, string what,
                                          [CallerFilePath] string callerFile = "",
                                          [CallerMemberName] string callerMember = "",
                                          [CallerLineNumber] int callerLine = 0)
        {
            // Under `--muta` a mutated case that cannot even be run
            // counts as «it turned red»: the mutation bit all the same.
            if (_quiet) { _quietRed = true; return; }

            _benchErrors++;
            var line = "  !!   " + InColumn(itemName) + "BENCH ERROR: " + what;
            _outcomes.Add(line);
            Console.WriteLine(line);
            Log(line);
            Ledger.Record(callerFile, callerMember, callerLine, TestLedger.BenchError, itemName, _groupInProgress);
        }

        // ══════════════════════════════════════════════════════════════════
        // THE TEST LEDGER (entry W3.1-bis.2, item 7)
        //
        // Every test by identity (File.cs:Method:ordinal), and the expectations
        // checked per case, written to Bench/report/tests.json at the end of
        // the run. The console and the files bench.ps1 reads do not change.
        //
        // LAZY, like the table of the document measures: a static initializer
        // runs before Main installs the resolver of the interop assemblies.
        // The sources of the bench are scanned when the ledger is born, that
        // is at the first test, a few seconds into the run: the identities
        // must be computed on the sources this binary was built from.
        // ══════════════════════════════════════════════════════════════════
        private static TestLedger _ledger;

        private static TestLedger Ledger
        {
            get
            {
                if (_ledger != null) return _ledger;
                _ledger = new TestLedger(
                    new[] { nameof(Check), nameof(CheckExpectation), nameof(Skipped), nameof(BenchError) },
                    ReadBenchSource);
                var folder = BenchFolder();
                if (folder != null)
                    foreach (var f in Directory.GetFiles(folder, "*.cs")) _ledger.ScanSource(f);
                return _ledger;
            }
        }

        /// <summary>The folder of the bench (the parent of bin), or null.</summary>
        private static string BenchFolder()
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var parent = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                return parent == null ? null : parent.FullName;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// The source a test was recorded from: the file of that name in the
        /// bench folder, next to the binary built from it; else the path the
        /// compiler gave, when it exists (a relative one if the build maps its
        /// paths). Null when neither exists, and the ledger says so for each
        /// such test.
        /// </summary>
        private static string ReadBenchSource(string callerFile)
        {
            if (string.IsNullOrEmpty(callerFile)) return null;
            var folder = BenchFolder();
            var here = folder == null ? null : Path.Combine(folder, Path.GetFileName(callerFile));
            if (here != null && File.Exists(here)) return File.ReadAllText(here);
            return File.Exists(callerFile) ? File.ReadAllText(callerFile) : null;
        }

        /// <summary>
        /// A test of its own, before the summary: every test of this run has an
        /// identity. An unresolved one is a source edited after the build, or a
        /// declaration the scan does not understand, and the comparison with
        /// the reference runs would silently lose that test.
        /// </summary>
        private static void CheckTheTestIdentities()
        {
            var unresolved = Ledger.Tests.Where(t => t.Unresolved != null).ToList();
            Check("every test has its identity, file:method:ordinal", unresolved.Count == 0,
                  unresolved.Count == 0
                      ? Ledger.Tests.Count + " tests, each resolved on the sources of this binary"
                      : unresolved.Count + " of " + Ledger.Tests.Count + " unresolved, the first: " +
                        unresolved[0].Id + " (" + unresolved[0].Unresolved + ")");
        }

        /// <summary>
        /// A test of its own, before the summary: every lookup by name that
        /// would fall back to null in silence found its target (Bench/ReflectionLog.cs,
        /// W3.1-bis.2 item 3). Against another library (`--libreria`) it is
        /// skipped and says so: an older library lacks newer names by design.
        /// </summary>
        private static void CheckTheReflectionLog()
        {
            const string Rule = "every lookup by name that falls back to null found its target";
            var verdict = ReflectionLog.Global.Verdict();
            if (_anotherLibrary)
            {
                Skipped(Rule, "another library was named with --libreria" +
                              (verdict == null ? "" : " (" + verdict + ")"));
                return;
            }
            Check(Rule, verdict == null,
                  verdict ?? ReflectionLog.Global.Lookups + " lookups, none null");
        }

        /// <summary>
        /// The same log at the end of a mutation or regression run, where a test
        /// of its own would count as one more case: a miss is a bench error
        /// (the bench could not read what it measures), and nothing is added
        /// when every lookup found its target.
        /// </summary>
        private static void ReflectionLogAsBenchError()
        {
            var verdict = ReflectionLog.Global.Verdict();
            if (verdict != null && !_anotherLibrary)
                BenchError("every lookup by name that falls back to null found its target", verdict);
        }

        /// <summary>
        /// Writes Bench/report/tests.json and says where. A ledger that cannot
        /// be written is said on the console, like the other files of the report.
        /// </summary>
        private static void WriteTheTestLedger(string mode)
        {
            try
            {
                var folder = BenchFolder();
                if (folder == null) { Console.WriteLine("(the test ledger has no folder to go to)"); return; }
                var report = Path.Combine(folder, "report");
                Directory.CreateDirectory(report);
                var file = Path.Combine(report, "tests.json");
                var library = _addin != null ? _addin.GetName().Version.ToString() : "";
                // File.WriteAllText without an encoding writes UTF-8 without a byte order mark.
                File.WriteAllText(file, Ledger.Json(mode, library, DateTime.Now));
                Console.WriteLine("test ledger: " + file + " (" + Ledger.Tests.Count + " tests, " +
                                  Ledger.Cases.Count + " cases, " + Ledger.UnresolvedCount +
                                  " identities unresolved)");
            }
            catch (Exception ex)
            {
                Console.WriteLine("(the test ledger could not be written: " + ex.Message + ")");
            }
        }

        /// <summary>
        /// What a check could not measure. See BenchError.
        /// </summary>
        // `CouldNotMeasure` is in `Expectations.cs`: it is raised by the evaluator, and
        // that file is also compiled by `Oce.Solid.Agent.ContractTests`, where SOLIDWORKS is not there.

        private static int _skipped;
        private static int _benchErrors;

        /// <summary>
        /// EXPECTATIONS ATTEMPTED AND EXPECTATIONS VERIFIED, and if they diverge something is
        /// measuring nothing (entry F4). It is the literal form of the project's
        /// trap: a check that verified a defect stops
        /// verifying it when the field it read is renamed, a
        /// dictionary that does not find the key returns `null`, and `null`
        /// compared with `null` passes.
        /// </summary>
        private static int _expectations;
        private static int _verifiedExpectations;

        private static bool _slow;
        private static bool _mutate;
        private static bool _regression;

        /// <summary>
        /// SOLIDWORKS MUST HAVE NOTHING OPEN WHEN THE RUN BEGINS.
        ///
        /// ── THE DEFECT, PAID FOR TWICE ───────────────────────────────────
        ///
        /// The state of a document lists `documents_open`, so the check
        /// «state: sotto i 16 KB su un telaio da quattro barre» also measures
        /// what is open besides the frame. On 14/9/2026 six parts
        /// left open by a run of `run_plans.js` were worth ~650
        /// characters and took the check from 15,628 to 16,280: **red without
        /// the code having changed**, and half an hour spent hunting for a
        /// regression that was not there.
        ///
        /// On 23/9/2026 it happened again, with fifteen documents open and
        /// a whole run — an hour and a quarter — thrown away.
        ///
        /// **And the remedy existed, and it ran.** `bench.ps1` calls
        /// `CloseLeftovers.exe --chiudi Bench\lavoro` at step 3 of five, since
        /// forever, and says so at the head of the file. What bypassed it was an
        /// `Oce.Solid.Agent.Bench.exe` launched by hand to pass it `--libreria`.
        ///
        /// **THE PROTECTION WAS IN THE SHELL.** Outside `bench.ps1` there was
        /// nothing, and outside `bench.ps1` is exactly where one goes when one is
        /// doing something special — a different library, a single
        /// group, a binary that is already there. *A protection that lives in the shell
        /// is lost at the moment one leaves the shell, which is the moment at
        /// which it is needed most.*
        ///
        /// Now step 3 of `bench.ps1` and this check say the same
        /// thing twice, and that is fine: the first closes in silence what it knows
        /// to be discards, the second refuses to begin if anything
        /// is left.
        ///
        /// ── WHY IT STOPS INSTEAD OF CLOSING ──────────────────────────────
        ///
        /// `CloseDoc` discards unsaved changes. If on this machine
        /// there is a designer with a part open, closing his document
        /// to make the checks run would be real damage done to make a
        /// false red disappear. So the bench stops and says what it found;
        /// `--chiudi-gli-scarti` is the explicit request of whoever knows that they are
        /// discards.
        ///
        /// It exits 3, not 1: a run that did not begin is not a red run,
        /// and calling it red would mean writing a report of a run
        /// that did not take place.
        /// </summary>
        private static bool TheDeskIsClean(string[] lineArguments)
        {
            bool closeItYourself = lineArguments != null &&
                            Array.IndexOf(lineArguments, "--chiudi-gli-scarti") >= 0;

            var names = new List<string>();
            try
            {
                var d = (IModelDoc2)_sw.GetFirstDocument();
                while (d != null)
                {
                    names.Add(d.GetTitle() ?? "(untitled)");
                    d = (IModelDoc2)d.GetNext();
                }
            }
            catch (Exception ex)
            {
                // THE RUN IS NOT STOPPED FOR A CHECK THAT COULD NOT
                // LOOK. It is said, and one goes on: the 16 KB check
                // stays there to catch the worst case.
                Console.WriteLine("could not count the open documents: " + ex.Message);
                return true;
            }

            if (names.Count == 0) return true;

            if (!closeItYourself)
            {
                Console.WriteLine();
                Console.WriteLine("SOLIDWORKS already has " + names.Count +
                                  (names.Count == 1 ? " open document" : " open documents") + ":");
                foreach (var n in names.Take(20)) Console.WriteLine("    " + n);
                if (names.Count > 20) Console.WriteLine("    ... and another " + (names.Count - 20));
                Console.WriteLine();
                Console.WriteLine("The run does NOT start. A document's state lists the documents");
                Console.WriteLine("that are open, so these would make «state: under 16 KB» fail");
                Console.WriteLine("after an hour of checks, with nothing changed in the code.");
                Console.WriteLine();
                Console.WriteLine("  .\\Bench\\bin\\Oce.Solid.Agent.Bench.exe --chiudi-gli-scarti");
                Console.WriteLine();
                Console.WriteLine("closes them and starts. CloseDoc DISCARDS unsaved changes:");
                Console.WriteLine("look at the list above before asking for it.");
                return false;
            }

            int closed = 0;
            foreach (var n in names)
            {
                try { _sw.CloseDoc(n); closed++; }
                catch { }
            }

            // IT IS RECOUNTED. `CloseDoc` returns nothing, and «I called the
            // closing fifteen times» is not «they are closed».
            int remain = 0;
            try
            {
                var d = (IModelDoc2)_sw.GetFirstDocument();
                while (d != null)
                {
                    remain++;
                    d = (IModelDoc2)d.GetNext();
                }
            }
            catch { }

            Console.WriteLine("desk: closed " + closed + " documents out of " + names.Count +
                              ", left over: " + remain);
            if (remain > 0)
            {
                Console.WriteLine("It did not start all the same: those that remain would make");
                Console.WriteLine("«state: under 16 KB» fail. Close them by hand and run again.");
                return false;
            }
            return true;
        }

        // ==================================================================
        // 1. the add-in's section measurer
        // ==================================================================
        private static void Sections()
        {
            Console.WriteLine();
            Console.WriteLine("=== Services.SectionProfile ===");
            var t = _addin.GetType(NS + "SectionProfile", true);
            var m = t.GetMethod("FrameOverhangs",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            foreach (var testCase in new[]
            {
                new object[] { SQUARE_TUBE, "80X80X5", 0, 1, 80.0, 80.0 },
                new object[] { SQUARE_TUBE, "40x40x3", 0, 2, 40.0, 40.0 },
                new object[] { RECT, "80X50X5", 0, 1, 50.0, 50.0 },
                new object[] { RECT, "80X50X5", 0, 2, 80.0, 80.0 },
            })
            {
                var r = m.Invoke(null, new object[]
                {
                    _sw, testCase[0], testCase[1], testCase[2], testCase[3], 0.0,
                    1 /* miter*/, 0.0,
                    (Action<string>)(s => { }),
                }) as double[];
                bool ok = r != null &&
                          Math.Abs(r[0] - (double)testCase[4]) < 0.01 &&
                          Math.Abs(r[1] - (double)testCase[5]) < 0.01;
                Check("overhang " + testCase[1] + " on axes " + testCase[2] + "," + testCase[3], ok,
                      r == null ? "null" : string.Format("measured {0:0.##}/{1:0.##}, expected {2:0.##}/{3:0.##}",
                          r[0], r[1], testCase[4], testCase[5]));
            }
        }

        // ==================================================================
        // 2. the resolution of the material names
        // ==================================================================
        private static void Materials()
        {
            Console.WriteLine();
            Console.WriteLine("=== Services.SolidWorksMaterials ===");
            var t = _addin.GetType(NS + "SolidWorksMaterials", true);
            var all = t.GetMethod("All",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var resolve = t.GetMethod("Resolve",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            var list = all.Invoke(null, new object[] { _sw, (Action<string>)(s => { }) })
                as System.Collections.IList;
            Check("reads the material archives", list != null && list.Count > 100,
                  (list == null ? "null" : list.Count + " materiali"));

            foreach (var testCase in new[]
            {
                new[] { "AISI 304", "AISI 304" },
                new[] { "1.0037 (S235JR)", "1.0037 (S235JR)" },
                new[] { "S235JR", "1.0037 (S235JR)" },
                new[] { "S275JR", "1.0044 (S275JR)" },
                new[] { "S235", null },              // ambiguous: it must refuse
                new[] { "6082", null },              // does not exist: it must refuse
                new[] { "invented steel", null },
            })
            {
                var arguments = new object[] { _sw, testCase[0], "", (Action<string>)(s => { }), null };
                var found = resolve.Invoke(null, arguments);
                string why = arguments[4] as string;
                string itemName = null;
                if (found != null)
                    itemName = found.GetType().GetField("Name").GetValue(found) as string;

                bool ok = testCase[1] == null ? (found == null && !string.IsNullOrEmpty(why))
                                          : (itemName == testCase[1]);
                Check("material «" + testCase[0] + "»", ok,
                      found == null ? ("rifiutato: " + (why ?? "").Substring(0, Math.Min(70, (why ?? "").Length)))
                                      : ("-> " + itemName));
            }
        }

        // ==================================================================
        // 3. the frame with external dimensions, going through the add-in
        // ==================================================================
        private static void Frames()
        {
            Console.WriteLine();
            Console.WriteLine("=== rectangular_frame (add-in) ===");
            Frame("square 80x80x5, 1000x800, front", SQUARE_TUBE, "80X80X5", 1000, 800, "front", true);
            Frame("square 40x40x3, 1000x800, top", SQUARE_TUBE, "40x40x3", 1000, 800, "top", true);
            Frame("rectangular 80x50x5, 1200x600, front", RECT, "80X50X5", 1200, 600, "front", true);
            Frame("square 80x80x5, 600x400, corner on the origin", SQUARE_TUBE, "80X80X5", 600, 400, "front", false);
            // and one that MUST fail: section bigger than the frame
            Frame("square 80x80x5, 60x60 (must fail)", SQUARE_TUBE, "80X80X5", 60, 60, "front", true, mustFail: true);
        }

        private static void Frame(string itemName, string profile, string config,
                                   double L, double W, string plane, bool centered,
                                   bool mustFail = false)
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();
                var arguments = new Dictionary<string, object>
                {
                    ["profile_path"] = profile,
                    ["configuration"] = config,
                    ["length_mm"] = L,
                    ["width_mm"] = W,
                    ["plane"] = plane,
                    ["centered"] = centered,
                    ["corner_treatment"] = "miter",
                    ["segments"] = NominalSegments(L, W, plane, centered),
                    ["frame"] = new Dictionary<string, object>
                    {
                        ["length_mm"] = L,
                        ["width_mm"] = W,
                        ["plane"] = plane,
                        ["centered"] = centered,
                        ["measured_as"] = "overall",
                    },
                };

                string error;
                var measurements = Run(doc, "rectangular_frame", arguments, out error);
                if (mustFail)
                {
                    Check(itemName, measurements == null && !string.IsNullOrEmpty(error),
                          measurements == null ? ("refused: " + Short(error)) : "did NOT refuse");
                    return;
                }
                if (measurements == null) { Check(itemName, false, "fallito: " + Short(error)); return; }

                string boundingBox = measurements.ContainsKey("bounding_box_outer_mm")
                    ? Convert.ToString(measurements["bounding_box_outer_mm"]) : "(not verified)";
                bool verified = measurements.ContainsKey("bounding_box_checked") &&
                                  Convert.ToBoolean(measurements["bounding_box_checked"]);
                // and it is checked here too, independently
                var box = DocumentBox(doc);
                double gu, gv;
                Axes(plane, out int u, out int v);
                gu = box[3 + u] - box[u];
                gv = box[3 + v] - box[v];
                bool ok = verified && Math.Abs(gu - L) < 0.5 && Math.Abs(gv - W) < 0.5;
                Check(itemName, ok, string.Format(
                    "add-in says {0}, measured {1:0.##}x{2:0.##}, overhang {3}, bars {4}",
                    boundingBox, gu, gv,
                    measurements.ContainsKey("overhang_profile_mm") ? measurements["overhang_profile_mm"] : "?",
                    measurements.ContainsKey("bars") ? measurements["bars"] : "?"));
            }
            catch (Exception ex) { Check(itemName, false, Why(ex)); }
            finally { Close(doc); }
        }

        // ==================================================================
        // 4. the hole wizard holes, going through the add-in
        // ==================================================================
        private static void Holes()
        {
            Console.WriteLine();
            Console.WriteLine("=== hole_wizard (add-in) ===");
            Hole("four M10 through-all", "clearance", "M10", true, 0,
                 new[] { new[] { -70.0, -40.0 }, new[] { 70.0, -40.0 },
                         new[] { 70.0, 40.0 }, new[] { -70.0, 40.0 } }, 11.0);
            Hole("one threaded M8 through-all", "threaded", "M8", true, 0,
                 new[] { new[] { 0.0, 0.0 } }, 6.8);
            Hole("one blind threaded M12, 12 deep", "threaded", "M12", false, 12,
                 new[] { new[] { 30.0, 20.0 } }, 10.2);
            Hole("due lamati M8", "counterbore", "M8", true, 0,
                 new[] { new[] { -50.0, 0.0 }, new[] { 50.0, 0.0 } }, 9.0);
            Hole("invented size (must fail)", "threaded", "M7", true, 0,
                 new[] { new[] { 0.0, 0.0 } }, 0, mustFail: true);
            Hole("two holes at the same point (must fail)", "clearance", "M8", true, 0,
                 new[] { new[] { 10.0, 0.0 }, new[] { 10.0, 0.0 } }, 0, mustFail: true);
        }

        private static void Hole(string itemName, string typ, string size, bool throughAll,
                                 double depth, double[][] positions, double expectedDiameter,
                                 bool mustFail = false)
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();
                Plate(doc, 200, 120, 20);
                var docState = ReadState(doc);
                string uid = TopFace(docState);
                if (uid == null) { Check(itemName, false, "no face on top in the state"); return; }

                var arguments = new Dictionary<string, object>
                {
                    ["face_uid"] = uid,
                    ["variant"] = typ,
                    ["size"] = size,
                    ["through_all"] = throughAll,
                    ["positions"] = positions.Select(p => (object)new Dictionary<string, object>
                    {
                        ["x_mm"] = p[0], ["y_mm"] = p[1],
                    }).ToList(),
                };
                if (!throughAll) arguments["depth_mm"] = depth;

                string error;
                var measurements = Run(doc, "hole_wizard", arguments, out error, docState);
                if (mustFail)
                {
                    Check(itemName, measurements == null && !string.IsNullOrEmpty(error),
                          measurements == null ? ("refused: " + Short(error)) : "did NOT refuse");
                    return;
                }
                if (measurements == null) { Check(itemName, false, "fallito: " + Short(error)); return; }

                double d = measurements.ContainsKey("diameter_machined_mm")
                    ? Convert.ToDouble(measurements["diameter_machined_mm"]) : 0;
                // THE KEY WAS ALWAYS THERE, and this branch could not fire.
                // Up to 0.25.1 here there was `misure.ContainsKey(...)`, and the
                // bridge wrote that key unconditionally: `verificate`
                // was true by construction, «POSIZIONI NON VERIFICATE» was
                // dead code, and these checks measured only the diameter
                // believing they measured the positions too.
                // Now the key must be read AND be true, and the bridge writes a
                // DIFFERENT one when it could not look.
                bool verified = measurements.ContainsKey("positions_checked") &&
                                  Convert.ToBoolean(measurements["positions_checked"]);
                string why = measurements.ContainsKey("positions_not_checked")
                    ? Convert.ToString(measurements["positions_not_checked"]) : null;
                bool ok = verified && Math.Abs(d - expectedDiameter) < 0.05;
                Check(itemName, ok, string.Format("diameter {0:0.##} (expected {1:0.##}), holes {2}, {3}",
                    d, expectedDiameter,
                    measurements.ContainsKey("holes") ? measurements["holes"] : "?",
                    verified
                        ? "positions verified"
                        : "POSITIONS NOT VERIFIED" + (why == null ? "" : ": " + Short(why))));
            }
            catch (Exception ex) { Check(itemName, false, Why(ex)); }
            finally { Close(doc); }
        }

        // ==================================================================
        // 5. the material, going through the add-in
        // ==================================================================
        private static void Material()
        {
            Console.WriteLine();
            Console.WriteLine("=== apply_material (add-in) ===");
            foreach (var testCase in new[]
            {
                new object[] { "S235JR", "1.0037 (S235JR)", false },
                new object[] { "AISI 304", "AISI 304", false },
                new object[] { "S235", null, true },
                new object[] { "6082", null, true },
            })
            {
                IModelDoc2 doc = null;
                try
                {
                    doc = NewPart();
                    Plate(doc, 100, 60, 10);
                    string error;
                    var measurements = Run(doc, "apply_material",
                        new Dictionary<string, object> { ["material"] = testCase[0] }, out error);
                    bool mustFail = (bool)testCase[2];
                    if (mustFail)
                    {
                        Check("material «" + testCase[0] + "»",
                              measurements == null && !string.IsNullOrEmpty(error),
                              measurements == null ? ("refused: " + Short(error)) : "did NOT refuse");
                        continue;
                    }
                    if (measurements == null) { Check("material «" + testCase[0] + "»", false, Short(error)); continue; }

                    // and it is re-read from the document, not from the outcome
                    string db;
                    string read = (doc as IPartDoc).GetMaterialPropertyName2("", out db);
                    bool ok = read == (string)testCase[1];
                    // and now is the mass there too?
                    var docState = ReadState(doc);
                    bool hasMass = docState.ContainsKey("mass_kg");
                    Check("material «" + testCase[0] + "»", ok && hasMass,
                          "on the document: «" + read + "»" +
                          (hasMass ? ", mass " + docState["mass_kg"] + " kg" : ", MASS MISSING"));
                }
                catch (Exception ex) { Check("material «" + testCase[0] + "»", false, Why(ex)); }
                finally { Close(doc); }
            }
        }

        // ==================================================================
        // 6. the state of the document
        // ==================================================================
        private static void State()
        {
            Console.WriteLine();
            Console.WriteLine("=== DocumentState ===");
            IModelDoc2 doc = null;
            try
            {
                doc = NewPart();
                var docState = ReadState(doc);
                Check("empty document: explicit null material",
                      docState.ContainsKey("material") && docState["material"] == null);
                Check("empty document: no body",
                      docState.ContainsKey("solid_body") && !Convert.ToBoolean(docState["solid_body"]));
                Check("empty document: no mass (the material is missing)",
                      !docState.ContainsKey("mass_kg"));

                // a real frame
                var arguments = new Dictionary<string, object>
                {
                    ["profile_path"] = SQUARE_TUBE,
                    ["configuration"] = "80X80X5",
                    ["length_mm"] = 1000.0,
                    ["width_mm"] = 800.0,
                    ["plane"] = "front",
                    ["centered"] = true,
                    ["corner_treatment"] = "miter",
                    ["segments"] = NominalSegments(1000, 800, "front", true),
                    ["frame"] = new Dictionary<string, object>
                    {
                        ["length_mm"] = 1000.0, ["width_mm"] = 800.0,
                        ["plane"] = "front", ["centered"] = true, ["measured_as"] = "overall",
                    },
                };
                string error;
                var measurements = Run(doc, "rectangular_frame", arguments, out error);
                if (measurements == null) { Check("frame for the state", false, Short(error)); return; }

                docState = ReadState(doc);
                var bodies = docState.ContainsKey("bodies") ? docState["bodies"] as System.Collections.IList : null;
                Check("frame: four bodies in the state", bodies != null && bodies.Count == 4,
                      bodies == null ? "assenti" : bodies.Count + " corpi");
                if (bodies != null && bodies.Count > 0)
                {
                    var c0 = bodies[0] as Dictionary<string, object>;
                    Check("body: it has uid, bounding box, center and who made it",
                          c0.ContainsKey("uid") && c0.ContainsKey("bounding_box_mm") &&
                          c0.ContainsKey("center_mm") && c0.ContainsKey("created_by"),
                          Line(c0));
                }
                var ing = docState.ContainsKey("bounding_box_mm") ? docState["bounding_box_mm"] as double[] : null;
                Check("part: bounding box 1000x800x80",
                      ing != null && Math.Abs(ing[0] - 1000) < 0.5 &&
                      Math.Abs(ing[1] - 800) < 0.5 && Math.Abs(ing[2] - 80) < 0.5,
                      ing == null ? "assente" : string.Join("x", ing.Select(x => x.ToString("0.##"))));
                Check("part: no mass without material", !docState.ContainsKey("mass_kg"));

                var feature = docState["feature"] as System.Collections.IList;
                var names = feature.Cast<Dictionary<string, object>>()
                    .Select(f => Convert.ToString(f["kind"])).ToList();
                Check("feature: only those that count",
                      names.Contains("WeldMemberFeat") &&
                      !names.Contains("HistoryFolder") && !names.Contains("SolidBodyFolder"),
                      string.Join(", ", names));

                int nFaces = (docState["faces"] as System.Collections.IList).Count;
                Check("faces: ceiling on the total, not per body", nFaces <= 60,
                      nFaces + " facce" +
                      (docState.ContainsKey("faces_total") ? " su " + docState["faces_total"] : ""));
                var f0 = (docState["faces"] as System.Collections.IList)[0] as Dictionary<string, object>;
                Check("face: says which body it belongs to", f0.ContainsKey("body"), Line(f0));

                // THE CEILING IS OURS, NOT SOLIDWORKS'S NOR THE MODEL'S.
                //
                // There is no size limit in the product: in the
                // code there are only ceilings on the NUMBER of elements (60 faces,
                // 60 edges, 80 features, 24 bodies). This line is a
                // guard that we wrote, born when a truncation defect
                // made the message grow with the bars — 11 KB
                // for four, over 50 for twenty.
                //
                // On 10 September 2026 it was raised from 12,000 to 14,000, and
                // here it is written what that increase bought: the real
                // parameters of the features (standard, size, machined diameter,
                // thread, depth, end condition), the diameter of the
                // cylindrical faces, the volume of the part, and the suppressed
                // features. That is the difference between a model that reads
                // «Foro filettato M8-1» and one that reads «M8 ISO, preforo 6,8».
                //
                // Before raising it again: look whether there is a field that
                // REPEATS another field. Three were found in
                // half an hour, for six thousand characters in all.
                int characters = System.Text.Encoding.UTF8.GetByteCount(Json(docState));
                // 14 -> 16 KB on 10 September 2026, and what it bought: the
                // ROLE of the edges. Before, twenty lines with a length and
                // nothing else; now each one says along which axis it runs and between which
                // two faces it lies, that is how one says «the four corners» or «the
                // edge of the top face» without counting millimeters.
                // Measured cost: +1,425 characters on a frame of four
                // bars, +737 on a plate.
                //
                // THIRD TIME THAT THIS LINE GOES UP. If a fourth is needed, the
                // right question is not «how much do I raise» but «are sixty edges
                // really needed?»: on a weldment they are noise, and the ceiling
                // on the NUMBER is the only one that cannot be raised forever.
                // THIS CHECK ALSO MEASURES WHAT IT SHOULD NOT, and it must be said
                // in the message instead of being discovered every time.
                //
                // The state lists `documents_open`, so the measurement depends
                // on what is open in SOLIDWORKS besides the frame. Measured
                // on 14/9/2026: six parts saved by a run of `run_plans.js`
                // left open are worth ~650 characters, and take the check from
                // 15,628 to 16,280 — that is they make it FAIL without the code
                // having changed. Half an hour spent hunting for a regression that
                // was not there.
                //
                // `ChiudiScarti --chiudi` alone does not close them: it spares
                // on purpose the documents with a file. The folder is needed:
                //   CloseLeftovers.exe --chiudi "...\Bench\lavoro"
                //
                // The number is printed ALWAYS, not only when it fails: a
                // message that says the criterion serves to read the greens too.
                int open = 0;
                try
                {
                    var list = docState.ContainsKey("documents_open")
                        ? docState["documents_open"] as System.Collections.IList : null;
                    if (list != null) open = list.Count;
                }
                catch { }

                // ── THE MARGIN IS SAID, AND IT IS DEMANDED (entry K13) ─────────
                //
                // «Passed» at seventy-eight characters from the wall is not
                // warning anybody: the next addition of any kind makes it
                // red, and it will arrive as if it were a regression. On 23/9
                // the ceiling was already exceeded in the normal configuration of
                // whoever works — 16,089 with two documents open — and this
                // check did not see it because the bench runs on a clean
                // desk.
                //
                // So the criterion is no longer «under the ceiling», it is «under
                // the ceiling WITH A MARGIN», and the margin is always printed: even
                // on the greens, which is the only moment at which one can see
                // the wall coming.
                const int Cap = 16000;
                const int MinimumMarginPercent = 15;
                int threshold = Cap - Cap * MinimumMarginPercent / 100;
                int margin = Cap - characters;
                int percent = (int)Math.Round(margin * 100.0 / Cap);

                Check("state: under 16 KB with at least 15% margin",
                      characters <= threshold,
                      characters + " characters, margin " + margin + " (" + percent +
                      "%), " + open +
                      (open == 1 ? " open document" : " open documents") +
                      (open > 1 ? " (more than one inflates the measure: see above)" : ""));
            }
            catch (Exception ex) { Check("document state", false, Why(ex)); }
            finally { Close(doc); }
        }

        // ==================================================================
        //  The ceiling on features keeps the LAST ones (entry E12)
        // ==================================================================
        //
        // WHY HERE AND NOT IN A BRIDGE CASE. It is an assertion on the SHAPE
        // of the state, like the 16 KB one above, and the right place is
        // where the state is read with nothing else in between.
        //
        // And it is also the only place where it costs little. Tried on 14/9/2026 to
        // go through the bridge: a plan with 85 `cylindrical_cut` took
        // 26 seconds per cut and got itself killed by the thirty
        // minute ceiling at the forty-sixth step. Splitting it into five plans does not
        // work — `face_ref: {step: 1}` is relative TO THE PLAN, so in the
        // second block «step 1» is the first cut of the block and not the
        // bar, and the bridge refused it (rightly and loudly).
        //
        // Here the library is called directly: no JSON-RPC, no
        // plan machinery, and the face is found again from the state at every run.
        // ==================================================================
        // V1. the photograph of the part — the only thing that says a SHAPE
        // ==================================================================
        /// <summary>
        /// Tests `PartView.Fire`.
        ///
        /// WHY THIS CHECK EXISTS, AND WHY IT TESTS THESE FOUR THINGS.
        /// The capability of photographing was already there — the bench saved BMPs with
        /// `SaveBMP` for months — and it never reached whoever reads. When a
        /// capability descends from the bench to the bridge, the things that break
        /// are not the ones the bench tested: they are the ones the bench
        /// did not have to respect because nobody was looking at it while it worked.
        ///
        ///   1. **something comes out** — the minimum;
        ///   2. **it is a PNG** — because the same frame as BMP is about
        ///      fifty times heavier, and the conversion is a step that can
        ///      fail in silence by returning the BMP;
        ///   3. **the orientation of whoever works comes back as it was** — it is the only
        ///      rule of this file that concerns a person instead of a
        ///      datum: a read-only operation that leaves the part
        ///      turned has done real damage;
        ///   4. **an invented view is refused saying which ones there are**
        ///      — the RED case. Without it, «something is always shot» would pass
        ///      even if the name were never looked at.
        ///
        /// And at the tail: **no files left in the temp folder.** `SaveBMP`
        /// wants a path, so every shot writes 2 MB to disk; if the
        /// `Bitmap` keeps the file open the deletion fails and nobody
        /// notices until the folder is full.
        /// </summary>
        private static void TestSnapshot()
        {
            Console.WriteLine();
            Console.WriteLine("=== PartView (V1) ===");

            var t = _addin.GetType(NS + "PartView", false);
            if (t == null)
            {
                Check("photograph: the class exists", false,
                      "PartView is not in the loaded library");
                return;
            }
            var fire = t.GetMethod("Fire", BindingFlags.Public | BindingFlags.Static);
            if (fire == null)
            {
                Check("photograph: Fire exists", false, "the method does not exist");
                return;
            }

            var doc = NewPart();

            string model = "oce-vista-";
            int temporaryBefore = TemporaryFiles(model);

            try
            {
                string error;
                var measurements = Run(doc, "extrude_rectangle", new Dictionary<string, object>
                {
                    ["plane"] = "top",
                    ["length_mm"] = 200.0,
                    ["width_mm"] = 100.0,
                    ["thickness_mm"] = 20.0,
                    ["centered"] = true,
                }, out error);
                if (measurements == null) { Check("photograph: the block", false, Short(error)); return; }

                var view = doc.ActiveView as IModelView;

                // ── THE NINE ROADS ARE IN `Checks\ViewTest` ──────
                //
                // Here there were three questions on how a view is put
                // back in place, and each one cost a bench run of seventeen
                // minutes for an answer of three seconds. Now they are in a
                // tool of their own:
                //
                //     Checks\ViewTest\bin\ViewTest.exe
                //
                // which tries nine roads and prints which ones come back. It is there that one must
                // look if this check turns red again: it also says **by
                // how much** and **on which number** it is wrong, and it is that number
                // that found the answer (the order of the three writes).
                //
                // Here remains the statement that counts for whoever uses the bridge: after
                // a photograph, the part on the screen of whoever works
                // stays as it was.
                double[] before = Orientation(view);

                var p = new object[] { doc, "isometric", 800, 600, null };
                var png = fire.Invoke(null, p) as byte[];
                string reason = p[4] as string;

                Check("photograph: the isometric comes out", png != null && png.Length > 0,
                      png == null ? Short(reason) : png.Length + " byte");

                // THE PNG SIGNATURE: 89 50 4E 47. A BMP begins with «BM».
                bool isPng = png != null && png.Length > 8 &&
                              png[0] == 0x89 && png[1] == 0x50 &&
                              png[2] == 0x4E && png[3] == 0x47;
                Check("photograph: it is a PNG, not the BMP of SOLIDWORKS", isPng,
                      png == null || png.Length < 2
                          ? "nothing to look at"
                          : "first bytes " + png[0].ToString("X2") + " " + png[1].ToString("X2"));

                // 800x600 uncompressed would be 1.4 MB: if the PNG gets
                // close to that, the conversion compressed nothing.
                if (png != null)
                    Check("photograph: it weighs much less than the BMP", png.Length < 800 * 600,
                          png.Length + " bytes against " + (800 * 600 * 3) + " of the raw BMP");

                double[] after = Orientation(view);
                Check("photograph: the orientation of the person working came back as it was",
                      SameOrientation(before, after),
                      before == null ? "unreadable" : "16 numbers compared");

                // ── THE RED CASE ─────────────────────────────────────────
                var q = new object[] { doc, "assonometrica-della-nonna", 800, 600, null };
                var nothing = fire.Invoke(null, q) as byte[];
                string why = q[4] as string;
                Check("photograph: an invented view is refused, with the real names",
                      nothing == null && why != null &&
                      why.IndexOf("isometric", StringComparison.OrdinalIgnoreCase) >= 0,
                      nothing != null
                          ? "it took the shot all the same"
                          : Short(why));

                // ── NO GARBAGE IN THE TEMP FOLDER ────────────────────────
                int temporaryAfter = TemporaryFiles(model);
                Check("photograph: no file left in the temp folder",
                      temporaryAfter <= temporaryBefore,
                      temporaryBefore + " prima, " + temporaryAfter + " dopo");
            }
            finally { Close(doc); }
        }

        /// <summary>How many temporary files of this name there are now.</summary>
        private static int TemporaryFiles(string model)
        {
            try { return Directory.GetFiles(Path.GetTempPath(), model + "*").Length; }
            catch { return 0; }
        }

        /// <summary>
        /// The sixteen numbers that describe how the part is turned on the
        /// screen. Null if it could not be read — and then the comparison
        /// asserts nothing, instead of asserting that all is well.
        /// </summary>
        private static double[] Orientation(IModelView view)
        {
            if (view == null) return null;
            try { return Copy(view.Transform as IMathTransform); }
            catch { return null; }
        }

        /// <summary>
        /// The sixteen numbers of a transformation, AS AN ARRAY and not as a
        /// reference: `ArrayData` builds a new vector at every read,
        /// and that is what makes the comparison possible.
        /// </summary>
        private static double[] Copy(IMathTransform m)
        {
            if (m == null) return null;
            try { return m.ArrayData as double[]; }
            catch { return null; }
        }

        private static bool SameOrientation(double[] a, double[] b)
        {
            // If it could not be read even before, this check has
            // no input: it is NOT green, it is without an answer. It is declared
            // failed because a check that gives up saying «ok» is the
            // defect that this project exists to make impossible.
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > 1e-9) return false;
            return true;
        }

        private static void TestFeatureCap()
        {
            Console.WriteLine();
            Console.WriteLine("=== the ceiling on features (E12) ===");

            const int HowMany = 85;      // the ceiling is 80: five must stay out
            const int Cap = 80;
            var doc = NewPart();

            try
            {
                string error;
                var measurements = Run(doc, "extrude_rectangle", new Dictionary<string, object>
                {
                    ["plane"] = "top",
                    ["length_mm"] = (HowMany - 1) * 10.0 + 30,
                    ["width_mm"] = 30.0,
                    ["thickness_mm"] = 8.0,
                    ["centered"] = true,
                }, out error);
                if (measurements == null) { Check("ceiling: the bar", false, Short(error)); return; }

                // The cuts, one per run. The top face is found again from the
                // state every time: it is what whoever draws would do, and no
                // reference to a step is needed.
                double x0 = -((HowMany - 1) * 10.0) / 2;
                int done = 0;
                for (int i = 0; i < HowMany; i++)
                {
                    var docState = ReadState(doc);
                    string topUid = FaceUp(docState);
                    if (topUid == null) break;

                    var m = Run(doc, "cylindrical_cut", new Dictionary<string, object>
                    {
                        ["face_uid"] = topUid,
                        ["x_mm"] = x0 + i * 10.0,
                        ["y_mm"] = 0.0,
                        ["diameter_mm"] = 4.0,
                        ["through_all"] = true,
                    }, out error, docState);
                    if (m == null) break;
                    done++;
                }
                Check("ceiling: " + HowMany + " features built", done == HowMany,
                      done + " su " + HowMany + (done < HowMany ? " -- " + Short(error) : ""));
                if (done < HowMany) return;

                var final = ReadState(doc);
                var list = final.ContainsKey("feature")
                    ? final["feature"] as System.Collections.IList : null;
                int inList = list == null ? 0 : list.Count;

                // 1. THE CEILING IS DECLARED.
                int totali = final.ContainsKey("feature_total")
                    ? Convert.ToInt32(final["feature_total"]) : 0;
                int outside = final.ContainsKey("feature_not_listed")
                    ? Convert.ToInt32(final["feature_not_listed"]) : 0;
                bool truncated = final.ContainsKey("truncated") &&
                                Convert.ToBoolean(final["truncated"]);
                Check("ceiling: declared",
                      totali == HowMany + 1 && outside == HowMany + 1 - Cap && truncated,
                      "listed " + inList + ", total " + totali + ", outside " + outside +
                      ", truncated " + truncated);

                // 2. AND THE ONE THAT COUNTS: THE LAST ONE IS THERE.
                //
                // It is the real defect of E12, and it is not seen from any count:
                // the tree is scanned in order of CREATION, so keeping the
                // first eighty means throwing away precisely the feature
                // that the plan has just made -- the only one that whoever reads is
                // looking for. The model did not find it and could conclude that
                // the operation had done nothing, and redo it.
                string last = null, before = null;
                if (list != null && list.Count > 0)
                {
                    var u = list[list.Count - 1] as Dictionary<string, object>;
                    var p = list[0] as Dictionary<string, object>;
                    if (u != null && u.ContainsKey("uid")) last = Convert.ToString(u["uid"]);
                    if (p != null && p.ContainsKey("uid")) before = Convert.ToString(p["uid"]);
                }
                Check("ceiling: the LAST one created is in the list, not the first",
                      last != null && last.EndsWith(HowMany.ToString()),
                      "first listed: " + (before ?? "?") + ", last: " + (last ?? "?"));

                // 3. AND THE EXTRUSION, which is the OLDEST, is no longer there.
                bool cAreTheOldOnes = false;
                if (list != null)
                    foreach (var v in list)
                    {
                        var d = v as Dictionary<string, object>;
                        if (d != null && d.ContainsKey("uid") &&
                            Convert.ToString(d["uid"]).StartsWith("Estrusione"))
                        { cAreTheOldOnes = true; break; }
                    }
                Check("ceiling: the oldest was discarded", !cAreTheOldOnes,
                      cAreTheOldOnes ? "the extrusion is still there: the first ones are kept"
                                     : "the extrusion is no longer there, as it should be");
            }
            catch (Exception ex) { Check("ceiling on features", false, Why(ex)); }
            finally { Close(doc); }
        }

        /// <summary>The uid of the planar face turned upward, from the state.</summary>
        private static string FaceUp(Dictionary<string, object> docState)
        {
            var faces = docState != null && docState.ContainsKey("faces")
                ? docState["faces"] as System.Collections.IList : null;
            if (faces == null) return null;
            foreach (var v in faces)
            {
                var f = v as Dictionary<string, object>;
                if (f == null) continue;
                if (f.ContainsKey("facing") && Convert.ToString(f["facing"]) == "up" &&
                    f.ContainsKey("uid"))
                    return Convert.ToString(f["uid"]);
            }
            return null;
        }

        // ==================================================================
        // riflessione
        // ==================================================================
        private static Dictionary<string, object> Run(
            IModelDoc2 doc, string operation, Dictionary<string, object> arguments,
            out string error, object docState = null)
        {
            IModelDoc2 ignored;
            return Run(doc, operation, arguments, out error, out ignored, docState);
        }

        /// <summary>
        /// As above, but it also says **whether the operation opened a new
        /// document** (entry GT0).
        ///
        /// ── WHY IT IS NEEDED, AND WHY THE ACTIVE DOCUMENT IS NOT LOOKED AT ───
        ///
        /// `create_drawing` opens a drawing and from there on one works on that one:
        /// it is the only legitimate change of document in the catalog. As long as the
        /// bench executed every step always on the SAME document, a case that
        /// created a drawing put the views **inside the part** — or more
        /// probably failed saying «il documento attivo non e' una
        /// tavola», which is the reason why no GT entry ever had a
        /// bench case.
        ///
        /// **SOLIDWORKS is not asked which is the active document.** The
        /// rule of this project is to keep the reference, not
        /// `ActiveDoc`: on a machine where SOLIDWORKS is shared, «active»
        /// is what *anybody* has in front of them, and it is enough for somebody to open
        /// a window for the bench to measure somebody else's document.
        ///
        /// The operation **declares** it: `Outcome.NewDocument`, which
        /// `create_drawing` fills on purpose. A declared change can be
        /// followed; a deduced one can only be endured.
        /// </summary>
        private static Dictionary<string, object> Run(
            IModelDoc2 doc, string operation, Dictionary<string, object> arguments,
            out string error, out IModelDoc2 newDocument, object docState = null)
        {
            error = null;
            newDocument = null;
            var constructor = _tOperations.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];
            // WHAT THE ADD-IN TELLS IS READ.
            // An empty logger is convenient as long as all goes well; the day
            // an operation makes a choice in your place — finding again a
            // face that has moved, for example — it is the only line that
            // explains why the part came out this way.
            var op = constructor.Invoke(new object[]
                { _sw, (Action<string>)(s => Console.WriteLine("       | " + s)) });

            if (docState == null) docState = ReadObjectState(doc);
            var run = _tOperations.GetMethod("Run",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            object objectState = docState is Dictionary<string, object> ? ReadObjectState(doc) : docState;

            // THE LEASH (entry K18): it announces what it is about to do,
            // and says when it came back. The guardian watches from outside.
            Leash.Begin(operation, DateTime.Now);
            object outcome;
            try
            {
                outcome = run.Invoke(op, new object[]
                {
                    doc, operation, arguments, objectState, null,
                });
            }
            finally { Leash.Finished(); }
            bool succeeded = (bool)_tOutcome.GetField("Succeeded").GetValue(outcome);
            if (!succeeded)
            {
                error = _tOutcome.GetField("Error").GetValue(outcome) as string;
                return null;
            }
            // THE FIELD MAY NOT BE THERE, and it is not a fault: the bench loads the
            // library by reflection, and a library older than this
            // line does not have `NewDocument`. In that case `null` is returned and one
            // goes on as before — **what one does not do is blow up**,
            // because a check that dies for a missing field measures
            // nothing of what it was supposed to measure.
            try
            {
                var theField = _tOutcome.GetField("NewDocument");
                if (theField != null) newDocument = theField.GetValue(outcome) as IModelDoc2;
            }
            catch { }

            try { doc.EditRebuild3(); } catch { }
            return _tOutcome.GetField("Measurements").GetValue(outcome) as Dictionary<string, object>;
        }

        private static object ReadObjectState(IModelDoc2 doc)
        {
            var read = _tState.GetMethod("Read",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            // THE APPLICATION MUST BE PASSED. From a document one cannot go up to
            // SolidWorks — neither IModelDoc2 nor IModelDocExtension has
            // access to the application — and without it the state cannot
            // say which OTHER documents are open. With drawings that
            // datum stops being an extra: a view attaches to the
            // path of an open document, and if it is not seen it cannot
            // be named.
            return read.Invoke(null, new object[] { doc, _sw });
        }

        private static Dictionary<string, object> ReadState(IModelDoc2 doc)
        {
            var docState = ReadObjectState(doc);
            var data = _tState.GetProperty("Data",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return data.GetValue(docState) as Dictionary<string, object>;
        }

        /// <summary>The largest planar face: on a plate it is one of the two big ones.</summary>
        private static string TopFace(Dictionary<string, object> docState)
        {
            var faces = docState["faces"] as System.Collections.IList;
            foreach (Dictionary<string, object> f in faces)
            {
                var n = f["normal"] as double[];
                if (n == null) continue;
                bool axial = Math.Abs(n[0]) > 0.99 || Math.Abs(n[1]) > 0.99 || Math.Abs(n[2]) > 0.99;
                if (axial) return Convert.ToString(f["uid"]);
            }
            return null;
        }

        // ==================================================================
        // utilities
        // ==================================================================
        private static List<object> NominalSegments(double L, double W, string plane, bool centered)
        {
            Axes(plane, out int u, out int v);
            double u0 = centered ? -L / 2 : 0, v0 = centered ? -W / 2 : 0;
            double u1 = u0 + L, v1 = v0 + W;
            Func<double, double, double[]> p = (a, b) =>
            {
                var q = new double[3]; q[u] = a; q[v] = b; return q;
            };
            var angles = new[] { p(u0, v0), p(u1, v0), p(u1, v1), p(u0, v1) };
            var outside = new List<object>();
            for (int i = 0; i < 4; i++)
                outside.Add(new Dictionary<string, object>
                {
                    ["start"] = angles[i], ["end"] = angles[(i + 1) % 4],
                });
            return outside;
        }

        private static void Axes(string plane, out int u, out int v)
        {
            switch ((plane ?? "").ToLowerInvariant())
            {
                case "top": u = 0; v = 2; return;
                case "right": u = 1; v = 2; return;
                default: u = 0; v = 1; return;
            }
        }

        private static double[] DocumentBox(IModelDoc2 doc)
        {
            double[] tot = null;
            var bodies = (doc as IPartDoc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            foreach (var c in bodies)
            {
                var b = (c as IBody2).GetBodyBox() as double[];
                if (b == null) continue;
                if (tot == null) { tot = b.Select(x => x * 1000).ToArray(); continue; }
                for (int i = 0; i < 3; i++) tot[i] = Math.Min(tot[i], b[i] * 1000);
                for (int i = 3; i < 6; i++) tot[i] = Math.Max(tot[i], b[i] * 1000);
            }
            return tot;
        }

        private static string Line(Dictionary<string, object> d)
        {
            return string.Join(" ", d.Select(kv => kv.Key + "=" + Value(kv.Value)));
        }

        private static string Value(object v)
        {
            var a = v as double[];
            if (a != null) return "[" + string.Join(",", a.Select(x => x.ToString("0.#"))) + "]";
            return Convert.ToString(v);
        }

        private static string Json(object o)
        {
            var s = new System.Web.Script.Serialization.JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            try { return s.Serialize(o); } catch { return ""; }
        }

        /// <summary>
        /// Why a check blew up: message, TYPE and the line it
        /// started from.
        ///
        /// WHY `ex.Message` IS NOT ENOUGH. On 15/9/2026 a check printed
        /// «Riferimento a un oggetto non impostato su un'istanza di oggetto.»
        /// and nothing else. That sentence says that something was null and does not say
        /// **what**, nor whether the null was in the bench or in the library: to
        /// locate it another run of seventeen minutes was needed.
        ///
        /// A bench that reports an exception without saying where it comes from makes
        /// every investigation pay the price of a whole run.
        ///
        /// **The reflection wrapper is also discarded.** The bench calls
        /// the library with `Invoke`, so whatever blows up in there
        /// arrives as `TargetInvocationException` — «Eccezione generata dalla
        /// destinazione di una chiamata», which says nothing at all.
        /// The one that matters is always the `InnerException`.
        /// </summary>
        private static string Why(Exception ex)
        {
            if (ex == null) return "(no exception)";
            while (ex is TargetInvocationException && ex.InnerException != null)
                ex = ex.InnerException;

            string location = "";
            try
            {
                var lines = (ex.StackTrace ?? "").Split('\n');
                if (lines.Length > 0) location = "  <- " + lines[0].Trim();
            }
            catch { }
            return ex.GetType().Name + ": " + Short(ex.Message) + location;
        }

        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(no reason)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 110 ? s.Substring(0, 110) + "..." : s;
        }

        // ══════════════════════════════════════════════════════════════
        // THE SENTINEL (entry K17)
        //
        // Three new parts that are not born IN A ROW are not three red
        // checks: they are SOLIDWORKS that is no longer there. The why, and the hour
        // and a half run that its absence cost, are in
        // `Shared/SolidWorksSentinel.cs`.
        // ══════════════════════════════════════════════════════════════
        internal static readonly Shared.SolidWorksSentinel Sentinel =
            new Shared.SolidWorksSentinel(3);

        /// <summary>
        /// `--fingi-senza-solidworks`: NewDocument answers null regardless.
        ///
        /// It serves to SEE the sentinel fire on the real bench, not only
        /// in the contract checks. A guard that nobody has ever seen
        /// fire is a line of code, not a guard — and this one in
        /// particular lights up in a condition that cannot be
        /// reproduced on command.
        /// </summary>
        private static bool _pretendWithoutSolidWorks;

        /// <summary>
        /// The families of declared cases to run, or null for all
        /// (`--famiglia`). A run with this flag is partial like one with
        /// `--gruppo`, and the summary says so.
        /// </summary>
        private static List<string> _familiesOnly;

        // ══════════════════════════════════════════════════════════════
        // THE DOCUMENT TEMPLATE IS COPIED LOCALLY (entry K19)
        //
        // ── WHAT IT COST NOT TO DO IT ────────────────────────────────
        //
        // On 23/9/2026 three runs in a row died here. The default document
        // template of this workstation is on a disk MAPPED TO A NETWORK
        // SHARE: **every new part is an SMB
        // read**, and a run makes eight hundred. It is not a quirk
        // of here: it is the configuration of anybody who keeps the company
        // templates in one place only.
        //
        // When SOLIDWORKS hangs while opening that file, it keeps the
        // lock: from that moment the file can no longer be read EVEN from
        // outside. Measured while it was hung — the profiles folder
        // on the same share was answering (13 files), the server
        // answered the ping in 0 ms, and `Get-Item` on that `.prtdot` did not
        // come back within twenty seconds. SOLIDWORKS killed: 45,955 bytes,
        // immediate. **It looked like the network, and it was a lock.**
        //
        // And the next run fell into the same trap again, because the
        // new SOLIDWORKS reopened the same file on the same server.
        //
        // ── WHY A COPY, AND NOT A TIMEOUT ────────────────────────────
        //
        // A timeout on the call (entry K18) says «I got tired of
        // waiting»: it is right and serves all the same, but here the question is
        // another — **why does the bench read eight hundred times from the network
        // a file that never changes during the run?** One single copy,
        // at the start, and the question no longer arises.
        //
        // ── AND IT IS SAID WHAT WAS DONE ─────────────────────────────
        //
        // A bench that silently uses a template different from the one
        // of the workstation is a bench that measures something else. The copy
        // is announced, with the starting path and the arrival one; if it does not
        // succeed, the original is used and **it is said** — there is no pretending.
        //
        // IT DOES NOT COVER EVERYTHING: SOLIDWORKS reads from the network also the
        // structural profiles and the material archives. This is the most frequent
        // read, not the only one.
        // ══════════════════════════════════════════════════════════════
        private static readonly Dictionary<string, string> _localModels =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static string LocalModel(string its)
        {
            if (string.IsNullOrWhiteSpace(its)) return its;

            string already;
            if (_localModels.TryGetValue(its, out already)) return already;

            string arrival = its;
            try
            {
                if (IsRemote(its))
                {
                    var folder = Path.Combine(Path.GetTempPath(), "banco-modelli");
                    Directory.CreateDirectory(folder);
                    var copy = Path.Combine(folder, Path.GetFileName(its));
                    File.Copy(its, copy, true);
                    arrival = copy;
                    Console.WriteLine("  model copied locally: " +
                                      Path.GetFileName(its) + " -> " + folder);
                }
            }
            catch (Exception ex)
            {
                // YESTERDAY'S COPY IS WORTH MORE THAN THE UNREACHABLE ORIGINAL.
                //
                // The case in which the copy fails is precisely the one in which
                // the original cannot be read: on 23/9/2026 the `.prtdot`
                // was held by a lock left on the server, and falling back to the
                // network path meant hanging again in the same way.
                // If locally there is the copy of a previous run, that one
                // is used — and it is said **from when it is**, because an
                // old template passing itself off as the one of now would be the
                // same kind of silence that this bench chases.
                string old = null;
                try
                {
                    var c = Path.Combine(Path.GetTempPath(), "banco-modelli",
                                         Path.GetFileName(its));
                    if (File.Exists(c)) old = c;
                }
                catch { }

                if (old != null)
                {
                    Console.WriteLine("  I could not refresh the model (" +
                                      ex.Message + "): I use the local copy of the " +
                                      File.GetLastWriteTime(old)
                                          .ToString("d/M/yyyy HH:mm"));
                    arrival = old;
                }
                else
                {
                    // ONE DOES NOT KEEP QUIET. A run that reads from the network and does not say so
                    // is a run that tomorrow will hang without explanations.
                    Console.WriteLine("  I could NOT copy the model locally (" +
                                      ex.Message + "): I use the network one, " + its);
                    arrival = its;
                }
            }

            _localModels[its] = arrival;
            return arrival;
        }

        /// <summary>True if the path is on a network share —
        /// UNC or a mapped drive. A mapped drive cannot be seen from the
        /// path: `S:\...` looks local, and it is not.</summary>
        private static bool IsRemote(string filePath)
        {
            try
            {
                if (filePath.StartsWith("\\\\", StringComparison.Ordinal)) return true;
                var root = Path.GetPathRoot(filePath);
                if (string.IsNullOrEmpty(root)) return false;
                return new DriveInfo(root).DriveType == DriveType.Network;
            }
            catch { return false; }
        }
        // ══════════════════════════════════════════════════════════════
        // THE LEASH AND THE GUARDIAN (entry K18)
        //
        // On 23/9/2026 three runs died hung inside a call that
        // did not return, and the bench had no way of noticing: it has
        // no timeout on the calls to the library, only on the bridge.
        //
        // NINETY SECONDS, and it is not «how long it should take»: it is how long
        // beyond any doubt. The slowest check of this bench — 85
        // features on one part, with `--lento` — takes ~26 seconds per
        // cut, and no SINGLE call comes near that. A tight value
        // would turn a slow check into a false alarm, and a false
        // alarm switches the guardian off the next day.
        // ══════════════════════════════════════════════════════════════
        internal static readonly Shared.Leash Leash =
            new Shared.Leash(90000);

        private static System.Threading.Thread _guardian;

        /// <summary>
        /// The guardian watches the clock and does NOT touch the work.
        ///
        /// It does not try to interrupt the call: a stuck COM call
        /// cannot be cancelled, and the thread making it stays there forever.
        /// What it does is print WHICH one it was and **kill the process**,
        /// because with SOLIDWORKS stuck the checks that follow would
        /// measure nothing — and a run hung indefinitely is
        /// worse than a run that ended badly: nobody looks at it.
        ///
        /// `Environment.Exit` and not an exception: the exception would have to be
        /// raised on the guardian's thread, where nobody would catch it,
        /// and the hung thread would keep the process alive anyway.
        /// </summary>
        private static void TurnOnGuardian()
        {
            if (_guardian != null) return;
            _guardian = new System.Threading.Thread(() =>
            {
                while (true)
                {
                    System.Threading.Thread.Sleep(5000);
                    var report = Leash.Hanging(DateTime.Now);
                    if (report == null) continue;
                    Console.WriteLine();
                    Console.WriteLine(new string('!', 76));
                    Console.WriteLine(report);
                    Console.WriteLine("INVALID RUN: it stopped here, and " +
                                      "what comes after was not tried.");
                    Console.WriteLine(new string('!', 76));
                    Console.Out.Flush();
                    Log(report);
                    // `System.Environment` in full: SOLIDWORKS's interop
                    // has an `Environment` of its own, and without the whole name the
                    // compiler does not know which of the two.
                    System.Environment.Exit(5);
                }
            });
            _guardian.IsBackground = true;
            _guardian.Start();
        }
        private static IModelDoc2 NewPart()
        {
            // ALREADY DEAD: it is not even called. Every call to a
            // SOLIDWORKS that does not answer can hang forever — it is
            // how the 15:36 run stayed hung instead of
            // ending badly quickly.
            if (Sentinel.HasGone)
                throw new Shared.PreparationFailed(Sentinel.Report());

            string tpl = LocalModel(_sw.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplatePart));

            // THIS IS WHERE IT HUNG ON 23/9/2026, not inside an
            // operation: `NewDocument` reading the template from a
            // network share. The leash serves above all here.
            Leash.Begin("NewDocument (the document template)", DateTime.Now);
            IModelDoc2 born;
            try
            {
                born = _pretendWithoutSolidWorks
                    ? null
                    : _sw.NewDocument(tpl, 0, 0, 0) as IModelDoc2;
            }
            finally { Leash.Finished(); }

            if (born != null) { Sentinel.Succeeded(); return born; }

            if (Sentinel.Failed())
                throw new Shared.PreparationFailed(Sentinel.Report());

            // One or two in a row: it may still be the part, not the
            // program. The message stays the one of F12.
            // IT DOES NOT RETURN NULL: if SOLIDWORKS does not open the document, it SAYS so.
            return OrSayIt(born, "the new part to try on");
        }

        /// <summary>
        /// The object, or an exception that SAYS which step did not
        /// produce it (entry F12).
        ///
        /// ── WHAT IT COST NOT TO HAVE IT ──────────────────────────────────
        ///
        /// On 15/9/2026 the check «spostamento nullo (deve fallire)» came out
        /// red with «Riferimento a un oggetto non impostato su un'istanza di
        /// oggetto.», and nothing else. In the red run the line that the
        /// library prints before refusing was missing too, so the null was **before**
        /// the call to be tested — but in eighteen places of the bench
        /// `NewPart()` was used without looking at whether it had produced
        /// anything, and a `NullReferenceException` does not say which of the eighteen.
        ///
        /// The defect is not that the check failed: it is that **in failing it
        /// said nothing**. A bench that can say «I failed here» has already
        /// solved half of the problem; one that says «null reference» costs
        /// seventeen minutes every time it happens again.
        ///
        /// It is not a red check: it is the bench that did not manage to
        /// PREPARE the part, and that is another thing — for this reason the message says
        /// so in plain letters.
        /// </summary>
        private static T OrSayIt<T>(T what, string which) where T : class
        {
            if (what != null) return what;
            throw new Shared.PreparationFailed(
                "SOLIDWORKS did not produce " + which + " (it answered null). " +
                "It is not the check that failed: it is the bench that could not " +
                "prepare the part. Look at how many documents were " +
                "left open, and run again.");
        }

        /// <summary>
        /// The three reference planes of the document, and an exception that
        /// names them if the tree does not give them. It was the other point of the same
        /// family (entry F12): `GetFeatures(false) as object[]` may return
        /// null, and the `.Select` right after gave the same mute NullReference
        /// as `NewPart()`.
        /// </summary>
        private static List<IFeature> ReferencePlanes(IModelDoc2 doc)
        {
            var tree = OrSayIt(doc.FeatureManager.GetFeatures(false) as object[],
                                     "the document's feature tree");
            var planes = tree
                .Select(o => o as IFeature)
                .Where(f => { try { return f.GetTypeName2() == "RefPlane"; } catch { return false; } })
                .ToList();
            if (planes.Count == 0)
                throw new InvalidOperationException(
                    "the document has no reference plane: the default part " +
                    "template is not what the bench expects.");
            return planes;
        }

        private static void Close(IModelDoc2 doc)
        {
            if (doc == null) return;
            try { _sw.CloseDoc(doc.GetTitle()); } catch { }
        }

        private static void Plate(IModelDoc2 doc, double L, double W, double H)
        {
            var planes = ReferencePlanes(doc);
            doc.ClearSelection2(true);
            planes[0].Select2(false, 0);          // front: the face on top will have normal +Z
            var sm = doc.SketchManager;
            sm.InsertSketch(true);
            sm.CreateCenterRectangle(0, 0, 0, L / 2000.0, W / 2000.0, 0);
            sm.InsertSketch(true);
            doc.FeatureManager.FeatureExtrusion3(
                true, false, false, (int)swEndConditions_e.swEndCondBlind, 0, H / 1000.0, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, true, true, 0, 0, false);
            doc.ClearSelection2(true);
        }
    }
}
