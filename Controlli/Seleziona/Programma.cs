using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Controlli.Seleziona
{
    /// <summary>
    /// SELEZIONA QUALCOSA NEL PEZZO, come farebbe un dito sullo schermo.
    ///
    /// Serve a provare che lo stato sappia dire **cosa e' selezionato**: il
    /// criterio della voce E9 parla di un click in interfaccia, e un click non
    /// si puo' scrivere in un banco. Quello che si puo' fare — e che prova la
    /// stessa strada — e' selezionare per API e poi chiedere allo stato come si
    /// chiama: `ISelectionMgr` non sa da dove arrivi la selezione.
    ///
    ///     Seleziona.exe faccia 1        la prima faccia del primo corpo
    ///     Seleziona.exe spigolo 1       il primo spigolo
    ///     Seleziona.exe corpo 1         il primo corpo
    ///     Seleziona.exe niente          svuota la selezione
    ///
    /// I numeri sono quelli dell'ordine in cui SOLIDWORKS da' le entita', NON
    /// gli identificativi dello stato: sono due numerazioni diverse, ed e'
    /// proprio la differenza che questa prova serve a misurare.
    /// </summary>
    internal static class Programma
    {
        private static int Main(string[] argomenti)
        {
            // `--aggiungi` non svuota la selezione di prima: serve a provare
            // il caso che la voce E9 nominava come rischio — piu' entita'
            // selezionate insieme — che altrimenti non si costruisce.
            bool aggiungi = false;
            var puliti = new System.Collections.Generic.List<string>();
            foreach (var a in argomenti)
            {
                if (a == "--aggiungi") aggiungi = true;
                else puliti.Add(a);
            }
            argomenti = puliti.ToArray();

            string cosa = argomenti.Length > 0 ? argomenti[0] : "faccia";
            int quale = argomenti.Length > 1 && int.TryParse(argomenti[1], out var n) ? n : 1;

            object grezzo;
            try { grezzo = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS non e' aperto."); return 2; }
            var sw = (ISldWorks)grezzo;

            var doc = (IModelDoc2)sw.ActiveDoc;
            if (doc == null) { Console.Error.WriteLine("Nessun documento attivo."); return 2; }
            if (!aggiungi) doc.ClearSelection2(true);
            if (cosa == "niente")
            {
                doc.ClearSelection2(true);
                Console.WriteLine("selezione svuotata");
                return 0;
            }

            var parte = doc as IPartDoc;
            if (parte == null) { Console.Error.WriteLine("Non e' una parte."); return 2; }
            var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            if (corpi == null || corpi.Length == 0)
            {
                Console.Error.WriteLine("Il pezzo non ha corpi solidi.");
                return 1;
            }

            var corpo = corpi[Math.Min(quale, corpi.Length) - 1] as IBody2;
            bool preso = false;
            switch (cosa)
            {
                case "corpo":
                    try { preso = corpo.Select2(aggiungi, null); } catch { }
                    break;
                case "spigolo":
                {
                    var spigoli = corpo.GetEdges() as object[];
                    if (spigoli != null && spigoli.Length >= quale)
                    {
                        var e = spigoli[quale - 1] as IEntity;
                        try { preso = e.Select4(aggiungi, null); } catch { }
                    }
                    break;
                }
                default:
                {
                    var facce = corpo.GetFaces() as object[];
                    if (facce != null && facce.Length >= quale)
                    {
                        var f = facce[quale - 1] as IEntity;
                        try { preso = f.Select4(aggiungi, null); } catch { }
                    }
                    break;
                }
            }

            var selezione = (ISelectionMgr)doc.SelectionManager;
            Console.WriteLine("selezionato: " + preso +
                ", oggetti selezionati: " + selezione.GetSelectedObjectCount2(-1));
            return preso ? 0 : 1;
        }
    }
}
