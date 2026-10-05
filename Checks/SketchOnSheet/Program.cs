using System;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Checks.SketchOnSheet
{
    /// <summary>
    /// LA META' VIVA DELLA MISURA 0.6: si puo' disegnare SUL FOGLIO di una
    /// tavola, e con che cosa si riprende quello che si e' disegnato?
    ///
    /// PERCHE' IMPORTA. `CreateSectionViewAt5` vuole una LINEA gia' selezionata
    /// e `CreateDetailViewAt4` un CERCHIO: nessuna delle due prende l'entita'
    /// in firma. Finche' non si sa tracciare e riselezionare un'entita' sul
    /// foglio, sezioni e dettagli in tavola non si possono fare — e sono la
    /// prima cosa che manca a una tavola d'officina vera.
    ///
    /// Per riflessione si era gia' trovato che il foglio ha uno schizzo suo
    /// (`ISheet.GetTemplateSketch`) e che `EditSheet()` entra nel suo contesto.
    /// **Letto non e' misurato**: qui si traccia davvero e si guarda cosa
    /// succede.
    ///
    /// COSA STAMPA. Un numero per ogni domanda, cosi' la nota nel RAG si scrive
    /// copiando invece che ricordando. Dove c'e' «-» la domanda non ha avuto
    /// risposta, e si vede.
    ///
    ///     SketchOnSheet.exe        lavora sulla tavola attiva
    ///
    /// Vuole una TAVOLA aperta e davanti. Non ne apre una da solo: creare una
    /// tavola vuol dire scegliere un modello di foglio, e quella scelta e' di
    /// chi lavora.
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            object raw;
            try { raw = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS non e' aperto."); return 2; }
            var sw = (ISldWorks)raw;

            var doc = (IModelDoc2)sw.ActiveDoc;
            if (doc == null) { Console.Error.WriteLine("Nessun documento attivo."); return 2; }
            if (doc.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                Console.Error.WriteLine(
                    "Il documento attivo e' «" + doc.GetTitle() + "», che non e' una tavola.");
                return 2;
            }
            var drawing = (IDrawingDoc)doc;
            Console.WriteLine("tavola: " + doc.GetTitle());

            var sheet = (ISheet)drawing.GetCurrentSheet();
            Console.WriteLine("foglio: " + sheet.GetName());
            var property = sheet.GetProperties2() as double[];
            if (property != null && property.Length >= 7)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "misura foglio: {0:0.###} x {1:0.###} m", property[5], property[6]));

            // 1. QUANTE ENTITA' HA GIA' LO SCHIZZO DEL FOGLIO.
            // Il numero di partenza serve: «ne ho una» non vuol dire niente se
            // non si sa quante ce n'erano prima.
            int before = SheetSegments(sheet);
            Console.WriteLine("segmenti nello schizzo del foglio, prima: " + Show(before));

            // 2. SI ENTRA NEL CONTESTO DEL FOGLIO.
            Console.WriteLine("EditSheet()...");
            try { drawing.EditSheet(); }
            catch (Exception e) { Console.WriteLine("  EditSheet ha sollevato: " + e.Message); }

            // 3. SI TRACCIA. Le coordinate sono in METRI e nel sistema del
            //    foglio: 0,05 m sono 50 mm dal bordo in basso a sinistra.
            var sketches = doc.SketchManager;
            ISketchSegment segment = null;
            try
            {
                segment = sketches.CreateLine(0.05, 0.05, 0.0, 0.15, 0.05, 0.0) as ISketchSegment;
            }
            catch (Exception e) { Console.WriteLine("  CreateLine ha sollevato: " + e.Message); }
            // ATTENZIONE: `ISketchSegment.GetType()` NON e' quello di Object.
            // L'interop lo nasconde con un metodo suo che torna il TIPO DI
            // SEGMENTO come intero (0 = linea, 1 = arco, 3 = spline). Scriverci
            // `.Name` dietro non compila, ed e' un bene: se compilasse si
            // leggerebbe un nome di classe al posto di una linea.
            Console.WriteLine("CreateLine ha risposto: " +
                (segment == null ? "null" : "segmento di tipo " + segment.GetType() +
                 (segment.GetType() == 0 ? " (linea)" : "")));

            int after = SheetSegments(sheet);
            Console.WriteLine("segmenti nello schizzo del foglio, dopo:  " + Show(after));
            if (before >= 0 && after >= 0)
                Console.WriteLine(after > before
                    ? "  -> il segmento E' FINITO nello schizzo del foglio"
                    : "  -> NON e' finito nello schizzo del foglio: sta da un'altra parte");

            if (segment == null)
            {
                Console.WriteLine();
                Console.WriteLine("Senza segmento non si puo' provare la selezione.");
                return 1;
            }

            // 3b. E ALLORA DOV'E' FINITA?
            // La domanda non e' accademica: per riprendere una linea che non
            // si e' appena disegnata bisogna sapere in quale schizzo cercarla.
            var active = sketches.ActiveSketch as ISketch;
            Console.WriteLine("schizzo attivo: " + (active == null ? "nessuno" : Name(active)));
            if (active != null)
            {
                var inside = active.GetSketchSegments() as object[];
                Console.WriteLine("  segmenti dello schizzo attivo: " +
                    (inside == null ? "-" : inside.Length.ToString()));
            }

            // La prima vista di una tavola E' IL FOGLIO: le entita' disegnate
            // sul foglio stanno nel suo schizzo, non in quello del formato.
            try
            {
                var view = drawing.GetFirstView() as IView;
                int n = 0;
                while (view != null)
                {
                    var its = view.GetSketch() as ISketch;
                    var segm = its == null ? null : its.GetSketchSegments() as object[];
                    Console.WriteLine("  vista " + n + ": «" + view.GetName2() + "»" +
                        ", tipo " + view.Type +
                        ", segmenti nel suo schizzo: " +
                        (segm == null ? "-" : segm.Length.ToString()));
                    view = view.GetNextView() as IView;
                    n++;
                    if (n > 20) break;
                }
            }
            catch (Exception e) { Console.WriteLine("  giro delle viste: " + e.Message); }

            // 4. LA DOMANDA VERA: SI RIPRENDE SENZA PASSARE DA UN NOME?
            // `CreateSectionViewAt5` vuole la linea selezionata, e una stringa
            // di nome sarebbe fragile (localizzata, e diversa a ogni schizzo).
            var selection = (ISelectionMgr)doc.SelectionManager;
            doc.ClearSelection2(true);
            bool taken = false;
            try { taken = segment.Select4(false, null); }
            catch (Exception e) { Console.WriteLine("  Select4 ha sollevato: " + e.Message); }
            Console.WriteLine("Select4(false, null): " + taken);
            Console.WriteLine("oggetti selezionati: " +
                selection.GetSelectedObjectCount2(-1));
            if (selection.GetSelectedObjectCount2(-1) > 0)
            {
                int typ = selection.GetSelectedObjectType3(1, -1);
                Console.WriteLine("tipo selezionato: " + typ + "  (" + TypeNameOf(typ) + ")");
                Console.WriteLine("marca: " + selection.GetSelectedObjectMark(1));
            }

            // 5. E CON UNA MARCA? Le API che vogliono un'entita' «di un certo
            //    ruolo» la cercano per marca, non per posizione nell'elenco.
            doc.ClearSelection2(true);
            bool withMark = false;
            try
            {
                var data = (SelectData)selection.CreateSelectData();
                data.Mark = 1;
                withMark = segment.Select4(false, data);
            }
            catch (Exception e) { Console.WriteLine("  Select4 con marca ha sollevato: " + e.Message); }
            Console.WriteLine("Select4 con Mark=1: " + withMark +
                ", marca riletta: " +
                (selection.GetSelectedObjectCount2(-1) > 0
                    ? selection.GetSelectedObjectMark(1).ToString()
                    : "-"));

            // 6. IL NOME, se ce n'e' uno. Non per usarlo — per sapere se
            //    esiste e com'e' fatto.
            try
            {
                var entity = segment as IEntity;
                Console.WriteLine("il segmento e' anche IEntity: " + (entity != null));
            }
            catch { }

            Console.WriteLine();
            Console.WriteLine("Lo schizzo resta aperto: guardalo, poi annulla se non lo vuoi.");
            return 0;
        }

        /// <summary>
        /// Quanti segmenti ha lo schizzo del foglio. -1 se non si e' potuto
        /// contare: uno zero finto sarebbe peggio, perche' farebbe leggere
        /// «e' cresciuto da 0 a 1» a un controllo che non ha guardato niente.
        /// </summary>
        private static int SheetSegments(ISheet sheet)
        {
            try
            {
                var sketch = sheet.GetTemplateSketch() as ISketch;
                if (sketch == null) return -1;
                var segments = sketch.GetSketchSegments() as object[];
                return segments == null ? 0 : segments.Length;
            }
            catch { return -1; }
        }

        /// <summary>Il nome di uno schizzo, se si riesce a leggerlo.</summary>
        private static string Name(ISketch sketch)
        {
            try
            {
                var f = sketch as IFeature;
                return f == null ? "(senza nome)" : f.Name;
            }
            catch { return "(nome illeggibile)"; }
        }

        private static string Show(int n)
        {
            return n < 0 ? "- (non contati)" : n.ToString();
        }

        private static string TypeNameOf(int typ)
        {
            switch (typ)
            {
                case (int)swSelectType_e.swSelSKETCHSEGS: return "swSelSKETCHSEGS";
                case (int)swSelectType_e.swSelEDGES: return "swSelEDGES";
                case (int)swSelectType_e.swSelSKETCHES: return "swSelSKETCHES";
                case (int)swSelectType_e.swSelNOTHING: return "swSelNOTHING";
                default: return "?";
            }
        }
    }
}
