using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Oce.Solid.Agent.Installer
{
    internal static class Program
    {
        /// <summary>
        /// STAThread is not a formality: without it, the system dialog
        /// windows — «Browse», and the controls that use COM — behave
        /// unpredictably or hang. It is the error that is found only when
        /// someone clicks the wrong button on a machine
        /// different from yours.
        /// </summary>
        [STAThread]
        private static int Main(string[] arguments)
        {
            arguments = arguments ?? new string[0];

            if (arguments.Any(a => Equal(a, "--help") || Equal(a, "-h") || Equal(a, "/?")))
            {
                Show(Help());
                return 0;
            }

            // SILENT MODE: for whoever installs on several workstations, or from a
            // distribution script. It does the same things as the window and
            // writes to the console instead of to a box.
            // REMOVE WITHOUT OPENING THE WINDOW.
            // Whoever installed from a script must be able to remove from a script, and
            // the proof that the folder is NOT deleted when there remains
            // stuff belonging to others inside must be repeatable without a finger on the mouse.
            bool uninstall = arguments.Any(a => Equal(a, "--uninstall"));
            bool verify = arguments.Any(a => Equal(a, "--verify"));
            bool silent = arguments.Any(a => Equal(a, "--silent") || Equal(a, "-s"));
            if (uninstall || verify || silent)
            {
                // A REAL CONSOLE, AND A LOG (entry H19, B2): see Output.cs for
                // what attaching does, and the launch forms that were measured.
                Output.AttachIfNeeded();
                var log = Value(arguments, "--log");
                if (!string.IsNullOrWhiteSpace(log))
                {
                    try { Output.TeeTo(log); }
                    catch (Exception ex) { Console.WriteLine(ex.Message); return ExitNothingInstalled; }
                }
                try
                {
                    if (uninstall) return Remove(arguments);
                    if (verify)
                        return Verify.Run(
                            Value(arguments, "--dir") ?? Steps.DefaultFolder,
                            Value(arguments, "--redist"),
                            Value(arguments, "--config"),
                            arguments.Any(a => Equal(a, "--json")));
                    return Silent(arguments);
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
                Application.Run(new Window());
                return 0;
            }
            catch (Exception ex)
            {
                // An installer that disappears without saying anything is the quickest
                // way to make people conclude that the program is broken.
                MessageBox.Show(
                    "The installer stopped because of an error it could not " +
                    "handle:\n\n" + ex.Message + "\n\n" + ex.GetType().Name,
                    Steps.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        // ── THE EXIT CODES OF A SILENT INSTALL (entry H19, B1) ────────────
        //
        // Until 0.125.0 a silent install exited 0 when no client had been
        // wired at all: the exceptions of Connect were printed and swallowed,
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
        //      confirm (its code is not 0, 3 or 5; see Steps.SelfTestAccepted)
        //   0  installed, every client asked for is wired (or --no-client),
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

        private static bool Equal(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string Value(string[] arguments, string itemName)
        {
            for (int i = 0; i < arguments.Length - 1; i++)
                if (Equal(arguments[i], itemName)) return arguments[i + 1];
            return null;
        }

        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Removes the installation.
        ///
        /// AND THE CLIENT ENTRY, UNLESS --keep-client (entry H19, A5; decision
        /// L0b, the author's default, the owner can change it: H15 point 3).
        /// Until 0.125.0 the command line left the entry and the window
        /// removed it from every client: two behaviours for one gesture. An
        /// entry left behind points at an exe that is gone, and the client
        /// shows a server that fails to start. Only an entry that runs the
        /// bridge in THIS folder is removed (Configuration.Disconnect). With
        /// --config only that file is looked at.
        /// </summary>
        private static int Remove(string[] arguments)
        {
            var folder = Value(arguments, "--dir") ?? Steps.DefaultFolder;
            bool keepClient = arguments.Any(a => Equal(a, "--keep-client"));
            var chosenConfig = Value(arguments, "--config");
            Action<string> tell = line => Console.WriteLine("  " + line);
            Console.WriteLine(Steps.ProductName + " - uninstall");
            Console.WriteLine("  folder: " + folder);

            bool entryLeft = false;
            if (keepClient) tell("--keep-client: the MCP client entries are left as they are");
            else
            {
                foreach (var client in ClientsToTouch(chosenConfig).Where(c => File.Exists(c.File)))
                {
                    try { Unwire(client, folder, chosenConfig, tell); }
                    catch (Exception ex)
                    {
                        tell(client.Name + ": the entry could not be removed: " + ex.Message);
                        entryLeft = true;
                    }
                }
            }

            List<string> left;
            try { left = Steps.Uninstall(folder, tell); }
            catch (Exception ex)
            {
                Console.WriteLine("Uninstall failed: " + ex.Message);
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
        private static List<Client> ClientsToTouch(string chosenConfig)
        {
            // WITH --config THAT FILE IS WIRED AND THAT IS ALL.
            // It does not go looking for which clients are installed: whoever
            // indicated a file knows what they want, and going to touch others
            // would be doing more than what was asked.
            return string.IsNullOrWhiteSpace(chosenConfig)
                ? Configuration.FoundClients().Where(c => c.Installed).ToList()
                : new List<Client> { new Client { Name = "chosen file", File = chosenConfig, Installed = true } };
        }

        /// <summary>
        /// Wires one client: Claude Code through `claude mcp add` when the
        /// command is on PATH and no --config was given (entry H19, B5), the
        /// file otherwise, and the file as well when the command fails.
        /// Throws when the client could not be wired.
        /// </summary>
        internal static void Wire(Client client, string executable, string redist, string weldmentProfiles,
                                    string chosenConfig, Action<string> tell)
        {
            if (client.ClaudeCode && string.IsNullOrWhiteSpace(chosenConfig))
            {
                var claude = ClaudeCli.Find();
                if (claude != null)
                {
                    // The same two promises as the file route (H19 review): a
                    // backup before the file changes, and the variables
                    // already in the entry kept (Configuration.MergedEnv).
                    // `remove` + `add` rewrite .claude.json and drop the old
                    // entry whole, so both are done here, before them.
                    Configuration.Backup(client.File, tell);
                    var env = Configuration.MergedEnv(
                        Configuration.ConfiguredEnv(client), redist, weldmentProfiles, tell);
                    if (ClaudeCli.Register(claude, executable, env, tell)) return;
                    tell("Claude Code: writing " + client.File + " instead");
                }
            }
            Configuration.Connect(client, executable, redist, weldmentProfiles, tell);
        }

        /// <summary>The other way round: `claude mcp remove` for Claude Code when it can, the file otherwise.</summary>
        internal static void Unwire(Client client, string folder, string chosenConfig, Action<string> tell)
        {
            if (client.ClaudeCode && string.IsNullOrWhiteSpace(chosenConfig))
            {
                var claude = ClaudeCli.Find();
                var command = Configuration.HowItIsConfigured(client);
                if (claude != null && command != null &&
                    Steps.EqualPaths(command, Path.Combine(folder, Steps.ExecutableName)))
                {
                    Configuration.Backup(client.File, tell);
                    if (ClaudeCli.Unregister(claude, tell)) return;
                }
            }
            Configuration.Disconnect(client, folder, tell);
        }

        private static int Silent(string[] arguments)
        {
            var folder = Value(arguments, "--dir") ?? Steps.DefaultFolder;
            var redist = Value(arguments, "--redist") ?? Steps.FindRedist();
            bool nothing = arguments.Any(a => Equal(a, "--no-client"));
            var chosenConfig = Value(arguments, "--config");
            var weldmentProfilesAsked = Value(arguments, "--weldment-profiles");

            Action<string> tell = line => Console.WriteLine("  " + line);

            // ASCII ONLY ON THE CONSOLE. We attach to the console of whoever
            // launched the command, with its code page: a long dash
            // or an accent come out as a diamond with a question mark, and
            // an installer that prints broken characters looks broken itself.
            Console.WriteLine(Steps.ProductName + " - silent install");

            var checks = Steps.Check(redist);
            foreach (var c in checks)
            {
                var sign = c.Outcome == Outcome.Fine ? "[ok]" : c.Outcome == Outcome.Warning ? "[~] " : "[NO]";
                Console.WriteLine(sign + " " + c.Title + ": " + c.Detail);
                if (!string.IsNullOrWhiteSpace(c.WhatToDo)) Console.WriteLine("     " + c.WhatToDo);
            }
            if (checks.Any(c => c.Outcome == Outcome.Bad))
            {
                Console.WriteLine("I stop here: there are things to fix.");
                return ExitNothingInstalled;
            }

            // THE WELDMENT PROFILES (entry H19, A6). Asked for: it must be a
            // folder that exists, checked BEFORE anything is installed, so a
            // typo costs nothing. Not asked for: the folder SOLIDWORKS uses,
            // or the one it installs, when one exists (Steps.FindWeldmentProfiles).
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
                                  (anyProfile ? "" : " (no .sldlfp file found in it: weldment_profiles will say so)"));
            }
            else if (!nothing &&
                     (weldmentProfiles = Configuration.ExistingWeldmentProfiles(ClientsToTouch(chosenConfig))) != null)
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
                weldmentProfiles = Steps.FindWeldmentProfiles(redist, out source);
                if (weldmentProfiles != null)
                    Console.WriteLine("[ok] weldment profiles: " + weldmentProfiles + " (" + source +
                                      "; another folder with --weldment-profiles <folder>)");
                else
                    Console.WriteLine("[~]  weldment profiles: none found. weldment_profiles will refuse until " +
                                      "OCE_WELDMENT_PROFILES is set: install again with --weldment-profiles <folder>");
            }

            string executable;
            try { executable = Steps.Install(folder, tell); }
            catch (Exception ex)
            {
                Console.WriteLine("Install failed: " + ex.Message);
                return ExitNothingInstalled;
            }

            bool clientNotWired = false;
            if (!nothing)
            {
                var clients = ClientsToTouch(chosenConfig);
                if (clients.Count == 0)
                {
                    Console.WriteLine("  no MCP client found (Claude Desktop, Claude Code)");
                    clientNotWired = true;
                }
                foreach (var client in clients)
                {
                    try
                    {
                        Wire(client, executable, redist, weldmentProfiles, chosenConfig, tell);
                        // The window said it, the silent mode did not (entry
                        // H15): an open client reads the configuration only at
                        // startup, and whoever installs from a script does not see it.
                        if (Configuration.IsOpen(client))
                            tell("WARNING: " + client.Name + " is open right now: close it and " +
                                     "reopen it, or it will not read the new configuration.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  " + client.Name + ": " + ex.Message);
                        clientNotWired = true;
                    }
                }
                if (clientNotWired)
                {
                    Console.WriteLine();
                    Console.WriteLine(PasteHeader);
                    Console.WriteLine(Configuration.BlockToPaste(executable, redist, weldmentProfiles));
                }
            }

            int code;
            var said = Steps.Test(executable, redist, out code);
            Console.WriteLine(said);
            Console.WriteLine("self-test: exit " + code + ", " + Steps.SelfTestMeaning(code));

            if (clientNotWired)
            {
                Console.WriteLine("Installed in " + folder + ", but not every MCP client is wired: paste the " +
                                  "block above, then run Installer.exe --verify. (exit 3)");
                return ExitClientNotWired;
            }
            // «Installed» is said only if the dry run confirms it, or if
            // the only reason why it does not confirm is that SOLIDWORKS is closed —
            // which is not an installation problem. The exit code of the
            // self-test says which, not its text (entry H19, B3).
            if (Steps.SelfTestAccepted(code))
            {
                Console.WriteLine("Installed in " + folder);
                return ExitInstalled;
            }
            Console.WriteLine("Installed, but the dry run did not confirm: read above.");
            return ExitNotConfirmed;
        }

        /// <summary>The line before the block to paste. Checks/test-installer.js looks for it.</summary>
        internal const string PasteHeader =
            "Paste this into the MCP client's configuration file (inside \"mcpServers\" if it has one already):";

        private static void Show(string text)
        {
            // A WinExe executable does not have a console of its own: it attaches to
            // that of whoever launched it, if there is one. Without this, `--help` from
            // a prompt would print nothing.
            if (!Output.AttachIfNeeded()) MessageBox.Show(text, Steps.ProductName);
            else Console.WriteLine(text);
        }

        private static string Help() =>
            Steps.ProductName + " - installer\n\n" +
            "  Installer.exe                      opens the window\n" +
            "  Installer.exe --silent             installs without a window\n" +
            "\n" +
            "  Installer.exe --verify [--json]    checks an installation, as a client would find it\n" +
            "\n" +
            "  --dir <path>            where to install (default: " +
            Steps.DefaultFolder + ")\n" +
            "  --redist <path>         the api\\redist folder of SOLIDWORKS\n" +
            "  --no-client             do not touch the configuration of the MCP clients\n" +
            "  --config <path>         wire THAT file instead of looking for the clients\n" +
            "  --weldment-profiles <folder>  the weldment profiles (.sldlfp) for weldment_profiles,\n" +
            "                          written as OCE_WELDMENT_PROFILES in the client's env\n" +
            "  --log <file>            also write the run to this file\n" +
            "  --uninstall             removes the installation, without opening the window\n" +
            "  --keep-client           with --uninstall: leave the MCP client entry in place\n" +
            "  --verify                files, client entries, SOLIDWORKS, the bridge's self-test\n" +
            "                          and an MCP handshake; exit 0 all green, 2 warnings, 1 failed\n" +
            "  --json                  with --verify: one line of JSON\n" +
            "  --help                  this list\n" +
            "\n" +
            "Exit codes of --silent: 0 installed and wired, 1 nothing installed,\n" +
            "2 installed but the self-test did not confirm, 3 installed but a client\n" +
            "not wired (the block to paste is printed). Read them with\n" +
            "  PowerShell: (Start-Process Installer.exe -ArgumentList '--silent' -Wait -PassThru -NoNewWindow).ExitCode\n" +
            "  cmd, in a batch file: start \"\" /wait Installer.exe --silent, then %ERRORLEVEL%\n" +
            "\n" +
            "No administrator needed: it installs in the user's folder and does not\n" +
            "write to the system registry.";
    }
}
