using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// UNA NOTA DI TESTO SU UNA TAVOLA (voce E2-bis).
    ///
    /// ── PERCHE' ESISTE ──────────────────────────────────────────────────
    ///
    /// Una tavola d'officina non e' fatta solo di geometria e quote: porta le
    /// note generali — «smussare tutti gli spigoli vivi», «zincare dopo
    /// saldatura», «tolleranze non indicate ISO 2768-mK». Fino a oggi il ponte
    /// non sapeva scriverne nemmeno una.
    ///
    /// ── LE DUE API PER LA STESSA COSA, E UNA E' MUTA ────────────────────
    ///
    /// Misurato il 18/9/2026 fuori processo, con il controllo positivo verde
    /// nello stesso giro (tre viste create, risposta `True`):
    ///
    ///     IModelDoc2.InsertNote      -> __ComObject    annotazioni 67 -> 68
    ///     IDrawingDoc.InsertNewNote3 -> Void           annotazioni 68 -> 68
    ///
    /// `InsertNewNote3` torna `Void`: e' un bottone, non un'API — non ha modo
    /// di dire che non ha fatto niente, e infatti non lo dice. Si usa
    /// `InsertNote`, che risponde. **E' scritto qui perche' nessuno riprovi
    /// l'altra.** (Il TODO la chiamava `InsertNewNote2`: su questa
    /// installazione il tipo espone la `3`, ed e' muta uguale.)
    ///
    /// ── LA TRAPPOLA, ED E' NELLO STRUMENTO CON CUI SI GUARDA ────────────
    ///
    /// `InsertNote` scrive **sul FOGLIO**, non dentro una vista. Ma
    /// `GetNoteCount()` sta su `IView`, e le viste vere di una tavola
    /// cominciano DOPO la prima — perche' la prima «view» **e' il foglio**.
    ///
    /// Chi contasse solo le viste vere leggerebbe `0 -> 0` con la nota
    /// regolarmente sul foglio, e scriverebbe «anche le note non nascono:
    /// e' un blocco sistemico». Quel falso negativo avrebbe chiuso per
    /// sbaglio quattro voci di questo TODO (rugosita', datum, saldature,
    /// GD&T), tutte sbloccate proprio dal fatto che una nota nasce.
    ///
    /// Qui si contano tutti e due, e tutti e due escono nella risposta:
    /// `note_foglio_*` e `note_viste_*`. *Due numeri, due cose diverse, e
    /// sommarli farebbe sembrare piena una tavola vuota.*
    ///
    /// ── DOVE FINISCE, E PERCHE' SI CONTROLLA PRIMA ──────────────────────
    ///
    /// Le coordinate sono del foglio, in metri. Una nota messa fuori dal
    /// foglio **esiste, si conta, e sulla carta non c'e'**: stesso difetto
    /// delle quote fuori foglio, e non da' nessun errore. Quindi si legge la
    /// misura del foglio da `GetProperties2` e si rifiuta prima di scrivere.
    ///
    /// ── COSA QUESTA OPERAZIONE NON PUO' SAPERE ──────────────────────────
    ///
    /// Una nota e' testo libero. «Materiale S355» su un pezzo le cui
    /// proprieta' dicono S275 passa ogni conteggio e ogni rilettura: il testo
    /// torna identico a com'e' stato chiesto, ed e' comunque sbagliato. Qui si
    /// garantisce che la nota **c'e' e dice quello che hai scritto** — non che
    /// quello che hai scritto sia vero.
    /// </summary>
    public partial class SwOperations
    {
        private Outcome DrawingNote(IModelDoc2 model, Dictionary<string, object> a)
        {
            var drawing = model as IDrawingDoc;
            if (drawing == null)
                return Failed(
                    "Il documento attivo non e' una tavola: una nota di disegno sta " +
                    "su un foglio di tavola. Crea prima la tavola con «create_drawing».");

            string text = Str(a, "text", null);
            if (string.IsNullOrWhiteSpace(text))
                return Failed(
                    "«text» e' obbligatorio: una nota vuota e' un'annotazione che " +
                    "occupa un posto sul foglio e non dice niente.");

            // ── IL FOGLIO, E QUANTO E' GRANDE ───────────────────────────
            ISheet sheet = null;
            try { sheet = drawing.GetCurrentSheet() as ISheet; } catch { }
            if (sheet == null)
                return Failed("Non riesco a prendere il foglio attivo della tavola.");

            string sheetName = "";
            try { sheetName = sheet.GetName() ?? ""; } catch { }

            double[] measurements = SheetMeasurements(sheet);
            if (measurements == null || measurements.Length < 2)
                return Failed(
                    "Non riesco a leggere la misura del foglio, e senza quella non " +
                    "posso dire se la nota finirebbe dentro o fuori — e una nota " +
                    "fuori foglio esiste, si conta, e sulla carta non c'e'.");

            double wideMm = measurements[0], topMm = measurements[1];

            // Di serie in alto a sinistra, dentro il margine: e' dove stanno le
            // note generali su una tavola meccanica. Non al centro, che e' dove
            // sta il disegno.
            double xMm = Num(a, "x_mm", 20);
            double yMm = Num(a, "y_mm", topMm - 20);

            if (xMm < 0 || yMm < 0 || xMm > wideMm || yMm > topMm)
                return Failed(
                    "La nota finirebbe fuori dal foglio: hai chiesto x=" + xMm +
                    " y=" + yMm + " mm su un foglio " + wideMm + " x " + topMm +
                    " mm. Fuori dal foglio la nota c'e' davvero e si conta, ma " +
                    "stampando non compare — e nessun errore lo direbbe.");

            int sheetBefore = CountSheetNotes(drawing);
            int viewsBefore = CountViewNotes(drawing);
            if (sheetBefore < 0 || viewsBefore < 0)
                return Failed(
                    "Non riesco a contare le note della tavola prima di cominciare: senza quel " +
                    "numero non saprei dirti se la nota e' nata. Non ho toccato niente.");

            object born = null;
            try
            {
                model.ClearSelection2(true);
                born = model.InsertNote(text);
            }
            catch (Exception ex)
            {
                return Failed("«InsertNote» e' andata in errore: " + ex.Message);
            }

            // ── DOVE METTERLA ───────────────────────────────────────────
            //
            // Prima si posiziona, poi si rilegge: la posizione la si chiede
            // all'oggetto DOPO averla impostata, perche' e' l'unico modo di
            // sapere se e' stata accettata.
            var note = born as INote;
            bool moved = false;
            try
            {
                if (note != null)
                {
                    note.SetTextPoint(xMm / 1000.0, yMm / 1000.0, 0);
                    moved = true;
                }
            }
            catch { }

            try { model.EditRebuild3(); } catch { }
            try { model.ClearSelection2(true); } catch { }

            int sheetAfter = CountSheetNotes(drawing);
            int viewsAfter = CountViewNotes(drawing);
            if (sheetAfter < 0 || viewsAfter < 0)
                return Failed(
                    "Ho scritto la nota, ma poi non riesco a ricontare le note della tavola: " +
                    "non so dirti se e' nata ne' dove. Guarda la tavola prima di andare avanti.");
            int put = (sheetAfter - sheetBefore) + (viewsAfter - viewsBefore);

            // NON SI GUARDA SE `InsertNote` HA RISPOSTO: si guarda se la nota
            // c'e'. E' la regola di casa — il valore che torna da chi ha fatto
            // la cosa non e' la misura della cosa.
            if (put <= 0)
                return Failed(
                    "Nessuna nota e' nata (sul foglio erano " + sheetBefore + ", sono " +
                    sheetAfter + "; nelle viste erano " + viewsBefore + ", sono " +
                    viewsAfter + "). `InsertNote` " +
                    (born == null ? "non ha risposto un oggetto" : "ha risposto un oggetto") +
                    ", e il conteggio non e' salito lo stesso.");

            // ── IL TESTO SI RILEGGE ─────────────────────────────────────
            string reread = null;
            try { if (note != null) reread = note.GetText(); } catch { }

            double[] whereMm = WhereItEnded(note);

            var outside = new Dictionary<string, object>
            {
                ["sheet"] = sheetName,
                ["sheet_mm"] = new object[] { wideMm, topMm },
                // DUE CONTEGGI, MAI SOMMATI IN UNO SOLO: vedi il docstring.
                ["notes_sheet_before"] = sheetBefore,
                ["notes_sheet_after"] = sheetAfter,
                ["notes_views_before"] = viewsBefore,
                ["notes_views_after"] = viewsAfter,
                ["notes_placed"] = put,
                // IL TESTO RILETTO DA SOLIDWORKS, non quello che credevo di
                // aver chiesto: una nota che compare con un testo diverso si
                // legge come una nota giusta finche' nessuno la confronta.
                ["text_requested"] = text,
                ["text_reread"] = reread,
                ["text_matches"] = reread != null &&
                                     string.Equals(reread.Trim(), text.Trim(), StringComparison.Ordinal),
                ["where_mm"] = whereMm,
                ["position_set"] = moved,
                ["what_it_does_not_check"] =
                    "Che quello che c'e' scritto sia VERO. Una nota e' testo libero: " +
                    "«materiale S355» su un pezzo che nelle proprieta' dice S275 passa " +
                    "ogni conteggio e ogni rilettura. Qui si garantisce che la nota c'e' " +
                    "e dice quello che hai scritto, non che sia giusto scriverlo. " +
                    "E non si guarda se si accavalla col disegno o col cartiglio.",
            };

            _log("[agente] nota sul foglio «" + sheetName + "»: " +
                 (reread ?? text) + (whereMm == null ? "" : " a " + whereMm[0] + "," + whereMm[1] + " mm"));
            return WithoutFeature(outside);
        }

        /// <summary>
        /// Dove la nota e' finita davvero, in millimetri di foglio, oppure
        /// `null` se non si e' potuto leggere.
        ///
        /// TRE STRADE, PERCHE' LA PRIMA NON RISPONDE. Misurato il 22/9/2026
        /// sulla tavola vera: la nota nasce, il testo si rilegge, e
        /// `GetTextPoint2()` torna un oggetto il cui `ArrayData` e' nullo —
        /// posizione letta `null` con la nota al suo posto. Un falso negativo
        /// che, senza le altre due strade, avrebbe fatto scrivere «la
        /// posizione non si rilegge su questa versione» come se fosse una
        /// proprieta' di SOLIDWORKS invece che della prima chiamata provata.
        ///
        /// `null` qui vuol dire «non l'ho potuto leggere», MAI «zero»: una
        /// posizione zero e' l'angolo del foglio, ed e' un posto vero.
        /// </summary>
        private static double[] WhereItEnded(INote note)
        {
            if (note == null) return null;

            // 1. L'array diretto, che e' la strada piu' corta.
            try
            {
                var arr = note.GetTextPoint() as double[];
                if (arr != null && arr.Length >= 2) return InMm(arr);
            }
            catch { }

            // 2. Il punto matematico, e il suo `ArrayData`.
            try
            {
                var point = note.GetTextPoint2() as IMathPoint;
                var arr = point == null ? null : point.ArrayData as double[];
                if (arr != null && arr.Length >= 2) return InMm(arr);
            }
            catch { }

            // 3. L'annotazione sotto la nota: `IAnnotation.GetPosition()`.
            try
            {
                var ann = note.GetAnnotation() as IAnnotation;
                var arr = ann == null ? null : ann.GetPosition() as double[];
                if (arr != null && arr.Length >= 2) return InMm(arr);
            }
            catch { }

            return null;
        }

        private static double[] InMm(double[] meters)
        {
            return new[]
            {
                Math.Round(meters[0] * 1000, 2),
                Math.Round(meters[1] * 1000, 2),
            };
        }

        /// <summary>
        /// Le note del FOGLIO. La prima «view» di una tavola e' il foglio, ed
        /// e' li' che `InsertNote` scrive: contare da `GetNextView()` in poi —
        /// cioe' le viste vere — risponderebbe zero con la nota al suo posto.
        ///
        /// **-1 quando non si e' potuto leggere** (voce Z-C8). Con lo zero, una
        /// lettura fallita DOPO dava il rifiuto «nessuna nota e' nata» con la
        /// nota al suo posto. Una tavola ha sempre il foglio: se non c'e', non
        /// e' uno zero, e' una lettura andata male.
        /// </summary>
        private int CountSheetNotes(IDrawingDoc drawing)
        {
            try
            {
                var sheet = drawing.GetFirstView() as IView;
                if (sheet == null) return -1;
                return sheet.GetNoteCount();
            }
            catch { return -1; }
        }

        /// <summary>
        /// Le note delle viste vere, foglio escluso. Serve come CONTRALTARE:
        /// se salisse questa invece di quella del foglio, la nota sarebbe
        /// finita dentro una vista e si sposterebbe insieme a lei.
        ///
        /// -1 quando anche una sola vista non risponde: la somma delle altre
        /// sarebbe un numero piu' basso del vero, detto come se fosse vero.
        /// </summary>
        private int CountViewNotes(IDrawingDoc drawing)
        {
            int n = 0;
            try
            {
                var sheet = drawing.GetFirstView() as IView;
                if (sheet == null) return -1;
                var v = sheet.GetNextView() as IView;
                while (v != null)
                {
                    n += v.GetNoteCount();
                    v = v.GetNextView() as IView;
                }
            }
            catch { return -1; }
            return n;
        }
    }
}
