using System;
using System.IO;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// TEMPLATES OPENED FROM A LOCAL COPY, when whoever runs the library asks for it
    /// (29/10/2026, asked by the owner to make the bench faster).
    ///
    /// ── WHY ────────────────────────────────────────────────────────────
    ///
    /// The templates of a workshop often live on a network share, and every drawing,
    /// sheet and table opens one. On 28/10/2026 a new sheet with its format on the
    /// share took 3-47 s, with a local copy of the same file 0.33-0.55 s (probes
    /// t55-t58, without the bridge). The bench creates hundreds of them in one run.
    /// The bench already copied its own part template (Bench/Program.cs,
    /// `LocalModel`, 23/9/2026); the templates the OPERATIONS open were still read
    /// from the share.
    ///
    /// ── HOW ────────────────────────────────────────────────────────────
    ///
    /// `OCE_LOCAL_COPIES` = «folder on the share|local folder». A path under the first
    /// folder is OPENED from the same place under the second, if the file (or folder)
    /// is there; otherwise the path is left as it is, and the share is read as before.
    /// Unset, nothing changes. The preferences of SOLIDWORKS are never written: a run
    /// that dies halfway would leave the user's SOLIDWORKS pointing at the copy.
    ///
    /// ── AND THE ANSWERS DO NOT SEE IT ──────────────────────────────────
    ///
    /// What the operations REPORT (the template of a drawing, the format of a sheet,
    /// a table template) goes back to the path on the share with <see cref="Original"/>.
    /// A drawing made from the local template still names the share in its first
    /// sheet; a second sheet made from the local format would name the copy, and
    /// `title_block` would call the two formats different because of where the bench
    /// runs, not because of the drawing.
    /// </summary>
    public static class LocalCopies
    {
        /// <summary>The environment variable that turns the copies on.</summary>
        public const string Variable = "OCE_LOCAL_COPIES";

        /// <summary>The path to open: the local copy of <paramref name="path"/>, or the path itself.</summary>
        public static string Of(string path)
        {
            return Map(path, Environment.GetEnvironmentVariable(Variable));
        }

        /// <summary>The path to report: the share for a path under the local folder, or the path itself.</summary>
        public static string Original(string path)
        {
            return Unmap(path, Environment.GetEnvironmentVariable(Variable));
        }

        /// <summary>
        /// <see cref="Of"/> with the value of the variable given: the contract suite reads
        /// it without touching the environment of the process.
        /// </summary>
        internal static string Map(string path, string setting)
        {
            string share, local;
            if (!Split(setting, out share, out local)) return path;
            string copy = Moved(path, share, local);
            if (copy == null) return path;
            try { return File.Exists(copy) || Directory.Exists(copy) ? copy : path; }
            catch { return path; }
        }

        /// <summary><see cref="Original"/> with the value of the variable given; pure.</summary>
        internal static string Unmap(string path, string setting)
        {
            string share, local;
            if (!Split(setting, out share, out local)) return path;
            return Moved(path, local, share) ?? path;
        }

        private static bool Split(string setting, out string share, out string local)
        {
            share = local = null;
            if (string.IsNullOrWhiteSpace(setting)) return false;
            int bar = setting.IndexOf('|');
            if (bar <= 0) return false;
            share = setting.Substring(0, bar).Trim().TrimEnd('\\', '/');
            local = setting.Substring(bar + 1).Trim().TrimEnd('\\', '/');
            return share.Length > 0 && local.Length > 0;
        }

        /// <summary>The same path under <paramref name="to"/>, or null when it is not under <paramref name="from"/>.</summary>
        private static string Moved(string path, string from, string to)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string p = path.Trim();
            if (string.Equals(p.TrimEnd('\\', '/'), from, StringComparison.OrdinalIgnoreCase)) return to;
            if (!p.StartsWith(from + "\\", StringComparison.OrdinalIgnoreCase)) return null;
            return to + p.Substring(from.Length);
        }
    }
}
