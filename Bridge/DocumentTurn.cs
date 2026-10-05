using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Oce.Solid.Agent.Bridge
{
    /// <summary>
    /// ONE BRIDGE AT A TIME ON THE SAME DOCUMENT (entry W2.9).
    ///
    /// ── THE PRECEDENT, WHICH HAS ALREADY BEEN PAID FOR ───────────────────
    ///
    /// On 14 September 2026 a 314x274x52 block was extruded **inside
    /// someone else's part**: two sessions on the same SOLIDWORKS, and the
    /// active document had changed under their feet. No error, no
    /// warning: the plan succeeded, on the wrong part.
    ///
    /// That time it was repaired by keeping the `IModelDoc2` reference instead
    /// of asking for the active one every time. **Inside one process that is enough.** Between two
    /// bridge PROCESSES — two MCP clients open together, which is the most
    /// normal thing in the world — that reference protects nothing: they are two different
    /// COM objects looking at the same file, and each believes it is
    /// alone.
    ///
    /// ── WHY A MUTEX AND NOT A LOCK FILE ───────────────────────────────────
    ///
    /// A lock file outlives whoever created it: if the bridge dies, the file
    /// stays, and the document becomes unusable until someone
    /// deletes it by hand. A system mutex is **released by Windows** when the
    /// process ends, however it ends. A padlock that stays closed after
    /// whoever closed it is dead is not a protection, it is a fault.
    ///
    /// ── `Local\` AND NOT `Global\`, AND IT MUST BE SAID ──────────────────
    ///
    /// The name lives in the space of the current Windows session. It covers the
    /// real case — two MCP clients on the same desk — and **does not** cover two different
    /// users or a service: there `Global\` would be needed, but it wants privileges that
    /// a bridge launched by an MCP client does not have, and a padlock that sometimes
    /// cannot be closed is worse than one whose reach is known.
    /// **The limit is written here instead of being discovered.**
    /// </summary>
    internal sealed class DocumentTurn : IDisposable
    {
        private Mutex _mutex;

        private DocumentTurn(Mutex m) { _mutex = m; }

        /// <summary>
        /// Takes the turn on the document, or returns `null` if another has
        /// it. `whose` says what was found, for the message.
        ///
        /// ZERO WAIT, ON PURPOSE. Waiting would mean the client
        /// hangs without knowing why, and the bridge already has a problem with
        /// calls that do not return (entry W2.1). Better an immediate refusal
        /// that says what is going on: whoever reads can decide for themselves whether to
        /// wait.
        /// </summary>
        public static DocumentTurn Take(object doc, out string whose)
        {
            whose = "";
            string key = KeyOf(doc, out string descriptionText);
            whose = descriptionText;
            if (key == null)
            {
                // It was not possible to know which document is meant: it is NOT
                // blocked. A lock on a wrong key would block documents
                // different from each other and let through those that are
                // the same — that is, it would do the opposite of what is needed.
                return new DocumentTurn(null);
            }

            Mutex m = null;
            try
            {
                bool our;
                m = new Mutex(false, @"Local\OceSolidAgent_" + key, out our);
                if (!m.WaitOne(0))
                {
                    m.Dispose();
                    return null;
                }
                return new DocumentTurn(m);
            }
            catch (AbandonedMutexException)
            {
                // A dead process left it without releasing it: it is OURS
                // now, and it is not an error. Windows reports it only once
                // precisely to say «the previous one did not close the door».
                return new DocumentTurn(m);
            }
            catch
            {
                // Any other trouble in taking the lock must NOT
                // prevent working: the bridge worked even before this
                // existed. A new defense that becomes a new cause
                // of refusal is a worsening.
                try { if (m != null) m.Dispose(); } catch { }
                return new DocumentTurn(null);
            }
        }

        /// <summary>
        /// The key of the document: the path if there is one, otherwise the title.
        ///
        /// It goes through a SHA-1 and the path is not used as it is: mutex names
        /// do not accept the backslash (it separates the namespace) and have a
        /// length limit. A long path, truncated, would make two documents
        /// in the same deep folder collide — and two different documents
        /// that block each other are a defect that shows up only when
        /// someone really works.
        /// </summary>
        private static string KeyOf(object doc, out string descriptionText)
        {
            descriptionText = "";
            if (doc == null) return null;
            string filePath = "", docTitle = "";
            try { filePath = Ask(doc, "GetPathName") ?? ""; } catch { }
            try { docTitle = Ask(doc, "GetTitle") ?? ""; } catch { }

            string which = !string.IsNullOrWhiteSpace(filePath)
                ? filePath.ToLowerInvariant()
                : docTitle;
            if (string.IsNullOrWhiteSpace(which)) return null;

            descriptionText = !string.IsNullOrWhiteSpace(filePath) ? filePath : docTitle;

            using (var sha = SHA1.Create())
            {
                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(which));
                var s = new StringBuilder(b.Length * 2);
                foreach (var x in b) s.Append(x.ToString("x2", CultureInfo.InvariantCulture));
                return s.ToString();
            }
        }

        private static string Ask(object obj, string method)
        {
            return obj.GetType().InvokeMember(
                method, System.Reflection.BindingFlags.InvokeMethod,
                null, obj, null) as string;
        }

        public void Dispose()
        {
            if (_mutex == null) return;
            try { _mutex.ReleaseMutex(); } catch { }
            try { _mutex.Dispose(); } catch { }
            _mutex = null;
        }
    }
}
