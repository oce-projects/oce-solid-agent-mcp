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
    ///   1. the documents NEVER SAVED (empty path): they are the "PartNN" that
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
    ///     CloseLeftovers.exe --close --spare "Part1,Part2"
    ///
    /// `--spare` serves the bench, and it is the difference between «I close my
    /// leftovers» and «I close everything that is not saved». The bench reads which
    /// documents were there BEFORE starting and names them here: so it closes
    /// what it opened itself and not what somebody else had opened.
    ///
    /// TWO MODES FOR THE AUTONOMOUS RUN OF THE TASKS (Bench/tasks/session-documents.js).
    /// The run must start every session with NO document open, so it has to
    /// close what the session before opened whatever its state: saved files in
    /// the temporary folder, a library file, a drawing. The rule above (never
    /// saved, or under a folder) cannot say that, and the decision of WHAT to
    /// close belongs to the script, where it has a self-test. Here it only acts:
    ///
    ///     CloseLeftovers.exe --list-json                    the open documents as one JSON array
    ///                                                       of {title, path, type}; closes nothing
    ///     CloseLeftovers.exe --close-titles-file <file>     closes exactly the documents whose
    ///                                                       titles the file lists, one per line,
    ///                                                       in that order (changes are discarded, as
    ///                                                       CloseDoc does: never save anything here)
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            bool reallyClose = false;
            bool listJson = false;
            string titlesFile = null;
            var folders = new List<string>();
            var spare = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < arguments.Length; i++)
            {
                var a = arguments[i];
                if (a == "--close") { reallyClose = true; continue; }
                if (a == "--list-json") { listJson = true; continue; }
                if (a == "--close-titles-file" && i + 1 < arguments.Length) { titlesFile = arguments[++i]; continue; }
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

            if (listJson) return ListJson(sw);
            if (titlesFile != null) return CloseTitles(sw, titlesFile);

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
            foreach (var t in kept) Console.WriteLine("  kept:    " + t);
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

        private static string Quote(string s)
        {
            var b = new System.Text.StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }

        private static string TypeName(IModelDoc2 d)
        {
            int t = 0;
            try { t = d.GetType(); } catch { }
            return t == (int)swDocumentTypes_e.swDocPART ? "part"
                 : t == (int)swDocumentTypes_e.swDocASSEMBLY ? "assembly"
                 : t == (int)swDocumentTypes_e.swDocDRAWING ? "drawing" : "other";
        }

        /// <summary>The open documents as one JSON array; nothing is closed.</summary>
        private static int ListJson(ISldWorks sw)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            var items = new List<string>();
            var d = (IModelDoc2)sw.GetFirstDocument();
            while (d != null)
            {
                string t = "", p = "";
                try { t = d.GetTitle() ?? ""; } catch { }
                try { p = d.GetPathName() ?? ""; } catch { }
                items.Add("{\"title\":" + Quote(t) + ",\"path\":" + Quote(p) + ",\"type\":" + Quote(TypeName(d)) + "}");
                d = (IModelDoc2)d.GetNext();
            }
            Console.WriteLine("[" + string.Join(",", items) + "]");
            return 0;
        }

        /// <summary>
        /// Closes exactly the documents whose titles the file lists (one per
        /// line, in the order to close them), then says how many are still
        /// open. CloseDoc answers nothing, so the answer is the count after.
        /// </summary>
        private static int CloseTitles(ISldWorks sw, string file)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            var titles = new List<string>();
            foreach (var line in System.IO.File.ReadAllLines(file, System.Text.Encoding.UTF8))
                if (line.Trim().Length > 0) titles.Add(line.Trim());
            // A SAVED DOCUMENT IS CLOSED BY ITS PATH, the rule of the closing above (D9
            // defect 4): handed a title that matches no open title whole, CloseDoc falls
            // back to the base name and closes the first document of that base name,
            // which may be one the run spares. The path of each listed title is read
            // now, from the open documents; a title with no path (never saved) is
            // closed by that title, matched whole.
            var pathOf = new Dictionary<string, string>(StringComparer.Ordinal);
            var s = (IModelDoc2)sw.GetFirstDocument();
            while (s != null)
            {
                string t = "", p = "";
                try { t = s.GetTitle() ?? ""; } catch { }
                try { p = s.GetPathName() ?? ""; } catch { }
                if (t.Length > 0 && !pathOf.ContainsKey(t)) pathOf[t] = p;
                s = (IModelDoc2)s.GetNext();
            }
            int asked = 0;
            foreach (var t in titles)
            {
                string p;
                if (!pathOf.TryGetValue(t, out p)) { Console.Error.WriteLine("not open: " + t); continue; }
                try { sw.CloseDoc(p.Length > 0 ? p : t); asked++; }
                catch (Exception e) { Console.Error.WriteLine("not closed " + t + ": " + e.Message); }
            }
            int remain = 0;
            var d = (IModelDoc2)sw.GetFirstDocument();
            while (d != null) { remain++; d = (IModelDoc2)d.GetNext(); }
            Console.WriteLine("close asked for " + asked + " of " + titles.Count + "; documents open now: " + remain);
            return 0;
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
