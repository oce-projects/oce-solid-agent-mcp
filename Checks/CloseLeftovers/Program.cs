using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.CloseLeftovers
{
    /// <summary>
    /// CHIUDE I DOCUMENTI DI SCARTO LASCIATI APERTI DALLE PROVE.
    ///
    /// Ogni piano di prova apre una parte nuova e non la chiude. Dopo qualche
    /// decina di prove SOLIDWORKS si trova quaranta o cinquanta documenti in
    /// memoria, e non e' solo spreco: **ogni lettura di stato li scorre tutti**,
    /// a 244 ms di `GetNext()` ciascuno. E' il vero motivo per cui un piano da
    /// mezzo secondo di lavoro ne impiegava ottantacinque.
    ///
    /// COSA CHIUDE, E COSA NON TOCCA.
    /// Solo due categorie, e la seconda si legge dalla riga di comando:
    ///
    ///   1. i documenti MAI SALVATI (percorso vuoto): sono le "ParteNN" che
    ///      nascono dalle prove e non esistono su disco;
    ///   2. quelli il cui percorso comincia con una delle cartelle passate come
    ///      argomento — la scratchpad di sessione, tipicamente.
    ///
    /// Tutto il resto resta aperto. Un documento salvato da qualche altra parte
    /// non e' roba nostra, e chiuderlo senza chiedere sarebbe il modo piu'
    /// rapido di far perdere del lavoro a qualcuno.
    ///
    /// NON SALVA NIENTE. `CloseDoc` scarta le modifiche non salvate, ed e'
    /// esattamente quello che si vuole per un pezzo di prova. Per questo la
    /// prima categoria e' limitata ai documenti SENZA percorso: un file vero
    /// con modifiche non salvate non finisce mai in questa lista.
    ///
    ///     CloseLeftovers.exe                       elenca soltanto
    ///     CloseLeftovers.exe --chiudi [cartelle]   chiude davvero
    ///     CloseLeftovers.exe --chiudi --risparmia "Parte1,Parte2"
    ///
    /// `--risparmia` serve al banco, ed e' la differenza fra «chiudo i miei
    /// scarti» e «chiudo tutto quello che non e' salvato». Il banco legge quali
    /// documenti c'erano PRIMA di cominciare e li nomina qui: cosi' chiude
    /// quello che ha aperto lui e non quello che aveva aperto qualcun altro.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] arguments)
        {
            bool reallyClose = false;
            var folders = new List<string>();
            var spare = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < arguments.Length; i++)
            {
                var a = arguments[i];
                if (a == "--chiudi") { reallyClose = true; continue; }
                if (a == "--risparmia" && i + 1 < arguments.Length)
                {
                    foreach (var t in arguments[++i].Split(','))
                        if (t.Trim().Length > 0) spare.Add(t.Trim());
                    continue;
                }
                folders.Add(a.Replace('/', '\\').TrimEnd('\\') + "\\");
            }

            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException)
            {
                Console.Error.WriteLine("SOLIDWORKS non e' aperto: niente da chiudere.");
                return 2;
            }
            var sw = (ISldWorks)raw;

            // SI FA L'ELENCO PRIMA, POI SI CHIUDE.
            // Chiudere mentre si scorre la catena dei documenti la spezza: il
            // GetNext() successivo parte da un documento che non c'e' piu'.
            var toClose = new List<string>();
            var kept = new List<string>();
            var doc = (IModelDoc2)sw.GetFirstDocument();
            while (doc != null)
            {
                string docTitle = doc.GetTitle();
                string filePath = doc.GetPathName() ?? "";
                if (spare.Contains(docTitle))
                    kept.Add(docTitle + "  (risparmiato: c'era gia' prima)");
                else if (Deviation(filePath, folders)) toClose.Add(docTitle);
                else kept.Add(docTitle + (filePath == "" ? "" : "  " + filePath));
                doc = (IModelDoc2)doc.GetNext();
            }

            Console.WriteLine(toClose.Count + " da chiudere, " + kept.Count + " tenuti.");
            foreach (var t in kept) Console.WriteLine("  tenuto:  " + t);
            if (!reallyClose)
            {
                foreach (var t in toClose) Console.WriteLine("  sarebbe chiuso:  " + t);
                Console.WriteLine("Nessuno chiuso: manca --chiudi.");
                return 0;
            }

            int done = 0;
            foreach (var t in toClose)
            {
                try { sw.CloseDoc(t); done++; }
                catch (Exception e) { Console.Error.WriteLine("  non chiuso " + t + ": " + e.Message); }
            }
            Console.WriteLine("chiusi: " + done);

            // IL CONTROLLO CHE E' DAVVERO SUCCESSO. Si ricontano i documenti:
            // CloseDoc non torna niente, e senza ricontare non si distingue
            // «closed» da «chiamato».
            int remain = 0;
            var d2 = (IModelDoc2)sw.GetFirstDocument();
            while (d2 != null) { remain++; d2 = (IModelDoc2)d2.GetNext(); }
            Console.WriteLine("documenti aperti adesso: " + remain);
            return 0;
        }

        private static bool Deviation(string filePath, List<string> folders)
        {
            if (string.IsNullOrEmpty(filePath)) return true;   // mai salvato
            string p = filePath.Replace('/', '\\');
            foreach (var c in folders)
                if (p.StartsWith(c, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
