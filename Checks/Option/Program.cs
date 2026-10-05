using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

namespace Checks.Option
{
    /// <summary>
    /// LEGGE E CAMBIA UNA PREFERENZA DI SOLIDWORKS, per numero.
    ///
    /// Serve al banco per isolare una domanda del tipo «non funziona perche'
    /// l'API e' sbagliata, o perche' un'opzione e' spenta?». Cambiare
    /// un'opzione dentro il codice di produzione per scoprirlo significa
    /// ricompilare a ogni tentativo, e lasciarcela per sbaglio.
    ///
    /// STAMPA SEMPRE IL VALORE DI PRIMA, perche' e' un'impostazione di chi
    /// lavora e va rimessa com'era. Chi la cambia deve poterla rimettere senza
    /// ricordarsela.
    ///
    ///     Option.exe 248              legge
    ///     Option.exe 248 on           accende
    ///     Option.exe 248 off          spegne
    ///
    /// I numeri sono quelli di `swUserPreferenceToggle_e`; `DimensionApis.exe` li
    /// elenca cercando per parola.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            if (arguments.Length < 1)
            {
                Console.Error.WriteLine("Serve il numero dell'opzione.");
                return 2;
            }
            int number;
            if (!int.TryParse(arguments[0], out number))
            {
                Console.Error.WriteLine("Il primo argomento e' un numero.");
                return 2;
            }

            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS non e' aperto."); return 2; }
            var sw = (ISldWorks)raw;

            // DUE LIVELLI, E NON SONO INTERCAMBIABILI.
            //
            // Alcune preferenze stanno nell'APPLICAZIONE (`ISldWorks`), altre
            // nel DOCUMENTO (`IModelDocExtension`). Chiedere una di documento
            // all'applicazione **non solleva**: risponde un valore, e
            // scriverlo non cambia niente. Misurato su
            // `swDetailingAutoInsertDimsMarkedForDrawing` (248): letto False,
            // scritto True, riletto **False**, nessun errore.
            //
            // Per questo qui si stampano tutti e due, sempre: un'opzione che
            // «non si lascia cambiare» quasi sempre e' un'opzione chiesta al
            // livello sbagliato.
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

            Console.WriteLine("opzione " + number + " prima:");
            Console.WriteLine("  applicazione: " + (appOk ? app.ToString() : "non leggibile"));
            Console.WriteLine("  documento   : " +
                (doc == null ? "nessun documento attivo" : docOk ? document.ToString() : "non leggibile"));

            if (arguments.Length < 2) return 0;
            bool wanted = arguments[1] == "on";
            string location = arguments.Length > 2 ? arguments[2] : "app";

            if (location == "doc")
            {
                if (doc == null) { Console.Error.WriteLine("Nessun documento attivo."); return 1; }
                try { doc.Extension.SetUserPreferenceToggle(number, 0, wanted); }
                catch (Exception e) { Console.Error.WriteLine("Non riesco: " + e.Message); return 1; }
                Console.WriteLine("documento adesso: " + doc.Extension.GetUserPreferenceToggle(number, 0));
            }
            else
            {
                try { sw.SetUserPreferenceToggle(number, wanted); }
                catch (Exception e) { Console.Error.WriteLine("Non riesco: " + e.Message); return 1; }
                Console.WriteLine("applicazione adesso: " + sw.GetUserPreferenceToggle(number));
            }
            return 0;
        }
    }
}
