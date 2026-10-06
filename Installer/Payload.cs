using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Oce.Solid.Agent.Installer
{
    /// <summary>
    /// THE BRIDGE INSIDE THE INSTALLER.
    ///
    /// The goal is **a single file**: whoever installs downloads `Installer.exe`,
    /// double click, done. No zip to unpack, no «put this
    /// next to that», no folder that gets lost halfway.
    ///
    /// So `oce-solid-agent-mcp.exe` and `catalog.json` travel as EMBEDDED
    /// RESOURCES inside this executable and are written to disk at the
    /// moment of installation.
    ///
    /// TWO MODES, AND THE SECOND SERVES WHOEVER DEVELOPS.
    /// If the resources are not there — because the installer just
    /// compiled is being run inside `bin\`, without having gone through `build.ps1` —
    /// the files next to the executable are taken. So the same installer
    /// works both as a package to distribute and as a tool for whoever
    /// works on it, without two roads to keep aligned.
    ///
    /// THE ADD-IN IS NOT HERE. `OpenPDM.SwAddin.dll` is not embedded nor will it ever
    /// be in a public package: it is private code, and a public installer
    /// that carries it around publishes it. It is looked for on the machine of
    /// whoever installs, and if it is not there, it says what is missing.
    /// </summary>
    internal static class Payload
    {
        private const string Prefix = "Oce.Solid.Agent.Installer.Payload.";

        private static string[] Risorse =>
            Assembly.GetExecutingAssembly().GetManifestResourceNames();

        /// <summary>Is at least the bridge there, embedded?</summary>
        public static bool Embedded =>
            Risorse.Any(r => r.EndsWith(
                Steps.ExecutableName, StringComparison.OrdinalIgnoreCase));

        private static string ResourceName(string file)
        {
            // It is looked for by suffix instead of by exact name: MSBuild
            // builds the name of the resource from the path, and one folder more
            // or less would change the string without anyone
            // noticing until the installation.
            return Risorse.FirstOrDefault(r =>
                r.EndsWith("." + file, StringComparison.OrdinalIgnoreCase) ||
                r.Equals(Prefix + file, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Is the file there? It looks where <see cref="ExtractIfPresent"/> would look:
        /// first inside the payload, then next to the installer.
        ///
        /// It serves the checks, which must be able to say «I have it» **before**
        /// writing anything to disk: a check that has to install in order to
        /// answer is not a check.
        /// </summary>
        public static bool Contains(string file)
        {
            if (ResourceName(file) != null) return true;
            try { return File.Exists(Path.Combine(Steps.ThisExeFolder, file)); }
            catch { return false; }
        }

        /// <summary>Writes the file, or throws saying why it cannot.</summary>
        public static void Extract(string file, string destination, Action<string> tell)
        {
            if (!ExtractIfPresent(file, destination, tell))
                throw new Exception(
                    "I cannot find \"" + file + "\": it is neither inside the installer nor " +
                    "next to it. The installer is incomplete.");
        }

        /// <summary>Writes the file if it is there. Returns false if it was not.</summary>
        public static bool ExtractIfPresent(
            string file, string destination, Action<string> tell)
        {
            // 1. embedded
            var itemName = ResourceName(file);
            if (itemName != null)
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(itemName))
                {
                    if (s != null)
                    {
                        WriteWithCare(s, destination);
                        tell("written " + file + " (" + Byte(destination) + ")");
                        return true;
                    }
                }
            }

            // 2. next to the installer
            var nextTo = Path.Combine(Steps.ThisExeFolder, file);
            if (File.Exists(nextTo))
            {
                if (Steps.EqualPaths(nextTo, destination))
                {
                    tell(file + " is already in place");
                    return true;
                }
                using (var s = File.OpenRead(nextTo)) WriteWithCare(s, destination);
                tell("copied " + file + " (" + Byte(destination) + ")");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Writes a temporary file first and then renames.
        ///
        /// If the installation dies halfway — full disk, antivirus, power —
        /// what is left on the disk is the whole old version, not a
        /// truncated new one. A truncated executable does not give a readable error:
        /// it gives «could not load file or assembly», which sends one looking for
        /// a missing dependency that is not missing.
        /// </summary>
        private static void WriteWithCare(Stream source, string destination)
        {
            var folder = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            var temporary = destination + ".partial";
            try
            {
                using (var f = File.Create(temporary)) source.CopyTo(f);
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temporary, destination);
            }
            catch (UnauthorizedAccessException)
            {
                throw new Exception(
                    "I do not have permission to write in \"" + folder + "\". " +
                    "Choose another folder, for example inside Documents.");
            }
            catch (IOException ex) when (File.Exists(destination))
            {
                throw new Exception(
                    "\"" + Path.GetFileName(destination) + "\" is in use: the bridge is probably " +
                    "running inside an open MCP client. Close the " +
                    "client and try again. (" + ex.Message + ")");
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch { }
            }
        }

        private static string Byte(string file)
        {
            try
            {
                long n = new FileInfo(file).Length;
                if (n < 1024) return n + " B";
                if (n < 1024 * 1024) return (n / 1024) + " kB";
                return (n / (1024 * 1024)) + " MB";
            }
            catch { return "?"; }
        }
    }
}
