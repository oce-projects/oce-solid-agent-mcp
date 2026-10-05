using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.NewPart
{
    /// <summary>
    /// OPENS AN EMPTY PART, to restart the bench from zero.
    ///
    /// It is needed after closing the leftovers: the bridge works on the open document,
    /// and with SOLIDWORKS at zero documents it has nothing to rest on.
    ///
    /// Why compiled and not a script. `ISldWorks.NewDocument` by late
    /// binding from PowerShell 5.1 answers TYPE_E_ELEMENTNOTFOUND: PowerShell
    /// cannot resolve the method on the SOLIDWORKS IDispatch. It is the same
    /// reason why all the COM probes of this project are C# executables
    /// compiled against the interop, and not .ps1.
    ///
    /// THE TEMPLATE. The one preferred by the user is taken; if the preference
    /// is not there or points to a vanished file, a .prtdot is searched on disk. The search
    /// DISCARDS the MBD templates and those in thousandths: they have units and settings
    /// of their own, and a check that measures in millimetres on a template in thousandths
    /// reads right and wrong numbers together.
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException)
            {
                Console.Error.WriteLine("SOLIDWORKS non e' aperto.");
                return 2;
            }
            var sw = (ISldWorks)raw;

            string model = Preferred(sw) ?? SearchedOnDisk();
            if (model == null)
            {
                Console.Error.WriteLine("Nessun modello di parte (.prtdot) trovato.");
                return 1;
            }
            Console.WriteLine("modello: " + model);

            var doc = (IModelDoc2)sw.NewDocument(model, 0, 0, 0);
            if (doc == null)
            {
                Console.Error.WriteLine("NewDocument ha dato null.");
                return 1;
            }
            Console.WriteLine("nuova parte: " + doc.GetTitle());
            return 0;
        }

        private static string Preferred(ISldWorks sw)
        {
            try
            {
                var p = sw.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                return !string.IsNullOrEmpty(p) && File.Exists(p) ? p : null;
            }
            catch { return null; }
        }

        private static string SearchedOnDisk()
        {
            // `C:\ProgramData\SolidWorks` is where SOLIDWORKS puts its
            // templates on any installation. Next to it there was the path of
            // a network drive of ONE workstation: it was of no use to anybody else and
            // it told how that drive is laid out. Now it is a variable, and whoever
            // keeps the templates elsewhere says so.
            var folders = new System.Collections.Generic.List<string>
            {
                @"C:\ProgramData\SolidWorks",
            };
            // `System.` spelled out: the SOLIDWORKS interop has its own
            // `Environment`, and with the two `using`s the bare name does not compile.
            var its = System.Environment.GetEnvironmentVariable("MODELLI_SOLIDWORKS");
            if (!string.IsNullOrWhiteSpace(its)) folders.Add(its);
            foreach (var c in folders)
            {
                if (!Directory.Exists(c)) continue;
                string[] foundItems;
                try { foundItems = Directory.GetFiles(c, "*.prtdot", SearchOption.AllDirectories); }
                catch { continue; }
                foreach (var f in foundItems)
                {
                    string b = f.ToLowerInvariant();
                    if (b.Contains(@"\mbd\")) continue;
                    if (b.Contains("0001mm")) continue;
                    if (b.Contains("ansi-inch") || b.Contains("inch")) continue;
                    return f;
                }
                if (foundItems.Length > 0) return foundItems[0];
            }
            return null;
        }
    }
}
