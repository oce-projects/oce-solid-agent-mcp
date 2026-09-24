using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static Prove.Contratti.Programma;

namespace Prove.Contratti
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
    /// L'unica prova che il banco sia girato e' `Banco/resoconto/ultimo.json`,
    /// che `banco.ps1` scrive a ogni giro ed e' l'unico file di `resoconto/`
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
    internal static class ProveIlBanco
    {
        internal static void Tutte()
        {
            Gruppo("il banco e' stato lanciato su questa versione");

            var file = Path.Combine(Radice(), "Banco", "resoconto", "ultimo.json");
            if (!File.Exists(file))
            {
                Segna("il banco e' stato eseguito", false,
                      "il banco non e' stato eseguito su questa versione: manca " + file +
                      ". Si lancia con .\\Banco\\banco.ps1");
                return;
            }
            Segna("il banco e' stato eseguito", true, file);

            Dictionary<string, object> giro;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                giro = s.DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
            }
            catch (Exception ex) { Segna("il resoconto si legge", false, ex.Message); return; }
            if (giro == null) { Segna("il resoconto e' un oggetto", false, ""); return; }

            // ── PIU' DI ZERO PROVE ───────────────────────────────────────
            //
            // «0 prove, 0 fallite» e' verde e non vuol dire niente. E' la
            // forma che prende una suite che si e' svuotata.
            int passate = Intero(giro, "passate");
            Segna("il giro ha misurato qualcosa", passate > 0, passate + " prove passate");
            Segna("nessuna prova fallita", Intero(giro, "fallite") == 0,
                  Intero(giro, "fallite") + " fallite");
            Segna("nessun errore di banco", Intero(giro, "errori") == 0,
                  Intero(giro, "errori") + " errori di banco");
            Segna("il giro e' uscito 0", Intero(giro, "uscita") == 0,
                  "uscita " + Intero(giro, "uscita"));

            // ── NON UN GIRO PARZIALE ─────────────────────────────────────
            //
            // `--gruppo` e' comodo mentre si lavora e non vale come consegna:
            // il riepilogo lo dice, e qui si legge.
            string riepilogo = Convert.ToString(giro.ContainsKey("riepilogo") ? giro["riepilogo"] : "");
            Segna("non e' un giro parziale",
                  riepilogo.IndexOf("GIRO PARZIALE", StringComparison.Ordinal) < 0,
                  riepilogo);

            // ── LA VERSIONE DELLA LIBRERIA E' QUELLA DI ADESSO ───────────
            string dichiarata = VersioneDellAssembly();
            string girata = Convert.ToString(giro.ContainsKey("libreria") ? giro["libreria"] : "");
            Segna("il banco ha girato sulla libreria di adesso",
                  dichiarata != null && girata.StartsWith(dichiarata, StringComparison.Ordinal),
                  "AssemblyInfo dice " + (dichiarata ?? "?") + ", il giro dice " + girata);

            // ── E NON PRIMA DELL'ULTIMA MODIFICA ALLE OPERAZIONI ────────
            //
            // Il controllo che conta: una libreria toccata dopo l'ultimo giro
            // e' una libreria non provata, anche se il numero di versione
            // combacia perche' qualcuno si e' dimenticato di alzarlo.
            DateTime? quando = Data(Convert.ToString(giro.ContainsKey("quando") ? giro["quando"] : ""));
            if (quando == null)
            {
                Segna("il giro dice quando", false, "«quando» illeggibile");
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
                GuardaIlTocco("le operazioni", "Riscontro.Disegno", quando.Value);
                GuardaIlTocco("il banco", "Banco", quando.Value);
            }

            // Fuori dall'`if`: la regola si prova anche quando il resoconto
            // non c'e'. Una guardia che si controlla solo quando c'e' gia'
            // qualcosa da guardare tace proprio nel caso peggiore.
            ProvaLaRegolaDelTocco();
        }

        private static int Intero(Dictionary<string, object> d, string chiave)
        {
            object v;
            if (d == null || !d.TryGetValue(chiave, out v) || v == null) return -1;
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
        private static string VersioneDellAssembly()
        {
            try
            {
                var f = Path.Combine(Radice(), "Riscontro.Disegno", "Properties", "AssemblyInfo.cs");
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
        /// Chiedeva `git log -1 -- Riscontro.Disegno` e confrontava quella data
        /// con l'ora del giro. Ma il giro si lancia, scrive `ultimo.json`, e
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
        /// che non tocca anche `Banco/resoconto/ultimo.json`.
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
        private sealed class Tocco
        {
            public bool GitHaRisposto;
            public DateTime? Quando;
            /// <summary>Da dove viene la data: serve a chi legge il rosso.</summary>
            public string Perche;
        }

        private static Tocco UltimaModificaSenzaResoconto(string percorso)
        {
            var fuori = new Tocco();
            try
            {
                var righe = Git("log -30 --format=%H|%cI -- " + percorso);
                fuori.GitHaRisposto = true;
                foreach (var riga in righe)
                {
                    var pezzi = riga.Split('|');
                    if (pezzi.Length < 2) continue;
                    if (CommitPortaIlResoconto(pezzi[0])) continue;
                    fuori.Quando = Data(pezzi[1]);
                    fuori.Perche = "commit";
                    return fuori;
                }
                return fuori;
            }
            catch { return new Tocco(); }
        }

        /// <summary>
        /// Il giro e' posteriore all'ultima modifica a `percorso`?
        ///
        /// LA TOLLERANZA DI UN MINUTO NON E' UN ARROTONDAMENTO: il giro
        /// scrive `ultimo.json` e POI si committa, quindi la data del commit
        /// e' per costruzione posteriore all'ora del giro che contiene. I
        /// commit che portano il resoconto si saltano apposta; il minuto
        /// copre il caso in cui l'orologio del commit e quello del giro
        /// differiscano di poco.
        /// </summary>
        private static void GuardaIlTocco(string cosa, string percorso, DateTime quando)
        {
            var t = PiuRecente(UltimaModificaSenzaResoconto(percorso),
                               UltimaModificaNellAlberoDiLavoro(percorso));
            if (!t.GitHaRisposto)
            {
                Segna("so quando " + cosa + " e' stato toccato", false,
                      "git non risponde: senza, questo controllo non esiste");
                return;
            }
            if (t.Quando == null)
            {
                Segna("il giro e' posteriore all'ultima modifica a " + cosa, true,
                      "nessun commit su " + percorso + " senza il resoconto accanto: " +
                      "non c'e' niente di non provato");
                return;
            }
            Segna("il giro e' posteriore all'ultima modifica a " + cosa,
                  quando >= t.Quando.Value.AddMinutes(-1),
                  "giro " + quando.ToString("s") +
                  ", ultima modifica " + t.Quando.Value.ToString("s") +
                  (string.IsNullOrEmpty(t.Perche) ? "" : " (" + t.Perche + ")"));
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
        /// `UltimaModificaSenzaResoconto` chiede a **`git log`**: la storia
        /// committata, e nient'altro. Ma il codice **non vive li'** mentre lo
        /// si scrive: vive nell'albero di lavoro, ed e' da li' che `build.ps1`
        /// costruisce la DLL che poi gira. Fra la modifica e il commit c'e'
        /// una finestra in cui il controllo diceva **«non c'e' niente di non
        /// provato»** guardando un albero pieno di roba mai passata al banco.
        ///
        /// Misurato il 23/9/2026: `Riscontro.Disegno/Catalogo/Operazioni.cs`
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
        internal static List<string> SoloIlCodice(IEnumerable<string> nomi)
        {
            var fuori = new List<string>();
            foreach (var n in nomi)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                if (n.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                fuori.Add(n);
            }
            return fuori;
        }

        private static Tocco PiuRecente(Tocco daGit, DateTime? daLavoro)
        {
            if (daLavoro == null) return daGit;
            if (daGit.Quando != null && daGit.Quando.Value >= daLavoro.Value) return daGit;
            return new Tocco
            {
                GitHaRisposto = daGit.GitHaRisposto,
                Quando = daLavoro,
                Perche = "modificato e NON committato",
            };
        }

        /// <summary>
        /// Il piu' recente fra i file sporchi sotto `percorso`, o null se
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
        private static DateTime? UltimaModificaNellAlberoDiLavoro(string percorso)
        {
            var nomi = new List<string>();
            try
            {
                nomi.AddRange(Git("diff --name-only -- " + percorso));
                nomi.AddRange(Git("diff --name-only --cached -- " + percorso));
                nomi.AddRange(Git("ls-files --others --exclude-standard -- " + percorso));
            }
            catch { return null; }

            DateTime? piu = null;
            foreach (var nome in nomi)
            {
                if (string.IsNullOrWhiteSpace(nome)) continue;

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
                // `Banco/casi/` sono DATI che cambiano cosa il banco misura,
                // e restano contati. Si salta solo `.md`.
                if (nome.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;

                var pieno = Path.Combine(Radice(),
                                         nome.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(pieno)) continue;
                var q = File.GetLastWriteTime(pieno);
                if (piu == null || q > piu.Value) piu = q;
            }
            return piu;
        }

        /// <summary>
        /// E ADESSO LA REGOLA SI FA FALLIRE APPOSTA, in tutt'e due i versi.
        ///
        /// L'ultimo caso e' quello vero: e' il verde falso che c'era prima.
        /// Quando git non trovava **nessun** commit non provato, la guardia
        /// rispondeva «non c'e' niente di non provato» — e quella frase era
        /// falsa esattamente quando l'albero era pieno.
        /// </summary>
        private static void ProvaLaRegolaDelTocco()
        {
            var git = new Tocco
            {
                GitHaRisposto = true,
                Quando = new DateTime(2026, 9, 15, 8, 27, 48),
                Perche = "commit",
            };
            var sporco = new DateTime(2026, 9, 23, 19, 18, 20);

            var conSporco = PiuRecente(git, sporco);
            Segna("un file toccato e non committato VINCE sull'ultimo commit",
                  conSporco.Quando == sporco &&
                  conSporco.Perche == "modificato e NON committato",
                  "commit 15/9 08:27, file 23/9 19:18: vale il file");

            var pulito = PiuRecente(git, null);
            Segna("e con l'albero pulito resta la data del commit",
                  pulito.Quando == git.Quando && pulito.Perche == "commit",
                  "niente di sporco: vale il commit, come prima");

            var sporcoVecchio = PiuRecente(git, new DateTime(2026, 9, 10, 0, 0, 0));
            Segna("un file sporco PIU' VECCHIO del commit non sposta niente",
                  sporcoVecchio.Quando == git.Quando,
                  "vale il piu' recente dei due, non l'ultimo guardato");

            var senzaCommit = PiuRecente(
                new Tocco { GitHaRisposto = true, Quando = null }, sporco);
            Segna("«nessun commit non provato» NON copre un file sporco",
                  senzaCommit.Quando == sporco,
                  "era il verde falso: git taceva e l'albero era pieno");

            // ── E UN `.md` NON CONTA, MA UN `.json` SI' ──────────────────
            //
            // Le due meta' della stessa regola, e la seconda e' quella che
            // impedisce all'eccezione di allargarsi: i casi dichiarati sono
            // `.json`, cambiano cosa il banco misura, e devono continuare a
            // pretendere un giro nuovo.
            Segna("un LEGGIMI toccato dopo il giro non lo invalida",
                  SoloIlCodice(new[] { "Banco/LEGGIMI.md" }).Count == 0,
                  "una riga di documentazione non cambia cosa fa il banco");
            Segna("ma un caso dichiarato si'",
                  SoloIlCodice(new[] { "Banco/casi/corpi/x.json" }).Count == 1,
                  "un `.json` di `casi/` e' un DATO: cambia cosa si misura");
            Segna("e un sorgente pure",
                  SoloIlCodice(new[] { "Banco/Program.cs" }).Count == 1,
                  "l'eccezione e' stretta a `.md` e non si allarga da sola");
        }

        /// <summary>Questo commit porta anche `Banco/resoconto/ultimo.json`?</summary>
        private static bool CommitPortaIlResoconto(string sha)
        {
            foreach (var r in Git("show --name-only --format= " + sha +
                                  " -- Banco/resoconto/ultimo.json"))
                if (!string.IsNullOrWhiteSpace(r)) return true;
            return false;
        }

        private static List<string> Git(string argomenti)
        {
            var fuori = new List<string>();
            var p = new Process
            {
                StartInfo = new ProcessStartInfo("git", argomenti)
                {
                    WorkingDirectory = Radice(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            p.Start();
            string riga;
            while ((riga = p.StandardOutput.ReadLine()) != null) fuori.Add(riga.Trim());
            p.WaitForExit(10000);
            return fuori;
        }
    }
}
