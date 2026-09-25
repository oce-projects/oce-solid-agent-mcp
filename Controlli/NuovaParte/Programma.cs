using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Controlli.NuovaParte
{
    /// <summary>
    /// APRE UNA PARTE VUOTA, per far ripartire il banco da zero.
    ///
    /// Serve dopo aver chiuso gli scarti: il ponte lavora sul documento aperto,
    /// e con SOLIDWORKS a documenti zero non ha niente su cui posarsi.
    ///
    /// Perche' compilato e non uno script. `ISldWorks.NewDocument` per late
    /// binding da PowerShell 5.1 risponde TYPE_E_ELEMENTNOTFOUND: PowerShell non
    /// riesce a risolvere il metodo sull'IDispatch di SOLIDWORKS. E' lo stesso
    /// motivo per cui tutte le sonde COM di questo progetto sono eseguibili C#
    /// compilati contro l'interop, e non .ps1.
    ///
    /// IL MODELLO. Si prende quello preferito dall'utente; se la preferenza non
    /// c'e' o punta a un file sparito, si cerca un .prtdot sul disco. La ricerca
    /// SCARTA i modelli MBD e quelli in millesimi: hanno unita' e impostazioni
    /// loro, e una prova che misura in millimetri su un modello in millesimi
    /// legge numeri giusti e sbagliati insieme.
    /// </summary>
    internal static class Programma
    {
        private static int Main()
        {
            object grezzo;
            try { grezzo = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException)
            {
                Console.Error.WriteLine("SOLIDWORKS non e' aperto.");
                return 2;
            }
            var sw = (ISldWorks)grezzo;

            string modello = Preferito(sw) ?? CercatoSulDisco();
            if (modello == null)
            {
                Console.Error.WriteLine("Nessun modello di parte (.prtdot) trovato.");
                return 1;
            }
            Console.WriteLine("modello: " + modello);

            var doc = (IModelDoc2)sw.NewDocument(modello, 0, 0, 0);
            if (doc == null)
            {
                Console.Error.WriteLine("NewDocument ha dato null.");
                return 1;
            }
            Console.WriteLine("nuova parte: " + doc.GetTitle());
            return 0;
        }

        private static string Preferito(ISldWorks sw)
        {
            try
            {
                var p = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                return !string.IsNullOrEmpty(p) && File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        private static string CercatoSulDisco()
        {
            // `C:\ProgramData\SolidWorks` e' dove SOLIDWORKS mette i suoi
            // modelli su qualunque installazione. Accanto c'era il percorso di
            // un disco di rete di UNA postazione: non serviva a nessun altro e
            // diceva com'e' fatto quel disco. Adesso e' una variabile, e chi
            // tiene i modelli altrove lo dice.
            var cartelle = new System.Collections.Generic.List<string>
            {
                @"C:\ProgramData\SolidWorks",
            };
            // `System.` per esteso: l'interop di SOLIDWORKS ha un suo
            // `Environment`, e con i due `using` il nome nudo non compila.
            var sua = System.Environment.GetEnvironmentVariable("MODELLI_SOLIDWORKS");
            if (!string.IsNullOrWhiteSpace(sua)) cartelle.Add(sua);
            foreach (var c in cartelle)
            {
                if (!Directory.Exists(c)) continue;
                string[] trovati;
                try { trovati = Directory.GetFiles(c, "*.prtdot", SearchOption.AllDirectories); }
                catch { continue; }
                foreach (var f in trovati)
                {
                    string b = f.ToLowerInvariant();
                    if (b.Contains(@"\mbd\")) continue;
                    if (b.Contains("0001mm")) continue;
                    if (b.Contains("ansi-inch") || b.Contains("inch")) continue;
                    return f;
                }
                if (trovati.Length > 0) return trovati[0];
            }
            return null;
        }
    }
}
