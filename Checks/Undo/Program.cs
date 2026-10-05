using System;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.Undo
{
    /// <summary>
    /// A SINGLE CTRL+Z, and the volume before and after.
    ///
    /// It serves to test the second half of entry B8: the bridge gathers the whole
    /// plan inside ONE undo entry, so a single Ctrl+Z must
    /// bring the document back to the `volume_before_mm3` that the bridge itself had
    /// printed. Before, as many were needed as there were steps — and whoever pressed one
    /// too many also undid the work from before.
    ///
    /// The volume is printed BEFORE and AFTER, so the number does not have to be looked for
    /// elsewhere: a check that asks to look somewhere else to know
    /// how it went is done only once.
    ///
    ///     Undo.exe            undoes once
    ///     Undo.exe 3          undoes three times
    ///     Undo.exe 0          does not undo: prints only the volume
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
                // EditUndo2 and not RunCommand: the menu command depends on the
                // focus of the window, this one acts on the document that one has
                // in hand. It is the same reason why in this project one never
                // trusts the «active document» when it is possible to keep
                // a reference.
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
        /// The volume in cubic mm, or null if there is no body.
        ///
        /// `GetMassProperties2` answers in cubic metres, like all the APIs of
        /// SOLIDWORKS: 1e9 is the conversion, not a convenience factor.
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
