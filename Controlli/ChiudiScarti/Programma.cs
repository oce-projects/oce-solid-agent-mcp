using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Controlli.ChiudiScarti
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
    ///     ChiudiScarti.exe                       elenca soltanto
    ///     ChiudiScarti.exe --chiudi [cartelle]   chiude davvero
    ///     ChiudiScarti.exe --chiudi --risparmia "Parte1,Parte2"
    ///
    /// `--risparmia` serve al banco, ed e' la differenza fra «chiudo i miei
    /// scarti» e «chiudo tutto quello che non e' salvato». Il banco legge quali
    /// documenti c'erano PRIMA di cominciare e li nomina qui: cosi' chiude
    /// quello che ha aperto lui e non quello che aveva aperto qualcun altro.
    /// </summary>
    internal static class Programma
    {
        private static int Main(string[] argomenti)
        {
            bool chiudiDavvero = false;
            var cartelle = new List<string>();
            var risparmia = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < argomenti.Length; i++)
            {
                var a = argomenti[i];
                if (a == "--chiudi") { chiudiDavvero = true; continue; }
                if (a == "--risparmia" && i + 1 < argomenti.Length)
                {
                    foreach (var t in argomenti[++i].Split(','))
                        if (t.Trim().Length > 0) risparmia.Add(t.Trim());
                    continue;
                }
                cartelle.Add(a.Replace('/', '\\').TrimEnd('\\') + "\\");
            }

            object grezzo;
            try { grezzo = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException)
            {
                Console.Error.WriteLine("SOLIDWORKS non e' aperto: niente da chiudere.");
                return 2;
            }
            var sw = (ISldWorks)grezzo;

            // SI FA L'ELENCO PRIMA, POI SI CHIUDE.
            // Chiudere mentre si scorre la catena dei documenti la spezza: il
            // GetNext() successivo parte da un documento che non c'e' piu'.
            var daChiudere = new List<string>();
            var tenuti = new List<string>();
            var doc = (IModelDoc2)sw.GetFirstDocument();
            while (doc != null)
            {
                string titolo = doc.GetTitle();
                string percorso = doc.GetPathName() ?? "";
                if (risparmia.Contains(titolo))
                    tenuti.Add(titolo + "  (risparmiato: c'era gia' prima)");
                else if (Scarto(percorso, cartelle)) daChiudere.Add(titolo);
                else tenuti.Add(titolo + (percorso == "" ? "" : "  " + percorso));
                doc = (IModelDoc2)doc.GetNext();
            }

            Console.WriteLine(daChiudere.Count + " da chiudere, " + tenuti.Count + " tenuti.");
            foreach (var t in tenuti) Console.WriteLine("  tenuto:  " + t);
            if (!chiudiDavvero)
            {
                foreach (var t in daChiudere) Console.WriteLine("  sarebbe chiuso:  " + t);
                Console.WriteLine("Nessuno chiuso: manca --chiudi.");
                return 0;
            }

            int fatti = 0;
            foreach (var t in daChiudere)
            {
                try { sw.CloseDoc(t); fatti++; }
                catch (Exception e) { Console.Error.WriteLine("  non chiuso " + t + ": " + e.Message); }
            }
            Console.WriteLine("chiusi: " + fatti);

            // IL CONTROLLO CHE E' DAVVERO SUCCESSO. Si ricontano i documenti:
            // CloseDoc non torna niente, e senza ricontare non si distingue
            // «closed» da «chiamato».
            int restano = 0;
            var d2 = (IModelDoc2)sw.GetFirstDocument();
            while (d2 != null) { restano++; d2 = (IModelDoc2)d2.GetNext(); }
            Console.WriteLine("documenti aperti adesso: " + restano);
            return 0;
        }

        private static bool Scarto(string percorso, List<string> cartelle)
        {
            if (string.IsNullOrEmpty(percorso)) return true;   // mai salvato
            string p = percorso.Replace('/', '\\');
            foreach (var c in cartelle)
                if (p.StartsWith(c, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
