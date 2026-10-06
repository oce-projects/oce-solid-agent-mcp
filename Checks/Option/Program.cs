using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

namespace Checks.Option
{
    /// <summary>
    /// READS AND CHANGES A SOLIDWORKS PREFERENCE, by number.
    ///
    /// It serves the bench to isolate a question of the type «does it not work because
    /// the API is wrong, or because an option is turned off?». Changing
    /// an option inside the production code to find out means
    /// recompiling at every attempt, and leaving it there by mistake.
    ///
    /// IT ALWAYS PRINTS THE PREVIOUS VALUE, because it is a setting of whoever
    /// works and must be put back as it was. Whoever changes it must be able to put it back without
    /// remembering it.
    ///
    ///     Option.exe 248              reads
    ///     Option.exe 248 on           turns on
    ///     Option.exe 248 off          turns off
    ///
    /// The numbers are those of `swUserPreferenceToggle_e`; `DimensionApis.exe` lists
    /// them by searching for a word.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            if (arguments.Length < 1)
            {
                Console.Error.WriteLine("The option number is needed.");
                return 2;
            }
            int number;
            if (!int.TryParse(arguments[0], out number))
            {
                Console.Error.WriteLine("The first argument is a number.");
                return 2;
            }

            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS is not open."); return 2; }
            var sw = (ISldWorks)raw;

            // TWO LEVELS, AND THEY ARE NOT INTERCHANGEABLE.
            //
            // Some preferences live in the APPLICATION (`ISldWorks`), others
            // in the DOCUMENT (`IModelDocExtension`). Asking the application for a
            // document one **does not raise**: it answers a value, and
            // writing it changes nothing. Measured on
            // `swDetailingAutoInsertDimsMarkedForDrawing` (248): read False,
            // written True, read again **False**, no error.
            //
            // That is why both are printed here, always: an option that
            // «will not let itself be changed» is almost always an option asked at the
            // wrong level.
            var doc = sw.ActiveDoc as IModelDoc2;

            bool app = false, appOk = false;
            try { app = sw.GetUserPreferenceToggle(number); appOk = true; } catch { }
            bool document = false, docOk = false;
            try
            {
                if (doc != null)
                {
                    document = doc.Extension.GetUserPreferenceToggle(number, 0);
                    docOk = true;
                }
            }
            catch { }

            Console.WriteLine("option " + number + " before:");
            Console.WriteLine("  application: " + (appOk ? app.ToString() : "not readable"));
            Console.WriteLine("  document    : " +
                (doc == null ? "no active document" : docOk ? document.ToString() : "not readable"));

            if (arguments.Length < 2) return 0;
            bool wanted = arguments[1] == "on";
            string location = arguments.Length > 2 ? arguments[2] : "app";

            if (location == "doc")
            {
                if (doc == null) { Console.Error.WriteLine("No active document."); return 1; }
                try { doc.Extension.SetUserPreferenceToggle(number, 0, wanted); }
                catch (Exception e) { Console.Error.WriteLine("I cannot: " + e.Message); return 1; }
                Console.WriteLine("document now: " + doc.Extension.GetUserPreferenceToggle(number, 0));
            }
            else
            {
                try { sw.SetUserPreferenceToggle(number, wanted); }
                catch (Exception e) { Console.Error.WriteLine("I cannot: " + e.Message); return 1; }
                Console.WriteLine("application now: " + sw.GetUserPreferenceToggle(number));
            }
            return 0;
        }
    }
}
