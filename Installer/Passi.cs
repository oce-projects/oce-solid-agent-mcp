using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace SolidWorksMcp.Installer
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
        public const string NomeProdotto = "SolidWorks MCP";
        public const string NomeEseguibile = "SolidWorksMcp.exe";
        public const string NomeCatalogo = "catalogo.json";
        public const string NomeAddin = "OpenPDM.SwAddin.dll";

        /// <summary>Dove va installato, se chi installa non dice altro.</summary>
        public static string CartellaPredefinita =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SolidWorksMcp");

        public static string CartellaDiQuestoExe =>
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";

        // ─────────────────────────────────────────────────────────────────
        // I controlli
        // ─────────────────────────────────────────────────────────────────

        public static List<Controllo> Controlla(string percorsoAddin, string redist)
        {
            var fuori = new List<Controllo>
            {
                ControllaFramework(),
                ControllaSolidWorks(redist),
                ControllaPayload(),
                ControllaAddin(percorsoAddin),
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

        private static bool CartellaHaInterop(string cartella)
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
                return new Controllo
                {
                    Titolo = "SOLIDWORKS installato",
                    Esito = Esito.Bene,
                    Dettaglio = redist + (string.IsNullOrWhiteSpace(v) ? "" : "  (interop " + v + ")"),
                };
            }

            return new Controllo
            {
                Titolo = "SOLIDWORKS installato",
                Esito = Esito.Male,
                Dettaglio = "non trovo la cartella api\\redist",
                CosaFare =
                    "Serve SOLIDWORKS installato su questa macchina: le librerie di " +
                    "interoperabilita' si prendono da li' e non sono incluse qui. " +
                    "Se e' installato in un percorso non standard, indicalo con il " +
                    "bottone «Sfoglia» accanto a SOLIDWORKS.",
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

        public static string TrovaAddin()
        {
            var suo = Environment.GetEnvironmentVariable("OPENPDM_ADDIN");
            if (!string.IsNullOrWhiteSpace(suo) && File.Exists(suo)) return suo;

            var candidati = new[]
            {
                Path.Combine(CartellaDiQuestoExe, NomeAddin),
                Path.Combine(CartellaPredefinita, NomeAddin),
                Path.Combine(CartellaDiQuestoExe,
                    @"..\..\OpenPDM\client-windows\OpenPDMSwAddin\bin\Release\" + NomeAddin),
            };
            foreach (var c in candidati)
            {
                try { if (File.Exists(c)) return Path.GetFullPath(c); }
                catch { }
            }
            return null;
        }

        private static Controllo ControllaAddin(string percorso)
        {
            if (string.IsNullOrWhiteSpace(percorso)) percorso = TrovaAddin();

            if (!string.IsNullOrWhiteSpace(percorso) && File.Exists(percorso))
            {
                string v = "";
                try { v = FileVersionInfo.GetVersionInfo(percorso).FileVersion; }
                catch { }
                return new Controllo
                {
                    Titolo = "Add-in OpenPDM",
                    Esito = Esito.Bene,
                    Dettaglio = percorso + (string.IsNullOrWhiteSpace(v) ? "" : "  (" + v + ")"),
                };
            }

            // NON e' un errore bloccante, ed e' importante che sia detto qui:
            // il ponte parte lo stesso e risponde, semplicemente non sa
            // disegnare. Meglio un'installazione che funziona a meta' e lo
            // dichiara, che un installatore che si rifiuta di finire.
            return new Controllo
            {
                Titolo = "Add-in OpenPDM",
                Esito = Esito.Avviso,
                Dettaglio = "non trovato",
                CosaFare =
                    "Senza l'add-in il ponte si installa e risponde, ma non sa " +
                    "disegnare: dira' che non e' agganciato. L'add-in fa parte di " +
                    "OpenPDM, che oggi e' privato. Se ce l'hai, indicalo con " +
                    "«Sfoglia».",
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
                Dettaglio = "chiuso",
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
            string cartella, string percorsoAddin, Action<string> racconta)
        {
            if (string.IsNullOrWhiteSpace(cartella))
                throw new Exception("Non mi hai detto dove installare.");

            Directory.CreateDirectory(cartella);
            racconta("cartella: " + cartella);

            // 1. il ponte e il catalogo
            var eseguibile = Path.Combine(cartella, NomeEseguibile);
            Payload.Estrai(NomeEseguibile, eseguibile, racconta);
            Payload.Estrai(NomeCatalogo, Path.Combine(cartella, NomeCatalogo), racconta);
            Payload.EstraiSePresente(
                NomeEseguibile + ".config",
                Path.Combine(cartella, NomeEseguibile + ".config"), racconta);

            // 2. l'add-in, se c'e'. Si COPIA invece di puntarlo dov'e': cosi'
            //    il ponte continua a funzionare anche se quella cartella
            //    sparisce, e l'installazione e' una cosa sola da cancellare.
            if (!string.IsNullOrWhiteSpace(percorsoAddin) && File.Exists(percorsoAddin))
            {
                var destinazione = Path.Combine(cartella, NomeAddin);
                if (!PercorsiUguali(percorsoAddin, destinazione))
                {
                    File.Copy(percorsoAddin, destinazione, true);
                    racconta("copiato " + NomeAddin);
                }
            }

            // 3. le note legali viaggiano con il programma
            foreach (var nome in new[] { "LICENSE", "NOTICE" })
                Payload.EstraiSePresente(nome, Path.Combine(cartella, nome), racconta);

            if (!File.Exists(eseguibile))
                throw new Exception(
                    "Ho finito senza errori ma " + NomeEseguibile + " non c'e'. " +
                    "Non lo chiamo riuscito.");

            return eseguibile;
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
        /// </summary>
        public static string Prova(string eseguibile, string redist, out bool inPiedi)
        {
            inPiedi = false;
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
                    avvio.EnvironmentVariables["SW_REDIST"] = redist;

                using (var p = Process.Start(avvio))
                {
                    // `--prova` scrive su stderr di proposito: stdout appartiene
                    // al protocollo e li' non ci va nient'altro.
                    string err = p.StandardError.ReadToEnd();
                    string mid = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(60000))
                    {
                        try { p.Kill(); } catch { }
                        return "Il ponte non ha risposto entro un minuto.";
                    }
                    var tutto = (err + mid).Trim();
                    inPiedi = tutto.IndexOf("il ponte sta in piedi",
                        StringComparison.OrdinalIgnoreCase) >= 0;
                    return tutto;
                }
            }
            catch (Exception ex)
            {
                return "Non riesco a eseguire il ponte: " + ex.Message;
            }
        }

        /// <summary>Toglie i file installati. Non tocca la configurazione del client.</summary>
        public static void Disinstalla(string cartella, Action<string> racconta)
        {
            if (string.IsNullOrWhiteSpace(cartella) || !Directory.Exists(cartella))
            {
                racconta("niente da togliere in " + cartella);
                return;
            }

            // Si cancellano SOLO i file che questo installatore mette. Una
            // `Directory.Delete(recursive)` su una cartella scelta da chi
            // installa e' il modo classico di cancellare il desktop di
            // qualcuno per un percorso incollato male.
            foreach (var nome in new[]
            {
                NomeEseguibile, NomeEseguibile + ".config", "SolidWorksMcp.pdb",
                NomeCatalogo, NomeAddin, "LICENSE", "NOTICE",
            })
            {
                var f = Path.Combine(cartella, nome);
                try
                {
                    if (File.Exists(f)) { File.Delete(f); racconta("tolto " + nome); }
                }
                catch (Exception ex) { racconta("non riesco a togliere " + nome + ": " + ex.Message); }
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(cartella).Any())
                {
                    Directory.Delete(cartella);
                    racconta("tolta la cartella");
                }
                else
                {
                    racconta("la cartella resta: dentro c'e' anche altro, e non e' mio.");
                }
            }
            catch { }
        }
    }
}
