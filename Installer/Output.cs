using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Oce.Solid.Agent.Installer
{
    /// <summary>
    /// WHERE THE OUTPUT OF A RUN WITHOUT THE WINDOW GOES (entry H19, B2).
    ///
    /// Installa.exe is a Windows (GUI) program, so that a double click does
    /// not open a black console behind the window. The price: started from a
    /// shell it has no console of its own, and until 0.126.0 the silent and
    /// uninstall modes printed into nothing. Only --aiuto attached to the
    /// parent's console. The installer's tests passed because they read the
    /// output through pipes, which is the one way nobody types by hand.
    ///
    /// Now every mode without the window does what --aiuto did: if standard
    /// output is NOT already a file or a pipe, it attaches to the console of
    /// the process that started it. When it is a file or a pipe (a script
    /// reading the output, node's spawnSync, `> file`), it is left alone:
    /// attaching would not take the handle away, but there is nothing to gain.
    ///
    /// ATTACHING IS NOT WAITING: whether the shell waits for the program and
    /// sees its exit code depends on how it was started, not on this class.
    /// The forms that were measured are in the comment below.
    ///
    /// --log &lt;file&gt; writes the same lines into a file as well (UTF-8): the
    /// one record that survives whatever the shell does with the console.
    /// </summary>
    //
    // THE LAUNCH FORMS, MEASURED (entry H19, B2). On 1-2 October 2026, each
    // form run in a fresh console window with no redirection anywhere, on
    // 0.125.0 with a run that stops at the prerequisites (exit 1) and on
    // 0.126.0 with a malformed client config (exit 3). The exit codes seen
    // were the same on both versions; the console output was not:
    //
    //   form                                                    exit code seen        output on the console
    //   PowerShell: & .\Installa.exe ...                        none: not waited for  0.126.0 yes (after the prompt); 0.125.0 none
    //   PowerShell: Start-Process -Wait -PassThru -NoNewWindow  right ($p.ExitCode)   0.126.0 yes; 0.125.0 none
    //   PowerShell: cmd /c Installa.exe ...                     right                 0.126.0 yes; 0.125.0 none
    //   PowerShell: cmd /c "start "" /wait Installa.exe ..."    WRONG: 0 both times   0.126.0 yes; 0.125.0 none
    //   cmd, batch file: Installa.exe ... then %ERRORLEVEL%     right                 0.126.0 yes; 0.125.0 none
    //   cmd, batch file: start "" /wait Installa.exe ...        right                 0.126.0 yes; 0.125.0 none
    //   PowerShell: & .\Installa.exe ... | Out-Null             right (a pipe waits)  into the pipe
    //
    // The forms INSTALL-FOR-AI gives: Start-Process -Wait -PassThru
    // -NoNewWindow for PowerShell, `start "" /wait` inside a batch file for
    // cmd. The one-line `cmd /c "start /wait ..."` must not be used.
    // Controlli/prova-installatore.js launches the first two exactly so (B2).
    internal static class Output
    {
        private const int StdOutput = -11;
        private const int FileTypeDisk = 1;
        private const int FileTypePipe = 3;

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int which);

        [DllImport("kernel32.dll")]
        private static extern int GetFileType(IntPtr handle);

        /// <summary>True when standard output already goes to a file or a pipe.</summary>
        public static bool Redirected()
        {
            try
            {
                var h = GetStdHandle(StdOutput);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) return false;
                int type = GetFileType(h);
                return type == FileTypeDisk || type == FileTypePipe;
            }
            catch { return false; }
        }

        /// <summary>
        /// Attaches to the parent's console unless the output is already
        /// redirected. Returns true when there is somewhere to write: a
        /// redirection, or a console attached.
        /// </summary>
        public static bool AttachIfNeeded()
        {
            if (Redirected()) return true;
            try { return AttachConsole(-1); }
            catch { return false; }
        }

        /// <summary>
        /// From now on Console.Out writes to the console and to <paramref name="file"/>.
        /// Throws with a readable reason if the file cannot be created: a log
        /// that was asked for and silently not written is worse than an error.
        /// </summary>
        public static void TeeTo(string file)
        {
            StreamWriter w;
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(file));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                w = new StreamWriter(file, false, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                throw new Exception("Cannot write the log file \"" + file + "\": " + ex.Message);
            }
            Console.SetOut(new Tee(Console.Out, w));
        }

        private sealed class Tee : TextWriter
        {
            private readonly TextWriter _a, _b;
            public Tee(TextWriter a, TextWriter b) { _a = a; _b = b; }
            public override Encoding Encoding => _a.Encoding;
            public override void Write(char value) { _a.Write(value); _b.Write(value); }
            public override void Write(string value) { _a.Write(value); _b.Write(value); }
            public override void WriteLine(string value) { _a.WriteLine(value); _b.WriteLine(value); }
            public override void Flush() { _a.Flush(); _b.Flush(); }
            protected override void Dispose(bool disposing)
            {
                if (disposing) { try { _b.Dispose(); } catch { } }
                base.Dispose(disposing);
            }
        }
    }
}
