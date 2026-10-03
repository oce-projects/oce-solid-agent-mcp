using System;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Controlli.DettaglioInTavola
{
    /// <summary>
    /// LA VISTA DI DETTAGLIO, MISURATA INVECE CHE DEDOTTA (voce GT4).
    ///
    /// La misura 0.6 ha chiuso la sezione — linea sul foglio, `Select4`,
    /// `CreateSectionViewAt5`, quattro viste che diventano cinque — e ha
    /// lasciato scritta una cosa onesta:
    ///
    ///     «Resta non misurato il DETTAGLIO (CreateDetailViewAt4, che vuole un
    ///      cerchio invece di una linea). Per analogia dovrebbe funzionare
    ///      uguale — ma "per analogia" e' proprio il salto che qui e' gia'
    ///      costato una correzione.»
    ///
    /// Questa sonda toglie l'analogia di mezzo. Stessa forma di
    /// `SezioneInTavola`, con tre differenze che potrebbero non essere
    /// dettagli:
    ///
    ///   - si traccia un CERCHIO invece di una linea, e sta DENTRO la vista
    ///     (una sezione vuole attraversarla, un dettaglio vuole starci dentro);
    ///   - la firma ha dodici argomenti invece di sette;
    ///   - il cerchio e' un `ISketchSegment` come la linea, quindi `Select4`
    ///     dovrebbe bastare — ed e' la parte che va vista, non supposta.
    ///
    ///     IDrawingDoc :: CreateDetailViewAt4(Double X, Double Y, Double Z,
    ///         Int32 Style, Double Scale1, Double Scale2, String LabelIn,
    ///         Int32 Showtype, Boolean FullOutline, Boolean JaggedOutline,
    ///         Boolean NoOutline, Int32 ShapeIntensity)
    ///
    /// E POI SI MISURA SE E' USCITA: il conto delle viste prima e dopo, il tipo
    /// della vista nuova, e quante circonferenze di dettaglio ha la vista di
    /// partenza (`GetDetailCircleCount2`). Il valore di ritorno e' una
    /// promessa; il conto e' la misura.
    ///
    ///     DettaglioInTavola.exe        lavora sulla tavola attiva
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
                Console.Error.WriteLine("Il documento attivo non e' una tavola.");
                return 2;
            }
            var tavola = (IDrawingDoc)doc;
            Console.WriteLine("tavola: " + doc.GetTitle());

            // 1. LA VISTA DA DETTAGLIARE. La prima «view» e' il foglio: si salta.
            IView bersaglio = null;
            int viste = 0;
            var v = tavola.GetFirstView() as IView;
            while (v != null && viste < 50)
            {
                viste++;
                if (bersaglio == null && v.Type != (int)swDrawingViewTypes_e.swDrawingSheet)
                    bersaglio = v;
                v = v.GetNextView() as IView;
            }
            Console.WriteLine("viste sul foglio, prima: " + viste + " (il foglio compreso)");
            if (bersaglio == null)
            {
                Console.Error.WriteLine("Sul foglio non c'e' nessuna vista da dettagliare.");
                return 1;
            }

            var sagoma = bersaglio.GetOutline() as double[];
            if (sagoma == null || sagoma.Length < 4)
            {
                Console.Error.WriteLine("Non riesco a leggere l'ingombro della vista.");
                return 1;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "vista «{0}» (tipo {1}), ingombro x {2:0.####}..{3:0.####} y {4:0.####}..{5:0.####} m",
                bersaglio.GetName2(), bersaglio.Type, sagoma[0], sagoma[2], sagoma[1], sagoma[3]));

            // DUE USCITE, E NESSUNA E' OVVIA. Vedi il commento in testa al
            // file: si stampano tutte e due invece di sceglierne una.
            int cerchiPrima = -1, resoPrima = -1;
            try { resoPrima = bersaglio.GetDetailCircleCount2(out cerchiPrima); }
            catch (Exception e) { Console.WriteLine("  GetDetailCircleCount2: " + e.Message); }
            Console.WriteLine("circonferenze PRIMA: ritorno=" + resoPrima + "  out=" + cerchiPrima);

            // 2. IL CERCHIO, DENTRO LA VISTA.
            //
            // Un quarto del lato minore come raggio: abbastanza grande da
            // contenere geometria, abbastanza piccolo da stare dentro. Un
            // cerchio che sborda e' il caso limite gratuito di questa API.
            double cx = (sagoma[0] + sagoma[2]) / 2.0;
            double cy = (sagoma[1] + sagoma[3]) / 2.0;
            double minore = Math.Min(sagoma[2] - sagoma[0], sagoma[3] - sagoma[1]);
            double raggio = minore / 4.0;

            Console.WriteLine("EditSheet()...");
            try { tavola.EditSheet(); } catch (Exception e) { Console.WriteLine("  EditSheet: " + e.Message); }

            doc.ClearSelection2(true);
            ISketchSegment cerchio = null;
            try
            {
                cerchio = doc.SketchManager.CreateCircleByRadius(cx, cy, 0.0, raggio) as ISketchSegment;
            }
            catch (Exception e) { Console.WriteLine("  CreateCircleByRadius: " + e.Message); }
            if (cerchio == null)
            {
                Console.Error.WriteLine("Niente cerchio, niente dettaglio.");
                return 1;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "cerchio tracciato in ({0:0.####}, {1:0.####}) raggio {2:0.####} m", cx, cy, raggio));

            // Lo schizzo si CHIUDE prima, come per la sezione.
            try { doc.SketchManager.InsertSketch(true); } catch { }

            // 3. LA SELEZIONE.
            doc.ClearSelection2(true);
            bool presa = false;
            try { presa = cerchio.Select4(false, null); }
            catch (Exception e) { Console.WriteLine("  Select4: " + e.Message); }
            var selezione = (ISelectionMgr)doc.SelectionManager;
            Console.WriteLine("cerchio selezionato: " + presa +
                ", oggetti selezionati: " + selezione.GetSelectedObjectCount2(-1) +
                ", tipo: " + (selezione.GetSelectedObjectCount2(-1) > 0
                    ? selezione.GetSelectedObjectType3(1, -1).ToString() : "-"));

            // 4. IL DETTAGLIO, a fianco della vista.
            double xDettaglio = sagoma[2] + (sagoma[2] - sagoma[0]) * 0.8;
            double yDettaglio = (sagoma[1] + sagoma[3]) / 2.0;
            object nata = null;
            try
            {
                // Style 0, scala 2:1, etichetta B, Showtype 0, senza contorni
                // speciali. Sono i valori piu' neutri che la firma ammette: qui
                // si misura SE nasce, non come e' vestita.
                nata = tavola.CreateDetailViewAt4(
                    xDettaglio, yDettaglio, 0.0,
                    (int)swDetViewStyle_e.swDetViewSTANDARD, 2.0, 1.0, "B",
                    (int)swDetCircleShowType_e.swDetCircleCIRCLE, false, false, false, 0);
            }
            catch (Exception e)
            { Console.WriteLine("  CreateDetailViewAt4 ha sollevato: " + e.Message); }
            Console.WriteLine("CreateDetailViewAt4 ha risposto: " +
                (nata == null ? "null" : "un oggetto"));

            // IL RISCONTRO: quante viste ci sono adesso, e di che tipo e' l'ultima.
            int dopo = 0;
            IView ultima = null;
            var w = tavola.GetFirstView() as IView;
            while (w != null && dopo < 50)
            {
                dopo++;
                ultima = w;
                w = w.GetNextView() as IView;
            }
            Console.WriteLine("viste sul foglio, dopo:  " + dopo);

            int cerchiDopo = -1, resoDopo = -1;
            try { resoDopo = bersaglio.GetDetailCircleCount2(out cerchiDopo); } catch { }
            Console.WriteLine("circonferenze DOPO : ritorno=" + resoDopo + "  out=" + cerchiDopo);

            // E LA VERSIONE SENZA IL «2», che e' l'altra strada che non si
            // e' provata. Se rispondesse un conto pulito, il «2» sarebbe una
            // complicazione gratuita e va scritto.
            try
            {
                int vecchio = bersaglio.GetDetailCircleCount();
                Console.WriteLine("GetDetailCircleCount (senza il 2): " + vecchio);
            }
            catch (Exception e)
            { Console.WriteLine("GetDetailCircleCount (senza il 2): " + e.Message); }

            // E il tratteggio anche qui: un dettaglio mostra la stessa
            // geometria ingrandita, quindi se la vista di partenza non e'
            // una sezione non ci si aspetta campitura. Si guarda lo stesso,
            // perche' «non me lo aspetto» non e' una misura.
            if (ultima != null)
            {
                int facce = -1;
                try { facce = ultima.GetFaceHatchCount(); } catch { }
                Console.WriteLine("tratteggio di faccia sul dettaglio: " + facce);
            }

            if (dopo > viste)
            {
                string nome = "";
                int tipo = -1;
                try { nome = ultima.GetName2(); } catch { }
                try { tipo = ultima.Type; } catch { }
                Console.WriteLine("  -> IL DETTAGLIO C'E'. Ultima vista: «" + nome + "», tipo " + tipo +
                    (tipo == (int)swDrawingViewTypes_e.swDrawingDetailView
                        ? " (swDrawingDetailView)" : " (NON e' un dettaglio!)"));
            }
            else
            {
                Console.WriteLine("  -> nessuna vista in piu': il dettaglio NON e' nato");
            }

            try { doc.EditRebuild3(); } catch { }
            return dopo > viste ? 0 : 1;
        }
    }
}
