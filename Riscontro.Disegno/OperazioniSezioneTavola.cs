using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Riscontro.Disegno
{
    /// <summary>
    /// UNA VISTA DI SEZIONE SU UNA TAVOLA (voce GT4).
    ///
    /// ── PERCHE' ESISTE ──────────────────────────────────────────────────
    ///
    /// E' la prima cosa che manca per una tavola meccanica vera. Un pezzo con
    /// una cavita' interna, senza sezione, **non si legge in officina**: le
    /// linee nascoste dicono che c'e' un vuoto, non che forma ha.
    ///
    /// ── L'API NON PRENDE UN'ENTITA' IN FIRMA, E CAMBIA TUTTO ────────────
    ///
    ///     IDrawingDoc :: CreateSectionViewAt5(Double X, Double Y, Double Z,
    ///                        String SectionLabel, Int32 Options,
    ///                        Object ExcludedComponents, Double SectionDepth)
    ///
    /// Non c'e' nessun argomento per dire DOVE tagliare. La linea di taglio va
    /// **disegnata sul foglio e lasciata selezionata**: l'API prende quella.
    /// Questo vuol dire che l'operazione ha cinque passi che devono riuscire
    /// tutti, e ognuno puo' fallire in silenzio.
    ///
    /// Misurato il 14/9/2026 (voce 0.6, attrezzo `Controlli\SezioneInTavola`):
    ///
    ///     linea selezionata: True, oggetti: 1, tipo 10 (swSelSKETCHSEGS)
    ///     viste sul foglio: 4 -> 5
    ///       -> LA SEZIONE C'E'. Ultima vista: «Vista in sezione A-A»
    ///
    /// ── TRE COSE CHE NON SI DEDUCONO, E SONO COSTATE ────────────────────
    ///
    /// 1. **La linea deve attraversare la vista da parte a parte.** Una che la
    ///    sfiora da' una sezione **vuota senza dire niente**.
    /// 2. **Lo schizzo si chiude prima** (`InsertSketch(true)`). Con uno
    ///    schizzo aperto SOLIDWORKS e' dentro un comando, e le API che creano
    ///    viste si comportano diversamente.
    /// 3. **Lo schizzo del foglio non e' `GetTemplateSketch()`** — quello e' il
    ///    cartiglio. «Template» li' vuol dire *formato*. La linea finisce nello
    ///    schizzo della prima «vista», che E' il foglio.
    ///
    /// ── IL TRATTEGGIO E' IL CONTROLLO CHE CONTA ─────────────────────────
    ///
    /// Il rischio vero di questa operazione non e' che fallisca: e' che
    /// **riesca nel posto sbagliato**. Una linea di taglio caduta fuori dal
    /// materiale produce una vista formalmente valida, che passa ogni
    /// conteggio, e non mostra niente. E' la stessa specie del diedro: nessuna
    /// misura geometrica la rivela.
    ///
    /// Quindi dopo si conta la campitura, e **se e' zero l'operazione
    /// fallisce** invece di consegnare una vista vuota che sembra riuscita.
    /// *Una sezione senza tratteggio e' una linea di taglio caduta fuori dal
    /// pezzo.*
    ///
    /// **E IL CONTATORE NON E' QUELLO CHE LA VOCE PRESCRIVEVA.** GT4 diceva
    /// `GetSolidHatchCount() > 0`; misurato, quel numero e' **zero su una
    /// sezione vera**, prima e dopo il ricalcolo. Si legge
    /// `GetFaceHatchCount()`, che su una sezione buona vale 1. L'altro si
    /// riporta lo stesso nella risposta, perche' valga da avviso a chi
    /// ripercorresse la stessa strada.
    ///
    /// ── E IL CONTO DELLE VISTE, NON IL VALORE DI RITORNO ────────────────
    ///
    /// `CreateSectionViewAt5` puo' rispondere un oggetto senza aver fatto
    /// niente. Si contano le viste prima e dopo: quello e' il riscontro, il
    /// valore di ritorno e' una promessa.
    /// </summary>
    public partial class OperazioniSw
    {
        private Esito SezioneInTavola(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito(
                    "Il documento attivo non e' una tavola: una vista di sezione sta " +
                    "su un foglio di tavola. Crea prima la tavola con «crea_disegno».");

            // ── LA VISTA DA SEZIONARE ───────────────────────────────────
            //
            // La PRIMA «vista» del foglio e' il foglio stesso (tipo 1), e si
            // salta. Chi la prendesse per una vista sezionerebbe il foglio.
            string chiesta = VistaChiestaPerNome(disegno, Str(a, "vista", null));
            IView bersaglio = null;
            var nomiViste = new List<string>();
            int vistePrima = 0;
            try
            {
                var v = disegno.GetFirstView() as IView;
                while (v != null && vistePrima < 200)
                {
                    vistePrima++;
                    if (v.Type != (int)swDrawingViewTypes_e.swDrawingSheet)
                    {
                        string nome = "";
                        try { nome = v.GetName2() ?? ""; } catch { }
                        nomiViste.Add(nome);
                        if (bersaglio == null &&
                            (chiesta == null ||
                             string.Equals(nome, chiesta, StringComparison.OrdinalIgnoreCase)))
                            bersaglio = v;
                    }
                    v = v.GetNextView() as IView;
                }
            }
            catch (Exception ex)
            {
                return Fallito("Non riesco a scorrere le viste del foglio: " + ex.Message);
            }

            if (bersaglio == null)
                return Fallito(chiesta == null
                    ? "Sul foglio non c'e' nessuna vista da sezionare: mettici prima " +
                      "le viste con «viste_standard»."
                    : "Non trovo la vista «" + chiesta + "». Sul foglio ci sono: " +
                      (nomiViste.Count == 0 ? "nessuna vista" : string.Join(", ", nomiViste)) + ".");

            string nomeBersaglio = "";
            try { nomeBersaglio = bersaglio.GetName2() ?? ""; } catch { }

            var sagoma = bersaglio.GetOutline() as double[];
            if (sagoma == null || sagoma.Length < 4)
                return Fallito(
                    "Non riesco a leggere l'ingombro della vista «" + nomeBersaglio +
                    "», e senza quello non so dove tracciare la linea di taglio: una " +
                    "linea che sfiora la vista da' una sezione vuota senza dire niente.");

            // ── DOVE TAGLIARE ───────────────────────────────────────────
            //
            // Di serie: in mezzo alla vista. `a_mm` sposta la linea, in
            // millimetri di FOGLIO — non del modello: sul foglio la vista e'
            // in scala, e mescolare le due misure e' il modo piu' rapido di
            // tagliare dove non si voleva.
            string verso = (Str(a, "verso", "verticale") ?? "verticale").Trim().ToLowerInvariant();
            if (verso != "verticale" && verso != "orizzontale")
                return Fallito(
                    "«verso» dice come corre la linea di taglio e vale «verticale» o " +
                    "«orizzontale»: ho letto «" + verso + "».");

            double scostoM = Num(a, "a_mm", 0) / 1000.0;
            string etichetta = Str(a, "etichetta", "A");
            if (string.IsNullOrWhiteSpace(etichetta)) etichetta = "A";
            double profonditaM = Num(a, "profondita_mm", 0) / 1000.0;
            // DUE «A-A» SULLA STESSA TAVOLA (voce Z-C13): si rifiuta prima.
            string doppia = LetteraGiaUsata(disegno, etichetta);
            if (doppia != null) return Fallito(doppia);

            double xMin = sagoma[0], yMin = sagoma[1], xMax = sagoma[2], yMax = sagoma[3];
            double largo = xMax - xMin, alto = yMax - yMin;

            // SI ESCE DI UN DECIMO OLTRE I BORDI. Una linea che finisce
            // esattamente sul bordo e' un caso limite gratuito, e il caso
            // limite di questa API e' una sezione vuota che non lo dice.
            double x1, y1, x2, y2;
            if (verso == "verticale")
            {
                double x = (xMin + xMax) / 2.0 + scostoM;
                if (x <= xMin || x >= xMax)
                    return Fallito(
                        "Con «a_mm» = " + Num(a, "a_mm", 0) + " la linea di taglio cade " +
                        "FUORI dalla vista, e una sezione tagliata fuori dal materiale " +
                        "esce vuota senza dare nessun errore. La vista e' larga " +
                        Math.Round(largo * 1000, 1) + " mm sul foglio, quindi «a_mm» sta " +
                        "fra " + Math.Round(-largo * 500, 1) + " e " +
                        Math.Round(largo * 500, 1) + ".");
                x1 = x; y1 = yMin - alto * 0.1;
                x2 = x; y2 = yMax + alto * 0.1;
            }
            else
            {
                double y = (yMin + yMax) / 2.0 + scostoM;
                if (y <= yMin || y >= yMax)
                    return Fallito(
                        "Con «a_mm» = " + Num(a, "a_mm", 0) + " la linea di taglio cade " +
                        "FUORI dalla vista, e una sezione tagliata fuori dal materiale " +
                        "esce vuota senza dare nessun errore. La vista e' alta " +
                        Math.Round(alto * 1000, 1) + " mm sul foglio, quindi «a_mm» sta " +
                        "fra " + Math.Round(-alto * 500, 1) + " e " +
                        Math.Round(alto * 500, 1) + ".");
                x1 = xMin - largo * 0.1; y1 = y;
                x2 = xMax + largo * 0.1; y2 = y;
            }

            // ── LA LINEA, SULLO SCHIZZO DEL FOGLIO ──────────────────────
            try { disegno.EditSheet(); } catch { }

            // The segments of the sheet sketch before the line, so that a line
            // left behind by a refusal can be removed and the removal checked
            // (Z-C21.8, finding M41).
            int segmentsBefore = SheetSketchSegmentCount(disegno);
            ISketchSegment linea = null;
            try
            {
                modello.ClearSelection2(true);
                linea = modello.SketchManager.CreateLine(x1, y1, 0.0, x2, y2, 0.0) as ISketchSegment;
            }
            catch (Exception ex)
            {
                return Fallito("Non sono riuscito a tracciare la linea di taglio: " + ex.Message);
            }
            if (linea == null)
                return Fallito(
                    "«CreateLine» non ha prodotto nessun segmento sul foglio, e senza " +
                    "la linea non c'e' niente da selezionare: «CreateSectionViewAt5» " +
                    "non prende il taglio in firma, lo prende dalla selezione.");

            // LO SCHIZZO SI CHIUDE PRIMA. Vedi il docstring: con uno schizzo
            // aperto le API che creano viste si comportano diversamente.
            try { modello.SketchManager.InsertSketch(true); } catch { }

            bool presa = false;
            string selectError = null;
            try
            {
                modello.ClearSelection2(true);
                presa = linea.Select4(false, null);
            }
            catch (Exception ex) { selectError = ex.Message; }
            if (!presa)
                return Fallito(
                    (selectError != null
                        ? "I could not select the cutting line (" + selectError + ")"
                        : "The cutting line is there but does not let itself be selected") +
                    ", and CreateSectionViewAt5 takes the cut from the selection: without it, it would do " +
                    "something other than what you asked" +
                    SheetLineRemovedOrNot(RemoveSheetSketchSegment(modello, disegno, linea, segmentsBefore), "cutting line"));

            // ── LA SEZIONE, A FIANCO ────────────────────────────────────
            double xSezione, ySezione;
            if (verso == "verticale")
            {
                xSezione = xMax + largo * 0.8;
                ySezione = (yMin + yMax) / 2.0;
            }
            else
            {
                xSezione = (xMin + xMax) / 2.0;
                ySezione = yMin - alto * 0.8;
            }

            object nata = null;
            string esplosa = null;
            try
            {
                nata = disegno.CreateSectionViewAt5(
                    xSezione, ySezione, 0.0, etichetta, 0, null, profonditaM);
            }
            catch (Exception ex) { esplosa = ex.Message; }

            // ── IL RISCONTRO: QUANTE VISTE CI SONO ADESSO ───────────────
            int visteDopo = 0;
            IView ultima = null;
            try
            {
                var w = disegno.GetFirstView() as IView;
                while (w != null && visteDopo < 200)
                {
                    visteDopo++;
                    ultima = w;
                    w = w.GetNextView() as IView;
                }
            }
            catch { }

            if (visteDopo <= vistePrima)
                return Fallito(
                    "No view more: the section was NOT born (there were " + vistePrima +
                    ", there are " + visteDopo + "). CreateSectionViewAt5 " +
                    (esplosa != null ? "failed: " + esplosa
                                     : nata == null ? "returned no object"
                                                    : "returned an object") +
                    SheetLineRemovedOrNot(RemoveSheetSketchSegment(modello, disegno, linea, segmentsBefore), "cutting line"));

            // THE SECTION IS THE VIEW CreateSectionViewAt5 RETURNS (Z-C21.8, finding
            // M41). CreateSectionViewAt5.html, Return Value: «Section view». Until
            // library 0.144.0 the bridge read the LAST view of the chain instead,
            // which is the new one only as long as SOLIDWORKS appends it there. The
            // count of views stays the check that something was born; the last
            // view is only the fallback when the call returned no view, and the
            // answer says which one was read.
            IView sectionView = nata as IView;
            string viewReadFrom = "returned by CreateSectionViewAt5";
            if (sectionView == null)
            {
                sectionView = ultima;
                viewReadFrom = "last view of the sheet (CreateSectionViewAt5 returned no view)";
            }

            string nomeSezione = "";
            int tipoSezione = -1;
            int tratteggi = -1;
            try { nomeSezione = sectionView.GetName2() ?? ""; } catch { }
            try { tipoSezione = sectionView.Type; } catch { }
            // ── IL CONTATORE GIUSTO E' `GetFaceHatchCount` ──────────────
            //
            // La voce GT4 prescriveva `GetSolidHatchCount() > 0`, e il
            // criterio era giusto: una sezione senza campitura e' un taglio
            // caduto fuori dal materiale. **Il contatore no.**
            //
            // Misurato il 23/9/2026 su una sezione che attraversa una piastra
            // 120x80x20 piena, con la sonda `Controlli\SezioneInTavola`:
            //
            //     tratteggio PRIMA del ricalcolo: ritorno=0  out=0
            //     tratteggio DOPO  il ricalcolo: ritorno=0  out=0
            //     tratteggio di FACCIA:          1
            //     tipo vista: 2 (swDrawingSectionView), GetSection(): un oggetto
            //
            // `GetSolidHatchCount` risponde **zero su una sezione vera**,
            // prima e dopo il ricalcolo. Si legge `GetFaceHatchCount`.
            //
            // COME SI E' SCOPERTO, e vale la pena tenerlo: l'operazione si e'
            // rifiutata al primo uso vero, nel primo caso di banco scritto per
            // lei. La guardia aveva ragione a esistere e torto sul numero, e
            // le due cose si distinguono solo misurando. *Un criterio giusto
            // appoggiato al contatore sbagliato e' una guardia che dice
            // sempre di no, ed e' indistinguibile da una che funziona finche'
            // non la si usa.*
            try { tratteggi = sectionView.GetFaceHatchCount(); } catch { }

            // L'altro si legge lo stesso e si riporta: vale zero su una
            // sezione buona, e averlo accanto e' cio' che impedisce a
            // qualcuno di riprovarci fra sei mesi.
            int solidi = -1;
            try { int q; sectionView.GetSolidHatchCount(out q); solidi = q; } catch { }

            // ── IL TRATTEGGIO, E QUI SI FALLISCE ────────────────────────
            //
            // Vedi il docstring. Una vista di sezione senza tratteggio e' una
            // linea di taglio caduta fuori dal materiale: formalmente valida,
            // conta come vista, e non mostra niente.
            // LA VISTA VUOTA SI TOGLIE (revisione del 26/9/2026): restando, la sua
            // lettera «A» faceva rifiutare il rimedio che il messaggio stesso indica.
            // Only a view that was not on the sheet before is deleted: with no
            // view returned, the name comes from the last view of the chain, and
            // a name read before the call is a view of the user (Z-C21.8, M41).
            bool vuotaTolta = tratteggi == 0 && !nomiViste.Contains(nomeSezione) && RemoveNewView(modello, nomeSezione);
            if (tratteggi == 0)
                return Fallito(
                    (vuotaTolta ? "Ho tolto la vista «" + nomeSezione + "»: " : "") +
                    "La vista «" + nomeSezione + "» e' nata ma NON HA TRATTEGGIO " +
                    "(campiture di faccia: 0), e " +
                    "una sezione senza tratteggio e' una linea di taglio caduta fuori " +
                    "dal materiale: sul foglio c'e' una vista che sembra buona e non " +
                    "mostra niente. La linea l'ho tracciata " +
                    (verso == "verticale" ? "in verticale a x=" : "in orizzontale a y=") +
                    Math.Round((verso == "verticale" ? x1 : y1) * 1000, 2) + " mm di foglio. " +
                    "Sposta il taglio con «a_mm».");

            // LA LETTERA EFFETTIVA, riletta dalla vista (Z-C13).
            string letteraLetta = LetteraDellaVista(sectionView);
            var fuori = new Dictionary<string, object>
            {
                ["etichetta_riletta"] = letteraLetta,
                ["view_read_from"] = viewReadFrom,
                ["vista_sezionata"] = nomeBersaglio,
                ["viste_prima"] = vistePrima,
                ["viste_dopo"] = visteDopo,
                ["vista_sezione"] = nomeSezione,
                ["tipo_vista"] = tipoSezione,
                // IL NUMERO CRUDO NON SI LASCIA SOLO: chi legge non ha la
                // tabella di `swDrawingViewTypes_e` sottomano.
                ["e_una_sezione"] = tipoSezione == (int)swDrawingViewTypes_e.swDrawingSectionView,
                ["etichetta"] = etichetta,
                ["verso"] = verso,
                ["linea_taglio_mm"] = new object[]
                {
                    Math.Round(x1 * 1000, 2), Math.Round(y1 * 1000, 2),
                    Math.Round(x2 * 1000, 2), Math.Round(y2 * 1000, 2),
                },
                // DUE NUMERI, E IL SECONDO E' ZERO DI SUO. `tratteggi` e' il
                // conto delle campiture di faccia, che e' quello che dice se
                // il taglio ha incontrato materiale; `tratteggi_solidi` e'
                // l'altro contatore, che su una sezione vale zero — sta qui
                // per non far riprovare a nessuno la strada gia' misurata.
                ["tratteggi"] = tratteggi,
                ["tratteggi_solidi"] = solidi,
                ["profondita_mm"] = Math.Round(profonditaM * 1000, 2),
                ["cosa_non_guarda"] =
                    "SE QUELLA E' LA SEZIONE CHE SERVIVA. So dirti che il taglio ha " +
                    "incontrato materiale (il tratteggio c'e'), non che passa per la " +
                    "cavita' che volevi mostrare: una sezione tagliata dieci millimetri " +
                    "piu' in la' e' altrettanto valida e non fa vedere il foro. " +
                    "Scegli «a_mm» guardando il pezzo, non a caso.\n" +
                    "Non guardo se la vista di sezione si accavalla con le altre o col " +
                    "cartiglio, ne' se la scala del foglio la fa uscire dal formato. " +
                    "Non guardo le frecce e l'etichetta sulla vista di partenza: " +
                    "SOLIDWORKS le mette, io non le rileggo. E non decido se una " +
                    "sezione serva: quello dipende da cosa il pezzo ha dentro.",
            };

            _log("[agente] sezione «" + nomeSezione + "» da «" + nomeBersaglio + "»: " +
                 tratteggi + " tratteggi, viste " + vistePrima + " -> " + visteDopo);
            return SenzaFeature(fuori);
        }

        /// <summary>
        /// How many segments the sketch of the sheet has (the first «view» of
        /// the drawing IS the sheet, see the header), or -1 when it cannot be
        /// read. The cutting line of sezione_in_tavola and the circle of
        /// dettaglio_in_tavola are drawn there.
        /// </summary>
        private static int SheetSketchSegmentCount(IDrawingDoc drawing)
        {
            try
            {
                var sketch = (drawing.GetFirstView() as IView)?.GetSketch() as ISketch;
                if (sketch == null) return -1;
                var segments = sketch.GetSketchSegments() as object[];
                return segments == null ? 0 : segments.Length;   // ZERO-E-ZERO: null is a sheet sketch with no segment
            }
            catch { return -1; }
        }

        /// <summary>
        /// Removes a segment this operation drew on the sheet sketch and then did
        /// not use (Z-C21.8, finding M41: the cutting line or the detail circle
        /// was left on the sheet by every refusal after it was drawn). True only
        /// when the sheet sketch, counted again, is back to
        /// <paramref name="segmentsBefore"/>; DeleteSelection2 and not
        /// EditDelete, which can open a confirmation window.
        /// </summary>
        private static bool RemoveSheetSketchSegment(IModelDoc2 model, IDrawingDoc drawing, ISketchSegment segment, int segmentsBefore)
        {
            if (segment == null || segmentsBefore < 0) return false;
            // Already gone (taken along by a view removed first): the count says so.
            if (SheetSketchSegmentCount(drawing) == segmentsBefore) return true;
            bool deleted = false;
            try
            {
                model.ClearSelection2(true);
                if (segment.Select4(false, null))
                    deleted = model.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                model.ClearSelection2(true);
            }
            catch { }
            if (!deleted) return false;
            int now = SheetSketchSegmentCount(drawing);
            return now >= 0 && now == segmentsBefore;
        }

        /// <summary>The end of a refusal that removed the segment it drew, or tried to.</summary>
        private static string SheetLineRemovedOrNot(bool removed, string what)
        {
            return removed
                ? ". I removed the " + what + " I had drawn."
                : ". I could not confirm that the " + what + " I drew is gone: look at the sheet and, if it is " +
                  "there, remove it — " + Frasi.CosiSiRitira + ".";
        }
    }
}
