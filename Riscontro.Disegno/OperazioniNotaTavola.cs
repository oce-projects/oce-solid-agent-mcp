using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace Riscontro.Disegno
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
    /// cominciano DOPO la prima — perche' la prima «vista» **e' il foglio**.
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
    public partial class OperazioniSw
    {
        private Esito NotaInTavola(IModelDoc2 modello, Dictionary<string, object> a)
        {
            var disegno = modello as IDrawingDoc;
            if (disegno == null)
                return Fallito(
                    "Il documento attivo non e' una tavola: una nota di disegno sta " +
                    "su un foglio di tavola. Crea prima la tavola con «crea_disegno».");

            string testo = Str(a, "testo", null);
            if (string.IsNullOrWhiteSpace(testo))
                return Fallito(
                    "«testo» e' obbligatorio: una nota vuota e' un'annotazione che " +
                    "occupa un posto sul foglio e non dice niente.");

            // ── IL FOGLIO, E QUANTO E' GRANDE ───────────────────────────
            ISheet foglio = null;
            try { foglio = disegno.GetCurrentSheet() as ISheet; } catch { }
            if (foglio == null)
                return Fallito("Non riesco a prendere il foglio attivo della tavola.");

            string nomeFoglio = "";
            try { nomeFoglio = foglio.GetName() ?? ""; } catch { }

            double[] misure = MisureFoglio(foglio);
            if (misure == null || misure.Length < 2)
                return Fallito(
                    "Non riesco a leggere la misura del foglio, e senza quella non " +
                    "posso dire se la nota finirebbe dentro o fuori — e una nota " +
                    "fuori foglio esiste, si conta, e sulla carta non c'e'.");

            double largoMm = misure[0], altoMm = misure[1];

            // Di serie in alto a sinistra, dentro il margine: e' dove stanno le
            // note generali su una tavola meccanica. Non al centro, che e' dove
            // sta il disegno.
            double xMm = Num(a, "x_mm", 20);
            double yMm = Num(a, "y_mm", altoMm - 20);

            if (xMm < 0 || yMm < 0 || xMm > largoMm || yMm > altoMm)
                return Fallito(
                    "La nota finirebbe fuori dal foglio: hai chiesto x=" + xMm +
                    " y=" + yMm + " mm su un foglio " + largoMm + " x " + altoMm +
                    " mm. Fuori dal foglio la nota c'e' davvero e si conta, ma " +
                    "stampando non compare — e nessun errore lo direbbe.");

            int foglioPrima = ContaNoteDelFoglio(disegno);
            int vistePrima = ContaNoteDelleViste(disegno);

            object nata = null;
            try
            {
                modello.ClearSelection2(true);
                nata = modello.InsertNote(testo);
            }
            catch (Exception ex)
            {
                return Fallito("«InsertNote» e' andata in errore: " + ex.Message);
            }

            // ── DOVE METTERLA ───────────────────────────────────────────
            //
            // Prima si posiziona, poi si rilegge: la posizione la si chiede
            // all'oggetto DOPO averla impostata, perche' e' l'unico modo di
            // sapere se e' stata accettata.
            var nota = nata as INote;
            bool spostata = false;
            try
            {
                if (nota != null)
                {
                    nota.SetTextPoint(xMm / 1000.0, yMm / 1000.0, 0);
                    spostata = true;
                }
            }
            catch { }

            try { modello.EditRebuild3(); } catch { }
            try { modello.ClearSelection2(true); } catch { }

            int foglioDopo = ContaNoteDelFoglio(disegno);
            int visteDopo = ContaNoteDelleViste(disegno);
            int messe = (foglioDopo - foglioPrima) + (visteDopo - vistePrima);

            // NON SI GUARDA SE `InsertNote` HA RISPOSTO: si guarda se la nota
            // c'e'. E' la regola di casa — il valore che torna da chi ha fatto
            // la cosa non e' la misura della cosa.
            if (messe <= 0)
                return Fallito(
                    "Nessuna nota e' nata (sul foglio erano " + foglioPrima + ", sono " +
                    foglioDopo + "; nelle viste erano " + vistePrima + ", sono " +
                    visteDopo + "). `InsertNote` " +
                    (nata == null ? "non ha risposto un oggetto" : "ha risposto un oggetto") +
                    ", e il conteggio non e' salito lo stesso.");

            // ── IL TESTO SI RILEGGE ─────────────────────────────────────
            string riletto = null;
            try { if (nota != null) riletto = nota.GetText(); } catch { }

            double[] doveMm = DovEFinita(nota);

            var fuori = new Dictionary<string, object>
            {
                ["foglio"] = nomeFoglio,
                ["foglio_mm"] = new object[] { largoMm, altoMm },
                // DUE CONTEGGI, MAI SOMMATI IN UNO SOLO: vedi il docstring.
                ["note_foglio_prima"] = foglioPrima,
                ["note_foglio_dopo"] = foglioDopo,
                ["note_viste_prima"] = vistePrima,
                ["note_viste_dopo"] = visteDopo,
                ["note_messe"] = messe,
                // IL TESTO RILETTO DA SOLIDWORKS, non quello che credevo di
                // aver chiesto: una nota che compare con un testo diverso si
                // legge come una nota giusta finche' nessuno la confronta.
                ["testo_chiesto"] = testo,
                ["testo_riletto"] = riletto,
                ["testo_combacia"] = riletto != null &&
                                     string.Equals(riletto.Trim(), testo.Trim(), StringComparison.Ordinal),
                ["dove_mm"] = doveMm,
                ["posizione_impostata"] = spostata,
                ["cosa_non_guarda"] =
                    "Che quello che c'e' scritto sia VERO. Una nota e' testo libero: " +
                    "«materiale S355» su un pezzo che nelle proprieta' dice S275 passa " +
                    "ogni conteggio e ogni rilettura. Qui si garantisce che la nota c'e' " +
                    "e dice quello che hai scritto, non che sia giusto scriverlo. " +
                    "E non si guarda se si accavalla col disegno o col cartiglio.",
            };

            _log("[agente] nota sul foglio «" + nomeFoglio + "»: " +
                 (riletto ?? testo) + (doveMm == null ? "" : " a " + doveMm[0] + "," + doveMm[1] + " mm"));
            return SenzaFeature(fuori);
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
        private static double[] DovEFinita(INote nota)
        {
            if (nota == null) return null;

            // 1. L'array diretto, che e' la strada piu' corta.
            try
            {
                var arr = nota.GetTextPoint() as double[];
                if (arr != null && arr.Length >= 2) return InMm(arr);
            }
            catch { }

            // 2. Il punto matematico, e il suo `ArrayData`.
            try
            {
                var punto = nota.GetTextPoint2() as IMathPoint;
                var arr = punto == null ? null : punto.ArrayData as double[];
                if (arr != null && arr.Length >= 2) return InMm(arr);
            }
            catch { }

            // 3. L'annotazione sotto la nota: `IAnnotation.GetPosition()`.
            try
            {
                var ann = nota.GetAnnotation() as IAnnotation;
                var arr = ann == null ? null : ann.GetPosition() as double[];
                if (arr != null && arr.Length >= 2) return InMm(arr);
            }
            catch { }

            return null;
        }

        private static double[] InMm(double[] metri)
        {
            return new[]
            {
                Math.Round(metri[0] * 1000, 2),
                Math.Round(metri[1] * 1000, 2),
            };
        }

        /// <summary>
        /// Le note del FOGLIO. La prima «vista» di una tavola e' il foglio, ed
        /// e' li' che `InsertNote` scrive: contare da `GetNextView()` in poi —
        /// cioe' le viste vere — risponderebbe zero con la nota al suo posto.
        /// </summary>
        private int ContaNoteDelFoglio(IDrawingDoc disegno)
        {
            try
            {
                var foglio = disegno.GetFirstView() as IView;
                if (foglio == null) return 0;
                return foglio.GetNoteCount();
            }
            catch { return 0; }
        }

        /// <summary>
        /// Le note delle viste vere, foglio escluso. Serve come CONTRALTARE:
        /// se salisse questa invece di quella del foglio, la nota sarebbe
        /// finita dentro una vista e si sposterebbe insieme a lei.
        /// </summary>
        private int ContaNoteDelleViste(IDrawingDoc disegno)
        {
            int n = 0;
            try
            {
                var foglio = disegno.GetFirstView() as IView;
                if (foglio == null) return 0;
                var v = foglio.GetNextView() as IView;
                while (v != null)
                {
                    try { n += v.GetNoteCount(); } catch { }
                    v = v.GetNextView() as IView;
                }
            }
            catch { }
            return n;
        }
    }
}
