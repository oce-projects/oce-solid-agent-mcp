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
    /// IL BANCO E' STATO LANCIATO SU QUESTA VERSIONE? (voce F9)
    ///
    /// ── PERCHE' UNA PROVA SENZA SOLIDWORKS PUO' DIRLO ────────────────────
    ///
    /// La CI non puo' pilotare SOLIDWORKS — nessun runner con licenza — ma
    /// puo' fare una cosa che vale quasi altrettanto: **rifiutare una consegna
    /// in cui nessuno ha lanciato il banco.** E' il pezzo di disciplina che si
    /// compra a due ore, invece del runner con licenza che non si compra
    /// affatto.
    ///
    /// L'unica prova che il banco sia girato e' `Bench/report/last.json`,
    /// che `bench.ps1` scrive a ogni giro ed e' l'unico file di `report/`
    /// versionato.
    ///
    /// ── E IL DIFETTO SILENZIOSO DA TEMERE ───────────────────────────────
    ///
    /// Una CI che compila un progetto vuoto perche' il filtro dei csproj e'
    /// sbagliato, e resta verde per mesi. Per questo qui non si guarda solo che
    /// il file ci sia: si guarda che le prove siano state **piu' di zero**, che
    /// nessuna sia fallita, e che il giro non sia **piu' vecchio dell'ultima
    /// modifica alle operazioni**.
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

            // ── PIU' DI ZERO PROVE ───────────────────────────────────────
            //
            // «0 prove, 0 fallite» e' verde e non vuol dire niente. E' la
            // forma che prende una suite che si e' svuotata.
            int passed = Integer(benchRun, "passed");
            Check("il giro ha misurato qualcosa", passed > 0, passed + " prove passate");
            Check("nessuna prova fallita", Integer(benchRun, "failed") == 0,
                  Integer(benchRun, "failed") + " fallite");
            Check("nessun errore di banco", Integer(benchRun, "errors") == 0,
                  Integer(benchRun, "errors") + " errori di banco");
            Check("il giro e' uscito 0", Integer(benchRun, "exit_code") == 0,
                  "uscita " + Integer(benchRun, "exit_code"));

            // ── NON UN GIRO PARZIALE ─────────────────────────────────────
            //
            // `--gruppo` e' comodo mentre si lavora e non vale come consegna:
            // il riepilogo lo dice, e qui si legge.
            string summary = Convert.ToString(benchRun.ContainsKey("summary") ? benchRun["summary"] : "");
            Check("non e' un giro parziale",
                  summary.IndexOf("GIRO PARZIALE", StringComparison.Ordinal) < 0,
                  summary);

            // ── LA VERSIONE DELLA LIBRERIA E' QUELLA DI ADESSO ───────────
            string declared = AssemblyVersion();
            string didRun = Convert.ToString(benchRun.ContainsKey("library") ? benchRun["library"] : "");
            Check("il banco ha girato sulla libreria di adesso",
                  declared != null && didRun.StartsWith(declared, StringComparison.Ordinal),
                  "AssemblyInfo dice " + (declared ?? "?") + ", il giro dice " + didRun);

            // ── E NON PRIMA DELL'ULTIMA MODIFICA ALLE OPERAZIONI ────────
            //
            // Il controllo che conta: una libreria toccata dopo l'ultimo giro
            // e' una libreria non provata, anche se il numero di versione
            // combacia perche' qualcuno si e' dimenticato di alzarlo.
            DateTime? timestamp = Data(Convert.ToString(benchRun.ContainsKey("timestamp") ? benchRun["timestamp"] : ""));
            if (timestamp == null)
            {
                Check("il giro dice quando", false, "«timestamp» illeggibile");
            }
            else
            {
                // ── DUE META', E PRIMA SE NE GUARDAVA UNA (voce K14) ─────
                //
                // Il banco e' fatto di due cose: la LIBRERIA che misura e il
                // CODICE DEL BANCO che decide cosa misurare. Questa guardia
                // guardava solo la prima, quindi una prova nuova, un
                // contatore nuovo o un'attesa piu' severa entravano in
                // repository senza che niente chiedesse «e l'hai fatto
                // girare?».
                //
                // Non e' teoria: il 23/9/2026 sono entrati nel banco la
                // scrivania pulita (K12) e il conto dei documenti CAD
                // (K2-bis) mentre un giro era gia' partito, e il resoconto
                // committato raccontava un banco che nel repository non
                // c'era piu'. Non era falso — quel giro c'e' stato — ma
                // rispondeva a una domanda diversa da quella che sembrava:
                // non «questo banco e' verde» ma «un banco di qualche ora fa
                // lo era».
                LookAtTouch("le operazioni", "Oce.Solid.Agent.Core", timestamp.Value);
                LookAtTouch("il banco", "Bench", timestamp.Value);
            }

            // Fuori dall'`if`: la regola si prova anche quando il resoconto
            // non c'e'. Una guardia che si controlla solo quando c'e' gia'
            // qualcosa da guardare tace proprio nel caso peggiore.
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
        /// `AssemblyVersion` letta dal SORGENTE, non dalla DLL compilata: qui
        /// si vuole sapere cosa dichiara il repository, non cosa c'e' in `bin`
        /// — che potrebbe essere di ieri.
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
        /// Quando le operazioni sono state toccate l'ultima volta, secondo git.
        /// Null se git non c'e' o non risponde — e in quel caso il controllo
        /// lo DICE invece di passare.
        /// </summary>
        /// <summary>
        /// Quando la libreria e' stata toccata l'ultima volta SENZA che nello
        /// stesso commit entrasse anche un resoconto del banco.
        ///
        /// ── PRIMA QUESTO CONTROLLO NON POTEVA PASSARE MAI ─────────────────
        ///
        /// Chiedeva `git log -1 -- Oce.Solid.Agent.Core` e confrontava quella data
        /// con l'ora del giro. Ma il giro si lancia, scrive `last.json`, e
        /// POI si committa tutto insieme — libreria e resoconto nello stesso
        /// commit. La data del commit e' quindi per costruzione posteriore
        /// all'ora del giro che contiene. Misurato su b35550d: giro 21:12:13,
        /// commit 21:13:52, novantanove secondi dopo contro una tolleranza di
        /// sessanta.
        ///
        /// Non mancava per poco: mancava sempre. *Un controllo che non puo'
        /// diventare verde e' peggio di nessun controllo — si legge come una
        /// difesa, fallisce a ogni giro, e chi lo guarda impara a non
        /// guardarlo.*
        ///
        /// ── LA DOMANDA VERA, E COME SI RISPONDE ───────────────────────────
        ///
        /// Non «quand'e' l'ultimo commit sulla libreria», ma **la libreria e'
        /// stata toccata dopo l'ultimo giro?**. Un commit che porta con se' un
        /// resoconto nuovo E' il giro: e' la consegna, e il resoconto che
        /// contiene la valida. Un commit che tocca la libreria e NON porta un
        /// resoconto nuovo e' invece esattamente il caso da prendere —
        /// qualcuno ha cambiato il codice e non ha rilanciato.
        ///
        /// Quindi si scorrono i commit sulla libreria e ci si ferma al primo
        /// che non tocca anche `Bench/report/last.json`.
        /// </summary>
        /// <summary>
        /// «Git non ha risposto» e «non c'e' niente da trovare» erano lo
        /// stesso `null`, e il primo dei due si stampava col messaggio
        /// dell'altro.
        ///
        /// Un repository giovane — nessun commit sulla libreria senza
        /// resoconto accanto — non ha niente di non provato, ed e' un VERDE.
        /// Git che non risponde e' un ROSSO: senza di lui il controllo non
        /// esiste, e un controllo che non esiste non va scambiato per uno che
        /// non ha trovato niente.
        /// </summary>
        private sealed class Touch
        {
            public bool GitHasAnswered;
            public DateTime? When;
            /// <summary>Da dove viene la data: serve a chi legge il rosso.</summary>
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
        /// Il giro e' posteriore all'ultima modifica a `path`?
        ///
        /// LA TOLLERANZA DI UN MINUTO NON E' UN ARROTONDAMENTO: il giro
        /// scrive `last.json` e POI si committa, quindi la data del commit
        /// e' per costruzione posteriore all'ora del giro che contiene. I
        /// commit che portano il resoconto si saltano apposta; il minuto
        /// copre il caso in cui l'orologio del commit e quello del giro
        /// differiscano di poco.
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
        /// IL VERDETTO, STACCATO DA CHI LEGGE GIT E IL DISCO.
        ///
        /// Sta da solo perche' un controllo che non si puo' far fallire
        /// apposta non e' un controllo, e far fallire apposta `git log`
        /// vorrebbe dire costruire un repository finto. Cosi' invece si
        /// passano due date e si guarda cosa risponde.
        ///
        /// ── IL BUCO CHE QUESTA FUNZIONE CHIUDE ──────────────────────────
        ///
        /// `LastChangeWithoutReport` chiede a **`git log`**: la storia
        /// committata, e nient'altro. Ma il codice **non vive li'** mentre lo
        /// si scrive: vive nell'albero di lavoro, ed e' da li' che `build.ps1`
        /// costruisce la DLL che poi gira. Fra la modifica e il commit c'e'
        /// una finestra in cui il controllo diceva **«non c'e' niente di non
        /// provato»** guardando un albero pieno di roba mai passata al banco.
        ///
        /// Misurato il 23/9/2026: `Oce.Solid.Agent.Core/Catalog/Operations.cs`
        /// toccato alle 19:18, ultimo giro del banco alle 15:23, e il
        /// controllo **verde** — perche' l'ultimo *commit* su quella cartella
        /// era del 15/9. Verde per il motivo sbagliato, che e' il modo in cui
        /// una guardia smette di servire senza che nessuno se ne accorga.
        ///
        /// E la finestra non era stretta: in quel momento l'albero teneva
        /// 29 file modificati e 31 nuovi.
        /// </summary>
        /// <summary>
        /// Quali di questi nomi contano per «il giro e' vecchio?».
        ///
        /// Sta da solo per poterlo provare: la regola e' una riga, e una
        /// riga dentro un `foreach` non si puo' interrogare.
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
        /// Il piu' recente fra i file sporchi sotto `path`, o null se
        /// l'albero e' pulito li' sotto.
        ///
        /// TRE COMANDI E NON `status --porcelain`, e il motivo e' `Git()`:
        /// quel lettore fa `Trim()` su ogni riga, e `--porcelain` mette lo
        /// stato in **colonne fisse** che il Trim mangia. Questi tre danno
        /// nomi nudi, uno per riga, e non c'e' niente da sbagliare a
        /// riconoscerli:
        ///
        ///     diff --name-only              modificati non in indice
        ///     diff --name-only --cached     modificati in indice
        ///     ls-files --others             nuovi, non ancora tracciati
        ///
        /// Un file CANCELLATO si salta: `File.Exists` e' falso e non c'e'
        /// nessun codice nuovo da provare. Una cancellazione va provata anche
        /// lei, ma non con una data di modifica che non esiste piu'.
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

                // UN `.md` NON E' CODICE, e chiederne conto costa settanta
                // minuti di CAD.
                //
                // Questa guardia esiste per una cosa sola: **non consegnare
                // codice su cui il banco non ha girato**. Un LEGGIMI non
                // cambia cosa fa il banco — e il 24/9/2026 ha fatto scattare
                // il rosso perche' un conteggio nel LEGGIMI era stato
                // corretto DUE MINUTI dopo un giro verde, per allinearlo a
                // due casi che quel giro aveva gia' provato.
                //
                // E' lo stesso principio della guardia Z-A1, che conta le
                // righe di codice e non quelle grezze perche' *contare le
                // grezze punisce chi spiega*.
                //
                // ATTENZIONE A COSA **NON** SI SALTA: i `.json` di
                // `Bench/cases/` sono DATI che cambiano cosa il banco misura,
                // e restano contati. Si salta solo `.md`.
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
        /// E ADESSO LA REGOLA SI FA FALLIRE APPOSTA, in tutt'e due i versi.
        ///
        /// L'ultimo caso e' quello vero: e' il verde falso che c'era prima.
        /// Quando git non trovava **nessun** commit non provato, la guardia
        /// rispondeva «non c'e' niente di non provato» — e quella frase era
        /// falsa esattamente quando l'albero era pieno.
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

            // ── E UN `.md` NON CONTA, MA UN `.json` SI' ──────────────────
            //
            // Le due meta' della stessa regola, e la seconda e' quella che
            // impedisce all'eccezione di allargarsi: i casi dichiarati sono
            // `.json`, cambiano cosa il banco misura, e devono continuare a
            // pretendere un giro nuovo.
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

        // Both paths, on purpose: the marker keeps window-1-cases.js from
        // rewriting the old one into the new one on a rerun.
        private static readonly string[] ReportPaths =
            { "Bench/report/last.json", "Banco/resoconto/ultimo.json" }; // window-1: keep

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
