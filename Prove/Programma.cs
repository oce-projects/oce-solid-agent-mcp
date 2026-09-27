using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Prove.Contratti
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
    ///     dentro le descrizioni, e `Simili()` che su «asole» trova «asola»;
    ///   * `Sessione.StessoDocumento()` — la guardia contro il blocco estruso
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
    internal static class Programma
    {
        internal static int Falliti;

        /// <summary>
        /// Quanti controlli NON hanno potuto girare su questa macchina.
        /// Non sono verdi e non sono rossi: vedi <see cref="NonQui"/>.
        /// </summary>
        internal static int NonEseguibiliQui;

        internal static readonly List<Esito> Esiti = new List<Esito>();

        internal sealed class Esito
        {
            public string Gruppo;
            public string Nome;
            public bool Ok;
            public bool NonQui;
            public string Dettaglio;
        }

        private static string _gruppo = "generale";

        internal static void Gruppo(string nome)
        {
            _gruppo = nome;
            Console.WriteLine();
            Console.WriteLine("=== " + nome + " ===");
        }

        internal static void Segna(string nome, bool ok, string dettaglio = "")
        {
            if (!ok) Falliti++;
            Esiti.Add(new Esito { Gruppo = _gruppo, Nome = nome, Ok = ok, Dettaglio = dettaglio });
            // UN VERDE SI ACCORCIA, UN ROSSO NO. Il dettaglio di una prova
            // rossa e' l'unica cosa che si legge davvero, e troncarlo a
            // centodieci caratteri vuol dire rilanciare per vedere il resto.
            Console.WriteLine((ok ? "  OK   " : "  NO   ") + nome.PadRight(58) +
                              (ok ? Breve(dettaglio) : dettaglio));
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
        internal static void NonQui(string nome, string perche)
        {
            NonEseguibiliQui++;
            Esiti.Add(new Esito { Gruppo = _gruppo, Nome = nome, Ok = true,
                                  NonQui = true, Dettaglio = perche });
            Console.WriteLine("  --   " + nome.PadRight(58) + perche);
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
        internal static bool FingiSenzaInterop
        {
            get { return Environment.GetEnvironmentVariable("PROVE_SENZA_INTEROP") == "1"; }
        }

        /// <summary>
        /// Un file che lo produce `build.ps1`, e quindi che su una macchina
        /// senza interop non puo' esistere. Sotto simulazione non esiste mai.
        /// </summary>
        internal static bool ProdottoDaBuild(string percorso)
        {
            return !FingiSenzaInterop && File.Exists(percorso);
        }

        internal static string DoveStaLInterop()
        {
            if (FingiSenzaInterop) return null;
            var candidati = new List<string>();
            var radiceSw = Environment.GetEnvironmentVariable("SW_INSTALL_PATH");
            if (!string.IsNullOrEmpty(radiceSw))
                candidati.Add(Path.Combine(radiceSw, "api", "redist"));
            var redist = Environment.GetEnvironmentVariable("SW_REDIST");
            if (!string.IsNullOrEmpty(redist)) candidati.Add(redist);
            candidati.Add(@"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist");
            foreach (var c in candidati)
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
        internal static bool IlPonteSiPotevaCompilare()
        {
            return DoveStaLInterop() != null;
        }

        /// <summary>
        /// L'eseguibile del ponte, oppure una voce «non qui» e `null`.
        ///
        /// Tre gruppi facevano la stessa cosa in tre modi leggermente diversi
        /// (`ProveProtocollo`, `ProveSessione`, `ProveCatalogo`): la regola su
        /// quando un ponte mancante e' colpa della macchina stava scritta tre
        /// volte, ed e' il modo in cui due delle tre restano indietro.
        /// </summary>
        internal static string PonteOppureNonQui(string cosaNonGira)
        {
            var ponte = Path.Combine(Radice(), "bin", "oce-solid-agent-mcp.exe");
            if (ProdottoDaBuild(ponte)) return ponte;
            if (!IlPonteSiPotevaCompilare())
            {
                NonQui(cosaNonGira,
                       "qui SOLIDWORKS non c'e': senza api\\redist\\SolidWorks.Interop.* il " +
                       "ponte non si compila, quindi non esiste nessun eseguibile a cui " +
                       "parlare. Non e' un difetto del ponte, e fingere di averlo provato " +
                       "sarebbe peggio del non averlo provato.");
                return null;
            }
            Segna("il ponte c'e'", false,
                  "non trovo " + ponte + " — ma SOLIDWORKS su questa macchina c'e' (" +
                  DoveStaLInterop() + "), quindi manca solo .\\build.ps1");
            return null;
        }

        /// <summary>
        /// Il catalogo che il ponte serve: `bin\catalogo.json`, e se non c'e' la
        /// copia versionata `Strumenti\catalogo.json` — che e' esattamente il
        /// file che `build.ps1` rigenera e copia in `bin\`.
        ///
        /// QUATTRO CONTROLLI TENEVANO ROSSA LA CI DA CINQUE PUSH (misurato il
        /// 25/9/2026, dalla 0.85.0 alla 0.88.0): cercavano solo `bin\`, che in
        /// un clone pulito non esiste, e dicevano «il catalogo servito non
        /// c'e'». Non era un difetto del catalogo, e nemmeno una cosa che la CI
        /// non potesse fare: il file da leggere c'era, in `Strumenti\`.
        /// `ProveLeDescrizioni` lo faceva gia' giusto. *Una CI sempre rossa si
        /// impara a non guardarla, ed e' il modo in cui un rosso vero passa.*
        /// </summary>
        internal static string CatalogoServito()
        {
            var inBin = Path.Combine(Radice(), "bin", "catalogo.json");
            if (File.Exists(inBin)) return inBin;
            return Path.Combine(Radice(), "Strumenti", "catalogo.json");
        }

        internal static string Breve(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 110 ? s.Substring(0, 110) + "..." : s;
        }

        /// <summary>La radice del repository, due livelli sopra `Prove\bin\`.</summary>
        internal static string Radice()
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
        internal static string VersioneDelRepository()
        {
            try
            {
                var f = Path.Combine(Radice(), "VERSION.txt");
                if (!File.Exists(f)) return null;
                var s = File.ReadAllText(f).Trim();
                return s.Length == 0 ? null : s;
            }
            catch { return null; }
        }

        /// <summary>
        /// `Prove/LEGGIMI.md` dice quanti controlli ci sono. Deve essere vero.
        ///
        /// Il 16/9/2026 diceva **54** mentre ne giravano **99**: un numero
        /// giusto quando fu scritto, invecchiato da solo mentre la suite
        /// cresceva. Chi legge quel file non ha modo di accorgersene — e' la
        /// sua unica fonte. E' lo stesso difetto del README che prometteva
        /// trentotto operazioni su sessanta, e la difesa e' la stessa: far
        /// leggere il documento a un controllo.
        ///
        /// SI CONTA ANCHE QUESTO CONTROLLO. Gira per ultimo, quindi quando
        /// guarda `Esiti.Count` se stesso non c'e' ancora: il numero scritto
        /// nel documento deve valere `Esiti.Count + 1`. Non e' una furbizia,
        /// e' l'unico conto che torna — e il documento deve dire il totale che
        /// l'utente legge a schermo, non quello meno uno.
        /// </summary>
        private static void IlLeggimiDiceQuantiSono()
        {
            Gruppo("il LEGGIMI delle prove");

            // IL CONTO VALE PER UN GIRO INTERO (voce F9-bis). Dove SOLIDWORKS
            // non c'e', un gruppo da quaranta controlli diventa UNA riga «non
            // eseguibile qui»: il totale crolla, il LEGGIMI ha ancora ragione, e
            // questo controllo diventerebbe rosso accusando il documento giusto.
            // Un controllo che sa di non poter rispondere lo dice.
            if (NonEseguibiliQui > 0)
            {
                NonQui("il LEGGIMI dice quanti controlli ci sono, e sono quelli veri",
                       "su questa macchina " + NonEseguibiliQui + " controlli non hanno " +
                       "potuto girare: il numero scritto nel LEGGIMI e' quello di un giro " +
                       "intero e qui non e' confrontabile.");
                return;
            }

            int veri = Esiti.Count + 1;

            var leggimi = Path.Combine(Radice(), "Prove", "LEGGIMI.md");
            if (!File.Exists(leggimi))
            {
                Segna("il LEGGIMI c'e'", false, leggimi);
                return;
            }

            string testo;
            try { testo = File.ReadAllText(leggimi); }
            catch (Exception ex) { Segna("il LEGGIMI si legge", false, ex.Message); return; }

            // Si cerca SOLO la forma «<numero> controlli»: il file parla anche
            // di secondi e di licenze, e una regola larga griderebbe su una
            // frase giusta.
            var trovati = System.Text.RegularExpressions.Regex.Matches(
                testo, @"(\d+)\s+controlli");
            if (trovati.Count == 0)
            {
                Segna("il LEGGIMI dice quanti controlli ci sono", false,
                      "non trovo nessun «N controlli»: se il numero sparisce, " +
                      "sparisce anche questo controllo");
                return;
            }

            var sbagliati = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in trovati)
                if (Convert.ToInt32(m.Groups[1].Value) != veri)
                    sbagliati.Add("«" + m.Value + "»");

            Segna("il LEGGIMI dice quanti controlli ci sono, e sono quelli veri",
                  sbagliati.Count == 0,
                  sbagliati.Count == 0
                      ? veri + " controlli"
                      : "ne girano " + veri + ", il LEGGIMI dice " +
                        string.Join(" e ", sbagliati));
        }

        private static int Main(string[] argomenti)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = Encoding.UTF8;

            Console.WriteLine("Prove.Contratti — senza SOLIDWORKS, su questa macchina qualunque");
            Console.WriteLine("radice: " + Radice());

            if (FingiSenzaInterop)
            {
                Console.WriteLine();
                Console.WriteLine("### PROVE_SENZA_INTEROP=1 — GIRO SIMULATO ###");
                Console.WriteLine("Questa macchina fa finta di non avere SOLIDWORKS, per far");
                Console.WriteLine("vedere come si comporta la suite su un runner nudo. I");
                Console.WriteLine("risultati di questo giro NON valgono come collaudo.");
            }

            NessunInterop();
            OgniSorgenteStaNelSuoProgetto();
            OgniVariabileDAmbienteStaNelLeggimi();
            IlLeggimiDelBancoContaIFileGiusti();
            OgniFamigliaDelCatalogoHaUnDocumentoAmmesso();
            OgniCartellaDiCasiEDentroLElenco();
            NessunaVoceDelTodoSiChiamaComeUnAltra();
            IlRifiutoDelleQuoteDiceAncoraQuelloCheServe();
            LaLibreriaDelPonteELaSTESSADelBanco();
            ProveAttese.Tutte();
            ProveCatalogo.Tutte();
            ProveSessione.Tutte();
            ProveProtocollo.Tutte();
            ProveIlBanco.Tutte();
            ProveLaSentinella.Tutte();
            ProveIlDocumento.Tutte();
            ProveEnumDiErrore.Tutte();
            ProveILimiti.Tutte();
            ProveLeDescrizioni.Tutte();
            ProveIRifiuti.Tutte();
            ProveIlDocumentoRichiesto.Tutte();
            ProveICampiObbligatori.Tutte();
            ProveGliEsempi.Tutte();
            ProveLeImmagini.Tutte();
            ProveIlGuinzaglio.Tutte();
            ProveLaProvaAVuoto.Tutte();
            ProveLaConferma.Tutte();
            ProveLeFirmeVietate.Tutte();
            ProveLeFirmeDelDocumento.Tutte();
            ProveIlRegistro.Tutte();
            ProveLAvanzamento.Tutte();
            ApiCatalogContracts.Run();
            IlLeggimiDiceQuantiSono();

            Console.WriteLine();
            Console.WriteLine(new string('=', 76));
            int fatti = Esiti.Count - NonEseguibiliQui;
            var riepilogo = Esiti.Count + " controlli, " + Falliti + " falliti";
            if (NonEseguibiliQui > 0)
                riepilogo += ", " + NonEseguibiliQui + " non eseguibili qui (" +
                             fatti + " fatti davvero)";
            Console.WriteLine(riepilogo);

            // IL NUMERO DA SOLO SI LEGGEREBBE COME UN VERDE. Chi guarda una CI
            // legge «0 falliti» e va avanti: se meta' dei controlli non ha
            // girato, quel numero e' vero e ingannevole insieme, ed e'
            // esattamente il difetto che questo repository rimprovera altrove.
            if (NonEseguibiliQui > 0)
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
            // `Esiti.Count` la soglia l'avrebbero superata anche venticinque
            // righe «non eseguibile qui»: un minimo che si lascia soddisfare
            // da cio' che non ha girato non e' un minimo.
            const int MinimoDichiarato = 25;
            if (fatti < MinimoDichiarato)
            {
                Console.WriteLine("TROPPO POCHI: ne erano dichiarati almeno " + MinimoDichiarato +
                                  " davvero eseguiti, e ne risultano " + fatti +
                                  ". Una suite che si svuota resta verde, ed e' il modo piu' " +
                                  "silenzioso di smettere di provare.");
                Falliti++;
            }

            ScriviJUnit();
            return Falliti == 0 ? 0 : 1;
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
        private static void NessunInterop()
        {
            Gruppo("questo progetto gira dove SOLIDWORKS non c'e'");
            var mio = typeof(Programma).Assembly;
            var dassault = mio.GetReferencedAssemblies()
                .Where(a => a.Name.StartsWith("SolidWorks", StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Name)
                .ToList();
            Segna("nessun riferimento a SolidWorks.Interop",
                  dassault.Count == 0,
                  dassault.Count == 0
                      ? mio.GetReferencedAssemblies().Length + " riferimenti, nessuno di Dassault"
                      : "trovati: " + string.Join(", ", dassault));
        }

        /// <summary>
        /// OGNI SORGENTE STA NEL SUO `.csproj`.
        ///
        /// ── IL DIFETTO CHE QUESTO CONTROLLO PRENDE ───────────────────────
        ///
        /// Un file `.cs` che non compare fra i `<Compile Include>` **non viene
        /// compilato**. Se qualcuno lo referenzia, MSBuild lo dice subito ed e'
        /// successo davvero il 16/9/2026 con `OperazioniAssieme.cs`: due errori,
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
        private static void OgniSorgenteStaNelSuoProgetto()
        {
            Gruppo("i sorgenti stanno nei progetti");

            // cartella (relativa alla radice) -> csproj che la deve elencare.
            // I `Controlli\<attrezzo>\` hanno un csproj per attrezzo e stanno
            // fuori dalla soluzione apposta: non si controllano qui.
            var mappa = new[]
            {
                new { Dove = new[] { @"Riscontro.Disegno", @"Riscontro.Disegno\Catalogo",
                                     @"Riscontro.Disegno\Properties", @"Riscontro.Disegno\Api" },
                      Progetto = @"Riscontro.Disegno\Riscontro.Disegno.csproj" },
                new { Dove = new[] { @"Ponte", @"Strumenti", @"Mcp" },
                      Progetto = @"Oce.Solid.Agent.csproj" },
                new { Dove = new[] { @"Banco" }, Progetto = @"Banco\ProvaSaldatura.csproj" },
                new { Dove = new[] { @"Prove" }, Progetto = @"Prove\Prove.Contratti.csproj" },

                // `Condiviso\` LA COMPILANO IN DUE, e qui basta nominarne uno.
                //
                // Il controllo e' a senso unico: «ogni sorgente sotto questa
                // cartella compare in quel progetto». Che il banco compili gli
                // stessi file non lo rende falso, e chiedere che una cartella
                // stia in un progetto SOLO renderebbe rosso proprio il modo in
                // cui `Attese.cs` e' condiviso da giorni.
                //
                // Senza questa riga `Condiviso\` sarebbe l'unica cartella di
                // sorgenti che nessuno guarda: un file messo li' e dimenticato
                // fuori dai csproj non verrebbe compilato e non lo direbbe
                // nessuno — il difetto per cui questo controllo esiste.
                new { Dove = new[] { @"Condiviso" },
                      Progetto = @"Prove\Prove.Contratti.csproj" },
                new { Dove = new[] { @"Installer" }, Progetto = @"Installer\Installa.csproj" },
            };

            int guardati = 0;
            var fuori = new List<string>();

            foreach (var m in mappa)
            {
                var csproj = Path.Combine(Radice(), m.Progetto);
                if (!File.Exists(csproj))
                {
                    Segna("il progetto «" + m.Progetto + "» c'e'", false, csproj);
                    continue;
                }
                string testo;
                try { testo = File.ReadAllText(csproj); }
                catch (Exception ex) { Segna("«" + m.Progetto + "» si legge", false, ex.Message); continue; }

                foreach (var cartella in m.Dove)
                {
                    var piena = Path.Combine(Radice(), cartella);
                    if (!Directory.Exists(piena)) continue;
                    // SOLO il primo livello: `obj\` e `bin\` sono pieni di
                    // sorgenti generati che nessun csproj elenca, e che non
                    // devono entrare nel conto.
                    foreach (var f in Directory.GetFiles(piena, "*.cs", SearchOption.TopDirectoryOnly))
                    {
                        var nome = Path.GetFileName(f);
                        // Generati dal compilatore, mai elencati a mano.
                        if (nome.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                            nome.IndexOf("AssemblyAttributes", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        guardati++;
                        // Si cerca il NOME fra virgolette: copre sia
                        // `Include="Pippo.cs"` sia `Include="Ponte\Pippo.cs"`.
                        if (testo.IndexOf(nome + "\"", StringComparison.OrdinalIgnoreCase) < 0)
                            fuori.Add(cartella + "\\" + nome + " → " + m.Progetto);
                    }
                }
            }

            Segna("ci sono sorgenti da controllare", guardati > 10, guardati + " file .cs");
            Segna("ogni sorgente e' elencato nel suo .csproj",
                  fuori.Count == 0,
                  fuori.Count == 0
                      ? "tutti e " + guardati
                      : "FUORI DAL PROGETTO (non vengono compilati): " +
                        string.Join("; ", fuori));
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
        /// Solo i sorgenti del PONTE e della LIBRERIA: `Banco\` e `Prove\`
        /// leggono variabili che servono a chi sviluppa, non a chi installa, e
        /// pretenderle nel README vorrebbe dire riempirlo di roba che a chi
        /// legge non serve.
        ///
        /// Il nome si cerca **in tutto il README**, non solo nella tabella: una
        /// variabile spiegata per esteso in un paragrafo e' documentata. Il
        /// controllo non pretende una forma, pretende che il nome ci sia.
        /// </summary>
        private static void OgniVariabileDAmbienteStaNelLeggimi()
        {
            Gruppo("le variabili d'ambiente stanno nel README");

            var readme = Path.Combine(Radice(), "README.md");
            if (!File.Exists(readme)) { Segna("il README c'e'", false, readme); return; }
            string testo;
            try { testo = File.ReadAllText(readme); }
            catch (Exception ex) { Segna("il README si legge", false, ex.Message); return; }

            // `GetEnvironmentVariable("X")` e la scorciatoia `Variabile("X")`
            // di `Ponte\Percorsi.cs`. La seconda e' proprio quella che nascondeva
            // `RISCONTRO_RADICE` e `RISCONTRO_SOLA_LETTURA` a un grep fatto sul
            // nome lungo: **cercare una sola forma vuol dire trovarne una sola.**
            var forma = new System.Text.RegularExpressions.Regex(
                @"(?:GetEnvironmentVariable|Variabile)\(""([A-Z][A-Z_0-9]+)""\)");

            var trovate = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var cartella in new[] { "Ponte", "Strumenti", "Mcp", "Riscontro.Disegno" })
            {
                var piena = Path.Combine(Radice(), cartella);
                if (!Directory.Exists(piena)) continue;
                foreach (var f in Directory.GetFiles(piena, "*.cs", SearchOption.AllDirectories))
                {
                    if (f.IndexOf(@"\obj\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        f.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string sorgente;
                    try { sorgente = File.ReadAllText(f); } catch { continue; }
                    foreach (System.Text.RegularExpressions.Match m in forma.Matches(sorgente))
                        trovate.Add(m.Groups[1].Value);
                }
            }

            // UNA PROVA CHE NON TROVA NIENTE PASSA SEMPRE, e non prova niente.
            // Se un giorno le cartelle cambiano nome, questa riga diventa rossa
            // invece di lasciare che il controllo si spenga in silenzio.
            Segna("ci sono variabili da controllare", trovate.Count >= 6,
                  trovate.Count + ": " + string.Join(", ", trovate));

            var assenti = trovate.Where(v => testo.IndexOf(v, StringComparison.Ordinal) < 0).ToList();
            Segna("ogni variabile letta dal ponte e' nominata nel README",
                  assenti.Count == 0,
                  assenti.Count == 0
                      ? "tutte e " + trovate.Count
                      : "NEL CODICE E NON NEL README: " + string.Join(", ", assenti));
        }

        /// <summary>
        /// I NUMERI IN `Banco\LEGGIMI.md` SONO QUANTI SONO I FILE DAVVERO.
        ///
        /// ── PERCHE' ESISTE ───────────────────────────────────────────────
        ///
        /// Il LEGGIMI del banco ha una tabella «cartella / quanti casi / cosa
        /// provano». Guardata il 17/9/2026: **quattro numeri su venti erano
        /// sbagliati** — `lavorazioni` diceva 2 e ne aveva 7, `ripeti` 3 e ne
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
        /// n'e' una: `casi/tavola/` contiene sia i piani a mano sia i casi
        /// dichiarati, ed e' elencata in tutt'e due i blocchi con due numeri
        /// diversi. Si sommano le righe della stessa cartella e si confronta la
        /// somma: 7 + 1 = 8 file. Pretendere che ogni riga da sola combaci
        /// renderebbe rossa una tabella giusta.
        /// </summary>
        private static void IlLeggimiDelBancoContaIFileGiusti()
        {
            Gruppo("il LEGGIMI del banco conta i file giusti");

            var leggimi = Path.Combine(Radice(), "Banco", "LEGGIMI.md");
            if (!File.Exists(leggimi)) { Segna("il LEGGIMI del banco c'e'", false, leggimi); return; }
            string testo;
            try { testo = File.ReadAllText(leggimi); }
            catch (Exception ex) { Segna("il LEGGIMI del banco si legge", false, ex.Message); return; }

            // `casi/<nome>/` seguito da spazi e da un numero. Le righe di
            // continuazione (la descrizione che va a capo) non hanno il numero
            // e non corrispondono: e' voluto.
            //
            // A INIZIO RIGA, e il perche' e' misurato (25/9/2026): senza `^` il
            // conto prendeva anche la frase che RACCONTA la vecchia riga
            // («Questa riga prima la contava ... «casi/forma/ 5»»), e
            // `casi/forma/` non e' nel repository per scelta. In locale passava,
            // perche' i cinque file ci sono; in un clone pulito — la CI — era
            // «nominate e non esistenti: forma», rosso a ogni push dalla 0.85.0.
            var forma = new System.Text.RegularExpressions.Regex(
                @"^casi/([a-z]+)/\s+(\d+)", System.Text.RegularExpressions.RegexOptions.Multiline);

            var dichiarati = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in forma.Matches(testo))
            {
                var nome = m.Groups[1].Value;
                int n = Convert.ToInt32(m.Groups[2].Value);
                dichiarati[nome] = (dichiarati.ContainsKey(nome) ? dichiarati[nome] : 0) + n;
            }

            // UNA PROVA CHE NON TROVA NIENTE PASSA SEMPRE. Se la tabella cambia
            // forma, questa riga diventa rossa invece di spegnersi in silenzio.
            Segna("ci sono cartelle da controllare", dichiarati.Count >= 15,
                  dichiarati.Count + " righe «casi/<nome>/ N»");

            var sbagliate = new List<string>();
            var assenti = new List<string>();
            foreach (var v in dichiarati)
            {
                var cartella = Path.Combine(Radice(), "Banco", "casi", v.Key);
                if (!Directory.Exists(cartella)) { assenti.Add(v.Key); continue; }
                int veri = Directory.GetFiles(cartella, "*.json", SearchOption.TopDirectoryOnly).Length;
                if (veri != v.Value)
                    sbagliate.Add(v.Key + ": dice " + v.Value + ", ne ha " + veri);
            }

            Segna("ogni cartella nominata nel LEGGIMI esiste",
                  assenti.Count == 0,
                  assenti.Count == 0 ? dichiarati.Count + " cartelle"
                                     : "nominate e non esistenti: " + string.Join(", ", assenti));
            Segna("i numeri del LEGGIMI sono quanti sono i file",
                  sbagliate.Count == 0,
                  sbagliate.Count == 0
                      ? "tutte e " + dichiarati.Count
                      : string.Join("; ", sbagliate));
        }

        /// <summary>
        /// OGNI FAMIGLIA DEL CATALOGO HA UN TIPO DI DOCUMENTO SU CUI PUO' GIRARE.
        ///
        /// ── PERCHE' ESISTE QUESTA PROVA ──────────────────────────────────
        ///
        /// Trovato il 18/9/2026 leggendo `EsecutorePiano.cs` per un'altra
        /// ragione: `ControlliPreliminari()` — che gira **prima del primo
        /// passo di ogni piano**, e per tutti e quattro gli strumenti del ponte
        /// — ammette solo `swDocPART` e, se `AncheSuTavole`, `swDocDRAWING`.
        /// Gli assiemi no. Intanto il catalogo servito ai client elencava tre
        /// operazioni di famiglia `assiemi`.
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
        /// leggono da `ControlliPreliminari` con una regex sui token
        /// `swDoc...`, cosi' il giorno che qualcuno ne aggiunge o ne toglie uno
        /// questa prova se ne accorge invece di restare d'accordo con se stessa.
        ///
        /// ── COSA **NON** PRENDE, e va detto ──────────────────────────────
        ///
        /// Guarda le FAMIGLIE, non le singole operazioni. `verifica_interferenze`
        /// sta in famiglia `verifiche` e vuole un assieme: questa prova la
        /// classifica come «parte» e la lascia passare. Il rimedio vero e' un
        /// campo `DocumentoRichiesto` su `OperazioneDisegno` — voce a se',
        /// perche' tocca la libreria. Finche' non c'e', **questa prova copre la
        /// famiglia e non il caso singolo**, ed e' scritto qui perche' chi la
        /// vede verde non creda che copra di piu'.
        /// </summary>
        private static void OgniFamigliaDelCatalogoHaUnDocumentoAmmesso()
        {
            Gruppo("il catalogo promette solo documenti che l'esecutore ammette");

            // ── 1. i tipi che l'esecutore ammette, letti dal suo sorgente ──
            var sorgente = Path.Combine(Radice(), "Riscontro.Disegno", "EsecutorePiano.cs");
            if (!File.Exists(sorgente))
            {
                Segna("il sorgente dell'esecutore c'e'", false, sorgente);
                return;
            }
            string codice;
            try { codice = File.ReadAllText(sorgente); }
            catch (Exception ex) { Segna("il sorgente si legge", false, ex.Message); return; }

            int da = codice.IndexOf("bool ammesso", StringComparison.Ordinal);
            if (da < 0)
            {
                // Se la riga cambia forma questa prova NON deve passare in
                // silenzio: una prova che non trova cio' che cerca e passa e'
                // peggio di nessuna prova.
                Segna("trovo la riga che decide i tipi ammessi", false,
                      "«bool ammesso» non c'e' piu' in EsecutorePiano.cs: " +
                      "la prova va riscritta, non tolta");
                return;
            }
            int a = codice.IndexOf(';', da);
            var espressione = a > da ? codice.Substring(da, a - da) : codice.Substring(da);

            var ammessi = new SortedSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(espressione, @"swDoc[A-Z]+"))
                ammessi.Add(m.Value);

            Segna("l'esecutore ammette almeno un tipo di documento", ammessi.Count >= 1,
                  string.Join(", ", ammessi));

            // ── 2. le famiglie che il catalogo SERVE ai client ─────────────
            var json = Path.Combine(Radice(), "bin", "catalogo.json");
            // Come nel gruppo «il catalogo» (voce F9-bis): dove il ponte non si
            // poteva compilare, `bin\\` e' vuota e il file buono e' quello in
            // repository. Questo controllo legge un JSON e un sorgente C#: non ha
            // niente da chiedere a SOLIDWORKS, e fermarsi qui vorrebbe dire non
            // fare una verifica che si poteva fare.
            if (!ProdottoDaBuild(json) && !IlPonteSiPotevaCompilare())
            {
                var inRepository = Path.Combine(Radice(), "Strumenti", "catalogo.json");
                if (File.Exists(inRepository)) json = inRepository;
            }
            if (!File.Exists(json))
            {
                Segna("il catalogo c'e'", false, "non trovo " + json + " — si produce con .\\build.ps1");
                return;
            }
            Dictionary<string, object> radice;
            try
            {
                var s = new System.Web.Script.Serialization.JavaScriptSerializer();
                s.MaxJsonLength = 40 * 1024 * 1024;
                radice = s.DeserializeObject(File.ReadAllText(json)) as Dictionary<string, object>;
            }
            catch (Exception ex) { Segna("il catalogo si legge", false, ex.Message); return; }

            var elenco = (radice != null && radice.ContainsKey("operazioni")
                ? radice["operazioni"] : null) as object[];
            if (elenco == null) { Segna("il catalogo ha «operazioni»", false, ""); return; }

            var famiglie = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var o in elenco)
            {
                var v = o as Dictionary<string, object>;
                if (v != null && v.ContainsKey("famiglia") && v["famiglia"] != null)
                    famiglie.Add(Convert.ToString(v["famiglia"]));
            }
            Segna("ci sono famiglie da controllare", famiglie.Count >= 5,
                  famiglie.Count + ": " + string.Join(", ", famiglie));

            // ── 3. su che tipo di documento ha senso ognuna ────────────────
            // Tre righe scritte a mano, e il loro compito e' proprio di NON
            // sapere tutto: una famiglia che non conoscono e' ROSSA, non verde.
            // Cosi' l'elenco non puo' invecchiare in silenzio — chi ne aggiunge
            // una deve dire su cosa gira.
            var serve = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "assiemi",      "swDocASSEMBLY" },
                { "tavola",       "swDocDRAWING"  },
                { "estrusioni",   "swDocPART"     },
                { "lavorazioni",  "swDocPART"     },
                { "saldature",    "swDocPART"     },
                { "riferimenti",  "swDocPART"     },
                { "proprieta",    "swDocPART"     },
                { "verifiche",    "swDocPART"     },
                { "esportazioni", "swDocPART"     },
                { "identita",     "swDocPART"     },
                { "documento",    "swDocPART"     },
                { "annullare",    "swDocPART"     },
                { "lamiera",      "swDocPART"     },
                { "quote",        "swDocPART"     },
            };

            var sconosciute = famiglie.Where(f => !serve.ContainsKey(f)).ToList();
            Segna("ogni famiglia del catalogo e' classificata qui dentro",
                  sconosciute.Count == 0,
                  sconosciute.Count == 0
                      ? "tutte e " + famiglie.Count
                      : "FAMIGLIA NUOVA E NON CLASSIFICATA: " + string.Join(", ", sconosciute) +
                        " — dire su che tipo di documento gira, in Prove\\Programma.cs");

            var promesse = famiglie
                .Where(f => serve.ContainsKey(f) && !ammessi.Contains(serve[f]))
                .Select(f => f + " (vuole " + serve[f] + ")")
                .ToList();

            Segna("nessuna famiglia servita e' rifiutata prima del primo passo",
                  promesse.Count == 0,
                  promesse.Count == 0
                      ? famiglie.Count + " famiglie, tutte su un tipo ammesso (" +
                        string.Join(", ", ammessi) + ")"
                      : "PROMESSE E IRRAGGIUNGIBILI: " + string.Join("; ", promesse) +
                        " — l'esecutore ammette solo " + string.Join(", ", ammessi));
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
        /// `Banco\CasiDichiarati.cs` ha un elenco ESPLICITO di famiglie, e
        /// scorre solo quelle. Una cartella nuova sotto `Banco\casi\` che non
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
        /// Sotto `casi\` ci sono due cose diverse: i CASI (oggetti con dei
        /// passi e delle attese, che il banco esegue) e i PIANI (array, che
        /// lancia `corri_piani.js` a mano). Le cartelle di soli piani —
        /// `ponte\`, `limiti\`, `viste\`, `pezzi\`, `forma\` — stanno fuori
        /// dall'elenco ed e' giusto cosi'.
        ///
        /// Quindi il controllo non chiede «ogni cartella e' nell'elenco»:
        /// chiede «ogni cartella che contiene almeno un CASO e' nell'elenco».
        /// La differenza e' fra un controllo che descrive la realta' e uno che
        /// pretende un ordine che non c'e'.
        /// </summary>
        private static void OgniCartellaDiCasiEDentroLElenco()
        {
            Gruppo("le cartelle di casi stanno nell'elenco che il banco scorre");

            var sorgente = Path.Combine(Radice(), "Banco", "CasiDichiarati.cs");
            var radiceCasi = Path.Combine(Radice(), "Banco", "casi");
            if (!File.Exists(sorgente) || !Directory.Exists(radiceCasi))
            {
                Segna("il banco e i suoi casi ci sono", false,
                      sorgente + " / " + radiceCasi);
                return;
            }

            var testo = File.ReadAllText(sorgente);
            var blocco = System.Text.RegularExpressions.Regex.Match(testo, @"Famiglie\s*=\s*\{([\s\S]*?)\};");
            if (!blocco.Success)
            {
                // SE L'ELENCO SPARISCE, SPARISCE ANCHE QUESTO CONTROLLO: e
                // un controllo che sparisce in silenzio e' peggio di uno che
                // non c'e' mai stato.
                Segna("trovo l'elenco delle famiglie", false,
                      "non c'e' nessun «Famiglie = { ... }» in CasiDichiarati.cs");
                return;
            }

            var elencate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(blocco.Groups[1].Value, "\"([a-z0-9_-]+)\""))
                elencate.Add(m.Groups[1].Value);

            Segna("l'elenco delle famiglie si legge", elencate.Count >= 10,
                  elencate.Count + " famiglie");
            if (elencate.Count < 10) return;

            // `regressione` NON sta nell'elenco, e non e' un difetto: ha un
            // giro suo (`--regressione`), con un valutatore suo. Sta scritto
            // qui e non in un elenco di eccezioni generico, perche' e' una
            // sola e ha un motivo.
            var fuoriPerDisegno = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "regressione" };

            var mute = new List<string>();
            int guardate = 0, conCasi = 0;
            foreach (var cartella in Directory.GetDirectories(radiceCasi))
            {
                guardate++;
                var nome = Path.GetFileName(cartella);
                bool haCasi = false;
                foreach (var f in Directory.GetFiles(cartella, "*.json"))
                {
                    string grezzo;
                    try { grezzo = File.ReadAllText(f); } catch { continue; }
                    // UN CASO E' UN OGGETTO, UN PIANO E' UN ARRAY. Stessa
                    // distinzione strutturale che fa il banco.
                    var t = grezzo.TrimStart();
                    if (t.StartsWith("{") && t.IndexOf("\"passi\"", StringComparison.Ordinal) >= 0)
                    { haCasi = true; break; }
                }
                if (!haCasi) continue;
                conCasi++;
                if (!elencate.Contains(nome) && !fuoriPerDisegno.Contains(nome))
                    mute.Add(nome);
            }

            Segna("ogni cartella con dei casi dentro e' nell'elenco",
                  mute.Count == 0,
                  mute.Count == 0
                      ? conCasi + " cartelle con casi su " + guardate + ", tutte scorse"
                      : "MUTE, i loro casi non girano mai: " + string.Join(", ", mute) +
                        " — si aggiungono a «Famiglie» in Banco\\CasiDichiarati.cs");
        }

        /// <summary>
        /// Dove sta il TODO, o `null` se in questo clone non c'e'.
        ///
        /// Stesso ordine di `Controlli\dov-e-il-todo.js`, e per lo stesso
        /// motivo: la variabile `TODO_MD` per chi lo tiene altrove,
        /// `note-interne\TODO.md` dove sta oggi, e la radice dov'era prima —
        /// cosi' un clone vecchio continua a funzionare senza accorgersene.
        /// </summary>
        private static string DoveStaIlTodo()
        {
            var suo = Environment.GetEnvironmentVariable("TODO_MD");
            if (!string.IsNullOrEmpty(suo) && File.Exists(suo)) return suo;

            foreach (var c in new[] {
                Path.Combine(Radice(), "note-interne", "TODO.md"),
                Path.Combine(Radice(), "TODO.md") })
            {
                if (File.Exists(c)) return c;
            }
            return null;
        }

        private static void NessunaVoceDelTodoSiChiamaComeUnAltra()
        {
            Gruppo("nel TODO due voci non si chiamano allo stesso modo");

            // IL TODO NON E' PIU' NEL REPOSITORY PUBBLICO: e' un diario di
            // lavoro e vive in `note-interne\`, che il .gitignore esclude.
            //
            // QUI PRIMA C'ERA `Segna(..., false, ...)`, cioe' un ROSSO. Su
            // questa macchina non sarebbe mai scattato; su ogni clone sarebbe
            // stato rosso sempre, per un file che chi clona non ha e non deve
            // avere - una prova che fallisce per tutti tranne chi l'ha scritta
            // non misura il codice, misura chi lo esegue.
            //
            // Adesso la prova si SALTA dicendolo. Il conto dei controlli
            // scende di uno invece di salire di un rosso, che e' la differenza
            // fra «non l'ho controllato» e «l'ho controllato e va male».
            var todo = DoveStaIlTodo();
            if (todo == null)
            {
                Console.WriteLine("  --   il TODO non c'e' in questo clone: e' interno e non si pubblica.");
                Console.WriteLine("       QUESTO CONTROLLO NON E' STATO FATTO. Con TODO_MD si indica una copia.");
                return;
            }
            string[] righe;
            try { righe = File.ReadAllLines(todo); }
            catch (Exception ex) { Segna("il TODO si legge", false, ex.Message); return; }

            var intestazione = new System.Text.RegularExpressions.Regex(
                @"^#### \[(.)\] (\S+)\s*(.*)$");
            var traccia = new System.Text.RegularExpressions.Regex(
                @"traccia originale|progetto originale|tenut[ao] per|per riferimento|com'era scritta",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // nome -> quante volte, e quante di quelle erano tracce
            var quante = new Dictionary<string, int>(StringComparer.Ordinal);
            var tracce = new Dictionary<string, int>(StringComparer.Ordinal);
            int totali = 0;

            foreach (var r in righe)
            {
                var m = intestazione.Match(r);
                if (!m.Success) continue;
                totali++;
                var nome = m.Groups[2].Value;
                quante[nome] = (quante.ContainsKey(nome) ? quante[nome] : 0) + 1;
                if (traccia.IsMatch(m.Groups[3].Value))
                    tracce[nome] = (tracce.ContainsKey(nome) ? tracce[nome] : 0) + 1;
            }

            // UNA PROVA CHE NON TROVA NIENTE PASSA SEMPRE. Se un giorno le
            // intestazioni cambiano forma, questa riga diventa rossa invece di
            // lasciare che il controllo si spenga in silenzio.
            Segna("ci sono voci da controllare", totali >= 100, totali + " intestazioni");

            var doppie = new List<string>();
            foreach (var v in quante)
            {
                int t = tracce.ContainsKey(v.Key) ? tracce[v.Key] : 0;
                // una voce viva piu' le sue tracce: va bene. Due vive no.
                if (v.Value - t > 1)
                    doppie.Add(v.Key + " (×" + v.Value + ", di cui tracce: " + t + ")");
            }
            doppie.Sort(StringComparer.Ordinal);

            Segna("due voci diverse non si chiamano allo stesso modo",
                  doppie.Count == 0,
                  doppie.Count == 0
                      ? totali + " intestazioni, nessun nome usato due volte per due cose"
                      : "STESSO NOME, VOCI DIVERSE: " + string.Join("; ", doppie));
        }

        /// <summary>
        /// IL RIFIUTO DI `quote_dal_modello` DICE ANCORA QUELLO CHE SERVE.
        ///
        /// ── PERCHE' ESISTE, E COSA NON E' ────────────────────────────────
        ///
        /// Misurato il 18/9/2026: su un pezzo costruito dal ponte, con **sette
        /// quote tutte marcate per il disegno**, cinque strade di import ne
        /// portavano **zero**. **Il 26/9/2026 l'import funziona fuori processo**
        /// (7 quote sullo stesso pezzo, due sonde) e la prova di comportamento
        /// c'e': `quote/le_quote_arrivano_dal_modello`, col rifiuto senza viste e
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
        private static void IlRifiutoDelleQuoteDiceAncoraQuelloCheServe()
        {
            Gruppo("il rifiuto delle quote dice ancora quello che serve");

            var file = Path.Combine(Radice(), "Riscontro.Disegno", "OperazioniTavola.cs");
            if (!File.Exists(file)) { Segna("il sorgente c'e'", false, file); return; }
            string codice;
            try { codice = File.ReadAllText(file); }
            catch (Exception ex) { Segna("il sorgente si legge", false, ex.Message); return; }

            // L'ANCORA. Se sparisce, questo controllo non ha piu' un soggetto e
            // deve diventare ROSSO: una prova che non trova cio' che cerca e
            // passa e' peggio di nessuna prova.
            const string ancora = "Non e' arrivata nessuna quota";
            if (codice.IndexOf(ancora, StringComparison.Ordinal) < 0)
            {
                Segna("trovo il rifiuto di quote_dal_modello", false,
                      "«" + ancora + "» non c'e' piu' in OperazioniTavola.cs: " +
                      "se l'import e' stato riparato questa prova va RISCRITTA, non tolta");
                return;
            }
            Segna("trovo il rifiuto di quote_dal_modello", true, "");

            // Ogni pezzo, e accanto PERCHE' quel pezzo: un elenco di stringhe
            // senza il motivo e' la prima cosa che qualcuno taglia.
            var pezzi = new[]
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

            var mancanti = new List<string>();
            foreach (var p in pezzi)
                if (codice.IndexOf(p[0], StringComparison.Ordinal) < 0)
                    mancanti.Add("«" + p[0] + "» (" + p[1] + ")");

            Segna("il rifiuto nomina ancora quante quote sono marcate e cosa ha provato",
                  mancanti.Count == 0,
                  mancanti.Count == 0
                      ? "tutti e " + pezzi.Length + " i pezzi ci sono"
                      : "TOLTO DAL MESSAGGIO: " + string.Join("; ", mancanti));
        }

        /// <summary>
        /// LA LIBRERIA CHE CARICA IL PONTE E' LA STESSA CHE MISURA IL BANCO.
        ///
        /// ── PERCHE' ESISTE, E QUANTO E' COSTATA ──────────────────────────
        ///
        /// Ce ne sono **due copie**, e vivono in due posti:
        ///
        ///   Riscontro.Disegno\bin\Release\Riscontro.Disegno.dll   <- il banco
        ///   bin\Riscontro.Disegno.dll                             <- il ponte
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
        private static void LaLibreriaDelPonteELaSTESSADelBanco()
        {
            Gruppo("le TRE copie della libreria sono la stessa");

            var delBanco = Path.Combine(Radice(), "Riscontro.Disegno", "bin", "Release",
                                        "Riscontro.Disegno.dll");
            var delPonte = Path.Combine(Radice(), "bin", "Riscontro.Disegno.dll");

            // ── LA TERZA, CHE QUESTO CONTROLLO NON GUARDAVA ──────────────
            //
            // `Banco\bin\Riscontro.Disegno.dll` e' quella che
            // `ProvaSaldatura.exe` carica DAVVERO, e per due giorni questo
            // gruppo si e' chiamato «il ponte e il banco caricano la stessa
            // libreria» confrontando due copie di cui **nessuna delle due
            // era quella del banco in esecuzione**. La prima e' la sorgente
            // da cui `banco.ps1` la copia compilando.
            //
            // Il 24/9/2026 e' costato un giro: lanciato `ProvaSaldatura.exe`
            // a mano per iterare in fretta, ha caricato la copia del giorno
            // prima e ha detto che due predefiniti appena implementati **non
            // esistevano**. Erano nel sorgente e nelle altre due DLL.
            //
            // Questo controllo non impedisce di lanciare l'eseguibile a mano
            // — niente lo impedisce — ma dice **subito** che le copie sono
            // divergute, invece di lasciarlo scoprire a un resoconto che
            // accusa il codice sbagliato.
            var accantoAlBanco = Path.Combine(Radice(), "Banco", "bin",
                                              "Riscontro.Disegno.dll");

            bool c1 = ProdottoDaBuild(delBanco), c2 = ProdottoDaBuild(delPonte);

            // Senza interop la libreria non si poteva compilare: mancano tutte e
            // due perche' la macchina non ce la fa, non perche' qualcuno abbia
            // sbagliato. Vedi `NonQui`.
            if (!c1 && !c2 && !IlPonteSiPotevaCompilare())
            {
                NonQui("il ponte e il banco caricano la stessa libreria",
                       "qui SOLIDWORKS non c'e': Riscontro.Disegno.dll non si compila, " +
                       "e due file che non esistono non si possono confrontare.");
                return;
            }

            Segna("la libreria che misura il banco c'e'", c1, delBanco);
            Segna("la libreria che carica il ponte c'e'", c2,
                  c2 ? delPonte : delPonte + " — si produce con .\\build.ps1");
            if (!c1 || !c2) return;

            string v1 = Versione(delBanco), v2 = Versione(delPonte);
            Segna("le due versioni si leggono", v1 != null && v2 != null,
                  (v1 ?? "?") + " / " + (v2 ?? "?"));
            if (v1 == null || v2 == null) return;

            Segna("la sorgente e il ponte hanno la STESSA versione della libreria",
                  string.Equals(v1, v2, StringComparison.Ordinal),
                  string.Equals(v1, v2, StringComparison.Ordinal)
                      ? "tutti e due " + v1
                      : "SORGENTE " + v1 + " contro PONTE " + v2 +
                        " — chi ha compilato il solo progetto ha aggiornato il primo e " +
                        "non il secondo: si rilancia .\\build.ps1, che li allinea");

            // ── E LA TERZA COPIA: SI GUARDA LA RADICE, NON IL SINTOMO ────
            //
            // `Banco\bin\Riscontro.Disegno.dll` e' un RESIDUO: `banco.ps1`
            // passa `--libreria` puntando alla sorgente, e nessuno aggiorna
            // quella copia. Pretendere che combaci sarebbe un rosso perenne
            // su un file che il giro vero non guarda mai.
            //
            // Cio' che conta e' il PREDEFINITO: cosa carica
            // `ProvaSaldatura.exe` se qualcuno lo lancia a mano senza
            // `--libreria`. Fino al 24/9/2026 era la copia accanto a se', e
            // mi ha fatto misurare per un giro il codice di sei versioni
            // prima dicendo che due predefiniti appena scritti «non
            // esistevano». Adesso e' la stessa che usa `banco.ps1`.
            //
            // Si legge dal SORGENTE, perche' e' li' che la scelta vive, e
            // perche' un controllo che leggesse il residuo tornerebbe a
            // sorvegliare il sintomo.
            var sorgenteBanco = Path.Combine(Radice(), "Banco", "Program.cs");
            if (File.Exists(sorgenteBanco))
            {
                var testo = File.ReadAllText(sorgenteBanco);
                bool cercaLaBuona =
                    testo.IndexOf("LibreriaPredefinita", StringComparison.Ordinal) >= 0 &&
                    testo.IndexOf("\"Riscontro.Disegno\", \"bin\", \"Release\"",
                                  StringComparison.Ordinal) >= 0;
                Segna("senza «--libreria» il banco cerca PRIMA la libreria appena compilata",
                      cercaLaBuona,
                      cercaLaBuona
                          ? "il ripiego accanto all'eseguibile resta, ma viene dopo"
                          : "il predefinito punta accanto all'eseguibile: chi lancia " +
                            "`ProvaSaldatura.exe` a mano misura una copia che nessuno " +
                            "aggiorna");

                // E se il residuo c'e', lo si DICE — senza accusare: sapere
                // che e' vecchio spiega un log confuso di sei mesi fa.
                if (ProdottoDaBuild(accantoAlBanco))
                {
                    string v3 = Versione(accantoAlBanco);
                    Segna("e si sa se accanto all'eseguibile c'e' un residuo vecchio", true,
                          string.Equals(v1, v3, StringComparison.Ordinal)
                              ? "ce n'e' una, ed e' allineata (" + v3 + ")"
                              : "ce n'e' una FERMA A " + v3 + " contro " + v1 +
                                ": non la usa piu' nessuno, ma se un log vecchio " +
                                "non torna, e' lei");
                }
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
            string informativa = InformazioneDiVersione(delBanco);
            Segna("la versione mostrata da Windows e' quella vera",
                  informativa != null &&
                  v1.StartsWith(informativa, StringComparison.Ordinal),
                  informativa == null
                      ? "non si legge"
                      : "assembly " + v1 + ", proprieta' del file " + informativa +
                        (v1.StartsWith(informativa, StringComparison.Ordinal)
                            ? ""
                            : " — si allinea AssemblyInformationalVersion in " +
                              "Riscontro.Disegno\\Properties\\AssemblyInfo.cs"));
        }

        /// <summary>
        /// `AssemblyInformationalVersion` del file, cioe' quella che si legge
        /// in Proprieta' → Dettagli → Versione prodotto. `null` se non c'e'.
        /// </summary>
        private static string InformazioneDiVersione(string percorso)
        {
            try
            {
                var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(percorso)
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
        private static string Versione(string dll)
        {
            try { return System.Reflection.AssemblyName.GetAssemblyName(dll).Version.ToString(); }
            catch { return null; }
        }

        /// <summary>
        /// `prove.xml` in formato JUnit: e' quello che ogni CI sa leggere senza
        /// che nessuno le insegni niente.
        /// </summary>
        private static void ScriviJUnit()
        {
            try
            {
                var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "prove.xml");
                var x = new StringBuilder();
                x.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                x.AppendLine("<testsuites name=\"Prove.Contratti\" tests=\"" + Esiti.Count +
                             "\" failures=\"" + Falliti +
                             "\" skipped=\"" + NonEseguibiliQui + "\">");
                foreach (var g in Esiti.GroupBy(e => e.Gruppo))
                {
                    x.AppendLine("  <testsuite name=\"" + Xml(g.Key) + "\" tests=\"" + g.Count() +
                                 "\" failures=\"" + g.Count(e => !e.Ok) +
                                 "\" skipped=\"" + g.Count(e => e.NonQui) + "\">");
                    foreach (var e in g)
                    {
                        x.Append("    <testcase classname=\"" + Xml(g.Key) +
                                 "\" name=\"" + Xml(e.Nome) + "\"");
                        // UN SALTATO NON E' UN PASSATO, nemmeno nell'XML: un
                        // `<testcase />` nudo e' un verde per chiunque legga il
                        // file, e il file lo legge una CI che poi mostra un segno
                        // di spunta. `<skipped>` e' la voce che JUnit ha proprio
                        // per questo, e ogni interfaccia la sa disegnare diversa.
                        if (e.NonQui)
                        {
                            x.AppendLine(">");
                            x.AppendLine("      <skipped message=\"" + Xml(e.Dettaglio) + "\" />");
                            x.AppendLine("    </testcase>");
                        }
                        else if (e.Ok) x.AppendLine(" />");
                        else
                        {
                            x.AppendLine(">");
                            x.AppendLine("      <failure message=\"" + Xml(e.Dettaglio) + "\" />");
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
