using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Controlli.QuoteInTavola
{
    /// <summary>
    /// PERCHE' `InsertModelAnnotations3` NON PORTA NESSUNA QUOTA IN TAVOLA.
    ///
    /// Il pezzo ha undici quote, la tavola ne riceve zero, e l'API non dice
    /// perche': risponde un elenco vuoto. Provare a indovinare la combinazione
    /// giusta modificando il codice di produzione e rilanciando il banco costa
    /// dieci minuti a tentativo; qui si provano tutte in un colpo e si conta
    /// dopo ognuna.
    ///
    /// SI CONTA, NON SI GUARDA IL VALORE DI RITORNO. `InsertModelAnnotations3`
    /// puo' rispondere un elenco e non aver attaccato niente, o rispondere
    /// vuoto avendo lavorato: l'unico riscontro e' quante annotazioni hanno le
    /// viste prima e dopo.
    ///
    ///     QuoteInTavola.exe        lavora sulla tavola attiva
    /// </summary>
    internal static class Programma
    {
        private static IDrawingDoc _tavola;
        private static IModelDoc2 _doc;

        private static int Main()
        {
            object grezzo;
            try { grezzo = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS non e' aperto."); return 2; }
            var sw = (ISldWorks)grezzo;

            _doc = (IModelDoc2)sw.ActiveDoc;
            if (_doc == null || _doc.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                Console.Error.WriteLine("Serve una tavola attiva.");
                return 2;
            }
            _tavola = (IDrawingDoc)_doc;
            Console.WriteLine("tavola: " + _doc.GetTitle());

            // 1. LE VISTE, E COSA HANNO GIA'.
            var nomiViste = new List<string>();
            var v = _tavola.GetFirstView() as IView;
            while (v != null)
            {
                var ann = v.GetAnnotations() as object[];
                var dim = v.GetDisplayDimensions() as object[];
                object riferito = null;
                try { riferito = v.ReferencedDocument; } catch { }
                Console.WriteLine("  vista «" + v.GetName2() + "» tipo " + v.Type +
                    ", modello: «" + (v.GetReferencedModelName() ?? "") + "»" +
                    ", RISOLTA: " + (riferito != null) +
                    ", annotazioni " + (ann == null ? 0 : ann.Length) +
                    ", quote " + (dim == null ? 0 : dim.Length));
                if (v.Type != (int)swDrawingViewTypes_e.swDrawingSheet)
                    nomiViste.Add(v.GetName2());
                v = v.GetNextView() as IView;
            }
            Console.WriteLine("quote in tavola, prima: " + Quote());

            // 2. LE QUOTE DEL PEZZO SONO MARCATE PER IL DISEGNO?
            // E' la domanda che decide se il problema sta nella tavola o nel
            // pezzo. Si apre il modello referenziato e si guarda.
            string percorsoModello = null;
            var prima = _tavola.GetFirstView() as IView;
            while (prima != null && percorsoModello == null)
            {
                var n = prima.GetReferencedModelName();
                if (!string.IsNullOrEmpty(n)) percorsoModello = n;
                prima = prima.GetNextView() as IView;
            }
            Console.WriteLine("modello referenziato: " + (percorsoModello ?? "-"));
            if (percorsoModello != null) GuardaLeQuoteDelPezzo(sw, percorsoModello);

            // 3. I TENTATIVI.
            Console.WriteLine();
            Console.WriteLine("=== Tentativi");

            int daModello = (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel;
            int marcate = (int)swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing;
            int nonMarcate = (int)swInsertAnnotation_e.swInsertDimensionsNotMarkedForDrawing;
            int generico = (int)swInsertAnnotation_e.swInsertDimensions;

            Prova("v3 marcate, tutte le viste", () => V3(daModello, marcate, true, false));
            Prova("v3 non marcate, tutte le viste", () => V3(daModello, nonMarcate, true, false));
            Prova("v3 generico, tutte le viste", () => V3(daModello, generico, true, false));
            Prova("v3 marcate|non marcate|generico", () => V3(daModello, marcate | nonMarcate | generico, true, false));
            Prova("v3 generico, posizione dallo schizzo", () => V3(daModello, generico, true, true));

            // Le stesse, ma con una vista ATTIVATA prima. E' l'ipotesi
            // principale: l'API dice «tutte le viste» ma potrebbe volere
            // comunque un contesto di vista.
            foreach (var nome in nomiViste)
            {
                Prova("v3 generico dopo ActivateView(«" + nome + "»)", () =>
                {
                    _tavola.ActivateView(nome);
                    return V3(daModello, generico, true, false);
                });
                Prova("v3 generico su QUELLA vista sola", () =>
                {
                    _tavola.ActivateView(nome);
                    return V3(daModello, generico, false, false);
                });
            }

            // La vista SELEZIONATA per identificativo, che non e' la stessa
            // cosa di ActivateView: una e' il contesto di modifica, l'altra
            // e' cio' che l'API vede come selezione.
            foreach (var nome in nomiViste)
            {
                Prova("v3 generico con la vista «" + nome + "» SELEZIONATA", () =>
                {
                    _doc.ClearSelection2(true);
                    _doc.Extension.SelectByID2(
                        nome, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                    return V3(daModello, generico, false, false);
                });
            }

            // TUTTI I VALORI DELLA FONTE, uno per uno.
            // `swImportModelItemsFromEntireModel` vale ZERO in questo interop,
            // e zero e' anche quello che si passa per sbaglio quando una
            // costante non esiste: se l'API volesse 1 per «tutto il modello»,
            // passandole 0 risponderebbe un elenco vuoto senza lamentarsi.
            // Non si deduce: si provano.
            for (int fonte = 0; fonte <= 4; fonte++)
            {
                int f = fonte;
                Prova("v3 fonte=" + f + " marcate", () => V3(f, marcate, true, false));
                Prova("v3 fonte=" + f + " generico", () => V3(f, generico, true, false));
            }

            // I valori veri delle costanti, perche' un enum sbagliato da' zero
            // senza dire niente.
            Console.WriteLine("  (fonte=" + daModello + " marcate=" + marcate +
                " nonMarcate=" + nonMarcate + " generico=" + generico + ")");

            // LE VERSIONI VECCHIE, che hanno il parametro `AllTypes`: quello
            // che la 3 ha tolto. Con AllTypes a vero la maschera dei tipi non
            // conta piu', quindi se il guaio fosse li' si vedrebbe qui.
            Prova("v1 AllTypes", () =>
            {
                _tavola.InsertModelAnnotations(daModello, true, 0, true);
                return -1;
            });
            Prova("v2 AllTypes", () =>
            {
                _tavola.InsertModelAnnotations2(daModello, true, 0, true, false, false);
                return -1;
            });
            Prova("v2 AllTypes generico", () =>
            {
                _tavola.InsertModelAnnotations2(daModello, true, generico, true, false, false);
                return -1;
            });
            Prova("v1 fonte=1 AllTypes", () =>
            {
                _tavola.InsertModelAnnotations(1, true, 0, true);
                return -1;
            });

            Console.WriteLine();
            Console.WriteLine("quote in tavola, alla fine: " + Quote());
            // SI RICONTA IN UN ALTRO MODO. Se «zero quote» venisse da un conto
            // sbagliato invece che da un inserimento mancato, il numero delle
            // ANNOTAZIONI lo direbbe: sono due strade diverse per la stessa
            // domanda, e due zeri d'accordo valgono piu' di uno.
            var u = _tavola.GetFirstView() as IView;
            while (u != null)
            {
                Console.WriteLine("  vista «" + u.GetName2() + "»: annotazioni " +
                    u.GetAnnotationCount() + ", quote " +
                    ((u.GetDisplayDimensions() as object[]) == null
                        ? 0 : ((object[])u.GetDisplayDimensions()).Length));
                u = u.GetNextView() as IView;
            }
            return 0;
        }

        private static int V3(int fonte, int tipi, bool tutteLeViste, bool dalloSchizzo)
        {
            var tornate = _tavola.InsertModelAnnotations3(
                fonte, tipi, tutteLeViste, false, false, dalloSchizzo) as object[];
            return tornate == null ? 0 : tornate.Length;
        }

        private static void Prova(string cosa, Func<int> quello)
        {
            int prima = Quote();
            int restituite;
            try { restituite = quello(); }
            catch (Exception e)
            {
                Console.WriteLine("  XX " + cosa + " -> ha sollevato: " + e.Message);
                return;
            }
            int dopo = Quote();
            string esito = dopo > prima ? "  ok " : "  -- ";
            Console.WriteLine(esito + cosa + " -> restituite " +
                (restituite < 0 ? "?" : restituite.ToString()) +
                ", quote in tavola " + prima + " -> " + dopo);
        }

        /// <summary>Quante quote hanno in tutto le viste della tavola.</summary>
        private static int Quote()
        {
            int n = 0;
            try
            {
                var v = _tavola.GetFirstView() as IView;
                while (v != null)
                {
                    var d = v.GetDisplayDimensions() as object[];
                    if (d != null) n += d.Length;
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return n;
        }

        /// <summary>
        /// Le quote del pezzo, e se sono marcate per il disegno.
        ///
        /// E' la domanda che separa «la tavola non le prende» da «il pezzo non
        /// le offre», e senza risposta si continua a modificare il lato
        /// sbagliato.
        /// </summary>
        private static void GuardaLeQuoteDelPezzo(ISldWorks sw, string percorso)
        {
            IModelDoc2 pezzo = null;
            try { pezzo = sw.GetOpenDocumentByName(percorso) as IModelDoc2; }
            catch { }
            if (pezzo == null)
            {
                Console.WriteLine("  (il pezzo non risulta aperto: non posso guardarne le quote)");
                return;
            }

            int totali = 0, marcate = 0;
            try
            {
                var f = pezzo.FirstFeature() as IFeature;
                while (f != null)
                {
                    var d = f.GetFirstDisplayDimension() as IDisplayDimension;
                    while (d != null)
                    {
                        totali++;
                        try { if (d.MarkedForDrawing) marcate++; } catch { }
                        d = f.GetNextDisplayDimension(d) as IDisplayDimension;
                    }
                    // anche gli schizzi sotto la feature
                    var sotto = f.GetFirstSubFeature() as IFeature;
                    while (sotto != null)
                    {
                        var ds = sotto.GetFirstDisplayDimension() as IDisplayDimension;
                        while (ds != null)
                        {
                            totali++;
                            try { if (ds.MarkedForDrawing) marcate++; } catch { }
                            ds = sotto.GetNextDisplayDimension(ds) as IDisplayDimension;
                        }
                        sotto = sotto.GetNextSubFeature() as IFeature;
                    }
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch (Exception e) { Console.WriteLine("  giro delle quote: " + e.Message); }
            Console.WriteLine("  quote del pezzo: " + totali + ", di cui marcate per il disegno: " + marcate);
        }
    }
}
