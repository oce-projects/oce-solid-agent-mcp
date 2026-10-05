using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.DrawingDimensions
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
    ///     DrawingDimensions.exe        lavora sulla tavola attiva
    /// </summary>
    internal static class Program
    {
        private static IDrawingDoc _drawing;
        private static IModelDoc2 _doc;

        private static int Main()
        {
            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS non e' aperto."); return 2; }
            var sw = (ISldWorks)raw;

            _doc = (IModelDoc2)sw.ActiveDoc;
            if (_doc == null || _doc.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                Console.Error.WriteLine("Serve una tavola attiva.");
                return 2;
            }
            _drawing = (IDrawingDoc)_doc;
            Console.WriteLine("tavola: " + _doc.GetTitle());

            // 1. LE VISTE, E COSA HANNO GIA'.
            var viewNames = new List<string>();
            var v = _drawing.GetFirstView() as IView;
            while (v != null)
            {
                var ann = v.GetAnnotations() as object[];
                var dim = v.GetDisplayDimensions() as object[];
                object referred = null;
                try { referred = v.ReferencedDocument; } catch { }
                Console.WriteLine("  vista «" + v.GetName2() + "» tipo " + v.Type +
                    ", modello: «" + (v.GetReferencedModelName() ?? "") + "»" +
                    ", RISOLTA: " + (referred != null) +
                    ", annotazioni " + (ann == null ? 0 : ann.Length) +
                    ", quote " + (dim == null ? 0 : dim.Length));
                if (v.Type != (int)swDrawingViewTypes_e.swDrawingSheet)
                    viewNames.Add(v.GetName2());
                v = v.GetNextView() as IView;
            }
            Console.WriteLine("quote in tavola, prima: " + Quote());

            // 2. LE QUOTE DEL PEZZO SONO MARCATE PER IL DISEGNO?
            // E' la domanda che decide se il problema sta nella tavola o nel
            // pezzo. Si apre il modello referenziato e si guarda.
            string modelPath = null;
            var before = _drawing.GetFirstView() as IView;
            while (before != null && modelPath == null)
            {
                var n = before.GetReferencedModelName();
                if (!string.IsNullOrEmpty(n)) modelPath = n;
                before = before.GetNextView() as IView;
            }
            Console.WriteLine("modello referenziato: " + (modelPath ?? "-"));
            if (modelPath != null) LookAtPartDimensions(sw, modelPath);

            // 3. I TENTATIVI.
            Console.WriteLine();
            Console.WriteLine("=== Tentativi");

            int fromModel = (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel;
            int marked = (int)swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing;
            int notMarked = (int)swInsertAnnotation_e.swInsertDimensionsNotMarkedForDrawing;
            int generic = (int)swInsertAnnotation_e.swInsertDimensions;

            Test("v3 marcate, tutte le viste", () => V3(fromModel, marked, true, false));
            Test("v3 non marcate, tutte le viste", () => V3(fromModel, notMarked, true, false));
            Test("v3 generico, tutte le viste", () => V3(fromModel, generic, true, false));
            Test("v3 marcate|non marcate|generico", () => V3(fromModel, marked | notMarked | generic, true, false));
            Test("v3 generico, posizione dallo schizzo", () => V3(fromModel, generic, true, true));

            // Le stesse, ma con una vista ATTIVATA prima. E' l'ipotesi
            // principale: l'API dice «tutte le viste» ma potrebbe volere
            // comunque un contesto di vista.
            foreach (var itemName in viewNames)
            {
                Test("v3 generico dopo ActivateView(«" + itemName + "»)", () =>
                {
                    _drawing.ActivateView(itemName);
                    return V3(fromModel, generic, true, false);
                });
                Test("v3 generico su QUELLA vista sola", () =>
                {
                    _drawing.ActivateView(itemName);
                    return V3(fromModel, generic, false, false);
                });
            }

            // La vista SELEZIONATA per identificativo, che non e' la stessa
            // cosa di ActivateView: una e' il contesto di modifica, l'altra
            // e' cio' che l'API vede come selezione.
            foreach (var itemName in viewNames)
            {
                Test("v3 generico con la vista «" + itemName + "» SELEZIONATA", () =>
                {
                    _doc.ClearSelection2(true);
                    _doc.Extension.SelectByID2(
                        itemName, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                    return V3(fromModel, generic, false, false);
                });
            }

            // TUTTI I VALORI DELLA FONTE, uno per uno.
            // `swImportModelItemsFromEntireModel` vale ZERO in questo interop,
            // e zero e' anche quello che si passa per sbaglio quando una
            // costante non esiste: se l'API volesse 1 per «tutto il modello»,
            // passandole 0 risponderebbe un elenco vuoto senza lamentarsi.
            // Non si deduce: si provano.
            for (int source = 0; source <= 4; source++)
            {
                int f = source;
                Test("v3 fonte=" + f + " marcate", () => V3(f, marked, true, false));
                Test("v3 fonte=" + f + " generico", () => V3(f, generic, true, false));
            }

            // I valori veri delle costanti, perche' un enum sbagliato da' zero
            // senza dire niente.
            Console.WriteLine("  (fonte=" + fromModel + " marcate=" + marked +
                " nonMarcate=" + notMarked + " generico=" + generic + ")");

            // LE VERSIONI VECCHIE, che hanno il parametro `AllTypes`: quello
            // che la 3 ha tolto. Con AllTypes a vero la maschera dei tipi non
            // conta piu', quindi se il guaio fosse li' si vedrebbe qui.
            Test("v1 AllTypes", () =>
            {
                _drawing.InsertModelAnnotations(fromModel, true, 0, true);
                return -1;
            });
            Test("v2 AllTypes", () =>
            {
                _drawing.InsertModelAnnotations2(fromModel, true, 0, true, false, false);
                return -1;
            });
            Test("v2 AllTypes generico", () =>
            {
                _drawing.InsertModelAnnotations2(fromModel, true, generic, true, false, false);
                return -1;
            });
            Test("v1 fonte=1 AllTypes", () =>
            {
                _drawing.InsertModelAnnotations(1, true, 0, true);
                return -1;
            });

            Console.WriteLine();
            Console.WriteLine("quote in tavola, alla fine: " + Quote());
            // SI RICONTA IN UN ALTRO MODO. Se «zero quote» venisse da un conto
            // sbagliato invece che da un inserimento mancato, il numero delle
            // ANNOTAZIONI lo direbbe: sono due strade diverse per la stessa
            // domanda, e due zeri d'accordo valgono piu' di uno.
            var u = _drawing.GetFirstView() as IView;
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

        private static int V3(int source, int types, bool allViews, bool fromSketch)
        {
            var returned = _drawing.InsertModelAnnotations3(
                source, types, allViews, false, false, fromSketch) as object[];
            return returned == null ? 0 : returned.Length;
        }

        private static void Test(string what, Func<int> that)
        {
            int before = Quote();
            int returnedItems;
            try { returnedItems = that(); }
            catch (Exception e)
            {
                Console.WriteLine("  XX " + what + " -> ha sollevato: " + e.Message);
                return;
            }
            int after = Quote();
            string outcome = after > before ? "  ok " : "  -- ";
            Console.WriteLine(outcome + what + " -> restituite " +
                (returnedItems < 0 ? "?" : returnedItems.ToString()) +
                ", quote in tavola " + before + " -> " + after);
        }

        /// <summary>Quante quote hanno in tutto le viste della tavola.</summary>
        private static int Quote()
        {
            int n = 0;
            try
            {
                var v = _drawing.GetFirstView() as IView;
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
        private static void LookAtPartDimensions(ISldWorks sw, string filePath)
        {
            IModelDoc2 part = null;
            try { part = sw.GetOpenDocumentByName(filePath) as IModelDoc2; }
            catch { }
            if (part == null)
            {
                Console.WriteLine("  (il pezzo non risulta aperto: non posso guardarne le quote)");
                return;
            }

            int totali = 0, marked = 0;
            try
            {
                var f = part.FirstFeature() as IFeature;
                while (f != null)
                {
                    var d = f.GetFirstDisplayDimension() as IDisplayDimension;
                    while (d != null)
                    {
                        totali++;
                        try { if (d.MarkedForDrawing) marked++; } catch { }
                        d = f.GetNextDisplayDimension(d) as IDisplayDimension;
                    }
                    // anche gli schizzi sotto la feature
                    var below = f.GetFirstSubFeature() as IFeature;
                    while (below != null)
                    {
                        var ds = below.GetFirstDisplayDimension() as IDisplayDimension;
                        while (ds != null)
                        {
                            totali++;
                            try { if (ds.MarkedForDrawing) marked++; } catch { }
                            ds = below.GetNextDisplayDimension(ds) as IDisplayDimension;
                        }
                        below = below.GetNextSubFeature() as IFeature;
                    }
                    f = f.GetNextFeature() as IFeature;
                }
            }
            catch (Exception e) { Console.WriteLine("  giro delle quote: " + e.Message); }
            Console.WriteLine("  quote del pezzo: " + totali + ", di cui marcate per il disegno: " + marked);
        }
    }
}
