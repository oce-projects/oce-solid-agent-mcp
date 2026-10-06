using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace Oce.Solid.Agent.Installer
{
    /// <summary>How a check went: green, yellow, red.</summary>
    internal enum Outcome { Fine, Warning, Male }

    internal sealed class CheckItem
    {
        public string Title;
        public Outcome Outcome;
        public string Detail;
        /// <summary>What whoever reads must do, if there is something to do.</summary>
        public string WhatToDo;
    }

    /// <summary>
    /// THE CHECKS AND THE INSTALLATION, WITHOUT INTERFACE.
    ///
    /// It is kept apart from the window on purpose: so the same logic can
    /// be run without a window (`Installer.exe /silenzioso`) and, above all, it
    /// can be read without having to read the code of the buttons too.
    ///
    /// THE RULE OF THIS FILE: a check that fails must say WHAT TO DO,
    /// not only that it failed. «MSBuild not found» sends one to a search
    /// engine; «install Build Tools for Visual Studio 2022, workload
    /// .NET desktop development» gets the work going again. It is the same rule
    /// that holds for the error messages of the drawing operations.
    /// </summary>
    internal static class Steps
    {
        public const string ProductName = "OCE Solid Agent";
        public const string ExecutableName = "oce-solid-agent-mcp.exe";
        public const string CatalogName = "catalog.json";
        /// <summary>
        /// The drawing operations. **It is in the payload**, not looked for on the
        /// machine: up to 0.5.0 it was `OpenPDM.SwAddin.dll`, private
        /// code that the package could not contain, and the installer
        /// had to go and look for it. Now it has it inside.
        /// </summary>
        public const string LibraryName = "Oce.Solid.Agent.Core.dll";

        /// <summary>
        /// The files that the installation puts in the folder, and therefore the ONLY ones
        /// that the uninstall removes.
        ///
        /// ONE SINGLE LIST FOR BOTH DIRECTIONS (entry H15). Before, the uninstall
        /// had its own, and looked in it for `Oce.Solid.Agent.pdb` — a file that
        /// no version ever installed: up to 0.27.0 it was called
        /// `SolidWorksMcp.pdb`, and in both cases `Install` never
        /// extracted it. Two lists that should say the same thing drift apart
        /// the day one of them is touched.
        /// </summary>
        public static readonly string[] InstalledFiles =
        {
            ExecutableName, ExecutableName + ".config", CatalogName, LibraryName,
            "LICENSE", "NOTICE",
        };

        /// <summary>
        /// Files the BRIDGE writes beside itself while it runs, which no
        /// install puts there (entry H19, A5). Today one: the log of questions
        /// that had no operation (Tools/QuestionRegistry.cs, file name
        /// QuestionRegistry.FileName; OCE_QUESTION_REGISTRY can move it
        /// elsewhere, and then it is not ours to remove). Left behind, it kept
        /// the folder alive with the message that the folder held someone
        /// else's files, which was not true.
        /// </summary>
        public static readonly string[] BridgeOwnFiles = { "questions-without-answer.jsonl" };

        // ── THE EXIT CODES OF THE BRIDGE'S --self-test (entry H19, B3) ─────────
        // The same numbers as Program.SelfTest* in the bridge, which this
        // assembly cannot reference; Oce.Solid.Agent.ContractTests reads both and compares.
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

        /// <summary>Where to install, if whoever installs says nothing else.</summary>
        public static string DefaultFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Oce.Solid.Agent");

        public static string ThisExeFolder =>
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";

        // ─────────────────────────────────────────────────────────────────
        // The checks
        // ─────────────────────────────────────────────────────────────────

        public static List<CheckItem> Check(string redist)
        {
            var outside = new List<CheckItem>
            {
                CheckFramework(),
                CheckSolidWorks(redist),
                CheckPayload(),
                CheckLibrary(),
                CheckSolidWorksOpen(),
            };
            return outside;
        }

        private static CheckItem CheckFramework()
        {
            // .NET Framework 4.8 comes with Windows 10 since 1903. On older
            // machines it has to be installed by hand, and it is the only real prerequisite.
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
                return new CheckItem
                {
                    Title = ".NET Framework 4.8",
                    Outcome = Outcome.Fine,
                    Detail = "release " + release,
                };

            // If we got to run this code, a .NET is there. So
            // the «zero» case is «I cannot read it», not «it is not there».
            if (release == 0)
                return new CheckItem
                {
                    Title = ".NET Framework 4.8",
                    Outcome = Outcome.Warning,
                    Detail = "I cannot read its version from the registry",
                    WhatToDo = "If the installation fails, this is probably why.",
                };

            return new CheckItem
            {
                Title = ".NET Framework 4.8",
                Outcome = Outcome.Male,
                Detail = "installed release " + release + ", at least 528040 is needed",
                WhatToDo = "Download .NET Framework 4.8 from " +
                           "https://dotnet.microsoft.com/download/dotnet-framework/net48",
            };
        }

        public static string FindRedist()
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
                if (FolderHasInterop(fromRoot)) return fromRoot;
            }
            var its = Environment.GetEnvironmentVariable("SW_REDIST");
            if (!string.IsNullOrWhiteSpace(its) && FolderHasInterop(its)) return its;

            var candidates = new List<string>();
            foreach (var pf in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            })
            {
                if (string.IsNullOrWhiteSpace(pf)) continue;
                candidates.Add(Path.Combine(pf, @"SOLIDWORKS Corp\SOLIDWORKS\api\redist"));
                candidates.Add(Path.Combine(pf, @"SolidWorks Corp\SolidWorks\api\redist"));
            }

            // Also the installations by year: SOLIDWORKS 2024, 2025...
            foreach (var pf in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            })
            {
                if (string.IsNullOrWhiteSpace(pf)) continue;
                try
                {
                    foreach (var d in Directory.GetDirectories(pf, "SOLIDWORKS*"))
                        candidates.Add(Path.Combine(d, @"SOLIDWORKS\api\redist"));
                }
                catch { }
            }

            foreach (var c in candidates)
                if (FolderHasInterop(c)) return c;
            return null;
        }

        /// <summary>The major version of SOLIDWORKS tested: 29 = 2021 (SP5.1).</summary>
        public const int TestedVersion = 29;

        /// <summary>«29.5.1.1» -> 29; 0 if it cannot be read. 29 is 2021: the year is 1992 + the major.</summary>
        public static int MainVersion(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return 0;
            int point = v.IndexOf('.');
            int n;
            return int.TryParse(point > 0 ? v.Substring(0, point) : v, out n) ? n : 0;
        }

        public static bool FolderHasInterop(string folder)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(folder) &&
                       File.Exists(Path.Combine(folder, "SolidWorks.Interop.sldworks.dll"));
            }
            catch { return false; }
        }

        private static CheckItem CheckSolidWorks(string redist)
        {
            if (string.IsNullOrWhiteSpace(redist)) redist = FindRedist();

            if (FolderHasInterop(redist))
            {
                string v = "";
                try
                {
                    v = FileVersionInfo.GetVersionInfo(
                        Path.Combine(redist, "SolidWorks.Interop.sldworks.dll")).FileVersion;
                }
                catch { }
                // THE TESTED VERSION IS DECLARED (entry H17, decision of the owner of
                // 27/9/2026): the bridge is measured only on SOLIDWORKS 2021 SP5.1
                // (interop 29.5.1). A different version is not blocked: a warning is given.
                // The older ones can be tried when needed, the newer ones cannot
                // (the license on which we test does not install them).
                int main = MainVersion(v);
                string anno = main > 0 ? (main + 1992).ToString() : null;
                bool tested = main == TestedVersion;
                return new CheckItem
                {
                    Title = "SOLIDWORKS installed",
                    Outcome = tested || main <= 0 ? Outcome.Fine : Outcome.Warning,
                    Detail = redist + (string.IsNullOrWhiteSpace(v) ? "" : "  (interop " + v +
                                (anno == null ? "" : ", SOLIDWORKS " + anno) + ")"),
                    WhatToDo = tested || main <= 0 ? null :
                        "The bridge is only tested on SOLIDWORKS 2021 SP5.1. With " + anno + " it installs just the " +
                        "same, but nobody has measured it: " + (main > TestedVersion
                            ? "it is newer than the one tested, and some APIs may have moved."
                            : "it is older than the one tested; you can try, and if something goes wrong, say so."),
                };
            }

            return new CheckItem
            {
                Title = "SOLIDWORKS installed",
                Outcome = Outcome.Male,
                Detail = "cannot find the api\\redist folder",
                // There is no Browse button for SOLIDWORKS, in the window or
                // anywhere else (entry H19, A7): the text named one.
                WhatToDo =
                    "SOLIDWORKS must be installed on this machine: the interoperability libraries " +
                    "are taken from there and are not included here. " +
                    "If it is installed in a non-standard folder, pass --redist <its api\\redist folder>, " +
                    "or set SW_INSTALL_PATH to its root (the folder that ends in \\SOLIDWORKS).",
            };
        }

        private static CheckItem CheckPayload()
        {
            if (Payload.Embedded)
                return new CheckItem
                {
                    Title = "Bridge to install",
                    Outcome = Outcome.Fine,
                    Detail = "included in this installer",
                };

            var nextTo = Path.Combine(ThisExeFolder, ExecutableName);
            if (File.Exists(nextTo))
                return new CheckItem
                {
                    Title = "Bridge to install",
                    Outcome = Outcome.Fine,
                    Detail = "taken from the installer's folder",
                };

            return new CheckItem
            {
                Title = "Bridge to install",
                Outcome = Outcome.Male,
                Detail = "cannot find " + ExecutableName,
                WhatToDo =
                    "This installer does not have the bridge inside and cannot find it " +
                    "next to itself. If you cloned the repository, build first " +
                    "with build.ps1: it produces a complete Installer.exe.",
            };
        }

        /// <summary>
        /// The library that is NEXT TO this executable, if there is one.
        ///
        /// It is NOT looked for around the machine any more, and it is not a
        /// simplification: looking for it would mean installing, on a
        /// machine that happens to have an old copy somewhere,
        /// THAT copy — with the operations of two weeks ago and nobody
        /// to say so. The payload has it, and that one is the good one.
        ///
        /// It serves only the case of whoever launches the installer just compiled
        /// from the `bin` folder, where the payload is not yet embedded.
        /// </summary>
        public static string FindLibraryNextTo()
        {
            try
            {
                var c = Path.Combine(ThisExeFolder, LibraryName);
                if (File.Exists(c)) return Path.GetFullPath(c);
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Are the drawing operations there? Since 0.6.0 the answer is almost
        /// always «yes, I have them inside»: it is a statement of fact, no longer a
        /// question to whoever installs.
        ///
        /// The case that remains is the installer launched from the `bin` folder
        /// just compiled, where the payload is not yet embedded.
        /// </summary>
        private static CheckItem CheckLibrary()
        {
            if (Payload.Contains(LibraryName))
                return new CheckItem
                {
                    Title = "Drawing operations",
                    Outcome = Outcome.Fine,
                    Detail = LibraryName + " is inside the installer",
                };

            var nextTo = FindLibraryNextTo();
            if (!string.IsNullOrWhiteSpace(nextTo))
            {
                string v = "";
                try { v = FileVersionInfo.GetVersionInfo(nextTo).FileVersion; }
                catch { }
                return new CheckItem
                {
                    Title = "Drawing operations",
                    Outcome = Outcome.Fine,
                    Detail = nextTo + (string.IsNullOrWhiteSpace(v) ? "" : "  (" + v + ")"),
                };
            }

            // IT IS BLOCKING NOW, AND BEFORE IT WAS NOT.
            // When the operations lived in a private DLL, not having them was
            // normal and the installation made sense all the same: the bridge
            // started and said it was not attached. Now the operations
            // are part of the product: if they are missing, the package is broken, and a
            // broken package that gets installed is discovered by whoever uses it.
            return new CheckItem
            {
                Title = "Drawing operations",
                Outcome = Outcome.Male,
                Detail = LibraryName + " is neither inside nor next to it",
                WhatToDo =
                    "This installer is incomplete. If you cloned the " +
                    "repository, build with build.ps1: it produces an Installer.exe " +
                    "with the bridge and the operations inside.",
            };
        }

        private static CheckItem CheckSolidWorksOpen()
        {
            bool open = false;
            try { open = Process.GetProcessesByName("SLDWORKS").Length > 0; }
            catch { }

            if (open)
                return new CheckItem
                {
                    Title = "SOLIDWORKS open right now",
                    Outcome = Outcome.Fine,
                    Detail = "the final check will be able to look at the document",
                };

            return new CheckItem
            {
                Title = "SOLIDWORKS open right now",
                Outcome = Outcome.Warning,
                Detail = "closed",
                WhatToDo =
                    "It is not needed to install. It is needed to use the bridge: it " +
                    "attaches to a session that already exists, it does not start one.",
            };
        }

        // ─────────────────────────────────────────────────────────────────
        // The installation
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Copies the bridge into the chosen folder. Returns the path
        /// of the installed executable, or throws with a readable reason.
        /// </summary>
        public static string Install(
            string folder, Action<string> tell)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new Exception("You did not tell me where to install.");

            Directory.CreateDirectory(folder);
            tell("folder: " + folder);

            // 1. the bridge and the catalog
            //
            // FROM WHICH VERSION TO WHICH (entry H15). Whoever reinstalls wants to know
            // whether they are updating or putting back the same one: `VERSION.txt` is read
            // inside the executable that is there BEFORE overwriting it, and inside
            // the new one AFTER. Neither reading is a promise: if
            // one fails, it says «version not readable».
            var executable = Path.Combine(folder, ExecutableName);
            string old = File.Exists(executable) ? VersionOf(executable) ?? "unreadable version" : null;
            Payload.Extract(ExecutableName, executable, tell);
            string newOne = VersionOf(executable) ?? "unreadable version";
            if (old == null) tell("installing " + newOne);
            else if (old == newOne) tell("reinstalling " + newOne + " (it was already this one)");
            else tell("updating from " + old + " to " + newOne);
            Payload.Extract(CatalogName, Path.Combine(folder, CatalogName), tell);
            Payload.ExtractIfPresent(
                ExecutableName + ".config",
                Path.Combine(folder, ExecutableName + ".config"), tell);

            // 2. THE OPERATIONS. First from the payload; if the payload does not have them
            //    (installer launched from the bin folder just
            //    compiled), from next to the executable. If neither
            //    the one nor the other is there, it THROWS: an installed bridge that does not
            //    draw is worse than an installation that stops.
            //    `Extract` looks first inside the payload and then next
            //    to the installer, and throws if neither the one nor the other is there.
            //    It throws and rightly so: an installed bridge that does not draw is
            //    worse than an installation that stops saying what is missing.
            var library = Path.Combine(folder, LibraryName);
            Payload.Extract(LibraryName, library, tell);

            // 3. the legal notices travel with the program
            //
            // A new file extracted here also goes into `InstalledFiles`, otherwise the
            // uninstall leaves it there forever.
            foreach (var itemName in new[] { "LICENSE", "NOTICE" })
                Payload.ExtractIfPresent(itemName, Path.Combine(folder, itemName), tell);

            if (!File.Exists(executable))
                throw new Exception(
                    "I finished without errors but " + ExecutableName + " is not there. " +
                    "I do not call it a success.");

            return executable;
        }

        /// <summary>
        /// The version written in `VERSION.txt` inside a bridge
        /// executable, or null if it cannot be read.
        ///
        /// It is loaded from the BYTES, not from the file: `LoadFrom` and `ReflectionOnlyLoadFrom`
        /// keep the file open until exit, and the executable just read
        /// is exactly the one to overwrite. The version of the Win32 file is not
        /// needed: it says 0.0.0.0 (entry H14).
        /// </summary>
        public static string VersionOf(string executable)
        {
            try
            {
                var a = Assembly.Load(File.ReadAllBytes(executable));
                foreach (var itemName in a.GetManifestResourceNames())
                {
                    if (!itemName.EndsWith("VERSION.txt", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var s = a.GetManifestResourceStream(itemName))
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

        public static bool EqualPaths(string a, string b)
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
        /// The dry run: `--self-test` touches nothing, it attaches and tells.
        /// It is the only way to say «installed» knowing that it is true.
        ///
        /// THE EXIT CODE DECIDES, NOT THE TEXT (entry H19, B3). Until 0.125.0
        /// success was a phrase of the bridge, or the phrase that said
        /// SOLIDWORKS was not running; the second one also comes out when
        /// SOLIDWORKS runs as administrator and the client does not, and both
        /// were going to be translated. The text is still returned, for
        /// whoever reads it.
        /// </summary>
        public static string Test(string executable, string redist, out int code)
        {
            code = SelfTestNoAnswer;
            try
            {
                var startup = new ProcessStartInfo(executable, "--self-test")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executable),
                };
                if (!string.IsNullOrWhiteSpace(redist))
                {
                    // The folder the installer decided on is the one the
                    // bridge uses: SW_INSTALL_PATH would win over SW_REDIST.
                    startup.EnvironmentVariables.Remove("SW_INSTALL_PATH");
                    startup.EnvironmentVariables["SW_REDIST"] = redist;
                }

                using (var p = Process.Start(startup))
                {
                    // `--self-test` writes to stderr on purpose: stdout belongs
                    // to the protocol and nothing else goes there.
                    var err = p.StandardError.ReadToEndAsync();
                    var mid = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(60000))
                    {
                        try { p.Kill(); } catch { }
                        return "The bridge did not answer within a minute.";
                    }
                    code = p.ExitCode;
                    return (err.Result + mid.Result).Trim();
                }
            }
            catch (Exception ex)
            {
                return "I cannot run the bridge: " + ex.Message;
            }
        }

        /// <summary>
        /// Removes the installed files, and the files the bridge writes beside
        /// itself. It does not touch the client configuration. Returns the files
        /// of ours that could NOT be removed (entry H19, A5): until 0.125.0 a
        /// locked file was mentioned in a line and the uninstall still exited 0.
        /// </summary>
        public static List<string> Uninstall(string folder, Action<string> tell)
        {
            var left = new List<string>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                tell("nothing to remove in " + folder);
                return left;
            }

            // ONLY the files that this installer puts are deleted. A
            // `Directory.Delete(recursive)` on a folder chosen by whoever
            // installs is the classic way of deleting someone's desktop
            // because of a badly pasted path.
            foreach (var itemName in InstalledFiles.Concat(BridgeOwnFiles))
            {
                var f = Path.Combine(folder, itemName);
                try
                {
                    if (File.Exists(f)) { File.Delete(f); tell("removed " + itemName); }
                }
                catch (Exception ex)
                {
                    tell("cannot remove " + itemName + ": " + ex.Message);
                    left.Add(itemName);
                }
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                    tell("removed the folder");
                }
                else if (left.Count == 0)
                {
                    tell("the folder stays: there is other stuff inside, and it is not mine.");
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
        /// Since library 0.112 OCE_WELDMENT_PROFILES has no default, and without it
        /// weldment_profiles refuses. Two places were checked on the machine
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
                    if (FolderHasInterop(redist))
                        major = MainVersion(FileVersionInfo.GetVersionInfo(
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
                try { if (FolderHasInterop(redist)) swRoot = Directory.GetParent(redist.TrimEnd('\\'))?.Parent?.FullName; } catch { }
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
