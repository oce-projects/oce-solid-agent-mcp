using System;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Controlli.SchizzoSulFoglio
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
    ///     SchizzoSulFoglio.exe        lavora sulla tavola attiva
    ///
    /// Vuole una TAVOLA aperta e davanti. Non ne apre una da solo: creare una
    /// tavola vuol dire scegliere un modello di foglio, e quella scelta e' di
    /// chi lavora.
    /// </summary>
    internal static class Programma
    {
        private static int Main()
        {
            object grezzo;
            try { grezzo = Marshal.GetActiveObject("SldWorks.Application"); }
            catch (COMException) { Console.Error.WriteLine("SOLIDWORKS non e' aperto."); return 2; }
            var sw = (ISldWorks)grezzo;

            var doc = (IModelDoc2)sw.ActiveDoc;
            if (doc == null) { Console.Error.WriteLine("Nessun documento attivo."); return 2; }
            if (doc.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                Console.Error.WriteLine(
                    "Il documento attivo e' «" + doc.GetTitle() + "», che non e' una tavola.");
                return 2;
            }
            var tavola = (IDrawingDoc)doc;
            Console.WriteLine("tavola: " + doc.GetTitle());

            var foglio = (ISheet)tavola.GetCurrentSheet();
            Console.WriteLine("foglio: " + foglio.GetName());
            var proprieta = foglio.GetProperties2() as double[];
            if (proprieta != null && proprieta.Length >= 7)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "misura foglio: {0:0.###} x {1:0.###} m", proprieta[5], proprieta[6]));

            // 1. QUANTE ENTITA' HA GIA' LO SCHIZZO DEL FOGLIO.
            // Il numero di partenza serve: «ne ho una» non vuol dire niente se
            // non si sa quante ce n'erano prima.
            int prima = SegmentiDelFoglio(foglio);
            Console.WriteLine("segmenti nello schizzo del foglio, prima: " + Mostra(prima));

            // 2. SI ENTRA NEL CONTESTO DEL FOGLIO.
            Console.WriteLine("EditSheet()...");
            try { tavola.EditSheet(); }
            catch (Exception e) { Console.WriteLine("  EditSheet ha sollevato: " + e.Message); }

            // 3. SI TRACCIA. Le coordinate sono in METRI e nel sistema del
            //    foglio: 0,05 m sono 50 mm dal bordo in basso a sinistra.
            var schizzi = doc.SketchManager;
            ISketchSegment segmento = null;
            try
            {
                segmento = schizzi.CreateLine(0.05, 0.05, 0.0, 0.15, 0.05, 0.0) as ISketchSegment;
            }
            catch (Exception e) { Console.WriteLine("  CreateLine ha sollevato: " + e.Message); }
            // ATTENZIONE: `ISketchSegment.GetType()` NON e' quello di Object.
            // L'interop lo nasconde con un metodo suo che torna il TIPO DI
            // SEGMENTO come intero (0 = linea, 1 = arco, 3 = spline). Scriverci
            // `.Name` dietro non compila, ed e' un bene: se compilasse si
            // leggerebbe un nome di classe al posto di una linea.
            Console.WriteLine("CreateLine ha risposto: " +
                (segmento == null ? "null" : "segmento di tipo " + segmento.GetType() +
                 (segmento.GetType() == 0 ? " (linea)" : "")));

            int dopo = SegmentiDelFoglio(foglio);
            Console.WriteLine("segmenti nello schizzo del foglio, dopo:  " + Mostra(dopo));
            if (prima >= 0 && dopo >= 0)
                Console.WriteLine(dopo > prima
                    ? "  -> il segmento E' FINITO nello schizzo del foglio"
                    : "  -> NON e' finito nello schizzo del foglio: sta da un'altra parte");

            if (segmento == null)
            {
                Console.WriteLine();
                Console.WriteLine("Senza segmento non si puo' provare la selezione.");
                return 1;
            }

            // 3b. E ALLORA DOV'E' FINITA?
            // La domanda non e' accademica: per riprendere una linea che non
            // si e' appena disegnata bisogna sapere in quale schizzo cercarla.
            var attivo = schizzi.ActiveSketch as ISketch;
            Console.WriteLine("schizzo attivo: " + (attivo == null ? "nessuno" : Nome(attivo)));
            if (attivo != null)
            {
                var dentro = attivo.GetSketchSegments() as object[];
                Console.WriteLine("  segmenti dello schizzo attivo: " +
                    (dentro == null ? "-" : dentro.Length.ToString()));
            }

            // La prima vista di una tavola E' IL FOGLIO: le entita' disegnate
            // sul foglio stanno nel suo schizzo, non in quello del formato.
            try
            {
                var vista = tavola.GetFirstView() as IView;
                int n = 0;
                while (vista != null)
                {
                    var suo = vista.GetSketch() as ISketch;
                    var segm = suo == null ? null : suo.GetSketchSegments() as object[];
                    Console.WriteLine("  vista " + n + ": «" + vista.GetName2() + "»" +
                        ", tipo " + vista.Type +
                        ", segmenti nel suo schizzo: " +
                        (segm == null ? "-" : segm.Length.ToString()));
                    vista = vista.GetNextView() as IView;
                    n++;
                    if (n > 20) break;
                }
            }
            catch (Exception e) { Console.WriteLine("  giro delle viste: " + e.Message); }

            // 4. LA DOMANDA VERA: SI RIPRENDE SENZA PASSARE DA UN NOME?
            // `CreateSectionViewAt5` vuole la linea selezionata, e una stringa
            // di nome sarebbe fragile (localizzata, e diversa a ogni schizzo).
            var selezione = (ISelectionMgr)doc.SelectionManager;
            doc.ClearSelection2(true);
            bool preso = false;
            try { preso = segmento.Select4(false, null); }
            catch (Exception e) { Console.WriteLine("  Select4 ha sollevato: " + e.Message); }
            Console.WriteLine("Select4(false, null): " + preso);
            Console.WriteLine("oggetti selezionati: " +
                selezione.GetSelectedObjectCount2(-1));
            if (selezione.GetSelectedObjectCount2(-1) > 0)
            {
                int tipo = selezione.GetSelectedObjectType3(1, -1);
                Console.WriteLine("tipo selezionato: " + tipo + "  (" + NomeTipo(tipo) + ")");
                Console.WriteLine("marca: " + selezione.GetSelectedObjectMark(1));
            }

            // 5. E CON UNA MARCA? Le API che vogliono un'entita' «di un certo
            //    ruolo» la cercano per marca, non per posizione nell'elenco.
            doc.ClearSelection2(true);
            bool conMarca = false;
            try
            {
                var dati = (SelectData)selezione.CreateSelectData();
                dati.Mark = 1;
                conMarca = segmento.Select4(false, dati);
            }
            catch (Exception e) { Console.WriteLine("  Select4 con marca ha sollevato: " + e.Message); }
            Console.WriteLine("Select4 con Mark=1: " + conMarca +
                ", marca riletta: " +
                (selezione.GetSelectedObjectCount2(-1) > 0
                    ? selezione.GetSelectedObjectMark(1).ToString()
                    : "-"));

            // 6. IL NOME, se ce n'e' uno. Non per usarlo — per sapere se
            //    esiste e com'e' fatto.
            try
            {
                var entita = segmento as IEntity;
                Console.WriteLine("il segmento e' anche IEntity: " + (entita != null));
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
        private static int SegmentiDelFoglio(ISheet foglio)
        {
            try
            {
                var schizzo = foglio.GetTemplateSketch() as ISketch;
                if (schizzo == null) return -1;
                var segmenti = schizzo.GetSketchSegments() as object[];
                return segmenti == null ? 0 : segmenti.Length;
            }
            catch { return -1; }
        }

        /// <summary>Il nome di uno schizzo, se si riesce a leggerlo.</summary>
        private static string Nome(ISketch schizzo)
        {
            try
            {
                var f = schizzo as IFeature;
                return f == null ? "(senza nome)" : f.Name;
            }
            catch { return "(nome illeggibile)"; }
        }

        private static string Mostra(int n)
        {
            return n < 0 ? "- (non contati)" : n.ToString();
        }

        private static string NomeTipo(int tipo)
        {
            switch (tipo)
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
