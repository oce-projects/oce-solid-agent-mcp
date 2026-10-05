using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static Oce.Solid.Agent.ContractTests.Program;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// HAS THE BENCH BEEN RUN ON THIS VERSION? (entry F9)
    ///
    /// ── WHY A CHECK WITHOUT SOLIDWORKS CAN SAY SO ─────────────────────────
    ///
    /// The CI cannot drive SOLIDWORKS — no runner with a licence — but it
    /// can do something that is worth almost as much: **refuse a delivery
    /// in which nobody ran the bench.** It is the piece of discipline that is
    /// bought for two hours, instead of the licensed runner that cannot be
    /// bought at all.
    ///
    /// The only proof that the bench ran is `Bench/report/last.json`,
    /// which `bench.ps1` writes on every run and which is the only file of `report/`
    /// under version control.
    ///
    /// ── AND THE SILENT DEFECT TO FEAR ───────────────────────────────────
    ///
    /// A CI that builds an empty project because the csproj filter is
    /// wrong, and stays green for months. That is why here we do not only look
    /// for the file to be there: we look for the checks to have been **more than zero**, for
    /// none of them to have failed, and for the run not to be **older than the last
    /// change to the operations**.
    /// </summary>
    internal static class BenchTests
    {
        internal static void All()
        {
            Group("il banco e' stato lanciato su questa versione");

            var file = Path.Combine(Root(), "Bench", "report", "last.json");
            if (!File.Exists(file))
            {
                Check("il banco e' stato eseguito", false,
                      "il banco non e' stato eseguito su questa versione: manca " + file +
                      ". Si lancia con .\\Bench\\bench.ps1");
                return;
            }
            Check("il banco e' stato eseguito", true, file);

            Dictionary<string, object> benchRun;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                benchRun = s.DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
            }
            catch (Exception ex) { Check("il resoconto si legge", false, ex.Message); return; }
            if (benchRun == null) { Check("il resoconto e' un oggetto", false, ""); return; }

            // ── MORE THAN ZERO CHECKS ─────────────────────────────────────
            //
            // «0 prove, 0 fallite» is green and means nothing. It is the
            // shape a suite takes when it has emptied itself.
            int passed = Integer(benchRun, "passed");
            Check("il giro ha misurato qualcosa", passed > 0, passed + " prove passate");
            Check("nessuna prova fallita", Integer(benchRun, "failed") == 0,
                  Integer(benchRun, "failed") + " fallite");
            Check("nessun errore di banco", Integer(benchRun, "errors") == 0,
                  Integer(benchRun, "errors") + " errori di banco");
            Check("il giro e' uscito 0", Integer(benchRun, "exit_code") == 0,
                  "uscita " + Integer(benchRun, "exit_code"));

            // ── NOT A PARTIAL RUN ─────────────────────────────────────────
            //
            // `--gruppo` is handy while working and does not count as a delivery:
            // the summary says so, and here it is read.
            string summary = Convert.ToString(benchRun.ContainsKey("summary") ? benchRun["summary"] : "");
            Check("non e' un giro parziale",
                  summary.IndexOf("GIRO PARZIALE", StringComparison.Ordinal) < 0,
                  summary);

            // ── THE LIBRARY VERSION IS THE CURRENT ONE ────────────────────
            string declared = AssemblyVersion();
            string didRun = Convert.ToString(benchRun.ContainsKey("library") ? benchRun["library"] : "");
            Check("il banco ha girato sulla libreria di adesso",
                  declared != null && didRun.StartsWith(declared, StringComparison.Ordinal),
                  "AssemblyInfo dice " + (declared ?? "?") + ", il giro dice " + didRun);

            // ── AND NOT BEFORE THE LAST CHANGE TO THE OPERATIONS ──────────
            //
            // The check that counts: a library touched after the last run
            // is an untested library, even if the version number
            // matches because somebody forgot to raise it.
            DateTime? timestamp = Data(Convert.ToString(benchRun.ContainsKey("timestamp") ? benchRun["timestamp"] : ""));
            if (timestamp == null)
            {
                Check("il giro dice quando", false, "«timestamp» illeggibile");
            }
            else
            {
                // ── TWO HALVES, AND BEFORE ONLY ONE WAS WATCHED (entry K14) ─────
                //
                // The bench is made of two things: the LIBRARY that measures and the
                // BENCH CODE that decides what to measure. This guard
                // watched only the first, so a new check, a new
                // counter or a stricter expectation went into the
                // repository without anything asking «and did you
                // run it?».
                //
                // This is not theory: on 23/9/2026 the clean
                // desk (K12) and the count of CAD documents
                // (K2-bis) went into the bench while a run had already started, and the committed
                // report described a bench that was no
                // longer in the repository. It was not false — that run did happen — but it
                // answered a different question from the one it seemed to answer:
                // not «this bench is green» but «a bench from a few hours ago
                // was».
                LookAtTouch("le operazioni", "Oce.Solid.Agent.Core", timestamp.Value);
                LookAtTouch("il banco", "Bench", timestamp.Value);
            }

            // Outside the `if`: the rule is checked even when the report
            // is not there. A guard that is checked only when there is already
            // something to guard stays silent exactly in the worst case.
            TestTouchRule();
        }

        private static int Integer(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return -1;
            try { return Convert.ToInt32(v); } catch { return -1; }
        }

        private static DateTime? Data(string s)
        {
            DateTime d;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture,
                                     DateTimeStyles.AssumeLocal, out d) ? (DateTime?)d : null;
        }

        /// <summary>
        /// `AssemblyVersion` read from the SOURCE, not from the compiled DLL: here
        /// we want to know what the repository declares, not what is in `bin`
        /// — which could be from yesterday.
        /// </summary>
        private static string AssemblyVersion()
        {
            try
            {
                var f = Path.Combine(Root(), "Oce.Solid.Agent.Core", "Properties", "AssemblyInfo.cs");
                if (!File.Exists(f)) return null;
                var m = Regex.Match(File.ReadAllText(f),
                                    @"AssemblyVersion\(""([0-9]+\.[0-9]+\.[0-9]+)");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// When the operations were last touched, according to git.
        /// Null if git is not there or does not answer — and in that case the check
        /// SAYS so instead of passing.
        /// </summary>
        /// <summary>
        /// When the library was last touched WITHOUT a bench report
        /// entering the same commit.
        ///
        /// ── BEFORE, THIS CHECK COULD NEVER PASS ─────────────────
        ///
        /// It asked `git log -1 -- Oce.Solid.Agent.Core` and compared that date
        /// with the time of the run. But the run is launched, writes `last.json`, and
        /// THEN everything is committed together — library and report in the same
        /// commit. The date of the commit is therefore by construction later
        /// than the time of the run it contains. Measured on b35550d: run 21:12:13,
        /// commit 21:13:52, ninety-nine seconds later against a tolerance of
        /// sixty.
        ///
        /// It was not missing by a little: it was always missing. *A check that cannot
        /// become green is worse than no check — it reads like a
        /// defense, fails on every run, and whoever looks at it learns not to
        /// look at it.*
        ///
        /// ── THE REAL QUESTION, AND HOW IT IS ANSWERED ───────────────────────
        ///
        /// Not «when is the last commit on the library», but **was the library
        /// touched after the last run?**. A commit that brings with it a new
        /// report IS the run: it is the delivery, and the report it
        /// contains validates it. A commit that touches the library and does NOT bring a
        /// new report is instead exactly the case to catch —
        /// somebody changed the code and did not run it again.
        ///
        /// So the commits on the library are scanned, stopping at the first one
        /// that does not also touch `Bench/report/last.json`.
        /// </summary>
        /// <summary>
        /// «Git did not answer» and «there is nothing to find» were the
        /// same `null`, and the first of the two was printed with the message
        /// of the other.
        ///
        /// A young repository — no commit on the library without a
        /// report next to it — has nothing untested, and is a GREEN.
        /// Git not answering is a RED: without it the check does not
        /// exist, and a check that does not exist must not be mistaken for one that
        /// found nothing.
        /// </summary>
        private sealed class Touch
        {
            public bool GitHasAnswered;
            public DateTime? When;
            /// <summary>Where the date comes from: it is for whoever reads the red.</summary>
            public string Why;
        }

        private static Touch LastChangeWithoutReport(string filePath)
        {
            var outside = new Touch();
            try
            {
                var lines = Git("log -30 --format=%H|%cI -- " + filePath);
                outside.GitHasAnswered = true;
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length < 2) continue;
                    if (CommitCarriesReport(parts[0])) continue;
                    outside.When = Data(parts[1]);
                    outside.Why = "commit";
                    return outside;
                }
                return outside;
            }
            catch { return new Touch(); }
        }

        /// <summary>
        /// Is the run later than the last change to `path`?
        ///
        /// THE ONE-MINUTE TOLERANCE IS NOT A ROUNDING: the run
        /// writes `last.json` and THEN is committed, so the date of the commit
        /// is by construction later than the time of the run it contains. The
        /// commits that bring the report are skipped on purpose; the minute
        /// covers the case in which the clock of the commit and that of the run
        /// differ slightly.
        /// </summary>
        private static void LookAtTouch(string what, string filePath, DateTime timestamp)
        {
            var t = MoreRecent(LastChangeWithoutReport(filePath),
                               LastChangeInTheWorkTree(filePath));
            if (!t.GitHasAnswered)
            {
                Check("so quando " + what + " e' stato toccato", false,
                      "git non risponde: senza, questo controllo non esiste");
                return;
            }
            if (t.When == null)
            {
                Check("il giro e' posteriore all'ultima modifica a " + what, true,
                      "nessun commit su " + filePath + " senza il resoconto accanto: " +
                      "non c'e' niente di non provato");
                return;
            }
            Check("il giro e' posteriore all'ultima modifica a " + what,
                  timestamp >= t.When.Value.AddMinutes(-1),
                  "giro " + timestamp.ToString("s") +
                  ", ultima modifica " + t.When.Value.ToString("s") +
                  (string.IsNullOrEmpty(t.Why) ? "" : " (" + t.Why + ")"));
        }

        /// <summary>
        /// THE VERDICT, SEPARATED FROM WHO READS GIT AND THE DISK.
        ///
        /// It stands alone because a check that cannot be made to fail
        /// on purpose is not a check, and making `git log` fail on purpose
        /// would mean building a fake repository. This way instead two
        /// dates are passed and we look at what it answers.
        ///
        /// ── THE HOLE THIS FUNCTION CLOSES ───────────────────────────────
        ///
        /// `LastChangeWithoutReport` asks **`git log`**: the committed
        /// history, and nothing else. But the code **does not live there** while it
        /// is being written: it lives in the working tree, and it is from there that `build.ps1`
        /// builds the DLL that then runs. Between the change and the commit there is
        /// a window in which the check said **«non c'e' niente di non
        /// provato»** while looking at a tree full of stuff never passed through the bench.
        ///
        /// Measured on 23/9/2026: `Oce.Solid.Agent.Core/Catalog/Operations.cs`
        /// touched at 19:18, last bench run at 15:23, and the
        /// check **green** — because the last *commit* on that folder
        /// was from 15/9. Green for the wrong reason, which is the way in which
        /// a guard stops being useful without anyone noticing.
        ///
        /// And the window was not narrow: at that moment the tree held
        /// 29 modified files and 31 new ones.
        /// </summary>
        /// <summary>
        /// Which of these names count for «is the run old?».
        ///
        /// It stands alone to be able to test it: the rule is one line, and a
        /// line inside a `foreach` cannot be interrogated.
        /// </summary>
        internal static List<string> CodeOnly(IEnumerable<string> names)
        {
            var outside = new List<string>();
            foreach (var n in names)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                if (n.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                outside.Add(n);
            }
            return outside;
        }

        private static Touch MoreRecent(Touch fromGit, DateTime? fromWork)
        {
            if (fromWork == null) return fromGit;
            if (fromGit.When != null && fromGit.When.Value >= fromWork.Value) return fromGit;
            return new Touch
            {
                GitHasAnswered = fromGit.GitHasAnswered,
                When = fromWork,
                Why = "modificato e NON committato",
            };
        }

        /// <summary>
        /// The most recent among the dirty files under `path`, or null if
        /// the tree is clean there.
        ///
        /// THREE COMMANDS AND NOT `status --porcelain`, and the reason is `Git()`:
        /// that reader does `Trim()` on every line, and `--porcelain` puts the
        /// status in **fixed columns** that the Trim eats. These three give
        /// bare names, one per line, and there is nothing to get wrong in
        /// recognizing them:
        ///
        ///     diff --name-only              modified, not in the index
        ///     diff --name-only --cached     modified, in the index
        ///     ls-files --others             new, not yet tracked
        ///
        /// A DELETED file is skipped: `File.Exists` is false and there is
        /// no new code to test. A deletion must be tested too, but
        /// not with a modification date that no longer exists.
        /// </summary>
        private static DateTime? LastChangeInTheWorkTree(string filePath)
        {
            var names = new List<string>();
            try
            {
                names.AddRange(Git("diff --name-only -- " + filePath));
                names.AddRange(Git("diff --name-only --cached -- " + filePath));
                names.AddRange(Git("ls-files --others --exclude-standard -- " + filePath));
            }
            catch { return null; }

            DateTime? more = null;
            foreach (var itemName in names)
            {
                if (string.IsNullOrWhiteSpace(itemName)) continue;

                // A `.md` IS NOT CODE, and asking it to account for itself costs seventy
                // minutes of CAD.
                //
                // This guard exists for one thing only: **not to deliver
                // code on which the bench has not run**. A README does not
                // change what the bench does — and on 24/9/2026 it triggered
                // the red because a count in the README had been
                // corrected TWO MINUTES after a green run, to align it with
                // two cases that that run had already tested.
                //
                // It is the same principle as the Z-A1 guard, which counts
                // lines of code and not raw ones because *counting
                // raw ones punishes whoever explains*.
                //
                // BEWARE OF WHAT IS **NOT** SKIPPED: the `.json` files of
                // `Bench/cases/` are DATA that change what the bench measures,
                // and stay counted. Only `.md` is skipped.
                if (itemName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;

                var full = Path.Combine(Root(),
                                         itemName.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) continue;
                var q = File.GetLastWriteTime(full);
                if (more == null || q > more.Value) more = q;
            }
            return more;
        }

        /// <summary>
        /// AND NOW THE RULE IS MADE TO FAIL ON PURPOSE, in both directions.
        ///
        /// The last case is the real one: it is the false green that existed before.
        /// When git found **no** untested commit, the guard
        /// answered «non c'e' niente di non provato» — and that sentence was
        /// false exactly when the tree was full.
        /// </summary>
        private static void TestTouchRule()
        {
            var git = new Touch
            {
                GitHasAnswered = true,
                When = new DateTime(2026, 9, 15, 8, 27, 48),
                Why = "commit",
            };
            var dirty = new DateTime(2026, 9, 23, 19, 18, 20);

            var withDirt = MoreRecent(git, dirty);
            Check("un file toccato e non committato VINCE sull'ultimo commit",
                  withDirt.When == dirty &&
                  withDirt.Why == "modificato e NON committato",
                  "commit 15/9 08:27, file 23/9 19:18: vale il file");

            var clean = MoreRecent(git, null);
            Check("e con l'albero pulito resta la data del commit",
                  clean.When == git.When && clean.Why == "commit",
                  "niente di sporco: vale il commit, come prima");

            var dirtyOld = MoreRecent(git, new DateTime(2026, 9, 10, 0, 0, 0));
            Check("un file sporco PIU' VECCHIO del commit non sposta niente",
                  dirtyOld.When == git.When,
                  "vale il piu' recente dei due, non l'ultimo guardato");

            var withoutCommit = MoreRecent(
                new Touch { GitHasAnswered = true, When = null }, dirty);
            Check("«nessun commit non provato» NON copre un file sporco",
                  withoutCommit.When == dirty,
                  "era il verde falso: git taceva e l'albero era pieno");

            // ── AND A `.md` DOES NOT COUNT, BUT A `.json` DOES ───────────
            //
            // The two halves of the same rule, and the second is the one that
            // stops the exception from widening: the declared cases are
            // `.json`, they change what the bench measures, and must keep
            // demanding a new run.
            Check("un LEGGIMI toccato dopo il giro non lo invalida",
                  CodeOnly(new[] { "Bench/README.md" }).Count == 0,
                  "una riga di documentazione non cambia cosa fa il banco");
            Check("ma un caso dichiarato si'",
                  CodeOnly(new[] { "Bench/cases/bodies/x.json" }).Count == 1,
                  "un `.json` di `cases/` e' un DATO: cambia cosa si misura");
            Check("e un sorgente pure",
                  CodeOnly(new[] { "Bench/Program.cs" }).Count == 1,
                  "l'eccezione e' stretta a `.md` e non si allarga da sola");

            // ── A COMMIT CARRIES A RUN ONLY IF THE RUN'S TIMESTAMP CHANGES ──
            //
            // The shapes are the real ones of W3.1-bis.4: the move kept
            // `timestamp`, the rewrite turned it into `timestamp` with the same
            // value. Neither is a run; a new timestamp is.
            const string oldRun = "{\"timestamp\": \"2026-10-01T11:43:51\", \"passate\": 2160}";
            const string sameRunNewKeys = "{\"timestamp\": \"2026-10-01T11:43:51\", \"passed\": 2160}";
            const string newRun = "{\"timestamp\": \"2026-10-02T09:00:00\", \"passed\": 2160}";
            Check("moving the report is not a run",
                  !CarriesANewRun(oldRun, oldRun),
                  "same `timestamp` before and after the move");
            Check("rewriting the report's keys is not a run",
                  !CarriesANewRun(oldRun, sameRunNewKeys),
                  "`timestamp` became `timestamp` with the same value");
            Check("a new timestamp is a run",
                  CarriesANewRun(sameRunNewKeys, newRun),
                  "2026-10-01T11:43:51 then 2026-10-02T09:00:00");
            Check("an old-path run is still a run",
                  CarriesANewRun("{\"timestamp\": \"2026-09-30T08:00:00\"}", oldRun),
                  "the historical deliveries wrote the report at its Italian path");
            Check("the first report ever is a run",
                  CarriesANewRun(null, newRun),
                  "nothing before it");
            Check("an unreadable report is not a run",
                  !CarriesANewRun(oldRun, "{not json"),
                  "no timestamp, no run");

            // And on the real history: the commit that moved the report must
            // not count as a delivery.
            string rename = ReportRenameCommit();
            // Always one check, so the count in Tests/README.md holds on a
            // squashed history too (where there is no rename to look at).
            Check("the commit that moved the report is not a delivery",
                  rename == null || !CommitCarriesReport(rename),
                  rename == null
                      ? "no rename of the report in this history: nothing to look at"
                      : rename.Substring(0, Math.Min(7, rename.Length)) +
                        " moves the report and runs nothing");
        }

        /// <summary>
        /// Does this commit carry a NEW bench run?
        ///
        /// Touching the report file is not enough. W3.1-bis.4 moved
        /// the report from its Italian path to its English one and then
        /// rewrote its keys (`timestamp` became `timestamp`): both commits touch
        /// the file, neither carries a run, and the second one also changes
        /// the loader and every case. Counting them as deliveries made the
        /// freshness check skip exactly the commits it exists to catch, and
        /// the historical commits that did carry a run (they wrote the old
        /// path) stopped being recognised at all.
        ///
        /// So a commit carries a run only when the run's timestamp, read from
        /// whichever of the two paths exists, differs between `sha~1` and
        /// `sha`.
        /// </summary>
        private static bool CommitCarriesReport(string sha)
        {
            string after = ReportAt(sha);
            if (after == null) return false;
            return CarriesANewRun(ReportAt(sha + "~1"), after);
        }

        /// <summary>The report's text at `rev`, new path first, or null.</summary>
        private static string ReportAt(string rev)
        {
            foreach (var path in ReportPaths)
            {
                var lines = Git("show " + rev + ":" + path);
                if (lines.Count > 0) return string.Join("\n", lines);
            }
            return null;
        }

        // Every path the report has had, the newest first, on purpose: the
        // markers keep window-1-cases.js and window-3-identifiers.js from
        // rewriting the old ones into the new one on a rerun. Without the
        // middle one (the path from W3.1-bis.4 to rename window 3) the history
        // between the two renames carries no report, and no rename is found.
        private static readonly string[] ReportPaths =
            { "Bench/report/last.json", "Banco/report/last.json", "Banco/resoconto/ultimo.json" }; // window-1: keep // window-3: keep

        /// <summary>
        /// The verdict, apart from git so it can be made to fail on purpose:
        /// true only when `after` holds a run timestamp that `before` does
        /// not hold. A rename or a key rewrite keeps the timestamp and is not
        /// a run.
        /// </summary>
        private static bool CarriesANewRun(string before, string after)
        {
            string t = RunTimestamp(after);
            if (t == null) return false;
            return t != RunTimestamp(before);
        }

        /// <summary>`timestamp`, or `timestamp` on reports written before W3.1-bis.4.</summary>
        private static string RunTimestamp(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                var d = s.DeserializeObject(json) as Dictionary<string, object>;
                if (d == null) return null;
                object v;
                if (d.TryGetValue("timestamp", out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture);
                if (d.TryGetValue("timestamp", out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture);
                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// The commit that moved the report to its English path, if history
        /// has one. Null when there is none (a squashed or a younger history).
        /// </summary>
        private static string ReportRenameCommit()
        {
            foreach (var line in Git("log --format=%H -M --diff-filter=R -- " +
                                     string.Join(" ", ReportPaths)))
                if (!string.IsNullOrWhiteSpace(line)) return line;
            return null;
        }

        private static List<string> Git(string arguments)
        {
            var outside = new List<string>();
            var p = new Process
            {
                StartInfo = new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = Root(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            p.Start();
            string line;
            while ((line = p.StandardOutput.ReadLine()) != null) outside.Add(line.Trim());
            p.WaitForExit(10000);
            return outside;
        }
    }
}
