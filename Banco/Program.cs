using System;
using System.IO;
using System.Collections.Generic;
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

            Sezioni();
            Materiali();
            Telai();
            Fori();
            Materiale();
            Stato();
            NuoveOperazioni();

            Console.WriteLine();
            Console.WriteLine(new string('=', 76));
            foreach (var r in _esiti) Console.WriteLine(r);
            Console.WriteLine();
            Console.WriteLine(_esiti.Count + " prove, " + _falliti + " fallite");
            return _falliti == 0 ? 0 : 1;
        }

        private static void Segna(string nome, bool ok, string dettaglio = "")
        {
            if (!ok) _falliti++;
            _esiti.Add((ok ? "  OK   " : "  NO   ") + nome.PadRight(52) + dettaglio);
            Console.WriteLine((ok ? "  OK   " : "  NO   ") + nome.PadRight(52) + dettaglio);
        }

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
            catch (Exception ex) { Segna(nome, false, ex.Message); }
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
            catch (Exception ex) { Segna(nome, false, ex.Message); }
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
                catch (Exception ex) { Segna("materiale «" + caso[0] + "»", false, ex.Message); }
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
            catch (Exception ex) { Segna("stato del documento", false, ex.Message); }
            finally { Chiudi(doc); }
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
