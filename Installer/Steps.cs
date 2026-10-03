using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace Oce.Solid.Agent.Installer
{
    /// <summary>Com'e' andato un controllo: verde, giallo, rosso.</summary>
    internal enum Esito { Bene, Avviso, Male }

    internal sealed class Controllo
    {
        public string Titolo;
        public Esito Esito;
        public string Dettaglio;
        /// <summary>Cosa deve fare chi legge, se c'e' qualcosa da fare.</summary>
        public string CosaFare;
    }

    /// <summary>
    /// I CONTROLLI E L'INSTALLAZIONE, SENZA INTERFACCIA.
    ///
    /// Sta separato dalla finestra di proposito: cosi' la stessa logica si puo'
    /// eseguire senza finestra (`Installa.exe /silenzioso`) e, soprattutto, si
    /// puo' leggere senza dover leggere anche il codice dei bottoni.
    ///
    /// LA REGOLA DI QUESTO FILE: un controllo che fallisce deve dire COSA FARE,
    /// non solo che e' fallito. «MSBuild non trovato» manda su un motore di
    /// ricerca; «installa Build Tools for Visual Studio 2022, carico di lavoro
    /// Sviluppo desktop .NET» fa ripartire il lavoro. E' la stessa regola che
    /// vale per i messaggi di errore delle operazioni di disegno.
    /// </summary>
    internal static class Passi
    {
        public const string NomeProdotto = "OCE Solid Agent";
        public const string NomeEseguibile = "oce-solid-agent-mcp.exe";
        public const string NomeCatalogo = "catalogo.json";
        /// <summary>
        /// Le operazioni di disegno. **Sta nel carico**, non si cerca sulla
        /// macchina: fino alla 0.5.0 era `OpenPDM.SwAddin.dll`, codice
        /// privato che il pacchetto non poteva contenere, e l'installatore
        /// doveva andarla a cercare. Adesso ce l'ha dentro.
        /// </summary>
        public const string NomeLibreria = "Riscontro.Disegno.dll";

        /// <summary>
        /// I file che l'installazione mette nella cartella, e quindi gli UNICI
        /// che la disinstallazione toglie.
        ///
        /// UNA LISTA SOLA PER I DUE VERSI (voce H15). Prima la disinstallazione
        /// aveva la sua, e ci cercava `Oce.Solid.Agent.pdb` — un file che
        /// nessuna versione ha mai installato: fino alla 0.27.0 si chiamava
        /// `SolidWorksMcp.pdb`, e in tutti e due i casi `Installa` non l'ha mai
        /// estratto. Due liste che dovrebbero dire la stessa cosa si separano
        /// il giorno in cui se ne tocca una.
        /// </summary>
        public static readonly string[] FileInstallati =
        {
            NomeEseguibile, NomeEseguibile + ".config", NomeCatalogo, NomeLibreria,
            "LICENSE", "NOTICE",
        };

        /// <summary>
        /// Files the BRIDGE writes beside itself while it runs, which no
        /// install puts there (entry H19, A5). Today one: the log of questions
        /// that had no operation (Strumenti/RegistroDelleDomande.cs, file name
        /// RegistroDelleDomande.NomeDelFile; RISCONTRO_REGISTRO can move it
        /// elsewhere, and then it is not ours to remove). Left behind, it kept
        /// the folder alive with the message that the folder held someone
        /// else's files, which was not true.
        /// </summary>
        public static readonly string[] BridgeOwnFiles = { "domande-senza-risposta.jsonl" };

        // ── THE EXIT CODES OF THE BRIDGE'S --prova (entry H19, B3) ─────────
        // The same numbers as Programma.SelfTest* in the bridge, which this
        // assembly cannot reference; Prove.Contratti reads both and compares.
        public const int SelfTestAttached = 0;
        public const int SelfTestOther = 1;
        public const int SelfTestNotRunning = 3;
        public const int SelfTestNotInstalled = 4;
        public const int SelfTestSkipped = 5;
        public const int SelfTestNotReachable = 6;
        /// <summary>Not a code of the bridge: it could not be run, or did not answer.</summary>
        public const int SelfTestNoAnswer = -1;

        /// <summary>
        /// The self-test codes after which "installed" is true: attached; or
        /// SOLIDWORKS not running (not an install problem); or not tried
        /// because OCE_NO_ATTACH asked so. NOT 6 (H19 review): a SOLIDWORKS
        /// that runs and does not answer, usually as administrator, is one the
        /// bridge will not attach to until something changes.
        /// </summary>
        public static bool SelfTestAccepted(int code) =>
            code == SelfTestAttached || code == SelfTestNotRunning || code == SelfTestSkipped;

        public static string SelfTestMeaning(int code)
        {
            switch (code)
            {
                case SelfTestAttached: return "attached to SOLIDWORKS";
                case SelfTestNotRunning: return "SOLIDWORKS is installed but not running";
                case SelfTestNotInstalled: return "SOLIDWORKS is not installed, or not registered";
                case SelfTestSkipped: return "attach not tried: OCE_NO_ATTACH is set";
                case SelfTestNotReachable:
                    return "SOLIDWORKS is running but did not answer: probably started as administrator " +
                           "(or the client was); start both the same way";
                case SelfTestNoAnswer: return "the bridge did not run, or did not answer";
                default: return "not attached (code " + code + ")";
            }
        }

        /// <summary>Dove va installato, se chi installa non dice altro.</summary>
        public static string CartellaPredefinita =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Oce.Solid.Agent");

        public static string CartellaDiQuestoExe =>
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";

        // ─────────────────────────────────────────────────────────────────
        // I controlli
        // ─────────────────────────────────────────────────────────────────

        public static List<Controllo> Controlla(string redist)
        {
            var fuori = new List<Controllo>
            {
                ControllaFramework(),
                ControllaSolidWorks(redist),
                ControllaPayload(),
                ControllaLibreria(),
                ControllaSolidWorksAperto(),
            };
            return fuori;
        }

        private static Controllo ControllaFramework()
        {
            // .NET Framework 4.8 c'e' di serie da Windows 10 1903. Su macchine
            // piu' vecchie va messo a mano, ed e' l'unico prerequisito vero.
            int release = 0;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (k != null) release = Convert.ToInt32(k.GetValue("Release", 0));
                }
            }
            catch { }

            if (release >= 528040)
                return new Controllo
                {
                    Titolo = ".NET Framework 4.8",
                    Esito = Esito.Bene,
                    Dettaglio = "release " + release,
                };

            // Se siamo arrivati a eseguire questo codice, un .NET c'e'. Quindi
            // il caso «zero» e' «non riesco a leggerlo», non «non c'e'».
            if (release == 0)
                return new Controllo
                {
                    Titolo = ".NET Framework 4.8",
                    Esito = Esito.Avviso,
                    Dettaglio = "non riesco a leggerne la versione dal registro",
                    CosaFare = "Se l'installazione fallisce, e' probabilmente questo.",
                };

            return new Controllo
            {
                Titolo = ".NET Framework 4.8",
                Esito = Esito.Male,
                Dettaglio = "installato release " + release + ", ne serve almeno 528040",
                CosaFare = "Scarica .NET Framework 4.8 da " +
                           "https://dotnet.microsoft.com/download/dotnet-framework/net48",
            };
        }

        public static string TrovaRedist()
        {
            // SW_INSTALL_PATH FIRST, AND IT IS THE ROOT (entry H10, read here
            // since H19 A7). The bridge, build.ps1 and the README all give it
            // as the variable; the installer read only the old SW_REDIST, so
            // someone who had set the one the README names was told that
            // SOLIDWORKS was missing.
            var root = Environment.GetEnvironmentVariable("SW_INSTALL_PATH");
            if (!string.IsNullOrWhiteSpace(root))
            {
                string fromRoot = null;
                try { fromRoot = Path.Combine(root.Trim(), "api", "redist"); } catch { }
                if (CartellaHaInterop(fromRoot)) return fromRoot;
            }
            var suo = Environment.GetEnvironmentVariable("SW_REDIST");
            if (!string.IsNullOrWhiteSpace(suo) && CartellaHaInterop(suo)) return suo;

            var candidati = new List<string>();
            foreach (var pf in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            })
            {
                if (string.IsNullOrWhiteSpace(pf)) continue;
                candidati.Add(Path.Combine(pf, @"SOLIDWORKS Corp\SOLIDWORKS\api\redist"));
                candidati.Add(Path.Combine(pf, @"SolidWorks Corp\SolidWorks\api\redist"));
            }

            // Anche le installazioni per anno: SOLIDWORKS 2024, 2025...
            foreach (var pf in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            })
            {
                if (string.IsNullOrWhiteSpace(pf)) continue;
                try
                {
                    foreach (var d in Directory.GetDirectories(pf, "SOLIDWORKS*"))
                        candidati.Add(Path.Combine(d, @"SOLIDWORKS\api\redist"));
                }
                catch { }
            }

            foreach (var c in candidati)
                if (CartellaHaInterop(c)) return c;
            return null;
        }

        /// <summary>La versione principale di SOLIDWORKS provata: 29 = 2021 (SP5.1).</summary>
        public const int VersioneProvata = 29;

        /// <summary>«29.5.1.1» -> 29; 0 se non si legge. 29 e' la 2021: l'anno e' 1992 + la principale.</summary>
        public static int VersionePrincipale(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return 0;
            int punto = v.IndexOf('.');
            int n;
            return int.TryParse(punto > 0 ? v.Substring(0, punto) : v, out n) ? n : 0;
        }

        public static bool CartellaHaInterop(string cartella)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(cartella) &&
                       File.Exists(Path.Combine(cartella, "SolidWorks.Interop.sldworks.dll"));
            }
            catch { return false; }
        }

        private static Controllo ControllaSolidWorks(string redist)
        {
            if (string.IsNullOrWhiteSpace(redist)) redist = TrovaRedist();

            if (CartellaHaInterop(redist))
            {
                string v = "";
                try
                {
                    v = FileVersionInfo.GetVersionInfo(
                        Path.Combine(redist, "SolidWorks.Interop.sldworks.dll")).FileVersion;
                }
                catch { }
                // LA VERSIONE PROVATA SI DICHIARA (voce H17, decisione del proprietario del
                // 27/9/2026): il ponte e' misurato solo su SOLIDWORKS 2021 SP5.1
                // (interop 29.5.1). Una versione diversa non si blocca: si avvisa.
                // Le piu' vecchie si possono provare quando serve, le piu' nuove no
                // (la licenza su cui si prova non le installa).
                int principale = VersionePrincipale(v);
                string anno = principale > 0 ? (principale + 1992).ToString() : null;
                bool provata = principale == VersioneProvata;
                return new Controllo
                {
                    Titolo = "SOLIDWORKS installato",
                    Esito = provata || principale <= 0 ? Esito.Bene : Esito.Avviso,
                    Dettaglio = redist + (string.IsNullOrWhiteSpace(v) ? "" : "  (interop " + v +
                                (anno == null ? "" : ", SOLIDWORKS " + anno) + ")"),
                    CosaFare = provata || principale <= 0 ? null :
                        "Il ponte e' provato solo su SOLIDWORKS 2021 SP5.1. Con la " + anno + " si installa lo " +
                        "stesso, ma nessuno l'ha misurata: " + (principale > VersioneProvata
                            ? "e' piu' nuova di quella provata, e certe API possono essersi mosse."
                            : "e' piu' vecchia di quella provata; si puo' provare, e se qualcosa non va dillo."),
                };
            }

            return new Controllo
            {
                Titolo = "SOLIDWORKS installato",
                Esito = Esito.Male,
                Dettaglio = "non trovo la cartella api\\redist",
                // There is no Browse button for SOLIDWORKS, in the window or
                // anywhere else (entry H19, A7): the text named one.
                CosaFare =
                    "Serve SOLIDWORKS installato su questa macchina: le librerie di " +
                    "interoperabilita' si prendono da li' e non sono incluse qui. " +
                    "If it is installed in a non-standard folder, pass --redist <its api\\redist folder>, " +
                    "or set SW_INSTALL_PATH to its root (the folder that ends in \\SOLIDWORKS).",
            };
        }

        private static Controllo ControllaPayload()
        {
            if (Payload.Incorporato)
                return new Controllo
                {
                    Titolo = "Ponte da installare",
                    Esito = Esito.Bene,
                    Dettaglio = "incluso in questo installatore",
                };

            var accanto = Path.Combine(CartellaDiQuestoExe, NomeEseguibile);
            if (File.Exists(accanto))
                return new Controllo
                {
                    Titolo = "Ponte da installare",
                    Esito = Esito.Bene,
                    Dettaglio = "preso dalla cartella dell'installatore",
                };

            return new Controllo
            {
                Titolo = "Ponte da installare",
                Esito = Esito.Male,
                Dettaglio = "non trovo " + NomeEseguibile,
                CosaFare =
                    "Questo installatore non ha il ponte dentro e non lo trova " +
                    "accanto a se'. Se hai clonato il repository, compila prima " +
                    "con build.ps1: produce un Installa.exe completo.",
            };
        }

        /// <summary>
        /// La libreria che sta ACCANTO a questo eseguibile, se c'e'.
        ///
        /// NON si va piu' a cercarla per la macchina, e non e' una
        /// semplificazione: cercarla vorrebbe dire installare, su una
        /// macchina che ha per caso una copia vecchia da qualche parte,
        /// QUELLA copia — con le operazioni di due settimane fa e nessuno
        /// che lo dica. Il carico ce l'ha, e quella e' la buona.
        ///
        /// Serve solo al caso di chi lancia l'installatore appena compilato
        /// dalla cartella `bin`, dove il carico non e' ancora incorporato.
        /// </summary>
        public static string TrovaLibreriaAccanto()
        {
            try
            {
                var c = Path.Combine(CartellaDiQuestoExe, NomeLibreria);
                if (File.Exists(c)) return Path.GetFullPath(c);
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Le operazioni di disegno ci sono? Dalla 0.6.0 la risposta e' quasi
        /// sempre «si', le ho dentro»: e' una constatazione, non piu' una
        /// domanda a chi installa.
        ///
        /// Il caso che resta e' l'installatore lanciato dalla cartella `bin`
        /// appena compilata, dove il carico non e' ancora incorporato.
        /// </summary>
        private static Controllo ControllaLibreria()
        {
            if (Payload.Contiene(NomeLibreria))
                return new Controllo
                {
                    Titolo = "Operazioni di disegno",
                    Esito = Esito.Bene,
                    Dettaglio = NomeLibreria + " e' dentro l'installatore",
                };

            var accanto = TrovaLibreriaAccanto();
            if (!string.IsNullOrWhiteSpace(accanto))
            {
                string v = "";
                try { v = FileVersionInfo.GetVersionInfo(accanto).FileVersion; }
                catch { }
                return new Controllo
                {
                    Titolo = "Operazioni di disegno",
                    Esito = Esito.Bene,
                    Dettaglio = accanto + (string.IsNullOrWhiteSpace(v) ? "" : "  (" + v + ")"),
                };
            }

            // ADESSO E' BLOCCANTE, E PRIMA NON LO ERA.
            // Quando le operazioni stavano in una DLL privata, non averle era
            // normale e l'installazione aveva senso lo stesso: il ponte
            // partiva e diceva di non essere agganciato. Adesso le operazioni
            // sono parte del prodotto: se mancano, il pacchetto e' rotto, e un
            // pacchetto rotto che si installa lo scopre chi lo usa.
            return new Controllo
            {
                Titolo = "Operazioni di disegno",
                Esito = Esito.Male,
                Dettaglio = NomeLibreria + " non c'e' ne' dentro ne' accanto",
                CosaFare =
                    "Questo installatore e' incompleto. Se hai clonato il " +
                    "repository, compila con build.ps1: produce un Installa.exe " +
                    "con il ponte e le operazioni dentro.",
            };
        }

        private static Controllo ControllaSolidWorksAperto()
        {
            bool aperto = false;
            try { aperto = Process.GetProcessesByName("SLDWORKS").Length > 0; }
            catch { }

            if (aperto)
                return new Controllo
                {
                    Titolo = "SOLIDWORKS aperto adesso",
                    Esito = Esito.Bene,
                    Dettaglio = "la verifica finale potra' guardare il documento",
                };

            return new Controllo
            {
                Titolo = "SOLIDWORKS aperto adesso",
                Esito = Esito.Avviso,
                Dettaglio = "closed",
                CosaFare =
                    "Non serve per installare. Serve per usare il ponte: si " +
                    "aggancia a una sessione che c'e' gia', non ne avvia una.",
            };
        }

        // ─────────────────────────────────────────────────────────────────
        // L'installazione
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Copia il ponte nella cartella scelta. Ritorna il percorso
        /// dell'eseguibile installato, o solleva con un motivo leggibile.
        /// </summary>
        public static string Installa(
            string cartella, Action<string> racconta)
        {
            if (string.IsNullOrWhiteSpace(cartella))
                throw new Exception("Non mi hai detto dove installare.");

            Directory.CreateDirectory(cartella);
            racconta("cartella: " + cartella);

            // 1. il ponte e il catalogo
            //
            // DA QUALE VERSIONE A QUALE (voce H15). Chi reinstalla vuole sapere
            // se sta aggiornando o rimettendo la stessa: si legge `VERSION.txt`
            // dentro l'eseguibile che c'e' PRIMA di sovrascriverlo, e dentro
            // quello nuovo DOPO. Nessuna delle due letture e' una promessa: se
            // una non riesce, si dice «versione non leggibile».
            var eseguibile = Path.Combine(cartella, NomeEseguibile);
            string vecchia = File.Exists(eseguibile) ? VersioneDi(eseguibile) ?? "versione non leggibile" : null;
            Payload.Estrai(NomeEseguibile, eseguibile, racconta);
            string nuova = VersioneDi(eseguibile) ?? "versione non leggibile";
            if (vecchia == null) racconta("installo la " + nuova);
            else if (vecchia == nuova) racconta("reinstallo la " + nuova + " (era gia' questa)");
            else racconta("aggiorno dalla " + vecchia + " alla " + nuova);
            Payload.Estrai(NomeCatalogo, Path.Combine(cartella, NomeCatalogo), racconta);
            Payload.EstraiSePresente(
                NomeEseguibile + ".config",
                Path.Combine(cartella, NomeEseguibile + ".config"), racconta);

            // 2. LE OPERAZIONI. Prima dal carico; se il carico non ce l'ha
            //    (installatore lanciato dalla cartella bin appena
            //    compilata), da accanto all'eseguibile. Se non c'e' ne'
            //    l'una ne' l'altra si SOLLEVA: un ponte installato che non
            //    disegna e' peggio di un'installazione che si ferma.
            //    `Estrai` guarda prima dentro il carico e poi accanto
            //    all'installatore, e solleva se non c'e' ne' l'una ne' l'altra.
            //    Solleva ed e' giusto: un ponte installato che non disegna e'
            //    peggio di un'installazione che si ferma dicendo cosa manca.
            var libreria = Path.Combine(cartella, NomeLibreria);
            Payload.Estrai(NomeLibreria, libreria, racconta);

            // 3. le note legali viaggiano con il programma
            //
            // Un file nuovo estratto qui va anche in `FileInstallati`, se no la
            // disinstallazione lo lascia li' per sempre.
            foreach (var nome in new[] { "LICENSE", "NOTICE" })
                Payload.EstraiSePresente(nome, Path.Combine(cartella, nome), racconta);

            if (!File.Exists(eseguibile))
                throw new Exception(
                    "Ho finito senza errori ma " + NomeEseguibile + " non c'e'. " +
                    "Non lo chiamo riuscito.");

            return eseguibile;
        }

        /// <summary>
        /// La versione scritta in `VERSION.txt` dentro un eseguibile del
        /// ponte, o null se non si legge.
        ///
        /// Si carica dai BYTE, non dal file: `LoadFrom` e `ReflectionOnlyLoadFrom`
        /// tengono il file aperto fino all'uscita, e l'eseguibile appena letto
        /// e' proprio quello da sovrascrivere. La versione del file Win32 non
        /// serve: dice 0.0.0.0 (voce H14).
        /// </summary>
        public static string VersioneDi(string eseguibile)
        {
            try
            {
                var a = Assembly.Load(File.ReadAllBytes(eseguibile));
                foreach (var nome in a.GetManifestResourceNames())
                {
                    if (!nome.EndsWith("VERSION.txt", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var s = a.GetManifestResourceStream(nome))
                    using (var r = new StreamReader(s))
                    {
                        var v = r.ReadToEnd().Trim();
                        return v.Length == 0 ? null : v;
                    }
                }
            }
            catch { }
            return null;
        }

        public static bool PercorsiUguali(string a, string b)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd('\\'),
                    Path.GetFullPath(b).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Il giro secco: `--prova` non tocca niente, si aggancia e racconta.
        /// E' l'unico modo di dire «installato» sapendo che e' vero.
        ///
        /// THE EXIT CODE DECIDES, NOT THE TEXT (entry H19, B3). Until 0.125.0
        /// success was a phrase of the bridge, or the phrase that said
        /// SOLIDWORKS was not running; the second one also comes out when
        /// SOLIDWORKS runs as administrator and the client does not, and both
        /// were going to be translated. The text is still returned, for
        /// whoever reads it.
        /// </summary>
        public static string Prova(string eseguibile, string redist, out int code)
        {
            code = SelfTestNoAnswer;
            try
            {
                var avvio = new ProcessStartInfo(eseguibile, "--prova")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(eseguibile),
                };
                if (!string.IsNullOrWhiteSpace(redist))
                {
                    // The folder the installer decided on is the one the
                    // bridge uses: SW_INSTALL_PATH would win over SW_REDIST.
                    avvio.EnvironmentVariables.Remove("SW_INSTALL_PATH");
                    avvio.EnvironmentVariables["SW_REDIST"] = redist;
                }

                using (var p = Process.Start(avvio))
                {
                    // `--prova` scrive su stderr di proposito: stdout appartiene
                    // al protocollo e li' non ci va nient'altro.
                    var err = p.StandardError.ReadToEndAsync();
                    var mid = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(60000))
                    {
                        try { p.Kill(); } catch { }
                        return "Il ponte non ha risposto entro un minuto.";
                    }
                    code = p.ExitCode;
                    return (err.Result + mid.Result).Trim();
                }
            }
            catch (Exception ex)
            {
                return "Non riesco a eseguire il ponte: " + ex.Message;
            }
        }

        /// <summary>
        /// Toglie i file installati, and the files the bridge writes beside
        /// itself. Non tocca la configurazione del client. Returns the files
        /// of ours that could NOT be removed (entry H19, A5): until 0.125.0 a
        /// locked file was mentioned in a line and the uninstall still exited 0.
        /// </summary>
        public static List<string> Disinstalla(string cartella, Action<string> racconta)
        {
            var left = new List<string>();
            if (string.IsNullOrWhiteSpace(cartella) || !Directory.Exists(cartella))
            {
                racconta("niente da togliere in " + cartella);
                return left;
            }

            // Si cancellano SOLO i file che questo installatore mette. Una
            // `Directory.Delete(recursive)` su una cartella scelta da chi
            // installa e' il modo classico di cancellare il desktop di
            // qualcuno per un percorso incollato male.
            foreach (var nome in FileInstallati.Concat(BridgeOwnFiles))
            {
                var f = Path.Combine(cartella, nome);
                try
                {
                    if (File.Exists(f)) { File.Delete(f); racconta("tolto " + nome); }
                }
                catch (Exception ex)
                {
                    racconta("non riesco a togliere " + nome + ": " + ex.Message);
                    left.Add(nome);
                }
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(cartella).Any())
                {
                    Directory.Delete(cartella);
                    racconta("tolta la cartella");
                }
                else if (left.Count == 0)
                {
                    racconta("la cartella resta: dentro c'e' anche altro, e non e' mio.");
                }
            }
            catch { }
            return left;
        }

        // ─────────────────────────────────────────────────────────────────
        // The weldment profiles (entry H19, A6)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Where the weldment profiles (.sldlfp) are on this machine, or null;
        /// <paramref name="source"/> says where the answer came from.
        ///
        /// Since library 0.112 PROFILI_SALDATURA has no default, and without it
        /// profili_saldatura refuses. Two places were checked on the machine
        /// this was written on (SOLIDWORKS 2021 SP5.1), and only folders that
        /// exist are ever proposed:
        ///
        /// 1. the folder SOLIDWORKS itself uses, Tools > Options > File
        ///    Locations > Weldment Profiles: registry value "Weldment Profile
        ///    Folders" under HKCU\Software\SolidWorks\SOLIDWORKS &lt;year&gt;\ExtReferences
        ///    (several folders are separated by ';'; the first that exists wins);
        /// 2. the profiles SOLIDWORKS installs, &lt;root&gt;\lang\&lt;language&gt;\weldment profiles,
        ///    with &lt;language&gt; read from the same key's "Weldment Cut List
        ///    Template Folders" or "Weld Table Template Folder" (both end in
        ///    \lang\&lt;language&gt;), else english. On that machine all 15
        ///    language folders have one, with ANSI, ISO, DIN and six more
        ///    standards inside.
        ///
        /// &lt;year&gt; comes from the interop's major version (29 = 2021), or the
        /// newest SOLIDWORKS key when it cannot be read.
        /// </summary>
        public static string FindWeldmentProfiles(string redist, out string source)
        {
            source = null;
            RegistryKey ext = null;
            try
            {
                string yearKey = null;
                int major = 0;
                try
                {
                    if (CartellaHaInterop(redist))
                        major = VersionePrincipale(FileVersionInfo.GetVersionInfo(
                            Path.Combine(redist, "SolidWorks.Interop.sldworks.dll")).FileVersion);
                }
                catch { }
                using (var sw = Registry.CurrentUser.OpenSubKey(@"Software\SolidWorks"))
                {
                    if (sw != null)
                    {
                        var names = sw.GetSubKeyNames()
                            .Where(n => n.StartsWith("SOLIDWORKS 2", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                        if (major > 0)
                            yearKey = names.FirstOrDefault(n => n.EndsWith((major + 1992).ToString()));
                        if (yearKey == null) yearKey = names.FirstOrDefault();
                    }
                }
                if (yearKey != null) ext = Registry.CurrentUser.OpenSubKey(@"Software\SolidWorks\" + yearKey + @"\ExtReferences");

                // 1. what SOLIDWORKS itself uses
                var folders = Convert.ToString(ext?.GetValue("Weldment Profile Folders") ?? "");
                foreach (var c in folders.Split(';'))
                {
                    var d = c.Trim();
                    if (d.Length > 0 && Directory.Exists(d))
                    {
                        source = "the Weldment Profiles folder set in " + yearKey + " (Tools > Options > File Locations)";
                        return d;
                    }
                }

                // 2. what SOLIDWORKS installs
                string swRoot = null;
                try { if (CartellaHaInterop(redist)) swRoot = Directory.GetParent(redist.TrimEnd('\\'))?.Parent?.FullName; } catch { }
                if (swRoot == null) return null;
                string language = null;
                foreach (var valueName in new[] { "Weldment Cut List Template Folders", "Weld Table Template Folder" })
                {
                    var v = Convert.ToString(ext?.GetValue(valueName) ?? "").Split(';')[0].Trim().TrimEnd('\\');
                    int i = v.LastIndexOf(@"\lang\", StringComparison.OrdinalIgnoreCase);
                    if (i >= 0) { language = v.Substring(i + 6); break; }
                }
                foreach (var l in new[] { language, "english" })
                {
                    if (string.IsNullOrWhiteSpace(l)) continue;
                    var d = Path.Combine(swRoot, "lang", l, "weldment profiles");
                    if (Directory.Exists(d))
                    {
                        source = "the profiles installed with SOLIDWORKS (" + l + ")";
                        return d;
                    }
                }
                return null;
            }
            catch { return null; }
            finally { ext?.Dispose(); }
        }
    }
}
