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
//  `EsecutorePiano.ControlliPreliminari` rifiuta un piano se
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

internal static class Programma
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

        IntPtr cornice;
        try { cornice = new IntPtr(((IFrame)sw.Frame()).GetHWnd()); }
        catch (Exception ex) { Console.WriteLine("Frame().GetHWnd() non risponde: " + ex.Message); return 2; }
        uint pid;
        GetWindowThreadProcessId(cornice, out pid);
        Console.WriteLine("  finestra principale 0x" + cornice.ToInt64().ToString("X") + ", processo " + pid);
        Console.WriteLine();
        Console.WriteLine("  giro  modale   IsWindowEnabled (ms)   CommandInProgress (ms)");

        int falsiUser32 = 0, falsiCom = 0, nonTornate = 0;
        for (int giro = 1; giro <= 3; giro++)
        {
            foreach (bool aperto in new[] { false, true })
            {
                Thread filo = null;
                IntPtr dialogo = IntPtr.Zero;
                if (aperto)
                {
                    filo = new Thread(() =>
                    {
                        try
                        {
                            var suo = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
                            suo.SendMsgToUser2("Sonda Z-C3: questa finestra si chiude da sola.",
                                (int)swMessageBoxIcon_e.swMbInformation, (int)swMessageBoxBtn_e.swMbOk);
                        }
                        catch { }
                    });
                    filo.SetApartmentState(ApartmentState.STA);
                    filo.IsBackground = true;
                    filo.Start();
                    var t0 = Stopwatch.StartNew();
                    while (dialogo == IntPtr.Zero && t0.ElapsedMilliseconds < 8000)
                    {
                        Thread.Sleep(100);
                        dialogo = DialogoDi(pid);
                    }
                    if (dialogo == IntPtr.Zero)
                    {
                        Console.WriteLine("  il modale non si e' visto entro 8 s: la sonda non puo' misurare");
                        return 4;
                    }
                    Thread.Sleep(300);
                }

                var c1 = Stopwatch.StartNew();
                bool abilitata = IsWindowEnabled(cornice);
                long msU = c1.ElapsedMilliseconds;

                bool? inCorso = null;
                long msC = -1;
                var lettore = new Thread(() =>
                {
                    try
                    {
                        var c2 = Stopwatch.StartNew();
                        var suo = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
                        inCorso = suo.CommandInProgress;
                        msC = c2.ElapsedMilliseconds;
                    }
                    catch { }
                });
                lettore.SetApartmentState(ApartmentState.STA);
                lettore.IsBackground = true;
                lettore.Start();
                bool tornata = lettore.Join(5000);
                if (!tornata) nonTornate++;

                // ATTESO: aperto -> finestra DISABILITATA e comando in corso.
                if (abilitata == aperto) falsiUser32++;
                if (tornata && inCorso.HasValue && inCorso.Value != aperto) falsiCom++;

                Console.WriteLine("  " + giro + "     " + (aperto ? "APERTO " : "chiuso ") + "  " +
                                  abilitata.ToString().PadRight(6) + " (" + msU + ")".PadRight(10) + "        " +
                                  (!tornata ? "NON TORNATA entro 5000 ms"
                                   : inCorso.HasValue ? inCorso.Value + " (" + msC + ")" : "eccezione"));

                if (aperto)
                {
                    PostMessage(dialogo, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    if (!filo.Join(8000))
                    {
                        Console.WriteLine("  il modale non si chiude: CHIUDILO A MANO. Mi fermo.");
                        return 5;
                    }
                    Thread.Sleep(300);
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("  IsWindowEnabled: " + falsiUser32 + " risposte sbagliate su 6");
        Console.WriteLine("  CommandInProgress: " + falsiCom + " risposte sbagliate su 6, " + nonTornate + " non tornate");
        Console.WriteLine();
        if (falsiUser32 == 0 && falsiCom == 0 && nonTornate == 0)
        {
            Console.WriteLine("REGGE: le due letture concordano coi fatti, e la guardia di oggi (CommandInProgress) scatta.");
            return 0;
        }
        if (falsiUser32 == 0)
        {
            Console.WriteLine("LA GUARDIA DI OGGI NON BASTA: IsWindowEnabled vede il modale, CommandInProgress " +
                              (nonTornate > 0 ? "non torna" : "sbaglia") + ".");
            Console.WriteLine("La guardia va costruita su IsWindowEnabled della finestra principale.");
            return 6;
        }
        Console.WriteLine("NEMMENO IsWindowEnabled distingue: la sonda va ripensata prima di costruire niente.");
        return 7;
    }

    /// <summary>Una finestra di dialogo (#32770) visibile del processo di SOLIDWORKS.</summary>
    private static IntPtr DialogoDi(uint pid)
    {
        IntPtr trovato = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            uint suo;
            GetWindowThreadProcessId(h, out suo);
            if (suo != pid || !IsWindowVisible(h)) return true;
            var nome = new StringBuilder(64);
            GetClassName(h, nome, nome.Capacity);
            if (nome.ToString() == "#32770") { trovato = h; return false; }
            return true;
        }, IntPtr.Zero);
        return trovato;
    }
}
