using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Oce.Solid.Agent.Bridge
{
    /// <summary>
    /// WHERE IT IS ALLOWED TO WRITE.
    ///
    /// Some operations take a path and write over it: saving the
    /// part, saving the drawing, exporting the PDF. That path comes from the
    /// MODEL, that is from generated text: it is not a trusted input, and it is
    /// the only point of the bridge that touches the disk outside the open document.
    ///
    /// Without a check, «save in ..\..\..\Windows\System32\something» is
    /// a request like any other. No malice is needed for it to do damage:
    /// a plausible and wrong path is enough.
    ///
    /// THREE RULES, IN ORDER OF SEVERITY.
    ///
    /// 1. A ROOT, IF THERE IS ONE. With `RISCONTRO_RADICE` set, it writes only
    ///    under that folder and nowhere else. It is the configuration to use when
    ///    the bridge runs with nobody watching.
    ///
    /// 2. FORBIDDEN FOLDERS, ALWAYS. The system folders and the installation of
    ///    SOLIDWORKS are never touched, not even without a root set. Not
    ///    because someone would ask for it on purpose, but because a wrong relative
    ///    path ends up there on its own.
    ///
    /// 3. THE COMPANY LIBRARIES, if declared in `RISCONTRO_SOLA_LETTURA`.
    ///    It is a rule born from a fact: when inserting a structural member,
    ///    SOLIDWORKS opens by itself the profile library file and leaves it
    ///    marked as modified. From there any save rewrites the
    ///    library of everyone. Declaring those folders read-only is
    ///    cheaper than noticing afterwards.
    ///
    /// The message always says WHICH rule stopped the write and what to
    /// do: a refusal that cannot be legitimately worked around is as much a defect
    /// as one permission too many.
    /// </summary>
    internal static class Paths
    {
        /// <summary>The argument names that contain a path that is written to.</summary>
        private static readonly string[] WritingFields =
        {
            "path", "file", "percorso_pdf", "percorso_file", "destinazione",
        };

        /// <summary>
        /// Checks all the paths inside the arguments of an operation.
        /// Returns null if all is well, otherwise the reason for the refusal.
        /// </summary>
        public static string Check(Dictionary<string, object> arguments)
        {
            if (arguments == null) return null;
            foreach (var theField in WritingFields)
            {
                object v;
                if (!arguments.TryGetValue(theField, out v) || v == null) continue;
                var filePath = Convert.ToString(v);
                if (string.IsNullOrWhiteSpace(filePath)) continue;

                var reason = CheckOne(filePath, theField);
                if (reason != null) return reason;
            }
            return null;
        }

        public static string CheckOne(string filePath, string theField = "path")
        {
            string full;
            try
            {
                // Path.GetFullPath resolves «..» and mixed separators: it is the
                // step that turns a path into what it would really
                // touch. It must be done BEFORE any comparison, otherwise a
                // string is compared and something elsewhere is written.
                full = Path.GetFullPath(filePath);
            }
            catch (Exception ex)
            {
                return "Il percorso «" + filePath + "» non e' un percorso valido (" +
                       ex.Message + ").";
            }

            // No streams, no devices, no undeclared network paths: they are all things
            // that a save path has never needed
            // to be.
            if (full.IndexOf(':') != full.LastIndexOf(':'))
                return "Il percorso «" + filePath + "» contiene due volte i due punti: " +
                       "non lo tocco.";

            var root = Variable("RISCONTRO_RADICE");
            if (!string.IsNullOrWhiteSpace(root))
            {
                if (!Below(full, root))
                    return "Posso scrivere solo dentro «" + root + "» (lo dice la " +
                           "variabile RISCONTRO_RADICE di questa installazione), e «" +
                           full + "» e' fuori. Scegli un percorso li' dentro.";
            }

            foreach (var forbidden in Forbidden())
            {
                if (Below(full, forbidden))
                    return "Non scrivo dentro «" + forbidden + "»: e' una cartella di " +
                           "sistema o di programma. «" + full + "» finisce li'. " +
                           "Scegli una cartella di lavoro, per esempio dentro Documenti.";
            }

            foreach (var single in ReadOnly())
            {
                if (Below(full, single))
                    return "«" + single + "» e' dichiarata di sola lettura su questa " +
                           "installazione (variabile RISCONTRO_SOLA_LETTURA): di " +
                           "solito sono le librerie condivise, e riscriverle tocca il " +
                           "lavoro di tutti. «" + full + "» finisce li'.";
            }

            return null;
        }

        // ─────────────────────────────────────────────────────────────────

        private static string Variable(string itemName)
        {
            try { return Environment.GetEnvironmentVariable(itemName); }
            catch { return null; }
        }

        private static IEnumerable<string> Forbidden()
        {
            var outside = new List<string>();
            foreach (var folder in new[]
            {
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.System,
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.CommonApplicationData,
            })
            {
                try
                {
                    var p = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrWhiteSpace(p)) outside.Add(p);
                }
                catch { }
            }
            return outside;
        }

        /// <summary>
        /// Folders declared read-only, separated by semicolons.
        /// Example: `RISCONTRO_SOLA_LETTURA=S:\CAD;S:\_LIBRERIE`
        /// </summary>
        private static IEnumerable<string> ReadOnly()
        {
            var v = Variable("RISCONTRO_SOLA_LETTURA");
            if (string.IsNullOrWhiteSpace(v)) return Enumerable.Empty<string>();
            return v.Split(';')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));
        }

        /// <summary>
        /// Is `pieno` inside `folder`?
        ///
        /// The trailing separator is not pedantry: without it, «C:\Dati2» turns out to be
        /// inside «C:\Dati», and a check that says yes when it is no is a
        /// check that is not there.
        /// </summary>
        private static bool Below(string full, string folder)
        {
            try
            {
                var root = Path.GetFullPath(folder)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var p = Path.GetFullPath(full);
                return p.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(
                           p.TrimEnd(Path.DirectorySeparatorChar),
                           root.TrimEnd(Path.DirectorySeparatorChar),
                           StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
