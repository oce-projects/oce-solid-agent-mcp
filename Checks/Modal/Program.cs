using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  Modal — has anyone ever seen the guard against the modal trigger? (Z-C3)
// =============================================================================
//
//  `PlanExecutor.PreliminaryChecks` refuses a plan if
//  `ISldWorks.CommandInProgress` is true: «SOLIDWORKS e' occupato in un
//  comando». The sentence appears in ONE place of the repository, the line that
//  produces it. No case, no check: an unverified promise.
//
//  HERE A REAL MODAL IS OPENED and we look at what they say, while it is open:
//    - `CommandInProgress`, read via COM — with a maximum time, because a
//      COM call towards a SOLIDWORKS inside a modal loop may not return
//      (it is the trap that the entry names: the guard that inherits the defect
//      that it prevents);
//    - `IsWindowEnabled` on the main window, read from User32: an
//      owner with an open modal is DISABLED, and this reading does not
//      go through SOLIDWORKS, so it cannot hang.
//
//  THE MODAL: `SendMsgToUser2`, a SOLIDWORKS message window. The
//  call does not return as long as the window is open, so it is made from another
//  thread; the probe finds the window (class #32770 of the SOLIDWORKS
//  process) and closes it with WM_CLOSE.
//
//  THREE RUNS, alternating closed / open, with the milliseconds of every reading.
//  The criterion of the entry: the two readings agreeing, 0 false positives and 0 false
//  negatives. If `CommandInProgress` stays false with the modal open, the guard
//  is BLIND — and it is written down.
//
//  USAGE
//      .\Checks\Modal\bin\Modal.exe
// =============================================================================

internal static class Program
{
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc f, IntPtr l);
    private const uint WM_CLOSE = 0x0010;

    private static int Main()
    {
        Console.WriteLine();
        Console.WriteLine("MODALE — CommandInProgress e IsWindowEnabled con un modale aperto (voce Z-C3)");
        Console.WriteLine();

        ISldWorks sw;
        try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch (Exception ex) { Console.WriteLine("SOLIDWORKS non risponde: " + ex.Message); return 2; }

        IntPtr frameBorder;
        try { frameBorder = new IntPtr(((IFrame)sw.Frame()).GetHWnd()); }
        catch (Exception ex) { Console.WriteLine("Frame().GetHWnd() non risponde: " + ex.Message); return 2; }
        uint pid;
        GetWindowThreadProcessId(frameBorder, out pid);
        Console.WriteLine("  finestra principale 0x" + frameBorder.ToInt64().ToString("X") + ", processo " + pid);
        Console.WriteLine();
        Console.WriteLine("  giro  modale   IsWindowEnabled (ms)   CommandInProgress (ms)");

        int bogusUser32 = 0, bogusCom = 0, notReturned = 0;
        for (int benchRun = 1; benchRun <= 3; benchRun++)
        {
            foreach (bool open in new[] { false, true })
            {
                Thread wire = null;
                IntPtr dialog = IntPtr.Zero;
                if (open)
                {
                    wire = new Thread(() =>
                    {
                        try
                        {
                            var its = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
                            its.SendMsgToUser2("Sonda Z-C3: questa finestra si chiude da sola.",
                                (int)swMessageBoxIcon_e.swMbInformation, (int)swMessageBoxBtn_e.swMbOk);
                        }
                        catch { }
                    });
                    wire.SetApartmentState(ApartmentState.STA);
                    wire.IsBackground = true;
                    wire.Start();
                    var t0 = Stopwatch.StartNew();
                    while (dialog == IntPtr.Zero && t0.ElapsedMilliseconds < 8000)
                    {
                        Thread.Sleep(100);
                        dialog = DialogOf(pid);
                    }
                    if (dialog == IntPtr.Zero)
                    {
                        Console.WriteLine("  il modale non si e' visto entro 8 s: la sonda non puo' misurare");
                        return 4;
                    }
                    Thread.Sleep(300);
                }

                var c1 = Stopwatch.StartNew();
                bool enabled = IsWindowEnabled(frameBorder);
                long msU = c1.ElapsedMilliseconds;

                bool? inProgress = null;
                long msC = -1;
                var reader = new Thread(() =>
                {
                    try
                    {
                        var c2 = Stopwatch.StartNew();
                        var its = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
                        inProgress = its.CommandInProgress;
                        msC = c2.ElapsedMilliseconds;
                    }
                    catch { }
                });
                reader.SetApartmentState(ApartmentState.STA);
                reader.IsBackground = true;
                reader.Start();
                bool returned = reader.Join(5000);
                if (!returned) notReturned++;

                // EXPECTED: open -> window DISABLED and command in progress.
                if (enabled == open) bogusUser32++;
                if (returned && inProgress.HasValue && inProgress.Value != open) bogusCom++;

                Console.WriteLine("  " + benchRun + "     " + (open ? "APERTO " : "chiuso ") + "  " +
                                  enabled.ToString().PadRight(6) + " (" + msU + ")".PadRight(10) + "        " +
                                  (!returned ? "NON TORNATA entro 5000 ms"
                                   : inProgress.HasValue ? inProgress.Value + " (" + msC + ")" : "eccezione"));

                if (open)
                {
                    PostMessage(dialog, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    if (!wire.Join(8000))
                    {
                        Console.WriteLine("  il modale non si chiude: CHIUDILO A MANO. Mi fermo.");
                        return 5;
                    }
                    Thread.Sleep(300);
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("  IsWindowEnabled: " + bogusUser32 + " risposte sbagliate su 6");
        Console.WriteLine("  CommandInProgress: " + bogusCom + " risposte sbagliate su 6, " + notReturned + " non tornate");
        Console.WriteLine();
        if (bogusUser32 == 0 && bogusCom == 0 && notReturned == 0)
        {
            Console.WriteLine("REGGE: le due letture concordano coi fatti, e la guardia di oggi (CommandInProgress) scatta.");
            return 0;
        }
        if (bogusUser32 == 0)
        {
            Console.WriteLine("LA GUARDIA DI OGGI NON BASTA: IsWindowEnabled vede il modale, CommandInProgress " +
                              (notReturned > 0 ? "non torna" : "sbaglia") + ".");
            Console.WriteLine("La guardia va costruita su IsWindowEnabled della finestra principale.");
            return 6;
        }
        Console.WriteLine("NEMMENO IsWindowEnabled distingue: la sonda va ripensata prima di costruire niente.");
        return 7;
    }

    /// <summary>A visible dialog window (#32770) of the SOLIDWORKS process.</summary>
    private static IntPtr DialogOf(uint pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            uint its;
            GetWindowThreadProcessId(h, out its);
            if (its != pid || !IsWindowVisible(h)) return true;
            var itemName = new StringBuilder(64);
            GetClassName(h, itemName, itemName.Capacity);
            if (itemName.ToString() == "#32770") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
