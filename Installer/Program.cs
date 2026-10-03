using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Oce.Solid.Agent.Installer
{
    internal static class Programma
    {
        /// <summary>
        /// STAThread non e' una formalita': senza, le finestre di dialogo del
        /// sistema — «Sfoglia», e i controlli che usano il COM — si comportano
        /// in modo imprevedibile o si piantano. E' l'errore che si scopre solo
        /// quando qualcuno clicca il bottone sbagliato su una macchina
        /// diversa dalla tua.
        /// </summary>
        [STAThread]
        private static int Main(string[] argomenti)
        {
            argomenti = argomenti ?? new string[0];

            if (argomenti.Any(a => Uguale(a, "--aiuto") || Uguale(a, "-h") || Uguale(a, "/?")))
            {
                Mostra(Aiuto());
                return 0;
            }

            // MODO SILENZIOSO: per chi installa su piu' postazioni, o da uno
            // script di distribuzione. Fa le stesse cose della finestra e
            // scrive su console invece che in un riquadro.
            // TOGLIERE SENZA APRIRE LA FINESTRA.
            // Chi ha installato da script deve poter togliere da script, e
            // la prova che la cartella NON si cancella quando dentro resta
            // roba d'altri va potuta rifare senza un dito sul mouse.
            bool uninstall = argomenti.Any(a => Uguale(a, "--disinstalla"));
            bool verify = argomenti.Any(a => Uguale(a, "--verify"));
            bool silent = argomenti.Any(a => Uguale(a, "--silenzioso") || Uguale(a, "-s"));
            if (uninstall || verify || silent)
            {
                // A REAL CONSOLE, AND A LOG (entry H19, B2): see Output.cs for
                // what attaching does, and the launch forms that were measured.
                Output.AttachIfNeeded();
                var log = Valore(argomenti, "--log");
                if (!string.IsNullOrWhiteSpace(log))
                {
                    try { Output.TeeTo(log); }
                    catch (Exception ex) { Console.WriteLine(ex.Message); return ExitNothingInstalled; }
                }
                try
                {
                    if (uninstall) return Toglie(argomenti);
                    if (verify)
                        return Verify.Run(
                            Valore(argomenti, "--cartella") ?? Passi.CartellaPredefinita,
                            Valore(argomenti, "--redist"),
                            Valore(argomenti, "--config"),
                            argomenti.Any(a => Uguale(a, "--json")));
                    return Silenzioso(argomenti);
                }
                catch (Exception ex)
                {
                    // Nothing that goes wrong becomes a 0 (entry H19, B1).
                    Console.WriteLine("Stopped by an error that was not handled: " + ex.GetType().Name + ": " + ex.Message);
                    return ExitNothingInstalled;
                }
                finally { try { Console.Out.Flush(); } catch { } }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                Application.Run(new Finestra());
                return 0;
            }
            catch (Exception ex)
            {
                // Un installatore che sparisce senza dire niente e' il modo
                // piu' rapido di far concludere che il programma e' rotto.
                MessageBox.Show(
                    "L'installatore si e' fermato per un errore che non ha saputo " +
                    "gestire:\n\n" + ex.Message + "\n\n" + ex.GetType().Name,
                    Passi.NomeProdotto, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        // ── THE EXIT CODES OF A SILENT INSTALL (entry H19, B1) ────────────
        //
        // Until 0.125.0 a silent install exited 0 when no client had been
        // wired at all: the exceptions of Collega were printed and swallowed,
        // and with no client found nothing was printed. INSTALL-FOR-AI told
        // an assistant to read the exit code, and the exit code said
        // "installed". Now, checked in this order:
        //
        //   1  nothing installed: a prerequisite failed ([NO] lines), an
        //      option was wrong, or the copy itself failed
        //   3  the files are installed, but a client that should have been
        //      wired was not (its config is malformed or locked, the claude
        //      command and the file both failed), or no client was found;
        //      the block to paste by hand is printed
        //   2  installed and wired, but the bridge's self-test did not
        //      confirm (its code is not 0, 3 or 5; see Passi.SelfTestAccepted)
        //   0  installed, every client asked for is wired (or --senza-client),
        //      and the self-test confirmed
        //
        // 3 comes before 2 because it is what the install flow meets first,
        // and a client that is not wired never runs the bridge at all.
        private const int ExitInstalled = 0;
        private const int ExitNothingInstalled = 1;
        private const int ExitNotConfirmed = 2;
        private const int ExitClientNotWired = 3;

        // Uninstall: 0 done; 1 a file of ours stayed; 3 the files are gone but
        // a client entry could not be removed.

        private static bool Uguale(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string Valore(string[] argomenti, string nome)
        {
            for (int i = 0; i < argomenti.Length - 1; i++)
                if (Uguale(argomenti[i], nome)) return argomenti[i + 1];
            return null;
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Toglie l'installazione.
        ///
        /// AND THE CLIENT ENTRY, UNLESS --keep-client (entry H19, A5; decision
        /// L0b, the author's default, the owner can change it: H15 point 3).
        /// Until 0.125.0 the command line left the entry and the window
        /// removed it from every client: two behaviours for one gesture. An
        /// entry left behind points at an exe that is gone, and the client
        /// shows a server that fails to start. Only an entry that runs the
        /// bridge in THIS folder is removed (Configurazione.Scollega). With
        /// --config only that file is looked at.
        /// </summary>
        private static int Toglie(string[] argomenti)
        {
            var cartella = Valore(argomenti, "--cartella") ?? Passi.CartellaPredefinita;
            bool keepClient = argomenti.Any(a => Uguale(a, "--keep-client"));
            var configScelto = Valore(argomenti, "--config");
            Action<string> racconta = riga => Console.WriteLine("  " + riga);
            Console.WriteLine(Passi.NomeProdotto + " - disinstallazione");
            Console.WriteLine("  cartella: " + cartella);

            bool entryLeft = false;
            if (keepClient) racconta("--keep-client: the MCP client entries are left as they are");
            else
            {
                foreach (var client in ClientsToTouch(configScelto).Where(c => File.Exists(c.File)))
                {
                    try { Unwire(client, cartella, configScelto, racconta); }
                    catch (Exception ex)
                    {
                        racconta(client.Nome + ": the entry could not be removed: " + ex.Message);
                        entryLeft = true;
                    }
                }
            }

            List<string> left;
            try { left = Passi.Disinstalla(cartella, racconta); }
            catch (Exception ex)
            {
                Console.WriteLine("Disinstallazione fallita: " + ex.Message);
                return 1;
            }
            if (left.Count > 0)
            {
                Console.WriteLine(left.Count + " file(s) could not be removed: " + string.Join(", ", left) +
                                  ". Close the MCP client that runs the bridge, then uninstall again. (exit 1)");
                return 1;
            }
            if (entryLeft)
            {
                Console.WriteLine("The files are removed, but a client entry is still there: see above. (exit 3)");
                return ExitClientNotWired;
            }
            Console.WriteLine("Uninstalled. (exit 0)");
            return 0;
        }

        /// <summary>The clients a run touches: the one file of --config, or those found installed.</summary>
        private static List<Client> ClientsToTouch(string configScelto)
        {
            // CON --config SI COLLEGA QUEL FILE E BASTA.
            // Non si va a cercare quali client sono installati: chi ha
            // indicato un file sa cosa vuole, e andare a toccarne altri
            // sarebbe fare piu' di quello che e' stato chiesto.
            return string.IsNullOrWhiteSpace(configScelto)
                ? Configurazione.ClientTrovati().Where(c => c.Installato).ToList()
                : new List<Client> { new Client { Nome = "file indicato", File = configScelto, Installato = true } };
        }

        /// <summary>
        /// Wires one client: Claude Code through `claude mcp add` when the
        /// command is on PATH and no --config was given (entry H19, B5), the
        /// file otherwise, and the file as well when the command fails.
        /// Throws when the client could not be wired.
        /// </summary>
        internal static void Wire(Client client, string eseguibile, string redist, string weldmentProfiles,
                                    string configScelto, Action<string> racconta)
        {
            if (client.ClaudeCode && string.IsNullOrWhiteSpace(configScelto))
            {
                var claude = ClaudeCli.Find();
                if (claude != null)
                {
                    // The same two promises as the file route (H19 review): a
                    // backup before the file changes, and the variables
                    // already in the entry kept (Configurazione.MergedEnv).
                    // `remove` + `add` rewrite .claude.json and drop the old
                    // entry whole, so both are done here, before them.
                    Configurazione.Backup(client.File, racconta);
                    var env = Configurazione.MergedEnv(
                        Configurazione.ConfiguredEnv(client), redist, weldmentProfiles, racconta);
                    if (ClaudeCli.Register(claude, eseguibile, env, racconta)) return;
                    racconta("Claude Code: writing " + client.File + " instead");
                }
            }
            Configurazione.Collega(client, eseguibile, redist, weldmentProfiles, racconta);
        }

        /// <summary>The other way round: `claude mcp remove` for Claude Code when it can, the file otherwise.</summary>
        internal static void Unwire(Client client, string cartella, string configScelto, Action<string> racconta)
        {
            if (client.ClaudeCode && string.IsNullOrWhiteSpace(configScelto))
            {
                var claude = ClaudeCli.Find();
                var comando = Configurazione.ComeEConfigurato(client);
                if (claude != null && comando != null &&
                    Passi.PercorsiUguali(comando, Path.Combine(cartella, Passi.NomeEseguibile)))
                {
                    Configurazione.Backup(client.File, racconta);
                    if (ClaudeCli.Unregister(claude, racconta)) return;
                }
            }
            Configurazione.Scollega(client, cartella, racconta);
        }

        private static int Silenzioso(string[] argomenti)
        {
            var cartella = Valore(argomenti, "--cartella") ?? Passi.CartellaPredefinita;
            var redist = Valore(argomenti, "--redist") ?? Passi.TrovaRedist();
            bool niente = argomenti.Any(a => Uguale(a, "--senza-client"));
            var configScelto = Valore(argomenti, "--config");
            var weldmentProfilesAsked = Valore(argomenti, "--weldment-profiles");

            Action<string> racconta = riga => Console.WriteLine("  " + riga);

            // SOLO ASCII SU CONSOLE. Ci si attacca alla console di chi ha
            // lanciato il comando, con la sua tabella codici: un trattino lungo
            // o un accento escono come un rombo con un punto interrogativo, e
            // un installatore che stampa caratteri rotti sembra rotto lui.
            Console.WriteLine(Passi.NomeProdotto + " - installazione silenziosa");

            var controlli = Passi.Controlla(redist);
            foreach (var c in controlli)
            {
                var segno = c.Esito == Esito.Bene ? "[ok]" : c.Esito == Esito.Avviso ? "[~] " : "[NO]";
                Console.WriteLine(segno + " " + c.Titolo + ": " + c.Dettaglio);
                if (!string.IsNullOrWhiteSpace(c.CosaFare)) Console.WriteLine("     " + c.CosaFare);
            }
            if (controlli.Any(c => c.Esito == Esito.Male))
            {
                Console.WriteLine("Mi fermo: ci sono cose da sistemare.");
                return ExitNothingInstalled;
            }

            // THE WELDMENT PROFILES (entry H19, A6). Asked for: it must be a
            // folder that exists, checked BEFORE anything is installed, so a
            // typo costs nothing. Not asked for: the folder SOLIDWORKS uses,
            // or the one it installs, when one exists (Passi.FindWeldmentProfiles).
            string weldmentProfiles;
            if (!string.IsNullOrWhiteSpace(weldmentProfilesAsked))
            {
                if (!Directory.Exists(weldmentProfilesAsked))
                {
                    Console.WriteLine("[NO] weldment profiles: " + weldmentProfilesAsked + " is not a folder that exists. " +
                                      "Nothing installed.");
                    return ExitNothingInstalled;
                }
                // Without a trailing backslash, unless it is a drive root: the
                // value goes into JSON and onto a command line.
                weldmentProfiles = Path.GetFullPath(weldmentProfilesAsked);
                if (weldmentProfiles.Length > 3) weldmentProfiles = weldmentProfiles.TrimEnd('\\');
                bool anyProfile = false;
                try { anyProfile = Directory.EnumerateFiles(weldmentProfiles, "*.sldlfp", SearchOption.AllDirectories).Any(); }
                catch { }
                Console.WriteLine((anyProfile ? "[ok] " : "[~]  ") + "weldment profiles: " + weldmentProfiles +
                                  (anyProfile ? "" : " (no .sldlfp file found in it: profili_saldatura will say so)"));
            }
            else if (!niente &&
                     (weldmentProfiles = Configurazione.ExistingWeldmentProfiles(ClientsToTouch(configScelto))) != null)
            {
                // A folder already in the client entry was chosen by someone
                // (H19 review): a reinstall keeps it rather than replace it
                // with the one the installer would find.
                Console.WriteLine("[ok] weldment profiles: " + weldmentProfiles + " (kept from the existing client " +
                                  "entry; another folder with --weldment-profiles <folder>)");
            }
            else
            {
                string source;
                weldmentProfiles = Passi.FindWeldmentProfiles(redist, out source);
                if (weldmentProfiles != null)
                    Console.WriteLine("[ok] weldment profiles: " + weldmentProfiles + " (" + source +
                                      "; another folder with --weldment-profiles <folder>)");
                else
                    Console.WriteLine("[~]  weldment profiles: none found. profili_saldatura will refuse until " +
                                      "PROFILI_SALDATURA is set: install again with --weldment-profiles <folder>");
            }

            string eseguibile;
            try { eseguibile = Passi.Installa(cartella, racconta); }
            catch (Exception ex)
            {
                Console.WriteLine("Installazione fallita: " + ex.Message);
                return ExitNothingInstalled;
            }

            bool clientNotWired = false;
            if (!niente)
            {
                var clienti = ClientsToTouch(configScelto);
                if (clienti.Count == 0)
                {
                    Console.WriteLine("  no MCP client found (Claude Desktop, Claude Code)");
                    clientNotWired = true;
                }
                foreach (var client in clienti)
                {
                    try
                    {
                        Wire(client, eseguibile, redist, weldmentProfiles, configScelto, racconta);
                        // La finestra lo diceva, il modo silenzioso no (voce
                        // H15): un client aperto legge la configurazione solo
                        // all'avvio, e chi installa da script non lo vede.
                        if (Configurazione.EAperto(client))
                            racconta("ATTENZIONE: " + client.Nome + " e' aperto adesso: chiudilo e " +
                                     "riaprilo, se no non legge la configurazione nuova.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  " + client.Nome + ": " + ex.Message);
                        clientNotWired = true;
                    }
                }
                if (clientNotWired)
                {
                    Console.WriteLine();
                    Console.WriteLine(PasteHeader);
                    Console.WriteLine(Configurazione.BloccoDaIncollare(eseguibile, redist, weldmentProfiles));
                }
            }

            int code;
            var detto = Passi.Prova(eseguibile, redist, out code);
            Console.WriteLine(detto);
            Console.WriteLine("self-test: exit " + code + ", " + Passi.SelfTestMeaning(code));

            if (clientNotWired)
            {
                Console.WriteLine("Installed in " + cartella + ", but not every MCP client is wired: paste the " +
                                  "block above, then run Installer.exe --verify. (exit 3)");
                return ExitClientNotWired;
            }
            // «Installato» si dice solo se il giro secco lo conferma, oppure se
            // l'unico motivo per cui non conferma e' che SOLIDWORKS e' chiuso —
            // che non e' un problema di installazione. The exit code of the
            // self-test says which, not its text (entry H19, B3).
            if (Passi.SelfTestAccepted(code))
            {
                Console.WriteLine("Installato in " + cartella);
                return ExitInstalled;
            }
            Console.WriteLine("Installato, ma il giro secco non ha confermato: leggi sopra.");
            return ExitNotConfirmed;
        }

        /// <summary>The line before the block to paste. Checks/test-installer.js looks for it.</summary>
        internal const string PasteHeader =
            "Paste this into the MCP client's configuration file (inside \"mcpServers\" if it has one already):";

        private static void Mostra(string testo)
        {
            // Un eseguibile WinExe non ha una console propria: ci si attacca a
            // quella di chi l'ha lanciato, se c'e'. Senza questo, `--aiuto` da
            // un prompt non stamperebbe niente.
            if (!Output.AttachIfNeeded()) MessageBox.Show(testo, Passi.NomeProdotto);
            else Console.WriteLine(testo);
        }

        private static string Aiuto() =>
            Passi.NomeProdotto + " - installatore\n\n" +
            "  Installer.exe                      apre la finestra\n" +
            "  Installer.exe --silenzioso         installa senza finestra\n" +
            "\n" +
            "  Installer.exe --verify [--json]    checks an installation, as a client would find it\n" +
            "\n" +
            "  --cartella <percorso>   dove installare (predefinito: " +
            Passi.CartellaPredefinita + ")\n" +
            "  --redist <percorso>     la cartella api\\redist di SOLIDWORKS\n" +
            "  --senza-client          non toccare la configurazione dei client MCP\n" +
            "  --config <percorso>     collega QUEL file invece di cercare i client\n" +
            "  --weldment-profiles <folder>  the weldment profiles (.sldlfp) for profili_saldatura,\n" +
            "                          written as PROFILI_SALDATURA in the client's env\n" +
            "  --log <file>            also write the run to this file\n" +
            "  --disinstalla           toglie l'installazione, senza aprire la finestra\n" +
            "  --keep-client           with --disinstalla: leave the MCP client entry in place\n" +
            "  --verify                files, client entries, SOLIDWORKS, the bridge's self-test\n" +
            "                          and an MCP handshake; exit 0 all green, 2 warnings, 1 failed\n" +
            "  --json                  with --verify: one line of JSON\n" +
            "\n" +
            "Exit codes of --silenzioso: 0 installed and wired, 1 nothing installed,\n" +
            "2 installed but the self-test did not confirm, 3 installed but a client\n" +
            "not wired (the block to paste is printed). Read them with\n" +
            "  PowerShell: (Start-Process Installer.exe -ArgumentList '--silenzioso' -Wait -PassThru -NoNewWindow).ExitCode\n" +
            "  cmd, in a batch file: start \"\" /wait Installer.exe --silenzioso, then %ERRORLEVEL%\n" +
            "\n" +
            "Non serve l'amministratore: installa nella cartella dell'utente e non\n" +
            "scrive nel registro di sistema.";
    }
}
