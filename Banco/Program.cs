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
                // VOCE H10: una variabile sola, e il suo significato e' LA
                // RADICE. `SW_REDIST` resta letta dopo, per chi ce l'ha gia'.
                // Il banco e il ponte devono cercare NEGLI STESSI POSTI e
                // nello stesso ordine: due ricerche diverse dello stesso file
                // sono due programmi che un giorno usano due interop diverse.
                var radice = System.Environment.GetEnvironmentVariable("SW_INSTALL_PATH");
                if (!string.IsNullOrWhiteSpace(radice))
                    posti.Add(Path.Combine(radice.Trim(), "api", "redist"));
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
                Console.WriteLine("non trovo " + nome + ": indica dove e' installato " +
                                  "SOLIDWORKS nella variabile SW_INSTALL_PATH (la radice, " +
                                  "quella che finisce con \\SOLIDWORKS).");
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

            // `--muta`: non un giro con qualcosa in piu', UN ALTRO GIRO.
            // Perturba i casi e pretende che diventino rossi (voce F6). Costa
            // un documento di SOLIDWORKS per mutazione, quindi si lancia prima
            // di una consegna e non ogni sera.
            _muta = argomenti.Remove("--muta");

            // `--regressione`: la suite dei riferimenti (voce F8). Come
            // `--muta`, e' un altro giro: produce tre numeri, non prove verdi
            // e rosse, e mescolarli non direbbe niente a nessuno.
            _regressione = argomenti.Remove("--regressione");

            // `--gruppo <nomi>`: una famiglia di prove sola, mentre si lavora.
            //
            // ── PERCHE' ESISTE (voce F13) ─────────────────────────────────
            //
            // Un giro intero dura 13-17 minuti, e fino alla 0.40.0 non c'era
            // nessun modo di lanciarne un pezzo. Il 15/9/2026 il banco e' stato
            // lanciato una dozzina di volte — circa tre ore e mezza di
            // SOLIDWORKS — quasi tutte per riprovare cento cose non toccate.
            //
            // ── E PERCHE' IL GIRO INTERO RESTA IL PREDEFINITO ─────────────
            //
            // Perche' «provo solo quella che ho cambiato» NON funziona, e lo
            // stesso giorno l'ha dimostrato due volte: `centro_mm` sulle facce
            // dei fori ha fatto sforare il tetto dei 16 KB su un telaio da
            // quattro barre — che fori non ne ha, ma ha i raccordi d'angolo dei
            // profili — e il ritaglio delle immagini ha cambiato un'asserzione
            // sul peso del PNG. **Il banco misura cosa hai rotto, non cosa hai
            // fatto**: quello lo sai gia'.
            //
            // Quindi: `--gruppo` per sbagliare in fretta, il giro intero prima
            // di committare, e un giro parziale che NON puo' passare per uno
            // intero — il riepilogo lo scrive a chiare lettere.
            List<string> scelti = null;
            int doveG = argomenti.IndexOf("--gruppo");
            if (doveG >= 0)
            {
                if (doveG + 1 >= argomenti.Count)
                {
                    Console.WriteLine("«--gruppo» vuole un nome. " + ComeSiChiamano());
                    return 2;
                }
                scelti = new List<string>(
                    argomenti[doveG + 1].Split(new[] { ',' },
                                               StringSplitOptions.RemoveEmptyEntries));
                for (int i = 0; i < scelti.Count; i++) scelti[i] = scelti[i].Trim().ToLowerInvariant();
                argomenti.RemoveRange(doveG, 2);
            }
            if (argomenti.Remove("--gruppi"))
            {
                Console.WriteLine(ComeSiChiamano());
                return 0;
            }

            argomentiRiga = argomenti.ToArray();

            // UN NOME SBAGLIATO SI DICE SUBITO, prima di agganciare SOLIDWORKS.
            // Un refuso che si scopre dopo trenta secondi di avvio, e che lascia
            // credere «quel gruppo non ha prove», e' peggio di nessun gruppo.
            if (scelti != null)
            {
                var conosciuti = new HashSet<string>(
                    Gruppi().Select(g => g.Nome), StringComparer.OrdinalIgnoreCase);
                var sconosciuti = scelti.Where(n => !conosciuti.Contains(n)).ToList();
                if (sconosciuti.Count > 0)
                {
                    Console.WriteLine("non conosco " +
                                      string.Join(", ", sconosciuti.Select(n => "«" + n + "»")) +
                                      ". " + ComeSiChiamano());
                    return 2;
                }
            }

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
            // ── LA LIBRERIA NON SI CARICA NON E' UN GIRO ROSSO (voce F10) ─
            //
            // Puntando `--libreria` a una versione storica — quel che serve per
            // dimostrare che una prova custodisce davvero il suo difetto — i
            // tipi possono non esserci: prima della 0.52.0 le operazioni
            // stavano in `OpenPDM.SwAddin`, con un altro spazio dei nomi.
            //
            // Senza questa rete il banco usciva con un'eccezione non gestita e
            // il codice -532462766, che in un log e' indistinguibile da un
            // piantamento. Un confronto storico che da' «rosso dappertutto» non
            // significa niente, e questo ne e' il caso limite.
            try
            {
                _tOperazioni = _addin.GetType(NS + "OperazioniSw", true);
                _tStato = _addin.GetType(NS + "StatoDocumento", true);
                _tEsito = _addin.GetType(NS + "OperazioniSw+Esito", true);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("LA LIBRERIA NON ESPONE I TIPI CHE IL BANCO CERCA.");
                Console.WriteLine("  libreria: " + _addin.GetName().Version + "  (" + _libreria + ")");
                Console.WriteLine("  cercavo:  " + NS + "OperazioniSw");
                Console.WriteLine("  " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine();
                Console.WriteLine("Non e' un giro rosso: e' una libreria che il banco non sa leggere.");
                Console.WriteLine("Prima della 0.52.0 le operazioni stavano in «OpenPDM.SwAddin», " +
                                  "con un altro spazio dei nomi: il confronto storico comincia da li'.");
                return 2;
            }

            // QUANTE OPERAZIONI HA IL CATALOGO, detto qui e non altrove.
            // Il resoconto (voce F2) lo vuole in intestazione, e questo e'
            // l'unico punto in cui la libreria da misurare e' gia' in mano:
            // farlo leggere allo script vorrebbe dire contarle da un file
            // costruito da un'altra compilazione, cioe' da un'altra libreria.
            try
            {
                var nomi = _tOperazioni.GetField("NomiEseguibili",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string[];
                if (nomi != null) Console.WriteLine("catalogo: " + nomi.Length + " operazioni");
            }
            catch { }

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

            // ── LA SOLIDWORKS DEV'ESSERE PULITA PRIMA, NON DOPO ──────────
            if (!LaScrivaniaEPulita(argomentiRiga)) return 3;

            // Il diario si apre QUI e non prima: l'aggancio a SOLIDWORKS e il
            // caricamento della libreria possono far uscire dal programma, e un
            // diario aperto per un giro che non e' mai cominciato direbbe
            // «piantato al primo passo» quando invece non e' partito affatto.
            ApriIlDiario();
            Diario("SolidWorks " + _sw.RevisionNumber() +
                   ", libreria " + _addin.GetName().Version +
                   (_lento ? ", con --lento" : "") +
                   (scelti == null ? "" : ", SOLO i gruppi " + string.Join(", ", scelti)));

            // ── `--muta` E' UN GIRO A SE' ────────────────────────────────
            //
            // Non si aggiunge alle prove: le sostituisce. Mescolarlo vorrebbe
            // dire sommare centinaia di rossi CERCATI a un conteggio che serve
            // a leggere i rossi non cercati.
            if (_muta || _regressione)
            {
                if (_muta) IlGiroDelleMutazioni();
                else IlGiroDellaRegressione();
                Console.WriteLine();
                Console.WriteLine(new string('=', 76));
                foreach (var r in _esiti) Console.WriteLine(r);
                Console.WriteLine();
                var soloMuta = _muta
                    ? "MUTAZIONI — " + (_esiti.Count - _saltate - _erroriDiBanco) +
                      " casi, " + _falliti + " ciechi" +
                      (_erroriDiBanco > 0 ? ", " + _erroriDiBanco + " ERRORI DI BANCO" : "")
                    : "REGRESSIONE — " + (_esiti.Count - _saltate - _erroriDiBanco) +
                      " righe, " + _falliti + " fallite" +
                      (_erroriDiBanco > 0 ? ", " + _erroriDiBanco + " ERRORI DI BANCO" : "");
                Console.WriteLine(soloMuta);
                Console.WriteLine("attese verificate " + _atteseVerificate + " su " + _attese);
                ChiudiIlDiario(soloMuta);
                if (_erroriDiBanco > 0) return 2;
                return _falliti == 0 ? 0 : 1;
            }

            var nonLanciati = new List<string>();
            foreach (var gruppo in Gruppi())
            {
                if (scelti != null &&
                    !scelti.Contains(gruppo.Nome, StringComparer.OrdinalIgnoreCase))
                {
                    nonLanciati.Add(gruppo.Nome);
                    continue;
                }
                if (gruppo.SoloLento && !_lento)
                {
                    Saltata(gruppo.Cosa, gruppo.PerchePesa ?? "si lancia con --lento");
                    continue;
                }
                // DA QUI IN POI LE PROVE SONO DI QUESTO GRUPPO (voce K2).
                // Si alza qui e non dentro `Corri()` perche' i gruppi sono
                // diciotto metodi diversi e chiedere a ognuno di ricordarsene
                // vuol dire che prima o poi uno se ne dimentica — e un gruppo
                // che non si dichiara finisce in «(fuori gruppo)», che e'
                // visibile, ma dopo.
                _gruppoInCorso = gruppo.Nome;
                try { gruppo.Corri(); }
                finally { _gruppoInCorso = null; }
            }

            Console.WriteLine();
            Console.WriteLine(new string('=', 76));
            foreach (var r in _esiti) Console.WriteLine(r);
            Console.WriteLine();

            // I GRUPPI NON LANCIATI SI DICONO PER NOME, come le saltate.
            //
            // «8 prove, 0 fallite» su un giro parziale e' vero e fa credere una
            // cosa falsa, esattamente come lo era «88 prove, 0 fallite» con una
            // prova non lanciata. La difesa sta nella riga di riepilogo, non
            // nella disciplina di chi lancia: alla sera tardi nessuno si ricorda
            // di essersi limitato a un gruppo.
            if (nonLanciati.Count > 0)
            {
                Console.WriteLine("NON LANCIATI — " + nonLanciati.Count + " gruppi su " +
                                  Gruppi().Count + ": " + string.Join(", ", nonLanciati));
                Console.WriteLine("il giro intero si lancia senza «--gruppo», " +
                                  "ed e' quello che vale prima di committare.");
            }

            // LE SALTATE SI CONTANO A PARTE E SI DICONO. «88 prove, 0 fallite»
            // su un giro in cui una prova non e' stata lanciata e' una frase
            // vera che fa credere una cosa falsa.
            //
            // IL CONTO PER GRUPPO SI CONTROLLA PRIMA DEL RIEPILOGO, e non
            // dopo: quel controllo puo' alzare un rosso, e un rosso alzato
            // dopo che il riepilogo ha gia' contato i rossi non finisce nel
            // riepilogo ne' nel codice d'uscita. La prima stesura lo chiamava
            // in fondo, e sarebbe stato un controllo che non poteva far
            // fallire niente: la forma peggiore, perche' si legge come un
            // controllo.
            ControllaIlContoPerGruppo();

            var riepilogo =
                (nonLanciati.Count > 0
                    ? "GIRO PARZIALE (solo " + string.Join(", ", scelti) + ", " +
                      nonLanciati.Count + " gruppi non lanciati) — "
                    : "") +
                (_esiti.Count - _saltate - _erroriDiBanco) + " prove, " + _falliti + " fallite" +
                (_saltate > 0 ? ", " + _saltate + " SALTATE (--lento le lancia)" : "") +
                (_erroriDiBanco > 0 ? ", " + _erroriDiBanco + " ERRORI DI BANCO" : "");
            Console.WriteLine(riepilogo);

            // ATTESE VERIFICATE N SU M. Si scrive SEMPRE, anche quando i due
            // numeri combaciano: e' il numero che dice se la suite sta
            // misurando, e un numero che compare solo quando le cose vanno male
            // non lo guarda nessuno finche' non e' troppo tardi.
            var conto = "attese verificate " + _atteseVerificate + " su " + _attese;
            Console.WriteLine(conto);

            // ── E QUANTE PROVE NON HANNO NESSUNA ATTESA (voce K3) ────────
            //
            // Finche' i due numeri stavano in colonne diverse — 831 passate,
            // 659 attese verificate — la differenza era leggibile solo
            // sottraendo a mano, e nessuno la sottraeva. Scritta qui, con i
            // NOMI, diventa un elenco su cui si puo' decidere.
            //
            // NON E' UN ELENCO DI COLPEVOLI. Ci finiscono anche prove severe,
            // che confrontano nel codice invece che in un file di caso; e ci
            // finiscono le righe di servizio del banco, che non provano il
            // prodotto ma se stesse. Il punto e' poterle guardare una per una
            // invece di crederle tutte equivalenti a un'attesa misurata.
            Console.WriteLine("prove con un'attesa dichiarata: " + _conAttesa +
                              " — senza: " + _senzaAttesa.Count);

            // Voce K2-bis. Sta accanto al totale come il conto per gruppo,
            // perche' e' la stessa domanda: di quel numero grande, quanto
            // riguarda davvero SOLIDWORKS?
            Console.WriteLine("prove con un documento CAD aperto: " + _conDocumento +
                              " — senza: " + _senzaDocumento +
                              "   (tetto, non misura, finche' la K12-bis e' aperta)");
            ScriviLeSenzaAttesa();
            ScriviIlContoPerGruppo();

            ChiudiIlDiario(riepilogo + " · " + conto);

            // TRE USCITE, E IL 2 VINCE SULL'1. Se il banco non ha potuto
            // misurare, quante prove siano fallite e' una domanda a cui non si
            // e' risposto.
            if (_erroriDiBanco > 0) return 2;
            return _falliti == 0 ? 0 : 1;
        }

        // ══════════════════════════════════════════════════════════════════
        // I GRUPPI (voce F13)
        //
        // L'ORDINE E' QUELLO DI PRIMA, e non e' un dettaglio: alcune prove
        // lasciano il documento in uno stato che la successiva si aspetta, e
        // riordinarle per fare pulizia sarebbe il modo piu' silenzioso di
        // rompere il banco. Questa tabella e' l'elenco di prima, con un nome
        // davanti a ogni riga.
        // ══════════════════════════════════════════════════════════════════
        private sealed class Gruppo
        {
            public string Nome;      // come si scrive dopo --gruppo
            public string Cosa;      // come si chiama quando si dice che e' saltato
            public bool SoloLento;
            public string PerchePesa;   // cosa si dice quando --lento non c'e'

            /// <summary>
            /// LE PROVE DEL GRUPPO, UNA PER UNA e non una lambda che le
            /// chiama. Una lambda nasconderebbe i metodi veri dietro il suo, e
            /// `ProvaCheLaTabellaCopreTutto` — il controllo che nessuna
            /// famiglia resti fuori — non li vedrebbe piu': cercherebbe fra i
            /// metodi generati dal compilatore e direbbe di si' a tutto.
            /// </summary>
            public Action[] Azioni;

            public void Corri() { foreach (var a in Azioni) a(); }
        }

        private static List<Gruppo> Gruppi()
        {
            Func<Action, Action[]> una = a => new[] { a };
            return new List<Gruppo>
            {
                new Gruppo { Nome = "sezioni",   Cosa = "Services.SezioneProfilo",      Azioni = una(Sezioni) },
                new Gruppo { Nome = "archivio",  Cosa = "Services.MaterialiSolidWorks", Azioni = una(Materiali) },
                new Gruppo { Nome = "telai",     Cosa = "telaio_rettangolare",          Azioni = una(Telai) },
                new Gruppo { Nome = "fori",      Cosa = "foro_guidato",                 Azioni = una(Fori) },
                new Gruppo { Nome = "materiale", Cosa = "applica_materiale",            Azioni = una(Materiale) },
                new Gruppo { Nome = "stato",     Cosa = "StatoDocumento",               Azioni = una(Stato) },
                new Gruppo { Nome = "viste",     Cosa = "VistaDelPezzo (V1)",           Azioni = una(ProvaFotografia) },
                new Gruppo { Nome = "tetto",     Cosa = "il tetto sulle lavorazioni (E12)",
                             Azioni = una(ProvaTettoLavorazioni), SoloLento = true,
                             PerchePesa = "85 lavorazioni, circa mezz'ora: si lancia con --lento" },

                // Quelle che stavano dentro `NuoveOperazioni()`.
                new Gruppo { Nome = "scatto",       Cosa = "scatta la vista",        Azioni = una(ScattaLaVista) },
                new Gruppo { Nome = "quanto-vede",  Cosa = "quanto vede il modello", Azioni = una(QuantoVedeIlModello) },
                new Gruppo { Nome = "facce",        Cosa = "le facce della piastra", Azioni = una(StampaFaccePiastra) },
                new Gruppo { Nome = "sposta-corpi", Cosa = "sposta_corpi",           Azioni = una(ProvaSpostaCorpi) },
                // «asola» non c'e' piu': e' diventata quindici file in
                // `casi/asola/`, e si lancia con `--gruppo casi` (voce F11).
                // «smusso» non c'e' piu': e' diventato tre file in
                // `casi/smusso/`, e si lancia con `--gruppo casi` (voce F11).
                // «rivoluzione» non c'e' piu': e' diventata cinque file in
                // `casi/rivoluzione/`. La tolleranza e' la differenza: il C#
                // accettava `atteso * 0.02`, che sul cilindro da 62.831 mm³
                // sono 1.256 mm³ di scarto ammesso.
                // «guscio» non c'e' piu': e' diventato due file in
                // `casi/lavorazioni/`, e si lancia con `--gruppo casi`.
                // «crea-piano» non c'e' piu': e' diventato tre file in
                // `casi/piano/`, e l'attesa e' su DOVE E' FINITO il piano
                // (`ultimo_piano_mm`), non sulla distanza che il ponte
                // ricopia.
                // «crea-asse» e' rimasto in C# per UNA riga sola: che l'asse
                // si riagganci con SelectByID2("AXIS"), che e' una chiamata
                // COM e non un'operazione del catalogo. Tutto il resto della
                // famiglia sta in `casi/asse/`.
                new Gruppo { Nome = "crea-asse",    Cosa = "l'asse si ritrova per nome", Azioni = una(ProvaCreaAsse) },
                // «ripeti», «specchia» e «togliere» non ci sono piu': sono
                // diventati `casi/ripeti/`, `casi/specchia/` e
                // `casi/togliere/`, e si lanciano con `--gruppo casi`.
                // «forma» non c'e' piu': e' diventata `casi/frase/`, da quando
                // le attese sanno chiedere «contiene» e «non_contiene».
                new Gruppo { Nome = "dove-stava",   Cosa = "dove stava, e il grep vuoto (E10)",
                             Azioni = new Action[] { ProvaDoveStava, ProvaCheLaFrazioneNonDecide } },
                new Gruppo { Nome = "casi",         Cosa = "i casi dichiarati, che sono file (F3)",
                             Azioni = new Action[]
                             {
                                 ProvaCheIlSelettoreRifiuta,
                                 ProvaCasiDichiarati,
                                 // DOPO i casi: guarda anche il conto delle
                                 // attese, che prima di eseguirli e' zero.
                                 ProvaCheUnCampoAssenteNonEUnoZero,
                             } },
                new Gruppo { Nome = "il-null-ha-un-nome", Cosa = "il banco dice dove non e' riuscito (F12)",
                             Azioni = una(ProvaCheIlNullHaUnNome) },
                // La capacita' che apre tutta la sezione G-A: senza, nessuna
                // operazione di assieme potrebbe avere un caso.
                new Gruppo { Nome = "assieme-di-prova", Cosa = "il banco sa preparare un assieme (GA0)",
                             Azioni = una(ProvaLAssiemeDiProva) },
                new Gruppo { Nome = "tabella",      Cosa = "la tabella dei gruppi copre tutto (F13)",
                             Azioni = una(ProvaCheLaTabellaCopreTutto) },
            };
        }

        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// IL DIFETTO CHE I GRUPPI INTRODUCONO, e che non da' errore.
        ///
        /// Prima della voce F13 dimenticarsi di chiamare una prova nuova voleva
        /// dire non aggiungerla a `NuoveOperazioni()`. Adesso vuol dire non
        /// aggiungerla alla tabella dei gruppi — stessa dimenticanza, stesso
        /// silenzio: il banco continua a dire «tutto torna» su una prova in meno,
        /// e il conto totale non lo guarda nessuno da un giro all'altro.
        ///
        /// Qui si chiede a .NET quali metodi di prova esistono davvero e si
        /// confronta con la tabella. Il conto e' fatto sui `MethodInfo`, non sui
        /// nomi scritti a mano: un elenco scritto a mano avrebbe lo stesso
        /// difetto che sta controllando.
        ///
        /// ── E IL SUO ROSSO ───────────────────────────────────────────────
        ///
        /// Un setaccio che non trova niente e uno rotto si vedono uguali. Se la
        /// domanda alla riflessione tornasse vuota, «nessuna famiglia fuori
        /// dalla tabella» sarebbe vera per un motivo sbagliato: si controlla
        /// prima che le famiglie le veda.
        ///
        /// ── E IL ROSSO NON E' PIU' UN NUMERO ─────────────────────────────
        ///
        /// Era `famiglie.Count >= 15`, e il 15/9/2026 e' diventato rosso senza
        /// che niente si fosse rotto: la voce F11 ha trasformato sei famiglie
        /// di prove in file JSON, e i metodi sono scesi a dodici. **Un
        /// pavimento numerico su una cosa che deve calare e' un rosso a
        /// scadenza**: chi lo incontra lo alza o lo abbassa, e in tutti e due i
        /// casi ha guardato il numero invece del pezzo.
        ///
        /// Adesso il setaccio deve trovare SE STESSO. `ProvaCheLaTabellaCopreTutto`
        /// e' un metodo `private static void` senza parametri che comincia per
        /// «Prova»: se la riflessione non lo elenca, non funziona — e la cosa
        /// non dipende da quante famiglie ci siano oggi. Il conto resta
        /// scritto accanto come dato, non come soglia.
        /// </summary>
        private static void ProvaCheLaTabellaCopreTutto()
        {
            Console.WriteLine();
            Console.WriteLine("=== la tabella dei gruppi copre tutto (F13) ===");

            var famiglie = typeof(Program)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Where(m => m.Name.StartsWith("Prova", StringComparison.Ordinal) &&
                            m.ReturnType == typeof(void) &&
                            m.GetParameters().Length == 0)
                .ToList();

            // IL SETACCIO TROVA SE STESSO, o non funziona.
            bool miVede = famiglie.Any(m => m.Name == "ProvaCheLaTabellaCopreTutto");
            Segna("F13 ROSSO: il setaccio vede le famiglie di prove", miVede,
                  famiglie.Count + " metodi «Prova…()»" +
                  (miVede ? ", compreso questo" : ", e questo NON c'e': la riflessione non lo elenca"));

            var gruppi = Gruppi();
            var nella = new HashSet<MethodInfo>(gruppi.SelectMany(g => g.Azioni).Select(a => a.Method));
            var fuori = famiglie.Where(m => !nella.Contains(m)).Select(m => m.Name).ToList();
            Segna("F13: nessuna famiglia di prove fuori dalla tabella",
                  fuori.Count == 0,
                  fuori.Count == 0 ? gruppi.Count + " gruppi" : string.Join(", ", fuori));

            // DUE GRUPPI COLLO STESSO NOME: `--gruppo x` ne lancerebbe due e
            // il conto dei non lanciati direbbe una cosa e il giro un'altra.
            var doppi = gruppi.GroupBy(g => g.Nome, StringComparer.OrdinalIgnoreCase)
                              .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Segna("F13: i nomi dei gruppi sono tutti diversi",
                  doppi.Count == 0,
                  doppi.Count == 0 ? "" : string.Join(", ", doppi));
        }

        /// <summary>
        /// I nomi dei gruppi, per chi ne ha scritto uno sbagliato o non sa
        /// quali ci sono. Si costruisce dalla stessa tabella che li lancia: un
        /// elenco scritto a mano accanto invecchia al primo gruppo nuovo.
        /// </summary>
        private static string ComeSiChiamano()
        {
            return "I gruppi sono: " +
                   string.Join(", ", Gruppi().Select(g => g.Nome)) +
                   ". Senza «--gruppo» si lanciano tutti, ed e' quello che vale " +
                   "prima di committare.";
        }

        /// <summary>
        /// Il nome, incolonnato, **e sempre almeno uno spazio dopo**.
        ///
        /// `PadRight(52)` da solo non aggiunge niente quando il nome e' gia'
        /// lungo 52, e il diario usciva cosi':
        ///
        ///     OK   ...misure.copie_fattemisure.copie_fatte 3 (atteso 3)
        ///
        /// Due parole attaccate che sembrano una parola sola. Non e' un
        /// dettaglio estetico: il diario e' cio' che si legge quando il banco
        /// e' rosso, e una riga che si legge male si legge sbagliata — quel
        /// nome doppio sembra un difetto del banco, e si va a cercarlo.
        /// </summary>
        private static string InColonna(string nome)
        {
            if (nome == null) nome = "";
            return nome.Length >= 52 ? nome + "  " : nome.PadRight(52);
        }

        private static void Segna(string nome, bool ok, string dettaglio = "")
        {
            // ZITTO: il modo `--muta` riesegue lo stesso caso decine di volte e
            // PRETENDE che diventi rosso (voce F6). Quei rossi non sono
            // regressioni — sono il risultato che si cercava — e finire
            // nell'elenco degli esiti li farebbe leggere come tali.
            if (_zitto) { if (!ok) _zittoRosso = true; return; }

            if (!ok) _falliti++;

            // ── CON O SENZA UN'ATTESA DICHIARATA (voce K3) ───────────────
            //
            // `_attesaInCorso` lo alza chi ha appena MISURATO E CONFRONTATO
            // una grandezza dichiarata in un caso: il valutatore delle attese
            // e quello della regressione. Tutte le altre prove — quelle
            // scritte in C# — passano di qui con il segno abbassato.
            //
            // LA DIFFERENZA NON E' «BUONE E CATTIVE». Molte prove in C#
            // confrontano eccome, e alcune sono le piu' severe della suite.
            // Ma una parte non confronta niente: chiama, non esplode, e
            // finisce verde. *Una prova che passa senza asserzione non
            // dimostra un comportamento: dimostra che non e' esplosa*, e
            // finche' le due cose si sommano in un numero solo — 831 passate
            // contro 659 attese verificate — quel numero non dice quanto la
            // suite stia misurando.
            //
            // Qui si separano e si scrivono i NOMI di quelle senza. Poi si
            // decide, voce per voce, se chiuderle o dichiararle: e' una
            // decisione che si puo' prendere solo avendo l'elenco davanti.
            if (_attesaInCorso) _conAttesa++;
            else _senzaAttesa.Add(nome);

            // ── DI QUALE GRUPPO E' QUESTA PROVA (voce K2) ────────────────
            //
            // «831 prove su SOLIDWORKS vero» e' un numero che copre meno
            // terreno di quanto sembri, e contrapporlo a qualcuno mette nello
            // stesso conto prove che un documento CAD non lo aprono nemmeno.
            // Non e' una bugia: e' un numero grande che rassicura in
            // proporzione a quanto e' grande, non a quanto e' pertinente.
            //
            // Il conto per gruppo si scrive accanto al totale. Il totale
            // resta — serve a vedere se la suite cresce — ma smette di essere
            // l'unico numero che si legge.
            var chiave = _gruppoInCorso ?? "(fuori gruppo)";
            int quante;
            _perGruppo.TryGetValue(chiave, out quante);
            _perGruppo[chiave] = quante + 1;

            // ── E QUANTE HANNO UN DOCUMENTO CAD SOTTO (voce K2-bis) ──────
            //
            // Il conto per gruppo diceva DOVE stanno le prove; questo dice
            // quante di loro hanno SOLIDWORKS davvero in mezzo. Dentro il
            // gruppo `casi` — che da solo ne raccoglie la grande maggioranza —
            // stanno sia i casi dichiarati, che un documento lo aprono, sia i
            // controlli sul valutatore delle attese, che confrontano dizionari
            // e non aprono niente.
            //
            // SI CHIEDE A SOLIDWORKS, NON AL BANCO. La prima stesura contava
            // le aperture e le chiusure con un contatore interno, e sarebbe
            // stata sbagliata: i documenti si aprono in almeno quattro punti
            // (`NuovaParte`, `NuovoAssieme`, `Apri`, la sessione) e si chiudono
            // in tre, uno dei quali prova DUE VOLTE apposta. Un contatore con
            // sette guardiani e' un contatore che un giorno dira' -1.
            // `GetDocumentCount()` e' la stessa domanda fatta a chi la
            // risposta ce l'ha.
            //
            // COSA MISURA DAVVERO, detto qui e non scoperto dopo: «c'era un
            // documento aperto quando questa prova e' stata registrata». Da
            // quando il banco parte solo su una scrivania pulita (voce K12)
            // quel documento e' per forza uno che ha aperto lui. Ma se un caso
            // si dimentica di chiudere (voce K12-bis), le prove successive
            // risultano «con documento» anche se il CAD non l'hanno toccato.
            // **E' un limite, e va letto insieme alla K12-bis**: finche'
            // quella e' aperta, questo numero e' un tetto, non una misura.
            if (DocumentiAperti() > 0) _conDocumento++; else _senzaDocumento++;

            var riga = (ok ? "  OK   " : "  NO   ") + InColonna(nome) + dettaglio;
            _esiti.Add(riga);
            Console.WriteLine(riga);
            Diario(riga);
        }

        /// <summary>
        /// Alzato da chi sta registrando l'esito di un'attesa DICHIARATA, per
        /// il tempo di quella sola chiamata a `Segna`. Vedi la voce K3.
        /// </summary>
        internal static bool _attesaInCorso;

        internal static int _conAttesa;
        internal static readonly List<string> _senzaAttesa = new List<string>();

        /// <summary>Il gruppo che sta girando adesso (voce K2).</summary>
        internal static string _gruppoInCorso;

        /// <summary>Prove registrate con un documento CAD aperto (voce K2-bis).</summary>
        internal static int _conDocumento;
        internal static int _senzaDocumento;

        /// <summary>
        /// Quanti documenti ha aperti SOLIDWORKS adesso, o -1 se non si sa.
        ///
        /// Il -1 non e' zero travestito: «non ho potuto chiedere» e «non ce
        /// n'e'» sono due cose diverse, e confonderle qui vorrebbe dire
        /// contare come «senza documento» un giro in cui la domanda non e'
        /// passata. Chi legge il numero tratta il -1 come «non lo so».
        /// </summary>
        internal static int DocumentiAperti()
        {
            try { return _sw.GetDocumentCount(); }
            catch { return -1; }
        }

        /// <summary>
        /// Quante prove per gruppo. LA SOMMA DEVE FARE IL TOTALE, e se non lo
        /// fa il resoconto lo dice: un conto per gruppo che non torna col
        /// totale e' peggio di nessun conto per gruppo, perche' si legge come
        /// una ripartizione mentre e' una ripartizione incompleta.
        /// </summary>
        internal static readonly Dictionary<string, int> _perGruppo =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// Registra l'esito di un'attesa dichiarata: come `Segna`, ma conta
        /// nella colonna giusta. Sta qui e non nei chiamanti perche' un flag
        /// alzato a mano e dimenticato abbassato falsa tutto il conteggio da
        /// li' in avanti — e lo falserebbe in silenzio.
        /// </summary>
        internal static void SegnaAttesa(string nome, bool ok, string dettaglio = "")
        {
            _attesaInCorso = true;
            try { Segna(nome, ok, dettaglio); }
            finally { _attesaInCorso = false; }
        }

        /// <summary>
        /// Scrive in un file l'elenco delle prove senza attesa dichiarata, e a
        /// schermo le prime venti.
        ///
        /// IN UN FILE E NON SOLO A SCHERMO: sono centosettanta righe in fondo
        /// a un giro da ottocento, cioe' esattamente il posto in cui nessuno
        /// guarda. Il file invece si apre quando si ha voglia di decidere, e
        /// si puo' confrontare con quello del giro prima per vedere se
        /// l'elenco si sta accorciando.
        /// </summary>
        /// <summary>
        /// Il conto per gruppo, e il controllo che la somma faccia il totale.
        ///
        /// PERCHE' IL TOTALE DA SOLO DICE MENO DI QUANTO SEMBRI (voce K2).
        /// «831 prove su SOLIDWORKS vero» mette nello stesso numero prove che
        /// un documento CAD non lo aprono nemmeno — il protocollo MCP, il
        /// valutatore delle attese, i controlli sul banco stesso. Non e' una
        /// bugia: e' un numero che copre meno terreno di quanto sembri, ed e'
        /// la forma di difetto che questo progetto insegue.
        ///
        /// *Un numero grande rassicura in proporzione a quanto e' grande, non
        /// a quanto e' pertinente.*
        ///
        /// E SE LA SOMMA NON TORNA, SI DICE. Una ripartizione che non fa il
        /// totale si legge come una ripartizione e non lo e': manderebbe a
        /// credere che il resto non esista, invece di far cercare dove e'
        /// finito.
        /// </summary>
        private static void ControllaIlContoPerGruppo()
        {
            if (_perGruppo.Count == 0) return;
            int somma = 0;
            foreach (var v in _perGruppo.Values) somma += v;
            int totale = _conAttesa + _senzaAttesa.Count;
            Segna("il conto per gruppo fa il totale", somma == totale,
                  somma == totale
                      ? somma + " prove in " + _perGruppo.Count + " gruppi"
                      : "la somma per gruppo e' " + somma + ", le prove sono " + totale +
                        ": " + Math.Abs(totale - somma) + " non sono attribuite a nessun " +
                        "gruppo, e una ripartizione che non fa il totale si legge come " +
                        "una ripartizione completa");
        }

        /// <summary>Le righe «gruppo: quante», in ordine di quantita'.</summary>
        private static void ScriviIlContoPerGruppo()
        {
            if (_perGruppo.Count == 0) return;
            var righe = new List<KeyValuePair<string, int>>(_perGruppo);
            righe.Sort((a, b) => b.Value != a.Value
                ? b.Value.CompareTo(a.Value)
                : string.CompareOrdinal(a.Key, b.Key));
            var pezzi = new List<string>();
            foreach (var r in righe) pezzi.Add(r.Key + " " + r.Value);
            Console.WriteLine("per gruppo: " + string.Join(", ", pezzi));
        }

        private static void ScriviLeSenzaAttesa()
        {
            if (_senzaAttesa.Count == 0) return;
            try
            {
                // La cartella del banco si ricava come per il diario: il
                // binario sta in `Banco/bin/`, quindi il padre e' `Banco/`.
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var banco = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                var dove = Path.Combine(banco != null ? banco.FullName : bin,
                                        "resoconto", "senza-attesa.txt");
                var righe = new List<string>
                {
                    "# Le prove che passano senza un'attesa DICHIARATA (voce K3).",
                    "#",
                    "# Non e' un elenco di colpevoli: molte confrontano nel codice invece",
                    "# che in un file di caso, e alcune sono fra le piu' severe della suite.",
                    "# Altre pero' chiamano, non esplodono, e finiscono verdi — e una prova",
                    "# che passa senza asserzione non dimostra un comportamento: dimostra",
                    "# che non e' esplosa.",
                    "#",
                    "# " + _senzaAttesa.Count + " righe, su " + (_conAttesa + _senzaAttesa.Count) +
                    " prove in tutto. Il giro e' del " + DateTime.Now.ToString("s") + ".",
                    "",
                };
                righe.AddRange(_senzaAttesa);
                File.WriteAllLines(dove, righe);
                Console.WriteLine("   l'elenco: " + dove);
            }
            catch (Exception ex)
            {
                Console.WriteLine("   (l'elenco non si e' potuto scrivere: " + ex.Message + ")");
            }
        }

        private static bool _zitto;
        private static bool _zittoRosso;

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
            ScriviNelloStorico(riepilogo);
        }

        /// <summary>
        /// UNA RIGA PER GIRO, in coda a `Banco/storico.txt` (voce F12).
        ///
        /// ── PERCHE' SERVE ────────────────────────────────────────────────
        ///
        /// `in-corso.txt` lo riscrive il giro dopo, e `ultimo-giro.txt` e' una
        /// redirezione di chi lancia: **dei giri passati non restava niente.**
        /// Cosi' «quella prova e' fallita una volta sola» non si poteva
        /// dimostrare ne' smentire — e una prova che fallisce a caso e che
        /// nessuno contabilizza smette di essere una prova: la volta che
        /// fallira' per un motivo vero, verra' archiviata come «quella che ogni
        /// tanto salta».
        ///
        /// Le prove fallite si scrivono PER NOME, perche' la domanda che si fa
        /// a questo file e' sempre la stessa: «quante volte e' successo a
        /// QUELLA?».
        ///
        /// Non e' versionato: e' il registro di questa macchina, non una misura
        /// da portare in giro. Il conteggio, quando serve a decidere, si copia
        /// nel TODO con la data.
        /// </summary>
        private static void ScriviNelloStorico(string riepilogo)
        {
            try
            {
                var bin = AppDomain.CurrentDomain.BaseDirectory;
                var banco = Directory.GetParent(bin.TrimEnd(Path.DirectorySeparatorChar));
                var file = Path.Combine(banco != null ? banco.FullName : bin, "storico.txt");

                var rotte = _esiti.Where(r => r.StartsWith("  NO   ", StringComparison.Ordinal))
                                  .Select(r => r.Substring(7).Trim())
                                  .Select(r => r.Length > 52 ? r.Substring(0, 52).Trim() : r)
                                  .ToList();

                var riga = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                           "  libreria " + (_addin != null ? _addin.GetName().Version.ToString() : "?") +
                           "  " + riepilogo +
                           (rotte.Count > 0 ? "  ->  " + string.Join(" | ", rotte) : "");

                bool nuovo = !File.Exists(file);
                using (var s = new StreamWriter(file, true))
                {
                    if (nuovo)
                        s.WriteLine("# una riga per giro di banco. Serve a contare le prove " +
                                    "che falliscono a caso: «quante volte e' successo a quella?».");
                    s.WriteLine(riga);
                }
            }
            catch { }
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
            var riga = "  --   " + InColonna(nome) + "SALTATA: " + perche;
            _esiti.Add(riga);
            Console.WriteLine(riga);
            Diario(riga);
        }

        /// <summary>
        /// UN ERRORE DI BANCO NON E' UNA PROVA ROSSA (voce F4).
        ///
        /// Una prova rossa dice «il pezzo e' venuto diverso da come doveva»:
        /// e' una misura, ed e' un'informazione. Un errore di banco dice «non
        /// ho potuto misurare» — un campo che nello stato non c'e', un
        /// selettore che non risolve, un caso scritto male. Contarli insieme
        /// vuol dire leggere «una prova e' fallita» quando invece **non e' stata
        /// fatta nessuna prova**, ed e' il modo in cui una suite si svuota senza
        /// che il conto totale cambi.
        ///
        /// Per questo ha un suo simbolo, un suo conteggio, e **il suo codice di
        /// uscita: 2**.
        /// </summary>
        private static void ErroreDiBanco(string nome, string cosa)
        {
            // Sotto `--muta` un caso mutato che non si puo' nemmeno eseguire
            // conta come «e' diventato rosso»: la mutazione ha morso lo stesso.
            if (_zitto) { _zittoRosso = true; return; }

            _erroriDiBanco++;
            var riga = "  !!   " + InColonna(nome) + "ERRORE DI BANCO: " + cosa;
            _esiti.Add(riga);
            Console.WriteLine(riga);
            Diario(riga);
        }

        /// <summary>
        /// Quello che una prova non ha potuto misurare. Vedi ErroreDiBanco.
        /// </summary>
        // `NonHoPotutoMisurare` sta in `Attese.cs`: la alza il valutatore, e
        // quel file lo compila anche `Prove.Contratti`, dove SOLIDWORKS non c'e'.

        private static int _saltate;
        private static int _erroriDiBanco;

        /// <summary>
        /// ATTESE TENTATE E ATTESE VERIFICATE, e se divergono qualcosa sta
        /// misurando il nulla (voce F4). E' la forma letterale della trappola
        /// del progetto: una prova che verificava un difetto smette di
        /// verificarlo quando il campo che leggeva viene rinominato, un
        /// dizionario che non trova la chiave torna `null`, e `null`
        /// confrontato con `null` passa.
        /// </summary>
        private static int _attese;
        private static int _atteseVerificate;

        private static bool _lento;
        private static bool _muta;
        private static bool _regressione;

        /// <summary>
        /// SOLIDWORKS NON DEVE AVERE NIENTE APERTO QUANDO IL GIRO COMINCIA.
        ///
        /// ── IL DIFETTO, PAGATO DUE VOLTE ─────────────────────────────────
        ///
        /// Lo stato di un documento elenca `documenti_aperti`, quindi la prova
        /// «stato: sotto i 16 KB su un telaio da quattro barre» misura anche
        /// cio' che c'e' aperto oltre al telaio. Il 14/9/2026 sei parti
        /// rimaste aperte da un giro di `corri_piani.js` valevano ~650
        /// caratteri e portavano la prova da 15.628 a 16.280: **rossa senza
        /// che il codice fosse cambiato**, e mezz'ora spesa a cercare una
        /// regressione che non c'era.
        ///
        /// Il 23/9/2026 e' successo di nuovo, con quindici documenti aperti e
        /// un giro intero — un'ora e un quarto — buttato.
        ///
        /// **E il rimedio c'era, e girava.** `banco.ps1` chiama
        /// `ChiudiScarti.exe --chiudi Banco\lavoro` al passo 3 di cinque, da
        /// sempre, e lo dice in testa al file. A scavalcarlo e' stato un
        /// `ProvaSaldatura.exe` lanciato a mano per passargli `--libreria`.
        ///
        /// **LA PROTEZIONE STAVA NEL GUSCIO.** Fuori da `banco.ps1` non c'era
        /// niente, e fuori da `banco.ps1` ci si va esattamente quando si sta
        /// facendo qualcosa di particolare — una libreria diversa, un gruppo
        /// solo, un binario che c'e' gia'. *Una protezione che vive nel guscio
        /// si perde nel momento in cui si esce dal guscio, che e' il momento in
        /// cui serve di piu'.*
        ///
        /// Adesso il passo 3 di `banco.ps1` e questo controllo dicono la stessa
        /// cosa due volte, e va bene: il primo chiude in silenzio quel che sa
        /// essere scarto, il secondo si rifiuta di cominciare se e' rimasto
        /// qualcosa.
        ///
        /// ── PERCHE' SI FERMA INVECE DI CHIUDERE ──────────────────────────
        ///
        /// `CloseDoc` scarta le modifiche non salvate. Se su questa macchina
        /// c'e' un progettista con un pezzo aperto, chiudergli il documento
        /// per far girare le prove sarebbe un danno vero per far sparire un
        /// falso rosso. Quindi il banco si ferma e dice cosa ha trovato;
        /// `--chiudi-gli-scarti` e' la richiesta esplicita di chi sa che sono
        /// scarti.
        ///
        /// Esce 3, non 1: un giro che non e' cominciato non e' un giro rosso,
        /// e chiamarlo rosso vorrebbe dire scrivere un resoconto di un giro
        /// che non c'e' stato.
        /// </summary>
        private static bool LaScrivaniaEPulita(string[] argomentiRiga)
        {
            bool chiudiTu = argomentiRiga != null &&
                            Array.IndexOf(argomentiRiga, "--chiudi-gli-scarti") >= 0;

            var nomi = new List<string>();
            try
            {
                var d = (IModelDoc2)_sw.GetFirstDocument();
                while (d != null)
                {
                    nomi.Add(d.GetTitle() ?? "(senza titolo)");
                    d = (IModelDoc2)d.GetNext();
                }
            }
            catch (Exception ex)
            {
                // NON SI FERMA IL GIRO PER UN CONTROLLO CHE NON HA SAPUTO
                // GUARDARE. Si dice, e si va avanti: la prova dei 16 KB
                // resta li' a prendere il caso peggiore.
                Console.WriteLine("non ho potuto contare i documenti aperti: " + ex.Message);
                return true;
            }

            if (nomi.Count == 0) return true;

            if (!chiudiTu)
            {
                Console.WriteLine();
                Console.WriteLine("SOLIDWORKS ha gia' " + nomi.Count +
                                  (nomi.Count == 1 ? " documento aperto" : " documenti aperti") + ":");
                foreach (var n in nomi.Take(20)) Console.WriteLine("    " + n);
                if (nomi.Count > 20) Console.WriteLine("    ... e altri " + (nomi.Count - 20));
                Console.WriteLine();
                Console.WriteLine("Il giro NON parte. Lo stato di un documento elenca i documenti");
                Console.WriteLine("aperti, quindi questi farebbero fallire «stato: sotto i 16 KB»");
                Console.WriteLine("dopo un'ora di prove, senza che niente sia cambiato nel codice.");
                Console.WriteLine();
                Console.WriteLine("  .\\Banco\\bin\\ProvaSaldatura.exe --chiudi-gli-scarti");
                Console.WriteLine();
                Console.WriteLine("li chiude e parte. CloseDoc SCARTA le modifiche non salvate:");
                Console.WriteLine("guarda l'elenco qui sopra prima di chiederlo.");
                return false;
            }

            int chiusi = 0;
            foreach (var n in nomi)
            {
                try { _sw.CloseDoc(n); chiusi++; }
                catch { }
            }

            // SI RICONTA. `CloseDoc` non torna niente, e «ho chiamato la
            // chiusura quindici volte» non e' «sono chiusi».
            int restano = 0;
            try
            {
                var d = (IModelDoc2)_sw.GetFirstDocument();
                while (d != null)
                {
                    restano++;
                    d = (IModelDoc2)d.GetNext();
                }
            }
            catch { }

            Console.WriteLine("scrivania: chiusi " + chiusi + " documenti su " + nomi.Count +
                              ", ne restano " + restano);
            if (restano > 0)
            {
                Console.WriteLine("Non e' partito lo stesso: quelli che restano farebbero fallire");
                Console.WriteLine("«stato: sotto i 16 KB». Chiudili a mano e rilancia.");
                return false;
            }
            return true;
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

                // ── IL MARGINE SI DICE, E SI PRETENDE (voce K13) ─────────
                //
                // «Passata» a settantotto caratteri dal muro non sta
                // avvisando nessuno: la prossima aggiunta qualunque la fa
                // rossa, e arrivera' come se fosse una regressione. Il 23/9
                // il tetto era gia' superato nella configurazione normale di
                // chi lavora — 16.089 con due documenti aperti — e questa
                // prova non lo vedeva perche' il banco gira su una scrivania
                // pulita.
                //
                // Quindi il criterio non e' piu' «sotto il tetto», e' «sotto
                // il tetto CON MARGINE», e il margine si stampa sempre: anche
                // sui verdi, che e' l'unico momento in cui si puo' vedere
                // arrivare il muro.
                const int Tetto = 16000;
                const int MargineMinimoPerCento = 15;
                int soglia = Tetto - Tetto * MargineMinimoPerCento / 100;
                int margine = Tetto - caratteri;
                int perCento = (int)Math.Round(margine * 100.0 / Tetto);

                Segna("stato: sotto i 16 KB con almeno il 15% di margine",
                      caratteri <= soglia,
                      caratteri + " caratteri, margine " + margine + " (" + perCento +
                      "%), " + aperti +
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
            IModelDoc2 ignorato;
            return Esegui(doc, operazione, argomenti, out errore, out ignorato, stato);
        }

        /// <summary>
        /// Come sopra, ma dice anche **se l'operazione ha aperto un documento
        /// nuovo** (voce GT0).
        ///
        /// ── PERCHE' SERVE, E PERCHE' NON SI GUARDA IL DOCUMENTO ATTIVO ───
        ///
        /// `crea_disegno` apre una tavola e da li' in poi si lavora su quella:
        /// e' l'unico cambio di documento legittimo del catalogo. Finche' il
        /// banco eseguiva ogni passo sempre sullo STESSO documento, un caso che
        /// creava una tavola metteva le viste **dentro il pezzo** — o piu'
        /// probabilmente falliva dicendo «il documento attivo non e' una
        /// tavola», che e' il motivo per cui nessuna voce GT ha mai avuto un
        /// caso di banco.
        ///
        /// **Non si chiede a SOLIDWORKS qual e' il documento attivo.** La
        /// regola di questo progetto e' tenere il riferimento, non
        /// `ActiveDoc`: su una macchina dove SOLIDWORKS e' condiviso, «attivo»
        /// e' cio' che ha davanti *chiunque*, e basta che qualcuno apra una
        /// finestra perche' il banco misuri il documento di un altro.
        ///
        /// L'operazione lo **dichiara**: `Esito.DocumentoNuovo`, che
        /// `crea_disegno` riempie apposta. Un cambio dichiarato si puo'
        /// seguire; uno dedotto si puo' solo subire.
        /// </summary>
        private static Dictionary<string, object> Esegui(
            IModelDoc2 doc, string operazione, Dictionary<string, object> argomenti,
            out string errore, out IModelDoc2 documentoNuovo, object stato = null)
        {
            errore = null;
            documentoNuovo = null;
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
            // IL CAMPO PUO' NON ESSERCI, e non e' un guasto: il banco carica la
            // libreria per riflessione, e una libreria piu' vecchia di questa
            // riga non ha `DocumentoNuovo`. In quel caso si torna `null` e si
            // continua come prima — **quello che non si fa e' esplodere**,
            // perche' una prova che muore per un campo assente non misura
            // niente di cio' che doveva misurare.
            try
            {
                var campo = _tEsito.GetField("DocumentoNuovo");
                if (campo != null) documentoNuovo = campo.GetValue(esito) as IModelDoc2;
            }
            catch { }

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
            // NON TORNA NULL: se SOLIDWORKS non apre il documento, lo DICE.
            // Vedi OppureDillo, e la voce F12 per il perche' e' costato.
            return OppureDillo(_sw.NewDocument(tpl, 0, 0, 0) as IModelDoc2,
                               "la parte nuova su cui provare");
        }

        /// <summary>
        /// L'oggetto, oppure un'eccezione che DICE quale passo non l'ha
        /// prodotto (voce F12).
        ///
        /// ── QUANTO E' COSTATO NON AVERLA ─────────────────────────────────
        ///
        /// Il 15/9/2026 la prova «spostamento nullo (deve fallire)» e' uscita
        /// rossa con «Riferimento a un oggetto non impostato su un'istanza di
        /// oggetto.», e basta. Nel giro rosso mancava anche la riga che la
        /// libreria stampa prima di rifiutare, quindi il null stava **prima**
        /// della chiamata da provare — ma su diciotto punti del banco
        /// `NuovaParte()` veniva usata senza guardare se avesse prodotto
        /// qualcosa, e un `NullReferenceException` non dice quale dei diciotto.
        ///
        /// Il difetto non e' la prova che e' fallita: e' che **fallendo non ha
        /// detto niente**. Un banco che sa dire «ho fallito qui» ha gia'
        /// risolto meta' del problema; uno che dice «riferimento nullo» ne
        /// costa diciassette minuti ogni volta che ricapita.
        ///
        /// Non e' una prova rossa: e' il banco che non e' riuscito a
        /// PREPARARE il pezzo, ed e' un'altra cosa — per questo il messaggio lo
        /// dice a chiare lettere.
        /// </summary>
        private static T OppureDillo<T>(T cosa, string quale) where T : class
        {
            if (cosa != null) return cosa;
            throw new InvalidOperationException(
                "SOLIDWORKS non ha prodotto " + quale + " (ha risposto null). " +
                "Non e' la prova a essere fallita: e' il banco che non e' " +
                "riuscito a preparare il pezzo. Guarda quanti documenti sono " +
                "rimasti aperti, e rilancia.");
        }

        /// <summary>
        /// I tre piani di riferimento del documento, e un'eccezione che li
        /// nomina se l'albero non li da'. Era l'altro punto della stessa
        /// famiglia (voce F12): `GetFeatures(false) as object[]` puo' tornare
        /// null, e il `.Select` subito dopo dava lo stesso NullReference muto
        /// di `NuovaParte()`.
        /// </summary>
        private static List<IFeature> PianiDiRiferimento(IModelDoc2 doc)
        {
            var albero = OppureDillo(doc.FeatureManager.GetFeatures(false) as object[],
                                     "l'albero delle feature del documento");
            var piani = albero
                .Select(o => o as IFeature)
                .Where(f => { try { return f.GetTypeName2() == "RefPlane"; } catch { return false; } })
                .ToList();
            if (piani.Count == 0)
                throw new InvalidOperationException(
                    "il documento non ha nessun piano di riferimento: il modello " +
                    "di parte predefinito non e' quello che il banco si aspetta.");
            return piani;
        }

        private static void Chiudi(IModelDoc2 doc)
        {
            if (doc == null) return;
            try { _sw.CloseDoc(doc.GetTitle()); } catch { }
        }

        private static void Piastra(IModelDoc2 doc, double L, double W, double H)
        {
            var piani = PianiDiRiferimento(doc);
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
