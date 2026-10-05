using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// =============================================================================
//  Modale — la guardia contro il modale l'ha mai vista scattare qualcuno? (Z-C3)
// =============================================================================
//
//  `PlanExecutor.PreliminaryChecks` rifiuta un piano se
//  `ISldWorks.CommandInProgress` e' vero: «SOLIDWORKS e' occupato in un
//  comando». La frase compare in UN posto del repository, la riga che la
//  produce. Nessun caso, nessuna prova: una promessa non verificata.
//
//  QUI SI APRE UN MODALE VERO e si guarda cosa dicono, mentre e' aperto:
//    - `CommandInProgress`, letto via COM — con un tempo massimo, perche' una
//      chiamata COM verso un SOLIDWORKS dentro un ciclo modale puo' non tornare
//      (e' la trappola che la voce nomina: la guardia che eredita il difetto
//      che previene);
//    - `IsWindowEnabled` sulla finestra principale, letto da User32: un
//      proprietario con un modale aperto e' DISABILITATO, e questa lettura non
//      passa da SOLIDWORKS, quindi non si puo' appendere.
//
//  IL MODALE: `SendMsgToUser2`, una finestra di messaggio di SOLIDWORKS. La
//  chiamata non torna finche' la finestra e' aperta, quindi la si fa da un
//  altro filo; la sonda trova la finestra (classe #32770 del processo di
//  SOLIDWORKS) e la chiude con WM_CLOSE.
//
//  TRE GIRI, alternando chiuso / aperto, con i millisecondi di ogni lettura.
//  Il criterio della voce: le due letture concordi, 0 falsi positivi e 0 falsi
//  negativi. Se `CommandInProgress` resta falso col modale aperto, la guardia
//  e' CIECA — e lo si scrive.
//
//  USO
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

                // ATTESO: aperto -> finestra DISABILITATA e comando in corso.
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

    /// <summary>Una finestra di dialogo (#32770) visibile del processo di SOLIDWORKS.</summary>
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
