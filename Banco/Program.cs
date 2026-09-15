using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ProvaSaldatura
{
    /// <summary>
    /// Prova il DLL VERO dell'add-in contro SolidWorks, non una copia.
    ///
    /// PERCHE' COSI'
    /// Le tornate precedenti hanno misurato le API di SolidWorks riscrivendo
    /// qui le chiamate. Serviva a scoprire la ricetta, ma non dice niente su
    /// cio' che verra' installato: fra il programma di prova e l'add-in ci
    /// sono la lettura dei parametri, le conversioni di unita', i controlli e
    /// i messaggi d'errore — cioe' la meta' del codice che puo' sbagliare.
    ///
    /// Qui si carica `OpenPDM.SwAddin.dll` appena compilata e si chiamano i
    /// suoi tipi interni per riflessione, con gli stessi argomenti che
    /// arriverebbero dal piano del server. Quello che passa di qui e' quello
    /// che il progettista avra' in mano.
    ///
    /// Ogni prova lavora su una parte usa e getta, che viene chiusa.
    /// </summary>
    internal static partial class Program
    {
        /// <summary>
        /// Dove sta la libreria di disegno. Predefinito: accanto
        /// all'eseguibile. Si cambia con `--libreria &lt;percorso&gt;`.
        ///
        /// PRIMA ERA UNA COSTANTE che puntava a una cartella temporanea di
        /// sessione, e quel percorso oggi non esiste piu'. Un banco che si
        /// puo' lanciare solo dalla macchina e dalla settimana in cui e' stato
        /// scritto non e' una misura ripetibile: e' un aneddoto.
        /// </summary>
        private static string _libreria =
            Path.Combine(
                Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? ".",
                "Riscontro.Disegno.dll");

        /// <summary>I nomi dei tipi, dopo lo spostamento in libreria.</summary>
        /// <remarks>
        /// Erano `OpenPDM.SwAddin.Agente.*`. Le voci B4 e B5 hanno spostato le
        /// operazioni in `Riscontro.Disegno`, e da allora questo banco
        /// **non partiva piu'**: `GetType(..., true)` solleva su un tipo che
        /// non c'e'. Non se n'era accorto nessuno perche' il banco viveva
        /// fuori dal repository e non lo lanciava piu' niente — che e'
        /// esattamente il motivo per cui la voce F1 esiste.
        /// </remarks>
        private const string NS = "Riscontro.Disegno.";
        /// <summary>
        /// I PROFILI NORMATI, che sono di chi lavora e non del repository.
        ///
        /// Le prove sulle saldature hanno bisogno dei file di libreria veri:
        /// un `.SLDLFP` inventato non misura niente. Il percorso predefinito e'
        /// quello di questa installazione, e si scavalca con
        /// `PROFILI_SALDATURA`.
        ///
        /// Era una costante. Chi clona il repository non ha quel disco, e con
        /// una costante scopriva di non averlo quando il banco falliva senza
        /// dire perche'.
        /// </summary>
        private static readonly string ProfiliSaldatura =
            System.Environment.GetEnvironmentVariable("PROFILI_SALDATURA")
            ?? @"<impostazioni SOLIDWORKS>\ELEMENTI SALDATURA\NORMATI";

        private static readonly string QUADRO =
            System.IO.Path.Combine(ProfiliSaldatura, "TUBO STRUTTURALE QUADRO.SLDLFP");
        private static readonly string RETT =
            System.IO.Path.Combine(ProfiliSaldatura, "TUBO STRUTTURALE RETTANGOLARE.SLDLFP");

        private static ISldWorks _sw;
        private static Assembly _addin;
        private static Type _tOperazioni, _tStato, _tEsito;
        private static readonly List<string> _esiti = new List<string>();
        private static int _falliti;

        /// <summary>
        /// LE LIBRERIE DI DASSAULT NON SI COPIANO NEL REPOSITORY, e nemmeno
        /// accanto all'eseguibile del banco.
        ///
        /// Il csproj le referenzia con `Private=False`, quindi in `bin` non
        /// ci finiscono: non si possono ridistribuire per sbaglio, e non ci si
        /// lega a una versione. Al loro posto si prende quella INSTALLATA su
        /// questa macchina, che e' per definizione la stessa che sta girando.
        ///
        /// PERCHE' MAIN E' SPEZZATO IN DUE. Il gestore va installato PRIMA che
        /// il JIT compili un metodo che nomina un tipo interop, e il JIT
        /// compila tutto il metodo prima di eseguirne la prima riga. Con il
        /// giro dentro `Main` il gestore veniva installato troppo tardi e
        /// l'eccezione usciva "in Program.Main", cioe' prima di qualunque riga
        /// scritta qui. `Avvio` sta a parte e non si fa incorporare.
        /// </summary>
        private static int Main(string[] argomentiRiga)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (mittente, argomento) =>
            {
                var nome = new AssemblyName(argomento.Name).Name;
                if (!nome.StartsWith("SolidWorks.Interop", StringComparison.OrdinalIgnoreCase))
                    return null;
                var posti = new List<string>();
                var suo = System.Environment.GetEnvironmentVariable("SW_REDIST");
                if (!string.IsNullOrWhiteSpace(suo)) posti.Add(suo);
                posti.Add(@"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist");
                posti.Add(@"C:\Program Files\SOLIDWORKS\api\redist");
                foreach (var cartella in posti)
                {
                    var file = Path.Combine(cartella, nome + ".dll");
                    if (File.Exists(file))
                    {
                        try { return Assembly.LoadFrom(file); } catch { }
                    }
                }
                Console.WriteLine("non trovo " + nome + ": indica la cartella redist di " +
                                  "SOLIDWORKS nella variabile SW_REDIST.");
                return null;
            };
            return Avvio(argomentiRiga);
        }

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int Avvio(string[] argomentiRiga)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            // `--libreria <percorso>` prima di tutto il resto: se la libreria
            // e' un'altra, e' un'altra anche la misura, e va detto in testa.
            var argomenti = new List<string>(argomentiRiga ?? new string[0]);
            int dove = argomenti.IndexOf("--libreria");
            if (dove >= 0 && dove + 1 < argomenti.Count)
            {
                _libreria = argomenti[dove + 1];
                argomenti.RemoveRange(dove, 2);
            }
            // `--lento`: le prove che costano MINUTI, non secondi.
            //
            // Il tetto sulle lavorazioni (E12) va provato su un pezzo con piu'
            // di ottanta lavorazioni, e ottantacinque tagli costano ~26 secondi
            // l'uno: mezz'ora per una prova sola, contro i tredici minuti di
            // tutte le altre insieme. Su ogni giro sarebbe un prezzo che nessuno
            // paga volentieri, e una prova che pesa troppo e' una prova che
            // prima o poi qualcuno salta.
            //
            // MA UN CONTROLLO SALTATO SI DEVE VEDERE. Quando non si lancia, il
            // banco lo DICE e lo conta fra i saltati: un controllo che sparisce
            // in silenzio e' come un controllo che non scatta mai.
            _lento = argomenti.Remove("--lento");
            argomentiRiga = argomenti.ToArray();

            if (!File.Exists(_libreria))
            {
                Console.WriteLine("non trovo la libreria: " + _libreria);
                Console.WriteLine("indicala con  --libreria <percorso di Riscontro.Disegno.dll>");
                return 1;
            }

            try { _sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (Exception ex) { Console.WriteLine("non agganciabile: " + ex.Message); return 1; }

            // SI LEGGONO I BYTE E SI CARICA DA QUELLI.
            // `Assembly.LoadFrom` su un percorso di rete (S:) risponde
            // NotSupportedException, e la libreria sta proprio li'. Leggere il
            // file e passare i byte gira attorno al problema senza chiedere a
            // nessuno di copiarsi le cose a mano.
            _addin = Assembly.Load(File.ReadAllBytes(_libreria));
            Console.WriteLine("SolidWorks " + _sw.RevisionNumber());
            Console.WriteLine("libreria " + _addin.GetName().Version + "  (" + _libreria + ")");
            _tOperazioni = _addin.GetType(NS + "OperazioniSw", true);
            _tStato = _addin.GetType(NS + "StatoDocumento", true);
            _tEsito = _addin.GetType(NS + "OperazioniSw+Esito", true);

            // MODO SESSIONE: invece delle prove, il giro completo — un
            // documento tenuto aperto mentre qualcun altro pianifica.
            if (argomentiRiga != null && argomentiRiga.Length >= 2 &&
                argomentiRiga[0] == "--sessione")
            {
                // Terzo argomento facoltativo: un pezzo gia' fatto da
                // riprendere, invece di una parte vuota.
                string daAprire = argomentiRiga.Length >= 3 ? argomentiRiga[2] : null;
                return Sessione(argomentiRiga[1], daAprire);
            }

            // Il diario si apre QUI e non prima: l'aggancio a SOLIDWORKS e il
            // caricamento della libreria possono far uscire dal programma, e un
            // diario aperto per un giro che non e' mai cominciato direbbe
            // «piantato al primo passo» quando invece non e' partito affatto.
            ApriIlDiario();
            Diario("SolidWorks " + _sw.RevisionNumber() +
                   ", libreria " + _addin.GetName().Version +
                   (_lento ? ", con --lento" : ""));

            Sezioni();
            Materiali();
            Telai();
            Fori();
            Materiale();
            Stato();
            ProvaFotografia();
            if (_lento) ProvaTettoLavorazioni();
            else Saltata("il tetto sulle lavorazioni (E12)",
                         "85 lavorazioni, circa mezz'ora: si lancia con --lento");
            NuoveOperazioni();

            Console.WriteLine();
            Console.WriteLine(new string('=', 76));
            foreach (var r in _esiti) Console.WriteLine(r);
            Console.WriteLine();
            // LE SALTATE SI CONTANO A PARTE E SI DICONO. «88 prove, 0 fallite»
            // su un giro in cui una prova non e' stata lanciata e' una frase
            // vera che fa credere una cosa falsa.
            var riepilogo =
                (_esiti.Count - _saltate) + " prove, " + _falliti + " fallite" +
                (_saltate > 0 ? ", " + _saltate + " SALTATE (--lento le lancia)" : "");
            Console.WriteLine(riepilogo);
            ChiudiIlDiario(riepilogo);
            return _falliti == 0 ? 0 : 1;
        }

        private static void Segna(string nome, bool ok, string dettaglio = "")
        {
            if (!ok) _falliti++;
            var riga = (ok ? "  OK   " : "  NO   ") + nome.PadRight(52) + dettaglio;
            _esiti.Add(riga);
            Console.WriteLine(riga);
            Diario(riga);
        }

        // ══════════════════════════════════════════════════════════════════
        // IL DIARIO: cosa sta facendo il banco, mentre lo fa
        //
        // ── IL PROBLEMA, E QUANTO E' COSTATO ──────────────────────────────
        //
        // Un giro dura dai tredici ai diciassette minuti, e da fuori **un giro
        // lento e un giro piantato si vedono uguali**. Il 14/9/2026 un giro
        // bloccato sull'ottantaquattresimo taglio di `ProvaTettoLavorazioni` e'
        // sembrato per un'ora e mezza un giro che stava lavorando: l'unico modo
        // di distinguerli e' stato campionare il contatore di CPU di
        // SLDWORKS.exe e guardare l'ora di modifica di un file.
        //
        // ── E `ultimo-giro.txt` NON LO SCRIVE IL BANCO ────────────────────
        //
        // Va detto perche' cambia il rimedio: il banco stampa tutto su stdout,
        // e `ultimo-giro.txt` e' una REDIREZIONE di chi lo lancia. Quindi
        // quanto sia aggiornato non dipende dal banco ma dalla shell: con
        // `Tee-Object` cresce riga per riga, con `Out-String` compare tutto
        // insieme alla fine. Aggiungere un `Flush` da qualche parte dentro il
        // banco non avrebbe cambiato niente — non c'e' nessun «da qualche
        // parte»: non c'era un file.
        //
        // Adesso ce n'e' uno **del banco**, `Banco/in-corso.txt`, scritto e
        // svuotato riga per riga. `ultimo-giro.txt` resta cio' che era, e il
        // riepilogo di fine giro non cambia di una virgola: e' quello che
        // LEGGIMI documenta e che si guarda quando il giro e' finito.
        //
        // ── PERCHE' C'E' UN TEMPO SU OGNI RIGA ────────────────────────────
        //
        // «A che punto e'» si vede dall'ultima riga. «E' piantato» no: per
        // quello serve sapere **da quanto** quella riga e' li'. Col tempo
        // accanto bastano due letture a distanza di un minuto, e non serve
        // piu' andare a cercare l'ora di modifica del file — che e' il dato
        // che l'altra volta ha ingannato.
        //
        // ── NON PUO' FAR FALLIRE IL GIRO ──────────────────────────────────
        //
        // E' uno strumento per guardare le prove: se si rompe lui, le prove
        // devono andare avanti lo stesso. Ogni scrittura sta in un `try`
        // vuoto. **Ma il fatto che non si sia potuto aprire si dice una volta
        // su stdout**: un aiuto diagnostico che sparisce in silenzio e' esso
        // stesso il difetto che questo progetto insegue.
        // ══════════════════════════════════════════════════════════════════

        private static StreamWriter _diario;
        private static readonly Stopwatch _cronometro = Stopwatch.StartNew();

        /// <summary>
        /// Apre `Banco/in-corso.txt` accanto all'eseguibile, un livello sopra
        /// `bin\`. Se non ci riesce lo dice e si va avanti senza.
        /// </summary>
        private static void ApriIlDiario()
        {
            string percorso = null;
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var banco = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                percorso = Path.Combine(
                    banco != null ? banco.FullName : bin, "in-corso.txt");

                // SI TRONCA: dentro c'e' il giro di ADESSO. Un file che
                // accumula i giri costringe chi legge a chiedersi quale riga
                // sia di quale giro, ed e' la domanda che non deve doversi
                // fare chi sta cercando di capire se e' piantato.
                _diario = new StreamWriter(percorso, false) { AutoFlush = true };
                // L'INTESTAZIONE NON NOMINA LA PAROLA CHE CHIUDE IL GIRO.
                //
                // La prima scrittura diceva «se l'ultima riga non e' FINE...»,
                // e cosi' il primo `grep FINE` su questo file trovava
                // l'INTESTAZIONE e rispondeva «giro finito» dopo tre secondi.
                // E' successo subito, alla prima prova, a chi l'aveva appena
                // scritto. Adesso quella parola compare in un punto solo di
                // tutto il file, e il controllo piu' ingenuo che si possa fare
                // e' quello giusto.
                _diario.WriteLine("# il giro in corso, riga per riga. " +
                                  "Il numero fra parentesi e' quanti secondi " +
                                  "sono passati dall'avvio.");
                _diario.WriteLine("# l'ultima riga di un giro arrivato in fondo " +
                                  "porta il riepilogo; se non c'e', il giro si e' " +
                                  "fermato prima.");
                _diario.WriteLine("# se il tempo dell'ultima riga non cresce fra " +
                                  "due letture, e' piantato.");
            }
            catch (Exception ex)
            {
                _diario = null;
                Console.WriteLine("(niente diario" +
                                  (percorso == null ? "" : " in " + percorso) +
                                  ": " + ex.Message + ". Le prove vanno avanti.)");
            }
        }

        private static void Diario(string riga)
        {
            if (_diario == null) return;
            try
            {
                _diario.WriteLine(
                    "[" + _cronometro.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)
                        .PadLeft(7) + "s] " + riga);
            }
            catch { }
        }

        /// <summary>
        /// L'ultima riga, quella che dice che il giro e' arrivato in fondo.
        ///
        /// E' la meta' che conta: senza, un file che si ferma vuol dire «sta
        /// lavorando» oppure «e' morto», e sono di nuovo indistinguibili.
        /// </summary>
        private static void ChiudiIlDiario(string riepilogo)
        {
            if (_diario == null) return;
            try
            {
                Diario("FINE — " + riepilogo);
                _diario.Dispose();
            }
            catch { }
            _diario = null;
        }

        /// <summary>
        /// Una prova che NON e' stata lanciata, e il perche'.
        ///
        /// Non conta come fallita e non conta come riuscita: conta come
        /// saltata, e si vede. Una prova che sparisce dal resoconto quando non
        /// si lancia e' peggio di una che non esiste — chi legge «88 prove, 0
        /// fallite» crede di aver provato tutto.
        /// </summary>
        private static void Saltata(string nome, string perche)
        {
            _saltate++;
            var riga = "  --   " + nome.PadRight(52) + "SALTATA: " + perche;
            _esiti.Add(riga);
            Console.WriteLine(riga);
            Diario(riga);
        }

        private static int _saltate;
        private static bool _lento;

        // ==================================================================
        // 1. il misuratore di sezione dell'add-in
        // ==================================================================
        private static void Sezioni()
        {
            Console.WriteLine();
            Console.WriteLine("=== Services.SezioneProfilo ===");
            var t = _addin.GetType(NS + "SezioneProfilo", true);
            var m = t.GetMethod("SporgenzeDelTelaio",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            foreach (var caso in new[]
            {
                new object[] { QUADRO, "80X80X5", 0, 1, 80.0, 80.0 },
                new object[] { QUADRO, "40x40x3", 0, 2, 40.0, 40.0 },
                new object[] { RETT, "80X50X5", 0, 1, 50.0, 50.0 },
                new object[] { RETT, "80X50X5", 0, 2, 80.0, 80.0 },
            })
            {
                var r = m.Invoke(null, new object[]
                {
                    _sw, caso[0], caso[1], caso[2], caso[3], 0.0,
                    1 /* quartabuono */, 0.0,
                    (Action<string>)(s => { }),
                }) as double[];
                bool ok = r != null &&
                          Math.Abs(r[0] - (double)caso[4]) < 0.01 &&
                          Math.Abs(r[1] - (double)caso[5]) < 0.01;
                Segna("sporgenza " + caso[1] + " su assi " + caso[2] + "," + caso[3], ok,
                      r == null ? "null" : string.Format("misurato {0:0.##}/{1:0.##}, atteso {2:0.##}/{3:0.##}",
                          r[0], r[1], caso[4], caso[5]));
            }
        }

        // ==================================================================
        // 2. la risoluzione dei nomi dei materiali
        // ==================================================================
        private static void Materiali()
        {
            Console.WriteLine();
            Console.WriteLine("=== Services.MaterialiSolidWorks ===");
            var t = _addin.GetType(NS + "MaterialiSolidWorks", true);
            var tutti = t.GetMethod("Tutti",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var risolvi = t.GetMethod("Risolvi",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            var elenco = tutti.Invoke(null, new object[] { _sw, (Action<string>)(s => { }) })
                as System.Collections.IList;
            Segna("legge gli archivi di materiali", elenco != null && elenco.Count > 100,
                  (elenco == null ? "null" : elenco.Count + " materiali"));

            foreach (var caso in new[]
            {
                new[] { "AISI 304", "AISI 304" },
                new[] { "1.0037 (S235JR)", "1.0037 (S235JR)" },
                new[] { "S235JR", "1.0037 (S235JR)" },
                new[] { "S275JR", "1.0044 (S275JR)" },
                new[] { "S235", null },              // ambiguo: deve rifiutare
                new[] { "6082", null },              // non esiste: deve rifiutare
                new[] { "acciaio inventato", null },
            })
            {
                var argomenti = new object[] { _sw, caso[0], "", (Action<string>)(s => { }), null };
                var trovato = risolvi.Invoke(null, argomenti);
                string perche = argomenti[4] as string;
                string nome = null;
                if (trovato != null)
                    nome = trovato.GetType().GetField("Nome").GetValue(trovato) as string;

                bool ok = caso[1] == null ? (trovato == null && !string.IsNullOrEmpty(perche))
                                          : (nome == caso[1]);
                Segna("materiale «" + caso[0] + "»", ok,
                      trovato == null ? ("rifiutato: " + (perche ?? "").Substring(0, Math.Min(70, (perche ?? "").Length)))
                                      : ("-> " + nome));
            }
        }

        // ==================================================================
        // 3. il telaio a quote esterne, passando dall'add-in
        // ==================================================================
        private static void Telai()
        {
            Console.WriteLine();
            Console.WriteLine("=== telaio_rettangolare (add-in) ===");
            Telaio("quadro 80x80x5, 1000x800, frontale", QUADRO, "80X80X5", 1000, 800, "frontale", true);
            Telaio("quadro 40x40x3, 1000x800, superiore", QUADRO, "40x40x3", 1000, 800, "superiore", true);
            Telaio("rettangolare 80x50x5, 1200x600, frontale", RETT, "80X50X5", 1200, 600, "frontale", true);
            Telaio("quadro 80x80x5, 600x400, angolo sull'origine", QUADRO, "80X80X5", 600, 400, "frontale", false);
            // e uno che DEVE fallire: sezione piu' grande del telaio
            Telaio("quadro 80x80x5, 60x60 (deve fallire)", QUADRO, "80X80X5", 60, 60, "frontale", true, deveFallire: true);
        }

        private static void Telaio(string nome, string profilo, string config,
                                   double L, double W, string piano, bool centrato,
                                   bool deveFallire = false)
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                var argomenti = new Dictionary<string, object>
                {
                    ["profilo_percorso"] = profilo,
                    ["configurazione"] = config,
                    ["lunghezza_mm"] = L,
                    ["larghezza_mm"] = W,
                    ["piano"] = piano,
                    ["centrato"] = centrato,
                    ["trattamento_angolo"] = "quartabuono",
                    ["segmenti"] = SegmentiNominali(L, W, piano, centrato),
                    ["telaio"] = new Dictionary<string, object>
                    {
                        ["lunghezza_mm"] = L,
                        ["larghezza_mm"] = W,
                        ["piano"] = piano,
                        ["centrato"] = centrato,
                        ["misure"] = "ingombro",
                    },
                };

                string errore;
                var misure = Esegui(doc, "telaio_rettangolare", argomenti, out errore);
                if (deveFallire)
                {
                    Segna(nome, misure == null && !string.IsNullOrEmpty(errore),
                          misure == null ? ("rifiutato: " + Corto(errore)) : "NON ha rifiutato");
                    return;
                }
                if (misure == null) { Segna(nome, false, "fallito: " + Corto(errore)); return; }

                string ingombro = misure.ContainsKey("ingombro_esterno_mm")
                    ? Convert.ToString(misure["ingombro_esterno_mm"]) : "(non verificato)";
                bool verificato = misure.ContainsKey("ingombro_verificato") &&
                                  Convert.ToBoolean(misure["ingombro_verificato"]);
                // e si controlla anche qui, indipendentemente
                var scatola = ScatolaDelDocumento(doc);
                double gu, gv;
                Assi(piano, out int u, out int v);
                gu = scatola[3 + u] - scatola[u];
                gv = scatola[3 + v] - scatola[v];
                bool ok = verificato && Math.Abs(gu - L) < 0.5 && Math.Abs(gv - W) < 0.5;
                Segna(nome, ok, string.Format(
                    "add-in dice {0}, misurato {1:0.##}x{2:0.##}, sporgenza {3}, barre {4}",
                    ingombro, gu, gv,
                    misure.ContainsKey("sporgenza_profilo_mm") ? misure["sporgenza_profilo_mm"] : "?",
                    misure.ContainsKey("barre") ? misure["barre"] : "?"));
            }
            catch (Exception ex) { Segna(nome, false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        // ==================================================================
        // 4. i fori guidati, passando dall'add-in
        // ==================================================================
        private static void Fori()
        {
            Console.WriteLine();
            Console.WriteLine("=== foro_guidato (add-in) ===");
            Foro("quattro M10 passanti", "passaggio", "M10", true, 0,
                 new[] { new[] { -70.0, -40.0 }, new[] { 70.0, -40.0 },
                         new[] { 70.0, 40.0 }, new[] { -70.0, 40.0 } }, 11.0);
            Foro("un M8 filettato passante", "filettato", "M8", true, 0,
                 new[] { new[] { 0.0, 0.0 } }, 6.8);
            Foro("un M12 filettato cieco 12", "filettato", "M12", false, 12,
                 new[] { new[] { 30.0, 20.0 } }, 10.2);
            Foro("due lamati M8", "lamato", "M8", true, 0,
                 new[] { new[] { -50.0, 0.0 }, new[] { 50.0, 0.0 } }, 9.0);
            Foro("taglia inventata (deve fallire)", "filettato", "M7", true, 0,
                 new[] { new[] { 0.0, 0.0 } }, 0, deveFallire: true);
            Foro("due fori nello stesso punto (deve fallire)", "passaggio", "M8", true, 0,
                 new[] { new[] { 10.0, 0.0 }, new[] { 10.0, 0.0 } }, 0, deveFallire: true);
        }

        private static void Foro(string nome, string tipo, string taglia, bool passante,
                                 double profondita, double[][] posizioni, double diametroAtteso,
                                 bool deveFallire = false)
        {
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                Piastra(doc, 200, 120, 20);
                var stato = LeggiStato(doc);
                string uid = FacciaInCima(stato);
                if (uid == null) { Segna(nome, false, "nessuna faccia in cima nello stato"); return; }

                var argomenti = new Dictionary<string, object>
                {
                    ["faccia_uid"] = uid,
                    ["tipo"] = tipo,
                    ["taglia"] = taglia,
                    ["passante"] = passante,
                    ["posizioni"] = posizioni.Select(p => (object)new Dictionary<string, object>
                    {
                        ["x_mm"] = p[0], ["y_mm"] = p[1],
                    }).ToList(),
                };
                if (!passante) argomenti["profondita_mm"] = profondita;

                string errore;
                var misure = Esegui(doc, "foro_guidato", argomenti, out errore, stato);
                if (deveFallire)
                {
                    Segna(nome, misure == null && !string.IsNullOrEmpty(errore),
                          misure == null ? ("rifiutato: " + Corto(errore)) : "NON ha rifiutato");
                    return;
                }
                if (misure == null) { Segna(nome, false, "fallito: " + Corto(errore)); return; }

                double d = misure.ContainsKey("diametro_lavorato_mm")
                    ? Convert.ToDouble(misure["diametro_lavorato_mm"]) : 0;
                // LA CHIAVE C'ERA SEMPRE, e questo ramo non poteva scattare.
                // Fino alla 0.25.1 qui c'era `misure.ContainsKey(...)`, e il
                // ponte scriveva quella chiave senza condizione: `verificate`
                // era vero per costruzione, «POSIZIONI NON VERIFICATE» era
                // codice morto, e queste prove misuravano solo il diametro
                // credendo di misurare anche le posizioni.
                // Adesso la chiave va letta E deve essere vera, e il ponte ne
                // scrive una DIVERSA quando non ha potuto guardare.
                bool verificate = misure.ContainsKey("posizioni_verificate") &&
                                  Convert.ToBoolean(misure["posizioni_verificate"]);
                string perche = misure.ContainsKey("posizioni_non_verificate")
                    ? Convert.ToString(misure["posizioni_non_verificate"]) : null;
                bool ok = verificate && Math.Abs(d - diametroAtteso) < 0.05;
                Segna(nome, ok, string.Format("diametro {0:0.##} (atteso {1:0.##}), fori {2}, {3}",
                    d, diametroAtteso,
                    misure.ContainsKey("fori") ? misure["fori"] : "?",
                    verificate
                        ? "posizioni verificate"
                        : "POSIZIONI NON VERIFICATE" + (perche == null ? "" : ": " + Corto(perche))));
            }
            catch (Exception ex) { Segna(nome, false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        // ==================================================================
        // 5. il materiale, passando dall'add-in
        // ==================================================================
        private static void Materiale()
        {
            Console.WriteLine();
            Console.WriteLine("=== applica_materiale (add-in) ===");
            foreach (var caso in new[]
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
                    doc = NuovaParte();
                    Piastra(doc, 100, 60, 10);
                    string errore;
                    var misure = Esegui(doc, "applica_materiale",
                        new Dictionary<string, object> { ["materiale"] = caso[0] }, out errore);
                    bool deveFallire = (bool)caso[2];
                    if (deveFallire)
                    {
                        Segna("materiale «" + caso[0] + "»",
                              misure == null && !string.IsNullOrEmpty(errore),
                              misure == null ? ("rifiutato: " + Corto(errore)) : "NON ha rifiutato");
                        continue;
                    }
                    if (misure == null) { Segna("materiale «" + caso[0] + "»", false, Corto(errore)); continue; }

                    // e si rilegge dal documento, non dall'esito
                    string db;
                    string letto = (doc as IPartDoc).GetMaterialPropertyName2("", out db);
                    bool ok = letto == (string)caso[1];
                    // e adesso c'e' anche la massa?
                    var stato = LeggiStato(doc);
                    bool haMassa = stato.ContainsKey("massa_kg");
                    Segna("materiale «" + caso[0] + "»", ok && haMassa,
                          "sul documento: «" + letto + "»" +
                          (haMassa ? ", massa " + stato["massa_kg"] + " kg" : ", MASSA ASSENTE"));
                }
                catch (Exception ex) { Segna("materiale «" + caso[0] + "»", false, Perche(ex)); }
                finally { Chiudi(doc); }
            }
        }

        // ==================================================================
        // 6. lo stato del documento
        // ==================================================================
        private static void Stato()
        {
            Console.WriteLine();
            Console.WriteLine("=== StatoDocumento ===");
            IModelDoc2 doc = null;
            try
            {
                doc = NuovaParte();
                var stato = LeggiStato(doc);
                Segna("documento vuoto: materiale null esplicito",
                      stato.ContainsKey("materiale") && stato["materiale"] == null);
                Segna("documento vuoto: nessun corpo",
                      stato.ContainsKey("corpo_solido") && !Convert.ToBoolean(stato["corpo_solido"]));
                Segna("documento vuoto: nessuna massa (manca il materiale)",
                      !stato.ContainsKey("massa_kg"));

                // un telaio vero
                var argomenti = new Dictionary<string, object>
                {
                    ["profilo_percorso"] = QUADRO,
                    ["configurazione"] = "80X80X5",
                    ["lunghezza_mm"] = 1000.0,
                    ["larghezza_mm"] = 800.0,
                    ["piano"] = "frontale",
                    ["centrato"] = true,
                    ["trattamento_angolo"] = "quartabuono",
                    ["segmenti"] = SegmentiNominali(1000, 800, "frontale", true),
                    ["telaio"] = new Dictionary<string, object>
                    {
                        ["lunghezza_mm"] = 1000.0, ["larghezza_mm"] = 800.0,
                        ["piano"] = "frontale", ["centrato"] = true, ["misure"] = "ingombro",
                    },
                };
                string errore;
                var misure = Esegui(doc, "telaio_rettangolare", argomenti, out errore);
                if (misure == null) { Segna("telaio per lo stato", false, Corto(errore)); return; }

                stato = LeggiStato(doc);
                var corpi = stato.ContainsKey("corpi") ? stato["corpi"] as System.Collections.IList : null;
                Segna("telaio: quattro corpi nello stato", corpi != null && corpi.Count == 4,
                      corpi == null ? "assenti" : corpi.Count + " corpi");
                if (corpi != null && corpi.Count > 0)
                {
                    var c0 = corpi[0] as Dictionary<string, object>;
                    Segna("corpo: ha uid, ingombro, centro e chi lo ha fatto",
                          c0.ContainsKey("uid") && c0.ContainsKey("ingombro_mm") &&
                          c0.ContainsKey("centro_mm") && c0.ContainsKey("fatto_da"),
                          Riga(c0));
                }
                var ing = stato.ContainsKey("ingombro_mm") ? stato["ingombro_mm"] as double[] : null;
                Segna("pezzo: ingombro 1000x800x80",
                      ing != null && Math.Abs(ing[0] - 1000) < 0.5 &&
                      Math.Abs(ing[1] - 800) < 0.5 && Math.Abs(ing[2] - 80) < 0.5,
                      ing == null ? "assente" : string.Join("x", ing.Select(x => x.ToString("0.##"))));
                Segna("pezzo: nessuna massa senza materiale", !stato.ContainsKey("massa_kg"));

                var feature = stato["feature"] as System.Collections.IList;
                var nomi = feature.Cast<Dictionary<string, object>>()
                    .Select(f => Convert.ToString(f["tipo"])).ToList();
                Segna("feature: solo quelle che contano",
                      nomi.Contains("WeldMemberFeat") &&
                      !nomi.Contains("HistoryFolder") && !nomi.Contains("SolidBodyFolder"),
                      string.Join(", ", nomi));

                int nFacce = (stato["facce"] as System.Collections.IList).Count;
                Segna("facce: tetto sul totale, non per corpo", nFacce <= 60,
                      nFacce + " facce" +
                      (stato.ContainsKey("facce_totali") ? " su " + stato["facce_totali"] : ""));
                var f0 = (stato["facce"] as System.Collections.IList)[0] as Dictionary<string, object>;
                Segna("faccia: dice a quale corpo appartiene", f0.ContainsKey("corpo"), Riga(f0));

                // IL TETTO E' NOSTRO, NON DI SOLIDWORKS NE' DEL MODELLO.
                //
                // Non esiste nessun limite di dimensione nel prodotto: nel
                // codice ci sono solo tetti sul NUMERO di elementi (60 facce,
                // 60 spigoli, 80 lavorazioni, 24 corpi). Questa riga e' una
                // guardia che abbiamo scritto noi, nata quando un difetto di
                // troncamento faceva crescere il messaggio con le barre — 11 KB
                // per quattro, oltre 50 per venti.
                //
                // Il 10 settembre 2026 e' stata alzata da 12.000 a 14.000, e
                // qui c'e' scritto cosa ha comprato quell'aumento: i parametri
                // veri delle lavorazioni (norma, taglia, diametro lavorato,
                // filetto, profondita', condizione di fine), il diametro delle
                // facce cilindriche, il volume del pezzo, e le lavorazioni
                // soppresse. Cioe' la differenza fra un modello che legge
                // «Foro filettato M8-1» e uno che legge «M8 ISO, preforo 6,8».
                //
                // Prima di alzarla ancora: guardare se c'e' un campo che
                // RIPETE un altro campo. Tre ne sono stati trovati in
                // mezz'ora, per seimila caratteri in tutto.
                int caratteri = System.Text.Encoding.UTF8.GetByteCount(Json(stato));
                // 14 -> 16 KB il 10 settembre 2026, e cosa ha comprato: il
                // RUOLO degli spigoli. Prima venti righe con una lunghezza e
                // basta; adesso ognuno dice lungo quale asse va e fra quali
                // due facce sta, cioe' come si dice «i quattro angoli» o «il
                // bordo della faccia in alto» senza contare i millimetri.
                // Costo misurato: +1.425 caratteri su un telaio da quattro
                // barre, +737 su una piastra.
                //
                // TERZA VOLTA CHE QUESTA RIGA SALE. Se serve una quarta, la
                // domanda giusta non e' «quanto alzo» ma «servono davvero
                // sessanta spigoli?»: su un saldato sono rumore, e il tetto
                // sul NUMERO e' l'unico che non si puo' alzare all'infinito.
                // QUESTA PROVA MISURA ANCHE CIO' CHE NON DOVREBBE, e va detto
                // nel messaggio invece che scoperto ogni volta.
                //
                // Lo stato elenca `documenti_aperti`, quindi la misura dipende
                // da cosa c'e' aperto in SOLIDWORKS oltre al telaio. Misurato
                // il 14/9/2026: sei parti salvate da un giro di `corri_piani.js`
                // rimaste aperte valgono ~650 caratteri, e portano la prova da
                // 15.628 a 16.280 — cioe' la fanno FALLIRE senza che il codice
                // sia cambiato. Mezz'ora spesa a cercare una regressione che
                // non c'era.
                //
                // `ChiudiScarti --chiudi` da solo non le chiude: risparmia
                // apposta i documenti con un file. Ci vuole la cartella:
                //   ChiudiScarti.exe --chiudi "...\Banco\lavoro"
                //
                // Il numero si stampa SEMPRE, non solo quando fallisce: un
                // messaggio che dice il criterio serve a leggere anche i verdi.
                int aperti = 0;
                try
                {
                    var elenco = stato.ContainsKey("documenti_aperti")
                        ? stato["documenti_aperti"] as System.Collections.IList : null;
                    if (elenco != null) aperti = elenco.Count;
                }
                catch { }

                Segna("stato: sotto i 16 KB su un telaio da quattro barre", caratteri < 16000,
                      caratteri + " caratteri, " + aperti +
                      (aperti == 1 ? " documento aperto" : " documenti aperti") +
                      (aperti > 1 ? " (piu' di uno gonfia la misura: vedi qui sopra)" : ""));
            }
            catch (Exception ex) { Segna("stato del documento", false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        // ==================================================================
        //  Il tetto sulle lavorazioni tiene le ULTIME (voce E12)
        // ==================================================================
        //
        // PERCHE' QUI E NON IN UN CASO DEL PONTE. E' un'asserzione sulla FORMA
        // dello stato, come quella dei 16 KB qui sopra, e il posto giusto e'
        // dove lo stato si legge senza nient'altro in mezzo.
        //
        // Ed e' anche l'unico posto dove costa poco. Provato il 14/9/2026 a
        // passare dal ponte: un piano con 85 `taglio_cilindrico` ci ha messo
        // 26 secondi a taglio e si e' fatto uccidere dal tetto dei trenta
        // minuti al quarantaseiesimo passo. Spezzarlo in cinque piani non
        // funziona — `faccia_rif: {passo: 1}` e' relativo AL PIANO, quindi nel
        // secondo blocco «passo 1» e' il primo taglio del blocco e non la
        // barra, e il ponte l'ha rifiutato (giustamente e a voce alta).
        //
        // Qui si chiama la libreria direttamente: niente JSON-RPC, niente
        // macchina dei piani, e la faccia si ritrova dallo stato a ogni giro.
        // ==================================================================
        // V1. la fotografia del pezzo — l'unica cosa che dice una FORMA
        // ==================================================================
        /// <summary>
        /// Prova `VistaDelPezzo.Scatta`.
        ///
        /// PERCHE' QUESTA PROVA ESISTE, E PERCHE' PROVA QUESTE QUATTRO COSE.
        /// La capacita' di fotografare c'era gia' — il banco salvava BMP con
        /// `SaveBMP` da mesi — e non e' mai arrivata a chi legge. Quando una
        /// capacita' scende dal banco al ponte, le cose che si rompono non
        /// sono quelle che il banco provava: sono quelle che il banco non
        /// doveva rispettare perche' nessuno lo guardava mentre lavorava.
        ///
        ///   1. **esce qualcosa** — il minimo;
        ///   2. **e' un PNG** — perche' lo stesso fotogramma in BMP e' una
        ///      cinquantina di volte piu' pesante, e la conversione e' un
        ///      passaggio che puo' fallire in silenzio restituendo il BMP;
        ///   3. **l'orientamento di chi lavora torna com'era** — e' la sola
        ///      regola di questo file che riguarda una persona invece di un
        ///      dato: un'operazione di sola lettura che lascia il pezzo
        ///      girato ha fatto un danno vero;
        ///   4. **una vista inventata viene rifiutata dicendo quali ci sono**
        ///      — il caso ROSSO. Senza, «scatta sempre qualcosa» passerebbe
        ///      anche se il nome non venisse mai guardato.
        ///
        /// E in coda: **niente file lasciati nella temporanea.** `SaveBMP`
        /// vuole un percorso, quindi ogni scatto scrive 2 MB su disco; se il
        /// `Bitmap` tiene aperto il file la cancellazione fallisce e nessuno
        /// se ne accorge finche' la cartella non e' piena.
        /// </summary>
        private static void ProvaFotografia()
        {
            Console.WriteLine();
            Console.WriteLine("=== VistaDelPezzo (V1) ===");

            var t = _addin.GetType(NS + "VistaDelPezzo", false);
            if (t == null)
            {
                Segna("fotografia: la classe c'e'", false,
                      "VistaDelPezzo non e' nella libreria caricata");
                return;
            }
            var scatta = t.GetMethod("Scatta", BindingFlags.Public | BindingFlags.Static);
            if (scatta == null)
            {
                Segna("fotografia: Scatta c'e'", false, "il metodo non esiste");
                return;
            }

            var doc = NuovaParte();
            if (doc == null) { Segna("fotografia: parte nuova", false, "non creata"); return; }

            string modello = "oce-vista-";
            int temporaneiPrima = FileTemporanei(modello);

            try
            {
                string errore;
                var misure = Esegui(doc, "estrudi_rettangolo", new Dictionary<string, object>
                {
                    ["piano"] = "superiore",
                    ["lunghezza_mm"] = 200.0,
                    ["larghezza_mm"] = 100.0,
                    ["spessore_mm"] = 20.0,
                    ["centrato"] = true,
                }, out errore);
                if (misure == null) { Segna("fotografia: il blocco", false, Corto(errore)); return; }

                var vista = doc.ActiveView as IModelView;

                // ── LE NOVE STRADE STANNO IN `Controlli\ProvaVista` ──────
                //
                // Qui c'erano tre domande sul come si rimette a posto una
                // vista, e ognuna e' costata un giro di banco da diciassette
                // minuti per una risposta di tre secondi. Adesso stanno in un
                // attrezzo a se':
                //
                //     Controlli\ProvaVista\bin\ProvaVista.exe
                //
                // che prova nove strade e stampa quali tornano. E' li' che va
                // guardato se questa prova ridiventa rossa: dice anche **di
                // quanto** e **su quale numero** si sbaglia, ed e' quel numero
                // che ha trovato la risposta (l'ordine delle tre scritture).
                //
                // Qui resta l'affermazione che conta per chi usa il ponte: dopo
                // una fotografia, il pezzo sullo schermo di chi lavora sta
                // com'era.
                double[] prima = Orientamento(vista);

                var p = new object[] { doc, "isometrica", 800, 600, null };
                var png = scatta.Invoke(null, p) as byte[];
                string motivo = p[4] as string;

                Segna("fotografia: l'isometrica esce", png != null && png.Length > 0,
                      png == null ? Corto(motivo) : png.Length + " byte");

                // LA FIRMA DEL PNG: 89 50 4E 47. Un BMP comincia con «BM».
                bool eUnPng = png != null && png.Length > 8 &&
                              png[0] == 0x89 && png[1] == 0x50 &&
                              png[2] == 0x4E && png[3] == 0x47;
                Segna("fotografia: e' un PNG, non il BMP di SOLIDWORKS", eUnPng,
                      png == null || png.Length < 2
                          ? "niente da guardare"
                          : "primi byte " + png[0].ToString("X2") + " " + png[1].ToString("X2"));

                // 800x600 non compresso sarebbe 1,4 MB: se il PNG ci si
                // avvicina, la conversione non ha compresso niente.
                if (png != null)
                    Segna("fotografia: pesa molto meno del BMP", png.Length < 800 * 600,
                          png.Length + " byte contro " + (800 * 600 * 3) + " del BMP crudo");

                double[] dopo = Orientamento(vista);
                Segna("fotografia: l'orientamento di chi lavora e' tornato com'era",
                      StessoOrientamento(prima, dopo),
                      prima == null ? "non leggibile" : "16 numeri confrontati");

                // ── IL CASO ROSSO ────────────────────────────────────────
                var q = new object[] { doc, "assonometrica-della-nonna", 800, 600, null };
                var nulla = scatta.Invoke(null, q) as byte[];
                string perche = q[4] as string;
                Segna("fotografia: una vista inventata e' rifiutata, coi nomi veri",
                      nulla == null && perche != null &&
                      perche.IndexOf("isometrica", StringComparison.OrdinalIgnoreCase) >= 0,
                      nulla != null
                          ? "ha scattato lo stesso"
                          : Corto(perche));

                // ── NIENTE SPAZZATURA NELLA TEMPORANEA ───────────────────
                int temporaneiDopo = FileTemporanei(modello);
                Segna("fotografia: nessun file lasciato nella temporanea",
                      temporaneiDopo <= temporaneiPrima,
                      temporaneiPrima + " prima, " + temporaneiDopo + " dopo");
            }
            finally { Chiudi(doc); }
        }

        /// <summary>Quanti file temporanei di questo nome ci sono adesso.</summary>
        private static int FileTemporanei(string modello)
        {
            try { return Directory.GetFiles(Path.GetTempPath(), modello + "*").Length; }
            catch { return 0; }
        }

        /// <summary>
        /// I sedici numeri che descrivono come e' girato il pezzo sullo
        /// schermo. Null se non si e' potuto leggere — e allora il confronto
        /// non afferma niente, invece di affermare che va tutto bene.
        /// </summary>
        private static double[] Orientamento(IModelView vista)
        {
            if (vista == null) return null;
            try { return Copia(vista.Transform as IMathTransform); }
            catch { return null; }
        }

        /// <summary>
        /// I sedici numeri di una trasformazione, COME ARRAY e non come
        /// riferimento: `ArrayData` costruisce un vettore nuovo a ogni lettura,
        /// ed e' quello che rende il confronto possibile.
        /// </summary>
        private static double[] Copia(IMathTransform m)
        {
            if (m == null) return null;
            try { return m.ArrayData as double[]; }
            catch { return null; }
        }

        private static bool StessoOrientamento(double[] a, double[] b)
        {
            // Se non si e' potuto leggere nemmeno prima, questa prova non ha
            // un ingresso: NON e' verde, e' senza risposta. La si dichiara
            // fallita perche' un controllo che si arrende dicendo «ok» e' il
            // difetto che questo progetto esiste per rendere impossibile.
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > 1e-9) return false;
            return true;
        }

        private static void ProvaTettoLavorazioni()
        {
            Console.WriteLine();
            Console.WriteLine("=== il tetto sulle lavorazioni (E12) ===");

            const int Quanti = 85;      // il tetto e' 80: cinque devono restare fuori
            const int Tetto = 80;
            var doc = NuovaParte();
            if (doc == null) { Segna("tetto: parte nuova", false, "non creata"); return; }

            try
            {
                string errore;
                var misure = Esegui(doc, "estrudi_rettangolo", new Dictionary<string, object>
                {
                    ["piano"] = "superiore",
                    ["lunghezza_mm"] = (Quanti - 1) * 10.0 + 30,
                    ["larghezza_mm"] = 30.0,
                    ["spessore_mm"] = 8.0,
                    ["centrato"] = true,
                }, out errore);
                if (misure == null) { Segna("tetto: la barra", false, Corto(errore)); return; }

                // I tagli, uno per giro. La faccia in alto si ritrova dallo
                // stato ogni volta: e' cio' che farebbe chi disegna, e non
                // serve nessun riferimento a un passo.
                double x0 = -((Quanti - 1) * 10.0) / 2;
                int fatti = 0;
                for (int i = 0; i < Quanti; i++)
                {
                    var stato = LeggiStato(doc);
                    string uidAlto = FacciaInAlto(stato);
                    if (uidAlto == null) break;

                    var m = Esegui(doc, "taglio_cilindrico", new Dictionary<string, object>
                    {
                        ["faccia_uid"] = uidAlto,
                        ["x_mm"] = x0 + i * 10.0,
                        ["y_mm"] = 0.0,
                        ["diametro_mm"] = 4.0,
                        ["passante"] = true,
                    }, out errore, stato);
                    if (m == null) break;
                    fatti++;
                }
                Segna("tetto: " + Quanti + " lavorazioni costruite", fatti == Quanti,
                      fatti + " su " + Quanti + (fatti < Quanti ? " -- " + Corto(errore) : ""));
                if (fatti < Quanti) return;

                var finale = LeggiStato(doc);
                var elenco = finale.ContainsKey("feature")
                    ? finale["feature"] as System.Collections.IList : null;
                int inElenco = elenco == null ? 0 : elenco.Count;

                // 1. IL TETTO SI DICHIARA.
                int totali = finale.ContainsKey("feature_totali")
                    ? Convert.ToInt32(finale["feature_totali"]) : 0;
                int fuori = finale.ContainsKey("feature_non_elencate")
                    ? Convert.ToInt32(finale["feature_non_elencate"]) : 0;
                bool troncato = finale.ContainsKey("troncato") &&
                                Convert.ToBoolean(finale["troncato"]);
                Segna("tetto: dichiarato",
                      totali == Quanti + 1 && fuori == Quanti + 1 - Tetto && troncato,
                      "elencate " + inElenco + ", totali " + totali + ", fuori " + fuori +
                      ", troncato " + troncato);

                // 2. E QUELLA CHE CONTA: L'ULTIMA C'E'.
                //
                // E' il difetto vero di E12, e non si vede da nessun conteggio:
                // l'albero si scorre in ordine di CREAZIONE, quindi tenere le
                // prime ottanta vuol dire buttare via proprio la lavorazione
                // che il piano ha appena fatto -- l'unica che chi legge sta
                // cercando. Il modello non la trovava e poteva concluderne che
                // l'operazione non aveva fatto niente, e rifarla.
                string ultima = null, prima = null;
                if (elenco != null && elenco.Count > 0)
                {
                    var u = elenco[elenco.Count - 1] as Dictionary<string, object>;
                    var p = elenco[0] as Dictionary<string, object>;
                    if (u != null && u.ContainsKey("uid")) ultima = Convert.ToString(u["uid"]);
                    if (p != null && p.ContainsKey("uid")) prima = Convert.ToString(p["uid"]);
                }
                Segna("tetto: nell'elenco c'e' l'ULTIMA creata, non la prima",
                      ultima != null && ultima.EndsWith(Quanti.ToString()),
                      "prima elencata: " + (prima ?? "?") + ", ultima: " + (ultima ?? "?"));

                // 3. E L'ESTRUSIONE, che e' la PIU' VECCHIA, non c'e' piu'.
                bool cSonoLeVecchie = false;
                if (elenco != null)
                    foreach (var v in elenco)
                    {
                        var d = v as Dictionary<string, object>;
                        if (d != null && d.ContainsKey("uid") &&
                            Convert.ToString(d["uid"]).StartsWith("Estrusione"))
                        { cSonoLeVecchie = true; break; }
                    }
                Segna("tetto: la piu' vecchia e' stata scartata", !cSonoLeVecchie,
                      cSonoLeVecchie ? "l'estrusione c'e' ancora: si tengono le prime"
                                     : "l'estrusione non c'e' piu', come deve");
            }
            catch (Exception ex) { Segna("tetto sulle lavorazioni", false, Perche(ex)); }
            finally { Chiudi(doc); }
        }

        /// <summary>L'uid della faccia piana rivolta in alto, dallo stato.</summary>
        private static string FacciaInAlto(Dictionary<string, object> stato)
        {
            var facce = stato != null && stato.ContainsKey("facce")
                ? stato["facce"] as System.Collections.IList : null;
            if (facce == null) return null;
            foreach (var v in facce)
            {
                var f = v as Dictionary<string, object>;
                if (f == null) continue;
                if (f.ContainsKey("verso") && Convert.ToString(f["verso"]) == "alto" &&
                    f.ContainsKey("uid"))
                    return Convert.ToString(f["uid"]);
            }
            return null;
        }

        // ==================================================================
        // riflessione
        // ==================================================================
        private static Dictionary<string, object> Esegui(
            IModelDoc2 doc, string operazione, Dictionary<string, object> argomenti,
            out string errore, object stato = null)
        {
            errore = null;
            var costruttore = _tOperazioni.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];
            // QUELLO CHE L'ADD-IN RACCONTA SI LEGGE.
            // Un logger vuoto e' comodo finche' tutto va bene; il giorno in
            // cui un'operazione fa una scelta al posto tuo — ritrovare una
            // faccia che si e' spostata, per esempio — e' l'unica riga che
            // spiega perche' il pezzo e' venuto cosi'.
            var op = costruttore.Invoke(new object[]
                { _sw, (Action<string>)(s => Console.WriteLine("       | " + s)) });

            if (stato == null) stato = LeggiStatoOggetto(doc);
            var esegui = _tOperazioni.GetMethod("Esegui",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            object statoOggetto = stato is Dictionary<string, object> ? LeggiStatoOggetto(doc) : stato;

            var esito = esegui.Invoke(op, new object[]
            {
                doc, operazione, argomenti, statoOggetto, null,
            });
            bool riuscito = (bool)_tEsito.GetField("Riuscito").GetValue(esito);
            if (!riuscito)
            {
                errore = _tEsito.GetField("Errore").GetValue(esito) as string;
                return null;
            }
            try { doc.EditRebuild3(); } catch { }
            return _tEsito.GetField("Misure").GetValue(esito) as Dictionary<string, object>;
        }

        private static object LeggiStatoOggetto(IModelDoc2 doc)
        {
            var leggi = _tStato.GetMethod("Leggi",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            // L'APPLICAZIONE VA PASSATA. Da un documento non si risale a
            // SolidWorks — ne' IModelDoc2 ne' IModelDocExtension hanno un
            // accesso all'applicazione — e senza quello lo stato non sa
            // dire quali ALTRI documenti sono aperti. Con le tavole quel
            // dato smette di essere un di piu': una vista si aggancia al
            // percorso di un documento aperto, e se non lo si vede non lo
            // si puo' nominare.
            return leggi.Invoke(null, new object[] { doc, _sw });
        }

        private static Dictionary<string, object> LeggiStato(IModelDoc2 doc)
        {
            var stato = LeggiStatoOggetto(doc);
            var dati = _tStato.GetProperty("Dati",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return dati.GetValue(stato) as Dictionary<string, object>;
        }

        /// <summary>La faccia piana piu' grande: su una piastra e' una delle due grandi.</summary>
        private static string FacciaInCima(Dictionary<string, object> stato)
        {
            var facce = stato["facce"] as System.Collections.IList;
            foreach (Dictionary<string, object> f in facce)
            {
                var n = f["normale"] as double[];
                if (n == null) continue;
                bool assiale = Math.Abs(n[0]) > 0.99 || Math.Abs(n[1]) > 0.99 || Math.Abs(n[2]) > 0.99;
                if (assiale) return Convert.ToString(f["uid"]);
            }
            return null;
        }

        // ==================================================================
        // utilita'
        // ==================================================================
        private static List<object> SegmentiNominali(double L, double W, string piano, bool centrato)
        {
            Assi(piano, out int u, out int v);
            double u0 = centrato ? -L / 2 : 0, v0 = centrato ? -W / 2 : 0;
            double u1 = u0 + L, v1 = v0 + W;
            Func<double, double, double[]> p = (a, b) =>
            {
                var q = new double[3]; q[u] = a; q[v] = b; return q;
            };
            var angoli = new[] { p(u0, v0), p(u1, v0), p(u1, v1), p(u0, v1) };
            var fuori = new List<object>();
            for (int i = 0; i < 4; i++)
                fuori.Add(new Dictionary<string, object>
                {
                    ["da"] = angoli[i], ["a"] = angoli[(i + 1) % 4],
                });
            return fuori;
        }

        private static void Assi(string piano, out int u, out int v)
        {
            switch ((piano ?? "").ToLowerInvariant())
            {
                case "superiore": u = 0; v = 2; return;
                case "destro": u = 1; v = 2; return;
                default: u = 0; v = 1; return;
            }
        }

        private static double[] ScatolaDelDocumento(IModelDoc2 doc)
        {
            double[] tot = null;
            var corpi = (doc as IPartDoc).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            foreach (var c in corpi)
            {
                var b = (c as IBody2).GetBodyBox() as double[];
                if (b == null) continue;
                if (tot == null) { tot = b.Select(x => x * 1000).ToArray(); continue; }
                for (int i = 0; i < 3; i++) tot[i] = Math.Min(tot[i], b[i] * 1000);
                for (int i = 3; i < 6; i++) tot[i] = Math.Max(tot[i], b[i] * 1000);
            }
            return tot;
        }

        private static string Riga(Dictionary<string, object> d)
        {
            return string.Join(" ", d.Select(kv => kv.Key + "=" + Valore(kv.Value)));
        }

        private static string Valore(object v)
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
        /// Perche' una prova e' saltata per aria: messaggio, TIPO e la riga da
        /// cui e' partita.
        ///
        /// PERCHE' NON BASTA `ex.Message`. Il 15/9/2026 una prova ha stampato
        /// «Riferimento a un oggetto non impostato su un'istanza di oggetto.»
        /// e basta. Quella frase dice che qualcosa era null e non dice
        /// **cosa**, ne' se il null stesse nel banco o nella libreria: per
        /// localizzarla serviva un altro giro da diciassette minuti.
        ///
        /// Un banco che riporta un'eccezione senza dire da dove viene fa
        /// pagare a ogni indagine il prezzo di un giro intero.
        ///
        /// **Si scarta anche l'involucro della riflessione.** Il banco chiama
        /// la libreria con `Invoke`, quindi qualunque cosa esploda la' dentro
        /// arriva come `TargetInvocationException` — «Eccezione generata dalla
        /// destinazione di una chiamata», che non dice niente di niente.
        /// Quella che interessa e' sempre l'`InnerException`.
        /// </summary>
        private static string Perche(Exception ex)
        {
            if (ex == null) return "(nessuna eccezione)";
            while (ex is TargetInvocationException && ex.InnerException != null)
                ex = ex.InnerException;

            string dove = "";
            try
            {
                var righe = (ex.StackTrace ?? "").Split('\n');
                if (righe.Length > 0) dove = "  <- " + righe[0].Trim();
            }
            catch { }
            return ex.GetType().Name + ": " + Corto(ex.Message) + dove;
        }

        private static string Corto(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(nessun motivo)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 110 ? s.Substring(0, 110) + "..." : s;
        }

        private static IModelDoc2 NuovaParte()
        {
            string tpl = _sw.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            return _sw.NewDocument(tpl, 0, 0, 0) as IModelDoc2;
        }

        private static void Chiudi(IModelDoc2 doc)
        {
            if (doc == null) return;
            try { _sw.CloseDoc(doc.GetTitle()); } catch { }
        }

        private static void Piastra(IModelDoc2 doc, double L, double W, double H)
        {
            var piani = (doc.FeatureManager.GetFeatures(false) as object[])
                .Select(o => o as IFeature)
                .Where(f => { try { return f.GetTypeName2() == "RefPlane"; } catch { return false; } })
                .ToList();
            doc.ClearSelection2(true);
            piani[0].Select2(false, 0);          // frontale: la faccia in cima avra' normale +Z
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
