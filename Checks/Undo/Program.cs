using System;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.Undo
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
    ///     Undo.exe            annulla una volta
    ///     Undo.exe 3          annulla tre volte
    ///     Undo.exe 0          non annulla: stampa solo il volume
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            int howMany = 1;
            if (arguments.Length > 0 && !int.TryParse(arguments[0], out howMany))
            {
                Console.Error.WriteLine("L'argomento e' quante volte annullare.");
                return 2;
            }

            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS non e' aperto."); return 2; }
            var sw = (ISldWorks)raw;

            var doc = (IModelDoc2)sw.ActiveDoc;
            if (doc == null) { Console.Error.WriteLine("Nessun documento attivo."); return 2; }

            Console.WriteLine("documento: " + doc.GetTitle());
            Console.WriteLine(Line("prima", Volume(doc)));

            for (int i = 0; i < howMany; i++)
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

            Console.WriteLine("annullamenti: " + howMany);
            Console.WriteLine(Line("dopo", Volume(doc)));
            return 0;
        }

        private static string Line(string timestamp, double? volume)
        {
            return volume == null
                ? "volume " + timestamp + ": nessun corpo solido"
                : string.Format(CultureInfo.InvariantCulture,
                    "volume {0}: {1:0.#} mm3", timestamp, volume);
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
                var partDoc = doc as IPartDoc;
                if (partDoc == null) return null;
                var bodies = partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (bodies == null || bodies.Length == 0) return null;
                double total = 0;
                foreach (var c in bodies)
                {
                    var body = c as IBody2;
                    if (body == null) continue;
                    var p = body.GetMassProperties(1) as double[];
                    if (p != null && p.Length > 3) total += p[3];
                }
                return total * 1e9;
            }
            catch { return null; }
        }
    }
}
