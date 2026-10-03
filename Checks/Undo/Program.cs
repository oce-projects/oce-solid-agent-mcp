using System;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Controlli.Annulla
{
    /// <summary>
    /// UN SOLO CTRL+Z, e il volume prima e dopo.
    ///
    /// Serve a provare la seconda meta' della voce B8: il ponte raccoglie tutto
    /// il piano dentro UNA voce di annullamento, quindi un solo Ctrl+Z deve
    /// riportare il documento al `volume_before_mm3` che il ponte stesso aveva
    /// stampato. Prima ne servivano tanti quanti i passi — e chi ne premeva uno
    /// di troppo disfaceva anche il lavoro di prima.
    ///
    /// Il volume si stampa PRIMA e DOPO, cosi' il numero non va cercato
    /// altrove: una prova che chiede di guardare da un'altra parte per sapere
    /// com'e' andata si fa una volta sola.
    ///
    ///     Annulla.exe            annulla una volta
    ///     Annulla.exe 3          annulla tre volte
    ///     Annulla.exe 0          non annulla: stampa solo il volume
    /// </summary>
    internal static class Programma
    {
        private static int Main(string[] argomenti)
        {
            int quante = 1;
            if (argomenti.Length > 0 && !int.TryParse(argomenti[0], out quante))
            {
                Console.Error.WriteLine("L'argomento e' quante volte annullare.");
                return 2;
            }

            object grezzo;
            try { grezzo = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS non e' aperto."); return 2; }
            var sw = (ISldWorks)grezzo;

            var doc = (IModelDoc2)sw.ActiveDoc;
            if (doc == null) { Console.Error.WriteLine("Nessun documento attivo."); return 2; }

            Console.WriteLine("documento: " + doc.GetTitle());
            Console.WriteLine(Riga("prima", Volume(doc)));

            for (int i = 0; i < quante; i++)
            {
                // EditUndo2 e non RunCommand: il comando di menu dipende dal
                // fuoco della finestra, questo agisce sul documento che si ha
                // in mano. E' la stessa ragione per cui in questo progetto non
                // ci si fida mai del «documento attivo» quando si puo' tenere
                // un riferimento.
                try { doc.EditUndo2(1); }
                catch (Exception e)
                {
                    Console.Error.WriteLine("annullamento " + (i + 1) + " fallito: " + e.Message);
                    return 1;
                }
            }
            try { doc.EditRebuild3(); } catch { }

            Console.WriteLine("annullamenti: " + quante);
            Console.WriteLine(Riga("dopo", Volume(doc)));
            return 0;
        }

        private static string Riga(string quando, double? volume)
        {
            return volume == null
                ? "volume " + quando + ": nessun corpo solido"
                : string.Format(CultureInfo.InvariantCulture,
                    "volume {0}: {1:0.#} mm3", quando, volume);
        }

        /// <summary>
        /// Il volume in mm cubi, o null se non c'e' nessun corpo.
        ///
        /// `GetMassProperties2` risponde in metri cubi, come tutte le API di
        /// SOLIDWORKS: 1e9 e' la conversione, non un fattore di comodo.
        /// </summary>
        private static double? Volume(IModelDoc2 doc)
        {
            try
            {
                var parte = doc as IPartDoc;
                if (parte == null) return null;
                var corpi = parte.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (corpi == null || corpi.Length == 0) return null;
                double totale = 0;
                foreach (var c in corpi)
                {
                    var corpo = c as IBody2;
                    if (corpo == null) continue;
                    var p = corpo.GetMassProperties(1) as double[];
                    if (p != null && p.Length > 3) totale += p[3];
                }
                return totale * 1e9;
            }
            catch { return null; }
        }
    }
}
