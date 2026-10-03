using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.ChangeByHand
{
    /// <summary>
    /// CAMBIA IL PEZZO APERTO SENZA PASSARE DAL PONTE (voce X2).
    ///
    /// Serve a provare una difesa che, per definizione, non si puo' provare
    /// dall'interno: «qualcuno ha toccato il pezzo mentre il piano era gia'
    /// scritto». Finche' la modifica arriva dal ponte, il ponte lo sa e
    /// aggiorna la sua firma; la difesa scatta solo su una modifica di cui il
    /// ponte non sa niente. Quindi ci vuole un secondo attore, ed e' questo.
    ///
    /// PERCHE' UN ESEGUIBILE E NON UNO SCRIPT. `ISldWorks` per late binding da
    /// PowerShell 5.1 risponde TYPE_E_ELEMENTNOTFOUND: PowerShell non risolve i
    /// metodi sull'IDispatch di SOLIDWORKS. E' la stessa ragione per cui tutte
    /// le sonde COM di questo progetto sono C# compilati, ed e' scritta anche
    /// in `Checks\NewPart`.
    ///
    /// COSA FA: estrude un blocchetto sul primo piano di riferimento del
    /// documento attivo. Non e' «tirare una quota», ed e' onesto dirlo: e' una
    /// modifica alla geometria che cambia il volume e aggiunge una
    /// lavorazione, cioe' esattamente le due cose che la firma leggera guarda.
    /// Una quota tirata a mano cambierebbe il volume e basta, e sarebbe un
    /// caso PIU' DEBOLE di questo.
    ///
    /// NON SALVA. Il documento resta sporco, come lo lascerebbe una persona
    /// che ha toccato qualcosa e non ha ancora salvato.
    ///
    ///     ChangeByHand.exe            un blocchetto 10x10x10
    ///     ChangeByHand.exe 25         un blocchetto 25x25x25
    ///
    /// Esce 0 se ha cambiato qualcosa, 1 se non ci e' riuscito - e in quel
    /// caso lo dice, perche' una sonda che fallisce in silenzio farebbe
    /// sembrare verde la difesa che doveva mettere alla prova.
    /// </summary>
    internal static class Programma
    {
        private static int Main(string[] argomenti)
        {
            double lato = 10;
            if (argomenti.Length > 0 &&
                !double.TryParse(argomenti[0],
                                 System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture,
                                 out lato))
            {
                Console.WriteLine("il lato si scrive in millimetri, per esempio 25");
                return 1;
            }

            ISldWorks sw;
            try { sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
            catch (Exception ex)
            {
                Console.WriteLine("non mi aggancio a SOLIDWORKS: " + ex.Message);
                return 1;
            }

            var doc = (IModelDoc2)sw.ActiveDoc;
            if (doc == null)
            {
                Console.WriteLine("nessun documento attivo: non c'e' niente da cambiare.");
                return 1;
            }
            if (doc.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                Console.WriteLine("il documento attivo non e' una parte.");
                return 1;
            }

            Console.WriteLine("documento: " + doc.GetTitle());

            // IL VOLUME PRIMA E DOPO, perche' «ho chiamato l'estrusione» non e'
            // «il pezzo e' cambiato». E' la stessa regola che il ponte applica
            // a ogni operazione: si rilegge.
            double prima = Volume(doc);

            IFeature piano = null;
            try
            {
                var albero = doc.FeatureManager.GetFeatures(false) as object[];
                if (albero != null)
                {
                    foreach (var o in albero)
                    {
                        var f = o as IFeature;
                        if (f == null) continue;
                        try { if (f.GetTypeName2() == "RefPlane") { piano = f; break; } }
                        catch { }
                    }
                }
            }
            catch { }

            if (piano == null)
            {
                Console.WriteLine("il documento non ha piani di riferimento.");
                return 1;
            }

            try
            {
                doc.ClearSelection2(true);
                piano.Select2(false, 0);
                var sm = doc.SketchManager;
                sm.InsertSketch(true);
                // Fuori dall'origine, cosi' e' un corpo in piu' e non un taglio.
                sm.CreateCenterRectangle(lato / 1000.0, lato / 1000.0, 0,
                                         lato / 1000.0 + lato / 2000.0,
                                         lato / 1000.0 + lato / 2000.0, 0);
                sm.InsertSketch(true);
                doc.FeatureManager.FeatureExtrusion3(
                    true, false, false, (int)swEndConditions_e.swEndCondBlind, 0,
                    lato / 1000.0, 0, false, false, false, false, 0, 0,
                    false, false, false, false, true, true, true, 0, 0, false);
                doc.ClearSelection2(true);
                doc.EditRebuild3();
            }
            catch (Exception ex)
            {
                Console.WriteLine("l'estrusione e' esplosa: " + ex.Message);
                return 1;
            }

            double dopo = Volume(doc);
            Console.WriteLine("volume: " + prima.ToString("0.#") + " -> " + dopo.ToString("0.#") + " mm3");

            if (Math.Abs(dopo - prima) < 1e-6)
            {
                Console.WriteLine("NON E' CAMBIATO NIENTE: la sonda non ha fatto il suo mestiere.");
                return 1;
            }
            return 0;
        }

        /// <summary>Il volume in mm3, o 0 se non si riesce a leggerlo.</summary>
        private static double Volume(IModelDoc2 doc)
        {
            try
            {
                var p = doc.Extension.CreateMassProperty();
                if (p == null) return 0;
                return p.Volume * 1e9;
            }
            catch { return 0; }
        }
    }
}
