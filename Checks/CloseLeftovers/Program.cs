using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.CloseLeftovers
{
    /// <summary>
    /// CLOSES THE LEFTOVER DOCUMENTS LEFT OPEN BY THE CHECKS.
    ///
    /// Every check plan opens a new part and does not close it. After a few
    /// dozen checks SOLIDWORKS has forty or fifty documents in
    /// memory, and it is not only waste: **every state read walks through all of them**,
    /// at 244 ms of `GetNext()` each. It is the real reason why a plan of
    /// half a second of work took eighty-five.
    ///
    /// WHAT IT CLOSES, AND WHAT IT DOES NOT TOUCH.
    /// Only two categories, and the second is read from the command line:
    ///
    ///   1. the documents NEVER SAVED (empty path): they are the "ParteNN" that
    ///      are born from the checks and do not exist on disk;
    ///   2. those whose path starts with one of the folders passed as an
    ///      argument — the session scratchpad, typically.
    ///
    /// Everything else stays open. A document saved somewhere else
    /// is not our stuff, and closing it without asking would be the quickest
    /// way to make somebody lose work.
    ///
    /// IT SAVES NOTHING. `CloseDoc` discards unsaved changes, and it is
    /// exactly what is wanted for a test piece. That is why the
    /// first category is limited to documents WITHOUT a path: a real file
    /// with unsaved changes never ends up in this list.
    ///
    ///     CloseLeftovers.exe                       only lists
    ///     CloseLeftovers.exe --close [folders]    really closes
    ///     CloseLeftovers.exe --close --spare "Parte1,Parte2"
    ///
    /// `--spare` serves the bench, and it is the difference between «I close my
    /// leftovers» and «I close everything that is not saved». The bench reads which
    /// documents were there BEFORE starting and names them here: so it closes
    /// what it opened itself and not what somebody else had opened.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            bool reallyClose = false;
            var folders = new List<string>();
            var spare = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < arguments.Length; i++)
            {
                var a = arguments[i];
                if (a == "--close") { reallyClose = true; continue; }
                if (a == "--spare" && i + 1 < arguments.Length)
                {
                    foreach (var t in arguments[++i].Split(','))
                        if (t.Trim().Length > 0) spare.Add(t.Trim());
                    continue;
                }
                folders.Add(a.Replace('/', '\\').TrimEnd('\\') + "\\");
            }

            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException)
            {
                Console.Error.WriteLine("SOLIDWORKS is not open: nothing to close.");
                return 2;
            }
            var sw = (ISldWorks)raw;

            // THE LIST IS MADE FIRST, THEN WE CLOSE.
            // Closing while walking the chain of documents breaks it: the
            // next GetNext() starts from a document that is no longer there.
            var toClose = new List<Candidate>();
            var kept = new List<string>();
            var doc = (IModelDoc2)sw.GetFirstDocument();
            while (doc != null)
            {
                string docTitle = doc.GetTitle();
                string filePath = doc.GetPathName() ?? "";
                if (spare.Contains(docTitle))
                    kept.Add(docTitle + "  (spared: it was there before)");
                else if (Deviation(filePath, folders)) toClose.Add(new Candidate { Title = docTitle, Path = filePath });
                else kept.Add(docTitle + (filePath == "" ? "" : "  " + filePath));
                doc = (IModelDoc2)doc.GetNext();
            }

            Console.WriteLine(toClose.Count + " to close, " + kept.Count + " kept.");
            foreach (var t in kept) Console.WriteLine("  tenuto:  " + t);
            if (!reallyClose)
            {
                foreach (var t in toClose) Console.WriteLine("  would be closed:  " + t.Title);
                Console.WriteLine("None closed: --close is missing.");
                return 0;
            }

            // CLOSED BY PATH WHEN THERE IS ONE (D9 defect 4, 6/10/2026).
            //
            // ISldWorks~CloseDoc.html says «Name of document» and not which name. Measured
            // (Checks/D9Defects, subcommands names, close and closing; SOLIDWORKS 29.5):
            //   - the full path is exact, whatever else is open;
            //   - a title is matched whole first. Handed a name WITHOUT the extension that
            //     matches no title, CloseDoc falls back to the base name and closes the FIRST
            //     open document of that base name, in whatever case, whatever its folder or
            //     type: with twin.SLDPRT and twin.SLDASM open from two folders,
            //     CloseDoc("twin") closed the part and left the assembly;
            //   - two documents with the same title cannot be open together (OpenDoc6 answers
            //     65536, swFileWithSameTitleAlreadyOpen), and the titles GetTitle gives on this
            //     machine, with the extension and the sheet of a drawing, matched whole in
            //     every desk the probe built. So the harm of closing by title was NOT
            //     reproduced here: it would need a title that matches nothing whole (a machine that
            //     hides the extensions, a title that changed between the listing and the
            //     closing), and then the fallback picks a document by base name.
            // A saved document is therefore closed by its path, which cannot be read as
            // anything else. One that was never saved has only its title, and it is closed by
            // that, matched whole.
            int done = 0, notClosed = 0;
            foreach (var t in toClose)
            {
                try { sw.CloseDoc(t.Path != "" ? t.Path : t.Title); done++; }
                catch (Exception e) { Console.Error.WriteLine("  not closed " + t.Title + ": " + e.Message); notClosed++; }
            }
            Console.WriteLine("closed: " + done + (notClosed > 0 ? ", not closed: " + notClosed : ""));

            // THE CHECK THAT REALLY HAPPENED. The documents are counted again:
            // CloseDoc returns nothing, and without recounting «closed» cannot be told
            // apart from «called».
            int remain = 0;
            var d2 = (IModelDoc2)sw.GetFirstDocument();
            while (d2 != null) { remain++; d2 = (IModelDoc2)d2.GetNext(); }
            Console.WriteLine("open documents now: " + remain);
            return 0;
        }

        private sealed class Candidate
        {
            public string Title;
            public string Path;      // "" for a document that was never saved
        }

        private static bool Deviation(string filePath, List<string> folders)
        {
            if (string.IsNullOrEmpty(filePath)) return true;   // never saved
            string p = filePath.Replace('/', '\\');
            foreach (var c in folders)
                if (p.StartsWith(c, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
