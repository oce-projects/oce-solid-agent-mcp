using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Oce.Solid.Agent.ContractTests
{
    /// <summary>
    /// LE PROVE CHE GIRANO SU UNA MACCHINA QUALUNQUE (voce F7).
    ///
    /// ── PERCHE' ESISTONO ──────────────────────────────────────────────────
    ///
    /// Sono le uniche prove che girano **senza licenza, senza CAD, in
    /// secondi**. Il banco vero apre SOLIDWORKS e dura un quarto d'ora: va
    /// benissimo la sera prima di committare, e non serve a niente in una CI
    /// o sulla macchina di chi apre questo repository per la prima volta.
    ///
    /// E il valutatore delle attese **deve** stare qui: se e' rotto lui, tutta
    /// la suite con SOLIDWORKS diventa verde gratis.
    ///
    /// ── COSA PROVA ────────────────────────────────────────────────────────
    ///
    ///   * il **catalogo**: campi, famiglie, nessun percorso di questa macchina
    ///     dentro le descrizioni, e `Similar()` che su «asole» trova «slot»;
    ///   * `Session.SameDocument()` — la guardia contro il blocco estruso
    ///     nel pezzo di qualcun altro, che fino a oggi non aveva una prova;
    ///   * il **protocollo MCP**, parlando col ponte su stdin/stdout come
    ///     farebbe un cliente;
    ///   * il **valutatore delle attese**, contro stati finti scritti a mano.
    ///
    /// ── E SCRIVE `prove.xml` ─────────────────────────────────────────────
    ///
    /// In formato JUnit, che e' quello che ogni CI sa leggere senza che
    /// nessuno le insegni niente.
    /// </summary>
    internal static class Program
    {
        internal static int FailedOnes;

        /// <summary>
        /// Quanti controlli NON hanno potuto girare su questa macchina.
        /// Non sono verdi e non sono rossi: vedi <see cref="NotHere"/>.
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

        private static string _group = "generale";

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
            // UN VERDE SI ACCORCIA, UN ROSSO NO. Il dettaglio di una prova
            // rossa e' l'unica cosa che si legge davvero, e troncarlo a
            // centodieci caratteri vuol dire rilanciare per vedere il resto.
            Console.WriteLine((ok ? "  OK   " : "  NO   ") + itemName.PadRight(58) +
                              (ok ? Truncated(detail) : detail));
        }

        /// <summary>
        /// UN CONTROLLO CHE **NON POTEVA** GIRARE QUI NON E' VERDE E NON E' ROSSO.
        ///
        /// ── IL DIFETTO CHE HA FATTO NASCERE QUESTA TERZA VOCE ─────────────
        ///
        /// 23/9/2026, voce F9-bis. La CI di questo repository non e' MAI
        /// diventata verde, e il motivo non era un difetto del codice: il suo
        /// primo passo era `msbuild Oce.Solid.Agent.csproj`, e quel progetto
        /// referenzia `SolidWorks.Interop.*` con un `HintPath` sull'installazione
        /// di SOLIDWORKS. Su un runner di GitHub quella cartella non c'e' e non
        /// ci sara'. **Misurato, non dedotto**: compilando con
        /// `/p:SwInstallPath=Z:\non-esiste` escono duecento e passa
        /// `CS0246: il nome di tipo IModelDoc2 non e' stato trovato`.
        ///
        /// Il README intanto diceva «la CI compila, lancia i controlli di
        /// contratto e rifiuta una consegna in cui nessuno ha lanciato il
        /// banco». Le ultime due erano vere in teoria e irraggiungibili in
        /// pratica, perche' la prima non arrivava mai in fondo. **Una CI che
        /// non e' mai girata e' un distintivo, non una prova.**
        ///
        /// ── PERCHE' NON BASTA SALTARE ─────────────────────────────────────
        ///
        /// La tentazione e' `if (!File.Exists(ponte)) return;`: la suite
        /// diventa verde e nessuno sa piu' che meta' dei controlli non e'
        /// stata fatta. E' il difetto che questo repository rimprovera
        /// altrove, applicato alla propria CI.
        ///
        /// Quindi: la voce c'e' lo stesso, si stampa `--`, si conta a parte, e
        /// finisce in `prove.xml` come `<skipped>` — che ogni CI sa mostrare.
        ///
        /// ── E «NON POTEVA» SI DIMOSTRA, NON SI DICHIARA ───────────────────
        ///
        /// La distinzione che conta e' fra **non si puo' qui** e **ti sei
        /// dimenticato di compilare**. Danno lo stesso sintomo — l'eseguibile
        /// non c'e' — e sono due difetti opposti. Si separano guardando
        /// l'interop: se sulla macchina non c'e', il ponte NON POTEVA esistere;
        /// se c'e', l'eseguibile manca perche' qualcuno non ha lanciato
        /// `build.ps1`, e quello **resta rosso**.
        /// </summary>
        internal static void NotHere(string itemName, string why)
        {
            NotRunnableHere++;
            Outcomes.Add(new Outcome { Group = _group, Name = itemName, Ok = true,
                                  NotHere = true, Detail = why });
            Console.WriteLine("  --   " + itemName.PadRight(58) + why);
        }

        /// <summary>
        /// Dove sta l'interop di Dassault su QUESTA macchina, o `null`.
        ///
        /// Stesso ordine dei `.csproj` — `SW_INSTALL_PATH`, poi `SW_REDIST`,
        /// poi il posto di serie — perche' la domanda e' esattamente «msbuild
        /// l'avrebbe trovata?», e cercarla con un ordine diverso da quello
        /// della build risponderebbe a un'altra domanda.
        /// </summary>
        /// <summary>
        /// «Comportati come un runner senza SOLIDWORKS», qui e adesso.
        ///
        /// Le voci «non eseguibile qui» le vedrebbe solo una macchina nuda, e
        /// questa non lo e': senza questo interruttore il codice che le scrive
        /// resterebbe non provato fino alla prima corsa di CI — cioe' si
        /// dovrebbe credere che funzioni. **Un controllo mai visto scattare non
        /// e' un controllo.**
        ///
        /// Non e' una scorciatoia per far passare qualcosa: sotto simulazione il
        /// riepilogo lo grida e i saltati si contano lo stesso, quindi usarla
        /// per «diventare verdi» renderebbe il buco piu' visibile, non meno.
        /// </summary>
        internal static bool PretendWithoutInterop
        {
            get { return Environment.GetEnvironmentVariable("PROVE_SENZA_INTEROP") == "1"; }
        }

        /// <summary>
        /// Un file che lo produce `build.ps1`, e quindi che su una macchina
        /// senza interop non puo' esistere. Sotto simulazione non esiste mai.
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
        /// Vero se su questa macchina il ponte SI SAREBBE POTUTO compilare.
        /// Non dice che sia stato compilato: dice che, se manca, la colpa e'
        /// di chi non ha lanciato `build.ps1` e non della macchina.
        /// </summary>
        internal static bool TheBridgeCouldBeBuilt()
        {
            return WhereTheInteropIs() != null;
        }

        /// <summary>
        /// L'eseguibile del ponte, oppure una voce «non qui» e `null`.
        ///
        /// Tre gruppi facevano la stessa cosa in tre modi leggermente diversi
        /// (`ProtocolTests`, `SessionTests`, `CatalogTests`): la regola su
        /// quando un ponte mancante e' colpa della macchina stava scritta tre
        /// volte, ed e' il modo in cui due delle tre restano indietro.
        /// </summary>
        internal static string BridgeOrNotHere(string whatDoesNotRun)
        {
            var bridge = Path.Combine(Root(), "bin", "oce-solid-agent-mcp.exe");
            if (ProductFromBuild(bridge)) return bridge;
            if (!TheBridgeCouldBeBuilt())
            {
                NotHere(whatDoesNotRun,
                       "qui SOLIDWORKS non c'e': senza api\\redist\\SolidWorks.Interop.* il " +
                       "ponte non si compila, quindi non esiste nessun eseguibile a cui " +
                       "parlare. Non e' un difetto del ponte, e fingere di averlo provato " +
                       "sarebbe peggio del non averlo provato.");
                return null;
            }
            Check("il ponte c'e'", false,
                  "non trovo " + bridge + " — ma SOLIDWORKS su questa macchina c'e' (" +
                  WhereTheInteropIs() + "), quindi manca solo .\\build.ps1");
            return null;
        }

        /// <summary>
        /// Il catalogo che il ponte serve: `bin\catalog.json`, e se non c'e' la
        /// copia versionata `Tools\catalog.json` — che e' esattamente il
        /// file che `build.ps1` rigenera e copia in `bin\`.
        ///
        /// QUATTRO CONTROLLI TENEVANO ROSSA LA CI DA CINQUE PUSH (misurato il
        /// 25/9/2026, dalla 0.85.0 alla 0.88.0): cercavano solo `bin\`, che in
        /// un clone pulito non esiste, e dicevano «il catalogo servito non
        /// c'e'». Non era un difetto del catalogo, e nemmeno una cosa che la CI
        /// non potesse fare: il file da leggere c'era, in `Tools\`.
        /// `DescriptionsTests` lo faceva gia' giusto. *Una CI sempre rossa si
        /// impara a non guardarla, ed e' il modo in cui un rosso vero passa.*
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

        /// <summary>La radice del repository, due livelli sopra `Tests\bin\`.</summary>
        internal static string Root()
        {
            var bin = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            return Directory.GetParent(Directory.GetParent(bin).FullName).FullName;
        }

        /// <summary>
        /// La versione del prodotto, letta da `VERSION.txt` nel repository.
        ///
        /// Null se non si legge: chi la usa deve poter dire «non lo so»
        /// invece di confrontare con una stringa vuota e trovarla diversa
        /// da tutto.
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
        /// `Tests/README.md` dice quanti controlli ci sono. Deve essere vero.
        ///
        /// Il 16/9/2026 diceva **54** mentre ne giravano **99**: un numero
        /// giusto quando fu scritto, invecchiato da solo mentre la suite
        /// cresceva. Chi legge quel file non ha modo di accorgersene — e' la
        /// sua unica fonte. E' lo stesso difetto del README che prometteva
        /// trentotto operazioni su sessanta, e la difesa e' la stessa: far
        /// leggere il documento a un controllo.
        ///
        /// SI CONTA ANCHE QUESTO CONTROLLO. Gira per ultimo, quindi quando
        /// guarda `Outcomes.Count` se stesso non c'e' ancora: il numero scritto
        /// nel documento deve valere `Esiti.Count + 1`. Non e' una furbizia,
        /// e' l'unico conto che torna — e il documento deve dire il totale che
        /// l'utente legge a schermo, non quello meno uno.
        /// </summary>
        private static void TheReadmeSaysHowManyThereAre()
        {
            Group("il LEGGIMI delle prove");

            // IL CONTO VALE PER UN GIRO INTERO (voce F9-bis). Dove SOLIDWORKS
            // non c'e', un gruppo da quaranta controlli diventa UNA riga «non
            // eseguibile qui»: il totale crolla, il LEGGIMI ha ancora ragione, e
            // questo controllo diventerebbe rosso accusando il documento giusto.
            // Un controllo che sa di non poter rispondere lo dice.
            if (NotRunnableHere > 0)
            {
                NotHere("il LEGGIMI dice quanti controlli ci sono, e sono quelli veri",
                       "su questa macchina " + NotRunnableHere + " controlli non hanno " +
                       "potuto girare: il numero scritto nel LEGGIMI e' quello di un giro " +
                       "intero e qui non e' confrontabile.");
                return;
            }

            int real = Outcomes.Count + 1;

            var readme = Path.Combine(Root(), "Tests", "README.md");
            if (!File.Exists(readme))
            {
                Check("il LEGGIMI c'e'", false, readme);
                return;
            }

            string text;
            try { text = File.ReadAllText(readme); }
            catch (Exception ex) { Check("il LEGGIMI si legge", false, ex.Message); return; }

            // Si cerca SOLO la forma «<numero> controlli»: il file parla anche
            // di secondi e di licenze, e una regola larga griderebbe su una
            // frase giusta.
            var foundItems = System.Text.RegularExpressions.Regex.Matches(
                text, @"(\d+)\s+controlli");
            if (foundItems.Count == 0)
            {
                Check("il LEGGIMI dice quanti controlli ci sono", false,
                      "non trovo nessun «N controlli»: se il numero sparisce, " +
                      "sparisce anche questo controllo");
                return;
            }

            var wrong = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in foundItems)
                if (Convert.ToInt32(m.Groups[1].Value) != real)
                    wrong.Add("«" + m.Value + "»");

            Check("il LEGGIMI dice quanti controlli ci sono, e sono quelli veri",
                  wrong.Count == 0,
                  wrong.Count == 0
                      ? real + " controlli"
                      : "ne girano " + real + ", il LEGGIMI dice " +
                        string.Join(" e ", wrong));
        }

        private static int Main(string[] arguments)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = Encoding.UTF8;

            Console.WriteLine("Oce.Solid.Agent.ContractTests — senza SOLIDWORKS, su questa macchina qualunque");
            Console.WriteLine("radice: " + Root());

            if (PretendWithoutInterop)
            {
                Console.WriteLine();
                Console.WriteLine("### PROVE_SENZA_INTEROP=1 — GIRO SIMULATO ###");
                Console.WriteLine("Questa macchina fa finta di non avere SOLIDWORKS, per far");
                Console.WriteLine("vedere come si comporta la suite su un runner nudo. I");
                Console.WriteLine("risultati di questo giro NON valgono come collaudo.");
            }

            NoInterop();
            EverySourceIsInItsOwnProject();
            EveryEnvironmentVariableIsInTheReadme();
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
            AliasLayerContract.RunAll();
            DescriptionValuesContract.RunAll();
            Zc2189WeldAssemblyContract.RunAll();
            Zc2189DrawingContract.RunAll();
            Zc2189MachiningContract.RunAll();
            Zc215IdentityContract.RunAll();
            Zc215SketchIdentityContract.RunAll();
            Zc215ActivationRebuildContract.RunAll();
            Zc215HolePointIdentityContract.RunAll();
            Zc2113PartDimensionContract.RunAll();
            Zc2111ComponentsContract.RunAll();
            Zc14MateDirectionsContract.RunAll();
            Zc2114UndoHonestyContract.RunAll();
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
            var summary = Outcomes.Count + " controlli, " + FailedOnes + " falliti";
            if (NotRunnableHere > 0)
                summary += ", " + NotRunnableHere + " non eseguibili qui (" +
                             done + " fatti davvero)";
            Console.WriteLine(summary);

            // IL NUMERO DA SOLO SI LEGGEREBBE COME UN VERDE. Chi guarda una CI
            // legge «0 falliti» e va avanti: se meta' dei controlli non ha
            // girato, quel numero e' vero e ingannevole insieme, ed e'
            // esattamente il difetto che questo repository rimprovera altrove.
            if (NotRunnableHere > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  «NON ESEGUIBILI QUI» non vuol dire passati.");
                Console.WriteLine("  Su questa macchina SOLIDWORKS non c'e', quindi il ponte non si");
                Console.WriteLine("  poteva nemmeno COMPILARE e i controlli che gli parlano non hanno");
                Console.WriteLine("  girato. Questo numero e' l'unico posto in cui la differenza fra");
                Console.WriteLine("  «provato e passa» e «non provato» resta visibile.");
            }

            // IL MINIMO E' DICHIARATO E SI CONTROLLA. Una suite che si svuota
            // — un file di prove tolto, un gruppo che esce prima — resterebbe
            // verde con tre controlli, e «0 falliti» suonerebbe uguale.
            //
            // SI CONTANO I FATTI, NON LE VOCI (voce F9-bis). Contando
            // `Outcomes.Count` la soglia l'avrebbero superata anche venticinque
            // righe «non eseguibile qui»: un minimo che si lascia soddisfare
            // da cio' che non ha girato non e' un minimo.
            const int DeclaredMinimum = 25;
            if (done < DeclaredMinimum)
            {
                Console.WriteLine("TROPPO POCHI: ne erano dichiarati almeno " + DeclaredMinimum +
                                  " davvero eseguiti, e ne risultano " + done +
                                  ". Una suite che si svuota resta verde, ed e' il modo piu' " +
                                  "silenzioso di smettere di provare.");
                FailedOnes++;
            }

            WriteJUnit();
            return FailedOnes == 0 ? 0 : 1;
        }

        /// <summary>
        /// QUESTO PROGETTO NON REFERENZIA L'INTEROP, E LO CONTROLLA DA SOLO.
        ///
        /// E' la ragione per cui esiste: girare dove SOLIDWORKS non c'e'. Se
        /// qualcuno aggiunge un riferimento al csproj — perche' «serviva un
        /// tipo» — la promessa cade, e su questa macchina non lo noterebbe
        /// nessuno: qui SOLIDWORKS c'e', e compila lo stesso.
        ///
        /// Si guardano i riferimenti dell'assembly, non il testo del csproj: il
        /// csproj si puo' leggere in dieci modi, i riferimenti veri sono uno.
        /// </summary>
        private static void NoInterop()
        {
            Group("questo progetto gira dove SOLIDWORKS non c'e'");
            var myOwn = typeof(Program).Assembly;
            var dassault = myOwn.GetReferencedAssemblies()
                .Where(a => a.Name.StartsWith("SolidWorks", StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Name)
                .ToList();
            Check("nessun riferimento a SolidWorks.Interop",
                  dassault.Count == 0,
                  dassault.Count == 0
                      ? myOwn.GetReferencedAssemblies().Length + " riferimenti, nessuno di Dassault"
                      : "trovati: " + string.Join(", ", dassault));
        }

        /// <summary>
        /// OGNI SORGENTE STA NEL SUO `.csproj`.
        ///
        /// ── IL DIFETTO CHE QUESTO CONTROLLO PRENDE ───────────────────────
        ///
        /// Un file `.cs` che non compare fra i `<Compile Include>` **non viene
        /// compilato**. Se qualcuno lo referenzia, MSBuild lo dice subito ed e'
        /// successo davvero il 16/9/2026 con `AssemblyOperations.cs`: due errori,
        /// riparato in trenta secondi.
        ///
        /// **Il caso pericoloso e' l'altro.** Se nessuno lo referenzia ancora —
        /// una classe nuova, un'operazione appena scritta e non ancora
        /// collegata al dispatcher — **la build resta verde e il codice non
        /// esiste**. Si scrive un file, si compila, tutto passa, e quel file non
        /// e' nel programma. Nessun errore, da nessuna parte.
        ///
        /// E' il difetto che non da' errore applicato al PROGETTO invece che al
        /// pezzo, e non c'era nessun controllo che lo prendesse.
        ///
        /// ── PERCHE' STA QUI E NON IN UNO SCRIPT ──────────────────────────
        ///
        /// Uno script bisogna ricordarsi di lanciarlo, ed e' esattamente il
        /// modo in cui il gancio `pre-push` e' nato (voce W2.10): il controllo
        /// c'era, mancava che qualcuno lo eseguisse. Qui gira a ogni giro.
        /// </summary>
        private static void EverySourceIsInItsOwnProject()
        {
            Group("i sorgenti stanno nei progetti");

            // cartella (relativa alla radice) -> csproj che la deve elencare.
            // I `Checks\<attrezzo>\` hanno un csproj per attrezzo e stanno
            // fuori dalla soluzione apposta: non si controllano qui.
            var map = new[]
            {
                new { Dove = new[] { @"Oce.Solid.Agent.Core", @"Oce.Solid.Agent.Core\Catalog",
                                     @"Oce.Solid.Agent.Core\Properties", @"Oce.Solid.Agent.Core\Api" },
                      Progetto = @"Oce.Solid.Agent.Core\Oce.Solid.Agent.Core.csproj" },
                new { Dove = new[] { @"Bridge", @"Tools", @"Mcp" },
                      Progetto = @"Oce.Solid.Agent.csproj" },
                new { Dove = new[] { @"Bench" }, Progetto = @"Bench\Oce.Solid.Agent.Bench.csproj" },
                new { Dove = new[] { @"Tests" }, Progetto = @"Tests\Oce.Solid.Agent.ContractTests.csproj" },

                // `Shared\` LA COMPILANO IN DUE, e qui basta nominarne uno.
                //
                // Il controllo e' a senso unico: «ogni sorgente sotto questa
                // cartella compare in quel progetto». Che il banco compili gli
                // stessi file non lo rende falso, e chiedere che una cartella
                // stia in un progetto SOLO renderebbe rosso proprio il modo in
                // cui `Expectations.cs` e' condiviso da giorni.
                //
                // Senza questa riga `Shared\` sarebbe l'unica cartella di
                // sorgenti che nessuno guarda: un file messo li' e dimenticato
                // fuori dai csproj non verrebbe compilato e non lo direbbe
                // nessuno — il difetto per cui questo controllo esiste.
                new { Dove = new[] { @"Shared" },
                      Progetto = @"Tests\Oce.Solid.Agent.ContractTests.csproj" },
                new { Dove = new[] { @"Installer" }, Progetto = @"Installer\Installer.csproj" },
            };

            int inspected = 0;
            var outside = new List<string>();

            foreach (var m in map)
            {
                var csproj = Path.Combine(Root(), m.Progetto);
                if (!File.Exists(csproj))
                {
                    Check("il progetto «" + m.Progetto + "» c'e'", false, csproj);
                    continue;
                }
                string text;
                try { text = File.ReadAllText(csproj); }
                catch (Exception ex) { Check("«" + m.Progetto + "» si legge", false, ex.Message); continue; }

                foreach (var folder in m.Dove)
                {
                    var full = Path.Combine(Root(), folder);
                    if (!Directory.Exists(full)) continue;
                    // SOLO il primo livello: `obj\` e `bin\` sono pieni di
                    // sorgenti generati che nessun csproj elenca, e che non
                    // devono entrare nel conto.
                    foreach (var f in Directory.GetFiles(full, "*.cs", SearchOption.TopDirectoryOnly))
                    {
                        var itemName = Path.GetFileName(f);
                        // Generati dal compilatore, mai elencati a mano.
                        if (itemName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                            itemName.IndexOf("AssemblyAttributes", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        inspected++;
                        // Si cerca il NOME fra virgolette: copre sia
                        // `Include="Pippo.cs"` sia `Include="Bridge\Pippo.cs"`.
                        if (text.IndexOf(itemName + "\"", StringComparison.OrdinalIgnoreCase) < 0)
                            outside.Add(folder + "\\" + itemName + " → " + m.Progetto);
                    }
                }
            }

            Check("ci sono sorgenti da controllare", inspected > 10, inspected + " file .cs");
            Check("ogni sorgente e' elencato nel suo .csproj",
                  outside.Count == 0,
                  outside.Count == 0
                      ? "tutti e " + inspected
                      : "FUORI DAL PROGETTO (non vengono compilati): " +
                        string.Join("; ", outside));
        }

        /// <summary>
        /// OGNI VARIABILE D'AMBIENTE CHE IL PONTE LEGGE STA NEL README.
        ///
        /// ── PERCHE' ESISTE QUESTA PROVA ──────────────────────────────────
        ///
        /// Trovate il 17/9/2026 leggendo il README per un'altra ragione:
        /// **tre variabili su otto non c'erano**, e due delle tre erano
        /// `RISCONTRO_RADICE` e `RISCONTRO_SOLA_LETTURA` — cioe' le uniche due
        /// con cui si dice al ponte dove NON deve scrivere. Chi installa legge
        /// la tabella, non i sorgenti: una difesa che c'e' nel codice e non nel
        /// README **e' una difesa che nessuno accende**, e il pezzo lo scopre
        /// riscrivendo una libreria condivisa.
        ///
        /// Non e' pignoleria documentale: e' lo stesso difetto delle altre
        /// prove di contratto qui dentro — due elenchi scritti in due posti
        /// divergono, e il momento in cui divergono nessuno lo vede.
        ///
        /// ── COSA GUARDA, E COSA NO ───────────────────────────────────────
        ///
        /// Solo i sorgenti del PONTE e della LIBRERIA: `Bench\` e `Tests\`
        /// leggono variabili che servono a chi sviluppa, non a chi installa, e
        /// pretenderle nel README vorrebbe dire riempirlo di roba che a chi
        /// legge non serve.
        ///
        /// Il nome si cerca **in tutto il README**, non solo nella tabella: una
        /// variabile spiegata per esteso in un paragrafo e' documentata. Il
        /// controllo non pretende una forma, pretende che il nome ci sia.
        /// </summary>
        private static void EveryEnvironmentVariableIsInTheReadme()
        {
            Group("le variabili d'ambiente stanno nel README");

            var readme = Path.Combine(Root(), "README.md");
            if (!File.Exists(readme)) { Check("il README c'e'", false, readme); return; }
            string text;
            try { text = File.ReadAllText(readme); }
            catch (Exception ex) { Check("il README si legge", false, ex.Message); return; }

            // `GetEnvironmentVariable("X")` e la scorciatoia `Variable("X")`
            // di `Bridge\Paths.cs`. La seconda e' proprio quella che nascondeva
            // `RISCONTRO_RADICE` e `RISCONTRO_SOLA_LETTURA` a un grep fatto sul
            // nome lungo: **cercare una sola forma vuol dire trovarne una sola.**
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

            // UNA PROVA CHE NON TROVA NIENTE PASSA SEMPRE, e non prova niente.
            // Se un giorno le cartelle cambiano nome, questa riga diventa rossa
            // invece di lasciare che il controllo si spenga in silenzio.
            Check("ci sono variabili da controllare", foundOnes.Count >= 6,
                  foundOnes.Count + ": " + string.Join(", ", foundOnes));

            var absent = foundOnes.Where(v => text.IndexOf(v, StringComparison.Ordinal) < 0).ToList();
            Check("ogni variabile letta dal ponte e' nominata nel README",
                  absent.Count == 0,
                  absent.Count == 0
                      ? "tutte e " + foundOnes.Count
                      : "NEL CODICE E NON NEL README: " + string.Join(", ", absent));
        }

        /// <summary>
        /// I NUMERI IN `Bench\README.md` SONO QUANTI SONO I FILE DAVVERO.
        ///
        /// ── PERCHE' ESISTE ───────────────────────────────────────────────
        ///
        /// Il LEGGIMI del banco ha una tabella «cartella / quanti casi / cosa
        /// provano». Guardata il 17/9/2026: **quattro numeri su venti erano
        /// sbagliati** — `features` diceva 2 e ne aveva 7, `ripeti` 3 e ne
        /// aveva 6. Nessuno se n'era accorto, perche' un numero in un documento
        /// non fallisce: invecchia.
        ///
        /// E' lo stesso difetto che il README aveva sul conto delle operazioni
        /// («trentotto» quando erano sessanta), riparato allo stesso modo: non
        /// ricontando a mano — *un conteggio fatto a mano invecchia mentre lo si
        /// scrive* — ma facendolo ricontare a ogni giro.
        ///
        /// ── LA SOMMA, E NON LA RIGA ──────────────────────────────────────
        ///
        /// Una cartella puo' comparire **due volte** nella tabella, e adesso ce
        /// n'e' una: `cases/drawing/` contiene sia i piani a mano sia i casi
        /// dichiarati, ed e' elencata in tutt'e due i blocchi con due numeri
        /// diversi. Si sommano le righe della stessa cartella e si confronta la
        /// somma: 7 + 1 = 8 file. Pretendere che ogni riga da sola combaci
        /// renderebbe rossa una tabella giusta.
        /// </summary>
        private static void TheBenchReadmeCountsTheRightFiles()
        {
            Group("il LEGGIMI del banco conta i file giusti");

            var readme = Path.Combine(Root(), "Bench", "README.md");
            if (!File.Exists(readme)) { Check("il LEGGIMI del banco c'e'", false, readme); return; }
            string text;
            try { text = File.ReadAllText(readme); }
            catch (Exception ex) { Check("il LEGGIMI del banco si legge", false, ex.Message); return; }

            // `cases/<nome>/` seguito da spazi e da un numero. Le righe di
            // continuazione (la descrizione che va a capo) non hanno il numero
            // e non corrispondono: e' voluto.
            //
            // A INIZIO RIGA, e il perche' e' misurato (25/9/2026): senza `^` il
            // conto prendeva anche la frase che RACCONTA la vecchia riga
            // («Questa riga prima la contava ... «cases/forma/ 5»»), e
            // `cases/forma/` non e' nel repository per scelta. In locale passava,
            // perche' i cinque file ci sono; in un clone pulito — la CI — era
            // «nominate e non esistenti: forma», rosso a ogni push dalla 0.85.0.
            var shape = new System.Text.RegularExpressions.Regex(
                @"^cases/([a-z_]+)/\s+(\d+)", System.Text.RegularExpressions.RegexOptions.Multiline);

            var declared = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in shape.Matches(text))
            {
                var itemName = m.Groups[1].Value;
                int n = Convert.ToInt32(m.Groups[2].Value);
                declared[itemName] = (declared.ContainsKey(itemName) ? declared[itemName] : 0) + n;
            }

            // UNA PROVA CHE NON TROVA NIENTE PASSA SEMPRE. Se la tabella cambia
            // forma, questa riga diventa rossa invece di spegnersi in silenzio.
            Check("ci sono cartelle da controllare", declared.Count >= 15,
                  declared.Count + " righe «cases/<nome>/ N»");

            var wrong = new List<string>();
            var absent = new List<string>();
            foreach (var v in declared)
            {
                var folder = Path.Combine(Root(), "Bench", "cases", v.Key);
                if (!Directory.Exists(folder)) { absent.Add(v.Key); continue; }
                int real = Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly).Length;
                if (real != v.Value)
                    wrong.Add(v.Key + ": dice " + v.Value + ", ne ha " + real);
            }

            Check("ogni cartella nominata nel LEGGIMI esiste",
                  absent.Count == 0,
                  absent.Count == 0 ? declared.Count + " cartelle"
                                     : "nominate e non esistenti: " + string.Join(", ", absent));
            Check("i numeri del LEGGIMI sono quanti sono i file",
                  wrong.Count == 0,
                  wrong.Count == 0
                      ? "tutte e " + declared.Count
                      : string.Join("; ", wrong));
        }

        /// <summary>
        /// OGNI FAMIGLIA DEL CATALOGO HA UN TIPO DI DOCUMENTO SU CUI PUO' GIRARE.
        ///
        /// ── PERCHE' ESISTE QUESTA PROVA ──────────────────────────────────
        ///
        /// Trovato il 18/9/2026 leggendo `PlanExecutor.cs` per un'altra
        /// ragione: `PreliminaryChecks()` — che gira **prima del primo
        /// passo di ogni piano**, e per tutti e quattro gli strumenti del ponte
        /// — ammette solo `swDocPART` e, se `AlsoOnDrawings`, `swDocDRAWING`.
        /// Gli assiemi no. Intanto il catalogo servito ai client elencava tre
        /// operazioni di famiglia `assemblies`.
        ///
        /// **Il ponte prometteva operazioni che avrebbe rifiutato prima di
        /// cominciare, e nessuna delle 710 prove del banco poteva vederlo: il
        /// banco chiama la LIBRERIA, non il ponte.** Due misure vere di due
        /// cose diverse, e chi legge il numero grande crede che copra anche
        /// l'altra.
        ///
        /// ── COME LO GUARDA ───────────────────────────────────────────────
        ///
        /// Dal sorgente, non da un elenco scritto a mano: i tipi ammessi si
        /// leggono da `PreliminaryChecks` con una regex sui token
        /// `swDoc...`, cosi' il giorno che qualcuno ne aggiunge o ne toglie uno
        /// questa prova se ne accorge invece di restare d'accordo con se stessa.
        ///
        /// ── COSA **NON** PRENDE, e va detto ──────────────────────────────
        ///
        /// Guarda le FAMIGLIE, non le singole operazioni. `interference_check`
        /// sta in famiglia `checks` e vuole un assieme: questa prova la
        /// classifica come «part» e la lascia passare. Il rimedio vero e' un
        /// campo `RequestedDocument` su `OperationSpec` — voce a se',
        /// perche' tocca la libreria. Finche' non c'e', **questa prova copre la
        /// famiglia e non il caso singolo**, ed e' scritto qui perche' chi la
        /// vede verde non creda che copra di piu'.
        /// </summary>
        private static void EveryCatalogFamilyHasAnAllowedDocument()
        {
            Group("il catalogo promette solo documenti che l'esecutore ammette");

            // ── 1. i tipi che l'esecutore ammette, letti dal suo sorgente ──
            var source = Path.Combine(Root(), "Oce.Solid.Agent.Core", "PlanExecutor.cs");
            if (!File.Exists(source))
            {
                Check("il sorgente dell'esecutore c'e'", false, source);
                return;
            }
            string code;
            try { code = File.ReadAllText(source); }
            catch (Exception ex) { Check("il sorgente si legge", false, ex.Message); return; }

            int da = code.IndexOf("bool allowed", StringComparison.Ordinal);
            if (da < 0)
            {
                // Se la riga cambia forma questa prova NON deve passare in
                // silenzio: una prova che non trova cio' che cerca e passa e'
                // peggio di nessuna prova.
                Check("trovo la riga che decide i tipi ammessi", false,
                      "«bool allowed» non c'e' piu' in PlanExecutor.cs: " +
                      "la prova va riscritta, non tolta");
                return;
            }
            int a = code.IndexOf(';', da);
            var expression = a > da ? code.Substring(da, a - da) : code.Substring(da);

            var allowed = new SortedSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(expression, @"swDoc[A-Z]+"))
                allowed.Add(m.Value);

            Check("l'esecutore ammette almeno un tipo di documento", allowed.Count >= 1,
                  string.Join(", ", allowed));

            // ── 2. le famiglie che il catalogo SERVE ai client ─────────────
            var json = Path.Combine(Root(), "bin", "catalog.json");
            // Come nel gruppo «il catalogo» (voce F9-bis): dove il ponte non si
            // poteva compilare, `bin\\` e' vuota e il file buono e' quello in
            // repository. Questo controllo legge un JSON e un sorgente C#: non ha
            // niente da chiedere a SOLIDWORKS, e fermarsi qui vorrebbe dire non
            // fare una verifica che si poteva fare.
            if (!ProductFromBuild(json) && !TheBridgeCouldBeBuilt())
            {
                var inRepository = Path.Combine(Root(), "Tools", "catalog.json");
                if (File.Exists(inRepository)) json = inRepository;
            }
            if (!File.Exists(json))
            {
                Check("il catalogo c'e'", false, "non trovo " + json + " — si produce con .\\build.ps1");
                return;
            }
            Dictionary<string, object> root;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                s.MaxJsonLength = 40 * 1024 * 1024;
                root = s.DeserializeObject(File.ReadAllText(json)) as Dictionary<string, object>;
            }
            catch (Exception ex) { Check("il catalogo si legge", false, ex.Message); return; }

            var list = (root != null && root.ContainsKey("operations")
                ? root["operations"] : null) as object[];
            if (list == null) { Check("il catalogo ha «operations»", false, ""); return; }

            var families = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var o in list)
            {
                var v = o as Dictionary<string, object>;
                if (v != null && v.ContainsKey("family") && v["family"] != null)
                    families.Add(Convert.ToString(v["family"]));
            }
            Check("ci sono famiglie da controllare", families.Count >= 5,
                  families.Count + ": " + string.Join(", ", families));

            // ── 3. su che tipo di documento ha senso ognuna ────────────────
            // Tre righe scritte a mano, e il loro compito e' proprio di NON
            // sapere tutto: una famiglia che non conoscono e' ROSSA, non verde.
            // Cosi' l'elenco non puo' invecchiare in silenzio — chi ne aggiunge
            // una deve dire su cosa gira.
            var serve = new Dictionary<string, string>(StringComparer.Ordinal)
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
                { "annullare",    "swDocPART"     },
                { "sheet_metal",      "swDocPART"     },
                { "dimensions",        "swDocPART"     },
            };

            var unknown = families.Where(f => !serve.ContainsKey(f)).ToList();
            Check("ogni famiglia del catalogo e' classificata qui dentro",
                  unknown.Count == 0,
                  unknown.Count == 0
                      ? "tutte e " + families.Count
                      : "FAMIGLIA NUOVA E NON CLASSIFICATA: " + string.Join(", ", unknown) +
                        " — dire su che tipo di documento gira, in Tests\\Program.cs");

            var promises = families
                .Where(f => serve.ContainsKey(f) && !allowed.Contains(serve[f]))
                .Select(f => f + " (vuole " + serve[f] + ")")
                .ToList();

            Check("nessuna famiglia servita e' rifiutata prima del primo passo",
                  promises.Count == 0,
                  promises.Count == 0
                      ? families.Count + " famiglie, tutte su un tipo ammesso (" +
                        string.Join(", ", allowed) + ")"
                      : "PROMESSE E IRRAGGIUNGIBILI: " + string.Join("; ", promises) +
                        " — l'esecutore ammette solo " + string.Join(", ", allowed));
        }

        /// <summary>
        /// DUE VOCI DEL TODO NON SI CHIAMANO ALLO STESSO MODO.
        ///
        /// ── PERCHE' ESISTE QUESTA PROVA ──────────────────────────────────
        ///
        /// Il 17/9/2026 sono state innestate 65 voci nuove (la «sezione Z»),
        /// chiamate `A1`, `B3`, `C3`… **Trentasette di quei nomi erano gia'
        /// usati, nello stesso file, da voci diverse — e trentatre' di quelle
        /// erano chiuse.** «Fai C3» portava a una voce verde del 14/9 che non
        /// c'entrava niente: chi ci arrivava concludeva che il lavoro era
        /// fatto, e la voce vera restava invisibile.
        ///
        /// Nessun errore da nessuna parte, che e' la firma di questa famiglia
        /// di difetti — la stessa di `40x40x3` contro `40X40X3` e di `.sldprt`
        /// contro `.SLDPRT`, applicata all'indice del lavoro invece che ai dati.
        ///
        /// ── L'ECCEZIONE, ED E' UNA CONVENZIONE VERA ──────────────────────
        ///
        /// Quando una voce viene rifatta, l'originale si tiene sotto col nome
        /// di prima e la dicitura «traccia originale, tenuta per riferimento»:
        /// li' il nome doppio e' voluto e dice qualcosa. Quindi il doppione si
        /// accetta **solo** se una delle due e' una traccia.
        ///
        /// Non si pretende che il TODO sia in un formato: si pretende che due
        /// cose diverse non si chiamino uguale.
        /// </summary>
        /// <summary>
        /// OGNI CARTELLA DI CASI STA NELL'ELENCO CHE IL BANCO SCORRE.
        ///
        /// ── IL DIFETTO CHE QUESTO CONTROLLO PRENDE ───────────────────────
        ///
        /// `Bench\DeclaredCases.cs` ha un elenco ESPLICITO di famiglie, e
        /// scorre solo quelle. Una cartella nuova sotto `Bench\cases\` che non
        /// finisca in quell'elenco contiene file che **sembrano prove e non
        /// girano mai**: nessun errore, nessun avviso, e il conto delle prove
        /// non se ne accorge perche' quei casi non sono mai esistiti per lui.
        ///
        /// E' successo il 23/9/2026 scrivendo la voce Z-C6: la cartella
        /// `saldature\` e' nata col suo primo caso dentro, e sarebbe rimasta
        /// muta. *Un elenco scritto a mano e' una cosa che un giorno qualcuno
        /// dimentica di allungare, e il modo in cui lo dimentica e' non
        /// accorgersene.*
        ///
        /// ── PERCHE' SI GUARDA LA FORMA DEI FILE, NON IL NOME ─────────────
        ///
        /// Sotto `cases\` ci sono due cose diverse: i CASI (oggetti con dei
        /// passi e delle attese, che il banco esegue) e i PIANI (array, che
        /// lancia `run_plans.js` a mano). Le cartelle di soli piani —
        /// `ponte\`, `limiti\`, `viste\`, `pezzi\`, `forma\` — stanno fuori
        /// dall'elenco ed e' giusto cosi'.
        ///
        /// Quindi il controllo non chiede «ogni cartella e' nell'elenco»:
        /// chiede «ogni cartella che contiene almeno un CASO e' nell'elenco».
        /// La differenza e' fra un controllo che descrive la realta' e uno che
        /// pretende un ordine che non c'e'.
        /// </summary>
        private static void EveryCasesFolderIsInTheList()
        {
            Group("le cartelle di casi stanno nell'elenco che il banco scorre");

            var source = Path.Combine(Root(), "Bench", "DeclaredCases.cs");
            var casesRoot = Path.Combine(Root(), "Bench", "cases");
            if (!File.Exists(source) || !Directory.Exists(casesRoot))
            {
                Check("il banco e i suoi casi ci sono", false,
                      source + " / " + casesRoot);
                return;
            }

            var text = File.ReadAllText(source);
            var block = System.Text.RegularExpressions.Regex.Match(text, @"Families\s*=\s*\{([\s\S]*?)\};");
            if (!block.Success)
            {
                // SE L'ELENCO SPARISCE, SPARISCE ANCHE QUESTO CONTROLLO: e
                // un controllo che sparisce in silenzio e' peggio di uno che
                // non c'e' mai stato.
                Check("trovo l'elenco delle famiglie", false,
                      "non c'e' nessun «Families = { ... }» in DeclaredCases.cs");
                return;
            }

            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(block.Groups[1].Value, "\"([a-z0-9_-]+)\""))
                listed.Add(m.Groups[1].Value);

            Check("l'elenco delle famiglie si legge", listed.Count >= 10,
                  listed.Count + " famiglie");
            if (listed.Count < 10) return;

            // `regression` NON sta nell'elenco, e non e' un difetto: ha un
            // giro suo (`--regressione`), con un valutatore suo. Sta scritto
            // qui e non in un elenco di eccezioni generico, perche' e' una
            // sola e ha un motivo.
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
                    // UN CASO E' UN OGGETTO, UN PIANO E' UN ARRAY. Stessa
                    // distinzione strutturale che fa il banco.
                    var t = raw.TrimStart();
                    if (t.StartsWith("{") && t.IndexOf("\"steps\"", StringComparison.Ordinal) >= 0)
                    { hasCases = true; break; }
                }
                if (!hasCases) continue;
                withCases++;
                if (!listed.Contains(itemName) && !outsideByDesign.Contains(itemName))
                    mute.Add(itemName);
            }

            Check("ogni cartella con dei casi dentro e' nell'elenco",
                  mute.Count == 0,
                  mute.Count == 0
                      ? withCases + " cartelle con casi su " + inspected + ", tutte scorse"
                      : "MUTE, i loro casi non girano mai: " + string.Join(", ", mute) +
                        " — si aggiungono a «Families» in Bench\\DeclaredCases.cs");
        }

        /// <summary>
        /// Dove sta il TODO, o `null` se in questo clone non c'e'.
        ///
        /// Stesso ordine di `Checks\where-is-the-todo.js`, e per lo stesso
        /// motivo: la variabile `TODO_MD` per chi lo tiene altrove,
        /// `note-interne\TODO.md` dove sta oggi, e la radice dov'era prima —
        /// cosi' un clone vecchio continua a funzionare senza accorgersene.
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
            Group("nel TODO due voci non si chiamano allo stesso modo");

            // IL TODO NON E' PIU' NEL REPOSITORY PUBBLICO: e' un diario di
            // lavoro e vive in `note-interne\`, che il .gitignore esclude.
            //
            // QUI PRIMA C'ERA `Check(..., false, ...)`, cioe' un ROSSO. Su
            // questa macchina non sarebbe mai scattato; su ogni clone sarebbe
            // stato rosso sempre, per un file che chi clona non ha e non deve
            // avere - una prova che fallisce per tutti tranne chi l'ha scritta
            // non misura il codice, misura chi lo esegue.
            //
            // Adesso la prova si SALTA dicendolo. Il conto dei controlli
            // scende di uno invece di salire di un rosso, che e' la differenza
            // fra «non l'ho controllato» e «l'ho controllato e va male».
            var todo = WhereTheTodoIs();
            if (todo == null)
            {
                Console.WriteLine("  --   il TODO non c'e' in questo clone: e' interno e non si pubblica.");
                Console.WriteLine("       QUESTO CONTROLLO NON E' STATO FATTO. Con TODO_MD si indica una copia.");
                return;
            }
            string[] lines;
            try { lines = File.ReadAllLines(todo); }
            catch (Exception ex) { Check("il TODO si legge", false, ex.Message); return; }

            var header = new System.Text.RegularExpressions.Regex(
                @"^#### \[(.)\] (\S+)\s*(.*)$");
            var trace = new System.Text.RegularExpressions.Regex(
                @"traccia originale|progetto originale|tenut[ao] per|per riferimento|com'era scritta",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // nome -> quante volte, e quante di quelle erano tracce
            var howMany = new Dictionary<string, int>(StringComparer.Ordinal);
            var traces = new Dictionary<string, int>(StringComparer.Ordinal);
            int totali = 0;

            foreach (var r in lines)
            {
                var m = header.Match(r);
                if (!m.Success) continue;
                totali++;
                var itemName = m.Groups[2].Value;
                howMany[itemName] = (howMany.ContainsKey(itemName) ? howMany[itemName] : 0) + 1;
                if (trace.IsMatch(m.Groups[3].Value))
                    traces[itemName] = (traces.ContainsKey(itemName) ? traces[itemName] : 0) + 1;
            }

            // UNA PROVA CHE NON TROVA NIENTE PASSA SEMPRE. Se un giorno le
            // intestazioni cambiano forma, questa riga diventa rossa invece di
            // lasciare che il controllo si spenga in silenzio.
            Check("ci sono voci da controllare", totali >= 100, totali + " intestazioni");

            var doubled = new List<string>();
            foreach (var v in howMany)
            {
                int t = traces.ContainsKey(v.Key) ? traces[v.Key] : 0;
                // una voce viva piu' le sue tracce: va bene. Due vive no.
                if (v.Value - t > 1)
                    doubled.Add(v.Key + " (×" + v.Value + ", di cui tracce: " + t + ")");
            }
            doubled.Sort(StringComparer.Ordinal);

            Check("due voci diverse non si chiamano allo stesso modo",
                  doubled.Count == 0,
                  doubled.Count == 0
                      ? totali + " intestazioni, nessun nome usato due volte per due cose"
                      : "STESSO NOME, VOCI DIVERSE: " + string.Join("; ", doubled));
        }

        /// <summary>
        /// IL RIFIUTO DI `model_dimensions` DICE ANCORA QUELLO CHE SERVE.
        ///
        /// ── PERCHE' ESISTE, E COSA NON E' ────────────────────────────────
        ///
        /// Misurato il 18/9/2026: su un pezzo costruito dal ponte, con **sette
        /// quote tutte marcate per il disegno**, cinque strade di import ne
        /// portavano **zero**. **Il 26/9/2026 l'import funziona fuori processo**
        /// (7 quote sullo stesso pezzo, due sonde) e la prova di comportamento
        /// c'e': `dimensions/dimensions_come_from_the_model`, col rifiuto senza viste e
        /// la gemella verde (Z-C1-bis, Z-E7 chiusa). Questo controllo resta per il
        /// giorno in cui l'import torna a portare zero quote: allora il messaggio
        /// qui sotto e' di nuovo tutto cio' che un modello riceve.
        ///
        /// In quel caso **quel messaggio e' tutto cio' che un modello riceve
        /// quando chiede le quote**, ed e' la parte che funziona: dice quante
        /// quote ha il pezzo, **quante sono marcate** — il dato che divide in
        /// due le cause possibili — e l'elenco delle strade provate. Se qualcuno
        /// lo accorcia «perche' era lungo», non se ne accorge nessuno.
        ///
        /// ── COSA GUARDA, E COSA NON PUO' GUARDARE ────────────────────────
        ///
        /// Guarda il **sorgente**, non l'esecuzione: qui SOLIDWORKS non c'e'.
        /// Quindi prova che le frasi ci sono ancora, **non** che il rifiuto
        /// scatta e nemmeno che dice il vero: quello lo prova il caso di banco
        /// della coppia (dal 26/9/2026).
        /// </summary>
        private static void TheDimensionsRefusalStillSaysWhatIsNeeded()
        {
            Group("il rifiuto delle quote dice ancora quello che serve");

            var file = Path.Combine(Root(), "Oce.Solid.Agent.Core", "DrawingOperations.cs");
            if (!File.Exists(file)) { Check("il sorgente c'e'", false, file); return; }
            string code;
            try { code = File.ReadAllText(file); }
            catch (Exception ex) { Check("il sorgente si legge", false, ex.Message); return; }

            // L'ANCORA. Se sparisce, questo controllo non ha piu' un soggetto e
            // deve diventare ROSSO: una prova che non trova cio' che cerca e
            // passa e' peggio di nessuna prova.
            const string anchor = "Non e' arrivata nessuna quota";
            if (code.IndexOf(anchor, StringComparison.Ordinal) < 0)
            {
                Check("trovo il rifiuto di model_dimensions", false,
                      "«" + anchor + "» non c'e' piu' in DrawingOperations.cs: " +
                      "se l'import e' stato riparato questa prova va RISCRITTA, non tolta");
                return;
            }
            Check("trovo il rifiuto di model_dimensions", true, "");

            // Ogni pezzo, e accanto PERCHE' quel pezzo: un elenco di stringhe
            // senza il motivo e' la prima cosa che qualcuno taglia.
            var parts = new[]
            {
                new[] { "marcate per il disegno",
                        "e' il dato che divide in due le cause: marcate e non importate " +
                        "(guasto nell'import) contro non marcate (guasto a monte)" },
                new[] { "[marcata]",
                        "la qualifica accanto a OGNI nome, non solo il totale" },
                new[] { "[NON marcata]",
                        "senza il contrario, «marcata» non e' un'informazione" },
                new[] { "Ho provato: ",
                        "l'elenco delle strade tentate: e' cio' che rende il " +
                        "fallimento un fatto invece di un mistero" },
            };

            var missingItems = new List<string>();
            foreach (var p in parts)
                if (code.IndexOf(p[0], StringComparison.Ordinal) < 0)
                    missingItems.Add("«" + p[0] + "» (" + p[1] + ")");

            Check("il rifiuto nomina ancora quante quote sono marcate e cosa ha provato",
                  missingItems.Count == 0,
                  missingItems.Count == 0
                      ? "tutti e " + parts.Length + " i pezzi ci sono"
                      : "TOLTO DAL MESSAGGIO: " + string.Join("; ", missingItems));
        }

        /// <summary>
        /// LA LIBRERIA CHE CARICA IL PONTE E' LA STESSA CHE MISURA IL BANCO.
        ///
        /// ── PERCHE' ESISTE, E QUANTO E' COSTATA ──────────────────────────
        ///
        /// Ce ne sono **due copie**, e vivono in due posti:
        ///
        ///   Oce.Solid.Agent.Core\bin\Release\Oce.Solid.Agent.Core.dll   <- il banco
        ///   bin\Oce.Solid.Agent.Core.dll                             <- il ponte
        ///
        /// La seconda la mette `build.ps1`. Chi compila **il solo progetto**
        /// della libreria — che e' la cosa piu' naturale del mondo mentre si
        /// itera — aggiorna la prima e **non** la seconda.
        ///
        /// Il 18/9/2026 sono divergute di quattro versioni per nove ore.
        /// Il banco misurava 0.98.4 e il ponte girava 0.98.1: una riparazione
        /// e' stata provata tre volte dal ponte e **non e' mai stata provata**,
        /// perche' in quel binario non c'era. Il sintomo era perfetto —
        /// l'operazione «non rifiutava» — e la causa non era nel codice.
        ///
        /// *Due copie della stessa cosa in due posti divergono, e il giorno in
        /// cui divergono nessuno lo vede: si vede il sintomo, mesi dopo,
        /// travestito da difetto del codice.*
        ///
        /// ── COSA GUARDA ─────────────────────────────────────────────────
        ///
        /// Le due versioni, e basta. Non i byte: un rebuild senza modifiche
        /// cambia i byte e non cambia niente di cio' che conta.
        /// Se una delle due manca lo dice e **non passa**: una prova che non
        /// trova cio' che cerca e passa e' peggio di nessuna prova.
        /// </summary>
        private static void TheBridgeLibraryIsTheSAMEAsTheBench()
        {
            Group("le TRE copie della libreria sono la stessa");

            var bench = Path.Combine(Root(), "Oce.Solid.Agent.Core", "bin", "Release",
                                        "Oce.Solid.Agent.Core.dll");
            var bridge = Path.Combine(Root(), "bin", "Oce.Solid.Agent.Core.dll");

            // ── LA TERZA, CHE QUESTO CONTROLLO NON GUARDAVA ──────────────
            //
            // `Bench\bin\Oce.Solid.Agent.Core.dll` e' quella che
            // `Oce.Solid.Agent.Bench.exe` carica DAVVERO, e per due giorni questo
            // gruppo si e' chiamato «il ponte e il banco caricano la stessa
            // libreria» confrontando due copie di cui **nessuna delle due
            // era quella del banco in esecuzione**. La prima e' la sorgente
            // da cui `bench.ps1` la copia compilando.
            //
            // Il 24/9/2026 e' costato un giro: lanciato `Oce.Solid.Agent.Bench.exe`
            // a mano per iterare in fretta, ha caricato la copia del giorno
            // prima e ha detto che due predefiniti appena implementati **non
            // esistevano**. Erano nel sorgente e nelle altre due DLL.
            //
            // Questo controllo non impedisce di lanciare l'eseguibile a mano
            // — niente lo impedisce — ma dice **subito** che le copie sono
            // divergute, invece di lasciarlo scoprire a un resoconto che
            // accusa il codice sbagliato.
            var nextToBench = Path.Combine(Root(), "Bench", "bin",
                                              "Oce.Solid.Agent.Core.dll");

            bool c1 = ProductFromBuild(bench), c2 = ProductFromBuild(bridge);

            // Senza interop la libreria non si poteva compilare: mancano tutte e
            // due perche' la macchina non ce la fa, non perche' qualcuno abbia
            // sbagliato. Vedi `NotHere`.
            if (!c1 && !c2 && !TheBridgeCouldBeBuilt())
            {
                NotHere("il ponte e il banco caricano la stessa libreria",
                       "qui SOLIDWORKS non c'e': Oce.Solid.Agent.Core.dll non si compila, " +
                       "e due file che non esistono non si possono confrontare.");
                return;
            }

            Check("la libreria che misura il banco c'e'", c1, bench);
            Check("la libreria che carica il ponte c'e'", c2,
                  c2 ? bridge : bridge + " — si produce con .\\build.ps1");
            if (!c1 || !c2) return;

            string v1 = Version(bench), v2 = Version(bridge);
            Check("le due versioni si leggono", v1 != null && v2 != null,
                  (v1 ?? "?") + " / " + (v2 ?? "?"));
            if (v1 == null || v2 == null) return;

            Check("la sorgente e il ponte hanno la STESSA versione della libreria",
                  string.Equals(v1, v2, StringComparison.Ordinal),
                  string.Equals(v1, v2, StringComparison.Ordinal)
                      ? "tutti e due " + v1
                      : "SORGENTE " + v1 + " contro PONTE " + v2 +
                        " — chi ha compilato il solo progetto ha aggiornato il primo e " +
                        "non il secondo: si rilancia .\\build.ps1, che li allinea");

            // ── E LA TERZA COPIA: SI GUARDA LA RADICE, NON IL SINTOMO ────
            //
            // `Bench\bin\Oce.Solid.Agent.Core.dll` e' un RESIDUO: `bench.ps1`
            // passa `--libreria` puntando alla sorgente, e nessuno aggiorna
            // quella copia. Pretendere che combaci sarebbe un rosso perenne
            // su un file che il giro vero non guarda mai.
            //
            // Cio' che conta e' il PREDEFINITO: cosa carica
            // `Oce.Solid.Agent.Bench.exe` se qualcuno lo lancia a mano senza
            // `--libreria`. Fino al 24/9/2026 era la copia accanto a se', e
            // mi ha fatto misurare per un giro il codice di sei versioni
            // prima dicendo che due predefiniti appena scritti «non
            // esistevano». Adesso e' la stessa che usa `bench.ps1`.
            //
            // Si legge dal SORGENTE, perche' e' li' che la scelta vive, e
            // perche' un controllo che leggesse il residuo tornerebbe a
            // sorvegliare il sintomo.
            var benchSource = Path.Combine(Root(), "Bench", "Program.cs");
            if (File.Exists(benchSource))
            {
                var text = File.ReadAllText(benchSource);
                bool findTheGoodOne =
                    text.IndexOf("DefaultLibrary", StringComparison.Ordinal) >= 0 &&
                    text.IndexOf("\"Oce.Solid.Agent.Core\", \"bin\", \"Release\"",
                                  StringComparison.Ordinal) >= 0;
                Check("senza «--libreria» il banco cerca PRIMA la libreria appena compilata",
                      findTheGoodOne,
                      findTheGoodOne
                          ? "il ripiego accanto all'eseguibile resta, ma viene dopo"
                          : "il predefinito punta accanto all'eseguibile: chi lancia " +
                            "`Oce.Solid.Agent.Bench.exe` a mano misura una copia che nessuno " +
                            "aggiorna");

                // E se il residuo c'e', lo si DICE — senza accusare: sapere
                // che e' vecchio spiega un log confuso di sei mesi fa.
                var leftoverCheck = "e si sa se accanto all'eseguibile c'e' un residuo vecchio";
                if (ProductFromBuild(nextToBench))
                {
                    string v3 = Version(nextToBench);
                    Check(leftoverCheck, true,
                          string.Equals(v1, v3, StringComparison.Ordinal)
                              ? "ce n'e' una, ed e' allineata (" + v3 + ")"
                              : "ce n'e' una FERMA A " + v3 + " contro " + v1 +
                                ": non la usa piu' nessuno, ma se un log vecchio " +
                                "non torna, e' lei");
                }
                // Said when there is none as well: a check that only exists
                // on the machines with a leftover makes the count of a full
                // run depend on the machine, and the count check on the readme
                // of the contracts then accuses a right document (1/10/2026:
                // 765 on the main checkout, 764 in a fresh worktree).
                else
                    Check(leftoverCheck, true, "there is none: nothing stale next to the executable");
            }

            // ── E IL TERZO NUMERO, QUELLO CHE NESSUNO GUARDA ─────────────
            //
            // `AssemblyInformationalVersion` e' la versione che Windows mostra
            // nelle proprieta' del file. Nessun codice la legge, e per questo
            // invecchia da sola: il 18/9/2026 diceva 0.61.0 con l'assembly a
            // 0.66.0, e' stata corretta, e il 22/9/2026 diceva **0.94.0 con
            // l'assembly a 0.99.0** — cinque versioni indietro, di nuovo.
            //
            // Sopra quella riga c'era gia' un commento che raccontava il
            // difetto per esteso. Non e' servito, e non poteva servire: **un
            // commento non e' un controllo**. Chi allinea il numero a mano lo
            // riallinea finche' se ne ricorda, e poi smette.
            //
            // Si guarda il binario e non il sorgente: quello che finisce nel
            // pacchetto e' il binario.
            string notice = VersionInformation(bench);
            Check("la versione mostrata da Windows e' quella vera",
                  notice != null &&
                  v1.StartsWith(notice, StringComparison.Ordinal),
                  notice == null
                      ? "non si legge"
                      : "assembly " + v1 + ", proprieta' del file " + notice +
                        (v1.StartsWith(notice, StringComparison.Ordinal)
                            ? ""
                            : " — si allinea AssemblyInformationalVersion in " +
                              "Oce.Solid.Agent.Core\\Properties\\AssemblyInfo.cs"));
        }

        /// <summary>
        /// `AssemblyInformationalVersion` del file, cioe' quella che si legge
        /// in Proprieta' → Dettagli → Versione prodotto. `null` se non c'e'.
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
        /// La versione di un assembly, letta senza caricarlo nel processo:
        /// caricarlo vorrebbe dire tenerselo, e questa suite gira anche dove
        /// l'interop di Dassault non c'e'.
        /// </summary>
        private static string Version(string dll)
        {
            try { return System.Reflection.AssemblyName.GetAssemblyName(dll).Version.ToString(); }
            catch { return null; }
        }

        /// <summary>
        /// `prove.xml` in formato JUnit: e' quello che ogni CI sa leggere senza
        /// che nessuno le insegni niente.
        /// </summary>
        private static void WriteJUnit()
        {
            try
            {
                var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prove.xml");
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
                        // UN SALTATO NON E' UN PASSATO, nemmeno nell'XML: un
                        // `<testcase />` nudo e' un verde per chiunque legga il
                        // file, e il file lo legge una CI che poi mostra un segno
                        // di spunta. `<skipped>` e' la voce che JUnit ha proprio
                        // per questo, e ogni interfaccia la sa disegnare diversa.
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
                Console.WriteLine("prove.xml: " + file);
            }
            catch (Exception ex)
            {
                Console.WriteLine("non ho potuto scrivere prove.xml: " + ex.Message);
            }
        }

        private static string Xml(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;")
                            .Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
