using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.Select
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
    ///     Select.exe faccia 1        la prima faccia del primo corpo
    ///     Select.exe spigolo 1       il primo spigolo
    ///     Select.exe corpo 1         il primo corpo
    ///     Select.exe niente          svuota la selezione
    ///
    /// I numeri sono quelli dell'ordine in cui SOLIDWORKS da' le entita', NON
    /// gli identificativi dello stato: sono due numerazioni diverse, ed e'
    /// proprio la differenza che questa prova serve a misurare.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            // `--aggiungi` non svuota la selezione di prima: serve a provare
            // il caso che la voce E9 nominava come rischio — piu' entita'
            // selezionate insieme — che altrimenti non si costruisce.
            bool append = false;
            var clean = new System.Collections.Generic.List<string>();
            foreach (var a in arguments)
            {
                if (a == "--aggiungi") append = true;
                else clean.Add(a);
            }
            arguments = clean.ToArray();

            string what = arguments.Length > 0 ? arguments[0] : "faccia";
            int which = arguments.Length > 1 && int.TryParse(arguments[1], out var n) ? n : 1;

            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS non e' aperto."); return 2; }
            var sw = (ISldWorks)raw;

            var doc = (IModelDoc2)sw.ActiveDoc;
            if (doc == null) { Console.Error.WriteLine("Nessun documento attivo."); return 2; }
            if (!append) doc.ClearSelection2(true);
            if (what == "niente")
            {
                doc.ClearSelection2(true);
                Console.WriteLine("selezione svuotata");
                return 0;
            }

            var partDoc = doc as IPartDoc;
            if (partDoc == null) { Console.Error.WriteLine("Non e' una parte."); return 2; }
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            if (bodies == null || bodies.Length == 0)
            {
                Console.Error.WriteLine("Il pezzo non ha corpi solidi.");
                return 1;
            }

            var body = bodies[Math.Min(which, bodies.Length) - 1] as IBody2;
            bool taken = false;
            switch (what)
            {
                case "body":
                    try { taken = body.Select2(append, null); } catch { }
                    break;
                case "edge":
                {
                    var edges = body.GetEdges() as object[];
                    if (edges != null && edges.Length >= which)
                    {
                        var e = edges[which - 1] as IEntity;
                        try { taken = e.Select4(append, null); } catch { }
                    }
                    break;
                }
                default:
                {
                    var faces = body.GetFaces() as object[];
                    if (faces != null && faces.Length >= which)
                    {
                        var f = faces[which - 1] as IEntity;
                        try { taken = f.Select4(append, null); } catch { }
                    }
                    break;
                }
            }

            var selection = (ISelectionMgr)doc.SelectionManager;
            Console.WriteLine("selezionato: " + taken +
                ", oggetti selezionati: " + selection.GetSelectedObjectCount2(-1));
            return taken ? 0 : 1;
        }
    }
}
