using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

namespace Controlli.Opzione
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
    ///     Opzione.exe 248              legge
    ///     Opzione.exe 248 on           accende
    ///     Opzione.exe 248 off          spegne
    ///
    /// I numeri sono quelli di `swUserPreferenceToggle_e`; `ApiQuote.exe` li
    /// elenca cercando per parola.
    /// </summary>
    internal static class Programma
    {
        private static int Main(string[] argomenti)
        {
            if (argomenti.Length < 1)
            {
                Console.Error.WriteLine("Serve il numero dell'opzione.");
                return 2;
            }
            int numero;
            if (!int.TryParse(argomenti[0], out numero))
            {
                Console.Error.WriteLine("Il primo argomento e' un numero.");
                return 2;
            }

            object grezzo;
            try { grezzo = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS non e' aperto."); return 2; }
            var sw = (ISldWorks)grezzo;

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
            try { app = sw.GetUserPreferenceToggle(numero); appOk = true; } catch { }
            bool documento = false, docOk = false;
            try
            {
                if (doc != null)
                {
                    documento = doc.Extension.GetUserPreferenceToggle(numero, 0);
                    docOk = true;
                }
            }
            catch { }

            Console.WriteLine("opzione " + numero + " prima:");
            Console.WriteLine("  applicazione: " + (appOk ? app.ToString() : "non leggibile"));
            Console.WriteLine("  documento   : " +
                (doc == null ? "nessun documento attivo" : docOk ? documento.ToString() : "non leggibile"));

            if (argomenti.Length < 2) return 0;
            bool voluto = argomenti[1] == "on";
            string dove = argomenti.Length > 2 ? argomenti[2] : "app";

            if (dove == "doc")
            {
                if (doc == null) { Console.Error.WriteLine("Nessun documento attivo."); return 1; }
                try { doc.Extension.SetUserPreferenceToggle(numero, 0, voluto); }
                catch (Exception e) { Console.Error.WriteLine("Non riesco: " + e.Message); return 1; }
                Console.WriteLine("documento adesso: " + doc.Extension.GetUserPreferenceToggle(numero, 0));
            }
            else
            {
                try { sw.SetUserPreferenceToggle(numero, voluto); }
                catch (Exception e) { Console.Error.WriteLine("Non riesco: " + e.Message); return 1; }
                Console.WriteLine("applicazione adesso: " + sw.GetUserPreferenceToggle(numero));
            }
            return 0;
        }
    }
}
